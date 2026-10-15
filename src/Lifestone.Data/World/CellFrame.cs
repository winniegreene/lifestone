using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;

namespace Lifestone.Data.World;

/// <summary>
/// A full retail <c>Position</c>: an object cell id (<c>objcell_id</c>) plus a frame (<c>frame</c>: origin and
/// orientation) in that cell's landblock-local space, metres from the landblock's south-west corner, AC axes (+X east,
/// +Y north, +Z up). The one server-position type shared by physics, motion, the session, readiness and the tools.
/// </summary>
/// <remarks>
/// <para>Retail keeps an EnvCell's frame in its landblock's space too, so the same origin rules apply to every cell
/// kind (<see cref="LandDefs"/>). <c>Lifestone.Client.World.CellPosition</c> is the rotation-free counterpart; the
/// Client's <c>CellFrameExtensions</c> convert between them and to global metres.</para>
/// <para>On the wire a position is the cell id, the origin x, y, z and then the quaternion in the order w, x, y, z
/// (ACE.Entity <c>Position</c>'s packet constructor and <c>Serialize</c>, ACEmulator/ACE@95ef426d);
/// <see cref="FromWire"/> takes them in that order.</para>
/// </remarks>
/// <param name="CellId">The object cell id: an outdoor cell 0x0001-0x0040, an EnvCell 0x0100-0xFFFD, or the landblock 0xFFFF.</param>
/// <param name="Frame">Origin and orientation in the cell's landblock-local space.</param>
public readonly record struct CellFrame(uint CellId, AcFrame Frame)
{
    /// <summary>Retail's small-vector length for headings (<c>normalize_check_small</c>, 0.0002).</summary>
    private const double SmallHeadingLength = 0.0002;

    /// <summary>The origin, in the landblock's local space.</summary>
    public Vector3 Origin => Frame.Origin;

    /// <summary>The orientation (a unit quaternion once <see cref="Normalized"/>).</summary>
    public Quaternion Orientation => Frame.Orientation;

    /// <summary>The landblock the cell belongs to.</summary>
    public LandblockCoord Landblock => LandblockCoord.FromId(CellId);

    /// <summary>True for an outdoor (land) cell, low word 0x0001-0x0040 (<see cref="LandDefs.IsOutdoorCell"/>).</summary>
    public bool IsOutdoor => LandDefs.IsOutdoorCell(CellId);

    /// <summary>True for an EnvCell, low word 0x0100-0xFFFD (<see cref="LandDefs.IsEnvCell"/>).</summary>
    public bool IsEnvCell => LandDefs.IsEnvCell(CellId);

    /// <summary>
    /// Compass heading of the frame's forward (+Y) axis in degrees: 0 = north, 90 = east, clockwise, in [0, 360).
    /// Computed like retail <c>AC1Legacy::Vector3::get_heading</c> (<c>fmod(450 - atan2(y, x) * 180 / pi, 360)</c>, as
    /// <c>AcCoordinates.HeadingOf</c> and the scenery <c>RetailFrame.GetHeading</c> do) on the rotated forward axis, the
    /// axis ACE's <c>AFrame.get_heading</c> reads; 0 when that axis points straight up or down (horizontal part below
    /// 0.0002).
    /// </summary>
    public double HeadingDegrees
    {
        get
        {
            var forward = Vector3.Transform(Vector3.UnitY, Frame.Orientation);
            double x = forward.X;
            double y = forward.Y;
            if (Math.Sqrt((x * x) + (y * y)) < SmallHeadingLength)
            {
                return 0.0;
            }

            var heading = (450.0 - (Math.Atan2(y, x) * (180.0 / Math.PI))) % 360.0;
            return heading < 0 ? heading + 360.0 : heading;
        }
    }

    /// <summary>
    /// Builds a position from its wire fields in wire order: the cell id, the origin, then the quaternion as
    /// <paramref name="qw"/>, <paramref name="qx"/>, <paramref name="qy"/>, <paramref name="qz"/>. The quaternion is kept
    /// as sent; call <see cref="Normalized"/> before using it as a rotation.
    /// </summary>
    public static CellFrame FromWire(uint cellId, Vector3 origin, float qw, float qx, float qy, float qz) =>
        new(cellId, new AcFrame(origin, new Quaternion(qx, qy, qz, qw)));

    /// <summary>
    /// The same position with a unit orientation, as retail <c>Frame::set_rotate</c> leaves one
    /// (<see cref="AcFrame.SetRotate"/>): the quaternion normalised, or the identity when it has no length or is not
    /// finite. The cell id and the origin are unchanged.
    /// </summary>
    public CellFrame Normalized() =>
        this with { Frame = Frame with { Orientation = AcFrame.SetRotate(Frame.Orientation, Quaternion.Identity) } };

    /// <summary>For logs: <c>0xA9B40019 [84 7.1 94] w=1 x=0 y=0 z=0</c>.</summary>
    public override string ToString()
    {
        var o = Frame.Origin;
        var q = Frame.Orientation;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{CellId:X8} [{o.X:0.###} {o.Y:0.###} {o.Z:0.###}] w={q.W:0.####} x={q.X:0.####} y={q.Y:0.####} z={q.Z:0.####}");
    }
}
