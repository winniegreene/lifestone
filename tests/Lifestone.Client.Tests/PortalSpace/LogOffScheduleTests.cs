using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>Step PS7: the timing half of <c>CPlayerSystem::RequestLogOff</c> and <c>SetLogOffStarted</c>.</summary>
public sealed class LogOffScheduleTests
{
    [Fact]
    public void A_request_is_due_strictly_after_3_s()
    {
        var schedule = new LogOffSchedule();
        Assert.False(schedule.IsDue(1e9));

        schedule.Request(10.0, playerKillerDelay: false);

        Assert.True(schedule.Requested);
        Assert.Equal(13.0, schedule.DueTime);
        Assert.False(schedule.IsDue(12.9));
        Assert.False(schedule.IsDue(13.0));
        Assert.True(schedule.IsDue(13.0001));
        Assert.False(schedule.PlayerKillerDelayIgnored);
    }

    [Fact]
    public void Starting_the_transition_spends_the_request()
    {
        var schedule = new LogOffSchedule();
        schedule.Request(0.0, playerKillerDelay: false);

        schedule.Start();

        Assert.False(schedule.Requested);
        Assert.False(schedule.IsDue(100.0));
    }

    [Fact]
    public void The_player_killer_delay_is_recorded_and_not_applied()
    {
        var schedule = new LogOffSchedule();

        schedule.Request(0.0, playerKillerDelay: true);

        Assert.Equal(LogOffSchedule.Delay, schedule.DueTime);
        Assert.True(schedule.PlayerKillerDelayIgnored);
        Assert.Equal(20.0, LogOffSchedule.PlayerKillerExtraDelay);
    }

    [Fact]
    public void A_second_request_reschedules_and_Clear_forgets()
    {
        var schedule = new LogOffSchedule();
        schedule.Request(0.0, playerKillerDelay: true);
        schedule.Request(5.0, playerKillerDelay: false);
        Assert.Equal(8.0, schedule.DueTime);
        Assert.False(schedule.PlayerKillerDelayIgnored);

        schedule.Clear();

        Assert.False(schedule.Requested);
        Assert.Equal(0.0, schedule.DueTime);
    }
}
