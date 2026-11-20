using System.Diagnostics;

namespace Tinker.Core;

/// <summary>
/// The one place that names what Tinker records in its spans (technical spec, section 3). Names are lowercase and
/// dot-separated, from the OpenTelemetry conventions when one exists (<c>error.type</c>) and <c>&lt;entity&gt;.id</c> for domain ids.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "Tinker";

    public const string DeviceId = "device.id";
    public const string DeviceBrand = "device.brand";
    public const string DeviceModel = "device.model";
    public const string SearchQuery = "search.query";
    public const string ErrorType = "error.type";
    public const string Matches = "matches.count";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Records the error codes of an outcome that was not a success. A success has no <c>error.type</c>.</summary>
    public static void SetErrorTypes(this Activity? activity, IEnumerable<string> codes)
    {
        var list = codes.Distinct().ToArray();
        if (list.Length > 0)
        {
            activity?.SetTag(ErrorType, list);
        }
    }

    /// <summary>Marks a step as failed because of something unexpected.</summary>
    public static void Fail(this Activity? activity, Exception error)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag(ErrorType, error.GetType().FullName);
    }
}
