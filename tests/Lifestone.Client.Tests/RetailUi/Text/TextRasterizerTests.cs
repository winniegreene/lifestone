using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Text;

/// <summary>
/// SurfaceWindow::DrawCharacter: placement by the glyph's offsets, the advance rules, A8 tinting versus blitting a colour
/// atlas, the background (outline) atlas grown by the border, clipping to the window, glyph 0 and the '?' fallback.
/// </summary>
public sealed class TextRasterizerTests
{
    private const uint White = 0xFFFFFFFF;
    private const uint Red = 0xFFFF0000;

    [Fact]
    public void An_alpha_glyph_is_tinted_at_the_pen_plus_its_offsets()
    {
        var surface = new UiSurface(20, 20);
        var font = new FakeFont();

        var advance = TextRasterizer.DrawCharacter(surface, Window(surface), 3, 4, font, 'A', Red, UiTextFlags.AdvanceWithOffsets, 0);

        Assert.Equal(7, advance);
        Assert.Equal(Red, surface.GetArgb(4, 6));
        Assert.Equal(Red, surface.GetArgb(8, 13));
        Assert.Equal(0u, surface.GetArgb(3, 6));
        Assert.Equal(0u, surface.GetArgb(9, 6));
        Assert.Equal(0u, surface.GetArgb(4, 5));
        Assert.Equal(0u, surface.GetArgb(4, 14));
    }

    [Fact]
    public void Without_the_offsets_flag_the_glyph_sits_at_the_pen_and_advances_its_width()
    {
        var surface = new UiSurface(20, 20);

        var advance = TextRasterizer.DrawCharacter(surface, Window(surface), 3, 4, new FakeFont(), 'A', Red, UiTextFlags.None, 0);

        Assert.Equal(5, advance);
        Assert.Equal(Red, surface.GetArgb(3, 6));
        Assert.Equal(0u, surface.GetArgb(8, 6));
    }

    [Fact]
    public void Monospace_advances_the_widest_glyph_plus_one_and_baseline_lifts_by_the_baseline_offset()
    {
        var surface = new UiSurface(20, 20);
        var font = new FakeFont(cellWidth: 4);

        Assert.Equal(5, TextRasterizer.DrawCharacter(surface, Window(surface), 0, 12, font, 'A', Red, UiTextFlags.Monospace | UiTextFlags.Baseline, 0));
        Assert.Equal(Red, surface.GetArgb(0, 4));
        Assert.Equal(0u, surface.GetArgb(0, 3));
    }

    [Fact]
    public void A_colour_atlas_is_blitted_with_its_own_colours_unless_colorized()
    {
        var surface = new UiSurface(20, 20);
        var font = new ColourFont();

        TextRasterizer.DrawCharacter(surface, Window(surface), 0, 0, font, 'A', Red, UiTextFlags.None, 0);
        Assert.Equal(0xFF00FF00u, surface.GetArgb(0, 0));

        TextRasterizer.DrawCharacter(surface, Window(surface), 5, 0, font, 'A', Red, UiTextFlags.Colorize, 0);
        Assert.Equal(Red, surface.GetArgb(5, 0));
    }

    [Fact]
    public void The_background_atlas_is_drawn_grown_by_the_border_in_the_background_colour()
    {
        var foreground = new FakeFont(border: 1);
        var background = new UiImage(0x06000002, foreground.Foreground!.Width, foreground.Foreground.Height, UiPixelFormat.A8, Opaque(foreground.Foreground.Width, foreground.Foreground.Height));
        var font = new FakeFont(border: 1, background: background);
        var surface = new UiSurface(20, 20);

        var advance = TextRasterizer.DrawCharacter(surface, Window(surface), 5, 5, font, 'A', White,
            UiTextFlags.AdvanceWithOffsets | UiTextFlags.SkipForeground | UiTextFlags.DrawBackground, Red);
        TextRasterizer.DrawCharacter(surface, Window(surface), 5, 5, font, 'A', White, UiTextFlags.AdvanceWithOffsets, Red);

        Assert.Equal(7, advance);
        Assert.Equal(Red, surface.GetArgb(5, 6));
        Assert.Equal(Red, surface.GetArgb(11, 15));
        Assert.Equal(White, surface.GetArgb(6, 7));
        Assert.Equal(White, surface.GetArgb(10, 14));
        Assert.Equal(0u, surface.GetArgb(12, 6));
        Assert.Equal(0u, surface.GetArgb(5, 16));
    }

