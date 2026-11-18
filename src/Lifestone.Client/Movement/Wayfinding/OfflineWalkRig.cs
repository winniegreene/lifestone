using System.Diagnostics;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Physics;
using Lifestone.Client.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>A server object a walk has in its world: what the object is and where it stands.</summary>
/// <param name="Description">The object, as a server would describe it.</param>
/// <param name="Position">Where it stands.</param>
public sealed record OfflineObject(ServerObjectDescription Description, CellFrame Position);

/// <summary>One offline walk: where the body starts, where it goes, and the character it walks as.</summary>
/// <param name="Name">The walk's name, for the logs.</param>
/// <param name="Start">Where the body is placed.</param>
/// <param name="Goal">The goal.</param>
/// <param name="SimSecondsLimit">Simulated seconds after which the walk ends <c>over-time</c>.</param>
public sealed record OfflineWalkRequest(string Name, CellFrame Start, NavGoal Goal, double SimSecondsLimit)
{
    /// <summary>The Run skill the body walks with (<see cref="OfflinePlayerWeenie.PlaceholderRunSkill"/>: about 7.7 m/s; 15: about 4.8 m/s, a low-level character).</summary>
    public int RunSkill { get; init; } = OfflinePlayerWeenie.PlaceholderRunSkill;

    /// <summary>Called once with the map after the start's landblocks are attached and before the first plan: a planted fault for the checks of the checker itself (null: none).</summary>
    public Action<NavMap>? TamperAfterAttach { get; init; }

    /// <summary>Makes the path source over the walk's map (null: the outdoor planner).</summary>
    public Func<NavMap, INavPathSource>? PathSource { get; init; }

    /// <summary>Server objects standing in the walk's world, made through the live game's own host (null: none). One in a landblock the walk has not attached waits, and is not met.</summary>
    public IReadOnlyList<OfflineObject>? Objects { get; init; }

    /// <summary>The world's land as a whole, for far goals (null: the walk knows only the attached tiles).</summary>
    public WorldRoutes? Routes { get; init; }

    /// <summary>Walks with no route check, to see whether a refusal was right: the body is the judge. Only for the world walk's repeat of a refused walk.</summary>
    public bool NoRouteCheck { get; init; }
}

/// <summary>
/// What an offline walk did. Everything here repeats exactly for the same request on the same dats, except <see cref="WallMilliseconds"/>.
/// </summary>
public sealed record OfflineWalkResult
{
    /// <summary>The walk's name.</summary>
    public required string Name { get; init; }

    /// <summary>How the runner ended, or null when the walk never started or ran out of time.</summary>
    public NavRunState? State { get; init; }

    /// <summary>The named reason a walk did not arrive (the runner's, or the rig's: <see cref="OfflineWalkRig.StartRefused"/>, <see cref="OfflineWalkRig.OverTime"/>, <see cref="OfflineWalkRig.StreamerStalled"/>); null when it arrived.</summary>
    public string? Reason { get; init; }

    /// <summary>Recovery rungs the runner took.</summary>
    public int Rungs { get; init; }

    /// <summary>The route check's refusals at the end of the walk.</summary>
    public int ConfirmFailures { get; init; }

    /// <summary>Replans.</summary>
    public int Replans { get; init; }

    /// <summary>Metres of the last route.</summary>
    public float RouteMetres { get; init; }

    /// <summary>Metres the body moved over the ground.</summary>
    public double WalkedMetres { get; init; }

    /// <summary>Metres from the body to the goal at the end.</summary>
    public float LeftMetres { get; init; }

    /// <summary>Simulated seconds.</summary>
    public double SimSeconds { get; init; }

    /// <summary>Tile builds that threw.</summary>
    public int TileFailures { get; init; }

    /// <summary>Where each rung happened, in order: simulated time, cell and position.</summary>
    public IReadOnlyList<string> RungsAt { get; init; } = [];

    /// <summary>Why the route check refused each time, in order: the probe, where along the route, the cell and what it found.</summary>
    public IReadOnlyList<string> RefusedWhy { get; init; } = [];

