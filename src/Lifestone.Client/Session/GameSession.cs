using System.Net;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Session.State;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Net.Taps;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session;

/// <summary>
/// Creates a live session's network session for its sink (the session's reducer) and its raw tap. Built once by
/// <see cref="GameSession"/>'s creation and again by every <see cref="GameSession.Reconnect"/>. <see cref="SessionBootstrap"/>'s
/// factory calls net's <c>AcSession.Create</c> (its file is the one product caller); tests and replays pass their own.
/// </summary>
/// <param name="sink">The session's reducer.</param>
/// <param name="tap">The session's raw tap (the bus's raw subscribers and the recorder).</param>
internal delegate IAcSession NetworkSessionFactory(IAcSessionSink sink, IRawMessageTap tap);

/// <summary>What a live <see cref="GameSession"/> is built from (<see cref="SessionBootstrap"/> makes one after its gates).</summary>
/// <param name="Server">The server the gates passed (the Action API's loopback checks repeat on it).</param>
/// <param name="Credentials">The account; the session owns it from now on and zeroes it when disposed.</param>
/// <param name="Network">Creates the network session (again on each reconnect).</param>
/// <param name="Budget">The network session's dispatch budget per tick (<see cref="SessionOptions.BudgetMs"/>).</param>
/// <param name="Recording">Where to record, or null (the host owns the stream).</param>
/// <param name="RecordingHeader">The recording's header, or null for a default one (runner <c>session</c>).</param>
internal sealed record LiveSessionSetup(
    EndPoint Server,
    AcCredentials Credentials,
    NetworkSessionFactory Network,
    TimeSpan Budget,
    Stream? Recording,
    RecordingHeader? RecordingHeader);

/// <summary>
/// A game session: the network session (or stand-in data), the reducer, the event bus, the Action API, the player tools
/// and the recorder, behind <see cref="IGameSessionView"/> (A0, docs/session.md). The host creates it
/// (<see cref="SessionBootstrap.CreateLive"/> for a live session, <see cref="Offline"/> for stand-in data), calls
/// <see cref="Tick"/> once per frame on its game thread, and disposes it; everyone else holds the view.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> One game thread. The first <see cref="Tick"/> binds the session to its thread; ticking,
/// acting and subscribing from another thread then throw <see cref="InvalidOperationException"/>. Every event, every
/// handle completion and every continuation that awaits a handle on the game thread runs inside <see cref="Tick"/>.</para>
/// <para><b>Tick order.</b> (1) the network session pumps for at most the options' budget of dispatch time (the
/// transport sweeps always run), and each message goes to the raw tap and the recorder, then to the reducer, which
/// updates <see cref="State"/> and publishes its events; (2) parked messages that waited too long are dropped; (3) the
/// Action API resolves the confirmations it saw, expires timeouts, sends queued actions within their rate limits, and
/// completes handles; (4) the recorder flushes (at most once a second). An offline session runs its due stand-in steps in
/// place of (1).</para>
/// <para><b>Credentials.</b> A live session owns the account's credentials (net's <see cref="AcCredentials"/>, a zeroable
/// <see cref="char"/> array, never a string) until it is disposed, so <see cref="Reconnect"/> needs no secret; the network
/// session zeroes its own copy on reaching character select.</para>
/// </remarks>
public sealed class GameSession : IGameSessionView, IDisposable
{
    /// <summary>The runner token of a recording whose host gave no header.</summary>
    internal const string DefaultRunner = "session";

    private readonly GameThreadBinding _thread = new();
    private readonly ILogSink _log;
    private readonly TimeProvider _time;
    private readonly EventBus _bus;
    private readonly GameStateReducer _reducer;
    private readonly ActionApi _actions;
    private readonly Action<IInboundMessageHandler>? _standIn;
    private readonly LiveSessionSetup? _live;
    private readonly SessionRecorder? _recorder;
    private readonly IRawMessageTap? _tap;
    private readonly TimeSpan _budget;
    private IAcSession? _net;
    private bool _disposed;

