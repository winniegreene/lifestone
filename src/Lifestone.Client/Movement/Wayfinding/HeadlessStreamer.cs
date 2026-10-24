using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The headless host's collision streamer (P5a S6): the <see cref="CollisionStreamer"/> over a two-thread <see cref="WorkerPool"/>, pumped once
/// a frame on a <see cref="FrameBudgetScheduler"/>, so a landblock's 300 to 500 ms build runs on a worker and the game thread (the net pump)
/// only pays for the attach; and, when it is given a <see cref="NavMap"/>, the landblock's <see cref="NavTile"/> built on a worker after each
/// attach and attached to the map, detached with the landblock. Not retail: the wayfinder is Lifestone's own, the 4 ms budget and the
/// two workers are Lifestone's constants.
/// </summary>
/// <remarks>
/// <para><b>Shadows.</b> A tile is built with the attached neighbours (<see cref="NavBuildOptions.Neighbours"/>): a static of a neighbour whose footprint
/// reaches over the border is stamped as a "shadow". When a landblock attaches after its neighbour's tile was built and has shadows that fall in that
/// neighbour, the neighbour's tile is built again (counted in <see cref="TileRebuilds"/>); a tile built after its neighbours needs nothing more. When a
/// landblock detaches, the neighbours' tiles keep its old shadows (conservative: an obstacle that is no longer there is avoided, never walked into),
/// and the tile is built fresh when the landblock attaches again. A tile build whose landblock was released meanwhile is dropped.</para>
/// <para><b>Threads.</b> The collision build and the tile build run on the workers (read-only on the attached blocks); every attach, detach, event
/// and <see cref="NavMap"/> call is on the pumping thread, as the map requires. The headless <c>wayfind</c> scenario pumps it from its step.</para>
/// <para><b>Hosts that need the collision at once</b> (the hunt's own body placed by a teleport, the laps scenario before the session exists) call
/// <see cref="EnsureNow"/>, which builds what the cell needs in parallel on the workers and waits for it.</para>
/// </remarks>
public sealed class HeadlessStreamer : IDisposable
{
    private const string LogCategory = "streamer";

    /// <summary>The worker threads. Not retail: Lifestone's own (a 2 km run needs a landblock every 40 s or so).</summary>
    public const int WorkerCount = 2;

    /// <summary>The scheduler's budget for the attaches of one frame, in milliseconds. Not retail: Lifestone's own (the Godot host uses its frame budget).</summary>
    public const double AttachBudgetMilliseconds = 4;

    private readonly PhysicsWorld _world;
    private readonly NavMap? _map;
    private readonly ILogSink _log;
    private readonly WorkerPool _workers;
    private readonly FrameBudgetScheduler _budget = new(TimeSpan.FromMilliseconds(AttachBudgetMilliseconds));
    private readonly CollisionStreamer _collision;
    private readonly NavBuildOptions _tileOptions;
    private readonly Dictionary<LandblockCoord, TileJob> _jobs = [];
    private readonly List<LandblockCoord> _scratch = [];
    private readonly HashSet<LandblockCoord> _waterLogged = [];
    private bool _disposed;

    /// <summary>A streamer for <paramref name="world"/> (and <paramref name="map"/>, when it is not null).</summary>
    /// <param name="dats">The dats the collision is built from.</param>
    /// <param name="geometry">The geometry library the builds share (null: made here).</param>
    /// <param name="world">The world the collision is attached to.</param>
    /// <param name="map">The map the tiles are attached to, or null for collision only.</param>
    /// <param name="log">Where it logs.</param>
    /// <param name="workers">The worker threads (default <see cref="WorkerCount"/>).</param>
    /// <param name="tileOptions">The tile build's options without neighbours (default <see cref="NavBuildOptions.Default"/>).</param>
    public HeadlessStreamer(DatSet dats, PhysicsGeometryLibrary? geometry, PhysicsWorld world, NavMap? map, ILogSink log, int workers = WorkerCount, NavBuildOptions? tileOptions = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(log);
        var builder = new LandblockCollisionBuilder(dats, geometry ?? new PhysicsGeometryLibrary(dats));
        (_world, _map, _log) = (world, map, log);
        _tileOptions = tileOptions ?? NavBuildOptions.Default;
        _workers = new WorkerPool(workers);
        _collision = new CollisionStreamer(world, (coord, cancellation) => builder.Build(coord, cancellation), _workers, log);
        _collision.BlockAttached += OnAttached;
        _collision.BlockReleased += OnReleased;
        _budget.Error = e => _log.Error(LogCategory, "an attach on the frame budget threw", new LogField("error", e.GetType().Name), new LogField("message", e.Message));
    }

