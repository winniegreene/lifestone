using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;

namespace Lifestone.Client.Tests.RetailUi.Text;

/// <summary>
/// Line breaking (GlyphList::Recalculate with LinebreakUtils) and the run's bookkeeping, worked out by hand with letters
/// 6 pixels wide, spaces 3, punctuation 2 and newlines 6 (a newline gets the font's '?' advance in retail).
/// </summary>
public sealed class GlyphLayoutTests
{
    [Fact]
    public void A_line_breaks_after_whitespace_and_leaves_the_last_whitespace_out_of_its_width()
    {
        var layout = Layout("aaa bbb");

        layout.Recalculate(30, false, out var width, out var height);

        Assert.Equal([new UiGlyphLine(0, 18, 12), new UiGlyphLine(4, 18, 12)], layout.Lines);
        Assert.Equal(18, width);
        Assert.Equal(24, height);
    }

    [Fact]
    public void Only_the_whitespace_glyph_before_the_break_is_left_out()
    {
        var layout = Layout("aaa  bbb");

        layout.Recalculate(30, false, out _, out _);

        Assert.Equal(new UiGlyphLine(0, 21, 12), layout.Lines[0]);
        Assert.Equal(5, layout.Lines[1].Index);
    }

    [Fact]
    public void A_word_longer_than_the_line_breaks_before_the_glyph_that_overflows()
    {
        var layout = Layout("aaaaaaaaaa");

        layout.Recalculate(25, false, out var width, out _);

        Assert.Equal([new UiGlyphLine(0, 24, 12), new UiGlyphLine(4, 24, 12), new UiGlyphLine(8, 12, 12)], layout.Lines);
        Assert.Equal(24, width);
    }

    [Fact]
    public void A_newline_forces_a_break_and_a_trailing_one_adds_an_empty_line()
    {
        var layout = Layout("ab\ncd\n");

        layout.Recalculate(1000, false, out var width, out var height);

        // The second line's width keeps its newline (only a break leaves its last whitespace out), as retail.
        Assert.Equal([new UiGlyphLine(0, 12, 12), new UiGlyphLine(3, 18, 12), new UiGlyphLine(6, 0, 12)], layout.Lines);
        Assert.Equal(18, width);
        Assert.Equal(36, height);
    }

    [Fact]
    public void Trailing_spaces_count_in_the_last_lines_width()
    {
        var layout = Layout("aa  ");

        layout.Recalculate(1000, false, out var width, out _);

        Assert.Equal([new UiGlyphLine(0, 18, 12)], layout.Lines);
        Assert.Equal(18, width);
    }

    [Fact]
    public void Spaces_past_the_margin_break_like_any_other_glyph()
    {
        // The second space is a break point (after whitespace) and the width without it already passes 14.
        var layout = Layout("aa    ");

        layout.Recalculate(14, false, out var width, out _);

        Assert.Equal([new UiGlyphLine(0, 12, 12), new UiGlyphLine(3, 9, 12)], layout.Lines);
        Assert.Equal(12, width);
    }

    [Fact]
    public void One_line_text_never_breaks()
    {
        var layout = Layout("aaa bbb ccc");

        layout.Recalculate(10, true, out var width, out var height);

        Assert.Equal([new UiGlyphLine(0, 60, 12)], layout.Lines);
        Assert.Equal((60, 12), (width, height));
    }

    [Theory]
    [InlineData(' ', 'a', true)]
    [InlineData('a', 'b', false)]
    [InlineData(' ', '!', false)]
    [InlineData(' ', ')', false)]
    [InlineData(' ', ',', false)]
    [InlineData(' ', '.', false)]
    [InlineData(' ', '?', false)]
    [InlineData('\n', '!', true)]
    [InlineData('(', 'あ', false)]
    [InlineData('a', 'あ', true)]
    [InlineData('あ', 'a', true)]
    [InlineData('あ', '。', false)]
    [InlineData('\t', 'a', true)]
    public void Line_breaks_follow_LinebreakUtils(char previous, char current, bool allowed)
    {
        Assert.Equal(allowed, GlyphLayout.CanBreakLineAt(previous, current));
    }

