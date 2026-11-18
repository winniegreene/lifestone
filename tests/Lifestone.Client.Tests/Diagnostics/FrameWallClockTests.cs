using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>
/// M6 14 hub plan: the slow-frame line and the soak line measure a frame by the wall clock (<see cref="FrameWallClock"/>), so a frame the game's thread holds for seconds reads seconds (live 2026-11-16 16:54:52 UTC: the first
/// plan in the Town Network held the thread 7.6 s, then 4.5 s, and the engine's delta read 130.9 ms).
/// </summary>
public sealed class FrameWallClockTests
{
    private long _ticks;

    private FrameWallClock Clock() => new(() => _ticks, 1_000_000); // a microsecond clock

    [Fact]
    public void A_frame_that_follows_the_one_before_reads_the_wall_time_between_them_not_the_delta()
    {
        var clock = Clock();
        _ticks = 5_000_000;
        _ = clock.Next(100, 16.7);
        _ticks += 12_060_000; // the game's thread held 12.060 s (live 16:54:52.626 to 16:55:04.686 UTC)
        Assert.Equal(12_060.0, clock.Next(101, 130.9), 6);
        _ticks += 16_667;
        Assert.Equal(16.667, clock.Next(102, 16.7), 6);
    }

    [Fact]
    public void The_first_frame_reads_the_engine_s_delta()
    {
        var clock = Clock();
        _ticks = 9_000_000;
        Assert.Equal(16.7, clock.Next(1, 16.7));
    }

    [Fact]
    public void A_frame_after_frames_the_node_did_not_process_reads_the_engine_s_delta()
    {
        var clock = Clock();
        _ = clock.Next(10, 16.7);
        _ticks += 60_000_000; // a minute hidden or paused: the node processed no frame
        Assert.Equal(16.7, clock.Next(3610, 16.7));
        _ticks += 20_000;
        Assert.Equal(20.0, clock.Next(3611, 16.7), 6);
    }
}
