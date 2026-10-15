using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;

namespace Lifestone.Client.Objects;

/// <summary>
/// The dat-backed definitions living statics need, shared and thread-safe: PhysicsScripts, ParticleEmitterInfos, Animations,
/// MotionTables, and which particle GfxObjs a default script can create (<see cref="ParticleGfxObjsOf"/>).
/// </summary>
public sealed class LivingStaticSources(IEffectSource effects, DatAnimationSource animations)
{
    private readonly ConcurrentDictionary<uint, uint[]> _particleGfxObjs = new();

    public IEffectSource Effects { get; } = effects ?? throw new ArgumentNullException(nameof(effects));

    public DatAnimationSource Animations { get; } = animations ?? throw new ArgumentNullException(nameof(animations));

    /// <summary>
    /// Any thread: the hardware GfxObjs of every emitter a PhysicsScript can create, following CallPES hooks (cached). The
    /// renderer prepares their meshes before the first particle needs them.
    /// </summary>
    public IReadOnlyList<uint> ParticleGfxObjsOf(uint scriptId) => _particleGfxObjs.GetOrAdd(scriptId, id =>
    {
        var found = new HashSet<uint>();
        Collect(id, found, new HashSet<uint>());
        return [.. found];
    });

    /// <summary>
    /// Any thread: how far from its object's origin any part of <paramref name="model"/> can be (unscaled), over its rest
    /// pose and every frame of the animations its default animation or default motion table can play (cached). Animated
    /// parts are drawn with culling bounds of that radius around the object's origin; a SetOmega turn keeps it.
    /// </summary>
    public float PoseReach(ObjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _reach.GetOrAdd(model.Id, _ =>
        {
            var reach = 0f;
            foreach (var part in model.Parts)
            {
                reach = MathF.Max(reach, part.RestOrigin.Length());
            }

            var animations = new HashSet<uint>();
            if (model.DefaultAnimationId != 0)
            {
                animations.Add(model.DefaultAnimationId);
            }

            if (model.DefaultMotionTableId != 0 && Animations.GetMotionTable(model.DefaultMotionTableId) is { } table)
            {
                foreach (var motion in table.Cycles.Values.Concat(table.Modifiers.Values).Concat(table.Links.Values.SelectMany(l => l.Values)))
                {
                    foreach (var anim in motion.Anims)
                    {
                        animations.Add(anim.AnimId);
                    }
                }
            }

            foreach (var id in animations)
            {
                foreach (var frame in Animations.GetAnimation(id)?.PartFrames ?? [])
                {
                    foreach (var f in frame.Frames)
                    {
                        reach = MathF.Max(reach, f.Origin.Length());
                    }
                }
            }

            return reach;
        });
    }

    private readonly ConcurrentDictionary<uint, float> _reach = new();
    private readonly ConcurrentDictionary<uint, string[]> _undrawn = new();

