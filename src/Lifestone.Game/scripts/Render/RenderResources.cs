using System.Collections.Concurrent;
using System.Diagnostics;
using Godot;
using Lifestone.Client.Caching;
using Lifestone.Client.Effects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Lifestone.Game.Core;

namespace Lifestone.Game.Render;

/// <summary>
/// The shared render resources every visual track uses: textures, surface materials and meshes, with the worker /
/// main-thread split the frame budget needs (docs/render-contract.md §4).
/// </summary>
/// <remarks>
/// <para>Typical layer flow for a mesh:</para>
/// <list type="number">
/// <item>Worker (<c>ILandblockLayer.PrepareAsync</c>): build or fetch the <c>MeshData</c>, call
/// <see cref="MeshFactory.Prepare"/>, and <see cref="PrepareSurface"/> for each sub-mesh
/// (<see cref="SurfaceRequest.For"/>).</item>
/// <item>Main thread (<c>Attach</c>): <see cref="AcquireMesh"/> for a shared mesh (it acquires the materials, builds
/// the ArrayMesh if needed and binds them), or <see cref="AcquireMaterial"/> per sub-mesh plus
/// <see cref="MeshFactory.Build"/> for a mesh of one's own.</item>
/// <item>Main thread (<c>Detach</c>): <see cref="ReleaseMesh"/> / <see cref="ReleaseMaterial"/> for everything acquired.
/// Dispose the prepared surfaces (the streamer does it for prepared objects that implement IDisposable).</item>
/// </list>
/// <para><b>Memory.</b> The caches are chained (<see cref="RenderResourceCaches{TTexture, TMaterial, TMesh}"/>): a mesh
/// holds its materials and a material its texture only while in use, so idle entries pin nothing and each budget
/// bounds its own resources; evicted Godot resources are disposed at once.</para>
/// </remarks>
public sealed class RenderResources
{
    /// <summary>Materials cached (held and idle) before idle ones are evicted.</summary>
    public const int DefaultMaterialBudget = 4096;

    public RenderResources(TextureLoader loader, DxtUploadPolicy dxtPolicy, long textureBudgetBytes, long meshBudgetBytes, bool exactCutouts = false)
    {
        Loader = loader;
        DxtPolicy = dxtPolicy;
        ExactCutouts = exactCutouts;
        Shaders = new SurfaceShaders();
        var resident = new ConcurrentDictionary<TextureKey, TextureInfo>();
        Caches = new RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh>(
            new GodotRenderResourceHost(key => resident.TryRemove(key, out _)),
            textureBudgetBytes,
            DefaultMaterialBudget,
            meshBudgetBytes);
        Textures = new TextureCache(Caches, resident);
        Materials = new MaterialFactory(Shaders, Textures, Caches);
    }

    public TextureLoader Loader { get; }

    public DxtUploadPolicy DxtPolicy { get; }

    /// <summary>
    /// When true, alpha-tested surfaces that blend (ONE/INVSRCALPHA or SRCALPHA/INVSRCALPHA after the test) always get
    /// their blended variant, as retail draws them; when false (default) the cut-out shortcut applies
    /// (docs/render-contract.md §2, <c>--cutouts</c>).
    /// </summary>
    public bool ExactCutouts { get; }

    public SurfaceShaders Shaders { get; }

    public TextureCache Textures { get; }

    public MaterialFactory Materials { get; }

    /// <summary>The chained caches (statistics, budgets).</summary>
    public RenderResourceCaches<ImageTexture, ShaderMaterial, ArrayMesh> Caches { get; }

    /// <summary>Shared-mesh statistics.</summary>
    public IResourceCacheStats Meshes => Caches.Meshes;

    /// <summary>
    /// Retail's building and environment detail textures (<c>--detail=on|off</c>; default on, retail's preference default;
    /// docs/render-contract.md §2.4). Takes effect at <see cref="WarmUp"/>, which loads the textures.
    /// </summary>
    public bool DetailEnabled { get; init; } = true;

    /// <summary>
    /// Every variant the dats' Surfaces can map to under the world's lighting (<see cref="SurfaceShaderVariant.Reachable"/>), set by <see cref="WarmUp"/> (empty
    /// before it): the other warm-ups re-light it for their own rules instead of enumerating the dats again.
    /// </summary>
    public IReadOnlyCollection<SurfaceShaderVariant> ReachableVariants { get; private set; } = [];

