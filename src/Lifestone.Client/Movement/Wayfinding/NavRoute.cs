using System.Numerics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// A point on the outdoor walk map (P5a, docs/wayfinding.md): a landblock (0xXXYY0000) plus landblock-local metres, AC
/// axes (+X east, +Y north, +Z up), exactly a retail position's landblock and origin. The planner works in global metres
/// (<see cref="Global"/>: the landblock's offset from landblock 0x0000 by <see cref="LandDefs.GetBlockOffset"/>, plus the
/// local x and y), so a point and a route cross landblock edges with no seam; <see cref="Normalized"/> puts a local
/// origin that left its landblock back into the landblock that holds it. Not retail: Lifestone's own wayfinder.
/// </summary>
/// <param name="LandblockId">The landblock, 0xXXYY0000 (the low 16 bits are ignored).</param>
/// <param name="Local">Landblock-local metres; z is the ground's height where the point came from a node.</param>
public readonly record struct NavPoint(uint LandblockId, Vector3 Local)
{
    /// <summary>A point at landblock-local (x, y, z).</summary>
    public static NavPoint At(uint landblockId, float x, float y, float z = 0f) =>
        new(landblockId & 0xFFFF0000u, new Vector3(x, y, z));

    /// <summary>The height (the local z).</summary>
    public float Z => Local.Z;

    /// <summary>The global horizontal position in metres: <see cref="BlockOrigin"/> plus the local x and y.</summary>
    public Vector2 Global => BlockOrigin(LandblockId) + new Vector2(Local.X, Local.Y);

    /// <summary>
    /// The global metres of a landblock's south-west corner: <see cref="LandDefs.GetBlockOffset"/> from landblock 0x0000
    /// (the map's south-west corner) to <paramref name="landblockId"/>, so (x * 192, y * 192).
    /// </summary>
    public static Vector2 BlockOrigin(uint landblockId)
    {
        var offset = LandDefs.GetBlockOffset(0u, landblockId & 0xFFFF0000u);
        return new Vector2(offset.X, offset.Y);
    }

    /// <summary>The point at global metres <paramref name="global"/> and height <paramref name="z"/> (the landblock that holds it; clamped to the map).</summary>
    public static NavPoint FromGlobal(Vector2 global, float z)
    {
        const float block = NavTile.Size * NavTile.NodeMetres;
        int bx = Math.Clamp((int)MathF.Floor(global.X / block), 0, LandDefs.BlocksPerSide - 1);
        int by = Math.Clamp((int)MathF.Floor(global.Y / block), 0, LandDefs.BlocksPerSide - 1);
        uint id = ((uint)bx << 24) | ((uint)by << 16);
        return new NavPoint(id, new Vector3(global.X - (bx * block), global.Y - (by * block), z));
    }

    /// <summary>The same point with its local origin inside its landblock (a local x of 193 becomes 1 in the landblock east).</summary>
    public NavPoint Normalized() => FromGlobal(Global, Local.Z);

    /// <summary>The horizontal distance to <paramref name="other"/> in metres, across landblocks.</summary>
    public float HorizontalDistance(NavPoint other) => Vector2.Distance(Global, other.Global);

    /// <inheritdoc/>
    public override string ToString() =>
        $"0x{LandblockId:X8} [{Local.X:0.00} {Local.Y:0.00} {Local.Z:0.00}]";
}

/// <summary>What a plan came to (<see cref="NavPlanResult.Status"/>).</summary>
public enum NavStatus
{
    /// <summary>A route to the goal.</summary>
    Found,

    /// <summary>The goal lies beyond the attached tiles: a route to a horizon goal on their edge (<see cref="NavPlanResult.HorizonPoint"/>).</summary>
    Horizon,

    /// <summary>No route over the attached tiles (the unbounded search failed too).</summary>
    NoPath,

    /// <summary>The start is not on an attached tile, or stands on a blocked node with no open node next to it.</summary>
    StartBlocked,

    /// <summary>The goal stands on a blocked or scarred node (a point goal), or no open node lies within an object goal's range.</summary>
    GoalBlocked,
}

/// <summary>How far the fine search had to widen (<see cref="NavPlanResult.Widened"/>).</summary>
public enum NavWidening
{
    /// <summary>Bounded to the coarse corridor plus one land cell.</summary>
    Bounded,