    [Fact]
    public void A_glyph_is_clipped_to_the_window_and_still_advances()
    {
        var surface = new UiSurface(20, 20);
        var window = new UiBox(10, 10, 13, 19);

        var advance = TextRasterizer.DrawCharacter(surface, window, 0, 0, new FakeFont(), 'A', Red, UiTextFlags.AdvanceWithOffsets, 0);
        var outside = TextRasterizer.DrawCharacter(surface, window, 40, 0, new FakeFont(), 'A', Red, UiTextFlags.AdvanceWithOffsets, 0);

        Assert.Equal(7, advance);
        Assert.Equal(7, outside);
        Assert.Equal(Red, surface.GetArgb(11, 12));
        Assert.Equal(Red, surface.GetArgb(13, 12));
        Assert.Equal(0u, surface.GetArgb(14, 12));
        Assert.Equal(0u, surface.GetArgb(9, 12));
    }

    [Fact]
    public void A_missing_character_draws_glyph_0_inside_the_range_and_the_question_mark_outside_it_and_a_font_with_neither_draws_nothing()
    {
        var surface = new UiSurface(20, 20);
        var font = new FakeFont(characters: ['a']); // 'a' and '?': the range is '?' to 'a', glyph 0 is 'a'

        Assert.True(font.TryGetCharDesc('Z', out var inside));
        Assert.Equal('a', inside.Unicode);
        Assert.True(font.TryGetCharDesc('~', out var outside));
        Assert.Equal('?', outside.Unicode);
        Assert.Equal(7, TextRasterizer.DrawCharacter(surface, Window(surface), 0, 0, font, 'Z', Red, UiTextFlags.AdvanceWithOffsets, 0));
        Assert.Equal(Red, surface.GetArgb(1, 2));
        Assert.Equal(7, TextRasterizer.DrawCharacter(surface, Window(surface), 0, 0, font, '~', Red, UiTextFlags.AdvanceWithOffsets, 0));
        Assert.Equal(0, TextRasterizer.DrawCharacter(surface, Window(surface), 0, 0, new NoGlyphFont(), 'Z', Red, UiTextFlags.AdvanceWithOffsets, 0));
    }

    private static UiBox Window(UiSurface surface) => new(0, 0, surface.Width - 1, surface.Height - 1);

    private static byte[] Opaque(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[(i * 4) + 3] = 255;
        }

        return rgba;
    }

    private sealed class ColourFont : IUiFont
    {
        public uint Did => 1;

        public int MaxCharHeight => 2;

        public int MaxCharWidth => 2;

        public int BaselineOffset => 0;

        public int NumHorizontalBorderPixels => 0;

        public int NumVerticalBorderPixels => 0;

        public UiImage? Foreground { get; } = FakeImageSource.Solid(1, 2, 2, 0xFF00FF00);

        public UiImage? Background => null;

        public bool ContainsChar(char c) => c == 'A';

        public bool TryGetCharDesc(char c, out UiGlyphDesc desc)
        {
            desc = new UiGlyphDesc('A', 0, 0, 2, 2, 0, 0, 0);
            return c == 'A';
        }
    }

    private sealed class NoGlyphFont : IUiFont
    {
        public uint Did => 2;

        public int MaxCharHeight => 2;

        public int MaxCharWidth => 2;

        public int BaselineOffset => 0;

        public int NumHorizontalBorderPixels => 0;

        public int NumVerticalBorderPixels => 0;

        public UiImage? Foreground => null;

        public UiImage? Background => null;

        public bool ContainsChar(char c) => false;

        public bool TryGetCharDesc(char c, out UiGlyphDesc desc)
        {
            desc = default;
            return false;
        }
    }
}
