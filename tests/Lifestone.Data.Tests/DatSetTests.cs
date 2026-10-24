using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.Tests;

/// <summary>Checks against the user's end-of-retail dats; expected values come from docs/research/local-scout.md.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class DatSetTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>
    /// Holtburg (0xA9B4FFFF) height indices exactly as printed in docs/research/local-scout.md:
    /// one row per y, north row (y = 8) first; columns are x = 0..8 west to east.
    /// </summary>
    private static readonly byte[][] HoltburgHeightsNorthFirst =
    [
        [20, 23, 25, 25, 25, 24, 24, 20, 15], // y = 8
        [25, 33, 33, 33, 33, 33, 33, 27, 24], // y = 7
        [29, 33, 33, 33, 33, 33, 33, 33, 32], // y = 6
        [33, 33, 33, 33, 33, 33, 33, 33, 32], // y = 5
        [35, 35, 35, 37, 40, 40, 33, 33, 32], // y = 4
        [39, 39, 39, 41, 40, 40, 40, 39, 36], // y = 3
        [39, 39, 39, 47, 47, 47, 47, 47, 41], // y = 2
        [42, 42, 42, 47, 47, 47, 47, 47, 45], // y = 1
        [45, 45, 45, 47, 47, 47, 47, 47, 48], // y = 0
    ];

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void DatSet_opens_end_of_retail_portal_and_cell()
    {
        Assert.Equal(1024u, (uint)Dats.Portal.Header.BlockSize);
        Assert.Equal(256u, (uint)Dats.Cell.Header.BlockSize);

        // Iteration records of the complete end-of-retail dats (ACE documents the same values).
        Assert.Equal(2072, Dats.Portal.Iteration.CurrentIteration);
        Assert.Equal(982, Dats.Cell.Iteration.CurrentIteration);
        Assert.Equal(2072, Dats.Iterations.Portal);
        Assert.Equal(982, Dats.Iterations.Cell);
    }

    [RequiresDatsFact]
    public void Optional_highres_dat_opens_with_end_of_retail_iteration_when_present()
    {
        if (Dats.HighRes is null)
        {
            Assert.Null(Dats.Iterations.HighRes);
            return;
        }

        // ACE DatManager.ITERATION_HIRES = 497; validated by tools/installer/datcheck.py.
        Assert.Equal(497, Dats.Iterations.HighRes);
        Assert.Equal(1024u, (uint)Dats.HighRes.Header.BlockSize);
        Assert.EndsWith("-h497", Dats.Iterations.CacheKey);
    }

    [RequiresDatsFact]
    public void Concurrent_reads_through_the_lock_are_consistent()
    {
        var ids = Enumerable.Range(0, 64).Select(i => 0xA9B4FFFFu - (uint)(i % 4 << 16)).ToArray();
        var expected = ids.Select(id => Dats.GetLandBlock(id)?.Height.Sum(h => h) ?? -1).ToArray();
        var results = new int[ids.Length];
        Parallel.For(0, ids.Length, i => results[i] = Dats.GetLandBlock(ids[i])?.Height.Sum(h => h) ?? -1);
        Assert.Equal(expected, results);
    }

    [RequiresDatsFact]
    public void Holtburg_landblock_heights_match_scouted_values()
    {
        var landBlock = Dats.GetLandBlock(0xA9B4FFFFu);
        Assert.NotNull(landBlock);
        Assert.True(landBlock.HasObjects);

        var terrain = Dats.GetLandblockTerrain(LandblockCoord.Holtburg);
        Assert.NotNull(terrain);
        for (var row = 0; row < 9; row++)
        {
            var y = 8 - row;
            for (var x = 0; x < 9; x++)
            {
                Assert.True(
                    HoltburgHeightsNorthFirst[row][x] == terrain.HeightIndex(x, y),
                    $"height index at x={x}, y={y}: expected {HoltburgHeightsNorthFirst[row][x]}, got {terrain.HeightIndex(x, y)}");
                Assert.Equal(landBlock.Height[(x * 9) + y], terrain.HeightIndex(x, y));
            }
        }
    }

    [RequiresDatsFact]
    public void Holtburg_terrain_words_use_known_terrain_types()
    {
        var terrain = Dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var names = new HashSet<string>();
        for (var x = 0; x < 9; x++)
        {
            for (var y = 0; y < 9; y++)
            {
                names.Add(Dats.TerrainRegion.TerrainTypes[terrain.TerrainType(x, y)].Name);
            }
        }

        // local-scout.md: "LushGrass/Grassland/PatchyGrassland/SemiBarrenRock here".
        Assert.Subset(new HashSet<string> { "LushGrass", "Grassland", "PatchyGrassland", "SemiBarrenRock" }, names);
    }

    [RequiresDatsFact]
    public void Region_land_height_table_has_256_monotonic_entries()
    {
        Assert.Equal("Dereth", Dats.Region.RegionName.Value);

        var table = Dats.TerrainRegion.LandHeightTable;
        Assert.Equal(256, table.Count);
        for (var i = 1; i < table.Count; i++)
        {
            Assert.True(table[i] > table[i - 1], $"LandHeightTable not strictly increasing at {i}: {table[i - 1]} -> {table[i]}");
        }

        Assert.Equal(0f, table[0]);
        Assert.Equal(2f, table[1]);
        Assert.Equal(66f, table[33]); // Holtburg town plateau
        Assert.Equal(128f, table[64]);
        Assert.Equal(256f, table[128]);
        Assert.Equal(700f, table[255]);
        Assert.Equal(24f, Dats.TerrainRegion.SquareLength);
    }

    [RequiresDatsFact]
    public void Region_has_32_terrain_types()
    {
        var types = Dats.TerrainRegion.TerrainTypes;
        Assert.Equal(32, types.Count);
        Assert.All(types, t => Assert.False(string.IsNullOrEmpty(t.Name)));
        Assert.Contains(types, t => t.Name == "LushGrass");
    }
}
