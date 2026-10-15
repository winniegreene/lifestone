using System.Diagnostics.CodeAnalysis;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Session.State;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions;

/// <summary>
/// The Action API (A0's action layer; steps ST9 to ST12; docs/session.md §7): every action a person or a bot takes
/// becomes a tracked handle, waits in its lane, is sent within its rate limit, and completes on the server's confirmation,
/// the server's error, its timeout or a refusal. It also owns the movement sink (<see cref="MovementChannel"/>), the
/// automation movement intents (<see cref="MoveIntents"/>) and the player tools (<see cref="PlayerTools"/>).
/// </summary>
/// <remarks>
/// <para><b>When a call is made</b> it returns the handle at once. These checks run first, in order, and complete it
/// there and then: the kill switch (automation only), the arguments, the loopback gate (automation and the tools, live
/// sessions), dry run (automation only), and an offline session's refusal of every server action. Otherwise the handle is
/// queued.</para>
/// <para><b><see cref="Update"/></b> runs once per tick, after the network session's messages went through the reducer and
/// the bus: (1) confirmations observed this tick resolve their trackers; (2) trackers in flight past their timeout end as
/// <see cref="ActionOutcomeKind.TimedOut"/>; (3) queued actions are sent in call order, a quit first, one session-phase
/// action in flight at a time, the others within their lane's rate limit, each after its precondition is checked again;
/// (4) the handles completed by these steps complete, in that order, so their continuations run here, on the game thread,
/// and may issue new actions (they wait for the next tick).</para>
/// <para><b>Every completion</b> goes to the <see cref="ActionLog"/> and writes one <c>session</c> log line with the id, the
/// kind, the source, the outcome and the milliseconds it took (an ERROR for a loud stub, a WARNING for a timeout, INFO
/// otherwise). Summaries and details never hold a secret or the text the player typed.</para>
/// <para>Game thread only.</para>
/// </remarks>
internal sealed class ActionApi : IActionApi, IDisposable
{
    /// <summary>The refusal every server action gets in an offline session.</summary>
    internal const string OfflineRefusal = "offline session: there is no server (stand-in data only)";

    /// <summary>The refusal and cancellation of automation actions under the kill switch.</summary>
    internal const string KillSwitchRefusal = "the kill switch is on";

    private readonly GameStateReducer _reducer;
    private readonly EventBus _bus;
    private readonly ActionLog _actionLog;
    private readonly GameThreadBinding? _thread;
    private readonly RateLimiter?[] _limiters = new RateLimiter?[ActionPolicy.LaneCount];
    private readonly bool[] _laneWaiting = new bool[ActionPolicy.LaneCount];
    private readonly List<ActionTracker> _queued = [];
    private readonly List<ActionTracker> _inFlight = [];
    private readonly Action<ActionHandle, Exception> _onHandlerError;
    private readonly MovementChannel _movement;
    private readonly bool _serverIsLoopback;
    private List<(ActionTracker Tracker, ActionOutcome Outcome)> _completing = [];
    private List<(ActionTracker Tracker, ActionOutcome Outcome)> _completingSpare = [];
    private long _nextId;
    private bool _killSwitch;
    private bool _dryRun;
    private string? _killSwitchRequest;
    private bool _updating;
    private bool _quitting;
    private bool _disposed;
    private int _arrival;
    private int _loginCompleteArrival;

    /// <summary>The Action API of a session.</summary>
    /// <param name="reducer">The session's reducer (its state, clock, local notices and recorder).</param>
    /// <param name="bus">The session's bus (the trackers' observations; <see cref="LogOffRequested"/>).</param>
    /// <param name="actionLog">The session's action log.</param>
    /// <param name="log">Where completions and refusals are logged.</param>
    /// <param name="link">The live session's link, or null for an offline session.</param>
    /// <param name="thread">The session's game-thread binding, or null (tests).</param>
    internal ActionApi(GameStateReducer reducer, EventBus bus, ActionLog actionLog, ILogSink log, ActionLink? link, GameThreadBinding? thread)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(actionLog);
        ArgumentNullException.ThrowIfNull(log);
        _reducer = reducer;
        _bus = bus;
        _actionLog = actionLog;
        Log = log;
        Link = link;
        _thread = thread;
        _serverIsLoopback = link is null || LoopbackGate.IsAllowed(link.Server, out _);
        _onHandlerError = OnHandlerError;
        for (var lane = 0; lane < ActionPolicy.LaneCount; lane++)
        {
            _limiters[lane] = ActionPolicy.CreateLimiter((ActionLane)lane);
        }

