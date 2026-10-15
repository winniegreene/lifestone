using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// The pictures the appearance page makes for its colour wheel: each colour spot is the "blank bullet" with its black replaced by the colour
/// (<c>gmCGAppearancePage::DoColorSpots</c>, 11.4186 0x0047D850, 11.6096 0x0047DC00), and the disk under the shade slider is the gradient
/// ring multiplied by the current colour (<c>DoGradDisk</c>, 11.4186 0x0047DA90, 11.6096 0x0047DE40). Pixel for pixel as retail's software surface calls do it.
/// </summary>
/// <remarks>
/// <para>
/// <c>SurfaceWindow::Blit</c> with the source copy mode puts the source on an A8R8G8B8 surface (<see cref="Copy"/>: an X8R8G8B8 or R8G8B8 source is
/// opaque); <c>SurfaceWindow::ReplaceColor</c> (11.4186 0x00441530, 11.6096 0x004416D0) then writes the new colour over every pixel that equals the old
/// one as a whole 32-bit value (alpha included): <c>RGBAColor::GetColor32</c> (0x00423D00, 0x00424000) gives the old colour, opaque black, as 0xFF000000
/// (<see cref="ReplaceBlack"/>). <c>SurfaceWindow::BlitAndColor</c> (0x00442750, 0x004428B0) with the multiply colour mode scales each of red, green and blue
/// by <c>(c + (c != 0)) / 256</c> and keeps the alpha (<see cref="Multiply"/>; the software loop is <c>ColoringLoopNoScale__BlitOp_Multiply_BlitFormat_Fill_BlitFormat_ARGB_</c>).
/// </para>
/// </remarks>
public static class CharGenSwatches
{
    /// <summary>A copy of <paramref name="source"/> on an A8R8G8B8 surface (<c>SurfaceWindow::Blit</c>, source copy): an opaque source format gets alpha 255.</summary>
    public static UiImage Copy(UiImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var pixels = (byte[])source.Pixels.Clone();
        if (source.Format != UiPixelFormat.Argb)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return new UiImage(0, source.Width, source.Height, UiPixelFormat.Argb, pixels);
    }

    /// <summary><see cref="Copy"/> of <paramref name="blank"/> with every opaque black pixel replaced by (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>), opaque.</summary>
    public static UiImage ReplaceBlack(UiImage blank, byte r, byte g, byte b)
    {
        var image = Copy(blank);
        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 255)
            {
                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
            }
        }

        return image;
    }

    /// <summary><see cref="Copy"/> of <paramref name="source"/> with red, green and blue multiplied by (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>) as retail's multiply colour mode does; alpha kept.</summary>
    public static UiImage Multiply(UiImage source, byte r, byte g, byte b)
    {
        var image = Copy(source);
        var pixels = image.Pixels;
        var fr = r + (r != 0 ? 1 : 0);
        var fg = g + (g != 0 ? 1 : 0);
        var fb = b + (b != 0 ? 1 : 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)((pixels[i] * fr) >> 8);
            pixels[i + 1] = (byte)((pixels[i + 1] * fg) >> 8);
            pixels[i + 2] = (byte)((pixels[i + 2] * fb) >> 8);
        }

        return image;
    }
}
