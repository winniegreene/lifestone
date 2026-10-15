// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/EnvCell.cs (FindCollisions,
// FindEnvCollisions, point_in_cell, find_transit_cells, check_building_transit) (AGPL-3.0)
// with changes: built from the cells track's EnvCellData and CellStructure and PH2's physics BSP; portal planes are
// retail's make_plane of the portal polygon; checked against the retail functions cited below (a cell without portals
// contains no point, as in retail).

using System.Numerics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Physics.World;

/// <summary>A portal of a <see cref="PhysEnvCell"/> as collision uses it (retail <c>CCellPortal</c>).</summary>
/// <param name="OtherCellId">The neighbour's full id, or <see cref="CellIds.Outside"/>.</param>
/// <param name="OtherPortalIndex">The matching portal's index in the neighbour (retail <c>other_portal_id</c>).</param>
/// <param name="PortalSide">Retail <c>portal_side</c>: true when the cell lies on the negative side of <paramref name="Plane"/>.</param>
/// <param name="Plane">The portal polygon's plane in the cell's frame (retail <c>CPolygon::make_plane</c>).</param>
public readonly record struct PhysCellPortal(uint OtherCellId, int OtherPortalIndex, bool PortalSide, Plane Plane)
{
    /// <summary>True for a portal onto the landscape.</summary>
    public bool LeadsOutside => OtherCellId == CellIds.Outside;
}

/// <summary>
/// An EnvCell (retail <c>CEnvCell</c>): a room of a building or dungeon, with its shape's physics BSP, its cell BSP and
/// portals, its visible cells, and the static objects that reach into it.
/// </summary>
public sealed class PhysEnvCell : PhysCell
{
    private readonly PhysCellPortal[] _portals;
    private readonly uint[] _visibleCells;

    internal PhysEnvCell(uint id, CellFrame position, CellBsp cellBsp, CellStructPhysics? physics, PhysCellPortal[] portals, uint[] visibleCells, bool seenOutside)
        : base(id, position)
    {
        CellBsp = cellBsp;
        Physics = physics;
        _portals = portals;
        _visibleCells = visibleCells;
        SeenOutside = seenOutside;
    }

    /// <summary>The containment tree of the cell's shape (cell-local).</summary>
    public CellBsp CellBsp { get; }

    /// <summary>The shape's physics polygons and BSP (cell-local), or null for a shape without one.</summary>
    public CellStructPhysics? Physics { get; }

    /// <summary>The portals in stored order.</summary>
    public IReadOnlyList<PhysCellPortal> Portals => _portals;

    /// <summary>The cells visible from this one (retail <c>stab_list</c>).</summary>
    public IReadOnlyList<uint> VisibleCellIds => _visibleCells;

    /// <summary>The landscape is visible from this cell (retail <c>seen_outside</c>).</summary>
    public bool SeenOutside { get; }

