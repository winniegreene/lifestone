using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>
/// M6 16 Eperf2: the long-frame line. The live climb of 2026-11-18 had frames of 100 to 750 ms a minute whose process CPU time was a small part of their length, at no one place of the log; the play host now names each
/// such frame's parts (<see cref="LongFrameLine"/>), at most one line every few seconds (<see cref="LongFrameGate"/>), from the sums its own calls add (<see cref="FramePhaseTimes"/>).
/// </summary>
public sealed class LongFrameLineTests
{
    [Fact]
    public void A_frame_s_phases_are_what_was_added_since_the_boundary_before()
    {
        var times = new FramePhaseTimes();
        times.Add(FramePhase.Session, 300);
        times.Add(FramePhase.World, 40);
        times.Add(FramePhase.World, 60); // several nodes are one phase
        times.Add(FramePhase.Draw, -5); // a clock that went back adds nothing
        times.Close();
        times.Add(FramePhase.Session, 7); // the next frame's, not yet closed

        Assert.Equal(300, times.Closed(FramePhase.Session));
        Assert.Equal(100, times.Closed(FramePhase.World));
        Assert.Equal(0, times.Closed(FramePhase.Draw));
        Assert.Equal(400, times.ClosedTotal);

        times.Close();
        Assert.Equal(7, times.Closed(FramePhase.Session));
        Assert.Equal(0, times.Closed(FramePhase.World));
    }

    [Fact]
    public void Every_phase_has_a_field_of_the_line()
    {
        Assert.Equal(FramePhaseTimes.Count, Enum.GetValues<FramePhase>().Length);
        Assert.Equal(FramePhaseTimes.Count, LongFrameLine.PhaseFieldNames.Count);
        Assert.Equal(
            ["services_ms", "session_ms", "play_ms", "player_ms", "autopilot_ms", "ui_ms", "world_ms", "draw_ms", "after_draw_ms"],
            LongFrameLine.PhaseFieldNames);
        Assert.Equal(0, (int)FramePhase.Services);
        Assert.Equal(7, (int)FramePhase.Draw);
        Assert.Equal(8, (int)FramePhase.AfterDraw);
    }

    [Fact]
    public void A_frame_at_or_under_the_limit_writes_nothing()
    {
        var gate = new LongFrameGate();
        Assert.False(gate.Offer(16.7, 1_000));
        Assert.False(gate.Offer(50.0, 2_000));
        Assert.False(gate.Offer(double.NaN, 3_000));
        Assert.Equal(0, gate.Skipped);
    }

    [Fact]
    public void Long_frames_write_one_line_every_few_seconds_and_the_rest_are_counted()
    {
        var gate = new LongFrameGate();
        Assert.True(gate.Offer(98.0, 10_000));
        gate.Written();
        Assert.False(gate.Offer(74.0, 10_100));
        Assert.False(gate.Offer(120.0, 12_000));
        Assert.False(gate.Offer(60.0, 14_999));
        Assert.Equal(3, gate.Skipped);
        Assert.Equal(120.0, gate.SkippedMaxMilliseconds);

        Assert.True(gate.Offer(55.0, 15_000)); // five seconds after the line before
        Assert.Equal(3, gate.Skipped); // the line reads them, then says it was written
        gate.Written();
        Assert.Equal(0, gate.Skipped);
        Assert.Equal(0.0, gate.SkippedMaxMilliseconds);
    }

    [Fact]
    public void A_frame_twice_the_longest_since_the_last_line_is_written_at_once()
    {
        // Live 2026-11-18 01:17:40 to 01:17:50 UTC: frames of 125 and 130 ms, then one of 753 ms ten seconds later; inside one window the worse frame must not be the one left out.
        var gate = new LongFrameGate();
        Assert.True(gate.Offer(125.2, 100_000));
        gate.Written();
        Assert.False(gate.Offer(130.0, 100_130));
        Assert.False(gate.Offer(249.0, 101_000)); // not yet twice 130
        Assert.True(gate.Offer(752.6, 102_000));
        Assert.Equal(2, gate.Skipped);
        gate.Written();
        Assert.False(gate.Offer(900.0, 103_000)); // not twice 752.6; a frame left out raises the bar too
        Assert.True(gate.Offer(1_800.0, 104_000));
    }

