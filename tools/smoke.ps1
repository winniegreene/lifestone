<#
.SYNOPSIS
    The integration suite: build, tests and every Godot check harness, one after the other, with a summary table and a
    scan for leaked passwords.

.DESCRIPTION
    Runs, sequentially and off-screen (each Godot run through tools\godot.ps1 -Run -Offscreen -IsolateUserData with a
    timeout, so silent: Dummy audio):

      build                    dotnet build Lifestone.slnx (must report 0 warnings and 0 errors)
      test                     dotnet test Lifestone.slnx --filter "Category!=Loopback&Tier!=Soak" (must pass): the
                               loopback tests need the backend, and the soak tier runs only at merge gates
      cli-help                 --help prints the option registry and exits 0
      cli-bad-argument         an automated run with a mistyped option exits 1 at once
      terrain-golden           tools\terrain-golden.ps1 (GPU terrain against the CPU reference compositor)
      engine-checks            --engine-checks
      interior-engine-checks   --interior-engine-checks
      sky-checks               --sky-checks
      blend-checks             --blend-checks (byte-space blending, the decode, the detail pass, built-in materials)
      portal-space-checks      --portal-space-checks (the portal view in the engine: lens, lighting, byte-space composition, the
                               world lens, the UIASSET chain, the tunnel's hues, the sound streams, buses and mute, the hum, the offline sequence, the presenter)
      portal-space             --portal-space-frames=<OutDir>\portal-space (Holtburg to Arwic at a fixed 1/60 s step and seed;
                               four review frames: mid-expand, in the tunnel, mid-collapse, mid-arrival; fails on a frame over 50 ms;
                               its own limits (60 s to settle, 120 s to run) stay under the step's 240 s)
      retail-ui                the retail login screens, offline on stand-in data: --retail-ui-frames=<OutDir>\retail-ui\<W>x<H> at 800x600
                               (retail's native size; with -AllSizes also at 1920x1080; each through tools\godot.ps1 -Resolution; the run fails if the window is not the size asked for or
                               a frame is missing, blank or the wrong size; verdict "retail ui frames done size=<W>x<H> scale=<s> captured=<n>
                               failures=0 missing_images=<k>"; the one known unresolved retail image, DID 0, is named in its log), then the
                               retail UI layer's engine checks (--retail-ui-checks at 800x600, with --all-sizes in the shell placement at 1920x1080 and 2560x1440 too: tiles byte for byte at scales 1 to 3, nearest filtering,
                               no seams, the viewport hole, the cursor, the key table, the fonts, the real screens, input; review frames go to
                               cache\retail-ui-checks), which have no step of their own
      offline-player-checks    --offline-player-checks (the real offline player driven through its intents in the explore scene: a lap of Holtburg's
                               ring, a standing and a running jump, a walk into a wall, the stairs up into a building and the cellar's stairs down and
                               back out in the chase camera and first person; blocked and sliding, cell tracking through the doorways, MoveToState on
                               every key change, AutonomousPosition on the retail cadence and only on walkable ground, the camera never inside geometry,
                               no frame over 50 ms; one [walkcheck] PASS|FAIL line each and a final line)
      offline-player           --offline-player-frames=<OutDir>\player (four review frames of the offline player: the chase camera behind the character
                               in Holtburg, first person, inside a building, mid-jump, each taken when the state it is named for has been checked;
                               verdict "player frames done captured=4 failures=0 max_frame_ms=<x>")
      play-checks              --play-checks (play mode in the engine on a scripted stand-in server, no backend: the world waits for the player, the
                               player is built from its own CreateObject with the session's movement sink, the world streams and physics places it,
                               portal space plays and sends LoginComplete once per arrival, a server teleport (the scripted one, a tools place, a portal Use)
                               moves the world, the physics and the camera, a boot and a silent server end on the disconnected screen whose button
                               reconnects, a log off through Shift+Escape takes it all away; one [playcheck] PASS|FAIL line each and a final line)
      shell-frames             --play-checks --play-frames=<OutDir>\shell-frames\<W>x<H> at 800x600 (UI scale 1; with -AllSizes also 1920x1080 at scale 1 and 2560x1440 at scale 2), each through
                               tools\godot.ps1 -Resolution: the play checks on the stand-in server save the shell's ten review frames (portal space in the
                               SmartBox, the arrival, the spew box and chat, a NotInM2 message, an object selected with the vivid indicator, character information,
                               the options with side-by-side vitals, the logout confirmation, the automation inspector, the tools panel); the run fails if the window is not the size asked for or a frame is
                               missing, blank or the wrong size; verdict "shell frames done size=<W>x<H> scale=<s> captured=10 failures=0 frame_time=reported" (a frames
                               run is not a timing run: the 50 ms judgement is reported, not judged); the step also checks the ten files exist
      login-check              --account=test --login-check=all (needs the local backend: the whole first login of the test account, off-screen: the data
                               check, the character list, select or create, Enter, portal space, the arrival in the Training Academy, LoginComplete once, the
                               unhide, a 5 s wait, then with the game's own player the walk to the Academy's exit portal and its Use (the Sentry's quest stamped for it through the
                               server mod; ACE teleports it to Holtburg, exit=teleported, and the portal's own emote set erases the quest, moves the sanctuary to Holtburg and enables
                               recalls: the test character is graduated for good, only its position and that quest are left as found, quest=put-back) and three laps of Holtburg after a server teleport, each ending with the character back at its start, then a log off
                               through retail's key (Shift+Escape) back to the list and quit; about 2.5 minutes; verdict "[logincheck] done ok=true enter_to_walk_max_ms=<x>
                               holtburg_min_fps=<x> corrections=0 ... logoff=ok materialised=true fade_ms=<ms, judged 650-900 by the game> objects_drawn=<n> tunnel_seconds=<x> portal_seconds=<x> tunnel_covered_frames=<n> logoff_tunnel=held portal_sounds=ok"; the portal rules are judged
                               (the arrival tunnel visible 5 s or more, the logoff tunnel held until the character list, the enter and exit whooshes once each and the hum at least once); the frame rules are judged: no frame
                               over 50 ms between Enter and walking (the first 3 frames after the character list appears excluded) nor around the legs' teleports,
                               60 fps or more in every second of the laps)
      login                    tools\play.ps1 -Offscreen --login-check=enter (needs the local backend, exactly as login-check, but through the
                               player's own entry point: the whole first login of the test account with the server's objects drawn: the
                               Academy's census, the player materialising from its bubbles, four review frames under docs\screenshots\m2\login
                               (gitignored: portal space live, the arrival among the Academy's objects, the shell after the settle, the character list after the log off), and
                               no frame over 50 ms between Enter and walking; verdict "[logincheck] done ok=true ... materialised=true
                               objects_drawn=<n>"; SKIP when the backend is not ready under -Backend auto)
      login-throwaway          --account=test --login-check=throwaway (docs/session.md section 28; needs the local backend and the server mod with the purge verb; runs ONLY with -Backend require,
                               SKIP under auto and start): creates 'Lifestone Throwaway' through the retail character-creation screens (ACE's two name refusals in retail dialogs first), uses the tools panel
                               (Where am I, Add XP 2,500, a Places teleport), logs off, deletes, restores and deletes it, enters 'Lifestone Smoke', purges it and checks the list is as it began;
                               verdict "[logincheck] throwaway done ok=true throwaway=purged name_errors=2 xp=added where_m=<x> place=teleported deletes=2 restores=1 list=same leftover=<yes|no>
                               enter_to_walk_max_ms=<x> teleport_frame_max_ms=<x>" (the two frame figures judged against 50)
      spell-showcase           --scene=spellviewer --showcase (four entries; must end with exceptions=0)
      golden-viewpoints        --golden-viewpoints (every viewpoint captured)
      fly-path                 --fly-path=holtburg:arwic:<FlySpeed> (DoD 8: no frame over 50 ms; exit 2 if one is)
      stray-output             no image, video, audio, .bin or .lsrec file appeared under src/ or tests/ during the run

    Every step's console output goes to <OutDir>\<nn>-<step>.log, outputs (golden images, terrain-golden diffs) below
    <OutDir>, and the table to <OutDir>\summary.md. Exits 0 when no step failed and the secret scan found nothing, 1
    otherwise.

    A step ends PASS, FAIL or SKIP. SKIP (yellow) is not a failure, and the summary counts it apart: a step that needs
    the local backend (server\README.md) is skipped while the backend is not ready, unless -Backend says otherwise. No
    step that needs it joins with its harness (login-check is the first). Ready means what the loopback
    tests' BackendProbe means: the main checkout's server\runtime\pids.json names a live mariadbd and a live ACE whose
    entry has modReady = true, ACE listens on UDP 127.0.0.1:<acePort of server\versions.json>, and secrets.json has an
    accounts.test entry (checked by key; no value is read). The check sends nothing to the server. The summary always
    shows the backend's state.

    After the steps, the secret scan reads every password in the main checkout's server\runtime\secrets.json (the
    account and database passwords, schema 1 or 2) into memory only and counts where one appears, as UTF-8 or UTF-16LE
    bytes, in this run's OutDir, the isolated Godot logs (cache\godot\**\logs), session recordings (cache\sessions) and
    ACE's logs (server\runtime\logs\ACE_Log*.txt, rolled copies too). The summary's 'secrets=<n>' line gives the count
    and, for each hit, the file and the secrets.json key that matched, never a value. Any hit, or a file or folder that
    could not be read or listed, fails the run.

    A Godot step also fails when Lifestone logged an ERROR line during it (the "Errors logged" column), except
    cli-bad-argument, whose ERROR is the point: false ERRORs (shutdown cancellations) once went unnoticed in passing runs.

    The fly path is judged as the DoD states it: a frame over 50 ms fails the step. On a shared machine other processes
    (another Godot instance starting) can stall presentation for 50-140 ms with no Lifestone work in the frame (perf
    audit, dod8-verdict-polluted-by-environment), so the step first waits (up to 3 min) until no other Godot process
    has run for 5 s, and reports for each attempt the slowest frame with Lifestone work (a main-thread drain of 1 ms or
    more, a GC, an attach or a rebase) and the slowest without, plus how many other Godot processes were running. It is
    retried once ONLY when every frame over 50 ms had no Lifestone work in it; a frame over 50 ms with Lifestone work (a
    GC pause, a long drain), or a flight whose frames cannot be attributed, fails at once. A pass on the retry shows as
    "PASS (retry)". It never stops other processes.

    Environment changes (DOTNET_ROOT, PATH, opt-outs) apply ONLY to the child processes this script starts. Nothing
    is written outside the repository (with -Backend start, the backend's own scripts write the main checkout's
    server\runtime); no environment, registry or system setting is changed.

.PARAMETER OutDir
    Folder for logs and outputs. Default: <repo>\cache\smoke (gitignored). Recreated on every run.

.PARAMETER Only
    Run only these steps (names as above), e.g. -Only engine-checks,sky-checks. The build still runs unless -NoBuild.

.PARAMETER Skip
    Skip these steps.

.PARAMETER NoBuild
    Use the existing build (skips the build step).

.PARAMETER Backend
    What a step that needs the local backend does when the backend is not ready:
      auto     (default) it is skipped (SKIP). Smoke never starts the backend.
      require  it fails (FAIL): for merge gates that must exercise the backend.
      start    the main checkout's server\start-server.ps1 runs before the first such step (only when a selected step
               needs the backend), and after the run server\stop-server.ps1 stops only what this run started; a step
               still fails if the backend is not ready (owner decision D12).

.PARAMETER FlySpeed
    Fly-path speed in m/s (default 200, about Shift-speed in free-fly: a 30 s flight).

.PARAMETER JudgeLifestoneFramesOnly
    Fly path: pass when every frame over 50 ms had no Lifestone work in it (main-thread drain under 1 ms, no GC, attach
    or rebase), i.e. judge DoD 8 on the attributed maximum (perf audit, dod8-verdict-polluted-by-environment), shown as
    "PASS (attributed)". Off by default: the DoD's own wording judges the raw maximum (with the one retry above).

.PARAMETER AllSizes
    Owner rule (2026-10-14): tests, checks and frame runs stay at retail's native 800x600 (the play-checks, retail-ui, shell-frames,
    login-check and login steps pass -Resolution 800x600), and several resolutions are a polish step at the end of the project.
    -AllSizes is the opt-in: the retail-ui frames also at 1920x1080, the shell frames also at 1920x1080 and 2560x1440, and the
    shell's [uicheck] placement also at those sizes (--all-sizes). Never run by default.

.PARAMETER DotnetRoot
    The .NET SDK root (the folder holding dotnet.exe). Default, the first that exists: $env:LIFESTONE_DOTNET_ROOT, the
    per-user install %LOCALAPPDATA%\Microsoft\dotnet, $env:DOTNET_ROOT, then the folder of dotnet.exe on PATH
    (tools\lib\Toolchain.psm1). The Godot steps find Godot as tools\godot.ps1 does.

.EXAMPLE
    pwsh -File tools\smoke.ps1

.EXAMPLE
    pwsh -File tools\smoke.ps1 -NoBuild -Only engine-checks,interior-engine-checks,sky-checks,blend-checks

.EXAMPLE
    pwsh -File tools\smoke.ps1 -Backend require
    A merge gate: a step that needs the backend fails instead of being skipped when the backend is down.
#>
[CmdletBinding()]
param(
    [string] $OutDir = '',
    [string[]] $Only = @(),
    [string[]] $Skip = @(),
    [switch] $NoBuild,
    [ValidateSet('auto', 'start', 'require')]
    [string] $Backend = 'auto',
    [ValidateRange(10, 2000)]
    [int] $FlySpeed = 200,
    [switch] $JudgeLifestoneFramesOnly,
    [switch] $AllSizes,
    [string] $DotnetRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$godotScript = Join-Path $PSScriptRoot 'godot.ps1'
$playScript = Join-Path $PSScriptRoot 'play.ps1'
$goldenScript = Join-Path $PSScriptRoot 'terrain-golden.ps1'
$pwshExe = (Get-Process -Id $PID).Path
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'cache\smoke' }
$OutDir = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutDir)
# Owner rule (2026-10-14): every Godot check and frame run stays at retail's native 800x600; -AllSizes adds the larger sizes (a polish step at the end of the project).
$defaultSize = '800x600'
$frameSizes = if ($AllSizes) { @('800x600', '1920x1080') } else { @('800x600') }
$shellFrameSizes = if ($AllSizes) { @('800x600', '1920x1080', '2560x1440') } else { @('800x600') }
Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot
$dotnetExe = Join-Path $DotnetRoot 'dotnet.exe'

