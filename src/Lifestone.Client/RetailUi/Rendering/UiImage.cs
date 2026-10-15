using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Rendering;

/// <summary>
/// How an image is blitted onto a UI surface: retail's <c>BlitMode</c> as <c>UIRegion::SetImageByDID</c> (11.4186
/// 0x0069F960, 11.6096 0x006A07E0) sets it from a media draw mode (<see cref="UiBlitModes.FromDrawMode"/>), and as the software
/// blitter's operations implement it (<see cref="SoftwareUiPainter"/>).
/// </summary>
public enum UiBlitMode : uint
{
    /// <summary>0: the source replaces the destination (<c>BlitOp_SrcCopy</c>); draw mode Normal and every other.</summary>
    SrcCopy = 0,

    /// <summary>1: alpha blend of the colour channels, the destination keeps its alpha (<c>BlitOp_ThreeChannelAlpha</c>); draw mode Overlay.</summary>
    ThreeChannelAlpha = 1,

    /// <summary>2: alpha blend of colour and alpha (<c>BlitOp_FourChannelAlpha</c>); draw mode AlphaBlend.</summary>
    FourChannelAlpha = 2,
}

/// <summary>Conversions between the media draw modes and the blit modes.</summary>
public static class UiBlitModes
{
    /// <summary>
    /// <c>UIRegion::SetImageByDID</c>'s mapping: draw mode 2 (Overlay) blits with <see cref="UiBlitMode.ThreeChannelAlpha"/>,
    /// 3 (AlphaBlend) with <see cref="UiBlitMode.FourChannelAlpha"/>, every other value with <see cref="UiBlitMode.SrcCopy"/>.
    /// </summary>
    public static UiBlitMode FromDrawMode(UiDrawMode drawMode) => drawMode switch
    {
        UiDrawMode.Overlay => UiBlitMode.ThreeChannelAlpha,
        UiDrawMode.AlphaBlend => UiBlitMode.FourChannelAlpha,
        _ => UiBlitMode.SrcCopy,
    };
}

/// <summary>
/// An element's image as it names it: a RenderSurface DID and the blit mode (retail's <c>Graphic</c> plus
/// <c>m_eBlitMode</c>). <see cref="None"/> (DID 0, retail's INVALID_DID) is no image. The painter resolves the DID to
/// pixels (<see cref="IUiImageSource"/>); a DID that does not resolve draws retail's default graphic.
/// </summary>
/// <param name="Did">The RenderSurface's DID (0x06xxxxxx), or 0 for none.</param>
/// <param name="Mode">How it is blitted.</param>
public readonly record struct UiImageRef(uint Did, UiBlitMode Mode)
{
    /// <summary>No image.</summary>
    public static UiImageRef None => default;

    /// <summary>True when there is an image.</summary>
    public bool HasImage => Did != 0;

    /// <summary>For logs: <c>0x06xxxxxx/SrcCopy</c> or <c>none</c>.</summary>
    public override string ToString() => HasImage ? $"0x{Did:X8}/{Mode}" : "none";
}

/// <summary>
/// The pixel format an image had in the dat, as far as the blitter cares: retail's software blitter handles A8R8G8B8,
/// X8R8G8B8 and R8G8B8 itself (<c>SurfaceWindow::BlitAndColor</c>'s blit formats 0, 1 and 2) and uses A8 as a mask.
/// </summary>
public enum UiPixelFormat
{
    /// <summary>A8R8G8B8: the alpha channel is real.</summary>
    Argb,

    /// <summary>X8R8G8B8: opaque; the blitter treats alpha as 255.</summary>
    Xrgb,

    /// <summary>R8G8B8 (24-bit): opaque; the blitter treats alpha as 255.</summary>
    Rgb,

    /// <summary>A8: an alpha mask (font atlases); only its alpha is used.</summary>
    A8,
}

/// <summary>
/// A decoded image, ready to blit: RGBA8 pixels (R, G, B, A bytes per pixel, rows top to bottom, no padding) and the
/// format it had in the dat. Immutable after construction; shared by every thread.
/// </summary>
public sealed class UiImage
{
    /// <summary>An image of <paramref name="width"/>×<paramref name="height"/> RGBA8 pixels.</summary>
    /// <param name="did">The RenderSurface DID it came from (0 for synthetic images).</param>
    /// <param name="width">Width in pixels (0 or more).</param>
    /// <param name="height">Height in pixels (0 or more).</param>
    /// <param name="format">The dat format (decides how the blitter reads alpha).</param>
    /// <param name="rgba">The pixels, <c>width * height * 4</c> bytes; owned by the image from now on.</param>
    public UiImage(uint did, int width, int height, UiPixelFormat format, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"Image 0x{did:X8}: {rgba.Length} bytes for {width}x{height} RGBA8 pixels.", nameof(rgba));
        }

        Did = did;
        Width = width;
        Height = height;
        Format = format;
        Pixels = rgba;
    }

    /// <summary>The RenderSurface DID (0 for synthetic images).</summary>
    public uint Did { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The dat format.</summary>
    public UiPixelFormat Format { get; }

    /// <summary>RGBA8 pixels, <c>Width * 4</c> bytes per row.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// True when the pixel at (<paramref name="x"/>, <paramref name="y"/>) has a non-zero colour with its alpha masked off
    /// (<c>SurfaceWindow::PointTestOverlay</c>, 11.4186 0x004416A0; the symbol table has no row for it, and its
    /// neighbours predict 11.6096 0x00441840: TODO(retailui-runtime): confirm from the bytes); false outside the image.
    /// An A8 image has nothing left once its alpha is masked, so it never tests true: Lifestone's fix of a retail glitch
    /// (<paramref name="retail"/> restores it).
    /// </summary>
    /// <remarks>
    /// Retail's whole-dword read also reaches past a 24-bit pixel into the next pixel's first (blue) byte, and reads the X
    /// byte of an X8R8G8B8 pixel, whose format has no alpha mask (<c>PixelFormatDesc::SetFormat</c>); Lifestone tests the
    /// pixel's colour alone in both, with or without <paramref name="retail"/>. No retail data reaches any of these cases:
    /// the only hit-test masks of the retail layouts (alpha media of layout 0x21000040: 0x06004C9D, 0x06004CA2,
    /// 0x06004CB1) are A8R8G8B8, which both tests treat alike.
    /// </remarks>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="retail">
    /// True for retail's test of an A8 image (<see cref="UiRetailGlitches.A8PointTest"/>): retail reads a whole dword at
    /// the pixel's byte and masks off the format's alpha mask, 0xFF for A8 (<c>PixelFormatDesc::SetFormat</c>, 11.4186
    /// 0x0043D0F0, 11.6096 0x0043D290), so the alpha bytes of the next three pixels in memory decide, running on into the next row. Bytes
    /// past the image's end count as 0 (TODO(retailui-runtime): retail reads whatever follows the surface, and its
    /// pitch may pad rows; neither is known). Other formats test the same either way.
    /// </param>
    public bool PointTestOverlay(int x, int y, bool retail = false)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return false;
        }

        var pixel = (y * Width) + x;
        if (Format == UiPixelFormat.A8)
        {
            if (!retail)
            {
                return false;
            }

            var end = Math.Min(pixel + 4, Width * Height);
            for (var p = pixel + 1; p < end; p++)
            {
                if (Pixels[(p * 4) + 3] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        var i = pixel * 4;
        return (Pixels[i] | Pixels[i + 1] | Pixels[i + 2]) != 0;
    }
}
