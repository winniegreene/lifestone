using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// <see cref="ObjectClock"/>, the time slicing of retail <c>CPhysicsObj::update_object</c> extracted from the M1
/// <c>EffectActor</c> in M2 wave 0: the same steps, bit for bit, as the loop it replaced.
/// </summary>
public sealed class ObjectClockTests
{
    [Fact]
    public void A_gap_up_to_0_2_s_is_one_step()
    {
        var clock = new ObjectClock(10.0);
        Assert.Equal(new[] { new ObjectQuantum(10.1 - 10.0, 10.0 + (10.1 - 10.0)) }, Steps(clock, 10.1));
        Assert.Equal(10.0 + (10.1 - 10.0), clock.UpdateTime);
    }

    [Fact]
    public void A_longer_gap_is_split_into_0_2_s_steps_and_a_remainder_step()
    {
        var clock = new ObjectClock(0.0);
        var steps = Steps(clock, 0.5);
        Assert.Equal(3, steps.Count);
        Assert.Equal(ObjectClock.MaxQuantum, steps[0].Quantum);
        Assert.Equal(ObjectClock.MaxQuantum, steps[1].Quantum);
        Assert.Equal(0.1, steps[2].Quantum, 12);
        Assert.Equal(0.5, clock.UpdateTime, 12);
        Assert.Equal(clock.UpdateTime, steps[^1].Time);
    }

    [Fact]
    public void A_split_gap_carries_a_remainder_of_1_30_s_or_less_to_the_next_call()
    {
        var clock = new ObjectClock(0.0);
        var steps = Steps(clock, 0.42);
        Assert.Equal(2, steps.Count);
        Assert.Equal(0.4, clock.UpdateTime, 12); // 0.02 s carried

        // The next call steps the carried time with the new one; an unsplit gap is stepped even below 1/30 s.
        steps = Steps(clock, 0.43);
        var step = Assert.Single(steps);
        Assert.Equal(0.43 - 0.4, step.Quantum, 12);
    }

    [Theory]
    [InlineData(0.0001)] // at or below ε: the clock jumps
    [InlineData(2.0001)] // longer than 2 s: the clock jumps
    [InlineData(-1.0)] // backwards
    public void Tiny_huge_and_negative_gaps_are_skipped_and_the_clock_jumps(double gap)
    {
        var clock = new ObjectClock(5.0);
        Assert.Empty(Steps(clock, 5.0 + gap));
        Assert.Equal(5.0 + gap, clock.UpdateTime);
    }

    [Fact]
    public void Enumerating_the_steps_twice_never_steps_the_same_time_twice()
    {
        var clock = new ObjectClock(0.0);
        var steps = clock.Advance(0.5);
        var first = 0;
        foreach (var _ in steps)
        {
            first++;
        }

        var second = 0;
        foreach (var _ in steps)
        {
            second++;
        }

        Assert.Equal(3, first);
        Assert.Equal(0, second);
        Assert.Equal(0.5, clock.UpdateTime, 12);
    }

    [Fact]
    public void Steps_match_the_M1_EffectActor_loop_bit_for_bit()
    {
        // The loop EffectActor.AdvanceTo ran before the extraction, as the oracle, over clocks with every kind of gap.
        var random = new Random(6096);
        var clock = new ObjectClock(100.0);
        var oracle = new M1Loop(100.0);
        var now = 100.0;
        for (var frame = 0; frame < 20_000; frame++)
        {
            now += random.Next(10) switch
            {
                0 => random.NextDouble() * 0.0003, // around ε
                1 => 2.0 + (random.NextDouble() * 0.5), // around the 2 s skip
                2 => random.NextDouble() * 1.9, // split gaps
                _ => random.NextDouble() / 30.0, // frame-sized gaps
            };

            var expected = oracle.AdvanceTo(now);
            var actual = Steps(clock, now);
            if (!expected.SequenceEqual(actual) || oracle.UpdateTime != clock.UpdateTime)
            {
                Assert.Fail($"frame {frame} at {now:R}: expected [{string.Join(", ", expected)}] ending {oracle.UpdateTime:R}, got [{string.Join(", ", actual)}] ending {clock.UpdateTime:R}");
            }
        }
    }

    private static List<ObjectQuantum> Steps(ObjectClock clock, double now)
    {
        var steps = new List<ObjectQuantum>();
        foreach (var step in clock.Advance(now))
        {
            steps.Add(step);
        }

        return steps;
    }

    /// <summary>The M1 <c>EffectActor.AdvanceTo</c> slicing loop, unchanged, recording its steps instead of running them.</summary>
    private sealed class M1Loop(double now)
    {
        private const double MaxQuantum = 1.0 / 5.0;
        private const double MinQuantum = 1.0 / 30.0;
        private const double Epsilon = 0.00019999999;

        public double UpdateTime { get; private set; } = now;

        public List<ObjectQuantum> AdvanceTo(double now)
        {
            var steps = new List<ObjectQuantum>();
            var dt = now - UpdateTime;
            if (dt <= Epsilon || dt > 2.0)
            {
                UpdateTime = now;
                return steps;
            }

            var time = UpdateTime;
            if (dt > MaxQuantum)
            {
                while (dt > MaxQuantum)
                {
                    time += MaxQuantum;
                    steps.Add(new ObjectQuantum(MaxQuantum, time));
                    dt -= MaxQuantum;
                }

                if (dt > MinQuantum)
                {
                    time += dt;
                    steps.Add(new ObjectQuantum(dt, time));
                }
            }
            else
            {
                time += dt;
                steps.Add(new ObjectQuantum(dt, time));
            }

            UpdateTime = time;
            return steps;
        }
    }
}
