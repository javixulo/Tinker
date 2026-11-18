using System.Globalization;
using System.Text;

namespace Tinker.Core;

/// <summary>
/// The one place that turns the values of a client into the stored format (functional spec, section 1; technical spec,
/// section 7). Plain logic, used by the registration and by the lookups, so they all agree.
/// </summary>
public static class DeviceNormalizer
{
    /// <summary>
    /// Removes the accents and every character that is not a letter, a digit, a space, a hyphen or <c>&amp;</c>, and
    /// squashes the spaces. It does not touch the casing. The result is empty when nothing valid is left.
    /// </summary>
    public static string CleanBrand(string? brand)
    {
        var plain = TextKeys.RemoveAccents(brand ?? "");
        var kept = new StringBuilder(plain.Length);
        foreach (var c in plain)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '-' or '&')
            {
                kept.Append(c);
            }
        }

        return TextKeys.Squash(kept.ToString());
    }

    /// <summary>Cleaned; kept as it is when entirely in uppercase (an acronym), otherwise first letter of each word in uppercase and the rest in lowercase.</summary>
    public static string Brand(string? brand)
    {
        var clean = CleanBrand(brand);
        if (clean.Any(char.IsLetter) && !clean.Any(char.IsLower))
        {
            return clean;
        }

        return string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Capitalize));
    }

    /// <summary>All uppercase; hyphens, slashes and the like are kept.</summary>
    public static string Model(string? model) => TextKeys.Squash(model).ToUpperInvariant();

    /// <summary>First character in uppercase, the rest as given. Used by <c>device_type</c>, <c>color</c> and <c>location</c>.</summary>
    public static string FirstUpper(string? text)
    {
        var squashed = TextKeys.Squash(text);
        return squashed.Length == 0 ? squashed : string.Concat(squashed[..1].ToUpperInvariant(), squashed.AsSpan(1));
    }

    private static string Capitalize(string word) =>
        string.Concat(word[..1].ToUpperInvariant(), word[1..].ToLowerInvariant());
}
