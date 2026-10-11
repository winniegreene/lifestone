using System.Diagnostics;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain.Oracle;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>
/// Conformance of the per-cell TexMerge recipes against the ACViewer/ACE oracle on the user's real dats.
/// </summary>
public sealed class TerrainCellBlendConformanceTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Holtburg_area_cells_match_the_oracle()
    {
        var result = Sweep(TerrainTestData.LandblocksAround(fixture.Dats, LandblockCoord.Holtburg, 4));
        Assert.Equal(81, result.Landblocks);
        Assert.True(result.Mismatches == 0, result.FirstMismatch);
        Assert.True(result.MaxOverlays >= 2, "Holtburg area should contain multi-overlay cells");
        Assert.True(result.CellsWithRoads > 0, "Holtburg has roads");
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_cell_of_every_landblock_matches_the_oracle()
    {
        var watch = Stopwatch.StartNew();
        var result = Sweep(TerrainTestData.AllLandblocks(fixture.Dats));
        output.WriteLine($"{result.Landblocks} landblocks, {result.Cells} cells, {result.UniquePalCodes} unique palette codes, " +
                         $"{result.CellsWithRoads} cells with road layers, {result.AllRoadCells} all-road cells, " +
                         $"overlay histogram [{string.Join(", ", result.OverlayHistogram)}], " +
                         $"double-literal prng disagreements {result.PrngDisagreements}, {watch.Elapsed.TotalSeconds:F1} s");

        Assert.Equal(255 * 255, result.Landblocks);
        Assert.Equal(255 * 255 * 64, result.Cells);
        Assert.True(result.Mismatches == 0, result.FirstMismatch);

        // Retail's float 2^-32 and the ports' double 2.3283064e-10 never disagree on a palette code that exists in the world.
        Assert.Equal(0, result.PrngDisagreements);
    }

    private SweepResult Sweep(IEnumerable<LandblockTerrain> landblocks)
    {
        var texMerge = TerrainTestData.RegionTexMerge(fixture.Dats);
        var manifest = TerrainAtlasManifest.FromTexMerge(texMerge);
        var merge = new TerrainTexMerge(manifest);
        var oracle = new AcViewerTexMergeOracle(texMerge);
        var oracleCache = new Dictionary<uint, OracleBlend>();
        var result = new SweepResult();
        var unique = new HashSet<uint>();

        foreach (var terrain in landblocks)
        {
            result.Landblocks++;
            var blend = merge.BuildLandblock(terrain);
            for (var cx = 0; cx < 8; cx++)
            {
                for (var cy = 0; cy < 8; cy++)
                {
                    result.Cells++;
                    var cell = blend[cx, cy];

                    // The oracle derives the palette code from the raw terrain words itself (ACE GetCellRotation).
                    var pal = AcViewerTexMergeOracle.CellPalCode(terrain.TerrainWords, cx, cy, out var single);
                    if (!oracleCache.TryGetValue(pal, out var expected))
                    {
                        expected = oracle.BuildTexture(pal);
                        oracleCache.Add(pal, expected);
                    }

                    var diff = cell.IsSingleTexture != single
                        ? $"singleTexture {cell.IsSingleTexture} vs {single}"
                        : OracleComparison.Difference(cell, expected, manifest, checkAcViewerAtlasIndex: true);
                    if (diff is not null)
                    {
                        result.Mismatches++;
                        result.FirstMismatch ??= $"{terrain.Coord} cell ({cx},{cy}): {diff}";
                    }

                    if (unique.Add(pal))
                    {
                        foreach (var count in new[] { manifest.CornerAlphaLayers.Count, manifest.SideAlphaLayers.Count, manifest.RoadAlphaLayers.Count })
                        {
                            if (TerrainTexMerge.PseudoRandomIndex(pal, count) != AcViewerTexMergeOracle.AcViewerPrng(pal, count))
                            {
                                result.PrngDisagreements++;
                            }
                        }
                    }

                    result.OverlayHistogram[cell.OverlayCount]++;
                    result.MaxOverlays = Math.Max(result.MaxOverlays, cell.OverlayCount);
                    result.CellsWithRoads += cell.RoadCount > 0 ? 1 : 0;
                    result.AllRoadCells += cell.IsAllRoad ? 1 : 0;
                }
            }
        }

        result.UniquePalCodes = unique.Count;
        Assert.Equal(unique.Count, merge.CachedCount);
        return result;
    }

    private sealed class SweepResult
    {
        public int Landblocks { get; set; }

        public int Cells { get; set; }

        public int Mismatches { get; set; }

        public string? FirstMismatch { get; set; }

        public int UniquePalCodes { get; set; }

        public int PrngDisagreements { get; set; }

        public int[] OverlayHistogram { get; } = new int[4];

        public int MaxOverlays { get; set; }

        public int CellsWithRoads { get; set; }

        public int AllRoadCells { get; set; }
    }
}