    /// <summary>
    /// Any thread: the hook types a model's default script (following CallPES) and default animation use that M1 executes
    /// but does not draw or play (texture scrolling, part material fades, scale, NoDraw, lights, part swaps; sounds are
    /// dropped on purpose and not listed), so the renderer can name each such Setup once (docs/objects.md §4). Cached.
    /// </summary>
    public IReadOnlyList<string> UndrawnHooks(ObjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _undrawn.GetOrAdd(model.Id, _ =>
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<uint>();
            void Script(uint id)
            {
                if (!visited.Add(id) || Effects.GetScript(id) is not { } script)
                {
                    return;
                }

                foreach (var entry in script.Hooks)
                {
                    Note(entry.Hook);
                    if (entry.Hook is CallPesHook call)
                    {
                        Script(call.ScriptId);
                    }
                }
            }

            void Note(AnimHook hook)
            {
                if (hook is TextureVelocityHook or TextureVelocityPartHook or TransparentHook or TransparentPartHook or LuminousHook or LuminousPartHook
                    or DiffuseHook or DiffusePartHook or ScaleHook or NoDrawHook or SetLightHook or ReplaceObjectHook)
                {
                    found.Add(hook.GetType().Name);
                }
            }

            if (model.DefaultScriptId != 0)
            {
                Script(model.DefaultScriptId);
            }

            if (model.DefaultAnimationId != 0 && Animations.GetAnimation(model.DefaultAnimationId) is { } anim)
            {
                foreach (var frame in anim.PartFrames)
                {
                    foreach (var hook in frame.Hooks)
                    {
                        Note(hook);
                        if (hook is CallPesHook call)
                        {
                            Script(call.ScriptId);
                        }
                    }
                }
            }

            return [.. found];
        });
    }

    /// <summary>The hardware GfxObjs of the CreateParticle hooks in an animation (default animations can create emitters).</summary>
    public IEnumerable<uint> ParticleGfxObjsOfAnimation(uint animationId)
    {
        if (Animations.GetAnimation(animationId) is not { } anim)
        {
            yield break;
        }

        foreach (var frame in anim.PartFrames)
        {
            foreach (var hook in frame.Hooks)
            {
                if (EmitterOf(hook) is { } info && Effects.GetEmitterInfo(info) is { HwGfxObjId: var hw and not 0 })
                {
                    yield return hw;
                }
                else if (hook is CallPesHook call)
                {
                    foreach (var g in ParticleGfxObjsOf(call.ScriptId))
                    {
                        yield return g;
                    }
                }
            }
        }
    }

    private void Collect(uint scriptId, HashSet<uint> found, HashSet<uint> visited)
    {
        if (!visited.Add(scriptId) || Effects.GetScript(scriptId) is not { } script)
        {
            return;
        }

        foreach (var entry in script.Hooks)
        {
            if (EmitterOf(entry.Hook) is { } info)
            {
                if (Effects.GetEmitterInfo(info) is { HwGfxObjId: var hw and not 0 })
                {
                    found.Add(hw);
                }
            }
            else if (entry.Hook is CallPesHook call)
            {
                Collect(call.ScriptId, found, visited);
            }
        }
    }

    private static uint? EmitterOf(AnimHook hook) => hook switch
    {
        CreateParticleHook h => h.EmitterInfoId,
        CreateBlockingParticleHook h => h.EmitterInfoId,
        _ => null,
    };
}

/// <summary>
/// One static object that retail animates every physics tick: a Setup with a default PhysicsScript (torch flames, chimney
/// smoke, braziers) and/or a default animation (birds circling, flags, windmill sails). Engine-agnostic; landblock-local
/// AC coordinates; the clock is injected (docs/objects.md §1.7).
/// </summary>
/// <remarks>
/// <para><b>Creation</b> re-expresses <c>CPhysicsObj::makeObject(id, 0, bDynamic = 0)</c> (acclient 11.4186):
/// <c>InitObjectBegin</c> marks the object STATIC and sets <c>update_time</c> to now; <c>CPartArray::InitDefaults</c>
/// appends the Setup's default animation (frames 0 to the last, 30 fps); <c>CPhysicsObj::InitDefaults</c> plays the default
/// script (<c>play_script_internal</c>), installs the default motion table (<c>SetMotionTableID</c> →
/// <c>MakeMovementManager(1)</c> → <c>EnterDefaultState</c> → <c>InitializeMotionTables</c>) and the default
/// PhysicsScriptTable, and, for a STATIC object with a default animation or script, sets <c>HAS_DEFAULT_ANIM</c> /
/// <c>HAS_DEFAULT_SCRIPT</c> and registers it with <c>CPhysics::AddStaticAnimatingObject</c>.</para>
/// <para><b>Stepping</b> (<see cref="Animate"/>) re-expresses <c>CPhysicsObj::animate_static_object</c> (acclient.c:321150),
/// which <c>CPhysics::UseTime</c> calls for every registered static each frame: with <c>dt = now − update_time</c>,
/// nothing happens while <c>ε &lt; dt &lt; 1/30 s</c> (<c>MIN_QUANTUM</c>; time accumulates), an elapsed time of at most ε or
/// more than 2 s only moves <c>update_time</c>, and otherwise ONE step of <c>dt</c>: with a default animation the part
/// array advances (<c>CPartArray::Update</c>), the object frame turns by the omega a SetOmega hook set
/// (<c>Frame::grotate(frame, omega)</c>, once per step, not scaled by dt) and the parts are posed
/// (<c>UpdatePartsInternal</c>); with a default script the scripts run; then the particles and <c>process_hooks</c>
/// (<see cref="EffectObject.UpdateStatic"/>). On a fast machine statics therefore step at about 30 Hz, as in retail.</para>
/// </remarks>
public sealed class LivingStatic
{
    /// <summary>Retail <c>MIN_QUANTUM</c> for statics (<c>animate_static_object</c>): 1/30 s.</summary>
    public const double MinQuantum = 1.0 / 30.0;

