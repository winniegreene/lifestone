# Autopilot command contract

**Approved by the owner 2026-11-09, with the three decisions in section 14. Nothing here is built, and building it needs its own go.** Written from a
read of `m6/7` at `b9eab19d` (2026-11-06), then corrected after two independent reviews against the code. Its citations are pinned to `master` at `5966212e`, and
`check_citations.py` re-checks every code reference in the appendix against any later commit. Paths use `<repo>`, and nothing here is private, so this file can move into the repo's docs when the seam starts.

This is the one way anything outside the autopilot asks it to do something. A plain script, the MCP server and the owner all use the
same orders. The MCP server is a thin layer on top, so this contract comes first.

## The design in brief

- **Orders never drive the character.** They change what the autopilot does next. It keeps every reflex: fights, heals, retreats, deaths.
- **Four classes of order:** Probe (read something), Control (start and stop), Policy (standing rules), Job (one piece of work).
- **Orders wait for a safe point** and age out if none comes. Nothing waits on the model.
- **Every order ends in a named result.** Nothing fails silently, and nothing is replayed after a restart.
- **A person always wins.** The kill switch and a typed `/stop` can't be overridden by any order.
- **Standing rules live in one overlay file** and reach the autopilot through only two seams, so the existing code stays nearly untouched.

### What changed from the plan after reading the code

| Plan said | Contract says | Why |
|---|---|---|
| Every order is validated in the server | Two layers. The server checks role and budget, the client has the final word on "legal now" | State moves between a check and a safe point, and only the client can see it |
| Add a new fight stop reason and arm per order | **No new fight stop reasons.** Reuse `ForcedTrip` and `MoveOnReached`, and re-seat at a lull | A reason with no arm stops the run with `failed:fight:unrouted-reason` |
| Re-seat through a `rung-moved`-style stop | A new `Reseat` built on the hand-back path, for same-ground changes | A stop with `ok=yes` marks the checkpoint `Completed`, which resets cycles and totals. A hand-back carries them |
| Quest-then-resume under `--autopilot-start` | **Keep `--autopilot-start`**, and let the desk claim the end of a run it started | An unattended run logs off and quits at `task-done:*`, so the first quest would end the session. Dropping the option would lose the soak's proven fault exit and stop file |
| No order to pick a hunting ground | New `use_ground`, limited to a rung's `alternates` | The owner asked for a way to choose the ground. The ladder already names alternates, but nothing at run time uses them |
| Hold a rung with a separate clamp | A field on `RungAsk`, read by `RungOwner` | A repo test fails any other caller, and the rung's gates and move-on logic then follow it for free |
| Live hook in the picker for deny | A new fight option, applied at the next seat | Every policy change then uses the same route |
| Pass an altered ground to the stations | An override layer inside `GroundCatalog` and `LadderLoader` | Stations keep only the ground's key and re-read the catalog, so an altered copy would never reach the fight, death or gear code |

**Built since (2026-11-17, `m6/long-run-orders`):** the command orders. A `command` order (29th kind, Job class) carries `args.line`, any line the chat's orders take (`docs/commands.md`): `trip`, `rebuff [self|bot]`, `quest <key>`, `ladder`, `home`, `den`, `pause`, `resume`, `stop`, `status`, `help`, `queue`, `cancel`, `clear`. `trip` and `run_quest` (`args.key`) are built through the same runner. The desk takes them only with `--autopilot-steer-orders=on`, which observe-only does not refuse. Their report stays `running` while the command is queued, waiting for its safe point or acting, and ends `done`, `failed`, `refused`, `expired` or `cancelled` by name. The safe points are this section's Known state and Lull, plus the errand fight's lull for a trip. An order taken there acts through `AutopilotScenario.Orders.cs`, which adds one fight stop reason, `order`, routed in `FightEnded`. `tools\order.ps1` drops an order and prints its report.

## 1. Rules

1. **Same path as the human (R2).** Orders only reach existing dial and scenario calls, which send through `IActionApi`. No server verbs, so
   `granted=0` stays true.
2. **Server-confirmed proof (R3).** An order is `done` only on evidence from the server: the spellbook, a worn slot, or the quest station's own proof.
3. **A person wins (R6).** See section 7.
4. **Localhost only (R1).** The desk opens only when the session's server is loopback, by the same check the automation already uses. It opens 5 seconds after the character is in the world, so its first work stays out of the login.
5. **Inert when off.** With `--autopilot-steer-dir` absent there is no desk and the overlay is null. A test proves every hook falls through.
6. **No client network.** Files only. `Lifestone.Client` stays free of file I/O (`ArchitectureTests`), so `OrderDesk` and the overlay rules are pure
   code there and file work sits in the Godot host. `StopFileWatch` polls on the game thread, which is fine every 5 s. The spool does more, so it gets
   one background thread, and its writes follow `UserDataCheckpointStore`: a temp file, then a move over the old one.
7. **Steering can't take the autopilot down.** The desk has its own `try`, separate from the host's: a throw inside the host's `try` disposes the whole
   dial. A desk fault closes the desk (`desk-closed`) and the run carries on. If building a fresh scenario fails during a seat, that's a host fault, with
   the existing fault exit. If the harness dies, the character keeps playing by reflex.
8. **Text is data.** `why` is trimmed and logged, never sent to the server or shown in game chat. Orders hold no paths or commands.

## 2. Files

