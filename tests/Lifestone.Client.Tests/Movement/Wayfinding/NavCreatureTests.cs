using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The wayfinder's creature layer (nav-creature-avoid, docs/wayfinding.md "The creature layer"): the cost of a creature's aggro disc, the planner
/// going round a creature it would have crossed (but not round the run's own goal creature, and through a gap a creature stands in rather than
/// failing), the feed's hostile rule, the pinned awareness, and the runner replanning when a creature walks onto the route ahead.
/// </summary>
public sealed class NavCreatureTests
{
    private const uint Drudge = 0x80001234;

    [Fact]
    public void A_disc_costs_one_more_a_metre_at_its_edge_five_at_the_creature_and_nothing_outside()
    {
        Assert.Equal(18f + 1f + 0.48f, NavCreatures.AggroRadius(18f));
        Assert.Equal(0f, NavCreatures.CostPerMetre(20f, 19.48f));
        Assert.Equal(1f, NavCreatures.CostPerMetre(19.479f, 19.48f), 2);
        Assert.Equal(5f, NavCreatures.CostPerMetre(0f, 19.48f), 3);

        var layer = new NavCreatures();
        layer.Set([new NavCreature(1, new Vector2(0, 0), 10f), new NavCreature(2, new Vector2(4, 0), 10f)]);
        Assert.Equal(NavCreatures.CostPerMetre(2f, 10f) * 2, layer.CostAt(new Vector2(2, 0)), 4); // overlapping discs add
        Assert.Equal(0f, layer.ExposureAlong(new Vector2(-30, 30), new Vector2(30, 30)));
        Assert.True(layer.ExposureAlong(new Vector2(-30, 0), new Vector2(30, 0)) > layer.ExposureAlong(new Vector2(-30, 9), new Vector2(30, 9)));
    }

    [Fact]
    public void The_planner_goes_round_a_creature_it_would_have_crossed()
    {
        var map = Map(Flat());
        var planner = new PathPlanner(map);
        var goal = NavGoal.ToPoint(P(170, 96));
        var creature = new NavCreature(Drudge, P(95, 96).Global, NavCreatures.AggroRadius(CreatureAwareness.MetresOf(7))); // a Drudge Skulker: 10 m

        var bare = planner.Plan(P(20, 96), goal).Route!;
        Assert.Equal(2, bare.Vertices.Count);
        Assert.True(MinDistance(bare, creature.Centre) < 1f, "the straight route crosses the creature");

        planner.Creatures = new NavCreatures();
        planner.Creatures.Set([creature]);
        var result = planner.Plan(P(20, 96), goal);

        Assert.Equal(NavStatus.Found, result.Status);
        Assert.Equal(1, planner.CreaturesStamped);
        var round = result.Route!;
        Assert.True(MinDistance(round, creature.Centre) >= creature.Radius - 1.5f, $"the route keeps out of the disc: {MinDistance(round, creature.Centre):F1} m from the creature, the disc {creature.Radius:F1} m");
        Assert.InRange(round.ArcLength, bare.ArcLength, bare.ArcLength * 1.25f);
        Assert.InRange(round.Vertices.Count, 3, 8); // a few straight legs round it, not a vertex a metre
    }

    [Fact]
    public void The_runs_own_goal_creature_is_left_out()
    {
        var map = Map(Flat());
        var planner = new PathPlanner(map) { Creatures = new NavCreatures() };
        planner.Creatures.Set([new NavCreature(Drudge, P(95, 96).Global, 19.48f)]);

        var seek = planner.Plan(P(20, 96), NavGoal.ToObject(Drudge, P(95, 96), 2f, needSight: false));

        Assert.Equal(NavStatus.Found, seek.Status);
        Assert.Equal(0, planner.CreaturesStamped);
        Assert.Equal(2, seek.Route!.Vertices.Count); // straight at it
    }