    /// <summary>Widened once: the corridor plus three land cells (one plus two).</summary>
    Widened,

    /// <summary>Unbounded over every attached tile (the corridor failed twice, or the coarse graph found none).</summary>
    Unbounded,
}

/// <summary>
/// Where a run goes (P5a, docs/wayfinding.md): a point goal (<see cref="Point"/>, stop within <see cref="StopTolerance"/>,
/// pass within <see cref="PassTolerance"/>) or an object goal (<see cref="Guid"/> at <see cref="Point"/>, arrive within
/// <see cref="Range"/>, with <see cref="NeedSight"/> for the runner's arrival test). Either may name an indoor cell
/// (<see cref="CellId"/>): the room graph (S3) joins there; the outdoor planner treats it as data.
/// </summary>
public sealed record NavGoal
{
    /// <summary>The point goal's stop tolerance in metres. Not retail: Lifestone's own.</summary>
    public const float DefaultStopTolerance = 0.5f;

    /// <summary>The point goal's pass tolerance in metres (a goal passed through, not stopped at). Not retail: Lifestone's own.</summary>
    public const float DefaultPassTolerance = 1f;

    /// <summary>The goal point (an object goal's target position).</summary>
    public NavPoint Point { get; init; }

    /// <summary>The object's guid for an object goal; null for a point goal.</summary>
    public uint? Guid { get; init; }

    /// <summary>An object goal's arrival range in metres (0 for a point goal).</summary>
    public float Range { get; init; }

    /// <summary>Whether an object goal's arrival needs a clear sight line (the runner's test; the planner does not judge it).</summary>
    public bool NeedSight { get; init; }

    /// <summary>A point goal's stop tolerance.</summary>
    public float StopTolerance { get; init; } = DefaultStopTolerance;

    /// <summary>A point goal's pass tolerance.</summary>
    public float PassTolerance { get; init; } = DefaultPassTolerance;

    /// <summary>An indoor cell the goal names (data for the room graph), or null.</summary>
    public uint? CellId { get; init; }

    /// <summary>Whether this is an object goal.</summary>
    public bool IsObject => Guid is not null;

    /// <summary>A point goal.</summary>
    public static NavGoal ToPoint(NavPoint point, uint? cellId = null) => new() { Point = point, CellId = cellId };

    /// <summary>An object goal: reach within <paramref name="range"/> metres of <paramref name="target"/>.</summary>
    public static NavGoal ToObject(uint guid, NavPoint target, float range, bool needSight, uint? cellId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(range);
        return new NavGoal { Point = target, Guid = guid, Range = range, NeedSight = needSight, CellId = cellId };
    }
}

/// <summary>
/// A planned route (P5a, docs/wayfinding.md): the A* node path pulled to vertices by the lattice line of sight, each with
/// its ground z. Arc length is horizontal (global metres in x and y), the measure the runner's pure pursuit and its 3 m
/// off-route test use (<see cref="PointAt"/>, <see cref="NearestArcLength"/>, <see cref="DistanceFrom"/>). Immutable.
/// </summary>
public sealed class NavRoute
{
    private readonly Vector2[] _xy;
    private readonly float[] _z;
    private readonly float[] _arc;
    private readonly NavPoint[] _vertices;

    internal NavRoute(Vector2[] xy, float[] z, int rawNodeCount)
    {
        if (xy.Length == 0 || xy.Length != z.Length)
        {
            throw new ArgumentException("A route needs at least one vertex and one z per vertex.", nameof(xy));
        }

        _xy = xy;
        _z = z;
        _arc = new float[xy.Length];
        _vertices = new NavPoint[xy.Length];
        for (int i = 0; i < xy.Length; i++)
        {
            _arc[i] = i == 0 ? 0f : _arc[i - 1] + Vector2.Distance(xy[i - 1], xy[i]);
            _vertices[i] = NavPoint.FromGlobal(xy[i], z[i]);
        }

        RawNodeCount = rawNodeCount;
    }

    /// <summary>The pulled vertices, start first; each z is its node's ground height.</summary>
    public IReadOnlyList<NavPoint> Vertices => _vertices;

    /// <summary>The horizontal length of the route in metres.</summary>
    public float ArcLength => _arc[^1];

