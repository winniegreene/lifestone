namespace Lifestone.Data.Terrain.Blending;

/// <summary>
/// Box-filtered mip chains for the terrain texture arrays: square, power-of-two images, 1 or 4 bytes per texel, laid out
/// level 0 first with no padding (the layout Godot's <c>Image.CreateFromData(..., useMipmaps: true, ...)</c> expects).
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>ImgTex::CreateD3DTexture</c> (acclient.c:366008) gives every texture
/// <c>min(4, log2(max side) + 1)</c> mip levels and fills them with <c>D3DXFilterTexture(..., D3DX_FILTER_BOX |
/// D3DX_FILTER_MIRROR)</c> (filter word 0x70005). The decompiled <c>CreateTexture</c> call is garbled, so "at most 4
/// levels" is very likely but not proven (docs/textures.md §7). Retail mip-maps the <i>merged</i> 1024² cell texture
/// (<c>TexMerge::MakeNewSurface</c> → <c>ImgTex::CreateLScapeTexture</c>), not the source layers.
/// </para>
/// <para>
/// <b>Lifestone.</b> A Godot <c>Image</c> cannot hold a partial chain, so every layer gets the full chain down to 1×1 and
/// the terrain shader clamps the level it samples (<c>ac_max_mip_level</c>, 3 by default) so that it never minifies past
/// the density of retail's level 3 (docs/terrain-renderer.md, "Mip policy"). Each level averages 2×2 texels of the level
/// above per channel and rounds half up: <c>(a + b + c + d + 2) &gt;&gt; 2</c>. D3DX does not specify its rounding; this
/// is the rounding Godot's own <c>Image.generate_mipmaps</c> uses for 8-bit formats.
/// </para>
/// </remarks>
public static class TerrainMipChain
{
    /// <summary>Retail's cap on mip levels (<c>ImgTex::CreateD3DTexture</c>: <c>if (levels &gt; 4) levels = 4</c>).</summary>
    public const int RetailMaxLevels = 4;

    /// <summary>Levels in a full chain for a square power-of-two size: log2(size) + 1.</summary>
    public static int LevelCount(int size)
    {
        RequirePowerOfTwo(size);
        return System.Numerics.BitOperations.Log2((uint)size) + 1;
    }

    /// <summary>Levels retail would create for a texture of this size: <c>min(4, log2(size) + 1)</c>.</summary>
    public static int RetailLevelCount(int size) => Math.Min(RetailMaxLevels, LevelCount(size));

    /// <summary>Edge length of level <paramref name="level"/>.</summary>
    public static int LevelSize(int size, int level) => Math.Max(1, size >> level);

    /// <summary>Byte offset of level <paramref name="level"/> inside a chain.</summary>
    public static int LevelOffset(int size, int bytesPerTexel, int level)
    {
        var offset = 0;
        for (var i = 0; i < level; i++)
        {
            var s = LevelSize(size, i);
            offset += s * s * bytesPerTexel;
        }

        return offset;
    }

    /// <summary>Total bytes of a full chain.</summary>
    public static int ByteLength(int size, int bytesPerTexel) => LevelOffset(size, bytesPerTexel, LevelCount(size));

    /// <summary>Builds the full chain from level 0 (copied into the result).</summary>
    /// <param name="level0">size × size texels, row-major, top row first.</param>
    /// <param name="size">Edge length (a power of two).</param>
    /// <param name="bytesPerTexel">1 (alpha maps) or 4 (RGBA8 colour).</param>
    public static byte[] Build(ReadOnlySpan<byte> level0, int size, int bytesPerTexel)
    {
        RequirePowerOfTwo(size);
        if (bytesPerTexel is not (1 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerTexel), bytesPerTexel, "Only 1 or 4 bytes per texel are supported.");
        }

        if (level0.Length != size * size * bytesPerTexel)
        {
            throw new ArgumentException($"Level 0 of a {size}x{size} image needs {size * size * bytesPerTexel} bytes, got {level0.Length}.", nameof(level0));
        }

        var chain = new byte[ByteLength(size, bytesPerTexel)];
        level0.CopyTo(chain);
        var levels = LevelCount(size);
        var source = 0;
        for (var level = 1; level < levels; level++)
        {
            var srcSize = LevelSize(size, level - 1);
            var dstSize = LevelSize(size, level);
            var target = source + (srcSize * srcSize * bytesPerTexel);
            Downsample(chain.AsSpan(source, srcSize * srcSize * bytesPerTexel), srcSize, chain.AsSpan(target, dstSize * dstSize * bytesPerTexel), bytesPerTexel);
            source = target;
        }

        return chain;
    }

    /// <summary>One 2×2 box-filter step from a srcSize² level to the next.</summary>
    private static void Downsample(ReadOnlySpan<byte> src, int srcSize, Span<byte> dst, int bytesPerTexel)
    {
        var dstSize = srcSize / 2;
        var srcRow = srcSize * bytesPerTexel;
        for (var y = 0; y < dstSize; y++)
        {
            var top = 2 * y * srcRow;
            var bottom = top + srcRow;
            var outRow = y * dstSize * bytesPerTexel;
            for (var x = 0; x < dstSize; x++)
            {
                var left = 2 * x * bytesPerTexel;
                var right = left + bytesPerTexel;
                for (var c = 0; c < bytesPerTexel; c++)
                {
                    var sum = src[top + left + c] + src[top + right + c] + src[bottom + left + c] + src[bottom + right + c];
                    dst[outRow + (x * bytesPerTexel) + c] = (byte)((sum + 2) >> 2);
                }
            }
        }
    }

    private static void RequirePowerOfTwo(int size)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new NotSupportedException($"Terrain textures must be square powers of two; got {size}.");
        }
    }
}
