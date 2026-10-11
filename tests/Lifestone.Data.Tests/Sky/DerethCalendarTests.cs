using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>
/// Retail <c>GameTime::CalcDayBegin</c>/<c>CalcTimeOfDay</c> on the user's Region GameTime.
/// </summary>
public sealed class DerethCalendarTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    private DerethCalendar Calendar => fixture.Calendar;

    /// <summary>
    /// Reference points stated by ACE (vendor-eval/ACE @95ef426d, Source/ACE.Common/DerethDateTime.cs, AGPL-3.0),
    /// which the server uses to name dates from the same game time the client receives:
    /// <list type="bullet">
    /// <item>line 23/56-58: ticks 0 = "Morningthaw 1, 10 P.Y. - Morntide-and-Half" (MinValue)</item>
    /// <item>line 24: hourOneTicks 210 = "Morningthaw 1, 10 P.Y. - Midsong"</item>
    /// <item>line 25: dayOneTicks = 210 + 476.25 * 8 = 4020 = "Morningthaw 2, 10 P.Y. - Darktide"</item>
    /// <item>line 27: yearZeroTicks = 4020 + 7620 * 269 = 2053800 = "Snowreap 1, 10 P.Y. - Darktide"</item>
    /// <item>line 26: yearOneTicks = 4020 + 7620 * 359 = 2739600 = "Morningthaw 1, 11 P.Y. - Darktide"</item>
    /// <item>lines 60-64: MaxValue 1073741828 = "Thistledown 2, 401 P.Y. - Morntide-and-Half"</item>
    /// </list>
    /// ACE hard-codes these; the dat-driven retail algorithm must reproduce every one.
    /// </summary>
    [RequiresDatsTheory]
    [InlineData(0.0, 10, "Morningthaw", 1, "Morntide-and-Half")]
    [InlineData(210.0, 10, "Morningthaw", 1, "Midsong")]
    [InlineData(4020.0, 10, "Morningthaw", 2, "Darktide")]
    [InlineData(2053800.0, 10, "Snowreap", 1, "Darktide")]
    [InlineData(2739600.0, 11, "Morningthaw", 1, "Darktide")]
    [InlineData(1073741828.0, 401, "Thistledown", 2, "Morntide-and-Half")]
    public void ACE_reference_points(double gameTime, int year, string season, int dayOfSeason, string timeOfDay)
    {
        var d = Calendar.ToDate(gameTime);
        Assert.Equal(year, d.Year);
        Assert.Equal(season, d.SeasonName);
        Assert.Equal(dayOfSeason, d.DayOfSeason);
        Assert.Equal(timeOfDay, d.TimeOfDayName);
        Assert.Equal($"{season} {dayOfSeason}, {year} P.Y.", d.DateString);
        Assert.Equal(timeOfDay, d.TimeString);
    }

    [RequiresDatsFact]
    public void Game_time_zero_is_7_16ths_plus_into_the_first_day()
    {
        var d = Calendar.ToDate(0.0);
        Assert.Equal(-3600.0, d.DayBeginTime);
        Assert.Equal(0, d.DayOfYear);
        Assert.Equal(3600.0 / 7620.0, d.DayFractionExact);
        Assert.Equal((float)(3600.0 / 7620.0), d.DayFraction);
        Assert.Equal(7, d.TimeOfDayIndex);
        Assert.False(d.IsNight);

        // Next event = Midsong at 0.5 day: 0.5 * 7620 - 3600 = 210 (ACE's hourOneTicks).
        Assert.Equal(210.0, d.NextEventTime);
    }

    [RequiresDatsFact]
    public void Time_of_day_boundaries_are_inclusive_at_their_begin()
    {
        // CalcTimeOfDay breaks on fraction < begin, so a boundary instant belongs to the later time of day.
        for (var i = 0; i < Calendar.TimesOfDay.Count; i++)
        {
            var t = Calendar.ToGameTime(20, 100, Calendar.TimesOfDay[i].Begin);
            var d = Calendar.ToDate(t);
            Assert.Equal(i, d.TimeOfDayIndex);
            Assert.Equal(Calendar.TimesOfDay[i].Begin, d.DayFraction);
            Assert.Equal(i < 4 || i >= 14, d.IsNight);
            var before = Calendar.ToDate(t - 0.001);
            Assert.Equal(i == 0 ? 15 : i - 1, before.TimeOfDayIndex);
        }
    }

    [RequiresDatsFact]
    public void Day_rolls_over_at_Darktide()
    {
        var lastSecond = Calendar.ToDate(4019.999);
        Assert.Equal((0, 15), (lastSecond.DayOfYear, lastSecond.TimeOfDayIndex));
        Assert.Equal(4020.0, lastSecond.NextEventTime);
        var next = Calendar.ToDate(4020.0);
        Assert.Equal((1, 0, 0f), (next.DayOfYear, next.TimeOfDayIndex, next.DayFraction));
    }

    [RequiresDatsFact]
    public void Last_time_of_day_progress_keeps_the_retail_day_length_divisor()
    {
        // GameTime::CalcTimeOfDay divides by (day_length - begin) for the last time of day, a retail quirk kept as-is.
        var t = Calendar.ToGameTime(30, 10, 0.96875); // halfway through Gloaming-and-Half
        var d = Calendar.ToDate(t);
        Assert.Equal(15, d.TimeOfDayIndex);
        Assert.Equal((0.96875 - 0.9375) / (7620.0 - 0.9375), d.TimeOfDayProgress, 12);

        var mid = Calendar.ToDate(Calendar.ToGameTime(30, 10, 0.53125)); // halfway through Midsong
        Assert.Equal(0.5, mid.TimeOfDayProgress, 9);
    }

    [RequiresDatsFact]
    public void Calendar_to_game_time_round_trips_for_every_day_and_time_of_day_of_several_years()
    {
        foreach (var year in new[] { 10, 11, 57, 121, 206, 401 })
        {
            for (var season = 0; season < Calendar.Seasons.Count; season++)
            {
                for (var day = 1; day <= 30; day++)
                {
                    for (var tod = 0; tod < Calendar.TimesOfDay.Count; tod++)
                    {
                        var t = Calendar.ToGameTime(year, season, day, tod);
                        var d = Calendar.ToDate(t);
                        Assert.True(
                            (year, season, day, tod) == (d.Year, d.SeasonIndex, d.DayOfSeason, d.TimeOfDayIndex),
                            $"{year}/{season}/{day}/{tod} -> {t} -> {d}");
                        Assert.Equal((season * 30) + day - 1, d.DayOfYear);
                    }
                }
            }
        }
    }

    [RequiresDatsFact]
    public void Game_time_to_calendar_round_trips_for_arbitrary_times()
    {
        var rng = new Random(1234);
        for (var i = 0; i < 20000; i++)
        {
            var t = rng.NextDouble() * 1.2e9;
            var d = Calendar.ToDate(t);
            var back = Calendar.ToGameTime(d.Year, d.DayOfYear, d.DayFractionExact);
            Assert.True(Math.Abs(back - t) < 1e-6, $"{t} -> {d} -> {back}");
            Assert.True(d.DayFractionExact >= 0.0 && d.DayFractionExact < 1.0, $"{t}: fraction {d.DayFractionExact}");
            Assert.True(Math.Abs(d.DayBeginTime - Calendar.ToGameTime(d.Year, d.DayOfYear)) < 1e-6, $"{t}: day begin");
            Assert.Equal(d.DayFraction, Calendar.DayFractionAt(t));
        }
    }

    [RequiresDatsFact]
    public void With_day_fraction_keeps_the_date()
    {
        var t = Calendar.ToGameTime(115, 200, 0.3);
        foreach (var f in new[] { 0.0, 0.25, 0.5, 0.999 })
        {
            var moved = Calendar.WithDayFraction(t, f);
            var d = Calendar.ToDate(moved);
            Assert.Equal((115, 200), (d.Year, d.DayOfYear));
            Assert.Equal(f, d.DayFractionExact, 9);
        }
    }

    [RequiresDatsFact]
    public void Invalid_times_fail_loudly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToDate(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToDate(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToDate(-3600.5));
        Assert.Equal(0, Calendar.ToDate(-3600.0).DayOfYear);
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToGameTime(9, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToGameTime(10, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToGameTime(10, 0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ToGameTime(10, 0, 31, 0));
    }

    [Fact]
    public void Synthetic_calendar_season_and_time_of_day_scan()
    {
        // Uneven seasons/times exercise the linear scans independently of the user's evenly spaced data.
        var c = new DerethCalendar(
            zeroTimeOfYear: 0,
            zeroYear: 1,
            dayLength: 100f,
            daysPerYear: 10,
            yearSpec: "Y",
            timesOfDay: [new(0f, true, "Night"), new(0.3f, false, "Day"), new(0.9f, true, "Dusk")],
            weekDayNames: ["A"],
            seasons: [new(0, "Early"), new(7, "Late")]);
        var d = c.ToDate((10 * 100 * 3) + (7 * 100) + 95);
        Assert.Equal((4, 7, 1, "Late", 1), (d.Year, d.DayOfYear, d.SeasonIndex, d.SeasonName, d.DayOfSeason));
        Assert.Equal((2, "Dusk", true), (d.TimeOfDayIndex, d.TimeOfDayName, d.IsNight));
        Assert.Equal("Late 1, 4 Y", d.DateString);
        Assert.Equal(1, c.ToDate(30.001).TimeOfDayIndex);

        // Begin is a float: 0.3f = 0.30000001..., so a double fraction of exactly 0.3 is still before it.
        Assert.Equal(0, c.ToDate(30).TimeOfDayIndex);
    }
}
