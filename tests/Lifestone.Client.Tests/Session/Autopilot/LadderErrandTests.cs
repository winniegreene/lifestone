using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 8 slice F (D118): the ladder sends her on a quest at its level. Proved, pure (<see cref="AutopilotQuestRule.LadderCalls"/>): the band's edges, <c>once</c>, the timers, the named refusals, a missed band only for a quest undone, a ladder quest is never a trophy. Over the scripted
/// ACE: the errand runs from home at the fight's trip and the trip is still made; a trophy goes first; a missed band is one loud line; a failed or killed errand is not called again and the climb goes on; <c>errands=</c> and <c>turn_ins=</c> count apart; the rung is decided afresh by
/// <see cref="RungOwner"/> when the errand moved her past its edge. The quest records are the test's own (the quest files are the next slice's).
/// </summary>
public sealed partial class LadderErrandTests(ITestOutputHelper output)
{
    private const string Key = "fetch-test";
    private const string Lodge = "tusker-lodge";
    private const uint Lost = 0x12340100; // on no ground and in no travel place
    private const uint Shoushi = 0xDA55001D; // the travel file's place shoushi
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);
    private static readonly HuntingGround Burrow = GroundCatalog.Get("tusker-burrow");
    private static readonly IReadOnlyDictionary<string, string> NoFailures = new Dictionary<string, string>(StringComparer.Ordinal);

    private static uint Holtburg => Burrow.Home.LifestoneCell;

    private static Quest Errand(int from = 10, int before = 30, bool once = true, int? timer = null, string key = Key, uint? firstCell = null) => new()
    {
        Key = key,
        Name = "A test errand",
        Giver = null,
        Start = new QuestPlace(firstCell ?? Holtburg, Vector3.Zero, "the errand's start"),
        Steps = [TrophyQuestTests.Step(0, QuestStepKind.Go, "go to the giver") with { Place = new QuestPlace(firstCell ?? Holtburg, Vector3.Zero, "the errand's start") }],
        Done = new QuestDone(null, "Thank you.", null),
        TimerSeconds = timer,
        Sources = ["the test's own record"],
        Ladder = new QuestLadder(from, before, once),
    };

    private static TrophyQuestInput Input(int level = 10, QuestTimers? timers = null, uint? cell = null, params Quest[] quests) =>
        new(null, quests.Length == 0 ? [Errand()] : quests, timers ?? new QuestTimers("Winnie"), Now, _ => 0, level, false, cell ?? Holtburg, GroundCatalog.All, TravelRoutes.Default);

    private static LadderErrandCall One(TrophyQuestInput input, IReadOnlyDictionary<string, string>? failed = null) => Assert.Single(AutopilotQuestRule.LadderCalls(input, failed ?? NoFailures));

    // ------------------------------------------------------------------------------------------------ the rule

    [Theory]
    [InlineData(9, false, "level-out-of-band")] // one under
    [InlineData(10, true, null)] // the first in
    [InlineData(29, true, null)] // the last in
    [InlineData(30, false, "level-out-of-band")] // one over
    public void The_band_runs_from_from_level_up_to_below_before_level(int level, bool runs, string? refusal)
    {
        var call = One(Input(level));

        Assert.Equal((runs, refusal), (call.Runs, call.Refusal));
        Assert.Equal(runs ? JourneyPlans.FromHome : null, call.From);
        Assert.Equal(level == 30, call.Missed); // only a band passed is missed, never one not reached yet
        Assert.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from={(runs ? "home" : "none")} why=", call.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Once_is_never_again_even_when_the_timer_would_allow_more()
    {
        var timers = new QuestTimers("Winnie");
        timers.Complete(Errand(timer: 3600), Now.AddHours(-2), QuestTimers.HowReply); // the timer has run out

        var once = One(Input(timers: timers, quests: Errand(timer: 3600)));
        Assert.Equal(("done-once", false), (once.Refusal, once.Runs));
        Assert.Contains("recorded done 1 time(s)", once.Why, StringComparison.Ordinal);

        Assert.True(One(Input(timers: timers, quests: Errand(once: false, timer: 3600))).Runs); // not once: due again

        var waiting = new QuestTimers("Winnie");
        waiting.Complete(Errand(timer: 3600), Now.AddMinutes(-10), QuestTimers.HowReply);
        var notDue = One(Input(timers: waiting, quests: Errand(once: false, timer: 3600)));
        Assert.Equal(("not-due", false), (notDue.Refusal, notDue.Due));
        Assert.Contains("next due 2026-11-09T14:50:00Z", notDue.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusals_are_named_and_a_failed_errand_is_not_called_again()
    {
        var failed = new Dictionary<string, string>(StringComparer.Ordinal) { [Key] = "failed:quest:fetch-test:go:off-place" };
        var again = One(Input(), failed);
        Assert.Equal("failed-this-run", again.Refusal);
        Assert.Contains("failed:quest:fetch-test:go:off-place", again.Why, StringComparison.Ordinal);

        var body = One(Input(cell: Lost));
        Assert.Equal("no-route", body.Refusal);
        Assert.Contains("on no ground and in no travel place", body.Why, StringComparison.Ordinal);

        var unread = One(Input() with { BodyCell = null });
        Assert.Equal("no-route", unread.Refusal);
        Assert.Contains("unread", unread.Why, StringComparison.Ordinal);

        var goal = One(Input(quests: Errand(firstCell: Lost)));
        Assert.Equal("no-route", goal.Refusal);
        Assert.Contains("in no travel place of", goal.Why, StringComparison.Ordinal);

        var onGround = One(Input(cell: Burrow.ArrivalDestination().Location.Position.CellId)); // the errand starts from home whatever ground she stands on
        Assert.Equal(JourneyPlans.FromHome, onGround.From);
        Assert.Contains("on tusker-burrow", onGround.Why, StringComparison.Ordinal);
        Assert.Null(AutopilotQuestRule.FirstLadder([again, body, unread, goal]));
        Assert.Same(onGround, AutopilotQuestRule.FirstLadder([again, onGround]));
    }

    [Fact]
    public void A_band_passed_is_missed_only_for_a_quest_undone()
    {
        Assert.True(One(Input(40)).Missed);

        var timers = new QuestTimers("Winnie");
        timers.Complete(Errand(), Now.AddDays(-1), QuestTimers.HowReply);
        var done = One(Input(40, timers));
        Assert.Equal(("level-out-of-band", false), (done.Refusal, done.Missed));
    }

    [Fact]
    public void The_band_that_closes_first_is_decided_first_and_a_ladder_quest_is_never_a_trophy()
    {
        var calls = AutopilotQuestRule.LadderCalls(Input(quests: [Errand(before: 30, key: "late"), Errand(before: 20, key: "soon")]), NoFailures);
        Assert.Equal(["soon", "late"], calls.Select(c => c.Quest.Key));

        var hunted = TrophyQuestTests.TuskFemale(ground: Lodge) with { Ladder = new QuestLadder(10, 30, true) };
        Assert.Empty(AutopilotQuestRule.Trophies(Lodge, [hunted]));
        Assert.Single(AutopilotQuestRule.Trophies(Lodge, [TrophyQuestTests.TuskFemale(ground: Lodge)]));
        Assert.Empty(AutopilotQuestRule.Want(Input(quests: hunted) with { GroundKey = Lodge }));
        Assert.Empty(AutopilotQuestRule.LadderCalls(Input(quests: TrophyQuestTests.TuskFemale(ground: Lodge)), NoFailures)); // a quest with no ladder object is never sent
    }

    [Fact]
    public void The_ladder_line_counts_errands_apart_from_turn_ins()
    {
        var rung = LadderLoader.WarMage.RungFor(5);
        var text = new LadderLine("cycle", 3, 7, rung.Key, "holtburg-outskirts", new AutopilotTally(), 0, rung.MoveOn, null, TurnIns: 1, Errands: 2).Text();

        Assert.Contains(" errands=2 turn_ins=1 health=", text, StringComparison.Ordinal);
        Assert.Contains("errands", LadderLine.FieldNames);
    }

    // ------------------------------------------------------------------------------------------------ the autopilot over the scripted ACE

    private static string Fields(StationSpec spec, bool done, string fail) =>
        $"key={spec.Arguments[JourneyPlans.QuestArgument]} from={spec.Arguments[JourneyPlans.FromArgument]} steps={(done ? 1 : 0)}/1 done={(done ? "yes" : "no")} xp=+{(done ? 100 : 0)} reward_line={(done ? 100 : 0)} timer={(done ? "saved" : "none")} ok={(done ? "yes" : "no")} fail={fail}";

    /// <summary>A station that ends well and records the completion, as the real one does through <see cref="JourneyContext.CompleteQuest"/>; <paramref name="also"/> is what the errand did to the world.</summary>
    private static ScriptedRun Completes(StationSpec spec, Quest quest, QuestTimers timers, Action? also = null) => new(
        spec.Name,
        _ =>
        {
            also?.Invoke();
            timers.Complete(quest, Now, QuestTimers.HowReply);
            return ScenarioStatus.Succeeded;
        },
        () => Fields(spec, done: true, "none"));

    private static StationSpec[] Quests(ApWorld w) => [.. w.Factory.Made.Where(s => s.Kind == StationKinds.Quest)];

    private static string LastLadder(AutopilotScenario a) => a.Lines.Last(l => l.StartsWith(LadderLine.Prefix, StringComparison.Ordinal));

    private (ApWorld World, AutopilotScenario Autopilot, QuestTimers Timers) Climber(Quest quest, Func<StationSpec, ApWorld, IStationRun?> script, bool tusk = false, params Quest[] before)
    {
        ApWorld? world = null;
        var timers = new QuestTimers("Winnie");
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk, spec => script(spec, world!), timers);
        world = w;
        autopilot.TrophyQuests = [.. before, quest];
        return (w, autopilot, timers);
    }

    [Fact]
    public void A_due_errand_runs_from_home_before_the_trip_the_fight_stopped_for_and_counts_on_errands()
    {
        var quest = Errand();
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!));
        timers = t;
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var spec = Assert.Single(Quests(w));
        Assert.Equal((Key, JourneyPlans.FromHome), (spec.Arguments[JourneyPlans.QuestArgument], spec.Arguments[JourneyPlans.FromArgument]));
        var kinds = Kinds(w);
        Assert.Equal("den-fight", kinds[Array.IndexOf(kinds, "quest") - 1]);
        Assert.Equal("logistics", kinds[Array.IndexOf(kinds, "quest") + 1]); // the trip the fight stopped for is still made
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=home why=", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: ended key={Key} ok=yes done=yes xp=+100 timer=saved turn_ins=0 errands=1", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key} due=no errand=ladder from=none why=done-once: ", StringComparison.Ordinal) || l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=none why=done-once: ", StringComparison.Ordinal)); // the stop's way home: never again
        Assert.Equal(1, autopilot.LadderErrands);
        Assert.Equal(0, autopilot.TurnIns);
        Assert.Contains(" errands=1 turn_ins=0 health=", LastLadder(autopilot), StringComparison.Ordinal);
    }

    /// <summary>Long run 5 (2026-11-10): the timers file is named for the character's name, so a fresh climber read the last climber's finished errand as her own and never ran it.</summary>
    [Fact]
    public void A_climber_recreated_under_the_same_name_does_not_inherit_the_old_ones_finished_errand()
    {
        var quest = Errand();
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!));
        timers = t;
        Assert.Null(t.BindTo(0x5FFFFFF0)); // the old character's guid
        t.Complete(quest, Now.AddDays(-1), QuestTimers.HowReply); // her finished errand, left in the file
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Contains(w.Lines, l => l.StartsWith("quest timers: check before: the file's records are another character's (guid 0x5FFFFFF0 in the file, ", StringComparison.Ordinal) && l.Contains("1 record(s) dropped", StringComparison.Ordinal) && l.Contains(Key, StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("quest timers: check after: bound to ", StringComparison.Ordinal) && l.Contains(", 0 record(s), ", StringComparison.Ordinal));
        Assert.Single(Quests(w)); // the errand ran on this character
        Assert.NotEqual(0x5FFFFFF0u, t.CharacterGuid);
        Assert.Equal(1, t.Get(Key)!.Completions); // her own completion, not the old one's second
        Assert.Equal(1, autopilot.LadderErrands);
    }

    /// <summary>The long round (2026-11-10): two of its three errands began on a trip called for burden (138% and 141%) and failed, the one that began light ran clean. Over the trip line the sale goes first.</summary>
    [Fact]
    public void An_errand_due_with_the_load_over_the_trip_line_waits_for_the_sale_and_runs_after_the_trip()
    {
        var quest = Errand();
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!));
        timers = t;
        autopilot.BurdenForTest = 141;
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.KnownState, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        var kinds = Kinds(w);
        Assert.Equal("logistics", kinds[Array.IndexOf(kinds, "quest") - 1]); // the sale first, then the errand
        Assert.Equal("den-fight", kinds[Array.IndexOf(kinds, "quest") - 2]);
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: check before: burden 141% is over the trip line ", StringComparison.Ordinal) && l.EndsWith("(m6-errand-after-sale)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("after the sale trip (check after: burden 141%)", StringComparison.Ordinal) && l.Contains($"the ladder errand {Key} is due", StringComparison.Ordinal));
        Assert.Single(Quests(w));
        Assert.Equal(1, autopilot.LadderErrands);
        Assert.Equal(1, t.Get(Key)!.Completions);
    }

    /// <summary>Long4's stop (2026-11-10): the stop's "recalling home before the stop" started the whole errand as its way home. A stop is a way home, not an errand: the errand is named and waits for the next run.</summary>
    [Fact]
    public void A_stop_requested_with_an_errand_due_recalls_home_and_starts_no_errand()
    {
        var (w, autopilot, _) = Climber(Errand(), (_, _) => throw new InvalidOperationException("a stop must not start a ladder errand"));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight, 20000); // the first fight: the errand is due, no way home taken yet
        Assert.Empty(Quests(w));
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(Quests(w));
        Assert.Equal("request:test", autopilot.StopName);
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key}: the way home before the stop starts no ladder errand (m6-stop-runs-errand)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("autopilot: recalling home before the stop", StringComparison.Ordinal));
        Assert.Equal(0, autopilot.LadderErrands);
    }

    [Fact]
    public void A_trophy_due_beside_an_errand_goes_first_and_the_errand_takes_a_later_way_home()
    {
        var quest = Errand();
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(
            quest,
            (spec, world) => spec.Arguments[JourneyPlans.QuestArgument] == Key ? Completes(spec, quest, timers!) : TrophyQuestTests.TurnIn(spec, world),
            tusk: true,
            TrophyQuestTests.TuskFemale(ground: Lodge));
        timers = t;
        w.Run(autopilot, a => a.TurnIns > 0 && a.State == AutopilotState.KnownState, 20000);
        Assert.DoesNotContain(w.Lines, l => l.Contains("errand=ladder", StringComparison.Ordinal)); // one errand a trip, trophies first
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State == AutopilotState.KnownState, 20000); // a later trip's way home, never the stop's (m6-stop-runs-errand)
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Equal(["tusk-female", Key], Quests(w).Select(s => s.Arguments[JourneyPlans.QuestArgument]));
        Assert.Equal((1, 1), (autopilot.TurnIns, autopilot.LadderErrands));
        Assert.Contains(" errands=1 turn_ins=1 health=", LastLadder(autopilot), StringComparison.Ordinal);
    }

    [Fact]
    public void A_band_passed_with_the_quest_undone_is_one_loud_line_and_the_quest_is_not_run()
    {
        var quest = Errand(from: 5, before: 10);
        var (w, autopilot, _) = Climber(quest, (_, _) => throw new InvalidOperationException("a missed errand must not run"));
        w.Run(autopilot, a => Kinds(w).Count(k => k == "logistics") >= 2 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(Quests(w));
        Assert.True(w.Lines.Count(l => l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=none why=level-out-of-band: ", StringComparison.Ordinal)) >= 2); // decided at each way home
        var missed = Assert.Single(w.Lines, l => l.Contains("TODO(m6-ladder-errand-missed)", StringComparison.Ordinal));
        Assert.Contains($"key={Key} level=17", missed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("give:no-answer: the golem said nothing")]
    [InlineData("logistics: stop: the words a recall cannot change")] // Unrecoverable's text: a failed trophy would stop the run on it
    public void A_failed_errand_is_named_not_called_again_and_the_climb_goes_on(string failure)
    {
        var (w, autopilot, _) = Climber(Errand(), (spec, _) => new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => Fields(spec, done: false, "give:no-answer")) { Failure = failure });
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 20000); // the audit's learn trip, then the trip the fight stopped for
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Single(Quests(w)); // never a loop: the next trip's way home and the stop's refuse it by name
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: ladder errand failed: key={Key} stop=failed:quest:{Key}:give:no-answer", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=none why=failed-this-run: ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after quest:", StringComparison.Ordinal)); // no station-failure recovery: its budget and repeat rules would end the run
        Assert.False(autopilot.StopName?.StartsWith("failed:", StringComparison.Ordinal) ?? false, autopilot.StopName);
        Assert.Equal(0, autopilot.LadderErrands);
        Assert.Contains(" errands=0 turn_ins=0 health=", LastLadder(autopilot), StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 14 (the lead's ruling after live ladder42 20:20:15 UTC: the Atlan errand's first walk retreated by recall, <c>fail=go:hop:retreat:hop:recall</c>, and the errand was failed for the run): a failed walk step leaves the errand
    /// callable at the next way home, <see cref="AutopilotScenario.LadderErrandWalkTries"/> times a run; the next walk failure fails it for the run, never a loop.
    /// </summary>
    [Fact]
    public void A_failed_walk_leaves_the_errand_callable_within_its_tries()
    {
        var (w, autopilot, _) = Climber(Errand(), (spec, _) => new ScriptedRun(spec.Name, _ => ScenarioStatus.Failed, () => Fields(spec, done: false, "go:hop:retreat:hop:recall")) { Failure = "go:hop:retreat:hop:recall: the hop leg retreated" });
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= AutopilotScenario.LadderErrandWalkTries + 3, 60000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Equal(AutopilotScenario.LadderErrandWalkTries + 1, Quests(w).Count()); // three walks that failed stay callable, the fourth fails it for the run
        Assert.Equal(AutopilotScenario.LadderErrandWalkTries, w.Lines.Count(l => l.Contains("a walk step failed (", StringComparison.Ordinal) && l.Contains("the errand stays callable at the next way home", StringComparison.Ordinal)));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=none why=failed-this-run: ", StringComparison.Ordinal));
        Assert.False(autopilot.StopName?.StartsWith("failed:", StringComparison.Ordinal) ?? false, autopilot.StopName);
    }

    [Fact]
    public void A_failed_errand_in_an_unknown_place_comes_home_by_a_named_way_and_the_trip_is_still_made()
    {
        var (w, autopilot, _) = Climber(Errand(), (spec, world) => new ScriptedRun(
            spec.Name,
            _ =>
            {
                world.Trip.Vendor.StandAt(Lost, new Vector3(100, 100, 0));
                return ScenarioStatus.Failed;
            },
            () => Fields(spec, done: false, "go:off-place")) { Failure = "go:off-place: nowhere" });
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 20000);

        var kinds = Kinds(w);
        var after = kinds[(Array.IndexOf(kinds, "quest") + 1)..];
        Assert.Equal("recall-home", after[0]); // the way home is named: no route from a place unknown, so the recall
        Assert.Contains("logistics", after);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot travel: direction=home way=recall-home ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_station_that_ended_well_with_the_quest_not_done_counts_as_failed()
    {
        var (w, autopilot, _) = Climber(Errand(), (spec, _) => new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => Fields(spec, done: false, "none").Replace("ok=no", "ok=yes", StringComparison.Ordinal)));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Single(Quests(w));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: ladder errand failed: key={Key} stop=failed:quest:{Key}:not-done", StringComparison.Ordinal));
        Assert.Equal(0, autopilot.LadderErrands);
    }

    [Fact]
    public void A_death_inside_an_errand_goes_to_the_death_path_and_the_errand_is_not_walked_again()
    {
        var (w, autopilot, _) = Climber(Errand(), (spec, world) => new ScriptedRun(
            spec.Name,
            step =>
            {
                if (step == 0)
                {
                    world.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain on the errand!", 0, DeathNoticeSource.VictimNotification));
                }

                return null;
            },
            () => Fields(spec, done: false, "none")));
        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2 && Kinds(w).Contains("death"), 30000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 30000);

        var kinds = Kinds(w);
        Assert.Single(Quests(w));
        Assert.Equal("death", kinds[Array.IndexOf(kinds, "quest") + 1]);
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: ladder errand died: key={Key}", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: key={Key} due=yes errand=ladder from=none why=failed-this-run: it failed this run (failed:quest:{Key}:death)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_errand_that_moved_her_past_a_rungs_edge_has_the_rung_decided_afresh_after_the_trip()
    {
        var quest = Errand();
        ApWorld? world = null;
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!, () => world!.Level(22))); // the Lodge's band ends at 21
        (world, timers) = (w, t);
        w.Run(autopilot, null, 20000);

        var kinds = Kinds(w);
        Assert.Equal("logistics", kinds[Array.IndexOf(kinds, "quest") + 1]);
        Assert.Equal("rung-moved:cave", autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("ladder: rung: asked by the cycle's check: level=22", StringComparison.Ordinal)); // RungOwner.Decide, by the existing path
    }

    [Fact]
    public void From_elsewhere_the_errand_goes_first_and_ended_in_town_no_recall_is_cast_from_home()
    {
        var quest = Errand();
        ApWorld? world = null;
        QuestTimers? timers = null;
        var (w, autopilot, t) = Climber(quest, (spec, _) => Completes(spec, quest, timers!, () => world!.Trip.Vendor.StandAt(world.Ground.Home.LifestoneCell, world.Ground.Home.LifestonePoint))); // the errand ends in Holtburg
        (world, timers) = (w, t);
        w.Trip.Vendor.StandAt(Shoushi, new Vector3(100, 100, 0)); // a travel place, neither the ground nor home
        w.Run(autopilot, a => a.LadderErrands > 0 && a.State != AutopilotState.Trip, 20000);
        w.Run(autopilot, a => a.State == AutopilotState.Buff || a.State == AutopilotState.Travel, 2000);

        var kinds = Kinds(w);
        Assert.Single(Quests(w));
        Assert.DoesNotContain("recall-home", kinds[Array.IndexOf(kinds, "quest")..]);
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot quest: after the turn-in of {Key}: at home: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_with_no_ground_rung_has_no_errand_input_and_does_not_throw()
    {
        Assert.True(One(Input() with { GroundKey = null }).Runs); // the rule itself never reads the ground

        var (w, climber) = TrophyQuestTests.MakeClimber(output, tusk: false);
        var den = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 }); // the den job: TrophyInput is null
        den.BuffsForTest = climber.BuffsForTest;
        den.TrophyQuests = [Errand()];
        w.Run(den, a => Kinds(w).Contains("logistics") && a.State == AutopilotState.Fight, 20000);

        Assert.Empty(Quests(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("errand=ladder", StringComparison.Ordinal));
    }

    [Fact]
    public void A_ground_rung_with_no_errands_decides_nothing_new()
    {
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false);
        w.Run(autopilot, a => Kinds(w).Count(k => k == "logistics") >= 2 && a.State == AutopilotState.Fight, 20000);
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 20000);

        Assert.Empty(Quests(w));
        Assert.DoesNotContain(w.Lines, l => l.Contains("errand=ladder", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("m6-ladder-errand-missed", StringComparison.Ordinal));
        Assert.Contains(" errands=0 turn_ins=0 health=", LastLadder(autopilot), StringComparison.Ordinal);
    }
}
