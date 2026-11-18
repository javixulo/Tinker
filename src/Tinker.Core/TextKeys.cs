using System.Globalization;
using System.Text;

namespace Tinker.Core;

public static class TextKeys
{
    /// <summary>
    /// What identifies a brand or a model: trimmed, each run of whitespace collapsed to one space, case ignored.
    /// Nothing else (no hyphens, no accents). Technical spec, section 7.
    /// </summary>
    public static string Normalize(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>
    /// What <c>find_devices</c> compares: <see cref="Normalize"/> and then without accents ("Habitación" and
    /// "habitacion" are the same). Lowercase is the cheapest form to compare in.
    /// </summary>
    public static string Fold(string? text)
    {
        var decomposed = Normalize(text).Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(c);
            }
        }

        return plain.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);
}
