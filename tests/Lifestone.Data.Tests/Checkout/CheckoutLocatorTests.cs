using Lifestone.Data.Checkout;

namespace Lifestone.Data.Tests.Checkout;

/// <summary>
/// <see cref="CheckoutLocator"/> on stand-in checkouts in a temporary folder (a Lifestone.slnx marker, optional git
/// metadata and an optional server runtime folder); no real checkout, git repository or secret is touched.
/// </summary>
public sealed class CheckoutLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lifestone-checkout-locator-" + Guid.NewGuid().ToString("N"));

    public CheckoutLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Enclosing_checkouts_are_listed_nearest_first()
    {
        var main = Checkout("repo");
        var worktree = Checkout(Path.Combine("repo", ".claude", "worktrees", "wt"));
        var deep = Folder(Path.Combine(worktree, "tests", "bin", "Debug"));

        Assert.Equal([worktree, main], CheckoutLocator.EnclosingCheckouts(deep));
        Assert.Equal([worktree, main], CheckoutLocator.EnclosingCheckouts(worktree + Path.DirectorySeparatorChar));
        Assert.Equal([main], CheckoutLocator.EnclosingCheckouts(main));
    }

    [Fact]
    public void Several_search_roots_list_each_checkout_once_in_order()
    {
        var main = Checkout("repo");
        var worktree = Checkout(Path.Combine("repo", ".claude", "worktrees", "wt"));
        var other = Checkout("other");
        var outside = Folder(Path.Combine(_root, "elsewhere"));

        var checkouts = CheckoutLocator.EnclosingCheckouts([outside, "", " ", Path.Combine(worktree, "src"), main, other]);
        Assert.Equal([worktree, main, other], checkouts);
        Assert.Empty(CheckoutLocator.EnclosingCheckouts([outside]));
    }

    [Fact]
    public void Outside_any_checkout_nothing_is_found()
    {
        var outside = Folder(Path.Combine(_root, "elsewhere", "deeper"));
        Assert.Empty(CheckoutLocator.EnclosingCheckouts(outside));
        Assert.Null(CheckoutLocator.FindMainCheckout(outside));
        Assert.Null(CheckoutLocator.FindMainCheckout([outside]));
        Assert.Null(CheckoutLocator.FindServerRuntime([outside]));
    }

    [Fact]
    public void A_worktree_inside_the_main_checkout_finds_the_main_checkout()
    {
        var main = Checkout("repo");
        Folder(Path.Combine(main, ".git", "worktrees", "wt"));
        var worktree = Checkout(Path.Combine("repo", ".claude", "worktrees", "wt"));
        WorktreePointer(worktree, Path.Combine(main, ".git", "worktrees", "wt"));

        Assert.Equal(main, CheckoutLocator.FindMainCheckout(Path.Combine(worktree, "src")));
        Assert.Equal(main, CheckoutLocator.FindMainCheckout(main));
        Assert.Equal(main, CheckoutLocator.FindMainCheckout(["  ", Folder(Path.Combine(_root, "elsewhere")), worktree]));
    }

    [Fact]
    public void A_worktree_elsewhere_follows_its_gitdir_pointer_absolute_or_relative()
    {
        var main = Checkout("repo");
        Folder(Path.Combine(main, ".git", "worktrees", "a"));
        Folder(Path.Combine(main, ".git", "worktrees", "b"));
        var absolute = Checkout("a");
        var relative = Checkout("b");
        WorktreePointer(absolute, Path.Combine(main, ".git", "worktrees", "a"));
        File.WriteAllText(Path.Combine(relative, ".git"), "gitdir: ../repo/.git/worktrees/b\r\n");

        Assert.Equal(main, CheckoutLocator.FindMainCheckout(absolute));
        Assert.Equal(main, CheckoutLocator.FindMainCheckout(Path.Combine(relative, "src")));
    }

    [Fact]
    public void Without_git_metadata_the_outermost_checkout_is_the_main_one()
    {
        var main = Checkout("repo");
        var nested = Checkout(Path.Combine("repo", "nested"));
        Assert.Equal(main, CheckoutLocator.FindMainCheckout(nested));

        // A .git file that is not a worktree pointer (a submodule's, say) is no evidence either way.
        File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: ../.git/modules/nested\n");
        Assert.Equal(main, CheckoutLocator.FindMainCheckout(nested));

        // The nearest checkout with a .git folder wins over an outer one.
        Folder(Path.Combine(nested, "inner", ".git"));
        var inner = Checkout(Path.Combine("repo", "nested", "inner"));
        Assert.Equal(inner, CheckoutLocator.FindMainCheckout(inner));
    }

    [Fact]
    public void A_pointer_into_a_folder_that_is_not_a_checkout_is_ignored()
    {
        var notCheckout = Folder(Path.Combine(_root, "bare"));
        Folder(Path.Combine(notCheckout, ".git", "worktrees", "wt"));
        var worktree = Checkout("wt");
        WorktreePointer(worktree, Path.Combine(notCheckout, ".git", "worktrees", "wt"));
        Assert.Equal(worktree, CheckoutLocator.FindMainCheckout(worktree)); // falls back to the outermost (only) checkout
    }

    [Fact]
    public void The_server_runtime_is_the_nearest_checkouts_that_holds_the_secrets_file()
    {
        var main = Checkout("repo");
        var worktree = Checkout(Path.Combine("repo", ".claude", "worktrees", "wt"));
        var start = Folder(Path.Combine(worktree, "tests"));
        Assert.Null(CheckoutLocator.FindServerRuntime([start]));

        // A runtime folder without the secrets file is not a set-up backend.
        Folder(Path.Combine(main, "server", "runtime"));
        Assert.Null(CheckoutLocator.FindServerRuntime([start]));

        // The worktree has no backend of its own, so it uses the enclosing (main) checkout's.
        File.WriteAllText(Path.Combine(main, "server", "runtime", CheckoutLocator.SecretsFileName), "{}");
        Assert.Equal(Path.Combine(main, "server", "runtime"), CheckoutLocator.FindServerRuntime([start]));

        // A nested checkout with its own set-up backend uses that one.
        Folder(Path.Combine(worktree, "server", "runtime"));
        File.WriteAllText(Path.Combine(worktree, "server", "runtime", CheckoutLocator.SecretsFileName), "{}");
        Assert.Equal(Path.Combine(worktree, "server", "runtime"), CheckoutLocator.FindServerRuntime([start]));
    }

    [Fact]
    public void The_dat_sets_marker_is_the_locators()
    {
        Assert.Equal(CheckoutLocator.MarkerFileName, Lifestone.Data.Dat.DatSet.CheckoutMarkerFileName);
        Assert.Equal(Path.Combine("server", "runtime"), CheckoutLocator.ServerRuntimeRelativePath);
        Assert.Equal(2, CheckoutLocator.DefaultSearchRoots().Count);
    }

    /// <summary>A stand-in checkout at <paramref name="relative"/> under the temp root: a folder holding Lifestone.slnx.</summary>
    private string Checkout(string relative)
    {
        var dir = Folder(Path.Combine(_root, relative));
        File.WriteAllText(Path.Combine(dir, CheckoutLocator.MarkerFileName), "<Solution />");
        return dir;
    }

    private static string Folder(string path) => Path.TrimEndingDirectorySeparator(Directory.CreateDirectory(path).FullName);

    private static void WorktreePointer(string worktree, string gitDir) =>
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {gitDir.Replace('\\', '/')}\n");
}
