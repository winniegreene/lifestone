using System.Buffers.Binary;

namespace Lifestone.Data.Textures;

/// <summary>CPU decoder for the DXT1/DXT3/DXT5 (BC1/BC2/BC3) blocks stored in RenderSurfaces.</summary>
/// <remarks>
/// <para>
/// Retail never decodes these on the CPU: <c>RenderSurface::CreateFromSourceData</c> copies the blocks straight into a
/// D3D texture and the GPU decodes them (D3DX converts only when the device lacks compressed-texture support,
/// <c>RenderSurface::SelectSurfaceFormat</c>). This decoder exists for the RGBA8 path (CPU compositing, tests,
/// devices without BCn) and follows the published block layout (Microsoft "Block Compression (Direct3D 10)" /
/// S3TC): 4x4 blocks in row-major order, texel k of a block at (k &amp; 3, k &gt;&gt; 2), little-endian fields.
/// </para>
/// <para>
/// Rounding is not specified bit-exactly by D3D, so hardware decoders differ by a step or two. We use the rules of
/// the research reference decoder (scratchpad <c>research/scout/acimage.py</c>), which the tests port independently:
/// RGB565 endpoints expand by bit replication (<c>r5 &lt;&lt; 3 | r5 &gt;&gt; 2</c>, the same expansion ACE's
/// <c>Texture.get565RGB</c> uses for R5G6B5), and interpolated colours and alphas truncate
/// (<c>(2*c0 + c1) / 3</c>, <c>((7-k)*a0 + k*a1) / 7</c>), as ACE <c>DxtUtil</c> and OpenAC do. The DXT1 three-colour
/// mode (<c>c0 &lt;= c1</c>) maps index 3 to transparent black (0,0,0,0), matching D3D9's 1-bit-alpha DXT1. DXT3/DXT5
/// colour blocks always use the four-colour mode.
/// </para>
/// </remarks>
public static class BlockCompression
{
    /// <summary>Bytes per 4x4 block: 8 for DXT1, 16 for DXT3/DXT5.</summary>
    public static int BlockSize(DecodedTextureFormat format) => format switch
    {
        DecodedTextureFormat.Dxt1 => 8,
        DecodedTextureFormat.Dxt3 or DecodedTextureFormat.Dxt5 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a block-compressed format."),
    };

    /// <summary>Total byte size of a block-compressed image with partial edge blocks rounded up.</summary>
    public static int ByteLength(DecodedTextureFormat format, int width, int height) =>
        BlocksAcross(width) * BlocksAcross(height) * BlockSize(format);

    /// <summary>Decodes DXT1/DXT3/DXT5 blocks into R,G,B,A bytes (<paramref name="rgba"/> must hold w*h*4 bytes).</summary>
    public static void DecodeToRgba8(DecodedTextureFormat format, ReadOnlySpan<byte> blocks, int width, int height, Span<byte> rgba)
    {
        var expected = ByteLength(format, width, height);
        if (blocks.Length < expected)
        {
            throw new InvalidDataException($"{format} {width}x{height} needs {expected} bytes of blocks, got {blocks.Length}.");
        }

        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException($"Destination holds {rgba.Length} bytes, need {width * height * 4}.", nameof(rgba));
        }

