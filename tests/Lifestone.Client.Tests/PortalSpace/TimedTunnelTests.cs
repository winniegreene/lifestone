using Lifestone.Client.PortalSpace;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS5: the spin's frame counter without the dats. It steps the real <see cref="TunnelSpin"/> over a stand-in
/// animation with retail's creature-mode clock (<see cref="CreatureModeClock"/>, <c>CPhysicsObj::update_position</c>), so
/// these tests also pin the loop arithmetic of <c>CSequence::update_internal</c> that the tunnel uses.
/// </summary>
public sealed class TimedTunnelTests
{
    [Fact]
    public void A_new_counter_is_stopped_at_frame_0_and_a_restart_starts_at_frame_1()
    {
        var tunnel = new TimedTunnel();
        Assert.False(tunnel.IsSpinning);
        Assert.Equal(0, tunnel.CurrentFrame);

        tunnel.Restart();

        Assert.True(tunnel.IsSpinning);
        Assert.Equal(1, tunnel.CurrentFrame);
        Assert.Equal(1.0, tunnel.FrameNumber);
        Assert.Equal(0.0, tunnel.UpdateTime);
    }

    [Fact]
    public void At_1_64_s_the_spin_steps_every_third_frame_and_cycles_1_to_119_every_2_975_s()
    {
        // 2/64 s is under retail's 1/30 s minimum and 3/64 s is over it, so update_position steps on every third frame, by
        // 3/64 s (exact as a float) = 1.875 spin frames: after j steps the frame number is exactly 1 + (1.875 j mod 119).
        const double dt = 1.0 / 64.0;
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        var wraps = new List<double>();
        var previous = tunnel.CurrentFrame;
        for (var k = 1; k <= 3 * 191; k++)
        {
            var t = k * dt;
            tunnel.AdvanceTo(t);
            var steps = k / 3;
            Assert.Equal(steps * 3 * dt, tunnel.UpdateTime);
            Assert.Equal(1 + (15 * steps / 8 % 119), tunnel.CurrentFrame);
            Assert.InRange(tunnel.CurrentFrame, 1, 119);
            if (tunnel.CurrentFrame < previous)
            {
                wraps.Add(t);
            }

            previous = tunnel.CurrentFrame;
        }

        Assert.Equal(3, wraps.Count);
        for (var loop = 0; loop < wraps.Count; loop++)
        {
            Assert.InRange(wraps[loop] - ((loop + 1) * TunnelSpin.RetailLoopSeconds), 0.0, 3 * dt);
        }

        Assert.Equal(2.975, TunnelSpin.RetailLoopSeconds, 12);
    }

    [Fact]
    public void At_60_fps_the_spin_steps_every_second_or_third_frame_and_holds_between_steps()
    {
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        var lastStep = 0.0;
        var lastFrameNumber = tunnel.FrameNumber;
        var steps = 0;
        for (var k = 1; k <= 3 * 179; k++)
        {
            var t = k / 60.0;
            tunnel.AdvanceTo(t);
            if (tunnel.UpdateTime == lastStep)
            {
                Assert.Equal(lastFrameNumber, tunnel.FrameNumber); // no step: the time builds up
                continue;
            }

            steps++;
            Assert.Equal(t, tunnel.UpdateTime);
            Assert.InRange(t - lastStep, (2.0 / 60.0) - 1e-12, (3.0 / 60.0) + 1e-12);
            var exact = 40.0 * t;
            Assert.Equal(1.0 + (exact % 119.0), tunnel.FrameNumber, 1e-4);
            lastStep = t;
            lastFrameNumber = tunnel.FrameNumber;
        }

        Assert.InRange(steps, 3 * 179 / 3, 3 * 179 / 2);
    }

    [Fact]
    public void The_exit_window_frames_68_to_76_fall_between_1_675_s_and_1_9_s_into_each_loop_at_the_last_step()
    {
        // At 1/400 s a step comes every 14 frames (0.0325 s < 1/30 s ≤ 0.035 s), 1.4 spin frames long.
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        for (var k = 1; k <= 3 * 1190; k++)
        {
            tunnel.AdvanceTo(k / 400.0);
            var intoLoop = tunnel.UpdateTime % TunnelSpin.RetailLoopSeconds;
            if (Math.Abs(intoLoop - 1.675) < 1e-6 || Math.Abs(intoLoop - 1.9) < 1e-6 || intoLoop < 1e-6 || TunnelSpin.RetailLoopSeconds - intoLoop < 1e-6)
            {
                continue;
            }

            Assert.Equal(k / 14 * 14 / 400.0, tunnel.UpdateTime);
            Assert.Equal(intoLoop is > 1.675 and < 1.9, ExitWindow.Admits(tunnel.CurrentFrame));
        }
    }

