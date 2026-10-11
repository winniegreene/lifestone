<#
.SYNOPSIS
    The M1 integration suite: build, tests and every Godot check harness, one after the other, with a summary table.

.DESCRIPTION
    Runs, sequentially and off-screen (each Godot run through tools\godot.ps1 -Run -Offscreen -IsolateUserData with a
    timeout):

      build                    dotnet build Lifestone.slnx (must report 0 warnings and 0 errors)
      test                     dotnet test Lifestone.slnx (must pass)
      cli-help                 --help prints the option registry and exits 0
      cli-bad-argument         an automated run with a mistyped option exits 1 at once
      terrain-golden           tools\terrain-golden.ps1 (GPU terrain against the CPU reference compositor)
      engine-checks            --engine-checks
      interior-engine-checks   --interior-engine-checks
      sky-checks               --sky-checks
      blend-checks             --blend-checks (byte-space blending, the decode, the detail pass, built-in materials)
      spell-showcase           --scene=spellviewer --showcase (four entries; must end with exceptions=0)
      golden-viewpoints        --golden-viewpoints (every viewpoint captured)
      fly-path                 --fly-path=holtburg:arwic:<FlySpeed> (DoD 8: no frame over 50 ms; exit 2 if one is)
      stray-output             no image or video file appeared under src/ or tests/ during the run

    Every step's console output goes to <OutDir>\<nn>-<step>.log, outputs (golden images, terrain-golden diffs) below
    <OutDir>, and the table to <OutDir>\summary.md. Exits 0 when every step passed, 1 otherwise.

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
    is written outside the repository; no environment, registry or system setting is changed.

.PARAMETER OutDir
    Folder for logs and outputs. Default: <repo>\cache\smoke (gitignored). Recreated on every run.

.PARAMETER Only
    Run only these steps (names as above), e.g. -Only engine-checks,sky-checks. The build still runs unless -NoBuild.

.PARAMETER Skip
    Skip these steps.

.PARAMETER NoBuild
    Use the existing build (skips the build step).

.PARAMETER FlySpeed
    Fly-path speed in m/s (default 200, about Shift-speed in free-fly: a 30 s flight).

.PARAMETER JudgeLifestoneFramesOnly
    Fly path: pass when every frame over 50 ms had no Lifestone work in it (main-thread drain under 1 ms, no GC, attach
    or rebase), i.e. judge DoD 8 on the attributed maximum (perf audit, dod8-verdict-polluted-by-environment), shown as
    "PASS (attributed)". Off by default: the DoD's own wording judges the raw maximum (with the one retry above).

.PARAMETER DotnetRoot
    The .NET SDK root (the folder holding dotnet.exe). Default, the first that exists: $env:LIFESTONE_DOTNET_ROOT, the
    per-user install %LOCALAPPDATA%\Microsoft\dotnet, $env:DOTNET_ROOT, then the folder of dotnet.exe on PATH
    (tools\lib\Toolchain.psm1). The Godot steps find Godot as tools\godot.ps1 does.

.EXAMPLE
    pwsh -File tools\smoke.ps1

.EXAMPLE
    pwsh -File tools\smoke.ps1 -NoBuild -Only engine-checks,interior-engine-checks,sky-checks,blend-checks
#>
[CmdletBinding()]
param(
    [string] $OutDir = '',
    [string[]] $Only = @(),
    [string[]] $Skip = @(),
    [switch] $NoBuild,
    [ValidateRange(10, 2000)]
    [int] $FlySpeed = 200,
    [switch] $JudgeLifestoneFramesOnly,
    [string] $DotnetRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$godotScript = Join-Path $PSScriptRoot 'godot.ps1'
$goldenScript = Join-Path $PSScriptRoot 'terrain-golden.ps1'
$pwshExe = (Get-Process -Id $PID).Path
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'cache\smoke' }
$OutDir = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutDir)
Import-Module (Join-Path $PSScriptRoot 'lib\Toolchain.psm1') -Force
$DotnetRoot = Find-LifestoneDotnetRoot -DotnetRoot $DotnetRoot
$dotnetExe = Join-Path $DotnetRoot 'dotnet.exe'

$allSteps = @('build', 'test', 'cli-help', 'cli-bad-argument', 'terrain-golden', 'engine-checks', 'interior-engine-checks',
    'sky-checks', 'blend-checks', 'spell-showcase', 'golden-viewpoints', 'fly-path', 'stray-output')
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

function Invoke-Logged {
    <# Runs one child process with its output (stdout and stderr) captured into $LogPath; returns the exit code. #>
    param([string] $FileName, [string[]] $Arguments, [string] $LogPath, [int] $TimeoutSeconds)
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
        $process.Kill($true)
        $process.WaitForExit()
        Add-Content -LiteralPath $LogPath -Value $stdout.Result, $stderr.Result, "[smoke] killed after $TimeoutSeconds s" -Encoding utf8
        return 124
    }
    $process.WaitForExit()
    Add-Content -LiteralPath $LogPath -Value $stdout.Result, $stderr.Result -Encoding utf8
    return $process.ExitCode
}

