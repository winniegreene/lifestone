using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>Where a <see cref="RouteConfirmer"/> stands.</summary>
public enum RouteConfirmStatus
{
    /// <summary>No path given yet.</summary>
    Idle,

    /// <summary>Part of the path is confirmed; call <see cref="RouteConfirmer.Continue"/> again.</summary>
    Pending,

    /// <summary>Every sample of the path stood on walkable ground and every placement fitted.</summary>
    Confirmed,

    /// <summary>A sample failed (<see cref="RouteConfirmer.Failure"/>).</summary>
    Failed,
}

/// <summary>The first sample of a path the physics refused.</summary>
/// <param name="Segment">The path segment (from waypoint <c>Segment</c> to <c>Segment + 1</c>).</param>
/// <param name="At">Where the sample was taken (its cell; z the previous sample's ground).</param>
/// <param name="ArcMetres">How far along the path, in metres.</param>
/// <param name="Probe">Which probe refused it: "ground" (no walkable ground within the step window) or "placement" (no room to stand).</param>
/// <param name="Why">What it found.</param>
public readonly record struct RouteConfirmFailure(int Segment, CellFrame At, float ArcMetres, string Probe, string Why)
{
    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Probe} probe at {ArcMetres:F1} m (segment {Segment}, cell 0x{At.CellId:X8} ({At.Origin.X:F2}, {At.Origin.Y:F2}, {At.Origin.Z:F2})): {Why}");
}

/// <summary>
/// The physics half of the wayfinder's confirmation (P5a design 3.1: "the map proposes, the physics confirms"): along a planned path,
/// every <see cref="SampleMetres"/> the real ground probe (<see cref="PhysicsWorld.TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/>,
/// retail's <c>CTransition::step_down</c>) from the previous sample's height, whose window of <see cref="PhysicsWorld.GroundProbeRise"/>
/// up and <see cref="PhysicsWorld.GroundProbeDrop"/> down is the step rule, and every <see cref="PlacementMetres"/> a placement of the
/// player's body (<see cref="PhysicsObj.SetPosition"/> with <see cref="SetPositionFlags.Placement"/>, the pattern of
/// <c>RouteLibrary.TryStand</c>). Indoor legs (the room graph's doorway pairs) take the same probes in their cells.
/// </summary>
/// <remarks>
/// <para><b>Hosts.</b> The headless host confirms a whole path at once (<see cref="ConfirmAll"/>); the Godot host confirms it in slices
/// (<see cref="Continue"/> with a budget in milliseconds per frame). The probe body leaves the world at the end of every call, so it never
/// stands in the player's way.</para>
/// <para><b>Sight.</b> <see cref="PhysicsWorld.SweepViewer"/> (a gravity-free sphere) serves only the runner's in-sight arrival test
/// (<see cref="InSight"/>) and the wall check between doorway pairs (<see cref="WallBetween"/>).</para>
/// <para><b>The fixed world only</b> (M5 item 5 fix F3): both probes pass the server objects the world holds since item 5 (D70; the ground probe is
/// <see cref="PhysicsWorld.TryFixedGroundZ"/>, the placement body is <see cref="PhysicsObj.FixedWorldOnly"/>), so a closed door, an NPC or a creature on
/// the path never refuses it: the runner meets them at walk time (a closed door: the door step; a creature: the creature layer and its replans; anything
/// else: the stuck ladder). The live gate's round 1 refused every route through a closed door's sill this way. The player's own transitions still meet
/// every server object.</para>
/// <para>Not retail: the sampling and its constants are Lifestone's own. Game thread only (the world is).</para>
/// </remarks>
public sealed class RouteConfirmer : IDisposable
{
    /// <summary>The ground probe's spacing along the path, in metres. Not retail: Lifestone's own.</summary>
    public const float SampleMetres = 1f;

    /// <summary>The placement probe's spacing along the path, in metres. Not retail: Lifestone's own.</summary>
    public const float PlacementMetres = 2f;

    /// <summary>How far a placement may move the body sideways and still count as standing there, in metres. Not retail: Lifestone's own.</summary>
    public const float PlacementSlackMetres = 0.5f;

    /// <summary>
    /// The deepest drop a sample may take, in metres (a probe lowered by the full window finds ground down to this): the lattice's
    /// edge drop (<see cref="NavMap.MaxDropMetres"/>, 2 m) is the planner's; the confirmer takes ground within 1.8 m. Not retail: Lifestone's own.
    /// </summary>
    public const float MaxDropMetres = PhysicsWorld.GroundProbeRise + PhysicsWorld.GroundProbeDrop + PhysicsWorld.GroundProbeDrop;

