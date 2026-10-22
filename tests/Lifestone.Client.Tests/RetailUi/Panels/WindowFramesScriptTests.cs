using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The Godot side of the combat windows' review frames, pinned in its source because Lifestone.Game has no test project: the <c>c1-powerbar</c> frame is taken during a real attack (M4 1.3 s1: the stand-in's tusker selected, the Medium
/// button pressed and clicked through the UI's own element messages) and nothing begins or levels the power bar by hand any more (M3's 0.6 placeholder is gone); and the play-check's <c>c2-spellcasting</c> frame is staged through the
/// combat window, whose notice drives the toolbar's magic button, as the panel shots stage it. What the pictures show (the meter part way up, the held items) is only seen in the frames themselves: <c>play-checks</c> with
/// <c>--play-frames</c> and <c>--play-panel-shots</c>.
/// </summary>
public sealed class WindowFramesScriptTests
{
    private const string PanelC1File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC1.cs";
    private const string PanelC2File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC2.cs";
    private const string PanelShotsFile = "src/Lifestone.Game/scripts/Session/PlayMode.PanelShots.cs";

    [Fact]
    public void The_play_check_frame_is_taken_during_a_real_attack_and_nothing_drives_the_bar_by_hand()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelC1File);
        var press = code.IndexOf("medium.BroadcastElementMessage(UiMessageIds.MouseDown);", StringComparison.Ordinal);
        var click = code.IndexOf("medium.BroadcastElementMessage(UiMessageIds.ButtonClick);", StringComparison.Ordinal);
        var shot = code.IndexOf("SaveCheckedFrame($\"{folder}/{PanelFramesPlan.FileName(frame)}\", frame.Name)", StringComparison.Ordinal);
        Assert.True(press >= 0 && click > press && shot > click, "the Medium button is pressed and clicked, then the frame is taken while the power charges");
        Assert.Contains("PanelFramesPlan.PlaceAttackFrameTusker(session, server);", code, StringComparison.Ordinal);
        Assert.Contains("_mode.Selection.Select(PanelFramesPlan.AttackFrameTusker);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.Begin(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.SetLevel(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerBarLevel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Placeholder", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_shots_take_the_power_frame_during_a_real_attack_and_nothing_drives_the_bar_by_hand()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelShotsFile);
        var select = code.IndexOf("Selection.Select(PanelFramesPlan.AttackFrameTusker);", StringComparison.Ordinal);
        var press = code.IndexOf("medium?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.MouseDown);", StringComparison.Ordinal);
        var click = code.IndexOf("medium?.BroadcastElementMessage(Lifestone.Client.RetailUi.Core.UiMessageIds.ButtonClick);", StringComparison.Ordinal);
        Assert.True(select >= 0 && press > select && click > press, "the tusker is selected, then the Medium button pressed and clicked");
        Assert.Contains("PanelShotPhase.AttackCharge", code, StringComparison.Ordinal);
        Assert.Contains("TODO(m4-powerbar-frame-live)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.Begin(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.SetLevel(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerBarLevel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Placeholder", code, StringComparison.Ordinal);
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
