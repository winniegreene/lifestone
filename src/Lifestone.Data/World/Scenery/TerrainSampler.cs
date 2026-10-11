// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/{LandblockStruct.cs (ConstructVertices,
// ConstructPolygons, AddPolygon), LandCell.cs (find_terrain_poly)}, Source/ACE.Server/Physics/Polygon.cs (make_plane,
// point_in_poly2D) and Source/ACE.Server/Physics/Extensions/PlaneExtensions.cs (set_height) (AGPL-3.0)
// with changes: arithmetic re-expressed to match the retail x87 code (double registers, float stores) instead of
// ACE's System.Numerics float math; the split comes from Lifestone.Data.Terrain.TerrainSplit.

using System.Numerics;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World.Scenery;

/// <summary>A terrain triangle's plane: <c>dot(Normal, p) + D = 0</c>, unit normal pointing up.</summary>
/// <param name="Normal">Unit normal (float, as retail stores it).</param>
/// <param name="D">Plane distance (float).</param>
public readonly record struct TerrainPlane(Vector3 Normal, float D);

/// <summary>
/// Retail landscape height sampling for one landblock: the 81 vertices and 128 triangles that
/// <c>CLandBlockStruct::ConstructVertices/ConstructPolygons</c> build, their planes (<c>CPolygon::make_plane</c>), the
/// per-cell triangle lookup (<c>CLandCell::find_terrain_poly</c> / <c>CPolygon::point_in_poly2D</c>) and the exact
/// plane height (<c>Plane::set_height</c>).
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are landblock-local AC units (X east, Y north, Z up, origin at the south-west corner). Polygon
/// <c>2 * (cellX * 8 + cellY) + t</c> is triangle t of cell (cellX, cellY), with the vertex order of
/// <c>CLandBlockStruct::ConstructPolygons</c>, which is the same order as <see cref="TerrainMeshBuilder"/>:
/// SW-NE cut: (SW, SE, NE), (SW, NE, NW); otherwise (SW, SE, NW), (NE, NW, SE).
/// </para>
/// <para>
/// Arithmetic follows the retail x87 code (11.6096: CPolygon::make_plane 0x539110, point_in_poly2D 0x538DA0,
/// Plane::set_height 0x52FD90): double intermediates, rounded to float where retail stores them. For terrain
/// triangles the plane is therefore bit-identical to retail, and the inside test is exact for every float point at
/// least 2^-18 from the block's west and south edges (closer, it rounds as retail does; see
/// <c>IsStrictlyLeftOfEdge</c>).
/// </para>
/// <para>Thread safety: immutable after construction.</para>
/// </remarks>
public sealed class TerrainSampler
{
    /// <summary>Retail <c>Plane::set_height</c> leaves z unchanged when |N.z| is at most this (float 0.0002).</summary>
    public const float VerticalNormalEpsilon = 0.0002f;

    private readonly Vector3[] _vertices;
    private readonly int[] _polygonVertices;
    private readonly TerrainPlane[] _planes;

    /// <summary>Builds the sampler from a landblock's heights and the Region's LandHeightTable.</summary>
    public TerrainSampler(LandblockTerrain terrain, IReadOnlyList<float> landHeightTable)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(landHeightTable);
        if (landHeightTable.Count != 256)
        {
            throw new ArgumentException("LandHeightTable must have 256 entries.", nameof(landHeightTable));
        }

        Coord = terrain.Coord;

        // CLandBlockStruct::ConstructVertices: (ix * 24, iy * 24, LandHeightTable[height]), poly_size = 192 / 8 = 24.
        _vertices = new Vector3[TerrainConstants.VertexCount];
        for (var x = 0; x < TerrainConstants.VerticesPerSide; x++)
        {
            for (var y = 0; y < TerrainConstants.VerticesPerSide; y++)
            {
                _vertices[TerrainConstants.VertexIndex(x, y)] = new Vector3(
                    x * TerrainConstants.CellSize,
                    y * TerrainConstants.CellSize,
                    landHeightTable[terrain.HeightIndex(x, y)]);
            }
        }

