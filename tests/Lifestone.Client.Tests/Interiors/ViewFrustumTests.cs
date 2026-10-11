using System.Numerics;
using Lifestone.Client.Interiors;

namespace Lifestone.Client.Tests.Interiors;

public sealed class ViewFrustumTests
{
    private static readonly Vector3 North = Vector3.UnitY;

    [Fact]
    public void The_view_axis_is_inside_and_the_back_is_outside()
    {
        var f = ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 16f / 9f, marginDegrees: 0f);
        Assert.True(f.ContainsDirection(North));
        Assert.False(f.ContainsDirection(-North));
        Assert.False(f.ContainsDirection(Vector3.UnitX)); // 90° to the right
        Assert.False(f.ContainsDirection(Vector3.UnitZ)); // straight up
    }

    [Theory]
    [InlineData(29.0, true)]
    [InlineData(31.0, false)]
    public void The_vertical_half_angle_is_half_the_field_of_view(double degrees, bool inside)
    {
        var f = ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 16f / 9f, marginDegrees: 0f);
        var r = degrees * Math.PI / 180.0;
        Assert.Equal(inside, f.ContainsDirection(new Vector3(0f, (float)Math.Cos(r), (float)Math.Sin(r))));
        Assert.Equal(inside, f.ContainsDirection(new Vector3(0f, (float)Math.Cos(r), -(float)Math.Sin(r))));
    }

    [Fact]
    public void The_horizontal_half_angle_follows_the_aspect_ratio()
    {
        // KEEP_HEIGHT: tan(h) = aspect × tan(v).
        var f = ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 2f, marginDegrees: 0f);
        var half = Math.Atan(2.0 * Math.Tan(30.0 * Math.PI / 180.0));
        Vector3 At(double a) => new((float)Math.Sin(a), (float)Math.Cos(a), 0f);
        Assert.True(f.ContainsDirection(At(half - 0.01)));
        Assert.False(f.ContainsDirection(At(half + 0.01)));
        Assert.True(f.ContainsDirection(At(-half + 0.01)));
        Assert.False(f.ContainsDirection(At(-half - 0.01)));
    }

    [Fact]
    public void The_margin_widens_every_side()
    {
        var exact = ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 1f, marginDegrees: 0f);
        var wide = ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 1f, marginDegrees: 2f);
        var r = 31.0 * Math.PI / 180.0;
        var up = new Vector3(0f, (float)Math.Cos(r), (float)Math.Sin(r));
        Assert.False(exact.ContainsDirection(up));
        Assert.True(wide.ContainsDirection(up));
    }

    [Fact]
    public void Planes_pass_through_the_eye_and_face_the_view_axis()
    {
        var f = ViewFrustum.FromCamera(new Vector3(1f, 1f, -0.2f), Vector3.UnitZ, 70f, 1.5f);
        var eye = new Vector3(100f, -40f, 12f);
        foreach (var plane in f.PlanesAt(eye))
        {
            Assert.Equal(0f, plane.Distance(eye), 3);
            Assert.True(plane.Distance(eye + (f.Forward * 10f)) > 0f);
            Assert.Equal(1f, plane.Normal.Length(), 4);
        }
    }

    [Fact]
    public void Degenerate_input_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ViewFrustum.FromCamera(Vector3.UnitZ, Vector3.UnitZ, 60f, 1f));
        Assert.Throws<ArgumentException>(() => ViewFrustum.FromCamera(North, Vector3.UnitZ, 0f, 1f));
        Assert.Throws<ArgumentException>(() => ViewFrustum.FromCamera(North, Vector3.UnitZ, 60f, 0f));
    }
}