One run folder: `<repo>\cache\avatar\<run>\`, gitignored, because frames are retail-derived. The option is resolved by `OutputPaths.TryResolve`,
as the stop file is. Each file has one writer.

| Path | Writer | Contents |
|---|---|---|
| `inbox\<seq>-<id>.json` | harness | One order. Written as `.tmp` and renamed, so it is never seen half-written |
| `taken\` | client | Orders it has read, moved out of the inbox |
| `outbox\<id>.json` | client | The order's latest report, replaced atomically |
| `status.json` | client | The live picture, every 2 s and on any change |
| `events.ndjson` | client | Append-only event stream |
| `overlay.json` | client | The standing rules. The harness changes it only through orders |
| `sheets\<section>.json` | client | Answers to `sheet` |
| `shots\<name>.png` | client | Answers to `capture` |

The background thread polls the inbox every second and hands parsed orders to the game thread. The desk runs once a frame in
`AutopilotHost._Process`, between `TickStopFile` and `_logic.Tick`. Files over 64 KB are refused unread.

## 3. An order and its report

```json
{
  "v": 1,
  "id": "o-000142",
  "kind": "run_quest",
  "args": { "key": "tusk-male" },
  "by": "director",
  "why": "worth 2 levels at this rate",
  "issued_utc": "2026-11-07T04:12:09Z",
  "expires_s": 900,
  "guard": { "character_guid": "0x50000A12", "level_min": 22, "level_max": 31, "ground": "tusker-cave" },
  "dry_run": false
}
```

- `guard.character_guid` is required on everything except `ping`. Level and ground are optional, and a stale order is refused by name.
- `expires_s` runs from `issued_utc`, between 5 and 86,400 seconds.
- `dry_run: true` runs every check, acts on nothing and answers `checked`. Automation-vision R6 asks for this, and it lets the bake-off replay
  orders safely.
- `id` is unique in a run. A repeated id returns the earlier report and does nothing.

```json
{
  "v": 1, "id": "o-000142", "kind": "run_quest",
  "state": "done", "name": "task-done:quest:tusk-male", "detail": "the turn-in was made",
  "at_utc": "2026-11-07T04:31:40Z", "level": 24, "ground": "tusker-cave", "cycle": 3,
  "evidence": { "reply": true, "item": true }
}
```

### Lifecycle

```
received --> waiting --> running --> done
    |           |           '------> failed:<name>
    |           |---> expired
    |           |---> cancelled
    '-----------'---> refused:<name>
