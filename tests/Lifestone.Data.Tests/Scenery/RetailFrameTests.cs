using System.Numerics;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Data.Tests.Scenery;

/// <summary>Retail Frame heading math: convention (0 = north, clockwise) and pitch preservation.</summary>
public sealed class RetailFrameTests
{
    private static Vector3 Forward(RetailFrame frame) => Vector3.Transform(Vector3.UnitY, frame.Orientation);

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(90f, 1f, 0f)]
    [InlineData(180f, 0f, -1f)]
    [InlineData(270f, -1f, 0f)]
    [InlineData(45f, 0.70710677f, 0.70710677f)]
    public void Set_heading_turns_the_forward_axis_clockwise_from_north(float degrees, float east, float north)
    {
        var frame = RetailFrame.FromStored(new Vector3(1, 2, 3), Quaternion.Identity);
        frame.SetHeading(degrees);
        var forward = Forward(frame);
        Assert.Equal(east, forward.X, 5);
        Assert.Equal(north, forward.Y, 5);
        Assert.Equal(0f, forward.Z, 6);
        Assert.Equal(new Vector3(1, 2, 3), frame.Origin);

        // A pure rotation of -degrees about +Z.
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -degrees * MathF.PI / 180f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, frame.Orientation)) > 0.999999f, $"{frame.Orientation} vs {expected}");
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1f, 0f, 90f)]
    [InlineData(0f, -1f, 180f)]
    [InlineData(-1f, 0f, 270f)]
    [InlineData(1f, 1f, 45f)]
    [InlineData(0.00001f, 0.00001f, 0f)]
    public void Vector_heading_is_compass_degrees_and_zero_for_tiny_vectors(float x, float y, double expected)
    {
        Assert.Equal(expected, RetailFrame.GetHeading(new Vector3(x, y, 5f)), 4);
    }

    [Fact]
    public void Set_heading_keeps_the_pitch_of_a_tilted_base_orientation()
    {
        // An arbitrary tilt of about 3 degrees under a yaw. The real tilted BaseLoc orientations, read from the dats,
        // are checked in SceneryTiltedOrientationTests.
        var tilted = Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f) * Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 0.3f, 0f)), 0.05f));
        var baseForward = Vector3.Transform(Vector3.UnitY, tilted);
        var frame = RetailFrame.FromStored(Vector3.Zero, tilted);
        frame.SetHeading(30f);

        // set_heading normalises (sin d, cos d, forward.z of the base) and pitches about X by asin of its z.
        var forward = Forward(frame);
        var pitchZ = baseForward.Z / MathF.Sqrt(1f + (baseForward.Z * baseForward.Z));
        Assert.Equal(pitchZ, forward.Z, 6);
        Assert.Equal(30.0, RetailFrame.GetHeading(forward), 3);              // heading replaced
        Assert.Equal(0f, Vector3.Transform(Vector3.UnitX, frame.Orientation).Z, 6); // no roll: X stays horizontal
    }

    [Fact]
    public void Cached_matrix_is_the_rotation_of_the_quaternion()
    {
        var q = Quaternion.Normalize(new Quaternion(0.1f, -0.2f, 0.3f, 0.9f));
        var frame = RetailFrame.FromStored(new Vector3(10, 20, 30), q);
        var m = frame.Matrix;
        var r = Matrix4x4.CreateFromQuaternion(q); // row-vector convention: row i is the image of axis i
        Assert.Equal(r.M11, m[0], 6);
        Assert.Equal(r.M12, m[1], 6);
        Assert.Equal(r.M13, m[2], 6);
        Assert.Equal(r.M21, m[3], 6);
        Assert.Equal(r.M22, m[4], 6);
        Assert.Equal(r.M23, m[5], 6);
        Assert.Equal(r.M31, m[6], 6);
        Assert.Equal(r.M32, m[7], 6);
        Assert.Equal(r.M33, m[8], 6);

        var local = new Vector3(1.5f, -2f, 0.25f);
        var expected = new Vector3(10, 20, 30) + Vector3.Transform(local, q);
        Assert.True(Vector3.Distance(expected, frame.LocalToGlobal(local)) < 1e-5f);
    }

    [Fact]
    public void Frame_validity_follows_retail_tolerance()
    {
        Assert.True(RetailFrame.IsValidQuaternion(Quaternion.Identity));
        Assert.True(RetailFrame.IsValidQuaternion(new Quaternion(0, 0, 0, 1.0004f)));   // |norm^2 - 1| = 0.0008
        Assert.False(RetailFrame.IsValidQuaternion(new Quaternion(0, 0, 0, 1.001f)));   // 0.002 > 0.001
        Assert.False(RetailFrame.IsValidQuaternion(new Quaternion(float.NaN, 0, 0, 1)));
    }
}
