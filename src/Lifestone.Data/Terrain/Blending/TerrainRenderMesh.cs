using System.Numerics;

namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// The GPU-ready terrain mesh of one landblock for the TexMerge shader: indexed, four vertices per cell (the cell's
/// corners), each carrying the cell's packed <see cref="TerrainCellBlend"/>, so the blend data is flat per cell while
/// both triangles of a cell share their corners. In AC landblock-local coordinates and AC winding; the Godot layer
/// converts with <c>AcToGodot</c>.
/// </summary>
/// <remarks>
/// <para>Per vertex (docs/terrain-shader.md §3.2):</para>
/// <list type="bullet">
/// <item><see cref="Positions"/>: the height-grid corner, <c>(x·24, y·24, LandHeightTable[h])</c>
/// (<c>CLandBlockStruct::ConstructVertices</c>, via <see cref="TerrainMeshBuilder"/>).</item>
/// <item><see cref="Normals"/>: the retail lighting normal of that grid vertex (<see cref="TerrainVertexLighting.ComputeNormals"/>,
/// <c>CLandBlockStruct::calc_lighting</c>), or the across-blocks variant (<see cref="TerrainSeamlessNormals"/>).</item>
/// <item><see cref="Uvs"/>: cell UV, SW (0,1), SE (1,1), NE (1,0), NW (0,0) (<c>CLandBlockStruct::land_uvs</c>, rotation 0).</item>
/// <item><see cref="Custom0"/>/<see cref="Custom1"/>/<see cref="Custom2"/>: RGBA8 bytes of <see cref="TerrainBlendPacking.Pack(in TerrainCellBlend, Span{byte})"/>
/// (bytes 0-3, 4-7 and 8-11), identical on the four vertices of a cell.</item>
/// </list>
/// <para>
/// Triangles are exactly <see cref="TerrainMesh.Indices"/> (same order, same per-cell diagonal from <see cref="TerrainSplit"/>,
/// counter-clockwise from above) with each grid index replaced by the cell-local corner vertex. 256 vertices and 384
/// indices instead of the 384 non-indexed vertices of <see cref="TerrainBlendVertices"/>: a third fewer vertex shader runs,
/// same pixels.
/// </para>
/// </remarks>
public sealed class TerrainRenderMesh
{
    /// <summary>Vertices per cell: SW, SE, NE, NW.</summary>
    public const int VerticesPerCell = 4;

    /// <summary>Vertices per landblock (64 cells × 4).</summary>
    public const int VertexCount = TerrainConstants.CellCount * VerticesPerCell;

    /// <summary>Indices per landblock (128 triangles × 3).</summary>
    public const int IndexCount = TerrainConstants.TriangleCount * 3;

    /// <summary>Bytes per vertex in each of <see cref="Custom0"/>, <see cref="Custom1"/>, <see cref="Custom2"/>.</summary>
    public const int CustomStride = 4;

