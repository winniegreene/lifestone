using System.Globalization;
using System.Text;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// One reading of everything the <c>[soak]</c> line reports (M5 item 0 c3; the fields are DoD 13's, the sources
/// cache/m5/longrun-risks.md section 4's table). A field the game cannot read yet stays null and prints as <c>n/a</c>
/// (<see cref="SoakLineFormat.Todo"/> names why). One instance is reused for every line.
/// </summary>
public sealed class SoakSnapshot
{
    /// <summary>Process working set, MiB.</summary>
    public double? WsMb { get; set; }

    /// <summary>Process private bytes, MiB.</summary>
    public double? PrivateMb { get; set; }

    /// <summary>Managed heap in use (<c>GC.GetTotalMemory</c>), MiB.</summary>
    public double? ManagedMb { get; set; }

    /// <summary>Memory the collector has committed, MiB.</summary>
    public double? CommittedMb { get; set; }

    /// <summary>Fragmented bytes of the last collection, MiB.</summary>
    public double? FragMb { get; set; }

    /// <summary>The large object heap after the last collection, MiB.</summary>
    public double? LohMb { get; set; }

    /// <summary>Collections of generation 0 since the process began (cumulative; a gen 2 collection counts in all three).</summary>
    public long? Gen0 { get; set; }

    /// <summary>Collections of generation 1 since the process began.</summary>
    public long? Gen1 { get; set; }

    /// <summary>Collections of generation 2 since the process began.</summary>
    public long? Gen2 { get; set; }

    /// <summary>Total time the process was paused by collections, milliseconds (cumulative).</summary>
    public double? GcPauseMs { get; set; }

    /// <summary>Godot's static memory, MiB.</summary>
    public double? StaticMb { get; set; }

    /// <summary>Video memory in use, MiB.</summary>
    public double? VideoMb { get; set; }

    /// <summary>Texture memory in use, MiB.</summary>
    public double? TexMb { get; set; }

    /// <summary>Godot objects: nodes in the tree.</summary>
    public long? Nodes { get; set; }

    /// <summary>Godot objects: orphan nodes.</summary>
    public long? Orphans { get; set; }

    /// <summary>Godot objects: resources.</summary>
    public long? Resources { get; set; }

    /// <summary>The session's object table count.</summary>
    public long? Objects { get; set; }

    /// <summary>Avatars the avatar set holds.</summary>
    public long? Avatars { get; set; }

    /// <summary>Avatars whose body is in a cell, drawn or not (the login check's <c>drawn_in_cell</c> counts only those drawn this frame; M5 0 fix 0-R7).</summary>
    public long? InCell { get; set; }

    /// <summary>Rows of the object light table in use.</summary>
    public long? LightRows { get; set; }

    /// <summary>Objects the client-side cull has queued for removal (<c>GameSession.Cull.Pending</c>; M5 0 fix 0-R11).</summary>
    public long? CullPending { get; set; }

    /// <summary>Objects the cull has removed since the session began (<c>GameSession.Cull.Culled</c>, cumulative; M5 0 fix 0-R11).</summary>
    public long? Culled { get; set; }

    /// <summary>Longest frame of the interval, milliseconds.</summary>
    public double? FrameMaxMs { get; set; }

    /// <summary>99th percentile frame of the interval, milliseconds.</summary>
    public double? FrameP99Ms { get; set; }

    /// <summary>Frames over 50 ms in the interval.</summary>
    public long? Over50 { get; set; }

    /// <summary>Frames over 75 ms in the interval.</summary>
    public long? Over75 { get; set; }

    /// <summary>The session's last tick, milliseconds.</summary>
    public double? TickMs { get; set; }

    /// <summary>Messages the last tick's pump dispatched.</summary>
    public long? PumpMsgs { get; set; }

    /// <summary>Link counter: datagrams sent, resends included, since the session began.</summary>
    public long? Sent { get; set; }

    /// <summary>Link counter: datagrams received and admitted.</summary>
    public long? Recv { get; set; }

