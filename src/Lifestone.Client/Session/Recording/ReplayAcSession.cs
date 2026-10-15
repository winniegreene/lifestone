using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Taps;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>One frame of a recording held in memory for <see cref="ReplayAcSession"/> (bodies copied).</summary>
/// <param name="Kind">The frame's kind.</param>
/// <param name="Time">Its recorded session-clock time.</param>
/// <param name="Direction">A message's direction.</param>
/// <param name="Queue">A message's queue.</param>
/// <param name="FragmentSequence">A message's fragment sequence.</param>
/// <param name="Opcode">A message's opcode.</param>
/// <param name="ActionType">For an F7B1 GameAction with its body, the action type (offset 8); else 0.</param>
/// <param name="Body">A message's body (empty when it was omitted).</param>
/// <param name="SessionEvent">A session event.</param>
/// <param name="Notice">A local notice.</param>
internal readonly record struct ReplayFrame(
    RecordingFrameKind Kind,
    double Time,
    MessageDirection Direction,
    MessageQueue Queue,
    uint FragmentSequence,
    uint Opcode,
    uint ActionType,
    byte[] Body,
    SessionEvent SessionEvent,
    RecordingNotice Notice)
{
    /// <summary>Every frame of <paramref name="recording"/>, in order, with copied bodies.</summary>
    internal static List<ReplayFrame> Load(Stream recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        using var reader = SessionRecordingReader.Open(recording, leaveOpen: true);
        var frames = new List<ReplayFrame>();
        while (reader.TryRead(out var frame))
        {
            var m = frame.Message;
            var body = frame.Kind == RecordingFrameKind.Message ? m.Body.ToArray() : [];
            var action = frame.Kind == RecordingFrameKind.Message && m.Opcode == MessageOpcodes.GameAction && body.Length >= 12
                ? BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8))
                : 0u;
            frames.Add(new ReplayFrame(frame.Kind, frame.Time, m.Direction, m.Queue, m.FragmentSequence, m.Opcode, action, body, frame.SessionEvent, frame.Notice));
        }

        return frames;
    }
}

/// <summary>
/// A network session that plays a recording back into a whole <see cref="GameSession"/> (step ST14): the session's Action
/// API, handles, tools and reducer run as live, against the recorded server traffic, so handle behaviour is tested on real
/// traffic without a server.
/// </summary>
/// <remarks>
/// <para><b>Following the client.</b> The recorded frames are delivered in order, each when the session clock reaches its
/// recorded time plus the replay's lag. Delivery stops at the client's own steps until the session takes them again: the
/// first connect waits for <see cref="Login"/>, a recorded close waits for <see cref="Quit"/>, and each recorded client
/// message the client chose to send (character create, delete and restore, entering the world, LoginComplete, use,
/// query health, talk, tell, logoff) waits for the same send (same opcode and, for a GameAction, the same action type).
/// The lag grows by however much later the client acted than it did in the recording. Client messages net sends by
/// itself (the data check's answer F7E6, F657 after F7DF) and movement (physics' cadence) are passed over.</para>
/// <para><b>Divergence.</b> A send that does not match the next recorded client step is counted in
/// <see cref="Divergences"/> with a description (and logged as a WARNING), and the replay goes on.</para>
/// <para>Local notices in the recording are skipped: the replayed session's own Action API makes them again. Session
/// events are stamped with the replay's time. Single-threaded, like the session.</para>
/// </remarks>
internal sealed class ReplayAcSession : IAcSession
{
    private readonly IReadOnlyList<ReplayFrame> _frames;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly IAcSessionSink _sink;
    private readonly IRawMessageTap? _tap;
    private readonly ILogSink _log;
    private readonly Queue<(uint Opcode, uint Action, double At)> _sent = new();
    private readonly Capture _capture = new();
    private readonly List<string> _divergences = [];
    private int _next;
    private int _logins;
    private int _quits;
    private double _lag;
    private uint _sequence;
    private bool _disposed;

