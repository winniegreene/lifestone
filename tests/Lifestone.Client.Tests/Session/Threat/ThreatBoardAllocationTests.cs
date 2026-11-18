using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Threat;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2a: the threat board costs the frame no allocation. The outer loop's watch is documented allocation-free and is asked every frame; the board's queries, its feed for a name it holds, and the shadow's comparison beside
/// each detector allocate nothing, whether the two agree or a difference stands (a line is written, and allocates, only when the pair changes). Measured on this thread in the non-parallel
/// <see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ThreatBoardAllocationTests
{
    private const string Guard = "Tusker Guard";

    /// <summary>A floor with targets and strangers near and far, and two kinds heard attacking.</summary>
    private static ThreatRig Busy()
    {
        var t = new ThreatRig();
        _ = t.Board;
        for (var i = 0; i < 12; i++)
        {
            t.Creature(Guard, t.Listed, 4 + (i * 5));
            t.Creature("Stranger", ThreatRig.Unlisted, 6 + (i * 5));
        }

        t.Hit(Guard, 9);
        t.Evade("Stranger");
        t.Wait(1);
        return t;
    }

    [Fact]
    public void The_boards_queries_allocate_nothing()
    {
        var t = Busy();
        var board = t.Board;
        var view = t.View;
        var inReach = ThreatQuery.InReach(ThreatRig.OuterReach, HostileWatch.AttackQuietSeconds, t.Targets);
        var standing = inReach with { AttackAlone = false }; // the bodies are read: no attack answers first
        var onHer = ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds, view.State.Self.Position);
        var awake = ThreatQuery.AwakeNear(22);
        var now = t.Now;
        Assert.Equal(ThreatAnswerKind.Attacked, board.Ask(view, in inReach, now).Kind);
        Assert.Equal(4, board.Ask(view, in standing, now).Count); // the targets at 4, 9, 14 and 19 m
        Assert.Equal(15, board.Ask(view, in onHer, now).Count); // both kinds heard: 8 targets (4 to 39 m) and 7 strangers (6 to 36 m) within 40 m
        Assert.Equal(8, board.Ask(view, in awake, now).Count); // 4 of each within 22 m

        var sum = 0.0;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                sum += board.Ask(view, in inReach, now).At;
                sum += board.Ask(view, in standing, now).Metres;
                sum += board.Ask(view, in onHer, now).Metres;
                sum += board.Ask(view, in awake, now).Metres;
                sum += board.Lull(now, 10).LastAt + board.Lull(now, 10, ThreatHeard.Attacks).LastAt;
                sum += board.Window(Guard, now, 10).Damage + board.Window(null, now, 10).Damage;
                sum += board.Find(Guard)!.Damage + board.WcidOf(Guard);
            }
        });
        Assert.True(sum > 0);
    }

    /// <summary>A session view over a reducer harness that records no events (the fight tests' harness boxes every event it publishes into a list, which is the harness's allocation, not the feed's).</summary>
    private sealed class BareView(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => throw new NotSupportedException();

        public PlayerTools Tools => throw new NotSupportedException();

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog => throw new NotSupportedException();

        public Lifestone.Net.Diagnostics.LinkStats Link => default;
    }

    [Fact]
    public void The_boards_feed_allocates_nothing_for_a_defence_notice_a_swing_order_or_a_removal_of_what_it_holds()
    {
        var h = new ReducerHarness(track: false);
        var player = h.EnterWorld();
        const uint Creature = 0x80007001;
        var basis = ReducerHarness.Create(Creature, ReducerHarness.Stamps(), ReducerHarness.Position(player.Position!.Value.CellId, 13), name: Guard);
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = 4242, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
        var board = ThreatBoard.For(new BareView(h));
        var bus = h.Bus;
        var hit = new CombatEvent(CombatEventKind.DefenseHit, Guard, 1, 7, 0.05, false, 4, false, 0, "Tusker Guard hits you for 7 points of slashing damage!");
        var evaded = new CombatEvent(CombatEventKind.DefenseEvaded, Guard, 0, 0, 0, false, -1, true, 0, "You evaded Tusker Guard!");
        var swing = new ObjectMotionChanged(Creature, false, false, new ServerMotionState { MovementType = MovementType.Interpreted, StickyGuid = ReducerHarness.PlayerGuid });
        var gone = new ObjectDeleted(0x80004444);
        var died = new PlayerDied("You have been killed by a Tusker Guard", Creature, DeathNoticeSource.VictimNotification);
        var killed = new PlayerKilledSomething("Tusker Guard is shattered by your assault!");
        bus.Publish(in hit); // the name's entry is made here, once

        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                bus.Publish(in hit);
                bus.Publish(in evaded);
                bus.Publish(in swing);
                bus.Publish(in gone);
                bus.Publish(in died);
                bus.Publish(in killed);
            }
        });
        var guard = board.Find(Guard)!;
        Assert.True(guard.Hits > 100 && guard.Attacks > 200 && guard.Swings >= 100 && board.Deaths >= 100 && board.Kills >= 100, $"{guard.Hits} hits, {guard.Attacks} attacks, {guard.Swings} swings");
        Assert.Equal((Creature, 4242u, "seen"), (guard.Guid, guard.Wcid, guard.WcidSource));
    }

    /// <summary>M6 16 Q2: the outer loop's watch asks the board and allocates nothing when it answers no (its words allocate only for a true answer).</summary>
    [Fact]
    public void The_outer_loops_watch_asking_the_board_allocates_nothing_while_nothing_is_in_reach()
    {
        var t = new ThreatRig();
        using var watch = t.OuterWatch();
        t.Creature(Guard, t.Listed, 30); // out of the reach: the watch answers false, with no words
        t.Creature("Stranger", ThreatRig.Unlisted, 6);
        var now = t.Now;
        var asked = 0;

        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                asked += watch.InReach(now += 0.016, out _) ? 1 : 0; // a new frame each ask: the board is asked every time
            }
        });
        Assert.Equal(0, asked);
    }

    /// <summary>M6 16 Q2: a hit the board heard before the watch was built, past its quiet, answers no and allocates nothing.</summary>
    [Fact]
    public void The_outer_loops_watch_allocates_nothing_while_a_hit_heard_before_it_is_past_its_quiet()
    {
        var t = new ThreatRig();
        _ = t.Board;
        t.Hit(Guard, 9);
        t.Wait(HostileWatch.AttackQuietSeconds + 0.5);
        using var watch = t.OuterWatch(); // built after the hit, which is past the quiet
        t.Creature(Guard, t.Listed, 30);
        var start = t.Now;
        var asked = 0;
        var round = 0;

        AllocationAssert.Nothing(() =>
        {
            var now = start + (++round * 0.001);
            for (var i = 0; i < 100; i++)
            {
                asked += watch.InReach(now += 0.00001, out _) ? 1 : 0;
            }
        });
        Assert.Equal(0, asked);
    }

    /// <summary>M6 16 E2b: a detector's shadow (<see cref="ThreatDetectorShadow"/>) and an owner's share of the lanes cost the frame nothing: while the pair agrees, while a difference stands, with a creature set aside and with a count.</summary>
    [Fact]
    public void A_detectors_shadow_allocates_nothing_while_the_two_agree_or_a_difference_stands()
    {
        var t = Busy();
        Func<uint, bool> none = static _ => false; // the owner's set-aside test, made once
        var agrees = new ThreatDetectorShadow(t.View, "agrees", static _ => { }, NullLogSink.Instance, "fight");
        var differs = new ThreatDetectorShadow(t.View, "differs", static _ => { }, NullLogSink.Instance, "fight", hearsSince: true);
        var share = new ThreatShadowShare();
        share.Add(agrees.Lane);
        share.Add(differs.Lane);
        var targets = t.Targets;
        var here = t.View.State.Self.Position;
        var start = t.Now;
        var nearest = t.Board.Ask(t.View, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds), start).Guid;
        differs.Answered(start + 0.001, ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds), false); // the difference appears: its one line
        Assert.Equal((1L, 1L), (differs.Lane.Differ, differs.Lane.Lines));
        var round = 0;
        var sum = 0L;

        AllocationAssert.Nothing(() =>
        {
            var now = start + 0.002 + (++round * 0.001); // inside the attacks' ten seconds, each ask a frame of its own
            for (var i = 0; i < 100; i++)
            {
                now += 0.00001;
                var onHer = ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds, here) with { Skip = none };
                agrees.Answered(now, in onHer, true, nearest, 4, count: 15);
                var ground = new ThreatQuery(22, 0, ThreatHeard.Hits, ThreatStanding.Targets) { Targets = targets, Skip = none };
                differs.Answered(now, in ground, false);
                sum += share.Counts.Agree + share.Counts.Differ;
            }
        });
        Assert.True(sum > 0);
        Assert.True(agrees.Lane.Agree >= 200 && agrees.Lane.Differ == 0, $"agree {agrees.Lane.Agree} differ {agrees.Lane.Differ}");
        Assert.True(differs.Lane.Differ >= 200 && differs.Lane.Agree == 0, $"agree {differs.Lane.Agree} differ {differs.Lane.Differ}");
        Assert.True(differs.Lane.Lines <= 2, $"lines {differs.Lane.Lines}"); // the first difference's line, and one more when its pair changed with the question
    }

    /// <summary>
    /// M6 16 Q2 and Q2b: the travel leg's reads off the board (its question): the attackers present gathered into a list the leg keeps, the fight-back's pick with its skip made once and the lull test, the later of two at one
    /// distance, allocate nothing, under attack and after it.
    /// </summary>
    [Fact]
    public void The_travel_legs_present_pick_and_lull_reads_off_the_board_allocate_nothing()
    {
        var t = Busy();
        using var watch = t.LegWatch(); // built after the attacks: the board still reads fifteen
        var here = t.View.State.Self.Position;
        var standers = new List<ThreatStander>(32);
        var query = ThreatQuery.OnHer(ThreatRig.LegReach, ThreatRig.LegSeconds, here);
        Func<uint, bool> skip = static guid => (guid & 1) == 0;
        var start = t.Now;
        var round = 0;
        var (gathered, picked, onHer) = (0, 0, 0);

        AllocationAssert.Nothing(() =>
        {
            var now = start + 0.002 + (++round * 0.001);
            for (var i = 0; i < 100; i++)
            {
                now += 0.00001;
                gathered += t.Board.Gather(t.View, in query, now, standers).Count;
                var pick = query with { Skip = skip, TiesToLast = true };
                picked += t.Board.Ask(t.View, in pick, now).Any ? 1 : 0;
                var lull = query with { TiesToLast = true };
                onHer += t.Board.Ask(t.View, in lull, now).Any ? 1 : 0;
            }
        });
        Assert.True(gathered > 0 && standers.Count > 0 && picked > 0 && onHer > 0, $"gathered {gathered}, standing {standers.Count}, picked {picked}, on her {onHer}");

        t.Wait(11); // the attacks are over: every read is none
        var later = t.Now;
        (picked, onHer) = (0, 0);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                later += 0.016;
                t.Board.Gather(t.View, in query, later, standers);
                var lull = query with { TiesToLast = true };
                onHer += t.Board.Ask(t.View, in lull, later).Any ? 1 : 0;
            }
        });
        Assert.Empty(standers);
        Assert.Equal(0, onHer);
    }
}
