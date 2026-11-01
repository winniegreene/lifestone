<#
.SYNOPSIS
    The soak's outside loop (M5 item 0 c3, DoD 13): every few minutes one line about the machine, from outside the game.

.DESCRIPTION
    A foreground loop the lead starts in the background beside a run. Every -IntervalSeconds (default 300) it writes one row to
    a CSV under cache\ (-OutFile) and one line to the console:
      * the working set, private bytes, handles and threads of the Godot, ACE.Server and mariadbd processes, found by name
        (a process that is absent reads n/a; with several of one name the largest working set is reported);
      * the sizes of the Godot user-data logs folder (default: the isolated one tools\godot.ps1 uses), server\runtime\logs
        and server\runtime\mariadb-data (a folder that is absent reads n/a);
      * the free disk of the repository's drive.
    It is read-only: it changes nothing, starts nothing, kills nothing and prints no file contents (sizes only). The sibling
    of the in-game [soak] line (docs/session.md, the soak line).

.PARAMETER IntervalSeconds
    Seconds between two samples (default 300).

.PARAMETER DurationSeconds
    Stop after this many seconds (default 0 = until the shell is stopped).

.PARAMETER Once
    One sample, then exit.

.PARAMETER OutFile
    The CSV (appended to; the header is written when the file is new). Default cache\soak\watch-<UTC stamp>.csv under the repository.

.PARAMETER GodotLogsDirectory
    The Godot user-data logs folder to measure. Default: the isolated folder tools\godot.ps1 -IsolateUserData uses
    (cache\godot\roaming\Godot\app_userdata\Lifestone\logs).

.PARAMETER ServerLogsDirectory
    ACE's logs folder (default server\runtime\logs).

.PARAMETER DatabaseDirectory
    MariaDB's data folder (default server\runtime\mariadb-data).

.PARAMETER RunFolder
    The soak's run folder (M5 7 G2). The alarm pass writes <RunFolder>\ALARM.txt there (default: the folder of -OutFile).

.PARAMETER GameLog
    The game's live log the alarm pass reads the tail of (default: the newest lifestone*.log in -GodotLogsDirectory).

.PARAMETER Since
    UTC time (yyyy-MM-ddTHH:mm:ssZ): log lines older than this are ignored by the alarm pass (default: when this watcher started, so an older run's log raises nothing).

.PARAMETER ExpectGame
    The alarm pass treats a missing Godot process as "the game process ended" even when this watcher never saw one (the soak starts the watcher before the game, so it does not pass this; it is the test seam).

.PARAMETER GodotProcessPattern
    Test seam: the process-name pattern of the game (default Godot*).

.NOTES
    The alarm pass (M5 7 G2, edge audit item 11). Each interval, after the sample, it reads the tail of the game's log and writes one line per NEW alarm to <RunFolder>\ALARM.txt ("<UTC time> ALARM <text>") and to the console
    ("[soak-watch] ALARM <text>"): the game process ended; the autopilot was interrupted (the host's "interrupted by" line, or an ap= field on the [soak] line); kills stalled for 15 minutes; a death; 3 or more trips in 30 minutes;
    free_slots under 3; a reconnect; Godot's working set over 3 GB. A state alarm (stall, trips, slots, memory) is raised once per time it starts holding. The lead's Monitor waits on ALARM.txt. Read-only apart from that file.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 86400)] [int] $IntervalSeconds = 300,
    [ValidateRange(0, 31536000)] [int] $DurationSeconds = 0,
    [switch] $Once,
    [string] $OutFile = '',
    [string] $GodotLogsDirectory = '',
    [string] $ServerLogsDirectory = '',
    [string] $DatabaseDirectory = '',
    [string] $RunFolder = '',
    [string] $GameLog = '',
    [string] $Since = '',
    [switch] $ExpectGame,
    [string] $GodotProcessPattern = 'Godot*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) { $OutFile = Join-Path $repoRoot ('cache\soak\watch-{0}.csv' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')) }
