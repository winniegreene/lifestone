using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Sockets;
using Lifestone.Net.Taps;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// The <see cref="IAcSession"/> implementation behind <see cref="AcSession.Create"/>: the state machine of
/// docs/protocol.md §4, written for Lifestone (OpenAC's <c>WorldSession</c> was read for its flow only, m2-plan §3.3 N5).
/// </summary>
/// <remarks>
/// <para>The file is split by concern: this part holds the lifetime, the pump loop, the session events and the failure
/// path; <c>SessionMachine.Receive.cs</c> the datagram path and the inbound order; <c>SessionMachine.Handshake.cs</c>
/// the login attempts, the handshake, the retries and the watchdog; <c>SessionMachine.Inbound.cs</c> the messages the
/// session reacts to (the data check, the character list, errors, boots); <c>SessionMachine.Characters.cs</c>,
/// <c>SessionMachine.World.cs</c> and <c>SessionMachine.Logoff.cs</c> the character operations, entering the world and
/// leaving it (N6).</para>
/// <para><b>Events.</b> Every session event is queued and delivered from <see cref="Pump"/>, after the message that
/// caused it has reached the sink; an event caused by a call outside the pump (the phase change of
/// <see cref="Login"/>, <see cref="EnterWorld"/>, <see cref="LogOff"/> or <see cref="Quit"/>) is delivered at the
/// start of the next pump. So the sink is never called re-entrantly from one of its own calls into the session.</para>
/// </remarks>
internal sealed partial class SessionMachine : IAcSession
{
    /// <summary>The receive buffer: ACE's datagrams are at most 484 bytes (docs/protocol.md §1).</summary>
    private const int ReceiveBufferSize = 2048;

    private readonly AcSessionOptions _options;
    private readonly IAcSessionSink _sink;
    private readonly IRawMessageTap? _tap;
    private readonly INetLog _log;
    private readonly TimeProvider _time;
    private readonly Func<System.Net.IPEndPoint, IDatagramSocket> _socketFactory;
    private readonly SessionInbound _inbound;
    private readonly Queue<SessionEvent> _events = new();
    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
    private readonly PacketWriter _writer = new(512);
    private int _ownerThreadId;
    private bool _disposed;
    private bool _inPump;
    private bool _flushing;
    private bool _writerBusy;
    private volatile SessionConnection? _connection;
    private volatile int _readyHint;
    private LinkTotals _retired;
    private long _packetsReceived;

    // The pump's budget (m2-plan §3.3 N5): set at the start of each pump.
    private long _pumpStart;
    private TimeSpan _pumpBudget;
    private int _pumpDispatched;
    private long _sinkStamp;
    private bool _budgetExhausted;

    public SessionMachine(AcSessionOptions options, IAcSessionSink sink, IRawMessageTap? tap)
    {
        _options = options;
        _sink = sink;
        _tap = tap;
        _log = options.Log ?? NullNetLog.Instance;
        _time = options.Time;
        _socketFactory = options.SocketFactory ?? (server => new UdpLoopbackSocket(server, _time, _log));
        _inbound = new SessionInbound(this, sink);
    }

    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    public SessionFailure? Failure { get; private set; }

    public CharacterListMsg? CharacterList { get; private set; }

    public uint? PlayerGuid { get; private set; }

    public LinkStats Link
    {
        get
        {
            var totals = _retired;
            var loss = 0d;
            if (_connection is { } connection)
            {
                totals = totals.Plus(connection.Totals());
                loss = connection.Transport?.PacketLossPercentage ?? 0d;
            }

            return new LinkStats(
                totals.PacketsSent,
                _packetsReceived,
                totals.Resends,
                totals.NaksSent,
                totals.NaksReceived,
                totals.DuplicatesDropped,
                totals.ChecksumFailures,
                totals.GapSkips,
                totals.TailProbes,
                totals.DatagramsFiltered,
                loss,
                Volatile.Read(ref _lastArrival));
        }
    }

    /// <summary>The options the session was created with, the server endpoint being the loopback policy's copy.</summary>
    internal AcSessionOptions Options => _options;

    /// <summary>The sink the session reports to.</summary>
    internal IAcSessionSink Sink => _sink;

    /// <summary>The raw tap, if any.</summary>
    internal IRawMessageTap? Tap => _tap;

