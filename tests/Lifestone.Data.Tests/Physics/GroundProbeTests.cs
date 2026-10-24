using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// <see cref="IGroundProbe"/> on <see cref="PhysicsWorld"/> (m2-plan PH8, owner decision D24): the ground under a remote
/// object, found by retail's step down from <see cref="PhysicsWorld.GroundProbeRise"/> above it to
/// <see cref="PhysicsWorld.GroundProbeDrop"/> below.
/// <remarks>
/// <c>Probing_allocates_nothing</c> measures this thread's allocations, so the class runs in the non-parallel
/// <see cref="Lifestone.Client.Tests.AllocationMeasurementCollection"/> (other classes running at the same time trigger GCs and runtime
/// work that land inside the measured window), and a window with a GC inside is not judged.
/// </remarks>
[Collection(Lifestone.Client.Tests.AllocationMeasurementCollection.Name)]
public sealed class GroundProbeTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const float Radius = 0.48f;

    private DatSet Dats => fixture.Dats;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(-0.3f)]
    [InlineData(0.59f)]
    [InlineData(-0.59f)]
    public void On_flat_terrain_the_ground_is_the_terrain_within_the_window(float offset)
    {
        var world = WorldOf(Flat(Block));
        Assert.True(world.TryGroundZ(At(Block, 100f, 100f, offset), Radius, out var z, out var contact));
        Assert.Equal(0f, z, 4);
        Assert.Equal(Vector3.UnitZ, contact.Normal);
        Assert.Equal(0f, contact.D, 4);
    }

    [Fact]
    public void Below_the_window_retails_step_down_allowance_still_finds_the_ground()
    {
        // CTransition::step_down accepts a rise of up to 1.1 steps (OBJECTINFO::validate_walkable's walk interpolation
        // down to -0.1), so an object up to 0.72 m below the ground finds it: GroundProbeDrop plus a tenth of the 1.2 m step.
        var world = WorldOf(Flat(Block));
        Assert.True(world.TryGroundZ(At(Block, 100f, 100f, -0.7f), Radius, out var z, out _));
        Assert.Equal(0f, z, 4);
    }

    [Theory]
    [InlineData(0.7f)]
    [InlineData(-0.75f)]
    public void Ground_outside_the_window_is_not_found(float offset)
    {
        var world = WorldOf(Flat(Block));
        Assert.False(world.TryGroundZ(At(Block, 100f, 100f, offset), Radius, out var z, out var contact));
        Assert.Equal(0f, z);
        Assert.Equal(default, contact);
    }

    [Fact]
    public void A_landblock_that_is_not_attached_or_a_bad_sphere_answers_false()
    {
        var world = WorldOf(Flat(Block));
        Assert.False(world.TryGroundZ(At(new LandblockCoord(0x41, 0x41), 10f, 10f, 0f), Radius, out _, out _));
        Assert.False(world.TryGroundZ(At(Block, 100f, 100f, 0f), 0f, out _, out _));
        Assert.False(world.TryGroundZ(At(Block, 100f, 100f, float.NaN), Radius, out _, out _));
    }

    [Fact]
    public void On_a_slope_the_sphere_rests_on_the_terrain_plane_under_it()
    {
        // Heights rise 1 m per vertex eastward: a uniform slope of 1 in 24.
        var terrain = TerrainOf(Block, (x, _) => (byte)x);
        var world = WorldOf(LandblockCollisionBuilder.FromTerrain(terrain, Heights(i => i)));
        var sampler = new TerrainSampler(terrain, Heights(i => i));
        foreach (var x in new[] { 30f, 61.5f, 140f })
        {
            var ground = sampler.GetHeight(x, 77f);
            Assert.True(world.TryGroundZ(At(Block, x, 77f, ground + 0.2f), Radius, out var z, out var contact));
            output.WriteLine($"x {x}: terrain {ground:F4}, probe {z:F4}");

            // The terrain test sets the sphere's lowest point on the plane under its centre (validate_walkable).
            Assert.Equal(ground, z, 3);
            Assert.True(contact.Normal.Z > PhysicsGlobals.FloorZ);
            Assert.Equal(0f, Plane.DotCoordinate(contact, new Vector3(x, 77f, ground)), 3);
        }
    }

    [Fact]
    public void The_top_of_a_box_within_reach_is_ground_and_one_above_it_is_not()
    {
        var low = Box(Block, 100f, 100f, 0f, new Vector3(4f, 4f, 0.4f));
        var high = Box(Block, 120f, 100f, 0f, new Vector3(4f, 4f, 2f), id: 0x0100F001);
        var world = WorldOf(Flat(Block, low, high));

        Assert.True(world.TryGroundZ(At(Block, 100f, 100f, 0.1f), Radius, out var z, out var contact));
        Assert.Equal(0.4f, z, 3);
        Assert.Equal(Vector3.UnitZ, contact.Normal);

        // On the 2 m box, from 2.2 m: its top. From the ground beside it: the ground.
        Assert.True(world.TryGroundZ(At(Block, 120f, 100f, 2.2f), Radius, out z, out _));
        Assert.Equal(2f, z, 3);
        Assert.True(world.TryGroundZ(At(Block, 124f, 100f, 0f), Radius, out z, out _));
        Assert.Equal(0f, z, 3);
    }

    [Fact]
    public void The_answer_is_in_the_asking_positions_landblock_space()
    {
        // A probe near the east edge whose sphere centre lies in the next landblock east.
        var east = Block.Offset(1, 0)!.Value;
        var world = WorldOf(Flat(Block), LandblockCollisionBuilder.FromTerrain(TerrainOf(east, (_, _) => 2), Heights(i => i)));
        var at = new CellFrame(LandCellAt(Block, 191.9f, 50f), new AcFrame(new Vector3(192.5f, 50f, 2.1f), Quaternion.Identity));
        Assert.True(world.TryGroundZ(at, Radius, out var z, out var contact));
        Assert.Equal(2f, z, 3);
        Assert.Equal(-2f, contact.D, 3); // z = 2 in Block's space as well
    }

    [Fact]
    public void Probing_allocates_nothing()
    {
        var world = WorldOf(Flat(Block, Box(Block, 100f, 100f, 0f, new Vector3(4f, 4f, 0.4f))));
        var at = At(Block, 100f, 100f, 0.1f);
        for (var i = 0; i < 100; i++)
        {
            world.TryGroundZ(at, Radius, out _, out _); // warm up (JIT, the first query's lazy state)
        }

        var allocated = 0L;
        var gcWindows = 0;
        for (var window = 0; window < 200; window++)
        {
            var open = Lifestone.Client.Tests.AllocationWindow.Open();
            for (var i = 0; i < 100; i++)
            {
                world.TryGroundZ(at, Radius, out _, out _);
            }

            var (bytes, gcInside) = open.Close();
            if (gcInside)
            {
                gcWindows++;
                continue;
            }

            allocated += bytes;
        }

        Assert.True(gcWindows <= 20, $"{gcWindows} of 200 windows had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"probing allocated {allocated} B over 200 windows of 100 probes");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Around_Holtburg_the_probe_finds_the_terrain_and_the_floors_of_its_rooms()
    {
        var builder = new LandblockCollisionBuilder(Dats);
        var world = new PhysicsWorld();
        world.Attach(builder.Build(LandblockCoord.Holtburg));
        var terrain = new TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);

        // Terrain: a grid of points (the probe agrees with the terrain sampler wherever no object stands).
        var agreed = 0;
        for (var x = 6f; x < 190f; x += 23f)
        {
            for (var y = 6f; y < 190f; y += 23f)
            {
                var ground = terrain.GetHeight(x, y);
                if (world.TryGroundZ(At(LandblockCoord.Holtburg, x, y, ground + 0.1f), Radius, out var z, out _)
                    && MathF.Abs(z - ground) < 1e-3f)
                {
                    agreed++;
                }
            }
        }

        output.WriteLine($"{agreed} of 81 terrain points agree");
        Assert.True(agreed > 60, $"only {agreed} of 81 terrain points agree");

        // Rooms: a point over the centre of each room's largest floor rests on a floor of that room.
        var block = world.GetLandblock(LandblockCoord.Holtburg)!;
        var floors = 0;
        var rooms = 0;
        foreach (var cell in block.EnvCells.Values)
        {
            if (cell.Physics is null)
            {
                continue;
            }

            var floor = cell.Physics.Polygons.Where(p => p.Plane.Normal.Z > 0.99f).OrderByDescending(p => p.NumPoints).FirstOrDefault();
            if (floor is null)
            {
                continue;
            }

            var centre = floor.Vertices.ToArray().Aggregate(Vector3.Zero, (s, v) => s + v) / floor.NumPoints;
            var point = cell.Position.Frame.LocalToGlobal(centre);
            if (!cell.PointInCell(point + new Vector3(0, 0, 0.5f)))
            {
                continue;
            }

            // Furniture standing on the floor's centre is ground too, so a room may answer higher.
            rooms++;
            if (world.TryGroundZ(new CellFrame(cell.Id, new AcFrame(point + new Vector3(0, 0, 0.2f), Quaternion.Identity)), Radius, out var z, out var contact)
                && MathF.Abs(z - point.Z) < 0.01f && contact.Normal.Z > 0.99f)
            {
                floors++;
            }
        }

        output.WriteLine($"{floors} of {rooms} room floors found");
        Assert.True(floors > 20);
        Assert.True(floors >= rooms * 0.8, $"only {floors} of {rooms} room floors found");
    }
}
