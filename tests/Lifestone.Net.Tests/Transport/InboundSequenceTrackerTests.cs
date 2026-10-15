// Partly ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Transport/InboundSequenceTrackerTests.cs
// (MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Transport; the
// ten tests that drive the tracker alone are taken unchanged; the four that drive OpenAC's WorldSession and its ACE
// model are not (the N4 fake server and the N5 session tests cover that ground); the allocation test runs in the shared
// non-parallel allocation collection.

using System.Buffers.Binary;
using Lifestone.Client.Tests;
using Lifestone.Net.Crypto;
using Lifestone.Net.Transport;

namespace Lifestone.Net.Tests.Transport;

[Collection(AllocationMeasurementCollection.Name)]
public sealed class InboundSequenceTrackerTests
{
    private const uint Seed = 0x5EED5EEDu;

    [Fact]
    public void GapWalk_ParksTheMissingKey_LaterPacketsAndLateArrivalDecode()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        uint w11 = shadow.Next();
        uint w12 = shadow.Next();
        uint w13 = shadow.Next();
        uint w14 = shadow.Next();
        uint w15 = shadow.Next();

        // In-order packets draw in sequence order.
        Assert.Equal(w10, Admitted(tracker, 10));
        Assert.Equal(w11, Admitted(tracker, 11));

        Assert.Equal(w13, Admitted(tracker, 13));
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(1, stats.KeysParked);
        Assert.Equal(13u, tracker.HighestIdReceived);

        // 14 keeps flowing on the aligned stream.
        Assert.Equal(w14, Admitted(tracker, 14));

        Assert.Equal(w12, Admitted(tracker, 12));
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(14u, tracker.HighestIdReceived);

