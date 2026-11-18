using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Tests.Movement;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Cells;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// What the loops ask of a hunting ground (M5 item 2 g2b, D46): the targets, the walkable and no-go cells, the arrival destination, the room graph and the walk's stop, all read through the profile. These are the former
/// <c>BlackSpawnDenTests</c> (the den's floor set, its exclusions, <c>LeftFloor</c> and <c>IsTarget</c>, deleted with <c>BlackSpawnDen</c>) turned into the ground's: the floor and exclusion literals are pinned in
/// <c>HuntingGroundTests</c>, the recomputation from the dats in <c>DenGroundFloorDatTests</c>, and the room graph's walkable set in <c>DenRoomGraphTests</c>.
/// </summary>
public sealed class GroundRulesTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int FighterRunSkill = 230;

    private static readonly uint[] SpawnCells = [0x02E1, 0x02E2, 0x0303, 0x030D, 0x0310, 0x0320, 0x0325, 0x0338, 0x033F, 0x036F, 0x0377, 0x0383];

    [Fact]
    public void The_arrival_and_the_twelve_spawn_cells_are_walkable()
    {
        var den = DenGround.Profile;
        Assert.True(den.IsWalkable(0x01050375));
        foreach (var low in SpawnCells)
        {
            Assert.True(den.IsWalkable(0x01050000 | low), $"0x{low:X4}");
        }
    }

    [Fact]
    public void The_exits_the_outliers_and_the_wing_are_no_go_and_never_walkable()
    {
        var den = DenGround.Profile;
        foreach (var low in new uint[] { 0x0244, 0x02D3, 0x02D5, 0x02D6, 0x0341, 0x0342, 0x0343, 0x035C, 0x010D })
        {
            Assert.True(den.IsNoGo(0x01050000 | low), $"0x{low:X4}");
            Assert.False(den.IsWalkable(0x01050000 | low), $"0x{low:X4}");
        }
    }

    [Fact]
    public void Another_landblock_or_an_unlisted_cell_is_neither_walkable_nor_no_go()
    {
        var den = DenGround.Profile;
        foreach (var cell in new uint[] { 0x01040375, 0x01060375, 0x0375, 0, 0xA9B40019 })
        {
            Assert.False(den.IsWalkable(cell), $"0x{cell:X8}");
            Assert.False(den.IsNoGo(cell), $"0x{cell:X8}");
            Assert.False(den.InLandblocks(cell) && cell >> 16 != DenGround.Landblock);
        }

        Assert.True(den.InLandblocks(0x01050375));
        Assert.True(den.InLandblocks(0x0105FFFF)); // beyond the cell masks, still the ground's landblock
        Assert.False(den.IsNoGo(0x0105FFFF)); // D4: only entering a no-go cell stops a walk; a cell no storey holds is not one
    }

    [Fact]
    public void Only_the_grounds_target_creatures_in_walkable_cells_are_targets()
    {
        var den = DenGround.Profile;
        Assert.True(den.IsTarget(1628, 0x01050303));
        Assert.True(den.IsTarget(1629, 0x01050375));

        // The avoided creatures never pass, wherever they stand; an unlisted wcid neither.
        foreach (var wcid in new uint[] { 23, 237, 1609, 1610, 3955 })
        {
            foreach (var low in SpawnCells)
            {
                Assert.False(den.IsTarget(wcid, 0x01050000 | low), $"wcid {wcid} 0x{low:X4}");
            }

            Assert.False(den.IsTarget(wcid, 0x01050154));
        }

        // A tusker in the outliers' cells or the exit's room is not a target.
        Assert.False(den.IsTarget(1628, 0x010502D6));
        Assert.False(den.IsTarget(1629, 0x010502D6));
        Assert.False(den.IsTarget(1629, 0x0105010D));
        Assert.False(den.IsTarget(1628, 0x01050341));
    }

    [Fact]
    public void The_teleport_destination_is_the_profiles_arrival_not_the_places_row()
    {
        var den = DenGround.Profile;
        var destination = den.ArrivalDestination();
        Assert.Equal(den.Arrival.Cell, destination.Location.Position.CellId);
        Assert.Equal(den.Arrival.Origin, destination.Location.Position.Local);
        Assert.Contains(den.Key, destination.Source, StringComparison.Ordinal);

        // For the den the two coincide (the profile's arrival is the portal's destination, the Places row the same cell) ...
        var row = InteriorLocations.BlackSpawnDenAluvian;
        Assert.Equal(row.CellId, destination.Location.Position.CellId);
        Assert.Equal(row.Location.Position.Local, destination.Location.Position.Local);
        Assert.Equal(row.Location.HeadingDegrees, destination.Location.HeadingDegrees, 3);

        // ... but it is the profile's: another arrival in the profile is another destination, with no Places row involved.
        var elsewhere = den with { Arrival = new GroundArrival(0x01060101, new Vector3(1f, 2f, 3f), 1f, 0f) };
        var other = elsewhere.ArrivalDestination();
        Assert.Equal((0x01060101u, new Vector3(1f, 2f, 3f)), (other.Location.Position.CellId, other.Location.Position.Local));
        Assert.Equal(0x0106u, elsewhere.ArrivalLandblock());
        Assert.IsType<PlaceLocRequest>(PlaceRequests.For(other)); // a location request, as for any non-ACE place
    }

    /// <summary>
    /// M5 2 fix F2 (review 2-R7): a profile's <c>home.town</c> is an ACE point-of-interest name, and only <see cref="GroundExit"/>'s constructor read it, so a dungeon whose town ACE's list lacks would load and pass every dat check, then
    /// fail every fight and death. Every dungeon profile's town resolves, through the constructor that resolves it, to exactly one point of interest, and the leave goes to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(DenRoomGraphTests.Dungeons), MemberType = typeof(DenRoomGraphTests))]
    public void Every_dungeons_home_town_is_one_ace_point_of_interest_the_way_out_resolves(string key)
    {
        var ground = GroundCatalog.Get(key);
        var named = AcePointsOfInterest.All.Where(p => p.DisplayName == ground.Home.Town).ToList();
        Assert.True(named.Count == 1, $"ground '{key}': its home town '{ground.Home.Town}' is {(named.Count == 0 ? "not an ACE point of interest" : "the name of " + named.Count + " points of interest")}");

        var exit = new GroundExit(ground, NullLogSink.Instance, "ground-rules"); // throws, naming the ground and the town, when the town does not resolve
        Assert.Equal(ground.Home.Town, exit.Destination.Name);
        Assert.Equal(named[0].CellId, exit.Destination.Location.Position.CellId);
    }

    [Fact]
    public void A_home_town_ace_does_not_list_fails_the_ways_out_construction_naming_the_ground_and_the_town()
    {
        var lost = DenGround.Profile with { Home = DenGround.Profile.Home with { Town = "Fort Tethana Outpost" } };
        var e = Assert.Throws<InvalidOperationException>(() => new GroundExit(lost, NullLogSink.Instance, "ground-rules"));
        Assert.Contains(lost.Key, e.Message, StringComparison.Ordinal);
        Assert.Contains("Fort Tethana Outpost", e.Message, StringComparison.Ordinal);
        Assert.Contains("not an ACE point of interest", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ground_of_several_landblocks_is_refused_by_the_room_graph_naming_the_todo()
    {
        var two = DenGround.Profile with { Landblocks = [0x0105, 0x0106] };
        var thrown = Assert.Throws<NotSupportedException>(() => GroundRooms.CoordOf(two));
        Assert.Contains("TODO(ground-multi-landblock)", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(two.Key, thrown.Message, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_room_graph_is_the_grounds_walkable_cells_at_its_own_lean()
    {
        var den = DenGround.Profile;
        var graph = GroundRooms.Load(fixture.Dats, den);
        Assert.Equal(den.Walk.DoorwayLean, graph.MaxLean);
        Assert.Equal(den.Walkable.Count, graph.Cells.Count);
        Assert.NotNull(graph.RoomFrame(den.Arrival.Cell));
        Assert.Null(graph.RoomFrame(0x01050341)); // the exit's room is no-go: not in the graph
        output.WriteLine($"the den's room graph: {graph.Cells.Count} rooms at lean {graph.MaxLean}");
    }

    [RequiresDatsFact]
    public void A_body_that_enters_a_no_go_cell_between_legs_stops_the_game_walk_at_that_frame()
    {
        // 2-R9: no leg is active (the fight is casting), and a slide or an overshoot puts the body in the exit's room: the walk still fails at the very next tick.
        var dats = fixture.Dats;
        var den = DenGround.Profile;
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(GroundRooms.CoordOf(den)));
        var graph = GroundRooms.Load(dats, den);
        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: new RecordingMovementSink());
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(den.Arrival.Frame, sliding: true));
        using var walk = new GameFightWalk(new RoutePlayHost(() => walker, () => 0), graph, den, NullLogSink.Instance);
        walk.Prepare(rig.View, rig.Now);
        Assert.Equal(FightWalkStatus.Idle, walk.Tick(rig.View, rig.Now)); // no leg begun: idle, the body is on the floor

        var room = LandblockCellSet.Load(new CellStructureLibrary(dats), GroundRooms.CoordOf(den)).GetCell(0x01050341)!;
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(new CellFrame(room.Id, new AcFrame(room.Frame.Origin, Quaternion.Identity)), sliding: true));
        Assert.Equal(FightWalkStatus.Failed, walk.Tick(rig.View, rig.Now + 0.05));
        Assert.Equal($"the walk entered the no-go cell 0x01050341 of {den.Name} (TODO(m3-fight-left-floor) if this is a planner or physics gap)", walk.Failure);
    }

    /// <summary>
    /// M6 4 w6a (W2's note): the fight walks build their own runners, and like the journey hosts' they carry the NPC body feed, so a leg plans round a standing NPC
    /// instead of sticking against her (<c>NpcBodyTests</c>); the game's fight walk's runner gets it at its first leg.
    /// </summary>
    [RequiresDatsFact]
    public void The_game_fight_walks_runner_plans_round_the_npcs_bodies()
    {
        var dats = fixture.Dats;
        var den = DenGround.Profile;
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(GroundRooms.CoordOf(den)));
        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: new RecordingMovementSink());
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(den.Arrival.Frame, sliding: true));
        using var walk = new GameFightWalk(new RoutePlayHost(() => walker, () => 0), GroundRooms.Load(dats, den), den, NullLogSink.Instance);
        walk.Prepare(rig.View, rig.Now);
        Assert.True(walk.BeginLeg(rig.View, rig.Now, out _), walk.Failure);
        Assert.IsType<NpcBodyFeed>(walk.Runner!.BodyFeed);
        Assert.IsType<SessionDoorFeed>(walk.Runner.Doors);
    }

    [RequiresDatsFact]
    public void Entering_a_no_go_cell_stops_the_game_walk_naming_the_cell_and_the_ground()
    {
        var dats = fixture.Dats;
        var den = DenGround.Profile;
        var geometry = new PhysicsGeometryLibrary(dats);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(GroundRooms.CoordOf(den)));
        var graph = GroundRooms.Load(dats, den);
        var rig = new J1JourneyRig(J1JourneyRig.DenCell);
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(FighterRunSkill), sink: new RecordingMovementSink());
        world.UseTime(0.0);
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(den.Arrival.Frame, sliding: true));
        var host = new RoutePlayHost(() => walker, () => 0);
        using var walk = new GameFightWalk(host, graph, den, NullLogSink.Instance);
        walk.Prepare(rig.View, rig.Now);
        Assert.True(walk.BeginLeg(rig.View, rig.Now, out var description), walk.Failure);
        Assert.Equal(FightWalkStatus.Walking, walk.Tick(rig.View, rig.Now));

        // The walker slides into the exit's room (0x0341, no-go): the walk stops at that frame, and says where.
        var room = LandblockCellSet.Load(new CellStructureLibrary(dats), GroundRooms.CoordOf(den)).GetCell(0x01050341)!;
        Assert.Equal(Data.Physics.Objects.SetPositionError.OK, walker.Physics.SetPositionSimple(new CellFrame(room.Id, new AcFrame(room.Frame.Origin, Quaternion.Identity)), sliding: true));
        Assert.Equal(FightWalkStatus.Failed, walk.Tick(rig.View, rig.Now + 0.05));
        Assert.Equal($"the walk entered the no-go cell 0x01050341 of {den.Name} (TODO(m3-fight-left-floor) if this is a planner or physics gap)", walk.Failure);
        output.WriteLine(description);
    }
}
