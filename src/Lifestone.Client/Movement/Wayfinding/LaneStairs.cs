using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The walk from a raised door down to the ground when its stairs turn aside from the lane's line (M6 7 A, D62's Brighteyes walk; asked only of a door
/// <c>NavTileBuilder.LaneClimbs</c> refuses). The Refuge's three huts (model 0x01000D67, six doors with sills at 63.60 over the ground at 58.00) open onto a covered walk between two rooms; the stairs are
/// not the shell's but five statics (0x02000300, a 1.5 m wide flight, normal z 0.78) that leave the walk at its open end, at right angles to both doors.
/// </summary>
/// <remarks>
/// <para>A shortest-path search over the walkable polygons from the sill to the terrain, on squares of 0.25 m: a body keeps to the floor nearest its height within the step-up, stands with floor under its radius on
/// all four sides (so it keeps off a brink and to a flight's middle), and clears the shell and the physics-BSP statics over its walking band. The floors are the shell's and those statics' polygons that face up with a
/// normal z of at least <c>PhysicsGlobals.FloorZ</c> (the undersides, which <c>LaneClimbs</c> takes too, would step a body through the deck), and the terrain ends the search. A balcony has no such walk, so it keeps no
/// lane. Lifestone's own rule (no retail planner exists); the physics confirms every walk as before.</para>
/// <para>Named limits: a static with no physics BSP (a cylinder, a sphere: a chair on the deck) is not looked at here, and the stairs must lie within this landblock; a walk through either is the confirmer's to refuse.</para>
/// </remarks>
internal sealed class LaneStairs
{
    /// <summary>The search's square, in metres (as the lane's own sampling).</summary>
    private const float Sample = 0.25f;

    /// <summary>Two floors over the same square within this are one state of the search, in metres.</summary>
    private const float HeightBand = 0.25f;

    /// <summary>The squares of walk the step off a floor onto the ground costs per metre of step: a walk leaves a flight by its foot, not by its side at the limit of the step-up, unless the foot itself stands over the ground.</summary>
    private const float GroundStepCost = 20f;

