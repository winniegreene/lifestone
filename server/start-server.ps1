<#
.SYNOPSIS
  Starts the Lifestone local backend: mariadbd (127.0.0.1:3316) then ACE (UDP 127.0.0.1:9000-9001).

.DESCRIPTION
  Both run as normal user processes with a private window-less console (no service, no elevation). PIDs go to
  server\runtime\pids.json; logs go to server\runtime\logs (mariadb.err, ACE_Log.txt, ACE_Log-WARN.txt).
  The script refuses to start if the rendered configs are not loopback-only, if the ports are taken, or if a DAT
  file is missing / still being written by another process. It waits until ACE has finished booting and then
  verifies the actual socket bindings.

.PARAMETER TimeoutSec
  How long to wait for ACE to finish booting (first boot after setup is the slowest).
.PARAMETER DatabaseOnly
  Start only mariadbd (useful for DB tooling).
.PARAMETER KeepOnFailure
  Do not stop processes started by this invocation if startup fails (for debugging).
#>
[CmdletBinding()]
param(
    [int]$TimeoutSec = 600,
    [switch]$DatabaseOnly,
    [switch]$KeepOnFailure
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LifestoneServer.psm1') -Force
$cfg = Get-LsConfig

foreach ($f in @($cfg.MyIni, $cfg.Secrets, $cfg.RootCnf, $cfg.MariaDbd, $cfg.AceExe, $cfg.AceConfig, $cfg.AceLog4net)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "Missing $f. Run server\setup-server.ps1 first." }
}
Assert-LsMyIniLoopback $cfg
Assert-LsAceConfigLoopback $cfg

$pids = Get-LsPids $cfg
$db = $null; $ace = $null
if ($pids) {
    if ($pids.PSObject.Properties['mariadbd']) { $db = Get-LsLiveProcess $pids.mariadbd }
    if ($pids.PSObject.Properties['ace']) { $ace = Get-LsLiveProcess $pids.ace }
}
$tracked = @($db, $ace) | Where-Object { $_ } | ForEach-Object { $_.Id }
$stray = @(Get-LsRuntimeProcesses $cfg | Where-Object { $tracked -notcontains $_.Id })
if ($stray.Count -gt 0) {
    throw "Untracked Lifestone server processes are running (PIDs $(($stray | ForEach-Object Id) -join ', ')). Run server\stop-server.ps1 first."
}
if ($db -and ($ace -or $DatabaseOnly)) {
    Write-LsStep "Already running: mariadbd PID $($db.Id)$(if ($ace) { ", ACE PID $($ace.Id)" })"
    exit 0
}

$record = [ordered]@{ startedUtc = (Get-Date).ToUniversalTime().ToString('o'); host = $cfg.Host; mariadbPort = $cfg.DbPort; acePorts = @($cfg.AcePort, $cfg.AcePort + 1); logs = $cfg.Logs }
$startedHere = @()

function Save-Pids {
    if ($db) { $record.mariadbd = New-LsPidEntry $db $cfg.MariaDbd }
    if ($ace) { $record.ace = New-LsPidEntry $ace $cfg.AceExe }
    Write-LsUtf8File $cfg.PidsFile ($record | ConvertTo-Json -Depth 4)
}

