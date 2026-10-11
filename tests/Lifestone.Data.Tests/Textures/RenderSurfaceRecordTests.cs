using System.Buffers.Binary;
using System.Diagnostics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Dat;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Textures;

/// <summary>
/// The pooled decode path (wave-3 performance pass): RenderSurface records read as raw bytes
/// (<see cref="RenderSurfaceRecord"/>) and decoded into pooled buffers must equal DatReaderWriter's record and the
/// allocating decoder, for every RenderSurface in the user's dats.
/// </summary>
public sealed class RenderSurfaceRecordTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [Fact]
    public void A_record_is_parsed_field_by_field()
    {
        var record = new byte[RenderSurfaceRecord.HeaderSize + 8 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x06001234u);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), 7u);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), 2);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), (uint)PixelFormat.PFID_INDEX16);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(32), 0x04000ABCu);

        var parsed = RenderSurfaceRecord.Parse(0x06001234u, record);

        Assert.Equal(new RenderSurfaceRecord(0x06001234u, 2, 2, PixelFormat.PFID_INDEX16, 24, 8, 0x04000ABCu), parsed);
        Assert.Equal(0u, RenderSurfaceRecord.Parse(1, Header(PixelFormat.PFID_DXT1, 8)).DefaultPaletteId);
    }

    [Fact]
    public void A_truncated_record_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => RenderSurfaceRecord.Parse(1, new byte[10]));
        Assert.Throws<InvalidDataException>(() => RenderSurfaceRecord.Parse(1, Header(PixelFormat.PFID_DXT1, 8)[..30]));
        Assert.Throws<InvalidDataException>(() => RenderSurfaceRecord.Parse(1, Header(PixelFormat.PFID_P8, 8))); // no palette id
    }

    [RequiresDatsFact]
    public void A_pooled_decode_returns_its_buffer_once()
    {
        var loader = new TextureLoader(Dats);
        var id = TextureTestSupport.RenderSurfaceIds(Dats, highRes: false).First(i =>
            TextureTestSupport.ReadUncached<RenderSurface>(Dats, i)?.Format == PixelFormat.PFID_DXT1);
        var pooled = loader.DecodeRenderSurfacePooled(id, TextureDecodeMode.Native);
        Assert.Equal(DecodedTextureFormat.Dxt1, pooled.Format);
        pooled.Dispose();
        pooled.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => pooled.Data.Length);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_render_surface_reads_and_decodes_the_same_through_the_pooled_path()
    {
        var loader = new TextureLoader(Dats, paletteCacheCapacity: 5000);
        var ids = TextureTestSupport.RenderSurfaceIds(Dats, highRes: false)
            .Concat(TextureTestSupport.RenderSurfaceIds(Dats, highRes: true))
            .Distinct()
            .ToArray();
        var failures = new List<string>();
        var timer = Stopwatch.StartNew();
        foreach (var id in ids)
        {
            // The loader reads high-res first, as ReadRenderSurface does.
            var rs = loader.ReadRenderSurface(id, out _);
            if (rs is null)
            {
                failures.Add($"0x{id:X8}: unreadable");
                continue;
            }

            try
            {
                var palette = RenderSurfaceDecoder.IsIndexed(rs.Format) ? TextureTestSupport.Palette(loader, rs).Argb : default;
                foreach (var mode in new[] { TextureDecodeMode.Native, TextureDecodeMode.Rgba8 })
                {
                    var expected = RenderSurfaceDecoder.Decode(rs, mode, palette);
                    using var pooled = loader.DecodeRenderSurfacePooled(id, mode);

                    // The allocating loader API reads the same way (checked in one mode; the decode itself is shared).
                    var actuals = mode == TextureDecodeMode.Native ? new[] { pooled.ToDecodedTexture(), loader.DecodeRenderSurface(id, mode) } : [pooled.ToDecodedTexture()];
                    foreach (var actual in actuals)
                    {
                        if (actual.Format != expected.Format || actual.Width != expected.Width || actual.Height != expected.Height
                            || actual.HasAlpha != expected.HasAlpha || actual.SourceFormat != expected.SourceFormat || actual.SourceId != id
                            || !actual.Data.AsSpan().SequenceEqual(expected.Data))
                        {
                            failures.Add($"0x{id:X8} {rs.Format} {mode}: {actual} differs from {expected}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                failures.Add($"0x{id:X8} {rs.Format}: {ex.Message}");
            }
        }

        output.WriteLine($"{ids.Length} RenderSurfaces compared in {timer.Elapsed.TotalSeconds:F1} s");
        Assert.Empty(failures.Take(20));
    }

    private static byte[] Header(PixelFormat format, int length)
    {
        var record = new byte[RenderSurfaceRecord.HeaderSize + length];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), 4);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), (uint)format);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), length);
        return record;
    }
}