    /// <summary>Link counter: packets sent again.</summary>
    public long? Resends { get; set; }

    /// <summary>Link counter: NAK requests the client sent for missing server packets.</summary>
    public long? Naks { get; set; }

    /// <summary>Link counter: server packets received a second time and dropped.</summary>
    public long? Dup { get; set; }

    /// <summary>Link counter: fragment-sequence gaps the inbound sequencer gave up waiting for.</summary>
    public long? Gaps { get; set; }

    /// <summary>Retail's running packet-loss average, percent.</summary>
    public double? LossPct { get; set; }

    /// <summary>The longest time without a server datagram during the interval, seconds.</summary>
    public double? SilenceMaxS { get; set; }

    /// <summary>The session phase.</summary>
    public string? Phase { get; set; }

    /// <summary>Reconnects of the session.</summary>
    public long? Reconnects { get; set; }

    /// <summary>The autopilot's kills (the run's, the cycle in progress included; n/a when no autopilot runs: <see cref="SoakTallies"/>).</summary>
    public long? Kills { get; set; }

    /// <summary>The autopilot's deaths (n/a when no autopilot runs).</summary>
    public long? Deaths { get; set; }

    /// <summary>The autopilot's ended cycles (n/a when no autopilot runs).</summary>
    public long? Cycles { get; set; }

    /// <summary>Errors Godot itself logged (error, script and shader errors), counted in process.</summary>
    public long? GodotErrors { get; set; }

    /// <summary>Length of <c>lifestone.log</c>, bytes.</summary>
    public long? LogBytes { get; set; }

    /// <summary>The mage's health as a share of its maximum, percent (M5 7 F16; n/a with no autopilot or before the vitals are known).</summary>
    public double? HpPct { get; set; }

    /// <summary>The mage's stamina as a share of its maximum, percent (M5 7 F16).</summary>
    public double? SpPct { get; set; }

    /// <summary>The mage's mana as a share of its maximum, percent (M5 7 F16).</summary>
    public double? MpPct { get; set; }

    /// <summary>The biggest single hit the mage took over the run so far (M5 7 F16; n/a with no autopilot).</summary>
    public long? HitMax { get; set; }

    /// <summary>The mage's combat-mode changes over the run so far (M5 7 F25, D93; report-only; n/a with no autopilot).</summary>
    public long? StanceChanges { get; set; }

    /// <summary>The casts whose target died or left during the wind-up over the run so far (M5 7 F28; report-only; n/a with no autopilot).</summary>
    public long? TargetDiedMidCast { get; set; }

    /// <summary>Items the loot rule's plans kept over the process (M5 7 F29, D96; report-only; pyreals count as kept).</summary>
    public long? LootKept { get; set; }

    /// <summary>Items the loot rule's plans left over the process (M5 7 F29; report-only).</summary>
    public long? LootLeft { get; set; }

    /// <summary>The main pack's free slots now (M5 7 F29; report-only; n/a with no autopilot or before the load is known).</summary>
    public long? FreeSlots { get; set; }

    /// <summary>The burden as a share of the capacity, percent (M5 7 F29; report-only).</summary>
    public double? BurdenPct { get; set; }

    /// <summary>The sell trips made over the run (M5 7 F29; report-only; n/a with no autopilot).</summary>
    public long? Trips { get; set; }

    /// <summary>The corpses the loot step opened over the process (M5 7 loot retry; report-only; n/a with no autopilot).</summary>
    public long? CorpsesOpened { get; set; }

    /// <summary>The times a corpse was put off by the loot aggro rule over the process (one corpse can count more than once; report-only).</summary>
    public long? LootLeftAggro { get; set; }

    /// <summary>The corpses the loot step gave up to decay over the process (rotted or gone; report-only).</summary>
    public long? LootLeftDecayed { get; set; }

    /// <summary>M5 7 D123: the corpse opens answered "You're too busy!" (0x001D) and sent again over the process (report-only).</summary>
    public long? LootBusyWaits { get; set; }

