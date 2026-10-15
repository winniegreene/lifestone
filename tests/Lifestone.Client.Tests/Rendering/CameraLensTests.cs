using Lifestone.Client.PortalSpace;
using Lifestone.Client.Rendering;

namespace Lifestone.Client.Tests.Rendering;

/// <summary>
/// <see cref="CameraLens"/>: the engine-agnostic projection the interiors, the sky view and picking read (M2 W2-pre(a)),
/// and its tie to the portal zoom's <see cref="PortalLens"/> (D31).
/// </summary>
public sealed class CameraLensTests
{
    private const float Aspect = 16f / 9f;

    [Fact]
    public void A_perspective_lens_is_symmetric_and_keeps_its_height()
    {
        var (left, right, bottom, top) = CameraLens.Perspective(60f, 0.1f).Sides(Aspect);
        var half = MathF.Tan(MathF.PI / 6f);
        Assert.Equal(half, top, 5);
        Assert.Equal(-half, bottom, 5);
        Assert.Equal(half * Aspect, right, 4);
        Assert.Equal(-half * Aspect, left, 4);
    }

    [Fact]
    public void A_frustum_lens_is_its_size_at_the_near_plane_and_an_offset_moves_it_without_resizing()
    {
        var centred = CameraLens.Frustum(0.5f, 0.1f).Sides(2f);
        AssertSides((-5f, 5f, -2.5f, 2.5f), centred);

        // The offset is in the near plane's units: 0.1 right and 0.05 down at near 0.1 is one tangent right, half a tangent down.
        var shifted = CameraLens.Frustum(0.5f, 0.1f, 0.1f, -0.05f).Sides(2f);
        AssertSides((-4f, 6f, -3f, 2f), shifted);
        Assert.Equal(centred.Right - centred.Left, shifted.Right - shifted.Left, 4);
        Assert.Equal(centred.Top - centred.Bottom, shifted.Top - shifted.Bottom, 4);
    }

    private static void AssertSides((float Left, float Right, float Bottom, float Top) expected, (float Left, float Right, float Bottom, float Top) actual)
    {
        Assert.Equal(expected.Left, actual.Left, 4);
        Assert.Equal(expected.Right, actual.Right, 4);
        Assert.Equal(expected.Bottom, actual.Bottom, 4);
        Assert.Equal(expected.Top, actual.Top, 4);
    }

    [Theory]
    [InlineData(1.7320508f)] // the game camera's 60 degrees
    [InlineData(0.4f)]
    [InlineData(0.01f)]
    [InlineData(0.001f)] // fully collapsed: 179.885 degrees
    public void The_portal_zoom_is_the_frustum_of_its_size_at_its_near_plane_and_spans_its_field_of_view(float focal)
    {
        var portal = new PortalLens(focal, true);
        var lens = CameraLens.FromPortal(portal);
        Assert.True(lens.IsFrustum);
        Assert.True(lens.IsValid);
        Assert.Equal(portal.Near, lens.Near);
        Assert.Equal(portal.FrustumHeightAtNear, lens.FrustumHeight);

        // The half-height tangent is the focal distance's reciprocal (retail: vdst = 1 / tan(fov / 2)).
        var (_, _, bottom, top) = lens.Sides(Aspect);
        Assert.InRange(top * focal, 0.9999f, 1.0001f); // the frustum height is 0.5 only to about 1e-4 (PortalLens)
        Assert.InRange(-bottom * focal, 0.9999f, 1.0001f);
        Assert.Equal(portal.VerticalFovDegrees, lens.EffectiveVerticalFovDegrees, 2);
    }

    [Fact]
    public void Invalid_lenses_are_refused_by_the_frusta_that_use_them()
    {
        Assert.False(CameraLens.Perspective(0f, 0.1f).IsValid);
        Assert.False(CameraLens.Perspective(180f, 0.1f).IsValid);
        Assert.False(CameraLens.Perspective(60f, 0f).IsValid);
        Assert.False(CameraLens.Frustum(0f, 0.1f).IsValid);
        Assert.False(CameraLens.Frustum(0.5f, -1f).IsValid);
        Assert.False(CameraLens.Frustum(float.NaN, 0.1f).IsValid);
        Assert.Throws<ArgumentException>(() => CameraLens.Frustum(0.5f, 0f).Sides(Aspect));
        Assert.Throws<ArgumentException>(() => CameraLens.Perspective(60f, 0.1f).Sides(0f));
    }
    [Fact]
    public void The_whole_window_needs_no_offset_and_a_frame_centre_puts_the_axis_at_its_fraction_of_the_window()
    {
        Assert.Equal((0f, 0f), CameraLens.OffsetForCentre(0.5f, Aspect));

        // A frame centred three quarters across and a quarter down: the offset shifts the rectangle left (by a quarter of the
        // width, height x aspect = 2) and down (by a quarter of the height), so the axis lands three quarters across, a quarter down.
        var (x, y) = CameraLens.OffsetForCentre(1f, 2f, 0.75f, 0.25f);
        Assert.Equal(-0.5f, x, 5);
        Assert.Equal(-0.25f, y, 5);

        // Applied to a lens, the sides keep their width and the axis (tangent 0) sits at the asked fractions of the window.
        var lens = CameraLens.Frustum(1f, 1f, x, y);
        var (left, right, bottom, top) = lens.Sides(2f);
        Assert.Equal(2f, right - left, 5);
        Assert.Equal(0.75f, (0f - left) / (right - left), 5);
        Assert.Equal(0.25f, (top - 0f) / (top - bottom), 5);
    }
}
