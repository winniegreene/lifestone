using System.Globalization;
using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Client.World;

/// <summary>
/// A world position in metres, measured from the south-west corner of landblock 0x0000 in AC axes (+X east, +Y north,
/// +Z up). Double precision: Dereth is 255 x 192 = 48,960 m across, where a float keeps only about 4 mm.
/// </summary>
/// <remarks>
/// This is the global frame ACE uses for distances between landblocks (<c>PositionExtensions.ToGlobal</c>:
/// <c>landblockX * 192 + localX</c>). Retail never stores it; it works in (cell id, landblock-local frame) pairs and
/// in <c>lcoord</c> cell units (<see cref="AcCoordinates"/>). Indoor positions use the same formula with the EnvCell's
/// landblock, which places dungeons at their landblock's spot on the grid.
/// </remarks>
public readonly record struct GlobalPosition(double X, double Y, double Z)
{
    public static GlobalPosition operator +(GlobalPosition a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static GlobalPosition operator -(GlobalPosition a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Straight-line distance in metres.</summary>
    public double DistanceTo(GlobalPosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>Linear interpolation, exact at t = 0 and t = 1.</summary>
    public static GlobalPosition Lerp(GlobalPosition a, GlobalPosition b, double t) => t >= 1.0
        ? b
        : new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>
/// A retail position: an object cell id (<c>0xXXYYnnnn</c>: an outdoor cell 0x0001-0x0040, an EnvCell 0x0100-0xFFFD,
/// or 0xFFFF for "the landblock") plus an origin in that landblock's local frame (metres from its south-west corner,
/// Z up). This is retail <c>Position</c> (<c>objcell_id</c> + <c>frame.m_fOrigin</c>) without the rotation.
/// </summary>
public readonly record struct CellPosition(uint CellId, Vector3 Local)
{
    /// <summary>The landblock the cell belongs to.</summary>
    public LandblockCoord Landblock => LandblockCoord.FromId(CellId);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"0x{CellId:X8} [{Local.X:0.###} {Local.Y:0.###} {Local.Z:0.###}]");
}

/// <summary>Retail map coordinates: positive north and east, in map units of 240 m (0.1 per 24 m cell).</summary>
/// <param name="NorthSouth">Latitude: positive north, negative south.</param>
/// <param name="EastWest">Longitude: positive east, negative west.</param>
public readonly record struct MapCoordinates(double NorthSouth, double EastWest)
{
    /// <summary>The retail text, e.g. <c>42.1N, 33.6E</c> (<see cref="AcCoordinates.FormatMapCoordinates"/>).</summary>
    public override string ToString() => AcCoordinates.FormatMapCoordinates(this);
}

/// <summary>
/// Conversions between retail cell positions, <c>lcoord</c> cell coordinates, global metres and map coordinates.
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail client (Mag-nus/acclient-AI-RE 11.4186, function names cited; nothing copied):</para>
/// <list type="bullet">
/// <item><c>LandDefs::gid_to_lcoord</c> (acclient.c:209521): an outdoor cell id <c>0xXXYY00nn</c> (nn = 1..0x40) is
/// cell <c>(XX * 8 + (nn - 1) / 8, YY * 8 + (nn - 1) % 8)</c>; valid while both are in [0, 2040).</item>
/// <item><c>LandDefs::lcoord_to_gid</c> (acclient.c:218182): the inverse,
/// <c>((y &gt;&gt; 3) | 32 * (x &amp; ~7)) &lt;&lt; 16 | ((y &amp; 7) + 8 * (x &amp; 7) + 1)</c>.</item>
/// <item><c>LandDefs::get_outside_lcoord</c> (acclient.c:467411) and <c>LandDefs::adjust_to_outside</c>
/// (acclient.c:467434): the landblock's lcoord plus <c>floor(local / 24.0)</c>; the local origin is then wrapped back
/// into the new landblock with <c>local - floor(local / 192) * 192</c>, after snapping |x|, |y| below 0.0002 to 0.</item>
/// <item><c>CPlayerSystem::InqPlayerCoords</c> (acclient.c:398264): map coordinates from the player's outdoor cell,
/// <c>EW = (lx - 1024) * 0.1 + 0.5</c>, <c>NS = (ly - 1024) * 0.1 + 0.5</c>. Retail therefore reports the centre of
/// the 24 m cell (0.1 map units), not the exact position: Holtburg's town cell 0xA9B40019 is 42.1N, 33.6E. Indoor
/// cells have no lcoord, so retail shows no coordinates there (<c>gid_to_lcoord</c> fails).</item>
/// <item><c>LandDefs::CellidToCoordinateString</c> (acclient.c:467472) formats <c>"%.1f%s, %.1f%s"</c> with the
/// latitude first; the letter is N/S (E/W) by sign and empty at exactly 0 (<c>gmRadarUI::UpdateCoordinates</c> uses the
/// same rule without the space).</item>
/// </list>
/// <para>ACE's <c>PositionExtensions.GetMapCoords</c> (<c>global / 240 - 102</c>) is continuous instead and differs from
/// retail by up to half a cell (it reports Holtburg as 42.0N); Lifestone follows retail.</para>
/// </remarks>
public static class AcCoordinates
{
    /// <summary>Edge of a landblock in metres (<c>LandDefs::block_length</c>; ACE <c>LandDefs.BlockLength</c> = 192).</summary>
    public const double LandblockLength = TerrainConstants.LandblockSize;

    /// <summary>Edge of a landscape cell in metres (<c>LandDefs::square_length</c> = 24).</summary>
    public const double CellLength = TerrainConstants.CellSize;

    /// <summary>Cells per landblock side.</summary>
    public const int CellsPerLandblock = TerrainConstants.CellsPerSide;

    /// <summary>Landblocks per side: lcoords are valid below 2040 = 255 x 8 (<c>LandDefs::in_bounds</c>).</summary>
    public const int LandblocksPerSide = 255;

    /// <summary>Cells per side of the whole map (<c>LandDefs::in_bounds</c>: x, y &lt; 2040).</summary>
    public const int LandCoordsPerSide = LandblocksPerSide * CellsPerLandblock;

    /// <summary>The lcoord retail subtracts before scaling to map units (<c>CPlayerSystem::InqPlayerCoords</c>).</summary>
    public const int MapOriginLandCoord = 1024;

    /// <summary>Map units per cell (<c>CPlayerSystem::InqPlayerCoords</c> multiplies by 0.1).</summary>
    public const double MapUnitsPerCell = 0.1;

    /// <summary>Retail's half-cell offset added to map coordinates (<c>+ 0.5</c> in <c>InqPlayerCoords</c>).</summary>
    public const double MapCellCentreOffset = 0.5;

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
    public static bool IsOnMap(LandblockCoord landblock) => landblock.X < LandblocksPerSide && landblock.Y < LandblocksPerSide;

    /// <summary>The global position of a landblock's south-west corner (z = 0).</summary>
    public static GlobalPosition LandblockOrigin(LandblockCoord landblock) =>
        new(landblock.X * LandblockLength, landblock.Y * LandblockLength, 0.0);

    /// <summary>
    /// Cell position to global metres: <c>landblock * 192 + local</c> for any cell kind (ACE
    /// <c>PositionExtensions.ToGlobal</c> with <c>skipIndoors = false</c>).
    /// </summary>
    public static GlobalPosition ToGlobal(CellPosition position)
    {
        var landblock = position.Landblock;
        return new GlobalPosition(
            (landblock.X * LandblockLength) + position.Local.X,
            (landblock.Y * LandblockLength) + position.Local.Y,
            position.Local.Z);
    }

    /// <summary>
    /// The landblock containing a global position: <c>floor(x / 192)</c>, <c>floor(y / 192)</c>, or null off the map.
    /// </summary>
    public static LandblockCoord? LandblockAt(GlobalPosition position)
    {
        var x = Math.Floor(position.X / LandblockLength);
        var y = Math.Floor(position.Y / LandblockLength);
        return x is >= 0 and < LandblocksPerSide && y is >= 0 and < LandblocksPerSide
            ? new LandblockCoord((byte)x, (byte)y)
            : null;
    }

    /// <summary>The landblock containing a global position, clamped to the map edge.</summary>
    public static LandblockCoord ClampedLandblockAt(GlobalPosition position)
    {
        var x = Math.Clamp(Math.Floor(position.X / LandblockLength), 0, LandblocksPerSide - 1);
        var y = Math.Clamp(Math.Floor(position.Y / LandblockLength), 0, LandblocksPerSide - 1);
        return new LandblockCoord((byte)x, (byte)y);
    }

    /// <summary>
    /// The outdoor cell containing a global position (retail <c>adjust_to_outside</c> applied to the landblock-local
    /// origin), or false off the map. The local origin is landblock-relative.
    /// </summary>
    public static bool TryGetOutdoorCell(GlobalPosition position, out CellPosition cell)
    {
        // lcoord = floor(global / 24): identical to blockid_to_lcoord + floor(local / 24.0) because block * 192 is exact.
        var lx = Math.Floor(position.X / CellLength);
        var ly = Math.Floor(position.Y / CellLength);
        if (lx is < 0 or >= LandCoordsPerSide || ly is < 0 or >= LandCoordsPerSide)
        {
            cell = default;
            return false;
        }

        var cellId = LandCoordToCellId((int)lx, (int)ly);
        var landblock = LandblockCoord.FromId(cellId);
        cell = new CellPosition(cellId, new Vector3(
            (float)(position.X - (landblock.X * LandblockLength)),
            (float)(position.Y - (landblock.Y * LandblockLength)),
            (float)position.Z));
        return true;
    }

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
        local.X = (float)(local.X - (Math.Floor(local.X / LandblockLength) * LandblockLength));
        local.Y = (float)(local.Y - (Math.Floor(local.Y / LandblockLength) * LandblockLength));
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
    /// Retail <c>CPlayerSystem::InqPlayerCoords</c>: map coordinates of an outdoor cell (the cell's centre, in 0.1 map
    /// units per cell). False for EnvCells and other ids, where retail shows no coordinates.
    /// </summary>
    public static bool TryGetMapCoordinates(uint cellId, out MapCoordinates coordinates)
    {
        if (!TryGetLandCoord(cellId, out var x, out var y))
        {
            coordinates = default;
            return false;
        }

        coordinates = new MapCoordinates(
            ((y - MapOriginLandCoord) * MapUnitsPerCell) + MapCellCentreOffset,
            ((x - MapOriginLandCoord) * MapUnitsPerCell) + MapCellCentreOffset);
        return true;
    }

    /// <summary>Map coordinates of the outdoor cell under a global position, or false off the map.</summary>
    public static bool TryGetMapCoordinates(GlobalPosition position, out MapCoordinates coordinates)
    {
        if (TryGetOutdoorCell(position, out var cell))
        {
            return TryGetMapCoordinates(cell.CellId, out coordinates);
        }

        coordinates = default;
        return false;
    }

    /// <summary>
    /// Retail <c>LandDefs::CellidToCoordinateString</c>: <c>"%.1f%s, %.1f%s"</c>, latitude first, e.g. <c>42.1N, 33.6E</c>.
    /// The letter is N/S or E/W by sign and empty when the value is exactly 0.
    /// </summary>
    public static string FormatMapCoordinates(MapCoordinates coordinates)
    {
        var ns = coordinates.NorthSouth > 0 ? "N" : coordinates.NorthSouth < 0 ? "S" : string.Empty;
        var ew = coordinates.EastWest > 0 ? "E" : coordinates.EastWest < 0 ? "W" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Abs(coordinates.NorthSouth):0.0}{ns}, {Math.Abs(coordinates.EastWest):0.0}{ew}");
    }

    /// <summary>
    /// Compass heading of a horizontal direction in degrees: 0 = north (+Y), 90 = east (+X), clockwise, in [0, 360).
    /// Retail <c>AC1Legacy::Vector3::get_heading</c> (<c>fmod(450 - atan2(y, x) * 180 / pi, 360)</c>); 0 for a vector
    /// shorter than 0.0002.
    /// </summary>
    public static double HeadingOf(double x, double y)
    {
        if (Math.Sqrt((x * x) + (y * y)) < 0.0002)
        {
            return 0.0;
        }

        var heading = (450.0 - (Math.Atan2(y, x) * (180.0 / Math.PI))) % 360.0;
        return heading < 0 ? heading + 360.0 : heading;
    }

    /// <summary>The unit horizontal direction of a compass heading (retail <c>Frame::set_heading</c>: (sin h, cos h)).</summary>
    public static Vector2 DirectionOfHeading(double headingDegrees)
    {
        var radians = headingDegrees * (Math.PI / 180.0);
        return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
    }
}
