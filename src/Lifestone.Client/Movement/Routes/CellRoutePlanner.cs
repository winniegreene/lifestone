using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Routes;

/// <summary>A path through a landblock's rooms: the cells in order and the points to walk through.</summary>
/// <param name="Cells">The cells from the start cell to the target cell, each next one through a portal of the one before.</param>
/// <param name="Waypoints">
/// Two points per doorway: one in front of it in the room being left (<see cref="CellRoutePlanner.ApproachMetres"/>
/// before the portal) and one beyond it in the room entered (<see cref="CellRoutePlanner.PushIntoCellMetres"/> past it),
/// each at the portal's centre over the floor.
/// </param>
public sealed record CellRoute(IReadOnlyList<uint> Cells, IReadOnlyList<CellFrame> Waypoints);

/// <summary>
/// Plans a walk between two EnvCells of one landblock: a breadth-first search over the cells' portals
/// (<see cref="EnvCellData.Portals"/>, <see cref="CellPortalData.OtherCellId"/>) that a standing human can walk through,
/// and a pair of waypoints at each doorway (m2-plan PH9, the Academy exit route).
/// </summary>
/// <remarks>
/// <para><b>Doorways.</b> A portal is walked through only when it is upright (its plane's normal within 0.1 of
/// horizontal) and a standing human fits through it: the points 0.2 m and 1.8 m above its sill, and 0.48 m either side
/// of its centre 1 m above the sill, all lie in its polygon (the criterion of the Academy portal test,
/// InteriorCollisionTests). Portals in floors and ceilings and the slivers beside arches are left out; outside portals
/// too (the planner stays in one landblock).</para>
/// <para><b>Waypoints.</b> The portal polygon's centroid (in landblock space), moved along the portal's normal
/// <see cref="ApproachMetres"/> back into the room being left and <see cref="PushIntoCellMetres"/> into the room
/// entered, and dropped onto that room's floor (<see cref="CellStructure.Floors"/>: the highest floor polygon under it,
/// else the sill's height). Walking straight from one to the next crosses each doorway square to it. A point that would
/// fall outside its room (a room thinner than the distance) moves half as far, and an approach point that still does is
/// left out; the point beyond a doorway then falls back to the doorway's centre.</para>
/// <para>Not retail: the client has no path planner; this drives the scenarios the way a player walks.</para>
/// </remarks>
public sealed class CellRoutePlanner
{
    /// <summary>How far past a doorway the waypoint in the room entered lies (m2-plan PH9: 0.5 m).</summary>
    public const float PushIntoCellMetres = 0.5f;

    /// <summary>How far before a doorway the waypoint in the room being left lies.</summary>
    public const float ApproachMetres = 0.7f;

    /// <summary>The widest a standing human is: the human Setup's sphere radius (0.48 m) either side.</summary>
    public const float HumanHalfWidth = 0.48f;

    private readonly LandblockCellSet _cells;

    /// <summary>A planner over <paramref name="cells"/>.</summary>
    public CellRoutePlanner(LandblockCellSet cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells;
    }

    /// <summary>The cells planned over.</summary>
    public LandblockCellSet Cells => _cells;

    /// <summary>
    /// The shortest walk (fewest doorways) from <paramref name="fromCellId"/> to <paramref name="toCellId"/>, or null when
    /// either cell is not in the landblock or no walkable doorways join them.
    /// </summary>
    public CellRoute? Plan(uint fromCellId, uint toCellId)
    {
        var path = FindPath(fromCellId, toCellId);
        if (path is null)
        {
            return null;
        }

        var waypoints = new List<CellFrame>();
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var cell = _cells.GetCell(path[i])!;
            var portal = FindWalkablePortal(cell, path[i + 1])!.Value;
            if (InsideWaypoint(cell, portal, -ApproachMetres) is { } approach)
            {
                waypoints.Add(approach);
            }

            waypoints.Add(InsideWaypoint(cell, portal, PushIntoCellMetres) ?? Waypoint(cell, portal, 0f));
        }