    /// <summary>M5 7 D123: the corpses left with every open answered busy over the process (report-only).</summary>
    public long? LootLeftBusy { get; set; }

    /// <summary>M5 7 D123: the corpses left beyond the loot radius, out of sight or not reached by ACE's walk over the process (report-only).</summary>
    public long? LootLeftUnreached { get; set; }

    /// <summary>The wands and cantrip pieces the keep-aside rule kept over the process (M5 7 F30; report-only; n/a with no autopilot).</summary>
    public long? WorthALook { get; set; }

    /// <summary>M5 7 D121: the components pack's free slots last read (report-only; n/a before a layout was read).</summary>
    public long? FreeComponents { get; set; }

    /// <summary>M5 7 D121: the kit pack's free slots last read.</summary>
    public long? FreeKit { get; set; }

    /// <summary>M5 7 D134: the walkers' turn reversals over the process (<see cref="Lifestone.Client.Movement.Wayfinding.SteeringTally"/>; report-only; n/a with no autopilot).</summary>
    public long? TurnReversals { get; set; }

    /// <summary>M5 7 D134: the walkers' turn flips (the other side pressed within 0.2 s: the aim jumping) over the process (report-only).</summary>
    public long? TurnFlips { get; set; }

    /// <summary>M5 7 D134: the walkers' next vertex stepping back on one route over the process (report-only).</summary>
    public long? ProgressBacks { get; set; }

    /// <summary>M5 7 D134: the walkers' swings (a turn in place, then two reversals within 3 s; each logged) over the process (report-only).</summary>
    public long? Swings { get; set; }

    /// <summary>M5 7 D121: the review pack's free slots last read.</summary>
    public long? FreeReview { get; set; }

    /// <summary>M5 7 D125: the pieces the self-equip put on over the process (report-only).</summary>
    public long? SelfEquips { get; set; }

    /// <summary>M5 7 D125: the charged mana stones used on the player over the process.</summary>
    public long? StoneRefills { get; set; }

    /// <summary>M5 7 D125: the empty mana stones used on a junk magic item over the process.</summary>
    public long? StoneCaptures { get; set; }

    /// <summary>M5 7 P7d (D139 row e): textures the render cache holds now, in use and idle (n/a without a render path).</summary>
    public long? TexCacheCount { get; set; }

    /// <summary>M5 7 P7d: the texture cache's bytes now, MiB.</summary>
    public double? TexCacheMb { get; set; }

    /// <summary>M5 7 P7d: the texture cache's idle bytes now (what a trim could free), MiB.</summary>
    public double? TexIdleMb { get; set; }

    /// <summary>M5 7 P7d: meshes the render cache holds now.</summary>
    public long? MeshCacheCount { get; set; }

    /// <summary>M5 7 P7d: the mesh cache's bytes now, MiB.</summary>
    public double? MeshCacheMb { get; set; }

    /// <summary>M5 7 P7d: the arrival trims run over the process (<see cref="Lifestone.Client.Rendering.ArrivalCacheTrim.Trims"/>).</summary>
    public long? CacheTrims { get; set; }

    /// <summary>M5 7 P7d (D139 row f): orphaned objects dropped over the session (<see cref="Lifestone.Client.Session.State.ObjectCull.OrphansDropped"/>).</summary>
    public long? OrphansDropped { get; set; }

    /// <summary>M6 16 Eperf: the frames of the interval (<see cref="SoakFrameWindow.Frames"/>): with the interval's seconds, the frames a second she ran at.</summary>
    public long? Frames { get; set; }

    /// <summary>M6 16 Eperf: the mean frame of the interval, milliseconds (<see cref="SoakFrameWindow.Mean"/>).</summary>
    public double? FrameMeanMs { get; set; }
}

