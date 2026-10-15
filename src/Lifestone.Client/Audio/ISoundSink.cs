using Lifestone.Data.World;

namespace Lifestone.Client.Audio;

/// <summary>
/// Where a sound comes from, relative to the listener. Retail's sound is 2D: only the distance (attenuation) and the
/// bearing (stereo pan) matter (<c>SoundManager::PlaySoundInternal</c> with a position, 11.4186 0x00550170, 11.6096
/// 0x00550D80). Build one with <see cref="Between"/>, which applies retail's convention for the bearing.
/// </summary>
/// <param name="Distance">Metres from the listener (retail <c>Position::distance</c> to the player's sound position).</param>
/// <param name="BearingDegrees">
/// <para>The compass heading from the <b>source to the listener</b> minus the listener's own heading, in degrees, any
/// range: retail's <c>Position::heading(source, listener) − Frame::get_heading(listener)</c>, where
/// <c>Position::heading</c> measures the offset listener − source (<c>Position::get_offset</c>). Equivalently, the
/// bearing from the listener to the source, relative to the listener's heading, plus 180°. The rules take it modulo
/// 360.</para>
/// <para>Example: a listener facing north (heading 0) with a source 10 m to its east. The source-to-listener heading is
/// 270 (west), so the bearing is 270 (or −90), the pan index is +14 and the left channel is lowered: the sound is heard
/// on the right. Passing the listener-to-source bearing (90) instead would mirror every positioned sound.</para>
/// </param>
public readonly record struct SoundOrigin(float Distance, float BearingDegrees)
{
    /// <summary>Retail's small-vector length for headings (<c>AC1Legacy::Vector3::normalize_check_small</c>, 0.0002).</summary>
    private const double SmallHeadingLength = 0.0002;

    /// <summary>Retail's radians-to-degrees factor in <c>Position::heading</c>.</summary>
    private const double RadiansToDegrees = 57.29577951308232;

    /// <summary>The listener's own position: full volume, centred (retail <c>PlaySoundFromCenter</c> uses distance 0, pan 0).</summary>
    public static SoundOrigin Center => default;

    /// <summary>
    /// The origin of a sound at <paramref name="source"/> heard from <paramref name="listener"/>, as the positioned branch
    /// of retail <c>SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)</c> (11.4186 0x00550170, 11.6096
    /// 0x00550D80) derives it. Use this rather than deriving the bearing by hand: its direction is the one retail's pan
    /// needs (see <see cref="BearingDegrees"/>).
    /// </summary>
    /// <remarks>
    /// <para>Re-expresses, by function name: <c>Position::distance(source, listener)</c> (11.4186 0x005A94B0, 11.6096
    /// 0x005AA560), the length of the offset; <c>Position::heading(source, listener)</c> (11.4186 0x005A9520, 11.6096
    /// 0x005AA5D0), the compass heading <c>fmod(450 − atan2(y, x) · 180/π, 360)</c> of the offset's horizontal part, 0
    /// when that part is shorter than 0.0002 (<c>AC1Legacy::Vector3::normalize_check_small</c>, 11.4186 0x00452460,
    /// 11.6096 0x004524A0); both over <c>Position::get_offset(source, listener)</c> (an inline copy per object file, e.g.
    /// 11.4186 0x00509F60, 11.6096 0x0050AA30): the landblock offset from the source's landblock to the listener's plus
    /// the listener's origin minus the source's, so a landblock seam is continuous. The decompile prints
    /// <c>atan2(x, y)</c>; the arguments are read as <c>atan2(y, x)</c>, the order that makes north 0 and east 90 as in
    /// <c>Frame::get_heading</c> (11.4186 0x00535760, 11.6096 0x005364A0; <see cref="CellFrame.HeadingDegrees"/>) and
    /// ACE.</para>
    /// <para>Retail holds the distance, the heading and the listener's heading as floats and subtracts the two headings
    /// before its <c>fmod</c>; the difference is rounded to a float here because <see cref="BearingDegrees"/> is one.
    /// Allocation-free.</para>
    /// </remarks>
    /// <param name="listener">Retail's <c>SoundManager::player_position_</c>: the camera (docs/portal-space.md §7.3).</param>
    /// <param name="source">The sounding object's position.</param>
    public static SoundOrigin Between(in CellFrame listener, in CellFrame source)
    {
        var offset = LandDefs.GetBlockOffset(source.CellId, listener.CellId) + listener.Origin - source.Origin;
        double x = offset.X;
        double y = offset.Y;
        double z = offset.Z;
        var distance = (float)Math.Sqrt((x * x) + (y * y) + (z * z));

        var horizontal = Math.Sqrt((x * x) + (y * y));
        var heading = horizontal < SmallHeadingLength
            ? 0f
            : (float)((450.0 - (Math.Atan2(y / horizontal, x / horizontal) * RadiansToDegrees)) % 360.0);
        var listenerHeading = (float)listener.HeadingDegrees;
        return new SoundOrigin(distance, (float)((double)heading - listenerHeading));
    }
}

