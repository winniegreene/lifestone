<#
.SYNOPSIS
    One command to play: starts the local backend when it is not running, runs the game logged in to it, and stops what it
    started when the game exits (m2-plan L18; owner decisions D13 and D10).

.DESCRIPTION
    1. Refuses unless server\versions.json names 127.0.0.1 (the whole backend is loopback-only).
    2. Reads the backend's state (server\lib\LifestoneServer.psm1 Get-LsBackendState, of the MAIN checkout: a git worktree plays
       against the main checkout's backend, the only one that exists).
         - ACE up with the Lifestone mod ready (pids.json ace.modReady): used as it is; nothing is started or stopped.
         - ACE up WITHOUT a ready mod: refused (exit 1). Starting or stopping it is not play.ps1's job; run server\stop-server.ps1.
         - ACE down: server\start-server.ps1 starts what is missing (it returns once the mod reports ready).
    3. Runs the game through tools\godot.ps1 -Run with --play (and --account=NAME, a name only, and --character=NAME when given).
       The game signs in by itself, shows the retail data check, character management and character creation, and enters
       the world through portal space.
    4. When the game exits (or fails, or is stopped with Ctrl+C), stops ONLY what this run started: server\stop-server.ps1, or
       server\stop-server.ps1 -AceOnly when MariaDB was already up before. -KeepServer keeps both.

    Never a password: the game reads server\runtime\secrets.json itself, godot.ps1 echoes its arguments, and this script passes
    account and character NAMES only (tools\lib\Play.psm1 checks them and refuses anything that looks like a password option).

    Agents and tests always pass -Offscreen -IsolateUserData -TimeoutSeconds N (no window on the owner's desktop, no audio,
    user data inside the repository, a hard stop). An automated run uses the three test accounts only (M4 D31): -Offscreen makes -Account default to
    `test`, accepts `test-melee` and `test-archer`, and refuses any other; the play account is the owner's.

.PARAMETER Account
    The entry of server\runtime\secrets.json's accounts to sign in with (play, test, or one setup-server.ps1 -AddAccount made); a
    name only. Default: the file's defaultAccount (the play account); with -Offscreen, `test`.

.PARAMETER Character
    Enter the world with this character (case-insensitive) instead of stopping at the character list.

.PARAMETER KeepServer
    Leave the backend this run started running when the game exits.

.PARAMETER NoBuild
    Skip godot.ps1's 'dotnet build' (a build that was just made).

.PARAMETER Offscreen
    godot.ps1 -Offscreen: the window far off-screen and a silent audio driver. Implies the test account.

.PARAMETER IsolateUserData
    godot.ps1 -IsolateUserData: user data, logs and shader caches inside the repository (gitignored).

.PARAMETER TimeoutSeconds
    godot.ps1 -TimeoutSeconds: kill the game after this many seconds (0 = no timeout). The backend is stopped afterwards as usual.

.PARAMETER Resolution
    godot.ps1 -Resolution WxH.

.PARAMETER Audio
    godot.ps1 -Audio. Refused with -Offscreen (M4 P4 gate: an automated run is silent, M2 D8); only a run you play yourself can have sound.

.PARAMETER DatDir
    godot.ps1 -DatDir.

.PARAMETER GameArgs
    More game arguments (not --play, --account, --character or --server, which this script owns); a double-quoted run keeps its
    spaces. With 'pwsh -File' bind it with a colon: -GameArgs:'--record-session --quit-after=120'.

.PARAMETER StartTimeoutSec
    start-server.ps1's -TimeoutSec: how long ACE may take to boot (the first boot after setup is the slowest).

.PARAMETER PlanOnly
    Checks the arguments and the backend's state, prints what would run and exits 0 (1 when it would refuse). Starts nothing.

.EXAMPLE
    pwsh -File tools\play.ps1
    Starts the backend if needed, opens the game on the play account, and stops the backend afterwards if it started it.

