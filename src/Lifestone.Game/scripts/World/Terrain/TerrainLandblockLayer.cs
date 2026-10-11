using System.Collections;
using System.Diagnostics;
using Godot;
using Lifestone.Client.Scheduling;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Game.Core;

namespace Lifestone.Game.World.Terrain;

/// <summary>
/// The TexMerge terrain as a streaming layer (docs/terrain-renderer.md §5): per landblock, one mesh with the shared
/// terrain material under the landblock root, plus the landblock's exact ground sampler registered with
/// <see cref="GroundHeights"/> (walk mode and everything else that asks for the ground follow the drawn terrain).
/// </summary>
/// <remarks>
/// <para><b>Start-up.</b> <see cref="Start"/> builds the <see cref="TerrainRenderer"/> once, without a long frame: it
/// compiles the shader at once (in the main scene's <c>_Ready</c>, before the first frame, like the render warm-up), the
/// atlas is decoded on the worker pool (≈ 90 ms), then the renderer is constructed in one urgent main-thread queue item
/// (upload of the two texture arrays ≈ 11 ms, material and warm-up build ≈ 1 ms). Landblocks prepare meanwhile, since
/// <see cref="TerrainBlockBuilder"/> needs only the manifest; a prepare that finishes before the renderer exists waits
/// for it (<see cref="Ready"/>), so no attach ever runs without it. If the atlas cannot be built the failure is logged
/// once and every landblock's prepare rethrows it: loud, never a silent fallback.</para>
/// <para><b>Attach</b> is one step (≈ 0.05 ms, at most 0.9 ms measured in flight: one <see cref="ArrayMesh"/> of 256
/// vertices), so it runs in the <see cref="Attach"/> call. <b>Detach</b> frees the mesh at once and unregisters the
/// ground sampler.</para>
/// <para>Normals are seamless across landblocks by default (<see cref="TerrainRenderer.DefaultNormalMode"/>; lead decision,
/// wave 3: retail's edge light step is a glitch-fix deviation) unless <see cref="TerrainNormalMode.RetailPerBlock"/>
/// (<c>CLandBlockStruct::calc_lighting</c>, <c>--retail-normals</c>) is chosen.</para>
/// </remarks>
public sealed class TerrainLandblockLayer : ILandblockLayer, IDisposable
{
    private readonly TerrainBlockBuilder _builder;
    private readonly GroundHeights? _ground;
    private readonly IReadOnlyList<float> _landHeightTable;
    private readonly CancellationTokenSource _shutdown = new();
    private TerrainRenderer? _renderer;
    private bool _ownsRenderer;

