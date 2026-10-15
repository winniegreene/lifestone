using Lifestone.Client.Diagnostics;
using Lifestone.Data.Audio;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Audio;

/// <summary>
/// Retail's sound manager over an <see cref="IAudioOutput"/>: the six ways retail starts a sound, each with its gate,
/// its variant choice, its probability roll, its attenuation and pan, and the 16-voice pool. It decides what plays; the
/// output plays it.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name (step PS10), retail's <c>SoundManager</c>:</para>
/// <list type="bullet">
/// <item><see cref="PlayFromCenter"/>: <c>PlaySoundFromCenter(SoundType, CSoundTable*)</c> (11.4186 0x00550950, 11.6096
/// 0x00551560), the interface sounds.</item>
/// <item><see cref="PlayWaveFromCenter"/>: <c>PlaySoundFromCenter(gid, float)</c> (11.4186 0x005509E0, 11.6096
/// 0x005515F0), the UI's sound media.</item>
/// <item><see cref="PlaySound"/>: <c>PlaySoundA(gid, CPhysicsObj*)</c> (11.4186 0x00550730, 11.6096 0x00551340), the Sound
/// hook (<c>SoundHook::Execute</c>, 11.4186 0x00526A20, 11.6096 0x00527620).</item>
/// <item><see cref="PlayWave"/>: <c>PlaySoundA(gid, CPhysicsObj*, prio, prob, vol)</c> (11.4186 0x005507A0, 11.6096
/// 0x005513B0), the SoundTweaked hook (<c>SoundTweakedHook::Execute</c>, 11.4186 0x00526A80, 11.6096 0x00527680).</item>
/// <item><see cref="PlayFromTable(uint, uint, SoundOrigin)"/>: <c>PlaySoundA(SoundType, CPhysicsObj*)</c> (11.4186
/// 0x00550B70, 11.6096 0x00551780), the SoundTable hook (<c>SoundTableHook::Execute</c>, 11.4186 0x00526AB0, 11.6096
/// 0x005276B0).</item>
/// <item><see cref="PlayFromTable(uint, uint, SoundOrigin, float)"/>: <c>PlaySoundA(SoundType, CPhysicsObj*, float)</c>
/// (11.4186 0x00550AF0, 11.6096 0x00551700), <c>CPhysicsObj::play_sound</c> (11.4186 0x0050F460, 11.6096
/// 0x0050FF30).</item>
/// </list>
/// <para>with <c>GetSound</c> (11.4186 0x00550680, 11.6096 0x00551290), <c>PlayProbability</c>, <c>GetAttenuation</c> and
/// the pan (<see cref="RetailSoundRules"/>), and <c>PlaySoundInternal</c> (<see cref="VoicePool"/>).</para>
/// <para><b>Order of each call</b>, as retail's: the category gate (<see cref="AudioLevels.InterfaceEnabled"/> for the two
/// centred entry points, <see cref="AudioLevels.EffectsEnabled"/> for the rest) and the active-window gate; for a table
/// sound, the variant (one <c>RollDice(0, 1)</c> draw on <see cref="Dice"/> whenever the type has a variant); the wave;
/// the probability roll (one <c>rand()</c> on <see cref="Crt"/>, except for <see cref="PlayWaveFromCenter"/> and
/// <see cref="PlaySound"/>, which have none); the attenuation (a centred sound is at distance 0); the pan; the
/// voice.</para>
/// <para><b>Findings</b> kept as retail has them:</para>
/// <list type="bullet">
/// <item>The interface sounds follow the effects slider; retail's interface slider is never read.</item>
/// <item>A Sound hook's wave plays at the effects slider's volume, which the attenuation then multiplies by the slider
/// again, so it scales with the slider's square.</item>
/// <item>The priority of a table entry or a SoundTweaked hook is never used: every voice stores
/// <see cref="StoredPriority"/>.</item>
/// <item>The listener is the camera (retail <c>SoundManager::SetPlayerPosition</c> from <c>SmartBox::set_viewer</c>);
/// while creature mode updates its objects (the portal tunnel), it is the creature-mode camera. The caller expresses
/// that in each <see cref="SoundOrigin"/>.</item>
/// </list>
/// <para><b>Lifestone's differences.</b> A wave is decoded the first time it is asked for, so a wave no loaded table
/// names still plays (retail's centred wave call would find nothing). A missing table, type or wave, a malformed one,
/// and a format Lifestone cannot play (<see cref="WaveData.Unsupported"/>) are a once-per-id warning on the log
/// (category <c>audio</c>), never an exception. Retail shares one <c>rand()</c> and one <c>Random</c> with the whole
/// client; here each player has its own (seeded by the host), as M1 does for effects.</para>
/// <para>Frame thread only. Allocation-free once each table and wave has been decoded and each warning given: the
/// player remembers the ids whose records failed to decode and does not ask <see cref="Source"/> for them again.</para>
/// </remarks>
public sealed class RetailSoundPlayer : ISoundSink
{
    /// <summary>
    /// The priority every voice stores: the cached sound buffer's (<c>SoundBufRef::data_.priority_</c>), which keeps
    /// <c>SoundData</c>'s default 0 (<c>SoundBufRef::SoundBufRef</c>, 11.4186 0x00550110, 11.6096 0x00550D20).
    /// </summary>
    public const float StoredPriority = 0f;