    /// <summary>The streamer underneath (the focus, the corridor, the landblock states).</summary>
    public CollisionStreamer Collision => _collision;

    /// <summary>The world the collision is attached to.</summary>
    public PhysicsWorld World => _world;

    /// <summary>The map the tiles are attached to, or null.</summary>
    public NavMap? Map => _map;

    /// <summary>The most landblocks the world held at once, sampled after each pump.</summary>
    public int PeakAttached { get; private set; }

    /// <summary>The most tiles the map held at once, sampled after each pump (0 without a map).</summary>
    public int PeakTiles { get; private set; }

    /// <summary>Tiles built (the first build of each attach).</summary>
    public int TileBuilds { get; private set; }

    /// <summary>Tiles built again because a landblock attached later and casts shadows into them.</summary>
    public int TileRebuilds { get; private set; }

    /// <summary>Tile builds that threw (each logged once; the landblock counts as settled without a tile).</summary>
    public int TileFailures { get; private set; }

    /// <summary>The longest one tile build took on its worker, in milliseconds.</summary>
    public double TileBuildMaxMilliseconds { get; private set; }

    /// <summary>The longest one <see cref="NavMap.Attach"/> took on the pumping thread, in milliseconds.</summary>
    public double TileAttachMaxMilliseconds { get; private set; }

    /// <summary>The longest one <see cref="Pump"/> took on the pumping thread (the stall the streamer adds to a frame), in milliseconds.</summary>
    public double PumpMaxMilliseconds { get; private set; }

    /// <summary>The longest single attach the frame-budget scheduler ran (<c>PhysicsWorld.Attach</c> of one landblock), in milliseconds.</summary>
    public double AttachMaxMilliseconds => _budget.MaxItemMilliseconds;

    /// <summary>Landblocks attached by the streamer so far (collision).</summary>
    public int Attaches { get; private set; }

    /// <summary>Landblocks detached by the streamer so far.</summary>
    public int Releases { get; private set; }

