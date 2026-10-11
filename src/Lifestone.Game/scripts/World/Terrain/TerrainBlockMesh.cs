using Godot;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// One landblock's terrain converted to Godot axes and winding, ready for <see cref="TerrainBlockMesh.Build"/>. Plain
/// arrays only, so it is built on a worker thread.
/// </summary>
public sealed class PreparedTerrainBlock
{
    internal PreparedTerrainBlock(
        LandblockTerrain terrain,
        TerrainRenderMesh source,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] uvs,
        int[] indices,
        Aabb bounds,
        double prepareMilliseconds)
    {
        Terrain = terrain;
        Source = source;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
        Bounds = bounds;
        PrepareMilliseconds = prepareMilliseconds;
    }

    public LandblockCoord Coord => Source.Coord;

    /// <summary>The landblock's heights and terrain words (e.g. for a ground-height sampler).</summary>
    public LandblockTerrain Terrain { get; }

    /// <summary>The engine-agnostic mesh (AC axes) the arrays came from.</summary>
    public TerrainRenderMesh Source { get; }

    /// <summary>Landblock-local positions, Godot axes (<see cref="AcToGodot.Position"/>).</summary>
    public Vector3[] Positions { get; }

    /// <summary>Retail lighting normals, Godot axes (<see cref="AcToGodot.Direction"/>).</summary>
    public Vector3[] Normals { get; }

    /// <summary>Cell UVs (no flip: both use a top-left origin).</summary>
    public Vector2[] Uvs { get; }

    /// <summary>Godot winding (<see cref="AcToGodot.TriangleIndices"/>).</summary>
    public int[] Indices { get; }

    /// <summary>Landblock-local bounds, Godot axes.</summary>
    public Aabb Bounds { get; }

    /// <summary>Worker time: dat read, recipe lookup, mesh, normals, packing and conversion.</summary>
    public double PrepareMilliseconds { get; }
}

/// <summary>Builds the per-landblock terrain <see cref="ArrayMesh"/> (docs/terrain-renderer.md "Mesh").</summary>
public static class TerrainBlockMesh
{
    /// <summary>CUSTOM0..2 are RGBA8 unorm (the shader reads a byte as <c>round(x * 255)</c>).</summary>
    public static readonly Mesh.ArrayFormat CustomFormatFlags =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom0Shift)
        | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom1Shift)
        | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom2Shift);

    /// <summary>Worker thread: converts an AC render mesh to Godot arrays.</summary>
    public static PreparedTerrainBlock Prepare(LandblockTerrain terrain, TerrainRenderMesh mesh, double prepareMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(mesh);
        var positions = AcToGodot.Positions(mesh.Positions);
        var normals = AcToGodot.Directions(mesh.Normals);
        var uvs = new Vector2[mesh.Uvs.Length];
        for (var i = 0; i < uvs.Length; i++)
        {
            uvs[i] = new Vector2(mesh.Uvs[i].X, mesh.Uvs[i].Y);
        }

        var indices = AcToGodot.TriangleIndices(mesh.Indices);
        var min = positions[0];
        var max = positions[0];
        foreach (var p in positions)
        {
            min = min.Min(p);
            max = max.Max(p);
        }

        return new PreparedTerrainBlock(terrain, mesh, positions, normals, uvs, indices, new Aabb(min, max - min), prepareMilliseconds);
    }

    /// <summary>Main thread: creates the mesh (one surface, the shared terrain material).</summary>
    public static ArrayMesh Build(PreparedTerrainBlock block, Material material)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Build(block.Positions, block.Normals, block.Uvs, block.Source.Custom0, block.Source.Custom1, block.Source.Custom2, block.Indices, material);
    }

    /// <summary>
    /// Main thread: builds and frees one triangle through exactly the <see cref="Build(PreparedTerrainBlock, Material)"/>
    /// path, so the first landblock attach does not pay the one-time JIT and interop set-up (≈ 14 ms measured for the
    /// placeholder layer's first build).
    /// </summary>
    public static void WarmUp(Material material)
    {
        var custom = new byte[3 * TerrainRenderMesh.CustomStride];
        Array.Fill(custom, (byte)255);
        custom[0] = custom[4] = custom[8] = 0;
        using var mesh = Build(
            [Vector3.Zero, Vector3.Right, Vector3.Back],
            [Vector3.Up, Vector3.Up, Vector3.Up],
            [Vector2.Zero, Vector2.Right, Vector2.Down],
            custom,
            custom,
            custom,
            [0, 1, 2],
            material);
    }

    private static ArrayMesh Build(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, byte[] custom0, byte[] custom1, byte[] custom2, int[] indices, Material material)
    {
        // Wave-3 performance pass: the Variants (native copies with finalizable disposers) are freed here too, not by the
        // finalizer thread: seven per landblock attach, and finalizable garbage lengthens every GC pause.
        using var arrays = new Godot.Collections.Array();
        using var vertex = Variant.CreateFrom(positions);
        using var normal = Variant.CreateFrom(normals);
        using var uv = Variant.CreateFrom(uvs);
        using var c0 = Variant.CreateFrom(custom0);
        using var c1 = Variant.CreateFrom(custom1);
        using var c2 = Variant.CreateFrom(custom2);
        using var index = Variant.CreateFrom(indices);
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertex;
        arrays[(int)Mesh.ArrayType.Normal] = normal;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Custom0] = c0;
        arrays[(int)Mesh.ArrayType.Custom1] = c1;
        arrays[(int)Mesh.ArrayType.Custom2] = c2;
        arrays[(int)Mesh.ArrayType.Index] = index;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, CustomFormatFlags);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }
}
