using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.Checkout;

namespace Lifestone.Client.Session.Credentials;

/// <summary>
/// Where the localhost backend keeps its runtime files (<c>&lt;checkout&gt;\server\runtime</c>): the two a host reads
/// before a live session, secrets.json (<see cref="PlayAccounts"/>) and pids.json (<see cref="BackendReadiness"/>).
/// Found the way <c>DatSet</c> finds the dats and tools\lib\Toolchain.psm1 finds the backend, through
/// <see cref="CheckoutLocator"/>: the nearest enclosing checkout whose runtime folder holds secrets.json, so a worktree
/// nested in the main checkout uses the main checkout's backend (there is one ACE). The host reads the files; this type
/// only names them.
/// </summary>
/// <param name="Folder">The runtime folder (absolute).</param>
public sealed record ServerRuntimePaths(string Folder)
{
    /// <summary>
    /// The environment variable that names the runtime folder instead (an absolute path), for a process that runs
    /// outside any checkout. Hosts read it and pass its value to <see cref="TryFind(string?, IEnumerable{string}, out ServerRuntimePaths?, out string?)"/>.
    /// </summary>
    public const string EnvironmentVariable = "LIFESTONE_SERVER_RUNTIME";

    /// <summary>The process-ids file start-server.ps1 writes (with <c>ace.modReady</c>).</summary>
    public const string PidsFileName = "pids.json";

    /// <summary>The credentials file (gitignored; read only by the host, as bytes).</summary>
    public string SecretsFile => Path.Combine(Folder, CheckoutLocator.SecretsFileName);

    /// <summary>The process-ids file.</summary>
    public string PidsFile => Path.Combine(Folder, PidsFileName);

    /// <summary>
    /// The runtime folder: <paramref name="environmentValue"/> when given (it must be absolute), else the nearest checkout
    /// around one of <paramref name="searchRoots"/> whose server\runtime holds secrets.json. False with the reason (and the
    /// remedy) when there is none.
    /// </summary>
    /// <param name="environmentValue">The value of <see cref="EnvironmentVariable"/>, or null.</param>
    /// <param name="searchRoots">Where to start walking up (the hosts pass <see cref="CheckoutLocator.DefaultSearchRoots"/>).</param>
    /// <param name="paths">The paths when found.</param>
    /// <param name="problem">Why not.</param>
    public static bool TryFind(
        string? environmentValue,
        IEnumerable<string> searchRoots,
        [NotNullWhen(true)] out ServerRuntimePaths? paths,
        [NotNullWhen(false)] out string? problem)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        paths = null;
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            if (!Path.IsPathFullyQualified(environmentValue))
            {
                problem = $"{EnvironmentVariable} must name an absolute folder (the backend's server\\runtime)";
                return false;
            }

            paths = new ServerRuntimePaths(Path.TrimEndingDirectorySeparator(Path.GetFullPath(environmentValue)));
            problem = null;
            return true;
        }

        if (CheckoutLocator.FindServerRuntime(searchRoots) is { } folder)
        {
            paths = new ServerRuntimePaths(folder);
            problem = null;
            return true;
        }

        problem = $"no backend found: no Lifestone checkout around this program has server\\runtime\\{CheckoutLocator.SecretsFileName} " +
            $"(set the backend up with server\\setup-server.ps1 in the main checkout, or set {EnvironmentVariable})";
        return false;
    }
}
