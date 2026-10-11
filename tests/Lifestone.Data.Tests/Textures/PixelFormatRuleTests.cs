using DatReaderWriter.Enums;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// Byte-order and edge-case rules on hand-built inputs. These pin the documented rules; conformance on real records is
/// in <see cref="RenderSurfacePixelTests"/> and <see cref="RenderSurfaceSweepTests"/>.
/// </summary>
public sealed class PixelFormatRuleTests
{
    private static byte[] Rgba(PixelFormat format, byte[] source, int width = 1, int height = 1, uint[]? palette = null, bool clipMap = false) =>
        RenderSurfaceDecoder.Decode(1, format, width, height, source, TextureDecodeMode.Rgba8, palette, clipMap).Data;

    [Fact]
    public void Byte_orders_follow_retail_channel_masks()
    {
        Assert.Equal(new byte[] { 3, 2, 1, 4 }, Rgba(PixelFormat.PFID_A8R8G8B8, [1, 2, 3, 4]));          // B,G,R,A
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, Rgba(PixelFormat.PFID_X8R8G8B8, [1, 2, 3, 4]));        // B,G,R,x
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, Rgba(PixelFormat.PFID_R8G8B8, [1, 2, 3]));             // B,G,R
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, Rgba(PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8, [1, 2, 3])); // R,G,B
        Assert.Equal(new byte[] { 255, 255, 255, 7 }, Rgba(PixelFormat.PFID_A8, [7]));
        Assert.Equal(new byte[] { 255, 255, 255, 9 }, Rgba(PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA, [9]));
    }

    [Fact]
    public void Sixteen_bit_formats_expand_by_bit_replication_and_times_17()
    {
        // R5G6B5 0xF81F = r 31, g 0, b 31; 0x0841 = r 1, g 2, b 1.
        Assert.Equal(new byte[] { 255, 0, 255, 255 }, Rgba(PixelFormat.PFID_R5G6B5, [0x1F, 0xF8]));
        Assert.Equal(new byte[] { 8, 8, 8, 255 }, Rgba(PixelFormat.PFID_R5G6B5, [0x41, 0x08]));
        // A4R4G4B4 0x8421: a 8, r 4, g 2, b 1.
        Assert.Equal(new byte[] { 68, 34, 17, 136 }, Rgba(PixelFormat.PFID_A4R4G4B4, [0x21, 0x84]));
    }

    [Fact]
    public void Palettes_keep_alpha_only_for_clip_maps()
    {
        var palette = new uint[2048];
        palette[3] = 0x80102030u;
        palette[300] = 0x40AABBCCu;
        // Non-clip: ImgTex::Combine builds X8R8G8B8, so palette alpha is dropped.
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 255 }, Rgba(PixelFormat.PFID_INDEX16, [44, 1], palette: palette));
        // Clip map: A8R8G8B8 keeps the palette alpha for index >= 8...
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0x40 }, Rgba(PixelFormat.PFID_INDEX16, [44, 1], palette: palette, clipMap: true));
        // ...and writes transparent black for index < 8.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Rgba(PixelFormat.PFID_INDEX16, [3, 0], palette: palette, clipMap: true));
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 255 }, Rgba(PixelFormat.PFID_P8, [3], palette: palette));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Rgba(PixelFormat.PFID_P8, [7], palette: palette, clipMap: true));
    }

    [Fact]
    public void Clip_map_flag_does_not_change_non_indexed_formats()
    {
        Assert.Equal(Rgba(PixelFormat.PFID_A8R8G8B8, [0, 0, 0, 5]), Rgba(PixelFormat.PFID_A8R8G8B8, [0, 0, 0, 5], clipMap: true));
    }

    [Fact]
    public void Dxt1_three_colour_mode_index_3_is_transparent_black()
    {
        // c0 = 0x001F (blue) <= c1 = 0xF800 (red): three-colour mode. Indices: texel 0 -> 0, 1 -> 1, 2 -> 2, 3 -> 3.
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0b11_10_01_00, 0, 0, 0];
        var rgba = Rgba(PixelFormat.PFID_DXT1, block, 4, 4);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[0..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[4..8]);
        Assert.Equal(new byte[] { 127, 0, 127, 255 }, rgba[8..12]); // (c0 + c1) / 2, truncated
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, rgba[12..16]);
        var native = RenderSurfaceDecoder.Decode(1, PixelFormat.PFID_DXT1, 4, 4, block, TextureDecodeMode.Native);
        Assert.Equal(DecodedTextureFormat.Dxt1, native.Format);
        Assert.True(native.HasAlpha);
        Assert.Equal(block, native.Data);
    }

    [Fact]
    public void Dxt1_four_colour_mode_interpolates_with_truncation()
    {
        // c0 = 0xF800 (red) > c1 = 0x001F (blue). Index 2 = (2*c0 + c1) / 3, index 3 = (c0 + 2*c1) / 3.
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0b11_10_01_00, 0, 0, 0];
        var rgba = Rgba(PixelFormat.PFID_DXT1, block, 4, 4);
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, rgba[8..12]);
        Assert.Equal(new byte[] { 85, 0, 170, 255 }, rgba[12..16]);
        Assert.False(RenderSurfaceDecoder.Decode(1, PixelFormat.PFID_DXT1, 4, 4, block, TextureDecodeMode.Native).HasAlpha);
    }

    [Fact]
    public void Dxt3_and_dxt5_alpha_blocks()
    {
        // DXT3: nibble k is texel k's alpha x 17; colour block always four-colour (c0 <= c1 here).
        byte[] dxt3 = [0x10, 0xF2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x1F, 0x00, 0x00, 0xF8, 0b11_10_01_00, 0, 0, 0];
        var a3 = Rgba(PixelFormat.PFID_DXT3, dxt3, 4, 4);
        Assert.Equal(new byte[] { 0, 17, 34, 255 }, new[] { a3[3], a3[7], a3[11], a3[15] });
        Assert.Equal(new byte[] { 85, 0, 170 }, a3[8..11]);  // (2*blue + red) / 3
        Assert.Equal(new byte[] { 170, 0, 85 }, a3[12..15]); // index 3 is a colour in four-colour mode, not transparent

        // DXT5, a0 = 200 > a1 = 100: index 2 = (6*200 + 100) / 7 = 185. Texel indices 0, 1, 2 = 0b10_001_000.
        byte[] dxt5 = [200, 100, 0b10_001_000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var a5 = Rgba(PixelFormat.PFID_DXT5, dxt5, 4, 4);
        Assert.Equal(new byte[] { 200, 100, 185 }, new[] { a5[3], a5[7], a5[11] });
        // a0 = 100 <= a1 = 200: the six-value table ends with 0 (index 6) and 255 (index 7). Texel indices 6, 7, 0.
        byte[] dxt5b = [100, 200, 0b00_111_110, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var b5 = Rgba(PixelFormat.PFID_DXT5, dxt5b, 4, 4);
        Assert.Equal(new byte[] { 0, 255, 100 }, new[] { b5[3], b5[7], b5[11] });
    }

    [Fact]
    public void Partial_edge_blocks_are_clipped()
    {
        // A 2x2 DXT1 still occupies one 8-byte block in the block layout.
        Assert.Equal(8, BlockCompression.ByteLength(DecodedTextureFormat.Dxt1, 2, 2));
        var rgba = new byte[2 * 2 * 4];
        BlockCompression.DecodeToRgba8(DecodedTextureFormat.Dxt1, new byte[] { 0xFF, 0xFF, 0, 0, 0, 0, 0, 0 }, 2, 2, rgba);
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(new byte[] { 255, 255, 255, 255 }, rgba[(i * 4)..((i * 4) + 4)]));
    }

    [Fact]
    public void Wrong_lengths_unknown_formats_and_missing_palettes_fail_loudly()
    {
        Assert.Throws<InvalidDataException>(() => Rgba(PixelFormat.PFID_A8R8G8B8, [1, 2, 3]));
        Assert.Throws<NotSupportedException>(() => Rgba(PixelFormat.PFID_V8U8, [1, 2]));
        Assert.Throws<ArgumentException>(() => Rgba(PixelFormat.PFID_INDEX16, [1, 0]));
        Assert.Throws<InvalidDataException>(() => Rgba(PixelFormat.PFID_INDEX16, [0xFF, 0xFF], palette: new uint[2048]));
        Assert.Throws<InvalidDataException>(() => RenderSurfaceDecoder.Decode(1, PixelFormat.PFID_CUSTOM_RAW_JPEG, 0, 0, [0xFF, 0xD8, 0xFF, 0xD9], TextureDecodeMode.Native));
    }

    [Fact]
    public void Jpeg_header_reader_finds_the_frame_after_app_segments()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,                                     // SOI
            0xFF, 0xE0, 0x00, 0x04, 0x4A, 0x46,             // APP0, length 4
            0xFF, 0xC2, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, // SOF2: precision 8, height 480, width 640, 3 components
        ];
        Assert.True(JpegHeader.TryRead(jpeg, out var h));
        Assert.Equal(new JpegHeader(640, 480, 3, 0xC2), h);
        Assert.False(JpegHeader.TryRead([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02], out _));
    }
}
