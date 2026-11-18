using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
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
/// M6 4 f3 on the retail dats: the proof the travel slices never wrote. For every portal edge of <c>Session/Travel/travel-routes.json</c>, from every place a body starts in its From place (each
/// arrival there: the destination of every edge into that place; Holtburg's lifestone, the file's <c>start</c>; the island's four exit drops), the walk the hop leg makes (<c>TravelLeg.HopThrough</c>):
/// to the front point exactly as <c>TravelLeg.FrontOf</c> computes it (the facing side; when that walk fails, the approach side from the start, the hop's one recovery <c>front-wall</c>), then the
/// seek within <see cref="DenTravelStation.PortalRangeMetres"/> of the portal (an object goal). Every plan comes from the journeys' own path source over the headless streamer's tiles (the start's
/// 3 x 3 first, the focus moved to each horizon's end) and is confirmed by the walker's physics (<see cref="RouteConfirmer"/>), as <c>GoalTileDatTests</c> does.
/// </summary>
/// <remarks>
/// Live (the lead's round 1, 2026-11-04, RungChangeLiveTests): both cases failed at the first hop, <c>holtburg-to-network</c>, both fronts <c>goal-blocked</c> at plan 1. The cause: the dais under the
/// portal, static 0x020019E3 at (14.54, 55.64) in 0xA9B40003, is one cylinder-sphere 2.6 m in radius and 0.26 m high, and the tile builder stamped every static's footprint a wall: both fronts 3 m
/// from the portal stood on it, and so did every node within the seek's 2 m. The physics walks up onto it (retail <c>CCylSphere::step_sphere_up</c>, 11.6096 0x0053C070); since this slice a static's
/// flat top within the step-up of the ground is a step, not a wall (<c>NavTileBuilder</c>, <see cref="NavBuildStats.StaticNodesStepped"/>). The island exits' drops are the pinned world SQL v0.9.296's
/// <c>weenie_properties_position</c> rows (PositionType 2) of the Surface portals 22662 (row id 1811), 22664 (1813), 22681 (1830) and 22689 (1838).
/// </remarks>
[Trait("Tier", "Exhaustive")]
public sealed partial class TownNetworkWalkDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The horizons a walk may take (the island's 1.3 km walks to the Refuge's portal take about eight).</summary>
    private const int MaxHops = 24;

    /// <summary>The island exits' drops (the pinned world SQL v0.9.296, weenie_properties_position PositionType 2 of the Surface portals; see the remarks).</summary>
    private static readonly (string Name, uint Cell, Vector3 At)[] IslandExits =
    [
        ("exit 22662", 0xF07F0039, new Vector3(177.99f, 20.043f, 12.005f)),
        ("exit 22664", 0xF9820039, new Vector3(181.6f, 2.6f, 12.005f)),
        ("exit 22681", 0xEA7B0001, new Vector3(9.452f, 23.534f, 54.044f)),
        ("exit 22689", 0xFA7F0006, new Vector3(3.2f, 121.8f, 45.9f)),
    ];

    /// <summary>
    /// The cases no walk reaches on this base, each with its TODO (M6 4 f3, for the lead; the cause is neither the front point nor the outdoor tile): the pin fails when the walk reaches, so a fix
    /// removes its row. M6 7 J closed <c>m6-facility-hub-rooms</c> (nine rows: every hop out of the Facility Hub's drop 0x8A020212 now walks over the floors its statics carry, <see cref="FacilityHubWalkDatTests"/>). <c>m6-neydisa-network-front</c>: from Neydisa's Network drop 0x95D60033 the outdoor plan finds no path to either front point of the castle's
    /// Portal to Town Network (73.93, 74.99) in 0x95D6001C.
    /// </summary>
    private static readonly Dictionary<(string Edge, string Start), string> Known = new()
    {
        [("neydisa-castle-to-network", "drop of network-to-neydisa")] = "TODO(m6-neydisa-network-front)",
        [("facility-hub-to-blackmire", "drop of network-to-facility-hub")] = "TODO(villa-blackmire-portal-ground)", // M6 villa: the Blackmire Temple portal 0x78A02082 stands where the confirmer's ground probe finds no walkable ground (0x8A020196 near (22.0, -50.1, 0.31))
    };

    private DatSet Dats => fixture.Dats;

    /// <summary>A case no walk reached: green only when it is pinned in <see cref="Known"/> with its TODO (logged), else the failure with its cause.</summary>
    private void Unreached(TravelEdge edge, (string Name, uint Cell, Vector3 At) start, string line)
    {
        output.WriteLine(line);
        Assert.True(Known.TryGetValue((edge.Key, start.Name), out var todo), line);
        output.WriteLine($"pinned: {todo}");
    }

    private static TravelRoutes Routes => TravelRoutes.Default;

    /// <summary>Every (edge, start) case: each portal edge of the file from each start of its From place.</summary>
    public static IEnumerable<object[]> Cases() =>
        Routes.Edges.SelectMany(e => StartsIn(e.From).Select(s => new object[] { e.Key, s.Name }));

    /// <summary>Every portal edge has a start to walk from (the cases cover the whole file).</summary>
    [Fact]
    public void Every_edge_of_the_travel_file_has_a_start_in_its_from_place()
    {
        Assert.Equal(56, Routes.Edges.Count); // M6 villa: the four of the minor stones' runs (Holtburg Wilderness Settlement, Blackmire Temple, Mayoi and the West Mayoi Mountain Villas); M6 item 14 (m6-goldscarab): Uziz's two (the town stock's stop at Archmage Sammam al-Ashiz); M6 item 14: the den run's three (Danby's Outpost, the Desert March Portal, the Black Hill portal); M6 5 q6b: the 28 and the four Shoushi and Lin edges; M6 8 B1: the seven of the chains to Crater Lake and to Yinar; M6 8 B2a: the Holtburg Redoubt's way in and out; M6 8 B2c: the Stone Cathedral's two, and Yaraq's four
        Assert.All(Routes.Edges, e => Assert.NotEmpty(StartsIn(e.From)));
        Assert.Contains(StartsIn("holtburg"), s => s.Cell == 0xA9B40019u);
        Assert.Contains(StartsIn("holtburg"), s => s.Name == "lifestone");
        Assert.Contains(StartsIn("town-network"), s => s.Cell == 0x00070143u);
        Assert.Contains(StartsIn("town-network"), s => s.Cell == 0x00070145u);
        Assert.Contains(StartsIn("facility-hub"), s => s.Cell == 0x8A020212u);
        Assert.Contains(StartsIn("island"), s => s.Cell == 0xF6820033u);
        Assert.Contains(StartsIn("town-network"), s => s.Cell == 0x00070156u); // the drop of the two towns' way back (M6 5 q6b)
        Assert.Contains(StartsIn("shoushi"), s => s.Cell == 0xDA55001Du);
        Assert.Contains(StartsIn("lin"), s => s.Cell == 0xDC3C0011u);
        Assert.Equal(4, StartsIn("island").Count(s => s.Name.StartsWith("exit ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The edge's hop from the start: the walk to its front point (facing, then approach) planned whole and confirmed, then the seek to within 2 m of the portal planned and confirmed. One line a case
    /// (<c>walk edge= from= front= metres= plan_ms= pass=</c>) is the doc notes' table.
    /// </summary>
    /// <param name="edgeKey">The edge.</param>
    /// <param name="startName">The start's name (<see cref="StartsIn"/>).</param>
    [RequiresDatsTheory]
    [MemberData(nameof(Cases))]
    public void The_hop_walks_from_the_start_to_the_front_point_and_on_to_the_portal(string edgeKey, string startName)
    {
        var edge = Routes.Edge(edgeKey);
        var start = StartsIn(edge.From).Single(s => s.Name == startName);
        var from = new CellFrame(start.Cell, new AcFrame(start.At, Quaternion.Identity));
        var portal = RungPortal.Of(edge, Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var near = from.CellId >> 16 == portalCell >> 16 && Vector2.Distance(new Vector2(from.Origin.X, from.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y)) <= TravelLeg.HopNearMetres;

        using var rig = Rig(from);
        from = rig.RealRoom(from); // M6 8 B2c: a portal's stored Destination cell can be stale (the Sea Temple's 0x0156018F holds no such point; its point stands in 0x01560192): the body stands in the room that holds it
        var clock = Stopwatch.StartNew();
        var metres = 0.0;
        var basis = "near";
        if (edge.WalkIn is { Count: > 0 } walkIn)
        {
            // M6 8 B1: the door's front by the walk; the way through the door and down the stair is the authored way (A_body_walks_the_way_in_to_the_crater_pathways_portal); the hop goes on from the way's end, inside the portal's room.
            var door = Walk(rig, from, rig.PointGoal(PortalCell(walkIn[0].Cell, walkIn[0].Origin), walkIn[0].Origin));
            Assert.True(door.Ok, Line(edge, start, "walk-in", door.Metres, clock, door.Ok) + " the walk to the door's front failed: " + door.Why);
            metres += door.Metres;
            from = rig.RealRoom(new CellFrame(walkIn[^1].Cell, new AcFrame(walkIn[^1].Origin, Quaternion.Identity))); // M6 8 B2c: the Cathedral's last point is named in the door hall and stands in the portal's room
            rig.Streamer.EnsureNow(from.CellId);
            near = Vector2.Distance(new Vector2(from.Origin.X, from.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y)) <= TravelLeg.HopNearMetres;
            basis = "walk-in";
        }

        var at = from;
        string? facingWhy = null;
        if (!near)
        {
            var (frontCell, front, frontBasis) = TravelLeg.FrontOf(portal, portalCell, from);
            (frontCell, front) = rig.FrontRoom(frontCell, front, out var inNoRoom);
            basis = frontBasis;
            var walk = inNoRoom && frontBasis == TravelLeg.FrontBasisFacing ? new WalkResult(false, 0, from, "front-in-no-room") : Walk(rig, from, rig.PointGoal(frontCell, front));
            if (!walk.Ok && frontBasis == TravelLeg.FrontBasisFacing)
            {
                // The hop's one recovery (front-wall): the approach side's point, from where the body stands (the start: the failed walk did not move it).
                facingWhy = walk.Why;
                var (approachCell, approach, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, from);
                (approachCell, approach) = rig.FrontRoom(approachCell, approach, out _);
                basis = TravelLeg.FrontBasisApproach + "(front-wall)";
                walk = Walk(rig, from, rig.PointGoal(approachCell, approach));
            }

            metres += walk.Metres;
            if (!walk.Ok)
            {
                Unreached(edge, start, Line(edge, start, basis, metres, clock, false) + $" the walk to the front failed: facing: {facingWhy}; {basis}: {walk.Why}");
                return;
            }

            at = walk.End;
        }

        // The seek (an object goal within the portal's 2 m; the portal object is not in the rig's view, so the point is the row's).
        var seek = Walk(rig, at, rig.ObjectGoal(portal.Guid == 0 ? 1u : portal.Guid, portalCell, portal.Origin, DenTravelStation.PortalRangeMetres));
        metres += seek.Metres;
        var line = Line(edge, start, basis, metres, clock, seek.Ok);
        output.WriteLine(line + (facingWhy is null ? string.Empty : $" facing_failed=\"{facingWhy}\""));
        if (!seek.Ok)
        {
            Unreached(edge, start, line + " the seek failed: " + seek.Why);
            return;
        }

        Assert.False(Known.ContainsKey((edge.Key, start.Name)), $"{line}: reached, so the pin {Known.GetValueOrDefault((edge.Key, start.Name))} is stale: remove it from Known");
        var end = seek.End;
        Assert.Equal(portalCell >> 16, end.CellId >> 16);
        Assert.True(Vector2.Distance(new Vector2(end.Origin.X, end.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y)) <= DenTravelStation.PortalRangeMetres + 0.5, $"the seek ended at {end}");
    }

    /// <summary>
    /// The cause, pinned: Holtburg's Network portal stands on the dais 0x020019E3 (one cylinder-sphere 2.6 m in radius, 0.26 m high, the portal at its middle), and the nodes of both front points and
    /// the portal's own node are open: a static's flat top within the step-up is a step (M6 4 f3). The physics finds the dais's top under the portal.
    /// </summary>
    [RequiresDatsFact]
    public void Holtburgs_Network_portal_stands_on_a_dais_the_tile_leaves_open_as_a_step()
    {
        var edge = Routes.Edge("holtburg-to-network");
        var arrival = new CellFrame(0xA9B40019, new AcFrame(new Vector3(84f, 7.1f, 94.01f), Quaternion.Identity));
        using var rig = Rig(arrival);
        var block = rig.World.GetLandblock(LandblockCoord.FromId(0xA9B40000))!;
        var dais = block.StaticObjects.Single(s => s.ModelId == 0x020019E3 && Vector2.Distance(new Vector2(s.Position.Frame.Origin.X, s.Position.Frame.Origin.Y), new Vector2(edge.Origin.X, edge.Origin.Y)) < 1f);
        var cyl = Assert.Single(dais.CylSpheres);
        Assert.InRange(cyl.Radius * dais.Scale, 2.5f, 2.7f);
        Assert.InRange(cyl.Height * dais.Scale, 0.2f, NavMap.StepUpMetres);

        Assert.True(rig.Map.TryGetTile(0xA9B40000, out var tile));
        var portal = RungPortal.Of(edge, Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        var (_, facing, _) = TravelLeg.FrontOf(portal, portalCell, arrival);
        var (_, approach, _) = TravelLeg.FrontOf(portal with { Facing = null }, portalCell, arrival);
        foreach (var p in new[] { facing, approach, edge.Origin })
        {
            var (x, y) = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            Assert.True(tile.IsWalkable(x, y), $"node ({x}, {y}) is {tile.Flags[NavTile.Index(x, y)]}");
        }

        Assert.True(rig.World.TryFixedGroundZ(new CellFrame(portalCell, new AcFrame(edge.Origin, Quaternion.Identity)), rig.Foot, out var z, out _));
        var daisTop = dais.Position.Frame.Origin.Z + (cyl.Height * dais.Scale);
        Assert.InRange(z, daisTop - 0.05f, daisTop + 0.05f);
    }

    /// <summary>
    /// M6 5 q6b (D55): the quest givers' walks, planned and confirmed as a hop's seek is. San Ming (the Blood Shreth Kill Task) stands 11.2 m from Shoushi's arrival 0xDA55001D (landblock_instance guid
    /// 0x7DA5504B, cell 0xDA55001D, (73.58, 98.23, 20.0)); Shoichi (the Tusker Guard Kill Task) stands indoors (guid 0x7DA3B03A, cell 0xDA3B0116, (110.6, 77.9, 6.8)), 355.8 m from Lin's arrival 0xDC3C0011.
    /// One line a walk (<c>quest-walk name= metres= pass=</c>); a walk no plan reaches is green only when it is pinned in <see cref="QuestKnown"/> with its TODO.
    /// </summary>
    /// <param name="name">The walk's name.</param>
    [RequiresDatsTheory]
    [InlineData("san-ming")]
    [InlineData("shoichi")]
    public void A_quest_giver_is_reached_from_his_towns_arrival(string name)
    {
        var (arrival, giver) = name == "san-ming"
            ? (new CellFrame(0xDA55001D, new AcFrame(new Vector3(84.8f, 99f, 20f), Quaternion.Identity)), (Guid: 0x7DA5504Bu, Cell: 0xDA55001Du, At: new Vector3(73.5787f, 98.2255f, 20.005f)))
            : (new CellFrame(0xDC3C0011, new AcFrame(new Vector3(59.72f, 10.774f, 18.958f), Quaternion.Identity)), (Guid: 0x7DA3B03Au, Cell: 0xDA3B0116u, At: new Vector3(110.636f, 77.923f, 6.805f)));
        using var rig = Rig(arrival);
        var clock = Stopwatch.StartNew();
        var cell = PortalCell(giver.Cell, giver.At);
        var walk = Walk(rig, arrival, rig.ObjectGoal(giver.Guid, cell, giver.At, PathwardenRun.UseRange));
        var line = string.Create(CultureInfo.InvariantCulture, $"quest-walk name={name} from=0x{arrival.CellId:X8} to=0x{cell:X8} metres={walk.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(walk.Ok ? "yes" : "no")}");
        output.WriteLine(line);
        if (!walk.Ok)
        {
            output.WriteLine($"the walk failed: {walk.Why}");
            Assert.True(QuestKnown.TryGetValue(name, out var todo), line + " " + walk.Why);
            output.WriteLine($"pinned: {todo}");
            return;
        }

        Assert.False(QuestKnown.ContainsKey(name), $"{line}: reached, so the pin {QuestKnown.GetValueOrDefault(name)} is stale: remove it from QuestKnown");
        Assert.True(Vector2.Distance(new Vector2(walk.End.Origin.X, walk.End.Origin.Y), new Vector2(giver.At.X, giver.At.Y)) <= PathwardenRun.UseRange + 0.5, $"the walk ended at {walk.End}");
    }

    /// <summary>
    /// The quest walks no plan reaches on this base, each with its TODO and the cause named in the test's output (M6 5 q6b); the pin fails when the walk reaches, so a fix removes its row.
    /// M6 5 w: <c>alabree</c> now plans whole (the cave's leaning mouth is a doorway at the buildings' lean, the holes over its ramp are a stair: <c>AlabreeEntryCaveDatTests</c>), 277 m of
    /// horizons to the mouth, and a body walks down the ramp into the portal room, but the confirmer refuses the ramp: the physics' ground probe (<c>PhysicsWorld.TryFixedGroundZ</c>, retail's
    /// step down) misses the ramp from most start heights where the foot sphere starts over the two stacked holes (x 101.5 to 102.3 at y 135.3), and the placement at (101.5, 135.3, 29.6)
    /// is NoValidPosition (the physics port, not the planner: <c>TODO(m6-ramp-step-down)</c>, for the lead). The world walk's W8 closed it: where the foot meets a wall at every height, the confirmer
    /// steps the body there from where it last stood and takes the ground it lands on (<c>RouteConfirmer.TryBodyStep</c>), so the run reaches the front point and its pin is gone. M6 14 guard task: <c>shoichi</c>
    /// is reached (its pin is gone): his building's street door 0xDA3B0109 (sill z 8.00 at x 117.5) opens over the entry 0xDA3B0118 (floor z 6.80), and the room graph refused it as a pit edge; the three steps down
    /// are the room's statics, which the doorway rule now reads (<c>CellRoutePlanner</c>'s steps down past a doorway, <see cref="Shoichis_door_opens_over_three_static_steps_the_doorway_rule_walks"/>).
    /// Both pins are gone, so the table is empty; a quest walk that stops reaching adds its row back with its TODO.
    /// </summary>
    private static readonly Dictionary<string, string> QuestKnown = new()
    {
    };

    /// <summary>Shoichi's house in Lin (landblock 0xDA3B): the street door's cell, the entry past it and the room he stands in (the pinned world SQL v0.9.296: guid 0x7DA3B03A in 0xDA3B0116 at (110.636, 77.923, 6.805)).</summary>
    private const uint ShoichiDoorCell = 0xDA3B0109;

    private const uint ShoichiEntryCell = 0xDA3B0118;

    private static readonly (uint Guid, uint Cell, Vector3 At) Shoichi = (0x7DA3B03Au, 0xDA3B0116u, new Vector3(110.636f, 77.923f, 6.805f));

    /// <summary>
    /// M6 14 guard task, the cause of <c>TODO(m6-lin-shoichi-walk)</c> on the dats: the street door 0xDA3B0109's doorway into the entry 0xDA3B0118 has its sill at z 8.00 and the entry's floor polygons lie at 6.80, 1.2 m down (more than the
    /// step-up), so the floor polygons alone call it a pit edge; three static steps of the entry (0x01000ACD at z 7.60, 0x01000AC5 at 7.20 and 0x01000ACA at 6.80, 0.5 m apart westward on y 84) carry a floor from the sill down to it. The
    /// planner now walks the doorway both ways, and a body's descent from the sill's lip follows the steps to the entry's floor.
    /// </summary>
    [RequiresDatsFact]
    public void Shoichis_door_opens_over_three_static_steps_the_doorway_rule_walks()
    {
        var cells = LandblockCellSet.Load(new CellStructureLibrary(Dats), new LandblockCoord(0xDA, 0x3B));
        var planner = new CellRoutePlanner(cells);
        var door = cells.GetCell(ShoichiDoorCell)!;
        var entry = cells.GetCell(ShoichiEntryCell)!;
        var portal = door.Portals.Single(p => p.OtherCellId == ShoichiEntryCell);
        var doorway = planner.DoorwayOf(door, portal)!.Value;
        Assert.Equal(8.00f, doorway.Sill, 2);
        Assert.Equal(6.80f, planner.RoomFloorUnder(ShoichiEntryCell, new Vector3(116.5f, 84f, 8.5f))!.Value, 2);
        var steps = entry.StaticObjects.Where(s => s.ModelId is 0x01000ACD or 0x01000AC5 or 0x01000ACA).OrderByDescending(s => s.Frame.Origin.Z).Select(s => s.Frame.Origin).ToList();
        Assert.Equal([7.60f, 7.20f, 6.80f], steps.Select(s => MathF.Round(s.Z, 2)));
        Assert.All(steps, s => Assert.Equal(84f, s.Y, 1));
        Assert.True(planner.IsDoorway(door, portal), "the street door into the entry is a doorway: its steps carry a floor from the sill down");
        Assert.True(planner.IsWalkable(door, portal));
        var back = entry.Portals.Single(p => p.OtherCellId == ShoichiDoorCell);
        Assert.True(planner.IsWalkable(entry, back), "and back up the steps to the street door");
        Assert.NotNull(planner.FindPath(ShoichiDoorCell, Shoichi.Cell));
        Assert.NotNull(planner.FindPath(Shoichi.Cell, ShoichiDoorCell));
    }

    /// <summary>
    /// M6 14 guard task (<c>TODO(m6-lin-shoichi-walk)</c>, the way back): from where Shoichi stands, the hop leg's walk to the front of Lin's Portal to Town Network (edge <c>lin-to-network</c>, guid 0x7DA3B039 in 0xDA3B003B) and the seek
    /// within 2 m of it, planned whole over the headless streamer's tiles and the house's rooms and confirmed by the walker's physics: up the steps, out of the street door and east along the street.
    /// </summary>
    [RequiresDatsFact]
    public void From_shoichi_the_walk_back_reaches_lins_portal_to_the_town_network()
    {
        var edge = Routes.Edge("lin-to-network");
        var from = new CellFrame(Shoichi.Cell, new AcFrame(Shoichi.At + new Vector3(1.5f, 1.5f, 0f), Quaternion.Identity)); // where the talk leaves her: within his use range
        var portal = RungPortal.Of(edge, Routes);
        var portalCell = PortalCell(portal.Cell, portal.Origin);
        using var rig = Rig(from);
        var clock = Stopwatch.StartNew();
        var (frontCell, front, basis) = TravelLeg.FrontOf(portal, portalCell, from);
        (frontCell, front) = rig.FrontRoom(frontCell, front, out _);
        var walk = Walk(rig, from, rig.PointGoal(frontCell, front));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=shoichi-back front={basis} metres={walk.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(walk.Ok ? "yes" : "no")} cells={string.Join(' ', walk.Cells.Distinct().Where(c => !LandDefs.IsOutdoorCell(c)).Select(c => c.ToString("X8", CultureInfo.InvariantCulture)))} {walk.Why}"));
        Assert.True(walk.Ok, "the walk to the portal's front: " + walk.Why);
        Assert.Contains(ShoichiEntryCell, walk.Cells);
        Assert.Contains(ShoichiDoorCell, walk.Cells);
        var seek = Walk(rig, walk.End, rig.ObjectGoal(portal.Guid, portalCell, portal.Origin, DenTravelStation.PortalRangeMetres));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=shoichi-back-seek metres={seek.Metres:0.0} pass={(seek.Ok ? "yes" : "no")} {seek.Why}"));
        Assert.True(seek.Ok, "the seek to the portal: " + seek.Why);
        Assert.True(Vector2.Distance(new Vector2(seek.End.Origin.X, seek.End.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y)) <= DenTravelStation.PortalRangeMetres + 0.5, $"the seek ended at {seek.End}");
    }

    /// <summary>
    /// M6 5 q6b (D55): the run of the Cave of Alabree's route, from Holtburg's lifestone (the start place's start) to the entry portal 4937's front point (the profile's <c>entry.front_point</c>, 3 m along the portal's
    /// facing) and the seek to the portal within 2 m, two landblocks west and one south (354.5 m straight). The walk is planned in the cells' order the rung change plans it (the front point's walk, then the seek).
    /// </summary>
    [RequiresDatsFact]
    public void The_cave_of_alabrees_entry_is_reached_from_holtburgs_lifestone()
    {
        var run = Assert.Single(Routes.RoutesFor("cave-of-alabree")).Run!;
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("cave-of-alabree");
        var entry = ground.Entry!;
        Assert.Equal((run.EntryGuid, run.EntryCell), (entry.Guid, entry.Cell));
        var lifestone = Routes.StartLifestone;
        var from = new CellFrame(PortalCell(lifestone.Cell, lifestone.Origin), new AcFrame(lifestone.Origin, Quaternion.Identity));
        using var rig = Rig(from);
        var clock = Stopwatch.StartNew();
        var (frontCell, front) = rig.FrontRoom(PortalCell(entry.FrontCell, entry.Front), entry.Front, out var inNoRoom); // the hop leg's own step: the room that holds the front point
        output.WriteLine($"quest-walk name=alabree-front-room cell=0x{frontCell:X8} in_no_room={inNoRoom}");
        var toFront = Walk(rig, from, rig.PointGoal(frontCell, front));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=alabree-front from=0x{from.CellId:X8} to=0x{frontCell:X8} metres={toFront.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(toFront.Ok ? "yes" : "no")} {toFront.Why}"));
        if (!toFront.Ok)
        {
            Assert.True(QuestKnown.TryGetValue("alabree", out var todo), "the run to the entry's front point: " + toFront.Why);
            output.WriteLine($"pinned: {todo}");
            return;
        }

        Assert.False(QuestKnown.ContainsKey("alabree"), "reached, so the pin is stale: remove it from QuestKnown");
        var seek = Walk(rig, toFront.End, rig.ObjectGoal(entry.Guid, PortalCell(entry.Cell, entry.Origin), entry.Origin, DenTravelStation.PortalRangeMetres));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=alabree-seek metres={seek.Metres:0.0} pass={(seek.Ok ? "yes" : "no")} {seek.Why}"));
        Assert.True(seek.Ok, "the seek to the entry: " + seek.Why);
        Assert.Equal(entry.Cell >> 16, seek.End.CellId >> 16);
    }

    /// <summary>
    /// M6 5 w (<c>m6-alabree-way-home</c>): the cave's exit 4938 "Surface" drops at 0xA7B3001E (92.3, 136.1, 30.0) (the pinned world SQL v0.9.296, weenie_properties_position id 390,
    /// object 4938, PositionType 2), outside the cave's mouth; the way home is the run from there to Holtburg's lifestone point (the profile's <c>home.lifestone_point</c>, 0xA9B40019
    /// (84, 7.7, 94)), planned whole over the streamer's tiles and confirmed. The travel file's ways home are portal hops only, so this run is not yet data (the lead's).
    /// </summary>
    [RequiresDatsFact]
    public void The_cave_of_alabrees_surface_exit_runs_home_to_holtburgs_lifestone()
    {
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("cave-of-alabree");
        var exit = ground.Exit!;
        Assert.Equal(0xA7B3001Eu, exit.Destination);
        var from = new CellFrame(exit.Destination, new AcFrame(new Vector3(92.3f, 136.1f, 30f), Quaternion.Identity));
        using var rig = Rig(from);
        var clock = Stopwatch.StartNew();
        var home = ground.Home;
        var walk = Walk(rig, from, rig.PointGoal(PortalCell(home.LifestoneCell, home.LifestonePoint), home.LifestonePoint));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"way-home-walk name=alabree-surface from=0x{from.CellId:X8} to=0x{home.LifestoneCell:X8} metres={walk.Metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.Equal(home.LifestoneCell >> 16, walk.End.CellId >> 16);
    }

    /// <summary>The starts of a place: the destination of every edge into it, the file's <c>start</c> (Holtburg's lifestone) and the island's exit drops.</summary>
    private static List<(string Name, uint Cell, Vector3 At)> StartsIn(string place)
    {
        var starts = Routes.Edges.Where(e => e.To == place).GroupBy(e => e.DestCell).Select(g => ("drop of " + g.First().Key, g.First().WalkOut is { Count: > 0 } o ? PortalCell(o[^1].Cell, o[^1].Origin) is var c && LandDefs.IsOutdoorCell(c) ? c : CellIds.LandCellAt(LandblockCoord.FromId(o[^1].Cell), o[^1].Origin.X, o[^1].Origin.Y) ?? o[^1].Cell : g.Key, g.First().WalkOut is { Count: > 0 } w ? w[^1].Origin : g.First().DestOrigin)).ToList(); // M6 8 B1: an edge with a walk out starts its next hop where the walk ended
        if (Routes.StartPlace == place)
        {
            starts.Add(("lifestone", PortalCell(Routes.StartLifestone.Cell, Routes.StartLifestone.Origin), Routes.StartLifestone.Origin));
        }

        if (place == "island")
        {
            starts.AddRange(IslandExits);
        }

        return starts;
    }

    /// <summary>The hop leg's cell of a row (<c>TravelLeg.CellAt</c>: the land cell under an outdoor row's point).</summary>
    private static uint PortalCell(uint cell, Vector3 at) =>
        (cell & 0xFFFF) < 0x100 ? CellIds.LandCellAt(LandblockCoord.FromId(cell), Math.Clamp(at.X, 0.01f, 191.99f), Math.Clamp(at.Y, 0.01f, 191.99f)) ?? cell : cell;

    private static string Line(TravelEdge edge, (string Name, uint Cell, Vector3 At) start, string basis, double metres, Stopwatch clock, bool pass) =>
        string.Create(CultureInfo.InvariantCulture, $"walk edge={edge.Key} from=\"{start.Name}\" (0x{start.Cell:X8}) front={basis} metres={metres:0.0} plan_ms={clock.Elapsed.TotalMilliseconds:0} pass={(pass ? "yes" : "no")}");

    /// <summary>
    /// A walk as the journey's leg plans it and its runner answers a refusal: the plan, and while it is a horizon (confirmed) the streamer's focus moved to its end and the plan again; a plan the
    /// physics refuses is planned again (at most <see cref="NavRunner.MaxConfirmFailures"/> refusals in a row) without the room the refused sample was on its way into (the runner's <c>AvoidCellOf</c>)
    /// or, outdoors, with a scar of <see cref="NavRunner.StuckScarMetres"/> at the refused sample; the last plan whole and confirmed.
    /// </summary>
    private static WalkResult Walk(StreamRig rig, CellFrame from, NavGoal goal, PortalKeepOff? keepOff = null)
    {
        rig.Streamer.EnsureNow(from.CellId);
        var source = rig.Planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), rig.Map);
        var scars = new NavScars();
        if (keepOff is not null)
        {
            // M6 item 14 (m6-vendor-portal-keep-off): as NavRunner gives a run's list to its plans: the outdoor discs to the lattice, the list to the room graph
            scars.SetKeepOff(keepOff.OutdoorDiscs);
            if (source is INavRoomSource withRooms)
            {
                withRooms.Rooms.KeepOff = keepOff;
            }
        }

        var at = from;
        var metres = 0.0;
        var refusals = 0;
        var notes = new List<string>();
        var cells = new List<uint>();
        for (var plans = 0; plans < MaxHops; plans++)
        {
            var path = source.Plan(at, goal, scars);
            if (path.Status is not (NavPathStatus.Found or NavPathStatus.Horizon))
            {
                return new WalkResult(false, metres, at, $"{path.Status} {path.Reason}{Notes(notes)}");
            }

            if (keepOff?.FirstMeeting(path.Waypoints) is { } met)
            {
                return new WalkResult(false, metres, at, $"portal-keep-off: leg {met.Leg} of the plan passes within {keepOff.Clearance:0.00} m of {met.Portal.Name}{Notes(notes)}");
            }

            if (rig.Confirm(path, goal) != RouteConfirmStatus.Confirmed)
            {
                var failure = rig.Confirmer.Failure!.Value;
                notes.Add($"refused: {failure}; cells {string.Join(' ', path.CellsEntered.Select(c => c.ToString("X8", CultureInfo.InvariantCulture)))}");
                if (++refusals >= NavRunner.MaxConfirmFailures)
                {
                    return new WalkResult(false, metres, at, $"the physics refused the path {refusals} times{Notes(notes)}");
                }

                if (NavRunner.AvoidCellOf(path.Waypoints, failure, goal) is { } cell && source is INavCellAvoider avoider && avoider.Avoid(cell))
                {
                    notes.Add($"room 0x{cell:X8} left out");
                }
                else if (LandDefs.IsOutdoorCell(failure.At.CellId))
                {
                    scars.Add(NavMap.FromCellFrame(failure.At), NavRunner.StuckScarMetres);
                }

                continue;
            }

            refusals = 0;
            metres += Length(path);
            cells.AddRange(path.CellsEntered);
            if (path.Status == NavPathStatus.Found)
            {
                return new WalkResult(true, metres, path.Waypoints[^1].At, null) { Cells = cells };
            }

            at = path.Waypoints[^1].At;
            rig.Streamer.EnsureNow(at.CellId);
        }

        return new WalkResult(false, metres, at, $"no whole plan in {MaxHops} plans{Notes(notes)}");
    }

    private static string Notes(List<string> notes) => notes.Count == 0 ? string.Empty : " (" + string.Join("; ", notes) + ")";

    private static double Length(NavPath path) => path.Waypoints.Zip(path.Waypoints.Skip(1)).Sum(p => Vector2.Distance(p.First.Point.Global, p.Second.Point.Global));

    private StreamRig Rig(CellFrame start)
    {
        var world = new PhysicsWorld();
        var map = new NavMap();
        var streamer = new HeadlessStreamer(Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(start.CellId);
        world.UseTime(0.0);
        var walker = OfflineWalker.Create(Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        return new StreamRig(new JourneyLegPlanner(Dats), new J1JourneyRig(), map, streamer, new RouteConfirmer(world, walker.Physics.Setup!), world, walker.Physics.Setup!.Spheres[0]);
    }

    private sealed record WalkResult(bool Ok, double Metres, CellFrame End, string? Why)
    {
        /// <summary>The rooms the confirmed plans of a whole walk entered, in order (empty for a walk that failed).</summary>
        public IReadOnlyList<uint> Cells { get; init; } = [];
    }

    /// <summary>The journeys' leg planner over the headless streamer's tiles and collision, with the walker's confirmer on the human body (as <c>GoalTileDatTests</c>'s).</summary>
    private sealed class StreamRig(JourneyLegPlanner planner, J1JourneyRig view, NavMap map, HeadlessStreamer streamer, RouteConfirmer confirmer, PhysicsWorld world, PhysSphere foot) : IDisposable
    {
        public PhysicsWorld World { get; } = world;

        public PhysSphere Foot { get; } = foot;

        public JourneyLegPlanner Planner { get; } = planner;

        public NavMap Map { get; } = map;

        public HeadlessStreamer Streamer { get; } = streamer;

        public RouteConfirmer Confirmer { get; } = confirmer;

        /// <summary>The hop leg's room of an indoor front point (<c>TravelLeg.FrontRoomIn</c> over the journeys' cells of its landblock).</summary>
        public (uint Cell, Vector3 At) FrontRoom(uint cell, Vector3 at, out bool inNoRoom)
        {
            inNoRoom = false;
            if (LandDefs.IsOutdoorCell(cell))
            {
                return (cell, at);
            }

            var room = TravelLeg.FrontRoomIn(Planner.CellsOf(cell), cell, at);
            inNoRoom = room is null;
            return room is { } r ? (r.Cell, r.At) : (cell, at);
        }

        /// <summary>M6 8 B2c: the room that truly holds the frame's point (its middle 0.5 m over it): an env cell's frame moved to it, the frame unchanged when none does or the cell is outdoors.</summary>
        public CellFrame RealRoom(CellFrame frame)
        {
            if (LandDefs.IsOutdoorCell(frame.CellId))
            {
                return frame;
            }

            var room = Planner.CellsOf(frame.CellId).FindCell(frame.Frame.Origin + new Vector3(0f, 0f, 0.5f), frame.CellId);
            return room is null || room.Id == frame.CellId ? frame : new CellFrame(room.Id, frame.Frame);
        }

        /// <summary>The hop's walk to its front point (<c>WalkToPoint</c>, <see cref="TravelPurpose.Cell"/>, no range).</summary>
        public NavGoal PointGoal(uint cell, Vector3 point) => Planner.GoalOf(view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Cell));

        /// <summary>M6 item 14 (m6-goldscarab): the shop step's walk to a vendor not seen yet (<c>ResupplyRun.RoomRequest</c>): his own cell and point with <paramref name="range"/> as the stop.</summary>
        public NavGoal VendorGoal(uint cell, Vector3 point, double range) => Planner.GoalOf(view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Vendor, Range: range));

        /// <summary>The hop's seek: the portal by its guid within <paramref name="range"/>.</summary>
        public NavGoal ObjectGoal(uint guid, uint cell, Vector3 point, double range) => Planner.GoalOf(view.View, new TravelRequest(Destination(cell, point), TravelPurpose.Seek, guid, range));

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

        private static TeleportDestination Destination(uint cell, Vector3 point) =>
            new("hop", [], new WorldLocation("hop", new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), "walk test");
    }
}
