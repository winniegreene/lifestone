using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// Room walks the world walk's rooms found wrong (skips without the dats). Walks past static objects: the plan ran through the object (or failed at it both ways), a body slid round it without the check, and the check
/// rightly refused the line; each is walked from the room's frame, as the world walk places a body, with the check on. And ledges the room graph counted as rooms.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class RoomStaticShapesDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <param name="block">The landblock (a dungeon's, so no neighbours).</param>
    /// <param name="from">The room the walk starts in.</param>
    /// <param name="to">The room it goes to.</param>
    [RequiresDatsTheory]
    [InlineData(0x00AFu, 0x017Au, 0x0179u)] // a pillar 0x02000248 (a cylinder 3 m wide in radius, shapes only) in the middle of a 10 m room: its Setup radius closed the room
    [InlineData(0x0002u, 0x0198u, 0x019Cu)] // a ball 0x02000988 (0.4 m) beside the doorway: the walk's end lay inside its disc, so the walk went straight past it
    [InlineData(0x000Cu, 0x011Fu, 0x0120u)] // a staircase 0x010000D6 (physics BSP) across a 10 m corridor, its disc the whole room: the walk takes the lane beside it
    [InlineData(0x0106u, 0x0182u, 0x0184u)] // the room's frame under a bunk's top board 0x01000A29 (physics BSP, 1.49 m up): the foot fits there and the body does not, so the walk starts from the nearest place it stands
    [InlineData(0x5966u, 0x0204u, 0x0205u)] // a GfxObj 0x01004DAE (physics BSP) whose origin is 2.3 m up and whose solid reaches under the floor: the origin rule called it overhead
    [InlineData(0x01EBu, 0x032Eu, 0x032Fu)] // the same rule for another GfxObj with physics BSP whose origin is over a standing human
    [InlineData(0x00CBu, 0x0151u, 0x015Au)] // an object of the next room reaching through the doorway: the planner read the room's own objects only, and the plan ran into it
    [InlineData(0x8A03u, 0x0135u, 0x0137u)] // the same, with the walk starting beside the doorway
    public void A_room_walk_past_a_static_goes_round_it_with_the_check_on(uint block, uint from, uint to)
    {
        var coord = new LandblockCoord((byte)(block >> 8), (byte)(block & 0xFF));
        var lb = ((uint)coord.X << 24) | ((uint)coord.Y << 16);
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var structures = new CellStructureLibrary(fixture.Dats);
        var world = new PhysicsWorld(PhysicsCompat.Ace);
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, geometry, structures).Build(coord));
        var graph = new RoomGraph(LandblockCellSet.Load(structures, coord), RoomGraph.BuildingDoorwayLean);
        var source = new RoomPathSource(graph);
        var goal = graph.RoomFrame(lb | to)!.Value;
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, "room-static");
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), geometry, new DatAnimationSource(fixture.Dats), sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);

        // The world walk's placement: the room's frame, else a few centimetres up or a step aside, staying in the room.
        var frame = graph.RoomFrame(lb | from)!.Value;
        Vector3[] nudges = [Vector3.Zero, new(0, 0, 0.05f), new(0, 0, 0.25f), new(0.3f, 0, 0.05f), new(-0.3f, 0, 0.05f), new(0, 0.3f, 0.05f), new(0, -0.3f, 0.05f)];
        Assert.Contains(nudges, n => walker.Place(new CellFrame(frame.CellId, new AcFrame(frame.Origin + n, Quaternion.Identity))) && walker.Position.CellId == frame.CellId);

        using var check = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), source, check, audit, new NavRunnerOptions { Name = "room-static", ScarClock = time }, NullLogSink.Instance);
        runner.Start(NavGoal.ToPoint(new NavPoint(lb, goal.Origin), lb | to), now);
        while (!runner.IsDone && now < 40)
        {
            now += 1.0 / 60;
            time.Advance(1.0 / 60);
            walker.Controller.Tick(now);
            runner.Step(now);
            session.Tick(now);
        }

        output.WriteLine($"{runner.State}: refusals {runner.ConfirmFailures}, rungs {runner.Rungs}, {runner.FailureReason}");
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(0, runner.ConfirmFailures);
        Assert.Equal(0, runner.Rungs);
    }

    /// <summary>
    /// A walk begun standing still beside a staircase, facing north as the world walk places a body (its standing place moved off the room's centre, which lies in the stairs): the
    /// planner reads the staircase (Setup 0x02000396, no radius of its own, 5.7 m long and 4.8 m tall, in a house layout repeated over a hundred landblocks) by its parts and bends the plan round it, and a
    /// body standing still turns to its first leg before it runs, in a corner's approach too. Each walked into the stairs' side and needed a recovery move.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0x42AEu, 0x0109u, 164.325f, 36.8f, 66.05f, 0x0108u)] // the staircase's side 0.33 m off, the plan's bend 12 degrees off north
    [InlineData(0x46A5u, 0x0109u, 27.675f, 35.2f, 82.05f, 0x010Du)] // the same room in another landblock, the stairs beside it, its doorway 25 degrees off north and within a corner's approach
    public void A_walk_begun_standing_beside_stairs_turns_first_and_goes_round(uint block, uint from, float x, float y, float z, uint to)
    {
        var coord = new LandblockCoord((byte)(block >> 8), (byte)(block & 0xFF));
        var lb = ((uint)coord.X << 24) | ((uint)coord.Y << 16);
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var structures = new CellStructureLibrary(fixture.Dats);
        var world = new PhysicsWorld(PhysicsCompat.Ace);
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, geometry, structures).Build(coord));
        var graph = new RoomGraph(LandblockCellSet.Load(structures, coord), RoomGraph.BuildingDoorwayLean);
        var goal = graph.RoomFrame(lb | to)!.Value;
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, "stand-start");
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), geometry, new DatAnimationSource(fixture.Dats), sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(lb | from, new AcFrame(new Vector3(x, y, z), Quaternion.Identity))));

        using var check = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), check, audit, new NavRunnerOptions { Name = "stand-start", ScarClock = time }, NullLogSink.Instance);
        runner.Start(NavGoal.ToPoint(new NavPoint(lb, goal.Origin), lb | to), now);
        while (!runner.IsDone && now < 30)
        {
            now += 1.0 / 60;
            time.Advance(1.0 / 60);
            walker.Controller.Tick(now);
            runner.Step(now);
            session.Tick(now);
        }

        output.WriteLine($"{runner.State} in {now:F1} s: refusals {runner.ConfirmFailures}, rungs {runner.Rungs}, {runner.FailureReason}");
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(0, runner.ConfirmFailures);
        Assert.Equal(0, runner.Rungs);
    }

    /// <summary>
    /// A walk to a standing place the world walk moved off its room's centre, which the route check refused though the body walks it. Beside a dais (0x720201F7, a layout repeated over 83 landblocks), the room's
    /// centre is on the dais, the place 0.75 m east of it between the dais and a wall fitting: the straight walk from the doorway's push clipped the dais and no floor corner or centre saw both ends, so points round the
    /// walk's ends are tried too. Through an arch in a thick wall (0x0287010E, 0x01680137: rooms 0.7 m deep), the push 0.5 m past the doorway lay beyond a band where the foot does not fit, so the push is taken at
    /// half the distance when the ground does not hold all the way to it.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0x7202u, 0x01F6u, 40f, -90f, 0.05f, 0x01F7u, 40.75f, -96.19f, 0.05f)] // beside the dais
    [InlineData(0x0287u, 0x010Bu, 42.17f, -20f, -11.95f, 0x010Eu, 39.7f, -15.33f, -11.95f)] // an arch, the band 0.2 m short of the push
    [InlineData(0x0168u, 0x0128u, 34.01f, -70f, 0.05f, 0x0137u, 35.03f, -70f, 0.05f)] // an arch, the push itself where the foot does not fit
    [InlineData(0x7200u, 0x01ADu, 114.23f, -86.52f, 0.05f, 0x01B1u, 110f, -87.02f, 0.05f)] // beside an object filling the room, a gap of half a metre to the doorway that no corner or ring point sees: the grid finds it
    [InlineData(0x0007u, 0x0144u, 70.89f, -74.87f, 0.05f, 0x0133u, 60f, -66.67f, 0.05f)] // inside a ring of posts round a fountain, its disc only bounding them: the grid finds the way out
    [InlineData(0x0011u, 0x0128u, 35.5f, -210f, -29.95f, 0x0111u, 34.5f, -210f, -29.95f)] // half a metre from a doorway, nearer than its approach in a thin room: the walk goes straight to the push
    [InlineData(0x7200u, 0x0149u, 63.18f, -63.48f, 0.05f, 0x014Bu, 61.25f, -60f, 0.05f)] // a statue's base on the push of a narrow doorway: no square crossing stands, the push moved a quarter metre does
    [InlineData(0x011Du, 0x010Fu, 34.75f, -38.59f, 0.05f, 0x0113u, 40f, -36.67f, 0.05f)] // the approach in a wall's thickness: moved where the foot stands
    [InlineData(0x7200u, 0x0255u, 150f, -170f, 0.05f, 0x0257u, 152.98f, -168.75f, 0.05f)] // a statue's base on the push: moved where the foot stands
    [InlineData(0x7200u, 0x0257u, 152.98f, -168.75f, 0.00f, 0x0253u, 153.48f, -173.48f, 0.00f)] // a statue as wide as its niche between the start and the doorway: the way round lies through the hall beside it
    [InlineData(0x009Cu, 0x0191u, 330.00f, -80.00f, -24.00f, 0x0193u, 330.00f, -87.50f, -24.00f)] // a step from the start to a floor's corner crossed a table's edge: every step of a detour is probed
    [InlineData(0x002Eu, 0x018Fu, 176.67f, -250.00f, -35.99f, 0x018Du, 173.33f, -250.00f, -36.00f)] // the check's samples climb onto something beside the step: the next is probed again from the step's line
    [InlineData(0x0044u, 0x0150u, 207.75f, -472.25f, -11.99f, 0x0152u, 214.75f, -470.00f, -11.95f)] // a straight step over the floor meets a solid the floor test does not see: the probe sends it round
    [InlineData(0x5660u, 0x019Au, 160.75f, -13.81f, 0.00f, 0x019Eu, 160.00f, -15.63f, 0.05f)] // a doorway point moved where the foot stands left a crossing the probe refuses: the pair whose crossing walks
    [InlineData(0x0169u, 0x0156u, 17.50f, -127.50f, 0.00f, 0x014Fu, 12.50f, -127.50f, 0.00f)] // the body placed on the line is refused, stepped there from where it stood it gets there: the check's step
    [InlineData(0x00E8u, 0x0104u, 20.00f, -23.33f, 0.09f, 0x0101u, 10.00f, -23.33f, 0.25f)] // a straight step where the foot finds ground and the body does not fit: the planner places the body too and goes round
    [InlineData(0x0015u, 0x016Du, 35.25f, -30.00f, -11.99f, 0x016Bu, 42.25f, -26.50f, -11.95f)] // the foot meets a wall at every height beside the line: the body stepped there from where it stood gets there, and its ground is taken
    [InlineData(0x01AEu, 0x0251u, 150.00f, -6.67f, -12.00f, 0x0250u, 149.70f, -3.33f, -11.95f)] // a step aimed square at something stops flat against it: a sidestep, then on
    [InlineData(0x013Du, 0x010Eu, 1.93f, -247.50f, -41.99f, 0x010Du, 0.00f, -251.69f, -41.95f)] // a sample behind a post in a doorway's middle, nearer its centre than a body stands: the body passes it to the next sample
    public void A_walk_to_a_moved_standing_place_passes_the_check(uint block, uint from, float x, float y, float z, uint to, float gx, float gy, float gz)
    {
        var coord = new LandblockCoord((byte)(block >> 8), (byte)(block & 0xFF));
        var lb = ((uint)coord.X << 24) | ((uint)coord.Y << 16);
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var structures = new CellStructureLibrary(fixture.Dats);
        var world = new PhysicsWorld(); // retail, as the product and the world walk run it (the planner's own world is retail too)
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, geometry, structures).Build(coord));
        var graph = new RoomGraph(LandblockCellSet.Load(structures, coord), RoomGraph.BuildingDoorwayLean);
        var goal = new Vector3(gx, gy, gz);
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, NullLogSink.Instance);
        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, "moved-place");
        var walker = OfflineWalker.Create(fixture.Dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(OfflinePlayerWeenie.PlaceholderRunSkill), geometry, new DatAnimationSource(fixture.Dats), sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(lb | from, new AcFrame(new Vector3(x, y, z), Quaternion.Identity))));

        using var check = new RouteConfirmer(world, walker.Physics.Setup!);
        using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), new RoomPathSource(graph), check, audit, new NavRunnerOptions { Name = "moved-place", ScarClock = time }, NullLogSink.Instance);
        runner.Start(NavGoal.ToPoint(new NavPoint(lb, goal), lb | to), now);
        while (!runner.IsDone && now < 30)
        {
            now += 1.0 / 60;
            time.Advance(1.0 / 60);
            walker.Controller.Tick(now);
            runner.Step(now);
            session.Tick(now);
        }

        output.WriteLine($"{runner.State} in {now:F1} s: refusals {runner.ConfirmFailures}, rungs {runner.Rungs}, {runner.FailureReason}");
        Assert.Equal(NavRunState.Arrived, runner.State);
        Assert.Equal(0, runner.ConfirmFailures);
        Assert.Equal(0, runner.Rungs);
    }

    /// <summary>
    /// A bed 3 m long in the middle of 0x0043010C, end on to its doorway (Setup 0x02001051, in a layout repeated over many dungeons), has no radius of its own: it is solid only by its part's physics BSP. Read by its
    /// part's reach it stands in the way, and a walk between the doorway and a place beside it goes round it by a corner drawn in from the room's thick walls, so the check passes the plan both ways.
    /// </summary>
    [RequiresDatsFact]
    public void A_bed_with_no_radius_of_its_own_stands_in_the_way_by_its_part_and_walks_go_round_it()
    {
        var coord = new LandblockCoord(0x00, 0x43);
        var geometry = new PhysicsGeometryLibrary(fixture.Dats);
        var structures = new CellStructureLibrary(fixture.Dats);
        var world = new PhysicsWorld(PhysicsCompat.Ace);
        world.Attach(new LandblockCollisionBuilder(fixture.Dats, geometry, structures).Build(coord));
        var cells = LandblockCellSet.Load(structures, coord);
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var source = new RoomPathSource(graph);
        Assert.True(graph.Planner.InStatic(cells.GetCell(0x0043010Cu)!, new Vector3(20f, -377.5f, -18f)));

        var beside = new CellFrame(0x0043010Cu, new AcFrame(new Vector3(21.25f, -377.5f, -18f), Quaternion.Identity));
        var hall = graph.RoomFrame(0x0043010Bu)!.Value;
        using var check = new RouteConfirmer(world, PhysicsSetup.Load(fixture.Dats, OfflineWalker.HumanSetupId, geometry)!);
        foreach (var (from, to) in new[] { (beside, hall), (hall, beside) })
        {
            var path = source.Plan(from, NavGoal.ToPoint(new NavPoint(0x00430000u, to.Origin), to.CellId), new NavScars());
            Assert.Equal(NavPathStatus.Found, path.Status);
            Assert.True(check.ConfirmAll(path.Waypoints) == RouteConfirmStatus.Confirmed, $"{from.CellId:X8} to {to.CellId:X8}: {check.Failure}");
        }
    }

    /// <summary>
    /// A plinth 0.5 m wide and 0.5 m high along a room's walls (0x62440115 and 0118, in a layout repeated over hundreds of landblocks) has a floor and doorways but holds no body: it is a ledge, and its
    /// doorways are closed. The room beside it is a room.
    /// </summary>
    [RequiresDatsFact]
    public void A_plinth_along_a_rooms_walls_is_a_ledge_and_its_doorways_are_closed()
    {
        var coord = new LandblockCoord(0x62, 0x44);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), coord);
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var room = cells.GetCell(0x62440113u)!;
        Assert.True(graph.Planner.IsLedge(cells.GetCell(0x62440115u)!));
        Assert.True(graph.Planner.IsLedge(cells.GetCell(0x62440118u)!));
        Assert.False(graph.Planner.IsLedge(room));
        Assert.False(graph.IsPassable(room, room.Portals.Single(p => p.OtherCellId == 0x62440115u)));
    }

    /// <summary>
    /// An L-shaped edging 0.5 m wide and 0.5 m high round a room's corner (0x00110107 and 0102, env 0x0D000599, repeated over dozens of dungeons): at its outer corner a body balances with its centre on the
    /// floor's diagonal edge, mostly off it, which is no standing on it, so it is a ledge and the room's doorway onto it is closed.
    /// </summary>
    [RequiresDatsFact]
    public void An_edging_round_a_rooms_corner_is_a_ledge_though_a_body_balances_on_its_corner()
    {
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(0x00, 0x11));
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var room = cells.GetCell(0x00110105u)!;
        Assert.True(graph.Planner.IsLedge(cells.GetCell(0x00110107u)!));
        Assert.True(graph.Planner.IsLedge(cells.GetCell(0x00110102u)!));
        Assert.False(graph.Planner.IsLedge(room));
        Assert.False(graph.IsPassable(room, room.Portals.Single(p => p.OtherCellId == 0x00110107u)));
    }

    /// <summary>A doorframe as narrow as a ledge whose middle a static blocks (0x01680137: the mesh 0x01000A69 in its mouth) still holds the foot in the lane beside it, so it is no ledge.</summary>
    [RequiresDatsFact]
    public void A_doorframe_with_a_static_in_its_middle_is_not_a_ledge()
    {
        var cells = LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), new LandblockCoord(0x01, 0x68));
        var graph = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        Assert.False(graph.Planner.IsLedge(cells.GetCell(0x01680137u)!));
        Assert.NotNull(graph.FindPath(0x01680128u, 0x01680134u));
    }
}
