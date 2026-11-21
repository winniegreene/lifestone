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

    /// <summary>The most one step out of water can lift a body, in metres: a flooded cell's 0.9 m depth less the 0.1 m of a partly flooded cell's dry corner (retail's water depths).</summary>
    public const float WaterRiseMetres = 0.8f;

    /// <summary>
    /// How much higher than its waypoint the start of a path is looked for when the first sample finds nothing, in metres. A path's first waypoint takes its height from the planner's node, and on a steep slope that can lie about a metre under the real
    /// ground, so the first probe begins inside the hill (the world walk: 25 of the 217 long walks the check ended). Not retail: Lifestone's own.
    /// </summary>
    public const float StartLiftMetres = PhysicsWorld.GroundProbeRise + PhysicsWorld.GroundProbeDrop;

    /// <summary>
    /// The most refused samples in a row an outdoor path may step round (<see cref="SideLooks"/>). Two samples are two metres, a body's width and a little: the corner of a steep land triangle, or a tree's edge the foot sphere clips. A real hillside
    /// or cliff runs on for longer and is still refused. Not retail: Lifestone's own.
    /// </summary>
    public const int MaxSideLooksInARow = 2;

    /// <summary>Where the side look tries, as metres across the path and ahead along it, nearest first. Not retail: Lifestone's own.</summary>
    private static readonly (float Across, float Ahead)[] SideLooks = [(0.5f, 0f), (-0.5f, 0f), (0f, 0.5f), (1f, 0f), (-1f, 0f), (0f, 1f)];

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

    /// <summary>
    /// How near a refused placement the body must come, stepped there from where it last stood, for the placement to pass, in metres (horizontal). A placement
    /// without slide may push the body no more than 0.05 m east or north (<c>CPhysicsObj::CheckPositionInternal</c>, 11.6096 0x00512960), so a path that runs
    /// within a body's width of a wall, a door's jamb or a pillar is refused where the walking body slides along it (the world walk's rooms, W8: 227 walks the
    /// check refused that the body walks, 180 with no recovery move). The step is the walking body's own transition (<see cref="PhysicsObj.TryTransitionTo"/>), so a
    /// gap narrower than the body still stops it short. Not retail: Lifestone's own.
    /// </summary>
    public const float SweepSlackMetres = 0.5f;

    /// <summary>
    /// How many times the body pushes on toward a refused placement from where its last push stopped (<see cref="SweepSlackMetres"/>): a step into the edge of
    /// a pillar or a table slides along its face to the corner, and the walking body, steering at its aim every frame, goes on from there. Not retail: Lifestone's own.
    /// </summary>
    public const int SweepPushes = 3;

    /// <summary>The least a push must bring the body nearer its aim to earn another, in metres. Not retail: Lifestone's own.</summary>
    public const float SweepGainMetres = 0.05f;

    /// <summary>
    /// How far the body steps aside, each way, when its pushes stop flat against something on the line before pushing on (<see cref="SweepSlackMetres"/>), in metres: a thin post in a doorway's middle
    /// stops a step aimed at its centre, and the walking body, coming at it askew, slides past it (the world walk's rooms, W8: 0x01AE0251; 22 walks the check refused arrive with it, none only without). Not retail: Lifestone's own.
    /// </summary>
    public const float SweepSideMetres = 0.5f;

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
    private int _sideLooksInARow;
    private CellFrame? _lastStood;
    private CellFrame? _prevAt;
    private string _stepWhy = string.Empty;

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

    /// <summary>Placements refused and passed by the body's step from where it last stood (<see cref="SweepSlackMetres"/>) for the current path.</summary>
    public int Sweeps { get; private set; }

    /// <summary>Indoor ground samples the foot refused and the body's step from where it last stood passed (<see cref="TryBodyStep"/>) for the current path.</summary>
    public int GroundSteps { get; private set; }

    /// <summary>Samples of the current path the height window refused and the water rise found (<see cref="WaterRiseMetres"/>).</summary>
    public int WaterRises { get; private set; }

    /// <summary>Paths whose first sample needed the start looked for higher up (<see cref="StartLiftMetres"/>): 0 or 1 for the current path.</summary>
    public int StartLifts { get; private set; }

    /// <summary>Samples of the current path refused where ground beside or just ahead took the body (<see cref="MaxSideLooksInARow"/>).</summary>
    public int SidesTaken { get; private set; }

    /// <summary>Metres of the current path confirmed so far.</summary>
    public float ConfirmedMetres => _arc;

    /// <summary>M6 14 pits: the jumps' flights this confirmation stepped over (a take-off to its landing).</summary>
    public int JumpsSkipped { get; private set; }

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
        JumpsSkipped = 0;
        Placements = 0;
        PlacementNudges = 0;
        WaterRises = 0;
        StartLifts = 0;
        SidesTaken = 0;
        _sideLooksInARow = 0;
        _lastStood = null;
        _prevAt = null;
        Sweeps = 0;
        GroundSteps = 0;
        _stepWhy = string.Empty;
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
    /// indoor goal's cell) the search starts at the path's first waypoint in that cell, the latest the runner's arrival comes (a wall between the street and
    /// the object is within the range too); since the M6 sprint's m6-vendor-reach it may come sooner, within the range with nothing walling the body off or
    /// on a contact with the object (<c>NavRunner.ObjectInReach</c>), so this check covers more of the path than is walked, never less.
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
        if (a.Jump is not null)
        {
            // M6 14 pits: the step from a take-off to its landing is a jump's flight, not a walk: no ground under it to sample (the pit). The runner checks the body's flight before it jumps and
            // the landing after it; the flight itself is proven on the dats (PitJumpDatTests). The samples go on from the landing.
            _arc += MathF.Max(0f, length - _along);
            _segment++;
            _along = 0f;
            _prevZ = b.At.Origin.Z;
            _sincePlacement = 0f;
            _lastStood = Lifted(b.At); // the body's steps (TryBodyStep, a refused placement's Step) go on from the landing, never back across the pit
            _prevAt = b.At;
            JumpsSkipped++;
            return;
        }

        var step = MathF.Min(MathF.Min(SampleMetres, length - _along), _limit - _arc);
        if (step <= 1e-4f && length - _along <= 1e-4f)
        {
            _segment++;
            _along = 0f;
            return;
        }

        var target = _along + step;
        var tookSide = false;
        if (!TryGround(a, b, ga, gb, length, target, out var z, out var at))
        {
            // A steep walkable slope: the half sample first, then a step down past the window (a ledge within MaxDropMetres), then out of water, then a start height under the ground, then ground beside or just ahead.
            var found = false;
            if (step > SampleMetres * 0.5f && TryGround(a, b, ga, gb, length, _along + (step * 0.5f), out var halfZ, out _))
            {
                Accept(halfZ);
                found = TryGround(a, b, ga, gb, length, target, out z, out at);
            }

            if (!found && !TryDrop(a, b, ga, gb, length, target, out z, out at) && !TryLineHeight(a, b, ga, gb, length, target, ref z, ref at) && !TryOutOfWater(a, b, ga, gb, length, target, ref z, ref at) && !TryStartHeight(a, b, ga, gb, length, target, ref z, ref at) && !(tookSide = TrySideLook(a, b, ga, gb, length, target, ref z, ref at)) && !TryBodyStep(a, b, ga, gb, length, target, ref z, ref at))
            {
                Fail(at, "ground", string.Create(CultureInfo.InvariantCulture, $"no walkable ground within {PhysicsWorld.GroundProbeRise:F1} m up or {MaxDropMetres:F1} m down of the previous sample's z {_prevZ:F2} (a step, a wall or a hole)") + (_stepWhy.Length > 0 ? "; the body stepped from where it last stood: " + _stepWhy : string.Empty));
                return;
            }
        }

        if (!tookSide)
        {
            _sideLooksInARow = 0;
        }

        Accept(z);
        _prevAt = at;
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
                    _lastStood = _probe!.Position;
                    if (PlacementNudges == 1)
                    {
                        _log.Warn("wayfinding", "TODO(placement-cell-edge): a placement refused on a land cell's edge was taken 0.1 m off it (x and y)", new LogField("at", at.ToString()), new LogField("why", why));
                    }
                }
                else if (Step(_probe!, at, _lastStood ?? Lifted(_path[0].At), out var stepWhy))
                {
                    _lastStood = _probe!.Position;
                    Sweeps++;
                }
                else
                {
                    Fail(at, "placement", why + "; stepped from where it last stood: " + stepWhy);
                }
            }
            else
            {
                _lastStood = _probe!.Position;
            }
        }
    }

    /// <summary>
    /// The pre-check's placement at <paramref name="at"/>, and when it is refused, the body's step there from <paramref name="lastStood"/> (<see cref="Step"/>; the planner's mirror,
    /// <c>CellRoutePlanner</c>'s straight steps and doorway crossings): true when the body stands there, <paramref name="lastStood"/> then where it stands.
    /// </summary>
    internal static bool StandsOrSteps(PhysicsObj probe, in CellFrame at, ref CellFrame lastStood)
    {
        var stood = Place(probe, at, out _) || Place(probe, new CellFrame(at.CellId, new AcFrame(at.Origin + new Vector3(PlacementNudgeMetres, PlacementNudgeMetres, 0f), Quaternion.Identity)), out _) || Step(probe, at, lastStood, out _);
        if (stood)
        {
            lastStood = probe.Position;
        }

        return stood;
    }

    /// <summary><paramref name="at"/> raised <see cref="PlacementLiftMetres"/>: where a placement stands the body.</summary>
    internal static CellFrame Lifted(in CellFrame at) => new(at.CellId, new AcFrame(at.Origin + new Vector3(0f, 0f, PlacementLiftMetres), Quaternion.Identity));

    /// <summary>
    /// The body placed at <paramref name="from"/> (where it last stood, else the path's start, raised: <see cref="Lifted"/>) and stepped to <paramref name="at"/> by the walking body's transition, sliding as it
    /// does, pushing on <see cref="SweepPushes"/> times while it gains: true when it ends on walkable ground within <see cref="SweepSlackMetres"/> of <paramref name="at"/>.
    /// </summary>
    internal static bool Step(PhysicsObj probe, in CellFrame at, in CellFrame from, out string why)
    {
        if (probe.Cell is not null)
        {
            probe.LeaveWorld();
        }

        var placed = probe.SetPosition(from, SetPositionFlags.Placement | SetPositionFlags.Slide);
        if (placed != SetPositionError.OK || probe.Cell is null)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"its start would not place ({placed})");
            return false;
        }

        var target = Lifted(at);
        var first = probe.Position;
        if (Pushes(probe, target, out var hard))
        {
            why = string.Empty;
            return true;
        }

        if (hard is not null)
        {
            why = hard;
            return false;
        }

        // Stopped flat against something on the line (a thin post in a doorway's middle): the walking body, coming at it a little askew, slides off one side; so a sidestep each way, then on.
        var stopped = probe.Position;
        var heading = stopped.GetOffset(target);
        var across = new Vector2(heading.X, heading.Y) is { } h && h.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-h.Y, h.X)) : Vector2.UnitX;
        foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
        {
            probe.LeaveWorld();
            if (probe.SetPosition(stopped, SetPositionFlags.Placement | SetPositionFlags.Slide) != SetPositionError.OK || probe.Cell is null)
            {
                break;
            }

            var aside = new CellFrame(stopped.CellId, new AcFrame(stopped.Origin + new Vector3(across * (sign * SweepSideMetres), 0f), stopped.Frame.Orientation));
            if (probe.TryTransitionTo(aside, carriedContact: true) && probe.OnWalkable && Pushes(probe, target, out _))
            {
                why = string.Empty;
                return true;
            }
        }

        var aim = first.GetOffset(target);
        var went = first.GetOffset(stopped);
        var dir = Vector2.Normalize(new Vector2(aim.X, aim.Y));
        var along = Vector2.Dot(new Vector2(went.X, went.Y), dir);
        var side = MathF.Abs((went.X * dir.Y) - (went.Y * dir.X));
        var end = stopped.GetOffset(target);
        why = string.Create(CultureInfo.InvariantCulture, $"it stopped {new Vector2(end.X, end.Y).Length():F2} m short, {end.Z:F2} m below (in cell 0x{stopped.CellId:X8}, along {along:F2} of {new Vector2(aim.X, aim.Y).Length():F2} m, side {side:F2} m)");
        return false;
    }

    /// <summary>
    /// The body pushed toward <paramref name="target"/> from where it is, <see cref="SweepPushes"/> times while each push gains <see cref="SweepGainMetres"/>: true when it ends on walkable ground within
    /// <see cref="SweepSlackMetres"/> of it. <paramref name="hard"/> says why when the first push found no valid position or a push left walkable ground; null when it only stopped short.
    /// </summary>
    private static bool Pushes(PhysicsObj probe, in CellFrame target, out string? hard)
    {
        hard = null;
        var left = float.MaxValue;
        for (var push = 0; push < SweepPushes; push++)
        {
            var start = probe.Position;
            var to = new CellFrame(start.CellId, new AcFrame(start.Origin + start.GetOffset(target), start.Frame.Orientation));
            if (!probe.TryTransitionTo(to, carriedContact: true))
            {
                if (push == 0)
                {
                    hard = string.Create(CultureInfo.InvariantCulture, $"it found no valid position ({probe.LastTransitionObstacle})");
                }

                return false; // pressed against what stopped the last push
            }

            if (!probe.OnWalkable)
            {
                hard = string.Create(CultureInfo.InvariantCulture, $"push {push + 1} left walkable ground (contact {probe.HasContact})");
                return false;
            }

            var off = probe.Position.GetOffset(target);
            var gap = new Vector2(off.X, off.Y).Length();
            if (gap <= SweepSlackMetres && MathF.Abs(off.Z) <= PhysicsWorld.GroundProbeRise)
            {
                return true;
            }

            if (gap > left - SweepGainMetres)
            {
                return false; // no ground gained: stopped against it
            }

            left = gap;
        }

        return false;
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

    /// <summary>The path's first sample found nothing because the start's own height lies under the ground (<see cref="StartLiftMetres"/>): the start is found from higher up, and the sample taken again from there.</summary>
    private bool TryStartHeight(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, ref float z, ref CellFrame at)
    {
        if (_segment != 0 || _arc > 1e-4f)
        {
            return false;
        }

        var start = SampleAt(a, b, ga, gb, length, 0f, _prevZ + StartLiftMetres);
        GroundSamples++;
        if (!_world.TryFixedGroundZ(start, _foot, out var startZ, out _))
        {
            return false;
        }

        var kept = _prevZ;
        _prevZ = startZ;
        if (!TryGround(a, b, ga, gb, length, along, out var found, out var there))
        {
            _prevZ = kept;
            return false;
        }

        z = found;
        at = there;
        StartLifts++;
        return true;
    }

    /// <summary>
    /// A refused outdoor sample where the body would be carried past: the corner of a steep land triangle a real body crosses in a stride, or a tree's edge the foot sphere clips and a real body slides round (the world walk: 177 of the 217 long walks the check ended, and those walks arrived without it, at the cost of a recovery move at some of these spots). The body takes ground found beside or just ahead, unless a wall stands between. At most <see cref="MaxSideLooksInARow"/> in a row. Indoors a thin wall or a sill matters, so rooms keep the strict rule.
    /// </summary>
    /// <summary>
    /// A refused indoor sample where the foot sphere meets a wall at every height (a doorway's jamb, a wall the step's line runs within a foot's width of): the body stepped there from where it last stood
    /// (<see cref="Step"/>), sliding as the walking body does, and the ground it lands on is taken (the world walk's rooms, W8: 275 of the 355 ground refusals the body walks found no ground from any
    /// height in any of the path's rooms). A wall across the step still stops the body short. The planner does not mirror this: a straight step it keeps on such a step got the walking body stuck where
    /// a way round walks clean (92 walks of the rooms run made worse), so it goes round where it can and takes such a step only when nothing else is (<c>RoomGraph.UnwalkedLegMetres</c>).
    /// </summary>
    private bool TryBodyStep(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, ref float z, ref CellFrame at)
    {
        if (!a.Indoor && !b.Indoor)
        {
            return false;
        }

        var sample = SampleAt(a, b, ga, gb, length, along, _prevZ);
        _probe ??= new PhysicsObj(_world, _body, id: 0, new ProbeWeenie()) { FixedWorldOnly = true };
        // From where the body last stood (a placement, every PlacementMetres), else from the last sample the foot took.
        // A sample closer to a thin post's centre than a body can stand is passed, not stood on: the step to the next sample of the same step is taken instead (the world walk's rooms, W8: a post in the
        // middle of a doorway, 0x013D010E and 0x0153026B, layouts of many dungeons).
        var from = _lastStood ?? Lifted(_path[0].At);
        var beyond = along + SampleMetres <= length + 1e-4f ? SampleAt(a, b, ga, gb, length, along + SampleMetres, _prevZ) : (CellFrame?)null;
        if (!Step(_probe, sample, from, out _stepWhy)
            && (_prevAt is not { } prev || !Step(_probe, sample, Lifted(prev), out _))
            && (beyond is not { } next || !Step(_probe, next, from, out _)))
        {
            return false;
        }

        _lastStood = _probe.Position;
        at = _probe.Position;
        z = at.Origin.Z;
        at = new CellFrame(at.CellId, new AcFrame(at.Origin, Quaternion.Identity));
        GroundSteps++;
        return true;
    }

    private bool TrySideLook(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, ref float z, ref CellFrame at)
    {
        if (a.Indoor || b.Indoor || length <= 0f || _sideLooksInARow >= MaxSideLooksInARow)
        {
            return false;
        }

        var way = (gb - ga) / length;
        var across = new Vector2(-way.Y, way.X);
        var sample = Vector2.Lerp(ga, gb, along / length);
        var from = NavMap.ToCellFrame(NavPoint.FromGlobal(Vector2.Lerp(ga, gb, _along / length), _prevZ));
        foreach (var (side, ahead) in SideLooks)
        {
            var there = NavMap.ToCellFrame(NavPoint.FromGlobal(sample + (across * side) + (way * ahead), _prevZ));
            GroundSamples++;
            if (!_world.TryFixedGroundZ(there, _foot, out var found, out _))
            {
                continue;
            }

            var stand = new CellFrame(there.CellId, new AcFrame(there.Origin with { Z = found }, Quaternion.Identity));
            if (WallBetween(from, stand))
            {
                continue;
            }

            z = found;
            at = stand;
            SidesTaken++;
            _sideLooksInARow++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The body in deep water: physics sinks it up to 0.9 m below the ground (0.45 m or 0.1 m in a partly flooded cell, by the nearest corner: <see cref="PhysCell"/>'s water depth). The height window is measured from the sunk height,
    /// so a probe started there can begin under the ground and find nothing, though a walking body is carried on (the world walk: 331 of 339 long walks the check ended arrived without it). So after a water sample the probe also starts from that much higher.
    /// </summary>
    private bool TryOutOfWater(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, ref float z, ref CellFrame at)
    {
        var last = SampleAt(a, b, ga, gb, length, _along, _prevZ);
        if (_world.GetCell(last.CellId) is not { WaterType: not WaterType.NotWater })
        {
            return false;
        }

        var raised = SampleAt(a, b, ga, gb, length, along, _prevZ + WaterRiseMetres);
        GroundSamples++;
        if (!_world.TryFixedGroundZ(raised, _foot, out var found, out _))
        {
            return false;
        }

        z = found;
        at = new CellFrame(raised.CellId, new AcFrame(raised.Origin with { Z = found }, Quaternion.Identity));
        WaterRises++;
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

    /// <summary>
    /// The samples have climbed onto something beside an indoor step (a crate's or a plinth's top within the step-up), more than <see cref="LineClimbMetres"/> over the step's own line, and the next sample, probed from up
    /// there, starts inside the thing's side and finds nothing, though a body walking past never climbs it (the world walk's rooms, W8: 0x0015016D to 0x0015016B, the goal 0.5 m from a crate; 211 walks the
    /// check refused that the body walks). The sample is probed again from the line's height (the straight line between the step's two waypoints), and taken when it lies within the step window below the last.
    /// Outdoor steps keep the rule they had: an outdoor waypoint's height comes from the planner's lattice.
    /// </summary>
    private bool TryLineHeight(in NavWaypoint a, in NavWaypoint b, Vector2 ga, Vector2 gb, float length, float along, ref float z, ref CellFrame at)
    {
        if (!a.Indoor && !b.Indoor)
        {
            return false;
        }

        var line = LineZ(a.At.Origin.Z, b.At.Origin.Z, length, along);
        if (_prevZ - line <= LineClimbMetres)
        {
            return false;
        }

        var there = SampleAt(a, b, ga, gb, length, along, line);
        GroundSamples++;
        if (!_world.TryFixedGroundZ(there, _foot, out var found, out _) || found < _prevZ - MaxDropMetres)
        {
            return false;
        }

        z = found;
        at = new CellFrame(there.CellId, new AcFrame(there.Origin with { Z = found }, Quaternion.Identity));
        return true;
    }

    /// <summary>How far above an indoor step's line the samples must have climbed before a refused sample is probed again from the line's height (<see cref="TryLineHeight"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float LineClimbMetres = 0.2f;

    /// <summary>The height of the straight line from a step's start height <paramref name="from"/> to its end height <paramref name="to"/>, <paramref name="along"/> metres into its <paramref name="length"/>.</summary>
    public static float LineZ(float from, float to, float length, float along) => from + ((to - from) * (length > 0f ? MathF.Min(along / length, 1f) : 1f));

    private bool TryPlace(in CellFrame at, out string why)
    {
        _probe ??= new PhysicsObj(_world, _body, id: 0, new ProbeWeenie()) { FixedWorldOnly = true }; // F3: the planner judges the fixed world; the runner meets server objects
        return Place(_probe, at, out why);
    }

    /// <summary>The body placed at <paramref name="at"/> raised <see cref="PlacementLiftMetres"/>, without slide, moving no more than <see cref="PlacementSlackMetres"/> sideways.</summary>
    internal static bool Place(PhysicsObj probe, in CellFrame at, out string why)
    {
        if (probe.Cell is not null)
        {
            probe.LeaveWorld(); // each placement fresh, not a move from the last one
        }

        var lifted = Lifted(at);
        var result = probe.SetPosition(lifted, SetPositionFlags.Placement);
        if (result != SetPositionError.OK || probe.Cell is null)
        {
            why = "no room to stand: " + result;
            return false;
        }

        var moved = lifted.GetOffset(probe.Position);
        var sideways = new Vector2(moved.X, moved.Y).Length();
        if (sideways > PlacementSlackMetres)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the placement moved the body {sideways:F2} m (to cell 0x{probe.Position.CellId:X8})");
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>The probe's body: a player, not a player killer, so the player and other players never block it (as RouteLibrary's ring probe).</summary>
    internal sealed class ProbeWeenie : IPhysicsWeenie
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
