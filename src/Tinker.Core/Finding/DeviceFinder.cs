using System.Diagnostics;
using System.Globalization;
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
        var given = Given(criteria);
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

    /// <summary>The properties that were given: a blank text, an empty list and a missing value count as not given.</summary>
    private static List<Property> Given(Device d)
    {
        var given = new List<Property>();
        if (d.DeviceId is { } id)
        {
            given.Add(new("device_id", id.ToString(CultureInfo.InvariantCulture), x => x.DeviceId == id));
        }

        AddText(given, "brand", d.Brand, x => x.Brand);
        AddText(given, "model", d.Model, x => x.Model);
        AddText(given, "device_type", d.DeviceType, x => x.DeviceType);
        AddText(given, "color", d.Color, x => x.Color);
        AddText(given, "location", d.Location, x => x.Location);
        AddText(given, "invoice", d.Invoice, x => x.Invoice);
        AddText(given, "status", d.Status, x => x.Status);
        AddList(given, "manuals", d.Manuals, x => x.Manuals);
        AddList(given, "urls", d.Urls, x => x.Urls);
        AddDate(given, "purchase_date", d.PurchaseDate, x => x.PurchaseDate);
        AddDate(given, "warranty_end_date", d.WarrantyEndDate, x => x.WarrantyEndDate);
        return given;
    }

    private static void AddText(List<Property> given, string name, string? value, Func<Device, string?> own)
    {
        if (!TextKeys.IsBlank(value))
        {
            var wanted = TextKeys.Fold(value);
            given.Add(new(name, value!.Trim(), x => own(x) is { } text && TextKeys.Fold(text) == wanted));
        }
    }

    private static void AddList(List<Property> given, string name, IReadOnlyList<string>? values, Func<Device, IReadOnlyList<string>?> own)
    {
        var wanted = (values ?? []).Where(v => !TextKeys.IsBlank(v)).Select(v => v.Trim()).ToList();
        if (wanted.Count > 0)
        {
            given.Add(new(name, string.Join("; ", wanted), x =>
            {
                var folded = (own(x) ?? []).Select(TextKeys.Fold).ToHashSet();
                return wanted.All(item => folded.Contains(TextKeys.Fold(item)));
            }));
        }
    }

    private static void AddDate(List<Property> given, string name, DateOnly? value, Func<Device, DateOnly?> own)
    {
        if (value is { } date)
        {
            given.Add(new(name, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x => own(x) == date));
        }
    }

    private sealed record Property(string Name, string Value, Func<Device, bool> IsMatchedBy)
    {
        public string Listed => $"{Name}: {Value}";

        public bool Matches(Device device) => IsMatchedBy(device);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Find devices: {Matches} found with {Properties} properties given")]
    private partial void LogFound(int matches, int properties);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Find devices: nothing found ({Registered} devices registered, {Properties} properties given)")]
    private partial void LogEmpty(int registered, int properties);

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for devices failed")]
    private partial void LogFailed(Exception error);
}
