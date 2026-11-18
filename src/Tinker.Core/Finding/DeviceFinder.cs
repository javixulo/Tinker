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
public sealed partial class DeviceFinder(IInventory inventory, ILogger<DeviceFinder> logger)
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
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        activity?.SetTag("tinker.ok", result.Ok);
        activity?.SetTag("tinker.matches", result.Devices.Count);
        if (result.Error is { } failure)
        {
            activity?.SetTag("tinker.error_codes", failure.Code);
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
            LogFound(matches.Count, given.Count);
            return FindDevicesResult.Found(matches);
        }

        var message = all.Count == 0 ? Messages.NoDevices : Messages.NoMatch(string.Join(", ", given.Select(p => p.Listed)));
        LogEmpty(all.Count, given.Count);
        return FindDevicesResult.Empty(message);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Find devices: {Matches} found with {Properties} properties given")]
    private partial void LogFound(int matches, int properties);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Find devices: nothing found ({Registered} devices registered, {Properties} properties given)")]
    private partial void LogEmpty(int registered, int properties);

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for devices failed")]
    private partial void LogFailed(Exception error);
}
