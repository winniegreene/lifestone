using Godot;
using Lifestone.Client.Effects;
using Lifestone.Client.Rendering;
using Lifestone.Client.Scheduling;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render.Particles;

/// <summary>
/// Meshes and materials for particle GfxObjs, through the shared render path: textures and surface materials come from
/// <see cref="RenderResources"/> (TextureCache, MaterialFactory), meshes from <see cref="MeshFactory"/>; the
/// materials are the surface's own, with the particle shader variant (<see cref="ParticleShaders"/>).
/// </summary>
/// <remarks>
/// <para>One ArrayMesh per batch key (drawn GfxObj, part-material state): with a part material a surface may switch blend
/// state (<see cref="PartTranslucencyRules.WithPartMaterial"/>), which Godot fixes per material; GfxObjs whose surfaces
/// all keep their state use one mesh for both (<see cref="ParticleModel.PartMaterialSwitchesBlend"/>). Materials are keyed
/// by (surface, addressing, variant). Particle GfxObjs are small (a quad drawn from both sides, a few dozen triangles at
/// most) and few (342 hardware GfxObjs across every ParticleEmitterInfo in the dats): <see cref="WarmUp"/> compiles their
/// shader variants at start-up and queues their meshes, which <see cref="PrewarmStep"/> prepares on the core WorkerPool
/// (mesh conversion, texture decoding) and finishes on the main thread within a per-frame budget (materials, uploads);
/// a mesh needed before that is built on first use, on the main thread. They are kept until <see cref="Dispose"/>.</para>
/// <para>Each particle material holds a reference to the core material of its surface (<see cref="RenderResources.AcquireMaterial"/>),
/// which holds the texture, so the texture stays resident; the references are released in <see cref="Dispose"/>.</para>
/// <para><b>Lighting.</b> Every mesh exists per lighting rule: <see cref="SurfaceLighting.Object"/> (the spell viewer, the
/// landscape's statics) and <see cref="SurfaceLighting.CellObject"/> (particles of statics inside EnvCells, which take
/// their cell's depth rules; <see cref="ParticleShaders.GenerateCode"/>). A world without cells never builds the second.</para>
/// </remarks>
public sealed class ParticleMeshLibrary : IParticleModelSource, IDisposable
{
    private readonly GfxObjMeshCache _gfxObjs;
    private readonly RenderResources _render;
    private readonly DatParticleModels _models;
    private readonly Dictionary<MeshKey, ArrayMesh?> _meshes = new();
    private readonly Dictionary<(uint SurfaceId, Lifestone.Data.Geometry.TextureAddressMode Addressing, SurfaceShaderVariant? Variant), Material> _materials = new();
    private readonly Queue<MeshKey> _prewarm = new();
    private readonly HashSet<MeshKey> _queued = new();
    private readonly Queue<(MeshKey Key, Task<PreparedParticleMesh?> Job)> _preparing = new();
    private readonly List<MaterialHandle<ShaderMaterial>> _handles = new();
    private readonly List<ShaderMaterial> _warm = new();
    private readonly WorkerPool? _workers;

    /// <summary>Prewarm meshes being prepared on workers at once (bounds the decoded images held in memory).</summary>
    public const int MaxPreparing = 8;

    /// <param name="dats">The dats.</param>
    /// <param name="gfxObjs">The shared GfxObj mesh cache.</param>
    /// <param name="render">The shared render path.</param>
    /// <param name="workers">The core WorkerPool for prewarm preparation (mesh conversion and texture decoding); null
    /// prepares on the main thread.</param>
    public ParticleMeshLibrary(DatSet dats, GfxObjMeshCache gfxObjs, RenderResources render, WorkerPool? workers = null)
    {
        _gfxObjs = gfxObjs;
        _render = render;
        _workers = workers;
        _models = new DatParticleModels(dats, gfxObjs)
        {
            Missing = (id, why) => Log.Error("particles", "particle GfxObj cannot be drawn; its particles are skipped", ("gfxobj", $"0x{id:X8}"), ("reason", why)),
        };
    }

