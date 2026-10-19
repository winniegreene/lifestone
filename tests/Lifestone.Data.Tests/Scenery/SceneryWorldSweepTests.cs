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

        // C2 (q5): one landblock row (fixed x) per work item. The generator and its data source are thread-safe
        // (concurrent scene/bounds caches, dat reads under DatSet's lock; the parallel half below already relies on it).
        // Counters merge per row; the digest folds the rows in x, y order afterwards, so it equals the sequential one.
        var rows = new List<ScenePlacement>[0xFF];
        var rowTotals = new long[0xFF][];
        var rowSetups = new long[0xFF];
        var rowBaseZ = new long[0xFF];
        var rowObjects = new HashSet<uint>[0xFF];
        ParallelSweep.For(0xFF, x =>
        {
            var row = new List<ScenePlacement>();
            var totalsHere = new long[totals.Length];
            var objectsHere = new HashSet<uint>();
            long setupsHere = 0, baseZHere = 0;
            for (var y = 0; y <= 0xFE; y++)
            {
                var coord = new LandblockCoord((byte)x, (byte)y);
                var landblock = data.GetLandblock(coord);
                Assert.NotNull(landblock); // all 255 x 255 landblocks exist at end of retail
                var result = generator.Generate(landblock);
                foreach (var stage in Enum.GetValues<SceneryStage>())
                {
                    totalsHere[(int)stage] += result.Funnel[stage];
                }

                if (result.Placements.Count == 0)
                {
                    continue;
                }

                var sampler = new TerrainSampler(landblock.Terrain, data.Region.LandHeightTable);
                var buildingCells = generator.BuildingCells(landblock);
                foreach (var p in result.Placements)
                {
                    row.Add(p);
                    objectsHere.Add(p.ObjectId);
                    setupsHere += p.IsSetup ? 1 : 0;

                    // In the block, in the stated cell, not in a building cell, off the road.
                    Assert.InRange(p.Origin.X, 0f, 191.99999f);
                    Assert.InRange(p.Origin.Y, 0f, 191.99999f);
                    Assert.Equal(TerrainSampler.CellIndexOf(p.Origin.X), p.CellX);
                    Assert.Equal(TerrainSampler.CellIndexOf(p.Origin.Y), p.CellY);
                    Assert.False(buildingCells[p.CellIndex], $"landblock {coord}: placement in a building cell");
                    Assert.False(SceneryRoad.OnRoad(landblock.Terrain, p.Origin.X, p.Origin.Y), $"landblock {coord}: placement on the road");

                    // z is exactly the terrain plane height (never ground + BaseLoc.Z).
                    Assert.Equal(sampler.GetHeight(p.Origin.X, p.Origin.Y), p.Origin.Z);
                    var obj = data.GetScene(p.SceneId)!.Objects[p.ObjectIndex];
                    baseZHere += obj.BaseOrigin.Z != 0f ? 1 : 0;

                    // Slope window, scale range, unit orientation.
                    Assert.True(SceneryGenerator.CheckSlope(obj, sampler.GetNormal(p.Origin.X, p.Origin.Y).Z), $"landblock {coord}: slope window");
                    Assert.InRange(p.Scale, Math.Min(obj.MinScale, obj.MaxScale) * 0.99999f, Math.Max(obj.MinScale, obj.MaxScale) * 1.00001f);
                    Assert.InRange(p.Orientation.Length(), 0.999f, 1.001f);
                    Assert.Equal(p.ObjectId, obj.ObjectId);
                }
            }

            rows[x] = row;
            rowTotals[x] = totalsHere;
            rowSetups[x] = setupsHere;
            rowBaseZ[x] = baseZHere;
            rowObjects[x] = objectsHere;
        });

        for (var x = 0; x < rows.Length; x++)
        {
            for (var st = 0; st < totals.Length; st++)
            {
                totals[st] += rowTotals[x][st];
            }

            setups += rowSetups[x];
            withBaseZ += rowBaseZ[x];
            objectIds.UnionWith(rowObjects[x]);
            placed += rows[x].Count;
            foreach (var p in rows[x])
            {
                digest.Add(p);
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
