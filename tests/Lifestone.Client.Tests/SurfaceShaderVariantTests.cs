using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests;

public sealed class SurfaceShaderVariantTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static SurfaceMaterial Material(SurfaceType type, float translucency = 0f) => new()
    {
        Id = 0x08000001,
        Type = type,
        EffectiveType = translucency > SurfaceMaterial.TranslucencyEpsilon ? type | SurfaceType.Translucent : type,
        Translucency = translucency,
        RenderState = SurfaceMaterial.DeriveRenderState(
            translucency > SurfaceMaterial.TranslucencyEpsilon ? type | SurfaceType.Translucent : type,
            translucency,
            (type & (SurfaceType.Base1Image | SurfaceType.Base1ClipMap)) != 0),
    };

    [Theory]
    [InlineData(SurfaceType.Base1Image, SurfaceBlend.Opaque, false, true, true)]
    [InlineData(SurfaceType.Base1ClipMap, SurfaceBlend.Opaque, true, true, true)] // cut-out: no passing texel blends
    [InlineData(SurfaceType.Base1ClipMap | SurfaceType.Alpha, SurfaceBlend.Opaque, true, true, true)]
    [InlineData(SurfaceType.Base1ClipMap | SurfaceType.Additive, SurfaceBlend.AddOne, true, true, false)]
    [InlineData(SurfaceType.Base1ClipMap | SurfaceType.Alpha | SurfaceType.Additive, SurfaceBlend.AddSourceAlpha, true, true, false)]
    [InlineData(SurfaceType.Base1Image | SurfaceType.Alpha | SurfaceType.Additive, SurfaceBlend.AddSourceAlpha, false, false, false)]
    [InlineData(SurfaceType.Base1Image | SurfaceType.Additive, SurfaceBlend.AddOne, false, false, false)]
    [InlineData(SurfaceType.Base1Image | SurfaceType.Alpha, SurfaceBlend.Mix, false, false, true)]
    public void Retail_render_state_maps_to_the_documented_variant(SurfaceType type, SurfaceBlend blend, bool test, bool depth, bool fog)
    {
        var v = SurfaceShaderVariant.For(Material(type), passingTexelsBlend: false, TextureAddressMode.Wrap);
        Assert.Equal((blend, test, depth, fog), (v.Blend, v.AlphaTest, v.DepthWrite, v.Fog));
    }

    [Fact]
    public void Alpha_tested_surfaces_keep_their_blend_when_passing_texels_are_translucent()
    {
        // docs/textures.md §5 rule 2: ONE/INVSRCALPHA after the test; texels with alpha 200-254 blend.
        var clip = SurfaceShaderVariant.For(Material(SurfaceType.Base1ClipMap), passingTexelsBlend: true, TextureAddressMode.Clamp);
        Assert.Equal((SurfaceBlend.Premultiplied, true, true), (clip.Blend, clip.AlphaTest, clip.DepthWrite));
        var clipAlpha = SurfaceShaderVariant.For(Material(SurfaceType.Base1ClipMap | SurfaceType.Alpha), passingTexelsBlend: true, TextureAddressMode.Clamp);
        Assert.Equal(SurfaceBlend.Mix, clipAlpha.Blend);
        Assert.Equal("premultiplied+test.z.back.clamp.tex.fog", clip.Name);
    }

    [Fact]
    public void Translucency_turns_a_clip_map_into_an_untested_alpha_blend()
    {
        var v = SurfaceShaderVariant.For(Material(SurfaceType.Base1ClipMap, translucency: 0.5f), passingTexelsBlend: false, TextureAddressMode.Wrap);
        Assert.Equal((SurfaceBlend.Mix, false, false), (v.Blend, v.AlphaTest, v.DepthWrite));
    }

    [Fact]
    public void An_inexpressible_blend_pair_fails_loudly()
    {
        var material = Material(SurfaceType.Base1Image | SurfaceType.InvAlpha); // INVSRCALPHA/SRCALPHA
        Assert.Throws<NotSupportedException>(() => SurfaceShaderVariant.For(material, false, TextureAddressMode.Wrap));
        var additive = SurfaceShaderVariant.For(Material(SurfaceType.Base1Image | SurfaceType.InvAlpha | SurfaceType.Additive), false, TextureAddressMode.Wrap);
        Assert.Equal(SurfaceBlend.AddInverseSourceAlpha, additive.Blend);
    }

    [Fact]
    public void Reachable_variants_cover_both_addressings_and_both_cut_out_forms()
    {
        var clip = Material(SurfaceType.Base1ClipMap);
        var shortcut = SurfaceShaderVariant.Reachable([clip]);
        Assert.Equal(4, shortcut.Count); // {opaque+test, premultiplied+test} x {wrap, clamp}
        Assert.Contains(shortcut, v => v is { Blend: SurfaceBlend.Opaque, AlphaTest: true, Addressing: TextureAddressMode.Clamp });
        var exact = SurfaceShaderVariant.Reachable([clip], exactCutouts: true);
        Assert.Equal(2, exact.Count);
        Assert.All(exact, v => Assert.Equal(SurfaceBlend.Premultiplied, v.Blend));
        Assert.Empty(SurfaceShaderVariant.Reachable([Material(SurfaceType.Base1Image | SurfaceType.InvAlpha)])); // error material
    }

    [RequiresDatsFact]
    public void The_end_of_retail_surfaces_need_26_shader_variants()
    {
        // Compiled once at start-up (RenderResources.WarmUp) so no attach step pays Godot's first-use shader compile.
        var loader = new TextureLoader(fixture.Dats);
        var materials = fixture.Dats.GetAllPortalIds<Surface>().Select(loader.GetSurface).OfType<SurfaceMaterial>().ToList();
        Assert.Equal(26, SurfaceShaderVariant.Reachable(materials).Count);
        Assert.True(SurfaceShaderVariant.Reachable(materials, exactCutouts: true).Count < 26);
    }

    [RequiresDatsFact]
    public void Every_end_of_retail_surface_maps_to_a_variant_with_the_documented_counts()
    {
        // docs/textures.md §5 table (6,152 surfaces): Opaque 4,983 (Image 4,904 + Solid 79); clip maps 662 (ClipMap 640 +
        // ClipMap|Alpha 22, drawn as cut-outs when no passing texel blends); ClipMap|Alpha|Additive 19; ClipMap|Additive 12;
        // SRCALPHA/ONE 163 (Image|Alpha|Additive 161 + Image|Translucent|Alpha|Additive 2); ONE/ONE 8 (Image|Additive 7 +
        // Solid|Translucent|Additive 1); alpha blend 305 (156 + 73 + 47 + 27 + 1 + 1).
        var loader = new TextureLoader(fixture.Dats);
        var ids = fixture.Dats.GetAllPortalIds<Surface>();
        Assert.Equal(6_152, ids.Count);
        var counts = new Dictionary<(SurfaceBlend, bool AlphaTest), int>();
        int textured = 0, fogOff = 0, depthWrite = 0;
        foreach (var id in ids)
        {
            var material = loader.GetSurface(id)!;
            var v = SurfaceShaderVariant.For(material, passingTexelsBlend: false, TextureAddressMode.Wrap);
            counts[(v.Blend, v.AlphaTest)] = counts.GetValueOrDefault((v.Blend, v.AlphaTest)) + 1;
            textured += v.Textured ? 1 : 0;
            fogOff += v.Fog ? 0 : 1;
            depthWrite += v.DepthWrite ? 1 : 0;
        }

        Assert.Equal(4_983, counts[(SurfaceBlend.Opaque, false)]);
        Assert.Equal(662, counts[(SurfaceBlend.Opaque, true)]);
        Assert.Equal(19, counts[(SurfaceBlend.AddSourceAlpha, true)]);
        Assert.Equal(12, counts[(SurfaceBlend.AddOne, true)]);
        Assert.Equal(163, counts[(SurfaceBlend.AddSourceAlpha, false)]);
        Assert.Equal(8, counts[(SurfaceBlend.AddOne, false)]);
        Assert.Equal(305, counts[(SurfaceBlend.Mix, false)]);
        Assert.Equal(7, counts.Count);

        // Fog is off for every Additive surface (202 with the additive factors + ClipMap|Translucent|Alpha|Additive).
        Assert.Equal(203, fogOff);

        // Depth is written by opaque and alpha-tested surfaces only.
        Assert.Equal(4_983 + 662 + 19 + 12, depthWrite);
        Assert.Equal(6_152 - 79 - 73 - 1, textured);
    }

    /// <summary>
    /// Retail's detail pass (render-fidelity fix; docs/render-contract.md §2.4): building detail only with object lighting,
    /// environment detail only with EnvCell lighting, and only on textured opaque surfaces without an alpha test (the
    /// detail texture's alpha, 24-46, never passes the clip-map test; blended subsets are left out).
    /// </summary>
    [Fact]
    public void Detail_pass_applies_only_where_retail_draws_it()
    {
        var opaqueTextured = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.Back, TextureAddressMode.Wrap, true, true);
        Assert.Equal(SurfaceDetail.Building, opaqueTextured.WithDetail(SurfaceDetail.Building).Detail);
        Assert.Equal(SurfaceDetail.None, opaqueTextured.WithDetail(SurfaceDetail.Environment).Detail);
        Assert.Equal(SurfaceDetail.None, opaqueTextured.WithDetail(SurfaceDetail.None).Detail);

        var cell = opaqueTextured with { Lighting = SurfaceLighting.EnvCell };
        Assert.Equal(SurfaceDetail.Environment, cell.WithDetail(SurfaceDetail.Environment).Detail);
        Assert.Equal(SurfaceDetail.None, cell.WithDetail(SurfaceDetail.Building).Detail);
        Assert.Equal(SurfaceDetail.None, (opaqueTextured with { Lighting = SurfaceLighting.CellObject }).WithDetail(SurfaceDetail.Environment).Detail);
        Assert.Equal(SurfaceDetail.None, (opaqueTextured with { Lighting = SurfaceLighting.CellObject }).WithDetail(SurfaceDetail.Building).Detail);

        Assert.Equal(SurfaceDetail.None, (opaqueTextured with { AlphaTest = true }).WithDetail(SurfaceDetail.Building).Detail);
        Assert.Equal(SurfaceDetail.None, (opaqueTextured with { Textured = false }).WithDetail(SurfaceDetail.Building).Detail);
        foreach (var blend in new[] { SurfaceBlend.Mix, SurfaceBlend.Premultiplied, SurfaceBlend.AddOne, SurfaceBlend.AddSourceAlpha })
        {
            Assert.Equal(SurfaceDetail.None, (opaqueTextured with { Blend = blend, DepthWrite = false }).WithDetail(SurfaceDetail.Building).Detail);
        }

        // A detail variant is a different material key and shader, and says so in its name.
        var detailed = opaqueTextured.WithDetail(SurfaceDetail.Building);
        Assert.NotEqual(opaqueTextured, detailed);
        Assert.EndsWith(".detail-building", detailed.Name, StringComparison.Ordinal);
        Assert.Equal(opaqueTextured, detailed.WithDetail(SurfaceDetail.None));
    }

    [RequiresDatsFact]
    public void Detail_variants_of_the_end_of_retail_surfaces()
    {
        // Every Surface the mesh path can draw, as a building and as an EnvCell: the detail pass needs exactly two more
        // variants per lighting rule (opaque textured, WRAP and CLAMP, fog on; no textured opaque surface is fog-off).
        var loader = new TextureLoader(fixture.Dats);
        var materials = fixture.Dats.GetAllPortalIds<Surface>().Select(id => loader.GetSurface(id)!).ToArray();
        var reachable = SurfaceShaderVariant.Reachable(materials);
        var building = reachable.Select(v => v.WithDetail(SurfaceDetail.Building)).Where(v => v.Detail != SurfaceDetail.None).ToHashSet();
        var cells = reachable.Select(v => (v with { Lighting = SurfaceLighting.EnvCell }).WithDetail(SurfaceDetail.Environment))
            .Where(v => v.Detail != SurfaceDetail.None).ToHashSet();
        Assert.Equal(2, building.Count);
        Assert.Equal(2, cells.Count);
        Assert.All(building.Concat(cells), v => Assert.True(v is { Blend: SurfaceBlend.Opaque, AlphaTest: false, Textured: true, Fog: true }, v.Name));
    }
    [Fact]
    public void Creature_mode_variants_have_no_fog_and_no_detail_pass()
    {
        var fogged = new SurfaceShaderVariant(SurfaceBlend.Opaque, false, true, SurfaceCull.Back, TextureAddressMode.Wrap, Textured: true, Fog: true);
        var creature = fogged.WithLighting(SurfaceLighting.CreatureMode);
        Assert.False(creature.Fog);
        Assert.Equal(SurfaceDetail.None, creature.Detail);
        Assert.Equal("opaque.z.back.wrap.tex.nofog.creaturemode", creature.Name);
        Assert.Equal(SurfaceDetail.None, creature.WithDetail(SurfaceDetail.Building).Detail);
        Assert.Equal(SurfaceDetail.None, creature.WithDetail(SurfaceDetail.Environment).Detail);

        // Variants that differ only in fog (or in a detail pass) collapse to one: the warm-up compiles each once.
        Assert.Equal(creature, (fogged with { Fog = false }).WithLighting(SurfaceLighting.CreatureMode));
        Assert.Equal(creature, fogged.WithDetail(SurfaceDetail.Building).WithLighting(SurfaceLighting.CreatureMode));

        // The other rules only change the rule.
        Assert.True(fogged.WithLighting(SurfaceLighting.CellObject).Fog);
        Assert.Equal(SurfaceLighting.EnvCell, fogged.WithLighting(SurfaceLighting.EnvCell).Lighting);
        Assert.Equal(fogged, fogged.WithLighting(SurfaceLighting.Object));
    }
}
