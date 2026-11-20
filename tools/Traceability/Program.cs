using Traceability;

const string Usage = """
    Checks that the acceptance criteria of the functional spec and the tests agree.

    Usage: dotnet run --project tools/Traceability -- [--spec <file>] [--tests <folder>] [--strict]

      --spec <file>      Functional spec with the criteria tables (default: docs/functional_spec.md)
      --tests <folder>   Folder with the test sources (default: tests)
      --strict           A criterion with no test is an error, not a warning

    A criterion is a table row whose first cell is an ID such as UC-03.AC1. A test covers it with
    [Trait("Criteria", "UC-03.AC1")] (several IDs separated by commas; "UC-03.AC1,2" is short for AC1 and AC2).

    Errors: a marker with an ID that is not in the spec or is malformed, a malformed, repeated or misplaced ID in the
    spec, a criteria table with a '#' column. Warnings: a criterion with no test, a use case with no criteria.
    Exit code: 0 with no errors, 1 with errors, 2 for a wrong call.
    """;

var specPath = "docs/functional_spec.md";
var testsPath = "tests";
var strict = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--spec" when i + 1 < args.Length:
            specPath = args[++i];
            break;
        case "--tests" when i + 1 < args.Length:
            testsPath = args[++i];
            break;
        case "--strict":
            strict = true;
            break;
        case "--help" or "-h":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (!File.Exists(specPath))
{
    Console.Error.WriteLine($"The spec '{specPath}' does not exist. Run the tool from the repository root or pass --spec.");
    return 2;
}

if (!Directory.Exists(testsPath))
{
    Console.Error.WriteLine($"The tests folder '{testsPath}' does not exist. Run the tool from the repository root or pass --tests.");
    return 2;
}

var spec = SpecReader.Read(specPath);
var tests = MarkerReader.Read(testsPath);

var errors = new List<Finding>(spec.Errors);
errors.AddRange(tests.Errors);
var warnings = new List<Finding>();
var specShown = specPath.Replace('\\', '/');

var known = spec.Criteria.Select(criterion => criterion.Id).ToHashSet(StringComparer.Ordinal);
foreach (var mark in tests.Marks.Where(mark => !known.Contains(mark.Id)))
{
    errors.Add(new Finding(mark.Path, mark.Line, $"{mark.Id} is not a criterion of the spec. Fix the ID, or add the criterion to the spec."));
}

var covered = tests.Marks.Select(mark => mark.Id).ToHashSet(StringComparer.Ordinal);
foreach (var criterion in spec.Criteria.Where(criterion => !covered.Contains(criterion.Id)))
{
    var finding = new Finding(specShown, criterion.Line, $"{criterion.Id} ({criterion.Name}) has no test marked with it.");
    if (strict)
    {
        errors.Add(finding);
    }
    else
    {
        warnings.Add(finding);
    }
}

foreach (var (useCase, line) in spec.UseCaseLines.Where(pair => !spec.Criteria.Any(criterion => string.Equals(criterion.UseCase, pair.Key, StringComparison.Ordinal))))
{
    warnings.Add(new Finding(specShown, line, $"{useCase} has no acceptance criteria."));
}

Print("error", errors);
Print("warning", warnings);

Console.WriteLine();
Console.WriteLine($"{"Use case",-10}{"Criteria",10}{"With a test",13}");
foreach (var group in spec.Criteria.GroupBy(criterion => criterion.UseCase))
{
    Console.WriteLine($"{group.Key,-10}{group.Count(),10}{group.Count(criterion => covered.Contains(criterion.Id)),13}");
}

Console.WriteLine($"{"Total",-10}{spec.Criteria.Count,10}{spec.Criteria.Count(criterion => covered.Contains(criterion.Id)),13}");
Console.WriteLine();
Console.WriteLine($"{errors.Count} error(s), {warnings.Count} warning(s).");

return errors.Count == 0 ? 0 : 1;

static void Print(string kind, List<Finding> findings)
{
    foreach (var finding in findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Line))
    {
        Console.WriteLine($"{finding.Path}:{finding.Line}: {kind}: {finding.Message}");
    }
}
