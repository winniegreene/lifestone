using System.Numerics;

namespace Lifestone.Data.World.Cells;

/// <summary>
/// CPU re-expression of retail's indoor portal visibility (<c>PView</c>, acclient.c:461050-462600), in 3D.
/// </summary>
/// <remarks>
/// <para><b>What retail does.</b> When the viewer's cell is an EnvCell, <c>SmartBox::RenderNormalMode</c> calls
/// <c>RenderDevice::DrawInside</c>, which calls <c>PView::DrawInside</c>:</para>
/// <list type="number">
/// <item><c>add_views(cell.stab_list)</c>: only the viewer cell and its VisibleCells (the precomputed PVS) can receive a
/// view; any other cell is never entered.</item>
/// <item><c>ConstructView</c>: a best-first walk (todo list ordered by the squared distance to the entry portal's
/// farthest vertex, <c>InsCellTodoList</c>). <c>InitCell</c> marks, per portal, whether the viewer is on the cell's
/// interior side of the portal plane (or on it, within 0.0002): only those portals are looked through, and never the
/// portal the walk came in by.</item>
/// <item><c>ClipPortals</c>: each such portal polygon is clipped to the cell's current view in screen space
/// (<c>GetClip</c>); if anything is left the clipped polygon becomes a view of the neighbour. A portal without
/// ExactMatch is clipped again by the neighbour's own portal polygon (<c>OtherPortalClip</c>). A portal whose other
/// side is the outside adds to <c>outside_view</c>. A cell reached through several portals keeps several views.</item>
/// <item><c>DrawCells</c>: if <c>outside_view</c> is not empty the landscape is drawn first (clipped to it), the
/// outside portal polygons are written into depth, then the cells back to front (only those whose struct has a
/// drawing BSP), then each cell's objects culled per view (sphere vs view cone, <c>RenderDeviceD3D::DrawMesh</c>).</item>
/// </list>
/// <para>Looking in from outdoors is the same walk seeded by a building doorway (<see cref="BuildingDoorway"/>):
/// <c>PView::DrawPortal</c> pushes views for the doorway's stab list and enters
/// <see cref="BuildingPortalData.OtherCellId"/> through <see cref="BuildingPortalData.OtherPortalIndex"/>; that view
/// never draws the landscape (the outdoor PView is built with <c>draw_lscape = 0</c>).</para>
/// <para><b>What this provides.</b> Screen-space clipping is replaced by the equivalent 3D clipping: a view is the
/// cone of planes through the eye and the edges of a clipped portal polygon, and a portal is clipped against those
/// planes (Sutherland-Hodgman, <see cref="ViewVolume.ThroughPortal"/>). Results are in landblock-local AC coordinates.
/// Differences from retail, all toward showing more:</para>
/// <list type="bullet">
/// <item>A portal whose plane passes within 0.0002 of the eye passes the current view on unchanged (retail's projected
/// polygon is degenerate there).</item>
/// <item>A stored neighbour-portal index outside the neighbour's list skips the extra non-ExactMatch clip.</item>
/// <item>A cell reached through more than <see cref="PortalWalkLimits.MaxViewsPerCell"/> views is treated as seen
/// through the whole camera frustum.</item>
/// <item>Clipping keeps corners up to 0.1 mm outside a plane and merges corners closer than 0.1 mm, dropping the
/// short edge's plane (<see cref="ViewVolume"/> remarks).</item>
/// </list>
/// <para>Completeness is tested against an independent ray oracle (<c>CellVisibilityExhaustiveTests</c>): rays from
/// the eye are followed through portal polygons and stopped by any other polygon, and every cell a ray enters must be in
/// the result. Over about 139,000 walks with 400 rays each, and as many in random camera frusta, the walk misses
/// nothing. An earlier version rebuilt cone planes from clipped corners, where a near-zero edge gave a plane of rounding
/// noise that cut the next portal away; it lost cells from 48 of those viewpoints.</para>
/// </remarks>
public static class PortalVisibility
{
    /// <summary>
    /// The precomputed potentially-visible set of a viewer cell: the cell itself plus its VisibleCells
    /// (<c>PView::DrawInside</c> / <c>add_views</c>; <c>CEnvCell::grab_visible_cells</c> loads the same set).
    /// </summary>
    public static IReadOnlyList<uint> PotentiallyVisibleSet(EnvCellData viewerCell)
    {
        ArgumentNullException.ThrowIfNull(viewerCell);
        var result = new List<uint>(viewerCell.VisibleCellIds.Count + 1) { viewerCell.Id };
        result.AddRange(viewerCell.VisibleCellIds);
        return result;
    }

