using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS3: the zoom maths of <c>Render::set_vdst</c> and <c>gmSmartBoxUI::UseTime</c> (m2-log-in.md §4.6), and the
/// <see cref="PortalLens"/> a presenter applies.
/// </summary>
public sealed class PortalZoomTests
{
    /// <summary>f_game of Lifestone's 60° main camera: 1 / tan(30°) = √3.</summary>
    private static readonly float Sixty = PortalZoom.FocalFromVerticalFov(Math.PI / 3.0);

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.7320508f)]
    [InlineData(0.01f)]
    public void Focal_distance_and_field_of_view_round_trip(float focal)
    {
        var fov = PortalZoom.VerticalFovRadians(focal);

        Assert.Equal(2.0 * Math.Atan(1.0 / focal), fov, 3e-7); // the FOV is handed on as a float
        Assert.Equal(focal, PortalZoom.FocalFromVerticalFov(fov), focal * 1e-5f);
    }

    [Fact]
    public void The_main_camera_s_sixty_degrees_is_focal_root_three()
    {
        Assert.Equal(MathF.Sqrt(3f), Sixty, 1e-6f);
        Assert.Equal(60.0, new PortalLens(Sixty, Overridden: false).VerticalFovDegrees, 4);
    }

    [Fact]
    public void Fully_collapsed_the_field_of_view_is_about_179_885_degrees()
    {
        var degrees = new PortalLens((float)PortalZoom.CollapsedFocal, Overridden: true).VerticalFovDegrees;

        Assert.Equal(179.8854, degrees, 3);
        Assert.True(degrees > 179.0, "the collapse goes past Godot's 179 degree perspective cap (D31: use a frustum)");
    }

    [Fact]
    public void The_field_of_view_is_clamped_to_0_001_and_pi_as_a_float()
    {
        Assert.Equal((double)(float)Math.PI, PortalZoom.VerticalFovRadians(0f)); // atan2(1, 0) = pi/2
        Assert.Equal((double)(float)Math.PI, PortalZoom.VerticalFovRadians(-1f)); // atan2(1, -1) = 3 pi/4: above pi, clamped
        Assert.Equal((double)0.001f, PortalZoom.VerticalFovRadians(1e6f)); // 2 atan(1e-6) < 0.001
        Assert.Equal((double)0.001f, PortalZoom.VerticalFovRadians(float.NaN));
        var justInside = PortalZoom.VerticalFovRadians(1999f); // 2 atan(1/1999) is a hair above 0.001
        Assert.True(justInside > 0.001);
        Assert.Equal((double)(float)(2.0 * Math.Atan(1.0 / 1999.0)), justInside);
    }

    [Fact]
    public void The_near_plane_follows_the_zoom_below_0_4()
    {
        var belowThreshold = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(0.4f) - 1);

        Assert.Equal(0.4f, PortalZoom.NearThreshold);
        Assert.Equal(0.1f, PortalZoom.NearPlane(0.4f));
        Assert.Equal(belowThreshold * 0.25f, PortalZoom.NearPlane(belowThreshold));
        Assert.Equal(0.1f, PortalZoom.NearPlane(Sixty));
        Assert.Equal(0.00025f, PortalZoom.NearPlane(0.001f));
        Assert.Equal(0.1f, new PortalLens(Sixty, Overridden: false).Near);
    }

    [Fact]
    public void Collapse_runs_from_f_game_to_0_001_and_expand_back()
    {
        foreach (var game in new[] { Sixty, 1f, 1.3549f, 0.5f })
        {
            Assert.Equal(game, PortalZoom.Collapse(game, 0.0));
            Assert.Equal(0.001f, PortalZoom.Collapse(game, 1.0));
            Assert.Equal(0.001f, PortalZoom.Expand(game, 0.0));
            Assert.Equal(game, PortalZoom.Expand(game, 1.0));
        }
    }

    [Fact]
    public void The_midpoints_ease_with_anim_level()
    {
        // L(0.5) = 512 / 1024: the focal distance is halfway, in retail's double arithmetic rounded to a float.
        Assert.Equal((float)((0.5 * (0.001 - Sixty)) + Sixty), PortalZoom.Collapse(Sixty, 0.5));
        Assert.Equal((float)((0.5 * (Sixty - 0.001)) + 0.001), PortalZoom.Expand(Sixty, 0.5));

        // A quarter of the way in time is 146 / 1024 of the way in focal distance.
        Assert.Equal((float)((146 * AnimLevel.Unit * (Sixty - 0.001)) + 0.001), PortalZoom.Expand(Sixty, 0.25));
    }

    [Fact]
    public void Time_past_the_state_holds_the_end_and_time_before_it_the_start()
    {
        Assert.Equal(PortalZoom.Collapse(Sixty, 1.0), PortalZoom.Collapse(Sixty, 7.5));
        Assert.Equal(PortalZoom.Expand(Sixty, 1.0), PortalZoom.Expand(Sixty, double.PositiveInfinity));
        Assert.Equal(PortalZoom.Expand(Sixty, 0.0), PortalZoom.Expand(Sixty, -0.25));
    }

    [Fact]
    public void Collapsing_shrinks_the_view_monotonically()
    {
        var previous = float.MaxValue;
        for (var k = 0; k <= 100; k++)
        {
            var focal = PortalZoom.Collapse(Sixty, k / 100.0);
            Assert.True(focal <= previous, $"t = {k / 100.0}: {focal} > {previous}");
            previous = focal;
        }
    }

    [Theory]
    [InlineData(0.001f)]
    [InlineData(0.39999998f)]
    public void Below_0_4_the_frustum_is_half_a_metre_tall_at_its_near_plane(float focal)
    {
        var lens = new PortalLens(focal, Overridden: true);

        Assert.Equal(0.5f, lens.FrustumHeightAtNear, 1e-4f);
        Assert.Equal(focal * 0.25f, lens.Near);
    }

    [Fact]
    public void Above_0_4_the_frustum_height_is_0_2_over_f()
    {
        foreach (var focal in new[] { 0.4f, 1f, Sixty, 10f })
        {
            Assert.Equal(0.2f / focal, PortalZoom.FrustumHeightAtNear(focal), 1e-6f);
        }
    }

    [Fact]
    public void Retail_f_game_is_the_focal_distance_of_its_world_view_s_vertical_fov()
    {
        // SmartBox::GetOverrideFovDistance and SmartBox::RenderNormalMode use the same gameFOV / (aspect - 0.1), so the
        // zoom ends on retail's normal view. At the default pi/2 and 4:3 that is about 72.97 degrees.
        var vertical = PortalZoom.RetailGameVerticalFov(PortalZoom.RetailDefaultGameFov, 4.0 / 3.0);

        Assert.Equal(0x3FC90FDBu, BitConverter.SingleToUInt32Bits(PortalZoom.RetailDefaultGameFov));
        Assert.Equal(72.97, vertical * 180.0 / Math.PI, 2);
        Assert.Equal(1.0 / Math.Tan(vertical / 2.0), PortalZoom.RetailGameFocal(PortalZoom.RetailDefaultGameFov, 4.0 / 3.0), 12);
        Assert.Equal((float)PortalZoom.RetailGameFocal(PortalZoom.RetailDefaultGameFov, 4.0 / 3.0), PortalZoom.FocalFromVerticalFov(vertical), 1e-6f);
    }

    [Fact]
    public void A_default_lens_has_focal_0_and_the_widest_view()
    {
        var lens = default(PortalLens);

        Assert.False(lens.Overridden);
        Assert.Equal((double)(float)Math.PI, lens.VerticalFovRadians);
        Assert.Equal(0f, lens.Near);
    }
}
