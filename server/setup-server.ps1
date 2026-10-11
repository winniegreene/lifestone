<#
.SYNOPSIS
  Idempotent setup of the Lifestone local game backend: ACE (built from pinned source) + portable MariaDB +
  ACE databases (auth, shard, world). Re-running skips every step that is already complete.

.DESCRIPTION
  Steps (each skipped when already done):
    1. ACE source  : clone ACEmulator/ACE at the pinned tag into vendor-eval\ACE and verify the pinned commit.
    2. ACE build   : dotnet restore + publish (Release, framework-dependent net10.0) to server\runtime\ace.
    3. MariaDB     : download the pinned Windows x64 ZIP from archive.mariadb.org, verify SHA-256 (pinned value and
                     the archive's sha256sums.txt), extract to server\runtime\mariadb. No MSI, no service.
    4. Secrets     : generate root / ace DB passwords + a test account into server\runtime\secrets.json (user-only ACL).
    5. my.ini      : render templates\my.template.ini (bind-address=127.0.0.1, port 3316, skip-name-resolve).
    6. Datadir     : mariadb-install-db.exe --datadir=server\runtime\mariadb-data (never --service).
    7. Users       : first start uses --init-file to set the root password and create 'ace'@'127.0.0.1' with
                     privileges on ace_auth / ace_shard / ace_world only. Init file is deleted right after.
    8. Databases   : ACE base schemas (only if the database does not exist yet -- existing player data is never
                     dropped), pinned world DB release (download via gh, verify SHA-256), then ACE's bundled
                     Database\Updates scripts (tracked in DatabaseSetupScripts\Updates\*\applied_updates.txt).
    9. ACE config  : render Config.js and log4net.config from templates.
  mariadbd is started only for steps 7-8 and stopped again before the script exits.

.PARAMETER RebuildAce
  Force dotnet publish even if the pinned commit is already built.
.PARAMETER ReimportWorld
  Drop and re-import ace_world from the pinned release (world content only; auth/shard untouched).
.PARAMETER ResetEmptyDatabases
  Drop and recreate ace_auth, ace_shard and ace_world from scratch. Refuses if any account or character exists.
#>
[CmdletBinding()]
param(
    [switch]$RebuildAce,
    [switch]$ReimportWorld,
    [switch]$ResetEmptyDatabases
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSScriptRoot 'lib\LifestoneServer.psm1') -Force

$cfg = Get-LsConfig
$v = $cfg.Versions
$state = Get-LsState $cfg
$started = Get-Date

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'Preflight'
$running = @(Get-LsRuntimeProcesses $cfg)
if ($running.Count -gt 0) {
    throw "Server processes are running (PIDs: $(($running | ForEach-Object Id) -join ', ')). Run stop-server.ps1 first."
}
foreach ($d in @($cfg.Runtime, $cfg.Downloads, $cfg.Logs, $cfg.AceModsDir, $cfg.TmpDir, $cfg.FilesDir)) { New-LsDirectory $d }
$git = (Get-Command git.exe -ErrorAction Stop).Source
Write-LsInfo "Runtime: $($cfg.Runtime)"

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep "ACE source ($($v.ace.tag) @ $($v.ace.commit.Substring(0, 12)))"
if (-not (Test-Path -LiteralPath (Join-Path $cfg.AceSourceDir '.git'))) {
    New-LsDirectory (Split-Path -Parent $cfg.AceSourceDir)
    Write-LsInfo "Cloning $($v.ace.repo) (shallow, tag $($v.ace.tag)) into $($cfg.AceSourceDir)"
    Invoke-LsTool -FilePath $git -Arguments @('-c', 'advice.detachedHead=false', 'clone', '--depth', '1', '--branch', $v.ace.tag, $v.ace.repo, $cfg.AceSourceDir) -TimeoutSec 1800 | Out-Null
}
$head = (Invoke-LsTool -FilePath $git -Arguments @('-C', $cfg.AceSourceDir, 'rev-parse', 'HEAD')).StdOut.Trim()
if ($head -ne $v.ace.commit) { throw "ACE source at $($cfg.AceSourceDir) is commit $head, expected pinned $($v.ace.commit). Refusing to build unreviewed source." }
$dirty = (Invoke-LsTool -FilePath $git -Arguments @('-C', $cfg.AceSourceDir, 'status', '--porcelain', '--untracked-files=no')).StdOut.Trim()
if ($dirty) { throw "ACE source tree has local modifications to tracked files; refusing to build:`n$dirty" }
Write-LsInfo "OK: HEAD = $head (clean)"

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'ACE build (dotnet publish -c Release)'
$built = $null
if (Test-Path -LiteralPath $cfg.AceBuildInfo) { $built = Get-Content -LiteralPath $cfg.AceBuildInfo -Raw | ConvertFrom-Json }
if ($RebuildAce -or -not (Test-Path -LiteralPath $cfg.AceExe) -or -not $built -or $built.commit -ne $v.ace.commit) {
    $dotnetRoot = Get-LsDotnetRoot
    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
    $dotnetEnv = @{
        DOTNET_ROOT                    = $dotnetRoot
        PATH                           = "$dotnetRoot;$env:PATH"
        DOTNET_CLI_TELEMETRY_OPTOUT    = '1'
        DOTNET_NOLOGO                  = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        # The SDK's first run (once per SDK version) otherwise appends %USERPROFILE%\.dotnet\tools to the persistent
        # user PATH and creates an ASP.NET dev certificate in CurrentUser\My. DOTNET_SKIP_FIRST_TIME_EXPERIENCE does not stop either.
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH    = 'false'
        DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        MSBUILDDISABLENODEREUSE        = '1'
    }
    $proj = Join-Path $cfg.AceSourceDir 'Source\ACE.Server\ACE.Server.csproj'
    $sdk = (Invoke-LsTool -FilePath $dotnet -Arguments @('--version') -Environment $dotnetEnv).StdOut.Trim()
    Write-LsInfo "dotnet SDK $sdk at $dotnetRoot"
    Write-LsInfo 'dotnet restore (nuget.org)...'
    Invoke-LsTool -FilePath $dotnet -Arguments @('restore', $proj, '-nodeReuse:false') -Environment $dotnetEnv -WorkingDirectory $cfg.AceSourceDir -TimeoutSec 1800 | Out-Null
    Write-LsInfo "dotnet publish -> $($cfg.AceDir) ..."
    $pub = Invoke-LsTool -FilePath $dotnet -Arguments @('publish', $proj, '-c', 'Release', '-o', $cfg.AceDir, '--no-restore', '-nodeReuse:false', '-p:UseSharedCompilation=false') -Environment $dotnetEnv -WorkingDirectory $cfg.AceSourceDir -TimeoutSec 3600
    $warn = ([regex]::Matches($pub.StdOut, 'warning [A-Z]+\d+')).Count
    Write-LsInfo "publish OK ($warn compiler/nuget warnings)"
    Invoke-LsTool -FilePath $dotnet -Arguments @('build-server', 'shutdown') -Environment $dotnetEnv -AllowFailure | Out-Null
    $info = [ordered]@{ tag = $v.ace.tag; commit = $v.ace.commit; sdk = $sdk; builtUtc = (Get-Date).ToUniversalTime().ToString('o'); aceServerDllSha256 = (Get-LsSha256 (Join-Path $cfg.AceDir 'ACE.Server.dll')) }
    Write-LsUtf8File $cfg.AceBuildInfo ($info | ConvertTo-Json)
} else {
    Write-LsInfo "Skip: $($cfg.AceExe) already built from $($built.commit.Substring(0, 12)) ($($built.builtUtc))"
}
Assert-LsNoElevation $cfg.AceExe

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep "MariaDB $($v.mariadb.version) binaries"
$mdbMarker = Join-Path $cfg.MariaDbDir '.lifestone-version.json'
$mdbOk = (Test-Path -LiteralPath $cfg.MariaDbd) -and (Test-Path -LiteralPath $mdbMarker) -and ((Get-Content -LiteralPath $mdbMarker -Raw | ConvertFrom-Json).sha256 -eq $v.mariadb.sha256)
if (-not $mdbOk) {
    $dl = Join-Path $cfg.Downloads "mariadb-$($v.mariadb.version)"
    New-LsDirectory $dl
    $zip = Join-Path $dl $v.mariadb.zipName
    if ((Test-Path -LiteralPath $zip) -and ((Get-LsSha256 $zip) -ne $v.mariadb.sha256)) {
        Write-LsWarn "Existing $zip does not match the pinned hash; re-downloading."
        Remove-Item -LiteralPath $zip -Force
    }
    if (-not (Test-Path -LiteralPath $zip)) {
        Write-LsInfo "Downloading $($v.mariadb.url)"
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $v.mariadb.url -OutFile $zip -UseBasicParsing
    }
    $actual = Get-LsSha256 $zip
    if ($actual -ne $v.mariadb.sha256) { throw "SHA-256 mismatch for ${zip}: got $actual, pinned $($v.mariadb.sha256)" }
    $sumsFile = Join-Path $dl 'sha256sums.txt'
    Invoke-WebRequest -Uri $v.mariadb.sha256sumsUrl -OutFile $sumsFile -UseBasicParsing
    $line = Get-Content -LiteralPath $sumsFile | Where-Object { $_ -match ("\s\*?\.?/?" + [regex]::Escape($v.mariadb.zipName) + '$') } | Select-Object -First 1
    if (-not $line -or ($line -split '\s+')[0].ToLowerInvariant() -ne $actual) { throw "archive.mariadb.org sha256sums.txt does not list $actual for $($v.mariadb.zipName)" }
    Write-LsInfo "SHA-256 verified (pinned + archive sha256sums.txt): $actual"

    $stage = Join-Path $cfg.Runtime 'mariadb-extract-tmp'
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-LsDirectory $stage
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $stage)
    if (Test-Path -LiteralPath $cfg.MariaDbDir) { Remove-Item -LiteralPath $cfg.MariaDbDir -Recurse -Force }   # binaries only; data lives in mariadb-data
    Move-Item -LiteralPath (Join-Path $stage $v.mariadb.topFolder) -Destination $cfg.MariaDbDir
    Remove-Item -LiteralPath $stage -Recurse -Force
    Write-LsUtf8File $mdbMarker (([ordered]@{ version = $v.mariadb.version; sha256 = $actual; zip = $zip; extractedUtc = (Get-Date).ToUniversalTime().ToString('o') }) | ConvertTo-Json)
    Write-LsInfo "Extracted to $($cfg.MariaDbDir)"
} else {
    Write-LsInfo "Skip: $($cfg.MariaDbDir) already holds the verified $($v.mariadb.version) ZIP"
}
foreach ($exe in @($cfg.MariaDbd, $cfg.MariaDbClient, $cfg.MariaDbAdmin, $cfg.MariaDbInstall)) { Assert-LsNoElevation $exe }

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'Secrets'
if (-not (Test-Path -LiteralPath $cfg.Secrets)) {
    $new = [ordered]@{
        note         = 'Generated by server/setup-server.ps1. Local-only credentials for the Lifestone backend. Never commit or share.'
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        mariadb      = [ordered]@{ host = $cfg.Host; port = $cfg.DbPort; rootUser = 'root'; rootPassword = (New-LsSecret 32); aceUser = $v.server.aceDbUser; acePassword = (New-LsSecret 32) }
        testAccount  = [ordered]@{ username = $v.server.testAccountName; password = (New-LsSecret 20); note = 'ACE auto-creates this account on first login (AllowAutoAccountCreation); the first account created is promoted to Admin.' }
    }
    Write-LsUtf8File $cfg.Secrets ($new | ConvertTo-Json -Depth 4)
    Protect-LsFile $cfg.Secrets
    Write-LsInfo "Generated $($cfg.Secrets) (access restricted to $env:USERNAME)"
} else {
    Write-LsInfo "Skip: using existing $($cfg.Secrets)"
}
$secrets = Get-LsSecrets $cfg
$cnfTemplate = "[client]`r`nuser={0}`r`npassword={1}`r`nhost={2}`r`nport={3}`r`nprotocol=TCP`r`n"
Write-LsUtf8File $cfg.RootCnf ($cnfTemplate -f 'root', $secrets.mariadb.rootPassword, $cfg.Host, $cfg.DbPort)
Write-LsUtf8File $cfg.AceCnf ($cnfTemplate -f $secrets.mariadb.aceUser, $secrets.mariadb.acePassword, $cfg.Host, $cfg.DbPort)
Protect-LsFile $cfg.RootCnf
Protect-LsFile $cfg.AceCnf

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'my.ini'
$ini = Expand-LsTemplate (Join-Path $cfg.Templates 'my.template.ini') @{
    BASEDIR  = $cfg.MariaDbDir -replace '\\', '/'
    DATADIR  = $cfg.DataDir -replace '\\', '/'
    TMPDIR   = $cfg.TmpDir -replace '\\', '/'
    FILESDIR = $cfg.FilesDir -replace '\\', '/'
    LOGDIR   = $cfg.Logs -replace '\\', '/'
    HOST     = $cfg.Host
    PORT     = $cfg.DbPort
}
Write-LsUtf8File $cfg.MyIni $ini
Assert-LsMyIniLoopback $cfg
Write-LsInfo "Rendered $($cfg.MyIni) (bind-address=$($cfg.Host), port=$($cfg.DbPort))"

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'MariaDB data directory'
if (-not (Test-Path -LiteralPath (Join-Path $cfg.DataDir 'mysql'))) {
    if ((Test-Path -LiteralPath $cfg.DataDir) -and @(Get-ChildItem -LiteralPath $cfg.DataDir -Force).Count -gt 0) {
        throw "$($cfg.DataDir) exists but is not an initialized MariaDB datadir. Inspect it and remove it manually."
    }
    Write-LsInfo 'mariadb-install-db.exe (no --service, no password on the command line)...'
    $r = Invoke-LsTool -FilePath $cfg.MariaDbInstall -Arguments @("--datadir=$($cfg.DataDir)", "--port=$($cfg.DbPort)") -TimeoutSec 600
    Write-LsInfo (($r.StdOut.Trim() -split "`r?`n") -join ' | ')
    $state.usersConfigured = $false
    Save-LsState $cfg $state
} else {
    Write-LsInfo "Skip: $($cfg.DataDir) already initialized"
}

