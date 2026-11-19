# Lifestone: notes for coding agents

Lifestone is a faithful, single-player Asheron's Call. It is a Godot 4.7 (.NET, C#) client that:
- renders the user's own retail dats;
- talks the AC protocol to a bundled ACE server on localhost.

Read `README.md`, `TECHNICAL.md` and `docs/goals.md` for the project. The current milestone is **M6, "the level ladder"**, under way: a fresh mage that climbs from level 1 on its own over
varied grounds, cheat-free. Its plan is [docs/milestones/m6-plan.md](docs/milestones/m6-plan.md) (canonical) and its spec
[docs/milestones/m6-the-ladder.md](docs/milestones/m6-the-ladder.md); items 0-7 are done; items 8 and 14 (the buff bot) are merged unfinished (D134), and the climb is built out as the climber Qwinnie climbs (D131). The seed list of grounds is in [docs/milestones/m5-first-dungeon.md](docs/milestones/m5-first-dungeon.md)
section 7.1. M5 ([docs/milestones/m5-first-dungeon.md](docs/milestones/m5-first-dungeon.md), **"the first dungeon"**: a mage
that plays the Black Spawn Den by itself for hours, cheat-free; its plan, gates, decisions and open TODOs in
[docs/milestones/m5-plan.md](docs/milestones/m5-plan.md), section 9's last table the rows placed M6) is complete (2026-10-31).
M4 ([docs/milestones/m4-fights-and-loot.md](docs/milestones/m4-fights-and-loot.md), "fights and loot"; its plan,
gates, decisions and open TODOs in [docs/milestones/m4-plan.md](docs/milestones/m4-plan.md), §9 the rows placed "M5"),
M3 ([docs/milestones/m3-lights-and-sound.md](docs/milestones/m3-lights-and-sound.md), "fights and sound" in prose;
its plan [docs/milestones/m3-plan.md](docs/milestones/m3-plan.md)) and M2
([docs/milestones/m2-log-in.md](docs/milestones/m2-log-in.md)) are complete.

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
  - Commit through `tools/commit.ps1`, which sets `TZ=UTC` and uses the repository's configured identity.
  - Push only `master`, only when the owner asks, and never with `--all` or `--mirror`.

### Data and sources

- Never commit retail dats or anything derived from them: images, audio, dumps, fixtures, screenshots or videos.
  Derived data may go in `cache/` (gitignored).
- Never paste decompiled Turbine code. Re-express the behaviour, and cite the function name and its 11.6096 address.
- Ported code carries a header naming the source repository, commit, path and licence, and is listed in
  `docs/third-party/`.

### Numbers come from the corpus

Everything about how the game behaves is on this machine: ACE's source, the world and shard databases, the retail dats
and the live logs. A chance, a rate, a hit size, a kill time, a spawn count or a "can she..." is looked up or simulated
before it is said or written. It is never guessed, hoped, expected or rounded from memory.

- **Ask the server first.** `pwsh -File tools\fight-sim.ps1` runs ACE's own combat code offline on the real creature and
  the character as last saved ([docs/fight-sim.md](docs/fight-sim.md)). Any agent runs it, from any worktree; it starts
  nothing and writes no database.
  - `matchup <creature>`: whether her spells land, what they hit for, the casts and seconds a kill takes, what the
    creature does to her, how many at once she survives.
  - `rank`: every placed creature and ground, ranked for her, with the way in.
  - `her`, `creature <name>`, `cast <spell>`: the facts about her, a creature and a cast.
- **What it does not answer, read at the source:**
  - a rule or formula: `vendor-eval/ACE/Source/ACE.Server`, cited by file and function;
  - a creature, item, spell, spawn, portal or quest fact: a read-only SELECT through `server\runtime\mariadb-ace.cnf`;
  - geometry, animations and the spell table: the dats through `Lifestone.Data`;
  - what happened: the live log, cited by file and time.
- **Every number names its source** in notes, briefs, commit messages and code comments: the simulator's command, the
  ACE function, the SELECT, or the log line. A game number with no source is a defect. In chat with the owner the number
  is stated plainly, and the source goes in the notes.
- **A guess is a defect.** "I hope", "should be able to", "probably", "I expect", "about half", "seems to" and
  "roughly" do not belong in a sentence about game behaviour. Replace the sentence with the number, or with "not known
  yet" and the command or query that answers it, and run that before going on.
- **In code,** a constant that stands for a game fact is derived where it is used (the creature table, the spell table,
  the live state), or it is a named `TODO(...)` that logs loudly. A hand-typed game number is never a resting place.
- **Before** a new ground, a new fight rule, a change to one, or a claim to the owner about a fight: the simulator's
  output for it is in the note.
- **When the simulator and a live log disagree,** that is a finding. Stop and report both numbers.

### Running Godot

Automated runs go through `pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds <n>`. Keep
`-IsolateUserData` and `-TimeoutSeconds`. A visible window is fine (the owner's correction of 2026-10-14): `-Offscreen`
stays the documented flag but does not hide the window. Tests, checks and review frames run at 800x600 (M2 decision
D57); larger sizes are opt-in.

Video: every recording is of the game window itself, never a screen region (the owner, 2026-11-16). Use
`tools/WindowCapture.psm1` (ffmpeg `gfxcapture`, Windows Graphics Capture); never gdigrab or ddagrab (`docs/hero-video.md` §9).

### Rules for agent sessions

Every brief cites this section instead of restating it. All of these are in force for every agent, lead or slice.

- **Database:** no writes of any kind. Read-only SELECTs only, through the existing `server\runtime\mariadb-ace.cnf`, or
  through `tools\fight-sim.ps1`, whose reads are ACE's own queries (it never initialises ACE's writer, so no save can
  run).
- **Backend:** it runs only from the main checkout, and only an item's lead starts and stops it. Never
  `-AllowStaleMod`. `server\setup-server.ps1` runs only with the owner's go-ahead (for M4, `-ModOnly` is
  pre-authorised for the leads: docs/milestones/m4-plan.md §7, D8, as M3 D6 was). Slices never run `server\*.ps1`, `tools\play.ps1`
  against a live server, Loopback or Soak tests, or `smoke.ps1 -Backend require|start`; loopback tests they write must
  compile and say what they prove, and the lead runs them.
