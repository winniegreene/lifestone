using Lifestone.Client.Session.Credentials;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Finding the backend's runtime folder (ST5): the environment override first, then the nearest checkout whose
/// server\runtime holds secrets.json, so a worktree inside the main checkout uses the main checkout's backend. Stand-in
/// checkouts in a temporary folder; no secrets file is read.
/// </summary>
public sealed class ServerRuntimePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lifestone-runtime-" + Guid.NewGuid().ToString("N"));

    public ServerRuntimePathsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_worktree_uses_the_main_checkouts_backend()
    {
        var main = Checkout(_root);
        var worktree = Checkout(Path.Combine(main, ".claude", "worktrees", "m2-state"));
        File.WriteAllText(Path.Combine(main, "server", "runtime", "secrets.json"), "{}");

        Assert.True(ServerRuntimePaths.TryFind(null, [Path.Combine(worktree, "src")], out var paths, out var problem));
        Assert.Null(problem);
        Assert.Equal(Path.Combine(main, "server", "runtime"), paths.Folder);
        Assert.Equal(Path.Combine(main, "server", "runtime", "secrets.json"), paths.SecretsFile);
        Assert.Equal(Path.Combine(main, "server", "runtime", "pids.json"), paths.PidsFile);
    }

    [Fact]
    public void No_backend_is_reported_with_the_remedy()
    {
        var checkout = Checkout(Path.Combine(_root, "solo"));
        Assert.False(ServerRuntimePaths.TryFind("", [checkout], out var paths, out var problem));
        Assert.Null(paths);
        Assert.Contains("setup-server.ps1", problem, StringComparison.Ordinal);
        Assert.Contains(ServerRuntimePaths.EnvironmentVariable, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_environment_override_wins_and_must_be_absolute()
    {
        var main = Checkout(_root);
        File.WriteAllText(Path.Combine(main, "server", "runtime", "secrets.json"), "{}");
        var elsewhere = Path.Combine(_root, "elsewhere", "runtime");

        Assert.True(ServerRuntimePaths.TryFind(elsewhere + Path.DirectorySeparatorChar, [main], out var paths, out _));
        Assert.Equal(elsewhere, paths.Folder);

        Assert.False(ServerRuntimePaths.TryFind(Path.Combine("relative", "runtime"), [main], out _, out var problem));
        Assert.Contains("absolute", problem, StringComparison.Ordinal);
    }

    private static string Checkout(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "server", "runtime"));
        File.WriteAllText(Path.Combine(folder, "Lifestone.slnx"), "<Solution />");
        return folder;
    }
}
