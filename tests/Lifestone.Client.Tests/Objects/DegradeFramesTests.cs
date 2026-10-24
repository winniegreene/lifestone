using System.Numerics;
using Lifestone.Client.Objects;
using Lifestone.Data.Models;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// The camera-facing draw frames of <c>CPhysicsPart::calc_draw_frame</c>, checked against their geometric meaning
/// (the retail functions are re-expressed, not copied, so the tests state what each one must do).
/// </summary>
public sealed class DegradeFramesTests
{
    private const float Tolerance = 2e-5f;

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 10.0)]
    [InlineData(180.0, -30.0)]
    [InlineData(270.0, 20.0)]
    [InlineData(359.5, 60.0)]
    public void Set_vector_heading_points_local_y_along_the_heading_without_roll(double compassDegrees, double pitchDegrees)
    {
        var heading = Heading(compassDegrees, pitchDegrees);
        var someRotation = Quaternion.CreateFromYawPitchRoll(0.3f, -1.1f, 2.0f); // discarded by mode 2
        var q = DegradeFrames.DrawRotation(DegradeMode.Billboard, someRotation, heading * 7f);

        AssertClose(heading, Vector3.Transform(Vector3.UnitY, q));
        Assert.InRange(Vector3.Transform(Vector3.UnitX, q).Z, -Tolerance, Tolerance); // Euler (x, 0, z): no roll
        Assert.True(Vector3.Transform(Vector3.UnitZ, q).Z >= -Tolerance); // the up axis never flips below the horizon
    }

    [Fact]
    public void Set_vector_heading_of_heading_zero_is_the_identity()
    {
        var q = DegradeFrames.SetVectorHeading(Quaternion.Identity, Vector3.UnitY);
        AssertClose(Vector3.UnitX, Vector3.Transform(Vector3.UnitX, q));
        AssertClose(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, q));
        AssertClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, q));
    }

    [Fact]
    public void A_tiny_heading_keeps_the_current_rotation()
    {
        var current = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.0f);
        Assert.Equal(current, DegradeFrames.SetVectorHeading(current, new Vector3(0.0001f, 0f, 0f)));
        Assert.Equal(current, DegradeFrames.RotateAroundAxisToVector(current, 2, new Vector3(0f, 0.0001f, 0f)));
    }

    [Fact]
    public void Mode_five_turns_about_local_z_so_local_y_faces_away_from_the_viewer()
    {
        // Identity part, viewer due west of it: the viewer -> part direction is +X.
        var q = DegradeFrames.DrawRotation(DegradeMode.AxisAlignedZ, Quaternion.Identity, Vector3.UnitX);
        AssertClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, q));
        AssertClose(Vector3.UnitX, Vector3.Transform(Vector3.UnitY, q));
        AssertClose(-Vector3.UnitY, Vector3.Transform(Vector3.UnitX, q)); // X = Y x Z, right-handed
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rotate_around_axis_keeps_the_axis_and_aims_the_previous_axis(int axis)
    {
        var part = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, 0.25f, -0.6f));
        var direction = Vector3.Normalize(new Vector3(0.3f, -0.8f, 0.2f));
        var q = DegradeFrames.RotateAroundAxisToVector(part, axis, direction);

        var unit = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        var kept = Vector3.Transform(unit[axis], part);
        AssertClose(kept, Vector3.Transform(unit[axis], q));

        // The previous axis in retail's cycle (X <- Z, Y <- X, Z <- Y) takes the direction's component perpendicular to the axis.
        var previous = axis switch { 0 => 2, 1 => 0, _ => 1 };
        var expected = Vector3.Normalize(direction - (kept * Vector3.Dot(kept, direction)));
        AssertClose(expected, Vector3.Transform(unit[previous], q));

        // Still a proper rotation.
        var x = Vector3.Transform(Vector3.UnitX, q);
        var y = Vector3.Transform(Vector3.UnitY, q);
        AssertClose(Vector3.Transform(Vector3.UnitZ, q), Vector3.Cross(x, y));
    }

    [Fact]
    public void A_direction_along_the_axis_leaves_the_rotation_unchanged()
    {
        var part = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var q = DegradeFrames.RotateAroundAxisToVector(part, 2, Vector3.UnitZ);
        AssertClose(Vector3.Transform(Vector3.UnitX, part), Vector3.Transform(Vector3.UnitX, q));
        AssertClose(Vector3.Transform(Vector3.UnitY, part), Vector3.Transform(Vector3.UnitY, q));
    }

    [Fact]
    public void Normal_mode_draws_the_part_frame()
    {
        var part = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        Assert.Equal(part, DegradeFrames.DrawRotation(DegradeMode.Normal, part, Vector3.UnitX));
    }

    /// <summary>A unit vector of compass heading (0 = +Y, 90 = +X) and pitch (up positive).</summary>
    private static Vector3 Heading(double compassDegrees, double pitchDegrees)
    {
        var h = compassDegrees * Math.PI / 180.0;
        var p = pitchDegrees * Math.PI / 180.0;
        return new Vector3((float)(Math.Sin(h) * Math.Cos(p)), (float)(Math.Cos(h) * Math.Cos(p)), (float)Math.Sin(p));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tolerance * 10f, $"expected {expected}, got {actual}");
    }
}
