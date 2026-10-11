// Ported from ACEmulator/ACViewer@c5c54fb3318cd956a330a920c105a0109aaeb3fd ACViewer/Physics/Common/TexMerge.cs (GPL-3.0)
// and ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/LandblockStruct.cs GetCellRotation/GetPalCode (AGPL-3.0)
// with changes: test oracle only; reads a DatReaderWriter TexMerge; returns TMTerrainDesc indices and terrain types instead of
// TerrainTex objects; records the pseudo-random pick and list kind of each alpha map next to ACViewer's alphaIdx; no
// texture loading; BuildTexture skips a road layer whose FindRoadAlpha search fails, as retail TexMerge::FillTempTexBuffer
// does (ACViewer instead records a road overlay with a null alpha map), and does not check for a missing road texture
// (GetTerrainTex(32) only fails for a TexMerge without TMTerrainDesc entries, which the product manifest rejects). The
// arithmetic (including the double literal 2.3283064e-10) is kept exactly as in the source.

using DatReaderWriter.Types;

namespace Lifestone.Data.Tests.Terrain.Oracle;

/// <summary>An overlay as the ACViewer oracle sees it.</summary>
internal sealed record OracleOverlay(int TerrainType, int DescIndex, uint TCode, int AlphaIdx, bool IsCorner, int Prng, int Rotation);

/// <summary>A road layer as the ACViewer oracle sees it.</summary>
internal sealed record OracleRoad(uint RCode, int AlphaIdx, int MapIndex, int Rotation);

/// <summary>ACViewer <c>TextureMergeInfo</c> reduced to comparable values.</summary>
internal sealed record OracleBlend(
    uint PalCode,
    bool AllRoad,
    int BaseTerrainType,
    int BaseDescIndex,
    IReadOnlyList<OracleOverlay> Overlays,
    IReadOnlyList<OracleRoad> Roads,
    bool Failed);

/// <summary>Independent TexMerge implementation used to check <c>Lifestone.Data.Terrain.Blending.TerrainTexMerge</c>.</summary>
internal sealed class AcViewerTexMergeOracle
{
    private readonly List<(uint TCode, uint Id)> _cornerTerrainMaps;
    private readonly List<(uint TCode, uint Id)> _sideTerrainMaps;
    private readonly List<(uint RCode, uint Id)> _roadMaps;
    private readonly List<uint> _terrainDescTypes;

    public AcViewerTexMergeOracle(TexMerge texMerge)
    {
        _cornerTerrainMaps = texMerge.CornerTerrainMaps.Select(m => (m.TCode, m.TextureId.DataId)).ToList();
        _sideTerrainMaps = texMerge.SideTerrainMaps.Select(m => (m.TCode, m.TextureId.DataId)).ToList();
        _roadMaps = texMerge.RoadMaps.Select(m => (m.RCode, m.TextureId.DataId)).ToList();
        _terrainDescTypes = texMerge.TerrainDesc.Select(d => (uint)d.TerrainType).ToList();
    }

    // ---- ACE LandblockStruct ----

    public static uint GetPalCode(int r1, int r2, int r3, int r4, int t1, int t2, int t3, int t4)
    {
        var terrainBits = t1 << 15 | t2 << 10 | t3 << 5 | t4;
        var roadBits = r1 << 26 | r2 << 24 | r3 << 22 | r4 << 20;

        // tex_size = 1 or 4, only used for palette shift (unused?)
        var sizeBits = 1 << 28;

        return (uint)(sizeBits | roadBits | terrainBits);
    }

