using System.Collections.Immutable;
using Lifestone.Client.Movement;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Movement;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M4 P5b lane J2b: the death loop and the fight on the travel seam (an embedded run places nothing when its legs run or recall, and asks with the right purposes), <c>startAt</c>, <c>leaveOnFailure</c>, and D37's one death
/// (no suicide, the undress before it, the lie-down sent through the controller and its echo).
/// </summary>
public sealed partial class DeathScenarioTests
{
    private static int SentPlaces(Rig rig) => rig.R.H.Net.Sends.Count(s => s.Kind == FakeSendKind.Talk && s.Text is { } t && t.Contains("place", StringComparison.Ordinal));

    /// <summary>Steps until the scenario reaches <paramref name="stage"/> (or ends), at most <paramref name="seconds"/>.</summary>
    private static ScenarioStatus RunTo(Rig rig, DeathScenario scenario, string stage, double seconds = 120)
    {
        var end = rig.Now + seconds;
        var status = ScenarioStatus.Running;
        while (status == ScenarioStatus.Running && scenario.Stage != stage && rig.Now < end)
        {
            status = rig.Frame(scenario);
        }

        return status;
    }

    [Fact]
    public void J2b_an_embedded_death_over_recall_and_run_legs_places_nothing_and_travels_with_the_right_purposes()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var travel = new J2bRecordingTravel(p => p == TravelPurpose.Corpse ? TravelMeans.Run : TravelMeans.Recall);
        var options = new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Travel = travel, Fight = false, Rebuff = false };
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death | DeathStages.Corpse, options);

        Assert.Equal(ScenarioStatus.Running, RunTo(rig, scenario, "death-wait"));
        rig.Kill();
        var status = rig.Run(scenario, 300);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
        Assert.Equal(0, SentPlaces(rig)); // no Tools.Place: the corpse leg ran, the way home recalled
        Assert.Empty(rig.Places);
        Assert.Equal([TravelPurpose.Corpse, TravelPurpose.Home], travel.Purposes);
        Assert.Equal(rig.CorpseGuid, travel.Requests[0].ObjectGuid); // the corpse is an object goal
        Assert.Equal(["arriving", "death-mode"], StageLog(rig).Take(2)); // startAt: the description only, then the death; no login, no gear, no bind, no den place
        Assert.DoesNotContain("den-place", StageLog(rig));
        Assert.Contains("corpse-return", StageLog(rig));
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" suicides=0 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void J2b_a_start_at_that_is_not_a_linear_stage_fails_loudly()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death | DeathStages.Vitae, new DeathOptions { StartAt = DeathStages.Vitae, LeaveOnFailure = false });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario, 10));
        Assert.Contains("TODO(m4-death-start-at)", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void J2b_leave_on_failure_false_ends_failed_in_the_world_with_no_den_exit_and_no_log_off_where_true_leaves_the_den()
    {
        var stay = new Rig(buffed: false, startCell: DenArrival);
        var stayed = stay.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false, Seconds = 6 });
        Assert.Equal(ScenarioStatus.Failed, stay.Run(stayed, 60)); // no tusker strikes a mage that was never placed: the 6 s wait fails
        Assert.Contains("did not kill the player", stayed.Failure, StringComparison.Ordinal);
        Assert.Equal(0, SentPlaces(stay));
        Assert.DoesNotContain(stay.R.H.Net.Sends, s => s.Kind == FakeSendKind.LogOff);
        Assert.Equal(0x0105u, stay.R.H.State.Self.Position!.Value.CellId >> 16); // still in the den, in the world

        var leave = new Rig(buffed: false, startCell: DenArrival);
        var left = leave.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, Seconds = 6 });
        Assert.Equal(ScenarioStatus.Failed, leave.Run(left, 60));
        Assert.Equal(1, SentPlaces(leave)); // the default keeps today's den exit
    }

    [Fact]
    public void J2b_the_death_plan_dies_once_with_no_suicide_and_undresses_before_the_wait()
    {
        Assert.False(DeathStages.All.HasFlag(DeathStages.Unbuff)); // D37: no host runs the suicide
        var rig = new Rig(buffed: true);
        const uint Breastplate = 0x80000C01;
        rig.R.H.Describe(new PlayerDescriptionMsg
        {
            Equipped = [new EquippedDesc(Breastplate, (uint)EquipMask.ChestArmor, 0)],
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(NumDeaths, 0),
            Attributes = [.. Enumerable.Range(1, 6).Select(i => new AttributeDesc((uint)i, 0, 55, 0))],
            Vitals = [new VitalDesc(1, 0, 0, 0, 28), new VitalDesc(3, 0, 0, 0, 55), new VitalDesc(5, 0, 0, 0, 55)],
        });
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.All & (DeathStages.Den | DeathStages.Death | DeathStages.Unbuff));

        rig.Run(scenario, 900);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(0, rig.Suicides);
        Assert.Equal(0, scenario.Suicides);
        var fields = scenario.VerdictFields();
        Assert.Contains(" died=1 ", fields, StringComparison.Ordinal);
        Assert.Contains(" deaths=1 ", fields, StringComparison.Ordinal);
        Assert.Contains(" undressed=", fields, StringComparison.Ordinal);
        Assert.Contains(" lie_down=refused:no-controller lie_echo=n/a", fields, StringComparison.Ordinal); // no controller is bound in this rig: loud, not silent
        Assert.DoesNotContain("unbuff", StageLog(rig));
        var lines = Lines(rig).ToList();
        var undress = lines.FindIndex(l => l.StartsWith("undress (D37): 1 worn piece(s)", StringComparison.Ordinal));
        var killed = lines.FindIndex(l => l.StartsWith("death: the tuskers killed the player", StringComparison.Ordinal));
        Assert.True(undress >= 0 && killed > undress, $"undress line {undress}, death line {killed}");
        Assert.Contains(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.PutItemInContainer && s.Guid == Breastplate);
    }

    [Fact]
    public void J2b_a_bound_controller_in_non_combat_lies_down_on_the_wire_and_the_echo_reads_yes()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var (controller, sink) = J2bLieDownKit.Controller();
        rig.R.Api.Move.Bind(controller, runByDefault: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });

        Assert.Equal(ScenarioStatus.Running, RunTo(rig, scenario, "death-wait"));
        for (var i = 0; i < 40 && scenario.LieDown == DeathLegVerdict.NotApplicable; i++)
        {
            rig.Frame(scenario);
        }

        Assert.Equal("sent", scenario.LieDown);
        Assert.Contains(sink.Of(RecordingMovementSink.Kind.MoveToState), m => m.Forward == J2bLieDownKit.Sleeping); // the MoveToState's forward command is Sleeping 0x41000014
        rig.R.H.R.Bus.Publish(new ObjectMotionChanged(WorldActionRig.Player, true, true, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = DeathScenario.LieDownWire }));
        rig.Kill();
        rig.Run(scenario, 120);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal("yes", scenario.LieEcho);
        Assert.Contains(" lie_down=sent lie_echo=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void J2b_the_controller_sends_sleeping_in_non_combat_and_in_the_magic_stance_refuses_it_with_ready_on_the_wire()
    {
        // W0's cause (research section 4): the echo's style 0x0049 was the client's own raw style (ACE MovementData.cs 94-95), the Magic stance, where retail CMotionInterp::DoMotion (11.6096 0x00529930)
        // refuses Sleeping and CommandInterpreter::HandleKeyboardCommand still sends the MoveToState with the raw forward command unchanged.
        var (peace, peaceSink) = J2bLieDownKit.Controller();
        peace.HandleCommand(J2bLieDownKit.Sleeping, start: true);
        Assert.Equal(J2bLieDownKit.Sleeping, peaceSink.Of(RecordingMovementSink.Kind.MoveToState).Last().Forward);

        var (magic, magicSink) = J2bLieDownKit.Controller();
        var notices = new List<MovementNotice>();
        magic.Notice += notices.Add;
        magic.HandleCommand(J2bLieDownKit.MagicStyle, start: true); // the stance a cast leaves the scenario walker in (it follows the cast's TurnTo, never the server's peace motion)
        magic.HandleCommand(J2bLieDownKit.Sleeping, start: true);
        Assert.Equal(MotionCommands.Ready, magicSink.Of(RecordingMovementSink.Kind.MoveToState).Last().Forward);
        Assert.Contains(notices, n => n.Kind == MovementNoticeKind.MotionRefused && n.Error == WeenieError.CantLieDownInCombat);
    }

    [Fact]
    public void J2b_an_embedded_fight_over_a_run_leg_places_nothing_asks_for_a_spot_and_fails_in_the_world()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var travel = new J2bRecordingTravel(_ => TravelMeans.Run);
        var fight = new FightScenario(GearKit.Of(Array.Empty<GearItem>()), new Dictionary<uint, SpellInfo>(), new BuffOptions(), new FightOptions { Seconds = 5, Travel = travel, LeaveOnFailure = false }, new FakeWalk(), rig.Log, endSession: false, stages: FightStages.Place);

        var status = ScenarioStatus.Running;
        for (var i = 0; i < 400 && status == ScenarioStatus.Running; i++)
        {
            rig.R.Tick(0.25);
            rig.R.Tick();
            status = fight.Step(rig.View, rig.Now);
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal([TravelPurpose.Spot], travel.Purposes); // a fight spot in the den, on foot in a journey
        Assert.Equal(0, SentPlaces(rig)); // neither the place nor, with leaveOnFailure false, the den exit
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.LogOff);
        Assert.Contains("stage settle", Lines(rig)); // the run's arrival was authoritative: no teleport was waited for
    }
}
