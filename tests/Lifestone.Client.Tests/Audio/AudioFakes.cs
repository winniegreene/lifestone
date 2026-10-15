using System.Buffers.Binary;
using Lifestone.Client.Audio;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Audio;

/// <summary>An <see cref="IAudioOutput"/> that records every start and stop; a voice plays until the test stops it.</summary>
internal sealed class RecordingAudioOutput : IAudioOutput
{
    private readonly bool[] _playing = new bool[RetailSoundRules.VoiceCount];

    public List<VoicePlay> Plays { get; } = [];

    public List<int> Stops { get; } = [];

    /// <summary>When false, every voice reports it has finished (as if each sound were instant).</summary>
    public bool KeepPlaying { get; set; } = true;

    public void Play(in VoicePlay play)
    {
        Plays.Add(play);
        _playing[play.Voice] = KeepPlaying;
    }

    public void Stop(int voice)
    {
        Stops.Add(voice);
        _playing[voice] = false;
    }

    public bool IsPlaying(int voice) => _playing[voice];

    /// <summary>Marks <paramref name="voice"/> as finished.</summary>
    public void Finish(int voice) => _playing[voice] = false;
}

/// <summary>An output that counts and keeps nothing (for allocation measurements).</summary>
internal sealed class CountingAudioOutput : IAudioOutput
{
    public int Plays { get; private set; }

    public VoicePlay Last { get; private set; }

    public void Play(in VoicePlay play)
    {
        Plays++;
        Last = play;
    }

    public void Stop(int voice)
    {
    }

    public bool IsPlaying(int voice) => false;
}

/// <summary>An <see cref="IAudioSource"/> over waves and tables built in code.</summary>
internal sealed class FakeAudioSource : IAudioSource
{
    public Dictionary<uint, WaveData> Waves { get; } = [];

    public Dictionary<uint, SoundTableData> Tables { get; } = [];

    /// <summary>Ids whose records are malformed: asking for one throws, as <see cref="DatAudioSource"/> does.</summary>
    public HashSet<uint> Malformed { get; } = [];

    public int WaveReads { get; private set; }

    public int TableReads { get; private set; }

    public WaveData? GetWave(uint waveId)
    {
        WaveReads++;
        return Malformed.Contains(waveId)
            ? throw new InvalidDataException($"Wave 0x{waveId:X8} is malformed (test).")
            : Waves.GetValueOrDefault(waveId);
    }

    public SoundTableData? GetSoundTable(uint soundTableId)
    {
        TableReads++;
        return Malformed.Contains(soundTableId)
            ? throw new InvalidDataException($"SoundTable 0x{soundTableId:X8} is malformed (test).")
            : Tables.GetValueOrDefault(soundTableId);
    }

    /// <summary>Adds a PCM wave (mono, 11,025 Hz, 16-bit) of <paramref name="bytes"/> sample bytes.</summary>
    public FakeAudioSource AddWave(uint id, int bytes = 64, ushort formatTag = WaveFormat.Pcm)
    {
        Waves[id] = WaveData.Decode(id, AudioRecords.Wave(id, formatTag, bytes));
        return this;
    }

    /// <summary>Adds a table whose root children are <paramref name="types"/>.</summary>
    public FakeAudioSource AddTable(uint id, params (uint Type, SoundEntry[] Entries)[] types)
    {
        Tables[id] = SoundTableData.Decode(id, AudioRecords.Table(id, types));
        return this;
    }
}

/// <summary>Raw audio records in the dats' layout.</summary>
internal static class AudioRecords
{
    public static byte[] Wave(uint id, ushort formatTag, int bytes)
    {
        var raw = new byte[12 + 18 + bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, id);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 18);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)bytes);
        var header = raw.AsSpan(12);
        BinaryPrimitives.WriteUInt16LittleEndian(header, formatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 11025);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 22050);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 16);
        return raw;
    }

    public static byte[] Table(uint id, (uint Type, SoundEntry[] Entries)[] types)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(id));
        bytes.AddRange(BitConverter.GetBytes(0u)); // the root's key
        bytes.AddRange(BitConverter.GetBytes(1u)); // the root's one default entry
        AddEntry(bytes, SoundEntry.Default);
        bytes.AddRange(BitConverter.GetBytes((uint)types.Length));
        foreach (var (type, entries) in types)
        {
            bytes.AddRange(BitConverter.GetBytes(type));
            bytes.AddRange(BitConverter.GetBytes((uint)entries.Length));
            foreach (var entry in entries)
            {
                AddEntry(bytes, entry);
            }

            bytes.AddRange(BitConverter.GetBytes(0u));
        }

        return [.. bytes];
    }

    private static void AddEntry(List<byte> bytes, SoundEntry entry)
    {
        bytes.AddRange(BitConverter.GetBytes(entry.WaveId));
        bytes.AddRange(BitConverter.GetBytes(entry.Priority));
        bytes.AddRange(BitConverter.GetBytes(entry.Probability));
        bytes.AddRange(BitConverter.GetBytes(entry.Volume));
    }
}

/// <summary>A <c>rand()</c> that hands out scripted values (then 0), counting the draws.</summary>
internal sealed class ScriptedCrt(params int[] values) : ICrtRandom
{
    private readonly Queue<int> _values = new(values);

    public int Draws { get; private set; }

    public int Rand()
    {
        Draws++;
        return _values.Count > 0 ? _values.Dequeue() : 0;
    }
}

/// <summary>A <c>RollDice</c> that hands out scripted rolls (then 0), counting the draws.</summary>
internal sealed class ScriptedDice(params double[] values) : IEffectRandom
{
    private readonly Queue<double> _values = new(values);

    public int Draws { get; private set; }

    public double RollDice(float lower, float upper)
    {
        Draws++;
        var roll = _values.Count > 0 ? _values.Dequeue() : 0.0;
        return (roll * ((double)upper - lower)) + lower;
    }
}

/// <summary>An <see cref="ISoundSink"/> that implements only the C-portal members and records them.</summary>
internal sealed class MinimalSoundSink : ISoundSink
{
    public List<string> Calls { get; } = [];

    public void PlayFromCenter(uint soundTableId, uint soundType) =>
        Calls.Add($"center 0x{soundTableId:X8} 0x{soundType:X2}");

    public void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin) =>
        Calls.Add(FormattableString.Invariant($"wave 0x{waveId:X8} {priority} {probability} {volume} {origin.Distance} {origin.BearingDegrees}"));

    public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume) =>
        Calls.Add(FormattableString.Invariant($"table 0x{tableId:X8} 0x{soundType:X2} {origin.Distance} {volume}"));
}