    [Fact]
    public void A_tunnel_shown_again_after_2_s_skips_the_hidden_time_and_one_shown_sooner_catches_up()
    {
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        tunnel.AdvanceTo(0.51);
        Assert.Equal(21, tunnel.CurrentFrame);

        tunnel.Stop();
        Assert.False(tunnel.IsSpinning);
        Assert.Equal(0, tunnel.CurrentFrame);

        // Hidden for 99.49 s: the first update skips the gap.
        tunnel.Restart();
        Assert.Equal(1, tunnel.CurrentFrame);
        Assert.Equal(0.51, tunnel.UpdateTime); // set_sequence_animation leaves update_time alone
        tunnel.AdvanceTo(100.0);
        Assert.Equal(1, tunnel.CurrentFrame);
        Assert.Equal(100.0, tunnel.UpdateTime);
        tunnel.AdvanceTo(100.0 + (3.0 / 64.0));
        Assert.Equal(2.875, tunnel.FrameNumber);

        // Hidden for about 1.01 s: the first update steps it whole, so the spin starts 40 frames on.
        tunnel.Stop();
        tunnel.Restart();
        tunnel.AdvanceTo(101.056875);
        Assert.Equal(41, tunnel.CurrentFrame);
        Assert.Equal(101.056875, tunnel.UpdateTime);
    }

    [Fact]
    public void A_half_second_hitch_is_one_step_and_a_gap_over_2_s_is_skipped_as_retail_s_update_position_does()
    {
        var tunnel = new TimedTunnel();
        tunnel.Restart();

        tunnel.AdvanceTo(0.51); // one step of the whole 0.51 s (update_object would have split it into 0.2 + 0.2 + 0.11)
        Assert.Equal(21, tunnel.CurrentFrame);
        Assert.Equal(1.0 + (40.0 * (float)0.51), tunnel.FrameNumber, 1e-12);

        tunnel.AdvanceTo(3.02); // 2.51 s: skipped
        Assert.Equal(21, tunnel.CurrentFrame);
        Assert.Equal(3.02, tunnel.UpdateTime);

        tunnel.AdvanceTo(3.12);
        Assert.Equal(25, tunnel.CurrentFrame);
    }

    [Fact]
    public void It_fires_no_hooks()
    {
        var tunnel = new TimedTunnel();
        tunnel.Restart();
        tunnel.AdvanceTo(1.0);
        var events = new List<EffectEvent>();

        Assert.Equal(0, tunnel.DrainEvents(events));
        Assert.Empty(events);
    }

    [Fact]
    public void A_shorter_stand_in_loops_its_own_frames()
    {
        // Frames 1 and 2 at 40 fps; each step is 3/80 s = 1.5 spin frames (a hair more: the step is a float).
        var tunnel = new TimedTunnel(frameCount: 3);
        tunnel.Restart();
        var frames = new List<int>();
        for (var k = 1; k <= 8; k++)
        {
            tunnel.AdvanceTo(k * 3.0 / 80.0);
            frames.Add(tunnel.CurrentFrame);
        }

        Assert.Equal([2, 2, 1, 1, 2, 2, 1, 1], frames);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimedTunnel(frameCount: 1));
    }

    [Fact]
    public void The_clock_starts_at_the_counter_s_creation()
    {
        var tunnel = new TimedTunnel(createdAt: 50.0);
        tunnel.Restart();

        tunnel.AdvanceTo(50.51); // 0.51 s after creation: one step
        Assert.Equal(21, tunnel.CurrentFrame);

        var late = new TimedTunnel(createdAt: 50.0);
        late.Restart();
        late.AdvanceTo(53.0); // 3 s after creation: skipped
        Assert.Equal(1, late.CurrentFrame);
    }
}
