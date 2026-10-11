namespace Lifestone.Data.Terrain.Blending;

/// <summary>The TexMerge recipes of the 64 cells of one landblock (full detail).</summary>
public sealed class LandblockTerrainBlend
{
    internal LandblockTerrainBlend(LandblockCoord coord, TerrainCellBlend[] cells)
    {
        Coord = coord;
        Cells = cells;
    }

    public LandblockCoord Coord { get; }

    /// <summary>64 cells, index <c>cellX * 8 + cellY</c> (same order as <see cref="TerrainMesh.CellSplitSWtoNE"/>).</summary>
    public IReadOnlyList<TerrainCellBlend> Cells { get; }

    /// <summary>Index of cell (cellX, cellY) in <see cref="Cells"/>.</summary>
    public static int CellIndex(int cellX, int cellY) => (cellX * TerrainConstants.CellsPerSide) + cellY;

    public TerrainCellBlend this[int cellX, int cellY] => Cells[CellIndex(cellX, cellY)];
}
