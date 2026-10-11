using Lifestone.Client.Navigation;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Sky;

/// <summary>
/// Where the game clock starts and how fast it runs, from the command line (docs/sky.md §2).
/// </summary>
/// <param name="GameTime">Start game time, or null for the live server clock.</param>
/// <param name="Speed">Start speed (0 = paused, 1 = retail).</param>
/// <param name="DayFraction">A day fraction to jump to after starting (interactive <c>--time-of-day</c>: the fraction
/// of today's live date), or null.</param>
public readonly record struct SkyClockStart(double? GameTime, double Speed, double? DayFraction)
{
    /// <summary>The day fraction automated runs use without <c>--time-of-day</c>: Midsong, retail's midday.</summary>
    public const double DefaultAutomatedDayFraction = 0.5;

    /// <summary>
    /// The start for a run.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Interactive runs</b> follow retail: the live server clock at speed 1. <c>--game-time</c> starts there
    /// instead; <c>--time-of-day=F</c> then moves to fraction F of that (or today's) Dereth day, so the live weather
    /// stays.</item>
    /// <item><b>Automated runs</b> (<c>--screenshot</c>, <c>--fly-path</c>, golden viewpoints, checks) must be
    /// reproducible, whatever the real date: without <c>--game-time</c> they start on the first Sunny day (day group 0)
    /// from 10 P.Y. at <c>--time-of-day</c> (default 0.5, Midsong). That is the core's documented meaning of
    /// <c>--time-of-day=F</c> ("day group 0 at fraction F", <c>RenderGlobals.ApplyDefaults</c>). With
    /// <c>--game-time</c> and <c>--time-of-day</c> both, the fraction applies to that game time's day. The clock is paused
    /// unless <c>--time-speed</c> is given.</item>
    /// </list>
    /// </remarks>
    /// <param name="automated">An automated run.</param>
    /// <param name="gameTime"><c>--game-time</c>.</param>
    /// <param name="timeOfDay"><c>--time-of-day</c> (clamped to [0, 1)).</param>
    /// <param name="speed"><c>--time-speed</c>.</param>
    /// <param name="sky">The Region sky (to find the Sunny day).</param>
    public static SkyClockStart Resolve(bool automated, double? gameTime, double? timeOfDay, double? speed, DerethSky sky)
    {
        ArgumentNullException.ThrowIfNull(sky);
        var fraction = timeOfDay is { } f ? Math.Clamp(f, 0.0, Math.BitDecrement(1.0)) : (double?)null;
        if (!automated)
        {
            return new SkyClockStart(gameTime, speed ?? 1.0, fraction);
        }

        if (gameTime is { } start)
        {
            return new SkyClockStart(start, speed ?? 0.0, fraction);
        }

        var sunny = GoldenViewpoints.GameTimeFor(new GoldenTime(fraction ?? DefaultAutomatedDayFraction, GoldenViewpoints.Sunny), sky);
        return new SkyClockStart(sunny, speed ?? 0.0, null);
    }
}
