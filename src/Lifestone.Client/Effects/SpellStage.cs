using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Effects;

/// <summary>The role of an actor on a <see cref="SpellStage"/>.</summary>
public enum StageRole
{
    Caster,
    Target,
    Projectile,
}

/// <summary>One drawable actor of the stage this frame.</summary>
/// <param name="Role">Caster, target or projectile.</param>
/// <param name="Visual">What it looks like (Setup, scale) and which tables drive it.</param>
/// <param name="Actor">Its simulated state (part frames, effects).</param>
/// <param name="Hidden">Its model is not drawn (an impacted projectile is NoDraw, ACE <c>ProjectileImpact</c>; a NoDraw hook);
/// its particles still are.</param>
/// <param name="Index">Projectile index within the cast (0 for the caster and the target).</param>
public readonly record struct StageActor(StageRole Role, VisualObject Visual, EffectActor Actor, bool Hidden, int Index);

/// <summary>A showcase entry (docs/spell-visuals.md §5).</summary>
/// <param name="SpellId">SpellTable id.</param>
/// <param name="Label">Button label.</param>
/// <param name="Fizzle">Cast it so that it fizzles (ACE: the words and gestures play, then <c>PlayScript.Fizzle</c> @ 0.5 on the caster).</param>
public sealed record ShowcaseEntry(uint SpellId, string Label, bool Fizzle = false);

/// <summary>
/// The spell viewer's scene logic, engine-agnostic: a caster and a target standing on the ground, idle in their motion
/// tables' default state, and the current <see cref="SpellCastSimulation"/>. The Godot viewer draws what it reports;
/// tests drive it without an engine.
/// </summary>
/// <remarks>
/// <para><b>Idle.</b> Before the first cast both actors play their MotionTable's default state (retail
/// <c>MotionTableManager::InitializeState</c>: the table's default style and its default substate, NonCombat Ready for
/// both). A cast replaces them with the simulation's own actors (the caster enters the Magic stance first), and they
/// stay after the cast ends.</para>
/// <para><b>Self-targeted spells</b> have no simulated target (their effects land on the caster); the target keeps its
/// idle actor so it remains on stage.</para>
/// <para><b>Fizzle</b> (<see cref="ResolveVisual"/>): ACE plays the whole cast sequence and, at the moment the spell would
/// take effect, broadcasts <c>PlayScript.Fizzle</c> at intensity 0.5 on the caster instead of any effect or projectile
/// (ACE @95ef426d <c>Player_Magic.cs</c> <c>DoCastSpell_Inner</c> with <c>CastingPreCheckStatus.CastFailed</c>).</para>
/// </remarks>
public sealed class SpellStage
{
    /// <summary>ACE broadcasts the fizzle script at intensity 0.5 (<c>GameMessageScript(Guid, PlayScript.Fizzle, 0.5f)</c>).</summary>
    public const float FizzleIntensity = 0.5f;

    /// <summary>The docs/spell-visuals.md §5 showcase, plus a fizzle.</summary>
    public static IReadOnlyList<ShowcaseEntry> Showcase { get; } =
    [
        new(27, "Flame Bolt I"),
        new(85, "Flame Bolt VI"),
        new(28, "Frost Bolt I"),
        new(75, "Lightning Bolt I"),
        new(1796, "Flame Streak I"),
        new(2739, "Flame Arc I"),
        new(1785, "Cassius' Ring of Fire"),
        new(6, "Heal Self I"),
        new(2, "Strength Self I"),
        new(24, "Armor Self I"),
        new(35, "Blood Drinker Self I"),
        new(25, "Imperil Other I"),
        new(7, "Harm Other I"),
        new(2645, "Portal Recall"),
        new(1635, "Lifestone Recall"),
        new(27, "Fizzle", Fizzle: true),
    ];

    private readonly SpellVisualResolver _resolver;
    private readonly EffectActor _idleCaster;
    private readonly EffectActor _idleTarget;
    private readonly List<StageActor> _actors = [];
    private int _casts;

    public SpellStage(SpellVisualResolver resolver, VisualObject caster, VisualObject target, AcFrame casterFrame, AcFrame targetFrame, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        _resolver = resolver;
        CasterVisual = caster;
        TargetVisual = target;
        CasterFrame = casterFrame;
        TargetFrame = targetFrame;
        Seed = seed;
        _idleCaster = CreateIdleActor(caster, casterFrame, seed);
        _idleTarget = CreateIdleActor(target, targetFrame, seed + 1);
    }

