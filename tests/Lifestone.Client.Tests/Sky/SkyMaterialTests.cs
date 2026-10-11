using DatReaderWriter.Enums;
using Lifestone.Client.Rendering;
using Lifestone.Client.Sky;
using Lifestone.Data.Geometry;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Sky;

/// <summary>
/// The part lighting state retail keeps on sky objects (<see cref="SkyPartMaterial"/>) and the surface variants it
/// leads to (<see cref="SkySurfaceVariant"/>): synthetic surfaces for the rules, the user's sky surfaces for the data.
/// </summary>
public sealed class SkyMaterialTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    private static SurfaceMaterial Synthetic(SurfaceType type, float luminosity = 0f, float translucency = 0f)
    {
        var effective = translucency > SurfaceMaterial.TranslucencyEpsilon ? type | SurfaceType.Translucent : type;
        return new SurfaceMaterial
        {
            Id = 0x08FFFFFF,
            Type = type,
            EffectiveType = effective,
            Luminosity = luminosity,
            Translucency = translucency,
            Diffuse = 1f,
            RenderState = SurfaceMaterial.DeriveRenderState(effective, translucency, (type & (SurfaceType.Base1Image | SurfaceType.Base1ClipMap)) != 0),
        };
    }

    [Fact]
    public void A_fresh_part_has_no_material_and_draws_with_the_vertex_colour()
    {
        var m = SkyPartMaterial.Default;
        Assert.False(m.HasMaterial);
        Assert.False(m.HasAlpha);
        Assert.Equal(1f, m.DiffuseFactor);
        var translucent = Synthetic(SurfaceType.Base1Image | SurfaceType.Alpha, translucency: 0.5f);
        Assert.Equal(127f / 255f, m.VertexAlpha(translucent)); // copyVert: (uint8)((1 - 0.5) * 255)
        Assert.Equal(0f, m.Emissive(translucent));
    }

    [Fact]
    public void Settings_make_a_material_whose_alpha_replaces_the_vertex_alpha()
    {
        var surface = Synthetic(SurfaceType.Base1Image | SurfaceType.Alpha, translucency: 0.5f);
        var m = SkyPartMaterial.Default.WithLuminosity(0.15f).WithDiffusion(0.15f);
        Assert.True(m.HasMaterial);
        Assert.False(m.HasAlpha);
        Assert.Equal(1f, m.VertexAlpha(surface)); // material Diffuse.a = 1 - 0
        Assert.Equal(0.15f, m.Emissive(surface));
        Assert.Equal(0.15f, m.DiffuseFactor);

        var faded = m.WithTranslucency(0.7f);
        Assert.True(faded.HasAlpha);
        Assert.Equal(0.3f, faded.VertexAlpha(surface), 6);

        // Back to the defaults: CurSettingsAreDefault drops the material.
        Assert.False(faded.WithLuminosity(0f).WithDiffusion(1f).WithTranslucency(0f).HasMaterial);
    }

    [Fact]
    public void Translucency_one_hides_the_part_and_changes_nothing_else()
    {
        var m = SkyPartMaterial.Default.WithTranslucency(0.3f).WithTranslucency(1.0f);
        Assert.True(m.Hidden);
        Assert.Equal(0.3f, m.Translucency);
        var shown = m.WithTranslucency(0.99f);
        Assert.False(shown.Hidden);
        Assert.Equal(0.99f, shown.Translucency);
        Assert.Equal(0f, SkyPartMaterial.Default.WithTranslucency(-1f).Translucency);
    }

    [Fact]
    public void A_luminous_surface_overrides_the_part_luminosity()
    {
        var luminous = Synthetic(SurfaceType.Base1Image, luminosity: 1f);
        var m = SkyPartMaterial.Default.WithLuminosity(0.11f);
        Assert.Equal(1f, m.Emissive(luminous)); // RenderMeshSubset writes the Surface's luminosity into the emissive
    }

    [Fact]
    public void Material_alpha_forces_mix_and_drops_the_alpha_test_unless_the_surface_already_blends_without_one()
    {
        var faded = SkyPartMaterial.Default.WithTranslucency(0.5f);
        var opaque = Synthetic(SurfaceType.Base1Image);
        var additive = Synthetic(SurfaceType.Base1Image | SurfaceType.Alpha | SurfaceType.Additive);
        var clip = Synthetic(SurfaceType.Base1ClipMap);

        Assert.Equal(SurfaceBlend.Opaque, SkySurfaceVariant.For(opaque, TextureAddressMode.Wrap, SkyPartMaterial.Default, SkyPass.Before).Blend);
        Assert.Equal(SurfaceBlend.Mix, SkySurfaceVariant.For(opaque, TextureAddressMode.Wrap, faded, SkyPass.Before).Blend);
        Assert.Equal(SurfaceBlend.AddSourceAlpha, SkySurfaceVariant.For(additive, TextureAddressMode.Wrap, faded, SkyPass.Before).Blend);

        var clipPlain = SkySurfaceVariant.For(clip, TextureAddressMode.Clamp, SkyPartMaterial.Default, SkyPass.Before);
        Assert.Equal((SurfaceBlend.Premultiplied, true), (clipPlain.Blend, clipPlain.AlphaTest));
        var clipFaded = SkySurfaceVariant.For(clip, TextureAddressMode.Clamp, faded, SkyPass.Before);
        Assert.Equal((SurfaceBlend.Mix, false), (clipFaded.Blend, clipFaded.AlphaTest));
    }

    [RequiresDatsFact]
    public void The_regions_sky_surfaces_map_to_the_expected_variants()
    {
        // Dome side (lum 1, opaque), stars (black, alpha blended), sun (additive with alpha), moon (clip map),
        // cloud layer (alpha blended), the translucent cloud 0x08000023 and the rain cylinder 0x080000C5.
        Assert.Equal(SurfaceBlend.Opaque, Variant(0x08000048, TextureAddressMode.Clamp).Blend);
        Assert.Equal(1f, fixture.Surface(0x08000048).Luminosity);
        Assert.Equal(SurfaceBlend.Mix, Variant(0x0800004D, TextureAddressMode.Wrap).Blend);
        Assert.Equal(SurfaceBlend.AddSourceAlpha, Variant(0x080000D1, TextureAddressMode.Clamp).Blend);
        var moon = Variant(0x080000D2, TextureAddressMode.Clamp);
        Assert.Equal((SurfaceBlend.Premultiplied, true), (moon.Blend, moon.AlphaTest));
        Assert.Equal(SurfaceBlend.Mix, Variant(0x080000D4, TextureAddressMode.Wrap).Blend);
        Assert.Equal(SurfaceBlend.Mix, Variant(0x08000023, TextureAddressMode.Wrap).Blend);
        Assert.Equal(SurfaceBlend.AddSourceAlpha, Variant(0x080000C5, TextureAddressMode.Wrap).Blend);
        Assert.Equal(127, fixture.Surface(0x080000C5).RenderState.MaterialAlpha);
    }

    private SkySurfaceVariant Variant(uint surface, TextureAddressMode addressing) =>
        SkySurfaceVariant.For(fixture.Surface(surface), addressing, SkyPartMaterial.Default, SkyPass.Before);
}
