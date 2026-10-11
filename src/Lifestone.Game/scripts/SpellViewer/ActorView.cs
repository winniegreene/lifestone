using AcVector3 = System.Numerics.Vector3;
using Godot;
using Lifestone.Client.Effects;
using Lifestone.Data.Effects;
using Lifestone.Game.Core;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// A Setup-based actor drawn as retail draws a part array: one <see cref="MeshInstance3D"/> per part, posed every frame
/// from the actor's part frames (<see cref="ActorPartDraws"/>: animation pose or placement frame, per-part LOD, degrade
/// modes), meshes from <see cref="ActorMeshLibrary"/>. Transforms go through <see cref="AcToGodot.Transform"/>.
/// </summary>
/// <remarks>
/// Not drawn yet (logged once when they occur): part-level translucency, luminosity and diffusion overrides from
/// Transparent/Luminous/Diffuse hooks (<see cref="EffectVisualState"/>) — none of the showcase spells uses them — and
/// ObjDesc clothing/palettes (M2).
/// </remarks>
public partial class ActorView : Node3D
{
    private ActorPartDraws _draws = null!;
    private ActorMeshLibrary _meshes = null!;
    private MeshInstance3D[] _parts = [];
    private uint[] _shown = [];
    private bool _warnedOverrides;

    /// <summary>The Setup shown.</summary>
    public uint SetupId { get; private set; }

    /// <summary>True when this actor is the player's own character (always level 0, mode 1).</summary>
    public bool IsPlayer { get; set; }

    /// <summary>Main thread: builds the part nodes for a Setup.</summary>
    public void Initialize(GameServices services, uint setupId, ActorMeshLibrary meshes)
    {
        var dats = services.Dats ?? throw new InvalidOperationException("dats not open");
        var assembly = services.Setups.Get(setupId) ?? throw new KeyNotFoundException($"Setup 0x{setupId:X8} not found.");
        _draws = ActorPartDraws.Create(dats, assembly, services.GfxObjMeshes);
        _meshes = meshes;
        SetupId = setupId;
        Name = $"Setup_0x{setupId:X8}";
        _parts = new MeshInstance3D[assembly.Parts.Count];
        _shown = new uint[_parts.Length];
        for (var i = 0; i < _parts.Length; i++)
        {
            _parts[i] = new MeshInstance3D
            {
                Name = $"Part{i:D2}_0x{assembly.Parts[i].GfxObjId:X8}",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false,
            };
            AddChild(_parts[i]);
        }
    }

    /// <summary>
    /// Main thread, during loading: builds (or shares) the meshes, materials and textures of every GfxObj this actor can
    /// draw (the player's own character only ever draws level 0), so its first posed frame does not pay for them.
    /// </summary>
    /// <returns>The GfxObjs preloaded.</returns>
    public int PreloadMeshes()
    {
        var ids = (IsPlayer
                ? _draws.Assembly.Parts.Where(p => p.IsGfxObj).Select(p => _draws.Chains[p.Index].NearestGfxObjId)
                : _draws.AllGfxObjIds)
            .Where(id => id != 0).Distinct().ToArray();
        foreach (var id in ids)
        {
            _meshes.Get(id);
        }

        return ids.Length;
    }

    /// <summary>Main thread: poses the parts for this frame.</summary>
    /// <param name="actor">Simulated state.</param>
    /// <param name="viewer">Camera position (AC, same space as the actor).</param>
    /// <param name="hidden">Draw no part (NoDraw); particles are drawn elsewhere.</param>
    public void UpdatePose(EffectActor actor, AcVector3 viewer, bool hidden)
    {
        var visual = actor.Effects.Visual;
        if (!_warnedOverrides && (visual.Translucency != 0f || visual.Luminosity is not null || visual.Diffusion is not null
            || visual.PartTranslucency.Count > 0 || visual.PartLuminosity.Count > 0 || visual.PartDiffusion.Count > 0 || visual.TextureVelocity.Count > 0))
        {
            _warnedOverrides = true;
            Log.Warn("spellviewer", "TODO(objects): material overrides from hooks are not drawn yet",
                ("setup", $"0x{SetupId:X8}"), ("translucency", visual.Translucency), ("luminosity", visual.Luminosity), ("parts", visual.PartTranslucency.Count));
        }

        var draws = _draws.Compute(actor, viewer, IsPlayer, hidden, SpellStage.DrawScale(actor));
        for (var i = 0; i < draws.Length; i++)
        {
            var draw = draws[i];
            var node = _parts[i];
            if (draw.GfxObjId == 0)
            {
                node.Visible = false;
                continue;
            }

            if (_shown[i] != draw.GfxObjId)
            {
                _shown[i] = draw.GfxObjId;
                node.Mesh = _meshes.Get(draw.GfxObjId);
            }

            node.Transform = AcToGodot.Transform(draw.Frame.Origin, draw.Frame.Orientation, draw.Scale);
            node.Visible = node.Mesh is not null;
        }
    }
}
