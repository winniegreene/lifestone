using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.Rendering;

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
    private static Vector3 Direction(double degreesFromForward, double aroundForward)
    {
        // Relative to a camera looking north with up = +Z: 0 around is to the right (+X), 90 up (+Z).
        var a = degreesFromForward * Math.PI / 180.0;
        return new Vector3(
            (float)(Math.Sin(a) * Math.Cos(aroundForward * Math.PI / 180.0)),
            (float)Math.Cos(a),
            (float)(Math.Sin(a) * Math.Sin(aroundForward * Math.PI / 180.0)));
    }

    [Theory]
    [InlineData(179.885f)] // the portal zoom's widest lens (f = 0.001, D31)
    [InlineData(179.99f)]
    public void Fields_of_view_up_to_just_below_180_degrees_make_a_cone_in_front_of_the_eye(float degrees)
    {
        var f = ViewFrustum.FromCamera(North, Vector3.UnitZ, degrees, 16f / 9f, marginDegrees: 0f);
        Assert.True(f.ContainsDirection(North));
        Assert.True(f.ContainsDirection(Direction(80, 90)));   // 80 degrees up
        Assert.True(f.ContainsDirection(Direction(80, 0)));    // 80 degrees right
        Assert.False(f.ContainsDirection(Direction(95, 90)));  // past the plane through the eye
        Assert.False(f.ContainsDirection(-North));
        foreach (var plane in f.PlanesAt(Vector3.Zero))
        {
            Assert.True(float.IsFinite(plane.Normal.X) && float.IsFinite(plane.Normal.Y) && float.IsFinite(plane.Normal.Z));
            Assert.Equal(1f, plane.Normal.Length(), 4);
        }
    }

    [Theory]
    [InlineData(180f)]
    [InlineData(float.NaN)]
    public void A_field_of_view_of_180_degrees_or_more_is_rejected(float degrees) =>
        Assert.Throws<ArgumentException>(() => ViewFrustum.FromCamera(North, Vector3.UnitZ, degrees, 1f));

    [Fact]
    public void The_widest_lens_still_sees_a_ray_at_the_edge_of_a_wide_screen()
    {
        // Half angles of 89.94 degrees (vertical) and 89.97 degrees (16:9) stay under the cap, margin or not.
        var lens = CameraLens.FromPortal(new PortalLens(0.001f, true));
        var f = ViewFrustum.FromLens(North, Vector3.UnitZ, lens, 16f / 9f);
        Assert.True(f.ContainsDirection(Direction(89.95, 90)));
        Assert.True(f.ContainsDirection(Direction(89.99, 0)));
        Assert.False(f.ContainsDirection(Direction(90.5, 90)));
    }

    [Fact]
    public void A_perspective_lens_gives_the_same_pyramid_as_the_field_of_view()
    {
        Assert.Equal(
            ViewFrustum.FromCamera(new Vector3(1f, 2f, 0.3f), Vector3.UnitZ, 60f, 16f / 9f),
            ViewFrustum.FromLens(new Vector3(1f, 2f, 0.3f), Vector3.UnitZ, CameraLens.Perspective(60f, 0.1f), 16f / 9f));
    }

    [Theory]
    [InlineData(1.7320508f)]
    [InlineData(0.4f)]
    [InlineData(0.01f)]
    public void A_centred_frustum_lens_is_the_pyramid_of_its_field_of_view(float focal)
    {
        var portal = new PortalLens(focal, true);
        var fromLens = ViewFrustum.FromLens(North, Vector3.UnitZ, CameraLens.FromPortal(portal), 16f / 9f);
        var fromFov = ViewFrustum.FromCamera(North, Vector3.UnitZ, (float)portal.VerticalFovDegrees, 16f / 9f);
        foreach (var (a, b) in new[] { (fromLens.Left, fromFov.Left), (fromLens.Right, fromFov.Right), (fromLens.Top, fromFov.Top), (fromLens.Bottom, fromFov.Bottom) })
        {
            Assert.True((a - b).Length() < 2e-3f, $"f={focal}: {a} vs {b}");
        }
    }

    [Fact]
    public void An_offset_frustum_sees_more_on_the_side_it_moves_towards()
    {
        // Shifted right by half its width: the left edge is on the view axis, the right edge one width away.
        var lens = CameraLens.Frustum(2f, 1f, offsetX: 1.5f); // width = 2 x aspect 1.5 = 3: the rectangle spans tangent 0 to 3
        var f = ViewFrustum.FromLens(North, Vector3.UnitZ, lens, 1.5f, marginDegrees: 0f);
        var straightRight = new Vector3(0.5f, 1f, 0f);           // tangent 0.5: inside (the rectangle spans tangent 0 to 3)
        var farRight = new Vector3(2.9f, 1f, 0f);                // inside
        var left = new Vector3(-0.2f, 1f, 0f);                   // outside: left of the axis
        Assert.True(f.ContainsDirection(straightRight));
        Assert.True(f.ContainsDirection(farRight));
        Assert.False(f.ContainsDirection(left));
        Assert.False(f.ContainsDirection(new Vector3(3.2f, 1f, 0f)));
        Assert.True(f.ContainsDirection(new Vector3(0.5f, 1f, 0.9f)) && !f.ContainsDirection(new Vector3(0.5f, 1f, 1.1f)));
    }
}
