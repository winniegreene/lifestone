using Lifestone.Data.Audio;

namespace Lifestone.Client.Audio;

/// <summary>A PCM sound ready for a host's stream object: interleaved little-endian samples at a rate.</summary>
/// <param name="Data">The samples: signed 8-bit or signed 16-bit, interleaved when stereo.</param>
/// <param name="MixRate">Samples per second per channel.</param>
/// <param name="Stereo">Two channels.</param>
/// <param name="Is8Bit">Signed 8-bit samples (otherwise signed 16-bit).</param>
public readonly record struct PcmStream(byte[] Data, int MixRate, bool Stereo, bool Is8Bit)
{
    /// <summary>The bytes of one sample frame (all channels).</summary>
    public int FrameBytes => (Stereo ? 2 : 1) * (Is8Bit ? 1 : 2);

    /// <summary>Sample frames in <see cref="Data"/>.</summary>
    public int Frames => Data.Length / FrameBytes;

    /// <summary>The sound's length in seconds.</summary>
    public double Seconds => Frames / (double)MixRate;
}

/// <summary>
/// Turns a decoded PCM <see cref="WaveData"/> into the samples a host's audio stream wants (the Godot audio service, G1 W2):
/// retail's unsigned 8-bit samples are re-centred to signed, a trailing partial frame is dropped, and a voice with a pan gets
/// retail's two channel gains baked into 16-bit stereo (Godot's non-spatial players have no pan).
/// </summary>
/// <remarks>
/// <para><b>Retail's pan</b> is a per-channel attenuation of up to 15 dB: below 0 the right channel is lowered by |pan| dB, above 0
/// the left by pan dB (<c>SoundBuf::Play</c>; <see cref="RetailSoundRules.ChannelGains"/>). The voice's volume is applied by the
/// player, so only the pan's own gains are baked here. A centred sound (pan 0: every portal-space and interface sound) is passed
/// through unchanged apart from the 8-bit re-centring.</para>
/// <para>1 or 2 channels of 8 or 16 bits are supported; anything else is a named <c>TODO(audio)</c> in <c>problem</c>.</para>
/// </remarks>
public static class PcmStreams
{
    /// <summary>Converts <paramref name="wave"/>; false with <paramref name="problem"/> set when its PCM layout is not supported.</summary>
    /// <param name="wave">A PCM wave (<see cref="WaveData.IsPcm"/>).</param>
    /// <param name="pan">Retail's pan index; values outside ±15 are clamped as <c>SoundBuf::Play</c> does.</param>
    /// <param name="stream">The samples.</param>
    /// <param name="problem">Why the wave cannot be converted.</param>
    public static bool TryBuild(WaveData wave, int pan, out PcmStream stream, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(wave);
        stream = default;
        var format = wave.Format;
        var channels = (int)format.Channels;
        var bits = (int)format.BitsPerSample;
        if (!wave.IsPcm)
        {
            problem = $"wave 0x{wave.Id:X8} is not PCM (format tag 0x{format.FormatTag:X4})";
            return false;
        }

        if (channels is < 1 or > 2 || bits is not (8 or 16) || format.SamplesPerSecond is 0 or > int.MaxValue)
        {
            problem = $"TODO(audio): wave 0x{wave.Id:X8} is PCM with {channels} channel(s), {bits} bit(s) at {format.SamplesPerSecond} Hz; Lifestone plays 1 or 2 channels of 8 or 16 bits";
            return false;
        }

        problem = null;
        var bytesPerSample = bits / 8;
        var frameBytes = channels * bytesPerSample;
        var samples = wave.Samples.Span;
        var frames = samples.Length / frameBytes; // a trailing partial frame is dropped
        pan = Math.Clamp(pan, -RetailSoundRules.MaxPan, RetailSoundRules.MaxPan);
        var rate = (int)format.SamplesPerSecond;
        if (pan == 0)
        {
            var data = new byte[frames * frameBytes];
            samples[..data.Length].CopyTo(data);
            if (bits == 8)
            {
                for (var i = 0; i < data.Length; i++)
                {
                    data[i] ^= 0x80; // retail's 8-bit PCM is unsigned, the host's is signed
                }
            }

            stream = new PcmStream(data, rate, channels == 2, bits == 8);
            return true;
        }

        var (left, right) = RetailSoundRules.ChannelGains(pan, 0);
        var baked = new byte[frames * 4];
        for (var f = 0; f < frames; f++)
        {
            var at = f * frameBytes;
            var l = Sample(samples, at, bits);
            var r = channels == 2 ? Sample(samples, at + bytesPerSample, bits) : l;
            Write(baked, f * 4, l * left);
            Write(baked, (f * 4) + 2, r * right);
        }

        stream = new PcmStream(baked, rate, Stereo: true, Is8Bit: false);
        return true;
    }

    /// <summary>One sample as a signed 16-bit value (8-bit unsigned samples are re-centred and widened).</summary>
    private static float Sample(ReadOnlySpan<byte> samples, int at, int bits) =>
        bits == 8 ? (samples[at] - 128) << 8 : (short)(samples[at] | (samples[at + 1] << 8));

    private static void Write(byte[] into, int at, float value)
    {
        var s = (short)Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue);
        into[at] = (byte)(s & 0xFF);
        into[at + 1] = (byte)((s >> 8) & 0xFF);
    }
}
