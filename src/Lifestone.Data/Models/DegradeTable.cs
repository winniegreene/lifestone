using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Models;

/// <summary>
/// How a LOD level is oriented when drawn (<c>GfxObjInfo::degrade_mode</c>, applied by
/// <c>CPhysicsPart::calc_draw_frame</c>).
/// </summary>
public enum DegradeMode
{
    /// <summary>Drawn with the part's own frame (<c>deg_mode == 1</c>, the default).</summary>
    Normal = 1,

    /// <summary>Full camera-facing billboard: <c>Frame::set_vector_heading(viewer_heading)</c>.</summary>
    Billboard = 2,

    /// <summary>Rotated about the part's local X axis towards the viewer: <c>Frame::rotate_around_axis_to_vector(0, …)</c>.</summary>
    AxisAlignedX = 3,

    /// <summary>Rotated about the part's local Y axis towards the viewer (<c>rotate_around_axis_to_vector(1, …)</c>).</summary>
    AxisAlignedY = 4,

    /// <summary>Rotated about the part's local Z axis towards the viewer (<c>rotate_around_axis_to_vector(2, …)</c>).</summary>
    AxisAlignedZ = 5,
}

/// <summary>
/// One LOD level (<c>GfxObjInfo</c>: <c>{gfxobj_id, degrade_mode, min_dist, ideal_dist, max_dist}</c>).
/// </summary>
/// <param name="GfxObjId">GfxObj to draw at this level; 0 means <b>draw nothing</b> (retail stores a null pointer in
/// <c>CPhysicsPart::gfxobj[]</c> and <c>CPhysicsPart::Draw</c> skips it).</param>
/// <param name="Mode">Orientation of this level (end-of-retail data uses 1, 2, 4 and 5).</param>
/// <param name="MinDistance">Switch distance used when the degrade bias is negative.</param>
/// <param name="IdealDistance">Switch distance at bias 0.</param>
/// <param name="MaxDistance">Switch distance at bias +1 (the steady state on modern hardware).</param>
public readonly record struct DegradeLevel(
    uint GfxObjId,
    DegradeMode Mode,
    float MinDistance,
    float IdealDistance,
    float MaxDistance)
{
    /// <summary>True when this level draws nothing (every end-of-retail table ends with such a level at FLT_MAX).</summary>
    public bool DrawsNothing => GfxObjId == 0;
}

/// <summary>A GfxObjDegradeInfo record (<c>0x11xxxxxx</c>): an ordered list of LOD levels, nearest first.</summary>
public sealed class DegradeTable
{
    public DegradeTable(uint id, IReadOnlyList<DegradeLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
        {
            throw new InvalidDataException($"GfxObjDegradeInfo 0x{id:X8} has no levels.");
        }

        Id = id;
        Levels = levels;
    }

    public uint Id { get; }

    public IReadOnlyList<DegradeLevel> Levels { get; }

    /// <summary>
    /// <c>GfxObjDegradeInfo::get_max_degrade_distance</c>: <c>degrades[0].max_dist</c> when there are at most two
    /// levels, else the <c>max_dist</c> of the second-to-last level (the last level being the "draw nothing" one).
    /// <c>CPhysicsPart::GetMaxDegradeDistance</c> returns 100 for a part without a table.
    /// </summary>
    public float MaxDegradeDistance => Levels.Count <= 2 ? Levels[0].MaxDistance : Levels[^2].MaxDistance;

    /// <summary><c>CPhysicsPart::Always2D</c>: the nearest level is already a billboard (mode ≠ 1).</summary>
    public bool Always2D => Levels[0].Mode != DegradeMode.Normal;

    public static DegradeTable FromDat(uint id, GfxObjDegradeInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var levels = info.Degrades
            .Select(d => new DegradeLevel(d.Id.DataId, (DegradeMode)d.DegradeMode, d.MinDist, d.IdealDist, d.MaxDist))
            .ToArray();
        return new DegradeTable(id, levels);
    }

    /// <summary>Reads GfxObjDegradeInfo <paramref name="degradeId"/>; null when absent.</summary>
    public static DegradeTable? Load(DatSet dats, uint degradeId)
    {
        ArgumentNullException.ThrowIfNull(dats);
        return dats.TryGetPortal<GfxObjDegradeInfo>(degradeId, out var info) ? FromDat(degradeId, info) : null;
    }
}

