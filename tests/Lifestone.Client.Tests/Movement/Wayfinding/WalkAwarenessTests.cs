using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 16 E1f (docs/automation-engine.md E1; m6-plan.md D139): the walker keeps off a creature by the kind's own range from the world database (the default since M6 16 Q3) or, under <c>--walk-awareness=pinned</c>, by the pinned awareness range (every walk before the option)
/// (the world database's range is the other); in both modes the session's <see cref="WalkAwareness"/> reports what the other range would have changed (<c>awareness-shadow:</c>). The creature kinds are the bestiary's: an Assailer 22053 sees 25 m and is read at
/// ACE's default of 18 m; a Tusker Minion 22511 sees 15 m and is read at 18 m; a Drudge Skulker 7 is pinned at its own 10 m; an Auroch Bull 20 is pinned at 22 m and in no table (<c>CreatureBehaviourTests</c>, the tables written by
/// <c>fight-sim bestiary</c>).
/// </summary>
public sealed class WalkAwarenessTests(ITestOutputHelper output)
{
    private const uint Assailer = 22053, Minion = 22511, Skulker = 7, AurochBull = 20, NoCreature = 999_999_999;

    /// <summary>A creature's disc with no Setup read: its awareness, the 1 m of an unread body and the player's 0.48 m.</summary>
    private static float Disc(float awareness) => NavCreatures.AggroRadius(awareness);

    [Fact]
    public void The_walk_keeps_the_kind_s_own_range_by_default_the_pinned_range_under_pinned_and_the_option_changes_no_other_value_of_the_layer()
    {
        var table = Table();
        var feed = new HostileCreatureFeed(table, self: 99);
        var pinned = new List<NavCreature>();
        var own = new List<NavCreature>();

        Assert.Equal(WalkAwarenessMode.Own, new WorldObjectTable().WalkAwareness.Mode); // the default since M6 16 Q3: each kind's own range
        feed.Fill(P(0, 0, 0x01050000).Global, 100f, pinned);
        table.WalkAwareness.Mode = WalkAwarenessMode.Own;
        feed.Fill(P(0, 0, 0x01050000).Global, 100f, own);

        // under pinned (the option) every disc is the pinned range's, else ACE's 18 m: what the feed gave before the option
        Assert.Equal([Disc(18f), Disc(18f), Disc(10f), Disc(22f), Disc(18f)], Radii(pinned));
        Assert.All(pinned, c => Assert.Equal(NavCreatures.AggroRadius(CreatureAwareness.MetresOf(Wcid(table, c.Guid))), c.Radius));

        // under own: the Assailer's 25 m and the Minion's 15 m; the pinned Skulker, the cattle's pin and a weenie that is no creature are unchanged
        Assert.Equal([Disc(25f), Disc(15f), Disc(10f), Disc(22f), Disc(18f)], Radii(own));

        // and the option switches the radius alone: the same creatures in the same order, each at the same place and height
        Assert.Equal(pinned.Select(c => (c.Guid, c.Centre, c.Z)), own.Select(c => (c.Guid, c.Centre, c.Z)));
        Assert.Equal([1u, 2u], pinned.Zip(own).Where(p => p.First.Radius != p.Second.Radius).Select(p => p.First.Guid).Order());

        static float[] Radii(List<NavCreature> layer) => [.. layer.OrderBy(c => c.Guid).Select(c => c.Radius)];
    }

