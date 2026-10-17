using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Rendering;
using Lifestone.Client.World;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Game.Core;
using Lifestone.Game.Render.Particles;
using Lifestone.Game.World.Interiors;
using Lifestone.Game.World.Objects;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.ServerObjects;

/// <summary>
/// AvatarParticles: draws the particles of the server's objects, and of the player's own character, with the particle renderer the world's living statics
/// use. An avatar's effect object lives in the space of its anchor landblock (<see cref="AvatarDraw.AnchorCellId"/>), so one renderer is kept for each
/// (anchor landblock, inside a cell or not): it sits at that landblock's origin, outdoors lit as an object, in a cell lit with the cell's draw state (the
/// cell's slot is each object's tag) and drawn on the interiors' cells layer, as the cell statics' particles are.
/// </summary>
/// <remarks>
/// <para><b>Hidden objects draw their particles.</b> The 14 emitters of the Hidden script are the pink bubbles round a character that is arriving (docs/avatars.md
/// section 10.2), so an object that is Hidden is not drawn but its particles are; the UnHide script stops them and fades the object in.</para>
/// <para>A particle mesh not ready yet is queued and skipped for the frame (<see cref="ParticleRenderer.BuildMissingMeshes"/> is false), never decoded on it.
/// Main thread only.</para>
/// </remarks>
public sealed class ServerObjectParticles : IDisposable
{
    private readonly Node3D _parent;
    private readonly WorldRoot _world;
    private readonly InteriorController? _interiors;
    private readonly ParticleMeshLibrary _library;
    private readonly bool _ownsLibrary;
    private readonly Dictionary<GroupKey, Group> _groups = new();
    private readonly List<GroupKey> _idle = [];
    private long _frame;

    /// <summary>Frames a landblock's renderer may go unused before it is freed (about 10 s at 60 Hz); one is rebuilt when particles return there (dod-6-7-6).</summary>
    public const int IdleFramesBeforeFree = 600;

