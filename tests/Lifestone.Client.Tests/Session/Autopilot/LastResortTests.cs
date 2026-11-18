using System.Numerics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 8 slice N (D120, "Yes, last resort"): <c>/lifestone</c> as the survival layer's last leave option. Pure (<see cref="LastResortRule"/>): the check before, the arrival after, the reason tokens. Over the scripted ACE: a failed ladder errand whose way home fails then takes
/// the recall once, stands still while it runs, arrives and the climb goes on; a refusal or no arrival stops by name with no retry; no failed errand, no last resort (a way home that fails keeps its road); the <c>[ladder]</c> line counts it.
/// </summary>
public sealed partial class LadderErrandTests
{
    private const string RecallHomeKind = "recall-home";

    // ------------------------------------------------------------------------------------------------ the rule

    [Fact]
    public void The_last_resort_checks_before_it_acts()
    {
        var ok = new LastResortInput { Alive = true, InWorld = true, Placed = true, Busy = false, SanctuaryKnown = null };

        var go = LastResortRule.Check(in ok);
        Assert.Equal(LastResortVerdict.Go, go.Verdict);
        Assert.Contains("refusal line is the check", go.Why, StringComparison.Ordinal); // the client cannot read ACE's Sanctuary: the server's line is the check, named

        Assert.Equal(LastResortVerdict.Go, LastResortRule.Check(ok with { SanctuaryKnown = true }).Verdict);
        Assert.Equal(("busy", LastResortVerdict.Wait), (LastResortRule.Check(ok with { Busy = true }).Reason, LastResortRule.Check(ok with { Busy = true }).Verdict));

        foreach (var (input, reason) in new (LastResortInput, string)[]
        {
            (ok with { InWorld = false }, "not-in-world"),
            (ok with { Alive = false }, "not-alive"),
            (ok with { Placed = false }, "portal-space"),
            (ok with { SanctuaryKnown = false }, "no-sanctuary"),
        })
        {
            var refused = LastResortRule.Check(in input);
            Assert.Equal((LastResortVerdict.Refuse, reason), (refused.Verdict, refused.Reason));
            Assert.Equal("failed:leave:lifestone-recall:" + reason, LastResortRule.Stop(reason));
        }
    }

    [Theory]
    [InlineData(true, false, 3.0, false)] // present, same cell, 3 m: she has not gone anywhere
    [InlineData(true, false, 8.5, true)] // past ACE's 8 m threshold
    [InlineData(true, true, 0.0, true)] // another cell
    [InlineData(false, true, 900.0, false)] // still in portal space
    public void The_arrival_is_the_body_placed_away_from_where_it_started(bool present, bool otherCell, double metres, bool arrived) =>
        Assert.Equal(arrived, LastResortRule.Arrived(present, sameCell: !otherCell, metres));

