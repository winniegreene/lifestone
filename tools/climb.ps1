<#
.SYNOPSIS
    The lead's driver for the level ladder's climb (M6 item 0 F5, review 0-R3): the climber's creation, the climber journey and the level up job, in order, with one summed verdict.

.DESCRIPTION
    The climb is three processes, and each judges only its own mod verbs. This script runs them in order against the lead's running backend and sums them:
      step 1  headless --scenario=climber-create --fresh-character (account test): Winnie is deleted and re-created from the climber's template (the new copy purges the old, M4 D44/D45).
      step 2  headless --scenario=journey --journey=climber: the Academy and the Pathwarden by play, cheat-free.
      step 3  the play host's level up job for Winnie (tools\play.ps1 -Offscreen -IsolateUserData -KeepServer ... --autopilot-job=level-up).
    Step 3 is refused unless step 1 read granted=0 and step 2 read cheat_free=yes granted=0 place=0 recovery_place=0 in THIS run (a count a log did not give is a refusal, not a zero).
    With -OneWindow (M6 onscreen start) the three run as ONE play process in one window (--login-check=climber-start): the creation through the retail screens, the journey and the level up job, the same rule enforced in the game
    and read again here from the one log.
    It ends with ONE line, report-only (D27):
      [climb] verdict level_start= level_end= rung= kills= deaths= granted= place= recovery_place= minutes= (then casters= and health_by_level=<level>:<health>,... when the log gives them)
    The backend is the lead's: this script never starts or stops it, never runs server\*.ps1, and refuses unless the backend is up with the Lifestone mod ready. It holds the display on during step 3
    the way the gate does (tools\lib\GateHost.psm1: one child process holding SetThreadExecutionState, ended in the finally; no setting is changed). Account test and the character Winnie only (M6 D7).

.PARAMETER Minutes
    The level up job's duration stop rule in minutes (--autopilot-minutes). Default 60.

.PARAMETER TargetLevel
    The level up job's level stop rule (--autopilot-level). Default 10.

.PARAMETER SkipCreate
    Resume from the existing Winnie: steps 1 and 2 are skipped, with a WARN that their counts are not in the sum.

.PARAMETER OutDir
    Where the step logs and the verdict go. Default cache\m6\climb\<UTC time>.

.PARAMETER NoBuild
    Skip the one `dotnet build` before step 1 (a build that was just made).

.PARAMETER HeadlessTimeoutSeconds
    Hard stop of each headless step (default 2400).

.PARAMETER NoDisplayHold
    Skip the display hold of step 3.

.PARAMETER NoLoot
    Leave the level up job's looting off (--fight-loot is on by default here: M6 item 2, the gear floor and the equip pass run on the loot trips, and a climber that never loots never buys its second caster or wears a better piece).

.PARAMETER NoQuests
    The sprint (M6 item 14): passes --autopilot-ladder-quests=off to the level up job: no quest errand from home, no trophy turn-in and no trophy want. Steps 1 and 2 are unchanged (the Academy and the Pathwarden chest are her first gear).

.PARAMETER Quests
    A quest selection (M6 14 guard task, off by default): passes --autopilot-ladder-quests=<list> to the level up job, which then runs only the quests the list names, by the quest catalog's keys or the group 'tusks' (every tusk
    turn-in), the rest off; a kill task it names is run (for example 'tusks,tusker-guard-kill-task': the tusk turn-ins and the Tusker Guard Kill Task). The game checks the keys and refuses an unknown one by name. Not with -NoQuests.

.PARAMETER NoBuffBot
    Passes --autopilot-buff-bot=off to the creation and to the level up job: the character is created and trained in the order of before item 14 (Creature Enchantment first) and buffs herself alone. Default: the bot is used, and its
    skill order (Life Magic, Item Enchantment, Mana Conversion, Creature Enchantment) is the creation's and the job's.

.PARAMETER TrialGround
    The trial switch (M6 item 14, off by default): passes --autopilot-trial-ground=<key> and --autopilot-trial-minutes to the level up job, which hunts that later rung's ground (for example tusker-quarters at level 49)
    in place of the ladder's own rung for -TrialMinutes of fight time from her first kill there, then returns to the ladder's rung by a normal rung change. The game reads the trial's rules (a later rung's ground or alternate,
    the entry portal's level range, the gate) and refuses it by name ('ladder: trial: refused: ...'), and the ladder goes on as usual. Not with -DenRun or -DenRunRehearse. Use it with -SkipCreate.

.PARAMETER TrialMinutes
    The trial's minutes of fight time (1 to 1440, default 30). Read only with -TrialGround.

.PARAMETER StartQuest
    A quest started at once (M6 14, the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once); off by default): passes --autopilot-start-quest=<key> to the level up job, which runs that quest's
    errand from home at the run's start (after its first audit, trip and buff pass), then goes on with the ladder; when the errand ends, done, failed or cut, the ladder resumes and the errand's retry rules apply. The game checks the
    key against the quest catalog and the quest's level gate and refuses by name. Not with -DenRun or -DenRunRehearse. Use it with -SkipCreate (for example -StartQuest atlan-wand).

