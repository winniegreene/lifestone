namespace Lifestone.Data.Checkout;

/// <summary>
/// Finds the Lifestone checkout a process runs in: a folder holding <see cref="MarkerFileName"/>. A git worktree created
/// inside a checkout (<c>&lt;repo&gt;\.claude\worktrees\&lt;name&gt;</c>) is itself a checkout nested in the main one, and
/// shares what only the main checkout holds: the user's dats (<c>client\</c>), the backend's runtime folder
/// (<c>server\runtime\</c>) and the shared <c>cache\</c>.
/// </summary>
/// <remarks>
/// Read-only: it checks whether files exist and reads nothing but a worktree's <c>.git</c> pointer file, never a secret.
/// The PowerShell side has the same walk (<c>Get-LifestoneCheckouts</c> in <c>tools\lib\Toolchain.psm1</c>).
/// </remarks>
public static class CheckoutLocator
{
    /// <summary>The file that marks the root of a Lifestone checkout.</summary>
    public const string MarkerFileName = "Lifestone.slnx";

    /// <summary>The backend's runtime folder, relative to a checkout (gitignored; the backend runs only from the main checkout).</summary>
    public static readonly string ServerRuntimeRelativePath = Path.Combine("server", "runtime");

    /// <summary>The credentials file in <see cref="ServerRuntimeRelativePath"/>. Its presence marks a set-up backend; it is never read here.</summary>
    public const string SecretsFileName = "secrets.json";

    private const string GitName = ".git";
    private const string GitDirPrefix = "gitdir:";
    private const int MaxGitPointerBytes = 4096;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Where a process looks by default: the current directory, then the program's folder.</summary>
    public static IReadOnlyList<string> DefaultSearchRoots() => [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];

    /// <summary>
    /// Every folder from <paramref name="start"/> up to the drive root that holds <see cref="MarkerFileName"/>, nearest
    /// first: a worktree inside a checkout gives the worktree, then the main checkout. Empty outside any checkout.
    /// </summary>
    public static IReadOnlyList<string> EnclosingCheckouts(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var checkouts = new List<string>();
        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, MarkerFileName)))
            {
                checkouts.Add(Path.TrimEndingDirectorySeparator(dir.FullName));
            }
        }

        return checkouts;
    }

    /// <summary>
    /// <see cref="EnclosingCheckouts(string)"/> of each of <paramref name="searchRoots"/> in turn (blank roots are
    /// skipped), each checkout listed once, at its first position.
    /// </summary>
    public static IReadOnlyList<string> EnclosingCheckouts(IEnumerable<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        var seen = new HashSet<string>(PathComparer);
        var checkouts = new List<string>();
        foreach (var root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var checkout in EnclosingCheckouts(root))
            {
                if (seen.Add(checkout))
                {
                    checkouts.Add(checkout);
                }
            }
        }

        return checkouts;
    }

    /// <summary>
    /// The main checkout <paramref name="start"/> belongs to, or null outside any checkout: the nearest enclosing checkout
    /// whose <c>.git</c> is a folder; for a linked worktree (whose <c>.git</c> is a <c>gitdir:</c> pointer file), the
    /// checkout that owns the repository it points into, even when the worktree lives elsewhere; without git metadata, the
    /// outermost enclosing checkout.
    /// </summary>
    public static string? FindMainCheckout(string start)
    {
        var checkouts = EnclosingCheckouts(start);
        foreach (var checkout in checkouts)
        {
            var git = Path.Combine(checkout, GitName);
            if (Directory.Exists(git))
            {
                return checkout;
            }

            if (File.Exists(git) && TryFollowWorktreePointer(checkout, git) is { } main)
            {
                return main;
            }
        }

        return checkouts.Count > 0 ? checkouts[^1] : null;
    }

    /// <summary>The main checkout of the first of <paramref name="searchRoots"/> that lies inside a checkout, or null.</summary>
    public static string? FindMainCheckout(IEnumerable<string> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        foreach (var root in searchRoots)
        {
            if (!string.IsNullOrWhiteSpace(root) && FindMainCheckout(root) is { } main)
            {
                return main;
            }
        }

        return null;
    }

    /// <summary>
    /// The backend runtime folder (<c>&lt;checkout&gt;\server\runtime</c>) of the first enclosing checkout, nearest first
    /// across <paramref name="searchRoots"/>, whose runtime folder holds <see cref="SecretsFileName"/>: a worktree uses the
    /// main checkout's. Null when none does (the backend is not set up, or the process runs outside any checkout). The
    /// file's presence is checked; its contents are never read.
    /// </summary>
    public static string? FindServerRuntime(IEnumerable<string> searchRoots)
    {
        foreach (var checkout in EnclosingCheckouts(searchRoots))
        {
            var runtime = Path.Combine(checkout, ServerRuntimeRelativePath);
            if (File.Exists(Path.Combine(runtime, SecretsFileName)))
            {
                return runtime;
            }
        }

        return null;
    }

    /// <summary>
    /// Follows a linked worktree's <c>.git</c> file (<c>gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, absolute
    /// or relative to the worktree) to the main checkout; null when the file has another shape or the folder it names is
    /// not a checkout.
    /// </summary>
    private static string? TryFollowWorktreePointer(string checkout, string gitFile)
    {
        string text;
        try
        {
            if (new FileInfo(gitFile).Length > MaxGitPointerBytes)
            {
                return null;
            }

            text = File.ReadAllText(gitFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var line = text.Split('\n', 2)[0].Trim();
        if (!line.StartsWith(GitDirPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var gitDir = new DirectoryInfo(Path.GetFullPath(line[GitDirPrefix.Length..].Trim(), checkout));
        var worktrees = gitDir.Parent;
        var commonDir = worktrees?.Parent;
        if (worktrees is null || commonDir is null || commonDir.Parent is null ||
            !PathComparer.Equals(worktrees.Name, "worktrees") || !PathComparer.Equals(commonDir.Name, GitName))
        {
            return null;
        }

        var main = Path.TrimEndingDirectorySeparator(commonDir.Parent.FullName);
        return File.Exists(Path.Combine(main, MarkerFileName)) ? main : null;
    }
}
