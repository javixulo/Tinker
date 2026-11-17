using System.Diagnostics;

namespace Tinker.Core;

public static class Telemetry
{
    public const string SourceName = "Tinker";

    public static readonly ActivitySource Source = new(SourceName);
}
