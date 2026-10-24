using Lifestone.Client.Textures;

namespace Lifestone.Client.Tests;

public sealed class MipChainTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(256, 256, 9)]
    [InlineData(512, 128, 10)]
    [InlineData(3, 7, 3)]
    public void Full_chains_go_down_to_one_texel(int w, int h, int levels) => Assert.Equal(levels, MipChain.LevelCount(w, h));

    [Fact]
    public void Block_levels_are_whole_blocks_as_engines_size_them()
    {
        Assert.Equal(8, MipChain.BlockLevelSize(1, 1, BlockFormat.Bc1));
        Assert.Equal(8, MipChain.BlockLevelSize(4, 4, BlockFormat.Bc1));
        Assert.Equal(16, MipChain.BlockLevelSize(2, 2, BlockFormat.Bc3));
        Assert.Equal(64 * 64 * 8, MipChain.BlockLevelSize(256, 256, BlockFormat.Bc1));
        Assert.Equal(2 * 1 * 16, MipChain.BlockLevelSize(8, 3, BlockFormat.Bc2));
    }

    [Fact]
    public void Box_filter_rounds_the_mean_of_each_2x2_block()
    {
        // 2x2 -> 1x1: (10 + 11 + 12 + 14 + 2) / 4 = 12 (rounded); alpha (0 + 255 + 255 + 255 + 2) / 4 = 191.
        byte[] rgba = [10, 0, 0, 0, 11, 0, 0, 255, 12, 0, 0, 255, 14, 0, 0, 255];
        var next = MipChain.Downsample(rgba, 2, 2, out var w, out var h);
        Assert.Equal((1, 1), (w, h));
        Assert.Equal(new byte[] { 12, 0, 0, 191 }, next);

        // Odd sizes repeat the last column: 3x1 -> 1x1 averages texels 0 and 1 (twice).
        byte[] row = [0, 0, 0, 0, 100, 0, 0, 0, 200, 0, 0, 0];
        Assert.Equal(50, MipChain.Downsample(row, 3, 1, out _, out _)[0]);
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(13, 7)]
    [InlineData(1, 9)]
    [InlineData(10, 1)]
    public void In_place_box_filter_matches_the_allocating_one_at_every_level(int width, int height)
    {
        var expected = Noise(width, height);
        var scratch = new byte[expected.Length + 32]; // pooled buffers are longer than the level
        expected.CopyTo(scratch, 0);
        int w = width, h = height;
        while (w > 1 || h > 1)
        {
            expected = MipChain.Downsample(expected, w, h, out var ew, out var eh);
            MipChain.DownsampleInPlace(scratch, w, h, out w, out h);
            Assert.Equal((ew, eh), (w, h));
            Assert.Equal(expected, scratch[..(w * h * 4)]);
        }
    }

    [Theory]
    [InlineData(BlockFormat.Bc1, 16, 16)]
    [InlineData(BlockFormat.Bc2, 12, 20)]
    [InlineData(BlockFormat.Bc3, 32, 8)]
    public void The_buffer_taking_chain_equals_downsampling_and_encoding_level_by_level(BlockFormat format, int width, int height)
    {
        var rgba = Gradient(width, height);
        var level0 = new byte[MipChain.BlockLevelSize(width, height, format)];
        BlockEncoder.Encode(rgba, width, height, format, level0);

        // The pre-pooling algorithm: a new array per level.
        var reference = new List<byte>(level0);
        var level = rgba;
        int w = width, h = height;
        while (w > 1 || h > 1)
        {
            level = MipChain.Downsample(level, w, h, out w, out h);
            var blocks = new byte[MipChain.BlockLevelSize(w, h, format)];
            BlockEncoder.Encode(level, w, h, format, blocks);
            reference.AddRange(blocks);
        }

        var destination = new byte[MipChain.BlockChainSize(width, height, format) + 64];
        MipChain.BuildBlockChain(level0, (byte[])rgba.Clone(), width, height, format, destination);
        Assert.Equal(reference.Count, MipChain.BlockChainSize(width, height, format));
        Assert.Equal(reference.ToArray(), destination[..reference.Count]);
        Assert.Equal(reference.ToArray(), MipChain.BuildBlockChain(level0, rgba, width, height, format));
    }

    [Theory]
    [InlineData(BlockFormat.Bc1)]
    [InlineData(BlockFormat.Bc2)]
    [InlineData(BlockFormat.Bc3)]
    public void Block_chain_keeps_level_zero_and_encodes_every_lower_level(BlockFormat format)
    {
        const int size = 16;
        var rgba = Gradient(size, size);
        var level0 = new byte[MipChain.BlockLevelSize(size, size, format)];
        BlockEncoder.Encode(rgba, size, size, format, level0);
        level0[0] ^= 0x5A; // a level 0 we did not produce must survive untouched

        var chain = MipChain.BuildBlockChain(level0, rgba, size, size, format);
        var expected = MipChain.BlockLevelSize(16, 16, format) + MipChain.BlockLevelSize(8, 8, format) +
            MipChain.BlockLevelSize(4, 4, format) + MipChain.BlockLevelSize(2, 2, format) + MipChain.BlockLevelSize(1, 1, format);
        Assert.Equal(expected, chain.Length);
        Assert.Equal(level0, chain[..level0.Length]);

        // Level 1 decodes to the box-filtered level 0 within encoder error.
        var level1 = MipChain.Downsample(rgba, size, size, out var w1, out var h1);
        var decoded = ReferenceBlockDecoder.Decode(chain.AsSpan(level0.Length, MipChain.BlockLevelSize(w1, h1, format)), w1, h1, format);
        var level1Error = MaxError(level1, decoded, alpha: format != BlockFormat.Bc1);
        Assert.True(level1Error <= 24, $"level 1 error {level1Error}");
    }

    [Theory]
    [InlineData(BlockFormat.Bc1)]
    [InlineData(BlockFormat.Bc2)]
    [InlineData(BlockFormat.Bc3)]
    public void Encoded_blocks_decode_close_to_the_source(BlockFormat format)
    {
        var random = new Random(7);
        for (var trial = 0; trial < 200; trial++)
        {
            // Smooth blocks (a gradient between two random colours) are what minified levels mostly hold.
            var rgba = new byte[16 * 4];
            var a = new int[4];
            var b = new int[4];
            for (var c = 0; c < 4; c++)
            {
                a[c] = random.Next(256);
                b[c] = random.Next(256);
            }

            for (var i = 0; i < 16; i++)
            {
                for (var c = 0; c < 4; c++)
                {
                    rgba[(i * 4) + c] = (byte)(a[c] + (((b[c] - a[c]) * i) / 15));
                }
            }

            var encoded = new byte[MipChain.BlockLevelSize(4, 4, format)];
            BlockEncoder.Encode(rgba, 4, 4, format, encoded);
            var decoded = ReferenceBlockDecoder.Decode(encoded, 4, 4, format);
            // Four palette entries on the line between the extremes: at most 1/6 of the range (42) plus 565 rounding.
            var colourError = MaxError(rgba, decoded, alpha: false);
            Assert.True(colourError <= 48, $"colour error {colourError}, trial {trial}");
            if (format != BlockFormat.Bc1)
            {
                var alphaError = Enumerable.Range(0, 16).Max(i => Math.Abs(rgba[(i * 4) + 3] - decoded[(i * 4) + 3]));
                Assert.True(alphaError <= (format == BlockFormat.Bc2 ? 9 : 19), $"alpha error {alphaError}, trial {trial}");
            }

            if (format == BlockFormat.Bc1)
            {
                // Never three-colour mode with index 3 (which BC1 decodes as transparent black).
                var c0 = encoded[0] | (encoded[1] << 8);
                var c1 = encoded[2] | (encoded[3] << 8);
                var indices = BitConverter.ToUInt32(encoded, 4);
                Assert.True(c0 > c1 || indices == 0, $"trial {trial}: c0 {c0} c1 {c1} indices {indices:X8}");
            }
        }
    }

    [Fact]
    public void Flat_and_two_colour_blocks_round_trip_exactly()
    {
        // 565-exact colours and alphas 0/255 survive unchanged.
        var rgba = new byte[16 * 4];
        for (var i = 0; i < 16; i++)
        {
            var on = i % 3 == 0;
            rgba[i * 4] = on ? (byte)255 : (byte)0;
            rgba[(i * 4) + 1] = on ? (byte)255 : (byte)0;
            rgba[(i * 4) + 2] = on ? (byte)255 : (byte)0;
            rgba[(i * 4) + 3] = on ? (byte)255 : (byte)0;
        }

        foreach (var format in new[] { BlockFormat.Bc1, BlockFormat.Bc2, BlockFormat.Bc3 })
        {
            var encoded = new byte[MipChain.BlockLevelSize(4, 4, format)];
            BlockEncoder.Encode(rgba, 4, 4, format, encoded);
            var decoded = ReferenceBlockDecoder.Decode(encoded, 4, 4, format);
            Assert.Equal(0, MaxError(rgba, decoded, alpha: format != BlockFormat.Bc1));
        }
    }

    private static byte[] Noise(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        new Random(w * 31 + h).NextBytes(rgba);
        return rgba;
    }

    private static byte[] Gradient(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // Colour varies along x only (a line in RGB, which BCn represents well; a 2-D colour gradient spans a
                // plane that no four-colour palette can follow), alpha along y.
                var i = ((y * w) + x) * 4;
                rgba[i] = (byte)(x * 255 / (w - 1));
                rgba[i + 1] = (byte)(255 - (x * 255 / (w - 1)));
                rgba[i + 2] = (byte)(64 + (x * 128 / (w - 1)));
                rgba[i + 3] = (byte)(y * 255 / (h - 1));
            }
        }

        return rgba;
    }

    private static int MaxError(byte[] expected, byte[] actual, bool alpha)
    {
        var max = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!alpha && i % 4 == 3)
            {
                continue;
            }

            max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        }

        return max;
    }

    /// <summary>An independent BCn decoder (straight from the format description) for the tests.</summary>
    private static class ReferenceBlockDecoder
    {
        public static byte[] Decode(ReadOnlySpan<byte> blocks, int w, int h, BlockFormat format)
        {
            var size = format == BlockFormat.Bc1 ? 8 : 16;
            var result = new byte[w * h * 4];
            var bw = (w + 3) / 4;
            for (var by = 0; by < (h + 3) / 4; by++)
            {
                for (var bx = 0; bx < bw; bx++)
                {
                    var block = blocks.Slice(((by * bw) + bx) * size, size);
                    var colour = format == BlockFormat.Bc1 ? block : block[8..];
                    var c0 = colour[0] | (colour[1] << 8);
                    var c1 = colour[2] | (colour[3] << 8);
                    var p = new int[4, 4];
                    Expand(c0, p, 0);
                    Expand(c1, p, 1);
                    var four = format != BlockFormat.Bc1 || c0 > c1;
                    for (var c = 0; c < 3; c++)
                    {
                        p[2, c] = four ? ((2 * p[0, c]) + p[1, c]) / 3 : (p[0, c] + p[1, c]) / 2;
                        p[3, c] = four ? (p[0, c] + (2 * p[1, c])) / 3 : 0;
                    }

                    p[0, 3] = p[1, 3] = p[2, 3] = 255;
                    p[3, 3] = four ? 255 : 0;
                    var indices = BitConverter.ToUInt32(colour.Slice(4, 4));
                    var alphas = new int[16];
                    if (format == BlockFormat.Bc2)
                    {
                        for (var i = 0; i < 16; i++)
                        {
                            alphas[i] = ((block[i / 2] >> (4 * (i % 2))) & 15) * 17;
                        }
                    }
                    else if (format == BlockFormat.Bc3)
                    {
                        int a0 = block[0], a1 = block[1];
                        ulong bits = 0;
                        for (var b = 0; b < 6; b++)
                        {
                            bits |= (ulong)block[2 + b] << (8 * b);
                        }

                        for (var i = 0; i < 16; i++)
                        {
                            var code = (int)((bits >> (3 * i)) & 7);
                            alphas[i] = code switch
                            {
                                0 => a0,
                                1 => a1,
                                _ when a0 > a1 => (((8 - code) * a0) + ((code - 1) * a1)) / 7,
                                6 => 0,
                                7 => 255,
                                _ => (((6 - code) * a0) + ((code - 1) * a1)) / 5,
                            };
                        }
                    }

                    for (var i = 0; i < 16; i++)
                    {
                        var x = (bx * 4) + (i % 4);
                        var y = (by * 4) + (i / 4);
                        if (x >= w || y >= h)
                        {
                            continue;
                        }

                        var index = (int)((indices >> (2 * i)) & 3);
                        var o = ((y * w) + x) * 4;
                        result[o] = (byte)p[index, 0];
                        result[o + 1] = (byte)p[index, 1];
                        result[o + 2] = (byte)p[index, 2];
                        result[o + 3] = format == BlockFormat.Bc1 ? (byte)p[index, 3] : (byte)alphas[i];
                    }
                }
            }

            return result;
        }

        private static void Expand(int c, int[,] p, int row)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            p[row, 0] = (r << 3) | (r >> 2);
            p[row, 1] = (g << 2) | (g >> 4);
            p[row, 2] = (b << 3) | (b >> 2);
        }
    }
}
