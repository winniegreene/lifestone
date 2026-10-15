using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Data.Motion;

/// <summary>
/// The retail <c>Position</c> and <c>Frame</c> arithmetic the position managers use, on <see cref="CellFrame"/> and
/// <see cref="AcFrame"/> values (re-expressed from the 11.4186 decompile; names and addresses cited, nothing copied).
/// Positions in different landblocks are compared through <see cref="LandDefs.GetBlockOffset"/>, so a seam between
/// landblocks is continuous. Addresses are 11.4186 / 11.6096. Allocation-free.
/// </summary>
public static class PositionFrames
{
    private const double RadiansToDegrees = 57.29577951308232;
    private const double DegreesToRadians = 0.0174532925199433;
    private const double SmallLength = 0.00019999999;

    /// <summary>
    /// Retail <c>Position::get_offset</c>: the vector from <paramref name="from"/> to <paramref name="to"/>, in the axes
    /// of <paramref name="from"/>'s landblock.
    /// </summary>
    public static Vector3 Offset(in CellFrame from, in CellFrame to) =>
        LandDefs.GetBlockOffset(from.CellId, to.CellId) + to.Origin - from.Origin;

    /// <summary>Retail <c>Position::distance</c> (0x005A94B0 / 0x005AA560): the length of <see cref="Offset"/>.</summary>
    public static float Distance(in CellFrame from, in CellFrame to)
    {
        var d = Offset(from, to);
        return (float)Math.Sqrt(((double)d.X * d.X) + ((double)d.Y * d.Y) + ((double)d.Z * d.Z));
    }

    /// <summary>
    /// Retail <c>Position::subtract2</c> (0x00555A90 / 0x005566B0), with <c>Frame::subtract2</c> (0x00535650 /
    /// 0x00536390) and <c>Position::localtolocal</c>: the frame that takes <paramref name="current"/> to
    /// <paramref name="target"/>, in <paramref name="current"/>'s local axes. Its origin is the target's origin seen from
    /// the current position (across landblocks); its rotation is <c>conj(current) ⊗ target</c>, so combining it onto the
    /// current frame (<see cref="AcFrame.Combine(in AcFrame, in AcFrame)"/>) gives the target's orientation.
    /// </summary>
    public static AcFrame Subtract2(in CellFrame target, in CellFrame current)
    {
        var inverse = Quaternion.Conjugate(current.Orientation);
        var origin = Vector3.Transform(Offset(current, target), inverse);
        var rotation = AcFrame.SetRotate(AcFrame.Hamilton(inverse, target.Orientation), Quaternion.Identity);
        return new AcFrame(origin, rotation);
    }

    /// <summary>
    /// Retail <c>Position::heading</c> (0x005A9520 / 0x005AA5D0): the compass heading in degrees from
    /// <paramref name="from"/> towards <paramref name="to"/>, measured on the horizontal part of the offset (across
    /// landblocks); 0 when that part is shorter than 0.0002. The decompile prints <c>atan2(x, y)</c>; the arguments are
    /// read as <c>atan2(y, x)</c>, the order that makes north 0 and east 90 (as <see cref="Heading"/> and ACE do).
    /// </summary>
    public static float HeadingTo(in CellFrame from, in CellFrame to)
    {
        var d = Offset(from, to);
        double x = d.X;
        double y = d.Y;
        var length = Math.Sqrt((x * x) + (y * y));
        if (length < SmallLength)
        {
            return 0f;
        }

        return (float)((450.0 - (Math.Atan2(y / length, x / length) * RadiansToDegrees)) % 360.0);
    }

    /// <summary>
    /// Retail <c>Position::cylinder_distance</c> (0x005A97F0 / 0x005AA8A0): the gap between two upright cylinders
    /// (radius, height, standing at their positions). The horizontal reach is the distance between the origins less
    /// both radii; the vertical gap is the lower top's distance below the higher base. Overlapping vertically, the result
    /// is the reach when positive, else minus the length of (gap, reach); apart vertically, the length of (gap, reach)
    /// when the reach is positive, else the gap.
    /// </summary>
    public static float CylinderDistance(float r1, float h1, in CellFrame p1, float r2, float h2, in CellFrame p2)
    {
        var reach = (double)(float)(Distance(p1, p2) - (r1 + r2));
        double gap;
        if (p1.Origin.Z <= p2.Origin.Z)
        {
            gap = p2.Origin.Z - (double)(p1.Origin.Z + h1);
        }
        else
        {
            gap = p1.Origin.Z - (double)(p2.Origin.Z + h2);
        }

        if (gap <= 0.0)
        {
            return reach <= 0.0 ? (float)-Math.Sqrt((gap * gap) + (reach * reach)) : (float)reach;
        }

        return reach <= 0.0 ? (float)gap : (float)Math.Sqrt((gap * gap) + (reach * reach));
    }

    /// <summary>
    /// Retail <c>Position::cylinder_distance_no_z</c> (0x005A98B0 / 0x005AA960): the distance between the origins less
    /// both radii (the offset's full length, despite the name).
    /// </summary>
    public static float CylinderDistanceNoZ(float r1, in CellFrame p1, float r2, in CellFrame p2) => Distance(p1, p2) - (r1 + r2);

    /// <summary>
    /// Retail <c>Frame::get_heading</c> (0x00535760 / 0x005364A0): the compass heading in degrees of the frame's forward
    /// (+Y) axis, 0 = north, 90 = east: <c>fmod(450 − atan2(y, x) × 180/π, 360)</c> of that axis.
    /// </summary>
    public static float Heading(Quaternion orientation)
    {
        var forward = Vector3.Transform(Vector3.UnitY, orientation);
        return (float)((450.0 - (Math.Atan2(forward.Y, forward.X) * RadiansToDegrees)) % 360.0);
    }

    /// <summary>
    /// Retail <c>Frame::set_heading</c> (0x00535E40 / 0x00536B80) through <c>Frame::set_vector_heading</c>: an
    /// orientation whose forward axis has the compass heading <paramref name="degrees"/>, built as retail's Euler
    /// rotation (order 0: about X by the pitch, then about Z by −heading; any roll is lost). The direction is
    /// <c>(sin h, cos h, z)</c> with <c>z</c> the vertical part of <paramref name="orientation"/>'s forward axis, so a
    /// level frame stays level and a pitched one comes out slightly flatter, as in retail. A degenerate direction keeps
    /// <paramref name="orientation"/>.
    /// </summary>
    public static Quaternion WithHeading(Quaternion orientation, float degrees)
    {
        var forward = Vector3.Transform(Vector3.UnitY, orientation);
        var radians = degrees * DegreesToRadians;
        double x = (float)Math.Sin(radians);
        double y = (float)Math.Cos(radians);
        double z = forward.Z;
        var length = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (length < SmallLength)
        {
            return orientation;
        }

        x /= length;
        y /= length;
        z /= length;
        var heading = (450.0 - (Math.Atan2(y, x) * RadiansToDegrees)) % 360.0;
        var yaw = -heading * DegreesToRadians;
        var pitch = Math.Asin(Math.Clamp(z, -1.0, 1.0));
        var cy = Math.Cos(yaw * 0.5);
        var sy = Math.Sin(yaw * 0.5);
        var cp = Math.Cos(pitch * 0.5);
        var sp = Math.Sin(pitch * 0.5);

        // Rz(yaw) ⊗ Rx(pitch), Hamilton.
        var q = new Quaternion((float)(cy * sp), (float)(sy * sp), (float)(sy * cp), (float)(cy * cp));
        return AcFrame.SetRotate(q, orientation);
    }
}
