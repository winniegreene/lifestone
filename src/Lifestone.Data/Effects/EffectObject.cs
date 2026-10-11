using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>Definitions an <see cref="EffectObject"/> needs (normally <see cref="DatEffectSource"/>).</summary>
public interface IEffectSource
{
    PhysicsScriptData? GetScript(uint scriptId);

    PhysicsScriptTableData? GetScriptTable(uint tableId);

    ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId);
}

/// <summary>One live particle as the renderer needs it (world space, AC coordinates).</summary>
/// <param name="EmitterId">Emitter id on its object (hook id, or an allocated id ≥ 0xFFFF0000).</param>
/// <param name="EmitterInfoId">ParticleEmitterInfo (0x32) id.</param>
/// <param name="GfxObjId">Hardware GfxObj drawn for the particle (<c>ParticleEmitterInfo.HwGfxObjId</c>).</param>
/// <param name="Slot">Particle slot within the emitter (0 .. MaxParticles − 1).</param>
/// <param name="Position">World position of the particle part.</param>
/// <param name="Orientation">
/// Part orientation. Only the GR/LR kernels rotate particles; the others keep identity, and the renderer applies the
/// GfxObj's degrade billboard mode (retail <c>CPhysicsPart::calc_draw_frame</c>, docs/research/rendering.md §6).
/// </param>
/// <param name="Scale">Uniform part scale.</param>
/// <param name="Translucency">0 = opaque … 1 = invisible (alpha = 1 − translucency; 1.0 is not drawn).</param>
public readonly record struct ParticleInstance(
    uint EmitterId, uint EmitterInfoId, uint GfxObjId, int Slot, Vector3 Position, Quaternion Orientation, float Scale, float Translucency);

/// <summary>A hook the effect layer does not act on itself (sound, attack, part swap, …), handed to the engine.</summary>
public readonly record struct EffectEvent(double Time, AnimHook Hook);

/// <summary>Object/part material overrides driven by Transparent/Luminous/Diffuse/Scale hooks (retail <c>process_fp_hook</c>).</summary>
public sealed class EffectVisualState
{
    internal readonly Dictionary<int, float> PartTranslucencyMap = [];
    internal readonly Dictionary<int, float> PartLuminosityMap = [];
    internal readonly Dictionary<int, float> PartDiffusionMap = [];
    internal readonly Dictionary<int, Vector2> PartTextureVelocityMap = [];

    /// <summary>Object scale (retail <c>m_scale</c>, <c>SetScaleStatic</c>).</summary>
    public float Scale { get; internal set; } = 1f;

    /// <summary>Whole-object translucency (retail <c>SetTranslucencyInternal</c>), never below <see cref="TranslucencyOriginal"/>.</summary>
    public float Translucency { get; internal set; }

    /// <summary>Retail <c>translucencyOriginal</c>: the object's base translucency (0 unless the server set one).</summary>
    public float TranslucencyOriginal { get; set; }

    /// <summary>Whole-object luminosity override, null = surface default (retail <c>CPartArray::SetLuminosityInternal</c>).</summary>
    public float? Luminosity { get; internal set; }

    /// <summary>Whole-object diffusion override, null = surface default.</summary>
    public float? Diffusion { get; internal set; }

    public IReadOnlyDictionary<int, float> PartTranslucency => PartTranslucencyMap;

    public IReadOnlyDictionary<int, float> PartLuminosity => PartLuminosityMap;

    public IReadOnlyDictionary<int, float> PartDiffusion => PartDiffusionMap;

    /// <summary>Texture scroll speeds (UV units per second) per part; key −1 = all parts (TextureVelocity hook).</summary>
    public IReadOnlyDictionary<int, Vector2> TextureVelocity => PartTextureVelocityMap;

    public bool NoDraw { get; internal set; }

    public bool LightsOn { get; internal set; } = true;

    public bool Ethereal { get; internal set; }

    /// <summary>Object angular velocity set by a SetOmega hook (retail <c>set_omega</c>).</summary>
    public Vector3 Omega { get; internal set; }
}

