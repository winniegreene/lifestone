using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Tests;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// M5 item 4 h1: the game's <c>--login-check=autopilot</c> is a kind of its own: it parses, is not part of <c>all</c> or the bare form, takes the mage's account only, sends <c>--play-screenshot</c> to the review frames, is listed in
/// <c>--help</c>, and is hooked by <c>LoginCheckRunner.Install</c> before the generic path (never reached by the <c>all</c> check).
/// </summary>
public sealed class AutopilotLoginCheckTests
{
    [Fact]
    public void The_kind_parses_and_the_bare_form_and_all_are_not_it()
    {
        Assert.Equal(LoginCheckKind.Autopilot, PlayOptions.Parse(["--login-check=autopilot"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
        Assert.Empty(PlayOptions.Parse(["--login-check=autopilot", "--autopilot-minutes=1"]).Errors);
        Assert.Empty(PlayOptions.Parse(["--login-check=autopilot", "--account=test"]).Errors);
    }

    [Fact]
    public void The_check_is_the_mages_on_the_test_account_and_takes_no_journey()
    {
        var wrong = Assert.Single(PlayOptions.Parse(["--login-check=autopilot", "--account=test-melee"]).Errors);
        Assert.StartsWith("--login-check=autopilot:", wrong, StringComparison.Ordinal);
        Assert.Contains("--journey: only --login-check=journey reads it", PlayOptions.Parse(["--login-check=autopilot", "--journey=mage"]).Errors);
    }

    [Fact]
    public void The_screenshot_path_goes_to_the_review_frames_not_the_generic_screenshot()
    {
        var options = PlayOptions.Parse(["--login-check=autopilot", "--play-screenshot=C:/x/frame.png"]);
        Assert.Equal("C:/x/frame.png", options.BuffShotPath);
        Assert.Null(options.ScreenshotPath);
    }

    [Fact]
    public void Help_lists_the_check_and_the_headless_help_lists_the_scenario()
    {
        var main = OptionCatalog.Registry.FormatHelp("preamble", OptionScenes.Main);
        Assert.Contains("--login-check=autopilot", main, StringComparison.Ordinal);
        Assert.Contains("--autopilot-minutes", main, StringComparison.Ordinal);
        var headless = OptionCatalog.Registry.FormatHelp("preamble", OptionScenes.Headless);
        Assert.Contains("wayfind|journey|autopilot", headless, StringComparison.Ordinal);
        Assert.Contains("--autopilot-level", headless, StringComparison.Ordinal);
    }

    [Fact]
    public void The_runner_hooks_the_kind_on_its_own_and_the_leg_shares_the_journeys_pieces()
    {
        var runner = Script("src", "Lifestone.Game", "scripts", "Session", "LoginCheckRunner.cs");
        var hook = runner.IndexOf("if (kind == LoginCheckKind.Autopilot)", StringComparison.Ordinal);
        Assert.True(hook > 0 && hook < runner.IndexOf("if (kind != LoginCheckKind.Enter)", StringComparison.Ordinal), "the autopilot kind is hooked before the generic path");
        Assert.Contains("AutopilotLegRunner.Install(main, services, host, mode, ui);", runner, StringComparison.Ordinal);

        var leg = Script("src", "Lifestone.Game", "scripts", "Session", "AutopilotLegRunner.cs");
        // One way to build the journey's context (M5 4 h3: the play host's dial and the leg share AutopilotKit and KitLoads, which call the journey's own BuildParts and StartLoads).
        var host = Script("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs");
        Assert.Contains("JourneyLegRunner.BuildParts(", host, StringComparison.Ordinal);
        Assert.Contains("JourneyLegRunner.StartLoads(", host, StringComparison.Ordinal);
        Assert.Contains("new AutopilotKit(", leg, StringComparison.Ordinal);
        Assert.Contains("new KitLoads(", leg, StringComparison.Ordinal);
        Assert.Contains("new AutopilotHostLogic(", leg, StringComparison.Ordinal); // the leg steps the same dial as the play host
        Assert.Contains("new LegSessionFlow(", leg, StringComparison.Ordinal); // the login and the log off are shared, not copied
        Assert.DoesNotContain("SelectCharacter", leg, StringComparison.Ordinal);
        Assert.DoesNotContain("IAutopilotSession", leg, StringComparison.Ordinal); // defined once, in AutopilotHost.cs and the client
        Assert.DoesNotContain("IAutopilotCheckpointStore", leg, StringComparison.Ordinal);
    }

    private static string Script(params string[] parts) => File.ReadAllText(RepositoryFiles.PathOf(parts)).Replace("\r\n", "\n", StringComparison.Ordinal);
}
