using Lifestone.Client.Options;
using Lifestone.Client.Play;

namespace Lifestone.Client.Tests.Play;

/// <summary>M3 1.4 s1: <c>--play-screenshot</c> and <c>--play-screenshot-after</c> (a review frame of the placed player in play mode) parse, default to 5 s, report bad values by name and are registered with help.</summary>
public sealed class PlayScreenshotOptionTests
{
    [Fact]
    public void The_screenshot_path_and_the_wait_parse_and_default_to_five_seconds()
    {
        var none = PlayOptions.Parse([]);
        Assert.Null(none.ScreenshotPath);
        Assert.Equal(5, none.ScreenshotAfterSeconds);

        var set = PlayOptions.Parse(["--play-screenshot=cache/m3/1-4/gear.png", "--play-screenshot-after=7.5"]);
        Assert.Empty(set.Errors);
        Assert.Equal("cache/m3/1-4/gear.png", set.ScreenshotPath);
        Assert.Equal(7.5, set.ScreenshotAfterSeconds);
    }

    [Theory]
    [InlineData("--play-screenshot-after=-1", "--play-screenshot-after: '-1' is not 0 to 600")]
    [InlineData("--play-screenshot-after=601", "--play-screenshot-after: '601' is not 0 to 600")]
    [InlineData("--play-screenshot=", "--play-screenshot: needs a file path (--name=VALUE)")]
    public void A_bad_value_is_reported_by_name_and_the_default_is_kept(string argument, string expected)
    {
        var o = PlayOptions.Parse([argument]);
        Assert.Equal(expected, Assert.Single(o.Errors));
        Assert.Null(o.ScreenshotPath);
        Assert.Equal(5, o.ScreenshotAfterSeconds);
    }

    [Fact]
    public void Both_options_are_registered_in_the_play_group_with_help()
    {
        var play = OptionCatalog.Play();
        foreach (var name in new[] { "--play-screenshot", "--play-screenshot-after" })
        {
            var option = Assert.Single(play.Options, o => o.Name == name);
            Assert.False(string.IsNullOrWhiteSpace(option.Help));
        }
    }
}
