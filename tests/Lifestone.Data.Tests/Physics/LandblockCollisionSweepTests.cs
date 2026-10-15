using System.Diagnostics;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// Building landblock collision over a wide area, and placing a human in every room of Holtburg and the Training Academy
/// (Tier=Exhaustive: minutes, run on demand).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class LandblockCollisionSweepTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_17_by_17_landblocks_around_Holtburg_build_and_attach()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        var watch = Stopwatch.StartNew();
        int envCells = 0, buildings = 0, statics = 0, foreign = 0, water = 0;
        for (var dx = -8; dx <= 8; dx++)
        {
            for (var dy = -8; dy <= 8; dy++)
            {
                var block = builder.Build(LandblockCoord.Holtburg.Offset(dx, dy)!.Value);
                world.Attach(block);
                envCells += block.EnvCells.Count;
                buildings += block.Buildings.Count;
                statics += block.StaticObjects.Count;
                foreign += block.ForeignShadows.Count;
                water += block.WaterType == WaterType.EntirelyWater ? 1 : 0;
                Assert.All(block.LandCells, c => Assert.Same(c, world.GetCell(c.Id)));
            }
        }

        output.WriteLine($"289 landblocks in {watch.ElapsedMilliseconds} ms: {envCells} EnvCells, {buildings} buildings, {statics} statics with a shape, {foreign} shadows across edges, {water} landblocks under water");
        Assert.Equal(289, world.AttachedCount);
    }

    [RequiresDatsTheory]
    [InlineData(0xA9B4)]
    [InlineData(0x8602)]
    public void A_standing_human_can_be_placed_at_the_centre_of_every_rooms_largest_floor(int landblock)
    {
        var coord = new LandblockCoord((byte)(landblock >> 8), (byte)landblock);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(Dats).Build(coord));
        var structures = new CellStructureLibrary(Dats);
        int placed = 0, noFloor = 0, smallFloor = 0;
        var failures = new List<string>();
        foreach (var data in LandblockCellSet.Load(structures, coord).Cells.OrderBy(c => c.Id))
        {
            var floor = structures.Get(data).Floors.MaxBy(f => f.Area);
            if (floor is null)
            {
                noFloor++;
                continue;
            }

            // A floor a standing human's lower sphere does not fit on around its centre (the slivers beside arches and
            // corners) is not a room to stand in, nor is an upward-facing polygon at the top of the cell (the floor of the
            // room above, seen from below): the human's lower sphere must be in the cell.
            var cell = (PhysEnvCell)world.GetCell(data.Id)!;
            var at = new CellFrame(data.Id, new AcFrame(cell.Position.Frame.LocalToGlobal(floor.Centroid) + new Vector3(0, 0, 0.02f), Quaternion.Identity));
            if (EdgeClearance(floor) < 0.48f || !cell.PointInCell(at.Frame.Origin + new Vector3(0, 0, 0.475f)))
            {
                smallFloor++;
                continue;
            }

            var body = Human(world);
            if (!body.Place(at))
            {
                failures.Add($"0x{data.Id:X8}: no placement at {at}");
                continue;
            }

            placed++;
            if (!CellIds.IsEnvCell(body.Position.CellId) || !body.HasContact)
            {
                failures.Add($"0x{data.Id:X8}: placed at {body.Position}, contact {body.HasContact}");
            }
        }

        output.WriteLine($"{coord}: {placed} rooms placed, {noFloor} without a floor, {smallFloor} whose floor has no room for a human at its centre");
        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        Assert.Empty(failures);
    }

    /// <summary>The distance from the floor's centroid to its nearest edge, in the floor's plane.</summary>
    private static float EdgeClearance(FloorPolygon floor)
    {
        var centre = floor.Centroid;
        var clearance = float.MaxValue;
        var vertices = floor.Vertices;
        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Length];
            var edge = b - a;
            var t = Math.Clamp(Vector3.Dot(centre - a, edge) / edge.LengthSquared(), 0f, 1f);
            clearance = MathF.Min(clearance, Vector3.Distance(centre, a + (edge * t)));
        }

        return clearance;
    }
}
