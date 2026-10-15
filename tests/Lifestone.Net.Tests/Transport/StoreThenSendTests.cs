using System.Buffers.Binary;
using System.Net.Sockets;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

/// <summary>
/// Store-then-send (m2-plan §3.3 N2). Sealing a reliable packet spends its packet sequence, its ISAAC key and its
/// fragment sequence, and ACE hands client messages over strictly in fragment order (docs/protocol.md §2.3), so a packet
/// lost before it was stored would stall every later message. The queue now stores before it sends, and the transport
/// counts a socket's refusal as a datagram lost on the wire, which the server's NAK recovers.
/// </summary>
public sealed class StoreThenSendTests
{
    private static readonly byte[] Seed = [1, 2, 3, 4];

    [Fact]
    public void A_send_that_throws_leaves_the_packet_stored_and_a_nak_resends_it()
    {
        var sent = new List<byte[]>();
        var failNext = true;
        var stats = new TransportStats();
        using var queue = new OutboundFlowQueue(new IsaacRandom(Seed), 1, 1, new TransportClock(new ManualTimeProvider()), stats, datagram =>
        {
            if (failNext)
            {
                failNext = false;
                throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
            }

            sent.Add(datagram.ToArray());
        });

        Assert.Throws<SocketException>(() => queue.SendGameMessage([0xB1, 0xF7, 0, 0], MessageQueue.Ui));

        Assert.Empty(sent);
        Assert.Equal(1, queue.CacheDepth);
        Assert.Equal(2u, queue.HighestIdSent);
        Assert.Equal(2u, queue.FragmentSequence);

        Nak(queue, 2);

        var resent = Assert.Single(sent);
        var header = PacketHeader.Unpack(resent);
        Assert.Equal(2u, header.Sequence);
        Assert.True(header.HasFlag(PacketHeaderFlags.Retransmission));
        Assert.Equal(1u, MessageFragmentHeader.Unpack(resent.AsSpan(PacketHeader.Size)).Sequence);
        Assert.Equal(1, stats.ResendsSent);
        Assert.True(Decodes(resent, new IsaacRandom(Seed)), "the resend carries the key the first send spent");
    }

    [Fact]
    public void The_transport_counts_a_refused_send_as_lost_and_recovers_it_on_a_nak()
    {
        var sent = new List<byte[]>();
        var refuse = 1;
        using var transport = new ReliableTransport(
            new IsaacRandom(Seed),
            new IsaacRandom([5, 6, 7, 8]),
            sessionClientId: 1,
            sessionIteration: 1,
            datagram =>
            {
                if (refuse-- > 0)
                {
                    throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
                }

                sent.Add(datagram.ToArray());
            },
            new TransportClock(new ManualTimeProvider()));

        transport.Outbound.SendGameMessage([0xB1, 0xF7, 0, 0], MessageQueue.Ui); // no exception: lost on the wire
        transport.Outbound.SendGameMessage([0xB1, 0xF7, 0, 1], MessageQueue.Ui);

        Assert.Equal(1, transport.Stats.SendFailures);
        Assert.Equal(1, transport.Stats.PacketsSent);
        Assert.Equal(2, transport.Outbound.CacheDepth);
        Assert.Equal(3u, PacketHeader.Unpack(Assert.Single(sent)).Sequence);

        // ACE NAKs 2 once 3 arrives; the next sweep resends it.
        var ids = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ids, 2);
        transport.Outbound.OnRetransmitRequest(ids, 1);
        transport.Sweep();

        Assert.Equal(2, sent.Count);
        Assert.Equal(2u, PacketHeader.Unpack(sent[1]).Sequence);
        Assert.Equal(1, transport.Stats.ResendsSent);
        Assert.Equal(2, transport.Stats.PacketsSent);
    }

    [Fact]
    public void Anything_but_a_socket_error_is_a_bug_and_propagates_from_the_transport()
    {
        using var transport = new ReliableTransport(
            new IsaacRandom(Seed),
            new IsaacRandom([5, 6, 7, 8]),
            1,
            1,
            _ => throw new InvalidOperationException("a bug in the send path"),
            new TransportClock(new ManualTimeProvider()));

        Assert.Throws<InvalidOperationException>(() => transport.Outbound.SendGameMessage([1, 2, 3, 4], MessageQueue.Ui));

        Assert.Equal(1, transport.Outbound.CacheDepth);
        Assert.Equal(0, transport.Stats.SendFailures);
    }

    private static void Nak(OutboundFlowQueue queue, uint sequence)
    {
        var ids = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ids, sequence);
        queue.OnRetransmitRequest(ids, 1);
        queue.TransmitPendingResends();
    }

    private static bool Decodes(byte[] datagram, IsaacRandom keystream)
    {
        if (!PacketCodec.TryParseBorrowed(datagram, out var packet, out var headerHash, out var payloadHash, out _))
        {
            return false;
        }

        var header = packet.Header;
        return PacketCodec.VerifyChecksum(in header, headerHash, payloadHash, keystream.Next());
    }
}
