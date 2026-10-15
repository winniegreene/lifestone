using DatReaderWriter.DBObjs;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Ui;

/// <summary>
/// One glyph of a retail bitmap font (retail's <c>FontCharDesc</c>, 11 bytes in the dat): the character, its rectangle in
/// the font's atlases and its spacing.
/// </summary>
/// <param name="Unicode">The UTF-16 character (<c>m_Unicode</c>).</param>
/// <param name="OffsetX">The glyph rectangle's left column in the atlas (<c>m_OffsetX</c>).</param>
/// <param name="OffsetY">The glyph rectangle's top row in the atlas (<c>m_OffsetY</c>).</param>
/// <param name="Width">The rectangle's width in pixels (<c>m_Width</c>).</param>
/// <param name="Height">The rectangle's height in pixels (<c>m_Height</c>).</param>
/// <param name="HorizontalOffsetBefore">Pixels added before the glyph (<c>m_HorizontalOffsetBefore</c>; may be negative).</param>
/// <param name="HorizontalOffsetAfter">Pixels added after the glyph (<c>m_HorizontalOffsetAfter</c>; may be negative).</param>
/// <param name="VerticalOffsetBefore">Rows added above the glyph (<c>m_VerticalOffsetBefore</c>).</param>
public readonly record struct UiGlyph(
    char Unicode,
    ushort OffsetX,
    ushort OffsetY,
    byte Width,
    byte Height,
    sbyte HorizontalOffsetBefore,
    sbyte HorizontalOffsetAfter,
    sbyte VerticalOffsetBefore)
{
    /// <summary>
    /// The glyph's advance as retail computes it (<c>Font::GetCharWidthA</c>, 11.4186 0x004433F0, 11.6096 0x00443550):
    /// the width and both horizontal offsets read as <b>unsigned</b> bytes, added, and kept to the low byte, so a
    /// negative offset wraps back (5 + 0xFF + 0 is 4) and a sum above 255 wraps too. <c>Glyph::SetFont</c> stores the
    /// result as an unsigned byte.
    /// </summary>
    public byte Advance => unchecked((byte)(Width + (byte)HorizontalOffsetBefore + (byte)HorizontalOffsetAfter));
}

/// <summary>
/// A retail bitmap font (Font 0x40xxxxxx in <c>client_portal.dat</c>): its glyphs, its line metrics and the RenderSurfaces
/// its glyphs are cut from. Immutable and safe from any thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Glyph lookup</b> re-expresses <c>Font::GenerateCharacterMap</c> (11.4186 0x00443470, 11.6096 0x004435D0) and
/// <c>Font::GetCharDesc</c> (11.4186 0x00443360, 11.6096 0x004434C0). The map covers every character from the font's
/// lowest to its highest; it starts zeroed and each glyph writes its index into it, so a character inside that range
/// that the font does not hold resolves to <b>glyph 0</b> (a space in 45 of the 49 retail fonts, U+0000 in the four
/// Gulim fonts), not to '?'. Only a character outside the range falls back to '?' (63), and when '?' is outside the
/// range too there is no glyph. <c>Font::ContainsChar</c> (11.4186 0x00443420, 11.6096 0x00443580) is true for every
/// character inside the range for the same reason; <c>Glyph::SetFont</c> relies on it (<see cref="UiFonts.ResolveGlyphFont"/>).
/// This is retail's behaviour, kept as is: a missing character draws as the font's first glyph.
/// </para>
/// <para>
/// <b>Metrics.</b> A line is <see cref="MaxCharHeight"/> rows tall (<c>Glyph::SetFont</c> gives every glyph that
/// height); <see cref="BaselineOffset"/> is the baseline's row within it. Glyph rectangles include
/// <see cref="HorizontalBorderPixels"/>/<see cref="VerticalBorderPixels"/> of room for the outline that the background
/// atlas draws.
/// </para>
/// <para>
/// <b>Atlases.</b> Every retail font atlas is an A8 RenderSurface (FontTests), decoded with
/// <see cref="TextureLoader.DecodeRenderSurface"/> in <see cref="TextureDecodeMode.Native"/> mode as
/// <see cref="DecodedTextureFormat.Alpha8"/>, so text can be tinted with any colour. 37 fonts have a background
/// (outline) atlas the same size as the foreground one; the 12 without have <see cref="BackgroundSurfaceId"/> 0.
/// </para>
/// </remarks>
public sealed class UiFont
{
    /// <summary>The character <c>Font::GetCharDesc</c> falls back to when a character is outside the font's range.</summary>
    public const char FallbackCharacter = '?';

    private readonly UiGlyph[] _glyphs;
    private readonly ushort[] _characterMap;

    private UiFont(uint id, uint maxCharHeight, uint maxCharWidth, UiGlyph[] glyphs, uint horizontalBorder, uint verticalBorder,
        uint baseline, uint foreground, uint background)
    {
        Id = id;
        MaxCharHeight = checked((int)maxCharHeight);
        MaxCharWidth = checked((int)maxCharWidth);
        _glyphs = glyphs;
        HorizontalBorderPixels = checked((int)horizontalBorder);
        VerticalBorderPixels = checked((int)verticalBorder);
        BaselineOffset = checked((int)baseline);
        ForegroundSurfaceId = foreground;
        BackgroundSurfaceId = background;

        // Font::GenerateCharacterMap: the lowest and highest characters, a zeroed map over that range, and each glyph's
        // index written at its character (a later glyph with the same character wins, as retail's loop overwrites).
        var min = char.MaxValue;
        var max = char.MinValue;
        foreach (var glyph in glyphs)
        {
            min = glyph.Unicode < min ? glyph.Unicode : min;
            max = glyph.Unicode > max ? glyph.Unicode : max;
        }

        MinChar = min;
        MaxChar = max;
        _characterMap = new ushort[max - min + 1];
        for (var i = 0; i < glyphs.Length; i++)
        {
            _characterMap[glyphs[i].Unicode - min] = checked((ushort)i);
        }
    }