    /// <summary>A replay of <paramref name="frames"/> for a session's reducer and tap.</summary>
    /// <param name="frames">The recording's frames (<see cref="ReplayFrame.Load"/>).</param>
    /// <param name="time">The session's time source; the replay's time 0 is now.</param>
    /// <param name="sink">The session's reducer.</param>
    /// <param name="tap">The session's raw tap, or null.</param>
    /// <param name="log">Where divergences are logged.</param>
    internal ReplayAcSession(IReadOnlyList<ReplayFrame> frames, TimeProvider time, IAcSessionSink sink, IRawMessageTap? tap, ILogSink log)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _tap = tap;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _start = time.GetTimestamp();
    }

    /// <inheritdoc/>
    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

    /// <inheritdoc/>
    public SessionFailure? Failure { get; private set; }

    /// <inheritdoc/>
    public CharacterListMsg? CharacterList => _capture.List;

    /// <inheritdoc/>
    public uint? PlayerGuid => _capture.Player;

    /// <inheritdoc/>
    public LinkStats Link => default;

    /// <summary>Frames delivered or passed over so far.</summary>
    internal int Position => _next;

    /// <summary>True once every frame was delivered or passed over.</summary>
    internal bool IsComplete => _next >= _frames.Count;

    /// <summary>How the client's sends departed from the recording, in order.</summary>
    internal IReadOnlyList<string> Divergences => _divergences;

    /// <summary>The replay's lag: how much later than recorded the frames now arrive, in seconds.</summary>
    internal double Lag => _lag;

    /// <summary>
    /// A live <see cref="GameSession"/> over a replay of <paramref name="frames"/> (no recording; the stand-in credentials
    /// never leave the process). <paramref name="replay"/> is the network session, for the tests.
    /// </summary>
    internal static GameSession CreateSession(IReadOnlyList<ReplayFrame> frames, TimeProvider time, ILogSink log, out ReplayAcSession replay)
    {
        ReplayAcSession? created = null;
        var session = GameSession.CreateLive(
            new LiveSessionSetup(
                new IPEndPoint(IPAddress.Loopback, SessionOptions.DefaultPort),
                new AcCredentials("replay", "-"),
                (sink, tap) => created = new ReplayAcSession(frames, time, sink, tap, log),
                TimeSpan.FromMilliseconds(SessionOptions.MaxBudgetMs),
                Recording: null,
                RecordingHeader: null),
            time,
            log);
        replay = created!;
        return session;
    }

    /// <inheritdoc/>
    public void Login(AcCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _logins++;
    }

    /// <inheritdoc/>
    public PumpResult Pump(TimeSpan budget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var now = _time.GetElapsedTime(_start).TotalSeconds;
        var messages = 0;
        while (_next < _frames.Count)
        {
            var frame = _frames[_next];
            if (frame.Time + _lag > now)
            {
                break;
            }

            if (!Deliver(frame, now, ref messages))
            {
                break;
            }

            _next++;
        }

        return new PumpResult(0, messages, 0, false);
    }

    /// <inheritdoc/>
    public ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_next < _frames.Count && _frames[_next].Time + _lag <= _time.GetElapsedTime(_start).TotalSeconds);

    /// <inheritdoc/>
    public OutboundTicket CreateCharacter(in CharacterCreateRequest request)
    {
        request.Validate();
        return Sent(MessageOpcodes.CharacterCreate, 0);
    }

    /// <inheritdoc/>
    public OutboundTicket DeleteCharacter(int listIndex) => Sent(MessageOpcodes.CharacterDelete, 0);

    /// <inheritdoc/>
    public OutboundTicket RestoreCharacter(uint guid) => Sent(MessageOpcodes.CharacterRestore, 0);

    /// <inheritdoc/>
    public OutboundTicket EnterWorld(uint guid) => Sent(MessageOpcodes.CharacterEnterWorldRequest, 0);

    /// <inheritdoc/>
    public OutboundTicket SendLoginComplete() => Sent(MessageOpcodes.GameAction, GameActionTypes.LoginComplete);

    /// <inheritdoc/>
    public OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump) =>
        Ticket();

    /// <inheritdoc/>
    public OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact) => Ticket();

    /// <inheritdoc/>
    public OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences) => Ticket();

    /// <inheritdoc/>
    public OutboundTicket SendUse(uint guid) => Sent(MessageOpcodes.GameAction, GameActionTypes.Use);

    /// <inheritdoc/>
    public OutboundTicket SendQueryHealth(uint guid) => Sent(MessageOpcodes.GameAction, GameActionTypes.QueryHealth);

    /// <inheritdoc/>
    public OutboundTicket SendTalk(ReadOnlySpan<char> text) => Sent(MessageOpcodes.GameAction, GameActionTypes.Talk);

    /// <inheritdoc/>
    public OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text) => Sent(MessageOpcodes.GameAction, GameActionTypes.Tell);

    /// <inheritdoc/>
    public void LogOff() => Sent(MessageOpcodes.CharacterLogOff, 0);

    /// <inheritdoc/>
    public void Quit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _quits++;
        if (Phase == SessionPhase.InWorld)
        {
            Sent(MessageOpcodes.CharacterLogOff, 0);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _disposed = true;

    /// <summary>True for a client message net sends by itself, or one physics' cadence decides (never waited for).</summary>
    private static bool IsAutomatic(in ReplayFrame frame) =>
        frame.Opcode is MessageOpcodes.DddInterrogationResponse or MessageOpcodes.CharacterEnterWorld
        || (frame.Opcode == MessageOpcodes.GameAction
            && frame.ActionType is GameActionTypes.MoveToState or GameActionTypes.AutonomousPosition or GameActionTypes.Jump or 0);

    private static string Describe(uint opcode, uint action) =>
        opcode == MessageOpcodes.GameAction ? $"F7B1 action 0x{action:X4}" : $"0x{opcode:X4}";

    /// <summary>Delivers one frame, or returns false when it must wait for the client.</summary>
    private bool Deliver(in ReplayFrame frame, double now, ref int messages)
    {
        switch (frame.Kind)
        {
            case RecordingFrameKind.Message when frame.Direction == MessageDirection.Inbound:
                var stamp = _time.GetTimestamp();
                _tap?.OnMessage(MessageDirection.Inbound, stamp, frame.FragmentSequence, frame.Queue, frame.Body);
                var context = new InboundContext(frame.FragmentSequence, frame.Queue, stamp);
                if (frame.Opcode is MessageOpcodes.CharacterList or MessageOpcodes.PlayerCreate)
                {
                    InboundDispatcher.Dispatch(context, frame.Body, _capture);
                }

                InboundDispatcher.Dispatch(context, frame.Body, _sink);
                messages++;
                return true;

            case RecordingFrameKind.Message:
                return IsAutomatic(frame) || TakeSend(frame, now);

            case RecordingFrameKind.SessionEvent:
                var e = frame.SessionEvent;
                if (e.Kind == SessionEventKind.PhaseChanged)
                {
                    if (e.Phase == SessionPhase.Connecting && e.PreviousPhase is SessionPhase.Idle or SessionPhase.Closed or SessionPhase.Failed)
                    {
                        if (_logins == 0)
                        {
                            return false;
                        }

                        _logins--;
                        _lag = Math.Max(_lag, now - frame.Time);
                    }
                    else if (e.Phase == SessionPhase.Closing)
                    {
                        if (_quits == 0)
                        {
                            return false;
                        }

                        _quits--;
                        _lag = Math.Max(_lag, now - frame.Time);
                    }

                    Phase = e.Phase;
                }

                if (e.Kind == SessionEventKind.Failed)
                {
                    Phase = SessionPhase.Failed;
                    Failure = e.Failure;
                }

                _sink.OnSessionEvent(e with { Timestamp = _time.GetTimestamp() });
                return true;

            default:
                // Local notices: the replayed session's Action API makes them again.
                return true;
        }
    }

    /// <summary>
    /// The recorded client message <paramref name="frame"/> waits for the client's matching send: true when it was made
    /// (the lag grows by how late it came), false while none is pending. A send that does not match is a divergence.
    /// </summary>
    private bool TakeSend(in ReplayFrame frame, double now)
    {
        while (_sent.Count > 0)
        {
            var (opcode, action, at) = _sent.Dequeue();
            if (opcode == frame.Opcode && (opcode != MessageOpcodes.GameAction || action == frame.ActionType))
            {
                _lag = Math.Max(_lag, at - frame.Time);
                return true;
            }

            var divergence = $"the client sent {Describe(opcode, action)} where the recording's next client message is {Describe(frame.Opcode, frame.ActionType)} (frame {_next})";
            _divergences.Add(divergence);
            _log.Warn(SessionLog.Category, "replay divergence: " + divergence);
        }

        return false;
    }

    private OutboundTicket Sent(uint opcode, uint action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sent.Enqueue((opcode, action, _time.GetElapsedTime(_start).TotalSeconds));
        return Ticket();
    }

    private OutboundTicket Ticket()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new OutboundTicket(++_sequence, 0);
    }

    /// <summary>Keeps what the replay needs to answer <see cref="CharacterList"/> and <see cref="PlayerGuid"/>.</summary>
    private sealed class Capture : IInboundMessageHandler
    {
        public CharacterListMsg? List { get; private set; }

        public uint? Player { get; private set; }

        public void OnCharacterList(in InboundContext context, CharacterListMsg message) => List = message;

        public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message) => Player = message.Guid;
    }
}