    /// <summary>Tile builds not finished yet.</summary>
    public int TilesPending
    {
        get
        {
            var n = 0;
            foreach (var job in _jobs.Values)
            {
                if (job.Task is not null)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The landblocks beyond the focus ring the corridor names. Not retail: Lifestone's own (a ring of 1 plus 2 is about 11 landblocks held on a straight run).</summary>
    public const int AheadLandblocks = 2;

    /// <summary>The dropped corridor landblocks that stay attached. Not retail: Lifestone's own.</summary>
    public const int KeepBehind = 1;

    /// <summary>
    /// Follows a run: the focus is the body's cell, and the corridor the next <see cref="AheadLandblocks"/> landblocks of the runner's route beyond the focus ring (its path, then the
    /// straight line from the path's end to the goal while the path only reaches the horizon of the attached tiles; <see cref="AheadBlocks"/>), keeping <see cref="KeepBehind"/> dropped ones.
    /// Call it a couple of times a second. Returns whether the corridor changed.
    /// </summary>
    /// <param name="runner">The run (its <see cref="NavRunner.Path"/> and <see cref="NavRunner.Goal"/>).</param>
    /// <param name="body">Where the body is.</param>
    public bool FollowRoute(NavRunner runner, in CellFrame body)
    {
        ArgumentNullException.ThrowIfNull(runner);
        SetFocus(body.CellId);
        var here = NavMap.FromCellFrame(body).Global;
        var waypoints = runner.Path is { } path ? path.Waypoints.Select(w => w.Point.Global).ToArray() : [];
        var goal = runner.Goal?.Point.Global ?? here;
        var ahead = AheadBlocks(here, waypoints, goal, AheadLandblocks);
        var changed = !ahead.Select(id => LandblockCoord.FromId(id)).SequenceEqual(_collision.Corridor);
        _collision.SetCorridor(ahead, KeepBehind);
        return changed;
    }

    /// <summary>
    /// The next landblocks of a route that lie beyond the ring of <paramref name="ring"/> landblocks round the body's: the polyline body, the waypoints from the nearest one on, then the goal is walked in
    /// 24 m steps and each new landblock outside the ring is taken, in order, until <paramref name="count"/> are found. Ids are landblock ids (<c>0xXXYY0000</c>).
    /// </summary>
    /// <param name="body">The body's global position in metres.</param>
    /// <param name="waypoints">The runner's waypoints as global metres (may be empty).</param>
    /// <param name="goal">The goal's global position in metres (the line to it continues a path that only reaches the horizon).</param>
    /// <param name="count">How many landblocks to name.</param>
    /// <param name="ring">The Chebyshev radius round the body's landblock that the focus covers already.</param>
    public static IReadOnlyList<uint> AheadBlocks(Vector2 body, IReadOnlyList<Vector2> waypoints, Vector2 goal, int count, int ring = 1)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        var found = new List<uint>(count);
        if (count <= 0)
        {
            return found;
        }

        var nearest = -1;
        var best = float.MaxValue;
        for (var i = 0; i < waypoints.Count; i++)
        {
            var d = Vector2.DistanceSquared(body, waypoints[i]);
            if (d < best)
            {
                (best, nearest) = (d, i);
            }
        }

        var (bx, by) = BlockOf(body);
        var previous = body;
        for (var i = nearest + 1; i <= waypoints.Count && found.Count < count; i++)
        {
            var next = i < waypoints.Count ? waypoints[i] : goal;
            var length = Vector2.Distance(previous, next);
            for (var s = 0f; s <= length; s += 24f)
            {
                Take(Vector2.Lerp(previous, next, length <= 0 ? 0 : s / length));
            }

            Take(next);
            previous = next;
        }

        return found;

        void Take(Vector2 point)
        {
            var (x, y) = BlockOf(point);
            if (Math.Max(Math.Abs(x - bx), Math.Abs(y - by)) > ring)
            {
                var id = ((uint)x << 24) | ((uint)y << 16);
                if (found.Count < count && !found.Contains(id))
                {
                    found.Add(id);
                }
            }
        }
    }

    private static (int X, int Y) BlockOf(Vector2 global) =>
        (Math.Clamp((int)MathF.Floor(global.X / 192f), 0, LandDefs.BlocksPerSide - 1), Math.Clamp((int)MathF.Floor(global.Y / 192f), 0, LandDefs.BlocksPerSide - 1));

    /// <summary>True while a collision build is waiting for or running on a worker, an attach waits on the frame budget or a tile is not built or attached yet.</summary>
    public bool Busy => _workers.Running + _workers.Waiting > 0 || _budget.Pending > 0 || TilesPending > 0;

    /// <summary>
    /// The player (or the route) is in <paramref name="cellId"/>: <see cref="CollisionStreamer.SetFocus"/>, with a hysteresis of one land cell (24 m): a body that steps into the
    /// edge row or column of the next landblock keeps the old focus until it is a cell deeper, so a route that hugs a landblock border does not release and build the same
    /// three landblocks over and over (the first run of the scripted wayfind test attached 39 landblocks for a 124 m walk without it). A jump of more than one landblock, an
    /// EnvCell and a move within the focus's own landblock take effect at once. Not retail: Lifestone's own.
    /// </summary>
    /// <param name="cellId">The cell the body is in.</param>
    public void SetFocus(uint cellId)
    {
        if (!HeldByHysteresis(cellId))
        {
            _collision.SetFocus(cellId);
        }
    }

    private bool HeldByHysteresis(uint cellId)
    {
        var old = _collision.FocusCellId;
        if (old == 0 || !LandDefs.IsOutdoorCell(old) || !LandDefs.IsOutdoorCell(cellId))
        {
            return false;
        }

        var (a, b) = (LandblockCoord.FromId(old), LandblockCoord.FromId(cellId));
        int dx = b.X - a.X, dy = b.Y - a.Y;
        if ((dx == 0 && dy == 0) || Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
        {
            return false;
        }

        var low = (int)(cellId & 0xFFFF) - 1;
        int cx = low / 8, cy = low % 8;
        return (dx == 1 && cx < 1) || (dx == -1 && cx > 6) || (dy == 1 && cy < 1) || (dy == -1 && cy > 6);
    }

    /// <summary>The route's next landblocks: <see cref="CollisionStreamer.SetCorridor"/>.</summary>
    public void SetCorridor(IReadOnlyList<uint> ahead, int keepBehind) => _collision.SetCorridor(ahead, keepBehind);

    /// <summary>
    /// One frame: starts and queues what the focus and the corridor need, runs the attaches within the budget, and attaches the tiles that
    /// finished. Never waits for a worker.
    /// </summary>
    public void Pump()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var start = Stopwatch.GetTimestamp();
        _collision.Pump(_budget);
        _budget.Drain();
        CollectTiles(waitForAll: false);
        Sample();
        PumpMaxMilliseconds = Math.Max(PumpMaxMilliseconds, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>
    /// Focuses on <paramref name="cellId"/> and builds, attaches and (with a map) tiles everything it needs before it returns: the collision builds run
    /// in parallel on the workers and the call waits for them. For a host that must place a body at once; the frame loop uses <see cref="Pump"/>.
    /// </summary>
    /// <param name="cellId">The cell to focus on.</param>
    public void EnsureNow(uint cellId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _collision.SetFocus(cellId);
        _collision.PumpSynchronously();
        CollectTiles(waitForAll: true);
        Sample();
    }

    /// <summary>
    /// True when the landblock of <paramref name="cellId"/> has settled: its collision is attached or failed, and its tile (with a map) is attached,
    /// or failed, or was never started because the collision failed.
    /// </summary>
    /// <param name="cellId">A cell id or a landblock id.</param>
    public bool IsSettled(uint cellId)
    {
        var coord = LandblockCoord.FromId(cellId);
        var state = _collision.StateOf(coord);
        if (state == AttachState.Pending)
        {
            return false;
        }

        if (_map is null || state == AttachState.Failed)
        {
            return true;
        }

        return !_jobs.TryGetValue(coord, out var job) || job.Task is null && (job.Done || job.Failed);
    }

    /// <summary>The state line for the log: what is attached and what the pump cost.</summary>
    public string StatsLine() => string.Create(
        CultureInfo.InvariantCulture,
        $"landblocks attached {_world.AttachedCount} (peak {PeakAttached}), tiles {_map?.AttachedLandblocks.Count ?? 0} (peak {PeakTiles}), attaches {Attaches}, releases {Releases}, tile builds {TileBuilds} (+{TileRebuilds} rebuilt, {TileFailures} failed), pump max {PumpMaxMilliseconds:F1} ms, attach max {AttachMaxMilliseconds:F1} ms, tile build max {TileBuildMaxMilliseconds:F1} ms, tile attach max {TileAttachMaxMilliseconds:F1} ms");

    /// <summary>
    /// A physics world with the collision of <paramref name="centre"/> and the eight landblocks around it attached (the scenarios that need the
    /// ring before a session exists): a streamer built, run once and disposed. The collision stays attached.
    /// </summary>
    public static PhysicsWorld AttachAround(DatSet dats, PhysicsGeometryLibrary geometry, LandblockCoord centre)
    {
        var world = new PhysicsWorld();
        using var streamer = new HeadlessStreamer(dats, geometry, world, null, NullLogSink.Instance);
        streamer.EnsureNow(centre.LandBlockId);
        return world;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _collision.BlockAttached -= OnAttached;
        _collision.BlockReleased -= OnReleased;
        foreach (var job in _jobs.Values)
        {
            job.Cancel?.Cancel();
        }

        _collision.Dispose();
        _workers.Dispose();
    }

    private void Sample()
    {
        PeakAttached = Math.Max(PeakAttached, _world.AttachedCount);
        if (_map is not null)
        {
            PeakTiles = Math.Max(PeakTiles, _map.AttachedLandblocks.Count);
        }
    }

    private void OnAttached(LandblockCoord coord, LandblockCollision block)
    {
        Attaches++;
        if (_map is null)
        {
            return;
        }

        StartTile(coord, rebuild: false);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && coord.Offset(dx, dy) is { } near && _jobs.TryGetValue(near, out var job) && (job.Done || job.Task is not null) && CastsShadowsInto(block, near))
                {
                    StartTile(near, rebuild: true);
                }
            }
        }
    }

    private void OnReleased(LandblockCoord coord)
    {
        Releases++;
        if (_map is null)
        {
            return;
        }

        if (_jobs.Remove(coord, out var job))
        {
            job.Cancel?.Cancel();
            ObserveFault(job.Task);
        }

        _map.Detach(TileId(coord));
    }

    private static bool CastsShadowsInto(LandblockCollision block, LandblockCoord target)
    {
        foreach (var shadow in block.ForeignShadows)
        {
            if (LandblockCoord.FromId(shadow.CellId) == target)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts the tile build of an attached landblock on a worker with the neighbours attached now; a build still running for it is cancelled and dropped.</summary>
    private void StartTile(LandblockCoord coord, bool rebuild)
    {
        if (_world.GetLandblock(coord) is not { } block)
        {
            return;
        }

        var neighbours = new List<LandblockCollision>(8);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && coord.Offset(dx, dy) is { } near && _world.GetLandblock(near) is { } other)
                {
                    neighbours.Add(other);
                }
            }
        }

        if (!_jobs.TryGetValue(coord, out var job))
        {
            _jobs.Add(coord, job = new TileJob());
        }

        job.Cancel?.Cancel();
        ObserveFault(job.Task);
        var cancel = job.Cancel = new CancellationTokenSource();
        var options = _tileOptions with { Neighbours = neighbours };
        job.Failed = false;
        job.Rebuild = rebuild;
        job.Task = _workers.RunSync(
            token =>
            {
                token.ThrowIfCancellationRequested();
                return NavTileBuilder.Build(block, options);
            },
            cancel.Token);
    }

