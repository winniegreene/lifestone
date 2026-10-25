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
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 86400)] [int] $IntervalSeconds = 300,
    [ValidateRange(0, 31536000)] [int] $DurationSeconds = 0,
    [switch] $Once,
    [string] $OutFile = '',
    [string] $GodotLogsDirectory = '',
    [string] $ServerLogsDirectory = '',
    [string] $DatabaseDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) { $OutFile = Join-Path $repoRoot ('cache\soak\watch-{0}.csv' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')) }
if (-not $GodotLogsDirectory) { $GodotLogsDirectory = Join-Path $repoRoot 'cache\godot\roaming\Godot\app_userdata\Lifestone\logs' }
if (-not $ServerLogsDirectory) { $ServerLogsDirectory = Join-Path $repoRoot 'server\runtime\logs' }
if (-not $DatabaseDirectory) { $DatabaseDirectory = Join-Path $repoRoot 'server\runtime\mariadb-data' }
$OutFile = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutFile)

# The processes by name: a Godot editor or game (including the _console wrapper), ACE, MariaDB.
$watched = [ordered]@{
    godot   = 'Godot*'
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

$outDirectory = Split-Path -Parent $OutFile
if ($outDirectory -and -not (Test-Path -LiteralPath $outDirectory)) { New-Item -ItemType Directory -Force -Path $outDirectory | Out-Null }
if (-not (Test-Path -LiteralPath $OutFile)) { Set-Content -LiteralPath $OutFile -Value ($columns -join ',') -Encoding utf8 }

$started = [DateTime]::UtcNow
while ($true) {
    $sample = Measure-Sample
    Add-Content -LiteralPath $OutFile -Value ($sample -join ',') -Encoding utf8
    $pairs = for ($i = 0; $i -lt $columns.Count; $i++) { '{0}={1}' -f $columns[$i], $sample[$i] }
    Write-Host ('[soak-watch] ' + ($pairs -join ' '))
    if ($Once) { break }
    if ($DurationSeconds -gt 0 -and ([DateTime]::UtcNow - $started).TotalSeconds + $IntervalSeconds -gt $DurationSeconds) { break }
    Start-Sleep -Seconds $IntervalSeconds
}