    /// <summary>Wall-clock milliseconds; the one field that does not repeat.</summary>
    public double WallMilliseconds { get; init; }

    /// <summary>The cell the body ended in (0 when it never started): an arrival's point says where it stands, this says which cell the physics holds it in.</summary>
    public uint EndCellId { get; init; }

    /// <summary>True when the walk arrived with no recovery rung and no route refusal: the only outcome that counts as OK.</summary>
    public bool Clean => State == NavRunState.Arrived && Rungs == 0 && ConfirmFailures == 0 && TileFailures == 0;
}

/// <summary>A <see cref="TimeProvider"/> that moves only when told to, so a walk's timing does not depend on how fast the machine ran it.</summary>
public sealed class SimulatedTime : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _ticks;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp() => _ticks;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(_ticks);

    /// <summary>Moves the clock on.</summary>
    /// <param name="seconds">Seconds to add.</param>
    public void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
}

/// <summary>
/// One walk at a time on the real dats with no server and no Godot (world walk W0): the headless streamer, the retail physics walker, the route check and the runner on an offline session, on
/// simulated time, as the island's walks run (<c>IslandRunDatTests</c>). Each walk gets a new world, map and session, so no walk depends on the one before.
/// </summary>
/// <remarks>
/// <para><b>Repeatability.</b> The clock is <see cref="SimulatedTime"/>. Simulated time stands still while the streamer has work, so how long a landblock took to build cannot change what the body meets.</para>
/// <para><b>Threads.</b> A rig is used by one thread. Many rigs may share <paramref name="dats"/>, <paramref name="geometry"/>, <paramref name="animations"/> and <paramref name="build"/>.</para>
/// </remarks>
/// <param name="dats">The dats.</param>
/// <param name="geometry">The shared geometry library.</param>
/// <param name="animations">The shared animation source.</param>
/// <param name="build">Builds or fetches a landblock's collision.</param>
/// <param name="log">Where the walks log.</param>
public sealed class OfflineWalkRig(DatSet dats, PhysicsGeometryLibrary geometry, DatAnimationSource animations, Func<LandblockCoord, CancellationToken, LandblockCollision> build, ILogSink log)
{
    /// <summary>The reason of a walk whose body could not be placed at its start.</summary>
    public const string StartRefused = "start-refused";

    /// <summary>The reason of a walk that ran out of simulated time.</summary>
    public const string OverTime = "over-time";

    /// <summary>The reason of a walk whose streamer did not settle in <see cref="StreamerStallSeconds"/>.</summary>
    public const string StreamerStalled = "streamer-stalled";

    /// <summary>Wall-clock seconds the streamer may take to settle before the walk stops by name. Not retail: Lifestone's own.</summary>
    public const double StreamerStallSeconds = 120;

    private WorldRouteSearch? _routeSearch;

    private const double Fps = 60.0;
    private const double FollowSeconds = 0.5;

