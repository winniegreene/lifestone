using Lifestone.Data.Dat;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The net track's live phase A (m2-plan N11; DoD 2, 8 and 12) against the bundled ACE on 127.0.0.1, on the test account only (<c>accounts.test</c>),
/// through <see cref="AcSession"/> itself: login, a character created when the account has none (Aluvian, male, 55 skill classes), the world entry into
/// the Training Academy's landblock, LoginComplete making the server clear Hidden, the log off back to the character list, Quit, an immediate second
/// login (ACE answers it with error 0x01 once, because it keeps a quit session for 2 s; the login policy's first retry gets in) and a third login after
/// the 2 s, which meets no 0x01 at all. The client's raw tap is captured into <c>cache/net-fixtures/&lt;CacheKey&gt;/</c> (never committed; a body that holds
/// the password is refused). Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.
/// </summary>
/// <remarks>
/// The first test asks the mod for its status and aborts the run (<see cref="LoopbackRun.Abort"/>) when the account is not a plain Player or ACE's
/// first-account auto-promotion is on: nothing after it logs in to a server in that state. The tests run in step order (<see cref="LiveStepAttribute"/>).
/// Each creates its own sessions inside its body (no fixtures: the loopback gate runs first). Where a 0x01 must not be retried the login policy is
/// <see cref="LoginRetryPolicy.Retail"/> (one attempt), so it shows as a failure.
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed class LivePhaseATests(ITestOutputHelper output)
{
    [LoopbackFact]
    [LiveStep(1)]
    public void Status_reports_access_Player_and_auto_promotion_off_or_the_run_aborts()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: true);
        try
        {
            EnterTheWorld(client);
            Unhide(client);

            var status = AskStatus(client);
            output.WriteLine($"mod status: access={status.Access} auto_promotion={status.AutoPromotion} wire={status.Wire} mod={status.Mod}");
            if (!string.Equals(status.Access, "Player", StringComparison.Ordinal) || status.AutoPromotion)
            {
                LoopbackRun.Abort($"the test account's access is {status.Access} and ACE's first-account auto-promotion is {(status.AutoPromotion ? "ON" : "off")} (owner decision D11: a Lifestone account is a plain Player and the promotion is off)");
            }

            Assert.Equal("Player", status.Access);
            Assert.False(status.AutoPromotion, "ACE's first-account Admin auto-promotion is on; the Lifestone server mod must have turned it off");
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    [LoopbackFact]
    [LiveStep(2)]
    public void Login_create_enter_unhide_logoff_quit_then_relogin_at_once_and_after_ACEs_linger()
    {
        using var dats = DatSet.Open();
        string? key;

        // First pass: the whole life of a session. One login attempt only, so a 0x01 would fail it instead of being retried.
        using (var first = LiveNetClient.Create(dats, retail: true))
        {
            Login(first, waitOutAceLinger: true);
            Assert.Contains(first.Sink.Events, e => e.Kind == SessionEventKind.DataCheckComplete);
            var created = first.CreateCharacterIfMissing();
            output.WriteLine($"character created on this run: {created}");
            Assert.NotNull(first.FindCharacter());

            Enter(first);
            var self = first.Sink.SelfCreate!;
            Assert.True((self.Physics.State & PhysicsState.Hidden) != 0, "the server creates the player hidden until LoginComplete");
            var cell = self.Physics.Position?.CellId ?? 0;
            output.WriteLine($"arrival cell 0x{cell:X8}");
            Assert.Equal(0x8602u, cell >> 16);

            first.Session.SendLoginComplete();
            Assert.True(first.PumpUntil(() => first.Sink.SelfUnhidden, ReplyWait), "LoginComplete did not make the server clear Hidden. " + first.Describe());
            output.WriteLine($"unhidden; objects sent besides the player: {first.Sink.OtherObjects}; malformed: {first.Sink.Malformed.Count}");

            LogOffAndQuit(first);
            Assert.Empty(first.Sink.CharacterErrors);
            Assert.DoesNotContain(first.Sink.Events, e => e.Kind == SessionEventKind.RetryScheduled);
            Assert.Empty(first.Sink.Malformed);
            key = Capture(first, "login-enter-logoff");
        }

        // Second pass, at once, with the product's login policy (up to 4 attempts, 3 s apart). ACE keeps a session that was quit or booted for 2 s
        // (SessionTerminationDetails.TerminationEndTicks) and, while it does, answers a login of the same account with 0x01 (Logon) and terminates both
        // sessions (AuthenticationHandler: account_login_boots_in_use); so the very first retry is what lets an immediate re-login in. The 0x01 may appear
        // once, never again, and the re-login must succeed and play.
        using (var second = LiveNetClient.Create(dats, retail: false))
        {
            Login(second, waitOutAceLinger: false);
            var errors = second.Sink.CharacterErrors;
            output.WriteLine($"immediate re-login: character errors [{string.Join(", ", errors.Select(c => "0x" + c.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)))}], retries {second.Sink.Events.Count(e => e.Kind == SessionEventKind.RetryScheduled)}");
            Assert.True(errors.Count <= 1, "the immediate re-login met more than one character error: ACE's 2 s linger allows one 0x01 at most");
            Assert.All(errors, c => Assert.Equal((uint)CharacterErrorCode.Logon, c));
            Assert.True(second.Sink.Events.Count(e => e.Kind == SessionEventKind.RetryScheduled) == errors.Count, "every 0x01 is answered by exactly one scheduled retry");

            Enter(second);
            second.Session.SendLoginComplete();
            Assert.True(second.PumpUntil(() => second.Sink.SelfUnhidden, ReplyWait), "after the re-login, LoginComplete did not make the server clear Hidden. " + second.Describe());

            LogOffAndQuit(second);
            Capture(second, "relogin-immediate");
        }

        // Third pass, once ACE has dropped the quit session (2 s after the Disconnect): no retries allowed, and no 0x01 at all.
        using (var third = LiveNetClient.Create(dats, retail: true))
        {
            Login(third, waitOutAceLinger: true);
            Assert.Empty(third.Sink.CharacterErrors);
            Assert.DoesNotContain(third.Sink.Events, e => e.Kind == SessionEventKind.RetryScheduled);
            third.Quit();
            third.PumpUntil(() => third.Session.Phase == SessionPhase.Closed, TimeSpan.FromSeconds(10));
            Assert.Equal(SessionPhase.Closed, third.Session.Phase);
            Capture(third, "relogin-after-linger");
        }

        output.WriteLine($"golden captures are under cache/net-fixtures/{key}");
    }

    /// <summary>
    /// dod-1-2-12-13-4: the 20 s silence watchdog (<see cref="SessionTimeouts.Silence"/>, owner decision D18; retail waits 140 s) must not
    /// trip on a character list the player is just reading. ACE's own TimeSync (every 20 s) and its ack-only packets are what keep the link
    /// from being silent; this proves it on the real backend with the product's timeouts: 30 s (the 20 s watchdog plus 10 s; M3 4.2 p5b C10, was 45 s) in CharacterSelect, nothing done, and the
    /// phase is still CharacterSelect with no failure. If it fails, the lead hands the fix (count the client's own acks as activity, or raise the
    /// watchdog while in CharacterSelect) to the owner, because D18 is the owner's.
    /// </summary>
    [LoopbackFact]
    [LiveStep(3)]
    public void Idling_in_the_character_list_past_the_silence_watchdog_does_not_trip_it()
    {
        var idle = SessionTimeouts.Default.Silence + TimeSpan.FromSeconds(10); // 30 s: ACE sends a TimeSync every 20 s, so any 30 s window holds one (C10)
        Assert.True(SessionTimeouts.Default.Silence < idle, "the default watchdog is not shorter than the idle time: the test would prove nothing");

        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: true);
        Login(client, waitOutAceLinger: true);
        Assert.Equal(SessionPhase.CharacterSelect, client.Session.Phase);

        // Pumps (and so runs the watchdog) for the whole time; any phase change ends the wait early.
        var timeSyncsBefore = client.Sink.Events.Count(e => e.Kind == SessionEventKind.ServerTime);
        var changed = client.PumpUntil(() => client.Session.Phase != SessionPhase.CharacterSelect, idle);
        output.WriteLine($"after {idle.TotalSeconds:0} s idle: phase {client.Session.Phase}, changed early: {changed}");
        var timeSyncs = client.Sink.Events.Count(e => e.Kind == SessionEventKind.ServerTime) - timeSyncsBefore;
        output.WriteLine($"TimeSync (ServerTime events) during the idle: {timeSyncs} (report-only in its first gate: the lead reads it before it is asserted >= 1)");
        Assert.False(changed, "the session left CharacterSelect while idle: " + client.Describe());
        Assert.Equal(SessionPhase.CharacterSelect, client.Session.Phase);
        Assert.Null(client.Session.Failure);

        client.Quit();
        client.PumpUntil(() => client.Session.Phase == SessionPhase.Closed, TimeSpan.FromSeconds(10));
    }

    private static string? Capture(LiveNetClient client, string name)
    {
        var directory = NetFixtureStore.Directory() ?? throw new InvalidOperationException("no cache key for the net fixtures");
        NetFixtureStore.Save(Path.Combine(directory, name + NetFixtureStore.Extension), client.Capture.Messages, client.PasswordNeedles);
        return NetFixtureStore.CacheKey();
    }
}
