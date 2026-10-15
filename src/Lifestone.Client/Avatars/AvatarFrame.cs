using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>Where the avatars are seen from: the camera (LOD, degrade headings) and the listener (sound bearings).</summary>
/// <param name="Position">The camera's cell and frame.</param>
/// <param name="DegradeBias">Retail <c>Render::deg_mul</c> (1 = the steady state).</param>
public readonly record struct AvatarViewer(CellFrame Position, float DegradeBias = DegradeSelector.SteadyStateBias);

/// <summary>
/// One avatar as drawn this frame. Its frames (<see cref="Frame"/>, the parts' <see cref="PartDraw.Frame"/> and its
/// particles) are in the landblock space of <see cref="AnchorCellId"/>.
/// </summary>
/// <param name="Guid">The object.</param>
/// <param name="CellId">The object's cell (for interiors: draw an EnvCell object only when its cell is drawn).</param>
/// <param name="AnchorCellId">The cell whose landblock the frames are expressed in (<see cref="AvatarBody.AnchorCellId"/>).</param>
/// <param name="Frame">The object frame.</param>
/// <param name="AppearanceVersion">Changes on every ObjDesc change: rebuild the parts' meshes and materials.</param>
/// <param name="PoseVersion">Changes when the drawn keyframe or the frame changes: rewrite the transforms.</param>
/// <param name="Visible">Drawn: in a cell, neither NoDraw nor Hidden (and not the player's own body in first person).</param>
/// <param name="IsPlayer">The player's own character (always LOD level 0).</param>
/// <param name="InEnvCell">The cell is an EnvCell.</param>
/// <param name="LightsOn">The Setup's lights are on (LightingOn).</param>
/// <param name="Translucency">The object's translucency (0 opaque … 1 not drawn).</param>
/// <param name="PartStart">The first of its rows in <see cref="AvatarFrame.Parts"/>.</param>
/// <param name="PartCount">Its part count.</param>
/// <param name="ParticleStart">The first of its particles in <see cref="AvatarFrame.Particles"/>.</param>
/// <param name="ParticleCount">Its particle count.</param>
public readonly record struct AvatarDraw(
    uint Guid,
    uint CellId,
    uint AnchorCellId,
    AcFrame Frame,
    int AppearanceVersion,
    int PoseVersion,
    bool Visible,
    bool IsPlayer,
    bool InEnvCell,
    bool LightsOn,
    float Translucency,
    int PartStart,
    int PartCount,
    int ParticleStart,
    int ParticleCount);

/// <summary>
/// The avatars' draw output of one <see cref="AvatarSet.Advance"/>: one row per avatar, its parts' draws (GfxObj per
/// LOD level, frame, scale, degrade mode, as the effects track's <see cref="PartDraw"/>) and translucencies, and its live
/// particles. The buffers are reused: the spans are valid until the next advance. Allocation-free once grown.
/// </summary>
public sealed class AvatarFrame
{
    private AvatarDraw[] _avatars = new AvatarDraw[64];
    private PartDraw[] _parts = new PartDraw[1024];
    private float[] _translucency = new float[1024];
    private readonly List<ParticleInstance> _particles = [];
    private int _avatarCount;
    private int _partCount;

    /// <summary>One row per avatar.</summary>
    public ReadOnlySpan<AvatarDraw> Avatars => _avatars.AsSpan(0, _avatarCount);

    /// <summary>The parts of every avatar, in <see cref="AvatarDraw.PartStart"/> order.</summary>
    public ReadOnlySpan<PartDraw> Parts => _parts.AsSpan(0, _partCount);

    /// <summary>
    /// Each part's own translucency (parallel to <see cref="Parts"/>): a TransparentPart hook's value, else the object's;
    /// <see cref="PartTranslucencyRules"/> says how to draw it (1 = not drawn).
    /// </summary>
    public ReadOnlySpan<float> PartTranslucency => _translucency.AsSpan(0, _partCount);

    /// <summary>The live particles of every avatar, in its anchor landblock's space.</summary>
    public IReadOnlyList<ParticleInstance> Particles => _particles;

