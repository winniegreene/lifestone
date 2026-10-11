using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Geometry;
using DatCellStruct = DatReaderWriter.Types.CellStruct;
using DatPolygon = DatReaderWriter.Types.Polygon;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// One room shape: a CellStruct of an Environment (0x0D), in cell-local AC coordinates.
/// </summary>
/// <remarks>
/// Layout per retail <c>CCellStruct::UnPack</c>: u32 num_polygons, num_physics_polygons, num_portals; CVertexArray;
/// drawing polygons; u16 portal polygon indices; cell BSP; physics polygons; physics BSP; u32 has_drawing_bsp;
/// [drawing BSP]. Retail stores polygons, vertices and cell structs in arrays and resolves the stored u16s as array
/// positions; DatReaderWriter keys them by the stored ids instead. The two agree for every end-of-retail Environment
/// (checked when loading: a record where they differ is rejected).
/// </remarks>
public sealed class CellStructure
{
    private readonly Dictionary<ushort, PortalPolygon> _portalsById;

    private CellStructure(
        uint environmentId,
        ushort index,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[][] uvs,
        CellPolygon[] polygons,
        PortalPolygon[] portals,
        CellBsp bsp,
        bool hasDrawingBsp,
        FloorPolygon[] floors,
        int physicsPolygonCount)
    {
        EnvironmentId = environmentId;
        Index = index;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Polygons = polygons;
        Portals = portals;
        _portalsById = portals.ToDictionary(p => p.PolygonId);
        Bsp = bsp;
        HasDrawingBsp = hasDrawingBsp;
        Floors = floors;
        PhysicsPolygonCount = physicsPolygonCount;
        Bounds = positions.Length == 0
            ? BoundingBox.Empty
            : new BoundingBox(positions.Aggregate(Vector3.Min), positions.Aggregate(Vector3.Max));
    }

    /// <summary>Owning Environment (0x0D) id.</summary>
    public uint EnvironmentId { get; }

    /// <summary>Position of this struct in the Environment's array (retail <c>CEnvironment::get_cellstruct</c>).</summary>
    public ushort Index { get; }

    /// <summary>Vertex positions (cell-local), indexed by vertex id.</summary>
    public IReadOnlyList<Vector3> Positions { get; }

    /// <summary>Vertex normals as stored, indexed by vertex id.</summary>
    public IReadOnlyList<Vector3> Normals { get; }

    /// <summary>Per vertex, its UV set (polygons pick one per corner by index).</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Uvs { get; }

    /// <summary>Drawing polygons in stored order (portal polygons included).</summary>
    public IReadOnlyList<CellPolygon> Polygons { get; }

    /// <summary>The struct's portal polygons in stored order (each EnvCell portal names one by polygon id).</summary>
    public IReadOnlyList<PortalPolygon> Portals { get; }

    /// <summary>Point-in-cell test (retail <c>CCellStruct::point_in_cell</c>).</summary>
    public CellBsp Bsp { get; }

    /// <summary>
    /// False for the three end-of-retail structs that have no drawing BSP (0x0D00006B[0], 0x0D00027F[0]: six invisible
    /// portal faces each; 0x0D0002F1[0]: physics only), used by 8,236 cells. Retail <c>PView::DrawCells</c> draws a cell
    /// only when its structure has a drawing BSP (it still draws the cell's objects).
    /// </summary>
    public bool HasDrawingBsp { get; }

    /// <summary>
    /// Walkable physics polygons: normal Z at least <see cref="FloorZ"/> (ACE <c>PhysicsGlobals.FloorZ</c>) in cell-local
    /// space. Used for spawn points; rotate into landblock space before testing slopes there.
    /// </summary>
    public IReadOnlyList<FloorPolygon> Floors { get; }

    public int PhysicsPolygonCount { get; }

    /// <summary>Axis-aligned bounds of all vertices, cell-local.</summary>
    public BoundingBox Bounds { get; }

    /// <summary>ACE <c>Physics/PhysicsGlobals.cs FloorZ</c>: the smallest normal Z of a walkable polygon.</summary>
    public const float FloorZ = 0.66417414618662751f;

    /// <summary>Looks up a portal polygon by polygon id (retail <c>CCellStruct::get_portal</c>).</summary>
    public PortalPolygon? GetPortal(ushort polygonId) => _portalsById.GetValueOrDefault(polygonId);

    public override string ToString() => $"CellStruct 0x{EnvironmentId:X8}[{Index}]";

    /// <summary>Assembles a struct from parts (synthetic unit tests; real data goes through the library).</summary>
    internal static CellStructure FromParts(
        uint environmentId,
        ushort index,
        Vector3[] positions,
        Vector3[] normals,
        Vector2[][] uvs,
        CellPolygon[] polygons,
        ushort[] portalPolygonIds,
        CellBsp bsp,
        bool hasDrawingBsp)
    {
        var portals = portalPolygonIds.Select(id =>
        {
            var vertices = polygons[id].VertexIndices.Select(v => positions[v]).ToArray();
            return new PortalPolygon(id, vertices, CellPlane.FromPolygon(vertices));
        }).ToArray();
        return new CellStructure(environmentId, index, positions, normals, uvs, polygons, portals, bsp, hasDrawingBsp, [], 0);
    }

    internal static CellStructure FromDat(uint environmentId, ushort index, DatCellStruct cs)
    {
        var vertexCount = cs.VertexArray.Vertices.Count;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount][];
        var expected = 0;
        foreach (var (key, vertex) in cs.VertexArray.Vertices)
        {
            RequirePosition(environmentId, index, "vertex", key, expected);
            positions[expected] = vertex.Origin;
            normals[expected] = vertex.Normal;
            uvs[expected] = vertex.UVs.Select(uv => new Vector2(uv.U, uv.V)).ToArray();
            expected++;
        }

