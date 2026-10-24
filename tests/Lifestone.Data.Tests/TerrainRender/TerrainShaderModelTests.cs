using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>
/// A line-by-line C# model of the fragment logic of src/Lifestone.Game/shaders/terrain/terrain.gdshader (byte decode of
/// CUSTOM0..2, the slot/rotation bit fields, the base → overlays → roads order, <c>retail_keep</c> and <c>mix</c>), run
/// on the packed bytes of real cells and compared with <see cref="TerrainCompositor.ComposeFloat"/>. It proves the
/// packing carries everything the shader needs and that the shader reads it in retail's order; the GPU itself is
/// checked against the same reference by the golden test (tools/terrain-golden.ps1). Keep the model in step with the shader.
/// </summary>
public sealed class TerrainShaderModelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Shader_model_matches_the_float_reference_on_synthetic_textures_and_random_cells()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped(baseTexSize: 64, tiling: 2));
        var merge = new TerrainTexMerge(manifest);
        var rng = new Random(77);
        var textures = manifest.TerrainLayers.Select(_ => RandomImage(rng, 32)).ToArray();
        var alphas = manifest.AlphaLayers.Select(_ => RandomAlpha(rng, 32)).ToArray();
        var (texels, alphaSampler) = TerrainCompositor.NearestSamplers(l => textures[l], l => alphas[l]);
        var tiling = manifest.TerrainLayers.Select(l => (float)l.TexTiling).ToArray();

        for (var n = 0; n < 3000; n++)
        {
            var pal = TerrainPalCode.Compose(rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(4), rng.Next(32), rng.Next(32), rng.Next(32), rng.Next(32));
            if (n % 3 == 0)
            {
                pal &= 0xF00FFFFFu;
            }

            var blend = merge.GetCellBlend(pal);
            var packed = TerrainBlendPacking.Pack(blend);
            for (var k = 0; k < 8; k++)
            {
                var u = (float)rng.NextDouble();
                var v = (float)rng.NextDouble();
                var expected = TerrainCompositor.ComposeFloat(blend, manifest, u, v, texels, alphaSampler);
                var actual = ShaderModel(packed, tiling, texels, alphaSampler, u, v);
                AssertClose(expected, actual, $"pal 0x{pal:X8} at ({u}, {v})");
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Shader_model_matches_the_float_reference_on_real_cells_and_textures()
    {
        var dats = fixture.Dats;
        var atlas = TerrainAtlasBuilder.FromDats(dats);
        var manifest = atlas.Manifest;
        var merge = new TerrainTexMerge(manifest);
        var colors = Enumerable.Range(0, atlas.ColorLayers.Count).Select(atlas.ColorImage).ToArray();
        var alphas = Enumerable.Range(0, atlas.AlphaLayers.Count).Select(atlas.AlphaImage).ToArray();
        var (texels, alphaSampler) = TerrainCompositor.NearestSamplers(l => colors[l], l => alphas[l]);
        var tiling = atlas.LayerTiling.ToArray();
        var rng = new Random(3);

        foreach (var coord in TerrainRenderMeshTests.Blocks)
        {
            var mesh = TerrainRenderMesh.Build(dats.GetLandblockTerrain(coord)!, dats.TerrainRegion, merge);
            for (var cell = 0; cell < 64; cell++)
            {
                var packed = mesh.PackedBytes(cell * 4);
                var blend = mesh.Blend.Cells[cell];
                for (var k = 0; k < 16; k++)
                {
                    var u = (float)rng.NextDouble();
                    var v = (float)rng.NextDouble();
                    var expected = TerrainCompositor.ComposeFloat(blend, manifest, u, v, texels, alphaSampler);
                    AssertClose(expected, ShaderModel(packed, tiling, texels, alphaSampler, u, v), $"{coord} cell {cell} ({u}, {v})");
                }
            }
        }
    }

    [Fact]
    public void Shader_source_declares_the_contract_and_the_retail_rotations()
    {
        var source = File.ReadAllText(ShaderPath());

        Assert.Contains("render_mode unshaded", source, StringComparison.Ordinal);
        foreach (var uniform in new[]
                 {
                     "ac_sun_dir", "ac_sun_color", "ac_land_ambient", "ac_fog_color", "ac_fog_start", "ac_fog_end", "ac_fog_enabled",
                     "ac_max_mip_level", "terrain_layers", "alpha_layers", "layer_tiling", "debug_view",
                 })
        {
            Assert.Matches(new Regex($@"uniform\s+\w+\s+{uniform}\b"), source);
        }

        // docs/terrain-shader.md §5 rotations: r1 (1 - v, u), r2 (1 - u, 1 - v), r3 (v, 1 - u).
        Assert.Contains("vec2(1.0 - uv.y, uv.x)", source, StringComparison.Ordinal);
        Assert.Contains("vec2(1.0 - uv.x, 1.0 - uv.y)", source, StringComparison.Ordinal);
        Assert.Contains("vec2(uv.y, 1.0 - uv.x)", source, StringComparison.Ordinal);

        // Raw bytes: no sampler may decode sRGB (docs/terrain-shader.md §6).
        var code = string.Join('\n', source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.DoesNotContain("source_color", code, StringComparison.Ordinal);
    }

    /// <summary>The shader's fragment(), debug_view 1 (nearest, no light, no fog), in C#.</summary>
    private static Vector4 ShaderModel(byte[] packed, float[] layerTiling, TerrainTexelSampler texels, TerrainAlphaSampler alphas, float u, float v)
    {
        // vertex(): CUSTOMn arrive as RGBA8 unorm floats; byte_of(x) = int(round(x * 255.0)).
        static int ByteOf(byte b) => (int)MathF.Round(b / 255f * 255f);
        var tex = new[] { ByteOf(packed[0]), ByteOf(packed[1]), ByteOf(packed[2]), ByteOf(packed[3]) };
        var alpha = new[] { ByteOf(packed[4]), ByteOf(packed[5]), ByteOf(packed[6]), ByteOf(packed[7]) };
        var misc = new[] { ByteOf(packed[8]), ByteOf(packed[9]), ByteOf(packed[10]), ByteOf(packed[11]) };

        Vector3 LayerRgb(int layer)
        {
            var t = texels(layer, u * layerTiling[layer], v * layerTiling[layer]);
            return new Vector3(t.R / 255f, t.G / 255f, t.B / 255f);
        }

        (float U, float V) RotUv(int r) => r switch
        {
            1 => (1f - v, u),
            2 => (1f - u, 1f - v),
            3 => (v, 1f - u),
            _ => (u, v),
        };

        static float RetailKeep(float a)
        {
            var a8 = a * 255f;
            return (a8 + Math.Clamp(a8 - 128f, 0f, 1f)) / 256f;
        }

        Vector3 Merge(Vector3 c, int layer, int alphaLayer, int rotation)
        {
            var (au, av) = RotUv(rotation);
            var keep = RetailKeep(alphas(alphaLayer, au, av) / 255f);
            return keep >= 1f ? c : Vector3.Lerp(LayerRgb(layer), c, keep);
        }

        var c = LayerRgb(tex[0]);
        var rots = misc[1];
        if (tex[1] != 255)
        {
            c = Merge(c, tex[1], alpha[0], rots & 3);
            if (tex[2] != 255)
            {
                c = Merge(c, tex[2], alpha[1], (rots >> 2) & 3);
                if (tex[3] != 255)
                {
                    c = Merge(c, tex[3], alpha[2], (rots >> 4) & 3);
                }
            }
        }

        if (alpha[3] != 255)
        {
            c = Merge(c, misc[3], alpha[3], (rots >> 6) & 3);
            if (misc[0] != 255)
            {
                c = Merge(c, misc[3], misc[0], misc[2] & 3);
            }
        }

        return new Vector4(c, 0f);
    }

    private static void AssertClose(Vector4 expected, Vector4 actual, string what)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.X) < 1e-5f && MathF.Abs(expected.Y - actual.Y) < 1e-5f && MathF.Abs(expected.Z - actual.Z) < 1e-5f,
            $"{what}: reference {expected} vs shader model {actual}");
    }

    private static string ShaderPath() => RepositoryFiles.PathOf("src", "Lifestone.Game", "shaders", "terrain", "terrain.gdshader");

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
        rng.NextBytes(pixels);
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 255; // plenty of "keep" texels, as in the real maps
        }

        return new TerrainAlphaImage(size, size, pixels);
    }
}
