namespace Lifestone.ServerTools;

/// <summary>
/// The cell-id rules a <c>place loc</c> request must satisfy before the mod looks it up in ACE's cell dat: the low word
/// says which kind of cell it is, an outdoor cell must be the one that holds the given x and y, and the landblock must
/// be on the map. Pure arithmetic over the id, shared by the mod and the client so both judge a request the same way.
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail client's <c>LandDefs</c> (Mag-nus/acclient-AI-RE 11.4186, names cited, nothing
/// copied; addresses as 11.4186 / 11.6096, docs/research/retail-addresses-m2.md), as <c>Lifestone.Data.World.LandDefs</c>
/// does for the client (this file may not reference it: the mod compiles only these sources):</para>
/// <list type="bullet">
/// <item><c>LandDefs::gid_to_lcoord</c> (0x00497A90 / 0x00497D70) and <c>LandDefs::lcoord_to_gid</c> (0x004A19A0 /
/// 0x004A1CD0): outdoor cell <c>n</c> of a landblock (1..0x40) is column <c>(n - 1) / 8</c> along x and row
/// <c>(n - 1) % 8</c> along y, so <c>n = floor(x / 24) * 8 + floor(y / 24) + 1</c>;</item>
/// <item><c>LandDefs::get_outside_lcoord</c> (0x005A9B00 / 0x005AABB0): the cell column of a local origin is
/// <c>floor(local / 24.0)</c>, taken in double precision (so a float just below a cell edge stays in the lower
/// cell);</item>
/// <item><c>LandDefs::in_bounds</c> (0x0043D650 / 0x0043D7F0) with <c>LandDefs::blockid_to_lcoord</c> (0x0043D680 /
/// 0x0043D820): landblock coordinates are valid below 255, so landblock row or column 0xFF is off the map.</item>
/// </list>
/// <para>EnvCells are 0x0100-0xFFFD (0xFFFE is the landblock info and 0xFFFF the landblock itself). Whether a cell
/// actually exists is the mod's question, answered from ACE's cell dat index (m2-plan SM3).</para>
/// </remarks>
public static class PlaceRules
{
    /// <summary>Edge of a landblock in metres (<c>LandDefs::block_length</c>).</summary>
    public const double BlockLength = 192.0;

    /// <summary>Edge of an outdoor cell in metres (<c>LandDefs::square_length</c>).</summary>
    public const double CellLength = 24.0;

    /// <summary>Outdoor cells per landblock side.</summary>
    public const int CellsPerSide = 8;

    /// <summary>Landblock rows and columns on the map (<c>LandDefs::in_bounds</c>: 0 to 254).</summary>
    public const int BlocksPerSide = 255;

    /// <summary>The highest EnvCell index (0xFFFE is the landblock info, 0xFFFF the landblock).</summary>
    public const uint LastEnvCellIndex = 0xFFFD;

    /// <summary>True for an outdoor (land) cell id: low word 0x0001-0x0040.</summary>
    public static bool IsOutdoorCell(uint cellId) => (cellId & 0xFFFFu) is >= 1 and <= 0x40;

    /// <summary>True for an EnvCell (indoor or dungeon) id: low word 0x0100-0xFFFD.</summary>
    public static bool IsEnvCell(uint cellId) => (cellId & 0xFFFFu) is >= 0x100 and <= LastEnvCellIndex;

    /// <summary>True when the cell's landblock row and column are both on the map (below 0xFF).</summary>
    public static bool IsOnMap(uint cellId) => (cellId >> 24) < BlocksPerSide && ((cellId >> 16) & 0xFFu) < BlocksPerSide;

    /// <summary>The landblock id of a cell (<c>0xXXYYFFFF</c>), as ACE's <c>LandblockId.Raw</c> spells it.</summary>
    public static uint LandblockOf(uint cellId) => cellId | 0xFFFFu;

    /// <summary>
    /// The outdoor cell of <paramref name="landblockId"/> (only its high word is used) that holds the landblock-frame
    /// point (<paramref name="x"/>, <paramref name="y"/>): index <c>floor(x / 24) * 8 + floor(y / 24) + 1</c>. False,
    /// with <paramref name="cellId"/> 0, when x or y is outside [0, 192) or not finite, or the landblock is off the map.
    /// </summary>
    public static bool TryOutdoorCell(uint landblockId, float x, float y, out uint cellId)
    {
        cellId = 0;
        if (!IsOnMap(landblockId) || !InBlock(x) || !InBlock(y))
        {
            return false;
        }

        var column = (uint)Math.Floor(x / CellLength);
        var row = (uint)Math.Floor(y / CellLength);
        cellId = (landblockId & 0xFFFF0000u) | ((column * CellsPerSide) + row + 1);
        return true;
    }

    /// <summary>
    /// Checks a <c>place loc</c> destination: finite numbers, a landblock on the map, an outdoor cell that holds (x, y)
    /// or an EnvCell, and a rotation whose length is within 1 ± <see cref="ToolRequestParser.QuaternionTolerance"/>.
    /// On failure <paramref name="problem"/> says what is wrong in one sentence.
    /// </summary>
    public static bool TryValidate(ToolLocation at, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? problem)
    {
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z))
        {
            problem = "the position must be finite numbers";
            return false;
        }

        if (!float.IsFinite(at.Qw) || !float.IsFinite(at.Qx) || !float.IsFinite(at.Qy) || !float.IsFinite(at.Qz))
        {
            problem = "the rotation must be finite numbers";
            return false;
        }

        var length = Math.Sqrt(((double)at.Qw * at.Qw) + ((double)at.Qx * at.Qx) + ((double)at.Qy * at.Qy) + ((double)at.Qz * at.Qz));
        if (Math.Abs(length - 1.0) > ToolRequestParser.QuaternionTolerance)
        {
            problem = "the rotation must be a unit quaternion (qw qx qy qz, length 1)";
            return false;
        }

        if (!IsOnMap(at.Cell))
        {
            problem = "cell " + ToolsWire.FormatCell(at.Cell) + " is in a landblock off the map (row or column 0xFF)";
            return false;
        }

        if (IsOutdoorCell(at.Cell))
        {
            if (!InBlock(at.X) || !InBlock(at.Y))
            {
                problem = "an outdoor position needs x and y from 0 up to (not including) 192";
                return false;
            }

            TryOutdoorCell(at.Cell, at.X, at.Y, out var holder);
            if (holder != at.Cell)
            {
                problem = "cell " + ToolsWire.FormatCell(at.Cell) + " does not hold that x and y; " + ToolsWire.FormatCell(holder) + " does";
                return false;
            }

            problem = null;
            return true;
        }

        if (IsEnvCell(at.Cell))
        {
            problem = null;
            return true;
        }

        problem = "cell " + ToolsWire.FormatCell(at.Cell) + " is neither an outdoor cell (index 0x0001-0x0040) nor an EnvCell (0x0100-0xFFFD)";
        return false;
    }

    private static bool InBlock(float v) => float.IsFinite(v) && v >= 0 && v < BlockLength;
}
