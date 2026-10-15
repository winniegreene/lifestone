# Shared helpers for the Lifestone local server scripts (setup/start/stop).
# Compatible with Windows PowerShell 5.1 and PowerShell 7+.
#
# Security posture (see server/README.md):
#   - Everything runs as the current user. Nothing here elevates, installs services, or touches the registry,
#     firewall, PATH or persistent environment variables. Environment changes are per-process only.
#   - All listeners bind to 127.0.0.1. start-server refuses to launch if the rendered configs say otherwise.
#   - Secrets live only in server/runtime (gitignored) and are never written to the console.

Set-StrictMode -Version 2.0

$script:ServerRoot = Split-Path -Parent $PSScriptRoot
$script:RepoRoot = Split-Path -Parent $script:ServerRoot

# A game account name Lifestone generates or accepts: lower case, as ACE stores it (it lower-cases a new account's name,
# AuthenticationHandler.cs:83, and allows at most 50 characters, :38-43).
$script:AccountNamePattern = '^[a-z0-9][a-z0-9-]{2,49}$'

function Get-LsConfig {
    $versions = Get-Content -LiteralPath (Join-Path $script:ServerRoot 'versions.json') -Raw | ConvertFrom-Json
    $runtime = Join-Path $script:ServerRoot 'runtime'
    $cfg = [ordered]@{
        Versions        = $versions
        RepoRoot        = $script:RepoRoot
        ServerRoot      = $script:ServerRoot
        Runtime         = $runtime
        Templates       = Join-Path $script:ServerRoot 'templates'
        Lib             = Join-Path $script:ServerRoot 'lib'
        Downloads       = Join-Path $runtime 'downloads'
        Logs            = Join-Path $runtime 'logs'
        AceDir          = Join-Path $runtime 'ace'
        AceExe          = Join-Path $runtime 'ace\ACE.Server.exe'
        AceConfig       = Join-Path $runtime 'ace\Config.js'
        AceLog4net      = Join-Path $runtime 'ace\log4net.config'
        AceLog          = Join-Path $runtime 'logs\ACE_Log.txt'
        AceModsDir      = Join-Path $runtime 'ace-mods'
        AceBuildInfo    = Join-Path $runtime 'ace\lifestone-build.json'
        AceSourceDir    = Join-Path $script:RepoRoot ($versions.ace.sourceDir -replace '/', '\')
        MariaDbDir      = Join-Path $runtime 'mariadb'
        MariaDbBin      = Join-Path $runtime 'mariadb\bin'
        MariaDbd        = Join-Path $runtime 'mariadb\bin\mariadbd.exe'
        MariaDbClient   = Join-Path $runtime 'mariadb\bin\mariadb.exe'
        MariaDbAdmin    = Join-Path $runtime 'mariadb\bin\mariadb-admin.exe'
        MariaDbInstall  = Join-Path $runtime 'mariadb\bin\mariadb-install-db.exe'
        DataDir         = Join-Path $runtime 'mariadb-data'
        TmpDir          = Join-Path $runtime 'mariadb-tmp'
        FilesDir        = Join-Path $runtime 'mariadb-files'
        MyIni           = Join-Path $runtime 'my.ini'
        MariaDbErrLog   = Join-Path $runtime 'logs\mariadb.err'
        Secrets         = Join-Path $runtime 'secrets.json'
        RootCnf         = Join-Path $runtime 'mariadb-root.cnf'
        AceCnf          = Join-Path $runtime 'mariadb-ace.cnf'
        PidsFile        = Join-Path $runtime 'pids.json'
        StateFile       = Join-Path $runtime 'setup-state.json'
        DatDir          = Join-Path $script:RepoRoot ($versions.server.datFilesDirectory -replace '/', '\')
        Host            = [string]$versions.network.host
        AcePort         = [int]$versions.network.acePort
        DbPort          = [int]$versions.network.mariadbPort
        InjectorScript  = Join-Path $script:ServerRoot 'lib\send-console-command.ps1'
        # The Lifestone server mod (server\mod; m2-plan SM4): its source, where setup builds it (staging stays outside
        # ace-mods, because ACE loads every ace-mods subfolder that has a Meta.json) and where ACE loads it from.
        ModRoot         = Join-Path $script:ServerRoot 'mod'
        ModName         = 'Lifestone.ServerMod'
        ModBuildDir     = Join-Path $runtime 'mod-build'
        ModInstallDir   = Join-Path $runtime 'ace-mods\Lifestone.ServerMod'
        ModDll          = Join-Path $runtime 'ace-mods\Lifestone.ServerMod\Lifestone.ServerMod.dll'
        ModMeta         = Join-Path $runtime 'ace-mods\Lifestone.ServerMod\Meta.json'
        # Game accounts (secrets.json schema 2) and the sentinel Admin account (owner decision D11).
        PlayAccountName   = [string]$versions.server.playAccountName
        TestAccountName   = [string]$versions.server.testAccountName
        SentinelAdminName = [string]$versions.server.sentinelAdminName
    }
    if ($cfg.Host -ne '127.0.0.1') { throw "versions.json network.host must be 127.0.0.1 (got '$($cfg.Host)')." }
    foreach ($k in @('PlayAccountName', 'TestAccountName', 'SentinelAdminName')) {
        if ($cfg[$k] -cnotmatch $script:AccountNamePattern) { throw "versions.json server.$k is missing or not a valid account name ($script:AccountNamePattern)." }
    }
    return $cfg
}

function Write-LsStep([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-LsInfo([string]$Message) { Write-Host "    $Message" }
function Write-LsWarn([string]$Message) { Write-Host "    WARNING: $Message" -ForegroundColor Yellow }

function Write-LsUtf8File([string]$Path, [string]$Text) {
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Expand-LsTemplate([string]$TemplatePath, [hashtable]$Values) {
    $text = Get-Content -LiteralPath $TemplatePath -Raw
    foreach ($k in $Values.Keys) { $text = $text.Replace('{{' + $k + '}}', [string]$Values[$k]) }
    $left = [regex]::Match($text, '\{\{[A-Z_]+\}\}')
    if ($left.Success) { throw "Unrendered placeholder $($left.Value) in $TemplatePath" }
    return $text
}

function New-LsDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Force -Path $Path | Out-Null }
}

# Restrict a file we generated (secrets, configs with passwords) to the current user only.
function Protect-LsFile([string]$Path) {
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $out = & icacls.exe $Path /inheritance:r /grant:r "*${sid}:(F)" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for ${Path}: $out" }
}

function Get-LsSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Refuse to run any downloaded/built executable (anything under the repo) whose embedded manifest asks for
# elevation, which would raise a UAC prompt. System tools (icacls, powershell.exe) are not inspected.
$script:NoElevationChecked = @{}
function Assert-LsNoElevation([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Executable not found: $Path" }
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($script:RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) { return }
    if ($script:NoElevationChecked.ContainsKey($full)) { return }
    $bytes = [System.IO.File]::ReadAllBytes($full)
    $text = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($text -match 'requestedExecutionLevel\s+level\s*=\s*"(requireAdministrator|highestAvailable)"') {
        throw "Refusing to run $full : its manifest requests elevation ($($Matches[1]))."
    }
    $script:NoElevationChecked[$full] = $true
}

function New-LsSecret([int]$Length = 32) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $sb = New-Object System.Text.StringBuilder
    $buf = New-Object byte[] 1
    try {
        while ($sb.Length -lt $Length) {
            $rng.GetBytes($buf)
            # rejection sampling: 248 = 62*4, keeps the distribution uniform
            if ($buf[0] -lt 248) { [void]$sb.Append($alphabet[$buf[0] % 62]) }
        }
    } finally { $rng.Dispose() }
    return $sb.ToString()
}

function Get-LsState($cfg) {
    if (Test-Path -LiteralPath $cfg.StateFile) {
        $obj = Get-Content -LiteralPath $cfg.StateFile -Raw | ConvertFrom-Json
        $h = @{}
        foreach ($p in $obj.PSObject.Properties) { $h[$p.Name] = $p.Value }
        return $h
    }
    return @{}
}

function Save-LsState($cfg, [hashtable]$State) {
    Write-LsUtf8File $cfg.StateFile ($State | ConvertTo-Json -Depth 6)
}

# secrets.json as an object. A parse error names only its exception type: a parser message can quote the text.
function Get-LsSecrets($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.Secrets)) { throw "Missing $($cfg.Secrets). Run setup-server.ps1 first." }
    try { return [System.IO.File]::ReadAllText($cfg.Secrets, [System.Text.Encoding]::UTF8) | ConvertFrom-Json }
    catch { throw "$($cfg.Secrets) is not valid JSON ($($_.Exception.GetType().Name)); restore it from your own copy or see server\README.md, 'Credentials'." }
}

# ---------------------------------------------------------------------------------------------------------------
# Process helpers
# ---------------------------------------------------------------------------------------------------------------

function ConvertTo-LsArgumentString([string[]]$Arguments) {
    $parts = foreach ($a in $Arguments) {
        if ($a -ne '' -and $a -notmatch '[\s"]') { $a; continue }
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append('"')
        $backslashes = 0
        $bs = [char]92
        foreach ($ch in $a.ToCharArray()) {
            if ($ch -eq $bs) { $backslashes++; continue }
            if ($ch -eq [char]34) { [void]$sb.Append($bs, 2 * $backslashes + 1); [void]$sb.Append([char]34) }
            else { if ($backslashes) { [void]$sb.Append($bs, $backslashes) }; [void]$sb.Append($ch) }
            $backslashes = 0
        }
        if ($backslashes) { [void]$sb.Append($bs, 2 * $backslashes) }
        [void]$sb.Append('"')
        $sb.ToString()
    }
    return ($parts -join ' ')
}

$script:NativeSource = @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class LifestoneNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize;
        public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute; public int dwFlags;
        public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2;
        public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);

    const uint CREATE_NO_WINDOW = 0x08000000, CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    // bInheritHandles = false: the child must not keep the caller's stdout/stderr pipes (or any other handle) open,
    // otherwise anything capturing start-server.ps1 output would wait forever.
    public static Process StartDetached(string app, string commandLine, string cwd, string envBlock)
    {
        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        PROCESS_INFORMATION pi;
        IntPtr env = Marshal.StringToHGlobalUni(envBlock);
        try
        {
            if (!CreateProcessW(app, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, env, cwd, ref si, out pi))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed for " + app);
        }
        finally { Marshal.FreeHGlobal(env); }
        try
        {
            var p = Process.GetProcessById(pi.dwProcessId);
            var keep = p.Handle;   // hold a handle so HasExited/ExitCode stay available
            return p;
        }
        finally { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
    }
}
'@

# Start a detached background process: own window-less console (CREATE_NO_WINDOW), no inherited handles. It never
# shares the caller's console (cannot steal keyboard input, is not hit by Ctrl+C in the caller's terminal) and does
# not keep the caller's output pipes open.
function Start-LsBackgroundProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )
    Assert-LsNoElevation $FilePath
    if (-not ('LifestoneNative' -as [type])) { Add-Type -TypeDefinition $script:NativeSource -Language CSharp }
    $vars = @{}
    foreach ($e in [System.Environment]::GetEnvironmentVariables().GetEnumerator()) { $vars[[string]$e.Key] = [string]$e.Value }
    foreach ($k in $Environment.Keys) { $vars[$k] = [string]$Environment[$k] }   # per-child only
    $sb = New-Object System.Text.StringBuilder
    foreach ($k in ($vars.Keys | Sort-Object { $_.ToUpperInvariant() })) { [void]$sb.Append("$k=$($vars[$k])").Append([char]0) }
    [void]$sb.Append([char]0)
    $cmdLine = (ConvertTo-LsArgumentString @($FilePath)) + ' ' + (ConvertTo-LsArgumentString $Arguments)
    $cwd = $WorkingDirectory
    if (-not $cwd) { $cwd = Split-Path -Parent $FilePath }
    return [LifestoneNative]::StartDetached($FilePath, $cmdLine.TrimEnd(), $cwd, $sb.ToString())
}

