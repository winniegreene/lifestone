using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The login retry policy (owner decision D18, m2-plan §3.3 N5): CharacterError 0x01 (the account is still logged in)
/// and 0x15 (the world is not open) start another attempt from a fresh socket, 3 s later: D18's 3 retries, so up to 4
/// attempts in all;
/// <see cref="LoginRetryPolicy.Retail"/> makes one.
/// </summary>
public sealed class RetryTests
{
    [Fact]
    public void Account_in_use_is_retried_and_the_second_attempt_succeeds()
    {
        using var rig = new SessionRig();

        // Someone else holds the account: the session's login boots them, and both get 0x01 (account_login_boots_in_use).
        using var other = new RawAceClient(rig.Harness);
        other.SendLoginRequest(SessionRig.Account, SessionRig.Password);
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        other.Drain();
        other.SendConnectResponse();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.ToCharacterSelect();

        var retry = Assert.Single(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal((2, (uint)CharacterErrorCode.Logon), (retry.Attempt, retry.Code));
        Assert.Equal(2, rig.SocketsOpened);
        Assert.Equal(FakeAceTermination.AccountLoggedIn, rig.Server.History[0].TerminationReason);
        Assert.Equal(FakeAceTermination.AccountInUse, rig.Server.History[1].TerminationReason);
        Assert.Null(rig.Server.History[2].TerminationReason);
    }

    [Fact]
    public void A_closed_world_is_tried_four_times_three_seconds_apart_then_fails()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script);
        var logins = new List<TimeSpan>();
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if ((BitConverter.ToUInt32(datagram, 4) & 0x10000) != 0)
            {
                logins.Add(rig.Harness.Time.Elapsed);
            }

            return false;
        };
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(20)));
        Assert.Equal((SessionFailureKind.CharacterError, (uint)CharacterErrorCode.LogonServerFull), (rig.Session.Failure!.Kind, rig.Session.Failure.Code));
        var retries = rig.Sink.OfKind(SessionEventKind.RetryScheduled).ToList();
        Assert.Equal(new[] { 2, 3, 4 }, retries.Select(r => r.Attempt));
        Assert.All(retries, r => Assert.Equal((0x15u, TimeSpan.FromSeconds(3)), (r.Code, r.Delay)));
        Assert.Equal(4, rig.SocketsOpened);
        Assert.Equal(4, rig.Server.History.Count);

        // Each attempt starts 3 s after the error that ended the one before it.
        Assert.Equal(4, logins.Count);
        for (var i = 0; i < retries.Count; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(3), logins[i + 1] - TimeSpan.FromTicks(retries[i].Timestamp));
        }
    }

    [Fact]
    public void A_world_that_opens_between_attempts_is_entered_on_the_next()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script);
        rig.Sink.OnAnyEvent = e =>
        {
            if (e.Kind == SessionEventKind.RetryScheduled)
            {
                script.WorldOpen = true;
            }
        };

        rig.ToCharacterSelect();
        Assert.Single(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal(2, rig.SocketsOpened);
    }

    [Fact]
    public void Retail_policy_makes_one_attempt()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script, o => o with { Retry = LoginRetryPolicy.Retail });
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));
        Assert.Equal(0x15u, rig.Session.Failure!.Code);
        Assert.Empty(rig.Sink.OfKind(SessionEventKind.RetryScheduled));
        Assert.Equal(1, rig.SocketsOpened);
    }

    [Fact]
    public void The_phase_goes_back_to_connecting_while_a_retry_waits_and_the_password_is_kept_until_character_select()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script);
        rig.Sink.OnAnyEvent = e =>
        {
            if (e.Kind == SessionEventKind.RetryScheduled)
            {
                Assert.Equal(SessionPhase.Connecting, e.Phase);
                Assert.True(rig.Machine.HoldsPassword);
                Assert.Null(rig.Machine.Connection);
                script.WorldOpen = true;
            }
        };

        rig.ToCharacterSelect();
        Assert.Equal(
            new[] { SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck, SessionPhase.CharacterSelect },
            rig.Sink.Phases);
        Assert.False(rig.Machine.HoldsPassword);
    }

    [Fact]
    public async Task Wait_for_inbound_sleeps_until_a_retry_is_due()
    {
        var script = SessionRig.DefaultScript();
        script.WorldOpen = false;
        using var rig = new SessionRig(script);
        rig.Login();
        Assert.True(rig.RunUntil(() => rig.Sink.OfKind(SessionEventKind.RetryScheduled).Any(), TimeSpan.FromSeconds(2)));

        var wait = rig.Session.WaitForInboundAsync(TimeSpan.FromSeconds(10), CancellationToken.None).AsTask();
        Assert.False(wait.IsCompleted);
        rig.Harness.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
