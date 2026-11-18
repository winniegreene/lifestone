using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.StandIn;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.PlayerRules;

/// <summary>
/// <c>--autopilot-player-rules on|off</c> (M6 item 15): on by default, listed with the autopilot's options for both hosts, read by the session's options (the rule is the session's: every automated path passes it) and handed to
/// the session by the bootstrap.
/// </summary>
public sealed class PlayerRulesOptionTests
{
    [Fact]
    public void The_rule_is_on_by_default_and_the_option_turns_the_whole_rule_off()
    {
        Assert.True(SessionOptions.Parse([]).PlayerRules);
        Assert.True(SessionOptions.Parse(["--autopilot-player-rules"]).PlayerRules);
        Assert.True(SessionOptions.Parse(["--autopilot-player-rules=on"]).PlayerRules);
        Assert.False(SessionOptions.Parse(["--autopilot-player-rules=off"]).PlayerRules);
        Assert.Empty(SessionOptions.Parse(["--autopilot-player-rules=off"]).Errors);
        Assert.True(SessionOptions.Parse(["--autopilot-player-rules=off", "--autopilot-player-rules=on"]).PlayerRules); // the last one wins, as every switch

        var bad = SessionOptions.Parse(["--autopilot-player-rules=sometimes"]);
        Assert.Contains("not on or off", Assert.Single(bad.Errors), StringComparison.Ordinal);
        Assert.True(bad.PlayerRules); // a bad value keeps the default
    }

    [Fact]
    public void The_option_is_registered_with_the_autopilots_for_the_main_scene_and_the_headless_host()
    {
        var registry = OptionCatalog.Registry;
        Assert.Equal("Autopilot", registry.FeatureOf("--autopilot-player-rules"));
        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, registry.ScenesOf("--autopilot-player-rules"));
        Assert.Equal("--autopilot-player-rules=[on|off]", registry.Find("--autopilot-player-rules")!.Usage); // as the autopilot's other switches are listed
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--autopilot-player-rules=off"]), OptionScenes.Main));
        Assert.Empty(registry.Problems(CommandLineArgument.ParseAll(["--autopilot-player-rules"]), OptionScenes.Headless));
        Assert.Contains("--autopilot-player-rules", OptionCatalog.Autopilot().Options.Select(o => o.Name));
    }

    [Fact]
    public void A_session_enforces_the_rule_unless_it_is_told_not_to_and_the_bootstrap_hands_the_option_to_the_live_session()
    {
        using var offline = GameSession.Offline(static h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), new RecordingLogSink());
        Assert.True(offline.PlayerRules);
        Assert.True(offline.ActionApi.PlayerRules.On);
        offline.PlayerRules = false;
        Assert.False(offline.ActionApi.PlayerRules.On);

        using var on = new LiveHarness(record: false);
        Assert.True(on.Session.PlayerRules);
        using var off = new LiveHarness(record: false, options: SessionOptions.Parse(["--account=test", "--autopilot-player-rules=off"]));
        Assert.False(off.Session.PlayerRules);
        Assert.Contains(off.Log.Entries, e => e.Message.StartsWith("live session created", StringComparison.Ordinal) && e.Fields.Any(f => f is { Key: "playerRules", Value: false }));
    }
}
