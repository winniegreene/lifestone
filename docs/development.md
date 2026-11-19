# Development

How to build, test and check Lifestone, and the options the test loops and the autopilot take. The
[technical guide](../TECHNICAL.md) has the short version; this is the whole of it. Every command runs from the repository root in
PowerShell 7 unless it says otherwise.

## Build and test

To build and run the unit tests without Godot, in PowerShell 7 from the repository root:

```powershell
# Only for a per-user .NET install that is not on PATH; this applies to the current terminal only.
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
# Keep the SDK's first run from adding to your user PATH or creating an HTTPS development certificate.
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

dotnet build Lifestone.slnx --disable-build-servers
dotnet test Lifestone.slnx --disable-build-servers --filter "Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Tier!=Tool&Status!=KnownFailure"
```

Or in Git Bash:

```bash
export DOTNET_ROOT="$(cygpath -u "$LOCALAPPDATA")/Microsoft/dotnet"; export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false DOTNET_GENERATE_ASPNET_CERTIFICATE=false
dotnet build Lifestone.slnx --disable-build-servers
dotnet test Lifestone.slnx --disable-build-servers --filter 'Category!=Loopback&Tier!=Soak&Tier!=Exhaustive&Tier!=Timing&Tier!=Tool&Status!=KnownFailure'
```

- The build treats every warning as an error, with the analysers pinned to the .NET 10 defaults. Packages come from
  nuget.org only (`NuGet.config`), and new ones are added deliberately.
- Tests that need the retail dats are reported as skipped, not failed, when the files are missing.
- `--disable-build-servers` keeps MSBuild and compiler servers from lingering after a build.
- **Test categories.** Tests that need the local server carry `[Trait("Category","Loopback")]` and live in
  `tests/Lifestone.Loopback.Tests`; they skip, with the reason, unless the backend is running with the Lifestone
  server mod ready. The tests that read the retail dats (the sweeps and the decoder and asset pins) carry
  `[Trait("Tier","Exhaustive")]`: they run at every gate (`tools\slice-check.ps1 -Exhaustive`, which a slice that touches
  `src/Lifestone.Data` or one of the Client folders whose tests moved (`src/Lifestone.Client/` `Audio`, `Effects`, `Interiors`, `Movement`, `Navigation`, `Objects`, `Play`, `PortalSpace`, `Rendering` or `Sky`) runs), not in the default run. Long soaks and fuzz runs carry `[Trait("Tier","Soak")]`. The `--filter` in the commands above
  leaves both out, as the smoke suite's test step does; without it the soaks run too (minutes, not seconds). With the
  backend running ([server/README.md](../server/README.md)), `--filter "Category=Loopback&Tier!=Soak"` runs
  the loopback tests, and `--filter "Tier=Soak"` runs the soaks.
