using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (the den run), on the retail dats: what the travel file says of the run to the Black Spawn Den at 60.0S 57.6W (route <c>den-run</c> of <c>black-spawn-den-sho</c>). The three new hops' walks are the theory's
/// (<see cref="The_hop_walks_from_the_start_to_the_front_point_and_on_to_the_portal"/>: every edge from every start of its place); here are the measures the route's sentences name: the way inside the Desert March dungeon, the terrain
/// under the two overland lines, the one bend and the walk from the den's exit to the tie point.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>
    /// M6 item 14 (the den run's rehearsal): the rehearsal's straight run from Danby's drop to the point <see cref="DenRunStation.RehearseShortMetres"/> short of the Desert March Portal is 227.7 m, no node of it is too steep, and the
    /// body's physics confirms every piece of it (the run-only straight run asks the same of each of its pieces before a step).
    /// </summary>
    [RequiresDatsFact]
    public void The_rehearsal_s_straight_run_at_danbys_outpost_ends_25_m_short_of_the_desert_march_portal_and_the_body_walks_it()
    {
        var drop = Routes.Edge("network-to-danbys");
        var portal = Routes.Edge(DenRunStation.RehearseBeforeEdge);
        var from = new CellFrame(drop.DestCell, new AcFrame(drop.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var (a, b) = (Global(drop.DestCell, drop.DestOrigin), Global(portal.Cell, portal.Origin));
        // the station's own rule, on the portal's row as the travel file gives it (cell 0x5B9C0000: the row stores cell 0 for an outdoor object)
        var point = TravelLeg.PointShortOf(Dats, from, new CellFrame(portal.Cell, new AcFrame(portal.Origin, Quaternion.Identity)), DenRunStation.RehearseShortMetres, out var why);
        Assert.True(point is not null, why);
        Assert.Equal(0u, portal.Cell & 0xFFFF);
        var end = Global(point!.Value.CellId, point.Value.Frame.Origin);
        Assert.Null(TravelLeg.PointShortOf(null, from, from, 25f, out var noDats));
        Assert.Equal("the dats are not at hand", noDats);
        Assert.Null(TravelLeg.PointShortOf(Dats, from, new CellFrame(0x5A9C0104, from.Frame), 25f, out var room));
        Assert.Contains("stands in a room", room, StringComparison.Ordinal);
        var flags = Flags(rig, a, end);
        var refused = Refused(rig, a, end);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run rehearsal: straight {Vector2.Distance(a, end):0.0} m, blocked {flags.Blocked} m, water {flags.Water} m, refused={refused ?? "none"}"));
        Assert.Equal(227.7, Vector2.Distance(a, end), 1);
        Assert.Equal(0, flags.Blocked);
        Assert.Null(refused);
        Assert.Equal(DenRunStation.RehearseShortMetres, Vector2.Distance(end, b), 1);
    }

    /// <summary>
    /// M6 item 14 (the lesson of Uziz, 2026-11-13: a portal no travel record names took the body on a walk): the Desert March dungeon holds seven portals beside the run's own Black Hill (the world copy's rows of landblock 0x02AA, read
    /// 2026-11-13: South Direlands 1107 in 0x0103, Southern Mt. Esper 8501 in 0x0119, Bandit Castle 1102 in 0x011A, Surface Portal 9004 in 0x0128 and 0x0231, Obsidian Rim 7210 in 0x014D, North Direlands 1104 in 0x0151). The run's way,
    /// planned with those seven kept off, is the same way: the same length and no leg within the clearance of one, so the plan the hop makes without them (its list is the travel records' portals) passes none of them either.
    /// </summary>
    [RequiresDatsFact]
    public void The_den_run_s_way_inside_the_desert_march_dungeon_passes_none_of_the_dungeons_other_portals()
    {
        const uint hub = 0x02AA0000;
        KeptPortal Kept(string name, float x, float y, float z) => new(name, new NavPoint(hub, new Vector3(x, y, z)).Global, z, false);
        var keep = new PortalKeepOff([
            Kept("South Direlands Portal", 210f, -60f, 0f), Kept("Southern Mt. Esper", 210f, -160f, 0f), Kept("Bandit Castle Portal", 220f, -30f, 0f), Kept("Surface Portal", 260f, 0f, 0f),
            Kept("Obsidian Rim Portal", 300f, -30f, 0f), Kept("North Direlands Portal", 310f, -60f, 0f), Kept("Surface Portal (arrival)", 50f, -520f, 30f),
        ]);
        var arrival = Routes.Edge("danbys-to-desert-march");
        var hop = Routes.Edge("desert-march-to-black-hill");
        var from = new CellFrame(arrival.DestCell, new AcFrame(arrival.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var portal = RungPortal.Of(hop, Routes);
        var (frontCell, front, _) = TravelLeg.FirstFront(portal, portal.Cell, from);
        (frontCell, front) = rig.FrontRoom(frontCell, front, out _);
        var walk = Walk(rig, from, rig.PointGoal(frontCell, front), keep);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run hub with the seven kept off: ok={walk.Ok} metres={walk.Metres:0.0} why={walk.Why ?? "none"}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.InRange(walk.Metres, 820, 836);
        Assert.Equal(DenRunHubRooms.Split(' ').Skip(1).Select(c => hub | uint.Parse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), walk.Cells.Distinct().Where(c => c != from.CellId));
    }

    private const string DenRunGround = "black-spawn-den-sho";

    /// <summary>A walk as <see cref="Walk"/> plans and confirms it, with the cells it enters.</summary>
    private static (bool Ok, double Metres, List<uint> Cells, CellFrame End, string? Why) Trace(StreamRig rig, CellFrame from, NavGoal goal)
    {
        rig.Streamer.EnsureNow(from.CellId);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var cells = new List<uint>();
        var at = from;
        var metres = 0.0;
        for (var plans = 0; plans < MaxHops; plans++)
        {
            var path = source.Plan(at, goal, new NavScars());
            if (path.Status is not (NavPathStatus.Found or NavPathStatus.Horizon) || rig.Confirm(path, goal) != RouteConfirmStatus.Confirmed)
            {
                return (false, metres, cells, at, $"{path.Status} {path.Reason} {rig.Confirmer.Failure}");
            }

            metres += Length(path);
            cells.AddRange(path.CellsEntered);
            at = path.Waypoints[^1].At;
            if (path.Status == NavPathStatus.Found)
            {
                return (true, metres, cells, at, null);
            }

            rig.Streamer.EnsureNow(at.CellId);
        }

        return (false, metres, cells, at, "no whole plan");
    }

    private static uint BlockOf(Vector2 global) => ((uint)(global.X / 192f) << 24) | ((uint)(global.Y / 192f) << 16);

    /// <summary>The tile flags under a straight line, a metre a sample: the metres of blocked (too steep, or under a static) and of water nodes, and where the first blocked node lies.</summary>
    private static (int Blocked, int Water, float FirstBlocked, float ZMin, float ZMax) Flags(StreamRig rig, Vector2 a, Vector2 b)
    {
        var length = Vector2.Distance(a, b);
        var (blocked, water, first, zMin, zMax) = (0, 0, float.NaN, float.MaxValue, float.MinValue);
        for (var d = 0f; d <= length; d += 1f)
        {
            var g = Vector2.Lerp(a, b, d / length);
            rig.Streamer.EnsureNow(BlockOf(g) | 1);
            Assert.True(rig.Map.TryGetNode(NavPoint.FromGlobal(g, 0), out var node), string.Create(CultureInfo.InvariantCulture, $"no tile under ({g.X:0}, {g.Y:0})"));
            (zMin, zMax) = (Math.Min(zMin, node.Z), Math.Max(zMax, node.Z));
            if ((node.Flags & NavFlags.Blocked) != 0)
            {
                blocked++;
                first = float.IsNaN(first) ? d : first;
            }
            else if ((node.Flags & NavFlags.Water) != 0)
            {
                water++;
            }
        }

        return (blocked, water, first, zMin, zMax);
    }

    /// <summary>The body's physics along a straight line, in pieces of 64 m (the route confirmer's probes, with the collision attached round each piece): null when every piece is confirmed, else where it is refused.</summary>
    private static string? Refused(StreamRig rig, Vector2 a, Vector2 b)
    {
        var pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 64f));
        for (var s = 0; s < pieces; s++)
        {
            var u = Vector2.Lerp(a, b, (float)s / pieces);
            var v = Vector2.Lerp(a, b, (float)(s + 1) / pieces);
            rig.Streamer.EnsureNow(BlockOf(Vector2.Lerp(u, v, 0.5f)) | 1);
            float Z(Vector2 g) => rig.Map.TryGetNode(NavPoint.FromGlobal(g, 0), out var node) ? node.Z : 0f;
            List<NavWaypoint> path = [new(NavMap.ToCellFrame(NavPoint.FromGlobal(u, Z(u))), false), new(NavMap.ToCellFrame(NavPoint.FromGlobal(v, Z(v))), false)];
            if (rig.Confirmer.ConfirmAll(path, 0f) != RouteConfirmStatus.Confirmed)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{s * Vector2.Distance(a, b) / pieces:0} m along: {rig.Confirmer.Failure}");
            }
        }

        return null;
    }

    private static Vector2 Global(uint cell, Vector3 at) => new((((cell >> 24) & 0xFF) * 192f) + at.X, (((cell >> 16) & 0xFF) * 192f) + at.Y);

    /// <summary>
    /// Inside the Desert March dungeon (landblock 0x02AA): from the arrival 0x02AA022F to the front of the Black Hill portal in 0x02AA0167 the room graph's way is 828 m over 102 rooms, down five storeys (z 30 to z 0), planned whole and
    /// confirmed by the physics; no door stands on it. (The pinned world SQL puts 21 creature rows in rooms of this way: <c>DenRunRouteSqlTests</c>.)
    /// </summary>
    [RequiresDatsFact]
    public void The_den_run_s_way_inside_the_desert_march_dungeon_is_828_m_over_102_rooms()
    {
        var arrival = Routes.Edge("danbys-to-desert-march");
        var hop = Routes.Edge("desert-march-to-black-hill");
        var from = new CellFrame(arrival.DestCell, new AcFrame(arrival.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var portal = RungPortal.Of(hop, Routes);
        var (frontCell, front, _) = TravelLeg.FirstFront(portal, portal.Cell, from);
        (frontCell, front) = rig.FrontRoom(frontCell, front, out var inNoRoom);
        Assert.False(inNoRoom);
        var walk = Trace(rig, from, rig.PointGoal(frontCell, front));
        var rooms = walk.Cells.Distinct().ToList();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run hub: ok={walk.Ok} metres={walk.Metres:0.0} rooms={rooms.Count} why={walk.Why}"));
        output.WriteLine("den-run hub rooms: " + string.Join(' ', rooms.Select(c => (c & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture))));
        Assert.True(walk.Ok, walk.Why);
        Assert.InRange(walk.Metres, 820, 836);
        Assert.Equal(DenRunHubRooms.Split(' ').Skip(1).Select(c => 0x02AA0000u | uint.Parse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), rooms.Where(c => c != from.CellId));
        Assert.Equal(428.0, Vector2.Distance(new Vector2(arrival.DestOrigin.X, arrival.DestOrigin.Y), new Vector2(hop.Origin.X, hop.Origin.Y)), 1);
    }

    /// <summary>The rooms of the way inside the Desert March dungeon, the arrival's first (the low words; <c>DenRunRouteSqlTests</c> counts the creature rows that stand in them).</summary>
    internal const string DenRunHubRooms =
        "022F 022E 022D 022C 022B 022A 0229 021C 021B 0214 0212 020B 0209 0208 0207 0206 020A 0211 0210 020F 020E 020D 020C 0213 021A 0219 0227 0226 0225 0224 0223 01BB 01BA 01B9 01B8 01B7 01B6 01B5 01B4 01B3 01B2 01BC 01C0 01C3 01CA 01D1 01D6 01D7 01D8 01DB 01DC "
        + "01E0 01E1 01E2 01E3 01E5 01E4 01EC 01EE 01F1 01FB 0200 0203 0204 0205 01A9 01AB 01AA 01AE 01AD 01AC 01A0 019F 019C 019D 019B 0199 0196 0197 0195 0193 0192 0191 0179 0142 0141 0140 0148 0147 014A 014C 014F 015E 015D 015C 015F 0161 0163 0162 0165 0166 0167";

    /// <summary>From Danby's Outpost's drop to the Desert March Portal: 252.7 m in a straight line, with no node too steep and about a hundred metres of shallow water the body wades (the tile flag Water: costed, not blocked).</summary>
    [RequiresDatsFact]
    public void The_den_run_s_walk_from_danbys_outpost_to_the_desert_march_portal_crosses_water_and_nothing_steep()
    {
        var drop = Routes.Edge("network-to-danbys");
        var portal = Routes.Edge("danbys-to-desert-march");
        var from = new CellFrame(drop.DestCell, new AcFrame(drop.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var (a, b) = (Global(drop.DestCell, drop.DestOrigin), Global(portal.Cell, portal.Origin));
        var flags = Flags(rig, a, b);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run danbys: straight {Vector2.Distance(a, b):0.0} m, blocked {flags.Blocked} m, water {flags.Water} m, z {flags.ZMin:0.0}..{flags.ZMax:0.0}"));
        Assert.Equal(252.7, Vector2.Distance(a, b), 1);
        Assert.Equal(0, flags.Blocked);
        Assert.InRange(flags.Water, 80, 110);
    }

    /// <summary>
    /// The run from the Black Hill drop to the den: the straight 1,395 m crosses ground too steep to stand on (blocked nodes from 694 m; the physics refuses it 692 m along) and no water; by the file's one bend both halves are straight
    /// lines with no blocked node and no water that the physics confirms, 1,443.6 m in all.
    /// </summary>
    [RequiresDatsFact]
    public void The_den_run_s_straight_line_is_too_steep_and_its_one_bend_makes_two_straight_halves_the_body_walks()
    {
        var hop = Routes.Edge("desert-march-to-black-hill");
        var den = GroundCatalog.Get(DenRunGround);
        var route = Routes.RoutesFor(DenRunGround).Single(r => r.Key == DenRunStation.RouteKey);
        var via = Assert.Single(route.Run!.Via!.Points);
        using var rig = Rig(new CellFrame(hop.DestCell, new AcFrame(hop.DestOrigin, Quaternion.Identity)));
        var (drop, bend, front) = (Global(hop.DestCell, hop.DestOrigin), Global(via.Cell, via.Origin), Global(den.Entry!.FrontCell, den.Entry.Front));

        var straight = Flags(rig, drop, front);
        var refused = Refused(rig, drop, front);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run straight: {Vector2.Distance(drop, front):0.0} m, blocked {straight.Blocked} m from {straight.FirstBlocked:0} m, water {straight.Water} m, z {straight.ZMin:0.0}..{straight.ZMax:0.0}; physics: {refused}"));
        Assert.InRange(straight.Blocked, 70, 100);
        Assert.InRange(straight.FirstBlocked, 690, 698);
        Assert.Equal(0, straight.Water);
        Assert.NotNull(refused);

        var first = Flags(rig, drop, bend);
        var second = Flags(rig, bend, front);
        var total = Vector2.Distance(drop, bend) + Vector2.Distance(bend, front);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run by the bend: {Vector2.Distance(drop, bend):0.0} + {Vector2.Distance(bend, front):0.0} = {total:0.0} m; blocked {first.Blocked}+{second.Blocked}, water {first.Water}+{second.Water}"));
        Assert.Equal((0, 0, 0, 0), (first.Blocked, second.Blocked, first.Water, second.Water));
        Assert.Null(Refused(rig, drop, bend));
        Assert.Null(Refused(rig, bend, front));
        Assert.Equal(1443.6, total, 0);
        rig.Streamer.EnsureNow(via.Cell);
        Assert.True(rig.Map.TryGetNode(new NavPoint(via.Cell & 0xFFFF0000u, via.Origin), out var node));
        Assert.Equal(via.Origin.Z, node.Z, 1);
        Assert.Equal(via.Cell, CellIds.LandCellAt(LandblockCoord.FromId(via.Cell), via.Origin.X, via.Origin.Y));
    }

    /// <summary>Out of the den: from the Surface Portal's drop (0x37340014) to the tie point 3 m in front of the entrance, 24 m, planned and confirmed.</summary>
    [RequiresDatsFact]
    public void The_den_run_s_exit_drop_walks_to_the_tie_point()
    {
        var den = GroundCatalog.Get(DenRunGround);
        var exitDrop = new CellFrame(den.Exit!.Destination, new AcFrame(new Vector3(56.3f, 91.1f, 87.3f), Quaternion.Identity)); // weenie_properties_position object_Id 7247 position_Type 2
        using var rig = Rig(exitDrop);
        var walk = Trace(rig, exitDrop, rig.PointGoal(den.Portal!.FrontCell, den.Portal.Front));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"den-run exit to tie point: ok={walk.Ok} metres={walk.Metres:0.0} why={walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.InRange(walk.Metres, 20, 30);
    }
}
