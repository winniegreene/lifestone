using System.Numerics;
using Lifestone.Data.Geometry;

namespace Lifestone.Data.Models;

/// <summary>
/// One drawing polygon as retail <c>D3DPolyRender::ConstructMesh</c> reads it: a <c>CPolygon</c> after
/// <c>CPolygon::UnPack</c>, whatever record it came from (a GfxObj's drawing polygons or an EnvCell CellStruct's).
/// </summary>
/// <param name="Id">The polygon's id (error messages only).</param>
/// <param name="VertexIds">Corner vertex ids, counter-clockwise seen from the positive side.</param>
/// <param name="PosSurface">Positive-side index into the owner's surface list (stored as an unsigned 16-bit value).</param>
/// <param name="NegSurface">Negative-side index (read only for <c>sides_type 2</c>).</param>
/// <param name="SidesType">Raw <c>sides_type</c>: 0 single-sided, 1 both sides with the positive surface, 2 separate negative side.</param>
/// <param name="Stippling">Raw stippling byte: 1 Positive, 2 Negative, 4 NoPos, 8 NoNeg.</param>
/// <param name="PosUvIndices">Per-corner UV index of the positive side; empty when the side stores none (NoPos).</param>
/// <param name="NegUvIndices">Per-corner UV index of the negative side; empty when it stores none (NoNeg, or not sides type 2).</param>
public readonly record struct ConstructMeshPolygon(
    int Id,
    IReadOnlyList<ushort> VertexIds,
    ushort PosSurface,
    ushort NegSurface,
    int SidesType,
    byte Stippling,
    IReadOnlyList<byte> PosUvIndices,
    IReadOnlyList<byte> NegUvIndices)
{
    /// <summary><c>StipplingType.NoPos</c>: the positive side stores no UV indices.</summary>
    public const byte NoPosBit = 0x4;

    public bool NoPos => (Stippling & NoPosBit) != 0;
}

/// <summary>A vertex array as <c>D3DPolyRender::ConstructMesh</c> reads it (<c>CVertexArray</c> of <c>CSWVertex</c>).</summary>
public interface IConstructMeshVertices
{
    /// <summary>Vertices in the array (statistics only).</summary>
    int Count { get; }

    /// <summary>The vertex <paramref name="id"/>: its position, its stored (unnormalised) normal and how many UVs it has.</summary>
    /// <returns>False when the array has no such vertex.</returns>
    bool TryGet(ushort id, out Vector3 position, out Vector3 normal, out int uvCount);

    /// <summary>UV <paramref name="index"/> of vertex <paramref name="id"/> (0 &lt;= index &lt; its UV count).</summary>
    Vector2 Uv(ushort id, int index);
}

/// <summary>
/// Output of <see cref="RetailConstructMesh.Build"/>: the sub-meshes, their retail draw metadata (parallel to
/// <see cref="MeshData.SubMeshes"/>), statistics, and how many polygons the surface filter kept or dropped.
/// </summary>
/// <param name="Mesh">One sub-mesh per (Surface id, side, addressing), in retail subset order.</param>
/// <param name="SubMeshInfo">Retail draw metadata, parallel to <c>Mesh.SubMeshes</c>.</param>
/// <param name="Stats">Counts gathered while building.</param>
/// <param name="DrawnPolygonCount">Polygons with at least one side in the mesh.</param>
/// <param name="FilteredPolygonCount">Non-degenerate polygons whose every side the surface filter dropped.</param>
public sealed record ConstructedMesh(
    MeshData Mesh,
    IReadOnlyList<SubMeshDrawInfo> SubMeshInfo,
    PolygonMeshStats Stats,
    int DrawnPolygonCount,
    int FilteredPolygonCount);