    [Fact]
    public void The_line_names_each_phase_the_rest_and_what_the_thread_and_the_machine_did()
    {
        var times = new FramePhaseTimes();
        times.Add(FramePhase.Services, 100);
        times.Add(FramePhase.Session, 300);
        times.Add(FramePhase.Play, 400);
        times.Add(FramePhase.Player, 309_270);
        times.Add(FramePhase.Autopilot, 500);
        times.Add(FramePhase.Ui, 1_000);
        times.Add(FramePhase.World, 800);
        times.Add(FramePhase.Draw, 2_630);
        times.Add(FramePhase.AfterDraw, 9_100);
        times.Close();
        var sample = new LongFrameSample
        {
            FrameMs = 325.3,
            Phases = times,
            TicksPerMillisecond = 1_000.0, // a microsecond clock
            LogMs = 0.21,
            LogPrintMs = 0.12,
            LogLines = 8,
            Actions = 4,
            MainCpuMs = 15.625,
            ProcessCpuMs = 15.625,
            MachineBusyPercent = 97.44,
            GcPauseMs = 0.0,
            State = "fighting",
            Stage = "den-fight",
            Station = "den-fight",
            StationStage = "the fight: \"loot\" of a corpse",
            Skipped = 2,
            SkippedMaxMs = 158.827,
        };

        var sb = new StringBuilder();
        LongFrameLine.Append(sb, sample);

        Assert.Equal(
            "long frame ms=325.3 services_ms=0.1 session_ms=0.3 play_ms=0.4 player_ms=309.3 autopilot_ms=0.5 ui_ms=1 world_ms=0.8 draw_ms=2.6 after_draw_ms=9.1 other_ms=1.2 log_ms=0.2 log_print_ms=0.1 log_lines=8 actions=4"
            + " main_cpu_ms=15.6 process_cpu_ms=15.6 machine_busy_pct=97.4 gc_pause_ms=0 state=fighting stage=den-fight station=den-fight station_stage=\"the fight: 'loot' of a corpse\" skipped=2 skipped_max_ms=158.8",
            sb.ToString());
    }

    [Fact]
    public void What_the_host_cannot_read_says_so()
    {
        var sb = new StringBuilder();
        LongFrameLine.Append(sb, new LongFrameSample { FrameMs = 61.0, ProcessCpuMs = 46.875 });

        var line = sb.ToString();
        Assert.StartsWith(LongFrameLine.Token + " ms=61 services_ms=0 ", line, StringComparison.Ordinal);
        Assert.Contains(" other_ms=61 ", line, StringComparison.Ordinal); // no phase timed: the whole frame is unnamed
        Assert.Contains(" main_cpu_ms=n/a process_cpu_ms=46.9 machine_busy_pct=n/a ", line, StringComparison.Ordinal); // not Windows
        Assert.EndsWith(" state=n/a stage=n/a station=n/a station_stage=n/a skipped=0 skipped_max_ms=0", line, StringComparison.Ordinal); // no autopilot
    }

    [Fact]
    public void Phases_that_sum_past_the_frame_leave_no_negative_rest()
    {
        var times = new FramePhaseTimes();
        times.Add(FramePhase.Draw, 70_000);
        times.Close();
        var sb = new StringBuilder();
        LongFrameLine.Append(sb, new LongFrameSample { FrameMs = 60.0, Phases = times, TicksPerMillisecond = 1_000.0 });
        Assert.Contains(" draw_ms=70 after_draw_ms=0 other_ms=0 ", sb.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_frame_s_bookkeeping_allocates_nothing()
    {
        var times = new FramePhaseTimes();
        var gate = new LongFrameGate();
        times.Add(FramePhase.Session, 1);
        times.Close();
        _ = gate.Offer(10.0, 0.0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 10_000; frame++)
        {
            times.Add(FramePhase.Session, 300);
            times.Add(FramePhase.Player, 900);
            times.Add(FramePhase.Draw, 2_000);
            times.Close();
            _ = gate.Offer(16.7, frame * 16.7);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
