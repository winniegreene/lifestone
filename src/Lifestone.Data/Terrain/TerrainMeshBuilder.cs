using System.Numerics;

namespace Lifestone.Data.Terrain;

/// <summary>
/// Indexed triangle mesh for one landblock, in AC landblock-local coordinates:
/// right-handed, Z up, X east, Y north, origin at the landblock's south-west corner.
/// </summary>
public sealed class TerrainMesh
{
    internal TerrainMesh(
        LandblockCoord coord,
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        byte[] terrainTypes,
        byte[] roads,
        bool[] cellSplitSWtoNE)
    {
        Coord = coord;
        Positions = positions;
        Normals = normals;
        Indices = indices;
        TerrainTypes = terrainTypes;
        Roads = roads;
        CellSplitSWtoNE = cellSplitSWtoNE;
    }

    public LandblockCoord Coord { get; }

    /// <summary>81 vertex positions, index <c>x * 9 + y</c>; position = (x * 24, y * 24, LandHeightTable[height]).</summary>
    public Vector3[] Positions { get; }

    /// <summary>
    /// Per-vertex normals: normalised sum of the face normals of this landblock's triangles that touch the vertex
    /// (retail <c>CLandBlockStruct::calc_lighting</c>; neighbouring landblocks are deliberately not included).
    /// </summary>
    public Vector3[] Normals { get; }

    /// <summary>
    /// 384 indices = 128 triangles, two per cell, cells ordered <c>cellX * 8 + cellY</c>.
    /// AC winding: counter-clockwise seen from above (+Z), i.e. front faces follow the right-hand rule.
    /// </summary>
    public int[] Indices { get; }

    /// <summary>Terrain type (0-31) per vertex.</summary>
    public byte[] TerrainTypes { get; }

    /// <summary>Road bits (0-3) per vertex; non-zero means road.</summary>
    public byte[] Roads { get; }

    /// <summary>Diagonal direction per cell (index <c>cellX * 8 + cellY</c>); see <see cref="TerrainSplit"/>.</summary>
    public bool[] CellSplitSWtoNE { get; }

    public int VertexCount => Positions.Length;

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Builds per-landblock terrain meshes from LandBlock height grids.</summary>
public static class TerrainMeshBuilder
{
    /// <summary>Normals shorter than this before normalisation fall back to straight up (retail behaviour).</summary>
    private const float MinNormalLength = 2e-4f;

    public static TerrainMesh Build(LandblockTerrain terrain, TerrainRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Build(terrain, region.LandHeightTable, region.SquareLength);
    }

    public static TerrainMesh Build(LandblockTerrain terrain, IReadOnlyList<float> landHeightTable, float cellSize = TerrainConstants.CellSize)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(landHeightTable);
        if (landHeightTable.Count != 256)
        {
            throw new ArgumentException("LandHeightTable must have 256 entries.", nameof(landHeightTable));
        }

        const int side = TerrainConstants.VerticesPerSide;
        var positions = new Vector3[TerrainConstants.VertexCount];
        var terrainTypes = new byte[TerrainConstants.VertexCount];
        var roads = new byte[TerrainConstants.VertexCount];

        for (var x = 0; x < side; x++)
        {
            for (var y = 0; y < side; y++)
            {
                var i = TerrainConstants.VertexIndex(x, y);
                positions[i] = new Vector3(x * cellSize, y * cellSize, landHeightTable[terrain.HeightIndex(x, y)]);
                terrainTypes[i] = (byte)terrain.TerrainType(x, y);
                roads[i] = (byte)terrain.Road(x, y);
            }
        }

        var indices = new int[TerrainConstants.TriangleCount * 3];
        var splits = new bool[TerrainConstants.CellCount];
        var n = 0;
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var sw = TerrainConstants.VertexIndex(cx, cy);
                var se = TerrainConstants.VertexIndex(cx + 1, cy);
                var ne = TerrainConstants.VertexIndex(cx + 1, cy + 1);
                var nw = TerrainConstants.VertexIndex(cx, cy + 1);

                var swToNe = TerrainSplit.IsSWtoNE(terrain.Coord, cx, cy);
                splits[(cx * TerrainConstants.CellsPerSide) + cy] = swToNe;

                if (swToNe)
                {
                    // Diagonal SW-NE: (SW, SE, NE) and (SW, NE, NW).
                    n = Emit(indices, n, sw, se, ne);
                    n = Emit(indices, n, sw, ne, nw);
                }
                else
                {
                    // Diagonal SE-NW: (SW, SE, NW) and (NE, NW, SE).
                    n = Emit(indices, n, sw, se, nw);
                    n = Emit(indices, n, ne, nw, se);
                }
            }
        }

        var normals = ComputeVertexNormals(positions, indices);
        return new TerrainMesh(terrain.Coord, positions, normals, indices, terrainTypes, roads, splits);
    }

    private static int Emit(int[] indices, int n, int a, int b, int c)
    {
        indices[n] = a;
        indices[n + 1] = b;
        indices[n + 2] = c;
        return n + 3;
    }

    private static Vector3[] ComputeVertexNormals(Vector3[] positions, int[] indices)
    {
        var sums = new Vector3[positions.Length];
        for (var t = 0; t < indices.Length; t += 3)
        {
            var a = indices[t];
            var b = indices[t + 1];
            var c = indices[t + 2];
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            var length = face.Length();
            if (length <= 0f)
            {
                continue;
            }

            face /= length;
            sums[a] += face;
            sums[b] += face;
            sums[c] += face;
        }

        var normals = new Vector3[positions.Length];
        for (var i = 0; i < sums.Length; i++)
        {
            var length = sums[i].Length();
            normals[i] = length < MinNormalLength ? Vector3.UnitZ : sums[i] / length;
        }

        return normals;
    }
}