/// <summary>
/// The one re-expression of retail <c>D3DPolyRender::ConstructMesh</c> (acclient.c:455780; 11.4186, rendering core
/// unchanged in 11.6096) in Lifestone: GfxObj meshes (<see cref="GfxObjMeshBuilder"/>, called through
/// <c>D3DPolyRender::ConstructMesh(CGfxObj*)</c>, acclient.c:456487) and EnvCell meshes (<c>CellMeshBuilder</c>, called
/// from <c>CEnvCell::UnPack</c> with the CellStruct's polygons, acclient.c:349389) both adapt their records to it.
/// </summary>
/// <remarks>
/// <para><b>Per drawing polygon</b> (in stored order; the loop at acclient.c:456173):</para>
/// <list type="bullet">
/// <item><b>Sides.</b> <c>numSides = sides_type == 2 ? 2 : 1</c> and <c>numWindings = sides_type == 1 ? 2 : 1</c>: the
/// positive side always (surface <c>pos_surface</c>, UVs <c>pos_uv_indices</c>); for <c>sides_type 1</c> a second,
/// reversed winding (<c>indexBuf[2 - c]</c>) of the positive side with negated normals (<c>normalMultiplier = -1</c>);
/// for <c>sides_type 2</c> a negative side with <c>neg_surface</c> and <c>neg_uv_indices</c>, negated normals.
/// Retail would draw any other <c>sides_type</c> single-sided, but no end-of-retail record has one, so how
/// <c>CPolygon::UnPack</c> reads such a polygon is unverified: <b>it fails loudly</b> (<see cref="NotSupportedException"/>).</item>
/// <item><b>Triangles</b> are fans <c>(v0, v[t+1], v[t+2])</c>; a negative side is written reversed. That is the
/// <see cref="MeshData"/> contract (a real back face); retail does not reverse a <c>sides_type 2</c> negative side
/// (see <see cref="GfxObjMeshBuilder"/>'s remarks: 11 polygons in 6 GfxObjs, none in a CellStruct).</item>
/// <item><b>UVs.</b> A side with a UV table uses <c>table[corner]</c>, one without uses index 0 for every corner
/// (<c>uvsForSide ? uvsForSide[c] : 0</c>). <c>CPolygon::UnPack</c> stores either no table or one entry per corner;
/// any other length fails loudly (<see cref="InvalidDataException"/>). <c>copyVert</c> reads the index as a signed
/// <c>char</c> and writes (0, 0) unless <c>0 &lt;= index &lt; num_uvs</c>.</item>
/// <item><b>Normals</b> are the stored <c>CSWVertex</c> normals, unnormalised (<c>copyVert</c>).</item>
/// <item><b>Degenerate</b> polygons (fewer than 3 corners) add <c>num_pts - 2 &lt;= 0</c> triangles: nothing.</item>
/// </list>
/// <para><b>Texture addressing.</b> <c>isStippledOrAlphaedMask[pos_surface] |= (char)stippling &gt; 0</c> over every
/// polygon, degenerate ones included; a set bit selects WRAP for the whole subset, else CLAMP
/// (<c>D3DPolyRender::SetSurface</c>, acclient.c:454437). The mask is final only after the whole list, so it is computed
/// first.</para>
/// <para><b>Sub-meshes.</b> Retail builds one D3DX attribute subset per surface <i>index</i> and draws them in index
/// order (<c>D3DPolyRender::DrawMesh</c>). <see cref="MeshData"/> wants one sub-mesh per (Surface id, side,
/// addressing): indices naming the same Surface with the same addressing share a sub-mesh, each keeping a contiguous
/// block of triangles in file order (<see cref="SubMeshDrawInfo.Ranges"/>, ascending index); sub-meshes are ordered by
/// their first surface index, positive side first. Vertices are split per (vertex id, UV index) within a sub-mesh, like
/// retail's <c>vertmap</c>.</para>
/// <para><b>Surface filter.</b> Retail constructs every subset and skips some when it draws them (untextured subsets of
/// buildings and EnvCells, <see cref="SurfaceDrawRules"/>). A caller that only ever draws in one such context may drop
/// those sides at build time with <c>keepSurface</c>; the sub-meshes that remain are exactly the unfiltered build's
/// minus the dropped ones (the addressing mask still sees every polygon).</para>
/// </remarks>
public static class RetailConstructMesh
{
    /// <summary>Retail <c>CullMode</c> value meaning "both sides, same surface" (ACE <c>CullMode.None</c>).</summary>
    public const int SidesTypeDoubleSided = 1;

    /// <summary>Retail <c>CullMode</c> value meaning "separate negative side" (ACE <c>CullMode.Clockwise</c>).</summary>
    public const int SidesTypeSeparateBack = 2;

