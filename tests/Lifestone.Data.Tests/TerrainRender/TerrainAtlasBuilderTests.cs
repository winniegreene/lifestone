using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Tests.Terrain;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>
/// The decoded texture arrays (<see cref="TerrainAtlasBuilder"/>): layer order, sizes, mip chains and the shader's tiling
/// and LOD uniforms on synthetic data; on the real dats, every layer against an independent raw decode.
/// </summary>
public sealed class TerrainAtlasBuilderTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void Layers_follow_the_manifest_order_with_full_mip_chains()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        var atlas = TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, 16));

        Assert.Equal(manifest.TerrainLayers.Count, atlas.ColorLayers.Count);
        Assert.Equal(manifest.AlphaLayers.Count, atlas.AlphaLayers.Count);
        Assert.Equal(16, atlas.ColorSize);
        Assert.Equal(16, atlas.AlphaSize);
        Assert.Equal(5, atlas.ColorMipLevels);

        for (var layer = 0; layer < manifest.TerrainLayers.Count; layer++)
        {
            var id = manifest.TerrainLayers[layer].SurfaceTextureId;
            Assert.Equal(id, atlas.ColorSources[layer].SurfaceTextureId);
            Assert.Equal(TerrainMipChain.ByteLength(16, 4), atlas.ColorLayers[layer].Length);
            Assert.Equal(Synthetic(id, false, 16).Texture.Data, atlas.ColorLayers[layer][..(16 * 16 * 4)]);
            Assert.Equal(TerrainMipChain.Build(Synthetic(id, false, 16).Texture.Data, 16, 4), atlas.ColorLayers[layer]);
            Assert.Equal(16, atlas.ColorSources[layer].SourceSize);
        }

        for (var layer = 0; layer < manifest.AlphaLayers.Count; layer++)
        {
            var id = manifest.AlphaLayers[layer].SurfaceTextureId;
            Assert.Equal(TerrainMipChain.Build(Synthetic(id, true, 16).Texture.Data, 16, 1), atlas.AlphaLayers[layer]);
        }

        // TerrainImage / TerrainAlphaImage views are level 0, row 0 first, RGBA in order.
        var image = atlas.ColorImage(3);
        var raw = Synthetic(manifest.TerrainLayers[3].SurfaceTextureId, false, 16).Texture.Data;
        Assert.Equal(new TerrainRgba(raw[4 * 17], raw[(4 * 17) + 1], raw[(4 * 17) + 2], raw[(4 * 17) + 3]), image[1, 1]);
        Assert.Equal(Synthetic(manifest.AlphaLayers[2].SurfaceTextureId, true, 16).Texture.Data, atlas.AlphaImage(2).Pixels);
    }

    [Theory]
    [InlineData(1024u, 2u, 512, 512, 0f, 1f)] // Dereth: a 512² layer twice per cell = merged density; alpha 512 -> 1024
    [InlineData(1024u, 1u, 512, 512, 1f, 1f)]
    [InlineData(512u, 2u, 256, 512, 0f, 0f)]
    public void Tiling_and_lod_offsets_describe_the_density_relative_to_the_merged_texture(
        uint baseTexSize, uint tiling, int colorSize, int alphaSize, float colorLodOffset, float alphaLodOffset)
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped(baseTexSize, tiling));
        var atlas = TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, isAlpha ? alphaSize : colorSize));
        Assert.All(atlas.LayerTiling, t => Assert.Equal((float)tiling, t));
        Assert.All(atlas.LayerLodOffset, o => Assert.Equal(colorLodOffset, o, 5));
        Assert.Equal(alphaLodOffset, atlas.AlphaLodOffset, 5);
    }

    [Fact]
    public void Layers_of_different_sizes_fail_loudly_instead_of_being_resampled()
    {
        // Retail addresses each layer by its own size (ImgTex::TileCSI / MergeTexture); sharing an array would need a
        // resample retail never does, so the builder refuses (TODO(terrain)) rather than upscaling silently.
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        var small = manifest.TerrainLayers[5].SurfaceTextureId;
        var colour = Assert.Throws<NotSupportedException>(() =>
            TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, !isAlpha && id == small ? 4 : 8)));
        Assert.Contains("TODO(terrain)", colour.Message, StringComparison.Ordinal);
        Assert.Contains($"0x{small:X8} 4²", colour.Message, StringComparison.Ordinal);
        Assert.Contains("colour layers", colour.Message, StringComparison.Ordinal);

        var smallAlpha = manifest.AlphaLayers[2].SurfaceTextureId;
        var alpha = Assert.Throws<NotSupportedException>(() =>
            TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, isAlpha && id == smallAlpha ? 16 : 8)));
        Assert.Contains($"0x{smallAlpha:X8} 16²", alpha.Message, StringComparison.Ordinal);
        Assert.Contains("alpha maps", alpha.Message, StringComparison.Ordinal);

        // The two arrays may differ from each other (each is one size).
        var mixed = TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, isAlpha ? 16 : 8));
        Assert.Equal(8, mixed.ColorSize);
        Assert.Equal(16, mixed.AlphaSize);
    }

    [Fact]
    public void Unsupported_sizes_and_formats_fail_loudly()
    {
        var manifest = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        var odd = manifest.TerrainLayers[1].SurfaceTextureId;
        Assert.Throws<NotSupportedException>(() =>
            TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, isAlpha, !isAlpha && id == odd ? 3 : 8), parallel: false));

        // An alpha map that decodes to colour is outside what retail's MergeTexture reads.
        Assert.Throws<NotSupportedException>(() =>
            TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => Synthetic(id, false, 8), parallel: false));

        var rectangular = new TerrainAtlasSourceTexture(
            new DecodedTexture { Width = 8, Height = 4, Format = DecodedTextureFormat.Rgba8, Data = new byte[8 * 4 * 4], HasAlpha = false },
            1, TextureDat.Portal);
        Assert.Throws<NotSupportedException>(() =>
            TerrainAtlasBuilder.Build(manifest, (id, isAlpha) => isAlpha ? Synthetic(id, true, 8) : rectangular, parallel: false));
    }

    [RequiresDatsFact]
    public void Real_atlas_matches_an_independent_raw_decode_of_every_layer()
    {
        var dats = fixture.Dats;
        var atlas = TerrainAtlasBuilder.FromDats(dats);
        var manifest = dats.TerrainAtlas;

        Assert.Equal(30, atlas.ColorLayers.Count);
        Assert.Equal(8, atlas.AlphaLayers.Count);
        Assert.Equal(512, atlas.ColorSize);
        Assert.Equal(512, atlas.AlphaSize);
        Assert.All(atlas.ColorSources.Concat(atlas.AlphaSources), s => Assert.Equal(512, s.SourceSize));
        Assert.Equal(30L * 1_398_100, atlas.ColorBytes);
        Assert.Equal(8L * 349_525, atlas.AlphaBytes);
        Assert.All(atlas.LayerTiling, t => Assert.Equal(2f, t));
        Assert.All(atlas.LayerLodOffset, o => Assert.Equal(0f, o));
        Assert.Equal(1f, atlas.AlphaLodOffset);

        for (var layer = 0; layer < manifest.TerrainLayers.Count; layer++)
        {
            // TerrainTestData decodes the A8R8G8B8 bytes itself (B, G, R, A in memory) with no product code involved.
            var expected = TerrainTestData.DecodeColor(dats, manifest.TerrainLayers[layer].SurfaceTextureId);
            var actual = atlas.ColorImage(layer);
            Assert.Equal(expected.Pixels, actual.Pixels);
            Assert.Equal(PixelFormat.PFID_A8R8G8B8, atlas.ColorSources[layer].SourceFormat);
            Assert.Equal(TextureDat.Portal, atlas.ColorSources[layer].Dat);
            Assert.Equal(TerrainMipChain.Build(atlas.ColorLayers[layer].AsSpan(0, 512 * 512 * 4), 512, 4), atlas.ColorLayers[layer]);
        }

        for (var layer = 0; layer < manifest.AlphaLayers.Count; layer++)
        {
            var expected = TerrainTestData.DecodeAlpha(dats, manifest.AlphaLayers[layer].SurfaceTextureId);
            Assert.Equal(expected.Pixels, atlas.AlphaImage(layer).Pixels);
            Assert.Equal(PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA, atlas.AlphaSources[layer].SourceFormat);
            Assert.Equal(dats.HighRes is null ? TextureDat.Portal : TextureDat.HighRes, atlas.AlphaSources[layer].Dat);
        }
    }

    [RequiresDatsFact]
    public void Real_grass_is_green_and_dirt_is_brown_after_decoding()
    {
        // Guards the channel order end to end: LushGrass (type 3) must average more green than blue and red at least as
        // much as blue; PackedDirt (type 7) must be red > green > blue. A B/R swap would make grass teal and dirt blue.
        var dats = fixture.Dats;
        var atlas = TerrainAtlasBuilder.FromDats(dats);
        var grass = Mean(atlas.ColorImage(dats.TerrainAtlas.LayerForTerrainType(3)));
        var dirt = Mean(atlas.ColorImage(dats.TerrainAtlas.LayerForTerrainType(7)));
        Assert.True(grass.G > grass.B + 20 && grass.R > grass.B, $"grass mean {grass}");
        Assert.True(dirt.R > dirt.G && dirt.G > dirt.B, $"dirt mean {dirt}");
    }

    [RequiresDatsFact]
    public void The_SurfaceTexture_level_choice_does_not_change_the_terrain_pixels()
    {
        // Colour layers are single-level SurfaceTextures; each alpha map's client_highres.dat level [0] has the same bytes
        // as its portal level [1]. So retail's default (ShouldDropHighDetail -> level 1) and Lifestone's High agree.
        var dats = fixture.Dats;
        foreach (var layer in dats.TerrainAtlas.TerrainLayers)
        {
            Assert.True(dats.TryGetPortal<SurfaceTexture>(layer.SurfaceTextureId, out var st));
            Assert.Single(st.Textures);
        }

        var high = TerrainAtlasBuilder.FromDats(dats, detail: TextureDetail.High);
        var low = TerrainAtlasBuilder.FromDats(dats, detail: TextureDetail.Low);
        for (var i = 0; i < high.AlphaLayers.Count; i++)
        {
            Assert.Equal(high.AlphaLayers[i], low.AlphaLayers[i]);
            if (dats.HighRes is not null)
            {
                Assert.NotEqual(high.AlphaSources[i].RenderSurfaceId, low.AlphaSources[i].RenderSurfaceId);
            }
        }

        for (var i = 0; i < high.ColorLayers.Count; i++)
        {
            Assert.Equal(high.ColorSources[i].RenderSurfaceId, low.ColorSources[i].RenderSurfaceId);
        }
    }

    private static (double R, double G, double B) Mean(TerrainImage image)
    {
        double r = 0, g = 0, b = 0;
        foreach (var p in image.Pixels)
        {
            r += p.R;
            g += p.G;
            b += p.B;
        }

        var n = image.Pixels.Length;
        return (r / n, g / n, b / n);
    }

    /// <summary>A deterministic texture whose bytes depend on the SurfaceTexture id, so a mixed-up layer order shows.</summary>
    private static TerrainAtlasSourceTexture Synthetic(uint id, bool isAlpha, int size)
    {
        var rng = new Random((int)id ^ (isAlpha ? 0x5A5A : 0) ^ (size * 7919));
        var data = new byte[size * size * (isAlpha ? 1 : 4)];
        rng.NextBytes(data);
        var texture = new DecodedTexture
        {
            Width = size,
            Height = size,
            Format = isAlpha ? DecodedTextureFormat.Alpha8 : DecodedTextureFormat.Rgba8,
            Data = data,
            HasAlpha = true,
            SourceId = 0x06000000u | (id & 0xFFFFu),
            SourceFormat = isAlpha ? PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA : PixelFormat.PFID_A8R8G8B8,
        };
        return new TerrainAtlasSourceTexture(texture, texture.SourceId, TextureDat.Portal);
    }
}
