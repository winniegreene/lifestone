<#
.SYNOPSIS
    Records a game window itself: ffmpeg's gfxcapture source (Windows Graphics Capture) on the window's handle.

.DESCRIPTION
    Every Lifestone video recording goes through this module (the owner's ruling, 2026-11-16: the recording must be able to capture just the game
    window, and all video recording works that way). A screen-region grab (gdigrab, ddagrab) records whatever covers the window
    (a soak take of 2026-11-16 caught another application over the game); Windows Graphics Capture reads the window's own frames, so a
    window on top, another monitor or the window moving does not reach the video. It needs ffmpeg 8 or later on PATH (the gfxcapture
    source; the owner's own ffmpeg) and Windows 10 1903 or later. A minimised window gives no new frames: the video holds its last one.

    Find-LifestoneGameWindow   the visible top-level "Engine" window of a Godot process (Godot's window class), optionally the one
                               whose command line contains a given text, once its handle has been stable for StableSeconds
                               (Godot re-creates its window at start-up). Returns { Hwnd, Pid, Width, Height } (client area,
                               physical pixels) or $null when none appears within TimeoutSeconds.
    Get-WindowCaptureInput     ffmpeg's input arguments for that window at a constant frame rate (frames are delivered when the
                               window changes; -fps_mode cfr -r fills the gaps), with no cursor and no capture border.

    Nothing here changes the system: no registry, no settings; the capture ends when ffmpeg ends.
#>
Set-StrictMode -Version Latest

if (-not ('LifestoneWindowCapture.Win' -as [type])) {
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
namespace LifestoneWindowCapture {
public static class Win
{
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    // Per-monitor DPI aware for this thread, so client sizes are physical pixels.
    public static void DpiAware() { SetThreadDpiAwarenessContext(new IntPtr(-4)); }
    // The visible top-level "Engine" window of a process: { hwnd, client width, client height }, or null.
    public static long[] EngineWindow(int pid)
    {
        long[] found = null;
        EnumWindows((h, p) => {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != (uint)pid || !IsWindowVisible(h)) return true;
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            if (cls.ToString() != "Engine") return true;
            RECT r; if (!GetClientRect(h, out r)) return true;
            found = new long[] { h.ToInt64(), r.R - r.L, r.B - r.T };
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
}
'@
}

function Find-LifestoneGameWindow {
    [CmdletBinding()]
    param(
        [string] $CommandLineLike = '',
        [int] $StableSeconds = 5,
        [int] $TimeoutSeconds = 180
    )

    [LifestoneWindowCapture.Win]::DpiAware()
    $hwnd = 0L; $since = $null; $last = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $win = $null; $owner = 0
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name LIKE 'Godot%'" -ErrorAction SilentlyContinue)) {
            if ($CommandLineLike -and -not ($p.CommandLine -and $p.CommandLine.Contains($CommandLineLike))) { continue }
            $w = [LifestoneWindowCapture.Win]::EngineWindow([int]$p.ProcessId)
            if ($w -and $w[1] -ge 320 -and $w[2] -ge 240) { $win = $w; $owner = [int]$p.ProcessId; break }
        }

        if (-not $win) { $hwnd = 0L; $since = $null }
        elseif ($win[0] -ne $hwnd) { $hwnd = $win[0]; $since = [DateTime]::UtcNow }
        else {
            $last = [pscustomobject]@{ Hwnd = $hwnd; Pid = $owner; Width = [int]($win[1] - ($win[1] % 2)); Height = [int]($win[2] - ($win[2] % 2)) }
            if (([DateTime]::UtcNow - $since).TotalSeconds -ge $StableSeconds) { return $last }
        }

        Start-Sleep -Milliseconds 250
    }

    return $null
}

function Get-WindowCaptureInput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Hwnd,
        [int] $Fps = 30
    )

    return @('-f', 'lavfi', '-i', "gfxcapture=hwnd=${Hwnd}:capture_cursor=0:display_border=0:max_framerate=${Fps},hwdownload,format=bgra", '-fps_mode', 'cfr', '-r', "$Fps")
}

function Test-WindowCaptureFfmpeg {
    # The recording's preflight, shared by soak.ps1 and climb.ps1: { Path; Problems[] }. Fail loudly before anything starts.
    $problems = [System.Collections.Generic.List[string]]::new()
    $ffmpeg = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ffmpeg) { $problems.Add('-Record: ffmpeg is not on PATH') }
    elseif (-not (& $ffmpeg.Source -hide_banner -filters 2>$null | Select-String -SimpleMatch ' gfxcapture ' -Quiet)) { $problems.Add('-Record: the ffmpeg on PATH has no gfxcapture source (ffmpeg 8 or later): every recording is a window capture') }
    return [pscustomobject]@{ Path = $(if ($ffmpeg) { $ffmpeg.Source } else { $null }); Problems = $problems }
}

