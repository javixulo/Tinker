using System.Text.RegularExpressions;

namespace Tinker.Core.Manuals;

public enum ManualRejection
{
    None,
    OutsideFolder,
    ContainsDotDot,
}

/// <param name="Rejection">Why the path is not allowed, or <see cref="ManualRejection.None"/>.</param>
/// <param name="RelativePath">The manual as stored: relative to the real manuals folder, in POSIX form.</param>
/// <param name="FullPath">The real file, inside the manuals folder. Only set when the path is allowed.</param>
public sealed record ManualPathResult(ManualRejection Rejection, string? RelativePath, string? FullPath)
{
    public bool Allowed => Rejection == ManualRejection.None;
}

/// <summary>
/// The only place that builds a manual path from client input (technical spec, section 5). The path is judged by its
/// text first (absolute, `~`, encoded sequences, `..`) and then by where it ends up once links are followed in every
/// component. It never looks at whether the file exists, so the answer does not reveal what exists outside the folder.
/// </summary>
public sealed partial class ManualPathResolver(string manualsPath)
{
    private const int MaxLinkHops = 40;
    private static readonly char[] Separators = ['/', '\\'];

    private readonly string _manualsPath = Path.GetFullPath(manualsPath);

    public ManualPathResult Resolve(string manual)
    {
        if (LooksOutside(manual))
        {
            return Rejected(ManualRejection.OutsideFolder);
        }

        var parts = manual.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Contains(".."))
        {
            return Rejected(ManualRejection.ContainsDotDot);
        }

        var root = RealPath(_manualsPath);
        var real = root is null ? null : Walk(root, parts);
        if (root is null || real is null || real == root || !real.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Rejected(ManualRejection.OutsideFolder);
        }

        var relative = Path.GetRelativePath(root, real).Replace(Path.DirectorySeparatorChar, '/');
        return new ManualPathResult(ManualRejection.None, relative, real);
    }

    private static ManualPathResult Rejected(ManualRejection reason) => new(reason, null, null);

    private static bool LooksOutside(string manual) =>
        manual.Contains('\0')
        || manual.StartsWith('~')
        || Path.IsPathRooted(manual)
        || manual.StartsWith('/') || manual.StartsWith('\\')
        || DriveLetter().IsMatch(manual)
        || EncodedSequence().IsMatch(manual);

    /// <summary>The real path of a directory: every link in it is followed. Null when links loop.</summary>
    private static string? RealPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)!;
        return Walk(root, fullPath[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Follows <paramref name="parts"/> from <paramref name="start"/> (already a real path), resolving a link in
    /// every component, not only in the last one. .NET has no <c>realpath</c>. A link whose target does not exist is
    /// still followed, so it cannot be used to probe. Null when there are too many links.
    /// </summary>
    private static string? Walk(string start, IEnumerable<string> parts)
    {
        var pending = new Stack<string>(parts.Reverse());
        var current = start;
        var hops = 0;
        while (pending.Count > 0)
        {
            var part = pending.Pop();
            if (part is "." or "")
            {
                continue;
            }

            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Combine(current, part);
            var target = new FileInfo(next).LinkTarget;
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                return null;
            }

            var targetRoot = Path.GetPathRoot(target) ?? "";
            if (targetRoot.Length > 0)
            {
                current = targetRoot;
            }

            foreach (var targetPart in target[targetRoot.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                pending.Push(targetPart);
            }
        }

        return current;
    }

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DriveLetter();

    [GeneratedRegex("%[0-9A-Fa-f]{2}")]
    private static partial Regex EncodedSequence();
}
