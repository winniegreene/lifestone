using System.Diagnostics;
using Godot;
using Lifestone.Client.Objects;
using Lifestone.Client.Rendering;
using Lifestone.Client.Streaming;
using Lifestone.Client.World;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Objects;

/// <summary>Counters for the overlay and the logs.</summary>
public sealed class ObjectsStats
{
    public int Registered { get; internal set; }

    public int Preparing { get; internal set; }

    public int Prepared { get; internal set; }

    /// <summary>Landblocks waiting for a worker to decode textures evicted since their prepare, before they attach.</summary>
    public int Decoding { get; internal set; }

    public int Attaching { get; internal set; }

    public int Active { get; internal set; }

    public int Statics { get; internal set; }

    public int Buildings { get; internal set; }

    public int Scenery { get; internal set; }

    public int Parts { get; internal set; }

    public int DrawnParts { get; internal set; }

    public int FacingParts { get; internal set; }

    public int Nodes { get; internal set; }

    public int VisibleNodes { get; internal set; }

    public int VisibleInstances { get; internal set; }

    public int Meshes { get; internal set; }

    public long Activations { get; internal set; }

    public long Deactivations { get; internal set; }

    public long Rejected { get; internal set; }

    /// <summary>Worker prepares started by the system (the streamer's own prepares are not counted).</summary>
    public long Prepares { get; internal set; }

    /// <summary>Attaches that reused a landblock's prepared data (it left the 3×3 but stayed in the prefetch ring).</summary>
    public long Reattaches { get; internal set; }

    /// <summary>Textures decoded again on a worker because they were evicted between a prepare and its attach.</summary>
    public long Redecodes { get; internal set; }

    public double LastUpdateMs { get; internal set; }

    public double MaxUpdateMs { get; internal set; }

    public double MaxAttachStepMs { get; internal set; }

    public double MaxPrepareMs { get; internal set; }

    public int LastRewrites { get; internal set; }

    public LandblockCoord? Center { get; internal set; }

    /// <summary>Clears the maxima (a measured run starts).</summary>
    public void ResetPeaks()
    {
        MaxUpdateMs = 0;
        MaxAttachStepMs = 0;
        MaxPrepareMs = 0;
    }
}

/// <summary>
/// Landscape objects (LandBlockInfo statics and buildings, procedural scenery) for the landblocks around the viewer, with
/// retail's per-frame LOD. Driven by <see cref="ObjectsLayer"/> (which reports landblock roots as they stream in and out).
/// </summary>
/// <remarks>
/// <para><b>Which landblocks</b> (<see cref="ObjectRings"/>): retail creates objects only for the landblocks within
/// <c>radius</c> rings of the viewer's landblock (1 = the retail 3×3) and destroys them when they leave. Landblocks one ring
/// further are prepared ahead on the workers (object sets, meshes, texture decodes), so crossing a landblock boundary only
/// costs the budgeted main-thread attach.</para>
/// <para><b>States</b> per landblock: idle → preparing (worker) → prepared → [decoding (worker)] → attaching (main-thread
/// steps through <see cref="GameServices.MainThread"/>, at the streamer's distance priority) → active (per-frame LOD).
/// Leaving the 3×3 destroys the drawn objects at once (retail <c>CLandBlock::notify_change_size</c>); a landblock still
/// in the prefetch ring keeps its CPU-side prepared data (object set, transforms, LOD state, instance buffers) and goes
/// back to prepared, so coming back costs only the attach.</para>
/// <para><b>Textures at attach.</b> When the attach is decided, every texture the landblock needs is pinned if resident
/// (<see cref="LandblockAttachTextures"/>); one evicted since the prepare (the texture budget is small, or the landblock
/// waited long in the prefetch ring) is decoded again on a worker first (state decoding), so no attach step ever
/// decodes on the main thread.</para>
/// <para><b>Viewer</b>: the current camera; the ring centre is the streaming focus's landblock (retail centres its grid
/// on the player's outdoor cell, <c>CellManager::UpdateLoadPoint</c>; in M1 the camera is the focus). An indoor focus keeps
/// the last outdoor centre.</para>
/// </remarks>
public partial class ObjectsSystem : Node
{
    /// <summary>Main-thread time one attach step may use before yielding.</summary>
    public const double AttachSliceMs = 0.75;

