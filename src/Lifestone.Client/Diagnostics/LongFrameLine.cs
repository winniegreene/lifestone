using System.Globalization;
using System.Text;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// Which long frames get a line (M6 16 Eperf2): a frame over the threshold, at most one every <see cref="EveryMilliseconds"/>, except that a frame at least twice the longest one seen since the last line is written
/// at once (so a much worse frame is never hidden behind a mild one; that adds at most a handful of lines to a window). The frames left out are counted and their longest kept for the next line. The host hands
/// in the time; this class reads no clock and allocates nothing.
/// </summary>
public sealed class LongFrameGate
{
    private readonly double _thresholdMs;
    private readonly double _everyMs;
    private double _lastLineAtMs = double.NegativeInfinity;
    private double _longestSinceLineMs;

    /// <summary>A gate at <paramref name="thresholdMs"/> (default 50, the frame rule's limit) writing at most one line every <paramref name="everyMs"/> (default 5 s).</summary>
    /// <param name="thresholdMs">A frame longer than this is long.</param>
    /// <param name="everyMs">The least time between two lines, milliseconds.</param>
    public LongFrameGate(double thresholdMs = SoakFrameWindow.Over50Milliseconds, double everyMs = 5000.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdMs);
        ArgumentOutOfRangeException.ThrowIfNegative(everyMs);
        _thresholdMs = thresholdMs;
        _everyMs = everyMs;
    }

    /// <summary>The least time between two lines, milliseconds.</summary>
    public double EveryMilliseconds => _everyMs;

    /// <summary>The long frames left out since the last line.</summary>
    public int Skipped { get; private set; }

    /// <summary>The longest of the frames left out since the last line, milliseconds (0 when none).</summary>
    public double SkippedMaxMilliseconds { get; private set; }

    /// <summary>
    /// True when the frame of <paramref name="frameMs"/> that ended at <paramref name="nowMs"/> is to be written. On true the caller reads <see cref="Skipped"/> and <see cref="SkippedMaxMilliseconds"/> for the line and
    /// then calls <see cref="Written"/>.
    /// </summary>
    /// <param name="frameMs">The frame's length, milliseconds.</param>
    /// <param name="nowMs">The host's time now, milliseconds on any steady clock.</param>
    public bool Offer(double frameMs, double nowMs)
    {
        if (!(frameMs > _thresholdMs))
        {
            return false;
        }

        if (nowMs - _lastLineAtMs >= _everyMs || frameMs >= 2.0 * _longestSinceLineMs)
        {
            _lastLineAtMs = nowMs;
            _longestSinceLineMs = frameMs;
            return true;
        }

        Skipped++;
        SkippedMaxMilliseconds = Math.Max(SkippedMaxMilliseconds, frameMs);
        _longestSinceLineMs = Math.Max(_longestSinceLineMs, frameMs);
        return false;
    }

    /// <summary>The line was written: the count of frames left out starts again.</summary>
    public void Written()
    {
        Skipped = 0;
        SkippedMaxMilliseconds = 0.0;
    }
}

/// <summary>What the host knows of one long frame (M6 16 Eperf2); <see cref="LongFrameLine.Append"/> writes it. A null reads <c>n/a</c>.</summary>
public struct LongFrameSample
{
    /// <summary>The frame's length on the wall clock, milliseconds.</summary>
    public double FrameMs { get; set; }

    /// <summary>The frame's phases in the host's clock ticks, closed at the frame's boundary.</summary>
    public FramePhaseTimes? Phases { get; set; }

    /// <summary>The host clock's ticks a millisecond.</summary>
    public double TicksPerMillisecond { get; set; }

    /// <summary>The time the game thread spent writing log lines during the frame (inside the phases, not beside them), milliseconds.</summary>
    public double LogMs { get; set; }

    /// <summary>The part of <see cref="LogMs"/> in the console print (the rest is the format and the log file's write), milliseconds.</summary>
    public double LogPrintMs { get; set; }

    /// <summary>The log lines written during the frame, every thread's.</summary>
    public long LogLines { get; set; }

    /// <summary>The action lines among them (<c>action #</c>).</summary>
    public long Actions { get; set; }

    /// <summary>The game thread's own CPU time over the frame, milliseconds (the operating system's count, in its clock's steps), or null where it cannot be read.</summary>
    public double? MainCpuMs { get; set; }

    /// <summary>Every thread's CPU time over the frame, milliseconds.</summary>
    public double ProcessCpuMs { get; set; }

    /// <summary>The share of the whole machine's processor time that was busy over the frame, every process's, in percent, or null where it cannot be read.</summary>
    public double? MachineBusyPercent { get; set; }

    /// <summary>The collector's pause over the frame, milliseconds.</summary>
    public double GcPauseMs { get; set; }

    /// <summary>The autopilot's state when the line is written, or null when none runs.</summary>
    public string? State { get; set; }

    /// <summary>The autopilot's stage text, or null.</summary>
    public string? Stage { get; set; }

    /// <summary>The name of the station in progress, or null when none runs.</summary>
    public string? Station { get; set; }