    internal void Clear()
    {
        _avatarCount = 0;
        _partCount = 0;
        _particles.Clear();
    }

    /// <summary>Appends one avatar's draws: its parts and its particles.</summary>
    internal void Add(Avatar avatar, in AvatarViewer viewer, bool hideBody, bool retailUnhideFlash = false)
    {
        var body = avatar.Body;
        var setup = body.Setup;
        var parts = setup.Parts.Count;
        EnsureParts(_partCount + parts);
        if (_avatarCount == _avatars.Length)
        {
            Array.Resize(ref _avatars, _avatars.Length * 2);
        }

        var visible = body.IsDrawn && !hideBody;
        var frame = body.AnchorFrame;
        var viewerLocal = viewer.Position.CellId == 0 || body.AnchorCellId == 0
            ? viewer.Position.Origin
            : viewer.Position.Origin + LandDefs.GetBlockOffset(body.AnchorCellId, viewer.Position.CellId);
        var visual = body.Effects.Visual;
        var objectTranslucency = Math.Max(visual.Translucency, visual.TranslucencyOriginal);
        if (body.ConsumeUnhideHold() && !retailUnhideFlash)
        {
            objectTranslucency = 1f; // the glitch fix: the first frame after an unhide is not drawn until the new fade's hook applies (AvatarBody.ConsumeUnhideHold)
        }

        var scale = visual.Scale != 1f ? visual.Scale : body.Scale;
        var appearance = body.Appearance.Parts;
        var frames = body.PartFrames;
        var objectRelation = DrawFrameRules.UsesObjectRelation(viewerLocal, frame.Origin, DegradeSelector.ObjectDegradeDistance(viewer.DegradeBias))
            ? DrawFrameRules.Relate(viewerLocal, frame.Origin)
            : (ViewerRelation?)null;
        var start = _partCount;
        for (var i = 0; i < parts; i++)
        {
            var part = setup.Parts[i];
            var partFrame = i < frames.Length ? frames[i] : frame;
            var partScale = part.DefaultScale * scale;
            var chain = appearance[i].Chain;
            _translucency[_partCount] = visual.PartTranslucency.TryGetValue(i, out var t) ? t : objectTranslucency;
            if (!visible || !part.IsGfxObj || !chain.IsLoadable)
            {
                _parts[_partCount++] = new PartDraw(i, 0, partFrame, partScale, DegradeMode.Normal);
                continue;
            }

            var relation = objectRelation ?? DrawFrameRules.PartRelation(viewerLocal, partFrame, partScale, avatar.SortCenter(i));
            var (_, gfxObjId, mode) = DrawFrameRules.SelectLevel(chain, relation.Distance, partScale.Z, avatar.IsPlayer, viewer.DegradeBias);
            var orientation = gfxObjId == 0 ? partFrame.Orientation : DrawFrameRules.DrawOrientation(partFrame.Orientation, mode, relation.Heading);
            _parts[_partCount++] = new PartDraw(i, gfxObjId, partFrame with { Orientation = orientation }, partScale, mode);
        }

        var particleStart = _particles.Count;
        if (body.Effects.Particles.Count > 0)
        {
            // Only with emitters: CollectParticles enumerates them through an interface, which allocates.
            body.Effects.CollectParticles(_particles);
        }
        var cell = body.Position.CellId;
        _avatars[_avatarCount++] = new AvatarDraw(
            body.Guid,
            cell,
            body.AnchorCellId,
            frame,
            body.Appearance.Version,
            body.PoseVersion,
            visible,
            avatar.IsPlayer,
            LandDefs.IsEnvCell(cell),
            body.LightsOn,
            objectTranslucency,
            start,
            parts,
            particleStart,
            _particles.Count - particleStart);
    }

    private void EnsureParts(int count)
    {
        if (count <= _parts.Length)
        {
            return;
        }

        var size = _parts.Length;
        while (size < count)
        {
            size *= 2;
        }

        Array.Resize(ref _parts, size);
        Array.Resize(ref _translucency, size);
    }
}
