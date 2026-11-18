using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Movement.Wayfinding;

/// <summary>
/// M6 5 w (<c>m6-alabree-entry-walk</c>): the Cave of Alabree's entry portal 4937 (guid 0x7A7B3007) stands in 0xA7B30100, the portal room of a dungeon-entrance cave on the land
/// (environment 0x0D0003F2, five cells): the mouth 0xA7B30104 opens west onto the land (sill (98.66, 135.41, 30.00)), its two portals leaning (normal Z 0.24); the mouth's floor
/// 0xA7B30103 (z 30.12) has a hole into the slab 0xA7B30102 (0.12 m) and a second hole under it onto the ramp floor of 0xA7B30101 (normal Z 0.79, z 30.12 down to 24.80 over 6.9 m);
/// the ramp ends at the doorway of 0xA7B30100 (sill z 24.80). Under the upright rule (0.1) the mouth was no doorway and the tile cut no lane, and the holes had no static stair, so
/// no plan reached the portal: <c>no-doorway-lane-reaches-the-goal</c>. Since this slice the buildings' doorway lean is the den's 0.3 (<see cref="RoomGraph.BuildingDoorwayLean"/>)
/// and a hole over a ramp floor is a stair (<see cref="CellRoutePlanner.StairOf"/>, the ramp half).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class AlabreeEntryCaveDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Mouth = 0xA7B30104, MouthFloor = 0xA7B30103, Slab = 0xA7B30102, Ramp = 0xA7B30101, PortalRoom = 0xA7B30100;

    private LandblockCellSet Cells() => LandblockCellSet.Load(new CellStructureLibrary(fixture.Dats), LandblockCoord.FromId(PortalRoom));

    /// <summary>The mouth is an exit to the land at the buildings' lean and was none under the upright rule; the tile cuts its lane and the room graph joins it.</summary>
    [RequiresDatsFact]
    public void The_caves_leaning_mouth_is_an_exit_with_a_lane_at_the_buildings_lean()
    {
        var cells = Cells();
        Assert.DoesNotContain(new RoomGraph(cells).ExitsToOutside, e => e.CellId == Mouth);
        var rooms = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var exit = Assert.Single(rooms.ExitsToOutside, e => e.CellId == Mouth);
        Assert.InRange(exit.Sill.X, 98.5f, 98.8f);
        Assert.True(exit.Outward.X < -0.99f, "the mouth opens west");

        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(fixture.Dats, null, world, map, NullLogSink.Instance);
        streamer.EnsureNow(PortalRoom);
        Assert.True(map.TryGetTile(0xA7B30000, out var tile));
        Assert.True(rooms.TryJoin(tile, Mouth, out var lane), "the tile's lane for the mouth joins the room graph's exit");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"lane into 0x{lane.EntryCellId:X8} sill ({lane.Sill.X:0.00}, {lane.Sill.Y:0.00}, {lane.Sill.Z:0.00}) {lane.Nodes.Count} nodes"));
    }

    /// <summary>The two holes over the ramp are a stair (the ramp floor's, no static object), so the room graph walks from the mouth to the portal room.</summary>
    [RequiresDatsFact]
    public void The_holes_over_the_ramp_floor_are_a_stair_and_the_rooms_join_the_mouth_to_the_portal_room()
    {
        var cells = Cells();
        var rooms = new RoomGraph(cells, RoomGraph.BuildingDoorwayLean);
        var floor = cells.GetCell(MouthFloor)!;
        var stair = rooms.Planner.StairOf(floor, floor.Portals.Single(p => p.OtherCellId == Slab));
        Assert.NotNull(stair);
        var s = stair!.Value;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"stair: foot ({s.Foot.X:0.00}, {s.Foot.Y:0.00}, {s.Foot.Z:0.00}) landing ({s.Landing.X:0.00}, {s.Landing.Y:0.00}, {s.Landing.Z:0.00}), past the foot in 0x{s.BeforeFoot.CellId:X8}, {s.Length:0.00} m"));
        Assert.Equal(Ramp, s.BeforeFoot.CellId);
        Assert.Equal(MouthFloor, s.PastLanding.CellId);
        Assert.InRange(s.Foot.Y, 134.5f, 136.0f); // down the ramp's middle (its centre line through the hole's middle), not off the lip at a side

        Assert.Equal([Mouth, MouthFloor, Slab, Ramp, PortalRoom], rooms.FindPath(Mouth, PortalRoom)!);
        var plan = rooms.Plan(Mouth, new Vector3(99.3f, 135.4f, 30.12f), PortalRoom, new Vector3(117.574f, 134.945f, 24.805f));
        Assert.NotNull(plan);
        Assert.Equal(PortalRoom, plan!.Cells[^1]);
    }

    /// <summary>The physics walks it: a body walking east from the mouth's floor goes down the ramp into the portal room, and one walking west leaves by the mouth onto the land.</summary>
    [RequiresDatsTheory]
    [InlineData(90f, PortalRoom)]
    [InlineData(270f, 0xA7B3001Eu)]
    public void A_body_walks_down_the_ramp_into_the_portal_room_and_out_by_the_mouth(float heading, uint endCell)
    {
        var dats = fixture.Dats;
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats)).Build(new LandblockCoord(0xA7, 0xB3)));
        var walker = OfflineWalker.Create(dats, world, NullLogSink.Instance, new OfflinePlayerWeenie(230));
        world.UseTime(0.0);
        Assert.True(walker.Place(new CellFrame(MouthFloor, RetailFrames.SetHeading(new AcFrame(new Vector3(99.6f, 135.3f, 30.2f), Quaternion.Identity), heading))));
        walker.DoMotion(MotionCommands.WalkForward);
        var lowest = float.MaxValue;
        for (var now = 1.0 / 30; now <= 6; now += 1.0 / 30)
        {
            walker.Controller.Tick(now);
            lowest = MathF.Min(lowest, walker.Position.Origin.Z);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"heading {heading}: {walker.Position}, lowest z {lowest:0.00}"));
        Assert.Equal(endCell, walker.Position.CellId);
        if (endCell == PortalRoom)
        {
            Assert.InRange(walker.Position.Origin.Z, 24.75f, 24.85f); // the portal room's floor, 24.80
        }
    }
}
