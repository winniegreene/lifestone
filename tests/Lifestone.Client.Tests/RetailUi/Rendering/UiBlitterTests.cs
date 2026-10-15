using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>
/// Retail's software blitter byte for byte (SoftwareBlitAndColor's SrcCopy, ThreeChannelAlpha, FourChannelAlpha and the
/// A8 colouring loop), each expectation worked out by hand from the loops' arithmetic.
/// </summary>
public sealed class UiBlitterTests
{
    [Fact]
    public void Src_copy_replaces_the_destination_and_an_opaque_source_writes_alpha_255()
    {
        var argb = Blit(UiBlitMode.SrcCopy, 1f, 0x80C86432, 0x4D0A141E, UiPixelFormat.Argb);
        var rgb = Blit(UiBlitMode.SrcCopy, 1f, 0x80C86432, 0x4D0A141E, UiPixelFormat.Rgb);
        var xrgb = Blit(UiBlitMode.SrcCopy, 0.5f, 0x00C86432, 0x4D0A141E, UiPixelFormat.Xrgb);

        Assert.Equal(0x80C86432u, argb);
        Assert.Equal(0xFFC86432u, rgb);
        Assert.Equal(0xFFC86432u, xrgb);
    }

    [Theory]
    [InlineData(UiBlitMode.SrcCopy)]
    [InlineData(UiBlitMode.ThreeChannelAlpha)]
    [InlineData(UiBlitMode.FourChannelAlpha)]
    public void An_opacity_that_truncates_to_0_draws_nothing(UiBlitMode mode)
    {
        Assert.Equal(0x4D0A141Eu, Blit(mode, 0.0039f, 0xFFC86432, 0x4D0A141E, UiPixelFormat.Argb));
    }

    [Fact]
    public void Three_channel_alpha_blends_the_colour_by_an_mmx_lane_and_keeps_the_destination_alpha()
    {
        // k = 128 + 1: r = 10 - (1290 >> 8) + (25800 >> 8) = 105; g = 20 - 10 + 50 = 60; b = 30 - 15 + 25 = 40.
        Assert.Equal(0x4D693C28u, Blit(UiBlitMode.ThreeChannelAlpha, 1f, 0x80C86432, 0x4D0A141E, UiPixelFormat.Argb));

        // An opaque source copies the colour, still keeping the destination alpha.
        Assert.Equal(0x4DC86432u, Blit(UiBlitMode.ThreeChannelAlpha, 1f, 0xFFC86432, 0x4D0A141E, UiPixelFormat.Argb));

        // Opacity 0.5 is modifier 127: the alpha counts as 128 x 128 / 256 = 64, k = 65: r = 10 - 2 + 50 = 58.
        Assert.Equal(58u, (Blit(UiBlitMode.ThreeChannelAlpha, 0.5f, 0x80C86432, 0x4D0A141E, UiPixelFormat.Argb) >> 16) & 0xFF);
    }

    [Fact]
    public void Four_channel_alpha_copies_over_transparent_and_blends_over_opaque()
    {
        // Over a transparent destination the source is written as is, its own alpha included.
        Assert.Equal(0x80C86432u, Blit(UiBlitMode.FourChannelAlpha, 1f, 0x80C86432, 0x000A141E, UiPixelFormat.Argb));

        // Over an opaque one, the three-channel lane; the alpha stays 255.
        Assert.Equal(0xFF693C28u, Blit(UiBlitMode.FourChannelAlpha, 1f, 0x80C86432, 0xFF0A141E, UiPixelFormat.Argb));

        // An opaque source at full opacity replaces it.
        Assert.Equal(0xFFC86432u, Blit(UiBlitMode.FourChannelAlpha, 1f, 0xFFC86432, 0x4D0A141E, UiPixelFormat.Argb));
    }

    [Fact]
    public void Four_channel_alpha_over_partial_alpha_blends_in_single_precision()
    {
        // k = 129, da = 77: A' = 129 - 129 x 78 / 256 + 78 = 168; r = 10 + 190 x 129 / 168 = 155.89 -> 156,
        // g = 20 + 80 x 129 / 168 = 81.43 -> 81, b = 30 + 20 x 129 / 168 = 45.36 -> 45; alpha A' - 1 = 167.
        Assert.Equal(0xA79C512Du, Blit(UiBlitMode.FourChannelAlpha, 1f, 0x80C86432, 0x4D0A141E, UiPixelFormat.Argb));
    }