    private GameSession(Action<IInboundMessageHandler> standIn, TimeProvider time, ILogSink log)
    {
        _log = log;
        _time = time;
        _standIn = standIn;
        Clock = new SessionClock(time);
        _bus = new EventBus(log, _thread, time);
        State = new GameState { IsOffline = true };
        _reducer = new GameStateReducer(State, _bus, Clock, log) { StandIn = new StandInSchedule() };
        ActionLog = new ActionLog();
        _actions = new ActionApi(_reducer, _bus, ActionLog, log, link: null, _thread);
        _reducer.SpellLookup = _actions.FindSpell;
    }

    private GameSession(LiveSessionSetup setup, TimeProvider time, ILogSink log)
    {
        _log = log;
        _time = time;
        Clock = new SessionClock(time);
        _bus = new EventBus(log, _thread, time);
        State = new GameState();
        _reducer = new GameStateReducer(State, _bus, Clock, log);
        ActionLog = new ActionLog();
        _live = setup;
        _budget = setup.Budget;
        if (setup.Recording is { } stream)
        {
            var header = setup.RecordingHeader ?? new RecordingHeader { Runner = DefaultRunner, CreatedUtc = time.GetUtcNow() };
            _recorder = new SessionRecorder(stream, header, Clock, log);
            _reducer.Recorder = _recorder;
        }

        _tap = new Tap(this);
        _actions = new ActionApi(_reducer, _bus, ActionLog, log, new ActionLink(setup.Server, () => _net, LogIn), _thread);
        _reducer.SpellLookup = _actions.FindSpell;
    }

    /// <inheritdoc/>
    public GameState State { get; }

    /// <inheritdoc/>
    public IEventBus Events => _bus;

    /// <inheritdoc/>
    public IActionApi Actions => _actions;

    /// <inheritdoc/>
    public AttackSequence Attack => _reducer.Attack;

    /// <summary>True for <c>--retail-cast-any-mode</c> (M3 4.2 D40): this session's casts are sent whatever the combat mode, as retail's client sends them. Default false: refused at once. Per session, so no test shares it (4.2 R1).</summary>
    public bool RetailCastAnyMode
    {
        get => _actions.RetailCastAnyMode;
        set => _actions.RetailCastAnyMode = value;
    }

    /// <inheritdoc/>
    /// <remarks>An offline session refuses every request (there is no server).</remarks>
    public PlayerTools Tools => _actions.Tools;

    /// <inheritdoc/>
    public SessionClock Clock { get; }

    /// <inheritdoc/>
    public ActionLog ActionLog { get; }

    /// <inheritdoc/>
    /// <remarks>The current network session's counters (a new network session after <see cref="Reconnect"/> starts at
    /// zero); all zero for an offline session.</remarks>
    public LinkStats Link => _net?.Link ?? default;

    /// <summary>The frame time the last <see cref="Tick"/> received (the host's frame clock), or 0 before the first.</summary>
    public double FrameTime { get; private set; }

    /// <summary>How long the last <see cref="Tick"/> took, in milliseconds on the session clock (the perf overlay's session row).</summary>
    public double LastTickMs { get; private set; }

    /// <summary>
    /// How many messages the last <see cref="Tick"/>'s pump dispatched (0 offline): with <see cref="LastPumpMilliseconds"/> and <see cref="LastTickMs"/> it names a slow
    /// frame's session part (<c>TODO(login-burst-frame)</c>, the live login check's slow-frame lines).
    /// </summary>
    public int LastPumpMessages => LastPump.Messages;

    /// <summary>How long the last <see cref="Tick"/>'s pump took, in milliseconds on the session clock (0 offline).</summary>
    public double LastPumpMilliseconds { get; private set; }

    /// <summary>Times <see cref="Reconnect"/> has run.</summary>
    public int Reconnects { get; private set; }

