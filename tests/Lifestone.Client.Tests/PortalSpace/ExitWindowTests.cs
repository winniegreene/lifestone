using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS7: the state-4 exit test of <c>gmSmartBoxUI::UseTime</c>. Retail's float rounding admits frames 68 to 76
/// (D27; docs/research/retail-addresses-m2.md §1.1), where exact arithmetic would admit 69 to 75.
/// </summary>
public sealed class ExitWindowTests
{
    [Fact]
    public void The_window_is_frames_68_to_76()
    {
        var admitted = Enumerable.Range(-5, 300).Where(ExitWindow.Admits).ToArray();

        Assert.Equal(Enumerable.Range(ExitWindow.FirstFrame, ExitWindow.LastFrame - ExitWindow.FirstFrame + 1), admitted);
        Assert.Equal((68, 76), (ExitWindow.FirstFrame, ExitWindow.LastFrame));
    }

    [Fact]
    public void The_bounds_are_the_float_constants_widened_and_added_in_double()
    {
        Assert.Equal(1.1000000014901161, ExitWindow.LowerBound);
        Assert.Equal(1.3000000119209290, ExitWindow.UpperBound);
    }

    [Theory]
    [InlineData(67, 1.325f, false)]
    [InlineData(68, 1.3f, true)] // float(1.3) = 1.29999995 is below 1.30000001
    [InlineData(76, 1.1f, true)] // float(1.1) = 1.10000002 is above 1.10000000149
    [InlineData(77, 1.075f, false)]
    public void The_pinned_frames_fall_either_side_by_the_float_rounding(int frame, float timeLeft, bool admitted)
    {
        Assert.Equal(timeLeft, ExitWindow.TimeLeftInLoop(frame));
        Assert.Equal(admitted, ExitWindow.Admits(frame));
    }

    [Fact]
    public void Exact_arithmetic_would_admit_only_69_to_75()
    {
        var exact = Enumerable.Range(0, 120).Where(f => (120 - f) / 40m is > 1.1m and < 1.3m);

        Assert.Equal(Enumerable.Range(69, 7), exact);
    }

    [Fact]
    public void A_frame_number_above_120_wraps_as_retail_s_unsigned_subtraction()
    {
        Assert.Equal((float)((4294967296.0 - 1.0) / 40.0), ExitWindow.TimeLeftInLoop(121));
        Assert.False(ExitWindow.Admits(121));
    }

    [Theory]
    [InlineData(1.999, 70, false)]
    [InlineData(2.0, 70, true)]
    [InlineData(2.0, 67, false)]
    [InlineData(4.999, 77, false)]
    [InlineData(5.0, 77, true)]
    public void The_hold_ends_after_2_s_in_the_window_or_at_5_s(double elapsed, int frame, bool ends)
    {
        Assert.Equal(ends, ExitWindow.HoldEnds(elapsed, frame));
    }
}
