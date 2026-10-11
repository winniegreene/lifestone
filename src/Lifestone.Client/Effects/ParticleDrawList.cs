using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;

namespace Lifestone.Client.Effects;

/// <summary>What the particle renderer needs to know about one particle GfxObj (a ParticleEmitterInfo's hardware GfxObj).</summary>
/// <param name="GfxObjId">The emitter's hardware GfxObj (the part the particle is).</param>
/// <param name="Chain">Its LOD chain (<see cref="LodChain.Load"/>): particle GfxObjs carry degrade tables whose level 0 is
/// a billboard (mode 2) up to 64 or 128 m.</param>
/// <param name="SortCenter">Sort centre of the level-0 GfxObj (<c>CPhysicsPart::UpdateViewerDistance</c> measures to it).</param>
/// <param name="PartMaterialSwitchesBlend">True when a part material changes how some surface of the GfxObj blends
/// (<see cref="PartTranslucencyRules.WithPartMaterial"/>: a surface that does not already blend without an alpha test).
/// Only then do translucent particles need their own batch; otherwise they share the default one and only their
/// per-instance alpha differs.</param>
public sealed record ParticleModel(uint GfxObjId, LodChain Chain, Vector3 SortCenter, bool PartMaterialSwitchesBlend = true)
{
    /// <summary><c>CPhysicsPart::GetMaxDegradeDistance</c> of the first particle part (<c>ParticleEmitter::SetInfo</c>).</summary>
    public float MaxDegradeDistance => Chain.MaxDegradeDistance;
}

/// <summary>Provides <see cref="ParticleModel"/>s (cached; null when the GfxObj cannot be drawn).</summary>
public interface IParticleModelSource
{
    ParticleModel? Get(uint gfxObjId);
}

/// <summary>A batch of particles drawn with the same GfxObj (LOD level) and the same material state.</summary>
/// <param name="GfxObjId">The GfxObj of the selected level (what is drawn).</param>
/// <param name="PartMaterial">The particles carry a translucency material that switches some surface's blend state
/// (<see cref="PartTranslucencyRules.WithPartMaterial"/>, <see cref="ParticleModel.PartMaterialSwitchesBlend"/>).</param>
public readonly record struct ParticleBatchKey(uint GfxObjId, bool PartMaterial);

/// <summary>One drawn particle: its draw frame (after billboarding), uniform scale and material alpha.</summary>
/// <param name="Origin">World position of the particle part (AC).</param>
/// <param name="Orientation">Draw orientation (<c>CPhysicsPart::calc_draw_frame</c>).</param>
/// <param name="Scale">Uniform <c>gfxobj_scale</c>.</param>
/// <param name="Alpha">1 − translucency (the part material's alpha); 1 for particles without a part material.</param>
/// <param name="ViewerDistance">Retail <c>CYpt</c> used for the LOD choice.</param>
/// <param name="PartMaterial">The particle has a part material: <see cref="Alpha"/> replaces the surface's vertex alpha.</param>
/// <param name="Tag">The caller's per-object value (<see cref="ParticleDrawList.Add(EffectObject, Vector3, IParticleModelSource, int)"/>),
/// carried to the instance data unchanged; the world's EnvCell statics pass their cell's draw-state slot. 0 by default.</param>
public readonly record struct ParticleDraw(Vector3 Origin, Quaternion Orientation, float Scale, float Alpha, float ViewerDistance, bool PartMaterial, int Tag = 0);

/// <summary>Draws of one <see cref="ParticleBatchKey"/>, in retail order (objects as added, emitters in creation order, slots ascending).</summary>
public sealed class ParticleBatch
{
    internal ParticleBatch(ParticleBatchKey key) => Key = key;

    public ParticleBatchKey Key { get; }

    public List<ParticleDraw> Draws { get; } = [];