    /// <summary>The current login attempt's connection, for tests; null while none is open.</summary>
    internal SessionConnection? Connection => _connection;

    /// <summary>True while the session holds its copy of the password (until character select, a failure or disposal).</summary>
    internal bool HoldsPassword => _password is not null;

    public PumpResult Pump(TimeSpan budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero);
        EnterGameThread(capture: true);
        if (_inPump)
        {
            throw new InvalidOperationException("Pump is not re-entrant: it was called from one of its own callbacks.");
        }

        _inPump = true;
        try
        {
            return PumpCore(budget);
        }
        finally
        {
            _inPump = false;
            _readyHint = _connection?.Sequencer?.ReadyCount ?? 0;
        }
    }

    public async ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (max < TimeSpan.Zero && max != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "must be non-negative or Timeout.InfiniteTimeSpan");
        }

        var infinite = max == Timeout.InfiniteTimeSpan;
        var start = _time.GetTimestamp();
        while (true)
        {
            if (_readyHint > 0)
            {
                return true;
            }

            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (_connection is { } connection)
            {
                bool arrived;
                try
                {
                    arrived = await connection.Socket.WaitForDatagramAsync(Left(max, start), cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    arrived = false;
                }

                if (arrived)
                {
                    return true;
                }

                if (!ReferenceEquals(_connection, connection))
                {
                    continue; // the game thread closed or replaced the connection meanwhile: look again
                }

                // The same connection and no datagram: max ran out, the wait was cancelled, or the socket can deliver
                // nothing more (its receive loop ended after a fault; the silence watchdog fails the session on a
                // later pump). Wait out the rest of max, so a host that loops on Pump and this never spins.
                if (infinite)
                {
                    return false; // a wait for a socket that cannot deliver would never end
                }

                await SleepAsync(Left(max, start), cancellationToken).ConfigureAwait(false);
                return false;
            }

            var due = Volatile.Read(ref _retryDueShadow);
            if (due != long.MinValue)
            {
                // Waiting to retry: sleep until the retry is due (or max), so the host's next pump starts the attempt.
                var untilDue = _time.GetElapsedTime(_time.GetTimestamp(), due);
                await SleepAsync(infinite || untilDue < Left(max, start) ? untilDue : Left(max, start), cancellationToken).ConfigureAwait(false);
                return false;
            }

            // No connection and no retry pending (idle, closed or failed): nothing can arrive until the game thread
            // calls Login. Wait out max all the same; an infinite wait would never end, so it returns at once.
            if (!infinite)
            {
                await SleepAsync(Left(max, start), cancellationToken).ConfigureAwait(false);
            }

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connection is { } connection && Phase is not (SessionPhase.Closed or SessionPhase.Failed))
        {
            // Best effort, so ACE frees the account at once instead of after its 60 s timeout.
            connection.SendDisconnect();
        }

        CloseConnection();
        ZeroPassword();
        RetryDueAt = null;
        if (Phase is not SessionPhase.Failed)
        {
            Phase = SessionPhase.Closed;
        }

        _events.Clear();
    }

    /// <summary>What is left of a wait of <paramref name="max"/> that started at <paramref name="start"/> (never negative).</summary>
    private TimeSpan Left(TimeSpan max, long start)
    {
        if (max == Timeout.InfiniteTimeSpan)
        {
            return max;
        }

        var left = max - _time.GetElapsedTime(start);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Sleeps for <paramref name="wait"/> on the session's clock, or until cancelled; nothing when it is not positive.</summary>
    private async ValueTask SleepAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private PumpResult PumpCore(TimeSpan budget)
    {
        FlushEvents();
        if (Phase is SessionPhase.Idle or SessionPhase.Closed or SessionPhase.Failed)
        {
            return default;
        }

        _pumpStart = _time.GetTimestamp();
        _pumpBudget = budget;
        _pumpDispatched = 0;
        _budgetExhausted = false;
        var previousPump = _previousPumpAt;
        _previousPumpAt = _pumpStart;

        if (_retryDueAt is { } due && _pumpStart >= due)
        {
            StartAttempt();
        }

        if (_connection is { } connection)
        {
            DrainReady(connection);
        }

        var datagrams = 0;
        while (_connection is { } current && current.Socket.TryReceive(_receiveBuffer, out var length, out var sourcePort))
        {
            datagrams++;
            ProcessDatagram(current, length, sourcePort);
        }

        if (_connection is { } afterReceive)
        {
            SkipGapIfDue(afterReceive);
            if (ReferenceEquals(_connection, afterReceive))
            {
                RunHandshakeTimers(afterReceive);
            }
        }

        if (_connection is { Transport: { } transport })
        {
            // Acks, NAKs, resends and the loss average run every pump, whatever the budget (m2-plan §3.3 N5).
            transport.Sweep();
        }

        RunWorldTimers();
        RunTimeouts(previousPump);
        FlushEvents();

        var pending = _connection?.Sequencer?.ReadyCount ?? 0;
        return new PumpResult(datagrams, _pumpDispatched, pending, _budgetExhausted && pending > 0);
    }

    /// <summary>
    /// True while the pump may dispatch another message. The budget applies only from <see cref="SessionPhase.EnteringWorld"/>
    /// on (before that every message is dispatched); one message is always dispatched per pump, so a tiny budget still
    /// makes progress.
    /// </summary>
    private bool CanDispatch()
    {
        if (Phase is not (SessionPhase.EnteringWorld or SessionPhase.InWorld or SessionPhase.LoggingOff) || _pumpDispatched == 0)
        {
            return true;
        }

        if (_time.GetElapsedTime(_pumpStart) < _pumpBudget)
        {
            return true;
        }

        _budgetExhausted = true;
        return false;
    }

    /// <summary>A message whose dispatch takes this long (about 5 ms) is reported to the tap (<see cref="IRawMessageTap.OnDispatched"/>).</summary>
    private long SlowMessageTicks => _time.TimestampFrequency * 5 / 1000;

    /// <summary>The time of the sink's first call in the message being dispatched (0 before it).</summary>
    internal long SinkStamp => _sinkStamp;

    private void StampSink()
    {
        if (_sinkStamp == 0)
        {
            _sinkStamp = _time.GetTimestamp();
        }
    }

    /// <summary>Taps and dispatches one message to the sink through the session's own handler, then delivers the events it raised.</summary>
    private void Dispatch(in InboundContext context, ReadOnlySpan<byte> body)
    {
        FlushEvents();
        _tap?.OnMessage(MessageDirection.Inbound, context.ReceivedTimestamp, context.FragmentSequence, context.Queue, body);
        _pumpDispatched++;
        _sinkStamp = 0;
        var dispatchStart = _time.GetTimestamp();
        InboundDispatcher.Dispatch(context, body, _inbound);
        var dispatchEnd = _time.GetTimestamp();
        if (_tap is not null && dispatchEnd - dispatchStart >= SlowMessageTicks)
        {
            // A slow message names itself (DoD 15, TODO(login-burst-frame)): its opcode and event type, the whole dispatch, and the part up to the sink's first call (decode).
            var sinkAt = _sinkStamp == 0 ? dispatchEnd : _sinkStamp;
            var eventType = body.Length >= GameEventEnvelope.HeaderSize && GameEventEnvelope.TryReadHeader(body, out var header) ? header.EventType : 0u;
            _tap.OnDispatched(
                body.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body) : 0u,
                eventType,
                _time.GetElapsedTime(dispatchStart, dispatchEnd).TotalMilliseconds,
                _time.GetElapsedTime(dispatchStart, sinkAt).TotalMilliseconds);
        }

        FlushEvents();
    }

    // ---- Sends ----

    /// <summary>
    /// A writer for one outbound message: the session's own, emptied, or a fresh one while the session's own is being
    /// sent (a tap that sends from inside a send must not overwrite the body it is reading).
    /// </summary>
    private PacketWriter BeginMessage()
    {
        if (_writerBusy)
        {
            return new PacketWriter();
        }

        _writer.Reset();
        return _writer;
    }

    /// <summary>
    /// Seals and sends the message in <paramref name="writer"/> on the current connection's transport (store, then
    /// send), taps it with the fragment sequence it got, and returns that as the ticket.
    /// </summary>
    private OutboundTicket SendMessage(PacketWriter writer, MessageQueue queue)
    {
        var own = ReferenceEquals(writer, _writer);
        _writerBusy |= own;
        try
        {
            var transport = _connection?.Transport ?? throw new InvalidOperationException("no connection to send on");
            var sequence = transport.Outbound.FragmentSequence;
            var timestamp = _time.GetTimestamp();
            var body = writer.AsSpan();
            transport.Outbound.SendGameMessage(body, queue);
            _tap?.OnMessage(MessageDirection.Outbound, timestamp, sequence, queue, body);
            return new OutboundTicket(sequence, timestamp);
        }
        finally
        {
            if (own)
            {
                _writerBusy = false;
            }
        }
    }

    // ---- Phases, events, failure ----

    /// <summary>Moves to <paramref name="next"/> and queues a <see cref="SessionEventKind.PhaseChanged"/> event.</summary>
    private void SetPhase(SessionPhase next)
    {
        var previous = Phase;
        if (previous == next)
        {
            return;
        }

        Phase = next;
        Raise(SessionEvent.PhaseChanged(_time.GetTimestamp(), previous, next));
        if (_log.IsEnabled(NetLogLevel.Debug))
        {
            _log.Log(NetLogLevel.Debug, $"phase: {previous} -> {next}");
        }
    }

    /// <summary>Queues an event; the pump delivers it (see the remarks).</summary>
    private void Raise(in SessionEvent sessionEvent) => _events.Enqueue(sessionEvent);

    private void FlushEvents()
    {
        if (_flushing || !_inPump)
        {
            return;
        }

        _flushing = true;
        try
        {
            while (_events.TryDequeue(out var sessionEvent))
            {
                _sink.OnSessionEvent(sessionEvent);
            }
        }
        finally
        {
            _flushing = false;
        }
    }

    /// <summary>
    /// Fails the session: records why, closes the connection (with a best-effort Disconnect, so ACE frees the account
    /// at once), zeroes the password and queues the phase change and the <see cref="SessionEventKind.Failed"/> event.
    /// A session already closed or failed stays as it is.
    /// </summary>
    private void Fail(SessionFailureKind kind, string message, uint code = 0)
    {
        if (Phase is SessionPhase.Closed or SessionPhase.Failed)
        {
            return;
        }

        var failure = new SessionFailure(kind, Phase, message, code);
        Failure = failure;
        _connection?.SendDisconnect();
        CloseConnection();
        ZeroPassword();
        RetryDueAt = null;
        ResetCharacterState();
        PlayerGuid = null;
        SetPhase(SessionPhase.Failed);
        Raise(SessionEvent.Failed(_time.GetTimestamp(), failure));
        if (_log.IsEnabled(NetLogLevel.Error))
        {
            _log.Log(NetLogLevel.Error, $"session failed in {failure.Phase}: {kind}{(code != 0 ? $" (code 0x{code:X2})" : string.Empty)}: {message}");
        }
    }

    /// <summary>Closes the current connection, folding its counters into the session's.</summary>
    private void CloseConnection()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        _retired = _retired.Plus(connection.Totals());
        _connection = null;
        connection.Dispose();
    }

    private void ZeroPassword()
    {
        if (_password is { } password)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            _password = null;
        }
    }

    private void RequirePhase(SessionPhase phase, string operation)
    {
        if (Phase != phase)
        {
            throw new InvalidOperationException($"{operation} needs a session in {phase}; this one is {Phase}.");
        }
    }

    /// <summary>
    /// Checks the caller's thread: the first <see cref="Pump"/> binds the session to its thread (when
    /// <paramref name="capture"/>), and every later call from another thread is refused. Also refuses calls after
    /// disposal.
    /// </summary>
    private void EnterGameThread(bool capture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = Environment.CurrentManagedThreadId;
        if (_ownerThreadId == 0)
        {
            if (capture)
            {
                _ownerThreadId = current;
            }

            return;
        }

        if (_ownerThreadId != current)
        {
            throw new InvalidOperationException(
                $"This session belongs to managed thread {_ownerThreadId} (the thread that first pumped it); it was called from thread {current}.");
        }
    }

    /// <summary>Logs at <paramref name="level"/> when enabled.</summary>
    private void Log(NetLogLevel level, string message)
    {
        if (_log.IsEnabled(level))
        {
            _log.Log(level, message);
        }
    }
}
