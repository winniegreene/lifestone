using System.Globalization;
using DatReaderWriter.DBObjs;

namespace Lifestone.Data.World.Sky;

/// <summary>A named time of day from the Region GameTime ("Darktide" ... "Gloaming-and-Half").</summary>
/// <param name="Begin">Day fraction where this time of day starts.</param>
/// <param name="IsNight">Retail <c>is_night</c> flag.</param>
/// <param name="Name">Display name.</param>
public sealed record DerethTimeOfDay(float Begin, bool IsNight, string Name);

/// <summary>A "season" from the Region GameTime. Retail's seasons are the 12 thirty-day months (Morningthaw ...
/// Wintersebb); <c>GameTime::GetDateTimeString</c> prints the season name as the month.</summary>
/// <param name="FirstDay">0-based day of the year the season starts.</param>
/// <param name="Name">Display name.</param>
public sealed record DerethSeason(int FirstDay, string Name);

/// <summary>A game time resolved into the retail calendar (<c>GameTime::CalcDayBegin</c> + <c>GameTime::CalcTimeOfDay</c>).</summary>
/// <param name="GameTime">The input game time in seconds (retail <c>Timer::cur_time</c> + offsets).</param>
/// <param name="Year">Absolute Portal Year (<c>GameTime::current_year</c> = zero_year + whole years).</param>
/// <param name="DayOfYear">0-based day of the year (<c>GameTime::current_day</c>), the day-group hash input.</param>
/// <param name="SeasonIndex">Index into <see cref="DerethCalendar.Seasons"/> (<c>current_season</c>).</param>
/// <param name="SeasonName">Season (month) name.</param>
/// <param name="DayOfSeason">1-based day within the season, as printed by <c>GetDateTimeString</c>.</param>
/// <param name="TimeOfDayIndex">Index into <see cref="DerethCalendar.TimesOfDay"/> (<c>current_time_of_day</c>).</param>
/// <param name="TimeOfDayName">E.g. "Morntide-and-Half".</param>
/// <param name="IsNight">The time of day's <c>is_night</c> flag.</param>
/// <param name="DayBeginTime">Game time at which this day started (<c>time_of_day_begin</c>).</param>
/// <param name="DayFractionExact">(time - day begin) / day length in double precision.</param>
/// <param name="DayFraction">Retail <c>GameTime::present_time_of_day</c>: the same value stored as a float. This is the
/// "time of day" every sky function takes.</param>
/// <param name="TimeOfDayProgress">Retail <c>present_time_in_day_unit</c>, progress through the current named time of day.
/// For the last time of day retail divides by <c>day_length - begin</c> (seconds) instead of <c>1 - begin</c>, so the
/// value stays near 0 there; reproduced as-is.</param>
/// <param name="NextEventTime">Game time of the next named time-of-day boundary (<c>time_of_next_event</c>).</param>
/// <param name="DateString">Retail date string, e.g. "Morningthaw 1, 10 P.Y.".</param>
/// <param name="TimeString">Retail time string, the time-of-day name.</param>
public sealed record DerethDate(
    double GameTime,
    int Year,
    int DayOfYear,
    int SeasonIndex,
    string SeasonName,
    int DayOfSeason,
    int TimeOfDayIndex,
    string TimeOfDayName,
    bool IsNight,
    double DayBeginTime,
    double DayFractionExact,
    float DayFraction,
    double TimeOfDayProgress,
    double NextEventTime,
    string DateString,
    string TimeString)
{
    public override string ToString() => $"{DateString} {TimeString}";
}

