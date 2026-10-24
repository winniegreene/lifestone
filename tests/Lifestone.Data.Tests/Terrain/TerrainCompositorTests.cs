using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain.Oracle;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>The CPU reference compositor: merge arithmetic, rotations, discrete/continuous agreement and real textures.</summary>
public sealed class TerrainCompositorTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Merge_follows_MergeTexture_arithmetic_for_every_alpha()
    {
        for (var alpha = 0; alpha < 256; alpha++)
        {
            foreach (var (dst, src) in new[] { (10, 200), (255, 0), (0, 255), (93, 94) })
            {
                var a = alpha > 128 ? alpha + 1 : alpha;
                var expected = alpha == 255 ? (byte)dst : (byte)(((a * dst) + ((256 - a) * src)) >> 8);
                var merged = TerrainCompositor.Merge(
                    new TerrainRgba((byte)dst, (byte)src, (byte)dst, 77), new TerrainRgba((byte)src, (byte)dst, (byte)src, 9), (byte)alpha);
                var expectedG = alpha == 255 ? (byte)src : (byte)(((a * src) + ((256 - a) * dst)) >> 8);
                Assert.Equal(new TerrainRgba(expected, expectedG, expected, 77), merged);
                Assert.Equal(alpha == 255 ? 1f : a / 256f, TerrainCompositor.RetailWeight((byte)alpha));
            }
        }
    }

    [Fact]
    public void Merge_worked_values()
    {
        static byte M(byte alpha, byte dst, byte src) => TerrainCompositor.Merge(new(dst, 0, 0, 0), new(src, 0, 0, 0), alpha).R;
        Assert.Equal(10, M(255, 10, 200));
        Assert.Equal(200, M(0, 10, 200));
        Assert.Equal(150, M(128, 100, 200));
        Assert.Equal(149, M(129, 100, 200)); // (130*100 + 126*200) >> 8 = 38200 >> 8 = 149
        Assert.Equal(0, M(254, 0, 255));
        Assert.Equal(0, M(1, 255, 0));
        Assert.Equal(1f, TerrainCompositor.RetailWeight(255));
        Assert.Equal(0f, TerrainCompositor.RetailWeight(0));
        Assert.Equal(0.5f, TerrainCompositor.RetailWeight(128));
        Assert.Equal(130f / 256f, TerrainCompositor.RetailWeight(129));
    }

    [Theory]
    [InlineData(TerrainRotation.Rot0, 8u, 0f, 0f)] // NW
    [InlineData(TerrainRotation.Rot1, 1u, 0f, 1f)] // SW
    [InlineData(TerrainRotation.Rot2, 2u, 1f, 1f)] // SE
    [InlineData(TerrainRotation.Rot3, 4u, 1f, 0f)] // NE
    public void Rotations_move_a_NW_corner_map_to_the_corner_of_the_rotated_code(TerrainRotation rotation, uint rotatedCode, float u, float v)
    {
        // Code 8 (NW) rotated r times: 8 -> 1 (SW) -> 2 (SE) -> 4 (NE). Cell UV: u east, v south, (0,0) = NW.
        var code = 8u;
        for (var i = 0; i < (int)rotation; i++)
        {
            code = TerrainTexMerge.RotateCode(code);
        }

        Assert.Equal(rotatedCode, code);

        // The corner of the cell named by the rotated code reads the map's NW texel, where a NW map is the new layer.
        Assert.Equal((0f, 0f), TerrainCompositor.RotateAlphaUv(rotation, u, v));

        // A side map (9 = west) rotated the same way covers the side that contains that corner and its successor.
        var sideCode = 9u;
        for (var i = 0; i < (int)rotation; i++)
        {
            sideCode = TerrainTexMerge.RotateCode(sideCode);
        }

        Assert.NotEqual(0u, sideCode & rotatedCode);
    }

    [Theory]
    [InlineData(TerrainRotation.Rot0)]
    [InlineData(TerrainRotation.Rot1)]
    [InlineData(TerrainRotation.Rot2)]
    [InlineData(TerrainRotation.Rot3)]
    public void Rotations_map_every_corner_and_interior_point_consistently_with_RotateCode(TerrainRotation rotation)
    {
        // Cell UV of each corner code (u east, v south, (0,0) = NW).
        static (float U, float V) Corner(uint code) => code switch
        {
            1 => (0f, 1f), // SW
            2 => (1f, 1f), // SE
            4 => (1f, 0f), // NE
            8 => (0f, 0f), // NW
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };

        uint Rotate(uint code)
        {
            for (var i = 0; i < (int)rotation; i++)
            {
                code = TerrainTexMerge.RotateCode(code);
            }

            return code;
        }

        // A map whose code is c, used at rotation r, covers code RotateCode^r(c): the cell corner named by the rotated
        // code must read the map at the corner named by c. All four corners pin the map down (a mirror fails two of them).
        foreach (var c in new uint[] { 1, 2, 4, 8 })
        {
            Assert.Equal(Corner(c), TerrainCompositor.RotateAlphaUv(rotation, Corner(Rotate(c)).U, Corner(Rotate(c)).V));
        }

        // The map is affine, so the corner images fix every interior point: f(u, v) = f(NW) + u (f(NE) - f(NW)) + v (f(SW) - f(NW)).
        var nw = TerrainCompositor.RotateAlphaUv(rotation, 0f, 0f);
        var ne = TerrainCompositor.RotateAlphaUv(rotation, 1f, 0f);
        var sw = TerrainCompositor.RotateAlphaUv(rotation, 0f, 1f);
        foreach (var (u, v) in new[] { (0.25f, 0.1f), (0.8f, 0.35f), (0.5f, 0.5f), (0.05f, 0.9f) })
        {
            var expected = (nw.U + (u * (ne.U - nw.U)) + (v * (sw.U - nw.U)), nw.V + (u * (ne.V - nw.V)) + (v * (sw.V - nw.V)));
            var actual = TerrainCompositor.RotateAlphaUv(rotation, u, v);
            Assert.Equal(expected.Item1, actual.U, 5);
            Assert.Equal(expected.Item2, actual.V, 5);
        }
    }

    [Fact]
    public void Retail_texture_matches_the_ImgTex_pointer_walk_on_random_asymmetric_maps()
    {
        // Random alpha maps have no symmetry, so a transposed or mirrored rotation cannot hide here (real corner maps are
        // nearly symmetric about their diagonal). 32x32 layers tiled twice into a 64x64 buffer: the same 2x replication.
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped(baseTexSize: 64, tiling: 2));
        var merge = new TerrainTexMerge(manifest);
        var rng = new Random(23);
        var textures = manifest.TerrainLayers.Select(_ => RandomImage(rng, 32)).ToArray();
        var alphas = manifest.AlphaLayers.Select(_ => RandomAlpha(rng, 32)).ToArray();
        var rawTextures = textures.Select(ToBgra).ToArray();
        var rawAlphas = alphas.Select(a => new RetailImgTexOracle.Alpha8(a.Width, a.Height, a.Pixels)).ToArray();

        var seen = new HashSet<(TerrainAlphaKind, TerrainRotation)>();
        for (var n = 0; n < 600; n++)
        {
            // A third of the cells draw from all 32 types (mostly four different corners); the rest from a pool of two or
            // three types, so the two-corner side codes (pairs of equal corners) come up often too.
            int[] pool = [rng.Next(32), rng.Next(32), rng.Next(32)];
            var poolSize = 2 + (n % 2);
            int T() => n % 3 == 0 ? rng.Next(32) : pool[rng.Next(poolSize)];
            var pal = TerrainPalCode.Compose(rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(4), T(), T(), T(), T());
            if (n % 2 == 0)
            {
                pal &= 0xF00FFFFFu; // road-free, so overlays dominate half the sample
            }

            var blend = merge.GetCellBlend(pal);
            NoteLayers(manifest, blend, seen);
            var image = TerrainCompositor.ComposeRetailTexture(blend, manifest, l => textures[l], l => alphas[l]);
            var oracle = OracleTexture(manifest, blend, rawTextures, rawAlphas);
            AssertSameTexture(blend, image, oracle);
        }

        Assert.All(ReachableLayerKinds, w => Assert.Contains(w, seen));
        Assert.DoesNotContain((TerrainAlphaKind.Side, TerrainRotation.Rot0), seen);
        Assert.DoesNotContain((TerrainAlphaKind.Side, TerrainRotation.Rot1), seen);
    }

    /// <summary>
    /// Every (alpha-map list, rotation) pair a Dereth-shaped TexMerge can produce. The side map's code is 9 (SW + NW), and
    /// <c>TexMerge::GetTerrain</c> only builds a two-corner code from two consecutive non-base corners of one type: SW + NW
    /// (9) are not consecutive in the scan, and SW + SE (3) of one type would themselves be the first duplicate, i.e. the
    /// base. So the side map is only used as 6 (rotation 2) and 12 (rotation 3). Corner and road maps cover every rotation.
    /// </summary>
    private static readonly (TerrainAlphaKind, TerrainRotation)[] ReachableLayerKinds =
        new[] { TerrainAlphaKind.Corner, TerrainAlphaKind.Side, TerrainAlphaKind.Road }
            .SelectMany(k => Enum.GetValues<TerrainRotation>().Select(r => (k, r)))
            .Where(w => w != (TerrainAlphaKind.Side, TerrainRotation.Rot0) && w != (TerrainAlphaKind.Side, TerrainRotation.Rot1))
            .ToArray();

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Real_cells_match_the_ImgTex_pointer_walk_for_every_map_kind_and_rotation()
    {
        var dats = fixture.Dats;
        var manifest = TerrainAtlasManifest.FromDatSet(dats);
        var merge = new TerrainTexMerge(manifest);
        var textures = manifest.TerrainLayers.Select(l => TerrainTestData.DecodeColor(dats, l.SurfaceTextureId)).ToArray();
        var alphas = manifest.AlphaLayers.Select(a => TerrainTestData.DecodeAlpha(dats, a.SurfaceTextureId)).ToArray();

        // The oracle reads the RenderSurface bytes directly, not the test decoder's TerrainImage.
        var rawTextures = manifest.TerrainLayers.Select(l => RawBgra(dats, l.SurfaceTextureId)).ToArray();
        var rawAlphas = manifest.AlphaLayers.Select(a =>
        {
            var rs = TerrainTestData.ResolveRenderSurface(dats, a.SurfaceTextureId);
            return new RetailImgTexOracle.Alpha8(rs.Width, rs.Height, rs.SourceData);
        }).ToArray();

        // Holtburg's distinct cells, plus the first cell (spiralling out) for every (map kind, rotation) Holtburg lacks.
        var picked = new Dictionary<uint, TerrainCellBlend>();
        var seen = new HashSet<(TerrainAlphaKind, TerrainRotation)>();
        var holtburg = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        foreach (var blend in merge.BuildLandblock(holtburg).Cells)
        {
            picked.TryAdd(blend.PalCode, blend);
            NoteLayers(manifest, blend, seen);
        }

        var wanted = ReachableLayerKinds;
        for (var radius = 1; radius <= 12 && !wanted.All(seen.Contains); radius++)
        {
            foreach (var terrain in TerrainTestData.LandblocksAround(dats, LandblockCoord.Holtburg, radius))
            {
                foreach (var blend in merge.BuildLandblock(terrain).Cells)
                {
                    var before = seen.Count;
                    NoteLayers(manifest, blend, seen);
                    if (seen.Count > before)
                    {
                        picked.TryAdd(blend.PalCode, blend);
                    }
                }
            }
        }

        Assert.All(wanted, w => Assert.Contains(w, seen));
        foreach (var blend in picked.Values)
        {
            var image = TerrainCompositor.ComposeRetailTexture(blend, manifest, l => textures[l], l => alphas[l]);
            AssertSameTexture(blend, image, OracleTexture(manifest, blend, rawTextures, rawAlphas));
        }

        output.WriteLine($"{picked.Count} cells checked texel-for-texel against the ImgTex pointer walk");
    }

    private static void NoteLayers(TerrainAtlasManifest manifest, in TerrainCellBlend blend, HashSet<(TerrainAlphaKind, TerrainRotation)> seen)
    {
        for (var i = 0; i < blend.OverlayCount; i++)
        {
            var o = blend.GetOverlay(i);
            seen.Add((manifest.AlphaLayers[o.AlphaLayer].Kind, o.Rotation));
        }

        for (var i = 0; i < blend.RoadCount; i++)
        {
            var r = blend.GetRoad(i);
            seen.Add((manifest.AlphaLayers[r.AlphaLayer].Kind, r.Rotation));
        }
    }

    private static byte[] OracleTexture(
        TerrainAtlasManifest manifest,
        in TerrainCellBlend blend,
        RetailImgTexOracle.Bgra[] textures,
        RetailImgTexOracle.Alpha8[] alphas)
    {
        var layers = new List<(RetailImgTexOracle.Bgra, RetailImgTexOracle.Alpha8, int)>();
        for (var i = 0; i < blend.OverlayCount; i++)
        {
            var o = blend.GetOverlay(i);
            layers.Add((textures[o.TextureLayer], alphas[o.AlphaLayer], (int)o.Rotation));
        }

        for (var i = 0; i < blend.RoadCount; i++)
        {
            var r = blend.GetRoad(i);
            layers.Add((textures[blend.RoadTextureLayer], alphas[r.AlphaLayer], (int)r.Rotation));
        }

        return RetailImgTexOracle.BuildCellTexture(
            (int)manifest.BaseTexSize,
            textures[blend.BaseTextureLayer],
            (int)manifest.TilingOfLayer(blend.BaseTextureLayer),
            layers);
    }

    private static void AssertSameTexture(in TerrainCellBlend blend, TerrainImage image, byte[] bgra)
    {
        Assert.Equal(image.Width * image.Height * 4, bgra.Length);
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var p = image.Pixels[i];
            if (p.B != bgra[i * 4] || p.G != bgra[(i * 4) + 1] || p.R != bgra[(i * 4) + 2] || p.A != bgra[(i * 4) + 3])
            {
                Assert.Fail(
                    $"{blend}: texel ({i % image.Width}, {i / image.Width}) is {p}, ImgTex walk gives " +
                    $"#{bgra[(i * 4) + 2]:X2}{bgra[(i * 4) + 1]:X2}{bgra[i * 4]:X2}{bgra[(i * 4) + 3]:X2}");
            }
        }
    }

    private static RetailImgTexOracle.Bgra ToBgra(TerrainImage image)
    {
        var bytes = new byte[image.Pixels.Length * 4];
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var p = image.Pixels[i];
            bytes[i * 4] = p.B;
            bytes[(i * 4) + 1] = p.G;
            bytes[(i * 4) + 2] = p.R;
            bytes[(i * 4) + 3] = p.A;
        }

        return new RetailImgTexOracle.Bgra(image.Width, image.Height, bytes);
    }

    private static RetailImgTexOracle.Bgra RawBgra(Lifestone.Data.Dat.DatSet dats, uint surfaceTextureId)
    {
        var rs = TerrainTestData.ResolveRenderSurface(dats, surfaceTextureId);
        Assert.Equal(rs.Width * rs.Height * 4, rs.SourceData.Length);
        return new RetailImgTexOracle.Bgra(rs.Width, rs.Height, rs.SourceData);
    }

    [Fact]
    public void Discrete_retail_texture_equals_continuous_composition_at_texel_centres()
    {
        // A small Dereth-shaped atlas: 32x32 layers tiled twice into a 64x64 buffer, 32x32 alpha maps (scale 2).
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped(baseTexSize: 64, tiling: 2));
        var merge = new TerrainTexMerge(manifest);
        var rng = new Random(7);
        var textures = manifest.TerrainLayers.Select(_ => RandomImage(rng, 32)).ToArray();
        var alphas = manifest.AlphaLayers.Select(_ => RandomAlpha(rng, 32)).ToArray();
        var (texels, alphaSampler) = TerrainCompositor.NearestSamplers(l => textures[l], l => alphas[l]);

        var maxFloatDiff = 0f;
        for (var n = 0; n < 400; n++)
        {
            var pal = TerrainPalCode.Compose(rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(33) % 32, rng.Next(32), rng.Next(32), rng.Next(32));
            if (n % 3 == 0)
            {
                // Plenty of road-free cells so overlays are exercised too.
                pal &= 0xF00FFFFFu;
            }

            var blend = merge.GetCellBlend(pal);
            var image = TerrainCompositor.ComposeRetailTexture(blend, manifest, l => textures[l], l => alphas[l]);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var u = (x + 0.5f) / 64f;
                    var v = (y + 0.5f) / 64f;
                    Assert.Equal(image[x, y], TerrainCompositor.Compose(blend, manifest, u, v, texels, alphaSampler));
                    var f = TerrainCompositor.ComposeFloat(blend, manifest, u, v, texels, alphaSampler);
                    maxFloatDiff = Math.Max(maxFloatDiff, MaxDiff(image[x, y], f));
                }
            }
        }

        // Retail truncates after every layer (up to 5): the float path stays within a few levels.
        Assert.True(maxFloatDiff <= 6, $"float path deviates by {maxFloatDiff} levels");
    }

    [Fact]
    public void Layers_merge_sequentially_in_retail_order()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped(baseTexSize: 64, tiling: 2));
        var merge = new TerrainTexMerge(manifest);

        // All four corners differ: base SW, overlays SE, NE, NW (codes 2, 4, 8) — then roads on top.
        var blend = merge.GetCellBlend(TerrainPalCode.Compose(0, 0, 1, 1, 1, 2, 3, 4));
        Assert.Equal(3, blend.OverlayCount);
        Assert.Equal(1, blend.RoadCount); // NE + NW road = 12

        static TerrainRgba Solid(int layer) => new((byte)(layer * 7), (byte)layer, 0, 255);
        static TerrainRgba Texels(int layer, float u, float v) => Solid(layer);

        // Every alpha map fully covers (alpha 0): the result must be the last layer, the road.
        var road = TerrainCompositor.Compose(blend, manifest, 0.3f, 0.6f, Texels, (_, _, _) => 0);
        Assert.Equal(Solid(manifest.RoadLayer), road);

        // Nothing covers (alpha 255): the base survives.
        var baseOnly = TerrainCompositor.Compose(blend, manifest, 0.3f, 0.6f, Texels, (_, _, _) => 255);
        Assert.Equal(Solid(blend.BaseTextureLayer), baseOnly);

        // Half coverage everywhere: the result is the left fold base -> overlay 0 -> 1 -> 2 -> road, and order matters.
        var half = TerrainCompositor.Compose(blend, manifest, 0.3f, 0.6f, Texels, (_, _, _) => 128);
        int[] order = [blend.Overlay0.TextureLayer, blend.Overlay1.TextureLayer, blend.Overlay2.TextureLayer, blend.RoadTextureLayer];
        var expected = order.Aggregate(Solid(blend.BaseTextureLayer), (c, layer) => TerrainCompositor.Merge(c, Solid(layer), 128));
        var reversed = Enumerable.Reverse(order).Aggregate(Solid(blend.BaseTextureLayer), (c, layer) => TerrainCompositor.Merge(c, Solid(layer), 128));
        Assert.Equal(expected, half);
        Assert.NotEqual(reversed, half);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Real_alpha_maps_cover_the_corners_and_sides_their_codes_name()
    {
        var dats = fixture.Dats;
        var m = TerrainAtlasManifest.FromDatSet(dats);

        // Row 0 is the north edge and column 0 the west edge; low alpha = the new layer.
        foreach (var corner in m.CornerAlphaLayers)
        {
            var a = TerrainTestData.DecodeAlpha(dats, corner.SurfaceTextureId);
            Assert.Equal(8u, corner.Code); // NW
            Assert.True(a[0, 0] < a[511, 511], $"corner 0x{corner.SurfaceTextureId:X8}: NW {a[0, 0]} vs SE {a[511, 511]}");
            Assert.Equal(255, a[511, 511]);
        }

        var side = TerrainTestData.DecodeAlpha(dats, m.SideAlphaLayers[0].SurfaceTextureId);
        Assert.Equal(9u, m.SideAlphaLayers[0].Code); // SW + NW = west side
        Assert.True(side[0, 256] < side[511, 256], $"side: W {side[0, 256]} vs E {side[511, 256]}");

        var roads = m.RoadAlphaLayers.ToDictionary(r => r.Code, r => TerrainTestData.DecodeAlpha(dats, r.SurfaceTextureId));
        Assert.True(roads[9][0, 256] < roads[9][511, 256], "road 9 (SW+NW) should run along the west edge");
        Assert.True(roads[8][0, 0] < roads[8][511, 511], "road 8 (NW) should sit at the north-west corner");
        Assert.True(roads[10][256, 256] < roads[10][511, 0] && roads[10][256, 256] < roads[10][0, 511],
            "road 10 (SE+NW) should run along the NW-SE diagonal");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Holtburg_cells_compose_identically_both_ways_with_real_textures()
    {
        var dats = fixture.Dats;
        var manifest = TerrainAtlasManifest.FromDatSet(dats);
        var merge = new TerrainTexMerge(manifest);
        var textures = manifest.TerrainLayers.Select(l => TerrainTestData.DecodeColor(dats, l.SurfaceTextureId)).ToArray();
        var alphas = manifest.AlphaLayers.Select(a => TerrainTestData.DecodeAlpha(dats, a.SurfaceTextureId)).ToArray();
        var (texels, alphaSampler) = TerrainCompositor.NearestSamplers(l => textures[l], l => alphas[l]);

        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg);
        Assert.NotNull(terrain);
        var blends = merge.BuildLandblock(terrain).Cells.DistinctBy(c => c.PalCode).ToArray();
        Assert.Contains(blends, b => b.OverlayCount > 0);
        Assert.Contains(blends, b => b.RoadCount > 0);

        var maxFloatDiff = 0f;
        foreach (var blend in blends)
        {
            var image = TerrainCompositor.ComposeRetailTexture(blend, manifest, l => textures[l], l => alphas[l]);
            if (blend.OverlayCount == 0 && blend.RoadCount == 0)
            {
                // Base-only cells are the base texture tiled twice.
                var b = textures[blend.BaseTextureLayer];
                Assert.Equal(b[5, 9], image[512 + 5, 9]);
                Assert.Equal(b[511, 511], image[1023, 1023]);
            }

            for (var y = 1; y < 1024; y += 7)
            {
                for (var x = 3; x < 1024; x += 7)
                {
                    var u = (x + 0.5f) / 1024f;
                    var v = (y + 0.5f) / 1024f;
                    Assert.Equal(image[x, y], TerrainCompositor.Compose(blend, manifest, u, v, texels, alphaSampler));
                    maxFloatDiff = Math.Max(maxFloatDiff, MaxDiff(image[x, y], TerrainCompositor.ComposeFloat(blend, manifest, u, v, texels, alphaSampler)));
                }
            }
        }

        output.WriteLine($"{blends.Length} distinct Holtburg cells; float path max deviation {maxFloatDiff} levels");
        Assert.True(maxFloatDiff <= 6, $"float path deviates by {maxFloatDiff} levels");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Vertex_light_interpolation_follows_the_mesh_triangles()
    {
        var dats = fixture.Dats;
        var region = dats.TerrainRegion;
        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg)!;
        var mesh = TerrainMeshBuilder.Build(terrain, region);
        var day = new TerrainLightingEnvironment(new System.Numerics.Vector3(0.6f, -0.2f, 0.4f), 0xFFFAD797u, 0.35f, 0xFFE6E6FFu);
        var lights = TerrainVertexLighting.Compute(terrain, region, day);
        var rng = new Random(5);

        for (var n = 0; n < 2000; n++)
        {
            var (cx, cy) = (rng.Next(8), rng.Next(8));
            var (u, v) = ((float)rng.NextDouble(), (float)rng.NextDouble());
            var actual = TerrainCompositor.InterpolateVertexLight(lights, cx, cy, mesh.CellSplitSWtoNE[(cx * 8) + cy], u, v);

            // Independent: barycentric weights of the mesh triangle (from TerrainMeshBuilder) that contains the point.
            var p = new System.Numerics.Vector2((cx + u) * 24f, (cy + 1 - v) * 24f);
            System.Numerics.Vector3? expected = null;
            for (var t = ((cx * 8) + cy) * 2; t < (((cx * 8) + cy) * 2) + 2 && expected is null; t++)
            {
                var ids = new[] { mesh.Indices[t * 3], mesh.Indices[(t * 3) + 1], mesh.Indices[(t * 3) + 2] };
                var a = new System.Numerics.Vector2(mesh.Positions[ids[0]].X, mesh.Positions[ids[0]].Y);
                var b = new System.Numerics.Vector2(mesh.Positions[ids[1]].X, mesh.Positions[ids[1]].Y);
                var c = new System.Numerics.Vector2(mesh.Positions[ids[2]].X, mesh.Positions[ids[2]].Y);
                var area = Cross(b - a, c - a);
                var wa = Cross(b - p, c - p) / area;
                var wb = Cross(c - p, a - p) / area;
                var wc = 1 - wa - wb;
                if (wa >= -1e-5f && wb >= -1e-5f && wc >= -1e-5f)
                {
                    expected = (L(ids[0]) * wa) + (L(ids[1]) * wb) + (L(ids[2]) * wc);
                }
            }

            Assert.NotNull(expected);
            Assert.True(System.Numerics.Vector3.Distance(expected.Value, actual) < 1e-4f, $"cell ({cx},{cy}) uv ({u},{v}): {actual} vs {expected}");
        }

        System.Numerics.Vector3 L(int i) => new(lights[i].R8 / 255f, lights[i].G8 / 255f, lights[i].B8 / 255f);
        static float Cross(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
    }

    private static float MaxDiff(TerrainRgba exact, System.Numerics.Vector4 f) =>
        Math.Max(Math.Abs(exact.R - (f.X * 255f)), Math.Max(Math.Abs(exact.G - (f.Y * 255f)), Math.Abs(exact.B - (f.Z * 255f))));

    private static TerrainImage RandomImage(Random rng, int size)
    {
        var pixels = new TerrainRgba[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new TerrainRgba((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), 255);
        }

        return new TerrainImage(size, size, pixels);
    }

    private static TerrainAlphaImage RandomAlpha(Random rng, int size)
    {
        var pixels = new byte[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            // Bias towards the special values 0, 128, 129 and 255.
            pixels[i] = (rng.Next(8)) switch { 0 => 0, 1 => 255, 2 => 128, 3 => 129, _ => (byte)rng.Next(256) };
        }

        return new TerrainAlphaImage(size, size, pixels);
    }
}
