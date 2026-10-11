using Godot;
using Lifestone.Data.Geometry;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>One sub-mesh converted to Godot axes and winding, ready to become an ArrayMesh surface.</summary>
public sealed class PreparedSubMesh(SubMesh source, Vector3[] positions, Vector3[] normals, float[] zeroNormalFlags, Vector2[] uvs, int[] indices)
{
    /// <summary>The engine-agnostic source (surface id, side, addressing).</summary>
    public SubMesh Source { get; } = source;

    public Vector3[] Positions { get; } = positions;

    public Vector3[] Normals { get; } = normals;

    /// <summary>
    /// Per vertex: 1 where the AC normal is zero (it gets ambient + emissive only), else 0. Uploaded as CUSTOM0.x
    /// (<see cref="MeshFactory.ZeroNormalFormat"/>), because Godot cannot store a zero normal (docs/render-contract.md §2.1).
    /// </summary>
    public float[] ZeroNormalFlags { get; } = zeroNormalFlags;

    public Vector2[] Uvs { get; } = uvs;

    public int[] Indices { get; } = indices;
}

/// <summary>A <see cref="MeshData"/> converted on a worker; <see cref="MeshFactory.Build"/> makes the ArrayMesh.</summary>
public sealed class PreparedMesh(IReadOnlyList<PreparedSubMesh> subMeshes, Aabb bounds)
{
    public IReadOnlyList<PreparedSubMesh> SubMeshes { get; } = subMeshes;

    /// <summary>Bounds in Godot axes (from <see cref="MeshData.Bounds"/>).</summary>
    public Aabb Bounds { get; } = bounds;

    /// <summary>Approximate GPU bytes: 36 bytes per vertex (position, normal, UV, zero-normal flag) and 4 per index.</summary>
    public long Bytes => SubMeshes.Sum(s => (s.Positions.Length * 36L) + (s.Indices.Length * 4L));

    public int VertexCount => SubMeshes.Sum(s => s.Positions.Length);
}

/// <summary>
/// <see cref="MeshData"/> (AC axes, counter-clockwise front faces) to Godot <see cref="ArrayMesh"/>es: one surface per
/// sub-mesh, its material from the caller, and a custom AABB from the data's bounds. All axis and winding conversion
/// goes through <see cref="AcToGodot"/>.
/// </summary>
/// <remarks>
/// <para>Split in two so the heavy part stays off the main thread: <see cref="Prepare"/> (any thread) converts the arrays;
/// <see cref="Build"/> (main thread, within the frame budget) hands them to the RenderingServer. Sub-meshes are kept in
/// <see cref="MeshData"/> order; materials must be keyed by (Surface id, addressing) — see
/// <see cref="SubMesh.Addressing"/> — and culled back (the builders emit explicit back faces).</para>
/// <para><b>Zero normals.</b> Godot stores vertex normals octahedral-encoded, so a zero normal comes back as a unit
/// vector ((0, 0, -1), AC north) and a shader cannot tell it apart. Every surface carries CUSTOM0 (one float per vertex)
/// set to 1 where the AC normal was zero; the surface shaders skip the sun term there (docs/render-contract.md §2.1,
/// pinned by <c>--engine-checks</c>). A mesh built without CUSTOM0 reads 0 and is lit normally.</para>
/// </remarks>
public static class MeshFactory
{
    /// <summary>Squared length below which an AC normal counts as zero (stored normals are unit length or exactly zero).</summary>
    public const float ZeroNormalLengthSquared = 1e-12f;

    /// <summary>The surface format flags for the CUSTOM0 zero-normal flag: one 32-bit float per vertex.</summary>
    public const Mesh.ArrayFormat ZeroNormalFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);

    /// <summary>Converts a mesh to Godot arrays. Thread-safe; touches no engine server.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="include">Optional filter over sub-mesh indices (e.g. <c>SurfaceDrawRules.DrawnSubMeshes</c>).</param>
    public static PreparedMesh Prepare(MeshData mesh, Func<int, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var subs = new List<PreparedSubMesh>(mesh.SubMeshes.Count);
        for (var i = 0; i < mesh.SubMeshes.Count; i++)
        {
            if (include is not null && !include(i))
            {
                continue;
            }

            var sub = mesh.SubMeshes[i];
            var uvs = new Vector2[sub.Uvs.Length];
            for (var u = 0; u < uvs.Length; u++)
            {
                // AC and Godot both put UV (0, 0) at the texture's top-left texel: no flip.
                uvs[u] = new Vector2(sub.Uvs[u].X, sub.Uvs[u].Y);
            }

            var flags = new float[sub.Normals.Length];
            for (var n = 0; n < flags.Length; n++)
            {
                flags[n] = sub.Normals[n].LengthSquared() <= ZeroNormalLengthSquared ? 1f : 0f;
            }

            subs.Add(new PreparedSubMesh(
                sub,
                AcToGodot.Positions(sub.Positions),
                AcToGodot.Directions(sub.Normals),
                flags,
                uvs,
                AcToGodot.TriangleIndices(sub.Indices)));
        }

        return new PreparedMesh(subs, AcToGodot.Bounds(mesh.Bounds.Min, mesh.Bounds.Max));
    }

    /// <summary>Creates the ArrayMesh. Main thread.</summary>
    /// <param name="prepared">Converted arrays.</param>
    /// <param name="materialFor">
    /// The material of each prepared sub-mesh (by index into <see cref="PreparedMesh.SubMeshes"/>), or null to leave the
    /// surfaces without materials (the shared-mesh cache binds them).
    /// </param>
    public static ArrayMesh Build(PreparedMesh prepared, Func<int, Material?>? materialFor = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var mesh = new ArrayMesh();
        for (var i = 0; i < prepared.SubMeshes.Count; i++)
        {
            var sub = prepared.SubMeshes[i];

            // Wave-3 performance pass: the array and its Variants (each a native copy of the data, with a finalizable
            // disposer) are freed here, not left to the finalizer thread; finalizable garbage lengthens every GC pause.
            using var arrays = new Godot.Collections.Array();
            using var positions = Variant.CreateFrom(sub.Positions);
            using var normals = Variant.CreateFrom(sub.Normals);
            using var uvs = Variant.CreateFrom(sub.Uvs);
            using var zeroNormalFlags = Variant.CreateFrom(sub.ZeroNormalFlags);
            using var indices = Variant.CreateFrom(sub.Indices);
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Custom0] = zeroNormalFlags;
            arrays[(int)Mesh.ArrayType.Index] = indices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: ZeroNormalFormat);
            if (materialFor?.Invoke(i) is { } material)
            {
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
            }
        }

        mesh.CustomAabb = prepared.Bounds;
        return mesh;
    }
}