/// <summary>
/// The Dereth calendar from Region GameTime, with retail's conversions between game time and calendar.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the retail decompile (Mag-nus/acclient-AI-RE, 11.4186, PORTAL/gametime/GameTime.c;
/// <c>GameTime::CalcDayBegin</c> acclient.c:463202, <c>GameTime::CalcTimeOfDay</c> 463242,
/// <c>GameTime::GetDateTimeString</c> 463285, <c>GameTime::UseTime</c> 463394, <c>GameTime::UnPack</c> 464013).
/// <c>CalcDayBegin</c> is byte-identical in 11.6096 (0x005A7510).
/// </para>
/// <para>
/// Game time is in seconds. Retail computes it in <c>GameTime::UseTime</c> as
/// <c>Timer::cur_time + clock_offset + time_zero_start_delta</c>: <c>Timer::cur_time</c> is the server clock
/// (<c>ClientNet::HandleTimeSynch</c> -> <c>Timer::set_time</c>, advanced locally by QueryPerformanceCounter seconds),
/// <c>clock_offset</c> is only ever zeroed, and <c>time_zero_start_delta</c> is the local registry variable
/// <c>GameTime.TimeZeroDelta</c> (default 0, <c>GameSky::OnTimeAdjustment</c>). See <see cref="DerethClock"/>.
/// </para>
/// <para>
/// Retail caches <c>time_of_day_begin</c> and only recomputes it when the day rolls over; this class is stateless and
/// recomputes it from the given time, which gives the same values (to double rounding of the cached begin).
/// The FPU runs at 53-bit precision (<c>ClientMain::ConfigureFPU</c>, acclient.c:60885; the D3D device is created
/// with D3DCREATE_FPU_PRESERVE), so C# double arithmetic in the same order reproduces retail's intermediates.
/// </para>
/// </remarks>
public sealed class DerethCalendar
{
    public DerethCalendar(
        double zeroTimeOfYear,
        int zeroYear,
        float dayLength,
        int daysPerYear,
        string yearSpec,
        IReadOnlyList<DerethTimeOfDay> timesOfDay,
        IReadOnlyList<string> weekDayNames,
        IReadOnlyList<DerethSeason> seasons)
    {
        ArgumentNullException.ThrowIfNull(timesOfDay);
        ArgumentNullException.ThrowIfNull(weekDayNames);
        ArgumentNullException.ThrowIfNull(seasons);
        if (!(dayLength > 0f) || daysPerYear <= 0)
        {
            throw new InvalidDataException($"GameTime day_length {dayLength} / days_per_year {daysPerYear} must be positive.");
        }

        // GameTime::CalcTimeOfDay and CalcDayBegin read entry [0] unconditionally (and CalcTimeOfDay loops to m_num - 1
        // as unsigned, which would run away on an empty array).
        if (timesOfDay.Count == 0 || seasons.Count == 0)
        {
            throw new InvalidDataException("GameTime needs at least one time of day and one season.");
        }

        // Both retail scans are linear "advance while begin <= value" loops that assume ascending order.
        for (var i = 1; i < timesOfDay.Count; i++)
        {
            if (timesOfDay[i].Begin < timesOfDay[i - 1].Begin)
            {
                throw new InvalidDataException($"GameTime time of day {i} begins before time of day {i - 1}.");
            }
        }

        for (var i = 1; i < seasons.Count; i++)
        {
            if (seasons[i].FirstDay < seasons[i - 1].FirstDay)
            {
                throw new InvalidDataException($"GameTime season {i} begins before season {i - 1}.");
            }
        }

        ZeroTimeOfYear = zeroTimeOfYear;
        ZeroYear = zeroYear;
        DayLength = dayLength;
        DaysPerYear = daysPerYear;

        // GameTime::UnPack: year_length = day_length * (double)days_per_year.
        YearLength = dayLength * (double)daysPerYear;
        YearSpec = yearSpec ?? string.Empty;
        TimesOfDay = timesOfDay;
        WeekDayNames = weekDayNames;
        Seasons = seasons;
    }

    /// <summary>Seconds added to game time before splitting it into years (3600 in the user's dat).</summary>
    public double ZeroTimeOfYear { get; }

    /// <summary>Portal Year of game time 0's year (10).</summary>
    public int ZeroYear { get; }

    /// <summary>Seconds per Dereth day (7620), a float in the dat.</summary>
    public float DayLength { get; }

    /// <summary>Days per year (360).</summary>
    public int DaysPerYear { get; }

    /// <summary>Seconds per year, <c>day_length * days_per_year</c> (2,743,200).</summary>
    public double YearLength { get; }

    /// <summary>"P.Y."</summary>
    public string YearSpec { get; }

    /// <summary>The 16 named times of day, in ascending <see cref="DerethTimeOfDay.Begin"/>.</summary>
    public IReadOnlyList<DerethTimeOfDay> TimesOfDay { get; }

