using System.Diagnostics;
using Godot;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.Terrain.Blending;
using Lifestone.Data.Textures;
using Lifestone.Data.World.Sky;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Terrain;

/// <summary>How the per-vertex lighting normals are computed (default: <see cref="TerrainRenderer.DefaultNormalMode"/>).</summary>
public enum TerrainNormalMode
{
    /// <summary>
    /// Retail <c>CLandBlockStruct::calc_lighting</c>: only the block's own triangles (light steps at block edges).
    /// <c>--retail-normals</c>.
    /// </summary>
    RetailPerBlock = 0,

    /// <summary>
    /// Not retail: block-edge vertices also sum the neighbours' triangles (<see cref="TerrainSeamlessNormals"/>). The
    /// default since the M1 render-fidelity fix: retail's edge step is a rendering artifact, not art (docs/terrain-renderer.md §8).
    /// </summary>
    SeamlessAcrossBlocks = 1,
}

/// <summary>Shader debug views (<c>debug_view</c> in shaders/terrain/terrain.gdshader).</summary>
public enum TerrainDebugView
{
    /// <summary>Retail: merged texture × vertex light, then fog.</summary>
    Retail = 0,

    /// <summary>Raw merged colour with nearest sampling at level 0, no light, no fog (golden test).</summary>
    RawNearest = 1,

    /// <summary>Raw merged colour with the normal filtering, no light, no fog.</summary>
    RawFiltered = 2,

    /// <summary>Vertex light only.</summary>
    Light = 3,

    /// <summary>Merged colour (nearest, level 0) × vertex light, no fog (golden lighting test).</summary>
    LitNearest = 4,

    /// <summary>Fog factor (white = no fog).</summary>
    Fog = 5,

    /// <summary>Layers merged in the cell (overlays + roads): black 0 .. white 5.</summary>
    LayerCount = 6,
}

/// <summary>What the atlas upload holds, kept after the CPU copies are dropped (logs, perf overlay).</summary>
/// <param name="ColorLayers">Colour layers in the array (30 in Dereth).</param>
/// <param name="ColorSize">Edge length of every colour layer (512).</param>
/// <param name="AlphaLayers">Alpha maps in the array (8).</param>
/// <param name="AlphaSize">Edge length of every alpha map (512).</param>
/// <param name="DecodeMilliseconds">Worker time: decode, mips and Godot image creation.</param>
public sealed record TerrainAtlasSummary(int ColorLayers, int ColorSize, int AlphaLayers, int AlphaSize, double DecodeMilliseconds);

/// <summary>
/// Lifestone's retail-looking terrain: the TexMerge shader (shaders/terrain/terrain.gdshader), its two texture arrays,
/// one shared material, and per-landblock meshes. See docs/terrain-renderer.md.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b></para>
/// <list type="bullet">
/// <item><see cref="PrepareAtlas"/> (once) and <see cref="PrepareBlock"/> (per landblock, through <see cref="Builder"/>)
/// run on worker threads: dat reads (under the <see cref="DatSet"/> lock), decoding, recipes, meshes, packing, axis
/// conversion, Godot <see cref="Image"/>s (CPU-side, allowed off the main thread: docs/render-contract.md §0).</item>
/// <item>The constructor, <see cref="AttachBlock"/>, <see cref="DetachBlock"/>, <see cref="ApplySky"/> and the setters run on
/// the main thread: they create or touch Godot resources and nodes.</item>
/// </list>
/// <para><b>Lighting and fog</b> come from the render contract's <em>global</em> shader uniforms
/// (<see cref="RenderGlobals"/>, docs/render-contract.md §1): whoever drives the sky drives the terrain. Terrain reads
/// <c>ac_land_ambient</c> (<c>CLandBlockStruct::calc_lighting</c>), objects read <c>ac_ambient</c>.</para>
/// <para>One <see cref="ShaderMaterial"/> is shared by every landblock, so a landblock costs one mesh (256 vertices, 384
/// indices) and one draw call.</para>
/// </remarks>
public sealed class TerrainRenderer : IDisposable
{
    /// <summary>The terrain shader.</summary>
    public const string ShaderPath = "res://shaders/terrain/terrain.gdshader";