$allSteps = @('build', 'test', 'cli-help', 'cli-bad-argument', 'terrain-golden', 'engine-checks', 'interior-engine-checks',
    'sky-checks', 'blend-checks', 'portal-space-checks', 'portal-space', 'retail-ui', 'offline-player-checks', 'offline-player', 'play-checks', 'shell-frames', 'login-check', 'login', 'login-throwaway', 'spell-showcase', 'golden-viewpoints', 'fly-path', 'stray-output')
# The steps that need the local backend (gated by -Backend).
$backendSteps = @('login-check', 'login', 'login-throwaway')
# The default test run leaves out the loopback tests (they need the backend) and the soak tier (merge gates only).
$testFilter = 'Category!=Loopback&Tier!=Soak'
# 'pwsh -File' passes '-Only a,b' as one string: split on commas either way.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Skip = @($Skip | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in @($Only) + @($Skip)) {
    if ($name -and $allSteps -notcontains $name) { throw "Unknown step '$name'. Steps: $($allSteps -join ', ')" }
}

# The output folder is ours alone: refuse anything outside the repository's cache\ (it is deleted and recreated).
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'cache')) + [IO.Path]::DirectorySeparatorChar
if (-not ([IO.Path]::GetFullPath($OutDir) + [IO.Path]::DirectorySeparatorChar).StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutDir must be inside $cacheRoot (it is recreated on every run): $OutDir"
}
if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Environment for child processes only (as tools\godot.ps1 does). LIFESTONE_DOTNET_ROOT makes the tools\godot.ps1 and
# tools\terrain-golden.ps1 children use the same SDK as the build and test steps.
$childEnv = [ordered]@{
    LIFESTONE_DOTNET_ROOT              = $DotnetRoot
    DOTNET_ROOT                        = $DotnetRoot
    PATH                               = "$DotnetRoot;$env:PATH"
    DOTNET_CLI_TELEMETRY_OPTOUT        = '1'
    DOTNET_NOLOGO                      = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE  = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH    = 'false'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    MSBUILDDISABLENODEREUSE            = '1'
    DOTNET_CLI_USE_MSBUILD_SERVER      = '0'
    UseSharedCompilation               = 'false'
}

$script:results = [System.Collections.Generic.List[object]]::new()
$script:stepIndex = 0
# -Backend start: what was running before and what this run started (Start-SmokeBackend, Stop-SmokeBackend).
$script:backendStart = $null
$script:serverModule = $null