    /// <summary>The A* node path's length in nodes before pulling.</summary>
    public int RawNodeCount { get; }

    /// <summary>The route's start.</summary>
    public NavPoint Start => _vertices[0];

    /// <summary>The route's end.</summary>
    public NavPoint End => _vertices[^1];

    /// <summary>The arc length at vertex <paramref name="index"/>.</summary>
    public float ArcLengthAt(int index) => _arc[index];

    /// <summary>The point at arc length <paramref name="s"/> (clamped to the route), z interpolated between vertices.</summary>
    public NavPoint PointAt(float s)
    {
        if (s <= 0f || _xy.Length == 1)
        {
            return _vertices[0];
        }

        if (s >= ArcLength)
        {
            return _vertices[^1];
        }

        int i = Array.BinarySearch(_arc, s);
        if (i >= 0)
        {
            return _vertices[i];
        }

        int hi = ~i;
        int lo = hi - 1;
        float span = _arc[hi] - _arc[lo];
        float t = span <= 0f ? 0f : (s - _arc[lo]) / span;
        return NavPoint.FromGlobal(Vector2.Lerp(_xy[lo], _xy[hi], t), _z[lo] + ((_z[hi] - _z[lo]) * t));
    }

    /// <summary>The arc length of the route's point nearest <paramref name="point"/> (horizontally).</summary>
    public float NearestArcLength(NavPoint point) => Nearest(point.Global).Arc;

    /// <summary>The horizontal distance from <paramref name="point"/> to the route (the runner's 3 m off-route test).</summary>
    public float DistanceFrom(NavPoint point) => Nearest(point.Global).Distance;

    private (float Arc, float Distance) Nearest(Vector2 p)
    {
        if (_xy.Length == 1)
        {
            return (0f, Vector2.Distance(p, _xy[0]));
        }

        float bestArc = 0f;
        float bestD2 = float.MaxValue;
        for (int i = 0; i + 1 < _xy.Length; i++)
        {
            var a = _xy[i];
            var ab = _xy[i + 1] - a;
            float len2 = ab.LengthSquared();
            float t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
            float d2 = Vector2.DistanceSquared(p, a + (ab * t));
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestArc = _arc[i] + (t * (_arc[i + 1] - _arc[i]));
            }
        }

        return (bestArc, MathF.Sqrt(bestD2));
    }
}

/// <summary>
/// One plan's outcome (P5a, docs/wayfinding.md). <see cref="Reason"/> names a failure (a constant string, never a guess).
/// </summary>
public sealed record NavPlanResult
{
    /// <summary>What the plan came to.</summary>
    public NavStatus Status { get; init; }

    /// <summary>The pulled route (on <see cref="NavStatus.Found"/> and <see cref="NavStatus.Horizon"/>), else null.</summary>
    public NavRoute? Route { get; init; }

    /// <summary>The goal planned for.</summary>
    public required NavGoal Goal { get; init; }

    /// <summary>On <see cref="NavStatus.Horizon"/>, the horizon goal on the attached tiles' edge; else null.</summary>
    public NavPoint? HorizonPoint { get; init; }

    /// <summary>The fine search's expanded (closed) nodes, over every attempt.</summary>
    public int NodesExpanded { get; init; }

    /// <summary>The fine search's touched (opened or improved) nodes, over every attempt.</summary>
    public int NodesTouched { get; init; }

    /// <summary>The coarse corridor's region count (0 when the coarse graph found none).</summary>
    public int CorridorRegions { get; init; }

    /// <summary>How far the fine search widened.</summary>
    public NavWidening Widened { get; init; }

    /// <summary>Whether the start stood on a blocked node and the route starts from the open node next to it (<see cref="PathPlanner.StartSnapNodes"/>).</summary>
    public bool StartSnapped { get; init; }

    /// <summary>The fine A* path's cost (<see cref="PathPlanner"/>'s step cost summed; the octile distance on open flat ground).</summary>
    public float Cost { get; init; }

    /// <summary>The plan's wall time in milliseconds.</summary>
    public double Milliseconds { get; init; }

    /// <summary>The failure's name (for example "start-not-attached", "goal-blocked", "goal-scarred"), or null.</summary>
    public string? Reason { get; init; }
}
