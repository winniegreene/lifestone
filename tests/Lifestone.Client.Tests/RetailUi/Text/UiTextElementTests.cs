using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Text;

/// <summary>
/// The Text element (UIElement_Text): its properties, layout, justification, truncation and fit-to-text, editing
/// (caret, selection, clipboard, filters, maximum length), the caret's blink, and how it draws.
/// </summary>
public sealed class UiTextElementTests
{
    private const uint LayoutId = 0x21000001;
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF000000;

    [Fact]
    public void Its_properties_set_the_text_colour_justification_and_margins()
    {
        var text = Create(
            new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("Hi")),
            new UiArrayProperty(UiPropertyIds.TextFontColors, [new UiColorProperty(UiPropertyIds.TextFontColor, 0xFF102030)]),
            new UiEnumProperty(UiPropertyIds.TextHorizontalJustification, 1),
            new UiEnumProperty(UiPropertyIds.TextVerticalJustification, 1),
            new UiIntegerProperty(UiPropertyIds.TextLeftMargin, 4),
            new UiIntegerProperty(UiPropertyIds.TextTopMargin, 50)).Text;

        Assert.Equal("Hi", text.Text);
        Assert.Equal(0xFF102030u, text.FontColor.ToColor32());
        Assert.All(text.Glyphs.Glyphs, g => Assert.Equal(0xFF102030u, g.Color.ToColor32()));
        Assert.Equal(1u, text.HorizontalJustification);

