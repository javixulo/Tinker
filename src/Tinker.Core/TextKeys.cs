namespace Tinker.Core;

public static class TextKeys
{
    /// <summary>
    /// What identifies a brand or a model: trimmed, each run of whitespace collapsed to one space, case ignored.
    /// Nothing else (no hyphens, no accents). Technical spec, section 7.
    /// </summary>
    public static string Normalize(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);
}