    /// <summary>
    /// TODO(nav-water-corner), where it bites: a tile with water nodes is flagged by the whole 24 m land cell (the finer corner data is private to
    /// the collision set), so a shore walk may be refused that retail allows. Logged once per landblock, with the node count.
    /// </summary>
    private void LogWaterOnce(LandblockCoord coord, NavTile tile)
    {
        if (!_waterLogged.Add(coord))
        {
            return;
        }

        var water = 0;
        foreach (var flag in tile.Flags)
        {
            if ((flag & NavFlags.Water) != 0)
            {
                water++;
            }
        }

        if (water > 0)
        {
            _log.Info(LogCategory, "TODO(nav-water-corner): water is flagged by whole land cells; a shore may be refused that retail allows", new LogField("landblock", coord.ToString()), new LogField("water_nodes", water));
        }
    }

    /// <summary>Attaches the finished tiles to the map (all of them waiting for the workers when <paramref name="waitForAll"/>, else within the frame budget).</summary>
    private void CollectTiles(bool waitForAll)
    {
        if (_map is null || _jobs.Count == 0)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        _scratch.Clear();
        foreach (var (coord, job) in _jobs)
        {
            if (job.Task is not null)
            {
                _scratch.Add(coord);
            }
        }

        foreach (var coord in _scratch)
        {
            var job = _jobs[coord];
            var task = job.Task!;
            if (!task.IsCompleted)
            {
                if (!waitForAll)
                {
                    continue;
                }

                try
                {
                    task.Wait();
                }
                catch (AggregateException)
                {
                    // handled below through the task's state
                }
            }

            if (!waitForAll && Stopwatch.GetElapsedTime(start).TotalMilliseconds > AttachBudgetMilliseconds)
            {
                break; // the rest next frame (a tile attach is a few milliseconds)
            }

            job.Task = null;
            job.Cancel = null;
            if (task.IsCanceled)
            {
                continue;
            }

            if (!task.IsCompletedSuccessfully)
            {
                job.Failed = true;
                TileFailures++;
                var error = task.Exception?.GetBaseException();
                _log.Error(
                    LogCategory,
                    "a tile failed to build; the landblock counts as settled without a tile",
                    new LogField("landblock", coord.ToString()),
                    new LogField("error", error?.GetType().Name ?? "unknown"),
                    new LogField("message", error?.Message ?? "the build did not finish"));
                continue;
            }

            var tile = task.Result;
            var at = Stopwatch.GetTimestamp();
            _map.Attach(tile);
            TileAttachMaxMilliseconds = Math.Max(TileAttachMaxMilliseconds, Stopwatch.GetElapsedTime(at).TotalMilliseconds);
            TileBuildMaxMilliseconds = Math.Max(TileBuildMaxMilliseconds, tile.BuildMilliseconds);
            job.Done = true;
            LogWaterOnce(coord, tile);
            if (job.Rebuild)
            {
                TileRebuilds++;
            }
            else
            {
                TileBuilds++;
            }
        }

        _scratch.Clear();
    }

    private static uint TileId(LandblockCoord coord) => ((uint)coord.X << 24) | ((uint)coord.Y << 16);

    private static void ObserveFault(Task? task) =>
        task?.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private sealed class TileJob
    {
        public Task<NavTile>? Task { get; set; }

        public CancellationTokenSource? Cancel { get; set; }

        public bool Done { get; set; }

        public bool Failed { get; set; }

        public bool Rebuild { get; set; }
    }
}
