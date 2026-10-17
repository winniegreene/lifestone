using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The Godot side of the combat windows' review frames, pinned in its source because Lifestone.Game has no test project (M3 3.2 review finding 10): the <c>c1-powerbar</c> frame hides the combat window as the AdvancedCombatUI
/// option does (retail begins an advanced-mode bar only with that option on, which hides the combat window in every mode, so it never draws the two together), both saves say the power bar's placeholder in their log line
/// (CLAUDE.md, Placeholders), and the play-check's <c>c2-spellcasting</c> frame is staged through the combat window, whose notice drives the toolbar's magic button, as the panel shots stage it. What the pictures show (the
/// floaty following the hide, the toolbar's button, the bar at 0.6) is only seen in the frames themselves: <c>play-checks</c> with <c>--play-frames</c> and <c>--play-panel-shots</c> on the lead's machine.
/// </summary>
public sealed class WindowFramesScriptTests
{
    private const string PlayChecksFile = "src/Lifestone.Game/scripts/Session/PlayChecks.cs";
    private const string PanelC1File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC1.cs";
    private const string PanelC2File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC2.cs";
    private const string PanelShotsFile = "src/Lifestone.Game/scripts/Session/PlayMode.PanelShots.cs";

    [Fact]
    public void The_play_check_frames_hide_the_combat_window_for_the_power_bar_and_say_its_placeholder()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelC1File);
        var mode = code.IndexOf("combat.SetCombatMode(frame.CombatMode);", StringComparison.Ordinal);
        var hide = code.IndexOf("if (frame.HideCombatWindow)", StringComparison.Ordinal);
        var hidden = code.IndexOf("combat.SetVisible(false);", StringComparison.Ordinal);
        var begin = code.IndexOf("bar.Begin(PowerBarMode.Advanced, frame.CombatMode);", StringComparison.Ordinal);
        Assert.True(mode >= 0 && hide > mode && hidden > hide && begin > hidden, "the frame takes the mode notice, hides the combat window when the plan says so, then begins the bar");
        Assert.Contains("frame.Name, frame.Placeholder)", code, StringComparison.Ordinal); // the placeholder reaches the save
        Assert.Contains("!frame.HideCombatWindow || !combat.IsShown", code, StringComparison.Ordinal); // a frame that would draw both windows is not saved

        var shared = CheckoutFiles.ReadCSharpCodeAndStrings(PlayChecksFile);
        Assert.Contains("private bool SaveCheckedFrame(string path, string name, string? placeholder = null)", shared, StringComparison.Ordinal);
        Assert.Contains("(\"placeholder\", placeholder)", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_shots_hide_the_combat_window_for_the_power_bar_and_say_its_placeholder()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelShotsFile);
        var mode = code.IndexOf("combat.SetCombatMode(frame.CombatMode);", StringComparison.Ordinal);
        var hide = code.IndexOf("if (frame.HideCombatWindow)", StringComparison.Ordinal);
        var hidden = code.IndexOf("combat.SetVisible(false);", StringComparison.Ordinal);
        var spells = code.IndexOf("spells.SetCombatMode(frame.CombatMode);", StringComparison.Ordinal);
        Assert.True(mode >= 0 && hide > mode && hidden > hide && spells > hidden, "the shot takes the combat window's mode notice, hides it when the plan says so, then the spellcasting window's notice");
        Assert.Contains("frame.Placeholder is { } placeholder", code, StringComparison.Ordinal);
        Assert.Contains("(\"placeholder\", placeholder)", code, StringComparison.Ordinal);
        Assert.Contains("!frame.HideCombatWindow || !combat.IsShown", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_play_check_spellcasting_frame_is_staged_through_the_combat_window_as_the_panel_shots_stage_it()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelC2File);
        var combat = code.IndexOf("combat?.SetCombatMode(frame.CombatMode);", StringComparison.Ordinal);
        var window = code.IndexOf("window.SetCombatMode(frame.CombatMode);", StringComparison.Ordinal);
        var windowBack = code.IndexOf("window.SetCombatMode(restore);", StringComparison.Ordinal);
        var combatBack = code.IndexOf("combat?.SetCombatMode(restoreCombat);", StringComparison.Ordinal);
        Assert.True(combat >= 0 && window > combat, "the combat window's notice (which drives the toolbar's button) goes before the spellcasting window's");
        Assert.True(windowBack > window && combatBack > windowBack, "both notices are put back after the shot");
        Assert.Contains("TODO(m3-combat-window-missing)", code, StringComparison.Ordinal); // a layout without the combat window says so
    }
}
