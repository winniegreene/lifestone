using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Sky;

/// <summary>
/// The game clock the client shows and lights the world with: retail's live Dereth time by default, plus the M1 tools
/// a viewer needs (pause, time-lapse, a time-of-day slider).
/// </summary>
/// <remarks>
/// <para><b>Retail.</b> Game time is <c>Timer::cur_time + time_zero_start_delta</c> (<c>GameTime::UseTime</c>,
/// acclient.c:463394, 11.4186): the server clock, advanced 1:1 by real seconds, plus the local registry offset
/// <c>GameTime.TimeZeroDelta</c> (<c>GameSky::OnTimeAdjustment</c>, acclient.c:308535, which applies a new offset at
/// once). <see cref="DerethClock"/> models that; at speed 1 this controller is exactly a <see cref="DerethClock"/> with
/// an offset, so the live default is retail.</para>
/// <para><b>Lifestone tools (not retail).</b> Speed 0 pauses, speeds above 1 run a time-lapse in which the whole client
/// clock (<see cref="ClientClock"/>, retail <c>Timer::cur_time</c>) runs fast: the sky's fog and light cadence
/// (<see cref="SkyUpdateCadence"/>) and the cloud scroll (<see cref="SkyTextureScroll"/>) then advance in game seconds,
/// as a time-lapse film of retail would show. Jumps (<see cref="SetGameTime"/>, <see cref="SetDayFraction"/>) do not
/// move <see cref="ClientClock"/>; they bump <see cref="Discontinuities"/> so the caller can refresh the light at once,
/// the way retail's <c>LScape::SetDay</c> zeroes its tick deadlines (acclient.c:306897).</para>
/// <para>Game time stays within the calendar: not before its origin (<c>-ZeroTimeOfYear</c>, where retail's
/// <c>CalcDayBegin</c> would convert a negative floor to uint64) and not after <see cref="DerethClock.MaxClientGameTime"/>
/// (ACE: later times crash the retail client).</para>
/// </remarks>
public sealed class DerethTimeController
{
    /// <summary>The speed presets the time panel offers: pause, retail rate, one Dereth hour per minute and per 6 s.</summary>
    public static readonly IReadOnlyList<double> PresetSpeeds = [0.0, 1.0, 60.0, 600.0];

    /// <summary>Fastest speed accepted (one Dereth day of 7,620 s in under 2 s).</summary>
    public const double MaxSpeed = 4096.0;

    private readonly Func<double> _liveClock;
    private double _gameTime;
    private double _liveOffset;

    /// <param name="calendar">The Region calendar (bounds and day fractions).</param>
    /// <param name="liveClock">Game time the live server clock reads now, e.g.
    /// <c>() =&gt; DerethClock.FromRealTime(DateTimeOffset.UtcNow)</c>.</param>
    /// <param name="startGameTime">Start time, or null for the live clock.</param>
    /// <param name="speed">Start speed (0 = paused, 1 = retail).</param>
    public DerethTimeController(DerethCalendar calendar, Func<double> liveClock, double? startGameTime = null, double speed = 1.0)
    {
        Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _liveClock = liveClock ?? throw new ArgumentNullException(nameof(liveClock));
        MinGameTime = -calendar.ZeroTimeOfYear;
        Speed = ClampSpeed(speed);
        _gameTime = Clamp(startGameTime ?? _liveClock());
        _liveOffset = _gameTime - _liveClock();
    }

    public DerethCalendar Calendar { get; }

    /// <summary>Earliest game time the calendar can express (<c>-ZeroTimeOfYear</c>).</summary>
    public double MinGameTime { get; }

    /// <summary>Latest game time: <see cref="DerethClock.MaxClientGameTime"/>.</summary>
    public double MaxGameTime => DerethClock.MaxClientGameTime;

    /// <summary>Current game time in seconds.</summary>
    public double GameTime => _gameTime;

    /// <summary>Game seconds per real second: 0 paused, 1 retail.</summary>
    public double Speed { get; private set; }

    /// <summary>True while paused.</summary>
    public bool IsPaused => Speed == 0.0;

    /// <summary>True at the retail rate, where the time follows the live clock plus an offset exactly.</summary>
    public bool IsLive => Speed == 1.0;

    /// <summary>
    /// Retail <c>Timer::cur_time</c> analogue: real seconds times the speed, summed. It never jumps, so cadence and
    /// texture scroll keep their pace through slider moves.
    /// </summary>
    public double ClientClock { get; private set; }

    /// <summary>Counts jumps of the game time (slider moves, explicit sets). Compare to detect one.</summary>
    public long Discontinuities { get; private set; }

    /// <summary>The offset to the live clock, i.e. retail's <c>GameTime.TimeZeroDelta</c> that gives this time.</summary>
    public double LiveOffset => _gameTime - _liveClock();

    /// <summary>The calendar date of <see cref="GameTime"/>.</summary>
    public DerethDate Date => Calendar.ToDate(_gameTime);

    /// <summary>Advances by a frame's real time.</summary>
    public void Advance(double realSeconds)
    {
        if (!(realSeconds > 0.0) || double.IsInfinity(realSeconds))
        {
            return;
        }

        ClientClock += realSeconds * Speed;
        if (IsLive)
        {
            _gameTime = Clamp(_liveClock() + _liveOffset);
        }
        else
        {
            _gameTime = Clamp(_gameTime + (realSeconds * Speed));
        }
    }

    /// <summary>Sets the speed (clamped to [0, <see cref="MaxSpeed"/>]); the game time is unchanged.</summary>
    public void SetSpeed(double speed)
    {
        Speed = ClampSpeed(speed);
        _liveOffset = _gameTime - _liveClock();
    }

    /// <summary>Jumps to a game time (clamped to the calendar).</summary>
    public void SetGameTime(double gameTime)
    {
        if (!double.IsFinite(gameTime))
        {
            throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "Game time must be finite.");
        }

        _gameTime = Clamp(gameTime);
        _liveOffset = _gameTime - _liveClock();
        Discontinuities++;
    }

    /// <summary>Jumps to another time of the same Dereth day (the day, and so the retail day group, stays).</summary>
    /// <param name="dayFraction">Day fraction in [0, 1).</param>
    public void SetDayFraction(double dayFraction)
    {
        var clamped = Math.Clamp(dayFraction, 0.0, Math.BitDecrement(1.0));
        SetGameTime(Calendar.WithDayFraction(_gameTime, clamped));
    }

    /// <summary>Jumps back to the live server clock (retail: <c>TimeZeroDelta</c> = 0) at speed 1.</summary>
    public void ResetToLive()
    {
        Speed = 1.0;
        _gameTime = Clamp(_liveClock());
        _liveOffset = _gameTime - _liveClock();
        Discontinuities++;
    }

    private static double ClampSpeed(double speed) =>
        double.IsFinite(speed) ? Math.Clamp(speed, 0.0, MaxSpeed) : 1.0;

    private double Clamp(double gameTime) => Math.Clamp(gameTime, MinGameTime, MaxGameTime);
}
