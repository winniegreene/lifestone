namespace Lifestone.Client.Audio;

/// <summary>
/// Retail's sound arithmetic, as pure functions: which variant of a sound plays, whether it plays at all, how loud, and
/// where in the stereo field. <see cref="RetailSoundPlayer"/> applies them in retail's order.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name (step PS10):</para>
/// <list type="bullet">
/// <item><see cref="PickVariant"/>: the variant choice of <c>SoundManager::GetSound</c> (11.4186 0x00550680, 11.6096
/// 0x00551290), over <c>Random::RollDice(float, float)</c> (11.4186 0x0042C600, 11.6096 0x0042C860).</item>
/// <item><see cref="PlayProbability"/>: <c>SoundManager::PlayProbability</c> (11.4186 0x005500E0, 11.6096
/// 0x00550CF0).</item>
/// <item><see cref="TryAttenuation"/>: <c>SoundManager::GetAttenuation</c> (11.4186 0x00550020, 11.6096
/// 0x00550C30).</item>
/// <item><see cref="PanIndex"/>: the pan of <c>SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)</c>
/// (11.4186 0x00550170, 11.6096 0x00550D80).</item>
/// <item><see cref="ChannelGains"/>: what <c>SoundBuf::Play</c> (11.4186 0x00552D50, 11.6096 0x00553970) asks of
/// DirectSound: a pan and a volume in hundredths of a decibel.</item>
/// </list>
/// <para><b>Findings</b> the rules pin (m2-log-in.md §4.10 corrections):</para>
/// <list type="bullet">
/// <item>The last of n ≥ 2 variants never plays: retail scales a roll in [0, 1) by n − 1 and truncates. The end-of-retail
/// portal has one such sound (Swoosh2 of SoundTable 0x200000A8).</item>
/// <item>The probability roll uses the C runtime's <c>rand()</c>, not retail's <c>Random</c>, with a float scale of
/// 1/32767 (the decompile prints 0.000030518509; not 2⁻¹⁵). A probability of 1 always plays.</item>
/// <item>Attenuation is in whole decibels, rounded up, and a sound quieter than <see cref="VolMin"/> (−50 dB) does not
/// play at all. Beyond 5 m it falls with the square of the distance (25·volume / d²); nearer, only the volume counts. The
/// effects slider multiplies the gain inside the logarithm, after the cap at 1.</item>
/// <item>Sound is 2D: the pan is the only direction cue, and only from 5 m on (retail creates every buffer without 3D
/// control). A mono setting (<c>Sound.SoundFeatures</c> = 1) centres everything.</item>
/// <item>The pan index never reaches ±15: retail's degrees-to-radians float is a hair under π/180, so the sine of a
/// float bearing never rounds to 1, and the truncation stops at ±14.</item>
/// </list>
/// <para>Static, deterministic and allocation-free.</para>
/// </remarks>
public static class RetailSoundRules
{
    /// <summary>The number of voices (retail <c>SoundManager::playing_sounds_[16]</c>).</summary>
    public const int VoiceCount = 16;

    /// <summary><c>SoundManager::VOL_MIN</c>: the quietest attenuation that still plays, in decibels.</summary>
    public const int VolMin = -50;

    /// <summary><c>VOL_MIN_DIST_SQ</c>: 25, the square of the distance (5 m) from which a sound falls off.</summary>
    public const double VolMinDistanceSquared = 25.0;

    /// <summary>The distance from which a sound falls off and pans (5 m).</summary>
    public const double FalloffDistance = 5.0;

    /// <summary>The largest pan index, either side (<c>SoundBuf::Play</c> clamps to ±15).</summary>
    public const int MaxPan = 15;

    /// <summary>Decibels per doubling of the gain: 20·log₁₀(2), as retail writes it.</summary>
    public const double DecibelsPerDoubling = 6.0206;

    /// <summary>1 / ln 2, retail's <c>INV_LOG_OF_2</c> (its log₂ is ln over ln 2).</summary>
    public const double InverseLogOf2 = 1.4426950408889634;

    /// <summary>
    /// The probability roll's scale: the float nearest 1/32767, 2⁻¹⁵·(1 + 2⁻¹⁵) (the decompile prints 0.000030518509).
    /// <c>rand()</c> 32767 scales to 1 − 2⁻³⁰, just under 1.
    /// </summary>
    public const float PlayProbabilityScale = 3.0518509E-05f;

    /// <summary>Retail's degrees-to-radians factor, a float (the decompile prints 0.017453292).</summary>
    public const float DegreesToRadians = 0.017453292f;

    /// <summary>The <c>Sound.SoundFeatures</c> value that means mono ("Stereo" is 0).</summary>
    public const uint MonoSoundFeatures = 1;

