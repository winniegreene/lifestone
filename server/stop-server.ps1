<#
.SYNOPSIS
  Gracefully stops the Lifestone local backend (ACE first, then mariadbd) and verifies nothing is left running.

.DESCRIPTION
  ACE: types ACE's own console command "stop-now" into ACE's private console (logs off players, saves, unloads
       landblocks, flushes the DB queue, exits). Falls back to terminating the process after -TimeoutSec.
  MariaDB: "mariadb-admin shutdown" (clean InnoDB shutdown). Falls back to terminating after -TimeoutSec.
  Also stops untracked processes running the server\runtime executables (e.g. if pids.json was lost).

.PARAMETER Force
  Skip the graceful paths and terminate immediately (risks losing unsaved game state).
#>
[CmdletBinding()]
param(
    [int]$TimeoutSec = 120,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LifestoneServer.psm1') -Force
$cfg = Get-LsConfig

$pids = Get-LsPids $cfg
$aceProcs = @(); $dbProcs = @()
if ($pids) {
    if ($pids.PSObject.Properties['ace']) { $p = Get-LsLiveProcess $pids.ace; if ($p) { $aceProcs += $p } }
    if ($pids.PSObject.Properties['mariadbd']) { $p = Get-LsLiveProcess $pids.mariadbd; if ($p) { $dbProcs += $p } }
}
foreach ($p in @(Get-LsRuntimeProcesses $cfg)) {
    if ($p.ProcessName -eq 'ACE.Server') { if (@($aceProcs | ForEach-Object Id) -notcontains $p.Id) { $aceProcs += $p } }
    else { if (@($dbProcs | ForEach-Object Id) -notcontains $p.Id) { $dbProcs += $p } }
}

if ($aceProcs.Count -eq 0 -and $dbProcs.Count -eq 0) {
    Write-LsStep 'Nothing to stop (no Lifestone server processes running).'
    Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
    exit 0
}

foreach ($ace in $aceProcs) {
    Write-LsStep "Stopping ACE (PID $($ace.Id))"
    $graceful = $false
    if (-not $Force) {
        try {
            Send-LsConsoleCommand $cfg $ace.Id 'stop-now'
            Write-LsInfo "Sent console command 'stop-now'; waiting up to $TimeoutSec s for a clean exit..."
            $graceful = $ace.WaitForExit($TimeoutSec * 1000)
        } catch {
            Write-LsWarn "Graceful stop failed: $($_.Exception.Message)"
        }
    }
    if (-not $graceful -and -not $ace.HasExited) {
        Write-LsWarn 'Terminating ACE (not graceful; unsaved state may be lost).'
        Stop-Process -Id $ace.Id -Force
        $ace.WaitForExit(15000) | Out-Null
    }
    Write-LsInfo ("ACE exited ({0})" -f $(if ($graceful) { 'graceful stop-now' } else { 'terminated' }))
}

foreach ($db in $dbProcs) {
    Write-LsStep "Stopping mariadbd (PID $($db.Id))"
    if ($Force -or -not (Test-Path -LiteralPath $cfg.RootCnf)) {
        Stop-Process -Id $db.Id -Force
        $db.WaitForExit(15000) | Out-Null
    } else {
        Stop-LsMariaDb -Cfg $cfg -Process $db -TimeoutSec $TimeoutSec
    }
    Write-LsInfo "mariadbd exited: $($db.HasExited)"
}

Write-LsStep 'Verifying'
Start-Sleep -Milliseconds 500
$left = @(Get-LsRuntimeProcesses $cfg)
$tcp = @(Get-LsTcpListeners $cfg.DbPort)
$udp = @(Get-LsUdpEndpoints @($cfg.AcePort, ($cfg.AcePort + 1)))
foreach ($p in $left) { Write-LsWarn "still running: $($p.ProcessName) PID $($p.Id)" }
foreach ($t in $tcp) { Write-LsWarn "TCP $($t.LocalAddress):$($t.LocalPort) still listening (PID $($t.OwningProcess))" }
foreach ($u in $udp) { Write-LsWarn "UDP $($u.LocalAddress):$($u.LocalPort) still bound (PID $($u.OwningProcess))" }
if ($left.Count -gt 0 -or $tcp.Count -gt 0 -or $udp.Count -gt 0) { Write-Host 'STOP INCOMPLETE' -ForegroundColor Red; exit 1 }
Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
Write-LsStep 'All Lifestone server processes stopped; ports 3316/tcp and 9000-9001/udp are free.'
