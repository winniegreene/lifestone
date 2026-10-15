namespace Lifestone.Client.Audio;

/// <summary>The mixer bus a voice plays on.</summary>
public enum AudioBus : byte
{
    /// <summary>Interface sounds (retail <c>PlaySoundFromCenter</c>; they follow the effects slider, as retail's do).</summary>
    Ui,

    /// <summary>World and animation sounds.</summary>
    Effects,
}

/// <summary>One start of a voice, as retail's sound manager hands it to the sound card.</summary>
/// <param name="Voice">The voice slot, 0 to 15 (retail's 16 <c>playing_sounds_</c>); a slot still playing is stopped first.</param>
/// <param name="WaveId">The Wave (0x0Axxxxxx) to play once, from its start.</param>
/// <param name="Pan">
/// Retail's pan index, sin(bearing) · −15 truncated, so −15 to 15, 0 for a centred sound (<c>SoundBuf::Play</c>'s pan
/// argument). Retail hands DirectSound 100 times it as the pan in hundredths of a decibel: below 0 the right channel is
/// lowered by |pan| dB, above 0 the left by pan dB. <see cref="RetailSoundRules.ChannelGains"/> gives the two linear
/// gains.
/// </param>
/// <param name="AttenuationDb">
/// Whole decibels, 0 (full) down to retail's <c>VOL_MIN</c> −50, applied to both channels (retail hands DirectSound 100
/// times it as the volume in hundredths of a decibel).
/// </param>
/// <param name="Bus">The bus.</param>
public readonly record struct VoicePlay(int Voice, uint WaveId, int Pan, int AttenuationDb, AudioBus Bus);

/// <summary>
/// The sound card: 16 voices that play decoded waves. Implemented by the host (the Godot <c>AudioService</c> in wave 2;
/// a recorder in tests); the retail rules that decide what plays live in <see cref="RetailSoundPlayer"/>.
/// </summary>
/// <remarks>
/// Called on the frame thread. Wave bytes come from the dats (<see cref="Lifestone.Data.Audio.DatAudioSource"/>), by
/// id.
/// </remarks>
public interface IAudioOutput
{
    /// <summary>Starts <paramref name="play"/>'s wave on its voice, stopping whatever that voice was playing.</summary>
    void Play(in VoicePlay play);

    /// <summary>Stops voice <paramref name="voice"/> (nothing when it is idle).</summary>
    void Stop(int voice);

    /// <summary>True while voice <paramref name="voice"/> is still playing (retail <c>SoundBuf::GetStatus</c> &amp; playing).</summary>
    bool IsPlaying(int voice);
}