.PARAMETER HoldGrounds
    Grounds held for the session (M6 14; default none): passes --autopilot-hold-grounds=<key,key> to the level up job, whose every way out then holds each by name (a crossing given up) and hunts the rung's other ground or the
    rung before it. For a ground found unsafe live until its cause is fixed (the Tusker Armory after the death of 2026-11-14).

.PARAMETER RungHoldsReport
    Passes --autopilot-rung-holds=report to the level up job (M6 holding at 60, the owner 2026-11-20: the only thing that should stop her is her level; default hold): the rungs' gates, the grounds' approach checks and the vitae
    burn are read and logged but hold nothing, and a crossing is never given up on its projection; the ladder's band and the portals' level limits decide. -HoldGrounds still holds the grounds it names.

.PARAMETER NoVilla
    Passes --autopilot-villa=off to the level up job (M6 villa, default on): no writ stones, lockpick, purchase, storage or rent steps at the town trips. For a character
    about to be rebuilt (the owner, 2026-11-16: the rebuild tosses everything), whose villa steps would only spend her hunting time.

.PARAMETER DenRun
    The den run's order (M6 item 14, off by default): passes --autopilot-den-run to the level up job, which then does one thing and stops: the trip home with its sale, the buff bot (the word all), her own pass, one run to the Black
    Spawn Den's entrance that never stops to fight, a minute inside, the tie at the entrance and home. It stops den-run:tied (exit code 0) or by the failure's name (exit code 1), and refuses to start under level 40. Use it with
    -SkipCreate on the character that reached level 40. The level stop rule (-TargetLevel) is not read while the order stands.

.PARAMETER DenRunRehearse
    The den run's rehearsal (M6 item 14, off by default): passes --autopilot-den-run-rehearse to the level up job: the den run's order with its road cut short: the hops to Danby's Outpost, a run-only run toward the
    Desert March Portal that ends 25 m short of it, Lifestone Recall home, and the stop den-run:rehearsed (exit code 1: no tie was meant) with each leg's metres, seconds, attackers and drinks in the log.

.PARAMETER NoOrders
    With -SteerRun (2026-11-17): the run takes no command orders. By default a watched run passes --autopilot-steer-orders, so tools\order.ps1 -Run <run> <line> (trip, rebuff, quest <key>, ladder, home, pause,
    resume, status, help; docs\commands.md) reaches her by the same runner as the chat's /trip, /rebuff, /quest and /ladder. The desk's controls (start, stop_here, ...) stay refused (observe-only) either way.

.PARAMETER Record
    Records the game window to video during step 3 (the hero cut; off by default), the way tools\soak.ps1 -Record does, through tools\WindowCapture.psm1: ffmpeg's gfxcapture source on the game window itself, never a screen
    region (a window on top is not recorded). Needs ffmpeg 8 or later on PATH; without it, or without gfxcapture, the script refuses before anything starts. Adds --video-sync-marker and the hero cut's screen to step 3's game arguments: --clean-hud (no diagnostic text), --video-chat (the main chat 0.37 of the window tall, 400 at 1080, and as wide as it can be short of her), --video-timer=<TargetLevel> (the clock since her creation, under the indicators, stopping at the target level) and --video-cantrip-look (each ideal great cantrip she loots looked at in the examine window with the paper doll and packs up, 3 s). The window is
    this run's Godot window (its command line holds --video-sync-marker), found after step 3 starts; if none appears within 180 s the run goes on unrecorded. Files: rec-NNN.ts (MPEG-TS: a crash loses nothing already
    written), segments.csv, record.txt and ffmpeg.log in -RecordDir. Steps 1 and 2 are headless and are not recorded. -PlanOnly prints the recording plan (no window exists then, so the window handle is a placeholder).

.PARAMETER RecordDir
    Where the recording's files go (a launch argument only; nothing is written to a tracked file). Default <OutDir>\record. Read only with -Record.

.PARAMETER RecordSegmentSeconds
    The length of one recording file in seconds (default 3600, soak.ps1's). Read only with -Record.

.PARAMETER OneWindow
    M6 onscreen start (owner ruling 2026-11-19: the sprint's whole run in one game window, one process, recorded from its first frame): the three steps run as ONE tools\play.ps1 process with --login-check=climber-start. In the window the live
    climber is deleted on the character screen and the new one made through character creation's pages to the same template the headless creation sends (checked, the old copy purged: the same '[climber] verdict' line); the start-up
    journey (the Academy and the Pathwarden quest) runs in the same session ('journey done', 'journey climber' lines); the game's own gate ('climber start: gate level_up=allowed|refused', the same rule as step 3's) then lets the level up job
    start in the same window with the level up arguments this script builds (every switch above applies). The verdict and gate lines below are read from the one log; the script applies its own rule to them too and reports a
    disagreement with the game's as a finding. With -Record the window is recorded from the character screen on: the game holds at the character list until the recording has begun (--climber-start-wait-file). The one process's hard
    stop is the level up job's plus -HeadlessTimeoutSeconds. Not with -SkipCreate. Off by default: the three-step path is unchanged.

.PARAMETER CreationPace
    With -OneWindow: --video-pace, the seconds each character-creation page and each step of the old climber's delete is held (and a quarter of it after each attribute and skill change), so a viewer can follow the delete and the creation (0 to 10; default 1.8, the M2 hero video's pace,
    docs/session.md). Read only with -OneWindow.

