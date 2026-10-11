using System.Buffers;
using Godot;
using Lifestone.Client.Interiors;
using Lifestone.Data.Geometry;
using Lifestone.Data.World.Cells;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using AcQuaternion = System.Numerics.Quaternion;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Interiors;

/// <summary>
/// One surface of a cell's merged mesh (docs/interiors.md §2): the cell's own polygons, or the parts of its objects,
/// that share one material, in landblock space (Godot axes).
/// </summary>
/// <param name="Surface">The prepared surface (the material).</param>
/// <param name="Light">CUSTOM1, four floats per vertex: the light RGB (burned static lights for the cell's polygons, the
/// hardware-light sum for objects, 0 where no light reaches), then the cell's slot in the cell-state table, written
/// on the main thread when the cell attaches (<see cref="InteriorMeshes.AddSurface"/>).</param>
/// <param name="Lod">Objects track (docs/objects.md §2.6): CUSTOM2 of an object surface, two floats per vertex (1 + the part's
/// LOD slot, the vertex's level; 0 = always drawn), the slot made global when the cell attaches; null for the cell's own
/// polygons.</param>
public sealed record CellMeshSurface(
    PreparedSurface Surface,
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uvs,
    float[] ZeroNormalFlags,
    float[] Light,
    int[] Indices,
    float[]? Lod = null);

/// <summary>A cell's merged mesh, ready for <see cref="InteriorMeshes.AddSurface"/>.</summary>
public sealed class CellMeshData(IReadOnlyList<CellMeshSurface> surfaces, Aabb bounds)
{
    public IReadOnlyList<CellMeshSurface> Surfaces { get; } = surfaces;

    /// <summary>Bounds in landblock space (Godot axes).</summary>
    public Aabb Bounds { get; } = bounds;

    public int VertexCount { get; } = surfaces.Sum(s => s.Positions.Length);

    /// <summary>Approximate GPU bytes: position 12, normal 12, UV 8, zero-normal flag 4, CUSTOM1 16 per vertex (+ CUSTOM2 8 on object surfaces), 4 per index.</summary>
    public long Bytes { get; } = surfaces.Sum(s => (s.Positions.Length * (long)InteriorMeshes.BytesPerVertex) + ((s.Lod?.Length ?? 0) * 4L) + (s.Indices.Length * 4L));
}

/// <summary>
/// Collects a cell's polygons and its objects' parts on a worker and merges them into one mesh per cell, one surface
/// per material (docs/interiors.md §2). Positions go to landblock space, so the cell node needs no transform; the
/// conversion to Godot axes and winding is <see cref="AcToGodot"/>'s.
/// </summary>
/// <remarks>
/// One mesh per cell keeps the per-cell draw state in the vertex data (the slot in CUSTOM1.a) instead of in Godot
/// instance uniforms, and draws a cell's objects with as few calls as their materials allow. The cost: object parts
/// shared by many cells are copied into each (measured in docs/interiors.md §7), and blended parts of a cell are sorted
/// with the cell, not one by one.
/// </remarks>
public sealed class CellMeshMerger
{
    private readonly Dictionary<PreparedSurface, int> _groupOf = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<Contribution>> _groups = new();
    private readonly List<PreparedSurface> _surfaces = new();
    private readonly List<(AcVector3 Center, float Radius)> _boundsSpheres = new();

    /// <summary>Contributions collected since the last <see cref="Build"/>.</summary>
    public int Count { get; private set; }

    /// <summary>The cell's own polygons (cell-local AC space), lit by the burned-in static lights.</summary>
    public void AddCellSubMesh(SubMesh sub, PreparedSurface surface, Placement cellFrame, IReadOnlyList<CellLight> lights) =>
        Add(new Contribution(sub, surface, ContributionKind.Cell, cellFrame.Origin, cellFrame.Orientation, AcVector3.One, lights, default));

    /// <summary>
    /// An object part's sub-mesh (part-local AC space, placed in landblock space), lit by <paramref name="lights"/> (may be
    /// empty). <paramref name="lod"/> is the objects track's CUSTOM2 tag (1 + LOD slot, level; default = always drawn,
    /// <see cref="Objects.CellObjectLodShader"/>).
    /// </summary>
    public void AddPartSubMesh(SubMesh sub, PreparedSurface surface, AcVector3 origin, AcQuaternion rotation, AcVector3 scale, IReadOnlyList<CellLight> lights, System.Numerics.Vector2 lod = default) =>
        Add(new Contribution(sub, surface, ContributionKind.Part, origin, rotation, scale, lights, lod));

