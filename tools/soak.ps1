<#
.SYNOPSIS
    The soak's runner (M5 item 7, DoD 13, D23, D85): one command for the long autopilot run on BSD Mage, with the machine held awake,
    the outside watcher beside it, the log copied out and the report written; with -Record, the game window recorded to video for the hero cut. Run by the item's lead only.

.DESCRIPTION
    Run it from the ITEM worktree (the soak runs the item's code). The backend is the main checkout's: this script starts and stops no
    backend (the lead does), and play.ps1 is run with -KeepServer.

    Roots. The main checkout (resolved as tools\play.ps1 does: the git repository's main worktree) holds the backend state, the lead's
    lock cache\backend-owner.txt (D54) and the server folders the watcher measures (server\runtime\logs, server\runtime\mariadb-data). The
    checkout this script runs from holds the run folder cache\soak\<UTC stamp>[-rehearsal], the game, the isolated user data and its logs.

    Refuses (exit 1, nothing started) unless:
      * cache\backend-owner.txt exists in the main checkout (the lead's lock);
      * the backend is up with the Lifestone mod ready (server\lib\LifestoneServer.psm1 Get-LsBackendState, as play.ps1 reads it);
      * no Godot process is running (tools\lib\GateHost.psm1 Get-ForeignGodot).

    Then, in order: makes the run folder; wakes the display and holds it on for the whole run (GateHost; ended in a finally; no power
    setting changed); starts tools\soak-watch.ps1 in the background (every 300 s, CSV in the run folder) and stops it in the finally;
    runs tools\play.ps1 -Offscreen -IsolateUserData -KeepServer -Character "BSD Mage" (the test account) with the game arguments below.
    After the game exits: copies every lifestone*.log (lifestone.log and the rolled lifestone.N.log files) out of the isolated logs folder, runs tools\soak-report.ps1 over
    the run folder, and writes one summary line. The exit code is play.ps1's. Recording is off unless -Record. No password is passed or printed.

    Game arguments: --autopilot-start --autopilot-minutes=<M> --fight-loot=on --max-fps=60 --soak-line-s=60 --log-copies=file --log-max-mb=16 --log-keep=0 --log-vitals-s=10 --log-objects-m=60 --clean-hud --mute
    --soak-frame-s=<F> --soak-frame-dir=<run folder relative to the repository>/frames --autopilot-stop-file=<run folder relative to the repository>/stop

.PARAMETER Until
    End of the run, HH:mm UTC today (the run is as many minutes as remain). Exactly one of -Until and -Minutes.

.PARAMETER Minutes
    Length of the run in minutes (1 to 1440).

.PARAMETER Resolution
    The play host's window size (default 1280x720).

.PARAMETER FrameSeconds
    Seconds between two frames saved for the owner's eye (default 1800).

.PARAMETER Record
    Record the game window for the hero video (M5 7, D97; the owner's yes of 2026-10-29). Adds --video-sync-marker to the game arguments (one white frame and its log line
    'video_sync_marker t=<unix_ms>', 2 s after the world shows, so a cut lines up with the log's clock to the frame). Once the game's window exists (polled up to 180 s), starts
    ffmpeg (the one on PATH) with gdigrab over the window's client area on the desktop: 30 fps, no cursor, x264 ultrafast on one thread at about 1.8 Mbps, cut into
    -RecordSegmentSeconds files <run folder>\record\rec-NNN.ts (MPEG-TS: it needs no index, so a crash or a kill loses nothing written) with record\segments.csv (each file's start and end in
    seconds from the recording's start) and record\record.txt (the start in UTC, the area, the command). ffmpeg's output goes to record\ffmpeg.log, never the console. It reads
    the screen only: the window must stay visible and on top (a window over it, or a locked screen, is what is recorded then). Stopped in the finally (q on its input, then
    killed after 5 s: ffmpeg did not stop on q from a pipe in the test, and the files survive a kill); a hard kill of this script still ends it at the play timeout plus 600 s (-t). The report gains a line recording= with the folder, the files and the bytes.

.PARAMETER RecordSegmentSeconds
    The length of one recording file in seconds (default 3600).

.PARAMETER Rehearsal
    A rehearsal: the run folder's name says so (<stamp>-rehearsal).

.PARAMETER TripAfterKills
    A rehearsal's forced town trip (M5 7 D140): passes --autopilot-trip-after-kills=N to the game, so one logistics trip runs after N kills
    (0, the default, passes nothing; a soak's own trips are unaffected).

.PARAMETER ExamineWornFrames
    The item examination's colour proof (M5 7 D142): passes --examine-worn-frames=<run folder>/examine-worn, so the wielded caster and the worn robe are
    examined once their pages show a spell-changed value (after the start's buff pass) and a frame of each is saved there (off by default).

.PARAMETER ControlDrill
    The live proof of taking the controls and giving them back (M5 7 D145): passes --autopilot-control-drill=<run folder>/control-drill, so the game itself, through its own input
    path (Move Forward's key through Godot's input, /stop and /auto typed into the main chat; no server cheat, no key reaches the operating system), runs the drill's steps: (a) W held
    1 s mid-fight does nothing and the notice shows; (b) /stop mid-fight stops him in place, then W moves him; (c) walked 10 m off, peace, the caster moved into a side pack, then /auto
    and the next kill; (d) /stop mid-walk on the next town trip, /auto 5 s later, the trip's end. Each step logs 'control drill: step=<id> verdict=pass|fail ...' and saves
    control-drill-<step>.png into the folder. Step d needs a trip: without -TripAfterKills the switch sets it to 6 (printed).

.PARAMETER Plan
    Prints the roots, the run folder, the preconditions and the commands, and exits 0. Starts nothing, creates nothing.

.PARAMETER Stop
    A clean stop from outside (M5 7 F18). Run from the same checkout as the soak: finds the newest run folder under cache\soak that has no report.txt yet (a soak whose game is still running) and writes the file `stop`
    in it, which the game polls for every 5 s (--autopilot-stop-file): the autopilot casts the recall home, the game logs off at home and quits with exit code 0. Prints what it did. Refuses (exit 1) when no soak is running.
    Starts nothing else; combine it with no other parameter.

.PARAMETER Resume
    Keep the autopilot checkpoint (M5 7 G2). Without it every start sets the checkpoint aside first: renames `autopilot\bsd-mage.json` in the isolated user data to `bsd-mage.<UTC stamp>.aside` (never deleted) and prints the new
    path, so a soak after a crash, a failed stop or an abandon starts from zero minutes and zero totals instead of resuming the old run. -Plan prints what it would move and moves nothing.

.PARAMETER AutopilotFolder
    Test seam: the folder that holds bsd-mage.json (default: the `autopilot` folder beside the isolated `logs` folder, cache\godot\roaming\Godot\app_userdata\Lifestone\autopilot).

.PARAMETER SetAsideOnly
    Test seam: only the checkpoint step (set aside, or kept under -Resume), then exit 0. Starts nothing and checks nothing else.

.PARAMETER RunsRoot
    Test seam for -Stop: the folder that holds the run folders (default cache\soak of this checkout); with it the running-game check is skipped.

.PARAMETER MainCheckout
    Test seam: the folder to treat as the main checkout (lock, backend state, server folders). Default: the git repository's main worktree.

.EXAMPLE
    pwsh -File tools\soak.ps1 -Minutes 300 -Plan
#>
[CmdletBinding()]
param(
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')]
    [string] $Until = '',
    [ValidateRange(0, 1440)]
    [int] $Minutes = 0,
    [ValidatePattern('^\d+x\d+$')]
    [string] $Resolution = '1280x720',
    [ValidateRange(1, 86400)]
    [int] $FrameSeconds = 1800,
    [switch] $Rehearsal,
    [switch] $Record,
    [ValidateRange(0, 100000)]
    [int] $TripAfterKills = 0,
    [switch] $ExamineWornFrames,
    [switch] $ControlDrill,
    [ValidateRange(60, 86400)]
    [int] $RecordSegmentSeconds = 3600,
    [switch] $Plan,
    [switch] $Stop,
    [switch] $Resume,
    [switch] $SetAsideOnly,
    [string] $AutopilotFolder = '',
    [string] $RunsRoot = '',
    [string] $MainCheckout = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pwshExe = (Get-Process -Id $PID).Path
Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'lib\GateHost.psm1') -Force -DisableNameChecking

function Stop-Refused([string] $Message) {
    Write-Host "[soak] REFUSED: $Message" -ForegroundColor Red
    exit 1
}

# --- 0. -Stop: the clean stop from outside (M5 7 F18). ---
if ($Stop) {
    $seam = [bool]$RunsRoot
    $runsRoot = if ($seam) { [IO.Path]::GetFullPath($RunsRoot) } else { Join-Path $repoRoot 'cache\soak' }
    $newest = $null
    if (Test-Path -LiteralPath $runsRoot -PathType Container) {
        $newest = Get-ChildItem -LiteralPath $runsRoot -Directory | Sort-Object Name -Descending | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.FullName 'report.txt')) } | Select-Object -First 1
    }
    if (-not $newest) { Stop-Refused "no soak is running: no run folder without a report.txt under $runsRoot" }
    if (-not $seam) {
        $games = @(Get-ForeignGodot)
        if ($games.Count -eq 0) { Stop-Refused "no soak is running: $($newest.Name) has no report yet, but no Godot process is running" }
    }
    $stopFile = Join-Path $newest.FullName 'stop'
    [IO.File]::WriteAllText($stopFile, "stop requested at $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')) UTC`n")
    Write-Host "[soak] -Stop: wrote $stopFile for the newest running soak ($($newest.Name)); the game looks for it every 5 s, the autopilot recalls home, then the game logs off and quits"
    exit 0
}

# --- 0b. The autopilot checkpoint (M5 7 G2): set aside at every start unless -Resume. ---
$checkpointFolder = if ($AutopilotFolder) { [IO.Path]::GetFullPath($AutopilotFolder) } else { Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\autopilot' }
$checkpointFile = Join-Path $checkpointFolder 'bsd-mage.json'
function Invoke-CheckpointAside([switch] $WhatIfOnly) {
    if ($Resume) {
        Write-Host "[soak] -Resume: the autopilot checkpoint is kept ($checkpointFile$(if (-not (Test-Path -LiteralPath $checkpointFile -PathType Leaf)) { '; none exists' }))"
        return
    }
    if (-not (Test-Path -LiteralPath $checkpointFile -PathType Leaf)) {
        Write-Host "[soak] checkpoint: none to set aside ($checkpointFile does not exist); the run starts from zero"
        return
    }
    $aside = Join-Path $checkpointFolder ('bsd-mage.{0}.aside' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
    if ($WhatIfOnly) {
        Write-Host "[soak] checkpoint: would set $checkpointFile aside as $aside (rename, never delete); -Resume keeps it"
        return
    }
    Move-Item -LiteralPath $checkpointFile -Destination $aside
    Write-Host "[soak] checkpoint set aside: $checkpointFile -> $aside (-Resume keeps it)"
}
if ($SetAsideOnly) {
    Invoke-CheckpointAside
    exit 0
}

# --- 1. The length of the run. ---
if ($Until -and $Minutes -gt 0) { Stop-Refused 'give -Until or -Minutes, not both' }
$now = [DateTime]::UtcNow
if ($Until) {
    $end = [DateTime]::ParseExact($Until, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture).TimeOfDay
    # F1 item 6: a time earlier than now is refused, never rolled to tomorrow; the local is not range-validated like $Minutes, so the refusal is the plain message.
    $remaining = [int][math]::Ceiling(($now.Date + $end - $now).TotalMinutes)
    if ($remaining -le 0) { Stop-Refused "-Until $Until UTC has already passed (it is $($now.ToString('HH:mm')) UTC); give a time later today, or -Minutes M" }
    $Minutes = $remaining
}
if ($Minutes -le 0) { Stop-Refused 'give -Until HH:mm (UTC, today) or -Minutes M' }

# --- 2. The roots, the run folder and the commands. ---
if (-not $MainCheckout) {
    # As tools\play.ps1 does: the backend's runtime folder is the main checkout's (Find-LifestoneServerRuntime reads the worktree link); its grandparent is the main checkout.
    $mainRuntime = Find-LifestoneServerRuntime -RepoRoot $repoRoot -AllowMissing
    # No runtime: the backend was never set up; the preconditions below say so (and -Plan still prints the run).
    $MainCheckout = if ($mainRuntime) { Split-Path -Parent (Split-Path -Parent $mainRuntime) } else { $repoRoot }
}
$MainCheckout = [IO.Path]::GetFullPath($MainCheckout).TrimEnd('\', '/')

$stamp = $now.ToString('yyyyMMddTHHmmssZ') + $(if ($Rehearsal) { '-rehearsal' } else { '' })
$runRelative = "cache/soak/$stamp"
$runFolder = Join-Path $repoRoot ($runRelative -replace '/', '\')
$godotLogs = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs'
$serverLogs = Join-Path $MainCheckout 'server\runtime\logs'
$databaseDir = Join-Path $MainCheckout 'server\runtime\mariadb-data'
$lockFile = Join-Path $MainCheckout 'cache\backend-owner.txt'
$timeoutSeconds = $Minutes * 60 + 1800

$gameArgs = "--autopilot-start --autopilot-minutes=$Minutes --fight-loot=on --max-fps=60 --soak-line-s=60 --log-copies=file --log-max-mb=16 --log-keep=0 --log-vitals-s=10 --log-objects-m=60 --clean-hud --mute --soak-frame-s=$FrameSeconds --soak-frame-dir=$runRelative/frames --autopilot-stop-file=$runRelative/stop"
if ($Record) { $gameArgs += ' --video-sync-marker' }
if ($TripAfterKills -gt 0) { $gameArgs += " --autopilot-trip-after-kills=$TripAfterKills" }
if ($ExamineWornFrames) { $gameArgs += " --examine-worn-frames=$runRelative/examine-worn" }
if ($ControlDrill) {
    # M5 7 D145: the drill's step d waits for a town trip, so a drill run always forces one (the owner's point: use the existing trip-after-kills option).
    if ($TripAfterKills -le 0) {
        $TripAfterKills = 6
        $gameArgs += " --autopilot-trip-after-kills=$TripAfterKills"
        Write-Host "[soak] -ControlDrill: no -TripAfterKills given; the drill's town trip is forced after $TripAfterKills kills"
    }
    $gameArgs += " --autopilot-control-drill=$runRelative/control-drill"
}
$recordFolder = Join-Path $runFolder 'record'
$playScript = Join-Path $PSScriptRoot 'play.ps1'
$watchScript = Join-Path $PSScriptRoot 'soak-watch.ps1'
$reportScript = Join-Path $PSScriptRoot 'soak-report.ps1'
$playArguments = @('-Offscreen', '-IsolateUserData', '-KeepServer', '-Character', 'BSD Mage', '-Resolution', $Resolution, '-TimeoutSeconds', "$timeoutSeconds", "-GameArgs:$gameArgs")
$watchArguments = @('-IntervalSeconds', '300', '-OutFile', (Join-Path $runFolder 'watch.csv'), '-GodotLogsDirectory', $godotLogs, '-ServerLogsDirectory', $serverLogs, '-DatabaseDirectory', $databaseDir, '-DurationSeconds', "$($timeoutSeconds + 600)", '-RunFolder', $runFolder) # 7-R3: the watcher ends itself (the play timeout plus 600 s) even when this script is killed hard and its finally never runs

# --- 3. The preconditions (read-only). ---
$problems = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) {
    $problems.Add('cache\backend-owner.txt does not exist in the main checkout: the item''s lead has not taken the backend (D54), and a soak never starts without it')
}
else {
    $backend = 'not read'
    try {
        $runtime = Find-LifestoneServerRuntime -RepoRoot $MainCheckout -AllowMissing
        if (-not $runtime) { $problems.Add('the main checkout has no server\runtime: the backend was never set up') }
        else {
            $serverRoot = Split-Path -Parent $runtime
            Import-Module (Join-Path $serverRoot 'lib\LifestoneServer.psm1') -Force -DisableNameChecking
            $state = Get-LsBackendState (Get-LsConfig)
            $backend = "mariadb=$([bool]$state.MariaDb) ace=$([bool]$state.Ace) mod_ready=$([bool]$state.ModReady)"
            if (-not $state.Ace) { $problems.Add('the backend is not up (ACE is not running): the lead starts it; a soak starts none') }
            elseif (-not $state.ModReady) { $problems.Add('ACE is up without the Lifestone mod ready (pids.json ace.modReady is not true)') }
            elseif (-not $state.MariaDb) { $problems.Add('ACE is up but mariadbd is not') }
        }
    }
    catch { $problems.Add("the backend's state could not be read ($($_.Exception.GetType().Name))") }
    $foreign = @(Get-ForeignGodot)
    foreach ($g in $foreign) { $problems.Add("a Godot process is running: pid $($g.Pid) started $($g.Started) args: $($g.Args)") }
}

Write-Host "[soak] main checkout (backend, lock, server folders): $MainCheckout"
Write-Host "[soak] this checkout (run folder, game, user data): $repoRoot"
Write-Host "[soak] run: $Minutes min$(if ($Until) { " (until $Until UTC)" }), $Resolution, a frame every $FrameSeconds s, rehearsal=$(if ($Rehearsal) { 'yes' } else { 'no' })"
Write-Host "[soak] run folder: $runFolder"
Write-Host '[soak] display: woken and held on for the whole run (tools\lib\GateHost.psm1); no power setting is changed'
Write-Host "[soak] watcher: tools\soak-watch.ps1 $($watchArguments -join ' ')"
Write-Host "[soak] game: tools\play.ps1 $(($playArguments | ForEach-Object { if ($_ -match '^-GameArgs:(.*)$') { "-GameArgs:'" + $Matches[1] + "'" } elseif ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ')"
Write-Host "[soak] game arguments: $gameArgs"
Write-Host "[soak] after the game: copy every lifestone*.log from $godotLogs into the run folder, then tools\soak-report.ps1 -RunFolder $runFolder"
Write-Host "[soak] lock: $lockFile"
if ($Record) {
    $ffmpeg = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ffmpeg) { $problems.Add('-Record: ffmpeg is not on PATH') }
    Write-Host "[soak] recording: the game window's client area by ffmpeg gdigrab, 30 fps, x264 ultrafast one thread ~1.8 Mbps, $RecordSegmentSeconds s files in $recordFolder; --video-sync-marker in the game arguments"
}
if ($problems.Count -eq 0) { Write-Host '[soak] preconditions: all met' }
else { foreach ($p in $problems) { Write-Host "[soak] precondition not met: $p" -ForegroundColor Yellow } }

if ($Plan) {
    Invoke-CheckpointAside -WhatIfOnly
    Write-Host '[soak] -Plan: nothing was started and nothing was created'
    exit 0
}
if ($problems.Count -gt 0) { Stop-Refused ($problems -join "`n  ") }

# --- The recording (-Record, D97). ---
function Find-SoakGameWindow([int] $Seconds) {
    # The game's top window and its client area in physical pixels (the thread made per-monitor DPI aware first), polled until it exists and lies wholly on the
    # virtual screen (godot.ps1 -Offscreen starts it at -20000,-20000; the game brings it on screen, and a grab of an off-screen area would record nothing).
    if (-not ('SoakRec.Win' -as [type])) {
        Add-Type -Namespace SoakRec -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
'@
    }
    [SoakRec.Win]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
    Add-Type -AssemblyName System.Windows.Forms
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($g in @(Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero -and $_.ProcessName -notlike '*console*' })) {
            $c = New-Object SoakRec.Win+RECT
            $p = New-Object SoakRec.Win+POINT
            if ([SoakRec.Win]::GetClientRect($g.MainWindowHandle, [ref]$c) -and [SoakRec.Win]::ClientToScreen($g.MainWindowHandle, [ref]$p) -and $c.R -ge 320 -and $c.B -ge 240 -and
                $p.X -ge $screen.Left -and $p.Y -ge $screen.Top -and ($p.X + $c.R) -le $screen.Right -and ($p.Y + $c.B) -le $screen.Bottom) {
                return [pscustomobject]@{ Pid = $g.Id; X = $p.X; Y = $p.Y; Width = ($c.R - ($c.R % 2)); Height = ($c.B - ($c.B % 2)) }
            }
        }
        Start-Sleep -Seconds 2
    }
    return $null
}

function Start-SoakRecording($Window, [int] $LimitSeconds) {
    New-Item -ItemType Directory -Force -Path $recordFolder | Out-Null
    $ffArgs = @('-hide_banner', '-loglevel', 'warning', '-stats', '-stats_period', '60', '-y', '-f', 'gdigrab', '-framerate', '30', '-draw_mouse', '0',
        '-offset_x', "$($Window.X)", '-offset_y', "$($Window.Y)", '-video_size', "$($Window.Width)x$($Window.Height)", '-i', 'desktop', '-t', "$LimitSeconds",
        '-c:v', 'libx264', '-preset', 'ultrafast', '-threads', '1', '-x264-params', 'threads=1', '-b:v', '1800k', '-maxrate', '2000k', '-bufsize', '4000k',
        '-pix_fmt', 'yuv420p', '-g', '300', '-f', 'segment', '-segment_time', "$RecordSegmentSeconds", '-reset_timestamps', '1',
        '-segment_list', (Join-Path $recordFolder 'segments.csv'), '-segment_list_type', 'csv', (Join-Path $recordFolder 'rec-%03d.ts'))
    $psi = [System.Diagnostics.ProcessStartInfo]::new($ffmpeg.Source)
    foreach ($a in $ffArgs) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $started = [DateTime]::UtcNow
    $proc = [System.Diagnostics.Process]::Start($psi)
    # ffmpeg's messages and its progress line (once a minute) are copied to the file by .NET itself: no PowerShell code runs on another thread.
    $log = [IO.File]::Create((Join-Path $recordFolder 'ffmpeg.log'))
    $copy = $proc.StandardError.BaseStream.CopyToAsync($log)
    [IO.File]::WriteAllText((Join-Path $recordFolder 'record.txt'), ("start_utc={0} unix_ms={1} area={2}x{3}+{4}+{5} game_pid={6} ffmpeg_pid={7} segment_s={8}`nffmpeg {9}`n" -f $started.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'), ([DateTimeOffset]$started).ToUnixTimeMilliseconds(), $Window.Width, $Window.Height, $Window.X, $Window.Y, $Window.Pid, $proc.Id, $RecordSegmentSeconds, ($ffArgs -join ' ')))
    Write-Host "[soak] recording started at $($started.ToString('HH:mm:ss')) UTC (ffmpeg pid $($proc.Id), area $($Window.Width)x$($Window.Height) at $($Window.X),$($Window.Y)) into $recordFolder"
    return [pscustomobject]@{ Process = $proc; Log = $log; Copy = $copy }
}

function Stop-SoakRecording($Recording) {
    if (-not $Recording) { return }
    $proc = $Recording.Process
    try {
        if (-not $proc.HasExited) {
            try { $proc.StandardInput.WriteLine('q'); $proc.StandardInput.Flush() } catch { }
            if (-not $proc.WaitForExit(5000)) { $proc.Kill($true); $proc.WaitForExit(5000) | Out-Null; Write-Host '[soak] recording: ffmpeg did not stop on q within 5 s and was killed (MPEG-TS files need no index, so the last one still plays)' -ForegroundColor Yellow }
        }
        $proc.WaitForExit()
    }
    catch { }
    try { $Recording.Copy.Wait(5000) | Out-Null } catch { }
    try { $Recording.Log.Dispose() } catch { }
    Write-Host "[soak] recording stopped (ffmpeg exit $(try { $proc.ExitCode } catch { 'unknown' }))"
}

function Get-SoakRecordingLine {
    $files = @(Get-ChildItem -LiteralPath $recordFolder -File -Filter 'rec-*.ts' -ErrorAction SilentlyContinue)
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { $bytes = 0 }
    return "recording=$runRelative/record files=$($files.Count) bytes=$bytes gb=$([math]::Round($bytes / 1GB, 2))"
}

# --- 4. The run. ---
New-Item -ItemType Directory -Force -Path $runFolder | Out-Null
Invoke-CheckpointAside
$runStart = [DateTime]::UtcNow
$hold = $null
$watcher = $null
$recording = $null
$exitCode = 2
try {
    $before = Get-GateDisplayState
    $woke = Invoke-GateDisplayWake
    $hold = Start-GateDisplayHold -Folder $runFolder
    $after = if ($woke) { Get-GateDisplayState } else { $null }
    Write-Host "[soak] display=$(Get-GateDisplayLine -Before $before -After $after -Held ($null -ne $hold) -Skipped $false)"

    $watchInfo = @{ FilePath = $pwshExe; PassThru = $true; WindowStyle = 'Hidden'; RedirectStandardOutput = (Join-Path $runFolder 'watch-console.txt'); RedirectStandardError = (Join-Path $runFolder 'watch-error.txt')
        ArgumentList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $watchScript) + $watchArguments }
    $watcher = Start-Process @watchInfo
    Write-Host "[soak] watcher started (pid $($watcher.Id))"

    $psi = [System.Diagnostics.ProcessStartInfo]::new($pwshExe)
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $playScript) + $playArguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $repoRoot
    Write-Host "[soak] starting the game at $([DateTime]::UtcNow.ToString('HH:mm:ss')) UTC"
    $child = [System.Diagnostics.Process]::Start($psi)
    if ($Record) {
        $window = Find-SoakGameWindow -Seconds 180
        if ($window) { $recording = Start-SoakRecording -Window $window -LimitSeconds ($timeoutSeconds + 600) }
        else { Write-Host '[soak] recording: no game window within 180 s; the run goes on unrecorded' -ForegroundColor Yellow }
    }
    $child.WaitForExit()
    $exitCode = $child.ExitCode
    Write-Host "[soak] the game run ended with exit code $exitCode"
}
catch {
    Write-Host "[soak] $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}
finally {
    Stop-SoakRecording $recording
    if ($watcher -and -not $watcher.HasExited) { try { $watcher.Kill($true); $watcher.WaitForExit(5000) | Out-Null } catch { } }
    Stop-GateDisplayHold -Process $hold

    $copied = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @(Get-ChildItem -LiteralPath $godotLogs -File -Filter 'lifestone*.log' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })) {
        $source = Join-Path $godotLogs $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $source).LastWriteTimeUtc -lt $runStart) {
            Write-Host "[soak] $name in the logs folder is older than this run and was not copied" -ForegroundColor Yellow
            continue
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $runFolder $name) -Force
        $copied.Add($name)
    }
    Write-Host "[soak] logs copied: $(if ($copied.Count) { $copied -join ', ' } else { 'none' })"

    $okField = 'n/a'
    try {
        & $pwshExe -NoProfile -ExecutionPolicy Bypass -File $reportScript -RunFolder $runFolder
        $reportFile = Join-Path $runFolder 'report.txt'
        if (Test-Path -LiteralPath $reportFile) {
            $m = [regex]::Match([IO.File]::ReadAllText($reportFile), '(?m)\bdone_ok=(\S+)')
            if ($m.Success) { $okField = $m.Groups[1].Value }
        }
    }
    catch { Write-Host "[soak] the report did not run ($($_.Exception.GetType().Name))" -ForegroundColor Red }
    if ($Record) {
        $recordingLine = Get-SoakRecordingLine
        Write-Host "[soak] $recordingLine"
        $reportFile = Join-Path $runFolder 'report.txt'
        if (Test-Path -LiteralPath $reportFile) { [IO.File]::AppendAllText($reportFile, "$recordingLine`n") }
    }
    # M5 7 G2: the alarms the watcher raised (soak-watch.ps1 writes <run folder>\ALARM.txt; absent when none)
    $alarmFile = Join-Path $runFolder 'ALARM.txt'
    # M5 7 lead (round 9 stopped itself between two watcher samples and raised no alarm): a game exit other than 0 is an alarm, with the autopilot's stop line from the copied log.
    if ($exitCode -ne 0) {
        $stopLine = ''
        $copiedLog = Join-Path $runFolder 'lifestone.log'
        if (Test-Path -LiteralPath $copiedLog -PathType Leaf) {
            $hit = Select-String -LiteralPath $copiedLog -Pattern '\[autopilot\] autopilot stop=' | Select-Object -Last 1
            if ($hit) { $stopLine = $hit.Line }
        }
        [IO.File]::AppendAllText($alarmFile, "$([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')) game-exit: the game ended with exit code $exitCode$(if ($stopLine) { ': ' + $stopLine } else { ' (no autopilot stop line in the log)' })`n")
    }
    if (Test-Path -LiteralPath $alarmFile -PathType Leaf) {
        Write-Host "[soak] ALARM.txt ($alarmFile):"
        foreach ($line in [IO.File]::ReadAllLines($alarmFile)) { Write-Host "[soak]   $line" }
    }
    else { Write-Host '[soak] ALARM.txt: no alarm was raised' }
    Write-Host "[soak] summary exit=$exitCode done_ok=$okField run=$runRelative report=$runRelative/report.txt minutes_planned=$Minutes"
}
exit $exitCode