function Invoke-Logged {
    <#
      Runs one child process with its output (stdout and stderr) captured into $LogPath; returns the exit code (124 when
      it was killed after $TimeoutSeconds). A timeout kills the whole process tree, or with -KillRootOnly only the child
      itself: the backend scripts start mariadbd and ACE detached, but a tree kill (by parent PID) still reaches them, and
      a MariaDB killed in its shutdown leaves InnoDB to crash-recover.
    #>
    param([string] $FileName, [string[]] $Arguments, [string] $LogPath, [int] $TimeoutSeconds, [switch] $KillRootOnly)
    $psi = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $repoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in $childEnv.Keys) { $psi.Environment[$key] = [string]$childEnv[$key] }
    "> $FileName $($Arguments -join ' ')" | Set-Content -LiteralPath $LogPath -Encoding utf8
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill(-not $KillRootOnly)
        $process.WaitForExit()
        # After a root-only kill, a grandchild that inherited the output pipes can hold them open: wait 30 s at most.
        $drained = [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdout, $stderr), 30000)
        $output = @(if ($drained) { $stdout.Result, $stderr.Result } else { '[smoke] the output was still open 30 s after the kill (not captured)' })
        $note = if ($KillRootOnly) { ' (only the script: the processes it started were left running)' } else { '' }
        Add-Content -LiteralPath $LogPath -Value ($output + "[smoke] killed after $TimeoutSeconds s$note") -Encoding utf8
        return 124
    }
    $process.WaitForExit()
    Add-Content -LiteralPath $LogPath -Value $stdout.Result, $stderr.Result -Encoding utf8
    return $process.ExitCode
}

function Invoke-Godot {
    <# One off-screen game run through tools\godot.ps1 (at -Resolution WxH when given); returns @{ Code; Log; Text }. #>
    param(
        [string] $Step,
        [string] $GameArgs,
        [int] $TimeoutSeconds,
        [string] $Suffix = '',
        [ValidatePattern('^(\d+x\d+)?$')]
        [string] $Resolution = ''
    )
    $log = Join-Path $OutDir ('{0:00}-{1}{2}.log' -f $script:stepIndex, $Step, $Suffix)
    $arguments = @('-NoProfile', '-File', $godotScript, '-Run', '-Offscreen', '-IsolateUserData', '-NoBuild',
        '-TimeoutSeconds', "$TimeoutSeconds")
    if ($Resolution) { $arguments += @('-Resolution', $Resolution) }
    $arguments += "-GameArgs:$GameArgs"
    $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath $log -TimeoutSeconds ($TimeoutSeconds + 60)
    return @{ Code = $code; Log = $log; Text = (Get-Content -LiteralPath $log -Raw) }
}

function Get-ErrorCount([string] $text) {
    # Lifestone's own ERROR entries (Log.Error: '<utc>Z ERROR [category] ...'); Godot's engine messages are not counted.
    return ([regex]::Matches($text, 'Z ERROR \[')).Count
}

function Add-Result {
    <#
      Records one step. $Errors (Lifestone ERROR lines the step logged; -1 = not counted) above 0 fails the step.
      $PassNote qualifies a pass ('retry', 'attributed') so it is visible in the Result column, not only in Detail.
      -Skipped records a SKIP: the step did not run ($Detail says why), which is not a failure. -NotRun leaves the Exit
      column empty for a step that did not run (a SKIP, or a FAIL because the backend was not ready).
    #>
    param([string] $Step, [bool] $Pass, [int] $Code, [double] $Seconds, [string] $Detail, [string] $Log = '', [int] $Errors = -1,
        [string] $PassNote = '', [switch] $Skipped, [switch] $NotRun)
    if ($Errors -gt 0) {
        $Pass = $false
        $Detail = "$Detail; FAIL: $Errors ERROR line(s) logged"
    }
    $outcome = if ($Skipped) { 'SKIP' } elseif ($Pass) { 'PASS' } else { 'FAIL' }
    $result = if ($outcome -eq 'PASS' -and $PassNote) { "PASS ($PassNote)" } else { $outcome }
    $script:results.Add([pscustomobject]@{
            Step    = $Step
            Result  = $result
            Outcome = $outcome
            Passed  = $outcome -ne 'FAIL'
            Exit    = if ($Skipped -or $NotRun) { '' } else { "$Code" }
            Seconds = [math]::Round($Seconds, 1)
            Errors  = if ($Errors -lt 0) { '' } else { "$Errors" }
            Detail  = $Detail
            Log     = if ($Log) { [IO.Path]::GetFileName($Log) } else { '' }
        })
    $colour = if ($outcome -eq 'FAIL') { 'Red' } elseif ($outcome -eq 'SKIP' -or $PassNote) { 'Yellow' } else { 'Green' }
    Write-Host ("[smoke] {0,-24} {1}  ({2:0.0} s) {3}" -f $Step, $result, $Seconds, $Detail) -ForegroundColor $colour
}

function Test-Wanted([string] $step) {
    if ($Skip -contains $step) { return $false }
    if ($Only.Count -gt 0 -and $Only -notcontains $step -and -not ($step -eq 'build' -and -not $NoBuild)) { return $false }
    return $true
}

function Get-ExceptionKind($errorRecord) {
    <# The innermost exception's type name: what a secrets.json error may report (a parser message can quote the text). #>
    $exception = $errorRecord.Exception
    while ($exception.InnerException) { $exception = $exception.InnerException }
    return $exception.GetType().Name
}

function Import-ServerModule([string] $runtime) {
    <# The read-only helpers of the main checkout's server\lib\LifestoneServer.psm1 (Get-LsConfig, Get-LsPids, ...). #>
    $module = Join-Path (Split-Path -Parent $runtime) 'lib\LifestoneServer.psm1'
    if ($script:serverModule -ne $module) {
        Import-Module $module -Force
        $script:serverModule = $module
    }
}

function Get-BackendProcesses([string] $runtime) {
    <# Which backend processes run, per pids.json (PID, path and start time must all match: Get-LsLiveProcess). #>
    Import-ServerModule $runtime
    $cfg = Get-LsConfig
    $pids = Get-LsPids $cfg
    $state = @{ Config = $cfg; Pids = $pids; MariaDb = $null; Ace = $null }
    if ($pids) {
        if ($pids.PSObject.Properties['mariadbd']) { $state.MariaDb = Get-LsLiveProcess $pids.mariadbd }
        if ($pids.PSObject.Properties['ace']) { $state.Ace = Get-LsLiveProcess $pids.ace }
    }
    return $state
}

function Test-BackendReady {
    <#
      Whether the local backend is ready for a step that logs in: the rule of the loopback tests' BackendProbe. Read-only:
      it reads pids.json and the key names of secrets.json (never a value), and sends nothing to the server.
      Returns @{ Ready; Reason }; Reason says what is missing first.
    #>
    $runtime = Find-LifestoneServerRuntime -RepoRoot $repoRoot -AllowMissing
    if (-not $runtime) { return @{ Ready = $false; Reason = 'the main checkout has no server\runtime (server\setup-server.ps1 never ran)' } }
    try {
        $state = Get-BackendProcesses $runtime
        if (-not $state.Pids) { return @{ Ready = $false; Reason = 'not running (no server\runtime\pids.json)' } }
        if (-not $state.MariaDb) { return @{ Ready = $false; Reason = 'MariaDB is not running (pids.json)' } }
        if (-not $state.Ace) { return @{ Ready = $false; Reason = 'ACE is not running (pids.json)' } }
        $ace = $state.Pids.ace
        if (-not ($ace.PSObject.Properties['modReady'] -and $ace.modReady -is [bool] -and $ace.modReady)) {
            return @{ Ready = $false; Reason = 'ACE runs without a ready Lifestone server mod (pids.json ace.modReady is not true)' }
        }
        $port = [int]$state.Config.AcePort
        $listening = @([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners() |
                Where-Object { $_.Port -eq $port -and $_.Address.ToString() -eq '127.0.0.1' })
        if ($listening.Count -eq 0) { return @{ Ready = $false; Reason = "nothing listens on UDP 127.0.0.1:$port" } }
    }
    catch { return @{ Ready = $false; Reason = "the check failed: $($_.Exception.Message)" } }

    # The account the backend steps log in with, by key only: the document is parsed, but no value is read out of it.
    $secretsPath = Join-Path $runtime 'secrets.json'
    if (-not (Test-Path -LiteralPath $secretsPath -PathType Leaf)) { return @{ Ready = $false; Reason = 'no server\runtime\secrets.json' } }
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($secretsPath))
        $accounts = [System.Text.Json.JsonElement]::new()
        $test = [System.Text.Json.JsonElement]::new()
        $hasTest = $document.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
            $document.RootElement.TryGetProperty('accounts', [ref] $accounts) -and
            $accounts.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
            $accounts.TryGetProperty('test', [ref] $test)
        if (-not $hasTest) {
            return @{ Ready = $false; Reason = 'secrets.json has no accounts.test (schema 2 comes with the server mod; server\setup-server.ps1 migrates)' }
        }
    }
    catch { return @{ Ready = $false; Reason = "secrets.json could not be parsed ($(Get-ExceptionKind $_))" } }
    finally { if ($document) { $document.Dispose() } }
    return @{ Ready = $true; Reason = 'ready' }
}

