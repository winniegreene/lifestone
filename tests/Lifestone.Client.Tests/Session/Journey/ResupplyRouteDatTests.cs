using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// The resupply station's first walk, planned on the retail dats (M5 item 3 fixes F3 and F4, review 3-R10; Exhaustive tier, as the journeys' other dat tests). The forced trip recalls the mage to its home lifestone and walks to the profile's
/// vendors (Archmage Cindrue's entry hall 0xA9B40117 for the den, Thelnoth's cellar, Renald's front room; Fort Tethana's three). Here the journeys' own leg planner (<see cref="JourneyLegPlanner"/>: the goal of the station's first request, the
/// path source its two ends need) plans from the home lifestone's point to each vendor's stop cell and <c>stop_point</c> (else his point), the request <c>ResupplyRun.Meet</c> sends, over the attached terrain tiles, and the plan must be a whole
/// path that ends in the stop cell within the stop tolerance of that point. A stop room in front of a vendor must also name its point, which must stand on a clear floor of that room: the planner appends the goal point it is given, so a point
/// in another room (Renald's own, in his back room) would pass as a route and the body could never stand on it. A vendor with no route is pinned here with its gap (<c>Gaps</c>), so a closed gap fails the test until its row is removed. What
/// this does not show: that the body walks the plan and stops at the stop point, or what ACE's move-to does from there (the lead's first live round, the <c>vendor: ... from his front room</c> line), only whether a route exists.
/// The clear-floor check here reads the cell's floor polygons only, not its statics: Renald's old stop (135.6, 15.8) passed it inside three barrels (M5 item 7's soak, <c>m5-renald-shop-floor</c>); the walker's own pre-check over
/// the planned leg, and the stop ring through the statics, is <see cref="VendorStopConfirmDatTests"/>.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ResupplyRouteDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const string Den = "black-spawn-den-aluvian";
    private const string Temple = "tusker-temple-fort-tethana";

    /// <summary>The metres of floor all round a stop point (the human body's radius is 0.48 m).</summary>
    private const float Clearance = 0.5f;

    /// <summary>
    /// The vendors the planner finds no route to, by ground and name, with the planner's reason. Empty since M6 4 w6a: Fort Tethana's two vendors in landblock 0x2581 (Shopkeeper Yoshi Deshi,
    /// Blacksmith Magarith; the gaps this test found on 2026-10-27, <c>no-path</c>) are reached through the fort's gate, whose two passage nodes the gatehouse's building stamp no longer blocks
    /// (the body stands there clear of the shell's physics). Live the gate's door opens only by its lever (<c>TODO(door-lever)</c>), and the walk is unverified live (<c>TODO(m6-ground-live-facts)</c>).
    /// </summary>
    private static readonly Dictionary<string, string> Gaps = [];

    [RequiresDatsFact]
    public void The_dens_vendors_are_reached_from_the_lifestone_at_their_stop_points_Cindrue_in_her_entry_hall() => Check(Den);

    [RequiresDatsFact]
    public void Fort_Tethanas_vendors_are_reached_from_its_lifestone_the_two_in_landblock_0x2581_through_the_forts_gate() => Check(Temple);

    [RequiresDatsFact]
    public void Cindrues_entry_hall_is_the_nearest_room_of_her_building_that_routes_and_her_own_cell_routes_by_her_stair()
    {
        var ground = GroundCatalog.Get(Den);
        var cindrue = ground.Vendors.First(v => v.Name == "Archmage Cindrue");
        var planner = new JourneyLegPlanner(fixture.Dats);
        var map = Map(ground);
        var home = Home(ground);
        var cells = planner.CellsOf(cindrue.StopCell);
        var rooms = new RoomGraph(cells);
        var nearest = new Dictionary<uint, float>();
        foreach (var room in new uint[] { 0xA9B40117, 0xA9B40118, 0xA9B40119 })
        {
            var goal = NavGoal.ToPoint(new NavPoint(room & 0xFFFF0000u, rooms.RoomFrame(room)!.Value.Origin), room);
            var path = planner.SourceFor(home.CellId, room, map).Plan(home, goal, new NavScars());
            nearest[room] = NearestFloorMetres(cells, room, cindrue.Point);
            output.WriteLine($"Cindrue's room 0x{room:X8}: {path.Status} {Metres(path):F1} m {path.Reason}; its floor comes within {nearest[room]:F2} m of her");
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.Equal(room, path.Waypoints[^1].At.CellId);
        }

        Assert.Equal(cindrue.StopCell, nearest.MinBy(kv => kv.Value).Key); // the hall under her floor, not the doorway rooms off it
        Assert.InRange(Vector3.Distance(cindrue.StopPoint!.Value, cindrue.Point), 0f, nearest[cindrue.StopCell] + 0.1f);

        // Her own cell was a step the room graph refused (TODO(room-steps)): the reason her stop is a room in front of her. Since M6 4 W6b the graph walks the stair from the
        // hall up to it (CellRoutePlanner.StairOf; the body climbs it in IslandDungeonWalkDatTests.Cindrues_stair_...), so her own cell routes: her stop may move back to
        // 0xA9B4011B (item 3's profile, the lead's call; the hall stays a stop that routes).
        var own = planner.SourceFor(home.CellId, cindrue.Cell, map).Plan(home, NavGoal.ToPoint(new NavPoint(cindrue.Cell & 0xFFFF0000u, cindrue.Point), cindrue.Cell), new NavScars());
        output.WriteLine($"Cindrue's own cell 0x{cindrue.Cell:X8}: {own.Status} {Metres(own):F1} m {own.Reason}");
        Assert.Equal(NavPathStatus.Found, own.Status);
        Assert.Equal(cindrue.Cell, own.Waypoints[^1].At.CellId);
    }

    /// <summary>
    /// M5 7 P7k (D143): the leg from Archmage Cindrue's entry hall 0xA9B40117 to the Scrivener of Item Magic, who stands outdoors in the next landblock (0xA9B3003C). Live check 2
    /// (2026-10-30 05:03 UTC) stood the mage in the hall after Monyra at (155.42, 134.31, 66.01), and the leg failed at once, <c>indoor-start-unreachable:0xA9B40117: the start is not
    /// one of the rooms</c>: the leg planner built the room graph of the goal's landblock (an outdoor goal is planned to its landblock id 0xA9B30000, not an outdoor cell), so the start's
    /// own rooms were not in it. The leg goes out through the hall's door and on outdoors, over the start's rooms.
    /// </summary>
    [RequiresDatsFact]
    public void The_leg_from_Cindrues_entry_hall_to_the_Scrivener_outdoors_in_the_next_landblock_goes_out_through_her_door()
    {
        var ground = GroundCatalog.Get(Den);
        var planner = new JourneyLegPlanner(fixture.Dats);
        var map = Map(ground);
        var hall = new CellFrame(0xA9B40117, new AcFrame(new Vector3(155.42f, 134.31f, 66.01f), Quaternion.Identity)); // where the mage stood after Monyra
        var scrivener = ground.Vendors.First(v => v.Name == "Scrivener of Item Magic");
        var goal = planner.GoalOf(new J1JourneyRig().View, MeetRequest(scrivener));
        var goalCell = JourneyLegPlanner.GoalCellOf(goal);
        Assert.Equal(0xA9B30000u, goalCell); // an outdoor goal is its landblock
        var source = Assert.IsType<IndoorOutdoorPathSource>(planner.SourceFor(hall.CellId, goalCell, map));
        Assert.Equal(0xA9B40000u, source.Rooms.LandblockId); // the start's rooms, not the goal's landblock's
        var path = source.Plan(hall, goal, new NavScars());
        output.WriteLine($"hall -> Scrivener: {path.Status} {path.Reason} {Metres(path):F1} m, {path.Waypoints.Count} waypoints: {string.Join(" ", path.Waypoints.Select(w => $"{w.At.CellId:X8}"))}");
        Assert.Equal(NavPathStatus.Found, path.Status);
        Assert.Equal(hall.CellId, path.Waypoints[0].At.CellId);
        Assert.Contains(path.Waypoints, w => w.Doorway); // out through a door of her building
        AssertEndsAtStop(scrivener, path);
    }

    /// <summary>
    /// M5 7 P7k (D143): the den's whole town walk as live check 2 ordered it (Shopkeeper Renald the Elder, Archmage Cindrue, Monyra the Jeweler, Scrivener of Item Magic), each leg
    /// planned from where the last one ended (the first from the home lifestone), with the station's request (<c>ResupplyRun.Meet</c>: the stop cell and stop point). Every leg is a
    /// whole path to its stop. What this does not show: the walk itself (the lead's live trip).
    /// </summary>
    [RequiresDatsFact]
    public void The_dens_town_walk_Renald_Cindrue_Monyra_Scrivener_plans_every_leg()
    {
        var ground = GroundCatalog.Get(Den);
        var planner = new JourneyLegPlanner(fixture.Dats);
        var rig = new J1JourneyRig();
        var map = Map(ground);
        var at = Home(ground);
        foreach (var name in new[] { "Shopkeeper Renald the Elder", "Archmage Cindrue", "Monyra the Jeweler", "Scrivener of Item Magic" })
        {
            var vendor = ground.Vendors.First(v => v.Name == name);
            var goal = planner.GoalOf(rig.View, MeetRequest(vendor));
            var path = planner.SourceFor(at.CellId, JourneyLegPlanner.GoalCellOf(goal), map).Plan(at, goal, new NavScars());
            output.WriteLine($"0x{at.CellId:X8} {at.Origin} -> {name}: {path.Status} {path.Reason} {Metres(path):F1} m, {path.Waypoints.Count} waypoints");
            Assert.True(path.Status == NavPathStatus.Found, $"the leg to {name} from 0x{at.CellId:X8} {at.Origin}: {path.Status} {path.Reason}");
            Assert.Equal(at.CellId, path.Waypoints[0].At.CellId);
            AssertEndsAtStop(vendor, path);
            at = path.Waypoints[^1].At; // the next leg starts where this one ends
        }
    }

    /// <summary>The station's request for a vendor not in view (<c>ResupplyRun.Meet</c>): his stop cell and stop point (else his point), no object, no range.</summary>
    private static TravelRequest MeetRequest(GroundVendor vendor) =>
        new(new TeleportDestination(vendor.Name, [], new WorldLocation(vendor.Name, new CellPosition(vendor.StopCell, vendor.StopPoint ?? vendor.Point), HasHeight: true, HeadingDegrees: 0), "resupply route test"), TravelPurpose.Vendor);

    /// <summary>The path ends in the vendor's stop cell (outdoors: its landblock) within the stop tolerance of his stop point.</summary>
    private static void AssertEndsAtStop(GroundVendor vendor, NavPath path)
    {
        var stop = vendor.StopPoint ?? vendor.Point;
        var outdoor = (vendor.StopCell & 0xFFFF) < 0x100;
        var end = path.Waypoints[^1];
        Assert.Equal(outdoor ? vendor.StopCell >> 16 : vendor.StopCell, outdoor ? end.At.CellId >> 16 : end.At.CellId);
        Assert.InRange(Vector2.Distance(new Vector2(end.At.Origin.X, end.At.Origin.Y), new Vector2(stop.X, stop.Y)), 0f, NavGoal.DefaultStopTolerance);
        Assert.InRange(MathF.Abs(end.At.Origin.Z - stop.Z), 0f, NavRunner.ArrivalHeightMetres);
    }

    private void Check(string key)
    {
        var ground = GroundCatalog.Get(key);
        var planner = new JourneyLegPlanner(fixture.Dats);
        var rig = new J1JourneyRig();
        var map = Map(ground);
        var home = Home(ground);
        foreach (var vendor in ground.Vendors)
        {
            // The station's first request when the vendor is not in view yet, and every request for a vendor in a room in front of him (ResupplyRun.Meet): his stop cell and stop point (else his point), no object, no range.
            var stop = vendor.StopPoint ?? vendor.Point;
            var request = new TravelRequest(new TeleportDestination(vendor.Name, [], new WorldLocation(vendor.Name, new CellPosition(vendor.StopCell, stop), HasHeight: true, HeadingDegrees: 0), "resupply route test"), TravelPurpose.Vendor);
            var goal = planner.GoalOf(rig.View, request);
            var goalCell = JourneyLegPlanner.GoalCellOf(goal);
            var path = planner.SourceFor(home.CellId, goalCell, map).Plan(home, goal, new NavScars());
            var metres = Metres(path);
            var cellsEntered = string.Join(" ", path.CellsEntered.Select(c => (c & 0xFFFF).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            output.WriteLine($"{key} / {vendor.Name}: stop 0x{vendor.StopCell:X8} at {stop}, {path.Status} {path.Reason} {metres:F1} m, {path.Waypoints.Count} waypoints, cells entered {cellsEntered}");
            var outdoor = (vendor.StopCell & 0xFFFF) < 0x100; // M5 7 P7j: the Scrivener of Item Magic stands outdoors, south of Holtburg: an outdoor goal is its landblock
            Assert.Equal(outdoor ? vendor.StopCell & 0xFFFF0000u : vendor.StopCell, goalCell); // the profile's stop cell is the goal's room
            if (Gaps.TryGetValue(key + "/" + vendor.Name, out var gap))
            {
                Assert.Equal(NavPathStatus.Failed, path.Status);
                Assert.Equal(gap, path.Reason);
                continue;
            }

            Assert.Equal(NavPathStatus.Found, path.Status);
            var end = path.Waypoints[^1];
            Assert.Equal(outdoor ? vendor.StopCell >> 16 : vendor.StopCell, outdoor ? end.At.CellId >> 16 : end.At.CellId); // ends in the stop cell (outdoors: its landblock)
            Assert.InRange(Vector2.Distance(new Vector2(end.At.Origin.X, end.At.Origin.Y), new Vector2(stop.X, stop.Y)), 0f, NavGoal.DefaultStopTolerance); // within the stop tolerance of the stop point
            Assert.InRange(MathF.Abs(end.At.Origin.Z - stop.Z), 0f, NavRunner.ArrivalHeightMetres);
            Assert.Equal(home.CellId, path.Waypoints[0].At.CellId); // from the lifestone
            Assert.InRange(metres, 5f, 1500f);
            if (vendor.StopCell != vendor.Cell)
            {
                // A stop room in front of the vendor: the point to stand at is named and is a floor of that room, clear all round.
                Assert.NotNull(vendor.StopPoint);
                Assert.True(OnClearFloor(planner.CellsOf(vendor.StopCell), vendor.StopCell, stop), $"{vendor.Name}: stop point {stop} is not on a clear floor of 0x{vendor.StopCell:X8}");
            }
        }

        Assert.All(Gaps.Keys.Where(k => k.StartsWith(key + "/", StringComparison.Ordinal)), k => Assert.Contains(ground.Vendors, v => key + "/" + v.Name == k));
    }

    private static bool InPolygon(Vector3[] v, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            if ((v[i].Y > y) != (v[j].Y > y) && x < ((v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y)) + v[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>The height of the cell's floor at a cell-local (x, y) within 0.3 m of <paramref name="near"/>, or null when no floor polygon lies there.</summary>
    private static float? FloorZ(IReadOnlyList<FloorPolygon> floors, float x, float y, float near)
    {
        foreach (var f in floors)
        {
            if (f.Plane.Normal.Z > 0.7f && InPolygon(f.Vertices, x, y))
            {
                var z = -((f.Plane.Normal.X * x) + (f.Plane.Normal.Y * y) + f.Plane.D) / f.Plane.Normal.Z;
                if (Math.Abs(z - near) < 0.3f)
                {
                    return z;
                }
            }
        }

        return null;
    }

    /// <summary>True when the landblock-local <paramref name="point"/> stands on a floor of the cell (within 5 cm) with floor at the same height all round, <see cref="Clearance"/> m out (no wall, edge or step beside it).</summary>
    private static bool OnClearFloor(LandblockCellSet cells, uint cellId, Vector3 point)
    {
        var cell = cells.GetCell(cellId)!;
        var floors = cells.GetStructure(cell).Floors;
        var local = cell.Frame.ParentToLocal(point);
        if (FloorZ(floors, local.X, local.Y, local.Z) is not { } z || Math.Abs(z - local.Z) > 0.05f)
        {
            return false;
        }

        for (var k = 0; k < 8; k++)
        {
            var a = k * MathF.PI / 4f;
            if (FloorZ(floors, local.X + (Clearance * MathF.Cos(a)), local.Y + (Clearance * MathF.Sin(a)), z) is null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The straight-line metres from the landblock-local <paramref name="to"/> to the nearest sampled point (a 1/20 fan grid) of the cell's floor polygons.</summary>
    private static float NearestFloorMetres(LandblockCellSet cells, uint cellId, Vector3 to)
    {
        var cell = cells.GetCell(cellId)!;
        var best = float.MaxValue;
        foreach (var f in cells.GetStructure(cell).Floors)
        {
            var v = f.Vertices.Select(p => cell.Frame.LocalToParent(p)).ToArray();
            for (var i = 1; i + 1 < v.Length; i++)
            {
                for (var a = 0; a <= 20; a++)
                {
                    for (var b = 0; b <= 20 - a; b++)
                    {
                        var p = ((v[0] * a) + (v[i] * b) + (v[i + 1] * (20 - a - b))) / 20f;
                        best = Math.Min(best, Vector3.Distance(p, to));
                    }
                }
            }
        }

        return best;
    }

    private static CellFrame Home(HuntingGround ground) => new(ground.Home.LifestoneCell, new AcFrame(ground.Home.LifestonePoint, Quaternion.Identity));

    /// <summary>The terrain tiles of the landblocks between the home lifestone and the vendors, with a landblock of margin: what the journey's streamer would have attached by the time the leg replans.</summary>
    private NavMap Map(HuntingGround ground)
    {
        var blocks = ground.Vendors.Select(v => v.StopCell).Append(ground.Home.LifestoneCell).Select(LandblockCoord.FromId).ToList();
        var (x0, x1) = (blocks.Min(c => c.X) - 1, blocks.Max(c => c.X) + 1);
        var (y0, y1) = (blocks.Min(c => c.Y) - 1, blocks.Max(c => c.Y) + 1);
        var builder = new LandblockCollisionBuilder(fixture.Dats);
        var map = new NavMap();
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                map.Attach(NavTileBuilder.Build(builder.Build(new LandblockCoord((byte)x, (byte)y))));
            }
        }

        return map;
    }

    private static float Metres(NavPath path)
    {
        var metres = 0f;
        for (var i = 1; i < path.Waypoints.Count; i++)
        {
            metres += Vector2.Distance(path.Waypoints[i - 1].Point.Global, path.Waypoints[i].Point.Global);
        }

        return metres;
    }
}

/// <summary>The resupply station's refusal kinds (review 3-R9): ACE's own buy-side lines (Vendor.cs 496, 498, 500 @95ef426d) and the lines the tracker and the sale name map to the station's kinds.</summary>
public sealed class ResupplyRefusalKindTests
{
    [Theory]
    [InlineData("You are too encumbered to buy that!", "pack")]
    [InlineData("You do not have enough pack space to buy that!", "pack")]
    [InlineData("You do not have enough container slots to buy that!", "pack")]
    [InlineData("too few pyreals", "coin")]
    [InlineData("The Wand is unsellable.", "unsellable")]
    [InlineData("the stock is gone", "stock")]
    [InlineData("something unrelated", "other")]
    public void An_ACE_refusal_line_maps_to_the_stations_kind(string detail, string kind) => Assert.Equal(kind, ResupplyRun.Kind(detail, "other"));
}
