// Ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Transport/OutboundFragmentationTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Transport;
// the owning decoder is the test-side Support.OwningPacketDecoder (PacketCodec.TryDecode moved there); MessageQueue for
// GameMessageGroup; TransportClock over TimeProvider.System; the LongChannelMessages_AreTransmittedWhole theory is not
// taken (it drives OpenAC's WorldSession, which is not vendored). N2: the failed-send test asserts store-then-send (the
// packet whose send threw is in the cache, and a NAK resends it) and is renamed to say so.

using System.Buffers;
using System.Buffers.Binary;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Support;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Transport;

public sealed class OutboundFragmentationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(447)]
    [InlineData(448)]
    [InlineData(449)]
    [InlineData(896)]
    [InlineData(897)]
    [InlineData(65536)]
    public void PayloadBoundaries_RoundTripWithOneMessageIdentity(int size)
    {
        byte[] body = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        VerifyRoundTrip(body);
    }

    private static void VerifyRoundTrip(byte[] body)
    {
        var sent = new List<byte[]>();
        using var queue = new OutboundFlowQueue(new IsaacRandom(new byte[4]), 1, 1,
            new TransportClock(TimeProvider.System), new TransportStats(), bytes => sent.Add(bytes.ToArray()));
        queue.SendGameMessage(body, MessageQueue.Ui);
        int count = Math.Max(1, (body.Length + 447) / 448);
        Assert.Equal(count, sent.Count);
        Assert.Equal(2u, queue.FragmentSequence);
        Assert.Equal(count, queue.CacheDepth);
        var cipher = new IsaacRandom(new byte[4]);
        foreach (byte[] packet in sent)
            Assert.True(OwningPacketDecoder.TryDecode(packet, cipher).IsOk);
        var assembler = new FragmentAssembler();
        byte[]? assembled = null;
        for (int i = count - 1; i >= 0; i--)
        {
            byte[] packet = sent[i];
            PacketHeader header = PacketHeader.Unpack(packet);
            Assert.Equal((uint)(2 + i), header.Sequence);
            Assert.InRange(packet.Length, PacketHeader.Size + MessageFragmentHeader.Size, 484);
            MessageFragmentHeader fragment = MessageFragmentHeader.Unpack(packet.AsSpan(PacketHeader.Size));
            Assert.Equal(1u, fragment.Sequence);
            Assert.Equal(GameMessageFragment.OutboundFragmentId, fragment.Id);
            Assert.Equal((ushort)count, fragment.Count);
            Assert.Equal((ushort)i, fragment.Index);
            Assert.Equal((ushort)MessageQueue.Ui, fragment.Queue);
            Assert.Equal(header.DataSize, fragment.TotalSize);
            byte[] payload = packet[(PacketHeader.Size + MessageFragmentHeader.Size)..];
            assembled = assembler.Ingest(new MessageFragment(fragment, payload), out _) ?? assembled;
        }
        Assert.Equal(body, assembled);
        queue.SendGameMessage([1, 2, 3], MessageQueue.Control);
        var next = MessageFragmentHeader.Unpack(sent[^1].AsSpan(PacketHeader.Size));
        Assert.Equal(2u, next.Sequence);
        Assert.Equal((ushort)1, next.Count);
        Assert.Equal((ushort)0, next.Index);
        Assert.True(OwningPacketDecoder.TryDecode(sent[^1], cipher).IsOk);
    }

    [Fact]
    public void LostMiddleFragment_IsRecoveredByNakWithoutAdvancingCipher()
    {
        var sent = new List<byte[]>();
        var stats = new TransportStats();
        using var queue = new OutboundFlowQueue(new IsaacRandom(new byte[4]), 1, 1,
            new TransportClock(TimeProvider.System), stats, bytes => sent.Add(bytes.ToArray()));
        byte[] body = Enumerable.Range(0, 897).Select(i => (byte)i).ToArray();
        queue.SendGameMessage(body, MessageQueue.Ui);
        var assembler = new FragmentAssembler();
        var cipher = new IsaacRandom(new byte[4]);
        var packets = sent.Select(p => OwningPacketDecoder.TryDecode(p, cipher).Packet!).ToArray();
        Assert.Null(assembler.Ingest(packets[0].Fragments[0], out _));
        Assert.Null(assembler.Ingest(packets[2].Fragments[0], out _));
        byte[] request = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 3);
        queue.OnRetransmitRequest(request, 1);
        queue.TransmitPendingResends();
        byte[] resent = sent[3];
        PacketHeader header = PacketHeader.Unpack(resent);
        Assert.Equal(3u, header.Sequence);
        Assert.True(header.HasFlag(PacketHeaderFlags.Retransmission));
        Assert.Equal(sent[1][PacketHeader.Size..], resent[PacketHeader.Size..]);
        uint sealedChecksum = packets[1].Header.Checksum - packets[1].Header.CalculateHeaderHash32();
        Assert.Equal(header.CalculateHeaderHash32() + sealedChecksum, header.Checksum);
        Assert.Equal(body, assembler.Ingest(packets[1].Fragments[0], out _));
        Assert.Equal(1, stats.ResendsSent);
        queue.SendGameMessage([4], MessageQueue.Ui);
        Assert.True(OwningPacketDecoder.TryDecode(sent[^1], cipher).IsOk);
    }

    [Fact]
    public void FailedLaterSend_StoresTheFailedPacketAndDoesNotReuseMessageIdentity()
    {
        var pool = new TrackingPool();
        var sent = new List<byte[]>();
        int attempts = 0;
        using (var queue = new OutboundFlowQueue(new IsaacRandom(new byte[4]), 1, 1,
            new TransportClock(TimeProvider.System), new TransportStats(), bytes =>
            {
                if (++attempts == 2) throw new IOException("send failed");
                sent.Add(bytes.ToArray());
            }, pool))
        {
            Assert.Throws<IOException>(() => queue.SendGameMessage(new byte[897], MessageQueue.Ui));
            // N2, store-then-send: the packet whose send threw (sequence 3) was stored before the send.
            Assert.Equal(2, queue.CacheDepth);
            Assert.Equal(2, pool.Outstanding.Count);
            Assert.Equal(2u, queue.FragmentSequence);
            queue.SendGameMessage([7], MessageQueue.Ui);
            Assert.Equal(2u, MessageFragmentHeader.Unpack(sent[^1].AsSpan(PacketHeader.Size)).Sequence);
            Assert.Equal(4u, PacketHeader.Unpack(sent[^1]).Sequence);

            // The server NAKs sequence 3 and gets it, with the Retransmission flag and the original body.
            byte[] request = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(request, 3);
            queue.OnRetransmitRequest(request, 1);
            queue.TransmitPendingResends();
            PacketHeader resent = PacketHeader.Unpack(sent[^1]);
            Assert.Equal(3u, resent.Sequence);
            Assert.True(resent.HasFlag(PacketHeaderFlags.Retransmission));
            Assert.Equal((ushort)1, MessageFragmentHeader.Unpack(sent[^1].AsSpan(PacketHeader.Size)).Index);
        }
        Assert.Empty(pool.Outstanding);
    }

    private sealed class TrackingPool : ArrayPool<byte>
    {
        public HashSet<byte[]> Outstanding { get; } = [];
        public override byte[] Rent(int minimumLength)
        {
            byte[] buffer = new byte[minimumLength];
            Assert.True(Outstanding.Add(buffer));
            return buffer;
        }
        public override void Return(byte[] array, bool clearArray = false) => Assert.True(Outstanding.Remove(array));
    }
}
