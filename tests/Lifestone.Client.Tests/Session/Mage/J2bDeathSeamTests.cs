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
using Lifestone.Data.World;
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
    public void J2b_a_start_at_that_is_not_a_linear_stage_fails_loudly()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death | DeathStages.Corpse, new DeathOptions { StartAt = DeathStages.Corpse, LeaveOnFailure = false });

        Assert.Equal(ScenarioStatus.Failed, rig.Run(scenario, 10));
        Assert.Contains("start_at: corpse is not a stage a run can begin at", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("not startable, by design; a death handled elsewhere enters at the respawn through JourneyFacts.ObservedDeath", scenario.Failure, StringComparison.Ordinal); // M6 6 C1: m4-death-start-at closed, reworded
        Assert.DoesNotContain("TODO(m4-death-start-at)", scenario.Failure, StringComparison.Ordinal);
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
        Assert.Contains(" respawn_deaths=1 ", fields, StringComparison.Ordinal);
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

    /// <summary>A movement controller that only records the commands it is given (and keeps no stance): what the Action API's Move intents reach.</summary>
    private sealed class StanceRecordingIntents : IPlayerMovementIntents
    {
        public List<(uint Command, bool Start)> Commands { get; } = [];

        public CellFrame Position => default;

        public bool OnGround => true;

        public void HandleCommand(uint command, bool start, float speed = 1f) => Commands.Add((command, start));

        public void SetHoldRun(bool on)
        {
        }

        public void SetHoldSidestep(bool on)
        {
        }

        public void ToggleAutoRun()
        {
        }

        public void CommenceJump(double now)
        {
        }

        public void DoJump(double now)
        {
        }
    }

    private static void RunToLieDown(Rig rig, DeathScenario scenario)
    {
        Assert.Equal(ScenarioStatus.Running, RunTo(rig, scenario, "death-wait"));
        for (var i = 0; i < 60 && scenario.LieDown == DeathLegVerdict.NotApplicable; i++)
        {
            rig.Frame(scenario);
        }
    }

    [Fact]
    public void M5_3_d4_m4_lie_down_controller_the_walkers_stance_is_synced_to_non_combat_first_and_only_then_Sleeping_is_sent()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var controller = new StanceRecordingIntents();
        rig.R.Api.Move.Bind(controller, runByDefault: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });

        RunToLieDown(rig, scenario);

        Assert.Equal("sent", scenario.LieDown);
        Assert.Equal(1, scenario.StanceSyncs);
        var stance = controller.Commands.IndexOf((CombatStances.NonCombat, true));
        var sleeping = controller.Commands.IndexOf((DeathScenario.LieDownCommand, true));
        Assert.True(stance >= 0 && sleeping > stance, $"stance at {stance}, Sleeping at {sleeping}: " + string.Join(", ", controller.Commands)); // the order: the stance, then Sleeping
        Assert.Contains(Lines(rig), l => l.Contains("stance is synced to non-combat", StringComparison.Ordinal));
    }

    [Fact]
    public void M5_3_d4_m4_lie_down_controller_a_walker_a_cast_left_in_the_Magic_stance_lies_down_on_the_wire_where_it_was_refused_before()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        var (controller, sink) = J2bLieDownKit.Controller();
        var notices = new List<MovementNotice>();
        controller.Notice += notices.Add;
        controller.HandleCommand(J2bLieDownKit.MagicStyle, start: true); // what a cast's TurnTo left the scenario walker in
        rig.R.Api.Move.Bind(controller, runByDefault: false);
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });

        RunToLieDown(rig, scenario);

        Assert.Equal("sent", scenario.LieDown);
        Assert.DoesNotContain(notices, n => n.Kind == MovementNoticeKind.MotionRefused); // retail's DoMotion did not refuse Sleeping (CantLieDownInCombat)
        Assert.Equal(J2bLieDownKit.Sleeping, sink.Of(RecordingMovementSink.Kind.MoveToState).Last().Forward);
    }

    [Fact]
    public void M5_3_d4_m4_lie_down_controller_a_lie_down_the_server_still_echoes_in_the_Magic_style_is_named_cant_lie_down_in_combat()
    {
        var rig = new Rig(buffed: false, startCell: DenArrival);
        rig.R.Api.Move.Bind(new StanceRecordingIntents(), runByDefault: false); // a controller whose stance sync does not take
        var scenario = rig.Scenario(new FakeWalk(), DeathStages.Death, new DeathOptions { StartAt = DeathStages.Death, LeaveOnFailure = false });

        RunToLieDown(rig, scenario);
        rig.R.H.R.Bus.Publish(new ObjectMotionChanged(WorldActionRig.Player, true, true, new ServerMotionState { MovementType = MovementType.Interpreted, Style = 0x0049, Forward = 0x0003 }));
        rig.Kill();
        rig.Run(scenario, 120);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal("no", scenario.LieEcho);
        Assert.Equal("refused:cant-lie-down-in-combat", scenario.LieDown);
        Assert.Contains(Lines(rig), l => l.Contains("CantLieDownInCombat", StringComparison.Ordinal) && l.Contains("0x0049", StringComparison.Ordinal));
        Assert.Contains(" lie_down=refused:cant-lie-down-in-combat lie_echo=no", scenario.VerdictFields(), StringComparison.Ordinal);
    }
}
