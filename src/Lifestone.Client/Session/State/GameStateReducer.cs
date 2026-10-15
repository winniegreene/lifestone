using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The session's reducer (A0's GameState reducer; docs/session.md §5, §6 and §15): the one writer of
/// <see cref="GameState"/>. It implements net's <see cref="IAcSessionSink"/>, so the live session, a replay and the
/// stand-in data all feed it the same decoded messages and session events. For each one it changes the state first and
/// publishes its events after, so a handler always sees the whole message applied.
/// </summary>
/// <remarks>
/// <para><b>Login phase</b> (ST7, <c>GameStateReducer.Login.cs</c>): the phases, the data check, the character list in
/// server order, create, delete and restore results, character errors and boots.</para>
/// <para><b>World phase</b> (ST8, <c>GameStateReducer.World.cs</c>): the one owner of retail's SmartBox semantics
/// (m2-plan §1.4): object creation and re-creation, deletion, the per-object instance gate, the per-field sequences, the
/// position verdicts, parking and replay, the player's travel state. <c>GameStateReducer.Chat.cs</c> adds the chat, the
/// tool replies and the player's qualities.</para>
/// <para><b>Local changes</b> (selection, LoginComplete sent, the client's own chat lines) come in as
/// <see cref="RecordingNotice"/>s through <see cref="ApplyLocal"/>, so the recorder keeps them and a replay reapplies
/// them (<see cref="SessionNotices"/>).</para>
/// <para>Game thread only (the bus checks the session's binding). The per-message path for a known object allocates
/// nothing.</para>
/// </remarks>
internal sealed partial class GameStateReducer : IAcSessionSink
{
    private readonly GameState _state;
    private readonly EventBus _bus;
    private readonly SessionClock _clock;
    private readonly ILogSink _log;
    private readonly PendingObjectMessages _parked = new();
    private readonly List<uint> _guidScratch = new(256);
    private readonly HashSet<uint> _unknownReported = [];
    private readonly List<string> _silentToolIds = [];
    private int _attempt = 1;
    private int _bootBalance;
    private int _characterErrorBalance;

    /// <summary>A reducer writing <paramref name="state"/> and publishing on <paramref name="bus"/>.</summary>
    /// <param name="state">The session's state (empty).</param>
    /// <param name="bus">The session's bus.</param>
    /// <param name="clock">The session clock: chat times, parking times, the server-time conversion.</param>
    /// <param name="log">Where the reducer logs (category <c>session</c>).</param>
    internal GameStateReducer(GameState state, EventBus bus, SessionClock clock, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _state = state;
        _bus = bus;
        _clock = clock;
        _log = log;
    }

    /// <summary>The state this reducer writes.</summary>
    internal GameState State => _state;

    /// <summary>The session clock.</summary>
    internal SessionClock Clock => _clock;

    /// <summary>The reducer's counters (the inspector's and the tests' view of what was parked, dropped and replayed).</summary>
    internal ReducerCounters Counters { get; } = new();

    /// <summary>The parked messages (tests).</summary>
    internal PendingObjectMessages Parked => _parked;

    /// <summary>When set, the session events and local notices the reducer applies are recorded (ST13 sets it).</summary>
    internal SessionRecorder? Recorder { get; set; }

    /// <summary>
    /// Receives every accepted tool reply, after <see cref="ToolReplyReceived"/> was published: the player tools (ST12)
    /// match it to their request by id, read the verb's data and complete the request's handle.
    /// </summary>
    internal Action<ToolReplyLine>? ToolReplyRouted { get; set; }

    /// <summary>The timed stand-in steps of an offline session; null for a live session (stand-in data cannot be timed there).</summary>
    internal StandInSchedule? StandIn { get; set; }

    /// <summary>
    /// The offline stand-in's opt-in responder for delete, restore and create (<see cref="StandInSessions.AnswerLocally"/>); null for a live
    /// session and for an offline one that did not ask. The Action API consults it only when it has no network session.
    /// </summary>
    internal StandInResponder? LocalResponder { get; set; }

