using System.Text.RegularExpressions;

namespace Traceability;

internal static partial class Patterns
{
    // A use case heading of the spec: "#### UC-03 · Delete a device".
    [GeneratedRegex(@"^#{1,6}\s+(?<uc>UC-[0-9]{2})\b")]
    public static partial Regex UseCaseHeading();

    // The ID of a criterion: UC-03.AC2.
    [GeneratedRegex(@"^UC-(?<uc>[0-9]{2})\.AC[0-9]+$")]
    public static partial Regex CriterionId();

    // The marker of a test: [Trait("Criteria", "UC-03.AC1, UC-03.AC2")].
    [GeneratedRegex("Trait\\(\\s*\"Criteria\"\\s*,\\s*\"(?<ids>[^\"]*)\"\\s*\\)")]
    public static partial Regex CriteriaTrait();
}
