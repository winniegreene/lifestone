using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Text;

/// <summary>
/// Draws glyphs of a retail font onto a UI surface: <c>SurfaceWindow::DrawCharacter</c> (11.4186 0x00442BD0, 11.6096
/// 0x00442D30) with <c>CreateCharRectPair</c> (11.4186 0x00441480, 11.6096 0x00441620).
/// </summary>
/// <remarks>
/// <para>
/// The glyph's top-left is the pen plus its leading offset (when the advance includes the offsets) and its vertical
/// offset (minus the font's baseline offset when the pen names the baseline). Source and destination rectangles are
/// cut to the window, and nothing is drawn when the glyph lies outside it.
/// </para>
/// <para>
/// <b>Background (outline) atlas</b>, when asked for and the font has one: the glyph's rectangle grown by the font's
/// border pixels (the destination's bottom grows by the horizontal border, a retail slip kept: every retail font with
/// a background has equal borders); an alpha atlas, or the colorize flag, colours it with the background colour,
/// otherwise its own pixels are blended (four-channel alpha). <b>Foreground atlas</b> next, unless skipped: an alpha
/// atlas, or the colorize flag, colours it with the text colour; otherwise its own pixels are blended.
/// </para>
/// </remarks>
public static class TextRasterizer
{
    /// <summary>Draws <paramref name="c"/> and returns the pen's advance (0 when the font has no glyph for it, '?' included).</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="window">The window, in surface coordinates; the glyph is clipped to it.</param>
    /// <param name="x">The pen's column relative to the window.</param>
    /// <param name="y">The pen's row relative to the window.</param>
    /// <param name="font">The font.</param>
    /// <param name="c">The character.</param>
    /// <param name="argb">The colour for an alpha foreground (0xAARRGGBB).</param>
    /// <param name="flags">How to draw.</param>
    /// <param name="backgroundArgb">The colour for an alpha background.</param>
    public static int DrawCharacter(UiSurface surface, UiBox window, int x, int y, IUiFont font, char c, uint argb, UiTextFlags flags, uint backgroundArgb)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(font);
        if (!font.TryGetCharDesc(c, out var desc))
        {
            return 0;
        }

        int advance;
        var leading = 0;
        if ((flags & UiTextFlags.Monospace) != 0)
        {
            advance = font.MaxCharWidth + 1;
        }
        else if ((flags & UiTextFlags.AdvanceWithOffsets) != 0)
        {
            advance = desc.Width + desc.HorizontalOffsetBefore + desc.HorizontalOffsetAfter;
            leading = desc.HorizontalOffsetBefore;
        }
        else
        {
            advance = desc.Width;
        }

        var top = y + desc.VerticalOffsetBefore;
        if ((flags & UiTextFlags.Baseline) != 0)
        {
            top -= font.BaselineOffset;
        }

        var left = x + leading;
        var windowWidth = window.X1 - window.X0 + 1;
        var windowHeight = window.Y1 - window.Y0 + 1;
        var (src, dst) = CharRectPair(0, 0, desc.X, desc.Y, desc.Width, desc.Height, left, top, windowWidth, windowHeight);
        if (dst.Left >= dst.Right || dst.Top >= dst.Bottom)
        {
            return advance;
        }

        var alphaOnly = (flags & UiTextFlags.AlphaChannelOnly) != 0;
        var colorize = (flags & UiTextFlags.Colorize) != 0;
        if (font.Background is { } background && (flags & UiTextFlags.DrawBackground) != 0)
        {
            var (bkSrc, bkDst) = CharRectPair(
                font.NumHorizontalBorderPixels, font.NumVerticalBorderPixels, desc.X, desc.Y, desc.Width, desc.Height, left, top, windowWidth, windowHeight);
            Draw(surface, window, background, bkSrc, bkDst, alphaOnly, background.Format == UiPixelFormat.A8 || colorize, backgroundArgb);
        }

        if ((flags & UiTextFlags.SkipForeground) == 0 && font.Foreground is { } foreground)
        {
            Draw(surface, window, foreground, src, dst, alphaOnly, foreground.Format == UiPixelFormat.A8 || colorize, argb);
        }

        return advance;
    }

    private static void Draw(UiSurface surface, UiBox window, UiImage atlas, Rect src, Rect dst, bool alphaOnly, bool colour, uint argb)
    {
        // SurfaceWindow keeps each rectangle inside its surface; the blit then covers the smaller of the two.
        var srcLeft = Math.Max(src.Left, 0);
        var srcTop = Math.Max(src.Top, 0);
        var srcRight = Math.Min(src.Right, atlas.Width);
        var srcBottom = Math.Min(src.Bottom, atlas.Height);
        var dstLeft = Math.Max(dst.Left, 0);
        var dstTop = Math.Max(dst.Top, 0);
        var width = Math.Min(srcRight - srcLeft, dst.Right - dstLeft);
        var height = Math.Min(srcBottom - srcTop, dst.Bottom - dstTop);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var dx = window.X0 + dstLeft;
        var dy = window.Y0 + dstTop;
        if (alphaOnly)
        {
            UiBlitter.CopyAlpha(atlas, srcLeft, srcTop, surface, dx, dy, width, height);
        }
        else if (colour)
        {
            UiBlitter.Colorize(atlas, srcLeft, srcTop, surface, dx, dy, width, height, argb);
        }
        else
        {
            UiBlitter.Blit(atlas, srcLeft, srcTop, surface, dx, dy, width, height, UiBlitMode.FourChannelAlpha, 1f);
        }
    }

    /// <summary>
    /// <c>CreateCharRectPair</c>: the source rectangle in the atlas and the destination rectangle in the window (right and
    /// bottom exclusive), grown by the borders and cut to the window.
    /// </summary>
    private static (Rect Src, Rect Dst) CharRectPair(int borderX, int borderY, int xs, int ys, int ws, int hs, int xd, int yd, int windowWidth, int windowHeight)
    {
        var src = new Rect(xs - borderX, ys - borderY, borderX + ws + xs, borderY + hs + ys);
        var dst = new Rect(xd - borderX, yd - borderY, borderX + xd + ws, borderX + yd + hs);
        if (dst.Left < 0)
        {
            src = src with { Left = src.Left - dst.Left };
            dst = dst with { Left = 0 };
        }

        if (dst.Top < 0)
        {
            src = src with { Top = src.Top - dst.Top };
            dst = dst with { Top = 0 };
        }

        if (dst.Right > windowWidth)
        {
            src = src with { Right = src.Right + windowWidth - dst.Right };
            dst = dst with { Right = windowWidth };
        }

        if (dst.Bottom > windowHeight)
        {
            src = src with { Bottom = src.Bottom + windowHeight - dst.Bottom };
            dst = dst with { Bottom = windowHeight };
        }

        return (src, dst);
    }

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);
}