    /// <summary>The 6 week-day names. Retail never computes <c>GameTime::current_week_day</c> (only the constructor and
    /// <c>Destroy</c> write it, and <c>GetDateTimeString</c> ignores it), so no week day is derived here.</summary>
    public IReadOnlyList<string> WeekDayNames { get; }

    /// <summary>The 12 seasons (months), in ascending <see cref="DerethSeason.FirstDay"/>.</summary>
    public IReadOnlyList<DerethSeason> Seasons { get; }

    public static DerethCalendar FromRegion(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var gt = region.GameTime ?? throw new InvalidDataException($"Region 0x{region.Id:X8} has no GameTime.");
        var times = gt.TimesOfDay.Select(t => new DerethTimeOfDay(t.Start, t.IsNight, t.Name?.Value ?? string.Empty)).ToArray();
        var weekDays = gt.DaysOfWeek.Select(d => d?.Value ?? string.Empty).ToArray();
        var seasons = gt.Seasons.Select(s => new DerethSeason(checked((int)s.Start), s.Name?.Value ?? string.Empty)).ToArray();
        return new DerethCalendar(
            gt.ZeroTimeOfYear,
            checked((int)gt.ZeroYear),
            gt.DayLength,
            checked((int)gt.DaysPerYear),
            gt.YearSpec?.Value ?? string.Empty,
            times,
            weekDays,
            seasons);
    }

    /// <summary>
    /// Resolves a game time exactly like <c>GameTime::CalcDayBegin</c> followed by <c>GameTime::CalcTimeOfDay</c> and
    /// the <c>present_time_of_day</c> update in <c>GameTime::UseTime</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The time is not finite or lies before the calendar's origin
    /// (<c>time + zero_time_of_year &lt; 0</c>), where retail's <c>(unsigned __int64)floor(...)</c> is undefined.</exception>
    public DerethDate ToDate(double gameTime)
    {
        var (year, dayOfYear, season, dayBegin) = CalcDayBegin(gameTime);

        // GameTime::UseTime: present_time_of_day (a float field) = (time - time_of_day_begin) / day_length.
        var fractionExact = (gameTime - dayBegin) / DayLength;

        // GameTime::CalcTimeOfDay: v3 = (time - time_of_day_begin) / day_length, kept in a register.
        var timeOfDay = 0;
        for (var i = 1; i < TimesOfDay.Count; i++)
        {
            if (fractionExact < TimesOfDay[i].Begin)
            {
                break;
            }

            timeOfDay++;
        }

        double nextEvent;
        double progress;
        var current = TimesOfDay[timeOfDay];
        if (timeOfDay == TimesOfDay.Count - 1)
        {
            nextEvent = DayLength + dayBegin;
            progress = (fractionExact - current.Begin) / (DayLength - current.Begin);
        }
        else
        {
            var next = TimesOfDay[timeOfDay + 1];
            nextEvent = (next.Begin * (double)DayLength) + dayBegin;
            progress = (fractionExact - current.Begin) / ((double)next.Begin - current.Begin);
        }

        var seasonDesc = Seasons[season];
        var dayOfSeason = dayOfYear - seasonDesc.FirstDay + 1;
        return new DerethDate(
            gameTime,
            year,
            dayOfYear,
            season,
            seasonDesc.Name,
            dayOfSeason,
            timeOfDay,
            current.Name,
            current.IsNight,
            dayBegin,
            fractionExact,
            (float)fractionExact,
            progress,
            nextEvent,
            FormatDate(seasonDesc.Name, dayOfSeason, year),
            FormatTime(current.Name));
    }

    /// <summary>Retail <c>GameTime::present_time_of_day</c> for a game time: the float day fraction the sky uses.</summary>
    public float DayFractionAt(double gameTime)
    {
        var (_, _, _, dayBegin) = CalcDayBegin(gameTime);
        return (float)((gameTime - dayBegin) / DayLength);
    }

