using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>
/// One glyph of laid-out text (retail's <c>Glyph</c>): the character, its font and colour, and its size: the font's
/// advance with both offsets (<c>Font::GetCharWidthA</c>) and the font's line height. A character no font has gets no
/// font and no size (it is laid out at zero width and not drawn).
/// </summary>
/// <param name="Char">The character.</param>
/// <param name="Font">The font, or null.</param>
/// <param name="Color">The colour, kept as retail keeps it (float channels).</param>
/// <param name="Width">The advance.</param>
/// <param name="Height">The line height.</param>
public readonly record struct UiGlyph(char Char, IUiFont? Font, UiRgbaColor Color, int Width, int Height)
{
    /// <summary><c>Glyph::IsWhiteSpace</c> (11.4186 0x004747D0, 11.6096 0x00474B90): tab, carriage return or space.</summary>
    public bool IsWhiteSpace => Char is '\t' or '\r' or ' ';

    /// <summary><c>Glyph::IsNewLine</c> (11.4186 0x004747F0, 11.6096 0x00474BB0): a line feed.</summary>
    public bool IsNewLine => Char == '\n';
}

/// <summary>One laid-out line (retail's <c>GlyphLine</c>): its first glyph, its width and its height.</summary>
/// <param name="Index">The index of the line's first glyph.</param>
/// <param name="Width">The line's width in pixels.</param>
/// <param name="Height">The line's height in pixels.</param>
public readonly record struct UiGlyphLine(int Index, int Width, int Height);

/// <summary>
/// A run of glyphs and their line breaks: retail's <c>GlyphList</c> (11.4186 0x00472520-0x00473D70, 11.6096
/// 0x004728E0-0x00474130), with <c>LinebreakUtils</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Line breaking</b> (<see cref="Recalculate"/>, <c>GlyphList::Recalculate</c>, 11.4186 0x00473800, 11.6096
/// 0x00473BC0): a line may break before a glyph when <see cref="CanBreakLineAt"/> allows it (after whitespace, or
/// around East Asian characters, but never before ! ) , . ? and their full-width forms or after an opening parenthesis;
/// always after a newline), and a word too long for the line breaks before the glyph that overflows. The break is taken
/// when the line's width without trailing whitespace passes the margin; the finished line's width leaves out the
/// whitespace it ends on, and its height is the tallest glyph before the break. Text ending in a newline gets an empty
/// last line.
/// </para>
/// <para>
/// <b>The maximum length</b> (<c>GlyphList::AddText</c>, 11.4186 0x00473190, 11.6096 0x00473550): text added past it is
/// cut, from the start when trimming from the top (chat), else at the insertion point.
/// </para>
/// <para>
/// Retail recalculates from the line before the first change; Lifestone recalculates the whole run, which gives the
/// same lines. Reusing the layout allocates nothing once its lists have grown.
/// </para>
/// </remarks>
public sealed class GlyphLayout
{
    private readonly List<UiGlyph> _glyphs = [];
    private readonly List<UiGlyphLine> _lines = [];
    private int _lastWidth = -1;
    private bool _lastOneLine;
    private bool _valid;

    /// <summary>The glyphs.</summary>
    public IReadOnlyList<UiGlyph> Glyphs => _glyphs;

    /// <summary>The lines of the last <see cref="Recalculate"/>.</summary>
    public IReadOnlyList<UiGlyphLine> Lines => _lines;

    /// <summary>The number of glyphs.</summary>
    public int Count => _glyphs.Count;

    /// <summary>The most glyphs the run keeps (<c>m_nMaxCharacters</c>, property 0x1E; 0xFFFF by default).</summary>
    public int MaxCharacters { get; set; } = 0xFFFF;

    /// <summary>True when text past the maximum is cut from the start (<c>m_bTrimFromTop</c>, property 0x28).</summary>
    public bool TrimFromTop { get; set; }

    /// <summary><c>LinebreakUtils::IsWhiteSpace</c> (11.4186 0x00472520, 11.6096 0x004728E0).</summary>
    public static bool IsWhiteSpace(char c) => c is '\t' or '\r' or ' ';

    /// <summary><c>LinebreakUtils::IsNonBeginningChar</c> (11.4186 0x00472540, 11.6096 0x00472900): characters a line may not start with.</summary>
    public static bool IsNonBeginningChar(char c) =>
        c is '!' or ')' or ',' or '.' or '?' or '、' or '。' or 'ー' or '！' or '）' or '？' or 'ｰ' or 'ﾞ' or 'ﾟ';

