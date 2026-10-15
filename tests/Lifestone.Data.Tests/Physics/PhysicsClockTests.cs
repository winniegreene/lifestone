using Lifestone.Data.Effects;
using Lifestone.Data.Physics;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The physics clock: retail <c>CPhysics::UseTime</c>'s 30 Hz gate, the per-object slicing it hands over to
/// (<see cref="ObjectClock"/>, retail <c>CPhysicsObj::update_object</c>), and the constants both read.
/// </summary>
public sealed class PhysicsClockTests
{
    [Fact]
    public void Floor_z_is_retails_cosine_rounded_to_float()
    {
        var retail = (float)Math.Cos(3437.746770784939);
        Assert.Equal(PhysicsGlobals.FloorZ, retail);
        Assert.Equal(0.66417414618662751f, retail);
    }

    [Fact]
    public void The_quanta_are_the_ones_the_object_clock_and_the_spell_viewer_step_with()
    {
        Assert.Equal(ObjectClock.MaxQuantum, PhysicsGlobals.MaxQuantum);
        Assert.Equal(ObjectClock.MinQuantum, PhysicsGlobals.MinQuantum);
        Assert.Equal(ObjectClock.MaxElapsed, PhysicsGlobals.HugeQuantum);
        Assert.Equal(EffectActor.MaxQuantum, PhysicsGlobals.MaxQuantum);
        Assert.Equal(EffectActor.MinQuantum, PhysicsGlobals.MinQuantum);
        Assert.Equal(PhysicsGlobals.MaxQuantum, PhysicsCompat.Retail.MaxQuantum);
    }

    [Fact]
    public void Physics_runs_once_a_thirtieth_of_a_second_has_passed()
    {
        var clock = new PhysicsClock(0.0);
        Assert.False(clock.UseTime(1.0 / 64.0, out var elapsed));
        Assert.Equal(1.0 / 64.0, elapsed);
        Assert.Equal(0.0, clock.LastUpdate);

        // Exactly 1/30 s runs (retail compares quantum >= MIN_QUANTUM).
        Assert.True(clock.UseTime(1.0 / 30.0, out elapsed));
        Assert.Equal(1.0 / 30.0, elapsed);
        Assert.Equal(1.0 / 30.0, clock.LastUpdate);
    }

    [Theory]
    [InlineData(64, 64, 21)]   // 1/64 s frames: 2/64 s is short of 1/30 s, so every third frame runs
    [InlineData(32, 64, 32)]   // 1/32 s frames: also short of 1/30 s, so every other frame runs
    [InlineData(16, 64, 64)]   // 1/16 s frames: every frame runs
    public void Physics_runs_on_the_frames_that_complete_a_thirtieth_of_a_second(int framesPerSecond, int frames, int expectedRuns)
    {
        // Frame times that are exact binary fractions, so the count does not depend on rounding.
        var clock = new PhysicsClock(0.0);
        var runs = 0;
        for (var frame = 1; frame <= frames; frame++)
        {
            if (clock.UseTime(frame / (double)framesPerSecond, out _))
            {
                runs++;
            }
        }

        Assert.Equal(expectedRuns, runs);
    }

    [Fact]
    public void At_sixty_frames_a_second_physics_runs_on_every_second_or_third_frame()
    {
        // Two 1/60 s frames make 1/30 s only in exact arithmetic: in double the difference of two frame times can fall
        // just short, so retail's gate (quantum >= MIN_QUANTUM) waits a third frame. Over one second at exactly 1/60 s
        // per frame that gives 21 runs, not 30; the same holds in retail.
        var clock = new PhysicsClock(0.0);
        var runs = 0;
        for (var frame = 1; frame <= 60; frame++)
        {
            if (clock.UseTime(frame / 60.0, out _))
            {
                runs++;
            }
        }

        Assert.Equal(21, runs);
    }

    [Fact]
    public void A_clock_going_backwards_resets_without_running()
    {
        var clock = new PhysicsClock(10.0);
        Assert.False(clock.UseTime(9.0, out var elapsed));
        Assert.Equal(-1.0, elapsed);
        Assert.Equal(9.0, clock.LastUpdate);
        Assert.True(clock.UseTime(9.5, out elapsed));
        Assert.Equal(0.5, elapsed);
    }

    [Fact]
    public void A_half_second_gap_is_stepped_as_retail_slices_it()
    {
        // CPhysicsObj::update_object: 0.2 + 0.2 + 0.1, as ObjectClock steps it.
        var objectClock = new ObjectClock(0.0);
        var steps = new List<double>();
        foreach (var (quantum, _) in objectClock.Advance(0.5))
        {
            steps.Add(quantum);
        }

        Assert.Equal(3, steps.Count);
        Assert.Equal(0.2, steps[0], 12);
        Assert.Equal(0.2, steps[1], 12);
        Assert.Equal(0.1, steps[2], 12);
    }
}
