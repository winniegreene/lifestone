using System.Buffers;
using DatReaderWriter.Enums;

namespace Lifestone.Data.Textures;

/// <summary>
/// A RenderSurface decoded into a buffer rented from <see cref="ArrayPool{T}.Shared"/>: the buffer-taking counterpart of
/// <see cref="DecodedTexture"/> (<see cref="TextureLoader.DecodeRenderSurfacePooled"/>). <see cref="Dispose"/> returns the
/// buffer; <see cref="Data"/> must not be used afterwards.
/// </summary>
/// <remarks>
/// <para>Why: decoding allocated one or two large arrays per texture (the record's pixel copy, the RGBA8 expansion),
/// most of them on the large-object heap, and a streaming flight decodes thousands of textures (docs/textures.md §8).
/// With pooled buffers the decode allocates no texel arrays at all.</para>
/// <para>Passthrough layouts (DXT blocks, JPEG files, Alpha8) are a slice of the pooled record buffer, so
/// <see cref="Data"/> may start at an offset inside <see cref="Buffer"/>. Not thread-safe except <see cref="Dispose"/>,
/// which may be called more than once from any thread.</para>
/// </remarks>
public sealed class PooledTexture : IDisposable
{
    private byte[]? _buffer;

    internal PooledTexture(byte[] buffer, int offset, int length)
    {
        _buffer = buffer;
        Offset = offset;
        Length = length;
    }

    /// <summary>Width in texels (JPEG: from its frame header).</summary>
    public required int Width { get; init; }

    /// <summary>Height in texels (JPEG: from its frame header).</summary>
    public required int Height { get; init; }

    /// <summary>Layout of <see cref="Data"/> (as <see cref="DecodedTexture.Format"/>).</summary>
    public required DecodedTextureFormat Format { get; init; }

    /// <summary>True when at least one texel has alpha below 255 (as <see cref="DecodedTexture.HasAlpha"/>).</summary>
    public required bool HasAlpha { get; init; }

    /// <summary>RenderSurface file id (0x06xxxxxx).</summary>
    public uint SourceId { get; init; }

    /// <summary>Pixel format stored in the dat.</summary>
    public PixelFormat SourceFormat { get; init; }

    /// <summary>Start of <see cref="Data"/> inside <see cref="Buffer"/>.</summary>
    public int Offset { get; }

    /// <summary>Bytes of <see cref="Data"/>.</summary>
    public int Length { get; }

    /// <summary>The rented buffer (longer than <see cref="Length"/>).</summary>
    /// <exception cref="ObjectDisposedException">After <see cref="Dispose"/>.</exception>
    public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledTexture));

    /// <summary>The texels, laid out as <see cref="Format"/> says.</summary>
    /// <exception cref="ObjectDisposedException">After <see cref="Dispose"/>.</exception>
    public ReadOnlySpan<byte> Data => Buffer.AsSpan(Offset, Length);

    /// <summary>See <see cref="DecodedTexture.AnyAlphaInRange"/> (DXT is decoded into a pooled scratch buffer).</summary>
    public bool AnyAlphaInRange(byte min, byte max) => TexelAlpha.AnyInRange(Format, Data, Width, Height, min, max);

    /// <summary>A copy with its own exactly-sized array.</summary>
    public DecodedTexture ToDecodedTexture() => new()
    {
        Width = Width, Height = Height, Format = Format, Data = Data.ToArray(), HasAlpha = HasAlpha, SourceId = SourceId,
        SourceFormat = SourceFormat,
    };

    /// <summary>Returns the buffer to the pool (once, from any thread).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _buffer, null) is { } buffer)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"0x{SourceId:X8} {SourceFormat} -> {Format} {Width}x{Height}{(HasAlpha ? " alpha" : string.Empty)} (pooled)";
}

/// <summary>Alpha queries shared by <see cref="DecodedTexture"/> and <see cref="PooledTexture"/>.</summary>
public static class TexelAlpha
{
    /// <summary>True when some texel's alpha lies in [<paramref name="min"/>, <paramref name="max"/>] (JPEG counts as 255).</summary>
    public static bool AnyInRange(DecodedTextureFormat format, ReadOnlySpan<byte> data, int width, int height, byte min, byte max)
    {
        if (min > max)
        {
            return false;
        }

        switch (format)
        {
            case DecodedTextureFormat.Jpeg:
                return max == 255;
            case DecodedTextureFormat.Alpha8:
                return data.IndexOfAnyInRange(min, max) >= 0;
            case DecodedTextureFormat.Rgba8:
                return RgbaAnyInRange(data, min, max);
            default:
                var size = width * height * 4;
                var rgba = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    BlockCompression.DecodeToRgba8(format, data, width, height, rgba.AsSpan(0, size));
                    return RgbaAnyInRange(rgba.AsSpan(0, size), min, max);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rgba);
                }
        }
    }

    /// <summary>True when some R,G,B,A texel's alpha lies in [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static bool RgbaAnyInRange(ReadOnlySpan<byte> rgba, byte min, byte max)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            var a = rgba[i];
            if (a >= min && a <= max)
            {
                return true;
            }
        }

        return false;
    }
}
