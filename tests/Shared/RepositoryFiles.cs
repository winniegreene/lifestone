namespace Lifestone.Client.Tests;

/// <summary>
/// The repository checkout the tests were built from (for tests that read the repo's own source files), and the main
/// checkout around it. The one walker every test project shares (tests/Shared; the namespace is the one it had in
/// Lifestone.Client.Tests, its first home).
/// </summary>
internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootPath = new(FindRoot);
    private static readonly Lazy<string> MainCheckoutPath = new(() => FindMainCheckout(Root));

    /// <summary>The folder holding Lifestone.slnx, found by walking up from the test assembly.</summary>
    public static string Root => RootPath.Value;

    /// <summary>
    /// The main checkout: the outermost folder holding Lifestone.slnx at or above <see cref="Root"/>. For a git worktree
    /// created inside a checkout (<c>&lt;checkout&gt;\.claude\worktrees\&lt;name&gt;</c>) it is the enclosing checkout, the
    /// one whose <c>server\runtime</c> runs the shared localhost backend and whose <c>cache</c> holds the cross-worktree
    /// loopback lock; otherwise it is <see cref="Root"/>.
    /// </summary>
    public static string MainCheckout => MainCheckoutPath.Value;

    /// <summary>A path below the repository root.</summary>
    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>The outermost folder holding Lifestone.slnx at or above <paramref name="start"/> (itself when none is above it).</summary>
    public static string FindMainCheckout(string start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var outermost = Path.GetFullPath(start);
        for (var dir = new DirectoryInfo(outermost).Parent; dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
            {
                outermost = dir.FullName;
            }
        }

        return outermost;
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Lifestone.slnx not found above {AppContext.BaseDirectory}; run the tests from a repository checkout.");
    }
}
