using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>Sanity of the terrain atlas manifest built from the user's Region (expected values: docs/research/rendering.md §3).</summary>
public sealed class TerrainAtlasManifestTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private TerrainAtlasManifest Manifest => TerrainAtlasManifest.FromDatSet(fixture.Dats);

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Region_texmerge_has_the_documented_shape()
    {
        var m = Manifest;
        Assert.Equal(1024u, m.BaseTexSize);
        Assert.Equal(33, m.TerrainDescs.Count);
        Assert.All(m.TerrainDescs, d => Assert.Equal(2u, d.TexTiling));
        Assert.Equal(Enumerable.Range(0, 33).Select(i => (uint)i), m.TerrainDescs.Select(d => d.TerrainType));

        Assert.Equal(8, m.AlphaLayers.Count);
        Assert.Equal([8u, 8u, 8u, 8u], m.CornerAlphaLayers.Select(a => a.Code));
        Assert.Equal([0x05001371u, 0x0500143Eu, 0x0500143Fu, 0x05001440u], m.CornerAlphaLayers.Select(a => a.SurfaceTextureId));
        Assert.Equal([9u], m.SideAlphaLayers.Select(a => a.Code));
        Assert.Equal([0x05001441u], m.SideAlphaLayers.Select(a => a.SurfaceTextureId));
        Assert.Equal([9u, 10u, 8u], m.RoadAlphaLayers.Select(a => a.Code));
        Assert.Equal([0x0500168Eu, 0x0500168Cu, 0x0500168Du], m.RoadAlphaLayers.Select(a => a.SurfaceTextureId));
        Assert.Equal(Enumerable.Range(0, 8), m.AlphaLayers.Select(a => a.Layer));

        Assert.Equal(0x05001458u, m.TerrainLayers[m.RoadLayer].SurfaceTextureId);
        Assert.Equal(0x0500145Cu, m.TerrainLayers[m.LayerForTerrainType(0)].SurfaceTextureId);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Terrain_layers_are_unique_and_follow_GetTerrainTex()
    {
        var m = Manifest;

        // 33 descs, but types 0/24/31 share 0x0500145C and 16/22 share 0x0500146A.
        Assert.Equal(30, m.TerrainLayers.Count);
        Assert.Equal(m.TerrainLayers.Count, m.TerrainLayers.Select(l => (l.SurfaceTextureId, l.TexTiling)).Distinct().Count());
        Assert.Equal(m.LayerForTerrainType(0), m.LayerForTerrainType(24));
        Assert.Equal(m.LayerForTerrainType(0), m.LayerForTerrainType(31));
        Assert.Equal(m.LayerForTerrainType(16), m.LayerForTerrainType(22));

        for (var t = 0; t < TerrainAtlasManifest.TerrainTypeCount; t++)
        {
            var desc = m.TerrainDescs[m.DescIndexForTerrainType(t)];
            Assert.Equal((uint)t, desc.TerrainType); // every type has its own desc in Dereth
            Assert.Equal(desc.Layer, m.LayerForTerrainType(t));
            Assert.Equal(desc.SurfaceTextureId, m.TerrainLayers[m.LayerForTerrainType(t)].SurfaceTextureId);
            Assert.Contains(t, m.TerrainLayers[desc.Layer].TerrainTypes);
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_referenced_texture_exists_and_has_the_sizes_retail_merges_with()
    {
        var m = Manifest;
        var dats = fixture.Dats;

        foreach (var layer in m.TerrainLayers)
        {
            var rs = TerrainTestData.ResolveRenderSurface(dats, layer.SurfaceTextureId);
            Assert.Equal(512, rs.Width);
            Assert.Equal(512, rs.Height);

            // TileCSI tiles tex_tiling times; MergeTexture repeats every texture width. Both equal BaseTexSize here.
            Assert.Equal(m.BaseTexSize, layer.TexTiling * (uint)rs.Width);
            Assert.Equal(PixelFormat.PFID_A8R8G8B8, rs.Format);
        }

        foreach (var alpha in m.AlphaLayers)
        {
            var rs = TerrainTestData.ResolveRenderSurface(dats, alpha.SurfaceTextureId);
            Assert.Equal(512, rs.Width);
            Assert.Equal(512, rs.Height);
            Assert.Equal(PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA, rs.Format);
        }

        foreach (var detail in m.DetailTextures)
        {
            Assert.True(dats.TryGetPortal<SurfaceTexture>(detail.SurfaceTextureId, out _), $"detail 0x{detail.SurfaceTextureId:X8}");
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Alpha_maps_list_a_highres_level_first_and_retail_picks_it_when_present()
    {
        var dats = fixture.Dats;
        foreach (var alpha in Manifest.AlphaLayers)
        {
            Assert.True(dats.TryGetPortal<SurfaceTexture>(alpha.SurfaceTextureId, out var st));
            Assert.Equal(2, st.Textures.Count);
            if (dats.HighRes is not null)
            {
                // ImgTex::GetSurfaceDID uses level 0 unless Render::ShouldDropHighDetail; level 0 lives in client_highres.dat.
                Assert.True(dats.TryGetHighRes<RenderSurface>(st.Textures[0].DataId, out _));
            }
        }
    }

    [Fact]
    public void Object_and_landscape_detail_are_never_used_whatever_the_region()
    {
        var m = TerrainAtlasManifest.FromTexMerge(TerrainTestData.DerethShaped());
        Assert.Equal(4, m.DetailTextures.Count);
        foreach (var d in m.DetailTextures)
        {
            var expected = d.Kind is TerrainDetailKind.Landscape or TerrainDetailKind.Object
                ? TerrainDetailRetailUse.Never
                : TerrainDetailRetailUse.EnvironmentDetailPreference;
            Assert.Equal(expected, d.RetailUse);
            Assert.Equal(0x05300000u + (uint)d.DescIndex, d.SurfaceTextureId);
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Detail_textures_come_from_the_first_four_descs_and_only_building_and_environment_detail_are_ever_used()
    {
        var m = Manifest;
        Assert.Equal(4, m.DetailTextures.Count);
        Assert.Equal(
            [TerrainDetailKind.Landscape, TerrainDetailKind.Building, TerrainDetailKind.Environment, TerrainDetailKind.Object],
            m.DetailTextures.Select(d => d.Kind));
        Assert.Equal([0, 1, 2, 3], m.DetailTextures.Select(d => d.DescIndex));

        // LScape::SetDetailTexturing callers always pass landscape = 0 and object = 0; building and environment follow
        // the "Environment Detail Textures" preference.
        Assert.Equal(
            [
                TerrainDetailRetailUse.Never,
                TerrainDetailRetailUse.EnvironmentDetailPreference,
                TerrainDetailRetailUse.EnvironmentDetailPreference,
                TerrainDetailRetailUse.Never,
            ],
            m.DetailTextures.Select(d => d.RetailUse));
        Assert.Equal(TerrainDetailRetailUse.Never, m.DetailTextures.Single(d => d.Kind == TerrainDetailKind.Object).RetailUse);
        Assert.Equal(0x05001786u, m.DetailTextures[0].SurfaceTextureId);
        Assert.Equal(0x05001787u, m.DetailTextures[1].SurfaceTextureId);
        Assert.All(m.DetailTextures, d => Assert.Equal(4u, d.Tiling));
        Assert.False(TerrainAtlasManifest.LandscapeDetailTexturingEnabled);
    }
}