    /// <summary>Indoor walk from the viewer's cell (retail <c>PView::DrawInside</c>).</summary>
    /// <param name="cells">Cells of the viewer's landblock.</param>
    /// <param name="viewerCellId">The EnvCell containing the eye.</param>
    /// <param name="eye">Eye position, landblock-local.</param>
    /// <param name="frustum">Optional camera frustum planes, landblock-local, positive inside. Null: no frustum (all
    /// directions).</param>
    /// <param name="limits">Safety caps; null for the defaults.</param>
    /// <exception cref="ArgumentException">The viewer cell is not in <paramref name="cells"/>.</exception>
    public static CellVisibility FromInside(
        ICellSource cells,
        uint viewerCellId,
        Vector3 eye,
        IReadOnlyList<CellPlane>? frustum = null,
        PortalWalkLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var viewer = cells.GetCell(viewerCellId) ?? throw new ArgumentException($"Cell 0x{viewerCellId:X8} is not available.", nameof(viewerCellId));
        var root = new ViewVolume(frustum ?? []);
        var walk = new Walk(cells, eye, limits ?? PortalWalkLimits.Default, root, drawsLandscape: true);
        walk.Seed(viewer, entryPortal: -1, root, PotentiallyVisibleSet(viewer), distance: 0f);
        walk.Run();
        return walk.Result();
    }

    /// <summary>Looking into a building from outdoors (retail <c>PView::DrawPortal</c> for each doorway).</summary>
    /// <param name="cells">Cells of the building's landblock.</param>
    /// <param name="building">The building's doorway polygons.</param>
    /// <param name="eye">Eye position in the building's landblock space.</param>
    /// <param name="frustum">Optional camera frustum planes in the same space, positive inside.</param>
    /// <param name="limits">Safety caps; null for the defaults.</param>
    public static CellVisibility FromOutside(
        ICellSource cells,
        BuildingPortalGeometry building,
        Vector3 eye,
        IReadOnlyList<CellPlane>? frustum = null,
        PortalWalkLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(building);
        var start = new ViewVolume(frustum ?? []);
        var walk = new Walk(cells, eye, limits ?? PortalWalkLimits.Default, start, drawsLandscape: false);
        foreach (var doorway in building.Doorways)
        {
            if (!doorway.CanLookIn(eye))
            {
                continue;
            }

            if (ViewVolume.ThroughPortal(eye, doorway.Vertices, start) is not { } view)
            {
                continue;
            }

            var entry = cells.GetCell(doorway.Portal.OtherCellId);
            if (entry is null)
            {
                continue;
            }

            walk.Seed(entry, doorway.Portal.OtherPortalIndex, view, doorway.Portal.StabList, MaxSquaredDistance(eye, doorway.Vertices));
        }

        walk.Run();
        return walk.Result();
    }

    private static float MaxSquaredDistance(Vector3 eye, IReadOnlyList<Vector3> vertices)
    {
        var max = 0f;
        foreach (var v in vertices)
        {
            max = MathF.Max(max, Vector3.DistanceSquared(eye, v));
        }

        return max;
    }

    private sealed class Walk(ICellSource cells, Vector3 eye, PortalWalkLimits limits, ViewVolume root, bool drawsLandscape)
    {
        private readonly PriorityQueue<WorkItem, float> _todo = new();
        private readonly Dictionary<uint, List<ViewVolume>> _views = new();
        private readonly List<uint> _order = new();
        private readonly Dictionary<uint, EnvCellData> _cellsById = new();
        private readonly HashSet<uint> _saturated = new();
        private readonly List<ViewVolume> _outside = new();
        private int _workItems;
        private bool _truncated;

        public void Seed(EnvCellData cell, int entryPortal, ViewVolume view, IReadOnlyList<uint> potentiallyVisible, float distance)
        {
            _todo.Enqueue(new WorkItem(cell, entryPortal, view, potentiallyVisible.ToHashSet()), distance);
        }

