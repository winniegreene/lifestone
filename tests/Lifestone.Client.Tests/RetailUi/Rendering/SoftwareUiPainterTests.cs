using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>Graphic::Draw on the software painter: cropping, tiling from the tiling offset, dirty pieces, the default graphic.</summary>
public sealed class SoftwareUiPainterTests
{
    [Fact]
    public void An_image_as_large_as_the_element_is_cropped_from_its_top_left_never_stretched()
    {
        var (painter, surface) = Painter(32, 32);
        var image = FakeImageSource.Coordinates(1, 16, 16);

        painter.DrawImage(image, UiBlitMode.SrcCopy, 1f, new UiBox(10, 10, 19, 19), new UiBox(12, 13, 15, 16), (0, 0));

        Assert.Equal(Coord(2, 3), surface.GetArgb(12, 13));
        Assert.Equal(Coord(5, 6), surface.GetArgb(15, 16));
        Assert.Equal(0u, surface.GetArgb(11, 13));
        Assert.Equal(0u, surface.GetArgb(16, 16));
    }

    [Fact]
    public void An_image_smaller_than_the_element_tiles_from_the_elements_corner()
    {
        var (painter, surface) = Painter(20, 20);
        var image = FakeImageSource.Coordinates(1, 4, 4);

        painter.DrawImage(image, UiBlitMode.SrcCopy, 1f, new UiBox(2, 2, 11, 11), new UiBox(2, 2, 11, 11), (0, 0));

        Assert.Equal(Coord(0, 0), surface.GetArgb(2, 2));
        Assert.Equal(Coord(3, 0), surface.GetArgb(5, 2));
        Assert.Equal(Coord(0, 0), surface.GetArgb(6, 6));
        Assert.Equal(Coord(1, 2), surface.GetArgb(11, 8));
        Assert.Equal(0u, surface.GetArgb(12, 12));
    }

    [Theory]
    [InlineData(1, 2, 0, 0, 1, 2)]
    [InlineData(1, 2, 5, 6, 2, 0)]
    [InlineData(-1, 0, 0, 0, 3, 0)]
    [InlineData(-5, -7, 0, 0, 3, 1)]
    public void The_tiling_offset_shifts_where_tiling_starts(int offsetX, int offsetY, int x, int y, int imageX, int imageY)
    {
        var (painter, surface) = Painter(10, 10);
        var image = FakeImageSource.Coordinates(1, 4, 4);

        painter.DrawImage(image, UiBlitMode.SrcCopy, 1f, new UiBox(0, 0, 9, 9), new UiBox(0, 0, 9, 9), (offsetX, offsetY));

        Assert.Equal(Coord(imageX, imageY), surface.GetArgb(x, y));
    }

    [Fact]
    public void A_tiling_offset_tiles_even_an_image_larger_than_the_element()
    {
        var (painter, surface) = Painter(10, 10);
        var image = FakeImageSource.Coordinates(1, 16, 16);

        painter.DrawImage(image, UiBlitMode.SrcCopy, 1f, new UiBox(0, 0, 9, 9), new UiBox(0, 0, 9, 9), (14, 0));

        Assert.Equal(Coord(14, 0), surface.GetArgb(0, 0));
        Assert.Equal(Coord(0, 0), surface.GetArgb(2, 0));
    }

    [Fact]
    public void Only_the_dirty_piece_is_painted()
    {
        var (painter, surface) = Painter(10, 10);
        var image = FakeImageSource.Coordinates(1, 4, 4);

        painter.DrawImage(image, UiBlitMode.SrcCopy, 1f, new UiBox(0, 0, 9, 9), new UiBox(5, 5, 6, 6), (0, 0));

        Assert.Equal(Coord(1, 1), surface.GetArgb(5, 5));
        Assert.Equal(Coord(2, 2), surface.GetArgb(6, 6));
        Assert.Equal(0u, surface.GetArgb(4, 5));
        Assert.Equal(0u, surface.GetArgb(7, 6));
    }

    [Fact]
    public void A_missing_image_draws_retails_default_graphic_logged_once()
    {
        var log = new RecordingLogSink();
        var painter = new SoftwareUiPainter(new FakeImageSource(), log);
        var surface = new UiSurface(40, 40);
        painter.Begin(surface);

        painter.DrawImage(new UiImageRef(0x06001234, UiBlitMode.SrcCopy), 1f, new UiBox(0, 0, 39, 39), new UiBox(0, 0, 39, 39), (0, 0));
        painter.DrawImage(new UiImageRef(0x06001234, UiBlitMode.SrcCopy), 1f, new UiBox(0, 0, 39, 39), new UiBox(0, 0, 39, 39), (0, 0));

        Assert.Equal(UiDefaultGraphic.FillArgb, surface.GetArgb(0, 0));
        Assert.Equal(UiDefaultGraphic.PlaceholderBorderArgb, surface.GetArgb(30, 0));
        Assert.Equal(UiDefaultGraphic.FillArgb, surface.GetArgb(32, 0));
        Assert.Equal([0x06001234u], painter.MissingImages);
        Assert.Single(log.Lines, l => l.Contains("0x06001234", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pending_image_draws_nothing_and_is_reported()
    {
        var images = new FakeImageSource();
        images.Pending.Add(0x06000001);
        var painter = new SoftwareUiPainter(images);
        var surface = new UiSurface(4, 4);
        painter.Begin(surface);

        painter.DrawImage(new UiImageRef(0x06000001, UiBlitMode.SrcCopy), 1f, new UiBox(0, 0, 3, 3), new UiBox(0, 0, 3, 3), (0, 0));

        Assert.True(painter.HadPendingImage);
        Assert.Equal(0u, surface.GetArgb(0, 0));
    }

    [Fact]
    public void Fill_writes_the_colour_and_not_color_bits_inverts_the_colour_keeping_alpha_both_clipped()
    {
        var (painter, surface) = Painter(4, 4);

        painter.Fill(new UiBox(-2, -2, 1, 1), 0x80102030);
        painter.NotColorBits(new UiBox(1, 1, 9, 9));

        Assert.Equal(0x80102030u, surface.GetArgb(0, 0));
        Assert.Equal(0x80EFDFCFu, surface.GetArgb(1, 1));
        Assert.Equal(0x00FFFFFFu, surface.GetArgb(3, 3));
    }

    private static (SoftwareUiPainter Painter, UiSurface Surface) Painter(int width, int height)
    {
        var painter = new SoftwareUiPainter(new FakeImageSource());
        var surface = new UiSurface(width, height);
        painter.Begin(surface);
        return (painter, surface);
    }

    private static uint Coord(int x, int y) => 0xFF000000u | ((uint)x << 8) | (uint)y;
}