        var blockSize = BlockSize(format);
        var blocksX = BlocksAcross(width);
        var blocksY = BlocksAcross(height);
        Span<byte> colours = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[16];
        var offset = 0;
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++, offset += blockSize)
            {
                var block = blocks.Slice(offset, blockSize);
                uint colourIndices;
                switch (format)
                {
                    case DecodedTextureFormat.Dxt1:
                        colourIndices = DecodeColourBlock(block, colours, threeColourModeAllowed: true);
                        WriteBlock(rgba, width, height, bx, by, colours, colourIndices, alphas: default);
                        break;
                    case DecodedTextureFormat.Dxt3:
                        DecodeExplicitAlpha(block, alphas);
                        colourIndices = DecodeColourBlock(block[8..], colours, threeColourModeAllowed: false);
                        WriteBlock(rgba, width, height, bx, by, colours, colourIndices, alphas);
                        break;
                    default:
                        DecodeInterpolatedAlpha(block, alphas);
                        colourIndices = DecodeColourBlock(block[8..], colours, threeColourModeAllowed: false);
                        WriteBlock(rgba, width, height, bx, by, colours, colourIndices, alphas);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// True when decoding would produce at least one texel with alpha below 255: a DXT1 three-colour-mode block that
    /// uses index 3, or a DXT3/DXT5 alpha below 255.
    /// </summary>
    public static bool HasAlpha(DecodedTextureFormat format, ReadOnlySpan<byte> blocks, int width, int height)
    {
        var blockSize = BlockSize(format);
        var count = BlocksAcross(width) * BlocksAcross(height);
        Span<byte> alphas = stackalloc byte[16];
        for (var i = 0; i < count; i++)
        {
            var block = blocks.Slice(i * blockSize, blockSize);
            switch (format)
            {
                case DecodedTextureFormat.Dxt1:
                    var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
                    var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
                    if (c0 <= c1 && UsesIndexThree(BinaryPrimitives.ReadUInt32LittleEndian(block[4..])))
                    {
                        return true;
                    }

                    break;
                case DecodedTextureFormat.Dxt3:
                    if (BinaryPrimitives.ReadUInt64LittleEndian(block) != ulong.MaxValue)
                    {
                        return true;
                    }

                    break;
                default:
                    DecodeInterpolatedAlpha(block, alphas);
                    foreach (var a in alphas)
                    {
                        if (a != 255)
                        {
                            return true;
                        }
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>RGB565 to 8-bit channels by bit replication.</summary>
    internal static void Expand565(ushort c, out byte r, out byte g, out byte b)
    {
        var r5 = (c >> 11) & 0x1F;
        var g6 = (c >> 5) & 0x3F;
        var b5 = c & 0x1F;
        r = (byte)((r5 << 3) | (r5 >> 2));
        g = (byte)((g6 << 2) | (g6 >> 4));
        b = (byte)((b5 << 3) | (b5 >> 2));
    }

    private static int BlocksAcross(int texels) => Math.Max(1, (texels + 3) / 4);

    private static bool UsesIndexThree(uint indices)
    {
        for (var k = 0; k < 16; k++)
        {
            if (((indices >> (2 * k)) & 3) == 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fills <paramref name="colours"/> with the 4 RGBA palette entries; returns the 2-bit index word.</summary>
    private static uint DecodeColourBlock(ReadOnlySpan<byte> block, Span<byte> colours, bool threeColourModeAllowed)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Expand565(c0, out var r0, out var g0, out var b0);
        Expand565(c1, out var r1, out var g1, out var b1);
        Set(colours, 0, r0, g0, b0, 255);
        Set(colours, 1, r1, g1, b1, 255);
        if (threeColourModeAllowed && c0 <= c1)
        {
            Set(colours, 2, (byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), 255);
            Set(colours, 3, 0, 0, 0, 0);
        }
        else
        {
            Set(colours, 2, (byte)(((2 * r0) + r1) / 3), (byte)(((2 * g0) + g1) / 3), (byte)(((2 * b0) + b1) / 3), 255);
            Set(colours, 3, (byte)((r0 + (2 * r1)) / 3), (byte)((g0 + (2 * g1)) / 3), (byte)((b0 + (2 * b1)) / 3), 255);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        static void Set(Span<byte> c, int i, byte r, byte g, byte b, byte a)
        {
            c[i * 4] = r;
            c[(i * 4) + 1] = g;
            c[(i * 4) + 2] = b;
            c[(i * 4) + 3] = a;
        }
    }

    /// <summary>DXT3: 16 explicit 4-bit alphas, texel k in bits 4k..4k+3, scaled by 17.</summary>
    private static void DecodeExplicitAlpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var k = 0; k < 16; k++)
        {
            alphas[k] = (byte)(((bits >> (4 * k)) & 0xF) * 17);
        }
    }

    /// <summary>DXT5: two endpoint alphas and 16 3-bit indices into an 8- or 6-entry interpolated table.</summary>
    private static void DecodeInterpolatedAlpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        int a0 = block[0];
        int a1 = block[1];
        Span<byte> table = stackalloc byte[8];
        table[0] = (byte)a0;
        table[1] = (byte)a1;
        if (a0 > a1)
        {
            for (var k = 1; k <= 6; k++)
            {
                table[k + 1] = (byte)((((7 - k) * a0) + (k * a1)) / 7);
            }
        }
        else
        {
            for (var k = 1; k <= 4; k++)
            {
                table[k + 1] = (byte)((((5 - k) * a0) + (k * a1)) / 5);
            }

            table[6] = 0;
            table[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)block[2 + i] << (8 * i);
        }

        for (var k = 0; k < 16; k++)
        {
            alphas[k] = table[(int)((bits >> (3 * k)) & 7)];
        }
    }

    private static void WriteBlock(Span<byte> rgba, int width, int height, int bx, int by, ReadOnlySpan<byte> colours, uint indices, ReadOnlySpan<byte> alphas)
    {
        for (var k = 0; k < 16; k++)
        {
            var x = (bx * 4) + (k & 3);
            var y = (by * 4) + (k >> 2);
            if (x >= width || y >= height)
            {
                continue;
            }

            var idx = (int)((indices >> (2 * k)) & 3);
            var o = ((y * width) + x) * 4;
            rgba[o] = colours[idx * 4];
            rgba[o + 1] = colours[(idx * 4) + 1];
            rgba[o + 2] = colours[(idx * 4) + 2];
            rgba[o + 3] = alphas.IsEmpty ? colours[(idx * 4) + 3] : alphas[k];
        }
    }
}
