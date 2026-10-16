using System.Diagnostics;
using Godot;
using Lifestone.Client.Audio;
using Lifestone.Client.Objects;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Render.Particles;
using AcFrame = Lifestone.Data.Animation.AcFrame;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.World.Objects;

/// <summary>Counters for the overlay and the logs.</summary>
public sealed class LivingStaticsStats
{
    public int Sources { get; internal set; }

    public int MaxSources { get; internal set; }

    public int MaxStatics { get; internal set; }

    public int Statics { get; internal set; }

    public int Stepped { get; internal set; }

    public int Emitters { get; internal set; }

    public int DegradedEmitters { get; internal set; }

    public int LiveParticles { get; internal set; }

    public int DrawnParticles { get; internal set; }

    public int Batches { get; internal set; }

    public double StepMs { get; internal set; }

    public double DrawMs { get; internal set; }

    public double MaxStepMs { get; internal set; }

    /// <summary>The slowest step without the garbage collection that fell in it (the statics' own work).</summary>
    public double MaxStepWorkMs { get; internal set; }

    public double MaxDrawMs { get; internal set; }

    public int MaxDrawnParticles { get; internal set; }

    /// <summary>Frames on which the step budget ran out before every static got its turn.</summary>
    public long BudgetHits { get; internal set; }

    /// <summary>Frames with at least one source (the averages' denominator).</summary>
    public long Frames { get; internal set; }

    public double TotalStepMs { get; internal set; }

    public double TotalDrawMs { get; internal set; }

    public long TotalDrawnParticles { get; internal set; }
}

/// <summary>
/// Living statics (docs/objects.md §1.7): runs the default scripts and default animations of the statics the world has
/// created (the landscape objects of the 3×3 around the viewer, and the statics of attached EnvCells) like retail's
/// <c>CPhysics::UseTime</c> → <c>CPhysicsObj::animate_static_object</c>, and draws their particles through the shared
/// particle renderer.
/// </summary>
/// <remarks>
/// <para><b>Sources.</b> The objects layer registers a landblock's living statics when its objects finish attaching and
/// removes them when the landblock leaves the 3×3 (retail creates and destroys them with the objects); the interiors
/// register those of each attached chunk of EnvCells, drawn on the cells' layer and only for cells in the portal view.
/// Each source has a <see cref="ParticleRenderer"/> under its landblock root (landblock-local coordinates, moved by the
/// floating origin).</para>
/// <para><b>Per frame</b> (priority 6: after the camera, the objects' LOD and the interiors' view, before the world rebases):
/// statics step round-robin within <see cref="StepBudgetMs"/> (<see cref="LivingStaticSet"/>); moved parts go back to their
/// owner (the objects' batches or the cells' LOD states); then every source's particles are drawn. Particle meshes are
/// prepared on workers ahead of need (<see cref="ParticleMeshLibrary.Request"/>) and finished within
/// <see cref="PrewarmBudgetMs"/>; a batch whose mesh is not ready yet is skipped, never built on the main thread.</para>
/// </remarks>
public partial class LivingStaticsSystem : Node
{
    /// <summary>Main-thread time the statics may step per frame; the rest step on later frames with a longer elapsed time.</summary>
    public const double StepBudgetMs = 1.5;

    /// <summary>Main-thread time per frame for finishing prepared particle meshes (materials, texture uploads).</summary>
    public const double PrewarmBudgetMs = 1.0;

    /// <summary>Steps slower than this are logged (debug) with the garbage collection inside them.</summary>
    private const double SlowStepMs = 4.0;

    private static LivingStaticsSystem? _instance;

    /// <summary>One for every set: the sink is the host's, the listener is set per source each frame.</summary>
    private static readonly LivingStaticSounds SharedSounds = new();

    /// <summary>
    /// Where the statics' sound hooks go (M3 1.3 s1: play mode sets the sound router, with the avatars'); null drops them, as M1 did. The
    /// listener is the camera (retail <c>SmartBox::set_viewer</c> → <c>SoundManager::SetPlayerPosition</c>).
    /// </summary>
    public static IEffectEventSink? EffectEvents
    {
        get => SharedSounds.Sink;
        set => SharedSounds.Sink = value;
    }

    private readonly List<Registration> _sources = new();
    private readonly HashSet<uint> _warnedModels = new();
    private GameServices _services = null!;
    private ParticleMeshLibrary _particles = null!;
    private LivingStaticSources _sourceData = null!;
    private ObjectModelLibrary _library = null!;
    private int _next;

    public LivingStaticsStats Stats { get; } = new();