.PARAMETER PlanOnly
    Prints the three command lines (with -OneWindow, the one) and the rules and exits 0. Starts nothing, reads no backend state.

.EXAMPLE
    pwsh -File tools\climb.ps1 -PlanOnly
.EXAMPLE
    pwsh -File tools\climb.ps1 -Minutes 120 -TargetLevel 10
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 10080)]
    [int] $Minutes = 60,
    [ValidateRange(1, 275)]
    [int] $TargetLevel = 10,
    [switch] $SkipCreate,
    [string] $OutDir = '',
    [switch] $NoBuild,
    [int] $HeadlessTimeoutSeconds = 2400,
    [switch] $NoDisplayHold,
    [switch] $NoLoot,
    [switch] $NoQuests,
    [string] $Quests = '',
    [switch] $NoBuffBot,
    [string] $TrialGround = '',
    [ValidateRange(1, 1440)]
    [int] $TrialMinutes = 30,
    [string] $HoldGrounds = '',
    [string] $StartQuest = '',
    [switch] $NoVilla,
    [switch] $RungHoldsReport,
    [switch] $DenRun,
    [switch] $DenRunRehearse,
    [switch] $PlanOnly,
    [switch] $Record,
    [string] $RecordDir = '',
    [ValidateRange(10, 86400)]
    [int] $RecordSegmentSeconds = 3600,
    [ValidateSet('Winnie', 'Qwinnie')]
    [string] $Climber = 'Winnie',
    [string] $SteerRun = '',
    [switch] $NoOrders,
    [switch] $OneWindow,
    [ValidateRange(0.0, 10.0)]
    [double] $CreationPace = 1.8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pwshExe = (Get-Process -Id $PID).Path
Import-Module (Join-Path $PSScriptRoot 'lib\Climb.psm1') -Force -DisableNameChecking
$playTimeout = $Minutes * 60 + 900   # the stop rule fires at a safe point; the rest is the walk home and the log off
if ($Quests -and $NoQuests) { Write-Host '[climb] REFUSED: -Quests is a selection and -NoQuests the sprint: give one of them'; exit 1 }
if (-not $Record -and ($RecordDir -or $PSBoundParameters.ContainsKey('RecordSegmentSeconds'))) { Write-Host '[climb] REFUSED: -RecordDir and -RecordSegmentSeconds are read only with -Record'; exit 1 }
if ($OneWindow -and $SkipCreate) { Write-Host '[climb] REFUSED: -OneWindow is the whole run from the climber''s re-creation; -SkipCreate resumes an existing climber: give one of them'; exit 1 }
if (-not $OneWindow -and $PSBoundParameters.ContainsKey('CreationPace')) { Write-Host '[climb] REFUSED: -CreationPace is read only with -OneWindow'; exit 1 }
$oneWindowTimeout = $playTimeout + $HeadlessTimeoutSeconds   # -OneWindow: the creation and the start-up journey run before the level up job in the same process
$commands = Get-ClimbCommands -Minutes $Minutes -TargetLevel $TargetLevel -PlayTimeoutSeconds $playTimeout -NoLoot:$NoLoot -NoQuests:$NoQuests -Quests $Quests -NoBuffBot:$NoBuffBot -Climber $Climber -SteerRun $SteerRun -NoOrders:$NoOrders -DenRun:$DenRun -DenRunRehearse:$DenRunRehearse

function ConvertTo-ClimbArgumentString([string[]] $Arguments) {
    # One command-line string: an argument with a space is double-quoted (none holds a quote).
    return ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
}