- **Tool trait (M5 item 2).** `[Trait("Tier","Tool")]` marks a lead-run command written as a test: today `GroundDraftRunTests`, the hunting-ground draft (it writes under `cache\grounds\`, needs `LIFESTONE_GROUND_DRAFT` and skips without it). The default filters of `dotnet test`, `tools\smoke.ps1` and `tools\slice-check.ps1` leave it out, and so does the lead's timing run; run it with `--filter "FullyQualifiedName~GroundDraftRunTests"`.
- **Timing and known-failure traits (M4).** `[Trait("Tier","Timing")]` marks a test that measures wall-clock time and is
  only meaningful on a quiet machine; the default filter leaves it out and the lead runs it alone (`--filter "Tier=Timing"`),
  never beside another Godot or dotnet run. `[Trait("Status","KnownFailure")]` marks a test that is known to fail; it is never
  in the green total, and each one names its TODO row in the test's reason, so the failure stays visible in the plan. Both are
  left out of the default filter of `dotnet test`, `tools\smoke.ps1` and `tools\slice-check.ps1`; run one with
  `--filter "Status=KnownFailure"`. The tests that carry Timing are the wall-clock ones (the wayfinder's 1 km plan,
  the frame budgets of the streaming and hub planners, and the like); none carries KnownFailure.
- **Test count.** Coverage never decreases: `tests/Shared/test-count-baseline.json` holds the floor of the default run and
  of the Exhaustive tier (16,809 and 1,326 at M6's candidate; 9,238 and 736 at the end of M4), which `slice-check` enforces unless `-MinTests` is given. Only
  a deletion that names where its behaviour is proved elsewhere lowers it.

## Live tests and the live gate

The loopback tests and the live legs of `tools\smoke.ps1` play on the local backend
([server/README.md](../server/README.md)). Each automated run uses one of the test accounts, whose keys live in
`server\runtime\secrets.json` (gitignored; read the key names, never print the values):

| Account key | Characters | What it does |
|---|---|---|
| `test` | BSD Mage (level 126, the Black Spawn Den), Holtburg Hunter (the baseline fighter, never re-created), the baseline Lifestone Smoke and Lifestone Loopback (never reset or levelled), and Winnie, the ladder's climber in the live gate (re-created at each gate, never granted anything) | the mage's journey, the Hunter, the raw-net and headless classes, and the level up job's gate lane |
| `test-melee` | Holtburg Brawler (level 55, Heavy Weapons specialised, a mismatched set of retail magic gear with the Staff of All Aspects, Creature, Item and Life Magic and Mana Conversion trained; hunts the Low B belt) | the melee journey |
| `test-archer` | Holtburg Archer (level 55, Missile Weapons specialised, retail magic gear with the Serpent's Flight bow and Greater Frost Arrows, Creature, Item and Life Magic and Mana Conversion trained; hunts the low north forest) | the missile journey |
| `qwinnie` | Qwinnie, a second climber, cheat-free like Winnie, in no gate's lane | the long climbs and M6's sprint |
| `test-tools` | Lifestone Throwaway (its tests create, use, delete and purge it) and Lifestone Tools (its purge caller; created once, never levelled or reset) | the throwaway and the mod-verb classes (headless) |

- **Journeys.** A journey is one character's whole day as a list of stations (`--scenario=journey --journey=<mage|melee|archer>`
  headless, `--login-check=journey` in the game; [docs/session.md](session.md) section 41). The mage builds, buffs, casts,
  harm-heals, binds at the lifestone, fights in the den, dies once, recovers and recalls home; the Brawler and the Archer buff,
  bind, hunt (the belt, the forest), heal, recall home, sell, and open chests or set the shortcut bar. Trips go through the
  wayfinder ([docs/wayfinding.md](wayfinding.md)) or a retail travel spell (Lifestone Recall, Primary Portal Recall) before any
  server hop. The fighters train Creature, Item and Life Magic and Mana Conversion for their self buffs and Lifestone Recall, wear
  retail magic gear (owner decision D41) and swap to a caster to cast; a vitals keeper (`--vitals-*`) keeps health, stamina and mana up. Each station prints one `ok=` line, and the
  journey's `done` line carries the verdict.
- **Fresh characters.** Every live gate first deletes and re-creates the BSD Mage, the Brawler and the Archer (`--fresh-character`,
  the `recreate` step; [docs/session.md](session.md) section 42), and the new copy purges the old one. The gate characters
  end one level above their template (127 and 56). The Hunter, Lifestone Smoke and Lifestone Loopback are never re-created, and
  only Lifestone Throwaway may be levelled freely or purged.
- **Traits.** Each loopback class carries one `[Trait("Account", "<key>")]`, and its characters live on that account;
  `LoopbackConventionTests` enforces it. `[Trait("Lane", "solo")]` marks the classes that need the game to themselves
  (the movement-precision runs). A filter picks an account and a lane, for example
  `--filter "Category=Loopback&Tier!=Soak&Account=test-melee&Lane!=solo"`. Each account has its own lock file,
  `cache\loopback-<account>.lock`, so two runs never log in to one account at once.
- **Adding an account.** `pwsh -File server\setup-server.ps1 -AddAccount <name>` runs on its own (no other switch). It
  adds the name to `secrets.json` only; ACE creates the account at its first login. The new account has no character until
  a build scenario makes one, and the owner decides any new test character.
- **Session mode.** `LIFESTONE_LOOPBACK_SESSION=shared` (the default) keeps one live session per account for its tests:
  the first run of a test resets the known state (peace mode, the launcher and ammunition off, the rest weapon, Holtburg),
  and each run logs `[live-session]` lines (`reset`, `logged <name> in`, `relog after failure`). `per-test` logs every run
  in and out; `-Serial` in the gate uses it.
- **The live gate.** `pwsh -File tools\live-gate.ps1 [-Worktree <path>] [-Tag <name>] [-Loopback full|core|<areas>] [-JourneySolo <names>] [-Serial] [-CompareTo <tag>] [-KeepCharacters] [-Steps <names>] [-Round <n>] [-KeepBackend] [-NoDisplayHold] [-Soak] [-Plan]`
  runs the backend from the main checkout (it stops it in a finally), so only an item's lead runs it. The exit code is 0 only
  when every step passed; logs go to `cache\gate\<Tag>`.
  - **Parallel (the default).** Four account lanes run at once, three of them with Godot: `test` re-creates the mage, runs its
    short loopback classes, then the mage's journey; `test-melee` re-creates the Brawler, then runs the melee journey;
    `test-archer` re-creates the Archer, runs Shortcut's relog class, then the archer journey; `test-tools` (headless) runs the
    throwaway's and the mod-verb classes. While the lanes share the machine, frame times are reported, not judged.
  - **Solo.** Then one step at a time with nothing else running: the frame judge three times (`login-check`, `login`), the
    throwaway's frames, the leak scan and the movement-precision classes. `-JourneySolo <mage|melee|archer>` moves a journey
    into this phase, so its station frames are judged. The frame rule: over 75 ms fails, over 50 ms is flagged, and two of
    three runs over 50 ms fail the gate.
  - **Rerun, backend and display.** `-Steps <names>` runs only the named steps (a journey, a re-create, a class, `frame-judge`, `soak`), in a new `<Tag>-r<n>` folder or `-Round <n>`. `-KeepBackend` leaves the backend up after the gate for the lead's reads. The gate reads the display first and holds it on for its run (`-NoDisplayHold` skips the hold). A Godot process that is not the gate's makes a solo step wait `-ForeignGodotSeconds` (120 s by default) and then fail by name. Section 54 of `docs/session.md` has the details.
  - **Options.** `-Serial` runs the three journeys headless, one after another, each after its own re-create; `-CompareTo <tag>`
    prints them beside the journeys of a default-mode gate on the same commit. `-KeepCharacters` skips the re-create (a rerun
    of one journey). `-Loopback core` (the default) is the fixed list in `tools\lib\GateAreas.psd1` plus the areas the diff
    touches (`net`, `movement`, `actions`, `fighter`, `mage`, `servermod`); `full` runs every non-Soak class, at a wave's last
    item and the milestone's end; an area list runs those areas only. `-Soak` adds the Soak-tier loopback tests once, alone,
    at the end: the real 1,800 s buff expiry (`EnchantmentExpiryLiveTests`) and the lossy-network walk. `-Plan` (or `-WhatIf`)
    prints every step and runs nothing.
  - **Time.** The gate that closed the fresh-characters item took 1,613 s, against about 4,860 s for the old order (the
    per-feature loopback classes and smoke legs, one after another).
  - **Frame cap.** None since M5 item 1 (m5-plan.md D28): item 0 capped the journeys at `-MaxFps 60` (`--max-fps=60`; the gates
    ran at 239 to 241 fps uncapped), and the archer's forest hunt failed capped three times and passed uncapped; `$script:GateMaxFps`
    set over 0 caps them again. The frame judge (`login-check`, `login`), the throwaway's frames and the leak scan were never capped:
    the judge checks frame time against the Holtburg 60 fps floor (`LoginCheckRunner.HoltburgMinimumFps`), which a 60 cap would fail
    on the cap itself. `-Plan` prints the cap once (`[gate-plan] frame cap (M5 item 0), dropped in M5 item 1 (D28)`).
  - **Godot errors.** Each smoke-backed row carries `godot_errors=<n>`, Godot's own `ERROR:`, `SCRIPT ERROR:` and `USER ERROR:` lines
    in that step's console (Lifestone's echoed errors do not count), and the gate judges it 0 (M5 D19, the one named exception to
    report-only-first). A Godot row whose smoke row carries no count fails; headless, dotnet-test, build and re-create rows read
    `godot_errors=n/a`.
- **The audio check.** Every automated Godot run must log `audio service installed ... muted=true`; a smoke step whose
  run does not fails with `audio check`. Capture runs count as automated. `tools\play.ps1 -Audio` is refused with `-Offscreen`,
  since an offscreen run is the muted path.
- **The soak watcher.** `tools\soak-watch.ps1 [-IntervalSeconds 300] [-DurationSeconds N] [-Once] [-OutFile cache\soak\x.csv]` is
  read-only, beside a soak run: every interval it appends one CSV row and prints one `[soak-watch]` line (the Godot, `ACE.Server` and
  `mariadbd` processes' memory, handles and threads, the size of the logs and database folders, the free space on the repository's
  drive). It starts, stops and kills nothing and reads no file contents. The soak line's fields are in docs/session.md section 44.
- **The soak run.** `tools\soak.ps1`: the overnight run in one command (the item's lead only; refuses without the backend, the lead's lock `cache\backend-owner.txt` or with a Godot running; `-Plan` prints it; `-Record` films the window, `-ControlDrill` runs the `/stop` and `/auto` take-over drill), and `tools\soak-report.ps1`: its read-only report of a run folder (`cache\soak\<stamp>\report.txt`).

## Slice check

- `pwsh -File tools\slice-check.ps1 -MinTests <count> -Steps <steps> [-Smoke] [-Rerun]` checks a slice in one command and
  prints one line per stage, `[slicecheck] <stage> PASS|FAIL|SKIP <detail> (<seconds>s)`, then `[slicecheck] done ok=true|false`
  (exit 0 or 1). Stages: `build` (warning and error counts), `test` (the default `dotnet test`, with the passed count per
  assembly; it fails when fewer than `-MinTests` passed, and without `-MinTests` the baseline file is the floor), `exhaustive`
  (the Exhaustive tier), each Godot step of `-Steps` (run through `smoke.ps1 -Only <step> -NoBuild -Backend auto`, so the
  command lines live in smoke only; `retail-ui-checks` is an alias of smoke's `retail-ui`) and, with `-Smoke`, the whole smoke
  suite. `-Stages build,test` runs a subset; `-Rerun` reruns a failed Godot step once (`PASS (rerun)`). A FAIL prints at most
  15 lines of the failure and the log path; full logs are in `cache\slice-check\`. It never starts the backend (`-Backend`
  accepts only `auto`).
- **Godot errors.** The steps stage prints `godot_errors=<n|n/a>` at the end of each step's line, and the smoke stage prints the
  total in its verdict and fails on any. `-Rerun` does not rerun a step whose first run reported `godot_errors` above 0: its FAIL
  and its count stand.
- **Which form, when.**
  - `-Gate` is G-A, the check before every live gate: build, default tests and the Exhaustive tier, no Godot step (a few minutes).
  - `-Changed` adds the backend-free Godot steps the diff selects (`tools\lib\StepPaths.psd1` maps folders to steps). It runs once
    per item, after its last live gate and before the merge.
  - `-Smoke` runs the whole offline smoke, at a wave's last item and the milestone's end.
  - `-Exhaustive` adds the Exhaustive tier to a slice that touches `src/Lifestone.Data` or the Client folders listed above.
- **Window size.** Every Godot check and frame run is at retail's native 800x600 (the owner's rule, 2026-10-14; M2 D57), and so is
  the frame judge (`login-check`, `login`). The opt-in `-AllSizes` (on `slice-check.ps1` and `smoke.ps1`; M5 D34) runs the shell
  frames (`shell-frames`) and the play-checks also at 1280x720 and 1920x1080, each size in its own `<W>x<H>` folder (the play-checks'
  verdict per size in `play-checks/<W>x<H>/play-checks.txt`), and the retail-ui frames also at 1920x1080; it also still runs the
  2560x1440 placement check of `--retail-ui-checks` (`TODO(uicheck-hidpi-split)`). `-HiDpi` adds the 2560x1440 scale-2 shell run
  (opt-in, not in the gate). `slice-check -Smoke` implies `-AllSizes`; the item-end offline check is `slice-check -Changed -AllSizes`
  or `-Smoke`. None of it runs by default. `LIFESTONE_ALL_SIZES=1` runs the one Client placement test at 1920x1080. From M5 item 7's gate three judges that were report-only are judged: the autopilot step's cycles>=2 (a value under 2, or the field missing, fails the step: "cycles=<n> (judged >=2)"); door_opened=yes on the melee journey's vendor and vendor-facts stations (a station that ran with another value, or without the field, fails the journey; a station that did not run is n/a and fails nothing); and the 1280x720 and 1920x1080 runs of -AllSizes (the play-checks and the shell frames), where a failed row fails the step except the play-checks' frame-time rows, which stay report-only above 800x600 (frame_time_rows=<n> (report-only above 800x600); frame time is judged only at 800x600, DoD 12). godot_errors=0 is judged at every size. stepped_off=, the physics fields and the autopilot's other fields stay report-only.

## Smoke suite and golden viewpoints

- `pwsh -File tools\smoke.ps1` is the integration suite: the build, the tests, `--help`, the terrain golden test,
  the four engine check suites, a spell-viewer showcase, the golden viewpoints and the Holtburg-to-Arwic flight, all
  off-screen, with a summary table and logs in `cache\smoke\`. It needs the dats and Godot.
- **The backend in smoke runs.** A step that cannot run reports `SKIP` (yellow), which is not a failure.
  `-Backend` decides what the steps that need the local server do:
  - `auto` (the default) runs them only when the backend is already up, and never starts it;
  - `start` starts the backend, then stops what it started;
  - `require` fails them instead of skipping them when the backend is down.
  - The three journey steps (`journey-mage`, `journey-melee`, `journey-archer`) run one character's whole day in the game
    (`--login-check=journey`) and, like `login-throwaway`, only under `-Backend require`; the live
    gate runs them.
  - The `login-throwaway` step (`--login-check=throwaway`, docs/session.md section 28) creates, uses, deletes and purges a character on the test-tools account (its purge caller is Lifestone Tools). It is not part of
    `--login-check=all` and runs only under `-Backend require` (skipped under `auto` and `start`).
- The `record-audio` step (M3 1.3 s3, no backend) runs `--play-checks --record-audio=<dir>` on the stand-in server: the game's own sound is recorded from the Master bus
  (automated, so muted: M2 D8), read back by `WaveLevels` and judged non-silent (peak above -60 dBFS and an active 50 ms window). The per-source counters
  (`sources=`) are printed, not judged.
- **Godot errors.** A Godot step also counts Godot's own errors, as `godot_errors=<n>` in its row: the console line, the summary
  table's `godot_errors` column, `summary.json`'s `godot_errors`, and the summary's closing `godot_errors=<total>` line. A Godot step
  with `godot_errors>0` fails, and its detail names the first such line. The "Errors logged" column stays Lifestone's own ERROR
  count. A C# unhandled exception counts once; Lifestone's own ERROR entries that `Log.ToGodot` echoes through `GD.PushError` are
  not Godot errors.
- **`-MaxFps <n>`** appends `--max-fps=<n>` to every Godot step's game arguments (0 = none, the default; only the live gate passes 60).
- The golden viewpoints render to `docs/screenshots/m1/golden/` (ignored by git) with the positions and times needed
  to take the same views in the retail client:

  ```powershell
  pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--golden-viewpoints'
  ```
- Every command-line option is registered once, with its help, in `src/Lifestone.Client/Options/OptionCatalog.cs`;
  a test fails when a parsed option is missing there.

## Options

| Option | Effect |
|---|---|
| `--start=LOC` | Where to start: `holtburg` (default), `arwic`, a curated interior such as `mines-of-despair`, a landblock (`A9B4`) or a cell (`A9B40019`) |
| `--walk` | Start on foot |
| `--time-of-day=F` | Time of day as a fraction of the day, 0 to 1 (default 0.5) |
| `--time-speed=X` | Game seconds per real second (0 pauses; 1 is retail) |
| `--vsync=off --max-fps=0` | Uncapped frame rate, for benchmarking (with `--vsync=off` alone the cap is the display's refresh rate) |
| `--soak-line-s=S` | Seconds between `[soak]` lines in the log (default 60; 0 = none); the fields are in [docs/session.md](session.md) section 44 |
| `--log-copies=console\|file\|all` | Where log lines go besides `lifestone.log` (default `console`; `file` drops the console copy of Debug, Info and Warning lines; `all` = the three copies) |
| `--log-max-mb=N` | Caps `lifestone.log` at N MiB (default 256; 0 = no cap): the file is renamed to `lifestone.1.log` and a new one begins, so the logs on disk stay under 2N MiB. [docs/session.md](session.md) section 44 |
| `--soak-frame-s=S`, `--soak-frame-dir=DIR` | Saves a screenshot every S seconds in play mode (0 = none; 60 to 86400) into DIR (default `cache/soak/frames`). Section 44 |
| `--load-radius=N` | Landblock rings to stream around you (default 8, as retail) |
| `--retail-normals`, `--retail-sky-anchor` | Restore retail's rendering quirks (see [Principles](../TECHNICAL.md#principles)) |
| `--retail-picking` | Retail's picking rules in play mode: a part's first hit polygon and per-part hit times, where a click can pick a different object than the nearest ([avatars.md](avatars.md) section 18) |
| `--dat-dir=DIR` | Read the dats from another folder |
| `--fov=DEG` | The world camera's vertical field of view, 1 to 179. In play mode the default is retail's (73 degrees at 800x600) and is recomputed when the window changes size; a `--fov` you give is kept |
| `--retail-confirm-slot` | Retail's single confirmation slot: a second question from the server is dropped while one shows. Default: the dialogs queue. Part of `--retail-ui-glitches` |

Playing on the local server and the test loops, which need it (`--help` lists every option, grouped, with its default):

| Option | Effect |
|---|---|
| `--buff-auto=on\|off` | The re-buff driver (default off); `--buff-auto-check-s` and `--buff-rebuff-margin-s` set how often it looks and how early it recasts. The `Pause` key switches it in play |
| `--examine-icon=on\|off` | The examine window's item icon (default off, as retail's layout has none) |
| `--heal*`, `--vitals-*`, `--loot*`, `--sell`, `--buy-kits`, `--hunt-*` | The hunt's recovery, vitals-keeper, loot, vendor and hunt rules |
| `--scenario=<name>` | A headless run on the local server: builds (`mage-build`, `fighter-build`), `buff`, `fight`, `death-and-regear`, `hunt`, `wayfind` (`--to=CELL`, `--from`, `--wayfind-back`) and `journey` |
| `--fight-ground=<key>` | The hunting ground of the fight, the death and the journeys' den: the profile whose key is its file name under `src/Lifestone.Client/Session/Grounds/profiles/` (default `black-spawn-den-aluvian`; an unknown key fails at once). [docs/session.md](session.md) section 52 |
| `--fight-stamina-full=N` | The stamina line the fight refills to with Revitalize Self (default 90, 0 is off; M5 7 F5, D88). Under the line the mana may run down; the stamina is kept near full. [docs/session.md](session.md) section 56 |
| `--fight-drain-under=N`, `--fight-drain-until=N` | The fight's health lines (defaults 50 and 75; M5 7 F7, D88): under the first, with a target in line, the fight casts Drain Health Other (Heal Self with no target in reach); the rule holds until the second. [docs/session.md](session.md) section 56 |
| `--fight-defend-forget-s=S`, `--fight-defend-hold-s=S` | Self-defence (M5 7 F4, F15): a creature that attacks the player is fought before the profile's targets until it has not attacked for S seconds (default 20; 0 turns it off; 0 to 600). The hold at a creature in a no-go cell is bounded (default 30 s; 0 is no bound). [docs/session.md](session.md) section 56 |
| `--fight-life-through=on\|off` | Whether an attacker no bolt can reach is fought with Life Magic (default on; M6 item 14): the server asks no line of sight of a spell with no projectile, so with no clear line to an attacker, or after the shot rule dropped it, the mage casts Drain Health Other or Harm Other at it where she stands in place of the walk. Three casts in a row that do not land set that attacker aside for 120 s. [docs/session.md](session.md) section 113 |
| `--fight-survival-mana=on\|off` | Whether the survival layer decides the fight's mana loop and stamina drain (default off; M6 item 16, stage 1 of the fight under the survival layer): on, the layer's rule answers the press, the stamina drain and its target, or nothing, on the same reads, and the fight carries it out; the default flips after a live gate. |
| `--fight-opener-tries=N` | The opener's presses on a resist or a fizzle (default 3, 0 to 10; M5 7 F8). The den's Tusker Slave opens with Fire Vulnerability Other. [docs/session.md](session.md) section 56 |
| `--mage-level=L` | The level the BSD Mage build reaches (50 to 126, default 126; M5 7 F3, F6). Below 126 the build is the traditional mage (no Melee Defense). Only `--scenario=mage-build` and the mage journey take it. [docs/session.md](session.md) section 56 |
| `--fight-storeys-min=N` | The fight's tour: after a kill the tour leaves a storey for the next until N storeys are visited (1 to 16, default 1); the mage journey's den-fight station uses 2. [docs/session.md](session.md) section 52 |
| `--fight-loot=on\|off` | Whether the fight loots its kills (default off, M5 D38): after a kill the mage walks to the corpse on foot, opens it, keeps what its rule says (pyreals first, then value per burden, within the load line and the free slots) and closes it. Off, the fight is the one without the step. [docs/session.md](session.md) section 53 |
| `--death-recover-abort-health=PCT`, `--death-recover-abort-seconds=S` | The corpse trip's aborts (defaults 40 percent and 90 s): under the health line the trip holds, heals and goes on; only failed heals or the time give the corpse up, named `trip_abort=`. [docs/session.md](session.md) section 53 |
| `--death-decoys`, `--death-stages=vitae\|regear` | Refused by name: the decoys are retired (a death drops the mage's own gear and the corpse trip brings it back), and the vitae and regear stages are gone (the vitae clears by earned experience). [docs/session.md](session.md) section 53 |
| `--journey=<mage\|melee\|archer>` | With `--scenario=journey` (headless) or `--login-check=journey` (the game, through `tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds N -GameArgs:'...'`): one character's whole day, station by station. `hero-archer`, `hero-brawler` and `hero-mage` are the hero video's journeys: the lower-right bar filled from the pack, the Brawler's store, the mage's den cleared of its own leftover corpses (`@lifestone corpses rot`), its death and re-buff |
| `--journey-film`, `--journey-shots` | With `--login-check=journey`: `--journey-film` turns retail's chase camera for each station (as a player turns it) and opens the attributes page at a re-buff and the inventory with the paper doll at a loot, saving no frame, for a screen capture of the window; `--journey-shots` saves frame bursts at the showpiece moments instead |
| `--login-check=keep` | The keep leg (M5 6 k2): one login flips a character option and moves a window; the next login shows both kept. Runs on the test-tools account only (as Lifestone Throwaway), never in `--login-check=all` |
| `--fresh-character` | With the character builds: delete and re-create the journey character first (the live gate does this) |
| `--autopilot-start[=on\|off]` | The play host only: starts the autopilot once, at login, as `/auto` does (BSD Mage only). A run it started logs off and quits the game at its own end (exit 0 for ok=yes, 1 otherwise). Off by default. Section 54 |
| `--scenario=autopilot`, `--login-check=autopilot` | The autopilot (M5 item 4): a headless run, or the game's leg, that plays BSD Mage's day on the `test` account by itself (stations, fights, trips, deaths, reconnects) until its stop rule. [docs/session.md](session.md) section 54 |
| `--autopilot-minutes=M`, `--autopilot-level=L`, `--autopilot-trip-after-kills=N`, `--autopilot-test-death[=on\|off]` | The autopilot's stop rules (a budget of minutes on the session clock, a character level), the forced trip after N kills (once a run) and the scripted death (once a run). Section 54 |
| `--autopilot-checkpoint-dir=DIR`, `--autopilot-watchdog-scale=F`, `--autopilot-watchdog-recoveries=N`, `--autopilot-reconnect-waits=S,S,...` | The headless run's checkpoint folder; the watchdogs' scale (default 1) and recoveries a cycle (default 2); the reconnect's waits (default 5, 15, 30, 60, 120 s). Section 54 |
| `--autopilot-test-interrupt-at=S`, `--autopilot-test-handback-after=S` | The gate's scripted hand-off (D58; typed since D145): `/stop` typed in the chat at S seconds into the fight, then `/auto` the second value's seconds after it. Section 54 |
| `--autopilot-job=den\|level-up`, `--autopilot-climber=NAME` | The autopilot's job (M6): `den` is M5's day in the den; `level-up` is the ladder's climb, played by a climber character |
| `--autopilot-ladder-quests=on\|off\|KEY,...` | The level up job's quests: `on` (the default) runs every ladder errand and tusk turn-in, `off` none, and a list only the quests it names (a kill task runs only when named) |
| `--autopilot-hold-grounds=KEY[,KEY]` | Grounds the level up job keeps out of for the session |
| `--autopilot-steer-dir=DIR`, `--autopilot-steer-orders` | The steer desk: a run folder through which `tools\order.ps1 -Run <run> <order>` reaches a running autopilot, as the chat's orders do |
| `--walk-awareness=pinned\|own` | How far the automation's walks keep from a hostile creature: each creature's own noticing range (`own`, the default) or one pinned distance |
| `--play-look-shots=DIR`, `--play-panel-shots=DIR` | Save review frames of held items, or of every panel page |

The climb in one command: `pwsh -File tools\climb.ps1 [-Climber <name>] [-TargetLevel <n>] [-Minutes <n>] [-Record] [-PlanOnly]`
creates the climber fresh, walks her through the Academy and starts the level up job in the game window, recording
the window when asked (`-PlanOnly` prints every step and starts nothing; `Get-Help tools\climb.ps1 -Full` lists the
rest).
