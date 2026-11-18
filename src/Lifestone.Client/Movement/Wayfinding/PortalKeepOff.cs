using System.Numerics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>A portal a walk keeps clear of: where it stands and what it is called.</summary>
/// <param name="Name">The portal's name for the log and a failure.</param>
/// <param name="Global">Its point, global metres (<see cref="NavPoint.Global"/>).</param>
/// <param name="Z">The height of its origin.</param>
/// <param name="Outdoor">True for a portal in an outdoor cell: the lattice's planner keeps off it too (<see cref="NavScars.SetKeepOff"/>); a portal in a room is the room graph's alone.</param>
/// <param name="Own">
/// M6 14 (portal overshoot): the walk's own portal, the one beside its goal that she must not enter yet (a hop's portal on the walk to its front point): kept off by <see cref="PortalKeepOff.OwnClearance"/>, its touch and her stop, not by the others'
/// clearance, so the front point stays reachable while no step of the plan passes through the portal.
/// </param>
public readonly record struct KeptPortal(string Name, Vector2 Global, float Z, bool Outdoor, bool Own = false);

/// <summary>
/// M6 7 AI: the portals a walk must not touch (gate A's climb, live: the walk to the Facility Hub's Tusker Lodge portal, level 15, ran 0.74 m past the Tusker Burrow portal, which took the body on touch; the hop failed by name and the route fell back
/// to an 807 m crossing of the island). Below a portal's level gate its touch does nothing, so no earlier run saw it: from level 15 every portal of the hub's ring can take a body on its way to a neighbour.
/// <para><b>What takes a body.</b> A portal's physics state is Ethereal with ReportCollisions (PropertyInt 93 = 3084 on every portal of the travel file), so ACE's sweep of the player reports the portal and walks on through it: <c>PhysicsObj.FindObjCollisions</c> and its <c>_Inner</c>
/// (@95ef426d Physics/PhysicsObj.cs:381-478), then <c>Player.OnCollideObject</c> (WorldObjects/Player.cs:368-376), <c>Portal.OnCollideObject</c> and <c>OnActivate</c> (WorldObjects/Portal.cs:103-106). The travel data's portal Setups (0x020001B3, 0x020005D2, 0x020005D3, 0x020005D5, 0x020005D6) have no BSP and no cylinder; the test is the
/// Setup's one sphere (radius 1.357, centre 1.42 over the origin, on all five) scaled by the object's DefaultScale (PropertyFloat 39: 0.8 in the hub and on the Refuge's Network portal, none, so 1, on the others) against the player's two spheres (Setup 0x02000001: radius 0.48). Seen from above the reach
/// is the two radii: <see cref="TouchMetres"/>, the unscaled portal's, 1.84 m (the hub's are 1.57 m).</para>
/// <para><b>The clearance.</b> <see cref="ClearanceMetres"/>, 3.14 m, is that reach and <see cref="WobbleMetres"/>, what a body strays from its plan's line: the walk is not a rail (the live log's corner swing at (87.06, -20.21)). The dat test walks the real body to every hub portal at three Run skills and pins the margin left.</para>
/// <para><b>The rule.</b> No straight step of a plan comes within the clearance of a kept portal at its storey (<see cref="FirstMeeting(Vector2, float, Vector2, float)"/>): the room graph goes round (<see cref="RoomGraph.KeepOff"/>), the lattice's planner reads the outdoor portals as blocked
/// discs (<see cref="NavScars.SetKeepOff"/>), the runner's straight run stays out of them, and a plan that cannot keep clear ends by name (<see cref="NavRunner.PortalKeepOffFailure"/>), never a silent brush. A step that starts inside a disc may leave it, moving outward (as a scar's): a body that arrived
/// beside a portal can walk away. The walk's own destination is left out of the list (<c>RungPortal.KeepOffTheOthers</c>). Not retail: Lifestone's own.</para>
/// <para><b>The own portal</b> (M6 14, live 2026-11-13 19:20 UTC: the walk to the Facility Hub's Tusker Barracks front, a point on the far side of the portal from the way the rooms came in, passed 0.72 m from the portal's centre; the server's sweep of her
/// reported positions crossed it and took her). A walk to a portal's front may name that portal as its own (<see cref="KeptPortal.Own"/>, <c>RungPortal.KeepOffForFront</c>): it is kept off by <see cref="OwnClearance"/>, the touch plus the runner's stop
/// (<see cref="NavRunner.StopMetres"/>, which the runner sets from the goal's tolerance at each plan: <see cref="WithOwnClearance"/>), so a goal outside that reach is never walked into, and no step of the plan crosses the portal on the way.</para>
/// </summary>
public sealed class PortalKeepOff
{
    /// <summary>The player's sphere radius (Setup 0x02000001, both spheres, read from the dats by the test).</summary>
    public const float BodyRadiusMetres = 0.48f;

