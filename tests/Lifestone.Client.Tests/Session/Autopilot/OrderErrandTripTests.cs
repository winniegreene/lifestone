using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// The command registry's trip order during a quest's hunt-until (the owner's ruling of 2026-11-17: a sell trip on its own, by order): the errand's own fight is in a lull, the errand is cut where she stands, the trip runs, and the errand runs again from home from
/// what she holds, so the hunt's count (the hunted items held, <see cref="QuestResume"/>) is kept. The quest station and its fight are scripted as <c>ErrandLapseTests</c>' are.
/// </summary>
public sealed class OrderErrandTripTests(ITestOutputHelper output)
{
    private const string Key = "order-hunt-test";
    private const uint Mote = 6353;

    private static Quest Hunt() => new()
    {
        Key = Key,
        Name = "A hunt the order cuts",
        Giver = null,
        Start = new QuestPlace(0xA9B40019, Vector3.Zero, "Holtburg"),
        Steps = [TrophyQuestTests.Step(0, QuestStepKind.HuntUntil, "the motes") with { Item = new QuestThing(Mote, "Pyreal Mote"), Count = 16, Ground = "tusker-lodge" }],
        Done = new QuestDone(null, null, new QuestThing(Mote, "Pyreal Mote")),
        TimerSeconds = null,
        Sources = ["the test's own record"],
        Ladder = new QuestLadder(10, 30, true),
    };

    private sealed class HuntFight : IStoppableFight
    {
        public int Kills { get; set; }

        public FightStopReason? StopReason { get; private set; }

        public void RequestStop(FightStopReason reason) => StopReason ??= reason;
    }

    /// <summary>The quest station's run, scripted: its hunt's fight shows (<see cref="IStationSubFight"/>), and it works until it is cut.</summary>
    private sealed class HuntRun(string name, ApWorld w, HuntFight fight) : IStationRun, IStationSubFight, IStationWorking
    {
        public string Name => name;

        public string Stage => "hunt-until";

        public string? Failure => null;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => false;

        public IStoppableFight? SubFight => fight;

        public double? WorkedAt => w.Now;

        public ScenarioStatus Step(IGameSessionView session, double now) => ScenarioStatus.Running;

        public string VerdictFields() => $"key={Key} from=home steps=0/1 done=no xp=+0 reward_line=0 timer=none ok=no fail=none";
    }

    [Fact]
    public void A_trip_ordered_during_a_hunt_until_cuts_the_errand_in_its_lull_runs_the_trip_and_runs_the_errand_again_from_what_she_holds()
    {
        var quest = Hunt();
        var timers = new QuestTimers("Winnie");
        var fights = new List<HuntFight>();
        ApWorld? world = null;
        var options = new AutopilotOptions { Job = AutopilotJob.LevelUp, StartQuest = Key };
        var (w, _) = TrophyQuestTests.MakeClimber(
            output,
            tusk: false,
            spec =>
            {
                var fight = new HuntFight();
                fights.Add(fight);
                return new HuntRun(spec.Name, world!, fight);
            },
            timers,
            prepare: x => x.Trip.Own("Pyreal Mote", Mote, 1, 0),
            options: options);
        world = w;
        w.SetHeld(Mote, 5); // the hunt's count so far: five of sixteen
        var autopilot = w.Autopilot(options);
        autopilot.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4);
        autopilot.TrophyQuests = [quest];

        w.Run(autopilot, a => fights.Count == 1 && a.State == AutopilotState.Trip, 20000, _ => AnswerWieldsAndModes(w));
        Assert.Single(fights);
        var order = new ScenarioOrder("o-1", ScenarioOrderKind.Trip);
        Assert.Null(autopilot.AskOrder(order));
        Assert.Equal("busy", autopilot.AskOrder(new ScenarioOrder("o-2", ScenarioOrderKind.Rebuff))); // one at a time

        var before = Kinds(w).Length;
        w.Run(autopilot, a => fights.Count == 2 || a.StopName is not null, 20000, _ => AnswerWieldsAndModes(w));
        var made = Kinds(w).Skip(before).ToArray();

        Assert.Equal(("errand-lull", AutopilotState.Trip), (order.TakenAt, order.TakenInState)); // the errand's fight in a lull: no hostile in reach, the fight not stopping
        Assert.Null(fights[0].StopReason); // the hunt's fight was never told to stop mid-cast: the station was cut between its steps
        Assert.Equal((ScenarioOrderState.Done, "trip-done"), (order.State, order.Outcome));
        Assert.Equal(2, fights.Count); // the errand ran again
        var trip = Array.IndexOf(made, "logistics");
        var again = Array.IndexOf(made, "quest");
        Assert.True(trip >= 0 && again > trip, string.Join(",", made)); // the trip, then the errand again
        Assert.Contains(w.Lines, l => l.Contains($"autopilot order: o-1 trip: check before: the errand {Key}'s fight is in a lull", StringComparison.Ordinal) && l.Contains("with 5 of its hunted items held", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains($"autopilot quest: key={Key} due=yes errand=ladder", StringComparison.Ordinal)); // the ladder errand's own rule calls it again at the trip's end
        Assert.Equal(5, GearInventory.CountOf(w.Trip.View.State, [], Mote)); // its count kept: the hunt goes on from five
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 2000);
    }
}
