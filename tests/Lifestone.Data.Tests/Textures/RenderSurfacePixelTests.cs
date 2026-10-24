using System.Buffers.Binary;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// Pixel-exact checks of every pixel format on real records against the research scout's Python decoder
/// (scratchpad <c>research/scout/acimage.py</c>), both as CRC-32s of the Python output computed by running it on the
/// user's dats (scratchpad <c>textures/crc_samples.py</c>) and as a full-image comparison with the C# transcription
/// <see cref="ScoutReferenceDecoder"/>. Samples: the first and last record of every format in each dat, plus the
/// records local-scout.md checked visually.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class RenderSurfacePixelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    /// <summary>(RenderSurface id, format, CRC-32 of the Python RGBA output, or of the raw bytes for JPEG).</summary>
    public static TheoryData<uint, string, uint> PortalSamples => new()
    {
        { 0x06006430u, "A4R4G4B4", 0x8CD82C94u },
        { 0x06007068u, "A4R4G4B4", 0x409DD5DDu },
        { 0x06005ECEu, "A8", 0xA48BBC32u },
        { 0x06005FE2u, "A8", 0x9CF1A578u },
        { 0x06000FAAu, "A8R8G8B8", 0x36F076CCu },
        { 0x06007571u, "A8R8G8B8", 0x74A87BC4u },
        { 0x060037D8u, "CUSTOM_LSCAPE_ALPHA", 0xEEED1F6Fu },
        { 0x06006D6Du, "CUSTOM_LSCAPE_ALPHA", 0xE3841BB1u },
        { 0x06003794u, "CUSTOM_LSCAPE_R8G8B8", 0x6C68579Cu },
        { 0x06006286u, "CUSTOM_LSCAPE_R8G8B8", 0x4D0DF5DDu },
        { 0x0600382Au, "CUSTOM_LSCAPE_R8G8B8", 0x4EF340F0u },
        { 0x06004D26u, "CUSTOM_RAW_JPEG", 0x399B7781u },
        { 0x06007576u, "CUSTOM_RAW_JPEG", 0xA78E513Cu },
        { 0x0600378Bu, "DXT1", 0x13F72C0Au },
        { 0x06007575u, "DXT1", 0xB349724Cu },
        { 0x06004304u, "DXT1", 0x87E73683u },
        { 0x06006992u, "DXT3", 0xF181E053u },
        { 0x06006E82u, "DXT3", 0xF8B727E9u },
        { 0x06003789u, "DXT5", 0x36C8B6AAu },
        { 0x06007533u, "DXT5", 0x5AF292C2u },
        { 0x0600378Eu, "INDEX16", 0x0998F3D2u },
        { 0x06007529u, "INDEX16", 0xE59CF22Au },
        { 0x06005747u, "INDEX16", 0xB9D2BEC5u },
        { 0x06005CD8u, "P8", 0xDE9BABA7u },
        { 0x06005CE4u, "P8", 0x7812D958u },
        { 0x06006402u, "R5G6B5", 0x6E3437FAu },
        { 0x0600641Du, "R5G6B5", 0x6E3437FAu },
        { 0x06000133u, "R8G8B8", 0xFCBFF073u },
        { 0x0600756Fu, "R8G8B8", 0x076ABB80u },
    };

    /// <summary>Same as <see cref="PortalSamples"/> for client_highres.dat.</summary>
    public static TheoryData<uint, string, uint> HighResSamples => new()
    {
        { 0x06003CEEu, "A8R8G8B8", 0x7B2ACFFDu },
        { 0x060064EEu, "A8R8G8B8", 0xBD0ADB6Du },
        { 0x060073A7u, "CUSTOM_LSCAPE_ALPHA", 0xA5093DF4u },
        { 0x060073AEu, "CUSTOM_LSCAPE_ALPHA", 0xF0930E33u },
        { 0x0600389Bu, "DXT1", 0x56A0619Bu },
        { 0x060072D5u, "DXT1", 0x6C7157F5u },
        { 0x06003E9Bu, "DXT5", 0x8BDEC030u },
        { 0x06003797u, "INDEX16", 0x3F8A09CFu },
        { 0x060061B0u, "INDEX16", 0xA06659D1u },
        { 0x0600649Cu, "R8G8B8", 0x83465535u },
        { 0x06006E26u, "R8G8B8", 0x0B22595Bu },
    };

    [RequiresDatsTheory]
    [MemberData(nameof(PortalSamples))]
    public void Portal_sample_matches_scout_reference(uint id, string format, uint pythonCrc) =>
        CheckAgainstScout(id, format, pythonCrc, highRes: false);

    [RequiresHighResDatsTheory]
    [MemberData(nameof(HighResSamples))]
    public void HighRes_sample_matches_scout_reference(uint id, string format, uint pythonCrc) =>
        CheckAgainstScout(id, format, pythonCrc, highRes: true);

    /// <summary>
    /// CUSTOM_LSCAPE_R8G8B8 byte order, checked independently of the scout (which shares the R,G,B assumption): retail's
    /// <c>PixelFormatDesc::SetFormat</c> masks and ACViewer's <c>AddAlpha</c> would make these B,G,R, i.e. blue soil
    /// and teal grass. In both records stored byte 0 averages more than 30 above byte 2 (reviewer's byte 0/1/2 means:
    /// dirt 84.9/66.7/43.7, grass 71.6/72.0/16.5). Natural soil and grass carry more red than blue, so byte 0 must
    /// decode to red.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0x0600382Au)] // dirt
    [InlineData(0x06003794u)] // grass
    public void Landscape_rgb_decodes_byte_0_as_red(uint id)
    {
        var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, id)!;
        Assert.Equal(PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8, rs.Format);
        var n = rs.Width * rs.Height;
        double byte0 = 0, byte2 = 0;
        for (var i = 0; i < n; i++)
        {
            byte0 += rs.SourceData[i * 3];
            byte2 += rs.SourceData[(i * 3) + 2];
        }

        Assert.True((byte0 - byte2) / n > 30, $"0x{id:X8}: stored byte 0 should be the warm channel");

        var tex = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8);
        double red = 0, green = 0, blue = 0;
        for (var i = 0; i < n; i++)
        {
            red += tex.Data[i * 4];
            green += tex.Data[(i * 4) + 1];
            blue += tex.Data[(i * 4) + 2];
        }

        red /= n;
        green /= n;
        blue /= n;
        Assert.True(red - blue > 30, $"0x{id:X8}: mean R {red:F1} B {blue:F1}; soil and grass are not blue or teal");
        Assert.True(green - blue > 20, $"0x{id:X8}: mean G {green:F1} B {blue:F1}");
    }

    [RequiresDatsFact]
    public void Raw_jpeg_dimensions_come_from_the_frame_header()
    {
        // Python SOF scan (scratchpad textures/jpeg_dims.py): both are baseline 800x600 with 3 components.
        foreach (var id in new[] { 0x06004D26u, 0x06007576u })
        {
            var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, id)!;
            var tex = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8);
            Assert.Equal((DecodedTextureFormat.Jpeg, 800, 600, false), (tex.Format, tex.Width, tex.Height, tex.HasAlpha));
            Assert.True(JpegHeader.TryRead(tex.Data, out var h));
            Assert.Equal((byte)0xC0, h.Marker);
        }
    }

    [RequiresDatsFact]
    public void Clip_map_index16_makes_indices_below_8_transparent_black()
    {
        // Surface 0x0800000A (Base1ClipMap) -> RenderSurface 0x06003792, palette 0x04000F3F; 2,649 of its 64x64
        // indices are below 8 (scratchpad textures/surf_samples.py). Rule: ImgTex::CopyIntoData.
        var loader = new TextureLoader(Dats);
        var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, 0x06003792u)!;
        Assert.Equal(PixelFormat.PFID_INDEX16, rs.Format);
        var palette = TextureTestSupport.Palette(loader, rs);
        Assert.Equal(0x04000F3Fu, palette.Id);

        var clip = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8, palette.Argb, clipMap: true);
        var plain = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8, palette.Argb, clipMap: false);
        Assert.True(clip.HasAlpha);
        Assert.False(plain.HasAlpha);

        var transparent = 0;
        for (var i = 0; i < rs.Width * rs.Height; i++)
        {
            var index = BinaryPrimitives.ReadUInt16LittleEndian(rs.SourceData.AsSpan(i * 2));
            var c = palette[index];
            var expected = index < 8 ? new byte[] { 0, 0, 0, 0 } : [(byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24)];
            Assert.Equal(expected, clip.Data.AsSpan(i * 4, 4).ToArray());
            Assert.Equal(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)255 }, plain.Data.AsSpan(i * 4, 4).ToArray());
            transparent += index < 8 ? 1 : 0;
        }

        Assert.Equal(2649, transparent);
    }

    [RequiresDatsFact]
    public void Loader_decodes_a_clip_map_surface_with_its_texture_default_palette()
    {
        var loader = new TextureLoader(Dats);
        var decoded = loader.DecodeSurface(0x0800000Au, TextureDecodeMode.Rgba8);
        Assert.NotNull(decoded);
        Assert.True(decoded.Material.IsClipMap);
        Assert.Equal(0x06003792u, decoded.Resolution!.RenderSurfaceId);
        Assert.Equal(0x04000F3Fu, decoded.Palette!.Id);
        Assert.Equal(0u, decoded.Material.OriginalPaletteId);
        Assert.True(decoded.Texture!.HasAlpha);
        Assert.Equal(2649, Enumerable.Range(0, 64 * 64).Count(i => decoded.Texture.Data[(i * 4) + 3] == 0));
    }

    private void CheckAgainstScout(uint id, string format, uint pythonCrc, bool highRes)
    {
        var loader = new TextureLoader(Dats);
        var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, id, highRes);
        Assert.NotNull(rs);
        Assert.Equal(format, rs.Format.ToString()["PFID_".Length..]);

        uint[]? palette = null;
        if (RenderSurfaceDecoder.IsIndexed(rs.Format))
        {
            palette = TextureTestSupport.Palette(loader, rs).Argb.ToArray();
        }

        var tex = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8, palette);
        if (rs.Format == PixelFormat.PFID_CUSTOM_RAW_JPEG)
        {
            Assert.Equal(DecodedTextureFormat.Jpeg, tex.Format);
            Assert.Equal(pythonCrc, Crc32(tex.Data));
            return;
        }

        // The scout writes alpha-only formats as opaque grey; production keeps the byte as alpha over white.
        var comparable = tex.Data;
        if (rs.Format is PixelFormat.PFID_A8 or PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA)
        {
            comparable = new byte[tex.Data.Length];
            for (var i = 0; i < tex.Data.Length; i += 4)
            {
                Assert.Equal(255, tex.Data[i] & tex.Data[i + 1] & tex.Data[i + 2]);
                comparable[i] = comparable[i + 1] = comparable[i + 2] = tex.Data[i + 3];
                comparable[i + 3] = 255;
            }
        }

        var reference = ScoutReferenceDecoder.Decode(rs.Format, rs.Width, rs.Height, rs.SourceData, palette);
        Assert.Equal(pythonCrc, Crc32(reference)); // the C# transcription reproduces the Python output
        Assert.Equal(pythonCrc, Crc32(comparable));
        Assert.True(reference.AsSpan().SequenceEqual(comparable), $"0x{id:X8}: pixels differ from the scout reference");

        // Native output: DXT and alpha-only formats pass through, the CPU expansion of those matches Rgba8 mode.
        var native = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Native, palette);
        if (native.Format is DecodedTextureFormat.Dxt1 or DecodedTextureFormat.Dxt3 or DecodedTextureFormat.Dxt5)
        {
            var expanded = new byte[rs.Width * rs.Height * 4];
            BlockCompression.DecodeToRgba8(native.Format, native.Data, rs.Width, rs.Height, expanded);
            Assert.Equal(tex.Data, expanded);
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        // Standard reflected CRC-32 (polynomial 0xEDB88320), as Python's zlib.crc32.
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
