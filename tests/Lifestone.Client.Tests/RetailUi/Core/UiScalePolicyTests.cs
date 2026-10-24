using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Tests.RetailUi.TestSupport;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>The integer UI scale (D37; m2-log-in.md §5.4): the largest scale leaving at least 800×600 UI pixels.</summary>
public sealed class UiScalePolicyTests
{
    [Theory]
    [InlineData(800, 600, 1, 800, 600)]
    [InlineData(1920, 1080, 1, 1920, 1080)]
    [InlineData(1600, 1200, 2, 800, 600)]
    [InlineData(3840, 2160, 3, 1280, 720)]
    [InlineData(1700, 1300, 2, 850, 650)]
    public void The_scale_is_the_largest_that_leaves_retails_minimum(int width, int height, int scale, int logicalWidth, int logicalHeight)
    {
        var chosen = UiScalePolicy.Choose(width, height);

        Assert.Equal(scale, chosen.Scale);
        Assert.Equal(logicalWidth, chosen.LogicalWidth);
        Assert.Equal(logicalHeight, chosen.LogicalHeight);
        Assert.False(chosen.BelowMinimum);
    }

    [Fact]
    public void A_window_below_800_by_600_gets_scale_1_and_is_flagged()
    {
        var chosen = UiScalePolicy.Choose(640, 480);

        Assert.Equal(1, chosen.Scale);
        Assert.True(chosen.BelowMinimum);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void The_override_is_honoured_as_given(int forced)
    {
        var chosen = UiScalePolicy.Choose(3840, 2160, forced);

        Assert.Equal(forced, chosen.Scale);
        Assert.Equal(3840 / forced, chosen.LogicalWidth);
    }

    [Fact]
    public void The_override_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UiScalePolicy.Choose(800, 600, 0));

    [Fact]
    public void A_manager_logs_a_window_below_the_minimum_as_a_placeholder()
    {
        var log = new RecordingLogSink();
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), Log = log });

        manager.SetScale(UiScalePolicy.Choose(640, 480));

        Assert.Equal((640, 480), manager.DisplaySize);
        Assert.Contains(log.Lines, l => l.Contains("fewer than retail's 800x600", StringComparison.Ordinal) && l.Contains("placeholder", StringComparison.Ordinal));
    }

    [Fact]
    public void A_forced_display_is_centred_in_the_logical_window()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]) }, UiScalePolicy.Choose(2560, 1440));

        manager.ForceDisplayResolution(true);

        Assert.Equal((800, 600), manager.DisplaySize);
        Assert.Equal((240, 60), manager.DisplayOrigin);
        Assert.Equal(new Lifestone.Data.Ui.UiBox(0, 0, 799, 599), manager.RootElement.Box);

        manager.ForceDisplayResolution(false);

        Assert.Equal((1280, 720), manager.DisplaySize);
        Assert.Equal((0, 0), manager.DisplayOrigin);
    }
}