.EXAMPLE
    pwsh -File tools\play.ps1 -Offscreen -IsolateUserData -TimeoutSeconds 240 -Character "Lifestone Smoke"
    An automated run on the test account: no window, a hard stop after 4 minutes.
#>
[CmdletBinding()]
param(
    [string] $Account = '',
    [string] $Character = '',
    [switch] $KeepServer,
    [switch] $NoBuild,
    [switch] $Offscreen,
    [switch] $IsolateUserData,
    [int] $TimeoutSeconds = 0,
    [ValidatePattern('^(\d+x\d+)?$')]
    [string] $Resolution = '',
    [switch] $Audio,
    [string] $DatDir = '',
    [string] $GameArgs = '',
    [int] $StartTimeoutSec = 600,
    [switch] $PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pwshExe = (Get-Process -Id $PID).Path
$godotScript = Join-Path $PSScriptRoot 'godot.ps1'
Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'lib\Play.psm1') -Force -DisableNameChecking

function Invoke-PlayChild {
    <# Runs a PowerShell script as a child process on this console and returns its exit code. The arguments go through ArgumentList, so a quoted value (a character name with spaces) reaches the child whole. #>
    param([string] $Script, [string[]] $Arguments = @(), [switch] $Capture)
    $psi = [System.Diagnostics.ProcessStartInfo]::new($pwshExe)
    if ($Capture) { $psi.RedirectStandardOutput = $true }
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Script) + $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false   # CreateProcess: no shell verbs, no elevation, inherits this console
    $psi.WorkingDirectory = $repoRoot
    $child = [System.Diagnostics.Process]::Start($psi)
    if ($Capture) {
        # -Offscreen (P4-R15): the output is echoed line by line and kept for the audio check.
        $script:capturedOutput = [System.Text.StringBuilder]::new()
        while ($null -ne ($line = $child.StandardOutput.ReadLine())) { Write-Host $line; [void]$script:capturedOutput.AppendLine($line) }
    }
    $child.WaitForExit()
    return $child.ExitCode
}

function Stop-Refused([string] $Message, [int] $Code = 1) {
    Write-Host "[play] REFUSED: $Message" -ForegroundColor Red
    exit $Code
}

# --- 1. The arguments: names only, the automated run's account, and nothing that carries a password. ---
$game = Get-PlayGameOptions -Account $Account -Character $Character -Offscreen:$Offscreen -GameArgs $GameArgs -Audio:$Audio
if ($game.Problems.Count -gt 0) { Stop-Refused ($game.Problems -join "`n  ") }

# --- 2. The backend: the main checkout's (a worktree has no runtime of its own). ---
$runtime = Find-LifestoneServerRuntime -RepoRoot $repoRoot
$serverRoot = Split-Path -Parent $runtime
Import-Module (Join-Path $serverRoot 'lib\LifestoneServer.psm1') -Force -DisableNameChecking
$cfg = Get-LsConfig
try { Assert-PlayLoopback $cfg.Versions } catch { Stop-Refused $_.Exception.Message }
# The game talks to the port the backend was configured with (not its compiled default): the two cannot disagree.
try { $serverEndpoint = Get-PlayServerEndpoint $cfg.Versions } catch { Stop-Refused $_.Exception.Message }
$game = Get-PlayGameOptions -Account $Account -Character $Character -Offscreen:$Offscreen -GameArgs $GameArgs -Server $serverEndpoint -Audio:$Audio
if ($game.Problems.Count -gt 0) { Stop-Refused ($game.Problems -join "`n  ") }

$state = Get-LsBackendState $cfg
$plan = Get-PlayBackendPlan $state
$godotParameters = @('-Run')
if ($NoBuild) { $godotParameters += '-NoBuild' }
if ($Offscreen) { $godotParameters += '-Offscreen' }
if ($IsolateUserData) { $godotParameters += '-IsolateUserData' }
if ($TimeoutSeconds -gt 0) { $godotParameters += @('-TimeoutSeconds', "$TimeoutSeconds") }
if ($Resolution) { $godotParameters += @('-Resolution', $Resolution) }
if ($Audio) { $godotParameters += '-Audio' }
if ($DatDir) { $godotParameters += @('-DatDir', $DatDir) }
$godotParameters += "-GameArgs:$($game.GameArgs)"

