using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary><c>loot_frame_max_ms</c> (M4 2.2 s0): the worst frame from a corpse's open being sent to its close being settled or abandoned, judged against the previous call's window state as the attack sampler's is.</summary>
public sealed class LootFrameSamplerTests
{
    [Fact]
    public void Nothing_is_sampled_outside_a_window()
    {
        var sampler = new LootFrameSampler();
        sampler.Step(0.5, false);
        sampler.Step(0.5, false);

        Assert.Null(sampler.LootFrameMaxMs);
        Assert.Equal(0, sampler.Frames);
        Assert.False(sampler.WindowOpen);
    }

    [Fact]
    public void The_worst_frame_from_the_open_to_the_close_is_kept_and_the_frame_that_applied_the_open_does_not_count()
    {
        var sampler = new LootFrameSampler();
        sampler.Step(0.004, false);
        sampler.Open(); // the frame that applies the send ...
        sampler.Step(0.200, false); // ... its delta is the frame before the send: not in the window
        sampler.Step(0.010, false);
        sampler.Step(0.030, false);
        sampler.Close(); // the frame that applies the close's settlement: its delta is a frame of the window
        sampler.Step(0.040, false);
        sampler.Step(0.900, false); // after the window

        Assert.Equal(40.0, sampler.LootFrameMaxMs!.Value, 6);
        Assert.Equal(3, sampler.Frames);
        Assert.Equal(1, sampler.Windows);
    }

    [Fact]
    public void A_skipped_frame_is_left_out_and_an_open_twice_is_one_window()
    {
        var sampler = new LootFrameSampler();
        sampler.Open();
        sampler.Step(0.001, false);
        sampler.Open(); // opened again with no close between: still one window
        sampler.Step(0.300, true); // the frame after a saved review frame (the read-back stalls it)
        sampler.Step(0.020, false);
        sampler.Close();
        sampler.Close(); // a close with no window open is ignored

        Assert.Equal(20.0, sampler.LootFrameMaxMs!.Value, 6);
        Assert.Equal(1, sampler.Windows);
        Assert.Equal(1, sampler.Frames);
    }

    [Fact]
    public void Track_opens_and_closes_the_window_from_the_scenarios_flag_each_frame()
    {
        var sampler = new LootFrameSampler();
        sampler.Track(false);
        sampler.Step(0.100, false);
        sampler.Track(true); // the scenario sent the open
        sampler.Step(0.100, false); // not counted
        sampler.Track(true);
        sampler.Step(0.045, false);
        sampler.Track(false); // the close settled
        sampler.Step(0.020, false);
        sampler.Track(false);
        sampler.Step(0.500, false);

        Assert.Equal(45.0, sampler.LootFrameMaxMs!.Value, 6);
        Assert.Equal(2, sampler.Frames);
        Assert.Equal(1, sampler.Windows);
    }

    [Fact]
    public void A_zero_or_not_a_number_delta_is_not_a_frame()
    {
        var sampler = new LootFrameSampler();
        sampler.Open();
        sampler.Step(0.0, false);
        sampler.Step(double.NaN, false);
        sampler.Step(-1.0, false);
        sampler.Step(0.012, false);

        Assert.Equal(12.0, sampler.LootFrameMaxMs!.Value, 6);
        Assert.Equal(1, sampler.Frames);
    }
}