function Start-SmokeBackend {
    <#
      -Backend start, before the first step that needs the backend (once per run): runs the main checkout's
      server\start-server.ps1 unless both servers already run, and records which ones this run started, so
      Stop-SmokeBackend stops only those (D12, D13). start-server.ps1 returns once ACE is up.
    #>
    if ($script:backendStart) { return }
    $script:backendStart = @{ Runtime = $null; StartedMariaDb = $false; StartedAce = $false; Note = '' }
    $runtime = Find-LifestoneServerRuntime -RepoRoot $repoRoot -AllowMissing
    if (-not $runtime) {
        $script:backendStart.Note = 'not started: the main checkout has no server\runtime (run server\setup-server.ps1 first)'
        return
    }
    $script:backendStart.Runtime = $runtime
    try {
        $before = Get-BackendProcesses $runtime
        if ($before.MariaDb -and $before.Ace) {
            $script:backendStart.Note = 'already running: nothing started, nothing will be stopped'
            return
        }
        $startScript = Join-Path (Split-Path -Parent $runtime) 'start-server.ps1'
        Write-Host "[smoke] -Backend start: $startScript (log: backend-start.log)"
        # start-server.ps1's own worst case is about 1050 s: MariaDB readiness (150 s), the ACE boot (-TimeoutSec), then
        # its failure cleanup (ACE 15 s, a clean MariaDB shutdown up to 280 s). Smoke's timeout stays well above that,
        # and -KillRootOnly keeps it from ever reaching the servers the script started.
        $aceBootSeconds = 600
        $code = Invoke-Logged -FileName $pwshExe -Arguments @('-NoProfile', '-File', $startScript, '-TimeoutSec', "$aceBootSeconds") `
            -LogPath (Join-Path $OutDir 'backend-start.log') -TimeoutSeconds ($aceBootSeconds + 900) -KillRootOnly
        $after = Get-BackendProcesses $runtime
        $script:backendStart.StartedMariaDb = [bool]($after.MariaDb -and -not $before.MariaDb)
        $script:backendStart.StartedAce = [bool]($after.Ace -and -not $before.Ace)
        $started = @()
        if ($script:backendStart.StartedMariaDb) { $started += 'MariaDB' }
        if ($script:backendStart.StartedAce) { $started += 'ACE' }
        $exited = if ($code -eq 124) { "timed out after $($aceBootSeconds + 900) s" } else { "exited $code" }
        $script:backendStart.Note = "start-server.ps1 $exited (backend-start.log); started: " +
            $(if ($started.Count) { $started -join ' and ' } else { 'nothing' })
    }
    catch { $script:backendStart.Note = "the start failed: $($_.Exception.Message)" }
}

function Stop-SmokeBackend {
    <#
      After the run, -Backend start: stops what Start-SmokeBackend started and nothing else. When only ACE was started
      (MariaDB already ran), that needs server\stop-server.ps1 -AceOnly; a stop-server.ps1 without it leaves ACE
      running, loudly. MariaDB started under an ACE that already ran is left running too (ACE needs it).
    #>
    $start = $script:backendStart
    if (-not $start -or -not ($start.StartedMariaDb -or $start.StartedAce)) { return }
    $stopScript = Join-Path (Split-Path -Parent $start.Runtime) 'stop-server.ps1'
    $arguments = @('-NoProfile', '-File', $stopScript)
    if (-not $start.StartedAce) {
        $start.Note += '; left running: the MariaDB this run started (ACE ran before and needs it)'
        Write-Warning "[smoke] $($start.Note)"
        return
    }
    if (-not $start.StartedMariaDb) {
        if (-not (Get-Command -Name $stopScript).Parameters.ContainsKey('AceOnly')) {
            $start.Note += '; TODO(server-mod SM6): stop-server.ps1 has no -AceOnly yet, so the ACE this run started is left running (MariaDB ran before)'
            Write-Warning "[smoke] $($start.Note)"
            return
        }
        $arguments += '-AceOnly'
    }
    Write-Host "[smoke] -Backend start: $stopScript$(if ($arguments -contains '-AceOnly') { ' -AceOnly' }) (log: backend-stop.log)"
    # stop-server.ps1 waits up to 120 s for each server before it terminates one (about 540 s at worst for one ACE and
    # one MariaDB); -KillRootOnly: a timeout must not kill a MariaDB in the middle of its clean shutdown.
    $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath (Join-Path $OutDir 'backend-stop.log') `
        -TimeoutSeconds 900 -KillRootOnly
    $start.Note += "; stop-server.ps1$(if ($arguments -contains '-AceOnly') { ' -AceOnly' }) exited $code (backend-stop.log)"
}

function Test-BackendGate([string] $step, [System.Diagnostics.Stopwatch] $timer) {
    <#
      Before a step that needs the backend: with -Backend start, starts it first (once per run). Returns $true when the
      backend is ready; otherwise records the step as SKIP (auto) or FAIL (require, start) with the reason.
    #>
    if ($Backend -eq 'start') { Start-SmokeBackend }
    $state = Test-BackendReady
    if ($state.Ready) { return $true }
    $detail = "backend not ready: $($state.Reason)"
    if ($script:backendStart -and $script:backendStart.Note) { $detail += " ($($script:backendStart.Note))" }
    if ($Backend -eq 'auto') { Add-Result $step $true 0 $timer.Elapsed.TotalSeconds "$detail; -Backend auto skips it" -Skipped }
    else { Add-Result $step $false 0 $timer.Elapsed.TotalSeconds "$detail; -Backend $Backend fails it" -NotRun }
    return $false
}

function Get-SecretPasswords {
    <# Every non-empty string under a key that contains 'password', anywhere in secrets.json: schema 1
       (mariadb.rootPassword, mariadb.acePassword, testAccount.password) and schema 2 (accounts.<name>.password, the
       mariadb ones unchanged). System.Text.Json keeps every string as written (ConvertFrom-Json would turn a date-like
       one into a DateTime). #>
    param([System.Text.Json.JsonElement] $Element, [string] $Path, [System.Collections.Generic.List[object]] $Into)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        foreach ($property in $Element.EnumerateObject()) {
            $childPath = if ($Path) { "$Path.$($property.Name)" } else { $property.Name }
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                Get-SecretPasswords -Element $property.Value -Path $childPath -Into $Into
            }
            elseif ($property.Name -match 'password') {
                $value = $property.Value.GetString()
                if ($value) { $Into.Add([pscustomobject]@{ Key = $childPath; Value = $value }) }
            }
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Get-SecretPasswords -Element $item -Path "$Path[$index]" -Into $Into
            $index++
        }
    }
}

function Get-NeedleHits {
    <#
      How often each needle (a byte string held as Latin-1 text, one char per byte) occurs in the file. The file is read
      in 4 MiB chunks that overlap by the longest needle less one byte; a match lying wholly in the carried overlap was
      counted with the previous chunk, so each match counts once. Opened with full sharing, so a log still being written
      (ACE's) can be read.
    #>
    param([string] $Path, [string[]] $Needles)
    $counts = [int[]]::new($Needles.Count)
    $overlap = [int](($Needles | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum) - 1
    $buffer = [byte[]]::new(4MB + $overlap)
    $latin1 = [Text.Encoding]::Latin1
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
    try {
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, $buffer.Length - $carry)) -gt 0) {
            $length = $carry + $read
            $text = $latin1.GetString($buffer, 0, $length)
            for ($n = 0; $n -lt $Needles.Count; $n++) {
                $needle = $Needles[$n]
                $at = $text.IndexOf($needle, [StringComparison]::Ordinal)
                while ($at -ge 0) {
                    if ($at + $needle.Length -gt $carry) { $counts[$n]++ }
                    $at = $text.IndexOf($needle, $at + 1, [StringComparison]::Ordinal)
                }
            }
            $carry = [math]::Min($overlap, $length)
            [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry)
        }
    }
    finally { $stream.Dispose() }
    return , $counts
}

