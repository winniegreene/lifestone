using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// The doorway polygons of a building, in landblock space: how a viewer outdoors looks into EnvCells.
/// </summary>
/// <remarks>
/// <para>
/// Retail draws a building (<c>RenderDeviceD3D::DrawBuilding</c>), then walks the building GfxObj's drawing BSP for
/// portal nodes (<c>BSPTREE::build_draw_portals_only</c>). Each PORT node lists <c>CPortalPoly {polygon index,
/// portal_index}</c> (<c>BSPPORTAL::UnPackPortal</c>: u16 polygon index into the GfxObj's polygon array, then u16
/// portal_index). For each one <c>PView::DrawPortal</c> takes <c>CBldPortal = building.portals[portal_index]</c>
/// (the LandBlockInfo's <see cref="BuildingData.Portals"/>), and <c>PView::ConstructView(CBldPortal*, ...)</c> looks in
/// when the viewer is on the outside of the polygon: negative side when <c>portal_side</c>, positive side otherwise
/// (strictly: a viewer within <see cref="CellPlane.SideEpsilon"/> of the plane sees nothing). The view, clipped to the
/// doorway, then enters <see cref="BuildingPortalData.OtherCellId"/> through portal
/// <see cref="BuildingPortalData.OtherPortalIndex"/> and the indoor walk runs over the doorway's stab list.
/// </para>
/// <para>
/// Verified on the user's dats: every one of the 16,937 building portals has exactly one portal polygon with its index
/// in the building's drawing BSP, and that polygon coincides (within 0.1 mm) with the entry cell's outside portal
/// polygon at <see cref="BuildingPortalData.OtherPortalIndex"/>.
/// </para>
/// </remarks>
public sealed class BuildingPortalGeometry
{
    private BuildingPortalGeometry(BuildingData building, IReadOnlyList<BuildingDoorway> doorways)
    {
        Building = building;
        Doorways = doorways;
    }

    public BuildingData Building { get; }

    /// <summary>One entry per building portal, in <see cref="BuildingData.Portals"/> order.</summary>
    public IReadOnlyList<BuildingDoorway> Doorways { get; }

    /// <summary>Reads the building GfxObj and pairs its drawing-BSP portal polygons with the building's portals.</summary>
    /// <exception cref="InvalidDataException">The GfxObj is missing or a portal index has no polygon.</exception>
    public static BuildingPortalGeometry Load(DatSet dats, BuildingData building)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(building);
        if (building.Portals.Count == 0)
        {
            return new BuildingPortalGeometry(building, []);
        }

        var bytes = CellRecordReader.ReadPortalBytes(dats, building.ModelId)
            ?? throw new InvalidDataException($"Building GfxObj 0x{building.ModelId:X8} not found.");
        var gfx = CellRecordReader.UnpackExact<GfxObj>(bytes, building.ModelId);
        var polygons = gfx.Polygons.Values.ToArray();
        var positions = gfx.VertexArray.Vertices.Values.Select(v => v.Origin).ToArray();
        RequireKeysArePositions(building.ModelId, gfx);

        var refs = new List<PortalRef>();
        CollectPortalRefs(gfx.DrawingBSP?.Root, refs);

        var doorways = new BuildingDoorway[building.Portals.Count];
        for (var i = 0; i < doorways.Length; i++)
        {
            var matches = refs.Where(r => r.PortalIndex == i).ToList();
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Building GfxObj 0x{building.ModelId:X8}: portal {i} has {matches.Count} portal polygons in its drawing BSP.");
            }

            var polygonIndex = matches[0].PolyId;
            if (polygonIndex >= polygons.Length)
            {
                throw new InvalidDataException($"Building GfxObj 0x{building.ModelId:X8}: portal polygon {polygonIndex} out of range.");
            }

            var local = polygons[polygonIndex].VertexIds.Select(v => positions[v]).ToArray();
            var vertices = local.Select(building.Frame.LocalToParent).ToArray();
            doorways[i] = new BuildingDoorway(i, polygonIndex, building.Portals[i], vertices, CellPlane.FromPolygon(vertices));
        }

        return new BuildingPortalGeometry(building, doorways);
    }

    private static void CollectPortalRefs(DrawingBSPNode? node, List<PortalRef> refs)
    {
        // Iterative walk: building BSPs can be deep.
        var stack = new Stack<DrawingBSPNode>();
        if (node is not null)
        {
            stack.Push(node);
        }

        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Type == BSPNodeType.Portal)
            {
                refs.AddRange(n.Portals);
            }

            if (n.NegNode is not null)
            {
                stack.Push(n.NegNode);
            }

            if (n.PosNode is not null)
            {
                stack.Push(n.PosNode);
            }
        }
    }

    private static void RequireKeysArePositions(uint modelId, GfxObj gfx)
    {
        var i = 0;
        foreach (var key in gfx.Polygons.Keys)
        {
            if (key != i++)
            {
                throw new InvalidDataException($"GfxObj 0x{modelId:X8}: polygon id {key} is not at its array position.");
            }
        }

        i = 0;
        foreach (var key in gfx.VertexArray.Vertices.Keys)
        {
            if (key != i++)
            {
                throw new InvalidDataException($"GfxObj 0x{modelId:X8}: vertex id {key} is not at its array position.");
            }
        }
    }
}

/// <summary>A building doorway polygon in landblock space.</summary>
/// <param name="PortalIndex">Index into <see cref="BuildingData.Portals"/>.</param>
/// <param name="PolygonIndex">Polygon index in the building GfxObj.</param>
/// <param name="Portal">The LandBlockInfo portal record.</param>
/// <param name="Vertices">Polygon corners in landblock space.</param>
/// <param name="Plane">Plane of the polygon in landblock space (<c>CPolygon::make_plane</c>).</param>
public sealed record BuildingDoorway(int PortalIndex, int PolygonIndex, BuildingPortalData Portal, Vector3[] Vertices, CellPlane Plane)
{
    /// <summary>
    /// Retail's outside test (<c>PView::ConstructView(CBldPortal*, ...)</c>): the viewer can look in through this doorway
    /// only from the negative side when <see cref="BuildingPortalData.PortalSide"/>, else from the positive side.
    /// </summary>
    public bool CanLookIn(Vector3 viewerLandblockLocal)
    {
        var side = Plane.SideOf(viewerLandblockLocal);
        return Portal.PortalSide ? side == PlaneSide.Negative : side == PlaneSide.Positive;
    }
}
