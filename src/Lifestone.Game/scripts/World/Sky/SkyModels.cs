using Godot;
using Lifestone.Client.Rendering;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Sky;

/// <summary>One sub-mesh surface of a sky model, resolved through the shared render path.</summary>
public sealed class SkyModelSurface
{
    /// <summary>The Surface record; null when it could not be resolved (drawn with the error material).</summary>
    public required SurfaceMaterial? Material { get; init; }

    public required TextureAddressMode Addressing { get; init; }

    /// <summary>The shared surface material's handle: holding it keeps the uploaded texture alive.</summary>
    public required MaterialHandle<ShaderMaterial> Handle { get; init; }

    /// <summary>The texture to sample, or null for a solid-colour surface.</summary>
    public Texture2D? Texture { get; init; }

    /// <summary>Retail's 1x1 solid texture for untextured surfaces (raw RGB, alpha (1 - t)). A <c>Vector4</c>, not a
    /// <c>Color</c>: Godot linearises a Color given to a vec4 shader parameter (pinned by <c>--sky-checks</c>).</summary>
    public Vector4 SolidColor { get; init; } = Vector4.One;

    /// <summary>True when the surface could not be drawn faithfully (logged).</summary>
    public bool IsError => Material is null || Handle.Key is null;
}

/// <summary>One part of a sky model: a GfxObj at its rest pose within the object.</summary>
public sealed class SkyModelPart
{
    public required uint GfxObjId { get; init; }

    /// <summary>Part-to-object transform (Godot axes).</summary>
    public required Transform3D Transform { get; init; }

    /// <summary>The geometry, without materials (each sky slot binds its own per-surface materials).</summary>
    public required ArrayMesh Mesh { get; init; }

    public required IReadOnlyList<SkyModelSurface> Surfaces { get; init; }
}

/// <summary>A sky object's model: a GfxObj (one part) or a Setup (its parts at rest).</summary>
public sealed class SkyModel
{
    public required uint Id { get; init; }

    public required IReadOnlyList<SkyModelPart> Parts { get; init; }
}

/// <summary>
/// Builds the meshes and textures of sky objects through the shared render path (<see cref="RenderResources"/>): mesh
/// conversion and texture decoding on the worker pool, uploads on the main thread one part per budgeted step.
/// </summary>
/// <remarks>
/// <para>Retail creates a sky object's physics object when its GfxObj appears (<c>GameSky::MakeObject</c>); the
/// renderer asks for every model of the current day group up front (<see cref="Lifestone.Client.Sky.SkyDirector.ModelIds"/>)
/// so a keyframe replacement never waits. A Region references about 25 distinct sky models; they are kept for the
/// session (their textures stay referenced).</para>
/// <para>Setups use <see cref="ModelAssembly.ComputeRestPose"/> (placement 0x65, else 0). The Region's sky Setups are
/// invisible PES carriers (a 5 cm fully translucent marker whose PhysicsScript retail never plays).</para>
/// </remarks>
public sealed class SkyModels
{
    /// <summary>Culling box of every sky mesh: retail draws sky objects whatever their bounds say (zfar x 4).</summary>
    public static readonly Aabb CullBounds = new(new Vector3(-20000f, -20000f, -20000f), new Vector3(40000f, 40000f, 40000f));

    private readonly GameServices _services;
    private readonly Dictionary<uint, SkyModel?> _ready = new();
    private readonly HashSet<uint> _loading = new();
    private readonly Dictionary<uint, Task<List<PreparedPart>>> _prefetched = new();