    /// <summary>True when <paramref name="cellId"/> is in the cell's visible list.</summary>
    public bool IsVisible(uint cellId)
    {
        foreach (var id in _visibleCells)
        {
            if (id == cellId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CEnvCell::point_in_cell</c> (11.4186 0x0052C300, 11.6096 0x0052CF10): the point, in the cell's frame,
    /// inside the cell BSP (<c>CCellStruct::point_in_cell</c>); never for a cell without portals.
    /// </summary>
    public override bool PointInCell(Vector3 point) =>
        _portals.Length != 0 && CellBsp.Contains(Position.Frame.GlobalToLocal(point));

    /// <summary>
    /// Retail <c>CEnvCell::find_visible_child_cell</c> (11.4186 0x0052DC50, 11.6096 0x0052E8B0): this cell when it contains
    /// <paramref name="origin"/>, else (with <paramref name="searchCells"/>) the first visible cell that does, or else the
    /// first portal neighbour that does.
    /// </summary>
    internal PhysEnvCell? FindVisibleChildCell(IPhysicsCellSource cells, Vector3 origin, bool searchCells)
    {
        if (PointInCell(origin))
        {
            return this;
        }

        if (searchCells)
        {
            foreach (var id in _visibleCells)
            {
                if (cells.GetCell(id) is PhysEnvCell visible && visible.PointInCell(origin))
                {
                    return visible;
                }
            }

            return null;
        }

        foreach (var portal in _portals)
        {
            if (cells.GetCell(portal.OtherCellId) is PhysEnvCell other && other.PointInCell(origin))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// Retail <c>CEnvCell::find_collisions</c> (11.4186 0x0052C100, 11.6096 0x0052CD10): the room's shape, then the static
    /// objects.
    /// </summary>
    internal override TransitionState FindCollisions(Transition transition)
    {
        var state = FindEnvCollisions(transition);
        return state == TransitionState.OK ? FindObjCollisions(transition) : state;
    }

    /// <summary>
    /// Retail <c>CEnvCell::find_env_collisions</c> (11.4186 0x0052C130, 11.6096 0x0052CD40): the transition's spheres,
    /// cached in the cell's frame, against the shape's physics BSP (a placement insert for an initial placement). A cell
    /// that stops an object without contact is an environment collision.
    /// </summary>
    internal override TransitionState FindEnvCollisions(Transition transition)
    {
        var state = CheckEntryRestrictions(transition);
        if (state != TransitionState.OK)
        {
            return state;
        }

        var path = transition.SpherePath;
        path.ObstructionEthereal = false;
        if (Physics is null)
        {
            return state;
        }

        path.CacheLocalSpaceSphere(Position, 1.0f);
        state = path.InsertType == InsertType.InitialPlacement
            ? Physics.Bsp.PlacementInsert(transition)
            : Physics.Bsp.FindCollisions(transition, 1.0f);
        if (state != TransitionState.OK && (transition.ObjectInfo.State & ObjectInfoState.Contact) == 0)
        {
            transition.CollisionInfo.CollidedWithEnvironment = true;
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CEnvCell::find_transit_cells(p, num_sphere, sphere, cell_array, path)</c> (11.4186 0x0052C820, 11.6096
    /// 0x0052D430): through each portal, the neighbour whose cell BSP a sphere reaches into; for a neighbour that is not
    /// attached, its id when a sphere reaches the portal from this cell's side; and the landscape when a sphere straddles
    /// an outside portal (within its radius and the epsilon).
    /// </summary>
    internal override void FindTransitCells(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray, SpherePath? path)
    {
        var checkOutside = false;
        foreach (var portal in _portals)
        {
            if (portal.LeadsOutside)
            {
                foreach (var sphere in spheres)
                {
                    var distance = portal.Plane.DistanceTo(Position.Frame.GlobalToLocal(sphere.Center));
                    var reach = sphere.Radius + PhysicsGlobals.Epsilon;
                    if (distance > -reach && distance < reach)
                    {
                        checkOutside = true;
                        break;
                    }
                }

                continue;
            }

            if (cells.GetCell(portal.OtherCellId) is PhysEnvCell other)
            {
                foreach (var sphere in spheres)
                {
                    var local = other.Position.Frame.GlobalToLocal(sphere.Center);
                    if (other.CellBsp.SphereIntersects(local, sphere.Radius) != CellBspContact.Outside)
                    {
                        cellArray.Add(other.Id, other);
                        break;
                    }
                }

                continue;
            }

            foreach (var sphere in spheres)
            {
                var distance = portal.Plane.DistanceTo(Position.Frame.GlobalToLocal(sphere.Center));
                var reach = sphere.Radius + PhysicsGlobals.Epsilon;
                if ((distance > -reach && portal.PortalSide) || (distance < reach && !portal.PortalSide))
                {
                    cellArray.Add(portal.OtherCellId, null);
                    break;
                }
            }
        }

        if (checkOutside)
        {
            CellLists.AddAllOutsideCells(cells, position, spheres, cellArray);
        }
    }

    /// <summary>
    /// Retail <c>CEnvCell::find_transit_cells(num_parts, parts, cell_array)</c> (11.4186 0x0052CAE0, 11.6096 0x0052D6F0):
    /// through each portal, the first part whose sphere reaches the portal's far side and whose box is not wholly on this
    /// cell's side: the landscape for an outside portal, the neighbour when the part's box reaches into its cell BSP, or
    /// the neighbour's id when it is not attached.
    /// </summary>
    internal override void FindTransitCells(IPhysicsCellSource cells, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        var checkOutside = false;
        foreach (var portal in _portals)
        {
            foreach (var part in parts)
            {
                if (part.Shape.CellSearchSphere is not { } sphere)
                {
                    continue;
                }

                var center = Position.LocalToLocal(part.Position, sphere.Center);
                var reach = sphere.Radius + PhysicsGlobals.Epsilon;
                var distance = portal.Plane.DistanceTo(center);
                if (portal.PortalSide ? distance < -reach : distance > reach)
                {
                    continue;
                }

                var box = BBox.LocalToLocal(part.Shape.Bounds, part.Position, Position);
                var side = portal.Plane.IntersectBox(box);
                if (side == (portal.PortalSide ? Sidedness.Negative : Sidedness.Positive))
                {
                    continue;
                }

                if (portal.LeadsOutside)
                {
                    checkOutside = true;
                    break;
                }

                if (cells.GetCell(portal.OtherCellId) is PhysEnvCell other)
                {
                    var otherBox = BBox.LocalToLocal(part.Shape.Bounds, part.Position, other.Position);
                    if (!other.CellBsp.BoxIntersects(otherBox.Min, otherBox.Max))
                    {
                        continue;
                    }

                    cellArray.Add(other.Id, other);
                    break;
                }

                cellArray.Add(portal.OtherCellId, null);
                break;
            }
        }

        if (checkOutside)
        {
            CellLists.AddAllOutsideCells(cells, parts, cellArray);
        }
    }

    /// <summary>
    /// Retail <c>CEnvCell::check_building_transit(portal_id, p, num_sphere, sphere, cell_array, path)</c> (11.4186
    /// 0x0052C5D0, 11.6096 0x0052D1E0): from a building outside, this cell when a sphere reaches into its cell BSP (the
    /// path then hits an interior cell).
    /// </summary>
    internal void CheckBuildingTransit(int portalId, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray, SpherePath? path)
    {
        if (portalId < 0)
        {
            return;
        }

        foreach (var sphere in spheres)
        {
            var local = Position.Frame.GlobalToLocal(sphere.Center);
            if (CellBsp.SphereIntersects(local, sphere.Radius) != CellBspContact.Outside)
            {
                if (path is not null)
                {
                    path.HitsInteriorCell = true;
                }

                cellArray.Add(Id, this);
                return;
            }
        }
    }

    /// <summary>
    /// Retail <c>CEnvCell::check_building_transit(portal_id, num_parts, parts, cell_array)</c> (11.4186 0x0052C680, 11.6096
    /// 0x0052D290): from a building outside, this cell (and the cells its parts reach from it) when a part's sphere reaches
    /// the portal from outside, its box is not wholly outside, and the box reaches into the cell BSP.
    /// </summary>
    internal void CheckBuildingTransit(IPhysicsCellSource cells, int portalId, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        if (portalId < 0 || portalId >= _portals.Length)
        {
            return;
        }

        var portal = _portals[portalId];
        foreach (var part in parts)
        {
            if (part.Shape.CellSearchSphere is not { } sphere)
            {
                continue;
            }

            var center = Position.LocalToLocal(part.Position, sphere.Center);
            var reach = sphere.Radius + PhysicsGlobals.Epsilon;
            var distance = portal.Plane.DistanceTo(center);
            if (portal.PortalSide ? distance > reach : distance < -reach)
            {
                continue;
            }

            var box = BBox.LocalToLocal(part.Shape.Bounds, part.Position, Position);
            var side = portal.Plane.IntersectBox(box);
            if (side != Sidedness.Crossing && side != (portal.PortalSide ? Sidedness.Negative : Sidedness.Positive))
            {
                continue;
            }

            if (CellBsp.BoxIntersects(box.Min, box.Max))
            {
                cellArray.Add(Id, this);
                FindTransitCells(cells, parts, cellArray);
                return;
            }
        }
    }
}
