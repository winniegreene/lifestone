<#
.SYNOPSIS
    The soak's report (M5 item 7, DoD 13): reads a run folder tools\soak.ps1 made and writes report.txt, report-only.

.DESCRIPTION
    Read-only over the run folder's inputs (lifestone.log and lifestone.1.log, the [soak] lines with their 43 fields by name; the
    autopilot cycle, done and host lines; the reconnect lines; watch.csv from tools\soak-watch.ps1). Writes <run folder>\report.txt and
    the same lines to the console. Every verdict is a fact to read, not a pass or fail (D54: report-only in this first soak).
    A value with no data reads n/a, never a guess.

    Lines (key=value):
      done_ok          the "autopilot done" line's ok (yes|no)
      cycles           the done line's cycles; cycle_lines = the "autopilot cycle" lines with a state other than stopped, numbered 1..N;
                       accounted=yes when they agree
      ws_slope_mb_per_h  least squares of ws_mb against time over the [soak] lines after the first hour of the run
      objects_max in_cell_max light_rows_max (light_rows_ok=yes when the maximum is under 256)
      godot_errors     the last [soak] line's value
      reconnects       the count of "reconnect: the session left the world" lines, each with its cause (phase, state, cycle)
      p99_first_ms p99_last_ms  the median of the [soak] lines' frame_p99_ms over the first hour and over the last hour
      kills deaths interrupted resumed  the done line's (else the last [soak] / host line's)
      hp_pct_avg hp_pct_min sp_pct_avg sp_pct_min mp_pct_avg mp_pct_min hit_max  the [soak] lines' hp_pct sp_pct mp_pct (the mage's vitals as a share of max): their average and minimum; hit_max the largest (M5 7 F16)
      log_mb minutes soak_lines  and the watch's largest working sets (godot, ace, mariadb), the smallest free disk and the rows

.PARAMETER RunFolder
    The run folder (cache\soak\<stamp>).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RunFolder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ci = [Globalization.CultureInfo]::InvariantCulture
$RunFolder = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($RunFolder)
if (-not (Test-Path -LiteralPath $RunFolder -PathType Container)) { Write-Host "[soak-report] REFUSED: run folder not found: $RunFolder" -ForegroundColor Red; exit 1 }

function Format-Num($value, [int] $digits = 1) {
    if ($null -eq $value) { return 'n/a' }
    return ([math]::Round([double]$value, $digits)).ToString($ci)
}

function ConvertTo-Real([string] $text) {
    $v = 0.0
    if ([double]::TryParse($text, [Globalization.NumberStyles]::Float, $ci, [ref]$v)) { return $v }
    return $null
}

