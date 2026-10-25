# M5 plan: the first dungeon

The plan of record for [m5-first-dungeon.md](m5-first-dungeon.md) (the spec: goal, DoD, research). Section numbers
are stable: append, never renumber, so briefs can cite "§4.2". The process (§6) is M4's (m4-plan.md §6, M3's P1-P8
with the model mix of D18 below): one fresh lead session per item at high effort, slices under 300K of context in
their own worktrees, merges and doc passes on the cheapest model, one review per item, the live gate by the lead alone,
frames judged solo (M3 D42), a commit per slice through `tools/commit.ps1`. The rules are CLAUDE.md, "Hard rules" and
"Rules for agent sessions"; a brief cites them and restates nothing. M4's decisions are cited as "M4 D<n>"
(m4-plan.md §7); this plan's own are D1-D24 (§7).

## 0. Status

Planned 2026-10-25 on master bfe466b7 (M4 complete; the hero video merged). The owner's answers are D1-D11 and D25-D28;
the planner's defaults D12-D24 may be overruled at any checkpoint. Budget (D12): the plan is sized in shares of the
owner's weekly meter, 40% build plus about 2% planning, against a 45% stop line; the meter resets on 2026-10-26.
**Item 0 is done** (2026-10-25, merged as "Merge m5/0 (The measuring run and the two live bugs)"; §8): the den's numbers
measured, the client-side cull, the runner's one-waypoint route, Godot's errors judged 0, the soak line. What it changed
for the items after it: the arrival floor is respawn-bound (D25: the whole den and the timed return, item 2), a level-80
mage casts VI (D26), and the fight's mana order is the stamina-to-mana dance (D27, item 1). Next: item 1 (§4.2), whose
lead prompt is `cache/m5-w1-lead-prompt.md`.

(Each lead appends its item's line to §8: what changed from the table, the agent size table, the gate numbers, the live
rounds, the mod installs, the spend by the meter.)

## 1. The read map (master bfe466b7; read by section or line range, never a whole file)

