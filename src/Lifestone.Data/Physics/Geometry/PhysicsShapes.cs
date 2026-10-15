using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Physics.Bsp;
using Lifestone.Data.Physics.Primitives;
using DatCellStruct = DatReaderWriter.Types.CellStruct;
using DatGfxObj = DatReaderWriter.DBObjs.GfxObj;
using DatPolygon = DatReaderWriter.Types.Polygon;
using DatVertexArray = DatReaderWriter.Types.VertexArray;

namespace Lifestone.Data.Physics.Geometry;

/// <summary>
/// The collision shape of a GfxObj (retail <c>CGfxObj</c>'s physics half, unpacked by <c>CGfxObj::Serialize</c>, 11.4186
/// 0x00534970, 11.6096 0x005356B0): its physics polygons and the physics BSP over them. Only GfxObjs with the HasPhysics
/// flag (1) have one.
/// </summary>
public sealed class GfxObjPhysics
{
    private GfxObjPhysics(uint id, PhysPolygon[] polygons, PhysicsBspTree bsp)
    {
        Id = id;
        Polygons = polygons;
        Bsp = bsp;
    }

    /// <summary>The GfxObj id.</summary>
    public uint Id { get; }

    /// <summary>The physics polygons, by position (each polygon's stored id equals its position).</summary>
    public IReadOnlyList<PhysPolygon> Polygons { get; }

    /// <summary>The physics BSP.</summary>
    public PhysicsBspTree Bsp { get; }

    /// <summary>
    /// The GfxObj's collision shape, or null when it has no HasPhysics flag (the GfxObj only draws).
    /// </summary>
    /// <exception cref="InvalidDataException">The physics polygons or the BSP are malformed.</exception>
    public static GfxObjPhysics? FromDat(uint id, DatGfxObj gfxObj)
    {
        ArgumentNullException.ThrowIfNull(gfxObj);
        if (!gfxObj.Flags.HasFlag(GfxObjFlags.HasPhysics))
        {
            return null;
        }

        var owner = $"GfxObj 0x{id:X8}";
        var polygons = PhysicsPolygons.FromDat(gfxObj.PhysicsPolygons, gfxObj.VertexArray, owner);
        return new GfxObjPhysics(id, polygons, PhysicsBspTree.FromDat(gfxObj.PhysicsBSP, polygons, owner));
    }

    /// <summary>A shape from parts (synthetic shapes in tests; real ones come from the dat).</summary>
    internal static GfxObjPhysics FromParts(uint id, PhysPolygon[] polygons, PhysicsBspTree bsp) => new(id, polygons, bsp);

    /// <inheritdoc/>
    public override string ToString() => $"GfxObjPhysics 0x{Id:X8} ({Polygons.Count} polygons)";
}

/// <summary>
/// What collision needs of any GfxObj, drawing-only ones included: the bounds of its vertices (retail
/// <c>CGfxObj::gfx_bound_box</c>, which <c>CPhysicsPart::GetBoundingBox</c> returns and static objects find their cells
/// with), its physics and drawing spheres, and its physics shape when it has one.
/// </summary>
public sealed class GfxObjShape
{
    internal GfxObjShape(uint id, BBox bounds, PhysSphere? physicsSphere, PhysSphere? drawingSphere, GfxObjPhysics? physics)
    {
        Id = id;
        Bounds = bounds;
        PhysicsSphere = physicsSphere;
        DrawingSphere = drawingSphere;
        Physics = physics;
    }

    /// <summary>The GfxObj id.</summary>
    public uint Id { get; }

    /// <summary>
    /// Retail <c>CGfxObj::gfx_bound_box</c>: the bounds of every vertex of the vertex array (<c>CGfxObj::init_end</c>);
    /// an empty box (minimum at <see cref="float.MaxValue"/>) without vertices.
    /// </summary>
    public BBox Bounds { get; }

    /// <summary>Retail <c>CGfxObj::physics_sphere</c>: the physics BSP's root sphere, or null without physics.</summary>
    public PhysSphere? PhysicsSphere { get; }

    /// <summary>
    /// Retail <c>CGfxObj::drawing_sphere</c>: the drawing BSP's root sphere, or null without a drawing BSP or when its root
    /// is a leaf (a drawing leaf stores no sphere).
    /// </summary>
    public PhysSphere? DrawingSphere { get; }

    /// <summary>The physics shape, or null when the GfxObj only draws.</summary>
    public GfxObjPhysics? Physics { get; }

    /// <summary>
    /// The sphere retail's static-object cell search uses for a part (<c>CEnvCell::find_transit_cells</c>,
    /// <c>CEnvCell::check_building_transit</c>, <c>CLandCell::add_all_outside_cells</c>): the physics sphere, else the
    /// drawing sphere, else none.
    /// </summary>
    public PhysSphere? CellSearchSphere => PhysicsSphere ?? DrawingSphere;

    /// <summary>The shape of a decoded GfxObj.</summary>
    /// <exception cref="InvalidDataException">The physics polygons or the BSP are malformed.</exception>
    public static GfxObjShape FromDat(uint id, DatGfxObj gfxObj)
    {
        ArgumentNullException.ThrowIfNull(gfxObj);
        var bounds = BBox.ForAdjustment;
        if (gfxObj.VertexArray is not null)
        {
            foreach (var vertex in gfxObj.VertexArray.Vertices.Values)
            {
                bounds.AdjustBBox(vertex.Origin);
            }
        }

        var physics = GfxObjPhysics.FromDat(id, gfxObj);
        PhysSphere? drawingSphere = null;
        if (gfxObj.Flags.HasFlag(GfxObjFlags.HasDrawing)
            && gfxObj.DrawingBSP?.Root is { Type: not BSPNodeType.Leaf, BoundingSphere: { } sphere })
        {
            drawingSphere = new PhysSphere(sphere.Origin, sphere.Radius);
        }

        return new GfxObjShape(id, bounds, physics?.Bsp.Sphere, drawingSphere, physics);
    }

