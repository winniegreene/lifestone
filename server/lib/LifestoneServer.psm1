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
    }
    if ($cfg.Host -ne '127.0.0.1') { throw "versions.json network.host must be 127.0.0.1 (got '$($cfg.Host)')." }
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

function Get-LsSecrets($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.Secrets)) { throw "Missing $($cfg.Secrets). Run setup-server.ps1 first." }
    return Get-Content -LiteralPath $cfg.Secrets -Raw | ConvertFrom-Json
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

function New-LsPidEntry([System.Diagnostics.Process]$Process, [string]$Path) {
    return [ordered]@{ pid = $Process.Id; path = $Path; startTime = $Process.StartTime.ToUniversalTime().ToString('o') }
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

Export-ModuleMember -Function *-Ls*