    /// <summary>Elapsed times above this only move the clock (<c>animate_static_object</c>).</summary>
    public const double MaxQuantum = 2.0;

    /// <summary>Retail <c>F_EPSILON</c> as a time.</summary>
    public const double Epsilon = 0.00019999999;

    private readonly AcFrame[] _restFrames;
    private readonly AcFrame[] _partFrames;
    private readonly IPoseDriver? _pose;

    internal LivingStatic(int objectIndex, in WorldObject obj, ObjectModel model, LivingStaticSources sources, int seed, double now)
    {
        ObjectIndex = objectIndex;
        Object = obj;
        Model = model;
        Scale = obj.Scale;
        Frame = new AcFrame(obj.Origin, obj.Orientation);
        HasDefaultScript = model.DefaultScriptId != 0;
        HasDefaultAnimation = model.DefaultAnimationId != 0;
        var count = model.Parts.Count;
        _restFrames = new AcFrame[count];
        _partFrames = new AcFrame[count];
        for (var i = 0; i < count; i++)
        {
            _restFrames[i] = ObjectPlacement.RestFrame(model.Parts[i]);
        }

        if (model.DefaultMotionTableId != 0 && sources.Animations.GetMotionTable(model.DefaultMotionTableId) is { } table)
        {
            // CPhysicsObj::InitDefaults → SetMotionTableID → MakeMovementManager(1) → CMotionInterp::enter_default_state →
            // InitializeMotionTables: the motion table's default state replaces the sequence (the default animation's
            // loop included). TODO(objects): enter_default_state also queues Ready and calls LeaveGround; for statics
            // (no weenie, not on a walkable) the initial state is assumed to be what shows. 6 statics in the sampled
            // dungeons have a default motion table (docs/objects.md §1.7).
            _pose = new MotionPlayer(table, sources.Animations);
            UsesMotionTable = true;
        }
        else if (HasDefaultAnimation)
        {
            _pose = new DefaultAnimationPlayer(sources.Animations, model.DefaultAnimationId);
        }

        UpdateTime = now;
        Effects = new EffectObject(sources.Effects, new RetailRandom(seed), model.DefaultScriptTableId);
        PoseParts();
        Effects.SetPlacement(now, Frame, _partFrames);
        if (HasDefaultScript)
        {
            Effects.PlayScriptId(model.DefaultScriptId);
        }
    }

    /// <summary>Index of the object in its <see cref="LandblockObjectSet"/>.</summary>
    public int ObjectIndex { get; }

    public WorldObject Object { get; }

    public ObjectModel Model { get; }

    /// <summary>The object's effects (scripts, emitters, particles).</summary>
    public EffectObject Effects { get; }

    /// <summary>Retail <c>HAS_DEFAULT_SCRIPT</c>.</summary>
    public bool HasDefaultScript { get; }

    /// <summary>Retail <c>HAS_DEFAULT_ANIM</c>: the part array advances each step.</summary>
    public bool HasDefaultAnimation { get; }