        public void Run()
        {
            while (_todo.TryDequeue(out var item, out _))
            {
                if (++_workItems > limits.MaxWorkItems)
                {
                    _truncated = true;
                    break;
                }

                var expand = AddView(item);
                if (expand is not null)
                {
                    Expand(expand);
                }
            }
        }

        public CellVisibility Result() =>
            new(
                _order.Select(id => new VisibleCell(_cellsById[id], _views[id])).ToArray(),
                _outside.Count > 0,
                _outside.ToArray(),
                _saturated.Count,
                _truncated);

        /// <summary>Records a view of a cell and returns the work to expand, or null when there is nothing new.</summary>
        private WorkItem? AddView(WorkItem item)
        {
            var cell = item.Cell;
            if (_saturated.Contains(cell.Id))
            {
                return null; // already seen through the whole root view
            }

            if (!_views.TryGetValue(cell.Id, out var list))
            {
                list = new List<ViewVolume>();
                _views.Add(cell.Id, list);
                _cellsById.Add(cell.Id, cell);
                _order.Add(cell.Id);
            }

            if (list.Count < limits.MaxViewsPerCell)
            {
                list.Add(item.View);
                return item;
            }

            // Too many views: replace them by the root view (camera frustum), a superset of every view the walk can
            // produce, and expand the cell once more through every portal. Nothing retail would show is lost.
            _saturated.Add(cell.Id);
            list.Clear();
            list.Add(root);
            return item with { EntryPortal = -1, View = root };
        }

        private void Expand(WorkItem item)
        {
            var cell = item.Cell;
            var structure = cells.GetStructure(cell);
            for (var i = 0; i < cell.Portals.Count; i++)
            {
                if (i == item.EntryPortal)
                {
                    continue; // PView::InitCell marks portal_in as already seen
                }

                var portal = cell.Portals[i];
                var polygon = structure.GetPortal(portal.PolygonId)
                    ?? throw new InvalidDataException($"{cell}: portal {i} names polygon {portal.PolygonId}, which is not a portal of its struct.");
                var plane = polygon.Plane.ToParent(cell.Frame);
                var world = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
                var side = plane.SideOf(eye);
                var interior = portal.PortalSide ? PlaneSide.Negative : PlaneSide.Positive;
                if (side != interior && side != PlaneSide.OnPlane)
                {
                    continue; // PView::InitCell: the viewer is outside this portal's cell side
                }

                ViewVolume next;
                if (side == PlaneSide.OnPlane)
                {
                    next = item.View; // degenerate cone; pass the view on (see class remarks)
                }
                else if (ViewVolume.ThroughPortal(eye, world, item.View) is { } through)
                {
                    next = through;
                }
                else
                {
                    continue; // nothing of the portal is inside the current view
                }

                if (portal.LeadsOutside)
                {
                    if (drawsLandscape)
                    {
                        _outside.Add(next);
                    }

                    continue;
                }

                if (!item.PotentiallyVisible.Contains(portal.OtherCellId) || cells.GetCell(portal.OtherCellId) is not { } other)
                {
                    continue; // no view pushed for it (PView::AddViewToPortals skips cells with num_view == 0)
                }

                if (!portal.ExactMatch && side != PlaneSide.OnPlane
                    && portal.OtherPortalIndex >= 0 && portal.OtherPortalIndex < other.Portals.Count)
                {
                    // PView::OtherPortalClip: also clip by the neighbour's own portal polygon.
                    var otherPortal = other.Portals[portal.OtherPortalIndex];
                    var otherPolygon = cells.GetStructure(other).GetPortal(otherPortal.PolygonId);
                    if (otherPolygon is not null)
                    {
                        var otherWorld = otherPolygon.Vertices.Select(other.Frame.LocalToParent).ToArray();
                        if (ViewVolume.ThroughPortal(eye, otherWorld, next) is not { } narrowed)
                        {
                            continue;
                        }

                        next = narrowed;
                    }
                }

                _todo.Enqueue(new WorkItem(other, portal.OtherPortalIndex, next, item.PotentiallyVisible), MaxSquaredDistance(eye, world));
            }
        }

        private sealed record WorkItem(EnvCellData Cell, int EntryPortal, ViewVolume View, HashSet<uint> PotentiallyVisible);
    }
}

