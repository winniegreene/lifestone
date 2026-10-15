// Ported from eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/Transport/OutboundFlowQueue.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Transport;
// the queue argument is Lifestone.Net.Types.MessageQueue (OpenAC's GameMessageGroup, same values). N2: a fragment is
// stored before it is sent (OpenAC sent first), so a send that throws never loses a spent sequence. N8: the tail probe
// (TailProbeEnabled, RawServerAck): an explicit server ack is kept apart from the NAK-inflated watermark, and every
// transmission stamps its packet's last-sent time; with the probe on, acks are held while a sent packet is unacked
// (HoldsAcks).

using System.Buffers;
using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;

namespace Lifestone.Net.Transport;

/// <summary>Sends one finalized datagram to the wire. Span-shaped so the
/// send path stays allocation-free.</summary>
internal delegate void DatagramSendDelegate(ReadOnlySpan<byte> datagram);

/// <remarks>
/// <para><b>The tail probe (N8, a Lifestone deviation; owner decision D18).</b> ACE NAKs a missing client packet only when
/// one at least two ahead of it arrives (<c>NetworkSession.ProcessPacket</c>), at most once a second, so the last packet
/// of a burst (LoginComplete, a final AutonomousPosition) can stay lost while the client sends nothing more. When the
/// server's ack (<see cref="OnAckSequence"/>) is behind <see cref="HighestIdSent"/> and is the highest ack seen so far
/// (<see cref="RawServerAck"/>), and the packet right after it was last put on the wire at least
/// <see cref="TailProbeDelaySeconds"/> before that ack arrived, that one packet is sent again with the Retransmission flag
/// and its original key, on the next <see cref="TransmitPendingResends"/>. Nothing above ack + 1 is ever probed: ACE
/// counts a duplicate encrypted packet as a CRC error (<c>ClientPacket.VerifyCRC</c> over <c>CryptoSystem</c>). ACE acks
/// every 2 s, so at most one probe goes out per ack. The raw ack, not <see cref="AckWatermark"/>, decides: OpenAC folds a
/// NAK's first id into the watermark, and that id is the first packet the server lacks, not the last it has. Off
/// (<see cref="TailProbeEnabled"/> false, <c>--retail-net</c>), the queue behaves as OpenAC's did: it resends only what
/// the server NAKs.</para>
/// <para><b>Held acks (N8, part of the same deviation).</b> An ack-only packet carries the sequence of the last packet
/// sent (<see cref="HighestIdSent"/>), as OpenAC's does and ACE expects (docs/protocol.md §2.7: ACE takes an ack-only
/// packet at the last sequence it received). ACE holds a packet that arrives ahead of the next
/// sequence it expects in a table keyed by sequence, and keeps the first one it gets for a key
/// (<c>NetworkSession.ProcessPacket</c>, <c>outOfOrderPackets</c>). So when that last data packet and one before it are
/// both lost, the ack-only packet takes the data packet's place; if the data packet's resend then arrives before the gap
/// below is filled, ACE checks it (spending its key) and drops it, and every later resend fails the checksum: the
/// message is lost for good and ACE stalls every message after it (the virtual soak found this). So while the tail probe
/// is on, a due ack waits as long as the server has not acked every packet sent (<see cref="HoldsAcks"/>). ACE uses the
/// client's ack only to prune its resend cache, which also ages out after 120 s, and any valid packet keeps its session
/// alive. With <c>--retail-net</c> the acks go out unheld, every 2 s, as OpenAC sends them (whether retail's own servers
/// kept such a table is not known).</para>
/// </remarks>
internal sealed class OutboundFlowQueue : IDisposable
{
    /// <summary>N8: how long after its last transmission an unacked packet is probed.</summary>
    public const double TailProbeDelaySeconds = 1.0;

    private readonly IsaacRandom _outboundIsaac;
    private readonly TransportClock _clock;
    private readonly TransportStats _stats;
    private readonly DatagramSendDelegate _send;
    private readonly SentPacketStore _store;
    private readonly ArrayPool<byte> _pool;
    private readonly ushort _sessionClientId;
    private readonly ushort _sessionIteration;

    private readonly List<uint> _pendingResends = new();
    private readonly long _tailProbeDelayTicks;
    private uint? _tailProbe;

    public uint HighestIdSent { get; private set; }

    /// <summary>The fragment sequence the NEXT reliable message will use.
    /// Starts 1, exactly like the pre-N1 <c>WorldSession</c> field.</summary>
    public uint FragmentSequence { get; private set; }

    public uint AckWatermark { get; private set; }

