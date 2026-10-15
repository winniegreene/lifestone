using System.Buffers;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Transport;

/// <summary>
/// Puts reassembled server messages in delivery order (m2-plan §3.3 N5, owner decision D18). With
/// <see cref="InboundOrder.FragmentSequence"/>, Lifestone's default, messages are delivered globally by fragment
/// sequence from 0, because ACE numbers every message of a session from one counter across all its queues
/// (<c>NetworkSession.SendBundle</c>); a message that arrives ahead of a gap waits until the gap fills. With
/// <see cref="InboundOrder.Arrival"/> (<c>--retail-net</c>) messages go out as they complete, which is what retail does.
/// In both modes a message the session could not dispatch yet (its pump budget was spent) waits here, in order.
/// </summary>
/// <remarks>
/// <para><b>Zero copy in order.</b> The caller asks <see cref="Classify"/> first. An in-order message is dispatched
/// straight from the datagram buffer (<see cref="Advance"/>, then dispatch); only a message that must wait is copied, into
/// a buffer rented from <see cref="ArrayPool{T}.Shared"/> that <see cref="Release"/> hands back after dispatch.</para>
/// <para><b>The gap-skip rule.</b> A gap (messages waiting, the next one missing) is given up on only when the transport
/// has no NAK outstanding, the reassembler holds no partial message, and either a RejectRetransmit arrived since the gap
/// opened (the server no longer has the packet) or the gap is <see cref="GapSkipAge"/> old. Under 5% loss a lost
/// message is NAKed and resent well inside that, so a skip means the message is gone for good; each skip is counted in
/// <see cref="GapSkips"/> and the session logs it as an error.</para>
/// <para>Single-threaded: the session's game thread.</para>
/// </remarks>
internal sealed class InboundSequencer
{
    /// <summary>How old a gap must be before it is skipped without a RejectRetransmit.</summary>
    public static readonly TimeSpan GapSkipAge = TimeSpan.FromSeconds(5);

    private readonly InboundOrder _order;
    private readonly TimeProvider _time;
    private readonly Dictionary<uint, WaitingMessage> _ahead = [];
    private readonly Queue<WaitingMessage> _arrivals = new();
    private long? _gapOpenedAt;
    private bool _rejectSinceGap;

    /// <summary>A sequencer in <paramref name="order"/> mode on <paramref name="time"/>'s clock.</summary>
    public InboundSequencer(InboundOrder order, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _order = order;
        _time = time;
    }

    /// <summary>The fragment sequence delivered next (<see cref="InboundOrder.FragmentSequence"/>; ACE's first is 0).</summary>
    public uint Next { get; private set; }

    /// <summary>Messages waiting (ahead of a gap, or behind a spent budget).</summary>
    public int WaitingCount => _order == InboundOrder.Arrival ? _arrivals.Count : _ahead.Count;

