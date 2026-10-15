using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Primitives;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// The retail <c>Frame</c> heading functions <c>CTransition::find_placement_pos</c> turns its probe frame with,
/// re-expressed from the 11.4186 decompile (PORTAL/cgeometry/CFrame.c) with the arithmetic as written there.
/// </summary>
internal static class RetailHeadings
{
    private const double RadiansToDegrees = 57.29577951308232;
    private const double DegreesToRadians = 0.0174532925199433;

    /// <summary>
    /// Retail <c>Frame::get_vector_heading</c> (11.4186 0x00535720, 11.6096 0x00536460): the frame's local +Y axis in its
    /// parent space.
    /// </summary>
    public static Vector3 GetVectorHeading(in AcFrame frame) => frame.LocalToGlobalVec(Vector3.UnitY);

    /// <summary>
    /// Retail <c>Frame::set_heading</c> (11.4186 0x00535E40, 11.6096 0x00536B80): <see cref="SetVectorHeading"/> with
    /// <c>(sin, cos)</c> of <paramref name="degrees"/> and the current heading vector's height.
    /// </summary>
    public static AcFrame SetHeading(in AcFrame frame, float degrees)
    {
        var radians = degrees * DegreesToRadians;
        var direction = new Vector3((float)Math.Sin(radians), (float)Math.Cos(radians), GetVectorHeading(frame).Z);
        return SetVectorHeading(frame, direction);
    }

    /// <summary>
    /// Retail <c>Frame::set_vector_heading</c> (11.4186 0x00535DB0, 11.6096 0x00536AF0): the rotation (Euler order 0, no
    /// roll) whose yaw is minus the compass heading, <c>-fmod(450 - atan2(y, x) in degrees, 360)</c>, and whose pitch is
    /// <c>asin(z)</c> of the normalised heading; a heading shorter than the epsilon leaves the frame alone.
    /// </summary>
    /// <remarks>
    /// The decompile prints the arctangent as <c>atan2(x, y)</c>, as it prints <c>Frame::get_heading</c>'s (11.4186
    /// 0x00535760, 11.6096 0x005364A0) over the heading vector's x and y; there that order must mean <c>atan2(y, x)</c>,
    /// since north (0, 1) has heading 0. The same order is read here (ACE's <c>AFrame.set_vector_heading</c> agrees), so a
    /// frame turned to a heading faces it (<c>RetailHeadingsTests</c>).
    /// </remarks>
    public static AcFrame SetVectorHeading(in AcFrame frame, Vector3 heading)
    {
        var normalized = heading;
        if (VectorMath.NormalizeCheckSmall(ref normalized))
        {
            return frame;
        }

        var yaw = (float)(-Fmod(450.0 - (Math.Atan2(normalized.Y, normalized.X) * RadiansToDegrees), 360.0) * DegreesToRadians);
        var pitch = (float)(Math.Asin(normalized.Z) * RadiansToDegrees * DegreesToRadians);
        return frame with { Orientation = EulerSetRotate(pitch, 0f, yaw, frame.Orientation) };
    }

    /// <summary>
    /// Retail <c>Frame::euler_set_rotate</c> (11.4186 0x00535BF0, 11.6096 0x00536930) for order 0 (X, then Z about the
    /// static axes; Y unused here): <c>q = (cos z/2 cos x/2, cos z/2 sin x/2, sin z/2 sin x/2, sin z/2 cos x/2)</c> as
    /// (w, x, y, z), renormalised by <c>Frame::set_rotate</c>.
    /// </summary>
    public static Quaternion EulerSetRotate(float x, float y, float z, Quaternion previous)
    {
        _ = y;
        var cosX = (float)Math.Cos(x * 0.5);
        var sinX = (float)Math.Sin(x * 0.5);
        var cosZ = (float)Math.Cos(z * 0.5);
        var sinZ = (float)Math.Sin(z * 0.5);
        return AcFrame.SetRotate(new Quaternion(sinX * cosZ, sinZ * sinX, cosX * sinZ, cosZ * cosX), previous);
    }

    /// <summary>C's <c>fmod</c>: the remainder with the dividend's sign.</summary>
    private static double Fmod(double dividend, double divisor) => dividend % divisor;
}
