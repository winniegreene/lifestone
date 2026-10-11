using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain.Oracle;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>
/// Unit tests of the TexMerge port against the ACViewer oracle on synthetic TexMerge records. Real-dat conformance is in
/// <see cref="TerrainCellBlendConformanceTests"/>.
/// </summary>
public sealed class TerrainTexMergeTests
{
    [Fact]
    public void RotateCode_cycles_corner_side_and_diagonal_families()
    {
        Assert.Equal([8u, 1u, 2u, 4u, 8u], Cycle(8));
        Assert.Equal([9u, 3u, 6u, 12u, 9u], Cycle(9));
        Assert.Equal([10u, 5u, 10u, 5u, 10u], Cycle(10));

        static uint[] Cycle(uint start)
        {
            var codes = new uint[5];
            codes[0] = start;
            for (var i = 1; i < 5; i++)
            {
                codes[i] = TerrainTexMerge.RotateCode(codes[i - 1]);
            }

            return codes;
        }
    }

    [Fact]
    public void GetRoadCodes_matches_oracle_for_every_road_combination()
    {
        for (var code = 0; code < 256; code++)
        {
            var pal = TerrainPalCode.Compose(code & 3, (code >> 2) & 3, (code >> 4) & 3, (code >> 6) & 3, 0, 0, 0, 0);
            var allRoad = false;
            var expected = AcViewerTexMergeOracle.GetRoadCode(pal, ref allRoad);
            var (r0, r1, all) = TerrainTexMerge.GetRoadCodes(pal);
            Assert.Equal(allRoad, all);
            Assert.Equal(expected[0], r0);
            Assert.Equal(expected[1], r1);
        }
    }

    [Fact]
    public void GetTerrain_matches_oracle_for_every_corner_pattern()
    {
        var oracle = new AcViewerTexMergeOracle(TerrainTestData.DerethShaped());
        for (var code = 0; code < 6 * 6 * 6 * 6; code++)
        {
            var t = new[] { code % 6, (code / 6) % 6, (code / 36) % 6, code / 216 };
            var pal = TerrainPalCode.Compose(0, 0, 0, 0, t[0], t[1], t[2], t[3]);
            oracle.GetTerrain(pal, out var tcodes, out var types);
            TerrainTexMerge.GetTerrain(pal, out var ourTypes, out var ourCodes);
            Assert.Equal(tcodes, ourCodes);
            Assert.Equal(types, ourTypes);
        }
    }

    [Theory]
    // Corner order SW, SE, NE, NW.
    [InlineData(1, 1, 1, 1, 1, -1, -1, 0u, 0u, 0u)] // uniform
    [InlineData(2, 1, 1, 1, 1, 2, -1, 1u, 0u, 0u)] // SW differs: corner code 1
    [InlineData(1, 2, 2, 1, 1, 2, -1, 6u, 0u, 0u)] // east side: SE + NE = 6
    [InlineData(2, 2, 1, 1, 2, 1, -1, 12u, 0u, 0u)] // north side: NE + NW = 12
    [InlineData(2, 1, 2, 1, 2, 1, 1, 2u, 8u, 0u)] // checkerboard: two overlays of the same type
    [InlineData(1, 1, 2, 3, 1, 2, 3, 4u, 8u, 0u)] // base pair plus two different corners
    [InlineData(1, 2, 3, 4, 1, 2, 3, 2u, 4u, 8u)] // all different: SW base, SE/NE/NW overlays
    public void GetTerrain_worked_examples(
        int sw, int se, int ne, int nw, int baseType, int ovl0, int ovl1, uint tc0, uint tc1, uint tc2)
    {
        TerrainTexMerge.GetTerrain(TerrainPalCode.Compose(0, 0, 0, 0, sw, se, ne, nw), out var types, out var tcodes);
        Assert.Equal(baseType, types[0]);
        Assert.Equal(ovl0, types[1]);
        Assert.Equal(ovl1, types[2]);
        Assert.Equal([tc0, tc1, tc2], tcodes);
    }

    [Fact]
    public void Cell_blends_match_oracle_for_all_type_and_road_combinations_with_dereth_shaped_texmerge()
    {
        var texMerge = TerrainTestData.DerethShaped();
        AssertAllCombinationsMatch(texMerge, [0, 1, 2, 24, 31], checkAcViewerAtlasIndex: true);
    }

    [Fact]
    public void Cell_blends_match_oracle_with_a_differently_shaped_texmerge()
    {
        // Three corner maps in different rotations, two side maps, and road maps that cannot serve corner road codes (those
        // road layers are skipped, as retail FindRoadAlpha failure skips them).
        var texMerge = TerrainTestData.Build(1024, 2, corner: [1, 4, 8], side: [6, 9], road: [5, 3]);
        AssertAllCombinationsMatch(texMerge, [3, 7, 9, 31, 30], checkAcViewerAtlasIndex: false);
    }

    [Fact]
    public void Unreachable_corner_code_fails_loudly_like_retail_FillTempTexBuffer()
    {
        // A "corner" map with a side code can never be rotated onto a corner code.
        var texMerge = TerrainTestData.Build(1024, 2, corner: [3], side: [9], road: [9, 10, 8]);
        var merge = new TerrainTexMerge(TerrainAtlasManifest.FromTexMerge(texMerge));
        var oracle = new AcViewerTexMergeOracle(texMerge);
        var pal = TerrainPalCode.Compose(0, 0, 0, 0, 2, 1, 1, 1);

        Assert.True(oracle.BuildTexture(pal).Failed);
        var ex = Assert.Throws<InvalidDataException>(() => merge.ComputeCellBlend(pal));
        Assert.Contains("tcode 1", ex.Message);

        // Side codes are still fine.
        Assert.Equal(1, merge.ComputeCellBlend(TerrainPalCode.Compose(0, 0, 0, 0, 1, 2, 2, 1)).OverlayCount);
    }

