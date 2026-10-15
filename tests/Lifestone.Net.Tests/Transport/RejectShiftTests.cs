using System.Buffers.Binary;
using System.Net;
using Lifestone.Net.Crypto;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Transport;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Transport;

/// <summary>
/// Keys and rejects (m2-plan §3.3 N8). ACE answers a NAK for a packet it no longer holds with a cleartext
/// RejectRetransmit that takes a sequence but no ISAAC key, so the client must hand that sequence's parked key to the
/// next packet (<see cref="InboundSequenceTracker.OnCleartextRejectSequence"/>): on the fake server, a reject in order
/// and a reject that arrives after the packets behind it both leave every later packet verifying. N8's rollback
/// (<see cref="InboundSequenceTracker.Reject"/>): a packet whose checksum fails moves nothing, so a corrupt sequence a
/// few thousand ahead neither moves the window nor parks a key.
/// </summary>
public sealed class RejectShiftTests
{
    private const uint Seed = 0x5EED5EEDu;

    // ---- The tracker alone ----

    [Fact]
    public void A_failed_packet_far_ahead_moves_nothing_and_the_real_ones_still_verify()
    {
        var (tracker, stats) = Tracker(initialWatermark: 9);
        var shadow = Isaac(Seed);
        var w10 = shadow.Next();
        var w11 = shadow.Next();
        var w12 = shadow.Next();

        Assert.Equal(w10, Admit(tracker, 10, out _));

        // A corrupt copy of a packet whose sequence reads 5,000: its admission parks 4,989 keys, then its checksum fails.
        var bogus = tracker.Admit(5_000, encrypted: true);
        Assert.True(bogus.Advanced);
        tracker.Reject(5_000, bogus);
        Assert.Equal(10u, tracker.HighestIdReceived);
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(1, stats.AdmissionsRejected);

        // The real 11 and 12 take the keys they would have taken anyway.
        Assert.Equal(w11, Admit(tracker, 11, out _));
        Assert.Equal(w12, Admit(tracker, 12, out _));
        Assert.Equal(0, tracker.NakCount);
    }

    [Fact]
    public void A_failed_packet_just_ahead_gives_back_the_gap_it_parked_in_draw_order()
    {
        var (tracker, _) = Tracker(initialWatermark: 9);
        var shadow = Isaac(Seed);
        var w10 = shadow.Next();
        var w11 = shadow.Next();
        var w12 = shadow.Next();
        var w13 = shadow.Next();

        var bogus = tracker.Admit(12, encrypted: true);   // parks 10 and 11, takes 12's key
        tracker.Reject(12, bogus);

        Assert.Equal(w10, Admit(tracker, 10, out _));
        Assert.Equal(w12, Admit(tracker, 12, out _));     // parks 11 with w11 again
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(w11, Admit(tracker, 11, out _));     // the parked key
        Assert.Equal(w13, Admit(tracker, 13, out _));
    }

    [Fact]
    public void A_failed_cleartext_packet_ahead_moves_nothing()
    {
        var (tracker, _) = Tracker(initialWatermark: 9);
        var shadow = Isaac(Seed);
        var w10 = shadow.Next();

        var bogus = tracker.Admit(20, encrypted: false);  // parks 10 to 20
        Assert.True(bogus.Advanced);
        tracker.Reject(20, bogus);

        Assert.Equal(9u, tracker.HighestIdReceived);
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(w10, Admit(tracker, 10, out _));
    }

    [Fact]
    public void A_failed_late_packet_gets_its_parked_key_back_as_before()
    {
        var (tracker, _) = Tracker(initialWatermark: 9);
        var shadow = Isaac(Seed);
        var w10 = shadow.Next();

        Admit(tracker, 11, out _);                            // parks 10
        var corrupt = tracker.Admit(10, encrypted: true);
        Assert.False(corrupt.Advanced);
        tracker.Reject(10, corrupt);
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(w10, Admit(tracker, 10, out _));
    }

    [Fact]
    public void A_cleartext_reject_hands_its_parked_key_to_the_next_packet()
    {
        var (tracker, stats) = Tracker(initialWatermark: 9);
        var shadow = Isaac(Seed);
        var w10 = shadow.Next();
        var w11 = shadow.Next();

        // ACE: 10 is the reject (no key), so 11 is encrypted with the first word, 12 with the second.
        tracker.Admit(10, encrypted: false);
        tracker.OnCleartextRejectSequence(10);
        Assert.Equal(1, stats.RejectWordsReclaimed);
        Assert.Equal(w10, Admit(tracker, 11, out _));
        Assert.Equal(w11, Admit(tracker, 12, out _));
    }

    // ---- On the fake server ----

    [Fact]
    public void An_uncached_NAK_is_rejected_and_the_packets_after_the_reject_verify()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var rejects = 0;
        var lost = LoseOneServerPacketAndForgetIt(rig, onReject: () => rejects++);