    /// <summary>The largest portal sphere radius of the travel file (Setup 0x020001B3 at scale 1; the dat test checks every portal's Setup and scale against it).</summary>
    public const float PortalSphereMetres = 1.357f;

    /// <summary>How near, seen from above, the portal's sphere and the body's touch (see the class's summary).</summary>
    public const float TouchMetres = PortalSphereMetres + BodyRadiusMetres;

    /// <summary>
    /// What a running body strays from its plan's line, in metres. Measured on the dats (<c>HubPortalKeepOffDatTests</c>, the offline body under the real runner, 2026-11-06): the worst is the sharp turn beside a portal: with a clearance of 2.84 m the plans passed 3.11 m from their nearest portal and the body came to 1.86 m
    /// (the Tusker Habitat on the walk to the Abode, Run skill 450, 9 m/s), 1.25 m off the line; at Run skill 230, 2.03 m, 1.08 m off. 1.3 m covers that, and a clearance past 3.11 m refuses those corners, so the plans go wider: at this clearance the worst of the 24 walks stands 2.96 m off a portal, 1.13 m outside the largest touch. Not retail: Lifestone's own.
    /// </summary>
    public const float WobbleMetres = 1.3f;

    /// <summary>How far a plan's line keeps from a portal.</summary>
    public const float ClearanceMetres = TouchMetres + WobbleMetres;

    /// <summary>
    /// A portal counts for a step within this height of its origin. The portal's sphere reaches 2.8 m over its origin at scale 1 and a body stands 1.84 m tall, so a storey's 6 m (the hub's gallery over its portal hall) passes free. Not retail: Lifestone's own, <see cref="RoomPatches.StoreyMetres"/>'s.
    /// </summary>
    public const float StoreyMetres = RoomPatches.StoreyMetres;

    /// <summary>
    /// M6 14 (m6-14-plaza): the least clearance a kept portal is given where it shares a lane with a neighbour (<see cref="ClearanceOf"/>): its touch and a body's width to spare. Measured on the dats' Facility Hub: the Eastham Portal stands
    /// between the Banderling Conquest Dungeon and Mosswart Maze portals (5.58 m apart, the world SQL's placements), and the only way to it is the lane between them, 2.79 m from each. Not retail: Lifestone's own.
    /// </summary>
    public const float LaneFloorMetres = TouchMetres + BodyRadiusMetres;

    private readonly KeptPortal[] _portals;
    private readonly float[] _clearances;
    private readonly Dictionary<KeptPortal, float> _byPortal;
    private readonly (Vector2 Centre, float Radius)[] _outdoor;

