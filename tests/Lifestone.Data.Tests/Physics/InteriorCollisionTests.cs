using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// Buildings and EnvCells as collision on the real dats (retail CBuildingObj, CEnvCell, 11.4186 PORTAL/ccell and
/// cphysobj): walking in through Holtburg's doorways, through the Training Academy's portals, and into a wall.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class InteriorCollisionTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The Training Academy cell new characters start in (CharGen StarterAreas[0].Locations[0]).</summary>
    private const uint AcademySpawnCell = 0x860201AD;

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void Walking_in_through_each_Holtburg_doorway_enters_its_entry_cell()
    {
        var world = HoltburgWorld();
        var statics = LandblockStatics.Read(Dats, LandblockCoord.Holtburg)!;
        var terrain = new Lifestone.Data.World.Scenery.TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);
        var walked = 0;
        var notDoors = 0;
        var failures = new List<string>();
        foreach (var building in statics.Buildings)
        {
            foreach (var doorway in BuildingPortalGeometry.Load(Dats, building).Doorways)
            {
                // Building portals are every polygon a viewer outside looks in through: doors, but also windows and
                // openings in roofs and floors. A door here is upright, at least a human wide (0.96 m), and its sill is
                // within a step up (0.6 m) of the ground in front of it.
                var centre = doorway.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / doorway.Vertices.Length;
                var sill = doorway.Vertices.Min(v => v.Z);
                var width = MathF.Sqrt(Square(doorway.Vertices.Max(v => v.X) - doorway.Vertices.Min(v => v.X)) + Square(doorway.Vertices.Max(v => v.Y) - doorway.Vertices.Min(v => v.Y)));
                if (MathF.Abs(doorway.Plane.Normal.Z) > 0.1f || width < 0.96f || sill > terrain.GetHeight(centre.X, centre.Y) + 0.6f)
                {
                    notDoors++;
                    continue;
                }

                var entered = WalkThroughDoorway(world, doorway);
                walked++;
                if (entered != doorway.Portal.OtherCellId)
                {
                    failures.Add($"building 0x{building.ModelId:X8} doorway {doorway.PortalIndex}: entered 0x{entered:X8}, expected 0x{doorway.Portal.OtherCellId:X8}");
                }
            }
        }

        output.WriteLine($"{walked} doors walked through, {notDoors} building portals that are windows or openings skipped");
        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        Assert.True(walked > 10);
        Assert.Empty(failures);
    }

    [RequiresDatsFact]
    public void Crossing_each_upright_portal_of_the_Academy_rooms_near_the_spawn_enters_the_neighbour()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        world.Attach(builder.Build(LandblockCoord.FromId(AcademySpawnCell)));
        var structures = new CellStructureLibrary(Dats);

        // Breadth first from the spawn cell through its portals: from 1 m inside each upright portal a standing human fits
        // through (its axis from 0.2 to 1.8 m above the sill and its shoulders 0.48 m either side at 1 m, all within the
        // portal; on the room's side), walk straight through; the first other cell entered must be the portal's
        // neighbour. Portals no human fits through (the slivers beside an arch) are skipped.
        var queue = new Queue<uint>([AcademySpawnCell]);
        var seen = new HashSet<uint> { AcademySpawnCell };
        var crossed = 0;
        var flat = 0;
        var narrow = 0;
        var failures = new List<string>();
        while (queue.Count > 0 && seen.Count <= 40)
        {
            var cellId = queue.Dequeue();
            var data = EnvCellData.Read(Dats, cellId)!;
            var cell = (PhysEnvCell)world.GetCell(cellId)!;
            var structure = structures.Get(data);
            for (var i = 0; i < data.Portals.Count; i++)
            {
                var portal = data.Portals[i];
                if (portal.LeadsOutside)
                {
                    continue;
                }

                if (seen.Add(portal.OtherCellId))
                {
                    queue.Enqueue(portal.OtherCellId);
                }

                var polygon = structure.GetPortal(portal.PolygonId)!;
                var normal = cell.Position.Frame.LocalToGlobalVec(polygon.Plane.Normal);
                if (MathF.Abs(normal.Z) > 0.1f)
                {
                    flat++;
                    continue;
                }

                var inside = portal.PortalSide ? -normal : normal;
                inside.Z = 0f;
                inside = Vector3.Normalize(inside);
                var corners = polygon.Vertices.Select(cell.Position.Frame.LocalToGlobal).ToArray();
                var centre = corners.Aggregate(Vector3.Zero, (sum, v) => sum + v) / corners.Length;
                var origin = new Vector3(centre.X, centre.Y, corners.Min(v => v.Z) + 0.3f) + inside;
                var along = Vector3.Normalize(new Vector3(-inside.Y, inside.X, 0f));
                var sill = new Vector3(centre.X, centre.Y, corners.Min(v => v.Z));
                var opening = new PhysPolygon(0, corners);
                var fits = opening.PointInPolygon(sill + new Vector3(0, 0, 0.2f))
                    && opening.PointInPolygon(sill + new Vector3(0, 0, 1.8f))
                    && opening.PointInPolygon(sill + new Vector3(0, 0, 1f) + (along * 0.48f))
                    && opening.PointInPolygon(sill + new Vector3(0, 0, 1f) - (along * 0.48f));
                if (!fits || !cell.PointInCell(origin))
                {
                    narrow++;
                    continue;
                }

                var entered = WalkTowards(world, new CellFrame(cellId, new AcFrame(origin, Quaternion.Identity)), centre, cellId, out var end);
                crossed++;
                if (entered != portal.OtherCellId)
                {
                    failures.Add($"0x{cellId:X8} portal {i} to 0x{portal.OtherCellId:X8}: entered 0x{entered:X8}, ended at {end}");
                }
            }
        }

        output.WriteLine($"{crossed} upright portals crossed in {seen.Count} cells; skipped {flat} in floors or ceilings and {narrow} no human fits through");
        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        Assert.True(crossed > 20);
        Assert.Empty(failures);
    }

    /// <summary>
    /// Places a human at <paramref name="start"/> and walks it horizontally through <paramref name="target"/> and 1.5 m
    /// on; answers the first cell other than <paramref name="from"/> it is in (0 for none).
    /// </summary>
    private static uint WalkTowards(PhysicsWorld world, in CellFrame start, Vector3 target, uint from, out CellFrame end)
    {
        var body = Human(world);
        Assert.True(body.Place(start), $"placement at {start}");
        var across = target - body.Position.Frame.Origin;
        across.Z = 0f;
        var distance = across.Length();
        var step = across * (0.2f / distance);
        for (var s = 0; s < (int)((distance + 1.5f) / 0.2f); s++)
        {
            body.MoveBy(step);
            if (body.Position.CellId != from)
            {
                end = body.Position;
                return body.Position.CellId;
            }
        }

        end = body.Position;
        return 0;
    }

    [RequiresDatsFact]
    public void A_Holtburg_building_wall_stops_a_walker_without_letting_it_into_any_polygon()
    {
        var world = HoltburgWorld();
        var block = world.GetLandblock(LandblockCoord.Holtburg)!;
        var building = block.Buildings.First(b => b.Part.Shape.Physics is not null);
        var physics = building.Part.Shape.Physics!;

        // The largest wall facing out: a vertical polygon whose base is near the ground under it.
        var frame = building.Position.Frame;
        var wall = physics.Polygons
            .Where(p => MathF.Abs(p.Plane.Normal.Z) < 0.05f)
            .OrderByDescending(p => Area(p))
            .First();
        var normal = frame.LocalToGlobalVec(wall.Plane.Normal);
        var centre = frame.LocalToGlobal(wall.Vertices.ToArray().Aggregate(Vector3.Zero, (s, v) => s + v) / wall.NumPoints);
        var body = Human(world);
        Assert.True(body.Place(new CellFrame(LandCellAt(LandblockCoord.Holtburg, centre.X + (normal.X * 3f), centre.Y + (normal.Y * 3f)), new AcFrame(centre + (normal * 3f) + new Vector3(0, 0, 1f), Quaternion.Identity))));
        var into = -new Vector3(normal.X, normal.Y, 0f) * 0.2f;
        for (var i = 0; i < 30; i++)
        {
            body.MoveBy(into);
        }

        // No sphere of the walker overlaps any of the building's physics polygons.
        foreach (var height in new[] { 0.475f, 1.35f })
        {
            var sphere = new PhysSphere(frame.GlobalToLocal(body.Position.Frame.Origin + new Vector3(0, 0, height)), 0.48f - 2 * PhysicsGlobals.Epsilon);
            Assert.DoesNotContain(physics.Polygons, p => p.HitsSphere(sphere));
        }

        Assert.True(Vector3.Distance(body.Position.Frame.Origin, centre) < 3.5f, $"walker at {body.Position}, wall at {centre}");
    }

    private PhysicsWorld HoltburgWorld()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                world.Attach(builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value));
            }
        }

        return world;
    }

    /// <summary>
    /// Walks a human from 2 m outside the doorway straight in, and answers the first EnvCell it enters (0 for none).
    /// </summary>
    private static uint WalkThroughDoorway(PhysicsWorld world, BuildingDoorway doorway)
    {
        var centre = doorway.Vertices.Aggregate(Vector3.Zero, (s, v) => s + v) / doorway.Vertices.Length;
        var floor = doorway.Vertices.Min(v => v.Z);
        var outward = doorway.Portal.PortalSide ? -doorway.Plane.Normal : doorway.Plane.Normal;
        outward.Z = 0f;
        outward = Vector3.Normalize(outward);
        var start = new Vector3(centre.X, centre.Y, floor + 0.3f) + (outward * 2f);
        var body = Human(world);
        body.Place(new CellFrame(LandCellAt(LandblockCoord.Holtburg, start.X, start.Y), new AcFrame(start, Quaternion.Identity)));
        for (var i = 0; i < 25; i++)
        {
            body.MoveBy(-outward * 0.2f);
            if (CellIds.IsEnvCell(body.Position.CellId))
            {
                return body.Position.CellId;
            }
        }

        return 0;
    }

    private static float Square(float value) => value * value;

    private static float Area(PhysPolygon polygon)
    {
        var sum = Vector3.Zero;
        var v = polygon.Vertices;
        for (var i = 1; i + 1 < v.Length; i++)
        {
            sum += Vector3.Cross(v[i] - v[0], v[i + 1] - v[0]);
        }

        return sum.Length() * 0.5f;
    }
}
