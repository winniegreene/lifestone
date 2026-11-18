<#
.SYNOPSIS
    Gives the running autopilot one order through its steer desk and prints the report.

.DESCRIPTION
    Drops one command order in cache\avatar\<run>\inbox (the steer contract's envelope: kind command, args.line) and waits for its report in outbox\<id>.json, printing each new state until the order ends (done, failed,
    refused, expired or cancelled) or -WaitSeconds pass. The line is what the chat's orders take (docs\commands.md): trip, sell, rebuff [self|bot], quest <key>, quest-step <key> <n>, ladder, den, home, pause, resume,
    stop, status, help [command], queue, cancel [n|id], clear; a trailing 'then idle' holds her at home after it. The run must have been started with --autopilot-steer-orders (tools\climb.ps1 -SteerRun passes it).

    The order's guard names the character in status.json, so an order written for one character is never taken by another. Reads and writes files under cache\avatar\<run> only; starts nothing, never touches the
    backend or the database.

.PARAMETER Run
    The run folder's name under cache\avatar (for example long7c).

.PARAMETER Order
    The command (trip, rebuff, quest, ladder, ...) or a whole line in quotes ("quest atlan-wand then idle").

.PARAMETER Arg
    The command's argument (a quest key, self or bot), joined after -Order.

.PARAMETER Line
    Anything after the named parameters is joined onto the line: tools\order.ps1 -Run long7c quest atlan-wand.

.PARAMETER WaitSeconds
    How long to wait for the end (default 1800). 0 prints the first report and returns.

.PARAMETER ExpiresSeconds
    The order's life on the desk (default 3600): past it a waiting order is taken off and reported expired.

.PARAMETER DryRun
    Every check, nothing done: the report says checked.

.EXAMPLE
    pwsh -File tools\order.ps1 -Run long7c -Order trip
.EXAMPLE
    pwsh -File tools\order.ps1 -Run long7c -Order quest -Arg atlan-wand
.EXAMPLE
    pwsh -File tools\order.ps1 -Run long7c rebuff then idle
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$')]
    [string] $Run,
    [string] $Order = '',
    [string] $Arg = '',
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $Line = @(),
    [ValidateRange(0, 86400)]
    [int] $WaitSeconds = 1800,
    [ValidateRange(5, 86400)]
    [int] $ExpiresSeconds = 3600,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $repoRoot "cache\avatar\$Run"
$text = (@($Order, $Arg) + $Line | Where-Object { $_ }) -join ' '
if (-not $text.Trim()) { Write-Host '[order] REFUSED: no order given (try -Order status, or -Order help)'; exit 2 }
if (-not (Test-Path -LiteralPath $folder)) { Write-Host "[order] REFUSED: no run folder $folder (is the run started with -SteerRun $Run?)"; exit 2 }

$statusFile = Join-Path $folder 'status.json'
if (-not (Test-Path -LiteralPath $statusFile)) { Write-Host "[order] REFUSED: $statusFile is not there yet: the desk opens 5 s after she is in the world"; exit 2 }
$status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
$guid = $status.character.guid
if (-not $guid) { Write-Host '[order] REFUSED: status.json names no character yet'; exit 2 }

$inbox = Join-Path $folder 'inbox'
$outbox = Join-Path $folder 'outbox'
New-Item -ItemType Directory -Force -Path $inbox | Out-Null
$utc = [DateTime]::UtcNow
$id = 'o-' + $utc.ToString('yyyyMMddHHmmssfff')
$sequence = $utc.ToString('HHmmssfff')
$envelope = [ordered]@{
    v          = 1
    id         = $id
    kind       = 'command'
    args       = @{ line = $text }
    by         = 'lead'
    why        = 'tools/order.ps1'
    issued_utc = $utc.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    expires_s  = $ExpiresSeconds
    guard      = @{ character_guid = $guid }
    dry_run    = [bool]$DryRun
}
$target = Join-Path $inbox "$sequence-$id.json"
$temporary = "$target.tmp"
[System.IO.File]::WriteAllText($temporary, ($envelope | ConvertTo-Json -Depth 4 -Compress), [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $target   # the desk never sees it half-written
Write-Host "[order] $id '$text' for $($status.character.name) ($guid) dropped at $($utc.ToString('HH:mm:ss')) UTC"

$report = Join-Path $outbox "$id.json"
$deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max($WaitSeconds, 15))
$last = ''
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $report) {
        try { $json = Get-Content -LiteralPath $report -Raw } catch { Start-Sleep -Milliseconds 200; continue }
        if ($json -and $json -ne $last) {
            $last = $json
            $r = $json | ConvertFrom-Json
            Write-Host "[order] $($r.at_utc) $($r.state):$($r.name) level=$($r.level) ground=$($r.ground): $($r.detail)"
            if ($r.evidence.PSObject.Properties.Name -contains 'lines') { $r.evidence.lines | ForEach-Object { Write-Host "    $_" } }
            if ($r.state -ne 'running') { exit $(if ($r.state -in @('done', 'checked')) { 0 } else { 1 }) }
            if ($WaitSeconds -eq 0) { exit 0 }
        }
    }

    Start-Sleep -Milliseconds 500
}

Write-Host "[order] no end within $WaitSeconds s$(if ($last) { ' (the last report is above; the order goes on)' } else { ': no report at all (is the desk open, and the run started with --autopilot-steer-orders?)' })"
exit 3
