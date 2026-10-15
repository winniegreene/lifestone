# Lifestone: notes for coding agents

Lifestone is a faithful, single-player Asheron's Call. It is a Godot 4.7 (.NET, C#) client that:
- renders the user's own retail dats;
- talks the AC protocol to a bundled ACE server on localhost.

Read `README.md` and `docs/goals.md` for the project. The current milestone, with the handoff for the agent who leads
it, is **[docs/milestones/m2-log-in.md](docs/milestones/m2-log-in.md)**.

## Hard rules

### Machine safety comes first

- **Elevation.** Never cause an elevation (UAC) prompt without first telling the owner exactly what it is (program,
  publisher, purpose) and getting their go-ahead in chat.
- **Installs.** Per-user, no-admin installs only. Make no service, scheduled-task, registry, firewall, PATH, persistent
  environment, certificate or file-association changes.
- **Network.**
  - Servers bind to 127.0.0.1 only.
  - Never connect to game servers or other machines on the local network.
  - Use the internet only for pinned downloads, package restore and reading public sources.
- **Third-party code.**
  - Pin every download (version and SHA-256) and verify it.
  - Read third-party code before building or running it.
  - Add no NuGet package without the owner's approval.
- **.NET.** For every `dotnet` process:
  - set `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` and
    `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`. Otherwise the SDK's first run edits the user PATH and creates a
    certificate;
  - pass `--disable-build-servers`.
- **Git and signing.**
  - Don't run `gpg` or `git verify-commit`, which create a keyring. Check signatures with `gh api` instead.
  - Never use `git stash`: parallel worktrees share it.
  - Worktrees need `git config core.longpaths true`.

### The repository is public

- **No personal details.** Tracked files never contain:
  - paths outside the repository;
  - user, machine or account names;
  - IP addresses, e-mail addresses or handles;
  - quotes from conversations.

  Write `<repo>` for the checkout.
- **Private files.** Files listed in `.git/info/exclude` are the owner's private files. Never open, read, quote or
  commit them.
- **Credentials.**
  - They live only in `server/runtime/secrets.json` (gitignored).
  - Never print, log, record or commit a password.
  - ACE's packet log writes the login password in clear text, so it stays under `server/runtime/`.
- **Commits and pushes.**
  - Commit with `TZ=UTC` and the repository's configured identity.
  - Push only `master`, only when the owner asks, and never with `--all` or `--mirror`.

### Data and sources

- Never commit retail dats or anything derived from them: images, audio, dumps, fixtures, screenshots or videos.
  Derived data may go in `cache/` (gitignored).
- Never paste decompiled Turbine code. Re-express the behaviour, and cite the function name and its 11.6096 address.
- Ported code carries a header naming the source repository, commit, path and licence, and is listed in
  `docs/third-party/`.

### Running Godot

Automated runs always go through `pwsh -File tools\godot.ps1 -Offscreen -IsolateUserData -TimeoutSeconds <n>`, so no
window appears on the owner's desktop. Open a visible window only when the owner asks to watch.

### Quality

- **Fidelity.** Reproduce retail's art and behaviour, but not its glitches. Each glitch fix is the documented default,
  and a flag restores retail (`docs/milestones/m1-explore-dereth.md`, "Fidelity rules").
- **Loud stubs.** An unknown is a named, logged TODO that fails visibly, never a silent guess.
- **Placeholders.** Say when something on screen is a placeholder.
- **Visible results.** The owner judges progress by what is on screen, so get real visuals in early.

## Build and test

```powershell
# PowerShell 7, from the repository root; only needed for a per-user .NET SDK that is not on PATH
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
dotnet build Lifestone.slnx --disable-build-servers
dotnet test Lifestone.slnx --disable-build-servers --filter "Category!=Loopback&Tier!=Soak"   # Loopback/Soak: README, Development
pwsh -File tools\smoke.ps1
```

```bash
# Git Bash
export DOTNET_ROOT="$(cygpath -u "$LOCALAPPDATA")/Microsoft/dotnet"; export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false DOTNET_GENERATE_ASPNET_CERTIFICATE=false
dotnet build Lifestone.slnx --disable-build-servers && dotnet test Lifestone.slnx --disable-build-servers --filter "Category!=Loopback&Tier!=Soak"
```

The build treats warnings as errors. Tests that need the dats skip when the dats are missing.

## Layout

- **`src/Lifestone.Data`:** dat decoding and retail algorithms, with no engine.
- **`src/Lifestone.Client`:** engine-agnostic client logic.
- **`src/Lifestone.Game`:** the Godot project, which is presentation only. The AC → Godot conversion happens only in
  `scripts/Core/AcToGodot.cs`.
- **Options.** Every command-line option is registered in `src/Lifestone.Client/Options/OptionCatalog.cs`.
- **`server/`:** the localhost ACE and MariaDB backend (`server/README.md`).
- **`tools/`:** the Godot launcher, the smoke suite and helpers.
- **`docs/`:** design docs, milestones and research.
