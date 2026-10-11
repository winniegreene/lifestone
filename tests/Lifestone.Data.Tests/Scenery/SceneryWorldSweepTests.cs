using System.Numerics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>Whole-world scenery: every landblock generates, every placement satisfies the retail invariants.</summary>
public sealed class SceneryWorldSweepTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_landblock_generates_and_every_placement_obeys_the_retail_rules()
    {
        var generator = SceneryGenerator.FromDats(fixture.Dats);
        var data = generator.Data;
        var totals = new long[Enum.GetValues<SceneryStage>().Length];
        long placed = 0, withBaseZ = 0, setups = 0;
        var objectIds = new HashSet<uint>();
        var digest = new HashCode();

        for (var x = 0; x <= 0xFE; x++)
        {
            for (var y = 0; y <= 0xFE; y++)
            {
                var coord = new LandblockCoord((byte)x, (byte)y);
                var landblock = data.GetLandblock(coord);
                Assert.NotNull(landblock); // all 255 x 255 landblocks exist at end of retail
                var result = generator.Generate(landblock);
                foreach (var stage in Enum.GetValues<SceneryStage>())
                {
                    totals[(int)stage] += result.Funnel[stage];
                }

                if (result.Placements.Count == 0)
                {
                    continue;
                }

                var sampler = new TerrainSampler(landblock.Terrain, data.Region.LandHeightTable);
                var buildingCells = generator.BuildingCells(landblock);
                foreach (var p in result.Placements)
                {
                    placed++;
                    objectIds.Add(p.ObjectId);
                    setups += p.IsSetup ? 1 : 0;
                    digest.Add(p);

                    // In the block, in the stated cell, not in a building cell, off the road.
                    Assert.InRange(p.Origin.X, 0f, 191.99999f);
                    Assert.InRange(p.Origin.Y, 0f, 191.99999f);
                    Assert.Equal(TerrainSampler.CellIndexOf(p.Origin.X), p.CellX);
                    Assert.Equal(TerrainSampler.CellIndexOf(p.Origin.Y), p.CellY);
                    Assert.False(buildingCells[p.CellIndex]);
                    Assert.False(SceneryRoad.OnRoad(landblock.Terrain, p.Origin.X, p.Origin.Y));

                    // z is exactly the terrain plane height (never ground + BaseLoc.Z).
                    Assert.Equal(sampler.GetHeight(p.Origin.X, p.Origin.Y), p.Origin.Z);
                    var obj = data.GetScene(p.SceneId)!.Objects[p.ObjectIndex];
                    withBaseZ += obj.BaseOrigin.Z != 0f ? 1 : 0;

                    // Slope window, scale range, unit orientation.
                    Assert.True(SceneryGenerator.CheckSlope(obj, sampler.GetNormal(p.Origin.X, p.Origin.Y).Z));
                    Assert.InRange(p.Scale, Math.Min(obj.MinScale, obj.MaxScale) * 0.99999f, Math.Max(obj.MinScale, obj.MaxScale) * 1.00001f);
                    Assert.InRange(p.Orientation.Length(), 0.999f, 1.001f);
                    Assert.Equal(p.ObjectId, obj.ObjectId);
                }
            }
        }

        output.WriteLine($"placed {placed} ({setups} Setups, {objectIds.Count} distinct objects, {withBaseZ} with BaseLoc.Z != 0); " +
            string.Join(", ", Enum.GetValues<SceneryStage>().Select(st => $"{st} {totals[(int)st]}")));

        Assert.Equal(3786622, placed);
        Assert.Equal(0, totals[(int)SceneryStage.NoTerrainPolygon]);
        Assert.Equal(0, totals[(int)SceneryStage.ObjectUnavailable]);
        Assert.Equal(13165, totals[(int)SceneryStage.Building]);
        Assert.Equal(861409, totals[(int)SceneryStage.Slope]);
        Assert.Equal(3619, totals[(int)SceneryStage.NotWithinBlock]);

        // Parallel generation with a fresh cache yields the same placements in the same order.
        var parallelGenerator = SceneryGenerator.FromDats(fixture.Dats);
        var coords = Enumerable.Range(0, 255 * 255).Select(i => new LandblockCoord((byte)(i / 255), (byte)(i % 255))).ToArray();
        var results = new IReadOnlyList<ScenePlacement>[coords.Length];
        Parallel.For(0, coords.Length, i => results[i] = parallelGenerator.Generate(coords[i]).Placements);
        var parallelDigest = new HashCode();
        foreach (var list in results)
        {
            foreach (var p in list)
            {
                parallelDigest.Add(p);
            }
        }

        Assert.Equal(digest.ToHashCode(), parallelDigest.ToHashCode());
    }
}