    /// <summary>A layer over an existing renderer (main thread); the caller keeps ownership of the renderer.</summary>
    public TerrainLandblockLayer(TerrainRenderer renderer, GroundHeights? ground)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _builder = renderer.Builder;
        _ground = ground;
        _landHeightTable = renderer.Dats.TerrainRegion.LandHeightTable;
        _renderer = renderer;
        Ready = Task.FromResult(renderer);
    }

    private TerrainLandblockLayer(TerrainBlockBuilder builder, GroundHeights? ground, WorkerPool workers, MainThreadQueue mainThread)
    {
        _builder = builder;
        _ground = ground;
        _landHeightTable = builder.Dats.TerrainRegion.LandHeightTable;
        _ownsRenderer = true;
        Ready = CreateRendererAsync(workers, mainThread);
    }

    /// <summary>Completes once the renderer exists (faults if the atlas cannot be built).</summary>
    public Task<TerrainRenderer> Ready { get; }

    /// <summary>The renderer once <see cref="Ready"/> has completed (main thread).</summary>
    public TerrainRenderer? Renderer => _renderer;

    public string Name => "terrain (TexMerge)";

    /// <summary>Landblocks attached.</summary>
    public int Count => _renderer?.AttachedCount ?? 0;

    /// <summary>
    /// Main thread, before streaming starts: creates the layer and starts building its renderer (atlas decode on a
    /// worker, upload in one urgent main-thread item). The layer frees its renderer when <paramref name="owner"/> leaves
    /// the tree.
    /// </summary>
    /// <param name="services">Dats, ground heights, worker pool and main-thread queue.</param>
    /// <param name="owner">Node whose lifetime bounds the layer's (the main scene).</param>
    /// <param name="normalMode">Seamless normals (default, <see cref="TerrainRenderer.DefaultNormalMode"/>) or retail's per-block ones.</param>
    public static TerrainLandblockLayer Start(GameServices services, Node owner, TerrainNormalMode normalMode = TerrainRenderer.DefaultNormalMode)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        var dats = services.Dats ?? throw new InvalidOperationException(services.StartupError ?? "The dats are not open.");
        var layer = new TerrainLandblockLayer(new TerrainBlockBuilder(dats, normalMode), services.Ground, services.Workers, services.MainThread);
        owner.TreeExiting += layer.Dispose;

        // Compile the shader now, before the first frame (the atlas decodes on a worker meanwhile), not in a frame.
        var shaderMs = TerrainRenderer.WarmUpShader();
        Log.Info("terrain", "terrain started", ("shader_compile_ms", shaderMs), ("normals", normalMode));
        return layer;
    }

    /// <summary>Worker thread: reads and builds the landblock and its ground sampler; null when it does not exist.</summary>
    public async Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation)
    {
        if (_builder.Prepare(coord, cancellation) is not { } block)
        {
            return null;
        }

        var sampler = new TerrainSampler(block.Terrain, _landHeightTable);

        // Only at start-up: a landblock prepared before the renderer exists waits for it here, so Attach never runs
        // without it. A failed atlas rethrows here, once per landblock (loud on purpose).
        if (!Ready.IsCompletedSuccessfully)
        {
            await Ready.WaitAsync(cancellation).ConfigureAwait(false);
        }

        return new Prepared(block, sampler);
    }

    /// <summary>Main thread: registers the ground sampler and adds the landblock's mesh (one step, ≈ 0.05 ms).</summary>
    public IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot)
    {
        var (block, sampler) = (Prepared)prepared;
        if (block.Coord != coord)
        {
            throw new ArgumentException($"Prepared terrain is for {block.Coord}, not {coord}.", nameof(prepared));
        }

        var renderer = _renderer ?? throw new InvalidOperationException("The terrain renderer is not ready (Attach before Ready completed).");
        _ground?.Register(sampler);
        renderer.AttachBlock(block, landblockRoot);
        return null;
    }

    /// <summary>Main thread: frees the landblock's mesh now and unregisters its ground sampler.</summary>
    public void Detach(LandblockCoord coord)
    {
        _renderer?.DetachBlock(coord);
        _ground?.Unregister(coord);
    }

    /// <summary>Main thread: stops a pending start-up and frees the renderer this layer built.</summary>
    public void Dispose()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        _shutdown.Cancel();
        if (_ownsRenderer && _renderer is not null)
        {
            _renderer.Dispose();
            Log.Debug("terrain", "renderer disposed");
        }

        _renderer = null;
        _ownsRenderer = false;
    }

    private async Task<TerrainRenderer> CreateRendererAsync(WorkerPool workers, MainThreadQueue mainThread)
    {
        var token = _shutdown.Token;
        var timer = Stopwatch.StartNew();
        PreparedTerrainAtlas? atlas = null;
        try
        {
            atlas = await workers.RunSync(_ => TerrainRenderer.PrepareAtlas(_builder.Dats), token).ConfigureAwait(false);
            var atlasMs = timer.Elapsed.TotalMilliseconds;
            var prepared = atlas;
            var renderer = await mainThread.InvokeAsync(
                () =>
                {
                    token.ThrowIfCancellationRequested();
                    var r = new TerrainRenderer(_builder, prepared);
                    _renderer = r;
                    return r;
                },
                MainThreadQueue.Urgent,
                token).ConfigureAwait(false);
            Log.Info(
                "terrain",
                "renderer ready",
                ("color_layers", $"{renderer.AtlasSummary.ColorLayers}x{renderer.AtlasSummary.ColorSize}²"),
                ("alpha_layers", $"{renderer.AtlasSummary.AlphaLayers}x{renderer.AtlasSummary.AlphaSize}²"),
                ("atlas_worker_ms", atlasMs),
                ("upload_ms", renderer.UploadMilliseconds),
                ("material_ms", renderer.MaterialMilliseconds),
                ("vram_mb", (renderer.Textures.ColorBytes + renderer.Textures.AlphaBytes) / (1024.0 * 1024.0)),
                ("normals", renderer.NormalMode),
                ("since_start_ms", timer.Elapsed.TotalMilliseconds));
            return renderer;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Exception("terrain", "the terrain atlas could not be built; no terrain will be drawn", ex);
            throw;
        }
        finally
        {
            // The renderer's constructor consumes the atlas; this frees it if the upload never ran (shutdown, failure).
            atlas?.Dispose();
        }
    }

    /// <summary>A landblock's mesh data and ground sampler, built on a worker.</summary>
    private sealed record Prepared(PreparedTerrainBlock Block, TerrainSampler Sampler);
}