    private TerrainRenderMesh(
        TerrainMesh mesh,
        LandblockTerrainBlend blend,
        Vector3[] gridNormals,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] uvs,
        byte[] custom0,
        byte[] custom1,
        byte[] custom2,
        int[] indices,
        int[] sourceVertex)
    {
        Mesh = mesh;
        Blend = blend;
        GridNormals = gridNormals;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Custom0 = custom0;
        Custom1 = custom1;
        Custom2 = custom2;
        Indices = indices;
        SourceVertex = sourceVertex;
        MinHeight = mesh.Positions.Min(p => p.Z);
        MaxHeight = mesh.Positions.Max(p => p.Z);
    }

    public LandblockCoord Coord => Mesh.Coord;

    /// <summary>The 9 × 9 source mesh (positions, triangle order, splits).</summary>
    public TerrainMesh Mesh { get; }

    /// <summary>The 64 cell recipes (index <c>cellX * 8 + cellY</c>).</summary>
    public LandblockTerrainBlend Blend { get; }

    /// <summary>The 81 lighting normals of the height grid (index x*9+y) the vertices use.</summary>
    public Vector3[] GridNormals { get; }

    /// <summary>256 positions (cell <c>c = cellX*8 + cellY</c> owns vertices 4c .. 4c+3: SW, SE, NE, NW).</summary>
    public Vector3[] Positions { get; }

    /// <summary>256 lighting normals (of each vertex's grid vertex).</summary>
    public Vector3[] Normals { get; }

    /// <summary>256 cell UVs.</summary>
    public Vector2[] Uvs { get; }

    /// <summary>CUSTOM0 RGBA8: base, overlay0, overlay1, overlay2 colour layers.</summary>
    public byte[] Custom0 { get; }

    /// <summary>CUSTOM1 RGBA8: overlay0, overlay1, overlay2, road0 alpha layers.</summary>
    public byte[] Custom1 { get; }

    /// <summary>CUSTOM2 RGBA8: road1 alpha layer, packed rotations, road1 rotation | flags &lt;&lt; 2, road colour layer.</summary>
    public byte[] Custom2 { get; }

    /// <summary>384 indices into the 256 vertices, AC winding (counter-clockwise from above).</summary>
    public int[] Indices { get; }

    /// <summary>Grid index (x*9+y) of each vertex.</summary>
    public int[] SourceVertex { get; }

    /// <summary>Lowest terrain height in the block (metres).</summary>
    public float MinHeight { get; }

    /// <summary>Highest terrain height in the block (metres).</summary>
    public float MaxHeight { get; }

    /// <summary>Corner slot (0-3: SW, SE, NE, NW) of grid offset (dx, dy) ∈ {0,1}² inside a cell.</summary>
    public static int CornerSlot(int dx, int dy) => (dx, dy) switch
    {
        (0, 0) => 0,
        (1, 0) => 1,
        (1, 1) => 2,
        (0, 1) => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(dx), $"({dx}, {dy}) is not a cell corner offset."),
    };

    /// <summary>Cell UV of a corner slot: SW (0,1), SE (1,1), NE (1,0), NW (0,0).</summary>
    public static Vector2 CornerUv(int slot) => slot switch
    {
        0 => new Vector2(0f, 1f),
        1 => new Vector2(1f, 1f),
        2 => new Vector2(1f, 0f),
        3 => new Vector2(0f, 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>Builds the render mesh of a landblock.</summary>
    /// <param name="terrain">The landblock's heights and terrain words.</param>
    /// <param name="region">LandHeightTable and cell size.</param>
    /// <param name="texMerge">Palette code → cell recipe (shared, cached).</param>
    /// <param name="gridNormals">81 normals to use; null = retail per-block normals (<see cref="TerrainVertexLighting.ComputeNormals"/>).</param>
    public static TerrainRenderMesh Build(
        LandblockTerrain terrain,
        TerrainRegion region,
        TerrainTexMerge texMerge,
        IReadOnlyList<Vector3>? gridNormals = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(texMerge);

        var mesh = TerrainMeshBuilder.Build(terrain, region);
        var blend = texMerge.BuildLandblock(terrain);
        var normals81 = gridNormals?.ToArray()
            ?? TerrainVertexLighting.ComputeNormals(terrain, region.LandHeightTable, region.SquareLength);
        if (normals81.Length != TerrainConstants.VertexCount)
        {
            throw new ArgumentException($"Expected {TerrainConstants.VertexCount} grid normals.", nameof(gridNormals));
        }

        var positions = new Vector3[VertexCount];
        var normals = new Vector3[VertexCount];
        var uvs = new Vector2[VertexCount];
        var source = new int[VertexCount];
        var custom0 = new byte[VertexCount * CustomStride];
        var custom1 = new byte[VertexCount * CustomStride];
        var custom2 = new byte[VertexCount * CustomStride];
        Span<byte> packed = stackalloc byte[TerrainBlendPacking.BytesPerVertex];

        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var cell = LandblockTerrainBlend.CellIndex(cx, cy);
                TerrainBlendPacking.Pack(blend.Cells[cell], packed);
                for (var slot = 0; slot < VerticesPerCell; slot++)
                {
                    var (dx, dy) = slot switch { 0 => (0, 0), 1 => (1, 0), 2 => (1, 1), _ => (0, 1) };
                    var grid = TerrainConstants.VertexIndex(cx + dx, cy + dy);
                    var v = (cell * VerticesPerCell) + slot;
                    positions[v] = mesh.Positions[grid];
                    normals[v] = normals81[grid];
                    uvs[v] = CornerUv(slot);
                    source[v] = grid;
                    packed[..4].CopyTo(custom0.AsSpan(v * CustomStride));
                    packed[4..8].CopyTo(custom1.AsSpan(v * CustomStride));
                    packed[8..12].CopyTo(custom2.AsSpan(v * CustomStride));
                }
            }
        }

        // Same triangles and order as TerrainMesh.Indices (two per cell, cells ordered cellX * 8 + cellY).
        var indices = new int[IndexCount];
        for (var i = 0; i < mesh.Indices.Length; i++)
        {
            var cell = i / 6;
            var cx = cell / TerrainConstants.CellsPerSide;
            var cy = cell % TerrainConstants.CellsPerSide;
            var grid = mesh.Indices[i];
            var dx = (grid / TerrainConstants.VerticesPerSide) - cx;
            var dy = (grid % TerrainConstants.VerticesPerSide) - cy;
            if ((uint)dx > 1 || (uint)dy > 1)
            {
                throw new InvalidOperationException($"Index {i} (grid vertex {grid}) is not a corner of cell ({cx}, {cy}).");
            }

            indices[i] = (cell * VerticesPerCell) + CornerSlot(dx, dy);
        }

        return new TerrainRenderMesh(mesh, blend, normals81, positions, normals, uvs, custom0, custom1, custom2, indices, source);
    }

    /// <summary>The 12 packed bytes of vertex <paramref name="vertex"/> (CUSTOM0, CUSTOM1, CUSTOM2).</summary>
    public byte[] PackedBytes(int vertex)
    {
        var bytes = new byte[TerrainBlendPacking.BytesPerVertex];
        Custom0.AsSpan(vertex * CustomStride, CustomStride).CopyTo(bytes);
        Custom1.AsSpan(vertex * CustomStride, CustomStride).CopyTo(bytes.AsSpan(4));
        Custom2.AsSpan(vertex * CustomStride, CustomStride).CopyTo(bytes.AsSpan(8));
        return bytes;
    }
}