/// <summary>
/// The effect-related part of one retail <c>CPhysicsObj</c>: its PhysicsScriptTable, <see cref="ScriptManager"/>,
/// <see cref="ParticleManager"/>, interpolating "FP hooks", and the execution of animation/script hooks
/// (retail <c>CAnimHook::Execute</c> overrides, acclient 11.4186 PORTAL/canim/CAnimHook.c).
/// The caller owns time and placement: each <see cref="Update"/> receives the clock and the object/part world frames.
/// </summary>
/// <remarks>
/// Per-update order follows <c>CPhysicsObj::UpdateObjectInternal</c>: the animation step (done by the caller, which
/// queues its hooks with <see cref="QueueAnimHook"/>), then <c>process_hooks</c> (FP hooks, then queued animation
/// hooks), then <c>ParticleManager::UpdateParticles</c>, then <c>ScriptManager::UpdateScripts</c>.
/// The object is always "in a cell" (retail plays scripts only then).
/// </remarks>
public sealed class EffectObject : IEffectFrames
{
    private const double Epsilon = 0.00019999999;

    private readonly IEffectSource _source;
    private readonly List<AnimHook> _animHooks = [];
    private readonly List<FpHook> _fpHooks = [];
    private readonly List<EffectEvent> _events = [];
    private readonly List<string> _warnings = [];
    private AcFrame[] _partFrames = [];

    public EffectObject(IEffectSource source, IEffectRandom random, uint physicsScriptTableId = 0)
    {
        _source = source;
        Random = random;
        PhysicsScriptTableId = physicsScriptTableId;
    }

    /// <summary>Retail <c>FPHook</c>: interpolates a value from start to end over <c>time</c> seconds.</summary>
    private sealed record FpHook(int Type, double TimeCreated, double InterpolationTime, float Start, float End, int PartIndex, uint ScriptId);

    public IEffectRandom Random { get; }

    /// <summary>The object's PhysicsScriptTable (0x34), or 0.</summary>
    public uint PhysicsScriptTableId { get; set; }

    /// <summary>Retail <c>default_script</c> (PlayScript used by DefaultScript hooks), from the server's PhysicsDesc.</summary>
    public PlayScript DefaultScript { get; set; }

    /// <summary>Retail <c>default_script_intensity</c>.</summary>
    public float DefaultScriptIntensity { get; set; }

    /// <summary>
    /// Stand-in for <c>CPhysicsObj::ShouldDrawParticles(degrade_distance)</c>: false when the viewer is beyond the
    /// particle GfxObj's maximum degrade distance. Defaults to drawn.
    /// </summary>
    public bool ParticlesInDrawRange { get; set; } = true;

    public double CurrentTime { get; private set; }

    public AcFrame ObjectFrame { get; private set; } = AcFrame.Identity;

    public int PartCount => _partFrames.Length;

    public ScriptManager Scripts { get; } = new();

    public ParticleManager Particles { get; } = new();

    public EffectVisualState Visual { get; } = new();

    /// <summary>Hooks handed to the engine since the last <see cref="DrainEvents"/>.</summary>
    public IReadOnlyList<EffectEvent> PendingEvents => _events;

    /// <summary>Data/Setup mismatches met so far (e.g. a hook addressing a part the object does not have).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public AcFrame GetPartFrame(int index) => _partFrames[index];

    /// <summary>Sets the clock and placement without running an update (e.g. before playing a script at creation).</summary>
    public void SetPlacement(double now, AcFrame objectFrame, ReadOnlySpan<AcFrame> partFrames)
    {
        CurrentTime = now;
        ObjectFrame = objectFrame;
        if (_partFrames.Length != partFrames.Length)
        {
            _partFrames = new AcFrame[partFrames.Length];
        }

        partFrames.CopyTo(_partFrames);
    }

    /// <summary>Retail <c>CPhysicsObj::play_script(PScriptType, mod)</c>: resolves through the PhysicsScriptTable.</summary>
    /// <returns>The script id that was queued, or 0 when the table has no script for (type, mod).</returns>
    public uint PlayScript(PlayScript type, float intensity)
    {
        var table = PhysicsScriptTableId != 0 ? _source.GetScriptTable(PhysicsScriptTableId) : null;
        if (table is null)
        {
            return 0;
        }

        var scriptId = table.GetScript(type, intensity);
        return PlayScriptId(scriptId) ? scriptId : 0;
    }