    /// <summary>Meshes built so far.</summary>
    public int MeshCount => _meshes.Count;

    /// <summary>Particle materials created so far.</summary>
    public int MaterialCount => _materials.Count;

    /// <summary>
    /// Main thread, before the first particle is drawn: compiles every particle shader variant any particle GfxObj in the
    /// dats can need (both part-material states), so no frame pays Godot's first-use compile (7–11 ms per variant, the
    /// same cost <see cref="SurfaceShaders.WarmUp"/> removes for world surfaces).
    /// </summary>
    /// <param name="hwGfxObjIds">Every ParticleEmitterInfo's hardware GfxObj (<c>DatEffectSource.AllEmitterInfoIds</c> →
    /// <c>HwGfxObjId</c>).</param>
    /// <returns>The particle GfxObjs and the variants compiled.</returns>
    public (int GfxObjs, int Variants) WarmUp(IEnumerable<uint> hwGfxObjIds) => WarmUp(hwGfxObjIds, queueMeshes: true);

    /// <summary>
    /// <see cref="WarmUp(IEnumerable{uint})"/>, optionally without queueing every mesh: the world compiles every variant
    /// at start-up but prepares only the meshes its statics can create (<see cref="Request"/>), as their landblocks
    /// come near (objects track, living statics).
    /// </summary>
    /// <param name="hwGfxObjIds">Every ParticleEmitterInfo's hardware GfxObj.</param>
    /// <param name="queueMeshes">Also queue every mesh (object lighting) for <see cref="PrewarmStep"/>.</param>
    /// <param name="cellVariants">Also compile the <see cref="SurfaceLighting.CellObject"/> variants (the world's EnvCell statics).</param>
    public (int GfxObjs, int Variants) WarmUp(IEnumerable<uint> hwGfxObjIds, bool queueMeshes, bool cellVariants = false)
    {
        ArgumentNullException.ThrowIfNull(hwGfxObjIds);
        // The sub-meshes' own addressing, not both modes: every particle GfxObj sub-mesh is drawn with its own
        // (SurfaceRequest(SurfaceId, Addressing)), which halves the variants compiled here.
        var surfaces = new HashSet<(Lifestone.Data.Textures.SurfaceMaterial Material, Lifestone.Data.Geometry.TextureAddressMode Addressing)>();
        var gfxObjs = 0;
        foreach (var id in hwGfxObjIds.Distinct())
        {
            if (Get(id) is not { } model)
            {
                continue;
            }

            gfxObjs++;
            foreach (var level in model.Chain.GfxObjIds.Where(g => g != 0).Distinct())
            {
                if (queueMeshes)
                {
                    Enqueue(new MeshKey(new ParticleBatchKey(level, false), SurfaceLighting.Object));
                    if (model.PartMaterialSwitchesBlend)
                    {
                        Enqueue(new MeshKey(new ParticleBatchKey(level, true), SurfaceLighting.Object));
                    }
                }

                foreach (var sub in _gfxObjs.Get(level)?.Mesh.SubMeshes ?? [])
                {
                    if (_render.Loader.GetSurface(sub.SurfaceId) is { } material)
                    {
                        surfaces.Add((material, sub.Addressing));
                    }
                }
            }
        }

        var variants = new HashSet<SurfaceShaderVariant>();
        foreach (var (material, addressing) in surfaces)
        {
            // Both cut-out shortcuts unless exact (whether texels pass and blend is only known once decoded).
            foreach (var passing in _render.ExactCutouts ? (ReadOnlySpan<bool>)[true] : [false, true])
            {
                SurfaceShaderVariant v;
                try
                {
                    v = SurfaceShaderVariant.For(material, passing, addressing);
                }
                catch (NotSupportedException)
                {
                    continue; // drawn with the error material; nothing to compile
                }

                variants.Add(v);
                variants.Add(PartTranslucencyRules.WithPartMaterial(v));
                if (cellVariants)
                {
                    var cell = v with { Lighting = SurfaceLighting.CellObject };
                    variants.Add(cell);
                    variants.Add(PartTranslucencyRules.WithPartMaterial(cell));
                }
            }
        }

        foreach (var v in variants)
        {
            // The same calls the particle materials make; kept so the compiled shaders stay referenced.
            var warm = new ShaderMaterial { Shader = ParticleShaders.Get(v), ResourceName = $"particle_warmup_{v.Name}" };
            warm.SetShaderParameter("material_alpha", 1f);
            warm.SetShaderParameter("luminosity", 0f);
            warm.SetShaderParameter("alpha_ref", 200f);
            _warm.Add(warm);
        }

        return (gfxObjs, variants.Count);
    }