    /// <summary>Size of the shader's per-layer uniform arrays (<c>layer_tiling[64]</c>, <c>layer_lod_offset[64]</c>).</summary>
    public const int MaxColorLayers = 64;

    /// <summary>
    /// Day fraction of the default lighting: 0.5, the start of Midsong, retail's midday
    /// (<see cref="RenderGlobals.NoonDayFraction"/>), day group 0.
    /// </summary>
    public const float DefaultTimeOfDay = RenderGlobals.NoonDayFraction;

    /// <summary>
    /// The normal mode used unless a caller asks otherwise: seamless (render-fidelity fix; lead policy: rendering
    /// artifacts retail players would call glitches are fixed by default, with a flag restoring retail). The game, the
    /// spell viewer and the preview take <c>--retail-normals</c>; the golden test compares against whichever normals the
    /// block was built with.
    /// </summary>
    public const TerrainNormalMode DefaultNormalMode = TerrainNormalMode.SeamlessAcrossBlocks;

    private static ShaderMaterial? _warmMaterial;
    private static StringName? s_nodeName;
    private readonly Dictionary<LandblockCoord, (MeshInstance3D Instance, ArrayMesh Mesh)> _attached = new();
    private TerrainDebugView _debugView;
    private bool _disposed;

    /// <summary>Main thread: uploads the atlas and creates the shared material (see the other constructor).</summary>
    public TerrainRenderer(
        DatSet dats,
        PreparedTerrainAtlas atlas,
        TerrainNormalMode normalMode = DefaultNormalMode,
        bool keepAtlasPixels = false)
        : this(new TerrainBlockBuilder(dats, normalMode), atlas, keepAtlasPixels)
    {
    }

    /// <summary>
    /// Main thread: uploads the atlas into two <see cref="Texture2DArray"/>s, creates the shared material (which compiles
    /// the shader) and builds one throw-away mesh (first-call JIT), so no attach pays for them.
    /// </summary>
    /// <param name="builder">The worker-side block builder (its manifest must be the atlas's).</param>
    /// <param name="atlas">The prepared atlas. <b>Consumed</b>: its images are disposed once uploaded.</param>
    /// <param name="keepAtlasPixels">
    /// Keep the decoded CPU arrays (~44 MB) as <see cref="AtlasPixels"/>; only the golden test needs them (its reference
    /// compositor). Otherwise they are dropped once the GPU has them.
    /// </param>
    public TerrainRenderer(TerrainBlockBuilder builder, PreparedTerrainAtlas atlas, bool keepAtlasPixels = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(atlas);
        var timer = Stopwatch.StartNew();
        Builder = builder;
        var pixels = atlas.Pixels;
        if (!pixels.Manifest.TerrainLayers.Select(l => l.SurfaceTextureId).SequenceEqual(builder.TexMerge.Manifest.TerrainLayers.Select(l => l.SurfaceTextureId))
            || !pixels.Manifest.AlphaLayers.Select(l => l.SurfaceTextureId).SequenceEqual(builder.TexMerge.Manifest.AlphaLayers.Select(l => l.SurfaceTextureId)))
        {
            throw new ArgumentException("The atlas and the block builder use different terrain manifests.", nameof(atlas));
        }

        if (pixels.ColorLayers.Count > MaxColorLayers || pixels.AlphaLayers.Count > TerrainCellBlend.None)
        {
            throw new NotSupportedException(
                $"{pixels.ColorLayers.Count} colour layers / {pixels.AlphaLayers.Count} alpha layers exceed the shader's limits ({MaxColorLayers} / 254).");
        }

        AtlasSummary = new TerrainAtlasSummary(pixels.ColorLayers.Count, pixels.ColorSize, pixels.AlphaLayers.Count, pixels.AlphaSize, atlas.DecodeMilliseconds);
        AtlasPixels = keepAtlasPixels ? pixels : null;
        try
        {
            Textures = TerrainTextureArrays.Create(atlas);
        }
        finally
        {
            // The pixels now live in the RenderingServer: free the Godot images (and our reference to the managed copy).
            atlas.Dispose();
        }

        UploadMilliseconds = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        Material = CreateMaterial(Textures, pixels);
        TerrainBlockMesh.WarmUp(Material);
        MaterialMilliseconds = timer.Elapsed.TotalMilliseconds;

        // The renderer's material now keeps the compiled shader referenced.
        _warmMaterial?.Dispose();
        _warmMaterial = null;
    }