    /// <summary>ACE <c>GetCellRotation</c>: palette code and singleTextureCell of cell (x, y) from the 81 terrain words.</summary>
    public static uint CellPalCode(IReadOnlyList<ushort> terrain, int x, int y, out bool singleTextureCell)
    {
        const int vertexDim = 9;

        // SW / SE / NE / NW
        var i = vertexDim * x + y;
        var terrain1 = terrain[i];
        var t1 = (terrain1 & 0x7F) >> 2;
        var r1 = terrain1 & 3;
        var j = vertexDim * (x + 1) + y;
        var terrain2 = terrain[j];
        var t2 = (terrain2 & 0x7F) >> 2;
        var r2 = terrain2 & 3;
        var terrain3 = terrain[j + 1];
        var t3 = (terrain3 & 0x7F) >> 2;
        var r3 = terrain3 & 3;
        var terrain4 = terrain[i + 1];
        var t4 = (terrain4 & 0x7F) >> 2;
        var r4 = terrain4 & 3;

        var singleRoadCell = r1 == r2 && r1 == r3 && r1 == r4;
        var singleTypeCell = t1 == t2 && t1 == t3 && t1 == t4;
        singleTextureCell = r1 != 0 ? singleRoadCell : singleRoadCell && singleTypeCell;

        return GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4);
    }

    // ---- ACViewer TexMerge ----

    public OracleBlend BuildTexture(uint pcode)
    {
        var terrainTex = GetTerrain(pcode, out var tcode, out var types);

        bool all_road = false;
        var rcode = GetRoadCode(pcode, ref all_road);
        var roadTex = GetTerrainTex(32);

        if (all_road)
        {
            return new OracleBlend(pcode, true, 32, roadTex, [], [], false);
        }

        var overlays = new List<OracleOverlay>();
        for (var i = 0; i < 3; i++)
        {
            if (tcode[i] == 0)
            {
                break;
            }

            var found = FindTerrainAlpha(pcode, tcode[i], out var rot, out var alphaIdx, out var isCorner, out var prng);
            if (!found)
            {
                return new OracleBlend(pcode, false, types[0], terrainTex[0], overlays, [], true);
            }

            overlays.Add(new OracleOverlay(types[i + 1], terrainTex[i + 1], tcode[i], alphaIdx, isCorner, prng, rot));
        }

        var roads = new List<OracleRoad>();
        for (var i = 0; i < 2; i++)
        {
            if (rcode[i] == 0)
            {
                break;
            }

            if (FindRoadAlpha(pcode, rcode[i], out var rot, out var alphaIdx, out var mapIndex))
            {
                roads.Add(new OracleRoad(rcode[i], alphaIdx, mapIndex, rot));
            }
        }

        return new OracleBlend(pcode, false, types[0], terrainTex[0], overlays, roads, false);
    }

    /// <summary>Returns the TMTerrainDesc index ACViewer's GetTerrainTex would pick (first match, else 0).</summary>
    public int GetTerrainTex(uint t1)
    {
        var numTextures = _terrainDescTypes.Count;
        if (numTextures == 0)
        {
            return -1;
        }

        for (var i = 0; i < numTextures; i++)
        {
            if (t1 == _terrainDescTypes[i])
            {
                return i;
            }
        }

        return 0;
    }

    public static List<uint> GetTerrainCodes(uint pcode)
    {
        var pcodes = new List<uint>
        {
            (pcode >> 15) & 0x1F,
            (pcode >> 10) & 0x1F,
            (pcode >> 5) & 0x1F,
            pcode & 0x1F,
        };

        return pcodes;
    }

    /// <summary>ACViewer GetTerrain; <paramref name="types"/> records the terrain type behind each returned TerrainTex.</summary>
    public List<int> GetTerrain(uint pcode, out List<uint> tcode, out List<int> types)
    {
        tcode = Enumerable.Repeat(0u, 3).ToList();

        var pcodes = GetTerrainCodes(pcode);

        for (var i = 0; i < 4; i++)
        {
            for (var j = i + 1; j < 4; j++)
            {
                if (pcodes[i] == pcodes[j])
                {
                    return BuildTCodes(pcodes, tcode, i, out types);
                }
            }
        }

        var terrainTex = new List<int>(4);
        types = new List<int>(4);

        for (var i = 0; i < 4; i++)
        {
            terrainTex.Add(GetTerrainTex(pcodes[i]));
            types.Add((int)pcodes[i]);
        }

        // tcodes: 2, 4, 8
        for (var i = 0; i < 3; i++)
        {
            tcode[i] = (uint)(1 << (i + 1));
        }

        return terrainTex;
    }

    public List<int> BuildTCodes(List<uint> pcodes, List<uint> tcode, int i, out List<int> types)
    {
        var terrainTex = Enumerable.Repeat(-1, 4).ToList();
        types = Enumerable.Repeat(-1, 4).ToList();

        uint t1 = pcodes[i];
        uint t2 = 0;

        terrainTex[0] = GetTerrainTex(t1);
        types[0] = (int)t1;

        for (var k = 0; k < 4; k++)
        {
            if (t1 == pcodes[k])
            {
                continue;
            }

            if (tcode[0] == 0)
            {
                tcode[0] = (uint)(1 << k);
                t2 = pcodes[k];

                terrainTex[1] = GetTerrainTex(t2);
                types[1] = (int)t2;
            }
            else
            {
                if (t2 == pcodes[k] && tcode[0] == (1 << (k - 1)))
                {
                    tcode[0] += (uint)(1 << k);
                }
                else
                {
                    terrainTex[2] = GetTerrainTex(pcodes[k]);
                    types[2] = (int)pcodes[k];
                    tcode[1] = (uint)(1 << k);
                }

                break;
            }
        }

        return terrainTex;
    }

    public static List<uint> GetRoadCode(uint pcode, ref bool all_road)
    {
        var rcode = Enumerable.Repeat(0u, 2).ToList();

        uint mask = 0;
        if ((pcode & 0xC000000) != 0)
        {
            mask = 1;
        }

        if ((pcode & 0x3000000) != 0)
        {
            mask |= 2;
        }

        if ((pcode & 0xC00000) != 0)
        {
            mask |= 4;
        }

        if ((pcode & 0x300000) != 0)
        {
            mask |= 8;
        }

        all_road = false;
        rcode[0] = 0;
        rcode[1] = 0;

        switch (mask)
        {
            case 0xF:
                all_road = true;
                break;
            case 0xE:
                rcode[0] = 6;
                rcode[1] = 12;
                break;
            case 0xD:
                rcode[0] = 9;
                rcode[1] = 12;
                break;
            case 0xB:
                rcode[0] = 9;
                rcode[1] = 3;
                break;
            case 0x7:
                rcode[0] = 3;
                rcode[1] = 6;
                break;
            case 0x0:
                break;
            default:
                rcode[0] = mask;
                break;
        }

        return rcode;
    }

    /// <summary>ACViewer's prng, kept verbatim: the double literal 2.3283064e-10 (retail uses the float 2^-32).</summary>
    public static int AcViewerPrng(uint pcode, int count) =>
        (int)Math.Floor((1379576222 * pcode - 1372186442) * 2.3283064e-10 * count);

    public bool FindTerrainAlpha(uint pcode, uint tcode, out int rot, out int alphaIdx, out bool isCorner, out int prng)
    {
        List<(uint TCode, uint Id)> terrainMaps;
        var baseIdx = 0;

        // corner tcodes - sw / se / ne / nw
        if (tcode != 1 && tcode != 2 && tcode != 4 && tcode != 8)
        {
            baseIdx = 4;
            terrainMaps = _sideTerrainMaps;
            isCorner = false;
        }
        else
        {
            terrainMaps = _cornerTerrainMaps;
            isCorner = true;
        }

        var numTerrains = terrainMaps.Count;

        prng = AcViewerPrng(pcode, numTerrains);
        if (prng >= numTerrains)
        {
            prng = 0;
        }

        var alpha = terrainMaps[prng];
        alphaIdx = baseIdx + prng;

        rot = 0;

        var i = 0;
        var alphaCode = alpha.TCode;
        while (alphaCode != tcode)
        {
            // corners: 8 -> 1 -> 2 -> 4
            // sides: 9 -> 3 -> 6 -> 12
            alphaCode *= 2;
            if (alphaCode >= 16)
            {
                alphaCode -= 15;
            }

            if (++i >= 4)
            {
                return false;
            }
        }

        rot = i;
        return true;
    }

    public bool FindRoadAlpha(uint pcode, uint rcode, out int rot, out int alphaIdx, out int mapIndex)
    {
        rot = 0;
        alphaIdx = -1;
        mapIndex = -1;

        var numRoadMaps = _roadMaps.Count;
        if (numRoadMaps == 0)
        {
            return false;
        }

        var prng = AcViewerPrng(pcode, numRoadMaps);

        for (var i = 0; i < numRoadMaps; i++)
        {
            var idx = (i + prng) % numRoadMaps;
            var alpha = _roadMaps[idx];
            rot = 0;
            var alphaCode = alpha.RCode;
            alphaIdx = 5 + idx;

            for (var j = 0; j < 4; j++)
            {
                if (alphaCode == rcode)
                {
                    rot = j;
                    mapIndex = idx;
                    return true;
                }

                alphaCode *= 2;
                if (alphaCode >= 16)
                {
                    alphaCode -= 15;
                }
            }
        }

        alphaIdx = -1;
        return false;
    }
}