    /// <summary>
    /// Axis-aligned bounds (AC) of the drawn particles for culling and sorting: the particle origins padded by the largest
    /// scale times <paramref name="meshRadius"/> (the drawn GfxObj's largest distance from its origin).
    /// </summary>
    public (Vector3 Min, Vector3 Max) Bounds(float meshRadius)
    {
        if (Draws.Count == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var scale = 0f;
        foreach (var d in Draws)
        {
            min = Vector3.Min(min, d.Origin);
            max = Vector3.Max(max, d.Origin);
            scale = MathF.Max(scale, d.Scale);
        }

        var pad = new Vector3(scale * meshRadius);
        return (min - pad, max + pad);
    }
}

/// <summary>Counters of the last <see cref="ParticleDrawList"/> build.</summary>
public readonly record struct ParticleDrawStats(int Live, int Drawn, int Hidden, int OutOfRange, int Degraded, int Missing);

/// <summary>
/// Collects the visible particles of effect objects into draw batches keyed by (drawn GfxObj, material state), applying
/// retail's per-part viewer rules (<see cref="DrawFrameRules"/>) and translucency rules
/// (<see cref="PartTranslucencyRules"/>). Engine-agnostic; reused every frame without allocating once warm.
/// </summary>
/// <remarks>
/// <para>Retail draws each particle as a <c>CPhysicsPart</c> of the emitter's particle object (<c>ParticleEmitter::SetInfo</c>
/// makes <c>max_particles</c> parts of the hardware GfxObj; <c>Particle::Update</c> sets the part's frame, its
/// <c>gfxobj_scale</c> to the particle scale and its translucency). Per frame:</para>
/// <list type="number">
/// <item>Emitters whose particle object is degraded out (<see cref="ParticleEmitter.DegradedOut"/>) draw nothing.</item>
/// <item>The particle object's position is the emitter position, <c>Frame::combine(parent part or object frame,
/// parent offset)</c> (<c>CPhysicsObj::UpdateChild</c>). If it is at least
/// <see cref="DegradeSelector.ParticleDegradeDistance"/> (25 m at the steady-state bias) away horizontally, all its
/// particles use its distance and heading; otherwise each particle uses its own (to its scaled sort centre).</item>
/// <item>The level is chosen with the LOD distance <c>CYpt / scale</c>; a level whose GfxObj is 0 draws nothing (small
/// particles therefore vanish sooner).</item>
/// <item>The draw orientation follows the level's degrade mode (particle GfxObjs: mode 2, a camera-facing billboard).</item>
/// <item>Translucency 1.0 hides the particle; 0 draws with the surface's own alpha; anything else draws with a part
/// material of alpha 1 − t (and possibly another blend, <see cref="PartTranslucencyRules.WithPartMaterial"/>).</item>
/// </list>
/// </remarks>
public sealed class ParticleDrawList
{
    private readonly Dictionary<ParticleBatchKey, ParticleBatch> _byKey = [];
    private readonly List<ParticleBatch> _active = [];
    private int _live;
    private int _drawn;
    private int _hidden;
    private int _outOfRange;
    private int _degraded;
    private int _missing;

    /// <summary>
    /// Retail <c>Render::deg_mul</c>; 1 is the steady state on any machine above 25 fps (<see cref="DegradeSelector"/>).
    /// Scales both the particle-object relation threshold and every particle's level selection.
    /// </summary>
    public float DegradeBias { get; set; } = DegradeSelector.SteadyStateBias;

    /// <summary>Batches that have at least one draw, in first-use order.</summary>
    public IReadOnlyList<ParticleBatch> Batches => _active;

    public ParticleDrawStats Stats => new(_live, _drawn, _hidden, _outOfRange, _degraded, _missing);

    /// <summary>Starts a new frame (keeps the batch objects for reuse).</summary>
    public void Clear()
    {
        foreach (var batch in _active)
        {
            batch.Draws.Clear();
        }

        _active.Clear();
        _live = _drawn = _hidden = _outOfRange = _degraded = _missing = 0;
    }

