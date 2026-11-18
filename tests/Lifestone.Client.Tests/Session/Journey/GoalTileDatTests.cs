using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 4 W1 on the retail dats: the walks to a room past the attached tiles (<c>m6-walk-goal-tile</c>) and between rooms of two landblocks (cross-landblock-rooms). Live in M6 item 1, a walk
/// from Fort Tethana's lifestone (0x2780) to Zisaha's shop room in 0x2481 failed <c>no-tile</c> at once (the goal room's landblock had no tile: a leg planned only over the tiles round its
/// start), and a walk from Holtburg's healer room 0xA9B40111 to the Apprentice Scrivener of Life Magic in 0xA9B3 failed <c>indoor-start-other-landblock</c> (no source joined two landblocks' rooms).
/// Here the tiles come from the headless host's streamer (<see cref="HeadlessStreamer"/>: the start's 3 x 3 first, then the focus moved to each horizon's end, as the walking body moves it), the
/// path source is the journeys' own (<see cref="JourneyLegPlanner.SourceFor(uint, uint, NavMap?)"/>), and every plan is confirmed by the walker's physics (<see cref="RouteConfirmer"/>).
/// </summary>
/// <remarks>
/// The points: Fort Tethana's lifestone from the Temple 40+ profile's home section (<c>Grounds/profiles/tusker-temple-fort-tethana.json</c>: 0x27800006 at (18.7945, 137.695, 144.005), its
/// landblock_instance row 0x72780000); Zisaha bint Jaidka the Mage from the catalog's scarab sellers (<c>Grounds/scriveners.json</c> <c>scarab_sellers</c>, the buy-trip's seller rows: guid 0x72481001
/// in 0x24810102 at (85.0908, 132.949, 219.245)); the Grand Master Scrivener of Creature Magic from its places (guid 0x72581033 in 0x25810170 at (107.076, 87.928, 220.005)); Holtburg's healer room is
/// Thelnoth's cellar 0xA9B40111 (docs/wayfinding.md "The room graph"), the Life apprentice's room 0xA9B30102 and his catalog stop the front room 0xA9B30100. All the pinned world SQL v0.9.296's rows.
/// The goals are the requests the stations make (<c>ResupplyStation.RoomRequest</c>, <c>BuyTripStation</c>'s point request): a point in the room within the unseen vendor's reach, or the catalog's stop.
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed class GoalTileDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Temple = "tusker-temple-fort-tethana";
    private const string Den = "black-spawn-den-aluvian";
    private const uint ZisahaGuid = 0x72481001;
    private const uint CreatureGrandMasterGuid = 0x72581033;
    private const uint GrandMasterRoom = 0x25810170;

    /// <summary>The stop before the Grand Master's counter (M6 4 W2's probe; confirmed with its ring standing here, M6 4 w6a): 3.04 m from his point (107.076, 87.928).</summary>
    private static readonly Vector3 GrandMasterStop = new(107.3f, 84.9f, 220f);
    private const uint LifeApprenticeGuid = 0x7A9B3009;
    private const uint HealerRoom = 0xA9B40111;
    private const int MaxHops = 8;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Fort_Tethanas_lifestone_reaches_Zisahas_room_three_landblocks_away_through_horizons_and_the_room_route()
    {
        var zisaha = ScrivenerCatalog.Default.ScarabSellers.SelectMany(s => s.Places).Single(p => p.Guid == ZisahaGuid);
        Assert.Equal(0x24810102u, zisaha.Cell);
        var (rig, path) = WalkFromTheLifestone("Zisaha", zisaha.Cell, zisaha.Point, ShopLeg.UnseenVendorReachMetres, whole: true);
        rig.Dispose();
        Assert.Equal([0x24810101u, 0x24810100u, 0x24810102u], path.CellsEntered);
    }

    /// <summary>
    /// The Grand Master Scrivener of Creature Magic stands inside the fort's walls, behind his counter. TODO(m6-tethana-grand-masters), the gate: the gatehouse building 0x01001030 at
    /// (180, 84, 220) in 0x2581 used to stamp both nodes of the gate's passage (x 173, y 83 and 84) blocked by its projected shell, though the walker's physics walks straight through it;
    /// since M6 4 w6a the building stamp opens a node the body stands on clear of the shell's own physics, outside the building's rooms, so the walk from the lifestone passes the gate
    /// (the fixed world: live the server's gate door 0x72581042 'doorprisonactivated' stands in it, opened only by its lever 0x72581044, TODO(door-lever)). The stop: the walk to his own
    /// point runs into his counter (the ground probe refuses the push into his room at (106.94, 85.47), or from the east door in 0x2581016D at (112.41, 86.09), TODO(room-steps)), so the
    /// walk ends at a stop before the counter, (107.3, 84.9, 220) in his room 0x25810170, 3.04 m from him, which the physics confirms and whose stop ring stands (the check of
    /// <c>VendorStopConfirmDatTests</c>). The stop belongs in <c>Grounds/scriveners.json</c> (item 3's file); until it is there the catalog's stop is his own point and this test pins the
    /// stop it should carry. The landblock_instance rows are the pinned world SQL v0.9.296's.
    /// </summary>
    [RequiresDatsFact]
    public void Fort_Tethanas_lifestone_walks_through_the_gate_to_a_stop_before_the_Grand_Master_Scriveners_counter()
    {
        var vendor = ScrivenerCatalog.Default.VendorsIn("Fort Tethana").Single(v => v.Guid == CreatureGrandMasterGuid);
        Assert.Equal(0x25810170u, vendor.Cell);
        if (vendor.StopPoint is { } catalogStop)
        {
            Assert.Equal(GrandMasterRoom, vendor.StopCell); // the catalog's stop, once item 3 writes it, is the one confirmed here
            Assert.True(Vector3.Distance(catalogStop, GrandMasterStop) < 0.05f, $"catalog stop {catalogStop}");
        }

        Assert.InRange(Vector2.Distance(new Vector2(GrandMasterStop.X, GrandMasterStop.Y), new Vector2(vendor.Point.X, vendor.Point.Y)), 3.0f, 3.1f);
        var (rig, path) = WalkFromTheLifestone(vendor.Name, GrandMasterRoom, GrandMasterStop, 0, whole: true);
        using (rig)
        {
            // Through the gate: a segment of the outdoor walk crosses x 173.5 inside the passage (y 82.5 to 85.5).
            var outdoor = path.Waypoints.Where(w => !w.Indoor && w.At.CellId >> 16 == 0x2581).Select(w => w.At.Origin).ToList();
            var through = outdoor.Zip(outdoor.Skip(1)).Any(p => (p.First.X - 173.5f) * (p.Second.X - 173.5f) <= 0f && Math.Min(p.First.Y, p.Second.Y) <= 85.5f && Math.Max(p.First.Y, p.Second.Y) >= 82.5f);
            output.WriteLine($"outdoor in 0x2581: {string.Join(' ', outdoor.Select(o => $"({o.X:F1},{o.Y:F1})"))}");
            Assert.True(through, "no segment through the gate");

            var goal = rig.Goal(GrandMasterRoom, GrandMasterStop, 0);
            AssertRingStands(rig, GrandMasterRoom, GrandMasterStop, goal.StopTolerance);

            // His own point, once refused (the reason for the stop): since M6 7 G the room graph crosses beside the pillar on the portal's centre (his hall is Holtburg's), and from before his building's west door the room plan is whole and the physics confirms it. The stop stays: it is before his counter.
            var own = rig.Goal(vendor.Cell, vendor.Point, ShopLeg.UnseenVendorReachMetres);
            var inside = NavMap.ToCellFrame(new NavPoint(0x25810000u, new Vector3(103f, 81.2f, 220f))); // 4 m out of the west door's sill (106.04, 83.76)
            var source = rig.Planner.SourceFor(inside.CellId, JourneyLegPlanner.GoalCellOf(own), rig.Map);
            var room = source.Plan(inside, own, new NavScars());
            var confirm = rig.Confirm(room, own);
            output.WriteLine($"{vendor.Name}'s own point from inside the walls 0x{inside.CellId:X8}: {room.Status} {room.Reason}; cells {Cells(room)}; confirm {confirm} {rig.Confirmer.Failure}");
            Assert.Equal(NavPathStatus.Found, room.Status);
            Assert.Equal([0x25810172u, 0x25810170u], room.CellsEntered);
            Assert.Equal(RouteConfirmStatus.Confirmed, confirm);
        }
    }

    /// <summary>The stop and the eight points of its tolerance round it find the room's floor through the statics at the stop's height (<c>VendorStopConfirmDatTests.AssertRingStands</c>, the whole ring).</summary>
    private void AssertRingStands(StreamRig rig, uint cell, Vector3 stop, float tolerance)
    {
        for (var k = -1; k < 8; k++)
        {
            var a = k * MathF.PI / 4f;
            var point = k < 0 ? stop : stop + new Vector3(tolerance * MathF.Cos(a), tolerance * MathF.Sin(a), 0f);
            var found = rig.World.TryFixedGroundZ(new CellFrame(cell, new AcFrame(point, Quaternion.Identity)), rig.Foot, out var z, out _);
            output.WriteLine($"   ring ({point.X:F2}, {point.Y:F2}): {(found ? $"floor at {z:F2}" : "no floor")}");
            Assert.True(found, $"no walkable floor at ({point.X:F2}, {point.Y:F2}) of 0x{cell:X8}");
            Assert.InRange(z, stop.Z - 0.2f, stop.Z + 0.1f);
        }
    }

    /// <summary>
    /// The healer's room 0xA9B40111 (0xA9B4) to the Life apprentice's room 0xA9B30102 (0xA9B3) plans as one composed walk: out of the cellar through its door, outdoors, in through the
    /// scriveners' door. The station's walk (his catalog stop, the front room 0xA9B30100, M6 1) is confirmed by the physics; the walk on into 0xA9B30102 itself plans, and the physics
    /// refuses its last push onto his counter as <c>HoltburgScrivenerWalkDatTests</c> shows (the reason the catalog has the stop).
    /// </summary>
    [RequiresDatsFact]
    public void Holtburgs_healer_room_reaches_the_Life_apprentices_room_as_one_composed_walk()
    {
        var ground = GroundCatalog.Get(Den);
        var life = ScrivenerCatalog.Default.VendorsIn("Holtburg").Single(v => v.Guid == LifeApprenticeGuid);
        Assert.Equal(0xA9B30102u, life.Cell);
        using var rig = Rig(new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity)));
        var healer = rig.Planner.CellsOf(HealerRoom);
        var start = new RoomGraph(healer).RoomFrame(HealerRoom)!.Value;
        rig.Streamer.EnsureNow(start.CellId);

        foreach (var (cell, point, range) in new (uint, Vector3, double)[] { (life.StopCell, life.StopPoint ?? life.Point, life.StopPoint is null ? ShopLeg.UnseenVendorReachMetres : 0), (life.Cell, life.Point, ShopLeg.UnseenVendorReachMetres) })
        {
            var goal = rig.Goal(cell, point, range);
            var source = Assert.IsType<IndoorOutdoorPathSource>(rig.Planner.SourceFor(start.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map, out var why));
            output.WriteLine($"0x{cell:X8}: {why}");
            Assert.Equal(0xA9B40000u, source.Rooms.LandblockId); // the start's rooms while the walk starts in them
            var path = source.Plan(start, goal, new NavScars());
            var confirm = rig.Confirm(path, goal);
            output.WriteLine($"0x{cell:X8}: {path.Status} {path.Reason} cells {Cells(path)}; confirm {confirm} {rig.Confirmer.Failure}");
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(HealerRoom, path.Waypoints[0].At.CellId);
            Assert.Contains(path.Waypoints, w => !w.Indoor); // out through the door, outdoors
            Assert.Equal(cell, path.Waypoints[^1].At.CellId); // in to the goal's room
            Assert.Equal(cell, path.CellsEntered[^1]);
            Assert.Contains(path.CellsEntered, c => c >> 16 == 0xA9B4);
            Assert.Contains(path.CellsEntered, c => c >> 16 == 0xA9B3);
            if (cell == life.StopCell)
            {
                Assert.Equal(RouteConfirmStatus.Confirmed, confirm);
            }
        }
    }

    /// <summary>
    /// From Fort Tethana's lifestone with only its 3 x 3 attached: the plan is a horizon (confirmed, no room in it), the streamer's focus moves to its end, and the plan again, until the plan is no
    /// horizon. <paramref name="whole"/>: it must be the whole route into <paramref name="cell"/>, confirmed. Returns the rig (the caller disposes it) and the last plan.
    /// </summary>
    private (StreamRig Rig, NavPath Last) WalkFromTheLifestone(string name, uint cell, Vector3 point, double range, bool whole)
    {
        var ground = GroundCatalog.Get(Temple);
        Assert.Equal("Fort Tethana", ground.Home.Town);
        var home = new CellFrame(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity));
        var rig = Rig(home);
        Assert.Equal(9, rig.Map.AttachedLandblocks.Count);
        Assert.False(rig.Map.TryGetTile(cell & 0xFFFF0000u, out _)); // the goal room's landblock is past the start's 3 x 3

        var goal = rig.Goal(cell, point, range);
        var source = Assert.IsType<IndoorOutdoorPathSource>(rig.Planner.SourceFor(home.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map));
        var at = home;
        var path = source.Plan(at, goal, new NavScars());
        var hops = 0;
        while (path.Status == NavPathStatus.Horizon && hops < MaxHops)
        {
            var confirm = rig.Confirm(path, goal);
            output.WriteLine($"{name} hop {hops}: horizon {path.Waypoints.Count} waypoints to 0x{path.Waypoints[^1].At.CellId:X8} {path.Waypoints[^1].At.Origin}; confirm {confirm} {rig.Confirmer.Failure}");
            Assert.Equal(RouteConfirmStatus.Confirmed, confirm);
            Assert.All(path.Waypoints, w => Assert.False(w.Indoor)); // a horizon walks no room
            at = path.Waypoints[^1].At; // the body at the horizon's end: the streamer's focus moves there (the runner plans again within 60 m of it once the map changed)
            rig.Streamer.EnsureNow(at.CellId);
            path = source.Plan(at, goal, new NavScars());
            hops++;
        }

        var last = rig.Confirm(path, goal);
        output.WriteLine($"{name}: {path.Status} {path.Reason} after {hops} horizons; cells {Cells(path)}; confirm {last} {rig.Confirmer.Failure}; {rig.Streamer.StatsLine()}");
        for (var refusals = 1; whole && last == RouteConfirmStatus.Failed && path.Status == NavPathStatus.Found && refusals < NavRunner.MaxConfirmFailures; refusals++)
        {
            // The runner's answer to a refusal in the rooms (NavRunner.AvoidCellOf): the room graph leaves out the cell the refused sample was on its way into, or, when that
            // is the goal's own room, the room the sample stands in (M6 4 w6a), and plans again.
            var failure = rig.Confirmer.Failure!.Value;
            var avoid = path.Waypoints.Skip(failure.Segment + 1).Select(w => w.At.CellId).FirstOrDefault(c => c != path.Waypoints[failure.Segment].At.CellId);
            avoid = avoid == cell ? failure.At.CellId : avoid;
            Assert.False(LandDefs.IsOutdoorCell(avoid) || avoid == cell || avoid == path.Waypoints[0].At.CellId, $"the refusal is not in a room on the way: {failure}");
            Assert.True(Assert.IsAssignableFrom<INavCellAvoider>(source).Avoid(avoid));
            path = source.Plan(at, goal, new NavScars());
            last = rig.Confirm(path, goal);
            output.WriteLine($"{name}: room 0x{avoid:X8} left out (refused); {path.Status} {path.Reason}; cells {Cells(path)}; confirm {last} {rig.Confirmer.Failure}");
        }

        Assert.InRange(hops, 1, MaxHops - 1); // at least one horizon: the goal's tile was not there at the first plan
        Assert.True(rig.Map.TryGetTile(cell & 0xFFFF0000u, out _)); // streamed in on the way
        if (whole)
        {
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(cell, path.Waypoints[^1].At.CellId);
            Assert.Equal(cell, path.CellsEntered[^1]);
            Assert.Equal(RouteConfirmStatus.Confirmed, last);
        }

        return (rig, path);
    }

    private static string Cells(NavPath path) => string.Join(' ', path.CellsEntered.Select(c => c.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));

    private StreamRig Rig(CellFrame start)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        world.UseTime(0.0);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new StreamRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, world, streamer, new RouteConfirmer(world, walker.Physics.Setup!), walker.Physics.Setup!.Spheres[0]);
    }

    /// <summary>The journeys' leg planner over the headless streamer's tiles and collision, with the walker's confirmer on the human body.</summary>
    private sealed record StreamRig(JourneyLegPlanner Planner, J1JourneyRig View, NavMap Map, PhysicsWorld World, HeadlessStreamer Streamer, RouteConfirmer Confirmer, PhysSphere Foot) : IDisposable
    {
        /// <summary>A point request (the resupply's <c>RoomRequest</c>): the cell and point, with <paramref name="range"/> as its stop when positive.</summary>
        public NavGoal Goal(uint cell, Vector3 point, double range)
        {
            var request = new TravelRequest(new TeleportDestination("room", [], new WorldLocation("room", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test"), TravelPurpose.Vendor, Range: range);
            return Planner.GoalOf(View.View, request);
        }

        /// <summary>The runner's pre-check of a plan: a horizon to its end, a whole path up to the goal's stop tolerance.</summary>
        public RouteConfirmStatus Confirm(NavPath path, NavGoal goal) =>
            path.Status == NavPathStatus.Failed ? RouteConfirmStatus.Failed
            : Confirmer.ConfirmAll(path.Waypoints, path.Status == NavPathStatus.Horizon ? 0f : goal.StopTolerance);

        /// <inheritdoc/>
        public void Dispose()
        {
            Confirmer.Dispose();
            Streamer.Dispose();
        }
    }
}