function Measure-SecretLeaks {
    <#
      The secret scan (see the description above). The passwords stay in this function's memory; the result holds
      counts, file names and the secrets.json keys that matched, never a value, and a secrets.json error names only its
      exception type. Returns @{ Hits (-1: the scan could not run); Files; Scanned; Secrets; Unreadable; Note }.
    #>
    $result = @{ Hits = 0; Files = [System.Collections.Generic.List[string]]::new(); Scanned = 0; Secrets = 0
        Unreadable = [System.Collections.Generic.List[string]]::new(); Note = '' }
    $runtime = Find-LifestoneServerRuntime -RepoRoot $repoRoot -AllowMissing
    $secretsPath = if ($runtime) { Join-Path $runtime 'secrets.json' } else { '' }
    if (-not $secretsPath -or -not (Test-Path -LiteralPath $secretsPath -PathType Leaf)) {
        $result.Note = 'no server\runtime\secrets.json in the main checkout, so no password to look for'
        return $result
    }

    $passwords = [System.Collections.Generic.List[object]]::new()
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($secretsPath))
        Get-SecretPasswords -Element $document.RootElement -Path '' -Into $passwords
    }
    catch {
        $result.Hits = -1
        $result.Note = "secrets.json could not be read ($(Get-ExceptionKind $_)), so nothing was scanned"
        return $result
    }
    finally { if ($document) { $document.Dispose() } }
    if ($passwords.Count -eq 0) {
        $result.Hits = -1
        $result.Note = 'secrets.json holds no password (an unknown schema?), so nothing was scanned'
        return $result
    }
    # Each distinct password as its UTF-8 and its UTF-16LE bytes; Keys names the secrets.json keys holding it.
    $latin1 = [Text.Encoding]::Latin1
    $needles = [System.Collections.Generic.List[string]]::new()
    $needleKeys = [System.Collections.Generic.List[string]]::new()
    foreach ($group in ($passwords | Group-Object -Property Value -CaseSensitive)) {
        $keys = ($group.Group | ForEach-Object { $_.Key }) -join ', '
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            $needles.Add($latin1.GetString($encoding.GetBytes([string]$group.Name)))
            $needleKeys.Add($keys)
        }
    }
    $result.Secrets = $needles.Count / 2
    $passwords.Clear()

    # Where runs leave text behind: this run's outputs, Godot's logs, session recordings and ACE's logs. A folder that
    # cannot be listed (access denied, a path too long) lands in $listErrors and counts as unreadable: its files were
    # not scanned, so it must not pass as clean.
    $mainCheckout = Split-Path -Parent (Split-Path -Parent $runtime)
    $targets = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $listErrors = [System.Collections.ArrayList]::new()
    $addFiles = {
        param([string] $root, $files)
        foreach ($file in $files) {
            if ($seen.Add($file.FullName)) { $targets.Add(@{ Path = $file.FullName; Name = [IO.Path]::GetRelativePath($root, $file.FullName) }) }
        }
    }
    & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $OutDir -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors)
    $godotData = Join-Path $repoRoot 'cache\godot'
    if (Test-Path -LiteralPath $godotData) {
        foreach ($logs in @(Get-ChildItem -LiteralPath $godotData -Recurse -Directory -Force -Filter 'logs' -ErrorAction SilentlyContinue -ErrorVariable +listErrors)) {
            & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $logs.FullName -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors)
        }
    }
    $sessions = Join-Path $repoRoot 'cache\sessions'
    if (Test-Path -LiteralPath $sessions) {
        & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $sessions -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors)
    }
    $aceLogs = Join-Path $runtime 'logs'
    if (Test-Path -LiteralPath $aceLogs) {
        # ACE_Log.txt, ACE_Log-WARN.txt and log4net's rolled copies (ACE_Log.txt.1, ...).
        & $addFiles $mainCheckout @(Get-ChildItem -LiteralPath $aceLogs -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors |
                Where-Object { $_.Name -match '^ACE_Log.*\.txt(\.\d+)?$' })
    }
    foreach ($listError in $listErrors) {
        # The folder relative to this checkout (or, for ACE's logs, the main checkout), and the exception type only.
        $where = '(unknown folder)'
        if ($listError.TargetObject -is [string] -and $listError.TargetObject) {
            $path = [string]$listError.TargetObject
            $root = if ($path.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { $repoRoot } else { $mainCheckout }
            $where = if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { [IO.Path]::GetRelativePath($root, $path) } else { [IO.Path]::GetFileName($path) }
        }
        $result.Unreadable.Add("$where (not listed: $(Get-ExceptionKind $listError))")
    }

    foreach ($target in $targets) {
        try { $counts = Get-NeedleHits -Path $target.Path -Needles $needles.ToArray() }
        catch {
            $result.Unreadable.Add("$($target.Name) ($(Get-ExceptionKind $_))")
            continue
        }
        $result.Scanned++
        $fileHits = 0
        $fileKeys = [System.Collections.Generic.List[string]]::new()
        for ($n = 0; $n -lt $counts.Length; $n++) {
            if ($counts[$n] -eq 0) { continue }
            $fileHits += $counts[$n]
            $encoding = if ($n % 2 -eq 0) { 'UTF-8' } else { 'UTF-16LE' }
            $fileKeys.Add("$($needleKeys[$n]) as $encoding")
        }
        if ($fileHits -gt 0) {
            $result.Hits += $fileHits
            $result.Files.Add("$($target.Name): $fileHits ($($fileKeys -join '; '))")
        }
    }
    $needles.Clear()
    return $result
}

function Get-StrayOutputs {
    <# Image, video, audio, .bin and .lsrec files under src/ and tests/ (build output folders excluded): renders,
       sound dumps, fixtures or session recordings that escaped the rule (they belong under cache/). #>
    $roots = @('src', 'tests') | ForEach-Object { Join-Path $repoRoot $_ }
    $extensions = @('.png', '.jpg', '.jpeg', '.webp', '.avi', '.mp4', '.wav', '.mp3', '.ogg', '.flac', '.bin', '.lsrec')
    return @(Get-ChildItem -LiteralPath $roots -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() -and $_.FullName -notmatch '[\\/](\.godot|bin|obj)[\\/]' } |
            ForEach-Object { $_.FullName })
}

function Get-OtherGodotProcesses {
    <# Godot processes this script did not start (its own runs are sequential and have exited by now). #>
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Godot*' })
}

function Wait-QuietMachine([int] $timeoutSeconds) {
    <#
      Waits (at most $timeoutSeconds) until no other Godot process has run for 5 s: another Godot or Vulkan instance
      starting stalls presentation for everyone (perf audit, dod8-verdict-polluted-by-environment). Never stops them.
      Returns how many were still running when the wait ended.
    #>
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $quietSince = $null
    while ((Get-Date) -lt $deadline) {
        if (@(Get-OtherGodotProcesses).Count -eq 0) {
            if (-not $quietSince) { $quietSince = Get-Date }
            if (((Get-Date) - $quietSince).TotalSeconds -ge 5) { return 0 }
        }
        else { $quietSince = $null }
        Start-Sleep -Milliseconds 500
    }
    return @(Get-OtherGodotProcesses).Count
}

function Get-FlyPathAttribution([string] $step, [string] $suffix) {
    <#
      Splits the flight's slowest frame by whether Lifestone did anything in it (FlyPathRunner's per-frame CSV: a
      main-thread drain of 1 ms or more, a GC, an attach or a rebase), and keeps the report and the CSV next to the log.
    #>
    $logs = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs'
    $csv = Join-Path $logs 'flypath-frames.csv'
    if (-not (Test-Path -LiteralPath $csv)) { return @{ Text = 'no frame CSV'; WorkMs = [double]::NaN } }
    foreach ($name in 'flypath-frames.csv', 'flypath-report.json') {
        $source = Join-Path $logs $name
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $OutDir ('{0:00}-{1}{2}-{3}' -f $script:stepIndex, $step, $suffix, $name))
        }
    }
    $maxWork = 0.0; $maxIdle = 0.0; $previous = $null
    foreach ($row in Import-Csv -LiteralPath $csv) {
        $ms = [double]$row.frame_ms
        $work = [double]$row.drain_ms -ge 1.0
        if ($previous) {
            foreach ($column in 'attached', 'gc0', 'gc1', 'gc2', 'rebases') { if ($row.$column -ne $previous.$column) { $work = $true } }
        }
        if ($work) { $maxWork = [math]::Max($maxWork, $ms) } else { $maxIdle = [math]::Max($maxIdle, $ms) }
        $previous = $row
    }
    return @{ Text = ('max with Lifestone work {0:0.0} ms, max with none {1:0.0} ms' -f $maxWork, $maxIdle); WorkMs = $maxWork }
}