    public SpellVisualResolver Resolver => _resolver;

    public VisualObject CasterVisual { get; }

    public VisualObject TargetVisual { get; }

    public AcFrame CasterFrame { get; }

    public AcFrame TargetFrame { get; }

    public int Seed { get; }

    /// <summary>Stage clock in seconds (advanced by <see cref="Advance"/>).</summary>
    public double Time { get; private set; }

    /// <summary>The current cast, or null before the first one.</summary>
    public SpellCastSimulation? Simulation { get; private set; }

    /// <summary>The visual of the current cast.</summary>
    public SpellVisual? Visual => Simulation?.Visual;

    /// <summary>True when the current cast fizzles.</summary>
    public bool Fizzling { get; private set; }

    /// <summary>Stage time at which the current cast was started.</summary>
    public double CastTime { get; private set; }

    public EffectActor Caster => Simulation?.Caster ?? _idleCaster;

    /// <summary>The target actor on stage: the simulation's, or the idle one during a self-targeted cast.</summary>
    public EffectActor Target => Simulation?.Target ?? _idleTarget;

    /// <summary>
    /// The visual of a spell as the viewer casts it: <see cref="SpellVisualResolver.Resolve"/>, or its fizzle (no
    /// caster/target effect, no projectile; the caster plays <c>PlayScript.Fizzle</c> at <see cref="FizzleIntensity"/>).
    /// </summary>
    public SpellVisual ResolveVisual(uint spellId, bool fizzle = false)
    {
        var visual = _resolver.Resolve(spellId, CasterVisual, TargetVisual);
        if (!fizzle)
        {
            return visual;
        }

        return visual with
        {
            CasterEffect = visual.Fizzle,
            TargetEffect = null,
            Projectile = null,
            IsProjectileSpell = false,
            Plan = visual.Plan with { EffectIntensity = FizzleIntensity },
        };
    }

    /// <summary>Starts casting <paramref name="spellId"/> now (replacing any cast in progress).</summary>
    public SpellCastSimulation Cast(uint spellId, bool fizzle = false)
    {
        var visual = ResolveVisual(spellId, fizzle);
        _casts++;
        Fizzling = fizzle;
        CastTime = Time;
        Simulation = new SpellCastSimulation(_resolver, visual, CasterFrame, TargetFrame, Seed + (_casts * 7919), Time);
        return Simulation;
    }

    /// <summary>
    /// Plays <paramref name="spellId"/> for <paramref name="seconds"/> on a throw-away simulation (the stage is not
    /// touched), so the first real cast does not pay the one-time cost of the spell, motion and effect code paths
    /// (first-call JIT and table loads: about 17 ms on the first cast, under 1 ms after, measured).
    /// </summary>
    public void WarmUp(uint spellId = 85, double seconds = 4.0)
    {
        var sim = new SpellCastSimulation(_resolver, ResolveVisual(spellId), CasterFrame, TargetFrame, Seed - 1);
        var particles = new List<ParticleInstance>();
        for (var t = 0.0; t < seconds; t += 1.0 / 30.0)
        {
            sim.AdvanceTo(t);
            particles.Clear();
            sim.Caster.Effects.CollectParticles(particles);
        }
    }

    /// <summary>Back to the idle actors (no cast).</summary>
    public void Reset()
    {
        Simulation = null;
        Fizzling = false;
    }

    /// <summary>Advances the stage clock by <paramref name="seconds"/> (≥ 0) and every actor with it.</summary>
    public void Advance(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Time only moves forward.");
        }