    /// <summary>
    /// The placement probe stands this far above the ground the ground probe found, in metres: the placement tests the body's fit, not the
    /// ground (a sphere resting exactly on a terrain triangle's edge was refused at flat ground's z). Not retail: Lifestone's own.
    /// </summary>
    public const float PlacementLiftMetres = 0.05f;

    /// <summary>
    /// How far off in x and in y a refused placement is tried again, in metres: TODO(placement-cell-edge): a placement whose origin lies
    /// exactly on a land cell's edge (x or y a multiple of 24 m) is refused (Collided) on flat open ground by this port's
    /// <see cref="PhysicsObj.SetPosition"/>, 0.1 m either side is OK (S5's probe, 2026-10-23); not yet traced to retail or the port. The
    /// nudge is counted (<see cref="PlacementNudges"/>) and logged. Not retail: Lifestone's own.
    /// </summary>
    public const float PlacementNudgeMetres = 0.1f;

    /// <summary>The eye above the feet for the sight test, in metres. Not retail: Lifestone's own.</summary>
    public const float EyeHeightMetres = 1.5f;

    /// <summary>A target's chest above its feet for the sight test, in metres. Not retail: Lifestone's own.</summary>
    public const float ChestHeightMetres = 1.0f;

    /// <summary>The sight test's sphere radius (retail's viewer sphere, <c>SmartBox::update_viewer</c>, is 0.3 m too).</summary>
    public const float SightRadiusMetres = 0.3f;

    /// <summary>The two tangent lines' offset from the target's centre, in metres. Not retail: Lifestone's own.</summary>
    public const float TangentMetres = 0.5f;

    /// <summary>The wall check's sphere height above the doorway pair's floor, in metres. Not retail: Lifestone's own.</summary>
    public const float WallCheckHeightMetres = 1.0f;

    private readonly PhysicsWorld _world;
    private readonly PhysicsSetup _body;
    private readonly PhysSphere _foot;
    private readonly ILogSink _log;
    private PhysicsObj? _probe;
    private IReadOnlyList<NavWaypoint> _path = [];
    private int _segment;
    private float _along;
    private float _arc;
    private float _limit;
    private float _prevZ;
    private float _sincePlacement;

    /// <summary>A confirmer in <paramref name="world"/> for a body of <paramref name="body"/>'s collision (the player's Setup).</summary>
    /// <param name="world">The world probed.</param>
    /// <param name="body">The body's collision.</param>
    /// <param name="log">The log (the placement nudge's TODO), or null.</param>
    public RouteConfirmer(PhysicsWorld world, PhysicsSetup body, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(body);
        if (body.Spheres.Count == 0)
        {
            throw new ArgumentException("the body has no foot sphere", nameof(body));
        }

        _world = world;
        _body = body;
        _foot = body.Spheres[0];
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>The world probed.</summary>
    public PhysicsWorld World => _world;

    /// <summary>Where the confirmation stands.</summary>
    public RouteConfirmStatus Status { get; private set; }

    /// <summary>The first refused sample, once <see cref="Status"/> is <see cref="RouteConfirmStatus.Failed"/>.</summary>
    public RouteConfirmFailure? Failure { get; private set; }

    /// <summary>Ground probes taken for the current path.</summary>
    public int GroundSamples { get; private set; }

    /// <summary>Placement probes taken for the current path.</summary>
    public int Placements { get; private set; }

    /// <summary>Placements refused and taken again <see cref="PlacementNudgeMetres"/> off (TODO(placement-cell-edge)) for the current path.</summary>
    public int PlacementNudges { get; private set; }

    /// <summary>Metres of the current path confirmed so far.</summary>
    public float ConfirmedMetres => _arc;

    /// <summary>The time spent on the current path, in milliseconds.</summary>
    public double Milliseconds { get; private set; }

    /// <summary>The steps up (positive) and down the samples took, the largest of each, in metres (the room-steps evidence).</summary>
    public (float Up, float Down) LargestSteps { get; private set; }

    /// <summary>
    /// Starts confirming <paramref name="path"/> (its waypoints, start first), all but its last <paramref name="stopShortMetres"/> (the
    /// goal's arrival tolerance: the body stops within it, so the goal point itself, a room's floor centre under a table, need not be
    /// standable).
    /// </summary>
    public void Begin(IReadOnlyList<NavWaypoint> path, float stopShortMetres = 0f)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
        var total = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            total += Vector2.Distance(path[i - 1].Point.Global, path[i].Point.Global);
        }