        // CLandBlockStruct::ConstructPolygons + AddPolygon.
        _polygonVertices = new int[TerrainConstants.TriangleCount * 3];
        _planes = new TerrainPlane[TerrainConstants.TriangleCount];
        for (var cx = 0; cx < TerrainConstants.CellsPerSide; cx++)
        {
            for (var cy = 0; cy < TerrainConstants.CellsPerSide; cy++)
            {
                var sw = TerrainConstants.VertexIndex(cx, cy);
                var se = TerrainConstants.VertexIndex(cx + 1, cy);
                var ne = TerrainConstants.VertexIndex(cx + 1, cy + 1);
                var nw = TerrainConstants.VertexIndex(cx, cy + 1);
                var p = 2 * ((cx * TerrainConstants.CellsPerSide) + cy);
                if (TerrainSplit.IsSWtoNE(terrain.Coord, cx, cy))
                {
                    AddPolygon(p, sw, se, ne);
                    AddPolygon(p + 1, sw, ne, nw);
                }
                else
                {
                    AddPolygon(p, sw, se, nw);
                    AddPolygon(p + 1, ne, nw, se);
                }
            }
        }
    }

    public LandblockCoord Coord { get; }

    /// <summary>The 81 vertices, index <c>x * 9 + y</c>.</summary>
    public IReadOnlyList<Vector3> Vertices => _vertices;

    /// <summary>384 vertex indices, three per polygon, in retail polygon order (see remarks).</summary>
    public IReadOnlyList<int> PolygonVertexIndices => _polygonVertices;

    /// <summary>The 128 polygon planes.</summary>
    public IReadOnlyList<TerrainPlane> Planes => _planes;

    /// <summary>
    /// Index (0..7) of the cell that contains <paramref name="value"/> along one axis, or -1 outside the landblock
    /// (retail <c>LandDefs::get_outside_lcoord</c>: <c>floor(v / 24.0)</c> in double).
    /// </summary>
    public static int CellIndexOf(float value)
    {
        var index = Math.Floor(value / (double)TerrainConstants.CellSize);
        return index is >= 0 and < TerrainConstants.CellsPerSide ? (int)index : -1;
    }

    /// <summary>
    /// Retail <c>CLandCell::find_terrain_poly</c>: the first of the cell's two triangles whose 2D footprint contains
    /// (x, y) (edges inclusive), or -1 when the point is outside the landblock.
    /// </summary>
    public int FindTerrainPolygon(float x, float y)
    {
        var cx = CellIndexOf(x);
        var cy = CellIndexOf(y);
        return cx < 0 || cy < 0 ? -1 : FindTerrainPolygon(cx, cy, x, y);
    }

    /// <summary>As <see cref="FindTerrainPolygon(float, float)"/> but searching the given cell (retail uses the cell
    /// from <c>Position::get_outside_cell_id</c>).</summary>
    public int FindTerrainPolygon(int cellX, int cellY, float x, float y)
    {
        var first = 2 * ((cellX * TerrainConstants.CellsPerSide) + cellY);
        for (var p = first; p < first + 2; p++)
        {
            if (PointInPolygon2D(p, x, y))
            {
                return p;
            }
        }

        return -1;
    }

    /// <summary>
    /// Retail <c>Plane::set_height</c>: the z of the plane at (x, y), <c>-(y*N.y + x*N.x + d) / N.z</c> in double then
    /// rounded to float. Returns false (z = <paramref name="fallbackZ"/>) when |N.z| &lt;= 0.0002.
    /// </summary>
    public static bool TrySetHeight(in TerrainPlane plane, float x, float y, float fallbackZ, out float z)
    {
        var n = plane.Normal;
        if (Math.Abs(n.Z) <= VerticalNormalEpsilon)
        {
            z = fallbackZ;
            return false;
        }

        z = (float)-(((((double)y * n.Y) + ((double)x * n.X)) + plane.D) / n.Z);
        return true;
    }

    /// <summary>
    /// Terrain height at landblock-local (x, y), 0 &lt;= x, y &lt; 192: retail triangle lookup + exact plane height.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The point is outside the landblock.</exception>
    public float GetHeight(float x, float y)
    {
        var polygon = FindTerrainPolygon(x, y);
        if (polygon < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside landblock {Coord}.");
        }

        TrySetHeight(_planes[polygon], x, y, float.NaN, out var z);
        return z;
    }

    /// <summary>The upward unit normal of the triangle under landblock-local (x, y).</summary>
    public Vector3 GetNormal(float x, float y)
    {
        var polygon = FindTerrainPolygon(x, y);
        return polygon < 0
            ? throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside landblock {Coord}.")
            : _planes[polygon].Normal;
    }

    /// <summary>
    /// Retail <c>CPolygon::point_in_poly2D(point, side = 0)</c>: the point is inside unless it lies strictly left of one
    /// of the edges. Retail walks the edges backwards, v0 -> v[n-1] -> ... -> v1 -> v0, which is clockwise seen from
    /// above for AC's counter-clockwise triangles, so the interior is on the right of every edge and points on an edge
    /// count as inside.
    /// </summary>
    internal bool PointInPolygon2D(int polygon, float px, float py)
    {
        var corners = _polygonVertices.AsSpan(polygon * 3, 3);
        var from = _vertices[corners[0]];
        for (var i = corners.Length - 1; i >= 0; i--)
        {
            var to = _vertices[corners[i]];
            if (IsStrictlyLeftOfEdge(from, to, px, py))
            {
                return false;
            }

            from = to;
        }

        return true;
    }

    private void AddPolygon(int polygon, int v0, int v1, int v2)
    {
        _polygonVertices[(polygon * 3) + 0] = v0;
        _polygonVertices[(polygon * 3) + 1] = v1;
        _polygonVertices[(polygon * 3) + 2] = v2;
        _planes[polygon] = MakePlane(_vertices[v0], _vertices[v1], _vertices[v2]);
    }

    /// <summary>
    /// Retail <c>CPolygon::make_plane</c> for a triangle: N = cross(v1 - v0, v2 - v0) scaled to unit length, and
    /// d = -(dot(N, v0) + dot(N, v1) + dot(N, v2)) / 3.
    /// </summary>
    /// <remarks>
    /// Retail evaluates this in x87 double precision (11.6096 0x539110), but some intermediates live in 4-byte stack
    /// slots and so are rounded to float. We round at the same places: the x and y of the first edge (v1 - v0); the
    /// x and y of the cross product, and its z (retail sums the normal over the polygon's triangle fan starting from
    /// zero, keeping the x and y sums in registers and the z sum in a float); the reciprocal length where it scales y
    /// and z (x is scaled by the double reciprocal); and the stored N and d. The squared length adds the z, y and x
    /// squares in that order, and each vertex's dot product adds its z, y and x terms before joining the running total.
    /// </remarks>
    internal static TerrainPlane MakePlane(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var firstEdge = new Vector3D(Float(v1.X - (double)v0.X), Float(v1.Y - (double)v0.Y), v1.Z - (double)v0.Z);
        var secondEdge = new Vector3D(v2.X - (double)v0.X, v2.Y - (double)v0.Y, v2.Z - (double)v0.Z);
        var cross = Vector3D.Cross(firstEdge, secondEdge);

        // The fan of a triangle has one step, added to the zero-initialised accumulators.
        var fanSum = new Vector3D(0.0 + Float(cross.X), 0.0 + Float(cross.Y), Float(0.0 + cross.Z));

        var reciprocal = 1.0 / Math.Sqrt((fanSum.Z * fanSum.Z) + (fanSum.Y * fanSum.Y) + (fanSum.X * fanSum.X));
        var reciprocalAsFloat = Float(reciprocal);
        var normal = new Vector3(
            (float)(reciprocal * fanSum.X),
            (float)(reciprocalAsFloat * fanSum.Y),
            (float)(reciprocalAsFloat * fanSum.Z));

        var total = 0.0;
        foreach (var v in (ReadOnlySpan<Vector3>)[v0, v1, v2])
        {
            total += ((v.Z * (double)normal.Z) + (v.Y * (double)normal.Y)) + (v.X * (double)normal.X);
        }

        return new TerrainPlane(normal, (float)-(total / 3.0));
    }

    /// <summary>
    /// True when (px, py) lies strictly left of the directed edge <paramref name="from"/> -> <paramref name="to"/>: the
    /// edge's line function <c>L(p) = n . (p - to)</c>, with <c>n = (from.Y - to.Y, to.X - from.X)</c> the edge's left
    /// normal, is positive.
    /// </summary>
    /// <remarks>
    /// Retail evaluates L in x87 double as a line equation: the constant <c>c = -(n . to)</c> first, then
    /// <c>c + n.y * py</c>, then <c>+ n.x * px</c> (11.6096 0x538DC2-0x538DE6). For terrain edges (corners at
    /// multiples of 24) every product and sum is exact, so the order cannot matter, unless px or py is non-zero and
    /// below 2^-18, i.e. within 4 microns of the landblock's west or south edge. There the sums round, and for points
    /// within about 1e-13 of a diagonal the rounding decides the side, so we keep retail's order (see
    /// <c>TerrainSamplerTests.Retail_evaluation_order_decides_a_point_a_femtometre_off_a_diagonal</c>).
    /// </remarks>
    private static bool IsStrictlyLeftOfEdge(Vector3 from, Vector3 to, float px, float py)
    {
        var normalX = (double)from.Y - to.Y;
        var normalY = (double)to.X - from.X;
        var lineConstant = -((normalX * to.X) + (normalY * to.Y));
        var partial = lineConstant + (normalY * py);
        return partial + (normalX * px) > 0.0;
    }

    /// <summary>A value rounded to float, as when the x87 code stores it to a 4-byte slot.</summary>
    private static double Float(double value) => (float)value;

    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public static Vector3D Cross(Vector3D a, Vector3D b) =>
            new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
    }
}
