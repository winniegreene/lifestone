using Lifestone.Client.PortalSpace;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Retail <c>CPhysicsObj::update_position</c>'s slicing, as <c>CreatureMode::Render</c> steps the portal tunnel: no step
/// below 1/30 s (the time builds up), one step of the whole time up to 2 s, a skip above, and a reset at ε.
/// </summary>
public sealed class CreatureModeClockTests
{
    [Fact]
    public void Below_1_30_s_nothing_steps_and_the_time_builds_up()
    {
        var clock = new CreatureModeClock(10.0);

        Assert.False(clock.TryStep(10.02, out var quantum));
        Assert.Equal(0.0, quantum);
        Assert.Equal(10.0, clock.UpdateTime);

        Assert.True(clock.TryStep(10.04, out quantum));
        Assert.Equal((float)(10.04 - 10.0), quantum);
        Assert.Equal(10.04, clock.UpdateTime);
    }

    [Fact]
    public void A_step_of_exactly_1_30_s_is_taken()
    {
        var clock = new CreatureModeClock(0.0);

        Assert.True(clock.TryStep(ObjectClock.MinQuantum, out var quantum));
        Assert.Equal((float)ObjectClock.MinQuantum, quantum);
    }

    [Theory]
    [InlineData(0.21)]
    [InlineData(0.51)]
    [InlineData(1.99)]
    [InlineData(2.0)]
    public void A_long_gap_up_to_2_s_is_one_step_of_the_whole_time_never_split(double gap)
    {
        var clock = new CreatureModeClock(1.0);

        Assert.True(clock.TryStep(1.0 + gap, out var quantum));
        Assert.Equal((float)(1.0 + gap - 1.0), quantum);
        Assert.Equal(1.0 + gap, clock.UpdateTime);
        Assert.False(clock.TryStep(1.0 + gap, out _)); // nothing left over
    }

    [Fact]
    public void A_gap_over_2_s_is_skipped_and_the_clock_jumps()
    {
        var clock = new CreatureModeClock(1.0);

        Assert.False(clock.TryStep(3.0001, out var quantum));
        Assert.Equal(0.0, quantum);
        Assert.Equal(3.0001, clock.UpdateTime);
    }

    [Fact]
    public void An_elapsed_time_at_or_below_epsilon_moves_the_clock_without_a_step()
    {
        var clock = new CreatureModeClock(5.0);

        Assert.False(clock.TryStep(5.0001, out _));
        Assert.Equal(5.0001, clock.UpdateTime); // dropped, not carried

        Assert.False(clock.TryStep(4.0, out _)); // a clock that went back
        Assert.Equal(4.0, clock.UpdateTime);
    }

    [Fact]
    public void The_step_is_rounded_to_a_float_as_retail_passes_it()
    {
        var clock = new CreatureModeClock(0.0);

        Assert.True(clock.TryStep(0.1, out var quantum));
        Assert.Equal(0.10000000149011612, quantum);
        Assert.NotEqual(0.1, quantum);
    }
}
