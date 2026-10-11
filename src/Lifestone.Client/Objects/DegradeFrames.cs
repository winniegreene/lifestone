// Re-expressed from the retail client's draw-frame behaviour (acclient 11.4186 decompile, Mag-nus/acclient-AI-RE):
// CPhysicsPart::calc_draw_frame, Frame::set_vector_heading, Frame::euler_set_rotate (order 0) and
// Frame::rotate_around_axis_to_vector. No decompiled code is copied. The heading convention (atan2(y, x), Hex-Rays prints
// the fpatan operands swapped) is the one the scenery track verified on the x87 code (Lifestone.Data RetailFrame).

using System.Numerics;
using Lifestone.Data.Models;

namespace Lifestone.Client.Objects;

/// <summary>
/// The orientation retail draws a part with at a LOD level whose degrade mode is not <see cref="DegradeMode.Normal"/>
/// (camera-facing impostors): <c>CPhysicsPart::calc_draw_frame</c>.
/// </summary>
/// <remarks>
/// <para><c>calc_draw_frame</c> copies the part's frame and then, by <c>deg_mode</c>:</para>
/// <list type="bullet">
/// <item>2: <c>Frame::set_vector_heading(viewer_heading)</c>: the rotation is replaced by Euler (x = asin(h.z),
/// y = 0, z = −heading) in order 0 (static XYZ, R = Rz·Rx), so the part's local +Y points along the viewer→part
/// direction (pitched), whatever the part's own rotation was.</item>
/// <item>3, 4, 5: <c>Frame::rotate_around_axis_to_vector(0 | 1 | 2, viewer_heading)</c>: the part keeps its local
/// X, Y or Z axis and turns about it so the previous axis in the cycle (Z for X, X for Y, Y for Z) points along the
/// component of the viewer→part direction perpendicular to that axis; the third axis is their cross product.</item>
/// </list>
/// <para><c>viewer_heading</c> is the unit vector from the viewer to the part's scaled sort centre, or to the object
/// origin when the object is far (<see cref="LandblockObjectLod"/>); (0, 0, 1) when the distance is ≤ 0.0002 m.
/// The frame's origin never changes.</para>
/// </remarks>
public static class DegradeFrames
{
    /// <summary>Retail's small-vector threshold (<c>AC1Legacy::Vector3::normalize_check_small</c>, float 0.0002).</summary>
    public const float SmallLength = 0.0002f;

    /// <summary><c>rotate_around_axis_to_vector</c> accepts the new axis when |new · axis| ≤ 0.0002 × 5.</summary>
    public const float PerpendicularTolerance = SmallLength * 5f;

    /// <summary>The rotation a part at a level of mode <paramref name="mode"/> is drawn with.</summary>
    /// <param name="mode">The level's degrade mode.</param>
    /// <param name="partRotation">The part's own rotation (object rotation ⊗ placement rotation).</param>
    /// <param name="viewerHeading">Unit vector from the viewer to the part (retail <c>viewer_heading</c>).</param>
    public static Quaternion DrawRotation(DegradeMode mode, Quaternion partRotation, Vector3 viewerHeading) => mode switch
    {
        DegradeMode.Billboard => SetVectorHeading(partRotation, viewerHeading),
        DegradeMode.AxisAlignedX => RotateAroundAxisToVector(partRotation, 0, viewerHeading),
        DegradeMode.AxisAlignedY => RotateAroundAxisToVector(partRotation, 1, viewerHeading),
        DegradeMode.AxisAlignedZ => RotateAroundAxisToVector(partRotation, 2, viewerHeading),
        _ => partRotation, // 1, and calc_draw_frame's default: the part's own frame
    };

    /// <summary>
    /// <c>Frame::set_vector_heading</c>: a rotation whose local +Y points along <paramref name="heading"/>; the current
    /// rotation is returned unchanged when the heading is shorter than <see cref="SmallLength"/>.
    /// </summary>
    public static Quaternion SetVectorHeading(Quaternion current, Vector3 heading)
    {
        if (!NormalizeCheckSmall(ref heading))
        {
            return current;
        }

        // z = -fmod(450 - atan2(y, x) in degrees, 360) in radians (C# % is C fmod); x = asin(z component).
        var z = -((450.0 - (Math.Atan2(heading.Y, heading.X) * (180.0 / Math.PI))) % 360.0) * (Math.PI / 180.0);
        var x = Math.Asin(Math.Clamp(heading.Z, -1f, 1f));
        return EulerOrder0(x, z);
    }

    /// <summary>
    /// <c>Frame::rotate_around_axis_to_vector(axis, dir)</c>: turns the rotation about its own local axis
    /// <paramref name="axis"/> (0 = X, 1 = Y, 2 = Z) towards <paramref name="direction"/>.
    /// </summary>
    public static Quaternion RotateAroundAxisToVector(Quaternion current, int axis, Vector3 direction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(axis);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(axis, 2);

        // Columns of the local-to-global matrix: the images of local X, Y and Z.
        Span<Vector3> columns =
        [
            Vector3.Transform(Vector3.UnitX, current),
            Vector3.Transform(Vector3.UnitY, current),
            Vector3.Transform(Vector3.UnitZ, current),
        ];
        var previous = Previous(axis);
        var m = columns[axis];
        var normal = direction - (m * Vector3.Dot(m, direction));
        if (!NormalizeCheckSmall(ref normal))
        {
            normal = columns[previous];
        }

        if (MathF.Abs(Vector3.Dot(normal, m)) > PerpendicularTolerance)
        {
            return current;
        }

        columns[previous] = normal;
        columns[Previous(previous)] = Vector3.Cross(normal, m);
        return FromColumns(columns[0], columns[1], columns[2]);
    }

    /// <summary>The previous axis in the cycle X → Z, Y → X, Z → Y (retail's <c>prev[] = {2, 0, 1}</c>).</summary>
    private static int Previous(int axis) => axis switch
    {
        0 => 2,
        1 => 0,
        _ => 1,
    };

    /// <summary>
    /// <c>Frame::euler_set_rotate(x, 0, z, order 0)</c>: Shoemake EulOrdXYZs with y = 0, i.e. R = Rz(z) · Rx(x)
    /// (rotate about X first, then about the static Z).
    /// </summary>
    private static Quaternion EulerOrder0(double x, double z)
    {
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)x);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)z);
        return Quaternion.Normalize(qz * qx);
    }

    /// <summary><c>Frame::cache_quaternion</c>: the rotation whose matrix has these columns (orthonormal).</summary>
    private static Quaternion FromColumns(Vector3 x, Vector3 y, Vector3 z)
    {
        // System.Numerics rotation matrices are row-vector: row i is the image of basis vector i.
        var matrix = new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    /// <summary>
    /// <c>AC1Legacy::Vector3::normalize_check_small</c>, with the opposite sense: false (vector unchanged) when shorter
    /// than <see cref="SmallLength"/>, else true with the vector normalised.
    /// </summary>
    private static bool NormalizeCheckSmall(ref Vector3 v)
    {
        var length = v.Length();
        if (length < SmallLength)
        {
            return false;
        }

        v /= length;
        return true;
    }
}