        // And the next fresh packet takes the next fresh word.
        Assert.Equal(w15, Admitted(tracker, 15));
        Assert.Equal(0, stats.InboundDupsDropped);
        Assert.Equal(0, stats.InboundSanityDrops);
    }


    [Fact]
    public void Duplicate_NeverNakked_DropsAtZeroKeystreamCost()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        uint w11 = shadow.Next();

        Assert.Equal(w10, Admitted(tracker, 10));

        // A duplicate of the already-decoded 10: dropped BEFORE any
        // keystream access (pre-N2 this burned one word and shifted the
        // stream).
        InboundSequenceTracker.Admission dup = tracker.Admit(10, encrypted: true);
        Assert.True(dup.Drop);
        Assert.Equal(1, stats.InboundDupsDropped);

        // The shadow position is unchanged: 11 draws the very next word.
        Assert.Equal(w11, Admitted(tracker, 11));
    }

    [Fact]
    public void ChecksumFailureRepark_TheRetransmissionDecodesWithTheSameKey()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        uint w11 = shadow.Next();

        InboundSequenceTracker.Admission corrupt =
            tracker.Admit(10, encrypted: true);
        Assert.False(corrupt.Drop);
        Assert.Equal(w10, corrupt.VerifyKey);
        tracker.ReparkKey(10, w10, corrupt.VerifyKeyDrawOrder);
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(1, stats.KeysParked);

        // The byte-identical retransmission decodes with the SAME word.
        Assert.Equal(w10, Admitted(tracker, 10));
        Assert.Equal(0, tracker.NakCount);

        // Alignment held throughout.
        Assert.Equal(w11, Admitted(tracker, 11));
    }


    [Fact]
    public void Cleartext_AtHighestPlusOne_NaksItsOwnBorrowedId()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        uint w11 = shadow.Next();

        InboundSequenceTracker.Admission cleartext =
            tracker.Admit(10, encrypted: false);
        Assert.False(cleartext.Drop);
        Assert.Null(cleartext.VerifyKey);
        Assert.Equal(10u, tracker.HighestIdReceived);
        Assert.Equal(1, tracker.NakCount);
        Assert.Equal(1, stats.KeysParked);

        // The real encrypted 10 arrives later: parked key, exact word.
        Assert.Equal(w10, Admitted(tracker, 10));
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(w11, Admitted(tracker, 11));
    }

    [Fact]
    public void Cleartext_AtHighest_NoNak_NoKey_NoWatermarkChange()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        uint w11 = shadow.Next();

        Assert.Equal(w10, Admitted(tracker, 10));

        InboundSequenceTracker.Admission ack =
            tracker.Admit(10, encrypted: false);
        Assert.False(ack.Drop);
        Assert.Null(ack.VerifyKey);
        Assert.Equal(10u, tracker.HighestIdReceived);
        Assert.Equal(0, tracker.NakCount);
        Assert.Equal(0, stats.KeysParked);

        Assert.Equal(w11, Admitted(tracker, 11));
    }


    [Fact]
    public void SanityWindow_HighestPlus0x7FFF_Accepted_OnePastIt_Dropped()
    {
        (InboundSequenceTracker accepted, _) =
            CreateTracker(initialWatermark: 100);
        InboundSequenceTracker.Admission atBoundary =
            accepted.Admit(100u + 0x7FFFu, encrypted: true);
        Assert.False(atBoundary.Drop);
        Assert.Equal(100u + 0x7FFFu, accepted.HighestIdReceived);

        (InboundSequenceTracker dropped, TransportStats stats) =
            CreateTracker(initialWatermark: 100);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w101 = shadow.Next();
        InboundSequenceTracker.Admission pastBoundary =
            dropped.Admit(100u + 0x8000u, encrypted: true);
        Assert.True(pastBoundary.Drop);
        Assert.Equal(1, stats.InboundSanityDrops);
        Assert.Equal(100u, dropped.HighestIdReceived);
        Assert.Equal(0, dropped.NakCount);
        Assert.Equal(w101, Admitted(dropped, 101));
    }

    [Fact]
    public void SanityWindow_IsWrapSafe()
    {
        // Watermark near the 32-bit wrap: the horizon lands past 0.
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 0xFFFFFF00u);

        // watermark + 0x8000 wraps to 0x7F00 — still one past the horizon,
        // still dropped.
        InboundSequenceTracker.Admission pastBoundary =
            tracker.Admit(unchecked(0xFFFFFF00u + 0x8000u), encrypted: true);
        Assert.True(pastBoundary.Drop);
        Assert.Equal(1, stats.InboundSanityDrops);

        InboundSequenceTracker.Admission postWrap =
            tracker.Admit(3u, encrypted: true);
        Assert.False(postWrap.Drop);
        Assert.Equal(3u, tracker.HighestIdReceived);
    }

    [Fact]
    public void GapWalk_SkipsSequenceZero_AcrossTheWrap()
    {
        (InboundSequenceTracker tracker, _) =
            CreateTracker(initialWatermark: 0xFFFFFFFEu);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint wMax = shadow.Next();
        uint w1 = shadow.Next();    // parked for 1 (0 skipped between)
        uint w2 = shadow.Next();    // the arriving packet's own key

        Assert.Equal(w2, Admitted(tracker, 2));
        Assert.Equal(2, tracker.NakCount);

        var naks = new List<uint>();
        tracker.CopyNakkedSequencesAscending(naks);
        Assert.Equal(new uint[] { 1u, 0xFFFFFFFFu }, naks);

        // Both parked keys decode their late arrivals.
        Assert.Equal(wMax, Admitted(tracker, 0xFFFFFFFFu));
        Assert.Equal(w1, Admitted(tracker, 1));
        Assert.Equal(0, tracker.NakCount);
    }


    [Fact]
    public void RejectRetransmit_RemovesIds_AndTheStreamStaysAligned()
    {
        (InboundSequenceTracker tracker, TransportStats stats) =
            CreateTracker(initialWatermark: 9);
        IsaacRandom shadow = MakeIsaac(Seed);
        uint w10 = shadow.Next();
        _ = shadow.Next();          // w11 — parked, then abandoned
        _ = shadow.Next();          // w12 — parked, then abandoned
        uint w13 = shadow.Next();
        uint w14 = shadow.Next();

        Assert.Equal(w10, Admitted(tracker, 10));
        Assert.Equal(w13, Admitted(tracker, 13)); // parks 11 + 12
        Assert.Equal(2, tracker.NakCount);

        // The server answers RejectRetransmit [11, 12]: silent abandonment.
        Span<byte> ids = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(ids, 11u);
        BinaryPrimitives.WriteUInt32LittleEndian(ids.Slice(4), 12u);
        tracker.OnRejectRetransmit(ids, count: 2);
        Assert.Equal(0, tracker.NakCount);

        // Alignment holds: the words were already drawn in sequence order.
        Assert.Equal(w14, Admitted(tracker, 14));

        Assert.True(tracker.Admit(11, encrypted: true).Drop);
        Assert.Equal(1, stats.InboundDupsDropped);
    }

    // =====================================================================
    // Zero-alloc steady state
    // =====================================================================

    [Fact]
    public void WarmAdmit_NoGap_AllocatesNothing()
    {
        (InboundSequenceTracker tracker, _) = CreateTracker(initialWatermark: 1);
        uint sequence = 2;
        for (int i = 0; i < 128; i++)
            _ = tracker.Admit(sequence++, encrypted: true);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
            _ = tracker.Admit(sequence++, encrypted: true);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static (InboundSequenceTracker Tracker, TransportStats Stats)
        CreateTracker(uint initialWatermark)
    {
        var stats = new TransportStats();
        return (
            new InboundSequenceTracker(MakeIsaac(Seed), stats, initialWatermark),
            stats);
    }

    /// <summary>Admit an encrypted packet that must NOT drop; returns the
    /// verify key the tracker handed out.</summary>
    private static uint Admitted(InboundSequenceTracker tracker, uint sequence)
    {
        InboundSequenceTracker.Admission admission =
            tracker.Admit(sequence, encrypted: true);
        Assert.False(admission.Drop);
        Assert.NotNull(admission.VerifyKey);
        return admission.VerifyKey!.Value;
    }

    private static IsaacRandom MakeIsaac(uint seed)
    {
        Span<byte> seedBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(seedBytes, seed);
        return new IsaacRandom(seedBytes);
    }
}
