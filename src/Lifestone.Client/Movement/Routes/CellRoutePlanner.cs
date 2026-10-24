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

/// <summary>A portal's doorway in landblock space (<see cref="CellRoutePlanner.DoorwayOf"/>).</summary>
/// <param name="Centre">The polygon's centroid.</param>
/// <param name="Sill">The height of the polygon's lowest corner.</param>
/// <param name="Outward">The unit horizontal direction toward the neighbour cell (outside, for an outside portal).</param>
public readonly record struct Doorway(Vector3 Centre, float Sill, Vector3 Outward);

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
/// <para><b>Lean.</b> A doorway's plane may lean: the Black Spawn Den's plazas are octagons whose doorways lean 11 degrees (the
/// portal normal has a vertical part of 0.196). <c>maxLean</c> (constructor; default 0, today's rule) is the largest
/// vertical part of the normal accepted; the rule never goes below the upright 0.1. The fit test is projected on the flat
/// normal (the horizontal part, renormalised), so a leaning doorway is judged by the width and height of the opening a
/// human walks through it, not of the tilted polygon (P5a: closes <c>m3-fight-planner-lean</c>; the den's own planner is
/// gone). Not retail: Lifestone's own constant.</para>
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

    /// <summary>The largest vertical part of an upright doorway's normal (the Academy portal test's rule); <c>maxLean</c> raises it, never lowers it.</summary>
    public const float UprightNormalZ = 0.1f;

    /// <summary>The height of a standing human the fit test probes under a doorway's lintel (the human Setup's two spheres: 0.48 m radius).</summary>
    public const float HumanHeight = 1.8f;

    private readonly LandblockCellSet _cells;
    private readonly float _limit;

    /// <summary>A planner over <paramref name="cells"/> that walks upright doorways only.</summary>
    public CellRoutePlanner(LandblockCellSet cells)
        : this(cells, 0f, 0f)
    {
    }

    /// <summary>A planner over <paramref name="cells"/> that also walks doorways leaning up to <paramref name="maxLean"/> (the vertical part of the portal normal; 0 keeps the upright rule).</summary>
    public CellRoutePlanner(LandblockCellSet cells, float maxLean)
        : this(cells, maxLean, 0f)
    {
    }

    /// <summary>
    /// A planner as above whose fit test probes <paramref name="headSlack"/> metres under <see cref="HumanHeight"/>: the shops' doorways
    /// are exactly 1.8 m tall (Renald's room 0xA9B40141 is reached through two), so the probe at the lintel's own height fails by no margin
    /// at all (0 keeps the Academy rule). Not retail: Lifestone's own.
    /// </summary>
    public CellRoutePlanner(LandblockCellSet cells, float maxLean, float headSlack)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLean);
        ArgumentOutOfRangeException.ThrowIfNegative(headSlack);
        _cells = cells;
        MaxLean = maxLean;
        HeadSlack = headSlack;
        _limit = MathF.Max(UprightNormalZ, maxLean);
    }

    /// <summary>The lean accepted beyond the upright rule: the largest vertical part of a doorway's normal (0: upright only).</summary>
    public float MaxLean { get; }

    /// <summary>How far under <see cref="HumanHeight"/> the fit test probes (0: at the full height).</summary>
    public float HeadSlack { get; }

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
            var (approach, push) = DoorwayPair(cell, portal);
            if (approach is { } before)
            {
                waypoints.Add(before);
            }

            waypoints.Add(push);
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
    /// in a polygon wide and tall enough (see <see cref="Fits"/> and the remarks).
    /// </summary>
    public bool IsWalkable(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return !portal.LeadsOutside && Fits(cell, portal);
    }

    /// <summary>
    /// True when <paramref name="portal"/>'s polygon stands upright enough to be a doorway: it exists in the cell's structure and its plane's
    /// normal leans no more than the limit (0.1, or <see cref="MaxLean"/>) from horizontal. Floors and ceilings are not.
    /// </summary>
    public bool IsUpright(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        return MathF.Abs(normal.Z) <= _limit && (normal.X * normal.X) + (normal.Y * normal.Y) >= 1e-6f;
    }

    /// <summary>
    /// True when a standing human fits through <paramref name="portal"/> of <paramref name="cell"/>, wherever it leads
    /// (an outside portal included): its plane's normal leans no more than the limit (0.1, or <see cref="MaxLean"/>), and
    /// the points 0.2 m and 1.8 m above its sill, and 0.48 m either side of its centre 1 m above the sill, all lie in its
    /// polygon seen along the flat normal.
    /// </summary>
    public bool Fits(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        if (MathF.Abs(normal.Z) > _limit)
        {
            return false;
        }

        var flat = new Vector3(normal.X, normal.Y, 0f);
        if (flat.LengthSquared() < 1e-6f)
        {
            return false; // a floor or a ceiling
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
        flat = Vector3.Normalize(flat);
        var along = new Vector3(-flat.Y, flat.X, 0f);
        var floor = new Vector3(centre.X, centre.Y, sill);
        return InPortal(corners, along, floor + new Vector3(0, 0, 0.2f))
            && InPortal(corners, along, floor + new Vector3(0, 0, HumanHeight - HeadSlack))
            && InPortal(corners, along, floor + new Vector3(0, 0, 1f) + (along * HumanHalfWidth))
            && InPortal(corners, along, floor + new Vector3(0, 0, 1f) - (along * HumanHalfWidth));
    }

    /// <summary>
    /// The doorway of <paramref name="portal"/> in landblock space: the polygon's centroid, its sill height and the unit
    /// horizontal direction toward the neighbour (outside, for an outside portal; <see cref="Waypoint"/>'s rule), or null
    /// when the cell's structure has no such portal polygon or its normal is vertical.
    /// </summary>
    public Doorway? DoorwayOf(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return null;
        }

        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        foreach (var vertex in polygon.Vertices)
        {
            var corner = cell.Frame.LocalToParent(vertex);
            centre += corner;
            sill = MathF.Min(sill, corner.Z);
        }

        centre /= polygon.Vertices.Length;
        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        var into = portal.PortalSide ? normal : -normal;
        into.Z = 0f;
        if (into.LengthSquared() < 1e-6f)
        {
            return null;
        }

        return new Doorway(centre, sill, Vector3.Normalize(into));
    }

    /// <summary>
    /// The pair of waypoints of a walk through <paramref name="portal"/>: the approach <see cref="ApproachMetres"/> before it in
    /// <paramref name="cell"/> (null when the room is too thin for it) and the push <see cref="PushIntoCellMetres"/> past it in the
    /// neighbour (the doorway's centre when neither lies inside the neighbour).
    /// </summary>
    public (CellFrame? Approach, CellFrame Push) DoorwayPair(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return (InsideWaypoint(cell, portal, -ApproachMetres), InsideWaypoint(cell, portal, PushIntoCellMetres) ?? Waypoint(cell, portal, 0f));
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

    /// <summary>True when <paramref name="point"/> lies inside the polygon <paramref name="corners"/> seen along the horizontal axis <paramref name="along"/> and Z (a crossing test).</summary>
    private static bool InPortal(ReadOnlySpan<Vector3> corners, Vector3 along, Vector3 point)
    {
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
