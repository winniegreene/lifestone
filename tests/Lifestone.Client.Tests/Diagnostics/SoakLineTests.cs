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
        HpPct = 87.5, SpPct = 90.0, MpPct = 40.5, HitMax = 21, // M5 7 F16: after log_bytes
        StanceChanges = 3, // M5 7 F25: after hit_max
        TargetDiedMidCast = 2, // M5 7 F28: after stance_changes
        LootKept = 42, LootLeft = 16, FreeSlots = 38, BurdenPct = 61.5, Trips = 3, // M5 7 F29 (D96): the tail
        CorpsesOpened = 55, LootLeftAggro = 7, LootLeftDecayed = 2, // M5 7 loot retry: after trips
        LootBusyWaits = 9, LootLeftBusy = 1, LootLeftUnreached = 4, // M5 7 D123: after loot_left_decayed
        WorthALook = 2, // M5 7 F30 (the owner)
        FreeComponents = 20, FreeKit = 18, FreeReview = 24, SelfEquips = 1, StoneRefills = 2, StoneCaptures = 1, // M5 7 D121, D125
        TurnReversals = 300, TurnFlips = 60, ProgressBacks = 4, Swings = 9, // M5 7 D134: the steering totals
        TexCacheCount = 2100, TexCacheMb = 412.5, TexIdleMb = 96.25, MeshCacheCount = 3300, MeshCacheMb = 88.0, CacheTrims = 7, OrphansDropped = 640, // M5 7 P7d
        Frames = 3540, FrameMeanMs = 16.949, // M6 16 Eperf: last
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
        Assert.Equal(80, pairs.Length); // M6 16 Eperf: frames frame_mean_ms last; P7d: tex_cache_n tex_cache_mb tex_idle_mb mesh_cache_n mesh_cache_mb cache_trims orphans_dropped last; D134: turn_reversals turn_flips progress_backs swings last; D121, D125: free_components free_kit free_review self_equips stone_refills stone_captures last; D123 loot_busy_waits loot_left_busy loot_left_unreached after loot_left_decayed; F30 worth_a_look last; F28 target_died_mid_cast after stance_changes; the 43 of DoD 13 stay in place; M5 7 F16 added hp_pct sp_pct mp_pct hit_max after log_bytes, F25 stance_changes after hit_max; F29 loot_kept loot_left free_slots burden_pct trips after that; the loot retry corpses_opened loot_left_aggro loot_left_decayed after trips
        Assert.Equal(["log_bytes", "hp_pct", "sp_pct", "mp_pct", "hit_max", "stance_changes", "target_died_mid_cast", "loot_kept", "loot_left", "free_slots", "burden_pct", "trips", "corpses_opened", "loot_left_aggro", "loot_left_decayed", "loot_busy_waits", "loot_left_busy", "loot_left_unreached", "worth_a_look", "free_components", "free_kit", "free_review", "self_equips", "stone_refills", "stone_captures", "turn_reversals", "turn_flips", "progress_backs", "swings", "tex_cache_n", "tex_cache_mb", "tex_idle_mb", "mesh_cache_n", "mesh_cache_mb", "cache_trims", "orphans_dropped", "frames", "frame_mean_ms"], pairs[42..]);
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
        Assert.Contains(" loss_pct=0.5 silence_max_s=0.31 phase=InWorld reconnects=0 kills=11 deaths=1 cycles=2 godot_errors=0 log_bytes=224545 hp_pct=87.5 sp_pct=90.0 mp_pct=40.5 hit_max=21 stance_changes=3 target_died_mid_cast=2 loot_kept=42 loot_left=16 free_slots=38 burden_pct=61.5 trips=3 corpses_opened=55 loot_left_aggro=7 loot_left_decayed=2 loot_busy_waits=9 loot_left_busy=1 loot_left_unreached=4 worth_a_look=2 free_components=20 free_kit=18 free_review=24 self_equips=1 stone_refills=2 stone_captures=1 turn_reversals=300 turn_flips=60 progress_backs=4 swings=9 tex_cache_n=2100 tex_cache_mb=412.5 tex_idle_mb=96.25 mesh_cache_n=3300 mesh_cache_mb=88.0 cache_trims=7 orphans_dropped=640 frames=3540 frame_mean_ms=16.949", line, StringComparison.Ordinal); // M5 7 F29 (D96): the tail; P7d; M6 16 Eperf last
        Assert.EndsWith(" orphans_dropped=640 frames=3540 frame_mean_ms=16.949", line, StringComparison.Ordinal); // M6 16 Eperf: the two new fields end the line, so a reader by prefix or by position of the 78 before is not moved
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
        Assert.Equal(80, values.Count);
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
        Assert.All(SoakLineFormat.Todo, t => Assert.Equal(SoakLineFormat.NoAutopilot, t.Todo)); // M5 4 h1: n/a only while no autopilot runs
    }

    [Fact]
    [Trait("Tier", "Timing")] // m6-6-a: an allocation count that flaked once under the parallel suite (4,024 B, M5 item 1's note); passes alone
    public void The_tallies_read_n_a_without_an_autopilot_and_the_autopilots_numbers_with_one()
    {
        var snapshot = Full();
        SoakTallies.Read(snapshot, null);
        Assert.Contains(" kills=n/a deaths=n/a cycles=n/a ", Line(snapshot), StringComparison.Ordinal);
        Assert.Contains(" hp_pct=n/a sp_pct=n/a mp_pct=n/a hit_max=n/a stance_changes=n/a target_died_mid_cast=n/a loot_kept=n/a loot_left=n/a free_slots=n/a burden_pct=n/a trips=n/a corpses_opened=n/a loot_left_aggro=n/a loot_left_decayed=n/a loot_busy_waits=n/a loot_left_busy=n/a loot_left_unreached=n/a worth_a_look=n/a free_components=n/a free_kit=n/a free_review=n/a self_equips=n/a stone_refills=n/a stone_captures=n/a turn_reversals=n/a turn_flips=n/a progress_backs=n/a swings=n/a tex_cache_n=", Line(snapshot), StringComparison.Ordinal); // M5 7 F16, F25; P7d's render-cache fields follow (not the autopilot's)

        using var autopilot = new Lifestone.Client.Session.Autopilot.AutopilotScenario(
            new Lifestone.Client.Session.Autopilot.AutopilotOptions(),
            Lifestone.Client.Session.Scenarios.Journey.JourneyStations.Create(Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Mage, [], null),
            Lifestone.Client.Session.Scenarios.Journey.JourneyScenario.NewContext(Lifestone.Client.Session.Scenarios.Journey.JourneyPlans.Mage, NullLogSink.Instance, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>()));
        autopilot.Totals.Kills = 7;
        autopilot.Totals.Deaths = 1;
        autopilot.CycleTally.Kills = 3;
        SoakTallies.Read(snapshot, autopilot);
        Assert.Contains(" kills=10 deaths=1 cycles=0 ", Line(snapshot), StringComparison.Ordinal);
        Assert.Matches(@" hp_pct=n/a sp_pct=n/a mp_pct=n/a hit_max=0 stance_changes=0 target_died_mid_cast=0 loot_kept=\d+ loot_left=\d+ free_slots=n/a burden_pct=n/a trips=0 corpses_opened=\d+ loot_left_aggro=\d+ loot_left_decayed=\d+ loot_busy_waits=\d+ loot_left_busy=\d+ loot_left_unreached=\d+ worth_a_look=\d+ free_components=(n/a|\d+) free_kit=(n/a|\d+) free_review=(n/a|\d+) self_equips=\d+ stone_refills=\d+ stone_captures=\d+ turn_reversals=\d+ turn_flips=\d+ progress_backs=\d+ swings=\d+ tex_cache_n=", Line(snapshot)); // F29: the loot totals are the process's; no session yet, so no load; no trip yet // M5 7 F16: no session yet, no hit yet; F25: no stance change yet

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            SoakTallies.Read(snapshot, autopilot);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); // a sample allocates nothing
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
        Assert.Equal(50.5, window.Mean); // M6 16 Eperf: the mean of 1 to 100, exact for the whole interval
        Assert.Equal(99.0, window.P99()); // nearest rank: the 99th of 100
        Assert.Equal(50, window.Over50); // 51 to 100
        Assert.Equal(25, window.Over75); // 76 to 100

        window.Reset();

        Assert.Equal(0, window.Frames);
        Assert.Equal(0.0, window.Max);
        Assert.Equal(0.0, window.Mean); // no frame: 0, never a division by none
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
        Assert.Equal(540.0 / 11, window.Mean); // M6 16 Eperf: the mean covers every frame of the interval, the early spike too
    }

    [Fact]
    public void The_default_window_holds_a_minute_at_240_fps()
    {
        Assert.True(new SoakFrameWindow().Capacity >= 60 * 240);
    }

    [Fact]
    [Trait("Tier", "Timing")] // m5-soakline-alloc-flake: a zero-byte allocation count (784 bytes once under the gate's parallel load); the 24-byte cause below is fixed, the rest is load
    public void Sampling_a_frame_allocates_nothing()
    {
        var window = new SoakFrameWindow();
        for (var i = 0; i < 64; i++)
        {
            window.Add(16.6 + i); // the JIT's first calls
        }

        _ = window.P99(); // M6 0 t, the cause of m5-soakline-alloc-flake's 24 bytes: the first Array.Sort over a window with more than one sample builds its comparer once per process (a one-sample window returns before it), so alone, or first in a run, the read below allocated 24 bytes

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
