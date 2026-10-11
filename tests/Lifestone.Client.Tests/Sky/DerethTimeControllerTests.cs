using Lifestone.Client.Sky;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Tests.Sky;

public sealed class DerethTimeControllerTests
{
    // The user's Region values (DerethCalendarTests): day 7,620 s, 360 days, ZeroTimeOfYear 3,600, 10 P.Y.
    private static readonly DerethCalendar Calendar = new(
        zeroTimeOfYear: 3600,
        zeroYear: 10,
        dayLength: 7620f,
        daysPerYear: 360,
        yearSpec: "P.Y.",
        timesOfDay: [new(0f, true, "Darktide"), new(0.25f, false, "Dawnsong"), new(0.5f, false, "Midsong"), new(0.875f, true, "Gloaming")],
        weekDayNames: ["Day"],
        seasons: [new(0, "Morningthaw"), new(30, "Solclaim")]);

    private sealed class FakeClock
    {
        public double Now { get; set; } = 1_000_000.0;
    }

    [Fact]
    public void Live_rate_follows_the_server_clock_exactly_with_the_offset()
    {
        var clock = new FakeClock();
        var time = new DerethTimeController(Calendar, () => clock.Now);
        Assert.True(time.IsLive);
        Assert.Equal(clock.Now, time.GameTime);
        Assert.Equal(0.0, time.LiveOffset);

        // Frame deltas do not accumulate at the retail rate: the clock is read (no drift).
        clock.Now += 10.0;
        time.Advance(9.9);
        Assert.Equal(clock.Now, time.GameTime);
        Assert.Equal(9.9, time.ClientClock, 12);

        time.SetGameTime(clock.Now + 500.0);
        clock.Now += 1.0;
        time.Advance(1.0);
        Assert.Equal(clock.Now + 500.0, time.GameTime);
        Assert.Equal(500.0, time.LiveOffset, 9);
    }

    [Fact]
    public void Pause_and_time_lapse_scale_both_the_game_time_and_the_client_clock()
    {
        var clock = new FakeClock();
        var time = new DerethTimeController(Calendar, () => clock.Now, startGameTime: 50_000.0, speed: 0.0);
        Assert.True(time.IsPaused);
        clock.Now += 100;
        time.Advance(100);
        Assert.Equal(50_000.0, time.GameTime);
        Assert.Equal(0.0, time.ClientClock);

        time.SetSpeed(60);
        time.Advance(2.0);
        Assert.Equal(50_120.0, time.GameTime);
        Assert.Equal(120.0, time.ClientClock);

        // Back to the retail rate: continue from the current time and then follow the clock.
        time.SetSpeed(1.0);
        clock.Now += 3;
        time.Advance(3);
        Assert.Equal(50_123.0, time.GameTime, 9);
    }

    [Fact]
    public void Day_fraction_keeps_the_date_and_marks_a_discontinuity()
    {
        var time = new DerethTimeController(Calendar, () => 0.0, startGameTime: Calendar.ToGameTime(10, 40, 0.3));
        var before = time.Date;
        var jumps = time.Discontinuities;
        time.SetDayFraction(0.9);
        var after = time.Date;
        Assert.Equal((before.Year, before.DayOfYear), (after.Year, after.DayOfYear));
        Assert.Equal(0.9, after.DayFractionExact, 9);
        Assert.Equal("Gloaming", after.TimeString);
        Assert.Equal(jumps + 1, time.Discontinuities);

        // The client clock does not jump with the game time.
        Assert.Equal(0.0, time.ClientClock);
    }

    [Fact]
    public void Time_stays_inside_the_calendar()
    {
        var time = new DerethTimeController(Calendar, () => 0.0, startGameTime: -1e9);
        Assert.Equal(-3600.0, time.GameTime);
        Assert.Equal(0, time.Date.DayOfYear);
        time.SetGameTime(1e12);
        Assert.Equal(DerethClock.MaxClientGameTime, time.GameTime);
        Assert.Throws<ArgumentOutOfRangeException>(() => time.SetGameTime(double.NaN));
        time.SetSpeed(double.PositiveInfinity);
        Assert.Equal(1.0, time.Speed);
        time.SetSpeed(1e9);
        Assert.Equal(DerethTimeController.MaxSpeed, time.Speed);
        time.SetSpeed(-5);
        Assert.Equal(0.0, time.Speed);
    }

    [Fact]
    public void Reset_to_live_returns_to_the_server_clock()
    {
        var clock = new FakeClock();
        var time = new DerethTimeController(Calendar, () => clock.Now, startGameTime: 12345.0, speed: 600);
        time.ResetToLive();
        Assert.True(time.IsLive);
        Assert.Equal(clock.Now, time.GameTime);
        Assert.Equal(0.0, time.LiveOffset);
    }

    [Fact]
    public void Presets_are_pause_retail_and_two_time_lapses()
    {
        Assert.Equal([0.0, 1.0, 60.0, 600.0], DerethTimeController.PresetSpeeds);
    }
}