/// <summary>
/// Retail LOD selection (<c>GfxObjDegradeInfo::get_degrade</c>) and the distances that feed it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Distance.</b> Per part, every frame (<c>CPhysicsPart::UpdateViewerDistance()</c>): <c>CYpt</c> = distance from the
/// viewer to the part's <see cref="GfxObjMesh.SortCenter"/> (of the nearest LOD level's GfxObj, scaled by the part's
/// <c>gfxobj_scale</c>, placed by the part frame); the LOD distance is <c>CYpt / gfxobj_scale.z</c>. When the object's
/// origin is at least <see cref="ObjectDegradeDistance"/> away horizontally, <c>CPhysicsObj::UpdateViewerDistance</c>
/// instead passes the object-origin distance and heading to every part (still divided by each part's scale.z).
/// The player's own parts always use level 0 / mode 1 (<c>CPhysicsPart::player_iid</c> check).
/// </para>
/// <para>
/// <b>Selection.</b> <c>d = max(0, |distance| − s_rDegradeDistance)</c>; with bias <c>b ≥ 0</c> the first level
/// <c>i</c> with <c>d &lt; ideal_i + (max_i − ideal_i)·b</c> wins, with <c>b &lt; 0</c> the first with
/// <c>d &lt; ideal_i + (ideal_i − min_i)·b</c>; if none, the last level. <c>Render::force_level ≥ 0</c> and
/// <c>degrades_disabled</c> override this; neither is set in normal play.
/// </para>
/// <para>
/// <b>Defaults</b> (acclient 11.4186 <c>acclient_data.h</c>): <c>Render::s_rDegradeDistance = 50.0</c>,
/// <c>Render::auto_update_deg_mul = true</c>, <c>Render::deg_mul</c> starts at 0. With automatic degrades on, the
/// bias is <c>deg_mul</c>, driven by the fuzzy controller <c>Render::CalcDegLevel</c> (min/ideal/max framerate
/// 8/10/20): above 25 fps its "high" rule adds +0.1 per update and clamps at +1, so on any modern machine the
/// steady-state bias is <see cref="SteadyStateBias"/> = 1, i.e. the switch distance is each level's <c>max_dist</c>
/// (+50 m).
/// </para>
/// </remarks>
public static class DegradeSelector
{
    /// <summary><c>Render::s_rDegradeDistance</c> default ("Degrade Distance" preference).</summary>
    public const float DefaultDegradeDistance = 50f;

    /// <summary>Steady-state <c>Render::deg_mul</c> at framerates above 25 fps (<c>Render::CalcDegLevel</c>).</summary>
    public const float SteadyStateBias = 1f;

    /// <summary><c>CPhysicsPart::GetMaxDegradeDistance</c> for a part without a degrade table.</summary>
    public const float DefaultMaxDegradeDistance = 100f;

    /// <summary>Viewer distances at or below this are treated as zero when computing the viewer heading.</summary>
    public const float MinHeadingDistance = 0.00019999999f;

    /// <summary>
    /// Index of the level retail draws at <paramref name="distance"/> (<c>GfxObjDegradeInfo::get_degrade</c>).
    /// </summary>
    /// <param name="table">The part's degrade table.</param>
    /// <param name="distance">LOD distance: <c>CYpt / gfxobj_scale.z</c>, in metres.</param>
    /// <param name="bias"><c>Render::deg_mul</c> (automatic degrades) or the user degrade bias, in [-1, 1].</param>
    /// <param name="degradeDistance"><c>Render::s_rDegradeDistance</c>.</param>
    public static int SelectLevel(
        DegradeTable table,
        float distance,
        float bias = SteadyStateBias,
        float degradeDistance = DefaultDegradeDistance)
    {
        ArgumentNullException.ThrowIfNull(table);
        var levels = table.Levels;
        var d = MathF.Abs(distance) - degradeDistance;
        if (d < 0f)
        {
            d = 0f;
        }

        if (bias >= 0f)
        {
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                if (d < level.IdealDistance - ((level.IdealDistance - level.MaxDistance) * bias))
                {
                    return i;
                }
            }
        }
        else
        {
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                if (((level.IdealDistance - level.MinDistance) * bias) + level.IdealDistance > d)
                {
                    return i;
                }
            }
        }

        return levels.Count - 1;
    }

    /// <summary>
    /// Horizontal distance beyond which retail uses the object's distance for all its parts
    /// (<c>Render::SetDegradeLevelInternal</c>: <c>object_distance_2dsq</c> = (25 + 25·b)² for b ≥ 0,
    /// (25 + 17·b)² for b &lt; 0). 50 m at the steady-state bias.
    /// </summary>
    public static float ObjectDegradeDistance(float bias = SteadyStateBias) =>
        bias >= 0f ? 25f + (25f * bias) : 25f + (17f * bias);

    /// <summary>
    /// Same threshold for particle-emitter objects (<c>particle_distance_2dsq</c> = (16 + 9·b)² for b ≥ 0,
    /// (16 + 8·b)² for b &lt; 0).
    /// </summary>
    public static float ParticleDegradeDistance(float bias = SteadyStateBias) =>
        bias >= 0f ? 16f + (9f * bias) : 16f + (8f * bias);

    /// <summary>
    /// <c>CPhysicsPart::UpdateViewerDistance()</c>: distance from <paramref name="viewer"/> to the part's scaled sort
    /// centre, all in the same (world or object) space, and the LOD distance <c>CYpt / scale.z</c>.
    /// </summary>
    /// <param name="viewer">Viewer position.</param>
    /// <param name="partFrame">The part's frame in the same space as <paramref name="viewer"/>.</param>
    /// <param name="partScale">The part's <c>gfxobj_scale</c> (Setup DefaultScale × object scale).</param>
    /// <param name="sortCenter">Sort centre of the part's nearest-level GfxObj (<c>gfxobj[0]</c>).</param>
    public static (float ViewerDistance, float LodDistance) PartDistances(
        Vector3 viewer,
        PartFrame partFrame,
        Vector3 partScale,
        Vector3 sortCenter)
    {
        var point = partFrame.TransformPoint(sortCenter * partScale);
        var viewerDistance = Vector3.Distance(viewer, point);
        return (viewerDistance, viewerDistance / partScale.Z);
    }
}
