<#
.SYNOPSIS
    The lead's live gate (M4 P4 D31, rebuilt for the journeys in P5b G2): the backend from the main checkout, the loopback tests and the journeys in two phases, the backend stopped in a finally.

.DESCRIPTION
    Slices never run this script (it starts the backend and logs in); the item's lead does. Replaces the cache\...
un-gate.ps1 each lead used to copy.

    Default (parallel) mode, by account (design cache\m4-p5-design.md 3.5):

      PARALLEL phase   four lanes at once (at most three Godot runs: the fourth lane is headless, M4 P5b G0, D39), each lane one after the other inside; each journey lane starts with its
                       re-create step (below, M4 D44, D45):
                         test         recreate (BSD Mage), its short loopback classes (Account=test: LoopbackGate, LivePhaseA and B, Headless, FighterBuild, AcademyExit's quest facts, ModVerbs, ModXp), then smoke journey-mage, then smoke autopilot-mage (M5 item 4 c1, D19: 30 minutes of the autopilot on BSD Mage)
                         test-melee   recreate (Holtburg Brawler), then smoke journey-melee (no loopback class is left on this account: they became the journey's stations)
                         test-archer  recreate (Holtburg Archer), Shortcut's relog class (Account=test-archer), then smoke journey-archer
                         test-tools   its loopback classes (Account=test-tools: Lifestone Throwaway's and the mod verbs'), headless: no Godot step and no re-create step
                       The lane smoke steps run with -ContendedFrames: frame-time fields are REPORTED, not judged. A frame-time failure that is the step's only failure, with another
                       Godot run up, is "frames=contended", which smoke counts as a PASS (D42; smoke.ps1 Resolve-LiveFrames). The classes whose coverage moved into the journeys' stations were deleted (M4 P5b J6). New judges are REPORT-ONLY in their first gate: each journey row carries [report-only ...] with the done line's route_m, rungs, blocked, sightless,
                       vitals_passes, run_s, the melee's granted and the keeper's fields. Judged from M4 4.2 J2 (smoke.ps1 Get-JourneyVerdict): teleports<=1, the mage's recalls>=2, recovery_places=0, the mage's and the archer's
                       granted=0, and the window frames of the hunt, vendor and vendor-facts stations (over 75 ms fails, over 50 ms flags; reported, not judged, in the lanes' contended runs).
                       Judged from M5 item 2's gate (k1): the mage's den-fight station mana_wait_max_s<=10 (a value over 10, or the field missing, fails the journey, named).
                       Judged from M5 item 3's gate (c2b; D44): the mage's den-fight station storeys_visited>=2 and basement_entries=0 (a value under 2 or over 0, or either field missing or n/a, fails the journey, named; contention does not excuse them).
                       Report-only: the den-fight station's returns, slots_standing, legs_recovered, kills_unpaired, slots_expired and loot, looted, kept, loot_value; the mage's logistics station's trigger, granted, places, loot, sold, bought, spent,
                       notes_sold, spent_xp and raises; the death station's deaths, corpse_recovered, route, items_back, vitae_grants, rebuffed, rebuffed_pretrip, relines and trip_abort; the done line's places.
                       Judged from M5 item 4's gate (c1; D55, D38, D56), on the mage journey ONLY (the fighters read them report-only): granted=0 places=0 on its done line and stations; bought>=1 and spent_xp>0 on its logistics line (sold= report-only,
                       D38); deaths=1 corpse_recovered=yes vitae_grants=0 rebuffed=yes on its death line. The autopilot step (smoke autopilot-mage, after the mage's journey) is judged by its done line, ok=yes (Get-AutopilotVerdict) and godot_errors=0;
                       judged from M5 item 7's gate: cycles>=2 (a value under 2, or the field missing, fails the step, named "cycles=<n> (judged >=2)"); report-only: reconnects=0 interrupted=1 resumed=1 deaths>=1 trips>=1 granted=0 places=0 and its frames (contended).
                       Judged from M5 item 7's gate (m5-plan.md section 5 item 3): door_opened=yes on the MELEE journey's vendor and vendor-facts stations (a station that ran with another value, or without the field, fails the journey, named;
                       n/a for a station that did not run fails nothing; stepped_off= and the physics fields stay report-only). Judged from M5 item 7's gate (D36): the offline smoke's -AllSizes runs of the play-checks and the shell frames
                       at 1280x720 and 1920x1080, EXCEPT their frame-time rows (report-only above 800x600: frame_time_rows=<n>); godot_errors=0 at every size.
      SOLO phase       one at a time, nothing else running (smoke's -Backend require implies -SoloFrames and waits for other Godot runs):
                         frame judge       smoke -Only login-check,login, three runs
                         journeys          only the ones named by -JourneySolo <mage|melee|archer>: smoke -Only journey-<name> alone, its station frames judged (not contended)
                         live phase B      Category=Loopback ... Lane=solo, LivePhaseBTests: the two raw-net walks (about 60 s)
                         throwaway frames  smoke -Only login-throwaway, three runs (on test-tools: its enter-to-walk and teleport frames)
                         leak scan         smoke -Only stray-output (the secret scan over the repository and the gate's output)
                       Gone from the old SOLO list (their judged fields come from the journeys' stations): the melee hunt, login-bsd, login-buff, login-death, login-hunt-missile and the solo movement loopback
                       (the laps and the exit are judged solo three times inside login-check=all).
      The frame rule of both three-run groups (m4-plan.md section 5, m3-plan.md section 5 "Changed in M3 4.2"): over 75 ms fails, over 50 ms is flagged, two of three runs over 50 ms fail the
      gate. A run whose only failure is a frame window is one flagged run; a failure of any other kind fails the group at once.

    -Serial: the three journeys one after another HEADLESS (dotnet run --project src/Lifestone.Headless -- --scenario=journey --journey=<name>; each judged by smoke.ps1's Get-JourneyVerdict, the
    same done-line rule as the Godot step), then the frame judge three runs. Each headless journey is preceded by its own step, recreate-<mage|melee|archer>. The same-commit comparison: with
    -CompareTo <tag of a default-mode gate on the same commit> the gate log prints both done lines of each journey side by side (headless, then Godot) and whether the two verdicts agree. The old
    serial loopback suite and the full smoke are gone (the journeys replaced them).

    The re-create step (M4 D44, D45): BSD Mage (account test), Holtburg Brawler (test-melee) and Holtburg Archer (test-archer) are deleted and re-created at the start of every live gate, by their
    idempotent builds, before their journeys; Holtburg Hunter is never re-created. The step is `dotnet run --disable-build-servers --no-build --project src/Lifestone.Headless -- --scenario=fighter-build
    --account=<test-melee|test-archer> --fresh-character` (the mage: `--scenario=mage-build --fresh-character`), run in the worktree. The NEW copy purges its predecessor (the server mod's purge rule),
    so no old copy is left behind. Judge (GateJudge.psm1 Get-RecreateVerdict): PASS = exit 0 and `headless done scenario=<fighter-build|mage-build> ok=1` and a line with `fresh=(recreated|created)`
    and `purged=<n>`; the row names them and the level (`fresh=recreated purged=1 level=55`); a FAIL names why (no done line; ok=0 with the build's own ERROR line; a missing field; the exit code).
    A journey step (the lane's smoke journey, journey-<name>-solo in SOLO, headless-journey-<name> in -Serial) is NOT run when its account's re-create failed in this gate: it gets one FAIL row
    "not run: the re-create on <account> failed" (the lane's other steps still run). With -JourneySolo the re-create stays at the head of its PARALLEL lane and the journey moves to SOLO.

    -JourneySolo <names>          moves those journeys from the PARALLEL phase into the SOLO phase (when a diff touches `fighter` or `mage`, so their station frames are judged, not contended).
    -CompareTo <tag>              -Serial only: the gate folder (a tag under <worktree>\cache\gate and <main>\cache\gate) of a default-mode gate whose journey logs to compare with.
    -KeepCharacters               no re-create step at all: the journeys run on the characters as they are (a rerun of one journey, or a gate that must not touch the characters). The plan and the gate log
                                  say so in one line at the start.

    No repeats: G-A is slice-check -Gate (build and tests, no Godot step, D46) and the offline smoke runs once per item at its end, with -AllSizes from M5 item 6 (D9, D34: slice-check -Changed -AllSizes, or -Smoke, which implies it; the gate runs no Godot step of its own offline); parallel mode has no full smoke; one journey of each character per gate; the loopback suite by area (-Loopback).

    -Loopback full|core|<areas>   full: every non-Soak loopback class (a wave's last item and the milestone end). core (default): a fixed list (tools\lib\GateAreas.psd1 Core) plus the classes of the
                                  areas the diff touches (the changed files mapped like tools\lib\StepPaths.psd1 maps folders to steps; a change to a shared file runs everything and says so). An area
                                  list (net, movement, actions, fighter, mage, servermod) runs those areas' classes only.
    -Soak                         adds Category=Loopback&Tier=Soak once, alone, at the end.
    -Plan (or -WhatIf)            prints every step with its lane and command line and runs nothing (the backend is not started).

    -Steps <names>                M5 item 4 c2 (m5-gate-step-rerun): a later round that reruns only the named steps. Names (comma-separated): build, recreate-<mage|melee|archer>, loopback-<account> (the lane's classes),
                                a loopback class name (LoopbackGateTests: the plan then holds only the named classes, in their lanes), journey-<mage|melee|archer> (the lane's journey, or the solo one under -JourneySolo),
                                journey-<name>-solo, frame-judge (the three runs; frame-judge-1 is one), live-phase-b, throwaway-frames (or -1..-3), leak-scan, soak (implies -Soak), and in -Serial headless-journey-<name>.
                                An unknown name, or one that is not in this plan, fails by name and lists the valid ones. Only the named steps run, and nothing else with them: no build (they run --no-build on the
                                existing build: name `build` to rebuild first), no re-create (name `recreate-<name>` before its journey to start from a fresh character), and the backend is started and stopped only when
                                a named step needs it (a build, or the leak scan alone, does not). `-Plan -Steps journey-mage` prints that one step.
    -Round <n>                    the round's number for the output folder. With -Steps (or -Round) the tag becomes <Tag>-r<n> (n defaults to the first free one from 2 under <worktree>\cache\gate and <main>\cache\gate), so a
                                rerun never overwrites the first round's logs. An explicit -LogDir is used as given.
    -KeepBackend                  M5 item 4 c2 (m5-gate-stops-backend): leaves the backend up after the gate for the lead's reads (the default stops it in the finally, as before). The gate's start and done lines say so
                                (`backend=kept-up`); the lead stops it with server\stop-server.ps1. A backend that failed to start is still stopped.
    -NoDisplayHold                M5 item 4 c2 (m5-gate-slow-machine): skips the display hold. By default the gate first reads `nvidia-smi --query-gpu=display_active --format=csv` (absent nvidia-smi: it says so and goes on) and
                                names a display that is off (Godot renders at about 7 fps then); wakes it with a 1-pixel mouse move and back; and holds it on for the run with a child process calling SetThreadExecutionState(
                                ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED), ended in the finally. No power setting, registry key or scheduled task is changed. The result is `display=` in the start and done lines.
    -ForeignGodotSeconds <s>      M5 item 4 c2 (m5-gate-foreign-godot): before each SOLO Godot step (the frame judge, the solo journeys, the throwaway's frames) a Godot process that is not the gate's is named (pid, start time, game
                                arguments) and waited for up to s seconds (default 120; 0 = fail at once); then the step is a FAIL row `foreign-godot: ...` and is not started. Also passed to smoke as -ForeignGodotSeconds.

    Output: one line per step and per lane, "[gate] <lane> <step> PASS|FAIL <detail> (<seconds>s)", and a last line "[gate] done ok=<bool> parallel=<s>s solo=<s>s total=<s>s". Exit code 0 only when every step
    passed. Logs: -LogDir (default <main checkout>\cache\gate\<Tag>); each smoke call writes its own folder under <worktree>\cache\gate\<Tag>\.

.EXAMPLE
    pwsh -File tools\live-gate.ps1 -Plan -Loopback core

.EXAMPLE
    pwsh -File tools\live-gate.ps1 -Worktree <repo>\.claude\worktrees\m4-x -Tag g1 -Loopback full
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Worktree = '',
    [string] $LogDir = '',
    [string] $Tag = 'gate',
    [string[]] $Loopback = @('core'),
    [switch] $Serial,
    [switch] $Soak,
    [switch] $Plan,
    [switch] $KeepCharacters,
    [Alias('Steps')] [string[]] $OnlySteps = @(),   # -Steps on the command line; the variable is not $Steps because PowerShell names are case-insensitive and the script's plan array is $steps
    [ValidateRange(0, 999)]
    [int] $Round = 0,
    [switch] $KeepBackend,
    [switch] $NoDisplayHold,
    [ValidateRange(0, 3600)]
    [int] $ForeignGodotSeconds = 120,
    [string[]] $JourneySolo = @(),
    [string] $CompareTo = '',
    [string] $Base = 'master',
    [string[]] $ChangedPaths = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Worktree) { $Worktree = Split-Path -Parent $PSScriptRoot }
$Worktree = [IO.Path]::GetFullPath($Worktree)
$Loopback = @($Loopback | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Loopback.Count -eq 0) { $Loopback = @('core') }
$ChangedPaths = @($ChangedPaths | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$JourneySolo = @($JourneySolo | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ } | Select-Object -Unique)
foreach ($j in $JourneySolo) { if (@('mage', 'melee', 'archer') -notcontains $j) { throw "-JourneySolo '$j': use mage, melee or archer" } }
$stepNames = @($OnlySteps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$dry = [bool]($Plan -or $WhatIfPreference)
if ($Tag -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,30}$') { throw "-Tag must be letters, digits, '_', '.' or '-': $Tag" }

# ---------------------------------------------------------------------------------------------- the loopback classes and the selection

function Get-LoopbackClasses {
    <# Reads the loopback test sources: every class in the Loopback collection with its Account trait, its class-level Lane=solo and Tier=Soak, and its file. Throws when a class has no account. #>
    param([string] $Root)
    $folder = Join-Path $Root 'tests\Lifestone.Loopback.Tests'
    $classes = [System.Collections.Generic.List[object]]::new()
    $pattern = '(?<attrs>(?:[ \t]*(?:\[[^\]\r\n]+\]|//[^\r\n]*)[ \t]*\r?\n)+)[ \t]*(?:public|internal)\s+(?:sealed\s+|partial\s+|abstract\s+|static\s+)*class\s+(?<name>\w+)'
    foreach ($file in Get-ChildItem -LiteralPath $folder -Filter '*.cs' -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'LoopbackConventionTests.cs' }) {  # that file's classes are deliberate violations the convention test builds on purpose
        $text = [IO.File]::ReadAllText($file.FullName)
        foreach ($m in [regex]::Matches($text, $pattern)) {
            $attrs = $m.Groups['attrs'].Value
            if ($attrs -notmatch 'Collection\(LoopbackCollection\.Name\)') { continue }
            $account = [regex]::Matches($attrs, 'Trait\("Account",\s*"([^"]+)"\)') | ForEach-Object { $_.Groups[1].Value }
            if (@($account).Count -ne 1) { throw "loopback class $($m.Groups['name'].Value) ($($file.Name)) must carry exactly one [Trait(`"Account`", ...)]" }
            $classes.Add([pscustomobject]@{
                    Name    = $m.Groups['name'].Value
                    Account = @($account)[0]
                    Solo    = ($attrs -match 'Trait\("Lane",\s*"solo"\)')
                    Soak    = ($attrs -match 'Trait\("Tier",\s*"Soak"\)')
                    File    = $file.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
                })
        }
    }
    return @($classes | Sort-Object Name -Unique)
}

function Get-ChangedFiles {
    <# The files the diff touches (read-only git: the merge base with $Base, the working tree and untracked files), repository-relative with forward slashes. #>
    param([string] $Root, [string] $BaseBranch)
    $mergeBase = (& git -C $Root merge-base HEAD $BaseBranch 2>$null)
    if (-not $mergeBase) { throw "no merge base of HEAD and $BaseBranch in $Root (pass -ChangedPaths to name the changed files)" }
    $files = @(& git -C $Root diff --name-only $mergeBase) + @(& git -C $Root ls-files --others --exclude-standard)
    return @($files | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') } | Select-Object -Unique)
}

function Get-LoopbackSelection {
    <#
      Which classes the gate runs. Returns @{ Mode; All; Classes; Notes }: All = every non-Soak class (no class clause in the filter). Notes names why (the areas, the fail-safe).
      -Mode full | core | one or more area names. $Changed are repository-relative paths (core only).
    #>
    param([string[]] $Mode, [object[]] $Classes, [string[]] $Changed, [hashtable] $Areas)
    $names = @($Classes | ForEach-Object { $_.Name })
    $notes = [System.Collections.Generic.List[string]]::new()
    $selected = [System.Collections.Generic.HashSet[string]]::new()
    function Add-Named([string[]] $list, [string] $why) {
        foreach ($n in $list) {
            if ($names -notcontains $n) { throw "GateAreas.psd1 names a class that is not a loopback class: $n ($why)" }
            [void]$selected.Add($n)
        }
    }
    if ($Mode -contains 'full') { return @{ Mode = 'full'; All = $true; Classes = @($names); Notes = @('every non-Soak loopback class') } }
    $areaNames = @($Mode | Where-Object { $_ -ne 'core' })
    foreach ($a in $areaNames) { if (-not $Areas.Areas.ContainsKey($a)) { throw "unknown -Loopback value '$a'. Use full, core or areas: $(($Areas.Areas.Keys | Sort-Object) -join ', ')" } }
    if ($Mode -contains 'core') {
        Add-Named @($Areas.Core) 'Core'
        $notes.Add("core: $(@($Areas.Core) -join ', ')")
        $everything = @($Changed | Where-Object { $path = $_; @($Areas.Everything | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
        if ($everything.Count -gt 0) {
            $notes.Add("a shared file changed ($(($everything | Select-Object -First 3) -join ', ')): every loopback class runs")
            return @{ Mode = 'core'; All = $true; Classes = @($names); Notes = @($notes) }
        }
        foreach ($area in $Areas.Areas.Keys | Sort-Object) {
            $hit = @($Changed | Where-Object { $path = $_; @($Areas.Areas[$area].Paths | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
            if ($hit.Count -gt 0) { $areaNames += $area; $notes.Add("area $area touched by $($hit[0])") }
        }
        foreach ($path in $Changed) {
            foreach ($c in @($Classes | Where-Object { $_.File -eq $path })) { [void]$selected.Add($c.Name); $notes.Add("class $($c.Name) changed") }
        }
    }
    foreach ($area in @($areaNames | Select-Object -Unique)) {
        Add-Named @($Areas.Areas[$area].Classes) "area $area"
        if ($Mode -notcontains 'core') { $notes.Add("area ${area}: $(@($Areas.Areas[$area].Classes) -join ', ')") }
    }
    return @{ Mode = $(if ($Mode -contains 'core') { 'core' } else { 'areas' }); All = $false; Classes = @($selected | Sort-Object); Notes = @($notes) }
}

function Get-LoopbackFilter {
    <#
      The dotnet test filter for one lane: $Base, then the retired classes and facts left out, plus the class clause when the selection is not everything (or $ClassClause is set: a solo step names its class).
      $LaneClasses are the lane's classes without the retired ones; $null when the selection has no class for the lane (so a lane with nothing left plans no step). $LaneClause is the Lane trait term
      (default Lane!=solo); $InLane names solo-trait classes the account lane runs anyway (GateAreas.psd1 InLane): the term becomes (Lane!=solo|FullyQualifiedName~.<class>.).
    #>
    param([string] $Base, [hashtable] $Selection, [string[]] $LaneClasses, [string[]] $Retired = @(), [string[]] $Facts = @(), [string[]] $InLane = @(), [string] $LaneClause = 'Lane!=solo', [switch] $ClassClause)
    $chosen = @($LaneClasses | Where-Object { $_ -and ($Selection.All -or $Selection.Classes -contains $_) })   # a lane with no class passes $null: it has no step
    if ($chosen.Count -eq 0) { return $null }
    $filter = $Base
    if ($LaneClause -eq 'Lane!=solo') {
        $inHere = @($InLane | Where-Object { $chosen -contains $_ })
        $filter += '&' + $(if ($inHere.Count -gt 0) { '(Lane!=solo|' + (($inHere | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|') + ')' } else { 'Lane!=solo' })
    }
    elseif ($LaneClause) { $filter += '&' + $LaneClause }
    foreach ($r in $Retired) { $filter += "&FullyQualifiedName!~.$r." }
    foreach ($f in $Facts) { $filter += "&FullyQualifiedName!~$f" }
    if (-not $Selection.All -or $ClassClause) { $filter += '&(' + (($chosen | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|') + ')' }
    return $filter
}

# ---------------------------------------------------------------------------------------------- the plan

# P5a F-gate: the account whose character a step logs in ('none': it logs in nobody; '': unknown, so it waits on any killed step). A killed step leaves its character in ACE's world for the release wait.
$script:ReleaseSeconds = 80   # ACE's session timeout (60 s) + the log off, plus 5 s (LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds is 75)
$script:GateMaxFps = 0   # M5 item 0 c5 set 60 for the journeys (the gates ran at 239 to 241 fps uncapped, m5-first-dungeon.md 5.8 risk 8); M5 item 1 dropped it (m5-plan.md D28: the archer's forest hunt failed again capped in item 1's round 1, as twice in item 0, and passed uncapped). 0 = no cap; a value over 0 caps the journeys again (smoke -MaxFps)
$script:JourneyAccounts = @{ mage = 'test'; melee = 'test-melee'; archer = 'test-archer' }
function Get-GateStepAccount {
    param([string] $Lane, [string] $Name)
    if ($Lane -in @('test', 'test-melee', 'test-archer', 'test-tools')) { return $Lane }   # M4 fresh G1, D44: a lane's `recreate` step is its lane's account
    if ($Name -match '^(headless-)?journey-(?<j>mage|melee|archer)(-solo)?$') { return $script:JourneyAccounts[$Matches['j']] }   # P5b G2: a journey logs in its own character's account
    if ($Name -eq 'autopilot-mage') { return $script:JourneyAccounts['mage'] }   # M5 item 4 c1 (D19): the autopilot plays BSD Mage, on the mage's account
    if ($Name -match '^recreate-(?<j>mage|melee|archer)$') { return $script:JourneyAccounts[$Matches['j']] }   # M4 fresh G1, D44: -Serial's re-create of that journey's character
    if ($Name -like 'frame-judge-*' -or $Name -eq 'live-phase-b') { return 'test' }
    if ($Name -like 'throwaway-frames-*') { return 'test-tools' }   # D39: Lifestone Throwaway lives on test-tools
    if ($Name -in @('build', 'leak-scan')) { return 'none' }
    return ''
}

function New-GateStep {
    <#
      One plan step: Kind 'test' (dotnet test, judged by its totals), 'smoke' (tools\smoke.ps1, judged by its rows), 'headless' (a headless journey, judged by Get-JourneyVerdict) or 'recreate' (a character's
      delete and re-create, judged by Get-RecreateVerdict; M4 fresh G1, D44). Group: steps of one three-run group share it. Expected: the step's expected seconds (the plan's projection).
      ForeignGodotSeconds: >= 0 on a solo Godot step = wait that long for a foreign Godot, then fail (m5-gate-foreign-godot); -1 = no check. NeedsRecreate: a journey step names the account whose re-create step must have passed ('' = none planned, or -KeepCharacters): the step is not run when that re-create failed (Invoke-GateSteps).
    #>
    param([string] $Phase, [string] $Lane, [string] $Name, [string] $Kind, [string[]] $Arguments, [string] $OutDir = '', [string] $Group = '', [int] $Run = 0, [string] $Exe = 'dotnet', [int] $TimeoutSeconds = 0, [hashtable] $Env = @{}, [int] $Expected = 0, [string] $NeedsRecreate = '', [int] $ForeignGodotSeconds = -1)
    if ($TimeoutSeconds -le 0) { throw "step $Lane $Name has no timeout: every gate step gets about twice its expected time (rfix-c)" }
    return [pscustomobject]@{ Phase = $Phase; Env = $Env; Lane = $Lane; Name = $Name; Kind = $Kind; Exe = $Exe; Arguments = @($Arguments); OutDir = $OutDir; Group = $Group; Run = $Run; TimeoutSeconds = $TimeoutSeconds; Expected = $Expected; Account = (Get-GateStepAccount $Lane $Name); ReleaseSeconds = $script:ReleaseSeconds; NeedsRecreate = $NeedsRecreate; ForeignGodotSeconds = $ForeignGodotSeconds }
}

# Each step's time limit is about twice its expected time (rfix-c, fault 2; never a flat 4 h). A step over its limit is killed and is a FAIL naming the step and the limit. The journeys' numbers are the design's
# (cache\m4-p5-design.md 3.5; P5b G2): estimates until the lead's first live gate, which tunes them. The smoke journeys' own deadlines (smoke.ps1) are the game's, inside or beside the gate's.
$script:ExpectedSeconds = [ordered]@{
    'build'                = 150    # 15 s warm; a cold restore is longer
    'loopback:test'        = 450    # FC-b3: round 1 timed out at 300 s; the lane runs LivePhaseB and HoltburgLaps too (about 7 min)
    'loopback:test-melee'  = 60     # unused while the account has no loopback class; kept so a class added later is not planned without a limit
    'loopback:test-archer' = 70     # Shortcut's relog class
    'loopback:test-tools'  = 200    # FC-b3: round 1 measured 167 s (the throwaway's and the mod verbs' classes)
    'journey-mage'         = 800    # FC-b3: round 1 measured 558 s Godot (lane 858 s less its 300 s loopback); the headless -Serial run uses the same figure. M5 3 c2b: 600 + 150 for the logistics station (Lifestone Recall, two vendors, the experience, Primary Portal Recall) + 50 for the death station's new order (the handler's re-buff, the recall and the corpse runner instead of a place): estimates until the lead's first gate
    'journey-melee'        = 450    # FC-b3: round 1 measured 206 s, ended early at vendor-facts; the serial run averaged 540 s a journey
    'journey-archer'       = 400    # FC-b3: round 1 measured 357 s (lane, with its 70 s class)
    'autopilot-mage'       = 2100   # M5 item 4 c1 (D19): the 30 minutes of the session clock (1,800 s) + the login and the buff, and the trip or death in progress at the minute (up to 600 s) finishing first: an estimate until the lead's first gate with the lane
    'recreate:fighter'     = 90     # M4 fresh G1 (D44): P5a measured about 65 s for a fighter's re-create and build; an estimate until the lead's first fresh gate
    'recreate:mage'        = 120    # M4 fresh G1 (D44): the mage's build trains and learns the most; an estimate until the lead's first fresh gate
    'frame-judge'          = 170    # login-check 138 + login 23 (g2)
    'live-phase-b'         = 60     # the two raw-net walks
    'throwaway-frames'     = 60     # g2: 41 to 54
    'leak-scan'            = 60
    'soak'                 = 2700   # the real 1,800 s buff expiry (M3 D26) and the soak run
}
$script:JourneyNames = @('mage', 'melee', 'archer')
# smoke.ps1's own step timeouts per journey: the gate's limit for a journey step must exceed them (plus the lane's loopback time and a margin), or the gate kills a journey its own deadline would still allow.
$script:SmokeJourneyTimeouts = @{ mage = 2500; melee = 1900; archer = 2900 }
# smoke.ps1's own timeout for the autopilot-mage step ($autopilotSteps; the leg's own limit is 3,360 s = 60 + 300 + 30 x 60 + 1,200): pinned to the table by a test.
$script:SmokeAutopilotSeconds = 3600
function Get-AutopilotLimit {
    # M5 item 4 c1: like a journey's, without the lane's loopback cushion (the step follows the journey in the lane, and each step's limit is its own): twice the expected time, or smoke's own limit plus 300 s, whichever is more.
    return [math]::Max(2 * $script:ExpectedSeconds['autopilot-mage'], $script:SmokeAutopilotSeconds + 300)
}
function Get-JourneyLimit([string] $Journey, [int] $LoopbackExpected) {
    # M4 fresh G1: unchanged. Every step's limit is its own (Invoke-GateSteps waits per step), so the lane's earlier steps never spend a journey's time; the loopback seconds added here are a cushion
    # for the lane's load, not a share of its clock, and the re-create (about 90 to 120 s, its own limit) needs no cushion of its own.
    return [math]::Max(2 * $script:ExpectedSeconds["journey-$Journey"], $script:SmokeJourneyTimeouts[$Journey] + 300 + $LoopbackExpected)
}

function New-RecreateStep {
    <# M4 fresh G1 (D44): the re-create step of one journey's character. The command line is the fixed contract with the headless slice (cache\m4\fresh\facts.md): the mage's build on the default account, the
       fighter build on the Brawler's or the Archer's, both with --fresh-character (the character is deleted through the retail delete and re-created from the template; the new copy purges its predecessor, D45). #>
    param([string] $Phase, [string] $Lane, [string] $Name, [string] $Journey)
    $account = $script:JourneyAccounts[$Journey]
    $fighter = $Journey -ne 'mage'
    $scenario = if ($fighter) { @('--scenario=fighter-build', "--account=$account") } else { @('--scenario=mage-build') }
    $key = if ($fighter) { 'recreate:fighter' } else { 'recreate:mage' }
    return New-GateStep $Phase $Lane $Name 'recreate' (@('run', '--disable-build-servers', '--no-build', '--project', 'src/Lifestone.Headless', '--') + $scenario + @('--fresh-character')) '' '' 0 'dotnet' (2 * $script:ExpectedSeconds[$key]) @{} $script:ExpectedSeconds[$key]
}

function Get-GatePlan {
    param([string] $Root, [string] $Tag, [string[]] $Mode, [string[]] $Changed, [bool] $Serial, [bool] $Soak, [string[]] $Solo, [string] $LogDir, [bool] $Keep = $false, [int] $Foreign = 120, [string[]] $OnlyClasses = @())
    $areas = Import-PowerShellDataFile (Join-Path $Root 'tools\lib\GateAreas.psd1')
    $classes = Get-LoopbackClasses $Root
    $selection = Get-LoopbackSelection -Mode $Mode -Classes $classes -Changed $Changed -Areas $areas
    if ($OnlyClasses.Count -gt 0) { $selection = @{ Mode = 'steps'; All = $false; Classes = @($OnlyClasses | Sort-Object -Unique); Notes = @("-Steps names the class(es) $(@($OnlyClasses | Sort-Object -Unique) -join ', '): only those run, each in its lane") } }   # m5-gate-step-rerun
    $facts = @($areas.RetiredFacts)
    $inLane = @($areas.InLane)
    $test = 'tests\Lifestone.Loopback.Tests'
    $out = Join-Path $Root "cache\gate\$Tag"
    $steps = [System.Collections.Generic.List[object]]::new()
    $base = 'Category=Loopback&Tier!=Soak'
    $laneClasses = { param($account) @($classes | Where-Object { $_.Account -eq $account -and -not $_.Soak -and (-not $_.Solo -or $inLane -contains $_.Name) } | ForEach-Object { $_.Name }) }
    $testArgs = { param($filter) @('test', $test, '--disable-build-servers', '--no-build', '--filter', $filter, '--logger', 'console;verbosity=detailed') }
    $smokeArgs = { param($only, $dir, $extra) @('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Only', $only, '-Backend', 'require', '-NoBuild', '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json'), '-NoScan') + $extra }
    $capped = if ($script:GateMaxFps -gt 0) { @('-MaxFps', "$($script:GateMaxFps)") } else { @() }   # D28: none since M5 item 1; the journeys only (the leak scan runs no Godot, so it takes no cap: M5 0 fix 0-R12); the frame judge and the throwaway's frames stay uncapped (the Holtburg 60 fps floor would fail on the cap itself)
    $pwsh = 'pwsh'
    $expected = $script:ExpectedSeconds
    $steps.Add((New-GateStep 'SETUP' 'setup' 'build' 'build' @('build', (Join-Path $Root 'Lifestone.slnx'), '--disable-build-servers', '-nologo') '' '' 0 'dotnet' (2 * $expected['build']) @{} $expected['build']))

    if ($Serial) {
        # P5b G2: the three journeys one after another, headless (judged by Get-JourneyVerdict, as the Godot step is), then the frame judge; -CompareTo prints both verdicts per journey side by side.
        # M4 fresh G1 (D44): each journey's character is re-created just before its journey (-KeepCharacters: not at all); the journey is not run when that re-create failed.
        foreach ($name in $script:JourneyNames) {
            $needs = ''
            if (-not $Keep) { $steps.Add((New-RecreateStep 'SERIAL' 'serial' "recreate-$name" $name)); $needs = $script:JourneyAccounts[$name] }
            $steps.Add((New-GateStep 'SERIAL' 'serial' "headless-journey-$name" 'headless' @('run', '--disable-build-servers', '--no-build', '--project', 'src/Lifestone.Headless', '--', '--scenario=journey', "--journey=$name") '' '' 0 'dotnet' (2 * $expected["journey-$name"]) @{} $expected["journey-$name"] $needs))
        }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "serial-frames-$i"
            $steps.Add((New-GateStep 'SERIAL' 'serial' "frame-judge-$i" 'smoke' (& $smokeArgs 'login-check,login' $dir @('-ForeignGodotSeconds', "$Foreign")) $dir 'frame-judge' $i $pwsh (2 * $expected['frame-judge']) @{} $expected['frame-judge'] '' $Foreign))
        }
    }
    else {
        # The lanes in account order; a smoke slot per Godot lane (the headless lane has none). A journey named by -JourneySolo leaves its lane for the SOLO phase.
        # M4 fresh G1 (D44): each journey lane starts with its character's re-create (-KeepCharacters: none), also when its journey moved to SOLO; test-tools has none.
        $lanes = [ordered]@{ 'test' = 'mage'; 'test-melee' = 'melee'; 'test-archer' = 'archer'; 'test-tools' = '' }
        $slot = 0
        foreach ($account in $lanes.Keys) {
            $journey = $lanes[$account]
            $needs = ''
            if ($journey -and -not $Keep) { $steps.Add((New-RecreateStep 'PARALLEL' $account 'recreate' $journey)); $needs = $account }
            $filter = Get-LoopbackFilter "$base&Account=$account" $selection (& $laneClasses $account) @() $facts $inLane
            if ($filter) { $steps.Add((New-GateStep 'PARALLEL' $account 'loopback' 'test' (& $testArgs $filter) '' '' 0 'dotnet' (2 * $expected["loopback:$account"]) @{} $expected["loopback:$account"])) }
            if ($journey -and $Solo -notcontains $journey) {
                $slot++
                $dir = Join-Path $out "journey-$journey"
                $steps.Add((New-GateStep 'PARALLEL' $account 'smoke' 'smoke' (& $smokeArgs "journey-$journey" $dir (@('-ContendedFrames', '-UserDataSlot', "g$slot") + $capped)) $dir '' 0 $pwsh (Get-JourneyLimit $journey $(if ($filter) { $expected["loopback:$account"] } else { 0 })) @{} $expected["journey-$journey"] $needs))
            }
            if ($journey -eq 'mage') {
                # M5 item 4 c1 (D19): the autopilot lane, right after the mage's journey on the same account (one character per account in the world), always in the lane (a solo mage journey leaves it, this step does not), judged by its done
                # line through smoke.ps1 (Get-AutopilotVerdict); the same smoke slot as the lane's journey, or a fourth when the journey went solo; the journeys' frame cap. Its frames are report-only: the lane is contended.
                $autoSlot = if ($Solo -notcontains $journey) { "g$slot" } else { 'g4' }
                $autoDir = Join-Path $out 'autopilot-mage'
                $steps.Add((New-GateStep 'PARALLEL' $account 'autopilot-mage' 'smoke' (& $smokeArgs 'autopilot-mage' $autoDir (@('-ContendedFrames', '-UserDataSlot', $autoSlot) + $capped)) $autoDir '' 0 $pwsh (Get-AutopilotLimit) @{} $expected['autopilot-mage'] $needs))
            }
        }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "frame-judge-$i"
            $steps.Add((New-GateStep 'SOLO' 'solo' "frame-judge-$i" 'smoke' (& $smokeArgs 'login-check,login' $dir @('-ForeignGodotSeconds', "$Foreign")) $dir 'frame-judge' $i $pwsh (2 * $expected['frame-judge']) @{} $expected['frame-judge'] '' $Foreign))
        }
        foreach ($journey in $script:JourneyNames) {
            if ($Solo -notcontains $journey) { continue }
            $dir = Join-Path $out "journey-$journey-solo"
            $steps.Add((New-GateStep 'SOLO' 'solo' "journey-$journey-solo" 'smoke' (& $smokeArgs "journey-$journey" $dir ($capped + @('-ForeignGodotSeconds', "$Foreign"))) $dir '' 0 $pwsh (Get-JourneyLimit $journey 0) @{} $expected["journey-$journey"] $(if ($Keep) { '' } else { $script:JourneyAccounts[$journey] }) $Foreign))
        }
        $filter = Get-LoopbackFilter "$base&Lane=solo" $selection @('LivePhaseBTests') @() @() @() '' -ClassClause   # LivePhaseB's two raw-net walks (Lane=solo on the methods)
        if ($filter) { $steps.Add((New-GateStep 'SOLO' 'solo' 'live-phase-b' 'test' (& $testArgs $filter) '' '' 0 'dotnet' (2 * $expected['live-phase-b']) @{} $expected['live-phase-b'])) }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "throwaway-$i"
            $steps.Add((New-GateStep 'SOLO' 'solo' "throwaway-frames-$i" 'smoke' (& $smokeArgs 'login-throwaway' $dir @('-ForeignGodotSeconds', "$Foreign")) $dir 'throwaway-frames' $i $pwsh (2 * $expected['throwaway-frames']) @{} $expected['throwaway-frames'] '' $Foreign))
        }
    }
    # The leak scan closes both modes (P5b G2: -Serial lost its full smoke, which used to carry it).
    $leakPhase = if ($Serial) { 'SERIAL' } else { 'SOLO' }; $leakLane = if ($Serial) { 'serial' } else { 'solo' }
    $dir = Join-Path $out 'leak-scan'
    $steps.Add((New-GateStep $leakPhase $leakLane 'leak-scan' 'smoke' @('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Only', 'stray-output', '-Backend', 'require', '-NoBuild', '-FullScan', '-ScanDir', "$out,$LogDir", '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json')) $dir '' 0 $pwsh (2 * $expected['leak-scan']) @{} $expected['leak-scan']))   # the whole gate: the sibling smoke folders and the dotnet test logs (P4-R4)
    if ($Soak) {
        $steps.Add((New-GateStep 'SOAK' 'soak' 'soak' 'test' @('test', $test, '--disable-build-servers', '--filter', 'Category=Loopback&Tier=Soak', '--logger', 'console;verbosity=detailed') '' '' 0 'dotnet' (2 * $expected['soak']) @{} $expected['soak']))
    }
    return @{ Steps = @($steps); Selection = $selection; Out = $out; Classes = $classes }
}

function Get-GateProjection {
    <# The plan's time projection from the steps' expected seconds: PARALLEL = the slowest lane, SOLO and SERIAL = the sum; the SETUP build and a Soak run are not in the headline total. Returns @{ Lanes; Parallel; Solo; Serial; Total; Bound }. #>
    param([object[]] $Steps)
    $lanes = [ordered]@{}
    foreach ($s in @($Steps | Where-Object { $_.Phase -eq 'PARALLEL' })) {
        $so = if ($lanes.Contains($s.Lane)) { [int]$lanes[$s.Lane] } else { 0 }
        $lanes[$s.Lane] = $so + [int]$s.Expected
    }
    $parallel = 0; $bound = ''
    foreach ($k in $lanes.Keys) { if ($lanes[$k] -gt $parallel) { $parallel = $lanes[$k]; $bound = $k } }
    $solo = 0; $serial = 0
    foreach ($s in @($Steps)) { if ($s.Phase -eq 'SOLO') { $solo += [int]$s.Expected } elseif ($s.Phase -eq 'SERIAL') { $serial += [int]$s.Expected } }
    return @{ Lanes = $lanes; Parallel = $parallel; Solo = $solo; Serial = $serial; Total = $parallel + $solo + $serial; Bound = $bound }
}

function Format-Command {
    param($Step)
    $parts = @($Step.Exe) + @($Step.Arguments | ForEach-Object { if ($_ -match '[\s&|()]') { '"' + $_ + '"' } else { $_ } })
    return ($parts -join ' ')
}

# ---------------------------------------------------------------------------------------------- -Steps: a later round reruns only the named steps (m5-gate-step-rerun)

function Get-GateStepKey {
    <# The name -Steps uses for a plan step: build, recreate-<journey>, loopback-<account>, journey-<name> (the lane's smoke), journey-<name>-solo, frame-judge-<n>, live-phase-b, throwaway-frames-<n>, leak-scan, soak, headless-journey-<name>. #>
    param($Step)
    switch ($Step.Kind) {
        'build' { return 'build' }
        'recreate' {
            if ($Step.Phase -eq 'SERIAL') { return $Step.Name }
            $who = @($script:JourneyAccounts.Keys | Where-Object { $script:JourneyAccounts[$_] -eq $Step.Lane })
            return "recreate-$($who[0])"
        }
        'test' { if ($Step.Phase -eq 'PARALLEL') { return "loopback-$($Step.Lane)" } else { return $Step.Name } }
        'smoke' {
            if ($Step.Phase -eq 'PARALLEL') { $args2 = @($Step.Arguments); return [string]$args2[[array]::IndexOf($args2, '-Only') + 1] }
            return $Step.Name
        }
        default { return $Step.Name }
    }
}

function Test-GateStepNamed {
    <# True when -Steps token $Token names the step with key $Key: the key itself; journey-<name> also the solo journey; frame-judge and throwaway-frames the three runs. #>
    param([string] $Key, [string] $Token)
    return ($Key -eq $Token) -or ($Key -eq "$Token-solo") -or ($Token -in @('frame-judge', 'throwaway-frames') -and $Key -like "$Token-*")
}

function Select-GateSteps {
    <# Keeps the plan steps the tokens name (in plan order). A token that names none throws, naming it and listing the valid ones. $ClassNamed: loopback classes were named, so the loopback steps they produced stay. #>
    param([object[]] $Plan, [string[]] $Tokens = @(), [bool] $ClassNamed = $false)
    $keys = @($Plan | ForEach-Object { Get-GateStepKey $_ })
    foreach ($t in $Tokens) {
        if (@($keys | Where-Object { Test-GateStepNamed $_ $t }).Count -eq 0) { throw "-Steps '$t': no such step in this plan. The steps of this plan: $(($keys | Select-Object -Unique) -join ', ') (also frame-judge and throwaway-frames for their three runs, and a loopback class name)" }
    }
    $kept = @($Plan | Where-Object { $key = Get-GateStepKey $_; @($Tokens | Where-Object { Test-GateStepNamed $key $_ }).Count -gt 0 -or ($ClassNamed -and $_.Kind -eq 'test' -and ($_.Phase -eq 'PARALLEL' -or $_.Name -eq 'live-phase-b')) })
    if ($kept.Count -eq 0) { throw "-Steps names no step this plan runs (the named loopback class is in no lane of this plan)" }
    return $kept
}

# ---------------------------------------------------------------------------------------------- the judge (pure)

function Get-GroupVerdict {
    <#
      The three-run rule (m4-plan.md section 5): $Runs is one entry per run, each @{ Ok; FrameOnly; Over75 }. Over 75 ms fails; a run whose only failure is a frame window is flagged; two flagged runs fail the group;
      any other failure fails it at once. Returns @{ Ok; Detail }.
    #>
    param([object[]] $Runs)
    $other = @($Runs | Where-Object { -not $_.Ok -and -not $_.FrameOnly })
    $over75 = @($Runs | Where-Object { $_.Over75 })
    $flagged = @($Runs | Where-Object { -not $_.Ok -and $_.FrameOnly })
    if ($other.Count -gt 0) { return @{ Ok = $false; Detail = "$($other.Count) of $($Runs.Count) run(s) failed for a reason other than a frame window" } }
    if ($over75.Count -gt 0) { return @{ Ok = $false; Detail = "$($over75.Count) run(s) over 75 ms (fails)" } }
    if ($flagged.Count -ge 2) { return @{ Ok = $false; Detail = "$($flagged.Count) of $($Runs.Count) runs over 50 ms (two of three fail the gate)" } }
    if ($flagged.Count -eq 1) { return @{ Ok = $true; Detail = "one run over 50 ms: flagged, not failed ($($Runs.Count - 1) of $($Runs.Count) clean)" } }
    return @{ Ok = $true; Detail = "$($Runs.Count) of $($Runs.Count) runs clean" }
}

# ---------------------------------------------------------------------------------------------- the plan is printed, or the gate runs

$commonDir = (& git -C $Worktree rev-parse --path-format=absolute --git-common-dir).Trim()
$main = Split-Path -Parent $commonDir
if (-not $LogDir) { $LogDir = Join-Path $main "cache\gate\$Tag" }
$LogDir = [IO.Path]::GetFullPath($LogDir)
if ($Serial -and $JourneySolo.Count -gt 0) { throw '-JourneySolo applies to the default (parallel) gate: -Serial runs every journey headless, one after another' }
if ($CompareTo -and -not $Serial) { throw '-CompareTo applies to -Serial: it names the default-mode gate whose Godot journey verdicts the headless ones are compared with' }
if ($CompareTo -and $CompareTo -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,30}$') { throw "-CompareTo must be a gate tag (letters, digits, '_', '.' or '-'): $CompareTo" }
# m5-gate-step-rerun: a later round's folder is <Tag>-r<n>, so a rerun never overwrites the first round's logs.
if ($stepNames.Count -gt 0 -or $Round -gt 0) {
    if ($Round -le 0) { $Round = 2; while ((Test-Path -LiteralPath (Join-Path $Worktree "cache\gate\$Tag-r$Round")) -or (Test-Path -LiteralPath (Join-Path $main "cache\gate\$Tag-r$Round"))) { $Round++ } }
    $Tag = "$Tag-r$Round"
    if ($Tag -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,30}$') { throw "the round's tag $Tag is too long or has a bad character: shorten -Tag" }
    if (-not $PSBoundParameters.ContainsKey('LogDir')) { $LogDir = Join-Path $main "cache\gate\$Tag" }
}
$classTokens = @(); $stepTokens = @($stepNames)
if ($stepNames.Count -gt 0) {
    $known = @(Get-LoopbackClasses $Worktree | Where-Object { -not $_.Soak } | ForEach-Object { $_.Name })
    $classTokens = @($stepNames | Where-Object { $t = $_; @($known | Where-Object { $_ -ieq $t }).Count -gt 0 } | ForEach-Object { $t = $_; @($known | Where-Object { $_ -ieq $t })[0] })
    $stepTokens = @($stepNames | Where-Object { $t = $_; @($classTokens | Where-Object { $_ -ieq $t }).Count -eq 0 })
    if ($stepTokens -contains 'soak') { $Soak = [switch]$true }
}
$gate = Get-GatePlan -Root $Worktree -Tag $Tag -Mode $Loopback -Changed $(if ($Loopback -contains 'core') { if ($ChangedPaths.Count -gt 0) { $ChangedPaths } else { Get-ChangedFiles $Worktree $Base } } else { @() }) `
    -Serial ([bool]$Serial) -Soak ([bool]$Soak) -Solo @($JourneySolo) -LogDir $LogDir -Keep ([bool]$KeepCharacters) -Foreign $ForeignGodotSeconds -OnlyClasses $classTokens
$steps = @($gate.Steps)
if ($stepNames.Count -gt 0) { $steps = @(Select-GateSteps -Plan $steps -Tokens $stepTokens -ClassNamed ($classTokens.Count -gt 0)) }
$needsBackend = @($steps | Where-Object { $_.Kind -ne 'build' -and $_.Name -ne 'leak-scan' }).Count -gt 0   # the build and the leak scan run no login
$needsGodot = @($steps | Where-Object { $_.Kind -eq 'smoke' -and $_.Name -ne 'leak-scan' }).Count -gt 0
$keepLine = '-KeepCharacters: no re-create step; the journeys run on the characters as they are'   # M4 fresh G1 (D45): said once at the start, in the plan and in the gate log

if ($dry) {
    $phases = @($steps | ForEach-Object { $_.Phase } | Select-Object -Unique)
    Write-Output "[gate-plan] mode=$(if ($Serial) { 'serial' } else { 'parallel' }) loopback=$($gate.Selection.Mode) classes=$(if ($gate.Selection.All) { 'all' } else { $gate.Selection.Classes -join ',' }) soak=$([bool]$Soak) worktree=<worktree> tag=$Tag session=$(if ($Serial) { 'n/a (headless journeys, no loopback class)' } else { 'shared' }) journeys=$(if ($Serial) { 'headless, one after another' } else { 'parallel in their lanes' + $(if ($JourneySolo.Count -gt 0) { '; solo: ' + ($JourneySolo -join ',') } else { '' }) })"
    if ($KeepCharacters) { Write-Output "[gate-plan] $keepLine" }
    else { Write-Output "[gate-plan] re-create (M4 D44, D45): $(if ($Serial) { 'each journey is preceded by recreate-<name>' } else { 'each journey lane starts with recreate' }) = dotnet run --project src/Lifestone.Headless -- --scenario=<mage-build|fighter-build> [--account=<test-melee|test-archer>] --fresh-character (the character is deleted and re-created by its idempotent build; the new copy purges its predecessor); a journey whose re-create failed is not run (one FAIL row); test-tools has none; -KeepCharacters skips them" }
    foreach ($n in $gate.Selection.Notes) { Write-Output "[gate-plan] loopback: $n" }
    if ($stepNames.Count -gt 0) {
        Write-Output "[gate-plan] rerun: round=$Round tag=$Tag -Steps $($stepNames -join ',') = $(@($steps | ForEach-Object { Get-GateStepKey $_ }) -join ', '); only these run, nothing with them (no build: they use the existing build, name 'build' to rebuild; no re-create: name 'recreate-<name>' before its journey for a fresh character); the folder is new, a first round's logs are never overwritten"
        if (-not $needsBackend) { Write-Output '[gate-plan] rerun: no named step needs the backend: it is not started and not stopped' }
    }
    if (-not $Serial) {
        Write-Output '[gate-plan] journeys: mage on lane test, melee on lane test-melee, archer on lane test-archer (smoke journey-<name>, -ContendedFrames in the lanes); -JourneySolo <name> moves one into the SOLO phase (judged station frames)'
        Write-Output '[gate-plan] autopilot (M5 item 4 c1, D19): smoke autopilot-mage in lane test after journey-mage (30 minutes of --login-check=autopilot on BSD Mage: a forced trip, the scripted death, the scripted hand-off); judged: the done line ok=yes, cycles>=2 (from M5 item 7''s gate) and godot_errors=0; report-only: reconnects=0 interrupted=1 resumed=1 deaths>=1 trips>=1 granted=0 places=0, its frames'
        Write-Output '[gate-plan] judged from M5 item 7''s gate: autopilot cycles>=2; the melee journey''s vendor and vendor-facts door_opened=yes (n/a for a station that did not run is not a failure; stepped_off= and the physics fields stay report-only); the offline smoke -AllSizes play-checks and shell frames at 1280x720 and 1920x1080 (frame-time rows report-only above 800x600, DoD 12; godot_errors=0 at every size)'
        foreach ($a in @('test', 'test-melee', 'test-archer', 'test-tools')) { if ($stepNames.Count -eq 0 -and @($steps | Where-Object { $_.Phase -eq 'PARALLEL' -and $_.Lane -eq $a -and $_.Name -eq 'loopback' }).Count -eq 0) { Write-Output "[gate-plan] lane ${a}: no loopback class (its classes are the journey's stations, or not selected by -Loopback)" } }
    }
    else { Write-Output "[gate-plan] serial: the three journeys headless (judged by smoke.ps1 Get-JourneyVerdict; each after its re-create unless -KeepCharacters), then the frame judge; $(if ($CompareTo) { "compare with the Godot verdicts of gate tag $CompareTo" } else { 'no -CompareTo: the Godot verdicts are not printed beside them' })" }
    $capText = if ($script:GateMaxFps -gt 0) { "-MaxFps $($script:GateMaxFps) (--max-fps=$($script:GateMaxFps) in the game arguments)" } else { 'none: the journeys run uncapped (D28; item 0 capped them at 60, and the archer''s forest hunt failed capped)' }
    Write-Output "[gate-plan] frame cap (M5 item 0), dropped in M5 item 1 (D28): $capText on the journeys (the parallel phase and any solo journey; the leak scan runs no Godot and carries no cap); UNCAPPED on the frame-judge steps (login-check, login) and the throwaway's frames, which judge frame time and the Holtburg 60 fps floor and would fail on the cap itself; godot_errors=<n> (Godot's own ERROR: and SCRIPT ERROR: lines) is read from smoke's rows and judged 0 on every Godot row (a fact, D19); headless and dotnet-test rows read n/a"
    Write-Output '[gate-plan] timeouts: every step about twice its expected time, named per step below; a step over its limit is killed and is a FAIL naming the step and the limit'
    Write-Output "[gate-plan] release: after a step is killed by its timeout the next step on the same account waits up to $($script:ReleaseSeconds) s for ACE to release the character (else it is refused with CharacterError 0x0D)"
    if ($needsBackend) {
        Write-Output "[gate-plan] backend: server\start-server.ps1 from the main checkout before the first step; $(if ($KeepBackend) { 'server\stop-server.ps1 is NOT run (-KeepBackend: the backend stays up for the lead''s reads; the lead stops it)' } else { 'server\stop-server.ps1 in a finally' }) (not run by -Plan)"
    }
    else { Write-Output '[gate-plan] backend: not needed by the named steps (not started, not stopped)' }
    Write-Output "[gate-plan] display (m5-gate-slow-machine): $(if (-not $needsGodot) { 'no Godot step in this plan: no hold' } elseif ($NoDisplayHold) { 'hold off (-NoDisplayHold)' } else { 'preflight nvidia-smi --query-gpu=display_active --format=csv (absent: said and gone on); a display that is not Enabled is woken by a 1-pixel mouse move and back; a child process holds SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED) for the run and ends in the finally; no setting is changed' }); display= goes into the start and done lines"
    Write-Output "[gate-plan] foreign Godot (m5-gate-foreign-godot): before each solo Godot step a Godot process that is not the gate's is named (pid, start, game arguments) and waited for up to $ForeignGodotSeconds s (-ForeignGodotSeconds), then the step fails as foreign-godot; smoke gets the same bound"
    foreach ($phase in $phases) {
        $inPhase = @($steps | Where-Object { $_.Phase -eq $phase })
        $lanes = @($inPhase | ForEach-Object { $_.Lane } | Select-Object -Unique)
        $how = switch ($phase) { 'PARALLEL' { "$($lanes.Count) lanes at once (at most three Godot), each lane in order" } 'SOLO' { 'one at a time, nothing else running' } 'SERIAL' { 'each journey headless after its re-create, one after another, then the frame judge' } 'SETUP' { 'once, before the lanes' } 'SOAK' { 'once, alone, at the end' } default { '' } }
        Write-Output "[gate-plan] phase $phase ($how)"
        $n = 0
        foreach ($s in $inPhase) {
            $n++
            $cmd = (Format-Command $s).Replace($Worktree, '<worktree>').Replace($main, '<main>') + $(if ($s.Env.Count -gt 0) { ' (env ' + (($s.Env.Keys | ForEach-Object { "$_=$($s.Env[$_])" }) -join ' ') + ')' } else { '' })
            Write-Output ("[gate-plan] {0} lane={1} step={2} {3}: {4} (timeout {5} s)" -f $phase, $s.Lane, $n, $s.Name, $cmd, $s.TimeoutSeconds)
        }
    }
    $proj = Get-GateProjection $steps
    Write-Output ("[gate-plan] projected (expected seconds, the SETUP build and a Soak run not counted): " + $(if ($Serial) { "serial=$($proj.Serial) s" } else { 'parallel=' + $proj.Parallel + ' s (lane ' + $proj.Bound + ' bounds it; ' + (($proj.Lanes.Keys | ForEach-Object { "$_=$($proj.Lanes[$_])" }) -join ' ') + ') solo=' + $proj.Solo + ' s' }) + " total=$($proj.Total) s about $([math]::Round($proj.Total / 60)) min")
    Write-Output "[gate-plan] done steps=$($steps.Count) (nothing was run)"
    exit 0
}

# ---------------------------------------------------------------------------------------------- the run

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Import-Module (Join-Path $Worktree 'tools\lib\GateJudge.psm1') -Force
Import-Module (Join-Path $Worktree 'tools\lib\Toolchain.psm1') -Force
Import-Module (Join-Path $Worktree 'tools\lib\GateHost.psm1') -Force
$env:DOTNET_ROOT = Find-LifestoneDotnetRoot -DotnetRoot ''
$env:PATH = "$($env:DOTNET_ROOT);$env:PATH"
$env:LIFESTONE_DOTNET_ROOT = $env:DOTNET_ROOT
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Get-ChildItem -LiteralPath $LogDir -Filter 'killed-*.stamp' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue   # an earlier run's kills do not hold this run
Get-ChildItem -LiteralPath $LogDir -Filter 'recreate-failed-*.stamp' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue   # nor an earlier run's failed re-creates (M4 fresh G1)
$gateLog = Join-Path $LogDir 'gate.log'

function Write-Gate([string] $Line) {
    Write-Output $Line
    Add-Content -LiteralPath $gateLog -Value $Line -Encoding utf8
}

$all = [System.Collections.Generic.List[object]]::new()
$phaseSeconds = [ordered]@{}
$total = [Diagnostics.Stopwatch]::StartNew()
$headCommit = (& git -C $Worktree rev-parse --short=10 HEAD 2>$null)
Write-Gate "[gate] start tag=$Tag commit=$headCommit mode=$(if ($Serial) { 'serial' } else { 'parallel' }) loopback=$($gate.Selection.Mode) logs=$LogDir"
if ($KeepCharacters) { Write-Gate "[gate] $keepLine" }
if ($stepNames.Count -gt 0) { Write-Gate "[gate] rerun round=$Round -Steps $($stepNames -join ',') = $(@($steps | ForEach-Object { Get-GateStepKey $_ }) -join ', ') (only these steps; no build or re-create unless named)" }
if ($KeepBackend) { Write-Gate '[gate] -KeepBackend: the backend is left up after the gate (the lead stops it with server\stop-server.ps1)' }

# m5-gate-slow-machine: the preflight (a display that is off makes Godot render at about 7 fps) and the gate's own display hold, ended in the finally. No setting is changed.
$displayBefore = Get-GateDisplayState
$displayAfter = $null; $displayHold = $null; $displaySkip = ''
if (-not $needsGodot) { $displaySkip = 'no Godot step in this run' } elseif ($NoDisplayHold) { $displaySkip = '-NoDisplayHold' }
Write-Gate "[gate] display preflight: $($displayBefore.Detail)"
if (-not $displaySkip) {
    if ($displayBefore.State -ne 'Enabled') { [void](Invoke-GateDisplayWake) }
    $displayHold = Start-GateDisplayHold -Folder $LogDir
    if ($displayBefore.State -ne 'Enabled') { Start-Sleep -Seconds 3; $displayAfter = Get-GateDisplayState }
    if ($displayBefore.State -eq 'Disabled') { Write-Gate "[gate] display was OFF: woken by a 1-pixel mouse move and back, now $($displayAfter.State)$(if ($displayAfter.State -eq 'Disabled') { '; Godot will render slowly (about 7 fps): frame and walk fields are not trustworthy' })" }
}
$displayLine = Get-GateDisplayLine -Before $displayBefore -After $displayAfter -Held ([bool]$displayHold) -Skipped ([bool]$displaySkip) -Why $displaySkip
Write-Gate "[gate] display=$displayLine"
$backendState = if ($needsBackend) { 'stopped' } else { 'not-started' }
$backendUp = $false
try {
    Set-Location $main
    if ($needsBackend) {
        $startLog = Join-Path $LogDir 'start-server.log'
        Write-Gate '[gate] backend start-server.ps1'
        & pwsh -NoProfile -File (Join-Path $main 'server\start-server.ps1') *> $startLog
        if ($LASTEXITCODE -ne 0) { throw "server\start-server.ps1 exited $LASTEXITCODE (see $startLog)" }
        $backendUp = $true
    }
    else { Write-Gate '[gate] backend not needed by the named steps: not started' }

    foreach ($phase in @($steps | ForEach-Object { $_.Phase } | Select-Object -Unique)) {
        $inPhase = @($steps | Where-Object { $_.Phase -eq $phase })
        $timer = [Diagnostics.Stopwatch]::StartNew()
        if ($phase -eq 'PARALLEL') {
            $jobs = @()
            foreach ($lane in @($inPhase | ForEach-Object { $_.Lane } | Select-Object -Unique)) {
                $laneSteps = @($inPhase | Where-Object { $_.Lane -eq $lane })
                $jobs += @{ Job = (Start-GateLaneJob -Lane $lane -Steps $laneSteps -Root $Worktree -LogDir $LogDir -GateLog $gateLog); Steps = $laneSteps }
            }
            foreach ($j in $jobs) {
                foreach ($r in @(Receive-GateLaneJob -Job $j.Job -Steps $j.Steps)) { $all.Add($r); Write-Output $r.Line }
            }
        }
        else {
            foreach ($step in $inPhase) {
                $out = @(Invoke-GateSteps -LaneSteps @($step) -Root $Worktree -LogDir $LogDir -GateLog $gateLog | Where-Object { $_ -is [psobject] -and $_.PSObject.Properties['Lane'] })
                foreach ($r in $out) { $all.Add($r); Write-Output $r.Line }
                # A failed build stops the gate: nothing after it is meaningful.
                if ($phase -eq 'SETUP' -and (@($out).Count -eq 0 -or -not @($out)[0].Ok)) { throw 'the setup build failed' }
            }
        }
        $phaseSeconds[$phase] = $timer.Elapsed.TotalSeconds
    }
}
catch {
    Write-Gate "[gate] ERROR $($_.Exception.Message)"
    $all.Add([pscustomobject]@{ Lane = 'gate'; Step = 'run'; Ok = $false; Detail = $_.Exception.Message; Seconds = 0; Group = ''; Run = 0; FrameOnly = $false; Over75 = $false; Line = '' })
}
finally {
    Set-Location $main
    if ($needsBackend -and $KeepBackend -and $backendUp) {
        $backendState = 'kept-up'
        Write-Gate '[gate] backend LEFT UP (-KeepBackend): stop it with server\stop-server.ps1 when the reads are done'
    }
    elseif ($needsBackend) {
        Write-Gate '[gate] backend stop-server.ps1 (finally)'
        & pwsh -NoProfile -File (Join-Path $main 'server\stop-server.ps1') *> (Join-Path $LogDir 'stop-server.log')
    }
    Stop-GateDisplayHold $displayHold
}

# P5b G2: -Serial's same-commit comparison: each headless journey's done line beside the Godot one of the default-mode gate named by -CompareTo (both printed in the gate log; a disagreement is reported, not hidden).
if ($Serial) {
    $compareDirs = @(); $godotCommit = ''
    if ($CompareTo) {
        $compareDirs = @((Join-Path $Worktree "cache\gate\$CompareTo"), (Join-Path $main "cache\gate\$CompareTo") | Where-Object { Test-Path -LiteralPath $_ })
        $godotLog = Join-Path (Join-Path $main "cache\gate\$CompareTo") 'gate.log'
        if (Test-Path -LiteralPath $godotLog) { $cm = [regex]::Match((Get-Content -LiteralPath $godotLog -TotalCount 1), 'commit=(\w+)'); if ($cm.Success) { $godotCommit = $cm.Groups[1].Value } }
        if ($compareDirs.Count -eq 0) { Write-Gate "[gate] compare: no gate folder for tag $CompareTo under <worktree>\cache\gate or <main>\cache\gate: nothing to compare with" }
    }
    else { Write-Gate '[gate] compare: no -CompareTo <tag of a default-mode gate on this commit>: the headless verdicts stand alone' }
    foreach ($name in $script:JourneyNames) {
        $hLog = Join-Path $LogDir "serial-headless-journey-$name.log"
        $hText = if (Test-Path -LiteralPath $hLog) { Get-Content -LiteralPath $hLog -Raw } else { '' }
        $gText = ''
        foreach ($d in $compareDirs) {
            foreach ($f in @(Get-ChildItem -LiteralPath $d -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.log', '.txt') })) {
                $c = Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue
                if ($c -and $c -match "journey done ok=\S+ name=${name}(\s|$)") { $gText = $c; break }
            }
            if ($gText) { break }
        }
        foreach ($line in @(Get-JourneyComparison -Name $name -HeadlessText $hText -GodotText $gText -Commit $headCommit -GodotCommit $godotCommit)) { Write-Gate $line }
    }
}

# P4-R5: the gate is never ok with a planned step that reported nothing (a lane that died, or a phase the run never reached).
foreach ($r in @(Get-MissingGateRows -Steps $steps -Rows @($all | Where-Object { $_.PSObject.Properties['PlanName'] }) -Why 'the step never reported (the run stopped before it, or its lane died)')) { $all.Add($r); Write-Gate $r.Line }

# The three-run groups: judged by the frame rule, in place of their runs' own verdicts.
$groupFailed = $false
foreach ($group in @($all | Where-Object { $_.Group } | ForEach-Object { $_.Group } | Select-Object -Unique)) {
    foreach ($g in @($all | Where-Object { $_.Group -eq $group } | Group-Object { $_.Step -replace '^[^:]*:', '' })) {
        $verdict = Get-GroupVerdict @($g.Group | Sort-Object Run | ForEach-Object { @{ Ok = $_.Ok; FrameOnly = $_.FrameOnly; Over75 = $_.Over75 } })
        if (-not $verdict.Ok) { $groupFailed = $true }
        Write-Gate ('[gate] solo {0} {1} {2} (three-run rule, m4-plan.md section 5) ({3:0}s)' -f $g.Name, $(if ($verdict.Ok) { 'PASS' } else { 'FAIL' }), $verdict.Detail, (($g.Group | Measure-Object Seconds -Sum).Sum))
    }
}
# A run in a three-run group is judged by its group line above; every other failure counts here.
$failedRows = @($all | Where-Object { -not $_.Ok -and -not $_.Group })
$ok = ($failedRows.Count -eq 0) -and -not $groupFailed
foreach ($lane in @($all | ForEach-Object { $_.Lane } | Select-Object -Unique)) {
    $rows = @($all | Where-Object { $_.Lane -eq $lane })
    $bad = @($rows | Where-Object { -not $_.Ok -and -not $_.Group })
    Write-Gate ('[gate] lane {0} {1} steps={2} failed={3} ({4:0}s)' -f $lane, $(if ($bad.Count -eq 0) { 'PASS' } else { 'FAIL' }), $rows.Count, $bad.Count, (($rows | Measure-Object Seconds -Sum).Sum))
}
$phaseText = ($phaseSeconds.Keys | ForEach-Object { '{0}={1:0}s' -f $_.ToLowerInvariant(), $phaseSeconds[$_] }) -join ' '
Write-Gate ('[gate] done ok={0} {1} total={2:0}s backend={3} display={4}' -f $ok.ToString().ToLowerInvariant(), $phaseText, $total.Elapsed.TotalSeconds, $backendState, $displayLine)
exit $(if ($ok) { 0 } else { 1 })
