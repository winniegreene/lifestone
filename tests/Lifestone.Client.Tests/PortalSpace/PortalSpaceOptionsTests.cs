using Lifestone.Client.Options;
using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>The Portal space option group (docs/portal-space.md §12) and its parser.</summary>
public sealed class PortalSpaceOptionsTests
{
    [Fact]
    public void Defaults_play_the_transition_and_hold_nothing()
    {
        var o = PortalSpaceOptions.Parse([]);
        Assert.True(o.Transition);
        Assert.False(o.Hold);
        Assert.Null(o.FramesDirectory);
        Assert.False(o.RetailLogoffTunnel);
        Assert.Equal(LogOffTunnelPolicy.HoldUntilCharacterList, o.LogOffPolicy);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--portal-transition=off", false)]
    [InlineData("--portal-transition=on", true)]
    [InlineData("--portal-transition", true)]
    [InlineData("--portal-transition=no", false)]
    public void The_transition_switch_reads_on_and_off(string argument, bool expected) =>
        Assert.Equal(expected, PortalSpaceOptions.Parse([argument]).Transition);

    [Fact]
    public void A_bad_transition_value_is_reported_and_keeps_the_default()
    {
        var o = PortalSpaceOptions.Parse(["--portal-transition=maybe"]);
        Assert.True(o.Transition);
        Assert.Single(o.Errors);
    }

    [Fact]
    public void The_hold_and_the_retail_logoff_tunnel_are_flags()
    {
        var o = PortalSpaceOptions.Parse(["--portal-space", "--retail-logoff-tunnel"]);
        Assert.True(o.Hold);
        Assert.True(o.RetailLogoffTunnel);
        Assert.Equal(LogOffTunnelPolicy.Retail, o.LogOffPolicy);
    }

    [Fact]
    public void The_frames_directory_defaults_and_can_be_given()
    {
        Assert.Equal(PortalSpaceOptions.DefaultFramesDirectory, PortalSpaceOptions.Parse(["--portal-space-frames"]).FramesDirectory);
        Assert.Equal("cache/x/frames", PortalSpaceOptions.Parse(["--portal-space-frames=cache/x/frames"]).FramesDirectory);
        Assert.Equal("docs/screenshots/m2/portal-space", PortalSpaceOptions.DefaultFramesDirectory);
    }

    [Fact]
    public void Other_groups_arguments_are_skipped()
    {
        var o = PortalSpaceOptions.Parse(["--screenshot=x.png", "--volume=0.5", "--portal-space"]);
        Assert.True(o.Hold);
        Assert.Empty(o.Errors);
    }

    [Fact]
    public void The_group_is_registered_for_the_main_scene_and_the_logoff_tunnel_also_for_the_headless_host()
    {
        var registry = OptionCatalog.Registry;
        foreach (var name in new[] { "--portal-transition", "--portal-space", "--portal-space-frames", "--retail-logoff-tunnel" })
        {
            Assert.True(registry.AppliesTo(name, OptionScenes.Main), name);
            Assert.False(registry.AppliesTo(name, OptionScenes.SpellViewer), name);
        }

        Assert.True(registry.AppliesTo("--retail-logoff-tunnel", OptionScenes.Headless));
        Assert.False(registry.AppliesTo("--portal-space", OptionScenes.Headless));
    }

    [Fact]
    public void Only_the_scripted_capture_ends_the_run()
    {
        var registry = OptionCatalog.Registry;
        Assert.True(registry.EndsRun(CommandLineArgument.ParseAll(["--portal-space-frames"]), OptionScenes.Main));
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--portal-space"]), OptionScenes.Main));
        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--portal-transition=off", "--retail-logoff-tunnel"]), OptionScenes.Main));
    }
}
