using System.Numerics;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS5 on the real dats: the tunnel object spins the UIASSET animation with <c>set_sequence_animation</c>'s start
/// frame 1 at 40 fps, and its frame-2 hook becomes one tunnel hum per 2.975 s loop.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class TunnelObjectTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint TunnelHum = 0x0A000316;

    private TunnelObject NewTunnel() => TunnelObject.Create(fixture.Dats, PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps));

    [RequiresDatsFact]
    public void The_tunnel_has_the_setup_s_two_parts_and_starts_stopped()
    {
        var tunnel = NewTunnel();

        Assert.Equal(2, tunnel.PartCount);
        Assert.Equal(120, tunnel.Spin.FrameCount);
        Assert.False(tunnel.Spin.IsSpinning);
        Assert.Equal(0, tunnel.CurrentFrame);
    }

    [RequiresDatsFact]
    public void Frames_cycle_1_to_119_with_a_period_of_2_975_s_stepping_every_third_frame_at_1_64_s()
    {
        // At 1/64 s retail's update_position steps every third frame, by 3/64 s = 1.875 spin frames (see TimedTunnelTests).
        const double dt = 1.0 / 64.0;
        var tunnel = NewTunnel();
        tunnel.Restart();
        Assert.Equal(1, tunnel.CurrentFrame);

        var wraps = new List<double>();
        var previous = tunnel.CurrentFrame;
        for (var k = 1; k <= 3 * 191; k++)
        {
            var t = k * dt;
            tunnel.AdvanceTo(t);
            Assert.Equal(1 + (15 * (k / 3) / 8 % 119), tunnel.CurrentFrame); // frame 0 is never shown again
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
    }

    [RequiresDatsFact]
    public void The_parts_are_posed_by_the_spin_about_the_scene_origin()
    {
        var tunnel = NewTunnel();
        var animation = new DatAnimationSource(fixture.Dats).GetAnimation(tunnel.Spin.AnimationId)!;
        tunnel.Restart();

        tunnel.AdvanceTo(0.51); // one step to frame 21
        tunnel.AdvanceTo(0.52); // under 1/30 s later: no step

        Assert.Equal(21, tunnel.CurrentFrame);
        for (var part = 0; part < tunnel.PartCount; part++)
        {
            var expected = animation.PartFrames[21].Frames[part];
            Assert.True(Vector3.Distance(expected.Origin, tunnel.PartFrames[part].Origin) < 1e-5f);
            Assert.True(Math.Abs(Quaternion.Dot(expected.Orientation, tunnel.PartFrames[part].Orientation)) > 0.999999f);
        }
    }

    [RequiresDatsTheory]
    [InlineData(60.0, 0.0)]
    [InlineData(30.0, 0.0)]
    [InlineData(60.0, 0.5)]
    public void Ten_loops_play_the_hum_ten_times_each_as_the_frame_crosses_from_2_to_3(double framesPerSecond, double hitch)
    {
        var tunnel = NewTunnel();
        var events = new List<EffectEvent>();
        tunnel.Restart();
        var end = 10 * TunnelSpin.RetailLoopSeconds;
        var now = 0.0;
        var step = 0;
        var crossings = 0;
        while (now < end - 1e-9)
        {
            step++;
            var dt = hitch > 0.0 && step % 150 == 0 ? hitch : 1.0 / framesPerSecond;
            now = Math.Min(end, now + dt);
            var before = tunnel.Spin.FrameNumber;
            tunnel.AdvanceTo(now);
            var after = tunnel.Spin.FrameNumber;
            var fired = tunnel.DrainEvents(events);
            if (fired > 0)
            {
                crossings++;
                Assert.Equal(1, fired);
                Assert.True(after >= 3.0 && (before < 3.0 || after < before), $"the hum fired going from frame {before:F3} to {after:F3}");
            }
        }

        Assert.Equal(10, events.Count);
        Assert.Equal(10, crossings);
        Assert.All(events, e => Assert.Equal(new SoundTweakedHook(AnimHookDirection.Both, TunnelHum, Priority: 1f, Probability: 1f, Volume: 0.3f), e.Hook));
        // The hum is timed at the step that crossed frame 3: under 1/30 s of built-up time plus the frame (or the hitch) later.
        for (var loop = 0; loop < events.Count; loop++)
        {
            var crossing = (loop * TunnelSpin.RetailLoopSeconds) + (2.0 / 40.0);
            Assert.InRange(events[loop].Time - crossing, -1e-6, hitch + ObjectClock.MinQuantum + (1.0 / framesPerSecond) + 1e-9);
        }
    }

    [RequiresDatsFact]
    public void A_restart_keeps_the_clock_and_the_pose_and_a_re_shown_tunnel_skips_a_gap_over_2_s()
    {
        var tunnel = NewTunnel();
        tunnel.Restart();
        tunnel.AdvanceTo(1.01);
        Assert.Equal(41, tunnel.CurrentFrame);
        var pose = tunnel.PartFrames.ToArray();

        tunnel.Stop();
        Assert.Equal(0, tunnel.CurrentFrame);

        // Hidden for 60 s, not advanced meanwhile (retail steps creature-mode objects only while drawing them).
        tunnel.Restart();
        Assert.Equal(1, tunnel.CurrentFrame);
        Assert.Equal(1.01, tunnel.UpdateTime); // set_sequence_animation leaves update_time alone
        Assert.Equal(pose, tunnel.PartFrames.ToArray()); // and poses nothing: the next step does

        tunnel.AdvanceTo(61.0); // the gap is skipped
        Assert.Equal(1, tunnel.CurrentFrame);
        Assert.Equal(61.0, tunnel.UpdateTime);
        Assert.Equal(pose, tunnel.PartFrames.ToArray());

        tunnel.AdvanceTo(61.0 + (1.0 / 60.0)); // under 1/30 s: no step yet
        Assert.Equal(61.0, tunnel.UpdateTime);
        tunnel.AdvanceTo(61.04);
        Assert.Equal(2, tunnel.CurrentFrame);
        Assert.Equal(61.04, tunnel.UpdateTime);
        Assert.NotEqual(pose, tunnel.PartFrames.ToArray());

        // Shown again 1.01 s after its last step: the first step catches up on the hidden time.
        tunnel.Stop();
        tunnel.Restart();
        tunnel.AdvanceTo(62.05);
        Assert.Equal(41, tunnel.CurrentFrame);
    }

    [RequiresDatsFact]
    public void The_timed_tunnel_agrees_with_the_real_tunnel_frame_for_frame()
    {
        var real = NewTunnel();
        var timed = new TimedTunnel();
        real.Restart();
        timed.Restart();

        for (var k = 1; k <= 3 * 179; k++)
        {
            var t = 5.0 + (k / 60.0);
            real.AdvanceTo(t);
            timed.AdvanceTo(t);
            Assert.Equal(real.Spin.FrameNumber, timed.FrameNumber);
            Assert.Equal(real.CurrentFrame, timed.CurrentFrame);
        }

        // A hitch, a skipped gap and a restart, the same way.
        foreach (var t in new[] { 14.5, 17.0, 17.3 })
        {
            real.AdvanceTo(t);
            timed.AdvanceTo(t);
            Assert.Equal(real.Spin.FrameNumber, timed.FrameNumber);
        }

        real.Stop();
        timed.Stop();
        Assert.Equal(real.CurrentFrame, timed.CurrentFrame);
        real.Restart();
        timed.Restart();
        real.AdvanceTo(20.7);
        timed.AdvanceTo(20.7);
        Assert.Equal(real.Spin.FrameNumber, timed.FrameNumber);

        // A restart within 2 s of the last step, caught up the same way.
        real.Stop();
        timed.Stop();
        real.Restart();
        timed.Restart();
        real.AdvanceTo(21.9);
        timed.AdvanceTo(21.9);
        Assert.Equal(real.Spin.FrameNumber, timed.FrameNumber);
        Assert.Equal(real.UpdateTime, timed.UpdateTime);
    }

    [RequiresDatsFact]
    public void A_missing_animation_fails_loudly()
    {
        var assets = PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps) with { TunnelAnimationId = 0x0300FFFE };

        Assert.Throws<InvalidDataException>(() => TunnelObject.Create(fixture.Dats, assets));
    }
}