        Time += seconds;
        if (Simulation is { } sim)
        {
            sim.AdvanceTo(Time);
            if (sim.Target is null)
            {
                _idleTarget.AdvanceTo(Time);
            }
        }
        else
        {
            _idleCaster.AdvanceTo(Time);
            _idleTarget.AdvanceTo(Time);
        }
    }

    /// <summary>Everything to draw this frame: caster, target, then projectiles in launch order (one list, refilled on each read).</summary>
    public IReadOnlyList<StageActor> Actors
    {
        get
        {
            _actors.Clear();
            _actors.Add(new StageActor(StageRole.Caster, CasterVisual, Caster, Caster.Effects.Visual.NoDraw, 0));
            _actors.Add(new StageActor(StageRole.Target, TargetVisual, Target, Target.Effects.Visual.NoDraw, 0));
            if (Simulation is { Visual.Projectile: { } projectile } sim)
            {
                var visual = new VisualObject(
                    projectile.Source.WeenieName ?? projectile.Source.SpellName,
                    projectile.Source.SetupId,
                    0,
                    projectile.Source.PhysicsEffectTableId ?? 0,
                    projectile.Source.DefaultScale ?? 1f);
                for (var i = 0; i < sim.Projectiles.Count; i++)
                {
                    var p = sim.Projectiles[i];
                    _actors.Add(new StageActor(StageRole.Projectile, visual, p.Actor, p.Hidden || p.Actor.Effects.Visual.NoDraw, i));
                }
            }

            return _actors;
        }
    }

    /// <summary>The effect objects whose particles are drawn (every actor, hidden ones included).</summary>
    public IEnumerable<EffectObject> EffectObjects => Actors.Select(a => a.Actor.Effects);

    /// <summary>The words of the current cast and when they were spoken (stage time), or null before they are.</summary>
    public (string Words, double Time)? SpokenWords
    {
        get
        {
            if (Simulation is not { } sim)
            {
                return null;
            }

            foreach (var e in sim.Events)
            {
                if (e.Text.Contains(" says \"", StringComparison.Ordinal))
                {
                    return (sim.Visual.Plan.Words, e.Time);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Object scale to draw an actor's model with: <see cref="EffectVisualState.Scale"/> once a Scale hook changed it
    /// (retail <c>CPhysicsObj::SetScale</c> sets <c>m_scale</c>), else the actor's own scale.
    /// </summary>
    /// <remarks>
    /// TODO(effects): <see cref="EffectVisualState.Scale"/> starts at 1 instead of the object's scale, so a Scale hook
    /// interpolates from 1 rather than from <c>m_scale</c> (frost bolts: DefaultScale 0.4, hook to 0.5). Once the effects
    /// layer seeds it with the object scale this becomes <c>Visual.Scale</c> alone.
    /// </remarks>
    public static float DrawScale(EffectActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var scale = actor.Effects.Visual.Scale;
        return scale != 1f ? scale : actor.Scale;
    }

    /// <summary>An actor that only plays its motion table's default state (no effects running).</summary>
    private EffectActor CreateIdleActor(VisualObject visual, AcFrame frame, int seed)
    {
        var table = _resolver.Animations.GetMotionTable(visual.MotionTableId)
            ?? throw new KeyNotFoundException($"MotionTable 0x{visual.MotionTableId:X8} of '{visual.Name}' not found.");
        var size = ObjectDimensions.FromSetup(_resolver.Animations.Dats, visual.SetupId, visual.Scale);
        var effects = new EffectObject(_resolver.Effects, new RetailRandom(seed), visual.PhysicsScriptTableId);
        return new EffectActor(effects, new MotionPlayer(table, _resolver.Animations), size.PartCount, frame, visual.Scale, Time);
    }

    /// <summary>
    /// Frames for a caster and a target <paramref name="distance"/> metres apart on the ground, facing each other
    /// (<see cref="EffectActor.SetVectorHeading"/>; identity faces +Y). <paramref name="groundHeight"/> gives the
    /// terrain height at an (x, y).
    /// </summary>
    public static (AcFrame Caster, AcFrame Target) FacingFrames(Vector2 casterXy, float headingDegrees, float distance, Func<float, float, float> groundHeight)
    {
        ArgumentNullException.ThrowIfNull(groundHeight);
        var radians = headingDegrees * MathF.PI / 180f;
        var dir = new Vector2(MathF.Sin(radians), MathF.Cos(radians));
        var targetXy = casterXy + (dir * distance);
        var caster = new Vector3(casterXy.X, casterXy.Y, groundHeight(casterXy.X, casterXy.Y));
        var target = new Vector3(targetXy.X, targetXy.Y, groundHeight(targetXy.X, targetXy.Y));
        var flat = new Vector3(dir.X, dir.Y, 0f);
        return (
            EffectActor.SetVectorHeading(new AcFrame(caster, Quaternion.Identity), flat),
            EffectActor.SetVectorHeading(new AcFrame(target, Quaternion.Identity), -flat));
    }

    /// <summary>Spells sorted by id, for the viewer's list: (id, name, school).</summary>
    public IEnumerable<(uint Id, string Name, MagicSchool School)> AllSpells =>
        _resolver.Spells.Spells.Values.OrderBy(s => s.Id).Select(s => (s.Id, s.Name, s.School));
}
