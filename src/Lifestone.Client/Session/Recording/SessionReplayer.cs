using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Recording;

/// <summary>
/// Replays a <c>.lsrec</c> recording into a fresh reducer (step ST14; docs/session.md §18): the same decode path as live
/// play (net's <see cref="InboundDispatcher"/>), the same reducer, the same events, so a recorded session can be studied,
/// tested and checked offline. The final state's digest must equal the one the recorder put in the trailer.
/// </summary>
/// <remarks>
/// <para><b>Frame by frame</b>, in recorded order: the replay's clock moves to the frame's time (so every time the reducer
/// stores is the recorded one), parked messages that waited too long expire as in a live tick, then:</para>
/// <list type="bullet">
/// <item>a server message goes to the raw subscribers, then through <see cref="InboundDispatcher.Dispatch"/> into the
/// reducer (its receive time is the frame's);</item>
/// <item>a client message goes to the raw subscribers and to <see cref="OutboundCheck"/> (a test decoder), and changes
/// nothing;</item>
/// <item>a network-session event is applied again, stamped with the frame's time;</item>
/// <item>a local notice is applied again: the reducer's codes through the reducer, the Action API's by publishing what it
/// published live (<see cref="LogOffRequested"/>, stamped with the frame's time).</item>
/// </list>
/// <para>No action is issued and nothing is sent: the replayer has no Action API and no network session. A notice code
/// nobody knows, a frame of an unknown kind or a server message recorded without its body fails loudly
/// (<see cref="InvalidDataException"/>). A recording cut short replays to its last whole frame
/// (<see cref="ReplayResult.End"/>).</para>
/// <para>One replay per instance; subscribe to <see cref="Events"/> before <see cref="Replay"/>. Not thread-safe.</para>
/// </remarks>
public sealed class SessionReplayer
{
    private readonly ReplayClock _time = new();
    private readonly EventBus _bus;
    private readonly GameStateReducer _reducer;
    private readonly List<uint> _malformed = [];
    private bool _used;

    /// <summary>A replayer with a fresh state.</summary>
    /// <param name="log">Where the reducer logs (category <c>session</c>), or null for nowhere.</param>
    public SessionReplayer(ILogSink? log = null)
    {
        Log = log ?? NullLogSink.Instance;
        Clock = new SessionClock(_time);
        State = new GameState();
        _bus = new EventBus(Log);
        _reducer = new GameStateReducer(State, _bus, Clock, Log);
    }

    /// <summary>The replayed state.</summary>
    public GameState State { get; }

    /// <summary>The events the reducer publishes during the replay, and the raw tap (every recorded message).</summary>
    public IEventBus Events => _bus;

    /// <summary>The replay's clock (at the last frame's time after <see cref="Replay"/>).</summary>
    public SessionClock Clock { get; }

    /// <summary>Where the reducer logs.</summary>
    public ILogSink Log { get; }

    /// <summary>Called with each client-to-server message (bodies off the allowlist are empty), or null.</summary>
    public Action<RecordedMessage>? OutboundCheck { get; set; }

    /// <summary>Replays <paramref name="recording"/> to its end and returns what happened.</summary>
    /// <param name="recording">The recording (read from its current position; left open).</param>
    /// <returns>The counts, the end, the trailer and the final digest.</returns>
    /// <exception cref="InvalidOperationException">This replayer has already replayed a recording.</exception>
    /// <exception cref="InvalidDataException">The recording is not a version-1 recording, or holds a frame a replay
    /// cannot apply.</exception>
    public ReplayResult Replay(Stream recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (_used)
        {
            throw new InvalidOperationException("a SessionReplayer replays one recording; create another for the next");
        }

        _used = true;
        using var reader = SessionRecordingReader.Open(recording, leaveOpen: true);
        var counts = default(ReplayCounts);
        while (reader.TryRead(out var frame))
        {
            _time.MoveTo(frame.Time);
            switch (frame.Kind)
            {
                case RecordingFrameKind.Message:
                    ApplyMessage(frame, ref counts);
                    break;
                case RecordingFrameKind.SessionEvent:
                    counts.SessionEvents++;
                    _reducer.OnSessionEvent(frame.SessionEvent with { Timestamp = _time.GetTimestamp() });
                    break;
                case RecordingFrameKind.Notice:
                    counts.Notices++;
                    ApplyNotice(frame.Notice, frame.Time);
                    break;
                default:
                    throw new InvalidDataException($"recording frame {reader.FramesRead}: kind {frame.Kind} cannot be replayed");
            }

            // After the frame, as the live session expires parked messages at the end of its tick (GameSession.Tick), after its pump has
            // applied what arrived: a message parked for 60 s whose object is created by the first message of a later tick is replayed
            // live, so it must be replayed here too (before the frame it would be dropped, which could change the digest; dod-1-2-12-13-6).
            // The recording keeps no tick boundaries, so a frame that arrives earlier in the same tick as the creating one can still
            // expire it here and not live: a window of less than one frame (TODO(replay-tick-boundary): record the tick ends to close it).
            _reducer.ExpireParked();
        }

        return new ReplayResult(
            reader.Header,
            reader.FramesRead,
            counts.Inbound,
            counts.Outbound,
            counts.SessionEvents,
            counts.Notices,
            counts.Decoded,
            counts.Unknown,
            counts.Malformed,
            [.. _malformed.Distinct()],
            reader.End,
            reader.Trailer,
            GameStateDigest.Compute(State),
            _bus.HandlerFailures);
    }