$strayBefore = Get-StrayOutputs
$suiteTimer = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "[smoke] repository $repoRoot; logs and outputs in $OutDir"
$wantedBackendSteps = @($backendSteps | Where-Object { Test-Wanted $_ })
$backendAtStart = Test-BackendReady
Write-Host "[smoke] backend: $(if ($backendAtStart.Ready) { 'ready' } else { "not ready: $($backendAtStart.Reason)" }); -Backend $Backend"

try {
    foreach ($step in $allSteps) {
        if ($step -eq 'build' -and $NoBuild) { continue }
        if (-not (Test-Wanted $step)) { continue }
        $script:stepIndex++
        $timer = [System.Diagnostics.Stopwatch]::StartNew()
        if ($step -eq 'login-throwaway' -and $Backend -ne 'require') {
            # It creates a character on the test account and purges it again: only a run that demands the backend (the lead's gate) does that.
            Add-Result $step $true 0 $timer.Elapsed.TotalSeconds "it creates and purges a character on the test account; runs only with -Backend require (this is -Backend $Backend)" -Skipped
            continue
        }
        if ($backendSteps -contains $step -and -not (Test-BackendGate $step $timer)) { continue }
        switch ($step) {
            'build' {
                $log = Join-Path $OutDir ('{0:00}-build.log' -f $script:stepIndex)
                $code = Invoke-Logged -FileName $dotnetExe -LogPath $log -TimeoutSeconds 900 `
                    -Arguments @('build', (Join-Path $repoRoot 'Lifestone.slnx'), '--disable-build-servers', '-nologo')
                $text = Get-Content -LiteralPath $log -Raw
                $warnings = if ($text -match '(\d+) Warning\(s\)') { [int]$Matches[1] } else { -1 }
                $errors = if ($text -match '(\d+) Error\(s\)') { [int]$Matches[1] } else { -1 }
                Add-Result $step ($code -eq 0 -and $warnings -eq 0 -and $errors -eq 0) $code $timer.Elapsed.TotalSeconds "warnings=$warnings errors=$errors" $log
            }
            'test' {
                $log = Join-Path $OutDir ('{0:00}-test.log' -f $script:stepIndex)
                $code = Invoke-Logged -FileName $dotnetExe -LogPath $log -TimeoutSeconds 1800 `
                    -Arguments @('test', (Join-Path $repoRoot 'Lifestone.slnx'), '--disable-build-servers', '--no-build', '-nologo', '--filter', $testFilter)
                $text = Get-Content -LiteralPath $log -Raw
                $passed = 0; $failed = 0; $skipped = 0
                foreach ($m in [regex]::Matches($text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')) {
                    $failed += [int]$m.Groups[1].Value; $passed += [int]$m.Groups[2].Value; $skipped += [int]$m.Groups[3].Value
                }
                Add-Result $step ($code -eq 0 -and $failed -eq 0 -and $passed -gt 0) $code $timer.Elapsed.TotalSeconds "passed=$passed failed=$failed skipped=$skipped filter=$testFilter" $log
            }
            'cli-help' {
                $run = Invoke-Godot $step '--help' 90
                $ok = $run.Code -eq 0 -and $run.Text -match 'Lifestone command-line options' -and $run.Text -match '--interior-engine-checks'
                Add-Result $step $ok $run.Code $timer.Elapsed.TotalSeconds 'help printed, exit 0' $run.Log (Get-ErrorCount $run.Text)
            }
            'cli-bad-argument' {
                $run = Invoke-Godot $step '--engine-checks --vsync=of' 90
                $ok = $run.Code -eq 1 -and $run.Text -match "automated run with bad arguments" -and $run.Text -match "'of' is not one of"
                Add-Result $step $ok $run.Code $timer.Elapsed.TotalSeconds 'mistyped --vsync in an automated run: exit 1 at once' $run.Log
            }
            'terrain-golden' {
                $log = Join-Path $OutDir ('{0:00}-terrain-golden.log' -f $script:stepIndex)
                $goldenDir = Join-Path $OutDir 'terrain-golden'
                $code = Invoke-Logged -FileName $pwshExe -LogPath $log -TimeoutSeconds 420 `
                    -Arguments @('-NoProfile', '-File', $goldenScript, '-NoBuild', '-TimeoutSeconds', '300', '-OutDir', $goldenDir)
                $text = Get-Content -LiteralPath $log -Raw
                $verdict = if ($text -match '\[terrain-golden\] (PASS|FAIL[^\r\n]*|error[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                Add-Result $step ($code -eq 0) $code $timer.Elapsed.TotalSeconds "$verdict (report: terrain-golden\terrain-golden-report.txt)" $log (Get-ErrorCount $text)
            }
            'engine-checks' {
                $run = Invoke-Godot $step '--engine-checks' 120
                $detail = if ($run.Text -match 'engine checks[^\r\n]*pass=(\d+) fail=(\d+)') { "pass=$($Matches[1]) fail=$($Matches[2])" } else { 'no summary line' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all engine checks hold') $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log (Get-ErrorCount $run.Text)
            }
            'interior-engine-checks' {
                $run = Invoke-Godot $step '--interior-engine-checks' 120
                $holds = ([regex]::Matches($run.Text, 'engine check holds')).Count
                $fails = ([regex]::Matches($run.Text, 'ENGINE CHECK FAILS')).Count
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'engine checks: all hold') $run.Code $timer.Elapsed.TotalSeconds "holds=$holds fails=$fails" $run.Log (Get-ErrorCount $run.Text)
            }
            'sky-checks' {
                $run = Invoke-Godot $step '--sky-checks' 150
                $pass = ([regex]::Matches($run.Text, '\[skycheck\] PASS')).Count
                $fail = ([regex]::Matches($run.Text, '\[skycheck\] FAIL')).Count
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all sky checks hold') $run.Code $timer.Elapsed.TotalSeconds "pass=$pass fail=$fail" $run.Log (Get-ErrorCount $run.Text)
            }
            'blend-checks' {
                $run = Invoke-Godot $step '--blend-checks' 150
                $pass = ([regex]::Matches($run.Text, '\[blendcheck\] PASS')).Count
                $fail = ([regex]::Matches($run.Text, '\[blendcheck\] FAIL')).Count
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all blend checks hold') $run.Code $timer.Elapsed.TotalSeconds "pass=$pass fail=$fail" $run.Log (Get-ErrorCount $run.Text)
            }
            'portal-space-checks' {
                $run = Invoke-Godot $step '--portal-space-checks' 180
                $pass = ([regex]::Matches($run.Text, '\[portalcheck\] PASS')).Count
                $fail = ([regex]::Matches($run.Text, '\[portalcheck\] FAIL')).Count
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all portal-space checks hold') $run.Code $timer.Elapsed.TotalSeconds "pass=$pass fail=$fail" $run.Log (Get-ErrorCount $run.Text)
            }
            'portal-space' {
                $dir = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $OutDir 'portal-space')) -replace '\\', '/'
                $run = Invoke-Godot $step "--portal-space-frames=$dir" 240
                # The runner fails (failures=1, exit 1) on any frame over 50 ms that is not its own capture (DoD 15), so slow_frames=0 rides on failures=0.
                $detail = if ($run.Text -match 'portal space frames done[^\r\n]*captured=(\d+) failures=(\d+) max_frame_ms=([\d.]+) slow_frames=(\d+)') { "captured=$($Matches[1]) failures=$($Matches[2]) max_frame_ms=$($Matches[3]) slow_frames=$($Matches[4]) in $dir" } else { 'no summary line' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'portal space frames done[^\r\n]*captured=4 failures=0 [^\r\n]*slow_frames=0') $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log (Get-ErrorCount $run.Text)
            }
            'retail-ui' {
                # The login screens' review frames at two window sizes, then the [uicheck] suite. Each Godot run has its own log (<nn>-retail-ui-<suffix>.log).
                # The frames runner fails (failures>0, exit 1) when the window is not the size asked for, so the size in the verdict rides on failures=0.
                $verdictPattern = 'retail ui frames done size=(?<size>\d+x\d+) scale=(?<scale>\d+) captured=(?<captured>\d+) failures=(?<failures>\d+) missing_images=(?<missing>\d+)'
                $parts = [System.Collections.Generic.List[string]]::new()
                $pass = $true; $errors = 0; $exitCode = 0; $firstLog = ''; $failedLog = ''
                foreach ($size in $frameSizes) {
                    $dir = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $OutDir "retail-ui\$size")).Replace('\', '/')
                    $run = Invoke-Godot $step "--retail-ui-frames=$dir" 240 "-$size" $size
                    $hasVerdict = $run.Text -match $verdictPattern
                    $ok = $run.Code -eq 0 -and $hasVerdict -and $Matches['size'] -eq $size -and $Matches['failures'] -eq '0' -and [int]$Matches['captured'] -gt 0
                    $parts.Add($(if ($hasVerdict) { "$($Matches['size']) scale=$($Matches['scale']) captured=$($Matches['captured']) failures=$($Matches['failures']) missing_images=$($Matches['missing'])" } else { "$size no verdict line" }))
                    $errors += Get-ErrorCount $run.Text
                    if (-not $firstLog) { $firstLog = $run.Log }
                    if (-not $ok) {
                        $pass = $false
                        if (-not $failedLog) { $failedLog = $run.Log }
                        if ($exitCode -eq 0) { $exitCode = if ($run.Code -ne 0) { $run.Code } else { 1 } }
                    }
                }
                $run = Invoke-Godot $step ('--retail-ui-checks' + $(if ($AllSizes) { ' --all-sizes' } else { '' })) 240 '-checks' $defaultSize
                $uiPass = ([regex]::Matches($run.Text, '\[uicheck\] PASS')).Count
                $uiFail = ([regex]::Matches($run.Text, '\[uicheck\] FAIL')).Count
                $parts.Add("uicheck pass=$uiPass fail=$uiFail")
                $errors += Get-ErrorCount $run.Text
                if ($run.Code -ne 0 -or $run.Text -notmatch 'all retail-ui checks hold') {
                    $pass = $false
                    if (-not $failedLog) { $failedLog = $run.Log }
                    if ($exitCode -eq 0) { $exitCode = if ($run.Code -ne 0) { $run.Code } else { 1 } }
                }
                $parts.Add("frames in $(Join-Path $OutDir 'retail-ui')")
                Add-Result $step $pass $exitCode $timer.Elapsed.TotalSeconds ($parts -join '; ') $(if ($failedLog) { $failedLog } else { $firstLog }) $errors
            }
            'offline-player-checks' {
                # The real player walks Holtburg for about two minutes of wall time (a lap, two jumps, a wall, two buildings each in two cameras).
                $run = Invoke-Godot $step '--offline-player-checks' 420
                $pass = ([regex]::Matches($run.Text, '\[walkcheck\] PASS')).Count
                $fail = ([regex]::Matches($run.Text, '\[walkcheck\] FAIL')).Count
                $max = if ($run.Text -match 'all offline-player checks hold[^\r\n]*max_frame_ms=([\d.]+)') { " max_frame_ms=$($Matches[1])" } else { '' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all offline-player checks hold') $run.Code $timer.Elapsed.TotalSeconds "pass=$pass fail=$fail$max" $run.Log (Get-ErrorCount $run.Text)
            }
            'play-checks' {
                $run = Invoke-Godot $step '--play-checks' 420 '' $defaultSize
                $pass = ([regex]::Matches($run.Text, '\[playcheck\] PASS')).Count
                $fail = ([regex]::Matches($run.Text, '\[playcheck\] FAIL')).Count
                $max = if ($run.Text -match 'all play checks hold[^\r\n]*max_frame_ms_after_arrival=([\d.]+)') { " max_frame_ms=$($Matches[1])" } else { '' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'all play checks hold') $run.Code $timer.Elapsed.TotalSeconds "pass=$pass fail=$fail$max" $run.Log (Get-ErrorCount $run.Text)
            }
            'shell-frames' {
                # The shell's review frames at two window sizes on the stand-in session (no backend). A frames run is not a timing run (the verdict says frame_time=reported).
                $verdictPattern = 'shell frames done size=(?<size>\d+x\d+) scale=(?<scale>\d+) captured=(?<captured>\d+) failures=(?<failures>\d+) frame_time=reported'
                $expectedFrames = 10 # ShellFramesPlan.FrameNames.Count (G7: 09 the automation inspector, 10 the tools panel)
                $parts = [System.Collections.Generic.List[string]]::new()
                $pass = $true; $errors = 0; $exitCode = 0; $firstLog = ''; $failedLog = ''
                foreach ($size in $shellFrameSizes) {
                    $dir = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $OutDir "shell-frames\$size")).Replace('\', '/')
                    $run = Invoke-Godot $step "--play-checks --play-frames=$dir" 420 "-$size" $size
                    $holds = $run.Text -match 'all play checks hold'
                    $hasVerdict = $run.Text -match $verdictPattern
                    $files = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot $dir) -Filter '*.png' -ErrorAction SilentlyContinue | Where-Object { $_.Length -gt 5000 }).Count
                    $ok = $run.Code -eq 0 -and $holds -and $hasVerdict -and $Matches['size'] -eq $size -and $Matches['failures'] -eq '0' -and [int]$Matches['captured'] -eq $expectedFrames -and $files -eq $expectedFrames
                    $parts.Add($(if ($hasVerdict) { "$($Matches['size']) scale=$($Matches['scale']) captured=$($Matches['captured']) failures=$($Matches['failures']) files=$files" } else { "$size no verdict line" }))
                    $errors += Get-ErrorCount $run.Text
                    if (-not $firstLog) { $firstLog = $run.Log }
                    if (-not $ok) {
                        $pass = $false
                        if (-not $failedLog) { $failedLog = $run.Log }
                        if ($exitCode -eq 0) { $exitCode = if ($run.Code -ne 0) { $run.Code } else { 1 } }
                    }
                }
                $parts.Add("frames in $(Join-Path $OutDir 'shell-frames')")
                Add-Result $step $pass $exitCode $timer.Elapsed.TotalSeconds ($parts -join '; ') $(if ($failedLog) { $failedLog } else { $firstLog }) $errors
            }
            'login-check' {
                # The test account only; the game reads its password from secrets.json itself (names only on the command line).
                $run = Invoke-Godot $step '--account=test --login-check=all' 1100 '' $defaultSize
                $verdict = if ($run.Text -match '\[logincheck\] (done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match '\[logincheck\] done ok=true ' -and $run.Text -match 'corrections=0 ' -and $run.Text -match 'logoff=ok ' -and $run.Text -match 'logoff_tunnel=held ' -and $run.Text -match 'portal_sounds=ok ') $run.Code $timer.Elapsed.TotalSeconds $verdict $run.Log (Get-ErrorCount $run.Text)
            }
            'login-throwaway' {
                # --login-check=throwaway (docs/session.md section 28): creates 'Lifestone Throwaway' on the test account through the retail screens, uses the tools panel, deletes, restores,
                # deletes, purges it from 'Lifestone Smoke' and checks the account's list is as it began. The test account only; the game reads its password from secrets.json itself.
                # 840 s = the runner's own 600 s limit + 120 s of recovery + start-up and shutdown with margin (docs/session.md section 28).
                $run = Invoke-Godot $step '--account=test --login-check=throwaway' 840 '' $defaultSize
                $verdict = if ($run.Text -match '\[logincheck\] (throwaway done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                $fields = 'throwaway=purged name_errors=2 xp=added where_m=\d+\.\d+ place=teleported deletes=2 restores=1 list=same leftover=(yes|no)'
                $framesOk = $true
                foreach ($key in 'enter_to_walk_max_ms', 'teleport_frame_max_ms') {
                    if ($run.Text -match "\[logincheck\] throwaway done [^\r\n]*$key=(\d+\.\d+)") { if ([double]$Matches[1] -gt 50.0) { $framesOk = $false } } else { $framesOk = $false }
                }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match '\[logincheck\] throwaway done ok=true ' -and $run.Text -match $fields -and $framesOk) $run.Code $timer.Elapsed.TotalSeconds $verdict $run.Log (Get-ErrorCount $run.Text)
            }
            'login' {
                # The player's own entry point (tools\play.ps1) on the test account (-Offscreen makes it the test account and refuses any other). The backend is the
                # one the gate above found ready (or started with -Backend start): play.ps1 uses a ready backend as it is and stops only what it started itself.
                $log = Join-Path $OutDir ('{0:00}-login.log' -f $script:stepIndex)
                $startedAt = [DateTime]::UtcNow
                $arguments = @('-NoProfile', '-File', $playScript, '-Offscreen', '-IsolateUserData', '-NoBuild', '-TimeoutSeconds', '420', '-Resolution', $defaultSize, '-GameArgs:--login-check=enter')
                $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath $log -TimeoutSeconds 480
                $text = Get-Content -LiteralPath $log -Raw
                $verdict = if ($text -match '\[logincheck\] (done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                $frameDir = Join-Path $repoRoot 'docs\screenshots\m2\login'
                # Only frames this run wrote count (the folder keeps the earlier run's until they are overwritten).
                $frames = @(Get-ChildItem -LiteralPath $frameDir -Filter '*.png' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTimeUtc -ge $startedAt }).Count
                Add-Result $step ($code -eq 0 -and $text -match '\[logincheck\] done ok=true ' -and $text -match 'materialised=true' -and $frames -ge 4 -and $text -match 'shell=ok ' -and $text -match 'spew_portal=yes ' -and $text -match 'logoff_tunnel=held ' -and $text -match 'portal_sounds=ok ') $code $timer.Elapsed.TotalSeconds "$verdict; frames=$frames in docs/screenshots/m2/login" $log (Get-ErrorCount $text)
            }
            'offline-player' {
                $dir = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $OutDir 'player')) -replace '\\', '/'
                $run = Invoke-Godot $step "--offline-player-frames=$dir" 240
                # The runner fails (failures>0, exit 1) on a state that does not hold, a blank or unsaved frame, a timed-out wait or a frame over 50 ms.
                $detail = if ($run.Text -match 'player frames done captured=(\d+) failures=(\d+) max_frame_ms=([\d.]+)') { "captured=$($Matches[1]) failures=$($Matches[2]) max_frame_ms=$($Matches[3]) in $dir" } else { 'no summary line' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'player frames done captured=4 failures=0 ') $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log (Get-ErrorCount $run.Text)
            }
            'spell-showcase' {
                $run = Invoke-Godot $step '--scene=spellviewer --showcase --showcase-only=0,6,13,15' 300
                $exceptions = if ($run.Text -match 'showcase done[^\r\n]*exceptions=(\d+)') { [int]$Matches[1] } else { -1 }
                $segments = ([regex]::Matches($run.Text, 'showcase segment')).Count
                Add-Result $step ($run.Code -eq 0 -and $exceptions -eq 0) $run.Code $timer.Elapsed.TotalSeconds "segments=$segments exceptions=$exceptions" $run.Log (Get-ErrorCount $run.Text)
            }
            'golden-viewpoints' {
                $dir = [IO.Path]::GetRelativePath($repoRoot, (Join-Path $OutDir 'golden')) -replace '\\', '/'
                $run = Invoke-Godot $step "--golden-viewpoints=$dir" 300
                $detail = if ($run.Text -match 'golden viewpoints done[^\r\n]*captured=(\d+) failures=(\d+)') { "captured=$($Matches[1]) failures=$($Matches[2]) in $dir" } else { 'no summary line' }
                Add-Result $step ($run.Code -eq 0 -and $run.Text -match 'golden viewpoints done[^\r\n]*failures=0') $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log (Get-ErrorCount $run.Text)
            }
            'fly-path' {
                $attempts = @()
                $attempt = 0
                foreach ($attempt in 1..2) {
                    $quiet = Wait-QuietMachine 180
                    $suffix = if ($attempt -gt 1) { '-retry' } else { '' }
                    $run = Invoke-Godot $step "--fly-path=holtburg:arwic:$FlySpeed" 300 $suffix
                    $others = @(Get-OtherGodotProcesses).Count
                    $max = if ($run.Text -match '\[flypath\] report [^\r\n]*max_ms=([\d.]+)') { $Matches[1] } else { '?' }
                    $over = if ($run.Text -match '\[flypath\] report [^\r\n]*over_50ms=(\d+)') { $Matches[1] } else { '?' }
                    $split = Get-FlyPathAttribution $step $suffix
                    $attempts += "attempt $attempt`: exit $($run.Code), max $max ms ($($split.Text)), frames over 50 ms $over; other Godot processes before/after: $quiet/$others"
                    # Retry only a flight whose frames over 50 ms all had no Lifestone work (an external stall). A frame over
                    # 50 ms with Lifestone work - or frames that cannot be attributed (no CSV: WorkMs is NaN) - fails at once.
                    if ($run.Code -ne 2 -or -not ($split.WorkMs -le 50.0)) { break }
                    if ($attempt -eq 1) { $attempts += 'retrying: every frame over 50 ms had no Lifestone work' }
                }
                $pass = $run.Code -eq 0
                $note = if ($pass -and $attempt -gt 1) { 'retry' } else { '' }
                if (-not $pass -and $JudgeLifestoneFramesOnly -and $run.Code -eq 2 -and $split.WorkMs -le 50.0) {
                    # Opt-in (shared machines): every frame over 50 ms had no Lifestone work in it.
                    $pass = $true
                    $note = 'attributed'
                    $attempts += 'PASS by -JudgeLifestoneFramesOnly: the frames over 50 ms had no Lifestone work'
                }
                Add-Result $step $pass $run.Code $timer.Elapsed.TotalSeconds ($attempts -join '; ') $run.Log (Get-ErrorCount $run.Text) $note
            }
            'stray-output' {
                $new = @(Get-StrayOutputs | Where-Object { $strayBefore -notcontains $_ })
                $detail = if ($new.Count -eq 0) { 'no new image, video, audio, .bin or .lsrec files under src/ or tests/' } else { "new: $($new -join ', ')" }
                Add-Result $step ($new.Count -eq 0) 0 $timer.Elapsed.TotalSeconds $detail
            }
        }
    }
}
finally {
    # Stop what -Backend start started, even when a step threw; the secret scan below then reads ACE's final logs.
    Stop-SmokeBackend
}

