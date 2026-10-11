using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;

namespace Lifestone.Client.Effects;

/// <summary>The viewer as one drawn part sees it: the distance and the unit direction from the viewer to the part.</summary>
/// <param name="Distance">Retail <c>CYpt</c>: viewer to the part's scaled sort centre (or to the object origin), metres.</param>
/// <param name="Heading">Retail <c>viewer_heading</c>: unit vector from the viewer towards that point; (0, 0, 1) when the
/// viewer is at the point.</param>
public readonly record struct ViewerRelation(float Distance, Vector3 Heading);

/// <summary>
/// How retail orients and picks the level of detail of a drawn part, every frame, from where the viewer is. Particles use
/// it for their billboards (their GfxObjs' degrade tables say "mode 2" at level 0); actors use it for LOD and for the rare
/// camera-facing level. Engine-agnostic, AC coordinates (Z up).
/// </summary>
/// <remarks>
/// <para>Re-expressed from the retail client (Mag-nus/acclient-AI-RE, acclient 11.4186, by function name; no decompiled
/// code is reproduced):</para>
/// <list type="bullet">
/// <item><c>CPhysicsPart::UpdateViewerDistance()</c>: <c>CYpt</c> = |part frame · (sort_centre ⊙ gfxobj_scale) − viewer|
/// (<c>Position::get_offset</c>), <c>viewer_heading</c> = that offset normalised, or (0, 0, 1) when <c>CYpt</c> ≤ 0.0002;
/// the LOD distance passed to <c>GfxObjDegradeInfo::get_degrade</c> is <c>CYpt / gfxobj_scale.z</c>; the player's own
/// parts always use level 0 and mode 1 (<c>player_iid</c> check).</item>
/// <item><c>CPhysicsObj::UpdateViewerDistance()</c>: when the object's origin is at least
/// <c>Render::object_distance_2dsq</c> (or <c>particle_distance_2dsq</c> for particle objects) away <b>horizontally</b>
/// (x² + y²), every part takes the object's <c>CYpt</c> and heading instead (<c>CPartArray::UpdateViewerDistance(float,
/// Vector3*)</c>); the thresholds are <see cref="DegradeSelector.ObjectDegradeDistance"/> and
/// <see cref="DegradeSelector.ParticleDegradeDistance"/>.</item>
/// <item><c>CPhysicsPart::calc_draw_frame</c>: the draw frame is the part frame, re-oriented by the level's degrade mode:
/// 2 → <c>Frame::set_vector_heading(viewer_heading)</c>; 3/4/5 → <c>Frame::rotate_around_axis_to_vector(0/1/2,
/// viewer_heading)</c>; 1 (and anything else) leaves it.</item>
/// </list>
/// </remarks>
public static class DrawFrameRules
{
    /// <summary>Retail <c>F_EPSILON</c>.</summary>
    public const float Epsilon = 0.00019999999f;

    /// <summary>
    /// Retail <c>rotate_around_axis_to_vector</c> only re-orients when the new axis is perpendicular to the kept one
    /// within <c>0.00019999999 × 5.0</c>.
    /// </summary>
    public const float PerpendicularTolerance = Epsilon * 5f;

    /// <summary>Distance and heading from <paramref name="viewer"/> to <paramref name="point"/> (retail rule for a zero offset).</summary>
    public static ViewerRelation Relate(Vector3 viewer, Vector3 point)
    {
        var offset = point - viewer;
        var distance = offset.Length();
        return distance <= Epsilon ? new ViewerRelation(distance, Vector3.UnitZ) : new ViewerRelation(distance, offset / distance);
    }

    /// <summary>
    /// <c>CPhysicsPart::UpdateViewerDistance()</c>: the relation to the part's sort centre scaled by its
    /// <c>gfxobj_scale</c> and placed by its frame.
    /// </summary>
    public static ViewerRelation PartRelation(Vector3 viewer, in AcFrame partFrame, Vector3 gfxObjScale, Vector3 sortCenter) =>
        Relate(viewer, partFrame.LocalToGlobal(sortCenter * gfxObjScale));

    /// <summary>
    /// <c>CPhysicsObj::UpdateViewerDistance()</c>: true when the object is at least <paramref name="threshold"/> metres away
    /// horizontally, so all its parts use the object's distance and heading.
    /// </summary>
    public static bool UsesObjectRelation(Vector3 viewer, Vector3 objectOrigin, float threshold)
    {
        var dx = objectOrigin.X - viewer.X;
        var dy = objectOrigin.Y - viewer.Y;
        return (dx * dx) + (dy * dy) >= threshold * threshold;
    }

