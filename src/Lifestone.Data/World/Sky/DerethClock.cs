namespace Lifestone.Data.World.Sky;

/// <summary>
/// Game time from real time, the way the client sees it when connected to ACE.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rate.</b> Game time is in seconds and advances 1:1 with real time. Retail <c>GameTime::UseTime</c>
/// (acclient.c:463394) reads <c>Timer::cur_time</c>, which <c>Timer::update_time</c> advances by
/// QueryPerformanceCounter seconds and <c>ClientNet::HandleTimeSynch</c> (acclient.c:371515) re-syncs to the server's
/// clock (<c>Timer::set_time</c> only ever moves it forward). ACE's server clock <c>Timers.PortalYearTicks</c> is
/// advanced by <c>worldTickTimer.Elapsed.TotalSeconds</c> each world tick (ACE WorldManager.cs:391).
/// </para>
/// <para>
/// <b>Zero.</b> ACE starts <c>PortalYearTicks</c> at <c>DerethDateTime.UtcNowToEMUTime</c> =
/// <c>(DateTime.UtcNow.AddHours(-5) - new DateTime(2017, 1, 31, 12, 0, 0)).TotalSeconds</c>
/// (ACE.Common/DerethDateTime.cs:1062, Timers.cs:29,47), i.e. seconds since 2017-01-31 17:00:00 UTC, the end of
/// retail. So game time 0 (Morningthaw 1, 10 P.Y., Morntide-and-Half) is that instant. The retail servers' own zero
/// is not recoverable from the client; only the server sends time.
/// </para>
/// <para>
/// <b>Local offset.</b> Retail adds the registry variable <c>GameTime.TimeZeroDelta</c> (<c>GameSky::s_timeAdjustment</c>,
/// default 0) to the server time; <see cref="Now"/> does the same with <see cref="TimeZeroDelta"/>.
/// </para>
/// </remarks>
public sealed class DerethClock
{
    /// <summary>Real-time instant of game time 0 under ACE's "EMU standard sync time" (2017-01-31 12:00 EST).</summary>
    public static readonly DateTimeOffset AceTimeZeroUtc = new(2017, 1, 31, 17, 0, 0, TimeSpan.Zero);

    /// <summary>Game-time seconds per real second.</summary>
    public const double GameSecondsPerRealSecond = 1.0;

    /// <summary>ACE <c>DerethDateTime.MaxValue</c> (Thistledown 2, 401 P.Y., Morntide-and-Half): "any value higher than
    /// this results in acclient crashing upon connection to server" (ACE.Common/DerethDateTime.cs:60-64).</summary>
    public const double MaxClientGameTime = 1073741828.0;

    private readonly TimeProvider _timeProvider;

    public DerethClock(TimeProvider? timeProvider = null, double timeZeroDelta = 0.0)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        TimeZeroDelta = timeZeroDelta;
    }

    /// <summary>Seconds added to the server-equivalent time, like retail's <c>GameTime.TimeZeroDelta</c>. A UI clock
    /// offset or time-of-day slider can set this.</summary>
    public double TimeZeroDelta { get; set; }

    /// <summary>Current game time: <see cref="FromRealTime"/>(now) + <see cref="TimeZeroDelta"/>.</summary>
    public double Now => FromRealTime(_timeProvider.GetUtcNow()) + TimeZeroDelta;

    /// <summary>Game time ACE would report at a real-time instant (without any local offset).</summary>
    public static double FromRealTime(DateTimeOffset instant) =>
        (instant.UtcDateTime - AceTimeZeroUtc.UtcDateTime).TotalSeconds * GameSecondsPerRealSecond;

    /// <summary>Real-time instant at which ACE's clock reads <paramref name="gameTime"/>.</summary>
    public static DateTimeOffset ToRealTime(double gameTime)
    {
        if (!double.IsFinite(gameTime))
        {
            throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "Game time must be finite.");
        }

        return AceTimeZeroUtc + TimeSpan.FromSeconds(gameTime / GameSecondsPerRealSecond);
    }
}