    /// <summary>
    /// The server's force-position corrections of the player since the session was created, across reconnects: every
    /// <see cref="ObjectMoved"/> of the player with <see cref="PositionVerdict.ForceBlip"/>. The verdict lines'
    /// <c>corrections=</c> (m2-plan §4.14).
    /// </summary>
    public long Corrections => _reducer.Counters.ForceBlips;

    /// <summary>
    /// Gives the Action API the retail SpellTable from <paramref name="dats"/> (decoded on a worker from this call, <see cref="SpellTableHost"/>; a cast before it is done is of unknown kind), so a cast completes by its spell's kind. A session without it (unit tests,
    /// a host without dats) treats every cast as of unknown kind (<c>TODO(m3-cast-no-spell-table)</c>).
    /// </summary>
    /// <param name="dats">The opened dats.</param>
    public void UseSpellsFrom(Lifestone.Data.Dat.DatSet dats)
    {
        var host = SpellTableHost.Start(dats, _log, _time);
        _spellHost = host;
        _actions.SpellLookup = host.Find;
        _actions.ProjectileSetupLookup = host.ProjectileSetup;
        UseStatTablesFrom(dats);
    }

    /// <summary>
    /// Loads the portal tables the panels and the enchantment rules read, from <paramref name="dats"/> (all small: a few milliseconds, on the calling thread, which is the host's start-up call and not the SpellTable's worker): the XpTable for
    /// <see cref="XpTable"/>, the SkillTable for <see cref="SkillTable"/> and the quality filter's secondary-attribute table into <see cref="EnchantmentRules.Attribute2ndFilter"/> (a process-wide setting the vitals' maximums read, set once
    /// here: every host that opens the dats reaches it through <see cref="UseSpellsFrom"/>, the shell, the headless runner and the session bootstrap). A table that does not load is logged (<c>TODO(m3-stat-tables)</c>) and stays null.
    /// <see cref="UseSpellsFrom"/> calls this.
    /// </summary>
    /// <param name="dats">The opened dats.</param>
    public void UseStatTablesFrom(Lifestone.Data.Dat.DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        try
        {
            _xpTable = Lifestone.Data.CharacterCreation.XpTableData.Load(dats);
            _skillTable = Lifestone.Data.CharacterCreation.SkillTableData.Load(dats);
            EnchantmentRules.Attribute2ndFilter = Lifestone.Data.Dat.QualityFilterData.LoadEnchantable(dats);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or KeyNotFoundException)
        {
            _log.Warn(SessionLog.Category, "TODO(m3-stat-tables): the XpTable or the SkillTable did not load; the skills page has no list or prices: " + ex.Message);
        }
    }

    /// <summary>Gives the session its XpTable and SkillTable directly (tests and hosts that hold them already).</summary>
    /// <param name="xpTable">The XpTable.</param>
    /// <param name="skillTable">The SkillTable.</param>
    public void UseStatTables(Lifestone.Data.CharacterCreation.XpTableData? xpTable, Lifestone.Data.CharacterCreation.SkillTableData? skillTable)
    {
        _xpTable = xpTable;
        _skillTable = skillTable;
    }

    private Lifestone.Data.CharacterCreation.XpTableData? _xpTable;
    private Lifestone.Data.CharacterCreation.SkillTableData? _skillTable;

    /// <inheritdoc/>
    public Lifestone.Data.Magic.SpellInfo? Spell(uint id) => _actions.FindSpell(id);

    /// <inheritdoc/>
    public Lifestone.Data.CharacterCreation.XpTableData? XpTable => _xpTable;

    /// <inheritdoc/>
    public Lifestone.Data.CharacterCreation.SkillTableData? SkillTable => _skillTable;

    /// <summary>Completes when the SpellTable's worker has finished (<see cref="UseSpellsFrom"/>; a completed task without it). Until then a cast's kind is Unknown (M3 1.2 fix R5).</summary>
    public Task SpellTableLoaded => _spellHost?.Loaded ?? Task.CompletedTask;