    /// <summary>Worker-side landblock preparation (dats, recipes, normal mode).</summary>
    public TerrainBlockBuilder Builder { get; }

    public DatSet Dats => Builder.Dats;

    /// <summary>Shared palette code → recipe cache.</summary>
    public TerrainTexMerge TexMerge => Builder.TexMerge;

    public TerrainNormalMode NormalMode => Builder.NormalMode;

    /// <summary>Layer counts and sizes of the uploaded atlas.</summary>
    public TerrainAtlasSummary AtlasSummary { get; }

    /// <summary>
    /// The decoded CPU atlas, only when the constructor was asked to keep it (the golden test's reference textures);
    /// null otherwise, so its ~44 MB is freed once uploaded.
    /// </summary>
    public TerrainAtlasPixels? AtlasPixels { get; }

    public TerrainTextureArrays Textures { get; }

    /// <summary>The material every terrain mesh uses.</summary>
    public ShaderMaterial Material { get; }

    /// <summary>Main-thread time of the atlas upload (both arrays) in the constructor.</summary>
    public double UploadMilliseconds { get; }

    /// <summary>Main-thread time of the material (shader compile) and the warm-up mesh in the constructor.</summary>
    public double MaterialMilliseconds { get; }

    /// <summary>Landblocks currently attached.</summary>
    public int AttachedCount => _attached.Count;

    /// <summary>Shader debug view (main thread).</summary>
    public TerrainDebugView DebugView
    {
        get => _debugView;
        set
        {
            _debugView = value;
            Material.SetShaderParameter("debug_view", (int)value);
        }
    }

    /// <summary>
    /// The global <c>ac_max_mip_level</c> (main thread): the highest merged-texture mip level sampled. 3 reproduces
    /// retail's at most 4 levels (<c>ImgTex::CreateD3DTexture</c>); larger values use Godot's full chain (less shimmer,
    /// not retail). It is the render contract's global, so objects switch with the terrain.
    /// </summary>
    public static float MaxMipLevel
    {
        get => RenderingServer.GlobalShaderParameterGet(RenderGlobals.MaxMipLevel).AsSingle();
        set => RenderingServer.GlobalShaderParameterSet(RenderGlobals.MaxMipLevel, value);
    }