    /// <summary>A shape from parts (synthetic shapes in tests).</summary>
    internal static GfxObjShape FromParts(uint id, BBox bounds, GfxObjPhysics? physics, PhysSphere? drawingSphere = null) =>
        new(id, bounds, physics?.Bsp.Sphere, drawingSphere, physics);

    /// <inheritdoc/>
    public override string ToString() => $"GfxObjShape 0x{Id:X8}{(Physics is null ? " (draw only)" : string.Empty)}";
}

/// <summary>
/// The collision shape of a room shape (retail <c>CCellStruct</c>'s physics polygons and physics BSP, unpacked by
/// <c>CCellStruct::UnPack</c>, 11.4186 0x00533D00, 11.6096 0x00534A40): what an EnvCell collides with.
/// </summary>
public sealed class CellStructPhysics
{
    private CellStructPhysics(uint environmentId, ushort index, PhysPolygon[] polygons, PhysicsBspTree bsp)
    {
        EnvironmentId = environmentId;
        Index = index;
        Polygons = polygons;
        Bsp = bsp;
    }

    /// <summary>The Environment (0x0D) the struct belongs to.</summary>
    public uint EnvironmentId { get; }

    /// <summary>The struct's position in the Environment (retail <c>CEnvironment::get_cellstruct</c>).</summary>
    public ushort Index { get; }

    /// <summary>The physics polygons, by position.</summary>
    public IReadOnlyList<PhysPolygon> Polygons { get; }

    /// <summary>The physics BSP.</summary>
    public PhysicsBspTree Bsp { get; }

    /// <summary>The struct's collision shape.</summary>
    /// <exception cref="InvalidDataException">The physics polygons or the BSP are malformed.</exception>
    public static CellStructPhysics FromDat(uint environmentId, ushort index, DatCellStruct cellStruct)
    {
        ArgumentNullException.ThrowIfNull(cellStruct);
        var owner = $"CellStruct 0x{environmentId:X8}[{index}]";
        var polygons = PhysicsPolygons.FromDat(cellStruct.PhysicsPolygons, cellStruct.VertexArray, owner);
        return new CellStructPhysics(environmentId, index, polygons, PhysicsBspTree.FromDat(cellStruct.PhysicsBSP, polygons, owner));
    }

    /// <summary>A shape from parts (synthetic rooms in tests; real ones come from the dat).</summary>
    internal static CellStructPhysics FromParts(uint environmentId, ushort index, PhysPolygon[] polygons, PhysicsBspTree bsp) =>
        new(environmentId, index, polygons, bsp);

    /// <inheritdoc/>
    public override string ToString() => $"CellStructPhysics 0x{EnvironmentId:X8}[{Index}] ({Polygons.Count} polygons)";
}

/// <summary>Physics polygons from DatReaderWriter, with retail's array semantics checked.</summary>
internal static class PhysicsPolygons
{
    /// <summary>
    /// The polygons in stored order. Retail stores polygons and vertices in arrays and resolves the stored ids as
    /// positions (<c>CPolygon::UnPack</c> indexes <c>pack_verts</c> by vertex id; <c>BSPLEAF::UnPackLeaf</c> indexes
    /// <c>pack_poly</c> by polygon index), while DatReaderWriter keys both by the stored ids; a record where they differ is
    /// refused rather than guessed (the same rule the cells track applies to drawing polygons).
    /// </summary>
    /// <exception cref="InvalidDataException">A polygon or vertex id differs from its position, or a polygon names a
    /// vertex that does not exist.</exception>
    public static PhysPolygon[] FromDat(Dictionary<ushort, DatPolygon> source, DatVertexArray? vertexArray, string owner)
    {
        var vertices = new Vector3[vertexArray?.Vertices.Count ?? 0];
        var expected = 0;
        if (vertexArray is not null)
        {
            foreach (var (key, vertex) in vertexArray.Vertices)
            {
                RequirePosition(owner, "vertex", key, expected);
                vertices[expected++] = vertex.Origin;
            }
        }

        var result = new PhysPolygon[source.Count];
        expected = 0;
        Span<Vector3> corners = stackalloc Vector3[256];
        foreach (var (key, polygon) in source)
        {
            RequirePosition(owner, "physics polygon", key, expected);
            var count = polygon.VertexIds.Count;
            for (var i = 0; i < count; i++)
            {
                var vertexId = (ushort)polygon.VertexIds[i];
                if (vertexId >= vertices.Length)
                {
                    throw new InvalidDataException($"{owner}: physics polygon {key} uses vertex {vertexId} of {vertices.Length}.");
                }

                corners[i] = vertices[vertexId];
            }

            result[expected] = new PhysPolygon(key, corners[..count], (int)polygon.SidesType, (byte)polygon.Stippling, polygon.PosSurface, polygon.NegSurface);
            expected++;
        }

        return result;
    }

    private static void RequirePosition(string owner, string what, int key, int position)
    {
        if (key != position)
        {
            throw new InvalidDataException($"{owner}: {what} stored with id {key} at position {position}.");
        }
    }
}
