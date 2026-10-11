using System.Diagnostics;
using Godot;
using Lifestone.Client.Objects;
using Lifestone.Client.Rendering;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render;

namespace Lifestone.Game.World.Objects;

/// <summary>
/// The objects path for a fixed set of landblocks, without the world's streaming: the spell viewer's Holtburg. Each
/// landblock is prepared on the workers (<see cref="PreparedLandblockObjects.Prepare"/>: statics, buildings, scenery and
/// living statics), its textures made resident (<see cref="LandblockAttachTextures"/>: pinned, or decoded again on a worker),
/// then attached in budgeted steps and drawn with retail LOD every frame (<see cref="LandblockObjectBatches"/>), exactly as the
/// world's objects are (docs/objects.md §2.5).
/// </summary>
/// <remarks>Main thread, except the preparation and decodes it starts on the core <see cref="Lifestone.Client.Scheduling.WorkerPool"/>.</remarks>
public sealed class StaticObjectsView : IDisposable
{
    private readonly GameServices _services;
    private readonly ObjectModelLibrary _library;
    private readonly PreparedGeometryCache _geometry = new();
    private readonly List<Block> _blocks = new();
    private bool _disposed;

    /// <param name="services">The game services.</param>
    /// <param name="landblocks">The landblocks with the node each one's objects go under (an unrotated node at its south-west corner).</param>
    /// <param name="kinds">Which objects to create.</param>
    public StaticObjectsView(GameServices services, IEnumerable<(LandblockCoord Coord, Node3D Root)> landblocks, WorldObjectKinds kinds = WorldObjectKinds.All)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ArgumentNullException.ThrowIfNull(landblocks);
        _library = ObjectsShared.Library(services);
        _ = LandblockObjectBatches.WarmUp(); // first-use JIT and interop of the batch path, here rather than in an attach step
        foreach (var (coord, root) in landblocks)
        {
            var block = new Block(coord, root);
            block.Prepare = services.Workers.RunSync(ct => PreparedLandblockObjects.Prepare(services, _library, _geometry, coord, kinds, ct), block.Cancellation.Token);
            _blocks.Add(block);
        }
    }

    /// <summary>True once every landblock's objects are attached (or have none).</summary>
    public bool IsReady => _blocks.All(b => b.Done);

    /// <summary>The slowest attach step so far (ms).</summary>
    public double SlowestStepMs { get; private set; }

    /// <summary>Objects created (statics, buildings, scenery).</summary>
    public int Objects => _blocks.Sum(b => b.Batches?.Set.Objects.Count ?? 0);

    /// <summary>
    /// Main thread, once per frame: advances the preparations and runs attach steps while the frame has spent less than
    /// <paramref name="budgetMs"/> on them (a step is never split), then re-selects every attached landblock's LOD for
    /// <paramref name="viewer"/> (render space).
    /// </summary>
    public void Process(Vector3 viewer, double budgetMs)
    {
        if (_disposed)
        {
            return;
        }

        var start = Stopwatch.GetTimestamp();
        foreach (var block in _blocks)
        {
            if (block.Done)
            {
                block.Batches?.Update(viewer);
                continue;
            }

            while (!block.Done && Stopwatch.GetElapsedTime(start).TotalMilliseconds < budgetMs)
            {
                var stepStart = Stopwatch.GetTimestamp();
                var progressed = Step(block, viewer);
                SlowestStepMs = Math.Max(SlowestStepMs, Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds);
                if (!progressed)
                {
                    break; // waiting for a worker
                }
            }
        }
    }

    /// <summary>Shows or hides every landblock's objects (their living statics keep running).</summary>
    public void SetVisible(bool visible)
    {
        foreach (var block in _blocks)
        {
            if (GodotObject.IsInstanceValid(block.Root))
            {
                block.Root.Visible = visible;
            }
        }
    }

    /// <summary>Main thread: releases every landblock's objects (meshes, textures, living statics) and stops pending work.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var block in _blocks)
        {
            block.Cancellation.Cancel();
            block.Batches?.Release();
            block.Textures?.Release();
            block.Prepared?.Dispose();
            _ = block.Decode?.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    foreach (var (_, texture) in t.Result)
                    {
                        texture.Dispose();
                    }
                }
            }, TaskScheduler.Default);
            _ = block.Prepare?.ContinueWith(t => (t.Status == TaskStatus.RanToCompletion ? t.Result : null)?.Dispose(), TaskScheduler.Default);
        }

        _blocks.Clear();
    }

    /// <summary>One unit of a landblock's progress; false while it waits for a worker.</summary>
    private bool Step(Block block, Vector3 viewer)
    {
        if (block.Batches is { } batches)
        {
            block.Done = batches.AttachStep(() => viewer, ObjectsSystem.AttachSliceMs);
            return true;
        }

        if (block.Decode is { } decode)
        {
            if (!decode.IsCompleted)
            {
                return false;
            }

            if (decode.IsCompletedSuccessfully)
            {
                block.Textures!.AddDecoded(decode.Result);
            }
            else if (decode.Exception is { } ex)
            {
                Log.Exception("objects", $"decoding the textures of {block.Coord} failed", ex.GetBaseException());
            }

            block.Decode = null;
            Begin(block);
            return true;
        }

        if (block.Prepare is not { IsCompleted: true } prepare)
        {
            return false;
        }

        block.Prepare = null;
        if (!prepare.IsCompletedSuccessfully || prepare.Result is not { } prepared)
        {
            if (prepare.Exception is { } ex)
            {
                Log.Exception("objects", $"preparing the objects of {block.Coord} failed", ex.GetBaseException());
            }

            block.Done = true; // no objects
            return true;
        }

        block.Prepared = prepared;
        block.Textures = new LandblockAttachTextures(_services.Render);
        var missing = block.Textures.PinResident(prepared);
        if (missing.Count > 0)
        {
            var render = _services.Render;
            block.Decode = _services.Workers.RunSync(ct => LandblockAttachTextures.Decode(render, missing, ct), block.Cancellation.Token);
            return true;
        }

        Begin(block);
        return true;
    }

    private void Begin(Block block)
    {
        if (!GodotObject.IsInstanceValid(block.Root))
        {
            block.Textures?.Release();
            block.Prepared?.Dispose();
            block.Done = true;
            return;
        }

        block.Batches = new LandblockObjectBatches(_services.Render, block.Prepared!, ObjectLodSettings.Retail, block.Root, block.Textures!);
        block.Textures = null; // owned by the batches from here on
    }

    private sealed class Block(LandblockCoord coord, Node3D root)
    {
        public LandblockCoord Coord { get; } = coord;

        public Node3D Root { get; } = root;

        public CancellationTokenSource Cancellation { get; } = new();

        public Task<PreparedLandblockObjects?>? Prepare { get; set; }

        public PreparedLandblockObjects? Prepared { get; set; }

        public LandblockAttachTextures? Textures { get; set; }

        public Task<(TextureKey, PreparedTexture)[]>? Decode { get; set; }

        public LandblockObjectBatches? Batches { get; set; }

        public bool Done { get; set; }
    }
}
