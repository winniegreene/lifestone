using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Routes;

/// <summary>
/// The DoD 8 scenarios' routes (m2-plan PH9): three laps of a ring around Holtburg's lifestone (owner decision D47: the
/// ring is generated at run time and validated against the offline collision) and the walk from the Training Academy's
/// spawn to its exit portal.
/// </summary>
public static class RouteLibrary
{
    /// <summary>The Holtburg route's name (the movement summary line's <c>route=</c>).</summary>
    public const string HoltburgLapsName = "holtburg-laps";

    /// <summary>The Academy route's name.</summary>
    public const string AcademyExitName = "academy-exit";

    /// <summary>The laps the Holtburg scenario walks.</summary>
    public const int DefaultLaps = 3;

    /// <summary>The points of the Holtburg ring (m2-plan PH9: 12).</summary>
    public const int RingPoints = 12;

    /// <summary>How long the scenarios stand at a stop before checking where-am-I (the 1.0 s cadence has sent the rest position).</summary>
    public const double DwellSeconds = 1.5;

    /// <summary>The arrival radius of a point the route walks through without stopping.</summary>
    public const float PassTolerance = 1.0f;

    /// <summary>The arrival radius of a stop.</summary>
    public const float StopTolerance = 0.5f;

    /// <summary>The radii the Holtburg ring tries, smallest first.</summary>
    public static IReadOnlyList<float> RingRadii { get; } = [8f, 10f, 12f, 14f, 16f, 18f, 20f, 24f, 28f];

    /// <summary>How far apart the ring's segments are tested.</summary>
    public const float RingSampleMetres = 0.25f;

    /// <summary>The largest height step between two tested points of a ring segment.</summary>
    public const float RingMaxRise = 0.3f;

    /// <summary>
    /// Three laps (by default) of a 12-point ring around <paramref name="lifestone"/>: the smallest radius of
    /// <see cref="RingRadii"/> whose ring <paramref name="body"/> can walk on the collision attached to
    /// <paramref name="world"/> (D47). Each lap walks through points 1 to 11 and stops at point 0, its start
    /// (<see cref="ScriptedRoute.Start"/>), for <see cref="DwellSeconds"/>.
    /// </summary>
    /// <remarks>
    /// A ring passes when every point <see cref="RingSampleMetres"/> along it has walkable ground
    /// (<see cref="PhysicsWorld.TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/> from the terrain's height),
    /// rises or falls no more than <see cref="RingMaxRise"/> from the one before, is outdoors, and has room for the body
    /// (a placement there, which must not move it). The test object it places is a player (so other players do not block
    /// it) and is destroyed before this returns. Both probes judge the fixed world only (M5 item 5 fix F3,
    /// <see cref="PhysicsWorld.TryFixedGroundZ"/> and <see cref="PhysicsObj.FixedWorldOnly"/>): the lifestone and passers-by the
    /// server-object host keeps in the world are met by the walk, not by the ring's choice (not retail: Lifestone's own tool).
    /// </remarks>
    /// <exception cref="InvalidOperationException">No radius gives a clear ring; the message names the last obstacle.</exception>
    public static ScriptedRoute HoltburgLaps(in CellFrame lifestone, PhysicsWorld world, PhysicsSetup body, int laps = DefaultLaps)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThan(laps, 1);
        if (body.Spheres.Count == 0)
        {
            throw new ArgumentException("the body has no spheres to stand on", nameof(body));
        }