    /// <summary>LOD updates slower than this are logged (debug).</summary>
    private const double SlowUpdateMs = 2.0;

    /// <summary>Attach steps slower than this are logged (debug) with the garbage collections that ran inside them.</summary>
    private const double SlowStepMs = 4.0;

    private readonly Dictionary<LandblockCoord, Entry> _entries = new();
    private readonly List<Entry> _active = new();
    private GameServices _services = null!;
    private WorldRoot _world = null!;
    private ObjectModelLibrary _library = null!;
    private LivingStaticsSystem? _living;
    private readonly PreparedGeometryCache _geometry = new();
    private ObjectLodSettings _lod = ObjectLodSettings.Retail;
    private WorldObjectKinds _kinds = WorldObjectKinds.All;
    private int _radius = ObjectRings.RetailRadius;
    private LandblockCoord? _center;
    private int _packedCenter = -1; // _center for worker threads: (x << 8) | y, or -1
    private readonly HashSet<string> _loggedRejections = new();

    public ObjectsStats Stats { get; } = new();

    /// <summary>Rings of landblocks around the viewer's that have objects (1 = retail).</summary>
    public int Radius => _radius;

    public WorldObjectKinds Kinds => _kinds;

    /// <summary>True when nothing is being prepared, decoded or attached.</summary>
    public bool IsIdle => Stats.Preparing == 0 && Stats.Decoding == 0 && Stats.Attaching == 0;

    public void Initialize(GameServices services, WorldRoot world, WorldObjectKinds kinds, int radius, ObjectLodSettings? lod = null)
    {
        _services = services;
        _world = world;
        _kinds = kinds;
        _radius = Math.Max(0, radius);
        _lod = lod ?? ObjectLodSettings.Retail;
        _library = ObjectsShared.Library(services); // shared with the EnvCell statics and the spell viewer (one posing path)
        _living = LivingStaticsSystem.Install(world, services);
        SetCenter(world.Origin.Origin); // the start landblock, before the first frame streams anything
        Name = "ObjectsSystem";
        // After the camera moves (0) and before the world rebases and plans streaming (10): the ring centre is current when
        // the streamer starts this frame's prepares, and the viewer-to-landblock offsets the LOD uses are the same before
        // and after a rebase (the camera and the landblock roots shift together).
        ProcessPriority = 5;
        var warmTimer = Stopwatch.StartNew();
        var multiMeshFreedAtOnce = LandblockObjectBatches.WarmUp();

        // Run the registration and attach path once on an empty landblock, so its first-call JIT (the attach iterator,
        // the LOD evaluation, the generic sort) happens here rather than inside a budgeted attach step.
        var probe = new Node3D();
        var empty = LandblockObjectSet.Build(new LandblockCoord(0, 0), null, null, _library);
        var warmSteps = Register(empty.Coord, probe, new ObjectsLayerPrepared(PreparedLandblockObjects.Empty(empty)))
            ?? BeginAttach(_entries[empty.Coord], new LandblockAttachTextures(_services.Render));
        if (warmSteps is not null)
        {
            foreach (var _ in warmSteps)
            {
            }
        }

        Unregister(empty.Coord);
        probe.QueueFree();
        Stats.Activations = 0;
        Stats.Deactivations = 0;
        Stats.Reattaches = 0;
        Stats.Prepares = 0;
        var warmMs = warmTimer.Elapsed.TotalMilliseconds;
        Log.Info(
            "objects",
            "objects system ready",
            ("warm_ms", warmMs),
            ("multimesh_release_frees_mesh_ref", multiMeshFreedAtOnce),
            ("gc", (System.Runtime.GCSettings.IsServerGC ? "server " : "workstation ") + System.Runtime.GCSettings.LatencyMode),
            ("kinds", kinds),
            ("radius", _radius),
            ("retail_radius", ObjectRings.RetailRadius),
            ("lod_bias", _lod.Bias),
            ("degrade_distance", _lod.DegradeDistance));
    }