        _limit = MathF.Max(0f, total - MathF.Max(0f, stopShortMetres));
        _segment = 0;
        _along = 0f;
        _arc = 0f;
        _sincePlacement = PlacementMetres; // the first placement is at the first sample, one metre in (the start itself is not placed)
        _prevZ = path.Count > 0 ? path[0].At.Origin.Z : 0f;
        GroundSamples = 0;
        Placements = 0;
        PlacementNudges = 0;
        Milliseconds = 0;
        LargestSteps = (0f, 0f);
        Failure = null;
        Status = path.Count == 0 ? RouteConfirmStatus.Failed : RouteConfirmStatus.Pending;
        if (path.Count == 0)
        {
            Failure = new RouteConfirmFailure(0, default, 0f, "ground", "an empty path");
        }
    }

    /// <summary>
    /// How much of <paramref name="path"/>'s end lies past its first point within <paramref name="range"/> (horizontal) of <paramref name="target"/>, in
    /// metres of arc: an object goal's stop-short (M4 P5b F1). The runner ends an object run at the first point within the range (with sight required,
    /// the arrival test is made there), so nothing past it is walked and nothing past it needs to be standable: the object's own point inside a static
    /// (Renald behind his counter) is never probed. 0 when no point of the path comes within the range but its end. With <paramref name="roomCell"/> (an
    /// indoor goal's cell) the search starts at the path's first waypoint in that cell, as the runner's arrival waits for the body to be in it (a wall
    /// between the street and the object is within the range too).
    /// </summary>
    /// <param name="path">The path, start first.</param>
    /// <param name="target">The object's point.</param>
    /// <param name="range">The object goal's range.</param>
    /// <param name="roomCell">The goal's indoor cell, or null.</param>
    public static float ArcBeyondRange(IReadOnlyList<NavWaypoint> path, NavPoint target, float range, uint? roomCell = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2 || range <= 0f)
        {
            return 0f;
        }

        var goal = target.Global;
        var total = 0f;
        for (var i = 1; i < path.Count; i++)
        {
            total += Vector2.Distance(path[i - 1].Point.Global, path[i].Point.Global);
        }

        var first = 1;
        var arc = 0f;
        if (roomCell is { } room && !LandDefs.IsOutdoorCell(room))
        {
            while (first < path.Count && path[first - 1].At.CellId != room)
            {
                arc += Vector2.Distance(path[first - 1].Point.Global, path[first].Point.Global);
                first++;
            }
        }

        for (var i = first; i < path.Count; i++)
        {
            var a = path[i - 1].Point.Global;
            var b = path[i].Point.Global;
            var length = Vector2.Distance(a, b);
            if (Vector2.Distance(a, goal) <= range)
            {
                return MathF.Max(0f, total - arc);
            }

            if (length > 1e-6f)
            {
                // The first t in [0, 1] with |a + t (b - a) - goal| = range (the segment enters the range circle).
                var d = (b - a) / length;
                var f = a - goal;
                var along = -Vector2.Dot(f, d);
                var square = (along * along) - (f.LengthSquared() - (range * range));
                if (square >= 0f)
                {
                    var t = along - MathF.Sqrt(square);
                    if (t >= 0f && t <= length)
                    {
                        return MathF.Max(0f, total - (arc + t));
                    }
                }
            }

            arc += length;
        }

        return 0f;
    }

    /// <summary>Confirms the whole of <paramref name="path"/> (but its last <paramref name="stopShortMetres"/>) now (the headless host).</summary>
    public RouteConfirmStatus ConfirmAll(IReadOnlyList<NavWaypoint> path, float stopShortMetres = 0f)
    {
        Begin(path, stopShortMetres);
        return Continue(double.PositiveInfinity);
    }

    /// <summary>Confirms on for about <paramref name="budgetMilliseconds"/> (at least one sample), then returns where it stands.</summary>
    public RouteConfirmStatus Continue(double budgetMilliseconds)
    {
        if (Status != RouteConfirmStatus.Pending)
        {
            return Status;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            while (Status == RouteConfirmStatus.Pending)
            {
                StepSample();
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budgetMilliseconds)
                {
                    break;
                }
            }
        }
        finally
        {
            Milliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (_probe is { Cell: not null })
            {
                _probe.LeaveWorld();
            }
        }

        return Status;
    }

    /// <summary>
    /// True when a sphere of <see cref="SightRadiusMetres"/> swept from <paramref name="from"/> to <paramref name="to"/>, both raised
    /// <see cref="WallCheckHeightMetres"/>, is stopped by collision: the wall check between doorway pairs.
    /// </summary>
    public bool WallBetween(in CellFrame from, in CellFrame to)
    {
        var lift = new Vector3(0f, 0f, WallCheckHeightMetres);
        return _world.SweepViewer(Raise(from, lift), Raise(to, lift), SightRadiusMetres).Blocked;
    }

    /// <summary>
    /// The runner's in-sight arrival test: a sphere of <see cref="SightRadiusMetres"/> from the eye (<see cref="EyeHeightMetres"/> above
    /// <paramref name="body"/>) to the target's chest (<see cref="ChestHeightMetres"/> above <paramref name="target"/>) not blocked;
    /// else the two tangents (the chest moved <see cref="TangentMetres"/> to either side across the line). Returns how many lines were tried.
    /// </summary>
    public bool InSight(in CellFrame body, in CellFrame target, out int tries)
    {
        var eye = Raise(body, new Vector3(0f, 0f, EyeHeightMetres));
        var chest = Raise(target, new Vector3(0f, 0f, ChestHeightMetres));
        tries = 1;
        if (!_world.SweepViewer(eye, chest, SightRadiusMetres).Blocked)
        {
            return true;
        }

        var line = body.GetOffset(target);
        var across = new Vector2(-line.Y, line.X);
        if (across.LengthSquared() < 1e-6f)
        {
            return false;
        }

        across = Vector2.Normalize(across) * TangentMetres;
        foreach (var side in (ReadOnlySpan<float>)[1f, -1f])
        {
            tries++;
            var tangent = Raise(chest, new Vector3(across.X * side, across.Y * side, 0f));
            if (!_world.SweepViewer(eye, tangent, SightRadiusMetres).Blocked)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The static object nearest <paramref name="at"/> horizontally among the attached landblocks round it (its model id, cell, position
    /// and distance: <c>PhysStaticObject.ModelId</c> and <c>Position</c>), for the runner's Blocked reason; null when none is attached.
    /// </summary>
    public string? NearestStatic(in CellFrame at)
    {
        var here = NavMap.FromCellFrame(at).Global;
        var coord = LandblockCoord.FromId(at.CellId);
        PhysStaticObject? best = null;
        var bestDistance = float.MaxValue;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (coord.Offset(dx, dy) is not { } near || _world.GetLandblock(near) is not { } block)
                {
                    continue;
                }

                foreach (var s in block.StaticObjects)
                {
                    var d = Vector2.Distance(here, NavMap.FromCellFrame(s.Position).Global);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = s;
                    }
                }
            }
        }

        return best is null ? null : string.Create(
            CultureInfo.InvariantCulture,
            $"static 0x{best.ModelId:X8} in cell 0x{best.Position.CellId:X8} at ({best.Position.Origin.X:F1}, {best.Position.Origin.Y:F1}, {best.Position.Origin.Z:F1}), {bestDistance:F1} m away");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _probe?.Destroy();
        _probe = null;
    }

    private static CellFrame Raise(in CellFrame frame, Vector3 by) =>
        new(frame.CellId, new AcFrame(frame.Origin + by, Quaternion.Identity));

    /// <summary>One ground sample (and a placement every <see cref="PlacementMetres"/>), or the path's end.</summary>
    private void StepSample()
    {
        if (_segment >= _path.Count - 1 || _arc >= _limit - 1e-4f)
        {
            Status = RouteConfirmStatus.Confirmed;
            return;
        }

        var a = _path[_segment];
        var b = _path[_segment + 1];
        var ga = a.Point.Global;
        var gb = b.Point.Global;
        var length = Vector2.Distance(ga, gb);
        var step = MathF.Min(MathF.Min(SampleMetres, length - _along), _limit - _arc);
        if (step <= 1e-4f && length - _along <= 1e-4f)
        {
            _segment++;
            _along = 0f;
            return;
        }

        var target = _along + step;
        if (!TryGround(a, b, ga, gb, length, target, out var z, out var at))
        {
            // A steep walkable slope: the half sample first, then a step down past the window (a ledge within MaxDropMetres).
            var found = false;
            if (step > SampleMetres * 0.5f && TryGround(a, b, ga, gb, length, _along + (step * 0.5f), out var halfZ, out _))
            {
                Accept(halfZ);
                found = TryGround(a, b, ga, gb, length, target, out z, out at);
            }

            if (!found && !TryDrop(a, b, ga, gb, length, target, out z, out at))
            {
                Fail(at, "ground", string.Create(CultureInfo.InvariantCulture, $"no walkable ground within {PhysicsWorld.GroundProbeRise:F1} m up or {MaxDropMetres:F1} m down of the previous sample's z {_prevZ:F2} (a step, a wall or a hole)"));
                return;
            }
        }

        Accept(z);
        _along = target;
        _arc += step;
        _sincePlacement += step;
        if (_sincePlacement >= PlacementMetres)
        {
            _sincePlacement = 0f;
            Placements++;
            if (!TryPlace(at, out var why))
            {
                var nudged = new CellFrame(at.CellId, new AcFrame(at.Origin + new Vector3(PlacementNudgeMetres, PlacementNudgeMetres, 0f), Quaternion.Identity));
                if (TryPlace(nudged, out _))
                {
                    PlacementNudges++;
                    if (PlacementNudges == 1)
                    {
                        _log.Warn("wayfinding", "TODO(placement-cell-edge): a placement refused on a land cell's edge was taken 0.1 m off it (x and y)", new LogField("at", at.ToString()), new LogField("why", why));
                    }
                }
                else
                {
                    Fail(at, "placement", why);
                }
            }
        }
    }

    private void Accept(float z)
    {
        var rise = z - _prevZ;
        LargestSteps = (MathF.Max(LargestSteps.Up, rise), MathF.Min(LargestSteps.Down, rise));
        _prevZ = z;
    }

    private void Fail(in CellFrame at, string probe, string why)
    {
        Status = RouteConfirmStatus.Failed;
        Failure = new RouteConfirmFailure(_segment, at, _arc, probe, why);
    }

    /// <summary>The sample <paramref name="along"/> metres into the segment a to b, in the nearer end's cell (an outdoor pair: the land cell under it).</summary>
    private CellFrame SampleAt(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, float z)
    {
        var t = length > 0f ? along / length : 1f;
        var g = Vector2.Lerp(ga, gb, t);
        if (!a.Indoor && !b.Indoor)
        {
            var land = NavMap.ToCellFrame(NavPoint.FromGlobal(g, z));
            return land;
        }

        var near = t < 0.5f ? a : b;
        var origin = NavPoint.BlockOrigin(near.At.CellId);
        return new CellFrame(near.At.CellId, new AcFrame(new Vector3(g.X - origin.X, g.Y - origin.Y, z), Quaternion.Identity));
    }

    private bool TryGround(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, out float z, out CellFrame at)
    {
        at = SampleAt(a, b, ga, gb, length, along, _prevZ);
        GroundSamples++;
        if (!_world.TryFixedGroundZ(at, _foot, out z, out _))
        {
            return false;
        }

        at = new CellFrame(at.CellId, new AcFrame(at.Origin with { Z = z }, Quaternion.Identity));
        return true;
    }

    private bool TryDrop(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, out float z, out CellFrame at)
    {
        var drop = PhysicsWorld.GroundProbeRise + PhysicsWorld.GroundProbeDrop;
        at = SampleAt(a, b, ga, gb, length, along, _prevZ - drop);
        GroundSamples++;
        if (!_world.TryFixedGroundZ(at, _foot, out z, out _))
        {
            at = SampleAt(a, b, ga, gb, length, along, _prevZ);
            return false;
        }

        at = new CellFrame(at.CellId, new AcFrame(at.Origin with { Z = z }, Quaternion.Identity));
        return true;
    }

    private bool TryPlace(in CellFrame at, out string why)
    {
        _probe ??= new PhysicsObj(_world, _body, id: 0, new ProbeWeenie()) { FixedWorldOnly = true }; // F3: the planner judges the fixed world; the runner meets server objects
        if (_probe.Cell is not null)
        {
            _probe.LeaveWorld(); // each placement fresh, not a move from the last one
        }

        var lifted = new CellFrame(at.CellId, new AcFrame(at.Origin + new Vector3(0f, 0f, PlacementLiftMetres), Quaternion.Identity));
        var result = _probe.SetPosition(lifted, SetPositionFlags.Placement);
        if (result != SetPositionError.OK || _probe.Cell is null)
        {
            why = "no room to stand: " + result;
            return false;
        }

        var moved = lifted.GetOffset(_probe.Position);
        var sideways = new Vector2(moved.X, moved.Y).Length();
        if (sideways > PlacementSlackMetres)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the placement moved the body {sideways:F2} m (to cell 0x{_probe.Position.CellId:X8})");
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>The probe's body: a player, not a player killer, so the player and other players never block it (as RouteLibrary's ring probe).</summary>
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