        var probe = new PhysicsObj(world, body, id: 0, new ProbeWeenie()) { FixedWorldOnly = true }; // F3: the ring is judged on the fixed world, not the lifestone or a passer-by
        try
        {
            string? why = null;
            foreach (var radius in RingRadii)
            {
                if (TryRing(lifestone, world, body, probe, radius, out var points, out why))
                {
                    var goals = new RouteGoal[RingPoints];
                    for (var i = 1; i < RingPoints; i++)
                    {
                        goals[i - 1] = new RouteGoal(points[i], PassTolerance, Stop: false, DwellSeconds: 0);
                    }

                    goals[RingPoints - 1] = new RouteGoal(points[0], StopTolerance, Stop: true, DwellSeconds);
                    return new ScriptedRoute(HoltburgLapsName, goals, laps) { Start = points[0] };
                }
            }

            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"No clear {RingPoints}-point ring around {lifestone} for radii {RingRadii[0]}-{RingRadii[^1]} m: {why}"));
        }
        finally
        {
            probe.Destroy();
        }
    }

    /// <summary>
    /// The walk from <paramref name="spawn"/> (the Training Academy's start, 0x860201AD) to <paramref name="exitPortal"/>
    /// through the landblock's rooms (<see cref="CellRoutePlanner"/>), stopping within <paramref name="stopWithin"/> of the
    /// portal for <see cref="DwellSeconds"/> (the scenario then checks where-am-I and uses it).
    /// </summary>
    /// <exception cref="InvalidOperationException">No walkable doorways join the two cells.</exception>
    public static ScriptedRoute AcademyExit(in CellFrame spawn, in CellFrame exitPortal, LandblockCellSet cells, float stopWithin)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stopWithin);
        var planner = new CellRoutePlanner(cells);
        var route = planner.Plan(spawn.CellId, exitPortal.CellId)
            ?? throw new InvalidOperationException(
                $"No walkable doorways lead from cell 0x{spawn.CellId:X8} to the exit portal's cell 0x{exitPortal.CellId:X8} in landblock {cells.Coord}.");
        var goals = new List<RouteGoal>(route.Waypoints.Count + 1);
        foreach (var waypoint in route.Waypoints)
        {
            goals.Add(new RouteGoal(waypoint, CellRoutePlanner.PushIntoCellMetres, Stop: false, DwellSeconds: 0));
        }

        goals.Add(new RouteGoal(exitPortal, stopWithin, Stop: true, DwellSeconds));
        return new ScriptedRoute(AcademyExitName, goals) { Start = spawn };
    }

    /// <summary>Tests the ring of <paramref name="radius"/>; its points when every segment is clear.</summary>
    private static bool TryRing(in CellFrame centre, PhysicsWorld world, PhysicsSetup body, PhysicsObj probe, float radius, out CellFrame[] points, out string? why)
    {
        points = new CellFrame[RingPoints];
        why = null;
        var foot = body.Spheres[0];
        float? previousZ = null;
        for (var i = 0; i <= RingPoints; i++)
        {
            var from = Offset(i % RingPoints, radius);
            var to = Offset((i + 1) % RingPoints, radius);
            var length = Vector2.Distance(from, to);
            var samples = i == RingPoints ? 1 : Math.Max(1, (int)MathF.Ceiling(length / RingSampleMetres));
            for (var s = 0; s < samples; s++)
            {
                var xy = Vector2.Lerp(from, to, s / (float)samples);
                if (!TryStand(centre, world, foot, probe, xy, previousZ, out var standing, out why))
                {
                    why = string.Create(CultureInfo.InvariantCulture, $"radius {radius} m: {why}");
                    return false;
                }

                previousZ = standing.Frame.Origin.Z;
                if (s == 0 && i < RingPoints)
                {
                    points[i] = standing;
                }
            }
        }

        return true;
    }

    /// <summary>The ring's point <paramref name="index"/> as an offset from the centre: point 0 due north, then clockwise.</summary>
    private static Vector2 Offset(int index, float radius)
    {
        var angle = index * (2.0 * Math.PI / RingPoints);
        return new Vector2((float)(Math.Sin(angle) * radius), (float)(Math.Cos(angle) * radius));
    }

    /// <summary>Where the body stands at <paramref name="xy"/> from the centre, if it can.</summary>
    private static bool TryStand(in CellFrame centre, PhysicsWorld world, PhysSphere foot, PhysicsObj probe, Vector2 xy, float? previousZ, out CellFrame standing, out string? why)
    {
        standing = default;
        var cellId = centre.CellId;
        var origin = centre.Frame.Origin + new Vector3(xy.X, xy.Y, 0f);
        if (!LandDefs.AdjustToOutside(ref cellId, ref origin) || world.GetCell(cellId) is not PhysLandCell land)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"no attached outdoor cell at {origin} of {centre}");
            return false;
        }

        if (land.FindTerrainPoly(origin) is not { } terrain || MathF.Abs(terrain.Plane.Normal.Z) < 1e-4f)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"no terrain under {origin} in cell 0x{cellId:X8}");
            return false;
        }

        var plane = terrain.Plane;
        origin.Z = -((plane.Normal.X * origin.X) + (plane.Normal.Y * origin.Y) + plane.D) / plane.Normal.Z;
        var at = new CellFrame(cellId, new AcFrame(origin, Quaternion.Identity));
        if (!world.TryFixedGroundZ(at, foot, out var z, out _))
        {
            why = string.Create(CultureInfo.InvariantCulture, $"no walkable ground at {at}");
            return false;
        }

        if (previousZ is { } before && MathF.Abs(z - before) > RingMaxRise)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"a step of {z - before:F2} m at {at}");
            return false;
        }

        at = new CellFrame(cellId, new AcFrame(origin with { Z = z }, Quaternion.Identity));
        if (probe.SetPosition(at, SetPositionFlags.Placement) != SetPositionError.OK || probe.Cell is null
            || !LandDefs.IsOutdoorCell(probe.Position.CellId))
        {
            why = string.Create(CultureInfo.InvariantCulture, $"no room to stand at {at}");
            return false;
        }

        standing = new CellFrame(probe.Position.CellId, new AcFrame(probe.Position.Frame.Origin, Quaternion.Identity));
        why = null;
        return true;
    }

    /// <summary>The ring test's body: a player, not a player killer, so other players do not block it.</summary>
    private sealed class ProbeWeenie : IPhysicsWeenie
    {
        public bool IsCreature => true;

        public bool IsThePlayer => false;

        public bool IsPlayer => true;

        public bool IsPK => false;

        public bool IsPKLite => false;

        public bool IsImpenetrable => false;

        public bool TryGetRunRate(out float rate)
        {
            rate = 1f;
            return false;
        }

        public bool TryGetJumpVelocity(float extent, out float velocityZ)
        {
            velocityZ = 0f;
            return false;
        }

        public bool CanJump(float extent) => false;

        public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
        {
            staminaCost = 0;
            return false;
        }
    }
}
