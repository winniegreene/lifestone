using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics;

/// <summary>
/// The retail <c>Frame</c>, <c>Position</c> and <c>Plane</c> tests and heading functions the client's movement code uses
/// (the player's command interpreter and the server-position policy, step PH7), re-expressed from the 11.4186 decompile
/// with their 11.4186 and 11.6096 addresses; nothing copied.
/// </summary>
public static class RetailFrames
{
    private const double RadiansToDegrees = 57.29577951308232;

    /// <summary>
    /// Retail <c>Frame::get_heading</c> (11.4186 0x00535760, 11.6096 0x005364A0) as <c>CPhysicsObj::get_heading</c>
    /// (11.4186 0x00511540, 11.6096 0x00512010) reads it: the compass heading in degrees of the frame's local +Y axis,
    /// 0 north and 90 east (clockwise), <c>fmod(450 - atan2(y, x) in degrees, 360)</c> over that axis's x and y.
    /// </summary>
    /// <remarks>
    /// The decompile prints the arctangent's operands as <c>(x, y)</c>; north (0, 1) must give 0, so they are read as
    /// <c>atan2(y, x)</c>, as <see cref="SetHeading"/> reads <c>Frame::set_vector_heading</c> (docs/physics.md §10). The
    /// result of <c>fmod</c> keeps the dividend's sign, so a heading vector a little east of north gives a value just
    /// under 360 rather than a negative one.
    /// </remarks>
    public static float GetHeading(in AcFrame frame)
    {
        var heading = RetailHeadings.GetVectorHeading(frame);
        return (float)((450.0 - (Math.Atan2(heading.Y, heading.X) * RadiansToDegrees)) % 360.0);
    }

    /// <summary>
    /// Retail <c>Frame::set_heading</c> (11.4186 0x00535E40, 11.6096 0x00536B80): the frame turned to face the compass
    /// heading <paramref name="degrees"/> (0 north, clockwise), keeping its origin and its heading vector's height.
    /// </summary>
    public static AcFrame SetHeading(in AcFrame frame, float degrees) => RetailHeadings.SetHeading(frame, degrees);

    /// <summary>
    /// Retail <c>Frame::is_equal</c> (11.4186 0x00424C30, 11.6096 0x00424E90): origins within <see cref="PhysicsGlobals.Epsilon"/>
    /// on every axis (<c>Vector3Math::AreEqual</c>, 0x00424CE0, 0x00424F40, which compares with &lt;=) and quaternion
    /// components strictly within it (<c>Frame::is_quaternion_equal</c>, 0x00424C70, 0x00424ED0).
    /// </summary>
    public static bool FrameIsEqual(in AcFrame a, in AcFrame b) =>
        MathF.Abs(a.Origin.X - b.Origin.X) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.Origin.Y - b.Origin.Y) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.Origin.Z - b.Origin.Z) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.Orientation.W - b.Orientation.W) < PhysicsGlobals.Epsilon
        && MathF.Abs(a.Orientation.X - b.Orientation.X) < PhysicsGlobals.Epsilon
        && MathF.Abs(a.Orientation.Y - b.Orientation.Y) < PhysicsGlobals.Epsilon
        && MathF.Abs(a.Orientation.Z - b.Orientation.Z) < PhysicsGlobals.Epsilon;

    /// <summary>
    /// Retail <c>Plane::operator==</c> as the command interpreter's translation unit has it (11.4186 0x006B3DD0, 11.6096
    /// 0x006B4D10): normals within <see cref="PhysicsGlobals.Epsilon"/> on every axis (&lt;=) and distances strictly within
    /// it. <c>ShouldSendPositionEvent</c> compares the player's contact plane with the last one sent this way.
    /// </summary>
    public static bool PlaneEquals(in Plane a, in Plane b) =>
        MathF.Abs(a.Normal.X - b.Normal.X) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.Normal.Y - b.Normal.Y) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.Normal.Z - b.Normal.Z) <= PhysicsGlobals.Epsilon
        && MathF.Abs(a.D - b.D) < PhysicsGlobals.Epsilon;

    /// <summary>
    /// Retail <c>Frame::IsValid</c> (11.4186 0x00534ED0, 11.6096 0x00535C10): no coordinate is NaN and the quaternion's
    /// squared length is within <c>5 × </c><see cref="PhysicsGlobals.Epsilon"/> of 1.
    /// </summary>
    public static bool FrameIsValid(in AcFrame frame)
    {
        var o = frame.Origin;
        var q = frame.Orientation;
        if (float.IsNaN(o.X) || float.IsNaN(o.Y) || float.IsNaN(o.Z)
            || float.IsNaN(q.W) || float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z))
        {
            return false;
        }

        var norm = (q.Z * q.Z) + (q.Y * q.Y) + (q.X * q.X) + (q.W * q.W);
        return !float.IsNaN(norm) && !(0.00019999999 * 5.0 < Math.Abs(norm - 1.0));
    }

    /// <summary>
    /// Retail <c>Position::IsValid</c> (11.4186 0x005A9480, 11.6096 0x005AA530): a valid cell id
    /// (<c>LandDefs::inbound_valid_cellid</c>, 11.4186 0x004979A0, 11.6096 0x00497C80: <see cref="LandDefs.IsValidCellId"/>
    /// in a landblock on the map, <see cref="LandDefs.IsOnMap"/>) and a valid frame (<see cref="FrameIsValid"/>).
    /// <c>CommandInterpreter::SendPositionEvent</c> sends nothing for an invalid position.
    /// </summary>
    public static bool PositionIsValid(in CellFrame position) =>
        LandDefs.IsValidCellId(position.CellId)
        && LandDefs.IsOnMap(Terrain.LandblockCoord.FromId(position.CellId))
        && FrameIsValid(position.Frame);
}
