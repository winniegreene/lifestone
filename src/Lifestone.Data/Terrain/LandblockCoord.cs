namespace Lifestone.Data.Terrain;

/// <summary>
/// A landblock coordinate. AC landblock ids are 0xXXYY____ where XX is the east-west index (west = 0) and
/// YY the south-north index (south = 0). Each landblock is 192 x 192 units (8 x 8 cells of 24 units).
/// </summary>
public readonly record struct LandblockCoord(byte X, byte Y)
{
    /// <summary>Holtburg, the first vertical slice.</summary>
    public static readonly LandblockCoord Holtburg = new(0xA9, 0xB4);

    /// <summary>Builds a coordinate from any id in the landblock (0xXXYY0000, 0xXXYYFFFF, a cell id, ...).</summary>
    public static LandblockCoord FromId(uint id) => new((byte)(id >> 24), (byte)(id >> 16));

    /// <summary>The LandBlock (heightmap) record id in client_cell_1.dat: 0xXXYYFFFF.</summary>
    public uint LandBlockId => ((uint)X << 24) | ((uint)Y << 16) | 0xFFFFu;

    /// <summary>The LandBlockInfo (statics, buildings) record id in client_cell_1.dat: 0xXXYYFFFE.</summary>
    public uint LandBlockInfoId => ((uint)X << 24) | ((uint)Y << 16) | 0xFFFEu;

    /// <summary>Global index of this landblock's south-west cell along X (east).</summary>
    public int GlobalCellX => X * TerrainConstants.CellsPerSide;

    /// <summary>Global index of this landblock's south-west cell along Y (north).</summary>
    public int GlobalCellY => Y * TerrainConstants.CellsPerSide;

    /// <summary>Returns the neighbouring landblock offset by (dx, dy), or null when it falls off the map.</summary>
    public LandblockCoord? Offset(int dx, int dy)
    {
        var x = X + dx;
        var y = Y + dy;
        return x is < 0 or > 0xFF || y is < 0 or > 0xFF ? null : new LandblockCoord((byte)x, (byte)y);
    }

    public override string ToString() => $"0x{X:X2}{Y:X2}";
}