    /// <summary>
    /// Waiting messages that could be dispatched now, in order: the run from <see cref="Next"/> (or every waiting
    /// message in arrival mode). Those behind a gap are not counted.
    /// </summary>
    public int ReadyCount
    {
        get
        {
            if (_order == InboundOrder.Arrival)
            {
                return _arrivals.Count;
            }

            var count = 0;
            for (var sequence = Next; _ahead.ContainsKey(sequence); sequence = unchecked(sequence + 1))
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>True when a waiting message can be dispatched now (the cheap form of <see cref="ReadyCount"/> &gt; 0).</summary>
    public bool HasReady => _order == InboundOrder.Arrival ? _arrivals.Count > 0 : _ahead.ContainsKey(Next);

    /// <summary>True while messages wait behind a missing one (<see cref="InboundOrder.FragmentSequence"/> only).</summary>
    public bool HasGap => _ahead.Count > 0 && !_ahead.ContainsKey(Next);

    /// <summary>Gaps given up on.</summary>
    public long GapSkips { get; private set; }

    /// <summary>Messages dropped because they were already delivered (or skipped) or were already waiting.</summary>
    public long DuplicatesDropped { get; private set; }

    /// <summary>What to do with a complete message.</summary>
    public SequencerVerdict Classify(uint fragmentSequence)
    {
        if (_order == InboundOrder.Arrival)
        {
            return _arrivals.Count == 0 ? SequencerVerdict.InOrder : SequencerVerdict.Wait;
        }

        if (fragmentSequence == Next)
        {
            return SequencerVerdict.InOrder;
        }

        return SequenceMath.IsNewer(fragmentSequence, Next) && !_ahead.ContainsKey(fragmentSequence)
            ? SequencerVerdict.Wait
            : SequencerVerdict.Duplicate;
    }

    /// <summary>Marks the in-order message <see cref="Classify"/> allowed as delivered, before the caller dispatches it.</summary>
    public void Advance()
    {
        if (_order == InboundOrder.FragmentSequence)
        {
            Next = unchecked(Next + 1);
            UpdateGap();
        }
    }

    /// <summary>
    /// Keeps a message that cannot be dispatched now. <paramref name="owned"/> is an array the caller hands over whole
    /// (a reassembled multi-fragment message); otherwise <paramref name="body"/> is copied into a pooled buffer.
    /// </summary>
    public void Hold(uint fragmentSequence, MessageQueue queue, long receivedTimestamp, ReadOnlySpan<byte> body, byte[]? owned)
    {
        if (Classify(fragmentSequence) == SequencerVerdict.Duplicate)
        {
            DuplicatesDropped++;
            return;
        }

        byte[] buffer;
        var pooled = false;
        if (owned is not null && owned.Length == body.Length)
        {
            buffer = owned;
        }
        else
        {
            buffer = ArrayPool<byte>.Shared.Rent(Math.Max(body.Length, 1));
            body.CopyTo(buffer);
            pooled = true;
        }

        var waiting = new WaitingMessage(fragmentSequence, queue, receivedTimestamp, buffer, body.Length, pooled);
        if (_order == InboundOrder.Arrival)
        {
            _arrivals.Enqueue(waiting);
        }
        else
        {
            _ahead.Add(fragmentSequence, waiting);
            UpdateGap();
        }
    }

    /// <summary>Takes the next waiting message that can be dispatched now, in order; the caller releases it after dispatch.</summary>
    public bool TryTakeReady(out WaitingMessage message)
    {
        if (_order == InboundOrder.Arrival)
        {
            return _arrivals.TryDequeue(out message);
        }

        if (_ahead.Remove(Next, out message))
        {
            Next = unchecked(Next + 1);
            UpdateGap();
            return true;
        }

        return false;
    }

    /// <summary>Returns a taken message's buffer to the pool (when it came from there).</summary>
    public static void Release(in WaitingMessage message)
    {
        if (message.Pooled)
        {
            ArrayPool<byte>.Shared.Return(message.Buffer);
        }
    }

    /// <summary>A RejectRetransmit arrived: a gap open now may be skipped as soon as nothing else can fill it.</summary>
    public void OnRejectRetransmit()
    {
        if (HasGap)
        {
            _rejectSinceGap = true;
        }
    }

    /// <summary>
    /// Applies the gap-skip rule (see the remarks): when it holds, <see cref="Next"/> jumps to the first waiting message
    /// and the skipped range is returned (<paramref name="from"/> to <paramref name="to"/>, inclusive).
    /// </summary>
    public bool TrySkipGap(int naksOutstanding, int partialMessages, out uint from, out uint to)
    {
        from = to = 0;
        if (!HasGap || naksOutstanding > 0 || partialMessages > 0 || _gapOpenedAt is not { } opened)
        {
            return false;
        }

        if (!_rejectSinceGap && _time.GetElapsedTime(opened) < GapSkipAge)
        {
            return false;
        }

        var first = Next;
        var found = false;
        foreach (var sequence in _ahead.Keys)
        {
            if (!found || SequenceMath.IsNewer(first, sequence))
            {
                first = sequence;
                found = true;
            }
        }

        from = Next;
        to = unchecked(first - 1);
        Next = first;
        GapSkips++;
        UpdateGap();
        return true;
    }

    /// <summary>Returns every waiting buffer to the pool (the connection is closing).</summary>
    public void Clear()
    {
        foreach (var waiting in _ahead.Values)
        {
            Release(waiting);
        }

        while (_arrivals.TryDequeue(out var waiting))
        {
            Release(waiting);
        }

        _ahead.Clear();
        _gapOpenedAt = null;
        _rejectSinceGap = false;
    }

    private void UpdateGap()
    {
        if (!HasGap)
        {
            _gapOpenedAt = null;
            _rejectSinceGap = false;
        }
        else
        {
            _gapOpenedAt ??= _time.GetTimestamp();
        }
    }
}

/// <summary>What <see cref="InboundSequencer.Classify"/> decided.</summary>
internal enum SequencerVerdict : byte
{
    /// <summary>The next message: dispatch it now (after <see cref="InboundSequencer.Advance"/>) or hold it if the budget is spent.</summary>
    InOrder,

    /// <summary>Ahead of a gap, or behind waiting messages: hold it.</summary>
    Wait,

    /// <summary>Already delivered, skipped or waiting: drop it.</summary>
    Duplicate,
}

/// <summary>A message the sequencer holds.</summary>
/// <param name="FragmentSequence">Its fragment sequence.</param>
/// <param name="Queue">Its queue.</param>
/// <param name="ReceivedTimestamp">When its last fragment arrived.</param>
/// <param name="Buffer">Its bytes (the first <paramref name="Length"/>).</param>
/// <param name="Length">Its length.</param>
/// <param name="Pooled">True when <paramref name="Buffer"/> was rented and must go back to the pool.</param>
internal readonly record struct WaitingMessage(uint FragmentSequence, MessageQueue Queue, long ReceivedTimestamp, byte[] Buffer, int Length, bool Pooled)
{
    /// <summary>The message bytes.</summary>
    public ReadOnlySpan<byte> Body => Buffer.AsSpan(0, Length);
}