        _movement = new MovementChannel(reducer.State, link is null ? null : () => link.Session, log, thread);
        Move = new MoveIntents(this);
        Tools = new PlayerTools(this);

        // The subscriptions live as long as the session's bus: the Action API is the session's own.
        bus.Subscribe<SessionPhaseChanged>(OnPhase);
        bus.Subscribe<CharacterListChanged>(OnCharacterList);
        bus.Subscribe<CharacterCreateResult>(OnCreateResult);
        bus.Subscribe<CharacterErrorReceived>(OnCharacterError);
        bus.Subscribe<SelfTravelChanged>(OnTravel);
        bus.Subscribe<PlayerTeleportAnnounced>(OnTeleport);
        bus.Subscribe<UseDone>(OnUseDone);
        bus.Subscribe<HealthUpdated>(OnHealth);
        bus.Subscribe<ChatReceived>(OnChat);
        bus.Subscribe<ToolsUnavailable>(OnToolsUnavailable);
        reducer.ToolReplyRouted = OnToolReply;
    }

    /// <inheritdoc/>
    public IMovementSink Movement => _movement;

    /// <inheritdoc/>
    public MoveIntents Move { get; }

    /// <summary>The player tools (<see cref="IGameSessionView.Tools"/>).</summary>
    internal PlayerTools Tools { get; }

    /// <inheritdoc/>
    public bool KillSwitch
    {
        get => _killSwitch;
        set
        {
            CheckThread("IActionApi.KillSwitch");
            if (value == _killSwitch)
            {
                return;
            }

            if (value)
            {
                Engage("set by the host");
            }
            else
            {
                _killSwitch = false;
                _actionLog.Touch(); // the inspector shows it
                Log.Info(SessionLog.Category, "kill switch off: automation actions are accepted again");
            }
        }
    }

    /// <inheritdoc/>
    public bool DryRun
    {
        get => _dryRun;
        set
        {
            CheckThread("IActionApi.DryRun"); // the change notice below is raised on the game thread
            if (value != _dryRun)
            {
                _dryRun = value;
                _actionLog.Touch(); // the inspector shows it
            }
        }
    }

    /// <summary>The movement channel (its counters, for the inspector and the tests).</summary>
    internal MovementChannel Channel => _movement;

    /// <summary>Where completions and refusals are logged.</summary>
    internal ILogSink Log { get; }

    /// <summary>The live session's link, or null offline.</summary>
    internal ActionLink? Link { get; }

    /// <summary>True for an offline session's Action API.</summary>
    internal bool IsOffline => Link is null;

    /// <summary>True when the server is a loopback endpoint (always, offline: there is none).</summary>
    internal bool ServerIsLoopback => _serverIsLoopback;

    /// <summary>The session's state.</summary>
    internal GameState State => _reducer.State;

    /// <summary>The session clock.</summary>
    internal SessionClock Clock => _reducer.Clock;

    /// <summary>The frame time of the current (or last) <see cref="Update"/>: the host's frame clock.</summary>
    internal double FrameTime { get; private set; }

    /// <summary>Actions waiting for their turn (tests, the inspector).</summary>
    internal int QueuedCount => CountIn(_queued, TrackerState.Queued);

    /// <summary>Actions sent and waiting for their confirmation (tests, the inspector).</summary>
    internal int InFlightCount => CountIn(_inFlight, TrackerState.InFlight);

    /// <summary>True when LoginComplete was already sent for the player's current arrival.</summary>
    internal bool LoginCompleteSentThisArrival => _arrival > 0 && _loginCompleteArrival == _arrival;

    /// <inheritdoc/>
    public ActionHandle Login(ActionSource source = ActionSource.Host)
    {
        CheckThread("IActionApi.Login");
        return Accept(new LoginTracker(NewHandle(ActionKind.Login, source, "Login")));
    }

    /// <inheritdoc/>
    public ActionHandle SelectCharacter(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.SelectCharacter");
        return Accept(new SelectCharacterTracker(NewHandle(ActionKind.SelectCharacter, source, "SelectCharacter guid=" + ActionText.Hex(guid)), guid), now: true);
    }

    /// <inheritdoc/>
    public ActionHandle CreateCharacter(CharacterCreateIntent intent, ActionSource source)
    {
        CheckThread("IActionApi.CreateCharacter");
        var handle = NewHandle(ActionKind.CreateCharacter, source, "CreateCharacter chars=" + ActionText.Count(intent?.Name?.Length ?? 0));
        string? problem = null;
        if (intent is null)
        {
            problem = "no character to create";
        }
        else if (!intent.TryValidate(out var invalid))
        {
            problem = invalid;
        }

        return Accept(new CreateCharacterTracker(handle, intent), problem is null ? null : Refused(problem));
    }

    /// <inheritdoc/>
    public ActionHandle DeleteCharacter(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.DeleteCharacter");
        return Accept(new DeleteCharacterTracker(NewHandle(ActionKind.DeleteCharacter, source, "DeleteCharacter guid=" + ActionText.Hex(guid)), guid));
    }

    /// <inheritdoc/>
    public ActionHandle RestoreCharacter(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.RestoreCharacter");
        return Accept(new RestoreCharacterTracker(NewHandle(ActionKind.RestoreCharacter, source, "RestoreCharacter guid=" + ActionText.Hex(guid)), guid));
    }

    /// <inheritdoc/>
    public ActionHandle EnterWorld(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.EnterWorld");
        return Accept(new EnterWorldTracker(NewHandle(ActionKind.EnterWorld, source, "EnterWorld guid=" + ActionText.Hex(guid)), guid));
    }

    /// <inheritdoc/>
    public ActionHandle LoginComplete(ActionSource source = ActionSource.Client)
    {
        CheckThread("IActionApi.LoginComplete");
        return Accept(new LoginCompleteTracker(NewHandle(ActionKind.LoginComplete, source, "LoginComplete")));
    }

    /// <inheritdoc/>
    public ActionHandle Use(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.Use");
        var handle = NewHandle(ActionKind.Use, source, "Use guid=" + ActionText.Hex(guid));
        return Accept(new UseTracker(handle, guid), guid == 0 ? Refused("no object to use") : null);
    }

    /// <inheritdoc/>
    public ActionHandle QueryHealth(uint guid, ActionSource source)
    {
        CheckThread("IActionApi.QueryHealth");
        return Accept(new QueryHealthTracker(NewHandle(ActionKind.QueryHealth, source, "QueryHealth guid=" + ActionText.Hex(guid)), guid));
    }

    /// <inheritdoc/>
    public ActionHandle Talk(string text, ActionSource source)
    {
        CheckThread("IActionApi.Talk");
        var handle = NewHandle(ActionKind.Talk, source, "Talk chars=" + ActionText.Count(text?.Length ?? 0));
        return Accept(new TalkTracker(handle, text ?? string.Empty), string.IsNullOrEmpty(text) ? Refused("nothing to say") : null);
    }

    /// <inheritdoc/>
    public ActionHandle Tell(string target, string text, ActionSource source)
    {
        CheckThread("IActionApi.Tell");
        var handle = NewHandle(ActionKind.Tell, source, "Tell chars=" + ActionText.Count(text?.Length ?? 0));
        var problem = string.IsNullOrWhiteSpace(target) ? "no one to tell" : string.IsNullOrEmpty(text) ? "nothing to tell" : null;
        return Accept(new TellTracker(handle, target ?? string.Empty, text ?? string.Empty), problem is null ? null : Refused(problem));
    }

    /// <inheritdoc/>
    public ActionHandle LogOff(ActionSource source)
    {
        CheckThread("IActionApi.LogOff");
        return Accept(new LogOffTracker(NewHandle(ActionKind.LogOff, source, "LogOff")));
    }

    /// <inheritdoc/>
    public void SetSessionOption(int word, uint mask, bool value)
    {
        CheckThread("IActionApi.SetSessionOption");
        if (word is not (1 or 2) || mask == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(word), $"session option word {word} mask 0x{mask:X}: want word 1 or 2 and a non-zero mask");
        }

        ApplyLocal(SessionNotices.SessionOptionSet, mask, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{word}:{(value ? 1 : 0)}"));
    }

    /// <inheritdoc/>
    public ActionHandle Quit(ActionSource source)
    {
        CheckThread("IActionApi.Quit");
        return Accept(new QuitTracker(NewHandle(ActionKind.Quit, source, "Quit")));
    }

    /// <summary>
    /// Runs the action layer for one tick (see the remarks for the order). The session calls it from
    /// <c>GameSession.Tick</c>, after the reducer took the tick's messages.
    /// </summary>
    /// <param name="frameTime">The tick's frame time (the host's frame clock), for <see cref="LogOffRequested"/> and the jump bar.</param>
    internal void Update(double frameTime)
    {
        CheckThread("ActionApi.Update");
        if (_updating)
        {
            throw new InvalidOperationException("the Action API's update was re-entered (a continuation ticked the session)");
        }

        _updating = true;
        try
        {
            FrameTime = frameTime;
            if (_killSwitchRequest is { } reason)
            {
                _killSwitchRequest = null;
                if (!_killSwitch)
                {
                    Engage(reason);
                }
            }

            var now = Clock.Now;
            ResolveObserved();
            ExpireTimeouts(now);
            Dispatch(now);
            ResolveObserved();
            CompletePending();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Cancels every action waiting or in flight with <paramref name="reason"/> (<see cref="ActionOutcomeKind.Cancelled"/>):
    /// the session ended or was reset.
    /// </summary>
    internal void CancelAll(string reason)
    {
        var cancelled = new ActionOutcome(ActionOutcomeKind.Cancelled, 0, reason);
        CancelIn(_queued, TrackerState.Queued, cancelled);
        CancelIn(_inFlight, TrackerState.InFlight, cancelled);

        // Settled but not completed yet (outside an update this is empty): they complete with what they settled on.
        for (var i = 0; i < _completing.Count; i++)
        {
            var (tracker, outcome) = _completing[i];
            Complete(tracker, outcome);
        }

        _completing.Clear();
        if (!_updating)
        {
            Compact();
        }
    }

    /// <summary>
    /// After a reconnect: every pending action is cancelled, the rate limits refill, and what the server said about the
    /// tools and the arrivals is forgotten. The movement controller stays bound.
    /// </summary>
    internal void Reset()
    {
        CancelAll("the session was reset (reconnect)");
        foreach (var limiter in _limiters)
        {
            limiter?.Reset();
        }

        _quitting = false;
        _arrival = 0;
        _loginCompleteArrival = 0;
        _killSwitchRequest = null;
        _movement.Reset();
        Tools.Reset();
    }

    /// <summary>
    /// Ends the Action API with its session: every pending action is cancelled (on the calling thread, as the session's
    /// <c>Dispose</c> allows), later calls complete as cancelled, and the tool replies stop being routed here.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAll("the session ended");
        if (_reducer.ToolReplyRouted == OnToolReply)
        {
            _reducer.ToolReplyRouted = null;
        }
    }

    /// <summary>
    /// Applies a recorded notice of the Action API's own (<see cref="ActionNotices"/>; a replay): it publishes again what
    /// the Action API published live.
    /// </summary>
    /// <exception cref="InvalidDataException">The code is not one the Action API knows.</exception>
    internal void ApplyRecordedNotice(in RecordingNotice notice) => ActionNotices.Publish(_bus, notice, FrameTime);

    /// <summary>Throws when called off the session's game thread (once the session is bound).</summary>
    internal void CheckThread(string member) => _thread?.Check(member);

    /// <summary>The next handle id (rising from 1; actions, tools and intents share it).</summary>
    internal long NextId() => ++_nextId;

    /// <summary>A queued handle with the next id.</summary>
    internal ActionHandle NewHandle(ActionKind kind, ActionSource source, string summary) => new(NextId(), kind, source, summary, Clock.Now);

    /// <summary>
    /// Accepts <paramref name="tracker"/>: runs the call-time checks (see the class remarks), completing the handle when
    /// one applies, and otherwise queues it, or with <paramref name="now"/> sends it at once (a local action).
    /// </summary>
    /// <param name="tracker">The action.</param>
    /// <param name="early">An outcome decided by the caller (bad arguments, a loud stub), applied after the kill switch.</param>
    /// <param name="now">True to dispatch it now instead of queueing it.</param>
    /// <param name="loopbackChecked">True when the caller already applied the loopback gate (the tools).</param>
    internal ActionHandle Accept(ActionTracker tracker, ActionOutcome? early = null, bool now = false, bool loopbackChecked = false)
    {
        var handle = tracker.Handle;
        if (_disposed)
        {
            return CompleteNow(tracker, new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the session ended"));
        }

        var automation = handle.Source == ActionSource.Automation;
        if (automation && _killSwitch)
        {
            return CompleteNow(tracker, Refused(KillSwitchRefusal));
        }

        if (early is { } decided)
        {
            return CompleteNow(tracker, decided);
        }

        if (automation && !loopbackChecked && !CheckLoopback("IActionApi." + handle.Kind + " (automation)", out var refusal))
        {
            return CompleteNow(tracker, Refused(refusal));
        }

        if (automation && DryRun)
        {
            var offline = IsOffline && tracker.NeedsSession ? " (offline: nothing could be sent)" : string.Empty;
            Log.Info(SessionLog.Category, "dry run: " + handle.Summary + ": would send " + tracker.WouldSend + offline);
            return CompleteNow(tracker, new ActionOutcome(ActionOutcomeKind.DryRun, 0, "would send " + tracker.WouldSend));
        }

        if (IsOffline && tracker.NeedsSession && LocalSession(tracker) is null)
        {
            return CompleteNow(tracker, Refused(OfflineRefusal));
        }

        if (now)
        {
            if (tracker.Refusal(this) is { } notNow)
            {
                return CompleteNow(tracker, Refused(notNow));
            }

            tracker.State = TrackerState.InFlight;
            return CompleteNow(tracker, SendGuarded(tracker, null) ?? Refused("TODO(state): a local action did not complete at once"));
        }

        _queued.Add(tracker);
        return handle;
    }

    /// <summary>
    /// The offline stand-in responder's network-session face when this is an offline session that opted in and <paramref name="tracker"/>'s
    /// action is one it answers (delete, restore, create); null otherwise, so every other offline action stays refused and a live
    /// session never reaches it.
    /// </summary>
    private IAcSession? LocalSession(ActionTracker tracker) =>
        Link is null && _reducer.LocalResponder is { } responder && responder.Handles(tracker.Handle.Kind) ? responder.Session : null;

    /// <summary>True for an offline session whose stand-in answers the world's actions (the play-mode checks: tools, Use, log off), so the tools work there.</summary>
    internal bool StandInAnswersWorld => Link is null && _reducer.LocalResponder is { World: not null };

    /// <summary>
    /// The loopback gate for automation and the tools (A0's R1, S8): true when the server is a loopback endpoint (always
    /// offline); otherwise one WARNING naming <paramref name="runner"/> and false with the reason.
    /// </summary>
    internal bool CheckLoopback(string runner, [NotNullWhen(false)] out string? refusal)
    {
        refusal = null;
        if (_serverIsLoopback)
        {
            return true;
        }

        if (!LoopbackGate.TryRequire(Link!.Server, runner, Log, out _, out refusal))
        {
            return false;
        }

        // Unreachable: the gate's rule and IsAllowed are the same rule.
        refusal = "TODO(state): the loopback gate disagreed with itself";
        Log.Error(SessionLog.Category, refusal);
        return false;
    }

    /// <summary>Applies a local change through the reducer (a <see cref="SessionNotices"/> code), so the recording keeps it.</summary>
    internal void ApplyLocal(ushort code, uint value = 0, string? text = null) => _reducer.ApplyLocal(new RecordingNotice(code, value, text));

    /// <summary>
    /// Publishes <see cref="LogOffRequested"/> with the frame time, after recording notice
    /// <see cref="ActionNotices.LogOffRequested"/> so a replay publishes it again.
    /// </summary>
    internal void PublishLogOffRequested(bool playerKillerDelay)
    {
        _reducer.Recorder?.Notice(Clock.Now, new RecordingNotice(ActionNotices.LogOffRequested, playerKillerDelay ? 1u : 0u, null));
        _bus.Publish(new LogOffRequested(FrameTime, playerKillerDelay));
    }

    /// <summary>Publishes <see cref="ToolsUnavailable"/> for a reason the reducer does not see (a non-loopback server).</summary>
    internal void PublishToolsUnavailable(ToolAvailability availability, string reason) => _bus.Publish(new ToolsUnavailable(availability, reason));

    /// <summary>LoginComplete went out for the current arrival.</summary>
    internal void MarkLoginCompleteSent() => _loginCompleteArrival = _arrival;

    /// <summary>
    /// Asks for the kill switch at the start of the next update (a server reply must not complete handles during the
    /// reducer's dispatch). The first reason wins.
    /// </summary>
    internal void RequestKillSwitch(string reason) => _killSwitchRequest ??= reason;

    private static ActionOutcome Refused(string why) => new(ActionOutcomeKind.Refused, 0, why);

    private static int CountIn(List<ActionTracker> list, TrackerState state)
    {
        var count = 0;
        foreach (var tracker in list)
        {
            if (tracker.State == state)
            {
                count++;
            }
        }

        return count;
    }

    private void Engage(string reason)
    {
        _killSwitch = true;
        _actionLog.Touch(); // the inspector shows it
        var cancelled = new ActionOutcome(ActionOutcomeKind.Cancelled, 0, KillSwitchRefusal);
        var count = 0;
        for (var i = 0; i < _queued.Count; i++)
        {
            // An index loop: a completion runs continuations, which may queue new actions.
            var tracker = _queued[i];
            if (tracker.State == TrackerState.Queued && tracker.Handle.Source == ActionSource.Automation)
            {
                Complete(tracker, cancelled);
                count++;
            }
        }

        var released = Move.ReleaseAutomation();
        Log.Warn(
            SessionLog.Category,
            "kill switch on: " + reason + "; automation actions are refused",
            new LogField("cancelled", count),
            new LogField("movementReleased", released));
    }

    private ActionHandle CompleteNow(ActionTracker tracker, ActionOutcome outcome)
    {
        Complete(tracker, outcome);
        return tracker.Handle;
    }

    /// <summary>Completes a tracker's handle: the action log, the log line, then the handle (its continuations run now).</summary>
    private void Complete(ActionTracker tracker, ActionOutcome outcome)
    {
        tracker.State = TrackerState.Done;
        var handle = tracker.Handle;
        if (handle.IsCompleted)
        {
            return;
        }

        var now = Clock.Now;
        _actionLog.Add(new ActionLogEntry(handle.Id, handle.Kind, handle.Source, handle.Summary, outcome, handle.QueuedAt, now, tracker.RateLimited));
        var line = "action #" + ActionText.Count(handle.Id) + " " + handle.Kind + " (" + handle.Source + ") " + outcome.Kind + ": " + handle.Summary +
            (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
        var milliseconds = Math.Round((now - handle.QueuedAt) * 1000.0, 1);
        switch (outcome.Kind)
        {
            case ActionOutcomeKind.NotImplemented:
                Log.Error(SessionLog.Category, line, new LogField("source", handle.Source), new LogField("ms", milliseconds));
                break;
            case ActionOutcomeKind.TimedOut:
                Log.Warn(SessionLog.Category, line, new LogField("source", handle.Source), new LogField("ms", milliseconds));
                break;
            default:
                Log.Info(SessionLog.Category, line, new LogField("source", handle.Source), new LogField("ms", milliseconds));
                break;
        }

        tracker.CompleteHandle(outcome, now, _onHandlerError);
    }

    private void OnHandlerError(ActionHandle handle, Exception e) =>
        Log.Error(SessionLog.Category, $"a Completed handler of action #{handle.Id} threw {e.GetType().Name}: {e.Message}");

    /// <summary>Step 1: trackers in flight whose confirmation (or error) was observed are handed to the completion step.</summary>
    private void ResolveObserved()
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            var tracker = _inFlight[i];
            if (tracker.State != TrackerState.InFlight)
            {
                continue;
            }

            if (tracker.Resolution is null)
            {
                tracker.Poll(this);
            }

            if (tracker.Resolution is null && State.Phase == SessionPhase.Failed)
            {
                // A failure reported without its reason, or raised by the send itself before the tracker listened.
                tracker.Resolve(tracker.SessionFailed(State.Failure));
            }

            if (tracker.Resolution is { } outcome)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, tracker.Finish(outcome)));
            }
        }

        Compact();
    }

    /// <summary>Step 2: trackers in flight past their timeout end as timed out.</summary>
    private void ExpireTimeouts(double now)
    {
        foreach (var tracker in _inFlight)
        {
            if (tracker.State == TrackerState.InFlight && tracker.Handle.SentAt is { } sent && now - sent >= tracker.TimeoutSeconds)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, new ActionOutcome(
                    ActionOutcomeKind.TimedOut,
                    0,
                    "no confirmation within " + ActionText.Count((long)tracker.TimeoutSeconds) + " s")));
            }
        }

        Compact();
    }

    /// <summary>Step 3: queued actions go out in call order, a quit first, within the lanes' rules.</summary>
    private void Dispatch(double now)
    {
        Array.Clear(_laneWaiting);
        var count = _queued.Count;
        for (var i = 0; i < count && i < _queued.Count; i++)
        {
            if (_queued[i] is QuitTracker { State: TrackerState.Queued } quit)
            {
                DispatchQuit(quit, now);
            }
        }

        for (var i = 0; i < count && i < _queued.Count; i++)
        {
            var tracker = _queued[i];
            if (tracker.State != TrackerState.Queued)
            {
                continue;
            }

            var lane = (int)tracker.Lane;
            if (_laneWaiting[lane])
            {
                continue;
            }

            if (tracker.Lane == ActionLane.Session && SessionActionInFlight())
            {
                _laneWaiting[lane] = true;
                continue;
            }

            if (tracker.Refusal(this) is { } refusal)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, Refused(refusal)));
                continue;
            }

            if (_limiters[lane] is { } limiter && !limiter.TryTake(now))
            {
                tracker.RateLimited = true;
                _laneWaiting[lane] = true;
                continue;
            }

            Send(tracker, now);
        }

        Compact();
    }

    private void DispatchQuit(QuitTracker quit, double now)
    {
        if (_quitting)
        {
            quit.State = TrackerState.Done;
            _completing.Add((quit, Refused("the session is already quitting")));
            return;
        }

        if (quit.Refusal(this) is { } refusal)
        {
            quit.State = TrackerState.Done;
            _completing.Add((quit, Refused(refusal)));
            return;
        }

        _quitting = true;
        var cancelled = new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the session is quitting");
        foreach (var tracker in _queued)
        {
            if (tracker != quit && tracker.State == TrackerState.Queued)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, cancelled));
            }
        }

        foreach (var tracker in _inFlight)
        {
            if (tracker.State == TrackerState.InFlight)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, cancelled));
            }
        }

        Send(quit, now);
    }

    private bool SessionActionInFlight()
    {
        foreach (var tracker in _inFlight)
        {
            if (tracker.State == TrackerState.InFlight && tracker.Lane == ActionLane.Session)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sends <paramref name="tracker"/>: in flight before the send (so a failure the send raises at once is observed),
    /// completed at once for an immediate outcome or a refused send.
    /// </summary>
    private void Send(ActionTracker tracker, double now)
    {
        IAcSession? session = null;
        if (tracker.NeedsSession)
        {
            session = Link is null ? LocalSession(tracker) : Link.Session;
            if (session is null)
            {
                tracker.State = TrackerState.Done;
                _completing.Add((tracker, Refused("there is no network session")));
                return;
            }
        }

        tracker.State = TrackerState.InFlight;
        _inFlight.Add(tracker);
        var immediate = SendGuarded(tracker, session);
        if (immediate is { } outcome)
        {
            if (outcome.Kind == ActionOutcomeKind.Sent)
            {
                tracker.Handle.MarkSent(now);
            }

            tracker.State = TrackerState.Done;
            _completing.Add((tracker, outcome));
        }
        else
        {
            tracker.Handle.MarkSent(now);
        }
    }

    /// <summary>The tracker's send, with net's exceptions as outcomes: a refused send, a closed session, a net loud stub.</summary>
    private ActionOutcome? SendGuarded(ActionTracker tracker, IAcSession? session)
    {
        try
        {
            return tracker.Send(session, this);
        }
        catch (ObjectDisposedException)
        {
            return new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the network session is closed");
        }
        catch (NotSupportedException e)
        {
            return new ActionOutcome(ActionOutcomeKind.NotImplemented, 0, e.Message);
        }
        catch (InvalidOperationException e)
        {
            return Refused("the network session refused it: " + e.Message);
        }
        catch (ArgumentException e)
        {
            return Refused(e.Message);
        }
    }

    /// <summary>Step 4: the handles of this update complete, in the order the steps settled them.</summary>
    private void CompletePending()
    {
        while (_completing.Count > 0)
        {
            (_completing, _completingSpare) = (_completingSpare, _completing);
            foreach (var (tracker, outcome) in _completingSpare)
            {
                Complete(tracker, outcome);
            }

            _completingSpare.Clear();
        }
    }

    private void CancelIn(List<ActionTracker> list, TrackerState state, ActionOutcome outcome)
    {
        // An index loop over the actions there now: a completion runs continuations, which may queue new actions, and
        // those are not cancelled with the old ones.
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            var tracker = list[i];
            if (tracker.State == state)
            {
                Complete(tracker, outcome);
            }
        }
    }

    private void Compact()
    {
        _queued.RemoveAll(static t => t.State != TrackerState.Queued);
        _inFlight.RemoveAll(static t => t.State != TrackerState.InFlight);
    }

    private void OnPhase(in SessionPhaseChanged e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnPhase(in e, this);
            }
        }
    }

    private void OnCharacterList(in CharacterListChanged e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnCharacterList(in e);
            }
        }
    }

    private void OnCreateResult(in CharacterCreateResult e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnCreateResult(in e);
            }
        }
    }

    private void OnCharacterError(in CharacterErrorReceived e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnCharacterError(in e);
            }
        }
    }

    private void OnTravel(in SelfTravelChanged e)
    {
        if (e.Travel is SelfTravel.Arriving or SelfTravel.TeleportArrived)
        {
            _arrival++;
        }

        _movement.OnTravel(in e);
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnTravel(in e);
            }
        }

        if (_disposed)
        {
            return;
        }

        if (e.Previous == SelfTravel.NotInWorld && e.Travel == SelfTravel.Arriving)
        {
            Tools.OnEnteringWorld();
        }
        else if (e.Travel == SelfTravel.NotInWorld)
        {
            Tools.OnLeftWorld();
        }
    }

    private void OnTeleport(in PlayerTeleportAnnounced e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnTeleport(in e);
            }
        }
    }

    private void OnUseDone(in UseDone e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            var tracker = _inFlight[i];
            if (tracker.State == TrackerState.InFlight && tracker.Resolution is null && tracker.OnUseDone(in e))
            {
                return;
            }
        }
    }

    private void OnHealth(in HealthUpdated e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            var tracker = _inFlight[i];
            if (tracker.State == TrackerState.InFlight && tracker.Resolution is null && tracker.OnHealth(in e))
            {
                return;
            }
        }
    }

    private void OnChat(in ChatReceived e)
    {
        for (var i = 0; i < _inFlight.Count; i++)
        {
            var tracker = _inFlight[i];
            if (tracker.State == TrackerState.InFlight && tracker.Resolution is null && tracker.OnChat(in e, this))
            {
                return;
            }
        }
    }

    private void OnToolsUnavailable(in ToolsUnavailable e)
    {
        if (e.Availability == ToolAvailability.ModMissing)
        {
            Tools.OnModMissing();
        }

        for (var i = 0; i < _inFlight.Count; i++)
        {
            if (_inFlight[i].State == TrackerState.InFlight)
            {
                _inFlight[i].OnToolsUnavailable(in e);
            }
        }
    }

    private void OnToolReply(ToolReplyLine reply)
    {
        Tools.OnReply();
        for (var i = 0; i < _inFlight.Count; i++)
        {
            var tracker = _inFlight[i];
            if (tracker.State == TrackerState.InFlight && tracker.Resolution is null && tracker.OnToolReply(reply, this))
            {
                return;
            }
        }
    }
}
