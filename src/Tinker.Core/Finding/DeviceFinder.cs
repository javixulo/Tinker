using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinker.Core.Models;
using Tinker.Core.Storage;

namespace Tinker.Core.Finding;

/// <summary>
/// UC-05 to UC-07: finds the devices that match the properties given. A device matches when every given property is
/// equal to its own, comparing the whole value in lowercase and without accents (for a list, when the device has every
/// item given). With nothing given, every device matches.
/// </summary>
public sealed class DeviceFinder(IInventory inventory, ILogger<DeviceFinder> logger)
{
    public async Task<FindDevicesResult> FindAsync(Device? criteria, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("find_devices");
        FindDevicesResult result;
        try
        {
            result = await FindCoreAsync(criteria ?? new Device(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = FindDevicesResult.Failure(new DeviceError(ErrorCodes.Failed, Messages.Failed("Looking for devices")));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.Matches, result.Devices.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    private async Task<FindDevicesResult> FindCoreAsync(Device criteria, CancellationToken cancellationToken)
    {
        var given = DeviceMatcher.Given(criteria);
        var all = await inventory.ListAllAsync(cancellationToken);
        var matches = all.Where(device => given.All(property => property.Matches(device))).ToList();
        if (matches.Count > 0)
        {
            return FindDevicesResult.Found(matches);
        }

        var message = all.Count == 0 ? Messages.NoDevices : Messages.NoMatch(string.Join(", ", given.Select(p => p.Listed)));
        LogEmpty(all.Count, string.Join(", ", given.Select(p => p.Listed)));
        return FindDevicesResult.Empty(message);
    }

    // Plain calls instead of [LoggerMessage]: the dotted names of the shared backend (`device.id`) are not valid C# identifiers, and the analyzer wants PascalCase.
#pragma warning disable CA1848, CA1873, CA1727

    private void LogEmpty(int registered, string criteria) =>
        logger.LogWarning("No device matched {criteria}: {devices.count} devices were registered", criteria, registered);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Looking for devices failed");
#pragma warning restore CA1848, CA1873, CA1727
}