    /// <summary>
    /// Objects track: a sphere (landblock space, AC) the cell's bounds must contain beyond its rest geometry: where a
    /// camera-facing level or an animated part can turn or move to (docs/objects.md §2.6).
    /// </summary>
    public void AddBoundsSphere(AcVector3 center, float radius) => _boundsSpheres.Add((center, radius));

    /// <summary>Builds the merged mesh (null when nothing was added) and clears the merger.</summary>
    public CellMeshData? Build()
    {
        if (Count == 0)
        {
            _boundsSpheres.Clear();
            return null;
        }

        var surfaces = new CellMeshSurface[_groups.Count];
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var g = 0; g < _groups.Count; g++)
        {
            surfaces[g] = Merge(_surfaces[g], _groups[g], ref min, ref max);
        }

        foreach (var (center, radius) in _boundsSpheres)
        {
            var c = AcToGodot.Position(center);
            var r = new Vector3(radius, radius, radius);
            min = new Vector3(Math.Min(min.X, c.X - r.X), Math.Min(min.Y, c.Y - r.Y), Math.Min(min.Z, c.Z - r.Z));
            max = new Vector3(Math.Max(max.X, c.X + r.X), Math.Max(max.Y, c.Y + r.Y), Math.Max(max.Z, c.Z + r.Z));
        }

