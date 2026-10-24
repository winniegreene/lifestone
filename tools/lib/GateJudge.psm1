<#
    The live gate's runner and judges (M4 P4, D31), as a module so the lane jobs (Start-Job) and the offline tests run the same code.
    tools\live-gate.ps1 imports it; LiveGateRunnerTests import it too and execute these functions with stub steps (no backend, no game).
#>

function Test-FrameOnlyText {
    <#
      True when a failed smoke row failed on frame windows only: every Lifestone ERROR line in its log is a frame line (D42), or the row says so (P4 rfix-c, fault 3: the leg and check ERROR lines
      are read through their why=, in the forms the game really writes: "frame over 50 ms", "a frame was over 50 ms", "DoD 15: 1 frame(s) over 50 ms between Enter and walking, the longest 68.5 ms (...)",
      and "<name>_frame_max_ms=N (at most M expected)").
    #>
    param([string] $Detail, [string] $LogText)
    if ($Detail -match 'frame-only failure') { return $true }
    $lines = @([regex]::Matches($LogText, 'Z ERROR \[[^\]]*\][^\r\n]*') | ForEach-Object { $_.Value })
    if ($lines.Count -eq 0) { return $false }
    $frameSegment = '(?i)(^|[\s:])(a |\d+ )?frames?(\(s\))? (was |were )?over \d+ ms|^\s*\w*frame_max_ms=[\d.]+ \(at most \d+ expected\)'
    foreach ($line in $lines) {
        if ($line -match 'frame over 50 ms') { continue }
        if ($line -match '(?:failed|check failed)[^\r\n]*?\bwhy=(?<why>.*)$') {
            $why = $Matches['why'] -replace '\s+recovered=\w+\s*$', ''
            $why = $why.Trim() -replace '^"', '' -replace '"$', ''
            $isFrame = {
                param($text)
                $segments = @($text -split ';\s*' | Where-Object { $_ })
                $segments.Count -gt 0 -and @($segments | Where-Object { $_ -notmatch $frameSegment }).Count -eq 0
            }
            # Whole, or without a trailing explanation in brackets ("(each is logged above ...)").
            if ((& $isFrame $why) -or (& $isFrame ($why -replace '\s*\([^()]*\)\s*$', ''))) { continue }
        }
        return $false
    }
    return $true
}

function Get-FrameMaxMs {
    <# The largest frame figure a failed row or its log names, in ms (0 when none): "(at most 75 expected)" fields, "the longest N ms", and the row's own *frame_max_ms / enter_to_walk_max_ms fields. #>
    param([string] $Detail, [string] $LogText)
    $max = 0.0
    $inv = [Globalization.CultureInfo]::InvariantCulture
    foreach ($m in [regex]::Matches($LogText, 'frame_max_ms=([\d.]+) \(at most \d+ expected\)')) { $max = [math]::Max($max, [double]::Parse($m.Groups[1].Value, $inv)) }
    foreach ($m in [regex]::Matches($LogText, 'the longest ([\d.]+) ms')) { $max = [math]::Max($max, [double]::Parse($m.Groups[1].Value, $inv)) }
    foreach ($m in [regex]::Matches($Detail, '(?<![\w])(?:enter_to_walk|[a-z]+_frame)_max_ms=([\d.]+)')) { $max = [math]::Max($max, [double]::Parse($m.Groups[1].Value, $inv)) }
    return $max
}

