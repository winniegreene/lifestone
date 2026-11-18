using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 6 MB (map-menu.md 6.2, 6.3): a one-shot task inside the install job, over the scripted ACE of <see cref="AutopilotRig"/> (the den job, the quest station and the logistics trip scripted). Proved: a quest task that is not due stops
/// <c>task-done:quest:&lt;key&gt;:not-due</c> (ok) with no station; a due one runs the quest station <c>from=home</c> once, recalls home and stops <c>task-done:quest:&lt;key&gt;</c> (ok); a failed one stops by the station's own name after
/// the station failure's one recovery; a cut (a death, a watchdog, a reconnect) enters the known state again, whose due check makes the retry safe (a turn-in the cut hid is not made twice; one that was not made is made once more); an errand
/// is one forced trip, then <c>task-done:errand</c>, and its failure stops <c>failed:errand:&lt;why&gt;</c>. The dial keeps the task (<see cref="AutopilotHostLogic.UseTask"/>, refused while running), carries it into every scenario it makes
/// (a hand-back resumes the same task, and the due check prevents a second turn-in), shows it as <see cref="AutopilotStatus.Job"/>, and ends an unattended task with exit code 0. The tusk errand (D58) is unchanged: TrophyQuestTests.
/// </summary>
public sealed class AutopilotTaskTests(ITestOutputHelper output)
{
    private const string Key = "tusk-female";
    private const uint TurnInCell = 0xF682011C; // Brighteyes's room: neither the den nor home (facts.md section 1)
    private static readonly Vector3 TurnInAt = new(175.994f, 30.9674f, 63.605f);
    private static readonly AutopilotTask QuestTask = AutopilotTask.ForQuest(Key);

    private static DateTimeOffset Utc(ApWorld w) => w.Trip.View.Clock.Time.GetUtcNow();

    /// <summary>A den world at home whose character's timers are shared by every context (as the host's file-backed ones), the quest station and the trip scripted.</summary>
    private ApWorld World(Func<StationSpec, ApWorld, IStationRun?>? quest = null, Func<StationSpec, IStationRun?>? trip = null)
    {
        var w = new ApWorld(Den, output) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind switch
        {
            StationKinds.Quest => quest?.Invoke(spec, w),
            StationKinds.Logistics => trip?.Invoke(spec) ?? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2"),
            _ => null,
        };
        return w;
    }

    private static AutopilotScenario Task(ApWorld w, AutopilotTask task, AutopilotOptions? options = null)
    {
        var autopilot = w.Autopilot((options ?? new AutopilotOptions()) with { Task = task });
        autopilot.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        return autopilot;
    }

    /// <summary>The scripted turn-in: the timer recorded (the station's <c>CompleteQuest</c>), the mage left at the turn-in place.</summary>
    private static IStationRun TurnIn(StationSpec spec, ApWorld w) => new ScriptedRun(
        spec.Name,
        _ =>
        {
            w.Quests!.Complete(TrophyQuestTests.TuskFemale(), Utc(w), QuestTimers.HowReply);
            w.Trip.Vendor.StandAt(TurnInCell, TurnInAt);
            return ScenarioStatus.Succeeded;
        },
        () => $"key={spec.Arguments[JourneyPlans.QuestArgument]} from={spec.Arguments[JourneyPlans.FromArgument]} steps=3/3 done=yes xp=+100000 reward_line=100000 timer=saved ok=yes fail=none");

    /// <summary>A quest station that never ends; with <paramref name="record"/> it has turned the quest in (the timer recorded) before it hangs.</summary>
    private static IStationRun Hang(StationSpec spec, ApWorld w, bool record, Action<int>? atStep = null) => new ScriptedRun(
        spec.Name,
        step =>
        {
            if (step == 0 && record)
            {
                w.Quests!.Complete(TrophyQuestTests.TuskFemale(), Utc(w), QuestTimers.HowReply);
            }

            atStep?.Invoke(step);
            return null;
        },
        () => "key=tusk-female from=home steps=0/3 done=no ok=no fail=none");

    private static int Made(ApWorld w, string kind) => w.Factory.Made.Count(s => s.Kind == kind);

