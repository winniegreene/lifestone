using System.Diagnostics;
using Godot;
using Lifestone.Client.Scheduling;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;

namespace Lifestone.Game.World;

/// <summary>Streaming counters for the perf overlay and the fly-path report.</summary>
public sealed class StreamingStats
{
    private readonly Queue<long> _recentAttaches = new();

    public long Started { get; internal set; }

    public long Attached { get; internal set; }

    public long Cancelled { get; internal set; }

    /// <summary>Landblocks cancelled after their attach had started (layers detached part-way).</summary>
    public long AbandonedMidAttach { get; internal set; }

    public long Unloaded { get; internal set; }

    public long LayerFailures { get; internal set; }

    /// <summary>Worker time from start to all layers prepared (wall clock, includes waiting for a worker).</summary>
    public double MaxPrepareMs { get; internal set; }

    public double TotalPrepareMs { get; internal set; }

    /// <summary>Main-thread time spent attaching one landblock (all its steps).</summary>
    public double MaxAttachMs { get; internal set; }

    /// <summary>The slowest single attach step (one budgeted work item).</summary>
    public double MaxAttachStepMs { get; internal set; }

    public double TotalAttachMs { get; internal set; }

    /// <summary>Landblocks attached during the last five seconds, per second.</summary>
    public double RecentAttachesPerSecond
    {
        get
        {
            Expire();
            return _recentAttaches.Count / 5.0;
        }
    }

    /// <summary>Clears the maxima (e.g. when a measured run starts) so they cover only what follows.</summary>
    public void ResetPeaks()
    {
        MaxPrepareMs = 0;
        MaxAttachMs = 0;
        MaxAttachStepMs = 0;
    }

    internal void RecordAttach()
    {
        Attached++;
        _recentAttaches.Enqueue(Stopwatch.GetTimestamp());
        Expire();
    }

    private void Expire()
    {
        var cutoff = Stopwatch.GetTimestamp() - (5 * Stopwatch.Frequency);
        while (_recentAttaches.Count > 0 && _recentAttaches.Peek() < cutoff)
        {
            _recentAttaches.Dequeue();
        }
    }
}

/// <summary>
/// Streams landblocks around the focus: asks <see cref="StreamingPlanner"/> what to load, unload and cancel, runs every
/// registered <see cref="ILandblockLayer"/>'s <c>PrepareAsync</c> on the worker pool, then attaches the results on the
/// main thread through the frame-budgeted queue, nearest landblocks first. Main thread only (except the worker tasks
/// it starts).
/// </summary>
public sealed class LandblockStreamer
{
    /// <summary>Attach steps slower than this are logged (debug level) with their layer.</summary>
    private const double SlowStepMilliseconds = 4.0;

    private readonly StreamingPlanner _planner;
    private readonly IReadOnlyList<ILandblockLayer> _layers;
    private readonly WorkerPool _workers;
    private readonly MainThreadQueue _mainThread;
    private readonly Func<LandblockCoord, Node3D> _createRoot;
    private readonly Dictionary<LandblockCoord, Entry> _entries = new();
    private GlobalPosition _focus;
    private long _updates;

    /// <summary>Creates a streamer.</summary>
    /// <param name="settings">Radii and limits.</param>
    /// <param name="layers">Layers, in attach order (a live list: layers registered later apply to later loads).</param>
    /// <param name="workers">Worker pool for PrepareAsync.</param>
    /// <param name="mainThread">Main-thread queue for attaches and unloads.</param>
    /// <param name="createRoot">Creates and parents a landblock's root node (main thread).</param>
    public LandblockStreamer(
        StreamingSettings settings,
        IReadOnlyList<ILandblockLayer> layers,
        WorkerPool workers,
        MainThreadQueue mainThread,
        Func<LandblockCoord, Node3D> createRoot)
    {
        _planner = new StreamingPlanner(settings);
        _layers = layers;
        _workers = workers;
        _mainThread = mainThread;
        _createRoot = createRoot;
    }

    public StreamingPlanner Planner => _planner;

    public StreamingStats Stats { get; } = new();

    /// <summary>Attaches waiting in the main-thread queue.</summary>
    public int PendingAttaches { get; private set; }

