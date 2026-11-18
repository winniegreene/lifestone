using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Game.Core;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// Names a long frame (M6 16 Eperf2): stepped once a frame by the soak line with the frame's wall length, it writes <c>[play] long frame ms=...</c> (<see cref="LongFrameLine"/>) for a frame over 50 ms, at most one
/// every few seconds (<see cref="LongFrameGate"/>). The line holds the time of each top-level call of the host (<see cref="FramePhases"/>), the draw, the rest, the logger's share and its console print's, the log lines and actions written,
/// the game thread's own CPU time, the process's, the whole machine's busy share, the collector's pause, and the autopilot's state and station.
/// </summary>
/// <remarks>
/// <para>Why the CPU fields: on the live climb of 2026-11-18 most frames over 50 ms spent a small part of their length on a processor (the slow-frame line's <c>process_cpu_ms</c>), and nothing in the log could say
/// whether the game thread was waiting on something of its own or the machine had no processor for it. <c>main_cpu_ms</c> against <c>ms</c> answers the first, <c>machine_busy_pct</c> the second.</para>
/// <para>A frame's cost here: the running totals read at each boundary (the process's CPU time, the game thread's, the machine's, the collector's pause, three counters of the logger) and one array copy. No allocation
/// on a frame without a line. The thread's and the machine's times are the operating system's, read on Windows only (<c>n/a</c> elsewhere); they advance in steps of its clock interrupt, about 16 ms.</para>
/// </remarks>
public sealed class LongFrameWatch
{
    private const string Category = "play";
    private const double FileTimeTicksPerMillisecond = 10_000.0;

    private readonly LongFrameGate _gate = new();
    private readonly StringBuilder _builder = new(512);
    private readonly bool _windows = OperatingSystem.IsWindows();
    private bool _seeded;
    private TimeSpan _processCpu;
    private TimeSpan _gcPause;
    private long _threadCpu;
    private long _machineIdle;
    private long _machineTotal;
    private bool _osRead;
    private long _logLines;
    private long _actions;
    private long _logTicks;
    private long _printTicks;

    /// <summary>Lines written so far.</summary>
    public int Lines { get; private set; }

    /// <summary>The frame that just ended took <paramref name="frameMs"/>; <paramref name="nowMs"/> is the host's time now. Called once a frame, on the game thread, from the node that measures the frame.</summary>
    /// <param name="frameMs">The frame's wall length, milliseconds.</param>
    /// <param name="nowMs">The host's steady time now, milliseconds.</param>
    /// <param name="autopilot">The running autopilot, or null.</param>
    public void Step(double frameMs, double nowMs, AutopilotScenario? autopilot)
    {
        var processCpu = System.Environment.CpuUsage.TotalTime;
        var gcPause = GC.GetTotalPauseDuration();
        var logLines = Log.LinesWritten;
        var actions = Log.ActionLines;
        var logTicks = Log.MainThreadWriteTicks;
        var printTicks = Log.MainThreadPrintTicks;
        long threadCpu = 0, machineIdle = 0, machineTotal = 0;
        var osRead = _windows && ReadWindows(out threadCpu, out machineIdle, out machineTotal);
        FramePhases.Times.Close();

        if (_seeded && _gate.Offer(frameMs, nowMs))
        {
            var both = osRead && _osRead;
            var machine = machineTotal - _machineTotal;
            var sample = new LongFrameSample
            {
                FrameMs = frameMs,
                Phases = FramePhases.Times,
                TicksPerMillisecond = Stopwatch.Frequency / 1000.0,
                LogMs = (logTicks - _logTicks) / (Stopwatch.Frequency / 1000.0),
                LogPrintMs = (printTicks - _printTicks) / (Stopwatch.Frequency / 1000.0),
                LogLines = logLines - _logLines,
                Actions = actions - _actions,
                MainCpuMs = both ? (threadCpu - _threadCpu) / FileTimeTicksPerMillisecond : null,
                ProcessCpuMs = (processCpu - _processCpu).TotalMilliseconds,
                MachineBusyPercent = both && machine > 0 ? 100.0 * (machine - (machineIdle - _machineIdle)) / machine : null,
                GcPauseMs = (gcPause - _gcPause).TotalMilliseconds,
                State = autopilot is null ? null : AutopilotScenario.StateName(autopilot.State),
                Stage = autopilot?.Stage,
                Station = autopilot is null ? null : autopilot.CurrentRun?.Name ?? "none",
                StationStage = autopilot?.CurrentRun?.Stage,
                Skipped = _gate.Skipped,
                SkippedMaxMs = _gate.SkippedMaxMilliseconds,
            };
            _builder.Clear();
            LongFrameLine.Append(_builder, sample);
            _gate.Written();
            Lines++;
            Log.Info(Category, _builder.ToString());

            // The line itself is a log write on this frame: the next frame's totals start after it, read again.
            logLines = Log.LinesWritten;
            actions = Log.ActionLines;
            logTicks = Log.MainThreadWriteTicks;
            printTicks = Log.MainThreadPrintTicks;
        }

        _seeded = true;
        _processCpu = processCpu;
        _gcPause = gcPause;
        _threadCpu = threadCpu;
        _machineIdle = machineIdle;
        _machineTotal = machineTotal;
        _osRead = osRead;
        _logLines = logLines;
        _actions = actions;
        _logTicks = logTicks;
        _printTicks = printTicks;
    }

    // The game thread's CPU time (kernel and user) and the machine's idle and whole processor time, each in 100 ns units. The machine's kernel time holds its idle time (GetSystemTimes), so the whole is kernel plus user.
    private static bool ReadWindows(out long threadCpu, out long machineIdle, out long machineTotal)
    {
        threadCpu = machineIdle = machineTotal = 0;
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var threadKernel, out var threadUser) || !GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return false;
        }

        threadCpu = threadKernel + threadUser;
        machineIdle = idle;
        machineTotal = kernel + user;
        return true;
    }

    // Read-only queries of the running thread's and the machine's processor times; they change nothing on the machine. A FILETIME is two 32-bit halves of one 64-bit count, read here as a long.
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