function Get-TestCounts {
    <#
      Passed, failed and skipped counts of one dotnet test step (P4 rfix-c, fault 1). Sources, best first: the trx's Counters element (passed, failed + error + timeout + aborted; skipped = total - executed),
      then the console summary in either of its forms: the detailed logger's "Total tests: N / Passed: N / Failed: N / Skipped: N" lines (a line is absent when its count is 0) and the normal logger's
      "Failed: N, Passed: N, Skipped: N, Total: N" line. Returns @{ Passed; Failed; Skipped; Source } with Source 'trx', 'console' or 'none'.
    #>
    param([string] $Text, [string] $TrxPath = '')
    if ($TrxPath -and (Test-Path -LiteralPath $TrxPath)) {
        $trx = Get-Content -LiteralPath $TrxPath -Raw
        $c = [regex]::Match($trx, '<Counters\b[^>]*>')
        if ($c.Success) {
            $get = { param($n) $m = [regex]::Match($c.Value, "\b$n=`"(\d+)`""); if ($m.Success) { [int]$m.Groups[1].Value } else { 0 } }
            $total = & $get 'total'; $executed = & $get 'executed'
            return @{ Passed = (& $get 'passed'); Failed = ((& $get 'failed') + (& $get 'error') + (& $get 'timeout') + (& $get 'aborted')); Skipped = [math]::Max(0, $total - $executed); Source = 'trx' }
        }
    }
    $passed = 0; $failed = 0; $skipped = 0; $found = $false
    foreach ($m in [regex]::Matches($Text, 'Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)')) { $failed += [int]$m.Groups[1].Value; $passed += [int]$m.Groups[2].Value; $skipped += [int]$m.Groups[3].Value; $found = $true }
    if (-not $found -and $Text -match '(?m)^\s*Total tests:\s+\d+') {
        $found = $true
        $m = [regex]::Match($Text, '(?m)^\s*Passed:\s+(\d+)\s*$'); if ($m.Success) { $passed = [int]$m.Groups[1].Value }
        $m = [regex]::Match($Text, '(?m)^\s*Failed:\s+(\d+)\s*$'); if ($m.Success) { $failed = [int]$m.Groups[1].Value }
        $m = [regex]::Match($Text, '(?m)^\s*Skipped:\s+(\d+)\s*$'); if ($m.Success) { $skipped = [int]$m.Groups[1].Value }
    }
    return @{ Passed = $passed; Failed = $failed; Skipped = $skipped; Source = $(if ($found) { 'console' } else { 'none' }) }
}

function New-GateRow {
    param([string] $Name, [bool] $Ok, [string] $Detail, [double] $Seconds = 0, [bool] $FrameOnly = $false, [bool] $Over75 = $false)
    return [pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail; Seconds = $Seconds; FrameOnly = $FrameOnly; Over75 = $Over75 }
}

function Get-SmokeStepRows {
    <#
      Judges one smoke step of the gate. $SmokeRows: the rows smoke wrote (rows.json); $Summary: its summary.json (or $null); $ReadLog: a scriptblock that returns the text of a row's log.
      A row is the step's own verdict, plus (P4-R3): a non-zero smoke exit with every row clean is a FAIL row (a summary-level failure such as an unreadable folder), and a secret found by the
      scan (summary.json secrets other than 0) or a missing summary.json is a FAIL row. A failed frame window is FrameOnly (the three-run rule) via Test-FrameOnlyText (P4-R9).
    #>
    param([string] $StepName, [int] $Code, [object[]] $SmokeRows, $Summary, [scriptblock] $ReadLog, [string] $LogPath = '')
    $rows = [System.Collections.Generic.List[object]]::new()
    $smokeRows = @($SmokeRows | Where-Object { $_ })
    if ($smokeRows.Count -eq 0) { $rows.Add((New-GateRow $StepName $false "smoke wrote no rows (exit $Code; $LogPath)")) }
    foreach ($r in $smokeRows) {
        $ok = $r.Outcome -eq 'PASS'
        $logText = ''
        if ($r.Log -and $ReadLog) { $t = & $ReadLog $r; if ($t) { $logText = [string]$t } }
        $frameOnly = (-not $ok) -and $r.Outcome -ne 'SKIP' -and [bool](Test-FrameOnlyText -Detail ([string]$r.Detail) -LogText $logText)
        $over75 = [bool]($logText -match 'frame_max_ms=([\d.]+) \(at most 75 expected\)') -or ((-not $ok) -and $frameOnly -and (Get-FrameMaxMs -Detail ([string]$r.Detail) -LogText $logText) -gt 75.0)
        $name = if ($smokeRows.Count -gt 1 -or $r.Step -ne $StepName) { "${StepName}:$($r.Step)" } else { $StepName }
        $label = if ($r.Outcome -eq 'SKIP') { 'SKIPPED (a gate step must run)' } else { $r.Outcome }
        $rows.Add((New-GateRow $name $ok "$label $($r.Detail)".Trim() ([double]$r.Seconds) $frameOnly $over75))
    }
    if ($smokeRows.Count -gt 0) {
        $anyFail = @($rows | Where-Object { -not $_.Ok }).Count -gt 0
        if ($null -eq $Summary) {
            $rows.Add((New-GateRow "${StepName}:summary" $false "smoke wrote no readable summary.json (exit $Code), so its secret scan cannot be read"))
        }
        else {
            $secrets = $Summary.PSObject.Properties['secrets']
            $scanned = $Summary.PSObject.Properties['scanned']
            $n = if ($secrets) { [int]$secrets.Value } else { -1 }
            if ($n -ne 0) { $rows.Add((New-GateRow "${StepName}:secret-scan" $false "secret scan: secrets=$n (see summary.md; -1 = the scan itself failed)")); $anyFail = $true }
            elseif ($scanned -and [int]$scanned.Value -gt 0) { $rows[0].Detail = "$($rows[0].Detail) secrets=0 scanned=$([int]$scanned.Value)".Trim() }
        }
        if ($Code -ne 0 -and -not $anyFail) {
            $verdict = if ($Summary -and $Summary.PSObject.Properties['verdict']) { " verdict=$($Summary.verdict)" } else { '' }
            $rows.Add((New-GateRow "${StepName}:exit" $false "smoke exit $Code with every row clean$verdict; see summary.md"))
        }
    }
    return @($rows)
}

function Get-GateReleaseWait {
    <#
      P5a F-gate (diagnosis item 6): a step killed by its timeout leaves its headless client in the world, and ACE keeps the character online until its session timeout (60 s) plus the log off (LoginRetryPolicy: 75 s), so
      the next step on the same account is refused with CharacterError 0x0D. Returns the seconds the step must still wait: the longest remaining release over the killed-step stamps of its account (an unknown account '' waits
      on every stamp; 'none' never waits). The stamps are files in the gate folder, so lanes (separate jobs) and phases see each other's kills. The gate cannot observe the character leaving the world (ACE's online state is
      in its memory, not in the database), so the wait is the bounded fixed one.
    #>
    param([string] $LogDir, [string] $Account, [datetime] $NowUtc = [datetime]::UtcNow)
    if ($Account -eq 'none') { return @{ Seconds = 0.0; From = '' } }
    $best = 0.0; $from = ''
    foreach ($f in @(Get-ChildItem -LiteralPath $LogDir -Filter 'killed-*.stamp' -ErrorAction SilentlyContinue)) {
        try { $o = Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json } catch { continue }
        if ($Account -and $o.Account -and $o.Account -ne 'none' -and $o.Account -ne $Account) { continue }
        if (-not $Account -and $o.Account -eq 'none') { continue }
        $left = [double]$o.ReleaseSeconds - ($NowUtc - [datetime]::Parse($o.KilledUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)).TotalSeconds
        if ($left -gt $best) { $best = $left; $from = [string]$o.Step }
    }
    return @{ Seconds = $best; From = $from }
}

function Invoke-GateSteps {
    <#
      Runs one lane's steps one after the other and returns one result object per row: Lane, Step, PlanName, Ok, Detail, Seconds, Group, Run, FrameOnly, Over75, Line.
      A step that throws becomes a FAIL row with the exception type (P4-R5); the other steps still run. A step's Env (name -> value) is set for its process and put back.
    #>
    param([object[]] $LaneSteps, [string] $Root, [string] $LogDir, [string] $GateLog)
    $ErrorActionPreference = 'Stop'
    function Say([string] $Line) {
        try { Add-Content -LiteralPath $GateLog -Value $Line -Encoding utf8 }
        catch { Start-Sleep -Milliseconds 150; try { Add-Content -LiteralPath $GateLog -Value $Line -Encoding utf8 } catch { Write-Warning "gate.log write failed ($($_.Exception.GetType().Name))" } }
    }
    $results = [System.Collections.Generic.List[object]]::new()
    foreach ($s in @($LaneSteps)) {
        $account = if ($s.PSObject.Properties['Account'] -and $null -ne $s.Account) { [string]$s.Account } else { '' }
        $release = Get-GateReleaseWait -LogDir $LogDir -Account $account
        if ($release.Seconds -gt 0) {
            Say ('[gate] {0} {1} WAIT {2:0} s: ACE still holds the character of account "{3}" from the killed step "{4}" (session timeout plus log off); a login now would be refused with 0x0D' -f $s.Lane, $s.Name, $release.Seconds, $account, $release.From)
            Start-Sleep -Milliseconds ([int][math]::Ceiling($release.Seconds * 1000))
        }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $rows = @()
        $saved = @{}
        try {
            $log = Join-Path $LogDir ("{0}-{1}.log" -f $s.Lane, $s.Name)
            $err = Join-Path $LogDir ("{0}-{1}.err" -f $s.Lane, $s.Name)
            Say "[gate] $($s.Lane) $($s.Name) START"
            Remove-Item -LiteralPath (Join-Path $LogDir ("{0}-{1}.trx" -f $s.Lane, $s.Name)) -Force -ErrorAction SilentlyContinue   # a trx left by an earlier run must not be read as this step's
            if ($s.OutDir) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $s.OutDir) | Out-Null }
            $envSet = if ($s.PSObject.Properties['Env'] -and $s.Env) { $s.Env } else { @{} }
            foreach ($k in @($envSet.Keys)) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, [string]$envSet[$k]) }
            $stepArgs = @($s.Arguments)
            # Every dotnet test step keeps its whole console output (the .log, detailed) and a trx beside it in the gate folder.
            if ($s.Kind -eq 'test' -and $stepArgs.Count -gt 0 -and $stepArgs[0] -eq 'test') {
                $stepArgs += @('--logger', ("trx;LogFileName={0}-{1}.trx" -f $s.Lane, $s.Name), '--results-directory', $LogDir)
            }
            try {
                # The child's stdin is an empty file (rfix-c, fault 2): inside a Start-Job lane the inherited stdin is the job's pipe, and every native command the child runs (smoke's git calls
                # for summary.json) then waits on it for ever. Reproduced with a stub; the test pins it.
                $stdin = Join-Path $LogDir 'empty-stdin.txt'
                if (-not (Test-Path -LiteralPath $stdin)) { New-Item -ItemType File -Force -Path $stdin | Out-Null }
                $p = Start-Process -FilePath $s.Exe -ArgumentList @($stepArgs | ForEach-Object { if ($_ -match '[\s&|()]') { '"' + $_ + '"' } else { $_ } }) -WorkingDirectory $Root -NoNewWindow -PassThru `
                    -RedirectStandardInput $stdin -RedirectStandardOutput $log -RedirectStandardError $err
                $timedOut = $false
                if (-not $p.WaitForExit($s.TimeoutSeconds * 1000)) {
                    $p.Kill($true); $p.WaitForExit(); $code = 124; $timedOut = $true
                    $relSecs = if ($s.PSObject.Properties['ReleaseSeconds'] -and $s.ReleaseSeconds) { [double]$s.ReleaseSeconds } else { 80.0 }
                    $stamp = [pscustomobject]@{ Account = $account; Step = "$($s.Lane) $($s.Name)"; ReleaseSeconds = $relSecs; KilledUtc = [datetime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture) }
                    Set-Content -LiteralPath (Join-Path $LogDir ("killed-{0}-{1}.stamp" -f $s.Lane, $s.Name)) -Value ($stamp | ConvertTo-Json -Compress) -Encoding utf8
                    Say "[gate] $($s.Lane) $($s.Name) KILLED by its timeout: the next step on account '$account' waits up to $relSecs s for ACE to release the character"
                }
                else { $code = $p.ExitCode }
            }
            finally { foreach ($k in @($saved.Keys)) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
            $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
            if ($null -eq $text) { $text = '' }
            if ($timedOut) {
                # A step over its limit is a FAIL naming the step and the limit; its partial output is not judged.
                $rows = @(New-GateRow $s.Name $false "TIMED OUT: step '$($s.Lane) $($s.Name)' ran past its timeout of $($s.TimeoutSeconds) s and was killed (exit 124); partial log $([IO.Path]::GetFileName($log))")
            }
            elseif ($s.Kind -eq 'test' -or $s.Kind -eq 'build') {
                $passed = 0; $failed = 0; $skipped = 0
                if ($s.Kind -eq 'test') {
                    $counts = Get-TestCounts -Text $text -TrxPath (Join-Path $LogDir ("{0}-{1}.trx" -f $s.Lane, $s.Name))
                    $passed = $counts.Passed; $failed = $counts.Failed; $skipped = $counts.Skipped
                }
                if ($s.Kind -eq 'build') {
                    $ok = $code -eq 0 -and $text -match '\b0 Warning\(s\)' -and $text -match '\b0 Error\(s\)'
                    $detail = "exit $code" + $(if ($text -match '(\d+) Warning\(s\)') { " warnings=$($Matches[1])" } else { '' })
                }
                else {
                    $ok = $code -eq 0 -and $failed -eq 0 -and $passed -gt 0
                    $logins = [regex]::Matches($text, '\[live-session\] logged .+ in on ').Count
                    $names = @([regex]::Matches($text, '(?m)^\s*Failed\s+(\S+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
                    $nameText = if (-not $ok -and $names.Count -gt 0) { ' failed tests: ' + (($names | Select-Object -First 5) -join ', ') + $(if ($names.Count -gt 5) { " (+$($names.Count - 5) more)" } else { '' }) } else { '' }
                    $detail = "passed=$passed failed=$failed skipped=$skipped exit=$code" + $(if ($logins -gt 0) { " logins=$logins" } else { '' }) + $(if ($passed -eq 0 -and $code -eq 0) { ' (no test passed: the filter matched nothing, or every test skipped)' } else { '' }) + $nameText + $(if (-not $ok) { " log=$([IO.Path]::GetFileName($log))" } else { '' })
                }
                $rows = @(New-GateRow $s.Name ([bool]$ok) $detail)
            }
            else {
                $json = Join-Path $s.OutDir 'rows.json'
                $smokeRows = @()
                if (Test-Path -LiteralPath $json) { try { $smokeRows = @(Get-Content -LiteralPath $json -Raw | ConvertFrom-Json) } catch { $smokeRows = @() } }
                $summary = $null
                $sj = Join-Path $s.OutDir 'summary.json'
                if (Test-Path -LiteralPath $sj) { try { $summary = Get-Content -LiteralPath $sj -Raw | ConvertFrom-Json } catch { $summary = $null } }
                $outDir = $s.OutDir
                $reader = { param($r) $lp = Join-Path $outDir $r.Log; if (Test-Path -LiteralPath $lp) { Get-Content -LiteralPath $lp -Raw } else { '' } }.GetNewClosure()
                $rows = @(Get-SmokeStepRows -StepName $s.Name -Code $code -SmokeRows $smokeRows -Summary $summary -LogPath $log -ReadLog $reader)
            }
        }
        catch {
            $rows = @(New-GateRow $s.Name $false "the step threw $($_.Exception.GetType().Name)")
        }
        $wall = $timer.Elapsed.TotalSeconds
        foreach ($row in $rows) {
            $secs = if ($row.Seconds -gt 0) { $row.Seconds } else { $wall }
            $line = '[gate] {0} {1} {2} {3} ({4:0}s)' -f $s.Lane, $row.Name, $(if ($row.Ok) { 'PASS' } else { 'FAIL' }), $row.Detail, $secs
            Say $line
            $results.Add([pscustomobject]@{ Lane = $s.Lane; Step = $row.Name; PlanName = $s.Name; Ok = $row.Ok; Detail = $row.Detail; Seconds = $secs; Group = $s.Group; Run = $s.Run; FrameOnly = $row.FrameOnly; Over75 = $row.Over75; Line = $line })
        }
    }
    return @($results)
}

function Start-GateLaneJob {
    <# One lane as a job (the parallel phase). The step array is passed as the first argument itself: a leading unary comma would wrap it one level too deep (P4-R1). #>
    param([string] $Lane, [object[]] $Steps, [string] $Root, [string] $LogDir, [string] $GateLog)
    $module = $PSCommandPath
    $body = { param($Module, $LaneSteps, $Root, $LogDir, $GateLog) Import-Module $Module -Force; Invoke-GateSteps -LaneSteps $LaneSteps -Root $Root -LogDir $LogDir -GateLog $GateLog }
    return Start-Job -Name "gate-$Lane" -ScriptBlock $body -ArgumentList $module, $Steps, $Root, $LogDir, $GateLog
}

function Receive-GateLaneJob {
    <# Waits for a lane job and returns its result rows. A planned step with no row (the job died, was killed or skipped it) becomes a FAIL row naming the job's state (P4-R5). #>
    param($Job, [object[]] $Steps)
    $errs = @()
    $out = @(Receive-Job -Job $Job -Wait -ErrorAction Continue -ErrorVariable errs)
    $state = [string]$Job.State
    $reason = ''
    try { $r = $Job.ChildJobs[0].JobStateInfo.Reason; if ($r) { $reason = " ($($r.GetType().Name))" } } catch { }
    if (-not $reason -and $errs.Count -gt 0) { $reason = " ($($errs[0].Exception.GetType().Name))" }
    Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
    $rows = @($out | Where-Object { $_ -is [psobject] -and $_.PSObject.Properties['Lane'] -and $_.PSObject.Properties['PlanName'] })
    $missing = Get-MissingGateRows -Steps $Steps -Rows $rows -Why "the lane job $state$reason"
    return @($rows) + @($missing)
}

function Get-MissingGateRows {
    <# A FAIL row for every planned step that produced no row. The gate is never ok with a step that did not report. #>
    param([object[]] $Steps, [object[]] $Rows, [string] $Why)
    $made = [System.Collections.Generic.List[object]]::new()
    foreach ($s in @($Steps)) {
        if (@($Rows | Where-Object { $_.Lane -eq $s.Lane -and $_.PlanName -eq $s.Name }).Count -gt 0) { continue }
        $detail = "no result: $Why"
        $made.Add([pscustomobject]@{ Lane = $s.Lane; Step = $s.Name; PlanName = $s.Name; Ok = $false; Detail = $detail; Seconds = 0; Group = $s.Group; Run = $s.Run; FrameOnly = $false; Over75 = $false
                Line = "[gate] $($s.Lane) $($s.Name) FAIL $detail (0s)" })
    }
    return @($made)
}

Export-ModuleMember -Function Get-GateReleaseWait, Test-FrameOnlyText, Get-FrameMaxMs, Get-TestCounts, Get-SmokeStepRows, Invoke-GateSteps, Start-GateLaneJob, Receive-GateLaneJob, Get-MissingGateRows
