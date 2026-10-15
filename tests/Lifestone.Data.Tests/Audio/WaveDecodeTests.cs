using System.Buffers.Binary;
using System.Globalization;
using Lifestone.Data.Audio;
using DatWave = DatReaderWriter.DBObjs.Wave;

namespace Lifestone.Data.Tests.Audio;

/// <summary>
/// Step PS9: every Wave (0x0A) of the user's portal decodes exactly (m2-log-in.md §4.2, §4.10), and the decoder fails
/// loudly on a malformed record. Nothing here writes audio to disk.
/// </summary>
public sealed class WaveDecodeTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The dats' only non-PCM wave.</summary>
    private const uint Mp3Wave = 0x0A000393;

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_wave_decodes_exactly_785_pcm_and_one_mp3()
    {
        var source = new DatAudioSource(fixture.Dats);
        var ids = source.AllWaveIds();
        Assert.Equal(786, ids.Count);

        var histogram = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var nonPcm = new List<uint>();
        foreach (var id in ids)
        {
            var wave = source.GetWave(id)!; // throws unless every byte is consumed
            Assert.Equal(id, wave.Id);
            Assert.Equal(0, wave.Padding);
            Assert.Null(wave.Unsupported);
            var f = wave.Format;
            var key = string.Create(CultureInfo.InvariantCulture, $"0x{f.FormatTag:X4} {f.SamplesPerSecond} {f.BitsPerSample} {f.Channels}");
            histogram[key] = histogram.GetValueOrDefault(key) + 1;
            if (!wave.IsPcm)
            {
                nonPcm.Add(id);
                continue;
            }

            // Every PCM header is an 18-byte WAVEFORMATEX with no extra data, and self-consistent.
            Assert.Equal(WaveFormat.WaveFormatExSize, wave.Header.Length);
            Assert.Equal(0, f.ExtraSize);
            Assert.Equal(f.Channels * f.BitsPerSample / 8, f.BlockAlign);
            Assert.Equal(f.SamplesPerSecond * f.BlockAlign, f.AverageBytesPerSecond);
            Assert.Equal(0, wave.Samples.Length % f.BlockAlign);
            Assert.Equal(f.BitsPerSample == 8, wave.IsUnsigned8BitPcm);
        }

        Assert.Equal([Mp3Wave], nonPcm);

        // Format tag, rate, bits and channels, pinned for the end-of-retail portal.
        Assert.Equal(
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["0x0001 11025 16 1"] = 446,
                ["0x0001 11025 16 2"] = 3,
                ["0x0001 11025 8 1"] = 21,
                ["0x0001 12000 8 2"] = 1,
                ["0x0001 16000 16 1"] = 10,
                ["0x0001 22050 16 1"] = 120,
                ["0x0001 22050 8 1"] = 44,
                ["0x0001 32000 16 1"] = 20,
                ["0x0001 44100 16 1"] = 79,
                ["0x0001 44100 16 2"] = 8,
                ["0x0001 44100 8 2"] = 2,
                ["0x0001 5500 16 1"] = 1,
                ["0x0001 6000 16 1"] = 2,
                ["0x0001 7333 16 1"] = 2,
                ["0x0001 8000 16 1"] = 23,
                ["0x0001 8000 8 1"] = 2,
                ["0x0001 8287 8 1"] = 1,
                ["0x0055 11025 0 1"] = 1,
            },
            histogram);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_wave_matches_DatReaderWriter_byte_for_byte()
    {
        var dats = fixture.Dats;
        var source = new DatAudioSource(dats);
        foreach (var id in source.AllWaveIds())
        {
            var wave = source.GetWave(id)!;
            Assert.True(dats.TryGetPortal<DatWave>(id, out var drw), $"DatReaderWriter cannot read 0x{id:X8}");
            Assert.True(wave.Header.Span.SequenceEqual(drw.Header), $"0x{id:X8}: header differs");
            Assert.True(wave.Samples.Span.SequenceEqual(drw.Data), $"0x{id:X8}: data differs");
        }
    }

    [RequiresDatsTheory]
    [InlineData(0x0A000316u, 3.0)] // the tunnel hum
    [InlineData(0x0A000246u, 1.11)] // UI_EnterPortal
    [InlineData(0x0A000245u, 1.12)] // UI_ExitPortal
    public void The_portal_space_waves_are_pcm_mono_11025_hz_16_bit(uint id, double seconds)
    {
        var wave = new DatAudioSource(fixture.Dats).GetWave(id)!;

        Assert.True(wave.IsPcm);
        Assert.False(wave.IsUnsigned8BitPcm);
        Assert.Equal(new WaveFormat(WaveFormat.Pcm, 1, 11025, 22050, 2, 16, 0), wave.Format);
        Assert.Equal(seconds, wave.DurationSeconds!.Value, 0.01);
        Assert.Equal(wave.Samples.Length / 2, wave.SampleFrames);
    }

    [RequiresDatsFact]
    public void The_mp3_wave_has_an_mpeglayer3_header_and_raw_frames()
    {
        var wave = new DatAudioSource(fixture.Dats).GetWave(Mp3Wave)!;

        Assert.True(wave.IsMp3);
        Assert.False(wave.IsPcm);
        Assert.Null(wave.Unsupported);
        Assert.Null(wave.DurationSeconds);
        Assert.Null(wave.SampleFrames);
        Assert.Equal(new WaveFormat(WaveFormat.MpegLayer3, 1, 11025, 2500, 1, 0, 12), wave.Format);
        Assert.Equal(30, wave.Header.Length); // MPEGLAYER3WAVEFORMAT
        Assert.Equal(5120, wave.Samples.Length);
    }

    [RequiresDatsFact]
    public void Ids_outside_the_wave_range_are_absent()
    {
        var source = new DatAudioSource(fixture.Dats);

        Assert.Null(source.GetWave(0x2000004B)); // a SoundTable
        Assert.Null(source.GetWave(0x0A00FFFE)); // no such wave
        Assert.Same(source.GetWave(0x0A000316), source.GetWave(0x0A000316));
    }

    [Fact]
    public void A_pcm_record_decodes_header_and_samples()
    {
        var raw = Record(0x0A000001, PcmHeader(1, 11025, 16), [1, 2, 3, 4]);

        var wave = WaveData.Decode(0x0A000001, raw);

        Assert.Equal(new WaveFormat(1, 1, 11025, 22050, 2, 16, 0), wave.Format);
        Assert.Equal([1, 2, 3, 4], wave.Samples.ToArray());
        Assert.Equal(PcmHeader(1, 11025, 16), wave.Header.ToArray());
        Assert.Equal(2, wave.SampleFrames);
        Assert.Equal(4 / 22050.0, wave.DurationSeconds);
        Assert.Equal(0, wave.Padding);
    }

    [Fact]
    public void A_16_byte_pcmwaveformat_header_has_no_extra_size()
    {
        var raw = Record(0x0A000001, PcmHeader(1, 8000, 8)[..16], [0x80]);

        var wave = WaveData.Decode(0x0A000001, raw);

        Assert.Equal(new WaveFormat(1, 1, 8000, 8000, 1, 8, 0), wave.Format);
        Assert.True(wave.IsUnsigned8BitPcm);
    }

    [Fact]
    public void Up_to_three_zero_bytes_of_alignment_are_tolerated_and_counted()
    {
        var raw = Record(0x0A000001, PcmHeader(1, 11025, 16), [1, 2], padding: [0, 0, 0]);

        Assert.Equal(3, WaveData.Decode(0x0A000001, raw).Padding);
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })] // four bytes is more than alignment
    [InlineData(new byte[] { 0, 1 })] // not zero
    public void Trailing_bytes_that_are_not_alignment_throw(byte[] trailing)
    {
        var raw = Record(0x0A000001, PcmHeader(1, 11025, 16), [1, 2], padding: trailing);

        var e = Assert.Throws<InvalidDataException>(() => WaveData.Decode(0x0A000001, raw));
        Assert.Contains("0x0A000001", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_truncated_record_throws()
    {
        var raw = Record(0x0A000001, PcmHeader(1, 11025, 16), [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => WaveData.Decode(0x0A000001, raw.AsSpan(0, raw.Length - 1)));
        Assert.Throws<InvalidDataException>(() => WaveData.Decode(0x0A000001, raw.AsSpan(0, 6)));
    }

    [Fact]
    public void A_record_of_another_id_throws()
    {
        var raw = Record(0x0A000002, PcmHeader(1, 11025, 16), [1, 2]);

        Assert.Throws<InvalidDataException>(() => WaveData.Decode(0x0A000001, raw));
    }

    [Fact]
    public void A_header_whose_cbsize_disagrees_with_its_length_throws()
    {
        var header = PcmHeader(1, 11025, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 2); // claims 2 extra bytes that are not there

        var e = Assert.Throws<InvalidDataException>(() => WaveData.Decode(0x0A000001, Record(0x0A000001, header, [1, 2])));
        Assert.Contains("cbSize", e.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => WaveFormat.Parse(header.AsSpan(0, 12)));
        Assert.Throws<InvalidDataException>(() => WaveFormat.Parse(header.AsSpan(0, 17)));
    }

    [Fact]
    public void Another_format_tag_decodes_but_is_a_named_unsupported_todo()
    {
        var header = PcmHeader(1, 11025, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(header, 2); // MS ADPCM

        var wave = WaveData.Decode(0x0A000001, Record(0x0A000001, header, [1, 2]));

        Assert.False(wave.IsPcm);
        Assert.False(wave.IsMp3);
        Assert.Null(wave.DurationSeconds);
        Assert.StartsWith("TODO(audio): wave 0x0A000001 has format tag 0x0002", wave.Unsupported, StringComparison.Ordinal);
    }

    /// <summary>An 18-byte WAVEFORMATEX for PCM with no extra data.</summary>
    internal static byte[] PcmHeader(ushort channels, uint rate, ushort bits)
    {
        var header = new byte[18];
        var align = (ushort)(channels * bits / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(header, WaveFormat.Pcm);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), rate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), rate * align);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), align);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), bits);
        return header;
    }

    /// <summary>A raw Wave record: id, header size, data size, header, data, then any padding.</summary>
    internal static byte[] Record(uint id, byte[] header, byte[] data, byte[]? padding = null)
    {
        padding ??= [];
        var raw = new byte[12 + header.Length + data.Length + padding.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, id);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)header.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)data.Length);
        header.CopyTo(raw, 12);
        data.CopyTo(raw, 12 + header.Length);
        padding.CopyTo(raw, 12 + header.Length + data.Length);
        return raw;
    }
}