    /// <summary>Meshes still to build in the background (queued by <see cref="WarmUp(IEnumerable{uint})"/> or <see cref="Request"/>, or being prepared).</summary>
    public int PrewarmPending => _prewarm.Count + _preparing.Count;

    /// <summary>
    /// Main thread: queues the meshes of these particle GfxObjs (every LOD level, both part-material states where they
    /// differ) for <see cref="PrewarmStep"/>, unless built or queued already. Used by the world for the particles its
    /// statics' default scripts can create, as their landblocks are prepared.
    /// </summary>
    /// <param name="hwGfxObjIds">The particle GfxObjs.</param>
    /// <param name="lighting">The lighting rule the meshes are drawn with (<see cref="SurfaceLighting.CellObject"/> for EnvCell statics).</param>
    public int Request(IEnumerable<uint> hwGfxObjIds, SurfaceLighting lighting = SurfaceLighting.Object)
    {
        ArgumentNullException.ThrowIfNull(hwGfxObjIds);
        var queued = 0;
        foreach (var id in hwGfxObjIds)
        {
            if (Get(id) is not { } model)
            {
                continue;
            }

            foreach (var level in model.Chain.GfxObjIds)
            {
                if (level == 0)
                {
                    continue;
                }

                queued += Enqueue(new MeshKey(new ParticleBatchKey(level, false), lighting)) ? 1 : 0;
                if (model.PartMaterialSwitchesBlend)
                {
                    queued += Enqueue(new MeshKey(new ParticleBatchKey(level, true), lighting)) ? 1 : 0;
                }
            }
        }

        return queued;
    }

    /// <summary>
    /// Main thread, never blocking: the mesh of a batch when it is built (<paramref name="mesh"/> null when the GfxObj has
    /// no geometry), else false, and the mesh is queued for <see cref="PrewarmStep"/> (its particles are drawn from the
    /// frame it is ready; the world must not decode a texture on the main thread, game-core.md §7).
    /// </summary>
    public bool TryGetMesh(ParticleBatchKey key, out ArrayMesh? mesh, SurfaceLighting lighting = SurfaceLighting.Object)
    {
        var meshKey = new MeshKey(key, lighting);
        if (_meshes.TryGetValue(meshKey, out mesh))
        {
            return true;
        }

        if (Enqueue(meshKey))
        {
            MeshesRequestedLate++;
        }

        return false;
    }

    /// <summary>Meshes a frame wanted before they were prepared (statistics; each is drawn once ready).</summary>
    public int MeshesRequestedLate { get; private set; }

    private bool Enqueue(MeshKey key)
    {
        if (_meshes.ContainsKey(key) || !_queued.Add(key))
        {
            return false;
        }

        _prewarm.Enqueue(key);
        return true;
    }