        _groupOf.Clear();
        _groups.Clear();
        _surfaces.Clear();
        _boundsSpheres.Clear();
        Count = 0;
        return new CellMeshData(surfaces, new Aabb(min, max - min));
    }

    private void Add(Contribution contribution)
    {
        if (contribution.Source.Positions.Length == 0 || contribution.Source.Indices.Length == 0)
        {
            return;
        }

        if (!_groupOf.TryGetValue(contribution.Surface, out var group))
        {
            group = _groups.Count;
            _groupOf.Add(contribution.Surface, group);
            _groups.Add(new List<Contribution>());
            _surfaces.Add(contribution.Surface);
        }

        _groups[group].Add(contribution);
        Count++;
    }

    private static CellMeshSurface Merge(PreparedSurface surface, List<Contribution> contributions, ref Vector3 min, ref Vector3 max)
    {
        var vertexCount = 0;
        var indexCount = 0;
        foreach (var c in contributions)
        {
            vertexCount += c.Source.Positions.Length;
            indexCount += c.Source.Indices.Length;
        }

        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var zero = new float[vertexCount];
        var light = new float[vertexCount * 4];

        // Objects track: object surfaces carry CUSTOM2 LOD tags (zeros when always drawn), so every cell-object mesh has
        // one vertex format (docs/objects.md §2.6).
        var lod = contributions[0].Kind == ContributionKind.Part ? new float[vertexCount * 2] : null;
        var acIndices = ArrayPool<int>.Shared.Rent(indexCount);
        try
        {
            var vertexBase = 0;
            var indexBase = 0;
            foreach (var c in contributions)
            {
                var n = c.Source.Positions.Length;
                var rgb = ArrayPool<AcVector3>.Shared.Rent(n);
                var placedPositions = ArrayPool<AcVector3>.Shared.Rent(n);
                var placedNormals = ArrayPool<AcVector3>.Shared.Rent(n);
                try
                {
                    Place(c, placedPositions.AsSpan(0, n), placedNormals.AsSpan(0, n));
                    LightOf(c, placedPositions.AsSpan(0, n), placedNormals.AsSpan(0, n), rgb.AsSpan(0, n));
                    for (var v = 0; v < n; v++)
                    {
                        var at = vertexBase + v;
                        var p = AcToGodot.Position(placedPositions[v]);
                        positions[at] = p;
                        min = new Vector3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                        max = new Vector3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                        normals[at] = AcToGodot.Direction(placedNormals[v]);
                        uvs[at] = new Vector2(c.Source.Uvs[v].X, c.Source.Uvs[v].Y); // AC and Godot share UV (0, 0) at the top-left texel
                        zero[at] = c.Source.Normals[v].LengthSquared() <= MeshFactory.ZeroNormalLengthSquared ? 1f : 0f;
                        light[at * 4] = rgb[v].X;
                        light[(at * 4) + 1] = rgb[v].Y;
                        light[(at * 4) + 2] = rgb[v].Z;
                        if (lod is not null)
                        {
                            lod[at * 2] = c.Lod.X;
                            lod[(at * 2) + 1] = c.Lod.Y;
                        }
                    }
                }
                finally
                {
                    ArrayPool<AcVector3>.Shared.Return(rgb);
                    ArrayPool<AcVector3>.Shared.Return(placedPositions);
                    ArrayPool<AcVector3>.Shared.Return(placedNormals);
                }

                // AC winding; a mirroring scale reverses it (Godot would flip the cull face for a mirrored node).
                var mirrored = c.Scale.X * c.Scale.Y * c.Scale.Z < 0f;
                var source = c.Source.Indices;
                for (var i = 0; i + 2 < source.Length; i += 3)
                {
                    acIndices[indexBase + i] = vertexBase + source[i];
                    acIndices[indexBase + i + 1] = vertexBase + source[mirrored ? i + 2 : i + 1];
                    acIndices[indexBase + i + 2] = vertexBase + source[mirrored ? i + 1 : i + 2];
                }

                vertexBase += n;
                indexBase += source.Length;
            }

            var indices = AcToGodot.TriangleIndices(acIndices.AsSpan(0, indexCount));
            return new CellMeshSurface(surface, positions, normals, uvs, zero, light, indices, lod);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(acIndices);
        }
    }

    /// <summary>Positions and normals of a contribution in landblock space (AC axes).</summary>
    private static void Place(Contribution c, Span<AcVector3> positions, Span<AcVector3> normals)
    {
        var rotation = AcQuaternion.Normalize(c.Rotation);
        var inverseScale = new AcVector3(1f / c.Scale.X, 1f / c.Scale.Y, 1f / c.Scale.Z);
        var source = c.Source;
        for (var v = 0; v < positions.Length; v++)
        {
            positions[v] = c.Origin + AcVector3.Transform(source.Positions[v] * c.Scale, rotation);
            var n = source.Normals[v];
            if (n.LengthSquared() <= MeshFactory.ZeroNormalLengthSquared)
            {
                normals[v] = AcVector3.Zero;
                continue;
            }

            var placed = AcVector3.Transform(n * inverseScale, rotation); // inverse-transpose for a scale
            normals[v] = placed.LengthSquared() > 0f ? AcVector3.Normalize(placed) : AcVector3.Zero;
        }
    }

    /// <summary>The light RGB of each vertex (§4.3 for the cell's polygons, §4.4 for object parts).</summary>
    private static void LightOf(Contribution c, ReadOnlySpan<AcVector3> placedPositions, ReadOnlySpan<AcVector3> placedNormals, Span<AcVector3> rgb)
    {
        rgb.Clear();
        if (c.Lights.Count == 0)
        {
            return;
        }

        if (c.Kind == ContributionKind.Part)
        {
            CellObjectLighting.Light(placedPositions, placedNormals, c.Lights, rgb);
            return;
        }

        // D3DPolyRender::SetStaticLightingVertexColors works in the cell's frame on the mesh's own vertices; the burned
        // colour is bytes, so the shader sees byte / 255 as the D3D vertex colour would.
        var n = placedPositions.Length;
        var bytes = ArrayPool<byte>.Shared.Rent(n * 3);
        try
        {
            CellStaticLighting.Burn(c.Source.Positions, c.Source.Normals, new Placement(c.Origin, c.Rotation), c.Lights, bytes.AsSpan(0, n * 3));
            for (var v = 0; v < n; v++)
            {
                rgb[v] = new AcVector3(bytes[v * 3] / 255f, bytes[(v * 3) + 1] / 255f, bytes[(v * 3) + 2] / 255f);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private enum ContributionKind
    {
        Cell,
        Part,
    }

    private sealed record Contribution(
        SubMesh Source,
        PreparedSurface Surface,
        ContributionKind Kind,
        AcVector3 Origin,
        AcQuaternion Rotation,
        AcVector3 Scale,
        IReadOnlyList<CellLight> Lights,
        System.Numerics.Vector2 Lod);
}

/// <summary>Builds the Godot meshes of cells (main thread).</summary>
public static class InteriorMeshes
{
    /// <summary>Approximate GPU bytes per vertex of a cell mesh (position, normal, UV, zero-normal flag, CUSTOM1).</summary>
    public const int BytesPerVertex = 52;

    /// <summary>CUSTOM1 as four floats per vertex: light RGB and the cell's slot.</summary>
    public const Mesh.ArrayFormat LightFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);

    /// <summary>Objects track: CUSTOM2 as two floats per vertex on object surfaces, the LOD tag (docs/objects.md §2.6).</summary>
    public const Mesh.ArrayFormat LodFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);

    /// <summary>
    /// Adds one merged surface to <paramref name="mesh"/>: writes <paramref name="slot"/> into every vertex's CUSTOM1.a
    /// (the cell-state table slot the shaders read; 0 = no state), then hands the arrays to the RenderingServer.
    /// </summary>
    /// <param name="lodSlotBase">Objects track: the first slot of the cells' statics in the LOD-state table, added to every
    /// non-zero CUSTOM2 tag (<see cref="Objects.CellObjectsSystem.Registration.SlotBase"/>).</param>
    public static void AddSurface(ArrayMesh mesh, CellMeshSurface surface, int slot, Material? material, int lodSlotBase = 0)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AddArrays(mesh, surface.Positions, surface.Normals, surface.Uvs, surface.ZeroNormalFlags, surface.Light, surface.Indices, slot, material, surface.Lod, lodSlotBase);
    }

    /// <summary><see cref="AddSurface"/> from bare arrays (the start-up warm-up uses it with one triangle per variant).</summary>
    public static void AddArrays(ArrayMesh mesh, Vector3[] positions, Vector3[] normals, Vector2[] uvs, float[] zeroNormalFlags, float[] light, int[] indices, int slot, Material? material, float[]? lod = null, int lodSlotBase = 0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(light);
        for (var i = 3; i < light.Length; i += 4)
        {
            light[i] = slot;
        }

        if (lod is not null && lodSlotBase != 0)
        {
            for (var i = 0; i < lod.Length; i += 2)
            {
                if (lod[i] > 0.5f)
                {
                    lod[i] += lodSlotBase;
                }
            }
        }

        // The array and its Variants (each a native copy of vertex data) are freed here rather than left to the
        // finalizer thread: thousands per dungeon.
        using var arrays = new Godot.Collections.Array();
        using var vertex = Variant.CreateFrom(positions);
        using var normal = Variant.CreateFrom(normals);
        using var uv = Variant.CreateFrom(uvs);
        using var custom0 = Variant.CreateFrom(zeroNormalFlags);
        using var custom1 = Variant.CreateFrom(light);
        using var custom2 = lod is null ? default : Variant.CreateFrom(lod);
        using var index = Variant.CreateFrom(indices);
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertex;
        arrays[(int)Mesh.ArrayType.Normal] = normal;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Custom0] = custom0;
        arrays[(int)Mesh.ArrayType.Custom1] = custom1;
        if (lod is not null)
        {
            arrays[(int)Mesh.ArrayType.Custom2] = custom2;
        }

        arrays[(int)Mesh.ArrayType.Index] = index;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: MeshFactory.ZeroNormalFormat | LightFormat | (lod is null ? 0 : LodFormat));
        if (material is not null)
        {
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    /// <summary>
    /// A mesh of <see cref="MeshFactory.Prepare"/>'s arrays plus CUSTOM1 (four floats per vertex and sub-mesh, as in
    /// <see cref="CellMeshSurface.Light"/>): the engine checks' quads.
    /// </summary>
    public static ArrayMesh Build(PreparedMesh prepared, IReadOnlyList<float[]> light, Func<int, Material?> materialFor)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(materialFor);
        if (light.Count != prepared.SubMeshes.Count)
        {
            throw new ArgumentException($"{light.Count} light arrays for {prepared.SubMeshes.Count} sub-meshes.", nameof(light));
        }

        var mesh = new ArrayMesh();
        for (var i = 0; i < prepared.SubMeshes.Count; i++)
        {
            var sub = prepared.SubMeshes[i];
            if (light[i].Length != sub.Positions.Length * 4)
            {
                throw new ArgumentException($"Sub-mesh {i}: {light[i].Length} light floats for {sub.Positions.Length} vertices.", nameof(light));
            }

            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = sub.Positions;
            arrays[(int)Mesh.ArrayType.Normal] = sub.Normals;
            arrays[(int)Mesh.ArrayType.TexUV] = sub.Uvs;
            arrays[(int)Mesh.ArrayType.Custom0] = sub.ZeroNormalFlags;
            arrays[(int)Mesh.ArrayType.Custom1] = light[i];
            arrays[(int)Mesh.ArrayType.Custom2] = new float[sub.Positions.Length * 2]; // objects track: LOD tags, always drawn
            arrays[(int)Mesh.ArrayType.Index] = sub.Indices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: MeshFactory.ZeroNormalFormat | LightFormat | LodFormat);
            if (materialFor(i) is { } material)
            {
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
            }
        }

        mesh.CustomAabb = prepared.Bounds;
        return mesh;
    }
}