    /// <summary>Walks one request to its end.</summary>
    /// <param name="request">The walk.</param>
    /// <returns>What happened.</returns>
    public OfflineWalkResult Walk(OfflineWalkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wall = Stopwatch.StartNew();
        var time = new SimulatedTime();
        using var session = GameSession.Offline(_ => { }, time, log);
        var world = new PhysicsWorld();
        var map = new NavMap();
        using var streamer = new HeadlessStreamer(dats, geometry, world, map, log, workers: 1, build: build);
        streamer.EnsureNow(request.Start.CellId);
        request.TamperAfterAttach?.Invoke(map);

        var now = 0.0;
        using var audit = new MovementAudit(new NullMovementSink(), session.Events, () => now, request.Name);
        var walker = OfflineWalker.Create(dats, world, log, new OfflinePlayerWeenie(request.RunSkill), geometry, animations, sink: audit);
        session.Actions.Move.Bind(walker.Controller, walker.Controller.Options.RunByDefault);
        world.UseTime(0.0);
        if (!walker.Place(request.Start))
        {
            return new OfflineWalkResult { Name = request.Name, Reason = StartRefused, WallMilliseconds = wall.Elapsed.TotalMilliseconds };
        }

        if (request.Objects is { Count: > 0 } objects)
        {
            var host = new ServerObjectPhysicsHost(world, dats, geometry, log);
            foreach (var o in objects)
            {
                host.Create(o.Description, o.Position);
            }
        }

        using var confirmer = new RouteConfirmer(world, walker.Physics.Setup!);
        var source = request.PathSource?.Invoke(map) ?? new OutdoorPathSource(new PathPlanner(map));
        if (request.Routes is { } routes && ((source as OutdoorPathSource)?.Planner ?? (source as IndoorOutdoorPathSource)?.Planner) is { } planner)
        {
            planner.WorldRoutes = _routeSearch ??= new WorldRouteSearch(routes);
        }

        using var runner = new NavRunner(session.Actions.Move, new ControllerNavBody(walker.Controller), source, request.NoRouteCheck ? null : confirmer, audit, new NavRunnerOptions { Name = request.Name, ScarClock = time }, log);
        runner.Start(request.Goal, now);

        var rungsAt = new List<string>();
        var rungs = 0;
        var refusedWhy = new List<string>();
        var refusals = 0;
        var walked = 0.0;
        var last = NavMap.FromCellFrame(walker.Position).Global;
        var nextFollow = 0.0;
        var stalled = false;
        while (!runner.IsDone && now < request.SimSecondsLimit && !stalled)
        {
            now += 1.0 / Fps;
            time.Advance(1.0 / Fps);
            if (now >= nextFollow)
            {
                nextFollow = now + FollowSeconds;
                streamer.FollowRoute(runner, walker.Position);
                stalled = !Settle(streamer);
            }

            streamer.Pump();
            walker.Controller.Tick(now);
            runner.Step(now);
            session.Tick(now);

            if (runner.ConfirmFailures != refusals)
            {
                refusals = runner.ConfirmFailures;
                refusedWhy.Add(runner.LastConfirmFailure?.ToString() ?? "unknown");
            }

            var here = NavMap.FromCellFrame(walker.Position).Global;
            walked += System.Numerics.Vector2.Distance(here, last);
            last = here;
            if (runner.Rungs != rungs)
            {
                rungs = runner.Rungs;
                var p = walker.Position;
                rungsAt.Add(string.Create(CultureInfo.InvariantCulture, $"t={now:F1} 0x{p.CellId:X8} ({p.Origin.X:F1},{p.Origin.Y:F1},{p.Origin.Z:F2})"));
            }
        }

        var done = runner.IsDone;
        var left = System.Numerics.Vector2.Distance(NavMap.FromCellFrame(walker.Position).Global, request.Goal.Point.Global);
        var reason = stalled ? StreamerStalled : !done ? OverTime : runner.State == NavRunState.Arrived ? null : runner.FailureReason ?? runner.State.ToString();
        return new OfflineWalkResult
        {
            Name = request.Name,
            State = done ? runner.State : null,
            Reason = reason,
            Rungs = runner.Rungs,
            ConfirmFailures = runner.ConfirmFailures,
            Replans = runner.Replans,
            RouteMetres = runner.RouteMetres,
            WalkedMetres = walked,
            LeftMetres = left,
            SimSeconds = now,
            TileFailures = streamer.TileFailures,
            RungsAt = rungsAt,
            RefusedWhy = refusedWhy,
            WallMilliseconds = wall.Elapsed.TotalMilliseconds,
            EndCellId = walker.Position.CellId,
        };
    }

    /// <summary>Pumps until the streamer has nothing in hand, bounded by <see cref="StreamerStallSeconds"/> (wall clock: the streamer's workers run on it). Simulated time does not move meanwhile.</summary>
    private static bool Settle(HeadlessStreamer streamer)
    {
        var start = Stopwatch.GetTimestamp();
        streamer.Pump();
        while (streamer.Busy)
        {
            if (Stopwatch.GetElapsedTime(start).TotalSeconds > StreamerStallSeconds)
            {
                return false;
            }

            Thread.Sleep(1);
            streamer.Pump();
        }

        return true;
    }
}