# Backend line: the state at the start, what -Backend start did, and which selected steps needed the backend.
$backendLine = "backend: -Backend $Backend; at start: $(if ($backendAtStart.Ready) { 'ready' } else { "not ready, $($backendAtStart.Reason)" })"
$backendLine += if ($wantedBackendSteps.Count) { "; steps that need it: $($wantedBackendSteps -join ', ')" } else { '; no selected step needs it' }
if ($Backend -eq 'start') { $backendLine += "; start: $(if ($script:backendStart) { $script:backendStart.Note } else { 'not needed, nothing started' })" }

try { $leaks = Measure-SecretLeaks }
catch {
    # Only the exception's type: whatever went wrong, no message may carry a password into the summary.
    $leaks = @{ Hits = -1; Files = @(); Scanned = 0; Secrets = 0; Unreadable = @(); Note = "the scan failed ($(Get-ExceptionKind $_))" }
}
$secretsFailed = $leaks.Hits -ne 0 -or $leaks.Unreadable.Count -gt 0
$secretsLine = if ($leaks.Hits -lt 0) { "secrets=? FAIL: $($leaks.Note)" } else {
    "secrets=$($leaks.Hits)$(if ($secretsFailed) { ' FAIL' })" +
    $(if ($leaks.Note) { " ($($leaks.Note))" } else { " ($($leaks.Secrets) password(s) from secrets.json; $($leaks.Scanned) file(s) scanned in OutDir, cache\godot\**\logs, cache\sessions and server\runtime\logs\ACE_Log*.txt, as UTF-8 and UTF-16LE)" }) +
    $(if ($leaks.Files.Count) { "; found in: $($leaks.Files -join ', ')" }) +
    $(if ($leaks.Unreadable.Count) { "; could not read: $($leaks.Unreadable -join ', ')" })
}