    /// <summary>The pose comes from the Setup's default motion table (<see cref="MotionPlayer"/>).</summary>
    public bool UsesMotionTable { get; }

    /// <summary>The parts' frames differ from the rest pose (an animation or motion table poses them).</summary>
    public bool HasPose => _pose is not null;

    /// <summary>Object scale.</summary>
    public float Scale { get; }

    /// <summary>The object frame (landblock-local AC); SetOmega hooks turn it.</summary>
    public AcFrame Frame { get; private set; }

    /// <summary>Retail <c>update_time</c>.</summary>
    public double UpdateTime { get; private set; }

    /// <summary>Steps taken (statistics).</summary>
    public long Steps { get; private set; }

    /// <summary>The parts' current frames (landblock-local AC).</summary>
    public ReadOnlySpan<AcFrame> PartFrames => _partFrames;

    /// <summary>
    /// Retail <c>CPhysicsObj::animate_static_object</c> at <paramref name="now"/>. Returns true when the parts moved
    /// (a step with a default animation).
    /// </summary>
    /// <param name="now">The clock (seconds).</param>
    /// <param name="drawRange">Per emitter, whether its particles are in draw range (<c>ShouldDrawParticles</c>).</param>
    public bool Animate(double now, Func<ParticleEmitter, bool> drawRange)
    {
        var dt = now - UpdateTime;
        if (dt <= Epsilon)
        {
            UpdateTime = now;
            return false;
        }

        if (dt < MinQuantum)
        {
            return false; // accumulates until a step is due
        }

        if (dt > MaxQuantum)
        {
            UpdateTime = now;
            return false;
        }

        var moved = false;
        if (HasDefaultAnimation && _pose is not null)
        {
            // CPartArray::Update(quantum, null): the sequence advances (root motion is not applied to statics) and its
            // hooks are queued for process_hooks; then Frame::grotate by the object's omega, then UpdatePartsInternal.
            var update = _pose.Update(dt);
            var hooks = update.Hooks;
            for (var h = 0; h < hooks.Count; h++)
            {
                Effects.QueueAnimHook(hooks[h]);
            }

            Frame = Frame.GRotate(Effects.Visual.Omega);
            PoseParts();
            moved = true;
        }

        Effects.UpdateStatic(now, Frame, _partFrames, HasDefaultScript, drawRange);
        if (Effects.PendingEvents.Count > 0)
        {
            // Sound, SoundTweaked, SetLight and NoDraw hooks are handed to the engine as events. M1 has no audio and no
            // dynamic lights (docs/objects.md §4); nothing else would drain them, and a looping script would grow the list.
            EventsDropped += Effects.DiscardEvents();
        }

        UpdateTime = now;
        Steps++;
        return moved;
    }

    /// <summary>Engine events (sounds, lights, NoDraw) this static's hooks raised and M1 does not act on.</summary>
    public long EventsDropped { get; private set; }

    /// <summary><c>CPartArray::UpdateParts</c>: the current pose (or the rest pose) under the object frame and scale.</summary>
    private void PoseParts()
    {
        var pose = _pose?.Pose;
        var scale = new Vector3(Scale);
        for (var i = 0; i < _partFrames.Length; i++)
        {
            // UpdateParts only updates min(num_parts, frame parts); the rest keep their (rest) frames.
            var local = pose is not null && i < pose.Count ? pose[i] : _restFrames[i];
            _partFrames[i] = AcFrame.Combine(Frame, local, scale);
        }
    }
}