if (-not $GodotLogsDirectory) { $GodotLogsDirectory = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs' }
if (-not $ServerLogsDirectory) { $ServerLogsDirectory = Join-Path $repoRoot 'server\runtime\logs' }
if (-not $DatabaseDirectory) { $DatabaseDirectory = Join-Path $repoRoot 'server\runtime\mariadb-data' }
$OutFile = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutFile)
if (-not $RunFolder) { $RunFolder = Split-Path -Parent $OutFile }
$RunFolder = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($RunFolder)
$alarmFile = Join-Path $RunFolder 'ALARM.txt'
$ci = [Globalization.CultureInfo]::InvariantCulture
$sinceUtc = if ($Since) { [DateTime]::Parse($Since, $ci, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal) } else { [DateTime]::UtcNow }

# The processes by name: a Godot editor or game (including the _console wrapper), ACE, MariaDB.
$watched = [ordered]@{
    godot   = $GodotProcessPattern
    ace     = 'ACE.Server'
    mariadb = 'mariadbd'
}

function Get-ProcessReading {
    <# One process group as four numbers, or four 'n/a'. The largest working set when several share the name. #>
    param([string] $Pattern)
    $found = @(Get-Process -Name $Pattern -ErrorAction SilentlyContinue | Sort-Object -Property WorkingSet64 -Descending)
    if ($found.Count -eq 0) { return @('n/a', 'n/a', 'n/a', 'n/a') }
    $p = $found[0]
    return @(
        [math]::Round($p.WorkingSet64 / 1MB, 1).ToString([Globalization.CultureInfo]::InvariantCulture),
        [math]::Round($p.PrivateMemorySize64 / 1MB, 1).ToString([Globalization.CultureInfo]::InvariantCulture),
        [string]$p.HandleCount,
        [string]$p.Threads.Count)
}

function Get-FolderMegabytes {
    <# The size of a folder's files in MiB (sizes only; no file is opened), or 'n/a' when the folder is absent. #>
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return 'n/a' }
    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { $sum = 0 }
    return [math]::Round($sum / 1MB, 1).ToString([Globalization.CultureInfo]::InvariantCulture)
}

function Get-FreeDiskGigabytes {
    <# Free space of the drive holding the repository, in GiB. #>
    $root = [IO.Path]::GetPathRoot($repoRoot)
    return [math]::Round(([IO.DriveInfo]::new($root)).AvailableFreeSpace / 1GB, 2).ToString([Globalization.CultureInfo]::InvariantCulture)
}

$columns = [System.Collections.Generic.List[string]]::new()
$columns.Add('time_utc')
foreach ($name in $watched.Keys) { foreach ($field in 'ws_mb', 'private_mb', 'handles', 'threads') { $columns.Add("${name}_$field") } }
foreach ($column in 'godot_logs_mb', 'ace_logs_mb', 'db_mb', 'free_gb') { $columns.Add($column) }

