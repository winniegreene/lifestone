namespace Lifestone.Data.Audio;

/// <summary>
/// A decoded Wave (0x0A) record: its format header and its sample bytes, exactly as the dat stores them. Re-expresses
/// retail <c>DBWave::UnPack</c> (11.4186 0x00551B90; 11.6096 unmapped, TODO(portal): 11.6096, see
/// docs/research/retail-addresses-m2.md §3: not in the pinned table; it is the <c>UnPack</c> slot of the vtable that
/// <c>DBWave::DBWave</c>, 11.6096 0x005526C0, installs): after the record's id, a u32 header size, a u32 data size, the
/// header (a <c>WAVEFORMATEX</c>, <see cref="WaveFormat"/>), then the data.
/// </summary>
/// <remarks>
/// <para><b>Formats.</b> <see cref="WaveFormat.Pcm"/> waves are played as they are (8-bit PCM is unsigned, 16-bit is
/// signed little-endian, as in any WAV file). The dats' one <see cref="WaveFormat.MpegLayer3"/> wave holds raw MPEG
/// frames; retail converted it to PCM with the Windows audio-compression manager when it filled the buffer
/// (<c>SoundBuf::CopyWaveToBuffer</c>, 11.4186 0x005526D0, 11.6096 0x005532F0), and a host decodes it with its own MP3
/// decoder. Any other tag is <see cref="Unsupported"/>: a named TODO a host reports and never plays.</para>
/// <para>Immutable once decoded; safe to share between threads. No audio bytes are ever written to disk.</para>
/// </remarks>
public sealed class WaveData
{
    private WaveData(uint id, WaveFormat format, ReadOnlyMemory<byte> header, ReadOnlyMemory<byte> samples, int padding)
    {
        Id = id;
        Format = format;
        Header = header;
        Samples = samples;
        Padding = padding;
    }

    /// <summary>The Wave's id (0x0Axxxxxx).</summary>
    public uint Id { get; }

    /// <summary>The parsed format header.</summary>
    public WaveFormat Format { get; }

    /// <summary>The format header's raw bytes (a host that hands the header to a codec uses these).</summary>
    public ReadOnlyMemory<byte> Header { get; }

    /// <summary>The sample bytes (PCM samples, or the MP3 frames).</summary>
    public ReadOnlyMemory<byte> Samples { get; }

    /// <summary>Zero bytes after the data that only align the record (0 to 3).</summary>
    public int Padding { get; }

    /// <summary>The wave is uncompressed PCM.</summary>
    public bool IsPcm => Format.FormatTag == WaveFormat.Pcm;

    /// <summary>The wave holds MPEG layer 3 frames.</summary>
    public bool IsMp3 => Format.FormatTag == WaveFormat.MpegLayer3;

    /// <summary>The samples are 8-bit unsigned PCM (a host whose mixer wants signed samples converts them).</summary>
    public bool IsUnsigned8BitPcm => IsPcm && Format.BitsPerSample == 8;

    /// <summary>Whole sample frames (PCM only): the data size over the block alignment.</summary>
    public int? SampleFrames => IsPcm && Format.BlockAlign != 0 ? Samples.Length / Format.BlockAlign : null;

    /// <summary>The length in seconds (PCM only): the data size over the average bytes per second.</summary>
    public double? DurationSeconds => IsPcm && Format.AverageBytesPerSecond != 0 ? Samples.Length / (double)Format.AverageBytesPerSecond : null;

    /// <summary>
    /// Why a host cannot play the wave, or null when it can (PCM and MPEG layer 3). Retail converted every non-PCM format
    /// with MSACM; Lifestone supports only the formats the dats use.
    /// </summary>
    public string? Unsupported => IsPcm || IsMp3
        ? null
        : $"TODO(audio): wave 0x{Id:X8} has format tag 0x{Format.FormatTag:X4}; retail converted it with MSACM (SoundBuf::CopyWaveToBuffer), Lifestone has no decoder for it";

    /// <summary>
    /// Decodes one raw Wave record (the bytes of file <paramref name="id"/>, starting with the id). Every byte must be
    /// consumed; up to 3 trailing zero bytes are taken as alignment (<see cref="Padding"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">The record is malformed.</exception>
    public static WaveData Decode(uint id, ReadOnlySpan<byte> raw)
    {
        var reader = new AudioRecordReader(raw, "Wave", id);
        reader.ReadId();
        var headerSize = Count(reader.ReadUInt32(), id, "header");
        var dataSize = Count(reader.ReadUInt32(), id, "data");
        var header = reader.ReadBytes(headerSize);
        var data = reader.ReadBytes(dataSize);
        var padding = reader.RequireEnd();

        WaveFormat format;
        try
        {
            format = WaveFormat.Parse(header);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"Wave 0x{id:X8}: {e.Message}", e);
        }

        // One array for both, so a cached wave is one allocation besides the object.
        var bytes = new byte[headerSize + dataSize];
        header.CopyTo(bytes);
        data.CopyTo(bytes.AsSpan(headerSize));
        var memory = bytes.AsMemory();
        return new WaveData(id, format, memory[..headerSize], memory[headerSize..], padding);
    }

    private static int Count(uint value, uint id, string what) =>
        value <= int.MaxValue
            ? (int)value
            : throw new InvalidDataException($"Wave 0x{id:X8}: a {what} size of {value} byte(s) is not plausible.");
}