```

- **One Job at a time.** A second answers `refused:busy`. Policy orders don't queue behind a Job, and Control orders jump the desk.
- **The autopilot owns recovery.** If a death cuts a trip, the autopilot retries it by its own rules, and the order stays `running` until the
  trip ends or the run stops.
- **After a restart,** every unfinished order answers `refused:restarted`, and an inbox file older than the client's start gets the same.
  Nothing is replayed. The harness decides again from fresh status.
- **No sequences.** The desk holds no scripts. The harness composes orders and watches reports.

### Who checks what

| Check | MCP server | Desk |
|---|---|---|
| Schema and known kind | yes | yes, again |
| Known key (quest, spell, rung, ground) | yes, from the catalog | yes, from the embedded data |
| Role, and budgets | yes | no |
| Rate limits | yes | hard floors only (section 5) |
| Guard and expiry | yes | **final word** |
| Legal now | advice only | **final word** |

## 4. Safe points

| Point | Where | What's true there | Used by |
|---|---|---|---|
| **Frame** | `AutopilotHost._Process`, before `_logic.Tick` | Any mode | Probe, Control, writing the overlay |
| **Known state** | `KnownState`, after the `_deathPending` arm, beside `TaskArrow` | Present, audit finished, no corpse owed, no level-up spend in flight | Job, seat |
| **Lull** | `WatchFight`, a new last arm, after the `_keeperBack` return, read only when nothing else asked to stop the fight | In a fight, no hostile in reach (`HostileWatch.InReach`), no trip or death pending, no spend in flight, no pull stage running | Job, seat, rung read |
| **Dial done** | `AutopilotHostLogic.Tick`, after a run ends | Mode `Done`, nothing running | Starting the next run |

**Never cut:** a trip, a death, travel, portal space, a reconnect, audit stations, the final recall, or a run already `Stopping`. An order
just waits and may expire. A keeper's fight-back and a trophy pull count as "never cut" too: the pull waits up to 420 s with nothing in reach, and cutting it wastes one of its two tries. Every existing `WatchFight` trigger ranks above the new arm, including stop rules, retreat, the counters and the health floor, so
an order can't hold any of them back.

The Known state arm must copy `TaskArrow`'s own guards. Death is only found below that point, so it stands aside for a dead body, for a place that's neither home
nor the ground, and for an attacker on the ground.

The new `WatchFight` arm does one of three things, and none needs a new fight stop reason:

- Asks for `ForcedTrip`, whose `FightEnded` arm already runs the trip. Its log text must not contain "forced trip", or the dial marks the once-a-run option as used.
- Asks for `MoveOnReached`, which ends the cycle so its check re-reads the rung.
- Sets a flag that a seat is ready. `Reseat` can't run inside `Step`, so the dial acts on the flag straight after `Step` returns, in the same frame.

## 5. The two ways a run changes

**Seat.** A fresh scenario on the same ground, with the current overlay. It's needed because `AutopilotKit.Create` builds the parts from the
arguments and the ground, and `AutopilotOptions.Task` is fixed for a scenario's life. A new `AutopilotHostLogic.Reseat(task, why)` works like
`/stop` then `/auto` in one frame, and:

- never counts as a person: no `interrupted=`, and no key unlock, because the mode stays Running and the keys follow the mode;
- carries the cycle, totals, seconds and task (`AutopilotCarry`), so the checkpoint isn't marked `Completed`;
- runs the start audit, as every start does;
- happens only at a Known state or Lull, and never with a trip, a death or a level-up spend in flight. A spend cut by a seat would be forgotten, because `_ladderSpendOwed` isn't carried.

`HandBack` refuses a Running dial, so its build step has to be factored out for `Reseat` to share.

**A seat must not look like a new run.** `AutopilotSeed` has to grow, or a seat does damage:

| Carried | Without it |
|---|---|
| The rung was already checked | A fresh scenario's first check treats a due move-on as `failed:rung-ground-mismatch` and ends the run. Carried, it takes the normal `rung-moved` route instead |
| Level already spent | Each seat spends again, and a refusal at a cycle start stops the run, unlike a refusal mid-fight |
| Trip loop guard, failed quests, buy-trip tried | The guards that stop retry loops are wiped, so a seat could restart a loop they had ended |
| Retreats on this ground (`_retreatsHere`), watchdog recoveries | `retreat:again:<ground>` and the recovery limit stop firing across seats, though the cycle number is carried |
| The book the last whole pass stood on (`_passBook`) | The Known state runs a whole start pass again, or walks home for one: 18 lines and about 7 minutes. Seats could then keep the character from fighting |

Several Policy orders coalesce into one seat. Hard floor: one seat a minute (`seat-rate`). That number is a first guess, to be set in the slice.

**Rung move.** Changing ground reloads parts, so it uses the existing, proven route: `rung-moved:<rung>`, then `MoveOnRestart`, then `MoveOnNext`.
The plan's Risk 7 applies: while parts load, nothing heals or retreats. So rung orders need a Lull, and the existing refusal stops a hold that
leaves no targets.

Dial totals restart whenever a run ends with `ok=yes`, including at every rung move and every finished quest. **The harness keeps lifetime totals itself** from
cycle and `[ladder]` lines, and never treats `cycle` as a lifetime count.

### Where standing rules take effect

| Seam | Code | Rules |
|---|---|---|
| **Build** | At each seat, `AutopilotKit.Create` takes effective arguments, and `GroundCatalog` serves the effective ground for its key | deny, learn, stock, opener, tuning, protected items |
| **Rung** | `RungAsk` gains `Hold`, and `LadderLoader.WarMage` serves the effective ladder | hold, ground choice |

- **Why inside the catalogs.** Stations keep only `ground.Key`, and the fight, death, gear and buff code re-read `GroundCatalog`. Learn plans and the logistics counters
  read `LadderLoader.WarMage` directly, and `ScrivenerLearnPlan.RungOf` looks a rung up by ground key. An override layer at those two reads reaches all of them. Without it, a
  `use_ground` alternate would silently lose its learn trigger and scroll keeping. Both layers are empty when the desk is off, and tests must reset them.
- **Kit refresh.** `AutopilotKit` holds its ground and arguments in read-only fields, so these must become re-readable at each `Create`.
- **Three rung readers.** `AskRung`, the host's static `Asked` and `DeathHandler`'s corpse gate. The first two must read the hold. A corpse run ignores it: the corpse is where it is.
- **Override layers and tests.** `Lifestone.Client.Tests` runs in parallel, so a static layer needs `AsyncLocal` or a non-parallel collection, or "tests must reset them" races every reader of `LadderLoader.WarMage`. A copy of a `Ladder` must keep every field: its constructor's optional `StatCap`, `BigPack` and `CorpseCoin` would otherwise be dropped silently.
- **`raise_plan` fits neither seam.** `XpSpendRule` reads the static `MageTemplate.SharesAt`, from two builders: the trip and the level-up spend. It needs a parameter through both.

**Trap:** `AutopilotOptions` copies six fight options when the host installs (`EmergencyFloorPercent`, `EmergencySafePercent`, `EmergencyMarginPercent`, `LootOn`,
`HealDamageWeight`, `DrainUnderPercent`) and one death option (`AggroReachMetres`), while stations re-parse `_arguments`. Tuning must re-run `AutopilotOptions.FromArguments`
over the effective arguments, keeping `Job`, `Task` and `Buff`, or the fight and the vitals guard disagree.

## 6. The orders

Twenty-eight kinds. The MCP server exposes them through one `order` tool, so the tool list stays short.

### Probe: no effect on the game, answered in a frame

| Kind | Args | Answer | Slice |
|---|---|---|---|
| `ping` | none | Report with the desk's state. Measures latency | 1 |
| `sheet` | `section`: skills, attributes, spells, buffs, inventory, equipped, quests | `sheets\<section>.json` | 1 |
| `capture` | `name` (a token) | `shots\<name>.png`, and its main-thread cost in the report | 2 |

### Control: acts this frame

| Kind | Legal when | Calls | Done when | Slice |
|---|---|---|---|---|
| `start` | Off or Done | The host's press with `startedBy` set, so the run stays unattended | Mode is Running | 3 |
| `resume` | Interrupted by an order, never by a person | `HandBack` | Mode is Running | 3 |
| `stop_home` | Running | `_logic.Stop("steer-<id>")`, a clean stop that recalls home. The game stays open | `request:steer-<id>` | 3 |
| `stop_here` | Running or Stopping | `StopHere`. No recall, so the character stands where it is | Mode is Interrupted | 3 |
| `leave` | Running, Off or Done | Running: the stop file's path, which recalls home, logs off and quits. Off or Done: a new dial call that sets the same end, logging off where the character stands | Client gone. Used to yield to gates | 3 |
| `cancel` | Target is `waiting` | Takes it off the desk | Target is `cancelled`. Too late answers `too-late` | 3 |

**Launch with `--autopilot-start`.** That keeps the soak's tested ends: a run that fails logs off and quits with exit code 1, the supervisor restarts it, and the
checkpoint resumes it. A failed run isn't `Completed`. The one change is a new `AfterRun` hook, called in `Tick` before `DecideUnattendedEnd`. When a run the desk started
ends, whether `task-done:*` or `failed:*`, the desk claims the end: it reports the order, calls `UseTask(Job)` and starts the next run. `UseTask(Job)` is needed because
the chosen task sticks, and a later start would rerun the quest. Every other end behaves exactly as it does today.

Proposed: the director doesn't get `stop_here`, because it can strand a character beside a creature. Only `owner` and `drill` use it. That
rule would live in the MCP server's roles.

### Policy: standing rules, stored at once, in force at the next seat

`done` means stored. `status.dial.seated_overlay` shows when it's in force.

| Kind | Args | Effect | Refusals | Slice |
|---|---|---|---|---|
| `hold_rung` | `rung` | Rung reads stop at that rung's top level. A hold also suppresses a due trophy job (section 14) | `rung-above-level`, `rung-quest`, `rung-pending`, `rung-unfightable` | 5 |
| `release_rung` | none | Ends the hold | | 5 |
| `use_ground` | `rung`, `ground` | Picks one of the rung's `alternates` | `not-an-alternate`, `alternate-pending`, `no-route`, `rung-unfightable` | 5 |
| `release_ground` | `rung` | Back to the rung's own ground | | 5 |
| `deny_spell` | `spell` | The picker stops offering it | `bolt-undeniable`, `unknown-spell` | 6 |
| `allow_spell` | `spell` | Removes a denial | | 6 |
| `learn_spell` | `spell` | Added to the learn list, bought on a later trip | `unknown-spell`, `no-scroll-vendor`, `known` | 6 |
| `set_stock` | `wcid`, `keep`, `trip_under`, `stop_under` | Adds or changes a stock line | `unknown-item`, `bad-levels` | 6 |
| `set_opener` | `target`, `line` | Sets a target's opener | `unknown-target`, `unknown-spell` | 6 |
| `raise_plan` | `shares`, `credit_order` | Changes the XP spend | `bad-shares` | 6 |
| `set_tuning` | `key`, `value` | One allowed option, inside its own bounds | `not-tunable`, `out-of-bounds` | 6 |
| `protect_item` | `guid` | Never sold | `not-held` | 7 |

- `hold_rung` only holds down. `RungOwner.Decide` already refuses a rung above the character's level, "whoever named it", and gates still apply.
- The ladder's only live alternates today are `tusker-cavern` (cave), `tusker-habitat` (abode), `tusker-temple-fort-tethana` and `tusker-barracks` (quarters), and
  `tusker-temple-fort-tethana`, `black-spawn-den-aluvian` and `tusker-quarters` (holding). `hills-citadel` is pending. All six are named in `travel-routes.json`, but no
  run has been there on one of these rungs.
- `set_tuning` candidates: `--fight-opener-tries`, the `--fight-loot*` family, `--fight-drain-under`, `--fight-drain-until`, `--fight-stamina-full`,
  `--fight-heal-damage-weight` and `--fight-emergency-*`. Run-shape options such as `--fight-seconds` and `--fight-ground` stay out, and so does
  `--fight-max-level`, because the rung sets its own cap.
- There's no rotation in the autopilot, so "add a spell" is three orders: `learn_spell`, `set_stock`, and `allow_spell` if it was denied. The
  picker may still not choose it, and no order can force a cast. The MCP server can wrap that as one `add_spell` call.

### Job: one piece of work, one at a time

| Kind | Args | How it runs | Done when | Refusals | Slice |
|---|---|---|---|---|---|
| `trip` | none | `ForcedTrip` at a Lull, or begun at a Known state | The trip's station ended well. A given-up trip never raises the count, so the order must watch `TripGivenUp` too and answer `failed:trip-given-up` | `no-lull`, `busy` | 4 |
| `errand` | none | Runs as `trip`, with no seat, so the task stays Job and keeps the keeper rule and the mana way out | As `trip` | `no-lull`, `busy` | 4 |
| `run_quest` | `key` | Seat with the quest task, only at a Known state at home, then back to the job | `task-done:quest:<key>`. The station has already proved it by the NPC's reply and the item | `ladder-quest`, `no-level`, `level-out-of-band`, `done-once`, `not-due:<utc>`, `unknown-quest`, `gate-shut:<rung>`, `busy` | 4 |
| `buy_item` | `wcid`, `count` | Added to the next trip's list | Item is in the pack | `no-home-vendor`, `no-coin` at the buy | 7 |
| `equip` | `guid`, `slot` | Station at home, in peace | Slot holds it | `not-held`, `cant-wield` | 7 |
| `save_set` | `name` | Stores the worn pieces' guids in the overlay | Stored | `not-all-worn` | 7 |
| `wear_set` | `name` | Station using `Wield` and `MoveToPack` | Every piece worn | `piece-missing` | 7 |

- `run_quest`'s refusals are `JobMenu.Judge`'s own names. The desk also refuses `not-due`, which `UseTask` lets through, so an order can't cause a
  pointless seat, and `gate-shut:<rung>`, because the quest station reads neither the ladder nor `RungOwner` and `Judge` reads only the quest's own level. Without it, `tusk-redeemer` at level 20 walks to the Abode. The desk asks `RungOwner.CorpseRun` about the quest's hunting ground, and must cover alternates, for which that call answers `no-rung`.
- A one-shot quest task has the keeper rule and the mana way out off, by design: its stations fight back with their own legs. Starting only from home limits the exposure to what a person's menu choice has today.
- Only `pathwarden` keeps a server stamp, and the ladder runs that one. So `quest has` can't prove any quest this order can run.
- Slice 7 is a sketch and needs its own design pass. `SelfEquipJudge` swaps gear by its own judgement, so `equip` must pin pieces or the next trip undoes
  it. `SaleMemory.KeptAside` and `PackMemory.Rotation` live in process memory, so `protect_item` must seed them from the overlay at every seat, or a
  restart sells the spare set. Off-home purchases aren't in v1. `BuyTripStation` exists, but only for scarabs.

### Worked examples

| The request | Orders |
|---|---|
| Buy this spell and add it to the rotation | `learn_spell`, `set_stock`, `allow_spell` |
| Buy and equip this armor | `buy_item`, then `equip` |
| Run this quest now | `run_quest` |
| Switch armor sets | `wear_set` |
| Choose a dungeon | `use_ground`, with `hold_rung` to stay there |

### Loot: keep, sort, sell, store (requested 2026-11-09; proposed, needs its own design pass)

The autopilot sets good finds aside by itself, with no model involved, and the model sorts the pile later. This adds a Policy kind `loot_rule` and the kinds `mark_item` and `store_items`, beyond the 28, once it's designed.

| Stage | What it adds | Starts from |
|---|---|---|
| **Keep by rule** | Any cantrip of a chosen tier (minor, major, epic, legendary), high spell levels, and quality floors such as workmanship, war bonus, armour and damage. It lives in the autopilot, so it works with the harness off | `KeepAsideRule` keeps only cantrips a ground names and a war bonus, and only casters, wearables and jewellery are appraised at the corpse. Weapons would need adding, and each appraisal costs time at the corpse |
| **Ledger** | Every kept item on disk, with the facts read when it was kept: name, type, spells, workmanship, material, value, burden, wield needs, why it was kept, where and when it was found, and its mark. `sheet loot` returns it | The facts must be saved at keep time. Appraisals sit in a bounded cache and a restart empties them |
| **Marks** | `mark_item`: `guid` (or several) and one of `sell`, `keep`, `store`. It replaces `protect_item`. The MCP server offers a filtered, sorted table, so the model reads a short list rather than the whole pile | The overlay. The kept list is process memory, and after a restart the sale re-derives it from the rule, where an appraisal is held. A hand-made mark has no rule behind it, so it needs the disk |
| **Selling** | Items marked `sell` go in the next trip's sale, under the existing sale rules | `SellRules`, `ResupplyStation.IsKeptAside` |
| **Pack pressure** | A limit and an overflow rule, so kept items can't crowd out the loot or the load line. What was left behind is named | `MageLootPlan`, `PackLayout`, the ground's `MinFreeSlots` |
| **Storage** | Buy or rent a house, travel to it, and move `store` items into its chests | Nothing in the client: M5's list has housing "not built, later". ACE has it, with purchase checks that apply by default: `house_15day_account` (refuses an account under 15 days old), rent, and level and allegiance needs by house type. Whether this server changes those isn't checked |

Until housing exists, `store` is recorded and acts as `keep`, so nothing is lost and no decision has to be made twice.

## 7. What no order can do

- Override the kill switch or a person's `/stop`. In `Halted`, or in `Interrupted` by a person, every order that would move the character answers
  `kill-switch` or `person-stopped`. Probes still work. This also covers `stop_home`, because `Stop` in `Interrupted` would hand the controls back.
  - **New person hold.** A `/stop` typed while the dial isn't running records nothing today, so a later `start` would override the person. The dial must record a hold on
    any typed `/stop`, the panel button or the menu's stop row, in any mode. Only a person's own `/auto` clears it.
  - **The stop file** keeps its behaviour while a run is going. It ignores Off and Done today, so with the desk open it counts as `leave` in those modes.
- Force a cast, or deny the bolt.
- Change element. The fight casts only `ArenaProfile.WarLineName`, so a non-fire rung stops with `failed:rung-element-line`. Answer: `element-locked`.
- Send the character above its level's rung, or past a shut gate. `hold_rung` and `use_ground` get this from `RungOwner`. `run_quest` gets it only from the desk's own `gate-shut` check above.
- Run the ladder's own quests. Those are the rung's (`ladder-quest`).
- Use a server verb, touch the database, or log in. Re-creating the character is the supervisor's work, with no client running: `--scenario=climber-create
  --fresh-character`, then `--scenario=journey --journey=climber`, then the play host. Today those steps can only make Winnie on `test` (section 10).

## 8. Status and events

`status.json`:

```json
{
  "v": 1, "at_utc": "...", "seq": 8123, "run": "...", "build": "...",
  "character": { "name": "Qwinnie", "guid": "0x50000A12" },
  "session": { "phase": "InWorld" },
  "dial": { "mode": "running", "state": "fight:den-fight", "decision": "...", "next_trigger": "...",
            "cycle": 3, "job": "level up (rung cave)", "seated_overlay": 7 },
  "place": { "class": "ground", "cell": "0x5C4300F1" },
  "level": 24, "xp": { "total": 0, "unassigned": 0, "to_next": 0 }, "vitae": 1.0,
  "vitals": { "health": [0, 0], "stamina": [0, 0], "mana": [0, 0] },
  "rung": { "key": "cave", "ground": "tusker-cave", "reason": "band", "held": false },
  "stock": [ { "name": "...", "held": 0, "trip_under": 0, "stop_under": 0 } ],
  "burden": 0.0, "free_slots": 0, "pyreals": 0,
  "buffs": { "up": 0, "due": 0 },
  "overlay": { "version": 7 },
  "desk": { "open": true, "waiting": [], "running": null }
}
```

`dial` comes straight from the existing `AutopilotStatus`. Reading the rest must not allocate on most frames, because it's written from the game
thread's data on a timer.

`events.ndjson` has one JSON object a line, stamped by the client with time, level, ground and cycle. Most come from seams that already exist (`StateChanged`,
`ChatNotice`, `Stopped`, `Lines`):

`state`, `stop`, `level`, `death`, `retreat`, `trip`, `spend`, `learn`, `ladder`, `cycle`, `notice`, `gate`, `seat`, `reconnect`, `order`, `fault`.

`cycle` carries the dial's cycle and done lines, and `notice` its chat lines. Slice 1 emits all of these except `gate` (no seam yet) and `seat` (slice 3).

## 9. The overlay

```json
{
  "v": 1, "version": 7, "character_guid": "0x50000A12",
  "hold_rung": "cave", "grounds": { "quarters": "tusker-barracks" },
  "deny": [ "..." ], "learn": [ 0 ], "stock": [ { "wcid": 0, "keep": 0, "trip_under": 0, "stop_under": 0 } ],
  "openers": { "0": "..." }, "raise": null, "tuning": { "--fight-opener-tries": "2" },
  "protected": [ 0 ], "sets": { "name": [ 0 ] }, "buy": [ { "wcid": 0, "count": 1 } ]
}
```

- Written atomically by the client, and `version` rises on every change. Inside the client it's an immutable snapshot.
- Tied to the character's guid. A re-created character, an unreadable file or a newer schema sets the file aside and starts empty, as the checkpoint does.
- It survives client restarts, so the character's standing rules survive a crash.

## 10. Identity: needed before any of this runs on Qwinnie

The climber is a hard-coded name: `MageTemplate.ClimberName` and `AutopilotOptions.LadderCharacter` are both `"Winnie"`. It's used in two ways, and each needs its own fix.

**As a gate: does this character get climber behaviour?** These become one predicate, `MageTemplate.IsClimber(name)`, with a repo test in the style of
`RungOwnerRepositoryTests` so no name check creeps back:

| Area | Where |
|---|---|
| **Cheat-free guards** | `PlayerTools` (never granted), `JourneyScenario` (never placed in recovery) |
| Start and audit | `AutopilotHostLogic.RequiredCharacter`, `StartupAudit` |
| Buffs | `BuffCycle`, `BuffProfiles.For`, `RebuffHostLogic` |
| Supplies and spending | `LogisticsCounters`, `ResupplyStation.Learn`, `ResupplyStation.ScrollKit` (`m6/8`), `XpSpendRule`, `AutopilotScenario`'s floor-only lines, `ManaOrder.PicksByNet` |
| Death | `DeathHandler`'s corpse gate: a miss means no ladder, so she walks back behind a shut gate |
| Travel and stations | `JourneyTravel`, `RecallHome`, `JourneyStations`, `GearScenario` |

If a guard is missed, Qwinnie fails quietly, and a missed cheat-free guard would void `granted=0`, so this list gets a test rather than a promise.

**As a value: which character is this?** `ClimberCreateScenario` creates and purges that name, `JourneyPlans.Climber` fixes the name and the account `test`, `TestAccounts`
lists it, `HeadlessOptions` refuses `--character` for climber-create, and the server mod's `CharPurgeRules` (with the stand-in's copy) lists `Winnie` as a re-created name, so without a row the new copy can't purge its old self and section 7's re-create fails. These need the identity passed in, so `climber-create` and the climber journey can make Qwinnie on her own account.

Stock lists built for account `test` must also be derived per account, because the component formula hashes the account name. The new account touches `TestAccounts.cs`,
`Play.psm1` and CLAUDE.md's accounts rule, and `-AddAccount` needs the owner's go-ahead.

**Built so far.** One predicate, `MageTemplate.IsClimber`, with a repository test that fails any other comparison to the climber name. `--autopilot-climber=NAME` picks the climber for the level up job, `--journey=qwinnie` is her journey, `--scenario=climber-create --account=qwinnie` creates her, and the accounts rule, `TestAccounts`, `Play.psm1`, `climb.ps1 -Climber` and the purge lists know her. Not built: per-account stock lists. The profiles' lists are sized on the account `test`'s formula, so components for schools she has no focus for may be wrong until they are derived for her account. The foci schools (War, Life) don't depend on it.

## 11. Testing without the model

| Level | What it proves |
|---|---|
| `OrderDesk` unit tests | Every order, in every mode, on an injected clock: legal, refused by name, expired, duplicate, restart |
| Dial tests over scripted stations | `Reseat` carries totals and guards, never counts as a person, never unlocks keys. A seat with a move-on due takes `rung-moved`, not a failure. A desk-claimed end doesn't quit, and every other end still does. A `/stop` typed while off blocks `start` |
| Spool tests | Torn file, oversized file, bad UTF-8, duplicate id, out-of-order files, read-only folder |
| Inert test | With the option absent, behaviour and logs are unchanged |
| Fault tests | A throw in the desk leaves the run going. A failed build during a seat takes the fault exit |
| Repo tests | No client file I/O, one rung owner, one climber predicate |
| Live drill by plain script | Every order on Qwinnie, effect read from the server, modelled on `ControlDrill` |

A plain script (`avatar\steer.py`) writes orders and reads reports. If it can run the whole drill, the MCP server can only add rules on top. It uses no second
slot, so none of this costs the queue anything.

## 12. Slices

| Slice | Delivers | Size |
|---|---|---|
| 1 | Option, spool, `status.json`, events, `ping`, `sheet` | S |
| 2 | `capture` | S |
| 3 | Desk, overlay store, Control orders, `Reseat` and the larger seed, `AfterRun`, person hold, safe points | M |
| 4 | `trip`, `errand`, `run_quest` | M |
| 5 | Rung seam: `hold_rung`, `use_ground`, the `LadderLoader` layer | M |
| 6 | Build seam: deny, learn, stock, opener, tuning, the `GroundCatalog` layer. `raise_plan` with its own parameter | M |
| 7 | Gear: buy, equip, sets, protection. Needs its own design pass first | L |
| 8 | Loot: wider keep rule, ledger, marks, selling what's marked, pack pressure. Needs its own design pass first | L |
| 9 | Storage: housing and chests. Client work the size of a milestone item, with its own design | L |

**Slice 3 as built.** The Control orders act at the Frame point; one that hasn't ended yet reports `running` and is replaced by its end. Added refusal names: `wrong-mode`, `kill-switch`, `person-stopped`, `too-late`, `unknown-order`, `leaving` (the game is already on its way out). A person's stop also withdraws an order's press still waiting for parts and ends a `leave` in progress. A `start` the host refuses is answered and never quits the game. A stop that was asked now beats a rung move-on at a cycle's start, on every run, with or without the desk. Failure names on a report's `failed` state: `not-started`, and the stop or mode that came instead. `AutopilotHostLogic.AskSeat` makes a re-seat, and no order calls it until slices 4 to 6. `AfterRun` is a hook with no claimant until slice 4. `status.dial` also carries `person_hold` and `kill_switch`.

**Slice 1 as built** (`m6/avatar`, cut from `master` at `5966212e`). Refusal names beyond the kind-specific ones: `bad-file-name`, `too-large`, `restarted`, `unreadable`, `bad-encoding`, `bad-json`, `bad-version`, `bad-id`, `unknown-kind`, `bad-by`, `bad-args`, `bad-issued`, `bad-expiry`, `bad-dry-run`, `bad-guard`, `id-mismatch`, `guard-character`, `guard-level`, `guard-ground`, `not-built`, `unknown-section`, `not-in-world`. A file with no usable name gets an event and no report, because a report needs an id. An answered file stays in the inbox until its report is written, so a crash between the two leaves a file the next client refuses as `restarted`. `rung.reason` and `dial.seated_overlay` are null, and `overlay.version` is 0, until their slices.

Identity (section 10) isn't a slice of this system, but nothing runs on Qwinnie until it's done. Slices 1, 3 and 4 already give a steerable character. Each slice goes through
the project's slice process on `m6/avatar`, re-running `check_citations.py` first.

## 13. Not known yet

- **Nothing has run.** This comes from reading code, not from testing it, and one reviewer's read isn't a test.
- **The cost of a seat** in a live fight is unmeasured: the audit's length, and what a dropped fight loses. Loot waiting at a lull is likely lost, because a
  hand-back's checkpoint read comes back empty.
- **`use_ground` in play.** Whether each alternate is fightable at that rung, and whether the override layers reach every reader. Two were found by review, so there may be more.
- **Screenshot cost.** Today's capture reads back on the game thread. Slice 2 reports the real number.
- **Measured so far:** the desk costs 5 to 17 ms on the frame it opens and about 4 ms at worst after that, and the worst whole frame in the 3 seconds after it opens was 6 to 10 ms. A report takes about a second, which is the poll. Offline runs fail frame-time checks at about the same rate with it or without it: six alternating runs under the same load, closed 2 of 3, open 1 of 3. That is too few to say more.
- **The numbers here are guesses:** one seat a minute, the 2-second status, the 1-second poll, the 64 KB limit.
- **The code moved after this was written.** `m6/7` ended and merged, and `m6/avatar` is cut from `master` at `5966212e`. A second review against the newer code (`a415f337`) found nine gaps, folded in above: a quest order could pass a shut gate; `errand` and `run_quest` lost the new keeper rule and mana way out; a seat wiped `_passBook`, the retreat and recovery limits, and a spend in flight; the trophy job beat a hold; a pull could be cut; the identity list was short; the override layers race the parallel tests. It reads the code; none of this is tested. Sections 4 and 5 still need checking against the base again before slice 3.
- **`m6/8` is still moving** and rewrites the scroll buying rule (D116). `learn_spell` and `set_stock` must be read against that flow at slice 6, and a shopping list held only in run state would have to be carried by a seat.

## 14. Decisions (approved 2026-11-09)

1. **The director has no `stop_here`.** It gets `stop_home` only.
2. **Grounds are limited to the ladder's alternates in v1.** Other profiles can join later through `--avatar-profiles-dir`.
3. **General code, with only Qwinnie's run turning it on,** so Winnie's runs and a person's use stay possible later.

Open, decided at slice 5: **a hold against a due trophy job.** `RungOwner.Decide` overwrites the level with the job's rung, so today a due Female Tusk beats any hold. Recommended: a hold wins and suppresses the job, because the model asked for it and can release the hold.

## Appendix: code citations

Checked by `check_citations.py`. Paths are relative to `src/`, unless they start with `tests/`.

| File | Line | Text |
|---|---|---|
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 161 | var choice = RungOwner.Decide(LadderLoader.WarMage, ask); |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 324 | public string? UseTask(AutopilotTask task, string by = TaskByMenu) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 554 | (Interrupted, Resumed, Halts, _forcedTripDone |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 579 | public void Stop(string why) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 591 | case AutopilotMode.Interrupted when _session is { } session: |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 616 | public bool HandBack(IGameSessionView session, string? stopWhy = null) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 659 | var carry = new AutopilotCarry(old.Cycles |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 687 | public string StopHere(IGameSessionView session, string via = "/stop") |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 776 | public void Tick(IGameSessionView session, double now) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 824 | if (IsMoveOn(_options, scenario.StopName, scenario.Ok, MoveOnStop) && MoveOnRestart(session, scenario)) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 911 | private void DecideUnattendedEnd(AutopilotScenario scenario) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 925 | var taskDone = AutopilotScenario.IsTaskDone(stop); |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 848 | private ScenarioStatus KnownState(IGameSessionView session) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 888 | if (_deathPending) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 893 | if (_options.Task.Kind != AutopilotTaskKind.Job && TaskArrow(session) is { } task) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2080 | private void WatchFight(IGameSessionView session) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2145 | else if (TaskFightBackOver()) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2157 | else if (LadderMoveOnReached(session)) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2536 | if (reason == FightStopReason.ForcedTrip) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2582 | if (reason == MoveOnReached) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2600 | failed:fight:unrouted-reason: |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 3480 | Completed = StopName is not null && Ok, |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 329 | private readonly TripLoopGuard _tripLoop = new(); |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 645 | public AutopilotSeed Seed => new( |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 2719 | private ScenarioStatus TripGivenUp(string? why) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 319 | var first = !_ladderChecked; |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 600 | private ScenarioStatus? LadderSpend(IGameSessionView session, int level) |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 1107 | private readonly Dictionary<string, string> _questFailed |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Jobs.cs | 85 | private ScenarioStatus? TaskArrow(IGameSessionView session) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 691 | if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) \|\| _scenario is not { } scenario) |
| Lifestone.Client/Session/Autopilot/AutopilotHostLogic.cs | 1010 | t.Why.Contains("forced trip", StringComparison.Ordinal) |
| Lifestone.Client/Session/Scenarios/FightOptions.cs | 46 | public HuntingGround GroundProfile => GroundForTest ?? GroundCatalog.Get(Ground); |
| Lifestone.Client/Session/Scenarios/Journey/JourneyStations.cs | 640 | _fight = _fight with { Ground = ground.Key }; |
| Lifestone.Client/Session/Scenarios/Journey/JourneyStations.cs | 104 | plan.Character == Mage.MageTemplate.ClimberName |
| Lifestone.Client/Session/Scenarios/Journey/JourneyPlans.cs | 414 | MageTemplate.ClimberName, |
| Lifestone.Client/Session/Scenarios/Journey/JourneyScenario.cs | 887 | if (string.Equals(_plan.Character, MageTemplate.ClimberName, StringComparison.Ordinal)) |
| Lifestone.Client/Session/Scenarios/Journey/JourneyTravel.cs | 322 | if (string.Equals(_character, MageTemplate.ClimberName, StringComparison.Ordinal)) |
| Lifestone.Client/Session/Scenarios/Journey/QuestStation.cs | 444 | QuestDone = replyOk && itemOk && stampOk |
| Lifestone.Client/Session/Scenarios/Journey/ResupplyStation.Learn.cs | 157 | MageTemplate.ClimberName |
| Lifestone.Client/Session/Scenarios/ClimberCreateScenario.cs | 57 | _purge = new FreshPurge(MageTemplate.ClimberName, log, LogCategory); |
| Lifestone.Client/Session/Scenarios/GearScenario.cs | 158 | MageTemplate.CharacterName |
| Lifestone.Client/Session/Fighter/RecallHome.cs | 168 | MageTemplate.ClimberNa |
| Lifestone.Client/Session/Mage/LogisticsCounters.cs | 283 | _ladder = ground.Scriveners.Count > 0 && ladder.Rungs.Any( |
| Lifestone.Client/Session/Mage/ScrollKeepRule.cs | 228 | ScrivenerLearnPlan.RungOf(ladder, groundKey, q.Level) |
| Lifestone.Client/Session/Mage/XpSpendRule.cs | 290 | MageTemplate.SharesAt(q.Level), |
| Lifestone.Client/Session/Mage/BuffCycle.cs | 508 | MageTemplate.ClimberName |
| Lifestone.Client/Session/Mage/RebuffHostLogic.cs | 31 | MageTemplate.ClimberName |
| Lifestone.Client/Session/Autopilot/StartupAudit.cs | 522 | MageTemplate.ClimberName |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 139 | internal static LadderDecision DecideRung( |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 329 | failed:rung-ground-mismatch: |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 464 | failed:rung-element-line: |
| Lifestone.Client/Session/Autopilot/RungOwner.cs | 20 | public sealed record RungAsk( |
| Lifestone.Client/Session/Autopilot/RungOwner.cs | 107 | A rung above her level is no rung she can be sent to, whoever named it. |
| Lifestone.Client/Session/Autopilot/AutopilotJobs.cs | 86 | internal static (string? Refusal, long? Xp) Judge( |
| Lifestone.Client/Session/Autopilot/AutopilotOptions.cs | 19 | public const string LadderCharacter = "Winnie"; |
| Lifestone.Client/Session/Autopilot/AutopilotOptions.cs | 148 | EmergencyFloorPercent = fight.EmergencyFloorPercent, |
| Lifestone.Client/Session/Autopilot/HostileWatch.cs | 64 | public bool InReach(double now, out string what) |
| Lifestone.Client/Session/Autopilot/StopFileWatch.cs | 7 | public sealed class StopFileWatch |
| Lifestone.Client/Session/Autopilot/ControlDrill.cs | 13 | public interface IControlDrillHands |
| Lifestone.Client/Session/Contracts/AutopilotStatus.cs | 34 | public readonly record struct AutopilotStatus( |
| Lifestone.Client/Session/Contracts/PlayerTools.cs | 551 | _api.AllowClimberGrant |
| Lifestone.Client/Session/Mage/MageTemplate.cs | 474 | public const string ClimberName = "Winnie"; |
| Lifestone.Client/Session/Mage/FightPicker.cs | 443 | public FightPick Decide(in FightSnapshot s, double clock) |
| Lifestone.Client/Session/Mage/SelfEquip.cs | 164 | public sealed class SelfEquipJudge |
| Lifestone.Client/Session/Mage/PackLayout.cs | 538 | public HashSet<uint> Rotation { get; } = []; |
| Lifestone.Client/Session/Scenarios/Journey/ResupplyStation.cs | 42 | public HashSet<uint> KeptAside { get; } = []; |
| Lifestone.Client/Session/Grounds/Ladder.cs | 117 | IReadOnlyList<LadderAlternate> Alternates, |
| Lifestone.Client/Session/Grounds/HuntingGround.cs | 211 | public GroundSupplies Supplies { get; init; } = GroundSupplies.Empty; |
| Lifestone.Client/Session/Grounds/HuntingGround.cs | 283 | public IReadOnlyList<LearnLine> Learn { get; init; } = []; |
| Lifestone.Client/Session/Grounds/HuntingGround.cs | 703 | public sealed record StockLine( |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 106 | var options = AutopilotOptions.FromArguments(parsed) with |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 268 | TickStopFile(delta); |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 270 | _logic.Tick(session, PortalSpaceSetup.Now()); |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 370 | public string RunTask(AutopilotTask task, string by = AutopilotHostLogic.TaskByChat) |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 558 | private void UseRung(HuntingGround ground, int level) |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 589 | private MoveOnAnswer MoveOnNext(IGameSessionView session) |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 235 | if (_guard.Tripped) |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 283 | _logic.Dispose(); |
| Lifestone.Game/scripts/Session/AutopilotHost.cs | 824 | JourneyLegRunner.BuildParts(_plan, game, _loads.Spells, _arguments |
| Lifestone.Game/scripts/Session/AutopilotHost.Unattended.cs | 86 | if (_logic.Mode is AutopilotMode.Running or AutopilotMode.Interrupted or AutopilotMode.Halted) |
| Lifestone.Game/scripts/Session/AutopilotHost.Unattended.cs | 66 | OutputPaths.TryResolve(path, |
| Lifestone.Game/scripts/Session/AutopilotHost.Unattended.cs | 99 | var exitCode = _faultEnd ? FaultExitCode : _logic.UnattendedEnd?.ExitCode; |
| tests/Lifestone.Client.Tests/Repository/ArchitectureTests.cs | 200 | "System.IO.File", "System.IO.Directory" |
| tests/Lifestone.Client.Tests/Session/Autopilot/RungOwnerRepositoryTests.cs | 11 | public sealed partial class RungOwnerRepositoryTests |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Retreat.cs | 165 | private bool WayOutOn() |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.KeeperFight.cs | 52 | private bool KeeperGivesWay |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.cs | 307 | private int? _passBook; |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 729 | private bool _ladderSpendOwed; |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Ladder.cs | 790 | private void LadderSpendCut() |
| Lifestone.Client/Session/Autopilot/AutopilotScenario.Retreat.cs | 58 | private int _retreatsHere; |
| Lifestone.Client/Session/Autopilot/AutopilotWatchdogs.cs | 25 | private int _recoveries; |
| Lifestone.Client/Session/Autopilot/RungOwner.cs | 110 | var job = named is null ? TrophyJob(ladder, ask) : null; |
| Lifestone.Client/Session/Mage/DeathHandler.cs | 1541 | var gate = RungOwner.CorpseRun(ladder, new RungAsk( |
| Lifestone.Client/Session/Vitals/ManaOrder.cs | 378 | public static bool PicksByNet |
| Lifestone.Client/Session/Scenarios/FightScenario.Pull.cs | 136 | memory.NoteTry(stand.Quest); |