    /// <summary>False with <c>--static-effects=off</c>: nothing registers.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The installed system, if any.</summary>
    public static LivingStaticsSystem? Instance => _instance is { } i && IsInstanceValid(i) ? i : null;

    /// <summary>
    /// Any thread: the particle models (degrade distances, LOD chains) of the installed system, so the workers preparing a
    /// landblock resolve its statics' particle GfxObjs there and not in a frame (<see cref="WarmParticleModels"/>).
    /// </summary>
    private static volatile Lifestone.Client.Effects.IParticleModelSource? _sharedModels;

    /// <summary>
    /// Any thread (workers preparing objects): resolves the particle models a plan's statics can create (dat reads, mesh
    /// conversion), so neither the step's draw-range test nor the mesh requests do it on the main thread.
    /// </summary>
    public static void WarmParticleModels(LivingStaticPlan? plan)
    {
        if (plan is null || _sharedModels is not { } models)
        {
            return;
        }

        foreach (var id in plan.ParticleGfxObjs)
        {
            _ = models.Get(id);
        }
    }

    /// <summary>
    /// Main thread, start-up: adds the system under <paramref name="parent"/> once (the objects layer and the interiors both
    /// call it) and compiles every particle shader variant (so no frame compiles one).
    /// </summary>
    /// <param name="parent">The node to add the system under.</param>
    /// <param name="services">The game services.</param>
    /// <param name="particles">A particle library to share (the spell viewer's, warmed up already; not disposed by the
    /// system), or null to create and warm one.</param>
    public static LivingStaticsSystem Install(Node parent, GameServices services, ParticleMeshLibrary? particles = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);
        if (Instance is { } existing)
        {
            return existing;
        }

