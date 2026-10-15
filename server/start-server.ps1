<#
.SYNOPSIS
  Starts the Lifestone local backend: mariadbd (127.0.0.1:3316) then ACE (UDP 127.0.0.1:9000-9001) with the
  Lifestone server mod, and returns once the mod reports ready.

.DESCRIPTION
  Both run as normal user processes with a private window-less console (no service, no elevation). PIDs go to
  server\runtime\pids.json; logs go to server\runtime\logs (mariadb.err, ACE_Log.txt, ACE_Log-WARN.txt).
  The script refuses to start if the rendered configs are not loopback-only, if the ports are taken, if a DAT
  file is missing / still being written by another process, or if the Lifestone server mod is missing, is not the
  build setup installed, or is stale (its sources or ACE changed since setup built it).

  ACE is ready when it owns both UDP endpoints AND its log shows the mod's line 'Lifestone mod ready:', which the mod
  writes once ACE's first-account Admin auto-promotion is off, the world is open and its @lifestone command is
  registered. Log lines are judged whole: a line ACE is still writing waits for the next read. Any line saying the
  mod did not load ends the start at once (the processes it started are stopped).
  pids.json's ace entry then records modReady (true), modVersion, modWire, modDllSha256 and readyUtc; tools\play.ps1,
  the clients and the loopback tests log in only when modReady is true.

  "Already running" counts only when pids.json says the running ACE's mod is ready.

.PARAMETER TimeoutSec
  How long to wait for ACE to finish booting (first boot after setup is the slowest).
.PARAMETER DatabaseOnly
  Start only mariadbd (useful for DB tooling).
.PARAMETER KeepOnFailure
  Do not stop processes started by this invocation if startup fails (for debugging).
.PARAMETER AllowNoMod
  Debugging only: start ACE without requiring the Lifestone server mod. ACE counts as up when the world opens, and
  pids.json always records modReady false, so nothing logs in automatically: even when an installed mod loads and
  reports ready, this start skipped its install checks (the DLL setup installed, not stale). A mod that fails to load
  is only a warning. With no mod, only the sentinel Admin account keeps ACE from promoting the next new account to
  Admin.
.PARAMETER AllowStaleMod
  Start even though server\mod changed since setup built the installed mod (it warns instead of refusing).
#>
[CmdletBinding()]
param(
    [int]$TimeoutSec = 600,
    [switch]$DatabaseOnly,
    [switch]$KeepOnFailure,
    [switch]$AllowNoMod,
    [switch]$AllowStaleMod
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\LifestoneServer.psm1') -Force
$cfg = Get-LsConfig
$withMod = -not $DatabaseOnly -and -not $AllowNoMod

foreach ($f in @($cfg.MyIni, $cfg.Secrets, $cfg.RootCnf, $cfg.MariaDbd, $cfg.AceExe, $cfg.AceConfig, $cfg.AceLog4net)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "Missing $f. Run server\setup-server.ps1 first." }
}
if ($withMod) {
    foreach ($f in @($cfg.ModDll, $cfg.ModMeta)) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Missing ${f}: the Lifestone server mod is not installed. Run server\setup-server.ps1 -ModOnly (or the full setup)." }
    }
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
if ($ace -and -not $DatabaseOnly -and -not $AllowNoMod -and -not (Get-LsBackendState $cfg).ModReady) {
    Write-Host "START REFUSED: ACE (PID $($ace.Id)) is running without a confirmed Lifestone server mod (pids.json ace.modReady is not true). Run server\stop-server.ps1, then server\start-server.ps1." -ForegroundColor Red
    exit 1
}
if ($db -and ($ace -or $DatabaseOnly)) {
    $modNote = ''
    if ($ace) {
        $state = Get-LsBackendState $cfg
        $modNote = $(if ($state.ModReady) { "; Lifestone mod $($state.ModVersion) ready (wire $($state.ModWire))" } else { '; WITHOUT a ready Lifestone mod (-AllowNoMod)' })
    }
    Write-LsStep "Already running: mariadbd PID $($db.Id)$(if ($ace) { ", ACE PID $($ace.Id)" })$modNote"
    exit 0
}

