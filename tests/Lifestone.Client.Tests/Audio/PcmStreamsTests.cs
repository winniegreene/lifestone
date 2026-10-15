using System.Buffers.Binary;
using Lifestone.Client.Audio;
using Lifestone.Data.Audio;

namespace Lifestone.Client.Tests.Audio;

/// <summary>PCM waves as host stream samples: re-centred 8-bit, whole frames, and retail's pan baked into 16-bit stereo.</summary>
public sealed class PcmStreamsTests
{
    private const uint Id = 0x0A000316;

    /// <summary>A wave record with a PCM header of the given layout and the given sample bytes.</summary>
    private static WaveData Wave(ushort channels, ushort bits, uint rate, byte[] samples, ushort formatTag = WaveFormat.Pcm)
    {
        var raw = new byte[12 + 16 + samples.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, Id);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)samples.Length);
        var header = raw.AsSpan(12);
        var blockAlign = (ushort)(channels * (bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(header, formatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], rate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], rate * blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], bits);
        samples.CopyTo(raw.AsSpan(28));
        return WaveData.Decode(Id, raw);
    }

    private static byte[] Pcm16(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }

        return bytes;
    }

    private static short At(PcmStream stream, int index) => BinaryPrimitives.ReadInt16LittleEndian(stream.Data.AsSpan(index * 2));

    [Fact]
    public void A_centred_16_bit_mono_wave_passes_through_with_its_rate_and_length()
    {
        // The dats' sounds: 11,025 Hz mono 16-bit. One second of samples is one second of sound.
        var samples = new byte[11_025 * 2];
        samples[0] = 0x34;
        samples[1] = 0x12;
        Assert.True(PcmStreams.TryBuild(Wave(1, 16, 11_025, samples), 0, out var stream, out var problem));
        Assert.Null(problem);
        Assert.Equal(11_025, stream.MixRate);
        Assert.False(stream.Stereo);
        Assert.False(stream.Is8Bit);
        Assert.Equal(11_025, stream.Frames);
        Assert.Equal(1.0, stream.Seconds, 6);
        Assert.Equal(samples, stream.Data);
        Assert.Equal(0x1234, At(stream, 0));
    }

    [Fact]
    public void Unsigned_8_bit_samples_are_re_centred_to_signed()
    {
        Assert.True(PcmStreams.TryBuild(Wave(1, 8, 22_050, [0x80, 0xFF, 0x00, 0x81]), 0, out var stream, out _));
        Assert.True(stream.Is8Bit);
        Assert.Equal(22_050, stream.MixRate);
        Assert.Equal(new byte[] { 0x00, 0x7F, 0x80, 0x01 }, stream.Data); // 0, +127, -128, +1 as signed bytes
    }

    [Fact]
    public void A_trailing_partial_frame_is_dropped()
    {
        Assert.True(PcmStreams.TryBuild(Wave(2, 16, 44_100, new byte[4 * 3 + 3]), 0, out var stream, out _));
        Assert.True(stream.Stereo);
        Assert.Equal(3, stream.Frames);
        Assert.Equal(12, stream.Data.Length);
    }

    [Fact]
    public void A_negative_pan_lowers_the_right_channel_by_its_decibels()
    {
        // Retail: below 0 the right channel is lowered by |pan| dB (SoundBuf::Play), the left is left alone.
        Assert.True(PcmStreams.TryBuild(Wave(1, 16, 11_025, Pcm16(10_000, -10_000)), -15, out var stream, out _));
        Assert.True(stream.Stereo);
        Assert.False(stream.Is8Bit);
        Assert.Equal(2, stream.Frames);
        var (_, right) = RetailSoundRules.ChannelGains(-15, 0);
        Assert.Equal(10_000, At(stream, 0));
        Assert.Equal((int)Math.Round(10_000 * right), At(stream, 1));
        Assert.Equal(-10_000, At(stream, 2));
        Assert.Equal((int)Math.Round(-10_000 * right), At(stream, 3));
        Assert.InRange(right, 0.1778f, 0.1779f); // 10^(-15/20)
    }

    [Fact]
    public void A_positive_pan_lowers_the_left_channel_and_a_stereo_wave_keeps_its_channels()
    {
        Assert.True(PcmStreams.TryBuild(Wave(2, 16, 11_025, Pcm16(8_000, -6_000)), 10, out var stream, out _));
        var (left, _) = RetailSoundRules.ChannelGains(10, 0);
        Assert.Equal((int)Math.Round(8_000 * left), At(stream, 0));
        Assert.Equal(-6_000, At(stream, 1));
    }

    [Fact]
    public void A_pan_on_an_8_bit_wave_widens_it_to_16_bit_stereo()
    {
        Assert.True(PcmStreams.TryBuild(Wave(1, 8, 11_025, [0xC0]), -15, out var stream, out _));
        Assert.False(stream.Is8Bit);
        Assert.Equal(64 << 8, At(stream, 0)); // 0xC0 is 64 above the unsigned midpoint
    }

    [Fact]
    public void A_pan_beyond_fifteen_is_clamped_as_retail_does()
    {
        Assert.True(PcmStreams.TryBuild(Wave(1, 16, 11_025, Pcm16(1_000)), -40, out var far, out _));
        Assert.True(PcmStreams.TryBuild(Wave(1, 16, 11_025, Pcm16(1_000)), -15, out var edge, out _));
        Assert.Equal(edge.Data, far.Data);
    }

    [Theory]
    [InlineData(3, 16)]
    [InlineData(1, 24)]
    [InlineData(2, 4)]
    public void An_unsupported_pcm_layout_is_a_named_todo(int channels, int bits)
    {
        Assert.False(PcmStreams.TryBuild(Wave((ushort)channels, (ushort)bits, 11_025, new byte[24]), 0, out _, out var problem));
        Assert.StartsWith("TODO(audio)", problem, StringComparison.Ordinal);
        Assert.Contains("0x0A000316", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wave_that_is_not_pcm_is_refused()
    {
        Assert.False(PcmStreams.TryBuild(Wave(1, 16, 11_025, new byte[8], WaveFormat.MpegLayer3), 0, out _, out var problem));
        Assert.NotNull(problem);
    }
}
