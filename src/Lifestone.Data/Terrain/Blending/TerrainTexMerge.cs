// Ported (API shape) from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core/Terrain/TerrainBlending.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: only the public shape of PseudoRandomIndex(pal, count)
// (with its "index >= count -> 0" guard), GetRoadCodes returning (road0, road1, allRoad) with the mask switch, and
// RotateTerrainCode (renamed RotateCode) follows OpenAC. The arithmetic is re-expressed from the retail decompile
// (Mag-nus/acclient-AI-RE, acclient 11.4186; no decompiled code copied): the alpha-map pick is the exact (x * n) >> 32
// instead of OpenAC's double literal, and an empty list throws instead of returning 0. GetTerrain, FindTerrainAlpha,
// FindRoadAlpha, ComputeCellBlend and the cache are written from the decompile and cross-checked against
// ACEmulator/ACViewer@c5c54fb ACViewer/Physics/Common/TexMerge.cs (GPL-3.0). See docs/third-party/terrain.md.

using System.Collections.Concurrent;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// Retail TexMerge cell-surface selection: palette code → <see cref="TerrainCellBlend"/>.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the retail decompile (Mag-nus/acclient-AI-RE, acclient 11.4186, PORTAL/cregion/CTexMerge.c; doc 13
/// reports no rendering-core changes in the user's 11.6096): <c>TexMerge::FillTempTexBuffer</c> (acclient.c:305909),
/// <c>GetTerrain</c> (305246), <c>GetRoadCode</c> (304565), <c>FindTerrainAlpha</c> (304756), <c>FindRoadAlpha</c>
/// (304697) and <c>GetTerrainTex</c> (304870). Cross-checked line by line against ACViewer
/// <c>ACViewer/Physics/Common/TexMerge.cs</c> @c5c54fb and OpenAC <c>src/AcDream.Core/Terrain/TerrainBlending.cs</c>
/// @1ee5a08; the test project carries an ACViewer port as an independent oracle.
/// </para>
/// <para>
/// One deliberate difference from ACViewer/ACE/OpenAC: the alpha-map pick uses retail's exact arithmetic. Retail multiplies
/// by the <b>float</b> constant 2.3283064e-10f, which is exactly 2^-32 (Hex-Rays prints float constants with 8 significant
/// digits; 1/255 appears the same way as 0.0039215689), so <c>floor(x * 2^-32 * n)</c> is the integer
/// <c>(x * n) &gt;&gt; 32</c>. The C# ports use the <b>double</b> literal 2.3283064e-10, which is about 1.6e-9 smaller and
/// rounds down at a handful of x values (see <see cref="PseudoRandomIndex"/>). The tests show that no palette code
/// in the user's world hits such a value, so both agree on the data.
/// </para>
/// <para>Thread-safe: the per-palette-code cache is concurrent.</para>
/// </remarks>
public sealed class TerrainTexMerge
{
    private readonly TerrainAtlasManifest _manifest;
    private readonly ConcurrentDictionary<uint, TerrainCellBlend> _cache = new();

    public TerrainTexMerge(TerrainAtlasManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public TerrainAtlasManifest Manifest => _manifest;

    /// <summary>Number of distinct palette codes resolved so far.</summary>
    public int CachedCount => _cache.Count;

    /// <summary>The blend recipe for a palette code (memoised; the recipe depends on nothing else).</summary>
    public TerrainCellBlend GetCellBlend(uint palCode) => _cache.GetOrAdd(palCode, ComputeCellBlend);

    /// <summary>The blend recipes of all 64 cells of a landblock, for full-detail rendering.</summary>
    public LandblockTerrainBlend BuildLandblock(LandblockTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var cells = new TerrainCellBlend[TerrainConstants.CellCount];
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                cells[LandblockTerrainBlend.CellIndex(cx, cy)] = GetCellBlend(TerrainPalCode.ForCell(terrain, cx, cy));
            }
        }