    /// <summary>True once the planner has run and nothing is pending, preparing or waiting to attach (or, after a
    /// teleport, waiting to be freed).</summary>
    public bool IsIdle => _updates > 0 && _planner.IsIdle && PendingAttaches == 0 && _oldRoots.Count == 0;

    /// <summary>Root nodes of attached (or attaching) landblocks.</summary>
    public IEnumerable<(LandblockCoord Coord, Node3D Root)> Roots =>
        _entries.Where(e => e.Value.Root is not null).Select(e => (e.Key, e.Value.Root!));

    /// <summary>One planning step (call once per frame).</summary>
    public void Update(StreamingFocus focus)
    {
        _focus = focus.Position;
        _updates++;
        var plan = _planner.Update(focus);
        foreach (var coord in plan.ToCancel)
        {
            Cancel(coord);
        }

        foreach (var coord in plan.ToUnload)
        {
            Unload(coord);
        }

        foreach (var coord in plan.ToLoad)
        {
            Start(coord);
        }
    }

    /// <summary>Drops everything (teleport): cancels loads and detaches attached landblocks now.</summary>
    /// <remarks>
    /// Navigation fix (M1 wave 3, teleport-frame-freeze): the layers detach now (the interiors' cell tracker and the
    /// objects ring must forget the old place before the camera is placed), but the hidden landblock roots, with the
    /// nodes still under them (a town's or dungeon's EnvCell meshes), are freed at most <see cref="OldNodesPerFrame"/>
    /// nodes a frame from the next frame on (<see cref="FreeOldRoots"/>) instead of all at the end of the jump frame:
    /// the jump frame's median fell from about 32 to 22-24 ms, measured with <c>--teleport-tour</c>
    /// (docs/navigation.md §1).
    /// </remarks>
    public void Reset()
    {
        _planner.Reset();
        foreach (var (coord, entry) in _entries.ToList())
        {
            entry.Cancellation.Cancel();
            DetachNow(coord, entry, deferRootFree: true);
            Stats.Cancelled++;
        }

        _entries.Clear();
    }

    /// <summary>
    /// Old nodes freed per frame after a <see cref="Reset"/>. A node costs about 25 µs all told (its exit from the tree
    /// in the drain, the rendering server's frees later in the frame; leaving the Training Academy, whose 2,160 cell
    /// nodes were freed at once, the end of the jump frame took 49-67 ms), so this bounds the cost at about 2-3 ms a frame;
    /// counted in nodes, not time, because the rendering server's share falls outside the main-thread budget.
    /// </summary>
    private const int OldNodesPerFrame = 96;

    /// <summary>Old landblock roots (hidden, detached from every layer) waiting to be freed after a <see cref="Reset"/>.</summary>
    private readonly Queue<Node3D> _oldRoots = new();

    /// <summary>Deferred to the end of a frame: the next frame's drain frees the next batch, first of all (Urgent).</summary>
    private void StartFreeingOldRoots() => _mainThread.Enqueue(FreeOldRoots, MainThreadQueue.Urgent);

    /// <summary>
    /// Frees up to <see cref="OldNodesPerFrame"/> nodes of the old roots (children first, then the root), and leaves the
    /// rest to the next frame.
    /// </summary>
    private void FreeOldRoots()
    {
        for (var freed = 0; freed < OldNodesPerFrame && _oldRoots.TryPeek(out var root);)
        {
            if (!GodotObject.IsInstanceValid(root))
            {
                _oldRoots.Dequeue();
                continue;
            }

            var children = root.GetChildCount();
            if (children == 0)
            {
                _oldRoots.Dequeue();
                root.Free();
            }
            else
            {
                root.GetChild(children - 1).Free();
            }

            freed++;
        }

        if (_oldRoots.Count > 0)
        {
            Callable.From(StartFreeingOldRoots).CallDeferred();
        }
    }

    private void Start(LandblockCoord coord)
    {
        var entry = new Entry(_layers.Count);
        _entries[coord] = entry;
        Stats.Started++;
        _ = PrepareAsync(coord, entry);
    }