        var system = new LivingStaticsSystem { Name = "LivingStatics" };
        system.Initialize(services, particles);
        parent.AddChild(system);
        _instance = system;
        return system;
    }

    private void Initialize(GameServices services, ParticleMeshLibrary? particles)
    {
        _services = services;
        Enabled = services.Options.StaticEffects;
        ProcessPriority = 6;
        if (!Enabled)
        {
            Log.Info("objects", "living statics off (--static-effects=off): statics show at rest, without their scripts and animations");
            return;
        }

        var timer = Stopwatch.StartNew();
        var dats = services.Dats!;
        _library = ObjectsShared.Library(services);
        _sourceData = ObjectsShared.Sources(services);
        if (particles is not null)
        {
            _particles = particles;
            _ownsParticles = false;
            _sharedModels = particles;
            var warmMs = WarmUpStepping(services);
            Log.Info("objects", "living statics ready (shared particle meshes)", ("step_warm_ms", warmMs), ("step_budget_ms", StepBudgetMs));
            return;
        }

        _particles = new ParticleMeshLibrary(dats, services.GfxObjMeshes, services.Render, services.Workers);
        _sharedModels = _particles;
        var effects = (Lifestone.Data.Effects.DatEffectSource)_sourceData.Effects;
        var hw = effects.AllEmitterInfoIds().Select(id => effects.GetEmitterInfo(id)?.HwGfxObjId ?? 0).Where(id => id != 0);
        var (gfxObjs, variants) = _particles.WarmUp(hw, queueMeshes: false, cellVariants: true); // cell variants: EnvCell statics' particles
        var stepWarmMs = WarmUpStepping(services);
        Log.Info("objects", "living statics ready", ("particle_gfxobjs", gfxObjs), ("particle_variants", variants), ("ms", timer.Elapsed.TotalMilliseconds),
            ("step_warm_ms", stepWarmMs), ("step_budget_ms", StepBudgetMs));
    }

    /// <summary>
    /// Start-up: steps Holtburg's living statics (torches, chimneys) for a few updates on a throw-away set, so the first
    /// frames with statics do not pay first-use JIT of the script, emitter and particle code (measured: 5-14 ms steps in
    /// the first frames without it).
    /// </summary>
    private double WarmUpStepping(GameServices services)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var coord = LandblockCoord.Holtburg;
            var statics = Lifestone.Data.World.Cells.LandblockStatics.Read(services.Dats!, coord);
            var set = LandblockObjectSet.Build(coord, statics, null, _library, WorldObjectKinds.Statics);
            if (LivingStaticPlan.Create(set, _library, _sourceData) is not { } plan)
            {
                return timer.Elapsed.TotalMilliseconds;
            }

            WarmParticleModels(plan);
            var living = new LivingStaticSet(plan, _library, _sourceData, _particles, 0.0, 1);
            var viewer = set.Objects[plan.ObjectIndices[0]].Origin;
            for (var t = 0.05; t < 0.5; t += 0.05)
            {
                living.Advance(t, viewer, null, long.MaxValue);
            }

            // The draw path too (batch nodes, MultiMeshes, uploads), building Holtburg's few particle meshes now, at start-up.
            var renderer = new ParticleRenderer { Name = "WarmUp", BuildMissingMeshes = true };
            renderer.Initialize(_particles);
            renderer.UpdateParticles(living.Drawn, viewer);
            renderer.ReleaseBatches();
            renderer.Free();
        }
        catch (Exception ex)
        {
            Log.Exception("objects", "living statics warm-up failed; the first frames pay the first-use costs", ex);
        }

        return timer.Elapsed.TotalMilliseconds;
    }

    private bool _ownsParticles = true;

    /// <summary>The shared particle meshes (also the particle models: degrade distances).</summary>
    public ParticleMeshLibrary Particles => _particles;

    /// <summary>
    /// Main thread: creates the living statics of a plan now (retail creates them with their objects) and draws their
    /// particles under <paramref name="parent"/>. Returns null when living statics are off.
    /// </summary>
    /// <param name="coord">The landblock (seed and logs).</param>
    /// <param name="parent">A node at the landblock's origin (landblock-local coordinates).</param>
    /// <param name="plan">The statics (worked out on a worker).</param>
    /// <param name="layers">Visual layers of the particle batches.</param>
    /// <param name="cellVisible">For EnvCell statics: whether a cell is in the portal view this frame.</param>
    /// <param name="moved">Gets the set (and the landblock-local viewer) after a frame in which some parts moved.</param>
    /// <param name="cellSlot">For EnvCell statics: a cell's slot in the interiors' cell-state table (0 = none yet). Their
    /// particles are then drawn with the cell-object variants and the cell's depth rules (front depth, doorway clipping),
    /// like the cell's own geometry (<see cref="ParticleShaders.GenerateCode"/>).</param>
    public Registration? Register(LandblockCoord coord, Node3D parent, LivingStaticPlan plan, uint layers, Func<uint, bool>? cellVisible, Action<LivingStaticSet, AcVector3>? moved,
        Func<uint, int>? cellSlot = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(plan);
        if (!Enabled)
        {
            return null;
        }

        var set = new LivingStaticSet(plan, _library, _sourceData, _particles, Now, unchecked((int)coord.LandBlockId + (_sources.Count * 104729))) { Sounds = SharedSounds };
        foreach (var s in set.Statics)
        {
            // Loud, once per Setup: hooks the step executes but M1 does not draw (docs/objects.md §4).
            if (_sourceData.UndrawnHooks(s.Model) is { Count: > 0 } hooks && _warnedModels.Add(s.Model.Id))
            {
                Log.Warn("objects", "TODO(objects): living static uses hooks M1 does not draw yet", ("setup", $"0x{s.Model.Id:X8}"), ("hooks", string.Join(",", hooks)), ("landblock", coord));
            }
        }

        var lighting = cellSlot is null ? Lifestone.Client.Rendering.SurfaceLighting.Object : Lifestone.Client.Rendering.SurfaceLighting.CellObject;
        var renderer = new ParticleRenderer { Name = "StaticParticles", Layers = layers, BuildMissingMeshes = false, Lighting = lighting };
        renderer.Initialize(_particles);
        parent.AddChild(renderer);
        _particles.Request(plan.ParticleGfxObjs, lighting);
        var registration = new Registration(coord, parent, set, renderer, cellVisible, moved, cellSlot);
        _sources.Add(registration);
        Stats.Sources = _sources.Count;
        Stats.MaxSources = Math.Max(Stats.MaxSources, _sources.Count);
        return registration;
    }

    /// <summary>Main thread: destroys a source's living statics and their particle batches (retail <c>RemoveStaticAnimatingObject</c>).</summary>
    public void Unregister(Registration? registration)
    {
        if (registration is null || !_sources.Remove(registration))
        {
            return;
        }

        registration.Renderer.ReleaseBatches();
        if (IsInstanceValid(registration.Renderer))
        {
            registration.Renderer.QueueFree();
        }

        Stats.Sources = _sources.Count;
    }

    /// <summary>
    /// The clock statics run on: the engine's process time (seconds), which is retail's wall clock in a normal run and the
    /// fixed frame time under Movie Maker (<c>--fixed-fps</c>), so recordings show statics at their real speed.
    /// </summary>
    public double Now => _now;

    private double _now = 1.0;

    private const int SteadyAfterFrames = 600;

    private readonly Lifestone.Client.Diagnostics.FrameTimeRecorder _frameTimes = new();
    private long _frame;

    public override void _Process(double delta)
    {
        // Steady-state frame times for the exit summary (on/off comparisons, docs/objects.md §3): from frame 600 on.
        if (++_frame > SteadyAfterFrames && _frameTimes.Count < 200_000)
        {
            _frameTimes.Add(delta * 1000.0);
        }

        if (!Enabled)
        {
            return;
        }

        _now += delta;
        if (_ownsParticles)
        {
            _particles.PrewarmStep(PrewarmBudgetMs); // a shared library is stepped by its owner
        }

        if (_sources.Count == 0)
        {
            Stats.Statics = Stats.Stepped = Stats.Emitters = Stats.LiveParticles = Stats.DrawnParticles = Stats.Batches = 0;
            return;
        }

        var camera = GetViewport()?.GetCamera3D();
        var eye = camera?.GlobalPosition ?? Vector3.Zero;
        var now = Now;
        var gcPauseBefore = GC.GetTotalPauseDuration();
        var gc0Before = GC.CollectionCount(0);
        var start = Stopwatch.GetTimestamp();
        var deadline = start + (long)(StepBudgetMs * Stopwatch.Frequency / 1000.0);
        int statics = 0, stepped = 0, emitters = 0, degraded = 0, live = 0;
        var complete = true;

        // Round-robin over the sources too, so a budget that runs out does not always starve the same landblocks.
        var count = _sources.Count;
        _next %= count;
        for (var n = 0; n < count; n++)
        {
            var source = _sources[(_next + n) % count];
            if (!IsInstanceValid(source.Parent))
            {
                continue;
            }

            var viewer = Local(source, eye);
            if (camera is null)
            {
                SharedSounds.ClearListener();
            }
            else
            {
                SharedSounds.SetListener(ListenerFrame(source, camera, viewer));
            }

            var all = source.Set.Advance(now, viewer, source.CellVisible, complete ? deadline : start);
            complete &= all;
            if (source.Set.Moved.Count > 0)
            {
                source.Moved?.Invoke(source.Set, viewer);
            }

            var s = source.Set.Stats;
            statics += s.Statics;
            stepped += s.Stepped;
            emitters += s.Emitters;
            degraded += s.DegradedEmitters;
            live += s.LiveParticles;
        }

        _next = (_next + 1) % count;
        var stepMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (stepMs > SlowStepMs)
        {
            // A step over budget is a garbage collection or a burst of first steps (new emitters allocate their particles).
            var gcMs = (GC.GetTotalPauseDuration() - gcPauseBefore).TotalMilliseconds;
            Log.Debug("objects", "slow living statics step", ("ms", stepMs), ("gc_pause_ms", gcMs), ("gc0", GC.CollectionCount(0) - gc0Before),
                ("stepped", stepped), ("sources", count), ("frame", _frame));
            Stats.MaxStepWorkMs = Math.Max(Stats.MaxStepWorkMs, stepMs - gcMs);
        }
        else
        {
            Stats.MaxStepWorkMs = Math.Max(Stats.MaxStepWorkMs, stepMs);
        }
        var drawStart = Stopwatch.GetTimestamp();
        int drawn = 0, batches = 0;
        foreach (var source in _sources)
        {
            if (!IsInstanceValid(source.Parent) || !IsInstanceValid(source.Renderer))
            {
                continue;
            }

            if (source.Set.Drawn.Count == 0)
            {
                if (source.Renderer.DrawnBatches > 0)
                {
                    source.Renderer.HideAll();
                }

                continue;
            }

            source.Renderer.UpdateParticles(source.Set.Drawn, Local(source, eye), CellSlots(source));
            drawn += source.Renderer.DrawList.Stats.Drawn;
            batches += source.Renderer.DrawnBatches;
        }

        var drawMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
        Stats.Frames++;
        Stats.BudgetHits += complete ? 0 : 1;
        (Stats.Statics, Stats.Stepped, Stats.Emitters, Stats.DegradedEmitters, Stats.LiveParticles) = (statics, stepped, emitters, degraded, live);
        (Stats.DrawnParticles, Stats.Batches, Stats.StepMs, Stats.DrawMs) = (drawn, batches, stepMs, drawMs);
        Stats.MaxStepMs = Math.Max(Stats.MaxStepMs, stepMs);
        Stats.MaxDrawMs = Math.Max(Stats.MaxDrawMs, drawMs);
        Stats.MaxDrawnParticles = Math.Max(Stats.MaxDrawnParticles, drawn);
        Stats.MaxStatics = Math.Max(Stats.MaxStatics, statics);
        Stats.TotalStepMs += stepMs;
        Stats.TotalDrawMs += drawMs;
        Stats.TotalDrawnParticles += drawn;
    }

    public override void _ExitTree()
    {
        Log.Info(
            "objects",
            "frame times (steady state)",
            ("living_statics", Enabled),
            ("frames", _frameTimes.Count),
            ("avg_ms", _frameTimes.Average),
            ("p99_ms", _frameTimes.Count > 0 ? _frameTimes.Percentile(99) : 0),
            ("max_ms", _frameTimes.Max),
            ("draw_calls", RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)));
        if (!Enabled)
        {
            return;
        }

        Log.Info(
            "objects",
            "living statics summary",
            ("max_sources", Stats.MaxSources),
            ("max_statics", Stats.MaxStatics),
            ("emitters", Stats.Emitters),
            ("max_drawn_particles", Stats.MaxDrawnParticles),
            ("avg_drawn_particles", Stats.Frames > 0 ? Stats.TotalDrawnParticles / (double)Stats.Frames : 0),
            ("avg_step_ms", Stats.Frames > 0 ? Stats.TotalStepMs / Stats.Frames : 0),
            ("avg_draw_ms", Stats.Frames > 0 ? Stats.TotalDrawMs / Stats.Frames : 0),
            ("max_step_ms", Stats.MaxStepMs),
            ("max_step_work_ms", Stats.MaxStepWorkMs),
            ("max_draw_ms", Stats.MaxDrawMs),
            ("budget_hits", Stats.BudgetHits),
            ("frames", Stats.Frames),
            ("particle_meshes", _particles.MeshCount),
            ("particle_meshes_late", _particles.MeshesRequestedLate),
            ("slowest_particle_mesh_ms", _particles.SlowestPrewarmMs));
        foreach (var source in _sources.ToArray())
        {
            Unregister(source);
        }

        _sharedModels = null;
        if (_ownsParticles)
        {
            _particles.Dispose();
        }
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private readonly List<int> _slots = new();

    /// <summary>
    /// The cell-state slot of each drawn static's cell (parallel to <see cref="LivingStaticSet.Drawn"/>), or null for a
    /// source outside cells. Looked up every frame: a cell gets its slot when its mesh attaches, after its statics register.
    /// </summary>
    private List<int>? CellSlots(Registration source)
    {
        if (source.CellSlot is not { } slotOf)
        {
            return null;
        }

        _slots.Clear();
        var cells = source.Set.DrawnCells;
        for (var i = 0; i < cells.Count; i++)
        {
            _slots.Add(slotOf(cells[i]));
        }

        return _slots;
    }

    /// <summary>
    /// The camera as the sound listener in a source's landblock space: its position, and its heading from the direction it looks along
    /// (the compass heading <c>fmod(450 - atan2(y, x), 360)</c>, <c>Frame::get_heading</c>); the cell is the landblock's.
    /// </summary>
    private static CellFrame ListenerFrame(Registration source, Camera3D camera, AcVector3 local)
    {
        var forward = AcToGodot.ToAc(-camera.GlobalTransform.Basis.Z); // the conversion lives in AcToGodot (M3 1.3 fix B, R5); the heading is then taken in AC space
        var heading = (float)((450.0 - (Math.Atan2(forward.Y, forward.X) * (180.0 / Math.PI))) % 360.0);
        return new CellFrame(source.Coord.LandBlockId, new AcFrame(local, PositionFrames.WithHeading(System.Numerics.Quaternion.Identity, heading)));
    }

    /// <summary>The camera in a source's landblock-local AC coordinates (its parent is an unrotated node at the landblock's corner).</summary>
    private static AcVector3 Local(Registration source, Vector3 eye) =>
        AcToGodot.ToAc(eye - (source.Parent.IsInsideTree() ? source.Parent.GlobalPosition : source.Parent.Position));

    /// <summary>One registered source of living statics.</summary>
    public sealed class Registration(LandblockCoord coord, Node3D parent, LivingStaticSet set, ParticleRenderer renderer, Func<uint, bool>? cellVisible, Action<LivingStaticSet, AcVector3>? moved,
        Func<uint, int>? cellSlot)
    {
        public LandblockCoord Coord { get; } = coord;

        public Node3D Parent { get; } = parent;

        public LivingStaticSet Set { get; } = set;

        public ParticleRenderer Renderer { get; } = renderer;

        public Func<uint, bool>? CellVisible { get; } = cellVisible;

        public Action<LivingStaticSet, AcVector3>? Moved { get; } = moved;

        /// <summary>For EnvCell statics: a cell's slot in the interiors' cell-state table.</summary>
        public Func<uint, int>? CellSlot { get; } = cellSlot;
    }
}