        // The top margin is cut to the height (20); the left margin stays 4. Centring: (100 - 4 - 0) / 2 - 14 / 2 + 4.
        text.RecalculateGlyphList();
        Assert.Equal(4 + 48 - 7, text.CalcJustification(14, true));
        Assert.Equal(20, text.CalcJustification(12, false));
    }

    [Fact]
    public void Table_text_comes_from_the_string_source_and_a_missing_one_is_loud()
    {
        var strings = new FakeStringSource();
        strings.Strings[(0x23000001, 0x1234)] = "Play";
        var scene = Create(strings, new UiStringInfoProperty(UiPropertyIds.TextEntry, new UiStringRef(0x1234, 0x23000001)));
        Assert.Equal("Play", scene.Text.Text);

        var missing = Create(strings, new UiStringInfoProperty(UiPropertyIds.TextEntry, new UiStringRef(0x9999, 0x23000001)));
        Assert.Equal(string.Empty, missing.Text.Text);
        Assert.Contains(missing.Log.Lines, l => l.Contains("does not resolve", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_lays_out_on_the_paper_and_fit_to_text_resizes_the_element()
    {
        var scene = Create(
            new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("abc")),
            new UiIntegerProperty(UiPropertyIds.TextLeftMargin, 2),
            new UiIntegerProperty(UiPropertyIds.TextRightMargin, 3),
            new UiBoolProperty(UiPropertyIds.TextFitToText, true));

        // Three glyphs of 7 plus the margins; one line of 12.
        Assert.Equal(new UiBox(0, 0, 25, 11), scene.Text.Box);
        scene.Text.RecalculateGlyphList();
        Assert.Equal((26, 12), (scene.Text.PaperWidth, scene.Text.PaperHeight));
    }

    [Fact]
    public void One_line_text_too_wide_is_truncated_before_the_trailer()
    {
        var scene = Create(
            new UiBoolProperty(UiPropertyIds.TextOneLine, true),
            new UiStringInfoProperty(UiPropertyIds.TextTruncateTextToFit, UiStringRef.FromLiteral("..")),
            new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("abcdefghijklmnopqrstuvwxyz")));

        scene.Text.RecalculateGlyphList();

        // 100 pixels less the 14-pixel trailer leaves 86: 12 glyphs of 7 (84).
        Assert.Equal(12, scene.Text.TruncationPosition);
        Assert.Equal(12 * 7 + 14, scene.Text.PaperWidth);
    }

    [Fact]
    public void Typing_inserts_at_the_caret_honours_the_filter_and_announces_each_character()
    {
        var scene = Editable();
        var messages = new List<UiElementMessage>();
        scene.Manager.ElementMessage += messages.Add;
        scene.Text.Filter = TextFilters.Name;

        foreach (var c in "Bo1b'-x\t")
        {
            scene.Text.CharacterHandler(c);
        }

        Assert.Equal("Bob'-x", scene.Text.Text);
        Assert.Equal(6, scene.Text.CursorPosition);
        Assert.Equal(8, messages.Count(m => m.MessageId == 0x12));
    }

    [Fact]
    public void A_one_line_field_ignores_newlines_and_a_multi_line_one_takes_them()
    {
        var one = Editable(new UiBoolProperty(UiPropertyIds.TextOneLine, true));
        one.Text.CharacterHandler('a');
        one.Text.CharacterHandler('\r');
        Assert.Equal("a", one.Text.Text);

        var many = Editable();
        many.Text.CharacterHandler('a');
        many.Text.CharacterHandler('\r');
        many.Text.CharacterHandler('b');
        Assert.Equal("a\nb", many.Text.Text);
    }

    [Fact]
    public void The_maximum_length_cuts_what_is_typed_past_it()
    {
        var scene = Editable(new UiIntegerProperty(UiPropertyIds.TextMaxChars, 4));

        foreach (var c in "abcdef")
        {
            scene.Text.CharacterHandler(c);
        }

        Assert.Equal("abcd", scene.Text.Text);
        Assert.Equal(4, scene.Text.CursorPosition);
    }

    [Fact]
    public void The_caret_moves_by_character_word_line_and_text()
    {
        var scene = Editable();
        scene.Text.SetText("ab cd\nef gh");
        scene.Text.RecalculateGlyphList();

        scene.Text.MoveCursor(UiTextDirection.TextEnd);
        Assert.Equal(11, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.LineStart);
        Assert.Equal(6, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.Up);
        Assert.Equal(0, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.LineEnd);
        Assert.Equal(5, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.PreviousWord);
        Assert.Equal(3, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.Left);
        Assert.Equal(2, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.NextWord);
        Assert.Equal(3, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.Down);
        Assert.Equal(9, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.TextStart);
        Assert.Equal(0, scene.Text.CursorPosition);
        scene.Text.MoveCursor(UiTextDirection.Left);
        Assert.Equal(0, scene.Text.CursorPosition);
    }

    [Fact]
    public void Shift_selects_and_copy_cut_and_paste_go_through_the_clipboard()
    {
        var scene = Editable();
        scene.Text.SetText("hello world");
        scene.Text.MoveCursor(UiTextDirection.TextEnd);

        scene.Manager.ShiftKeyDown = true;
        for (var i = 0; i < 5; i++)
        {
            scene.Text.MoveCursor(UiTextDirection.Left);
        }

        scene.Manager.ShiftKeyDown = false;
        Assert.Equal("world", scene.Text.SelectedText);

        scene.Text.Copy();
        Assert.Equal("world", scene.Clipboard.Text);

        scene.Text.Cut();
        Assert.Equal("hello ", scene.Text.Text);

        scene.Clipboard.Text = "there\r\nyou";
        scene.Text.Paste();
        Assert.Equal("hello there\nyou", scene.Text.Text);
        Assert.Equal(15, scene.Text.CursorPosition);

        // A move without Shift drops the selection.
        scene.Text.SelectAll();
        Assert.Equal("hello there\nyou", scene.Text.SelectedText);
        scene.Text.MoveCursor(UiTextDirection.Left);
        Assert.False(scene.Text.GetSelection(out _, out _));
    }

    [Fact]
    public void Paste_types_through_the_filter_and_replaces_the_selection()
    {
        var scene = Editable();
        scene.Text.Filter = TextFilters.Number;
        scene.Text.SetText("12");
        scene.Text.SelectAll();
        scene.Clipboard.Text = "3a4";

        scene.Text.Paste();

        Assert.Equal("34", scene.Text.Text);
    }

    [Fact]
    public void Backspace_and_delete_remove_around_the_caret_or_the_selection()
    {
        var scene = Editable();
        scene.Text.SetText("abcd");
        scene.Text.SetCursorPosition(2);

        scene.Text.DeleteBackward();
        Assert.Equal(("acd", 1), (scene.Text.Text, scene.Text.CursorPosition));

        scene.Text.DeleteForward();
        Assert.Equal(("ad", 1), (scene.Text.Text, scene.Text.CursorPosition));

        scene.Text.SelectAll();
        scene.Text.DeleteBackward();
        Assert.Equal(string.Empty, scene.Text.Text);
    }

    [Fact]
    public void The_caret_blinks_while_the_field_has_the_focus()
    {
        var scene = Editable();
        scene.Manager.SetFocusElement(scene.Text);
        scene.Text.CharacterHandler('a');

        scene.Manager.Update(0.1);
        Assert.True(scene.Text.CaretOn);

        scene.Manager.Update(0.5);
        Assert.True(scene.Text.CaretOn);

        scene.Manager.Update(0.7);
        Assert.False(scene.Text.CaretOn);

        scene.Manager.Update(1.3);
        Assert.True(scene.Text.CaretOn);

        scene.Manager.SetFocusElement(null);
        scene.Manager.Update(5.0);
        Assert.True(scene.Text.CaretOn);
    }

    [Fact]
    public void Glyphs_draw_in_the_font_colour_at_the_justified_position()
    {
        var scene = Create(new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("A")));

        var surface = scene.Composite();

        Assert.Equal(White, surface.GetArgb(1, 2));
        Assert.Equal(White, surface.GetArgb(5, 9));
        Assert.Equal(0u, surface.GetArgb(0, 2));
        Assert.Equal(0u, surface.GetArgb(6, 2));
    }

    [Fact]
    public void An_outline_without_a_background_atlas_is_the_glyph_drawn_at_the_eight_neighbours()
    {
        var scene = Create(
            new UiBoolProperty(UiPropertyIds.TextOutline, true),
            new UiColorProperty(UiPropertyIds.TextFontOutlineColor, Black),
            new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("A")));

        var surface = scene.Composite();

        Assert.Equal(Black, surface.GetArgb(0, 1));
        Assert.Equal(Black, surface.GetArgb(6, 10));
        Assert.Equal(White, surface.GetArgb(3, 5));
        Assert.Equal(0u, surface.GetArgb(7, 5));
    }

    [Fact]
    public void Selected_glyphs_are_inverted_and_the_focused_caret_is_a_line_in_the_font_colour()
    {
        var scene = Editable(new UiStringInfoProperty(UiPropertyIds.TextEntry, UiStringRef.FromLiteral("AB")));
        scene.Manager.SetFocusElement(scene.Text);
        scene.Text.SetCursorPosition(1);
        scene.Manager.ShiftKeyDown = true;
        scene.Text.MoveCursor(UiTextDirection.Left);
        scene.Manager.ShiftKeyDown = false;
        Assert.Equal("A", scene.Text.SelectedText);

        var surface = scene.Composite();

        // 'A' (pen 0-6, line 0-11) is selected: transparent pixels invert to 0x00FFFFFF and its white glyph to black.
        Assert.Equal(0x00FFFFFFu, surface.GetArgb(6, 0));
        Assert.Equal(Black, surface.GetArgb(1, 2));
        Assert.Equal(White, surface.GetArgb(8, 2));
        Assert.Equal(0u, surface.GetArgb(7, 0));

        // The caret sits before 'A' (position 0): one pixel wide, the line's height, in the font colour, drawn last.
        Assert.Equal(White, surface.GetArgb(0, 0));
        Assert.Equal(White, surface.GetArgb(0, 11));
        Assert.Equal(0u, surface.GetArgb(0, 12));
    }

    private static TextScene Editable(params UiProperty[] extra) =>
        Create([new UiBoolProperty(UiPropertyIds.TextEditable, true), new UiBoolProperty(UiPropertyIds.TextSelectable, true), .. extra]);

    private static TextScene Create(params UiProperty[] properties) => Create(new FakeStringSource(), properties);

    private static TextScene Create(FakeStringSource strings, params UiProperty[] properties)
    {
        var font = new FakeFont();
        var log = new RecordingLogSink();
        var clipboard = new FakeClipboard();
        var images = new FakeImageSource();
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 100, 20, children: [UiDescs.Element(2, UiClassIds.Text, 0, 0, 100, 20, properties: properties)]);
        var manager = new UiElementManager(new UiServices
        {
            Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]),
            Fonts = FakeFontSource.With(font),
            Strings = strings,
            Clipboard = clipboard,
            Log = log,
            Images = images,
        });
        var compositor = new UiCompositor(images);
        compositor.Attach(manager);
        var root = manager.CreateRootElementByDataId(LayoutId, 1)!;
        return new TextScene(manager, compositor, root, (UiTextElement)root.GetChild(2)!, clipboard, log);
    }

    private sealed record TextScene(UiElementManager Manager, UiCompositor Compositor, UiElement Root, UiTextElement Text, FakeClipboard Clipboard, RecordingLogSink Log)
    {
        public UiSurface Composite()
        {
            Assert.True(Compositor.Composite(long.MaxValue));
            return Compositor.GetSurface(Root)!;
        }
    }
}
