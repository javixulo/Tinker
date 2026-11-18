using System.Globalization;
using Tinker.Core.Models;

namespace Tinker.Core.Finding;

/// <summary>
/// How a device is matched by the properties a client gives (functional spec, section 2.3). Shared by
/// <c>find_devices</c> and <c>delete_device</c>, so both always agree on what "the same" means.
/// </summary>
public static class DeviceMatcher
{
    /// <summary>The properties that were given: a blank text, an empty list and a missing value count as not given.</summary>
    public static List<Criterion> Given(Device d)
    {
        var given = new List<Criterion>();
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

    private static void AddText(List<Criterion> given, string name, string? value, Func<Device, string?> own)
    {
        if (!TextKeys.IsBlank(value))
        {
            var wanted = TextKeys.Fold(value);
            given.Add(new(name, value!.Trim(), x => own(x) is { } text && TextKeys.Fold(text) == wanted));
        }
    }

    private static void AddList(List<Criterion> given, string name, IReadOnlyList<string>? values, Func<Device, IReadOnlyList<string>?> own)
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

    private static void AddDate(List<Criterion> given, string name, DateOnly? value, Func<Device, DateOnly?> own)
    {
        if (value is { } date)
        {
            given.Add(new(name, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x => own(x) == date));
        }
    }

    public sealed record Criterion(string Name, string Value, Func<Device, bool> IsMatchedBy)
    {
        public string Listed => $"{Name}: {Value}";

        public bool Matches(Device device) => IsMatchedBy(device);
    }
}
