using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinker.Core.Finding;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Removal;

/// <summary>
/// UC-03 and UC-04: deletes the one device that the properties given single out, or fails without changing anything.
/// The device is matched exactly as in <c>find_devices</c>. The inventory and the manuals index are written in one
/// transaction: a deletion is all or nothing. The manual files are never touched.
/// </summary>
public sealed class DeviceRemover(
    IInventory inventory,
    IManualsIndex index,
    ITransactionRunner transactions,
    ILogger<DeviceRemover> logger)
{
    public async Task<DeletionResult> DeleteAsync(Device? criteria, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("delete_device");
        DeletionResult result;
        try
        {
            result = await DeleteCoreAsync(criteria ?? new Device(), activity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            // Everything written was inside the transaction, which was rolled back: the device is still there.
            LogFailed(error);
            result = DeletionResult.Failure(new DeviceError(ErrorCodes.Failed, Messages.Failed("Deleting the device")));
            activity.Fail(error);
            activity?.AddEvent(new ActivityEvent("rolled_back"));
        }

        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        if (result.Device?.DeviceId is { } id)
        {
            activity?.SetTag(Telemetry.DeviceId, id);
        }

        return result;
    }

    private async Task<DeletionResult> DeleteCoreAsync(Device criteria, Activity? activity, CancellationToken cancellationToken)
    {
        var given = DeviceMatcher.Given(criteria);
        if (given.Count == 0)
        {
            return Reject(ErrorCodes.InvalidFields, Messages.NoDeleteProperties, activity);
        }

        var listed = string.Join(", ", given.Select(p => p.Listed));
        var matches = (await inventory.ListAllAsync(cancellationToken)).Where(d => given.All(p => p.Matches(d))).ToList();
        switch (matches.Count)
        {
            case 0:
                return Reject(ErrorCodes.DeviceNotFound, Messages.DeleteNotFound(listed), activity);
            case > 1:
                return Reject(ErrorCodes.AmbiguousDevice, Messages.Ambiguous(matches.Count, listed), activity, devices: matches);
        }

        var device = matches[0];
        var deleted = await transactions.RunAsync(
            async () =>
            {
                var removed = await inventory.DeleteAsync(device.DeviceId!.Value, cancellationToken);
                await index.DeleteByDeviceAsync(device.DeviceId!.Value, cancellationToken);
                return removed;
            },
            cancellationToken);
        if (!deleted)
        {
            // Another request deleted it between the lookup and the delete: there is no device to delete any more.
            return Reject(ErrorCodes.DeviceNotFound, Messages.DeleteNotFound(listed), activity);
        }

        activity?.AddEvent(new ActivityEvent("committed"));
        LogCompleted(device.DeviceId, device.Brand, device.Model);
        return DeletionResult.Success(device);
    }

    private DeletionResult Reject(string code, string message, Activity? activity, IReadOnlyList<Device>? devices = null)
    {
        LogRejected(code, message);
        activity?.AddEvent(new ActivityEvent("rejected"));
        return DeletionResult.Failure(new DeviceError(code, message, Devices: devices));
    }

    // Plain calls instead of [LoggerMessage]: the dotted names of the shared backend (`device.id`) are not valid C# identifiers, and the analyzer wants PascalCase.
#pragma warning disable CA1848, CA1873, CA1727

    private void LogCompleted(long? deviceId, string? brand, string? model) =>
        logger.LogInformation("Device {device.id} deleted: {device.brand} {device.model} and its chunks", deviceId, brand, model);

    private void LogRejected(string code, string message) =>
        logger.LogWarning("Deletion rejected with {error.type}: {error.message}", code, message);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Deleting the device failed");
#pragma warning restore CA1848, CA1873, CA1727
}