    /// <summary>
    /// Main thread, once per frame: keeps up to <see cref="MaxPreparing"/> queued particle meshes preparing on the workers
    /// (mesh conversion, surface resolution, texture decoding: <see cref="MeshFactory.Prepare"/>,
    /// <see cref="RenderResources.PrepareSurface"/>), and finishes prepared ones (materials, texture uploads, the
    /// ArrayMesh) for up to <paramref name="budgetMs"/> milliseconds, so a spell's first particles do not pay for them.
    /// One mesh is never split, so a frame can overrun the budget by one finish (<see cref="SlowestPrewarmMs"/>).
    /// </summary>
    public void PrewarmStep(double budgetMs)
    {
        while (_prewarm.Count > 0 && _preparing.Count < MaxPreparing)
        {
            var key = _prewarm.Dequeue();
            if (_meshes.ContainsKey(key))
            {
                continue;
            }

            var job = _workers is { } workers
                ? workers.RunSync(_ => Prepare(key))
                : Task.FromResult(Prepare(key));
            _preparing.Enqueue((key, job));
        }

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_preparing.Count > 0 && _preparing.Peek().Job.IsCompleted
            && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs)
        {
            var (key, job) = _preparing.Dequeue();
            if (job.Status != TaskStatus.RanToCompletion)
            {
                // Loud, and once: the GfxObj's particles are not drawn (not expected; every particle GfxObj prepares).
                Log.Error("particles", "particle mesh preparation failed; its particles are not drawn", ("gfxobj", $"0x{key.Batch.GfxObjId:X8}"), ("lighting", key.Lighting), ("error", job.Exception?.GetBaseException().Message));
                _meshes.TryAdd(key, null);
                continue;
            }

            var finishStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using (var prepared = job.Result)
            {
                if (!_meshes.ContainsKey(key))
                {
                    _meshes.Add(key, Finish(key, prepared));
                }
            }

            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(finishStart).TotalMilliseconds;
            if (ms > SlowestPrewarmMs)
            {
                SlowestPrewarmMs = ms;
                SlowestPrewarmKey = key.Batch;
            }
        }
    }

    /// <summary>The slowest main-thread finish of one prewarmed mesh so far.</summary>
    public double SlowestPrewarmMs { get; private set; }

    /// <summary>The batch key of <see cref="SlowestPrewarmMs"/>.</summary>
    public ParticleBatchKey SlowestPrewarmKey { get; private set; }

    /// <summary>LOD chain and sort centre of a particle GfxObj (thread-safe; null, logged once, when it cannot be drawn).</summary>
    public ParticleModel? Get(uint gfxObjId) => _models.Get(gfxObjId);

    /// <summary>Main thread: the mesh drawn for a batch (built on first use), or null when the GfxObj has no geometry.</summary>
    public ArrayMesh? GetMesh(ParticleBatchKey key, SurfaceLighting lighting = SurfaceLighting.Object)
    {
        var meshKey = new MeshKey(key, lighting);
        if (_meshes.TryGetValue(meshKey, out var mesh))
        {
            return mesh;
        }

        mesh = Build(meshKey);
        _meshes.Add(meshKey, mesh);
        return mesh;
    }

    private ArrayMesh? Build(MeshKey key)
    {
        using var prepared = Prepare(key);
        return Finish(key, prepared);
    }

    /// <summary>
    /// Any thread: converts the GfxObj's mesh and resolves (and decodes) its surfaces. Particle parts are drawn through
    /// the part array like any object (<c>CPhysicsPart::Draw</c>): every sub-mesh is drawn. Null when the GfxObj has no
    /// geometry.
    /// </summary>
    private PreparedParticleMesh? Prepare(MeshKey key)
    {
        if (_gfxObjs.Get(key.Batch.GfxObjId) is not { } gfxObj || gfxObj.Mesh.SubMeshes.Count == 0)
        {
            return null;
        }

        var mesh = MeshFactory.Prepare(gfxObj.Mesh);
        var surfaces = mesh.SubMeshes.Select(s => _render.PrepareSurface(new SurfaceRequest(s.Source.SurfaceId, s.Source.Addressing, Lighting: key.Lighting))).ToArray();
        return new PreparedParticleMesh(mesh, surfaces);
    }

    /// <summary>Main thread: materials (texture uploads) and the ArrayMesh of a prepared particle mesh.</summary>
    private ArrayMesh? Finish(MeshKey key, PreparedParticleMesh? prepared)
    {
        if (prepared is null)
        {
            Log.Error("particles", "particle GfxObj has no drawing geometry", ("gfxobj", $"0x{key.Batch.GfxObjId:X8}"));
            return null;
        }

        var materials = prepared.Surfaces.Select(s => MaterialFor(s, key.Batch.PartMaterial)).ToArray();
        var mesh = MeshFactory.Build(prepared.Mesh, i => materials[i]);
        mesh.ResourceName = $"particle_0x{key.Batch.GfxObjId:X8}{(key.Batch.PartMaterial ? "_part" : string.Empty)}{(key.Lighting == SurfaceLighting.Object ? string.Empty : "_cell")}";
        return mesh;
    }

    /// <summary>Main thread: the particle material of a prepared surface (the caller keeps and disposes it).</summary>
    private Material MaterialFor(PreparedSurface prepared, bool partMaterial)
    {
        var surfaceId = prepared.Request.SurfaceId;
        var addressing = prepared.Request.Addressing;
        SurfaceShaderVariant? variant = prepared.Key is { } key ? (partMaterial ? PartTranslucencyRules.WithPartMaterial(key.Variant) : key.Variant) : null;
        var cacheKey = (surfaceId, addressing, variant);
        if (_materials.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // One reference per particle material: the core material holds the surface's texture while we live. Every handle
        // is kept and released in Dispose, including an error material's (uncounted, so releasing it is a no-op).
        var handle = _render.AcquireMaterial(prepared);
        _handles.Add(handle);
        Material material;
        if (variant is not { } v || handle.Key is null)
        {
            // The loud error material (logged by PrepareSurface or MaterialFactory.Acquire): never copy its parameters
            // into a particle shader, which would draw an untextured surface instead of the visible error.
            material = handle.Material;
        }
        else
        {
            var particle = new ShaderMaterial
            {
                Shader = ParticleShaders.Get(v),
                ResourceName = $"particle_surface_0x{surfaceId:X8}_{v.Name}",
            };

            // Same per-surface parameters as the core material (MaterialFactory.Create), texture included.
            foreach (var name in new[] { MaterialFactory.TextureParameter, "solid_color", "material_alpha", "luminosity", "alpha_ref", "diffuse" })
            {
                var value = handle.Material.GetShaderParameter(name);
                if (value.VariantType != Variant.Type.Nil)
                {
                    particle.SetShaderParameter(name, value);
                }
            }

            material = particle;
            Log.Debug("particles", "particle material", ("surface", $"0x{surfaceId:X8}"), ("variant", v.Name));
        }

        _materials.Add(cacheKey, material);
        return material;
    }

    /// <summary>Main thread: drops the meshes and the material references.</summary>
    public void Dispose()
    {
        // Preparations still running free their decoded images when they finish.
        foreach (var (_, job) in _preparing)
        {
            job.ContinueWith(t => (t.Status == TaskStatus.RanToCompletion ? t.Result : null)?.Dispose(), TaskScheduler.Default);
        }

        _preparing.Clear();
        _prewarm.Clear();
        _queued.Clear();
        foreach (var mesh in _meshes.Values)
        {
            mesh?.Dispose();
        }

        _meshes.Clear();
        foreach (var handle in _handles)
        {
            _render.ReleaseMaterial(handle);
        }

        _handles.Clear();
        _materials.Clear();
    }

    /// <summary>A mesh of the library: the batch it draws and the lighting rule of its materials.</summary>
    private readonly record struct MeshKey(ParticleBatchKey Batch, SurfaceLighting Lighting);
}

/// <summary>A particle mesh prepared off the main thread; disposing frees decoded texture images that were never uploaded.</summary>
internal sealed class PreparedParticleMesh(PreparedMesh mesh, PreparedSurface[] surfaces) : IDisposable
{
    public PreparedMesh Mesh { get; } = mesh;

    public PreparedSurface[] Surfaces { get; } = surfaces;

    public void Dispose()
    {
        foreach (var surface in Surfaces)
        {
            surface.Dispose();
        }
    }
}