    [Fact]
    public void A_refusal_has_the_servers_name_in_the_stop()
    {
        Assert.Equal("no-sanctuary", AutopilotScenario.LastResortReason(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the recall: " + RecallTracker.NoSanctuaryLine)));
        Assert.Equal("timed-out", AutopilotScenario.LastResortReason(new ActionOutcome(ActionOutcomeKind.TimedOut, 0, "no teleport")));
        var moved = AutopilotScenario.LastResortReason(new ActionOutcome(ActionOutcomeKind.ServerError, 0x0498, "the server refused the recall: WeenieError YouHaveMovedTooFar (0x0498)"));
        Assert.Contains("movedtoofar", moved.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_ladder_line_counts_the_lifestone_recalls()
    {
        var text = new LadderLine("cycle", 1, 8, "outskirts", "holtburg-outskirts", new AutopilotTally(), 0, null, null, LifestoneRecalls: 2).Text();
        Assert.Contains(" health=? lifestone_recalls=2", text, StringComparison.Ordinal);
        Assert.Equal("lifestone_recalls", LadderLine.FieldNames[^2]); // M6 item 14: buffbot= follows it, the last field before reason=
        Assert.Equal("buffbot", LadderLine.FieldNames[^1]);
        Assert.EndsWith(" lifestone_recalls=2 buffbot=off", text, StringComparison.Ordinal);
        Assert.EndsWith(" buffbot=on", new LadderLine("cycle", 1, 8, "outskirts", "holtburg-outskirts", new AutopilotTally(), 0, null, null, BuffBot: true).Text(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ over the scripted ACE

    /// <summary>The scripted server's answers to /lifestone: after <see cref="Frames"/> frames of her standing still it answers (<see cref="Answer"/>), and every frame in between must find her where she was and no station started.</summary>
    private sealed class RecallServer(ApWorld w, Action<ApWorld> answer, int frames = 24)
    {
        private int _answered;
        private int _waited;
        private CellFrameSnapshot? _from;
        private int _madeAtSend;

        public int Frames { get; } = frames;

        public bool Moved { get; private set; }

        public bool StationStarted { get; private set; }

        public int Sends => w.Trip.Vendor.H.Net.Of(FakeSendKind.TeleToLifestone).Count();

        public void Frame(AutopilotScenario a)
        {
            if (Sends <= _answered)
            {
                return;
            }

            var at = w.Trip.View.State.Self.Position;
            if (_from is null)
            {
                _from = new CellFrameSnapshot(at!.Value.CellId, at.Value.Origin);
                _madeAtSend = w.Factory.Made.Count;
            }

            Moved |= at is { } now && (now.CellId != _from.Cell || Vector3.Distance(now.Origin, _from.Origin) > 0.01f);
            StationStarted |= w.Factory.Made.Count != _madeAtSend;
            if (++_waited >= Frames)
            {
                _answered = Sends;
                answer(w);
            }
        }

        private sealed record CellFrameSnapshot(uint Cell, Vector3 Origin);
    }

    private static void Teleported(ApWorld w)
    {
        w.Trip.Vendor.H.ServerMessage("Winnie is recalling to the lifestone.", 0x17);
        w.Trip.Vendor.H.R.Bus.Publish(new PlayerTeleportAnnounced(1));
        w.Trip.Vendor.StandAt(w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint);
    }

    /// <summary>A climber whose ladder errand failed in an unknown place and whose way home (the recall-home station, scripted) then fails, as long as <paramref name="wayHomeFails"/> says so.</summary>
    private (ApWorld World, AutopilotScenario Autopilot, Func<bool> ErrandFailed, Action<bool> WayHome) FailedErrandClimber()
    {
        var errandFailed = false;
        var wayHomeFails = true;
        var (w, autopilot, _) = Climber(Errand(), (spec, world) => new ScriptedRun(
            spec.Name,
            _ =>
            {
                errandFailed = true;
                world.Trip.Vendor.StandAt(Lost, new Vector3(100, 100, 0));
                return ScenarioStatus.Failed;
            },
            () => Fields(spec, done: false, "go:off-place")) { Failure = "go:off-place: nowhere" });
        w.Factory.FailWith = spec => errandFailed && wayHomeFails && spec.Name == RecallHomeKind ? "recall-home: scripted: the way home failed" : null;
        return (w, autopilot, () => errandFailed, v => wayHomeFails = v);
    }

    [Fact]
    public void A_failed_way_home_after_a_failed_errand_takes_the_lifestone_recall_arrives_and_the_climb_goes_on()
    {
        var (w, autopilot, _, wayHome) = FailedErrandClimber();
        var server = new RecallServer(w, world =>
        {
            Teleported(world);
            wayHome(true); // the later trips' recall-home would fail as well: the climb must not need it
        });

        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });
        Assert.Equal(AutopilotState.Fight, autopilot.State);

        Assert.Equal(1, server.Sends);
        Assert.False(server.Moved, "she stood still while the recall ran");
        Assert.False(server.StationStarted, "no station ran while the recall ran");
        Assert.Equal(1, autopilot.LifestoneRecalls);
        Assert.Contains(w.Lines, l => l.StartsWith("survival: ", StringComparison.Ordinal) && l.Contains("leave: lifestone-recall: the walk recall-home failed after the ladder errand " + Key, StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("lifestone-recall: check before:", StringComparison.Ordinal) && l.Contains("ACE halves her mana up front", StringComparison.Ordinal) && l.Contains("walk failed: recall-home", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("lifestone-recall: check after: arrived", StringComparison.Ordinal) && l.Contains("lifestone_recalls=1", StringComparison.Ordinal));
        Assert.Null(autopilot.StopName);
        Assert.Contains(" lifestone_recalls=1", LastLadder(autopilot), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_stops_by_name_with_no_retry()
    {
        var (w, autopilot, _, _) = FailedErrandClimber();
        var server = new RecallServer(w, world => world.Trip.Vendor.H.ServerMessage(RecallTracker.NoSanctuaryLine, 1), frames: 4);

        var status = w.Run(autopilot, null, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal("failed:leave:lifestone-recall:no-sanctuary", autopilot.StopName);
        Assert.False(autopilot.Ok);
        Assert.Equal(1, server.Sends); // never a second /lifestone
        Assert.Equal(0, autopilot.LifestoneRecalls);
        Assert.Equal(1, Kinds(w).Count(k => k == RecallHomeKind)); // no recall home before the stop, no retry of the way home
        Assert.Contains(w.Lines, l => l.Contains("lifestone-recall: check failed:", StringComparison.Ordinal) && l.Contains("failed:leave:lifestone-recall:no-sanctuary", StringComparison.Ordinal));
    }

    [Fact]
    public void No_arrival_within_the_trackers_time_stops_by_name()
    {
        var (w, autopilot, _, _) = FailedErrandClimber();
        var server = new RecallServer(w, _ => { }, frames: int.MaxValue); // the server never answers

        var status = w.Run(autopilot, null, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal("failed:leave:lifestone-recall:timed-out", autopilot.StopName);
        Assert.Equal(1, server.Sends);
        Assert.False(server.Moved);
    }

    [Theory]
    [InlineData("failed:travel:recall-home:scripted", true)]
    [InlineData("failed:travel:rung-change-home:cathedral-to-shoushi:walk", true)]
    [InlineData("failed:known-state:elsewhere", true)]
    [InlineData("failed:known-state:not-in-world", false)]
    [InlineData("failed:travel:rung-change-out:hub:walk", false)]
    [InlineData("failed:trip:logistics", false)]
    public void Only_a_stop_that_says_no_way_home_worked_falls_to_the_last_resort(string stop, bool covered) =>
        Assert.Equal(covered, LastResortRule.CoversStop(stop));

    /// <summary>The owner's widening of D120, after long round 3 (it replaces the pin that a failed way home with no failed errand never sent a recall): the run's own retries first, then /lifestone instead of the stop, and the climb goes on.</summary>
    [Fact]
    public void Without_a_failed_errand_a_way_home_that_keeps_failing_takes_the_last_resort_after_its_retries_and_the_climb_goes_on()
    {
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false);
        w.Factory.FailWith = spec => spec.Name == RecallHomeKind ? "recall-home: scripted: the way home failed" : null;
        var server = new RecallServer(w, Teleported);
        w.Trip.Vendor.StandAt(Lost, new Vector3(100, 100, 0)); // elsewhere, on no ground: the known state takes the recall home, which fails

        w.Run(autopilot, a => a.State == AutopilotState.Fight || a.StopName is not null, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });

        Assert.True(Kinds(w).Count(k => k == RecallHomeKind) >= 2, "the run's own retries come first");
        Assert.Equal(1, server.Sends);
        Assert.Equal(1, autopilot.LifestoneRecalls);
        Assert.Null(autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("leave: lifestone-recall: the run would stop by failed:travel:recall-home", StringComparison.Ordinal) && l.Contains("TODO(m6-lifestone-covered-a-failure)", StringComparison.Ordinal));
    }

    /// <summary>The bound: a lifestone that is not home lands her somewhere the known state does not recognise; a second recall would land her there again, so the stop stands, by its own name.</summary>
    [Fact]
    public void A_recall_that_lands_her_nowhere_known_is_not_taken_again_and_the_run_stops_by_its_own_name()
    {
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false);
        w.Factory.FailWith = spec => spec.Name == RecallHomeKind ? "recall-home: scripted: the way home failed" : null;
        var server = new RecallServer(w, world =>
        {
            world.Trip.Vendor.H.ServerMessage("Winnie is recalling to the lifestone.", 0x17);
            world.Trip.Vendor.H.R.Bus.Publish(new PlayerTeleportAnnounced(1));
            world.Trip.Vendor.StandAt(Lost, new Vector3(150, 150, 0));
        });
        w.Trip.Vendor.StandAt(Lost, new Vector3(100, 100, 0));

        var status = w.Run(autopilot, null, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });

        Assert.NotEqual(ScenarioStatus.Running, status);
        Assert.Equal(1, server.Sends);
        Assert.False(autopilot.StopName?.StartsWith("failed:leave:", StringComparison.Ordinal) ?? true, autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains("leave: lifestone-recall: not taken: she stands in landblock", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recall_is_taken_once_for_a_failed_errand()
    {
        var (w, autopilot, _, wayHome) = FailedErrandClimber();
        var server = new RecallServer(w, world =>
        {
            Teleported(world);
            wayHome(true);
        });

        w.Run(autopilot, a => a.State == AutopilotState.Fight && Kinds(w).Count(k => k == "logistics") >= 2, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });
        autopilot.RequestStop("test");
        w.Run(autopilot, null, 30000, a => { AnswerWieldsAndModes(w); server.Frame(a); });

        Assert.Equal(1, server.Sends); // the errand is not called again this run, so no second failure arms a second recall
        Assert.Equal(1, autopilot.LifestoneRecalls);
    }

    [Theory]
    [InlineData(true, 16, 16, 30, true, true)] // the Burrow at 16 with no recall: the walk passes the hall
    [InlineData(true, 30, 16, 30, true, false)] // at the level the keep-out lifts, the walk is a walk
    [InlineData(true, 16, 0, 30, true, false)] // a dungeon with no kept-out hall
    [InlineData(false, 16, 16, 30, true, false)] // outdoors
    [InlineData(true, 16, 16, 30, false, false)] // the recall is the way home
    public void A_walk_out_past_a_kept_out_hall_is_no_way_home_M6_8(bool dungeon, int level, int cells, int under, bool walk, bool expected) =>
        Assert.Equal(expected, LastResortRule.NoSafeWalkOut(dungeon, level, cells, under, walk));
}