    /// <summary>Builds the mesh of a polygon list (see the class remarks).</summary>
    /// <param name="owner">The owning record, for error messages (e.g. "0x01000C1E", "CellStruct 0x0D00034F[0]").</param>
    /// <param name="surfaceIds">Resolved Surface ids (<c>0x08xxxxxx</c>) indexed by the polygons' surface indices.</param>
    /// <param name="vertices">The vertex array.</param>
    /// <param name="polygons">Drawing polygons in stored order.</param>
    /// <param name="keepSurface">Null to emit every side, else which Surface ids to emit (asked once per surface index).</param>
    /// <exception cref="InvalidDataException">A polygon references a missing vertex or surface index, or has a UV table
    /// whose length is neither 0 nor its corner count.</exception>
    /// <exception cref="NotSupportedException">A polygon has a <c>sides_type</c> other than 0, 1 or 2.</exception>
    public static ConstructedMesh Build(
        string owner,
        IReadOnlyList<uint> surfaceIds,
        IConstructMeshVertices vertices,
        IReadOnlyList<ConstructMeshPolygon> polygons,
        Func<uint, bool>? keepSurface = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(surfaceIds);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(polygons);

        var surfaceCount = surfaceIds.Count;

        // isStippledOrAlphaedMask, over every polygon (degenerate ones included), before any triangle is placed.
        var wrapByIndex = new bool[surfaceCount];
        for (var p = 0; p < polygons.Count; p++)
        {
            var polygon = polygons[p];
            var posIndex = CheckSurfaceIndex(owner, polygon.Id, polygon.PosSurface, surfaceCount, "positive");
            if (polygon.Stippling is > 0 and < 0x80)
            {
                wrapByIndex[posIndex] = true;
            }
        }

        // keepSurface, asked once per surface index: 0 = not asked yet, 1 = keep, 2 = drop.
        var keepByIndex = new byte[surfaceCount];
        var buckets = new Dictionary<(uint SurfaceId, MeshSide Side, TextureAddressMode Addressing), Bucket>();
        var zeroNormalVertices = new HashSet<ushort>();
        int noPos = 0, doubleSided = 0, separateBack = 0, degenerate = 0, outOfRangeUv = 0, drawn = 0, filtered = 0;

        for (var p = 0; p < polygons.Count; p++)
        {
            var polygon = polygons[p];
            if (polygon.NoPos)
            {
                noPos++;
            }

            var sidesType = polygon.SidesType;
            if (sidesType is < 0 or > SidesTypeSeparateBack)
            {
                throw new NotSupportedException(
                    $"TODO(models): {owner} polygon {polygon.Id} has sides type {sidesType}. Retail's ConstructMesh would draw " +
                    "it single-sided (numSides = sides_type == 2 ? 2 : 1, numWindings = sides_type == 1 ? 2 : 1), but no " +
                    "end-of-retail record has one, so CPolygon::UnPack's reading of it is unverified.");
            }

            if (polygon.VertexIds.Count < 3)
            {
                // Retail adds (num_pts - 2) <= 0 triangles: nothing is drawn.
                degenerate++;
                continue;
            }

            var anyEmitted = Emit(polygon, MeshSide.Positive, polygon.PosSurface, polygon.PosUvIndices, countUvs: true);
            if (sidesType == SidesTypeDoubleSided)
            {
                doubleSided++;
                anyEmitted |= Emit(polygon, MeshSide.Negative, polygon.PosSurface, polygon.PosUvIndices, countUvs: false);
            }
            else if (sidesType == SidesTypeSeparateBack)
            {
                separateBack++;
                var negIndex = CheckSurfaceIndex(owner, polygon.Id, polygon.NegSurface, surfaceCount, "negative");
                anyEmitted |= Emit(polygon, MeshSide.Negative, negIndex, polygon.NegUvIndices, countUvs: true);
            }

            if (anyEmitted)
            {
                drawn++;
            }
            else
            {
                filtered++;
            }
        }

        var ordered = buckets.Values
            .OrderBy(b => b.FirstSurfaceIndex)
            .ThenBy(b => b.Side)
            .ToList();
        var subMeshes = new SubMesh[ordered.Count];
        var info = new SubMeshDrawInfo[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            var indices = new List<int>();
            var ranges = new List<SurfaceIndexRange>(b.TrianglesBySurfaceIndex.Count);
            foreach (var (surfaceIndex, triangles) in b.TrianglesBySurfaceIndex)
            {
                ranges.Add(new SurfaceIndexRange(surfaceIndex, indices.Count, triangles.Count));
                indices.AddRange(triangles);
            }

            subMeshes[i] = new SubMesh(b.SurfaceId, b.Side, b.Addressing, [.. b.Positions], [.. b.Normals], [.. b.Uvs], [.. indices]);
            info[i] = new SubMeshDrawInfo(b.FirstSurfaceIndex, b.PolygonCount, ranges);
        }

        var stats = new PolygonMeshStats(
            vertices.Count,
            polygons.Count,
            noPos,
            doubleSided,
            separateBack,
            degenerate,
            zeroNormalVertices.Count,
            outOfRangeUv);
        return new ConstructedMesh(new MeshData(subMeshes), info, stats, drawn, filtered);

        bool Emit(ConstructMeshPolygon polygon, MeshSide side, int surfaceIndex, IReadOnlyList<byte> uvIndices, bool countUvs)
        {
            var surfaceId = surfaceIds[surfaceIndex];
            if (keepSurface is not null)
            {
                if (keepByIndex[surfaceIndex] == 0)
                {
                    keepByIndex[surfaceIndex] = keepSurface(surfaceId) ? (byte)1 : (byte)2;
                }

                if (keepByIndex[surfaceIndex] == 2)
                {
                    return false;
                }
            }

            var n = polygon.VertexIds.Count;
            var hasUvTable = uvIndices.Count > 0;
            if (hasUvTable && uvIndices.Count != n)
            {
                throw new InvalidDataException(
                    $"{owner} polygon {polygon.Id}: the {side.ToString().ToLowerInvariant()} side has {uvIndices.Count} UV indices for {n} corners.");
            }

            var addressing = wrapByIndex[surfaceIndex] ? TextureAddressMode.Wrap : TextureAddressMode.Clamp;
            if (!buckets.TryGetValue((surfaceId, side, addressing), out var bucket))
            {
                bucket = new Bucket(surfaceId, side, addressing);
                buckets.Add((surfaceId, side, addressing), bucket);
            }

            bucket.PolygonCount++;
            var triangles = bucket.TrianglesFor(surfaceIndex);
            Span<int> corners = n <= 64 ? stackalloc int[n] : new int[n];
            for (var c = 0; c < n; c++)
            {
                var vertexId = polygon.VertexIds[c];
                if (!vertices.TryGet(vertexId, out var position, out var normal, out var uvCount))
                {
                    throw new InvalidDataException($"{owner} polygon {polygon.Id} references missing vertex {vertexId}.");
                }

                // Explicit UV indices when the side stores them, else index 0 (retail's null table).
                var uvIndex = hasUvTable ? uvIndices[c] : 0;
                var uvValid = HasUv(uvIndex, uvCount);
                if (countUvs && hasUvTable && !uvValid)
                {
                    outOfRangeUv++;
                }

                if (normal == Vector3.Zero)
                {
                    zeroNormalVertices.Add(vertexId);
                }

                corners[c] = bucket.GetOrAddVertex(
                    vertexId,
                    uvIndex,
                    position,
                    side == MeshSide.Negative ? -normal : normal,
                    uvValid ? vertices.Uv(vertexId, uvIndex) : Vector2.Zero);
            }

            // Fan (v0, v[t+1], v[t+2]) as in ConstructMesh (corner c of triangle t is 0, t+1, t+2); the negative side
            // is written reversed.
            for (var t = 0; t < n - 2; t++)
            {
                if (side == MeshSide.Positive)
                {
                    triangles.Add(corners[0]);
                    triangles.Add(corners[t + 1]);
                    triangles.Add(corners[t + 2]);
                }
                else
                {
                    triangles.Add(corners[t + 2]);
                    triangles.Add(corners[t + 1]);
                    triangles.Add(corners[0]);
                }
            }

            return true;
        }
    }

