namespace Lifestone.Data.Terrain;

/// <summary>Fixed landscape dimensions (Region LandDefs for Dereth: 24-unit squares, 8 cells per landblock side).</summary>
public static class TerrainConstants
{
    /// <summary>Cells per landblock side.</summary>
    public const int CellsPerSide = 8;

    /// <summary>Height-grid vertices per landblock side.</summary>
    public const int VerticesPerSide = CellsPerSide + 1;

    /// <summary>Height-grid vertices per landblock (9 x 9).</summary>
    public const int VertexCount = VerticesPerSide * VerticesPerSide;

    /// <summary>Cells per landblock (8 x 8).</summary>
    public const int CellCount = CellsPerSide * CellsPerSide;

    /// <summary>Triangles per landblock (two per cell).</summary>
    public const int TriangleCount = CellCount * 2;

    /// <summary>Edge length of one cell in world units (meters).</summary>
    public const float CellSize = 24f;

    /// <summary>Edge length of one landblock in world units (192).</summary>
    public const float LandblockSize = CellsPerSide * CellSize;

    /// <summary>Index into the 81-entry terrain/height arrays: x runs west to east, y south to north.</summary>
    public static int VertexIndex(int x, int y) => (x * VerticesPerSide) + y;
}