    /// <summary>Adds every live particle of <paramref name="effects"/> as seen from <paramref name="viewer"/> (AC world).</summary>
    /// <param name="effects">The effect object.</param>
    /// <param name="viewer">The viewer (AC, the effect object's space).</param>
    /// <param name="models">Particle GfxObj models.</param>
    /// <param name="tag">Carried to every draw of this object (<see cref="ParticleDraw.Tag"/>).</param>
    public void Add(EffectObject effects, Vector3 viewer, IParticleModelSource models, int tag = 0)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(models);
        var objectThreshold = DegradeSelector.ParticleDegradeDistance(DegradeBias);
        var emitters = effects.Particles.Emitters;
        for (var e = 0; e < emitters.Count; e++)
        {
            // Indexed (the world draws hundreds of effect objects a frame; an interface foreach allocates an enumerator).
            var emitter = emitters[e];
            var slots = emitter.Slots;
            if (emitter.DegradedOut)
            {
                _degraded += emitter.NumParticles;
                continue;
            }

            if (models.Get(emitter.Info.HwGfxObjId) is not { } model)
            {
                foreach (var slot in slots)
                {
                    _missing += slot is null ? 0 : 1;
                }

                continue;
            }

            var emitterOrigin = EmitterPosition(effects, emitter);
            ViewerRelation? shared = DrawFrameRules.UsesObjectRelation(viewer, emitterOrigin, objectThreshold)
                ? DrawFrameRules.Relate(viewer, emitterOrigin)
                : null;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] is not { } particle)
                {
                    continue;
                }

                _live++;
                Add(particle.Frame, particle.Scale, particle.Translucency, model, viewer, shared, tag);
            }
        }
    }

    /// <summary>Adds one particle part (exposed for tests and for callers with their own particle sources).</summary>
    public void Add(in AcFrame partFrame, float scale, float translucency, ParticleModel model, Vector3 viewer, ViewerRelation? objectRelation = null, int tag = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (PartTranslucencyRules.IsHidden(translucency))
        {
            _hidden++;
            return;
        }

        var relation = objectRelation ?? DrawFrameRules.PartRelation(viewer, partFrame, new Vector3(scale), model.SortCenter);
        var (_, gfxObjId, mode) = DrawFrameRules.SelectLevel(model.Chain, relation.Distance, scale, bias: DegradeBias);
        if (gfxObjId == 0)
        {
            _outOfRange++;
            return;
        }

        var partMaterial = PartTranslucencyRules.HasPartMaterial(translucency);
        var key = new ParticleBatchKey(gfxObjId, partMaterial && model.PartMaterialSwitchesBlend);
        if (!_byKey.TryGetValue(key, out var batch))
        {
            batch = new ParticleBatch(key);
            _byKey.Add(key, batch);
        }

        if (batch.Draws.Count == 0)
        {
            _active.Add(batch);
        }

        var orientation = DrawFrameRules.DrawOrientation(partFrame.Orientation, mode, relation.Heading);
        batch.Draws.Add(new ParticleDraw(
            partFrame.Origin, orientation, scale, partMaterial ? PartTranslucencyRules.MaterialAlpha(translucency) : 1f, relation.Distance, partMaterial, tag));
        _drawn++;
    }

    /// <summary>
    /// The particle object's position: <c>Frame::combine(parent frame, parent offset)</c>, where the parent frame is the
    /// emitter's part (or the object frame for part −1, or for an index the object does not have, as the effects layer does).
    /// </summary>
    public static Vector3 EmitterPosition(EffectObject effects, ParticleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(emitter);
        var parent = emitter.PartIndex >= 0 && emitter.PartIndex < effects.PartCount ? effects.GetPartFrame(emitter.PartIndex) : effects.ObjectFrame;
        return AcFrame.Combine(parent, emitter.ParentOffset).Origin;
    }
}
