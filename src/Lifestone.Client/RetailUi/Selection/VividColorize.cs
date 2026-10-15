using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.RetailUi.Selection;

/// <summary>
/// How the vivid target indicator tints its pictures (<c>VividTargetIndicator::CopyImage</c>, 11.4186 0x004F5DD0, 11.6096 0x004F6A70; docs/retail-ui.md section 21.7). The global <c>s_BlitMethod</c> reads 4 in the
/// 11.6096 exe (docs/research/retail-addresses-m2.md section 1.8), so retail copies the picture with <c>SurfaceWindow::BlitAndColor</c> in the colorize mode and the colour's 32-bit value
/// (<c>RGBAColor::GetColor32</c>); the other three methods (multiply, colorize, screen over a plain copy) are dead code in the end-of-retail client and are not built.
/// </summary>
/// <remarks>
/// The colorize loop (<c>ColoringLoopNoScale__BlitOp_Colorize_BlitFormat_Fill_BlitFormat_ARGB_</c>, read from the 11.4186 decompile, whose body the 11.6096 build shares) takes each pixel's
/// brightness (its largest channel, plus one when non-zero, so 0 to 256) and paints it in the colour's hue and saturation: a grey picture comes out as the colour at that brightness, and
/// black stays black. The pixel's alpha is not touched. With a fully opaque colour and the full alpha modifier the new colour replaces the pixel; a colour with less alpha blends in by that
/// fraction (never the case for the radar colours the indicator uses).
/// </remarks>
public static class VividColorize
{
    /// <summary>A copy of <paramref name="source"/> (as <see cref="CharGenSwatches.Copy"/>) with every pixel recoloured to <paramref name="color"/>'s hue and saturation at its own brightness.</summary>
    public static UiImage Apply(UiImage source, UiRgbaColor color)
    {
        ArgumentNullException.ThrowIfNull(source);
        var image = CharGenSwatches.Copy(source);
        var pixels = image.Pixels;
        var argb = color.ToColor32();
        int a = (int)(argb >> 24) & 0xFF;
        int r = (int)(argb >> 16) & 0xFF;
        int g = (int)(argb >> 8) & 0xFF;
        int b = (int)argb & 0xFF;

        // The colour: channels 0 to 256, then saturation (0 to 256) and hue (4096 per sextant, 0 to 24576).
        var r8 = r + (r != 0 ? 1 : 0);
        var g8 = g + (g != 0 ? 1 : 0);
        var b8 = b + (b != 0 ? 1 : 0);
        var max = Math.Max(r8, Math.Max(g8, b8));
        var min = Math.Min(r8, Math.Min(g8, b8));
        var range = max - min;
        var saturation = 0;
        var hue = 0;
        if (range != 0)
        {
            saturation = (range << 8) / max;
            int numerator;
            int sector;
            if (r8 == max)
            {
                numerator = g8 - b8;
                sector = numerator < 0 ? 24576 : 0;
            }
            else if (g8 == max)
            {
                numerator = b8 - r8;
                sector = 0x2000;
            }
            else
            {
                numerator = r8 - g8;
                sector = 0x4000;
            }

            hue = sector + ((numerator << 12) / range);
        }

        var weight = a + (a != 0 ? 1 : 0); // the colour alpha, 0 to 256 (the alpha modifier is 1.0)
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // Pixels are stored R, G, B, A (as CharGenSwatches).
            var pr = pixels[i];
            var pg = pixels[i + 1];
            var pb = pixels[i + 2];
            var value = Math.Max(pr + (pr != 0 ? 1 : 0), Math.Max(pg + (pg != 0 ? 1 : 0), pb + (pb != 0 ? 1 : 0)));
            Colorized(value, saturation, hue, out var nr, out var ng, out var nb);
            if (weight == 256)
            {
                pixels[i] = (byte)nr;
                pixels[i + 1] = (byte)ng;
                pixels[i + 2] = (byte)nb;
            }
            else
            {
                pixels[i] = (byte)(pr - (weight * (pr - nr) / 256));
                pixels[i + 1] = (byte)(pg - (weight * (pg - ng) / 256));
                pixels[i + 2] = (byte)(pb - (weight * (pb - nb) / 256));
            }
        }

        return image;
    }

    // The loop's HSV to RGB step in fixed point: value 0 to 256, chroma = saturation * value / 256, and the in-sextant ramp rounded to nearest. The channels come back as bytes
    // (each value less one when non-zero, undoing the "plus one when non-zero" scale).
    private static void Colorized(int value, int saturation, int hue, out int r, out int g, out int b)
    {
        int cr = value;
        int cg = value;
        int cb = value;
        if (saturation != 0)
        {
            var sextant = hue / 4096;
            var chroma = saturation * value / 256;
            var low = value - chroma;
            var scaled = chroma * (hue - ((((hue / 4096) + 1) / 2) << 13));
            var ramp = scaled / 4096;
            if (scaled % 4096 < -2048)
            {
                ramp--;
            }

            if (scaled % 4096 > 2048)
            {
                ramp++;
            }

            switch (sextant)
            {
                case 0:
                    cr = value;
                    cg = low + ramp;
                    cb = low;
                    break;
                case 1:
                    cr = low - ramp;
                    cg = value;
                    cb = low;
                    break;
                case 2:
                    cr = low;
                    cg = value;
                    cb = low + ramp;
                    break;
                case 3:
                    cr = low;
                    cg = low - ramp;
                    cb = value;
                    break;
                case 4:
                    cr = low + ramp;
                    cg = low;
                    cb = value;
                    break;
                case 5:
                    cr = value;
                    cg = low;
                    cb = low - ramp;
                    break;
                default:
                    break;
            }
        }

        r = Narrow(cr);
        g = Narrow(cg);
        b = Narrow(cb);
    }

    private static int Narrow(int channel) => Math.Clamp(channel - (channel != 0 ? 1 : 0), 0, 255);
}