function Measure-Sample {
    $values = [System.Collections.Generic.List[string]]::new()
    $values.Add([DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'"))
    foreach ($name in $watched.Keys) { foreach ($v in (Get-ProcessReading -Pattern $watched[$name])) { $values.Add($v) } }
    $values.Add((Get-FolderMegabytes -Path $GodotLogsDirectory))
    $values.Add((Get-FolderMegabytes -Path $ServerLogsDirectory))
    $values.Add((Get-FolderMegabytes -Path $DatabaseDirectory))
    $values.Add((Get-FreeDiskGigabytes))
    return , $values
}

# --- The alarm pass (M5 7 G2). ---
$AlarmStallMinutes = 15
$AlarmTripWindowMinutes = 30
$AlarmTripCount = 3
$AlarmFreeSlots = 3
$AlarmGodotWsMb = 3072
$linePattern = [regex]'^(?<t>\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z) (?<lvl>[A-Z]+)\s+\[(?<cat>[^\]]+)\] (?<msg>.*)$'
$raised = @{}          # key -> $true: an alarm already written (events by key, states while they hold)
$godotSeen = $false

function Get-LogTail {
    <# The last lines of the newest game log (about 6 MiB at most), read shared so the game keeps writing. #>
    $path = $GameLog
    if (-not $path) {
        $newest = Get-ChildItem -LiteralPath $GodotLogsDirectory -File -Filter 'lifestone*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($newest) { $path = $newest.FullName }
    }
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $length = $stream.Length
        $take = [math]::Min($length, 6MB)
        $null = $stream.Seek(-$take, [IO.SeekOrigin]::End)
        $reader = [IO.StreamReader]::new($stream)
        $text = $reader.ReadToEnd()
    }
    finally { $stream.Dispose() }
    $lines = @($text -split "`n")
    if ($take -lt $length -and $lines.Count -gt 1) { $lines = $lines[1..($lines.Count - 1)] } # the first line is cut
    return $lines
}

function Get-FieldMap([string] $Message) {
    $map = @{}
    foreach ($m in [regex]::Matches($Message, '(?<k>[A-Za-z_][A-Za-z0-9_]*)=(?<v>\S+)')) { $map[$m.Groups['k'].Value] = $m.Groups['v'].Value }
    return $map
}

function ConvertTo-Number($Text) {
    $v = 0.0
    if ($null -ne $Text -and [double]::TryParse([string]$Text, [Globalization.NumberStyles]::Float, $ci, [ref]$v)) { return $v }
    return $null
}

function Add-Alarm([string] $Key, [string] $Text, [bool] $Holding = $true) {
    if (-not $Holding) { $raised.Remove($Key); return }
    if ($raised.ContainsKey($Key)) { return }
    $raised[$Key] = $true
    $stamp = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    if (-not (Test-Path -LiteralPath $RunFolder)) { New-Item -ItemType Directory -Force -Path $RunFolder | Out-Null }
    Add-Content -LiteralPath $alarmFile -Value "$stamp ALARM $Text" -Encoding utf8
    Write-Host "[soak-watch] ALARM $Text"
}

function Invoke-AlarmPass($Sample) {
    # the process: seen once, then gone (or, under -ExpectGame, absent)
    $godotWs = ConvertTo-Number $Sample[$columns.IndexOf('godot_ws_mb')]
    if ($null -ne $godotWs) { $script:godotSeen = $true }
    Add-Alarm 'game-ended' 'the game process ended (no Godot process is running)' (($null -eq $godotWs) -and ($script:godotSeen -or $ExpectGame))
    Add-Alarm 'godot-memory' ("Godot's working set is {0:0} MB, over {1} MB" -f $godotWs, $AlarmGodotWsMb) (($null -ne $godotWs) -and $godotWs -gt $AlarmGodotWsMb)

    $soak = [System.Collections.Generic.List[object]]::new()
    foreach ($raw in (Get-LogTail)) {
        if (-not ($raw.Contains('[soak] ws_mb=') -or $raw.Contains('autopilot host: interrupted by') -or $raw.Contains('reconnect: the session left'))) { continue }
        $m = $linePattern.Match($raw.TrimEnd("`r"))
        if (-not $m.Success) { continue }
        $time = [DateTime]::ParseExact($m.Groups['t'].Value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", $ci, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
        if ($time -lt $sinceUtc) { continue }
        $msg = $m.Groups['msg'].Value
        if ($m.Groups['cat'].Value -eq 'soak' -and $msg.StartsWith('ws_mb=')) {
            $f = Get-FieldMap $msg
            $soak.Add(@{ T = $time; F = $f })
            if ($f.ContainsKey('ap') -and $f['ap'] -match 'interrupt|halt') { Add-Alarm "interrupt-soak-$($f['ap'])" "the [soak] line shows ap=$($f['ap'])" }
        }
        elseif ($msg.StartsWith('autopilot host: interrupted by')) { Add-Alarm ('interrupt-' + $m.Groups['t'].Value) ('the autopilot was interrupted at {0}: {1}' -f $m.Groups['t'].Value, $msg.Substring(0, [math]::Min($msg.Length, 80))) }
        elseif ($msg.StartsWith('reconnect: the session left the world')) { Add-Alarm ('reconnect-' + $m.Groups['t'].Value) ('a reconnect at {0}: {1}' -f $m.Groups['t'].Value, $msg.Substring(0, [math]::Min($msg.Length, 100))) }
    }
    if ($soak.Count -eq 0) { return }
    $last = $soak[$soak.Count - 1]
    $lastT = $last.T

    # a death: the largest deaths= seen is raised again each time it grows
    $deaths = ($soak | ForEach-Object { ConvertTo-Number $_.F['deaths'] } | Where-Object { $null -ne $_ } | Measure-Object -Maximum).Maximum
    if ($null -ne $deaths -and $deaths -gt 0) { Add-Alarm "deaths-$deaths" "a death: deaths=$deaths" }

    # a reconnect counted by the [soak] line itself
    $reconnects = ($soak | ForEach-Object { ConvertTo-Number $_.F['reconnects'] } | Where-Object { $null -ne $_ } | Measure-Object -Maximum).Maximum
    if ($null -ne $reconnects -and $reconnects -gt 0) { Add-Alarm "reconnects-$reconnects" "a reconnect: reconnects=$reconnects" }

    # kills stalled for 15 minutes: the window is covered by the lines and the kills did not move
    $window = @($soak | Where-Object { $_.T -ge $lastT.AddMinutes(-$AlarmStallMinutes) })
    $covered = $soak[0].T -le $lastT.AddMinutes(-$AlarmStallMinutes)
    $killValues = @($window | ForEach-Object { ConvertTo-Number $_.F['kills'] } | Where-Object { $null -ne $_ })
    $stalled = $covered -and $killValues.Count -ge 2 -and (($killValues | Measure-Object -Minimum).Minimum -eq ($killValues | Measure-Object -Maximum).Maximum)
    $stallText = if ($killValues.Count -gt 0) { "kills stalled for $AlarmStallMinutes minutes (kills=$($killValues[-1]) since $($window[0].T.ToString("HH:mm:ss'Z'")))" } else { 'kills stalled' }
    Add-Alarm 'kills-stalled' $stallText ([bool]$stalled)

    # 3 trips in 30 minutes
    $tripWindow = @($soak | Where-Object { $_.T -ge $lastT.AddMinutes(-$AlarmTripWindowMinutes) })
    $tripFirst = ConvertTo-Number $tripWindow[0].F['trips']
    $tripLast = ConvertTo-Number $last.F['trips']
    $looping = ($null -ne $tripFirst) -and ($null -ne $tripLast) -and (($tripLast - $tripFirst) -ge $AlarmTripCount)
    $tripText = if ($looping) { '{0} trips in {1} minutes (trips {2} to {3})' -f ($tripLast - $tripFirst), $AlarmTripWindowMinutes, $tripFirst, $tripLast } else { 'trips' }
    Add-Alarm 'trips' $tripText ([bool]$looping)

    # free slots
    $slots = ConvertTo-Number $last.F['free_slots']
    Add-Alarm 'free-slots' "free_slots=$slots, under $AlarmFreeSlots" (($null -ne $slots) -and $slots -lt $AlarmFreeSlots)
}

$outDirectory = Split-Path -Parent $OutFile
if ($outDirectory -and -not (Test-Path -LiteralPath $outDirectory)) { New-Item -ItemType Directory -Force -Path $outDirectory | Out-Null }
if (-not (Test-Path -LiteralPath $OutFile)) { Set-Content -LiteralPath $OutFile -Value ($columns -join ',') -Encoding utf8 }

$started = [DateTime]::UtcNow
while ($true) {
    $sample = Measure-Sample
    Add-Content -LiteralPath $OutFile -Value ($sample -join ',') -Encoding utf8
    $pairs = for ($i = 0; $i -lt $columns.Count; $i++) { '{0}={1}' -f $columns[$i], $sample[$i] }
    Write-Host ('[soak-watch] ' + ($pairs -join ' '))
    try { Invoke-AlarmPass $sample }
    catch { Write-Host "[soak-watch] the alarm pass failed ($($_.Exception.GetType().Name)): $($_.Exception.Message)" }
    if ($Once) { break }
    if ($DurationSeconds -gt 0 -and ([DateTime]::UtcNow - $started).TotalSeconds + $IntervalSeconds -gt $DurationSeconds) { break }
    Start-Sleep -Seconds $IntervalSeconds
}