/// <summary>Which objects of a landblock's set are living statics, worked out on a worker (dat records warmed into the caches).</summary>
/// <param name="Set">The object set.</param>
/// <param name="ObjectIndices">The objects that animate or carry a default script or motion table.</param>
/// <param name="ParticleGfxObjs">Every particle GfxObj their scripts and animations can create (to prepare meshes for).</param>
/// <param name="Reach">
/// Parallel to <paramref name="ObjectIndices"/>: for objects whose parts move or are posed (a default animation or motion
/// table), how far from the object's origin a part's origin can be, object scale applied (<see cref="LivingStaticSources.PoseReach"/>);
/// −1 for objects that only run a script.
/// </param>
public sealed record LivingStaticPlan(LandblockObjectSet Set, int[] ObjectIndices, uint[] ParticleGfxObjs, float[] Reach)
{
    /// <summary>
    /// The reach of object <paramref name="objectIndex"/>'s moving parts (see <see cref="Reach"/>), or null when its parts
    /// stay at their rest pose.
    /// </summary>
    public float? ReachOf(int objectIndex)
    {
        var i = Array.BinarySearch(ObjectIndices, objectIndex);
        return i >= 0 && Reach[i] >= 0f ? Reach[i] : null;
    }

    /// <summary>
    /// Any thread: the living statics among <paramref name="set"/>'s objects, or null when there are none. Decodes (and so
    /// caches) their scripts, emitters, animations and motion tables, so creating them on the main thread costs little.
    /// </summary>
    public static LivingStaticPlan? Create(LandblockObjectSet set, ObjectModelLibrary library, LivingStaticSources sources)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sources);
        List<int>? indices = null;
        List<float>? reach = null;
        var gfxObjs = new HashSet<uint>();
        for (var o = 0; o < set.Objects.Count; o++)
        {
            var model = library.GetModel(set.Objects[o].ModelId);
            if (!model.IsLoadable || (model.DefaultScriptId == 0 && model.DefaultAnimationId == 0 && model.DefaultMotionTableId == 0))
            {
                continue;
            }

            (indices ??= []).Add(o);
            var posed = model.DefaultAnimationId != 0 || model.DefaultMotionTableId != 0;
            (reach ??= []).Add(posed ? sources.PoseReach(model) * set.Objects[o].Scale : -1f);
            if (model.DefaultScriptId != 0)
            {
                gfxObjs.UnionWith(sources.ParticleGfxObjsOf(model.DefaultScriptId));
            }

            if (model.DefaultAnimationId != 0)
            {
                gfxObjs.UnionWith(sources.ParticleGfxObjsOfAnimation(model.DefaultAnimationId));
            }

            if (model.DefaultMotionTableId != 0)
            {
                _ = sources.Animations.GetMotionTable(model.DefaultMotionTableId);
            }

            _ = sources.UndrawnHooks(model);
        }

        return indices is null ? null : new LivingStaticPlan(set, [.. indices], [.. gfxObjs], [.. reach!]);
    }
}

/// <summary>Per-frame counters of a <see cref="LivingStaticSet"/>.</summary>
public readonly record struct LivingStaticStats(int Statics, int Stepped, int Emitters, int LiveParticles, int DegradedEmitters);