try {
    if (-not $DatabaseOnly) {
        $datIssues = @(Test-LsDatFiles $cfg)
        if ($datIssues.Count -gt 0) { throw ("DAT files in $($cfg.DatDir) are not usable:`n  " + ($datIssues -join "`n  ")) }
        foreach ($u in (Get-LsUdpEndpoints @($cfg.AcePort, ($cfg.AcePort + 1)))) {
            throw "UDP port $($u.LocalPort) is already in use by PID $($u.OwningProcess) ($($u.LocalAddress))."
        }
    }

    if (-not $db) {
        Write-LsStep "Starting mariadbd ($($cfg.Host):$($cfg.DbPort))"
        $db = Start-LsMariaDb -Cfg $cfg
        $startedHere += $db
        Save-Pids
        Write-LsInfo "mariadbd PID $($db.Id) ready"
    } else {
        Write-LsInfo "mariadbd already running (PID $($db.Id))"
    }

    if (-not $DatabaseOnly) {
        Write-LsStep "Starting ACE ($($cfg.Host):$($cfg.AcePort)/udp)"
        $logOffset = 0L
        if (Test-Path -LiteralPath $cfg.AceLog) { $logOffset = (Get-Item -LiteralPath $cfg.AceLog).Length }
        $dotnetRoot = Get-LsDotnetRoot
        $ace = Start-LsBackgroundProcess -FilePath $cfg.AceExe -WorkingDirectory $cfg.AceDir -Environment @{ DOTNET_ROOT = $dotnetRoot }
        $startedHere += $ace
        Save-Pids
        Write-LsInfo "ACE PID $($ace.Id) (DOTNET_ROOT=$dotnetRoot); waiting for boot (log: $($cfg.AceLog))"

        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $seen = New-Object System.Text.StringBuilder
        $ready = $false
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            if (Test-Path -LiteralPath $cfg.AceLog) {
                $fs = [System.IO.File]::Open($cfg.AceLog, 'Open', 'Read', 'ReadWrite, Delete')
                try {
                    if ($fs.Length -lt $logOffset) { $logOffset = 0 }   # rolled over
                    [void]$fs.Seek($logOffset, 'Begin')
                    $sr = New-Object System.IO.StreamReader($fs)
                    $chunk = $sr.ReadToEnd()
                    $logOffset = $fs.Position
                } finally { $fs.Dispose() }
                if ($chunk) {
                    [void]$seen.Append($chunk)
                    foreach ($line in ($chunk -split "`r?`n")) {
                        if ($line -match ' (INFO|WARN|ERROR|FATAL) .*(Successfully (opened|connected)|Binding ConnectionListener|World started|WARN|ERROR|FATAL|Initializing CommandManager)') {
                            Write-LsInfo ($line -replace '^\S+ \S+ \[[^\]]*\] ', '')
                        }
                    }
                }
            }
            $all = $seen.ToString()
            if ($all -match 'ACEmulator will now abort startup|Unable to load or parse starterGear|Attempting to reconnect to') { throw 'ACE reported a fatal startup error (see lines above and the ACE log).' }
            if ($ace.HasExited) {
                $code = '?'; try { $code = $ace.ExitCode } catch { }
                throw "ACE exited during startup (exit code $code). See $($cfg.AceLog)"
            }
            if ($all -match 'Initializing CommandManager') {
                $udp = @(Get-LsUdpEndpoints @($cfg.AcePort, ($cfg.AcePort + 1)) | Where-Object { $_.OwningProcess -eq $ace.Id })
                if ($udp.Count -ge 2) { $ready = $true; break }
            }
        }
        if (-not $ready) { throw "ACE did not finish booting within $TimeoutSec s. See $($cfg.AceLog)" }
    }

    Write-LsStep 'Verifying socket bindings'
    $bad = @()
    $tcp = @(Get-LsTcpListeners $cfg.DbPort)
    foreach ($t in $tcp) {
        Write-LsInfo ("TCP {0}:{1} LISTEN pid {2}" -f $t.LocalAddress, $t.LocalPort, $t.OwningProcess)
        if ($t.LocalAddress -ne '127.0.0.1') { $bad += "mariadb TCP listener on $($t.LocalAddress)" }
    }
    if ($tcp.Count -eq 0) { $bad += "nothing listening on TCP $($cfg.DbPort)" }
    $dbOther = @(Get-NetTCPConnection -OwningProcess $db.Id -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -ne $cfg.DbPort })
    foreach ($t in $dbOther) { $bad += "mariadbd also listens on $($t.LocalAddress):$($t.LocalPort)" }
    if ($ace) {
        $udpAll = @(Get-NetUDPEndpoint -OwningProcess $ace.Id -ErrorAction SilentlyContinue)
        foreach ($u in $udpAll) {
            Write-LsInfo ("UDP {0}:{1} pid {2}" -f $u.LocalAddress, $u.LocalPort, $u.OwningProcess)
            if ($u.LocalAddress -ne '127.0.0.1') { $bad += "ACE UDP endpoint on $($u.LocalAddress):$($u.LocalPort)" }
        }
        $aceTcp = @(Get-NetTCPConnection -OwningProcess $ace.Id -State Listen -ErrorAction SilentlyContinue)
        foreach ($t in $aceTcp) { $bad += "ACE TCP listener on $($t.LocalAddress):$($t.LocalPort)" }
    }
    if ($bad.Count -gt 0) { throw ("Non-loopback or unexpected listeners detected:`n  " + ($bad -join "`n  ")) }

    Write-LsStep ("Lifestone backend is up: mariadbd PID {0}{1}. Stop with server\stop-server.ps1" -f $db.Id, $(if ($ace) { ", ACE PID $($ace.Id) on $($cfg.Host):$($cfg.AcePort)" } else { '' }))
}
catch {
    Write-Host "START FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $KeepOnFailure -and $startedHere.Count -gt 0) {
        Write-LsWarn 'Stopping processes started by this invocation...'
        if ($ace -and ($startedHere -contains $ace) -and -not $ace.HasExited) {
            Stop-Process -Id $ace.Id -Force -ErrorAction SilentlyContinue
            $ace.WaitForExit(15000) | Out-Null
        }
        if ($db -and ($startedHere -contains $db)) { Stop-LsMariaDb -Cfg $cfg -Process $db }
        Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