$mod = $null
if ($withMod) {
    Write-LsStep 'Lifestone server mod'
    $mod = Get-LsServerModInstall $cfg
    if ($mod.Problems.Count -gt 0) { throw ("The Lifestone server mod cannot be used:`n  " + ($mod.Problems -join "`n  ")) }
    if ($mod.Stale) {
        if (-not $AllowStaleMod) { throw "The installed Lifestone server mod is stale: $($mod.StaleReason). Run server\setup-server.ps1 -ModOnly (or pass -AllowStaleMod)." }
        Write-LsWarn "the installed Lifestone server mod is stale ($($mod.StaleReason)); starting anyway (-AllowStaleMod)"
    }
    Write-LsInfo "Installed: version $($mod.Version), DLL SHA-256 $($mod.DllSha256)$(if ($mod.Source -ne $cfg.ModRoot) { ", built from $($mod.Source)" })"
}
if (-not $DatabaseOnly) {
    foreach ($other in @(Get-LsOtherAceMods $cfg)) {
        Write-LsWarn "ace-mods\$other is another ACE mod, which ACE will load too; Lifestone does not vet third-party mods (they may patch ACE)."
    }
}

$record = [ordered]@{ startedUtc = (Get-Date).ToUniversalTime().ToString('o'); host = $cfg.Host; mariadbPort = $cfg.DbPort; acePorts = @($cfg.AcePort, $cfg.AcePort + 1); logs = $cfg.Logs }
$startedHere = @()
$aceMod = $null