# ----------------------------------------------------------------------------------------------------------------
$mysqld = $null
$restoreFlush = $false
try {
    Write-LsStep 'MariaDB users'
    $usersDone = $state.ContainsKey('usersConfigured') -and $state.usersConfigured
    if (-not $usersDone) {
        $rootPw = $secrets.mariadb.rootPassword; $acePw = $secrets.mariadb.acePassword; $aceUser = $secrets.mariadb.aceUser
        $initSql = @(
            "CREATE USER IF NOT EXISTS 'root'@'localhost' IDENTIFIED BY '$rootPw';",
            "ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPw';",
            "CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '$rootPw';",
            "ALTER USER 'root'@'127.0.0.1' IDENTIFIED BY '$rootPw';",
            "GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;",
            "DROP USER IF EXISTS 'root'@'::1';",
            "DROP USER IF EXISTS 'root'@'%';",
            "DROP USER IF EXISTS ''@'localhost';",
            "CREATE USER IF NOT EXISTS '$aceUser'@'127.0.0.1' IDENTIFIED BY '$acePw';",
            "ALTER USER '$aceUser'@'127.0.0.1' IDENTIFIED BY '$acePw';",
            "GRANT ALL PRIVILEGES ON ``ace_auth``.* TO '$aceUser'@'127.0.0.1';",
            "GRANT ALL PRIVILEGES ON ``ace_shard``.* TO '$aceUser'@'127.0.0.1';",
            "GRANT ALL PRIVILEGES ON ``ace_world``.* TO '$aceUser'@'127.0.0.1';",
            "FLUSH PRIVILEGES;"
        ) -join "`n"
        $initFile = Join-Path $cfg.Runtime 'init-users.sql'
        Write-LsUtf8File $initFile ($initSql + "`n")
        Protect-LsFile $initFile
        try {
            Write-LsInfo 'Starting mariadbd once with --init-file to set credentials...'
            $mysqld = Start-LsMariaDb -Cfg $cfg -InitFile $initFile
        } finally {
            Remove-Item -LiteralPath $initFile -Force -ErrorAction SilentlyContinue
        }
        $state.usersConfigured = $true
        Save-LsState $cfg $state
    } else {
        Write-LsInfo 'Credentials already configured; starting mariadbd...'
        $mysqld = Start-LsMariaDb -Cfg $cfg
    }
    Write-LsInfo "mariadbd PID $($mysqld.Id) listening on $($cfg.Host):$($cfg.DbPort)"
    $who = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT CURRENT_USER(), VERSION();').StdOut.Trim()
    Write-LsInfo "root login OK ($who)"
    # Harden: mariadb-install-db also creates root@<computername>; drop every root/anonymous account other than
    # root@localhost and root@127.0.0.1 (idempotent).
    $extra = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql "SELECT CONCAT(QUOTE(User),'@',QUOTE(Host)) FROM mysql.user WHERE is_role='N' AND ((User='root' AND Host NOT IN ('localhost','127.0.0.1')) OR User='');").StdOut.Trim() -split "`r?`n" | Where-Object { $_ }
    foreach ($acct in $extra) {
        Write-LsInfo "dropping extra account $acct"
        Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql "DROP USER $acct;" | Out-Null
    }
    $users = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql "SELECT CONCAT(User,'@',Host) FROM mysql.user ORDER BY User, Host;").StdOut.Trim() -split "`r?`n"
    Write-LsInfo "accounts: $($users -join ', ')"
    $aceWho = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.AceCnf -Sql 'SELECT CURRENT_USER();').StdOut.Trim()
    Write-LsInfo "ace login OK ($aceWho)"

    # ------------------------------------------------------------------------------------------------------------
    Write-LsStep 'ACE databases'
    $scripts = Join-Path $cfg.AceDir 'DatabaseSetupScripts'
    $dbs = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SHOW DATABASES;').StdOut -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    if ($ResetEmptyDatabases) {
        $acc = 0; $chr = 0
        if ($dbs -contains 'ace_auth') { $acc = [int](Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT COUNT(*) FROM ace_auth.account;').StdOut.Trim() }
        if ($dbs -contains 'ace_shard') { $chr = [int](Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT COUNT(*) FROM ace_shard.`character`;').StdOut.Trim() }
        if ($acc -gt 0 -or $chr -gt 0) { throw "-ResetEmptyDatabases refused: $acc account(s) and $chr character(s) exist." }
        Write-LsWarn 'Dropping ace_auth, ace_shard and ace_world (no accounts or characters present).'
        Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'DROP DATABASE IF EXISTS ace_auth; DROP DATABASE IF EXISTS ace_shard; DROP DATABASE IF EXISTS ace_world;' | Out-Null
        foreach ($type in @('Authentication', 'Shard', 'World')) {
            Remove-Item -LiteralPath (Join-Path $scripts "Updates\$type\applied_updates.txt") -Force -ErrorAction SilentlyContinue
        }
        $dbs = @($dbs | Where-Object { $_ -notin @('ace_auth', 'ace_shard', 'ace_world') })
    }
    Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SET GLOBAL innodb_flush_log_at_trx_commit=2;' | Out-Null
    $restoreFlush = $true

    foreach ($pair in @(@('ace_auth', 'AuthenticationBase.sql'), @('ace_shard', 'ShardBase.sql'))) {
        if ($dbs -contains $pair[0]) {
            Write-LsInfo "Skip: $($pair[0]) exists (never re-imported, it holds accounts/characters)"
        } else {
            Write-LsInfo "Creating $($pair[0]) from $($pair[1])"
            Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -SourceFile (Join-Path $scripts "Base\$($pair[1])") | Out-Null
        }
    }

    $worldVersion = $null
    if ($dbs -contains 'ace_world') {
        $wv = Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT base_Version, patch_Version FROM ace_world.version LIMIT 1;' -AllowFailure
        if ($wv.ExitCode -eq 0 -and $wv.StdOut.Trim()) { $worldVersion = ($wv.StdOut.Trim() -split "`t")[1] }
    }
    $worldImported = $false
    if ($ReimportWorld -or $worldVersion -ne $v.world.tag) {
        $wdl = Join-Path $cfg.Downloads "ace-world-$($v.world.tag)"
        New-LsDirectory $wdl
        $wzip = Join-Path $wdl $v.world.assetName
        if ((Test-Path -LiteralPath $wzip) -and ((Get-LsSha256 $wzip) -ne $v.world.sha256)) {
            Write-LsWarn "Existing $wzip does not match the pinned hash; re-downloading."
            Remove-Item -LiteralPath $wzip -Force
        }
        if (-not (Test-Path -LiteralPath $wzip)) {
            $gh = Get-Command gh.exe -ErrorAction SilentlyContinue
            if ($gh) {
                Write-LsInfo "gh release download $($v.world.tag) -R $($v.world.repo)"
                Invoke-LsTool -FilePath $gh.Source -Arguments @('release', 'download', $v.world.tag, '-R', $v.world.repo, '-p', $v.world.assetName, '-D', $wdl) -TimeoutSec 1800 | Out-Null
            } else {
                Write-LsInfo "gh not found; downloading $($v.world.url)"
                [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
                Invoke-WebRequest -Uri $v.world.url -OutFile $wzip -UseBasicParsing
            }
        }
        $wsha = Get-LsSha256 $wzip
        if ($wsha -ne $v.world.sha256) { throw "SHA-256 mismatch for ${wzip}: got $wsha, pinned $($v.world.sha256)" }
        Write-LsInfo "World DB zip SHA-256 verified: $wsha"

        $wx = Join-Path $wdl 'extracted'
        $wsql = Join-Path $wx $v.world.sqlName
        if (Test-Path -LiteralPath $wx) { Remove-Item -LiteralPath $wx -Recurse -Force }
        New-LsDirectory $wx
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($wzip, $wx)
        if (-not (Test-Path -LiteralPath $wsql)) { throw "Expected $($v.world.sqlName) inside $wzip" }

        if (-not ($dbs -contains 'ace_world')) {
            Write-LsInfo 'Creating ace_world schema from WorldBase.sql'
            Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -SourceFile (Join-Path $scripts 'Base\WorldBase.sql') | Out-Null
        }
        Write-LsInfo "Importing $($v.world.sqlName) ($([math]::Round((Get-Item -LiteralPath $wsql).Length / 1MB)) MB) into ace_world (drops and recreates ace_world)..."
        $sw = [Diagnostics.Stopwatch]::StartNew()
        Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -SourceFile $wsql -TimeoutSec 7200 | Out-Null
        Write-LsInfo ("World import finished in {0:N0} s" -f $sw.Elapsed.TotalSeconds)
        Remove-Item -LiteralPath $wx -Recurse -Force
        $worldImported = $true
        $wv = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SELECT base_Version, patch_Version FROM ace_world.version LIMIT 1;').StdOut.Trim() -split "`t"
        if ($wv[1] -ne $v.world.tag) { throw "ace_world.version reports patch '$($wv[1])', expected $($v.world.tag)" }
        Write-LsInfo "ace_world.version = base $($wv[0]) / patch $($wv[1])"
    } else {
        Write-LsInfo "Skip: ace_world already at $worldVersion"
    }

    foreach ($type in @('Authentication', 'Shard', 'World')) {
        $db = @{ Authentication = 'ace_auth'; Shard = 'ace_shard'; World = 'ace_world' }[$type]
        $dir = Join-Path $scripts "Updates\$type"
        $appliedFile = Join-Path $dir 'applied_updates.txt'
        if ($type -eq 'World' -and $worldImported -and (Test-Path -LiteralPath $appliedFile)) { Remove-Item -LiteralPath $appliedFile -Force }
        $applied = @()
        if (Test-Path -LiteralPath $appliedFile) { $applied = @(Get-Content -LiteralPath $appliedFile | Where-Object { $_ }) }
        $pending = @(Get-ChildItem -LiteralPath $dir -Filter '*.sql' -File | Sort-Object Name | Where-Object { $applied -notcontains $_.Name })
        foreach ($f in $pending) {
            Write-LsInfo "update $type/$($f.Name)"
            Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Database $db -SourceFile $f.FullName | Out-Null
            [System.IO.File]::AppendAllText($appliedFile, $f.Name + [Environment]::NewLine)
        }
        if (-not (Test-Path -LiteralPath $appliedFile)) { Write-LsUtf8File $appliedFile '' }
        Write-LsInfo ("{0}: {1} update script(s) applied now, {2} previously" -f $type, $pending.Count, $applied.Count)
    }

    $countSql = @(
        "SELECT 'ace_auth tables', COUNT(*) FROM information_schema.tables WHERE table_schema='ace_auth'",
        "SELECT 'ace_shard tables', COUNT(*) FROM information_schema.tables WHERE table_schema='ace_shard'",
        "SELECT 'ace_world tables', COUNT(*) FROM information_schema.tables WHERE table_schema='ace_world'",
        "SELECT 'weenies', COUNT(*) FROM ace_world.weenie",
        "SELECT 'landblock_instances', COUNT(*) FROM ace_world.landblock_instance",
        "SELECT 'spells', COUNT(*) FROM ace_world.spell"
    ) -join ' UNION ALL '
    $counts = (Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql ($countSql + ';')).StdOut.Trim() -split "`r?`n"
    foreach ($c in $counts) { Write-LsInfo ($c -replace "`t", ': ') }

    Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SET GLOBAL innodb_flush_log_at_trx_commit=1;' | Out-Null
    $restoreFlush = $false
}
finally {
    if ($mysqld) {
        if ($restoreFlush) { Invoke-LsSql -Cfg $cfg -Cnf $cfg.RootCnf -Sql 'SET GLOBAL innodb_flush_log_at_trx_commit=1;' -AllowFailure | Out-Null }
        Stop-LsMariaDb -Cfg $cfg -Process $mysqld
        Write-LsInfo "mariadbd stopped (exited: $($mysqld.HasExited))"
    }
}

# ----------------------------------------------------------------------------------------------------------------
Write-LsStep 'ACE configuration'
$j = { param($s) ConvertTo-Json -InputObject ([string]$s) -Compress }
$datDir = [System.IO.Path]::GetFullPath($cfg.DatDir).TrimEnd('\') + '\'
$modsDir = [System.IO.Path]::GetFullPath($cfg.AceModsDir).TrimEnd('\') + '\'
$js = Expand-LsTemplate (Join-Path $cfg.Templates 'Config.template.js') @{
    WORLD_NAME  = & $j $v.server.worldName
    HOST        = & $j $cfg.Host
    PORT        = $cfg.AcePort
    DAT_DIR     = & $j $datDir
    MODS_DIR    = & $j $modsDir
    DB_HOST     = & $j $cfg.Host
    DB_PORT     = $cfg.DbPort
    DB_USER     = & $j $secrets.mariadb.aceUser
    DB_PASSWORD = & $j $secrets.mariadb.acePassword
}
Write-LsUtf8File $cfg.AceConfig $js
Protect-LsFile $cfg.AceConfig
Assert-LsAceConfigLoopback $cfg
$l4n = Expand-LsTemplate (Join-Path $cfg.Templates 'log4net.template.config') @{ LOGDIR = $cfg.Logs }
Write-LsUtf8File $cfg.AceLog4net $l4n
Write-LsInfo "Rendered $($cfg.AceConfig) (Host $($cfg.Host):$($cfg.AcePort), DatFilesDirectory $datDir, world '$($v.server.worldName)')"
Write-LsInfo "Rendered $($cfg.AceLog4net) (logs -> $($cfg.Logs))"

$datIssues = @(Test-LsDatFiles $cfg)
foreach ($i in $datIssues) { Write-LsWarn "DAT: $i" }
if (Test-Path -LiteralPath (Join-Path $cfg.DatDir 'client_highres.dat')) { Write-LsInfo 'client_highres.dat present (optional for ACE).' }
else { Write-LsInfo 'client_highres.dat not present (optional for ACE; it only logs when found).' }

$state.aceTag = $v.ace.tag; $state.aceCommit = $v.ace.commit
$state.mariadbVersion = $v.mariadb.version; $state.mariadbSha256 = $v.mariadb.sha256
$state.worldTag = $v.world.tag; $state.worldSha256 = $v.world.sha256
$state.lastSetupUtc = (Get-Date).ToUniversalTime().ToString('o')
Save-LsState $cfg $state

Write-LsStep ("Setup complete in {0:N0} s. Start with: server\start-server.ps1" -f ((Get-Date) - $started).TotalSeconds)
