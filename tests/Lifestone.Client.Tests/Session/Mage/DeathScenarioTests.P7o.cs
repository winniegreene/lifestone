using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 P7o (D141, check, act, check): the death wait checks the lie-down it made each frame. In the item-end gate the vitals guard's heal took the magic mode during the scripted death and the mage stood among the tuskers, evading almost
/// every swing; a lie-down that the mode or a stand-up undid is restored (non-combat re-synced, the stance and Sleeping again), at most <see cref="DeathScenario.MaxLieRestores"/> times, each logged.
/// </summary>
public sealed partial class DeathScenarioTests
{
    private static void Echo(Rig rig, ushort forward, ushort? style = null) =>
        rig.R.H.R.Bus.Publish(new ObjectMotionChanged(WorldActionRig.Player, true, true, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = forward, Style = style }));

    private static int Sleeps(StanceRecordingIntents controller) => controller.Commands.Count(c => c == (DeathScenario.LieDownCommand, true));

    [Fact]
    public void P7o_a_lie_down_the_magic_mode_undid_is_restored_with_non_combat_re_synced_and_logged()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival) { Lethal = false };
        var controller = new StanceRecordingIntents();
        rig.R.Api.Move.Bind(controller, runByDefault: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });
        RunToLieDown(rig, scenario);
        Assert.Equal("sent", scenario.LieDown);
        Echo(rig, DeathScenario.LieDownWire); // ACE: the player lies
        rig.Frame(scenario);
        Assert.Equal(1, Sleeps(controller));

        rig.R.CombatModeIs(CombatModes.Magic); // what the vitals guard's heal did (the item-end gate)
        var modesBefore = rig.ModeChanges;
        for (var i = 0; i < 12; i++)
        {
            rig.Frame(scenario);
        }

        Assert.Equal(1, scenario.LieRestores);
        Assert.Contains(Lines(rig), l => l.StartsWith(DeathScenario.LieLostLine + " (the combat mode is Magic", StringComparison.Ordinal) && l.Contains("non-combat is re-synced, then the mage lies down again (restore 1 of 5)", StringComparison.Ordinal));
        Assert.True(rig.ModeChanges > modesBefore, "non-combat was asked for again");
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode);
        Assert.Contains(Lines(rig), l => l.Contains("death: check: non-combat again (restore 1 of 5); the lie-down follows", StringComparison.Ordinal));
        Assert.Equal(2, Sleeps(controller)); // Sleeping again
        Echo(rig, DeathScenario.LieDownWire);
        rig.Frame(scenario);
        Assert.Contains(Lines(rig), l => l.Contains("death: check: the lie-down is restored: the server's motion shows Sleeping again (restore 1 of 5)", StringComparison.Ordinal));
        Assert.Equal(1, scenario.LieRestores); // no restore while it holds
    }

    [Fact]
    public void P7o_a_lie_down_a_stand_up_interrupted_is_restored_and_the_restores_are_bounded()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival) { Lethal = false };
        var controller = new StanceRecordingIntents();
        rig.R.Api.Move.Bind(controller, runByDefault: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });
        RunToLieDown(rig, scenario);
        Echo(rig, DeathScenario.LieDownWire);
        rig.Frame(scenario);

        Echo(rig, 0x0003, NonCombatStyle); // the server's motion shows the player standing (Ready) in non-combat
        for (var i = 0; i < 6; i++)
        {
            rig.Frame(scenario);
        }

        Assert.Equal(1, scenario.LieRestores);
        Assert.Contains(Lines(rig), l => l.StartsWith(DeathScenario.LieLostLine + " (the server's motion for the player no longer shows Sleeping (fwd=0x0003 style=0x003D)): the mage lies down again (restore 1 of 5)", StringComparison.Ordinal));
        Assert.Equal(2, Sleeps(controller));

        Echo(rig, WorldObjectTable.DeadMotionLowWord); // the Dead motion is no stand-up
        for (var i = 0; i < 6; i++)
        {
            rig.Frame(scenario);
        }

        Assert.Equal(1, scenario.LieRestores);

        for (var n = 0; n < DeathScenario.MaxLieRestores + 1; n++) // stood up after each lie-down: the bound ends the restores, named
        {
            Echo(rig, DeathScenario.LieDownWire);
            rig.Frame(scenario);
            Echo(rig, 0x0003, NonCombatStyle);
            for (var i = 0; i < 6; i++)
            {
                rig.Frame(scenario);
            }
        }

        Assert.Equal(DeathScenario.MaxLieRestores, scenario.LieRestores);
        Assert.Equal(DeathScenario.MaxLieRestores + 1, Sleeps(controller));
        Assert.Single(Lines(rig), l => l.Contains("after 5 restores: no more restores; the mage waits as it stands", StringComparison.Ordinal));
        Assert.Equal(0, rig.Suicides); // D37: no suicide path
    }

    private const ushort NonCombatStyle = DeathScenario.NonCombatWire;
}
