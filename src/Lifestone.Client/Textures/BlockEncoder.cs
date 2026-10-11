namespace Lifestone.Client.Textures;

/// <summary>
/// A small BC1/BC2/BC3 (DXT1/DXT3/DXT5) encoder for generated mip levels (<see cref="MipChain"/>). Level 0 of a dat
/// texture is never re-encoded: it keeps the dat's blocks.
/// </summary>
/// <remarks>
/// <para>Block layouts follow the BCn specification (the same layout <c>Lifestone.Data.Textures.BlockCompression</c>
/// decodes; docs/textures.md §2):</para>
/// <list type="bullet">
/// <item><b>Colour block</b> (8 bytes): two RGB565 endpoints (little-endian), then 16 two-bit indices, texel 0 in the
/// lowest bits, rows top to bottom. The encoder always writes <c>c0 &gt; c1</c> (four-colour mode: c0, c1,
/// (2c0 + c1) / 3, (c0 + 2c1) / 3), or <c>c0 == c1</c> with every index 0 for a flat block, so a BC1 block never uses
/// three-colour mode's transparent index 3.</item>
/// <item><b>BC2 alpha</b> (8 bytes before the colour block): 16 four-bit alphas, <c>round(a × 15 / 255)</c>.</item>
/// <item><b>BC3 alpha</b> (8 bytes before the colour block): <c>a0 = max</c>, <c>a1 = min</c> (eight-value mode when
/// they differ), then 16 three-bit indices to the nearest of a0, a1 and the six interpolants.</item>
/// </list>
/// <para>Colour endpoints are the block's two most distant texels, or that pair inset by 1/16 (whichever encodes with
/// less squared error); indices pick the nearest palette entry. Good enough for minified levels; not a high-quality
/// encoder.
/// Texels past the image edge (levels smaller than 4x4) repeat the last row or column.</para>
/// </remarks>
public static class BlockEncoder
{
    /// <summary>Encodes an RGBA8 image (rows top to bottom) into <paramref name="destination"/>.</summary>
    public static void Encode(ReadOnlySpan<byte> rgba, int width, int height, BlockFormat format, Span<byte> destination)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA8.", nameof(rgba));
        }

        var blockBytes = format == BlockFormat.Bc1 ? 8 : 16;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (destination.Length != blocksX * blocksY * blockBytes)
        {
            throw new ArgumentException($"Expected {blocksX * blocksY * blockBytes} destination bytes.", nameof(destination));
        }

        Span<byte> block = stackalloc byte[64];
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                for (var ty = 0; ty < 4; ty++)
                {
                    var y = Math.Min((by * 4) + ty, height - 1);
                    for (var tx = 0; tx < 4; tx++)
                    {
                        var x = Math.Min((bx * 4) + tx, width - 1);
                        rgba.Slice(((y * width) + x) * 4, 4).CopyTo(block.Slice(((ty * 4) + tx) * 4, 4));
                    }
                }

                var output = destination.Slice(((by * blocksX) + bx) * blockBytes, blockBytes);
                switch (format)
                {
                    case BlockFormat.Bc1:
                        EncodeColor(block, output);
                        break;
                    case BlockFormat.Bc2:
                        EncodeExplicitAlpha(block, output[..8]);
                        EncodeColor(block, output[8..]);
                        break;
                    default:
                        EncodeInterpolatedAlpha(block, output[..8]);
                        EncodeColor(block, output[8..]);
                        break;
                }
            }
        }
    }

    /// <summary>Encodes 16 RGBA texels' colour into an 8-byte four-colour block.</summary>
    internal static void EncodeColor(ReadOnlySpan<byte> block, Span<byte> output)
    {
        // Endpoints: the two texels farthest apart (the ends of the block's colour line), and the same pair inset by
        // 1/16 of their difference; keep whichever encodes with less squared error.
        int first = 0, second = 0, farthest = -1;
        for (var i = 0; i < 16; i++)
        {
            for (var j = i + 1; j < 16; j++)
            {
                var d = Distance(block, i, block[j * 4], block[(j * 4) + 1], block[(j * 4) + 2]);
                if (d > farthest)
                {
                    farthest = d;
                    first = i;
                    second = j;
                }
            }
        }

        Span<int> e0 = [block[first * 4], block[(first * 4) + 1], block[(first * 4) + 2]];
        Span<int> e1 = [block[second * 4], block[(second * 4) + 1], block[(second * 4) + 2]];
        Span<byte> best = stackalloc byte[8];
        Span<byte> candidate = stackalloc byte[8];
        var bestError = TryEndpoints(block, To565(e0[0], e0[1], e0[2]), To565(e1[0], e1[1], e1[2]), best);
        Span<int> i0 = stackalloc int[3];
        Span<int> i1 = stackalloc int[3];
        for (var c = 0; c < 3; c++)
        {
            var inset = (e1[c] - e0[c]) / 16;
            i0[c] = e0[c] + inset;
            i1[c] = e1[c] - inset;
        }

        if (TryEndpoints(block, To565(i0[0], i0[1], i0[2]), To565(i1[0], i1[1], i1[2]), candidate) < bestError)
        {
            candidate.CopyTo(best);
        }

        best.CopyTo(output);
    }

    /// <summary>Writes a four-colour block for two endpoints (ordered c0 &gt; c1) and returns its squared error.</summary>
    private static long TryEndpoints(ReadOnlySpan<byte> block, ushort c0, ushort c1, Span<byte> output)
    {
        if (c0 < c1)
        {
            (c0, c1) = (c1, c0);
        }

        output[0] = (byte)c0;
        output[1] = (byte)(c0 >> 8);
        output[2] = (byte)c1;
        output[3] = (byte)(c1 >> 8);
        Span<int> palette = stackalloc int[12];
        From565(c0, palette[..3]);
        From565(c1, palette.Slice(3, 3));
        var count = 4;
        if (c0 == c1)
        {
            // Equal endpoints would be three-colour mode in BC1; use index 0 only (c0).
            count = 1;
        }
        else
        {
            for (var c = 0; c < 3; c++)
            {
                palette[6 + c] = ((2 * palette[c]) + palette[3 + c]) / 3;
                palette[9 + c] = (palette[c] + (2 * palette[3 + c])) / 3;
            }
        }

        uint indices = 0;
        long error = 0;
        for (var i = 0; i < 16; i++)
        {
            var bestIndex = 0;
            var bestDistance = int.MaxValue;
            for (var p = 0; p < count; p++)
            {
                var distance = Distance(block, i, palette[p * 3], palette[(p * 3) + 1], palette[(p * 3) + 2]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = p;
                }
            }

            indices |= (uint)bestIndex << (2 * i);
            error += bestDistance;
        }

        output[4] = (byte)indices;
        output[5] = (byte)(indices >> 8);
        output[6] = (byte)(indices >> 16);
        output[7] = (byte)(indices >> 24);
        return error;
    }

    private static int Distance(ReadOnlySpan<byte> block, int texel, int r, int g, int b)
    {
        var dr = block[texel * 4] - r;
        var dg = block[(texel * 4) + 1] - g;
        var db = block[(texel * 4) + 2] - b;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    /// <summary>BC2: 16 explicit 4-bit alphas.</summary>
    internal static void EncodeExplicitAlpha(ReadOnlySpan<byte> block, Span<byte> output)
    {
        for (var i = 0; i < 16; i += 2)
        {
            var lo = ((block[(i * 4) + 3] * 15) + 127) / 255;
            var hi = ((block[((i + 1) * 4) + 3] * 15) + 127) / 255;
            output[i / 2] = (byte)(lo | (hi << 4));
        }
    }

    /// <summary>BC3: two alpha endpoints and 16 three-bit indices.</summary>
    internal static void EncodeInterpolatedAlpha(ReadOnlySpan<byte> block, Span<byte> output)
    {
        int min = 255, max = 0;
        for (var i = 0; i < 16; i++)
        {
            min = Math.Min(min, block[(i * 4) + 3]);
            max = Math.Max(max, block[(i * 4) + 3]);
        }

        output[0] = (byte)max;
        output[1] = (byte)min;
        ulong indices = 0;
        if (max != min)
        {
            // Eight-value mode (a0 > a1): a0, a1, then (6a0 + a1) / 7 ... (a0 + 6a1) / 7 at indices 2..7.
            Span<int> palette = stackalloc int[8];
            palette[0] = max;
            palette[1] = min;
            for (var k = 1; k <= 6; k++)
            {
                palette[k + 1] = (((7 - k) * max) + (k * min)) / 7;
            }

            for (var i = 0; i < 16; i++)
            {
                var a = block[(i * 4) + 3];
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var p = 0; p < 8; p++)
                {
                    var distance = Math.Abs(a - palette[p]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = p;
                    }
                }

                indices |= (ulong)best << (3 * i);
            }
        }

        for (var b = 0; b < 6; b++)
        {
            output[2 + b] = (byte)(indices >> (8 * b));
        }
    }

    private static ushort To565(int r, int g, int b) =>
        (ushort)((((r * 31) + 127) / 255 << 11) | (((g * 63) + 127) / 255 << 5) | (((b * 31) + 127) / 255));

    private static void From565(ushort c, Span<int> rgb)
    {
        var r = (c >> 11) & 31;
        var g = (c >> 5) & 63;
        var b = c & 31;
        rgb[0] = (r << 3) | (r >> 2);
        rgb[1] = (g << 2) | (g >> 4);
        rgb[2] = (b << 3) | (b >> 2);
    }
}
