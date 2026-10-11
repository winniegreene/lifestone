namespace Lifestone.Client.Diagnostics;

/// <summary>
/// Where automated runs may write their output (screenshots, reports): one rule for every runner, so a relative path
/// means the same thing whoever launched Godot and from where.
/// </summary>
/// <remarks>
/// <para>A relative path is taken from the repository root, not from the process's working directory (Godot runs in
/// <c>src/Lifestone.Game</c> when started from the editor or an older tools\godot.ps1). The result must not lie inside
/// the source trees (<c>src/</c>, <c>tests/</c>): renders of the user's retail data must never land where a
/// <c>git add</c> picks them up. The gitignored places are <c>docs/screenshots/</c> and <c>cache/</c>.</para>
/// <para>Pure string work (no file system access), so it is testable without the engine.</para>
/// </remarks>
public static class OutputPaths
{
    /// <summary>Repository folders output must never be written into.</summary>
    public static readonly IReadOnlyList<string> ForbiddenRoots = ["src", "tests"];

    /// <summary>
    /// Resolves <paramref name="path"/> against <paramref name="repositoryRoot"/> and checks it.
    /// </summary>
    /// <param name="path">An absolute path, or a path relative to the repository root.</param>
    /// <param name="repositoryRoot">The repository root (absolute).</param>
    /// <param name="fullPath">The absolute path (also set when it is refused, for the message).</param>
    /// <param name="problem">Why the path is refused, or null.</param>
    /// <returns>True when output may be written to <paramref name="fullPath"/>.</returns>
    public static bool TryResolve(string path, string repositoryRoot, out string fullPath, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            problem = "the output path is empty";
            return false;
        }

        try
        {
            var root = Path.GetFullPath(repositoryRoot);
            fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            foreach (var forbidden in ForbiddenRoots)
            {
                var tree = Path.GetFullPath(Path.Combine(root, forbidden));
                if (IsSameOrInside(fullPath, tree))
                {
                    problem = $"'{fullPath}' is inside the repository's {forbidden}/ tree; write output under docs/screenshots/ or cache/ (gitignored)";
                    return false;
                }
            }

            problem = null;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problem = $"'{path}' is not a valid path: {ex.Message}";
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or below it (case-insensitive on Windows).</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return p.Equals(f, comparison)
            || (p.Length > f.Length && p.StartsWith(f, comparison) && (p[f.Length] == Path.DirectorySeparatorChar || p[f.Length] == Path.AltDirectorySeparatorChar));
    }
}