/// <summary>Formats a <see cref="SoakSnapshot"/> as the body of the <c>[soak]</c> line (the log adds the time, the level and the category).</summary>
public static class SoakLineFormat
{
    /// <summary>The category the line is logged under: the line reads <c>INFO  [soak] ws_mb=...</c>.</summary>
    public const string Category = "soak";

    /// <summary>The fields in the order they print.</summary>
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "ws_mb", "private_mb", "managed_mb", "committed_mb", "frag_mb", "loh_mb", "gen0", "gen1", "gen2", "gc_pause_ms",
        "static_mb", "video_mb", "tex_mb", "nodes", "orphans", "resources", "objects", "avatars", "in_cell", "light_rows", "cull_pending", "culled",
        "frame_max_ms", "frame_p99_ms", "over_50", "over_75", "tick_ms", "pump_msgs", "sent", "recv", "resends", "naks",
        "dup", "gaps", "loss_pct", "silence_max_s", "phase", "reconnects", "kills", "deaths", "cycles", "godot_errors", "log_bytes",
        "hp_pct", "sp_pct", "mp_pct", "hit_max", // M5 7 F16: after log_bytes, so the 43 fields before stay in place
        "stance_changes", // M5 7 F25: after hit_max, read by name
        "target_died_mid_cast", // M5 7 F28: after stance_changes, read by name
        "loot_kept", "loot_left", "free_slots", "burden_pct", "trips", // M5 7 F29 (D96): the tail, read by name
        "corpses_opened", "loot_left_aggro", "loot_left_decayed", // M5 7 loot retry: after trips, read by name
        "loot_busy_waits", "loot_left_busy", "loot_left_unreached", // M5 7 D123: the busy waits and the corpses left by reason, read by name
        "worth_a_look", // M5 7 F30 (the owner): the wands and cantrip pieces kept aside, read by name
        "free_components", "free_kit", "free_review", "self_equips", "stone_refills", "stone_captures", // M5 7 D121, D125: the side packs' free slots and the gear counters, read by name
        "turn_reversals", "turn_flips", "progress_backs", "swings", // M5 7 D134: the walkers' steering totals, read by name
        "tex_cache_n", "tex_cache_mb", "tex_idle_mb", "mesh_cache_n", "mesh_cache_mb", "cache_trims", "orphans_dropped", // M5 7 P7d (D139 rows e, f): the render caches and the orphans, read by name
        "frames", "frame_mean_ms", // M6 16 Eperf: the interval's frame count and mean frame, last (every field before stays in place), read by name
    ];

    /// <summary>Why a tally reads n/a: no autopilot runs in this process (the source is <see cref="SoakTallies"/>).</summary>
    public const string NoAutopilot = "n/a unless an autopilot runs (m5-soak-tallies)";

    /// <summary>
    /// The fields that read <c>n/a</c> for want of a source, with the TODO that names why (loud stubs: the node logs this once, at its
    /// start). The three tallies read n/a unless an autopilot runs (M5 4 h1, <c>m5-soak-tallies</c> closed: <see cref="SoakTallies"/> fills them); <c>godot_errors</c> would read n/a only if Godot's logger could not be registered.
    /// </summary>
    public static readonly IReadOnlyList<(string Field, string Todo)> Todo =
    [
        ("kills", NoAutopilot),
        ("deaths", NoAutopilot),
        ("cycles", NoAutopilot),
    ];

    /// <summary>Appends the line's body to <paramref name="sb"/>: <c>name=value</c> pairs separated by single spaces, <c>n/a</c> for a missing value.</summary>
    /// <param name="sb">The builder (cleared by the caller; reused between lines).</param>
    /// <param name="s">The reading.</param>
    public static void Append(StringBuilder sb, SoakSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(s);
        var ci = CultureInfo.InvariantCulture;
        Real(sb, ci, "ws_mb", s.WsMb, first: true);
        Real(sb, ci, "private_mb", s.PrivateMb);
        Real(sb, ci, "managed_mb", s.ManagedMb);
        Real(sb, ci, "committed_mb", s.CommittedMb);
        Real(sb, ci, "frag_mb", s.FragMb);
        Real(sb, ci, "loh_mb", s.LohMb);
        Whole(sb, ci, "gen0", s.Gen0);
        Whole(sb, ci, "gen1", s.Gen1);
        Whole(sb, ci, "gen2", s.Gen2);
        Real(sb, ci, "gc_pause_ms", s.GcPauseMs);
        Real(sb, ci, "static_mb", s.StaticMb);
        Real(sb, ci, "video_mb", s.VideoMb);
        Real(sb, ci, "tex_mb", s.TexMb);
        Whole(sb, ci, "nodes", s.Nodes);
        Whole(sb, ci, "orphans", s.Orphans);
        Whole(sb, ci, "resources", s.Resources);
        Whole(sb, ci, "objects", s.Objects);
        Whole(sb, ci, "avatars", s.Avatars);
        Whole(sb, ci, "in_cell", s.InCell);
        Whole(sb, ci, "light_rows", s.LightRows);
        Whole(sb, ci, "cull_pending", s.CullPending);
        Whole(sb, ci, "culled", s.Culled);
        Real(sb, ci, "frame_max_ms", s.FrameMaxMs);
        Real(sb, ci, "frame_p99_ms", s.FrameP99Ms);
        Whole(sb, ci, "over_50", s.Over50);
        Whole(sb, ci, "over_75", s.Over75);
        Real(sb, ci, "tick_ms", s.TickMs);
        Whole(sb, ci, "pump_msgs", s.PumpMsgs);
        Whole(sb, ci, "sent", s.Sent);
        Whole(sb, ci, "recv", s.Recv);
        Whole(sb, ci, "resends", s.Resends);
        Whole(sb, ci, "naks", s.Naks);
        Whole(sb, ci, "dup", s.Dup);
        Whole(sb, ci, "gaps", s.Gaps);
        Real(sb, ci, "loss_pct", s.LossPct);
        Real(sb, ci, "silence_max_s", s.SilenceMaxS);
        sb.Append(" phase=").Append(string.IsNullOrEmpty(s.Phase) ? "n/a" : s.Phase);
        Whole(sb, ci, "reconnects", s.Reconnects);
        Whole(sb, ci, "kills", s.Kills);
        Whole(sb, ci, "deaths", s.Deaths);
        Whole(sb, ci, "cycles", s.Cycles);
        Whole(sb, ci, "godot_errors", s.GodotErrors);
        Whole(sb, ci, "log_bytes", s.LogBytes);
        Real(sb, ci, "hp_pct", s.HpPct);
        Real(sb, ci, "sp_pct", s.SpPct);
        Real(sb, ci, "mp_pct", s.MpPct);
        Whole(sb, ci, "hit_max", s.HitMax);
        Whole(sb, ci, "stance_changes", s.StanceChanges);
        Whole(sb, ci, "target_died_mid_cast", s.TargetDiedMidCast);
        Whole(sb, ci, "loot_kept", s.LootKept);
        Whole(sb, ci, "loot_left", s.LootLeft);
        Whole(sb, ci, "free_slots", s.FreeSlots);
        Real(sb, ci, "burden_pct", s.BurdenPct);
        Whole(sb, ci, "trips", s.Trips);
        Whole(sb, ci, "corpses_opened", s.CorpsesOpened);
        Whole(sb, ci, "loot_left_aggro", s.LootLeftAggro);
        Whole(sb, ci, "loot_left_decayed", s.LootLeftDecayed);
        Whole(sb, ci, "loot_busy_waits", s.LootBusyWaits);
        Whole(sb, ci, "loot_left_busy", s.LootLeftBusy);
        Whole(sb, ci, "loot_left_unreached", s.LootLeftUnreached);
        Whole(sb, ci, "worth_a_look", s.WorthALook);
        Whole(sb, ci, "free_components", s.FreeComponents);
        Whole(sb, ci, "free_kit", s.FreeKit);
        Whole(sb, ci, "free_review", s.FreeReview);
        Whole(sb, ci, "self_equips", s.SelfEquips);
        Whole(sb, ci, "stone_refills", s.StoneRefills);
        Whole(sb, ci, "stone_captures", s.StoneCaptures);
        Whole(sb, ci, "turn_reversals", s.TurnReversals);
        Whole(sb, ci, "turn_flips", s.TurnFlips);
        Whole(sb, ci, "progress_backs", s.ProgressBacks);
        Whole(sb, ci, "swings", s.Swings);
        Whole(sb, ci, "tex_cache_n", s.TexCacheCount);
        Real(sb, ci, "tex_cache_mb", s.TexCacheMb);
        Real(sb, ci, "tex_idle_mb", s.TexIdleMb);
        Whole(sb, ci, "mesh_cache_n", s.MeshCacheCount);
        Real(sb, ci, "mesh_cache_mb", s.MeshCacheMb);
        Whole(sb, ci, "cache_trims", s.CacheTrims);
        Whole(sb, ci, "orphans_dropped", s.OrphansDropped);
        Whole(sb, ci, "frames", s.Frames);
        Real(sb, ci, "frame_mean_ms", s.FrameMeanMs);
    }

    private static void Real(StringBuilder sb, CultureInfo ci, string name, double? value, bool first = false)
    {
        if (!first)
        {
            sb.Append(' ');
        }

        sb.Append(name).Append('=');
        if (value is { } v && double.IsFinite(v))
        {
            sb.Append(ci, $"{v:0.0##}");
        }
        else
        {
            sb.Append("n/a");
        }
    }

    private static void Whole(StringBuilder sb, CultureInfo ci, string name, long? value)
    {
        sb.Append(' ').Append(name).Append('=');
        if (value is { } v)
        {
            sb.Append(ci, $"{v}");
        }
        else
        {
            sb.Append("n/a");
        }
    }
}

