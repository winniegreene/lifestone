using Lifestone.Data.Dat;

namespace Lifestone.Data.Tests;

/// <summary>
/// Where the retail dats are looked for (<see cref="DatSet.ResolveDirectory(string?, string?, IReadOnlyList{string})"/>):
/// <c>--dat-dir</c>, then <c>LIFESTONE_DAT_DIR</c>, then the <c>client</c> folder of the checkout (the folder holding
/// Lifestone.slnx), found by walking up. Runs on empty stand-in files in a temporary folder; no retail data needed.
/// </summary>
public sealed class DatDirectoryResolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lifestone-dat-resolution-" + Guid.NewGuid().ToString("N"));

    public DatDirectoryResolutionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_explicit_folder_wins_over_the_environment_and_the_checkout()
    {
        var checkout = Checkout("repo", withDats: true);
        var explicitDir = Path.Combine(_root, "explicit");
        var resolved = DatSet.ResolveDirectory(explicitDir, Path.Combine(_root, "from-env"), [Path.Combine(checkout, "src")]);
        Assert.Equal(explicitDir, resolved);
    }

    [Fact]
    public void The_environment_variable_wins_over_the_checkout()
    {
        var checkout = Checkout("repo", withDats: true);
        var fromEnvironment = Path.Combine(_root, "from-env");
        Assert.Equal(fromEnvironment, DatSet.ResolveDirectory(null, fromEnvironment, [Path.Combine(checkout, "src")]));
        Assert.Equal(fromEnvironment, DatSet.ResolveDirectory("  ", fromEnvironment, [Path.Combine(checkout, "src")]));
    }

    [Fact]
    public void Without_either_the_client_folder_of_the_enclosing_checkout_is_used()
    {
        var checkout = Checkout("repo", withDats: true);
        var deep = Directory.CreateDirectory(Path.Combine(checkout, "src", "Lifestone.Game", ".godot", "mono")).FullName;
        Assert.Equal(Path.Combine(checkout, "client"), DatSet.ResolveDirectory(null, null, [deep]));
        Assert.Equal(Path.Combine(checkout, "client"), DatSet.ResolveDirectory(" ", "", [deep]));
    }

    [Fact]
    public void A_worktree_inside_a_checkout_uses_the_enclosing_checkouts_dats()
    {
        var outer = Checkout("repo", withDats: true);
        var worktree = Checkout(Path.Combine("repo", ".claude", "worktrees", "wt"), withDats: false);
        var testBin = Directory.CreateDirectory(Path.Combine(worktree, "tests", "bin", "Debug")).FullName;
        Assert.Equal(Path.Combine(outer, "client"), DatSet.FindDefaultDirectory([testBin]));
    }

    [Fact]
    public void The_nearest_checkout_wins_when_it_has_the_dats()
    {
        Checkout("repo", withDats: true);
        var inner = Checkout(Path.Combine("repo", "nested"), withDats: true);
        Assert.Equal(Path.Combine(inner, "client"), DatSet.FindDefaultDirectory([inner]));
    }

    [Fact]
    public void Without_dats_anywhere_the_nearest_checkouts_client_folder_is_named()
    {
        Checkout("repo", withDats: false);
        var inner = Checkout(Path.Combine("repo", "nested"), withDats: false);
        Assert.Equal(Path.Combine(inner, "client"), DatSet.FindDefaultDirectory([inner]));
        Assert.False(DatSet.Exists(DatSet.ResolveDirectory(null, null, [inner])));
    }

    [Fact]
    public void Later_search_roots_are_tried_when_an_earlier_one_is_outside_any_checkout()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
        var checkout = Checkout("repo", withDats: true);
        var programDir = Directory.CreateDirectory(Path.Combine(checkout, "bin")).FullName;
        Assert.Equal(Path.Combine(checkout, "client"), DatSet.ResolveDirectory(null, null, [outside, programDir]));
    }

    [Fact]
    public void Outside_any_checkout_the_client_folder_next_to_the_program_is_used()
    {
        var cwd = Directory.CreateDirectory(Path.Combine(_root, "cwd")).FullName;
        var program = Directory.CreateDirectory(Path.Combine(_root, "Lifestone")).FullName;
        Assert.Null(DatSet.FindDefaultDirectory([cwd, program]));
        Assert.Equal(Path.Combine(program, "client"), DatSet.ResolveDirectory(null, null, [cwd, program]));
    }

    [Fact]
    public void Opening_a_folder_without_the_dats_names_the_folder_and_the_remedy()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        var error = Assert.Throws<FileNotFoundException>(() => DatSet.Open(empty));
        Assert.Contains(empty, error.Message, StringComparison.Ordinal);
        Assert.Contains(DatSet.PortalFileName, error.Message, StringComparison.Ordinal);
        Assert.Contains(DatSet.DirectoryEnvironmentVariable, error.Message, StringComparison.Ordinal);
        Assert.False(DatSet.Exists(empty));
    }

    [Fact]
    public void The_default_resolution_of_this_test_run_is_the_client_folder_of_a_checkout()
    {
        var resolved = DatSet.ResolveDirectory();
        var fromEnvironment = Environment.GetEnvironmentVariable(DatSet.DirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            Assert.Equal(Path.GetFullPath(fromEnvironment), resolved);
            return;
        }

        // The tests run inside a checkout, so the default is some checkout's client folder (this one's, or the enclosing
        // checkout's when this is a worktree inside it and only that one holds the dats).
        Assert.Equal(DatSet.DefaultDirectoryName, Path.GetFileName(resolved));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(resolved)!, DatSet.CheckoutMarkerFileName)), resolved);
    }

    /// <summary>A stand-in checkout at <paramref name="relative"/> under the temp root: Lifestone.slnx and, optionally, empty dats.</summary>
    private string Checkout(string relative, bool withDats)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;
        File.WriteAllText(Path.Combine(dir, DatSet.CheckoutMarkerFileName), "<Solution />");
        if (withDats)
        {
            var client = Directory.CreateDirectory(Path.Combine(dir, DatSet.DefaultDirectoryName)).FullName;
            File.WriteAllBytes(Path.Combine(client, DatSet.PortalFileName), []);
            File.WriteAllBytes(Path.Combine(client, DatSet.CellFileName), []);
        }

        return dir;
    }
}
