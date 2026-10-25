<#
.SYNOPSIS
    The integration suite: build, tests and every Godot check harness, one after the other, with a summary table and a
    scan for leaked passwords.

.DESCRIPTION
    Runs, sequentially and off-screen (each Godot run through tools\godot.ps1 -Run -Offscreen -IsolateUserData with a
    timeout, so silent: Dummy audio):

      build                    dotnet build Lifestone.slnx (must report 0 warnings and 0 errors)
      test                     dotnet test Lifestone.slnx --filter "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Status!=KnownFailure" (must pass): the
                               loopback tests need the backend, the soak tier runs only at merge gates and the Exhaustive tier (the dat
                               sweeps and pins, M4 D30: every test that reads the retail dats outside the retail UI, session and avatar tests) runs through slice-check -Exhaustive (G-A and before a merge). The
                               step writes <OutDir>\test-results\*.trx (per-test timings, VSTest's trx logger) and <OutDir>\test-times.txt (the
                               20 slowest tests); its detail and the summary name the slowest (M3 4.2 p5a C6)
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
      record-audio             --play-checks --record-audio=<OutDir>\record-audio\record-audio.wav (M3 1.3 s3, no backend; the audio check; M3 4.2 p5a C3: when play-checks is
                               wanted too, the two steps SHARE this one Godot run, started at the play-checks step, and each keeps its own judges and row; the record-audio row
                               says "shared run <log>", the play-checks row fails on the checks and on any ERROR that is not the recording's, the record-audio row on the
                               recording's: the levels, the file, the muted flag, the sounded counts and an ERROR naming the recording; a run that did not hold names the half
                               that failed in both rows): the play checks on the stand-in
                               server (portal sounds, the F750, the footstep and gesture hooks, the UI's sounds, ambient sound when the run reaches the outdoors) with the game's own
                               sound recorded from the Master bus; automated and therefore muted (M2 D8: no --volume; the recording is taken before the bus's level, so it holds the full
                               mix and nothing reaches the speakers). The run quits normally, the game reads the file back (Lifestone.Client.Audio.WaveLevels) and logs
                               "audio recording levels file= seconds= peak_dbfs= rms_dbfs= active_windows= sources=<server:n,hooks:n,footsteps:n,footsteps_sounded:n,ui:n,ambient:n,server_handed:n,hooks_handed:n; each n or n/a; server and hooks are voices started, the *_handed counts are calls handed to the sound player> windows= muted=true". The step passes on the play
                               checks' own verdict, a recording that exists, is non-silent (peak above -60 dBFS and at least one active 50 ms window) and was taken with the Master bus
                               muted, and the sounded counts in sources= are judged: server>=1 and hooks>=1 (a recording of portal sounds alone fails; footsteps stay unjudged, TODO(footstep-sound-table)). Detail: "peak_dbfs=<x> rms_dbfs=<x> active_windows=<n>/<n> seconds=<x> muted=true sources=<..>"
      shell-frames             --play-checks --play-frames=<OutDir>\shell-frames\<W>x<H> at 800x600 (UI scale 1; with -AllSizes also 1920x1080 at scale 1 and 2560x1440 at scale 2; M3 4.2 p5a C4;
                               the stand-in has no fight or death scenarios since M4 4.1 t1, so there is nothing to leave out), each through
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
      journey-mage             --account=test --login-check=journey --journey=mage --play-screenshot=<OutDir>/frames/journey-mage.png (M4 P5b G1; the mage's journey on BSD Mage: the Godot journey host, the stations in one
                               login; replaces login-buff (journey-mage buff station), login-bsd (den-fight) and login-death (death); needs the local backend; runs ONLY with -Backend require, SKIP under auto and start;
                               muted, 800x600, the Godot run gets 2200 s: the runner's own limit is 2,010 s). Judged by the journey's own done line, "[logincheck] journey done ok=true name=mage stations=<n>/<m> failed=none
                               teleports=<n> ... budget=<n>" (ok is the scenario's: every station ok or skipped by a precondition, teleports<=budget; the step also reads failed=none, an ok=false station line without skipped=, and
                               teleports against budget itself). The station frames (<OutDir>/frames/journey-mage-station-<name>.png and the hunt, vendor and fight frames) are the review frames; the
                               "journey frames station=<n> frame_max_ms=<x>" lines are copied report-only (frame_over_50 is yes when any station's max is over 50; -ContendedFrames reports it, never judges it).
                               Judged from M4 4.2 J2 (the owner's call): teleports<=1 on every journey, recalls>=2 on the mage, recovery_places=0 on every journey, granted=0 on the mage and the archer (melee's granted stays
                               report-only); and the window frames on the station lines (attack_frame_max_ms and loot_frame_max_ms on hunt, vendor_frame_max_ms on vendor and vendor-facts): over 75 ms fails, over 50 ms flags
                               frame_over_50, n/a or absent fails nothing, and with -ContendedFrames (or another Godot run up) they are reported, never judged
      journey-melee            --account=test-melee --login-check=journey --journey=melee (as journey-mage, on Holtburg Brawler; replaces login-hunt (journey-melee hunt station); the Godot run gets 1900 s: the runner's limit is 1,710 s)
      journey-archer           --account=test-archer --login-check=journey --journey=archer (as journey-mage, on Holtburg Archer; replaces login-hunt-missile (journey-archer hunt station); the Godot run gets 2000 s: the runner's limit is 1,860 s)
      spell-showcase           --scene=spellviewer --showcase (four entries; must end with exceptions=0)
      golden-viewpoints        --golden-viewpoints (every viewpoint captured)
      fly-path                 --fly-path=holtburg:arwic:<FlySpeed> (DoD 8: no frame over 50 ms; exit 2 if one is)
      stray-output             no image, video, audio, .bin or .lsrec file appeared under src/ or tests/ during the run

    Every step's console output goes to <OutDir>\<nn>-<step>.log, outputs (golden images, terrain-golden diffs) below
    <OutDir>, and the table to <OutDir>\summary.md; <OutDir>\summary.json (M4 1.1 s3) holds the same run as data: the commit, the branch, the test filter,
    the run's start and end (UTC) and, per step, the verdict, the outcome, the seconds and the errors logged (no detail text, so no path and no account
    name); a judged live leg's step also carries frame_over_50 (yes|no|n/a) and frame_judge (counted|contended), shown in the md as a "frame_over_50" line (report-only). The build step runs dotnet restore --locked-mode first (the package graph must match the
    committed packages.lock.json files) and builds with --no-restore. Exits 0 when no step failed and the secret scan found nothing, 1
    otherwise.

    A step ends PASS, FAIL or SKIP. SKIP (yellow) is not a failure, and the summary counts it apart: a step that needs
    the local backend (server\README.md) is skipped while the backend is not ready, unless -Backend says otherwise. No
    step that needs it joins with its harness (login-check is the first). Ready means what the loopback
    tests' BackendProbe means: the main checkout's server\runtime\pids.json names a live mariadbd and a live ACE whose
    entry has modReady = true, ACE listens on UDP 127.0.0.1:<acePort of server\versions.json>, and secrets.json has an
    accounts.test entry (checked by key; no value is read). The check sends nothing to the server. The summary always
    shows the backend's state.

    M4 P4 gate (D31, the parallel live gate): each backend step logs in with the account of its character ($stepAccounts: test for Lifestone Smoke and BSD Mage; test-tools for Lifestone Throwaway (M4 D39); test-melee for the Brawler's journey-melee;
    test-archer for the Archer's journey-archer; journey-mage runs on test), and the readiness probe checks the key of each account the wanted steps need (key names only). -Lanes runs the wanted backend steps of each account at once (a child
    smoke per account, its steps one after the other, at most three Godot runs, each child with -ContendedFrames); the default is one step at a time. -ContendedFrames: the live steps run beside other Godot runs, so
    -Backend require does not imply -SoloFrames: a frame-time failure with another run up is reported as frames=contended (a PASS). Every Godot run of a step in $audioSteps must log `audio service installed ... muted=true`
    (Get-AudioMuteVerdict): none, or muted=false, fails the step with a named reason (the record-audio step is held to it too: it records the Master bus before the bus's level and the run is muted all the same).

    After the steps, the secret scan reads every password in the main checkout's server\runtime\secrets.json (the
    account and database passwords, schema 1 or 2) into memory only and counts where one appears, as UTF-8 or UTF-16LE
    bytes, in this run's OutDir, the isolated Godot logs (cache\godot\**\logs), session recordings (cache\sessions) and
    ACE's logs (server\runtime\logs\ACE_Log*.txt, rolled copies too). The summary's 'secrets=<n>' line gives the count
    and, for each hit, the file and the secrets.json key that matched, never a value. Any hit, or a file or folder that
    could not be read or listed, fails the run.

    The scan is bounded (M3 4.2 p5a C8): this run's OutDir is always read whole; the other folders (the isolated Godot logs, the
    session recordings, ACE's logs) are read only for files written since the last clean full or incremental scan (the stamp
    cache\smoke-scan.json holds the scan's start time and secrets.json's modification time and length, never a password; a file
    is read when it changed in the 2 minutes before that start or since). A full scan runs when there is no readable stamp, the
    stamp is over 7 days old, secrets.json changed (a new password may already sit in old logs) or -FullScan is given: slice-check
    -Smoke (G-A) and the live gate pass it. The 'secrets=' line says "full scan" or "incremental since <utc>: <n> file(s) unchanged,
    not read" (counts only).

    Steps that need neither the backend nor an undisturbed machine run two at a time with -Parallel 2 (default 1): cli-help,
    cli-bad-argument, terrain-golden, engine-checks, interior-engine-checks, sky-checks, blend-checks, portal-space-checks,
    retail-ui, spell-showcase and golden-viewpoints. Each runs as a child of this script (-Only <step>, its own OutDir below
    <OutDir>\par\<step>, its own Godot user-data slot) and the batch is joined before any other step starts, so no frame-judged
    step ever shares the machine with one of them; the rows are replayed in the table's order. A further child starts only
    while fewer than 3 Godot processes run on the machine (other agents' too).

    Frame-time checks (the play checks: an-unhidden-object-and-the-player-pass-through-intermediate-alpha-and-end-opaque-with-no-
    frame-over-50-ms, no-frame-over-50-ms-from-the-arrival-to-the-teleport, enter-right-after-character-select, no-frame-over-50-ms-
    from-enter-to-the-arrival-with-a-heavy-player-description and no-ui-step-after-enter-is-over-12-ms; the live legs' frame windows
    (cast_frame_max_ms, fight_frame_max_ms, death_frame_max_ms, enter_to_walk and the teleport windows)) are judged against the machine
    they ran on (M3 4.2 D42): the steps that run them (play-checks, record-audio, login-check, login, journey-mage,
    journey-melee, journey-archer) sample the Godot processes on the machine (the step's own excluded). When every failure of such a step is a
    frame-time check and another Godot run was up during the step, the step is reported, not failed: "PASS (frames=contended)" and
    the detail names the checks. With no other run it fails as today. The live legs' rule, settled (M4 1.1 s3): -Backend require implies -SoloFrames, so at the live gate a
    leg's frame judge counts only when no other engine process is up (a leg waits for a quiet machine and a frame-time failure fails); elsewhere a leg that ran
    with another engine process up reports frames=contended (the verdict stands, the frame judge is not counted). -SoloFrames: any frame-time failure fails, and a frame-judged
    step does not start while another Godot run is up (it waits at most 10 minutes, one line per minute, then fails as refused).

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
    [string] $DotnetRoot = '',
    [switch] $SoloFrames,
    # M4 P4 gate: the parallel live phase. The live steps run beside other Godot runs (the gate's three lanes), so a frame-time failure with another run up is REPORTED
    # (frames=contended, a PASS), as it is without -Backend require; -Backend require no longer implies -SoloFrames then. Mutually exclusive with -SoloFrames.
    [switch] $ContendedFrames,
    # M4 P4 gate: when the wanted backend steps belong to more than one test account, run each account's steps at once (one child smoke per account, its steps one after the other,
    # at most three Godot runs; implies -ContendedFrames). Without it the steps run one at a time, as they always did.
    [switch] $Lanes,
    [switch] $FullScan,
    # M4 P4 rfix-a (R4): extra folders the secret scan reads whole (the live gate names its whole gate folder and its dotnet test logs). Comma-separated.
    [string[]] $ScanDir = @(),
    [ValidateRange(1, 3)]
    [int] $Parallel = 1,
    # Internals of -Parallel (a child run of one step): the step's number in the parent's table, where it writes its rows, the Godot user-data slot, and no secret scan (the parent scans the lot).
    [Parameter(DontShow)] [int] $StepNumber = 0,
    [Parameter(DontShow)] [string] $ResultsJson = '',
    [Parameter(DontShow)] [ValidatePattern('^[a-z0-9]{0,8}$')] [string] $UserDataSlot = '',
    [Parameter(DontShow)] [switch] $NoScan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FrameRule {
    <# 4.2 fix A (R4): the frame rule in force. The live gate (-Backend require) always judges frames solo (D42: a frame-time failure there is never "contended") and scans in full; elsewhere the rule is what the switches say.
       Returns @{ Solo; Full; Text }: Text is the words the smoke's first line prints. #>
    param([string] $Backend, [bool] $Solo, [bool] $Full, [bool] $Contended = $false)
    if ($Solo -and $Contended) { throw '-SoloFrames and -ContendedFrames (or -Lanes) contradict each other: the frame rule is solo or contended, not both.' }
    $implied = $Backend -eq 'require' -and -not $Contended
    $soloNow = $Solo -or $implied
    $fullNow = $Full -or $Backend -eq 'require'
    $text = if ($Contended) { 'frames: contended (-ContendedFrames or -Lanes: the live steps run beside other Godot runs; a frame-time failure with another run up is reported as frames=contended, a PASS; frame-over-50 flags are reported, not judged)' }
    elseif ($soloNow) { "frames: solo (-SoloFrames$(if ($implied -and -not $Solo) { ' implied by -Backend require' })): a frame-time failure fails, no contended pass)" } else { 'frames: contended-allowed (a frame-time failure with another Godot run up is reported, not failed; -SoloFrames judges it)' }
    return @{ Solo = $soloNow; Full = $fullNow; Text = $text }
}
$frameRule = Get-FrameRule -Backend $Backend -Solo ([bool]$SoloFrames) -Full ([bool]$FullScan) -Contended ([bool]($ContendedFrames -or $Lanes))
$SoloFrames = [switch]$frameRule.Solo
# P4-R6 (D31: the parallel phase reports, the solo phase judges): in a parallel run (-ContendedFrames or -Lanes) a frame-only failure is reported whatever else is running.
$parallelPhase = [bool]($ContendedFrames -or $Lanes)
$ScanDir = @($ScanDir | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$FullScan = [switch]$frameRule.Full

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
Import-Module (Join-Path $PSScriptRoot 'lib\TestTimes.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot
$dotnetExe = Join-Path $DotnetRoot 'dotnet.exe'

$allSteps = @('build', 'test', 'cli-help', 'cli-bad-argument', 'terrain-golden', 'engine-checks', 'interior-engine-checks',
    'sky-checks', 'blend-checks', 'portal-space-checks', 'portal-space', 'retail-ui', 'offline-player-checks', 'offline-player', 'play-checks', 'record-audio', 'shell-frames', 'login-check', 'login', 'login-throwaway', 'journey-mage', 'journey-melee', 'journey-archer', 'spell-showcase', 'golden-viewpoints', 'fly-path', 'stray-output')
# The steps that need the local backend (gated by -Backend).
$backendSteps = @('login-check', 'login', 'login-throwaway', 'journey-mage', 'journey-melee', 'journey-archer')
# M4 P4 gate (D31): the test account each backend step logs in with, from the step's character (the fighter templates' accounts for the hunts). The readiness probe checks the key of each account a wanted
# step needs (by key name only), and -Lanes groups the wanted steps by this value.
$stepAccounts = [ordered]@{
    'login-check'        = 'test'          # Lifestone Smoke
    'login'              = 'test'          # Lifestone Smoke (tools\play.ps1 -Offscreen)
    'login-throwaway'    = 'test-tools'    # Lifestone Throwaway (M4 D39)
    'journey-mage'       = 'test'          # BSD Mage (M4 P5b G1)
    'journey-melee'      = 'test-melee'    # Holtburg Brawler (FighterTemplate.Melee)
    'journey-archer'     = 'test-archer'   # Holtburg Archer (FighterTemplate.Archer)
}
# The journey steps (M4 P5b G1): the journey name and the Godot timeout of each (J5's handoff: the runner's own limit is WholeSecondsFor(plan) = 60 + 1.5 x expected + 300: mage 2,010 s, melee 1,710, archer 1,860).
$journeySteps = @{
    'journey-mage'   = @{ Journey = 'mage'; Seconds = 2200 }
    'journey-melee'  = @{ Journey = 'melee'; Seconds = 1900 }
    'journey-archer' = @{ Journey = 'archer'; Seconds = 2000 }
}
# The steps whose Godot run installs the audio service (play mode: the portal view, M2 D8): their log must say `audio service installed ... muted=true` (Get-AudioMuteVerdict, Invoke-Godot).
$audioSteps = @('portal-space', 'offline-player-checks', 'offline-player', 'play-checks', 'record-audio', 'shell-frames', 'login-check', 'login', 'login-throwaway', 'journey-mage', 'journey-melee', 'journey-archer')
# The steps whose verdict judges frame time: a failure of one of their frame-time checks is judged against the machine (D42, the help above).
$frameJudgedSteps = @('play-checks', 'record-audio', 'login-check', 'login', 'journey-mage', 'journey-melee', 'journey-archer')
# The frame-time checks of the play checks (PlayChecks.cs: the ones that compare a frame or a UI step with FrameLimitMilliseconds / MaxUiStepMilliseconds).
$frameTimeChecks = @(
    'an-unhidden-object-and-the-player-pass-through-intermediate-alpha-and-end-opaque-with-no-frame-over-50-ms',
    'no-frame-over-50-ms-from-the-arrival-to-the-teleport',
    'enter-right-after-character-select',
    'no-frame-over-50-ms-from-enter-to-the-arrival-with-a-heavy-player-description',
    'no-ui-step-after-enter-is-over-12-ms')
# The steps -Parallel runs two at a time: backend-free, and never frame-judged (a frame-judged step never shares the machine with them).
$parallelSteps = @('cli-help', 'cli-bad-argument', 'terrain-golden', 'engine-checks', 'interior-engine-checks', 'sky-checks', 'blend-checks', 'portal-space-checks', 'retail-ui', 'spell-showcase', 'golden-viewpoints')
# The default test run leaves out the loopback tests (they need the backend), the soak tier (merge gates only) and the Exhaustive tier (the dat sweeps and pins, M4 D30; slice-check -Exhaustive runs it at G-A and before a merge).
$testFilter = 'Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Status!=KnownFailure'
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
$script:audioFailures = @{}
$script:stepIndex = if ($StepNumber -gt 0) { $StepNumber - 1 } else { 0 }
$script:enginePeak = 0
$script:testTimesLine = ''
# C3: the one Godot run the play-checks and record-audio steps share (Get-PlayChecksRun).
$script:sharedPlay = $null
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
    param([string] $FileName, [string[]] $Arguments, [string] $LogPath, [int] $TimeoutSeconds, [switch] $KillRootOnly, [switch] $SampleEngines)
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
    $exited = $false
    if ($SampleEngines) {
        # D42: how many Godot processes ran at once while this step ran (its own included), sampled every 2 s: $script:enginePeak.
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $exited -and [DateTime]::UtcNow -lt $deadline) {
            $exited = $process.WaitForExit(2000)
            $script:enginePeak = [math]::Max($script:enginePeak, (Get-EngineProcessCount))
        }
    }
    else { $exited = $process.WaitForExit($TimeoutSeconds * 1000) }
    if (-not $exited) {
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

function Get-EngineProcessCount {
    <# Godot game processes on the machine (the console wrappers excluded): the count the brief's "other Godot run" rule uses. #>
    return @(Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*_console' }).Count
}

function Get-AudioMuteVerdict {
    <#
      M4 P4 gate (D31, M2 D8): an automated Godot run is silent. The game logs `audio service installed ... muted=<bool> device=...` when it installs the audio service (AudioService.Install); a run whose log has
      no such line, or one with muted=false, fails its step. Returns @{ Ok; Reason }: Reason names the failure ('' when Ok). The record-audio step is not exempted from this: it records the Master bus BEFORE the bus's
      level (so the recording holds the full mix) and the run is muted all the same, and the levels line's muted=true is judged there as well (the two judges agree).
    #>
    param([string] $Text)
    $found = [regex]::Matches($Text, 'audio service installed[^\r\n]*?\bmuted=(?<m>true|false)\b', 'IgnoreCase')
    if ($found.Count -eq 0) { return @{ Ok = $false; Reason = 'the log has no "audio service installed ... muted=true" line (the run never installed its audio service, so it cannot be shown silent; M2 D8)' } }
    foreach ($m in $found) {
        if ($m.Groups['m'].Value -ne 'true') { return @{ Ok = $false; Reason = 'the log says "audio service installed ... muted=false": an automated run must be muted (M2 D8; -Audio with -Offscreen is refused)' } }
    }
    return @{ Ok = $true; Reason = '' }
}

function Set-AudioVerdict {
    <# Records a failed audio check for $Step (Add-Result appends it to the step's row and fails it); prints it at once. #>
    param([string] $Step, [string] $Text)
    if ($audioSteps -notcontains $Step) { return }
    $verdict = Get-AudioMuteVerdict $Text
    if (-not $verdict.Ok) {
        $script:audioFailures[$Step] = $verdict.Reason
        Write-Host "[smoke] $Step`: audio check FAILED: $($verdict.Reason)" -ForegroundColor Red
    }
}

function Invoke-Godot {
    <# One off-screen game run through tools\godot.ps1 (at -Resolution WxH when given); returns @{ Code; Log; Text; Others; Seconds }.
       Others: the most other Godot runs seen on the machine while it ran (before it started, or sampled during it; its own excluded). #>
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
    if ($UserDataSlot) { $arguments += @('-UserDataSlot', $UserDataSlot) }
    $arguments += "-GameArgs:$GameArgs"
    $before = Get-EngineProcessCount
    $script:enginePeak = 0
    $started = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath $log -TimeoutSeconds ($TimeoutSeconds + 60) -SampleEngines
    $others = [math]::Max($before, $script:enginePeak - 1)
    Set-AudioVerdict $Step (Get-Content -LiteralPath $log -Raw)
    return @{ Code = $code; Log = $log; Text = (Get-Content -LiteralPath $log -Raw); Others = $others; Seconds = $started.Elapsed.TotalSeconds }
}

function Test-RecordedSources([string] $Sources) {
    <# The record-audio step's judge of the sounded counts in the levels line's sources= value (M3 2.1 s3, 1.3 review R7): server>=1 and hooks>=1, so a recording of portal sounds alone fails.
       The stand-in's play checks send an F750 and play the cast and walk hooks. Footsteps stay unjudged (TODO(footstep-sound-table): the human sound table has no footstep variant); the handed
       counts, the UI and the ambient count are reported. Returns @{ Ok; Detail } naming the count that failed. #>
    $counts = @{}
    foreach ($pair in ($Sources -split ',')) {
        $kv = $pair -split ':', 2
        if ($kv.Count -eq 2) { $counts[$kv[0]] = $kv[1] }
    }
    $failed = @()
    foreach ($name in 'server', 'hooks') {
        $n = 0
        if (-not $counts.ContainsKey($name)) { $failed += "$name is missing from sources= (a sounded count was expected)" }
        elseif (-not [int]::TryParse($counts[$name], [ref] $n)) { $failed += "$name=$($counts[$name]) (a sounded count was expected)" }
        elseif ($n -lt 1) { $failed += "$name=$n (>= 1 expected)" }
    }
    return @{ Ok = ($failed.Count -eq 0); Detail = ($failed -join '; ') }
}

function Get-PlayCheckFailures([string] $Text) {
    <# The names of the play checks that failed in a run's log ('[playcheck] FAIL <name>'), in order. #>
    return @([regex]::Matches($Text, '\[playcheck\] FAIL (\S+)') | ForEach-Object { $_.Groups[1].Value })
}

function Get-RecordingErrorCount([string] $Text) {
    <# Lifestone ERROR lines that name the recording: the record-audio half of a shared play-checks run (Main.cs's "--record-audio: nothing is recorded", the audio service's). #>
    return ([regex]::Matches($Text, 'Z ERROR \[[^\]]*\][^\r\n]*(?:--record-audio|audio recording|record-audio)')).Count
}

function Get-FrameVerdict {
    <#
      D42 for the play checks. $Failed: the checks that failed; $Frame of them are frame-time checks ($FrameChecks), $Other are not. Contended
      (the step is reported, not failed) when every failure is a frame-time check, no ERROR line besides those checks' own (and the $IgnoredErrors the
      other half of a shared run owns) was logged, another Godot run was up during the step ($Others > 0) and -SoloFrames is off.
      Returns @{ Contended; Frame; Other; Errors (ERROR lines the step still fails on); Note }.
    #>
    param([string] $Text, [int] $Others, [bool] $Solo, [string[]] $FrameChecks, [int] $IgnoredErrors = 0, [bool] $Reported = $false)
    $failed = @(Get-PlayCheckFailures $Text)
    $frame = @($failed | Where-Object { $FrameChecks -contains $_ })
    $other = @($failed | Where-Object { $FrameChecks -notcontains $_ })
    $errors = [math]::Max(0, (Get-ErrorCount $Text) - $IgnoredErrors)
    $contended = $frame.Count -gt 0 -and $other.Count -eq 0 -and ($errors - $frame.Count) -le 0 -and ($Others -gt 0 -or $Reported) -and -not $Solo
    $note = ''
    if ($contended) { $note = "; frames=contended: $($frame -join ', ') failed with $Others other Godot run(s) up$(if ($Reported) { ' (parallel phase: reported, not judged)' }) (reported, not failed; rerun on a quiet machine or with -SoloFrames to judge)" }
    elseif ($frame.Count -gt 0) { $note = "; frame-time check(s) failed: $($frame -join ', ') (other Godot runs up: $Others$(if ($Solo) { '; -SoloFrames' }))" }
    return @{ Contended = $contended; Frame = $frame; Other = $other; Errors = $(if ($contended) { $errors - $frame.Count } else { $errors }); Note = $note }
}

function Get-LiveFrameVerdict {
    <#
      D42 for the live steps (login-check, login, journey-mage, journey-melee, journey-archer): their frame windows are judged in the game and reach the log as
      ERROR lines: 'frame over 50 ms between Enter and walking', 'frame over 50 ms around a server teleport' and a leg's 'the ... leg failed ... why=...'
      whose reasons are all '<window>_frame_max_ms=<x> (at most 75 expected)'. Contended when there is at least one ERROR line and every one of them is such a
      line, another Godot run was up and -SoloFrames is off. Conservative on purpose (a failure with no ERROR line, or any other ERROR, stays a failure).
      Settled (M4 1.1 s3, closes p5a-live-frame-contended): -Backend require implies -SoloFrames (Get-FrameRule), so at the live gate a live leg's frame
      judge counts only when no other engine process is up: a leg does not start while another Godot run is up and a frame-time failure then fails, never
      a contended pass. Under -Backend auto or start without -SoloFrames, a leg that ran with another engine process up reports frames=contended: the
      verdict stands and the frame judge is not counted (this function). Either way the summary's per-step frame_over_50 and frame judge fields (Get-FrameFields)
      say whether the leg's 50 ms flag was counted or contended.
    #>
    param([string] $Text, [int] $Others, [bool] $Solo, [bool] $Reported = $false)
    $lines = @([regex]::Matches($Text, 'Z ERROR \[[^\]]*\][^\r\n]*') | ForEach-Object { $_.Value })
    $names = [System.Collections.Generic.List[string]]::new()
    $allFrame = $lines.Count -gt 0
    foreach ($line in $lines) {
        if ($line -match 'frame over 50 ms') { $names.Add('frame over 50 ms (enter or teleport window)'); continue }
        if ($line -match 'leg failed[^\r\n]*why=(?<why>.*)$') {
            $segments = @($Matches['why'] -split ';\s*' | Where-Object { $_ })
            $frameOnly = $segments.Count -gt 0
            foreach ($segment in $segments) {
                if ($segment -match '^\s*(?<n>\w*frame_max_ms)=[\d.]+ \(at most \d+ expected\)') { $names.Add($Matches['n']) } else { $frameOnly = $false }
            }
            if ($frameOnly) { continue }
        }
        $allFrame = $false
    }
    $contended = $allFrame -and ($Others -gt 0 -or $Reported) -and -not $Solo
    $note = if ($contended) { "; frames=contended: $(($names | Select-Object -Unique) -join ', ') failed with $Others other Godot run(s) up$(if ($Reported) { ' (parallel phase: reported, not judged)' }) (reported, not failed)" } else { '' }
    return @{ Contended = $contended; Names = @($names | Select-Object -Unique); Note = $note }
}

function Wait-SoloEngine {
    <#
      -SoloFrames: a frame-judged step does not start while another Godot run is up. Waits at most 10 minutes with a line per minute; after that the step
      is recorded as a FAIL (refused) and this returns $false. Without -SoloFrames it returns $true at once.
    #>
    param([string] $Step, [System.Diagnostics.Stopwatch] $Timer)
    if (-not $SoloFrames) { return $true }
    $started = Get-Date
    $reported = -1
    while ($true) {
        $count = Get-EngineProcessCount
        if ($count -eq 0) { return $true }
        $waited = ((Get-Date) - $started).TotalSeconds
        if ($waited -ge 600) {
            Add-Result $Step $false 0 $Timer.Elapsed.TotalSeconds "refused to start: $count other Godot run(s) were up for 10 minutes (-SoloFrames)" -NotRun
            return $false
        }
        $minute = [int][math]::Floor($waited / 60)
        if ($minute -gt $reported) {
            $reported = $minute
            Write-Host "[smoke] $Step`: waiting for $count other Godot run(s) to end (-SoloFrames), minute $($minute + 1) of 10"
        }
        Start-Sleep -Seconds 5
    }
}

function Resolve-LiveFrames {
    <# D42 for a live step: $Ok is the step's own verdict; a failure that is only frame windows with another Godot run up is reported as contended (PASS with the note), nothing else changes. Returns @{ Ok; Errors; Note; PassNote }. #>
    param([bool] $Ok, $Run)
    if ($Ok) { return @{ Ok = $true; Errors = (Get-ErrorCount $Run.Text); Note = ''; PassNote = '' } }
    $verdict = Get-LiveFrameVerdict $Run.Text $Run.Others ([bool]$SoloFrames) $parallelPhase
    if ($verdict.Contended -and $Run.Code -ne 124) { return @{ Ok = $true; Errors = -1; Note = $verdict.Note; PassNote = 'frames=contended' } }
    return @{ Ok = $false; Errors = (Get-ErrorCount $Run.Text); Note = ''; PassNote = '' }
}

function Get-PlayChecksRun {
    <#
      C3: the play-checks and record-audio steps share ONE Godot run (--play-checks --record-audio=<wav>) when both are wanted. The first of them to be
      reached starts it; the second reads the same result. Returns @{ Run; Rel; Wav; Owner; Refused }.
    #>
    param([string] $Step, [System.Diagnostics.Stopwatch] $Timer)
    if ($script:sharedPlay) { return $script:sharedPlay }
    $wav = Join-Path (Join-Path $OutDir 'record-audio') 'record-audio.wav'
    $rel = [IO.Path]::GetRelativePath($repoRoot, $wav).Replace('\', '/')
    $shared = @{ Run = $null; Rel = $rel; Wav = $wav; Owner = $Step; Refused = $false }
    if (Wait-SoloEngine $Step $Timer) { $shared.Run = Invoke-Godot $Step "--play-checks --record-audio=$rel" 420 '' $defaultSize }
    else { $shared.Refused = $true }
    $script:sharedPlay = $shared
    return $shared
}

function Get-FrameFlags([string] $Text, [string] $Prefix) {
    <#
      The 50 ms flags of a login-check leg (docs/milestones/m3-plan.md section 5: a window's worst frame over 75 ms fails in the leg;
      over 50 ms the leg prints frame_over_50=yes and still passes, the gate judges the worst of three runs). Reads the leg's
      '[logincheck] <Prefix> done ...' line; returns @{ Over50 = [bool]; Suffix = '; frame_over_50=<yes|no> <window>_frame_max_ms=<x> ...' }.
    #>
    $line = [regex]::Match($Text, '\[logincheck\] ' + [regex]::Escape($Prefix) + ' done [^\r\n]*')
    if (-not $line.Success) { return @{ Over50 = $false; Suffix = '' } }
    $over = $line.Value -match ' frame_over_50=yes(\s|$)'
    $maxima = @([regex]::Matches($line.Value, '(?<![\w])([a-z_]*frame_max_ms)=([\d.]+|n/a)') | ForEach-Object { "$($_.Groups[1].Value)=$($_.Groups[2].Value)" })
    $flag = if ($over) { 'yes' } else { 'no' }
    return @{ Over50 = $over; Suffix = ('; frame_over_50=' + $flag + $(if ($maxima.Count) { ' ' + ($maxima -join ' ') } else { '' })) }
}

function Get-JourneyVerdict([string] $Text, [bool] $FrameContended = $false) {
    <#
      M4 P5b G1: the judge of a journey step, from the Godot journey's own lines: '[logincheck] journey done ok=true name=<n> stations=<k>/<m> failed=none teleports=<t> ... budget=<b> ...' with
      teleports<=budget, and no '[logincheck] journey station=<n> ok=false' line without skipped= (a skip by a precondition is not a failure). Returns @{ Ok; Detail; Frames; FramesSeen; FrameMax;
      Windows; WindowsSeen; WindowMax; WindowOver50 }; Frames is the report-only 'journey frames station=<n> frame_max_ms=<x>' lines folded to 'station:x' pairs.
      M4 4.2 J2 (the owner's call of 2026-10-25: P5b's report-only fields become judges, from the first gate after theirs), on the done line: teleports<=1 on every journey (besides teleports<=budget),
      recalls>=2 on the mage journey (name=mage), recovery_places=0 on every journey, granted=0 on the mage and archer journeys (the melee journey's granted stays report-only: its vendor-facts
      station grants a sale item when the pack holds no loot, by design). A missing or non-numeric judged field fails by name. route_m, rungs, blocked, sightless, vitals_passes and run_s stay report-only.
      The window frames (m4-plan.md section 5, the frame rule), on the station lines: attack_frame_max_ms and loot_frame_max_ms (the hunt station), vendor_frame_max_ms (the vendor and vendor-facts stations);
      'n/a' (the headless host) or an absent field fails nothing. Over 75 ms fails the step and over 50 ms flags it (WindowOver50), unless $FrameContended (another Godot run was up or the run is a parallel
      phase, and -SoloFrames is off): then they are reported, never judged (the Detail names them as contended). Windows is the 'station:field=x' pairs; WindowMax the largest number seen.
    #>
    $done = [regex]::Match($Text, '\[logincheck\] (journey done ok=[^\r\n]*)')
    if (-not $done.Success) { return @{ Ok = $false; Detail = 'no journey done line'; Frames = ''; FramesSeen = $false; FrameMax = 0.0; Windows = ''; WindowsSeen = $false; WindowMax = 0.0; WindowOver50 = $false } }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $line = $done.Groups[1].Value
    $why = @()
    if ($line -notmatch '^journey done ok=true ') { $why += 'done ok is not true' }
    if ($line -notmatch ' failed=none(\s|$)') { $why += 'a station failed' }
    if ($line -match ' teleports=(\d+) ' -and $line -match ' budget=(\d+)(\s|$)') {
        $t = [int]([regex]::Match($line, ' teleports=(\d+)').Groups[1].Value); $b = [int]([regex]::Match($line, ' budget=(\d+)').Groups[1].Value)
        if ($t -gt $b) { $why += "teleports $t over budget $b" }
        elseif ($t -gt 1) { $why += "teleports $t over 1" }
    } else { $why += 'no teleports/budget on the done line' }
    $journeyName = [regex]::Match($line, ' name=(\S+)').Groups[1].Value
    $field = { param([string] $k) $m = [regex]::Match($line, '(?:^|\s)' + $k + '=(\d+)(?=\s|$)'); if ($m.Success) { [int]$m.Groups[1].Value } else { $null } }
    if ($journeyName -eq 'mage') {
        $recalls = & $field 'recalls'
        if ($null -eq $recalls) { $why += 'recalls n/a under 2 (mage)' } elseif ($recalls -lt 2) { $why += "recalls $recalls under 2 (mage)" }
    }
    $places = & $field 'recovery_places'
    if ($null -eq $places) { $why += 'recovery_places n/a, not 0' } elseif ($places -ne 0) { $why += "recovery_places $places not 0" }
    if ($journeyName -eq 'mage' -or $journeyName -eq 'archer') {
        $granted = & $field 'granted'
        if ($null -eq $granted) { $why += "granted n/a, not 0 ($journeyName)" } elseif ($granted -ne 0) { $why += "granted $granted not 0 ($journeyName)" }
    }
    $bad = @([regex]::Matches($Text, '\[logincheck\] journey station=(\S+) ok=false[^\r\n]*') | Where-Object { $_.Value -notmatch ' skipped=' } | ForEach-Object { $_.Groups[1].Value })
    if ($bad.Count) { $why += "station(s) ok=false: $($bad -join ',')" }
    $pairs = @(); $max = 0.0; $seen = $false
    foreach ($m in [regex]::Matches($Text, '\[logincheck\] journey frames station=(\S+) [^\r\n]*?frame_max_ms=([\d.]+|n/a)')) {
        $pairs += "$($m.Groups[1].Value):$($m.Groups[2].Value)"
        if ($m.Groups[2].Value -ne 'n/a') { $seen = $true; $max = [math]::Max($max, [double]::Parse($m.Groups[2].Value, $inv)) }
    }
    $windows = @(); $windowMax = 0.0; $windowSeen = $false; $over75 = @(); $over50 = $false
    foreach ($station in [regex]::Matches($Text, '\[logincheck\] journey station=(\S+) [^\r\n]*')) {
        foreach ($w in [regex]::Matches($station.Value, '(?<![\w])((?:attack|loot|vendor)_frame_max_ms)=([\d.]+|n/a)')) {
            $windows += "$($station.Groups[1].Value):$($w.Groups[1].Value)=$($w.Groups[2].Value)"
            if ($w.Groups[2].Value -eq 'n/a') { continue }
            $x = [double]::Parse($w.Groups[2].Value, $inv)
            $windowSeen = $true; $windowMax = [math]::Max($windowMax, $x)
            if ($x -gt 50.0) { $over50 = $true }
            if ($x -gt 75.0) { $over75 += "$($station.Groups[1].Value) $($w.Groups[1].Value)=$($w.Groups[2].Value)" }
        }
    }
    $contendedNote = ''
    if ($over75.Count -and -not $FrameContended) { $why += "window frame over 75 ms: $($over75 -join ', ')" }
    elseif ($over75.Count) { $contendedNote = "; window frames=contended: $($over75 -join ', ') over 75 ms (reported, not judged)" }
    $detail = $line + $(if ($why.Count) { '; ' + ($why -join '; ') } else { '' }) + $contendedNote
    return @{ Ok = ($why.Count -eq 0); Detail = $detail; Frames = ($pairs -join ' '); FramesSeen = $seen; FrameMax = $max; Windows = ($windows -join ' '); WindowsSeen = $windowSeen; WindowMax = $windowMax; WindowOver50 = $over50 }
}

function Get-FrameFields {
    <#
      M4 1.1 s3 (m4-plan.md section 9, the 4.2 k7 rows): the 50 ms flag of a judged live leg and whether its frame judge counts, for the summary (md and json), so the
      worst-of-three rule reads them. Report-only: the leg still passes on its own verdict. $Flags is Get-FrameFlags' result ($null for a leg whose line carries no
      flag: login-check, login). Over50: 'yes' or 'no' as the leg printed it, 'n/a' when it printed none. Judge: 'contended' when another Godot run was up while the
      leg ran (the verdict stands, the frame judge is not counted), else 'counted'. Returns @{ Over50; Judge }.
    #>
    param($Flags, $Run)
    $over = if ($null -eq $Flags -or -not $Flags.Suffix) { 'n/a' } elseif ($Flags.Over50) { 'yes' } else { 'no' }
    $judge = if ($Run.Others -gt 0) { 'contended' } else { 'counted' }
    return @{ Over50 = $over; Judge = $judge }
}

function Get-ErrorCount([string] $text) {
    # Lifestone's own ERROR entries (Log.Error: '<utc>Z ERROR [category] ...'); Godot's engine messages are not counted.
    return ([regex]::Matches($text, 'Z ERROR \[')).Count
}

function Add-Result {
    <#
      Records one step. $Errors (Lifestone ERROR lines the step logged; -1 = not counted) above 0 fails the step.
      $PassNote qualifies a pass ('retry', 'attributed') so it is visible in the Result column, not only in Detail.
      $FrameOver50 ('yes', 'no' or 'n/a') and $FrameJudge ('counted' or 'contended') are set by a judged live leg (M4 1.1 s3, Get-FrameFields) and reach
      the summary (md and json) per step: report-only, they never fail the step.
      -Skipped records a SKIP: the step did not run ($Detail says why), which is not a failure. -NotRun leaves the Exit
      column empty for a step that did not run (a SKIP, or a FAIL because the backend was not ready).
    #>
    param([string] $Step, [bool] $Pass, [int] $Code, [double] $Seconds, [string] $Detail, [string] $Log = '', [int] $Errors = -1,
        [string] $PassNote = '', [switch] $Skipped, [switch] $NotRun, [string] $FrameOver50 = '', [string] $FrameJudge = '')
    if ($Errors -gt 0) {
        $Pass = $false
        $Detail = "$Detail; FAIL: $Errors ERROR line(s) logged"
    }
    if (-not $Skipped -and $script:audioFailures.ContainsKey($Step)) {
        # M4 P4 gate: a Godot run of this step was not muted (or never said so): the step fails with the named reason (Get-AudioMuteVerdict).
        $Pass = $false
        $Detail = "$Detail; FAIL: audio check: $($script:audioFailures[$Step])"
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
            FrameOver50 = $FrameOver50
            FrameJudge  = $FrameJudge
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

function Get-StepAccounts {
    <# The test accounts the given backend steps log in with (distinct, in the table's order); steps that are not backend steps need none. #>
    param([string[]] $Steps)
    return @($stepAccounts.Keys | Where-Object { $Steps -contains $_ } | ForEach-Object { $stepAccounts[$_] } | Select-Object -Unique)
}

function Test-SecretsAccounts {
    <#
      P4-R2: whether secrets.json holds an accounts.<name> key for each of $Accounts. By key name only: the document is parsed but no value is read. Returns '' when every key is there, else the reason.
      The names are compared over EnumerateObject (a JsonElement.TryGetProperty call with a loop variable is bound to the wrong overload by PowerShell and threw live).
    #>
    param([string] $Path, [string[]] $Accounts = @('test'))
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path))
        $have = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $root = $document.RootElement
        if ($root.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach ($property in $root.EnumerateObject()) {
                if ($property.Name -ceq 'accounts' -and $property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                    foreach ($entry in $property.Value.EnumerateObject()) { [void]$have.Add($entry.Name) }
                }
            }
        }
        foreach ($account in $Accounts) {
            if (-not $have.Contains([string]$account)) {
                return "secrets.json has no accounts.$account (schema 2 comes with the server mod; server\setup-server.ps1 migrates; the other test accounts are added with setup-server.ps1 -AddAccount)"
            }
        }
    }
    catch { return "secrets.json could not be parsed ($(Get-ExceptionKind $_))" }
    finally { if ($document) { $document.Dispose() } }
    return ''
}

function Test-BackendReady {
    <#
      Whether the local backend is ready for a step that logs in: the rule of the loopback tests' BackendProbe. Read-only:
      it reads pids.json and the key names of secrets.json (never a value), and sends nothing to the server.
      Returns @{ Ready; Reason }; Reason says what is missing first.
      M4 P4 gate: -Accounts names the accounts the wanted steps need (Get-StepAccounts; default `test`); the key of each must be in secrets.json (by key name only).
    #>
    param([string[]] $Accounts = @('test'))
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
    $problem = Test-SecretsAccounts -Path $secretsPath -Accounts $Accounts
    if ($problem) { return @{ Ready = $false; Reason = $problem } }
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
    $state = Test-BackendReady -Accounts (Get-StepAccounts @($step))
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

function Get-ScanPlan {
    <#
      C8: whether the secret scan reads everything or only what changed. Full when forced (-FullScan), when the stamp ($StampPath: the last clean scan's start and
      secrets.json's modification time and length, never a password) is missing, unreadable or over 7 days old, or when secrets.json is not the file the stamp saw
      (a new password may already sit in old logs). Otherwise incremental: files written since the stamp's start less 2 minutes (a log still being appended).
      Returns @{ Full; Since (UTC, or $null when Full); Reason }.
    #>
    param([string] $StampPath, [string] $SecretsPath, [bool] $Force, [datetime] $Now)
    if ($Force) { return @{ Full = $true; Since = $null; Reason = 'full scan (-FullScan)' } }
    if (-not (Test-Path -LiteralPath $StampPath -PathType Leaf)) { return @{ Full = $true; Since = $null; Reason = 'full scan (no stamp)' } }
    try {
        $stamp = Get-Content -LiteralPath $StampPath -Raw | ConvertFrom-Json
        $started = [DateTime]::new([long]$stamp.scanStartedTicks, [DateTimeKind]::Utc)
        $info = Get-Item -LiteralPath $SecretsPath -ErrorAction Stop
        if ($info.LastWriteTimeUtc.Ticks -ne [long]$stamp.secretsMtimeTicks -or $info.Length -ne [long]$stamp.secretsLength) {
            return @{ Full = $true; Since = $null; Reason = 'full scan (secrets.json changed since the last scan)' }
        }
    }
    catch { return @{ Full = $true; Since = $null; Reason = 'full scan (the stamp could not be read)' } }
    if (($Now.ToUniversalTime() - $started).TotalDays -gt 7) { return @{ Full = $true; Since = $null; Reason = 'full scan (the last scan is over 7 days old)' } }
    return @{ Full = $false; Since = $started.AddMinutes(-2); Reason = "incremental since $($started.ToString('yyyy-MM-dd HH:mm:ss'))Z" }
}

function Write-ScanStamp {
    <# After a clean scan (0 hits, nothing unreadable): the stamp Get-ScanPlan reads. Timestamps and file facts only; a temp file, then a move. #>
    param([string] $StampPath, [string] $SecretsPath, [datetime] $Started)
    $info = Get-Item -LiteralPath $SecretsPath
    $json = [ordered]@{ scanStartedTicks = $Started.ToUniversalTime().Ticks; secretsMtimeTicks = $info.LastWriteTimeUtc.Ticks; secretsLength = $info.Length } | ConvertTo-Json
    $temp = "$StampPath.tmp"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $StampPath) | Out-Null
    Set-Content -LiteralPath $temp -Value $json -Encoding utf8
    Move-Item -LiteralPath $temp -Destination $StampPath -Force
}

function Measure-SecretLeaks {
    <#
      The secret scan (see the description above). The passwords stay in this function's memory; the result holds
      counts, file names and the secrets.json keys that matched, never a value, and a secrets.json error names only its
      exception type. Returns @{ Hits (-1: the scan could not run); Files; Scanned; Secrets; Unreadable; Note }.
    #>
    $scanStarted = [DateTime]::UtcNow
    $result = @{ Hits = 0; Files = [System.Collections.Generic.List[string]]::new(); Scanned = 0; Secrets = 0; Skipped = 0; Mode = ''
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
    $stampPath = Join-Path $repoRoot 'cache\smoke-scan.json'
    $plan = Get-ScanPlan -StampPath $stampPath -SecretsPath $secretsPath -Force ([bool]$FullScan) -Now $scanStarted
    $result.Mode = $plan.Reason
    # $bounded: the folders the run does not own (the isolated Godot logs, the session recordings, ACE's logs) are read only for files written since the stamp's start, in an incremental scan.
    $addFiles = {
        param([string] $root, $files, [bool] $bounded = $false)
        foreach ($file in $files) {
            if ($bounded -and -not $plan.Full -and $file.LastWriteTimeUtc -lt $plan.Since) { $result.Skipped++; continue }
            if ($seen.Add($file.FullName)) { $targets.Add(@{ Path = $file.FullName; Name = [IO.Path]::GetRelativePath($root, $file.FullName) }) }
        }
    }
    & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $OutDir -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors)
    foreach ($extra in $ScanDir) {
        # P4-R4: the live gate's whole folder and its dotnet test logs, read whole (not bounded by the stamp).
        if (Test-Path -LiteralPath $extra -PathType Container) { & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $extra -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors) }
        else { $result.Unreadable.Add('a -ScanDir folder does not exist (so it was not scanned)') }
    }
    $godotData = Join-Path $repoRoot 'cache\godot'
    if (Test-Path -LiteralPath $godotData) {
        foreach ($logs in @(Get-ChildItem -LiteralPath $godotData -Recurse -Directory -Force -Filter 'logs' -ErrorAction SilentlyContinue -ErrorVariable +listErrors)) {
            & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $logs.FullName -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors) $true
        }
    }
    $sessions = Join-Path $repoRoot 'cache\sessions'
    if (Test-Path -LiteralPath $sessions) {
        & $addFiles $repoRoot @(Get-ChildItem -LiteralPath $sessions -Recurse -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors) $true
    }
    $aceLogs = Join-Path $runtime 'logs'
    if (Test-Path -LiteralPath $aceLogs) {
        # ACE_Log.txt, ACE_Log-WARN.txt and log4net's rolled copies (ACE_Log.txt.1, ...).
        & $addFiles $mainCheckout @(Get-ChildItem -LiteralPath $aceLogs -File -Force -ErrorAction SilentlyContinue -ErrorVariable +listErrors |
                Where-Object { $_.Name -match '^ACE_Log.*\.txt(\.\d+)?$' }) $true
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
    if ($result.Hits -eq 0 -and $result.Unreadable.Count -eq 0) {
        try { Write-ScanStamp -StampPath $stampPath -SecretsPath $secretsPath -Started $scanStarted }
        catch { $result.Note = "the scan stamp could not be written ($(Get-ExceptionKind $_)): the next scan is a full one" }
    }
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

function Test-BatchSlot {
    <# C8 (cut 9): may another child of the parallel batch start? The first always does (as a sequential run would); a further one only while fewer than $Requested run and fewer than 3 Godot processes run on the machine (other agents' too). #>
    param([int] $Requested, [int] $Running, [int] $Engines)
    if ($Running -eq 0) { return $true }
    return $Running -lt $Requested -and $Engines -lt 3
}

function Start-BatchChild {
    <# One step as a child of this script: -Only <step>, its own OutDir (par\<step>), its own Godot user-data slot, no secret scan, its rows in par\<step>.json. #>
    param([string] $Step, [int] $Number, [string] $Slot)
    $dir = Join-Path $OutDir 'par'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $psi = [System.Diagnostics.ProcessStartInfo]::new($pwshExe)
    $arguments = @('-NoProfile', '-File', $PSCommandPath, '-NoBuild', '-Only', $Step, '-OutDir', (Join-Path $dir $Step), '-StepNumber', "$Number", '-UserDataSlot', $Slot,
        '-NoScan', '-ResultsJson', (Join-Path $dir "$Step.json"), '-Backend', 'auto', '-DotnetRoot', $DotnetRoot) + @(if ($AllSizes) { '-AllSizes' })
    foreach ($a in $arguments) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $repoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    return @{ Step = $Step; Slot = $Slot; Process = $process; Out = $process.StandardOutput.ReadToEndAsync(); Err = $process.StandardError.ReadToEndAsync()
        Deadline = [DateTime]::UtcNow.AddSeconds(1800); Json = (Join-Path $dir "$Step.json"); Console = (Join-Path $dir "$Step.console.log"); Timer = [Diagnostics.Stopwatch]::StartNew() }
}

function Invoke-ParallelBatch {
    <#
      C8 (cut 9): runs $Steps (all of them in $parallelSteps, backend-free, never frame-judged) as children, at most -Parallel at a time, and returns @{ <step> = @{ Rows; Code; Wall } }.
      The batch is fully joined before this returns, so no other step starts while a child runs. A child that wrote no rows is a FAIL row, never a pass.
    #>
    param([string[]] $Steps, [hashtable] $Numbers)
    $queue = [System.Collections.Generic.Queue[string]]::new($Steps)
    $running = [System.Collections.Generic.List[object]]::new()
    $results = @{}
    $wall = [Diagnostics.Stopwatch]::StartNew()
    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        foreach ($child in @($running)) {
            $finished = $child.Process.HasExited
            if (-not $finished -and [DateTime]::UtcNow -gt $child.Deadline) { $child.Process.Kill($true); $child.Process.WaitForExit(); $finished = $true }
            if (-not $finished) { continue }
            $child.Process.WaitForExit()
            $text = @($(if ($child.Out.Wait(30000)) { $child.Out.Result }), $(if ($child.Err.Wait(30000)) { $child.Err.Result })) -join [Environment]::NewLine
            Set-Content -LiteralPath $child.Console -Value $text -Encoding utf8
            $rows = @()
            if (Test-Path -LiteralPath $child.Json) { try { $rows = @(Get-Content -LiteralPath $child.Json -Raw | ConvertFrom-Json) } catch { $rows = @() } }
            $results[$child.Step] = @{ Rows = $rows; Code = $child.Process.ExitCode; Wall = $child.Timer.Elapsed.TotalSeconds }
            $running.Remove($child) | Out-Null
        }
        while ($queue.Count -gt 0 -and (Test-BatchSlot $Parallel $running.Count (Get-EngineProcessCount))) {
            $used = @($running | ForEach-Object { $_.Slot })
            $slot = @('p1', 'p2', 'p3') | Where-Object { $used -notcontains $_ } | Select-Object -First 1
            $step = $queue.Dequeue()
            $running.Add((Start-BatchChild -Step $step -Number $Numbers[$step] -Slot $slot))
        }
        Start-Sleep -Milliseconds 250
    }
    $sum = ($results.Values | ForEach-Object { $_.Wall } | Measure-Object -Sum).Sum
    Write-Host ("[smoke] parallel x{0}: {1} steps, {2:0} s wall (sum {3:0} s)" -f $Parallel, $Steps.Count, $wall.Elapsed.TotalSeconds, $sum)
    return $results
}

function Get-LaneGroups {
    <# M4 P4 gate: the wanted backend steps grouped by test account, in the table's order: an ordered map account -> steps. #>
    param([string[]] $Steps)
    $groups = [ordered]@{}
    foreach ($step in $stepAccounts.Keys) {
        if ($Steps -notcontains $step) { continue }
        $account = $stepAccounts[$step]
        if (-not $groups.Contains($account)) { $groups[$account] = [System.Collections.Generic.List[string]]::new() }
        $groups[$account].Add($step)
    }
    return $groups
}

function Invoke-LaneBatch {
    <#
      -Lanes: runs the wanted backend steps of each test account as one child smoke (its steps one after the other, in the table's order), all the children at once (at most three, one per account), and returns
      @{ <step> = @{ Rows; Code; Wall; Dir } }. Each child has its own OutDir (lanes\lane-<account>), its own Godot user-data slot and -ContendedFrames (frame-time failures with another run up are reported). The batch is
      fully joined before this returns. A child that wrote no rows is a FAIL row for each of its steps, never a pass. Returns an empty map when only one account is wanted (the steps then run here, in turn).
    #>
    param([string[]] $Steps, [hashtable] $Numbers)
    $groups = Get-LaneGroups $Steps
    if ($groups.Count -lt 2) { return @{} }
    # M4 P5b G0 (D39): four test accounts exist, and the throwaway's account (test-tools) runs a Godot step here too; -Lanes keeps its three Godot runs and three slots, so it refuses four groups.
    if ($groups.Count -gt 3) { throw "-Lanes runs at most three accounts' Godot steps at once (this selection spans $($groups.Count): $($groups.Keys -join ', ')); drop login-throwaway or run the steps without -Lanes" }
    $dir = Join-Path $OutDir 'lanes'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $running = [System.Collections.Generic.List[object]]::new()
    $slots = @('l1', 'l2', 'l3')
    $index = 0
    foreach ($account in $groups.Keys) {
        $laneSteps = @($groups[$account])
        $name = "lane-$account"
        $psi = [System.Diagnostics.ProcessStartInfo]::new($pwshExe)
        $arguments = @('-NoProfile', '-File', $PSCommandPath, '-NoBuild', '-Only', ($laneSteps -join ','), '-OutDir', (Join-Path $dir $name), '-StepNumber', "$($Numbers[$laneSteps[0]])",
            '-UserDataSlot', $slots[$index], '-NoScan', '-ResultsJson', (Join-Path $dir "$name.json"), '-Backend', $Backend, '-ContendedFrames', '-DotnetRoot', $DotnetRoot) + @(if ($Skip.Count) { '-Skip'; ($Skip -join ',') })
        foreach ($a in $arguments) { $psi.ArgumentList.Add($a) }
        $psi.UseShellExecute = $false
        $psi.WorkingDirectory = $repoRoot
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($psi)
        Write-Host "[smoke] lane $account (slot $($slots[$index])): $($laneSteps -join ', ')"
        $running.Add(@{ Account = $account; Steps = $laneSteps; Process = $process; Out = $process.StandardOutput.ReadToEndAsync(); Err = $process.StandardError.ReadToEndAsync()
                Json = (Join-Path $dir "$name.json"); Console = (Join-Path $dir "$name.console.log"); Dir = (Join-Path 'lanes' $name); Timer = [Diagnostics.Stopwatch]::StartNew()
                Deadline = [DateTime]::UtcNow.AddSeconds(7200) })
        $index++
    }
    $results = @{}
    $wall = [Diagnostics.Stopwatch]::StartNew()
    while ($running.Count -gt 0) {
        foreach ($child in @($running)) {
            $finished = $child.Process.HasExited
            if (-not $finished -and [DateTime]::UtcNow -gt $child.Deadline) { $child.Process.Kill($true); $child.Process.WaitForExit(); $finished = $true }
            if (-not $finished) { continue }
            $child.Process.WaitForExit()
            $text = @($(if ($child.Out.Wait(30000)) { $child.Out.Result }), $(if ($child.Err.Wait(30000)) { $child.Err.Result })) -join [Environment]::NewLine
            Set-Content -LiteralPath $child.Console -Value $text -Encoding utf8
            $rows = @()
            if (Test-Path -LiteralPath $child.Json) { try { $rows = @(Get-Content -LiteralPath $child.Json -Raw | ConvertFrom-Json) } catch { $rows = @() } }
            foreach ($laneStep in $child.Steps) { $results[$laneStep] = @{ Rows = $rows; Code = $child.Process.ExitCode; Wall = $child.Timer.Elapsed.TotalSeconds; Dir = $child.Dir } }
            Write-Host ("[smoke] lane {0} done in {1:0} s (exit {2})" -f $child.Account, $child.Timer.Elapsed.TotalSeconds, $child.Process.ExitCode)
            $running.Remove($child) | Out-Null
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Host ("[smoke] lanes: {0} accounts at once, {1:0} s wall" -f $groups.Count, $wall.Elapsed.TotalSeconds)
    return $results
}

function Add-ReplayedResult {
    <# The parent's row for a step a child ran: the child's own row (its Log under par\<step>\, or under -Dir for a lane), or a FAIL when the child wrote none. #>
    param([string] $Step, $Child, [double] $Seconds, [string] $Dir = '')
    $row = @($Child.Rows | Where-Object { $_.Step -eq $Step }) | Select-Object -First 1
    if (-not $row) {
        Add-Result $Step $false $Child.Code $Seconds "no result from the parallel child (exit $($Child.Code); par\$Step.console.log)" -NotRun
        return
    }
    $note = if ($row.Result -match '^PASS \((.*)\)$') { $Matches[1] } else { '' }
    $log = if ($row.Log) { Join-Path $(if ($Dir) { $Dir } else { Join-Path 'par' $Step }) $row.Log } else { '' }
    $errors = if ("$($row.Errors)" -ne '') { [int]$row.Errors } else { -1 }
    $exit = if ("$($row.Exit)" -ne '') { [int]$row.Exit } else { 0 }
    if ($row.Outcome -eq 'SKIP') { Add-Result $Step $true $exit ([double]$row.Seconds) $row.Detail $log -Skipped }
    else { Add-Result $Step ($row.Outcome -ne 'FAIL') $exit ([double]$row.Seconds) $row.Detail $log -1 $note }
}

$strayBefore = if (Test-Wanted 'stray-output') { Get-StrayOutputs } else { @() }
$suiteTimer = [System.Diagnostics.Stopwatch]::StartNew()
$suiteStartUtc = [DateTime]::UtcNow
Write-Host "[smoke] $($frameRule.Text); repository $repoRoot; logs and outputs in $OutDir"
$wantedBackendSteps = @($backendSteps | Where-Object { Test-Wanted $_ })
# The step numbers of the table (the loop's rule: build is skipped under -NoBuild, then every wanted step counts), for the -Parallel children.
$stepNumbers = @{}
$counter = if ($StepNumber -gt 0) { $StepNumber - 1 } else { 0 }
foreach ($candidate in $allSteps) {
    if ($candidate -eq 'build' -and $NoBuild) { continue }
    if (-not (Test-Wanted $candidate)) { continue }
    $counter++
    $stepNumbers[$candidate] = $counter
}
$script:batch = $null
$script:laneBatch = $null
$backendAtStart = Test-BackendReady -Accounts (Get-StepAccounts $wantedBackendSteps)
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
        if ($journeySteps.ContainsKey($step) -and $Backend -ne 'require') {
            # A journey logs in as a levelled character and walks the whole route (buffs, fights, loot, vendors, deaths, mod teleports): only a run that demands the backend (the lead's gate) does that. slice-check never runs it.
            Add-Result $step $true 0 $timer.Elapsed.TotalSeconds "it runs the $($journeySteps[$step].Journey) journey on the $($stepAccounts[$step]) account (the whole route, about 20 to 35 minutes); runs only with -Backend require (this is -Backend $Backend)" -Skipped
            continue
        }
        if ($backendSteps -contains $step -and -not (Test-BackendGate $step $timer)) { continue }
        # -Lanes: at the first backend step the accounts' lanes run at once (joined before anything else starts); each backend step then replays its row here, in the table's order.
        if ($Lanes -and $backendSteps -contains $step) {
            if ($null -eq $script:laneBatch) { $script:laneBatch = Invoke-LaneBatch $wantedBackendSteps $stepNumbers }
            if ($script:laneBatch.ContainsKey($step)) { Add-ReplayedResult $step $script:laneBatch[$step] $timer.Elapsed.TotalSeconds $script:laneBatch[$step].Dir; continue }
        }
        # -Parallel: at the first eligible step the whole batch runs (joined before anything else starts); each eligible step then replays its row here, in the table's order.
        if ($Parallel -gt 1 -and $parallelSteps -contains $step) {
            if ($null -eq $script:batch) {
                $eligible = @($allSteps | Where-Object { $parallelSteps -contains $_ -and (Test-Wanted $_) })
                $script:batch = if ($eligible.Count -ge 2) { Invoke-ParallelBatch $eligible $stepNumbers } else { @{} }
            }
            if ($script:batch.ContainsKey($step)) { Add-ReplayedResult $step $script:batch[$step] $timer.Elapsed.TotalSeconds; continue }
        }
        # -SoloFrames: a live step (a frame-judged one that has no shared run) waits for the machine; play-checks and record-audio wait inside their own blocks.
        if ($SoloFrames -and $frameJudgedSteps -contains $step -and $step -notin @('play-checks', 'record-audio') -and -not (Wait-SoloEngine $step $timer)) { continue }
        switch ($step) {
            'build' {
                $log = Join-Path $OutDir ('{0:00}-build.log' -f $script:stepIndex)
                # M4 1.1 s3: the restore is locked first (packages.lock.json beside each project, RestorePackagesWithLockFile in Directory.Build.props): a package graph that differs
                # from the committed lock files fails the build step instead of being rewritten silently. The build then reuses that restore.
                $restoreLog = Join-Path $OutDir ('{0:00}-restore.log' -f $script:stepIndex)
                $restoreCode = Invoke-Logged -FileName $dotnetExe -LogPath $restoreLog -TimeoutSeconds 900 `
                    -Arguments @('restore', (Join-Path $repoRoot 'Lifestone.slnx'), '--locked-mode', '--disable-build-servers', '-nologo')
                if ($restoreCode -ne 0) {
                    Add-Result $step $false $restoreCode $timer.Elapsed.TotalSeconds 'dotnet restore --locked-mode failed (the package graph differs from packages.lock.json, or a package is missing); see the restore log' $restoreLog
                    continue
                }
                $code = Invoke-Logged -FileName $dotnetExe -LogPath $log -TimeoutSeconds 900 `
                    -Arguments @('build', (Join-Path $repoRoot 'Lifestone.slnx'), '--no-restore', '--disable-build-servers', '-nologo')
                $text = Get-Content -LiteralPath $log -Raw
                $warnings = if ($text -match '(\d+) Warning\(s\)') { [int]$Matches[1] } else { -1 }
                $errors = if ($text -match '(\d+) Error\(s\)') { [int]$Matches[1] } else { -1 }
                Add-Result $step ($code -eq 0 -and $warnings -eq 0 -and $errors -eq 0) $code $timer.Elapsed.TotalSeconds "restore=locked warnings=$warnings errors=$errors" $log
            }
            'test' {
                $log = Join-Path $OutDir ('{0:00}-test.log' -f $script:stepIndex)
                # C6: --logger trx into this run's own folder (one trx per assembly, no LogFileName so they do not overwrite each other): the per-test timings, the 20 slowest in <OutDir>	est-times.txt.
                $trxDir = Join-Path $OutDir 'test-results'
                $code = Invoke-Logged -FileName $dotnetExe -LogPath $log -TimeoutSeconds 1800 `
                    -Arguments @('test', (Join-Path $repoRoot 'Lifestone.slnx'), '--disable-build-servers', '--no-build', '-nologo', '--filter', $testFilter, '--logger', 'trx;LogFilePrefix=test', '--results-directory', $trxDir)
                $text = Get-Content -LiteralPath $log -Raw
                $passed = 0; $failed = 0; $skipped = 0
                foreach ($m in [regex]::Matches($text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')) {
                    $failed += [int]$m.Groups[1].Value; $passed += [int]$m.Groups[2].Value; $skipped += [int]$m.Groups[3].Value
                }
                $times = @(Get-TrxTimes $trxDir)
                $script:testTimesLine = Format-SlowestSummary $times 20 3
                if ($times.Count) { Set-Content -LiteralPath (Join-Path $OutDir 'test-times.txt') -Value (Format-SlowestLines $times 20) -Encoding utf8 }
                Add-Result $step ($code -eq 0 -and $failed -eq 0 -and $passed -gt 0) $code $timer.Elapsed.TotalSeconds "passed=$passed failed=$failed skipped=$skipped filter=$testFilter; $($script:testTimesLine) (test-times.txt)" $log
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
                    -Arguments (@('-NoProfile', '-File', $goldenScript, '-NoBuild', '-TimeoutSeconds', '300', '-OutDir', $goldenDir) + @(if ($UserDataSlot) { '-UserDataSlot', $UserDataSlot }))
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
                # C3: with record-audio wanted too, the two steps share one Godot run (--play-checks --record-audio=<wav>, Get-PlayChecksRun); this row judges the checks and every ERROR that is not the recording's.
                $shared = Test-Wanted 'record-audio'
                if ($shared) { $sharedRun = Get-PlayChecksRun $step $timer; $run = $sharedRun.Run; $refused = $sharedRun.Refused }
                elseif (Wait-SoloEngine $step $timer) { $run = Invoke-Godot $step '--play-checks' 420 '' $defaultSize; $refused = $false }
                else { $run = $null; $refused = $true }
                if (-not $refused) {
                    $pass = ([regex]::Matches($run.Text, '\[playcheck\] PASS')).Count
                    $fail = ([regex]::Matches($run.Text, '\[playcheck\] FAIL')).Count
                    $max = if ($run.Text -match 'all play checks hold[^\r\n]*max_frame_ms_after_arrival=([\d.]+)') { " max_frame_ms=$($Matches[1])" } else { '' }
                    $recordingErrors = if ($shared) { Get-RecordingErrorCount $run.Text } else { 0 }
                    $frames = Get-FrameVerdict $run.Text $run.Others ([bool]$SoloFrames) $frameTimeChecks $recordingErrors -Reported $parallelPhase
                    $holds = $run.Text -match 'all play checks hold'
                    $detail = "pass=$pass fail=$fail$max$(if ($shared) { "; shared run with record-audio ($([IO.Path]::GetFileName($run.Log)))" })$($frames.Note)"
                    if ($recordingErrors -gt 0) { $detail += "; the recording half logged $recordingErrors ERROR line(s) (the record-audio row judges them)" }
                    Add-Result $step (($run.Code -eq 0 -and $holds) -or $frames.Contended) $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log $frames.Errors $(if ($frames.Contended) { 'frames=contended' } else { '' })
                }
            }
            'record-audio' {
                # The audio check (M3 1.3 s3; TODO(record-audio-check) closed): the play checks with the game's own sound recorded. No --volume: an automated run is muted (M2 D8),
                # and the recording is taken before the Master bus's level, so it holds the full mix and nothing reaches the speakers; the levels line's muted=true is the proof.
                # C3: with play-checks wanted too this row reads the run the play-checks row started (Get-PlayChecksRun) and judges the recording half; the play checks' own verdict still counts (a run that did not hold fails both rows, each naming its half).
                $shared = Test-Wanted 'play-checks'
                $recordDir = Join-Path $OutDir 'record-audio'
                $wav = Join-Path $recordDir 'record-audio.wav'
                $rel = [IO.Path]::GetRelativePath($repoRoot, $wav).Replace('\', '/')
                if ($shared) { $sharedRun = Get-PlayChecksRun $step $timer; $run = $sharedRun.Run; $refused = $sharedRun.Refused; $owner = $sharedRun.Owner }
                elseif (Wait-SoloEngine $step $timer) { $run = Invoke-Godot $step "--play-checks --record-audio=$rel" 420 '' $defaultSize; $refused = $false; $owner = $step }
                else { $run = $null; $refused = $true; $owner = $step }
                if ($refused) {
                    if ($owner -ne $step) { Add-Result $step $false 0 $timer.Elapsed.TotalSeconds "refused with the shared play-checks run (-SoloFrames: another Godot run was up for 10 minutes)" -NotRun }
                }
                else {
                    $pass = ([regex]::Matches($run.Text, '\[playcheck\] PASS')).Count
                    $fail = ([regex]::Matches($run.Text, '\[playcheck\] FAIL')).Count
                    $frames = Get-FrameVerdict $run.Text $run.Others ([bool]$SoloFrames) $frameTimeChecks 0 -Reported $parallelPhase
                    $holds = ($run.Text -match 'all play checks hold') -or $frames.Contended
                    $levelsPattern = 'audio recording levels file=(?<file>\S+) seconds=(?<seconds>[\d.]+) peak_dbfs=(?<peak>-?[\d.]+) rms_dbfs=(?<rms>-?[\d.]+) active_windows=(?<active>\d+) sources=(?<sources>\S+) windows=(?<windows>\d+)(?: clipped_samples=(?<clipped>\d+))?(?<muted> muted=(?:true|false))?'
                    $hasLevels = $run.Text -match $levelsPattern
                    if ($hasLevels) {
                        $levels = @{}
                        foreach ($name in 'seconds', 'peak', 'rms', 'active', 'sources', 'windows', 'clipped', 'muted') { $levels[$name] = $Matches[$name] }
                        $peak = [double]::Parse($levels.peak, [Globalization.CultureInfo]::InvariantCulture)
                        $active = [int]$levels.active
                        $nonSilent = $peak -gt -60.0 -and $active -ge 1
                        $muted = $levels.muted -eq ' muted=true'
                        $sourcesJudge = Test-RecordedSources $levels.sources
                        $sourcesOk = $sourcesJudge.Ok
                        $detail = "peak_dbfs=$($levels.peak) rms_dbfs=$($levels.rms) active_windows=$($levels.active)/$($levels.windows) seconds=$($levels.seconds) clipped_samples=$(if ($levels.clipped) { $levels.clipped } else { 'n/a' }) muted=$($muted.ToString().ToLowerInvariant()) sources=$($levels.sources) (server>=1 and hooks>=1 judged); play checks pass=$pass fail=$fail"
                        if (-not $nonSilent) { $detail += '; FAIL: the recording is silent (judge: peak above -60 dBFS and at least one active window)' }
                        if (-not $muted) { $detail += '; FAIL: the run was not muted (M2 D8)' }
                        if (-not $sourcesOk) { $detail += "; FAIL: sounded counts: $($sourcesJudge.Detail)" }
                    }
                    else {
                        $nonSilent = $false; $muted = $false; $sourcesOk = $false
                        $detail = "no 'audio recording levels' line (the recording was not written or not read back); play checks pass=$pass fail=$fail"
                    }
                    $fileOk = Test-Path -LiteralPath $wav
                    if (-not $fileOk) { $detail += "; FAIL: $rel does not exist" }
                    if (-not $holds) { $detail += '; FAIL: the play checks half did not all hold (shared run: the play-checks row names the checks)' }
                    if ($shared) { $detail += "; shared run $([IO.Path]::GetFileName($run.Log))" }
                    $detail += $frames.Note
                    # The recording's own ERROR lines fail this row; in an unshared run every ERROR does (as before).
                    $errorsHere = if ($shared) { Get-RecordingErrorCount $run.Text } else { $frames.Errors }
                    Add-Result $step (($run.Code -eq 0 -or $frames.Contended) -and $holds -and $hasLevels -and $nonSilent -and $muted -and $sourcesOk -and $fileOk) $run.Code $timer.Elapsed.TotalSeconds $detail $run.Log $errorsHere $(if ($frames.Contended) { 'frames=contended' } else { '' })
                }
            }
            'shell-frames' {
                # The shell's review frames at two window sizes on the stand-in session (no backend). A frames run is not a timing run (the verdict says frame_time=reported).
                # The ten frames and their judges are unchanged (M4 4.1 fix2b: --play-skip-scenarios is gone; the stand-in has no fight or death scenarios since t1).
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
                $run = Invoke-Godot $step "--account=$($stepAccounts[$step]) --login-check=all" 1100 '' $defaultSize
                $verdict = if ($run.Text -match '\[logincheck\] (done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                $live = Resolve-LiveFrames ($run.Code -eq 0 -and $run.Text -match '\[logincheck\] done ok=true ' -and $run.Text -match 'corrections=0 ' -and $run.Text -match 'logoff=ok ' -and $run.Text -match 'logoff_tunnel=held ' -and $run.Text -match 'portal_sounds=ok ') $run
                $fields = Get-FrameFields $null $run
                Add-Result $step $live.Ok $run.Code $timer.Elapsed.TotalSeconds ($verdict + $live.Note) $run.Log $live.Errors $live.PassNote -FrameOver50 $fields.Over50 -FrameJudge $fields.Judge
            }
            'login-throwaway' {
                # --login-check=throwaway (docs/session.md section 28): creates 'Lifestone Throwaway' on the test-tools account (M4 D39) through the retail screens, uses the tools panel, deletes, restores,
                # deletes, purges it from 'Lifestone Smoke' and checks the account's list is as it began. The tools account only; the game reads its password from secrets.json itself.
                # 840 s = the runner's own 600 s limit + 120 s of recovery + start-up and shutdown with margin (docs/session.md section 28).
                $run = Invoke-Godot $step "--account=$($stepAccounts[$step]) --login-check=throwaway" 840 '' $defaultSize
                $verdict = if ($run.Text -match '\[logincheck\] (throwaway done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                $fields = 'throwaway=purged name_errors=2 xp=added where_m=\d+\.\d+ place=teleported deletes=2 restores=1 list=same leftover=(yes|no)'
                $framesOk = $true
                foreach ($key in 'enter_to_walk_max_ms', 'teleport_frame_max_ms') {
                    if ($run.Text -match "\[logincheck\] throwaway done [^\r\n]*$key=(\d+\.\d+)") { if ([double]$Matches[1] -gt 50.0) { $framesOk = $false } } else { $framesOk = $false }
                }
                # M4 P4 gate (D31): in the parallel phase (-Lanes or -ContendedFrames) the two frame figures are REPORTED: when they are the only failure and another Godot run was up, the step passes with
                # frames=contended (D42's rule, as Resolve-LiveFrames gives the other live steps); in a solo run (-Backend require without -ContendedFrames) they are judged as before. The gate judges this leg's teleport frame
                # in its solo phase, three runs, 2 of 3 (tools\live-gate.ps1, the same rule as the frame judge).
                $legOk = $run.Code -eq 0 -and $run.Text -match '\[logincheck\] throwaway done ok=true ' -and $run.Text -match $fields
                $contended = $legOk -and -not $framesOk -and ($run.Others -gt 0 -or $parallelPhase) -and -not $SoloFrames
                $note = if ($contended) { "; frames=contended: enter_to_walk_max_ms or teleport_frame_max_ms over 50 with $($run.Others) other Godot run(s) up$(if ($parallelPhase) { ' (parallel phase: reported, not judged)' }) (reported, not failed)" } else { '' }
                if ($legOk -and -not $framesOk -and -not $contended) { $note = '; enter_to_walk_max_ms or teleport_frame_max_ms over 50 ms (frame-only failure: the gate judges it over three runs, two of three fail)' }
                Add-Result $step ($legOk -and ($framesOk -or $contended)) $run.Code $timer.Elapsed.TotalSeconds ($verdict + $note) $run.Log (Get-ErrorCount $run.Text) $(if ($contended) { 'frames=contended' } else { '' })
            }
            { $journeySteps.ContainsKey($_) } {
                # M4 P5b G1: --login-check=journey --journey=<name> (J5's Godot host over J1's scenario): one login, the stations in order, the journey's own verdict. Replaces login-buff, login-bsd, login-death, login-hunt and login-hunt-missile.
                # The Godot run gets $journeySteps[$step].Seconds (the runner's WholeSecondsFor(plan) plus a margin); the account is the step's ($stepAccounts); the review frames go to <OutDir>/frames (J5: <stem>-station-<name>.png and the in-station frames).
                $journey = $journeySteps[$step]
                $frameStem = [IO.Path]::GetRelativePath($repoRoot, (Join-Path (Join-Path $OutDir 'frames') "$step.png")).Replace([string][char]92, '/')
                New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'frames') | Out-Null
                $run = Invoke-Godot $step "--account=$($stepAccounts[$step]) --login-check=journey --journey=$($journey.Journey) --play-screenshot=$frameStem" $journey.Seconds '' $defaultSize
                # M4 4.2 J2: the window frames (attack, loot, vendor) are judged by the frame rule (over 75 ms fails, over 50 ms flags) unless the run is contended: another Godot run up or a parallel phase, -SoloFrames off (Resolve-LiveFrames' rule).
                $windowsContended = ($run.Others -gt 0 -or $parallelPhase) -and -not $SoloFrames
                $judged = Get-JourneyVerdict $run.Text $windowsContended
                $verdict = $judged.Detail
                $live = Resolve-LiveFrames ($run.Code -eq 0 -and $judged.Ok) $run
                $over50 = if ($judged.FramesSeen -or $judged.WindowsSeen) { if ($judged.FrameMax -gt 50.0 -or $judged.WindowOver50) { 'yes' } else { 'no' } } else { 'n/a' }
                $judge = if ($run.Others -gt 0) { 'contended' } else { 'counted' }
                # Report-only (a new judge: M4 P5b G1): the per-station frame figures, never judged here; -ContendedFrames / -Lanes reports them and the step passes on its journey verdict alone.
                # Judged (M4 4.2 J2): the window frames, shown beside them ('windows: station:field=x ...'; n/a on the headless host or in a log from before J1), judged inside Get-JourneyVerdict.
                Add-Result $step $live.Ok $run.Code $timer.Elapsed.TotalSeconds ($verdict + "; report-only: frame_over_50=$over50 $($judged.Frames); windows (judged over 75 ms unless contended): $(if ($judged.Windows) { $judged.Windows } else { 'n/a' })" + $live.Note) $run.Log $live.Errors $(if ($live.PassNote) { $live.PassNote } elseif ($over50 -eq 'yes') { 'frame over 50 ms' } else { '' }) -FrameOver50 $over50 -FrameJudge $judge
            }
            'login' {
                # The player's own entry point (tools\play.ps1) on the test account (-Offscreen makes it the test account and refuses any other). The backend is the
                # one the gate above found ready (or started with -Backend start): play.ps1 uses a ready backend as it is and stops only what it started itself.
                $log = Join-Path $OutDir ('{0:00}-login.log' -f $script:stepIndex)
                $startedAt = [DateTime]::UtcNow
                $arguments = @('-NoProfile', '-File', $playScript, '-Offscreen', '-IsolateUserData', '-NoBuild', '-TimeoutSeconds', '420', '-Resolution', $defaultSize, '-GameArgs:--login-check=enter')
                $enginesBefore = Get-EngineProcessCount
                $script:enginePeak = 0
                $code = Invoke-Logged -FileName $pwshExe -Arguments $arguments -LogPath $log -TimeoutSeconds 480 -SampleEngines
                $text = Get-Content -LiteralPath $log -Raw
                $loginRun = @{ Code = $code; Text = $text; Others = [math]::Max($enginesBefore, $script:enginePeak - 1) }
                Set-AudioVerdict $step $text
                $verdict = if ($text -match '\[logincheck\] (done ok=[^\r\n]*)') { $Matches[1] } else { 'no verdict line' }
                $frameDir = Join-Path $repoRoot 'docs\screenshots\m2\login'
                # Only frames this run wrote count (the folder keeps the earlier run's until they are overwritten).
                $frames = @(Get-ChildItem -LiteralPath $frameDir -Filter '*.png' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTimeUtc -ge $startedAt }).Count
                $live = Resolve-LiveFrames ($code -eq 0 -and $text -match '\[logincheck\] done ok=true ' -and $text -match 'materialised=true' -and $frames -ge 4 -and $text -match 'shell=ok ' -and $text -match 'spew_portal=yes ' -and $text -match 'logoff_tunnel=held ' -and $text -match 'portal_sounds=ok ') $loginRun
                $fields = Get-FrameFields $null $loginRun
                Add-Result $step $live.Ok $code $timer.Elapsed.TotalSeconds "$verdict; frames=$frames in docs/screenshots/m2/login$($live.Note)" $log $live.Errors $live.PassNote -FrameOver50 $fields.Over50 -FrameJudge $fields.Judge
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

if ($NoScan) { $leaks = @{ Hits = 0; Files = @(); Scanned = 0; Secrets = 0; Skipped = 0; Mode = 'not scanned (a -Parallel child: the parent scans the whole run)'; Unreadable = @(); Note = '' } }
else { try { $leaks = Measure-SecretLeaks }
catch {
    # Only the exception's type: whatever went wrong, no message may carry a password into the summary.
    $leaks = @{ Hits = -1; Files = @(); Scanned = 0; Secrets = 0; Skipped = 0; Mode = ''; Unreadable = @(); Note = "the scan failed ($(Get-ExceptionKind $_))" }
} }
$secretsFailed = $leaks.Hits -ne 0 -or $leaks.Unreadable.Count -gt 0
$secretsLine = if ($leaks.Hits -lt 0) { "secrets=? FAIL: $($leaks.Note)" } else {
    "secrets=$($leaks.Hits)$(if ($secretsFailed) { ' FAIL' })" +
    $(if ($leaks.Note) { " ($($leaks.Note))" } else { " ($($leaks.Secrets) password(s) from secrets.json; $($leaks.Scanned) file(s) scanned in OutDir, cache\godot\**\logs, cache\sessions and server\runtime\logs\ACE_Log*.txt, as UTF-8 and UTF-16LE; $($leaks.Mode)$(if ($leaks.Skipped) { ", $($leaks.Skipped) older file(s) not read" }))" }) +
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
if ($script:testTimesLine) { $lines.Add(''); $lines.Add("tests: $($script:testTimesLine) (test-times.txt, test-results\*.trx)") }
# M4 1.1 s3: the live legs' 50 ms flags, per step (report-only; the worst-of-three rule reads them).
$flagged = @($script:results | Where-Object { $_.FrameOver50 })
if ($flagged.Count) {
    $lines.Add('')
    $lines.Add('frame_over_50 (report-only): ' + (($flagged | ForEach-Object { if ($_.FrameOver50 -eq 'n/a') { "$($_.Step)=n/a" } else { "$($_.Step)=$($_.FrameOver50) ($($_.FrameJudge))" } }) -join ', '))
}
$summary = $lines -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $OutDir 'summary.md') -Value $summary -Encoding utf8
# M4 1.1 s3: summary.json beside summary.md: the commit, the branch, the test filter, per step the verdict and the duration, the run's start and end (UTC).
# No paths, no account names and no detail text (which may carry either); the md has the detail.
if ($StepNumber -eq 0) {
    $commit = ''; $branch = ''
    try { $commit = "$(& git -C $repoRoot rev-parse HEAD 2>$null)".Trim(); $branch = "$(& git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)".Trim() } catch { }
    $json = [ordered]@{
        schema     = 1
        commit     = $commit
        branch     = $branch
        filter     = $testFilter
        backend    = $Backend
        started_utc = $suiteStartUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
        ended_utc  = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        verdict    = $verdict
        secrets    = $leaks.Hits
        scanned    = $leaks.Scanned
        steps      = @($script:results | ForEach-Object {
                $step = [ordered]@{ step = $_.Step; verdict = $_.Result; outcome = $_.Outcome; seconds = $_.Seconds; errors_logged = $(if ("$($_.Errors)" -ne '') { [int]$_.Errors } else { $null }) }
                if ($_.FrameOver50) { $step['frame_over_50'] = $_.FrameOver50; $step['frame_judge'] = $_.FrameJudge }
                $step
            })
    }
    ConvertTo-Json -InputObject $json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'summary.json') -Encoding utf8
}
if ($ResultsJson) { ConvertTo-Json -InputObject @($script:results) -Depth 4 | Set-Content -LiteralPath $ResultsJson -Encoding utf8 }
Write-Host ''
Write-Host $summary
exit $(if ($failedSteps.Count -eq 0 -and -not $secretsFailed) { 0 } else { 1 })
