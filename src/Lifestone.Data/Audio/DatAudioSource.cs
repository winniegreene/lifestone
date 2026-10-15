using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using DatSoundTable = DatReaderWriter.DBObjs.SoundTable;
using DatWave = DatReaderWriter.DBObjs.Wave;

namespace Lifestone.Data.Audio;

/// <summary>Where decoded waves and sound tables come from (normally <see cref="DatAudioSource"/>; tests fake it).</summary>
public interface IAudioSource
{
    /// <summary>The Wave <paramref name="waveId"/>, or null when it does not exist.</summary>
    /// <exception cref="InvalidDataException">The record exists but is malformed.</exception>
    WaveData? GetWave(uint waveId);

    /// <summary>The SoundTable <paramref name="soundTableId"/>, or null when it does not exist.</summary>
    /// <exception cref="InvalidDataException">The record exists but is malformed.</exception>
    SoundTableData? GetSoundTable(uint soundTableId);
}

/// <summary>
/// Thread-safe cache of the Waves (0x0A) and SoundTables (0x20) of the user's portal dat, decoded by Lifestone's own
/// decoders (<see cref="WaveData.Decode"/>, <see cref="SoundTableData.Decode"/>) from the raw records, which are read
/// under the dat lock and parsed outside it.
/// </summary>
/// <remarks>
/// <para>Retail loads every wave a sound table or an animation hook names when that table or hook is unpacked
/// (<c>SoundManager::CreateSound</c>, 11.4186 0x00550BF0, 11.6096 0x00551800); Lifestone decodes a wave the first time
/// it is asked for, so a wave no table names (retail's <c>PlaySoundFromCenter(gid, volume)</c> would not find it) still
/// plays. An id outside its type's range (0x0Axxxxxx, 0x20xxxxxx) is reported as absent.</para>
/// <para>Every outcome is cached, a malformed record's included: the record is read and decoded once, and every request
/// for it throws a new <see cref="InvalidDataException"/> with the decoder's message (the decoder's exception inside it)
/// without reading or decoding it again.</para>
/// </remarks>
public sealed class DatAudioSource(DatSet dats) : IAudioSource
{
    /// <summary>The high byte of every Wave id.</summary>
    public const uint WaveIdPrefix = 0x0A;

    /// <summary>The high byte of every SoundTable id.</summary>
    public const uint SoundTableIdPrefix = 0x20;

    private readonly AudioRecordCache<WaveData> _waves = new();
    private readonly AudioRecordCache<SoundTableData> _tables = new();

    /// <summary>The dats.</summary>
    public DatSet Dats { get; } = dats ?? throw new ArgumentNullException(nameof(dats));

    /// <inheritdoc/>
    public WaveData? GetWave(uint waveId) =>
        waveId >> 24 != WaveIdPrefix
            ? null
            : _waves.Get(waveId, static (id, dats) => DatRaw.Portal(dats, id) is { } raw ? WaveData.Decode(id, raw) : null, Dats);

    /// <inheritdoc/>
    public SoundTableData? GetSoundTable(uint soundTableId) =>
        soundTableId >> 24 != SoundTableIdPrefix
            ? null
            : _tables.Get(soundTableId, static (id, dats) => DatRaw.Portal(dats, id) is { } raw ? SoundTableData.Decode(id, raw) : null, Dats);

    /// <summary>Every Wave id in the portal, ascending.</summary>
    public IReadOnlyList<uint> AllWaveIds() => DatRaw.PortalIds<DatWave>(Dats);

    /// <summary>Every SoundTable id in the portal, ascending.</summary>
    public IReadOnlyList<uint> AllSoundTableIds() => DatRaw.PortalIds<DatSoundTable>(Dats);
}
