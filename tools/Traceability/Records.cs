namespace Traceability;

/// <summary>One row of a criteria table in the specification.</summary>
internal sealed record Criterion(string Id, string UseCase, string Name, int Line);

/// <summary>One test marker: a criterion ID found in a test file.</summary>
internal sealed record Mark(string Id, string Path, int Line);

/// <summary>A problem found in a file, with the place where it was found.</summary>
internal sealed record Finding(string Path, int Line, string Message);

/// <summary>What was read from the specification.</summary>
internal sealed record SpecReading(List<Criterion> Criteria, Dictionary<string, int> UseCaseLines, List<Finding> Errors);

/// <summary>What was read from the tests.</summary>
internal sealed record MarkReading(List<Mark> Marks, List<Finding> Errors);