    /// <summary>
    /// True once the SpellTable is decoded and published (false while loading, after a failed load, and without <see cref="UseSpellsFrom"/>), or when the Action API was given a table another way (<c>ActionApi.SpellLookup</c>,
    /// <c>ActionApi.SpellTable</c>: tests and hosts that hold the spells already), so that <see cref="Spell"/> answers from a table whenever this is true (<see cref="IGameSessionView.SpellTableReady"/>).
    /// </summary>
    public bool SpellTableReady => _spellHost is { } host ? host.Ready : _actions.SpellLookup is not null || _actions.SpellTable is not null;

    private SpellTableHost? _spellHost;

    /// <summary>The reducer (tests and the replayer).</summary>
    internal GameStateReducer Reducer => _reducer;

    /// <inheritdoc/>
    public void CloseVendorWindow() => _reducer.CloseOpenVendor("the player closed the vendor window");

    /// <summary>The Action API (tests, the replayer's action notices, the inspector).</summary>
    internal ActionApi ActionApi => _actions;

    /// <summary>The bus (tests).</summary>
    internal EventBus Bus => _bus;

    /// <summary>The current network session, or null (offline, or after <see cref="Dispose"/>).</summary>
    internal IAcSession? Network => _net;

    /// <summary>The recorder, or null when the host gave no stream.</summary>
    internal SessionRecorder? Recorder => _recorder;

    /// <summary>What the last <see cref="Tick"/>'s pump did (all zero offline).</summary>
    internal PumpResult LastPump { get; private set; }

    /// <summary>
    /// An offline session fed by stand-in data instead of a server (the retail screens without a backend; step ST7b,
    /// checkpoint C-state-offline). <paramref name="standIn"/> is called once, now, with the session's reducer and feeds it
    /// synthetic messages (<c>StandInSessions</c>: Lifestone-authored names only, no dat or server data); timed stand-in
    /// steps run in <see cref="Tick"/>. Every server action is refused; <see cref="GameState.IsOffline"/> is true.
    /// </summary>
    /// <param name="standIn">Feeds the reducer its stand-in messages.</param>
    /// <param name="time">The session clock's time source.</param>
    /// <param name="log">Where the session logs.</param>
    /// <returns>The session.</returns>
    public static GameSession Offline(Action<IInboundMessageHandler> standIn, TimeProvider time, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(standIn);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        var session = new GameSession(standIn, time, log);
        log.Info(SessionLog.Category, "offline session: stand-in data, every server action is refused");
        session.RunStandIn();
        return session;
    }

    /// <summary>
    /// A live session over the network session <paramref name="setup"/> creates (step ST13): idle, with no socket open
    /// until <see cref="IActionApi.Login"/>. <see cref="SessionBootstrap"/> calls it after its gates; tests and the replay
    /// pass their own network sessions. The session owns <paramref name="setup"/>'s credentials once this returns; when it
    /// throws, the caller still does.
    /// </summary>
    /// <param name="setup">The parts.</param>
    /// <param name="time">The session clock's time source, also the network session's.</param>
    /// <param name="log">Where the session logs.</param>
    internal static GameSession CreateLive(LiveSessionSetup setup, TimeProvider time, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        var session = new GameSession(setup, time, log);
        try
        {
            session._net = session.NewNetworkSession();
        }
        catch
        {
            session._recorder?.Dispose();
            throw;
        }

        return session;
    }

    /// <summary>
    /// Runs the session for one frame (see the remarks for the order). Game thread only; allocation-free when nothing
    /// arrives.
    /// </summary>
    /// <param name="now">The frame time: the host's monotonic frame clock in seconds, the same value it gives portal
    /// space, physics and the avatars this frame. Timeouts do not read it (they read <see cref="Clock"/>); it stamps the
    /// events that frame-stepped machines consume (<see cref="LogOffRequested.Now"/>).</param>
    public void Tick(double now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _thread.Bind("GameSession.Tick");
        var start = Clock.Now;
        FrameTime = now;
        if (_net is { } net)
        {
            var pumpStart = Clock.Now;
            LastPump = net.Pump(_budget);
            LastPumpMilliseconds = (Clock.Now - pumpStart) * 1000.0;
        }
        else
        {
            LastPumpMilliseconds = 0.0;
            _reducer.StandIn?.RunDue(start, _reducer);
        }

        _reducer.ExpireParked();
        _actions.Update(now);
        _recorder?.FlushIfDue();
        LastTickMs = (Clock.Now - start) * 1000.0;
    }