# Run a console tool to completion, capturing stdout/stderr. Never shows a window.
function Invoke-LsTool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSec = 3600,
        [switch]$AllowFailure
    )
    Assert-LsNoElevation $FilePath
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ConvertTo-LsArgumentString $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill() } catch { }
        throw "$([IO.Path]::GetFileName($FilePath)) timed out after $TimeoutSec s"
    }
    $p.WaitForExit()
    $res = [pscustomobject]@{ ExitCode = $p.ExitCode; StdOut = $outTask.Result; StdErr = $errTask.Result }
    if (-not $AllowFailure -and $res.ExitCode -ne 0) {
        throw "$([IO.Path]::GetFileName($FilePath)) exited with code $($res.ExitCode).`n$($res.StdErr)`n$($res.StdOut)"
    }
    return $res
}

function Get-LsPids($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.PidsFile)) { return $null }
    return Get-Content -LiteralPath $cfg.PidsFile -Raw | ConvertFrom-Json
}

# A pids.json entry: PID, executable path and start time (so a recycled PID is never mistaken for ours). For ACE,
# $Mod adds what start-server.ps1 learnt from the Lifestone mod's ready line (m2-plan SM6): with Ready true, modReady
# (a JSON true, which tools\smoke.ps1 and the loopback tests' BackendProbe require), modVersion, modWire, modDllSha256
# and readyUtc; with Ready false (start-server.ps1 -AllowNoMod) modReady false and nothing else.
function New-LsPidEntry([System.Diagnostics.Process]$Process, [string]$Path, $Mod = $null) {
    $entry = [ordered]@{ pid = $Process.Id; path = $Path; startTime = $Process.StartTime.ToUniversalTime().ToString('o') }
    if ($null -ne $Mod) {
        if ($Mod.Ready) {
            $entry.modReady = $true
            $entry.modVersion = [string]$Mod.Version
            $entry.modWire = [int]$Mod.Wire
            $entry.modDllSha256 = [string]$Mod.DllSha256
            $entry.readyUtc = [string]$Mod.ReadyUtc
        } else {
            $entry.modReady = $false
        }
    }
    return $entry
}

# Returns the live process for a pids.json entry, or $null if it is gone (or the PID was reused by something else).
function Get-LsLiveProcess($Entry) {
    if (-not $Entry) { return $null }
    $p = Get-Process -Id ([int]$Entry.pid) -ErrorAction SilentlyContinue
    if (-not $p) { return $null }
    try {
        if ($p.Path -and ($p.Path -ne $Entry.path)) { return $null }
        # PowerShell 7's ConvertFrom-Json already turns ISO strings into DateTime; 5.1 leaves a string.
        $recorded = $Entry.startTime
        if (-not ($recorded -is [DateTime])) {
            $recorded = [DateTime]::Parse([string]$recorded, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
        }
        if ([Math]::Abs(($p.StartTime.ToUniversalTime() - $recorded.ToUniversalTime()).TotalSeconds) -gt 2) { return $null }
    } catch { }
    return $p
}

# Any process running one of our runtime executables (covers a lost/stale pids.json).
function Get-LsRuntimeProcesses($cfg) {
    $paths = @($cfg.MariaDbd, (Join-Path $cfg.MariaDbBin 'mysqld.exe'), $cfg.AceExe)
    $result = @()
    foreach ($name in @('mariadbd', 'mysqld', 'ACE.Server')) {
        foreach ($p in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            $pp = $null
            try { $pp = $p.Path } catch { }
            if ($pp -and ($paths -contains $pp)) { $result += $p }
        }
    }
    return $result
}

function Test-LsTcpPort([string]$TargetHost, [int]$Port, [int]$TimeoutMs = 500) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $c.BeginConnect($TargetHost, $Port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
        $c.EndConnect($iar)
        return $true
    } catch { return $false } finally { $c.Close() }
}

# Listening sockets (any process) on a port, with their bind addresses.
function Get-LsTcpListeners([int]$Port) {
    return @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}
function Get-LsUdpEndpoints([int[]]$Ports) {
    return @(Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $Ports -contains $_.LocalPort })
}

# ---------------------------------------------------------------------------------------------------------------
# MariaDB
# ---------------------------------------------------------------------------------------------------------------

function Assert-LsMyIniLoopback($cfg) {
    $ini = Get-Content -LiteralPath $cfg.MyIni -Raw
    $m = [regex]::Match($ini, '(?m)^\s*bind-address\s*=\s*(\S+)\s*$')
    if (-not $m.Success -or $m.Groups[1].Value -ne '127.0.0.1') { throw "$($cfg.MyIni) must contain bind-address=127.0.0.1; refusing to start mariadbd." }
    $p = [regex]::Match($ini, '(?m)^\s*port\s*=\s*(\d+)\s*$')
    if (-not $p.Success -or [int]$p.Groups[1].Value -ne $cfg.DbPort) { throw "$($cfg.MyIni) port must be $($cfg.DbPort)." }
}

function Invoke-LsSql {
    param(
        [Parameter(Mandatory = $true)]$Cfg,
        [Parameter(Mandatory = $true)][string]$Cnf,
        [string]$Database,
        [string]$Sql,
        [string]$SourceFile,
        [switch]$AllowFailure,
        [int]$TimeoutSec = 3600
    )
    # NOTE: without --abort-source-on-error the client keeps going after a failing statement inside "source" and
    # still exits 0. Any "ERROR nnnn" on stderr is treated as a failure as well.
    $toolArgs = @("--defaults-file=$Cnf", '--batch', '--skip-column-names', '--abort-source-on-error', '--default-character-set=utf8mb4', '--max-allowed-packet=64M')
    if ($Database) { $toolArgs += "--database=$Database" }
    if ($SourceFile) { $Sql = "source " + ($SourceFile -replace '\\', '/') }
    $toolArgs += @('-e', $Sql)
    $r = Invoke-LsTool -FilePath $Cfg.MariaDbClient -Arguments $toolArgs -TimeoutSec $TimeoutSec -AllowFailure
    if ($r.ExitCode -eq 0 -and $r.StdErr -match 'ERROR \d+') { $r.ExitCode = 1 }
    if (-not $AllowFailure -and $r.ExitCode -ne 0) {
        $what = $SourceFile; if (-not $what) { $what = $Sql }
        throw "SQL failed ($what):`n$($r.StdErr.Trim())"
    }
    return $r
}

function Wait-LsMariaDbReady($cfg, [System.Diagnostics.Process]$Process, [int]$TimeoutSec = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            $code = '?'; try { $code = $Process.ExitCode } catch { }
            throw "mariadbd exited during startup (code $code). See $($cfg.MariaDbErrLog)"
        }
        if (Test-LsTcpPort $cfg.Host $cfg.DbPort 500) {
            $r = Invoke-LsTool -FilePath $cfg.MariaDbAdmin -Arguments @("--defaults-file=$($cfg.RootCnf)", '--connect-timeout=5', 'ping') -AllowFailure -TimeoutSec 30
            if ($r.ExitCode -eq 0) { return }
        }
        Start-Sleep -Milliseconds 500
    }
    throw "mariadbd did not become ready within $TimeoutSec s. See $($cfg.MariaDbErrLog)"
}

function Start-LsMariaDb {
    param([Parameter(Mandatory = $true)]$Cfg, [string]$InitFile)
    Assert-LsMyIniLoopback $Cfg
    foreach ($l in (Get-LsTcpListeners $Cfg.DbPort)) {
        throw "TCP port $($Cfg.DbPort) is already in use by PID $($l.OwningProcess) ($($l.LocalAddress))."
    }
    New-LsDirectory $Cfg.Logs; New-LsDirectory $Cfg.TmpDir; New-LsDirectory $Cfg.FilesDir
    $a = @("--defaults-file=$($Cfg.MyIni)")
    if ($InitFile) { $a += "--init-file=$InitFile" }
    $p = Start-LsBackgroundProcess -FilePath $Cfg.MariaDbd -Arguments $a -WorkingDirectory $Cfg.Runtime
    Wait-LsMariaDbReady $Cfg $p
    return $p
}

function Stop-LsMariaDb {
    param([Parameter(Mandatory = $true)]$Cfg, [System.Diagnostics.Process]$Process, [int]$TimeoutSec = 120)
    if (-not $Process -or $Process.HasExited) { return }
    Write-LsInfo "Shutting down mariadbd (PID $($Process.Id)) via mariadb-admin shutdown..."
    $r = Invoke-LsTool -FilePath $Cfg.MariaDbAdmin -Arguments @("--defaults-file=$($Cfg.RootCnf)", '--connect-timeout=5', 'shutdown') -AllowFailure -TimeoutSec ($TimeoutSec + 30)
    if ($r.ExitCode -ne 0) { Write-LsWarn "mariadb-admin shutdown returned $($r.ExitCode): $($r.StdErr.Trim())" }
    if (-not $Process.WaitForExit($TimeoutSec * 1000)) {
        Write-LsWarn "mariadbd did not exit within $TimeoutSec s; terminating it (InnoDB will crash-recover on next start)."
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit(10000) | Out-Null
    }
}

# ---------------------------------------------------------------------------------------------------------------
# ACE
# ---------------------------------------------------------------------------------------------------------------

function Get-LsDotnetRoot {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += $env:DOTNET_ROOT }
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath (Join-Path $c 'dotnet.exe')) { return $c } }
    $cmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($cmd) { return (Split-Path -Parent $cmd.Source) }
    throw 'Could not locate a .NET installation (dotnet.exe). Set DOTNET_ROOT for this shell or install the .NET 10 runtime.'
}

function Assert-LsAceConfigLoopback($cfg) {
    $js = Get-Content -LiteralPath $cfg.AceConfig -Raw
    $m = [regex]::Match($js, '"Network"\s*:\s*\{[^}]*?"Host"\s*:\s*"([^"]*)"', 'Singleline')
    if (-not $m.Success -or $m.Groups[1].Value -ne '127.0.0.1') {
        throw "$($cfg.AceConfig) Server.Network.Host must be exactly 127.0.0.1; refusing to start ACE."
    }
    $hosts = [regex]::Matches($js, '"Host"\s*:\s*"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }
    foreach ($h in $hosts) { if ($h -ne '127.0.0.1') { throw "$($cfg.AceConfig) contains non-loopback Host '$h'." } }
}

# ACE opens the dats with FileShare.Read; if another process is still writing one (e.g. client_highres.dat being
# extracted) ACE would crash at startup with a sharing violation. Check up front with the same share mode.
function Test-LsDatFiles($cfg) {
    $required = @('client_cell_1.dat', 'client_portal.dat', 'client_local_English.dat')
    $problems = @()
    foreach ($f in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $cfg.DatDir $f))) { $problems += "missing required $f" }
    }
    foreach ($f in ($required + 'client_highres.dat')) {
        $path = Join-Path $cfg.DatDir $f
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try {
            $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $fs.Dispose()
        } catch {
            $problems += "$f is locked by another process (still being written?): $($_.Exception.Message)"
        }
    }
    return $problems
}

function Send-LsConsoleCommand($cfg, [int]$TargetPid, [string]$CommandLine) {
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $r = Invoke-LsTool -FilePath $ps -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $cfg.InjectorScript, '-TargetPid', "$TargetPid", '-CommandLine', $CommandLine) -AllowFailure -TimeoutSec 120
    if ($r.ExitCode -ne 0) { throw "Console injection into PID $TargetPid failed (exit $($r.ExitCode)): $($r.StdErr.Trim()) $($r.StdOut.Trim())" }
}

# ---------------------------------------------------------------------------------------------------------------
# .NET SDK environment
# ---------------------------------------------------------------------------------------------------------------