    /// <summary><c>LinebreakUtils::IsNonEndingChar</c> (11.4186 0x004725B0, 11.6096 0x00472970): characters a line may not end with.</summary>
    public static bool IsNonEndingChar(char c) => c is '(' or '（';

    /// <summary><c>LinebreakUtils::IsEastAsianChar</c> (11.4186 0x004725D0, 11.6096 0x00472990).</summary>
    public static bool IsEastAsianChar(char c) =>
        c is (>= 'ᄀ' and <= 'ᇿ') or (>= '　' and <= '힯') or (>= '豈' and <= '﫿') or (>= '＀' and <= 'ￜ');

    /// <summary>
    /// <c>LinebreakUtils::CanBreakLineAt</c> (11.4186 0x00472610, 11.6096 0x004729D0): true when a line may break between
    /// <paramref name="previous"/> and <paramref name="current"/>.
    /// </summary>
    public static bool CanBreakLineAt(char previous, char current) =>
        previous == '\n'
        || ((IsWhiteSpace(previous) || IsEastAsianChar(current) || IsEastAsianChar(previous))
            && !IsNonBeginningChar(current) && !IsNonEndingChar(previous));

    /// <summary><c>GlyphList::Flush</c>: removes every glyph and line.</summary>
    public void Flush()
    {
        _glyphs.Clear();
        _lines.Clear();
        _valid = false;
    }

    /// <summary>
    /// <c>GlyphList::AddText</c>: inserts <paramref name="glyphs"/> at <paramref name="position"/> (clamped to the end),
    /// then cuts the run back to <see cref="MaxCharacters"/>. Returns the glyphs added less those cut.
    /// </summary>
    public int AddText(int position, ReadOnlySpan<UiGlyph> glyphs)
    {
        position = Math.Clamp(position, 0, _glyphs.Count);
        var cursor = position;
        foreach (var glyph in glyphs)
        {
            _glyphs.Insert(cursor++, glyph);
        }

        _valid = false;
        var added = glyphs.Length;
        var deleted = 0;
        if (_glyphs.Count > MaxCharacters)
        {
            var excess = Math.Min(_glyphs.Count - MaxCharacters, glyphs.Length);
            deleted = TrimFromTop ? Delete(0, excess) : Delete(cursor - excess, cursor);
        }

        return added - deleted;
    }

    /// <summary><c>GlyphList::Delete</c> (11.4186 0x004730A0, 11.6096 0x00473460): removes glyphs [start, end), clamped; returns how many.</summary>
    public int Delete(int start, int end)
    {
        start = Math.Min(start, _glyphs.Count - 1);
        end = Math.Min(end, _glyphs.Count);
        if (start < 0 || start >= end)
        {
            return 0;
        }

        _glyphs.RemoveRange(start, end - start);
        _valid = false;
        return end - start;
    }

