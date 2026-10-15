// Partly ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Transport/NakEmissionTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Transport; the ten
// tests that drive the transport or the tracker alone are taken; OpenAC's VirtualClock is the tests/Shared
// ManualTimeProvider behind a TransportClock; GameMessageGroup is MessageQueue; the owning decoder is
// Support.OwningPacketDecoder; the five tests that drive OpenAC's WorldSession and its ACE model are not taken.

using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

public sealed class NakEmissionTests
{
    private const uint ClientSeed = 0x77EE88FFu;
    private const uint ServerSeed = 0x55DD66CCu;
    private const uint ClientId = 0x1234u;
    private const ushort SessionIteration = 0x0007;
    private const uint TrackerSeed = 0x5EED5EEDu;

    [Fact]
    public void NakGate_ClosedAtExactly600ms_OpensJustPastIt()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u);
        Admit(transport, 4u); // parks id 3
        Assert.Equal(1, transport.Inbound.NakCount);

        clock.Advance(TimeSpan.FromSeconds(0.6));
        transport.Sweep();
        Assert.Empty(sent);
        Assert.Equal(0, transport.Stats.NaksSent);

        // One millisecond past: open.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        transport.Sweep();
        Assert.Single(sent);
        Assert.Equal(1, transport.Stats.NaksSent);

        transport.Sweep();
        clock.Advance(TimeSpan.FromSeconds(0.6));
        transport.Sweep();
        Assert.Single(sent);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        transport.Sweep();
        Assert.Equal(2, sent.Count);
        Assert.Equal(2, transport.Stats.NaksSent);
    }


    [Fact]
    public void SharedTimestamp_AckDelaysNak_NakDelaysAck()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u);

        // An ack goes out at t = 2.0 and stamps the SHARED timestamp.
        clock.Advance(TimeSpan.FromSeconds(2.0));
        transport.Sweep();
        Assert.Equal(1, transport.Stats.AcksSent);
        sent.Clear();

        Admit(transport, 4u); // parks id 3
        clock.Advance(TimeSpan.FromSeconds(0.6));
        transport.Sweep();
        Assert.Empty(sent);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        transport.Sweep();
        byte[] nak = Assert.Single(sent);
        Assert.Equal(
            (uint)PacketHeaderFlags.RequestRetransmit,
            (uint)PacketHeader.Unpack(nak).Flags);
        sent.Clear();

        Admit(transport, 3u);
        Assert.Equal(0, transport.Inbound.NakCount);
        clock.Advance(TimeSpan.FromSeconds(1.999));
        transport.Sweep();
        Assert.Empty(sent);
        Assert.Equal(1, transport.Stats.AcksSent);
        clock.Advance(TimeSpan.FromSeconds(0.001));
        transport.Sweep();
        Assert.Single(sent);
        Assert.Equal(2, transport.Stats.AcksSent);
    }

    [Fact]
    public void NakSweep_BothGatesOpen_EmitsTheNakAndNeverTheAck()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u);
        Admit(transport, 4u); // parks id 3

        clock.Advance(TimeSpan.FromSeconds(5.0));
        transport.Sweep();
        byte[] nak = Assert.Single(sent);
        Assert.Equal(
            (uint)PacketHeaderFlags.RequestRetransmit,
            (uint)PacketHeader.Unpack(nak).Flags);
        Assert.Equal(1, transport.Stats.NaksSent);
        Assert.Equal(0, transport.Stats.AcksSent);
    }


    [Fact]
    public void NakShape_CleartextExactFlags_BorrowedSequence_AscendingIds()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();

        // Two reliable sends so the borrowed sequence is nontrivial.
        transport.Outbound.SendGameMessage(
            MakeMessage(1), MessageQueue.Ui);
        transport.Outbound.SendGameMessage(
            MakeMessage(2), MessageQueue.Ui);
        Assert.Equal(3u, transport.Outbound.HighestIdSent);
        sent.Clear();

        Admit(transport, 2u);
        Admit(transport, 6u); // parks 3, 4, 5
        clock.Advance(TimeSpan.FromSeconds(0.7));
        transport.Sweep();

        byte[] nak = Assert.Single(sent);
        PacketHeader header = PacketHeader.Unpack(nak);

        Assert.Equal(
            (uint)PacketHeaderFlags.RequestRetransmit,
            (uint)header.Flags);

        Assert.Equal(transport.Outbound.HighestIdSent, header.Sequence);
        Assert.Equal(3u, transport.Outbound.HighestIdSent);
        Assert.Equal((ushort)ClientId, header.Id);
        Assert.Equal(transport.Clock.IntervalId, header.Time);
        Assert.Equal((ushort)2, header.Time); // 0.7 s of 0.5 s intervals + 1
        Assert.Equal(SessionIteration, header.Iteration);

        Assert.Equal((ushort)16, header.DataSize);
        Assert.Equal(PacketHeader.Size + 16, nak.Length);
        Assert.Equal(
            3u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                nak.AsSpan(PacketHeader.Size)));
        Assert.Equal(
            3u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                nak.AsSpan(PacketHeader.Size + 4)));
        Assert.Equal(
            4u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                nak.AsSpan(PacketHeader.Size + 8)));
        Assert.Equal(
            5u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                nak.AsSpan(PacketHeader.Size + 12)));

        OwningPacketDecoder.PacketDecodeResult decoded =
            OwningPacketDecoder.TryDecode(nak, inboundIsaac: null);
        Assert.True(decoded.IsOk, decoded.Error.ToString());
        Assert.Equal(
            new uint[] { 3u, 4u, 5u },
            decoded.Packet!.Optional.RetransmitRequests);

        Assert.Equal(3, transport.Inbound.NakCount);
    }

    [Fact]
    public void NakList_CapsAt114LowestAscending_SetUntouched()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u);
        Admit(transport, 203u); // parks 3..202 — 200 ids
        Assert.Equal(200, transport.Inbound.NakCount);

        clock.Advance(TimeSpan.FromSeconds(0.7));
        transport.Sweep();

        byte[] nak = Assert.Single(sent);
        PacketHeader header = PacketHeader.Unpack(nak);
        Assert.Equal((ushort)(4 + 114 * 4), header.DataSize);
        Assert.Equal(
            114u,
            BinaryPrimitives.ReadUInt32LittleEndian(
                nak.AsSpan(PacketHeader.Size)));
        for (int i = 0; i < 114; i++)
        {
            Assert.Equal(
                (uint)(3 + i),
                BinaryPrimitives.ReadUInt32LittleEndian(
                    nak.AsSpan(PacketHeader.Size + 4 + i * 4)));
        }

        Assert.Equal(200, transport.Inbound.NakCount);
    }


    [Fact]
    public void RejectInOrder_OwnMisparkedWord_FeedsTheNextFreshDraw()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(9);
        IsaacRandom shadow = MakeIsaac(TrackerSeed);
        uint a = shadow.Next();
        uint b = shadow.Next();
        uint c = shadow.Next();

        Assert.False(tracker.Admit(10, encrypted: false).Drop);
        Assert.Equal(1, tracker.NakCount);

        tracker.OnCleartextRejectSequence(10);
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(1, tracker.ReclaimedWordCount);

        Assert.Equal(a, Admitted(tracker, 11));
        Assert.Equal(0, tracker.ReclaimedWordCount);
        Assert.Equal(b, Admitted(tracker, 12));
        Assert.Equal(c, Admitted(tracker, 13));
        Assert.Equal(0, tracker.NakCount);
    }

    [Fact]
    public void RejectAfterHigherArrival_BubbleRealignsTheParkedChain()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(9);
        IsaacRandom shadow = MakeIsaac(TrackerSeed);
        uint a = shadow.Next();
        uint b = shadow.Next();
        uint c = shadow.Next();

        InboundSequenceTracker.Admission eleven =
            tracker.Admit(11, encrypted: true);
        Assert.Equal(b, eleven.VerifyKey);
        tracker.ReparkKey(11, b, eleven.VerifyKeyDrawOrder);
        Assert.Equal(2, tracker.NakCount); // 10 and 11 both parked

        Assert.False(tracker.Admit(10, encrypted: false).Drop);
        tracker.OnCleartextRejectSequence(10);
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(1, tracker.ReclaimedWordCount);

        // The retransmission of 11 decodes with its TRUE word.
        Assert.Equal(a, Admitted(tracker, 11));
        Assert.Equal(0, tracker.NakCount);

        Assert.Equal(b, Admitted(tracker, 12));
        Assert.Equal(0, tracker.ReclaimedWordCount);
        Assert.Equal(c, Admitted(tracker, 13));
    }

    [Fact]
    public void RejectBodyIds_StayDiscarded_OnlyTheOwnSequenceReclaims()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(9);
        IsaacRandom shadow = MakeIsaac(TrackerSeed);
        uint s1 = shadow.Next();
        uint s2 = shadow.Next();
        uint s3 = shadow.Next();
        uint s4 = shadow.Next();
        uint s5 = shadow.Next();

        // Encrypted 10, 11 (sealed s1, s2) are lost; encrypted 12 (s3)
        // arrives: parks s1 beside 10, s2 beside 11, decodes with s3.
        Assert.Equal(s3, Admitted(tracker, 12));
        Assert.Equal(2, tracker.NakCount);

        Assert.False(tracker.Admit(13, encrypted: false).Drop);
        Span<byte> ids = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(ids, 10u);
        BinaryPrimitives.WriteUInt32LittleEndian(ids.Slice(4), 11u);
        tracker.OnRejectRetransmit(ids, count: 2);
        tracker.OnCleartextRejectSequence(13);

        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(1, tracker.ReclaimedWordCount); // s4 — 13's mis-park

        Assert.Equal(s4, Admitted(tracker, 14));
        Assert.Equal(s5, Admitted(tracker, 15));
        Assert.Equal(0, tracker.ReclaimedWordCount);
    }

    [Fact]
    public void TwoInterleavedRejects_InOrder_PoolPreservesAlignment()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(9);
        IsaacRandom shadow = MakeIsaac(TrackerSeed);
        uint s1 = shadow.Next();
        uint s2 = shadow.Next();

        Assert.False(tracker.Admit(10, encrypted: false).Drop);
        tracker.OnCleartextRejectSequence(10);
        Assert.False(tracker.Admit(11, encrypted: false).Drop);
        tracker.OnCleartextRejectSequence(11);
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(1, tracker.ReclaimedWordCount);

        Assert.Equal(s1, Admitted(tracker, 12));
        Assert.Equal(s2, Admitted(tracker, 13));
        Assert.Equal(0, tracker.ReclaimedWordCount);
    }

    [Fact]
    public void TwoInterleavedRejects_Crossed_PoolDrainsInDrawOrder()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(9);
        IsaacRandom shadow = MakeIsaac(TrackerSeed);
        uint s1 = shadow.Next();
        uint s2 = shadow.Next();
        uint s3 = shadow.Next();
        uint s4 = shadow.Next();
        uint s5 = shadow.Next();

        InboundSequenceTracker.Admission twelve =
            tracker.Admit(12, encrypted: true);
        Assert.Equal(s3, twelve.VerifyKey);
        tracker.ReparkKey(12, s3, twelve.VerifyKeyDrawOrder);
        InboundSequenceTracker.Admission thirteen =
            tracker.Admit(13, encrypted: true);
        Assert.Equal(s4, thirteen.VerifyKey);
        tracker.ReparkKey(13, s4, thirteen.VerifyKeyDrawOrder);
        Assert.Equal(4, tracker.NakCount);

        Assert.False(tracker.Admit(11, encrypted: false).Drop);
        tracker.OnCleartextRejectSequence(11);
        Assert.False(tracker.Admit(10, encrypted: false).Drop);
        tracker.OnCleartextRejectSequence(10);
        Assert.Equal(2, tracker.NakCount);          // 12, 13 remain
        Assert.Equal(2, tracker.ReclaimedWordCount); // s3, s4

        // The retransmissions decode with their TRUE words (the bubble
        // put s1 beside 12 and s2 beside 13).
        Assert.Equal(s1, Admitted(tracker, 12));
        Assert.Equal(s2, Admitted(tracker, 13));
        Assert.Equal(0, tracker.NakCount);

        Assert.Equal(s3, Admitted(tracker, 14));
        Assert.Equal(s4, Admitted(tracker, 15));
        Assert.Equal(0, tracker.ReclaimedWordCount);
        Assert.Equal(s5, Admitted(tracker, 16));
    }

    private static (ReliableTransport Transport, ManualTimeProvider Clock,
        List<byte[]> Sent) CreateTransport()
    {
        var virtualClock = new ManualTimeProvider();
        var sent = new List<byte[]>();
        var transport = new ReliableTransport(
            MakeIsaac(ClientSeed),
            MakeIsaac(ServerSeed),
            (ushort)ClientId,
            SessionIteration,
            datagram => sent.Add(datagram.ToArray()),
            new TransportClock(virtualClock));
        return (transport, virtualClock, sent);
    }

    private static (InboundSequenceTracker Tracker, TransportStats Stats)
        CreateTracker(uint initialWatermark)
    {
        var stats = new TransportStats();
        return (
            new InboundSequenceTracker(
                MakeIsaac(TrackerSeed), stats, initialWatermark),
            stats);
    }

    private static void Admit(ReliableTransport transport, uint sequence)
    {
        InboundSequenceTracker.Admission admission =
            transport.Inbound.Admit(sequence, encrypted: true);
        Assert.False(admission.Drop);
    }

    private static uint Admitted(InboundSequenceTracker tracker, uint sequence)
    {
        InboundSequenceTracker.Admission admission =
            tracker.Admit(sequence, encrypted: true);
        Assert.False(admission.Drop);
        Assert.NotNull(admission.VerifyKey);
        return admission.VerifyKey!.Value;
    }

    private static byte[] MakeMessage(byte marker) =>
        new byte[] { marker, 0x11, 0x22, 0x33, 0x00, 0x00, 0x00, 0x00 };

    private static IsaacRandom MakeIsaac(uint seed)
    {
        Span<byte> seedBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(seedBytes, seed);
        return new IsaacRandom(seedBytes);
    }
}
