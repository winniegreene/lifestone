using System.Buffers;
using System.Buffers.Binary;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;

namespace Lifestone.Data.Textures;

/// <summary>Decodes RenderSurface (0x06) pixel data for every pixel format present in the end-of-retail dats.</summary>
/// <remarks>
/// <para>
/// <b>Formats.</b> Census of <c>client_portal.dat</c> (20,684) and <c>client_highres.dat</c> (2,294):
/// A8R8G8B8, INDEX16, DXT1, R8G8B8, DXT5, A8, CUSTOM_RAW_JPEG, CUSTOM_LSCAPE_R8G8B8, CUSTOM_LSCAPE_ALPHA, P8, DXT3,
/// R5G6B5, A4R4G4B4. X8R8G8B8 is not stored but is handled because it is how retail expands non-clip-map palettes.
/// Any other format throws <see cref="NotSupportedException"/> rather than guessing.
/// </para>
/// <para>
/// <b>Byte order</b> (retail <c>PixelFormatDesc::SetFormat</c> channel masks over little-endian texels; ACE
/// <c>Texture.GetImageColorArray</c>, OpenAC <c>TextureHelpers</c> and the scout <c>acimage.py</c> agree):
/// A8R8G8B8 = B,G,R,A; X8R8G8B8 = B,G,R,x; R8G8B8 = B,G,R; R5G6B5 = u16 r:5 g:6 b:5 (high to low);
/// A4R4G4B4 = u16 a:4 r:4 g:4 b:4; A8 / CUSTOM_LSCAPE_ALPHA = one alpha byte; INDEX16 = u16 palette index;
/// P8 = u8 palette index. CUSTOM_LSCAPE_R8G8B8 is <b>R,G,B</b>: retail gives format 243 the R8G8B8 masks in
/// <c>PixelFormatDesc::SetFormat</c>, but no retail code reads 243 through those masks (it is not a D3D format,
/// <c>RenderSurface::IsD3DFormat</c>, and the only other references are in SetFormat itself). The stored bytes are
/// R,G,B: decoding 0x0600382A (dirt) and 0x06003794 (grass) as B,G,R gives blue soil and teal grass (their byte-0
/// means exceed their byte-2 means by 41 and 55; a dat test pins this), and ACE reads them as R,G,B. ACViewer
/// disagrees: its <c>TextureCache.AddAlpha</c> treats LSCAPE_R8G8B8 like R8G8B8 (B,G,R), which this evidence
/// overrules. Rows are stored top row first (D3D surface order); texture coordinates need no flip.
/// </para>
/// <para>
/// <b>Expansion rules.</b> 5- and 6-bit channels expand by bit replication, 4-bit channels by x17. Alpha-only formats
/// expand to (255, 255, 255, a): D3D alpha-only textures have no colour of their own. No Surface (0x08) references an
/// A8 RenderSurface (they are 86 category-10 atlases), and the terrain blend reads LSCAPE_ALPHA as a raw weight
/// (<c>ImgTex::MergeTexture</c>), so prefer <see cref="TextureDecodeMode.Native"/> (<see cref="DecodedTextureFormat.Alpha8"/>)
/// for those.
/// </para>
/// <para>
/// <b>Palettes</b> (<c>ImgTex::CopyIntoData</c>, <c>ImgTex::Combine</c>): each INDEX16/P8 index selects the palette's
/// 32-bit ARGB entry (<c>Palette::get_color32</c>, P8 indexes the first 256 of the 2,048 entries). For a Base1ClipMap
/// surface, retail writes 0x00000000 (transparent black) for every index below 8 and builds an A8R8G8B8 texture;
/// otherwise it builds an X8R8G8B8 texture, so the palette's alpha is ignored and the texel is opaque. Every palette
/// colour in the end-of-retail portal has alpha 255, so in practice indices &lt; 8 on clip maps are the only
/// transparency. (ACViewer <c>TextureCache.IndexToColor</c> and OpenAC <c>TextureHelpers.FillIndex16</c> implement
/// the same index &lt; 8 rule.)
/// </para>
/// </remarks>
public static class RenderSurfaceDecoder
{
    /// <summary>Number of clip-map indices that become transparent (<c>ImgTex::CopyIntoData</c>: <c>index &lt; 8</c>).</summary>
    public const int ClipMapTransparentIndexCount = 8;