    /// <summary>N8: the highest AckSequence the server sent (0 before the first). Unlike <see cref="AckWatermark"/>,
    /// a NAK never moves it.</summary>
    public uint RawServerAck { get; private set; }

    /// <summary>N8: whether the tail probe runs and acks are held (see the remarks); off by default, as in OpenAC and retail.</summary>
    public bool TailProbeEnabled { get; set; }

    /// <summary>
    /// N8: true while acks must wait (see the remarks): the probe is on, a data packet has been sent, and the server has
    /// not acked the last one.
    /// </summary>
    public bool HoldsAcks =>
        TailProbeEnabled
        && HighestIdSent != 1
        && (RawServerAck == 0 || SequenceMath.IsNewer(HighestIdSent, RawServerAck));

    public int CacheDepth => _store.Count;

    public int PendingResendCount => _pendingResends.Count;

    public OutboundFlowQueue(
        IsaacRandom outboundIsaac,
        ushort sessionClientId,
        ushort sessionIteration,
        TransportClock clock,
        TransportStats stats,
        DatagramSendDelegate send,
        ArrayPool<byte>? pool = null,
        uint highestIdSent = 1,
        uint fragmentSequence = 1)
    {
        ArgumentNullException.ThrowIfNull(outboundIsaac);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(send);

        _outboundIsaac = outboundIsaac;
        _sessionClientId = sessionClientId;
        _sessionIteration = sessionIteration;
        _clock = clock;
        _stats = stats;
        _send = send;
        _pool = pool ?? ArrayPool<byte>.Shared;
        _store = new SentPacketStore(_pool);
        HighestIdSent = highestIdSent;
        FragmentSequence = fragmentSequence;
        _tailProbeDelayTicks = (long)Math.Round(TailProbeDelaySeconds * clock.Frequency);
    }

    public uint PeekNextPacketSequence => NextSequenceAfter(HighestIdSent);

    private static uint NextSequenceAfter(uint sequence) =>
        sequence == uint.MaxValue ? 1u : sequence + 1u;

    public void SendGameMessage(
        ReadOnlySpan<byte> gameMessageBody,
        MessageQueue queue)
    {
        int count = Math.Max(1, (int)(((long)gameMessageBody.Length
            + MessageFragmentHeader.MaxFragmentDataSize - 1)
            / MessageFragmentHeader.MaxFragmentDataSize));
        if (count > ushort.MaxValue)
            throw new ArgumentException("Game message needs more fragments than the header can represent.", nameof(gameMessageBody));

        // All fragments share one message identity. Reserve it before sending
        // so a later send failure cannot reuse a partially transmitted identity.
        uint messageSequence = FragmentSequence++;
        int offset = 0;
        for (int index = 0; index < count; index++)
        {
            int length = Math.Min(MessageFragmentHeader.MaxFragmentDataSize,
                gameMessageBody.Length - offset);
            SendFragment(gameMessageBody.Slice(offset, length), queue,
                messageSequence, (ushort)index, (ushort)count);
            offset += length;
        }
    }

    private void SendFragment(
        ReadOnlySpan<byte> gameMessageBody,
        MessageQueue queue,
        uint messageSequence,
        ushort index,
        ushort count)
    {
        byte[] buffer = _pool.Rent(
            PacketHeader.Size
            + MessageFragmentHeader.Size
            + gameMessageBody.Length);
        PacketHeader header;
        int fragmentLength;
        int datagramLength;
        uint isaacKeyUsed;
        uint sealedChecksum;
        try
        {
            fragmentLength = GameMessageFragment.WriteFragment(
                buffer.AsSpan(PacketHeader.Size),
                messageSequence,
                queue,
                gameMessageBody,
                index,
                count);
            header = new PacketHeader
            {
                Sequence = PeekNextPacketSequence,
                Flags = PacketHeaderFlags.BlobFragments
                    | PacketHeaderFlags.EncryptedChecksum,
                Id = _sessionClientId,
                Time = _clock.IntervalId,
                Iteration = _sessionIteration,
            };
            datagramLength = PacketCodec.FinalizeInPlace(
                header,
                buffer,
                fragmentLength,
                optionalLength: 0,
                _outboundIsaac,
                out isaacKeyUsed,
                out sealedChecksum);
        }
        catch
        {
            _pool.Return(buffer);
            throw;
        }

        HighestIdSent = header.Sequence;

        // Store, then send (N2). Sealing spent the packet sequence and its ISAAC
        // key, and the fragment sequence was reserved before: from here on the
        // packet must be resendable whatever the send does. OpenAC sent first
        // and stored after, so a send that threw lost the packet for good, and
        // ACE, which hands messages over strictly in fragment order, stalled
        // every later one (docs/protocol.md section 2.3). The store owns the
        // buffer now; a NAK resends it.
        _store.Add(
            new SentPacketStore.CachedPacket(
                header.Sequence,
                buffer,
                fragmentLength,
                sealedChecksum,
                isaacKeyUsed,
                hasFragments: true,
                lastSentAt: _clock.GetTimestamp()),
            optionalLength: 0);

        _send(buffer.AsSpan(0, datagramLength));
    }

