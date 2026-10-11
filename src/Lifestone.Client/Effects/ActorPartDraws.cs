using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;

namespace Lifestone.Client.Effects;

/// <summary>One part of an actor as drawn this frame.</summary>
/// <param name="PartIndex">Setup part index.</param>
/// <param name="GfxObjId">GfxObj of the selected LOD level; 0 = draw nothing (hidden object, or a level that draws nothing).</param>
/// <param name="Frame">Draw frame in world space (AC): the part frame, re-oriented by the level's degrade mode.</param>
/// <param name="Scale">Mesh scale (<c>gfxobj_scale</c> = Setup default scale ⊙ object scale).</param>
/// <param name="Mode">Degrade mode of the selected level.</param>
public readonly record struct PartDraw(int PartIndex, uint GfxObjId, AcFrame Frame, Vector3 Scale, DegradeMode Mode);

/// <summary>
/// The parts of a Setup-based actor (caster, creature, projectile) to draw this frame, from its <see cref="EffectActor"/>
/// state: part frames, LOD and degrade modes (<see cref="DrawFrameRules"/>), visibility. Engine-agnostic.
/// </summary>
/// <remarks>
/// <para><b>Frames.</b> <see cref="EffectActor.PartFrames"/> are retail <c>CPartArray::UpdateParts</c> frames while a pose
/// driver poses the actor. An actor without a pose (no motion table and no Setup default animation, e.g. most spell
/// projectiles) shows the Setup's placement frame (<c>CPartArray::SetPlacementFrame(0x65)</c>: Resting, else Default),
/// composed with the object frame the same way (<see cref="ModelAssembly.ComputeRestPose"/>).</para>
/// <para><b>Scale.</b> Mesh scale is <c>DefaultScale[i] × object scale</c> (<c>CPartArray::SetScaleInternal</c>).</para>
/// <para><b>LOD.</b> <see cref="DrawFrameRules.SelectLevel"/> per part with the retail distances
/// (<c>CPhysicsPart::UpdateViewerDistance</c>; beyond <see cref="DegradeSelector.ObjectDegradeDistance"/> horizontally the
/// object's distance and heading, <c>CPhysicsObj::UpdateViewerDistance</c>). The player's own character always draws
/// level 0 with mode 1.</para>
/// </remarks>
public sealed class ActorPartDraws
{
    private readonly ModelAssembly _assembly;
    private readonly LodChain[] _chains;
    private readonly Vector3[] _sortCenters;
    private readonly Data.Models.PartPose[]? _restPose;
    private readonly PartDraw[] _draws;

    /// <param name="assembly">The actor's Setup.</param>
    /// <param name="chains">LOD chain of each part's GfxObj (<see cref="LodChain.Load"/>), parallel to the Setup parts.</param>
    /// <param name="sortCenters">Sort centre of each chain's level-0 GfxObj (<c>CGfxObj::sort_center</c>).</param>
    public ActorPartDraws(ModelAssembly assembly, IReadOnlyList<LodChain> chains, IReadOnlyList<Vector3> sortCenters)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(chains);
        ArgumentNullException.ThrowIfNull(sortCenters);
        if (chains.Count != assembly.Parts.Count || sortCenters.Count != assembly.Parts.Count)
        {
            throw new ArgumentException($"Setup 0x{assembly.Id:X8} has {assembly.Parts.Count} parts; got {chains.Count} chains and {sortCenters.Count} sort centres.");
        }

        _assembly = assembly;
        _chains = chains.ToArray();
        _sortCenters = sortCenters.ToArray();
        _restPose = assembly.ResolvePlacement() is not null ? assembly.ComputeRestPose() : null;
        _draws = new PartDraw[assembly.Parts.Count];
    }

    /// <summary>Loads the LOD chains and sort centres of an assembly's parts from the dats.</summary>
    public static ActorPartDraws Create(Lifestone.Data.Dat.DatSet dats, ModelAssembly assembly, GfxObjMeshCache meshes)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(meshes);
        var chains = assembly.Parts.Select(p => LodChain.Load(dats, p.GfxObjId)).ToArray();
        var sortCenters = chains.Select(c => meshes.Get(c.NearestGfxObjId)?.SortCenter ?? Vector3.Zero).ToArray();
        return new ActorPartDraws(assembly, chains, sortCenters);
    }

    public ModelAssembly Assembly => _assembly;

    public IReadOnlyList<LodChain> Chains => _chains;

    /// <summary>
    /// Retail <c>Render::deg_mul</c> (1 = the steady state): scales both the object relation threshold and every part's
    /// level selection.
    /// </summary>
    public float DegradeBias { get; set; } = DegradeSelector.SteadyStateBias;

    /// <summary>Every distinct GfxObj any part can draw at any level (for preloading meshes).</summary>
    public IEnumerable<uint> AllGfxObjIds => _chains.SelectMany(c => c.GfxObjIds).Where(id => id != 0).Distinct();

    /// <summary>
    /// The parts to draw for <paramref name="actor"/> seen from <paramref name="viewer"/>. <paramref name="objectScale"/>
    /// overrides <see cref="EffectActor.Scale"/> (e.g. after a Scale hook); <paramref name="hidden"/> draws nothing.
    /// </summary>
    public ReadOnlySpan<PartDraw> Compute(EffectActor actor, Vector3 viewer, bool isPlayer, bool hidden = false, float? objectScale = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var scale = objectScale ?? actor.Scale;
        var posed = actor.Motion?.Pose is not null;
        var frames = actor.PartFrames;
        var objectRelation = DrawFrameRules.UsesObjectRelation(viewer, actor.Frame.Origin, DegradeSelector.ObjectDegradeDistance(DegradeBias))
            ? DrawFrameRules.Relate(viewer, actor.Frame.Origin)
            : (ViewerRelation?)null;
        for (var i = 0; i < _draws.Length; i++)
        {
            var part = _assembly.Parts[i];
            var frame = PartFrame(actor, frames, posed, i, scale);
            var partScale = part.DefaultScale * scale;
            if (hidden || !part.IsGfxObj)
            {
                _draws[i] = new PartDraw(i, 0, frame, partScale, DegradeMode.Normal);
                continue;
            }

            var relation = objectRelation ?? DrawFrameRules.PartRelation(viewer, frame, partScale, _sortCenters[i]);
            var (_, gfxObjId, mode) = DrawFrameRules.SelectLevel(_chains[i], relation.Distance, partScale.Z, isPlayer, DegradeBias);
            var orientation = gfxObjId == 0 ? frame.Orientation : DrawFrameRules.DrawOrientation(frame.Orientation, mode, relation.Heading);
            _draws[i] = new PartDraw(i, gfxObjId, frame with { Orientation = orientation }, partScale, mode);
        }

        return _draws;
    }

    private AcFrame PartFrame(EffectActor actor, ReadOnlySpan<AcFrame> frames, bool posed, int index, float scale)
    {
        if (posed && index < frames.Length)
        {
            return frames[index];
        }

        if (_restPose is null || index >= _restPose.Length)
        {
            return actor.Frame;
        }

        // CPartArray::UpdateParts with the placement frame: origin scaled by the object scale, q = obj.q ⊗ part.q.
        var rest = _restPose[index];
        return AcFrame.Combine(actor.Frame, new AcFrame(rest.Origin, rest.Orientation), new Vector3(scale));
    }
}