    /// <summary>True for formats this decoder handles.</summary>
    public static bool IsSupported(PixelFormat format) => BitsPerPixel(format) is not null || format == PixelFormat.PFID_CUSTOM_RAW_JPEG;

    /// <summary>True for palette-indexed formats (INDEX16, P8), which need a palette to decode.</summary>
    public static bool IsIndexed(PixelFormat format) => format is PixelFormat.PFID_INDEX16 or PixelFormat.PFID_P8;

    /// <summary>
    /// Bits per pixel from retail <c>PixelFormatDesc::SetFormat</c> for the supported formats; null for JPEG and
    /// unsupported formats.
    /// </summary>
    public static int? BitsPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.PFID_A8R8G8B8 or PixelFormat.PFID_X8R8G8B8 => 32,
        PixelFormat.PFID_R8G8B8 or PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8 => 24,
        PixelFormat.PFID_R5G6B5 or PixelFormat.PFID_A4R4G4B4 or PixelFormat.PFID_INDEX16 => 16,
        PixelFormat.PFID_A8 or PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA or PixelFormat.PFID_P8 => 8,
        PixelFormat.PFID_DXT1 => 4,
        PixelFormat.PFID_DXT3 or PixelFormat.PFID_DXT5 => 8,
        _ => null,
    };

    /// <summary>
    /// Pixel byte count retail requires (<c>RenderSurface::Serialize</c>: <c>imageSize == w * h * bpp &gt;&gt; 3</c>
    /// unless the format is variable-size); null for JPEG, whose size is the file size.
    /// </summary>
    public static long? ExpectedSourceLength(PixelFormat format, int width, int height) =>
        BitsPerPixel(format) is { } bpp ? ((long)width * height * bpp) >> 3 : null;

    /// <summary>Decodes a RenderSurface record. The result never aliases <paramref name="surface"/>'s buffers.</summary>
    /// <param name="surface">The record (from portal or highres).</param>
    /// <param name="mode">Passthrough or full RGBA8 expansion.</param>
    /// <param name="palette">2,048 ARGB entries (0xAARRGGBB) for INDEX16/P8; ignored otherwise.</param>
    /// <param name="clipMap">The using Surface has Base1ClipMap; only affects INDEX16/P8.</param>
    public static DecodedTexture Decode(RenderSurface surface, TextureDecodeMode mode, ReadOnlySpan<uint> palette = default, bool clipMap = false)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return Decode(surface.Id, surface.Format, surface.Width, surface.Height, surface.SourceData, mode, palette, clipMap);
    }

    /// <summary>Decodes raw RenderSurface pixel data; see <see cref="Decode(RenderSurface, TextureDecodeMode, ReadOnlySpan{uint}, bool)"/>.</summary>
    public static DecodedTexture Decode(
        uint id, PixelFormat format, int width, int height, ReadOnlySpan<byte> source, TextureDecodeMode mode,
        ReadOnlySpan<uint> palette = default, bool clipMap = false) =>
        DecodeCore(id, format, width, height, source, mode, palette, clipMap);

    /// <summary>
    /// The buffer-taking decode (<see cref="TextureLoader.DecodeRenderSurfacePooled"/>): the pixel bytes are
    /// <paramref name="length"/> bytes at <paramref name="offset"/> of <paramref name="record"/>, a buffer rented from
    /// <see cref="ArrayPool{T}.Shared"/> whose ownership passes to this call. Passthrough layouts (DXT, JPEG, Alpha8) are
    /// returned as a slice of it; an RGBA8 expansion goes to a buffer of its own, and the record buffer is returned to
    /// the pool. If decoding throws, the record buffer is returned before the exception propagates.
    /// </summary>
    internal static PooledTexture DecodePooled(
        uint id, PixelFormat format, int width, int height, byte[] record, int offset, int length, TextureDecodeMode mode,
        ReadOnlySpan<uint> palette, bool clipMap)
    {
        byte[]? owned = record;
        byte[]? rgba = null;
        try
        {
            var source = record.AsSpan(offset, length);
            var layout = Plan(id, format, ref width, ref height, source, mode);
            if (layout != DecodedTextureFormat.Rgba8)
            {
                var passthrough = new PooledTexture(record, offset, length)
                {
                    Width = width, Height = height, Format = layout, HasAlpha = PassthroughHasAlpha(layout, source, width, height),
                    SourceId = id, SourceFormat = format,
                };
                owned = null;
                return passthrough;
            }

            var size = width * height * 4;
            rgba = ArrayPool<byte>.Shared.Rent(size);
            var hasAlpha = ExpandToRgba8(id, format, width, height, source, rgba.AsSpan(0, size), palette, clipMap);
            var expanded = new PooledTexture(rgba, 0, size)
            {
                Width = width, Height = height, Format = DecodedTextureFormat.Rgba8, HasAlpha = hasAlpha, SourceId = id, SourceFormat = format,
            };
            rgba = null;
            return expanded;
        }
        finally
        {
            if (owned is not null)
            {
                ArrayPool<byte>.Shared.Return(owned);
            }

            if (rgba is not null)
            {
                ArrayPool<byte>.Shared.Return(rgba);
            }
        }
    }

    private static DecodedTexture DecodeCore(
        uint id, PixelFormat format, int width, int height, ReadOnlySpan<byte> source, TextureDecodeMode mode,
        ReadOnlySpan<uint> palette, bool clipMap)
    {
        var layout = Plan(id, format, ref width, ref height, source, mode);
        if (layout != DecodedTextureFormat.Rgba8)
        {
            return new DecodedTexture
            {
                Width = width, Height = height, Format = layout, Data = source.ToArray(),
                HasAlpha = PassthroughHasAlpha(layout, source, width, height), SourceId = id, SourceFormat = format,
            };
        }

        var rgba = new byte[width * height * 4];
        var hasAlpha = ExpandToRgba8(id, format, width, height, source, rgba, palette, clipMap);
        return new DecodedTexture
        {
            Width = width, Height = height, Format = DecodedTextureFormat.Rgba8, Data = rgba, HasAlpha = hasAlpha,
            SourceId = id, SourceFormat = format,
        };
    }

    /// <summary>
    /// Validates the record and picks the output layout: a passthrough (<see cref="DecodedTextureFormat.Jpeg"/>, DXT,
    /// <see cref="DecodedTextureFormat.Alpha8"/>) or <see cref="DecodedTextureFormat.Rgba8"/>. JPEG takes its size from
    /// its frame header (the dat header says 0).
    /// </summary>
    private static DecodedTextureFormat Plan(uint id, PixelFormat format, ref int width, ref int height, ReadOnlySpan<byte> source, TextureDecodeMode mode)
    {
        if (format == PixelFormat.PFID_CUSTOM_RAW_JPEG)
        {
            var header = ReadJpegHeader(id, source);
            width = header.Width;
            height = header.Height;
            return DecodedTextureFormat.Jpeg;
        }

        var expected = ExpectedSourceLength(format, width, height)
            ?? throw new NotSupportedException($"RenderSurface 0x{id:X8}: pixel format {format} (0x{(uint)format:X}) is not supported.");
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: invalid size {width}x{height} for {format}.");
        }

        if (source.Length != expected)
        {
            throw new InvalidDataException(
                $"RenderSurface 0x{id:X8}: {format} {width}x{height} needs {expected} bytes, record has {source.Length}.");
        }

        if (mode != TextureDecodeMode.Native)
        {
            return DecodedTextureFormat.Rgba8;
        }

        var native = format switch
        {
            PixelFormat.PFID_DXT1 => DecodedTextureFormat.Dxt1,
            PixelFormat.PFID_DXT3 => DecodedTextureFormat.Dxt3,
            PixelFormat.PFID_DXT5 => DecodedTextureFormat.Dxt5,
            PixelFormat.PFID_A8 or PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA => DecodedTextureFormat.Alpha8,
            _ => DecodedTextureFormat.Rgba8,
        };
        if (native is DecodedTextureFormat.Dxt1 or DecodedTextureFormat.Dxt3 or DecodedTextureFormat.Dxt5
            && BlockCompression.ByteLength(native, width, height) != source.Length)
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: {format} {width}x{height} is not a whole number of 4x4 blocks.");
        }

        return native;
    }

    /// <summary><see cref="DecodedTexture.HasAlpha"/> of a passthrough layout.</summary>
    private static bool PassthroughHasAlpha(DecodedTextureFormat layout, ReadOnlySpan<byte> source, int width, int height) => layout switch
    {
        DecodedTextureFormat.Jpeg => false,
        DecodedTextureFormat.Alpha8 => source.IndexOfAnyExcept((byte)255) >= 0,
        _ => BlockCompression.HasAlpha(layout, source, width, height),
    };

    /// <summary>
    /// Expands any supported non-JPEG format to R,G,B,A bytes. Returns true when any output alpha is below 255.
    /// </summary>
    public static bool ExpandToRgba8(
        uint id, PixelFormat format, int width, int height, ReadOnlySpan<byte> source, Span<byte> rgba,
        ReadOnlySpan<uint> palette = default, bool clipMap = false)
    {
        var n = width * height;
        if (rgba.Length < n * 4)
        {
            throw new ArgumentException($"Destination holds {rgba.Length} bytes, need {n * 4}.", nameof(rgba));
        }

        var expected = ExpectedSourceLength(format, width, height);
        if (expected is null || source.Length < expected)
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: cannot expand {format} {width}x{height} from {source.Length} bytes.");
        }

        switch (format)
        {
            case PixelFormat.PFID_A8R8G8B8:
            {
                byte minAlpha = 255;
                for (var i = 0; i < n; i++)
                {
                    var s = i * 4;
                    rgba[s] = source[s + 2];
                    rgba[s + 1] = source[s + 1];
                    rgba[s + 2] = source[s];
                    var a = source[s + 3];
                    rgba[s + 3] = a;
                    minAlpha = Math.Min(minAlpha, a);
                }

                return minAlpha != 255;
            }

            case PixelFormat.PFID_X8R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    var s = i * 4;
                    rgba[s] = source[s + 2];
                    rgba[s + 1] = source[s + 1];
                    rgba[s + 2] = source[s];
                    rgba[s + 3] = 255;
                }

                return false;

            case PixelFormat.PFID_R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    var s = i * 3;
                    var d = i * 4;
                    rgba[d] = source[s + 2];
                    rgba[d + 1] = source[s + 1];
                    rgba[d + 2] = source[s];
                    rgba[d + 3] = 255;
                }

                return false;

            case PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8:
                for (var i = 0; i < n; i++)
                {
                    var s = i * 3;
                    var d = i * 4;
                    rgba[d] = source[s];
                    rgba[d + 1] = source[s + 1];
                    rgba[d + 2] = source[s + 2];
                    rgba[d + 3] = 255;
                }

                return false;

            case PixelFormat.PFID_R5G6B5:
                for (var i = 0; i < n; i++)
                {
                    BlockCompression.Expand565(BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..]), out var r, out var g, out var b);
                    var d = i * 4;
                    rgba[d] = r;
                    rgba[d + 1] = g;
                    rgba[d + 2] = b;
                    rgba[d + 3] = 255;
                }

                return false;

            case PixelFormat.PFID_A4R4G4B4:
            {
                var anyAlpha = false;
                for (var i = 0; i < n; i++)
                {
                    var c = BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..]);
                    var d = i * 4;
                    rgba[d] = (byte)(((c >> 8) & 0xF) * 17);
                    rgba[d + 1] = (byte)(((c >> 4) & 0xF) * 17);
                    rgba[d + 2] = (byte)((c & 0xF) * 17);
                    var a = (byte)(((c >> 12) & 0xF) * 17);
                    rgba[d + 3] = a;
                    anyAlpha |= a != 255;
                }

                return anyAlpha;
            }

            case PixelFormat.PFID_A8:
            case PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
            {
                var anyAlpha = false;
                for (var i = 0; i < n; i++)
                {
                    var d = i * 4;
                    rgba[d] = 255;
                    rgba[d + 1] = 255;
                    rgba[d + 2] = 255;
                    var a = source[i];
                    rgba[d + 3] = a;
                    anyAlpha |= a != 255;
                }

                return anyAlpha;
            }

            case PixelFormat.PFID_INDEX16:
            case PixelFormat.PFID_P8:
                return ExpandIndexed(id, format == PixelFormat.PFID_INDEX16, n, source, rgba, palette, clipMap);

            case PixelFormat.PFID_DXT1:
            case PixelFormat.PFID_DXT3:
            case PixelFormat.PFID_DXT5:
            {
                var bc = format switch
                {
                    PixelFormat.PFID_DXT1 => DecodedTextureFormat.Dxt1,
                    PixelFormat.PFID_DXT3 => DecodedTextureFormat.Dxt3,
                    _ => DecodedTextureFormat.Dxt5,
                };
                BlockCompression.DecodeToRgba8(bc, source, width, height, rgba);
                for (var i = 3; i < n * 4; i += 4)
                {
                    if (rgba[i] != 255)
                    {
                        return true;
                    }
                }

                return false;
            }

            default:
                throw new NotSupportedException($"RenderSurface 0x{id:X8}: pixel format {format} (0x{(uint)format:X}) is not supported.");
        }
    }

    private static bool ExpandIndexed(uint id, bool index16, int n, ReadOnlySpan<byte> source, Span<byte> rgba, ReadOnlySpan<uint> palette, bool clipMap)
    {
        if (palette.IsEmpty)
        {
            throw new ArgumentException($"RenderSurface 0x{id:X8} is palette-indexed; a palette is required.", nameof(palette));
        }

        var anyAlpha = false;
        for (var i = 0; i < n; i++)
        {
            int index = index16 ? BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..]) : source[i];
            var d = i * 4;
            if (clipMap && index < ClipMapTransparentIndexCount)
            {
                // ImgTex::CopyIntoData writes a zero DWORD: transparent black.
                rgba[d] = 0;
                rgba[d + 1] = 0;
                rgba[d + 2] = 0;
                rgba[d + 3] = 0;
                anyAlpha = true;
                continue;
            }

            if ((uint)index >= (uint)palette.Length)
            {
                throw new InvalidDataException(
                    $"RenderSurface 0x{id:X8}: palette index {index} is outside the {palette.Length}-entry palette.");
            }

            var c = palette[index];
            rgba[d] = (byte)(c >> 16);
            rgba[d + 1] = (byte)(c >> 8);
            rgba[d + 2] = (byte)c;
            if (clipMap)
            {
                // ImgTex::Combine builds an A8R8G8B8 texture for clip maps: the palette alpha is kept.
                var a = (byte)(c >> 24);
                rgba[d + 3] = a;
                anyAlpha |= a != 255;
            }
            else
            {
                // ...and an X8R8G8B8 texture otherwise: alpha is not stored, the texel is opaque.
                rgba[d + 3] = 255;
            }
        }

        return anyAlpha;
    }

    private static JpegHeader ReadJpegHeader(uint id, ReadOnlySpan<byte> source)
    {
        if (!JpegHeader.TryRead(source, out var header))
        {
            throw new InvalidDataException($"RenderSurface 0x{id:X8}: CUSTOM_RAW_JPEG payload has no readable JPEG frame header.");
        }

        if (header.Components != 3)
        {
            // RenderSurface::CreateFromSourceData fails unless JPGChannels == 3.
            throw new InvalidDataException(
                $"RenderSurface 0x{id:X8}: JPEG has {header.Components} components; retail only accepts 3.");
        }

        return header;
    }
}