    /// <param name="parent">The node the renderers hang from (at the render origin).</param>
    /// <param name="services">The shared services.</param>
    /// <param name="world">The streamed world (render positions).</param>
    /// <param name="interiors">The interiors layer, or null.</param>
    public ServerObjectParticles(Node3D parent, GameServices services, WorldRoot world, InteriorController? interiors)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(world);
        _parent = parent;
        _world = world;
        _interiors = interiors;
        if (LivingStaticsSystem.Instance is { } statics)
        {
            _library = statics.Particles;
        }
        else
        {
            _library = new ParticleMeshLibrary(services.Dats ?? throw new InvalidOperationException("particles need the dats"), services.GfxObjMeshes, services.Render, services.Workers);
            _ownsLibrary = true;
        }
    }

    private static readonly HashSet<uint> TablesPrepared = [];
    private static readonly Queue<uint> PendingModels = new();
    private static readonly List<uint> PumpBuffer = [];

    /// <summary>GfxObjs a frame may queue for their meshes (<see cref="PumpPending"/>): the creation burst's script tables are spread over frames, not queued in the burst's frame (M3 1.3 fix A, 1.3-R1 (a)).</summary>
    public const int ModelsPerFrame = 4;

    /// <summary>Particle GfxObjs resolved by a worker and waiting for <see cref="PumpPending"/>.</summary>
    public static int PendingModelCount => PendingModels.Count;

    /// <summary>Particle GfxObjs <see cref="PumpPending"/> has queued so far (all tables, this process).</summary>
    public static int PumpedModelCount { get; private set; }

    /// <summary>Frames on which <see cref="PumpPending"/> queued something.</summary>
    public static int PumpFrames { get; private set; }

    /// <summary>
    /// Main thread, once per frame (play mode, on a frame that did no heavy work): queues up to <paramref name="maxModels"/> of the resolved particle models for their
    /// meshes (<see cref="ParticleMeshLibrary.Request"/>; workers build them and <see cref="ParticleMeshLibrary.PrewarmStep"/> finishes them within its budget). Returns how many.
    /// </summary>
    public static int PumpPending(int maxModels)
    {
        if (PendingModels.Count == 0 || LivingStaticsSystem.Instance is not { Enabled: true } statics)
        {
            return 0;
        }

        PumpBuffer.Clear();
        while (PendingModels.Count > 0 && PumpBuffer.Count < maxModels)
        {
            PumpBuffer.Add(PendingModels.Dequeue());
        }

        statics.Particles.Request(PumpBuffer);
        PumpedModelCount += PumpBuffer.Count;
        PumpFrames++;
        return PumpBuffer.Count;
    }

    /// <summary>
    /// Main thread, when a server object's body is made (M3 1.3 s1): queues the particle meshes of every script in its PhysicsScriptTable, so a script the server plays
    /// (F755: a level-up's glow, a spell's burst) shows on the frame it lands and not a few hundred ms later, after the meshes are prepared on first use. A worker
    /// walks the table and resolves the particle models (dat reads); the main thread then queues the meshes (<see cref="ParticleMeshLibrary.Request"/>) a few models a frame (<see cref="PumpPending"/>), which workers
    /// build and <see cref="ParticleMeshLibrary.PrewarmStep"/> finishes within its budget. Once per table; nothing without the living statics' library (<c>--static-effects=off</c>).
    /// </summary>
    public static void PrepareScriptTable(GameServices services, uint scriptTableId)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (scriptTableId == 0 || LivingStaticsSystem.Instance is not { Enabled: true } statics || !TablesPrepared.Add(scriptTableId))
        {
            return;
        }

        var library = statics.Particles;
        var sources = ObjectsShared.Sources(services);
        QueueModels(
            services,
            () =>
            {
                var ids = new HashSet<uint>();
                if (sources.Effects.GetScriptTable(scriptTableId) is { } table)
                {
                    foreach (var entries in table.Scripts.Values)
                    {
                        foreach (var entry in entries)
                        {
                            CollectModels(sources, library, entry.ScriptId, ids);
                        }
                    }
                }

                return ids.ToArray();
            },
            $"table 0x{scriptTableId:X8}");
    }

    /// <summary>
    /// Main thread (M3 3.1 lane fc, the projectile warm-up): <see cref="PrepareScriptTable"/> for single scripts, such as a Setup's default script (a bolt's own trail or glow, which no table names): the worker resolves the
    /// particle models the scripts can create and the main thread queues their meshes a few a frame (<see cref="PumpPending"/>). Once per script; nothing without the living statics' library.
    /// </summary>
    public static void PrepareScripts(GameServices services, IReadOnlyList<uint> scriptIds)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scriptIds);
        if (LivingStaticsSystem.Instance is not { Enabled: true } statics)
        {
            return;
        }

        var fresh = scriptIds.Where(id => id != 0 && ScriptsPrepared.Add(id)).ToArray();
        if (fresh.Length == 0)
        {
            return;
        }

        var library = statics.Particles;
        var sources = ObjectsShared.Sources(services);
        QueueModels(
            services,
            () =>
            {
                var ids = new HashSet<uint>();
                foreach (var scriptId in fresh)
                {
                    CollectModels(sources, library, scriptId, ids);
                }

                return ids.ToArray();
            },
            $"{fresh.Length} script(s)");
    }

    private static readonly HashSet<uint> ScriptsPrepared = [];

    private static void CollectModels(Lifestone.Client.Objects.LivingStaticSources sources, ParticleMeshLibrary library, uint scriptId, HashSet<uint> ids)
    {
        foreach (var id in sources.ParticleGfxObjsOf(scriptId))
        {
            if (ids.Add(id))
            {
                _ = library.Get(id); // the model (LOD chain, degrade distance) is resolved here, not in a frame
            }
        }
    }

    private static int _resolving;

    /// <summary>
    /// Particle work not yet queued for meshes: worker resolutions still running (<see cref="PrepareScriptTable"/> and <see cref="PrepareScripts"/>) plus the resolved models waiting for <see cref="PumpPending"/>.
    /// 0 means every model asked for so far has been handed to the particle library (its workers still build the meshes). Main thread (3.1-R9: the projectile warm-up's done line waits for it).
    /// </summary>
    public static int PrepareBacklog => Volatile.Read(ref _resolving) + PendingModels.Count;

    private static void QueueModels(GameServices services, Func<uint[]> resolve, string what)
    {
        Interlocked.Increment(ref _resolving);
        var work = services.Workers.RunSync(_ => resolve());
        _ = work.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    services.MainThread.Enqueue(() =>
                    {
                        foreach (var id in t.Result)
                        {
                            PendingModels.Enqueue(id); // queued for their meshes a few a frame (PumpPending), not all in the frame the worker finishes
                        }

                        Interlocked.Decrement(ref _resolving); // after the models are pending, so PrepareBacklog never reads 0 in between
                    });
                }
                else
                {
                    Interlocked.Decrement(ref _resolving);
                    Log.Warn("particles", "the particle meshes of a PhysicsScriptTable could not be requested ahead; they are prepared when first used", ("what", what), ("problem", t.Exception?.GetBaseException().Message ?? "cancelled"));
                }
            },
            TaskScheduler.Default);
    }

    /// <summary>Objects whose particles were handed to a renderer in the last <see cref="Update"/>.</summary>
    public int ObjectsLastFrame { get; private set; }

    /// <summary>
    /// 1 when the player's own character was among <see cref="ObjectsLastFrame"/>, else 0: the login check judges the Hidden script's bubbles on this, since
    /// any other emitter in view (a lifestone, a portal, a scripted NPC) would satisfy <see cref="ObjectsLastFrame"/>.
    /// </summary>
    public int PlayerObjectsLastFrame { get; private set; }

    /// <summary>Batches drawn in the last <see cref="Update"/> (all renderers).</summary>
    public int BatchesLastFrame { get; private set; }

    /// <summary>The most objects with particles at once so far.</summary>
    public int MaxObjects { get; private set; }

    /// <summary>
    /// Draws this frame's particles. <paramref name="viewer"/> is the camera's render-space position.
    /// </summary>
    public void Update(AvatarSet avatars, Vector3 viewer)
    {
        ArgumentNullException.ThrowIfNull(avatars);
        _frame++;
        if (_ownsLibrary)
        {
            _library.PrewarmStep(2.0); // the world's living statics step a shared library; a library of our own is stepped here
        }

        foreach (var group in _groups.Values)
        {
            group.Objects.Clear();
            group.Tags.Clear();
        }

        var objects = 0;
        var playerObjects = 0;
        var viewerGlobal = _world.FromRender(viewer);
        foreach (var draw in avatars.Frame.Avatars)
        {
            if (draw.ParticleCount == 0 || !avatars.TryGet(draw.Guid, out var avatar))
            {
                continue;
            }

            if (draw.InEnvCell && (_interiors is null || !_interiors.IsCellShown(draw.CellId)))
            {
                continue;
            }

            var coord = LandblockCoord.FromId(draw.AnchorCellId);
            var key = new GroupKey(draw.AnchorCellId >> 16, draw.InEnvCell);
            if (!_groups.TryGetValue(key, out var group))
            {
                group = CreateGroup(coord, draw.InEnvCell);
                _groups.Add(key, group);
            }

            group.Objects.Add(avatar.Body.Effects);
            group.Tags.Add(draw.InEnvCell ? _interiors!.SlotOf(draw.CellId) : 0);
            group.Used = _frame;
            objects++;
            if (draw.IsPlayer)
            {
                playerObjects++;
            }
        }

        var batches = 0;
        foreach (var (key, group) in _groups)
        {
            if (group.Used != _frame)
            {
                if (group.Renderer.DrawnBatches > 0)
                {
                    group.Renderer.HideAll();
                }

                if (_frame - group.Used > IdleFramesBeforeFree)
                {
                    _idle.Add(key);
                }

                continue;
            }

            var origin = AcCoordinates.LandblockOrigin(group.Coord);
            group.Renderer.Position = _world.ToRender(origin);
            var local = new AcVector3((float)(viewerGlobal.X - origin.X), (float)(viewerGlobal.Y - origin.Y), (float)(viewerGlobal.Z - origin.Z));
            group.Renderer.UpdateParticles(group.Objects, local, group.Tags);
            batches += group.Renderer.DrawnBatches;
        }

        foreach (var key in _idle)
        {
            if (_groups.Remove(key, out var idle))
            {
                idle.Renderer.ReleaseBatches();
                if (GodotObject.IsInstanceValid(idle.Renderer))
                {
                    idle.Renderer.QueueFree();
                }
            }
        }

        _idle.Clear();
        ObjectsLastFrame = objects;
        PlayerObjectsLastFrame = playerObjects;
        BatchesLastFrame = batches;
        MaxObjects = Math.Max(MaxObjects, objects);
    }

    /// <summary>Hides every renderer's batches.</summary>
    public void HideAll()
    {
        foreach (var group in _groups.Values)
        {
            group.Renderer.HideAll();
        }

        ObjectsLastFrame = 0;
        PlayerObjectsLastFrame = 0;
        BatchesLastFrame = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var group in _groups.Values)
        {
            group.Renderer.ReleaseBatches();
            if (GodotObject.IsInstanceValid(group.Renderer))
            {
                group.Renderer.QueueFree();
            }
        }

        _groups.Clear();
        if (_ownsLibrary)
        {
            _library.Dispose();
        }
    }

    private Group CreateGroup(LandblockCoord coord, bool inCell)
    {
        var renderer = new ParticleRenderer
        {
            Name = $"AvatarParticles_{coord.LandBlockId:X8}_{(inCell ? "cell" : "out")}",
            Layers = inCell ? InteriorController.CellsLayerMask : 1u,
            BuildMissingMeshes = false,
            Lighting = inCell ? SurfaceLighting.CellObject : SurfaceLighting.Object,
        };
        renderer.Initialize(_library);
        _parent.AddChild(renderer);
        return new Group(coord, renderer);
    }

    private readonly record struct GroupKey(uint Landblock, bool InCell);

    private sealed class Group(LandblockCoord coord, ParticleRenderer renderer)
    {
        public LandblockCoord Coord { get; } = coord;

        public ParticleRenderer Renderer { get; } = renderer;

        public List<EffectObject> Objects { get; } = [];

        public List<int> Tags { get; } = [];

        public long Used;
    }
}