    /// <summary>
    /// The level a part draws (<see cref="LodChain.Select"/> with the LOD distance <c>CYpt / gfxobj_scale.z</c> and the
    /// degrade bias <c>Render::deg_mul</c>); the player's own parts always draw level 0 with mode 1.
    /// </summary>
    /// <param name="chain">The part's LOD chain.</param>
    /// <param name="viewerDistance">Retail <c>CYpt</c>.</param>
    /// <param name="gfxObjScaleZ">The part's <c>gfxobj_scale.z</c>.</param>
    /// <param name="isPlayer">The part belongs to the player's own character.</param>
    /// <param name="bias">Retail <c>Render::deg_mul</c>; the same value that scales the object/particle relation
    /// thresholds (<see cref="DegradeSelector.ObjectDegradeDistance"/>), so a caller's bias applies to both.</param>
    public static (int Level, uint GfxObjId, DegradeMode Mode) SelectLevel(
        LodChain chain, float viewerDistance, float gfxObjScaleZ, bool isPlayer = false, float bias = DegradeSelector.SteadyStateBias)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (isPlayer || chain.Table is null)
        {
            return (0, chain.NearestGfxObjId, DegradeMode.Normal);
        }

        return chain.Select(gfxObjScaleZ != 0f ? viewerDistance / gfxObjScaleZ : float.PositiveInfinity, bias);
    }

    /// <summary><c>CPhysicsPart::calc_draw_frame</c>: the orientation a part is drawn with.</summary>
    public static Quaternion DrawOrientation(Quaternion partOrientation, DegradeMode mode, Vector3 viewerHeading) => mode switch
    {
        DegradeMode.Billboard => SetVectorHeading(partOrientation, viewerHeading),
        DegradeMode.AxisAlignedX => RotateAroundAxisToVector(partOrientation, 0, viewerHeading),
        DegradeMode.AxisAlignedY => RotateAroundAxisToVector(partOrientation, 1, viewerHeading),
        DegradeMode.AxisAlignedZ => RotateAroundAxisToVector(partOrientation, 2, viewerHeading),
        _ => partOrientation,
    };

    /// <summary>
    /// Retail <c>Frame::set_vector_heading</c>: the orientation whose local +Y points along <paramref name="heading"/>,
    /// local X horizontal (yaw from the horizontal direction, pitch = asin(z), no roll); unchanged when the heading is
    /// shorter than <see cref="Epsilon"/>. Uses the effects track's port (<see cref="EffectActor.SetVectorHeading"/>,
    /// with its documented <c>atan2</c> argument order).
    /// </summary>
    public static Quaternion SetVectorHeading(Quaternion orientation, Vector3 heading) =>
        EffectActor.SetVectorHeading(new AcFrame(Vector3.Zero, orientation), heading).Orientation;

    /// <summary>
    /// Retail <c>Frame::rotate_around_axis_to_vector(axis, dir)</c>: keeps local axis <paramref name="axis"/> (0 = X,
    /// 1 = Y, 2 = Z) and turns the frame about it so that the next axis in the cycle Z→X→Y→Z (local Z for X, local X for Y,
    /// local Y for Z) points along the component of <paramref name="dir"/> perpendicular to the kept axis; the third axis
    /// is their cross product. Unchanged when that component is shorter than <see cref="Epsilon"/> or the result is not
    /// perpendicular within <see cref="PerpendicularTolerance"/>.
    /// </summary>
    public static Quaternion RotateAroundAxisToVector(Quaternion orientation, int axis, Vector3 dir)
    {
        if (axis is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be 0 (X), 1 (Y) or 2 (Z).");
        }

        // Retail keeps the local-to-global matrix: row i is the world direction of local axis i.
        Span<Vector3> axes = [LocalAxis(orientation, 0), LocalAxis(orientation, 1), LocalAxis(orientation, 2)];
        ReadOnlySpan<int> previous = [2, 0, 1];
        var kept = axes[axis];
        var along = Vector3.Dot(kept, dir);
        var normal = dir - (kept * along);
        var length = normal.Length();
        normal = length < Epsilon ? axes[previous[axis]] : normal / length;
        if (MathF.Abs(Vector3.Dot(normal, kept)) > PerpendicularTolerance)
        {
            return orientation;
        }

        var p = previous[axis];
        axes[p] = normal;
        axes[previous[p]] = Vector3.Cross(normal, kept);

        // Frame::cache_quaternion: back to a quaternion from the three axes.
        var m = new Matrix4x4(
            axes[0].X, axes[0].Y, axes[0].Z, 0f,
            axes[1].X, axes[1].Y, axes[1].Z, 0f,
            axes[2].X, axes[2].Y, axes[2].Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>World direction of local axis <paramref name="axis"/> (0 = X, 1 = Y, 2 = Z) under <paramref name="orientation"/>.</summary>
    public static Vector3 LocalAxis(Quaternion orientation, int axis) => axis switch
    {
        0 => Vector3.Transform(Vector3.UnitX, orientation),
        1 => Vector3.Transform(Vector3.UnitY, orientation),
        2 => Vector3.Transform(Vector3.UnitZ, orientation),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };
}
