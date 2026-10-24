using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Shared;

/// <summary>
/// tests/Shared's <see cref="ManualTimeProvider"/>, which every test of timeout-driven code (the session, the transport,
/// the action API) stands on: time moves only on Advance, and timers fire there, in due order, on the caller's thread.
/// </summary>
public sealed class ManualTimeProviderTests
{
    [Fact]
    public void Time_moves_only_when_advanced()
    {
        var time = new ManualTimeProvider();
        var start = time.GetTimestamp();
        Assert.Equal(ManualTimeProvider.DefaultStart, time.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, time.GetElapsedTime(start));

        time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(TimeSpan.FromSeconds(1.5), time.GetElapsedTime(start));
        Assert.Equal(ManualTimeProvider.DefaultStart.AddSeconds(1.5), time.GetUtcNow());
        Assert.Equal(TimeSpan.FromSeconds(1.5), time.Elapsed);
        Assert.Equal(TimeZoneInfo.Utc, time.LocalTimeZone);

        var later = new DateTimeOffset(2000, 1, 1, 0, 1, 0, TimeSpan.Zero);
        time.SetUtcNow(later);
        Assert.Equal(later, time.GetUtcNow());
        Assert.Equal(TimeSpan.FromMinutes(1), time.GetElapsedTime(start));
    }

    [Fact]
    public void Timers_fire_on_advance_in_due_order_with_the_clock_at_their_due_time()
    {
        var time = new ManualTimeProvider();
        var fired = new List<(string Name, TimeSpan At)>();
        using var late = time.CreateTimer(_ => fired.Add(("late", time.Elapsed)), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        using var early = time.CreateTimer(_ => fired.Add(("early", time.Elapsed)), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var never = time.CreateTimer(_ => fired.Add(("never", time.Elapsed)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Assert.Empty(fired);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(fired);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal([("early", TimeSpan.FromSeconds(1)), ("late", TimeSpan.FromSeconds(3))], fired);
        Assert.Equal(TimeSpan.FromMilliseconds(5999), time.Elapsed);

        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, fired.Count);
    }

    [Fact]
    public void Task_delay_and_timed_cancellation_complete_only_on_advance()
    {
        var time = new ManualTimeProvider();
        var delay = Task.Delay(TimeSpan.FromSeconds(10), time);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20), time);

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(delay.IsCompleted);
        Assert.False(cancel.IsCancellationRequested);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(delay.IsCompletedSuccessfully);
        Assert.False(cancel.IsCancellationRequested);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(cancel.IsCancellationRequested);
    }
}