    [Fact]
    public void Every_kind_s_own_range_is_the_table_s_through_the_server_s_scale_and_the_pinned_read_answers_as_before()
    {
        var differing = 0;
        foreach (var kind in Bestiary.All)
        {
            Assert.Equal((float)kind.Awareness.TableVisualMetres!.Value * CreatureAwareness.ServerScale, Bestiary.OwnAwarenessMetres(kind.Wcid));
            Assert.Equal(CreatureAwareness.MetresOf(kind.Wcid), Bestiary.AwarenessMetres(kind.Wcid)); // every other caller's read
            differing += Bestiary.OwnAwarenessMetres(kind.Wcid) != Bestiary.AwarenessMetres(kind.Wcid) ? 1 : 0;
        }

        output.WriteLine($"of the bestiary's {Bestiary.Count} kinds, {differing} have an own range that is not the range read today");

        // the pins are the check: all 74 answer the same under both reads, each by the table's value: the belt's three cattle too, rows of the table from M6 16 E1e on
        Assert.Equal(74, CreatureAwareness.Wcids.Count());
        Assert.All(CreatureAwareness.Wcids, wcid => Assert.Equal(Bestiary.AwarenessMetres(wcid), Bestiary.OwnAwarenessMetres(wcid)));
        Assert.All(new uint[] { 20, 181, 182 }, wcid =>
        {
            Assert.Equal(22.0, Bestiary.Find(wcid)!.Awareness.TableVisualMetres);
            Assert.Equal(22f, Bestiary.OwnAwarenessMetres(wcid));
        });

        Assert.Equal((18f, 25f), (Bestiary.AwarenessMetres(Assailer), Bestiary.OwnAwarenessMetres(Assailer)));
        Assert.Equal((18f, 15f), (Bestiary.AwarenessMetres(Minion), Bestiary.OwnAwarenessMetres(Minion)));
        Assert.Equal((18f, 18f), (Bestiary.AwarenessMetres(NoCreature), Bestiary.OwnAwarenessMetres(NoCreature))); // ACE's default, both ways
    }

