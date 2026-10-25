using Lifestone.Client.Tests;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Harness;

/// <summary>
/// <see cref="LoopbackLock"/> on a lock file in a temporary folder (never the main checkout's): exclusive while held,
/// a clear reason when the wait runs out, released and deleted on dispose; and where the real lock lives.
/// </summary>
public sealed class LoopbackLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lifestone-lock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_lock_is_exclusive_until_released_and_its_file_goes_with_it()
    {
        var path = Path.Combine(_root, "cache", "loopback.lock");
        using (var held = LoopbackLock.TryAcquire(path, TimeSpan.Zero, out var failure))
        {
            Assert.NotNull(held);
            Assert.Null(failure);
            Assert.Equal(Path.GetFullPath(path), held.Path);
            Assert.True(File.Exists(path));

            Assert.Null(LoopbackLock.TryAcquire(path, TimeSpan.FromMilliseconds(300), out var busy));
            Assert.NotNull(busy);
            Assert.Contains(Path.GetFullPath(path), busy, StringComparison.Ordinal);
            Assert.Contains("held", busy, StringComparison.Ordinal);
            Assert.Contains("300 ms", busy, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(path));
        using var again = LoopbackLock.TryAcquire(path, TimeSpan.Zero, out _);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_waiting_run_gets_the_lock_once_the_holder_lets_go()
    {
        var path = Path.Combine(_root, "loopback.lock");
        var first = LoopbackLock.TryAcquire(path, TimeSpan.Zero, out _);
        Assert.NotNull(first);
        var release = Task.Run(async () =>
        {
            await Task.Delay(400);
            first.Dispose();
        });

        using var second = LoopbackLock.TryAcquire(path, TimeSpan.FromSeconds(30), out var failure);
        Assert.NotNull(second);
        Assert.Null(failure);
        await release;
    }

    [Fact]
    public async Task A_lock_file_that_cannot_be_opened_yet_is_retried_until_the_wait_runs_out()
    {
        // A read-only lock file stands in for one still delete-pending after its holder let go: Windows reports both as
        // access denied (UnauthorizedAccessException), which is retried like a sharing violation.
        var path = Path.Combine(_root, "loopback.lock");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, string.Empty);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Null(LoopbackLock.TryAcquire(path, TimeSpan.FromMilliseconds(300), out var denied));
            Assert.NotNull(denied);
            Assert.Contains("cannot be opened", denied, StringComparison.Ordinal);
            Assert.Contains("300 ms", denied, StringComparison.Ordinal);

            var unblock = Task.Run(async () =>
            {
                await Task.Delay(400);
                File.SetAttributes(path, FileAttributes.Normal);
            });
            using (var held = LoopbackLock.TryAcquire(path, TimeSpan.FromSeconds(30), out var failure))
            {
                Assert.NotNull(held);
                Assert.Null(failure);
            }

            await unblock;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void Two_accounts_hold_their_locks_at_once_and_the_same_account_excludes_itself()
    {
        var melee = Path.Combine(_root, "cache", "loopback-test-melee.lock");
        var archer = Path.Combine(_root, "cache", "loopback-test-archer.lock");
        using var first = LoopbackLock.TryAcquire(melee, TimeSpan.Zero, out var failure1);
        using var second = LoopbackLock.TryAcquire(archer, TimeSpan.Zero, out var failure2);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(failure1);
        Assert.Null(failure2);
        using var tools = LoopbackLock.TryAcquire(Path.Combine(_root, "cache", "loopback-test-tools.lock"), TimeSpan.Zero, out var failure3); // the fourth account holds its own lock beside the other two
        Assert.NotNull(tools);
        Assert.Null(failure3);
        Assert.Null(LoopbackLock.TryAcquire(melee, TimeSpan.FromMilliseconds(300), out var busy));
        Assert.Contains("one test account", busy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_lock_lives_in_the_main_checkout_s_cache()
    {
        Assert.Equal(Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback-test.lock"), LoopbackLock.DefaultPath);
        Assert.Equal(Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback-test-melee.lock"), LoopbackLock.PathFor("test-melee"));
        Assert.Equal(Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback-test-archer.lock"), LoopbackLock.PathFor("test-archer"));
        Assert.Equal(Path.Combine(RepositoryFiles.MainCheckout, "cache", "loopback-test-tools.lock"), LoopbackLock.PathFor("test-tools")); // M4 D39: the tools lane's own lock
        Assert.Throws<ArgumentException>(() => LoopbackLock.PathFor("../x"));
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.MainCheckout, "Lifestone.slnx")));
        Assert.StartsWith(RepositoryFiles.MainCheckout, RepositoryFiles.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_main_checkout_is_the_outermost_one_around_a_worktree()
    {
        var main = Path.Combine(_root, "checkout");
        var worktree = Path.Combine(main, ".claude", "worktrees", "track");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(main, "Lifestone.slnx"), string.Empty);
        File.WriteAllText(Path.Combine(worktree, "Lifestone.slnx"), string.Empty);

        Assert.Equal(main, RepositoryFiles.FindMainCheckout(worktree));
        Assert.Equal(main, RepositoryFiles.FindMainCheckout(main));
    }
}
