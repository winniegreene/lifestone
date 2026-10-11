# Lifestone local game backend (ACE + MariaDB)

Lifestone's Godot client talks the Asheron's Call protocol to a **bundled, localhost-only ACEmulator (ACE)** server
backed by a **portable MariaDB**. Game rules come from ACE plus the ACE-World-16PY-Patches world database. Everything
in this folder is scripted, version-pinned and runs as the current user: no installer, no Windows service, no
elevation, no registry/firewall/PATH changes, and nothing listens on anything but `127.0.0.1`.

```powershell
# one-time (idempotent; re-run any time, completed steps are skipped)
pwsh -NoProfile -ExecutionPolicy Bypass -File server\setup-server.ps1

pwsh -NoProfile -ExecutionPolicy Bypass -File server\start-server.ps1   # mariadbd, then ACE; returns when ACE is up
pwsh -NoProfile -ExecutionPolicy Bypass -File server\stop-server.ps1    # graceful ACE stop-now, then mariadb shutdown
```

`-ExecutionPolicy Bypass` only applies to that one process. The scripts also run under Windows PowerShell 5.1
(`powershell.exe`).

## Pinned versions

All pins live in [`versions.json`](versions.json). `setup-server.ps1` refuses anything that does not match.

| Component | Version | Source | Verification |
|---|---|---|---|
| ACEmulator server | `v1.78.4816` (commit `95ef426d412459ff619a12cc18f82bcf72c1c2a8`) | `git clone --depth 1 --branch v1.78.4816 https://github.com/ACEmulator/ACE.git` → `vendor-eval/ACE` | HEAD must equal the pinned commit and the tracked tree must be clean, or the build is refused |
| ACE build | `dotnet publish Source/ACE.Server -c Release` (framework-dependent, net10.0, x64) | .NET SDK 10.0.401 | NuGet packages from nuget.org only |
| MariaDB | `11.4.13` (11.4 LTS, EOL 2029-05-29), Windows x64 ZIP | `https://archive.mariadb.org/mariadb-11.4.13/winx64-packages/mariadb-11.4.13-winx64.zip` | SHA-256 `d62986d433eeebfde218560b276103831604a61e929e87f1a17f5aebd80257e2` (pinned, and re-checked against the archive's `sha256sums.txt` on every download). GPG signature checked once by hand: good signature from MariaDB Signing Key `177F 4010 FE56 CA33 3630 0305 F165 6F24 C74C D1D8` |
| World DB | ACE-World-16PY-Patches `v0.9.296` (base `v0.8.8`) | `gh release download v0.9.296 -R ACEmulator/ACE-World-16PY-Patches` (HTTPS fallback if `gh` is missing) | SHA-256 `e56c594197b03a96963b14e3db61ad205d47f63a69d0c1c72e9bef78878f180c` (equals GitHub's published asset digest) |

**Why MariaDB 11.4 and not 11.8?** 11.8 (11.8.9) is the newest 11.x LTS, but ACE's EF Core provider
(Pomelo.EntityFrameworkCore.MySql 9.0.0) was only tested against MariaDB 11.6/11.5/11.4/11.3/10.11/10.6/10.5
(its 9.0.0 release notes). 11.4 is the newest LTS that is on that list. MySqlConnector 2.6.2 supports both.

## Ports and processes

| What | Binary | Bind | Notes |
|---|---|---|---|
| MariaDB | `runtime\mariadb\bin\mariadbd.exe --defaults-file=runtime\my.ini` | TCP `127.0.0.1:3316` | `skip-name-resolve`, no named pipe, `local-infile=0`, `secure-file-priv=runtime\mariadb-files` |
| ACE | `runtime\ace\ACE.Server.exe` | UDP `127.0.0.1:9000` (client→server), UDP `127.0.0.1:9001` (server→client) | Talks to MariaDB at `127.0.0.1:3316` as user `ace` |

`start-server.ps1` refuses to launch if `my.ini` lacks `bind-address=127.0.0.1` or if any `Host` in ACE's `Config.js`
is not exactly `127.0.0.1`. That second check matters: ACE silently falls back to `0.0.0.0` (all interfaces) when the
`Host` value does not parse. After startup the script checks the real sockets (`Get-NetTCPConnection` and
`Get-NetUDPEndpoint`) and fails if anything is bound elsewhere.

## Layout

```
server/
  setup-server.ps1  start-server.ps1  stop-server.ps1
  versions.json                       pinned versions + hashes
  templates/Config.template.js        ACE config      -> runtime/ace/Config.js
  templates/my.template.ini           MariaDB config  -> runtime/my.ini
  templates/log4net.template.config   ACE logging     -> runtime/ace/log4net.config
  lib/LifestoneServer.psm1            shared helpers (process launch, SQL, checks)
  lib/send-console-command.ps1        types a command into ACE's private console (used for graceful stop)
  runtime/                            GITIGNORED - everything generated or downloaded
    ace/                              published ACE (+ Config.js, log4net.config, lifestone-build.json)
    ace/DatabaseSetupScripts/Updates/*/applied_updates.txt   which ACE update scripts were applied
    ace-mods/                         ACE Harmony mods folder (ModsDirectory)
    mariadb/                          extracted MariaDB 11.4.13 binaries (+ .lifestone-version.json)
    mariadb-data/                     MariaDB datadir (ace_auth, ace_shard, ace_world)
    mariadb-tmp/  mariadb-files/      tmpdir / secure_file_priv
    downloads/mariadb-11.4.13/        ZIP, sha256sums.txt
    downloads/ace-world-v0.9.296/     world DB release ZIP
    logs/                             mariadb.err, ACE_Log.txt, ACE_Log-WARN.txt
    my.ini  secrets.json  mariadb-root.cnf  mariadb-ace.cnf  pids.json  setup-state.json
```

The retail DATs are read from `client\` (`DatFilesDirectory`), never copied.

## What setup does

1. **ACE source:** clones the pinned tag if `vendor-eval\ACE` is missing, then verifies the commit and checks that
   the tree is clean.
2. **ACE build:** runs `dotnet restore` and `dotnet publish` with `-nodeReuse:false -p:UseSharedCompilation=false`,
   then `dotnet build-server shutdown`, so no build servers are left running. It finds the per-user SDK through
   `DOTNET_ROOT`, falling back to `%LOCALAPPDATA%\Microsoft\dotnet`. It skips the build when that commit is already built.
3. **MariaDB:** downloads the ZIP, verifies the hash and extracts it. The MSI is never used.
4. **Secrets:** generates the root and `ace` DB passwords and a test game account into `runtime\secrets.json`. The file
   ACL is cut down to the current user, and so are the ACLs on the `.cnf` client option files and `Config.js`.
5. **my.ini:** rendered from the template.
6. **Datadir:** `mariadb-install-db.exe --datadir=runtime\mariadb-data --port=3316`, with no `--service` and no password
   on the command line.
7. **Users:** the first `mariadbd` start uses `--init-file`, which runs before any connection is accepted. It sets
   the root password and creates `'ace'@'127.0.0.1'` with `ALL` on `ace_auth.*`, `ace_shard.*` and `ace_world.*`
   only. The file is deleted straight away. Every setup run also drops extra root and anonymous accounts, such as
   the `root@<computername>` that install-db creates. Under `skip-name-resolve`, TCP logins match `127.0.0.1`, not
   `localhost`.
8. **Databases:** loaded with the `mariadb` client and `--abort-source-on-error`; any `ERROR nnnn` fails setup.
   * `ace_auth` and `ace_shard` are created from ACE's `Database/Base/*.sql` **only if the database does not
     exist**. Setup never drops player data. The one exception is `-ResetEmptyDatabases`, which refuses to run if
     any account or character exists.
   * `ace_world` is created from `WorldBase.sql`, then the pinned release SQL is imported (148 MB, about 15 s). The
     release SQL itself drops and recreates `ace_world`. It is re-imported only when `ace_world.version.patch_Version`
     differs from the pin, or when you pass `-ReimportWorld`.
   * ACE's bundled `Database/Updates/{Authentication,Shard,World}/*.sql` are applied in name order. They are recorded
     in the same `applied_updates.txt` files that ACE uses, so ACE's own boot-time pass is a no-op. v1.78.4816 ships
     32 shard updates, and no auth or world updates.
   * Result for v0.9.296: auth 2 tables, shard 40, world 54; 43,916 weenies, 365,196 landblock instances, 6,266 spells.
9. **ACE config:** `Config.js` and `log4net.config` are rendered from the templates.

`mariadbd` runs only during steps 7-8 and is shut down cleanly before setup exits. Setup refuses to run while the
server is running.

### ACE configuration choices (`templates/Config.template.js`)

| Setting | Value | Why |
|---|---|---|
| `Server.WorldName` | `Lifestone` | |
| `Server.Network.Host` / `Port` | `127.0.0.1` / `9000` (+9001) | Loopback only |
| `Server.Accounts.AllowAutoAccountCreation` | `true` | The client's first login creates the account. ACE also promotes the **first** account it creates to Admin, because the auth DB has no admin yet. |
| `Server.DatFilesDirectory` | `<repo>\client\` (the absolute path, filled in by setup) | Retail end-of-retail DATs |
| `Server.ModsDirectory` | `server\runtime\ace-mods\` | ACE creates this folder if it is missing; its default (`c:\ACE\Mods\`) would be outside the repo |
| `Offline.AutoServerUpdateCheck` | `false` | It would call the GitHub API at boot |
| `Offline.AutoUpdateWorldDatabase` / `AutoApplyWorldCustomizations` | `false` | Versions are pinned; it would download the latest world release at boot |
| `Offline.AutoApplyDatabaseUpdates` | `true` | Local scripts only, and a no-op unless ACE itself is upgraded (setup pre-applies them) |
| `DDD.EnableDATPatching` | `false` | |
| `WorldDatabasePrecaching` | `false` | ACE's dev default. `true` adds 1-2 min to boot and uses about 1.7 GB of RAM |

**`client_highres.dat` is optional for ACE.** `ACE.DatLoader/DatManager.cs` opens it only if it exists ("This is not
required for ACE operation"). Only cell, portal and local_English are required. When highres is present, ACE logs
`client_highres.dat ... 2295 records, iteration 497` (the end-of-retail value).

ACE opens the DATs with `FileShare.Read`. If another process is still writing a DAT, ACE crashes at boot with a
sharing violation, and while ACE runs, nothing else can write the DATs. `start-server.ps1` opens every DAT in the same
mode first and refuses to start if one is locked.

## Running ACE in the background, and how stop works

ACE reads admin commands from its console (`Console.ReadLine`). The supported switches for unattended use are
environment variables:

* `ACE_NONINTERACTIVE_SETUP`, `ACE_SQL_*`, `ACE_SQL_INITIALIZE_DATABASES`, `ACE_SQL_DOWNLOAD_LATEST_WORLD_RELEASE`,
  `ACE_WORLD_NAME`, `ACE_DAT_FILES_DIRECTORY`, `MYSQL_USER` and `MYSQL_PASSWORD` drive the first-run wizard, which
  runs only when `Config.js` is missing. **We never use the wizard.** In non-interactive mode it hard-codes
  `Host = 0.0.0.0`, it swallows SQL errors, and it downloads the latest world DB.
* `ACE_NONINTERACTIVE_CONSOLE=true` disables the console command thread. Without it, a redirected or closed stdin
  makes ACE's command loop spin at 100% CPU, because `ReadLine` returns null in a loop.
* `DOTNET_RUNNING_IN_CONTAINER=true` is Docker mode. It reads and writes `/ace/Config/...`, which on Windows means
  `C:\ace\...`. Not used.

What we do instead: `start-server.ps1` starts both servers through `CreateProcessW` with `CREATE_NO_WINDOW` and
`bInheritHandles=FALSE`. Each server gets its **own private console with no window**. ACE's command prompt blocks on
that console, so it stays idle with no busy loop. It cannot steal the caller's keyboard input, Ctrl+C in your
terminal does not reach it, and it does not hold the caller's stdout pipe open. Logs go to files through log4net.

`stop-server.ps1` runs `lib\send-console-command.ps1` in a short-lived, windowless `powershell.exe`. That process
attaches to ACE's console and types ACE's own admin command **`stop-now`**. ACE then logs off players, disconnects
sessions, unloads landblocks, saves offline players, drains the shard DB queue and exits. MariaDB then gets
`mariadb-admin shutdown`. Either one is force-terminated only if it has not exited after `-TimeoutSec` (default 120).
`-Force` skips the graceful path.

Process identity is tracked in `runtime\pids.json` as PID + exe path + start time, so a recycled PID is never
killed. Untracked processes running the `runtime\` executables are also found and stopped.

## Credentials

`runtime\secrets.json` (gitignored, current-user ACL) holds:

* `mariadb.rootPassword`, for `root@localhost` and `root@127.0.0.1`.
* `mariadb.acePassword`, used by ACE through `Config.js`.
* `testAccount.username` / `password`: suggested game login for the client. ACE creates the account on first login,
  and it becomes Admin.

Use `runtime\mariadb-root.cnf` or `runtime\mariadb-ace.cnf` with the bundled client so that passwords never appear
on a command line:

```powershell
server\runtime\mariadb\bin\mariadb.exe --defaults-file=server\runtime\mariadb-ace.cnf -e "SELECT * FROM ace_world.version"
```

Losing `secrets.json` locks you out of the datadir. Restore it, or reset with
`stop-server`, delete `runtime\mariadb-data` and `runtime\secrets.json`, then run `setup-server` again.

## Security notes

* **No elevation:** every executable we launch from `runtime\` is checked for a `requireAdministrator` or
  `highestAvailable` manifest before it runs. MariaDB's own `mariadb-upgrade-wizard.exe` *does* request admin, and
  nothing ever runs it. All executables we run (`mariadbd`, `mariadb`, `mariadb-admin`, `mariadb-install-db`,
  `ACE.Server.exe`) are x64 with `asInvoker` manifests, so Windows installer-detection heuristics do not apply.
* **No services, registry, firewall, PATH or persistent environment changes.** `DOTNET_ROOT` is passed per child
  process only.
* **Outbound network:** only during setup (git clone, nuget.org, archive.mariadb.org, GitHub release download). At
  runtime ACE talks only to `127.0.0.1:3316`, because the update checks are disabled.
* **Build-file review of ACE v1.78.4816** (done before the first build):
  * No `Directory.Build.*`, `global.json`, `NuGet.config` or `.targets` files in the repo.
  * `ACE.Server.csproj` has one `Exec` (a PreBuild target) that copies `Config.js` / `log4net.config` between the
    project and output dirs with `cmd copy` or `cp`. It does no downloads.
  * Packages are restored only from nuget.org. Their imported build assets are Microsoft analyzer-wiring targets
    (EF Core, Extensions.Logging/Options, System.Text.Json, NETStandard.Library) with no `Exec` tasks.
  * There is one committed binary, `Source/lib/Lifestoned.DataModel.dll` (MIT, from gitlab.com/Scribble/lifestoned),
    SHA-256 `9dfebb310f0894deb3b5d73361902321bf246416f75d7250574077fb9f5b8b6e`.
  * Runtime HTTP use exists only in `Program_Setup.cs` / `Program_DbUpdates.cs` / `Program_BinUpdates.cs`, and our
    config disables all three.

## Troubleshooting

* Logs are `runtime\logs\mariadb.err` and `runtime\logs\ACE_Log.txt` (everything, including DEBUG).
  `ACE_Log-WARN.txt` holds warnings and errors only. On a fresh setup, the only expected warning is "no admin
  accounts".
* `start-server.ps1 -KeepOnFailure` leaves the processes running after a failed start so you can inspect them.
  `-DatabaseOnly` starts only MariaDB.
* "DAT files ... locked": another process is writing a DAT in `client\`. Wait for it to finish.
* ACE does not write anything except log4net output to its console. Raw `Console.WriteLine` output (for example the
  update-script progress) is not captured, because ACE's console has no window.

## Upgrading

Edit `versions.json` (version, URL and SHA-256 together), then re-run `setup-server.ps1`.

* A new ACE tag means a new commit pin. Review the new build files first, then run `setup-server.ps1 -RebuildAce`.
* A new world tag re-imports `ace_world` only.
* A new MariaDB version re-extracts the binaries. Major upgrades may need `mariadb-upgrade.exe` against the datadir.

## Licensing

* **ACEmulator** (ACE): AGPL-3.0. ACE's `LICENSE` also appends GPLv3 text that covers early contributors.
  `Lifestoned.DataModel.dll`, which ships with ACE, is MIT. If we distribute ACE binaries we must offer the
  corresponding source (AGPL §6). If we modify ACE and let others interact with it over a network, §13 also
  applies. We build unmodified upstream source at a pinned tag.
* **ACE-World-16PY-Patches** world database: AGPL-3.0. Shipping the SQL or a database derived from it means
  providing its source, which is the release ZIP plus the repo.
* **MariaDB Server**: GPL-2.0. The client libraries are LGPL. Redistributing the ZIP means meeting GPLv2 source-offer
  terms (sources at mariadb.org / archive.mariadb.org).
* The retail AC DAT files are user-supplied and are never redistributed.