function Invoke-Godot {
    <# One off-screen game run through tools\godot.ps1; returns @{ Code; Log; Text }. #>
    param([string] $Step, [string] $GameArgs, [int] $TimeoutSeconds, [string] $Suffix = '')
    $log = Join-Path $OutDir ('{0:00}-{1}{2}.log' -f $script:stepIndex, $Step, $Suffix)
    $arguments = @('-NoProfile', '-File', $godotScript, '-Run', '-Offscreen', '-IsolateUserData', '-NoBuild',
        '-TimeoutSeconds', "$TimeoutSeconds", "-GameArgs:$GameArgs")
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
    #>
    param([string] $Step, [bool] $Pass, [int] $Code, [double] $Seconds, [string] $Detail, [string] $Log = '', [int] $Errors = -1, [string] $PassNote = '')
    if ($Errors -gt 0) {
        $Pass = $false
        $Detail = "$Detail; FAIL: $Errors ERROR line(s) logged"
    }
    $result = if (-not $Pass) { 'FAIL' } elseif ($PassNote) { "PASS ($PassNote)" } else { 'PASS' }
    $script:results.Add([pscustomobject]@{
            Step    = $Step
            Result  = $result
            Passed  = $Pass
            Exit    = $Code
            Seconds = [math]::Round($Seconds, 1)
            Errors  = if ($Errors -lt 0) { '' } else { "$Errors" }
            Detail  = $Detail
            Log     = if ($Log) { [IO.Path]::GetFileName($Log) } else { '' }
        })
    $colour = if (-not $Pass) { 'Red' } elseif ($PassNote) { 'Yellow' } else { 'Green' }
    Write-Host ("[smoke] {0,-24} {1}  ({2:0.0} s) {3}" -f $Step, $result, $Seconds, $Detail) -ForegroundColor $colour
}

function Test-Wanted([string] $step) {
    if ($Skip -contains $step) { return $false }
    if ($Only.Count -gt 0 -and $Only -notcontains $step -and -not ($step -eq 'build' -and -not $NoBuild)) { return $false }
    return $true
}

function Get-StrayOutputs {
    <# Image and video files under src/ and tests/ (build output folders excluded): renders that escaped the rule. #>
    $roots = @('src', 'tests') | ForEach-Object { Join-Path $repoRoot $_ }
    $extensions = @('.png', '.jpg', '.jpeg', '.webp', '.avi', '.mp4')
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

foreach ($step in $allSteps) {
    if ($step -eq 'build' -and $NoBuild) { continue }
    if (-not (Test-Wanted $step)) { continue }
    $script:stepIndex++
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
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
                -Arguments @('test', (Join-Path $repoRoot 'Lifestone.slnx'), '--disable-build-servers', '--no-build', '-nologo')
            $text = Get-Content -LiteralPath $log -Raw
            $passed = 0; $failed = 0; $skipped = 0
            foreach ($m in [regex]::Matches($text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')) {
                $failed += [int]$m.Groups[1].Value; $passed += [int]$m.Groups[2].Value; $skipped += [int]$m.Groups[3].Value
            }
            Add-Result $step ($code -eq 0 -and $failed -eq 0 -and $passed -gt 0) $code $timer.Elapsed.TotalSeconds "passed=$passed failed=$failed skipped=$skipped" $log
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
            $detail = if ($new.Count -eq 0) { 'no new image/video files under src/ or tests/' } else { "new: $($new -join ', ')" }
            Add-Result $step ($new.Count -eq 0) 0 $timer.Elapsed.TotalSeconds $detail
        }
    }
}

$failedSteps = @($script:results | Where-Object { -not $_.Passed })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Lifestone smoke run")
$lines.Add('')
$lines.Add("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $repoRoot - total $([math]::Round($suiteTimer.Elapsed.TotalMinutes, 1)) min - " +
    $(if ($failedSteps.Count -eq 0) { 'ALL PASS' } else { "$($failedSteps.Count) FAILED" }))
$lines.Add('')
$lines.Add('| Step | Result | Exit | Seconds | Errors logged | Detail | Log |')
$lines.Add('|---|---|---:|---:|---:|---|---|')
foreach ($r in $script:results) {
    $lines.Add("| $($r.Step) | $($r.Result) | $($r.Exit) | $($r.Seconds) | $($r.Errors) | $($r.Detail -replace '\|', '/') | $($r.Log) |")
}
$summary = $lines -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $OutDir 'summary.md') -Value $summary -Encoding utf8
Write-Host ''
Write-Host $summary
exit $(if ($failedSteps.Count -eq 0) { 0 } else { 1 })