    /// <summary>
    /// The network session's own events (net's <see cref="SessionEvent"/>): the phase, the connection progress, the data
    /// check, retries, the server's time, boots, character errors, logoff and failure (docs/session.md §11).
    /// </summary>
    public void OnSessionEvent(in SessionEvent sessionEvent)
    {
        Recorder?.SessionEvent(_clock.ToSeconds(sessionEvent.Timestamp), in sessionEvent);
        switch (sessionEvent.Kind)
        {
            case SessionEventKind.PhaseChanged:
                ChangePhase(sessionEvent.Phase);
                break;
            case SessionEventKind.ConnectRequestReceived:
                UpdateServerTime(sessionEvent.ServerTime, _clock.ToSeconds(sessionEvent.Timestamp));
                break;
            case SessionEventKind.DataCheckStarted:
                StartDataCheck();
                break;
            case SessionEventKind.DataCheckComplete:
                CompleteDataCheck();
                break;
            case SessionEventKind.RetryScheduled:
                _attempt = Math.Max(1, sessionEvent.Attempt);
                Publish(new ConnectionProgress(ConnectionStage.Retrying, _attempt, sessionEvent.Delay, sessionEvent.Code, sessionEvent.Text));
                break;
            case SessionEventKind.Booted:
                ReportBoot(sessionEvent.Text, fromMessage: false);
                break;
            case SessionEventKind.CharacterErrorReceived:
                ReportCharacterError(sessionEvent.Code, fromMessage: false);
                break;
            case SessionEventKind.ServerTime:
                UpdateServerTime(sessionEvent.ServerTime, _clock.ToSeconds(sessionEvent.Timestamp));
                break;
            case SessionEventKind.LoggedOff:
                LeaveWorld();
                break;
            case SessionEventKind.Failed:
                Fail(sessionEvent.Failure);
                break;
            case SessionEventKind.Warning:
                _log.Warn(
                    SessionLog.Category,
                    "network session warning: " + sessionEvent.Warning + (sessionEvent.Text is null ? string.Empty : ": " + sessionEvent.Text),
                    new LogField("warning", sessionEvent.Warning));
                break;
            default:
                _log.Error(SessionLog.Category, "TODO(state): unknown network session event kind " + sessionEvent.Kind + " ignored");
                break;
        }
    }

    /// <summary>
    /// Applies a local change (a <see cref="RecordingNotice"/> with one of the <see cref="SessionNotices"/> codes): records
    /// it when a recorder is attached, then changes the state and publishes, as for a server message.
    /// </summary>
    /// <param name="notice">The change.</param>
    /// <exception cref="InvalidDataException">The code is not one the reducer knows (a replay of a newer recording, or a bug).</exception>
    internal void ApplyLocal(in RecordingNotice notice)
    {
        if (notice.Code is 0 or > SessionNotices.LastReducerCode)
        {
            throw new InvalidDataException(
                $"local notice code {notice.Code} does not belong to the reducer (codes 1-{SessionNotices.LastReducerCode}; SessionNotices)");
        }

        Recorder?.Notice(_clock.Now, in notice);
        switch (notice.Code)
        {
            case SessionNotices.CharacterSelected:
                Select(notice.Value);
                break;
            case SessionNotices.LoginCompleteSent:
                LoginCompleteSent();
                break;
            case SessionNotices.ClientChatLine:
                AddChat(new ChatLine(_clock.Now, ChatKind.Notice, 0, null, 0, notice.Text ?? string.Empty));
                break;
            case SessionNotices.CharacterCreateSent:
                _createsAwaiting++;
                break;
            case SessionNotices.CharacterRestoreSent:
                _restoreAwaiting = notice.Value;
                break;
            case SessionNotices.ToolRequestSilent:
                if (!string.IsNullOrEmpty(notice.Text))
                {
                    _silentToolIds.Add(notice.Text);
                }

                break;
            case SessionNotices.SessionReset:
                Reset();
                break;
            case SessionNotices.SessionOptionSet:
                ApplySessionOption(notice);
                break;
            default:
                throw new InvalidDataException($"local notice code {notice.Code} is reserved for the reducer but not assigned (SessionNotices)");
        }
    }