# The per-child environment for every dotnet process the scripts start (CLAUDE.md, ".NET"): no telemetry, no banner,
# and none of the SDK's first-run changes (it would append a tools folder to the persistent user PATH and create an
# ASP.NET development certificate in CurrentUser\My; DOTNET_SKIP_FIRST_TIME_EXPERIENCE does not stop either), and no
# MSBuild node or server outliving the build. Callers also pass --disable-build-servers and -nodeReuse:false.
function Get-LsDotnetEnvironment([string]$DotnetRoot) {
    return @{
        DOTNET_ROOT                        = $DotnetRoot
        PATH                               = "$DotnetRoot;$env:PATH"
        DOTNET_CLI_TELEMETRY_OPTOUT        = '1'
        DOTNET_NOLOGO                      = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE  = '1'
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH    = 'false'
        DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        MSBUILDDISABLENODEREUSE            = '1'
        DOTNET_CLI_USE_MSBUILD_SERVER      = '0'
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Lifestone server mod: build key, build, install (m2-plan SM4) and the checks start-server makes (SM6)
# ---------------------------------------------------------------------------------------------------------------

# The files a mod build is made of, so a change to any of them makes the installed mod stale: under server\mod the
# sources and project files (*.cs, *.csproj, *.json, *.props, *.targets and .editorconfig) outside bin\ and obj\, and
# the two Lifestone.Net files the project compiles by link. Documentation (README.md) is not part of the build.
$script:ModKeyFormat = 'lifestone-mod-build-key/1'
$script:ModKeyExtensions = @('.cs', '.csproj', '.json', '.props', '.targets')
$script:ModLinkedFiles = @('src\Lifestone.Net\Sockets\LoopbackPolicy.cs', 'src\Lifestone.Net\Sockets\LoopbackRequiredException.cs')
# What a build or an install of the mod must hold, exactly: ACE loads <folder>\<folder>.dll and Meta.json
# (ModContainer.cs:28-40), and the pdb gives readable stack traces in ACE's log.
$script:ModFiles = @('Lifestone.ServerMod.dll', 'Lifestone.ServerMod.pdb', 'Meta.json')

# Returns @([pscustomobject]@{ Path; FullName }) in ordinal Path order. Path is repository-relative with '/'
# separators (server/mod/Shared/ToolsWire.cs, src/Lifestone.Net/Sockets/LoopbackPolicy.cs). $ModRoot is a server\mod
# folder: this checkout's, or another checkout's or worktree's (setup-server.ps1 -ModSource).
function Get-LsServerModSourceFiles([string]$ModRoot) {
    $root = [System.IO.Path]::GetFullPath($ModRoot).TrimEnd('\')
    $repo = Split-Path -Parent (Split-Path -Parent $root)
    if (-not (Test-Path -LiteralPath (Join-Path $root 'Lifestone.ServerMod\Lifestone.ServerMod.csproj') -PathType Leaf)) {
        throw "$root is not a Lifestone server mod folder (no Lifestone.ServerMod\Lifestone.ServerMod.csproj)."
    }
    $paths = New-Object System.Collections.Generic.List[string]
    $fulls = New-Object System.Collections.Generic.List[string]
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)) {
        $relative = $f.FullName.Substring($root.Length + 1)
        $inBuildOutput = @($relative -split '\\' | Where-Object { $_ -eq 'bin' -or $_ -eq 'obj' }).Count -gt 0
        $kind = $f.Extension.ToLowerInvariant()
        if ($inBuildOutput -or -not ($f.Name -eq '.editorconfig' -or $script:ModKeyExtensions -contains $kind)) { continue }
        $paths.Add('server/mod/' + ($relative -replace '\\', '/'))
        $fulls.Add($f.FullName)
    }
    foreach ($linked in $script:ModLinkedFiles) {
        $full = Join-Path $repo $linked
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "The server mod compiles $linked by link, and $repo has no such file." }
        $paths.Add(($linked -replace '\\', '/'))
        $fulls.Add($full)
    }
    $keys = $paths.ToArray()
    $items = $fulls.ToArray()
    [Array]::Sort($keys, $items, [StringComparer]::Ordinal)
    $result = @()
    for ($i = 0; $i -lt $keys.Length; $i++) { $result += [pscustomobject]@{ Path = $keys[$i]; FullName = $items[$i] } }
    return $result
}

