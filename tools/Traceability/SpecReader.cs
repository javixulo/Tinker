namespace Traceability;

/// <summary>
/// Reads the criteria tables of a functional spec. A criteria table has the header <c>| ID | Name | ...</c> and sits under
/// a use case heading (<c>#### UC-03 · Title</c>); the first cell of each row is the ID, <c>UC-03.AC1</c>.
/// </summary>
internal static class SpecReader
{
    public static SpecReading Read(string path)
    {
        var shown = path.Replace('\\', '/');
        var criteria = new List<Criterion>();
        var useCaseLines = new Dictionary<string, int>(StringComparer.Ordinal);
        var errors = new List<Finding>();
        var firstLineOf = new Dictionary<string, int>(StringComparer.Ordinal);

        string? useCase = null;
        var inCriteriaTable = false;
        var lines = File.ReadAllLines(path);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();

            var heading = Patterns.UseCaseHeading().Match(line);
            if (heading.Success)
            {
                useCase = heading.Groups["uc"].Value;
                useCaseLines.TryAdd(useCase, lineNumber);
                inCriteriaTable = false;
                continue;
            }

            if (!line.StartsWith('|'))
            {
                inCriteriaTable = false;
                continue;
            }

            var cells = Cells(line);
            if (cells.Count < 2)
            {
                continue;
            }

            if (string.Equals(cells[0], "ID", StringComparison.Ordinal) && string.Equals(cells[1], "Name", StringComparison.Ordinal))
            {
                inCriteriaTable = true;
                continue;
            }

            if (string.Equals(cells[0], "#", StringComparison.Ordinal) && string.Equals(cells[1], "Name", StringComparison.Ordinal))
            {
                errors.Add(new Finding(shown, lineNumber, "A criteria table has a '#' column. Use an 'ID' column with IDs like UC-03.AC1."));
                continue;
            }

            if (!inCriteriaTable || IsSeparator(cells))
            {
                continue;
            }

            var id = cells[0];
            var match = Patterns.CriterionId().Match(id);
            if (!match.Success)
            {
                errors.Add(new Finding(shown, lineNumber, $"'{id}' is not a criterion ID. Expected the form UC-03.AC1."));
                continue;
            }

            var idUseCase = "UC-" + match.Groups["uc"].Value;
            if (!string.Equals(idUseCase, useCase, StringComparison.Ordinal))
            {
                errors.Add(new Finding(shown, lineNumber, $"{id} sits under {useCase ?? "no use case"}: its ID must start with that use case."));
            }

            if (firstLineOf.TryGetValue(id, out var firstLine))
            {
                errors.Add(new Finding(shown, lineNumber, $"{id} is repeated (first at line {firstLine})."));
                continue;
            }

            firstLineOf[id] = lineNumber;
            criteria.Add(new Criterion(id, idUseCase, cells[1], lineNumber));
        }

        return new SpecReading(criteria, useCaseLines, errors);
    }

    private static List<string> Cells(string line)
    {
        var inner = line.Trim('|');
        return inner.Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsSeparator(List<string> cells) =>
        cells.All(cell => cell.Length > 0 && cell.All(ch => ch is '-' or ':'));
}
