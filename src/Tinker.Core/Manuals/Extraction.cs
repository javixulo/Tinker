using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Tinker.Core.Manuals;

/// <summary>Reads the text of a manual, page by page.</summary>
public interface IManualTextExtractor
{
    /// <summary>One string per page, in order. A page without text gives an empty string.</summary>
    /// <exception cref="ManualUnreadableException">The file is damaged, protected by a password or not a PDF.</exception>
    Task<IReadOnlyList<string>> ExtractPagesAsync(string fullPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// A manual that exists but cannot be used (technical spec, section 7). It is not a failure of the server: the cause is
/// the file, so the services turn it into a <c>manual_unreadable</c> error.
/// </summary>
public sealed class ManualUnreadableException(string reason, Exception? inner = null)
    : Exception($"The manual cannot be read: {reason}.", inner)
{
    public const string Password = "it is protected by a password";
    public const string NotAPdf = "it is not a PDF";
    public const string Damaged = "it is damaged";

    public string Reason { get; } = reason;
}

public sealed class PdfPigTextExtractor : IManualTextExtractor
{
    public Task<IReadOnlyList<string>> ExtractPagesAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("extract_manual");
        try
        {
            return Extract(fullPath, cancellationToken);
        }
        catch (ManualUnreadableException)
        {
            // The file is at fault, not the server: an error type, but not a failed step.
            activity?.SetTag(Telemetry.ErrorType, "manual_unreadable");
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            activity.Fail(error);
            throw;
        }
    }

    private static Task<IReadOnlyList<string>> Extract(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(fullPath);
            var pages = new List<string>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages.Add(ContentOrderTextExtractor.GetText(page));
            }

            return Task.FromResult<IReadOnlyList<string>>(pages);
        }
        catch (PdfDocumentEncryptedException error)
        {
            throw new ManualUnreadableException(ManualUnreadableException.Password, error);
        }
        catch (PdfDocumentFormatException error)
        {
            throw new ManualUnreadableException(HasPdfHeader(fullPath) ? ManualUnreadableException.Damaged : ManualUnreadableException.NotAPdf, error);
        }
    }

    /// <summary>A PDF starts with <c>%PDF-</c> (the specification allows some bytes before it, within the first 1024).</summary>
    private static bool HasPdfHeader(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var buffer = new byte[1024];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.Latin1.GetString(buffer, 0, read).Contains("%PDF-", StringComparison.Ordinal);
    }
}

/// <summary>
/// Splits the text of one page into chunks that never cut a word or a sentence.
/// The text of a PDF has no paragraphs: the lines are wrapped by the layout, so the chunker first rebuilds the units of
/// the manual (a bullet, a numbered step, an entry such as "Factory Reset - Return Shelly...", a heading with its text)
/// and then packs whole units into chunks.
/// </summary>
public static partial class TextChunker
{
    /// <summary>A chunk is closed when the next unit would take it over this size (in characters).</summary>
    public const int DefaultTarget = 500;

    /// <summary>A chunk never grows over this size, except when a single word is longer than it. A list of steps or bullets may grow past the target, up to this size, so it stays in one chunk.</summary>
    public const int DefaultMax = 1500;

    /// <summary>A chunk smaller than this is joined to its neighbour when both fit in the maximum.</summary>
    public const int MinimumSize = 100;

    public static IReadOnlyList<string> Split(string text, int target = DefaultTarget, int max = DefaultMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, target);

