using Lifestone.Net.Packets;
using Lifestone.Net.Transport;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

/// <summary>
/// The transport's clocks run on a <see cref="TimeProvider"/> (m2-plan §3.3 N2; A0's R5), never on <c>Stopwatch</c>, so
/// tests and the headless host can drive them: the packet header's interval id ticks every half second, and the
/// fragment assembler's time-to-live is measured on the same provider.
/// </summary>
public sealed class TransportClockTests
{
    [Fact]
    public void The_interval_id_starts_at_one_and_ticks_every_half_second_of_the_provider()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);
        Assert.Equal(time.TimestampFrequency, clock.Frequency);
        Assert.Equal((ushort)1, clock.IntervalId);

        time.Advance(TimeSpan.FromMilliseconds(499));
        clock.Update();
        Assert.Equal((ushort)1, clock.IntervalId);

        time.Advance(TimeSpan.FromMilliseconds(1));
        clock.Update();
        Assert.Equal((ushort)2, clock.IntervalId);

        time.Advance(TimeSpan.FromSeconds(2.25));
        clock.Update();
        Assert.Equal((ushort)6, clock.IntervalId);
        Assert.Equal(time.GetTimestamp(), clock.GetTimestamp());
    }

    [Fact]
    public void The_interval_id_is_a_wrapping_u16()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);

        // 65,534 half-seconds after the start the id is 65,535; the next half-second wraps it to 0.
        time.Advance(TimeSpan.FromSeconds(65_534 / 2.0));
        clock.Update();
        Assert.Equal(ushort.MaxValue, clock.IntervalId);

        time.Advance(TimeSpan.FromSeconds(0.5));
        clock.Update();
        Assert.Equal((ushort)0, clock.IntervalId);
    }

    [Fact]
    public void A_clock_needs_a_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new TransportClock(null!));
        Assert.Throws<ArgumentNullException>(() => new FragmentAssembler(null!));
    }
}