/// <summary>
/// The sound façade the rest of Lifestone plays through, with retail's rules (variant choice, probability roll,
/// attenuation in whole dB, pan, 16 voices): the retail UI's sounds, portal space's whooshes and hum, and later the
/// world's animation sounds. <see cref="RetailSoundPlayer"/> implements it over an <see cref="IAudioOutput"/>.
/// </summary>
/// <remarks>
/// <para>Ids are dat ids: a SoundTable (0x20xxxxxx) and a <c>Sound</c> type (DatReaderWriter's <c>Sound</c> enum, e.g.
/// <c>UI_EnterPortal</c> 0x6A), or a Wave (0x0Axxxxxx). Called on the frame thread. A missing table, type or wave is a
/// once-per-id warning, never an exception.</para>
/// <para><b>Retail's six entry points.</b> The first three members are the C-portal contract; step PS10 added the other
/// three of retail's sound manager (<see cref="PlayWaveFromCenter"/>, <see cref="PlaySound"/> and the volume-less
/// <see cref="PlayFromTable(uint, uint, SoundOrigin)"/>) as default members, so an existing implementation (a test's
/// recorder) still compiles; <see cref="RetailSoundPlayer"/> implements all six with retail's rules, and a recorder that
/// cares overrides them.</para>
/// </remarks>
public interface ISoundSink
{
    /// <summary>
    /// A UI sound: one entry of <paramref name="soundType"/> in table <paramref name="soundTableId"/>, centred, at the
    /// entry's volume (retail <c>SoundManager::PlaySoundFromCenter(SoundType, CSoundTable*)</c>, 11.4186 0x00550950,
    /// 11.6096 0x00551560).
    /// </summary>
    void PlayFromCenter(uint soundTableId, uint soundType);

    /// <summary>
    /// One wave with a hook's tweaks, from <paramref name="origin"/> (the SoundTweaked hook; retail
    /// <c>SoundManager::PlaySoundA(gid, obj, prio, prob, vol)</c>, 11.4186 0x005507A0, 11.6096 0x005513B0).
    /// </summary>
    void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin);

    /// <summary>
    /// One entry of <paramref name="soundType"/> in table <paramref name="tableId"/> (an object's sound table), from
    /// <paramref name="origin"/> at <paramref name="volume"/> (retail <c>SoundManager::PlaySoundA(SoundType, obj, volume)</c>,
    /// 11.4186 0x00550AF0, 11.6096 0x00551700).
    /// </summary>
    void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume);

    /// <summary>
    /// One wave, centred, at <paramref name="volume"/>, with no probability roll (an interface sound; retail
    /// <c>SoundManager::PlaySoundFromCenter(gid, volume)</c>, 11.4186 0x005509E0, 11.6096 0x005515F0, which the UI's sound
    /// media use). The default forwards to <see cref="PlayWave"/> with probability 1.
    /// </summary>
    void PlayWaveFromCenter(uint waveId, float volume) => PlayWave(waveId, 0f, 1f, volume, SoundOrigin.Center);

    /// <summary>
    /// One wave from <paramref name="origin"/> with no tweaks and no probability roll (the plain Sound hook; retail
    /// <c>SoundManager::PlaySoundA(gid, obj)</c>, 11.4186 0x00550730, 11.6096 0x00551340). The default forwards to
    /// <see cref="PlayWave"/> with probability and volume 1.
    /// </summary>
    void PlaySound(uint waveId, SoundOrigin origin) => PlayWave(waveId, 0f, 1f, 1f, origin);

    /// <summary>
    /// One entry of <paramref name="soundType"/> in table <paramref name="tableId"/> (an object's sound table), from
    /// <paramref name="origin"/> at the entry's own volume (the SoundTable hook; retail <c>SoundManager::PlaySoundA(SoundType,
    /// obj)</c>, 11.4186 0x00550B70, 11.6096 0x00551780). The default forwards to the overload with volume 1.
    /// </summary>
    void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin) => PlayFromTable(tableId, soundType, origin, 1f);
}