    [Fact]
    public void Four_channel_alpha_rounds_halves_to_even()
    {
        // Source alpha 1 (k = 2) over destination alpha 1: A' = 2 - 4 / 256 + 2 = 4; r = 10 - (10 - 3) x 2 / 4 = 6.5 -> 6.
        var result = Blit(UiBlitMode.FourChannelAlpha, 1f, 0x01030000, 0x010A0000, UiPixelFormat.Argb);

        Assert.Equal(6u, (result >> 16) & 0xFF);
        Assert.Equal(3u, result >> 24);
    }

    [Fact]
    public void An_opaque_source_blends_as_alpha_255_with_the_modifier()
    {
        // Opacity 0.5: 255 x 128 / 256 = 127, k = 128, over an opaque destination: r = 10 - 5 + 100 = 105.
        Assert.Equal(0xFF693C28u, Blit(UiBlitMode.FourChannelAlpha, 0.5f, 0x00C86432, 0xFF0A141E, UiPixelFormat.Rgb));
    }

    [Fact]
    public void Colouring_through_an_alpha_mask_uses_the_mask_as_alpha_and_the_colours_alpha_as_modifier()
    {
        // Mask 128, colour alpha 255 (modifier 256): k = 129 over opaque -> the lane formula.
        Assert.Equal(0xFF693C28u, Colorize(0xFFC86432, 128, 0xFF0A141E));

        // Over a transparent destination: the colour with the mask as alpha.
        Assert.Equal(0x80C86432u, Colorize(0xFFC86432, 128, 0x000A141E));

        // Colour alpha 128: modifier 129, alpha 129 x 128 / 256 = 64, k = 65: r = 10 - 2 + 50 = 58.
        Assert.Equal(58u, (Colorize(0x80C86432, 128, 0xFF0A141E) >> 16) & 0xFF);

        // Colour alpha 0 draws nothing; mask 0 draws nothing.
        Assert.Equal(0xFF0A141Eu, Colorize(0x00C86432, 255, 0xFF0A141E));
        Assert.Equal(0xFF0A141Eu, Colorize(0xFFC86432, 0, 0xFF0A141E));
    }

    [Fact]
    public void The_opacity_modifier_truncates()
    {
        Assert.Equal(255, UiBlitter.Modifier(1f));
        Assert.Equal(127, UiBlitter.Modifier(0.5f));
        Assert.Equal(0, UiBlitter.Modifier(0.0039f));
        Assert.Equal(1, UiBlitter.Modifier(0.004f));
    }

    private static uint Blit(UiBlitMode mode, float opacity, uint source, uint destination, UiPixelFormat format)
    {
        var image = Image(source, format);
        var surface = new UiSurface(1, 1);
        surface.SetArgb(0, 0, destination);
        UiBlitter.Blit(image, 0, 0, surface, 0, 0, 1, 1, mode, opacity);
        return surface.GetArgb(0, 0);
    }

    private static uint Colorize(uint argb, byte mask, uint destination)
    {
        var image = new UiImage(0, 1, 1, UiPixelFormat.A8, [255, 255, 255, mask]);
        var surface = new UiSurface(1, 1);
        surface.SetArgb(0, 0, destination);
        UiBlitter.Colorize(image, 0, 0, surface, 0, 0, 1, 1, argb);
        return surface.GetArgb(0, 0);
    }

    private static UiImage Image(uint argb, UiPixelFormat format) =>
        new(0, 1, 1, format, [(byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, format == UiPixelFormat.Argb ? (byte)(argb >> 24) : (byte)255]);
}

/// <summary>Retail's colour round trip (RGBAColor::SetColor32 then GetColor32).</summary>
public sealed class UiRgbaColorTests
{
    [Fact]
    public void Every_byte_survives_the_round_trip()
    {
        for (uint b = 0; b < 256; b++)
        {
            var argb = (b << 24) | (b << 16) | (b << 8) | b;
            Assert.Equal(argb, UiRgbaColor.FromColor32(argb).ToColor32());
        }
    }

    [Fact]
    public void Float_colours_truncate()
    {
        Assert.Equal(0xFF4C4C66u, new UiRgbaColor(0.3f, 0.3f, 0.4f, 1f).ToColor32());
        Assert.Equal(0xFFFFFFFFu, UiRgbaColor.White.ToColor32());
        Assert.Equal(0xFF000000u, UiRgbaColor.Black.ToColor32());
        Assert.Equal(UiDefaultGraphic.FillArgb, new UiRgbaColor(0.3f, 0.3f, 0.4f, 1f).ToColor32());
    }
}
