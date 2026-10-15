using Lifestone.Client.Play;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// "Automation uses only accounts.test" in the game itself (M2 G6 review): <c>godot.ps1 -Offscreen -GameArgs:'--login-check=enter'</c> with no
/// <c>--account</c> must not fall back to secrets.json's default (the owner's play account), and <c>--account=play</c> with it is refused.
/// The game's <c>SessionHost.Start</c> applies <see cref="AutomatedAccountRule"/> (the architecture pin below); the Godot project has no test
/// project, so the rule lives in the Client and is tested here.
/// </summary>
public sealed class AutomatedAccountRuleTests
{
    private const string SessionHostFile = "src/Lifestone.Game/scripts/Session/SessionHost.cs";

    /// <summary>What <c>SessionHost.Start</c> computes: the run is automated when it ends by itself or runs a login check.</summary>
    private static bool Automated(IEnumerable<string> args, bool endsRun = false) => endsRun || PlayOptions.Parse(args).LoginCheck != LoginCheckKind.None;

    [Fact]
    public void A_login_check_with_no_account_resolves_to_the_test_account_not_the_default()
    {
        string[] args = ["--login-check=enter"];
        var session = SessionOptions.Parse(args);
        Assert.Null(session.Account); // what SessionBootstrap would turn into AccountChoice.Default: the play account

        Assert.True(AutomatedAccountRule.TryApply(session, Automated(args), out var applied, out var refusal));
        Assert.Null(refusal);
        Assert.Equal(PlayAccounts.TestKey, applied.Account);

        // End to end through the bootstrap's own resolution (SessionBootstrap.CreateLive): the test account's username, not the play account's.
        using var credentials = PlayAccounts.Parse(SessionTestData.SecretsV2(), AccountChoice.FromOption(applied.Account, AccountChoice.Default));
        Assert.Equal("lifestone-test", credentials.Account);
    }

    [Fact]
    public void A_login_check_on_the_play_account_or_any_other_is_refused_with_the_rule_and_no_account_name()
    {
        foreach (var account in new[] { "play", "alice", "Alice-Alt", "lifestone", "TEST", "lifestone-test", "hunter2-typo" })
        {
            string[] args = ["--play", $"--account={account}", "--login-check=enter"];
            var session = SessionOptions.Parse(args);
            Assert.Empty(session.Errors);

            Assert.False(AutomatedAccountRule.TryApply(session, Automated(args), out var applied, out var refusal), account);
            Assert.Same(session, applied);
            Assert.NotNull(refusal);
            Assert.Contains("only the test account", refusal, StringComparison.Ordinal);
            Assert.Contains("--account=test", refusal, StringComparison.Ordinal);
            if (account is "hunter2-typo" or "Alice-Alt" or "lifestone-test")
            {
                Assert.DoesNotContain(account, refusal, StringComparison.Ordinal); // a mistyped option could be a password: never repeated
            }
        }
    }

    [Fact]
    public void A_login_check_on_the_test_account_goes_on_unchanged()
    {
        string[] args = ["--account=test", "--login-check=enter"];
        var session = SessionOptions.Parse(args);
        Assert.True(AutomatedAccountRule.TryApply(session, Automated(args), out var applied, out var refusal));
        Assert.Null(refusal);
        Assert.Same(session, applied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("play")]
    [InlineData("alice")]
    public void An_owner_run_that_is_not_automated_keeps_its_account(string? account)
    {
        var session = new SessionOptions { Play = true, Account = account };
        Assert.True(AutomatedAccountRule.TryApply(session, automated: false, out var applied, out var refusal));
        Assert.Null(refusal);
        Assert.Same(session, applied);
    }

    [Fact]
    public void Any_run_that_ends_by_itself_is_held_to_the_rule_too()
    {
        var session = new SessionOptions { Play = true };
        Assert.True(AutomatedAccountRule.TryApply(session, Automated([], endsRun: true), out var applied, out _));
        Assert.Equal(PlayAccounts.TestKey, applied.Account);

        var play = new SessionOptions { Play = true, Account = "play" };
        Assert.False(AutomatedAccountRule.TryApply(play, Automated([], endsRun: true), out _, out var refusal));
        Assert.NotNull(refusal);
    }

    [Fact]
    public void A_blank_account_counts_as_none()
    {
        Assert.True(AutomatedAccountRule.TryApply(new SessionOptions { Account = "  " }, automated: true, out var applied, out _));
        Assert.Equal(PlayAccounts.TestKey, applied.Account);
    }

    [Fact]
    public void SessionHost_applies_the_rule_before_it_resolves_the_account()
    {
        // The game has no test project, so the call is pinned in its source: the rule runs, on login checks and automated runs, before the bootstrap.
        var code = CheckoutFiles.ReadCSharpCode(SessionHostFile);
        var rule = code.IndexOf("AutomatedAccountRule.TryApply(", StringComparison.Ordinal);
        var bootstrap = code.IndexOf("SessionBootstrap.CreateLive(", StringComparison.Ordinal);
        Assert.True(rule >= 0, "SessionHost.Start must apply AutomatedAccountRule");
        Assert.True(bootstrap > rule, "AutomatedAccountRule must be applied before SessionBootstrap.CreateLive");
        Assert.Contains("IsAutomated", code, StringComparison.Ordinal);
        Assert.Contains("LoginCheck != LoginCheckKind.None", code, StringComparison.Ordinal);
        Assert.Contains("CreateLive(Options,", code, StringComparison.Ordinal); // the bootstrap gets the rule's options, not services.Options.Session
    }
}