        return new CellRoute(path, waypoints);
    }

    /// <summary>
    /// The cells of the shortest walk (breadth first, portals in stored order) from <paramref name="fromCellId"/> to
    /// <paramref name="toCellId"/> through walkable doorways (<see cref="IsWalkable"/>), or null.
    /// </summary>
    public IReadOnlyList<uint>? FindPath(uint fromCellId, uint toCellId)
    {
        if (_cells.GetCell(fromCellId) is null || _cells.GetCell(toCellId) is null)
        {
            return null;
        }

        var previous = new Dictionary<uint, uint> { [fromCellId] = 0 };
        var queue = new Queue<uint>();
        queue.Enqueue(fromCellId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == toCellId)
            {
                var path = new List<uint>();
                for (var at = toCellId; at != 0; at = previous[at])
                {
                    path.Add(at);
                    if (at == fromCellId)
                    {
                        break;
                    }
                }

                path.Reverse();
                return path;
            }

            var cell = _cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || previous.ContainsKey(portal.OtherCellId) || _cells.GetCell(portal.OtherCellId) is null
                    || !IsWalkable(cell, portal))
                {
                    continue;
                }

                previous[portal.OtherCellId] = id;
                queue.Enqueue(portal.OtherCellId);
            }
        }

        return null;
    }

    /// <summary>
    /// True when a standing human can walk through <paramref name="portal"/> of <paramref name="cell"/>: an inside portal
    /// in an upright polygon wide and tall enough (see the remarks).
    /// </summary>
    public bool IsWalkable(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (portal.LeadsOutside || _cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        if (MathF.Abs(normal.Z) > 0.1f)
        {
            return false;
        }

        Span<Vector3> corners = stackalloc Vector3[polygon.Vertices.Length];
        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = cell.Frame.LocalToParent(polygon.Vertices[i]);
            centre += corners[i];
            sill = MathF.Min(sill, corners[i].Z);
        }

        centre /= corners.Length;
        var along = Vector3.Normalize(new Vector3(-normal.Y, normal.X, 0f));
        var floor = new Vector3(centre.X, centre.Y, sill);
        return InPortal(corners, normal, floor + new Vector3(0, 0, 0.2f))
            && InPortal(corners, normal, floor + new Vector3(0, 0, 1.8f))
            && InPortal(corners, normal, floor + new Vector3(0, 0, 1f) + (along * HumanHalfWidth))
            && InPortal(corners, normal, floor + new Vector3(0, 0, 1f) - (along * HumanHalfWidth));
    }

    /// <summary>
    /// The point at <paramref name="portal"/>'s centre <paramref name="metres"/> beyond it (negative: before it, in
    /// <paramref name="cell"/>), over the floor of the cell it lies in.
    /// </summary>
    public CellFrame Waypoint(EnvCellData cell, CellPortalData portal, float metres)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var polygon = _cells.GetStructure(cell).GetPortal(portal.PolygonId)
            ?? throw new InvalidDataException($"EnvCell 0x{cell.Id:X8} has no portal polygon {portal.PolygonId}.");
        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        foreach (var vertex in polygon.Vertices)
        {
            var corner = cell.Frame.LocalToParent(vertex);
            centre += corner;
            sill = MathF.Min(sill, corner.Z);
        }

        centre /= polygon.Vertices.Length;

        // Retail portal_side: the cell lies on the negative side of the portal's plane when PortalSide, so the neighbour
        // lies along the normal; otherwise against it.
        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        var into = portal.PortalSide ? normal : -normal;
        into.Z = 0f;
        into = Vector3.Normalize(into);
        var point = centre + (into * metres);
        var target = metres >= 0f ? _cells.GetCell(portal.OtherCellId) : cell;
        var z = target is null ? sill : FloorUnder(target, new Vector3(point.X, point.Y, sill + 0.5f)) ?? sill;
        var cellId = target?.Id ?? cell.Id;
        return new CellFrame(cellId, new AcFrame(new Vector3(point.X, point.Y, z), Quaternion.Identity));
    }

    /// <summary>
    /// <see cref="Waypoint"/> at <paramref name="metres"/>, or at half of it, whichever first lies inside the cell it is
    /// meant for (tested 1 m above the floor; a room thinner than the distance, such as a doorway's own cell, would put it
    /// in the next room); null when neither does.
    /// </summary>
    private CellFrame? InsideWaypoint(EnvCellData cell, CellPortalData portal, float metres)
    {
        var expected = metres >= 0f ? _cells.GetCell(portal.OtherCellId) : cell;
        if (expected is null)
        {
            return null;
        }

        foreach (var distance in (ReadOnlySpan<float>)[metres, metres * 0.5f])
        {
            var point = Waypoint(cell, portal, distance);
            if (_cells.Contains(expected, point.Frame.Origin + new Vector3(0f, 0f, 1f)))
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>The first walkable portal of <paramref name="cell"/> into <paramref name="otherCellId"/>, or null.</summary>
    private CellPortalData? FindWalkablePortal(EnvCellData cell, uint otherCellId)
    {
        foreach (var portal in cell.Portals)
        {
            if (portal.OtherCellId == otherCellId && IsWalkable(cell, portal))
            {
                return portal;
            }
        }

        return null;
    }

    /// <summary>
    /// The height (landblock space) of the highest floor polygon of <paramref name="cell"/> under <paramref name="point"/>
    /// and not above it, or null.
    /// </summary>
    private float? FloorUnder(EnvCellData cell, Vector3 point)
    {
        var local = cell.Frame.ParentToLocal(point);
        float? best = null;
        foreach (var floor in _cells.GetStructure(cell).Floors)
        {
            if (MathF.Abs(floor.Plane.Normal.Z) < 1e-4f || !InPolygon2D(floor.Vertices, local.X, local.Y))
            {
                continue;
            }

            var z = -((floor.Plane.Normal.X * local.X) + (floor.Plane.Normal.Y * local.Y) + floor.Plane.D) / floor.Plane.Normal.Z;
            if (z <= local.Z && (best is null || z > best))
            {
                best = z;
            }
        }

        return best is { } height ? cell.Frame.LocalToParent(new Vector3(local.X, local.Y, height)).Z : null;
    }

    /// <summary>True when <paramref name="point"/> lies inside the upright polygon <paramref name="corners"/> (its plane's normal <paramref name="normal"/>).</summary>
    private static bool InPortal(ReadOnlySpan<Vector3> corners, Vector3 normal, Vector3 point)
    {
        // Project onto the plane's horizontal axis and Z, then a crossing test.
        var along = new Vector3(-normal.Y, normal.X, 0f);
        var inside = false;
        var px = Vector3.Dot(point, along);
        var py = point.Z;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var xi = Vector3.Dot(corners[i], along);
            var yi = corners[i].Z;
            var xj = Vector3.Dot(corners[j], along);
            var yj = corners[j].Z;
            if ((yi > py) != (yj > py) && px < ((xj - xi) * (py - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>A crossing test of (<paramref name="x"/>, <paramref name="y"/>) against a polygon's X and Y.</summary>
    private static bool InPolygon2D(Vector3[] vertices, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            if ((vertices[i].Y > y) != (vertices[j].Y > y)
                && x < ((vertices[j].X - vertices[i].X) * (y - vertices[i].Y) / (vertices[j].Y - vertices[i].Y)) + vertices[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