function Save-Pids {
    if ($db) { $record.mariadbd = New-LsPidEntry $db $cfg.MariaDbd }
    if ($ace) { $record.ace = New-LsPidEntry $ace $cfg.AceExe $aceMod }
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
        Write-LsInfo "ACE PID $($ace.Id) (DOTNET_ROOT=$dotnetRoot); waiting for boot and the Lifestone mod (log: $($cfg.AceLog))"

        # Each read is split into whole lines; a line ACE is still writing waits for the next read (Add-LsAceLogText).
        $watch = New-LsAceLogWatch $AllowNoMod.IsPresent $logOffset
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $ready = $false
        while (-not $ready) {
            Start-Sleep -Seconds 1
            $exited = $ace.HasExited
            $last = $exited -or (Get-Date) -ge $deadline
            foreach ($entry in @(Add-LsAceLogText $watch (Read-LsAceLogChunk $watch $cfg.AceLog) -Final:$last)) {
                if ($entry.Line -match ' (INFO|WARN|ERROR|FATAL) .*(Successfully (opened|connected)|Binding ConnectionListener|World started|WARN|ERROR|FATAL|Initializing CommandManager|World is now open|\) Lifestone)') {
                    Write-LsInfo ($entry.Line -replace '^\S+ \S+ \[[^\]]*\] ', '')
                }
                if ($AllowNoMod -and $entry.Signal -eq 'ModFailed') { Write-LsWarn 'the Lifestone server mod did not load or will not become ready; starting anyway (-AllowNoMod)' }
                if ($AllowNoMod -and $entry.Signal -eq 'ModReady') { Write-LsWarn 'the Lifestone server mod reports ready, but -AllowNoMod skipped its install checks, so pids.json records modReady false' }
            }
            if ($watch.Fatal) { throw 'ACE reported a fatal startup error (see lines above and the ACE log).' }
            if ($watch.FailureLine) { throw ("The Lifestone server mod did not load or will not become ready: " + ($watch.FailureLine -replace '^\S+ \S+ \[[^\]]*\] ', '') + " (see $($cfg.AceLog))") }
            if ($exited) {
                $code = '?'; try { $code = $ace.ExitCode } catch { }
                throw "ACE exited during startup (exit code $code). See $($cfg.AceLog)"
            }
            # A mod ACE never loaded logs nothing at all: fail soon after the world opens, not at the timeout.
            $silence = Get-LsModSilenceProblem $watch ([DateTime]::UtcNow)
            if ($silence) { throw "$silence (see $($cfg.AceLog))." }
            if ($watch.ReadyLine -or ($AllowNoMod -and $watch.WorldOpen)) {
                $udp = @(Get-LsUdpEndpoints @($cfg.AcePort, ($cfg.AcePort + 1)) | Where-Object { $_.OwningProcess -eq $ace.Id })
                if ($udp.Count -ge 2) { $ready = $true }
            }
            if ($last -and -not $ready) {
                $what = $(if ($AllowNoMod) { "the world to open ('World is now open')" } else { "the Lifestone mod's ready line ('Lifestone mod ready:')" })
                throw "ACE did not finish booting within $TimeoutSec s: no $what with both UDP ports bound. See $($cfg.AceLog)"
            }
        }

        # Under -AllowNoMod this is always modReady false, whatever the log said (Get-LsStartedAceMod).
        $aceMod = Get-LsStartedAceMod $cfg $watch $mod
        if ($aceMod.Ready) { Write-LsInfo "Lifestone mod $($aceMod.Version) ready (wire $($aceMod.Wire), ACE $($aceMod.Ace), auto-promotion off)" }
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

    # Only now, with every socket checked, does pids.json say the mod is ready (modReady true) for the clients and tests.
    if ($ace) { Save-Pids }

    # The accounts Lifestone logs in with must be Players, and the sentinel must be a banned Admin (names only).
    try {
        $mine = @(Get-LsSecretsAccounts (Get-LsSecrets $cfg) | ForEach-Object { $_.Username.ToLowerInvariant() })
        $elevated = @(Get-LsElevatedAccounts $cfg)
        foreach ($a in $elevated) {
            if ($mine -contains $a.Name.ToLowerInvariant()) { Write-Host "    WARNING: the secrets.json account '$($a.Name)' is $($a.LevelName), not Player (server\README.md, 'Credentials')." -ForegroundColor Red }
        }
        $sentinel = Get-LsSentinelAdmin $cfg
        if (-not $sentinel) {
            Write-Host "    WARNING: the sentinel Admin account '$($cfg.SentinelAdminName)' is missing, so ACE's first-account Admin promotion rests on the mod alone; run server\setup-server.ps1 (it creates it)." -ForegroundColor Red
        } elseif (-not $sentinel.Sound) {
            $what = $(if ($sentinel.Level -ne 5) { "at AccessLevel $($sentinel.Level), not Admin" } else { 'not banned' })
            Write-Host "    WARNING: the sentinel Admin account '$($cfg.SentinelAdminName)' is $what, so it may let someone log in or no longer stop ACE's first-account Admin promotion; run server\setup-server.ps1 (it resets it)." -ForegroundColor Red
        }
    } catch { Write-LsWarn "the account check could not run: $($_.Exception.Message)" }

    if ($ace -and $aceMod -and -not $aceMod.Ready) {
        Write-Host '    WARNING: ACE is up WITHOUT a ready Lifestone server mod (-AllowNoMod): pids.json says modReady false, so tools\play.ps1, the clients and the loopback tests will not log in, and only the sentinel Admin account keeps ACE from making the next new account an Admin.' -ForegroundColor Red
    }
    $modText = $(if ($aceMod -and $aceMod.Ready) { "; Lifestone mod $($aceMod.Version) ready" } else { '' })
    Write-LsStep ("Lifestone backend is up: mariadbd PID {0}{1}{2}. Stop with server\stop-server.ps1" -f $db.Id, $(if ($ace) { ", ACE PID $($ace.Id) on $($cfg.Host):$($cfg.AcePort)" } else { '' }), $modText)
}
catch {
    Write-Host "START FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $KeepOnFailure -and $startedHere.Count -gt 0) {
        Write-LsWarn 'Stopping processes started by this invocation...'
        if ($ace -and ($startedHere -contains $ace) -and -not $ace.HasExited) {
            Stop-Process -Id $ace.Id -Force -ErrorAction SilentlyContinue
            $ace.WaitForExit(15000) | Out-Null
        }
        if ($db -and ($startedHere -contains $db)) {
            Stop-LsMariaDb -Cfg $cfg -Process $db
            Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
        } elseif ($db -and -not $db.HasExited) {
            # MariaDB ran before this start and keeps running: pids.json keeps tracking it, without the ACE that failed.
            $ace = $null
            $record.Remove('ace')
            Save-Pids
        } else {
            Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
        }
    }
    exit 1
}
