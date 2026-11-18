using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 6 FXM2 (review-menu 6-M2, 6-M7, 6-M9): a hand-back that changes the task carries neither the old task's done mark nor its cut trip (the seed names its task); a hand-back keeps that the cut trip had done work, so a skipped
/// by-counters trip still counts as the errand's trip; <c>/auto quest &lt;key&gt;</c> and <c>--autopilot-task</c> are judged by the menu's own rule (<c>ladder-quest</c>, <c>level-out-of-band</c>, <c>done-once</c>), while <c>not-due</c> stays the scenario's.
/// </summary>
public sealed class AutopilotFxm2Tests(ITestOutputHelper output)
{
    private const string Key = "tusk-female";
    private const uint TurnInCell = 0xF682011C;
    private static readonly Vector3 TurnInAt = new(175.994f, 30.9674f, 63.605f);
    private static readonly AutopilotTask QuestTask = AutopilotTask.ForQuest(Key);
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Utc(ApWorld w) => w.Trip.View.Clock.Time.GetUtcNow();

    private ApWorld World(Func<StationSpec, IStationRun?>? trip = null)
    {
        var w = new ApWorld(Den, output, atHome: false) { Quests = new QuestTimers("BSD Mage") };
        w.Factory.Script = spec => spec.Kind switch
        {
            StationKinds.Quest => new ScriptedRun(
                spec.Name,
                _ =>
                {
                    w.Quests!.Complete(TrophyQuestTests.TuskFemale(), Utc(w), QuestTimers.HowReply);
                    w.Trip.Vendor.StandAt(TurnInCell, TurnInAt);
                    return ScenarioStatus.Succeeded;
                },
                () => $"key={spec.Arguments[JourneyPlans.QuestArgument]} from={spec.Arguments[JourneyPlans.FromArgument]} steps=3/3 done=yes xp=+100000 reward_line=100000 timer=saved ok=yes fail=none"),
            StationKinds.Logistics => trip?.Invoke(spec) ?? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2"),
            _ => null,
        };
        return w;
    }

    private static AutopilotScenario Seeded(ApWorld w, AutopilotTask task, AutopilotSeed seed)
    {
        var scenario = new AutopilotScenario(new AutopilotOptions { Task = task }, w.Factory, w.Context(), null, null, seed);
        scenario.TrophyQuests = [TrophyQuestTests.TuskFemale()];
        return scenario;
    }

    private static int Made(ApWorld w, string kind) => w.Factory.Made.Count(s => s.Kind == kind);

    [Fact]
    public void A_hand_back_that_changes_the_task_does_not_carry_the_old_tasks_done_and_the_quest_runs()
    {
        var w = World();
        var errand = new AutopilotScenario(new AutopilotOptions { Task = AutopilotTask.Errand }, w.Factory, w.Context());
        w.Run(errand, a => a.TaskDone && a.State == AutopilotState.Travel, 6000); // the errand's trip was made, the recall home is in hand
        Assert.True(errand.TaskDone);
        errand.Abandon("interrupt");
        var seed = errand.Seed;
        Assert.True(seed.TaskDone);
        Assert.Equal("errand", seed.TaskName);

        var quest = Seeded(w, QuestTask, seed); // the person chose a quest instead
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(quest, null, 6000));

