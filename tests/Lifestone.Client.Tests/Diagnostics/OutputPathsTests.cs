using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>One rule for output paths: relative to the repository root, never inside src/ or tests/.</summary>
public sealed class OutputPathsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lifestone-repo"));

    [Fact]
    public void Relative_paths_are_taken_from_the_repository_root()
    {
        Assert.True(OutputPaths.TryResolve("docs/screenshots/m1/x.png", Root, out var full, out var problem));
        Assert.Null(problem);
        Assert.Equal(Path.Combine(Root, "docs", "screenshots", "m1", "x.png"), full);
    }

    [Fact]
    public void Absolute_paths_are_kept()
    {
        var elsewhere = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "y.png"));
        Assert.True(OutputPaths.TryResolve(elsewhere, Root, out var full, out _));
        Assert.Equal(elsewhere, full);
    }

    [Theory]
    [InlineData("src/Lifestone.Game/shot.png")]
    [InlineData("src")]
    [InlineData("tests/Lifestone.Data.Tests/TestResults/x.png")]
    [InlineData("docs/../src/Lifestone.Game/frames/f1.png")]
    public void The_source_trees_are_refused(string path)
    {
        Assert.False(OutputPaths.TryResolve(path, Root, out var full, out var problem));
        Assert.NotNull(problem);
        Assert.Contains("docs/screenshots/", problem, StringComparison.Ordinal);
        Assert.StartsWith(Root, full, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_path_into_the_source_tree_is_refused_too()
    {
        Assert.False(OutputPaths.TryResolve(Path.Combine(Root, "src", "a.png"), Root, out _, out _));
    }

    [Theory]
    [InlineData("src2/a.png")]
    [InlineData("srcfoo")]
    [InlineData("docs/src/a.png")]
    public void Only_the_trees_themselves_are_refused_not_lookalikes(string path)
    {
        Assert.True(OutputPaths.TryResolve(path, Root, out _, out var problem), problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_paths_are_refused(string path)
    {
        Assert.False(OutputPaths.TryResolve(path, Root, out _, out var problem));
        Assert.Equal("the output path is empty", problem);
    }

    [Fact]
    public void Containment_is_case_insensitive_on_windows()
    {
        var inside = OutputPaths.IsSameOrInside(Path.Combine(Root, "SRC", "x.png"), Path.Combine(Root, "src"));
        Assert.Equal(OperatingSystem.IsWindows(), inside);
    }
}