    /// <summary>That station's stage text, or null.</summary>
    public string? StationStage { get; set; }

    /// <summary>The long frames left out since the last line.</summary>
    public int Skipped { get; set; }

    /// <summary>The longest of them, milliseconds.</summary>
    public double SkippedMaxMs { get; set; }
}

/// <summary>
/// The long-frame line (M6 16 Eperf2): what a frame over 50 ms was made of, as far as the play host can know.
/// <c>long frame ms= services_ms= session_ms= play_ms= player_ms= autopilot_ms= ui_ms= world_ms= draw_ms= after_draw_ms= other_ms= log_ms= log_print_ms= log_lines= actions= main_cpu_ms= process_cpu_ms= machine_busy_pct= gc_pause_ms=
/// state= stage= station= station_stage= skipped= skipped_max_ms=</c>.
/// <list type="bullet">
/// <item>The nine <c>*_ms</c> phases are <see cref="FramePhase"/>'s, in its order; <c>other_ms</c> is the frame less their sum: the nodes the host does not time and the engine between the process step and the
/// draw (deferred calls, signal handlers).</item>
/// <item><c>after_draw_ms</c> is where a quiet frame sleeps; a long one there with a small <c>main_cpu_ms</c> is a thread the operating system woke late.</item>
/// <item><c>log_ms</c> is inside the phases: the game thread's time in the logger (the format, the log file's write and the console print); <c>log_print_ms</c> is the print's part of it.</item>
/// <item><c>main_cpu_ms</c> against <c>ms</c> says whether the game thread worked or waited; <c>machine_busy_pct</c> says whether the machine had a processor to give it.</item>
/// <item><c>state</c> to <c>station_stage</c> are read when the line is written, which is the start of the frame after the long one.</item>
/// </list>
/// </summary>
public static class LongFrameLine
{
    /// <summary>The line's first words.</summary>
    public const string Token = "long frame";

    private static readonly string[] PhaseFields = ["services_ms", "session_ms", "play_ms", "player_ms", "autopilot_ms", "ui_ms", "world_ms", "draw_ms", "after_draw_ms"];

    /// <summary>The field names of the phases, in <see cref="FramePhase"/>'s order.</summary>
    public static IReadOnlyList<string> PhaseFieldNames => PhaseFields;

    /// <summary>Appends the line for <paramref name="s"/> to <paramref name="sb"/>.</summary>
    /// <param name="sb">The builder.</param>
    /// <param name="s">The sample.</param>
    public static void Append(StringBuilder sb, in LongFrameSample s)
    {
        ArgumentNullException.ThrowIfNull(sb);
        var ci = CultureInfo.InvariantCulture;
        sb.Append(Token);
        Real(sb, ci, "ms", s.FrameMs);
        var perMs = s.TicksPerMillisecond > 0.0 ? s.TicksPerMillisecond : 1.0;
        var phases = 0.0;
        for (var i = 0; i < FramePhaseTimes.Count; i++)
        {
            var ms = s.Phases is { } times ? times.Closed((FramePhase)i) / perMs : 0.0;
            phases += ms;
            Real(sb, ci, PhaseFields[i], ms);
        }

        Real(sb, ci, "other_ms", Math.Max(0.0, s.FrameMs - phases));
        Real(sb, ci, "log_ms", s.LogMs);
        Real(sb, ci, "log_print_ms", s.LogPrintMs);
        sb.Append(" log_lines=").Append(s.LogLines.ToString(ci));
        sb.Append(" actions=").Append(s.Actions.ToString(ci));
        Real(sb, ci, "main_cpu_ms", s.MainCpuMs);
        Real(sb, ci, "process_cpu_ms", s.ProcessCpuMs);
        Real(sb, ci, "machine_busy_pct", s.MachineBusyPercent);
        Real(sb, ci, "gc_pause_ms", s.GcPauseMs);
        Text(sb, "state", s.State);
        Text(sb, "stage", s.Stage);
        Text(sb, "station", s.Station);
        Text(sb, "station_stage", s.StationStage);
        sb.Append(" skipped=").Append(s.Skipped.ToString(ci));
        Real(sb, ci, "skipped_max_ms", s.SkippedMaxMs);
    }

    private static void Real(StringBuilder sb, CultureInfo ci, string key, double? value)
    {
        sb.Append(' ').Append(key).Append('=');
        if (value is { } v && double.IsFinite(v))
        {
            sb.Append(v.ToString("0.#", ci));
        }
        else
        {
            sb.Append("n/a");
        }
    }

    // A text value as the log's fields are written: quoted when it holds a space, a double quote inside written as a single one; null and empty read n/a.
    private static void Text(StringBuilder sb, string key, string? value)
    {
        sb.Append(' ').Append(key).Append('=');
        if (string.IsNullOrEmpty(value))
        {
            sb.Append("n/a");
        }
        else if (value.Contains(' ', StringComparison.Ordinal))
        {
            sb.Append('"').Append(value.Replace('"', '\'')).Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }
}
