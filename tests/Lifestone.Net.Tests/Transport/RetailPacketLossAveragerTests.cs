// Ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Transport/RetailPacketLossAveragerTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Transport;
// the clock is a tests/Shared ManualTimeProvider advanced in seconds instead of a captured long at 10 ticks per second.

using Lifestone.Net.Transport;
using Lifestone.Tests.Shared;

namespace Lifestone.Net.Tests.Transport;

public sealed class RetailPacketLossAveragerTests
{
    [Fact]
    public void Sweep_BeforeRetailHeartbeat_DoesNotPublishPartialSample()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);
        var stats = new TransportStats { PacketsSent = 5, NakIdsSent = 1 };
        var sut = new RetailPacketLossAverager(clock, stats);

        stats.PacketsSent += 10;
        stats.NakIdsSent += 2;
        time.Advance(TimeSpan.FromSeconds(1.9));
        sut.Sweep(time.GetTimestamp(), stats);

        Assert.Equal(0d, sut.Percentage);
    }

    [Fact]
    public void Sweep_UsesRetailNakPlusRetransmitOverReceivedPlusSentFormula()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);
        var stats = new TransportStats();
        var sut = new RetailPacketLossAverager(clock, stats);

        stats.PacketsSent = 60;
        stats.PacketsReceived = 40;
        stats.NakIdsSent = 2;
        stats.ResendsSent = 3;
        time.Advance(TimeSpan.FromSeconds(2));
        sut.Sweep(time.GetTimestamp(), stats);

        Assert.Equal(5d, sut.Percentage);
    }

    [Fact]
    public void Sweep_EvictsTheOldestSampleAfterFortyHeartbeats()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);
        var stats = new TransportStats();
        var sut = new RetailPacketLossAverager(clock, stats);

        stats.PacketsSent = 10;
        stats.NakIdsSent = 10;
        time.Advance(TimeSpan.FromSeconds(2));
        sut.Sweep(time.GetTimestamp(), stats);

        for (int i = 0; i < RetailPacketLossAverager.WindowSize; i++)
        {
            stats.PacketsSent += 10;
            time.Advance(TimeSpan.FromSeconds(2));
            sut.Sweep(time.GetTimestamp(), stats);
        }

        Assert.Equal(0d, sut.Percentage);
    }

    [Fact]
    public void Sweep_ZeroTrafficReportsZero()
    {
        var time = new ManualTimeProvider();
        var clock = new TransportClock(time);
        var stats = new TransportStats();
        var sut = new RetailPacketLossAverager(clock, stats);

        time.Advance(TimeSpan.FromSeconds(2));
        sut.Sweep(time.GetTimestamp(), stats);

        Assert.Equal(0d, sut.Percentage);
    }
}
