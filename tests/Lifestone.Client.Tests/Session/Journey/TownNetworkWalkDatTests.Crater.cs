using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 8 B1, on the retail dats: the walks of the chains to Crater Lake Village and the stone cave that the hop's own theory does not cover: the walk out of the Crater Pathway's top building (a body), the way in to the Crater Pathway's portal through its door and down its stair (a body along the file's way),
/// Silencia's tower: the spiral inside it (a body, flight by flight, with no jump), the run from the Yinar drop to the stone's cave (planned and confirmed), and the village's walk to the Crater Caves portal.
/// </summary>
public sealed partial class TownNetworkWalkDatTests
{
    /// <summary>The tower's flights, in order: the floor of 0x90D00100 inside the door, then a landing a flight, the last the roof's rim (the points from the probe of the tower's floor heights, M6 8 B1: x from 56.5 to 63.5 and y from 105 to 111.5, 0.5 m a sample; each is a straight run the body makes within a plan).</summary>
    private static readonly (float X, float Y, float Z)[] Flights =
    [
        (60f, 107f, 282f), (60f, 110.7f, 283.6f), (57.3f, 110.7f, 284.4f), (57.4f, 105.3f, 287.6f), (62.2f, 105.3f, 290.8f), (62.8f, 110.7f, 294f), (58.2f, 110.8f, 297.2f),
    ];

    /// <summary>Silencia (landblock_instance guid 0x790D0003).</summary>
    private static readonly Vector3 Silencia = new(59.0085f, 107.151f, 297.207f);

    /// <summary>Silencia's Magma Golem (landblock_instance guid 0x790D0007).</summary>
    private static readonly Vector3 Golem = new(62.1963f, 107.7f, 297.21f);

    /// <summary>The walk out of the Crater Pathway's top building: a body from the drop to the street, by the two points of the travel file, with no rung.</summary>
    [RequiresDatsFact]
    public void A_body_walks_out_of_the_crater_pathways_top_building_to_the_street()
    {
        var edge = Routes.Edge("pathway-to-crater-lake");
        var start = new CellFrame(edge.DestCell, new AcFrame(edge.DestOrigin, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
        foreach (var p in edge.WalkOut!)
        {
            var end = rig.Run(rig.PointGoal(p.Cell, p.Origin), "walk-out");
            output.WriteLine(end.Line);
            Assert.True(end.Arrived, end.Line);
            Assert.Equal(0, end.Rungs);
        }

        Assert.True(LandDefs.IsOutdoorCell(rig.Position.CellId), $"the walk out ended in 0x{rig.Position.CellId:X8}");
    }

    /// <summary>
    /// Silencia's roof: the body walks the tower's spiral inside it, from the street through the door of 0x90D00100 and up seven flights by point goals planned as one room (the physics takes it through 0x90D00101, the shaft the room graph has no floor for) onto the roof, at z 297.2, with no jump, and the last
    /// point stands within the use reach of Silencia and of her golem (the buy and the give are the server's move-to from there, D57's reach=use).
    /// </summary>
    [RequiresDatsFact]
    public void A_body_climbs_the_tower_to_silencias_roof_and_stands_within_reach_of_her_and_her_golem()
    {
        var walkOut = Routes.Edge("pathway-to-crater-lake").WalkOut!;
        var street = new CellFrame(PortalCell(0x90D00000, walkOut[^1].Origin), new AcFrame(walkOut[^1].Origin, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, street);
        foreach (var (x, y, z) in Flights)
        {
            var end = rig.Run(rig.PointGoal(0x90D00100, new Vector3(x, y, z)), string.Create(CultureInfo.InvariantCulture, $"flight ({x}, {y}, {z})"));
            output.WriteLine(end.Line);
            Assert.True(end.Arrived, end.Line);
            Assert.Equal(0, end.Rungs);
        }

        var at = rig.Position.Frame.Origin;
        Assert.InRange(at.Z, 296.5f, 297.8f);
        foreach (var (name, npc) in new[] { ("Silencia", Silencia), ("the golem", Golem) })
        {
            var metres = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(npc.X, npc.Y));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"the rim of the roof (the body at ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0})) is {metres:0.0} m from {name}"));
            Assert.True(metres <= QuestStepLeg.UseReachMetres, $"{name} is {metres:0.0} m from the body, over the use reach {QuestStepLeg.UseReachMetres}");
        }
    }