    /// <summary>Retail <c>CPhysicsObj::play_script_internal(id)</c>.</summary>
    public bool PlayScriptId(uint scriptId)
    {
        if (scriptId == 0)
        {
            return false;
        }

        var script = _source.GetScript(scriptId);
        if (script is null)
        {
            _warnings.Add($"PhysicsScript 0x{scriptId:X8} does not exist.");
            return false;
        }

        Scripts.AddScript(script, CurrentTime);
        return true;
    }

    /// <summary>Retail <c>CPhysicsObj::play_default_script()</c>.</summary>
    public uint PlayDefaultScript() => PlayScript(DefaultScript, DefaultScriptIntensity);

    /// <summary>Retail <c>CPhysicsObj::add_anim_hook</c>: queues a hook from the animation sequence for <c>process_hooks</c>.</summary>
    public void QueueAnimHook(AnimHook hook) => _animHooks.Add(hook);

    /// <summary>
    /// One retail object update at <paramref name="now"/>. As in <c>CPhysicsObj::UpdateObjectInternal</c>,
    /// <c>process_hooks</c> runs at the end of <c>UpdatePositionInternal</c>, i.e. while the object still has its
    /// previous placement; then <c>set_frame</c> applies the new object/part frames (<paramref name="objectFrame"/>,
    /// <paramref name="partFrames"/>: world frames after this update's animation step); then particles, then scripts.
    /// </summary>
    public void Update(double now, AcFrame objectFrame, ReadOnlySpan<AcFrame> partFrames)
    {
        CurrentTime = now;
        ProcessHooks();
        SetPlacement(now, objectFrame, partFrames);
        Particles.UpdateParticles(now, this, Random, ParticlesInDrawRange, _warnings);
        Scripts.UpdateScripts(now, ExecuteHook);
    }

    // --- objects track (M1 wave 3, living statics; docs/objects.md §1.7): static objects step differently. ---

    /// <summary>Most data warnings kept per object; long-lived statics would otherwise grow the list every update.</summary>
    public const int MaxWarnings = 64;

    private Action<AnimHook>? _executeHook;

    /// <summary>
    /// The effect half of retail <c>CPhysicsObj::animate_static_object</c> (acclient 11.4186, acclient.c:321150), which
    /// <c>CPhysics::UseTime</c> runs for every static registered by <c>CPhysicsObj::InitDefaults</c> instead of
    /// <c>update_object</c>. The caller has already advanced the default animation (<c>CPartArray::Update</c>, queuing its
    /// hooks with <see cref="QueueAnimHook"/>) and posed the parts (<c>UpdatePartsInternal</c>); then, in retail order:
    /// the scripts (<c>ScriptManager::UpdateScripts</c>, only for objects with a default script, retail
    /// <c>HAS_DEFAULT_SCRIPT</c>), the particles (<c>ParticleManager::UpdateParticles</c>), and last
    /// <c>process_hooks</c> (FP hooks such as delayed CallPES, then the queued animation hooks). <see cref="Update"/>
    /// keeps <c>UpdateObjectInternal</c>'s order (hooks first) for moving objects.
    /// </summary>
    /// <param name="now">The clock (seconds).</param>
    /// <param name="objectFrame">The object frame after this step.</param>
    /// <param name="partFrames">The part frames after this step.</param>
    /// <param name="runScripts">Retail <c>HAS_DEFAULT_SCRIPT</c>: the Setup has a default script.</param>
    /// <param name="drawRange">
    /// Per emitter, retail <c>CPhysicsObj::ShouldDrawParticles(degrade_distance)</c> of its particle object
    /// (<c>ParticleEmitter::UpdateParticles</c>); null uses <see cref="ParticlesInDrawRange"/> for every emitter.
    /// </param>
    public void UpdateStatic(double now, AcFrame objectFrame, ReadOnlySpan<AcFrame> partFrames, bool runScripts, Func<ParticleEmitter, bool>? drawRange)
    {
        CurrentTime = now;
        SetPlacement(now, objectFrame, partFrames);
        if (runScripts)
        {
            // One cached delegate: statics step for as long as they exist, and a method group allocates on every call.
            Scripts.UpdateScripts(now, _executeHook ??= ExecuteHook);
        }

        var warnings = _warnings.Count < MaxWarnings ? _warnings : null;
        if (drawRange is null)
        {
            Particles.UpdateParticles(now, this, Random, ParticlesInDrawRange, warnings);
        }
        else
        {
            Particles.UpdateParticles(now, this, Random, drawRange, warnings);
        }

        ProcessHooks();
    }