    /// <summary>The log category.</summary>
    public const string LogCategory = "audio";

    private readonly HashSet<(Problem Kind, uint Id, uint Type)> _warned = [];

    /// <summary>Tables and waves whose records failed to decode (the two id ranges do not overlap).</summary>
    private readonly HashSet<uint> _malformed = [];

    /// <summary>Creates a player.</summary>
    /// <param name="output">The voices that play.</param>
    /// <param name="source">The waves and sound tables (normally <see cref="DatAudioSource"/>).</param>
    /// <param name="dice">The variant roll (retail <c>Random::RollDice</c>; M1's <see cref="RetailRandom"/>).</param>
    /// <param name="crt">The probability roll (retail <c>rand()</c>; <see cref="MsvcCrtRandom"/>).</param>
    /// <param name="levels">The preferences; <see cref="AudioLevels.Default"/> when null.</param>
    /// <param name="log">Receives the once-per-id warnings; null for none.</param>
    public RetailSoundPlayer(IAudioOutput output, IAudioSource source, IEffectRandom dice, ICrtRandom crt, AudioLevels? levels = null, ILogSink? log = null)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Dice = dice ?? throw new ArgumentNullException(nameof(dice));
        Crt = crt ?? throw new ArgumentNullException(nameof(crt));
        Levels = levels ?? AudioLevels.Default;
        Log = log ?? NullLogSink.Instance;
    }

    private enum Problem : byte
    {
        MissingTable,
        MissingType,
        MissingWave,
        BadRecord,
        Unsupported,
    }

    /// <summary>The voices that play.</summary>
    public IAudioOutput Output { get; }

    /// <summary>The waves and sound tables.</summary>
    public IAudioSource Source { get; }

    /// <summary>The variant roll.</summary>
    public IEffectRandom Dice { get; }

    /// <summary>The probability roll.</summary>
    public ICrtRandom Crt { get; }

    /// <summary>The log.</summary>
    public ILogSink Log { get; }

    /// <summary>The preferences, read on every call.</summary>
    public AudioLevels Levels { get; set; }

    /// <summary>The application's window is active (retail <c>Device::m_bIsActiveApp</c>); matters with <see cref="AudioLevels.PlayOnlyWhenActive"/>.</summary>
    public bool ApplicationActive { get; set; } = true;

    /// <summary>The 16 voices.</summary>
    public VoicePool Voices { get; } = new();

    /// <summary>Calls made.</summary>
    public int Requested { get; private set; }

    /// <summary>Sounds handed to the output.</summary>
    public int Played { get; private set; }

    /// <summary>Sounds dropped because every voice was busy with a higher priority (never, with retail's priorities).</summary>
    public int Dropped { get; private set; }

    /// <summary>Sounds the probability roll or the attenuation (quieter than −50 dB) kept silent.</summary>
    public int Silent { get; private set; }

    /// <summary>Calls refused by a disabled category or an inactive window.</summary>
    public int Disabled { get; private set; }

    /// <summary>Calls that found no table, type, variant or playable wave.</summary>
    public int Missing { get; private set; }

    /// <inheritdoc/>
    public void PlayFromCenter(uint soundTableId, uint soundType)
    {
        Requested++;
        if (!Allowed(Levels.InterfaceEnabled))
        {
            return;
        }

        if (GetSound(soundTableId, soundType, out var entry) && Roll(entry.Probability))
        {
            Start(entry.WaveId, SoundOrigin.Center, entry.Volume, AudioBus.Ui);
        }
    }

    /// <inheritdoc/>
    public void PlayWaveFromCenter(uint waveId, float volume)
    {
        Requested++;
        if (Allowed(Levels.InterfaceEnabled) && HasWave(waveId))
        {
            Start(waveId, SoundOrigin.Center, volume, AudioBus.Ui);
        }
    }

    /// <inheritdoc/>
    public void PlaySound(uint waveId, SoundOrigin origin)
    {
        Requested++;
        if (Allowed(Levels.EffectsEnabled) && HasWave(waveId))
        {
            Start(waveId, origin, Levels.EffectVolume, AudioBus.Effects);
        }
    }

    /// <inheritdoc/>
    /// <remarks><paramref name="priority"/> is not used, as in retail (<see cref="StoredPriority"/>).</remarks>
    public void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin)
    {
        Requested++;
        if (Allowed(Levels.EffectsEnabled) && HasWave(waveId) && Roll(probability))
        {
            Start(waveId, origin, volume, AudioBus.Effects);
        }
    }

    /// <inheritdoc/>
    public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin)
    {
        Requested++;
        if (Allowed(Levels.EffectsEnabled) && GetSound(tableId, soundType, out var entry) && Roll(entry.Probability))
        {
            Start(entry.WaveId, origin, entry.Volume, AudioBus.Effects);
        }
    }

    /// <inheritdoc/>
    public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume)
    {
        Requested++;
        if (Allowed(Levels.EffectsEnabled) && GetSound(tableId, soundType, out var entry) && Roll(entry.Probability))
        {
            Start(entry.WaveId, origin, volume, AudioBus.Effects);
        }
    }

    /// <summary>Stops every voice and forgets them (a host's teardown; retail never stops its voices).</summary>
    public void StopAll()
    {
        for (var voice = 0; voice < RetailSoundRules.VoiceCount; voice++)
        {
            if (Voices.IsOccupied(voice))
            {
                Output.Stop(voice);
            }
        }

        Voices.Reset();
    }

    /// <summary>The category gate and <c>s_bPlaySoundOnlyWhenActive</c>.</summary>
    private bool Allowed(bool categoryEnabled)
    {
        if (categoryEnabled && (!Levels.PlayOnlyWhenActive || ApplicationActive))
        {
            return true;
        }

        Disabled++;
        return false;
    }

    /// <summary><c>SoundManager::GetSound</c>: the table, the type, the variant, then the wave.</summary>
    private bool GetSound(uint tableId, uint soundType, out SoundEntry entry)
    {
        entry = default;
        var table = Table(tableId);
        if (table is null)
        {
            return false;
        }

        var variants = table.Lookup(soundType);
        if (variants is null || variants.Count == 0)
        {
            Missing++;
            Warn(Problem.MissingType, tableId, soundType, "the sound table has no variant of this sound type");
            return false;
        }

        var index = RetailSoundRules.PickVariant(variants.Count, Dice.RollDice(0f, 1f));
        if (index < 0)
        {
            Missing++;
            return false;
        }

        entry = variants[index];
        if (entry.WaveId == 0)
        {
            // Retail's GetSound returns nothing for an entry without a wave.
            Missing++;
            return false;
        }

        return HasWave(entry.WaveId);
    }

    private SoundTableData? Table(uint tableId)
    {
        if (_malformed.Contains(tableId))
        {
            Missing++; // warned when it failed
            return null;
        }

        SoundTableData? table;
        try
        {
            table = Source.GetSoundTable(tableId);
        }
        catch (InvalidDataException e)
        {
            Missing++;
            _malformed.Add(tableId);
            Warn(Problem.BadRecord, tableId, 0, e.Message);
            return null;
        }

        if (table is null)
        {
            Missing++;
            Warn(Problem.MissingTable, tableId, 0, "the sound table does not exist");
        }

        return table;
    }

    /// <summary>The wave exists and can be played (retail: it is in <c>sound_hash_</c>).</summary>
    private bool HasWave(uint waveId)
    {
        if (_malformed.Contains(waveId))
        {
            Missing++; // warned when it failed
            return false;
        }

        WaveData? wave;
        try
        {
            wave = Source.GetWave(waveId);
        }
        catch (InvalidDataException e)
        {
            Missing++;
            _malformed.Add(waveId);
            Warn(Problem.BadRecord, waveId, 0, e.Message);
            return false;
        }

        if (wave is null)
        {
            Missing++;
            Warn(Problem.MissingWave, waveId, 0, "the wave does not exist");
            return false;
        }

        if (wave.Unsupported is { } unsupported)
        {
            Missing++;
            Warn(Problem.Unsupported, waveId, 0, unsupported);
            return false;
        }

        return true;
    }

    /// <summary><c>SoundManager::PlayProbability</c> on the next <c>rand()</c>.</summary>
    private bool Roll(float probability)
    {
        if (RetailSoundRules.PlayProbability(Crt.Rand(), probability))
        {
            return true;
        }

        Silent++;
        return false;
    }

    /// <summary><c>PlaySoundInternal</c>: the attenuation, the pan and a voice.</summary>
    private void Start(uint waveId, in SoundOrigin origin, float volume, AudioBus bus)
    {
        var levels = Levels;
        if (!RetailSoundRules.TryAttenuation(origin.Distance, volume, levels.EffectVolume, out var decibels))
        {
            Silent++;
            return;
        }

        var pan = RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, levels.Mono);
        var voice = Voices.Acquire(StoredPriority, Output);
        if (voice < 0)
        {
            Dropped++;
            return;
        }

        Played++;
        Output.Play(new VoicePlay(voice, waveId, pan, decibels, bus));
    }

    private void Warn(Problem kind, uint id, uint type, string why)
    {
        if (!_warned.Add((kind, id, type)) || !Log.IsEnabled(LogSeverity.Warning))
        {
            return;
        }

        Log.Warn(
            LogCategory,
            kind == Problem.MissingType
                ? $"sound type 0x{type:X2} of sound table 0x{id:X8} cannot play: {why} (warned once)"
                : $"0x{id:X8} cannot play: {why} (warned once)");
    }
}
