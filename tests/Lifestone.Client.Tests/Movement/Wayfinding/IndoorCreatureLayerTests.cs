using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Tests.Session.Grounds;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// The creature layer indoors (nav-creatures-indoor, docs/wayfinding.md "The creature layer"): the room graph's walks costed by the hostile creatures'
/// discs with the outdoor weights, so a route goes round a creature through other doorways, a creature in the only doorway costs the route but never
/// fails it, the run's goal creature is left out, the runner refreshes its layer and replans once for a creature walking onto the route ahead over a room
/// source, and a warm indoor runner with a layer allocates nothing a step. Without a layer every plan is today's.
/// </summary>
public sealed class IndoorCreatureLayerTests
{
    private const uint Drudge = 0x80001234;

    /// <summary>A disc of 8 m: small enough that the ring's other way keeps out of it.</summary>
    private const float Disc = 8f;

    private static readonly Vector3 RingStart = new(93f, 99f, 0f);
    private static readonly Vector3 RingGoal = new(103f, 109f, 0f);

    [Fact]
    public void Without_a_layer_the_room_graphs_plans_are_todays()
    {
        var cells = IndoorRoomKit.Ring();
        var bare = new RoomGraph(cells).Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!;

        var empty = new RoomGraph(cells) { Creatures = new NavCreatures() };
        var none = new RoomGraph(cells) { Creatures = null };
        foreach (var route in new[] { empty.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!, none.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal, ignoreCreature: Drudge)! })
        {
            Assert.Equal(bare.Cells, route.Cells);
            Assert.Equal(bare.Waypoints, route.Waypoints);
        }