    /// <summary>The detail textures once <see cref="WarmUp"/> has loaded them (null before, or with <c>--detail=off</c>).</summary>
    public DetailTextures? Detail => Materials.Detail;

    /// <summary>
    /// <paramref name="variant"/> with retail's detail pass for <paramref name="requested"/> where retail draws one and
    /// its texture is loaded (<see cref="SurfaceShaderVariant.WithDetail"/>), else without. Any thread after
    /// <see cref="WarmUp"/>.
    /// </summary>
    public SurfaceShaderVariant WithDetail(SurfaceShaderVariant variant, SurfaceDetail requested) =>
        Detail is { } detail && detail.Has(requested) ? variant.WithDetail(requested) : variant with { Detail = SurfaceDetail.None };

    /// <summary>
    /// Worker-safe: resolves a surface and decodes its texture (unless it is resident). Never throws for bad data: the
    /// problem is reported in <see cref="PreparedSurface.Error"/> and the material becomes the error material.
    /// </summary>
    public PreparedSurface PrepareSurface(SurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SurfaceMaterial? material = null;
        try
        {
            material = Loader.GetSurface(request.SurfaceId);
            if (material is null)
            {
                return Failed($"Surface 0x{request.SurfaceId:X8} does not exist");
            }

            if (!material.IsTextured)
            {
                var solid = SurfaceShaderVariant.For(material, passingTexelsBlend: false, request.Addressing, request.Cull).WithLighting(request.Lighting);
                return new PreparedSurface(request, material, new MaterialKey(material.Id, null, solid), null, null);
            }

            var stId = request.SurfaceTextureOverride ?? material.SurfaceTextureId;
            var resolution = Loader.ResolveSurfaceTexture(stId);
            if (resolution is null || !resolution.IsResolved)
            {
                return Failed($"Surface 0x{request.SurfaceId:X8}: SurfaceTexture 0x{stId:X8} does not resolve ({resolution})");
            }

            // The plain key first; a resident DXT1 with punch-through texels needs its RGBA8 twin on alpha surfaces.
            var textureKey = TextureCache.KeyFor(material, resolution.RenderSurfaceId, request.Palette, DxtPolicy, dxt1WithAlpha: false);
            PreparedTexture? fresh = null;
            var resident = Textures.TryGetResidentInfo(textureKey, out var info);
            if (resident && info.Dxt1WithAlpha)
            {
                textureKey = TextureCache.KeyFor(material, resolution.RenderSurfaceId, request.Palette, DxtPolicy, dxt1WithAlpha: true);
                resident = Textures.TryGetResidentInfo(textureKey, out info);
            }

            if (!resident)
            {
                // Shared: concurrent prepares of landblocks that use the same texture decode it once (wave-3 perf pass).
                fresh = Textures.PrepareShared(Loader, material, resolution.RenderSurfaceId, request.Palette, DxtPolicy);
                info = fresh.Info;
                textureKey = fresh.Key;
            }

            var variant = SurfaceShaderVariant.For(material, info.PassingTexelsBlend || ExactCutouts, request.Addressing, request.Cull).WithLighting(request.Lighting);

            // Render-fidelity fix: retail's detail pass. DrawEnvCell makes the environment detail current for every cell
            // mesh; buildings ask for theirs (docs/render-contract.md §2.4).
            variant = WithDetail(variant, request.Lighting == SurfaceLighting.EnvCell ? SurfaceDetail.Environment : request.Detail);
            return new PreparedSurface(request, material, new MaterialKey(material.Id, textureKey, variant), fresh, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or NotSupportedException)
        {
            return Failed($"Surface 0x{request.SurfaceId:X8}: {ex.Message}");
        }

        PreparedSurface Failed(string error)
        {
            Log.Error("render", error);
            return new PreparedSurface(request, material, null, null, error);
        }
    }

    /// <summary>
    /// Main thread, at start-up (before streaming): compiles the shader of every variant the dats' Surfaces can map to,
    /// so no attach step pays Godot's first-use shader compile (7-11 ms each; <see cref="SurfaceShaders.WarmUp"/>), and runs the upload path once (first-call JIT).
    /// </summary>
    public (int Surfaces, int Variants, double EnumerateMs, double WarmMs) WarmUp(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var timer = Stopwatch.StartNew();
        var ids = dats.GetAllPortalIds<DatReaderWriter.DBObjs.Surface>();
        var variants = SurfaceShaderVariant.Reachable(ids.Select(Loader.GetSurface).OfType<SurfaceMaterial>(), ExactCutouts);
        ReachableVariants = variants;
        var enumerateMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();

        // Render-fidelity fix: the detail textures, and the building-detail variants next to the plain ones (the interiors'
        // warm-up compiles the environment-detail cell variants through WithDetail).
        if (DetailEnabled && Materials.Detail is null)
        {
            Materials.Detail = DetailTextures.Load(dats, Loader);
        }

        // M2 portal view: creature mode's variants too (the tunnel and the figures drawn in its views). Fog and detail do not
        // exist there, so the variants that differ only in them collapse.
        Shaders.WarmUp(variants.Concat(variants.Select(v => WithDetail(v, SurfaceDetail.Building)).Where(v => v.Detail != SurfaceDetail.None)));
        var creatureTimer = Stopwatch.StartNew();
        var creatureCompiled = Shaders.WarmUp(variants.Select(v => v.WithLighting(SurfaceLighting.CreatureMode)).Distinct());
        Log.Debug("render", "creature-mode surface variants warmed up", ("variants", creatureCompiled), ("ms", creatureTimer.Elapsed.TotalMilliseconds));
        WarmUpUploadPath(ids);
        return (ids.Count, variants.Count, enumerateMs, timer.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Runs the whole mesh path once (decode, upload, material, mesh build, binding, release, eviction) on a real
    /// textured surface and a throw-away triangle, then empties the caches: the first call of each step costs 3-14 ms of
    /// one-time JIT and interop set-up (measured), which would otherwise land in the first attach steps.
    /// </summary>
    private void WarmUpUploadPath(IReadOnlyList<uint> surfaceIds)
    {
        const string key = "lifestone:warm-up";
        foreach (var id in surfaceIds)
        {
            if (Loader.GetSurface(id) is not { IsTextured: true } material
                || Loader.ResolveSurfaceTexture(material.SurfaceTextureId) is not { IsResolved: true })
            {
                continue;
            }

            using var surface = PrepareSurface(new SurfaceRequest(id, Lifestone.Data.Geometry.TextureAddressMode.Wrap));
            if (surface.Key is null)
            {
                continue;
            }

            var triangle = new Lifestone.Data.Geometry.MeshData(
            [
                new Lifestone.Data.Geometry.SubMesh(
                    id,
                    Lifestone.Data.Geometry.MeshSide.Positive,
                    Lifestone.Data.Geometry.TextureAddressMode.Wrap,
                    [System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitZ],
                    [System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitY],
                    [System.Numerics.Vector2.Zero, System.Numerics.Vector2.UnitX, System.Numerics.Vector2.UnitY],
                    [0, 1, 2]),
            ]);
            AcquireMesh(key, MeshFactory.Prepare(triangle), [surface]);
            ReleaseMesh(key);
            Caches.ClearIdle();
            return;
        }
    }

    /// <summary>The suffix of the key a mesh's blended twin is held under (<see cref="FadeTwin"/>): the mesh's own key plus this.</summary>
    public const string FadeKeySuffix = ":fade";

    /// <summary>
    /// The blended twin of a prepared surface (the materialise fade, D45 and D63): the same surface and texture, its variant turned by
    /// <see cref="PartTranslucencyRules.WithPartMaterial"/> into the one retail draws a part with a translucency material (<c>D3DPolyRender::SetSurface</c>): SRCALPHA/INVSRCALPHA,
    /// no alpha test, no depth write; a surface that already blends without a test keeps its variant. The twin carries no decoded texture of its own: acquire the original
    /// first, so the texture is resident. Pure; any thread.
    /// </summary>
    public static PreparedSurface FadeTwin(PreparedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return surface.Key is { } key
            ? new PreparedSurface(surface.Request, surface.Material, key with { Variant = PartTranslucencyRules.WithPartMaterial(key.Variant) }, null, surface.Error)
            : surface;
    }

    /// <summary>Main thread: the shared material of a prepared surface (one reference; release the handle on detach).</summary>
    public MaterialHandle<ShaderMaterial> AcquireMaterial(PreparedSurface prepared) => Materials.Acquire(prepared, Loader, DxtPolicy);

    /// <summary>Main thread: drops a material reference.</summary>
    public void ReleaseMaterial(MaterialHandle<ShaderMaterial> handle) => Materials.Release(handle);

    /// <summary>
    /// Main thread: one reference to the shared mesh <paramref name="key"/> (e.g. <c>"gfxobj:0x01000C1E:Object"</c>).
    /// If it is not cached it is built from <paramref name="prepared"/> with the materials of
    /// <paramref name="surfaces"/> (one per prepared sub-mesh); if it is cached but idle, its materials are acquired
    /// from <paramref name="surfaces"/> and bound again. Release it with <see cref="ReleaseMesh"/>.
    /// </summary>
    /// <remarks>
    /// The key must identify the geometry and its surfaces. Appearance and lighting are part of it (M2): the actors' keys
    /// (<c>ActorMeshLibrary.Key</c>) are <c>gfxobj:0x…:Object</c> for the world's lighting, the lighting rule's name appended for any
    /// other (<c>:CreatureMode</c>, <c>:DynamicObject</c>, <c>:DynamicCellObject</c>: a material carries its lighting rule), and the
    /// part's ObjDesc dress (<c>PartAppearance.VariantKey</c>: its texture swaps and its modified palette) after that, so two
    /// characters dressed alike share meshes and one dressed differently never does.
    /// </remarks>
    public ArrayMesh AcquireMesh(string key, PreparedMesh prepared, IReadOnlyList<PreparedSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(surfaces);
        if (surfaces.Count != prepared.SubMeshes.Count)
        {
            throw new ArgumentException($"{surfaces.Count} surfaces for {prepared.SubMeshes.Count} sub-meshes.", nameof(surfaces));
        }

        if (Caches.TryAcquireMesh(key, () => AcquireMaterials(surfaces), out var cached))
        {
            return cached;
        }

        var materials = AcquireMaterials(surfaces);
        ArrayMesh mesh;
        try
        {
            mesh = MeshFactory.Build(prepared);
        }
        catch
        {
            foreach (var material in materials)
            {
                ReleaseMaterial(material);
            }

            throw;
        }

        return Caches.AddMesh(key, mesh, materials, prepared.Bytes);
    }

    /// <summary>Main thread: drops a mesh reference.</summary>
    public void ReleaseMesh(string key) => Caches.ReleaseMesh(key);

    private MaterialHandle<ShaderMaterial>[] AcquireMaterials(IReadOnlyList<PreparedSurface> surfaces)
    {
        var materials = new MaterialHandle<ShaderMaterial>[surfaces.Count];
        for (var i = 0; i < materials.Length; i++)
        {
            materials[i] = AcquireMaterial(surfaces[i]);
        }

        return materials;
    }

    /// <summary>Binds and frees Godot resources for the chained caches. Main thread.</summary>
    private sealed class GodotRenderResourceHost(Action<TextureKey> textureFreed) : IRenderResourceHost<ImageTexture, ShaderMaterial, ArrayMesh>
    {
        public void BindTexture(ShaderMaterial material, ImageTexture? texture) =>
            material.SetShaderParameter(MaterialFactory.Names.Texture, texture is null ? default : Variant.From(texture)); // cached StringName (wave-3 perf)

        public void BindMaterials(ArrayMesh mesh, IReadOnlyList<ShaderMaterial>? materials)
        {
            var count = mesh.GetSurfaceCount();
            for (var i = 0; i < count; i++)
            {
                mesh.SurfaceSetMaterial(i, materials?[i]!);
            }
        }

        // Dispose drops the C# reference; Godot frees the resource (and its RID) once nothing else references it, which
        // is at once because idle entries were unbound when they went idle.
        public void FreeTexture(TextureKey key, ImageTexture texture)
        {
            textureFreed(key);
            texture.Dispose();
        }

        public void FreeMaterial(MaterialKey key, ShaderMaterial material) => material.Dispose();

        public void FreeMesh(string key, ArrayMesh mesh) => mesh.Dispose();
    }
}
