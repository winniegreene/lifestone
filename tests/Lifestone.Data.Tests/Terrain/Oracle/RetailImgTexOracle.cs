// Behaviour re-expressed from the retail decompile (Mag-nus/acclient-AI-RE, acclient 11.4186): ImgTex::TileCSI
// (acclient.c:365513) and the scale-up branch of ImgTex::MergeTexture (acclient.c:365632), as driven by
// TexMerge::FillTempTexBuffer (acclient.c:305909). No decompiled code is copied; names and structure are Lifestone's.

namespace Lifestone.Data.Tests.Terrain.Oracle;

/// <summary>
/// Test oracle for <c>TerrainCompositor.ComposeRetailTexture</c>: retail's cell-texture build stated the way the client
/// walks memory, on raw A8R8G8B8 bytes (B, G, R, A per texel), with an alpha <b>cursor</b> that starts at a per-rotation
/// offset and moves by per-rotation x/y strides, and countdown counters that replicate each alpha texel
/// <c>destWidth / alphaWidth</c> times in both directions.
/// </summary>
/// <remarks>
/// The product compositor states the same walk as closed-form texel coordinates (<c>alpha[w - 1 - ay, ax]</c> and so on).
/// This oracle shares none of that arithmetic and none of the product's image types, so a misreading of the rotation
/// table, the replication or the per-row source wrap shows up as a mismatch.
/// </remarks>
internal static class RetailImgTexOracle
{
    /// <summary>An A8R8G8B8 texture as its raw bytes (B, G, R, A per texel, rows top to bottom).</summary>
    public sealed record Bgra(int Width, int Height, byte[] Bytes);

    /// <summary>An 8-bit alpha map as its raw bytes (one byte per texel, rows top to bottom).</summary>
    public sealed record Alpha8(int Width, int Height, byte[] Bytes);

    /// <summary>
    /// <c>FillTempTexBuffer</c> for one cell at full detail: tile the base, then merge each layer in order.
    /// </summary>
    /// <param name="size">Edge of the square buffer (BaseTexSize).</param>
    /// <param name="baseTexture">The base texture.</param>
    /// <param name="tiling">The base's tex_tiling.</param>
    /// <param name="layers">Overlays then road layers, in retail order: (texture, alpha map, rotation 0-3).</param>
    public static byte[] BuildCellTexture(int size, Bgra baseTexture, int tiling, IEnumerable<(Bgra Texture, Alpha8 Alpha, int Rotation)> layers)
    {
        var buffer = new byte[size * size * 4];
        TileCsi(buffer, size, size, baseTexture, tiling);
        foreach (var (texture, alpha, rotation) in layers)
        {
            MergeTextureScaleUp(buffer, size, size, texture, alpha, rotation);
        }

        return buffer;
    }

    /// <summary>
    /// <c>ImgTex::TileCSI</c>: for each of <paramref name="tiling"/> tile columns (outer) and tile rows (inner), copy the
    /// source rows one after another into the buffer, starting at (column * srcWidth, row * srcHeight).
    /// </summary>
    public static void TileCsi(byte[] buffer, int destHeight, int destWidth, Bgra source, int tiling)
    {
        if (source.Width * tiling > destWidth || source.Height * tiling > destHeight)
        {
            throw new NotSupportedException("Retail does not guard against tiles that overrun the buffer; the oracle refuses them.");
        }

        var columnStart = 0;
        for (var column = 0; column < tiling; column++)
        {
            var tileStart = columnStart;
            for (var row = 0; row < tiling; row++)
            {
                var sourceCursor = 0;
                var destLine = tileStart;
                for (var line = 0; line < source.Height; line++)
                {
                    Buffer.BlockCopy(source.Bytes, sourceCursor, buffer, destLine * 4, source.Width * 4);
                    sourceCursor += source.Width * 4;
                    destLine += destWidth;
                }

                tileStart += destWidth * source.Height;
            }

            columnStart += source.Width;
        }
    }

    /// <summary>
    /// <c>ImgTex::MergeTexture</c> when the buffer is wider than the alpha map (the only branch a 1024 buffer with 512
    /// maps takes). The alpha cursor strides by the <b>colour texture's</b> width, as retail does.
    /// </summary>
    public static void MergeTextureScaleUp(byte[] buffer, int destHeight, int destWidth, Bgra texture, Alpha8 alpha, int rotation)
    {
        var w = texture.Width;
        var h = texture.Height;
        if (destWidth <= alpha.Width || alpha.Width != w || alpha.Height != h || (w & (w - 1)) != 0)
        {
            throw new NotSupportedException("Only the scale-up branch with alpha maps the size of the power-of-two layer texture is modelled.");
        }

        var replicate = destWidth / alpha.Width;

        // Where the alpha cursor starts, and how far it moves per alpha step along a buffer row (x) and down a column (y).
        var (start, xStride, yStride) = rotation switch
        {
            0 => (0, 1, w),
            1 => (w - 1, w, -1),
            2 => ((h * w) - 1, -1, -w),
            3 => (w * (h - 1), -w, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
        };

        var dest = 0;
        var alphaRowStart = start;
        var alphaCursor = start;
        var xCountdown = replicate; // retail sets this once, before the first row, and never resets it per row
        var yCountdown = replicate;
        var sourceRow = 0;
        for (var y = 0; y < destHeight; y++)
        {
            var sourceLine = sourceRow * w * 4;

            // Retail bounds the inner loop by the buffer height as well (the buffer is square).
            for (var x = 0; x < destHeight; x++)
            {
                int a = alpha.Bytes[alphaCursor];
                var source = sourceLine + (4 * (x & (w - 1)));
                if (a < 0xFF)
                {
                    if (a > 0x80)
                    {
                        a++;
                    }

                    // Blue, green, red; the buffer's alpha byte is left alone.
                    for (var c = 0; c < 3; c++)
                    {
                        buffer[dest + c] = (byte)(((a * buffer[dest + c]) + ((256 - a) * texture.Bytes[source + c])) >> 8);
                    }
                }

                dest += 4;
                if (--xCountdown == 0)
                {
                    alphaCursor += xStride;
                    xCountdown = replicate;
                }
            }

            if (++sourceRow >= h)
            {
                sourceRow = 0;
            }

            if (--yCountdown == 0)
            {
                alphaRowStart += yStride;
                yCountdown = replicate;
            }

            alphaCursor = alphaRowStart;
        }
    }
}
