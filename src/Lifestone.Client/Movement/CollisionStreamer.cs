using Lifestone.Client.Diagnostics;
using Lifestone.Client.Objects;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement;

/// <summary>
/// Keeps the collision around the player attached to a <see cref="PhysicsWorld"/>: it builds each needed landblock's
/// <see cref="LandblockCollision"/> on the worker pool and attaches it within the frame budget, and answers whether a
/// landblock's collision has settled, for portal space's destination readiness (<see cref="IWorldAttachments.Collision"/>).
/// </summary>
/// <remarks>
/// <para><b>Which landblocks</b> (<see cref="SetFocus"/>). Outdoors, the player's landblock and its 3x3 neighbours (the
/// retail object rings, <see cref="ObjectRings.RetailRadius"/>). In an EnvCell, the cell's landblock, plus its 3x3 when
/// the cell can see outside (owner decision D29); whether it can is known once the cell's own landblock is attached, so
/// the 3x3 is requested then. Landblocks off the map are left out.</para>
/// <para><b>Hosts.</b> The game calls <see cref="Pump"/> once per frame with the shared main-thread scheduler (attaches
/// are queued on it, nearest first, and run within its budget); the headless host calls <see cref="PumpSynchronously"/>,
/// which builds on the calling thread and attaches everything needed before it returns.</para>
/// <para><b>Release.</b> A landblock that leaves the focus has its build cancelled, or is detached on the next pump
/// (objects in it go to the lost cell until it is attached again, <see cref="PhysicsWorld.Detach"/>).</para>
/// <para><b>Failures.</b> A landblock whose build throws is <see cref="AttachState.Failed"/>, which counts as settled, and
/// is logged as an error (category "physics") once, so a broken landblock can never hold the portal tunnel open. It is
/// built again only after it has left the focus and come back.</para>
/// <para><b>Collision attached by someone else</b> (the host or a test called <see cref="PhysicsWorld.Attach"/> itself) is
/// left alone: the streamer does not build that landblock, never replaces it with its own build (which would send the
/// objects in it to the lost cell and back), and never detaches it. <see cref="StateOf"/> reports it
/// <see cref="AttachState.Attached"/>. The streamer owns a landblock only while the collision attached for it is the very
/// <see cref="LandblockCollision"/> the streamer attached (<see cref="PhysicsWorld.GetLandblock"/>), so collision someone
/// else attaches over the streamer's (<see cref="PhysicsWorld.Attach"/> replaces) is theirs from then on. A needed
/// landblock that someone else detaches is built again on the next pump.</para>
/// <para>Main thread (or the headless tick) only, except the build function, which runs on the workers.
/// <see cref="Pump"/> allocates nothing while no build finishes.</para>
/// </remarks>
public sealed class CollisionStreamer : IDisposable
{
    private const string LogCategory = "physics";

    private readonly Func<LandblockCoord, CancellationToken, LandblockCollision> _build;
    private readonly WorkerPool _workers;
    private readonly ILogSink _log;
    private readonly Dictionary<LandblockCoord, Entry> _entries = [];
    private readonly List<LandblockCoord> _scratch = [];
    private uint _focusCellId;
    private bool _hasFocus;
    private bool _focusOutsideKnown;
    private bool _disposed;

    /// <summary>Creates a streamer for <paramref name="world"/>.</summary>
    /// <param name="world">The world the collision is attached to.</param>
    /// <param name="build">Builds one landblock's collision; called on a worker thread (or the caller's, in
    /// <see cref="PumpSynchronously"/>); may throw (the landblock then fails).</param>
    /// <param name="workers">The pool the builds run on.</param>
    /// <param name="log">Where failures are reported.</param>
    public CollisionStreamer(
        PhysicsWorld world,
        Func<LandblockCoord, CancellationToken, LandblockCollision> build,
        WorkerPool workers,
        ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(log);
        World = world;
        _build = build;
        _workers = workers;
        _log = log;
    }

    /// <summary>The world the collision is attached to.</summary>
    public PhysicsWorld World { get; }

    /// <summary>The cell of the last <see cref="SetFocus"/>, or 0.</summary>
    public uint FocusCellId => _focusCellId;

