using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Appearance;
using Lifestone.Game.Core;
using Lifestone.Game.Render;
using Lifestone.Game.SpellViewer;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// The meshes of the server's objects, as their ObjDescs dress them, for one lighting rule: a part's GfxObj with its texture swaps and its modified palette
/// (<see cref="PartAppearance.VariantKey"/>) is one shared mesh (<see cref="RenderResources.AcquireMesh"/> under the key <see cref="ActorMeshLibrary.Key"/>
/// plus the variant, so the player's meshes and these are the same resource when the dress is the same).
/// </summary>
/// <remarks>
/// <para><b>Worker prepares, main thread attaches.</b> <see cref="TryGet"/> never builds anything on the frame that asks: a mesh that is not resident is queued
/// to the worker pool, which decodes the GfxObj, resolves its surfaces and decodes the textures with the dress's palette
/// (<see cref="MeshFactory.Prepare"/>, <see cref="RenderResources.PrepareSurface"/>, both worker safe), and then the main-thread queue builds the Godot mesh within
/// its frame budget (<see cref="RenderResources.AcquireMesh"/>). The caller keeps an object hidden until every one of its parts answers (an object is shown only
/// when fully attached).</para>
/// <para><b>Lifetime.</b> A mesh stays while something asks for it; one not asked for in <see cref="IdleFrames"/> frames is released by <see cref="Sweep"/>
/// (the caller has stopped drawing it by then, see <see cref="ServerObjectsLayer"/>). Main thread only, except the worker step.</para>
/// </remarks>
public sealed class ServerObjectMeshes : IDisposable
{
    /// <summary>Frames a mesh may go unasked before <see cref="Sweep"/> releases it (ten seconds at 60 frames per second).</summary>
    public const long IdleFrames = 600;

    private readonly GameServices _services;
    private readonly SurfaceLighting _lighting;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Dictionary<ulong, ArrayMesh> _fades = new();
    private long _frame;
    private bool _disposed;

    /// <param name="services">The shared services.</param>
    /// <param name="lighting">The lighting rule the meshes' materials are drawn with (outdoors <see cref="SurfaceLighting.DynamicObject"/>, in cells <see cref="SurfaceLighting.DynamicCellObject"/>).</param>
    public ServerObjectMeshes(GameServices services, SurfaceLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _lighting = lighting;
    }

    /// <summary>The lighting rule of these meshes.</summary>
    public SurfaceLighting Lighting => _lighting;

    /// <summary>Meshes built (a part with no geometry counts as ready with none).</summary>
    public int Ready { get; private set; }

    /// <summary>Meshes queued to the workers or waiting for the main thread.</summary>
    public int Pending { get; private set; }

    /// <summary>Meshes prepared and attached over the run.</summary>
    public long Built { get; private set; }

    /// <summary>The slowest main-thread attach of a mesh so far, in milliseconds.</summary>
    public double MaxAttachMilliseconds { get; private set; }

    /// <summary>Marks the start of a frame (the idle clock).</summary>
    public void BeginFrame() => _frame++;

