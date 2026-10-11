using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Data.Tests.Terrain;

/// <summary>Synthetic TexMerge records (shaped like Dereth's) and real-dat helpers for the terrain blending tests.</summary>
internal static class TerrainTestData
{
    /// <summary>
    /// A TexMerge with Dereth's structure: 4 corner maps (TCode 8), 1 side map (TCode 9), road maps with RCodes 9, 10, 8,
    /// and 33 TMTerrainDesc entries (types 0-32) where types 24 and 31 reuse type 0's texture. Ids are synthetic.
    /// </summary>
    public static TexMerge DerethShaped(uint baseTexSize = 1024, uint tiling = 2) => Build(
        baseTexSize,
        tiling,
        corner: [8, 8, 8, 8],
        side: [9],
        road: [9, 10, 8]);

    public static TexMerge Build(uint baseTexSize, uint tiling, uint[] corner, uint[] side, uint[] road, int descCount = 33)
    {
        var texMerge = new TexMerge { BaseTexSize = baseTexSize };
        for (var i = 0; i < corner.Length; i++)
        {
            texMerge.CornerTerrainMaps.Add(new TerrainAlphaMap { TCode = corner[i], TextureId = 0x05100000u + (uint)i });
        }

        for (var i = 0; i < side.Length; i++)
        {
            texMerge.SideTerrainMaps.Add(new TerrainAlphaMap { TCode = side[i], TextureId = 0x05110000u + (uint)i });
        }

        for (var i = 0; i < road.Length; i++)
        {
            texMerge.RoadMaps.Add(new RoadAlphaMap { RCode = road[i], TextureId = 0x05120000u + (uint)i });
        }

        for (var t = 0; t < descCount; t++)
        {
            var texId = t is 24 or 31 ? 0x05200000u : 0x05200000u + (uint)t;
            texMerge.TerrainDesc.Add(new TMTerrainDesc
            {
                TerrainType = (TerrainTextureType)t,
                TerrainTex = new TerrainTex
                {
                    TextureId = texId,
                    TexTiling = tiling,
                    DetailTexTiling = 4,
                    DetailTextureId = 0x05300000u + (uint)t,
                },
            });
        }

        return texMerge;
    }

    /// <summary>The real Region's TexMerge.</summary>
    public static TexMerge RegionTexMerge(DatSet dats) => dats.Region.TerrainInfo.LandSurfaces.TexMerge;

    /// <summary>All LandBlocks present in client_cell_1.dat, in id order.</summary>
    public static IEnumerable<LandblockTerrain> AllLandblocks(DatSet dats)
    {
        for (var x = 0; x <= 0xFF; x++)
        {
            for (var y = 0; y <= 0xFF; y++)
            {
                var terrain = dats.GetLandblockTerrain(new LandblockCoord((byte)x, (byte)y));
                if (terrain is not null)
                {
                    yield return terrain;
                }
            }
        }
    }

    /// <summary>Landblocks in a square of radius <paramref name="radius"/> around <paramref name="center"/>.</summary>
    public static IEnumerable<LandblockTerrain> LandblocksAround(DatSet dats, LandblockCoord center, int radius)
    {
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var c = center.Offset(dx, dy);
                if (c is not null && dats.GetLandblockTerrain(c.Value) is { } terrain)
                {
                    yield return terrain;
                }
            }
        }
    }

    /// <summary>
    /// Test-only raw decode of a SurfaceTexture as retail picks it (<c>ImgTex::GetSurfaceDID</c>: level [0], i.e. the
    /// high-res one when client_highres.dat has it). Handles only the two formats terrain uses: A8R8G8B8 (BGRA bytes) and
    /// CUSTOM_LSCAPE_ALPHA (one byte per texel). The product decoder belongs to the textures track.
    /// </summary>
    public static RenderSurface ResolveRenderSurface(DatSet dats, uint surfaceTextureId)
    {
        Assert.True(dats.TryGetPortal<SurfaceTexture>(surfaceTextureId, out var st), $"SurfaceTexture 0x{surfaceTextureId:X8} missing");
        foreach (var level in st.Textures)
        {
            if (dats.TryGetHighResOrPortal<RenderSurface>(level.DataId, out var rs))
            {
                return rs;
            }
        }

        throw new InvalidDataException($"No RenderSurface of SurfaceTexture 0x{surfaceTextureId:X8} is present.");
    }

    public static TerrainImage DecodeColor(DatSet dats, uint surfaceTextureId)
    {
        var rs = ResolveRenderSurface(dats, surfaceTextureId);
        Assert.Equal(PixelFormat.PFID_A8R8G8B8, rs.Format);
        var pixels = new TerrainRgba[rs.Width * rs.Height];
        var data = rs.SourceData;
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new TerrainRgba(data[(i * 4) + 2], data[(i * 4) + 1], data[i * 4], data[(i * 4) + 3]);
        }

        return new TerrainImage(rs.Width, rs.Height, pixels);
    }

    public static TerrainAlphaImage DecodeAlpha(DatSet dats, uint surfaceTextureId)
    {
        var rs = ResolveRenderSurface(dats, surfaceTextureId);
        Assert.Equal(PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA, rs.Format);
        Assert.Equal(rs.Width * rs.Height, rs.SourceData.Length);
        return new TerrainAlphaImage(rs.Width, rs.Height, (byte[])rs.SourceData.Clone());
    }
}