# M6 item 14 (the owner, 2026-11-13): the trial switch joins the level up job's game arguments; its rules are the game's, which refuses a trial by name.
if ($TrialGround) {
    if ($DenRun -or $DenRunRehearse) { Write-Host '[climb] REFUSED: -TrialGround is the level up job''s own loop; the den run is a task of its own (-DenRun, -DenRunRehearse): give one of them'; exit 1 }
    $commands = Add-ClimbTrial -Commands $commands -TrialGround $TrialGround -TrialMinutes $TrialMinutes
}
elseif ($PSBoundParameters.ContainsKey('TrialMinutes')) { Write-Host '[climb] REFUSED: -TrialMinutes is read only with -TrialGround'; exit 1 }
if ($StartQuest) {
    if ($DenRun -or $DenRunRehearse) { Write-Host '[climb] REFUSED: -StartQuest is the level up job''s own loop; the den run is a task of its own'; exit 1 }
    $commands = Add-ClimbStartQuest -Commands $commands -StartQuest $StartQuest
    Write-Host "[climb] start quest: $StartQuest (-StartQuest: the level up job runs its errand from home at the run's start, then the ladder; the game refuses it by name if a rule fails)"
}
if ($HoldGrounds) {
    if ($DenRun -or $DenRunRehearse) { Write-Host '[climb] REFUSED: -HoldGrounds is the level up job''s own loop; the den run is a task of its own'; exit 1 }
    $commands = Add-ClimbHoldGrounds -Commands $commands -HoldGrounds $HoldGrounds
    Write-Host "[climb] hold: grounds=$HoldGrounds (-HoldGrounds: every way out holds them by name for the session)"
}
if ($RungHoldsReport) {
    if ($DenRun -or $DenRunRehearse) { Write-Host '[climb] REFUSED: -RungHoldsReport is the level up job''s own loop; the den run is a task of its own'; exit 1 }
    $levelUp = @($commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { Write-Host '[climb] REFUSED: the level up command has no -GameArgs: to add --autopilot-rung-holds=report to'; exit 1 }
    $levelUp[$at] += ' --autopilot-rung-holds=report'
    $commands.LevelUp = $levelUp
    Write-Host '[climb] rung holds: report only (-RungHoldsReport: gates, approach checks and the vitae burn are logged, never a hold; her level decides)'
}
if ($NoVilla) {
    if ($DenRun -or $DenRunRehearse) { Write-Host '[climb] REFUSED: -NoVilla is the level up job''s own loop; the den run is a task of its own'; exit 1 }
    $levelUp = @($commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { Write-Host '[climb] REFUSED: the level up command has no -GameArgs: to add --autopilot-villa=off to'; exit 1 }
    $levelUp[$at] += ' --autopilot-villa=off'
    $commands.LevelUp = $levelUp
    Write-Host '[climb] villa: off (-NoVilla: no villa steps at the town trips)'
}

if (-not $OutDir) { $OutDir = Join-Path $repoRoot ('cache\m6\climb\' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')) }
if ($Record) {
    # The recording (-Record): step 3 only, where the window exists. The marker is one white frame and a log line, for lining the take up with the log.
    $recordFolder = if ($RecordDir) { [IO.Path]::GetFullPath($RecordDir) } else { Join-Path $OutDir 'record' }
    Import-Module (Join-Path $PSScriptRoot 'WindowCapture.psm1') -Force
    $recordLimitSeconds = $(if ($OneWindow) { $oneWindowTimeout } else { $playTimeout }) + 600
    $levelUp = @($commands.LevelUp)
    $at = -1
    for ($i = 0; $i -lt $levelUp.Count; $i++) { if ($levelUp[$i] -like '-GameArgs:*') { $at = $i } }
    if ($at -lt 0) { Write-Host '[climb] REFUSED: the level up command has no -GameArgs: to add --video-sync-marker to'; exit 1 }
    $levelUp[$at] += ' --video-sync-marker --clean-hud --video-chat --video-timer=' + $TargetLevel + ' --video-cantrip-look' # the hero cut's screen: no diagnostic text, the taller chat, the clock to the target level, the cantrip looks
    $commands.LevelUp = $levelUp
}

$goFile = ''
$recordFirstBytes = 65536 # the take has frames once its first file passes this size (a 1080p keyframe at the recorder's 1800 kbit/s is larger; Lifestone's own threshold)
$recordLeadInSeconds = 3 # the character list on screen before the delete begins (Lifestone's own)
if ($OneWindow) {
    # M6 onscreen start: the one process, made from the level up command as finished above. With -Record the game holds at the character list until this script writes the go file, right after the recording begins.
    if ($Record) {
        $goFull = Join-Path $OutDir 'record-go'
        $goFile = if ($goFull.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $goFull.Substring($repoRoot.Length + 1).Replace('\', '/') } else { $goFull }
    }
    try { $commands.OneWindow = ConvertTo-ClimbOneWindowCommand -LevelUp @($commands.LevelUp) -TimeoutSeconds $oneWindowTimeout -Pace $CreationPace -WaitFile $goFile }
    catch { Write-Host "[climb] REFUSED: $($_.Exception.Message)"; exit 1 }
}

if ($PlanOnly) {
    Write-Host "[climb] plan: character $Climber, minutes=$Minutes target_level=$TargetLevel loot=$(if ($NoLoot) { 'off (-NoLoot)' } else { 'on' }) quests=$(if ($NoQuests) { 'off (-NoQuests, the sprint)' } elseif ($Quests) { "$Quests (-Quests, the selection)" } else { 'on' }) buff_bot=$(if ($NoBuffBot) { 'off (-NoBuffBot)' } else { 'on' })$(if ($DenRun) { ' den_run=on (-DenRun: the order; the level rule is not read)' })$(if ($SkipCreate) { ', steps 1-2 skipped (-SkipCreate)' })"
    if (-not $NoBuild) { Write-Host '[climb] build: dotnet build Lifestone.slnx --disable-build-servers -nologo' }
    if ($OneWindow) {
        Write-Host "[climb] one window: pwsh $(ConvertTo-ClimbArgumentString $commands.OneWindow)"
        Write-Host "[climb] one window: the creation through the retail screens (pace $($CreationPace.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture)) s), the start-up journey, the game's gate, then the level up job, in one process (hard stop $oneWindowTimeout s)"
    }
    else {
        Write-Host "[climb] step 1 create:  dotnet $(ConvertTo-ClimbArgumentString $commands.Create)$(if ($SkipCreate) { '   (skipped)' })"
        Write-Host "[climb] step 2 journey: dotnet $(ConvertTo-ClimbArgumentString $commands.Journey)$(if ($SkipCreate) { '   (skipped)' })"
        Write-Host "[climb] step 3 level-up: pwsh $(ConvertTo-ClimbArgumentString $commands.LevelUp)"
    }
    if ($TrialGround) { Write-Host "[climb] trial: ground=$TrialGround minutes=$TrialMinutes (-TrialGround: the level up job hunts it in place of the ladder's rung for $TrialMinutes minutes of fight time from her first kill there, then returns; the game refuses it by name if a rule fails)" }
    if ($Record) {
        $planArgs = Get-WindowRecordingArguments -Hwnd '<hwnd>' -LimitSeconds $recordLimitSeconds -SegmentSeconds $RecordSegmentSeconds -Folder $recordFolder
        if ($OneWindow) { Write-Host "[climb] record: on (-Record), the one window from the character screen on: the game window itself by ffmpeg gfxcapture (tools\WindowCapture.psm1); dir=$recordFolder segment_s=$RecordSegmentSeconds; --video-sync-marker --clean-hud --video-chat --video-timer=$TargetLevel --video-cantrip-look in the game arguments; the game holds at the character list until the go file $goFile is written, right after the recording begins (<hwnd> is a placeholder)" }
        else { Write-Host "[climb] record: on (-Record), step 3 only: the game window itself by ffmpeg gfxcapture (tools\WindowCapture.psm1); dir=$recordFolder segment_s=$RecordSegmentSeconds; --video-sync-marker --clean-hud --video-chat --video-timer=$TargetLevel --video-cantrip-look in the game arguments; the window is found after step 3 starts (<hwnd> is a placeholder)" }
        Write-Host "[climb] record: ffmpeg $(ConvertTo-ClimbArgumentString $planArgs)"
    }
    else { Write-Host '[climb] record: off (no -Record)' }
    if ($OneWindow) { Write-Host '[climb] rule: the level up job starts in the window only when the game''s gate reads the creation''s template=ok granted=0 and the journey''s ok=true cheat_free=yes granted=0 place=0 recovery_place=0 (climber start: gate level_up=allowed); this script reads the same lines from the one log and reports a disagreement as a finding; the backend is the lead''s (refused unless up with the mod ready); report-only (D27)' }
    else { Write-Host '[climb] rule: step 3 starts only when step 1 read granted=0 and step 2 read cheat_free=yes granted=0 place=0 recovery_place=0 in this run; the backend is the lead''s (refused unless up with the mod ready); report-only (D27)' }
    Write-Host '[climb] -PlanOnly: nothing was started'
    exit 0
}

# --- the toolchain, as the gate sets it ---
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'lib\GateHost.psm1') -Force -DisableNameChecking
$env:DOTNET_ROOT = Find-LifestoneDotnetRoot -DotnetRoot ''
$env:PATH = "$($env:DOTNET_ROOT);$env:PATH"
$env:LIFESTONE_DOTNET_ROOT = $env:DOTNET_ROOT
$dotnetExe = Join-Path $env:DOTNET_ROOT 'dotnet.exe'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$climbLog = Join-Path $OutDir 'climb.log'
function Write-Climb([string] $Line) { Write-Host $Line; Add-Content -LiteralPath $climbLog -Value $Line -Encoding utf8 }

function Stop-Climb([string] $Message, [int] $Code = 1) {
    Write-Climb "[climb] REFUSED: $Message"
    exit $Code
}

if ($Record) {
    # Fail loudly, before anything starts, when the recording cannot work.
    $ffmpegCheck = Test-WindowCaptureFfmpeg
    if ($ffmpegCheck.Problems.Count -gt 0) { Stop-Climb ($ffmpegCheck.Problems -join '; ') }
    Write-Climb "[climb] record: on, $(if ($OneWindow) { 'the one window from the character screen on' } else { 'step 3 only' }): ffmpeg gfxcapture on the game window, $RecordSegmentSeconds s files in $recordFolder"
}

# --- the backend is the lead's: read its state (read-only), never start or stop it ---
$runtime = Find-LifestoneServerRuntime -RepoRoot $repoRoot
Import-Module (Join-Path (Split-Path -Parent $runtime) 'lib\LifestoneServer.psm1') -Force -DisableNameChecking
$state = Get-LsBackendState (Get-LsConfig)
if (-not ([bool]$state.Ace -and [bool]$state.ModReady)) { Stop-Climb 'the backend is not up with the Lifestone mod ready; the lead starts it (this script never starts or stops it)' }
Write-Climb "[climb] start out=$OutDir minutes=$Minutes target_level=$TargetLevel skip_create=$([bool]$SkipCreate) one_window=$([bool]$OneWindow) quests=$(if ($NoQuests) { 'off' } elseif ($Quests) { $Quests } else { 'on' }) buff_bot=$(if ($NoBuffBot) { 'off' } else { 'on' })"
if ($SteerRun) {
    # The clean way to end a watched run early: the game looks for this file every 5 s. One left from an earlier run would end this one at once.
    $stopFile = Join-Path $repoRoot "cache\avatar\$SteerRun\stop"
    if (Test-Path -LiteralPath $stopFile) { Stop-Climb "a stop file is already there and would end the run at once: $stopFile" }
    Write-Climb "[climb] stop file: $stopFile"
}

# Which code this is, so a fault of the merge can be told from a fault of the climb.
Write-Climb "[climb] build commit=$((& git -C $repoRoot rev-parse HEAD).Trim()) parents=$((& git -C $repoRoot log -1 --format=%P).Trim()) dirty_files=$(@(& git -C $repoRoot status --porcelain).Count)"
foreach ($branch in @('m6/8', 'm6/avatar')) {
    & git -C $repoRoot merge-base --is-ancestor $branch HEAD
    $inBuild = if ($LASTEXITCODE -eq 0) { 'yes' } else { 'no' }
    Write-Climb "[climb] source $branch=$((& git -C $repoRoot rev-parse $branch).Trim()) in_build=$inBuild"
}

function Invoke-ClimbProcess {
    <#
      Runs one child with its output in $LogFile (stderr in $LogFile.err, appended after); kills its tree at the timeout. Returns @{ ExitCode; TimedOut; Seconds }.
      -GoFile (-OneWindow -Record): written once the recording has begun, or once no window was found, so the game's hold at the character list ends either way.
    #>
    param([string] $Exe, [string[]] $Arguments, [string] $LogFile, [int] $TimeoutSeconds, [switch] $Record, [string] $GoFile = '')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Exe -ArgumentList (ConvertTo-ClimbArgumentString $Arguments) -WorkingDirectory $repoRoot -NoNewWindow -PassThru -RedirectStandardOutput $LogFile -RedirectStandardError "$LogFile.err"
    $timedOut = $false
    if ($Record) {
        # This run's game window (its command line holds the sync marker), stable for 5 s; none within 180 s and the run goes on unrecorded.
        $window = Find-LifestoneGameWindow -CommandLineLike '--video-sync-marker' -StableSeconds 5 -TimeoutSeconds 180
        if ($window) { $script:recording = Start-WindowRecording -FfmpegPath $ffmpegCheck.Path -Window $window -LimitSeconds $recordLimitSeconds -SegmentSeconds $RecordSegmentSeconds -Folder $recordFolder -Label 'climb' }
        else { Write-Climb '[climb] record: no game window within 180 s; the run goes on unrecorded' }
        if ($GoFile -and $window) {
            # The live try of 2026-11-19 (20:17 UTC): the go file went with ffmpeg's start, and the take's first frame was already the profession page (the character list, the delete and the heritage page unrecorded).
            # The go waits for frames in the first file (its size past one keyframe's worth), then a lead-in holds the character list on screen.
            $first = Join-Path $recordFolder 'rec-000.ts'
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            while ([DateTime]::UtcNow -lt $deadline -and -not ((Test-Path -LiteralPath $first) -and (Get-Item -LiteralPath $first).Length -ge $recordFirstBytes)) { Start-Sleep -Milliseconds 250 }
            $bytes = if (Test-Path -LiteralPath $first) { (Get-Item -LiteralPath $first).Length } else { 0 }
            Write-Climb "[climb] record: frames in the take: $(if ($bytes -ge $recordFirstBytes) { 'yes' } else { 'not seen within 30 s' }) ($bytes bytes in rec-000.ts); the character list holds $recordLeadInSeconds s more on screen"
            Start-Sleep -Seconds $recordLeadInSeconds
        }

        if ($GoFile) {
            $goPath = if ([IO.Path]::IsPathRooted($GoFile)) { $GoFile } else { Join-Path $repoRoot $GoFile }
            Set-Content -LiteralPath $goPath -Value ((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')) -Encoding utf8
            Write-Climb "[climb] record: the go file is written ($(if ($window) { 'the recording has begun' } else { 'no recording' })): the game's creation begins"
        }
    }
    while (-not $p.WaitForExit(30000)) {
        Write-Host "[climb]   ... $([int]$clock.Elapsed.TotalSeconds) s"
        if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $timedOut = $true; try { $p.Kill($true) } catch { }; break }
    }
    $p.WaitForExit()
    if (Test-Path -LiteralPath "$LogFile.err") { Add-Content -LiteralPath $LogFile -Value (Get-Content -LiteralPath "$LogFile.err" -Raw) -Encoding utf8; Remove-Item -LiteralPath "$LogFile.err" -Force }
    return @{ ExitCode = $(if ($timedOut) { -1 } else { $p.ExitCode }); TimedOut = $timedOut; Seconds = $clock.Elapsed.TotalSeconds }
}

function Read-ClimbLog {
    <#
      A step's log, and, when a line the climb reads is not in it, those lines from the isolated user data's own log (--log-copies keeps Info lines off the console there: the lines read are only the climb's).
    #>
    param([string] $LogFile, [datetime] $Since, [string[]] $Needed, [string] $Patterns)
    $text = Get-Content -LiteralPath $LogFile -Raw
    if (@($Needed | Where-Object { $text -notmatch [regex]::Escape($_) }).Count -gt 0) {
        $logs = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs'
        if (Test-Path -LiteralPath $logs) {
            $files = @(Get-ChildItem -LiteralPath $logs -File -Filter 'lifestone*.log' | Where-Object { $_.LastWriteTime -ge $Since.AddSeconds(-5) } | Sort-Object LastWriteTime)
            if ($files.Count -gt 0) { $text += "`n" + ((Select-String -LiteralPath $files.FullName -Pattern $Patterns | ForEach-Object { $_.Line }) -join "`n") }
        }
    }
    return $text
}

$total = [Diagnostics.Stopwatch]::StartNew()
$create = $null; $journey = $null; $levelUp = $null; $failed = $false
$displayHold = $null
$recording = $null
try {
    if (-not $NoBuild) {
        Write-Climb '[climb] build: dotnet build Lifestone.slnx --disable-build-servers -nologo'
        $b = Invoke-ClimbProcess $dotnetExe @('build', (Join-Path $repoRoot 'Lifestone.slnx'), '--disable-build-servers', '-nologo') (Join-Path $OutDir 'build.log') 1200
        if ($b.ExitCode -ne 0) { throw "the build failed (exit code $($b.ExitCode)); see $(Join-Path $OutDir 'build.log')" }
    }

    if ($OneWindow) {
        # M6 onscreen start: the three steps in one process and one window; the verdict lines are read from its one log.
        Write-Climb "[climb] one window: pwsh $(ConvertTo-ClimbArgumentString $commands.OneWindow)"
        if ($TrialGround) { Write-Climb "[climb] trial: ground=$TrialGround minutes=$TrialMinutes (the log's 'ladder: trial:' lines say whether it started, every return and its end)" }
        if (-not $NoDisplayHold) {
            $before = Get-GateDisplayState
            Write-Climb "[climb] display preflight: $($before.Detail)"
            if ($before.State -ne 'Enabled') { [void](Invoke-GateDisplayWake) }
            $displayHold = Start-GateDisplayHold -Folder $OutDir
            Write-Climb "[climb] display hold: $(if ($displayHold) { 'on' } else { 'did not start' })"
        }
        $stepStart = Get-Date
        $oneLog = Join-Path $OutDir 'one-window.log'
        $r = Invoke-ClimbProcess $pwshExe $commands.OneWindow $oneLog ($oneWindowTimeout + 120) -Record:$Record -GoFile $goFile
        $text = Read-ClimbLog -LogFile $oneLog -Since $stepStart -Needed @('climber start: gate level_up=', 'autopilot done ok=') -Patterns '\[climber\] verdict|climber start: |journey done ok=|journey climber cheat_free=|\[ladder\] at=|autopilot done ok=|autopilot cycle n='
        $create = Get-ClimbCreateVerdict -Text $text -ExitCode $r.ExitCode -OneWindow
        Write-Climb "[climb] step 1 (in the window) ok=$($create.Ok) $($create.Line)$(if ($create.Why) { ' WHY: ' + $create.Why })"
        $journey = Get-ClimbJourneyVerdict -Text $text -ExitCode $r.ExitCode -OneWindow
        Write-Climb "[climb] step 2 (in the window) ok=$($journey.Ok) $($journey.Line)$(if ($journey.Why) { ' WHY: ' + $journey.Why })"
        $gameGate = Get-ClimbGameGate -Text $text
        $gate = Test-ClimbMayLevelUp -Create $create -Journey $journey
        Write-Climb "[climb] gate: game=$(if ($gameGate.Word) { $gameGate.Word } else { 'no line' }) script=$(if ($gate.Allowed) { 'allowed' } else { 'refused' })$(if ($gate.Why) { ' WHY: ' + $gate.Why })"
        if (($gameGate.Word -eq 'allowed') -ne [bool]$gate.Allowed) {
            $failed = $true
            Write-Climb "[climb] FINDING: the game's gate and this script's rule disagree on the same log: game '$($gameGate.Line)'; script $(if ($gate.Allowed) { 'allowed' } else { 'refused: ' + $gate.Why })"
        }
        if ($gameGate.Word -eq 'allowed') {
            $levelUp = Get-ClimbLevelUpVerdict -Text $text -ExitCode $r.ExitCode
            Write-Climb "[climb] step 3 (in the window) ok=$($levelUp.Ok) $($levelUp.Line)$(if ($levelUp.Why) { ' WHY: ' + $levelUp.Why })"
            if (-not $levelUp.Ok) { $failed = $true }
        }
        else {
            $failed = $true
            Write-Climb "[climb] REFUSED step 3: the game's gate did not allow the level up job ($(if ($gameGate.Line) { $gameGate.Line } else { 'no gate line: the run ended before it; exit code ' + $r.ExitCode }))"
        }
    }
    elseif ($SkipCreate) {
        Write-Climb '[climb] WARN -SkipCreate: steps 1 and 2 are skipped; their granted, place and recovery_place counts are NOT in the sum, and nothing here proves the character was never granted or placed'
    }
    else {
        Write-Climb "[climb] step 1 create: dotnet $(ConvertTo-ClimbArgumentString $commands.Create)"
        $r1 = Invoke-ClimbProcess $dotnetExe $commands.Create (Join-Path $OutDir 'step1-create.log') $HeadlessTimeoutSeconds
        $create = Get-ClimbCreateVerdict -Text (Get-Content -LiteralPath (Join-Path $OutDir 'step1-create.log') -Raw) -ExitCode $r1.ExitCode
        Write-Climb "[climb] step 1 ok=$($create.Ok) $($create.Line)$(if ($create.Why) { ' WHY: ' + $create.Why })"

        Write-Climb "[climb] step 2 journey: dotnet $(ConvertTo-ClimbArgumentString $commands.Journey)"
        if ($create.Ok) {
            $r2 = Invoke-ClimbProcess $dotnetExe $commands.Journey (Join-Path $OutDir 'step2-journey.log') $HeadlessTimeoutSeconds
            $journey = Get-ClimbJourneyVerdict -Text (Get-Content -LiteralPath (Join-Path $OutDir 'step2-journey.log') -Raw) -ExitCode $r2.ExitCode
            Write-Climb "[climb] step 2 ok=$($journey.Ok) $($journey.Line)$(if ($journey.Why) { ' WHY: ' + $journey.Why })"
        }
        else { Write-Climb '[climb] step 2 not run: step 1 did not finish ok' }

        $gate = Test-ClimbMayLevelUp -Create $create -Journey $journey
        if (-not $gate.Allowed) { $failed = $true; Write-Climb "[climb] REFUSED step 3: $($gate.Why)" }
    }

    if (-not $failed -and -not $OneWindow) {
        Write-Climb "[climb] step 3 level-up: pwsh $(ConvertTo-ClimbArgumentString $commands.LevelUp)"
        if ($TrialGround) { Write-Climb "[climb] trial: ground=$TrialGround minutes=$TrialMinutes (the log's 'ladder: trial:' lines say whether it started, every return and its end)" }
        if (-not $NoDisplayHold) {
            $before = Get-GateDisplayState
            Write-Climb "[climb] display preflight: $($before.Detail)"
            if ($before.State -ne 'Enabled') { [void](Invoke-GateDisplayWake) }
            $displayHold = Start-GateDisplayHold -Folder $OutDir
            Write-Climb "[climb] display hold: $(if ($displayHold) { 'on' } else { 'did not start' })"
        }
        $stepStart = Get-Date
        $r3 = Invoke-ClimbProcess $pwshExe $commands.LevelUp (Join-Path $OutDir 'step3-levelup.log') ($playTimeout + 120) -Record:$Record
        $text = Get-Content -LiteralPath (Join-Path $OutDir 'step3-levelup.log') -Raw
        if ($text -notmatch 'autopilot done ok=') {
            # --log-copies keeps Info lines off the console: the isolated user data's own log has them (the lines read are only the climb's).
            $logs = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs'
            if (Test-Path -LiteralPath $logs) {
                $files = @(Get-ChildItem -LiteralPath $logs -File -Filter 'lifestone*.log' | Where-Object { $_.LastWriteTime -ge $stepStart.AddSeconds(-5) } | Sort-Object LastWriteTime)
                if ($files.Count -gt 0) { $text += "`n" + ((Select-String -LiteralPath $files.FullName -Pattern '\[ladder\] at=|autopilot done ok=|autopilot cycle n=' | ForEach-Object { $_.Line }) -join "`n") }
            }
        }
        $levelUp = Get-ClimbLevelUpVerdict -Text $text -ExitCode $r3.ExitCode
        Write-Climb "[climb] step 3 ok=$($levelUp.Ok) $($levelUp.Line)$(if ($levelUp.Why) { ' WHY: ' + $levelUp.Why })"
        if (-not $levelUp.Ok) { $failed = $true }
    }
}
catch {
    $failed = $true
    Write-Climb "[climb] ERROR $($_.Exception.Message)"
}
finally {
    if ($Record) { Stop-WindowRecording -Recording $recording -Label 'climb' } # the recording's module is loaded only with -Record: without it the call was an unknown command, the display hold below was skipped and a clean climb exited 1 (live 2026-11-18, the Redoubt tests)
    if ($Record) { $found = Get-WindowRecordingFiles -Folder $recordFolder; Write-Climb "[climb] recording=$recordFolder files=$($found.Count) bytes=$($found.Bytes)" }
    Stop-GateDisplayHold $displayHold
}

$verdict = Format-ClimbVerdict -Create $create -Journey $journey -LevelUp $levelUp -Minutes $total.Elapsed.TotalMinutes
Write-Climb $verdict
Write-Climb '[climb] the backend was left as it was (the lead stops it)'
exit $(if ($failed) { 1 } else { 0 })
