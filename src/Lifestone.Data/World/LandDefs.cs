using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World;

/// <summary>
/// Retail <c>LandDefs</c>: the cell-id predicates, the <c>lcoord</c> cell coordinates and the move of a position whose
/// origin left its landblock to the outdoor cell that contains it. Engine-free, so physics, motion and the session can
/// use it; <c>Lifestone.Client.World.AcCoordinates</c> delegates its own members here.
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail client (Mag-nus/acclient-AI-RE 11.4186, function names cited; nothing copied):</para>
/// <list type="bullet">
/// <item><c>LandDefs::gid_to_lcoord</c> (acclient.c:209521): an outdoor cell id <c>0xXXYY00nn</c> (nn = 1..0x40) is
/// cell <c>(XX * 8 + (nn - 1) / 8, YY * 8 + (nn - 1) % 8)</c>; valid while both are in [0, 2040).</item>
/// <item><c>LandDefs::lcoord_to_gid</c> (acclient.c:218182): the inverse,
/// <c>((y &gt;&gt; 3) | 32 * (x &amp; ~7)) &lt;&lt; 16 | ((y &amp; 7) + 8 * (x &amp; 7) + 1)</c>.</item>
/// <item><c>LandDefs::blockid_to_lcoord</c> (acclient.c:122260) and <c>LandDefs::in_bounds</c> (acclient.c:122254):
/// a landblock's south-west lcoord is <c>(XX * 8, YY * 8)</c>, and lcoords are valid below 2040 = 255 x 8, so landblock
/// 0xFF is off the map.</item>
/// <item><c>LandDefs::get_outside_lcoord</c> (acclient.c:467411) and <c>LandDefs::adjust_to_outside</c>
/// (acclient.c:467434): the landblock's lcoord plus <c>floor(local / 24.0)</c>; the local origin is then wrapped back
/// into the new landblock with <c>local - floor(local / 192) * 192</c>, after snapping |x|, |y| below 0.0002 to 0. The
/// wrap constant <c>flt_8EDE44</c> is initialised to <c>24.0 * 8.0</c> at acclient.c:800518.</item>
/// </list>
/// <para>Moved here in M2 from <c>AcCoordinates</c> (M1) unchanged: the same arithmetic, in the same precision.</para>
/// </remarks>
public static class LandDefs
{
    /// <summary>Edge of a landblock in metres (<c>LandDefs::block_length</c>; ACE <c>LandDefs.BlockLength</c> = 192).</summary>
    public const double BlockLength = TerrainConstants.LandblockSize;

    /// <summary>Edge of a landscape cell in metres (<c>LandDefs::square_length</c> = 24).</summary>
    public const double CellLength = TerrainConstants.CellSize;

    /// <summary>Cells per landblock side.</summary>
    public const int CellsPerBlock = TerrainConstants.CellsPerSide;

    /// <summary>Landblocks per side: lcoords are valid below 2040 = 255 x 8 (<c>LandDefs::in_bounds</c>).</summary>
    public const int BlocksPerSide = 255;

    /// <summary>Cells per side of the whole map (<c>LandDefs::in_bounds</c>: x, y &lt; 2040).</summary>
    public const int LandCoordsPerSide = BlocksPerSide * CellsPerBlock;

    /// <summary>|x| and |y| below this snap to 0 in <c>LandDefs::adjust_to_outside</c> (float 0.0002).</summary>
    public const float AdjustToOutsideEpsilon = 0.0002f;

    /// <summary>True for an outdoor (land) cell id: low word 0x0001-0x0040.</summary>
    public static bool IsOutdoorCell(uint cellId) => (cellId & 0xFFFFu) is >= 1 and <= 0x40;

    /// <summary>True for an EnvCell id: low word 0x0100-0xFFFD.</summary>
    public static bool IsEnvCell(uint cellId) => (cellId & 0xFFFFu) is >= 0x100 and <= 0xFFFD;

    /// <summary>
    /// Retail's "valid cell id" test used by <c>get_outside_lcoord</c>: an outdoor cell, an EnvCell or the landblock
    /// id itself (low word 0xFFFF).
    /// </summary>
    public static bool IsValidCellId(uint cellId) => IsOutdoorCell(cellId) || IsEnvCell(cellId) || (cellId & 0xFFFFu) == 0xFFFF;

    /// <summary>True when the landblock is on the map (both indices below 255).</summary>
    public static bool IsOnMap(LandblockCoord landblock) => landblock.X < BlocksPerSide && landblock.Y < BlocksPerSide;

