using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Tinker.Core.Manuals;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Registration;

/// <summary>
/// UC-01 and UC-02: registers a device, with its manuals if it has any, or rejects the registration as a whole. The heavy work
/// (reading, chunking and embedding the manuals) happens before the transaction opens, so the write lock is held
/// briefly. The inventory and the manuals index are written in one transaction: a registration is all or nothing.
/// </summary>
public sealed partial class DeviceRegistrar(
    ManualPathResolver resolver,
    IInventory inventory,
    IManualsIndex index,
    ITransactionRunner transactions,
    IManualTextExtractor extractor,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<DeviceRegistrar> logger)
{
    public async Task<RegistrationResult> RegisterAsync(Device? request, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("register_device");
        RegistrationResult result;
        try
        {
            LogRequested(request?.Brand, request?.Model);
            result = await RegisterCoreAsync(request, activity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            // Nothing is left half done: everything written was inside the transaction, which was rolled back.
            LogFailed(error);
            result = RegistrationResult.Failure(new DeviceError(ErrorCodes.Failed, Messages.Failed("Registering the device")));
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddEvent(new ActivityEvent("rolled_back"));
        }

        activity?.SetTag("tinker.ok", result.Ok);
        activity?.SetTag("tinker.error_codes", string.Join(',', result.Errors.Select(e => e.Code)));
        if (result.Device?.DeviceId is { } id)
        {
            activity?.SetTag("tinker.device_id", id);
        }

        return result;
    }

    private async Task<RegistrationResult> RegisterCoreAsync(Device? request, Activity? activity, CancellationToken cancellationToken)
    {
        request ??= new Device();
        var errors = new List<DeviceError>();

        var brand = DeviceNormalizer.Brand(request.Brand);
        var model = DeviceNormalizer.Model(request.Model);
        var requested = (request.Manuals ?? []).Where(m => !TextKeys.IsBlank(m)).Select(m => m.Trim()).ToList();

        var missing = new List<string>();
        if (TextKeys.IsBlank(request.Brand))
        {
            missing.Add("brand");
        }

        if (TextKeys.IsBlank(model))
        {
            missing.Add("model");
        }

        if (missing.Count > 0)
        {
            errors.Add(new DeviceError(ErrorCodes.InvalidFields, Messages.MissingFields(missing)));
        }

        if (!TextKeys.IsBlank(request.Brand) && brand.Length == 0)
        {
            errors.Add(new DeviceError(ErrorCodes.InvalidFields, Messages.BrandWithoutValidCharacters));
        }

        if (!TextKeys.IsBlank(request.Status) && !DeviceStatus.All.Contains(request.Status!.Trim()))
        {
            errors.Add(new DeviceError(ErrorCodes.InvalidFields, Messages.InvalidStatus(request.Status)));
        }

        // Manuals: allowed first, then existing. Several spellings of one file count once.
        var manuals = new List<ManualPathResult>();
        var notFound = new List<string>();
        foreach (var manual in requested)
        {
            var resolved = resolver.Resolve(manual);
            switch (resolved.Rejection)
            {
                case ManualRejection.OutsideFolder:
                    errors.Add(new DeviceError(ErrorCodes.ManualNotAllowed, Messages.ManualOutside(manual)));
                    break;
                case ManualRejection.ContainsDotDot:
                    errors.Add(new DeviceError(ErrorCodes.ManualNotAllowed, Messages.ManualDotDot(manual)));
                    break;
                default:
                    if (!File.Exists(resolved.FullPath))
                    {
                        notFound.Add(manual);
                    }
                    else if (manuals.All(m => m.RelativePath != resolved.RelativePath))
                    {
                        manuals.Add(resolved);
                    }

                    break;
            }
        }

        if (notFound.Count > 0)
        {
            errors.Add(new DeviceError(ErrorCodes.ManualNotFound, Messages.ManualNotFound(notFound)));
        }

        // Reading is the check: a manual that exists but cannot be used is reported with the other reasons.
        var texts = new List<(ManualPathResult Manual, IReadOnlyList<string> Pages)>();
        var unreadable = new List<(string Path, string? Reason)>();
        foreach (var manual in manuals)
        {
            try
            {
                texts.Add((manual, await extractor.ExtractPagesAsync(manual.FullPath!, cancellationToken)));
            }
            catch (ManualUnreadableException error)
            {
                unreadable.Add((manual.RelativePath!, error.Reason));
            }
        }

        if (unreadable.Count > 0)
        {
            errors.Add(new DeviceError(ErrorCodes.ManualUnreadable, Messages.ManualUnreadable(unreadable)));
        }

        if (brand.Length > 0 && model.Length > 0
            && await inventory.FindByBrandAndModelAsync(brand, model, cancellationToken) is { } existing)
        {
            errors.Add(new DeviceError(ErrorCodes.DeviceExists, Messages.DeviceExists(existing.Brand, existing.Model), existing));
        }

        foreach (var manual in manuals)
        {
            if (await inventory.FindByManualAsync(manual.RelativePath!, cancellationToken) is { } owner)
            {
                errors.Add(new DeviceError(ErrorCodes.ManualInUse, Messages.ManualInUse(manual.RelativePath!), owner));
            }
        }

        if (errors.Count > 0)
        {
            return Reject(errors, activity);
        }

        var chunks = await ManualChunks.BuildAsync(texts.Select(t => (t.Manual.RelativePath!, t.Pages)), embeddings, LogNoText, cancellationToken);
        var device = request with
        {
            DeviceId = null,
            Brand = brand,
            Model = model,
            Manuals = manuals.Select(m => m.RelativePath!).ToList(),
            Urls = (request.Urls ?? []).Where(u => !TextKeys.IsBlank(u)).Select(u => u.Trim()).Distinct().ToList(),
            DeviceType = NormalizedOrNull(request.DeviceType, DeviceNormalizer.FirstUpper),
            Color = NormalizedOrNull(request.Color, DeviceNormalizer.FirstUpper),
            Location = NormalizedOrNull(request.Location, DeviceNormalizer.FirstUpper),
            Invoice = NullIfBlank(request.Invoice),
            Status = NullIfBlank(request.Status) ?? DeviceStatus.Active,
        };

        try
        {
            var stored = await transactions.RunAsync(
                async () =>
                {
                    var inserted = await inventory.InsertAsync(device, cancellationToken);
                    await index.AddAsync(inserted.DeviceId!.Value, chunks, cancellationToken);
                    return inserted;
                },
                cancellationToken);
            activity?.AddEvent(new ActivityEvent("committed"));
            LogCompleted(stored.DeviceId, stored.Manuals!.Count, chunks.Count);
            return RegistrationResult.Success(stored);
        }
        catch (InventoryConflictException conflict)
        {
            // Another registration got in between the checks and the insert: the database decided.
            activity?.AddEvent(new ActivityEvent("rolled_back"));
            return Reject(await ConflictErrorsAsync(conflict, device, manuals, cancellationToken), activity);
        }
    }

    private async Task<List<DeviceError>> ConflictErrorsAsync(
        InventoryConflictException conflict, Device device, List<ManualPathResult> manuals, CancellationToken cancellationToken)
    {
        if (conflict.Conflict == Conflict.DeviceExists
            && await inventory.FindByBrandAndModelAsync(device.Brand!, device.Model!, cancellationToken) is { } existing)
        {
            return [new DeviceError(ErrorCodes.DeviceExists, Messages.DeviceExists(existing.Brand, existing.Model), existing)];
        }

        var errors = new List<DeviceError>();
        foreach (var manual in manuals)
        {
            if (await inventory.FindByManualAsync(manual.RelativePath!, cancellationToken) is { } owner)
            {
                errors.Add(new DeviceError(ErrorCodes.ManualInUse, Messages.ManualInUse(manual.RelativePath!), owner));
            }
        }

        // The winner may already be gone again: with no device to show, it is reported as an internal failure.
        return errors.Count > 0 ? errors : throw conflict;
    }

    private RegistrationResult Reject(List<DeviceError> errors, Activity? activity)
    {
        foreach (var error in errors)
        {
            LogRejected(error.Code, error.Message);
        }

        activity?.AddEvent(new ActivityEvent("rejected"));
        return RegistrationResult.Failure([.. errors]);
    }

    private static string? NullIfBlank(string? text) => TextKeys.IsBlank(text) ? null : text!.Trim();

    private static string? NormalizedOrNull(string? text, Func<string?, string> normalize) => TextKeys.IsBlank(text) ? null : normalize(text);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registration requested: {Brand} {Model}")]
    private partial void LogRequested(string? brand, string? model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registration completed: device {DeviceId} with {Manuals} manuals and {Chunks} chunks")]
    private partial void LogCompleted(long? deviceId, int manuals, int chunks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Registration rejected [{Code}]: {Message}")]
    private partial void LogRejected(string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No text could be extracted from the manual {Manual}: nothing to index")]
    private partial void LogNoText(string? manual);

    [LoggerMessage(Level = LogLevel.Error, Message = "Registering the device failed")]
    private partial void LogFailed(Exception error);
}