    /// <summary>The way in to the Crater Pathway's portal: a body from the door's front through the door and down the stair (the file's way, <see cref="AuthoredWaySource"/>), then the seek to the portal within 2 m.</summary>
    [RequiresDatsFact]
    public void A_body_walks_the_way_in_to_the_crater_pathways_portal()
    {
        var edge = Routes.Edge("highland-to-pathway");
        var way = edge.WalkIn!;
        var start = new CellFrame(way[0].Cell, new AcFrame(way[0].Origin, Quaternion.Identity));
        using var rig = new FacilityHubWalkDatTests.Rig(Dats, start);
        var frames = way.Skip(1).Select(p => new CellFrame(p.Cell, new AcFrame(p.Origin, Quaternion.Identity))).ToList();
        var end = rig.Run(rig.PointGoal(way[^1].Cell, way[^1].Origin), "walk-in", way: new AuthoredWaySource(frames));
        output.WriteLine(end.Line);
        Assert.True(end.Arrived, end.Line);
        var portal = RungPortal.Of(edge, Routes);
        var seek = rig.Run(rig.SeekGoal(portal), "seek");
        output.WriteLine(seek.Line);
        Assert.True(seek.Arrived, seek.Line);
    }

    /// <summary>The run from the Yinar drop to the stone's cave: the mouth, then the stone in 0xA66C0100, planned and confirmed (about 1.4 km outdoors).</summary>
    [RequiresDatsFact]
    public void The_run_from_the_yinar_drop_reaches_the_minor_smoldering_stones_cave()
    {
        var drop = Routes.Edge("samsur-to-yinar");
        var from = new CellFrame(drop.DestCell, new AcFrame(drop.DestOrigin, Quaternion.Identity));
        using var rig = Rig(from);
        var stone = new Vector3(83.55f, 141.65f, 16.8f);
        var walk = Walk(rig, from, rig.PointGoal(0xA66C0100, stone));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=yinar-to-stone metres={walk.Metres:0.0} pass={(walk.Ok ? "yes" : "no")} {walk.Why}"));
        Assert.True(walk.Ok, walk.Why);
        Assert.Equal(0xA66C0100u, walk.End.CellId);
    }

    /// <summary>From the street to the Crater Caves portal's front (the ground's run) and the seek to it.</summary>
    [RequiresDatsFact]
    public void The_village_walk_reaches_the_crater_caves_portal()
    {
        var ground = Lifestone.Client.Session.Grounds.GroundCatalog.Get("crater-caves");
        var entry = ground.Entry!;
        var walkOut = Routes.Edge("pathway-to-crater-lake").WalkOut!;
        var street = new CellFrame(PortalCell(0x90D00000, walkOut[^1].Origin), new AcFrame(walkOut[^1].Origin, Quaternion.Identity));
        using var rig = Rig(street);
        var via = Routes.RoutesFor("crater-caves")[0].Run!.Via!.Points[0];
        var toVia = Walk(rig, street, rig.PointGoal(via.Cell, via.Origin));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=village-to-via metres={toVia.Metres:0.0} pass={(toVia.Ok ? "yes" : "no")} {toVia.Why}"));
        Assert.True(toVia.Ok, toVia.Why);
        var toFront = Walk(rig, toVia.End, rig.PointGoal(entry.FrontCell, entry.Front));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"quest-walk name=via-to-caves metres={toFront.Metres:0.0} pass={(toFront.Ok ? "yes" : "no")} {toFront.Why}"));
        Assert.True(toFront.Ok, toFront.Why);
        var seek = Walk(rig, toFront.End, rig.ObjectGoal(entry.Guid, PortalCell(entry.Cell, entry.Origin), entry.Origin, DenTravelStation.PortalRangeMetres));
        Assert.True(seek.Ok, seek.Why);
    }
}