function Get-WindowRecordingArguments {
    # ffmpeg's whole argument list: the window (Hwnd, or a placeholder text in a plan), 30 fps, x264 ultrafast one thread ~1.8 Mbps, SegmentSeconds-long MPEG-TS files rec-NNN.ts in Folder.
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Hwnd, [Parameter(Mandatory)] [int] $LimitSeconds, [Parameter(Mandatory)] [int] $SegmentSeconds, [Parameter(Mandatory)] [string] $Folder)
    return @('-hide_banner', '-loglevel', 'warning', '-stats', '-stats_period', '60', '-y') + (Get-WindowCaptureInput -Hwnd $Hwnd -Fps 30) + @('-t', "$LimitSeconds",
        '-c:v', 'libx264', '-preset', 'ultrafast', '-threads', '1', '-x264-params', 'threads=1', '-b:v', '1800k', '-maxrate', '2000k', '-bufsize', '4000k',
        '-pix_fmt', 'yuv420p', '-g', '300', '-f', 'segment', '-segment_time', "$SegmentSeconds", '-reset_timestamps', '1',
        '-segment_list', (Join-Path $Folder 'segments.csv'), '-segment_list_type', 'csv', (Join-Path $Folder 'rec-%03d.ts'))
}

function Start-WindowRecording {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $FfmpegPath, [Parameter(Mandatory)] $Window, [Parameter(Mandatory)] [int] $LimitSeconds, [Parameter(Mandatory)] [int] $SegmentSeconds, [Parameter(Mandatory)] [string] $Folder, [string] $Label = 'record')
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $ffArgs = Get-WindowRecordingArguments -Hwnd $Window.Hwnd -LimitSeconds $LimitSeconds -SegmentSeconds $SegmentSeconds -Folder $Folder
    $psi = [System.Diagnostics.ProcessStartInfo]::new($FfmpegPath)
    foreach ($a in $ffArgs) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $started = [DateTime]::UtcNow
    $proc = [System.Diagnostics.Process]::Start($psi)
    # ffmpeg's messages and its progress line (once a minute) are copied to the file by .NET itself: no PowerShell code runs on another thread.
    $log = [IO.File]::Create((Join-Path $Folder 'ffmpeg.log'))
    $copy = $proc.StandardError.BaseStream.CopyToAsync($log)
    [IO.File]::WriteAllText((Join-Path $Folder 'record.txt'), ("start_utc={0} unix_ms={1} window={2}x{3} hwnd={4} capture=gfxcapture game_pid={5} ffmpeg_pid={6} segment_s={7}`nffmpeg {8}`n" -f $started.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'), ([DateTimeOffset]$started).ToUnixTimeMilliseconds(), $Window.Width, $Window.Height, $Window.Hwnd, $Window.Pid, $proc.Id, $SegmentSeconds, ($ffArgs -join ' ')))
    Write-Host "[$Label] recording started at $($started.ToString('HH:mm:ss')) UTC (ffmpeg pid $($proc.Id), window $($Window.Width)x$($Window.Height), hwnd $($Window.Hwnd)) into $Folder"
    return [pscustomobject]@{ Process = $proc; Log = $log; Copy = $copy }
}

function Stop-WindowRecording {
    [CmdletBinding()]
    param($Recording, [string] $Label = 'record')
    if (-not $Recording) { return }
    $proc = $Recording.Process
    try {
        if (-not $proc.HasExited) {
            try { $proc.StandardInput.WriteLine('q'); $proc.StandardInput.Flush() } catch { }
            if (-not $proc.WaitForExit(5000)) { $proc.Kill($true); $proc.WaitForExit(5000) | Out-Null; Write-Host "[$Label] recording: ffmpeg did not stop on q within 5 s and was killed (MPEG-TS files need no index, so the last one still plays)" -ForegroundColor Yellow }
        }
        $proc.WaitForExit()
    }
    catch { }
    try { $Recording.Copy.Wait(5000) | Out-Null } catch { }
    try { $Recording.Log.Dispose() } catch { }
    Write-Host "[$Label] recording stopped (ffmpeg exit $(try { $proc.ExitCode } catch { 'unknown' }))"
}

function Get-WindowRecordingFiles {
    # { Count; Bytes } of the rec-*.ts files in Folder.
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Folder)
    $files = @(Get-ChildItem -LiteralPath $Folder -File -Filter 'rec-*.ts' -ErrorAction SilentlyContinue)
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { $bytes = 0 }
    return [pscustomobject]@{ Count = $files.Count; Bytes = $bytes }
}

Export-ModuleMember -Function Find-LifestoneGameWindow, Get-WindowCaptureInput, Test-WindowCaptureFfmpeg, Get-WindowRecordingArguments, Start-WindowRecording, Stop-WindowRecording, Get-WindowRecordingFiles