        var pieces = BuildUnits(text).SelectMany(unit => Shrink(unit, target, max)).ToList();
        return JoinSmall(Pack(pieces, target, max), max);
    }

    /// <summary>Rebuilds the units of the manual from the lines of the page.</summary>
    private static List<string> BuildUnits(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).ToList();
        var units = new List<StringBuilder>();
        StringBuilder? current = null;
        var glue = false; // the next line belongs to the current unit: it follows a heading or a line that ends with ':'

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                current = null;
                glue = false;
                continue;
            }

            var next = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0);
            var heading = IsHeading(line, next);
            if (current is null || (!glue && (heading || StartsUnit(line))))
            {
                current = new StringBuilder(line);
                units.Add(current);
            }
            else if (glue)
            {
                current.Append('\n').Append(line);
            }
            else
            {
                AppendWrapped(current, line);
            }

            glue = heading || current.ToString().EndsWith(':');
        }

        return units.Select(unit => unit.ToString()).ToList();
    }

    /// <summary>Appends a line that continues the previous one: it undoes the hyphen of the layout ("sin-" + "gle") and does not add a space after a slash.</summary>
    private static void AppendWrapped(StringBuilder current, string line)
    {
        if (current.Length >= 2 && current[^1] == '-' && char.IsLetter(current[^2]))
        {
            // "sin-" + "gle" is one word; "Wi-" + "Fi" keeps its hyphen
            if (!char.IsUpper(line[0]))
            {
                current.Length--;
            }
        }
        else if (current[^1] != '/')
        {
            current.Append(' ');
        }

        current.Append(line);
    }

    /// <summary>A short line without final punctuation, followed by text that starts a sentence and is clearly longer: "Settings", "Button Type", "Step 1".</summary>
    private static bool IsHeading(string line, string? next) =>
        next is not null
        && line.Length < 35
        && char.IsLetterOrDigit(line[0])
        && !char.IsLower(line[0])
        && !EndsLikeASentence().IsMatch(line)
        && (char.IsLetterOrDigit(next[0]) || next[0] is '•' or '⚠')
        && next.Length > line.Length * 3 / 2;

    private static bool StartsUnit(string line) => IsListItem(line) || line[0] == '⚠' || Entry().IsMatch(line);

    /// <summary>A lead-in glued to its first step ("Installation:\n1. Switch off") ends in a step.</summary>
    private static string LastLine(string unit) => unit[(unit.LastIndexOf('\n') + 1)..];

    private static bool IsListItem(string unit) => unit[0] == '•' || NumberedStep().IsMatch(unit);

    /// <summary>A unit longer than the maximum is cut by sentences, and a sentence longer than the maximum by words, in pieces of the target size.</summary>
    private static IEnumerable<string> Shrink(string unit, int target, int max)
    {
        if (unit.Length <= max)
        {
            yield return unit;
            yield break;
        }

        foreach (var sentence in SentenceEnd().Split(unit))
        {
            if (sentence.Length <= max)
            {
                yield return sentence;
                continue;
            }

            var piece = new StringBuilder();
            foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (piece.Length > 0 && piece.Length + 1 + word.Length > target)
                {
                    yield return piece.ToString();
                    piece.Clear();
                }

                piece.Append(piece.Length > 0 ? " " : "").Append(word);
            }

            if (piece.Length > 0)
            {
                yield return piece.ToString();
            }
        }
    }

    private static List<string> Pack(List<string> pieces, int target, int max)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        var size = 0;
        foreach (var piece in pieces)
        {
            var total = size + (current.Count > 0 ? 1 : 0) + piece.Length;
            var continuesList = current.Count > 0
                && IsListItem(piece)
                && (IsListItem(LastLine(current[^1])) || current[^1].EndsWith(':'));
            if (current.Count > 0 && total > target && !(continuesList && total <= max))
            {
                chunks.Add(string.Join('\n', current));
                current.Clear();
                size = 0;
                total = piece.Length;
            }

            current.Add(piece);
            size = total;
        }

        if (current.Count > 0)
        {
            chunks.Add(string.Join('\n', current));
        }

        return chunks;
    }

    private static List<string> JoinSmall(List<string> chunks, int max)
    {
        var joined = new List<string>();
        foreach (var chunk in chunks)
        {
            if (joined.Count > 0
                && (chunk.Length < MinimumSize || joined[^1].Length < MinimumSize)
                && joined[^1].Length + 1 + chunk.Length <= max)
            {
                joined[^1] += "\n" + chunk;
            }
            else
            {
                joined.Add(chunk);
            }
        }

        return joined;
    }

    [GeneratedRegex(@"[.,;:)]$")]
    private static partial Regex EndsLikeASentence();

    [GeneratedRegex(@"^\d{1,3}[.)] ")]
    private static partial Regex NumberedStep();

    /// <summary>An entry of a settings list: "Factory Reset - Return Shelly to its factory default settings."</summary>
    [GeneratedRegex(@"^\p{Lu}[\w/ ’'\-]{1,45} [-–] \p{L}")]
    private static partial Regex Entry();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=\p{Lu})")]
    private static partial Regex SentenceEnd();
}