/// <summary>
/// The living statics of one landblock (or one landblock's EnvCells): created when the landblock's objects are, stepped
/// each frame within a time budget, destroyed with them (retail <c>CPhysicsObj::Destroy</c> →
/// <c>CPhysics::RemoveStaticAnimatingObject</c>). Engine-agnostic.
/// </summary>
/// <remarks>
/// <para><b>Which statics run.</b> Retail animates every static of every created object (the 3×3 landblocks around the
/// viewer, and the EnvCells it has loaded) whatever the distance; what distance decides is whether an emitter's particles
/// are simulated and drawn: <c>ParticleEmitter::UpdateParticles</c> asks <c>CPhysicsObj::ShouldDrawParticles(degrade_distance)</c>
/// of the emitter's particle object, false when its viewer distance <c>CYpt</c> exceeds the particle GfxObj's maximum degrade
/// distance (<c>CPhysicsPart::GetMaxDegradeDistance</c>: 64-128 m for the sprites statics use, 100 m without a table), and
/// then the emitter is "degraded out": finite emitters lose their particles, endless ones freeze them until the viewer comes
/// back. <c>CYpt</c> is set by the draw pass (<c>RenderDeviceD3D::UpdateObjCell</c>): the horizontal distance to the land
/// cell's centre when that is over 50 m, else the 3-D distance to the particle object (the emitter position); EnvCells
/// always take the 3-D distance. TODO(objects): <c>ShouldDrawParticles</c> also asks a virtual of the object's cell whose
/// name the decompile lost; it is taken to be "the cell is drawn" (outdoors: the landblock is active; indoors: the cell is
/// in the portal view), which is also what keeps <c>CYpt</c> current in retail.</para>
/// <para><b>Budget.</b> Statics step round-robin until <c>budget</c> runs out; one left over steps on a later frame with the
/// longer elapsed time, exactly as retail steps at a lower frame rate (<see cref="LivingStatic.Animate"/>).</para>
/// </remarks>
public sealed class LivingStaticSet
{
    private readonly LivingStatic[] _statics;
    private readonly IParticleModelSource _models;
    private readonly Func<ParticleEmitter, bool> _drawRange;
    private readonly List<int> _moved = [];
    private readonly List<EffectObject> _drawn = [];
    private readonly List<uint> _drawnCells = [];
    private int _cursor;
    private bool _initialPosesReported;
    private LivingStatic? _current;
    private Vector3 _viewer;
    private bool _currentVisible;
    private float? _currentCellDistance;

