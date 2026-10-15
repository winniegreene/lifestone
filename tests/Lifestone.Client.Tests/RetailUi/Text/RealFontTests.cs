using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using DataUiFont = Lifestone.Data.Ui.UiFont;
using UiGlyph = Lifestone.Client.RetailUi.Text.UiGlyph;

namespace Lifestone.Client.Tests.RetailUi.Text;

/// <summary>
/// Text with the retail fonts from the user's dats through the production font source (<see cref="UiFontsSource"/> over
/// R5's <c>UiFonts</c>; R12): advances, R5's glyph lookup (glyph 0 inside a font's range, '?' outside it), the fallback
/// fonts, wrapping, drawing.
/// </summary>
public sealed class RealFontTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint LayoutId = 0x21000001;

    [RequiresDatsFact]
    public void The_text_elements_default_font_is_FONT_enum_24_and_the_absent_FixedSys_15_is_null()
    {
        var fonts = new UiFontsSource(fixture.Dats);

        var font = fonts.GetFontByEnum(24)!;

        Assert.Equal(0x40000002u, font.Did);
        Assert.Same(font, fonts.GetFont(0x40000002));
        Assert.Same(font, fonts.GetFontByEnum(3));
        Assert.Equal(14, font.MaxCharHeight);
        Assert.Equal(UiPixelFormat.A8, font.Foreground!.Format);
        Assert.Equal((1024, 214), (font.Foreground.Width, font.Foreground.Height));
        Assert.Equal(UiPixelFormat.A8, font.Background!.Format);
        Assert.Equal(3, font.NumHorizontalBorderPixels);
        Assert.Null(fonts.GetFontByEnum(Lifestone.Data.Ui.UiFonts.FixedSys15Enum));
        Assert.Null(fonts.GetFont(0x40000033));
    }

    [RequiresDatsFact]
    public void Advances_add_both_offsets_as_a_byte_and_a_missing_character_is_glyph_0_inside_the_range_and_the_question_mark_outside_it()
    {
        var fonts = new UiFontsSource(fixture.Dats);
        var small = fonts.GetFont(0x40000002)!;
        var large = fonts.GetFont(0x40000010)!;

        Assert.True(small.TryGetCharDesc('A', out var a));
        Assert.Equal(a.Width + a.HorizontalOffsetBefore + a.HorizontalOffsetAfter, small.GetCharWidth('A'));
        Assert.Equal(7, small.GetCharWidth('A'));

        // 'A' of the large font has an after-offset of -1 (0xFF): the byte sum wraps to width + before - 1.
        Assert.True(large.TryGetCharDesc('A', out var big));
        Assert.Equal(-1, big.HorizontalOffsetAfter);
        Assert.Equal(19, large.GetCharWidth('A'));

        // Font 0x40000002 runs from U+0020 to U+FFFC: '一' is inside the range but has no glyph, so it is glyph 0, the
        // space (Font::GenerateCharacterMap); U+0001 is below the range and falls back to '?'.
        Assert.True(small.ContainsChar('一'));
        Assert.True(small.TryGetCharDesc('一', out var inside));
        Assert.Equal(' ', inside.Unicode);
        Assert.Equal(small.GetCharWidth(' '), small.GetCharWidth('一'));
        Assert.Equal(4, small.GetCharWidth('一'));
        Assert.False(small.ContainsChar('\u0001'));
        Assert.True(small.TryGetCharDesc('\u0001', out var outside));
        Assert.Equal('?', outside.Unicode);
        Assert.Equal(5, small.GetCharWidth('\u0001'));
    }

    [RequiresDatsFact]
    public void A_text_element_draws_a_character_outside_its_fonts_range_in_the_Gulim_fallback_and_one_inside_it_as_glyph_0()
    {
        var fonts = new UiFontsSource(fixture.Dats);
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 400, 40, children:
        [
            UiDescs.Element(2, UiClassIds.Text, 0, 0, 400, 40, properties: [new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("A\u0001一"))]),
        ]);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]), Fonts = fonts });
        var text = (UiTextElement)manager.CreateRootElementByDataId(LayoutId, 1)!.GetChild(2)!;

        var glyphs = text.Glyphs.Glyphs;

        Assert.Equal(3, glyphs.Count);
        Assert.Equal((0x40000002u, 7), (glyphs[0].Font!.Did, glyphs[0].Width));
        // FONT enums 20 and 22 are both Gulim 18 (0x40000018), whose range starts at U+0000.
        Assert.Equal((0x40000018u, 9), (glyphs[1].Font!.Did, glyphs[1].Width));
        Assert.Equal((0x40000002u, 4), (glyphs[2].Font!.Did, glyphs[2].Width));
    }

    [RequiresDatsFact]
    public void Real_text_wraps_at_spaces_within_the_margin()
    {
        var font = new UiFontsSource(fixture.Dats).GetFont(0x40000002)!;
        var layout = new GlyphLayout();
        var text = "Welcome to Dereth, the land of Asheron's Call";
        layout.AddText(0, [.. text.Select(c => new UiGlyph(c, font, UiRgbaColor.White, font.GetCharWidth(c), font.MaxCharHeight))]);

        layout.Recalculate(80, false, out var width, out var height);

        Assert.True(layout.Lines.Count > 2);
        Assert.True(width <= 80);
        Assert.Equal(layout.Lines.Count * 14, height);
        foreach (var line in layout.Lines.Skip(1))
        {
            Assert.Equal(' ', text[line.Index - 1]);
        }
    }

    [RequiresDatsFact]
    public void A_real_glyph_draws_with_its_outline_atlas()
    {
        var font = new UiFontsSource(fixture.Dats).GetFont(0x40000002)!;
        var surface = new UiSurface(40, 30);
        var window = new Lifestone.Data.Ui.UiBox(0, 0, 39, 29);

        TextRasterizer.DrawCharacter(surface, window, 5, 5, font, 'W', 0xFFFFFFFF,
            UiTextFlags.AdvanceWithOffsets | UiTextFlags.SkipForeground | UiTextFlags.DrawBackground, 0xFF000000);
        var advance = TextRasterizer.DrawCharacter(surface, window, 5, 5, font, 'W', 0xFFFFFFFF, UiTextFlags.AdvanceWithOffsets, 0xFF000000);

        Assert.Equal(font.GetCharWidth('W'), advance);
        var pixels = Enumerable.Range(0, 40 * 30).Select(i => surface.GetArgb(i % 40, i / 40)).ToList();
        Assert.Contains(pixels, p => p == 0xFFFFFFFF);
        Assert.Contains(pixels, p => (p & 0x00FFFFFF) == 0 && p >> 24 != 0);
    }
}