    /// <summary>
    /// Waits, off the game thread's work, until a datagram is waiting for the next <see cref="Tick"/> or
    /// <paramref name="max"/> has passed on the session clock's time source: a host without a frame loop (the headless
    /// host) sleeps here between ticks instead of spinning. Always waits the whole <paramref name="max"/> when nothing
    /// arrives, also offline and while the network session has no socket (before login, after a quit). Keep
    /// <paramref name="max"/> short (16 ms): the handshake's timers run in <see cref="Tick"/>. May be awaited from any
    /// thread.
    /// </summary>
    /// <param name="max">The longest wait.</param>
    /// <param name="cancellationToken">Ends the wait early.</param>
    /// <returns>True when something arrived; false after <paramref name="max"/> or on cancellation.</returns>
    public async ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, TimeSpan.Zero);
        var time = Clock.Time;
        var started = time.GetTimestamp();
        if (_net is { } net)
        {
            try
            {
                if (await net.WaitForInboundAsync(max, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
                // A reconnect or a dispose replaced the network session meanwhile: wait out the rest.
            }
        }

        var left = max - time.GetElapsedTime(started);
        if (left > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(left, time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        return false;
    }

    /// <summary>
    /// After a failure (a stopped server, a timeout, a boot), starts again with a new network session for the same
    /// account: every pending action is cancelled, the old network session is disposed (a Disconnect when it was still
    /// connected), the state is reset to <see cref="SessionPhase.Idle"/> with the bus and its subscribers kept, and a new
    /// idle network session takes its place; the host then calls <see cref="IActionApi.Login"/> (owner decision D43: the
    /// disconnected screen's button reconnects). The reset is recorded (local notice 7), so a replay resets too. An offline
    /// session resets and runs its stand-in again.
    /// </summary>
    public void Reconnect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _thread.Check("GameSession.Reconnect");
        Reconnects++;
        _actions.Reset();
        if (_live is null)
        {
            _reducer.Reset();
            RunStandIn();
            return;
        }

        var old = _net;
        _net = null;
        DisposeNetwork(old, quit: false);
        _reducer.ApplyLocal(new RecordingNotice(SessionNotices.SessionReset, 0, null));
        _net = NewNetworkSession();
        _log.Info(SessionLog.Category, "reconnect: a new network session is ready to log in", new LogField("reconnects", Reconnects));
    }

    /// <summary>
    /// Ends the session: cancels pending handles, quits the network session if it is still open (F653 in the world, then
    /// a Disconnect; without waiting), completes the recording with the final state digest
    /// (<see cref="GameStateDigest"/>) and flushes it, and zeroes the credentials the session held. Safe to call more than
    /// once. Call it on the game thread; from another thread the network session is disposed without the quit.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The SpellTable's worker reads the dats: the host disposes the dats after the session, so the read ends first (it takes milliseconds; bounded, never a hang).
        _spellHost?.Loaded.Wait(TimeSpan.FromSeconds(10));
        _actions.Dispose();
        var net = _net;
        _net = null;
        DisposeNetwork(net, quit: true);
        _reducer.StandIn?.Clear();
        if (_recorder is { } recorder)
        {
            Span<byte> digest = stackalloc byte[GameStateDigest.Size];
            GameStateDigest.Compute(State, digest);
            recorder.Complete(digest);
            recorder.Dispose();
            _log.Info(
                SessionLog.Category,
                "session recording complete",
                new LogField("frames", recorder.Frames),
                new LogField("omittedBodies", recorder.OmittedBodies),
                new LogField("digest", GameStateDigest.ToHex(digest)));
        }

        _live?.Credentials.Dispose();
    }

    private IAcSession NewNetworkSession()
    {
        var net = _live!.Network(_reducer, _tap!);
        return net ?? throw new InvalidOperationException("TODO(state): the network session factory returned null");
    }

    private void DisposeNetwork(IAcSession? net, bool quit)
    {
        if (net is null)
        {
            return;
        }

        if (quit && (!_thread.IsBound || _thread.ThreadId == Environment.CurrentManagedThreadId))
        {
            try
            {
                net.Quit();
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                _log.Warn(SessionLog.Category, "the network session could not quit cleanly: " + e.Message);
            }
        }

        try
        {
            net.Dispose();
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or IOException)
        {
            _log.Warn(SessionLog.Category, "the network session could not be disposed cleanly: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>Starts a login on <paramref name="network"/> with the session's credentials (the Action API's login).</summary>
    private void LogIn(IAcSession network)
    {
        var credentials = _live?.Credentials ?? throw new InvalidOperationException("TODO(state): an offline session cannot log in");
        ObjectDisposedException.ThrowIf(credentials.IsDisposed, credentials);
        network.Login(credentials);
    }

    private void RunStandIn()
    {
        try
        {
            _standIn!(_reducer);
            if (_reducer.LocalResponder?.World is { } world)
            {
                world.Log = _log; // the stand-in logs the placeholders it answers with
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error(SessionLog.Category, $"the offline session's stand-in data failed: {e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// The session's raw tap (net's <see cref="IRawMessageTap"/>): every game message in either direction goes to the
    /// recorder and to the bus's raw subscribers, with net's timestamp converted to session-clock seconds. With neither
    /// it returns at once. The LoginRequest is a packet-level section and never reaches it.
    /// </summary>
    private sealed class Tap(GameSession session) : IRawMessageTap
    {
        private readonly HashSet<ulong> _slowReported = [];

        /// <summary>
        /// A slow message names itself, once per message type (DoD 15, <c>TODO(login-burst-frame)</c>): its opcode and event type, the whole dispatch, the part before the reducer's first call
        /// (decode, as <c>decode_ms</c>), the reducer's own and its subscribers' part, and each event subscriber that took 1 ms or more.
        /// </summary>
        public void OnDispatched(uint opcode, uint eventType, double totalMilliseconds, double decodeMilliseconds)
        {
            if (!_slowReported.Add(((ulong)opcode << 32) | eventType))
            {
                return;
            }

            session._log.Warn(
                SessionLog.Category,
                "a server message took long on the game thread (TODO(login-burst-frame)): logged once per message type",
                new LogField("opcode", $"0x{opcode:X4}"),
                new LogField("event", eventType == 0 ? "-" : $"0x{eventType:X4}"),
                new LogField("total_ms", totalMilliseconds),
                new LogField("decode_ms", decodeMilliseconds),
                new LogField("reduce_and_subscribers_ms", totalMilliseconds - decodeMilliseconds),
                new LogField("subscribers_over_1ms", session._bus.DescribeSlowHandlers()));
        }

        public void OnMessage(MessageDirection direction, long timestamp, uint fragmentSequence, MessageQueue queue, ReadOnlySpan<byte> body)
        {
            var recorder = session._recorder;
            var bus = session._bus;
            if (direction == MessageDirection.Inbound)
            {
                bus.ResetSlowHandlers();
            }

            var recording = recorder is { IsCompleted: false };
            if (!recording && !bus.HasRawSubscribers)
            {
                return;
            }

            var time = session.Clock.ToSeconds(timestamp);
            if (recording)
            {
                recorder!.Message(direction, time, fragmentSequence, queue, body);
            }

            bus.PublishRaw(direction, time, fragmentSequence, queue, body);
        }
    }
}
