<#
.SYNOPSIS
    The lead's live gate (M4 P4, D31): the backend from the main checkout, the loopback tests and the live smoke steps in two phases, the backend stopped in a finally.

.DESCRIPTION
    Slices never run this script (it starts the backend and logs in); the item's lead does. Replaces the cache\...\run-gate.ps1 each lead used to copy.

    Default (parallel) mode, by account, with the repeats removed:

      PARALLEL phase   three lanes at once (at most three Godot runs), each lane one after the other inside:
                         test        its loopback classes (Account=test, not the solo tests), then smoke login-buff, login-death (login-throwaway runs in the solo phase's three runs only, P4-R8; login-bsd is in the solo phase, rfix-c)
                         test-melee  its loopback classes (Account=test-melee)
                         test-archer its loopback classes (Account=test-archer), then smoke login-hunt-missile (kills=2, the review frames, the arrow frame among them)
                       The lane smoke steps run with -ContendedFrames: frame-time fields are REPORTED, not judged. A frame-time failure that is the step's only failure, with another
                       Godot run up, is "frames=contended", which smoke counts as a PASS (D42; smoke.ps1 Resolve-LiveFrames and the throwaway step's own rule).
      SOLO phase       one at a time, nothing else running (smoke's -Backend require implies -SoloFrames and waits for other Godot runs):
                         frame judge       smoke -Only login-check,login, three runs
                         melee hunt        smoke -Only login-hunt (test-melee, Holtburg Brawler) once, with its review frames, its frame fields judged
                         den fight         smoke -Only login-bsd (rfix-c: the den fight's walk needs a quiet machine; in the parallel lane it failed "no progress toward goal 12 for 3 s")
                         solo loopback     Category=Loopback ... Lane=solo: HoltburgLaps (with the jumps), AcademyExit (enter to walk), LivePhaseB's two walks
                         throwaway frames  smoke -Only login-throwaway, three runs (its enter-to-walk and teleport frames)
                         leak scan         smoke -Only stray-output (the secret scan over the repository and the gate's output)
      The frame rule of both three-run groups (m4-plan.md section 5, m3-plan.md section 5 "Changed in M3 4.2"): over 75 ms fails, over 50 ms is flagged, two of three runs over 50 ms fail the
      gate. A run whose only failure is a frame window is one flagged run; a failure of any other kind fails the group at once.

    -Serial: the old order exactly, for the comparison on one commit (P4-R10: with LIFESTONE_LOOPBACK_SESSION=per-test, every test logging in and out as before the shared session): the loopback suite (Category=Loopback&Tier!=Soak, the old filter, one process), the frame judge three runs, then the full smoke
    (-Backend require -SoloFrames -FullScan -Parallel 2). -SkipOffline leaves the smoke steps that need no backend, and the test step, out of that last call (the build step stays: the Godot project must be
    built); the parallel mode never runs them.

    No repeats: the offline smoke steps are G-A's (slice-check -Smoke on the same commit); the full smoke's login-check and login are not run again after the frame judge (parallel mode has no full smoke);
    one hunt of each weapon per gate; the loopback suite by area (-Loopback).

    -Loopback full|core|<areas>   full: every non-Soak loopback class (a wave's last item and the milestone end). core (default): a fixed list (tools\lib\GateAreas.psd1 Core) plus the classes of the
                                  areas the diff touches (the changed files mapped like tools\lib\StepPaths.psd1 maps folders to steps; a change to a shared file runs everything and says so). An area
                                  list (net, movement, actions, fighter, mage, servermod) runs those areas' classes only.
    -Soak                         adds Category=Loopback&Tier=Soak once, alone, at the end.
    -Plan (or -WhatIf)            prints every step with its lane and command line and runs nothing (the backend is not started).

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
    [switch] $SkipOffline,
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
    if ($Mode -contains 'full') { return @{ Mode = 'full'; All = $true; Classes = $names; Notes = @('every non-Soak loopback class') } }
    $areaNames = @($Mode | Where-Object { $_ -ne 'core' })
    foreach ($a in $areaNames) { if (-not $Areas.Areas.ContainsKey($a)) { throw "unknown -Loopback value '$a'. Use full, core or areas: $(($Areas.Areas.Keys | Sort-Object) -join ', ')" } }
    if ($Mode -contains 'core') {
        Add-Named @($Areas.Core) 'Core'
        $notes.Add("core: $(@($Areas.Core) -join ', ')")
        $everything = @($Changed | Where-Object { $path = $_; @($Areas.Everything | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
        if ($everything.Count -gt 0) {
            $notes.Add("a shared file changed ($(($everything | Select-Object -First 3) -join ', ')): every loopback class runs")
            return @{ Mode = 'core'; All = $true; Classes = $names; Notes = @($notes) }
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
    <# The dotnet test filter for one lane: $Base plus the class clause when the selection is not everything. Returns $null when the selection has no class for the lane. #>
    param([string] $Base, [hashtable] $Selection, [string[]] $LaneClasses)
    if ($Selection.All) { return $Base }
    $chosen = @($LaneClasses | Where-Object { $Selection.Classes -contains $_ })
    if ($chosen.Count -eq 0) { return $null }
    return $Base + '&(' + (($chosen | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|') + ')'
}

# ---------------------------------------------------------------------------------------------- the plan

function Get-OfflineSteps {
    <# The smoke steps that need no backend, read from tools\smoke.ps1's own lists, for -SkipOffline. #>
    param([string] $Root)
    $text = [IO.File]::ReadAllText((Join-Path $Root 'tools\smoke.ps1'))
    $all = [regex]::Match($text, '\$allSteps = @\(([^)]*)\)', 'Singleline').Groups[1].Value
    $backend = [regex]::Match($text, '\$backendSteps = @\(([^)]*)\)').Groups[1].Value
    $q = { param($s) @([regex]::Matches($s, "'([a-z-]+)'") | ForEach-Object { $_.Groups[1].Value }) }
    $backendList = & $q $backend
    return @(& $q $all | Where-Object { $backendList -notcontains $_ -and $_ -ne 'stray-output' })
}

# P5a F-gate: the account whose character a step logs in ('none': it logs in nobody; '': unknown, so it waits on any killed step). A killed step leaves its character in ACE's world for the release wait.
$script:ReleaseSeconds = 80   # ACE's session timeout (60 s) + the log off, plus 5 s (LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds is 75)
function Get-GateStepAccount {
    param([string] $Lane, [string] $Name)
    if ($Lane -in @('test', 'test-melee', 'test-archer')) { return $Lane }
    if ($Name -eq 'melee-hunt') { return 'test-melee' }
    if ($Name -eq 'login-bsd' -or $Name -like 'frame-judge-*' -or $Name -like 'throwaway-frames-*') { return 'test' }
    if ($Name -in @('build', 'leak-scan')) { return 'none' }
    return ''
}

function New-GateStep {
    <# One plan step: Kind 'test' (dotnet test, judged by its totals) or 'smoke' (tools\smoke.ps1, judged by its rows). Group: steps of one three-run group share it. #>
    param([string] $Phase, [string] $Lane, [string] $Name, [string] $Kind, [string[]] $Arguments, [string] $OutDir = '', [string] $Group = '', [int] $Run = 0, [string] $Exe = 'dotnet', [int] $TimeoutSeconds = 0, [hashtable] $Env = @{})
    if ($TimeoutSeconds -le 0) { throw "step $Lane $Name has no timeout: every gate step gets about twice its expected time (rfix-c)" }
    return [pscustomobject]@{ Phase = $Phase; Env = $Env; Lane = $Lane; Name = $Name; Kind = $Kind; Exe = $Exe; Arguments = @($Arguments); OutDir = $OutDir; Group = $Group; Run = $Run; TimeoutSeconds = $TimeoutSeconds; Account = (Get-GateStepAccount $Lane $Name); ReleaseSeconds = $script:ReleaseSeconds }
}

# Each step's time limit is about twice its expected time (rfix-c, fault 2; never a flat 4 h). The expected times are the g2 run's, in seconds; the smoke legs' own limits (smoke.ps1: buff 900, bsd 1200,
# death 2500, hunt 1500, throwaway 840, check 1100, login 480) stay inside them as the game's deadlines. A step over its limit is killed and is a FAIL naming the step and the limit.
$script:ExpectedSeconds = [ordered]@{
    'build'             = 150    # 15 s warm; a cold restore is longer
    'loopback:test'     = 1400   # g2: 1358 (full)
    'loopback:test-melee' = 800  # g2: 512; p5a-r2: 459 with the Brawler buff failing at 18 s; +700 s buff leg (FighterBuffLiveTests.cs:20) +about 130 s of the working harm-heal, vendor and hunt legs = about 1,250 s
    'loopback:test-archer' = 750 # g2: 192 with a failure; p5a-r2: 253 s done + Appraise about 60 s +700 s buff leg = about 1,015 s (the limit is twice this: 1,500 s)
    'smoke:test'        = 450    # login-buff 64 + login-death 374 (g2)
    'smoke:test-archer' = 150    # login-hunt-missile 83
    'frame-judge'       = 170    # login-check 138 + login 23
    'melee-hunt'        = 140    # g2: 134
    'login-bsd'         = 100    # g2: 96, now solo
    'movement-loopback' = 420    # g2: 415
    'throwaway-frames'  = 60     # g2: 41 to 54
    'leak-scan'         = 60
    'serial-loopback'   = 3600   # estimate: per-test sessions, every class in one process (no run yet; the lead adjusts after the first -Serial gate)
    'serial-full-smoke' = 3600   # estimate: the full smoke -Parallel 2 (no run yet)
    'soak'              = 2700   # the real 1,800 s buff expiry (M3 D26) and the soak run
}

function Get-GatePlan {
    param([string] $Root, [string] $Tag, [string[]] $Mode, [string[]] $Changed, [bool] $Serial, [bool] $Soak, [bool] $SkipOffline, [string] $LogDir)
    $areas = Import-PowerShellDataFile (Join-Path $Root 'tools\lib\GateAreas.psd1')
    $classes = Get-LoopbackClasses $Root
    $selection = Get-LoopbackSelection -Mode $Mode -Classes $classes -Changed $Changed -Areas $areas
    $test = 'tests\Lifestone.Loopback.Tests'
    $out = Join-Path $Root "cache\gate\$Tag"
    $steps = [System.Collections.Generic.List[object]]::new()
    $base = 'Category=Loopback&Tier!=Soak'
    $laneClasses = { param($account, $solo) @($classes | Where-Object { $_.Account -eq $account -and -not $_.Soak -and ($solo -or -not $_.Solo) } | ForEach-Object { $_.Name }) }
    $testArgs = { param($filter) @('test', $test, '--disable-build-servers', '--no-build', '--filter', $filter, '--logger', 'console;verbosity=detailed') }
    $smokeArgs = { param($only, $dir, $extra) @('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Only', $only, '-Backend', 'require', '-NoBuild', '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json'), '-NoScan') + $extra }
    $pwsh = 'pwsh'
    $perTest = @{ LIFESTONE_LOOPBACK_SESSION = 'per-test' }   # -Serial is the old order: every test logs in and out on its own (P4-R10)

    if ($Serial) {
        $filter = Get-LoopbackFilter $base $selection @($classes | Where-Object { -not $_.Soak } | ForEach-Object { $_.Name })
        if ($filter) { $steps.Add((New-GateStep 'SERIAL' 'serial' 'loopback' 'test' @('test', $test, '--disable-build-servers', '--filter', $filter, '--logger', 'console;verbosity=detailed') '' '' 0 'dotnet' (2 * $script:ExpectedSeconds['serial-loopback']) $perTest)) }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "serial-frames-$i"
            $steps.Add((New-GateStep 'SERIAL' 'serial' "frame-judge-$i" 'smoke' @('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Only', 'login-check,login', '-Backend', 'require', '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json'), '-NoScan') $dir 'frame-judge' $i $pwsh (2 * $script:ExpectedSeconds['frame-judge']) $perTest))
        }
        $dir = Join-Path $out 'serial-smoke'
        $skip = if ($SkipOffline) { @('-Skip', ((Get-OfflineSteps $Root | Where-Object { $_ -ne 'build' }) -join ',')) } else { @() }
        $steps.Add((New-GateStep 'SERIAL' 'serial' 'full-smoke' 'smoke' (@('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Backend', 'require', '-SoloFrames', '-FullScan', '-ScanDir', "$out,$LogDir", '-Parallel', '2', '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json')) + $skip) $dir '' 0 $pwsh (2 * $script:ExpectedSeconds['serial-full-smoke']) $perTest))
    }
    else {
        $steps.Add((New-GateStep 'SETUP' 'setup' 'build' 'build' @('build', (Join-Path $Root 'Lifestone.slnx'), '--disable-build-servers', '-nologo') '' '' 0 'dotnet' (2 * $script:ExpectedSeconds['build'])))
        # login-bsd is in the SOLO phase (rfix-c, fault 5): the den fight's walk needs precision a loaded machine cannot give (g2: no progress toward goal 12 for 3 s in the parallel lane).
        $lanes = [ordered]@{ 'test' = @('login-buff', 'login-death'); 'test-melee' = @(); 'test-archer' = @('login-hunt-missile') }
        $slot = 0
        foreach ($account in $lanes.Keys) {
            $slot++
            $filter = Get-LoopbackFilter "$base&Account=$account&Lane!=solo" $selection (& $laneClasses $account $false)
            if ($filter) { $steps.Add((New-GateStep 'PARALLEL' $account 'loopback' 'test' (& $testArgs $filter) '' '' 0 'dotnet' (2 * $script:ExpectedSeconds["loopback:$account"]))) }
            if ($lanes[$account].Count -gt 0) {
                $dir = Join-Path $out "smoke-$account"
                $steps.Add((New-GateStep 'PARALLEL' $account 'smoke' 'smoke' (& $smokeArgs ($lanes[$account] -join ',') $dir @('-ContendedFrames', '-UserDataSlot', "g$slot")) $dir '' 0 $pwsh (2 * $script:ExpectedSeconds["smoke:$account"])))
            }
        }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "frame-judge-$i"
            $steps.Add((New-GateStep 'SOLO' 'solo' "frame-judge-$i" 'smoke' (& $smokeArgs 'login-check,login' $dir @()) $dir 'frame-judge' $i $pwsh (2 * $script:ExpectedSeconds['frame-judge'])))
        }
        $dir = Join-Path $out 'melee-hunt'
        $steps.Add((New-GateStep 'SOLO' 'solo' 'melee-hunt' 'smoke' (& $smokeArgs 'login-hunt' $dir @()) $dir '' 0 $pwsh (2 * $script:ExpectedSeconds['melee-hunt'])))
        $dir = Join-Path $out 'login-bsd'
        $steps.Add((New-GateStep 'SOLO' 'solo' 'login-bsd' 'smoke' (& $smokeArgs 'login-bsd' $dir @()) $dir '' 0 $pwsh (2 * $script:ExpectedSeconds['login-bsd'])))
        $soloNames = @($classes | Where-Object { $_.Solo -or $_.Name -eq 'LivePhaseBTests' } | ForEach-Object { $_.Name })
        $filter = Get-LoopbackFilter "$base&Lane=solo" $selection $soloNames
        if ($filter) { $steps.Add((New-GateStep 'SOLO' 'solo' 'movement-loopback' 'test' (& $testArgs $filter) '' '' 0 'dotnet' (2 * $script:ExpectedSeconds['movement-loopback']))) }
        foreach ($i in 1..3) {
            $dir = Join-Path $out "throwaway-$i"
            $steps.Add((New-GateStep 'SOLO' 'solo' "throwaway-frames-$i" 'smoke' (& $smokeArgs 'login-throwaway' $dir @()) $dir 'throwaway-frames' $i $pwsh (2 * $script:ExpectedSeconds['throwaway-frames'])))
        }
        $dir = Join-Path $out 'leak-scan'
        $steps.Add((New-GateStep 'SOLO' 'solo' 'leak-scan' 'smoke' @('-NoProfile', '-File', (Join-Path $Root 'tools\smoke.ps1'), '-Only', 'stray-output', '-Backend', 'require', '-NoBuild', '-FullScan', '-ScanDir', "$out,$LogDir", '-OutDir', $dir, '-ResultsJson', (Join-Path $dir 'rows.json')) $dir '' 0 $pwsh (2 * $script:ExpectedSeconds['leak-scan'])))   # the whole gate: the sibling smoke folders and the dotnet test logs (P4-R4)
    }
    if ($Soak) {
        $steps.Add((New-GateStep 'SOAK' 'soak' 'soak' 'test' @('test', $test, '--disable-build-servers', '--filter', 'Category=Loopback&Tier=Soak', '--logger', 'console;verbosity=detailed') '' '' 0 'dotnet' (2 * $script:ExpectedSeconds['soak'])))
    }
    return @{ Steps = @($steps); Selection = $selection; Out = $out; Classes = $classes }
}

function Format-Command {
    param($Step)
    $parts = @($Step.Exe) + @($Step.Arguments | ForEach-Object { if ($_ -match '[\s&|()]') { '"' + $_ + '"' } else { $_ } })
    return ($parts -join ' ')
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
$gate = Get-GatePlan -Root $Worktree -Tag $Tag -Mode $Loopback -Changed $(if ($Loopback -contains 'core') { if ($ChangedPaths.Count -gt 0) { $ChangedPaths } else { Get-ChangedFiles $Worktree $Base } } else { @() }) `
    -Serial ([bool]$Serial) -Soak ([bool]$Soak) -SkipOffline ([bool]$SkipOffline) -LogDir $LogDir
$steps = @($gate.Steps)

if ($dry) {
    $phases = @($steps | ForEach-Object { $_.Phase } | Select-Object -Unique)
    Write-Output "[gate-plan] mode=$(if ($Serial) { 'serial' } else { 'parallel' }) loopback=$($gate.Selection.Mode) classes=$(if ($gate.Selection.All) { 'all' } else { $gate.Selection.Classes -join ',' }) soak=$([bool]$Soak) worktree=<worktree> tag=$Tag session=$(if ($Serial) { 'per-test (LIFESTONE_LOOPBACK_SESSION, the old order)' } else { 'shared' })"
    foreach ($n in $gate.Selection.Notes) { Write-Output "[gate-plan] loopback: $n" }
    if (-not $Serial) { Write-Output '[gate-plan] login-bsd runs in the SOLO phase, after the melee hunt (the den fight walks a route that needs a quiet machine; D31: walking precision is solo)' }
    Write-Output '[gate-plan] timeouts: every step about twice its expected time, named per step below; a step over its limit is killed and is a FAIL naming the step and the limit'
    Write-Output "[gate-plan] release: after a step is killed by its timeout the next step on the same account waits up to $($script:ReleaseSeconds) s for ACE to release the character (else it is refused with CharacterError 0x0D)"
    Write-Output '[gate-plan] backend: server\start-server.ps1 from the main checkout before the first step; server\stop-server.ps1 in a finally (not run by -Plan)'
    foreach ($phase in $phases) {
        $inPhase = @($steps | Where-Object { $_.Phase -eq $phase })
        $lanes = @($inPhase | ForEach-Object { $_.Lane } | Select-Object -Unique)
        $how = switch ($phase) { 'PARALLEL' { "$($lanes.Count) lanes at once (at most three Godot), each lane in order" } 'SOLO' { 'one at a time, nothing else running' } 'SERIAL' { 'the old order, one at a time' } 'SETUP' { 'once, before the lanes' } 'SOAK' { 'once, alone, at the end' } default { '' } }
        Write-Output "[gate-plan] phase $phase ($how)"
        $n = 0
        foreach ($s in $inPhase) {
            $n++
            $cmd = (Format-Command $s).Replace($Worktree, '<worktree>').Replace($main, '<main>') + $(if ($s.Env.Count -gt 0) { ' (env ' + (($s.Env.Keys | ForEach-Object { "$_=$($s.Env[$_])" }) -join ' ') + ')' } else { '' })
            Write-Output ("[gate-plan] {0} lane={1} step={2} {3}: {4} (timeout {5} s)" -f $phase, $s.Lane, $n, $s.Name, $cmd, $s.TimeoutSeconds)
        }
    }
    Write-Output "[gate-plan] done steps=$($steps.Count) (nothing was run)"
    exit 0
}

# ---------------------------------------------------------------------------------------------- the run

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Import-Module (Join-Path $Worktree 'tools\lib\GateJudge.psm1') -Force
Import-Module (Join-Path $Worktree 'tools\lib\Toolchain.psm1') -Force
$env:DOTNET_ROOT = Find-LifestoneDotnetRoot -DotnetRoot ''
$env:PATH = "$($env:DOTNET_ROOT);$env:PATH"
$env:LIFESTONE_DOTNET_ROOT = $env:DOTNET_ROOT
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Get-ChildItem -LiteralPath $LogDir -Filter 'killed-*.stamp' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue   # an earlier run's kills do not hold this run
$gateLog = Join-Path $LogDir 'gate.log'

function Write-Gate([string] $Line) {
    Write-Output $Line
    Add-Content -LiteralPath $gateLog -Value $Line -Encoding utf8
}

$all = [System.Collections.Generic.List[object]]::new()
$phaseSeconds = [ordered]@{}
$total = [Diagnostics.Stopwatch]::StartNew()
Write-Gate "[gate] start tag=$Tag mode=$(if ($Serial) { 'serial' } else { 'parallel' }) loopback=$($gate.Selection.Mode) logs=$LogDir"
try {
    Set-Location $main
    $startLog = Join-Path $LogDir 'start-server.log'
    Write-Gate '[gate] backend start-server.ps1'
    & pwsh -NoProfile -File (Join-Path $main 'server\start-server.ps1') *> $startLog
    if ($LASTEXITCODE -ne 0) { throw "server\start-server.ps1 exited $LASTEXITCODE (see $startLog)" }

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
    Write-Gate '[gate] backend stop-server.ps1 (finally)'
    & pwsh -NoProfile -File (Join-Path $main 'server\stop-server.ps1') *> (Join-Path $LogDir 'stop-server.log')
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
Write-Gate ('[gate] done ok={0} {1} total={2:0}s' -f $ok.ToString().ToLowerInvariant(), $phaseText, $total.Elapsed.TotalSeconds)
exit $(if ($ok) { 0 } else { 1 })