    /// <summary>
    /// Back to an empty state before login (<c>GameSession.Reconnect</c>): the world is left as on a logoff (every object
    /// gets <see cref="ObjectDeleted"/>, the player <see cref="SelfTravel.NotInWorld"/>), then the phase returns to
    /// <see cref="SessionPhase.Idle"/> and the list, chat and server time are forgotten. Subscribers are kept.
    /// </summary>
    internal void Reset()
    {
        LeaveWorld();
        var previous = _state.Phase;
        _state.Phase = SessionPhase.Idle;
        _state.Failure = null;
        _state.DataCheck = DataCheckState.NotStarted;
        _state.WorldName = null;
        var list = _state.Characters;
        list.Mutable.Clear();
        list.SlotCount = 0;
        list.SelectedGuid = 0;
        list.Version++;
        _state.Chat.Clear();
        _state.Time.Reset();
        _parked.Clear();
        _silentToolIds.Clear();
        _attempt = 1;
        _bootBalance = 0;
        _characterErrorBalance = 0;
        _createsAwaiting = 0;
        _restoreAwaiting = 0;
        _deleteAwaitingList = false;
        _lastCreateAnswer = default;
        _lastCreateAnswerAt = double.NegativeInfinity;
        StandIn?.Clear();
        Changed();
        if (previous != SessionPhase.Idle)
        {
            Publish(new SessionPhaseChanged(previous, SessionPhase.Idle, null));
        }
    }

    /// <summary>
    /// Removes the parked messages that waited <see cref="PendingObjectMessages.ExpirySeconds"/> or more, each reported as
    /// <c>MessageDropped(ParkingExpired)</c>. The session calls it once per tick; it returns at once when none can have
    /// expired.
    /// </summary>
    internal void ExpireParked()
    {
        var now = _clock.Now;
        if (_parked.Count == 0 || !_parked.AnyExpired(now))
        {
            return;
        }

        // A fresh list: the drops below run handlers, which may reach the reducer again.
        var expired = new List<ParkedMessage>();
        _parked.TakeExpired(now, expired);
        foreach (var entry in expired)
        {
            Counters.ParkingExpired++;
            ReportDrop(entry.Opcode, entry.ObjectGuid, DropReason.ParkingExpired);
        }
    }

    private void Changed() => _state.Version++;

    private void Publish<T>(in T e)
        where T : struct, IGameEvent => _bus.Publish(in e);

    /// <summary>Publishes <see cref="MessageDropped"/> (it costs nothing without subscribers) and logs it at DEBUG.</summary>
    private void ReportDrop(uint opcode, uint guid, DropReason reason)
    {
        Counters.Dropped++;
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(SessionLog.Category, "message dropped", new LogField("opcode", opcode), new LogField("guid", guid), new LogField("reason", reason));
        }