        rig.Session.SendTalk("one");
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        rig.Session.SendTalk("two");
        rig.RunFor(TimeSpan.FromSeconds(8));
        rig.Session.SendTalk("three");
        rig.RunFor(TimeSpan.FromSeconds(2));

        Assert.True(lost.Dropped, "no server packet was dropped");
        Assert.Equal(1, rejects);
        Assert.Equal(["two", "three"], rig.Sink.Speech.Select(s => s.Text).TakeLast(2));
        Assert.DoesNotContain("one", rig.Sink.Speech.Select(s => s.Text));
        Assert.Equal(0, rig.Session.Link.ChecksumFailures);
        Assert.Equal(1, rig.Session.Link.GapSkips);
        Assert.Equal(1, rig.Machine.Connection!.Transport!.Stats.RejectWordsReclaimed);
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);
    }

    [Fact]
    public void A_reject_that_arrives_after_the_packets_behind_it_still_shifts_their_keys()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.RunFor(TimeSpan.FromSeconds(3));
        var network = rig.Harness.Network;
        var lost = LoseOneServerPacketAndForgetIt(rig, onReject: null);

        // Hold the reject back until two encrypted server packets have overtaken it.
        (IPEndPoint From, IPEndPoint To, byte[] Datagram)? held = null;
        var overtaken = 0;
        var inner = network.Drop!;
        network.Drop = (from, to, datagram) =>
        {
            if (inner(from, to, datagram))
            {
                return true;
            }

            if (to.Port == rig.Harness.ServerEndPoint.Port)
            {
                return false;
            }

            var flags = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
            if (held is null && (flags & (uint)PacketHeaderFlags.RejectRetransmit) != 0)
            {
                held = (from, to, datagram);
                return true;
            }

            if (held is not null && (flags & (uint)PacketHeaderFlags.EncryptedChecksum) != 0 && ++overtaken == 2)
            {
                network.Drop = null;
                rig.Harness.Time.CreateTimer(_ => network.Send(held.Value.From, held.Value.To, held.Value.Datagram), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }

            return false;
        };

        rig.Session.SendTalk("one");
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        rig.Session.SendTalk("two");
        Assert.True(rig.RunUntil(() => held is not null, TimeSpan.FromSeconds(5)), "the fake sent no reject");
        rig.Session.SendTalk("three");
        rig.RunFor(TimeSpan.FromMilliseconds(100));
        rig.Session.SendTalk("four");
        rig.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(lost.Dropped);
        Assert.Equal(2, overtaken);
        Assert.Equal(["two", "three", "four"], rig.Sink.Speech.Select(s => s.Text).TakeLast(3));
        Assert.True(rig.Session.Link.ChecksumFailures > 0, "the overtaking packets should have failed before the reject came");
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);

        // After the shift, new packets verify again.
        var failures = rig.Session.Link.ChecksumFailures;
        rig.Session.SendTalk("five");
        rig.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal("five", rig.Sink.Speech[^1].Text);
        Assert.Equal(failures, rig.Session.Link.ChecksumFailures);
    }

    // ---- Helpers ----

    /// <summary>
    /// Drops the next encrypted server packet that carries a message, and makes the fake forget its resend cache when
    /// the one after it passes, so the client's NAK for the lost one is rejected.
    /// </summary>
    private static Loss LoseOneServerPacketAndForgetIt(SessionRig rig, Action? onReject)
    {
        var loss = new Loss();
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (to.Port == rig.Harness.ServerEndPoint.Port)
            {
                return false;
            }

            var flags = BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4));
            if ((flags & (uint)PacketHeaderFlags.RejectRetransmit) != 0)
            {
                onReject?.Invoke();
            }

            var carriesMessage = (flags & (uint)PacketHeaderFlags.BlobFragments) != 0 && (flags & (uint)PacketHeaderFlags.Retransmission) == 0;
            if (!carriesMessage)
            {
                return false;
            }

            if (!loss.Dropped)
            {
                loss.Dropped = true;
                return true;
            }

            if (!loss.Forgotten)
            {
                loss.Forgotten = true;
                rig.ServerSession.ForgetCachedPackets();
            }

            return false;
        };
        return loss;
    }

    private static (InboundSequenceTracker Tracker, TransportStats Stats) Tracker(uint initialWatermark)
    {
        var stats = new TransportStats();
        return (new InboundSequenceTracker(Isaac(Seed), stats, initialWatermark), stats);
    }

    private static uint Admit(InboundSequenceTracker tracker, uint sequence, out InboundSequenceTracker.Admission admission)
    {
        admission = tracker.Admit(sequence, encrypted: true);
        Assert.False(admission.Drop);
        return admission.VerifyKey!.Value;
    }

    private static IsaacRandom Isaac(uint seed)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, seed);
        return new IsaacRandom(bytes);
    }

    private sealed class Loss
    {
        public bool Dropped { get; set; }

        public bool Forgotten { get; set; }
    }
}
