using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The reducer's login phase (ST7): phases and connection progress, the data check, the character list in server order
/// with raw pending flags, create, delete and restore, character errors and boots paired with net's own events, the
/// world name, logoff, local notices and reset.
/// </summary>
public sealed class ReducerLoginTests
{
    [Fact]
    public void Phases_publish_their_change_and_the_connection_progress_in_order()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Reducer.OnSessionEvent(SessionEvent.ConnectRequestReceived(h.Time.GetTimestamp(), SessionPhase.Connecting, 1234.5));
        h.Phase(SessionPhase.Connecting, SessionPhase.Handshaking);
        h.Phase(SessionPhase.Handshaking, SessionPhase.DataCheck);
        h.Reducer.OnDddInterrogation(h.Context, new DddInterrogationMsg(1, 1, 1, [1]));
        h.Reducer.OnSessionEvent(SessionEvent.DataCheckStarted(h.Time.GetTimestamp(), SessionPhase.DataCheck));
        h.Reducer.OnDddEnd(h.Context);
        h.Reducer.OnSessionEvent(SessionEvent.DataCheckComplete(h.Time.GetTimestamp(), SessionPhase.DataCheck));

        var stages = h.Of<ConnectionProgress>().Select(p => p.Stage).ToList();
        Assert.Equal(
            [ConnectionStage.Connecting, ConnectionStage.Handshaking, ConnectionStage.DataCheckStarted, ConnectionStage.DataCheckComplete],
            stages);
        Assert.Equal(
            [SessionPhase.Connecting, SessionPhase.Handshaking, SessionPhase.DataCheck],
            h.Of<SessionPhaseChanged>().Select(p => p.Phase).ToList());
        Assert.Equal(DataCheckState.Complete, h.State.DataCheck);
        Assert.Equal(SessionPhase.DataCheck, h.State.Phase);
        var time = Assert.Single(h.Of<ServerTimeUpdated>());
        Assert.Equal(1234.5, time.ServerSeconds);
        Assert.Equal(1234.5, h.State.Time.ServerSecondsAt(h.Clock.Now));
    }

    [Fact]
    public void A_retry_reports_its_attempt_and_restarts_the_data_check()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Phase(SessionPhase.Connecting, SessionPhase.Handshaking);
        h.Phase(SessionPhase.Handshaking, SessionPhase.DataCheck);
        Assert.Equal(DataCheckState.Checking, h.State.DataCheck);
        h.Reducer.OnSessionEvent(SessionEvent.RetryScheduled(h.Time.GetTimestamp(), SessionPhase.Connecting, 2, TimeSpan.FromSeconds(3), 0x15, "server full"));
        h.Phase(SessionPhase.DataCheck, SessionPhase.Connecting);

        var retry = h.Of<ConnectionProgress>().Single(p => p.Stage == ConnectionStage.Retrying);
        Assert.Equal((2, TimeSpan.FromSeconds(3), 0x15u, "server full"), (retry.Attempt, retry.Delay, retry.Code, retry.Detail));
        Assert.Equal(2, h.Of<ConnectionProgress>()[^1].Attempt);
        Assert.Equal(DataCheckState.NotStarted, h.State.DataCheck);
    }

    [Fact]
    public void Character_list_keeps_server_order_and_raw_pending_flags()
    {
        var h = new ReducerHarness();
        h.Reducer.OnServerName(h.Context, new ServerNameMsg(1, -1, "Lifestone Test"));
        h.Reducer.OnCharacterList(h.Context, List(("Zed", 0u, 0x50000003u), ("Amy", 1u, 0x50000001u), ("Bo", 86400u, 0x50000002u)));

        Assert.Equal("Lifestone Test", h.State.WorldName);
        var entries = h.State.Characters.Entries;
        Assert.Equal(["Zed", "Amy", "Bo"], entries.Select(e => e.Name));
        Assert.Equal([false, true, true], entries.Select(e => e.PendingDeletion));
        Assert.Equal(86400u, entries[2].DeleteTimeout);
        Assert.Equal(11u, h.State.Characters.SlotCount);
        Assert.Equal(1, h.State.Characters.IndexOf(0x50000001));
        var changed = Assert.Single(h.Of<CharacterListChanged>());
        Assert.Equal((1, CharacterListReason.Received), (changed.Version, changed.Reason));
    }

    [Fact]
    public void Create_ok_appends_locally_and_a_duplicate_NameInUse_is_ignored()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 0u, 0x50000001u)));
        h.ClearEvents();

        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.Ok, 0x50000002, "Bea", 0));
        Assert.Equal(["Amy", "Bea"], h.State.Characters.Entries.Select(e => e.Name));
        Assert.Equal(["CharacterListChanged", "CharacterCreateResult"], h.Names());
        Assert.Equal(CharacterListReason.Created, h.Of<CharacterListChanged>()[0].Reason);
        Assert.Equal(new CharacterCreateResult(CharGenResult.Ok, 0x50000002, "Bea", false), h.Of<CharacterCreateResult>()[0]);

        // ACE can answer one create with NameInUse twice; the second, unrequested and within 2 s, is folded.
        h.ClearEvents();
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        Assert.Equal(CharGenResult.NameInUse, Assert.Single(h.Of<CharacterCreateResult>()).Result);

        // A second create sent (the action layer's notice) is answered again, and so is one after the window.
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterCreateSent, 0, null));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        Assert.Equal(3, h.Of<CharacterCreateResult>().Count);
        Assert.Equal(2, h.State.Characters.Entries.Count);
    }

    [Fact]
    public void Delete_ack_then_new_list_marks_pending_and_restore_clears_it()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 0u, 0x50000001u), ("Bo", 0u, 0x50000002u)));
        h.ClearEvents();

        h.Reducer.OnCharacterDeleteAck(h.Context);
        Assert.True(h.Reducer.DeleteAwaitingList);
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 0u, 0x50000001u), ("Bo", 1u, 0x50000002u)));
        Assert.False(h.Reducer.DeleteAwaitingList);
        Assert.True(h.State.Characters.Entries[1].PendingDeletion);
        Assert.Equal([CharacterListReason.DeleteAcknowledged, CharacterListReason.Received], h.Of<CharacterListChanged>().Select(c => c.Reason));

        h.ClearEvents();
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterRestoreSent, 0x50000002, null));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.Ok, 0x50000002, "+Bo", 0));
        Assert.False(h.State.Characters.Entries[1].PendingDeletion);
        Assert.Equal("Bo", h.State.Characters.Entries[1].Name);
        Assert.Equal(CharacterListReason.Restored, Assert.Single(h.Of<CharacterListChanged>()).Reason);
        Assert.True(Assert.Single(h.Of<CharacterCreateResult>()).Restore);
        Assert.Equal(2, h.State.Characters.Entries.Count);
    }

    [Fact]
    public void A_restore_without_its_notice_is_recognised_by_its_listed_guid()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 1u, 0x50000001u)));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.Ok, 0x50000001, "Amy", 0));
        Assert.False(h.State.Characters.Entries[0].PendingDeletion);
        Assert.Single(h.State.Characters.Entries);

        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterRestoreSent, 0x50000001, null));
        h.Reducer.OnCharGenResponse(h.Context, new CharGenResponseMsg(CharGenResult.NameInUse, 0, null, 0));
        var failed = h.Of<CharacterCreateResult>()[^1];
        Assert.Equal(new CharacterCreateResult(CharGenResult.NameInUse, 0x50000001, null, true), failed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_boot_before_or_after_the_handshake_is_reported_once_with_its_reason(bool messageFirst)
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        if (messageFirst)
        {
            h.Reducer.OnAccountBoot(h.Context, new AccountBootMsg(" Server going down"));
            h.Reducer.OnSessionEvent(SessionEvent.Booted(h.Time.GetTimestamp(), SessionPhase.Connecting, " Server going down"));
        }
        else
        {
            h.Reducer.OnSessionEvent(SessionEvent.Booted(h.Time.GetTimestamp(), SessionPhase.Connecting, " Server going down"));
            h.Reducer.OnAccountBoot(h.Context, new AccountBootMsg(" Server going down"));
        }

        var failure = new SessionFailure(SessionFailureKind.Booted, SessionPhase.Connecting, "booted:  Server going down");
        h.Reducer.OnSessionEvent(SessionEvent.Failed(h.Time.GetTimestamp(), failure));

        var boot = Assert.Single(h.Of<Booted>());
        Assert.Equal(new Booted(" Server going down", false), boot);
        Assert.Equal(SessionPhase.Failed, h.State.Phase);
        Assert.Same(failure, h.State.Failure);
        Assert.Same(failure, h.Of<SessionPhaseChanged>()[^1].Failure);

        // A second boot later (a new login) is reported again.
        h.Phase(SessionPhase.Failed, SessionPhase.Connecting);
        h.Reducer.OnAccountBoot(h.Context, new AccountBootMsg(null));
        Assert.Equal(2, h.Of<Booted>().Count);
        Assert.Null(h.State.Failure);
    }

    [Fact]
    public void Character_errors_pair_the_message_with_net_s_event()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0D));
        h.Reducer.OnSessionEvent(SessionEvent.CharacterErrorReceived(h.Time.GetTimestamp(), SessionPhase.CharacterSelect, 0x0D));
        h.Reducer.OnSessionEvent(SessionEvent.CharacterErrorReceived(h.Time.GetTimestamp(), SessionPhase.CharacterSelect, 0x01));
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x01));
        Assert.Equal([0x0Du, 0x01u], h.Of<CharacterErrorReceived>().Select(e => e.Code));
    }

    [Fact]
    public void A_0x0D_while_entering_the_world_is_held_until_the_session_says_retry_or_give_up_and_a_retry_is_counted_and_logged_P5a_Fgate()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);

        // Three refusals the session retries (the machine queues RetryScheduled; the message reaches the reducer first): nothing is published, each is counted.
        for (var i = 1; i <= 3; i++)
        {
            h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0D));
            Assert.Empty(h.Of<CharacterErrorReceived>());
            h.Reducer.OnSessionEvent(SessionEvent.RetryScheduled(h.Time.GetTimestamp(), SessionPhase.EnteringWorld, i + 1, TimeSpan.FromSeconds(5), 0x0D, "refused"));
            Assert.Empty(h.Of<CharacterErrorReceived>());
            Assert.Contains(h.Log.Entries, e => e.Message.Contains($"login_retries={i})", StringComparison.Ordinal));
        }

        // The session gives up: PhaseChanged and CharacterErrorReceived are queued after the message. One error is published, once.
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0D));
        Assert.Empty(h.Of<CharacterErrorReceived>());
        h.Phase(SessionPhase.EnteringWorld, SessionPhase.CharacterSelect);
        h.Reducer.OnSessionEvent(SessionEvent.CharacterErrorReceived(h.Time.GetTimestamp(), SessionPhase.CharacterSelect, 0x0D));
        Assert.Equal([0x0Du], h.Of<CharacterErrorReceived>().Select(e => e.Code));

        // The pairing still balances: a later error at the list publishes exactly once.
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x01));
        h.Reducer.OnSessionEvent(SessionEvent.CharacterErrorReceived(h.Time.GetTimestamp(), SessionPhase.CharacterSelect, 0x01));
        Assert.Equal([0x0Du, 0x01u], h.Of<CharacterErrorReceived>().Select(e => e.Code));
    }

    [Fact]
    public void A_retry_event_that_arrives_before_the_refusal_message_is_also_counted_and_a_failed_session_drops_the_held_refusal_P5a_Fgate()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.EnteringWorld);
        h.Reducer.OnSessionEvent(SessionEvent.RetryScheduled(h.Time.GetTimestamp(), SessionPhase.EnteringWorld, 2, TimeSpan.FromSeconds(5), 0x0D, "refused"));
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0D));
        Assert.Empty(h.Of<CharacterErrorReceived>());
        Assert.Contains(h.Log.Entries, e => e.Message.Contains("login_retries=1)", StringComparison.Ordinal));

        // A refusal the machine turns into a session failure is the failure's to report, not a character error.
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0D));
        h.Reducer.OnSessionEvent(SessionEvent.Failed(h.Time.GetTimestamp(), new SessionFailure(SessionFailureKind.CharacterError, SessionPhase.EnteringWorld, "ended", 0x0D)));
        Assert.Empty(h.Of<CharacterErrorReceived>());

        // A non-retryable code while entering the world is published at once, as before.
        h.Phase(SessionPhase.Failed, SessionPhase.EnteringWorld);
        h.Reducer.OnCharacterError(h.Context, new CharacterErrorMsg(0x0B));
        Assert.Equal([0x0Bu], h.Of<CharacterErrorReceived>().Select(e => e.Code));
    }

    [Fact]
    public void A_dat_mismatch_fails_with_DataCheckMismatch()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Phase(SessionPhase.Connecting, SessionPhase.Handshaking);
        h.Phase(SessionPhase.Handshaking, SessionPhase.DataCheck);
        h.Reducer.OnDddBegin(h.Context, new DddBeginMsg(1000, 3));
        h.Reducer.OnDddEnd(h.Context);
        var failure = new SessionFailure(SessionFailureKind.DataCheckMismatch, SessionPhase.DataCheck, "the dats differ");
        h.Reducer.OnSessionEvent(SessionEvent.Failed(h.Time.GetTimestamp(), failure));

        Assert.Equal(DataCheckState.Mismatch, h.State.DataCheck);
        Assert.Equal(SessionFailureKind.DataCheckMismatch, h.State.Failure!.Kind);
        Assert.DoesNotContain(h.Of<ConnectionProgress>(), p => p.Stage == ConnectionStage.DataCheckComplete);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("does not patch", StringComparison.Ordinal));
    }

    [Fact]
    public void A_logoff_ack_leaves_the_world_and_returns_to_character_select()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Reducer.OnCharacterList(h.Context, List(("Tester", 0u, ReducerHarness.PlayerGuid)));
        h.Phase(SessionPhase.Connecting, SessionPhase.InWorld);
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(0x80000100, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD)), false);
        h.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        h.ClearEvents();

        h.Reducer.OnCharacterLogOff(h.Context);
        h.Reducer.OnCharacterList(h.Context, List(("Tester", 0u, ReducerHarness.PlayerGuid)));
        h.Reducer.OnSessionEvent(SessionEvent.LoggedOff(h.Time.GetTimestamp(), SessionPhase.CharacterSelect));
        h.Phase(SessionPhase.LoggingOff, SessionPhase.CharacterSelect);

        Assert.Equal(SessionPhase.CharacterSelect, h.State.Phase);
        Assert.Equal(0, h.State.Objects.Count);
        Assert.Equal(0u, h.State.Self.Guid);
        Assert.Null(h.State.Self.Position);
        Assert.Equal(SelfTravel.NotInWorld, h.State.Self.Travel);
        Assert.Equal(new[] { ReducerHarness.PlayerGuid, 0x80000100u }, h.Of<ObjectDeleted>().Select(d => d.Guid).Order().ToArray());
        Assert.Equal(new SelfTravelChanged(SelfTravel.Arriving, SelfTravel.NotInWorld), Assert.Single(h.Of<SelfTravelChanged>()));
    }

    [Fact]
    public void Selecting_a_character_is_a_local_notice_that_is_recorded()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 0u, 0x50000001u)));
        using var stream = new MemoryStream();
        using (var recorder = new SessionRecorder(stream, new RecordingHeader { Runner = "test", CreatedUtc = h.Time.GetUtcNow() }, h.Clock, h.Log))
        {
            h.Reducer.Recorder = recorder;
            h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
            h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterSelected, 0x50000001, null));
            h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterSelected, 0x50000009, null));
            h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.ClientChatLine, 0, "Logging off..."));
            recorder.Complete();
        }

        Assert.Equal(0x50000001u, h.State.Characters.SelectedGuid);
        Assert.Equal(0x50000001u, Assert.Single(h.Of<CharacterSelected>()).Guid);
        Assert.Equal(new ChatLine(0, ChatKind.Notice, 0, null, 0, "Logging off..."), h.State.Chat[0]);

        stream.Position = 0;
        using var reader = SessionRecordingReader.Open(stream, leaveOpen: true);
        var codes = new List<ushort>();
        var phases = new List<SessionPhase>();
        while (reader.TryRead(out var frame))
        {
            if (frame.Kind == RecordingFrameKind.Notice)
            {
                codes.Add(frame.Notice.Code);
            }
            else if (frame.Kind == RecordingFrameKind.SessionEvent)
            {
                phases.Add(frame.SessionEvent.Phase);
            }
        }

        Assert.Equal([SessionNotices.CharacterSelected, SessionNotices.CharacterSelected, SessionNotices.ClientChatLine], codes);
        Assert.Equal([SessionPhase.Connecting], phases);

        // A new list without the selected character clears the selection.
        h.Reducer.OnCharacterList(h.Context, List(("Bo", 0u, 0x50000002u)));
        Assert.Equal(0u, h.State.Characters.SelectedGuid);
        Assert.Equal(0u, h.Of<CharacterSelected>()[^1].Guid);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)8)]
    [InlineData((ushort)15)]
    [InlineData((ushort)16)]
    public void An_unknown_local_notice_fails_loudly(ushort code)
    {
        var h = new ReducerHarness();
        Assert.Throws<InvalidDataException>(() => h.Reducer.ApplyLocal(new RecordingNotice(code, 0, null)));
    }

    [Fact]
    public void Reset_returns_to_idle_and_keeps_subscribers()
    {
        var h = new ReducerHarness();
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Reducer.OnCharacterList(h.Context, List(("Amy", 0u, 0x50000001u)));
        h.Reducer.OnSessionEvent(SessionEvent.Failed(h.Time.GetTimestamp(), new SessionFailure(SessionFailureKind.Silence, SessionPhase.CharacterSelect, "silent")));
        h.ClearEvents();

        h.Reducer.Reset();
        Assert.Equal(SessionPhase.Idle, h.State.Phase);
        Assert.Null(h.State.Failure);
        Assert.Empty(h.State.Characters.Entries);
        Assert.Equal(new SessionPhaseChanged(SessionPhase.Failed, SessionPhase.Idle, null), Assert.Single(h.Of<SessionPhaseChanged>()));

        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        Assert.Equal(SessionPhase.Connecting, h.Of<SessionPhaseChanged>()[^1].Phase);
    }

    [Fact]
    public void Every_change_raises_the_state_version()
    {
        var h = new ReducerHarness();
        var before = h.State.Version;
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        var after = h.State.Version;
        Assert.True(after > before);
        h.Phase(SessionPhase.Connecting, SessionPhase.Connecting);
        Assert.Equal(after, h.State.Version);
    }

    private static CharacterListMsg List(params (string Name, uint DeleteTimeout, uint Guid)[] characters) => new(
        0,
        [.. characters.Select(c => new CharacterListEntry(c.Guid, c.Name, c.DeleteTimeout))],
        0,
        11,
        "account-string",
        0,
        1);
}