    /// <summary>
    /// Worker thread (<see cref="ObjectsLayer.PrepareAsync"/>): prepares a streamed landblock's objects when it is within
    /// the prefetch ring of the current centre, so the first 3×3 attaches through the streamer (and "streaming idle"
    /// includes it). Returns null when the landblock is too far (the system prepares it later if it comes closer).
    /// </summary>
    public ObjectsLayerPrepared? PrepareForStreaming(LandblockCoord coord, CancellationToken cancellation)
    {
        var center = Volatile.Read(ref _packedCenter);
        if (center < 0 || ObjectRings.Ring(coord, new LandblockCoord((byte)(center >> 8), (byte)center)) > _radius + 1)
        {
            return null;
        }

        return new ObjectsLayerPrepared(PreparedLandblockObjects.Prepare(_services, _library, _geometry, coord, _kinds, cancellation));
    }

    /// <summary>
    /// A landblock's root is attached (main thread, from <see cref="ObjectsLayer.Attach"/>), with the objects the layer
    /// prepared, if any. Returns the attach steps when its objects are wanted now (the streamer runs them within its
    /// budget), else null.
    /// </summary>
    public System.Collections.IEnumerable? Register(LandblockCoord coord, Node3D root, ObjectsLayerPrepared? prepared)
    {
        if (_entries.Remove(coord, out var old))
        {
            Drop(old);
        }

        var entry = new Entry(coord, root);
        _entries[coord] = entry;
        Stats.Registered = _entries.Count;
        if (prepared?.Take() is not { } objects)
        {
            if (prepared is not null)
            {
                entry.State = EntryState.Empty; // prepared, and it has no objects
            }

            return null;
        }

        Stats.MaxPrepareMs = Math.Max(Stats.MaxPrepareMs, objects.PrepareMs);
        ReportRejections(objects.Set);
        entry.Prepared = objects;
        entry.State = EntryState.Prepared;
        if (_center is not { } center || ObjectRings.Ring(coord, center) > _radius)
        {
            return null;
        }

        // Attach through the streamer, unless a texture was evicted since the prepare: then a worker decodes it first and
        // the system attaches the landblock itself (UpdateStates).
        var textures = new LandblockAttachTextures(_services.Render);
        var missing = textures.PinResident(objects);
        if (missing.Count > 0)
        {
            StartDecode(entry, textures, missing);
            return null;
        }

        return BeginAttach(entry, textures);
    }

    /// <summary>A landblock streams out (main thread, from <see cref="ObjectsLayer.Detach"/>).</summary>
    public void Unregister(LandblockCoord coord)
    {
        if (_entries.Remove(coord, out var entry))
        {
            Drop(entry);
        }

        Stats.Registered = _entries.Count;
    }

    /// <summary>
    /// Where the landblock's objects stand on their way into the scene, for portal space's destination readiness
    /// (<see cref="Lifestone.Client.Streaming.IWorldAttachments.Objects"/>; a cheap read of state the system already keeps,
    /// main thread, never starts a load).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="AttachState.Attached"/>: drawn (<c>Active</c>), or the landblock has no objects (<c>Empty</c>).</item>
    /// <item><see cref="AttachState.Failed"/>: preparing threw (logged when it did).</item>
    /// <item><see cref="AttachState.Disabled"/>: this system builds no objects for the landblock at all: it lies beyond
    /// <see cref="Radius"/> rings of the ring centre (<c>--object-radius=0</c> leaves a destination's neighbours here),
    /// so a wait never hangs on it.</item>
    /// <item><see cref="AttachState.Pending"/>: not streamed in yet, or on its way (idle, preparing, prepared, decoding,
    /// attaching), or the ring centre has not moved to the destination yet (it follows the streaming focus each frame).</item>
    /// </list>
    /// </remarks>
    public AttachState StateOf(LandblockCoord coord)
    {
        if (!_entries.TryGetValue(coord, out var entry))
        {
            return AttachState.Pending;
        }

        switch (entry.State)
        {
            case EntryState.Active:
            case EntryState.Empty:
                return AttachState.Attached;
            case EntryState.Failed:
                return AttachState.Failed;
            default:
                return _center is { } center && ObjectRings.Ring(coord, center) > _radius ? AttachState.Disabled : AttachState.Pending;
        }
    }

