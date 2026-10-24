using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The Godot side of the combat windows' review frames, pinned in its source because Lifestone.Game has no test project: the <c>c1-powerbar</c> frame needs a real attack on a creature, which only the live hunt leg makes (M4 4.1 t1, D26: the offline runs
/// skip it with a log line naming <c>m4-powerbar-frame-live</c>); nothing begins or levels the power bar by hand (M3's 0.6 placeholder is gone); and the play-check's <c>c2-spellcasting</c> frame is staged through the
/// combat window, whose notice drives the toolbar's magic button, as the panel shots stage it. What the pictures show (the meter part way up, the held items) is only seen in the frames themselves: <c>play-checks</c> with
/// <c>--play-frames</c> and <c>--play-panel-shots</c>.
/// </summary>
public sealed class WindowFramesScriptTests
{
    private const string PanelC1File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC1.cs";
    private const string PanelC2File = "src/Lifestone.Game/scripts/Session/PlayChecks.PanelC2.cs";
    private const string PanelShotsFile = "src/Lifestone.Game/scripts/Session/PlayMode.PanelShots.cs";

    [Fact]
    public void The_play_checks_place_no_creature_for_the_power_frame_and_skip_it_naming_the_live_todo_M4_4_1_t1()
    {
        // D26 (m4-standin-tusker-retire): the offline run has no stand-in creature, so the c1-powerbar frame (a real attack) is skipped with one log line and the live hunt takes it; nothing drives the bar by hand.
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelC1File);
        Assert.Contains("TODO(m4-powerbar-frame-live)", code, StringComparison.Ordinal);
        Assert.Contains("if (frame.ByAttack)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceAttackFrameTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AttackFrameTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StandInTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("medium.BroadcastElementMessage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.Begin(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("bar.SetLevel(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerBarLevel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Placeholder", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_shots_skip_the_power_frame_with_one_line_naming_the_live_todo_and_place_no_creature_M4_4_1_t1()
    {
        var code = CheckoutFiles.ReadCSharpCodeAndStrings(PanelShotsFile);
        var skip = code.IndexOf("TODO(m4-powerbar-frame-live)", StringComparison.Ordinal);
        var branch = code.IndexOf("if (frame.ByAttack)", StringComparison.Ordinal);
        Assert.True(branch >= 0 && skip > branch, "the by-attack frame is skipped with a line naming the live TODO");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, "TODO\\(m4-powerbar-frame-live\\)")); // one line, one place
        Assert.DoesNotContain("AttackFrameTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceAttackFrameTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StandInTusker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PanelShotPhase.AttackCharge", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PanelShotPhase.AttackStow", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Actions.Attack(", code, StringComparison.Ordinal);
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
