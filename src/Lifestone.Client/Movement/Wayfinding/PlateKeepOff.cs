using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>A pressure plate a walk keeps off (M6 14 holding plates): its name, cell and landblock-local origin (the profile's facts), and its shape's footprint and top (read from its Setup in the dats: <see cref="PlateKeepOff.ShapeOf(DatSet, uint)"/>).</summary>
/// <param name="Name">The plate's name for the log and a failure.</param>
/// <param name="Cell">The cell it stands in.</param>
/// <param name="Point">Its origin, landblock-local.</param>
/// <param name="ShapeMetres">How far its collision shape reaches from its origin, seen from above.</param>
/// <param name="TopMetres">How high over its origin her feet must be to pass over its shape.</param>
public readonly record struct KeptPlate(string Name, uint Cell, Vector3 Point, float ShapeMetres, float TopMetres);

/// <summary>
/// M6 14 holding plates (the owner, 2026-11-14: in the Tusker Holding the climber must avoid the Tusker's Friend pressure plates, and where one blocks the way, she can jump): the plates a walk never crosses. A wall, not a cost: no straight step
/// of a plan comes within a plate's reach (<see cref="ReachOf"/>) at its storey, so the room graph goes round (<see cref="RoomGraph.PlateList"/>) or jumps it (<see cref="Routes.PlateJumps"/>), and with neither the plan is null and the walk names
/// the plate. A step that starts inside a reach and only leaves it moves outward (a scar's rule, <see cref="PortalKeepOff"/>'s): a body that stands too near can walk away.
/// </summary>
/// <remarks>
/// <para><b>What fires a plate</b> (ACE @95ef426d): <c>PressurePlate.OnCollideObject</c> activates it for a player (PressurePlate.cs:52-94: players only, at most once in 2 s, then its reset interval), and the collision is the server's
/// sweep of her two spheres against the plate's shape (<c>PhysicsObj.FindObjCollisions</c>, PhysicsObj.cs:381-454: an ethereal object is reported and passed through, except in the step-down), between the positions she reports
/// (<c>Player.UpdatePlayerPosition</c>, Player_Tick.cs:411-479; <c>UpdateObjectInternalServer</c>, PhysicsObj.cs:1766-1781) and along the server's own flight of a jump (<c>Player.HandleActionJump</c>, Player.cs:866-935;
/// <c>UpdateObjectPhysics</c>, Player_Tick.cs:303-313). The shape is the plate's own Setup: ACE gives a non-creature object the physics of its Setup (<c>WorldObject.InitPhysicsObj</c>, WorldObject.cs:139-160), and tests a body against
/// its parts' physics BSP when the Setup has one, its spheres only when it has none (PhysicsObj.cs:412-447), so <see cref="ShapeOf(PhysicsSetup, PhysicsSetup)"/> reads that shape from the dats where the walls are made, never from a
/// typed number. The Tusker Holding's plate 22719 is Setup 0x02000450 (the world SQL's <c>weenie_properties_d_i_d</c> type 1, the Setup of all 68 PressurePlates of the world that use it): flag 0x8 set, one part 0x0100125E whose physics BSP
/// is a closed box 2.258 m square from 0.005 to 0.075 m over the plate's origin (six polygons), and a sphere of 1.125 m at 1.125 m that ACE therefore never tests; her Setup 0x02000001 is two spheres of 0.48 m at 0.475 and 1.35 m (the dats).
/// The plates stand at several headings (the world SQL's angles), so the footprint is the box's half-diagonal, 1.596 m, and the top 0.08 m (her lower sphere's bottom 0.005 m under her feet). Pinned by <c>HoldingPlatesTests</c>.</para>
/// <para><b>The reach.</b> The plate's footprint (<see cref="KeptPlate.ShapeMetres"/>), her body's radius (<see cref="BodyRadiusMetres"/>) and her stop (<see cref="StopMetres"/>): how far she carries past
/// the line of her plan at her live speed, the runner's own brake: one physics step and the coast (<see cref="StopAt"/>), as the own portal's reach adds her stop (<see cref="PortalKeepOff.OwnReach"/>). The runner sets the
/// speed from her live Run skill at each plan (<see cref="RoomGraph.PlateSpeedMetresPerSecond"/>). The footprint and her radius make a touch that stands for every heading of the plate and every height of her body (a cylinder round the
/// shape): 2.076 m for the Holding's box, where her lower sphere, her feet at the plate's origin, meets it 0.265 m past its edge (1.394 m off its centre on a side, 1.862 m at a corner), on the safe side. Not retail: Lifestone's own.</para>
/// </remarks>
public sealed class PlateKeepOff
{
    /// <summary>Her sphere radius (Setup 0x02000001, both spheres; <see cref="PortalKeepOff.BodyRadiusMetres"/>).</summary>
    public const float BodyRadiusMetres = PortalKeepOff.BodyRadiusMetres;

    /// <summary>A plate counts for a step within this height of its origin (<see cref="RoomPatches.StoreyMetres"/>: the Holding's lower level is 12 m under its upper).</summary>
    public const float StoreyMetres = RoomPatches.StoreyMetres;

    /// <summary>
    /// The speed the stop is taken at when the body cannot tell its own (no character data yet), metres a second: retail's run tends to a rate of 3.75 below Run skill 800 (<c>GetRunRate</c>: (11 skill / (skill + 200) + 4) / 4), so 15 m/s
    /// is no body's run, and the walls it makes are wider than any body's. Not retail: Lifestone's own.
    /// </summary>
    public const float UnknownSpeedMetresPerSecond = 15f;

    /// <summary>The shapes read so far, by dat set and Setup (<see cref="ShapeOf(DatSet, uint)"/>).</summary>
    private static readonly ConditionalWeakTable<DatSet, Dictionary<uint, (float, float)>> Shapes = new();

    private readonly KeptPlate[] _plates;

    /// <summary>The plates <paramref name="plates"/>, each kept its reach off at a stop of <paramref name="stopMetres"/>.</summary>
    /// <param name="plates">The plates.</param>
    /// <param name="stopMetres">Her stop at her live speed (<see cref="StopAt"/>).</param>
    public PlateKeepOff(IEnumerable<KeptPlate> plates, float stopMetres)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentOutOfRangeException.ThrowIfNegative(stopMetres);
        _plates = [.. plates];
        StopMetres = stopMetres;
    }

    /// <summary>The plates.</summary>
    public IReadOnlyList<KeptPlate> Plates => _plates;

    /// <summary>Her stop at the speed the list was made for, in metres.</summary>
    public float StopMetres { get; }

    /// <summary>
    /// How far a body running at <paramref name="speed"/> carries past the line of its plan, in metres: one physics step (<see cref="PhysicsGlobals.MinQuantum"/>) before released keys act, then the coast the runner brakes on
    /// (<see cref="NavRunner.CornerStopMetres"/>, or <see cref="NavRunner.CornerBrakeSeconds"/> of the speed): <see cref="NavRunner.StopMetres"/>'s rule at her live speed. 1.03 m at the buffed climber's 7.74 m/s, 0.50 m at a walk.
    /// </summary>
    /// <param name="speed">Her run speed, metres a second.</param>
    public static float StopAt(float speed)
    {
        var v = MathF.Max(0f, speed);
        return (v * (float)PhysicsGlobals.MinQuantum) + MathF.Max(NavRunner.CornerStopMetres, NavRunner.CornerBrakeSeconds * v);
    }

    /// <summary>
    /// The footprint and top of a plate whose Setup is <paramref name="plate"/>, for a body whose Setup is <paramref name="body"/>, on the shape ACE collides her with (<c>PhysicsObj.FindObjCollisions</c>, PhysicsObj.cs:412-447 @95ef426d): the
    /// physics BSP of its parts when it has one (its state's HasPhysicsBSP, from the Setup's flag 0x8: <c>CalculatedPhysicsState</c>, WorldObject_Networking.cs:665-669; its Setup's sphere is then never tested), else its spheres. The
    /// footprint is the farthest the shape reaches from the plate's origin seen from above, whatever its heading (a polygon's corner at its part's rest frame and scale; a sphere's offset and radius); the top, the height of her feet
    /// over its origin at which her lowest sphere passes over the shape's highest point. The Setups are the dats'.
    /// </summary>
    /// <param name="plate">The plate's Setup.</param>
    /// <param name="body">Her Setup (0x02000001).</param>
    /// <exception cref="NotSupportedException">The shape ACE would test is cylinders, which this model does not read (<c>TODO(m6-plate-shape)</c>), or there is none.</exception>
    public static (float ShapeMetres, float TopMetres) ShapeOf(PhysicsSetup plate, PhysicsSetup body)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(body);
        if (body.Spheres.Count == 0)
        {
            throw new NotSupportedException(string.Create(CultureInfo.InvariantCulture, $"TODO(m6-plate-shape): her Setup 0x{body.Id:X8} has no sphere"));
        }

        var lowest = body.Spheres.Min(s => s.Center.Z - s.Radius);
        if (plate.HasPhysicsBsp)
        {
            var (shape, top) = (0f, float.NegativeInfinity);
            foreach (var part in plate.Parts)
            {
                foreach (var polygon in part.Shape.Physics?.Polygons ?? [])
                {
                    foreach (var v in polygon.Vertices)
                    {
                        var at = Vector3.Transform(v * part.DefaultScale, part.RestFrame.Orientation) + part.RestFrame.Origin;
                        (shape, top) = (MathF.Max(shape, new Vector2(at.X, at.Y).Length()), MathF.Max(top, at.Z));
                    }
                }
            }

            return float.IsNegativeInfinity(top)
                ? throw new NotSupportedException(string.Create(CultureInfo.InvariantCulture, $"TODO(m6-plate-shape): the plate's Setup 0x{plate.Id:X8} has a physics BSP and no physics polygon"))
                : (shape, top - lowest);
        }

        if (plate.CylSpheres.Count > 0 || plate.Spheres.Count == 0)
        {
            throw new NotSupportedException(string.Create(
                CultureInfo.InvariantCulture,
                $"TODO(m6-plate-shape): the plate's Setup 0x{plate.Id:X8} has no physics BSP, {plate.CylSpheres.Count} cylinders and {plate.Spheres.Count} spheres; the plate walls read a BSP or spheres"));
        }

        return (plate.Spheres.Max(s => new Vector2(s.Center.X, s.Center.Y).Length() + s.Radius), plate.Spheres.Max(s => s.Center.Z + s.Radius) - lowest);
    }

    /// <summary>The footprint and top (<see cref="ShapeOf(PhysicsSetup, PhysicsSetup)"/>) of the plate whose Setup is <paramref name="setupId"/> in <paramref name="dats"/>, for her body (Setup 0x02000001); read once a dat set.</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="setupId">The plate's Setup (the world SQL's <c>weenie_properties_d_i_d</c> type 1 of its weenie).</param>
    /// <exception cref="InvalidDataException">The Setup, or hers, is not in the dats.</exception>
    public static (float ShapeMetres, float TopMetres) ShapeOf(DatSet dats, uint setupId)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var known = Shapes.GetOrCreateValue(dats);
        lock (known)
        {
            if (known.TryGetValue(setupId, out var found))
            {
                return found;
            }

            var plate = PhysicsSetup.Load(dats, setupId) ?? throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"the plate's Setup 0x{setupId:X8} is not in the dats"));
            var body = PhysicsSetup.Load(dats, OfflineWalker.HumanSetupId) ?? throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"her Setup 0x{OfflineWalker.HumanSetupId:X8} is not in the dats"));
            return known[setupId] = ShapeOf(plate, body);
        }
    }

    /// <summary>How near, seen from above, her body and <paramref name="plate"/>'s shape touch: the footprint and her radius.</summary>
    /// <param name="plate">A plate.</param>
    public static float TouchOf(in KeptPlate plate) => plate.ShapeMetres + BodyRadiusMetres;

    /// <summary>The wall round <paramref name="plate"/>: the touch and her stop.</summary>
    /// <param name="plate">A plate.</param>
    public float ReachOf(in KeptPlate plate) => TouchOf(plate) + StopMetres;

    /// <summary>The widest reach of the list (0 for none).</summary>
    public float MaxReach => _plates.Length == 0 ? 0f : _plates.Max(p => ReachOf(p));

    /// <summary>This list at a stop of <paramref name="stopMetres"/>; the same list when the stop is unchanged.</summary>
    /// <param name="stopMetres">The stop in metres.</param>
    public PlateKeepOff WithStop(float stopMetres) => MathF.Abs(stopMetres - StopMetres) < 1e-4f ? this : new PlateKeepOff(_plates, stopMetres);

    /// <summary>The first plate the straight step from <paramref name="a"/> to <paramref name="b"/> (landblock-local) comes within the reach of at its storey, or null; a step that starts inside a reach and only leaves it meets nothing.</summary>
    /// <param name="a">The step's start.</param>
    /// <param name="b">Its end.</param>
    public KeptPlate? FirstMeeting(Vector3 a, Vector3 b)
    {
        var a2 = new Vector2(a.X, a.Y);
        var ab = new Vector2(b.X, b.Y) - a2;
        var length2 = ab.LengthSquared();
        foreach (var p in _plates)
        {
            var at = new Vector2(p.Point.X, p.Point.Y);
            var t = length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(at - a2, ab) / length2, 0f, 1f);
            var reach = ReachOf(p);
            if (Vector2.DistanceSquared(at, a2 + (ab * t)) >= reach * reach || MathF.Abs(float.Lerp(a.Z, b.Z, t) - p.Point.Z) > StoreyMetres)
            {
                continue;
            }

            if (t > 0f)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>The plate whose reach holds <paramref name="point"/> (landblock-local) at its storey, or null.</summary>
    /// <param name="point">The point.</param>
    public KeptPlate? Holding(Vector3 point)
    {
        foreach (var p in _plates)
        {
            var reach = ReachOf(p);
            if (MathF.Abs(point.Z - p.Point.Z) <= StoreyMetres && Vector2.DistanceSquared(new Vector2(point.X, point.Y), new Vector2(p.Point.X, p.Point.Y)) < reach * reach)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>The nearest plate to <paramref name="point"/> (landblock-local) at its storey: the planar metres from its origin and the plate; null when none stands at that height.</summary>
    /// <param name="point">The point.</param>
    public (float Metres, KeptPlate Plate)? Nearest(Vector3 point)
    {
        (float Metres, KeptPlate Plate)? best = null;
        foreach (var p in _plates)
        {
            var metres = Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(p.Point.X, p.Point.Y));
            if (MathF.Abs(point.Z - p.Point.Z) <= StoreyMetres && (best is not { } b || metres < b.Metres))
            {
                best = (metres, p);
            }
        }

        return best;
    }

    /// <summary>
    /// The first plate the line of <paramref name="path"/> comes within the reach of, and the leg it does it on, its jumps' flights left out (a jump passes over the plates: <see cref="Routes.PlateJumps"/>); null when it stays clear. Points outside
    /// <paramref name="landblockId"/> are not this list's.
    /// </summary>
    /// <param name="path">The waypoints.</param>
    /// <param name="landblockId">The plates' landblock (its id, low word 0).</param>
    public (KeptPlate Plate, int Leg)? FirstMeeting(IReadOnlyList<NavWaypoint> path, uint landblockId)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1].Point;
            var b = path[i].Point;
            if (path[i - 1].Jump is not null || a.LandblockId != landblockId || b.LandblockId != landblockId)
            {
                continue; // the flight from a take-off to its landing
            }

            if (FirstMeeting(a.Local, b.Local) is { } met)
            {
                return (met, i);
            }
        }

        return null;
    }

    /// <summary>The list for the log: each plate's name and reach.</summary>
    public override string ToString() => string.Join("; ", _plates.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} [{p.Point.X:0.#} {p.Point.Y:0.#}] reach {ReachOf(p):0.00} m")));
}
