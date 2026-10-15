using System.Buffers.Binary;

namespace Lifestone.Data.Audio;

/// <summary>
/// The format header of a Wave record: Windows' <c>WAVEFORMATEX</c> (or its 16-byte <c>PCMWAVEFORMAT</c> prefix), as
/// retail hands it to DirectSound unchanged (<c>SoundBuf::Create</c>, 11.4186 0x00552930, 11.6096 0x00553550).
/// </summary>
/// <param name="FormatTag"><c>wFormatTag</c>: <see cref="Pcm"/> or <see cref="MpegLayer3"/> in the end-of-retail dats.</param>
/// <param name="Channels"><c>nChannels</c>.</param>
/// <param name="SamplesPerSecond"><c>nSamplesPerSec</c>: the sample rate.</param>
/// <param name="AverageBytesPerSecond"><c>nAvgBytesPerSec</c>.</param>
/// <param name="BlockAlign"><c>nBlockAlign</c>: bytes per sample frame (PCM) or per codec block.</param>
/// <param name="BitsPerSample"><c>wBitsPerSample</c>.</param>
/// <param name="ExtraSize">
/// <c>cbSize</c>: the bytes of format-specific data after the 18-byte <c>WAVEFORMATEX</c> (12 for MPEG layer 3), or 0
/// when the header is a 16-byte <c>PCMWAVEFORMAT</c> without the field.
/// </param>
public readonly record struct WaveFormat(
    ushort FormatTag,
    ushort Channels,
    uint SamplesPerSecond,
    uint AverageBytesPerSecond,
    ushort BlockAlign,
    ushort BitsPerSample,
    int ExtraSize)
{
    /// <summary><c>WAVE_FORMAT_PCM</c>: uncompressed samples (8-bit unsigned, or 16-bit signed little-endian).</summary>
    public const ushort Pcm = 1;

    /// <summary><c>WAVE_FORMAT_MPEGLAYER3</c>: MPEG-1 layer 3 frames (retail converted it to PCM with MSACM).</summary>
    public const ushort MpegLayer3 = 0x55;

    /// <summary>The size of <c>PCMWAVEFORMAT</c>, the shortest header: everything up to <c>wBitsPerSample</c>.</summary>
    public const int PcmWaveFormatSize = 16;

    /// <summary>The size of <c>WAVEFORMATEX</c> without its extra data (<c>cbSize</c> included).</summary>
    public const int WaveFormatExSize = 18;

    /// <summary>
    /// Parses a header. A 16-byte header has no <c>cbSize</c>; a longer one must be exactly 18 + <c>cbSize</c> bytes.
    /// </summary>
    /// <exception cref="InvalidDataException">The header is shorter than 16 bytes, or its length disagrees with <c>cbSize</c>.</exception>
    public static WaveFormat Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < PcmWaveFormatSize)
        {
            throw new InvalidDataException($"A wave format header of {header.Length} byte(s) is shorter than {PcmWaveFormatSize}.");
        }

        var extra = 0;
        if (header.Length != PcmWaveFormatSize)
        {
            if (header.Length < WaveFormatExSize)
            {
                throw new InvalidDataException($"A wave format header of {header.Length} byte(s) has a partial cbSize field.");
            }

            extra = BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
            if (header.Length != WaveFormatExSize + extra)
            {
                throw new InvalidDataException(
                    $"A wave format header of {header.Length} byte(s) declares {extra} extra byte(s) (cbSize), so it should be {WaveFormatExSize + extra}.");
            }
        }

        return new WaveFormat(
            BinaryPrimitives.ReadUInt16LittleEndian(header),
            BinaryPrimitives.ReadUInt16LittleEndian(header[2..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
            extra);
    }
}
