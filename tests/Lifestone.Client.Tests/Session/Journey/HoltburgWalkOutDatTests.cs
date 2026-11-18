using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Physics;
using Lifestone.Client.Tests.Session;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 7 G (gate A's rehearsal, 2026-11-05): the walks OUT of the places the climb's stations stand at, on the retail dats. <see cref="HoltburgScrivenerWalkDatTests"/> proves the way in; the rehearsal stopped after
/// 6 minutes because the way out of the Apprentice Scrivener of Creature Magic's hall had never been walked: both plans <c>indoor-start-unreachable:0xA9B30103</c> in 0.2 s, the room graph's crossing of the 9.6 m wide
/// portal between 0xA9B30103 and 0xA9B30100 stood on the pillar in its middle (0x0200020E at (182.08, 83.9)), and the detour to the back door 0xA9B30105 stood 0.26 m off a wall. The way in differs: its goal
/// is in the front room, so it never takes that crossing but walks straight to the stop past the pillar. Here: the rehearsal's own start, the two causes one by one, and the class: every place a station on the climb stands at in Holtburg walks
/// out to the lifestone point and to the outskirts' first cell, and walks back to the town's Network portal in the towns the learn trips reach (<see cref="TownNetworkTripDatTests"/> proves only their way in).
/// </summary>
/// <remarks>
/// The rows are the catalogs': <c>Grounds/scriveners.json</c> and the outskirts' and the den's vendors (their landblock_instance rows, the pinned world SQL v0.9.296); the portals are <c>Travel/travel-routes.json</c>'s. The walker ends a stop where the
/// runner stops it, and ACE's walk-to-use MoveTo may then carry the body further (the rehearsal stood at (176.96, 83.66), 1.1 m from the test walk's end, on the foot of the inner stair), so <c>From_every_standable_point_round_a_station_the_walk_out_arrives</c>
/// runs the pre-check from a ring of points round each end.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class HoltburgWalkOutDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Den = "black-spawn-den-aluvian";
    private const double Fps = 60.0;
    private const double WalkSeconds = 240.0;
    private const uint HallBack = 0xA9B30103;
    private const uint HallFront = 0xA9B30100;
    private const uint BackDoor = 0xA9B30105;
    private static readonly Vector3 Pillar = new(182.075f, 83.9f, 116f); // static 0x0200020E of 0xA9B30100, its feet at the floor

    /// <summary>Where the rehearsal stood when both walks out failed (the live log's "player in cell"): the foot of the inner stair, 0.27 m up its ramp.</summary>
    private static readonly CellFrame RehearsalStart = new(HallBack, new AcFrame(new Vector3(176.96f, 83.66f, 116.27f), Quaternion.Identity));

    /// <summary>
    /// The learn trips' towns on the Town Network with a Master or Grand Master of each school (<see cref="TownNetworkTripDatTests"/>): the edge the trip arrives by and the edge it leaves by. Fort Tethana's Masters are behind its lever gate
    /// (<c>TownNetworkTrip.Choose</c> refuses them, <see cref="TownNetworkTripDatTests"/>), so no learn trip stands there.
    /// </summary>
    private static readonly (string Town, string Drop, string Out)[] NetworkTowns =
    [
        ("Dryreach", "network-to-dryreach", "dryreach-to-network"),
        ("Stonehold", "network-to-stonehold", "stonehold-to-network"),
    ];

    /// <summary>
    /// The ring's starts that never arrive, by station, radius and direction, with their causes; none is a place a walk ends at, and a fixed one fails here until its row goes.
    /// Closed in M6 7 Q: TODO(m6-behind-counter-start): the two points behind the Scrivener of War Magic's counter in 0xA9B30102 are not shut in; since a crossing's approach stands, the crossing into 0xA9B30100 moves along its opening and the plan by (184.25, 85.99) confirms.
    /// Closed in the world walk's W8 (steps that cross objects): m6-stair-top-start, a point over the inner stair's top, by the hole in 0xA9B3010A, whose plan's first leg ran down the stair from a body already past its landing.
    /// </summary>
    private static readonly Dictionary<string, string> RingGaps = new()
    {
    };

    private DatSet Dats => fixture.Dats;

    private ITestOutputHelper Out => output;

    [RequiresDatsFact]
    public void The_rehearsals_start_plans_and_walks_out_to_the_lifestone_point_and_the_outskirts_first_cell()
    {
        var rig = HoltburgRig();
        foreach (var (name, cell, point) in HoltburgGoals())
        {
            var (path, goal) = rig.Plan(RehearsalStart, cell, point, 0);
            var status = rig.Confirmer.ConfirmAll(path.Waypoints, goal.StopTolerance);
            output.WriteLine($"{name}: {path.Status} {path.Reason}: {status} {rig.Confirmer.Failure}");
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(RouteConfirmStatus.Confirmed, status);
            Assert.Equal([HallBack, HallFront, 0xA9B30104u], path.CellsEntered); // the front door, the short way, as the plan that failed

            using var body = new Body(this, rig, RehearsalStart);
            var walk = body.Walk(goal, "from the rehearsal's start to " + name);
            Assert.Equal(NavRunState.Arrived, walk.State);
            Assert.Equal(0, walk.ConfirmFailures);
        }
    }

    /// <summary>
    /// The first cause: the portal between the hall's two rooms is 9.6 m wide and the pillar stands on its centre: the foot has no floor there (the plan's refused sample) and the room is open either side. So the graph takes a crossing either side
    /// of the pillar, both ways through, and every sample of each stands; the same pillar in Ecutha's lobby 0xA9B40127 was this fault's first sighting (TODO(nav-static-footprint), closed).
    /// </summary>
    [RequiresDatsFact]
    public void The_hall_is_crossed_either_side_of_the_pillar_on_its_portals_centre_both_ways()
    {
        var rig = HoltburgRig();
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(0xA9, 0xB3));
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var foot = rig.Confirmer.World;
        foreach (var (from, to) in new[] { (HallBack, HallFront), (HallFront, HallBack) })
        {
            var cell = cells.GetCell(from)!;
            var portal = cell.Portals.Single(p => p.OtherCellId == to);
            var pairs = graph.Planner.DoorwayPairs(cell, portal);
            output.WriteLine($"0x{from:X8} to 0x{to:X8}: " + string.Join("; ", pairs.Select(p => $"{p.Approach?.Origin} {p.Push.Origin}")));
            Assert.Equal(2, pairs.Count);
            Assert.Single(pairs, p => p.Push.Origin.Y > Pillar.Y + 0.5f);
            Assert.Single(pairs, p => p.Push.Origin.Y < Pillar.Y - 0.5f);
            Assert.All(pairs, p =>
            {
                Assert.Equal(from, p.Approach!.Value.CellId);
                Assert.Equal(to, p.Push.CellId);
                Assert.Equal(p.Approach.Value.Origin.Y, p.Push.Origin.Y, 3); // square through the portal
            });
        }

        var centre = cells.GetCell(HallBack)!;
        var door = graph.Planner.DoorwayOf(centre, centre.Portals.Single(p => p.OtherCellId == HallFront))!.Value;
        var sample = new CellFrame(HallFront, new AcFrame(new Vector3(182.1f, door.Centre.Y, 116.01f), Quaternion.Identity));
        Assert.False(foot.TryFixedGroundZ(sample, rig.Foot, out _, out _)); // the refused sample itself
    }

    /// <summary>
    /// The second cause: with the front room left out the plan took the back door, round the table by a floor corner drawn 0.5 m toward the room's centre: 0.26 m off the wall, in a room half as wide as it is long, no floor for the foot. A corner is now
    /// drawn in from both its edges by the body's width and a margin. The way out by the back door alone is planned, confirmed and walked (the door's lane is slice A's; the plan here is the room's).
    /// </summary>
    [RequiresDatsFact]
    public void A_detour_stays_the_bodys_width_off_the_walls_and_the_way_out_by_the_back_door_is_confirmed()
    {
        var rig = HoltburgRig();
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(0xA9, 0xB3));
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var cell = cells.GetCell(HallBack)!;
        var door = graph.Planner.DoorwayPair(cell, cell.Portals.Single(p => p.OtherCellId == BackDoor));
        var start = RehearsalStart.Origin;
        var vias = graph.Planner.FloorDetour(cell, start, door.Approach!.Value.Origin);
        output.WriteLine("vias: " + string.Join(' ', vias));
        Assert.NotEmpty(vias); // the table stands on the straight line
        var floor = cells.GetStructure(cell).Floors[0].Vertices.Select(cell.Frame.LocalToParent).ToArray();
        foreach (var via in vias)
        {
            for (var i = 0; i < floor.Length; i++)
            {
                var (a, b) = (floor[i], floor[(i + 1) % floor.Length]);
                var along = Vector3.Normalize(b - a);
                var distance = Vector3.Distance(via, a + (along * Vector3.Dot(via - a, along)));
                Assert.True(distance >= CellRoutePlanner.HumanHalfWidth, $"the via {via} stands {distance:F2} m from the floor's edge");
            }
        }

        var goal = HoltburgGoals().First();
        var request = rig.Goal(goal.Cell, goal.Point, 0);
        var source = rig.Planner.SourceFor(HallBack, JourneyLegPlanner.GoalCellOf(request), rig.Map);
        Assert.True(((INavCellAvoider)source).Avoid(HallFront));
        var path = source.Plan(RehearsalStart, request, new NavScars());
        var status = rig.Confirmer.ConfirmAll(path.Waypoints, request.StopTolerance);
        output.WriteLine($"by the back door: {path.Status} {path.Reason}; cells {string.Join(' ', path.CellsEntered.Select(c => c.ToString("X8")))}; {status} {rig.Confirmer.Failure}");
        Assert.Equal(NavPathStatus.Found, path.Status);
        Assert.Contains(BackDoor, path.CellsEntered);
        Assert.Equal(RouteConfirmStatus.Confirmed, status);
    }

    /// <summary>
    /// The class, in Holtburg: every station on the climb (every scriveners.json place in the two landblocks, upper floor included; the outskirts' and the den's vendors) is walked to from the lifestone as the resupply asks for it, and from where
    /// the walker ended, the walk out to the lifestone point and to the outskirts' first cell is walked by the runner over the walker's physics. Closes the walks of TODO(m6-holtburg-upper-floor).
    /// </summary>
    [RequiresDatsFact]
    public void Every_station_in_Holtburg_walks_out_to_the_lifestone_point_and_the_outskirts_first_cell()
    {
        var rig = HoltburgRig();
        var failures = new List<string>();
        var stations = HoltburgStations().ToList();
        Assert.True(stations.Count >= 19, $"{stations.Count} stations");
        foreach (var station in stations)
        {
            using var body = new Body(this, rig, rig.Home);
            var goal = rig.Goal(station.Cell, station.Point, station.Range);
            var walk = body.Walk(goal, station.Name + " in");
            output.WriteLine($"{station.Name} 0x{station.Guid:X8}: in {walk.State} at 0x{body.At.CellId:X8} {body.At.Origin}");
            if (walk.State != NavRunState.Arrived)
            {
                failures.Add($"{station.Name}: the walk in: {walk.State}: {walk.Why}");
                continue;
            }

            var stood = body.At;
            foreach (var (name, cell, point) in HoltburgGoals())
            {
                body.Place(stood);
                var way = body.Walk(rig.Goal(cell, point, 0), station.Name + " to " + name);
                output.WriteLine($"  to {name}: {way.State} at 0x{body.At.CellId:X8} {body.At.Origin}, refusals {way.ConfirmFailures}");
                if (way.State != NavRunState.Arrived)
                {
                    failures.Add($"{station.Name}: to {name}: {way.State}, {way.ConfirmFailures} refusals: {way.Why}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    /// <summary>
    /// ACE's walk-to-use MoveTo moves a body after the walker's stop, so a walk out starts anywhere near the stop (the rehearsal did): from every point of rings of 1, 2 and 3 m round each indoor station's end where a body stands in the same building, on
    /// the end's floor, the walk out to the lifestone point arrives: the physics confirms the plan, or (a refused first plan is logged) the runner's own replan walks it.
    /// </summary>
    [RequiresDatsFact]
    public void From_every_standable_point_round_a_station_the_walk_out_arrives()
    {
        var rig = HoltburgRig();
        var failures = new List<string>();
        var (tried, refused) = (0, 0);
        var goal = HoltburgGoals().First();
        foreach (var station in HoltburgStations().Where(s => !LandDefs.IsOutdoorCell(s.Cell)))
        {
            using var body = new Body(this, rig, rig.Home);
            var walk = body.Walk(rig.Goal(station.Cell, station.Point, station.Range), station.Name + " in");
            Assert.Equal(NavRunState.Arrived, walk.State);
            var end = body.At;
            foreach (var radius in new[] { 1f, 2f, 3f })
            {
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * MathF.PI / 4f;
                    var ring = end.Origin + new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), 0.05f);
                    if (!body.TryStand(new CellFrame(end.CellId, new AcFrame(ring, Quaternion.Identity)), end, out var start))
                    {
                        continue;
                    }

                    tried++;
                    var (path, request) = rig.Plan(start, goal.Cell, goal.Point, 0);
                    var status = path.Status == NavPathStatus.Found ? rig.Confirmer.ConfirmAll(path.Waypoints, request.StopTolerance) : RouteConfirmStatus.Failed;
                    if (status == RouteConfirmStatus.Confirmed)
                    {
                        continue;
                    }

                    refused++;
                    var refusal = rig.Confirmer.Failure;
                    body.Place(start);
                    var walked = body.Walk(request, $"{station.Name} r{radius} k{k} out");
                    output.WriteLine($"{station.Name} r{radius} k{k} 0x{start.CellId:X8} {start.Origin}: {path.Status} {path.Reason}; {refusal}; walked: {walked.State}");
                    if (walked.State != NavRunState.Arrived)
                    {
                        failures.Add($"0x{station.Guid:X8} r{radius} k{k}");
                        output.WriteLine($"never arrived: {station.Name} 0x{station.Guid:X8} r{radius} k{k} 0x{start.CellId:X8} {start.Origin}: {walked.State}: {walked.Why}");
                    }
                }
            }
        }

        output.WriteLine($"{tried} starts tried, {refused} refused at the first plan, {failures.Count} never arrived\n" + string.Join('\n', failures));
        Assert.True(tried > 100, $"{tried} starts");
        Assert.Equal(RingGaps.Keys.Order(), failures.Order());
    }

    /// <summary>
    /// The class, on the Town Network: from the drop, every scrivener of the learn trips' towns is walked to as the resupply asks, and from where the walker ended the walk back to the town's Network portal (within his reach, as the journey's portal
    /// leg) is walked by the runner. <see cref="TownNetworkTripDatTests"/> proves the way in only.
    /// </summary>
    [RequiresDatsFact]
    public void Every_learn_stop_of_the_Network_towns_walks_back_to_the_towns_Network_portal()
    {
        var failures = new List<string>();
        foreach (var (town, drop, leave) in NetworkTowns)
        {
            var edge = TravelRoutes.Default.Edge(drop);
            var start = new CellFrame(edge.DestCell, new AcFrame(edge.DestOrigin, Quaternion.Identity));
            var portal = TravelRoutes.Default.Edge(leave);
            var rig = StreamRig(start);
            foreach (var place in ScrivenerCatalog.Default.Scriveners.SelectMany(s => s.Places.Select(p => (s.Name, Place: p))).Where(p => p.Place.Town == town))
            {
                using var body = new Body(this, rig, start);
                var (cell, point, range) = place.Place.StopPoint is { } stop ? (place.Place.StopCell!.Value, stop, 0.0) : (place.Place.Cell, place.Place.Point, ShopLeg.UnseenVendorReachMetres);
                var walk = body.Walk(rig.Goal(cell, point, range), $"{town} {place.Name} in");
                output.WriteLine($"{town} {place.Name} 0x{place.Place.Guid:X8}: in {walk.State} at 0x{body.At.CellId:X8} {body.At.Origin}");
                if (walk.State != NavRunState.Arrived)
                {
                    failures.Add($"{town} {place.Name}: the walk in: {walk.State}: {walk.Why}");
                    continue;
                }

                var back = body.Walk(rig.Goal(portal.Cell, portal.Origin, ShopLeg.UnseenVendorReachMetres), $"{town} {place.Name} to the Network portal");
                output.WriteLine($"  to the portal: {back.State} at 0x{body.At.CellId:X8} {body.At.Origin}, refusals {back.ConfirmFailures}");
                if (back.State != NavRunState.Arrived)
                {
                    failures.Add($"{town} {place.Name}: to the portal: {back.State}, {back.ConfirmFailures} refusals: {back.Why}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    /// <summary>The two walks out: the home walk's lifestone point and the way back's first cell of the outskirts, as <c>LogisticsStation.WalkBack</c> asks for it.</summary>
    private IEnumerable<(string Name, uint Cell, Vector3 Point)> HoltburgGoals()
    {
        var outskirts = GroundCatalog.Get(Outskirts);
        var first = WayHomeRule.FirstCell(outskirts)!;
        var hunt = WayHomeRule.HuntCellOf(first);
        yield return ("the lifestone point", outskirts.Home.LifestoneCell, outskirts.Home.LifestonePoint);
        yield return ("the outskirts' first cell", first.Cell, HuntGround.StopPoint(hunt, HuntGround.TerrainHeight(Dats, hunt)!.Value));
    }

    /// <summary>Every station by guid: the catalog's Holtburg places and the two profiles' vendors, as the resupply's <c>RoomRequest</c> asks for them: the stop, else his own point with his unseen reach as the stop.</summary>
    private static IEnumerable<Station> HoltburgStations()
    {
        var places = ScrivenerCatalog.Default.Scriveners.SelectMany(s => s.Places.Select(p => Row(s.Name, p.Guid, p.StopCell ?? p.Cell, p.StopPoint, p.Point)));
        var vendors = GroundCatalog.Get(Outskirts).Vendors.Concat(GroundCatalog.Get(Den).Vendors).Select(v => Row(v.Name, v.Guid, v.StopCell, v.StopPoint, v.Point));
        return places.Concat(vendors).Where(s => s.Cell >> 16 is 0xA9B3 or 0xA9B4).DistinctBy(s => s.Guid);

        static Station Row(string name, uint guid, uint cell, Vector3? stop, Vector3 own) =>
            stop is { } at ? new Station(name, guid, cell, at, 0) : new Station(name, guid, cell, own, ShopLeg.UnseenVendorReachMetres);
    }

    private WalkRig HoltburgRig()
    {
        var ground = GroundCatalog.Get(Outskirts);
        var world = ServerObjectPhysicsTests.HoltburgWorld(Dats);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        var builder = new LandblockCollisionBuilder(Dats);
        var map = new NavMap();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -2; dy <= 1; dy++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value)));
            }
        }

        var home = new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity));
        return new WalkRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, home, world, new RouteConfirmer(world, walker.Physics.Setup!), walker.Physics.Setup!.Spheres[0]);
    }

    /// <summary>A rig over the headless streamer's three by three round <paramref name="start"/> (as <see cref="TownNetworkTripDatTests"/>): the town's own landblocks.</summary>
    private WalkRig StreamRig(in CellFrame start)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        world.UseTime(0.0);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new WalkRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, start, world, new RouteConfirmer(world, walker.Physics.Setup!), walker.Physics.Setup!.Spheres[0]);
    }

    /// <summary>A station's walk as the resupply asks for it.</summary>
    private sealed record Station(string Name, uint Guid, uint Cell, Vector3 Point, double Range);

    /// <summary>What a walk came to: the runner's end state, the physics' refusals on the way, why it ended.</summary>
    private readonly record struct WalkEnd(NavRunState State, int ConfirmFailures, string? Why);

    /// <summary>One body walking by the runner over the walker's physics, as <see cref="HoltburgScrivenerWalkDatTests"/>'s walk.</summary>
    private sealed class Body : IDisposable
    {
        private readonly HoltburgWalkOutDatTests _test;
        private readonly WalkRig _rig;
        private readonly ActionHarness _h = new(offline: true);
        private readonly MovementAudit _audit;
        private readonly OfflineWalker _walker;
        private double _now;

        public Body(HoltburgWalkOutDatTests test, WalkRig rig, in CellFrame start)
        {
            (_test, _rig) = (test, rig);
            _audit = new MovementAudit(new RecordingMovementSink(), _h.R.Bus, () => _now, "walk-out");
            _walker = OfflineWalker.Create(test.Dats, rig.World, NullLogSink.Instance, new OfflinePlayerWeenie(230), sink: _audit);
            _h.Api.Move.Bind(_walker.Controller, _walker.Controller.Options.RunByDefault);
            rig.World.UseTime(0.0);
            Place(start);
        }

        public CellFrame At => _walker.Position;

        public void Place(in CellFrame at) => Assert.True(_walker.Place(at), $"placed at {at}");

        /// <summary>True when a body placed at <paramref name="at"/> stands there: not slid away, on the floor of <paramref name="end"/>, indoors in the same landblock; <paramref name="stands"/> is where.</summary>
        public bool TryStand(in CellFrame at, in CellFrame end, out CellFrame stands)
        {
            stands = default;
            if (!_walker.Place(at))
            {
                return false;
            }

            stands = _walker.Position;
            var slid = Vector2.Distance(new Vector2(stands.Origin.X, stands.Origin.Y), new Vector2(at.Origin.X, at.Origin.Y));
            return slid <= 0.3f && MathF.Abs(stands.Origin.Z - end.Origin.Z) <= 0.5f && stands.CellId >> 16 == end.CellId >> 16 && !LandDefs.IsOutdoorCell(stands.CellId);
        }

        public WalkEnd Walk(NavGoal goal, string name)
        {
            using var confirmer = new RouteConfirmer(_rig.World, _walker.Physics.Setup!);
            var source = _rig.Planner.SourceFor(At.CellId, JourneyLegPlanner.GoalCellOf(goal), _rig.Map);
            var runner = new NavRunner(_h.Api.Move, new ControllerNavBody(_walker.Controller), source, confirmer, _audit, new NavRunnerOptions { Name = name, PredictiveSteering = true, PredictiveOutdoors = true }); // as the journeys steer (GodotJourneyRunnerHost)
            var began = _now;
            runner.Start(goal, _now);
            while (!runner.IsDone && _now - began < WalkSeconds)
            {
                _now += 1.0 / Fps;
                _walker.Controller.Tick(_now);
                runner.Step(_now);
                _h.Tick(1.0 / Fps);
            }

            _test.Out.WriteLine($"  {name}: {runner.State} after {_now - began:F1} s; {runner.VerdictFields()}; {runner.FailureReason}");
            var end = new WalkEnd(runner.State, runner.ConfirmFailures, runner.FailureReason);
            runner.Dispose();
            return end;
        }

        public void Dispose() => _audit.Dispose();
    }

    /// <summary>The journeys' leg planner over a town's collision, with the walker's confirmer on the human body.</summary>
    private sealed record WalkRig(JourneyLegPlanner Planner, J1JourneyRig View, NavMap Map, CellFrame Home, PhysicsWorld World, RouteConfirmer Confirmer, PhysSphere Foot)
    {
        /// <summary>A point request: the cell and point, with <paramref name="range"/> as its stop when positive.</summary>
        public NavGoal Goal(uint cell, Vector3 point, double range)
        {
            var request = new TravelRequest(new TeleportDestination("walk-out", [], new WorldLocation("walk-out", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test"), TravelPurpose.Cell, Range: range);
            return Planner.GoalOf(View.View, request);
        }

        /// <summary>The plan of a point request from <paramref name="from"/>, by the leg's own source.</summary>
        public (NavPath Path, NavGoal Goal) Plan(in CellFrame from, uint cell, Vector3 point, double range)
        {
            var goal = Goal(cell, point, range);
            return (Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), Map).Plan(from, goal, new NavScars()), goal);
        }
    }
}