        Assert.Equal("task-done:quest:tusk-female", quest.StopName);
        Assert.Equal(1, Made(w, StationKinds.Quest)); // the quest station ran: no false task-done from the errand's mark
        Assert.Contains(w.Lines, l => l.Contains("the hand-back changed the task from errand", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("the work was done before the hand-back", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cut_errand_trip_does_not_cross_into_a_quest_task_but_the_same_errand_keeps_it()
    {
        var w = World();
        var cut = new AutopilotSeed(1, 0, PendingTrip: JourneyStations.TripByCounters, TaskName: "errand");
        var quest = Seeded(w, QuestTask, cut);
        Assert.Null(quest.Seed.PendingTrip); // dropped for a quest task
        var errand = Seeded(w, AutopilotTask.Errand, cut);
        Assert.Equal(JourneyStations.TripByCounters, errand.Seed.PendingTrip); // kept for the same task
    }

    [Fact]
    public void A_hand_back_keeps_that_the_cut_trip_had_done_work_so_a_skipped_trip_counts_as_the_errands_trip()
    {
        var w = World(spec => new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "skipped=nothing-due sold=0 bought=0"));
        var seed = new AutopilotSeed(1, 0, PendingTrip: JourneyStations.TripByCounters, TripCutWork: true, TaskName: "errand");
        var errand = Seeded(w, AutopilotTask.Errand, seed);
        Assert.True(errand.Seed.TripCutWork);

        Assert.Equal(ScenarioStatus.Succeeded, w.Run(errand, null, 6000));

        Assert.Equal(AutopilotScenario.TaskDoneErrandStop, errand.StopName); // not failed:errand:given-up
        Assert.True(errand.Ok);
    }

    // ------------------------------------------------------------------------------------------------ 6-M9: one rule for the menu, the chat and the option

    private AutopilotHostLogic Logic(ApWorld w, AutopilotOptions options, JobMenuInput? input)
    {
        var logic = new AutopilotHostLogic(carry => new AutopilotScenario(carry.Remaining(options), w.Factory, w.Context(), null, null, carry.Seed), w.Trip.Context().Log, options, w.Ground);
        logic.MenuInput = () => input;
        return logic;
    }

    /// <summary>The trophy fixture with a level band that fits (the fixture itself names none).</summary>
    private static Quest Banded() => TrophyQuestTests.TuskFemale() with { Level = new QuestLevel(1, null) };

    private static JobMenuInput Input(Quest quest, int level = 30, QuestTimers? timers = null, params string[] ladder) =>
        new(AutopilotJob.LevelUp, level, 1_000_000, [quest], timers ?? new QuestTimers("Winnie"), Now, ladder);

    [Fact]
    public void The_chat_refuses_a_quest_the_menu_refuses_with_the_menus_names()
    {
        var w = World();
        var tusk = Banded();

        Assert.Equal("ladder-quest", Logic(w, new AutopilotOptions(), Input(tusk, ladder: Key)).UseTask(QuestTask, AutopilotHostLogic.TaskByChat));
        Assert.Equal("level-out-of-band", Logic(w, new AutopilotOptions(), Input(tusk with { Level = new QuestLevel(50, 60) })).UseTask(QuestTask, AutopilotHostLogic.TaskByChat));
        Assert.Equal("no-level", Logic(w, new AutopilotOptions(), Input(tusk with { Level = null })).UseTask(QuestTask, AutopilotHostLogic.TaskByChat));
        var once = new QuestTimers("Winnie");
        var oneShot = tusk with { TimerSeconds = null };
        once.Complete(oneShot, Now.AddDays(-1), QuestTimers.HowReply);
        Assert.Equal("done-once", Logic(w, new AutopilotOptions(), Input(oneShot, timers: once)).UseTask(QuestTask, AutopilotHostLogic.TaskByChat));

        var logic = Logic(w, new AutopilotOptions(), Input(tusk));
        Assert.Null(logic.UseTask(QuestTask, AutopilotHostLogic.TaskByChat)); // a fitting one is chosen
        Assert.Equal(QuestTask, logic.Task);
        var timers = new QuestTimers("Winnie");
        timers.Complete(tusk, Now.AddHours(-1), QuestTimers.HowReply);
        var notDue = Logic(w, new AutopilotOptions(), Input(tusk, timers: timers));
        Assert.Null(notDue.UseTask(QuestTask, AutopilotHostLogic.TaskByChat)); // not-due stays the scenario's task-done:...:not-due
        Assert.Contains(w.Lines, l => l.Contains("autopilot task: quest:tusk-female refused (by chat): level-out-of-band", StringComparison.Ordinal));
    }

    [Fact]
    public void The_option_task_is_judged_at_the_start_and_a_refused_one_starts_nothing()
    {
        var w = World();
        var logic = Logic(w, new AutopilotOptions { Task = QuestTask }, Input(Banded() with { Level = new QuestLevel(50, 60) }));

        Assert.False(logic.Start(w.Trip.View));

        Assert.Equal(AutopilotMode.Off, logic.Mode);
        Assert.Contains("level-out-of-band", logic.Status.LastDecision, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.Contains("refused at the start: level-out-of-band", StringComparison.Ordinal));

        var fits = Logic(w, new AutopilotOptions { Task = QuestTask }, Input(Banded()));
        Assert.True(fits.Start(w.Trip.View));
    }
}