    /// <summary>
    /// Main thread, before streaming (like <c>RenderResources.WarmUp</c>): loads the terrain shader and creates a material
    /// with it, which starts Godot's compile of the shader. Measured 2026-10-10: this call takes 4–13 ms, and the
    /// renderer's own material later costs ≈ 1.6 ms. Without it, the renderer's material creation took 97 ms on the first
    /// run after a shader change (Godot's shader cache cold) and ≈ 10 ms otherwise, inside a frame. The material is kept
    /// so the compiled shader stays referenced until the renderer's own material holds it.
    /// </summary>
    /// <returns>Milliseconds spent (0 when already warm).</returns>
    public static double WarmUpShader()
    {
        if (_warmMaterial is not null)
        {
            return 0;
        }

        var timer = Stopwatch.StartNew();
        _warmMaterial = new ShaderMaterial { Shader = LoadShader(), ResourceName = "lifestone_terrain_warmup" };
        _warmMaterial.SetShaderParameter("debug_view", 0);
        return timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>Worker thread: decodes the atlas from the dats and wraps it in Godot images.</summary>
    /// <exception cref="NotSupportedException">The region's layers cannot share texture arrays (TerrainAtlasBuilder).</exception>
    public static PreparedTerrainAtlas PrepareAtlas(DatSet dats, TextureLoader? loader = null, TextureDetail detail = TextureDetail.High)
    {
        var timer = Stopwatch.StartNew();
        var pixels = TerrainAtlasBuilder.FromDats(dats, loader, detail);
        return PreparedTerrainAtlas.Prepare(pixels, timer.Elapsed.TotalMilliseconds);
    }

    /// <summary>Worker thread: everything a landblock needs before <see cref="AttachBlock"/>; null when it does not exist.</summary>
    public PreparedTerrainBlock? PrepareBlock(LandblockCoord coord, CancellationToken cancellation = default) =>
        Builder.Prepare(coord, cancellation);

    /// <summary>
    /// Main thread: creates the landblock's mesh instance under <paramref name="landblockRoot"/>, a node placed at the
    /// landblock's south-west corner (the mesh is landblock-local).
    /// </summary>
    public MeshInstance3D AttachBlock(PreparedTerrainBlock block, Node3D landblockRoot)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(landblockRoot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        DetachBlock(block.Coord);
        var mesh = TerrainBlockMesh.Build(block, Material);
        var instance = new MeshInstance3D
        {
            Name = s_nodeName ??= "Terrain", // cached StringName (wave-3 perf: no finalizable garbage per attach)
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        landblockRoot.AddChild(instance);
        _attached[block.Coord] = (instance, mesh);
        return instance;
    }

    /// <summary>
    /// Main thread: removes a landblock's mesh instance and frees its mesh at once (its vertex buffers go now, not when
    /// the garbage collector finalises the wrapper; docs/render-contract.md §5). No-op when not attached.
    /// </summary>
    public void DetachBlock(LandblockCoord coord)
    {
        if (!_attached.Remove(coord, out var attached))
        {
            return;
        }

        if (GodotObject.IsInstanceValid(attached.Instance))
        {
            attached.Instance.Mesh = null;
            attached.Instance.QueueFree();
        }

        attached.Mesh.Dispose();
    }

    /// <summary>
    /// Main thread: sets the render contract's global lighting and fog uniforms from a sky state
    /// (<see cref="RenderGlobals.Apply"/>), with world fog forced off when <paramref name="fog"/> is false (preview key F,
    /// golden test). In the game the sky driver calls <see cref="RenderGlobals.Apply"/> itself.
    /// </summary>
    public static void ApplySky(SkyState sky, bool fog = true)
    {
        ArgumentNullException.ThrowIfNull(sky);
        RenderGlobals.Apply(sky);
        if (!fog)
        {
            RenderingServer.GlobalShaderParameterSet(RenderGlobals.FogEnabled, false);
        }
    }

    /// <summary>The retail landscape light state for the same sky (for CPU lighting references).</summary>
    public static TerrainLightingEnvironment LightingEnvironment(SkyState sky)
    {
        var l = sky.Lighting;
        return TerrainLightingEnvironment.FromSkyLighting(l.SunVector, l.SunColor.Argb, l.AmbientLevelRaw, l.AmbientColor.Argb);
    }

    /// <summary>Main thread: detaches every landblock and frees the material and both arrays.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var coord in _attached.Keys.ToArray())
        {
            DetachBlock(coord);
        }

        Material.Dispose();
        Textures.Dispose();
    }

    private static ShaderMaterial CreateMaterial(TerrainTextureArrays textures, TerrainAtlasPixels atlas)
    {
        var material = new ShaderMaterial { Shader = LoadShader(), ResourceName = "lifestone_terrain" };
        material.SetShaderParameter("terrain_layers", textures.Color);
        material.SetShaderParameter("alpha_layers", textures.Alpha);
        material.SetShaderParameter("terrain_layers_nearest", textures.Color);
        material.SetShaderParameter("alpha_layers_nearest", textures.Alpha);

        var tiling = new float[MaxColorLayers];
        var lodOffset = new float[MaxColorLayers];
        for (var i = 0; i < atlas.LayerTiling.Count; i++)
        {
            tiling[i] = atlas.LayerTiling[i];
            lodOffset[i] = atlas.LayerLodOffset[i];
        }

        material.SetShaderParameter("layer_tiling", tiling);
        material.SetShaderParameter("layer_lod_offset", lodOffset);
        material.SetShaderParameter("alpha_lod_offset", atlas.AlphaLodOffset);
        material.SetShaderParameter("debug_view", 0);
        return material;
    }

    private static Shader LoadShader() =>
        GD.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"Cannot load {ShaderPath}.");
}