    /// <summary>Main thread: creates the living statics of a plan (retail object creation; see <see cref="LivingStatic"/>).</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="library">Resolved models.</param>
    /// <param name="sources">Scripts, emitters, animations.</param>
    /// <param name="models">Particle GfxObj degrade distances (<see cref="ParticleModel.MaxDegradeDistance"/>).</param>
    /// <param name="now">The clock (seconds).</param>
    /// <param name="seed">Random seed base (each object adds its index: retail seeds one global generator with the time).</param>
    public LivingStaticSet(LivingStaticPlan plan, ObjectModelLibrary library, LivingStaticSources sources, IParticleModelSource models, double now, int seed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sources);
        Plan = plan;
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _drawRange = DrawRange;
        var objects = plan.Set.Objects;
        _statics = new LivingStatic[plan.ObjectIndices.Length];
        for (var i = 0; i < _statics.Length; i++)
        {
            var index = plan.ObjectIndices[i];
            var obj = objects[index];
            _statics[i] = new LivingStatic(index, obj, library.GetModel(obj.ModelId), sources, unchecked(seed + (index * 7919)), now);
        }
    }

    public LivingStaticPlan Plan { get; }

    public LandblockObjectSet Set => Plan.Set;

    public IReadOnlyList<LivingStatic> Statics => _statics;

    /// <summary>Statics whose parts moved during the last <see cref="Advance"/> (indices into <see cref="Statics"/>).</summary>
    public IReadOnlyList<int> Moved => _moved;

    /// <summary>The effect objects with particles to draw after the last <see cref="Advance"/> (visible statics only).</summary>
    public IReadOnlyList<EffectObject> Drawn => _drawn;

    /// <summary>
    /// Parallel to <see cref="Drawn"/>: each drawn static's cell (<see cref="WorldObject.CellId"/>). The renderer gives the
    /// particles of EnvCell statics their cell's draw state (front depth, doorway clipping), as the cell's own geometry has.
    /// </summary>
    public IReadOnlyList<uint> DrawnCells => _drawnCells;

    /// <summary>Counters of the last <see cref="Advance"/>.</summary>
    public LivingStaticStats Stats { get; private set; }

    /// <summary>
    /// Steps the statics (round-robin from where the last call stopped) until <paramref name="deadline"/>
    /// (<see cref="Stopwatch.GetTimestamp"/> units; <see cref="long.MaxValue"/> for no budget), and lists the effect objects
    /// to draw.
    /// </summary>
    /// <param name="now">The clock (seconds).</param>
    /// <param name="viewer">The viewer, landblock-local AC.</param>
    /// <param name="cellVisible">For EnvCell statics, whether their cell is drawn; null treats every object as drawn.</param>
    /// <param name="deadline">When to stop stepping.</param>
    /// <returns>True when every static got its turn.</returns>
    public bool Advance(double now, Vector3 viewer, Func<uint, bool>? cellVisible, long deadline)
    {
        _moved.Clear();
        _drawn.Clear();
        _drawnCells.Clear();
        _viewer = viewer;
        if (!_initialPosesReported)
        {
            // Posed statics (a default animation or motion table) differ from the rest pose from creation on.
            _initialPosesReported = true;
            for (var i = 0; i < _statics.Length; i++)
            {
                if (_statics[i].HasPose)
                {
                    _moved.Add(i);
                }
            }
        }

        var stepped = 0;
        var complete = true;
        for (var n = 0; n < _statics.Length; n++)
        {
            if ((n & 7) == 0 && n > 0 && Stopwatch.GetTimestamp() > deadline)
            {
                complete = false;
                break;
            }

            var i = (_cursor + n) % _statics.Length;
            var s = _statics[i];
            Prepare(s, cellVisible);
            if (s.Animate(now, _drawRange))
            {
                _moved.Add(i);
            }

            stepped++;
        }

        _cursor = complete ? 0 : (_cursor + stepped) % Math.Max(1, _statics.Length);
        var emitters = 0;
        var live = 0;
        var degraded = 0;
        foreach (var s in _statics)
        {
            var count = s.Effects.Particles.Count;
            if (count == 0)
            {
                continue;
            }

            emitters += count;
            var list = s.Effects.Particles.Emitters;
            for (var e = 0; e < list.Count; e++)
            {
                // Indexed: a foreach over the interface would allocate an enumerator per static per frame.
                var emitter = list[e];
                if (emitter.DegradedOut)
                {
                    degraded++;
                }
                else
                {
                    live += emitter.NumParticles;
                }
            }

            if (cellVisible is null || !s.Object.InEnvCell || cellVisible(s.Object.CellId))
            {
                _drawn.Add(s.Effects);
                _drawnCells.Add(s.Object.CellId);
            }
        }

        Stats = new LivingStaticStats(_statics.Length, stepped, emitters, live, degraded);
        _current = null;
        return complete;
    }

    /// <summary>Sets up the draw-range test for one static (its cell, and the far-cell rule's distance).</summary>
    private void Prepare(LivingStatic s, Func<uint, bool>? cellVisible)
    {
        _current = s;
        var obj = s.Object;
        _currentVisible = cellVisible is null || !obj.InEnvCell || cellVisible(obj.CellId);
        _currentCellDistance = null;
        if (!obj.InEnvCell)
        {
            // RenderDeviceD3D::UpdateObjCell: beyond 50 m from the land cell's centre (horizontally) every object in the
            // cell, its particle objects included, takes that distance.
            var dx = obj.CellCenter.X - _viewer.X;
            var dy = obj.CellCenter.Y - _viewer.Y;
            var d = MathF.Sqrt((dx * dx) + (dy * dy));
            if (d > LandblockObjectLod.CellDistance)
            {
                _currentCellDistance = d;
            }
        }
    }

    /// <summary>Retail <c>CPhysicsObj::ShouldDrawParticles(degrade_distance)</c> of an emitter's particle object.</summary>
    private bool DrawRange(ParticleEmitter emitter)
    {
        var s = _current;
        if (s is null || !_currentVisible)
        {
            return false;
        }

        var degrade = _models.Get(emitter.Info.HwGfxObjId)?.MaxDegradeDistance ?? DegradeSelector.DefaultMaxDegradeDistance;
        var cypt = _currentCellDistance ?? Vector3.Distance(_viewer, ParticleDrawList.EmitterPosition(s.Effects, emitter));
        return cypt <= degrade;
    }
}
