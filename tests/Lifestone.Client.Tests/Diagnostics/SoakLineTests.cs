using System.Text;
using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 0 c3: the <c>[soak]</c> line's engine-free parts: the formatter, with every field and the <c>n/a</c> forms, and the frame window that is sampled each frame without allocating.</summary>
public sealed partial class SoakLineTests
{
    [GeneratedRegex(@"(?<name>[a-z0-9_]+)=(?<value>\S+)")]
    private static partial Regex Pair();

    private static SoakSnapshot Full() => new()
    {
        WsMb = 1462.25, PrivateMb = 2112.5, ManagedMb = 188.125, CommittedMb = 197.0, FragMb = 12.5, LohMb = 40.0,
        Gen0 = 5447, Gen1 = 46, Gen2 = 19, GcPauseMs = 1892.349, StaticMb = 176.0, VideoMb = 337.5, TexMb = 120.0,
        Nodes = 9000, Orphans = 3, Resources = 1500, Objects = 120, Avatars = 80, InCell = 64, LightRows = 12, CullPending = 4, Culled = 310,
        FrameMaxMs = 41.5, FrameP99Ms = 9.25, Over50 = 0, Over75 = 0, TickMs = 0.4, PumpMsgs = 7,
        Sent = 1000, Recv = 1200, Resends = 2, Naks = 1, Dup = 0, Gaps = 0, LossPct = 0.5, SilenceMaxS = 0.31,
        Phase = "InWorld", Reconnects = 0, Kills = 11, Deaths = 1, Cycles = 2, GodotErrors = 0, LogBytes = 224545,
    };

    private static string Line(SoakSnapshot snapshot)
    {
        var sb = new StringBuilder();
        SoakLineFormat.Append(sb, snapshot);
        return sb.ToString();
    }

    [Fact]
    public void Every_field_of_the_spec_prints_once_in_order()
    {
        var pairs = Pair().Matches(Line(Full())).Select(m => m.Groups["name"].Value).ToArray();

        Assert.Equal(SoakLineFormat.FieldNames, pairs);
        Assert.Equal(43, pairs.Length);
        // DoD 13's fields, by name (m5-first-dungeon.md section 2): memory, collections, objects, frames, link, phase, tallies, errors, log size.
        foreach (var name in new[] { "ws_mb", "private_mb", "managed_mb", "committed_mb", "frag_mb", "loh_mb", "gen0", "gen1", "gen2", "gc_pause_ms", "static_mb", "video_mb", "tex_mb", "nodes", "orphans", "resources", "objects", "avatars", "in_cell", "light_rows", "cull_pending", "culled", "frame_max_ms", "frame_p99_ms", "over_50", "over_75", "tick_ms", "pump_msgs", "sent", "recv", "resends", "naks", "dup", "gaps", "loss_pct", "silence_max_s", "phase", "reconnects", "kills", "deaths", "cycles", "godot_errors", "log_bytes" })
        {
            Assert.Contains(name, pairs);
        }
    }