    /// <summary>A list of <paramref name="portals"/>, each kept <paramref name="clearance"/> off (an own portal <paramref name="ownClearance"/>).</summary>
    /// <param name="portals">The portals.</param>
    /// <param name="clearance">The clearance in metres.</param>
    /// <param name="ownClearance">The own portal's reach in metres (<see cref="KeptPortal.Own"/>), or null: the touch alone until the runner sets the stop (<see cref="WithOwnClearance"/>).</param>
    public PortalKeepOff(IEnumerable<KeptPortal> portals, float clearance = ClearanceMetres, float? ownClearance = null)
    {
        ArgumentNullException.ThrowIfNull(portals);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clearance);
        _portals = [.. portals];
        Clearance = clearance;
        OwnClearance = ownClearance ?? TouchMetres;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OwnClearance);
        _clearances = Lanes(_portals, clearance, OwnClearance);
        _byPortal = new Dictionary<KeptPortal, float>(_portals.Length);
        for (var i = 0; i < _portals.Length; i++)
        {
            _byPortal.TryAdd(_portals[i], _clearances[i]);
        }

        _outdoor = [.. _portals.Select((p, i) => (p, i)).Where(t => t.p.Outdoor).Select(t => (t.p.Global, _clearances[t.i]))];
        _centres = [.. _portals.Select(p => p.Global)];
    }

    private readonly Vector2[] _centres;

    /// <summary>M6 14 (m6-14-plaza): a key equal for every list of the same portals at the same clearances in the same order (<see cref="SameAs"/>): the memo's detours of a list (<see cref="Lifestone.Client.Movement.Routes.CellRoutePlanner.Memo.KeptDetours"/>).</summary>
    public object ContentKey => _contentKey ??= new KeepKey(this);

    private KeepKey? _contentKey;

    private sealed class KeepKey(PortalKeepOff keep)
    {
        private readonly int _hash = Hash(keep);

        public override bool Equals(object? obj) => obj is KeepKey other && other._hash == _hash && keep.SameAs(other.Keep);

        public override int GetHashCode() => _hash;

        private PortalKeepOff Keep => keep;

        private static int Hash(PortalKeepOff keep)
        {
            var h = new HashCode();
            for (var i = 0; i < keep._portals.Length; i++)
            {
                h.Add(keep._portals[i]);
                h.Add(keep._clearances[i]);
            }

            return h.ToHashCode();
        }
    }

    /// <summary>M6 14 (m6-14-plaza): true when <paramref name="other"/> keeps the same portals at the same clearances in the same order (a list made again from the same records: the room graph keeps its detours).</summary>
    /// <param name="other">Another list, or null.</param>
    public bool SameAs(PortalKeepOff? other) =>
        other is not null && (ReferenceEquals(this, other) || (_portals.AsSpan().SequenceEqual(other._portals) && _clearances.AsSpan().SequenceEqual(other._clearances)));

    /// <summary>
    /// M6 14 (m6-14-plaza): each portal's clearance. The own portal's is <paramref name="own"/>; another's is <paramref name="clearance"/>, unless a neighbour of the list (not the own portal) stands at its storey so near that the two clearances
    /// close the lane between them: then each keeps half the gap less a body's radius, never less than <see cref="LaneFloorMetres"/> (the lane is shared, its middle a body's width from each disc).
    /// </summary>
    private static float[] Lanes(KeptPortal[] portals, float clearance, float own)
    {
        var result = new float[portals.Length];
        var cellSize = 2f * clearance;
        var grid = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < portals.Length; i++)
        {
            if (portals[i].Own)
            {
                continue;
            }

            var key = ((int)MathF.Floor(portals[i].Global.X / cellSize), (int)MathF.Floor(portals[i].Global.Y / cellSize));
            if (!grid.TryGetValue(key, out var list))
            {
                grid[key] = list = [];
            }

            list.Add(i);
        }

        for (var i = 0; i < portals.Length; i++)
        {
            var p = portals[i];
            if (p.Own)
            {
                result[i] = own;
                continue;
            }

            var gap = float.PositiveInfinity;
            var kx = (int)MathF.Floor(p.Global.X / cellSize);
            var ky = (int)MathF.Floor(p.Global.Y / cellSize);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue((kx + dx, ky + dy), out var near))
                    {
                        continue;
                    }

                    foreach (var j in near)
                    {
                        if (j != i && MathF.Abs(portals[j].Z - p.Z) <= StoreyMetres)
                        {
                            var d = Vector2.Distance(portals[j].Global, p.Global);
                            if (d > 1e-3f)
                            {
                                gap = MathF.Min(gap, d);
                            }
                        }
                    }
                }
            }

            result[i] = float.IsInfinity(gap) ? clearance : Math.Clamp((gap / 2f) - BodyRadiusMetres, MathF.Min(LaneFloorMetres, clearance), clearance);
        }

        return result;
    }

    /// <summary>The clearance in metres.</summary>
    public float Clearance { get; }

    /// <summary>M6 14: how far the walk's own portal (<see cref="KeptPortal.Own"/>) is kept off: its touch and her stop (<see cref="WithOwnClearance"/>).</summary>
    public float OwnClearance { get; }

    /// <summary>The own portal of the list, or null.</summary>
    public KeptPortal? Own => _portals.FirstOrDefault(p => p.Own) is { Own: true } own ? own : null;

    /// <summary>The clearance <paramref name="portal"/> is kept off by: <see cref="OwnClearance"/> for the own portal, else <see cref="Clearance"/>, or its share of a lane with a close neighbour (<see cref="LaneFloorMetres"/>; M6 14 m6-14-plaza).</summary>
    /// <param name="portal">A portal of the list.</param>
    public float ClearanceOf(in KeptPortal portal) => portal.Own ? OwnClearance : _byPortal.TryGetValue(portal, out var lane) ? lane : Clearance;

    /// <summary>M6 14: the reach the own portal is kept off by for a stop of <paramref name="stopMetres"/>: the touch (<see cref="TouchMetres"/>) plus the stop.</summary>
    /// <param name="stopMetres">How far she may still go past the moment the runner decides she arrived (<see cref="NavRunner.StopMetres"/>).</param>
    public static float OwnReach(float stopMetres) => TouchMetres + MathF.Max(0f, stopMetres);

    /// <summary>This list with the own portal kept <paramref name="ownClearance"/> off (the runner's, at each plan); the same list when it holds no own portal or the reach is unchanged.</summary>
    /// <param name="ownClearance">The own portal's reach in metres.</param>
    public PortalKeepOff WithOwnClearance(float ownClearance) =>
        Own is null || MathF.Abs(ownClearance - OwnClearance) < 1e-4f ? this : new PortalKeepOff(_portals, Clearance, ownClearance);

    /// <summary>The portals.</summary>
    public IReadOnlyList<KeptPortal> Portals => _portals;

    /// <summary>The discs of the portals in outdoor cells, for <see cref="NavScars.SetKeepOff"/>.</summary>
    public IReadOnlyList<(Vector2 Centre, float Radius)> OutdoorDiscs => _outdoor;

    /// <summary>
    /// The first portal the straight step from <paramref name="a"/> to <paramref name="b"/> (global metres, heights) comes within the clearance of, or null. A step that starts inside a disc and only leaves it does not meet it (a scar's rule).
    /// </summary>
    /// <param name="a">The step's start.</param>
    /// <param name="az">Its height.</param>
    /// <param name="b">The step's end.</param>
    /// <param name="bz">Its height.</param>
    public KeptPortal? FirstMeeting(Vector2 a, float az, Vector2 b, float bz)
    {
        // M6 14 (m6-14-plaza): with every placed portal on the lists (the Town Network's 49), a portal whose disc lies outside the step's box is passed by four comparisons before the exact test.
        var lo = Vector2.Min(a, b);
        var hi = Vector2.Max(a, b);
        for (var i = 0; i < _portals.Length; i++)
        {
            var c = _centres[i];
            var r = _clearances[i];
            if (c.X + r < lo.X || c.X - r > hi.X || c.Y + r < lo.Y || c.Y - r > hi.Y)
            {
                continue;
            }

            if (Meets(i, a, az, b, bz))
            {
                return _portals[i];
            }
        }

        return null;
    }

    /// <summary>Whether the step meets portal <paramref name="i"/>'s disc at its storey (a step that starts inside and only leaves it does not).</summary>
    private bool Meets(int i, Vector2 a, float az, Vector2 b, float bz)
    {
        var p = _portals[i];
        var radius = _clearances[i];
        var ab = b - a;
        var length2 = ab.LengthSquared();
        var t = length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p.Global - a, ab) / length2, 0f, 1f);
        return Vector2.DistanceSquared(p.Global, a + (ab * t)) < radius * radius && MathF.Abs(float.Lerp(az, bz, t) - p.Z) <= StoreyMetres && t > 0f; // t == 0: the nearest point is the start, the step only leaves the disc
    }

    /// <summary>The first portal the whole line of <paramref name="path"/> comes within the clearance of, and the leg it does it on; null when it stays clear.</summary>
    /// <param name="path">The waypoints.</param>
    public (KeptPortal Portal, int Leg)? FirstMeeting(IReadOnlyList<NavWaypoint> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1].Point;
            var b = path[i].Point;
            if (FirstMeeting(a.Global, a.Z, b.Global, b.Z) is { } met)
            {
                return (met, i);
            }
        }

        return null;
    }

    /// <summary>The nearest any leg of <paramref name="path"/> comes to a portal at its storey (the own portal left out: its own overload), for the log and the test; null when no leg stands at a portal's storey.</summary>
    /// <param name="path">The waypoints.</param>
    public (float Metres, KeptPortal Portal)? NearestOnLine(IReadOnlyList<NavWaypoint> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        (float Metres, KeptPortal Portal)? best = null;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1].Point;
            var b = path[i].Point;
            var ab = b.Global - a.Global;
            var length2 = ab.LengthSquared();
            foreach (var p in _portals)
            {
                var t = length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p.Global - a.Global, ab) / length2, 0f, 1f);
                var metres = Vector2.Distance(p.Global, a.Global + (ab * t));
                if (!p.Own && MathF.Abs(float.Lerp(a.Z, b.Z, t) - p.Z) <= StoreyMetres && (best is not { } known || metres < known.Metres))
                {
                    best = (metres, p);
                }
            }
        }

        return best;
    }

    /// <summary>The nearest the line of <paramref name="path"/> comes to <paramref name="portal"/> at its storey, in metres, or null when no leg stands at its storey (M6 14: the own portal's line, logged).</summary>
    /// <param name="path">The waypoints.</param>
    /// <param name="portal">The portal.</param>
    public float? NearestOnLine(IReadOnlyList<NavWaypoint> path, in KeptPortal portal)
    {
        ArgumentNullException.ThrowIfNull(path);
        float? best = null;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1].Point;
            var b = path[i].Point;
            var ab = b.Global - a.Global;
            var length2 = ab.LengthSquared();
            var t = length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(portal.Global - a.Global, ab) / length2, 0f, 1f);
            var metres = Vector2.Distance(portal.Global, a.Global + (ab * t));
            if (MathF.Abs(float.Lerp(a.Z, b.Z, t) - portal.Z) <= StoreyMetres && (best is not { } known || metres < known))
            {
                best = metres;
            }
        }

        return best;
    }

    /// <summary>The nearest of the portals to a body at <paramref name="at"/> at its storey, the own portal left out (its goal stands beside it): the planar metres and the portal; null when none stands at that height.</summary>
    /// <param name="at">The body, global metres.</param>
    /// <param name="z">Its height.</param>
    public (float Metres, KeptPortal Portal)? Nearest(Vector2 at, float z)
    {
        (float Metres, KeptPortal Portal)? best = null;
        foreach (var p in _portals)
        {
            var metres = Vector2.Distance(p.Global, at);
            if (!p.Own && MathF.Abs(z - p.Z) <= StoreyMetres && (best is not { } b || metres < b.Metres))
            {
                best = (metres, p);
            }
        }

        return best;
    }

    /// <summary>This list without the portals further than <paramref name="metres"/> from the straight line from <paramref name="a"/> to <paramref name="b"/> (a plan's blocked discs cost it time on every step, so a walk carries the ones it can meet).</summary>
    /// <param name="a">The walk's start, global metres.</param>
    /// <param name="b">Its goal.</param>
    /// <param name="metres">The reach.</param>
    public PortalKeepOff Near(Vector2 a, Vector2 b, float metres)
    {
        var ab = b - a;
        var length2 = ab.LengthSquared();
        return new PortalKeepOff(_portals.Where(p => p.Own || Vector2.Distance(p.Global, a + (ab * (length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p.Global - a, ab) / length2, 0f, 1f)))) <= metres), Clearance, OwnClearance);
    }
}
