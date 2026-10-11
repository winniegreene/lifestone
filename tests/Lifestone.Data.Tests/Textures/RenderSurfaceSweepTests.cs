using System.Buffers.Binary;
using System.Diagnostics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Textures;

/// <summary>Decodes every RenderSurface in the user's portal and highres dats.</summary>
public sealed class RenderSurfaceSweepTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>Format census of client_portal.dat (docs/research/local-scout.md, dat-format.md).</summary>
    private static readonly Dictionary<PixelFormat, int> PortalCensus = new()
    {
        [PixelFormat.PFID_A8R8G8B8] = 12_984,
        [PixelFormat.PFID_INDEX16] = 4_182,
        [PixelFormat.PFID_DXT1] = 1_971,
        [PixelFormat.PFID_R8G8B8] = 1_190,
        [PixelFormat.PFID_DXT5] = 127,
        [PixelFormat.PFID_A8] = 86,
        [PixelFormat.PFID_CUSTOM_RAW_JPEG] = 79,
        [PixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8] = 33,
        [PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA] = 16,
        [PixelFormat.PFID_P8] = 6,
        [PixelFormat.PFID_DXT3] = 5,
        [PixelFormat.PFID_R5G6B5] = 3,
        [PixelFormat.PFID_A4R4G4B4] = 2,
    };

    /// <summary>Format census of client_highres.dat.</summary>
    private static readonly Dictionary<PixelFormat, int> HighResCensus = new()
    {
        [PixelFormat.PFID_INDEX16] = 1_283,
        [PixelFormat.PFID_DXT1] = 986,
        [PixelFormat.PFID_R8G8B8] = 13,
        [PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA] = 8,
        [PixelFormat.PFID_A8R8G8B8] = 3,
        [PixelFormat.PFID_DXT5] = 1,
    };

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_portal_render_surface_decodes_in_both_modes() =>
        Sweep(highRes: false, PortalCensus, new() { [PixelFormat.PFID_DXT1] = 257, [PixelFormat.PFID_DXT3] = 5, [PixelFormat.PFID_DXT5] = 116 });

    [RequiresHighResDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_highres_render_surface_decodes_in_both_modes() =>
        Sweep(highRes: true, HighResCensus, new() { [PixelFormat.PFID_DXT1] = 21, [PixelFormat.PFID_DXT5] = 1 });

    /// <param name="highRes">Sweep client_highres.dat instead of the portal.</param>
    /// <param name="expectedCensus">Records per pixel format.</param>
    /// <param name="expectedBlockAlpha">
    /// DXT records whose decoded alpha is not all 255, counted independently with the Python scout decoder
    /// (scratchpad textures/dxt1split.py).
    /// </param>
    private void Sweep(bool highRes, Dictionary<PixelFormat, int> expectedCensus, Dictionary<PixelFormat, int> expectedBlockAlpha)
    {
        var loader = new TextureLoader(Dats, paletteCacheCapacity: 5000);
        var ids = TextureTestSupport.RenderSurfaceIds(Dats, highRes);
        var census = new Dictionary<PixelFormat, int>();
        var blockAlpha = new Dictionary<PixelFormat, int>();
        var maxIndex = 0;
        long sourceBytes = 0, nativeBytes = 0, rgbaBytes = 0;
        var readTime = new Stopwatch();
        var nativeTime = new Stopwatch();
        var rgbaTime = new Stopwatch();
        var failures = new List<string>();

        foreach (var id in ids)
        {
            readTime.Start();
            var rs = TextureTestSupport.ReadUncached<RenderSurface>(Dats, id, highRes);
            readTime.Stop();
            if (rs is null)
            {
                failures.Add($"0x{id:X8}: unreadable");
                continue;
            }

            census[rs.Format] = census.GetValueOrDefault(rs.Format) + 1;
            sourceBytes += rs.SourceData.Length;
            try
            {
                CheckLength(rs);
                ReadOnlySpan<uint> palette = default;
                if (RenderSurfaceDecoder.IsIndexed(rs.Format))
                {
                    var pal = TextureTestSupport.Palette(loader, rs);
                    Assert.Equal(PaletteData.RetailColorCount, pal.Count);
                    palette = pal.Argb;
                    maxIndex = Math.Max(maxIndex, MaxIndex(rs));
                }

                nativeTime.Start();
                var native = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Native, palette);
                nativeTime.Stop();
                rgbaTime.Start();
                var rgba = RenderSurfaceDecoder.Decode(rs, TextureDecodeMode.Rgba8, palette);
                rgbaTime.Stop();
                nativeBytes += native.Data.Length;
                rgbaBytes += rgba.Data.Length;
                CheckOutput(rs, native, rgba);
                if (native.Format is DecodedTextureFormat.Dxt1 or DecodedTextureFormat.Dxt3 or DecodedTextureFormat.Dxt5 && native.HasAlpha)
                {
                    blockAlpha[rs.Format] = blockAlpha.GetValueOrDefault(rs.Format) + 1;
                }
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                failures.Add($"0x{id:X8} {rs.Format}: {ex.Message}");
            }
        }

        output.WriteLine($"{(highRes ? "highres" : "portal")}: {ids.Length} RenderSurfaces, {sourceBytes / 1e6:F1} MB source");
        output.WriteLine($"  read (DatReaderWriter, uncached): {readTime.Elapsed.TotalSeconds:F2} s");
        output.WriteLine($"  decode Native: {nativeTime.Elapsed.TotalSeconds:F2} s -> {nativeBytes / 1e6:F1} MB");
        output.WriteLine($"  decode Rgba8:  {rgbaTime.Elapsed.TotalSeconds:F2} s -> {rgbaBytes / 1e6:F1} MB");
        output.WriteLine($"  DXT with alpha: {string.Join(", ", blockAlpha.Select(kv => $"{kv.Key} {kv.Value}"))}; max palette index: {maxIndex}");

        Assert.Empty(failures);
        Assert.Equal(expectedCensus.OrderBy(kv => kv.Key), census.OrderBy(kv => kv.Key));
        Assert.True(maxIndex < PaletteData.RetailColorCount);
        Assert.Equal(expectedBlockAlpha.OrderBy(kv => kv.Key), blockAlpha.OrderBy(kv => kv.Key));
    }

    private static void CheckLength(RenderSurface rs)
    {
        if (rs.Format == PixelFormat.PFID_CUSTOM_RAW_JPEG)
        {
            // The dat header says 0x0; the size lives in the JPEG frame header.
            Assert.Equal(0, rs.Width);
            Assert.Equal(0, rs.Height);
            Assert.True(JpegHeader.TryRead(rs.SourceData, out var header), "JPEG frame header");
            Assert.Equal(3, header.Components);
            return;
        }

        Assert.True(RenderSurfaceDecoder.IsSupported(rs.Format), $"unsupported {rs.Format}");
        Assert.True(rs.Width > 0 && rs.Height > 0);
        Assert.Equal(RenderSurfaceDecoder.ExpectedSourceLength(rs.Format, rs.Width, rs.Height), rs.SourceData.Length);
    }

    private static void CheckOutput(RenderSurface rs, DecodedTexture native, DecodedTexture rgba)
    {
        if (rs.Format == PixelFormat.PFID_CUSTOM_RAW_JPEG)
        {
            Assert.Equal(DecodedTextureFormat.Jpeg, native.Format);
            Assert.Equal(DecodedTextureFormat.Jpeg, rgba.Format);
            Assert.Equal(rs.SourceData, native.Data);
            Assert.True(native.Width > 0 && native.Height > 0);
            return;
        }

        Assert.Equal(DecodedTextureFormat.Rgba8, rgba.Format);
        Assert.Equal(rs.Width * rs.Height * 4, rgba.Data.Length);
        switch (native.Format)
        {
            case DecodedTextureFormat.Dxt1:
            case DecodedTextureFormat.Dxt3:
            case DecodedTextureFormat.Dxt5:
                Assert.Equal(rs.SourceData, native.Data);
                Assert.Equal(BlockCompression.ByteLength(native.Format, rs.Width, rs.Height), native.Data.Length);
                Assert.Equal(native.HasAlpha, rgba.HasAlpha);
                break;
            case DecodedTextureFormat.Alpha8:
                Assert.Equal(rs.SourceData, native.Data);
                Assert.Equal(native.HasAlpha, rgba.HasAlpha);
                break;
            default:
                Assert.Equal(DecodedTextureFormat.Rgba8, native.Format);
                Assert.Equal(rgba.Data, native.Data);
                break;
        }
    }

    private static int MaxIndex(RenderSurface rs)
    {
        var max = 0;
        if (rs.Format == PixelFormat.PFID_P8)
        {
            foreach (var b in rs.SourceData)
            {
                max = Math.Max(max, b);
            }

            return max;
        }

        for (var i = 0; i < rs.SourceData.Length; i += 2)
        {
            max = Math.Max(max, BinaryPrimitives.ReadUInt16LittleEndian(rs.SourceData.AsSpan(i)));
        }

        return max;
    }
}
