using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session;

/// <summary>The vendor window's frame measure (M4 3.2 s4a, <see cref="VendorFrameSampler"/>; <c>vendor_frame_max_ms</c>, report-only until 4.2): the window rules it shares with the loot sampler, and two windows for the two vendors.</summary>
public sealed class VendorFrameSamplerTests
{
    [Fact]
    public void Nothing_is_sampled_outside_a_window_and_the_field_reads_null()
    {
        var s = new VendorFrameSampler();
        s.Step(0.200, skip: false);
        s.Step(0.016, skip: false);

        Assert.Null(s.VendorFrameMaxMs);
        Assert.Equal(0, s.Frames);
        Assert.Equal(0, s.Windows);
        Assert.False(s.WindowOpen);
    }

    [Fact]
    public void The_slowest_frame_between_the_open_and_the_end_is_the_value_and_the_frame_that_applied_the_send_does_not_count()
    {
        var s = new VendorFrameSampler();
        s.Track(true); // the scenario sent the open in this frame
        s.Step(0.120, skip: false); // the frame before the send, whose delta is not the window's
        s.Step(0.016, skip: false);
        s.Step(0.040, skip: false);
        s.Step(0.020, skip: false);
        s.Track(false); // the visit ended in this frame
        s.Step(0.030, skip: false); // the frame that applied the end counts: it is a frame of the window
        s.Step(0.500, skip: false); // after the window: not counted

        Assert.Equal(40.0, s.VendorFrameMaxMs!.Value, 6);
        Assert.Equal(4, s.Frames);
        Assert.Equal(1, s.Windows);
        Assert.False(s.WindowOpen);
    }

    [Fact]
    public void A_skipped_frame_is_left_out_and_a_bad_delta_is_ignored()
    {
        var s = new VendorFrameSampler();
        s.Track(true);
        s.Step(0.010, skip: false);
        s.Step(0.300, skip: true); // the frame after a saved review frame
        s.Step(double.NaN, skip: false);
        s.Step(0, skip: false);
        s.Step(-1, skip: false);
        s.Step(0.012, skip: false);

        Assert.Equal(12.0, s.VendorFrameMaxMs!.Value, 6);
        Assert.Equal(1, s.Frames);
    }

    [Fact]
    public void Both_vendors_have_a_window_and_the_worst_frame_of_either_is_kept_a_second_open_is_one_window()
    {
        var s = new VendorFrameSampler();
        s.Track(true);
        s.Open(); // an open twice without an end is one window
        s.Step(0.001, skip: false);
        s.Step(0.030, skip: false);
        s.Track(false);
        s.Step(0.010, skip: false);
        s.Close(); // an end with no window open is ignored
        s.Track(true);
        s.Step(0.001, skip: false);
        s.Step(0.080, skip: false);
        s.Step(0.020, skip: false);
        s.Track(false);
        s.Step(0.005, skip: false);

        Assert.Equal(2, s.Windows);
        Assert.Equal(80.0, s.VendorFrameMaxMs!.Value, 6);
        Assert.Equal(VendorFrameSampler.WarnMs, 50.0);
    }
}