    [Fact]
    public void Values_print_with_the_invariant_culture_and_whole_numbers_stay_whole()
    {
        var line = Line(Full());

        Assert.StartsWith("ws_mb=1462.25 private_mb=2112.5 managed_mb=188.125 ", line, StringComparison.Ordinal);
        Assert.Contains(" gen0=5447 gen1=46 gen2=19 gc_pause_ms=1892.349 ", line, StringComparison.Ordinal);
        Assert.Contains(" frame_max_ms=41.5 frame_p99_ms=9.25 over_50=0 over_75=0 ", line, StringComparison.Ordinal);
        Assert.Contains(" loss_pct=0.5 silence_max_s=0.31 phase=InWorld reconnects=0 kills=11 deaths=1 cycles=2 godot_errors=0 log_bytes=224545", line, StringComparison.Ordinal);
        Assert.Contains(" in_cell=64 light_rows=12 cull_pending=4 culled=310 frame_max_ms=", line, StringComparison.Ordinal); // 0-R11: the cull's counts sit with the object fields
        Assert.DoesNotContain("n/a", line, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", line, StringComparison.Ordinal); // single spaces between pairs
        using (new CultureScope("de-DE"))
        {
            Assert.Contains("ws_mb=1462.25 ", Line(Full()), StringComparison.Ordinal); // a comma decimal separator would break the line's parsers
        }
    }

    [Fact]
    public void A_field_with_no_source_reads_n_a_and_the_rest_are_kept()
    {
        var line = Line(new SoakSnapshot { WsMb = 10.0, Phase = null });

        var values = Pair().Matches(line).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
        Assert.Equal(43, values.Count);
        Assert.Equal("10.0", values["ws_mb"]);
        foreach (var (name, value) in values.Where(p => p.Key != "ws_mb"))
        {
            Assert.True(value == "n/a", $"{name} should read n/a, read {value}");
        }
    }

    [Fact]
    public void The_driver_tallies_and_a_missing_session_read_n_a_with_a_named_todo()
    {
        var snapshot = Full();
        snapshot.Kills = snapshot.Deaths = snapshot.Cycles = null;
        snapshot.Phase = null;
        snapshot.Sent = snapshot.Recv = snapshot.Resends = snapshot.Naks = snapshot.Dup = snapshot.Gaps = null;
        snapshot.LossPct = null;
        var line = Line(snapshot);

        Assert.Contains(" phase=n/a reconnects=0 kills=n/a deaths=n/a cycles=n/a godot_errors=0 ", line, StringComparison.Ordinal);
        Assert.Contains(" sent=n/a recv=n/a resends=n/a naks=n/a dup=n/a gaps=n/a loss_pct=n/a ", line, StringComparison.Ordinal);
        Assert.Equal(["kills", "deaths", "cycles"], SoakLineFormat.Todo.Select(t => t.Field));
        Assert.All(SoakLineFormat.Todo, t => Assert.Equal("TODO(m5-soak-tallies)", t.Todo));
    }

    [Fact]
    public void A_number_that_is_not_finite_reads_n_a()
    {
        var snapshot = Full();
        snapshot.TickMs = double.NaN;
        snapshot.LossPct = double.PositiveInfinity;

        var line = Line(snapshot);

        Assert.Contains(" tick_ms=n/a ", line, StringComparison.Ordinal);
        Assert.Contains(" loss_pct=n/a ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_is_reused_and_the_category_is_soak()
    {
        var sb = new StringBuilder();
        SoakLineFormat.Append(sb, Full());
        var first = sb.ToString();
        sb.Clear();
        SoakLineFormat.Append(sb, Full());

        Assert.Equal(first, sb.ToString());
        Assert.Equal("soak", SoakLineFormat.Category);
    }

    [Fact]
    public void The_frame_window_reads_the_max_the_percentile_and_the_over_counts()
    {
        var window = new SoakFrameWindow(1000);
        for (var i = 1; i <= 100; i++)
        {
            window.Add(i); // 1 ms to 100 ms
        }

        Assert.Equal(100, window.Frames);
        Assert.Equal(100.0, window.Max);
        Assert.Equal(99.0, window.P99()); // nearest rank: the 99th of 100
        Assert.Equal(50, window.Over50); // 51 to 100
        Assert.Equal(25, window.Over75); // 76 to 100

        window.Reset();

        Assert.Equal(0, window.Frames);
        Assert.Equal(0.0, window.Max);
        Assert.Equal(0.0, window.P99());
        Assert.Equal(0, window.Over50);
        Assert.Equal(0, window.Over75);
    }

    [Fact]
    public void A_full_ring_keeps_the_last_frames_for_the_percentile_and_every_frame_for_the_counts()
    {
        var window = new SoakFrameWindow(10);
        window.Add(500.0); // an early spike
        for (var i = 0; i < 10; i++)
        {
            window.Add(4.0);
        }

        Assert.Equal(500.0, window.Max); // the maximum and the counts cover the whole interval
        Assert.Equal(1, window.Over75);
        Assert.Equal(4.0, window.P99()); // the percentile reads the last 10
        Assert.Equal(11, window.Frames);
    }

    [Fact]
    public void The_default_window_holds_a_minute_at_240_fps()
    {
        Assert.True(new SoakFrameWindow().Capacity >= 60 * 240);
    }

    [Fact]
    public void Sampling_a_frame_allocates_nothing()
    {
        var window = new SoakFrameWindow();
        window.Add(16.6); // the JIT's first call
        _ = window.P99();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200_000; i++)
        {
            window.Add(i % 97);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(0, ReadP99Allocation(window)); // the once-a-minute read sorts in the buffer made at construction
    }

    private static long ReadP99Allocation(SoakFrameWindow window)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = window.P99();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name) => System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
    }
}
