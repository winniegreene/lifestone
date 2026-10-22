using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary><c>attack_frame_max_ms</c> (M4 1.3 s2): the worst frame from an attack's send to its run's Done, judged against the previous call's window state as the fight sampler's flags are.</summary>
public sealed class AttackFrameSamplerTests
{
    [Fact]
    public void Nothing_is_sampled_outside_a_window()
    {
        var sampler = new AttackFrameSampler();
        sampler.Step(0.5, false);
        sampler.Step(0.5, false);
        Assert.Null(sampler.AttackFrameMaxMs);
        Assert.Equal(0, sampler.Frames);
    }

    [Fact]
    public void The_worst_frame_from_the_send_to_the_done_is_kept_and_the_frame_that_applied_the_send_does_not_count()
    {
        var sampler = new AttackFrameSampler();
        sampler.Step(0.004, false);
        sampler.Open(); // the frame that applies the send ...
        sampler.Step(0.200, false); // ... its delta is the frame before the send: not in the window
        sampler.Step(0.010, false);
        sampler.Step(0.030, false);
        sampler.Close(); // the frame that applies the Done: its delta is a frame of the run
        sampler.Step(0.040, false);
        sampler.Step(0.900, false); // after the window
        Assert.Equal(40.0, sampler.AttackFrameMaxMs!.Value, 6);
        Assert.Equal(3, sampler.Frames);
        Assert.Equal(1, sampler.Windows);
    }

    [Fact]
    public void A_second_send_inside_a_run_opens_no_second_window_and_a_second_run_is_counted_in_the_same_maximum()
    {
        var sampler = new AttackFrameSampler();
        sampler.Open();
        sampler.Step(0.001, false);
        sampler.Open();
        sampler.Step(0.020, false);
        sampler.Close();
        sampler.Step(0.001, false);
        sampler.Close(); // a Done with no window open is ignored
        sampler.Open();
        sampler.Step(0.001, false);
        sampler.Step(0.060, false);
        sampler.Close();
        sampler.Step(0.001, false);
        Assert.Equal(60.0, sampler.AttackFrameMaxMs!.Value, 6);
        Assert.Equal(2, sampler.Runs);
        Assert.Equal(2, sampler.Windows);
    }

    [Fact]
    public void A_skipped_or_bad_frame_is_left_out()
    {
        var sampler = new AttackFrameSampler();
        sampler.Open();
        sampler.Step(0.001, false);
        sampler.Step(0.700, true);
        sampler.Step(0, false);
        sampler.Step(double.NaN, false);
        sampler.Step(0.012, false);
        Assert.Equal(12.0, sampler.AttackFrameMaxMs!.Value, 6);
    }

    [Fact]
    public void The_session_s_send_and_done_events_open_and_close_the_window()
    {
        var r = new AttackRig();
        r.Creature(AttackRig.Reedshark, "Adult Reedshark");
        var sampler = new AttackFrameSampler();
        using var attached = sampler.Attach(r.W.H.R.Bus);

        r.Api.Attack(AttackRig.Reedshark, AttackHeight.Medium, 0.5f, ActionSource.Host);
        Assert.False(sampler.WindowOpen);
        r.Tick(0.5);
        Assert.True(sampler.WindowOpen); // the send reached the wire
        r.OwnMotion(AttackRig.SlashMed, AttackRig.Reedshark);
        r.Done(0); // a swing over: the run goes on
        Assert.True(sampler.WindowOpen);
        r.Done(AttackRig.Over); // 0x0036: the run is over
        Assert.False(sampler.WindowOpen);
        Assert.Equal(1, sampler.Windows);
    }
}