Write-Host "[play] backend: $($plan.Reason)"
Write-Host "[play] game: tools\godot.ps1 $($godotParameters -join ' ')"
if ($plan.Action -eq 'refuse') { Stop-Refused $plan.Reason }
if ($PlanOnly) {
    Write-Host '[play] -PlanOnly: nothing was started'
    exit 0
}

$startedMariaDb = $false
$startedAce = $false
$ownershipKnown = ($plan.Action -ne 'start')   # a run that starts nothing owns nothing; otherwise it is known once start-server.ps1 has returned and the state is re-read
$exitCode = 0
try {
    if ($plan.Action -eq 'start') {
        $startScript = Join-Path $serverRoot 'start-server.ps1'
        Write-Host "[play] starting the backend: $startScript"
        $startCode = Invoke-PlayChild $startScript @('-TimeoutSec', "$StartTimeoutSec")
        # What is up now and was not before is ours to stop, also when the start failed half-way.
        $after = Get-LsBackendState $cfg
        $started = Get-PlayStartedByUs -Before $state -After $after
        $startedMariaDb = $started.MariaDb
        $startedAce = $started.Ace
        $ownershipKnown = $true
        if ($startCode -ne 0) { throw "server\start-server.ps1 failed (exit code $startCode); see its output above." }
        if (-not $after.ModReady) { throw 'the backend started, but pids.json does not say the Lifestone mod is ready (ace.modReady); not playing against it.' }
    }

    Write-Host '[play] starting the game'
    $exitCode = Invoke-PlayChild $godotScript $godotParameters -Capture:$Offscreen
    Write-Host "[play] the game exited with code $exitCode"
    if ($Offscreen) {
        # M2 D8 / P4-R15: an automated run is muted, the capture runs (--play-look-shots, --play-panel-shots, --play-screenshot) included; none or muted=false fails the run (exit 3) and says why.
        $audioVerdict = Get-PlayAudioVerdict $script:capturedOutput.ToString()
        if (-not $audioVerdict.Ok) { Write-Host "[play] AUDIO CHECK FAILED: $($audioVerdict.Reason)" -ForegroundColor Red; if ($exitCode -eq 0) { $exitCode = 3 } }
        else { Write-Host '[play] audio check: muted' }
    }
}
catch {
    Write-Host "[play] $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}
finally {
    if (-not $ownershipKnown) {
        # Ctrl+C or a throw while start-server.ps1 was running (it may take StartTimeoutSec): mariadbd and ACE run detached and may be up now, so they are ours to stop.
        try {
            $started = Get-PlayStartedByUs -Before $state -After (Get-LsBackendState $cfg)
            $startedMariaDb = $started.MariaDb
            $startedAce = $started.Ace
        }
        catch {
            Write-Host "[play] could not read the backend's state to find what this run started ($($_.Exception.Message)); check server\stop-server.ps1 and the ports" -ForegroundColor Red
            $exitCode = 2
        }
    }

    $stop = Get-PlayStopPlan -StartedMariaDb $startedMariaDb -StartedAce $startedAce -KeepServer:$KeepServer
    Write-Host "[play] $($stop.Note)"
    $stopScript = Join-Path $serverRoot 'stop-server.ps1'
    switch ($stop.Action) {
        'all' { if ((Invoke-PlayChild $stopScript) -ne 0) { Write-Host '[play] stop-server.ps1 did not finish cleanly; check server\stop-server.ps1 and the ports' -ForegroundColor Red; $exitCode = 2 } }
        'aceonly' { if ((Invoke-PlayChild $stopScript @('-AceOnly')) -ne 0) { Write-Host '[play] stop-server.ps1 -AceOnly did not finish cleanly; check the ports' -ForegroundColor Red; $exitCode = 2 } }
        default { }
    }
}
exit $exitCode
