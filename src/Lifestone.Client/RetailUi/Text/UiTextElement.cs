using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>How the caret moves (retail's <c>TextDefs::Direction</c>, as <c>UIElement_Text::MoveCursor</c> switches on it).</summary>
public enum UiTextDirection
{
    /// <summary>0: to the start of the text.</summary>
    TextStart = 0,

    /// <summary>1: to the end of the text.</summary>
    TextEnd = 1,

    /// <summary>2: to the start of the line.</summary>
    LineStart = 2,

    /// <summary>3: to the end of the line.</summary>
    LineEnd = 3,

    /// <summary>6: to the start of the previous word.</summary>
    PreviousWord = 6,

    /// <summary>7: past the next word.</summary>
    NextWord = 7,

    /// <summary>8: one character left.</summary>
    Left = 8,

    /// <summary>9: one character right.</summary>
    Right = 9,

    /// <summary>10: one line up.</summary>
    Up = 10,

    /// <summary>11: one line down.</summary>
    Down = 11,
}

/// <summary>How a caret move treats the selection (<c>UIElement_Text::CursorMovementFlags</c>).</summary>
public enum UiCaretSelection
{
    /// <summary>0: extend the selection when Shift is down (or the mouse is selecting), else drop it.</summary>
    Default = 0,

    /// <summary>1: extend the selection.</summary>
    Select = 1,

    /// <summary>2: drop the selection (text was typed or pasted).</summary>
    DontSelect = 2,
}

/// <summary>
/// The Text element (class 0x0C, retail's <c>UIElement_Text</c> over <c>UIElement_Scrollable</c>): laid-out retail-font
/// text with justification, margins, colour and font runs, an outline, truncation with a trailer, fit-to-text, and
/// editing (caret, selection, copy, cut and paste, input filters, a maximum length; D39, no IME).
/// </summary>
/// <remarks>
/// <para>
/// <b>Text</b> becomes glyphs in the current font and colour (<c>UIElement_Text::InqGlyphs</c>, 11.4186 0x00468EA0,
/// 11.6096 0x00469130); a character outside the font's range (<see cref="IUiFont.ContainsChar"/>) falls back to the fonts
/// of FONT enums 20 and 22, as <c>Glyph::SetFont</c> (11.4186 0x004749A0, 11.6096 0x00474D60) does, and one inside it
/// that the font lacks draws as the font's glyph 0, as retail. Text tags (<c>&lt;Tell:...&gt;</c>, chat links)
/// are not parsed yet: a loud TODO, and the text shows as written.
/// </para>
/// <para>
/// <b>Layout</b> happens when the text is dirty (<c>UIElement_Text::RecalculateGlyphList</c>, 11.4186 0x00468420, 11.6096
/// 0x004686B0): lines as wide as the element less its margins (<see cref="GlyphLayout"/>), then the paper size, then
/// truncation (<c>UIElement_Text::RecalculateTruncation</c>, 11.4186 0x00466F80, 11.6096 0x00467210).
/// </para>
/// <para>
/// <b>Drawing</b> (<c>UIElement_Text::DrawSelf</c>, 11.4186 0x00467AA0, 11.6096 0x00467D30): the image first, then the
/// text into the dirty window: with an outline, an outline pass first (the font's background atlas in the outline
/// colour, or, without one, the glyph drawn at the eight one-pixel offsets in the outline colour), then the glyphs, each
/// line placed by the justification (<c>UIElement_Text::CalcJustification</c>, 11.4186 0x00467260, 11.6096
/// 0x004674F0); selected glyphs are inverted; the caret, when the element has the focus and it is on, is a one-pixel
/// line in the font colour.
/// </para>
/// </remarks>
public class UiTextElement : UiScrollable, IUiTickTarget
{
    private const uint TagTextType = 0x10000001;
    private const uint DefaultFontEnum = 24;
    private const uint FallbackFontEnum = 3;
    private const uint MissingGlyphFontEnum1 = 20;
    private const uint MissingGlyphFontEnum2 = 22;

    private readonly GlyphLayout _glyphs = new();
    private readonly GlyphLayout _trailer = new();
    private UiGlyph[] _scratch = new UiGlyph[32];
    private int _marginLeft;
    private int _designMarginRight;
    private int _marginRight;
    private int _marginTop;
    private int _marginBottom;
    private int _truncationPosition = -1;
    private int _adjustedLineNumber = -1;
    private int _adjustedLineSize;
    private int _trailerWidth;
    private int _trailerHeight;
    private double _lastCursorMoveTime = -1;
    private double _lastFlashFlipTime = -1;
    private UiBox _lastCursor;
    private bool _dirty = true;
    private bool _caretOn = true;
    private bool _selecting;
    private bool _mouseSelecting;

    /// <summary>
    /// The text element of <paramref name="init"/> (<c>UIElement_Text::UIElement_Text</c>, 11.4186 0x00468570, 11.6096
    /// 0x00468800): left and top justified, white text, a black outline colour, no margins, and the font FONT enum 24
    /// names (else enum 3).
    /// </summary>
    public UiTextElement(in UiElementInit init)
        : base(init)
    {
        var fonts = init.Manager.Services.Fonts;
        CurrentFont = fonts?.GetFontByEnum(DefaultFontEnum) ?? fonts?.GetFontByEnum(FallbackFontEnum);
        if (CurrentFont is null)
        {
            init.Manager.LogOnce("text:nofont", LogSeverity.Warning, "TODO(retailui-runtime): no retail fonts are available (the font source is missing or lacks FONT enums 24 and 3); text elements draw nothing (placeholder)");
        }

        DetermineMarginValues();
        ClearAllText();
    }

    /// <summary>The font new text is laid out in (<c>m_curFontObj</c>).</summary>
    public IUiFont? CurrentFont { get; private set; }

    /// <summary>The colour new text gets (<c>m_curFontColor</c>).</summary>
    public UiRgbaColor FontColor { get; private set; } = UiRgbaColor.White;

    /// <summary>The colour of tagged text (<c>m_curTagFontColor</c>).</summary>
    public UiRgbaColor TagFontColor { get; private set; } = UiRgbaColor.White;

    /// <summary>The outline colour as 0xAARRGGBB (<c>m_curOutlineColor</c>).</summary>
    public uint OutlineColor { get; private set; } = UiRgbaColor.Black.ToColor32();

    /// <summary>The horizontal justification (property 0x14): 1 centre, 3 or 5 the far side, anything else the near side (default 2).</summary>
    public uint HorizontalJustification { get; private set; } = 2;

    /// <summary>The vertical justification (property 0x15): 1 centre, 3 or 5 the far side, anything else the near side (default 4).</summary>
    public uint VerticalJustification { get; private set; } = 4;

    /// <summary>True when the text can be edited (property 0x16; bit 0x1).</summary>
    public bool Editable { get; private set; }

    /// <summary>True when the text stays on one line (property 0x20; bit 0x2).</summary>
    public bool OneLine { get; private set; }

    /// <summary>True when the text can be selected (property 0x27; bit 0x4).</summary>
    public bool Selectable { get; private set; }

    /// <summary>True when the text is drawn with an outline (property 0x21; bit 0x10).</summary>
    public bool Outline { get; private set; }

    /// <summary>True when the element resizes to its text (property 0x29; bit 0x400).</summary>
    public bool FitToText { get; private set; }

    /// <summary>True when text that does not fit is cut and ends in the trailer (property 0xC7; bit 0x800).</summary>
    public bool TruncateTextToFit { get; private set; }

    /// <summary>True when Escape takes the focus away (property 0xCB).</summary>
    public bool LoseFocusOnEscape { get; private set; }

    /// <summary>True when accepting the input takes the focus away (property 0xCC).</summary>
    public bool LoseFocusOnAcceptInput { get; private set; }

    /// <summary>The input filter (<c>m_filter</c>), or null for none: a typed character is taken only when it returns true (<see cref="TextFilters"/>).</summary>
    public Func<char, bool>? Filter { get; set; }

    /// <summary>The laid-out glyphs.</summary>
    public GlyphLayout Glyphs => _glyphs;

    /// <summary>The caret's position (<c>m_nCursorPos</c>), 0 to the glyph count.</summary>
    public int CursorPosition { get; private set; }

    /// <summary>True when the caret is drawn this blink (bit 0x200).</summary>
    public bool CaretOn => _caretOn;

    /// <summary>The glyph index where truncation starts, or -1.</summary>
    public int TruncationPosition => _truncationPosition;

    /// <summary>True when the text needs laying out again (bit 0x100).</summary>
    public bool IsLayoutDirty => _dirty;

    /// <summary>The text as characters (<c>UIElement_Text::GetText</c>).</summary>
    public string Text => _glyphs.GetText(0, _glyphs.Count);

    /// <summary>True when the text is <paramref name="text"/>; allocates nothing (<see cref="Text"/> builds a string each time).</summary>
    public bool TextEquals(string text) => _glyphs.TextEquals(text);

    /// <summary>True when this element has the keyboard focus.</summary>
    public bool HasFocus => ReferenceEquals(Manager.FocusElement, this);

    // ---------------------------------------------------------------- Properties

    /// <summary><c>UIElement_Text::OnSetAttribute</c> (11.4186 0x0046A640, 11.6096 0x0046A8D0): the Text properties.</summary>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.TextFontOutlineColor:
                OutlineColor = (value is UiColorProperty color ? UiRgbaColor.FromColor32(color.Argb) : UiRgbaColor.Black).ToColor32();
                DoFontReset();
                break;
            case UiPropertyIds.TextFonts:
            case UiPropertyIds.TextFontColors:
            case UiPropertyIds.TextTagFontColors:
                DoFontReset();
                break;
            case UiPropertyIds.TextEditable:
                SetEditable(GetBool(value));
                UpdateMouseVisibility();
                break;
            case UiPropertyIds.TextOneLine:
                if (OneLine != GetBool(value))
                {
                    OneLine = GetBool(value);
                    SetDirty();
                }

                break;
            case UiPropertyIds.TextSelectable:
                SetSelectable(GetBool(value));
                UpdateMouseVisibility();
                break;
            case UiPropertyIds.TextVerticalJustification:
                VerticalJustification = value is UiEnumProperty v ? v.Value : 4;
                SetDirty();
                break;
            case UiPropertyIds.TextHorizontalJustification:
                HorizontalJustification = value is UiEnumProperty h ? h.Value : 2;
                SetDirty();
                break;
            case UiPropertyIds.TextNoIme:
                // IME is out of scope for M2 (D39); the flag has nothing to switch.
                break;
            case UiPropertyIds.TextTrimFromTop:
                _glyphs.TrimFromTop = GetBool(value);
                break;
            case UiPropertyIds.TextOutline:
                Outline = GetBool(value);
                SetDirty();
                break;
            case UiPropertyIds.TextTopMargin:
                _marginTop = value is UiIntegerProperty top ? top.Value : 0;
                DetermineMarginValues();
                SetDirty();
                break;
            case UiPropertyIds.TextBottomMargin:
                _marginBottom = value is UiIntegerProperty bottom ? bottom.Value : 0;
                DetermineMarginValues();
                SetDirty();
                break;
            case UiPropertyIds.TextLeftMargin:
                _marginLeft = value is UiIntegerProperty left ? left.Value : 0;
                DetermineMarginValues();
                SetDirty();
                break;
            case UiPropertyIds.TextRightMargin:
                _marginRight = value is UiIntegerProperty right ? right.Value : 0;
                _designMarginRight = _marginRight;
                DetermineMarginValues();
                SetDirty();
                break;
            case UiPropertyIds.TextMaxChars:
                _glyphs.MaxCharacters = value is UiIntegerProperty max ? max.Value : 0xFFFF;
                break;
            case UiPropertyIds.TextEntry:
                if (value is UiStringInfoProperty entry && (entry.Value.IsLiteral || entry.Value.StringId != 0 || entry.Value.TableId != 0))
                {
                    SetStringInfo(entry.Value);
                }
                else
                {
                    ClearAllText();
                }

                break;
            case UiPropertyIds.TextFitToText:
                SetFitToText(GetBool(value));
                break;
            case UiPropertyIds.TextTruncateTextToFit:
                SetTruncation(value as UiStringInfoProperty);
                break;
            case UiPropertyIds.TextLoseFocusOnEscape:
                LoseFocusOnEscape = GetBool(value);
                break;
            case UiPropertyIds.TextLoseFocusOnAcceptInput:
                LoseFocusOnAcceptInput = GetBool(value);
                break;
        }
    }

    /// <summary>Text elements that can be edited or selected take the mouse (<c>UIElement_Text::GetShouldBeMouseVisible</c>).</summary>
    protected override bool GetShouldBeMouseVisible() => Editable || Selectable || base.GetShouldBeMouseVisible();

    private void SetEditable(bool editable)
    {
        if (editable == Editable)
        {
            return;
        }

        if (editable && TruncateTextToFit)
        {
            // An editable field never truncates (UIElement_Text::SetEditable).
            TruncateTextToFit = false;
            SetDirty();
        }

        Editable = editable;
    }

    private void SetSelectable(bool selectable)
    {
        if (selectable == Selectable)
        {
            return;
        }

        if (selectable && TruncateTextToFit)
        {
            TruncateTextToFit = false;
            SetDirty();
        }

        Selectable = selectable;
        if (_selecting)
        {
            SetSelecting(false);
        }
    }

    private void SetFitToText(bool fit)
    {
        FitToText = fit;
        if (fit)
        {
            ResizeToPaper();
        }
    }

    private void SetTruncation(UiStringInfoProperty? trailer)
    {
        // UIElement_Text::SetTruncateTextToFit: never for a field that can be edited or selected.
        var valid = trailer is not null && (trailer.Value.IsLiteral || trailer.Value.StringId != 0);
        var truncate = valid && !Editable && !Selectable;
        if (truncate != TruncateTextToFit)
        {
            TruncateTextToFit = truncate;
            SetDirty();
        }

        _trailer.Flush();
        if (valid)
        {
            SetFontIndex(0);
            SetFontColorIndex(0);
            var text = ResolveString(trailer!.Value);
            var count = InqGlyphs(text);
            _trailer.AddText(0, _scratch.AsSpan(0, count));
            _trailer.Recalculate(int.MaxValue, true, out _trailerWidth, out _trailerHeight);
        }
        else
        {
            _trailerWidth = 0;
            _trailerHeight = 0;
        }
    }

    /// <summary>
    /// <c>UIElement_Text::DoFontReset</c> (11.4186 0x0046A560, 11.6096 0x0046A7F0): the fonts and colours go back to the
    /// first of each list, and the text is laid out again in them (retail's <c>s_bChangeTextInDoFontRest</c> is set).
    /// </summary>
    private void DoFontReset()
    {
        SetFontIndex(0);
        SetFontColorIndex(0);
        var text = Text;
        SetText(text);
    }

    /// <summary><c>UIElement_Text::SetFontDIDNum</c> through <c>SetFontDIDHelper</c> (11.4186 0x00466960, 11.6096 0x00466A90).</summary>
    private void SetFontIndex(int index)
    {
        // Retail changes nothing until the element has a font.
        if (CurrentFont is null || !InqProperty(UiPropertyIds.TextFonts, out var property) || property is not UiArrayProperty fonts)
        {
            return;
        }

        if (index >= fonts.Items.Count || fonts.Items[index] is not UiDataIdProperty did || did.Value == 0 || did.Value == CurrentFont.Did)
        {
            return;
        }

        var font = Manager.Services.Fonts?.GetFont(did.Value);
        if (font is not null)
        {
            CurrentFont = font;
        }
        else
        {
            Manager.LogOnce($"font:{did.Value:X8}", LogSeverity.Warning, $"TODO(retailui-runtime): font 0x{did.Value:X8} is not available; {Desc.Describe()} keeps its font (placeholder)");
        }
    }

    /// <summary><c>UIElement_Text::SetFontColorNum</c> through <c>SetFontColorHelper</c> (11.4186 0x00466AC0, 11.6096 0x00466BF0).</summary>
    private void SetFontColorIndex(int index)
    {
        if (TryGetColor(UiPropertyIds.TextFontColors, index, out var color))
        {
            FontColor = color;
        }

        if (TryGetColor(UiPropertyIds.TextTagFontColors, index, out var tagColor))
        {
            TagFontColor = tagColor;
        }
    }

    private bool TryGetColor(uint id, int index, out UiRgbaColor color)
    {
        color = default;
        if (!InqProperty(id, out var property) || property is not UiArrayProperty colors || index >= colors.Items.Count || colors.Items[index] is not UiColorProperty item)
        {
            return false;
        }

        color = UiRgbaColor.FromColor32(item.Argb);
        return true;
    }

    // ---------------------------------------------------------------- Text

    /// <summary><c>UIElement_Text::SetText</c> (11.4186 0x0046A4B0, 11.6096 0x0046A740): replaces the text.</summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ClearAllText();
        AddTextInternal(text, keepSelection: true, append: true);
    }

    /// <summary><c>UIElement_Text::AppendText</c> (11.4186 0x00469CE0, 11.6096 0x00469F70): adds text at the end.</summary>
    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AddTextInternal(text, keepSelection: true, append: true);
    }

    /// <summary>
    /// <c>UIElement_Text::AppendTextWithFont</c> (11.4186 0x00469D70, 11.6096 0x0046A000): adds text at the end in font
    /// <paramref name="fontIndex"/> and colour <paramref name="colorIndex"/> of the element's lists (chat runs).
    /// </summary>
    public void AppendTextWithFont(string text, int fontIndex, int colorIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetFontIndex(fontIndex);
        SetFontColorIndex(colorIndex);
        AddTextInternal(text, keepSelection: true, append: true);
    }

    /// <summary><c>UIElement_Text::SetStringInfo</c> (11.4186 0x0046A0C0, 11.6096 0x0046A350): replaces the text with a string reference's.</summary>
    public void SetStringInfo(in UiStringRef text) => SetStringInfo(text, null);

    /// <summary>
    /// <see cref="SetStringInfo(in UiStringRef)"/> with the reference's variables (retail's <c>StringInfo</c> carries
    /// them; a dialog sets <c>time</c>).
    /// </summary>
    public void SetStringInfo(in UiStringRef text, UiStringVariables? variables)
    {
        ClearAllText();
        AddTextInternal(ResolveString(text, variables), keepSelection: true, append: true);
    }

    /// <summary>
    /// <c>UIElement_Text::AppendStringInfoWithFont</c> (11.4186 0x00469DE0, 11.6096 0x0046A070): adds a string reference's text at the
    /// end in font <paramref name="fontIndex"/> and colour <paramref name="colorIndex"/> of the element's lists (a page's headed
    /// paragraphs: the header in colour 1, the body in colour 0).
    /// </summary>
    public void AppendStringInfoWithFont(in UiStringRef text, int fontIndex, int colorIndex) => AppendTextWithFont(ResolveString(text), fontIndex, colorIndex);

    /// <summary><c>UIElement_Text::SetStringInfoWithFont</c> (11.4186 0x0046A0E0, 11.6096 0x0046A370): <see cref="ClearAllText"/>, then <see cref="AppendStringInfoWithFont"/>.</summary>
    public void SetStringInfoWithFont(in UiStringRef text, int fontIndex, int colorIndex)
    {
        ClearAllText();
        AppendStringInfoWithFont(text, fontIndex, colorIndex);
    }

    /// <summary><c>UIElement_Text::ClearAllText</c> (11.4186 0x00467850, 11.6096 0x00467AE0): no text, no selection, the caret at 0.</summary>
    public void ClearAllText()
    {
        _glyphs.Flush();
        if (_selecting)
        {
            SetSelecting(false);
        }

        CursorPosition = 0;
        SetDirty();
    }

    /// <summary>
    /// <c>UIElement_Text::AddText_Internal</c> (11.4186 0x00469A30, 11.6096 0x00469CC0): an editable field first deletes its
    /// selection; the text becomes glyphs, inserted at the caret (which moves past them) or appended; the element resizes
    /// to its text when it fits to it.
    /// </summary>
    private void AddTextInternal(string text, bool keepSelection, bool append)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (Editable)
        {
            DeleteSelection();
        }
        else if (!keepSelection && _selecting)
        {
            SetSelecting(false);
        }

        var count = InqGlyphs(text);
        var glyphs = _scratch.AsSpan(0, count);
        if (append)
        {
            _glyphs.AddText(_glyphs.Count, glyphs);
        }
        else
        {
            var added = _glyphs.AddText(CursorPosition, glyphs);
            SetCursorPosition(added + CursorPosition, UiCaretSelection.DontSelect);
        }

        _lastCursorMoveTime = Manager.Now;
        if (FitToText)
        {
            ResizeToPaper();
        }

        SetDirty();
    }

    /// <summary>The glyphs of <paramref name="text"/> in the current font and colour, into the scratch array; returns how many.</summary>
    private int InqGlyphs(string text)
    {
        if (_scratch.Length < text.Length)
        {
            _scratch = new UiGlyph[Math.Max(text.Length, _scratch.Length * 2)];
        }

        if (text.Contains('<', StringComparison.Ordinal) && text.Contains('>', StringComparison.Ordinal))
        {
            Manager.LogOnce("text:tags", LogSeverity.Warning, $"TODO(retailui-runtime): text tags (TextTagFactory::MakeTag) are not parsed yet; '<...>' shows as written in {Desc.Describe()}");
        }

        for (var i = 0; i < text.Length; i++)
        {
            _scratch[i] = MakeGlyph(text[i], FontColor);
        }

        return text.Length;
    }

    /// <summary>
    /// <c>Glyph::Glyph</c> with <c>Glyph::SetFont</c>: the current font when its range holds the character (or it is a
    /// newline), else the FONT enum 22 font when it does, else the enum 20 font when it does, else none; the glyph's
    /// advance and height in that font.
    /// </summary>
    private UiGlyph MakeGlyph(char c, UiRgbaColor color)
    {
        var font = CurrentFont;
        if (font is not null && !font.ContainsChar(c) && c != '\n')
        {
            var fonts = Manager.Services.Fonts;
            IUiFont? fallback = null;
            var first = fonts?.GetFontByEnum(MissingGlyphFontEnum1);
            if (first is not null && first.ContainsChar(c))
            {
                fallback = first;
            }

            var second = fonts?.GetFontByEnum(MissingGlyphFontEnum2);
            if (second is not null && second.ContainsChar(c))
            {
                fallback = second;
            }

            font = fallback;
        }

        return font is null
            ? new UiGlyph(c, null, color, 0, 0)
            : new UiGlyph(c, font, color, Math.Max(0, font.GetCharWidth(c) + Tracking), font.MaxCharHeight);
    }

    // ---------------------------------------------------------------- M5 7 D132: the fitted stack count (a Lifestone glitch fix, not retail)

    /// <summary>Pixels added to each glyph's advance, in the layout and the drawing: 0, retail's, unless <see cref="SetSqueeze"/> set it (M5 7 D132's fitted stack count).</summary>
    public int Tracking { get; private set; }

    /// <summary>The right margin the element's own property gives (retail's), whatever <see cref="SetSqueeze"/> set.</summary>
    public int DesignRightMargin => _designMarginRight;

    /// <summary>
    /// The fitted stack count's layout (M5 7 D132; Lifestone's, not retail's): <paramref name="tracking"/> pixels added to each glyph's advance and the right margin
    /// <paramref name="rightMargin"/> (null: the element's own). Set before the text: a glyph takes its advance when it is added. <c>SetSqueeze(0, null)</c> is retail's layout.
    /// </summary>
    public void SetSqueeze(int tracking, int? rightMargin)
    {
        Tracking = tracking;
        _marginRight = rightMargin ?? _designMarginRight;
        DetermineMarginValues();
        SetDirty();
    }

    /// <summary>The width <paramref name="text"/> takes in the current font with <paramref name="tracking"/>, and whether it fits the box less the left margin and <paramref name="rightMargin"/>.</summary>
    public bool Fits(string text, int tracking, int rightMargin)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (CurrentFont is not { } font)
        {
            return true;
        }

        var width = 0;
        foreach (var c in text)
        {
            width += Math.Max(0, font.GetCharWidth(c) + tracking);
        }

        return width <= Box.Width - _marginLeft - rightMargin;
    }

    private string ResolveString(in UiStringRef reference, UiStringVariables? variables = null)
    {
        if (reference.IsLiteral)
        {
            return reference.Literal!;
        }

        var strings = Manager.Services.Strings;
        if (strings is not null && strings.TryGetString(reference, variables, out var text))
        {
            return text;
        }

        Manager.LogOnce($"string:{reference}", LogSeverity.Warning, strings is null
            ? "TODO(retailui-runtime): no string source is wired (R6 strings); table text shows as nothing (placeholder)"
            : $"TODO(retailui-runtime): string {reference} does not resolve; {Desc.Describe()} shows nothing (placeholder)");
        return string.Empty;
    }

    // ---------------------------------------------------------------- Layout

    /// <summary><c>UIElement_Text::SetDirty</c> (11.4186 0x00466850, 11.6096 0x00466980): the text needs laying out and the element repainting.</summary>
    protected void SetDirty()
    {
        if (!_dirty)
        {
            _dirty = true;
            MakeRootDirtyHere();
        }
    }

    /// <summary>
    /// <c>UIElement_Text::DetermineMarginValues</c> (11.4186 0x004668A0, 11.6096 0x004669D0): each margin is cut to what the
    /// element's size leaves (top, then bottom, left, right).
    /// </summary>
    private void DetermineMarginValues()
    {
        var width = Box.Width;
        var height = Box.Height;
        _marginTop = Math.Min(height, _marginTop);
        _marginBottom = Math.Min(height - _marginTop, _marginBottom);
        _marginLeft = Math.Min(width, _marginLeft);
        _marginRight = Math.Min(width - _marginLeft, _marginRight);
    }

    /// <inheritdoc/>
    protected override void OnResized()
    {
        DetermineMarginValues();
        SetDirty();
    }

    /// <summary>Lays the text out when it is dirty (<c>UIElement_Text::RecalculateGlyphList</c>).</summary>
    public void RecalculateGlyphList()
    {
        if (!_dirty)
        {
            return;
        }

        _glyphs.Recalculate(Box.Width - _marginRight - _marginLeft, OneLine, out var paperWidth, out var paperHeight);
        paperWidth += _marginLeft + _marginRight;
        paperHeight += _marginTop + _marginBottom;
        if (TruncateTextToFit)
        {
            RecalculateTruncation(ref paperWidth, ref paperHeight);
        }
        else
        {
            _truncationPosition = -1;
            _adjustedLineNumber = -1;
        }

        // The paper is the laid-out text with margins (UIElement_Scrollable::ResizeScrollableArea); retail then sets the
        // view where it is, forced (no clamp), which changes nothing.
        ResizeScrollableArea(paperWidth, paperHeight);
        SetScrollableXY(ScrollX, ScrollY, force: true);
        _dirty = false;
    }

    /// <summary>
    /// <c>UIElement_Text::RecalculateTruncation</c>: when the text is longer (or, on one line, wider) than the element, it is
    /// cut at the last complete line so the trailer fits, and the paper shrinks to match.
    /// </summary>
    private void RecalculateTruncation(ref int paperWidth, ref int paperHeight)
    {
        var size = OneLine ? Box.Width : Box.Height;
        var paper = OneLine ? paperWidth : paperHeight;
        if (paper <= size)
        {
            _truncationPosition = -1;
            _adjustedLineNumber = -1;
            return;
        }

        var line = OneLine ? 0 : _glyphs.FindCompleteLineFromY(size);
        _glyphs.FindPosFromLineAndPixels(line, unchecked((uint)(Box.Width - _marginRight - _marginLeft - _trailerWidth)), false, out _truncationPosition);
        if (OneLine)
        {
            _glyphs.FindPixelsFromPos(_truncationPosition, out var pixels);
            _adjustedLineNumber = 0;
            _adjustedLineSize = pixels + _trailerWidth;
            paperWidth = _adjustedLineSize + _marginLeft + _marginRight;
            return;
        }

        var widest = 0;
        var height = 0;
        for (var l = 0; l <= line && l < _glyphs.Lines.Count; l++)
        {
            int lineWidth;
            if (l == line)
            {
                _glyphs.FindPixelsFromPos(_truncationPosition, out var pixels);
                lineWidth = pixels + _trailerWidth;
                _adjustedLineSize = lineWidth;
            }
            else
            {
                lineWidth = _glyphs.Lines[l].Width;
            }

            widest = Math.Max(widest, lineWidth);
            height += _glyphs.Lines[l].Height;
        }

        _adjustedLineNumber = line;
        paperWidth = widest + _marginLeft + _marginRight;
        paperHeight = height + _marginTop + _marginBottom;
    }

    /// <summary>
    /// <c>UIElement_Text::ResizeToPaper</c> (11.4186 0x00467550, 11.6096 0x004677E0): the element takes its text's size,
    /// laid out no wider than its maximum width property (else the display).
    /// </summary>
    private void ResizeToPaper()
    {
        var maxWidth = TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out var max) ? max : Manager.DisplaySize.Width;
        _glyphs.Recalculate(maxWidth - _marginRight - _marginLeft, OneLine, out var width, out var height);
        ResizeTo(_marginLeft + _marginRight + width, height + _marginTop + _marginBottom);
    }

    /// <summary>
    /// <c>UIElement_Text::InqSizewMargins</c> (11.4186 0x00469660, 11.6096 0x004698F0), its default form: the size, margins
    /// included, that <paramref name="text"/> takes in this element's font and colour when laid out no wider than the
    /// element's maximum width property (else the display), as <see cref="ResizeToPaper"/> lays out; the element's own
    /// text and size are untouched. No text measures 0 by 0.
    /// </summary>
    public (int Width, int Height) InqSizeWithMargins(in UiStringRef text)
    {
        var resolved = ResolveString(text);
        if (resolved.Length == 0)
        {
            return (0, 0);
        }

        var maxWidth = TryGetIntegerProperty(UiPropertyIds.ElementMaxWidth, out var max) ? max : Manager.DisplaySize.Width;
        var layout = new GlyphLayout();
        layout.AddText(0, _scratch.AsSpan(0, InqGlyphs(resolved)));
        layout.Recalculate(maxWidth - _marginRight - _marginLeft, OneLine, out var width, out var height);
        return (width + _marginLeft + _marginRight, height + _marginTop + _marginBottom);
    }

    /// <summary>
    /// <c>UIElement_Text::CalcJustification</c>: where a run of <paramref name="size"/> pixels starts, horizontally or
    /// vertically, in the element (or its paper, when larger) less the margins; unsigned arithmetic, as retail.
    /// </summary>
    public int CalcJustification(int size, bool horizontal)
    {
        uint available;
        uint justification;
        if (horizontal)
        {
            available = unchecked((uint)(Math.Max(PaperWidth, Box.Width) - _marginRight - _marginLeft));
            justification = HorizontalJustification;
        }
        else
        {
            available = unchecked((uint)(Math.Max(PaperHeight, Box.Height) - _marginBottom - _marginTop));
            justification = VerticalJustification;
        }

        var offset = justification switch
        {
            1 => unchecked((int)((available >> 1) - ((uint)size >> 1))),
            3 or 5 => unchecked((int)(available - (uint)size)),
            _ => 0,
        };
        return (horizontal ? _marginLeft : _marginTop) + offset;
    }

    /// <summary><c>UIElement_Text::AdjustToScrollableXYChange</c> (11.4186 0x00467440, 11.6096 0x004676D0): the view moved; lay out and redraw.</summary>
    protected override void AdjustToScrollableXYChange() => SetDirty();

    /// <summary>
    /// <c>UIElement_Text::InqScrollDelta</c> (11.4186 0x004689B0, 11.6096 0x00468C40): a line is the width or height of the
    /// line at the top of the view (at most the element's size); a page is the element's size less that line.
    /// </summary>
    protected override int InqScrollDelta(bool horizontal, bool towardStart, bool page)
    {
        RecalculateGlyphList();
        // Retail finds the glyph at the view's top-left corner (UIElement_Text::DeterminePositionFromXY) and its line.
        var line = 0;
        var lineIndex = _glyphs.FindLineFromY(ScrollY - _marginTop);
        if (lineIndex < _glyphs.Lines.Count)
        {
            var size = horizontal ? _glyphs.Lines[lineIndex].Width : _glyphs.Lines[lineIndex].Height;
            line = Math.Min(size, horizontal ? Box.Width : Box.Height);
        }

        var delta = line;
        if (page)
        {
            delta = Math.Max((horizontal ? Box.Width : Box.Height) - line, line);
        }

        return towardStart ? -delta : delta;
    }

    // ---------------------------------------------------------------- Drawing

    /// <inheritdoc/>
    public override void Draw(IUiPainter painter, UiBox self, UiBox clip, ReadOnlySpan<UiBox> dirty)
    {
        base.Draw(painter, self, clip, dirty);
        RecalculateGlyphList();
        if (_glyphs.Lines.Count == 0 || clip.Width <= 0 || clip.Height <= 0)
        {
            return;
        }

        // Positions are relative to the window (the dirty bounding box), as retail's SurfaceWindow.
        var windowSize = new UiBox(0, 0, clip.X1 - clip.X0, clip.Y1 - clip.Y0);
        var originX = self.X0 - ScrollX - clip.X0;
        var originY = self.Y0 - ScrollY - clip.Y0;
        GetSelection(out var selectionStart, out var selectionEnd);
        var selecting = _selecting;
        var cursorX = 0;
        var cursorY = 0;
        var cursorHeight = CurrentFont?.MaxCharHeight ?? 0;
        for (var pass = Outline ? 0 : 1; pass < 2; pass++)
        {
            var lines = _glyphs.Lines;
            var lineSize = _adjustedLineNumber == 0 ? _adjustedLineSize : lines[0].Width;
            var x = originX + CalcJustification(lineSize, true);
            var textHeight = Math.Max(PaperHeight, CurrentFont?.MaxCharHeight ?? 12);
            var y = originY + CalcJustification(textHeight, false);
            var lineNumber = 0;
            var lineHeight = lines[0].Height;
            var drawingTrailer = false;
            var position = 0;
            var glyphCount = _glyphs.Count;
            for (var i = 0; ; i++, position++)
            {
                UiGlyph glyph;
                if (!drawingTrailer && position == _truncationPosition)
                {
                    drawingTrailer = true;
                    i = 0;
                }

                if (drawingTrailer)
                {
                    if (i >= _trailer.Count)
                    {
                        break;
                    }

                    glyph = _trailer.Glyphs[i];
                }
                else
                {
                    if (i >= glyphCount)
                    {
                        break;
                    }

                    glyph = _glyphs.Glyphs[i];
                }

                if (glyph.Font is not { } font)
                {
                    continue;
                }

                if (!drawingTrailer && lineNumber + 1 < lines.Count && position >= lines[lineNumber + 1].Index)
                {
                    lineNumber++;
                    y += lineHeight;
                    var size = lineNumber == _adjustedLineNumber ? _adjustedLineSize : lines[lineNumber].Width;
                    x = originX + CalcJustification(size, true);
                    lineHeight = lines[lineNumber].Height;
                }

                if (position == CursorPosition)
                {
                    cursorX = x;
                    cursorY = y;
                    cursorHeight = font.MaxCharHeight;
                }

                if (glyph.IsNewLine)
                {
                    continue;
                }

                int advance;
                if (pass == 1)
                {
                    advance = painter.DrawCharacter(x, y, font, glyph.Char, glyph.Color.ToColor32(), UiTextFlags.AdvanceWithOffsets, OutlineColor, clip);
                    if (selecting && position >= selectionStart && position < selectionEnd
                        && UiBoxes.TryIntersect(windowSize, UiBox.FromPositionAndSize(x, y, advance, lineHeight), out var selected))
                    {
                        painter.NotColorBits(UiBoxes.Offset(selected, clip.X0, clip.Y0));
                    }
                }
                else if (font.Background is not null)
                {
                    advance = painter.DrawCharacter(x, y, font, glyph.Char, glyph.Color.ToColor32(),
                        UiTextFlags.AdvanceWithOffsets | UiTextFlags.SkipForeground | UiTextFlags.DrawBackground, OutlineColor, clip);
                }
                else
                {
                    advance = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx != 0 || dy != 0)
                            {
                                advance = painter.DrawCharacter(x + dx, y + dy, font, glyph.Char, OutlineColor,
                                    UiTextFlags.AdvanceWithOffsets | UiTextFlags.Colorize, 0xFF000000, clip);
                            }
                        }
                    }
                }

                x += advance + Tracking;
            }

            if (position == CursorPosition)
            {
                if (lineNumber + 1 < lines.Count && position >= lines[lineNumber + 1].Index)
                {
                    y += lineHeight;
                    var size = lineNumber + 1 == _adjustedLineNumber ? _adjustedLineSize : lines[lineNumber + 1].Width;
                    x = originX + CalcJustification(size, true);
                    lineHeight = lines[lineNumber + 1].Height;
                }

                cursorX = x;
                cursorY = y;
                if (lineHeight != 0)
                {
                    cursorHeight = lineHeight;
                }
            }
        }

        var cursorBox = new UiBox(cursorX, cursorY, cursorX, cursorY + cursorHeight - 1);
        if (HasFocus && Editable && _caretOn && UiBoxes.TryIntersect(windowSize, cursorBox, out var caret))
        {
            painter.Fill(UiBoxes.Offset(caret, clip.X0, clip.Y0), FontColor.ToColor32());
        }

        // The caret's box in the element's surface, repainted when it blinks.
        _lastCursor = UiBoxes.Offset(cursorBox, clip.X0, clip.Y0);
    }

    // ---------------------------------------------------------------- Editing

    /// <summary>
    /// <c>UIElement_Text::CharacterHandler</c> (11.4186 0x00469B90, 11.6096 0x00469E20): a typed character. An editable
    /// field takes a newline (unless it is one line) and any printable character its filter accepts; Tab and Escape do
    /// nothing. The character is announced to listeners either way (element message 0x12).
    /// </summary>
    public void CharacterHandler(char c)
    {
        if (Editable)
        {
            switch (c)
            {
                case '\n':
                case '\r':
                    if (!OneLine)
                    {
                        AddTextInternal("\n", keepSelection: false, append: false);
                        ScrollToPosition(CursorPosition);
                    }

                    break;
                case '\t':
                case '\u001B':
                    break;
                default:
                    if (c > '\u001F' && c != '\u007F' && (Filter is null || Filter(c)))
                    {
                        AddTextInternal(c.ToString(), keepSelection: false, append: false);
                        ScrollToPosition(CursorPosition);
                    }

                    break;
            }
        }

        Manager.BroadcastElementMessage(this, 0x12, c, 0);
    }

    /// <summary>
    /// <c>UIElement_Text::SetCursorPosition</c> (11.4186 0x00468B90, 11.6096 0x00468E20): moves the caret (clamped to the
    /// text); a selecting move starts the selection at the old caret (selectable text only), any other move drops it.
    /// </summary>
    public void SetCursorPosition(int position, UiCaretSelection mode = UiCaretSelection.Default)
    {
        if (mode == UiCaretSelection.Select || ((_mouseSelecting || Manager.ShiftKeyDown) && mode == UiCaretSelection.Default))
        {
            if (!_selecting && Selectable)
            {
                _selecting = true;
                SetSelectionStart(CursorPosition);
            }
        }
        else if (_selecting)
        {
            SetSelecting(false);
        }

        CursorPosition = Math.Clamp(position, 0, _glyphs.Count);
        if (!IsPositionInView(CursorPosition) && (Editable || Selectable))
        {
            ScrollToPosition(CursorPosition);
        }

        _lastCursorMoveTime = Manager.Now;
        if (_selecting)
        {
            SetSelectionEnd(CursorPosition);
        }

        if (HasFocus && Editable)
        {
            SetDirty();
        }
    }

    /// <summary><c>UIElement_Text::MoveCursor</c> (11.4186 0x00468D00, 11.6096 0x00468F90).</summary>
    public void MoveCursor(UiTextDirection direction)
    {
        RecalculateGlyphList();
        var position = CursorPosition;
        switch (direction)
        {
            case UiTextDirection.TextStart:
                SetCursorPosition(0);
                break;
            case UiTextDirection.TextEnd:
                SetCursorPosition(_glyphs.Count);
                break;
            case UiTextDirection.LineStart:
                if (_glyphs.FindCurrentLine(position, out _, out var start))
                {
                    SetCursorPosition(start);
                }

                break;
            case UiTextDirection.LineEnd:
                if (_glyphs.FindCurrentLine(position, out var line, out _))
                {
                    _glyphs.FindNextLine(position, out _, out var nextStart);
                    SetCursorPosition(line == _glyphs.Lines.Count - 1 ? _glyphs.Count : nextStart - 1);
                }

                break;
            case UiTextDirection.PreviousWord:
                SetCursorPosition(_glyphs.FindPrevWord(position));
                break;
            case UiTextDirection.NextWord:
                SetCursorPosition(_glyphs.FindNextWord(position));
                break;
            case UiTextDirection.Left:
                SetCursorPosition(position > 0 ? position - 1 : 0);
                break;
            case UiTextDirection.Right:
                SetCursorPosition(position + 1);
                break;
            case UiTextDirection.Up:
            case UiTextDirection.Down:
                if (MoveUpDown(direction == UiTextDirection.Up, position, out var moved))
                {
                    SetCursorPosition(moved);
                }

                break;
        }
    }

    /// <summary><c>UIElement_Text::MoveUpDown</c> (11.4186 0x004682E0, 11.6096 0x00468570): the same column on the line above or below.</summary>
    private bool MoveUpDown(bool up, int position, out int moved)
    {
        moved = position;
        if (_glyphs.FindCurrentLine(position, out var line, out _) && (up ? line == 0 : line == _glyphs.Lines.Count - 1))
        {
            return true;
        }

        if (!_glyphs.FindPixelsFromPos(position, out var pixels))
        {
            return false;
        }

        int newLine;
        if (up)
        {
            _glyphs.FindPrevLine(position, out newLine, out _);
        }
        else
        {
            _glyphs.FindNextLine(position, out newLine, out _);
        }

        pixels += CalcJustification(LineWidth(line), true);
        pixels -= CalcJustification(LineWidth(newLine), true);
        return _glyphs.FindPosFromLineAndPixels(newLine, unchecked((uint)pixels), true, out moved);
    }

    private int LineWidth(int line) => line >= 0 && line < _glyphs.Lines.Count ? _glyphs.Lines[line].Width : 0;

    /// <summary>The Delete key (input action 0x26): the selection, else the character after the caret.</summary>
    public void DeleteForward()
    {
        if (_selecting && SelectionStart != SelectionEnd)
        {
            DeleteSelection();
        }
        else
        {
            DeleteSection(CursorPosition, CursorPosition + 1);
        }
    }

    /// <summary>The Backspace key (input action 0x28): the selection, else the character before the caret.</summary>
    public void DeleteBackward()
    {
        if (_selecting && SelectionStart != SelectionEnd)
        {
            DeleteSelection();
        }
        else if (CursorPosition > 0)
        {
            SetCursorPosition(CursorPosition - 1, UiCaretSelection.Default);
            DeleteSection(CursorPosition, CursorPosition + 1);
        }
    }

    /// <summary>
    /// <c>UIElement_Text::DeleteSection</c> (11.4186 0x00469800, 11.6096 0x00469A90): removes glyphs [start, end); a caret
    /// after them moves back.
    /// </summary>
    public void DeleteSection(int start, int end)
    {
        if (start >= _glyphs.Count || end > _glyphs.Count || start >= end)
        {
            return;
        }

        var deleted = _glyphs.Delete(start, end);
        if (CursorPosition > end)
        {
            SetCursorPosition(CursorPosition - deleted, UiCaretSelection.Default);
        }
        else if (CursorPosition > start)
        {
            SetCursorPosition(start, UiCaretSelection.Default);
        }

        SetDirty();
        if (deleted != 0)
        {
            Manager.BroadcastElementMessage(this, 0x44, 0, 0);
        }
    }

    /// <summary><c>UIElement_Text::DeleteSelection</c> (11.4186 0x004699B0, 11.6096 0x00469C40).</summary>
    public void DeleteSelection()
    {
        if (GetSelection(out var start, out var end))
        {
            DeleteSection(start, end);
            if (_selecting)
            {
                SetSelecting(false);
            }
        }
    }

    /// <summary><c>UIElement_Text::SelectAll</c> (11.4186 0x004678D0, 11.6096 0x00467B60): selectable text only.</summary>
    public void SelectAll()
    {
        if (!Selectable)
        {
            return;
        }

        _selecting = true;
        SetSelectionStart(0);
        SetSelectionEnd(_glyphs.Count);
        SetDirty();
    }

    /// <summary><c>UIElement_Text::Deselect</c> (11.4186 0x00467930, 11.6096 0x00467BC0).</summary>
    public void Deselect()
    {
        if (_selecting)
        {
            SetSelecting(false);
        }

        SetDirty();
    }

    /// <summary>The selection's start, as set (it may be after the end).</summary>
    public int SelectionStart { get; private set; }

    /// <summary>The selection's end, as set.</summary>
    public int SelectionEnd { get; private set; }

    /// <summary>
    /// <c>UIElement_Text::GetSelection</c> (11.4186 0x00466F20, 11.6096 0x004671B0): the selection in order; false (and 0, 0)
    /// when nothing is being selected.
    /// </summary>
    public bool GetSelection(out int start, out int end)
    {
        start = 0;
        end = 0;
        if (!_selecting)
        {
            return false;
        }

        start = Math.Min(SelectionStart, SelectionEnd);
        end = Math.Max(SelectionStart, SelectionEnd);
        return true;
    }

    /// <summary>The selected text (<c>UIElement_Text::GetSelectedText</c>), or empty.</summary>
    public string SelectedText => GetSelection(out var start, out var end) ? _glyphs.GetText(start, end) : string.Empty;

    /// <summary><c>UIElement_Text::Copy</c> (11.4186 0x004688A0, 11.6096 0x00468B30): the selection to the clipboard.</summary>
    public void Copy()
    {
        if (_selecting)
        {
            Manager.Services.Clipboard?.SetText(SelectedText);
        }
    }

    /// <summary><c>UIElement_Text::Cut</c> (11.4186 0x00469B60, 11.6096 0x00469DF0): copy, then delete when editable.</summary>
    public void Cut()
    {
        if (_selecting)
        {
            Copy();
            if (Editable)
            {
                DeleteSelection();
            }
        }
    }

    /// <summary>
    /// <c>UIElement_Text::Paste</c> (11.4186 0x0046A110, 11.6096 0x0046A3A0): an editable field replaces its selection with
    /// the clipboard's text (CR LF made LF), typed character by character, so the filter and the maximum length apply.
    /// </summary>
    public void Paste()
    {
        if (!Editable)
        {
            return;
        }

        var text = Manager.Services.Clipboard?.GetText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_selecting)
        {
            DeleteSelection();
        }

        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            CharacterHandler(c);
        }

        if (!IsPositionInView(CursorPosition))
        {
            ScrollToPosition(CursorPosition);
        }
    }

    private void SetSelecting(bool selecting)
    {
        if (selecting == _selecting)
        {
            return;
        }

        _selecting = selecting;
        if (!selecting)
        {
            _mouseSelecting = false;
            SelectionStart = 0;
            SelectionEnd = 0;
        }
    }

    private void SetSelectionStart(int position)
    {
        if (_selecting)
        {
            SelectionStart = Math.Min(position, _glyphs.Count);
            SelectionEnd = SelectionStart;
            SetDirty();
        }
    }

    private void SetSelectionEnd(int position)
    {
        if (_selecting)
        {
            SelectionEnd = Math.Min(position, _glyphs.Count);
            SetDirty();
        }
    }

    /// <summary><c>UIElement_Text::IsPositionInView</c> (11.4186 0x00468AA0, 11.6096 0x00468D30).</summary>
    private bool IsPositionInView(int position)
    {
        RecalculateGlyphList();
        if (!_glyphs.FindXYFromPosition(position, out var x, out var y))
        {
            return false;
        }

        if (OneLine)
        {
            var visible = Box.Width - _marginRight - _marginLeft;
            return x >= ScrollX && x + 2 <= visible + ScrollX;
        }

        var visibleHeight = Box.Height - _marginBottom - _marginTop;
        var lineHeight = Math.Min(CurrentFont?.MaxCharHeight ?? 0, visibleHeight);
        return y >= ScrollY && y + lineHeight <= visibleHeight + ScrollY;
    }

    /// <summary>
    /// Raised when the Accept action (Enter, input action 0x25) reaches the field, before it gives up the focus: a chat entry sends its line on it
    /// (<c>ChatInterface::HandleEnterKey</c>, 11.4186 0x004F52D0, 11.6096 0x004F5F30).
    /// </summary>
    public event Action? AcceptInput;

    /// <summary>
    /// True when the view shows the end of the text (<c>UIElement_Text::IsAtVerticalEnd</c>, 11.4186 0x00469350, 11.6096 0x004695E0): an empty text is at its end; else the
    /// LAST GLYPH is in view (<see cref="IsPositionInView"/> at count-1). The chat log keeps following new lines while it is. It is not "the paper's bottom is in view": the
    /// chat appends its line break first (<c>ChatInterface::RecvNotice_DisplayFinalStringInfo</c>, 11.6096 0x004F52A0), which opens an empty line below the view, and the
    /// paper test then said "not at the end" at every arrival, so the log stopped following after its first page.
    /// </summary>
    public bool IsAtVerticalEnd
    {
        get
        {
            RecalculateGlyphList();
            return _glyphs.Count == 0 || IsPositionInView(_glyphs.Count - 1);
        }
    }

    /// <summary>Scrolls the least that shows the end of the text (<c>UIElement_Text::ScrollToPosition</c> at the last glyph).</summary>
    public void ScrollToEnd() => ScrollToPosition(_glyphs.Count);

    /// <summary><c>UIElement_Text::ScrollToPosition</c> (11.4186 0x004691B0, 11.6096 0x00469440): scrolls the least that shows the position.</summary>
    private void ScrollToPosition(int position)
    {
        RecalculateGlyphList();
        if (!_glyphs.FindXYFromPosition(position, out var x, out var y))
        {
            return;
        }

        var visibleWidth = Box.Width - _marginRight - _marginLeft;
        var visibleHeight = Box.Height - _marginBottom - _marginTop;
        if (OneLine)
        {
            var scrollY = Math.Max(0, PaperHeight - visibleHeight);
            var scrollX = x + 2 > visibleWidth + ScrollX ? x + 2 - visibleWidth : x;
            SetScrollableXY(scrollX, scrollY);
        }
        else
        {
            var scrollX = Math.Max(0, PaperWidth - visibleWidth);
            var lineHeight = Math.Min(CurrentFont?.MaxCharHeight ?? 0, visibleHeight);
            var scrollY = lineHeight + y > visibleHeight + ScrollY ? y + lineHeight - visibleHeight : y;
            SetScrollableXY(scrollX, scrollY);
        }

        MakeRootDirtyHere();
    }

    // ---------------------------------------------------------------- Focus and caret

    /// <summary>
    /// The element gained or lost the keyboard focus (<c>UIElement_Text::ListenToElementMessage</c> for message 0x2F, 11.4186
    /// 0x004687C0, 11.6096 0x00468A50): an editable field takes typed characters while focused; an editable or selectable
    /// field turns text mode on (<see cref="UiInputRouter.SetTextMode"/>) and ticks its caret while focused, and losing the
    /// focus drops the selection and turns text mode off.
    /// </summary>
    protected internal override void OnFocusChanged(bool focused)
    {
        if (!Editable && !Selectable)
        {
            return;
        }

        if (Editable)
        {
            if (focused)
            {
                Manager.Input.RegisterCharacterHandler(this);
            }
            else
            {
                Manager.Input.UnregisterCharacterHandler(this);
            }
        }

        if (focused)
        {
            Manager.Input.SetTextMode(true);
            Manager.RegisterTick(this);
        }
        else
        {
            Deselect();
            Manager.Input.SetTextMode(false);
            Manager.UnregisterTick(this);
        }

        MakeRootDirtyHere();
    }

    /// <summary>
    /// <c>UIElement_Text::RegisterInputMaps</c> (11.4186 0x00467350, 11.6096 0x004675E0): after dropping its old maps, the
    /// element registers the scrollable's; an editable field also the text map 7 and, ten below, the keyboard stop map 1
    /// (so no lower map sees a key while it has the focus); an editable or selectable one the clipboard map 8.
    /// </summary>
    protected internal override void RegisterInputMaps(int priority)
    {
        var input = Manager.Input;
        input.UnregisterCallback(this);
        if (Editable && HasFocus)
        {
            input.RegisterCharacterHandler(this);
        }

        base.RegisterInputMaps(priority);
        if (Editable)
        {
            input.RegisterInputMap(UiInputRouter.KeyboardStopMap, this, priority - 10);
            input.RegisterInputMap(UiInputRouter.TextEditMap, this, priority);
        }

        if (Editable || Selectable)
        {
            input.RegisterInputMap(UiInputRouter.TextClipboardMap, this, priority);
        }
    }

    /// <summary><c>UIElement_Text::UnregisterInputMaps</c> (11.4186 0x004673D0, 11.6096 0x00467660): the reverse.</summary>
    protected internal override void UnregisterInputMaps()
    {
        base.UnregisterInputMaps();
        var input = Manager.Input;
        input.UnregisterInputMap(UiInputRouter.KeyboardStopMap, this);
        input.UnregisterInputMap(UiInputRouter.TextEditMap, this);
        input.UnregisterInputMap(UiInputRouter.TextClipboardMap, this);
    }

    /// <summary>
    /// <c>UIElement_Text::OnAction</c> (11.4186 0x0046A260, 11.6096 0x0046A4F0): an action the parents do not take, when it
    /// starts, edits: the arrows, Home and End (with Ctrl, by word or to the text's ends), Page Up and Down, copy, cut and
    /// paste, Delete and Backspace; Enter (0x25) and Escape (0x27) give the focus up when the field says so (properties
    /// 0xCC and 0xCB). Every editing action is taken, even one that changed nothing.
    /// </summary>
    protected internal override bool OnAction(in UiInputAction action)
    {
        if (base.OnAction(action) || !action.Activation.Start)
        {
            return false;
        }

        switch (action.ActionId)
        {
            case TextActions.Up:
                MoveCursor(UiTextDirection.Up);
                return true;
            case TextActions.Down:
                MoveCursor(UiTextDirection.Down);
                return true;
            case TextActions.Left:
                MoveCursor(UiTextDirection.Left);
                return true;
            case TextActions.WordLeft:
                MoveCursor(UiTextDirection.PreviousWord);
                return true;
            case TextActions.Right:
                MoveCursor(UiTextDirection.Right);
                return true;
            case TextActions.WordRight:
                MoveCursor(UiTextDirection.NextWord);
                return true;
            case TextActions.PageUp:
                ScrollPage(up: true);
                return true;
            case TextActions.PageDown:
                ScrollPage(up: false);
                return true;
            case TextActions.LineStart:
                MoveCursor(UiTextDirection.LineStart);
                return true;
            case TextActions.TextStart:
                MoveCursor(UiTextDirection.TextStart);
                return true;
            case TextActions.LineEnd:
                MoveCursor(UiTextDirection.LineEnd);
                return true;
            case TextActions.TextEnd:
                MoveCursor(UiTextDirection.TextEnd);
                return true;
            case TextActions.Copy:
                Copy();
                return true;
            case TextActions.Cut:
                Cut();
                return true;
            case TextActions.Paste:
                Paste();
                return true;
            case TextActions.Delete:
                DeleteForward();
                return true;
            case TextActions.Backspace:
                DeleteBackward();
                return true;
            case TextActions.Escape:
                if (LoseFocusOnEscape)
                {
                    RelinquishFocus();
                }

                return true;
            case TextActions.Accept:
                AcceptInput?.Invoke();
                if (LoseFocusOnAcceptInput)
                {
                    RelinquishFocus();
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>UIElement_Text::ScrollPage</c> (11.4186 0x00466440, 11.6096 0x00466570) with <c>MoveCursor</c>'s page directions:
    /// the view moves a page and the caret goes to the start of the line now at the view's top.
    /// </summary>
    private void ScrollPage(bool up)
    {
        RecalculateGlyphList();
        var delta = InqScrollDelta(horizontal: false, towardStart: up, page: true);
        SetScrollableXY(ScrollX, ScrollY + delta);
        RecalculateGlyphList();
        var line = _glyphs.FindLineFromY(ScrollY - _marginTop);
        if (line < _glyphs.Lines.Count)
        {
            SetCursorPosition(_glyphs.Lines[line].Index);
        }
    }

    /// <summary>
    /// <c>UIElement_Text::MouseDown</c> (11.4186 0x00469370, 11.6096 0x00469600): after the scrollable's press (which
    /// activates the window and takes the focus), a left press on an editable or selectable field puts the caret under the
    /// mouse and starts a mouse selection (with Shift, from the old caret); a field that selects all on gaining the focus
    /// (property 0xD1) selects all on the press that gives it the focus.
    /// </summary>
    public override void MouseDown(int x, int y, uint button)
    {
        var hadFocus = HasFocus;
        base.MouseDown(x, y, button);
        if (IsMoving || IsResizing || !IsMouseOverTop || button != UiMouseActions.Left || (!Editable && !Selectable))
        {
            return;
        }

        if (!hadFocus && TryGetBoolProperty(UiPropertyIds.TextSelectAllOnGainFocusMouseDown, out var selectAll) && selectAll)
        {
            SelectAll();
            return;
        }

        var (px, py) = ToTextPoint(x, y);
        var anchor = _selecting ? SelectionStart : CursorPosition;
        SetCursorPosition(DeterminePositionFromXY(px, py), UiCaretSelection.DontSelect);
        Activate();
        if (!Selectable)
        {
            return;
        }

        SetSelecting(true);
        _mouseSelecting = true;
        if (Manager.ShiftKeyDown)
        {
            SetSelectionStart(anchor);
            SetSelectionEnd(CursorPosition);
        }
        else
        {
            SetSelectionStart(CursorPosition);
        }
    }

    /// <summary><c>UIElement_Text::MouseUp</c> (11.4186 0x004694F0, 11.6096 0x00469780): a mouse selection ends where the button came up.</summary>
    public override void MouseUp(int x, int y, uint button)
    {
        base.MouseUp(x, y, button);
        if (_mouseSelecting)
        {
            var (px, py) = ToTextPoint(x, y);
            SetCursorPosition(DeterminePositionFromXY(px, py));
            _mouseSelecting = false;
        }
    }

    /// <summary><c>UIElement_Text::MouseMove</c> (11.4186 0x004695F0, 11.6096 0x00469880): a mouse selection follows the mouse.</summary>
    public override void MouseMove(int x, int y)
    {
        if (!_mouseSelecting)
        {
            base.MouseMove(x, y);
            return;
        }

        var (px, py) = ToTextPoint(x, y);
        SetCursorPosition(DeterminePositionFromXY(px, py));
    }

    /// <summary>
    /// <c>UIElement_Text::DeterminePositionFromXY</c> (11.4186 0x004688F0, 11.6096 0x00468B80): the glyph position at
    /// (<paramref name="x"/>, <paramref name="y"/>) of the view less the margins, on the line at that height, nearest the
    /// point after the line's justification. As retail, the point arrives without the margins and the justification
    /// includes the left margin again.
    /// </summary>
    public int DeterminePositionFromXY(int x, int y)
    {
        RecalculateGlyphList();
        var line = _glyphs.FindLineFromY(y + ScrollY);
        var justification = CalcJustification(LineWidth(line), true);
        return _glyphs.FindPosFromLineAndPixels(line, unchecked((uint)(ScrollX + x - justification)), true, out var position)
            ? Math.Min(position, _glyphs.Count)
            : position;
    }

    private (int X, int Y) ToTextPoint(int x, int y)
    {
        var screen = ScreenBox;
        return (x - _marginLeft - screen.X0, y - _marginTop - screen.Y0);
    }

    /// <summary>The text-editing actions of input maps 7 and 8 (engine key map 0x14000002) that <see cref="OnAction"/> handles.</summary>
    public static class TextActions
    {
        /// <summary>0x16: Left.</summary>
        public const uint Left = 0x16;

        /// <summary>0x17: Right.</summary>
        public const uint Right = 0x17;

        /// <summary>0x18: Ctrl+Left, a word left.</summary>
        public const uint WordLeft = 0x18;

        /// <summary>0x19: Ctrl+Right, a word right.</summary>
        public const uint WordRight = 0x19;

        /// <summary>0x1A: Home, to the line's start.</summary>
        public const uint LineStart = 0x1A;

        /// <summary>0x1B: End, to the line's end.</summary>
        public const uint LineEnd = 0x1B;

        /// <summary>0x1C: Ctrl+Home, to the text's start.</summary>
        public const uint TextStart = 0x1C;

        /// <summary>0x1D: Ctrl+End, to the text's end.</summary>
        public const uint TextEnd = 0x1D;

        /// <summary>0x1E: Up.</summary>
        public const uint Up = 0x1E;

        /// <summary>0x1F: Down.</summary>
        public const uint Down = 0x1F;

        /// <summary>0x20: Page Up.</summary>
        public const uint PageUp = 0x20;

        /// <summary>0x21: Page Down.</summary>
        public const uint PageDown = 0x21;

        /// <summary>0x22: copy (Ctrl+C, Ctrl+Insert).</summary>
        public const uint Copy = 0x22;

        /// <summary>0x23: cut (Ctrl+X, Shift+Delete).</summary>
        public const uint Cut = 0x23;

        /// <summary>0x24: paste (Ctrl+V, Shift+Insert).</summary>
        public const uint Paste = 0x24;

        /// <summary>0x25: accept (Enter).</summary>
        public const uint Accept = 0x25;

        /// <summary>0x26: Delete.</summary>
        public const uint Delete = 0x26;

        /// <summary>0x27: Escape.</summary>
        public const uint Escape = 0x27;

        /// <summary>0x28: Backspace.</summary>
        public const uint Backspace = 0x28;
    }

    /// <inheritdoc/>
    void IUiTickTarget.Tick(double now) => OnTick(now);

    /// <summary>
    /// <c>UIElement_Text::Global_Loop</c> (11.4186 0x00469FC0, 11.6096 0x0046A250): the caret blinks every caret-blink time
    /// while the field has the focus, and shows at once after it moves; only its box is repainted.
    /// </summary>
    protected internal override void OnTick(double now)
    {
        if ((!Editable && !Selectable) || !HasFocus)
        {
            return;
        }

        bool on;
        if (_lastCursorMoveTime > _lastFlashFlipTime)
        {
            on = true;
        }
        else if (Manager.Services.CaretBlinkSeconds > now - _lastFlashFlipTime)
        {
            return;
        }
        else
        {
            on = !_caretOn;
        }

        _lastFlashFlipTime = now;
        _caretOn = on;
        if (_lastCursor.Width > 0 && _lastCursor.Height > 0)
        {
            AddDirtyRect(_lastCursor);
        }
    }
}
