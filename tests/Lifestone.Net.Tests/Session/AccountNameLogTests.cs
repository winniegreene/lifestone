using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Session;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// M4 1.1 s3 (m3-plan review R8): no session log line carries the account name. The login line says that a LoginRequest was sent, to which server and
/// which attempt; character select says how many characters; neither names the account (the name is a credential-adjacent detail and the logs are
/// kept on disk).
/// </summary>
public sealed class AccountNameLogTests
{
    private const string UniqueAccount = "zq-account-7731";

    [Fact]
    public void No_session_log_line_carries_the_account_name_through_login_world_and_logoff()
    {
        var script = SessionRig.DefaultScript();
        var account = script.AddAccount(UniqueAccount, SessionRig.Password);
        account.Characters.Add(new FakeCharacter(SessionRig.HeroGuid + 1, "Other") { Position = script.StartPosition });
        using var rig = new SessionRig(script);

        rig.ToCharacterSelect(UniqueAccount, SessionRig.Password);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.Phase);
        // M4 1.2 s5 (review R11): the rig reaches the world, so the test now covers it (ToWorld takes no account, so the steps are spelled out).
        rig.Session.EnterWorld(SessionRig.HeroGuid + 1);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(5)), $"stuck in {rig.Session.Phase} ({rig.Session.Failure})");
        rig.RunFor(TimeSpan.FromSeconds(1));
        rig.Session.Quit();
        rig.Harness.Step();

        Assert.NotEmpty(rig.Log.Lines);
        Assert.All(rig.Log.Lines, line => Assert.DoesNotContain(UniqueAccount, line.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_login_line_still_names_the_server_and_the_attempt()
    {
        using var rig = new SessionRig();

        rig.Login();
        rig.RunFor(TimeSpan.FromMilliseconds(50));

        var line = Assert.Single(rig.Log.Lines, l => l.Message.StartsWith("login: LoginRequest sent to ", StringComparison.Ordinal));
        Assert.Contains(rig.Options.Server.ToString(), line.Message, StringComparison.Ordinal);
        Assert.Contains("(attempt 1 of ", line.Message, StringComparison.Ordinal);
    }
}