        Publish(new MessageDropped(opcode, guid, reason));
    }

    /// <summary>A session-local option change (<see cref="SessionNotices.SessionOptionSet"/>): the reducer owns the override, <see cref="PlayerQualities"/> merges it over the server's words.</summary>
    private void ApplySessionOption(in RecordingNotice notice)
    {
        var text = notice.Text ?? string.Empty;
        if (text.Length != 3 || text[1] != ':' || text[0] is not ('1' or '2') || text[2] is not ('0' or '1') || notice.Value == 0)
        {
            throw new InvalidDataException($"local notice {SessionNotices.SessionOptionSet} (session option) has a bad payload: value 0x{notice.Value:X}, text '{text}' (want '1:1', '2:0' ...)");
        }

        _state.Self.Qualities.SetSessionOption(text[0] - '0', notice.Value, text[2] == '1');
        Changed();
        Publish(new QualitiesChanged(QualityChange.Options, notice.Value));
    }

    private void ChangePhase(SessionPhase phase)
    {
        var previous = _state.Phase;
        if (previous == phase)
        {
            return;
        }

        _state.Phase = phase;
        if (phase != SessionPhase.Failed)
        {
            _state.Failure = null;
        }

        if (phase == SessionPhase.Connecting)
        {
            // Every attempt (a retry included) runs the data check again.
            _state.DataCheck = DataCheckState.NotStarted;
            if (previous is SessionPhase.Idle or SessionPhase.Closed or SessionPhase.Failed)
            {
                _attempt = 1;
                _bootBalance = 0;
                _characterErrorBalance = 0;
            }
        }

        if (phase == SessionPhase.EnteringWorld)
        {
            // The in-game shell's chat starts empty for each world entry (retail's chat log dies with gmGamePlayUI).
            _state.Chat.EntryMark = _state.Chat.Total;
        }

        var dataCheckStarted = phase == SessionPhase.DataCheck && _state.DataCheck == DataCheckState.NotStarted;
        if (dataCheckStarted)
        {
            _state.DataCheck = DataCheckState.Checking;
        }

        Changed();
        Publish(new SessionPhaseChanged(previous, phase, _state.Failure));
        switch (phase)
        {
            case SessionPhase.Connecting:
                Publish(new ConnectionProgress(ConnectionStage.Connecting, _attempt, TimeSpan.Zero, 0, null));
                break;
            case SessionPhase.Handshaking:
                Publish(new ConnectionProgress(ConnectionStage.Handshaking, _attempt, TimeSpan.Zero, 0, null));
                break;
        }

        if (dataCheckStarted)
        {
            Publish(new ConnectionProgress(ConnectionStage.DataCheckStarted, _attempt, TimeSpan.Zero, 0, null));
        }
    }

    private void Fail(SessionFailure? failure)
    {
        var previous = _state.Phase;
        if (previous == SessionPhase.Failed && Equals(_state.Failure, failure))
        {
            return;
        }

        _state.Phase = SessionPhase.Failed;
        _state.Failure = failure;
        Changed();
        _log.Warn(
            SessionLog.Category,
            "session failed: " + (failure is null ? "no reason given" : failure.Kind + ": " + failure.Message),
            new LogField("phase", failure?.Phase ?? previous));
        Publish(new SessionPhaseChanged(previous, SessionPhase.Failed, failure));
    }

    private void UpdateServerTime(double serverSeconds, double clientSeconds)
    {
        _state.Time.Update(serverSeconds, clientSeconds);
        Changed();
        Publish(new ServerTimeUpdated(serverSeconds, clientSeconds));
    }
}

/// <summary>The reducer's counters (<see cref="GameStateReducer.Counters"/>).</summary>
internal sealed class ReducerCounters
{
    /// <summary>Messages parked (for an unknown object, a newer instance, or before the player was known).</summary>
    internal long Parked;

    /// <summary>Parked messages replayed through their handler.</summary>
    internal long Replayed;

    /// <summary>Messages refused by the parking bounds.</summary>
    internal long ParkingFull;

    /// <summary>Parked messages that expired.</summary>
    internal long ParkingExpired;

    /// <summary>Every <see cref="MessageDropped"/> published.</summary>
    internal long Dropped;

    /// <summary>Force-position corrections of the player (<see cref="PositionVerdict.ForceBlip"/>).</summary>
    internal long ForceBlips;

    /// <summary>
    /// Retail's <c>error_count</c> in <c>SmartBox::HandleReceivedPosition</c>: position updates whose position sequence was
    /// not newer while their teleport sequence was.
    /// </summary>
    internal long RetailErrorCount;
}
