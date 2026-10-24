using System.Text.RegularExpressions;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Panels;

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

    [Fact]
    public void The_offline_content_frames_of_the_inventory_and_the_container_window_are_extra_frames_with_unique_file_safe_names_that_clash_with_no_other_frame()
    {
        // M4 2.2 s2 and s4, 4.1 t1: review frames of PLACEHOLDER content (the stand-in's Pack with one Pyreal; the stand-in chest's Bowl and Smock; the loot chest's three items). The offline `corpse` frame went with the stand-in tusker (D26).
        var names = PanelFramesPlan.OfflineContentFrames;
        Assert.Equal(["inventory-sidepack", "container-window", "container-chest"], names); // M4 3.2 lane e added the chest frame (D23: a chest, no creature corpse); 4.1 t1 removed the stand-in tusker's corpse frame
        Assert.Equal("container-chest", PanelFramesPlan.ContainerChestFrame);
        Assert.Equal("c2-spellcasting-empty", PanelFramesPlan.SpellcastingEmptyFrame.Name);
        Assert.Equal("corpse", Lifestone.Client.Session.Scenarios.HuntUiShotPlan.Names[Lifestone.Client.Session.Scenarios.HuntUiShotPlan.Corpse]); // the live hunt's frame is <stem>-corpse.png: no offline frame takes its name since 4.1 t1
        Assert.DoesNotContain("corpse", names); // no offline run draws a creature's corpse (D26)
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", name));
        Assert.Equal("inventory-sidepack", PanelFramesPlan.InventorySidePackFrame);
        Assert.Equal("container-window", PanelFramesPlan.ContainerWindowFrame);
        Assert.Equal("container-window.png", ShellFramesPlan.FileName(PanelFramesPlan.ContainerWindowFrame)); // the play-checks save them in the extra folder by this name

        // Not one of the shell's ten frames (those are counted by file), a window frame, or a page's frame.
        Assert.DoesNotContain(names, name => ShellFramesPlan.FrameNames.Any(frame => frame.EndsWith(name, StringComparison.Ordinal)));
        Assert.DoesNotContain(names, name => PanelFramesPlan.AllWindowFrames.Any(window => window.Name == name));
        var pageFiles = GamePanelHost.Pages.Select(PanelFramesPlan.FileName).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(names, name => pageFiles.Contains(name + ".png"));
        Assert.Contains("7-inventory-backpack-and-paperdoll.png", pageFiles); // the page's own frame is a different file
    }

    [Fact]
    public void The_vendor_windows_offline_frame_is_an_extra_frame_with_a_unique_file_safe_name_that_clashes_with_no_other_frame()
    {
        // M4 3.2 s4b: one review frame of PLACEHOLDER content (the stand-in's Thelnoth and shop), saved in the extra folder by the play-checks' vendor rows.
        Assert.Equal(["vendor-buy-sell"], PanelFramesPlan.VendorContentFrames);
        Assert.Equal("vendor-buy-sell", PanelFramesPlan.VendorBuySellFrame);
        Assert.Equal("vendor-buy-sell.png", ShellFramesPlan.FileName(PanelFramesPlan.VendorBuySellFrame));
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", PanelFramesPlan.VendorBuySellFrame);
        Assert.DoesNotContain(PanelFramesPlan.VendorBuySellFrame, PanelFramesPlan.OfflineContentFrames);
        Assert.DoesNotContain(PanelFramesPlan.VendorBuySellFrame, PanelFramesPlan.PanelShotExtraFrames);
        Assert.DoesNotContain(ShellFramesPlan.FrameNames, frame => frame.EndsWith(PanelFramesPlan.VendorBuySellFrame, StringComparison.Ordinal));
        Assert.DoesNotContain(PanelFramesPlan.AllWindowFrames, window => window.Name == PanelFramesPlan.VendorBuySellFrame);
    }

    [Fact]
    public void The_offline_examine_frame_is_an_extra_frame_with_a_file_safe_name_that_clashes_with_no_other_frame_M4_3_2_s4c()
    {
        // M4 3.2 s4c: the play-checks' examine rows save the panel on the stand-in (PLACEHOLDER content: a labelled subset of ACE's tables, TODO(m4-standin-appraise-tables)) in the extra folder; the lead's panel shot is a different file.
        var names = PanelFramesPlan.OfflineExamineFrames;
        Assert.Equal(["examine-kit-standin"], names);
        Assert.Equal("examine-kit-standin", PanelFramesPlan.ExamineStandInFrame);
        Assert.Equal("examine-kit-standin.png", ShellFramesPlan.FileName(PanelFramesPlan.ExamineStandInFrame));
        Assert.All(names, name => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", name));
        Assert.DoesNotContain(names, name => PanelFramesPlan.OfflineContentFrames.Contains(name));
        Assert.DoesNotContain(names, name => PanelFramesPlan.ExamineShotFrames.Contains(name));
        Assert.DoesNotContain(names, name => ShellFramesPlan.FrameNames.Any(frame => frame.EndsWith(name, StringComparison.Ordinal)));
        Assert.DoesNotContain(names, name => PanelFramesPlan.AllWindowFrames.Any(window => window.Name == name));
        var pageFiles = GamePanelHost.Pages.Select(PanelFramesPlan.FileName).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(names, name => pageFiles.Contains(name + ".png"));
    }
}
