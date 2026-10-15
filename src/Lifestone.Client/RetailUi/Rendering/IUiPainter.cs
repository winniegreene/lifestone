using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// The flags of <c>SurfaceWindow::DrawCharacter</c> (11.4186 0x00442BD0, 11.6096 0x00442D30), named after what the code
/// does with them.
/// </summary>
[Flags]
public enum UiTextFlags : uint
{
    /// <summary>Nothing: the advance is the glyph's width and it is drawn at x.</summary>
    None = 0,

    /// <summary>0x1: the advance is the font's widest glyph plus one.</summary>
    Monospace = 0x1,

    /// <summary>0x2: y names the baseline (the font's baseline offset is subtracted).</summary>
    Baseline = 0x2,

    /// <summary>0x100: write only the alpha channel (<c>SurfaceWindow::CopyAlpha</c>).</summary>
    AlphaChannelOnly = 0x100,

    /// <summary>0x1000: the glyph is drawn after its leading offset and advances by width plus both offsets (what text elements use).</summary>
    AdvanceWithOffsets = 0x1000,

    /// <summary>0x2000: skip the foreground atlas.</summary>
    SkipForeground = 0x2000,

    /// <summary>0x4000: draw the background (outline) atlas, grown by the font's border pixels.</summary>
    DrawBackground = 0x4000,

    /// <summary>0x8000: colour the atlas's alpha with the given colour even when the atlas has colours of its own.</summary>
    Colorize = 0x8000,
}

/// <summary>
/// What a region draws with: retail's drawing primitives on a UI surface (<c>Graphic::Draw</c>,
/// <c>SurfaceWindow::DrawCharacter</c>, <c>SurfaceWindow::NotColorBits</c>, <c>SurfaceWindow::FillArea</c>).
/// <see cref="SoftwareUiPainter"/> is the implementation; all coordinates are surface pixels.
/// </summary>
public interface IUiPainter
{
    /// <summary>
    /// <c>Graphic::Draw</c> (11.4186 0x00693B20, 11.6096 0x00694A40) of an element's image: resolves the DID (retail's
    /// default graphic when it does not resolve) and blits the part of <paramref name="bounds"/> inside
    /// <paramref name="dirty"/>, tiling, never stretching.
    /// </summary>
    /// <param name="image">The image and blit mode; nothing is drawn for <see cref="UiImageRef.None"/>.</param>
    /// <param name="alphaBlendMod">The opacity, 0-1.</param>
    /// <param name="bounds">The element's box.</param>
    /// <param name="dirty">The piece of it to repaint (within <paramref name="bounds"/>).</param>
    /// <param name="tiling">The tiling offset.</param>
    void DrawImage(UiImageRef image, float alphaBlendMod, UiBox bounds, UiBox dirty, (int X, int Y) tiling);

    /// <summary><c>Graphic::Draw</c> of an image already in hand.</summary>
    void DrawImage(UiImage image, UiBlitMode mode, float alphaBlendMod, UiBox bounds, UiBox dirty, (int X, int Y) tiling);

    /// <summary>
    /// <c>SurfaceWindow::DrawCharacter</c>: draws one glyph into <paramref name="window"/> and returns how far the pen
    /// advances (0 when the font has no such glyph, '?' included).
    /// </summary>
    /// <param name="x">The pen's column, relative to <paramref name="window"/>.</param>
    /// <param name="y">The pen's row, relative to <paramref name="window"/>.</param>
    /// <param name="font">The font.</param>
    /// <param name="c">The character.</param>
    /// <param name="argb">The colour for an alpha atlas (0xAARRGGBB).</param>
    /// <param name="flags">How to draw.</param>
    /// <param name="backgroundArgb">The outline colour for an alpha background atlas.</param>
    /// <param name="window">The window the glyph is clipped to, in surface coordinates.</param>
    int DrawCharacter(int x, int y, IUiFont font, char c, uint argb, UiTextFlags flags, uint backgroundArgb, UiBox window);

    /// <summary><c>SurfaceWindow::NotColorBits</c>: inverts the colour bits of every pixel of <paramref name="box"/>, alpha kept (text selection).</summary>
    void NotColorBits(UiBox box);

    /// <summary><c>SurfaceWindow::FillArea</c>: writes <paramref name="argb"/> into every pixel of <paramref name="box"/> (no blending).</summary>
    void Fill(UiBox box, uint argb);
}
