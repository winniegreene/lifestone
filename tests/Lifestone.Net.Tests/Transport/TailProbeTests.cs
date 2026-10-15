using System.Buffers.Binary;
using System.Net;
using Lifestone.Net.Crypto;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Transport;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

/// <summary>
/// The tail probe and the held acks (m2-plan §3.3 N8; owner decision D18; <see cref="OutboundFlowQueue"/>'s remarks).
/// ACE NAKs a missing client packet only when one two ahead of it arrives, so a lost last packet stays lost while the
/// client is idle; the probe resends exactly the packet after the server's ack, once per ack, after 1 s; it never fires
/// for a packet that is merely late, never targets ack + 2, and is off with <c>--retail-net</c>, where the lost packet
/// stays lost on ACE. While a sent packet is unacked, acks wait (ACE would let an ack-only packet take a lost data
/// packet's place).
/// </summary>
public sealed class TailProbeTests
{
    private static readonly byte[] Seed = [1, 2, 3, 4];

    // ---- The queue alone ----

    [Fact]
    public void The_packet_after_the_ack_is_probed_once_its_last_send_is_a_second_old()
    {
        var (queue, sent, time, stats) = Queue();
        using (queue)
        {
            queue.SendGameMessage(Message(1), MessageQueue.Weenie);  // packet 2
            queue.SendGameMessage(Message(2), MessageQueue.Weenie);  // packet 3
            sent.Clear();

            time.Advance(TimeSpan.FromMilliseconds(999));
            queue.OnAckSequence(2);
            queue.TransmitPendingResends();
            Assert.Empty(sent);  // packet 3 went out 999 ms ago: not yet

            time.Advance(TimeSpan.FromMilliseconds(1));
            queue.OnAckSequence(2);
            queue.TransmitPendingResends();
            var probe = PacketHeader.Unpack(Assert.Single(sent));
            Assert.Equal(3u, probe.Sequence);
            Assert.True(probe.HasFlag(PacketHeaderFlags.Retransmission));
            Assert.Equal(1, stats.TailProbes);
            Assert.Equal(1, stats.ResendsSent);

            // The probe restarts the second: the next ack, 200 ms later, does not probe again.
            sent.Clear();
            time.Advance(TimeSpan.FromMilliseconds(200));
            queue.OnAckSequence(2);
            queue.TransmitPendingResends();
            Assert.Empty(sent);
        }
    }

    [Fact]
    public void The_probe_targets_ack_plus_one_and_never_beyond()
    {
        var (queue, sent, time, stats) = Queue();
        using (queue)
        {
            for (byte i = 0; i < 4; i++)
            {
                queue.SendGameMessage(Message(i), MessageQueue.Weenie);  // packets 2 to 5
            }

            sent.Clear();
            time.Advance(TimeSpan.FromSeconds(3));
            queue.OnAckSequence(1);
            queue.TransmitPendingResends();
            Assert.Equal([2u], sent.Select(d => PacketHeader.Unpack(d).Sequence));

            sent.Clear();
            time.Advance(TimeSpan.FromSeconds(2));
            queue.OnAckSequence(3);
            queue.TransmitPendingResends();
            Assert.Equal([4u], sent.Select(d => PacketHeader.Unpack(d).Sequence));
            Assert.Equal(2, stats.TailProbes);
        }
    }

