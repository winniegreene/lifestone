namespace Lifestone.Client.Audio;

/// <summary>
/// Retail's sound preferences that decide what plays and how loud (<c>SoundManager::InitPrefs</c>, 11.4186 0x005503F0,
/// 11.6096 0x00551000). <see cref="RetailSoundPlayer"/> reads them on every call.
/// </summary>
/// <remarks>
/// <para><b>Retail's preferences and what reads them.</b> <c>Sound.SoundVolume</c> is the effects slider
/// (<see cref="EffectVolume"/>); every sound, the interface's included, is scaled by it. <c>Sound.InterfaceSoundVolume</c>
/// is registered but never read, so it has no field here. <c>Sound.SoundDisabled</c>, <c>Sound.InterfaceSoundDisabled</c>
/// and <c>Sound.AmbientSoundDisabled</c> gate whole categories at their entry points (despite the names, the flags they
/// set are "enabled" flags). <c>Sound.SoundFeatures</c> chooses Stereo (0) or Mono (1). <c>Sound.PlaySoundOnlyWhenActive</c>
/// silences new sounds while the window is not active. The ambient sounds and their slider are not in M2.</para>
/// <para><b>Defaults</b> are Lifestone's (full volume, everything enabled, stereo, play while inactive): retail reads its
/// values from the user's preferences, and its compiled-in initial values were not read.</para>
/// </remarks>
/// <param name="EffectVolume">The effects slider, 0 to 1 (retail <c>effect_sound_volume</c>).</param>
/// <param name="EffectsEnabled">World and animation sounds play (retail <c>effect_sounds_enabled</c>).</param>
/// <param name="InterfaceEnabled">Interface sounds play (retail <c>interface_sounds_enabled</c>).</param>
/// <param name="Mono">Every sound is centred (retail <c>s_SoundFeatures</c> = 1).</param>
/// <param name="PlayOnlyWhenActive">New sounds are skipped while the application is not active (retail <c>s_bPlaySoundOnlyWhenActive</c>).</param>
public readonly record struct AudioLevels(float EffectVolume, bool EffectsEnabled, bool InterfaceEnabled, bool Mono, bool PlayOnlyWhenActive)
{
    /// <summary>Full volume, everything enabled, stereo, playing while inactive.</summary>
    public static AudioLevels Default => new(1f, EffectsEnabled: true, InterfaceEnabled: true, Mono: false, PlayOnlyWhenActive: false);
}

/// <summary>
/// Lifestone's master volume (not retail's): the host applies it to its master bus after the retail rules, so it never
/// changes what plays, only how loud everything is. G1 maps <c>--volume</c> and <c>--mute</c> to it (D8: automated
/// runs are silent unless a volume is given).
/// </summary>
/// <param name="MasterVolume">Linear, 0 to 1.</param>
/// <param name="Muted">Silence everything.</param>
public sealed record AudioSettings(float MasterVolume, bool Muted)
{
    /// <summary>The level a muted or silent master bus is set to, in decibels (a host's floor for "off").</summary>
    public const float SilentDecibels = -80f;

    /// <summary>Full volume, not muted.</summary>
    public static readonly AudioSettings Default = new(1f, false);

    /// <summary>A muted master bus.</summary>
    public static readonly AudioSettings Mute = new(1f, true);

    private readonly float _masterVolume = Check(MasterVolume);

    /// <summary>The master volume, checked: a finite value from 0 to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside [0, 1] or not a number.</exception>
    public float MasterVolume
    {
        get => _masterVolume;
        init => _masterVolume = Check(value);
    }

    /// <summary>The master bus's linear gain: 0 when muted.</summary>
    public float Gain => Muted ? 0f : MasterVolume;

    /// <summary>The master bus's level in decibels: 20·log₁₀(gain), or <see cref="SilentDecibels"/> when muted or silent.</summary>
    public float MasterDecibels => Gain <= 0f ? SilentDecibels : Math.Max(SilentDecibels, 20f * MathF.Log10(Gain));

    private static float Check(float volume) =>
        float.IsFinite(volume) && volume is >= 0f and <= 1f
            ? volume
            : throw new ArgumentOutOfRangeException(nameof(MasterVolume), volume, "The master volume is a linear value from 0 to 1.");
}
