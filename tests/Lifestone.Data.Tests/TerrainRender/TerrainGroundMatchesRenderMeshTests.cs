using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>
/// The ground the game walks on is the ground it draws: the terrain layer registers each streamed landblock's
/// <see cref="TerrainSampler"/> (retail <c>CLandCell::find_terrain_poly</c> + <c>Plane::set_height</c>) with the game's
/// ground-height service, and draws <see cref="TerrainRenderMesh"/>. Both must describe the same 128 triangles, so walk
/// mode's eye height follows the drawn TexMerge surface (the shader's 0.01 m <c>zFightTerrainAdjust</c> lowering aside).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class TerrainGroundMatchesRenderMeshTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// Metres. The sampler's planes are floats (retail x87, double intermediates); the mesh is interpolated in double here.
    /// Measured worst case 0.036 mm (0x7AC2, 271 m up): float resolution at that height.
    /// </summary>
    private const double HeightTolerance = 1e-4;

    [RequiresDatsFact]
    public void Sampler_heights_equal_the_render_mesh_surface_everywhere()
    {
        var dats = fixture.Dats;
        var merge = new TerrainTexMerge(dats.TerrainAtlas);
        var rng = new Random(20261010);
        var blocks = TerrainRenderMeshTests.Blocks
            .Concat(TerrainTestData.LandblocksAround(dats, LandblockCoord.Holtburg, 1).Select(t => t.Coord))
            .Distinct()
            .ToArray();
        Assert.Equal(13, blocks.Length);
        var compared = 0;
        var worst = (Diff: 0.0, What: "none");
        foreach (var coord in blocks)
        {
            var terrain = dats.GetLandblockTerrain(coord)!;
            var mesh = TerrainRenderMesh.Build(terrain, dats.TerrainRegion, merge);
            var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
            Assert.Equal(sampler.Planes.Count * 3, mesh.Indices.Length);

            // Every grid corner: the drawn vertex height is the sampled height.
            for (var v = 0; v < TerrainRenderMesh.VertexCount; v++)
            {
                var p = mesh.Positions[v];
                var x = Math.Min(p.X, MathF.BitDecrement(TerrainConstants.LandblockSize));
                var y = Math.Min(p.Y, MathF.BitDecrement(TerrainConstants.LandblockSize));
                if (x == p.X && y == p.Y && Math.Abs(sampler.GetHeight(x, y) - p.Z) > worst.Diff)
                {
                    worst = (Math.Abs(sampler.GetHeight(x, y) - p.Z), $"{coord} vertex {v} at ({x}, {y}): drawn {p.Z:0.000000} m, sampled {sampler.GetHeight(x, y):0.000000} m");
                }
            }

            // Random points: the render triangle under the point, interpolated, against the sampler.
            for (var i = 0; i < 3000; i++)
            {
                var x = (float)(rng.NextDouble() * TerrainConstants.LandblockSize);
                var y = (float)(rng.NextDouble() * TerrainConstants.LandblockSize);
                var drawn = RenderHeight(mesh, x, y, out var triangle);
                var walked = sampler.GetHeight(x, y);
                if (Math.Abs(drawn - walked) > worst.Diff)
                {
                    worst = (Math.Abs(drawn - walked), $"{coord} ({x}, {y}): render triangle {triangle} gives {drawn:0.000000} m, the sampler {walked:0.000000} m");
                }

                // Same triangle numbering too (2 × cell + t), away from the shared diagonal where either may win.
                if (DistanceToEdges(mesh, triangle, x, y) > 1e-3)
                {
                    Assert.Equal(triangle, sampler.FindTerrainPolygon(x, y));
                }

                compared++;
            }
        }

        Assert.Equal(blocks.Length * 3000, compared);
        Assert.True(worst.Diff <= HeightTolerance, $"largest difference {worst.Diff:0.000000} m at {worst.What}");
    }

    /// <summary>Height of the render mesh at (x, y): barycentric interpolation in the triangle whose footprint holds it.</summary>
    private static double RenderHeight(TerrainRenderMesh mesh, float x, float y, out int triangle)
    {
        var best = double.NegativeInfinity;
        var found = -1;
        var height = double.NaN;
        for (var t = 0; t < mesh.Indices.Length / 3; t++)
        {
            var (a, b, c) = Corners(mesh, t);
            var (w0, w1, w2) = Barycentric(a, b, c, x, y);
            var inside = Math.Min(w0, Math.Min(w1, w2));
            if (inside > best)
            {
                best = inside;
                found = t;
                height = (w0 * a.Z) + (w1 * b.Z) + (w2 * c.Z);
            }
        }

        Assert.True(best >= -1e-6, $"({x}, {y}) is under no render triangle");
        triangle = found;
        return height;
    }

    private static double DistanceToEdges(TerrainRenderMesh mesh, int triangle, float x, float y)
    {
        var (a, b, c) = Corners(mesh, triangle);
        var (w0, w1, w2) = Barycentric(a, b, c, x, y);
        return Math.Min(w0, Math.Min(w1, w2)) * TerrainConstants.CellSize;
    }

    private static (Vector3 A, Vector3 B, Vector3 C) Corners(TerrainRenderMesh mesh, int triangle) =>
        (mesh.Positions[mesh.Indices[triangle * 3]], mesh.Positions[mesh.Indices[(triangle * 3) + 1]], mesh.Positions[mesh.Indices[(triangle * 3) + 2]]);

    private static (double W0, double W1, double W2) Barycentric(Vector3 a, Vector3 b, Vector3 c, double x, double y)
    {
        var det = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
        var w0 = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / det;
        var w1 = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / det;
        return (w0, w1, 1 - w0 - w1);
    }
}
