using Lifestone.Client.Options;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;

namespace Lifestone.Client.Tests.RetailUi;

/// <summary>The Retail UI option group (docs/retail-ui.md §15) and its parser.</summary>
public sealed class RetailUiOptionsTests
{
    [Fact]
    public void Defaults_leave_the_retail_ui_off_so_the_explore_scene_is_unchanged()
    {
        var o = RetailUiOptions.Parse([]);
        Assert.Equal(RetailUiMode.Off, o.Mode);
        Assert.False(o.Enabled);
        Assert.Null(o.ScaleOverride);
        Assert.Null(o.StartMode);
        Assert.Null(o.FramesDirectory);
        Assert.False(o.UiOnly);
        Assert.Equal(UiRetailGlitches.None, o.Glitches);
        Assert.False(o.RetailDisconnect);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--retail-ui=standin", RetailUiMode.StandIn)]
    [InlineData("--retail-ui=off", RetailUiMode.Off)]
    public void The_mode_reads_off_and_standin(string argument, RetailUiMode expected) =>
        Assert.Equal(expected, RetailUiOptions.Parse([argument]).Mode);

    [Fact]
    public void A_bad_mode_is_reported_and_the_ui_stays_off()
    {
        var o = RetailUiOptions.Parse(["--retail-ui=on"]);
        Assert.Equal(RetailUiMode.Off, o.Mode);
        Assert.Contains("standin", Assert.Single(o.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--ui-scale=1", 1)]
    [InlineData("--ui-scale=2", 2)]
    [InlineData("--ui-scale=16", 16)]
    public void The_scale_override_is_a_whole_number_from_one_to_sixteen(string argument, int expected) =>
        Assert.Equal(expected, RetailUiOptions.Parse([argument]).ScaleOverride);

    [Theory]
    [InlineData("--ui-scale=0")]
    [InlineData("--ui-scale=17")]
    [InlineData("--ui-scale=-2")]
    [InlineData("--ui-scale=1.5")]
    [InlineData("--ui-scale=big")]
    [InlineData("--ui-scale")]
    public void A_bad_scale_is_reported_and_keeps_the_rule(string argument)
    {
        var o = RetailUiOptions.Parse([argument]);
        Assert.Null(o.ScaleOverride);
        Assert.Single(o.Errors);
    }

    [Theory]
    [InlineData("datapatch", UiModes.DataPatch)]
    [InlineData("intro", UiModes.Intro)]
    [InlineData("charmanage", UiModes.CharacterManagement)]
    [InlineData("game", UiModes.GamePlay)]
    [InlineData("epilogue", UiModes.Epilogue)]
    [InlineData("disconnected", UiModes.Disconnected)]
    [InlineData("chargen", UiModes.CharGen)]
    [InlineData("credits", UiModes.Credits)]
    public void Every_screen_name_starts_its_mode_and_implies_the_stand_in_ui(string name, uint mode)
    {
        var o = RetailUiOptions.Parse([$"--retail-ui-start={name}"]);
        Assert.Equal(mode, o.StartMode);
        Assert.Equal(RetailUiMode.StandIn, o.Mode);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void The_screen_names_cover_the_eight_modes_once_each()
    {
        Assert.Equal(UiModes.RetailOrder.Order(), RetailUiOptions.Screens.Select(s => s.Mode).Order());
        Assert.Equal(RetailUiOptions.Screens.Count, RetailUiOptions.Screens.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_bad_start_screen_is_reported_with_the_choices()
    {
        var o = RetailUiOptions.Parse(["--retail-ui=standin", "--retail-ui-start=login"]);
        Assert.Null(o.StartMode);
        Assert.Contains("datapatch", Assert.Single(o.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void The_frames_option_takes_a_folder_or_the_default_and_implies_the_stand_in_ui()
    {
        var withFolder = RetailUiOptions.Parse(["--retail-ui-frames=cache/frames"]);
        Assert.Equal("cache/frames", withFolder.FramesDirectory);
        Assert.True(withFolder.Enabled);

        var plain = RetailUiOptions.Parse(["--retail-ui-frames"]);
        Assert.Equal(RetailUiOptions.DefaultFramesDirectory, plain.FramesDirectory);
        Assert.Equal("docs/screenshots/m2/ui", plain.FramesDirectory);
    }

    [Fact]
    public void The_flags_set_what_they_name()
    {
        var o = RetailUiOptions.Parse(["--retail-ui-only", "--retail-ui-glitches", "--retail-disconnect"]);
        Assert.True(o.UiOnly);
        Assert.True(o.Enabled);
        Assert.Equal(UiRetailGlitches.All, o.Glitches);
        Assert.True(o.RetailDisconnect);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void The_glitch_flag_restores_every_glitch_the_runtime_has()
    {
        var all = Enum.GetValues<UiRetailGlitches>().Where(g => g != UiRetailGlitches.None && g != UiRetailGlitches.All).Aggregate(UiRetailGlitches.None, (a, g) => a | g);
        Assert.Equal(UiRetailGlitches.All, all);
        Assert.Equal(UiRetailGlitches.All, RetailUiOptions.Parse(["--retail-ui-glitches"]).Glitches);
    }

    [Theory]
    [InlineData("--retail-ui-only")]
    [InlineData("--retail-ui-start=game")]
    [InlineData("--retail-ui-frames")]
    public void An_option_that_needs_the_ui_conflicts_with_an_explicit_off(string needsUi)
    {
        var o = RetailUiOptions.Parse(["--retail-ui=off", needsUi]);
        Assert.False(o.Enabled);
        Assert.Contains("--retail-ui=off", Assert.Single(o.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Other_groups_arguments_are_skipped_and_every_option_is_registered_in_the_retail_ui_group()
    {
        Assert.Empty(RetailUiOptions.Parse(["--start=arwic", "--walk", "--mute"]).Errors);
        var registered = OptionCatalog.RetailUi().Options.Select(o => o.Name).Order(StringComparer.Ordinal);
        Assert.Equal(
            ["--retail-disconnect", "--retail-ui", "--retail-ui-frames", "--retail-ui-glitches", "--retail-ui-only", "--retail-ui-start", "--ui-scale"],
            registered);
    }

    [Fact]
    public void The_frames_option_ends_the_run_and_the_others_leave_it_interactive()
    {
        var registry = OptionCatalog.Registry;
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--retail-ui-frames"]), OptionScenes.Main));
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--retail-ui-checks"]), OptionScenes.Main));
        foreach (var interactive in new[] { "--retail-ui=standin", "--ui-scale=2", "--retail-ui-start=charmanage", "--retail-ui-only", "--retail-ui-glitches", "--retail-disconnect" })
        {
            Assert.False(registry.EndsRun(CommandLineArgument.ParseAll([interactive]), OptionScenes.Main), interactive);
        }

        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--retail-ui=standin", "--ui-scale=2", "--retail-ui-only", "--retail-disconnect"]), OptionScenes.Main));
    }
}
