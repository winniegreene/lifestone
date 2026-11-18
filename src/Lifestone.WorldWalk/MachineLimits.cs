using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lifestone.Client.Movement.Wayfinding;

namespace Lifestone.WorldWalk;

/// <summary>
/// The owner's Light mode (2026-11-06): the process runs on cores 16 to 31 only, below normal priority, keeps the PC awake only while it runs, and pauses itself for the runs a load would fail.
/// Everything here ends with the process: nothing is written to the machine.
/// </summary>
/// <remarks>
/// Cores 0 to 15 hold the 96 MB cache games favour, so they stay free for Godot. A live gate's "contended" pass counts only other Godot runs (tools/smoke.ps1), so this process's load there would be a real
/// failure, not an excused one; that is why the walk stops for a gate instead of slowing down.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class MachineLimits : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private static readonly nint Cores16To31 = unchecked((nint)0xFFFF0000L);
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(5);

    private readonly ManualResetEventSlim _running = new(true);
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _note;
    private readonly string _pauseFile;
    private readonly bool _ignoreClimb;
    private readonly Thread _watcher;
    private string? _pausedFor;

    public MachineLimits(string runFolder, bool ignoreClimb, Action<string> note)
    {
        _note = note;
        _pauseFile = Path.Combine(runFolder, "PAUSE");
        _ignoreClimb = ignoreClimb;
        using (var self = Process.GetCurrentProcess())
        {
            self.ProcessorAffinity = Cores16To31;
            self.PriorityClass = ProcessPriorityClass.BelowNormal;
            self.Refresh();
            if (self.ProcessorAffinity != Cores16To31 || self.PriorityClass != ProcessPriorityClass.BelowNormal)
            {
                throw new InvalidOperationException($"light-mode-not-applied: affinity 0x{(long)self.ProcessorAffinity:X} priority {self.PriorityClass}");
            }
        }

        note("light mode applied: cores 16-31, below normal priority");
        _watcher = new Thread(Watch) { IsBackground = true, Name = "machine-limits" };
        _watcher.Start();
    }

    /// <summary>Blocks while the walk is paused. Workers call it between landblocks, so a pause waits for the landblocks in hand, seconds at most.</summary>
    public void Gate() => _running.Wait();

    public void Dispose()
    {
        _stop.Cancel();
        _running.Set();
        _watcher.Join(TimeSpan.FromSeconds(10));
        _running.Dispose();
        _stop.Dispose();
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int infoClass, nint info, int length, out int returned);

    /// <summary>A process's command line, or null when it cannot be read (it has gone, or belongs to another user).</summary>
    private static string? CommandLineOf(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == 0)
        {
            return null;
        }

        const int Size = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(Size);
        try
        {
            if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, Size, out _) != 0)
            {
                return null;
            }

            var length = (ushort)Marshal.ReadInt16(buffer); // a UNICODE_STRING: its length in bytes, then a pointer to its text
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return text == 0 ? null : Marshal.PtrToStringUni(text, length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(handle);
        }
    }

    private string? FindReason()
    {
        if (File.Exists(_pauseFile))
        {
            return "pause-file";
        }

        foreach (var name in new[] { "pwsh", "powershell", "dotnet", "testhost" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.Id != Environment.ProcessId && CommandLineOf(process.Id) is { } line && WalkPauseRule.Reason(line, _ignoreClimb) is { } reason)
                    {
                        return reason;
                    }
                }
            }
        }

        return null;
    }

    private void Watch()
    {
        // The hold lives on this thread and ends with it: nothing is left set on the machine.
        if (SetThreadExecutionState(EsContinuous | EsSystemRequired) == 0)
        {
            _note("warning: the PC could not be held awake");
        }

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                string? reason;
                try
                {
                    reason = FindReason();
                }
                catch (Win32Exception e)
                {
                    reason = _pausedFor;
                    _note("warning: the process check failed: " + e.Message);
                }

                if (reason != _pausedFor)
                {
                    _pausedFor = reason;
                    if (reason is null)
                    {
                        _note("resumed at " + Stamp());
                        _running.Set();
                    }
                    else
                    {
                        _note($"paused for {reason} at {Stamp()}");
                        _running.Reset();
                    }
                }

                _stop.Token.WaitHandle.WaitOne(PollEvery);
            }
        }
        finally
        {
            SetThreadExecutionState(EsContinuous);
        }
    }

    private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