    /// <summary>Game time at a day fraction of a 0-based day of a Portal Year (the inverse of <see cref="ToDate"/>).</summary>
    /// <param name="year">Absolute Portal Year (&gt;= <see cref="ZeroYear"/>).</param>
    /// <param name="dayOfYear">0-based day of the year, below <see cref="DaysPerYear"/>.</param>
    /// <param name="dayFraction">Fraction of the day in [0, 1).</param>
    public double ToGameTime(int year, int dayOfYear, double dayFraction = 0.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, ZeroYear);
        ArgumentOutOfRangeException.ThrowIfNegative(dayOfYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayOfYear, DaysPerYear);
        if (!(dayFraction >= 0.0 && dayFraction < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(dayFraction), dayFraction, "Day fraction must be in [0, 1).");
        }

        return ((year - ZeroYear) * YearLength) + (dayOfYear * (double)DayLength) + (dayFraction * DayLength) - ZeroTimeOfYear;
    }

    /// <summary>Game time at the start of a named time of day on a calendar date.</summary>
    /// <param name="year">Absolute Portal Year.</param>
    /// <param name="seasonIndex">Index into <see cref="Seasons"/>.</param>
    /// <param name="dayOfSeason">1-based day within the season, as displayed.</param>
    /// <param name="timeOfDayIndex">Index into <see cref="TimesOfDay"/>.</param>
    public double ToGameTime(int year, int seasonIndex, int dayOfSeason, int timeOfDayIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seasonIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seasonIndex, Seasons.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(timeOfDayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(timeOfDayIndex, TimesOfDay.Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(dayOfSeason, 1);
        var seasonEnd = seasonIndex + 1 < Seasons.Count ? Seasons[seasonIndex + 1].FirstDay : DaysPerYear;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dayOfSeason, seasonEnd - Seasons[seasonIndex].FirstDay);
        return ToGameTime(year, Seasons[seasonIndex].FirstDay + dayOfSeason - 1, TimesOfDay[timeOfDayIndex].Begin);
    }

    /// <summary>The game time on the same Dereth day as <paramref name="gameTime"/> at another day fraction (a
    /// time-of-day slider that keeps the date, and therefore the day group).</summary>
    public double WithDayFraction(double gameTime, double dayFraction)
    {
        if (!(dayFraction >= 0.0 && dayFraction < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(dayFraction), dayFraction, "Day fraction must be in [0, 1).");
        }

        var (_, _, _, dayBegin) = CalcDayBegin(gameTime);
        return dayBegin + (dayFraction * DayLength);
    }

    /// <summary><c>GameTime::CalcDayBegin</c>: year, 0-based day of year, season and the game time the day began.</summary>
    private (int Year, int DayOfYear, int Season, double DayBegin) CalcDayBegin(double gameTime)
    {
        if (!double.IsFinite(gameTime))
        {
            throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "Game time must be finite.");
        }

        var totalTime = gameTime + ZeroTimeOfYear;
        if (totalTime < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gameTime),
                gameTime,
                $"Game time is before the calendar origin (-{ZeroTimeOfYear} s); retail converts a negative floor to uint64.");
        }

        var years = Math.Floor(totalTime / YearLength);
        var offsetInYear = totalTime - (years * YearLength);
        var year = ZeroYear + checked((int)years);
        var day = (int)Math.Floor(offsetInYear / DayLength);

        var season = 0;
        for (var i = 1; i < Seasons.Count; i++)
        {
            if (day < Seasons[i].FirstDay)
            {
                break;
            }

            season++;
        }

        var dayBegin = gameTime - (offsetInYear - (day * (double)DayLength));
        return (year, day, season, dayBegin);
    }

    /// <summary><c>GameTime::GetDateTimeString</c>: <c>"%s %s, %s %s"</c>, or " " when it would not fit 60 chars.</summary>
    private string FormatDate(string seasonName, int dayOfSeason, int year)
    {
        var dayText = dayOfSeason.ToString(CultureInfo.InvariantCulture);
        var yearText = year.ToString(CultureInfo.InvariantCulture);
        return seasonName.Length + dayText.Length + yearText.Length + YearSpec.Length + 9 >= 60
            ? " "
            : $"{seasonName} {dayText}, {yearText} {YearSpec}";
    }

    /// <summary><c>GameTime::GetDateTimeString</c>: the time-of-day name, or " " when it is 30 chars or longer.</summary>
    private static string FormatTime(string timeOfDayName) => timeOfDayName.Length >= 30 ? " " : timeOfDayName;
}