    [Fact]
    public void A_space_before_punctuation_is_no_break_point()
    {
        var layout = Layout("aa bb !cc");

        layout.Recalculate(34, false, out _, out _);

        // The space before '!' may not break, so the break falls after the first space.
        Assert.Equal([0, 3], layout.Lines.Select(l => l.Index));
    }

    [Fact]
    public void Text_past_the_maximum_is_cut_at_the_insertion_point_or_from_the_top()
    {
        var cut = new GlyphLayout { MaxCharacters = 5 };
        Assert.Equal(5, cut.AddText(0, Glyphs("abcdefg")));
        Assert.Equal("abcde", cut.GetText(0, cut.Count));
        Assert.Equal(0, cut.AddText(2, Glyphs("XY")));
        Assert.Equal("abcde", cut.GetText(0, cut.Count));

        var trim = new GlyphLayout { MaxCharacters = 5, TrimFromTop = true };
        trim.AddText(0, Glyphs("abcdefg"));
        Assert.Equal("cdefg", trim.GetText(0, trim.Count));
    }

    [Fact]
    public void Positions_map_to_lines_pixels_and_back()
    {
        var layout = Layout("aaa bbb");
        layout.Recalculate(30, false, out _, out _);

        Assert.True(layout.FindCurrentLine(5, out var line, out var start));
        Assert.Equal((1, 4), (line, start));
        Assert.True(layout.FindPixelsFromPos(6, out var pixels));
        Assert.Equal(12, pixels);
        Assert.True(layout.FindXYFromPosition(6, out var x, out var y));
        Assert.Equal((12, 12), (x, y));
        Assert.Equal(1, layout.FindLineFromY(13));
        Assert.Equal(0, layout.FindLineFromY(12));
        Assert.True(layout.FindPosFromLineAndPixels(1, 8, false, out var exact));
        Assert.Equal(5, exact);
        Assert.True(layout.FindPosFromLineAndPixels(1, 8, true, out var nearest));
        Assert.Equal(5, nearest);
        Assert.True(layout.FindPosFromLineAndPixels(1, 2, true, out var before));
        Assert.Equal(4, before);
        Assert.True(layout.FindPosFromLineAndPixels(0, 100, false, out var pastEnd));
        Assert.Equal(3, pastEnd);
        Assert.True(layout.FindPosFromLineAndPixels(1, 100, false, out var textEnd));
        Assert.Equal(7, textEnd);
        Assert.Equal(4, layout.FindNextWord(1));
        Assert.Equal(4, layout.FindPrevWord(6));
    }

    internal static GlyphLayout Layout(string text)
    {
        var layout = new GlyphLayout();
        layout.AddText(0, Glyphs(text));
        return layout;
    }

    internal static UiGlyph[] Glyphs(string text) =>
        [.. text.Select(c => new UiGlyph(c, null, UiRgbaColor.White, c switch
        {
            ' ' or '\t' => 3,
            '!' or '.' or ',' => 2,
            _ => 6,
        }, 12))];
}

/// <summary>Layout allocations (R12: no allocation when an unchanged element is laid out again).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class GlyphLayoutAllocationTests
{
    [Fact]
    public void An_unchanged_layout_is_not_laid_out_again_and_a_warm_relayout_allocates_nothing()
    {
        var layout = GlyphLayoutTests.Layout("the quick brown fox jumps over the lazy dog");
        layout.Recalculate(60, false, out _, out _);
        layout.Recalculate(70, false, out _, out _);

        var window = AllocationWindow.Open();
        layout.Recalculate(70, false, out var w1, out _);
        layout.Recalculate(60, false, out var w2, out _);
        var (bytes, gc) = window.Close();

        Assert.True(w1 > 0 && w2 > 0);
        if (!gc)
        {
            Assert.Equal(0, bytes);
        }
    }
}
