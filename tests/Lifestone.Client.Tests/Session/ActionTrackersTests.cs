using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The action trackers (step ST10): what each action sends, when it is refused, and what confirms it, against the
/// reducer fed with ACE's answers (docs/session.md §7).
/// </summary>
public sealed class ActionTrackersTests
{
    private static readonly uint[] FiftyFiveSkills = new uint[CharacterCreateIntent.RequiredSkillCount];

    [Fact]
    public void Login_completes_after_list_and_ddd()
    {
        var h = new ActionHarness();
        var login = h.Api.Login();
        h.Tick();
        Assert.Equal([FakeSendKind.Login], h.Net.Kinds());
        Assert.Equal(["lifestone-test"], h.Net.LoginAccounts);

        // Retries show as progress; the handle waits for the character list and the end of the data check.
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.R.Reducer.OnSessionEvent(SessionEvent.RetryScheduled(h.R.Time.GetTimestamp(), SessionPhase.Connecting, 2, TimeSpan.FromSeconds(1), 0, "no ConnectRequest"));
        h.Phase(SessionPhase.Connecting, SessionPhase.Handshaking);
        h.Phase(SessionPhase.Handshaking, SessionPhase.DataCheck);
        h.Tick(1);
        Assert.False(login.IsCompleted);
        h.R.Reducer.OnDddEnd(h.R.Context);
        h.CharacterList(("Amy", 0u, ActionHarness.Amy));
        h.Phase(SessionPhase.DataCheck, SessionPhase.CharacterSelect);
        h.Tick(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, login.Outcome!.Value.Kind);

        // A second login is refused: the session is not idle.
        var again = h.Api.Login();
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, again.Outcome!.Value.Kind);
        Assert.Contains("not idle", again.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.False(h.Log.AnyLineContains(ActionHarness.Password));
    }

    [Theory]
    [InlineData(SessionFailureKind.DataCheckMismatch, ActionOutcomeKind.ServerError)]
    [InlineData(SessionFailureKind.Booted, ActionOutcomeKind.ServerError)]
    [InlineData(SessionFailureKind.CharacterError, ActionOutcomeKind.ServerError)]
    [InlineData(SessionFailureKind.ConnectTimeout, ActionOutcomeKind.TimedOut)]
    [InlineData(SessionFailureKind.Silence, ActionOutcomeKind.TimedOut)]
    public void A_failed_login_ends_with_the_failure(SessionFailureKind kind, ActionOutcomeKind outcome)
    {
        var h = new ActionHarness();
        var login = h.Api.Login();
        h.Tick();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Phase(SessionPhase.Connecting, SessionPhase.Failed);
        h.R.Reducer.OnSessionEvent(SessionEvent.Failed(h.R.Time.GetTimestamp(), new SessionFailure(kind, SessionPhase.Connecting, "it failed", 0x0B)));
        h.Tick();
        Assert.Equal(outcome, login.Outcome!.Value.Kind);
        Assert.Equal(0x0Bu, login.Outcome.Value.Code);
        Assert.Contains(kind.ToString(), login.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_refuses_non_ascii_names_and_wrong_skill_count()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var unicode = h.Api.CreateCharacter(Intent("Zoë"), ActionSource.Ui);
        var short54 = h.Api.CreateCharacter(Intent("Dee") with { SkillAdvancementClasses = new uint[54] }, ActionSource.Ui);
        var none = h.Api.CreateCharacter(null!, ActionSource.Ui);
        Assert.All([unicode, short54, none], a => Assert.Equal(ActionOutcomeKind.Refused, a.Outcome!.Value.Kind));
        Assert.Contains("printable ASCII", unicode.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("55", short54.Outcome!.Value.Detail, StringComparison.Ordinal);
        h.Tick();
        Assert.Empty(h.Net.Sends);

        // A valid intent is sent field for field, in the next free slot; F643 Ok confirms it and the list grows.
        var create = h.Api.CreateCharacter(Intent("Dee"), ActionSource.Ui);
        h.Tick();
        var request = Assert.Single(h.Net.Of(FakeSendKind.CreateCharacter)).Create!.Value;
        Assert.Equal(("Dee", 3u, 55, 6u), (request.Name, request.Slot, request.SkillAdvancementClasses.Length, request.Heritage));
        h.R.Reducer.OnCharGenResponse(h.R.Context, new CharGenResponseMsg(CharGenResult.Ok, 0x50000009, "Dee", 0));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, create.Outcome!.Value.Kind);
        Assert.Equal(4, h.State.Characters.Entries.Count);

        // NameInUse is a server error with the CharGenResult as its code.
        var taken = h.Api.CreateCharacter(Intent("Amy"), ActionSource.Ui);
        h.Tick();
        h.R.Reducer.OnCharGenResponse(h.R.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, (uint)CharGenResult.NameInUse, "the server refused the character: NameInUse"), taken.Outcome);
    }

