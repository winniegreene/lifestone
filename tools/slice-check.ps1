<#
.SYNOPSIS
    One command that checks a slice: the build, the default tests and the Godot steps a slice names, one line per stage.

.DESCRIPTION
    Prints exactly one line per stage and nothing else on stdout:

      [slicecheck] <stage> PASS|FAIL|SKIP <short detail> (<seconds>s)

    Stages, in order (-Stages picks a subset; the default is build,test,steps, plus smoke with -Smoke):

      build   dotnet build Lifestone.slnx --disable-build-servers; detail = warning and error counts (0 and 0 to pass)
      test    dotnet test Lifestone.slnx --no-build --filter "Category!=Loopback&Tier!=Soak"; detail = passed/failed/skipped
              totals and the passed count of each test assembly. -MinTests N fails the stage when fewer than N tests
              passed (the "count never decreases" gate)
      steps   each step of -Steps through tools\smoke.ps1 itself (-Only <step> -NoBuild -Backend auto), so the Godot command
              lines live only there; one line per step with the step's own detail (pass/fail counts, max frame time). The
              name retail-ui-checks is an alias of smoke's retail-ui step (its frames and the [uicheck] suite)
      smoke   with -Smoke: the whole of tools\smoke.ps1 -Backend auto -NoBuild; detail = its verdict and total time

    Full logs go to cache\slice-check\<stage>.log (gitignored). A FAIL prints at most 15 lines of the failure (failing test
    names and messages, or the step's FAIL lines), then the log path. The last line is "[slicecheck] done ok=true|false"
    and the exit code is 0 or 1. When the build fails the later stages are reported SKIP.

    -Rerun runs a failed Godot step once more (the known 50 ms flakes on a loaded machine); a pass on the rerun is
    reported as "PASS (rerun)".

    Every Godot check and frame run is at retail's native 800x600 (owner rule, 2026-10-14). -AllSizes passes smoke's -AllSizes: the larger sizes
    (1920x1080, 2560x1440) as well; never run by default.

    The backend is never started: -Backend accepts only auto (a step that needs it is skipped by smoke when it is down).
    The dotnet environment (telemetry off, no global tools on PATH, no ASP.NET certificate) is set for this process
    and its children, and the per-user SDK is found as smoke.ps1 does (tools\lib\Toolchain.psm1).

.EXAMPLE
    pwsh -File tools\slice-check.ps1 -MinTests 6142 -Steps play-checks,retail-ui-checks -Rerun

.EXAMPLE
    pwsh -File tools\slice-check.ps1 -Stages build,test
#>
[CmdletBinding()]
param(
    [string[]] $Stages = @(),
    [string[]] $Steps = @(),
    [ValidateRange(0, 1000000)]
    [int] $MinTests = 0,
    [switch] $Smoke,
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
$allStages = @('build', 'test', 'steps', 'smoke')
# retail-ui-checks is the name slices use for the retail UI layer's checks; smoke runs them inside its retail-ui step.
$stepAliases = @{ 'retail-ui-checks' = 'retail-ui' }
$testFilter = 'Category!=Loopback&Tier!=Soak'
$maxFailureLines = 15

# 'pwsh -File' passes '-Steps a,b' as one string: split on commas either way.
function Split-List([string[]] $values) { @($values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
$Stages = @(Split-List $Stages)
$Steps = @(Split-List $Steps)
foreach ($name in $Stages) { if ($allStages -notcontains $name) { throw "Unknown stage '$name'. Stages: $($allStages -join ', ')" } }
if ($Stages.Count -eq 0) { $Stages = @(@("build", "test") + @(if ($Steps.Count) { "steps" }) + @(if ($Smoke) { "smoke" })) }
elseif ($Smoke -and $Stages -notcontains 'smoke') { $Stages += 'smoke' }
$resolvedSteps = @($Steps | ForEach-Object { if ($stepAliases.ContainsKey($_)) { $stepAliases[$_] } else { $_ } })
foreach ($name in $resolvedSteps) {
    if ($name -in @('build', 'test')) { throw "'$name' is a stage, not a step: use -Stages." }
    if ($name -in @('login-check', 'login')) { throw "'$name' needs the backend, which slice-check never starts." }
}
if ($Stages -contains 'steps' -and $Steps.Count -eq 0) { throw "The steps stage needs -Steps <names>." }

Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
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

function Strip-Ansi([string] $text) { [regex]::Replace($text, '\x1b\[[0-9;]*[A-Za-z]', '') }

function Get-LogLines([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path | ForEach-Object { Strip-Ansi $_ })
}

function Write-Stage([string] $stage, [string] $outcome, [string] $detail, [double] $seconds, [string[]] $failure = @(), [string] $log = '') {
    # One line per stage; on a FAIL at most $maxFailureLines lines of the failure, then the log path.
    if ($outcome -eq 'FAIL') { $script:ok = $false }
    $colour = switch -Wildcard ($outcome) { 'FAIL' { 'Red' } 'SKIP' { 'Yellow' } '*rerun*' { 'Yellow' } default { 'Green' } }
    Write-Host ('[slicecheck] {0} {1} {2} ({3:0}s)' -f $stage, $outcome, $detail, $seconds) -ForegroundColor $colour
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

function Invoke-TestStage {
    $log = Join-Path $outDir 'test.log'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $code = Invoke-ToFile $dotnetExe @('test', 'Lifestone.slnx', '--no-build', '--disable-build-servers', '--filter', $testFilter) $log
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
    $pass = $code -eq 0 -and $failed -eq 0 -and $perAssembly.Count -gt 0
    $failure = @()
    if ($pass -and $MinTests -gt 0 -and $passed -lt $MinTests) {
        $pass = $false
        $detail += "; below -MinTests $MinTests"
        $failure = @("$passed tests passed, -MinTests requires at least $MinTests (the count must never decrease)")
    }
    elseif (-not $pass) {
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
        if ($failure.Count -eq 0) { $failure = @(Get-FirstLines $lines 'error|fail|exception' 8) }
        if ($perAssembly.Count -eq 0) { $detail = "no test summary (exit $code)" }
    }
    Write-Stage 'test' $(if ($pass) { 'PASS' } else { 'FAIL' }) $detail $timer.Elapsed.TotalSeconds $failure $log
    return $pass
}

function Invoke-Smoke([string[]] $smokeArguments, [string] $name) {
    # tools\smoke.ps1 as a child process with its console output in cache\slice-check\<name>.log; its own work folder
    # (smoke deletes and recreates it) is cache\slice-check\<name>.work. Returns @{ Code; Log; Lines; Work }.
    $log = Join-Path $outDir "$name.log"
    $work = Join-Path $outDir "$name.work"
    $arguments = @('-NoProfile', '-File', $smokeScript, '-NoBuild', '-Backend', $Backend, '-OutDir', $work) + @(if ($AllSizes) { '-AllSizes' }) + $smokeArguments
    $code = Invoke-ToFile $pwshExe $arguments $log
    return @{ Code = $code; Log = $log; Lines = (Get-LogLines $log); Work = $work }
}

function Get-StepFailureLines($run, [string] $step) {
    # The step's own FAIL lines ([playcheck] FAIL ..., [uicheck] FAIL ...) from its logs, else smoke's row for it.
    $failure = [Collections.Generic.List[string]]::new()
    foreach ($stepLog in @(Get-ChildItem -LiteralPath $run.Work -Filter "*-$step*.log" -ErrorAction SilentlyContinue | Sort-Object Name)) {
        foreach ($line in (Get-FirstLines (Get-LogLines $stepLog.FullName) '\[\w+\] FAIL|Z ERROR \[|SCRIPT ERROR|Unhandled exception|killed after|no verdict' 10)) { $failure.Add($line) }
    }
    foreach ($line in (Get-FirstLines $run.Lines '^\[smoke\] .*\bFAIL\b' 4)) { $failure.Add($line) }
    return @($failure | Select-Object -Unique)
}

function Read-SmokeRow($run, [string] $step) {
    # "[smoke] play-checks             PASS  (68.3 s) pass=80 fail=0 max_frame_ms=21.3"
    foreach ($line in $run.Lines) {
        if ($line -match '^\[smoke\]\s+(\S+)\s+(PASS(?: \([^)]*\))?|FAIL|SKIP)\s+\(([\d.]+) s\)\s*(.*)$' -and $Matches[1] -eq $step) {
            $detail = $Matches[4] -replace ';?\s*frames in .*$', ''
            return @{ Result = $Matches[2]; Detail = $detail.Trim() }
        }
    }
    return $null
}

function Invoke-StepsStage {
    foreach ($step in $Steps) {
        $smokeStep = if ($stepAliases.ContainsKey($step)) { $stepAliases[$step] } else { $step }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $safe = $smokeStep -replace '[^\w-]', '_'
        $run = Invoke-Smoke @('-Only', $smokeStep) "step-$safe"
        $row = Read-SmokeRow $run $smokeStep
        $outcome = if ($row) { ($row.Result -split ' ')[0] } else { 'FAIL' }
        $detail = if ($row) { $row.Detail } else { "no result row for '$smokeStep' (smoke exit $($run.Code))" }
        $note = ''
        if ($outcome -eq 'FAIL' -and $Rerun) {
            $firstFailure = Get-StepFailureLines $run $smokeStep
            $rerunRun = Invoke-Smoke @('-Only', $smokeStep) "step-$safe-rerun"
            $rerunRow = Read-SmokeRow $rerunRun $smokeStep
            if ($rerunRow -and $rerunRow.Result -like 'PASS*') {
                $outcome = 'PASS'; $note = ' (rerun)'; $detail = $rerunRow.Detail; $run = $rerunRun
            }
            else {
                $run = $rerunRun
                $detail = if ($rerunRow) { "$($rerunRow.Detail); failed again on rerun" } else { "no result row on rerun; failed again" }
            }
        }
        $failure = if ($outcome -eq 'FAIL') { Get-StepFailureLines $run $smokeStep } else { @() }
        Write-Stage $step "$outcome$note" $detail $timer.Elapsed.TotalSeconds $failure $run.Log
    }
}

function Invoke-SmokeStage {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $run = Invoke-Smoke @() 'smoke'
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
foreach ($stage in 'test', 'steps', 'smoke') {
    if ($Stages -notcontains $stage) { continue }
    if (-not $buildOk) {
        if ($stage -eq 'steps') { foreach ($step in $Steps) { Write-Stage $step 'SKIP' 'the build failed' 0 } } else { Write-Stage $stage 'SKIP' 'the build failed' 0 }
        continue
    }
    switch ($stage) {
        'test' { Invoke-TestStage | Out-Null }
        'steps' { Invoke-StepsStage }
        'smoke' { Invoke-SmokeStage }
    }
}

Write-Host "[slicecheck] done ok=$($script:ok.ToString().ToLowerInvariant())"
exit $(if ($script:ok) { 0 } else { 1 })