    /// <summary>The font's DID (0x40xxxxxx).</summary>
    public uint Id { get; }

    /// <summary>The line height in pixels (<c>maxCharHeight</c>); every glyph of the font is laid out this tall.</summary>
    public int MaxCharHeight { get; }

    /// <summary>The widest glyph rectangle in pixels (<c>maxCharWidth</c>).</summary>
    public int MaxCharWidth { get; }

    /// <summary>The baseline's row within a line (<c>m_BaselineOffset</c>).</summary>
    public int BaselineOffset { get; }

    /// <summary>Columns of outline room on each side of a glyph rectangle (<c>m_NumHorizontalBorderPixels</c>).</summary>
    public int HorizontalBorderPixels { get; }

    /// <summary>Rows of outline room above and below a glyph rectangle (<c>m_NumVerticalBorderPixels</c>).</summary>
    public int VerticalBorderPixels { get; }

    /// <summary>The foreground atlas RenderSurface (<c>m_ForegroundSurfaceDataID</c>).</summary>
    public uint ForegroundSurfaceId { get; }

    /// <summary>The background (outline) atlas RenderSurface (<c>m_BackgroundSurfaceDataID</c>), or 0 when the font has none.</summary>
    public uint BackgroundSurfaceId { get; }

    /// <summary>The lowest character the font holds (<c>minUnicodeChar</c>).</summary>
    public char MinChar { get; }

    /// <summary>The highest character the font holds (<c>maxUnicodeChar</c>).</summary>
    public char MaxChar { get; }

    /// <summary>The glyphs in dat order (<c>charDescs</c>); an index from <see cref="GetCharDescIndex"/> points here.</summary>
    public IReadOnlyList<UiGlyph> Glyphs => _glyphs;

    /// <summary>Converts a decoded Font record.</summary>
    /// <exception cref="InvalidDataException">The font holds no glyph (retail's <c>Font::InitLoad</c> fails for it).</exception>
    public static UiFont FromRecord(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.CharDescs.Count == 0)
        {
            throw new InvalidDataException(
                $"TODO(retailui-data): Font 0x{font.Id:X8} holds no glyph; retail's Font::InitLoad fails for it (GenerateCharacterMap finds no range).");
        }

        var glyphs = font.CharDescs
            .Select(c => new UiGlyph((char)c.Unicode, c.OffsetX, c.OffsetY, c.Width, c.Height, c.HorizontalOffsetBefore, c.HorizontalOffsetAfter, c.VerticalOffsetBefore))
            .ToArray();
        return new UiFont(font.Id, font.MaxCharHeight, font.MaxCharWidth, glyphs, font.NumHorizontalBorderPixels, font.NumVerticalBorderPixels,
            font.BaselineOffset, font.ForegroundSurfaceDataId, font.BackgroundSurfaceDataId);
    }

    /// <summary>
    /// <c>Font::GetCharDesc</c>: the index into <see cref="Glyphs"/> of the glyph that draws <paramref name="c"/>, or -1
    /// when there is none (see the remarks: glyph 0 for a missing character inside the range, '?' outside it).
    /// Allocation-free.
    /// </summary>
    public int GetCharDescIndex(char c)
    {
        if (c < MinChar || c > MaxChar)
        {
            if (c == FallbackCharacter || FallbackCharacter < MinChar || FallbackCharacter > MaxChar)
            {
                return -1;
            }

            c = FallbackCharacter;
        }

        return _characterMap[c - MinChar];
    }

    /// <summary><c>Font::GetCharDesc</c> as a value: the glyph that draws <paramref name="c"/>; false when there is none.</summary>
    public bool TryGetCharDesc(char c, out UiGlyph glyph)
    {
        var index = GetCharDescIndex(c);
        glyph = index < 0 ? default : _glyphs[index];
        return index >= 0;
    }

    /// <summary>
    /// <c>Font::GetCharWidthA</c>: the advance of the glyph that draws <paramref name="c"/> (<see cref="UiGlyph.Advance"/>,
    /// a byte that wraps), or 0 when there is none.
    /// </summary>
    public byte GetCharWidth(char c)
    {
        var index = GetCharDescIndex(c);
        return index < 0 ? (byte)0 : _glyphs[index].Advance;
    }

    /// <summary>
    /// <c>Font::ContainsChar</c>: true when <paramref name="c"/> is inside the font's range, whether or not a glyph was
    /// authored for it (the remarks). No '?' fallback.
    /// </summary>
    public bool ContainsChar(char c) => c >= MinChar && c <= MaxChar;

    /// <summary>Decodes the foreground atlas as alpha (<see cref="DecodedTextureFormat.Alpha8"/> for the retail fonts).</summary>
    /// <exception cref="KeyNotFoundException">The RenderSurface is in neither image dat.</exception>
    public DecodedTexture DecodeForeground(TextureLoader textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        return textures.DecodeRenderSurface(ForegroundSurfaceId, TextureDecodeMode.Native);
    }

    /// <summary>Decodes the background (outline) atlas, or returns null when the font has none.</summary>
    /// <exception cref="KeyNotFoundException">The RenderSurface is in neither image dat.</exception>
    public DecodedTexture? DecodeBackground(TextureLoader textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        return BackgroundSurfaceId == 0 ? null : textures.DecodeRenderSurface(BackgroundSurfaceId, TextureDecodeMode.Native);
    }

    /// <summary>For logs: <c>Font 0xXXXXXXXX</c>.</summary>
    public override string ToString() => $"Font 0x{Id:X8}";
}