    [Fact]
    public void PseudoRandomIndex_uses_exact_retail_arithmetic_where_the_double_literal_rounds_down()
    {
        // Pick a palette code whose hashed value x is exactly 2^30: retail floor(x * 2^-32 * 4) = 1, while
        // the C# ports' double literal 2.3283064e-10 (< 2^-32) gives floor(0.99999999843...) = 0.
        const uint target = 1u << 30;
        var pal = SolvePalCodeFor(target);
        Assert.Equal(target, unchecked((1379576222u * pal) - 1372186442u));

        Assert.Equal(1, TerrainTexMerge.PseudoRandomIndex(pal, 4));
        Assert.Equal(0, AcViewerTexMergeOracle.AcViewerPrng(pal, 4));

        // Elsewhere both agree, and the index is uniform over the maps.
        var counts = new int[4];
        var rng = new Random(1234);
        for (var i = 0; i < 100_000; i++)
        {
            var p = (uint)rng.NextInt64(0, 1L << 32);
            var exact = TerrainTexMerge.PseudoRandomIndex(p, 4);
            counts[exact]++;
            Assert.Equal(exact, Math.Min(3, AcViewerTexMergeOracle.AcViewerPrng(p, 4)));
        }

        Assert.All(counts, c => Assert.InRange(c, 24_000, 26_000));
    }

    [Fact]
    public void PalCode_composition_matches_ace_GetPalCode_and_GetCellRotation()
    {
        var rng = new Random(42);
        for (var n = 0; n < 200; n++)
        {
            var words = new ushort[TerrainConstants.VertexCount];
            for (var i = 0; i < words.Length; i++)
            {
                // Real words never set bits 7-10; random scene bits and road values are fine.
                words[i] = (ushort)((rng.Next(32) << 11) | (rng.Next(32) << 2) | rng.Next(4));
            }

            var terrain = new LandblockTerrain(new LandblockCoord((byte)rng.Next(256), (byte)rng.Next(256)), words, new byte[TerrainConstants.VertexCount]);
            for (var cx = 0; cx < 8; cx++)
            {
                for (var cy = 0; cy < 8; cy++)
                {
                    var expected = AcViewerTexMergeOracle.CellPalCode(words, cx, cy, out var single);
                    var pal = TerrainPalCode.ForCell(terrain, cx, cy);
                    Assert.Equal(expected, pal);
                    Assert.Equal(single, TerrainPalCode.IsSingleTexture(pal));
                    Assert.Equal(TerrainPalCode.FullDetailSize, TerrainPalCode.Size(pal));
                }
            }
        }
    }

    [Fact]
    public void Cached_blend_equals_uncached_and_all_road_cells_use_the_road_texture()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        var merge = new TerrainTexMerge(manifest);
        var pal = TerrainPalCode.Compose(1, 2, 3, 1, 5, 6, 7, 8);
        var blend = merge.GetCellBlend(pal);
        Assert.Equal(merge.ComputeCellBlend(pal), blend);
        Assert.True(blend.IsAllRoad);
        Assert.False(blend.IsSingleTexture); // road values differ, so retail clamps (stippling stays 0)
        Assert.Equal(TerrainAtlasManifest.RoadTerrainType, blend.BaseTerrainType);
        Assert.Equal(manifest.RoadLayer, blend.BaseTextureLayer);
        Assert.Equal(0, blend.OverlayCount);
        Assert.Equal(0, blend.RoadCount);
        Assert.Equal(1, merge.CachedCount);
    }

    private static void AssertAllCombinationsMatch(DatReaderWriter.Types.TexMerge texMerge, int[] alphabet, bool checkAcViewerAtlasIndex)
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(texMerge);
        var merge = new TerrainTexMerge(manifest);
        var oracle = new AcViewerTexMergeOracle(texMerge);
        var n = alphabet.Length;
        var checkedCount = 0;
        for (var types = 0; types < n * n * n * n; types++)
        {
            for (var roads = 0; roads < 256; roads++)
            {
                var pal = TerrainPalCode.Compose(
                    roads & 3, (roads >> 2) & 3, (roads >> 4) & 3, (roads >> 6) & 3,
                    alphabet[types % n], alphabet[(types / n) % n], alphabet[(types / (n * n)) % n], alphabet[types / (n * n * n)]);
                var expected = oracle.BuildTexture(pal);
                Assert.False(expected.Failed);
                var actual = merge.ComputeCellBlend(pal);
                var diff = OracleComparison.Difference(actual, expected, manifest, checkAcViewerAtlasIndex);
                Assert.True(diff is null, $"0x{pal:X8}: {diff}");
                checkedCount++;
            }
        }

        Assert.Equal(n * n * n * n * 256, checkedCount);
    }

    /// <summary>Solves 1379576222 * pal - 1372186442 == x (mod 2^32) for pal (x must make the right side even).</summary>
    private static uint SolvePalCodeFor(uint x)
    {
        // 1379576222 = 2 * 689788111, so pal = ((x + 1372186442) / 2) * inverse(689788111) mod 2^31.
        var rhs = unchecked(x + 1372186442u);
        Assert.Equal(0u, rhs & 1);
        const uint odd = 689788111u;
        var inverse = odd; // Newton iteration for the inverse of an odd number modulo 2^32.
        for (var i = 0; i < 5; i++)
        {
            inverse = unchecked(inverse * (2u - (odd * inverse)));
        }

        return unchecked((rhs >> 1) * inverse) & 0x7FFFFFFFu;
    }
}