        var polygons = ConvertPolygons(environmentId, index, cs.Polygons, vertexCount, "polygon");
        var portals = new PortalPolygon[cs.Portals.Count];
        for (var i = 0; i < portals.Length; i++)
        {
            var polygonIndex = cs.Portals[i];
            if (polygonIndex >= polygons.Length)
            {
                throw new InvalidDataException($"CellStruct 0x{environmentId:X8}[{index}]: portal {i} names polygon {polygonIndex} of {polygons.Length}.");
            }

            var poly = polygons[polygonIndex];
            var vertices = poly.VertexIndices.Select(v => positions[v]).ToArray();
            portals[i] = new PortalPolygon(poly.Id, vertices, CellPlane.FromPolygon(vertices));
        }

        var physics = ConvertPolygons(environmentId, index, cs.PhysicsPolygons, vertexCount, "physics polygon");
        var floors = new List<FloorPolygon>();
        foreach (var poly in physics)
        {
            if (poly.VertexIndices.Length < 3)
            {
                continue;
            }

            var vertices = poly.VertexIndices.Select(v => positions[v]).ToArray();
            if (!CellPlane.TryFromPolygon(vertices, out var plane))
            {
                continue; // zero-area physics polygon: no normal, so it cannot be a floor
            }

            if (plane.Normal.Z >= FloorZ)
            {
                floors.Add(new FloorPolygon(poly.Id, vertices, plane));
            }
        }

        return new CellStructure(
            environmentId,
            index,
            positions,
            normals,
            uvs,
            polygons,
            portals,
            CellBsp.FromDat(cs.CellBSP),
            cs.DrawingBSP?.Root is not null,
            floors.ToArray(),
            physics.Length);
    }

    private static CellPolygon[] ConvertPolygons(uint environmentId, ushort index, Dictionary<ushort, DatPolygon> source, int vertexCount, string what)
    {
        var result = new CellPolygon[source.Count];
        var expected = 0;
        foreach (var (key, p) in source)
        {
            RequirePosition(environmentId, index, what, key, expected);
            var vertexIndices = new ushort[p.VertexIds.Count];
            for (var i = 0; i < vertexIndices.Length; i++)
            {
                var v = p.VertexIds[i];
                if (v < 0 || v >= vertexCount)
                {
                    throw new InvalidDataException($"CellStruct 0x{environmentId:X8}[{index}]: {what} {key} uses vertex {v} of {vertexCount}.");
                }

                vertexIndices[i] = (ushort)v;
            }

            result[expected] = new CellPolygon(
                key,
                vertexIndices,
                p.PosUVIndices.ToArray(),
                p.NegUVIndices.ToArray(),
                p.PosSurface,
                p.NegSurface,
                (byte)p.Stippling,
                (int)p.SidesType);
            expected++;
        }

        return result;
    }

    private static void RequirePosition(uint environmentId, ushort index, string what, int key, int position)
    {
        if (key != position)
        {
            // Retail resolves these as array positions; a record where the stored id differs would need that rule
            // re-checked before we pick either interpretation.
            throw new InvalidDataException(
                $"CellStruct 0x{environmentId:X8}[{index}]: {what} stored with id {key} at position {position}.");
        }
    }
}

/// <summary>
/// A drawing polygon of a CellStruct (retail <c>CPolygon</c>, unpacked by <c>CPolygon::UnPack</c>).
/// </summary>
/// <param name="Id">Polygon id (equals its position).</param>
/// <param name="VertexIndices">Corner vertex ids, counter-clockwise seen from the positive side.</param>
/// <param name="PosUvIndices">Per-corner UV index for the positive side; empty when stippling has NoPos (4).</param>
/// <param name="NegUvIndices">Per-corner UV index for the negative side; only stored for sides type 2 without NoNeg (8).</param>
/// <param name="PosSurface">Index into the EnvCell's surface list for the positive side.</param>
/// <param name="NegSurface">Index into the surface list for the negative side (sides type 2).</param>
/// <param name="Stippling">Raw stippling byte: 1 Positive, 2 Negative, 4 NoPos, 8 NoNeg.</param>
/// <param name="SidesType">Raw cull mode: 0 single-sided, 1 both sides with the positive surface, 2 separate negative side.</param>
public sealed record CellPolygon(
    ushort Id,
    ushort[] VertexIndices,
    byte[] PosUvIndices,
    byte[] NegUvIndices,
    short PosSurface,
    short NegSurface,
    byte Stippling,
    int SidesType)
{
    public bool NoPos => (Stippling & (byte)StipplingType.NoPos) != 0;
}

/// <summary>A portal polygon in cell-local space with its retail plane (<c>CPolygon::make_plane</c>).</summary>
public sealed record PortalPolygon(ushort PolygonId, Vector3[] Vertices, CellPlane Plane);

/// <summary>A walkable physics polygon in cell-local space.</summary>
public sealed record FloorPolygon(ushort PolygonId, Vector3[] Vertices, CellPlane Plane)
{
    /// <summary>Vertex centroid (cell-local).</summary>
    public Vector3 Centroid => Vertices.Aggregate(Vector3.Zero, (s, v) => s + v) / Vertices.Length;

    /// <summary>Area of the polygon (half the length of the summed fan cross products).</summary>
    public float Area
    {
        get
        {
            var sum = Vector3.Zero;
            for (var i = 1; i + 1 < Vertices.Length; i++)
            {
                sum += Vector3.Cross(Vertices[i] - Vertices[0], Vertices[i + 1] - Vertices[0]);
            }

            return sum.Length() * 0.5f;
        }
    }
}