    [Fact]
    public void A_kind_whose_own_range_differs_is_named_once_a_session_whatever_feed_sees_it()
    {
        var table = Table();
        var log = new RecordingLogSink();
        var into = new List<NavCreature>();
        var first = new HostileCreatureFeed(table, self: 99, log);
        var second = new HostileCreatureFeed(table, self: 99, log); // another runner's feed over the same session

        first.Fill(P(0, 0, 0x01050000).Global, 100f, into);
        first.Fill(P(0, 0, 0x01050000).Global, 100f, into);
        second.Fill(P(0, 0, 0x01050000).Global, 100f, into);

        var shadow = log.Entries.Where(e => e.Message.StartsWith(WalkAwareness.Token, StringComparison.Ordinal)).ToList();
        Assert.Equal(
            ["awareness-shadow: Assailer (class 22053): the walk keeps 18 m, its own range gives 25 m", "awareness-shadow: Tusker Minion (class 22511): the walk keeps 18 m, its own range gives 15 m"],
            shadow.Select(e => e.Message).Order(StringComparer.Ordinal));
        Assert.All(shadow, e => Assert.Equal((LogSeverity.Info, "wayfinding"), (e.Severity, e.Category)));
        Assert.Equal(2, table.WalkAwareness.KindsDiffering);

        // under own the line says what the walk now keeps, and what the pinned read gave
        var ownTable = Table();
        ownTable.WalkAwareness.Mode = WalkAwarenessMode.Own;
        var ownLog = new RecordingLogSink();
        new HostileCreatureFeed(ownTable, self: 99, ownLog).Fill(P(0, 0, 0x01050000).Global, 100f, into);
        Assert.Contains(ownLog.Entries, e => e.Message == "awareness-shadow: Assailer (class 22053): the walk keeps 25 m, its own range gives 25 m; the pinned read gives 18 m");

        // a feed with no log names nothing and loses nothing: the first feed with one names the kind
        var quiet = Table();
        new HostileCreatureFeed(quiet, self: 99).Fill(P(0, 0, 0x01050000).Global, 100f, into);
        Assert.Equal(2, quiet.WalkAwareness.KindsDiffering);
        var later = new RecordingLogSink();
        new HostileCreatureFeed(quiet, self: 99, later).Fill(P(0, 0, 0x01050000).Global, 100f, into);
        Assert.Equal(2, later.Entries.Count(e => e.Message.StartsWith(WalkAwareness.Token, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_leg_counts_each_creature_seen_inside_its_own_disc_and_outside_the_pinned_one_and_names_the_closest()
    {
        var shadow = new WalkAwareness { Mode = WalkAwarenessMode.Pinned };
        shadow.Seen(1, Assailer, "Assailer", 22f, Disc(18f), Disc(25f)); // no leg open: nothing
        Assert.Null(shadow.EndLeg("no leg"));
        Assert.Equal(0, shadow.Legs);

        shadow.BeginLeg();
        Assert.True(shadow.LegOpen);
        shadow.Attacked(1); // heard before she passed it: it is not one of the leg's passes yet
        shadow.Seen(1, Assailer, "Assailer", 30f, Disc(18f), Disc(25f)); // outside its own disc (26.48 m)
        shadow.Seen(1, Assailer, "Assailer", 24f, Disc(18f), Disc(25f)); // inside it, outside the pinned disc (19.48 m): counted
        shadow.Seen(1, Assailer, "Assailer", 21.3f, Disc(18f), Disc(25f)); // closer
        shadow.Seen(1, Assailer, "Assailer", 4f, Disc(18f), Disc(25f)); // it ran at her: inside the pinned disc is no sample of the pass
        shadow.Seen(2, 27800, "Infected Assailer", 25f, Disc(18f), Disc(25f)); // a second
        shadow.Seen(3, Assailer, "Assailer", 10f, Disc(18f), Disc(25f)); // first seen inside the pinned disc: the walk knew that ground
        shadow.Seen(4, Minion, "Tusker Minion", 17f, Disc(18f), Disc(15f)); // it notices less far than it is read: never a pass
        shadow.Seen(5, Skulker, "Drudge Skulker", 11f, Disc(10f), Disc(10f)); // the two ranges are one
        shadow.Attacked(2); // then among her attackers
        shadow.Attacked(3); // an attacker the leg did not count
        shadow.Attacked(77); // an attacker the layer never saw

        Assert.Equal(
            "awareness-shadow: the run leg to entry-front: 2 creature(s) passed inside their own range and outside the pinned radius, 1 of them among her attackers; closest 'Assailer' (class 22053) 0x00000001 at 21.3 m (pinned radius 19.5 m, own 26.5 m); mode=pinned passed=2 attackers=1 closest_m=21.3 session_legs=1 session_passed=2 session_attackers=1",
            shadow.EndLeg("the run leg to entry-front"));
        Assert.False(shadow.LegOpen);

        // a leg with no such pass writes no line and still counts as a leg; the session's totals carry over
        shadow.BeginLeg();
        shadow.Seen(3, Assailer, "Assailer", 10f, Disc(18f), Disc(25f));
        Assert.Null(shadow.EndLeg("the hop leg to walk-in-front"));
        shadow.BeginLeg();
        shadow.Seen(1, Assailer, "Assailer", 20f, Disc(18f), Disc(25f)); // the creature of the first leg, passed again on another: counted on that leg too
        shadow.Attacked(1);
        var third = shadow.EndLeg("the walk-home leg to home-lifestone-point");
        Assert.EndsWith("mode=pinned passed=1 attackers=1 closest_m=20.0 session_legs=3 session_passed=3 session_attackers=2", third, StringComparison.Ordinal);
        Assert.Equal((3, 3, 2), (shadow.Legs, shadow.Passed, shadow.Attackers));

        // a leg left open is dropped by the next one's start, not carried into it
        shadow.BeginLeg();
        shadow.Seen(9, Assailer, "Assailer", 22f, Disc(18f), Disc(25f));
        shadow.BeginLeg();
        Assert.Null(shadow.EndLeg("the run leg to entry-front"));
        Assert.Equal((4, 3, 2), (shadow.Legs, shadow.Passed, shadow.Attackers));
    }

    [Fact]
    public void The_feed_gives_an_open_leg_each_creature_s_distance_from_the_body_and_its_two_discs()
    {
        var table = Table(); // the Assailer stands 22 m from the landblock's origin, the Minion 17 m, the Skulker 30 m, the bull 36 m, the weenie that is no creature 38 m
        var feed = new HostileCreatureFeed(table, self: 99);
        var into = new List<NavCreature>();
        var shadow = table.WalkAwareness;

        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into); // no leg: the layer is filled, nothing is counted
        shadow.BeginLeg();
        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);
        feed.Fill(P(0, 1.5f, 0x01050000).Global, 100f, into); // the body a step aside: a little farther from the Assailer
        shadow.Attacked(1);

        Assert.Equal(
            "awareness-shadow: the run leg to entry-front: 1 creature(s) passed inside their own range and outside the pinned radius, 1 of them among her attackers; closest 'Assailer' (class 22053) 0x00000001 at 22.0 m (pinned radius 19.5 m, own 26.5 m); mode=pinned passed=1 attackers=1 closest_m=22.0 session_legs=1 session_passed=1 session_attackers=1",
            shadow.EndLeg("the run leg to entry-front"));

        // under own the walk keeps that disc, and the leg still says she was inside it: the count the flip is judged by
        shadow.Mode = WalkAwarenessMode.Own;
        shadow.BeginLeg();
        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);
        Assert.Contains("mode=own passed=1 attackers=0 closest_m=22.0 session_legs=2 session_passed=2 session_attackers=1", shadow.EndLeg("the run leg to entry-front"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_shadow_says_once_a_session_that_it_watches_and_which_range_the_walks_keep()
    {
        var shadow = new WalkAwareness { Mode = WalkAwarenessMode.Pinned };
        Assert.Equal(
            "awareness-shadow: watching (--walk-awareness=pinned): the walks keep the pinned ranges off a creature; a kind whose own range is another is named once a session, and a leg names once the creatures it passed inside their own range and outside the pinned radius",
            shadow.Announce());
        Assert.Null(shadow.Announce());
        Assert.StartsWith("awareness-shadow: watching (--walk-awareness=own): the walks keep each kind's own range off a creature;", new WalkAwareness { Mode = WalkAwarenessMode.Own }.Announce(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_warm_fill_with_the_shadow_on_an_open_leg_allocates_nothing()
    {
        var table = Table();
        var feed = new HostileCreatureFeed(table, self: 99, new RecordingLogSink());
        var into = new List<NavCreature>();
        var from = P(0, 0, 0x01050000).Global;
        table.WalkAwareness.BeginLeg();
        feed.Fill(from, 100f, into); // the bestiary loaded, the kinds named, the leg's passes counted, the lists grown

        AllocationAssert.Nothing(() =>
        {
            feed.Fill(from, 100f, into);
            table.WalkAwareness.Attacked(1);
            table.WalkAwareness.Attacked(77);
        });
        Assert.Equal(5, into.Count);
    }

    [Fact]
    public void A_larger_disc_is_the_same_rule_with_a_bigger_number_round_it_in_open_ground_and_through_the_only_gap_never_a_failed_plan()
    {
        // open ground: the route goes round the disc, farther off for the larger one
        var open = new PathPlanner(Map(Flat())) { Creatures = new NavCreatures() };
        var nearest = new List<float>();
        foreach (var awareness in new[] { 18f, 25f, 30f })
        {
            var creature = new NavCreature(1, P(95, 96).Global, Disc(awareness));
            open.Creatures.Set([creature]);
            var result = open.Plan(P(20, 96), NavGoal.ToPoint(P(170, 96)));
            Assert.Equal(NavStatus.Found, result.Status);
            var off = MinDistance(result.Route!, creature.Centre);
            Assert.True(off >= creature.Radius - 1.5f, $"awareness {awareness} m: the route keeps out of the disc: {off:F1} m from the creature, the disc {creature.Radius:F1} m");
            nearest.Add(off);
        }

        Assert.True(nearest[0] < nearest[1] && nearest[1] < nearest[2], $"the larger the disc, the farther round: {string.Join(", ", nearest.Select(n => n.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");

        // a creature in the only gap of a wall: the plan is found and goes through the gap, past the creature, whatever the disc; it costs more, it never fails and never waits
        var tile = Flat().Box(90, 0, 92, 90).Box(90, 102, 92, 191);
        var gap = new PathPlanner(Map(tile)) { Creatures = new NavCreatures() };
        foreach (var awareness in new[] { 18f, 25f, 30f })
        {
            gap.Creatures.Set([new NavCreature(1, P(91, 96).Global, Disc(awareness))]);
            var result = gap.Plan(P(20, 96), NavGoal.ToPoint(P(170, 96)));
            Assert.Equal(NavStatus.Found, result.Status);
            Assert.Equal(1, gap.CreaturesStamped);
            Assert.True(MinDistance(result.Route!, P(91, 96).Global) < 6f, $"awareness {awareness} m: through the gap, past the creature");
        }

        // the cost of a metre at one distance from the creature rises with the disc: the same formula, a bigger radius
        Assert.True(NavCreatures.CostPerMetre(5f, Disc(25f)) > NavCreatures.CostPerMetre(5f, Disc(18f)));
        Assert.Equal(0f, NavCreatures.CostPerMetre(22f, Disc(18f))); // 22 m from an Assailer costs nothing by the pinned disc
        Assert.True(NavCreatures.CostPerMetre(22f, Disc(25f)) > 0f); // and something by its own
    }

    [Fact]
    public void A_runner_over_the_sessions_feed_goes_round_the_pinned_disc_by_default_and_round_the_kinds_own_disc_under_own()
    {
        // the same run twice, an Assailer standing on the straight line: by default the walk keeps its 19.48 m disc (ACE's 18 m), under own its 26.48 m disc (its own 25 m)
        var pinned = RunPast(WalkAwarenessMode.Pinned);
        var own = RunPast(WalkAwarenessMode.Own);

        Assert.Equal((NavRunState.Arrived, NavRunState.Arrived), (pinned.State, own.State));
        Assert.True(pinned.Nearest >= Disc(18f) - 2f, $"under pinned the body came {pinned.Nearest:F1} m from the creature, the pinned disc {Disc(18f):F1} m");
        Assert.True(pinned.Nearest < Disc(25f), $"under pinned the body came {pinned.Nearest:F1} m from the creature: inside its own disc of {Disc(25f):F1} m, which the walk does not know");
        Assert.True(own.Nearest >= Disc(25f) - 2f, $"under own the body came {own.Nearest:F1} m from the creature, its own disc {Disc(25f):F1} m");
        Assert.True(own.Nearest > pinned.Nearest, $"the wider disc is a wider way round: {own.Nearest:F1} m against {pinned.Nearest:F1} m");

        static (NavRunState State, float Nearest) RunPast(WalkAwarenessMode mode)
        {
            var block = MovementTestKit.Block.LandBlockId;
            var table = new WorldObjectTable();
            table.WalkAwareness.Mode = mode;
            table.Set(Make(1, Assailer, "Assailer", 95, y: 96, cell: block | 0x0001));
            var harness = new ActionHarness(offline: true);
            var body = new S5KinematicBody(20, 96, heading: 90f);
            harness.Api.Move.Bind(body, false);
            var runner = new NavRunner(harness.Api.Move, body, new OutdoorPathSource(new PathPlanner(Map(Flat(block)))), creatures: new HostileCreatureFeed(table, self: 99));
            var creature = NavPoint.At(block, 95, 96).Global;
            var nearest = float.MaxValue;
            runner.Start(NavGoal.ToPoint(NavPoint.At(block, 170, 96)), body.Now);
            while (!runner.IsDone && body.Now < 60)
            {
                body.Advance(S5Rig.Dt);
                runner.Step(body.Now);
                harness.Tick(S5Rig.Dt);
                nearest = Math.Min(nearest, Vector2.Distance(NavMap.FromCellFrame(body.Position).Global, creature));
            }

            return (runner.State, nearest);
        }
    }

    [Fact]
    public void The_option_is_own_by_default_takes_pinned_or_own_and_the_bootstrap_hands_it_to_the_sessions_object_table()
    {
        Assert.Equal(WalkAwarenessMode.Own, SessionOptions.Parse([]).WalkAwareness);
        Assert.Equal(WalkAwarenessMode.Pinned, SessionOptions.Parse(["--walk-awareness=pinned"]).WalkAwareness);
        Assert.Equal(WalkAwarenessMode.Own, SessionOptions.Parse(["--walk-awareness=own"]).WalkAwareness);
        Assert.Equal(WalkAwarenessMode.Pinned, SessionOptions.Parse(["--walk-awareness=pinned"]).WalkAwareness); // pinned still works as an option
        Assert.Empty(SessionOptions.Parse(["--walk-awareness=own"]).Errors);
        Assert.Equal(WalkAwarenessMode.Pinned, SessionOptions.Parse(["--walk-awareness=own", "--walk-awareness=pinned"]).WalkAwareness); // the last one wins
        Assert.Equal(WalkAwarenessMode.Own, SessionOptions.Parse(["--walk-awareness=pinned", "--walk-awareness=own"]).WalkAwareness);

        var bad = SessionOptions.Parse(["--walk-awareness=both"]);
        Assert.Equal("--walk-awareness: 'both' is not one of pinned, own", Assert.Single(bad.Errors));
        Assert.Equal(WalkAwarenessMode.Own, bad.WalkAwareness); // a bad value keeps the default
        Assert.Single(SessionOptions.Parse(["--walk-awareness"]).Errors); // it is no switch: a value is needed

        var registry = OptionCatalog.Registry;
        Assert.Equal("Autopilot", registry.FeatureOf(WalkAwareness.OptionName)); // listed with the automation's options, read by the session's (as --autopilot-player-rules is)
        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, registry.ScenesOf("--walk-awareness"));
        Assert.Equal("--walk-awareness=pinned|own", registry.Find("--walk-awareness")!.Usage);
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--walk-awareness=own"]), OptionScenes.Main));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--walk-awareness=own"]), OptionScenes.Headless));

        using var byDefault = new LiveHarness(record: false);
        Assert.Equal(WalkAwarenessMode.Own, byDefault.Session.State.Objects.WalkAwareness.Mode);
        Assert.DoesNotContain(byDefault.Log.Entries, e => e.Message.StartsWith("walk awareness:", StringComparison.Ordinal)); // the default writes no line
        using var pinned = new LiveHarness(record: false, options: SessionOptions.Parse(["--account=test", "--walk-awareness=pinned"]));
        Assert.Equal(WalkAwarenessMode.Pinned, pinned.Session.State.Objects.WalkAwareness.Mode);
        Assert.Contains(pinned.Log.Entries, e => e.Message.StartsWith("walk awareness: the walks keep each creature kind's pinned awareness range off it (--walk-awareness=pinned", StringComparison.Ordinal));
    }

    /// <summary>The five creatures of these tests in one landblock, each <c>x</c> metres east of its origin: 1 the Assailer at 22, 2 the Tusker Minion at 17, 3 the Drudge Skulker at 30, 4 the Auroch Bull at 36, 5 a weenie that is no creature at 38.</summary>
    private static WorldObjectTable Table(WalkAwarenessMode mode = WalkAwarenessMode.Pinned)
    {
        var table = new WorldObjectTable();
        table.WalkAwareness.Mode = mode; // the tests compare the two ranges, so each names the one it starts from (the product's default is Own, M6 16 Q3)
        table.Set(Make(1, Assailer, "Assailer", 22));
        table.Set(Make(2, Minion, "Tusker Minion", 17));
        table.Set(Make(3, Skulker, "Drudge Skulker", 30));
        table.Set(Make(4, AurochBull, "Auroch Bull", 36));
        table.Set(Make(5, NoCreature, "Nothing", 38));
        return table;
    }

    private static uint Wcid(WorldObjectTable table, uint guid) => table.TryGet(guid, out var o) ? o.Weenie.WeenieClassId : throw new InvalidOperationException($"no object {guid}");

    private static float MinDistance(NavRoute route, Vector2 point) => Samples(route, 0.25f).Min(p => Vector2.Distance(p.Global, point));

    private static WorldObject Make(uint guid, uint wcid, string name, float x, float y = 0f, uint cell = 0x01050303)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = name,
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = HostileCreatureFeed.CreatureItemType,
            ObjectDescriptionFlags = HostileCreatureFeed.AttackableFlag,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0)
        {
            Position = new CellFrame(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity)),
        };
    }
}