    /// <summary>True when the characters of all glyphs are <paramref name="text"/>; allocates nothing (a per-frame change check, where <see cref="GetText"/> allocates a string).</summary>
    public bool TextEquals(string text)
    {
        if (text.Length != _glyphs.Count)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (_glyphs[i].Char != text[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The characters of glyphs [<paramref name="start"/>, <paramref name="end"/>) (<c>GlyphList::InqText</c>).</summary>
    public string GetText(int start, int end)
    {
        start = Math.Clamp(start, 0, _glyphs.Count);
        end = Math.Clamp(end, start, _glyphs.Count);
        return string.Create(end - start, (this, start), static (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = state.Item1._glyphs[state.start + i].Char;
            }
        });
    }

    /// <summary>
    /// <c>GlyphList::Recalculate</c>: lays the glyphs out in lines no wider than <paramref name="marginWidth"/> (one line
    /// when <paramref name="oneLine"/>), and returns the widest line and the total height. Nothing is redone when neither
    /// the glyphs nor the arguments changed.
    /// </summary>
    public void Recalculate(int marginWidth, bool oneLine, out int width, out int height)
    {
        if (_valid && marginWidth == _lastWidth && oneLine == _lastOneLine)
        {
            Measure(out width, out height);
            return;
        }

        _lastWidth = marginWidth;
        _lastOneLine = oneLine;
        _lines.Clear();
        width = 0;
        height = 0;
        var lineStart = 0;
        var currentWidth = 0;
        var currentHeight = 0;
        var accumulated = 0;
        var lastWrappable = 0;
        var wrappableHeight = 0;
        var wrappable = false;
        var mustWrap = false;
        var lastEndOfLine = -1;
        var previous = -1;
        for (var i = 0; i < _glyphs.Count; i++)
        {
            var glyph = _glyphs[i];
            var glyphWidth = glyph.Width;
            if (!oneLine)
            {
                if (previous >= 0 && CanBreakLineAt(_glyphs[previous].Char, glyph.Char))
                {
                    lastWrappable = i;
                    lastEndOfLine = previous;
                    wrappableHeight = currentHeight;
                    accumulated = 0;
                    wrappable = true;
                    if (_glyphs[previous].IsNewLine)
                    {
                        mustWrap = true;
                    }
                }
                else if (!glyph.IsWhiteSpace && !glyph.IsNewLine && accumulated + glyphWidth > marginWidth)
                {
                    // A word longer than the line breaks before the glyph that overflows.
                    lastWrappable = i;
                    lastEndOfLine = previous;
                    wrappableHeight = currentHeight;
                    accumulated = 0;
                    wrappable = true;
                }
            }

            currentWidth += glyphWidth;
            accumulated += glyphWidth;
            var visibleWidth = glyph.IsWhiteSpace || glyph.IsNewLine ? currentWidth - glyphWidth : currentWidth;
            if (mustWrap || (wrappable && visibleWidth > marginWidth))
            {
                var lineWidth = currentWidth - accumulated;
                if (lastEndOfLine >= 0 && (_glyphs[lastEndOfLine].IsWhiteSpace || _glyphs[lastEndOfLine].IsNewLine))
                {
                    lineWidth -= _glyphs[lastEndOfLine].Width;
                }

                _lines.Add(new UiGlyphLine(lineStart, lineWidth, wrappableHeight));
                width = Math.Max(width, lineWidth);
                height += wrappableHeight;
                lineStart = lastWrappable;
                currentWidth = accumulated;
                currentHeight = 0;
                wrappable = false;
                mustWrap = false;
            }

            currentHeight = Math.Max(glyph.Height, currentHeight);
            previous = i;
        }

        _lines.Add(new UiGlyphLine(lineStart, currentWidth, currentHeight));
        width = Math.Max(width, currentWidth);
        height += currentHeight;
        if (previous >= 0 && _glyphs[previous].IsNewLine)
        {
            var last = _glyphs[previous];
            _lines.Add(new UiGlyphLine(_glyphs.Count, 0, last.Height));
            height += last.Height;
        }

        _valid = true;
    }

    /// <summary><c>GlyphList::FindCurrentLine</c> (11.4186 0x004726F0, 11.6096 0x00472AB0): the line holding position <paramref name="position"/> and its first glyph.</summary>
    public bool FindCurrentLine(int position, out int line, out int lineStart)
    {
        line = 0;
        lineStart = 0;
        if (position > _glyphs.Count)
        {
            return false;
        }

        if (_lines.Count == 0)
        {
            return true;
        }

        var l = 0;
        while (l != _lines.Count - 1 && position >= _lines[l + 1].Index)
        {
            l++;
        }

        line = l;
        lineStart = _lines[l].Index;
        return true;
    }

    /// <summary><c>GlyphList::FindLineFromY</c> (11.4186 0x00472770, 11.6096 0x00472B30): the line at height <paramref name="y"/> (the last when below the text).</summary>
    public int FindLineFromY(int y)
    {
        if (y <= 0)
        {
            return 0;
        }

        var top = 0;
        for (var l = 0; l < _lines.Count; l++)
        {
            top += _lines[l].Height;
            if (top >= y)
            {
                return l;
            }
        }

        return Math.Max(0, _lines.Count - 1);
    }

    /// <summary>
    /// <c>GlyphList::FindCompleteLineFromY</c> (11.4186 0x004727F0, 11.6096 0x00472BB0): the last line wholly above
    /// <paramref name="y"/> (the line count when every line fits).
    /// </summary>
    public int FindCompleteLineFromY(int y)
    {
        if (_lines.Count <= 1)
        {
            return _lines.Count;
        }

        var bottom = _lines[0].Height;
        for (var l = 1; l < _lines.Count; l++)
        {
            bottom += _lines[l].Height;
            if (bottom > y)
            {
                return l - 1;
            }
        }

        return _lines.Count;
    }

    /// <summary><c>GlyphList::FindNextLine</c> (11.4186 0x00472840, 11.6096 0x00472C00): the line after the one holding <paramref name="position"/> (or the last).</summary>
    public void FindNextLine(int position, out int line, out int lineStart)
    {
        FindCurrentLine(position, out line, out lineStart);
        if (_lines.Count != 0)
        {
            line = Math.Min(_lines.Count - 1, line + 1);
            lineStart = _lines[line].Index;
        }
    }

    /// <summary><c>GlyphList::FindPrevLine</c> (11.4186 0x004728A0, 11.6096 0x00472C60): the line before the one holding <paramref name="position"/> (or the first).</summary>
    public void FindPrevLine(int position, out int line, out int lineStart)
    {
        FindCurrentLine(position, out line, out lineStart);
        if (_lines.Count != 0)
        {
            line = Math.Max(0, line - 1);
            lineStart = _lines[line].Index;
        }
    }

    /// <summary><c>GlyphList::FindPixelsFromPos</c> (11.4186 0x00472B40, 11.6096 0x00472F00): the width from the start of <paramref name="position"/>'s line to it.</summary>
    public bool FindPixelsFromPos(int position, out int pixels)
    {
        pixels = 0;
        if (position > _glyphs.Count || !FindCurrentLine(position, out _, out var lineStart))
        {
            return false;
        }

        for (var i = lineStart; i < position; i++)
        {
            pixels += _glyphs[i].Width;
        }

        return true;
    }

    /// <summary><c>GlyphList::FindXYFromPosition</c> (11.4186 0x00472EE0, 11.6096 0x004732A0): where <paramref name="position"/> is, from the text's top-left.</summary>
    public bool FindXYFromPosition(int position, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!FindCurrentLine(position, out var line, out _))
        {
            return false;
        }

        for (var l = 0; l < line; l++)
        {
            y += _lines[l].Height;
        }

        return FindPixelsFromPos(position, out x);
    }

    /// <summary>
    /// <c>GlyphList::FindPosFromLineAndPixels</c> (11.4186 0x004732D0, 11.6096 0x00473690): the position
    /// <paramref name="pixels"/> into line <paramref name="line"/>; with <paramref name="nearest"/>, a glyph is passed
    /// once the point is beyond its middle. Past the line's end, the position before the next line (or the text's end).
    /// </summary>
    public bool FindPosFromLineAndPixels(int line, uint pixels, bool nearest, out int position)
    {
        position = 0;
        if (line < 0 || line >= _lines.Count)
        {
            return false;
        }

        var start = _lines[line].Index;
        position = start;
        var next = Math.Min(_lines.Count - 1, line + 1);
        var end = next == line ? _glyphs.Count : _lines[next].Index;
        if (pixels <= (uint)_lines[line].Width)
        {
            var remaining = unchecked((int)pixels);
            var divisor = nearest ? 2 : 1;
            for (var i = start; i < end && i < _glyphs.Count; i++)
            {
                var glyph = _glyphs[i];
                if (remaining <= glyph.Width / divisor || (glyph.IsNewLine && i + 1 == end))
                {
                    break;
                }

                remaining -= glyph.Width;
                position++;
            }

            return true;
        }

        FindNextLine(start, out _, out var nextStart);
        position = line == _lines.Count - 1 ? _glyphs.Count : nextStart - 1;
        return true;
    }

    /// <summary><c>GlyphList::FindNextWord</c> (11.4186 0x00472A80, 11.6096 0x00472E40): past the whitespace that follows the current word.</summary>
    public int FindNextWord(int position)
    {
        var result = position;
        var seenSpace = false;
        for (var i = position; i < _glyphs.Count; i++)
        {
            if (_glyphs[i].IsWhiteSpace)
            {
                seenSpace = true;
            }
            else if (seenSpace)
            {
                break;
            }

            result++;
        }

        return result;
    }

    /// <summary><c>GlyphList::FindPrevWord</c> (11.4186 0x00472AE0, 11.6096 0x00472EA0): the start of the word before <paramref name="position"/>.</summary>
    public int FindPrevWord(int position)
    {
        var result = 0;
        var afterSpace = true;
        for (var i = 0; i < position && i < _glyphs.Count; i++)
        {
            if (_glyphs[i].IsWhiteSpace)
            {
                afterSpace = true;
            }
            else if (afterSpace)
            {
                result = i;
                afterSpace = false;
            }
        }

        return result;
    }

    private void Measure(out int width, out int height)
    {
        width = 0;
        height = 0;
        foreach (var line in _lines)
        {
            width = Math.Max(width, line.Width);
            height += line.Height;
        }
    }
}