    /// <summary>
    /// Which of <paramref name="count"/> variants plays for a roll <paramref name="roll01"/> in [0, 1) (retail
    /// <c>RollDice(0, 1)</c>): ⌊(count − 1) · roll⌋, so the last of several variants is never chosen. Returns −1 when
    /// there is nothing to pick (count ≤ 0, or a roll outside [0, 1) that lands past the end).
    /// </summary>
    public static int PickVariant(int count, double roll01)
    {
        if (count <= 0)
        {
            return -1;
        }

        var index = (long)((double)(count - 1) * roll01);
        return (uint)index < (uint)count ? (int)index : -1;
    }

    /// <summary>
    /// Whether a sound of <paramref name="probability"/> plays for the C runtime's <paramref name="crtRand"/> (0 to
    /// 32767): <c>rand() · scale &lt; probability</c>, strictly.
    /// </summary>
    public static bool PlayProbability(int crtRand, float probability) =>
        crtRand * (double)PlayProbabilityScale < probability;

    /// <summary>
    /// Retail's attenuation of a sound at <paramref name="distance"/> metres with <paramref name="volume"/> (linear) under
    /// the effects slider <paramref name="slider"/> (linear, retail <c>effect_sound_volume</c>): whole decibels, 0 or
    /// below.
    /// </summary>
    /// <param name="distance">Metres from the listener; 0 for a centred sound.</param>
    /// <param name="volume">The sound's volume (an entry's, a hook's, or the caller's).</param>
    /// <param name="slider">The effects slider (interface sounds follow it too).</param>
    /// <param name="decibels">The attenuation; <see cref="VolMin"/> when the sound does not play.</param>
    /// <returns>True when the sound plays: the gain is positive and the attenuation is <see cref="VolMin"/> or louder.</returns>
    public static bool TryAttenuation(float distance, float volume, float slider, out int decibels)
    {
        var gain = distance >= FalloffDistance
            ? VolMinDistanceSquared * volume / ((double)distance * distance)
            : volume;
        if (gain > 1.0)
        {
            gain = 1.0;
        }

        var scaled = gain * slider;
        if (scaled > 0.0)
        {
            decibels = (int)Math.Ceiling(Math.Log(scaled) * InverseLogOf2 * DecibelsPerDoubling);
            if (decibels >= VolMin)
            {
                return true;
            }
        }

        decibels = VolMin;
        return false;
    }

    /// <summary>
    /// Retail's pan index of a sound at <paramref name="distance"/> metres whose bearing relative to the listener's
    /// heading is <paramref name="bearingDegrees"/>: sin(bearing) · −15, truncated toward 0, so −14 to 14 in practice
    /// (see the remarks); 0 nearer than 5 m (the whole metres of the distance are compared) and 0 in mono. A positive
    /// index lowers the left channel (<see cref="ChannelGains"/>): the sound is on the listener's right.
    /// </summary>
    /// <param name="distance">Metres from the listener.</param>
    /// <param name="bearingDegrees">
    /// The compass heading from the <b>source to the listener</b> minus the listener's heading (retail
    /// <c>Position::heading(source, listener) − Frame::get_heading(listener)</c>, where <c>Position::heading</c>
    /// measures the offset listener − source); equivalently the listener-to-source bearing + 180°. A source on the right
    /// of the listener gives −90 (or 270) and a positive pan. <see cref="SoundOrigin.Between"/> computes it. Any range:
    /// it is taken modulo 360 (keeping its sign, as C's <c>fmod</c>) and then into (−360, 180].
    /// </param>
    /// <param name="mono">The mono setting.</param>
    public static int PanIndex(float distance, float bearingDegrees, bool mono)
    {
        if (mono)
        {
            return 0;
        }

        var delta = bearingDegrees % 360.0;
        if (delta > 180.0)
        {
            delta -= 360.0;
        }

        if (Math.Abs((long)distance) < FalloffDistance)
        {
            return 0;
        }

        return (int)(Math.Sin(delta * DegreesToRadians) * -MaxPan);
    }

    /// <summary>
    /// The linear left and right gains of a voice that DirectSound would play for <paramref name="pan"/> and
    /// <paramref name="attenuationDb"/> (<c>SoundBuf::Play</c> sets the pan and the volume to 100 times them, in
    /// hundredths of a decibel). A pan below 0 lowers the right channel by |pan| dB, above 0 the left by pan dB; the
    /// volume lowers both. Values outside ±15 and below −50 dB are clamped, as <c>SoundBuf::Play</c> does.
    /// </summary>
    public static (float Left, float Right) ChannelGains(int pan, int attenuationDb)
    {
        pan = Math.Clamp(pan, -MaxPan, MaxPan);
        var volume = Math.Max(attenuationDb, VolMin);
        var left = volume - Math.Max(pan, 0);
        var right = volume + Math.Min(pan, 0);
        return ((float)Math.Pow(10.0, left / 20.0), (float)Math.Pow(10.0, right / 20.0));
    }
}