    public SkyModels(GameServices services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>Models being prepared or uploaded.</summary>
    public int Pending => _loading.Count;

    /// <summary>Models ready (or known to be empty).</summary>
    public int Ready => _ready.Count;

    /// <summary>A ready model, or null (also null for an id that has nothing to draw). Requests it when unknown.</summary>
    public SkyModel? TryGet(uint id)
    {
        if (_ready.TryGetValue(id, out var model))
        {
            return model;
        }

        Request(id);
        return null;
    }

    /// <summary>True once <paramref name="id"/> is ready (or known to be empty).</summary>
    public bool IsReady(uint id) => _ready.ContainsKey(id);

    /// <summary>Starts preparing a model unless it is ready or loading. Main thread.</summary>
    public void Request(uint id)
    {
        if (id == 0 || _ready.ContainsKey(id) || !_loading.Add(id))
        {
            return;
        }

        _ = LoadAsync(id);
    }

    /// <summary>
    /// Main thread, at start-up: prepares every given model on the workers (in parallel) and builds them now, so no sky
    /// object ever waits for its model or uploads textures mid-flight. Returns the time taken in milliseconds.
    /// </summary>
    public double LoadAllNow(IEnumerable<uint> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var wanted = ids.Where(id => id != 0 && !_ready.ContainsKey(id) && !_loading.Contains(id)).Distinct().ToArray();
        var tasks = wanted.Select(id => _prefetched.Remove(id, out var early) ? early : _services.Workers.RunSync(_ => Prepare(id))).ToArray();
        for (var i = 0; i < wanted.Length; i++)
        {
            List<PreparedPart>? parts = null;
            try
            {
                parts = tasks[i].GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error("sky", "sky model preparation failed", ("model", $"0x{wanted[i]:X8}"), ("error", ex.Message));
            }

            var built = parts?.Select(Build).ToList() ?? [];
            _ready[wanted[i]] = built.Count == 0 ? null : new SkyModel { Id = wanted[i], Parts = built };
        }

        return timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Main thread, as early as possible at start-up (wave-3 performance pass): starts preparing the given models on the
    /// workers, so they decode while the main thread runs the other warm-ups; <see cref="LoadAllNow"/> then only waits
    /// for what is left and builds. Measured before: <see cref="LoadAllNow"/> blocked the start-up 111-137 ms.
    /// </summary>
    public void Prefetch(IEnumerable<uint> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        foreach (var id in ids.Where(id => id != 0).Distinct())
        {
            if (!_ready.ContainsKey(id) && !_loading.Contains(id) && !_prefetched.ContainsKey(id))
            {
                _prefetched[id] = _services.Workers.RunSync(_ => Prepare(id));
            }
        }
    }

    private async Task LoadAsync(uint id)
    {
        List<PreparedPart>? parts = null;
        try
        {
            parts = await _services.Workers.RunSync(_ => Prepare(id)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("sky", "sky model preparation failed", ("model", $"0x{id:X8}"), ("error", ex.Message));
        }

        var built = new List<SkyModelPart>();
        var index = 0;
        _services.MainThread.EnqueueSteps(
            () =>
            {
                if (parts is null || index >= parts.Count)
                {
                    _ready[id] = built.Count == 0 ? null : new SkyModel { Id = id, Parts = built };
                    _loading.Remove(id);
                    return true;
                }

                built.Add(Build(parts[index++]));
                return false;
            },
            priority: MainThreadQueue.Urgent);
    }

    /// <summary>Worker: assembly, rest pose, mesh conversion and texture decodes.</summary>
    private List<PreparedPart> Prepare(uint id)
    {
        var assembly = _services.Setups.Get(id) ?? throw new InvalidDataException($"sky model 0x{id:X8} does not exist");
        var poses = assembly.ComputeRestPose();
        var parts = new List<PreparedPart>();
        for (var i = 0; i < assembly.Parts.Count; i++)
        {
            var part = assembly.Parts[i];
            if (!part.IsGfxObj || _services.GfxObjMeshes.Get(part.GfxObjId) is not { } gfxObj || gfxObj.Mesh.SubMeshes.Count == 0)
            {
                continue;
            }

            var mesh = MeshFactory.Prepare(gfxObj.Mesh);
            var surfaces = mesh.SubMeshes.Select(s => _services.Render.PrepareSurface(SurfaceRequest.For(s.Source))).ToArray();
            var pose = poses[i];
            parts.Add(new PreparedPart(part.GfxObjId, AcToGodot.Transform(pose.Origin, pose.Orientation, pose.Scale), mesh, surfaces));
        }

        return parts;
    }

    /// <summary>Main thread: materials (texture uploads) and the ArrayMesh of one part.</summary>
    private SkyModelPart Build(PreparedPart part)
    {
        var render = _services.Render;
        var surfaces = new SkyModelSurface[part.Surfaces.Length];
        try
        {
            for (var s = 0; s < surfaces.Length; s++)
            {
                var prepared = part.Surfaces[s];
                var handle = render.AcquireMaterial(prepared);
                Texture2D? texture = null;
                var solid = Vector4.One;
                if (prepared.Material is { } material)
                {
                    if (material.IsTextured && handle.Key is not null)
                    {
                        texture = handle.Material.GetShaderParameter(MaterialFactory.TextureParameter).As<Texture2D>();
                    }
                    else
                    {
                        var texel = material.SolidColorTexel;
                        solid = new Vector4(((texel >> 16) & 0xFF) / 255f, ((texel >> 8) & 0xFF) / 255f, (texel & 0xFF) / 255f, (texel >> 24) / 255f);
                    }
                }

                surfaces[s] = new SkyModelSurface
                {
                    Material = handle.Key is null ? null : prepared.Material,
                    Addressing = prepared.Request.Addressing,
                    Handle = handle,
                    Texture = texture,
                    SolidColor = solid,
                };
            }
        }
        finally
        {
            foreach (var prepared in part.Surfaces)
            {
                prepared.Dispose();
            }
        }

        var mesh = MeshFactory.Build(part.Mesh);
        mesh.CustomAabb = CullBounds;
        return new SkyModelPart { GfxObjId = part.GfxObjId, Transform = part.Transform, Mesh = mesh, Surfaces = surfaces };
    }

    private sealed record PreparedPart(uint GfxObjId, Transform3D Transform, PreparedMesh Mesh, PreparedSurface[] Surfaces);
}