    private static readonly (int X, int Y, float Cost)[] Steps =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.4142f), (1, -1, 1.4142f), (-1, 1, 1.4142f), (-1, -1, 1.4142f),
    ];

    private readonly LandblockCollision _block;
    private readonly NavTile _tile;
    private readonly NavBuildOptions _o;
    private readonly List<Surface> _surfaces = [];
    private readonly List<Solid> _solids = [];
    private readonly Dictionary<(int, int, int), bool> _stands = [];

    /// <summary>The floors and solids round <paramref name="building"/>, shared by its doors.</summary>
    /// <param name="block">The landblock's collision.</param>
    /// <param name="tile">The tile being built (its steep nodes refuse a foot).</param>
    /// <param name="building">The building.</param>
    /// <param name="o">The constants.</param>
    public LaneStairs(LandblockCollision block, NavTile tile, PhysBuilding building, NavBuildOptions o)
    {
        _block = block;
        _tile = tile;
        _o = o;
        Gather(building);
    }

    /// <summary>
    /// The walk from the ground in to <paramref name="sill"/>: its points, the lane's outer end on the ground first and the first point past the sill last, about a metre apart, with the nodes of the strip on
    /// the ground at its foot (the raised part has no nodes: the tile is the ground); null when no floor leads from the sill down to the terrain.
    /// </summary>
    /// <param name="sill">The portal's centre on its sill.</param>
    /// <param name="outward">The way out of the door.</param>
    public (IReadOnlyList<Vector3> Points, IReadOnlyList<int> Nodes)? Find(Vector3 sill, Vector2 outward) =>
        Search(sill, outward) is { Count: >= 3 } path ? Lane(path) : null;

    private void Gather(PhysBuilding building)
    {
        var bounds = building.Part.Shape.Bounds;
        var scale = building.Part.GfxObjScale;
        var size = Vector3.Max(Vector3.Abs(bounds.Min * scale), Vector3.Abs(bounds.Max * scale)).Length();
        var centre = new Vector2(building.Part.Position.Frame.Origin.X, building.Part.Position.Frame.Origin.Y);
        AddPart(building.Part, centre, size, always: true);
        foreach (var obj in _block.StaticObjects)
        {
            if ((obj.State & PhysicsState.HasPhysicsBsp) == 0 || !LandDefs.IsOutdoorCell(obj.Position.CellId))
            {
                continue;
            }

            foreach (var part in obj.Parts)
            {
                AddPart(part, centre, size, always: false);
            }
        }
    }

    private void AddPart(PhysPart part, Vector2 centre, float size, bool always)
    {
        if (part.Shape.Physics is not { Polygons.Count: > 0 } physics)
        {
            return;
        }

        var frame = part.Position.Frame;
        var scale = part.GfxObjScale;
        var bounds = part.Shape.Bounds;
        var reach = Vector3.Max(Vector3.Abs(bounds.Min * scale), Vector3.Abs(bounds.Max * scale)).Length();
        if (!always && Vector2.Distance(new Vector2(frame.Origin.X, frame.Origin.Y), centre) > _o.LaneStairReach + size + reach)
        {
            return;
        }

        _solids.Add(new Solid(part, new Vector2(frame.Origin.X, frame.Origin.Y), reach + _o.BodyRadius));
        foreach (var polygon in physics.Polygons)
        {
            var vertices = polygon.Vertices;
            if (vertices.Length < 3)
            {
                continue;
            }

            var corners = new Vector3[vertices.Length];
            var xy = new Vector2[vertices.Length];
            var box = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = frame.Origin + Vector3.Transform(vertices[i] * scale, frame.Orientation);
                xy[i] = new Vector2(corners[i].X, corners[i].Y);
                box = new Vector4(MathF.Min(box.X, xy[i].X), MathF.Min(box.Y, xy[i].Y), MathF.Max(box.Z, xy[i].X), MathF.Max(box.W, xy[i].Y));
            }

            var plane = PhysPolygon.MakePlane(corners);
            if (plane.Normal.Z >= PhysicsGlobals.FloorZ)
            {
                _surfaces.Add(new Surface(xy, plane, box));
            }
        }
    }

    /// <summary>The shortest walk from just past the sill to the terrain, the sill first, or null.</summary>
    private List<Vector3>? Search(Vector3 sill, Vector2 outward)
    {
        var origin = new Vector2(sill.X, sill.Y);
        var seed = Vector2.Zero;
        var height = float.NaN;
        var run = (int)MathF.Round(_o.LaneLength / Sample);
        for (var k = 1; k <= run && float.IsNaN(height); k++)
        {
            var point = origin + (outward * (k * Sample));
            if (FloorAt(point, sill.Z) is { Ground: false } floor && Stands(point, floor.Z))
            {
                (seed, height) = (point, floor.Z);
            }
        }

        if (float.IsNaN(height))
        {
            return null;
        }

        var side = new Vector2(-outward.Y, outward.X);
        var states = new List<State> { new(0, 0, height, -1, 0f, false) };
        var seen = new HashSet<(int, int, int)> { (0, 0, (int)MathF.Round(height / HeightBand)) };
        var open = new PriorityQueue<int, float>();
        open.Enqueue(0, 0f);
        var limit = _o.LaneStairReach / Sample;
        while (open.TryDequeue(out var index, out _))
        {
            var state = states[index];
            if (state.Ground)
            {
                return Path(states, index, seed, outward, side, sill);
            }

            foreach (var (dx, dy, cost) in Steps)
            {
                var total = state.Cost + cost;
                var (x, y) = (state.X + dx, state.Y + dy);
                if (total > limit)
                {
                    continue;
                }

                var point = seed + (outward * (x * Sample)) + (side * (y * Sample));
                if (FloorAt(point, state.Z) is not { } floor || !seen.Add((x, y, (int)MathF.Round(floor.Z / HeightBand))))
                {
                    continue;
                }

                if (!Stands(point, floor.Z))
                {
                    continue;
                }

                var cost2 = floor.Ground ? total + (MathF.Abs(floor.Z - state.Z) * GroundStepCost) : total;
                states.Add(new State(x, y, floor.Z, index, cost2, floor.Ground));
                open.Enqueue(states.Count - 1, cost2);
            }
        }

        return null;
    }

    private static List<Vector3> Path(List<State> states, int last, Vector2 seed, Vector2 outward, Vector2 side, Vector3 sill)
    {
        var path = new List<Vector3>();
        for (var i = last; i >= 0; i = states[i].Parent)
        {
            path.Add(new Vector3(seed + (outward * (states[i].X * Sample)) + (side * (states[i].Y * Sample)), states[i].Z));
        }

        path.Add(sill);
        path.Reverse();
        return path;
    }

    /// <summary>The lane of a walk (sill first, ground last): the corners pulled straight, a point about every metre, and the strip on the ground past the foot.</summary>
    private (IReadOnlyList<Vector3> Points, IReadOnlyList<int> Nodes)? Lane(List<Vector3> path)
    {
        var corners = new List<Vector3> { path[0], path[1] };
        var at = 1;
        while (at < path.Count - 1)
        {
            var next = at + 1;
            for (var j = path.Count - 1; j > at + 1; j--)
            {
                if (Clear(path[at], path[j]))
                {
                    next = j;
                    break;
                }
            }

            corners.Add(path[next]);
            at = next;
        }

        var foot = corners[^1];
        var away = new Vector2(foot.X, foot.Y) - new Vector2(corners[^2].X, corners[^2].Y);
        if (away.LengthSquared() < 1e-6f)
        {
            return null;
        }

        away = Vector2.Normalize(away);
        var points = new List<Vector3>();
        var walked = foot;
        for (var along = IndoorOutdoorRoute.LanePointMetres; along <= _o.LaneLength + 1e-3f; along += IndoorOutdoorRoute.LanePointMetres)
        {
            var point = new Vector2(foot.X, foot.Y) + (away * along);
            if (NavTileBuilder.TerrainZ(_block, point) is not { } z || MathF.Abs(z - walked.Z) > _o.StepUp || SteepAt(point) || !BodyClear(point, z))
            {
                break;
            }

            walked = new Vector3(point, z);
            points.Insert(0, walked);
        }

        var nodes = StripNodes(new Vector2(foot.X, foot.Y) - (away * Sample * 2f), new Vector2(walked.X, walked.Y), away);
        if (nodes.Count == 0)
        {
            return null;
        }

        for (var i = corners.Count - 1; i >= 1; i--)
        {
            AddLeg(points, corners[i], corners[i - 1]);
        }

        return (points, nodes);
    }

    /// <summary>Appends the leg from <paramref name="from"/> toward <paramref name="to"/>: its start and a point every metre, without its end (the next leg's start, or the sill, which the lane adds).</summary>
    private static void AddLeg(List<Vector3> points, Vector3 from, Vector3 to)
    {
        var length = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));
        var pieces = Math.Max(1, (int)MathF.Ceiling(length / IndoorOutdoorRoute.LanePointMetres));
        for (var k = 0; k < pieces; k++)
        {
            points.Add(Vector3.Lerp(from, to, k / (float)pieces));
        }
    }

    private List<int> StripNodes(Vector2 from, Vector2 to, Vector2 away)
    {
        var side = new Vector2(-away.Y, away.X);
        var nodes = new List<int>();
        var seen = new HashSet<int>();
        var length = Vector2.Distance(from, to);
        var across = (int)MathF.Floor((_o.LaneWidth * 0.5f / Sample) + 1e-4f);
        for (var k = 0; k * Sample <= length + 1e-3f; k++)
        {
            for (var j = -across; j <= across; j++)
            {
                var point = from + (away * (k * Sample)) + (side * (j * Sample));
                var (x, y) = NavTile.NodeAt(point);
                if (NavTile.InBounds(x, y) && seen.Add(NavTile.Index(x, y)))
                {
                    nodes.Add(NavTile.Index(x, y));
                }
            }
        }

        return nodes;
    }

    /// <summary>Whether a straight walk from <paramref name="from"/> to <paramref name="to"/> keeps to floors the body stands on all the way.</summary>
    private bool Clear(Vector3 from, Vector3 to)
    {
        var a = new Vector2(from.X, from.Y);
        var b = new Vector2(to.X, to.Y);
        var steps = (int)MathF.Ceiling(Vector2.Distance(a, b) / (Sample * 0.5f));
        var height = from.Z;
        for (var k = 1; k <= steps; k++)
        {
            var point = Vector2.Lerp(a, b, k / (float)steps);
            if (FloorAt(point, height) is not { } floor || !Stands(point, floor.Z))
            {
                return false;
            }

            height = floor.Z;
        }

        return MathF.Abs(height - to.Z) <= _o.StepUp;
    }

    /// <summary>Whether the body stands here: floor under its radius on all four sides, and clear of the solids.</summary>
    private bool Stands(Vector2 point, float z)
    {
        var key = ((int)MathF.Round(point.X * 16f), (int)MathF.Round(point.Y * 16f), (int)MathF.Round(z * 16f));
        if (_stands.TryGetValue(key, out var known))
        {
            return known;
        }

        var r = _o.BodyRadius;
        var stands = FloorAt(point + new Vector2(r, 0f), z) is not null
            && FloorAt(point - new Vector2(r, 0f), z) is not null
            && FloorAt(point + new Vector2(0f, r), z) is not null
            && FloorAt(point - new Vector2(0f, r), z) is not null
            && BodyClear(point, z);
        _stands[key] = stands;
        return stands;
    }

    /// <summary>The body's two spheres, over the step-up (a body steps over anything lower, and a flight's own slope stays under them) to the overhead clearance, against the solids.</summary>
    private bool BodyClear(Vector2 point, float z)
    {
        var r = _o.BodyRadius;
        var low = new Vector3(point, z + _o.StepUp + r);
        var high = new Vector3(point, MathF.Max(low.Z, z + _o.OverheadClearance - r));
        foreach (var solid in _solids)
        {
            if (Vector2.DistanceSquared(solid.Centre, point) <= solid.Reach * solid.Reach && (solid.Part.PlacementCollides(low, r) || solid.Part.PlacementCollides(high, r)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The floor over <paramref name="point"/> nearest <paramref name="height"/> within the step-up; the terrain counts (<c>Ground</c>) unless it is too steep to stand on.</summary>
    private (float Z, bool Ground)? FloorAt(Vector2 point, float height)
    {
        (float Z, bool Ground)? best = null;
        var reach = _o.StepUp + 1e-3f;
        foreach (var surface in _surfaces)
        {
            var box = surface.Box;
            if (point.X < box.X || point.X > box.Z || point.Y < box.Y || point.Y > box.W || !NavTileBuilder.InsidePolygon(surface.Xy, point.X, point.Y))
            {
                continue;
            }

            var plane = surface.Plane;
            var z = -((plane.Normal.X * point.X) + (plane.Normal.Y * point.Y) + plane.D) / plane.Normal.Z;
            if (MathF.Abs(z - height) <= reach && (best is null || MathF.Abs(z - height) < MathF.Abs(best.Value.Z - height)))
            {
                best = (z, false);
            }
        }

        if (NavTileBuilder.TerrainZ(_block, point) is { } ground && MathF.Abs(ground - height) <= reach && !SteepAt(point)
            && (best is null || MathF.Abs(ground - height) <= MathF.Abs(best.Value.Z - height)))
        {
            best = (ground, true);
        }

        return best;
    }

    private bool SteepAt(Vector2 point)
    {
        var (x, y) = NavTile.NodeAt(point);
        return !NavTile.InBounds(x, y) || (_tile.SlopeCost[NavTile.Index(x, y)] == 255 && (_tile.Flags[NavTile.Index(x, y)] & NavFlags.Slope) == 0);
    }

    private readonly record struct Surface(Vector2[] Xy, Plane Plane, Vector4 Box);

    private readonly record struct Solid(PhysPart Part, Vector2 Centre, float Reach);

    private readonly record struct State(int X, int Y, float Z, int Parent, float Cost, bool Ground);
}
