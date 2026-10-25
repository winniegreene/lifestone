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

    /// <summary>The driver's kills (<c>TODO(m5-soak-tallies)</c> until item 4's driver reports them).</summary>
    public long? Kills { get; set; }

    /// <summary>The driver's deaths (<c>TODO(m5-soak-tallies)</c>).</summary>
    public long? Deaths { get; set; }

    /// <summary>The driver's cycles (<c>TODO(m5-soak-tallies)</c>).</summary>
    public long? Cycles { get; set; }

    /// <summary>Errors Godot itself logged (error, script and shader errors), counted in process.</summary>
    public long? GodotErrors { get; set; }

    /// <summary>Length of <c>lifestone.log</c>, bytes.</summary>
    public long? LogBytes { get; set; }
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
    ];

    /// <summary>
    /// The fields that read <c>n/a</c> for want of a source, with the TODO that names why (loud stubs: the node logs this once, at its
    /// start). The three tallies wait for item 4's driver; <c>godot_errors</c> would read n/a only if Godot's logger could not be registered.
    /// </summary>
    public static readonly IReadOnlyList<(string Field, string Todo)> Todo =
    [
        ("kills", "TODO(m5-soak-tallies)"),
        ("deaths", "TODO(m5-soak-tallies)"),
        ("cycles", "TODO(m5-soak-tallies)"),
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