        // Today's plan, pinned: the shorter way, through the north-west room.
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast], bare.Cells);
        Assert.Equal(4, bare.Waypoints.Count);

        // A run without a feed leaves a shared graph's layer off.
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f);
        rig.Graph.Creatures = new NavCreatures();
        rig.Graph.Creatures.Set([new NavCreature(Drudge, Global(96f, 106f), Disc)]);
        rig.Runner.Start(Goal(RingGoal, IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.Frame();
        Assert.Null(rig.Graph.Creatures);
        Assert.Equal(bare.Cells, rig.Runner.Path!.CellsEntered);
    }

    [Fact]
    public void A_route_goes_round_a_creature_it_would_have_crossed()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f, withFeed: true);
        rig.Creatures.Add(new NavCreature(Drudge, Global(96f, 106f), Disc)); // in the north-west room, on today's route

        var report = rig.Run(IndoorRoomKit.RingNorthEast, RingGoal, 30);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason + " " + report);
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast], rig.Runner.Path!.CellsEntered);
        Assert.Equal(0, rig.FramesOutside);
        Assert.DoesNotContain(rig.Track, p => InNorthWest(p));
        Assert.Equal(0, rig.Runner.CreatureReplans);
        Assert.Equal(1, rig.Runner.CreaturesMax);
    }

    [Fact]
    public void A_creature_in_the_only_doorway_costs_the_route_but_never_fails_it()
    {
        var cells = S3RoomKit.Rooms(withOutside: false);
        var bare = new RoomGraph(cells).Plan(S3RoomKit.A, new Vector3(96f, 93f, 0f), S3RoomKit.C, new Vector3(96f, 119f, 0f))!;
        var graph = new RoomGraph(cells) { Creatures = new NavCreatures() };
        graph.Creatures.Set([new NavCreature(Drudge, Global(96f, 101f), NavCreatures.AggroRadius(10f))]); // in the doorway from A to B

        var route = graph.Plan(S3RoomKit.A, new Vector3(96f, 93f, 0f), S3RoomKit.C, new Vector3(96f, 119f, 0f));

        Assert.NotNull(route);
        Assert.Equal(bare.Cells, route.Cells);
        Assert.Equal(bare.Waypoints, route.Waypoints);
        Assert.True(graph.Creatures.ExposureAlong(Global(96f, 93f), Global(96f, 119f)) > 0f, "the route is costed");
    }

    [Fact]
    public void The_runs_goal_creature_is_left_out()
    {
        var cells = IndoorRoomKit.Ring();
        var source = new RoomPathSource(new RoomGraph(cells));
        source.Rooms.Creatures = new NavCreatures();
        source.Rooms.Creatures.Set([new NavCreature(Drudge, Global(96f, 106f), Disc)]);
        var from = new CellFrame(IndoorRoomKit.RingSouthWest, new AcFrame(RingStart, Quaternion.Identity));
        var point = new NavPoint(IndoorRoomKit.Block.LandBlockId, RingGoal);

        var round = source.Plan(from, NavGoal.ToPoint(point, IndoorRoomKit.RingNorthEast), new NavScars());
        var seek = source.Plan(from, NavGoal.ToObject(Drudge, point, 2f, needSight: false, IndoorRoomKit.RingNorthEast), new NavScars());

        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast], round.CellsEntered);
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast], seek.CellsEntered); // the goal's own creature costs nothing

        // The runner leaves it off its layer too.
        var rig = new IndoorRig(cells, 0f, IndoorRoomKit.RingSouthWest, new Vector2(RingStart.X, RingStart.Y), heading: 0f, withFeed: true);
        rig.Creatures.Add(new NavCreature(Drudge, Global(96f, 106f), Disc));
        rig.Runner.Start(NavGoal.ToObject(Drudge, new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(96f, 106f, 0f)), 2f, needSight: false, IndoorRoomKit.RingNorthWest), rig.Body.Now);
        rig.RunFor(20);
        Assert.Equal(NavRunState.Arrived, rig.Runner.State);
        Assert.Equal(0, rig.Runner.CreaturesMax);
        Assert.Null(rig.Runner.CreatureMinMetres);
    }

    [Fact]
    public void The_runner_replans_once_when_a_creature_walks_onto_the_route_ahead_over_the_rooms()
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(93f, 92f), heading: 0f, withFeed: true);
        rig.Runner.Start(Goal(RingGoal, IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.RunFor(0.3); // today's route, through the north-west room
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast], rig.Runner.Path!.CellsEntered);
        Assert.Equal(0, rig.Runner.CreatureReplans);

        rig.Creatures.Add(new NavCreature(Drudge, Global(96f, 106f), Disc)); // walks into the north-west room
        rig.RunFor(30);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Equal(1, rig.Runner.CreatureReplans);
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast], rig.Runner.Path!.CellsEntered);
        Assert.DoesNotContain(rig.Track, p => InNorthWest(p));
        Assert.Contains("creatures_max=1 creature_replans=1 creature_min_m=", rig.Runner.VerdictFields(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ M5 2 fix F2 (review 2-R6): the storey band

    [Theory]
    [InlineData(0f, true)] // on the route's storey: costs it, as before
    [InlineData(2f, true)] // a step up or down within the storey
    [InlineData(NavCreatures.StoreyBandMetres, true)] // the band's edge
    [InlineData(float.NaN, true)] // a creature whose height is not known counts at every height
    [InlineData(6f, false)] // directly above, a storey up
    [InlineData(-6f, false)] // directly below
    [InlineData(12f, false)] // two storeys up
    public void The_room_graph_costs_a_creature_only_on_the_routes_storey(float z, bool goesRound)
    {
        var graph = new RoomGraph(IndoorRoomKit.Ring()) { Creatures = new NavCreatures() };
        graph.Creatures.Set([new NavCreature(Drudge, Global(96f, 106f), Disc, z)]); // in the north-west room, on today's route, at height z

        var route = graph.Plan(IndoorRoomKit.RingSouthWest, RingStart, IndoorRoomKit.RingNorthEast, RingGoal)!;

        uint[] expected = goesRound
            ? [IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast]
            : [IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast];
        Assert.Equal(expected, route.Cells);
    }

    [Fact]
    public void The_exposure_of_a_walk_counts_the_creatures_within_the_band_at_each_samples_height()
    {
        var layer = new NavCreatures();
        var a = Global(90f, 100f);
        var b = Global(110f, 100f);
        var middle = Global(100f, 100f);

        layer.Set([new NavCreature(Drudge, middle, Disc, 6f)]);
        Assert.Equal(0f, layer.ExposureAlong(a, 0f, b, 0f, 0)); // directly above
        layer.Set([new NavCreature(Drudge, middle, Disc, -6f)]);
        Assert.Equal(0f, layer.ExposureAlong(a, 0f, b, 0f, 0)); // directly below
        Assert.Equal(0f, layer.CostAt(middle, 0f, 0));
        layer.Set([new NavCreature(Drudge, middle, Disc, 0f)]);
        Assert.True(layer.ExposureAlong(a, 0f, b, 0f, 0) > 0f, "on the route's storey it costs");
        Assert.Equal(layer.ExposureAlong(a, b), layer.ExposureAlong(a, 0f, b, 0f, 0)); // the flat walk is the banded walk on its own storey
        Assert.Equal(0f, layer.ExposureAlong(a, 0f, b, 0f, Drudge)); // the goal's own creature is still left out

        // The flat overloads (outdoors, and the shortcut tests of the lattice) read no height: a creature a storey up still costs them.
        layer.Set([new NavCreature(Drudge, middle, Disc, 6f)]);
        Assert.True(layer.ExposureAlong(a, b) > 0f);
        Assert.True(layer.CostAt(middle) > 0f);

        // A ramp from 0 down to -6 across the walk: a creature on the lower storey at the far end counts where the ramp has come down to it,
        // and not for a walk that stays up on 0.
        layer.Set([new NavCreature(Drudge, Global(108f, 100f), 4f, -6f)]);
        Assert.True(layer.ExposureAlong(a, 0f, b, -6f, 0) > 0f);
        Assert.Equal(0f, layer.ExposureAlong(a, 0f, b, 0f, 0));
    }

    [Theory]
    [InlineData(0f, 1)] // on the route's storey: the one replan, as before
    [InlineData(2f, 1)]
    [InlineData(6f, 0)] // walking about the storey above: no replan, the route stays
    [InlineData(-6f, 0)] // and below
    public void The_runner_replans_for_a_creature_on_the_routes_storey_not_for_one_above_or_below(float z, int replans)
    {
        var rig = new IndoorRig(IndoorRoomKit.Ring(), 0f, IndoorRoomKit.RingSouthWest, new Vector2(93f, 92f), heading: 0f, withFeed: true);
        rig.Runner.Start(Goal(RingGoal, IndoorRoomKit.RingNorthEast), rig.Body.Now);
        rig.RunFor(0.3); // today's route, through the north-west room
        Assert.Equal([IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast], rig.Runner.Path!.CellsEntered);

        rig.Creatures.Add(new NavCreature(Drudge, Global(96f, 106f), Disc, z)); // in the north-west room at height z
        rig.RunFor(30);

        Assert.True(rig.Runner.State == NavRunState.Arrived, rig.Runner.FailureReason);
        Assert.Equal(replans, rig.Runner.CreatureReplans);
        Assert.Equal(1, rig.Runner.CreaturesMax); // seen either way: only the cost and the replan read the height
        uint[] expected = replans == 1
            ? [IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingSouthEast, IndoorRoomKit.RingNorthEast]
            : [IndoorRoomKit.RingSouthWest, IndoorRoomKit.RingNorthWest, IndoorRoomKit.RingNorthEast];
        Assert.Equal(expected, rig.Runner.Path!.CellsEntered);
        Assert.Equal(replans == 1, rig.Track.All(p => !InNorthWest(p)));
    }

    [Fact]
    [Trait("Tier", "Timing")] // m6-alloc-flake-indoor-layer: a zero-byte allocation count a step (failed once under the parallel suite, passes alone: tiered JIT and the other tests on the thread pool)
    public void A_warm_indoor_runner_with_a_creature_layer_allocates_nothing_a_step()
    {
        var rig = new IndoorRig(IndoorRoomKit.LongHall(), 0f, IndoorRoomKit.Hall, new Vector2(96f, 58f), heading: 0f, withFeed: true);
        rig.Creatures.Add(new NavCreature(Drudge, Global(130f, 76f), Disc)); // off the route: refreshed, never replanned for
        rig.Runner.Start(Goal(new Vector3(96f, 99f, 0f), IndoorRoomKit.Hall + 1), rig.Body.Now);
        rig.RunFor(0.5); // the plan, the pools grown
        var keys = rig.Runner.KeyChanges;

        long allocated = 0;
        for (var i = 0; i < 30; i++) // a second: two refreshes of the layer
        {
            rig.Body.Advance(S5Rig.Dt);
            rig.Indoor.Sync();
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.Runner.Step(rig.Body.Now);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            rig.H.Tick(S5Rig.Dt);
        }

        Assert.Equal(NavRunState.Running, rig.Runner.State);
        Assert.Equal(1, rig.Runner.CreaturesMax);
        Assert.Equal(keys, rig.Runner.KeyChanges);
        Assert.Equal(0, allocated);
    }

    private static Vector2 Global(float x, float y) => new NavPoint(IndoorRoomKit.Block.LandBlockId, new Vector3(x, y, 0f)).Global;

    private static NavGoal Goal(Vector3 at, uint cell) => NavGoal.ToPoint(new NavPoint(IndoorRoomKit.Block.LandBlockId, at), cell);

    /// <summary>Whether a global point lies in the ring's north-west room (local x 91 to 101, y 101 to 111).</summary>
    private static bool InNorthWest(Vector2 global)
    {
        var local = global - NavPoint.BlockOrigin(IndoorRoomKit.Block.LandBlockId);
        return local.X > 91f && local.X < 101f && local.Y > 101f && local.Y < 111f;
    }
}

/// <summary>
/// The creature layer on the Black Spawn Den's dats (Exhaustive tier): the walker runs the tour's leg from 0x0320 to 0x0303 straight between its corners,
/// and with a tusker it does not seek standing in 0x0308 (a disc of ACE's default awareness, 18 m, and a 1.5 m body) goes round through 0x031B to 0x0300
/// instead, arriving without a rung and never entering the tusker's room.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class IndoorCreatureLayerDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>, INavCreatureFeed
{
    private const uint Tusker = 0x80004321;
    private const uint From = 0x01050320u;
    private const uint To = 0x01050303u;
    private const uint TuskerRoom = 0x01050308u;
    private readonly List<NavCreature> _creatures = [];

    [RequiresDatsFact]
    public void A_den_leg_runs_straight_and_goes_round_a_tusker_it_does_not_seek()
    {
        var dats = fixture.Dats;
        var coord = new LandblockCoord(1, 5);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), DenGround.Profile);
        var tusker = graph.RoomFrame(TuskerRoom)!.Value.Origin;
        _creatures.Add(new NavCreature(Tusker, new NavPoint(0x01050000u, tusker).Global, NavCreatures.AggroRadius(CreatureAwareness.DefaultMetres, 1.5f)));

        var h = new ActionHarness(offline: true);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        h.Api.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(graph.RoomFrame(From)!.Value));
        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(h.Api.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), confirmer, options: new NavRunnerOptions { Name = "den-tusker" }, creatures: this);
        runner.Start(NavGoal.ToPoint(new NavPoint(0x01050000u, graph.RoomFrame(To)!.Value.Origin), To), 0.0);
        var visited = new List<uint> { walker.Position.CellId };
        var now = 0.0;
        while (!runner.IsDone && now < 60)
        {
            now += 1.0 / 60;
            walker.Controller.Tick(now);
            runner.Step(now);
            h.Tick(1.0 / 60);
            if (walker.Position.CellId != visited[^1])
            {
                visited.Add(walker.Position.CellId);
            }
        }

        output.WriteLine($"{runner.State} {runner.VerdictFields()} cells {string.Join(" ", visited.Select(c => (c & 0xFFFF).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)))}");
        Assert.True(runner.State == NavRunState.Arrived, runner.FailureReason);
        Assert.Equal(0, runner.Rungs);
        Assert.Equal(To, walker.Position.CellId);
        Assert.DoesNotContain(TuskerRoom, visited);
        Assert.DoesNotContain(TuskerRoom, runner.Path!.CellsEntered);
        Assert.Equal(1, runner.CreaturesMax);
        Assert.True(runner.Turns <= runner.RouteMetres * IndoorSteeringTests.MaxTurnsPerMetre, runner.VerdictFields());
    }

    [RequiresDatsFact]
    public void The_setup_radii_are_the_dats_setups_radius()
    {
        var radii = HostileCreatureFeed.SetupRadii(fixture.Dats);

        Assert.Equal(Data.Physics.Objects.PhysicsSetup.Load(fixture.Dats, OfflineWalker.HumanSetupId)!.Radius, radii(OfflineWalker.HumanSetupId));
        Assert.True(radii(OfflineWalker.HumanSetupId) > 0f);
        Assert.Null(radii(0x0200FFFFu));
    }

    void INavCreatureFeed.Fill(Vector2 from, float maxMetres, List<NavCreature> into)
    {
        into.Clear();
        into.AddRange(_creatures);
    }
}