    /// <summary>
    /// True when the mesh of <paramref name="gfxObjId"/> as <paramref name="appearance"/> dresses it is ready; <paramref name="mesh"/> is then the mesh, or
    /// null for a part that has no geometry to draw. False (and the mesh is prepared in the background) when it is not.
    /// </summary>
    public bool TryGet(uint gfxObjId, PartAppearance appearance, out ArrayMesh? mesh)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var key = ActorMeshLibrary.Key(gfxObjId, _lighting) + appearance.VariantKey;
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.LastUsed = _frame;
            mesh = entry.Mesh;
            return !entry.Pending;
        }

        entry = new Entry { LastUsed = _frame, Pending = true };
        _entries.Add(key, entry);
        Pending++;
        var lighting = _lighting;
        _ = _services.Workers.RunSync(_ => PrepareAndQueue(key, gfxObjId, appearance, lighting));
        mesh = null;
        return false;
    }

    /// <summary>
    /// The key <see cref="Warm(uint, string)"/> files the plain mesh of <paramref name="gfxObjId"/> under in this set (<see cref="ActorMeshLibrary.Key"/> with the set's lighting rule). A caller that polls many
    /// meshes (the projectile warm-up) makes the keys once and keeps them in its plan, so a poll every few frames builds no string. Any thread: it reads only the set's lighting rule.
    /// </summary>
    public string WarmKey(uint gfxObjId) => ActorMeshLibrary.Key(gfxObjId, _lighting);

    /// <summary>
    /// Prepares the plain mesh of <paramref name="gfxObjId"/> (no texture swap, no palette: the key a Setup's own parts have, <see cref="PartAppearance.VariantKey"/> "") on a worker and keeps it
    /// resident (M3 3.1 lane fc, the projectile warm-up): <see cref="Sweep"/> never releases a warmed mesh, so a bolt created a minute later still finds its part ready on the frame its create is applied.
    /// Main thread. Returns true when the mesh is ready now (already attached), false when it is queued.
    /// </summary>
    public bool Warm(uint gfxObjId) => Warm(gfxObjId, WarmKey(gfxObjId));

    /// <summary>As <see cref="Warm(uint)"/>, with the key <see cref="WarmKey"/> gave for <paramref name="gfxObjId"/> made beforehand (a poll that repeats builds no string; package 2 review, finding 12).</summary>
    public bool Warm(uint gfxObjId, string key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Pinned = true;
            return !entry.Pending;
        }

        entry = new Entry { LastUsed = _frame, Pending = true, Pinned = true };
        _entries.Add(key, entry);
        Pending++;
        var lighting = _lighting;
        _ = _services.Workers.RunSync(_ => PrepareAndQueue(key, gfxObjId, null, lighting));
        return false;
    }

    /// <summary>
    /// The blended twin of a mesh <see cref="TryGet"/> gave out: the same geometry with every opaque or alpha-tested surface blended and writing no depth (the materialise
    /// fade, D45 and D63), built beside the mesh so the first frame of a fade compiles and uploads nothing. Null for a mesh this set does not know.
    /// </summary>
    public ArrayMesh? FadeOf(ArrayMesh mesh) =>
        _fades.TryGetValue(mesh.GetInstanceId(), out var fade) && GodotObject.IsInstanceValid(fade) ? fade : null;

    /// <summary>Releases the meshes nothing has asked for in <see cref="IdleFrames"/> frames.</summary>
    public void Sweep()
    {
        List<string>? drop = null;
        foreach (var (key, entry) in _entries)
        {
            if (!entry.Pending && !entry.Pinned && _frame - entry.LastUsed > IdleFrames)
            {
                (drop ??= []).Add(key);
            }
        }

        if (drop is null)
        {
            return;
        }

        foreach (var key in drop)
        {
            if (_entries.Remove(key, out var entry) && entry.Mesh is not null)
            {
                ReleaseMesh(key, entry.Mesh);
            }

            Ready--;
        }
    }

    /// <summary>Releases every mesh. The worker steps still in flight free their surfaces when they land.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var (key, entry) in _entries)
        {
            if (!entry.Pending && entry.Mesh is not null)
            {
                ReleaseMesh(key, entry.Mesh);
            }
        }

        _entries.Clear();
        _fades.Clear();
        Ready = 0;
        Pending = 0;
    }

    /// <summary>Worker: decodes the part and queues the attach.</summary>
    private int PrepareAndQueue(string key, uint gfxObjId, PartAppearance? appearance, SurfaceLighting lighting)
    {
        PreparedMesh? prepared = null;
        PreparedSurface[]? surfaces = null;
        try
        {
            if (_services.GfxObjMeshes.Get(gfxObjId) is { } gfxObj && gfxObj.Mesh.SubMeshes.Count > 0)
            {
                prepared = MeshFactory.Prepare(gfxObj.Mesh);
                surfaces = prepared.SubMeshes.Select(s => _services.Render.PrepareSurface(SurfaceRequest.For(s.Source) with
                {
                    Lighting = lighting,
                    Palette = appearance?.Palette,
                    SurfaceTextureOverride = appearance?.TextureOverrideFor(s.Source.SurfaceId),
                })).ToArray();
            }
            else
            {
                Log.Warn("serverobjects", "part GfxObj has no drawing geometry; the part draws nothing", ("gfxobj", $"0x{gfxObjId:X8}"));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Exception("serverobjects", "preparing a part's mesh failed; the part draws nothing", ex);
            prepared = null;
            DisposeAll(surfaces);
            surfaces = null;
        }

        var preparedMesh = prepared;
        var preparedSurfaces = surfaces;
        _services.MainThread.Enqueue(() => Attach(key, preparedMesh, preparedSurfaces));
        return 0;
    }

    /// <summary>Main thread (the frame budget's queue): builds the mesh and answers the waiting callers.</summary>
    private void Attach(string key, PreparedMesh? prepared, PreparedSurface[]? surfaces)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (_disposed || !_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (prepared is not null && surfaces is not null)
            {
                entry.Mesh = _services.Render.AcquireMesh(key, prepared, surfaces);

                // The blended twin (after the mesh: its materials upload the textures the twin shares). Built eagerly for every mesh, which doubles this main-thread step and the
                // mesh's GPU memory (M2 dec review N3). TODO(serverobjects-lazy-twins): build a twin only for the meshes of hidden objects (ServerObjectsLayer.Prefetch) or at the
                // first fade that needs one. Not done: the decoded PreparedMesh is freed after this step, so a lazy twin must either keep it for every mesh (CPU memory instead of GPU)
                // or be prepared again on a worker when a fade first needs it, during which that fade would draw solid (the TODO(g5-translucency-twin) failure the checks now judge);
                // neither is a small change, and the warm-up of the twin pipelines (ServerObjectsWarmUp) must keep running for either.
                _fades[entry.Mesh.GetInstanceId()] = _services.Render.AcquireMesh(key + RenderResources.FadeKeySuffix, prepared, surfaces.Select(RenderResources.FadeTwin).ToArray());
            }

            entry.Pending = false;
            Pending--;
            Ready++;
            Built++;
        }
        finally
        {
            DisposeAll(surfaces);
            MaxAttachMilliseconds = Math.Max(MaxAttachMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private void ReleaseMesh(string key, ArrayMesh mesh)
    {
        _services.Render.ReleaseMesh(key);
        if (_fades.Remove(mesh.GetInstanceId()))
        {
            _services.Render.ReleaseMesh(key + RenderResources.FadeKeySuffix);
        }
    }

    private static void DisposeAll(PreparedSurface[]? surfaces)
    {
        if (surfaces is null)
        {
            return;
        }

        foreach (var surface in surfaces)
        {
            surface.Dispose();
        }
    }

    private sealed class Entry
    {
        public ArrayMesh? Mesh;
        public bool Pending;
        public bool Pinned;
        public long LastUsed;
    }
}