- **Accounts:** automation uses the test accounts `test`, `test-melee`, `test-archer` and `test-tools` (M4 D39) and `qwinnie` only. The graduated test
  characters (`Lifestone Smoke`, `Lifestone Loopback`) are the baseline and are never reset or levelled. M3's `BSD Mage`
  (m3-plan.md §7, D3) and M4's `Holtburg Hunter` (m4-plan.md §7, D1), `Holtburg Brawler` (`test-melee`) and `Holtburg
  Archer` (`test-archer`) (both M4 D31) are levelled only by their build scenarios and by the loops' kills and are
  never reset or purged, with one exception: the live gate deletes and re-creates `BSD Mage`, `Holtburg Brawler` and
  `Holtburg Archer` at the start of each gate (their builds' `--fresh-character`, M4 D44), and the new copy purges the
  old one (M4 D45). `Holtburg Hunter` is never re-created. M6's `Winnie` (on `test`; m6-plan.md §7, D7) is the ladder's climber, re-created at each gate by the M4 D44/D45 pattern and run alone on `test` after the gate's lanes (D25); it is never granted anything (D10, D34). Only `Lifestone Throwaway` (on `test-tools`) may be levelled freely or purged. `Lifestone Tools` (on `test-tools`, the Throwaway's purge caller; owner decision D43) is created once when absent, never levelled or reset: it only enters and leaves to purge. `Qwinnie` (on `qwinnie`; the owner's decision) is the local model's own climber, cheat-free like Winnie: only she and her own tools run on that account, and it is in no gate's lane. A new test character is the
  owner's decision.
- **Off limits:** never open or print the user profile's `.lifestone-secrets` key file, the values in
  `server\runtime\secrets.json`, or the files listed in `.git/info/exclude`.
- **Godot:** as "Running Godot" above; other agents may run Godot at the same time (at most three), so a frame-time
  failure is likely contention: rerun once, then report it.
- **Git:** commit with `tools/commit.ps1` on your own branch; push only when the owner asks; never `git stash`; never
  touch master or another worktree (the one exception: an item's lead merges the item branch to master at the end of
  the item's gates); leave existing worktrees alone unless a decision names them.
- **Numbers:** as "Numbers come from the corpus" above. `tools\fight-sim.ps1` is not a server script: a slice runs it.
  A report states each game number with its source, and holds no guess.
- **Checks:** no silent retries; a judged verdict field reads what it names on screen or in the state; a new judge is
  report-only in its first gate.
- **Roadmap:** at every slice or phase transition, a compact done / now / next roadmap.

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
dotnet test Lifestone.slnx --disable-build-servers --filter "Category!=Loopback&Tier!=Soak"   # Loopback/Soak: TECHNICAL.md, Development
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
- **`tools/`:** the Godot launcher, the smoke suite and helpers; `tools/fight-sim.ps1`, ACE's own combat code run
  offline ("Numbers come from the corpus").
- **`docs/`:** design docs, milestones and research.