    private void ApplyMessage(in RecordingFrame frame, ref ReplayCounts counts)
    {
        var message = frame.Message;
        var body = message.Body.Span;
        if (message.Direction == MessageDirection.Outbound)
        {
            counts.Outbound++;
            _bus.PublishRaw(message.Direction, frame.Time, message.FragmentSequence, message.Queue, body);
            OutboundCheck?.Invoke(message);
            return;
        }

        counts.Inbound++;
        if (message.BodyOmitted)
        {
            throw new InvalidDataException(
                $"a server message (opcode 0x{message.Opcode:X4}) was recorded without its body: the recorder keeps every server message whole");
        }

        _bus.PublishRaw(message.Direction, frame.Time, message.FragmentSequence, message.Queue, body);
        var context = new InboundContext(message.FragmentSequence, message.Queue, _time.GetTimestamp());
        var result = InboundDispatcher.Dispatch(context, body, _reducer);
        switch (result.Outcome)
        {
            case DispatchOutcome.Decoded:
                counts.Decoded++;
                break;
            case DispatchOutcome.Unknown:
                counts.Unknown++;
                break;
            default:
                counts.Malformed++;
                _malformed.Add(result.Opcode);
                break;
        }
    }

    private void ApplyNotice(in RecordingNotice notice, double time)
    {
        if (notice.Code is > 0 and <= SessionNotices.LastReducerCode)
        {
            _reducer.ApplyLocal(notice);
        }
        else if (ActionNotices.Owns(notice.Code))
        {
            ActionNotices.Publish(_bus, notice, time);
        }
        else
        {
            throw new InvalidDataException(
                $"local notice code {notice.Code} is not one this build knows (reducer 1-{SessionNotices.LastReducerCode}, actions {ActionNotices.First}-{ActionNotices.Last})");
        }
    }

    private struct ReplayCounts
    {
        public long Inbound;
        public long Outbound;
        public long SessionEvents;
        public long Notices;
        public long Decoded;
        public long Unknown;
        public long Malformed;
    }
}

/// <summary>What a <see cref="SessionReplayer"/> replay did.</summary>
/// <param name="Header">The recording's header.</param>
/// <param name="Frames">Frames read (the trailer not counted).</param>
/// <param name="InboundMessages">Server messages replayed.</param>
/// <param name="OutboundMessages">Client messages seen (they change nothing).</param>
/// <param name="SessionEvents">Network-session events applied.</param>
/// <param name="Notices">Local notices applied.</param>
/// <param name="Decoded">Server messages net decoded.</param>
/// <param name="Unknown">Server messages net does not decode (<see cref="UnknownMessage"/>).</param>
/// <param name="Malformed">Server messages net could not parse (<see cref="MessageDropped"/> Malformed).</param>
/// <param name="MalformedOpcodes">The opcodes of the malformed messages, each once.</param>
/// <param name="End">How the recording ended.</param>
/// <param name="Trailer">The trailer, when the recording has one.</param>
/// <param name="StateDigest">The replayed state's digest (<see cref="GameStateDigest"/>).</param>
/// <param name="HandlerFailures">Event handlers that threw during the replay (logged by the bus).</param>
public sealed record ReplayResult(
    RecordingHeader Header,
    long Frames,
    long InboundMessages,
    long OutboundMessages,
    long SessionEvents,
    long Notices,
    long Decoded,
    long Unknown,
    long Malformed,
    IReadOnlyList<uint> MalformedOpcodes,
    RecordingEnd End,
    RecordingTrailer? Trailer,
    byte[] StateDigest,
    long HandlerFailures)
{
    /// <summary>
    /// True when the trailer holds a digest equal to <see cref="StateDigest"/>; false when it holds another; null when
    /// there is no trailer or it has no digest.
    /// </summary>
    public bool? DigestMatches => Trailer?.StateDigest is { } recorded ? recorded.AsSpan().SequenceEqual(StateDigest) : null;

    /// <summary>The trailer's facts and both digests in one line, for a failed digest check to carry (M4 P4 sfix2, TODO(m4-headless-digest)).</summary>
    internal string DigestEvidence() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"trailer={(Trailer is null ? "missing" : "present")} frame_count_matches={Trailer?.FrameCountMatches.ToString() ?? "n/a"} stream_hash_matches={Trailer?.StreamHashMatches.ToString() ?? "n/a"} recorded_digest={(Trailer?.StateDigest is { } d ? GameStateDigest.ToHex(d) : "none")} replayed_digest={GameStateDigest.ToHex(StateDigest)} frames={Frames} inbound={InboundMessages} events={SessionEvents} notices={Notices} unknown={Unknown} malformed={Malformed} end={End} handler_failures={HandlerFailures}");
}