    [Fact]
    public void Delete_refuses_unknown_or_pending_and_sends_the_server_index()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var pending = h.Api.DeleteCharacter(ActionHarness.Bo, ActionSource.Ui);
        var unknown = h.Api.DeleteCharacter(0x5000FFFF, ActionSource.Ui);
        var delete = h.Api.DeleteCharacter(ActionHarness.Cy, ActionSource.Ui);
        h.Tick();

        Assert.Equal("the character is already pending deletion", pending.Outcome!.Value.Detail);
        Assert.Equal("the character is not in the list", unknown.Outcome!.Value.Detail);
        Assert.Equal(2, Assert.Single(h.Net.Of(FakeSendKind.DeleteCharacter)).Index);

        // The acknowledgement alone does not complete it; the list after it does.
        h.R.Reducer.OnCharacterDeleteAck(h.R.Context);
        h.Tick();
        Assert.False(delete.IsCompleted);
        h.CharacterList(("Amy", 0u, ActionHarness.Amy), ("Bo", 1u, ActionHarness.Bo), ("Cy", 1u, ActionHarness.Cy));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, delete.Outcome!.Value.Kind);
    }

    [Fact]
    public void Restore_needs_a_pending_character_and_completes_on_its_F643()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var notPending = h.Api.RestoreCharacter(ActionHarness.Amy, ActionSource.Ui);
        var restore = h.Api.RestoreCharacter(ActionHarness.Bo, ActionSource.Ui);
        h.Tick();
        Assert.Equal("the character is not pending deletion", notPending.Outcome!.Value.Detail);
        Assert.Equal(ActionHarness.Bo, Assert.Single(h.Net.Of(FakeSendKind.RestoreCharacter)).Guid);

        h.R.Reducer.OnCharGenResponse(h.R.Context, new CharGenResponseMsg(CharGenResult.Ok, ActionHarness.Bo, "Bo", 0));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, restore.Outcome!.Value.Kind);
        Assert.False(h.State.Characters.Entries[1].PendingDeletion);
    }

    [Fact]
    public void Enter_refuses_pending_slot()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var pending = h.Api.EnterWorld(ActionHarness.Bo, ActionSource.Ui);
        var enter = h.Api.EnterWorld(ActionHarness.Amy, ActionSource.Ui);
        h.Tick();
        Assert.Contains("pending deletion", pending.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionHarness.Amy, Assert.Single(h.Net.Of(FakeSendKind.EnterWorld)).Guid);

        // F746 alone does not complete it; the player's CreateObject does.
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        h.Phase(SessionPhase.EnteringWorld, SessionPhase.InWorld);
        h.R.Reducer.OnPlayerCreate(h.R.Context, new PlayerCreateMsg(ActionHarness.Amy));
        h.Tick();
        Assert.False(enter.IsCompleted);
        var create = ReducerHarness.Create(ActionHarness.Amy, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsStateHidden);
        h.R.Reducer.OnCreateObject(h.R.Context, create, false);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, enter.Outcome!.Value.Kind);
    }

    [Fact]
    public void LoginComplete_only_once_per_arrival_and_never_before_arrival()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var early = h.Api.LoginComplete();
        h.Tick();
        Assert.Contains("not in the world", early.Outcome!.Value.Detail, StringComparison.Ordinal);

        h.InWorld();
        h.Describe(); // LoginComplete waits for the description (M3 1.2 s5)
        var first = h.Api.LoginComplete();
        var second = h.Api.LoginComplete();
        h.Tick();
        Assert.Single(h.Net.Of(FakeSendKind.LoginComplete));
        Assert.Equal(SelfTravel.Materialising, h.State.Self.Travel);
        Assert.Equal(ActionOutcomeKind.Refused, second.Outcome!.Value.Kind);
        Assert.Contains("already sent for this arrival", second.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("second LoginComplete", StringComparison.Ordinal));
        Assert.False(first.IsCompleted);

        // ACE clears Hidden: present.
        h.Unhide();
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, first.Outcome!.Value.Kind);

        // A teleport is a new arrival: LoginComplete is allowed once more, after the destination's position.
        var announced = h.Api.LoginComplete();
        h.Tick();
        Assert.Contains("not arriving", announced.Outcome!.Value.Detail, StringComparison.Ordinal);
        h.R.Reducer.OnPlayerTeleport(h.R.Context, new PlayerTeleportMsg(2));
        h.R.Reducer.OnUpdatePosition(h.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 2, 2, 1, cell: 0xA9B40019));
        Assert.Equal(SelfTravel.TeleportArrived, h.State.Self.Travel);
        var arrived = h.Api.LoginComplete();
        h.Tick();
        Assert.Equal(2, h.Net.Of(FakeSendKind.LoginComplete).Count);
        Assert.Equal(ActionStatus.Sent, arrived.Status);
    }

    [Fact]
    public void Use_completes_on_UseDone_and_reports_teleport()
    {
        var h = new ActionHarness();
        h.Present();
        var portal = h.Api.Use(0x7A9B4001, ActionSource.Automation);
        var door = h.Api.Use(0x7A9B4002, ActionSource.Ui);
        h.Tick();
        Assert.Equal([0x7A9B4001u, 0x7A9B4002u], h.Net.Of(FakeSendKind.Use).Select(s => s.Guid));

        // UseDone answers the oldest use; a teleport the use started is in the detail.
        h.R.Reducer.OnPlayerTeleport(h.R.Context, new PlayerTeleportMsg(2));
        h.R.Reducer.OnUseDone(h.R.Context, h.Header, new UseDoneEvent(0));
        h.R.Reducer.OnUseDone(h.R.Context, h.Header, new UseDoneEvent(0x1D));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "the use started a teleport (F751)"), portal.Outcome);
        Assert.Equal(ActionOutcomeKind.ServerError, door.Outcome!.Value.Kind);
        Assert.Equal(0x1Du, door.Outcome.Value.Code);

        var nothing = h.Api.Use(0, ActionSource.Ui);
        Assert.Equal("no object to use", nothing.Outcome!.Value.Detail);
    }

    [Fact]
    public void QueryHealth_completes_on_the_object_s_health()
    {
        var h = new ActionHarness();
        h.Present();
        var query = h.Api.QueryHealth(0x80000100, ActionSource.Ui);
        var deselect = h.Api.QueryHealth(0, ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, deselect.Outcome!.Value.Kind);
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(0x80000999, 0.5f));
        h.Tick();
        Assert.False(query.IsCompleted);
        h.R.Reducer.OnUpdateHealth(h.R.Context, h.Header, new UpdateHealthEvent(0x80000100, 0.25f));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, query.Outcome!.Value.Kind);
    }

    [Fact]
    public void Talk_completes_on_self_echo()
    {
        var h = new ActionHarness();
        h.Present();
        var talk = h.Api.Talk("Hello Dereth", ActionSource.Ui);
        var command = h.Api.Talk("@acehelp", ActionSource.Ui);
        h.Tick();
        Assert.Equal(["Hello Dereth", "@acehelp"], h.Net.Said());
        Assert.Equal(ActionOutcomeKind.Sent, command.Outcome!.Value.Kind);

        h.R.Reducer.OnSpeech(h.R.Context, new SpeechMsg("Hello Dereth", "Guard", 0x80000001, 2, null));
        h.Tick();
        Assert.False(talk.IsCompleted);
        h.R.Reducer.OnSpeech(h.R.Context, new SpeechMsg("Hello Dereth", "Tester", ActionHarness.Amy, 2, null));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, talk.Outcome!.Value.Kind);

        // A gagged player gets ACE's gag line instead of the echo.
        var gagged = h.Api.Talk("Hello?", ActionSource.Ui);
        h.Tick();
        const string gag = "You are unable to talk locally, globally, or send tells because you have been gagged.";
        h.R.Reducer.OnTransientString(h.R.Context, h.Header, new TransientStringEvent(gag));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, gagged.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Talk("", ActionSource.Ui).Outcome!.Value.Kind);
    }

    [Fact]
    public void Tell_completes_on_the_confirmation_or_the_server_s_error()
    {
        var h = new ActionHarness();
        h.Present();
        var tell = h.Api.Tell("Bob", "psst", ActionSource.Ui);
        var away = h.Api.Tell("Nobody", "psst", ActionSource.Ui);
        h.Tick();
        Assert.Equal([("Bob", "psst"), ("Nobody", "psst")], h.Net.Of(FakeSendKind.Tell).Select(s => (s.Target, s.Text)));

        h.ServerMessage("You tell Bob, \"psst\"", 0x04);
        h.R.Reducer.OnWeenieError(h.R.Context, h.Header, new WeenieErrorEvent(0x052B, null));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, tell.Outcome!.Value.Kind);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0x052B, "the character is not available (WeenieError 0x052B)"), away.Outcome);
        Assert.Equal("Tell chars=4", tell.Summary);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("psst", StringComparison.Ordinal) && l.Contains("action #", StringComparison.Ordinal));
    }

    [Fact]
    public void LogOff_adds_chat_line_and_completes_on_character_list()
    {
        var h = new ActionHarness();
        h.Present();
        var requested = new List<LogOffRequested>();
        h.R.Bus.Subscribe((in LogOffRequested e) => requested.Add(e));
        var logoff = h.Api.LogOff(ActionSource.Ui);
        h.Tick(0.5);

        Assert.Equal([FakeSendKind.LogOff], h.Net.Kinds().Where(k => k == FakeSendKind.LogOff));
        Assert.Equal("Logging off...", h.State.Chat[h.State.Chat.Count - 1].Text);
        Assert.Equal(ChatKind.Notice, h.State.Chat[h.State.Chat.Count - 1].Kind);
        Assert.Equal(new LogOffRequested(h.R.Clock.Now, false), Assert.Single(requested));

        // The chat line comes before the send's LogOffRequested, as retail's RequestLogOff orders them.
        var names = h.R.Names();
        Assert.True(names.LastIndexOf(nameof(ChatReceived)) < names.IndexOf(nameof(LogOffRequested)));

        // The server's F653, its F658 and the session back at character select complete it.
        h.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        h.R.Reducer.OnCharacterLogOff(h.R.Context);
        h.Tick(5);
        Assert.False(logoff.IsCompleted);
        h.CharacterList(("Amy", 0u, ActionHarness.Amy));
        h.Phase(SessionPhase.LoggingOff, SessionPhase.CharacterSelect);
        h.Tick(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, logoff.Outcome!.Value.Kind);
        Assert.Equal(SelfTravel.NotInWorld, h.State.Self.Travel);

        // Not in the world: refused, and nothing is sent.
        var again = h.Api.LogOff(ActionSource.Ui);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, again.Outcome!.Value.Kind);
        Assert.Single(h.Net.Of(FakeSendKind.LogOff));
    }

    [Fact]
    public void Quit_in_world_sends_logoff_then_disconnect()
    {
        var h = new ActionHarness();
        h.Present();
        var quit = h.Api.Quit(ActionSource.Host);
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.Sent, 0, "F653 then Disconnect"), quit.Outcome);
        Assert.Single(h.Net.Of(FakeSendKind.Quit));
        Assert.Empty(h.Net.Of(FakeSendKind.LogOff));
        Assert.NotNull(quit.SentAt);

        // At the character list, the Disconnect only; before the login, nothing to end.
        var select = new ActionHarness();
        select.AtCharacterSelect();
        var atList = select.Api.Quit(ActionSource.Host);
        select.Tick();
        Assert.Equal("Disconnect", atList.Outcome!.Value.Detail);
        var idle = new ActionHarness();
        var nothing = idle.Api.Quit(ActionSource.Host);
        idle.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, nothing.Outcome!.Value.Kind);
    }

    [Fact]
    public void Local_notices_record_what_the_actions_changed()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        h.Api.CreateCharacter(Intent("Dee"), ActionSource.Ui);
        h.Tick();

        // The create notice tells the reducer the next F643 answers a create: a NameInUse is not folded away.
        h.R.Reducer.OnCharGenResponse(h.R.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        Assert.Single(h.R.Of<CharacterCreateResult>());
        h.Tick();
        h.Api.CreateCharacter(Intent("Dee"), ActionSource.Ui);
        h.Tick();
        h.R.Reducer.OnCharGenResponse(h.R.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        Assert.Equal(2, h.R.Of<CharacterCreateResult>().Count);
    }

    private const PhysicsState PhysicsStateHidden = PhysicsState.Hidden | PhysicsState.Gravity;

    private static CharacterCreateIntent Intent(string name) => new(
        6,
        1,
        default,
        0,
        10,
        10,
        10,
        10,
        10,
        10,
        1,
        FiftyFiveSkills,
        name,
        0);
}
