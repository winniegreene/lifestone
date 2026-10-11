using System.Numerics;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// Non-indexed per-vertex data for one landblock's terrain shader: 128 triangles × 3 = 384 vertices, in the triangle
/// order and AC winding of <see cref="TerrainMesh.Indices"/> (counter-clockwise from above; the Godot layer reverses it).
/// Every vertex of a cell carries the cell's packed <see cref="TerrainCellBlend"/>, so the data is flat per cell.
/// </summary>
public sealed class TerrainBlendVertices
{
    private TerrainBlendVertices(LandblockCoord coord, Vector3[] positions, Vector3[] normals, Vector2[] uvs, byte[] custom, int[] sourceVertex)
    {
        Coord = coord;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Custom = custom;
        SourceVertex = sourceVertex;
    }

    public LandblockCoord Coord { get; }

    /// <summary>Landblock-local AC positions (X east, Y north, Z up).</summary>
    public Vector3[] Positions { get; }

    /// <summary>Retail lighting normals (<see cref="TerrainVertexLighting.ComputeNormals"/>) of each vertex.</summary>
    public Vector3[] Normals { get; }

    /// <summary>
    /// Cell UV: SW (0,1), SE (1,1), NE (1,0), NW (0,0) — retail <c>CLandBlockStruct::land_uvs</c> with rotation 0.
    /// </summary>
    public Vector2[] Uvs { get; }

    /// <summary><see cref="TerrainBlendPacking.BytesPerVertex"/> bytes per vertex: CUSTOM0, CUSTOM1, CUSTOM2 (RGBA8 each).</summary>
    public byte[] Custom { get; }

    /// <summary>Index (x*9+y, 0-80) of the height-grid vertex each emitted vertex comes from, e.g. to attach per-vertex light.</summary>
    public int[] SourceVertex { get; }

    public int VertexCount => Positions.Length;

    /// <summary>Expands a terrain mesh into shader vertices with the cells' blend data.</summary>
    /// <param name="mesh">The landblock mesh (positions, triangle order, split).</param>
    /// <param name="blend">The landblock's cell recipes (same landblock).</param>
    /// <param name="normals">81 retail lighting normals (index x*9+y), normally from <see cref="TerrainVertexLighting.ComputeNormals"/>.</param>
    public static TerrainBlendVertices Build(TerrainMesh mesh, LandblockTerrainBlend blend, IReadOnlyList<Vector3> normals)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(blend);
        ArgumentNullException.ThrowIfNull(normals);
        if (mesh.Coord != blend.Coord)
        {
            throw new ArgumentException($"Mesh {mesh.Coord} and blend {blend.Coord} belong to different landblocks.", nameof(blend));
        }

        if (normals.Count != TerrainConstants.VertexCount)
        {
            throw new ArgumentException($"Expected {TerrainConstants.VertexCount} normals.", nameof(normals));
        }

        var count = mesh.Indices.Length;
        var positions = new Vector3[count];
        var vertexNormals = new Vector3[count];
        var uvs = new Vector2[count];
        var custom = new byte[count * TerrainBlendPacking.BytesPerVertex];
        var source = new int[count];
        Span<byte> packed = stackalloc byte[TerrainBlendPacking.BytesPerVertex];

        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            // TerrainMeshBuilder emits two triangles per cell, cells ordered cellX * 8 + cellY.
            var cellIndex = t / 2;
            var cellX = cellIndex / TerrainConstants.CellsPerSide;
            var cellY = cellIndex % TerrainConstants.CellsPerSide;
            TerrainBlendPacking.Pack(blend.Cells[cellIndex], packed);

            for (var k = 0; k < 3; k++)
            {
                var i = (t * 3) + k;
                var vi = mesh.Indices[i];
                var dx = (vi / TerrainConstants.VerticesPerSide) - cellX;
                var dy = (vi % TerrainConstants.VerticesPerSide) - cellY;
                if ((uint)dx > 1 || (uint)dy > 1)
                {
                    throw new InvalidOperationException($"Triangle {t} vertex {vi} is not a corner of cell ({cellX}, {cellY}).");
                }

                positions[i] = mesh.Positions[vi];
                vertexNormals[i] = normals[vi];
                uvs[i] = new Vector2(dx, 1 - dy);
                packed.CopyTo(custom.AsSpan(i * TerrainBlendPacking.BytesPerVertex));
                source[i] = vi;
            }
        }

        return new TerrainBlendVertices(mesh.Coord, positions, vertexNormals, uvs, custom, source);
    }
}