    /// <summary>
    /// Retail <c>LandDefs::adjust_to_outside</c>: moves a position whose local origin left its landblock to the outdoor
    /// cell that contains it, wrapping the origin into the new landblock. Returns false (and sets the cell id to 0, as
    /// retail does) for an invalid cell id or a position off the map.
    /// </summary>
    public static bool AdjustToOutside(ref uint cellId, ref Vector3 local)
    {
        if (!IsValidCellId(cellId))
        {
            cellId = 0;
            return false;
        }

        if (MathF.Abs(local.X) < AdjustToOutsideEpsilon)
        {
            local.X = 0f;
        }

        if (MathF.Abs(local.Y) < AdjustToOutsideEpsilon)
        {
            local.Y = 0f;
        }

        if (!TryGetOutsideLandCoord(cellId, local, out var x, out var y))
        {
            cellId = 0;
            return false;
        }

        cellId = LandCoordToCellId(x, y);
        local.X = (float)(local.X - (Math.Floor(local.X / BlockLength) * BlockLength));
        local.Y = (float)(local.Y - (Math.Floor(local.Y / BlockLength) * BlockLength));
        return true;
    }

    /// <summary>
    /// Retail <c>LandDefs::get_outside_lcoord</c>: the landblock's lcoord plus <c>floor(local / 24.0)</c> on each axis;
    /// false for an invalid cell id or an lcoord outside [0, 2040).
    /// </summary>
    public static bool TryGetOutsideLandCoord(uint cellId, Vector3 local, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!IsValidCellId(cellId))
        {
            return false;
        }

        // blockid_to_lcoord: x = (id >> 21) & 0x7F8, y = 8 * (byte)(id >> 16).
        x = (int)((cellId >> 21) & 0x7F8) + (int)Math.Floor(local.X / CellLength);
        y = (8 * (int)((cellId >> 16) & 0xFF)) + (int)Math.Floor(local.Y / CellLength);
        return x is >= 0 and < LandCoordsPerSide && y is >= 0 and < LandCoordsPerSide;
    }

    /// <summary>Retail <c>LandDefs::gid_to_lcoord</c>: the lcoord of an outdoor cell id; false for any other id.</summary>
    public static bool TryGetLandCoord(uint cellId, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!IsOutdoorCell(cellId))
        {
            return false;
        }

        var low = (int)(cellId & 0xFFFF);
        x = (int)((cellId >> 21) & 0x7F8) + ((low - 1) >> 3);
        y = (8 * (int)((cellId >> 16) & 0xFF)) + ((low - 1) & 7);
        return x is >= 0 and < LandCoordsPerSide && y is >= 0 and < LandCoordsPerSide;
    }

    /// <summary>Retail <c>LandDefs::lcoord_to_gid</c>: the outdoor cell id of an lcoord, or 0 outside [0, 2040).</summary>
    public static uint LandCoordToCellId(int x, int y)
    {
        if (x is < 0 or >= LandCoordsPerSide || y is < 0 or >= LandCoordsPerSide)
        {
            return 0;
        }

        return ((uint)((y >> 3) | (32 * (x & ~7))) << 16) | (uint)((y & 7) + (8 * (x & 7)) + 1);
    }

    /// <summary>
    /// The offset in metres from the landblock of <paramref name="fromCellId"/> to the landblock of
    /// <paramref name="toCellId"/>: <c>((toX - fromX) * 192, (toY - fromY) * 192, 0)</c>, zero within one landblock.
    /// Adding it to an origin in the "to" landblock's frame expresses that origin in the "from" landblock's frame.
    /// </summary>
    /// <remarks>
    /// ACE's <c>LandDefs.GetBlockOffset</c> (ACEmulator/ACE@95ef426d, functional reference) computes the same difference
    /// through <c>blockid_to_lcoord</c> and fails on landblock 0xFF, which is off the map; this works on the landblock
    /// bytes directly, so it is defined for every id.
    /// </remarks>
    public static Vector3 GetBlockOffset(uint fromCellId, uint toCellId)
    {
        if ((fromCellId >> 16) == (toCellId >> 16))
        {
            return Vector3.Zero;
        }

        var from = LandblockCoord.FromId(fromCellId);
        var to = LandblockCoord.FromId(toCellId);
        return new Vector3(
            (float)((to.X - from.X) * BlockLength),
            (float)((to.Y - from.Y) * BlockLength),
            0f);
    }
}