function Get-Median($values) {
    $values = @($values | Where-Object { $null -ne $_ })   # an empty window is $null on the way in (strict mode has no .Count on it)
    if ($values.Count -eq 0) { return $null }
    $sorted = @($values | Sort-Object)
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

function Get-FieldMap([string] $message) {
    $map = @{}
    foreach ($m in [regex]::Matches($message, '(?<k>[A-Za-z_][A-Za-z0-9_]*)=(?<v>"[^"]*"|\S+)')) { $map[$m.Groups['k'].Value] = $m.Groups['v'].Value.Trim('"') }
    return $map
}

# --- Inputs: the log (older copy first), then the watch CSV. ---
# M5 7 F17: --log-keep=0 keeps every rolled file: lifestone.N.log, the oldest with the highest number, read first, then lifestone.log.
$rolledNames = @(Get-ChildItem -LiteralPath $RunFolder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^lifestone\.(\d+)\.log$' } | Sort-Object { [int]($_.Name -replace '^lifestone\.(\d+)\.log$', '$1') } -Descending | ForEach-Object { $_.Name })
$logFiles = @(@($rolledNames) + @('lifestone.log') | ForEach-Object { Join-Path $RunFolder $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
$linePattern = [regex]'^(?<t>\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z) (?<lvl>[A-Z]+)\s+\[(?<cat>[^\]]+)\] (?<msg>.*)$'
$soak = [System.Collections.Generic.List[object]]::new()      # @{ T; F }
$cycleLines = [System.Collections.Generic.List[object]]::new() # @{ N; State }
$reconnectCauses = [System.Collections.Generic.List[string]]::new()
$doneFields = $null
$hostFields = $null
$rolls = 0   # the highest rolls=<n> of the "log rolled" lines (the roll's own first line in the new file)
$logBytes = 0L
foreach ($file in $logFiles) {
    $logBytes += (Get-Item -LiteralPath $file).Length
    foreach ($raw in [IO.File]::ReadLines($file)) {
        # A cheap prefilter first: the log is large and almost every line is none of these.
        if (-not ($raw.Contains('[soak] ws_mb=') -or $raw.Contains('autopilot cycle n=') -or $raw.Contains('autopilot done ') -or $raw.Contains('reconnect: the session left') -or $raw.Contains('autopilot host:') -or $raw.Contains('log rolled'))) { continue }
        $m = $linePattern.Match($raw)
        if (-not $m.Success) { continue }
        $msg = $m.Groups['msg'].Value
        $time = [DateTime]::ParseExact($m.Groups['t'].Value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", $ci, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
        if ($m.Groups['cat'].Value -eq 'log' -and $msg.StartsWith('log rolled')) {
            $rf = Get-FieldMap $msg
            $rn = 0
            if ($rf.ContainsKey('rolls') -and [int]::TryParse($rf['rolls'], [ref]$rn) -and $rn -gt $rolls) { $rolls = $rn }
        }
        elseif ($m.Groups['cat'].Value -eq 'soak' -and $msg.StartsWith('ws_mb=')) {
            $soak.Add(@{ T = $time; F = (Get-FieldMap $msg) })
        }
        elseif ($msg.StartsWith('autopilot cycle n=')) {
            $f = Get-FieldMap $msg
            $cycleLines.Add(@{ N = $f['n']; State = $f['state'] })
        }
        elseif ($msg.StartsWith('autopilot done ')) { $doneFields = Get-FieldMap $msg }
        elseif ($msg.StartsWith('reconnect: the session left the world')) {
            $r = [regex]::Match($msg, 'phase (?<phase>[^)]*)\) in the (?<state>.*?) state, cycle (?<cycle>\d+)')
            $cause = if ($r.Success) { "phase=$($r.Groups['phase'].Value -replace '\s', '_') state=$($r.Groups['state'].Value -replace '\s', '_') cycle=$($r.Groups['cycle'].Value)" } else { 'cause=unparsed' }
            $reconnectCauses.Add("at=$($time.ToString("HH:mm:ss'Z'")) $cause")
        }
        elseif ($msg.StartsWith('autopilot host:') -and $msg -match 'interrupted=\d+ resumed=\d+') { $hostFields = Get-FieldMap $msg }
    }
}
$haveLog = $logFiles.Count -gt 0
# 7-R5 (F17): every roll that left a file keeps the run whole (the older files are read first, so the first hour is intact); rolls beyond the files present (--log-keep below the rolls) lost the oldest lines, so the first-hour fields would measure a later window.
$firstHourLost = $rolls -gt $rolledNames.Count # F17: lost only when more files rolled than the run folder holds (--log-keep=0 keeps them all)

function Get-SoakValues([string] $name, $lines) {
    return @($lines | ForEach-Object { ConvertTo-Real $_.F[$name] } | Where-Object { $null -ne $_ })
}
function Get-Max($values) { if (@($values).Count -eq 0) { return $null } return ($values | Measure-Object -Maximum).Maximum }

# --- The verdict fields. ---
$out = [System.Collections.Generic.List[string]]::new()
$firstT = $null; $lastT = $null
if ($soak.Count -gt 0) { $firstT = $soak[0].T; $lastT = $soak[$soak.Count - 1].T }
$minutes = if ($soak.Count -ge 1) { ($lastT - $firstT).TotalMinutes } else { $null }

$out.Add("[soak-report] run=$(Split-Path -Leaf $RunFolder) rehearsal=$(if ((Split-Path -Leaf $RunFolder) -like '*-rehearsal') { 'yes' } else { 'no' }) minutes=$(Format-Num $minutes) soak_lines=$(if ($haveLog) { $soak.Count } else { 'n/a' }) rolled=$(if ($haveLog) { $rolls } else { 'n/a' }) first_hour=$(if (-not $haveLog) { 'n/a' } elseif ($firstHourLost) { 'partial' } else { 'whole' }) log_mb=$(if ($haveLog) { Format-Num ($logBytes / 1MB) 2 } else { 'n/a' })")

$doneOk = if ($doneFields -and $doneFields.ContainsKey('ok')) { $doneFields['ok'] } else { 'n/a' }
$doneStop = if ($doneFields -and $doneFields.ContainsKey('stop')) { $doneFields['stop'] } else { 'n/a' }
$doneCycles = if ($doneFields -and $doneFields.ContainsKey('cycles')) { $doneFields['cycles'] } else { 'n/a' }
$counted = @($cycleLines | Where-Object { $_.State -ne 'stopped' })
$accounted = 'n/a'
if ($haveLog -and $doneCycles -ne 'n/a' -and -not $firstHourLost) {
    $sequential = $true
    for ($i = 0; $i -lt $counted.Count; $i++) { if ($counted[$i].N -ne "$($i + 1)") { $sequential = $false } }
    $accounted = if ($sequential -and "$($counted.Count)" -eq $doneCycles) { 'yes' } else { 'no' }
}
$out.Add("[soak-report] done_ok=$doneOk stop=$doneStop cycles=$doneCycles cycle_lines=$(if ($haveLog) { $counted.Count } else { 'n/a' }) accounted=$accounted")

$slope = $null
if ($soak.Count -gt 0 -and -not $firstHourLost) {
    $after = @($soak | Where-Object { ($_.T - $firstT).TotalHours -ge 1 -and $null -ne (ConvertTo-Real $_.F['ws_mb']) })
    if ($after.Count -ge 3 -and ($after[$after.Count - 1].T - $after[0].T).TotalSeconds -gt 0) {
        $xs = @($after | ForEach-Object { ($_.T - $firstT).TotalHours })
        $ys = @($after | ForEach-Object { ConvertTo-Real $_.F['ws_mb'] })
        $mx = ($xs | Measure-Object -Average).Average; $my = ($ys | Measure-Object -Average).Average
        $sxy = 0.0; $sxx = 0.0
        for ($i = 0; $i -lt $xs.Count; $i++) { $sxy += ($xs[$i] - $mx) * ($ys[$i] - $my); $sxx += ($xs[$i] - $mx) * ($xs[$i] - $mx) }
        if ($sxx -gt 0) { $slope = $sxy / $sxx }
    }
}
$objectsMax = Get-Max (Get-SoakValues 'objects' $soak)
$inCellMax = Get-Max (Get-SoakValues 'in_cell' $soak)
$lightMax = Get-Max (Get-SoakValues 'light_rows' $soak)
$lightOk = if ($null -eq $lightMax) { 'n/a' } elseif ($lightMax -lt 256) { 'yes' } else { 'no' }
$out.Add("[soak-report] ws_slope_mb_per_h=$(Format-Num $slope 2) (after the first hour$(if ($firstHourLost) { '; lost to the roll' })) ws_max_mb=$(Format-Num (Get-Max (Get-SoakValues 'ws_mb' $soak))) objects_max=$(Format-Num $objectsMax 0) in_cell_max=$(Format-Num $inCellMax 0) light_rows_max=$(Format-Num $lightMax 0) light_rows_ok=$lightOk")

$godotErrors = 'n/a'
if ($soak.Count -gt 0 -and $soak[$soak.Count - 1].F.ContainsKey('godot_errors')) { $godotErrors = $soak[$soak.Count - 1].F['godot_errors'] }

$p99First = $null; $p99Last = $null
if ($soak.Count -gt 0) {
    $first = @()
    if (-not $firstHourLost) { $first = @($soak | Where-Object { ($_.T - $firstT).TotalHours -lt 1 }) }
    $last = @($soak | Where-Object { ($lastT - $_.T).TotalHours -le 1 })
    $p99First = Get-Median (Get-SoakValues 'frame_p99_ms' $first)
    $p99Last = Get-Median (Get-SoakValues 'frame_p99_ms' $last)
}
$out.Add("[soak-report] godot_errors=$godotErrors p99_first_ms=$(Format-Num $p99First 2) p99_last_ms=$(Format-Num $p99Last 2) (medians of the lines' frame_p99_ms over the first and the last hour)")

$reconnectCount = if ($haveLog) { "$($reconnectCauses.Count)" } else { 'n/a' }
$out.Add("[soak-report] reconnects=$reconnectCount$(if ($haveLog -and $reconnectCauses.Count -eq 0) { ' causes=none' })")
$n = 0
foreach ($c in $reconnectCauses) { $n++; $out.Add("[soak-report] reconnect n=$n $c") }

function Get-Tally([string] $name) {
    if ($doneFields -and $doneFields.ContainsKey($name)) { return $doneFields[$name] }
    if ($name -in 'interrupted', 'resumed' -and $hostFields -and $hostFields.ContainsKey($name)) { return $hostFields[$name] }
    if ($name -in 'kills', 'deaths' -and $soak.Count -gt 0 -and $soak[$soak.Count - 1].F.ContainsKey($name)) { return $soak[$soak.Count - 1].F[$name] }
    return 'n/a'
}
$out.Add("[soak-report] kills=$(Get-Tally 'kills') deaths=$(Get-Tally 'deaths') interrupted=$(Get-Tally 'interrupted') resumed=$(Get-Tally 'resumed')")

# M5 7 F16: the mage's vitals over the run and the biggest hit (the [soak] lines' hp_pct sp_pct mp_pct hit_max, read by name; n/a when the lines lack them).
function Get-Avg($values) { if (@($values).Count -eq 0) { return $null } return ($values | Measure-Object -Average).Average }
function Get-Min($values) { if (@($values).Count -eq 0) { return $null } return ($values | Measure-Object -Minimum).Minimum }
$vitalText = foreach ($v in 'hp', 'sp', 'mp') {
    $xs = @(Get-SoakValues "${v}_pct" $soak)
    "${v}_pct_avg=$(Format-Num (Get-Avg $xs)) ${v}_pct_min=$(Format-Num (Get-Min $xs))"
}
$out.Add("[soak-report] $($vitalText -join ' ') hit_max=$(Format-Num (Get-Max (Get-SoakValues 'hit_max' $soak)) 0)")
# M5 7 F25: the mage's combat-mode changes over the run (the [soak] lines' stance_changes, read by name, the last line's count; report-only; n/a when the lines lack it).
$out.Add("[soak-report] stance_changes=$(Format-Num (Get-Max (Get-SoakValues 'stance_changes' $soak)) 0)")
# M5 7 F29 (D96): the loot and the load (the [soak] lines' loot_kept loot_left free_slots burden_pct trips (M5 7 F30: and worth_a_look), read by name, the last line's values; report-only; n/a when the lines lack them).
$lootText = foreach ($n in 'loot_kept', 'loot_left', 'free_slots', 'burden_pct', 'trips', 'worth_a_look') {
    $last = @(Get-SoakValues $n $soak) | Select-Object -Last 1
    "$n=$(Format-Num $last $(if ($n -eq 'burden_pct') { 1 } else { 0 }))"
}
$out.Add("[soak-report] $($lootText -join ' ')")
# M5 7 D134: the walkers' steering totals (the [soak] lines' turn_reversals turn_flips progress_backs swings, read by name, the last line's counts and per hour of the log; report-only; n/a when the lines lack them).
$steerHours = if ($soak.Count -gt 1) { ($lastT - $firstT).TotalHours } else { 0 }
$steerText = foreach ($n in 'turn_reversals', 'turn_flips', 'progress_backs', 'swings') {
    $total = Get-Max (Get-SoakValues $n $soak)
    $perHour = if ($null -ne $total -and $steerHours -gt 0) { Format-Num ($total / $steerHours) 0 } else { 'n/a' }
    "$n=$(Format-Num $total 0) ${n}_per_h=$perHour"
}
$out.Add("[soak-report] $($steerText -join ' ')")

# --- The outside watcher. ---
$watchFile = Join-Path $RunFolder 'watch.csv'
if (Test-Path -LiteralPath $watchFile -PathType Leaf) {
    $rows = @(Import-Csv -LiteralPath $watchFile)
    $maxOf = { param($col) Get-Max @($rows | ForEach-Object { if ($_.PSObject.Properties[$col]) { ConvertTo-Real $_.$col } } | Where-Object { $null -ne $_ }) }
    $freeMin = $null
    $free = @($rows | ForEach-Object { if ($_.PSObject.Properties['free_gb']) { ConvertTo-Real $_.free_gb } } | Where-Object { $null -ne $_ })
    if ($free.Count -gt 0) { $freeMin = ($free | Measure-Object -Minimum).Minimum }
    $out.Add("[soak-report] watch_rows=$($rows.Count) godot_ws_mb_max=$(Format-Num (& $maxOf 'godot_ws_mb')) ace_ws_mb_max=$(Format-Num (& $maxOf 'ace_ws_mb')) mariadb_ws_mb_max=$(Format-Num (& $maxOf 'mariadb_ws_mb')) db_mb_max=$(Format-Num (& $maxOf 'db_mb')) free_gb_min=$(Format-Num $freeMin 2)")
}
else {
    $out.Add('[soak-report] watch_rows=n/a godot_ws_mb_max=n/a ace_ws_mb_max=n/a mariadb_ws_mb_max=n/a db_mb_max=n/a free_gb_min=n/a')
}
if (-not $haveLog) { $out.Add('[soak-report] no lifestone.log in the run folder: every log field reads n/a') }
$out.Add('[soak-report] report-only (D54): the fields above are facts to read, not a verdict')

[IO.File]::WriteAllText((Join-Path $RunFolder 'report.txt'), (($out -join "`n") + "`n"))
foreach ($line in $out) { Write-Host $line }
exit 0
