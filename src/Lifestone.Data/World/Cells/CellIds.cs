using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// Cell id arithmetic. A cell id is <c>0xXXYYCCCC</c>: the landblock in the high 16 bits and the cell in the low 16.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>0x0001-0x0040</c>: the 64 outdoor land cells, <c>index = x * 8 + y + 1</c> (not stored in the dat).</item>
/// <item><c>0x0100-0xFFFD</c>: EnvCells (interiors and dungeons), stored contiguously from <c>0x0100</c>. In the
/// end-of-retail cell dat the highest is <c>0x1174</c> (measured).</item>
/// <item><c>0xFFFE</c>: LandBlockInfo; <c>0xFFFF</c>: LandBlock (heights).</item>
/// </list>
/// Portal and visible-cell lists inside a landblock store only the low 16 bits; retail ORs them with the block mask
/// (<c>id &amp; 0xFFFF0000</c>) when unpacking (CEnvCell::UnPack, CCellPortal::UnPack, CBldPortal::UnPack).
/// </remarks>
public static class CellIds
{
    /// <summary>Low word of the first EnvCell of a landblock.</summary>
    public const ushort FirstEnvCell = 0x0100;

    /// <summary>Highest low word an EnvCell may use (0xFFFE and 0xFFFF are the LandBlockInfo and LandBlock).</summary>
    public const ushort LastEnvCell = 0xFFFD;

    /// <summary>Low word of a CellPortal's other cell when the portal leads outside (retail stores 0xFFFF and flag 4).</summary>
    public const ushort OutsideLow = 0xFFFF;

    /// <summary>Full id retail uses for "the outside" behind a portal (CCellPortal::UnPack sets other_cell_id = -1).</summary>
    public const uint Outside = 0xFFFFFFFFu;

    /// <summary>The landblock part of an id: <c>0xXXYY0000</c>.</summary>
    public static uint BlockMask(uint id) => id & 0xFFFF0000u;

    /// <summary>Joins a landblock coordinate and a low cell word into a full id.</summary>
    public static uint Make(LandblockCoord coord, ushort low) => ((uint)coord.X << 24) | ((uint)coord.Y << 16) | low;

    /// <summary>Joins the landblock of <paramref name="anyIdInBlock"/> with a low cell word.</summary>
    public static uint WithLow(uint anyIdInBlock, ushort low) => BlockMask(anyIdInBlock) | low;

    /// <summary>True for an EnvCell id (low word 0x0100-0xFFFD).</summary>
    public static bool IsEnvCell(uint id) => (id & 0xFFFFu) is >= FirstEnvCell and <= LastEnvCell;

    /// <summary>True for an outdoor land-cell id (low word 0x0001-0x0040).</summary>
    public static bool IsLandCell(uint id) => (id & 0xFFFFu) is >= 1 and <= 0x40;

    /// <summary>
    /// Outdoor land-cell id for a landblock-local position, or null when the position is outside the 192 x 192 block.
    /// Index layout per ACE <c>Physics/Common/LandDefs.cs lcoord_to_gid</c> (used by <c>AdjustToOutside</c>, retail
    /// <c>LandDefs::adjust_to_outside</c>): <c>1 + (x &amp; 7) * 8 + (y &amp; 7)</c> with x east and y north.
    /// </summary>
    public static uint? LandCellAt(LandblockCoord coord, float localX, float localY)
    {
        if (localX < 0f || localY < 0f || localX >= TerrainConstants.LandblockSize || localY >= TerrainConstants.LandblockSize)
        {
            return null;
        }

        var cx = (int)(localX / TerrainConstants.CellSize);
        var cy = (int)(localY / TerrainConstants.CellSize);
        return Make(coord, (ushort)((cx * TerrainConstants.CellsPerSide) + cy + 1));
    }
}