        return new LandblockTerrainBlend(terrain.Coord, cells);
    }

    /// <summary>Computes the blend recipe for a palette code (uncached).</summary>
    /// <exception cref="InvalidDataException">
    /// No terrain alpha map matches an overlay code. Retail's <c>FillTempTexBuffer</c> then fails and the cell gets no
    /// texture; this never happens with the Dereth Region (all codes are reachable by rotation), so it is an error here.
    /// </exception>
    public TerrainCellBlend ComputeCellBlend(uint palCode)
    {
        var m = _manifest;
        var isSingle = TerrainPalCode.IsSingleTexture(palCode);

        // FillTempTexBuffer: GetTerrain first (always succeeds), then the road code.
        GetTerrain(palCode, out var types, out var tcodes);
        var (rcode0, rcode1, allRoad) = GetRoadCodes(palCode);

        // FillTempTexBuffer: base = road texture when the cell is all road (and a road texture exists), else terrain_tex[0];
        // when all road nothing else is merged.
        if (allRoad)
        {
            var roadLayer = (byte)m.LayerForTerrainType(TerrainAtlasManifest.RoadTerrainType);
            return new TerrainCellBlend(
                palCode, TerrainAtlasManifest.RoadTerrainType, roadLayer, IsAllRoad: true, isSingle,
                OverlayCount: 0, default, default, default,
                RoadCount: 0, TerrainCellBlend.None, default, default);
        }

        Span<TerrainOverlay> overlays = stackalloc TerrainOverlay[3];
        var overlayCount = 0;
        for (var i = 0; i < 3; i++)
        {
            var tcode = tcodes[i];
            if (tcode == 0)
            {
                break;
            }

            if (!FindTerrainAlpha(palCode, tcode, out var alphaLayer, out var rotation))
            {
                throw new InvalidDataException(
                    $"TexMerge: no terrain alpha map matches tcode {tcode} for palette code 0x{palCode:X8} " +
                    "(retail FillTempTexBuffer fails and leaves the cell untextured).");
            }

            var type = types[i + 1];
            overlays[overlayCount++] = new TerrainOverlay(
                (byte)type, (byte)m.LayerForTerrainType(type), (byte)alphaLayer, rotation, (byte)tcode);
        }

        // Road layers use GetTerrainTex(0x20); a road code whose alpha map cannot be found is skipped (retail behaviour).
        Span<TerrainRoadOverlay> roads = stackalloc TerrainRoadOverlay[2];
        var roadCount = 0;
        Span<uint> rcodes = [rcode0, rcode1];
        for (var i = 0; i < 2; i++)
        {
            var rcode = rcodes[i];
            if (rcode == 0)
            {
                break;
            }

            if (FindRoadAlpha(palCode, rcode, out var alphaLayer, out var rotation))
            {
                roads[roadCount++] = new TerrainRoadOverlay((byte)alphaLayer, rotation, (byte)rcode);
            }
        }

        return new TerrainCellBlend(
            palCode,
            (byte)types[0],
            (byte)m.LayerForTerrainType(types[0]),
            IsAllRoad: false,
            isSingle,
            (byte)overlayCount,
            overlayCount > 0 ? overlays[0] : default,
            overlayCount > 1 ? overlays[1] : default,
            overlayCount > 2 ? overlays[2] : default,
            (byte)roadCount,
            roadCount > 0 ? (byte)m.RoadLayer : TerrainCellBlend.None,
            roadCount > 0 ? roads[0] : default,
            roadCount > 1 ? roads[1] : default);
    }

    /// <summary>
    /// Retail <c>TexMerge::GetTerrain</c>: base and overlay terrain types with their coverage codes.
    /// </summary>
    /// <param name="palCode">Palette code.</param>
    /// <param name="types">
    /// <c>terrain_tex[0..3]</c> as terrain types: [0] = base, [k+1] = overlay k. Entries past the last overlay are -1.
    /// </param>
    /// <param name="tcodes"><c>tcode[0..2]</c>: coverage code of overlay k, 0 = no more overlays.</param>
    /// <remarks>
    /// Corner order SW, SE, NE, NW (bits 1, 2, 4, 8). If any two corners share a type, the first such type (scanning
    /// i = 0..2, j = i+1..3) is the base; scanning corners k = 0..3 and skipping base corners, the first other corner
    /// starts overlay 0 with code 1&lt;&lt;k; at the next non-base corner, if it has overlay 0's type and overlay 0's code
    /// is exactly 1&lt;&lt;(k-1) the code grows by 1&lt;&lt;k, otherwise it becomes overlay 1 with code 1&lt;&lt;k; either
    /// way the scan stops. If all four corners differ, SW is the base and SE, NE, NW are overlays with codes 2, 4, 8.
    /// </remarks>
    public static void GetTerrain(uint palCode, out int[] types, out uint[] tcodes)
    {
        Span<int> corner =
        [
            (int)((palCode >> 15) & 0x1F),
            (int)((palCode >> 10) & 0x1F),
            (int)((palCode >> 5) & 0x1F),
            (int)(palCode & 0x1F),
        ];

        types = [-1, -1, -1, -1];
        tcodes = [0, 0, 0];

        var duplicate = -1;
        for (var i = 0; i < 3 && duplicate < 0; i++)
        {
            for (var j = i + 1; j < 4; j++)
            {
                if (corner[i] == corner[j])
                {
                    duplicate = i;
                    break;
                }
            }
        }

        if (duplicate < 0)
        {
            for (var k = 0; k < 4; k++)
            {
                types[k] = corner[k];
            }

            tcodes[0] = 2;
            tcodes[1] = 4;
            tcodes[2] = 8;
            return;
        }

        var primary = corner[duplicate];
        types[0] = primary;
        var secondary = -1;
        for (var k = 0; k < 4; k++)
        {
            if (corner[k] == primary)
            {
                continue;
            }

            if (tcodes[0] == 0)
            {
                tcodes[0] = 1u << k;
                secondary = corner[k];
                types[1] = secondary;
                continue;
            }

            if (corner[k] == secondary && tcodes[0] == 1u << (k - 1))
            {
                tcodes[0] += 1u << k;
            }
            else
            {
                types[2] = corner[k];
                tcodes[1] = 1u << k;
            }

            return;
        }
    }

    /// <summary>
    /// Retail <c>TexMerge::GetRoadCode</c>: road coverage codes from the corners' road bits.
    /// </summary>
    /// <returns>
    /// (rcode0, rcode1, allRoad). Mask bits SW = 1, SE = 2, NE = 4, NW = 8 (set when the corner's road bits are non-zero).
    /// 0xF → all road; 0xE → (6, 12); 0xD → (9, 12); 0xB → (9, 3); 0x7 → (3, 6); otherwise (mask, 0).
    /// </returns>
    public static (uint Road0, uint Road1, bool AllRoad) GetRoadCodes(uint palCode)
    {
        uint mask = 0;
        if ((palCode & 0x0C000000u) != 0)
        {
            mask = 1;
        }

        if ((palCode & 0x03000000u) != 0)
        {
            mask |= 2;
        }

        if ((palCode & 0x00C00000u) != 0)
        {
            mask |= 4;
        }

        if ((palCode & 0x00300000u) != 0)
        {
            mask |= 8;
        }

        return mask switch
        {
            0xF => (0, 0, true),
            0xE => (6, 12, false),
            0xD => (9, 12, false),
            0xB => (9, 3, false),
            0x7 => (3, 6, false),
            _ => (mask, 0, false),
        };
    }

    /// <summary>
    /// One rotation step of a coverage code: <c>c *= 2; if (c &gt;= 16) c -= 15</c> (corners 8 → 1 → 2 → 4 → 8,
    /// sides 9 → 3 → 6 → 12 → 9), as in <c>FindTerrainAlpha</c>/<c>FindRoadAlpha</c>.
    /// </summary>
    public static uint RotateCode(uint code)
    {
        code *= 2;
        return code >= 16 ? code - 15 : code;
    }

    /// <summary>
    /// Retail alpha-map pick: <c>floor((uint32)(1379576222 * palCode - 1372186442) * 2^-32 * count)</c>, computed exactly
    /// as <c>(x * count) &gt;&gt; 32</c>; an index &gt;= count becomes 0 (never happens with exact arithmetic).
    /// </summary>
    public static int PseudoRandomIndex(uint palCode, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Alpha map list is empty.");
        }

        var x = unchecked((1379576222u * palCode) - 1372186442u);
        var index = (int)(((ulong)x * (ulong)count) >> 32);
        return index >= count ? 0 : index;
    }

    /// <summary>
    /// Retail <c>TexMerge::FindTerrainAlpha</c>: corner codes (1, 2, 4, 8) use the corner maps, anything else the side
    /// maps; one map is picked by <see cref="PseudoRandomIndex"/> and rotated up to three times until its code matches.
    /// </summary>
    /// <returns>False when the picked map cannot be rotated onto <paramref name="tcode"/>.</returns>
    public bool FindTerrainAlpha(uint palCode, uint tcode, out int alphaLayer, out TerrainRotation rotation)
    {
        var maps = tcode is 1 or 2 or 4 or 8 ? _manifest.CornerAlphaLayers : _manifest.SideAlphaLayers;
        var map = maps[PseudoRandomIndex(palCode, maps.Count)];
        var code = map.Code;
        var steps = 0;
        while (code != tcode)
        {
            code = RotateCode(code);
            if (++steps >= 4)
            {
                alphaLayer = -1;
                rotation = TerrainRotation.Rot0;
                return false;
            }
        }

        alphaLayer = map.Layer;
        rotation = (TerrainRotation)steps;
        return true;
    }

    /// <summary>
    /// Retail <c>TexMerge::FindRoadAlpha</c>: starting at <see cref="PseudoRandomIndex"/>, try each road map in turn
    /// (<c>(i + start) % n</c>) at rotations 0-3; the first match wins.
    /// </summary>
    /// <returns>False when no road map matches (retail then skips this road layer).</returns>
    public bool FindRoadAlpha(uint palCode, uint rcode, out int alphaLayer, out TerrainRotation rotation)
    {
        var maps = _manifest.RoadAlphaLayers;
        alphaLayer = -1;
        rotation = TerrainRotation.Rot0;
        if (maps.Count == 0)
        {
            return false;
        }

        var start = PseudoRandomIndex(palCode, maps.Count);
        for (var i = 0; i < maps.Count; i++)
        {
            var map = maps[(i + start) % maps.Count];
            var code = map.Code;
            for (var steps = 0; steps < 4; steps++)
            {
                if (code == rcode)
                {
                    alphaLayer = map.Layer;
                    rotation = (TerrainRotation)steps;
                    return true;
                }

                code = RotateCode(code);
            }
        }

        return false;
    }
}
