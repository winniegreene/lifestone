using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Tests.Textures;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// The dat-backed appearance inputs: <see cref="GfxObjMesh.SurfaceIds"/>, <see cref="TextureLoader.GetOriginalPaletteId"/>
/// and <see cref="DatAppearanceSource"/>.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class AppearanceSourceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void A_mesh_keeps_its_GfxObjs_surface_list_in_index_order()
    {
        var ids = Dats.GetAllPortalIds<GfxObj>().Where((_, i) => i % 97 == 0).ToArray();
        Assert.True(ids.Length > 100);
        foreach (var id in ids)
        {
            Assert.True(Dats.TryGetPortal<GfxObj>(id, out var gfxObj));
            var mesh = GfxObjMeshBuilder.Build(id, gfxObj);
            Assert.Equal(gfxObj.Surfaces.Select(s => s.DataId), mesh.SurfaceIds);
            foreach (var range in mesh.SubMeshInfo.SelectMany(i => i.Ranges))
            {
                Assert.InRange(range.SurfaceIndex, 0, mesh.SurfaceIds.Count - 1);
            }
        }
    }

    [RequiresDatsFact]
    public void A_surfaces_original_palette_is_its_indexed_textures_default_palette()
    {
        var loader = new TextureLoader(Dats);
        int indexed = 0, other = 0;
        foreach (var id in Dats.GetAllPortalIds<Surface>().Where((_, i) => i % 7 == 0))
        {
            var material = loader.GetSurface(id)!;
            var palette = loader.GetOriginalPaletteId(id, TextureDetail.Low);
            if (!material.IsTextured)
            {
                Assert.Equal(0u, palette);
                other++;
                continue;
            }

            var resolution = loader.ResolveSurfaceTexture(material.SurfaceTextureId, TextureDetail.Low)!;
            var rs = loader.ReadRenderSurface(resolution.RenderSurfaceId, out _)!;
            var isIndexed = rs.Format is PixelFormat.PFID_INDEX16 or PixelFormat.PFID_P8;
            Assert.Equal(isIndexed ? rs.DefaultPaletteId : material.OriginalPaletteId, palette);
            Assert.Equal(palette, loader.GetOriginalPaletteId(id, TextureDetail.Low)); // cached
            indexed += isIndexed ? 1 : 0;
            other += isIndexed ? 0 : 1;
        }

        Assert.True(indexed > 100 && other > 100, $"{indexed} indexed, {other} other");
        Assert.Equal(0u, loader.GetOriginalPaletteId(0x0800FFFF));
    }

    [RequiresDatsFact]
    public void The_source_reads_the_same_surfaces_with_or_without_a_mesh_cache()
    {
        var loader = new TextureLoader(Dats);
        var plain = new DatAppearanceSource(loader);
        var cached = new DatAppearanceSource(loader, TextureDetail.High, new GfxObjMeshCache(Dats));
        foreach (var id in new uint[] { 0x0100004E, 0x01001787, 0x010001EC, 0x0100FFFF })
        {
            Assert.Equal(plain.SurfaceIds(id), cached.SurfaceIds(id));
            Assert.Same(plain.SurfaceIds(id), plain.SurfaceIds(id));
        }

        Assert.Empty(plain.SurfaceIds(0x0100FFFF));
        Assert.Same(plain.GetChain(0x0100004E), plain.GetChain(0x0100004E));
        Assert.Equal(0x01001787u, plain.GetChain(0x0100004E).NearestGfxObjId); // the human head's level 0 (LodChain)
        Assert.NotNull(plain.GetPalSet(0x0F000013));
        Assert.Null(plain.GetClothingTable(0));
        Assert.Equal(TextureTestSupport.ReadUncached<Palette>(Dats, 0x0400007E)!.Colors.Count, plain.GetPalette(0x0400007E)!.Count);
    }
}
