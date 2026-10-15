using System.Text.RegularExpressions;
using Lifestone.Client.RetailUi;

namespace Lifestone.Client.Tests.RetailUi;

/// <summary>The engine-free half of the review-frames run (<c>--retail-ui-frames</c>, m2-plan G2 U9) and its smoke step.</summary>
public sealed class RetailUiFramesPlanTests
{
    [Fact]
    public void The_frames_are_numbered_in_order_with_unique_file_safe_names()
    {
        var names = RetailUiFramesPlan.FrameNames;
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", name));
        Assert.Equal("01-datapatch-checking.png", RetailUiFramesPlan.FileName(1));
        Assert.Equal($"{names.Count:00}-disconnected.png", RetailUiFramesPlan.FileName(names.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetailUiFramesPlan.FileName(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetailUiFramesPlan.FileName(names.Count + 1));
    }

    [Fact]
    public void The_frames_cover_every_login_screen_and_each_dialog_type()
    {
        var names = RetailUiFramesPlan.FrameNames;
        Assert.Contains(names, n => n.StartsWith("datapatch-", StringComparison.Ordinal));
        Assert.Contains("charmanage-selected", names);
        Assert.Contains("charmanage-pending-selected", names);
        Assert.Contains("disconnected", names);
        foreach (var dialog in new[] { "dialog-delete-confirm", "dialog-please-wait", "dialog-exit-confirm", "dialog-credits-not-in-m2", "dialog-entering-world", "dialog-enter-refused-offline", "dialog-character-error" })
        {
            Assert.Contains(dialog, names);
        }
    }

    [Fact]
    public void The_default_folder_gets_the_window_size_and_a_named_folder_is_kept()
    {
        Assert.Equal("docs/screenshots/m2/ui/800x600", RetailUiFramesPlan.FolderFor(RetailUiOptions.DefaultFramesDirectory, 800, 600));
        Assert.Equal("docs/screenshots/m2/ui/1920x1080", RetailUiFramesPlan.FolderFor(RetailUiOptions.DefaultFramesDirectory, 1920, 1080));
        Assert.Equal("cache/m2/ui/800x600", RetailUiFramesPlan.FolderFor("cache/m2/ui/800x600", 1920, 1080));
        Assert.Equal(RetailUiOptions.DefaultFramesDirectory, RetailUiOptions.Parse(["--retail-ui-frames"]).FramesDirectory);
    }

    [Theory]
    [InlineData("--path|p|--resolution|800x600|--position|-20000,-20000", 800, 600)]
    [InlineData("--resolution|1920x1080|--|--retail-ui-frames", 1920, 1080)]
    [InlineData("--resolution|800x600|--resolution|1280x720", 1280, 720)]
    [InlineData("--path|p|--audio-driver|Dummy", 1152, 648)]
    [InlineData("--path|p|--|--resolution|640x480", 1152, 648)]
    public void The_requested_window_is_the_last_resolution_before_the_games_own_arguments_else_the_projects(string args, int width, int height) =>
        Assert.Equal((width, height), RetailUiFramesPlan.RequestedWindowSize(args.Split('|'), 1152, 648));

    [Theory]
    [InlineData("--resolution")]
    [InlineData("--resolution|big")]
    [InlineData("--resolution|800")]
    [InlineData("--resolution|0x600")]
    [InlineData("--resolution|800x600x2")]
    [InlineData("--resolution|-8x6")]
    [InlineData("--resolution|800x600|--resolution|oops")]
    public void A_resolution_that_is_not_a_size_leaves_the_requested_window_unknown(string args) =>
        Assert.Null(RetailUiFramesPlan.RequestedWindowSize(args.Split('|'), 1152, 648));

    [Fact]
    public void The_verdict_line_has_the_frozen_wording()
    {
        Assert.Equal(
            "retail ui frames done size=1920x1080 scale=1 captured=14 failures=0 missing_images=0",
            RetailUiFramesPlan.Verdict(1920, 1080, 1, 14, 0, 0));
        Assert.StartsWith(RetailUiFramesPlan.VerdictText, RetailUiFramesPlan.Verdict(800, 600, 1, 1, 1, 1), StringComparison.Ordinal);
    }

    [Fact]
    public void The_known_unresolved_image_is_did_zero_which_clears_an_elements_image()
    {
        Assert.Equal([0u], RetailUiFramesPlan.KnownMissingImages);
    }

    [Fact]
    public void The_smoke_step_reads_the_verdict_this_line_has_and_runs_both_sizes_and_the_checks()
    {
        var script = File.ReadAllText(RepositoryFiles.PathOf("tools", "smoke.ps1"));
        var pattern = Regex.Match(script, @"\$verdictPattern = '([^']+)'");
        Assert.True(pattern.Success, "tools/smoke.ps1 has no $verdictPattern for the retail-ui step");
        var match = Regex.Match(RetailUiFramesPlan.Verdict(800, 600, 1, 14, 0, 0), pattern.Groups[1].Value);
        Assert.True(match.Success);
        Assert.Equal("800x600", match.Groups["size"].Value);
        Assert.Equal("14", match.Groups["captured"].Value);
        Assert.Equal("0", match.Groups["failures"].Value);

        var step = script[script.IndexOf("'retail-ui' {", StringComparison.Ordinal)..];
        step = step[..step.IndexOf("'spell-showcase' {", StringComparison.Ordinal)];
        Assert.Contains("'800x600', '1920x1080'", script, StringComparison.Ordinal); // the opt-in sizes (-AllSizes); the default is 800x600 alone
        Assert.Contains("$defaultSize = '800x600'", script, StringComparison.Ordinal);
        Assert.Contains("foreach ($size in $frameSizes)", step, StringComparison.Ordinal);
        Assert.Contains("--retail-ui-frames=$dir", step, StringComparison.Ordinal);
        Assert.Contains("--retail-ui-checks", step, StringComparison.Ordinal);
        Assert.Contains("'portal-space', 'retail-ui', 'offline-player-checks',", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'retail-ui-checks' {", script, StringComparison.Ordinal); // the checks run in the retail-ui step, not as a second step of the same suite
    }
}
