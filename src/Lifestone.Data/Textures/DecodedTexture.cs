using DatReaderWriter.Enums;

namespace Lifestone.Data.Textures;

/// <summary>Pixel layout of <see cref="DecodedTexture.Data"/>.</summary>
public enum DecodedTextureFormat
{
    /// <summary>4 bytes per texel in the order R, G, B, A; rows top to bottom, no padding.</summary>
    Rgba8,

    /// <summary>1 byte per texel holding alpha (or a terrain blend weight); rows top to bottom.</summary>
    Alpha8,

    /// <summary>Raw BC1 (DXT1) blocks exactly as stored in the dat, 8 bytes per 4x4 block, row-major blocks.</summary>
    Dxt1,

    /// <summary>Raw BC2 (DXT3) blocks exactly as stored in the dat, 16 bytes per 4x4 block.</summary>
    Dxt3,

    /// <summary>Raw BC3 (DXT5) blocks exactly as stored in the dat, 16 bytes per 4x4 block.</summary>
    Dxt5,

    /// <summary>A complete JFIF file (the dat stores whole JPEG files). Decode with any JPEG decoder (Godot can).</summary>
    Jpeg,
}

/// <summary>How <see cref="RenderSurfaceDecoder"/> should lay out its output.</summary>
public enum TextureDecodeMode
{
    /// <summary>
    /// GPU-friendly passthrough where the dat format allows it: DXT blocks and JPEG bytes are returned unchanged,
    /// alpha-only formats as <see cref="DecodedTextureFormat.Alpha8"/>, and everything else expanded to
    /// <see cref="DecodedTextureFormat.Rgba8"/> (Godot has no BGR or palette formats).
    /// </summary>
    Native,

    /// <summary>
    /// CPU expansion of every format to <see cref="DecodedTextureFormat.Rgba8"/>, including DXT1/3/5. JPEG is the one
    /// exception: it is always returned as <see cref="DecodedTextureFormat.Jpeg"/> (we ship no JPEG decoder).
    /// </summary>
    Rgba8,
}

/// <summary>A RenderSurface (0x06) decoded into a layout an engine can upload.</summary>
/// <remarks>Plain data: no engine types, safe to build on worker threads. <see cref="Data"/> is owned by this object.</remarks>
public sealed class DecodedTexture
{
    /// <summary>Width in texels. For JPEG this comes from the JPEG frame header (the dat header says 0).</summary>
    public required int Width { get; init; }

    /// <summary>Height in texels. For JPEG this comes from the JPEG frame header (the dat header says 0).</summary>
    public required int Height { get; init; }

    /// <summary>Layout of <see cref="Data"/>.</summary>
    public required DecodedTextureFormat Format { get; init; }

    /// <summary>Texel data in <see cref="Format"/> (see the enum for exact byte order).</summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// True when at least one texel has alpha below 255 after decoding (DXT1: a punch-through texel; DXT3/DXT5: an
    /// alpha value below 255; Alpha8: any value below 255; JPEG: never). Lets the presentation layer pick an opaque
    /// texture format, and warns it when DXT1 data needs BC1 RGBA (1-bit alpha) semantics.
    /// </summary>
    public required bool HasAlpha { get; init; }

    /// <summary>RenderSurface file id (0x06xxxxxx) this texture was decoded from, or 0.</summary>
    public uint SourceId { get; init; }

    /// <summary>Pixel format stored in the dat.</summary>
    public PixelFormat SourceFormat { get; init; }

    /// <summary>
    /// True when at least one texel's alpha lies in [<paramref name="min"/>, <paramref name="max"/>]. DXT data is
    /// decoded on the CPU for the check; JPEG texels count as alpha 255. With
    /// (<see cref="SurfaceMaterial.ClipMapAlphaTestReference"/>, 254) it tells whether an alpha-tested surface
    /// (<see cref="SurfaceBlendMode.AlphaTestBlend"/>) has texels that pass the test and are then blended, that is,
    /// whether a plain cut-out would differ from retail.
    /// </summary>
    public bool AnyAlphaInRange(byte min, byte max) => TexelAlpha.AnyInRange(Format, Data, Width, Height, min, max);

    /// <inheritdoc/>
    public override string ToString() =>
        $"0x{SourceId:X8} {SourceFormat} -> {Format} {Width}x{Height}{(HasAlpha ? " alpha" : string.Empty)}";
}
