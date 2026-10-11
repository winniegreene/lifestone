using Lifestone.Data.Terrain.Blending;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>The terrain arrays' box-filtered mip chains (<see cref="TerrainMipChain"/>).</summary>
public sealed class TerrainMipChainTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(8, 4, 4)]
    [InlineData(512, 10, 4)]
    [InlineData(1024, 11, 4)]
    public void Level_counts_are_the_full_chain_and_retails_capped_count(int size, int full, int retail)
    {
        Assert.Equal(full, TerrainMipChain.LevelCount(size));
        Assert.Equal(retail, TerrainMipChain.RetailLevelCount(size));
    }

    [Fact]
    public void Byte_layout_is_level_zero_first_without_padding()
    {
        // 512² RGBA8: 4 * (512² + 256² + ... + 1²) = 4 * 349,525.
        Assert.Equal(1_398_100, TerrainMipChain.ByteLength(512, 4));
        Assert.Equal(349_525, TerrainMipChain.ByteLength(512, 1));
        Assert.Equal(0, TerrainMipChain.LevelOffset(512, 4, 0));
        Assert.Equal(512 * 512 * 4, TerrainMipChain.LevelOffset(512, 4, 1));
        Assert.Equal((512 * 512 * 4) + (256 * 256 * 4), TerrainMipChain.LevelOffset(512, 4, 2));
        Assert.Equal(TerrainMipChain.ByteLength(512, 4) - 4, TerrainMipChain.LevelOffset(512, 4, 9));
    }

    [Fact]
    public void Each_level_averages_two_by_two_texels_rounding_half_up()
    {
        // 4x4 single-channel image; level 1 is 2x2, level 2 is 1x1.
        byte[] level0 =
        [
            0, 1, 10, 20,
            1, 1, 30, 40,
            255, 255, 2, 1,
            255, 254, 1, 1,
        ];
        var chain = TerrainMipChain.Build(level0, 4, 1);
        Assert.Equal(16 + 4 + 1, chain.Length);
        Assert.Equal(level0, chain[..16]);

        // (0+1+1+1+2)>>2 = 1, (10+20+30+40+2)>>2 = 25, (255+255+255+254+2)>>2 = 255, (2+1+1+1+2)>>2 = 1.
        Assert.Equal([1, 25, 255, 1], chain[16..20]);
        Assert.Equal((1 + 25 + 255 + 1 + 2) >> 2, chain[20]);
    }

    [Fact]
    public void Rgba_channels_are_filtered_independently()
    {
        var level0 = new byte[2 * 2 * 4];
        for (var texel = 0; texel < 4; texel++)
        {
            level0[(texel * 4) + 0] = (byte)(texel * 10);       // 0, 10, 20, 30 -> 15
            level0[(texel * 4) + 1] = 200;                      // 200
            level0[(texel * 4) + 2] = (byte)(texel == 0 ? 3 : 0); // 3 -> (3+2)>>2 = 1
            level0[(texel * 4) + 3] = 255;
        }

        var chain = TerrainMipChain.Build(level0, 2, 4);
        Assert.Equal([15, 200, 1, 255], chain[16..20]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(48)]
    public void Non_power_of_two_sizes_are_refused(int size)
    {
        Assert.Throws<NotSupportedException>(() => TerrainMipChain.Build(new byte[Math.Max(0, size * size)], size, 1));
    }

    [Fact]
    public void Wrong_lengths_and_texel_sizes_are_refused()
    {
        Assert.Throws<ArgumentException>(() => TerrainMipChain.Build(new byte[15], 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMipChain.Build(new byte[16 * 3], 4, 3));
    }
}
