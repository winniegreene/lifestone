<#
.SYNOPSIS
    One command that checks a slice: the build, the default tests and the Godot steps a slice names, one line per stage.

.DESCRIPTION
    Prints exactly one line per stage and nothing else on stdout:

      [slicecheck] <stage> PASS|FAIL|SKIP <short detail> (<seconds>s) at_s=<seconds since the start>

    Stages, in order (-Stages picks a subset; the default is build,test,steps, plus exhaustive with -Exhaustive and smoke with -Smoke):

      build       dotnet build Lifestone.slnx --disable-build-servers; detail = warning and error counts (0 and 0 to pass)
      test        dotnet test Lifestone.slnx --no-build --filter "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Status!=KnownFailure"; detail = passed/failed/skipped
                  totals, the passed count of each test assembly and the slowest tests (the VSTest trx logger's per-test timings,
                  top 20 in cache\slice-check\test-times.txt). -MinTests N fails the stage when fewer than N tests passed (the
                  "count never decreases" gate: N is the DEFAULT count; the Exhaustive tier is counted by its own stage). Without
                  -MinTests the floor is the baseline file tests\Shared\test-count-baseline.json (M4 1.1 s3: "default" for this stage, "exhaustive" for the
                  exhaustive stage; the lead writes it at each merge, a slice never lowers it); an explicit -MinTests wins, and the detail names the
                  floor's source ("floor 7968 (baseline file)" or "(-MinTests)"). Tier=Timing (the lead runs it alone) and Status=KnownFailure (never in
                  the green total) are left out of the default filter
      exhaustive  dotnet test Lifestone.slnx --no-build --filter "Tier=Exhaustive": the dat sweeps (every GfxObj, landblock and cell),
                  left out of the default filter because they cost about 55 s of an 85 s run. -Exhaustive adds the stage; -Smoke implies
                  it (G-A); the lead runs it before a merge. Per-test timings as above (exhaustive-times.txt)
      steps       the steps of -Steps (and, with -Changed, the steps the diff touches) through ONE tools\smoke.ps1 process
                  (-Only <steps> -NoBuild -Backend auto), so the Godot command lines live only there; one line per step with the
                  step's own detail and seconds. The name retail-ui-checks is an alias of smoke's retail-ui step. A
                  -Rerun runs the failed steps once more in a second smoke process
      smoke       with -Smoke: the whole of tools\smoke.ps1 -Backend auto -NoBuild -SoloFrames -FullScan; detail = its verdict and
                  total time. Its own test step is the run's test: with this stage the test stage is not run a second time, the
                  `test` line is read from the smoke's test row (and -MinTests judges it)

    -Changed [-Base master]: adds the backend-free steps the diff touches (merge-base with -Base, the working tree and untracked
    files, read-only git; tools\lib\StepPaths.psd1 maps source folders to steps). A path under src/ or tools/ that no rule matches runs
    every backend-free step and is named (never a silent skip); a docs- or tests-only diff runs no Godot step. -Steps stays a floor.
    Paths that only the live steps (login-check, login) notice print one advisory line, "live-probe SKIP touched: ...": slice-check never
    starts the backend.

    Full logs go to cache\slice-check\<stage>.log (gitignored). A FAIL prints at most 15 lines of the failure (failing test
    names and messages, or the step's FAIL lines), then the log path. The last line is "[slicecheck] done ok=true|false"
    and the exit code is 0 or 1. When the build fails the later stages are reported SKIP.

    -Rerun runs a failed Godot step once more (the known 50 ms flakes on a loaded machine); a pass on the rerun is
    reported as "PASS (rerun)". A frame-time failure while another Godot run was up is reported by smoke as
    "frames=contended" (D42) and is a PASS; the smoke stage passes -SoloFrames and fails on any frame check.

    -Parallel N (default 2): smoke runs the backend-free, not frame-judged steps (cli-help, ..., retail-ui, spell-showcase, golden-viewpoints) N at a time.

    Every Godot check and frame run is at retail's native 800x600 (owner rule, 2026-10-14). -AllSizes passes smoke's -AllSizes: the larger sizes
    (1920x1080, 2560x1440) as well; never run by default.

    The backend is never started: -Backend accepts only auto (a step that needs it is skipped by smoke when it is down).
    The dotnet environment (telemetry off, no global tools on PATH, no ASP.NET certificate) is set for this process
    and its children, and the per-user SDK is found as smoke.ps1 does (tools\lib\Toolchain.psm1).

.EXAMPLE
    pwsh -File tools\slice-check.ps1 -MinTests 7900 -Steps play-checks,retail-ui-checks -Rerun

.EXAMPLE
    pwsh -File tools\slice-check.ps1 -Stages build,test

.EXAMPLE
    pwsh -File tools\slice-check.ps1 -Changed -MinTests 7900
#>
[CmdletBinding()]
param(
    [string[]] $Stages = @(),
    [string[]] $Steps = @(),
    [ValidateRange(0, 1000000)]
    [int] $MinTests = 0,
    [switch] $Smoke,
    [switch] $Exhaustive,
    [switch] $Changed,
    [string] $Base = 'master',
    [ValidateRange(1, 3)]
    [int] $Parallel = 2,
    [switch] $Rerun,
    [switch] $AllSizes,
    [ValidateSet('auto')]
    [string] $Backend = 'auto',
    [string] $DotnetRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$smokeScript = Join-Path $PSScriptRoot 'smoke.ps1'
$pwshExe = (Get-Process -Id $PID).Path
$outDir = Join-Path $repoRoot 'cache\slice-check'
$allStages = @('build', 'test', 'exhaustive', 'steps', 'smoke')
# retail-ui-checks is the name slices use for the retail UI layer's checks; smoke runs them inside its retail-ui step.
$stepAliases = @{ 'retail-ui-checks' = 'retail-ui' }
# The default tests leave out the loopback tests (backend), the soak tier (merge gates) and the Exhaustive tier (its own stage).
$testFilter = 'Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Status!=KnownFailure'
$exhaustiveFilter = 'Tier=Exhaustive'
# M4 1.1 s3: the count never decreases. The baseline file is the default floor; an explicit -MinTests wins (and is the only floor of the default count when given).
$baselineFile = Join-Path $repoRoot 'tests\Shared\test-count-baseline.json'
$maxFailureLines = 15
$clock = [Diagnostics.Stopwatch]::StartNew()

# 'pwsh -File' passes '-Steps a,b' as one string: split on commas either way.
function Split-List([string[]] $values) { @($values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
$Stages = @(Split-List $Stages)
$Steps = @(Split-List $Steps)
foreach ($name in $Stages) { if ($allStages -notcontains $name) { throw "Unknown stage '$name'. Stages: $($allStages -join ', ')" } }
if ($Stages.Count -eq 0) { $Stages = @(@("build", "test") + @(if ($Exhaustive -or $Smoke) { "exhaustive" }) + @(if ($Steps.Count -or $Changed) { "steps" }) + @(if ($Smoke) { "smoke" })) }
else {
    if ($Smoke -and $Stages -notcontains 'smoke') { $Stages += 'smoke' }
    if (($Smoke -or $Exhaustive) -and $Stages -notcontains 'exhaustive') { $Stages += 'exhaustive' }
    if ($Changed -and $Stages -notcontains 'steps') { $Stages += 'steps' }
}
foreach ($name in @($Steps | ForEach-Object { if ($stepAliases.ContainsKey($_)) { $stepAliases[$_] } else { $_ } })) {
    if ($name -in @('build', 'test')) { throw "'$name' is a stage, not a step: use -Stages." }
    if ($name -in @('login-check', 'login')) { throw "'$name' needs the backend, which slice-check never starts." }
}
if ($Stages -contains 'steps' -and $Steps.Count -eq 0 -and -not $Changed) { throw "The steps stage needs -Steps <names> (or -Changed)." }

Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\TestTimes.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot
$dotnetExe = Join-Path $DotnetRoot 'dotnet.exe'
# This process only (children inherit it); nothing persistent.
$env:LIFESTONE_DOTNET_ROOT = $DotnetRoot
$env:DOTNET_ROOT = $DotnetRoot
$env:PATH = "$DotnetRoot;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$script:ok = $true

function Read-TestBaseline {
    # {default, exhaustive} from tests\Shared\test-count-baseline.json; a missing or unreadable file is an error, never a silent zero floor.
    if (-not (Test-Path -LiteralPath $baselineFile)) { throw "The test-count baseline file is missing: tests\Shared\test-count-baseline.json (pass -MinTests <n> to override)." }
    $json = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
    foreach ($key in 'default', 'exhaustive') {
        if ($null -eq $json.$key -or [int]$json.$key -lt 0) { throw "tests\Shared\test-count-baseline.json has no '$key' count." }
    }
    return @{ Default = [int]$json.default; Exhaustive = [int]$json.exhaustive }
}
$baseline = Read-TestBaseline
$minTestsSource = if ($PSBoundParameters.ContainsKey('MinTests')) { '-MinTests' } else { 'baseline file' }
if ($minTestsSource -eq 'baseline file') { $MinTests = $baseline.Default }
$exhaustiveMinimum = $baseline.Exhaustive

function Strip-Ansi([string] $text) { [regex]::Replace($text, '\x1b\[[0-9;]*[A-Za-z]', '') }

function Get-LogLines([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path | ForEach-Object { Strip-Ansi $_ })
}

function Write-Stage([string] $stage, [string] $outcome, [string] $detail, [double] $seconds, [string[]] $failure = @(), [string] $log = '') {
    # One line per stage; on a FAIL at most $maxFailureLines lines of the failure, then the log path. at_s: the stage clock, seconds since slice-check started.
    if ($outcome -eq 'FAIL') { $script:ok = $false }
    $colour = switch -Wildcard ($outcome) { 'FAIL' { 'Red' } 'SKIP' { 'Yellow' } '*rerun*' { 'Yellow' } default { 'Green' } }
    Write-Host ('[slicecheck] {0} {1} {2} ({3:0}s) at_s={4:0}' -f $stage, $outcome, $detail, $seconds, $clock.Elapsed.TotalSeconds) -ForegroundColor $colour
    if ($outcome -eq 'FAIL') {
        foreach ($line in @($failure | Select-Object -First $maxFailureLines)) { Write-Host "  $line" }
        if ($log) { Write-Host ('  log: {0}' -f [IO.Path]::GetRelativePath($repoRoot, $log).Replace('\', '/')) }
    }
}

function Invoke-ToFile([string] $exe, [string[]] $arguments, [string] $log) {
    # Output (stdout and stderr) to $log, nothing on the console; returns the exit code.
    Push-Location $repoRoot
    try {
        & $exe @arguments *> $log
        return $LASTEXITCODE
    }
    finally { Pop-Location }
}

function Get-FirstLines([string[]] $lines, [string] $pattern, [int] $count = $maxFailureLines) {
    return @($lines | Where-Object { $_ -match $pattern } | ForEach-Object { $_.Trim() } | Select-Object -Unique -First $count)
}

function Invoke-BuildStage {
    $log = Join-Path $outDir 'build.log'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-ToFile $dotnetExe @('build', 'Lifestone.slnx', '--disable-build-servers') $log
    $lines = Get-LogLines $log
    $warnings = if (($lines | Select-String -Pattern '^\s*(\d+) Warning\(s\)' | Select-Object -Last 1)) { [int]($lines | Select-String -Pattern '^\s*(\d+) Warning\(s\)' | Select-Object -Last 1).Matches[0].Groups[1].Value } else { -1 }
    $errors = if (($lines | Select-String -Pattern '^\s*(\d+) Error\(s\)' | Select-Object -Last 1)) { [int]($lines | Select-String -Pattern '^\s*(\d+) Error\(s\)' | Select-Object -Last 1).Matches[0].Groups[1].Value } else { -1 }
    $pass = $code -eq 0 -and $warnings -eq 0 -and $errors -eq 0
    $detail = "$(if ($warnings -ge 0) { $warnings } else { '?' }) warnings, $(if ($errors -ge 0) { $errors } else { '?' }) errors$(if ($code -ne 0) { ", exit $code" })"
    $failure = @(Get-FirstLines $lines ': (error|warning) [A-Z]+\d+' | ForEach-Object { if ($_.Length -gt 220) { $_.Substring(0, 220) } else { $_ } })
    if ($failure.Count -eq 0) { $failure = @($lines | Select-Object -Last 5) }
    Write-Stage 'build' $(if ($pass) { 'PASS' } else { 'FAIL' }) $detail $timer.Elapsed.TotalSeconds $failure $log
    return $pass
}

function Get-TestFailureLines([string[]] $lines) {
    # "  Failed Some.Test.Name [12 ms]" and the first line of its "Error Message:" block.
    $failure = [Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $lines.Count -and $failure.Count -lt $maxFailureLines; $i++) {
        if ($lines[$i] -match '^\s+Failed\s+(\S.*?)(\s+\[[^\]]*\])?\s*$') {
            $message = ''
            for ($j = $i + 1; $j -lt [Math]::Min($i + 4, $lines.Count); $j++) {
                if ($lines[$j] -match 'Error Message:') { $message = if ($j + 1 -lt $lines.Count) { $lines[$j + 1].Trim() } else { '' }; break }
            }
            $text = "$($Matches[1]): $message"
            $failure.Add($(if ($text.Length -gt 220) { $text.Substring(0, 220) } else { $text }))
        }
    }
    if ($failure.Count -eq 0) { return @(Get-FirstLines $lines 'error|fail|exception' 8) }
    return @($failure)
}

function Get-SlowestDetail([string] $name) {
    # C6: the per-test timings of <name>'s trx files: top 20 to cache\slice-check\<name>-times.txt, the first three in the stage line.
    $times = @(Get-TrxTimes (Join-Path $outDir "$name.trx"))
    if ($times.Count -eq 0) { return '' }
    $file = Join-Path $outDir "$name-times.txt"
    Set-Content -LiteralPath $file -Value (Format-SlowestLines $times 20) -Encoding utf8
    return '; ' + (Format-SlowestSummary $times 20 3) + " (cache/slice-check/$name-times.txt)"
}

function Invoke-TestRun([string] $stage, [string] $filter, [int] $minimum) {
    # One `dotnet test` over the solution with the trx logger into this run's own folder (cache\slice-check\<stage>.trx, one trx per assembly).
    $log = Join-Path $outDir "$stage.log"
    $trx = Join-Path $outDir "$stage.trx"
    if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx -Recurse -Force }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-ToFile $dotnetExe @('test', 'Lifestone.slnx', '--no-build', '--disable-build-servers', '--filter', $filter, '--logger', 'trx;LogFilePrefix=test', '--results-directory', $trx) $log
    $lines = Get-LogLines $log
    $passed = 0; $failed = 0; $skipped = 0; $perAssembly = [Collections.Generic.List[string]]::new()
    # "Passed!  - Failed: 0, Passed: 905, Skipped: 8, Total: 913, Duration: 2 s - Lifestone.Net.Tests.dll (net10.0)"
    foreach ($line in $lines) {
        if ($line -match '(?:Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+\d+.*?-\s+(\S+?)\.dll') {
            $failed += [int]$Matches[1]; $passed += [int]$Matches[2]; $skipped += [int]$Matches[3]
            $name = $Matches[4] -replace '^Lifestone\.', '' -replace '\.Tests$', ''
            $perAssembly.Add("$name $($Matches[2])")
        }
    }
    $detail = "$passed passed ($($perAssembly -join ', ')), $failed failed, $skipped skipped"
    if ($minimum -gt 0) { $detail += "; floor $minimum ($(if ($stage -eq 'exhaustive') { 'baseline file' } else { $minTestsSource }))" }
    $pass = $code -eq 0 -and $failed -eq 0 -and $perAssembly.Count -gt 0
    $failure = @()
    if ($pass -and $minimum -gt 0 -and $passed -lt $minimum) {
        $pass = $false
        $detail += "; below -MinTests $minimum"
        $failure = @("$passed tests passed, -MinTests requires at least $minimum (the count must never decrease)")
    }
    elseif (-not $pass) {
        $failure = Get-TestFailureLines $lines
        if ($perAssembly.Count -eq 0) { $detail = "no test summary (exit $code)" }
    }
    $detail += Get-SlowestDetail $stage
    Write-Stage $stage $(if ($pass) { 'PASS' } else { 'FAIL' }) $detail $timer.Elapsed.TotalSeconds $failure $log
    return $pass
}

function Invoke-Smoke([string[]] $smokeArguments, [string] $name) {
    # tools\smoke.ps1 as a child process with its console output in cache\slice-check\<name>.log; its own work folder
    # (smoke deletes and recreates it) is cache\slice-check\<name>.work. Returns @{ Code; Log; Lines; Work }.
    $log = Join-Path $outDir "$name.log"
    $work = Join-Path $outDir "$name.work"
    $arguments = @('-NoProfile', '-File', $smokeScript, '-NoBuild', '-Backend', $Backend, '-OutDir', $work, '-Parallel', "$Parallel") + @(if ($AllSizes) { '-AllSizes' }) + $smokeArguments
    $code = Invoke-ToFile $pwshExe $arguments $log
    return @{ Code = $code; Log = $log; Lines = (Get-LogLines $log); Work = $work }
}

function Get-SmokeRowLog($run, [string] $step) {
    # The Log cell of the step's row in smoke's printed summary table ("| play-checks | PASS | 0 | 300 | 0 | detail | 05-play-checks.log |"), relative to the work folder.
    foreach ($line in $run.Lines) {
        if ($line -match '^\|\s*(\S+)\s*\|' -and $Matches[1] -eq $step) {
            $cells = @($line.Trim().Trim('|') -split '\|' | ForEach-Object { $_.Trim() })
            if ($cells.Count -ge 7) { return $cells[-1] }
        }
    }
    return ''
}

function Get-StepFailureLines($run, [string] $step) {
    # The step's own FAIL lines ([playcheck] FAIL ..., [uicheck] FAIL ...) from its log (named in smoke's table), else smoke's row for it.
    $failure = [Collections.Generic.List[string]]::new()
    $rel = Get-SmokeRowLog $run $step
    if ($rel) {
        $path = Join-Path $run.Work $rel
        foreach ($line in (Get-FirstLines (Get-LogLines $path) '\[\w+\] FAIL|Z ERROR \[|SCRIPT ERROR|Unhandled exception|killed after|no verdict' 10)) { $failure.Add($line) }
    }
    foreach ($line in (Get-FirstLines $run.Lines '^\[smoke\] .*\bFAIL\b' 4)) { $failure.Add($line) }
    return @($failure | Select-Object -Unique)
}

function Read-SmokeRow($run, [string] $step) {
    # "[smoke] play-checks             PASS  (68.3 s) pass=80 fail=0 max_frame_ms=21.3"; PASS (frames=contended) keeps its note.
    foreach ($line in $run.Lines) {
        if ($line -match '^\[smoke\]\s+(\S+)\s+(PASS(?: \([^)]*\))?|FAIL|SKIP)\s+\(([\d.]+) s\)\s*(.*)$' -and $Matches[1] -eq $step) {
            $detail = $Matches[4] -replace ';?\s*frames in .*$', ''
            return @{ Result = $Matches[2]; Seconds = [double]$Matches[3]; Detail = $detail.Trim() }
        }
    }
    return $null
}

function Get-ChangedFiles {
    # Read-only git: the diff against the merge base with -Base, the working tree against HEAD, and untracked files.
    $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Push-Location $repoRoot
    try {
        $mergeBase = (& git merge-base HEAD $Base 2>$null | Select-Object -First 1)
        $sources = @()
        if ($mergeBase) { $sources += , @('diff', '--name-only', $mergeBase, 'HEAD') }
        $sources += , @('diff', '--name-only', 'HEAD')
        $sources += , @('ls-files', '-o', '--exclude-standard')
        foreach ($source in $sources) {
            foreach ($line in @(& git @source 2>$null)) { if ($line) { [void]$files.Add(([string]$line).Trim().Replace('\', '/')) } }
        }
    }
    finally { Pop-Location }
    return @($files | Sort-Object)
}

function Resolve-ChangedSteps([string[]] $files, $map) {
    # Which backend-free steps the files touch (tools\lib\StepPaths.psd1). Returns @{ Steps; Live; Unmatched; Everything }.
    $startsWith = { param([string] $path, [string[]] $prefixes) foreach ($p in $prefixes) { if ($path.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)) { return $true } } return $false }
    $selected = [Collections.Generic.HashSet[string]]::new()
    $live = [Collections.Generic.HashSet[string]]::new()
    $unmatched = [Collections.Generic.List[string]]::new()
    $everything = $false
    foreach ($file in $files) {
        $matched = $false
        if (& $startsWith $file @($map.NoGodot)) { $matched = $true }
        if (& $startsWith $file @($map.Everything)) { $matched = $true; $everything = $true }
        foreach ($step in $map.Steps.Keys) { if (& $startsWith $file @($map.Steps[$step])) { $matched = $true; [void]$selected.Add($step) } }
        foreach ($step in $map.Live.Keys) { if (& $startsWith $file @($map.Live[$step])) { [void]$live.Add($step) } }
        if (-not $matched -and ($file.StartsWith('src/') -or $file.StartsWith('tools/'))) { $unmatched.Add($file) }
    }
    if ($everything -or $unmatched.Count -gt 0) { foreach ($step in $map.Steps.Keys) { [void]$selected.Add($step) } }
    return @{ Steps = @($selected); Live = @($live | Sort-Object); Unmatched = @($unmatched); Everything = $everything }
}

function Get-StepsToRun {
    # -Steps (a floor, aliases resolved) plus, with -Changed, the steps the diff touches; in smoke's own order. Prints the advisory lines.
    $resolved = @($Steps | ForEach-Object { if ($stepAliases.ContainsKey($_)) { $stepAliases[$_] } else { $_ } })
    if (-not $Changed) { return $resolved }
    $map = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'lib\StepPaths.psd1')
    $files = Get-ChangedFiles
    $found = Resolve-ChangedSteps $files $map
    $order = @($map.Steps.Keys)
    # smoke's order is its $allSteps order; the map's keys are not ordered, so sort by smoke's list.
    $smokeText = Get-Content -LiteralPath $smokeScript -Raw
    $allSteps = if ($smokeText -match '\$allSteps = @\(([^)]*)\)') { @([regex]::Matches($Matches[1], "'([^']+)'") | ForEach-Object { $_.Groups[1].Value }) } else { $order }
    $union = @($resolved + $found.Steps | Select-Object -Unique)
    $sorted = @($allSteps | Where-Object { $union -contains $_ }) + @($union | Where-Object { $allSteps -notcontains $_ })
    if ($found.Unmatched.Count -gt 0) {
        $named = ($found.Unmatched | Select-Object -First 5) -join ', '
        Write-Host "[slicecheck] changed: no rule for $($found.Unmatched.Count) path(s) under src/ or tools/ ($named), so every backend-free step runs (add them to tools\lib\StepPaths.psd1)"
    }
    elseif ($found.Everything) { Write-Host '[slicecheck] changed: a cross-cutting path changed, so every backend-free step runs' }
    if ($found.Live.Count -gt 0) { Write-Host "[slicecheck] live-probe SKIP touched: $($found.Live -join ',') (slice-check never starts the backend)" }
    return $sorted
}

function Invoke-StepsStage([string[]] $stepList) {
    if ($stepList.Count -eq 0) { Write-Stage 'steps' 'SKIP' 'no backend-free step is touched by the diff' 0; return }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    # ONE smoke process for all the steps (one start-up, one scan, one stray-output listing); the rerun is one more for the failures.
    $run = Invoke-Smoke @('-Only', ($stepList -join ',')) 'steps'
    $rows = @{}
    foreach ($step in $stepList) { $rows[$step] = Read-SmokeRow $run $step }
    $failed = @($stepList | Where-Object { -not $rows[$_] -or $rows[$_].Result -eq 'FAIL' })
    $rerunRun = $null
    $rerunRows = @{}
    if ($failed.Count -gt 0 -and $Rerun) {
        $rerunRun = Invoke-Smoke @('-Only', ($failed -join ',')) 'steps-rerun'
        foreach ($step in $failed) { $rerunRows[$step] = Read-SmokeRow $rerunRun $step }
    }
    foreach ($step in $stepList) {
        $row = $rows[$step]
        $thisRun = $run
        $outcome = if ($row) { ($row.Result -split ' ')[0] } else { 'FAIL' }
        $detail = if ($row) { $row.Detail } else { "no result row for '$step' (smoke exit $($run.Code))" }
        $seconds = if ($row) { $row.Seconds } else { 0 }
        $note = ''
        if ($row -and $row.Result -match '^PASS \((?<n>[^)]*)\)$') { $note = " ($($Matches['n']))" }
        if ($outcome -eq 'FAIL' -and $Rerun) {
            $rerunRow = $rerunRows[$step]
            $thisRun = $rerunRun
            if ($rerunRow -and $rerunRow.Result -like 'PASS*') { $outcome = 'PASS'; $note = ' (rerun)'; $detail = $rerunRow.Detail; $seconds += $rerunRow.Seconds }
            else { $detail = if ($rerunRow) { "$($rerunRow.Detail); failed again on rerun" } else { 'no result row on rerun; failed again' } }
        }
        $failure = if ($outcome -eq 'FAIL') { Get-StepFailureLines $thisRun $step } else { @() }
        Write-Stage $step "$outcome$note" $detail $seconds $failure $thisRun.Log
    }
}

function Invoke-SmokeStage {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    # G-A: the whole smoke, frame checks judged (-SoloFrames: it waits for a quiet machine and fails on any frame check) and the secret scan in full (-FullScan).
    $run = Invoke-Smoke @('-SoloFrames', '-FullScan') 'smoke'
    # Its test step is the run's test (no second dotnet test): the `test` line comes from its row, judged by -MinTests.
    $testRow = Read-SmokeRow $run 'test'
    if ($testRow) {
        $count = if ($testRow.Detail -match 'passed=(\d+) failed=(\d+) skipped=(\d+)') { @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3]) } else { @(0, 0, 0) }
        $pass = $testRow.Result -like 'PASS*' -and $count[0] -gt 0
        $detail = "$($count[0]) passed, $($count[1]) failed, $($count[2]) skipped (smoke's test step)$(if ($MinTests -gt 0) { "; floor $MinTests ($minTestsSource)" })" + $(if ($testRow.Detail -match ';\s*(top \d+ slowest.*)$') { "; $($Matches[1])" } else { '' })
        $failure = @()
        if ($pass -and $MinTests -gt 0 -and $count[0] -lt $MinTests) {
            $pass = $false; $detail += "; below -MinTests $MinTests"
            $failure = @("$($count[0]) tests passed, -MinTests requires at least $MinTests (the count must never decrease)")
        }
        elseif (-not $pass) { $failure = Get-StepFailureLines $run 'test' }
        Write-Stage 'test' $(if ($pass) { 'PASS' } else { 'FAIL' }) $detail $testRow.Seconds $failure $run.Log
    }
    $summary = @($run.Lines | Where-Object { $_ -match ' - total [\d.]+ min - ' } | Select-Object -Last 1)
    $verdict = if ($summary -and $summary[0] -match ' - (total [\d.]+ min) - (.*)$') { "$($Matches[2]), $($Matches[1])" } else { "no summary (exit $($run.Code))" }
    $pass = $run.Code -eq 0
    $failure = @()
    if (-not $pass) {
        $failure = @($run.Lines | Where-Object { $_ -match '^\|[^|]+\|\s*FAIL' } | ForEach-Object { ($_ -replace '\s+', ' ').Trim() })
        $failure += Get-FirstLines $run.Lines 'secrets=.*FAIL' 2
    }
    Write-Stage 'smoke' $(if ($pass) { 'PASS' } else { 'FAIL' }) $verdict $timer.Elapsed.TotalSeconds $failure $run.Log
}

$buildOk = $true
if ($Stages -contains 'build') { $buildOk = Invoke-BuildStage }
$stepList = @()
if ($Stages -contains 'steps') { $stepList = @(Get-StepsToRun) }
foreach ($stage in 'test', 'exhaustive', 'steps', 'smoke') {
    if ($Stages -notcontains $stage) { continue }
    if (-not $buildOk) {
        if ($stage -eq 'steps') { foreach ($step in $stepList) { Write-Stage $step 'SKIP' 'the build failed' 0 } } else { Write-Stage $stage 'SKIP' 'the build failed' 0 }
        continue
    }
    switch ($stage) {
        # With the smoke stage its test step is the run's test (below): no second dotnet test.
        'test' { if ($Stages -notcontains 'smoke') { Invoke-TestRun 'test' $testFilter $MinTests | Out-Null } }
        'exhaustive' { Invoke-TestRun 'exhaustive' $exhaustiveFilter $exhaustiveMinimum | Out-Null }
        'steps' { Invoke-StepsStage $stepList }
        'smoke' { Invoke-SmokeStage }
    }
}

Write-Host "[slicecheck] done ok=$($script:ok.ToString().ToLowerInvariant())"
exit $(if ($script:ok) { 0 } else { 1 })
