using System.Numerics;
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
/// The landscape as collision (retail CLandCell, CLandBlockStruct, 11.4186 PORTAL/ccell): terrain heights and planes on
/// the real dats around Holtburg, crossing landblock seams, water, and static objects across landblock edges.
/// </summary>
public sealed class LandscapeCollisionTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_test_human_has_the_human_Setups_spheres_and_step_heights()
    {
        Assert.True(Dats.TryGetPortal<DatReaderWriter.DBObjs.Setup>(0x02000001, out var setup));
        var human = Lifestone.Data.Models.ModelAssembly.FromSetup(0x02000001, setup);
        Assert.Equal(0.6f, human.StepUpHeight);
        Assert.Equal(1.5f, human.StepDownHeight);
        Assert.Equal([new(new Vector3(0, 0, 0.475f), 0.48f), new(new Vector3(0, 0, 1.35f), 0.48f)], human.Spheres.Select(s => new PhysSphere(s.Center, s.Radius)));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_sphere_dropped_on_each_of_the_576_land_cells_around_Holtburg_rests_on_the_terrain()
    {
        var world = new PhysicsWorld();
        var samplers = new Dictionary<LandblockCoord, TerrainSampler>();
        foreach (var coord in Around(LandblockCoord.Holtburg))
        {
            var terrain = Dats.GetLandblockTerrain(coord)!;
            world.Attach(LandblockCollisionBuilder.FromTerrain(terrain, Dats.TerrainRegion.LandHeightTable));
            samplers.Add(coord, new TerrainSampler(terrain, Dats.TerrainRegion.LandHeightTable));
        }

        var dropped = 0;
        var sunk = 0;
        foreach (var coord in Around(LandblockCoord.Holtburg))
        {
            for (var cx = 0; cx < 8; cx++)
            {
                for (var cy = 0; cy < 8; cy++)
                {
                    var x = (cx * 24f) + 11f;
                    var y = (cy * 24f) + 13f;
                    var ground = samplers[coord].GetHeight(x, y);
                    var body = Human(world);
                    var start = At(coord, x, y, ground + 3f);
                    Assert.True(body.Place(start), $"placement at {start}");
                    Assert.False(body.HasContact);
                    Assert.True(body.MoveTo(start.AddOffset(new Vector3(0, 0, -5f))), $"drop at {start}");

                    // The remaining fall slides down the contact plane (CTransition::adjust_offset), so the sphere rests
                    // where it ends: its centre one radius above the terrain there, less the water it may sink into. (The
                    // human's lower sphere reaches 5 mm below its origin: 0.475 - 0.48.)
                    var end = body.Position;
                    Assert.Equal(coord, end.Landblock);
                    var cell = (PhysLandCell)world.GetCell(end.CellId)!;
                    var expected = samplers[coord].GetHeight(end.Frame.Origin.X, end.Frame.Origin.Y) - cell.GetWaterDepth(end.Frame.Origin) + 0.48f;
                    var center = end.Frame.Origin.Z + 0.475f;
                    Assert.True(MathF.Abs(expected - center) <= 1e-4f, $"{start} rests at {end}, sphere centre {center}, expected {expected}");
                    Assert.True(body.HasContact, $"{start} ended without contact");
                    var plane = samplers[coord].Planes[samplers[coord].FindTerrainPolygon(end.Frame.Origin.X, end.Frame.Origin.Y)];
                    Assert.Equal(plane.Normal, body.ContactPlane.Normal);
                    Assert.Equal(plane.D, body.ContactPlane.D);
                    dropped++;
                    if (cell.WaterType != WaterType.NotWater)
                    {
                        sunk++;
                    }
                }
            }
        }

        output.WriteLine($"{dropped} drops, {sunk} in cells with water");
        Assert.Equal(576, dropped);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Walking_across_a_landblock_seam_changes_the_cell_and_wraps_the_local_origin()
    {
        var world = new PhysicsWorld();
        foreach (var coord in Around(LandblockCoord.Holtburg))
        {
            world.Attach(LandblockCollisionBuilder.FromTerrain(Dats.GetLandblockTerrain(coord)!, Dats.TerrainRegion.LandHeightTable));
        }

        // East from 0xA9B4 into 0xAAB4, then north into 0xAAB5.
        var body = Human(world);
        var holtburg = new TerrainSampler(Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!, Dats.TerrainRegion.LandHeightTable);
        Assert.True(body.Place(At(LandblockCoord.Holtburg, 190f, 186f, holtburg.GetHeight(190f, 186f) + 0.5f)));
        Assert.True(body.HasContact);
        for (var i = 0; i < 20; i++)
        {
            Assert.True(body.MoveBy(new Vector3(0.25f, 0f, 0f)));
        }

        Assert.Equal(new LandblockCoord(0xAA, 0xB4), body.Position.Landblock);
        Assert.InRange(body.Position.Frame.Origin.X, 195f - 192f - 0.01f, 195f - 192f + 0.01f);
        Assert.Equal(0x08u, body.Position.CellId & 0xFFFF);
        for (var i = 0; i < 40; i++)
        {
            Assert.True(body.MoveBy(new Vector3(0f, 0.25f, 0f)));
        }

        Assert.Equal(new LandblockCoord(0xAA, 0xB5), body.Position.Landblock);
        Assert.InRange(body.Position.Frame.Origin.Y, 196f - 192f - 0.01f, 196f - 192f + 0.01f);
        Assert.Equal(0x01u, body.Position.CellId & 0xFFFF);
        Assert.True(body.HasContact);
    }

    [Fact]
    public void A_static_straddling_a_landblock_edge_collides_from_the_neighbouring_landblock()
    {
        // A wall in block A whose box reaches 2 m into block B east of it (CLandCell::add_cell_block crosses the edge,
        // decision table row 6). A walker in B runs into it; with the ACE switch the shadow is dropped and it walks through.
        var west = new LandblockCoord(0x40, 0x40);
        var east = new LandblockCoord(0x41, 0x40);
        var wall = Box(west, 191f, 100f, 0f, new Vector3(6f, 1f, 3f));
        var blockA = Flat(west, wall);
        Assert.Contains(blockA.ForeignShadows, s => LandblockCoord.FromId(s.CellId) == east && s.FromCellBlock);

        foreach (var (compat, blocked) in new[] { (PhysicsCompat.Retail, true), (PhysicsCompat.Ace, false) })
        {
            var world = WorldOf(compat, blockA, Flat(east));
            var body = new PhysicsTestBody(world);
            Assert.True(body.Place(At(east, 1f, 96f, 0f)));
            for (var i = 0; i < 40; i++)
            {
                body.MoveBy(new Vector3(0f, 0.2f, 0f));
            }

            Assert.Equal(east, body.Position.Landblock);
            if (blocked)
            {
                Assert.InRange(body.Position.Frame.Origin.Y, 99.5f - 0.5f - 0.2f, 99.5f - 0.5f);
            }
            else
            {
                Assert.True(body.Position.Frame.Origin.Y > 103.9f, $"stopped at {body.Position}");
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void An_entirely_flooded_landblock_stops_a_walker_and_a_flooded_cell_lets_it_sink()
    {
        // The first landblock around Holtburg (rings outwards) that is entirely water and borders land.
        var (sea, shore) = FindSeaAndShore();
        output.WriteLine($"sea {sea}, shore {shore}");
        var world = new PhysicsWorld();
        world.Attach(LandblockCollisionBuilder.FromTerrain(Dats.GetLandblockTerrain(sea)!, Dats.TerrainRegion.LandHeightTable));
        var shoreBlock = LandblockCollisionBuilder.FromTerrain(Dats.GetLandblockTerrain(shore)!, Dats.TerrainRegion.LandHeightTable);
        world.Attach(shoreBlock);
        Assert.Equal(WaterType.EntirelyWater, world.GetLandblock(sea)!.WaterType);

        // CLandCell::find_env_collisions: nothing but the camera and missiles enters a landblock entirely under water.
        var direction = new Vector3((sea.X - shore.X) * 0.25f, (sea.Y - shore.Y) * 0.25f, 0f);
        var shoreSampler = new TerrainSampler(Dats.GetLandblockTerrain(shore)!, Dats.TerrainRegion.LandHeightTable);
        var x = sea.X > shore.X ? 186f : sea.X < shore.X ? 6f : 96f;
        var y = sea.Y > shore.Y ? 186f : sea.Y < shore.Y ? 6f : 96f;
        var body = Human(world);
        Assert.True(body.Place(At(shore, x, y, shoreSampler.GetHeight(x, y) + 0.5f)));
        for (var i = 0; i < 60; i++)
        {
            body.MoveBy(direction);
        }

        Assert.Equal(shore, body.Position.Landblock);

        // In the shore landblock, a cell under water lets the sphere sink 0.9 m below the terrain (CObjCell::get_water_depth).
        var flooded = shoreBlock.LandCells.FirstOrDefault(c => c.WaterType == WaterType.EntirelyWater);
        if (flooded is not null)
        {
            var cx = (flooded.CellX * 24f) + 12f;
            var cy = (flooded.CellY * 24f) + 12f;
            var ground = shoreSampler.GetHeight(cx, cy);
            var swimmer = Human(world);
            Assert.True(swimmer.Place(At(shore, cx, cy, ground + 0.5f)));
            Assert.InRange(swimmer.Position.Frame.Origin.Z + 0.475f, ground - 0.9f + 0.48f - 1e-3f, ground - 0.9f + 0.48f + 1e-3f);
        }
    }

    /// <summary>The landblock and its eight neighbours.</summary>
    private static IEnumerable<LandblockCoord> Around(LandblockCoord center)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                yield return center.Offset(dx, dy)!.Value;
            }
        }
    }

    private (LandblockCoord Sea, LandblockCoord Shore) FindSeaAndShore()
    {
        for (var ring = 1; ring < 40; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || LandblockCoord.Holtburg.Offset(dx, dy) is not { } coord)
                    {
                        continue;
                    }

                    var terrain = Dats.GetLandblockTerrain(coord);
                    if (terrain is null || LandblockCollision.CalcWater(terrain.TerrainWords).Block != WaterType.EntirelyWater)
                    {
                        continue;
                    }

                    foreach (var (nx, ny) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    {
                        if (coord.Offset(nx, ny) is { } neighbour
                            && Dats.GetLandblockTerrain(neighbour) is { } other
                            && LandblockCollision.CalcWater(other.TerrainWords).Block != WaterType.EntirelyWater)
                        {
                            return (coord, neighbour);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("No entirely flooded landblock borders land within 40 landblocks of Holtburg.");
    }
}
