using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Selection;

namespace Lifestone.Client.Tests.RetailUi.Selection;

/// <summary>
/// The vivid target indicator's arithmetic (docs/retail-ui.md section 21.7): the eight arrow sectors, the on-screen frame (grown by one corner, 8 pixels from the edge), the off-screen arrow
/// position by heading, the heading of a rectangle outside the view, and the colorize that tints the pictures.
/// </summary>
public sealed class VividTargetMathTests
{
    private const int W = 1920;
    private const int H = 1080;

    [Theory]
    [InlineData(0.0, 6)]
    [InlineData(22.9, 6)]
    [InlineData(337.9, 5)]
    [InlineData(338.0, 6)]
    [InlineData(359.0, 6)]
    [InlineData(23.0, 7)]
    [InlineData(67.9, 7)]
    [InlineData(68.0, 9)]
    [InlineData(90.0, 9)]
    [InlineData(112.9, 9)]
    [InlineData(113.0, 12)]
    [InlineData(157.9, 12)]
    [InlineData(158.0, 11)]
    [InlineData(180.0, 11)]
    [InlineData(203.0, 10)]
    [InlineData(247.9, 10)]
    [InlineData(248.0, 8)]
    [InlineData(270.0, 8)]
    [InlineData(293.0, 5)]
    [InlineData(300.0, 5)]
    public void The_off_screen_arrow_is_picked_by_the_heading_sector(double heading, int image) =>
        Assert.Equal(image, VividTargetMath.OffScreenImage(heading));

    [Fact]
    public void The_on_screen_frame_grows_by_one_corner_outward()
    {
        // A box 400..520 by 300..420 with 12 pixel corners: the frame spans 388..532 by 288..432.
        var frame = VividTargetMath.OnScreenFrame(400, 300, 520, 420, 12, 12, W, H);
        Assert.Equal((388, 288, 144, 144), frame);
    }

    [Fact]
    public void The_on_screen_frame_stays_eight_pixels_inside_the_view()
    {
        // A box that reaches past the top-left corner: the frame starts at 8.
        var topLeft = VividTargetMath.OnScreenFrame(-50, -40, 30, 20, 12, 12, W, H);
        Assert.Equal(8, topLeft.X);
        Assert.Equal(8, topLeft.Y);

        // A box past the bottom-right corner: the frame's far edge is 8 pixels in (view - corner - 8, plus the corner).
        var bottomRight = VividTargetMath.OnScreenFrame(1900, 1060, 2100, 1200, 12, 12, W, H);
        Assert.Equal(W - 8, bottomRight.X + bottomRight.Width);
        Assert.Equal(H - 8, bottomRight.Y + bottomRight.Height);
        Assert.True(bottomRight.Width >= 12 && bottomRight.Height >= 12);
    }

    [Fact]
    public void A_projected_rectangle_is_rounded_to_pixels()
    {
        var frame = VividTargetMath.OnScreenFrame(new ScreenRect(400.4f, 300.6f, 119.7f, 120.2f), 12, 12, W, H);
        Assert.Equal(VividTargetMath.OnScreenFrame(400, 301, 520, 421, 12, 12, W, H), frame);
    }

    [Theory]
    [InlineData(0.0, 948, 8)] // ahead: the top, centred (1920 / 2 - 12)
    [InlineData(90.0, 1888, 528)] // right: the right edge, centred (1920 - 8 - 24; 1080 / 2 - 12)
    [InlineData(180.0, 948, 1048)] // behind: the bottom, centred
    [InlineData(270.0, 8, 528)] // left: the left edge, centred
    public void The_off_screen_arrow_sits_on_the_border_at_the_headings_axes(double heading, int x, int y)
    {
        var (px, py) = VividTargetMath.OffScreenPosition(heading, 24, 24, W, H);
        Assert.InRange(px, x - 1, x + 1);
        Assert.InRange(py, y - 1, y + 1);
    }

    [Fact]
    public void The_off_screen_arrow_slides_along_the_border_with_the_headings_tangent_and_is_clamped_inside()
    {
        // 30 degrees: on the top edge, tan(30) * half the view height to the right of the centre.
        var (x, y) = VividTargetMath.OffScreenPosition(30.0, 24, 24, W, H);
        Assert.Equal(8, y);
        Assert.InRange(x, 960 - 12 + 311, 960 - 12 + 313);

        // 44 degrees would be 520 pixels right of the centre (inside); 45 is the corner of a 2:1 view's cone, still on the top edge.
        var (x45, y45) = VividTargetMath.OffScreenPosition(45.0, 24, 24, W, H);
        Assert.Equal(8, y45);
        Assert.InRange(x45, 960 - 12 + 539, 960 - 12 + 541);

        // Every heading ends inside the view with the 8 pixel margin.
        for (var heading = 0.0; heading < 360.0; heading += 7.5)
        {
            var (px, py) = VividTargetMath.OffScreenPosition(heading, 24, 24, W, H);
            Assert.InRange(px, 8, W - 8 - 24);
            Assert.InRange(py, 8, H - 8 - 24);
        }
    }

    [Fact]
    public void The_heading_of_a_rectangle_outside_the_view_is_its_screen_angle_from_the_centre()
    {
        Assert.InRange(VividTargetMath.HeadingOf(new ScreenRect(2400, 500, 100, 80), W, H), 85.0, 95.0);
        Assert.InRange(VividTargetMath.HeadingOf(new ScreenRect(910, -600, 100, 100), W, H), -0.01, 5.0); // above: ahead
        Assert.InRange(VividTargetMath.HeadingOf(new ScreenRect(910, 1500, 100, 100), W, H), 175.0, 185.0); // below
        Assert.InRange(VividTargetMath.HeadingOf(new ScreenRect(-900, 500, 100, 80), W, H), 265.0, 275.0); // left
        Assert.Equal(180.0, VividTargetMath.HeadingOf(default, W, H)); // behind the camera: the empty rectangle points back
    }

    [Fact]
    public void Colorize_paints_a_grey_picture_in_the_colours_hue_at_each_pixels_brightness_and_keeps_black_and_alpha()
    {
        // Pixels are R, G, B, A.
        var source = new UiImage(0, 3, 1, UiPixelFormat.Argb, [128, 128, 128, 255, 0, 0, 0, 255, 255, 255, 255, 40]);
        var gold = new UiRgbaColor(1f, 0.67f, 0f, 1f);
        var tinted = VividColorize.Apply(source, gold);
        var p = tinted.Pixels;

        // Mid grey (brightness 129 of 256): red at full brightness, green at 67 percent of it, no blue.
        Assert.Equal(128, p[0]);
        Assert.InRange((int)p[1], 84, 88);
        Assert.Equal(0, p[2]);
        Assert.Equal(255, p[3]);

        // Black stays black.
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, p[4..8]);

        // White becomes the colour itself; the translucent pixel keeps its alpha.
        Assert.Equal(255, p[8]);
        Assert.InRange((int)p[9], 168, 172);
        Assert.Equal(0, p[10]);
        Assert.Equal(40, p[11]);

        // The source is not changed.
        Assert.Equal(128, source.Pixels[0]);
    }

    [Fact]
    public void Colorize_with_a_grey_colour_leaves_a_grey_picture_grey()
    {
        var source = new UiImage(0, 1, 1, UiPixelFormat.Argb, [200, 200, 200, 255]);
        var p = VividColorize.Apply(source, new UiRgbaColor(1f, 1f, 1f, 1f)).Pixels;
        Assert.Equal(200, p[0]);
        Assert.Equal(200, p[1]);
        Assert.Equal(200, p[2]);
    }
}