/// <summary>
/// The soak line's <c>kills= deaths= cycles=</c> from the autopilot (M5 item 4 h1, <c>m5-soak-tallies</c>, the source half): the run's totals over the ended cycles plus the cycle in progress,
/// and the ended cycles. Reads three integers off the scenario, once per interval, so the sample allocates nothing; with no autopilot all three read <c>n/a</c>.
/// </summary>
public static class SoakTallies
{
    /// <summary>Fills the three tallies of <paramref name="snapshot"/> from <paramref name="autopilot"/>, or <c>n/a</c> (null) for each when it is null.</summary>
    /// <param name="snapshot">The reading to fill.</param>
    /// <param name="autopilot">The running autopilot, or null.</param>
    public static void Read(SoakSnapshot snapshot, Lifestone.Client.Session.Autopilot.AutopilotScenario? autopilot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (autopilot is null)
        {
            snapshot.Kills = snapshot.Deaths = snapshot.Cycles = snapshot.HitMax = snapshot.StanceChanges = snapshot.TargetDiedMidCast = null;
            snapshot.HpPct = snapshot.SpPct = snapshot.MpPct = null;
            snapshot.LootKept = snapshot.LootLeft = snapshot.FreeSlots = snapshot.Trips = snapshot.WorthALook = null;
            snapshot.CorpsesOpened = snapshot.LootLeftAggro = snapshot.LootLeftDecayed = null;
            snapshot.LootBusyWaits = snapshot.LootLeftBusy = snapshot.LootLeftUnreached = null;
            snapshot.BurdenPct = null;
            snapshot.FreeComponents = snapshot.FreeKit = snapshot.FreeReview = snapshot.SelfEquips = snapshot.StoneRefills = snapshot.StoneCaptures = null;
            snapshot.TurnReversals = snapshot.TurnFlips = snapshot.ProgressBacks = snapshot.Swings = null;
            return;
        }

        var live = autopilot.State == Lifestone.Client.Session.Autopilot.AutopilotState.Fight && autopilot.CurrentRun is Lifestone.Client.Session.Scenarios.Journey.IStationInner { Scenario: Lifestone.Client.Session.Scenarios.IStoppableFight fight } ? fight.Kills : 0; // the fight in progress (its kills join the tally when it ends)
        snapshot.Kills = autopilot.Totals.Kills + autopilot.CycleTally.Kills + live;
        snapshot.Deaths = autopilot.Totals.Deaths + autopilot.CycleTally.Deaths;
        snapshot.Cycles = autopilot.Cycles;
        snapshot.HitMax = autopilot.HitMax; // M5 7 F16: the vitals and the biggest hit
        snapshot.StanceChanges = autopilot.StanceChanges; // M5 7 F25
        snapshot.TargetDiedMidCast = autopilot.TargetDiedMidCast; // M5 7 F28
        snapshot.LootKept = Lifestone.Client.Session.Mage.LootTally.Kept; // M5 7 F29 (D96): the loot and the load, report-only
        snapshot.LootLeft = Lifestone.Client.Session.Mage.LootTally.Left;
        snapshot.CorpsesOpened = Lifestone.Client.Session.Mage.LootTally.Opened; // M5 7 loot retry: the corpses opened, put off and given up
        snapshot.LootLeftAggro = Lifestone.Client.Session.Mage.LootTally.LeftAggro;
        snapshot.LootLeftDecayed = Lifestone.Client.Session.Mage.LootTally.LeftDecayed;
        snapshot.LootBusyWaits = Lifestone.Client.Session.Mage.LootTally.BusyWaits; // M5 7 D123
        snapshot.LootLeftBusy = Lifestone.Client.Session.Mage.LootTally.LeftBusy;
        snapshot.LootLeftUnreached = Lifestone.Client.Session.Mage.LootTally.LeftUnreached;
        snapshot.WorthALook = Lifestone.Client.Session.Mage.KeepAsideTally.WorthALook; // M5 7 F30
        snapshot.FreeComponents = Lifestone.Client.Session.Mage.PackTally.FreeComponents is var fc and >= 0 ? fc : null; // M5 7 D121: the side packs' free slots, n/a before a layout was read
        snapshot.FreeKit = Lifestone.Client.Session.Mage.PackTally.FreeKit is var fk and >= 0 ? fk : null;
        snapshot.FreeReview = Lifestone.Client.Session.Mage.PackTally.FreeReview is var fr and >= 0 ? fr : null;
        snapshot.SelfEquips = Lifestone.Client.Session.Mage.PackTally.SelfEquips; // M5 7 D125
        snapshot.StoneRefills = Lifestone.Client.Session.Mage.PackTally.StoneRefills;
        snapshot.StoneCaptures = Lifestone.Client.Session.Mage.PackTally.StoneCaptures;
        snapshot.TurnReversals = Lifestone.Client.Movement.Wayfinding.SteeringTally.TurnReversals; // M5 7 D134: the walkers' steering totals
        snapshot.TurnFlips = Lifestone.Client.Movement.Wayfinding.SteeringTally.TurnFlips;
        snapshot.ProgressBacks = Lifestone.Client.Movement.Wayfinding.SteeringTally.ProgressBacks;
        snapshot.Swings = Lifestone.Client.Movement.Wayfinding.SteeringTally.Swings;
        snapshot.Trips = autopilot.Totals.Trips + autopilot.CycleTally.Trips;
        if (autopilot.TryLoadShares(out var free, out var burdenPercent))
        {
            (snapshot.FreeSlots, snapshot.BurdenPct) = (free, burdenPercent);
        }
        else
        {
            snapshot.FreeSlots = null;
            snapshot.BurdenPct = null;
        }
        if (autopilot.TryVitalShares(out var hp, out var sp, out var mp))
        {
            (snapshot.HpPct, snapshot.SpPct, snapshot.MpPct) = (hp, sp, mp);
        }
        else
        {
            snapshot.HpPct = snapshot.SpPct = snapshot.MpPct = null;
        }
    }
}
