using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Textures;

/// <summary>Surface (0x08) flags and the render state retail derives from them (<c>D3DPolyRender::SetSurface</c>).</summary>
public sealed class SurfaceMaterialTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const BlendFactor One = BlendFactor.One;
    private const BlendFactor Zero = BlendFactor.Zero;
    private const BlendFactor SrcA = BlendFactor.SourceAlpha;
    private const BlendFactor InvA = BlendFactor.InverseSourceAlpha;

    /// <summary>Type combinations present in the end-of-retail portal (scratchpad textures/survey2.py) and their counts.</summary>
    private static readonly Dictionary<SurfaceType, int> TypeCensus = new()
    {
        [SurfaceType.Base1Image] = 4904,
        [SurfaceType.Base1ClipMap] = 640,
        [SurfaceType.Base1Image | SurfaceType.Alpha | SurfaceType.Additive] = 161,
        [SurfaceType.Base1Image | SurfaceType.Translucent] = 156,
        [SurfaceType.Base1Solid] = 79,
        [SurfaceType.Base1Solid | SurfaceType.Translucent] = 73,
        [SurfaceType.Base1Image | SurfaceType.Alpha] = 47,
        [SurfaceType.Base1ClipMap | SurfaceType.Translucent] = 27,
        [SurfaceType.Base1ClipMap | SurfaceType.Alpha] = 22,
        [SurfaceType.Base1ClipMap | SurfaceType.Alpha | SurfaceType.Additive] = 19,
        [SurfaceType.Base1ClipMap | SurfaceType.Additive] = 12,
        [SurfaceType.Base1Image | SurfaceType.Additive] = 7,
        [SurfaceType.Base1Image | SurfaceType.Translucent | SurfaceType.Alpha | SurfaceType.Additive] = 2,
        [SurfaceType.Base1Image | SurfaceType.Translucent | SurfaceType.Alpha] = 1,
        [SurfaceType.Base1Solid | SurfaceType.Translucent | SurfaceType.Additive] = 1,
        [SurfaceType.Base1ClipMap | SurfaceType.Translucent | SurfaceType.Alpha | SurfaceType.Additive] = 1,
    };

    private DatSet Dats => fixture.Dats;

    [Fact]
    public void Surface_type_values_match_retail_enum()
    {
        // acclient.h enum SurfaceType (PDB names) == DatReaderWriter SurfaceType.
        Assert.Equal(0x1u, (uint)SurfaceType.Base1Solid);
        Assert.Equal(0x2u, (uint)SurfaceType.Base1Image);
        Assert.Equal(0x4u, (uint)SurfaceType.Base1ClipMap);
        Assert.Equal(0x10u, (uint)SurfaceType.Translucent);
        Assert.Equal(0x20u, (uint)SurfaceType.Diffuse);
        Assert.Equal(0x40u, (uint)SurfaceType.Luminous);
        Assert.Equal(0x100u, (uint)SurfaceType.Alpha);
        Assert.Equal(0x200u, (uint)SurfaceType.InvAlpha);
        Assert.Equal(0x10000u, (uint)SurfaceType.Additive);
        Assert.Equal(0x20000u, (uint)SurfaceType.Detail);
        Assert.Equal(0x10000000u, (uint)SurfaceType.Gouraud);
        Assert.Equal(0x40000000u, (uint)SurfaceType.Stippled);
        Assert.Equal(0x80000000u, (uint)SurfaceType.Perspective);
    }

    /// <summary>Every type combination in the dats, traced by hand through <c>D3DPolyRender::SetSurface</c>.</summary>
    [Theory]
    //          type      transl  src   dst   blend  test   zwrite alpha fog   mode
    [InlineData(0x2u,     0f,     One,  Zero, false, false, true,  255, true,  SurfaceBlendMode.Opaque)]
    [InlineData(0x1u,     0f,     One,  Zero, false, false, true,  255, true,  SurfaceBlendMode.Opaque)]
    [InlineData(0x4u,     0f,     One,  InvA, true,  true,  true,  255, true,  SurfaceBlendMode.AlphaTestBlend)]
    [InlineData(0x104u,   0f,     SrcA, InvA, true,  true,  true,  255, true,  SurfaceBlendMode.AlphaTestBlend)]
    [InlineData(0x10104u, 0f,     SrcA, One,  true,  true,  true,  255, false, SurfaceBlendMode.AlphaTestAdditive)]
    [InlineData(0x10004u, 0f,     One,  One,  true,  true,  true,  255, false, SurfaceBlendMode.AlphaTestAdditive)]
    [InlineData(0x102u,   0f,     SrcA, InvA, true,  false, false, 255, true,  SurfaceBlendMode.AlphaBlend)]
    [InlineData(0x10102u, 0f,     SrcA, One,  true,  false, false, 255, false, SurfaceBlendMode.Additive)]
    [InlineData(0x10002u, 0f,     One,  One,  true,  false, false, 255, false, SurfaceBlendMode.Additive)]
    [InlineData(0x12u,    0.25f,  SrcA, InvA, true,  false, false, 191, true,  SurfaceBlendMode.AlphaBlend)]
    [InlineData(0x11u,    1f,     SrcA, InvA, true,  false, false, 0,   true,  SurfaceBlendMode.AlphaBlend)]
    [InlineData(0x14u,    1f,     SrcA, InvA, true,  false, false, 0,   true,  SurfaceBlendMode.AlphaBlend)]   // translucency cancels the clip test
    [InlineData(0x112u,   0.4f,   SrcA, InvA, true,  false, false, 152, true,  SurfaceBlendMode.AlphaBlend)]   // (1 - 0.4f) * 255 = 152.99999.. truncates
    [InlineData(0x10112u, 0.5f,   SrcA, One,  true,  false, false, 127, false, SurfaceBlendMode.Additive)]    // already blending: factors kept
    [InlineData(0x10011u, 0.5f,   One,  One,  true,  false, false, 127, false, SurfaceBlendMode.Additive)]
    [InlineData(0x10114u, 0.25f,  SrcA, InvA, true,  false, false, 191, false, SurfaceBlendMode.AlphaBlend)]  // clip+translucent drops additive factors
    [InlineData(0x202u,   0f,     InvA, SrcA, true,  false, false, 255, true,  SurfaceBlendMode.AlphaBlend)]  // InvAlpha (not in the dats)
    [InlineData(0x10202u, 0f,     InvA, One,  true,  false, false, 255, false, SurfaceBlendMode.Additive)]
    public void Render_state_follows_retail_SetSurface(
        uint type, float translucency, BlendFactor src, BlendFactor dst, bool blend, bool test, bool zwrite, int alpha, bool fog, SurfaceBlendMode mode)
    {
        var effective = (SurfaceType)type;
        var state = SurfaceMaterial.DeriveRenderState(effective, translucency, textured: (type & 6) != 0);
        Assert.Equal((src, dst, blend, test, zwrite, (byte)alpha, fog, mode),
            (state.SourceBlend, state.DestinationBlend, state.AlphaBlendEnabled, state.AlphaTestEnabled, state.DepthWrite, state.MaterialAlpha, state.FogEnabled, state.BlendMode));
        Assert.Equal(SurfaceMaterial.ClipMapAlphaTestReference, state.AlphaTestReference);
        Assert.Equal(blend || test, state.IsDeferred);
    }

    [Fact]
    public void Translucency_above_epsilon_sets_the_translucent_flag()
    {
        var s = new Surface { Type = SurfaceType.Base1Solid, ColorValue = new DatReaderWriter.Types.ColorARGB { Red = 1, Green = 2, Blue = 3, Alpha = 4 }, Translucency = 0.5f };
        var m = SurfaceMaterial.FromDat(0x08000001, s);
        Assert.Equal(SurfaceType.Base1Solid, m.Type);
        Assert.Equal(SurfaceType.Base1Solid | SurfaceType.Translucent, m.EffectiveType);
        Assert.Equal(0x04010203u, m.SolidColorArgb);
        Assert.Equal(0x7F010203u, m.SolidColorTexel); // stored alpha dropped, (1 - 0.5) * 255 truncated
        Assert.Equal(SurfaceAlphaSource.SolidColorTimesMaterial, m.RenderState.AlphaSource);
        Assert.Equal(SurfaceType.Base1Solid, SurfaceMaterial.FromDat(2, new Surface { Type = SurfaceType.Base1Solid, Translucency = 0.0001f }).EffectiveType);
    }

    [Fact]
    public void Translucency_epsilon_is_the_float_F_EPSILON_compared_as_float()
    {
        // CSurface::InitEnd: translucency > F_EPSILON, both floats (F_EPSILON = 0.0002f; the decompile prints floats
        // with 8 significant digits, hence "0.00019999999"). Exactly 0.0002f is not translucent; the next float is.
        Assert.Equal(0.0002f, SurfaceMaterial.TranslucencyEpsilon);
        Assert.Equal(SurfaceType.Base1Image, SurfaceMaterial.FromDat(3, new Surface { Type = SurfaceType.Base1Image, Translucency = 0.0002f }).EffectiveType);
        Assert.Equal(
            SurfaceType.Base1Image | SurfaceType.Translucent,
            SurfaceMaterial.FromDat(4, new Surface { Type = SurfaceType.Base1Image, Translucency = MathF.BitIncrement(0.0002f) }).EffectiveType);
    }

    /// <summary>Known surfaces (first of each type combination by id; scratchpad textures/surf_samples.py).</summary>
    [RequiresDatsTheory]
    [InlineData(0x08000000u, 0x4u, 0x050025F2u, 0u, 0f, 0f)]
    [InlineData(0x08000002u, 0x2u, 0x050025F4u, 0u, 0f, 0f)]
    [InlineData(0x08000006u, 0x102u, 0x050025F5u, 0u, 0f, 0f)]
    [InlineData(0x08000015u, 0x14u, 0x050016D7u, 0u, 1f, 0f)]
    [InlineData(0x08000023u, 0x10114u, 0x05001470u, 0u, 0.25f, 0f)]
    [InlineData(0x08000031u, 0x10102u, 0x05001359u, 0u, 0f, 1f)]
    [InlineData(0x08000034u, 0x11u, 0u, 0xFF000000u, 1f, 0f)]
    [InlineData(0x0800006Cu, 0x10011u, 0u, 0xFFE6F5FFu, 0.5f, 1f)]
    [InlineData(0x080000DFu, 0x1u, 0u, 0xFF000000u, 0f, 0f)]
    [InlineData(0x08000159u, 0x10104u, 0x05001768u, 0u, 0f, 1f)]
    [InlineData(0x080002F5u, 0x10004u, 0x0500171Eu, 0u, 0f, 1f)]
    [InlineData(0x08000990u, 0x10002u, 0x05001726u, 0u, 0f, 1f)]
    [InlineData(0x080016B2u, 0x112u, 0x0500303Du, 0u, 0.4f, 1f)]
    public void Known_surfaces_decode_their_fields(uint id, uint type, uint surfaceTexture, uint color, float translucency, float luminosity)
    {
        var m = new TextureLoader(Dats).GetSurface(id);
        Assert.NotNull(m);
        Assert.Equal((SurfaceType)type, m.Type);
        Assert.Equal(m.Type, m.EffectiveType);
        Assert.Equal(surfaceTexture, m.SurfaceTextureId);
        Assert.Equal(color, m.SolidColorArgb);
        Assert.Equal(translucency, m.Translucency, 3);
        Assert.Equal(luminosity, m.Luminosity, 3);
        Assert.Equal(1f, m.Diffuse, 3);
        Assert.Equal(SurfaceMaterial.DeriveRenderState(m.EffectiveType, m.Translucency, m.IsTextured), m.RenderState);
    }

    [RequiresDatsFact]
    public void Sun_quad_surface_is_alpha_additive()
    {
        // rendering.md: the sun GfxObj 0x01001348 uses an Additive|Alpha surface; its only surface is 0x080000D1.
        var gfx = TextureTestSupport.ReadUncached<GfxObj>(Dats, 0x01001348u)!;
        var surfaceId = gfx.Surfaces.Single().DataId;
        Assert.Equal(0x080000D1u, surfaceId);
        var m = new TextureLoader(Dats).GetSurface(surfaceId)!;
        Assert.Equal(SurfaceType.Alpha | SurfaceType.Additive, m.Type & (SurfaceType.Alpha | SurfaceType.Additive));
        Assert.Equal((BlendFactor.SourceAlpha, BlendFactor.One, false, false), (m.RenderState.SourceBlend, m.RenderState.DestinationBlend, m.RenderState.DepthWrite, m.RenderState.FogEnabled));
    }

    /// <summary>
    /// Alpha-tested surfaces still blend (<c>D3DPolyRender::SetSurface</c>): palette-expanded clip maps only have alpha
    /// 0 or 255, so they are exact cut-outs, but non-paletted clip-map textures have texels with alpha in [200, 254]
    /// that pass the test and are then blended ONE/INVSRCALPHA. Counts per unique level-0 RenderSurface, matching the
    /// reviewer's independent Python count (scratchpad review-textures/clip_alpha.py).
    /// </summary>
    [RequiresHighResDatsFact]
    public void Non_paletted_clip_maps_have_texels_that_pass_the_alpha_test_and_blend()
    {
        var loader = new TextureLoader(Dats, paletteCacheCapacity: 5000);
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<Surface>().ToArray());
        var seen = new HashSet<uint>();
        var blendedAfterTest = new Dictionary<PixelFormat, int>();
        var allTested = new Dictionary<SurfaceBlendMode, int>();
        var palettedPartial = 0;
        foreach (var id in ids)
        {
            var m = loader.GetSurface(id)!;
            if (m.RenderState.BlendMode is not (SurfaceBlendMode.AlphaTestBlend or SurfaceBlendMode.AlphaTestAdditive))
            {
                continue;
            }

            var d = loader.DecodeSurface(id, TextureDecodeMode.Native)!;
            var partial = d.Texture!.AnyAlphaInRange(SurfaceMaterial.ClipMapAlphaTestReference, 254);
            if (d.Palette is not null)
            {
                palettedPartial += d.Texture.AnyAlphaInRange(1, 254) ? 1 : 0; // only 0 or 255: an exact cut-out
                continue;
            }

            allTested[m.RenderState.BlendMode] = allTested.GetValueOrDefault(m.RenderState.BlendMode) + (partial ? 1 : 0);
            var plainClipMap = (m.RenderState.SourceBlend, m.RenderState.DestinationBlend) == (One, InvA);
            if (plainClipMap && seen.Add(d.Resolution!.RenderSurfaceId) && partial)
            {
                blendedAfterTest[d.Texture.SourceFormat] = blendedAfterTest.GetValueOrDefault(d.Texture.SourceFormat) + 1;
            }
        }

        output.WriteLine(string.Join(", ", blendedAfterTest.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        output.WriteLine("surfaces with alpha in [200,254] by mode: " + string.Join(", ", allTested.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Equal(0, palettedPartial);
        Assert.Equal(109, allTested.GetValueOrDefault(SurfaceBlendMode.AlphaTestBlend));    // surfaces, not unique textures
        Assert.Equal(1, allTested.GetValueOrDefault(SurfaceBlendMode.AlphaTestAdditive));
        Assert.Equal(90, blendedAfterTest.Values.Sum());
        Assert.Equal(61, blendedAfterTest[PixelFormat.PFID_DXT5]);
        Assert.Equal(26, blendedAfterTest[PixelFormat.PFID_A8R8G8B8]);
        Assert.Equal(2, blendedAfterTest[PixelFormat.PFID_DXT3]);
        Assert.Equal(1, blendedAfterTest[PixelFormat.PFID_A4R4G4B4]);
    }

    [RequiresHighResDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_surface_builds_a_material_and_decodes_its_texture()
    {
        var loader = new TextureLoader(Dats, paletteCacheCapacity: 5000);
        var ids = Dats.Read(d => d.Portal.GetAllIdsOfType<Surface>().ToArray());
        Assert.Equal(6152, ids.Length);
        var census = new Dictionary<SurfaceType, int>();
        var modes = new Dictionary<SurfaceBlendMode, int>();
        var failures = new List<string>();
        var clipWithTransparency = 0;
        foreach (var id in ids)
        {
            var m = loader.GetSurface(id)!;
            census[m.Type] = census.GetValueOrDefault(m.Type) + 1;
            modes[m.RenderState.BlendMode] = modes.GetValueOrDefault(m.RenderState.BlendMode) + 1;
            Assert.Equal(m.Type, m.EffectiveType); // every Translucent-flagged surface already has translucency > 0.0002
            try
            {
                var d = loader.DecodeSurface(id, TextureDecodeMode.Native)!;
                Assert.Equal(m.IsTextured, d.Texture is not null);
                if (m.IsTextured)
                {
                    Assert.Equal(0u, m.OriginalPaletteId); // retail replaces it with the texture's palette anyway
                    if (m.IsClipMap && d.Palette is not null && d.Texture!.HasAlpha)
                    {
                        clipWithTransparency++;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or NotSupportedException)
            {
                failures.Add($"0x{id:X8}: {ex.Message}");
            }
        }

        output.WriteLine(string.Join(", ", modes.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")));
        output.WriteLine($"palette clip maps with transparent texels: {clipWithTransparency}");
        Assert.Empty(failures);
        Assert.Equal(TypeCensus.OrderBy(kv => kv.Key), census.OrderBy(kv => kv.Key));
        Assert.Equal(4904 + 79, modes[SurfaceBlendMode.Opaque]);
        Assert.Equal(640 + 22, modes[SurfaceBlendMode.AlphaTestBlend]);
        Assert.Equal(19 + 12, modes[SurfaceBlendMode.AlphaTestAdditive]);
        Assert.Equal(161 + 7 + 2 + 1, modes[SurfaceBlendMode.Additive]);
        Assert.Equal(156 + 73 + 47 + 27 + 1 + 1, modes[SurfaceBlendMode.AlphaBlend]);
    }
}
