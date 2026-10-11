using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

public sealed class DerethClockTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    /// <summary>
    /// ACE <c>DerethDateTime.UtcNowToEMUTime</c> (Source/ACE.Common/DerethDateTime.cs:1062):
    /// <c>(DateTime.UtcNow.AddHours(-5) - new DateTime(2017, 1, 31, 12, 0, 0)).TotalSeconds</c>, transcribed literally.
    /// </summary>
    private static double AceEmuTime(DateTime utcNow) => (utcNow.AddHours(-5) - new DateTime(2017, 1, 31, 12, 0, 0)).TotalSeconds;

    [Theory]
    [InlineData("2017-01-31T17:00:00Z")]
    [InlineData("2017-01-31T17:00:00.5Z")]
    [InlineData("2020-06-15T03:21:09Z")]
    [InlineData("2026-10-10T12:00:00Z")]
    [InlineData("2016-12-31T00:00:00Z")]
    public void Matches_ACE_EMU_standard_sync_time(string instant)
    {
        var utc = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(AceEmuTime(utc.UtcDateTime), DerethClock.FromRealTime(utc));
        Assert.Equal(utc, DerethClock.ToRealTime(DerethClock.FromRealTime(utc)));
    }

    [Fact]
    public void Zero_is_the_end_of_retail()
    {
        Assert.Equal(0.0, DerethClock.FromRealTime(new DateTimeOffset(2017, 1, 31, 12, 0, 0, TimeSpan.FromHours(-5))));
        Assert.Equal(new DateTimeOffset(2017, 1, 31, 17, 0, 0, TimeSpan.Zero), DerethClock.ToRealTime(0));
    }

    [Fact]
    public void Advances_one_game_second_per_real_second_plus_the_local_delta()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));
        var clock = new DerethClock(time);
        var t0 = clock.Now;
        time.Now += TimeSpan.FromSeconds(7620);
        Assert.Equal(t0 + 7620, clock.Now, 6);

        clock.TimeZeroDelta = -1234.5;
        Assert.Equal(t0 + 7620 - 1234.5, clock.Now, 6);
    }

    [RequiresDatsFact]
    public void Calendar_on_the_ACE_clock_today_is_in_the_120s_P_Y()
    {
        var calendar = fixture.Calendar;

        // 2026-10-10: ~305.4M s after ACE's zero, / 2,743,200 s per year = 111.3 years after 10 P.Y.
        var d = calendar.ToDate(DerethClock.FromRealTime(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(121, d.Year);
        Assert.True(DerethClock.FromRealTime(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero)) < DerethClock.MaxClientGameTime);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