/// <summary>The font adapter's lookup on synthetic Font records (no dats): R5's rules, through the <see cref="IUiFont"/> seam.</summary>
public sealed class UiFontAdapterTests
{
    [Fact]
    public void The_adapter_answers_R5s_lookup_glyph_0_inside_the_range_and_the_question_mark_outside_it()
    {
        var record = new Font { Id = 0x40000099, MaxCharHeight = 10, MaxCharWidth = 8, BaselineOffset = 8, NumHorizontalBorderPixels = 1 };
        foreach (var (c, width) in new[] { (' ', 3), ('?', 5), ('A', 6), ('C', 7) })
        {
            record.CharDescs.Add(new FontCharDesc { Unicode = c, Width = (byte)width, Height = 10, HorizontalOffsetAfter = 1 });
        }

        var font = new UiFontAdapter(DataUiFont.FromRecord(record), null, null);

        Assert.Equal((0x40000099u, 10, 8, 8, 1), (font.Did, font.MaxCharHeight, font.MaxCharWidth, font.BaselineOffset, font.NumHorizontalBorderPixels));
        Assert.True(font.ContainsChar('B'));
        Assert.True(font.TryGetCharDesc('B', out var missing));
        Assert.Equal((' ', 3), (missing.Unicode, (int)missing.Width));
        Assert.False(font.ContainsChar('Z'));
        Assert.True(font.TryGetCharDesc('Z', out var outside));
        Assert.Equal('?', outside.Unicode);
        Assert.Equal(8, font.GetCharWidth('C'));
        Assert.Equal(4, font.GetCharWidth('B'));
    }

    [Fact]
    public void A_font_whose_range_leaves_out_the_question_mark_has_no_glyph_outside_it()
    {
        var record = new Font { Id = 0x40000098, MaxCharHeight = 10, MaxCharWidth = 8 };
        record.CharDescs.Add(new FontCharDesc { Unicode = 'a', Width = 6, Height = 10 });
        record.CharDescs.Add(new FontCharDesc { Unicode = 'c', Width = 6, Height = 10 });

        var font = new UiFontAdapter(DataUiFont.FromRecord(record), null, null);

        Assert.False(font.TryGetCharDesc('Z', out _));
        Assert.Equal(0, font.GetCharWidth('Z'));
        Assert.True(font.TryGetCharDesc('b', out var glyph0));
        Assert.Equal('a', glyph0.Unicode);
    }
}
