using System.Diagnostics;

namespace Tinker.Core.Models;

/// <summary>
/// Messages of the tool results (technical spec, section 6). They are written for the AI that consumes them: what
/// happened and what to do next. They never give the location of the manuals folder nor say whether a file outside it
/// exists.
/// </summary>
public static class Messages
{
    public static string MissingFields(IEnumerable<string> fields) =>
        $"Missing or blank required fields: {string.Join(", ", fields)}. Ask the user for them and call register_device again.";

    public static string InvalidStatus(string status) =>
        $"Invalid status: '{status}'. It must be one of: {string.Join(", ", DeviceStatus.All)}. Fix it or leave it out and call register_device again.";

    public static string ManualOutside(string path) =>
        $"Manual not allowed: {path} (outside the manuals folder). Use a path relative to the manuals folder.";

    public static string ManualDotDot(string path) =>
        $"Manual not allowed: {path} (its path cannot contain `..`). Use a path relative to the manuals folder.";

    public static string ManualNotFound(IEnumerable<string> paths) =>
        $"Manual not found: {string.Join(", ", paths)}. Check the path with the user.";

    public static string ManualInUse(string path) =>
        $"Manual already belongs to another device: {path}. That device is in the error. Do not register the manual twice.";

    public static string DeviceExists(string? brand, string? model) =>
        $"A device with this brand and model already exists: {brand} {model}. It is in the error. Do not register it again.";

    public static string NoMatch(string listed) =>
        $"No registered device matches ({listed}). Ask the user for other details, or register the device.";

    public const string NoDeleteProperties =
        "No property was given. Give at least one property of the device (for example brand and model) to identify it. Nothing was deleted.";

    public static string DeleteNotFound(string listed) =>
        $"No registered device matches the properties ({listed}). Nothing was deleted. Ask the user for other details, or check the registered devices with find_devices.";

    public static string Ambiguous(int count, string listed) =>
        $"{count} registered devices match the properties ({listed}). Nothing was deleted. Ask the user which one it is, or give more properties.";

    public const string NoDevices = "There are no registered devices. Register one with register_device.";

    public const string MissingDeviceId =
        "`device_id` is missing: it is the id of the device whose manuals are searched. Get it with find_devices.";

    public const string BlankQuery = "`query` is blank: it must be the text to look for in the manuals.";

    public const string InvalidResultCount = "`n_results` must be 1 or more: it is the number of excerpts to return.";

    public static string SearchDeviceNotFound(long deviceId) =>
        $"The device id {deviceId} does not exist. Use find_devices to get a valid device_id.";

    public const string NoExtractableText =
        "The manuals of this device have no extractable text (for example, they are scanned), so nothing can be answered from them. Tell the user.";

    public static string Failed(string operation) =>
        $"{operation} failed because of an internal problem, not because of the request. Nothing was changed. Tell the user instead of retrying. Trace id: {Activity.Current?.TraceId.ToString() ?? "none"}.";
}