    [Fact]
    public void A_creature_in_the_only_gap_costs_the_route_but_never_fails_the_plan()
    {
        var tile = Flat().Box(90, 0, 92, 90).Box(90, 102, 92, 191); // a wall across the tile with a gap at y 91 to 101
        var planner = new PathPlanner(Map(tile)) { Creatures = new NavCreatures() };
        planner.Creatures.Set([new NavCreature(Drudge, P(91, 96).Global, 19.48f)]);

        var result = planner.Plan(P(20, 96), NavGoal.ToPoint(P(170, 96)));

        Assert.Equal(NavStatus.Found, result.Status);
        Assert.True(MinDistance(result.Route!, P(91, 96).Global) < 6f, "through the gap, past the creature");
    }

    [Fact]
    public void The_feed_lists_the_hostile_creatures_alive_and_placed_and_not_the_player_npcs_vendors_or_the_dead()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 0x10, 0x10, 7, 10)); // a Drudge Skulker: hostile
        table.Set(Make(2, 0x10, 0x10 | 0x8, 0, 12)); // a player
        table.Set(Make(3, 0x10, 0x10 | 0x200, 0, 14)); // a vendor
        table.Set(Make(4, 0x10, 0x0, 0, 16)); // an NPC: not attackable
        table.Set(Make(5, 0x1000, 0x10, 0, 18)); // not a creature
        var dead = Make(6, 0x10, 0x10, 0, 20);
        dead.Health = 0f;
        table.Set(dead);
        var dying = Make(7, 0x10, 0x10, 0, 22);
        dying.Motion = new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord };
        table.Set(dying);
        table.Set(Make(8, 0x10, 0x10, 99999, 24)); // an unpinned creature: ACE's default 18 m
        table.Set(Make(9, 0x10, 0x10, 7, 150)); // too far
        table.Set(Make(10, 0x10, 0x10, 7, 30)); // the player itself (by id)
        var feed = new HostileCreatureFeed(table, self: 10);
        var into = new List<NavCreature>();

        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);

        Assert.Equal([1u, 8u], into.Select(c => c.Guid).Order());
        Assert.Equal(NavCreatures.AggroRadius(10f), into.Single(c => c.Guid == 1).Radius);
        Assert.Equal(NavCreatures.AggroRadius(CreatureAwareness.DefaultMetres), into.Single(c => c.Guid == 8).Radius);
    }

    [Fact]
    public void The_awareness_is_the_world_databases_for_the_hunt_grounds_creatures_and_aces_default_otherwise()
    {
        Assert.Equal(74, CreatureAwareness.Count);
        Assert.Equal(10f, CreatureAwareness.MetresOf(7)); // Drudge Skulker
        Assert.Equal(8f, CreatureAwareness.MetresOf(4110)); // Blood Shreth
        Assert.Equal(25f, CreatureAwareness.MetresOf(235)); // Goldenback Tusker
        Assert.Equal(35f, CreatureAwareness.MetresOf(1989)); // Shadow Wisp
        Assert.False(CreatureAwareness.IsPinned(1));
        Assert.Equal(18f, CreatureAwareness.MetresOf(1)); // VisualAwarenessRange_Default
    }

    [Fact]
    public void The_runner_replans_once_when_a_creature_walks_onto_the_route_ahead_and_passes_outside_its_disc()
    {
        var rig = new CreatureRig(20, 96);
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 170, 96)), rig.Body.Now);
        rig.RunFor(1.0); // a straight start: nothing in the way
        Assert.Equal(0, rig.Runner.CreatureReplans);
        var creature = new NavCreature(Drudge, NavPoint.At(rig.Block, 110, 96).Global, NavCreatures.AggroRadius(10f));
        rig.Creatures.Add(creature);

        var nearest = float.MaxValue;
        while (!rig.Runner.IsDone && rig.Body.Now < 40)
        {
            rig.Frame();
            nearest = Math.Min(nearest, Vector2.Distance(NavMap.FromCellFrame(rig.Body.Position).Global, creature.Centre));
        }

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(1, rig.Runner.CreatureReplans);
        Assert.True(nearest >= creature.Radius - 2f, $"the body came {nearest:F1} m from the creature, its disc {creature.Radius:F1} m");
        Assert.Contains("creatures_max=1 creature_replans=1 creature_min_m=", rig.Runner.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_creature_the_plan_knew_and_the_runs_own_goal_creature_are_no_replan()
    {
        var known = new CreatureRig(20, 96);
        known.Creatures.Add(new NavCreature(Drudge, NavPoint.At(known.Block, 95, 96).Global, NavCreatures.AggroRadius(10f)));
        known.Runner.Start(NavGoal.ToPoint(NavPoint.At(known.Block, 170, 96)), known.Body.Now);
        known.RunFor(40);
        Assert.Equal(NavRunState.Arrived, known.Runner.State);
        Assert.Equal(0, known.Runner.CreatureReplans);
        Assert.True(known.Runner.CreatureMinMetres >= NavCreatures.AggroRadius(10f) - 2f);

        var seek = new CreatureRig(20, 96);
        seek.Creatures.Add(new NavCreature(Drudge, NavPoint.At(seek.Block, 95, 96).Global, NavCreatures.AggroRadius(10f)));
        seek.Runner.Start(NavGoal.ToObject(Drudge, NavPoint.At(seek.Block, 95, 96), 2f, needSight: false), seek.Body.Now);
        seek.RunFor(20);
        Assert.Equal(NavRunState.Arrived, seek.Runner.State);
        Assert.Equal(0, seek.Runner.CreaturesMax); // the goal is left out of the layer
        Assert.Null(seek.Runner.CreatureMinMetres);
    }

    [Fact]
    public void A_warm_runner_with_a_creature_layer_allocates_nothing_a_step_while_it_runs_straight()
    {
        var rig = new CreatureRig(20, 96);
        rig.Creatures.Add(new NavCreature(Drudge, NavPoint.At(rig.Block, 95, 150).Global, NavCreatures.AggroRadius(10f))); // off the route: refreshed, never replanned for
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 170, 96)), rig.Body.Now);
        rig.RunFor(2.0); // the plan, the turn onto the line, the pools grown
        var keys = rig.Runner.KeyChanges;

        long allocated = 0;
        for (var i = 0; i < 60; i++) // two seconds: four refreshes of the layer
        {
            rig.Body.Advance(S5Rig.Dt);
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.Runner.Step(rig.Body.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            rig.H.Tick(S5Rig.Dt);
        }

        Assert.Equal(keys, rig.Runner.KeyChanges); // straight: no key changed
        Assert.Equal(0, allocated);
    }

    private static float MinDistance(NavRoute route, Vector2 point) => Samples(route, 0.25f).Min(p => Vector2.Distance(p.Global, point));

    private static WorldObject Make(uint guid, uint itemType, uint flags, uint wcid, float x)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "creature",
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = itemType,
            ObjectDescriptionFlags = flags,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0)
        {
            Position = new CellFrame(0x01050303, new AcFrame(new Vector3(x, 0f, 0f), Quaternion.Identity)),
        };
    }

    /// <summary>The scripted body over a flat tile with the real planner and a feed the test fills.</summary>
    private sealed class CreatureRig : INavCreatureFeed
    {
        public CreatureRig(float x, float y)
        {
            H = new ActionHarness(offline: true);
            Body = new S5KinematicBody(x, y, heading: 90f);
            H.Api.Move.Bind(Body, false);
            Runner = new NavRunner(H.Api.Move, Body, new OutdoorPathSource(new PathPlanner(Map(Flat(Block)))), creatures: this);
        }

        public uint Block { get; } = MovementTestKit.Block.LandBlockId;

        public ActionHarness H { get; }

        public S5KinematicBody Body { get; }

        public NavRunner Runner { get; }

        public List<NavCreature> Creatures { get; } = [];

        public void Fill(Vector2 from, float maxMetres, List<NavCreature> into)
        {
            into.Clear();
            foreach (var c in Creatures)
            {
                if (Vector2.Distance(c.Centre, from) <= maxMetres)
                {
                    into.Add(c);
                }
            }
        }

        public void Frame()
        {
            Body.Advance(S5Rig.Dt);
            Runner.Step(Body.Now);
            H.Tick(S5Rig.Dt);
        }

        public void RunFor(double seconds)
        {
            var end = Body.Now + seconds;
            while (Body.Now < end && !Runner.IsDone)
            {
                Frame();
            }
        }
    }
}
