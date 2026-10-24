using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>
/// Conformance of the retail scenery funnel on the user's dats, with each stage re-derived independently
/// (TerrainMeshBuilder geometry, raw LandBlockInfo buildings, 64-bit hash transcription, the ACE oracle).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class SceneryConformanceTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly LandblockCoord A9B3 = new(0xA9, 0xB3);

    [RequiresDatsFact]
    public void Landblock_A9B3_reproduces_the_research_funnel_and_every_later_stage()
    {
        var generator = SceneryGenerator.FromDats(fixture.Dats);
        var result = generator.Generate(A9B3, includeCandidates: true);
        var funnel = result.Funnel;
        output.WriteLine($"{A9B3}: {funnel}");

        // 64 of the 81 vertices reach a scene; their scenes hold 328 ObjectDescs. 87 fail the frequency test and 64 are
        // weenies (server-spawned), leaving 177 candidates.
        Assert.Equal(64, funnel.VerticesWithScenes);
        Assert.Equal(328, funnel.ObjectsEvaluated);
        Assert.Equal(87, funnel[SceneryStage.FrequencyRejected]);
        Assert.Equal(64, funnel[SceneryStage.Weenie]);
        Assert.Equal(177, funnel.Candidates);

        // Research funnel (scenery_check.py): 30 displaced outside the block, 5 on the road, 142 survivors.
        Assert.Equal(30, funnel[SceneryStage.OutOfBlock]);
        Assert.Equal(5, funnel[SceneryStage.OnRoad]);
        Assert.Equal(142, funnel.AfterRoad);

        // Later stages: 3 land in a building's cell, 3 fail the slope test, every object exists and fits in the block.
        Assert.Equal(3, funnel[SceneryStage.Building]);
        Assert.Equal(0, funnel[SceneryStage.NoTerrainPolygon]);
        Assert.Equal(3, funnel[SceneryStage.Slope]);
        Assert.Equal(0, funnel[SceneryStage.ObjectUnavailable]);
        Assert.Equal(0, funnel[SceneryStage.NotWithinBlock]);
        Assert.Equal(136, funnel.Placed);
        Assert.Equal(136, result.Placements.Count);

        ExplainEveryStage(generator, result);

        // The independent ACE oracle stops every candidate at the same stage.
        var ace = new AceSceneryOracle(fixture.Dats).GetLandScenes(A9B3);
        var mine = result.Candidates!.Where(c => c.Stage is not (SceneryStage.FrequencyRejected or SceneryStage.Weenie)).ToArray();
        Assert.Equal(mine.Select(c => (c.VertexX, c.VertexY, c.ObjectIndex, c.Stage)), ace.Select(c => (c.VertexX, c.VertexY, c.ObjectIndex, c.Stage)));
    }

    [RequiresDatsFact]
    public void Holtburg_town_block_has_no_scenery_candidates()
    {
        // docs/research/rendering.md §4: town block 0xA9B4 gives 0. All 46 ObjectDescs its scenes offer are weenies.
        var result = SceneryGenerator.FromDats(fixture.Dats).Generate(LandblockCoord.Holtburg, includeCandidates: true);
        Assert.Equal(0, result.Funnel.Candidates);
        Assert.Empty(result.Placements);
        Assert.All(result.Candidates!, c => Assert.Equal(SceneryStage.Weenie, c.Stage));
    }

    [RequiresDatsFact]
    public void Generation_is_deterministic_across_instances_and_threads()
    {
        var coords = new List<LandblockCoord>();
        for (var x = 0xA6; x <= 0xAC; x++)
        {
            for (var y = 0xB1; y <= 0xB7; y++)
            {
                coords.Add(new LandblockCoord((byte)x, (byte)y));
            }
        }

        var first = SceneryGenerator.FromDats(fixture.Dats);
        var sequential = coords.Select(c => first.Generate(c).Placements.ToArray()).ToArray();

        var second = SceneryGenerator.FromDats(fixture.Dats);
        var parallel = new ScenePlacement[coords.Count][];
        Parallel.For(0, coords.Count, i => parallel[i] = second.Generate(coords[i]).Placements.ToArray());

        for (var i = 0; i < coords.Count; i++)
        {
            Assert.Equal(sequential[i], parallel[i]);
            Assert.Equal(sequential[i], first.Generate(coords[i]).Placements.ToArray());
        }

        Assert.True(sequential.Sum(p => p.Length) > 3000);
    }

    [RequiresDatsFact]
    public void Missing_landblocks_produce_no_scenery()
    {
        var result = SceneryGenerator.FromDats(fixture.Dats).Generate(new LandblockCoord(0xFF, 0xFF));
        Assert.Empty(result.Placements);
        Assert.Equal(0, result.Funnel.ObjectsEvaluated);
    }

    /// <summary>Re-derives the reason for every stage of <paramref name="result"/> without the generator's helpers.</summary>
    private void ExplainEveryStage(SceneryGenerator generator, SceneryResult result)
    {
        var dats = fixture.Dats;
        var terrain = dats.GetLandblockTerrain(result.Coord)!;
        var mesh = TerrainMeshBuilder.Build(terrain, dats.TerrainRegion);

        // Building cells straight from LandBlockInfo: the cell of each building origin.
        Assert.True(dats.TryGetCell<LandBlockInfo>(result.Coord.LandBlockInfoId, out var info));
        var buildingCells = info.Buildings
            .Select(b => ((int)Math.Floor(b.Frame.Origin.X / 24.0) * 8) + (int)Math.Floor(b.Frame.Origin.Y / 24.0))
            .ToHashSet();
        output.WriteLine($"buildings: {string.Join(", ", info.Buildings.Select(b => $"0x{b.ModelId:X8}@{b.Frame.Origin}"))}");

        var placements = new Queue<ScenePlacement>(result.Placements);
        foreach (var c in result.Candidates!)
        {
            var obj = generator.Data.GetScene(c.SceneId)!.Objects[c.ObjectIndex];
            var gx = (long)((result.Coord.X * 8) + c.VertexX);
            var gy = (long)((result.Coord.Y * 8) + c.VertexY);
            var m = ((1360117743L * gx * gy) + 1888038839L) & 0xFFFFFFFFL;
            var noise = (((-1109124029L * gx) + (1813693831L * gy) - ((m * (23399 + c.ObjectIndex)) & 0xFFFFFFFFL)) & 0xFFFFFFFFL) / 4294967296.0;
            var p = c.Position;
            var cell = p.X is >= 0 and < 192 && p.Y is >= 0 and < 192 ? ((int)(p.X / 24) * 8) + (int)(p.Y / 24) : -1;
            var line = $"v({c.VertexX},{c.VertexY}) scene 0x{c.SceneId:X8}#{c.ObjectIndex} 0x{c.ObjectId:X8} {c.Stage} at {p}";

            switch (c.Stage)
            {
                case SceneryStage.FrequencyRejected:
                    Assert.True(noise >= obj.Frequency, line);
                    continue;
                case SceneryStage.Weenie:
                    Assert.True(noise < obj.Frequency && obj.WeenieObj != 0, line);
                    continue;
            }

            Assert.True(noise < obj.Frequency && obj.WeenieObj == 0, line);
            output.WriteLine(line);
            switch (c.Stage)
            {
                case SceneryStage.OutOfBlock:
                    Assert.Equal(-1, cell);
                    break;
                case SceneryStage.OnRoad:
                    var (cx, cy) = (cell / 8, cell % 8);
                    Assert.True(terrain.Road(cx, cy) + terrain.Road(cx + 1, cy) + terrain.Road(cx, cy + 1) + terrain.Road(cx + 1, cy + 1) > 0, line);
                    break;
                case SceneryStage.Building:
                    Assert.Contains(cell, buildingCells);
                    break;
                case SceneryStage.Slope:
                    var normal = MeshNormal(mesh, p.X, p.Y);
                    Assert.True(normal.Z < obj.MinSlope || normal.Z > obj.MaxSlope, $"{line}: n.z {normal.Z} in [{obj.MinSlope}, {obj.MaxSlope}]");
                    break;
                case SceneryStage.Placed:
                    Assert.DoesNotContain(cell, buildingCells);
                    var placement = placements.Dequeue();
                    Assert.Equal(c.ObjectId, placement.ObjectId);
                    Assert.Equal(cell, placement.CellIndex);
                    Assert.Equal(p, placement.Origin);
                    Assert.True(Math.Abs(MeshHeight(mesh, p.X, p.Y) - p.Z) < 2e-3, $"{line}: not on the terrain");
                    var n = MeshNormal(mesh, p.X, p.Y);
                    Assert.InRange(n.Z, obj.MinSlope - 1e-6f, obj.MaxSlope + 1e-6f);
                    Assert.InRange(placement.Scale, Math.Min(obj.MinScale, obj.MaxScale) * 0.99999f, Math.Max(obj.MinScale, obj.MaxScale) * 1.00001f);
                    break;
                default:
                    Assert.Fail($"unexpected stage {line}");
                    break;
            }
        }

        Assert.Empty(placements);
    }

    private static (Vector3 A, Vector3 B, Vector3 C) MeshTriangle(TerrainMesh mesh, float x, float y)
    {
        var cx = (int)(x / 24f);
        var cy = (int)(y / 24f);
        var cell = (cx * 8) + cy;
        for (var t = 0; t < 2; t++)
        {
            var i = (cell * 6) + (t * 3);
            var (a, b, c) = (mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]]);
            if (Inside(a, b, c, x, y))
            {
                return (a, b, c);
            }
        }

        throw new InvalidOperationException($"({x}, {y}) in no triangle");

        static bool Inside(Vector3 a, Vector3 b, Vector3 c, double x, double y) =>
            Cross(a, b, x, y) >= 0 && Cross(b, c, x, y) >= 0 && Cross(c, a, x, y) >= 0;

        static double Cross(Vector3 p, Vector3 q, double x, double y) => ((q.X - (double)p.X) * (y - p.Y)) - ((q.Y - (double)p.Y) * (x - p.X));
    }

    private static Vector3 MeshNormal(TerrainMesh mesh, float x, float y)
    {
        var (a, b, c) = MeshTriangle(mesh, x, y);
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }

    private static double MeshHeight(TerrainMesh mesh, float x, float y)
    {
        var (a, b, c) = MeshTriangle(mesh, x, y);
        var n = Vector3.Cross(b - a, c - a);
        return a.Z - (((n.X * (x - (double)a.X)) + (n.Y * (y - (double)a.Y))) / n.Z);
    }
}
