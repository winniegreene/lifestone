namespace Lifestone.Data.World.Sky;

/// <summary>What a <see cref="SkyUpdateCadence.Tick"/> says to refresh this frame.</summary>
[Flags]
public enum SkyUpdates
{
    /// <summary>Only the sky objects (retail moves them every frame in <c>GameSky::UseTime</c>).</summary>
    None = 0,

    /// <summary>Re-read <see cref="SkyEvaluator.GetWorldFog"/> and apply it (when enabled).</summary>
    Fog = 1,

    /// <summary>Re-read <see cref="SkyEvaluator.GetLighting"/> and re-light terrain and objects.</summary>
    Lighting = 2,
}

/// <summary>
/// Retail's update cadence for fog and lighting (<c>LScape::UseTime</c>, acclient.c:307221, 11.4186).
/// </summary>
/// <remarks>
/// <code>
/// if (cur_time >= next_tick) {                         // x87 flag test; skipped when cur_time &lt; next_tick
///     next_tick = cur_time + tick_size;                // 0.8 s
///     if (cur_time > next_light_tick) {
///         GetLighting(present_time_of_day) -> clamp ambient -> set_landscape_lighting (re-lights every block)
///         next_light_tick = cur_time + light_tick_size; // 15 s
///     }
///     GetWorldFog(present_time_of_day) -> SetFFFogProperties
/// }
/// </code>
/// The clock is <c>Timer::cur_time</c> (seconds). <c>LScape::SetDay</c> (acclient.c:306897) zeroes both deadlines,
/// forcing an immediate refresh; <see cref="Reset"/> does the same. The comparison direction of the outer test is
/// read from Hex-Rays' <c>!(c0 | c2)</c> flag check on the cur_time/next_tick compare.
/// </remarks>
public sealed class SkyUpdateCadence
{
    private readonly double _tickSize;
    private readonly double _lightTickSize;

    public SkyUpdateCadence(SkyDescription sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        _tickSize = sky.TickSize;
        _lightTickSize = sky.LightTickSize;
    }

    /// <summary>Retail <c>LScape::next_tick</c>.</summary>
    public double NextTick { get; private set; }

    /// <summary>Retail <c>LScape::next_light_tick</c>.</summary>
    public double NextLightTick { get; private set; }

    /// <summary>Advances the cadence to <paramref name="clockSeconds"/> and reports what to refresh.</summary>
    public SkyUpdates Tick(double clockSeconds)
    {
        if (!(clockSeconds >= NextTick))
        {
            return SkyUpdates.None;
        }

        NextTick = _tickSize + clockSeconds;
        var updates = SkyUpdates.Fog;
        if (clockSeconds > NextLightTick)
        {
            updates |= SkyUpdates.Lighting;
            NextLightTick = _lightTickSize + clockSeconds;
        }

        return updates;
    }

    /// <summary><c>LScape::SetDay</c>: zero both deadlines so the next <see cref="Tick"/> refreshes everything.</summary>
    public void Reset()
    {
        NextTick = 0.0;
        NextLightTick = 0.0;
    }
}
