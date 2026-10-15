using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>
/// One glyph of a retail font (<c>FontCharDesc</c>): where it is in the atlas, its size, and how far it sits from the pen.
/// </summary>
/// <remarks>
/// The horizontal offsets are signed: of the 153,749 glyphs of the 49 retail fonts, 8,738 have a negative before-offset
/// and 7,048 a negative after-offset (no vertical offset is negative; UiRuntimeCensusTests), for example 'A' of font
/// 0x40000010 with an after-offset of -1 (0xFF). The 11.4186 decompile reads them as unsigned bytes in
/// <c>SurfaceWindow::DrawCharacter</c>, which would move such glyphs 255 pixels on; <c>Font::GetCharWidthA</c>'s byte
/// sum is the same either way. Lifestone reads them signed, as DatReaderWriter does (TODO(retailui-runtime): confirm
/// the sign extension in the 11.6096 machine code).
/// </remarks>
/// <param name="Unicode">The character.</param>
/// <param name="X">The glyph's column in the atlas.</param>
/// <param name="Y">The glyph's row in the atlas.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="HorizontalOffsetBefore">Blank pixels before it.</param>
/// <param name="HorizontalOffsetAfter">Blank pixels after it.</param>
/// <param name="VerticalOffsetBefore">Pixels between the line's top and the glyph's top.</param>
public readonly record struct UiGlyphDesc(char Unicode, ushort X, ushort Y, byte Width, byte Height, sbyte HorizontalOffsetBefore, sbyte HorizontalOffsetAfter, sbyte VerticalOffsetBefore);

/// <summary>
/// A retail bitmap font (Font, 0x40xxxxxx in the portal dat): metrics, glyph descs and the foreground and background
/// (outline) atlases. A narrow seam over the retail-UI data track's fonts (m2-plan.md R5, <c>Lifestone.Data.Ui.UiFont</c>),
/// implemented by <see cref="UiFontAdapter"/> (from <see cref="UiFontsSource"/>); tests implement it with fakes that keep
/// the same lookup rules.
/// </summary>
/// <remarks>
/// The lookup rules are retail's (docs/retail-ui-data.md §5, "Glyph lookup"): <c>Font::GenerateCharacterMap</c> (11.4186
/// 0x00443470, 11.6096 0x004435D0) maps every character from the font's lowest to its highest, so a character inside
/// that range the font does not hold resolves to glyph 0 (a space in most retail fonts), and only a character outside
/// the range falls back to '?'.
/// </remarks>
public interface IUiFont
{
    /// <summary>The font's DID.</summary>
    uint Did { get; }

    /// <summary>The tallest glyph's height (<c>maxCharHeight</c>): every glyph's line height.</summary>
    int MaxCharHeight { get; }

    /// <summary>The widest glyph's width (<c>maxCharWidth</c>).</summary>
    int MaxCharWidth { get; }

    /// <summary>The baseline's distance from the top (<c>m_BaselineOffset</c>).</summary>
    int BaselineOffset { get; }

    /// <summary>Pixels the background atlas extends each glyph left and right (<c>m_NumHorizontalBorderPixels</c>).</summary>
    int NumHorizontalBorderPixels { get; }

    /// <summary>Pixels the background atlas extends each glyph up and down (<c>m_NumVerticalBorderPixels</c>).</summary>
    int NumVerticalBorderPixels { get; }

    /// <summary>The foreground atlas (A8 for most fonts), or null.</summary>
    UiImage? Foreground { get; }

    /// <summary>The background (outline) atlas, or null.</summary>
    UiImage? Background { get; }

    /// <summary>
    /// <c>Font::ContainsChar</c> (11.4186 0x00443420, 11.6096 0x00443580): true when <paramref name="c"/> is inside the
    /// font's character range, whether or not a glyph was authored for it (<c>Glyph::SetFont</c> keeps the font then).
    /// </summary>
    bool ContainsChar(char c);

    /// <summary>
    /// <c>Font::GetCharDesc</c> (11.4186 0x00443360, 11.6096 0x004434C0): inside the font's range, the glyph for
    /// <paramref name="c"/>, or glyph 0 when the font lacks it; outside the range, the glyph for '?' when '?' is inside it;
    /// else false.
    /// </summary>
    bool TryGetCharDesc(char c, out UiGlyphDesc desc);
}

/// <summary>
/// Where fonts come from: by DID, or by FONT enum (group 9) for the fallbacks retail names by enum. Production:
/// <see cref="UiFontsSource"/>.
/// </summary>
public interface IUiFontSource
{
    /// <summary>The font <paramref name="did"/>, or null when it does not exist (<c>FontLocal::GetGlobalOrLocalFont</c>).</summary>
    IUiFont? GetFont(uint did);

    /// <summary>The font FONT enum <paramref name="fontEnum"/> names, or null (<c>DBObj::GetByEnum(enum, 9, ...)</c>).</summary>
    IUiFont? GetFontByEnum(uint fontEnum);
}

/// <summary>Font helpers re-expressing retail's inline arithmetic.</summary>
public static class UiFonts
{
    /// <summary>
    /// <c>Font::GetCharWidthA</c> (11.4186 0x004433F0, 11.6096 0x00443550): width plus both offsets of the glyph
    /// <see cref="IUiFont.TryGetCharDesc"/> finds, as a byte (it wraps past 255, as retail); 0 when it finds none.
    /// </summary>
    public static int GetCharWidth(this IUiFont font, char c)
    {
        ArgumentNullException.ThrowIfNull(font);
        return font.TryGetCharDesc(c, out var desc)
            ? (byte)(desc.Width + desc.HorizontalOffsetBefore + desc.HorizontalOffsetAfter)
            : 0;
    }
}