/// <summary>Caps that keep the walk bounded on pathological portal graphs.</summary>
/// <param name="MaxViewsPerCell">Views kept per cell. One more view saturates the cell: its views are replaced by the
/// root view (the camera frustum) and it is expanded once more, so the result only grows (counted in the result).</param>
/// <param name="MaxWorkItems">Total (cell, view) expansions before the walk stops (flagged in the result). With
/// saturation a walk needs at most (MaxViewsPerCell + 1) expansions per cell, so this is a last-resort guard.</param>
public sealed record PortalWalkLimits(int MaxViewsPerCell, int MaxWorkItems)
{
    public static readonly PortalWalkLimits Default = new(32, 250_000);
}

/// <summary>Result of a portal walk.</summary>
/// <param name="Cells">Visible cells in visit order (nearest entry portal first); draw in reverse for back to front,
/// as retail's cell_draw_list.</param>
/// <param name="OutsideVisible">The landscape is visible through an outside portal (indoor walks only).</param>
/// <param name="OutsideViews">The views through outside portals (clip the landscape / outdoor objects to them).</param>
/// <param name="SaturatedCells">Cells that reached <see cref="PortalWalkLimits.MaxViewsPerCell"/> and are treated as
/// seen through the whole root view.</param>
/// <param name="Truncated">The walk hit <see cref="PortalWalkLimits.MaxWorkItems"/>.</param>
public sealed record CellVisibility(
    IReadOnlyList<VisibleCell> Cells,
    bool OutsideVisible,
    IReadOnlyList<ViewVolume> OutsideViews,
    int SaturatedCells,
    bool Truncated)
{
    public bool Contains(uint cellId) => Cells.Any(c => c.Cell.Id == cellId);
}

/// <summary>A visible cell and every view it is seen through.</summary>
public sealed record VisibleCell(EnvCellData Cell, IReadOnlyList<ViewVolume> Views)
{
    /// <summary>Retail-style object culling: a sphere is drawn if it touches any of the cell's views.</summary>
    public bool IntersectsSphere(Vector3 center, float radius) => Views.Any(v => v.IntersectsSphere(center, radius));
}

/// <summary>
/// A convex view region: the intersection of the positive half-spaces of its planes (no planes = everything).
/// </summary>
/// <remarks>
/// <para>Numerical rules, all of which can only make a view larger than the exact one:</para>
/// <list type="bullet">
/// <item>Clipping treats a corner within <see cref="OnPlaneTolerance"/> of a plane as on it: the corner is kept and no
/// intersection point is added next to it.</item>
/// <item>After each plane, corners closer than <see cref="MergeDistance"/> are merged, so no edge is shorter than that.
/// The constraint of the dropped short edge is dropped with it.</item>
/// <item><see cref="ThroughPortal"/> never rebuilds a plane from clipped corners: each edge of a clipped portal lies on
/// a plane of the incoming view (reused as is) or on an edge of the portal (plane built from the portal's own corners).
/// A plane through the eye and a short clipped edge is mostly rounding noise and can cut away part of the next portal,
/// which once lost every cell behind it.</item>
/// </list>
/// </remarks>
public sealed class ViewVolume
{
    /// <summary>Corners within this distance (metres) of a clipping plane count as on it.</summary>
    public const float OnPlaneTolerance = 1e-4f;

    /// <summary>Clipped corners closer than this (metres) are merged into one.</summary>
    public const float MergeDistance = 1e-4f;

    /// <summary>Clipped polygons below this area (square metres) count as empty (a sliver seen edge-on).</summary>
    public const float MinArea = 1e-8f;

    public ViewVolume(IReadOnlyList<CellPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        Planes = planes;
    }

    public IReadOnlyList<CellPlane> Planes { get; }

    public bool Contains(Vector3 point)
    {
        foreach (var plane in Planes)
        {
            if (plane.Distance(point) < 0f)
            {
                return false;
            }
        }

        return true;
    }

