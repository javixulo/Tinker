using System.Globalization;
using System.Text;

namespace Tinker.Core;

public static class TextKeys
{
    /// <summary>
    /// The one way text is compared (technical spec, section 7): trimmed, each run of whitespace collapsed to one
    /// space, lowercase and without accents ("Habitación" and " habitacion " are the same; so are "Philips Hué" and
    /// "philips hue"). It decides when two devices are the same (<c>brand_key</c> and <c>model_key</c>) and what
    /// <c>find_devices</c> matches. Nothing else is ignored (no hyphens).
    /// </summary>
    public static string Fold(string? text)
    {
        var spaced = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var decomposed = spaced.Normalize(NormalizationForm.FormD);
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
