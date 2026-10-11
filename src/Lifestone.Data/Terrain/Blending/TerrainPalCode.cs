namespace Lifestone.Data.Terrain.Blending;

/// <summary>Cell corners in TexMerge order (bit value in coverage codes: SW = 1, SE = 2, NE = 4, NW = 8).</summary>
public enum TerrainCorner
{
    /// <summary>(x, y): coverage bit 1.</summary>
    SouthWest = 0,

    /// <summary>(x + 1, y): coverage bit 2.</summary>
    SouthEast = 1,

    /// <summary>(x + 1, y + 1): coverage bit 4.</summary>
    NorthEast = 2,

    /// <summary>(x, y + 1): coverage bit 8.</summary>
    NorthWest = 3,
}

/// <summary>
/// The per-cell "palette code" that keys retail's merged landscape textures.
/// </summary>
/// <remarks>
/// <para>
/// Retail <c>CLandBlockStruct::GetCellRotation</c> (CLandBlockStruct.c, acclient.c:354223) reads the four corner terrain
/// words of cell (ix, iy) — SW (ix, iy), SE (ix+1, iy), NE (ix+1, iy+1), NW (ix, iy+1), word index x*9+y — and builds
/// four rotated codes. For a TexMerge region <c>LandSurf::SelectTerrain</c> (CLandSurf.c, acclient.c:304328) uses only
/// <c>pal_code[0]</c> with rotation 0 (the rotated codes serve PalShift regions):
/// </para>
/// <code>
/// pal = size&lt;&lt;28 | rSW&lt;&lt;26 | rSE&lt;&lt;24 | rNE&lt;&lt;22 | rNW&lt;&lt;20 | tSW&lt;&lt;15 | tSE&lt;&lt;10 | tNE&lt;&lt;5 | tNW
/// t = (word &gt;&gt; 2) &amp; 0x1F,  r = word &amp; 3
/// </code>
/// <para>
/// <c>size</c> is 1 for full-detail blocks (side_cell_count 8) and 4 for reduced-detail blocks of non-PalShift regions.
/// Lifestone renders every block at full detail, so <see cref="FullDetailSize"/> is the default. The size bits feed the
/// alpha-map pick (<see cref="TerrainTexMerge.PseudoRandomIndex"/>), so they matter. Same layout in ACE/ACViewer
/// <c>LandblockStruct.GetPalCode</c>.
/// </para>
/// </remarks>
public static class TerrainPalCode
{
    /// <summary><c>tex_size</c> of a full-detail block in <c>GetCellRotation</c>.</summary>
    public const uint FullDetailSize = 1;

    /// <summary>Composes a palette code from corner road bits (0-3) and terrain types (0-31), corners SW, SE, NE, NW.</summary>
    public static uint Compose(
        int roadSW, int roadSE, int roadNE, int roadNW,
        int typeSW, int typeSE, int typeNE, int typeNW,
        uint size = FullDetailSize)
    {
        unchecked
        {
            return (size << 28)
                | ((uint)(roadSW & 3) << 26) | ((uint)(roadSE & 3) << 24) | ((uint)(roadNE & 3) << 22) | ((uint)(roadNW & 3) << 20)
                | ((uint)(typeSW & 0x1F) << 15) | ((uint)(typeSE & 0x1F) << 10) | ((uint)(typeNE & 0x1F) << 5) | (uint)(typeNW & 0x1F);
        }
    }

    /// <summary>Palette code of cell (<paramref name="cellX"/>, <paramref name="cellY"/>), 0-7 each, of a landblock.</summary>
    public static uint ForCell(LandblockTerrain terrain, int cellX, int cellY, uint size = FullDetailSize)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if ((uint)cellX >= TerrainConstants.CellsPerSide || (uint)cellY >= TerrainConstants.CellsPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(cellX), $"Cell ({cellX}, {cellY}) is outside 0-7.");
        }

        var words = terrain.TerrainWords;
        var sw = words[TerrainConstants.VertexIndex(cellX, cellY)];
        var se = words[TerrainConstants.VertexIndex(cellX + 1, cellY)];
        var ne = words[TerrainConstants.VertexIndex(cellX + 1, cellY + 1)];
        var nw = words[TerrainConstants.VertexIndex(cellX, cellY + 1)];
        return Compose(sw & 3, se & 3, ne & 3, nw & 3, (sw >> 2) & 0x1F, (se >> 2) & 0x1F, (ne >> 2) & 0x1F, (nw >> 2) & 0x1F, size);
    }

    /// <summary>Terrain type (0-31) at a corner of a palette code.</summary>
    public static int TerrainType(uint palCode, TerrainCorner corner) => corner switch
    {
        TerrainCorner.SouthWest => (int)((palCode >> 15) & 0x1F),
        TerrainCorner.SouthEast => (int)((palCode >> 10) & 0x1F),
        TerrainCorner.NorthEast => (int)((palCode >> 5) & 0x1F),
        TerrainCorner.NorthWest => (int)(palCode & 0x1F),
        _ => throw new ArgumentOutOfRangeException(nameof(corner)),
    };

    /// <summary>Road bits (0-3) at a corner of a palette code.</summary>
    public static int Road(uint palCode, TerrainCorner corner) => corner switch
    {
        TerrainCorner.SouthWest => (int)((palCode >> 26) & 3),
        TerrainCorner.SouthEast => (int)((palCode >> 24) & 3),
        TerrainCorner.NorthEast => (int)((palCode >> 22) & 3),
        TerrainCorner.NorthWest => (int)((palCode >> 20) & 3),
        _ => throw new ArgumentOutOfRangeException(nameof(corner)),
    };

    /// <summary>The <c>tex_size</c> field (bits 28-31).</summary>
    public static uint Size(uint palCode) => palCode >> 28;

    /// <summary>
    /// Retail <c>bSingleTextureCell</c> (<c>CLandBlockStruct::GetCellRotation</c>): all four road values equal and,
    /// when they are zero, all four terrain types equal.
    /// </summary>
    public static bool IsSingleTexture(uint palCode)
    {
        var r = Road(palCode, TerrainCorner.SouthWest);
        var sameRoad = r == Road(palCode, TerrainCorner.SouthEast)
            && r == Road(palCode, TerrainCorner.NorthEast)
            && r == Road(palCode, TerrainCorner.NorthWest);
        if (r != 0)
        {
            return sameRoad;
        }

        var t = TerrainType(palCode, TerrainCorner.SouthWest);
        return sameRoad
            && t == TerrainType(palCode, TerrainCorner.SouthEast)
            && t == TerrainType(palCode, TerrainCorner.NorthEast)
            && t == TerrainType(palCode, TerrainCorner.NorthWest);
    }
}
