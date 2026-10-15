using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.RetailUi.Rendering;

/// <summary>
/// Lifestone's baseline JPEG decoder (T.81): hand-built files with known pixels (grey, three components with 4:2:0 and
/// 4:2:2 chroma, restart intervals, Huffman-coded AC coefficients), refusals, the inverse DCT, and the six retail UI JPEG
/// images.
/// </summary>
public sealed class UiJpegDecoderTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void A_block_with_no_coefficients_is_mid_grey()
    {
        Assert.True(UiJpegDecoder.Decode(Grey(dcCategory: 0, dcBits: 0), out var width, out var height, out var rgba, out var failure), failure);

        Assert.Equal((8, 8), (width, height));
        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(128, rgba![i * 4]));
        Assert.Equal(255, rgba![3]);
    }

    [Fact]
    public void A_dc_coefficient_of_8_raises_every_sample_by_one()
    {
        // DC category 4, value 1000b = 8; quantizer 1: f = F(0,0) / 8 = 1.
        Assert.True(UiJpegDecoder.Decode(Grey(dcCategory: 4, dcBits: 0b1000), out _, out _, out var rgba, out var failure), failure);

        Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(129, rgba![i * 4]));
    }

    [Fact]
    public void The_inverse_dct_of_one_ac_coefficient_is_a_cosine()
    {
        Span<int> coefficients = stackalloc int[64];
        coefficients[1] = 80; // horizontal frequency 1
        var samples = new byte[64];

        UiJpegDecoder.InverseDct(coefficients, samples, 8);

        // f(x) = (1/4) C(0) C(1) 80 cos((2x + 1) pi / 16) = 14.14 cos(...): 13.9, 11.8, 7.9, 2.8 and their negatives.
        Assert.Equal([142, 140, 136, 131, 125, 120, 116, 114], samples.AsSpan(0, 8).ToArray());
        Assert.Equal(samples.AsSpan(0, 8).ToArray(), samples.AsSpan(56, 8).ToArray());
    }

    [Fact]
    public void Files_it_cannot_decode_are_refused_with_a_reason()
    {
        Assert.False(UiJpegDecoder.Decode([1, 2, 3, 4], out _, out _, out _, out var notJpeg));
        Assert.Contains("SOI", notJpeg, StringComparison.Ordinal);

        var progressive = Grey(0, 0);
        progressive[Array.IndexOf(progressive, (byte)0xC0, 2)] = 0xC2;
        Assert.False(UiJpegDecoder.Decode(progressive, out _, out _, out _, out var refused));
        Assert.Contains("SOF2", refused, StringComparison.Ordinal);

        Assert.False(UiJpegDecoder.Decode(Grey(0, 0).AsSpan(0, 30), out _, out _, out _, out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void Three_components_with_subsampled_chroma_decode_to_the_JFIF_colours_of_each_block(int lumaRows)
    {
        // Two MCUs of luma 2x2 (4:2:0) or 2x1 (4:2:2) blocks and one Cb and one Cr block each; quantizer 1, so a block's
        // samples are 128 + DC / 8. MCU 0: luma 138, 108 (top) and 128, 178 (bottom), Cb 148, Cr 98; MCU 1: luma 128,
        // Cb 118, Cr 148. Each chroma block covers its whole MCU (replicated, not smoothed across MCUs).
        var width = 32;
        var height = 8 * lumaRows;
        int[] lumaDc = lumaRows == 2 ? [80, -160, 0, 400] : [80, -160];
        var blocks = new List<(int Component, int[] Coefficients)>();
        foreach (var (luma, cb, cr) in new[] { (lumaDc, 160, -240), (new int[lumaDc.Length], -80, 160) })
        {
            blocks.AddRange(luma.Select(dc => (0, Dc(dc))));
            blocks.Add((1, Dc(cb)));
            blocks.Add((2, Dc(cr)));
        }

        var file = JpegWriter.Build(width, height, [(2, lumaRows), (1, 1), (1, 1)], restartInterval: 0, blocks);

        Assert.True(UiJpegDecoder.Decode(file, out var w, out var h, out var rgba, out var failure), failure);
        Assert.Equal((width, height), (w, h));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // R = Y + 1.402 Cr', G = Y - 0.344136 Cb' - 0.714136 Cr', B = Y + 1.772 Cb', rounded (Cb' = Cb - 128):
                // Cb' 20 and Cr' -30 in MCU 0, Cb' -10 and Cr' 20 in MCU 1.
                var luma = (x < 8, y < 8) switch
                {
                    (true, true) => 138,
                    (false, true) => 108,
                    (true, false) => 128,
                    _ => 178,
                };
                (int R, int G, int B) expected = x >= 16 ? (128 + 28, 128 - 11, 128 - 18) : (luma - 42, luma + 15, luma + 35);
                var i = ((y * width) + x) * 4;
                Assert.True(expected == (rgba![i], rgba[i + 1], rgba[i + 2]), $"pixel ({x}, {y}): {(rgba[i], rgba[i + 1], rgba[i + 2])}, expected {expected}");
                Assert.Equal(255, rgba[i + 3]);
            }
        }
    }

    [Fact]
    public void A_restart_interval_resets_the_dc_predictors_after_its_marker()
    {
        // Four grey blocks, a restart every two: the third block's DC is coded from 0 again.
        var blocks = new[] { 8, 16, -8, 0 }.Select(dc => (0, Dc(dc))).ToList();

        var file = JpegWriter.Build(32, 8, [(1, 1)], restartInterval: 2, blocks);

        Assert.Contains(file.Zip(file.Skip(1)), p => p is (0xFF, 0xD0));
        Assert.True(UiJpegDecoder.Decode(file, out _, out _, out var rgba, out var failure), failure);
        Assert.Equal([129, 130, 127, 128], Enumerable.Range(0, 4).Select(b => (int)rgba![b * 8 * 4]));
        Assert.All(Enumerable.Range(0, 32 * 8), p => Assert.Equal(128 + (new[] { 1, 2, -1, 0 }[(p % 32) / 8]), rgba![p * 4]));
    }

    [Fact]
    public void Huffman_coded_ac_coefficients_with_a_zero_run_decode_to_their_inverse_dct()
    {
        // Zig-zag positions 1, 2 and 5 (runs 0, 0 and 2), then a run of 24 zeros (a ZRL and a run of 8) to position 30.
        var zigzag = new int[64];
        zigzag[0] = 16;
        zigzag[1] = 80;
        zigzag[2] = -40;
        zigzag[5] = 24;
        zigzag[30] = -16;

        var file = JpegWriter.Build(8, 8, [(1, 1)], restartInterval: 0, [(0, zigzag)]);

        Assert.True(UiJpegDecoder.Decode(file, out _, out _, out var rgba, out var failure), failure);
        var natural = new int[64];
        for (var k = 0; k < 64; k++)
        {
            natural[JpegWriter.ZigZag[k]] = zigzag[k];
        }

        var expected = new byte[64];
        UiJpegDecoder.InverseDct(natural, expected, 8);
        Assert.Equal(expected, Enumerable.Range(0, 64).Select(i => rgba![i * 4]).ToArray());
        Assert.Contains(expected, b => b != expected[0]);
    }

    [RequiresDatsTheory]
    [InlineData(0x0600610Fu, 640, 480)]
    [InlineData(0x06006EBCu, 640, 480)]
    [InlineData(0x06005D3Fu, 800, 600)]
    [InlineData(0x06007576u, 800, 600)]
    [InlineData(0x06004D26u, 800, 600)]
    [InlineData(0x06004D5Fu, 471, 471)]
    public void The_retail_UI_JPEG_images_decode(uint did, int width, int height)
    {
        var jpeg = new TextureLoader(fixture.Dats).DecodeRenderSurface(did, TextureDecodeMode.Rgba8);
        Assert.Equal(DecodedTextureFormat.Jpeg, jpeg.Format);

        Assert.True(UiJpegDecoder.Decode(jpeg.Data, out var w, out var h, out var first, out var failure), failure);
        Assert.True(UiJpegDecoder.Decode(jpeg.Data, out _, out _, out var second, out _));

        Assert.Equal((width, height), (w, h));
        Assert.Equal(first, second);
        Assert.Contains(first!.Where((_, i) => i % 4 != 3), b => b > 32);
    }

    /// <summary>
    /// An 8×8 one-component baseline file: quantizer all 1, a DC table with one 1-bit code for <paramref name="dcCategory"/>,
    /// an AC table with one 1-bit code for end-of-block.
    /// </summary>
    private static byte[] Grey(int dcCategory, int dcBits)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xDB, 0x00, 0x43, 0x00]);
        bytes.AddRange(Enumerable.Repeat((byte)1, 64));
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00]);
        bytes.AddRange([0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, .. new byte[15], (byte)dcCategory]);
        bytes.AddRange([0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, .. new byte[15], 0x00]);
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);

        // DC code '0', its category's bits, AC code '0' (end of block), padded with ones.
        var bits = "0" + (dcCategory == 0 ? string.Empty : Convert.ToString(dcBits, 2).PadLeft(dcCategory, '0')) + "0";
        bits = bits.PadRight((bits.Length + 7) / 8 * 8, '1');
        for (var i = 0; i < bits.Length; i += 8)
        {
            var b = Convert.ToByte(bits.Substring(i, 8), 2);
            bytes.Add(b);
            if (b == 0xFF)
            {
                bytes.Add(0);
            }
        }

        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    /// <summary>A block whose only coefficient is the DC term <paramref name="dc"/> (zig-zag order).</summary>
    private static int[] Dc(int dc)
    {
        var block = new int[64];
        block[0] = dc;
        return block;
    }

    /// <summary>
    /// A baseline JFIF writer for the tests (T.81, written for them): quantizer 1 everywhere, one DC and one AC Huffman
    /// table holding exactly the symbols the blocks use (each code as long as the largest symbol count needs), blocks given
    /// in decode order with their coefficients in zig-zag order, and restart markers every <c>restartInterval</c> MCUs.
    /// </summary>
    private static class JpegWriter
    {
        private const int RestartSymbol = -1;

        public static readonly int[] ZigZag =
        [
            0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        ];

        public static byte[] Build(int width, int height, (int H, int V)[] sampling, int restartInterval, IReadOnlyList<(int Component, int[] Coefficients)> blocks)
        {
            var blocksPerMcu = sampling.Length == 1 ? 1 : sampling.Sum(s => s.H * s.V);
            var dcSymbols = new SortedSet<int>();
            var acSymbols = new SortedSet<int>();
            Encode(blocks, sampling.Length, blocksPerMcu, restartInterval, (dc, symbol) => (dc ? dcSymbols : acSymbols).Add(symbol), null);
            var dcCodes = Codes(dcSymbols, out var dcLength);
            var acCodes = Codes(acSymbols, out var acLength);

            var bytes = new List<byte> { 0xFF, 0xD8 };
            bytes.AddRange([0xFF, 0xDB, 0x00, 0x43, 0x00]);
            bytes.AddRange(Enumerable.Repeat((byte)1, 64));
            bytes.AddRange([0xFF, 0xC0, 0x00, (byte)(8 + (3 * sampling.Length)), 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)sampling.Length]);
            for (var c = 0; c < sampling.Length; c++)
            {
                bytes.AddRange([(byte)(c + 1), (byte)((sampling[c].H << 4) | sampling[c].V), 0x00]);
            }

            Table(bytes, 0x00, dcSymbols, dcLength);
            Table(bytes, 0x10, acSymbols, acLength);
            if (restartInterval != 0)
            {
                bytes.AddRange([0xFF, 0xDD, 0x00, 0x04, (byte)(restartInterval >> 8), (byte)restartInterval]);
            }

            bytes.AddRange([0xFF, 0xDA, 0x00, (byte)(6 + (2 * sampling.Length)), (byte)sampling.Length]);
            for (var c = 0; c < sampling.Length; c++)
            {
                bytes.AddRange([(byte)(c + 1), 0x00]);
            }

            bytes.AddRange([0x00, 0x3F, 0x00]);
            var writer = new BitWriter(bytes);
            Encode(blocks, sampling.Length, blocksPerMcu, restartInterval, null, (dc, symbol, bits, count) =>
            {
                if (symbol == RestartSymbol)
                {
                    writer.Restart(bits);
                    return;
                }

                var (code, length) = dc ? (dcCodes[symbol], dcLength) : (acCodes[symbol], acLength);
                writer.Write(code, length);
                writer.Write(bits, count);
            });
            writer.Flush();
            bytes.AddRange([0xFF, 0xD9]);
            return [.. bytes];
        }

        private static void Encode(
            IReadOnlyList<(int Component, int[] Coefficients)> blocks, int components, int blocksPerMcu, int restartInterval,
            Action<bool, int>? symbols, Action<bool, int, int, int>? emit)
        {
            var predictors = new int[components];
            for (var b = 0; b < blocks.Count; b++)
            {
                var mcu = b / blocksPerMcu;
                if (restartInterval != 0 && b % blocksPerMcu == 0 && mcu != 0 && mcu % restartInterval == 0)
                {
                    Array.Clear(predictors);
                    emit?.Invoke(false, RestartSymbol, (mcu / restartInterval) - 1, 0);
                }

                var (component, zz) = blocks[b];
                var diff = zz[0] - predictors[component];
                predictors[component] = zz[0];
                var category = Category(diff);
                symbols?.Invoke(true, category);
                emit?.Invoke(true, category, Bits(diff, category), category);
                var run = 0;
                for (var k = 1; k < 64; k++)
                {
                    if (zz[k] == 0)
                    {
                        run++;
                        continue;
                    }

                    for (; run >= 16; run -= 16)
                    {
                        symbols?.Invoke(false, 0xF0);
                        emit?.Invoke(false, 0xF0, 0, 0);
                    }

                    var size = Category(zz[k]);
                    symbols?.Invoke(false, (run << 4) | size);
                    emit?.Invoke(false, (run << 4) | size, Bits(zz[k], size), size);
                    run = 0;
                }

                if (run != 0)
                {
                    symbols?.Invoke(false, 0x00);
                    emit?.Invoke(false, 0x00, 0, 0);
                }
            }
        }

        private static int Category(int value)
        {
            var magnitude = Math.Abs(value);
            var bits = 0;
            for (; magnitude != 0; magnitude >>= 1)
            {
                bits++;
            }

            return bits;
        }

        /// <summary>The value's bits as T.81 F.1.2.1 sends them: negative values one less, in the category's width.</summary>
        private static int Bits(int value, int category) => value >= 0 ? value : value + (1 << category) - 1;

        /// <summary>Every symbol gets a code of one length, the shortest that leaves the all-ones code unused.</summary>
        private static Dictionary<int, int> Codes(SortedSet<int> symbols, out int length)
        {
            length = 1;
            while ((1 << length) <= symbols.Count)
            {
                length++;
            }

            var codes = new Dictionary<int, int>();
            var code = 0;
            foreach (var symbol in symbols)
            {
                codes[symbol] = code++;
            }

            return codes;
        }

        private static void Table(List<byte> bytes, byte classAndId, SortedSet<int> symbols, int length)
        {
            bytes.AddRange([0xFF, 0xC4, 0x00, (byte)(19 + symbols.Count), classAndId]);
            for (var l = 1; l <= 16; l++)
            {
                bytes.Add(l == length ? (byte)symbols.Count : (byte)0);
            }

            bytes.AddRange(symbols.Select(v => (byte)v));
        }

        private sealed class BitWriter(List<byte> bytes)
        {
            private int _bits;
            private int _count;

            public void Write(int value, int count)
            {
                for (var i = count - 1; i >= 0; i--)
                {
                    _bits = (_bits << 1) | ((value >> i) & 1);
                    if (++_count == 8)
                    {
                        Emit();
                    }
                }
            }

            /// <summary>Pads to a byte with ones and writes RSTn.</summary>
            public void Restart(int n)
            {
                Flush();
                bytes.AddRange([0xFF, (byte)(0xD0 + (n % 8))]);
            }

            public void Flush()
            {
                while (_count != 0)
                {
                    Write(1, 1);
                }
            }

            private void Emit()
            {
                bytes.Add((byte)_bits);
                if (_bits == 0xFF)
                {
                    bytes.Add(0x00);
                }

                _bits = 0;
                _count = 0;
            }
        }
    }
}
