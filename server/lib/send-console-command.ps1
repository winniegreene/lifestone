<#
.SYNOPSIS
  Types a command line into the console of another process (used to send ACE's own "stop-now" console command).

.DESCRIPTION
  ACE reads admin commands from its console (Console.ReadLine). start-server.ps1 launches ACE with a private,
  window-less console (CREATE_NO_WINDOW), so stop-server.ps1 runs this helper in a separate, window-less
  PowerShell process which detaches from its own console, attaches to ACE's console and writes key events into
  its input buffer. Nothing is elevated, no global state is changed.

  Must run in its own process: FreeConsole/AttachConsole affect the calling process.
  Compatible with Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [Parameter(Mandatory = $true)][string]$CommandLine
)

$ErrorActionPreference = 'Stop'

$source = @'
using System;
using System.Runtime.InteropServices;

public static class LifestoneConsoleInjector
{
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    public struct KEY_EVENT_RECORD
    {
        [FieldOffset(0)] public int bKeyDown;
        [FieldOffset(4)] public ushort wRepeatCount;
        [FieldOffset(6)] public ushort wVirtualKeyCode;
        [FieldOffset(8)] public ushort wVirtualScanCode;
        [FieldOffset(10)] public char UnicodeChar;
        [FieldOffset(12)] public uint dwControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool WriteConsoleInputW(IntPtr h, INPUT_RECORD[] records, uint count, out uint written);

    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const ushort KEY_EVENT = 1;

    static INPUT_RECORD Key(char c, ushort vk, bool down)
    {
        var r = new INPUT_RECORD();
        r.EventType = KEY_EVENT;
        r.KeyEvent.bKeyDown = down ? 1 : 0;
        r.KeyEvent.wRepeatCount = 1;
        r.KeyEvent.wVirtualKeyCode = vk;
        r.KeyEvent.UnicodeChar = c;
        return r;
    }

    public static string Send(uint pid, string line)
    {
        FreeConsole();
        if (!AttachConsole(pid))
            return "AttachConsole failed, win32 error " + Marshal.GetLastWin32Error();
        try
        {
            IntPtr h = CreateFileW("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == new IntPtr(-1))
                return "CreateFile(CONIN$) failed, win32 error " + Marshal.GetLastWin32Error();
            try
            {
                var recs = new INPUT_RECORD[(line.Length + 1) * 2];
                int i = 0;
                foreach (char c in line)
                {
                    recs[i++] = Key(c, 0, true);
                    recs[i++] = Key(c, 0, false);
                }
                recs[i++] = Key('\r', 0x0D, true);
                recs[i++] = Key('\r', 0x0D, false);
                uint written;
                if (!WriteConsoleInputW(h, recs, (uint)recs.Length, out written))
                    return "WriteConsoleInput failed, win32 error " + Marshal.GetLastWin32Error();
                if (written != recs.Length)
                    return "WriteConsoleInput wrote " + written + " of " + recs.Length + " records";
                return "OK";
            }
            finally { CloseHandle(h); }
        }
        finally { FreeConsole(); }
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
$result = [LifestoneConsoleInjector]::Send([uint32]$TargetPid, $CommandLine)
if ($result -ne 'OK') {
    [Console]::Error.WriteLine($result)
    exit 2
}
exit 0