    public override void _Ready() => _ = Viewer(); // first-call interop of the camera lookup, outside the first frame's work

    public override void _Process(double delta)
    {
        using var framePhase = Lifestone.Game.Diagnostics.FramePhases.World(); // M6 16 Eperf2: this call's share of a long frame
        if (_world is null || !_world.IsInitialized)
        {
            return;
        }

        UpdateCenter();
        UpdateStates();
        UpdateLod();
    }

    public override void _ExitTree()
    {
        Log.Info(
            "objects",
            "summary",
            ("centre", Stats.Center?.ToString() ?? "-"),
            ("active", Stats.Active),
            ("statics", Stats.Statics),
            ("buildings", Stats.Buildings),
            ("scenery", Stats.Scenery),
            ("parts", Stats.Parts),
            ("drawn_parts", Stats.DrawnParts),
            ("facing_parts", Stats.FacingParts),
            ("batches", Stats.Nodes),
            ("shown_batches", Stats.VisibleNodes),
            ("shown_instances", Stats.VisibleInstances),
            ("draw_calls", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)),
            ("objects_in_frame", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)),
            ("primitives", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)),
            ("fps", Engine.GetFramesPerSecond()),
            ("static_mb", OS.GetStaticMemoryUsage() / (1024 * 1024)),
            ("managed_mb", GC.GetTotalMemory(false) / (1024 * 1024)),
            ("video_mb", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / (1024 * 1024)),
            ("gc_count", $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"),
            ("gc_pause_total_ms", GC.GetTotalPauseDuration().TotalMilliseconds),
            ("activations", Stats.Activations),
            ("deactivations", Stats.Deactivations),
            ("system_prepares", Stats.Prepares),
            ("reattaches", Stats.Reattaches),
            ("redecoded_textures", Stats.Redecodes),
            ("max_update_ms", Stats.MaxUpdateMs),
            ("max_attach_step_ms", Stats.MaxAttachStepMs),
            ("max_prepare_ms", Stats.MaxPrepareMs),
            ("models", _library?.ModelCount ?? 0),
            ("geometry", $"{_geometry.Count} cached, {_geometry.Hits} hits, {_geometry.Misses} misses, {_geometry.Evictions} evicted"));
        foreach (var entry in _entries.Values)
        {
            Drop(entry);
        }

        _entries.Clear();
        _active.Clear();
    }

    private void UpdateCenter()
    {
        var focus = _world.FocusSource?.StreamingFocus ?? new StreamingFocus(_world.FromRender(Viewer()));

        // A dungeon cell (not seen_outside) has no landscape objects at all, not even the dungeon landblock's own
        // (ObjectRings.CenterFor: retail's LScape::release_all; audit objects-active-in-dungeon-landblock). The first
        // outdoor focus brings the grid back.
        if (ObjectRings.CenterFor(focus) is not { } center)
        {
            if (_center is not null)
            {
                Log.Debug("objects", "landscape objects released (dungeon cell)", ("from", _center));
                SetCenter(null);
            }

            return;
        }

        if (center != _center)
        {
            Log.Debug("objects", "centre landblock", ("from", _center?.ToString() ?? "none"), ("to", center));
            SetCenter(center);
        }
    }

    private void SetCenter(LandblockCoord? center)
    {
        _center = center;
        Stats.Center = center;
        Volatile.Write(ref _packedCenter, center is { } c ? (c.X << 8) | c.Y : -1);
    }

    private void UpdateStates()
    {
        if (_center is not { } center)
        {
            // No grid (a dungeon cell): every landscape object goes, as LScape::release_all destroys them.
            foreach (var entry in _entries.Values)
            {
                switch (entry.State)
                {
                    case EntryState.Preparing:
                        CancelPrepare(entry);
                        break;
                    case EntryState.Prepared:
                        DisposePrepared(entry);
                        break;
                    case EntryState.Decoding:
                        CancelDecode(entry);
                        DisposePrepared(entry);
                        break;
                    case EntryState.Attaching:
                    case EntryState.Active:
                        Deactivate(entry, keepPrepared: false);
                        break;
                }
            }

            Stats.Preparing = Stats.Prepared = Stats.Decoding = Stats.Attaching = 0;
            return;
        }

        var focus = _world.FocusSource?.StreamingFocus.Position ?? _world.FromRender(Viewer());
        int preparing = 0, prepared = 0, decoding = 0, attaching = 0;
        foreach (var entry in _entries.Values)
        {
            var ring = ObjectRings.Ring(entry.Coord, center);
            var wanted = ring <= _radius;
            var prefetch = ring <= _radius + 1;
            switch (entry.State)
            {
                case EntryState.Idle:
                    if (prefetch)
                    {
                        StartPrepare(entry);
                    }

                    break;
                case EntryState.Preparing:
                    if (!prefetch)
                    {
                        CancelPrepare(entry);
                    }
                    else if (entry.Task is { IsCompleted: true } task)
                    {
                        FinishPrepare(entry, task);
                    }

                    break;
                case EntryState.Prepared:
                    if (!prefetch)
                    {
                        DisposePrepared(entry);
                    }

                    break;
                case EntryState.Decoding:
                    if (!wanted)
                    {
                        CancelDecode(entry);
                        if (!prefetch)
                        {
                            DisposePrepared(entry);
                        }
                    }
                    else if (entry.DecodeTask is { IsCompleted: true } decode)
                    {
                        FinishDecode(entry, decode, focus);
                    }

                    break;
                case EntryState.Attaching:
                case EntryState.Active:
                    if (!wanted)
                    {
                        Deactivate(entry, keepPrepared: prefetch);
                    }

                    break;
            }

            if (entry.State == EntryState.Prepared && wanted)
            {
                Activate(entry, focus);
            }

            switch (entry.State)
            {
                case EntryState.Preparing:
                    preparing++;
                    break;
                case EntryState.Prepared:
                    prepared++;
                    break;
                case EntryState.Decoding:
                    decoding++;
                    break;
                case EntryState.Attaching:
                    attaching++;
                    break;
            }
        }

        Stats.Preparing = preparing;
        Stats.Prepared = prepared;
        Stats.Decoding = decoding;
        Stats.Attaching = attaching;
    }

    private void UpdateLod()
    {
        var start = Stopwatch.GetTimestamp();
        var gc0 = GC.CollectionCount(0);
        var viewer = Viewer();
        var rewrites = 0;
        int statics = 0, buildings = 0, scenery = 0, parts = 0, drawn = 0, facing = 0, nodes = 0, visibleNodes = 0, instances = 0, meshes = 0;
        foreach (var entry in _active)
        {
            var batches = entry.Batches!;
            if (batches.IsAttached)
            {
                rewrites += batches.Update(viewer);
            }

            var set = batches.Set;
            statics += set.Count(WorldObjectKind.Static);
            buildings += set.Count(WorldObjectKind.Building);
            scenery += set.Count(WorldObjectKind.Scenery);
            parts += set.Parts.Count;
            drawn += batches.Lod.DrawnParts;
            facing += batches.Lod.FacingParts;
            nodes += batches.NodeCount;
            visibleNodes += batches.VisibleNodeCount;
            instances += batches.VisibleInstanceCount;
            meshes += batches.AcquiredMeshCount;
        }

        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Stats.LastUpdateMs = ms;
        Stats.MaxUpdateMs = Math.Max(Stats.MaxUpdateMs, ms);
        Stats.LastRewrites = rewrites;
        var active = 0;
        foreach (var entry in _active)
        {
            active += entry.State == EntryState.Active ? 1 : 0;
        }

        Stats.Active = active;
        (Stats.Statics, Stats.Buildings, Stats.Scenery, Stats.Parts, Stats.DrawnParts, Stats.FacingParts) = (statics, buildings, scenery, parts, drawn, facing);
        (Stats.Nodes, Stats.VisibleNodes, Stats.VisibleInstances, Stats.Meshes) = (nodes, visibleNodes, instances, meshes);
        if (ms > SlowUpdateMs)
        {
            Log.Debug("objects", "slow LOD update", ("ms", ms), ("rewrites", rewrites), ("active", _active.Count), ("gc_during", GC.CollectionCount(0) - gc0));
        }
    }

    private Vector3 Viewer() => GetViewport()?.GetCamera3D() is { } camera ? camera.GlobalPosition : Vector3.Zero;

    private void StartPrepare(Entry entry)
    {
        var cts = new CancellationTokenSource();
        var coord = entry.Coord;
        entry.Cancellation = cts;
        entry.State = EntryState.Preparing;
        Stats.Prepares++;
        entry.Task = _services.Workers.RunSync(ct => PreparedLandblockObjects.Prepare(_services, _library, _geometry, coord, _kinds, ct), cts.Token);
    }

    private void FinishPrepare(Entry entry, Task<PreparedLandblockObjects?> task)
    {
        entry.Task = null;
        entry.Cancellation?.Dispose();
        entry.Cancellation = null;
        if (task.IsCompletedSuccessfully)
        {
            entry.Prepared = task.Result;
            entry.State = entry.Prepared is null ? EntryState.Empty : EntryState.Prepared;
            if (entry.Prepared is { } prepared)
            {
                Stats.MaxPrepareMs = Math.Max(Stats.MaxPrepareMs, prepared.PrepareMs);
                ReportRejections(prepared.Set);
            }

            return;
        }

        if (task.Exception is { } ex)
        {
            Log.Exception("objects", $"preparing the objects of {entry.Coord} failed", ex.GetBaseException());
            entry.State = EntryState.Failed;
            return;
        }

        entry.State = EntryState.Idle; // cancelled
    }

    private static void CancelPrepare(Entry entry)
    {
        entry.Cancellation?.Cancel();
        if (entry.Task is { } task)
        {
            // A result that still arrives is disposed (it may hold decoded images).
            _ = task.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    t.Result?.Dispose();
                }
            }, TaskScheduler.Default);
        }

        entry.Task = null;
        entry.Cancellation = null;
        entry.State = EntryState.Idle;
    }

    /// <summary>
    /// System path, a prepared landblock entered the 3×3: pins its resident textures, then either attaches it or, when a
    /// texture was evicted since the prepare, has a worker decode it first (<see cref="EntryState.Decoding"/>).
    /// </summary>
    private void Activate(Entry entry, GlobalPosition focus)
    {
        var textures = new LandblockAttachTextures(_services.Render);
        var missing = textures.PinResident(entry.Prepared!);
        if (missing.Count > 0)
        {
            StartDecode(entry, textures, missing);
            return;
        }

        StartAttach(entry, textures, focus);
    }

    private void StartDecode(Entry entry, LandblockAttachTextures textures, List<MissingTexture> missing)
    {
        var cts = new CancellationTokenSource();
        var render = _services.Render;
        entry.Textures = textures;
        entry.Cancellation = cts;
        entry.DecodeTask = _services.Workers.RunSync(ct => LandblockAttachTextures.Decode(render, missing, ct), cts.Token);
        entry.State = EntryState.Decoding;
        Stats.Redecodes += missing.Count;
        Log.Debug("objects", "textures evicted since the prepare; a worker decodes them before the attach",
            ("landblock", entry.Coord), ("textures", missing.Count), ("pinned", textures.PinnedCount));
    }

    private void FinishDecode(Entry entry, Task<(TextureKey, PreparedTexture)[]> task, GlobalPosition focus)
    {
        var textures = entry.Textures!;
        entry.Textures = null;
        entry.DecodeTask = null;
        entry.Cancellation?.Dispose();
        entry.Cancellation = null;
        if (task.IsCompletedSuccessfully)
        {
            textures.AddDecoded(task.Result);
        }
        else if (task.Exception is { } ex)
        {
            // Not expected (the same textures decoded at the prepare); the material path decodes or reports what is missing.
            Log.Exception("objects", $"decoding the evicted textures of {entry.Coord} failed", ex.GetBaseException());
        }

        entry.State = EntryState.Prepared;
        StartAttach(entry, textures, focus);
    }

    /// <summary>Stops a decode (its late result is freed) and drops the pins; the entry goes back to prepared.</summary>
    private static void CancelDecode(Entry entry)
    {
        entry.Cancellation?.Cancel();
        if (entry.DecodeTask is { } task)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    foreach (var (_, texture) in t.Result)
                    {
                        texture.Dispose();
                    }
                }
            }, TaskScheduler.Default);
        }

        entry.DecodeTask = null;
        entry.Cancellation = null;
        entry.Textures?.Release();
        entry.Textures = null;
        entry.State = EntryState.Prepared;
    }

    /// <summary>Attaches a prepared landblock through the main-thread queue at the streamer's distance priority.</summary>
    private void StartAttach(Entry entry, LandblockAttachTextures textures, GlobalPosition focus)
    {
        if (BeginAttach(entry, textures) is not { } steps)
        {
            return;
        }

        var enumerator = steps.GetEnumerator();
        _services.MainThread.EnqueueSteps(() => !enumerator.MoveNext(), StreamingPlanner.EdgeDistanceSquared(focus, entry.Coord));
    }

    /// <summary>
    /// Creates the landblock's batches (which take over <paramref name="textures"/>) and returns its attach steps (one
    /// budgeted slice each).
    /// </summary>
    private System.Collections.IEnumerable? BeginAttach(Entry entry, LandblockAttachTextures textures)
    {
        var prepared = entry.Prepared!;
        entry.Prepared = null;
        if (!GodotObject.IsInstanceValid(entry.Root))
        {
            textures.Release();
            prepared.Dispose();
            entry.State = EntryState.Idle;
            return null;
        }

        if (prepared.Lod is not null)
        {
            Stats.Reattaches++;
        }

        var batches = new LandblockObjectBatches(_services.Render, prepared, _lod, entry.Root, textures);
        entry.Batches = batches;
        entry.State = EntryState.Attaching;
        _active.Add(entry);
        Stats.Activations++;
        return AttachSteps(entry, batches, ++entry.Generation);
    }

    private System.Collections.IEnumerable AttachSteps(Entry entry, LandblockObjectBatches batches, int generation)
    {
        while (entry.Generation == generation && entry.State == EntryState.Attaching)
        {
            var start = Stopwatch.GetTimestamp();
            var gen0 = GC.CollectionCount(0);
            var gen2 = GC.CollectionCount(2);
            var done = batches.AttachStep(Viewer, AttachSliceMs);
            var stepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Stats.MaxAttachStepMs = Math.Max(Stats.MaxAttachStepMs, stepMs);
            if (stepMs > SlowStepMs)
            {
                var gcInfo = GC.GetGCMemoryInfo(GCKind.Any);
                Log.Debug("objects", "slow attach step", ("landblock", entry.Coord), ("ms", stepMs), ("gc", GC.CollectionCount(0) - gen0), ("gc2", GC.CollectionCount(2) - gen2),
                    ("last_gc_gen", gcInfo.Generation), ("last_gc_pause_ms", gcInfo.PauseDurations.Length > 0 ? gcInfo.PauseDurations[0].TotalMilliseconds : 0),
                    ("promoted_mb", gcInfo.PromotedBytes / (1024.0 * 1024.0)), ("heap_mb", gcInfo.HeapSizeBytes / (1024.0 * 1024.0)), ("concurrent", gcInfo.Concurrent));
            }

            if (done)
            {
                entry.State = EntryState.Active;
                Log.Debug("objects", "landblock objects attached", ("landblock", entry.Coord), ("objects", batches.Set.Objects.Count),
                    ("meshes", batches.AcquiredMeshCount), ("nodes", batches.NodeCount));
                yield break;
            }

            yield return null;
        }
    }

    /// <summary>
    /// The landblock left the 3×3: its drawn objects go at once (nodes, MultiMeshes, mesh and texture references). With
    /// <paramref name="keepPrepared"/> (it is still in the prefetch ring) the CPU-side prepared data is kept and the entry
    /// goes back to prepared, so coming back does not prepare it again; otherwise it is idle.
    /// </summary>
    private void Deactivate(Entry entry, bool keepPrepared)
    {
        entry.Generation++;
        var batches = entry.Batches;
        entry.Batches = null;
        _active.Remove(entry);
        Stats.Deactivations++;
        entry.State = EntryState.Idle;
        if (batches is null)
        {
            return;
        }

        batches.Release();
        if (keepPrepared)
        {
            entry.Prepared = batches.Prepared;
            entry.State = EntryState.Prepared;
        }
        else
        {
            batches.Prepared.Dispose();
        }
    }

    private static void DisposePrepared(Entry entry)
    {
        entry.Prepared?.Dispose();
        entry.Prepared = null;
        entry.State = EntryState.Idle;
    }

    private void Drop(Entry entry)
    {
        switch (entry.State)
        {
            case EntryState.Preparing:
                CancelPrepare(entry);
                break;
            case EntryState.Prepared:
                DisposePrepared(entry);
                break;
            case EntryState.Decoding:
                CancelDecode(entry);
                DisposePrepared(entry);
                break;
            case EntryState.Attaching:
            case EntryState.Active:
                Deactivate(entry, keepPrepared: false);
                break;
        }

        entry.State = EntryState.Dropped;
    }

    private void ReportRejections(LandblockObjectSet set)
    {
        foreach (var (reason, count) in set.Rejected)
        {
            Stats.Rejected += count;
            if (_loggedRejections.Add($"{set.Coord}:{reason}"))
            {
                Log.Debug("objects", "objects not created (retail rule)", ("landblock", set.Coord), ("reason", reason), ("count", count));
            }
        }
    }

    private enum EntryState
    {
        Idle,
        Preparing,
        Prepared,

        /// <summary>Wanted, with resident textures pinned; a worker decodes the ones evicted since the prepare.</summary>
        Decoding,
        Attaching,
        Active,

        /// <summary>The landblock has no objects (stays so until it streams out).</summary>
        Empty,

        /// <summary>Preparing threw (logged); not retried until the landblock streams out and back.</summary>
        Failed,
        Dropped,
    }

    private sealed class Entry(LandblockCoord coord, Node3D root)
    {
        public LandblockCoord Coord { get; } = coord;

        public Node3D Root { get; } = root;

        public EntryState State { get; set; }

        public Task<PreparedLandblockObjects?>? Task { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        /// <summary>The worker decode of textures evicted since the prepare (<see cref="EntryState.Decoding"/>).</summary>
        public Task<(TextureKey, PreparedTexture)[]>? DecodeTask { get; set; }

        /// <summary>The pins and decodes of an attach that waits for <see cref="DecodeTask"/>.</summary>
        public LandblockAttachTextures? Textures { get; set; }

        public PreparedLandblockObjects? Prepared { get; set; }

        public LandblockObjectBatches? Batches { get; set; }

        /// <summary>Bumped on every activation and deactivation, so a stale attach step retires.</summary>
        public int Generation { get; set; }
    }
}
