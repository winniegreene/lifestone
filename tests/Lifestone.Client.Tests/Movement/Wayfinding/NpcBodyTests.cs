using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Movement.Wayfinding.S2TileKit;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 4 W2 (<c>m6-npc-in-door-lane</c>, docs/wayfinding.md "The creature layer"): the NPCs' bodies on the plan. The feed's rule (the creatures the creature layer
/// leaves out, solid and alive), the disc (the walker's radius plus the body's own), the bodies on the scars apart from the timed scars, the plan going round an NPC
/// it would have walked through, the request's own NPC and an NPC holding the goal left out, a body on another level left out, and an NPC in the only gap: waited for,
/// planned once more, then named <c>npc-in-lane</c>, never pushed.
/// </summary>
public sealed class NpcBodyTests
{
    private const uint Scrivener = 0x7A9B301B;
    private const float HumanRadius = 0.68f; // the human Setup 0x02000001's radius, as HoltburgScrivenerWalkDatTests reads it from the dats

    [Fact]
    public void The_feed_lists_the_npcs_and_vendors_alive_placed_and_solid_and_not_the_hostile_the_player_or_the_ethereal()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 0x10, 0x0, 10)); // an NPC: not attackable
        table.Set(Make(2, 0x10, 0x10 | 0x200, 12, setup: 0x02000001, scale: 2f)); // a vendor (attackable bit set, vendor bit set): a body
        table.Set(Make(3, 0x10, 0x10, 14)); // a hostile creature: the creature layer's, not a body
        table.Set(Make(4, 0x10, 0x8, 16)); // a player
        table.Set(Make(5, 0x1000, 0x0, 18)); // not a creature
        var ghost = Make(6, 0x10, 0x0, 20);
        ghost.State = PhysicsState.Ethereal; // the physics lets the body through
        table.Set(ghost);
        var dead = Make(7, 0x10, 0x0, 22);
        dead.Health = 0f;
        table.Set(dead);
        table.Set(Make(8, 0x10, 0x0, 24, cell: 0x01050110)); // an NPC in a room
        table.Set(Make(9, 0x10, 0x0, 150)); // too far
        table.Set(Make(10, 0x10, 0x0, 30)); // the player itself (by id)
        var log = new CountingLogSink(new RecordingLogSink());
        var feed = new NpcBodyFeed(table, self: 10, log, id => id == 0x02000001 ? HumanRadius : null);
        var into = new List<NavBody>();

        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);

        Assert.Equal([1u, 2u, 8u], into.Select(b => b.Guid).Order());
        Assert.Equal(NpcBodyFeed.PersonRadiusMetres, into.Single(b => b.Guid == 2).Radius, 4); // M6 item 14: its Setup's radius times its scale is 1.36 m, wider than a person, and this lookup reads no collision shape: a person's width, not the model's bounds
        Assert.Equal(NavCreatures.UnknownCreatureRadiusMetres, into.Single(b => b.Guid == 1).Radius); // no Setup id: 1 m, logged once
        Assert.Equal(1, log.Warnings);
        Assert.True(into.Single(b => b.Guid == 1).Outdoor);
        Assert.False(into.Single(b => b.Guid == 8).Outdoor);
        Assert.Equal(NavCreatures.PlayerRadiusMetres + HumanRadius, NpcBodies.DiscRadius(HumanRadius));
    }

    /// <summary>
    /// M6 4 f3 (live round 1: a "Bottle" in Holtburg logged as an NPC of 1 m): only a creature whose shape the physics has is a body. An item (a bottle, a sign: ItemType Misc 0x80, Writable
    /// 0x2000) never is; a creature-typed prop whose Setup the lookup cannot read (ACE weenie 40526, Setup 0x02001A57) is not either, logged once by name (npc-setup-unread); the NPC with a
    /// readable Setup is.
    /// </summary>
    [Fact]
    public void Only_a_creature_the_physics_has_a_shape_for_is_a_body_never_an_item_or_an_unread_prop()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 0x80, 0x0, 10, setup: 0x02000001)); // a bottle as an item (Misc)
        table.Set(Make(2, 0x2000, 0x0, 12, setup: 0x02000001)); // a sign (Writable)
        table.Set(Make(3, 0x10, 0x0, 14, setup: 0x02001A57)); // the creature-typed bottle prop: its Setup is not read
        table.Set(Make(4, 0x10, 0x0, 16, setup: 0x02000001)); // an NPC
        var log = new CountingLogSink(new RecordingLogSink());
        var feed = new NpcBodyFeed(table, self: 99, log, id => id == 0x02000001 ? HumanRadius : null);
        var into = new List<NavBody>();

        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);

        Assert.Equal([4u], into.Select(b => b.Guid));
        Assert.Equal(HumanRadius, into[0].Radius, 4);
        Assert.Equal(1, log.Warnings); // npc-setup-unread, once
        feed.Fill(P(0, 0, 0x01050000).Global, 100f, into);
        Assert.Equal(1, log.Warnings);
        Assert.False(NpcBodyFeed.IsBody(table.All.Single(o => o.Guid == 1), 99));
        Assert.False(NpcBodyFeed.IsBody(table.All.Single(o => o.Guid == 2), 99));
    }

    /// <summary>
    /// M6 item 14 (<c>m6-forge-in-smithy-lane</c>): a body's radius on the plan. A person (bounds 0.68 m) is sized as before; a body wider than a person is as wide as what its collision shape blocks at walking height, never
    /// wider than its bounds, and a person's width when no shape is read. Holtburg's Salvaging Forge: bounds 2.90 m (a disc of 3.38 m that closed the smithy's lane), collision shape 2.05 m (a disc of 2.53 m; the lane passes
    /// 3.3 m from its centre).
    /// </summary>
    [Theory]
    [InlineData(0.68f, 0.48f, 0.68f)] // a person: its bounds, as before (the human Setup's cylinder is 0.48 m)
    [InlineData(0.68f, null, 0.68f)] // a person with no shape read: as before
    [InlineData(0.50f, null, 0.50f)] // smaller than a person: its bounds
    [InlineData(0.75f, 0.53f, 0.68f)] // a tall person (scale 1.1): a person's width
    [InlineData(2.90f, 2.05f, 2.05f)] // the forge: its collision shape, not its model's bounds
    [InlineData(2.90f, null, 0.68f)] // a wide model with no shape read: a person's width, the physics meets the rest
    [InlineData(2.90f, 0.30f, 0.68f)] // a wide model on a thin post: never thinner than a person
    [InlineData(1.00f, 3.00f, 1.00f)] // never wider than its bounds
    public void A_body_wider_than_a_person_is_as_wide_as_its_collision_shape_and_a_person_is_sized_as_before(float bounds, float? shape, float expected)
    {
        Assert.Equal(expected, NpcBodyFeed.BodyRadius(bounds, shape), 4);
        Assert.Equal(0.68f, NpcBodyFeed.PersonRadiusMetres);
    }

    [Fact]
    public void The_collision_shape_is_the_farthest_reach_of_the_cylinders_and_spheres_that_stand_in_the_walking_height()
    {
        static Lifestone.Data.Models.ModelCylSphere Cyl(float x, float y, float z, float radius, float height) => new(new Vector3(x, y, z), radius, height);
        static Lifestone.Data.Models.ModelSphere Ball(float x, float y, float z, float radius) => new(new Vector3(x, y, z), radius);

        Assert.Null(SetupShapes.ShapeRadiusOf([], [])); // no cylinder and no sphere: the parts' own geometry is the collision, not measured here
        Assert.Equal(0.48f, SetupShapes.ShapeRadiusOf([Cyl(0, 0, 0, 0.48f, 1.84f)], [])!.Value, 4); // a person's cylinder
        Assert.Equal(1.5f, SetupShapes.ShapeRadiusOf([Cyl(1, 0, 0, 0.5f, 1f)], [])!.Value, 4); // off the axis: its offset and its radius
        Assert.Equal(1.5f, SetupShapes.ShapeRadiusOf([Cyl(0, 0, 0, 0.3f, 1f), Cyl(0, 1, 0, 0.5f, 1f)], [Ball(0, 0, 1, 0.7f)])!.Value, 4); // the farthest of them
        Assert.Equal(0.7f, SetupShapes.ShapeRadiusOf([], [Ball(0, 0, 1, 0.7f)])!.Value, 4);
        Assert.Null(SetupShapes.ShapeRadiusOf([Cyl(0, 0, 2.5f, 3f, 1f)], [Ball(0, 0, 4, 1f)])); // wholly over a walker's head (2 m): blocks no one on the ground
        Assert.Null(SetupShapes.ShapeRadiusOf([Cyl(0, 0, 0, 0f, 1f)], [])); // a cylinder of no radius is no shape
        Assert.Equal(2f, SetupShapes.WalkingHeightMetres);
        Assert.Null(SetupShapes.Of(id => 1f)); // a plain lookup carries no shapes: the feed then caps a wide body at a person's width
        Assert.Null(SetupShapes.Of(null));
    }

    [Fact]
    public void The_scars_carry_the_bodies_apart_from_the_timed_scars_and_replace_them_whole()
    {
        var scars = new NavScars();
        scars.Add(P(10, 10), 1.5f);
        scars.SetBodies([(P(50, 50).Global, 1.16f), (P(60, 50).Global, 1.16f)]);

        Assert.True(scars.IsScarred(P(50.5f, 50)));
        Assert.True(scars.IsScarred(P(10, 10)));
        Assert.Single(scars.All); // the timed scars alone
        Assert.Equal(1, scars.LiveCount);
        Assert.Equal(2, scars.Bodies.Count);

        scars.SetBodies([(P(60, 50).Global, 1.16f)]);
        Assert.False(scars.IsScarred(P(50.5f, 50)));
        Assert.True(scars.IsScarred(P(60, 50)));
        scars.ClearBodies();
        Assert.False(scars.IsScarred(P(60, 50)));
        Assert.True(scars.IsScarred(P(10, 10)));
    }

    [Fact]
    public void A_path_meets_a_body_through_its_disc_not_at_its_edge_and_not_one_the_start_stands_beside()
    {
        var bodies = new NpcBodies();
        var feed = new BodyRig(0, 0) { Bodies = { Body(Scrivener, 50, 50, HumanRadius) } };
        bodies.Refresh(feed, Q(0, 50).Global, 200f, ignore: 0, goal: Q(100, 50).Global, map: null);
        var disc = NpcBodies.DiscRadius(HumanRadius);

        Assert.Equal(Scrivener, bodies.FirstMeeting(Path(Q(0, 50), Q(100, 50)), Q(0, 50).Global)?.Guid); // straight through her
        Assert.Null(bodies.FirstMeeting(Path(Q(0, 50 + disc - 0.1f), Q(100, 50 + disc - 0.1f)), Q(0, 50).Global)); // grazing the disc's edge within the slack
        Assert.Null(bodies.FirstMeeting(Path(Q(0, 52), Q(100, 52)), Q(0, 52).Global));
        Assert.Null(bodies.FirstMeeting(Path(Q(50.5f, 50), Q(100, 50)), Q(50.5f, 50).Global)); // the start beside her: the plan lets the route leave her disc
        Assert.Equal(1, bodies.SelectNear(Q(0, 50 + NpcBodies.RouteMetres - 1f).Global, Q(100, 50 + NpcBodies.RouteMetres - 1f).Global));
        Assert.Equal(0, new NpcBodies().SelectNear(Q(0, 90).Global, Q(100, 90).Global));
    }

    [Fact]
    public void The_plan_goes_round_an_npc_it_would_have_walked_through_and_the_walk_arrives()
    {
        var rig = new BodyRig(60, 96.5f);
        var npc = Body(Scrivener, 95, 96.5f, HumanRadius);
        rig.Bodies.Add(npc);
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.Frame();

        Assert.Single(rig.Runner.Npcs.Stamped);
        Assert.Single(rig.Runner.Scars.Bodies);
        var route = rig.Runner.Path!.ToRoute();
        var disc = NpcBodies.DiscRadius(HumanRadius);
        Assert.True(MinDistance(route, npc.Centre) >= disc - NpcBodies.MeetSlackMetres, $"the route keeps out of her body: {MinDistance(route, npc.Centre):F2} m, the disc {disc:F2} m");

        var nearest = float.MaxValue;
        while (!rig.Runner.IsDone && rig.Body.Now < 60)
        {
            rig.Frame();
            nearest = Math.Min(nearest, Vector2.Distance(NavMap.FromCellFrame(rig.Body.Position).Global, npc.Centre));
        }

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(0, rig.Runner.NpcLaneWaits);
        Assert.True(nearest >= 2 * NavCreatures.PlayerRadiusMetres, $"the body came {nearest:F2} m from her centre (two body radii: touching)");
    }

    [Fact]
    public void A_walk_to_the_npc_leaves_her_own_body_out_and_so_does_a_goal_inside_her_disc()
    {
        var seek = new BodyRig(60, 96.5f) { Bodies = { Body(Scrivener, 95, 96.5f, HumanRadius) } };
        seek.Runner.Start(NavGoal.ToObject(Scrivener, NavPoint.At(seek.Block, 95, 96.5f), 3f, needSight: false), seek.Body.Now);
        seek.Frame();
        Assert.Empty(seek.Runner.Npcs.Stamped);
        Assert.Empty(seek.Runner.Npcs.Seen);
        Assert.Equal(2, seek.Runner.Path!.Waypoints.Count); // straight at her

        var stand = new BodyRig(60, 96.5f) { Bodies = { Body(Scrivener, 95, 96.5f, HumanRadius) } };
        stand.Runner.Start(NavGoal.ToPoint(NavPoint.At(stand.Block, 94.5f, 96.5f)), stand.Body.Now);
        stand.Frame();
        Assert.Equal(1, stand.Runner.Npcs.AtGoal);
        Assert.Empty(stand.Runner.Npcs.Stamped);
        Assert.NotNull(stand.Runner.Path); // planned, not goal-scarred
    }

    [Fact]
    public void A_body_on_another_level_than_the_ground_under_it_is_not_on_the_plan()
    {
        var rig = new BodyRig(60, 96.5f) { Bodies = { Body(Scrivener, 95, 96.5f, HumanRadius, z: 3f) } }; // a balcony 3 m over the street (Holtburg's Item and War apprentice stands so)
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.Frame();

        Assert.Equal(1, rig.Runner.Npcs.OffLevel);
        Assert.Empty(rig.Runner.Npcs.Stamped);
        Assert.Equal(2, rig.Runner.Path!.Waypoints.Count); // straight under her
    }

    [Fact]
    public void An_npc_in_the_only_gap_is_waited_for_planned_once_more_then_named_and_never_pushed()
    {
        var rig = new BodyRig(60, 96.5f, Wall()) { Bodies = { Body(Scrivener, 91, 96.5f, 1f) } }; // the gap is 3 m; her disc is 2.96 m across
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        var start = NavMap.FromCellFrame(rig.Body.Position).Global;

        rig.RunFor(1.0);
        Assert.Equal(NavRunState.Running, rig.Runner.State); // waiting, keys released
        Assert.Equal(1, rig.Runner.NpcLaneWaits);
        Assert.Equal(0, rig.Body.TotalDepth);
        rig.RunFor(10.0);

        Assert.Equal(NavRunState.Blocked, rig.Runner.State);
        Assert.Contains("no path: npc-in-lane:0x7A9B301B: ", rig.Runner.FailureReason, StringComparison.Ordinal); // the runner's failure shape: run '<name>' to <goal> plan <n> rung <n>: <why>
        Assert.Equal(2, rig.Runner.Plans);
        Assert.Equal(1, rig.Runner.NpcLaneWaits);
        Assert.True(Vector2.Distance(start, NavMap.FromCellFrame(rig.Body.Position).Global) < 0.1f, "the body never moved toward her: no push");
    }

    [Fact]
    public void An_npc_who_steps_out_of_the_gap_during_the_wait_lets_the_walk_on()
    {
        var rig = new BodyRig(60, 96.5f, Wall()) { Bodies = { Body(Scrivener, 91, 96.5f, 1f) } };
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.RunFor(1.0);
        Assert.Equal(1, rig.Runner.NpcLaneWaits);

        rig.Bodies.Clear(); // she walks off
        rig.RunFor(60.0);

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(1, rig.Runner.NpcLaneWaits);
    }

    [Fact]
    public void An_npc_who_comes_into_sight_on_the_route_ahead_is_planned_round_before_the_body_meets_him()
    {
        // M6 sprint (run 5, cache/m6/sprint/run5/step3-levelup.log 14:24:58 to 14:26:09 UTC): the walk to the Apprentice Scrivener of Life Magic was planned 265 m from the Scrivener of Item Magic, out of sight, so
        // not round him; the walk ran into him outside the door and replanned only after four rungs. Here the feed shows a body only within 25 m of the walker, as the object table does at its reach.
        var rig = new BodyRig(40, 96.5f) { SightMetres = 25f };
        var npc = Body(Scrivener, 95, 96.5f, HumanRadius);
        rig.Bodies.Add(npc);
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.Frame();
        Assert.Empty(rig.Runner.Npcs.Stamped); // 55 m off at the plan: the plan goes straight through his spot
        Assert.Equal(2, rig.Runner.Path!.Waypoints.Count);

        var nearest = float.MaxValue;
        while (!rig.Runner.IsDone && rig.Body.Now < 60)
        {
            rig.Frame();
            nearest = Math.Min(nearest, Vector2.Distance(NavMap.FromCellFrame(rig.Body.Position).Global, npc.Centre));
        }

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(1, rig.Runner.NpcReplans); // m6-npc-ahead: once, when he came into sight on the route ahead
        Assert.Equal(0, rig.Runner.NpcLaneWaits);
        Assert.True(nearest >= 2 * NavCreatures.PlayerRadiusMetres, $"the body came {nearest:F2} m from his centre (two body radii: touching)");
    }

    [Fact]
    public void An_npc_who_comes_into_sight_off_the_route_is_no_replan()
    {
        var rig = new BodyRig(40, 96.5f) { SightMetres = 25f, Bodies = { Body(Scrivener, 95, 110f, HumanRadius) } }; // 13.5 m beside the way
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.RunFor(60.0);

        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(0, rig.Runner.NpcReplans);
    }

    [Fact]
    public void A_runner_without_a_body_feed_plans_as_before_through_the_npc()
    {
        var rig = new BodyRig(60, 96.5f, feed: false) { Bodies = { Body(Scrivener, 95, 96.5f, HumanRadius) } };
        rig.Runner.Start(NavGoal.ToPoint(NavPoint.At(rig.Block, 130, 96.5f)), rig.Body.Now);
        rig.Frame();

        Assert.Empty(rig.Runner.Scars.Bodies);
        Assert.Equal(2, rig.Runner.Path!.Waypoints.Count);
    }

    /// <summary>A wall across the tile at x 90 to 92 with one gap at y 95 to 97 (3 m).</summary>
    private static NavTile Wall() => Flat(MovementTestKit.Block.LandBlockId).Box(90, 0, 92, 94).Box(90, 98, 92, 191);

    private static NavBody Body(uint guid, float x, float y, float radius, float z = 0f) =>
        new(guid, "Scrivener of Item Magic", NavPoint.At(MovementTestKit.Block.LandBlockId, x, y).Global, radius, z, Outdoor: true);

    private static NavPoint Q(float x, float y) => NavPoint.At(MovementTestKit.Block.LandBlockId, x, y);

    private static List<NavWaypoint> Path(params NavPoint[] points) => [.. points.Select(p => new NavWaypoint(NavMap.ToCellFrame(p), Doorway: false))];

    private static float MinDistance(NavRoute route, Vector2 point) => Samples(route, 0.1f).Min(p => Vector2.Distance(p.Global, point));

    private static WorldObject Make(uint guid, uint itemType, uint flags, float x, uint? setup = null, float? scale = null, uint cell = 0x0105003D)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "npc",
            WeenieClassId = 20214,
            IconId = 0x06000001,
            ItemType = itemType,
            ObjectDescriptionFlags = flags,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1), SetupId = setup, Scale = scale };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0)
        {
            Position = new CellFrame(cell, new AcFrame(new Vector3(x, 0f, 0f), Quaternion.Identity)),
        };
    }

    /// <summary>The scripted body over a flat tile (or <paramref name="tile"/>) with the real planner and a body feed the test fills.</summary>
    private sealed class BodyRig : INavBodyFeed
    {
        public BodyRig(float x, float y, NavTile? tile = null, bool feed = true)
        {
            H = new ActionHarness(offline: true);
            Body = new S5KinematicBody(x, y, heading: 90f);
            H.Api.Move.Bind(Body, false);
            Runner = new NavRunner(H.Api.Move, Body, new OutdoorPathSource(new PathPlanner(Map(tile ?? Flat(Block))))) { BodyFeed = feed ? this : null };
        }

        public uint Block { get; } = MovementTestKit.Block.LandBlockId;

        public ActionHarness H { get; }

        public S5KinematicBody Body { get; }

        public NavRunner Runner { get; }

        public List<NavBody> Bodies { get; } = [];

        /// <summary>M6 sprint: the feed shows a body only within this of the walker (the object table's reach); unlimited by default.</summary>
        public float SightMetres { get; init; } = float.MaxValue;

        public void Fill(Vector2 from, float maxMetres, List<NavBody> into)
        {
            into.Clear();
            var me = NavMap.FromCellFrame(Body.Position).Global;
            foreach (var b in Bodies)
            {
                if (Vector2.Distance(b.Centre, from) <= maxMetres && Vector2.Distance(b.Centre, me) <= SightMetres)
                {
                    into.Add(b);
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