$failedSteps = @($script:results | Where-Object { $_.Outcome -eq 'FAIL' })
$skippedSteps = @($script:results | Where-Object { $_.Outcome -eq 'SKIP' })
$verdict = if ($failedSteps.Count -eq 0 -and -not $secretsFailed) { 'ALL PASS' } else {
    (@($(if ($failedSteps.Count) { "$($failedSteps.Count) FAILED" }), $(if ($secretsFailed) { 'SECRET SCAN FAILED' })) | Where-Object { $_ }) -join ', '
}
if ($skippedSteps.Count) { $verdict += ", $($skippedSteps.Count) SKIPPED" }
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Lifestone smoke run")
$lines.Add('')
$lines.Add("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $repoRoot - total $([math]::Round($suiteTimer.Elapsed.TotalMinutes, 1)) min - $verdict")
$lines.Add('')
$lines.Add('| Step | Result | Exit | Seconds | Errors logged | Detail | Log |')
$lines.Add('|---|---|---:|---:|---:|---|---|')
foreach ($r in $script:results) {
    $lines.Add("| $($r.Step) | $($r.Result) | $($r.Exit) | $($r.Seconds) | $($r.Errors) | $($r.Detail -replace '\|', '/') | $($r.Log) |")
}
$lines.Add('')
$lines.Add($backendLine)
$lines.Add('')
$lines.Add($secretsLine)
$summary = $lines -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $OutDir 'summary.md') -Value $summary -Encoding utf8
Write-Host ''
Write-Host $summary
exit $(if ($failedSteps.Count -eq 0 -and -not $secretsFailed) { 0 } else { 1 })
