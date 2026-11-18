using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Updating;

/// <summary>
/// UC-10 to UC-12: edits a registered device, its data, its manuals or both, or rejects the update as a whole. The
/// manuals given are compared with the current ones: only the new ones are checked, read and indexed, the removed ones
/// lose their chunks and the kept ones are left alone. The heavy work happens before the transaction opens.
/// </summary>
public sealed partial class DeviceUpdater(
    ManualPathResolver resolver,
    IInventory inventory,
    IManualsIndex index,
    ITransactionRunner transactions,
    IManualTextExtractor extractor,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<DeviceUpdater> logger)
{
    public async Task<UpdateResult> UpdateAsync(long? deviceId, DeviceChanges? changes, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("update_device");
        activity?.SetTag("tinker.device_id", deviceId);
        UpdateResult result;
        try
        {
            LogRequested(deviceId);
            result = await UpdateCoreAsync(deviceId, changes, activity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            // Everything written was inside the transaction, which was rolled back.
            LogFailed(error);
            result = UpdateResult.Failure(new DeviceError(ErrorCodes.Failed, Messages.Failed("Updating the device")));
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddEvent(new ActivityEvent("rolled_back"));
        }

        activity?.SetTag("tinker.ok", result.Ok);
        activity?.SetTag("tinker.error_codes", string.Join(',', result.Errors.Select(e => e.Code)));
        return result;
    }

    private async Task<UpdateResult> UpdateCoreAsync(long? deviceId, DeviceChanges? changes, Activity? activity, CancellationToken cancellationToken)
    {
        changes ??= new DeviceChanges();
        var problems = Validate(deviceId, changes);
        if (problems.Count > 0)
        {
            return Reject([new DeviceError(ErrorCodes.InvalidFields, string.Join(' ', problems))], activity);
        }

        var id = deviceId!.Value;
        var current = (await inventory.ListAllAsync(cancellationToken)).FirstOrDefault(d => d.DeviceId == id);
        if (current is null)
        {
            return Reject([new DeviceError(ErrorCodes.DeviceNotFound, Messages.UpdateDeviceNotFound(id))], activity);
        }

        var errors = new List<DeviceError>();
        var updated = Apply(current, changes);

        // Manuals: only the ones that are new for the device are checked and read.
        var removed = new List<string>();
        var added = new List<ManualPathResult>();
        if (changes.Manuals is not null)
        {
            var resolved = new List<ManualPathResult>();
            foreach (var manual in changes.Manuals.Where(m => !TextKeys.IsBlank(m)).Select(m => m.Trim()))
            {
                var path = resolver.Resolve(manual);
                switch (path.Rejection)
                {
                    case ManualRejection.OutsideFolder:
                        errors.Add(new DeviceError(ErrorCodes.ManualNotAllowed, Messages.ManualOutside(manual)));
                        break;
                    case ManualRejection.ContainsDotDot:
                        errors.Add(new DeviceError(ErrorCodes.ManualNotAllowed, Messages.ManualDotDot(manual)));
                        break;
                    default:
                        if (resolved.All(m => m.RelativePath != path.RelativePath))
                        {
                            resolved.Add(path);
                        }

                        break;
                }
            }

            var kept = (current.Manuals ?? []).ToHashSet(StringComparer.Ordinal);
            added = resolved.Where(m => !kept.Contains(m.RelativePath!)).ToList();
            var wanted = resolved.Select(m => m.RelativePath!).ToHashSet(StringComparer.Ordinal);
            removed = kept.Where(m => !wanted.Contains(m)).ToList();
            updated = updated with { Manuals = resolved.Select(m => m.RelativePath!).ToList() };
        }

        var notFound = added.Where(m => !File.Exists(m.FullPath)).Select(m => m.RelativePath!).ToList();
        if (notFound.Count > 0)
        {
            errors.Add(new DeviceError(ErrorCodes.ManualNotFound, Messages.ManualNotFound(notFound)));
        }

        var texts = new List<(string Path, IReadOnlyList<string> Pages)>();
        var unreadable = new List<(string Path, string? Reason)>();
        foreach (var manual in added.Where(m => !notFound.Contains(m.RelativePath!)))
        {
            try
            {
                texts.Add((manual.RelativePath!, await extractor.ExtractPagesAsync(manual.FullPath!, cancellationToken)));
            }
            catch (ManualUnreadableException error)
            {
                unreadable.Add((manual.RelativePath!, error.Reason));
            }
        }

        if (unreadable.Count > 0)
        {
            errors.Add(new DeviceError(ErrorCodes.ManualUnreadable, Messages.ManualUnreadable(unreadable, update: true)));
        }

        if (await inventory.FindByBrandAndModelAsync(updated.Brand!, updated.Model!, cancellationToken) is { } other && other.DeviceId != id)
        {
            errors.Add(new DeviceError(ErrorCodes.DeviceExists, Messages.UpdateDeviceExists(other.Brand, other.Model), other));
        }

        foreach (var manual in added)
        {
            if (await inventory.FindByManualAsync(manual.RelativePath!, cancellationToken) is { } owner && owner.DeviceId != id)
            {
                errors.Add(new DeviceError(ErrorCodes.ManualInUse, Messages.UpdateManualInUse(manual.RelativePath!), owner));
            }
        }

        if (errors.Count > 0)
        {
            return Reject(errors, activity);
        }

        if (Same(current, updated))
        {
            LogUnchanged(id);
            return UpdateResult.Success(current);
        }

        var chunks = await ManualChunks.BuildAsync(texts, embeddings, LogNoText, cancellationToken);
        try
        {
            var stored = await transactions.RunAsync(
                async () =>
                {
                    var written = await inventory.UpdateAsync(updated, cancellationToken);
                    if (written is null)
                    {
                        return null;
                    }

                    foreach (var manual in removed)
                    {
                        await index.DeleteByManualAsync(id, manual, cancellationToken);
                    }

                    await index.AddAsync(id, chunks, cancellationToken);
                    return written;
                },
                cancellationToken);
            if (stored is null)
            {
                // Deleted between the lookup and the write.
                return Reject([new DeviceError(ErrorCodes.DeviceNotFound, Messages.UpdateDeviceNotFound(id))], activity);
            }

            activity?.AddEvent(new ActivityEvent("committed"));
            LogCompleted(id, added.Count, removed.Count, chunks.Count);
            return UpdateResult.Success(stored);
        }
        catch (InventoryConflictException conflict)
        {
            // Another request got in between the checks and the write: the database decided.
            activity?.AddEvent(new ActivityEvent("rolled_back"));
            return Reject(await ConflictErrorsAsync(conflict, id, updated, added, cancellationToken), activity);
        }
    }

    /// <summary>The reasons why the input itself is not valid. They do not depend on the device.</summary>
    private static List<string> Validate(long? deviceId, DeviceChanges changes)
    {
        var problems = new List<string>();
        if (deviceId is null)
        {
            problems.Add(Messages.UpdateMissingDeviceId);
        }

        if (changes.DeviceId is not null)
        {
            problems.Add(Messages.DeviceIdInChanges);
        }

        var given = changes with { DeviceId = null };
        if (changes.DeviceId is null && given == new DeviceChanges())
        {
            problems.Add(Messages.NothingToChange);
        }

        if (changes.Brand is not null && DeviceNormalizer.Brand(changes.Brand).Length == 0)
        {
            problems.Add(Messages.CannotEmpty("brand"));
        }

        if (changes.Model is not null && TextKeys.IsBlank(changes.Model))
        {
            problems.Add(Messages.CannotEmpty("model"));
        }

        if (changes.Status is not null)
        {
            if (TextKeys.IsBlank(changes.Status))
            {
                problems.Add(Messages.CannotEmpty("status"));
            }
            else if (!DeviceStatus.All.Contains(changes.Status.Trim()))
            {
                problems.Add(Messages.InvalidStatus(changes.Status, "update_device"));
            }
        }

        foreach (var (field, value) in new[] { ("purchase_date", changes.PurchaseDate), ("warranty_end_date", changes.WarrantyEndDate) })
        {
            if (!TextKeys.IsBlank(value) && ParseDate(value) is null)
            {
                problems.Add(Messages.InvalidDate(field));
            }
        }

        return problems;
    }

    /// <summary>The device with the changes applied and normalized. The manuals are handled apart.</summary>
    private static Device Apply(Device current, DeviceChanges changes) => current with
    {
        Brand = changes.Brand is null ? current.Brand : DeviceNormalizer.Brand(changes.Brand),
        Model = changes.Model is null ? current.Model : DeviceNormalizer.Model(changes.Model),
        DeviceType = Text(changes.DeviceType, current.DeviceType, DeviceNormalizer.FirstUpper),
        Color = Text(changes.Color, current.Color, DeviceNormalizer.FirstUpper),
        Location = Text(changes.Location, current.Location, DeviceNormalizer.FirstUpper),
        Invoice = Text(changes.Invoice, current.Invoice, text => text.Trim()),
        Urls = changes.Urls is null ? current.Urls : changes.Urls.Where(u => !TextKeys.IsBlank(u)).Select(u => u.Trim()).Distinct().ToList(),
        PurchaseDate = changes.PurchaseDate is null ? current.PurchaseDate : ParseDate(changes.PurchaseDate),
        WarrantyEndDate = changes.WarrantyEndDate is null ? current.WarrantyEndDate : ParseDate(changes.WarrantyEndDate),
        Status = changes.Status is null ? current.Status : changes.Status.Trim(),
    };

    private static string? Text(string? given, string? current, Func<string, string> normalize) =>
        given is null ? current : TextKeys.IsBlank(given) ? null : normalize(given);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static bool Same(Device a, Device b) => System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);

    private async Task<List<DeviceError>> ConflictErrorsAsync(
        InventoryConflictException conflict, long id, Device updated, List<ManualPathResult> added, CancellationToken cancellationToken)
    {
        if (conflict.Conflict == Conflict.DeviceExists
            && await inventory.FindByBrandAndModelAsync(updated.Brand!, updated.Model!, cancellationToken) is { } existing && existing.DeviceId != id)
        {
            return [new DeviceError(ErrorCodes.DeviceExists, Messages.UpdateDeviceExists(existing.Brand, existing.Model), existing)];
        }

        var errors = new List<DeviceError>();
        foreach (var manual in added)
        {
            if (await inventory.FindByManualAsync(manual.RelativePath!, cancellationToken) is { } owner && owner.DeviceId != id)
            {
                errors.Add(new DeviceError(ErrorCodes.ManualInUse, Messages.UpdateManualInUse(manual.RelativePath!), owner));
            }
        }

        // The winner may already be gone again: with no device to show, it is reported as an internal failure.
        return errors.Count > 0 ? errors : throw conflict;
    }

    private UpdateResult Reject(List<DeviceError> errors, Activity? activity)
    {
        foreach (var error in errors)
        {
            LogRejected(error.Code, error.Message);
        }

        activity?.AddEvent(new ActivityEvent("rejected"));
        return UpdateResult.Failure([.. errors]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update requested: device {DeviceId}")]
    private partial void LogRequested(long? deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update completed: device {DeviceId}, {Added} manuals added, {Removed} removed, {Chunks} chunks added")]
    private partial void LogCompleted(long deviceId, int added, int removed, int chunks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update completed: device {DeviceId} already had these values, nothing was written")]
    private partial void LogUnchanged(long deviceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update rejected [{Code}]: {Message}")]
    private partial void LogRejected(string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No text could be extracted from the manual {Manual}: nothing to index")]
    private partial void LogNoText(string? manual);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the device failed")]
    private partial void LogFailed(Exception error);
}
