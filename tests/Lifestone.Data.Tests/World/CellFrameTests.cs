using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.World;

/// <summary><see cref="CellFrame"/>: the wire order of the quaternion, normalisation, headings and the cell kinds.</summary>
public sealed class CellFrameTests
{
    private static readonly Vector3 Holtburg = new(84f, 7.1f, 94f);

    [Fact]
    public void From_wire_takes_the_quaternion_in_w_x_y_z_order()
    {
        var frame = CellFrame.FromWire(0xA9B40019, Holtburg, qw: 0.5f, qx: 0.1f, qy: 0.2f, qz: 0.3f);

        Assert.Equal(0xA9B40019u, frame.CellId);
        Assert.Equal(Holtburg, frame.Origin);
        Assert.Equal(0.5f, frame.Orientation.W);
        Assert.Equal(0.1f, frame.Orientation.X);
        Assert.Equal(0.2f, frame.Orientation.Y);
        Assert.Equal(0.3f, frame.Orientation.Z);
        Assert.Equal(frame.Frame.Orientation, frame.Orientation);
    }

    [Fact]
    public void Normalized_makes_the_orientation_a_unit_quaternion_and_keeps_the_rest()
    {
        var frame = CellFrame.FromWire(0x01D90100, new Vector3(3f, -49.9f, 0f), 2f, 0f, 0f, 2f).Normalized();

        Assert.Equal(0x01D90100u, frame.CellId);
        Assert.Equal(new Vector3(3f, -49.9f, 0f), frame.Origin);
        Assert.Equal(1f, frame.Orientation.Length(), 6);
        Assert.Equal(MathF.Sqrt(0.5f), frame.Orientation.W, 6);
        Assert.Equal(MathF.Sqrt(0.5f), frame.Orientation.Z, 6);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 0f, 1f)]
    [InlineData(float.PositiveInfinity, 0f, 0f, 0f)]
    public void Normalized_turns_a_degenerate_quaternion_into_the_identity(float qw, float qx, float qy, float qz)
    {
        var frame = CellFrame.FromWire(0xA9B40019, Holtburg, qw, qx, qy, qz).Normalized();
        Assert.Equal(Quaternion.Identity, frame.Orientation);
        Assert.Equal(Holtburg, frame.Origin);
    }

    [Theory]
    [InlineData(0.0)] // north
    [InlineData(90.0)] // east
    [InlineData(180.0)] // south
    [InlineData(270.0)] // west
    [InlineData(45.0)]
    [InlineData(312.5)]
    public void Heading_is_the_compass_heading_retail_set_heading_gives(double heading)
    {
        // Retail Frame::set_heading(d) is a rotation of -d degrees about +Z; heading 0 faces +Y (north).
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-heading * Math.PI / 180.0));
        var frame = new CellFrame(0xA9B40019, new AcFrame(Holtburg, q));
        Assert.Equal(heading, frame.HeadingDegrees, 3);
    }

    [Fact]
    public void Heading_of_a_wire_quaternion_facing_east_is_90()
    {
        // As ACE writes a position facing east: w = cos(-45°), z = sin(-45°).
        var frame = CellFrame.FromWire(0xA9B40019, Holtburg, 0.70710677f, 0f, 0f, -0.70710677f);
        Assert.Equal(90.0, frame.HeadingDegrees, 3);
    }

    [Fact]
    public void Heading_is_0_when_forward_points_straight_up()
    {
        var up = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var frame = new CellFrame(0xA9B40019, new AcFrame(Holtburg, up));
        Assert.Equal(0.0, frame.HeadingDegrees);
    }

    [Theory]
    [InlineData(0xA9B40019u, true, false)]
    [InlineData(0x01D90100u, false, true)]
    [InlineData(0xA9B4FFFFu, false, false)]
    public void Outdoor_cells_and_EnvCells_are_told_apart(uint cellId, bool outdoor, bool envCell)
    {
        var frame = new CellFrame(cellId, AcFrame.Identity);
        Assert.Equal(outdoor, frame.IsOutdoor);
        Assert.Equal(envCell, frame.IsEnvCell);
        Assert.Equal(LandblockCoord.FromId(cellId), frame.Landblock);
    }

    [Fact]
    public void Equal_positions_are_equal_and_print_for_logs()
    {
        var a = CellFrame.FromWire(0xA9B40019, Holtburg, 1f, 0f, 0f, 0f);
        var b = new CellFrame(0xA9B40019, new AcFrame(Holtburg, Quaternion.Identity));
        Assert.Equal(a, b);
        Assert.Equal("0xA9B40019 [84 7.1 94] w=1 x=0 y=0 z=0", a.ToString());
    }
}