    /// <summary>
    /// An explicit AckSequence from the server: folds it into <see cref="AckWatermark"/>, keeps the raw maximum in
    /// <see cref="RawServerAck"/>, and arms the tail probe when it is due (see the remarks).
    /// </summary>
    public void OnAckSequence(uint ackSequence)
    {
        FoldWatermark(ackSequence);
        if (RawServerAck != 0 && SequenceMath.IsNewer(RawServerAck, ackSequence))
            return;  // an older ack, reordered: it says nothing new

        RawServerAck = ackSequence;
        if (!TailProbeEnabled || !SequenceMath.IsNewer(HighestIdSent, ackSequence))
            return;

        uint next = ackSequence == uint.MaxValue ? 1u : ackSequence + 1u;
        if (_store.TryGet(next, out SentPacketStore.CachedPacket cached)
            && _clock.GetTimestamp() - cached.LastSentAt >= _tailProbeDelayTicks)
        {
            _tailProbe = next;
        }
    }

    private void FoldWatermark(uint ackSequence)
    {
        _stats.AcksConsumed++;
        AckWatermark = SequenceMath.Max(AckWatermark, ackSequence);
    }

    public void OnRetransmitRequest(ReadOnlySpan<byte> idBytes, int count)
    {
        if (count <= 0 || idBytes.Length < count * 4)
            return;

        _stats.NakRequestsReceived++;
        for (int i = 0; i < count; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(
                idBytes.Slice(i * 4));
            if (i == 0)
                FoldWatermark(id);

            if (_store.Contains(id))
                MergeInsertPending(id);
            else
                _stats.UncachedNakIds++;
        }
    }

    private void MergeInsertPending(uint id)
    {
        int index = 0;
        while (index < _pendingResends.Count
               && SequenceMath.IsNewer(id, _pendingResends[index]))
        {
            index++;
        }

        if (index < _pendingResends.Count && _pendingResends[index] == id)
            return;

        _pendingResends.Insert(index, id);
    }

    public void TransmitPendingResends()
    {
        if (_tailProbe is uint probe)
        {
            // A NAK resend of the same packet in this pass makes the probe redundant.
            _tailProbe = null;
            if (!_pendingResends.Contains(probe)
                && (AckWatermark == 0 || !SequenceMath.IsNewer(AckWatermark, probe))
                && _store.TryGet(probe, out SentPacketStore.CachedPacket probed))
            {
                Resend(in probed);
                _stats.TailProbes++;
            }
        }

        if (_pendingResends.Count > 0)
        {
            for (int i = 0; i < _pendingResends.Count; i++)
            {
                if (AckWatermark != 0
                    && SequenceMath.IsNewer(
                        AckWatermark,
                        _pendingResends[i]))
                {
                    continue;
                }

                if (!_store.TryGet(
                        _pendingResends[i],
                        out SentPacketStore.CachedPacket cached))
                {
                    continue;
                }

                Resend(in cached);
            }

            _pendingResends.Clear();
        }

        _store.FlushOlderThan(AckWatermark);
    }

    private void Resend(in SentPacketStore.CachedPacket cached)
    {
        PacketHeader header = BuildResendHeader(in cached, _clock.IntervalId);
        header.Pack(cached.Buffer);
        _store.MarkSent(cached.Sequence, _clock.GetTimestamp());
        _send(cached.Buffer.AsSpan(
            0,
            PacketHeader.Size + cached.BodyLength));
        _stats.ResendsSent++;
    }

    internal static PacketHeader BuildResendHeader(
        in SentPacketStore.CachedPacket cached,
        ushort intervalId)
    {
        PacketHeader header = PacketHeader.Unpack(cached.Buffer);
        PacketHeaderFlags flags = PacketHeaderFlags.Retransmission
            | PacketHeaderFlags.EncryptedChecksum;
        if (cached.HasFragments)
            flags |= PacketHeaderFlags.BlobFragments;
        header.Flags = flags;
        header.Time = intervalId;
        header.Checksum =
            header.CalculateHeaderHash32() + cached.SealedChecksum;
        return header;
    }

    public void Dispose() => _store.Dispose();
}