    public bool IntersectsSphere(Vector3 center, float radius)
    {
        foreach (var plane in Planes)
        {
            if (plane.Distance(center) < -radius)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The cone from <paramref name="eye"/> through a convex polygon: one plane through the eye and each edge, facing in.
    /// </summary>
    /// <remarks>
    /// Planes face in by the polygon's winding as seen from the eye, not by a per-edge centroid test. Corners closer than
    /// <see cref="MergeDistance"/> are merged first, and a plane that would cut one of the polygon's own corners by more
    /// than <see cref="OnPlaneTolerance"/> (rounding noise from a short edge) is left out. An eye in the polygon's plane
    /// gives no planes (everything). For a portal seen through an existing view use <see cref="ThroughPortal"/>.
    /// </remarks>
    public static ViewVolume Through(Vector3 eye, IReadOnlyList<Vector3> polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        var corners = new List<Corner>(polygon.Count);
        for (var i = 0; i < polygon.Count; i++)
        {
            corners.Add(new Corner(polygon[i], ~i));
        }

        Merge(corners);
        var points = corners.Select(c => c.Point).ToArray();
        var orientation = points.Length < 3 ? 0 : Orientation(eye, points);
        if (orientation == 0)
        {
            return new ViewVolume([]);
        }

        var planes = new List<CellPlane>(points.Length);
        for (var i = 0; i < points.Length; i++)
        {
            if (EdgePlane(eye, points[i], points[(i + 1) % points.Length], orientation) is not { } plane)
            {
                continue;
            }

            if (points.All(p => plane.Distance(p) >= -OnPlaneTolerance))
            {
                planes.Add(plane);
            }
        }

        return new ViewVolume(planes);
    }

    /// <summary>
    /// The view through a convex portal polygon: the cone from <paramref name="eye"/> through the part of the portal
    /// inside <paramref name="view"/>, or null when no part of it is (retail <c>PView::ClipPortals</c>, in 3D).
    /// </summary>
    /// <remarks>
    /// The result's planes are those of <paramref name="view"/> that bound the clipped portal, reused exactly, plus the
    /// planes through the eye and those portal edges that survive clipping, built from the portal's own corners. So it
    /// always contains the exact cone (see the class remarks). An eye in the portal's plane returns
    /// <paramref name="view"/> unchanged when part of the portal is inside it.
    /// </remarks>
    public static ViewVolume? ThroughPortal(Vector3 eye, IReadOnlyList<Vector3> portal, ViewVolume view)
    {
        ArgumentNullException.ThrowIfNull(portal);
        ArgumentNullException.ThrowIfNull(view);
        var clipped = ClipCorners(portal, view.Planes);
        if (clipped is null)
        {
            return null;
        }

        var orientation = Orientation(eye, portal);
        if (orientation == 0)
        {
            return view;
        }

        var planes = new List<CellPlane>(clipped.Count);
        var used = new HashSet<int>();
        foreach (var corner in clipped)
        {
            if (!used.Add(corner.Edge))
            {
                continue;
            }

            if (corner.Edge >= 0)
            {
                planes.Add(view.Planes[corner.Edge]);
            }
            else
            {
                var edge = ~corner.Edge;
                if (EdgePlane(eye, portal[edge], portal[(edge + 1) % portal.Count], orientation) is { } plane)
                {
                    planes.Add(plane);
                }
            }
        }

        return new ViewVolume(planes);
    }

    /// <summary>
    /// Clips a convex polygon to the volume (Sutherland-Hodgman against each plane, with the tolerances in the class
    /// remarks). Returns no corners when nothing of at least <see cref="MinArea"/> is left.
    /// </summary>
    public static Vector3[] Clip(IReadOnlyList<Vector3> polygon, ViewVolume volume)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(volume);
        return ClipCorners(polygon, volume.Planes)?.Select(c => c.Point).ToArray() ?? [];
    }

    /// <summary>
    /// Sutherland-Hodgman that tracks, for each corner, what its outgoing edge lies on: a plane index (0 or more) or
    /// the complement <c>~k</c> of the input polygon's edge k. Null when the result is empty.
    /// </summary>
    private static List<Corner>? ClipCorners(IReadOnlyList<Vector3> polygon, IReadOnlyList<CellPlane> planes)
    {
        var current = new List<Corner>(polygon.Count + 4);
        for (var i = 0; i < polygon.Count; i++)
        {
            current.Add(new Corner(polygon[i], ~i));
        }

        Merge(current);
        if (current.Count < 3)
        {
            return null;
        }

        var next = new List<Corner>(polygon.Count + 4);
        var sides = new List<int>(polygon.Count + 4);
        var distances = new List<float>(polygon.Count + 4);
        for (var j = 0; j < planes.Count; j++)
        {
            var plane = planes[j];
            sides.Clear();
            distances.Clear();
            var cut = false;
            foreach (var corner in current)
            {
                var d = plane.Distance(corner.Point);
                var side = d > OnPlaneTolerance ? 1 : d < -OnPlaneTolerance ? -1 : 0;
                distances.Add(d);
                sides.Add(side);
                cut |= side < 0;
            }

            if (!cut)
            {
                continue;
            }

            next.Clear();
            for (var i = 0; i < current.Count; i++)
            {
                var b = (i + 1) % current.Count;
                var a = current[i];
                if (sides[i] >= 0)
                {
                    // A corner on the plane whose edge leaves runs along the plane to where the polygon comes back.
                    next.Add(sides[i] == 0 && sides[b] < 0 ? a with { Edge = j } : a);
                    if (sides[i] > 0 && sides[b] < 0)
                    {
                        next.Add(new Corner(Intersect(a.Point, current[b].Point, distances[i], distances[b]), j));
                    }
                }
                else if (sides[b] > 0)
                {
                    next.Add(new Corner(Intersect(a.Point, current[b].Point, distances[i], distances[b]), a.Edge));
                }
            }

            (current, next) = (next, current);
            Merge(current);
            if (current.Count < 3)
            {
                return null;
            }
        }

        return Area(current) > MinArea ? current : null;
    }

    private static Vector3 Intersect(Vector3 a, Vector3 b, float da, float db) => a + ((b - a) * (da / (da - db)));

    /// <summary>
    /// Merges consecutive corners (and the last with the first) closer than <see cref="MergeDistance"/>. The short edge
    /// between them disappears; the merged corner keeps the first position and the outgoing edge of the second.
    /// </summary>
    private static void Merge(List<Corner> corners)
    {
        const float limit = MergeDistance * MergeDistance;
        var kept = 0;
        for (var i = 0; i < corners.Count; i++)
        {
            if (kept > 0 && Vector3.DistanceSquared(corners[kept - 1].Point, corners[i].Point) < limit)
            {
                corners[kept - 1] = corners[kept - 1] with { Edge = corners[i].Edge };
                continue;
            }

            corners[kept++] = corners[i];
        }

        corners.RemoveRange(kept, corners.Count - kept);
        while (corners.Count > 1 && Vector3.DistanceSquared(corners[^1].Point, corners[0].Point) < limit)
        {
            corners.RemoveAt(corners.Count - 1);
        }
    }

    /// <summary>
    /// +1 when <c>(a - eye) x (b - eye)</c> over the polygon's edges points into its cone, -1 when it points out, 0 when
    /// the eye is (within 1 micrometre) in the polygon's plane. From the winding: the edge cross products point out of
    /// the cone when the eye is on the polygon's front (counter-clockwise) side.
    /// </summary>
    private static int Orientation(Vector3 eye, IReadOnlyList<Vector3> polygon)
    {
        var normal = Vector3.Zero;
        var centroid = Vector3.Zero;
        for (var i = 0; i < polygon.Count; i++)
        {
            centroid += polygon[i];
            if (i >= 1 && i + 1 < polygon.Count)
            {
                normal += Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
            }
        }

        var length = normal.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            return 0;
        }

        var distance = Vector3.Dot(normal / length, eye - (centroid / polygon.Count));
        return distance > 1e-6f ? -1 : distance < -1e-6f ? 1 : 0;
    }

    /// <summary>The plane through the eye and edge a-b, facing into the cone; null when the eye is on the edge's line.</summary>
    private static CellPlane? EdgePlane(Vector3 eye, Vector3 a, Vector3 b, int orientation)
    {
        var normal = Vector3.Cross(a - eye, b - eye) * orientation;
        var length = normal.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            return null;
        }

        normal /= length;
        return new CellPlane(normal, -Vector3.Dot(normal, eye));
    }

    private static float Area(List<Corner> polygon)
    {
        var sum = Vector3.Zero;
        for (var i = 1; i + 1 < polygon.Count; i++)
        {
            sum += Vector3.Cross(polygon[i].Point - polygon[0].Point, polygon[i + 1].Point - polygon[0].Point);
        }

        return sum.Length() * 0.5f;
    }

    /// <summary>A clipped corner and what its outgoing edge lies on (plane index, or <c>~k</c> for input edge k).</summary>
    private readonly record struct Corner(Vector3 Point, int Edge);
}
