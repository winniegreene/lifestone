// Partly ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Transport/AckNakSchedulerTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Transport; the three
// tests that drive the transport alone are taken; OpenAC's VirtualClock is the tests/Shared ManualTimeProvider behind a
// TransportClock; the owning decoder is Support.OwningPacketDecoder; the tests that drive OpenAC's WorldSession or its
// ACE model are not taken (FakeAceInteropTests covers the server's side of the acks).

using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Transport;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

public sealed class AckNakSchedulerTests
{
    private const uint ClientSeed = 0x11AA22BBu;
    private const uint ServerSeed = 0x33CC44DDu;
    private const uint ClientId = 0x1234u;
    private const ushort SessionIteration = 0x0007;

    [Fact]
    public void CumulativeAck_TwoSecondGate_CarriesWatermarkAtEmission()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u);
        Admit(transport, 3u);

        transport.Sweep();
        clock.Advance(TimeSpan.FromSeconds(1.99));
        transport.Sweep();
        Assert.Empty(sent);

        clock.Advance(TimeSpan.FromSeconds(0.01));
        transport.Sweep();
        Assert.Equal((ushort)5, transport.Clock.IntervalId);
        AssertAckShape(
            Assert.Single(sent),
            expectedSequence: 1u,
            expectedValue: 3u,
            expectedTime: transport.Clock.IntervalId);
        Assert.Equal(1, transport.Stats.AcksSent);

        // The gate reset: silent until the next 2.0 s elapses.
        transport.Sweep();
        clock.Advance(TimeSpan.FromSeconds(1.99));
        transport.Sweep();
        Assert.Single(sent);

        Admit(transport, 4u);
        Admit(transport, 5u);
        clock.Advance(TimeSpan.FromSeconds(0.01));
        transport.Sweep();
        Assert.Equal(2, sent.Count);
        AssertAckShape(
            sent[1],
            expectedSequence: 1u,
            expectedValue: 5u,
            expectedTime: transport.Clock.IntervalId);
        Assert.Equal(2, transport.Stats.AcksSent);
    }



    [Fact]
    public void ParkedNak_SuppressesTheAck_AckResumesWhenTheGapClears()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();
        Admit(transport, 2u); // watermark 2, no gap
        Admit(transport, 4u); // gap walk parks id 3
        Assert.Equal(1, transport.Inbound.NakCount);

        clock.Advance(TimeSpan.FromSeconds(2.5));
        transport.Sweep();
        transport.Sweep();
        byte[] nak = Assert.Single(sent);
        Assert.Equal(
            (uint)PacketHeaderFlags.RequestRetransmit,
            (uint)PacketHeader.Unpack(nak).Flags);
        Assert.Equal(0, transport.Stats.AcksSent);
        Assert.Equal(1, transport.Stats.NaksSent);
        sent.Clear();

        Admit(transport, 3u);
        Assert.Equal(0, transport.Inbound.NakCount);
        transport.Sweep();
        Assert.Empty(sent);
        clock.Advance(TimeSpan.FromSeconds(2.0));
        transport.Sweep();
        AssertAckShape(
            Assert.Single(sent),
            expectedSequence: 1u,
            expectedValue: 4u,
            expectedTime: transport.Clock.IntervalId);
    }


    [Fact]
    public void CreateObjectFlood_InsideOneWindow_CollapsesToOneAck()
    {
        (ReliableTransport transport, ManualTimeProvider clock, List<byte[]> sent) =
            CreateTransport();

        uint sequence = 2;
        for (int i = 0; i < 50; i++)
        {
            Admit(transport, sequence++);
            clock.Advance(TimeSpan.FromMilliseconds(20));
            transport.Sweep();
        }

        Assert.Empty(sent);

        clock.Advance(TimeSpan.FromSeconds(1.0));
        transport.Sweep();
        AssertAckShape(
            Assert.Single(sent),
            expectedSequence: 1u,
            expectedValue: 51u,
            expectedTime: transport.Clock.IntervalId);
        Assert.Equal(1, transport.Stats.AcksSent);
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

    /// <summary>Admit one encrypted sequenced arrival that must not drop.</summary>
    private static void Admit(ReliableTransport transport, uint sequence)
    {
        InboundSequenceTracker.Admission admission =
            transport.Inbound.Admit(sequence, encrypted: true);
        Assert.False(admission.Drop);
    }

    private static void AssertAckShape(
        byte[] datagram,
        uint expectedSequence,
        uint expectedValue,
        ushort expectedTime)
    {
        Assert.Equal(PacketHeader.Size + sizeof(uint), datagram.Length);
        PacketHeader header = PacketHeader.Unpack(datagram);
        Assert.Equal(
            (uint)PacketHeaderFlags.AckSequence,
            (uint)header.Flags);
        Assert.Equal(expectedSequence, header.Sequence);
        Assert.Equal((ushort)ClientId, header.Id);
        Assert.Equal(expectedTime, header.Time);
        Assert.Equal(SessionIteration, header.Iteration);
        Assert.Equal((ushort)sizeof(uint), header.DataSize);
        Assert.Equal(
            expectedValue,
            BinaryPrimitives.ReadUInt32LittleEndian(
                datagram.AsSpan(PacketHeader.Size)));

        OwningPacketDecoder.PacketDecodeResult decoded =
            OwningPacketDecoder.TryDecode(datagram, inboundIsaac: null);
        Assert.True(decoded.IsOk, decoded.Error.ToString());
        Assert.Equal(expectedValue, decoded.Packet!.Optional.AckSequence);
    }

    /// <summary>An 8-byte message body whose first byte is a test marker.</summary>
    private static byte[] MakeMessage(byte marker) =>
        new byte[] { marker, 0x11, 0x22, 0x33, 0x00, 0x00, 0x00, 0x00 };

    private static IsaacRandom MakeIsaac(uint seed)
    {
        Span<byte> seedBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(seedBytes, seed);
        return new IsaacRandom(seedBytes);
    }
}