| Area | Where | What a lead reads first |
|---|---|---|
| The spec and this plan | `docs/milestones/m5-first-dungeon.md` (§2 DoD, §5 research), this file (§3-§5, §7) | the item's DoD rows and research subsections; the decisions |
| The P5b design | `cache/m5-patron-design.md` (gitignored; §1 what exists, §2 the cast policy, §3 the Mana Conversion tries, §5.1 the K1-K5 slice table) | item 1's lead: §2-§3 whole |
| The audits | `cache/m5/den-loop-audit.md`, `cheat-ledger.md`, `longrun-risks.md`, `protocol-coverage.md`, `ui-coverage.md` (gitignored; the spec's §5 carries their facts) | the item's note, by its section headings |
| The session layer | `docs/session.md` §3 (clocks), §5-§7 (events, the read model, actions and outcomes), §9 (the player tools), §14 (recordings), §18 (the live session and `Reconnect`), §29-§32 (the mage, the buff leg, the fight loop, the death loop), §40-§42 (the wayfinder's keys, the vitals keeper, journeys and stations, fresh characters) | by the item: §31-§32 for items 1-4; §41 for items 3-4 |
| The casts and their outcomes | `src/Lifestone.Client/Session/Actions/Trackers/MagicTrackers.cs` (13-46 the kinds, 123-129 the stance guard, 146-191 the confirmations, 280-335 the refusals), `Session/Contracts/IActionApi.cs` (128-165 `Cast`, 427-437 the kill switch and dry run), `Session/Actions/ActionApi.cs` (808-811, 948-966) | item 1 |
| The callers of `Cast` | `Session/Mage/BuffCycle.cs` (124, 136-142, 1138, 1191-1198, 1263, 1291-1318), `Mage/RebuffDriver.cs` (31-49, 122, 188, 357-366, 426, 575), `Vitals/VitalsKeeper.cs` (39-43, 83-107, 412-452, 652, 691, 1003-1013), `Vitals/HarmHealScript.cs` (39, 74, 605, 641, 800, 880-883), `Scenarios/FightScenario.cs` (52-54, 898, 938-953, 1127-1148), `Fighter/RecallHome.cs` (65-66, 316, 332-341), `Scenarios/Journey/TravelStations.cs` (40-41, 125-142, 466-675) | item 1 (every `ActionSource.Host` there becomes `Automation`, D13) |
| The fight loop | `Session/Scenarios/FightScenario.cs` (the phases, 149 and 888 the killed list, 340-384 and 574-596 the casts, 786-811 the limits, 905-908 the picks, 955-1046 the level choice), `Session/Mage/BlackSpawnDen.cs` (the floor rule, `IsTarget`, `IsExcluded`), `Session/Mage/ArenaProfile.cs` (344-390 the war lines and durations), `FightWalk.cs` (115, 124), `DenPlanner`, `DenWalk`, `DenExit` | items 0, 1, 2, 4 |
| The death loop | `Session/Scenarios/DeathScenario.cs` (987 the floor abort, 1566 the vitae grant), `DeathScenario.Order.cs` (281 the regear), `DeathScenario.TopUp.cs` (58), `DeathScenario.Corpse.cs` (40 the rot time, 324) | items 3, 4 |
| The journeys | `Session/Scenarios/Journey/JourneyScenario.cs` (49-53, 296-305 known state, 472-507 the vitals pass, 535-548), `JourneyPlans.cs` (133-151 the mage plan), `JourneyStations.cs` (471 the fighter-template need), `TravelStations.cs`, `ShopStations.cs`, `JourneyTravel` | items 3, 4 |
| The hosts and their limits | `src/Lifestone.Headless/Program.cs` (36-48 recording, 65-116 and 322-334 the limits), `Session/Hosting/HeadlessRunner.cs` (24, 48-60, 326-330), `src/Lifestone.Game/scripts/Session/FightLegRunner.cs` (41-62), `JourneyLegRunner.cs` (47-57, 222-224), `LoginCheckRunner.cs` (54-57, 439), `RebuffDriverHost.cs` (12-20), `Main.cs` (289 the field of view, 315-320 quit-after) | items 0, 4, 6 |
| The wayfinder | `docs/wayfinding.md` (the tile, the planner, the room graph, the runner, the creature layer, open TODOs), `src/Lifestone.Client/Movement/Wayfinding/NavRunner.cs` (446, 590 the object goal; 1022-1029 the one-waypoint steer), `PathPlanner.cs`, `NavRoute.cs` (132-135), `RoomGraph` | items 0, 2, 5 |
| The physics host plan | `docs/physics.md` §9-§12 ("Other objects" at 397-401), `docs/milestones/m4-hero-visual-bugs.md` §1.3-§1.6 and §4 (on the branch `m4/hero-visual-bugs`, 3ac99bba: the diagnosis and `AvatarJumpWatch`) | items 0 (the merge), 5 |
| The objects and their lights | `src/Lifestone.Game/scripts/World/Objects/ObjectsSystem.cs` (339 the GC watch), `ServerObjectsLayer`, `AvatarSet.cs` (1314 the notices ring), `ObjectLightTable.cs` (27 the 256 rows, 68 `InUse`), `Session/State/GameStateReducer.World.cs` (the object table), ACE `Managers/ObjectMaint.cs` (21, 41 the 25 s client cull it relies on), `Managers/GuidManager.cs` (126) | item 0 (the cull) |
| The session's timeouts and reconnect | `src/Lifestone.Net/Session/SessionTimeouts.cs` (13, 28-49), `Session/GameSession.cs` (155 `LastTickMs`, 167 `Reconnects`), `Session/Credentials/LoginRetryPolicy.cs` (19, 45, 73), `RetailUi/Screens/DisconnectedScreen.cs` (81), ACE `Network/NetworkSession.cs` (336-338) | item 4 |
| The measurement hooks | `src/Lifestone.Game/scripts/Diagnostics/PerfOverlay.cs` (79-80), `FlyPathRunner.cs` (285-318), `GcFrameWatch.cs`, `Session/Scenarios/HeadlessBody.cs`, `FrameTimeStats.cs` (4-55), `LinkStats.cs` (7-19) | item 0 (the `[soak]` line) |
| The retail UI | `docs/retail-ui.md` §21.5 (the panel host), §21.8 (the options panel, 1395-1424), §23.6 (the spellbook and components), §23.8 (the spellcasting window, 1617-1658), §21.2 (the chat, 1185-1210); `src/Lifestone.Client/RetailUi/Options/*`, `Panels/GameSpellcastingPanel.cs` (40-46), `Core/UiClassRegistry.cs`; `src/Lifestone.Client/Options/OptionCatalog.*.cs` | item 6 |
| The wire | `src/Lifestone.Net/Messages/Opcodes.cs` (160-263 the event types, 264 on the actions), the codec folders, `docs/protocol.md` §5 (the message table), ACE `Network/GameEvent/Events/*`, `Network/GameAction/Actions/*`, `Network/GameMessages/Messages/*` (the writers are the specification) | item 6 (the confirmation, the options, the property updates, the recalls) |
| The checks | `tools/smoke.ps1` (277-280 the steps, 463 the contended rule, 691-693 the error count), `tools/slice-check.ps1`, `tools/live-gate.ps1`, `tools/lib/StepPaths.psd1`, `GateAreas.psd1`, `tests/Shared/test-count-baseline.json` (9,238 default, 736 Exhaustive) | every item's check slice |
| The vendors and the world data | the spec §5.7; the pinned world SQL under `server/runtime/downloads` (parsed read-only, never loaded by a slice); the knowledge corpus at `<repo>\..\ACOracle` (D11; `world/npcs`, `world/items`, `world/creatures`; the MCP server in `rag/`) | items 2, 3; the end item's M6 seed list |

## 2. Seams and owners

One owner per file set; another owner's need goes under "For the lead" in a reply, never as an edit. Within an item,
a slice may edit only the files its §4 row names.

| File set | Owner | Contract the others use |
|---|---|---|
| `src/Lifestone.Client/Session/Casting/**` (new) | item 1 | `CastAttempt`, `CastRules` and its presets, `CastReport`, `CastTally`; the proof hook; every caller casts through it |
| `src/Lifestone.Client/Session/Grounds/**` (new: the profile records, the embedded JSON profiles, the loader) | item 2 | `HuntingGround` (immutable; a missing field fails at load); `GroundCatalog.Get(key)`; the den's constants read from it |
| `src/Lifestone.Client/Session/Autopilot/**` (new: the state machine, the checkpoint, the stop rules, the cycle line) | item 4 | `AutopilotScenario`, `AutopilotState`, `Checkpoint`, the `autopilot cycle` and `done` lines (additive fields) |
| `src/Lifestone.Client/Session/Scenarios/**`, `Session/Mage/**`, `Session/Fighter/**`, `Session/Vitals/**`, `Session/Scenarios/Journey/**` | the item's scenario slice (items 1, 3, 4) | `IScenario`, the stations, the travel seam, the verdict lines (additive fields) |
| `src/Lifestone.Client/Movement/**`, `Movement/Wayfinding/**` | items 0 (the one-waypoint fix), 2 (the den graph, the indoor layer and steering), 5 (doors as blocked) | `NavRunner`, `PathPlanner`, `RoomGraph`, `NavGoal`; `-Exhaustive` on every slice that touches them |
| `src/Lifestone.Client/Session/State/**`, `Session/Contracts/**`, `Session/Actions/**` | items 0 (the cull), 1 (the `Automation` source), 6 (the new messages' reducers and actions) | `IGameSessionView`, `IActionApi`, the events; one reducer partial per area |
| `src/Lifestone.Net/Messages/**`, `Dispatch/**`, `Session/IAcSession.cs`, `SessionTimeouts.cs` | item 6 (the wire), item 4 (the reconnect's timeouts) | `IInboundMessageHandler` callbacks (append only); every codec cites ACE's writer or reader and the retail dispatcher where known; a port carries the N-process header at M4 D7's pin |
| `src/Lifestone.Client/Session/StandIn/**`, `scripts/Session/PlayChecks*.cs` | one slice per item, named in §4; never two of an item at once | `StandInWorld`'s answers, `StandInVisit`, the play-checks list |
| `src/Lifestone.Game/scripts/World/**`, `scripts/Session/*Host.cs`, `scripts/Diagnostics/**`, `Main.cs` | items 0 (the `[soak]` node, the cull's host side, `--max-fps`), 4 (the autopilot host and key), 5 (the physics host's wiring), 6 (the field of view) | the AC to Godot conversion stays in `scripts/Core/AcToGodot.cs`; a host node reads `IGameSessionView` and acts through `IActionApi` only |
| `src/Lifestone.Client/Physics/**` (the server-object host) | item 5 | `ServerObjectPhysicsHost`; `PhysicsWorld` and `PhysicsObj` unchanged in shape |
| `src/Lifestone.Client/RetailUi/**`, `scripts/RetailUi/**` | items 4 (the autopilot panel and the inspector's fields), 6 | `GamePanelHost.SetPageHandler`, `NotInM5`; a panel reads `IGameSessionView` and acts through `IActionApi` only |
| `tools/smoke.ps1`, `tools/slice-check.ps1`, `tools/live-gate.ps1`, `tools/lib/**`, `tests/Shared/**` | item 0's check slice (Godot's errors counted, `-AllSizes` in the gate's offline step), then each item's check slice | the step list, `$backendSteps`, the filter, the baseline file, the error count |
| `server/**` | no item plans a mod change; a verb an item needs is asked of the lead and installed under M4 D8's rule | `ToolsWire` verbs and replies |
| `src/Lifestone.Client/Options/OptionCatalog.*.cs` | every slice registers its options there | append only |
| `docs/**` | the item's last slice and the lead's doc pass; lanes write notes under `cache/m5/<item>/` (gitignored; the lead copies what matters into the docs before removing a worktree) | stable section numbers; append |

## 3. The waves

A wave is one to three items with a theme and a checkpoint; an item is one fresh lead session; a slice is one agent.
Estimates are shares of a week on the owner's meter, item and lead together, sized from M4's as-run shares (m4-plan.md
§8: a loop item with live rounds 5-6%, a panel item 3%, a mechanical item 2-4%, P5a and P5b about 5% each).

| Wave | Item | Theme and content | Gate | Share |
|---|---|---|---|---|
| 0 | 0 | **The measuring run and the two live bugs** (the lead, with four slices): the den's numbers (DoD 1) by a loopback measuring class and read-only SELECTs; the client-side object cull (D16) and its host side; the runner's one-waypoint fix with a runner exception made loud and Godot's `ERROR:` lines counted by smoke, slice-check and the gate; the `[soak]` metrics line and the outside loop script; `--max-fps` in the gate's Godot runs and one log instead of three; the branch `m4/hero-visual-bugs` merged (the diagnosis and `AvatarJumpWatch`) | `ObjectCullTests`, `NavRunnerTests`; `godot_errors=0` in the smoke summary (judged, a fact); the numbers in §8 | 3% |
| 1 | 1 | **The outcome-aware caster and mana inside the fight:** the P5b design's K1-K5 (`CastAttempt` and the presets, the Mana Conversion model and the stand-in's rolled 0x0401, the two adoption lanes, the tries in the keeper's order with their options) with every automated cast as `Automation` (D13); the fight's mana order (transfer, drink, rest, the try floor) and the bolt level re-chosen on a buff or vitae change | the callers' tests unchanged under the presets; `CastAttemptTests`, `FightManaTests`; live, the three journeys: `fizzles=` within bounds, `mana_wait_max_s<=10` report-only, the kill switch stopping a buff pass | 4% |
| — | checkpoint 1 | **"The mage casts like a player."** The item 1 lead reports the spend, the measured kill rate against item 0's and pings the owner (D17) | | |
| 2 | 2 | **The hunting-ground profile and the whole den:** `HuntingGround` as embedded JSON with its loader and dat tests (D22); the den's profile replacing `BlackSpawnDen`'s constants and M4 D12 (D4); the belt's and the forest's profiles read by the fighters' hunt; the 0x0105 room graph over every storey with the exit rooms as no-go and the storeys' connectivity proved on the dats; the indoor creature layer and steering; the avoid rule | `HuntingGroundTests`, `HuntGroundDatTests`, `DenRoomGraphTests`, `IndoorSteeringTests`; live, the mage tours two storeys (`storeys_visited>=2 basement_entries=0` report-only), the melee journey's hunt on the belt's profile unchanged | 6% |
| 2 | 3 | **The mage's logistics and the death handler, cheat-free:** the vendor kit with the four foci (D8), the mage's loot step, the resupply station (Renald, Cindrue, Thelnoth, a target list), the component and burden counters, the experience spending rule, the in-play grants and places removed, the death handled inside the loop with no vitae grant; the gate's scripted death kept as the trigger | `MageLootRulesTests`, `ResupplyStationTests`, `XpSpendRuleTests`, `DeathHandlerTests`; live, one cycle `granted=0 places=0 sold>=1 bought>=1 spent_xp>0` and a scripted death recovered, report-only | 5% |
| — | checkpoint 2 | **"The mage lives in the den without the mod."** The item 3 lead reports and pings (D17) | | |
| 3 | 4 | **The outer loop and the dial:** `AutopilotScenario` and its state machine, the checkpoint and resume, the reconnect that re-enters the world, the stop rules and the lifted caps, the watchdogs, the cycle and done lines; the Godot host with the key (D20), the panel button and the inspector's state; the person's keys interrupting and the hand-back resuming; the autopilot lane in the gate (D19) | `AutopilotTests` (every arrow, a restart that resumes, a drop that reconnects), `AutopilotHostTests`; live, the autopilot lane `cycles>=2` and the lead's hand-off on screen, report-only | 5% |
| 3 | 5 | **A solid world** (a lane beside item 4 under its own lead: the physics seam): the server-object physics host, doors as blocked and used on routes, the lifestone landing; the transitions for server objects if measured within the frame rule | `ServerObjectPhysicsTests`; live, Renald's door opened on screen, `stepped_off=no`, the jump counts in a den frame | 5% |
| — | checkpoint 3 | **"It runs until you stop it."** The item 4 lead reports both lanes and pings (D17); the cut order if the owner cuts: item 6, then item 5's transitions, then item 2's third profile | | |
| 4 | 6 | **Retail for the person at the keys, and the two sizes:** the Yes/No confirmation dialog, character options saved with the layout and the chat masks, the spellcasting bars editable with their hotkeys, the slash commands with tells and the recalls, the four public property updates, the field of view on resize; the shell frames and play-checks at 1280x720 and 1920x1080 in the gate's offline step | codec tests from ACE's writers, the panels' tests, play-checks, frames; live, a login that keeps the last one's options; a chest unlocked by a key reads unlocked | 4% |
| 4 | 7 | **The 24-hour run:** the long-run fixes left (the memory measures and fixes, the logs, recording off, the backend under the lead for the day, D6), then the soak itself at 1280x720 with `--max-fps=60` and the `[soak]` line, the frames every 30 min for the owner, the verdicts read (DoD 13) | `autopilot done ok=true` after 24 h; the working-set slope, `light_rows<256`, `godot_errors=0`, the reconnects explained | 4% |
| 4 | 8 | **The end:** the cluster reviews, the audit table, m4-plan.md §9's rows placed M5 closed or re-placed, the doc pass (session.md, automation-vision "as built", the README, CLAUDE.md to M6's planning), M6's seed list of grounds from the corpus (D11), the hero video from the soak's capture (D24), the private push on the owner's word | the audit table complete | 3% |
| | | **Build** | | **39%** plus 1% contingency = **40%** |

Build 40% plus about 2% of planning: **42% against the 45% stop line (D12)**. A lead asks before exceeding its item's
share by 20% (M3 D5), and each checkpoint re-estimates the waves left against what remains.

Order: 0 → 1 → 2 → 3 → (4 and 5 in parallel) → 6 → 7 → 8. Item 1 needs item 0's numbers and its two fixes; item 2
needs item 1's caster (the tour's casts go through it); item 3 needs item 2's profile (the vendors and the kit are
profile values); item 4 needs items 1-3 (the arrows it joins); item 5 needs only item 0 (the merge of the diagnosis
branch) and runs beside 4; item 6 needs nothing above and may start when a lead is free; item 7 needs 4, 5 and 6; item
8 is last. Each later wave is sliced by its first lead at the wave boundary (M3 P8), in the shape of §4, with at most
four owner questions in one call.

## 4. Wave 0's and wave 1's slice tables

Every slice: a fresh agent (the model by D18), 60-120 calls, under 300K of context, in its own worktree cut from the
item branch `m5/<item>` (`.claude/worktrees/m5-<item>-<slice>`, `git config core.longpaths true`); at most four files
to create and eight to edit; one acceptance command (`slice-check.ps1 -MinTests <count> -Steps <steps> -Rerun`, with
`-Exhaustive` for a slice that touches `src/Lifestone.Data` or the Client folders M4 D30 names, `Movement` among
them), one Godot step at most; the handoff file `docs/handoff-m5-<item>.md` in the branch (removed before the merge).
A new judge or verdict field is report-only in the slice that adds it. The test baseline is the count `slice-check.ps1`
reports on master at the item's start (9,238 default and 736 Exhaustive at M4's end; M4 D28: a floor only the lead
lowers, for deletions it names). Rounds of one agent stay under 300K: an agent that needs a second round ends with its
handoff and a fresh agent continues.

### 4.1 Item 0 The measuring run and the two live bugs (branch `m5/0`)

**W0 (the lead; read-only plus the live measures; the backend started and stopped by the lead):**
- a loopback measuring class, run by the lead only, over BSD Mage in the den: the respawn delay (30 minutes of the
  17 slaves' deaths and re-creations), mana per cast and the regeneration at rest and in combat (the 0x02E? vital
  pushes against the session clock), the cast time from the request to the projectile and the damage for Flame Bolt
  I-VII, Flame Streak, Flame Arc and Cassius' Ring of Fire 1785 (`FightTally`), Flame Bolt VII's component burn over
  100 casts (the pack's counts before and after), the ring's kills per cast on a cluster, the north wing's caster
  damage against the buffed mage (one bounded probe from the stairs, the kill switch armed), the mage's run speed;
- read-only SELECTs of the pinned world database: the den's generator rows (`RegenerationInterval`, the links' delays),
  the tuskers' treasure profile 448 and create lists, Cindrue's and Thelnoth's whole stock with prices, the Scrivener
  of War Magic's position in 0xA9B3 and the nearest Scriveners of the other three schools;
- a level-80 mage's buffed War Magic from `MageSpendingPlanner` and the XpTable (a unit test that prints it);
- the numbers written into §8 and the spec's §5.4 and §5.7 amended where they disagree;
- the branch `m4/hero-visual-bugs` merged into `m5/0` first (the diagnosis doc and `AvatarJumpWatch`; its gate is
  `slice-check -Gate`).

| Slice | Scope | Files (create / edit) | Acceptance | Size |
|---|---|---|---|---|
| c1 the cull | `ObjectCull`: an object the server has stopped sending leaves the object table and the avatar set after retail's client-side time (25 s; ACE `ObjectMaint.cs:21,41` relies on it; the retail function name and 11.6096 address are read from the symbol table, else "unknown"), never the player, never an object in the player's inventory or a container the player has open; a re-sent object is one entry; the light table's `InUse` falls with it; `objects= in_cell= light_rows=` on the login-check line | create `Session/State/ObjectCull.cs`, `ObjectCullTests.cs`; edit `GameStateReducer.World.cs`, `AvatarSet.cs`, `ObjectLightTable.cs`, `LoginCheckScenario.cs` | `slice-check -MinTests <base>` | Opus high, ~100 calls |
| c2 the runner and the errors | `NavRunner.Steer` on a one-waypoint route; an exception in a runner's or host's `_Process` ends the leg loudly once, not every frame; `godot_errors=<n>` counted from Godot's `ERROR:` and `SCRIPT ERROR:` lines by `smoke.ps1`, `slice-check.ps1` and `live-gate.ps1` and judged 0 (a fact, D19) | edit `NavRunner.cs`, `NavRunnerTests.cs`, `tools/smoke.ps1`, `tools/slice-check.ps1`, `tools/live-gate.ps1`, the script tests | `slice-check -MinTests <base> -Exhaustive` | Sonnet, ~70 |
| c3 the soak line | the `[soak]` INFO line every 60 s from a small node beside `ObjectsSystem` (the fields of DoD 13; allocation off the frame path; `--soak-line-s`, 0 off), `tools/soak-watch.ps1` (the outside loop: the three processes' memory, the log and database sizes, free disk; read-only), `--max-fps=60` in the gate's Godot runs, one log file (the console and `godot.log` copies off by default: `--log-copies`) | create `scripts/Diagnostics/SoakLine.cs`, `tools/soak-watch.ps1`, tests; edit `OptionCatalog.Play.cs`, `tools/lib/Play.psm1`, `tools/live-gate.ps1` | `slice-check -MinTests <base> -Steps login-check` | Sonnet, ~80 |
| c4 the measuring class | `DenMeasureLiveTests` (Loopback, the lead only; `[Trait("Account","test")]`): the measures of W0 as one class with its fields printed, no asserts beyond "measured"; the SELECT helper over the pinned SQL (read-only) | create `tests/Lifestone.Loopback.Tests/Session/DenMeasureLiveTests.cs`, the helper | compiles; the lead runs it | Sonnet, ~60 |
| D, R, F | the doc pass (docs/session.md: the cull and the soak line; the spec §5.4 and §5.7 amended), the review, the fix slice | docs | `slice-check -Gate`; the live gate's journeys green with `godot_errors=0` | Haiku, Opus, Sonnet |

### 4.2 Item 1 The outcome-aware caster and mana inside the fight (branch `m5/1`; after item 0)

The P5b design's §5.1 table carried, with its W0 done by item 0 (skill 16 read; the 0x0401 latency; ACE's FastTick;
a refused try changes nothing) and one slice added for the fight's mana order:

| Slice | Scope | Files (create / edit) | Acceptance | Size |
|---|---|---|---|---|
| K1 the policy | `CastAttempt`, `CastRules` (the presets `BuffLine`, `Once`, `Utility(n)`, `Bolt`, `Travel(n)`, `Timed(s)`), `CastReport`, `CastTally`; the proof hook; tests of every rule row of the design's §2.2 | create `Session/Casting/CastAttempt.cs`, `CastRules.cs`, `CastReport.cs`, tests | `slice-check -MinTests <base>` | Opus high, ~100 |
| K2 the model (a lane beside K1) | `ManaConversionModel` (ACE's two rolls, an exact distribution, the try floor); the stand-in's 0x0401 on a rolled cost; the scripted caster's rolls | create `Session/Casting/ManaConversionModel.cs`, tests; edit `StandInWorld.Actions.cs`, the scripted caster | the same | Sonnet, ~70 |
| K3 adoption 1 (after K1) | `BuffCycle` (the lines, the upgrade pass, the mana loop), `RebuffDriver`, `FightScenario` through `CastAttempt`; `ActionSource.Automation` everywhere (D13); the fizzle rule of D14 | edit `BuffCycle.cs`, `RebuffDriver.cs`, `FightScenario.cs`, their tests | the existing tests unchanged | Sonnet high, ~110 |
| K4 adoption 2 (a lane beside K3) | `VitalsKeeper`, `HarmHealScript`, `RecallHome`, `DeathScenario.Heal`, the script legs' cast helper | edit those five, tests | the same | Sonnet, ~100 |
| K5 the tries in the order (after K2, K4) | `VitalsNeeds.NextCastFloor`, the three-step order, the mana loop's order, `--cast-mana-tries`, `--cast-mana-try-chance`, `--cast-mana-try-spacing-s`, the fields `mana_tries= mana_refusals= mana_landed= busy= resists= proved=` | edit `VitalsKeeper.cs`, `VitalsOptions.cs`, `BuffCycle.cs`, `OptionCatalog.*`, tests | `slice-check -MinTests <base>` | Opus high, ~100 |
| F1 the fight's mana (after K3, K5) | the fight's mana phase replaced: the try floor, Stamina to Mana at a mana floor, a Mana Potion, a rest in peace for stamina, the bolt level re-chosen after a buff or vitae change and at a level-up; `mana_wait_max_s=` and `kills_per_hour=` on the fight's line; the kill cap lifted (0 = unlimited) and a clean duration stop (`--fight-minutes`) | edit `FightScenario.cs`, `FightOptions.cs`, `FightLegVerdict.cs`, tests | `slice-check -MinTests <base>`; the lead's live fight of 30 min with the rate read | Opus high, ~100 |
| D, R, F | the doc pass (docs/session.md's new section; automation-vision R6's reach), the review, the fix slice | docs | the gate's three journeys live: `fizzles=` within bounds, no mana wait over 10 s, the kill switch stopping a buff pass; the measured kill rate against item 0's | Haiku, Opus, Sonnet |

## 5. Gates and the live-gate rules

M4's §5 in full, with its P4, P5b and fresh-gate changes (the four-lane parallel gate, at most three Godot windows,
the journeys judged by their done lines, G-A as `slice-check -Gate`, the offline Godot smoke once per item, fresh
journey characters each gate), and these M5 changes:

1. **Branches and worktrees:** `m5/<item>`, `.claude/worktrees/m5-<item>-<slice>`; the merged item's worktrees and
   branches removed after its merge (M4 D15, D21 here).
2. **The checkpoint after every wave (D17):** the wave's last lead writes the spend per item against §3, the DoD items
   met so far, the measured rates, the frames, and pings the owner; nothing is cut or started until the owner answers.
3. **New verdict fields and their first gates:** item 0's `objects= in_cell= light_rows=` report-only; `godot_errors`
   judged 0 from its first gate (a fact, not a threshold: the named exception, as M4 D31's audio fact); item 1's
   `mana_tries= mana_landed= busy= resists= proved= mana_wait_max_s= kills_per_hour=` report-only, `mana_wait_max_s<=10`
   judged from item 2's gate; item 2's `storeys_visited= basement_entries=` report-only, judged from item 3's gate;
   item 3's cycle fields report-only, `granted=0 places=0` judged from item 4's gate; item 4's `cycles= reconnects=
   interrupted= resumed=` report-only, `cycles>=2` judged from item 6's gate; item 5's `door_opened=` report-only,
   judged from item 6's gate; the soak's verdicts report-only (one soak).
4. **The autopilot lane (D19):** in the `test` lane after the mage's journey (one character per account in the world),
   `smoke -Only autopilot-mage -ContendedFrames`: 30 minutes of the autopilot on BSD Mage with one trip forced early
   (`--autopilot-trip-after-kills=5`) and one scripted death (M4 D37, `--autopilot-test-death`), judged by its done
   line as the journeys are (`Get-AutopilotVerdict`); its frames report-only (contended). The three journeys and the
   tools lane stay as they are.
5. **The two sizes (D9):** the gate's offline step runs `smoke.ps1 -AllSizes` for the shell frames and the play-checks
   at 1280x720 and 1920x1080 from item 6 on; the frame judge stays at 800x600 (M2 D57).
6. **The soak (D6, D23):** once, in item 7, alone on the machine, the backend under the lead for the day, the Godot
   play host at 1280x720 with `--max-fps=60`, recording off, the `[soak]` line and `tools/soak-watch.ps1`, a frame
   every 30 min for the owner's eye (checked for held items and models before it is shown); at most one re-run after a
   fix, then the numbers recorded and the owner asked.
7. **Rounds, not marathons:** at most three live rounds per item, then the lead stops, writes the numbers into §8 and
   asks the owner (M4 §5 item 7).
8. **The owner's eye on every frame** (M4 §5 item 8): the lead checks the held items and the models before sending,
   says plainly what is a placeholder, and now also reads the `AvatarJumpWatch` counts of a den frame.

## 6. The rules and the process

The rules are CLAUDE.md, "Hard rules" (fidelity under "Quality") and "Rules for agent sessions"; a brief cites them
and restates nothing. The templates in `cache/m5-prompt-templates.md` (gitignored; M4's T1-T7 with the names changed,
made by item 0's lead from `cache/m4-prompt-templates.md`) carry a rules block whose lines must equal CLAUDE.md's; a
lead compares the two at the start of its item and corrects the template. The process decisions are M3's P1-P8
(m3-plan.md §6) with M4's amendments (the checkpoint after every wave, a planned research phase per item, the roadmap
line at every transition, frames judged solo) and D18's model mix. One rule is M5's own, from the spec's §4: a
den-specific fact is a profile value, and a lead refuses a slice that adds one as a constant.

Templates: **T1** the item lead prompt (one fresh session per item, with its settings table: the model, the effort,
the mode, the worktree, the subagent limits, the budget share and ask-line); **T2** the implementer slice brief; **T3**
the parallel lane brief; **T4** the review brief; **T5** the fix-slice brief; **T6** the merge brief; **T7** the
live-gate runbook, with the soak's runbook added in item 7. Each lead writes the next item's T1 into `cache/` before it
ends.

## 7. Decisions

D1-D11 are the owner's, given in the M5 workshop of 2026-10-25 (recorded, not quoted); D12-D24 are the planner's
defaults, which the owner may overrule at any checkpoint.

| # | Question | Blocks | Decided |
|---|---|---|---|
| D1 | The direction after M4 | everything | **Single player first; patrons later.** The product is a game that plays itself from a character's start to the game's end, with a lot of grinding, where the person plays as much or as little as they like, by hand or watching. Not a demo: the game. M5 moves the project from writing retail to "retail+" |
| D2 | Which class first | the autopilot, the profiles, the gate | **Mages, broadly; the other classes from M6 on.** A mage is the character that automates fully with vendor gear and Item Magic; the fighters' journeys stay as the gate's coverage of melee and missile |
| D3 | The milestone's shape | §3 | **M5 is the first dungeon; M6 adds dungeons and regions to idle in, rapidly, as data** |
| D4 | The whole dungeon, or M4 D12's arrival floor | item 2 | **Capable of the whole den; in practice never the drudge and virindi wing** (not worth the time). D12 is superseded by the profile's avoid rule |
| D5 | The autopilot's character | items 3-4, the soak | **The test character, BSD Mage, for now** (re-created at each gate under M4 D44; the soak runs on the gate's copy); a character of the owner's own is a later decision |
| D6 | The backend up for days | the soak | **Yes**, under the lead |
| D7 | The server's `xp_modifier` | the grind's numbers | **No; retail rates.** Players grinded one dungeon for a month at several points in the game's history; a month in the den is the plan |
| D8 | The components Holtburg does not sell (Platinum, Pyreal and Gold Scarabs), and the foci | item 3 | **The owner's point: retail's mages carried the four foci**, so every cast burns only its scarab and Prismatic Tapers (the spec §5.7). The kit holds the four foci and a first stack of Platinum and Pyreal Scarabs, granted by the build (the cheat layer) until the mage can buy them; a stock-up trip to a Scrivener or Archmage Philomon is a profile step for a later item; a stack under three stops the loop loudly |
| D9 | The window | item 6, the soak | **It is resizable already; fix the camera's field of view on a resize (`cam-fov-resize`) and test 1280x720 and 1920x1080**; the soak at 1280x720 |
| D10 | The layers above the first dungeon | §3.2 | **Recorded, deferred** (the spec §5.1): the goal planner and the quest layer, the service bots of retail (buff, tinker, dye, ingot bots on their own accounts, the companion design as their base), the self-levelling alts with a trade-skill focus, the crafting bots |
| D11 | The knowledge corpus | items 2, 3, 8; M6 | **ACOracle, the owner's sibling checkout, is the source** for grounds by level, quest profiles, recipes, vendors and the service-bot customs, under the spec §5.10's rules; nothing of it is committed to Lifestone |
| D12 | The budget | every item | **40% build plus about 2% planning against a 45% stop line**, in shares of the weekly meter (M4 D2's shape); a lead asks before exceeding its item's share by 20%; the meter resets 2026-10-26 |
| D13 | Every automated cast as `ActionSource.Automation` | item 1 | **Yes** (the P5b design's Q11): the kill switch and dry run then reach buff passes, keeper casts and recalls, as automation-vision R6 says |
| D14 | The fizzle rule | item 1 | **The same level twice more, then one level down once, then a named miss** for the buff lines and the bolt (the design's Q2); recalls, ties and the keeper keep their bounds; the owner asked for all of the design's fixes |
| D15 | Keep pressing cast short of mana | item 1 | **Yes, by default:** at most 8 tries a need, 0.25 s apart, from a 25%-a-try floor, every try counted; `--cast-mana-tries 0` restores today's ceiling (the design's Q3) |
| D16 | The client-side cull | item 0 | **Ported from retail's rule** (the 25 s cull ACE's `ObjectMaint` relies on; the retail function named where the symbol table gives it); never the player, an inventory item or an open container; the interim log-off is not used |
| D17 | The checkpoint and the cut line | every wave | **A checkpoint after every wave** (M4 D3's shape); the cut order if the owner cuts: item 6, then item 5's transitions part, then item 2's third profile |
| D18 | The model mix | every item | **M4's** (m4-plan.md D38's mix: Opus 5.5 at high effort for the per-item lead, the item review and the design-heavy slices named in §4; Sonnet 5.5 for the other slices, lanes and fixes; Haiku 5.5 for merges without conflicts, doc passes and lookups); the owner may name another model for a lead at the item's start |
| D19 | The gate | items 4-7 | **The autopilot lane in the `test` lane after the mage's journey** (30 min, one early trip, one scripted death), report-only in its first gate; `godot_errors=0` judged from its first gate as a fact; the soak once |
| D20 | The autopilot's key | item 4 | **One retail's default map leaves unbound**, picked by item 4's lead from the key map in the dats (docs/retail-ui.md §22.1), beside `Pause` (auto-buff), `F7` (tools) and `Scroll Lock` (the inspector); a panel button does the same |
| D21 | Worktrees and branches | every item | `m5/<item>`; the worktrees and branches of merged items removed after each merge (M4 D15); `m4/hero-visual-bugs` merged in item 0; the other leftover M4 worktrees left alone (CLAUDE.md) |
| D22 | The profile's form | item 2; M6 | **Embedded JSON profiles** under `Session/Grounds/profiles/` with a loader that fails loudly on a missing field and dat tests per profile; not C# constants (M6 adds a ground by adding a file); user-supplied profile files are automation-vision §1.4 K's later step |
| D23 | The soak's shape | item 7 | **The Godot play host at 1280x720, `--max-fps=60`, recording off, one log**, the `[soak]` line every 60 s, the outside loop every 5 min, a frame every 30 min; the owner's machine settings (sleep, updates) are the owner's |
| D24 | The hero video | item 8 | **As M4 D32:** under 30 s, no on-screen text, the mage's day in the den cut from the soak's capture (the capture at 1920x1080 with `--journey-film`'s camera turns if the soak's own window is not enough) |
| D25 | The den loop's movement (the owner, during item 0, 2026-10-25) | item 2 | **The mage runs the whole den hunting tuskers and is back at the arrival floor just before its slaves' respawn timers run out.** Item 0 measured why: the arrival floor's 17 Tusker Slaves die in 4.4 min and each slot returns 903-955 s after its death (the generator's 900 s delay plus its 60 s tick), so the floor alone is respawn-bound at about 64 kills an hour; standing still draws no tuskers (RingCluster's first run). The profile carries the respawn schedule (per slot: the death time plus 900 s, rounded up to the generator's tick); the loop tours the storeys below (64 Tusker Guards) and returns in time |
| D26 | The level-80 mage's spell level (the owner, during item 0) | items 1-3 | **A level-80 mage casts level VI** (Pyreal Scarabs, not 13,500-pyreal Platinum). Item 0's planner figure (buffed War Magic 361 at level 80, Flame Bolt VII at chance 0.986) would allow VII; the profile's level rule follows the owner. VI is also the cheaper mana: Flame Bolt VI's expected cost 4.0 against VII's 7.1 at Mana Conversion 311 (§8) |
| D27 | The stamina-to-mana dance (the owner, during item 0) | item 1 (K5, F1) | **When mana is very low, Stamina to Mana Self is pressed repeatedly until a Mana Conversion roll lets it land**: ACE refuses a cast whose rolled cost exceeds the mana held and charges nothing (`Player_Magic.cs:566-585` @95ef426d), so each press is a fresh roll (`Creature_Magic.cs` `GetManaCost` 58-93: two skill checks, against power / 2 and power, factor 0.03, one shared luck roll); the transfer gives 0.875 x current stamina at VII (0.75 at VI), capped at the missing mana (`WorldObject_Magic.cs` `HandleCastSpell_Transfer` 697-760). The fight's mana order is: fight to near zero, pound the transfer, fight on; transfer before stamina reaches its maximum. Item 0's model and live numbers are §8's |
| D28 | Item 0's merge after three live rounds (the owner) | item 0 | **Merge with the 60 fps cap kept on the gate's journeys**; the archer's forest hunt that failed twice capped (and passed twice uncapped, master's control needing 5.5 km) is M4's thin-forest hunt (`m4-hunt-band-empty`), named for item 2's hunt profiles; if item 1's gate fails the archer again while capped, item 1's lead drops the cap |

## 8. As run

**Planning (2026-10-25).** One Fable 5.1 session in the owner's M5 workshop, read-only until the docs were written, on
master bfe466b7 while M4's last commit was being made. The direction came in the workshop (D1-D11). Five read-only
audit agents ran in parallel (their notes gitignored under `cache/m5/`; the synthesis `cache/m5-gap-analysis.md`):
the protocol and game-system coverage (Opus, 92 calls, 285K tokens), the retail UI coverage (Sonnet, 101 calls, 330K),
the days-long run risks (Opus, 173 calls, 363K), the cheat ledger and the mage's economy (Sonnet, 137 calls, 497K), the
den fight loop (Sonnet, 157 calls, 523K). The XP table was read from the owner's dat by a scratch probe outside the
repository. The knowledge corpus (D11) was found in the workshop and read for the foci, the scarabs and the Scriveners.
The spend is the owner's meter reading at the session's end.

**Item 0, the measuring run and the two live bugs (2026-10-25, branch `m5/0`; one Opus 5.5 lead at high effort).**
*Changed from §4.1:* c2 was split in two: c2 kept the runner's one-waypoint route and gained the leg guard
(`LegFaultGuard` in eight Godot runners, more than eight edits, each a few lines); a new lane c5 took the checks' Godot error
count (`smoke.ps1`, `slice-check.ps1`, `live-gate.ps1`, `GateJudge.psm1`) and the gate's `--max-fps=60` (the lead's
decision: the journeys capped, the solo frame judge, the throwaway's frames and the leak scan not, since
`LoginCheckRunner.HoltburgMinimumFps` is 60). c3's "one log" is `godot.log` off by default with the console kept (every
check reads the console; `--log-copies=console|file|all`). The review's 0-R1 (the cull inside a dungeon counted the whole
landblock as visible) was fixed in this item, not left to item 2: the cull reads each EnvCell's visible-cell list from the
dat in both hosts, as ACE's `ObjectMaint.GetVisibleObjects` does. The measuring class gained ManaDance (D27) on the owner's
word. The `m4/hero-visual-bugs` merge raised the test floor 9,238 to 9,243, the lanes to 9,322, the fixes to 9,337.
*Retail's cull rule* is ACE's: `CObjectMaint::AddObjectToBeDestroyed` (11.6096 0x00509A40) queues an object for 25 s,
`UseTime` (0x00509480) deletes it when due, `RemoveObjectToBeDestroyed` (0x00508F10) cancels it; it is queued when its
cell is released (`ReleaseObjCell` 0x005086E0) or it goes to the lost cell (`GotoLostCell` 0x00508CE0). The owner's
question 3 was not needed.
*Agents* (calls, peak context): c1 Opus high 82 and 39 (the 0-R1 round), 211K and 264K; c2 Sonnet 59, 175K; c3 Sonnet 86,
243K; c4 Sonnet 101, 346K, then four resumed rounds of 6-17 calls each up to 425K (ran long: the measures' fixes, RingCluster's
and SpellTable's redesigns and ManaDance were added to its context instead of a fresh agent's); c5 Sonnet 94, 262K (one resume:
its first hand-back came before its test run ended); the review Opus high 73, 265K (0 blockers, 6 should-fix, 7 nits); the fix
slice Sonnet 60, 179K; the doc pass Haiku 33, 129K.
*The measures* ([M] measured live on BSD Mage, level 127, on `test`; [D] derived; [S] the pinned world SQL v0.9.296; the logs
under `cache/m5/0/`):

| Measure | Value | Tag |
|---|---|---|
| The den's generators | three of wcid 3955 "Linkable Monster Gen (15 min.)": `RegenerationInterval` 60 s, one profile row each, delay 900 s, init 1, max 1; 0x70105022 links 35 (8 Stalkers, 16 Raveners, 11 Guards), 0x7010507A 1 (a Virindi Master), 0x7010507B 81 (8 Virindi, 1 Master, 19 Slaves, 53 Guards) | [S] |
| The respawn delay | the arrival floor's 17 Slaves, each death paired in order with the next new guid: 903-955 s, median about 924 s (900 s plus the generator's next 60 s tick); the creates come in batches at the ticks | [M] |
| The arrival floor's kill rate | 34 kills in 1,800 s (68 an hour): 17 in the first 265 s (14.5 s a kill, about 250 an hour while targets stand), then none for 12 min until the slots returned; respawn-bound, not mana-bound (`mana_wait_s=0`); 118 casts (3.5 a kill), 17 misses | [M] |
| Damage per landed Flame Bolt VII | 136-212, median 178 (67 lines; the killing blow has no line) | [M] |
| Cast time, request to projectile, facing the target | Flame Bolt I 0.63 s, II 1.15, III 1.63, IV 2.06, V 2.47, VI 2.40, VII 2.46; Flame Streak VII (Sizzling Fury 2129) 0.61 s; Flame Arc VII 2.46 s; with a turn and the fight's pick: VII 2.9 s median, Streak 1.7 s. The spec's 3.33 s included the turn and the next pick | [M] |
| Mana regeneration | 0.44 a second at rest and in magic stance; stamina 1.30 a second at rest, 0.66 in magic stance | [M] |
| Mana per cast | the vital pushes lag, so the live pairs are noisy (0-22); the expected cost from ACE's formula at Mana Conversion 311: Flame Bolt VII 7.1 (base 35, power 300), VI 4.0 (base 30, power 250), Streak VII 14.2 (base 70), Ring of Fire 7.8 (base 80, power 200), Meditative Trance 14.2 (base 70, power 300), Stamina to Mana VI 7.9 (base 60); at 258: 10.3, 6.6, 20.6, 10.9, 20.7, 13.1 | [D] from the dat and `GetManaCost` |
| The stamina-to-mana dance (D27) | landing chance per press at Mana Conversion 311, Meditative Trance: mana 1 12%, 5 35%, 10 53%, 15 65%; at 258: 5 18%, 10 37%, 15 50%; at 220: 10 17%, 15 35% (Stamina to Mana VI lands about twice as easily). Live at mana 10-15: 5 dances, 4 landed on the first press, one after 8 refusals (2 s); 38% per press over 13 presses; each landing +197 to +277 mana for 109-158 stamina (1.75 to 1) | [D], [M] |
| The loop's mana supply | regeneration alone 0.44 a second (about 63 kills an hour at Flame Bolt VII's 25 a kill); with the dance in magic stance about 1.0 a second (0.44 + 0.66 x 0.875): about 145 kills an hour at VII, and at VI (14-16 a kill) about 230, near the cast-bound 240 | [D] |
| Component burn | Flame Bolt VII on the Foci of Strife: 0.43 Prismatic Tapers a cast (51 over 118 casts, all VII), no scarab in 118 casts | [M] |
| Ring of Fire 1785 | range 4.5 m (a target farther is refused 0x0550); 9 projectiles; on 6-7 tuskers within 5 m: 2 casts, 10 damage lines (500 and 239), 3 kills: about 1.5 kills a cast, against 3.5 bolts a kill; 66-74 a projectile | [M] |
| The north wing (one bounded probe, D4) | at the Virindi spawn 0x0105016C (z -24; the den map names no stairs cell), 129 s: a Drudge Ravener's critical Lightning Bolt IV for 106 and a Virindi Servant's Lightning Bolt III for 50; health 240 to 81; retreat at 50%; no death | [M] |
| Run speed | 10.4 m/s on the archer journey's legs (`speed_mps` 10.4-10.5); the mage's legs were too short to read | [M] |
| A level-80 mage's War Magic | `MageSpendingPlanner` and the XpTable: base 311, buffed 361 (Flame Bolt VII at 0.986); level 100 387, 120 399, 126 402 (live 410 at 127); D26 sets VI at level 80 | [D] |
| BSD Mage's kit | 60 component kinds, 2,454 units, burden 22,034 (not the spec's 17,000); the Foci of Strife only; 5 Platinum Scarabs, 689 Prismatic Tapers; health 240, stamina 315, mana 466; War Magic 345 base and 410 buffed; Mana Conversion ranks 169 | [M] |
| The vendors | Archmage Cindrue 2304 and Thelnoth Cort 715 sell at 1.35: Cindrue Lead, Iron, Copper (135) and Silver Scarabs, the Prismatic Taper (30), the coloured tapers (34), the herbs (14), powders and talismans (7), Minor (338) and Lesser (675) Mana Stones and the Mana Charges (473-3,375), no Platinum Scarab and no focus; Thelnoth the Mana Potion (230), Healing Potion (230), Stamina Potion (102), Mana Draught (115) and the kits (14, 68) | [S] |
| The Scriveners | all four schools' in landblock 0xA9B3 (cells 0x003C-0x0108, z 116-119), 152-158 m from Holtburg's lifestone; the next in 0xA1A4, about 3.3 km away | [S] |
| The tuskers' loot | wcid 1628 and 1629: `DeathTreasureType` 448 (tier 4; 1-2 magic items, 0-1 mundane), create lists of 12 and 10 rows | [S] |

*Not taken:* the loot's sale value (`m5-den-loot-value`, item 3), `ComponentLoss` below VII (`m5-component-loss-vi`; only
VII was cast in the burn window), the travel spells' formulas. *Live rounds:* round 1 the whole journeys gate (`m5-0-r1` at
d3a1ab72, 1,597 s): every step but one passed (the recreates, the mage and melee journeys, the loopback classes, the frame
judge 3 of 3 clean with `enter_to_walk_max_ms` 22.6-32.7 and `holtburg_min_fps` 236-238, the throwaway's frames 3 of 3,
`godot_errors=0` on all 12 Godot rows, the leak scan `secrets=0`); the archer journey's hunt failed ("no creature of the
table in the band 20-30 within 35 m ... after 8 cell(s)", route 5,259 m). Round 2 the archer journey alone, capped: the same
failure (5,104 m). Round 3 the archer journey uncapped on this branch (pass, 1,505 m) and on master's build (pass, 5,498 m):
the forest is thin and the hunt marginal either way; the cull is not the cause (the creature the archer chased was culled
about 85 s after it had left that area by four landblocks); D28. The cull's fields on the login check: `objects=100
drawn_in_cell=8 light_rows=9` (report-only). The den frames: the jump detector logged 0 in the den fight and 12 in the death
station (11 from one object, a 0.4 m `RootFlip`, culled 11 s after the recall). G-A (`slice-check -Gate`, solo, backend
down): 9,336 of 9,337 with one wall-clock flake (`GameSpellPageTests.The_controllers_are_built_in_staged_units_under_8_ms`,
45 ms; its one rerun passed; c3 saw the same flake); 736 Exhaustive. The item-end `slice-check -Changed` first failed one step: `interior-engine-checks` printed 22 Godot errors ("Too many
instances using shader instance variables"), the new judge's first catch; they are the check's intended negative control
(it fills Godot's global shader buffer on purpose), so the check now declares them (an exact text and a computed count of 11
for each of the two kinds) and smoke passes the step only when the seen count equals the declared one and no other Godot
error appears (`godot_errors=0 expected_engine_errors=22/22`; c5's round 3, 0126322f); `tools/soak-watch.ps1` joined
`StepPaths.psd1`. The rerun: every backend-free step PASS (shell-frames on its one rerun), 9,344 tests. No mod change.
*Spend:* the meter read 82% at the item's start and 85% at its end (about 3%, on the share).

## 9. Open TODOs

**Inherited from M4** (m4-plan.md §9, every row placed "M5"; the names are the rows'; a row an M5 item does not touch is
re-placed by the end item):

| Area | Rows | Placed in M5 |
|---|---|---|
| fights | `m3-cast-projectile-attribution`, `m3-cast-other-target-proof`, `m3-autotarget-*`, `m3-combat-options`, `m3-combat-tooltips`, `m3-squelch-combat`, the 1.3 review's nits, `m4-combat-map-keys`, `m4-retail-wield-stance`, `m4-click-without-press`, `m4-powerbar-frame-live` | item 1 (the proof hook); item 6 (the map keys with the spell hotkeys); the rest re-placed M6 |
| the hunt loop and the journeys | `m4-hunt-follow-collision`, `m4-creature-level`, `m4-forest-cells`, `m4-hunt-frame-camera`, `m4-hunt-pack-full`, `hunt-crossing-stall`, `m4-hunt-band-empty`, `m4-journey-buffed`, `m4-journey-ground-height`, `j4-kit-account`, `j4-station-kind`, `j4-hunt-character`, `renald-front-room-point`, `m4-travel-failure-unnamed`, `m4-journey-sounds-server` | items 2 and 3 (the profile carries the cells, the levels, the vendor points; the mage's stations close `j4-hunt-character`); the rest re-placed |
| the death loop | `m4-death-trip-heal-resume`, `m4-decoy-bonded-flag`, `m4-decoy-value-unknown`, `m4-death-walkless-wait`, `m4-lie-down-controller` | item 3 (the handler) |
| the wayfinder | `nav-indoor-steering`, `nav-creatures-indoor`, `nav-creature-radius`, `nav-static-footprint`, `nav-sight-no-physics`, `wayfind-indoor`, `nav-water-corner`, `room-steps`, `placement-cell-edge` | item 2 (the first three and `wayfind-indoor`); item 5 (`nav-sight-no-physics`); the rest re-placed |
| the physics | `d25-server-object-host`, the lifestone landing | item 5 |
| the camera | `cam-fov-resize` | item 6 |
| the testing loop | `m3-ui-step-gc-margin`, the flakes under load (`collision-streamer-settle-flake`, `offline-walkcheck-flake`, `dynamic-lights-alloc-flake`), the wall-clock tests, `m4-buff-ledger-hand-legs` | item 7's soak measures the GC and the allocations over a day; the Timing tier as M4 D8; the flakes re-placed unless the soak explains them |
| auto-buff | the driver judged beyond `rebuffed=yes`; the lane guard and a held lane | item 1 (the driver through `CastAttempt`; `forced=no` judged) |
| the caster and the harm-heal | `m4-caster-value`, `m4-harm-heal-resist` | item 3 (the vendor wand's value read) |
| the vendors, loot and the panels | `m4-vendor-issellable-wire`, the vendor window's unbuilt parts, `m4-loot-merge-stacks`, `m4-pickup-merge`, `m4-give-chain`, the drag rows, the examine's unbuilt lines, the container window's rows, `m4-env-panel-windows` | item 3 (`m4-vendor-issellable-wire`, the sale from the wire); the rest re-placed M6 (the examine, the drag, the panels' second tranche) |
| the stand-in | the `m4-standin-*` rows | re-placed: a stand-in row closes only when a play-check needs it |
| combat visuals, world sound, chat | `g5-object-lights`, `interiors-light-reach`, `particle-cell-static-lights`, `m3-admin-environs`, `m3-environs-fog`, `ambient-seen-outside`, `m3-ambient-judge`, `g4-chat-*` | item 6 (`g4-chat-focus` with the slash commands); the rest re-placed |

**New in M5** (from the audits; each opens as a code TODO in the slice that owns it, or stays a row until placed):

| Area | Gap | TODO | Placed |
|---|---|---|---|
| the objects | the client never drops an object the server has stopped sending (no client-side cull); a den reloaded after 5 minutes fills with ghosts; the light table fills at 256; ACE reuses guids after 6 h | `m5-object-cull` | item 0 c1 |
| the wayfinder | `NavRunner.Steer` throws every frame on a one-waypoint route; the last gate's melee journey wrote 8,642 traces | `m5-nav-one-waypoint` | item 0 c2 |
| the checks | smoke counts Lifestone's ERROR lines only; Godot's own errors and a runner's exceptions pass unseen; the summaries are written only at exit | `m5-godot-errors-uncounted`, `m5-summary-at-exit` | item 0 c2, c3 |
| the hosts | memory 3 to 5 times the 400 MB target and unmeasured over time; three copies of every log line; the frame rate uncapped; recording always on headless | `m5-memory-soak`, `m5-log-triple`, `m5-fps-uncapped`, `m5-recording-growth` | item 0 c3 (the measures, the log, the cap); item 7 (the fixes) |
| the session | a dropped session is never recovered; the client's 20 s silence watchdog against ACE's 60 s | `m5-reconnect-auto` | item 4 |
| the fight loop | the killed list grows by one guid a kill and is scanned per pick; the mana wait; the level chosen once; a walk failure ends the run; `--fight-seconds` ends as a failure; the kill cap 1,000 | `m5-fight-killed-growth`, `m5-fight-mana`, `m5-fight-level-rechoice`, `m5-fight-walk-recover`, `m5-fight-clean-stop` | items 1 F1, 2 |
| the casts | every automated cast is sent as `Host`, so the kill switch does not stop a buff pass, a keeper cast or a recall | `m5-cast-source-host` | item 1 K3, K4 |
| the mage's economy | the mage loots, sells, buys and spends nothing; the kit is loot-only (the wand, the helm, the coat, the leggings) and 17,000 burden; only the Foci of Strife is held; Platinum and Pyreal Scarabs are not sold in Holtburg; `ComponentLoss` below level VII and the travel spells' formulas unread; the den's loot value unknown | `m5-mage-loot`, `m5-mage-resupply`, `m5-mage-foci`, `m5-platinum-stock`, `m5-component-loss-vi`, `m5-den-loot-value` | item 0 (the measures), item 3 |
| the den | the respawn delay unmeasured (the row says 60 s, the generator's name 15 minutes); the storeys' connectivity unknown; the north wing's caster damage unmeasured; a level-80 mage's War Magic estimated | `m5-den-respawn`, `m5-den-storeys`, `m5-den-wing-damage`, `m5-mage-80-skill` | item 0 |
| the wire | three events arrive unknown at every login (SetTurbineChatChannels 0x0295, CharacterTitle 0x0029, FriendsListUpdate 0x0021); the contract tracker table once a contract is held | `m5-login-unknown-events` | item 6 decodes and ignores them with a name; the panels M6 |
| the world | doors, the lifestone landing, the creature flicker (`TODO(hero-creature-flicker)`) | `d25-server-object-host` | item 5 |

**Item 0's closures and new rows (2026-10-25).** Closed: `m5-object-cull` (c1 and fix 0-R1: outdoors the 3x3 landblocks,
in a dungeon each cell's visible-cell list, as ACE), `m5-nav-one-waypoint` (c2), `m5-godot-errors-uncounted` (c5 and fix
0-R2: smoke, slice-check, the gate and the soak line count Godot's own errors, Lifestone's echoed errors excluded),
`m5-den-respawn`, `m5-den-wing-damage`, `m5-mage-80-skill` (§8). Partly: `m5-summary-at-exit` (the `[soak]` line is
periodic; the exit summaries stay), `m5-memory-soak` (the line and `tools/soak-watch.ps1` measure; the fixes are item 7's),
`m5-log-triple` (`godot.log` off by default, `--log-copies=file` for the soak; rotation item 7's), `m5-fps-uncapped` (the
gate's journeys at 60; the soak item 7's). New:

| Area | Gap | TODO | Placed |
|---|---|---|---|
| the den | the loop stands on the arrival floor; the floor is respawn-bound (17 Slaves, a slot every 903-955 s); D25: the whole den and the timed return | `m5-den-roam-respawn` | item 2 |
| the fight loop | the mana order is the ceiling wait; D27: the dance (fight to near zero, pound Stamina to Mana Self, transfer before stamina's maximum); D26: VI at level 80 | `m5-fight-mana-dance`, `m5-level80-vi` | item 1 K5, F1; items 1-3 (the level rule) |
| the hunt | the archer's forest band can be empty within 35 m of all eight cells (M4's `m4-hunt-band-empty`; failed twice capped in item 0, D28) | `m5-hunt-band-empty` | item 2 (the hunt profiles) |
| the soak line | `kills= deaths= cycles=` read n/a until the autopilot | `m5-soak-tallies` | item 4 |
| the testing loop | `GameSpellPageTests.The_controllers_are_built_in_staged_units_under_8_ms` flakes under load (45 ms twice) | `m5-spellpage-timing-flake` | item 7 (the wall-clock tests) |
| the measures | the den loot's sale value, `ComponentLoss` below VII, the travel spells' formulas; Ring of Fire on a cluster needs a pull (tuskers came only after the SpellTable's aggro) | `m5-den-loot-value`, `m5-component-loss-vi` (open) | item 3; item 2 (the pull) |