# The mod's build key: SHA-256 (lower-case hex) of the UTF-8 text
#   "lifestone-mod-build-key/1\n" + one "<path>\t<file SHA-256>\n" per Get-LsServerModSourceFiles entry
#   + "ACE.Server.dll\t<aceServerDllSha256>\n".
# setup-server.ps1 records it (setup-state.json modBuildKey) and skips a rebuild while it holds; start-server.ps1
# recomputes it and refuses a stale mod. tests\Lifestone.ServerMod.Tests computes the same key in C#.
function Get-LsServerModBuildKey {
    param([Parameter(Mandatory = $true)][string]$ModRoot, [Parameter(Mandatory = $true)][string]$AceServerDllSha256)
    $aceSha = $AceServerDllSha256.ToLowerInvariant()
    if ($aceSha -cnotmatch '^[0-9a-f]{64}$') { throw 'The ACE.Server.dll SHA-256 must be 64 hex digits (lifestone-build.json aceServerDllSha256).' }
    $text = New-Object System.Text.StringBuilder
    [void]$text.Append($script:ModKeyFormat).Append("`n")
    foreach ($f in @(Get-LsServerModSourceFiles $ModRoot)) {
        [void]$text.Append($f.Path).Append("`t").Append((Get-LsSha256 $f.FullName)).Append("`n")
    }
    [void]$text.Append('ACE.Server.dll').Append("`t").Append($aceSha).Append("`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text.ToString())) } finally { $sha.Dispose() }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

# The ACE build the mod compiles against: runtime\ace\lifestone-build.json, written by setup-server.ps1's ACE build.
function Get-LsAceBuildStamp($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.AceBuildInfo -PathType Leaf)) { throw "Missing $($cfg.AceBuildInfo): ACE was not built by setup-server.ps1. Run server\setup-server.ps1." }
    $stamp = [System.IO.File]::ReadAllText($cfg.AceBuildInfo, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($k in @('commit', 'aceServerDllSha256')) {
        if (-not $stamp.PSObject.Properties[$k]) { throw "$($cfg.AceBuildInfo) has no '$k'; rebuild ACE with server\setup-server.ps1 -RebuildAce." }
    }
    return $stamp
}

# What is wrong with a mod build or install folder (an empty list when nothing is): it must hold exactly
# Lifestone.ServerMod.dll, its pdb and Meta.json (Name Lifestone, Enabled true, HotReload false), and none of ACE's own
# assemblies. A copy of ACE.Server.dll beside the mod is the type-identity trap: the plugin loader would load the mod's
# own ACE.Server, whose IHarmonyMod ACE does not recognise ('as IHarmonyMod' gives null, ModContainer.cs:201).
function Get-LsServerModOutputProblems([string]$Dir) {
    $problems = @()
    if (-not (Test-Path -LiteralPath $Dir -PathType Container)) { return @("$Dir does not exist") }
    $root = [System.IO.Path]::GetFullPath($Dir).TrimEnd('\')
    $found = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($root.Length + 1) })
    $forbidden = @($found | Where-Object { $_ -match '(^|\\)(ACE\.[^\\]*|[^\\]*Harmony[^\\]*|McMaster[^\\]*|log4net[^\\]*)\.dll$' })
    if ($forbidden.Count) { $problems += "it holds assemblies ACE must provide itself: $($forbidden -join ', ')" }
    $extra = @($found | Where-Object { $script:ModFiles -notcontains $_ -and $forbidden -notcontains $_ })
    $missing = @($script:ModFiles | Where-Object { $found -notcontains $_ })
    if ($extra.Count) { $problems += "unexpected files: $($extra -join ', ')" }
    if ($missing.Count) { $problems += "missing: $($missing -join ', ')" }
    $metaPath = Join-Path $root 'Meta.json'
    if (Test-Path -LiteralPath $metaPath -PathType Leaf) {
        try {
            $meta = [System.IO.File]::ReadAllText($metaPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
            $ok = $meta.PSObject.Properties['Name'] -and $meta.Name -ceq 'Lifestone' -and
                $meta.PSObject.Properties['Enabled'] -and $meta.Enabled -eq $true -and
                $meta.PSObject.Properties['HotReload'] -and $meta.HotReload -eq $false
            if (-not $ok) { $problems += 'Meta.json must have Name "Lifestone", Enabled true and HotReload false' }
        } catch { $problems += "Meta.json cannot be read ($($_.Exception.GetType().Name))" }
    }
    return $problems
}

# Builds the mod from $ModRoot into $OutDir against the ACE build in $AceDir, then checks the output. MSBuild's restore
# and intermediate files go to the sibling folder <OutDir>.obj, not to the project's own obj\, so the setup step, the
# compile check and a test's build into a temporary folder never share intermediates. Both folders are emptied first,
# so $OutDir must be one of the mod's own build folders: runtime\mod-build, or a folder inside <repo>\cache named
# server-mod* (its .obj sibling then is too). Throws on any failure. Returns @{ Dll; Sha256; Version; Sdk }.
function Invoke-LsServerModBuild {
    param(
        [Parameter(Mandatory = $true)]$Cfg,
        [Parameter(Mandatory = $true)][string]$ModRoot,
        [Parameter(Mandatory = $true)][string]$OutDir,
        [Parameter(Mandatory = $true)][string]$AceDir,
        [Parameter(Mandatory = $true)][string]$AceServerDllSha256,
        [Parameter(Mandatory = $true)][string]$AceCommit,
        [string]$BuildKey = 'none'
    )
    $out = [System.IO.Path]::GetFullPath($OutDir).TrimEnd('\')
    $buildDir = [System.IO.Path]::GetFullPath($Cfg.ModBuildDir).TrimEnd('\')
    $cache = [System.IO.Path]::GetFullPath((Join-Path $Cfg.RepoRoot 'cache')).TrimEnd('\') + '\'
    $inCache = ($out + '\').StartsWith($cache, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $out).StartsWith('server-mod', [StringComparison]::OrdinalIgnoreCase)
    if (-not ($out -ieq $buildDir -or $inCache)) {
        throw "The mod build folder must be $buildDir or a folder inside $cache named server-mod* (got $out)."
    }
    $project = Join-Path $ModRoot 'Lifestone.ServerMod\Lifestone.ServerMod.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "No mod project at $project." }
    $dotnetRoot = Get-LsDotnetRoot
    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
    $dotnetEnv = Get-LsDotnetEnvironment $dotnetRoot
    $obj = $out + '.obj'
    foreach ($dir in @($out, $obj)) { if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force } }
    New-LsDirectory $out
    $sdk = (Invoke-LsTool -FilePath $dotnet -Arguments @('--version') -Environment $dotnetEnv).StdOut.Trim()
    $arguments = @(
        'build', $project, '-c', 'Release', '-o', $out,
        '--disable-build-servers', '-nodeReuse:false', '-p:UseSharedCompilation=false', "-p:BaseIntermediateOutputPath=$obj\",
        "-p:AceDir=$([System.IO.Path]::GetFullPath($AceDir).TrimEnd('\'))", "-p:AceServerDllSha256=$AceServerDllSha256",
        "-p:AceCommit=$AceCommit", "-p:LifestoneModBuildKey=$BuildKey"
    )
    $build = Invoke-LsTool -FilePath $dotnet -Arguments $arguments -Environment $dotnetEnv -WorkingDirectory $Cfg.RepoRoot -TimeoutSec 900 -AllowFailure
    if ($build.ExitCode -ne 0) { throw "dotnet build of the server mod failed (exit $($build.ExitCode)):`n$($build.StdOut)`n$($build.StdErr)" }
    $warnings = @([regex]::Matches($build.StdOut, 'warning [A-Z]+\d+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    if ($warnings.Count) { throw "The server mod build reported warnings ($($warnings -join ', ')):`n$($build.StdOut)" }
    $problems = @(Get-LsServerModOutputProblems $out)
    if ($problems.Count) { throw "The server mod build output in $out is wrong: $($problems -join '; ')." }
    $meta = [System.IO.File]::ReadAllText((Join-Path $out 'Meta.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $dll = Join-Path $out 'Lifestone.ServerMod.dll'
    return [pscustomobject]@{ Dll = $dll; Sha256 = (Get-LsSha256 $dll); Version = [string]$meta.Version; Sdk = $sdk }
}

# Installs a checked build into runtime\ace-mods\Lifestone.ServerMod: removes the old folder, copies the three files
# and verifies their hashes. ACE loads the DLL from that file (HotReload is off), and setup runs only while the
# server is stopped, so nothing holds it open. Returns the installed DLL's SHA-256.
function Install-LsServerMod {
    param([Parameter(Mandatory = $true)]$Cfg, [Parameter(Mandatory = $true)][string]$FromDir)
    $problems = @(Get-LsServerModOutputProblems $FromDir)
    if ($problems.Count) { throw "Refusing to install the server mod from ${FromDir}: $($problems -join '; ')." }
    $mods = [System.IO.Path]::GetFullPath($Cfg.AceModsDir).TrimEnd('\') + '\'
    $dest = [System.IO.Path]::GetFullPath($Cfg.ModInstallDir).TrimEnd('\')
    if (-not ($dest + '\').StartsWith($mods, [StringComparison]::OrdinalIgnoreCase) -or ($dest + '\') -eq $mods) {
        throw "The mod install folder $dest must be a folder inside $mods."
    }
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-LsDirectory $dest
    foreach ($name in $script:ModFiles) {
        $from = Join-Path $FromDir $name
        $to = Join-Path $dest $name
        Copy-Item -LiteralPath $from -Destination $to
        if ((Get-LsSha256 $from) -ne (Get-LsSha256 $to)) { throw "Copying $name into $dest did not give the same file." }
    }
    return (Get-LsSha256 $Cfg.ModDll)
}

# setup-server.ps1's 'Lifestone server mod' step: builds the mod from $ModRoot into runtime\mod-build and installs it,
# unless setup-state.json's modBuildKey still matches and the installed DLL is the recorded one (or -Rebuild). Updates
# $State (modBuildKey, modDllSha256, modVersion, modSource, modBuiltUtc); the caller saves it.
function Invoke-LsServerModSetup {
    param(
        [Parameter(Mandatory = $true)]$Cfg,
        [Parameter(Mandatory = $true)][hashtable]$State,
        [Parameter(Mandatory = $true)][string]$ModRoot,
        [switch]$Rebuild
    )
    $stamp = Get-LsAceBuildStamp $Cfg
    $aceSha = ([string]$stamp.aceServerDllSha256).ToLowerInvariant()
    $actual = Get-LsSha256 (Join-Path $Cfg.AceDir 'ACE.Server.dll')
    if ($actual -ne $aceSha) { throw "$($Cfg.AceDir)\ACE.Server.dll has SHA-256 $actual, but lifestone-build.json says $aceSha. Rebuild ACE with server\setup-server.ps1 -RebuildAce." }
    $root = [System.IO.Path]::GetFullPath($ModRoot).TrimEnd('\')
    $key = Get-LsServerModBuildKey -ModRoot $root -AceServerDllSha256 $aceSha
    Write-LsInfo "Build key $key (server mod sources in $root + ACE.Server.dll $($aceSha.Substring(0, 12)))"
    $installedSha = $null
    if (Test-Path -LiteralPath $Cfg.ModDll -PathType Leaf) { $installedSha = Get-LsSha256 $Cfg.ModDll }
    if (-not $Rebuild -and $State['modBuildKey'] -eq $key -and $installedSha -and $installedSha -eq $State['modDllSha256'] -and
        @(Get-LsServerModOutputProblems $Cfg.ModInstallDir).Count -eq 0) {
        Write-LsInfo "Skip: Lifestone server mod already built (key $($key.Substring(0, 12))) and installed (DLL SHA-256 $installedSha)"
        return [pscustomobject]@{ Built = $false; Key = $key; DllSha256 = $installedSha; Version = [string]$State['modVersion'] }
    }
    Write-LsInfo "dotnet build -> $($Cfg.ModBuildDir)"
    $build = Invoke-LsServerModBuild -Cfg $Cfg -ModRoot $root -OutDir $Cfg.ModBuildDir -AceDir $Cfg.AceDir -AceServerDllSha256 $aceSha -AceCommit ([string]$stamp.commit) -BuildKey $key
    Write-LsInfo "built with SDK $($build.Sdk): version $($build.Version), DLL SHA-256 $($build.Sha256); output holds exactly $($script:ModFiles -join ', ')"
    $sha = Install-LsServerMod -Cfg $Cfg -FromDir $Cfg.ModBuildDir
    Write-LsInfo "Installed into $($Cfg.ModInstallDir) (DLL SHA-256 $sha)"
    $State.modBuildKey = $key
    $State.modDllSha256 = $sha
    $State.modVersion = $build.Version
    $State.modBuiltUtc = (Get-Date).ToUniversalTime().ToString('o')
    $default = [System.IO.Path]::GetFullPath($Cfg.ModRoot).TrimEnd('\')
    if ($root -ieq $default) { [void]$State.Remove('modSource') } else { $State.modSource = $root }
    return [pscustomobject]@{ Built = $true; Key = $key; DllSha256 = $sha; Version = $build.Version }
}

# What start-server.ps1 checks before it starts ACE with the mod. Returns
# @{ Installed; DllSha256; Version; Problems (refuse); Stale; StaleReason; Source; ExpectedKey; RecordedKey }.
# Stale means the mod's sources (setup-state modSource when that tree still exists, else server\mod) or ACE changed
# since setup built the installed mod.
function Get-LsServerModInstall($cfg) {
    $state = Get-LsState $cfg
    $problems = @()
    $installed = (Test-Path -LiteralPath $cfg.ModDll -PathType Leaf) -and (Test-Path -LiteralPath $cfg.ModMeta -PathType Leaf)
    $dllSha = $null
    $version = $null
    if ($installed) {
        $dllSha = Get-LsSha256 $cfg.ModDll
        try { $version = [string]([System.IO.File]::ReadAllText($cfg.ModMeta, [System.Text.Encoding]::UTF8) | ConvertFrom-Json).Version }
        catch { $problems += "$($cfg.ModMeta) cannot be read ($($_.Exception.GetType().Name))" }
        $recorded = [string]$state['modDllSha256']
        if (-not $recorded) {
            $problems += 'setup-state.json records no installed mod (modDllSha256), so the installed one was not put there by setup; run server\setup-server.ps1 -ModOnly'
        } elseif ($recorded -ne $dllSha) {
            $problems += "the installed Lifestone.ServerMod.dll (SHA-256 $dllSha) is not the one setup installed ($recorded); run server\setup-server.ps1 -ModOnly"
        }
        foreach ($p in @(Get-LsServerModOutputProblems $cfg.ModInstallDir)) { $problems += "the installed mod folder: $p" }
    } else {
        $problems += "the Lifestone server mod is not installed in $($cfg.ModInstallDir); run server\setup-server.ps1 -ModOnly (or the full setup)"
    }
    $source = $cfg.ModRoot
    $recordedSource = [string]$state['modSource']
    if ($recordedSource -and (Test-Path -LiteralPath $recordedSource -PathType Container)) { $source = $recordedSource }
    $recordedKey = [string]$state['modBuildKey']
    $expected = $null
    $stale = $false
    $staleReason = $null
    try {
        $stamp = Get-LsAceBuildStamp $cfg
        $expected = Get-LsServerModBuildKey -ModRoot $source -AceServerDllSha256 ([string]$stamp.aceServerDllSha256)
        if ($expected -ne $recordedKey) {
            $stale = $true
            $staleReason = "the server mod's sources ($source) or ACE changed since setup built the installed mod (build key $(if ($recordedKey) { $recordedKey.Substring(0, 12) } else { 'none' }), now $($expected.Substring(0, 12)))"
        }
    } catch {
        $stale = $true
        $staleReason = "the mod's build key cannot be computed: $($_.Exception.Message)"
    }
    return [pscustomobject]@{
        Installed = $installed; DllSha256 = $dllSha; Version = $version; Problems = $problems
        Stale = $stale; StaleReason = $staleReason; Source = $source; ExpectedKey = $expected; RecordedKey = $recordedKey
    }
}

# Other mods in ace-mods (folders with a Meta.json, which ACE loads, ModManager.cs:113-123). Lifestone does not vet
# third-party mods, which may patch ACE with Harmony: start-server.ps1 warns about each by folder name.
function Get-LsOtherAceMods($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.AceModsDir -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $cfg.AceModsDir -Directory -Force |
            Where-Object { $_.Name -ne $cfg.ModName -and (Test-Path -LiteralPath (Join-Path $_.FullName 'Meta.json') -PathType Leaf) } |
            ForEach-Object { $_.Name })
}

# ---------------------------------------------------------------------------------------------------------------
# ACE log lines (the mod's contract, m2-plan 4.12; ACE's own mod-loader lines, ModContainer.cs and ModManager.cs)
# ---------------------------------------------------------------------------------------------------------------

# ACE_Log.txt lines look like '<date> <time> [<thread>] <LEVEL> (<logger>) <message>' (templates\log4net.template.config).
# The mod's lines and ACE's mod-loader lines come from the loggers ACE.Server.Mods.ModManager (ModManager.Log, which
# the mod writes through) and ACE.Server.Mods.ModContainer, so each pattern is anchored on the whole line prefix: text
# a player typed, which a 'Lifestone tool:' line quotes inside its own message, can never pass for one of these lines.
$script:ModLinePrefix = '^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d,\d{3} \[[^\]]*\] [A-Z]+ +\(ACE\.Server\.Mods\.Mod(?:Manager|Container)\) '
$script:ModReadyPattern = $script:ModLinePrefix + 'Lifestone mod ready: '
$script:ModFailurePatterns = @(
    'Lifestone mod refused to load: ',
    'Lifestone mod not ready: ',
    'Lifestone startup watch failed',
    'Error enabling Lifestone: ',
    'Failed to load mod file .*Lifestone\.ServerMod',
    'Missing IHarmonyMod Type Lifestone\.ServerMod',
    'Failed to create Mod instance: Lifestone\b',
    'Missing mod: .*Lifestone\.ServerMod',
    'Unable to deserialize mod metadata from: .*Lifestone\.ServerMod',
    'Metadata not found at: .*Lifestone\.ServerMod',
    'Unable to activate mod that failed to load: Lifestone\b'
) | ForEach-Object { $script:ModLinePrefix + $_ }
$script:AceFatalPattern = 'ACEmulator will now abort startup|Unable to load or parse starterGear|Attempting to reconnect to'
$script:WorldOpenPattern = 'World is now open'

# What one ACE log line means to start-server.ps1: 'ModFailed' (the mod did not load or will never be ready),
# 'ModReady', 'Fatal' (ACE aborts its boot), 'WorldOpen', or $null.
function Get-LsAceLogSignal([string]$Line) {
    if (-not $Line) { return $null }
    foreach ($p in $script:ModFailurePatterns) { if ($Line -match $p) { return 'ModFailed' } }
    if ($Line -match $script:ModReadyPattern) { return 'ModReady' }
    if ($Line -match $script:AceFatalPattern) { return 'Fatal' }
    if ($Line -match $script:WorldOpenPattern) { return 'WorldOpen' }
    return $null
}

# What start-server.ps1 has read of ACE's log so far: ReadyLine and FailureLine (the first of each), Fatal, WorldOpen
# and WorldOpenUtc (when the first 'World is now open' line was read), and Pending, the text after the last line break
# of the last read, which is not a whole line yet. Under -AllowNoMod (AllowNoMod true) the mod's lines are only noted
# (IgnoredReadyLine; Signal on each line Add-LsAceLogText returns): that start skipped the mod's install checks, so its
# ready line never counts, and a mod that fails to load does not stop it.
function New-LsAceLogWatch([bool]$AllowNoMod = $false, [long]$Offset = 0) {
    return [pscustomobject]@{
        AllowNoMod = $AllowNoMod; Offset = $Offset; Decoder = (New-Object System.Text.UTF8Encoding($false)).GetDecoder()
        Pending = ''; ReadyLine = $null; FailureLine = $null; IgnoredReadyLine = $null; Fatal = $false; WorldOpen = $false
        WorldOpenUtc = $null
    }
}

# The text ACE appended to its log at $Path since $Watch.Offset (a byte offset, which this moves on), or '' when there
# is none. The bytes go through $Watch's UTF-8 decoder, which keeps a character cut at the end of one read for the
# next. A file shorter than Offset has rolled over: it is read from its start, and the pending text of the old file is
# dropped (its end went to the rolled file).
function Read-LsAceLogChunk($Watch, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($fs.Length -lt $Watch.Offset) { $Watch.Offset = 0; $Watch.Pending = ''; $Watch.Decoder.Reset() }
        $count = [int]($fs.Length - $Watch.Offset)
        if ($count -le 0) { return '' }
        [void]$fs.Seek($Watch.Offset, 'Begin')
        $bytes = New-Object byte[] $count
        $read = 0
        while ($read -lt $count) {
            $n = $fs.Read($bytes, $read, $count - $read)
            if ($n -le 0) { break }
            $read += $n
        }
        $Watch.Offset += $read
    } finally { $fs.Dispose() }
    $chars = New-Object char[] ([System.Text.Encoding]::UTF8.GetMaxCharCount($read))
    $n = $Watch.Decoder.GetChars($bytes, 0, $read, $chars, 0)
    return [string]::new($chars, 0, $n)
}

# Reads one chunk of ACE's log into $Watch and returns its whole lines, as @([pscustomobject]@{ Line; Signal }) in
# order (Signal as Get-LsAceLogSignal gives it). A read can end inside a line that ACE is still writing: that text is
# kept in $Watch.Pending and read with the next chunk, so a line is judged only once it is whole. -Final (ACE has
# exited, or the wait is over) judges the pending text as it is. $NowUtc (default: the clock) is when the text was
# read; the first 'World is now open' line records it as $Watch.WorldOpenUtc.
function Add-LsAceLogText($Watch, [string]$Text, [switch]$Final, $NowUtc = $null) {
    $parts = @(($Watch.Pending + $Text) -split "`n")
    $whole = $parts.Count - 1
    if ($Final) { $whole = $parts.Count }
    $Watch.Pending = $(if ($Final) { '' } else { $parts[$parts.Count - 1] })
    $result = @()
    for ($i = 0; $i -lt $whole; $i++) {
        $line = $parts[$i].TrimEnd("`r").TrimStart([char]0xFEFF)
        if (-not $line) { continue }
        $signal = Get-LsAceLogSignal $line
        switch ($signal) {
            'ModFailed' { if (-not $Watch.AllowNoMod -and -not $Watch.FailureLine) { $Watch.FailureLine = $line } }
            'ModReady' {
                if ($Watch.AllowNoMod) { if (-not $Watch.IgnoredReadyLine) { $Watch.IgnoredReadyLine = $line } }
                elseif (-not $Watch.ReadyLine) { $Watch.ReadyLine = $line }
            }
            'Fatal' { $Watch.Fatal = $true }
            'WorldOpen' {
                if (-not $Watch.WorldOpen) { $Watch.WorldOpenUtc = $(if ($null -ne $NowUtc) { [DateTime]$NowUtc } else { [DateTime]::UtcNow }) }
                $Watch.WorldOpen = $true
            }
        }
        $result += [pscustomobject]@{ Line = $line; Signal = $signal }
    }
    return $result
}

# How long start-server.ps1 waits, after ACE's 'World is now open', for the mod's ready line or a failure line. The
# mod logs its ready line within about 100 ms of the world opening (StartupWatch polls the world status every 100 ms),
# so a long silence means ACE never loaded it.
$script:ModSilenceGraceSec = 30

# Why start-server.ps1 must stop waiting for a mod that never spoke, or $null: ACE opened the world at least
# $GraceSec seconds before $NowUtc and the log has neither the mod's ready line nor a failure line. That is a mod ACE
# never loaded and never logged about, for example when Config.js ModsDirectory does not point at runtime\ace-mods,
# so ModManager looks in another folder. Always $null under -AllowNoMod, which waits for the world only.
function Get-LsModSilenceProblem($Watch, [DateTime]$NowUtc, [int]$GraceSec = $script:ModSilenceGraceSec) {
    if ($Watch.AllowNoMod -or $Watch.ReadyLine -or $Watch.FailureLine -or $null -eq $Watch.WorldOpenUtc) { return $null }
    $silent = ($NowUtc - [DateTime]$Watch.WorldOpenUtc).TotalSeconds
    if ($silent -lt $GraceSec) { return $null }
    return ("ACE opened the world {0} s ago but the Lifestone mod never reported (no 'Lifestone mod ready:' or failure line); ACE probably did not load it: check Config.js ModsDirectory and runtime\ace-mods\Lifestone.ServerMod" -f [int][Math]::Floor($silent))
}

# The mod fields of pids.json's ace entry (New-LsPidEntry's $Mod) once ACE is up. Under -AllowNoMod
# ($Watch.AllowNoMod, or no $Mod install record to check against) they are always @{ Ready = $false }, whatever the
# log said: that start never checked that the DLL is the one setup installed, or that it is current. Otherwise the
# ready line must be in the expected form and report the configured endpoint and the installed version; the DLL's
# SHA-256 is recorded with it.
function Get-LsStartedAceMod($cfg, $Watch, $Mod) {
    if ($Watch.AllowNoMod -or -not $Mod) { return @{ Ready = $false } }
    if (-not $Watch.ReadyLine) { throw "The Lifestone mod's ready line was not seen." }
    $info = Get-LsModReadyInfo $Watch.ReadyLine
    if (-not $info) { throw "The Lifestone mod's ready line is not in the expected form: $($Watch.ReadyLine -replace '^\S+ \S+ \[[^\]]*\] ', '')" }
    if ($info.Host -ne $cfg.Host -or $info.Port -ne $cfg.AcePort) { throw "The Lifestone mod reports $($info.Host):$($info.Port), not $($cfg.Host):$($cfg.AcePort)." }
    if ($info.Version -ne $Mod.Version) { throw "The running Lifestone mod is version $($info.Version), but the installed one is $($Mod.Version)." }
    return @{ Ready = $true; Version = $info.Version; Wire = $info.Wire; DllSha256 = (Get-LsSha256 $cfg.ModDll); ReadyUtc = (Get-Date).ToUniversalTime().ToString('o'); Ace = $info.Ace }
}

# The fields of the mod's ready line ('Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE
# 95ef426d; auto-promotion off'), or $null when the line is not one.
function Get-LsModReadyInfo([string]$Line) {
    $m = [regex]::Match([string]$Line, $script:ModReadyPattern + '@lifestone on (?<host>[0-9.]+):(?<port>\d{1,5}); wire (?<wire>\d{1,4}); mod (?<version>[0-9A-Za-z.+-]{1,32}); ACE (?<ace>[0-9a-f]{1,40}); auto-promotion off\s*$')
    if (-not $m.Success) { return $null }
    return [pscustomobject]@{
        Host = $m.Groups['host'].Value; Port = [int]$m.Groups['port'].Value; Wire = [int]$m.Groups['wire'].Value
        Version = $m.Groups['version'].Value; Ace = $m.Groups['ace'].Value
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Backend state (m2-plan 4.12; read by tools\play.ps1 and the lead's merge procedure)
# ---------------------------------------------------------------------------------------------------------------

# Which backend processes run, from pids.json and the live processes (Get-LsLiveProcess: PID, path and start time
# must all match), plus any process running a runtime executable that pids.json does not track (Stray). Sends
# nothing to the servers. ModReady is pids.json ace.modReady, a JSON true written by start-server.ps1 once the mod's
# ready line was seen, for a live ACE only.
function Get-LsBackendState($cfg) {
    $pids = Get-LsPids $cfg
    $db = $null
    $ace = $null
    $aceEntry = $null
    if ($pids) {
        if ($pids.PSObject.Properties['mariadbd']) { $db = Get-LsLiveProcess $pids.mariadbd }
        if ($pids.PSObject.Properties['ace']) { $aceEntry = $pids.ace; $ace = Get-LsLiveProcess $aceEntry }
    }
    $tracked = @(@($db, $ace) | Where-Object { $_ } | ForEach-Object { $_.Id })
    $stray = @(Get-LsRuntimeProcesses $cfg | Where-Object { $tracked -notcontains $_.Id } | ForEach-Object { $_.Id })
    $modReady = $false
    $modVersion = ''
    $modWire = 0
    if ($ace -and $aceEntry.PSObject.Properties['modReady'] -and ($aceEntry.modReady -is [bool]) -and $aceEntry.modReady) {
        $modReady = $true
        if ($aceEntry.PSObject.Properties['modVersion']) { $modVersion = [string]$aceEntry.modVersion }
        if ($aceEntry.PSObject.Properties['modWire']) { $modWire = [int]$aceEntry.modWire }
    }
    return [pscustomobject]@{
        MariaDb    = [bool]$db
        Ace        = [bool]$ace
        ModReady   = $modReady
        ModVersion = $modVersion
        ModWire    = $modWire
        AcePid     = $(if ($ace) { [int]$ace.Id } else { 0 })
        MariaDbPid = $(if ($db) { [int]$db.Id } else { 0 })
        Stray      = $stray
    }
}

# A pids.json value fit to write back: PowerShell 7's ConvertFrom-Json turns ISO date strings into DateTime.
function ConvertTo-LsJsonValue($Value) {
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture) }
    return $Value
}

# ---------------------------------------------------------------------------------------------------------------
# secrets.json schema 2 (m2-plan SM5, owner decision D10)
# ---------------------------------------------------------------------------------------------------------------
#
# {
#   "schema": 2, "note": "...", "generatedUtc": "...",
#   "mariadb": { "host", "port", "rootUser", "rootPassword", "aceUser", "acePassword" },   (unchanged from schema 1)
#   "defaultAccount": "play",
#   "accounts": {
#     "play": { "username": "lifestone",      "password": "<20>", "note": "..." },
#     "test": { "username": "lifestone-test", "password": "<20>", "note": "..." },
#     "<name>": { "username": "<name>", "password": "<20>", "note": "..." }     (setup-server.ps1 -AddAccount <name>)
#   }
# }
# Schema 1 had 'testAccount' in place of 'accounts'; its username and password become accounts.play. Every write goes
# through Write-LsSecrets. No function here prints, logs or returns a password.

$script:SecretsNote = 'Generated by server/setup-server.ps1. Local-only credentials for the Lifestone backend. Never commit, print or share.'
$script:PlayAccountNote = 'Your play account: a normal Player account, created by ACE on its first login.'
$script:TestAccountNote = 'Loopback tests and smoke only, never for play: a login boots any session of the same account.'
$script:ReservedAccountKeys = @('play', 'test')

function Get-LsSecretsSchema($Secrets) {
    if ($Secrets.PSObject.Properties['schema']) { return [int]$Secrets.schema }
    return 1
}

# The game accounts a secrets.json names, as @([pscustomobject]@{ Key; Username }): never a password.
function Get-LsSecretsAccounts($Secrets) {
    $result = @()
    if ($Secrets.PSObject.Properties['accounts'] -and $Secrets.accounts) {
        foreach ($p in $Secrets.accounts.PSObject.Properties) {
            if ($p.Value -and $p.Value.PSObject.Properties['username']) { $result += [pscustomobject]@{ Key = $p.Name; Username = [string]$p.Value.username } }
        }
    } elseif ($Secrets.PSObject.Properties['testAccount'] -and $Secrets.testAccount -and $Secrets.testAccount.PSObject.Properties['username']) {
        $result += [pscustomobject]@{ Key = 'testAccount'; Username = [string]$Secrets.testAccount.username }
    }
    return $result
}

# What is wrong with a schema-2 document, by key name only (an empty list when nothing is).
function Get-LsSecretsProblems($Secrets) {
    $problems = @()
    if ((Get-LsSecretsSchema $Secrets) -ne 2) { return @('schema is not 2') }
    if (-not ($Secrets.PSObject.Properties['mariadb'] -and $Secrets.mariadb)) { $problems += 'mariadb is missing' }
    else {
        foreach ($k in @('rootPassword', 'acePassword', 'aceUser')) {
            if (-not ($Secrets.mariadb.PSObject.Properties[$k] -and [string]$Secrets.mariadb.$k)) { $problems += "mariadb.$k is missing or empty" }
        }
    }
    if (-not ($Secrets.PSObject.Properties['accounts'] -and $Secrets.accounts)) { return $problems + 'accounts is missing' }
    $names = @{}
    foreach ($p in $Secrets.accounts.PSObject.Properties) {
        $a = $p.Value
        if ($script:ReservedAccountKeys -notcontains $p.Name -and $p.Name -cnotmatch $script:AccountNamePattern) { $problems += "accounts has a key that is not a valid account name" }
        if (-not ($a -and $a.PSObject.Properties['username'] -and [string]$a.username)) { $problems += "accounts.$($p.Name).username is missing or empty"; continue }
        if (-not ($a.PSObject.Properties['password'] -and [string]$a.password)) { $problems += "accounts.$($p.Name).password is missing or empty" }
        $u = ([string]$a.username).ToLowerInvariant()
        if ($names.ContainsKey($u)) { $problems += "accounts.$($p.Name) and accounts.$($names[$u]) have the same username" } else { $names[$u] = $p.Name }
    }
    foreach ($k in $script:ReservedAccountKeys) { if (-not $Secrets.accounts.PSObject.Properties[$k]) { $problems += "accounts.$k is missing" } }
    if (-not ($Secrets.PSObject.Properties['defaultAccount'] -and $Secrets.accounts.PSObject.Properties[[string]$Secrets.defaultAccount])) {
        $problems += 'defaultAccount does not name an entry of accounts'
    }
    return $problems
}

# What a rewrite of $Original into $New would lose (an empty list when nothing): every mariadb value, and every
# account's username and password (schema 1's testAccount must reappear as accounts.play). Keys only in messages.
function Get-LsSecretsLossProblems($Original, $New) {
    $problems = @()
    $newMariaDb = $null
    if ($New.PSObject.Properties['mariadb']) { $newMariaDb = $New.mariadb }
    $newAccounts = $null
    if ($New.PSObject.Properties['accounts']) { $newAccounts = $New.accounts }
    if ($Original.PSObject.Properties['mariadb'] -and $Original.mariadb) {
        foreach ($p in $Original.mariadb.PSObject.Properties) {
            if (-not ($newMariaDb -and $newMariaDb.PSObject.Properties[$p.Name] -and [string]$newMariaDb.($p.Name) -ceq [string]$p.Value)) { $problems += "mariadb.$($p.Name) changed" }
        }
    }
    $pairs = @()
    if ($Original.PSObject.Properties['accounts'] -and $Original.accounts) {
        foreach ($p in $Original.accounts.PSObject.Properties) { $pairs += , @($p.Name, $p.Value) }
    } elseif ($Original.PSObject.Properties['testAccount'] -and $Original.testAccount) {
        $pairs += , @('play', $Original.testAccount)
    }
    foreach ($pair in $pairs) {
        $key = $pair[0]
        $old = $pair[1]
        $now = $null
        if ($newAccounts -and $newAccounts.PSObject.Properties[$key]) { $now = $newAccounts.$key }
        foreach ($field in @('username', 'password')) {
            $was = ''
            if ($old -and $old.PSObject.Properties[$field]) { $was = [string]$old.$field }
            $is = ''
            if ($now -and $now.PSObject.Properties[$field]) { $is = [string]$now.$field }
            if ($was -cne $is) { $problems += "accounts.$key.$field changed" }
        }
    }
    return $problems
}

# An ordered copy of a schema-2 document, to change and write back (its mariadb values and every account kept as is).
function ConvertTo-LsSecretsDocument($Secrets) {
    $doc = [ordered]@{ schema = 2; note = $script:SecretsNote }
    $doc.generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    if ($Secrets.PSObject.Properties['generatedUtc']) { $doc.generatedUtc = ConvertTo-LsJsonValue $Secrets.generatedUtc }
    foreach ($k in @('migratedUtc')) { if ($Secrets.PSObject.Properties[$k]) { $doc[$k] = ConvertTo-LsJsonValue $Secrets.$k } }
    $mariadb = [ordered]@{}
    foreach ($p in $Secrets.mariadb.PSObject.Properties) { $mariadb[$p.Name] = ConvertTo-LsJsonValue $p.Value }
    $doc.mariadb = $mariadb
    $doc.defaultAccount = [string]$Secrets.defaultAccount
    $accounts = [ordered]@{}
    foreach ($p in $Secrets.accounts.PSObject.Properties) {
        $entry = [ordered]@{}
        foreach ($f in $p.Value.PSObject.Properties) { $entry[$f.Name] = ConvertTo-LsJsonValue $f.Value }
        $accounts[$p.Name] = $entry
    }
    $doc.accounts = $accounts
    return $doc
}

function New-LsAccountEntry([string]$Username, [string]$Note) {
    return [ordered]@{ username = $Username; password = (New-LsSecret 20); note = $Note }
}

# Writes secrets.json (schema 2) without ever leaving its content readable by others: the new text goes into
# secrets.json.tmp, which is created empty and restricted to the current user (Protect-LsFile) before anything is
# written into it. It is then read back and checked (shape, and that nothing of $Original was lost), and moved over
# secrets.json with File.Replace (no backup copy is made), which is restricted again. On any failure the .tmp is
# deleted and secrets.json is left as it was.
function Write-LsSecrets {
    param([Parameter(Mandatory = $true)]$Cfg, [Parameter(Mandatory = $true)]$Document, $Original = $null)
    $target = $Cfg.Secrets
    $tmp = $target + '.tmp'
    New-LsDirectory (Split-Path -Parent $target)
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    try {
        [System.IO.File]::WriteAllBytes($tmp, (New-Object byte[] 0))
        Protect-LsFile $tmp
        $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes((ConvertTo-Json -InputObject $Document -Depth 8))
        # Truncate opens the existing, already protected file: it is never created again with inherited permissions.
        $stream = [System.IO.File]::Open($tmp, [System.IO.FileMode]::Truncate, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        [Array]::Clear($bytes, 0, $bytes.Length)
        $back = $null
        try { $back = [System.IO.File]::ReadAllText($tmp, [System.Text.Encoding]::UTF8) | ConvertFrom-Json }
        catch { throw "the new secrets.json did not read back as JSON ($($_.Exception.GetType().Name))" }
        $problems = @(Get-LsSecretsProblems $back)
        if ($null -ne $Original) { $problems += @(Get-LsSecretsLossProblems $Original $back) }
        if ($problems.Count) { throw "the new secrets.json failed its checks: $($problems -join '; ')" }
        # [NullString]::Value: PowerShell would pass $null to a string parameter as "", which File.Replace rejects.
        if (Test-Path -LiteralPath $target) { [System.IO.File]::Replace($tmp, $target, [NullString]::Value) }
        else { [System.IO.File]::Move($tmp, $target) }
        Protect-LsFile $target
    } catch {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        throw "secrets.json was not written: $($_.Exception.Message)"
    }
}

# Makes sure secrets.json exists in schema 2: writes a new one (new MariaDB and account passwords) when there is none,
# migrates schema 1 (its testAccount becomes accounts.play, unchanged; accounts.test is new) and checks schema 2.
# Returns @{ Action = 'created'|'migrated'|'kept'; Keys = @(account keys) }.
function Initialize-LsSecrets($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.Secrets)) {
        $doc = [ordered]@{
            schema         = 2
            note           = $script:SecretsNote
            generatedUtc   = (Get-Date).ToUniversalTime().ToString('o')
            mariadb        = [ordered]@{ host = $cfg.Host; port = $cfg.DbPort; rootUser = 'root'; rootPassword = (New-LsSecret 32); aceUser = [string]$cfg.Versions.server.aceDbUser; acePassword = (New-LsSecret 32) }
            defaultAccount = 'play'
            accounts       = [ordered]@{
                play = New-LsAccountEntry $cfg.PlayAccountName $script:PlayAccountNote
                test = New-LsAccountEntry $cfg.TestAccountName $script:TestAccountNote
            }
        }
        Write-LsSecrets -Cfg $cfg -Document $doc
        return [pscustomobject]@{ Action = 'created'; Keys = @('play', 'test') }
    }
    $current = Get-LsSecrets $cfg
    $schema = Get-LsSecretsSchema $current
    if ($schema -eq 2) {
        $problems = @(Get-LsSecretsProblems $current)
        if ($problems.Count) { throw "$($cfg.Secrets) (schema 2) is not valid: $($problems -join '; '). Fix it by hand; setup never rewrites a schema-2 file it cannot read." }
        return [pscustomobject]@{ Action = 'kept'; Keys = @($current.accounts.PSObject.Properties | ForEach-Object { $_.Name }) }
    }
    if ($schema -ne 1) { throw "$($cfg.Secrets) has schema $schema; this setup knows schemas 1 and 2." }
    if (-not ($current.PSObject.Properties['mariadb'] -and $current.mariadb)) { throw "$($cfg.Secrets) (schema 1) has no mariadb section; it cannot be migrated." }

    $doc = [ordered]@{ schema = 2; note = $script:SecretsNote }
    $doc.generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    if ($current.PSObject.Properties['generatedUtc']) { $doc.generatedUtc = ConvertTo-LsJsonValue $current.generatedUtc }
    $doc.migratedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $mariadb = [ordered]@{}
    foreach ($p in $current.mariadb.PSObject.Properties) { $mariadb[$p.Name] = ConvertTo-LsJsonValue $p.Value }
    $doc.mariadb = $mariadb
    $doc.defaultAccount = 'play'
    $play = $null
    if ($current.PSObject.Properties['testAccount'] -and $current.testAccount -and
        $current.testAccount.PSObject.Properties['username'] -and $current.testAccount.PSObject.Properties['password'] -and
        [string]$current.testAccount.username -and [string]$current.testAccount.password) {
        $play = [ordered]@{ username = [string]$current.testAccount.username; password = [string]$current.testAccount.password; note = $script:PlayAccountNote }
    } else {
        $play = New-LsAccountEntry $cfg.PlayAccountName $script:PlayAccountNote
    }
    $doc.accounts = [ordered]@{ play = $play; test = (New-LsAccountEntry $cfg.TestAccountName $script:TestAccountNote) }
    Write-LsSecrets -Cfg $cfg -Document $doc -Original $current
    return [pscustomobject]@{ Action = 'migrated'; Keys = @('play', 'test') }
}

# setup-server.ps1 -AddAccount: a new schema-2 entry { username = <name>, password = 20 random letters and digits }
# under the key <name>. ACE creates the account at its first login, as a Player (the mod keeps auto-promotion off).
# Refuses an invalid name, the reserved keys play and test, the sentinel Admin's name and any key or username already
# there. Returns the name; never the password.
function Add-LsAccount {
    param([Parameter(Mandatory = $true)]$Cfg, [Parameter(Mandatory = $true)][string]$Name)
    if ($Name -cnotmatch $script:AccountNamePattern) {
        throw "An account name must be 3 to 50 lower-case letters, digits and hyphens, starting with a letter or digit ($script:AccountNamePattern)."
    }
    if ($script:ReservedAccountKeys -contains $Name) { throw "'$Name' is a reserved key of secrets.json (the play and test accounts); choose another name." }
    if ($Name -eq $Cfg.SentinelAdminName) { throw "'$Name' is the sentinel Admin account's name; choose another name." }
    $current = Get-LsSecrets $Cfg
    if ((Get-LsSecretsSchema $current) -ne 2) { throw "$($Cfg.Secrets) is not schema 2; run server\setup-server.ps1 once to migrate it." }
    $problems = @(Get-LsSecretsProblems $current)
    if ($problems.Count) { throw "$($Cfg.Secrets) is not valid: $($problems -join '; '); no account was added." }
    foreach ($a in @(Get-LsSecretsAccounts $current)) {
        if ($a.Key -ieq $Name -or $a.Username -ieq $Name) { throw "secrets.json already has an account '$Name' (key '$($a.Key)')." }
    }
    $doc = ConvertTo-LsSecretsDocument $current
    $doc.accounts[$Name] = New-LsAccountEntry $Name 'Added by server/setup-server.ps1 -AddAccount: a normal Player account, created by ACE on its first login.'
    Write-LsSecrets -Cfg $Cfg -Document $doc -Original $current
    return $Name
}

# 'schema 2 (accounts: play, test)' for the console: keys only.
function Format-LsSecretsSummary($Secrets) {
    $keys = @(Get-LsSecretsAccounts $Secrets | ForEach-Object { $_.Key })
    return "schema $(Get-LsSecretsSchema $Secrets) (accounts: $($keys -join ', '))"
}

# Every password in a secrets object (any string under a key containing 'password', at any depth: schema 1 and 2).
function Get-LsSecretPasswords($Node, [System.Collections.Generic.List[string]]$Into) {
    if ($null -eq $Node) { return }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($p in $Node.PSObject.Properties) {
            if ($p.Value -is [string]) { if ($p.Name -match 'password' -and $p.Value) { $Into.Add($p.Value) } }
            else { Get-LsSecretPasswords $p.Value $Into }
        }
    } elseif ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
        foreach ($item in $Node) { Get-LsSecretPasswords $item $Into }
    }
}

# How often the needles (byte strings held as Latin-1 text, one char per byte) occur in a file, all needles together.
# Read in 4 MiB chunks that overlap by the longest needle less one byte, so each match counts once; opened with full
# sharing, so a log ACE is still writing can be read.
function Measure-LsNeedles([string]$Path, [string[]]$Needles) {
    if (-not $Needles -or $Needles.Count -eq 0) { return 0 }
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $overlap = [int](($Needles | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum) - 1
    $buffer = New-Object byte[] (4MB + $overlap)
    $count = 0
    $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, $buffer.Length - $carry)) -gt 0) {
            $length = $carry + $read
            $text = $latin1.GetString($buffer, 0, $length)
            foreach ($needle in $Needles) {
                $at = $text.IndexOf($needle, [StringComparison]::Ordinal)
                while ($at -ge 0) {
                    # A match wholly inside the carried overlap was counted with the previous chunk.
                    if ($at + $needle.Length -gt $carry) { $count++ }
                    $at = $text.IndexOf($needle, $at + 1, [StringComparison]::Ordinal)
                }
            }
            $carry = [Math]::Min($overlap, $length)
            [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry)
        }
    } finally { $stream.Dispose(); [Array]::Clear($buffer, 0, $buffer.Length) }
    return $count
}

# Counts every password of secrets.json (as UTF-8 and as UTF-16LE bytes) in the given files and in every file under
# the given folders. Returns @([pscustomobject]@{ File; Count }) for every file scanned, Count -1 for a file that could
# not be read. The passwords stay in this function; the result and any error hold no value.
function Find-LsSecretLeaks {
    param([Parameter(Mandatory = $true)]$Cfg, [Parameter(Mandatory = $true)][string[]]$Path)
    $passwords = New-Object System.Collections.Generic.List[string]
    Get-LsSecretPasswords (Get-LsSecrets $Cfg) $passwords
    if ($passwords.Count -eq 0) { throw "$($Cfg.Secrets) holds no password (an unknown schema?), so nothing can be scanned." }
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $needles = @()
    foreach ($p in @($passwords | Sort-Object -Unique)) {
        $needles += $latin1.GetString([System.Text.Encoding]::UTF8.GetBytes($p))
        $needles += $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($p))
    }
    $passwords.Clear()
    $secretsFull = [System.IO.Path]::GetFullPath($Cfg.Secrets)
    $files = @()
    foreach ($p in $Path) {
        if (Test-Path -LiteralPath $p -PathType Leaf) { $files += [System.IO.Path]::GetFullPath($p) }
        elseif (Test-Path -LiteralPath $p -PathType Container) { $files += @(Get-ChildItem -LiteralPath $p -Recurse -File -Force | ForEach-Object { $_.FullName }) }
    }
    $result = @()
    foreach ($f in @($files | Sort-Object -Unique)) {
        # secrets.json itself (and its .tmp) hold the passwords by design.
        if ($f -ieq $secretsFull -or $f -ieq ($secretsFull + '.tmp')) { continue }
        $n = -1
        try { $n = Measure-LsNeedles $f $needles } catch { $n = -1 }
        $result += [pscustomobject]@{ File = $f; Count = $n }
    }
    return $result
}

# ---------------------------------------------------------------------------------------------------------------
# ACE accounts: the sentinel Admin (owner decision D11) and the audit (m2-plan SM4); MariaDB must be running
# ---------------------------------------------------------------------------------------------------------------

# The sentinel Admin account's ban: until 2099, with this reason (ACE shows it to anyone who tries to log in).
$script:SentinelBanExpire = '2099-12-31 00:00:00'
$script:SentinelBanReason = 'Lifestone sentinel Admin: it exists so that ACE never promotes a new account to Admin; nobody logs in with it.'

# The columns, as SQL values, that make an account the sentinel Admin: AccessLevel Admin (5), a fresh discarded
# password and the ban. The password is 32 random letters and digits, hashed the way ACE still accepts (SHA-512 of the
# password's UTF-8 bytes and a 64-byte random salt, both Base64; AccountExtensions.cs PasswordMatches and
# GetPasswordHash) and then dropped: nobody, Lifestone included, can ever log in with it. The ban stops even a password
# someone knew: ACE refuses a banned account after the password (AuthenticationHandler.cs:215-227).
function New-LsSentinelAdminValues {
    $password = New-LsSecret 32
    $salt = New-Object byte[] 64
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $passwordBytes = [System.Text.Encoding]::UTF8.GetBytes($password)
    $password = $null
    $buffer = New-Object byte[] ($passwordBytes.Length + $salt.Length)
    [Array]::Copy($passwordBytes, 0, $buffer, 0, $passwordBytes.Length)
    [Array]::Copy($salt, 0, $buffer, $passwordBytes.Length, $salt.Length)
    $sha = [System.Security.Cryptography.SHA512]::Create()
    try { $hash = [Convert]::ToBase64String($sha.ComputeHash($buffer)) } finally { $sha.Dispose() }
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
    [Array]::Clear($buffer, 0, $buffer.Length)
    $saltText = [Convert]::ToBase64String($salt)
    return [ordered]@{
        passwordHash = "'$hash'"; passwordSalt = "'$saltText'"; accessLevel = '5'; banned_Time = 'UTC_TIMESTAMP()'
        ban_Expire_Time = "'$($script:SentinelBanExpire)'"; ban_Reason = "'$($script:SentinelBanReason)'"
    }
}

# The SQL that creates the sentinel Admin account when no account has its name. ACE sets its promote-the-next-account
# flag at boot only when no account has AccessLevel Admin (ACE.Database/DatabaseManager.cs:30-36), so with this
# account in ace_auth it never does, mod or no mod.
function New-LsSentinelAdminSql([string]$Name) {
    if ($Name -cnotmatch $script:AccountNamePattern) { throw 'The sentinel Admin account name is not a valid account name.' }
    $values = New-LsSentinelAdminValues
    return ("INSERT INTO ace_auth.account (accountName, " + ($values.Keys -join ', ') + ") " +
        "SELECT '$Name', " + ($values.Values -join ', ') + " " +
        "FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM ace_auth.account WHERE accountName = '$Name');")
}

# The SQL that makes an existing account of that name the sentinel Admin again, credentials and ban included. A row
# below Admin may be one ACE created at a login (AllowAutoAccountCreation), with a password its owner knows: raising
# only its level would make a loginable Admin.
function New-LsSentinelAdminResetSql([string]$Name) {
    if ($Name -cnotmatch $script:AccountNamePattern) { throw 'The sentinel Admin account name is not a valid account name.' }
    $values = New-LsSentinelAdminValues
    return ("UPDATE ace_auth.account SET " + (@($values.Keys | ForEach-Object { "$_ = $($values[$_])" }) -join ', ') +
        " WHERE accountName = '$Name';")
}

# The query for the sentinel account's state: one tab-separated row of its AccessLevel, 1 when its ban runs past now,
# and 1 when the ban reason is the sentinel's, or no row when the account is missing.
function Get-LsSentinelAdminQuery([string]$Name) {
    if ($Name -cnotmatch $script:AccountNamePattern) { throw 'The sentinel Admin account name is not a valid account name.' }
    return ("SELECT accessLevel, IF(ban_Expire_Time > UTC_TIMESTAMP(), 1, 0), IF(ban_Reason <=> '$($script:SentinelBanReason)', 1, 0) " +
        "FROM ace_auth.account WHERE accountName = '$Name';")
}

# The sentinel account's state from that query's output (mariadb --batch --skip-column-names): $null when there is no
# row, else [pscustomobject]@{ Level; Banned; SetupBan; Sound }. Sound means it is an Admin banned past now; SetupBan
# that the ban is the one setup writes.
function ConvertFrom-LsSentinelAdminRow([string]$Text) {
    $rows = @(([string]$Text -split "`r?`n") | Where-Object { $_.Trim() })
    if ($rows.Count -eq 0) { return $null }
    if ($rows.Count -gt 1) { throw 'MariaDB returned more than one sentinel Admin account row.' }
    $cells = $rows[0].Trim() -split "`t"
    if ($cells.Count -ne 3) { throw 'The sentinel Admin account row from MariaDB is not in the expected form.' }
    $level = [int]$cells[0]
    $banned = $cells[1] -eq '1'
    return [pscustomobject]@{ Level = $level; Banned = $banned; SetupBan = ($cells[2] -eq '1'); Sound = ($level -eq 5 -and $banned) }
}

# The sentinel Admin account's state (ConvertFrom-LsSentinelAdminRow), or $null when it is missing. MariaDB must run.
function Get-LsSentinelAdmin($cfg) {
    return ConvertFrom-LsSentinelAdminRow (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql (Get-LsSentinelAdminQuery $cfg.SentinelAdminName)).StdOut
}

# Creates the sentinel Admin account when it is missing. When it exists, it is made the sentinel again every time: a
# fresh discarded password and salt, AccessLevel Admin and the ban to 2099, so a row ACE created at a login, or one
# whose level, password or ban changed since, never stays loginable. Returns 'created', 'present' (it already was an
# Admin with setup's ban) or 'repaired'.
function Set-LsSentinelAdmin($cfg) {
    $name = $cfg.SentinelAdminName
    $before = Get-LsSentinelAdmin $cfg
    if (-not $before) {
        Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql (New-LsSentinelAdminSql $name) | Out-Null
        $outcome = 'created'
    } else {
        Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql (New-LsSentinelAdminResetSql $name) | Out-Null
        $outcome = $(if ($before.Sound -and $before.SetupBan) { 'present' } else { 'repaired' })
    }
    $after = Get-LsSentinelAdmin $cfg
    if (-not $after -or -not $after.Sound -or -not $after.SetupBan) {
        throw "The sentinel Admin account '$name' is not a banned Admin after setup."
    }
    return $outcome
}

# ACE's AccessLevel names (ACE.Entity/Enum/AccessLevel.cs).
$script:AccessLevelNames = @{ 0 = 'Player'; 1 = 'Advocate'; 2 = 'Sentinel'; 3 = 'Envoy'; 4 = 'Developer'; 5 = 'Admin' }

# The rows of 'SELECT accountName, accessLevel' as the mariadb client prints them with --batch --skip-column-names
# (tab-separated), as @([pscustomobject]@{ Name; Level; LevelName }).
function ConvertFrom-LsAccountRows([string]$Text) {
    $result = @()
    foreach ($row in ([string]$Text -split "`r?`n")) {
        if (-not $row.Trim()) { continue }
        $cells = $row -split "`t"
        if ($cells.Count -lt 2) { throw 'An account row from MariaDB has no access level.' }
        $level = [int]$cells[1]
        $levelName = "level $level"
        if ($script:AccessLevelNames.ContainsKey($level)) { $levelName = $script:AccessLevelNames[$level] }
        $result += [pscustomobject]@{ Name = $cells[0]; Level = $level; LevelName = $levelName }
    }
    return $result
}

# Every ace_auth account above Player, as @([pscustomobject]@{ Name; Level; LevelName }): names and levels only.
function Get-LsElevatedAccounts($cfg) {
    $text = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT accountName, accessLevel FROM ace_auth.account WHERE accessLevel > 0 ORDER BY accountName;').StdOut
    return @(ConvertFrom-LsAccountRows $text)
}

# Warns (names only) about every account above Player other than the sentinel, louder for an account secrets.json
# holds, which Lifestone logs in with and which must be a Player. Returns the number of warnings.
function Write-LsAccountAudit($cfg) {
    $mine = @{}
    try { foreach ($a in @(Get-LsSecretsAccounts (Get-LsSecrets $cfg))) { $mine[$a.Username.ToLowerInvariant()] = $a.Key } } catch { Write-LsWarn "secrets.json could not be read for the account audit: $($_.Exception.Message)" }
    $warnings = 0
    foreach ($a in @(Get-LsElevatedAccounts $cfg)) {
        if ($a.Name -eq $cfg.SentinelAdminName) { continue }
        $warnings++
        $fix = "to make it a Player: UPDATE ace_auth.account SET accessLevel = 0 WHERE accountName = '$($a.Name)';"
        if ($mine.ContainsKey($a.Name.ToLowerInvariant())) {
            Write-Host "    WARNING: the secrets.json account '$($a.Name)' (key '$($mine[$a.Name.ToLowerInvariant()])') is $($a.LevelName), not Player; Lifestone logs in with it. $fix" -ForegroundColor Red
        } else {
            Write-LsWarn "account '$($a.Name)' is $($a.LevelName) (above Player). $fix"
        }
    }
    return $warnings
}

# ---------------------------------------------------------------------------------------------------------------
# pids.json after stop-server.ps1 -AceOnly
# ---------------------------------------------------------------------------------------------------------------

# Rewrites pids.json without its 'ace' entry: every other field is kept, and the mariadbd entry is written again from
# the live process (or dropped when mariadbd no longer runs). With nothing left to track, pids.json is removed.
function Save-LsPidsWithoutAce($cfg) {
    $pids = Get-LsPids $cfg
    if (-not $pids) { return }
    $record = [ordered]@{}
    $db = $null
    foreach ($prop in $pids.PSObject.Properties) {
        if ($prop.Name -eq 'ace') { continue }
        if ($prop.Name -eq 'mariadbd') { $db = Get-LsLiveProcess $prop.Value; continue }
        $record[$prop.Name] = ConvertTo-LsJsonValue $prop.Value
    }
    if ($db) {
        $record.mariadbd = New-LsPidEntry $db $cfg.MariaDbd
        Write-LsUtf8File $cfg.PidsFile ($record | ConvertTo-Json -Depth 4)
    } else {
        Remove-Item -LiteralPath $cfg.PidsFile -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------------------------------------------
# The live check (m2-plan SM8, the lead's merge gate; smoke's 'server-mod' step when the backend is up)
# ---------------------------------------------------------------------------------------------------------------

# The lines of ACE_Log.txt and its rolled copies logged since $Since (local time, as log4net writes it); a line with no
# timestamp of its own (an exception's continuation) goes with the line before it.
function Get-LsAceLogLinesSince($cfg, [DateTime]$Since) {
    $lines = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem -LiteralPath $cfg.Logs -Filter 'ACE_Log.txt*' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
    foreach ($f in $files) {
        $stream = New-Object System.IO.FileStream($f.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
        $reader = New-Object System.IO.StreamReader($stream)
        try {
            $keep = $false
            while ($null -ne ($line = $reader.ReadLine())) {
                $stamp = [DateTime]::MinValue
                if ($line.Length -ge 23 -and [DateTime]::TryParseExact($line.Substring(0, 23), 'yyyy-MM-dd HH:mm:ss,fff', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeLocal, [ref]$stamp)) {
                    $keep = $stamp -ge $Since
                }
                if ($keep) { $lines.Add($line) }
            }
        } finally { $reader.Dispose(); $stream.Dispose() }
    }
    return $lines.ToArray()
}

# The mod's replies to console commands in a stretch of ACE's log ('Lifestone console: <line 1> | #lifestone/1
# {json}'), parsed, by verb (the last one of each verb wins).
function Get-LsConsoleReplies([string]$Text) {
    $replies = @{}
    foreach ($line in ([string]$Text -split "`r?`n")) {
        $m = [regex]::Match($line, $script:ModLinePrefix + 'Lifestone console: Lifestone: .* \| #lifestone/1 (?<json>\{.*\})\s*$')
        if (-not $m.Success) { continue }
        try { $reply = $m.Groups['json'].Value | ConvertFrom-Json } catch { continue }
        if ($reply.PSObject.Properties['verb']) { $replies[[string]$reply.verb] = $reply }
    }
    return $replies
}

# Whether a console reply to 'lifestone status' (Status) or 'lifestone net' (Net) says what the live check expects:
# version 1, ok, and for status wire 1, host 127.0.0.1, auto-promotion off and the Console access level.
# Returns @{ Pass; Detail }.
function Test-LsConsoleReply($Reply, [ValidateSet('status', 'net')][string]$Verb) {
    if ($null -eq $Reply) { return [pscustomobject]@{ Pass = $false; Detail = "no 'Lifestone console:' $Verb reply" } }
    try {
        $d = $Reply.data
        if ($Verb -eq 'status') {
            $pass = $Reply.v -eq 1 -and $Reply.ok -eq $true -and $d.wire -eq 1 -and $d.host -eq '127.0.0.1' -and $d.autoPromotion -eq $false -and $d.access -eq 'Console'
            return [pscustomobject]@{ Pass = [bool]$pass; Detail = "wire $($d.wire), host $($d.host), auto-promotion $($d.autoPromotion), access $($d.access), world $($d.world), mod $($d.mod), ACE $($d.aceCommit)" }
        }
        $pass = $Reply.v -eq 1 -and $Reply.ok -eq $true -and $d.c2SPackets -ge 0 -and $d.s2CPackets -ge 0 -and $d.c2SCrcErrors -ge 0
        return [pscustomobject]@{ Pass = [bool]$pass; Detail = "packets in $($d.c2SPackets), out $($d.s2CPackets), bad checksums $($d.c2SCrcErrors)" }
    } catch {
        return [pscustomobject]@{ Pass = $false; Detail = "the $Verb reply lacks a field ($($_.Exception.GetType().Name))" }
    }
}

# The live check's counts over one boot's ACE log lines: the mod enabled once, its command registered, no refusal,
# ACE's mod list not empty, auto-promotion turned off once, ready once, and no account promoted to Admin.
# Returns @(@{ Check; Pass; Detail }).
function Measure-LsModBootLog([string[]]$Lines) {
    $prefix = $script:ModLinePrefix
    $expect = @(
        @('mod enabled once', ($prefix + 'Enabled mod `Lifestone \(v'), 1, 1),
        @('command registered', '^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d,\d{3} \[[^\]]*\] [A-Z]+ +\(ACE\.Server\.Command\.CommandManager\) Command created: lifestone\s*$', 1, 1000),
        @('no refusal', ($prefix + 'Lifestone mod refused to load'), 0, 0),
        @('mods listed', ($prefix + 'No mods to display'), 0, 0),
        @('auto-promotion off once', ($prefix + "Lifestone: ACE's first-account Admin auto-promotion is off"), 1, 1),
        @('ready once', $script:ModReadyPattern, 1, 1),
        @('no Admin promotion', 'Automatically setting account AccessLevel to Admin', 0, 0)
    )
    $checks = @()
    foreach ($e in $expect) {
        $pattern = $e[1]
        $n = @($Lines | Where-Object { $_ -match $pattern }).Count
        $checks += [pscustomobject]@{ Check = $e[0]; Pass = ($n -ge $e[2] -and $n -le $e[3]); Detail = "$n line(s) in this boot's log" }
    }
    return $checks
}

# Checks a running backend's mod end to end, without a client: types 'lifestone status' and 'lifestone net' into ACE's
# console (as stop-server.ps1 types stop-now) and reads the replies the mod writes into ACE's log, then counts this
# boot's log lines and scans the logs for passwords. Returns @{ Passed; Checks = @(@{ Check; Pass; Detail }) }.
# Read-only apart from the two console commands; never prints a value from secrets.json.
function Test-LsServerModLive {
    param([Parameter(Mandatory = $true)]$Cfg, [int]$TimeoutSec = 60)
    $checks = New-Object System.Collections.Generic.List[object]

    $state = Get-LsBackendState $Cfg
    $checks.Add([pscustomobject]@{ Check = 'backend'; Pass = [bool]($state.Ace -and $state.ModReady); Detail = "ACE running: $($state.Ace); pids.json ace.modReady: $($state.ModReady); mod $($state.ModVersion), wire $($state.ModWire)" })
    if (-not ($state.Ace -and $state.ModReady)) { return [pscustomobject]@{ Passed = $false; Checks = $checks.ToArray() } }
    $bootStart = (Get-Process -Id $state.AcePid).StartTime.AddSeconds(-2)

    $offset = 0L
    if (Test-Path -LiteralPath $Cfg.AceLog) { $offset = (Get-Item -LiteralPath $Cfg.AceLog).Length }
    Send-LsConsoleCommand $Cfg $state.AcePid 'lifestone status'
    Send-LsConsoleCommand $Cfg $state.AcePid 'lifestone net'
    $replies = @{}
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline -and -not ($replies.ContainsKey('status') -and $replies.ContainsKey('net'))) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path -LiteralPath $Cfg.AceLog)) { continue }
        $stream = New-Object System.IO.FileStream($Cfg.AceLog, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
        try {
            if ($stream.Length -lt $offset) { $offset = 0 }
            [void]$stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
            $text = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
        } finally { $stream.Dispose() }
        $replies = Get-LsConsoleReplies $text
    }
    foreach ($verb in @('status', 'net')) {
        $verdict = Test-LsConsoleReply $replies[$verb] $verb
        $checks.Add([pscustomobject]@{ Check = "console $verb"; Pass = $verdict.Pass; Detail = $verdict.Detail })
    }

    $boot = @(Get-LsAceLogLinesSince $Cfg $bootStart)
    foreach ($c in @(Measure-LsModBootLog $boot)) { $checks.Add($c) }

    $leaks = @(Find-LsSecretLeaks -Cfg $Cfg -Path @($Cfg.Logs))
    $hits = 0
    foreach ($l in $leaks) { if ($l.Count -gt 0) { $hits += $l.Count } }
    $unreadable = @($leaks | Where-Object { $_.Count -lt 0 }).Count
    $checks.Add([pscustomobject]@{ Check = 'no passwords in the logs'; Pass = ($hits -eq 0 -and $unreadable -eq 0); Detail = "$($leaks.Count) file(s) scanned, $hits hit(s), $unreadable unreadable" })

    $passed = @($checks | Where-Object { -not $_.Pass }).Count -eq 0
    return [pscustomobject]@{ Passed = $passed; Checks = $checks.ToArray() }
}

Export-ModuleMember -Function *-Ls*