    /// <summary><c>copyVert</c>'s UV test: the index is read as a signed <c>char</c> and must be inside the vertex's UV list.</summary>
    public static bool HasUv(int uvIndex, int uvCount) => unchecked((sbyte)uvIndex) >= 0 && uvIndex < uvCount;

    private static int CheckSurfaceIndex(string owner, int polygonId, ushort index, int surfaceCount, string side)
    {
        // Retail stores pos_surface/neg_surface as unsigned 16-bit indices into the surface array.
        if (index >= surfaceCount)
        {
            throw new InvalidDataException(
                $"{owner} polygon {polygonId}: {side} surface index {index} is outside its {surfaceCount} surfaces.");
        }

        return index;
    }

    private sealed class Bucket(uint surfaceId, MeshSide side, TextureAddressMode addressing)
    {
        private readonly Dictionary<(ushort VertexId, int UvIndex), int> _vertexMap = [];

        public uint SurfaceId { get; } = surfaceId;

        public MeshSide Side { get; } = side;

        public TextureAddressMode Addressing { get; } = addressing;

        /// <summary>Triangle corner indices per contributing surface index, ascending (retail subset order).</summary>
        public SortedDictionary<int, List<int>> TrianglesBySurfaceIndex { get; } = [];

        public int FirstSurfaceIndex => TrianglesBySurfaceIndex.Keys.First();

        public int PolygonCount { get; set; }

        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> Uvs { get; } = [];

        public List<int> TrianglesFor(int surfaceIndex)
        {
            if (!TrianglesBySurfaceIndex.TryGetValue(surfaceIndex, out var triangles))
            {
                triangles = [];
                TrianglesBySurfaceIndex.Add(surfaceIndex, triangles);
            }

            return triangles;
        }

        public int GetOrAddVertex(ushort vertexId, int uvIndex, Vector3 position, Vector3 normal, Vector2 uv)
        {
            if (_vertexMap.TryGetValue((vertexId, uvIndex), out var existing))
            {
                return existing;
            }

            var index = Positions.Count;
            Positions.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            _vertexMap.Add((vertexId, uvIndex), index);
            return index;
        }
    }
}