    [Fact]
    public void An_older_ack_a_caught_up_ack_and_a_NAKed_packet_are_not_probed()
    {
        var (queue, sent, time, stats) = Queue();
        using (queue)
        {
            queue.SendGameMessage(Message(1), MessageQueue.Weenie);  // packet 2
            queue.SendGameMessage(Message(2), MessageQueue.Weenie);  // packet 3
            queue.SendGameMessage(Message(3), MessageQueue.Weenie);  // packet 4
            sent.Clear();
            time.Advance(TimeSpan.FromSeconds(2));

            queue.OnAckSequence(3);
            queue.OnAckSequence(2);  // reordered: older than the raw maximum, says nothing new
            Assert.Equal(3u, queue.RawServerAck);

            // The server NAKs 4 in the same pass the probe was armed for: one resend, not two.
            Span<byte> nak = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nak, 4);
            queue.OnRetransmitRequest(nak, 1);
            queue.TransmitPendingResends();
            Assert.Equal([4u], sent.Select(d => PacketHeader.Unpack(d).Sequence));
            Assert.Equal(0, stats.TailProbes);

            sent.Clear();
            time.Advance(TimeSpan.FromSeconds(2));
            queue.OnAckSequence(4);  // everything acked
            queue.TransmitPendingResends();
            Assert.Empty(sent);
            Assert.False(queue.HoldsAcks);
        }
    }

    [Fact]
    public void A_NAK_moves_the_watermark_but_not_the_raw_ack()
    {
        var (queue, _, _, _) = Queue();
        using (queue)
        {
            queue.SendGameMessage(Message(1), MessageQueue.Weenie);
            queue.SendGameMessage(Message(2), MessageQueue.Weenie);
            queue.OnAckSequence(1);
            Span<byte> nak = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nak, 3);
            queue.OnRetransmitRequest(nak, 1);

            Assert.Equal(3u, queue.AckWatermark);
            Assert.Equal(1u, queue.RawServerAck);
        }
    }

    [Fact]
    public void Off_it_neither_probes_nor_holds_acks()
    {
        var (queue, sent, time, stats) = Queue(tailProbe: false);
        using (queue)
        {
            queue.SendGameMessage(Message(1), MessageQueue.Weenie);
            sent.Clear();
            time.Advance(TimeSpan.FromSeconds(5));
            queue.OnAckSequence(1);
            queue.TransmitPendingResends();

            Assert.Empty(sent);
            Assert.Equal(0, stats.TailProbes);
            Assert.False(queue.HoldsAcks);
        }
    }

    [Fact]
    public void Acks_are_held_while_a_sent_packet_is_unacked()
    {
        var (queue, _, _, _) = Queue();
        using (queue)
        {
            Assert.False(queue.HoldsAcks);  // nothing sent: an ack-only packet carries sequence 1, which ACE takes

            queue.SendGameMessage(Message(1), MessageQueue.Weenie);
            Assert.True(queue.HoldsAcks);   // no ack yet
            queue.OnAckSequence(1);
            Assert.True(queue.HoldsAcks);   // packet 2 unacked
            queue.OnAckSequence(2);
            Assert.False(queue.HoldsAcks);
        }
    }

    // ---- On the fake server ----

    [Fact]
    public void A_dropped_LoginComplete_with_nothing_after_it_is_delivered_by_one_probe()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var dropped = DropFirstLoginComplete(rig);

        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(1, dropped.Count);
        Assert.False(rig.ServerSession.Teleporting, "the fake never took LoginComplete");
        Assert.Equal(1, rig.Session.Link.TailProbes);
        Assert.Equal(0, rig.Server.Counters.C2SCrcErrors);
        var probe = rig.Harness.Network.Sent.Where(s => IsToServer(rig, s.To) && IsRetransmission(s.Datagram)).Select(s => s.Datagram).Single();
        Assert.Equal(dropped.Sequences[0], PacketHeader.Unpack(probe).Sequence);
    }

    [Fact]
    public void With_retail_net_the_dropped_LoginComplete_stays_lost_on_ACE()
    {
        using var rig = new SessionRig(configure: o => o with { TailProbe = false });
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var dropped = DropFirstLoginComplete(rig);

        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromSeconds(15));

        Assert.Equal(1, dropped.Count);
        Assert.True(rig.ServerSession.Teleporting, "ACE NAKs only a gap of two: nothing should have recovered the packet");
        Assert.Equal(0, rig.Session.Link.TailProbes);
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);
    }

    [Fact]
    public void A_packet_merely_late_by_under_a_second_is_not_probed()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var network = rig.Harness.Network;

        // Time the send 300 ms before the fake's next ack, and hold the packet for 600 ms: the ack arrives while the
        // packet is late, but it went out less than a second before.
        var acks = 0;
        var lastAckAt = TimeSpan.Zero;
        network.Drop = (from, to, datagram) =>
        {
            if (!IsToServer(rig, to) && HasAck(datagram))
            {
                acks++;
                lastAckAt = rig.Harness.Time.Elapsed;
            }

            return false;
        };
        var seen = acks;
        Assert.True(rig.RunUntil(() => acks > seen, TimeSpan.FromSeconds(3)));
        rig.RunFor(lastAckAt + TimeSpan.FromMilliseconds(1_700) - rig.Harness.Time.Elapsed);

        (IPEndPoint From, IPEndPoint To, byte[] Datagram)? held = null;
        var acksWhileHeld = 0;
        network.Drop = (from, to, datagram) =>
        {
            if (IsToServer(rig, to) && held is null && IsLoginComplete(datagram))
            {
                held = (from, to, datagram);
                return true;
            }

            if (!IsToServer(rig, to) && HasAck(datagram) && held is not null)
            {
                acksWhileHeld++;
            }

            return false;
        };
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(600));
        Assert.True(acksWhileHeld > 0, "the fake's ack should have come while the packet was held");
        network.Drop = null;
        network.Send(held!.Value.From, held.Value.To, held.Value.Datagram);
        rig.RunFor(TimeSpan.FromSeconds(5));

        Assert.False(rig.ServerSession.Teleporting);
        Assert.Equal(0, rig.Session.Link.TailProbes);
        Assert.Equal(0, rig.Server.Counters.C2SCrcErrors);
    }

    [Fact]
    public void Two_lost_packets_are_probed_one_ack_at_a_time_never_ack_plus_two()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var network = rig.Harness.Network;
        var events = new List<(bool FromServer, uint Value)>();
        var drops = 0;
        network.Drop = (from, to, datagram) =>
        {
            if (IsToServer(rig, to))
            {
                if (IsRetransmission(datagram))
                {
                    events.Add((false, PacketHeader.Unpack(datagram).Sequence));
                }
                else if (drops < 2 && IsTalk(datagram))
                {
                    drops++;
                    return true;
                }
            }
            else if (HasAck(datagram, out var ack))
            {
                events.Add((true, ack));
            }

            return false;
        };

        rig.Session.SendTalk("first");
        rig.Session.SendTalk("second");
        rig.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(2, drops);
        Assert.Equal(2, rig.Session.Link.TailProbes);
        Assert.Equal(["first", "second"], rig.Sink.Speech.Select(s => s.Text).TakeLast(2));
        uint? lastAck = null;
        var probes = 0;
        foreach (var (fromServer, value) in events)
        {
            if (fromServer)
            {
                lastAck = value;
                continue;
            }

            probes++;
            Assert.Equal(lastAck + 1, value);
        }

        Assert.Equal(2, probes);
        Assert.Equal(0, rig.Server.Counters.C2SCrcErrors);
    }

    [Fact]
    public void Acks_wait_while_a_packet_is_unacked_and_resume_after()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var network = rig.Harness.Network;
        var clientAcks = new List<TimeSpan>();
        var dropped = false;
        network.Drop = (from, to, datagram) =>
        {
            if (IsToServer(rig, to))
            {
                if (!dropped && IsLoginComplete(datagram))
                {
                    dropped = true;
                    return true;
                }

                if (BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) == (uint)PacketHeaderFlags.AckSequence)
                {
                    clientAcks.Add(rig.Harness.Time.Elapsed);
                }
            }

            return false;
        };

        var sentAt = rig.Harness.Time.Elapsed;
        rig.Session.SendLoginComplete();
        Assert.True(rig.RunUntil(() => !rig.ServerSession.Teleporting, TimeSpan.FromSeconds(5)));
        var deliveredAt = rig.Harness.Time.Elapsed;
        rig.RunFor(TimeSpan.FromSeconds(6));

        Assert.DoesNotContain(clientAcks, at => at > sentAt && at < deliveredAt);
        Assert.Contains(clientAcks, at => at > deliveredAt);
        Assert.True(rig.Machine.Connection!.Transport!.Stats.AcksHeld > 0);
    }

    // ---- Helpers ----

    private static (OutboundFlowQueue Queue, List<byte[]> Sent, ManualTimeProvider Time, TransportStats Stats) Queue(bool tailProbe = true)
    {
        var time = new ManualTimeProvider();
        var sent = new List<byte[]>();
        var stats = new TransportStats();
        var queue = new OutboundFlowQueue(new IsaacRandom(Seed), 1, 1, new TransportClock(time), stats, d => sent.Add(d.ToArray()))
        {
            TailProbeEnabled = tailProbe,
        };
        return (queue, sent, time, stats);
    }

    private static byte[] Message(byte tag) => [0xB1, 0xF7, 0, 0, tag, 0, 0, 0];

    private static Dropped DropFirstLoginComplete(SessionRig rig)
    {
        var dropped = new Dropped();
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (IsToServer(rig, to) && dropped.Count == 0 && IsLoginComplete(datagram))
            {
                dropped.Count++;
                dropped.Sequences.Add(PacketHeader.Unpack(datagram).Sequence);
                return true;
            }

            return false;
        };
        return dropped;
    }

    private static bool IsToServer(SessionRig rig, IPEndPoint to) => to.Port == rig.Harness.ServerEndPoint.Port;

    /// <summary>What <see cref="DropFirstLoginComplete"/> dropped.</summary>
    private sealed class Dropped
    {
        public int Count { get; set; }

        public List<uint> Sequences { get; } = [];
    }

    private static bool IsRetransmission(byte[] datagram) =>
        (BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) & (uint)PacketHeaderFlags.Retransmission) != 0;

    private static bool IsLoginComplete(byte[] datagram) => IsAction(datagram, GameActionTypes.LoginComplete);

    private static bool IsTalk(byte[] datagram) => IsAction(datagram, GameActionTypes.Talk);

    /// <summary>A first send (not a resend) of a client packet whose one message is the game action <paramref name="action"/>.</summary>
    private static bool IsAction(byte[] datagram, uint action) =>
        !IsRetransmission(datagram)
        && ClientPacketParser.TryParse(datagram) is { Fragments: [{ Data: var data }] }
        && data.Length >= 12
        && BinaryPrimitives.ReadUInt32LittleEndian(data) == MessageOpcodes.GameAction
        && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) == action;

    private static bool HasAck(byte[] datagram) => HasAck(datagram, out _);

    /// <summary>A server datagram that carries an AckSequence, and its value.</summary>
    private static bool HasAck(byte[] datagram, out uint ack)
    {
        ack = 0;
        if (!PacketCodec.TryParseBorrowed(datagram, out var packet, out _, out _, out _)
            || !packet.Header.HasFlag(PacketHeaderFlags.AckSequence))
        {
            return false;
        }

        ack = packet.Optional.AckSequence;
        return true;
    }
}