    /// <summary>Landblocks requested by the focus now (attached, building, waiting to attach, or failed).</summary>
    public int RequestedCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _entries.Values)
            {
                if (entry.Needed)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Builds that failed since the streamer was made.</summary>
    public int FailureCount { get; private set; }

    /// <summary>
    /// The player is now in <paramref name="playerCellId"/>: requests the landblocks it needs (builds start on the
    /// workers) and releases the ones it no longer needs (cancelled, or detached on a later pump). Cheap when the cell
    /// does not change.
    /// </summary>
    public void SetFocus(uint playerCellId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hasFocus && playerCellId == _focusCellId)
        {
            return;
        }

        _hasFocus = true;
        _focusCellId = playerCellId;
        _focusOutsideKnown = false;
        UpdateNeeded();
    }

    /// <summary>
    /// Queues the attach of every finished build on <paramref name="budget"/> (nearest the focus first; the host's drain
    /// runs them within its time budget), logs failed builds, detaches released landblocks, and, once an EnvCell focus's
    /// landblock is attached, requests its 3x3 when it can see outside. Once per frame on the main thread.
    /// </summary>
    public void Pump(FrameBudgetScheduler budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ObjectDisposedException.ThrowIf(_disposed, this);
        RefreshFocusOutside();
        _scratch.Clear();
        foreach (var entry in _entries.Values)
        {
            if (!entry.Needed)
            {
                _scratch.Add(entry.Coord);
                continue;
            }

            if (entry.Build is null)
            {
                // Nothing building: attached, failed, or detached by someone else since (only then is a build started).
                StartBuild(entry);
                continue;
            }

            if (entry.Build is not { IsCompleted: true } task || entry.AttachQueued)
            {
                continue;
            }

            if (!task.IsCompletedSuccessfully)
            {
                Fail(entry, task.Exception?.GetBaseException());
                continue;
            }

            entry.AttachQueued = true;
            var block = task.Result;
            budget.Enqueue(() => Attach(entry, block), Ring(entry.Coord));
        }

        ReleaseScratch();
    }

    /// <summary>
    /// Builds (on the calling thread, or by waiting for a build already running) and attaches every landblock the focus
    /// needs, including an EnvCell focus's 3x3 when it sees outside, and detaches released ones, before it returns
    /// (headless host). Failures are logged and settle as <see cref="AttachState.Failed"/>.
    /// </summary>
    public void PumpSynchronously()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (var pass = 0; pass < 2; pass++)
        {
            _scratch.Clear();
            foreach (var entry in _entries.Values)
            {
                _scratch.Add(entry.Coord);
            }

            foreach (var coord in _scratch)
            {
                var entry = _entries[coord];
                if (!entry.Needed || entry.Failed || Owns(entry))
                {
                    continue;
                }

                if (World.IsAttached(coord))
                {
                    Disown(entry);
                    continue;
                }

                LandblockCollision block;
                try
                {
                    block = entry.Build is { } task ? task.GetAwaiter().GetResult() : _build(coord, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Fail(entry, e);
                    continue;
                }

                Attach(entry, block);
            }

            if (!RefreshFocusOutside())
            {
                break;
            }
        }

        _scratch.Clear();
        foreach (var entry in _entries.Values)
        {
            if (!entry.Needed)
            {
                _scratch.Add(entry.Coord);
            }
        }

        ReleaseScratch();
    }

    /// <summary>
    /// Whether the landblock's collision has settled: <see cref="AttachState.Attached"/> while it is attached to the world,
    /// <see cref="AttachState.Failed"/> when its build failed, and <see cref="AttachState.Pending"/> while it is requested
    /// or building, and for a landblock the focus does not need.
    /// </summary>
    public AttachState StateOf(LandblockCoord coord)
    {
        if (World.IsAttached(coord))
        {
            return AttachState.Attached;
        }

        return _entries.TryGetValue(coord, out var entry) && entry.Failed ? AttachState.Failed : AttachState.Pending;
    }

    /// <summary>Cancels the builds still running; attached collision stays in the world.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            entry.Cancel?.Cancel();
        }
    }

    /// <summary>Marks the landblocks the focus needs and starts their builds; marks the others released.</summary>
    private void UpdateNeeded()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Needed = false;
        }

        var center = LandblockCoord.FromId(_focusCellId);
        if (!LandDefs.IsValidCellId(_focusCellId) || !LandDefs.IsOnMap(center))
        {
            CancelReleased();
            return;
        }

        var ring = LandDefs.IsEnvCell(_focusCellId) && !FocusSeesOutside() ? 0 : ObjectRings.RetailRadius;
        for (var dx = -ring; dx <= ring; dx++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                if (center.Offset(dx, dy) is { } coord && LandDefs.IsOnMap(coord))
                {
                    Request(coord);
                }
            }
        }

        CancelReleased();
    }

    /// <summary>
    /// For an EnvCell focus whose landblock has been attached since the last look: whether it sees outside, and when it
    /// does, the 3x3 requested. True when that added landblocks.
    /// </summary>
    private bool RefreshFocusOutside()
    {
        if (!_hasFocus || _focusOutsideKnown || !LandDefs.IsEnvCell(_focusCellId))
        {
            return false;
        }

        if (World.GetCell(_focusCellId) is not PhysEnvCell cell)
        {
            if (_entries.TryGetValue(LandblockCoord.FromId(_focusCellId), out var own) && own.Failed)
            {
                _focusOutsideKnown = true;
            }

            return false;
        }

        _focusOutsideKnown = true;
        if (!cell.SeenOutside)
        {
            return false;
        }

        UpdateNeeded();
        return true;
    }

    /// <summary>True when the focus EnvCell is attached and sees outside (D29).</summary>
    private bool FocusSeesOutside() => World.GetCell(_focusCellId) is PhysEnvCell { SeenOutside: true };

    /// <summary>Marks <paramref name="coord"/> needed and starts its build unless it is built, building or failed.</summary>
    private void Request(LandblockCoord coord)
    {
        if (!_entries.TryGetValue(coord, out var entry))
        {
            entry = new Entry(coord);
            _entries.Add(coord, entry);
        }

        entry.Needed = true;
        StartBuild(entry);
    }

    /// <summary>
    /// Starts the build of <paramref name="entry"/> on the workers unless it is attached (by the streamer or anyone else),
    /// building or failed.
    /// </summary>
    private void StartBuild(Entry entry)
    {
        if (Owns(entry) || entry.Failed || entry.Build is not null || World.IsAttached(entry.Coord))
        {
            return;
        }

        var coord = entry.Coord;
        var cancel = new CancellationTokenSource();
        entry.Cancel = cancel;
        entry.AttachQueued = false;
        entry.Build = _workers.RunSync(token => _build(coord, token), cancel.Token);
    }

    /// <summary>Cancels the builds of released landblocks that are not attached and forgets them.</summary>
    private void CancelReleased()
    {
        _scratch.Clear();
        foreach (var entry in _entries.Values)
        {
            if (!entry.Needed && !Owns(entry))
            {
                _scratch.Add(entry.Coord);
            }
        }

        foreach (var coord in _scratch)
        {
            var entry = _entries[coord];
            entry.Cancel?.Cancel();
            ObserveFault(entry.Build);
            _entries.Remove(coord);
        }

        _scratch.Clear();
    }

    /// <summary>
    /// Detaches the released landblocks in <see cref="_scratch"/> whose collision the streamer still owns, and forgets them
    /// (collision someone else attached, over the streamer's or not, stays).
    /// </summary>
    private void ReleaseScratch()
    {
        foreach (var coord in _scratch)
        {
            if (!_entries.TryGetValue(coord, out var entry) || entry.Needed)
            {
                continue;
            }

            if (Owns(entry))
            {
                World.Detach(coord);
            }

            entry.Cancel?.Cancel();
            ObserveFault(entry.Build);
            _entries.Remove(coord);
        }

        _scratch.Clear();
    }

    /// <summary>
    /// Attaches a finished build, unless the landblock was released meanwhile or someone else attached it (the build is
    /// then dropped and their collision kept).
    /// </summary>
    private void Attach(Entry entry, LandblockCollision block)
    {
        if (Owns(entry) || !entry.Needed || !_entries.TryGetValue(entry.Coord, out var current) || !ReferenceEquals(current, entry))
        {
            return;
        }

        if (World.IsAttached(entry.Coord))
        {
            Disown(entry);
            return;
        }

        World.Attach(block);
        entry.Block = block;
        entry.Build = null;
        entry.Cancel = null;
    }

    /// <summary>
    /// Drops the streamer's build of a landblock that someone else attached: the build is cancelled and forgotten, and the
    /// entry has no <see cref="Entry.Block"/>, so the streamer never detaches their collision.
    /// </summary>
    private static void Disown(Entry entry)
    {
        entry.Cancel?.Cancel();
        ObserveFault(entry.Build);
        entry.Build = null;
        entry.Cancel = null;
        entry.AttachQueued = false;
    }

    /// <summary>
    /// True while the collision attached for <paramref name="entry"/>'s landblock is the one the streamer attached. When
    /// someone else has detached it or attached their own over it, the entry forgets it (and so never detaches theirs).
    /// </summary>
    private bool Owns(Entry entry)
    {
        if (entry.Block is null)
        {
            return false;
        }

        if (ReferenceEquals(World.GetLandblock(entry.Coord), entry.Block))
        {
            return true;
        }

        entry.Block = null;
        return false;
    }

    /// <summary>Settles a landblock as failed and logs it once.</summary>
    private void Fail(Entry entry, Exception? error)
    {
        entry.Failed = true;
        entry.Build = null;
        entry.Cancel = null;
        FailureCount++;
        _log.Error(
            LogCategory,
            "landblock collision failed to build; it counts as settled without collision",
            new LogField("landblock", entry.Coord.ToString()),
            new LogField("error", error?.GetType().Name ?? "unknown"),
            new LogField("message", error?.Message ?? "the build did not finish"));
    }

    /// <summary>The attach priority: the Chebyshev ring around the focus landblock (nearest first).</summary>
    private double Ring(LandblockCoord coord) => ObjectRings.Ring(coord, LandblockCoord.FromId(_focusCellId));

    /// <summary>Marks a dropped build's exception observed, so a cancelled or failed build never surfaces later.</summary>
    private static void ObserveFault(Task? task) =>
        task?.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>One landblock the streamer knows.</summary>
    private sealed class Entry(LandblockCoord coord)
    {
        public LandblockCoord Coord { get; } = coord;

        public bool Needed { get; set; }

        public Task<LandblockCollision>? Build { get; set; }

        public CancellationTokenSource? Cancel { get; set; }

        public bool AttachQueued { get; set; }

        /// <summary>The collision the streamer attached for this landblock, or null; see <see cref="Owns"/>.</summary>
        public LandblockCollision? Block { get; set; }

        public bool Failed { get; set; }
    }
}