    // ------------------------------------------------------------------------------------------------ the quest task

    [Fact]
    public void A_quest_task_not_due_stops_task_done_not_due_with_no_station()
    {
        var w = World((spec, world) => TurnIn(spec, world));
        w.Quests!.Complete(TrophyQuestTests.TuskFemale(), Utc(w).AddHours(-1), QuestTimers.HowReply); // 20 h timer: due again in 19 h
        var autopilot = Task(w, QuestTask);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 6000));
        Assert.Equal("task-done:quest:tusk-female:not-due", autopilot.StopName);
        Assert.True(autopilot.Ok);
        Assert.Equal(0, Made(w, StationKinds.Quest));
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind is StationKinds.Buff or StationKinds.DenFight); // the job's loop is not run
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: quest:tusk-female is not due (quest key=tusk-female", StringComparison.Ordinal));
        Assert.Contains(autopilot.Lines, l => l.StartsWith("autopilot done ok=yes cycles=0 stop=task-done:quest:tusk-female:not-due ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_due_quest_task_runs_the_quest_station_from_home_once_recalls_home_and_stops_task_done()
    {
        var w = World((spec, world) => TurnIn(spec, world));
        var autopilot = Task(w, QuestTask);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 6000));
        Assert.Equal("task-done:quest:tusk-female", autopilot.StopName);
        var quest = Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Quest);
        Assert.Equal((Key, JourneyPlans.FromHome), (quest.Arguments[JourneyPlans.QuestArgument], quest.Arguments[JourneyPlans.FromArgument]));
        var kinds = Kinds(w);
        Assert.Equal("recall-home", kinds[Array.LastIndexOf(kinds, "quest") + 1]); // the recall home before the stop (FinalAttempt)
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind is StationKinds.Buff or StationKinds.DenFight);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: quest:tusk-female begins at home (the station from=home)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot quest: ended key=tusk-female ok=yes done=yes", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: quest:tusk-female done (", StringComparison.Ordinal));
        Assert.Contains(autopilot.Lines, l => l.StartsWith("autopilot done ok=yes cycles=0 stop=task-done:quest:tusk-female ", StringComparison.Ordinal));
        Assert.Equal(AutopilotPlace.Home, AutopilotScenario.Classify(w.Trip.Vendor.State, w.Ground));
        Assert.False(w.Quests!.IsDue(TrophyQuestTests.TuskFemale(), Utc(w)));
    }

    [Fact]
    public void A_failed_quest_task_stops_by_the_stations_own_name_after_its_one_recovery()
    {
        var w = World((spec, _) => new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => "key=tusk-female from=home steps=1/3 done=no xp=+0 reward_line=0 timer=none ok=no fail=go:route") { Failure = "go:route: no route" });
        var autopilot = Task(w, QuestTask);

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot, null, 6000));
        Assert.Equal("failed:quest:tusk-female:go:route", autopilot.StopName);
        Assert.Equal(1, Made(w, StationKinds.Quest)); // never a loop
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery after quest:tusk-female:go:route: the quest station tusk-female failed", StringComparison.Ordinal));
        Assert.DoesNotContain("recall-home", Kinds(w)); // M6 7 BC: at home the recovery runs no recall home (AtHome), the known state classifies
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: at-home:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_quest_task_cut_by_a_death_after_its_turn_in_is_not_made_again_the_due_check_ends_it()
    {
        var w = World((spec, world) => Hang(spec, world, record: true, step =>
        {
            if (step == 1)
            {
                world.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain on the way home!", 0, DeathNoticeSource.VictimNotification)); // a death the autopilot did not script, after the turn-in
            }
        }));
        var autopilot = Task(w, QuestTask);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 8000));
        var kinds = Kinds(w);
        Assert.Equal("death", kinds[Array.IndexOf(kinds, "quest") + 1]); // the death station first
        Assert.Equal(1, Made(w, StationKinds.Quest)); // the timer says it was turned in: never twice
        Assert.Equal("task-done:quest:tusk-female:not-due", autopilot.StopName);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: quest:tusk-female was cut: the known state's due check decides", StringComparison.Ordinal));
    }

    [Fact]
    public void A_quest_task_cut_by_the_watchdog_before_its_turn_in_runs_once_more_and_ends_task_done()
    {
        var w = World((spec, world) => Made(world, StationKinds.Quest) == 1 ? Hang(spec, world, record: false) : TurnIn(spec, world));
        var autopilot = Task(w, QuestTask, new AutopilotOptions { WatchdogScale = 0.05 });

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 12000));
        Assert.Equal(2, Made(w, StationKinds.Quest)); // the cut one, then the one retry the due check allowed
        Assert.Equal("task-done:quest:tusk-female", autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("autopilot state trip -> known-state (cycle 1): watchdog recovery after the trip state went over its bound", StringComparison.Ordinal)); // the cut
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: quest:tusk-female was cut", StringComparison.Ordinal));
    }

    [Fact]
    public void A_quest_task_cut_twice_stops_by_name_never_a_loop()
    {
        var w = World((spec, world) => Hang(spec, world, record: false));
        var autopilot = Task(w, QuestTask, new AutopilotOptions { WatchdogScale = 0.05, WatchdogRecoveries = 5 });

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot, null, 20000));
        Assert.Equal("failed:quest:tusk-female:cut-again", autopilot.StopName);
        Assert.Equal(1 + AutopilotScenario.TaskRunsAgain, Made(w, StationKinds.Quest));
    }

    /// <summary>The host's double for a drop (AutopilotResumeTests' Host, trimmed): the world left, then the login after a few steps.</summary>
    private sealed class ReconnectHost(ApWorld w) : IAutopilotSession
    {
        private int _steps;

        public int Reconnects { get; private set; }

        public string? LoginFailure => null;

        public void Reconnect()
        {
            Reconnects++;
            _steps = 0;
            w.LeaveWorld();
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (++_steps < 4)
            {
                return ScenarioStatus.Running;
            }

            w.Trip.Vendor.H.Phase(SessionPhase.Idle, SessionPhase.InWorld);
            w.Relogin();
            return ScenarioStatus.Succeeded;
        }
    }

    [Fact]
    public void A_quest_task_cut_by_a_reconnect_after_its_turn_in_enters_the_known_state_and_the_due_check_ends_it()
    {
        var w = World((spec, world) => Hang(spec, world, record: true));
        var host = new ReconnectHost(w);
        var autopilot = w.Autopilot(new AutopilotOptions { Task = QuestTask }, host: host);
        autopilot.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        w.Run(autopilot, a => a.State == AutopilotState.Trip && !w.Quests!.IsDue(TrophyQuestTests.TuskFemale(), Utc(w)), 6000); // the station turned it in (the timer is recorded) and hangs
        w.Drop();

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 8000));
        Assert.Equal(1, host.Reconnects);
        Assert.Equal(1, Made(w, StationKinds.Quest));
        Assert.Equal("task-done:quest:tusk-female:not-due", autopilot.StopName);
    }

    // ------------------------------------------------------------------------------------------------ the errand task

    [Fact]
    public void An_errand_task_makes_one_forced_trip_then_stops_task_done_errand()
    {
        var w = World();
        var autopilot = Task(w, AutopilotTask.Errand);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 6000));
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        var trip = Assert.Single(w.Factory.Made, s => s.Kind == StationKinds.Logistics);
        Assert.Equal(JourneyPlans.TripForced, trip.Arguments[JourneyPlans.TripArgument]);
        Assert.DoesNotContain(w.Factory.Made, s => s.Kind is StationKinds.Buff or StationKinds.DenFight or StationKinds.Quest);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: errand begins", StringComparison.Ordinal));
        Assert.Contains(autopilot.Lines, l => l.StartsWith("autopilot done ok=yes cycles=1 stop=task-done:errand ", StringComparison.Ordinal));
    }

    /// <summary>M6 6 ME (MB's open point): the carry holds the task's "done": an errand handed back during its recall home goes to the stop, it does not make a second trip.</summary>
    [Fact]
    public void An_errand_handed_back_during_its_recall_home_makes_no_second_trip()
    {
        var w = new ApWorld(Den, output, atHome: false) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        var first = Task(w, AutopilotTask.Errand);
        w.Run(first, a => a.TaskDone && a.State == AutopilotState.Travel, 6000); // the trip was made; the mage is away from home, the recall before the stop is in hand
        Assert.True(first.TaskDone);
        Assert.Equal(1, Made(w, StationKinds.Logistics));
        first.Abandon("interrupt"); // the person's key press, then /auto: the hand-back
        var seed = first.Seed;
        Assert.True(seed.TaskDone);

        var second = new AutopilotScenario(new AutopilotOptions { Task = AutopilotTask.Errand }, w.Factory, w.Context(), null, null, seed);
        second.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(second, null, 6000));
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, second.StopName);
        Assert.Equal(1, Made(w, StationKinds.Logistics)); // no second trip
        Assert.Contains(w.Lines, l => l.Contains("the work was done before the hand-back; only the recall home and the stop are left", StringComparison.Ordinal));
        Assert.Equal("recall-home", w.Factory.Made[^1].Kind);
    }

    [Fact]
    public void A_failed_errand_stops_failed_errand_by_the_trips_reason()
    {
        var w = World(trip: spec => new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => "sold=0 bought=0") { Failure = "vendor:closed: the vendor would not trade" });
        var autopilot = Task(w, AutopilotTask.Errand);

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot, null, 8000));
        Assert.StartsWith("failed:errand:", autopilot.StopName, StringComparison.Ordinal);
        Assert.InRange(Made(w, StationKinds.Logistics), 1, 1 + AutopilotScenario.TaskRunsAgain); // the trip's one retry at most
        Assert.Contains(autopilot.Lines, l => l.StartsWith("autopilot done ok=no ", StringComparison.Ordinal) && l.Contains(" stop=failed:errand:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the dial: the carry, the hand-back, the status, the unattended end

    private sealed class Dial
    {
        public Dial(ApWorld w, AutopilotOptions options)
        {
            World = w;
            Host = new AutopilotHostLogic(
                carry =>
                {
                    var made = carry.Remaining(options);
                    Made.Add(made);
                    var scenario = new AutopilotScenario(made, w.Factory, w.Context(), null, null, carry.Seed);
                    scenario.TrophyQuests = [TrophyQuestTests.TuskFemale()];
                    carry.Apply(scenario);
                    return scenario;
                },
                w.Trip.Context().Log,
                options,
                w.Ground);
        }

        public ApWorld World { get; }

        public AutopilotHostLogic Host { get; }

        public List<AutopilotOptions> Made { get; } = [];

        public IGameSessionView View => World.Trip.View;

        public bool RunUntil(Func<bool> reached, int frames = 12000)
        {
            for (var i = 0; i < frames && !reached(); i++)
            {
                var t = World.Trip;
                t.Vendor.R.Tick(0.25);
                t.Shops.Pump();
                t.Xp.Pump();
                t.Vendor.R.Tick();
                Host.Tick(View, World.Now);
            }

            return reached();
        }
    }

    [Fact]
    public void The_carry_gives_its_task_to_the_options_and_none_keeps_the_options_own()
    {
        var options = new AutopilotOptions { Task = AutopilotTask.Errand };
        Assert.Equal(AutopilotTask.Errand, AutopilotCarry.None.Remaining(options).Task);
        Assert.Equal(QuestTask, (AutopilotCarry.None with { Task = QuestTask }).Remaining(options).Task);
        Assert.Equal(AutopilotTask.Job, (AutopilotCarry.None with { Task = AutopilotTask.Job }).Remaining(options).Task);
    }

    [Fact]
    public void The_dial_keeps_the_task_refuses_a_change_while_running_and_shows_it_as_the_job()
    {
        var dial = new Dial(World((spec, world) => Hang(spec, world, record: false)), new AutopilotOptions());
        Assert.Equal("hunt here", dial.Host.Status.Job); // the den job's own loop
        Assert.Equal(AutopilotTask.Job, dial.Host.Task);
        Assert.NotNull(dial.Host.UseTask(AutopilotTask.ForQuest("no-such-quest")));
        Assert.Null(dial.Host.UseTask(QuestTask)); // off: allowed
        Assert.Equal("do a quest: tusk-female", dial.Host.Status.Job);

        Assert.True(dial.Host.Start(dial.View));
        Assert.Equal(QuestTask, dial.Made[^1].Task);
        Assert.True(dial.RunUntil(() => dial.Host.Scenario is { TaskBegun: true }));
        Assert.StartsWith("the autopilot is running", dial.Host.UseTask(AutopilotTask.Errand), StringComparison.Ordinal);
        Assert.Equal(QuestTask, dial.Host.Task);

        dial.Host.StopHere(dial.View); // interrupted: allowed again
        Assert.Null(dial.Host.UseTask(AutopilotTask.Errand, AutopilotHostLogic.TaskByChat));
        Assert.Equal("errand", dial.Host.Status.Job);
        Assert.Contains(dial.World.Lines, l => l == "autopilot task: quest:tusk-female chosen by menu");
        Assert.Contains(dial.World.Lines, l => l == "autopilot task: errand chosen by chat");
        Assert.Contains(dial.World.Lines, l => l.StartsWith("autopilot task: errand refused", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hand_back_resumes_the_same_task_and_the_due_check_prevents_a_second_turn_in()
    {
        var dial = new Dial(World((spec, world) => Hang(spec, world, record: true)), new AutopilotOptions { Task = QuestTask });
        Assert.Contains(dial.World.Lines, l => l == "autopilot task: quest:tusk-female chosen by option");
        Assert.True(dial.Host.Start(dial.View));
        Assert.True(dial.RunUntil(() => dial.Host.Scenario is { TaskBegun: true } && Made(dial.World, StationKinds.Quest) == 1));
        dial.RunUntil(() => false, 8); // the station turns the quest in (the timer is recorded), then hangs
        dial.Host.StopHere(dial.View);
        Assert.Equal(AutopilotMode.Interrupted, dial.Host.Mode);

        dial.Host.Toggle(dial.View, reason: AutopilotHostLogic.ResumeReason); // /auto: the hand-back

        Assert.True(dial.RunUntil(() => dial.Host.Mode == AutopilotMode.Done));
        Assert.Equal(2, dial.Made.Count);
        Assert.All(dial.Made, o => Assert.Equal(QuestTask, o.Task)); // the start and the hand-back ran the same task
        Assert.Equal(1, Made(dial.World, StationKinds.Quest)); // never a second turn-in
        Assert.Equal("task-done:quest:tusk-female:not-due", dial.Host.Scenario!.StopName);
        Assert.Contains(dial.World.Lines, l => l == "autopilot host: the hand-back runs the task quest:tusk-female (do a quest: tusk-female)");
    }

    [Fact]
    public void An_unattended_task_that_is_done_ends_with_exit_code_0()
    {
        var dial = new Dial(World(), new AutopilotOptions { Task = AutopilotTask.Errand });
        Assert.True(dial.Host.Start(dial.View, "--autopilot-start"));

        Assert.True(dial.RunUntil(() => dial.Host.Mode == AutopilotMode.Done));
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, dial.Host.Scenario!.StopName);
        Assert.Equal(0, dial.Host.UnattendedEnd!.ExitCode);
        Assert.Contains("the task is done", dial.Host.UnattendedEnd.Decision, StringComparison.Ordinal);
        Assert.True(AutopilotScenario.IsTaskDone("task-done:quest:tusk-female:not-due"));
        Assert.False(AutopilotScenario.IsTaskDone("failed:errand:cut-again"));
    }
    /// <summary>
    /// M6 6 F2 (MB's open point 1, M5 D88): an errand begun on the ground with an attacker heard and in reach gets the job's fight-back first; once no attack is heard for the quiet time the fight stops by
    /// <see cref="AutopilotScenario.TaskFightBackDone"/> and the known state begins the errand.
    /// </summary>
    [Fact]
    public void A_task_begun_on_the_ground_with_an_attacker_on_him_fights_back_first_then_begins()
    {
        var w = new ApWorld(Den, output, atHome: false) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(0x80000F2A, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + 6, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
        var autopilot = Task(w, AutopilotTask.Errand);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        var hits = 0;
        var status = w.Run(
            autopilot,
            null,
            8000,
            a =>
            {
                if (hits < 12 && a.State != AutopilotState.Trip)
                {
                    hits++;
                    w.Trip.Vendor.R.Server((p, seq) => Lifestone.Tests.Shared.Net.AceM3Writers.DefenderNotification(p, seq, "Tusker Guard", 4, 0.1, 3, 4, false, 0)); // heard for the first 3 s
                }
            });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        var kinds = Kinds(w);
        Assert.True(kinds.IndexOf("den-fight") >= 0 && kinds.IndexOf("den-fight") < kinds.IndexOf("logistics"), string.Join(",", kinds)); // the fight first, then the errand's trip
        Assert.Contains(arrows, a => a.To == AutopilotState.Fight && a.Why.Contains("the fight fights back first (D88)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (task-fight-back)", StringComparison.Ordinal));
        Assert.Contains(arrows, a => a.To == AutopilotState.KnownState && a.Why.StartsWith("the task's fight-back is over", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: errand begins", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 16 Q2: a hit heard before the scenario was built (the session's threat board heard it; the scenario's watch did not), its attacker 6 m away. Before, the task's fight-back read the watch's own last attack and saw none,
    /// though the outer loop's InReach said attacked; now it reads the board's last hit, as InReach does, and the errand waits for the fight-back first, its words naming the hit.
    /// </summary>
    [Fact]
    public void A_task_begun_after_a_hit_heard_before_the_scenario_was_built_fights_back_first()
    {
        var w = GroundErrandWorld(output);
        PutCreature(w, 0x80000F2B);
        _ = Lifestone.Client.Session.Threat.ThreatBoard.For(w.Trip.View); // the session's board hears from before the scenario
        w.Trip.Vendor.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseHit, "Tusker Guard", 4, 3, 0.1, false, 4, false, 0, "Tusker Guard grazes your lower arm for 3 points of bludgeoning damage!"));
        var autopilot = Task(w, AutopilotTask.Errand);
        w.Run(autopilot, a => a.State == AutopilotState.Fight || a.State == AutopilotState.Trip, 4000);

        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: errand waits: an attacker is on it (Tusker Guard attacked ", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ M6 6 FXM1: the task's fight-back bounded (6-M1), the errand's own resume (6-M3, 6-M4), a task only at home or on the ground (6-M5)

    /// <summary>A living target creature of the ground ("Tusker Guard") 6 m from the mage.</summary>
    private static void PutCreature(ApWorld w, uint guid)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + 6, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    private static void Hit(ApWorld w) =>
        w.Trip.Vendor.R.Server((p, seq) => Lifestone.Tests.Shared.Net.AceM3Writers.DefenderNotification(p, seq, "Tusker Guard", 4, 0.1, 3, 4, false, 0));

    private static ApWorld GroundErrandWorld(ITestOutputHelper output)
    {
        var w = new ApWorld(Den, output, atHome: false) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        return w;
    }

    /// <summary>
    /// 6-M1 (blocker): on a busy ground the fight keeps drawing attacks (a defender notification every 2 s for 60 s, the creature alive in reach), so the attack never goes quiet: the task's fight-back stops at its cap
    /// <see cref="AutopilotScenario.TaskFightBackMaxSeconds"/> (logged), the errand begins while the attacks still come, and no second fight-back runs before the stop.
    /// </summary>
    [Fact]
    public void A_task_fight_back_on_a_busy_ground_stops_at_its_cap_and_the_task_begins()
    {
        var w = GroundErrandWorld(output);
        PutCreature(w, 0x80000F2B);
        var autopilot = Task(w, AutopilotTask.Errand);
        var frame = 0;
        var start = w.Now;
        double? fightAt = null, tripAt = null;
        var status = w.Run(
            autopilot,
            null,
            8000,
            a =>
            {
                if (frame < 240 && (frame < 4 || frame % 8 == 0))
                {
                    Hit(w); // every 2 s for 60 s, whatever the state (each of the first frames too: the watch is made at the first step)
                }

                frame++;
                fightAt ??= a.State == AutopilotState.Fight ? w.Now : null;
                tripAt ??= a.State == AutopilotState.Trip ? w.Now : null;
            });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("the fight-back reached its cap of 45 s (TaskFightBackMaxSeconds;", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (task-fight-back)", StringComparison.Ordinal));
        Assert.NotNull(fightAt);
        Assert.NotNull(tripAt);
        Assert.InRange(tripAt!.Value - fightAt!.Value, AutopilotScenario.TaskFightBackMaxSeconds, AutopilotScenario.TaskFightBackMaxSeconds + 5); // the task began at the cap,
        Assert.True(tripAt.Value - start < 60, "the errand began while the attacks still came"); // not when they ended
        Assert.Equal(1, Made(w, StationKinds.DenFight)); // one fight-back, never the job's hunt again before the stop
        Assert.Equal(1, Made(w, StationKinds.Logistics));
    }

    /// <summary>6-M1 ("defend only"): once the attacker recorded at the start is gone and no attack came for <see cref="AutopilotScenario.TaskFightBackLullSeconds"/>, the fight stops at this lull, before the quiet time.</summary>
    [Fact]
    public void A_task_fight_back_stops_at_the_first_lull_once_its_attacker_is_gone()
    {
        var w = GroundErrandWorld(output);
        const uint Guard = 0x80000F2C;
        PutCreature(w, Guard);
        var autopilot = Task(w, AutopilotTask.Errand);
        var frame = 0;
        double lastHit = 0;
        double? overAt = null;
        var fought = false;
        var status = w.Run(
            autopilot,
            null,
            8000,
            a =>
            {
                if (frame <= 24 && (frame < 4 || frame % 8 == 0))
                {
                    Hit(w); // for the first 6 s
                    lastHit = w.Now;
                }

                if (frame == 26)
                {
                    w.Trip.Vendor.H.R.Reducer.OnDeleteObject(w.Trip.Vendor.H.R.Context, new DeleteObjectMsg(Guard, 1)); // the attacker is gone
                }

                frame++;
                fought |= a.State == AutopilotState.Fight;
                overAt ??= fought && a.State != AutopilotState.Fight ? w.Now : null;
            });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("(defend only): its attacker Tusker Guard 0x80000F2C is gone from view and no attack for", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("the fight-back is over", StringComparison.Ordinal) && l.Contains("no attack heard for", StringComparison.Ordinal));
        Assert.NotNull(overAt);
        Assert.InRange(overAt!.Value - lastHit, AutopilotScenario.TaskFightBackLullSeconds, HostileWatch.AttackQuietSeconds); // the lull, not the quiet time
    }

    /// <summary>
    /// 6-M3 and 6-M5: an errand's trip cut by a drop while the mage stands away from home and the ground: the known state elsewhere decides nothing for the task (no cut counted, the recall home first), and at home, after the audit,
    /// the trip runs again once, counted once; the run ends <c>task-done:errand</c>, never <c>failed:errand:cut-again</c>.
    /// </summary>
    [Fact]
    public void An_errand_cut_away_from_home_runs_its_trip_again_once_at_home_counted_once()
    {
        ApWorld? world = null;
        var trips = 0;
        var w = world = World(trip: spec => trips++ == 0
            ? new ScriptedRun(
                spec.Name,
                _ =>
                {
                    world!.Trip.Vendor.StandAt(TurnInCell, TurnInAt); // the trip took him away from home, then hangs
                    return null;
                },
                () => "sold=0 bought=0")
            : new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2"));
        var host = new ReconnectHost(w);
        var autopilot = w.Autopilot(new AutopilotOptions { Task = AutopilotTask.Errand }, host: host);
        autopilot.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        w.Run(autopilot, a => a.State == AutopilotState.Trip && AutopilotScenario.Classify(w.Trip.Vendor.State, w.Ground) == AutopilotPlace.Elsewhere, 6000);
        w.Drop();

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 8000));
        Assert.Equal(1, host.Reconnects);
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        Assert.Equal(2, Made(w, StationKinds.Logistics)); // the cut one, then the one run again
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot task: errand waits: it is elsewhere", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("autopilot task: errand: the trip was cut or failed before it ended: it runs again from its start at home (run again 1 of 1)", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("cut-again", StringComparison.Ordinal));
    }

    /// <summary>
    /// 6-M4: an errand's trip cut on the ground with a hostile in reach (the audit's attacker, no attack heard) gets the task's bounded fight-back (it ends by <see cref="AutopilotScenario.TaskFightBackDone"/>), never the job's open
    /// fight; then its trip runs again and the run ends <c>task-done:errand</c>.
    /// </summary>
    [Fact]
    public void An_errand_cut_on_the_ground_with_a_hostile_in_reach_fights_back_as_the_task_then_runs_its_trip_again()
    {
        var trips = 0;
        var w = new ApWorld(Den, output, atHome: false) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind != StationKinds.Logistics ? null : trips++ == 0
            ? new ScriptedRun(spec.Name, _ => null, () => "sold=0 bought=0") // hangs on the ground
            : new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2");
        var host = new ReconnectHost(w);
        var autopilot = w.Autopilot(new AutopilotOptions { Task = AutopilotTask.Errand }, host: host);
        autopilot.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => a.State == AutopilotState.Trip, 6000);
        w.Drop();
        var placed = false;

        var status = w.Run(
            autopilot,
            null,
            8000,
            _ =>
            {
                if (!placed && host.Reconnects == 1 && w.Trip.View.State.Self.Position is not null)
                {
                    placed = true;
                    PutCreature(w, 0x80000F2D); // after the login, a creature of the ground stands 6 m away
                }
            });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, autopilot.StopName);
        Assert.Equal(["logistics", "den-fight", "logistics"], Kinds(w).Where(k => k is "logistics" or "den-fight").ToArray());
        Assert.Contains(arrows, a => a.To == AutopilotState.Fight && a.Why.Contains("audit: a hostile is on it (", StringComparison.Ordinal) && a.Why.Contains("the errand's trip is to run again", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("stop asked (task-fight-back)", StringComparison.Ordinal));
        Assert.Single(w.Lines, l => l.StartsWith("autopilot task: errand: the trip was cut or failed before it ended: it runs again from its start at ground (run again 1 of 1)", StringComparison.Ordinal));
    }

    /// <summary>6-M5: a quest task found away from home and the ground decides nothing there: the recall home first, then the audit from the top at home, then the station (its begin line names home).</summary>
    [Fact]
    public void A_quest_task_found_elsewhere_recalls_home_and_the_audit_finishes_before_it_begins()
    {
        var w = World((spec, world) => TurnIn(spec, world));
        w.Trip.Vendor.StandAt(TurnInCell, TurnInAt);
        var autopilot = Task(w, QuestTask);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, null, 8000));
        Assert.Equal("task-done:quest:tusk-female", autopilot.StopName);
        var kinds = Kinds(w);
        var quest = Array.IndexOf(kinds, "quest");
        Assert.True(quest > 0 && kinds[..quest].Contains("recall-home"), string.Join(",", kinds)); // home first
        var lines = w.Lines.ToList();
        var waits = lines.FindIndex(l => l.StartsWith("autopilot task: quest:tusk-female waits: it is elsewhere", StringComparison.Ordinal));
        var begins = lines.FindIndex(l => l.StartsWith("autopilot task: quest:tusk-female begins at home (the station from=home)", StringComparison.Ordinal));
        var homeAudit = lines.FindLastIndex(begins < 0 ? 0 : begins, l => l.StartsWith(StartupAudit.LinePrefix, StringComparison.Ordinal) && l.Contains("place", StringComparison.Ordinal) && l.Contains("home", StringComparison.Ordinal));
        Assert.True(waits >= 0 && begins > waits, "the task waited elsewhere, then began at home");
        Assert.True(homeAudit > waits && homeAudit < begins, "the audit's place check at home ran before the task began");
    }
}