    private void Cancel(LandblockCoord coord)
    {
        if (!_entries.Remove(coord, out var entry))
        {
            return;
        }

        entry.Cancellation.Cancel();
        Stats.Cancelled++;
        if (entry.Root is not null)
        {
            Stats.AbandonedMidAttach++;
        }

        // A partially attached landblock is cleaned up now (its attach sequence stopped, its layers detached), before a
        // new load of the same coordinate can attach; its queued attach item then sees the cancellation and retires.
        DetachNow(coord, entry);
    }

    private void Unload(LandblockCoord coord)
    {
        if (!_entries.Remove(coord, out var entry))
        {
            return;
        }

        entry.Cancellation.Cancel();
        Stats.Unloaded++;

        // Hidden now: it is no longer in Roots, so a rebase before the detach runs would not move it.
        if (entry.Root is { } root && GodotObject.IsInstanceValid(root))
        {
            root.Visible = false;
        }

        // Urgent: runs before any attach, so a landblock that streams straight back in detaches before it re-attaches.
        _mainThread.Enqueue(() => DetachNow(coord, entry), MainThreadQueue.Urgent);
    }

    private async Task PrepareAsync(LandblockCoord coord, Entry entry)
    {
        var token = entry.Cancellation.Token;
        var start = Stopwatch.GetTimestamp();
        var layerCount = entry.Prepared.Length;
        var tasks = new Task[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            var index = i;
            var layer = _layers[i];
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    entry.Prepared[index] = await _workers.Run(ct => layer.PrepareAsync(coord, ct), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Stale: nothing to do.
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref entry.Failures);
                    Log.Exception("stream", $"layer '{layer.Name}' failed to prepare {coord}", ex);
                }
            });
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var prepareMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (token.IsCancellationRequested)
        {
            DisposePrepared(entry);
            return;
        }

        var focus = _focus;
        var priority = StreamingPlanner.EdgeDistanceSquared(focus, coord);
        _mainThread.Enqueue(
            () =>
            {
                Stats.TotalPrepareMs += prepareMs;
                Stats.MaxPrepareMs = Math.Max(Stats.MaxPrepareMs, prepareMs);
                Stats.LayerFailures += entry.Failures;
                PendingAttaches++;
                _mainThread.EnqueueSteps(() => AttachStep(coord, entry), priority);
            },
            priority);
    }

    /// <summary>
    /// One main-thread step: create the root, then attach the layers in order (one step for each layer's
    /// <see cref="ILandblockLayer.Attach"/> call plus one per step of the sequence it returns), then report to the planner.
    /// </summary>
    private bool AttachStep(LandblockCoord coord, Entry entry)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            if (entry.Cancellation.IsCancellationRequested)
            {
                // Cancel() already detached whatever had attached; this only retires the queued item.
                PendingAttaches--;
                DetachNow(coord, entry);
                return true;
            }

            if (entry.Root is null)
            {
                entry.Root = _createRoot(coord);
                return false;
            }

            if (entry.Steps is not null)
            {
                RunLayerStep(coord, entry);
                return false;
            }

            if (entry.NextLayer < entry.Prepared.Length)
            {
                var index = entry.NextLayer++;
                if (entry.Prepared[index] is { } prepared)
                {
                    // Attached from now on: a cancellation after any step detaches the layer.
                    entry.Attached[index] = true;
                    entry.CurrentLayer = index;
                    try
                    {
                        if (_layers[index].Attach(coord, prepared, entry.Root) is { } steps)
                        {
                            entry.Steps = steps.GetEnumerator();
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Stats.LayerFailures++;
                        Log.Exception("stream", $"layer '{_layers[index].Name}' failed to attach {coord}", ex);
                    }

                    FinishLayer(entry);
                }

                return false;
            }

            PendingAttaches--;
            if (!_planner.MarkLoaded(coord))
            {
                // Should not happen (cancellation is checked above); clean up rather than leak.
                Log.Warn("stream", "attached landblock no longer wanted", ("landblock", coord));
                DetachNow(coord, entry);
                return true;
            }

            Stats.RecordAttach();
            Stats.TotalAttachMs += entry.AttachMs;
            Stats.MaxAttachMs = Math.Max(Stats.MaxAttachMs, entry.AttachMs);
            Log.Debug("stream", "attached", ("landblock", coord), ("attach_ms", entry.AttachMs), ("steps", entry.StepCount));
            return true;
        }
        finally
        {
            var stepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            entry.AttachMs += stepMs;
            entry.StepCount++;
            Stats.MaxAttachStepMs = Math.Max(Stats.MaxAttachStepMs, stepMs);
            if (stepMs > SlowStepMilliseconds)
            {
                var layer = entry.CurrentLayer < _layers.Count ? _layers[entry.CurrentLayer].Name : "none";
                Log.Debug("stream", "slow attach step", ("landblock", coord), ("layer", layer), ("step", entry.StepCount), ("ms", stepMs));
            }
        }
    }

    /// <summary>Runs one step of the current layer's attach sequence.</summary>
    private void RunLayerStep(LandblockCoord coord, Entry entry)
    {
        bool more;
        try
        {
            more = entry.Steps!.MoveNext();
        }
        catch (Exception ex)
        {
            Stats.LayerFailures++;
            Log.Exception("stream", $"layer '{_layers[entry.CurrentLayer].Name}' failed while attaching {coord}", ex);
            more = false;
        }

        if (!more)
        {
            FinishLayer(entry);
        }
    }

    /// <summary>The current layer's attach is over (done or failed): drop its sequence and dispose its prepared object.</summary>
    private static void FinishLayer(Entry entry)
    {
        StopSteps(entry);
        DisposePrepared(entry, entry.CurrentLayer);
    }

    private static void StopSteps(Entry entry)
    {
        if (entry.Steps is IDisposable disposable)
        {
            disposable.Dispose();
        }

        entry.Steps = null;
    }

    private void DetachNow(LandblockCoord coord, Entry entry, bool deferRootFree = false)
    {
        // Stop a half-run attach sequence first (its finally blocks run), and hide the root: shared meshes that go idle
        // in Detach are unbound from their materials, and the nodes under the root live until the end of the frame.
        StopSteps(entry);
        if (entry.Root is { } root && GodotObject.IsInstanceValid(root))
        {
            root.Visible = false;
        }

        for (var i = entry.Attached.Length - 1; i >= 0; i--)
        {
            if (!entry.Attached[i])
            {
                continue;
            }

            entry.Attached[i] = false;
            try
            {
                _layers[i].Detach(coord);
            }
            catch (Exception ex)
            {
                Log.Exception("stream", $"layer '{_layers[i].Name}' failed to detach {coord}", ex);
            }
        }

        if (entry.Root is { } attachedRoot && GodotObject.IsInstanceValid(attachedRoot))
        {
            if (deferRootFree)
            {
                // Reset (teleport): hidden above, freed by FreeOldRoots in later frames (out of Roots, so a rebase
                // does not move it; a new root for the same landblock gets a unique node name from Godot).
                _oldRoots.Enqueue(attachedRoot);
                if (_oldRoots.Count == 1)
                {
                    Callable.From(StartFreeingOldRoots).CallDeferred();
                }
            }
            else
            {
                attachedRoot.QueueFree();
            }
        }

        entry.Root = null;
        DisposePrepared(entry);
    }

    private static void DisposePrepared(Entry entry)
    {
        for (var i = 0; i < entry.Prepared.Length; i++)
        {
            DisposePrepared(entry, i);
        }
    }

    /// <summary>Takes a prepared object out of the entry (atomically: a cancelled load can reach here from a worker and the main thread) and disposes it.</summary>
    private static void DisposePrepared(Entry entry, int index)
    {
        if (Interlocked.Exchange(ref entry.Prepared[index], null) is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class Entry(int layerCount)
    {
        public CancellationTokenSource Cancellation { get; } = new();

        public object?[] Prepared { get; } = new object?[layerCount];

        public bool[] Attached { get; } = new bool[layerCount];

        public int NextLayer { get; set; }

        /// <summary>The layer whose attach is running (or last ran).</summary>
        public int CurrentLayer { get; set; }

        /// <summary>The current layer's remaining attach steps, or null.</summary>
        public System.Collections.IEnumerator? Steps { get; set; }

        public int StepCount { get; set; }

        public Node3D? Root { get; set; }

        public int Failures;

        public double AttachMs { get; set; }
    }
}