    /// <summary>Returns and clears the pending engine events.</summary>
    public EffectEvent[] DrainEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    /// <summary>Appends the live particles of every emitter to <paramref name="output"/>.</summary>
    public void CollectParticles(List<ParticleInstance> output)
    {
        foreach (var emitter in Particles.Emitters)
        {
            if (emitter.DegradedOut)
            {
                continue;
            }

            var slots = emitter.Slots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] is { } p)
                {
                    output.Add(new ParticleInstance(
                        emitter.Id, emitter.Info.Id, emitter.Info.HwGfxObjId, i, p.Frame.Origin, p.Frame.Orientation, p.Scale, p.Translucency));
                }
            }
        }
    }

    /// <summary>Retail <c>CPhysicsObj::process_hooks</c>: FP hooks (newest first), then the queued animation hooks in order.</summary>
    private void ProcessHooks()
    {
        for (var i = _fpHooks.Count - 1; i >= 0; i--)
        {
            if (ExecuteFpHook(_fpHooks[i]))
            {
                _fpHooks.RemoveAt(i);
            }
        }

        if (_animHooks.Count == 0)
        {
            return;
        }

        var hooks = _animHooks.ToArray();
        _animHooks.Clear();
        foreach (var hook in hooks)
        {
            ExecuteHook(hook);
        }
    }

    /// <summary>Retail <c>FPHook::Execute</c> + <c>CPhysicsObj::process_fp_hook</c>. Returns true when finished.</summary>
    private bool ExecuteFpHook(FpHook hook)
    {
        var delta = CurrentTime - hook.TimeCreated;
        var scale = delta > 0.0 ? (delta < hook.InterpolationTime ? delta / hook.InterpolationTime : 1.0) : 0.0;
        var value = (float)((hook.End - hook.Start) * scale + hook.Start);
        switch (hook.Type)
        {
            case 0:
                Visual.Scale = value;
                break;
            case 1:
                // CPhysicsObj::SetTranslucencyInternal: never below translucencyOriginal.
                Visual.Translucency = Math.Max(value, Visual.TranslucencyOriginal);
                break;
            case 2:
                Visual.PartTranslucencyMap[hook.PartIndex] = value;
                break;
            case 3:
                Visual.Luminosity = value;
                break;
            case 4:
                Visual.Diffusion = value;
                break;
            case 5:
                Visual.PartLuminosityMap[hook.PartIndex] = value;
                break;
            case 6:
                Visual.PartDiffusionMap[hook.PartIndex] = value;
                break;
            case 7:
                // CPhysicsObj::CallPESInternal: plays the script once the value reaches 1.0.
                if (value >= 1f)
                {
                    PlayScriptId(hook.ScriptId);
                }

                break;
        }

        return scale == 1.0;
    }

    /// <summary>Executes one hook (retail <c>CAnimHook::Execute</c> dispatch).</summary>
    public void ExecuteHook(AnimHook hook)
    {
        switch (hook)
        {
            case CreateParticleHook h:
                Particles.CreateParticleEmitter(_source.GetEmitterInfo(h.EmitterInfoId), h.PartIndex, h.Offset, h.EmitterId, CurrentTime, this, Random, _warnings);
                break;
            case CreateBlockingParticleHook h:
                Particles.CreateBlockingParticleEmitter(_source.GetEmitterInfo(h.EmitterInfoId), h.PartIndex, h.Offset, h.EmitterId, CurrentTime, this, Random, _warnings);
                break;
            case DestroyParticleHook h:
                Particles.DestroyParticleEmitter(h.EmitterId);
                break;
            case StopParticleHook h:
                Particles.StopParticleEmitter(h.EmitterId);
                break;
            case CallPesHook h:
                CallPes(h.ScriptId, h.Pause);
                break;
            case DefaultScriptHook:
                PlayDefaultScript();
                break;
            case ScaleHook h:
                // CPhysicsObj::SetScale(end, time): interpolates from the current scale.
                AddFp(0, h.Time, Visual.Scale, h.End, 0, 0, immediate: () => Visual.Scale = h.End);
                break;
            case TransparentHook h:
                // CPhysicsObj::SetTranslucency2: an immediate change is clamped to the original translucency.
                AddFp(1, h.Time, h.Start, h.End, 0, 0, immediate: () => Visual.Translucency = Math.Max(h.End, Visual.TranslucencyOriginal));
                break;
            case TransparentPartHook h:
                AddFp(2, h.Time, h.Start, h.End, h.PartIndex, 0, immediate: () => Visual.PartTranslucencyMap[h.PartIndex] = h.End);
                break;
            case LuminousHook h:
                AddFp(3, h.Time, h.Start, h.End, 0, 0, immediate: () => Visual.Luminosity = h.End);
                break;
            case DiffuseHook h:
                AddFp(4, h.Time, h.Start, h.End, 0, 0, immediate: () => Visual.Diffusion = h.End);
                break;
            case LuminousPartHook h:
                AddFp(5, h.Time, h.Start, h.End, h.PartIndex, 0, immediate: () => Visual.PartLuminosityMap[h.PartIndex] = h.End);
                break;
            case DiffusePartHook h:
                AddFp(6, h.Time, h.Start, h.End, h.PartIndex, 0, immediate: () => Visual.PartDiffusionMap[h.PartIndex] = h.End);
                break;
            case NoDrawHook h:
                Visual.NoDraw = h.NoDraw;
                _events.Add(new EffectEvent(CurrentTime, hook));
                break;
            case SetLightHook h:
                Visual.LightsOn = h.LightsOn;
                _events.Add(new EffectEvent(CurrentTime, hook));
                break;
            case EtherealHook h:
                Visual.Ethereal = h.Ethereal;
                break;
            case SetOmegaHook h:
                Visual.Omega = h.Axis;
                break;
            case TextureVelocityHook h:
                Visual.PartTextureVelocityMap[-1] = new Vector2(h.USpeed, h.VSpeed);
                break;
            case TextureVelocityPartHook h:
                Visual.PartTextureVelocityMap[h.PartIndex] = new Vector2(h.USpeed, h.VSpeed);
                break;
            case AnimationDoneHook:
            case NoOpHook:
                // AnimationDone is the motion layer's (MotionPlayer feeds MotionTableManager); NOOPHook does nothing.
                break;
            default:
                // Sound, SoundTable, SoundTweaked, Attack, ReplaceObject, DefaultScriptPart: engine/game concerns.
                _events.Add(new EffectEvent(CurrentTime, hook));
                break;
        }
    }

    /// <summary>Retail <c>CPhysicsObj::CallPES</c>: a delay drawn in [0, pause], or immediately when pause &lt; ε.</summary>
    private void CallPes(uint scriptId, float pause)
    {
        if (pause >= Epsilon)
        {
            var delay = Random.RollDice(0f, pause);
            _fpHooks.Add(new FpHook(7, CurrentTime, delay, 0f, 1f, 0, scriptId));
        }
        else
        {
            PlayScriptId(scriptId);
        }
    }

    /// <summary>The <c>Set*(…, delta)</c> pattern: an FP hook when <c>delta ≥ ε</c>, else an immediate change.</summary>
    private void AddFp(int type, float time, float start, float end, int partIndex, uint scriptId, Action immediate)
    {
        if (time >= Epsilon)
        {
            _fpHooks.Add(new FpHook(type, CurrentTime, time, start, end, partIndex, scriptId));
        }
        else
        {
            immediate();
        }
    }
}
