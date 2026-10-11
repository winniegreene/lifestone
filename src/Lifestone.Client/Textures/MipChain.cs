namespace Lifestone.Client.Textures;

/// <summary>Block-compressed formats the mip builder can encode (D3D DXT1/DXT3/DXT5 = BC1/BC2/BC3).</summary>
public enum BlockFormat
{
    Bc1,
    Bc2,
    Bc3,
}

/// <summary>
/// Mip chains for engine upload: 2x2 box filtering of RGBA8 levels, and block-compressed chains whose level 0 is the
/// dat's blocks unchanged and whose lower levels are box-filtered and re-encoded.
/// </summary>
/// <remarks>
/// <para><b>Why re-encode.</b> Retail fills a DXT texture's mip levels with <c>D3DXFilterTexture(..., D3DX_FILTER_BOX)</c>
/// (<c>ImgTex::CreateD3DTexture</c>; docs/textures.md §7), which decompresses each level, box-filters it and compresses
/// it again. Godot cannot generate mipmaps for a compressed image, so Lifestone does the same on a worker: level 0 stays
/// bit-exact, levels 1+ come from the box-filtered RGBA8 of the level above, encoded with <see cref="BlockEncoder"/>.
/// The encoder is not D3DX's, so lower levels can differ from retail by an encoder's rounding (they are 1/2 to 1/8
/// scale and only seen minified).</para>
/// <para><b>Chain length.</b> Engines want the full chain down to 1x1 (Godot's <c>Image</c> validates the data size).
/// Retail creates at most 4 levels; the shaders clamp sampling to level 3 (<c>ac_max_mip_level</c>), so the levels
/// past 3 exist only to satisfy the engine.</para>
/// <para><b>Box filter.</b> Each texel of level n+1 is the rounded mean of a 2x2 block of level n,
/// <c>(a + b + c + d + 2) / 4</c> per channel (the rounding Godot's own <c>Image.GenerateMipmaps</c> uses); an odd or
/// 1-texel dimension repeats its last row or column.</para>
/// </remarks>
public static class MipChain
{
    /// <summary>Number of levels in a full chain: floor(log2(max(w, h))) + 1.</summary>
    public static int LevelCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width >> 1);
            height = Math.Max(1, height >> 1);
            levels++;
        }

        return levels;
    }

    /// <summary>Bytes of one block-compressed level (whole 4x4 blocks, at least one).</summary>
    public static int BlockLevelSize(int width, int height, BlockFormat format) =>
        ((width + 3) / 4) * ((height + 3) / 4) * (format == BlockFormat.Bc1 ? 8 : 16);

    /// <summary>Box-filters an RGBA8 level (rows top to bottom) to the next one.</summary>
    public static byte[] Downsample(ReadOnlySpan<byte> rgba, int width, int height, out int nextWidth, out int nextHeight)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA8.", nameof(rgba));
        }

        nextWidth = Math.Max(1, width >> 1);
        nextHeight = Math.Max(1, height >> 1);
        var result = new byte[nextWidth * nextHeight * 4];
        for (var y = 0; y < nextHeight; y++)
        {
            var y0 = Math.Min(2 * y, height - 1);
            var y1 = Math.Min((2 * y) + 1, height - 1);
            for (var x = 0; x < nextWidth; x++)
            {
                var x0 = Math.Min(2 * x, width - 1);
                var x1 = Math.Min((2 * x) + 1, width - 1);
                for (var c = 0; c < 4; c++)
                {
                    var sum = rgba[(((y0 * width) + x0) * 4) + c] + rgba[(((y0 * width) + x1) * 4) + c]
                            + rgba[(((y1 * width) + x0) * 4) + c] + rgba[(((y1 * width) + x1) * 4) + c];
                    result[(((y * nextWidth) + x) * 4) + c] = (byte)((sum + 2) >> 2);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The full block-compressed chain: <paramref name="level0Blocks"/> unchanged, then each lower level box-filtered
    /// from <paramref name="level0Rgba"/> (the same texture decoded to RGBA8) and encoded.
    /// </summary>
    /// <param name="level0Blocks">The dat's blocks for level 0 (exactly <see cref="BlockLevelSize"/> bytes).</param>
    /// <param name="level0Rgba">Level 0 as RGBA8.</param>
    /// <param name="width">Level-0 width.</param>
    /// <param name="height">Level-0 height.</param>
    /// <param name="format">Block format.</param>
    public static byte[] BuildBlockChain(ReadOnlySpan<byte> level0Blocks, ReadOnlySpan<byte> level0Rgba, int width, int height, BlockFormat format)
    {
        var chain = new byte[BlockChainSize(width, height, format)];
        BuildBlockChain(level0Blocks, level0Rgba.ToArray(), width, height, format, chain);
        return chain;
    }

    /// <summary>Bytes of a full block-compressed chain (<see cref="LevelCount"/> levels of <see cref="BlockLevelSize"/>).</summary>
    public static int BlockChainSize(int width, int height, BlockFormat format)
    {
        var levels = LevelCount(width, height);
        var total = 0;
        for (var i = 0; i < levels; i++)
        {
            total += BlockLevelSize(width, height, format);
            width = Math.Max(1, width >> 1);
            height = Math.Max(1, height >> 1);
        }

        return total;
    }

    /// <summary>
    /// The buffer-taking form of <see cref="BuildBlockChain(ReadOnlySpan{byte}, ReadOnlySpan{byte}, int, int, BlockFormat)"/>
    /// (same bytes): writes the chain into the first <see cref="BlockChainSize"/> bytes of <paramref name="destination"/>
    /// and box-filters in place in <paramref name="level0RgbaScratch"/>, which holds level 0 as RGBA8 on entry and is
    /// overwritten. Allocates nothing, so the caller can use pooled buffers (wave-3 performance pass).
    /// </summary>
    public static void BuildBlockChain(ReadOnlySpan<byte> level0Blocks, Span<byte> level0RgbaScratch, int width, int height, BlockFormat format, Span<byte> destination)
    {
        var level0Size = BlockLevelSize(width, height, format);
        if (level0Blocks.Length != level0Size)
        {
            throw new ArgumentException($"Expected {level0Size} bytes of {format} blocks for {width}x{height}.", nameof(level0Blocks));
        }

        if (level0RgbaScratch.Length < width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes of RGBA8 for {width}x{height}.", nameof(level0RgbaScratch));
        }

        var total = BlockChainSize(width, height, format);
        if (destination.Length < total)
        {
            throw new ArgumentException($"The chain needs {total} bytes, the destination has {destination.Length}.", nameof(destination));
        }

        level0Blocks.CopyTo(destination);
        var offset = level0Size;
        var levels = LevelCount(width, height);
        int w = width, h = height;
        for (var i = 1; i < levels; i++)
        {
            DownsampleInPlace(level0RgbaScratch, w, h, out w, out h);
            var size = BlockLevelSize(w, h, format);
            BlockEncoder.Encode(level0RgbaScratch[..(w * h * 4)], w, h, format, destination.Slice(offset, size));
            offset += size;
        }
    }

    /// <summary>
    /// <see cref="Downsample"/> in place: the next level is written to the start of <paramref name="rgba"/>. Safe because
    /// output texel (x, y) lands at index y·w'+x, never past the first input texel it reads (2y·w+2x) nor any input a
    /// later texel reads.
    /// </summary>
    public static void DownsampleInPlace(Span<byte> rgba, int width, int height, out int nextWidth, out int nextHeight)
    {
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA8.", nameof(rgba));
        }

        nextWidth = Math.Max(1, width >> 1);
        nextHeight = Math.Max(1, height >> 1);
        for (var y = 0; y < nextHeight; y++)
        {
            var y0 = Math.Min(2 * y, height - 1);
            var y1 = Math.Min((2 * y) + 1, height - 1);
            for (var x = 0; x < nextWidth; x++)
            {
                var x0 = Math.Min(2 * x, width - 1);
                var x1 = Math.Min((2 * x) + 1, width - 1);
                for (var c = 0; c < 4; c++)
                {
                    var sum = rgba[(((y0 * width) + x0) * 4) + c] + rgba[(((y0 * width) + x1) * 4) + c]
                            + rgba[(((y1 * width) + x0) * 4) + c] + rgba[(((y1 * width) + x1) * 4) + c];
                    rgba[(((y * nextWidth) + x) * 4) + c] = (byte)((sum + 2) >> 2);
                }
            }
        }
    }
}
