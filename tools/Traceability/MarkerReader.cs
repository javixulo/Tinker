using System.Text.RegularExpressions;

namespace Traceability;

/// <summary>
/// Reads the criteria that each test says it covers, from <c>[Trait("Criteria", "UC-03.AC1, UC-03.AC2")]</c> in the
/// C# files under a folder. A short form such as <c>"UC-03.AC1,2"</c> means AC1 and AC2 of UC-03. The <c>bin</c> and <c>obj</c> folders are skipped.
/// </summary>
internal static class MarkerReader
{
    private static readonly char[] Separators = [',', ';', ' '];

    public static MarkReading Read(string testsFolder)
    {
        var marks = new List<Mark>();
        var errors = new List<Finding>();

        foreach (var file in SourceFiles(testsFolder))
        {
            var shown = Path.GetRelativePath(Environment.CurrentDirectory, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            foreach (Match match in Patterns.CriteriaTrait().Matches(text))
            {
                var line = 1 + text.AsSpan(0, match.Index).Count('\n');
                var tokens = match.Groups["ids"].Value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length == 0)
                {
                    errors.Add(new Finding(shown, line, "The Criteria trait is empty. List the IDs of the criteria the test covers."));
                    continue;
                }

                // "UC-11.AC1,2,4" is shorthand: a bare number reuses the use case and prefix of the last full ID.
                string? prefix = null;
                foreach (var token in tokens)
                {
                    var id = token;
                    if (prefix is not null && token.All(char.IsAsciiDigit))
                    {
                        id = prefix + token;
                    }

                    if (Patterns.CriterionId().IsMatch(id))
                    {
                        prefix = id[..(id.LastIndexOf("AC", StringComparison.Ordinal) + 2)];
                        marks.Add(new Mark(id, shown, line));
                    }
                    else
                    {
                        errors.Add(new Finding(shown, line, $"'{token}' is not a criterion ID. Expected the form UC-03.AC1."));
                    }
                }
            }
        }

        return new MarkReading(marks, errors);
    }

    private static List<string> SourceFiles(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(folder, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToList();
        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
