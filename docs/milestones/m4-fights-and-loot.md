# Milestone 4: fights and loot

**Status: draft (2026-10-17), not planned.** A draft spec for the owner's lead to adopt, written from the accepted
scoping memo (Shape A, "polished combat, not automation"), the ACE research pack, the testing-loop measurements and
the independent review of M3's items 3.1 and 3.2 (the lead files the three under `cache/m4/`). The plan of record
(the read map, the seams, the waves, the slice tables, the gates, the decisions, the "as run" record) will be
`m4-plan.md`; the local planner writes it. M3 ([m3-lights-and-sound.md](m3-lights-and-sound.md)) is complete
(2026-10-19; its item 4.2 was the end, with the polish pass, D39-D43; the milestone is "fights and sound" in prose and
the file names stay). M2
([m2-log-in.md](m2-log-in.md)) is complete on master.

## 1. Goal

Polished melee and missile combat near Holtburg, on the bundled ACE, by a legal fighter character, driven by test
loops on the Action API rather than by a person playing, and shown in the retail UI:

- **A fighter who fights.** The fighter selects a creature, swings or shoots with the power bar, sees the target's
  health meter fall, takes hits, kills it, and the combat lines reach the chat. Melee 0x0008 and missile 0x000A land
  on the Action API and in the combat window; no magic is added (M3 owns it).
- **Loot.** The fighter opens the corpse, picks up what it holds, splits a stack, drops what it does not want, gives
  an item; its side packs show their contents; a chest opens the same way. The death loop recovers the mage's own
  corpse (§6.1 Q3).
- **Recovery and trade.** A healing kit on itself and a potion when hurt; in Holtburg the loot is appraised and sold
  to the shopkeeper and kits are bought from the healer, each at the price the vendor's own message predicts.
- **The testing loop, cut first.** M4's first item measures the loop (slice check, smoke, loopback) and cuts what
  the owner approves, before any combat slice runs, and pays M3's 3.1 and 3.2 review debt.
- **Auto-buff** (the cut candidate): the M3 buff cycle re-buffs before expiry on its own, in play mode, the first
  built-in automation feature of [goals.md](../goals.md) §2.

The primary consumer is the hunt loop (select, attack, heal, loot, return and sell) with the M3 death loop extended
to loot its corpse. Every capability lands on the Action API and a loop first, the retail UI second (§4), and every
step is proved by a check (§2). LAN play, PvP, fellowships, housing, trade with players and crafting are out
(§3.2). The budget the memo gives is about $1,165 against the $1,250 ceiling.

## 2. Definition of done

Each item names the check that proves it, so the audit table (§7) is mechanical. "Loopback" is a test in
`tests/Lifestone.Loopback.Tests` (live, run by the item's lead); a "leg" is a `--login-check=<leg>` verdict field
(docs/session.md §27); "play-checks" is the offline stand-in step; a "frame" is a review frame at 800x600 (M2 D57).
A new verdict field is report-only in the item that adds it and a new judge is report-only in its first gate
(CLAUDE.md, Checks); "judged" names the gate at which it is judged. Every retail behaviour cites the retail function
and its 11.6096 address where the repository's docs give one, else says "unknown" (§5); every ACE rule cites its file
at 95ef426d. M3's decisions are cited as "M3 D<n>" (m3-plan.md §7); M4's open questions are §6.1.

1. **The testing loop, measured and cut; the 3.1/3.2 debt paid.** Instrumentation first (§5.1: the stage clock
   `at_s=`, the timed test sink, `headless timing seconds=`, per-test trx durations in slice-check and smoke), then
   the cuts the owner approves (§6.1 Q5) in the measured order, each with the text test its plan names and a
   before-and-after of the same step list; and the should-fix findings and nits of the independent 3.1/3.2 review,
   as a group (§3.3), each with the test the review names. Checks: the per-cut text tests; `slice-check` before and
   after with equal outcomes; the loop's numbers before and after in §7; the review's fixes by their tests
   (`DeathScenarioTests` on ACE's blow order, a death-mid-leg test, the death frame window closed at
   `Travel == Present`); live, the lead's `login-bsd` and `login-death` after the fixes.
2. **The fighter** (§6.1 Q1) on the test account, built by an idempotent scenario and its loopback test as BSD Mage
   was (M3 DoD 2): the creation intent of §5.10, the `xp` verb to its level, the template spent, Healing trained; a
   ready fighter is only verified. Checks: `FighterBuildScenarioTests`, loopback `FighterBuildLoopbackTests`,
   verdict `fighter=ready level=<n> template=ok`.
3. **Melee on the Action API.** `Attack(target, height, power)` (0x0008) and `CancelAttack` (0x01B7) sent; 0x01A7
   AttackDone and 0x01B8 CommenceAttack decoded; the tracker of §5.2. Checks: `CombatActionCodecTests`,
   `MeleeAttackTrackerTests` over ACE's order (the success, each refusal), `StandInMeleeTests`; loopback
   `MeleeLiveTests` (one kill on the hunt ground); leg `hunt`: `kills>=1 swings>=1 damage_dealt>0` (judged from the
   item's gate: the fields exist in M3's shape).
4. **Missile on the Action API.** `MissileAttack(target, height, accuracy)` (0x000A); the held ammo's stack in the
   read model; the arrow's F745 attributed by the war-spell rule (M3 §5.4; `m3-cast-projectile-attribution` shared,
   still open) or the ammo's 0197, whichever first; the out-of-range refusal 0x0550 as a ServerError. Checks: codec
   and tracker tests, `StandInMissileTests`; loopback `MissileLiveTests` (a bow and arrows from the `item` verb, one
   hit); leg `hunt` with `--hunt-weapon=missile`: `shots>=1 hits>=1` (report-only in its first gate).
5. **Targeting and the combat UI driven by real actions.** The 01C0 pushes (one per 5 s heartbeat) move the health
   meter; the combat window's attack buttons and height send `Attack` (closes `TODO(m3-melee-attack)`); the power
   bar is begun by the attack's send, levelled while the power charges and finished on 01B8 or 01A7, never by hand
   (M3's 0.6 placeholder removed); `TODO(m3-combat-use)` closed. Whether retail moves the meter on 01B1's damage
   share is unknown: the meter follows 01C0 only, `TODO(m4-health-from-01B1)`. Checks: `GameCombatPanelTests` (a
   click sends `Attack`), `GamePowerBarTests` (begun by the action), play-checks on the stand-in's fighting tusker,
   frames `c1-combat` and `c1-powerbar` of a real attack, a live frame of the target's meter lower than at selection.
6. **Containers.** GE 0196 and 0052 decoded; GA 0195 sent; `OpenContainer(guid)` and `CloseContainer(guid)` with the
   tracker of §5.4; a `ViewContents` model (order, foci slot) that fills `PlayerInventory.SidePacks` at login and on
   a pack pickup (closes `TODO(m3-inventory-sidepack-contents)` and its two siblings); a corpse's and a chest's
   contents in the read model; the refusals as Refused with the 02EB text. Checks: codec and reducer tests (ACE's
   bundle order, the 0022-before-F745 tolerance), `ContainerTrackerTests`, play-checks (a stand-in corpse opened),
   loopback `ContainerLiveTests` (a side pack at login; the fighter's own kill's corpse; one chest); leg `hunt`:
   `corpses_opened>=1`.
7. **Item moves.** `PickUp` (0x0019 from the ground or an open container: `MoveToPack` loses its held-item guard,
   `TODO(m3-pickup)` closed), `Drop` 0x001B, `SplitToContainer` 0x0055, `SplitTo3D` 0x0056, `SplitToWield` 0x019B,
   `Give` 0x00CD, with the trackers of §5.5; loot moves sent one at a time. Checks: codec tests,
   `ItemMoveTrackerTests`, play-checks; loopback `ItemMovesLiveTests` on Lifestone Throwaway (pick up a dropped item,
   drop, split a stack the `item` verb made, give to an NPC: the success on the stand-in, live ACE's refusal 028B
   0x046A by an NPC with no Give emote unless the lead finds one that accepts, §5.5).
8. **Corpse looting in the loops.** A loot rule (`LootRules`: every item whose Value is above 0 and whose ItemType
   the shopkeeper buys, until the burden limit; options) over an open corpse, reading Value and ItemType from the
   item's CreateObject description (ACE writes both under WeenieDesc flags, `W/WorldObject_Networking.cs:56-221`;
   inferred, confirmed by the first slice); the hunt loop loots its own kills; the death loop goes back for the
   mage's corpse by `place loc` before the re-gear's top-up (§6.1 Q3).
   Checks: `LootRulesTests`, `HuntScenarioTests` and `DeathScenarioTests` over a scripted corpse; loopback
   `DeathLiveTests` gains `corpse_looted=yes items_recovered=<n>`; leg `hunt`: `looted>=1` (judged from the second
   gate), `bsd-death`: `corpse_looted=` (report-only in its first gate).
9. **Recovery.** `UseWithTarget(kit, self)` (0x0035) with the kit tracker of §5.7; potions by `Use` with a tracker
   that completes on the 0197 or 0024 and the UseDone; the heal and potion chat lines read; a recovery rule (a kit
   under a health share, a potion while the kit is busy or out; options). On BSD Mage, whose Healing is untrained
   (M3 D19), a kit use ends 0x04FC: proved as the refusal. Checks: codec tests, `HealTrackerTests` over ACE's
   timeline, `StandInHealTests`; loopback `HealLiveTests` (the fighter after hits, a Handy Healing Kit 628 from the
   `item` verb; the mage's 0x04FC); leg `hunt`: `heals>=1` (judged from the second gate), `kit_uses= potions=`.
10. **The hunt loop near Holtburg.** `--scenario=hunt` and the leg `hunt`: the fighter bound at Holtburg's lifestone
    (M3 D34's way), teleported by `place loc` to a hunt ground row (`HuntGround`: the Low B belt or the Low North
    Forest Mix, §5.9, each a level band, a creature-type list and the camps' cells), the nearest creature of the band
    selected, attacked until it dies, the fighter healed by the recovery rule, the corpse looted, on to the next
    until a kill count or a time, then back to Holtburg by `place` before any log off (the 3.1/3.2 lesson: no loop
    leaves its character on the ground); the sell at the end (item 12). Checks: `HuntScenarioTests` on the stand-in
    (its tusker fights back since M3 3.2), loopback `HuntLiveTests`; leg `hunt` judged `ok=true kills>=1
    damage_dealt>0`, then `looted>=1 heals>=1` from the second gate, with `damage_taken= hits_taken= evades=
    xp_gained=` reported; smoke step `login-hunt` under `-Backend require`.
11. **Appraisal.** `Identify(guid)` (0x00C8) and GE 00C9 decoded into an `Appraisal` model (success 0 is a result,
    not an error). Checks: `AppraiseCodecTests` over ACE's layout section by section, `IdentifyTrackerTests` (the 5 s
    cooldown on a failed creature roll), play-checks; loopback `AppraiseLiveTests` (a kit, a weapon, a creature);
    `appraised=<n>` reported.
12. **Vendors.** GE 0062 decoded into a `VendorStock` model (the rates, the type mask, each entry's WeenieDesc through
    `WeenieDescCodec`, the predicted price per stack); `Buy` 0x005F and `Sell` 0x0060 with the trackers of §5.8.
    Checks: codec tests, `VendorTrackerTests`, play-checks; loopback `VendorLiveTests` (Thelnoth Cort opened, one
    Handy Healing Kit bought at the predicted price, the hunt's loot sold to Renald the Elder); leg `hunt`: `sold>=1
    pyreals=<delta>` (report-only in the first gate), `bought=` reported.
13. **The retail panels with content**, read-only over the read models with actions through the Action API (M3 D13;
    drag per §6.1 Q2): the combat window and power bar real (item 5); inventory 0x10000023 and backpack 0x10000022
    with side-pack contents; a container window for a corpse or chest; the vendor panel with buy and sell; the
    examine panel over `Appraisal` (§6.1 Q4); everything else answers `NotInM4`. Checks: client tests per panel,
    play-checks, a frame per panel, `--play-panel-shots` live on the fighter.
14. **Auto-buff** (the cut candidate): a re-buff driver in play mode runs M3's `BuffCycle` on the session clock
    (closes `TODO(m3-buff-rebuff-driver)`), under a toggle and the `--buff-*` options; the first automation feature,
    acting only through `IActionApi`. Checks: `BuffCycleTests` on the fake clock (the driver's schedule),
    `RebuffDriverTests`; live, the `buff` leg with the driver on: `rebuffed=yes forced=no` from the margin of the
    shortest line (the real 1,800 s expiry stays Soak-tier, M3 D26).
15. **Tests, gates and frame time.** `dotnet test` green with a non-decreasing count (7,780 at M3 3.2); the loopback
    suite; the smoke steps; no frame over 50 ms from Enter to walking (M2 DoD 15 kept) nor in an attack, a
    container's F745 burst or a vendor open, judged on the worst frame of each window: `attack_frame_max_ms`,
    `loot_frame_max_ms`, `vendor_frame_max_ms` (report-only in their first gate, judged against 50 ms from the next);
    M3's fight and death frame fields kept as they stand.
16. **The end:** the audit table (§7) one row per item; the doc pass that retitles M3 "fights and sound" in prose
    (the file names stay) and points CLAUDE.md at M4; the testing-loop numbers after, beside §5.1's before.

## 3. Scope

### 3.1 In

The goal's five parts; the Action API additions (attack, cancel, missile attack, open and close a container, pick
up, drop, split, give, identify, use with target, buy, sell); the message set of §5.2-§5.8; the read models (view
contents and side packs, containers, ammo, appraisal, vendor stock, coins); the fighter and its build; the hunt
ground rows and the loot and recovery rules; the hunt loop, its leg and smoke step; the death loop's corpse
recovery; the panels of DoD 13; the re-buff driver; the testing-loop cuts the owner approves and the 3.1/3.2 fixes;
the audit table.

### 3.2 Not in M4

Named in the UI as `NotInM4` where they would show: automation beyond the loops and the re-buff driver (the AFK
fighting and looting runner, navigation profiles, metas: [automation-vision.md](../automation-vision.md), a later
milestone); player killing; fellowships and allegiances; housing, hooks and storage; trade with players (the trade
window); crafting, tinkering and salvage; LAN play (goals.md §4; every server stays on 127.0.0.1, §6.2); Void magic
and new spells; arenas beyond the two hunt grounds; lockpicking and keys; window-layout and options saves.

### 3.3 What M3 leaves to M4

M3 is complete. Its testing-loop cuts landed in M3 4.2 (D41). The open TODO rows that M3 hands to M4 are placed in
[m3-plan.md](m3-plan.md) §9 (each "Placed" reads "M4 (<area>)"); the table below is the list M4 planned from.

| From | What | Where in M4 |
|---|---|---|
| M3 2.1 (D26, the owner) | the testing loop takes very long (the loopback suite about 15 minutes, the Soak expiry 31): cut it down | done in M3 4.2 as its polish item P5 (D41: the default filter excludes Exhaustive, the loopback suite 31 tests in 20.7 min, the full smoke 19.2 min); item 1.1 (DoD 1) is answered there |
| M3 2.1 review R8 | the session's `[net]` lines log the account name at login, in gitignored logs only | item 1.1 with the loop note |
| the independent 3.1/3.2 review (2026-10-17) | no blocker; nine should-fix findings (a report-only field that does not read what it names, four robustness defects that fail a live leg or leave the mage in the den, two verdict fields that can misread, a test pinning an order ACE does not use, two dormant defects) and nine nits | item 1.1 (DoD 1), as a group |
| M3 3.2 (`GameCombatPanel.cs:369`) | the combat window's attack buttons answer `NotInM3`, `TODO(m3-melee-attack)`; the power bar is begun by hand | DoD 3, 5 |
| M3 1.2 (`IActionApi.cs:206`) | `MoveToPack` refuses an item the player does not hold, `TODO(m3-pickup)` | DoD 7 |
| M3 2.3 (`GameInventoryPage.cs:244`, `LoginCompleteTracker.cs:28`, `RecordedDescription.cs:27`) | side-pack contents unknown without 0196 | DoD 6 |
| M3 1.2 s4b | a projectile's caster is not on the wire, `m3-cast-projectile-attribution` | DoD 4 shares the rule; stays open |
| M3 2.1 review R10 | nothing drives the timed re-buff, `m3-buff-rebuff-driver` | DoD 14 |
| M3 D8 | pick up, drop, split and appraise are `NotInM3` | DoD 7, 11 |
| M3 3.2 (`GameToolbar.cs:21`) | Examination opens `NotInM3` | DoD 13 (Q4) |

## 4. The automation-first order

Every capability lands in M3's order (m3-lights-and-sound.md §4), and a wave's gate reads it in this order: the wire
(codec, dispatcher, `IAcSession`), cited to ACE's writer and the retail dispatcher where known; the read model and the
event; the action and its tracker, with the stand-in's answer; the headless scenario and its loopback test (live,
the lead); the leg with judged verdict fields and the smoke step; the retail UI, read-only over the read model,
acting only through the Action API; the frame at 800x600. One rule is M4's own: **the testing loop is measured
before it is cut and cut before any combat slice runs**, so every later gate pays the shorter loop.

The memo's four waves, which the planner slices at each boundary (M3 P8): **1.1** the testing-loop cuts and the
3.1/3.2 fixes, instrumentation first; **1.2** melee and missile attacks with the fighter (Q1); **1.3** targeting and
the combat UI (the health meter, the attack height and the power bar driven by real actions); **2.1** containers and
item moves (corpse and chest opening, pick up, drop, split, give); **2.2** inventory polish and corpse looting inside
the death loop; **3.1** recovery (kits, potions) and the hunt loop near Holtburg; **3.2** vendors (buy, sell,
appraise); **4.1** auto-buff, the cut candidate; **4.2** the end: the audit, the doc pass. About $1,165 against the
$1,250 ceiling (M3 D5's rule: a lead asks before exceeding its item's estimate by 20%). The hunt loop and the death
loop with its corpse are the acceptance tests of the milestone. Each wave's first slice reads its rows of
[docs/research/openac-m4-map.md](../research/openac-m4-map.md) before the wire.

## 5. Research (2026-10-17, read-only)

ACE is read at 95ef426d (v1.78.4816), and OpenAC at v0.1.25 @6db05a2 is the second source (read 2026-10-19,
[docs/research/openac-m4-map.md](../research/openac-m4-map.md)): a message whose OpenAC codec exists is ported under
docs/third-party/net.md's N-process and checked against ACE's writer; a semantic rule is read in OpenAC's `Runtime`
and re-expressed with ACE's cite; an OpenAC `Retail*` file is re-expressed with the retail function and address, never
pasted (foundation-openac.md §6.2). ACE citations are `<path under Source/>:<lines>`, with `W/` for
`ACE.Server/WorldObjects/`, `A/` for `ACE.Server/Network/GameAction/Actions/`, `V/` for
`ACE.Server/Network/GameEvent/Events/`, `N/` for `ACE.Server/Network/`, `E/` for `ACE.Server/Entity/`, `M/` for
`ACE.Server/Managers/`; a bare number in a paragraph is a line of the file last named. "Lifestone today" is master
aa879e2. **Every message order below is read from ACE's code, not from a capture**; the first slice of each capability
confirms it with a lead-run loopback capture (M3's precedent: the wield's 02CE and 0023 did not agree on this server,
m3-lights-and-sound.md §5.4). Two code-order caveats hold everywhere: ACE flushes one bundle per message group per
tick, UIQueue 0x09 (GEs, 02xx updates, 0024, 0197, F7E0) before SmartboxQueue 0x0A (F745, F747, F748, F74A, F74C,
F750; `N/NetworkSession.cs:186-245`, `N/GameMessageGroup.cs:14-15`), so a GE 0022 or a GM 0197 can arrive before the
F745 of the object it names and the reducer must park or tolerate that; and `ActionChain` delays are quantised to
world ticks (`E/Actions/ActionChain.cs:125-147`). Retail names and 11.6096 addresses are only those the repository's
docs already give; everything else is "unknown". No retail dat was read; every dat-derived number (animation lengths,
swing commands, hook frames) needs a dat test. The full refusal catalogues, chat texts and per-line cites are in the
research pack; this section keeps what sizes a slice.

### 5.1 The testing loop as measured (before any cut)

| What | Measured | Where |
|---|---|---|
| the default suite on a 4-core Linux container, no dats | build 117 s wall; test 30 s wall for the five projects in parallel; 967 dat tests skipped; the sum of test durations 136 s (Client 61.7, Data 39.9, Net 22.5, ServerMod 9.9, Loopback 1.8) | trx, aa879e2 |
| the same with the dats (an earlier run on 310a092) | about 7 minutes wall, four and a half of them in `Lifestone.Data.Tests` | the owner's loop pays the dat-backed half |
| the slowest logic tests | `SessionFuzzTests` seed 2 10.2 s; two terrain and scenery oracle sweeps 9.4 and 8.3 s; `OneAnswerTests` 6.3 s; `NoPersonalDetailsTests` 4.8 s; `CSharpTextTests` 3.8 s; `AvatarStressTests` 3.6 s; `PortalSpaceMachineTests` 3.6 s; nothing else over 2.2 s | none worth cutting; the dat-backed Data tests are, if tiered (`Tier=Dat` at the item gate) |
| the loopback suite | 55 logins (non-Soak; 59 with Soak, 60 with the owner-gated `ModXpLiveTests`): AcademyExit 14, BSD Mage 11, ModVerbs 8, HoltburgLaps 6, PhaseA 5, PhaseB 5, HeadlessLoopback 2, ModCharPurge 2, Throwaway 2; each `LiveNetClient` login first waits out ACE's 2 s linger plus 0.5 s (`LiveNetClient.cs:224, 236-244`); about 15 minutes in all (M3 D26) | counted in the test sources; durations unmeasured until the trx logger lands |
| play-checks Godot runs per smoke | three: `play-checks`, `record-audio` (`--play-checks --record-audio=`), `shell-frames` (`--play-checks --play-frames=`; `--play-frames` demotes every frame-time judge, so frames cannot join a judged run) | `tools/smoke.ps1:922, 934, 971` |
| the item gate | the full smoke twice on one commit (G-A `-Backend auto`, the live gate `-Backend require`), `dotnet test` twice in G-A (slice-check's test stage, then smoke's `test` step, which `-NoBuild` does not skip), `-Only login-check,login` three times; 19 backend-free Godot launches per smoke, 21 with `login-check` and `login`, 25 under `require` | m3-plan.md §5; `slice-check.ps1:148`, `smoke.ps1:779-817` |
| slice-check | one `smoke.ps1` process per step, each paying the secret scan over the out dir, every godot log dir, `cache/sessions` (unbounded, never pruned) and the ACE logs (log4net caps them at 10 and 5 files of 20 MB), `Get-StrayOutputs`, the backend probe and the module import | `slice-check.ps1:187-243`, `smoke.ps1:598-707, 770-774, 1111` |

The thirteen cuts' plans (files, change, risk, proof, saving) exist as read-only patch plans; the order the
measurements suggest is 13 (instrumentation), 10 (the steps the diff touches, one smoke process per slice-check,
G-A's duplicate `dotnet test` removed), 3 (one play-checks run for `play-checks` and `record-audio`), 11 (the secret
scan bounded to files newer than the last clean scan, a full scan at the gates and weekly, an opt-in rotation of
`cache/sessions`), 12 (the loopback idle wait 45 s to 30 s, the Academy put-back only when not at the start: 30-45 s
per run), 9 (two or three backend-free, non-frame-judged Godot steps at once). Each is what M4's item 1.1 does if the
owner approves it (Q5), not a decision. The pins every cut must keep are the text tests that read the scripts
(`BuffLegJudgesTests`, `DeathLegRunnerTests`, `FightLegRunnerTests`, `RetailUiFramesPlanTests`: slice-check keeps
`[ValidateSet('auto')]` and never names `login-buff`, `login-death`, `login-bsd`).

### 5.2 Melee and missile attacks

| Need | Message (S→C unless said) | ACE | Lifestone today |
|---|---|---|---|
| start a melee attack | GA 0x0008 {u32 target, u32 height 1 High 2 Medium 3 Low, f32 power 0..1} (C→S) | `A/GameActionTargetedMeleeAttack.cs:8-12`, `W/Player_Melee.cs:51-164` | not sent; the combat window logs `TODO(m3-melee-attack)` |
| stop | GA 0x01B7 CancelAttack, empty (C→S) | `A/GameActionCancelAttack.cs:10`, `W/Player_Melee.cs:240-250` | not sent |
| approach and swing | F74C TurnToObject or MoveToObject, then the F74C swing (the stance motion, the animation speed, the StickToObject flag and the target guid) | `W/Player_Melee.cs:180-211, 398-430`, `W/Creature_Navigation.cs:127-175`, `W/WorldObject_Networking.cs:1306-1325, 1376-1385` | decoded with the sticky guid, played by the motion table |
| strike result | GE 01B1 {String16L defender, u32 damage type, f64 damage over max health, u32 damage, u32 crit, u64 conditions}; GE 01B3 {defender}; F750 and F755 on the target | `W/Player_Combat.cs:117-176`, `V/GameEventAttackerNotification.cs`, `V/GameEventEvasionAttackerNotification.cs` | decoded, applied (`GameStateReducer.Combat.cs:52-92`) |
| kill | GE 01AD to the killer; 02CF twice; F74C Dead; the corpse F745, the monster's F747 | `W/Creature_Death.cs:32-138`, `W/Monster_Combat.cs:375-398`, `W/Player_Xp.cs:72, 106-107` | decoded, applied |
| attack over | GE 01A7 AttackDone {u32 WeenieError}; GE 01B8 CombatCommenceAttack (empty) | `W/Player_Melee.cs:226, 289, 378`, `V/GameEventAttackDone.cs`, `V/GameEventCombatCommenceAttack.cs` | **not decoded**: the dispatcher's default returns Unknown (`InboundDispatcher.cs:632`) |
| refusals | GE 028A {u32 code}, 028B {u32, String16L}, 02EB {String16L}, F7E0 | `W/Player_Networking.cs:423-436` | decoded into chat lines (kind WeenieError with the code) |
| start a missile attack | GA 0x000A {u32 target, u32 height, f32 accuracy 0..1} (C→S) | `A/GameActionTargetedMissileAttack.cs:9-13`, `W/Player_Missile.cs:40-139` | not sent |
| the arrow | F745 (velocity, Missile state), 02CE PlayerKillerStatus, F755 Launch; hit 01B1 or 01B3, F750 Collision, F747; miss F7E0 type 0 | `W/Creature_Missile.cs:80-143, 352-387`, `W/ProjectileCollisionHelper.cs:17-155` | F745, F755, F747 decoded; attribution exists for spells only |
| ammo | F74A (the held ammo hidden), 0197 and 02CD; the last one: 02DA, 02CE 0, F74A, F750, F747 (no 0024) | `W/Player_Missile.cs:329-344`, `W/Player_Inventory.cs:117-147, 396-455` | decoded, applied (M3 1.2 s2) |

- **Melee refusals, in code order** (`W/Player_Melee.cs:55-131, 204-226`): the mode not Melee, 01A7 only; busy,
  teleporting or a suicide, 028A 0x001D then 01A7; jumping 0x04EB and a PK logout 0x04CC, each then 01A7; a sequence
  already running, no answer, the power queued (`E/AttackQueue.cs:24-40`); a missing, non-creature or dead target,
  01A7 only; a target that cannot be damaged, 02EB "You cannot attack X" then 01A7; a failed approach (15 s cap),
  01A7 only; every 01A7 above carries 0x0036. There is no melee "out of range": the server walks the player
  (`W/Player_Move.cs:16, 37-125`); no weapon is needed.
- **Melee success** (`W/Player_Melee.cs:144-174, 255-383`): the refill wait, the turn or walk, the swing F74C (GE
  01B8 first on repeat swings), 02E9 Stamina, then at each attack-hook frame `DamageTarget` (`W/Player_Combat.cs:
  117-192`): 01B1 (with F750 and F755) to the attacker only, or 01B3; **a lethal blow sends no 01B1**: 01AD at once,
  the 02CF pair queued on the player's own queue so it follows (`W/Player_Xp.cs:72`), F74C Dead, then the corpse's
  F745 and the monster's F747 after the Dead length. The swing's length is the player's motion table's at the
  Quickness-and-weapon speed (`W/Creature_Combat.cs:503-521`); then `NextRefillTime = now + power × (1.0 or 0.8)` s.
  With `AutoRepeatAttacks` on and the target alive and in reach: 01A7 {0} and the next swing; otherwise 01A7
  {0x0036}, after which the next 0x0008 is a new attack. 0x01B7 mid-swing lets the swing finish, then 01A7 {0x0036}.
- **Missile** (`W/Player_Missile.cs:44-304`): refusals as melee (mode Missile), plus no launcher or no equipped ammo
  (01A7 only), out of range (`dist > min(v² × 0.10204, 77.7 m)`, `W/Creature_Missile.cs:406-433`: 028A 0x0550 then
  01A7) and entering Missile mode with no ammo (02EB "You are out of ammunition!" and NonCombat,
  `W/Player_Combat.cs:843-856`). Success: the turn, the aim F74C (`W/Creature_Missile.cs:236-347, 435-472`), at the
  launch tick the F750 release sound, 02E9, the arrow's F745 (when the landblock tracks it, `W/Player_Tracking.cs:
  51-62`), 02CE, F755 Launch, the ammo update; then F74C Reload and Ready, F749, GE 01A7 {0} and the next shot after
  `AccuracyLevel` s with repeat on, else 01A7 {0x0036}. Nothing is sent in flight: the client integrates the F745
  velocity (`MaximumVelocity` default 20 m/s, ×1.2 with `UseFastMissiles`, `W/Creature_Missile.cs:210-234`); the
  impact (`DamageTarget`, or F7E0 "Your missile attack hit the environment." and F747) normally follows the 01A7.
- **Trackers (proposal).** Melee: its own lane, one in flight; Confirmed on the first of an own-guid swing F74C after
  the send or a 01B1, 01B3 or 01AD naming the target (01B1 and 01B3 can be squelched, `W/Player_Combat.cs:150, 162`);
  ServerError on 028A 0x001D, 0x04EB, 0x04CC, a 028B, or a 02EB "You cannot attack" seen before a start, resolved on
  the 01A7 that follows; a 01A7 with no start and no error is a silent refusal; 01A7 {0} is a swing over with a
  repeat pending, 01A7 {0x0036} the sequence over; timeout 20 s from the send to the start (`ActionPolicy.UseSeconds`).
  Missile: Confirmed on the arrow's F745 (Missile state, within 5 m of the player, velocity away, within 60 degrees
  of the aim, wearing the wielded ammo's Setup: the war-spell rule) or the ammo's 0197 or F747, whichever first;
  ServerError on 028A 0x0550, 0x03F9, 0x001D, 0x04EB, 0x04CC or a 02EB; the sequence ends on 01A7; 10 s.
- **Retail:** `ClientCombatSystem::HandleAttackerNotificationEvent` 0x0056C160, `HandleDefenderNotificationEvent`
  0x0056D670, `EndAttackRequest` 0x0056CE30 (the attack click), `HandleCommenceAttackEvent` 0x0056BA60 and
  `AttemptStartBuildingAttack` 0x0056C850 (the power bar begins), `HidePowerBar` 0x0056B5F0, `ToggleCombatMode`
  0x0056D610; the sender of 0x0008, 0x000A and 0x01B7 and the handlers of 01A7 and 01AD: unknown. **Gaps:** the dat
  values (the `CombatTable` swing commands, the swing and aim lengths, the attack hook frames, the arrow weenies'
  `MaximumVelocity` and physics state) need dat tests or a read-only world query; the options AutoRepeatAttacks,
  UseChargeAttack, UseFastMissiles and LeadMissileTargets steer the flow server-side and Lifestone's
  `CharacterOptions` overrides never reach ACE (0x01A1 not sent; ACE's default for a new character is not in the C#
  source); the order across ticks (02CF after 01AD, the arrow's F745 tick, the flight time) is inferred;
  `CombatEvents.cs:107` ("ACE sends it for player kills only") reads wrongly: 01AD goes to any player landing the
  last damage (`W/Creature_Death.cs:68-76`).

### 5.3 Targeting and health

No selection message exists (`N/GameAction/GameActionType.cs` has QueryHealth 0x01BF and no select); ACE keeps a
selection only as a side effect of QueryHealth (`W/Player.cs:416-437`), and guid 0, a non-creature or a guid outside
the player's landblock clears it and gets no answer (`W/Player.cs:393-409`). Layouts: GA 0x01BF {u32 guid}; GE 01C0
{u32 guid, f32 current over max}, sent at once (`W/Player.cs:413`). Pushes without a query, exactly two: one 01C0 per
heartbeat (`HeartbeatInterval` 5.0 s, `W/WorldObject_Tick.cs:44-48`) for the selected creature while its health is
above 0 (`W/Player_Tick.cs:112-118`, `W/Player_Vitals.cs:164-174`), and `Creature.OnHealthUpdate` to everyone who
selected a player, only at that player's respawn (`W/Creature.cs:369-380`, `W/Player_Death.cs:282`). Not pushed
after the player's own hit, in the monster's `TakeDamage`, on a death or on a selection change: a hit reaches the
meter up to about 5 s late; 01B1's f64 damage share would let a client move it sooner, and retail's use of it is
unknown (DoD 5's TODO). **Retail:** `gmToolbarUI::HandleSelectionChanged` 0x004C0010 sends one QueryHealth for an
attackable object (`CM_Combat::Event_QueryHealth` 0x006AAAA0) and QueryHealth 0 when the meter was showing; the
answer reaches `RecvNotice_UpdateObjectHealth` 0x004BDDA0; `ObjectIsAttackable` 0x0056B340; a periodic retail
re-query is not found. **Lifestone:** `IActionApi.QueryHealth` (lane Query, 4 a second, 5 s timeout, Confirmed on the
01C0 for that guid); the reducer stores every 01C0, pushes included (`GameStateReducer.Chat.cs:90-100`);
`GameSelection.cs` sends the query on selection (`SelectionRules.QueriesHealth`, `TODO(g5b-attackable)`). Nothing on
the wire is missing; M4 adds the meter's follow of the pushes and the loops' target choice
(`WorldObjectTable.NearestCreature`, docs/session.md §31).

### 5.4 Containers: corpses, chests, side packs

| Need | Message and direction | ACE | Lifestone today |
|---|---|---|---|
| open a corpse or chest | C→S GA 0x0036 Use {guid}; S→C [chest: F74C On], GE 0196 ViewContents × (1 + sub-packs), F745 per item, GE 01C7 UseDone 0 | `W/Player_Use.cs:176-250`, `W/Container.cs:695-809` | Use sent; UseDone, 02EB, F745 decoded; 0196 falls to Unknown (`InboundDispatcher.cs:632-634`) |
| close | C→S 0x0036 again, or GA 0x0195 {guid}; S→C GE 0052 CloseGroundContainer {guid} | `W/Container.cs:716-721, 831-875`, `A/GameActionNoLongerViewingContents.cs:6-12`, `V/GameEventCloseGroundContainer.cs:9-13` | no 0195, no 0052 |
| refused, locked, in use | GE 02EB text (and F750 0x94 on a locked chest), then UseDone 0; no WeenieError | `W/Corpse.cs:123-137`, `W/Chest.cs:96-167`, `W/Container.cs:727-742` | 02EB decoded |
| own side packs | F745 pack, GE 0196 {pack, entries}, F745 per content (at login and on a pack pickup) | `W/Player_Networking.cs:242-263`, `W/Player_Inventory.cs:97-103, 1081-1087` | 0196 not decoded; contents adopted from the F745 container field during the arrival only (`GameStateReducer.World.cs:663`) |

- **Open and close.** `TryUseItem` (`W/Player_Use.cs:176-243`) always sends UseDone 0 after `LastUseTime`, also
  when the 15 s walk failed: **UseDone 0 is not a success signal.** Success (`W/Container.cs:744-809`): an earlier
  open container is closed first (its 0052), [a chest broadcasts F74C On], GE 0196 for the container and one per
  sub-container, F745 for every item one level down, then UseDone 0. The 0196 (`V/GameEventViewContents.cs:13-26`):
  u32 container, u32 count, count × {u32 item, u32 ContainerType 0 NonContainer, 1 Container, 2 Foci}, ordered by
  PlacementPosition, no placement field. The close (`W/Container.cs:853-875`) sends 0052 to the viewer only, at once
  for a corpse, after half the Off animation for a chest; no F747 for the contents, so the client keeps stale item
  objects and a re-open resends them; never by distance; on a second Use, on 0195, at logout, when the player picks
  the open container up, and on a reset (a chest relocks with a broadcast GM 02D2 Locked, `W/Chest.cs:247-252`).
- **Permission and side packs.** `Corpse.HasPermission` (`W/Corpse.cs:152-199`): the own corpse or a null victim;
  the killer, or `IsLooted`; a permit holder; a monster corpse once its time to rot is under 180 s and no rare
  dropped; any close sets `IsLooted`; empty corpses rot within 15 s (`W/WorldObject_Decay.cs:75-81`). Single player:
  the own kill or own corpse always passes. Taking from a closed container finds nothing (`W/Player_Inventory.cs:
  558-581`): 02EB "Source item not found!" and 00A0. Side packs have no request: at login
  `SendInventoryAndWieldedItems` sends per main-pack item F745, then for a container 0196 and F745 per content, then
  the equipped items (`W/Player_Networking.cs:214-263`), so slot order and the foci type come from 0196.
- **Trackers (proposal).** OpenContainer: send Use; complete on the 0196 for that guid, then wait (cap about 3 s, as
  `LoginCompleteContentsCapSeconds`) until every listed guid has an object; fail when the UseDone arrives with no
  0196, the reason the 02EB text or the F750 0x94 seen since the send; timeout 20 s. CloseContainer: complete on the
  0052 {guid}; 5 s plus half a chest animation. **Retail:** `CM_Inventory::Event_UseEvent` 0x006AD310,
  `CM_Item::DispatchUI_UseDone` 0x006A9470; what retail's container window sends on close and the handlers of 0196
  and 0052: unknown. **Gaps:** a player corpse's lifetime (the death loop's trip back depends on it: `W/Corpse.cs` at
  the pinned commit, not read here); the server properties the pack names are `M/PropertyManager.cs` defaults and
  the running shard may differ (a read-only SELECT).

### 5.5 Item moves: pick up, drop, split, give

| Need | Message and direction | ACE | Lifestone today |
|---|---|---|---|
| pick up, loot | C→S 0x0019 {u32 item, u32 container, i32 placement}; S→C F748 + F74C, 02DA, GE 0022, 02CD, F750, F74A | `W/Player_Inventory.cs:790-1363` | sent and decoded; `MoveToPack` refuses un-held items (`InventoryTrackers.cs:117-118`) |
| drop | C→S 0x001B {u32 item}; S→C F748 + F74C, 02DA, 02CD, 02DA, GE 019A, F748, F750, F74C | `W/Player_Inventory.cs:1371-1514` | answers decoded; no sender |
| split | C→S 0x0055 {stack, container, place, amount} / 0x0056 {stack, amount} / 0x019B {stack, EquipMask, amount}; S→C F745, 0022, 0197 / 0197, 02CD, F750 / 0197 and the wield set | `W/Player_Inventory.cs:2243-2830` | answers decoded; no sender |
| give | C→S 0x00CD {u32 target, u32 item, i32 amount}; S→C 0024 (or 0197), 02CD, 0022, chat, F750 0x8E, F747 | `W/Player_Inventory.cs:3190-3622` | 0024, 0022, 0197, 028B decoded; no sender |

- **Pickup** (`W/Player_Inventory.cs:790-1363`). Refusals, each ending in GE 00A0 {u32 item, u32 code}: busy, 028A
  0x001D then 00A0 code 0; not found, 02EB "Source item not found!"; not dynamic or Stuck, 028A 0x0029; burden, 02EB
  "You are too encumbered to carry that!"; a failed walk, a cancel or a teleport, 0x0036; the main pack full, 02EB
  (no spill to side packs). Success: after the arrival the pickup motion is chosen by the item's height, broadcast as
  F748 and F74C, and the chain waits its length from the player's motion table (dat data), then the return-stance
  F74C and the same length again busy: about two animation lengths per move. A ground item's F74A is queued as an
  action to every viewer (`E/Landblock.cs:940-983`), so it lands a tick after the rest; then GM 02DA {Container}, GE
  0022 {item, container, placement, type}, 02CD EncumbranceVal [a coin: 02CD CoinValue] [a pack: 0196 and F745 per
  content], broadcast F750 0x8F; corpse and chest items skip the F74A; a move inside the player sends only 02DA and
  0022. `TryAddToInventory` shifts later items' PlacementPosition by one without telling the client
  (`W/Container.cs:558-570`): insert and shift locally.
- **Drop, split, give.** Drop (1371-1514): busy 028A 0x001D and 00A0; attuned 00A0 0x0426; F748 + F74C Pickup, the
  animation, 02DA Container 0, 02CD, `TryDropItem` (1.1 m in front, F74B broadcast), 02DA again, GE 019A {item}, F748
  of the item, F750 0x90, the return F74C; whether the dropper then gets an F745 is not verified. Split (2243-2830):
  each check a 02EB text and 00A0; to a container F745 (the new guid) then 0022 then GM 0197 of the old stack; to 3D
  0197, 02CD, the drop of the new stack, no 019A, no 0022; to wield 0197 then the wield sequence. Give (3190-3478):
  to a player the receiver's AllowGive option decides, there is no accept dialog; the giver gets 0024 {item}, 02CD,
  GE 0022 {item, container = target}, the chat "You give <target> <item>.", F747; an NPC needs a Give emote or
  `AiAcceptEverything`, else 028B {0x046A, npc} and 00A0 (a refuse emote: 00A0 0x04CD).
- **Trackers (proposal).** Pickup or loot: complete on the 0022 {item, container == requested}; fail on 00A0 {item}
  (the code is 0 for most, the preceding 028A or 02EB is the reason); timeout about 25 s (15 s walk, two animation
  lengths, a margin; `InventorySeconds` 5 s fits only moves without a walk); loot moves one at a time, the next after
  the 0022 and the animation. Drop: the 019A {item}. Split: the 0022 for the new guid (to a container), the 0197 of
  the old stack (to 3D). Give: the 0022 naming the target, or the 0024 for the item; a 0197 for a partial stack.
  **Retail:** `CM_Inventory::Event_PutItemInContainer` 0x006ACC80, `Event_StackableMerge` 0x006ACDD0,
  `CPlayerSystem::PlaceInBackpack` 0x0055E5E0, `SmartBox::HandlePickupEvent` 0x00453570, `DoPickupEvent` 0x00452280,
  `ACCWeenieObject::ServerSaysContainID` 0x0058CC70 (0022), `ServerSaysSetStackSize` 0x0058DA70 (0197),
  `CPlayerSystem::RecvNotice_ServerSaysMoveItem` 0x00564000 (019A), `RecvNotice_ServerSaysAttemptFailed` 0x0055F1B0
  (00A0); the senders of 0x001B, 0x0055, 0x0056, 0x019B and 0x00CD: unknown. **Gaps:** the F74A arriving a tick late
  and the dropper's F745 are read from code; the pickup seconds are dat-derived, so the timeout is a bound; a
  Holtburg NPC with a Give emote is unknown (a read-only SELECT of the emote tables before 2.1 is sliced); the
  reducer must tolerate a 0022 or 0197 before the F745 and the giver's 0022 naming a foreign container.

### 5.6 Appraisal

Request GA 0x00C8 {u32 guid} (`A/GameActionIdentifyObject.cs:9-11`); guid 0 clears the target with no reply
(`W/Player.cs:256-263`); no range or busy check, `FindObject(Everywhere)` covers the pack, equipped, the landblock,
the last-opened container or vendor stock, wielded by others, trade and known objects (`W/Player.cs:265`); not found
gives GE 00C9 {guid, flags 0, success 0}. The reply (`V/GameEventIdentifyObjectResponse.cs:9-16`,
`N/Structure/AppraiseInfo.cs:743-786`): u32 guid, u32 flags (`ACE.Entity/Enum/IdentifyResponseFlags.cs:8-24`), u32
success, then by flag bit in ACE's fixed order the Int, Int64, Bool, Float, String and DID tables (u16 count, u16
buckets, count × {u32 key, value} sorted by key % buckets then key), the SpellBook (item enchantments or'ed
0x80000000), the Armor (8 × f32), Creature, Weapon and Hook profiles, three u16-pair bitfields and the nine
ArmorLevels. Only `[AssessmentProperty]` ids are sent (`AppraiseInfo.cs:352-358`). The skill check (`W/Player.cs:
302-345`): only creatures roll (Assess Person or Assess Creature against Deception); items never roll; on a failure
the tables are still sent in full and the profiles, item enchantments, creature attributes, armour levels and
ratings are withheld (`AppraiseInfo.cs:334-345, 556-597`). Cooldown (`W/Player.cs:274-299`): a repeat on the current
successful target succeeds with no roll; a repeat within 5 s of a failed request on the same target fails with no
roll. Nothing is unsolicited. **Tracker:** Identify(guid) completes on the next 00C9 with that guid; success 0 is a
result; 5 s. **Lifestone has nothing** (M3 D8), but `PlayerDescriptionCodec.cs:273-423` has private readers for the
same table shapes and `WireReader.TryReadString16L` exists. **Retail:** `ClientUISystem::ExamineObject` (no address);
the sender of 0x00C8 and the handler of 00C9: unknown. **Gaps:** the full `[AssessmentProperty]` list and
`SkillCheck.GetSkillChance` were not read; how retail's panel shows a failed assess is unknown.

### 5.7 Healing kits and potions

- **Kits.** 0x0035 UseWithTarget {u32 kit, u32 target} (`A/GameActionUseWithTarget.cs:10-15`; `W/Player_Use.cs:
  29-170`): a kit not held or a missing target is a silent UseDone 0; a type mismatch 02EB and UseDone 0; self needs
  no walk. Healer refusals (`W/Healer.cs:49-127`), each ending the use: Healing below Trained UseDone 0x04FC; busy,
  teleporting or a suicide 0x001D; a non-player target 0x04FE; jumping 0x04EB; a full vital 028B 0x04FF (Health) or
  02EB (stamina, mana), then UseDone 0; the vital is the kit's BoosterEnum (PropertyInt 89). Timeline (121-171):
  IsBusy; F74C at t0 (SkillHealSelf 0x1000010E or SkillHealOther 0x1000010F); at t0 + the motion's length (dat) in
  one step `DoHealing`, IsBusy off, UseDone 0; then F74C Ready. `DoHealing` (173-297): a use is spent first, win or
  lose (Structure, PropertyInt 92); the roll is `1/(1+e^(−0.03 (eff − diff)))` with eff = round((Healing +
  BoostValue) × 1.5 specialised else 1.1) and diff = round(missing × 2 × (1.0 in NonCombat, else 1.1))
  (`W/SkillCheck.cs:7-12`); a failure sends F7E0 "You fail to heal yourself. ..." and 02CE Structure; a success sends
  02E9 stamina, 02E9 the vital (never 02E7, `W/Player_Vitals.cs:130-139`), the proficiency 02CF, F7E0 "You [expertly
  ]heal yourself for N Health points. ...", 02CE Structure, at 0 uses 0024 and 02CD, then UseDone 0.
- **Potions and food** by Use 0x0036 (`W/Food.cs:42-105`, `W/Player_Use.cs:268-311`): a refusal is 028A (0x001D,
  0x04EB) and then UseDone 0, never UseDone ≠ 0; the chain is IsBusy, [NonCombat Ready], F74C Eat 0x4000001A or Drink
  0x4000001B, the effect (02E9 of the BoosterEnum vital, F7E0 "The {Name} restores {N} points of your {Health}.",
  F750 UseSound, then 0197 {stack − 1} or 0024 for the last and 02CD), F74C Ready, the stance restored; **the UseDone
  fires at the sum of all those lengths** (310), seconds after the effect.
- **Trackers (proposal).** Both complete on the UseDone (the Use lane, 20 s). A kit is Confirmed iff since the send
  its 02CE Structure arrived and no 028A, 028B or 02EB; a heal is the target's 02E9 rising; a spent failed roll is
  the "You fail to heal" line (its own outcome, or Confirmed with a detail: the planner's call); UseDone ≠ 0 is a
  ServerError. A potion is Confirmed iff its 0197 or 0024 arrived (a 02E9 alone is not enough at a full vital).
  **Lifestone has:** Use and UseDone; 02E9, 02CE (Structure mirrored onto a held item, `GameStateReducer.Inventory.cs:
  370-413`), 0197, 0024, the chat kinds, F750, F74C; it lacks the 0x0035 sender and every tracker. **Retail:**
  `CM_Inventory::Event_UseEvent` 0x006AD310 (Use only), `CM_Item::DispatchUI_UseDone` 0x006A9470; the UseWithTarget
  sender and the heal handlers: unknown. **Gaps:** the heal, Eat and Drink animation lengths are dat data,
  unmeasured; a kit's BoosterEnum, BoostValue and HealkitMod are world-database facts not read.

### 5.8 Vendors

| Need | Message and direction | ACE | Lifestone today |
|---|---|---|---|
| open | C→S 0x0036 Use {vendor}; S→C [F74C MoveToObject], F74C TurnToObject, GE 01C7 UseDone, GE 0062 ApproachVendor | `W/Player_Use.cs:176-241`, `W/Vendor.cs:229-298`, `V/GameEventApproachVendor.cs:14-63` | Use sent, UseDone decoded; 0062 falls to Unknown; no vendor model |
| buy | C→S 0x005F {u32 vendor, u32 n, n × {i32 amount, u32 item}}; S→C 0024 or 0197 and 02CD (the spend), per item F745, GE 0022, 02CD; F750; GE 0062; 01C7; refusal 00A0 and 01C7 | `A/GameActionBuyItems.cs:12-34`, `W/Player_Commerce.cs:22-113`, `W/Vendor.cs:431-571` | not sent |
| sell | C→S 0x0060 {the same layout}; S→C per item 02CD and GE 0022 (to the vendor), GE 0062, per coin stack F745, GE 0022, 02CD × 2, F750, 01C7 | `A/GameActionSellItems.cs:12-32`, `W/Player_Commerce.cs:126-226`, `W/Vendor.cs:601-662` | not sent |

- **Open, buy, sell.** The use radius is the vendor's `UseRadius` or 0.6 m (`W/WorldObject_Use.cs:47-61`); a failed
  walk is a bare UseDone 0; `ActOnUse` (`W/Vendor.cs:229-266`) loads the stock on first use (one world object per
  CreateList entry, their guids are what Buy names), then GE 0062 and the "Open" emote; **the 01C7 precedes the
  0062** and is the whole answer for a closed shop or a failed walk. The 0062 (`V/GameEventApproachVendor.cs:14-63`):
  u32 vendor, the type mask, min and max value, DealMagicalItems, f32 BuyPrice (the rate it pays), f32 SellPrice (the
  rate it charges), the alternate currency, u32 numItems, then per entry an i32 packed stack (0xFFFFFF unlimited) +
  u32 guid + a WeenieDesc block (`W/WorldObject_Networking.cs:56-221`; `WeenieDescCodec.TryRead` reads one, retail
  `PublicWeenieDesc::UnPack` 0x005AE520); up to about 15 KB. Prices (`W/Vendor.cs:573-609`): you pay per created
  stack `max(1, ceil(SellPrice × Value − 0.1))`; the vendor pays per item `max(1, floor(BuyPrice × Value + 0.1))`
  on the whole stack's Value; both predictable from the 0062. Buy (`W/Player_Commerce.cs:22-113`): no distance check;
  unknown guids skipped silently; the spend first (0024 or 0197, 02CD EncumbranceVal, 02CD CoinValue), then per item
  F745, GE 0022, 02CD (never merged into a held stack), F750 0x8F, the 0062 refreshed, 01C7 0. Sell (126-226): **the
  whole stack is sold and paid on its Value whatever the amount** (261, 296); per item 02CD and GE 0022 {item,
  vendor}; **the 0062 and the "Sell" emote come before the coins**; per payout stack F745, 0022, 02CD × 2; F750; 01C7
  0. No restock (`vendor_shop_uses_generator` false, `M/PropertyManager.cs:612`); the close sends no client message.
- **Refusals:** GE 00A0 with item = the player's own guid and error 0 for every vendor refusal; busy 00A0 and 01C7
  0x001D; trading 0x0506; a vendor off the landblock 0x0038; a failed validation 00A0 and 01C7 0 after a 02EB (an
  invalid amount, the burden, the pack space, an unsellable item), a 028B 0x001E or an F7E0 (the unique cap); **too
  few pyreals gives no text at all** (`W/Vendor.cs:546-552`).
- **Trackers (proposal).** Open completes on the 0062 for the vendor guid, not on the UseDone (after a UseDone 0
  allow about 1 s plus the rotate time, else Refused "vendor did not open"); UseDone ≠ 0 is a ServerError; 20 s. Buy
  and Sell complete on the UseDone: Confirmed iff no 00A0 since the send, checked by the CoinValue (int 20) delta
  against the predicted price or payout; the reason is the 02EB, 028B or F7E0 text (none for pyreals); then wait for
  the F745 of each 0022 item (they follow the UseDone, as `SpawnGear` waits); 5 s. **Lifestone has:** Use and
  `UseTracker`; 01C7, 0022 (a foreign container reads as Removed), 0024, 0197, 00A0, F745, F750, F74C; the chat
  kinds; 02CD CoinValue stored in `PlayerQualities.Ints[20]` with no reader or event; `WeenieDescCodec`; it lacks the
  0062 codec and vendor state, the 0x005F and 0x0060 senders, the trackers and a CoinValue reader. **Retail:** the
  0062, 0x005F and 0x0060 handlers and senders: unknown. **Gaps:** a vendor's UseRadius and CreateList stacks and the
  coin stack's MaxStackSize (25,000 is the stand-in's assumption) are world-database facts not read;
  UseDone-before-0062 and F745-after-UseDone are derived from the tick and queue order.

### 5.9 Holtburg's hunt grounds and vendors (the public world database, v0.9.296)

Read with SELECTs only from the public ACE-World-16PY-Patches repository at tag v0.9.296 (the release
`server/versions.json` pins) compiled on its base into a scratch database: the `encounter` rows (one per 24 m cell)
and the generators' children one and two levels down; ACE's map rule (a landblock is 192 m, a map unit 240 m, origin
−102). Holtburg's centre is 42.5N 33.6E (landblock 0xA9B4); the town's arrival point, which the Academy's exit
portal sets as the sanctuary, is cell 0xA9B40019 (84, 7.1, 94) (docs/session.md §25), and M3's death loop binds BSD
Mage at the lifestone there by a Use (M3 D34). **Nothing in the 30-60 band spawns within 4 map units of the town**:
the rings around it are Harmless (5150, level 4), Newbie (2007, level 8, 355 spots) and Low A (1996, level 8-20).

1. **The Low B belt, 4.2 map units south-west (39.0-40.2N 27.9-28.4E; generators 1999, 723 spots, and 2001).**
   Level 15-20 everywhere (Outcast Lord, Drudge Vagabond, Mudlurk Mosswart, Hunter Shreth 15; Broken Fragment, Mite
   Squire 20) and the level-30 **Tumerok Warrior (wcid 233, XP 5,000)** only where the Drudge Low Slave Camp (4292,
   p 0.26 of its cells) rolls it (p 0.80), with Drudge Slave 1632 and Drudge Servant 1631 (15). A level-30
   fighter's first hours; thin for level 50.
2. **The Low North Forest Mix, 7.9 map units west (36.1-37.5N 26.1-26.8E; generator 21183, 82 spots).** The level
   30-60 ground, rolled per cell by probability:

| Camp (wcid, p) | Creatures (wcid, level, XP) |
|---|---|
| High Tusker Camp 4372 (0.86) | Tusker Crimsonback 1627 (30, 5,000) × 6 slots; Goldenback Tusker 235 (30, 5,000) as a direct spawn (0.91) |
| Wild Monouga Camp 4303 (0.82), Crude Monouga Camp 4304 (0.73) | Wild Monouga 2576, Crude Monouga 2575 (30, 5,000); Rough Monouga 2574 (20), Outcast Monouga 2612 (8) |
| Shadow Camp 6054 (0.61) | Shadow 1758 (30, 5,000), Shadow Child 1756 (15) |
| Copper Golem Kingpin 27254 (0.54), Drudge Lurker Camp 4291 (0.51) | Copper Golem 194 (40, 7,000); Drudge Lurker 1608 (40, 7,000) |
| Drudge Stalker Camp 4289 (0.45) | Drudge Lurker 1608 (40), Drudge Stalker 1609 (50, 10,000) |
| Grievver camps 8061 (0.38), 8059 (0.31) | Scrawed Grievver 7978, Virulent Grievver 7979 (50, 10,000) |
| direct spawns | Brutish Monouga 9251 (50, 10,000, p 0.76), Wily Monouga 9253 (60, 17,500, p 0.79), Shadow Wisp 1989 (50, p 0.70), Sufut Zefir 7128 (50, p 0.57), Olthoi Drone 22009 (40, p 0.25), Olthoi Nymph 213 (30, p 0.29) |

A landscape generator rolls its children, so a hunt ground row is a **level band and a creature-type list**, not
the den's two wcids; monougas and tuskers are melee-only, grievvers and shadows cast, golems resist slashing; the
forest is about 1.9 km from town, so the loop teleports there by `place loc` and walks the camps. The probabilities
and slots were read from the same commit the release was built from; the lead verifies them against the pinned
release before relying on them.

**Holtburg's vendors** (positions from `landblock_instance`; the cells are indoor, so the coordinates are the
building's). The rates are the world database's float properties 37 (BuyPrice, what it pays) and 38 (SellPrice,
what it charges), applied in `W/Vendor.cs`.

| Vendor (wcid) | Cell | Where | Sells | Buys (MerchandiseItemTypes) |
|---|---|---|---|---|
| **Thelnoth Cort the Healer (715)** | 0xA9B40111 | 42.7N 33.4E | Handy Healing Kit 628, Adept Healing Kit 629, Potion of Healing 377, Stamina Potion 378, Mana Potion 379, Health Draught 2457, Mana Draught 2460, Stamina Tincture 27326 | Misc, Key, PromissoryNote (0x44080); BuyPrice 0.9, SellPrice 1.35; an unused kit sells back to him |
| **Shopkeeper Renald the Elder (718)** | 0xA9B40141 | 42.1N 33.8E | general goods | MeleeWeapon, Armor, Clothing, Food, Misc, MissileWeapon, Container, Useless, Writable, Key, PromissoryNote, CraftFletchingIntermediate, TinkeringMaterial: the loot buyer |
| Sedor Wystan the Blacksmith (712) | 0xA9B40100 | 42.6N 33.6E | weapons, armour | MeleeWeapon, Armor, MissileWeapon, PromissoryNote, TinkeringMaterial |
| Sontella Dagroff the Bowyer (713) | 0xA9B40121 | 42.6N 33.7E | bows, arrows (the missile item's ammo, if not from the `item` verb) | MeleeWeapon, MissileWeapon, PromissoryNote |
| Monyra the Jeweler (716); Archmage Cindrue (2304) | | 42.5N 33.8E | jewelry; components | Jewelry, Gem; SpellComponents, Writable, Caster, ManaStone |

### 5.10 The loops, the fighter and the checks

- **Shape.** As M3 §5.7: a loop is an `IScenario` run by the headless host (`--scenario=`) and as a leg of
  `LoginCheckScenario`'s `playing` stage; scenarios act through `IActionApi` and `PlayerTools` only; the screens
  through `IThrowawayScreens`. `login-hunt` is a backend step of `tools/smoke.ps1` under `-Backend require` only;
  `slice-check.ps1` never runs it (the text tests pin that for M3's steps; M4 adds its own).
- **The fighter's path** (Q1, if a new character): `CreateCharacter` with a `HeadlessCharacter.Intent` build (Aluvian,
  docs/session.md §8) amended to a melee template (Strength, Endurance and Coordination high; a melee weapon skill
  specialised, Missile Weapons, Healing and Melee Defense trained: the ids and the credit costs pinned by a dat test
  over `CharGenData.GetSkillTrainedCost`, as M3 1.1 s0 did), the `xp` verb to its level (`W/Player_Xp.cs:189-192`),
  the spending by `RaiseAttribute`, `RaiseVital`, `RaiseSkill`, `TrainSkill`; a weapon, armour, a bow and arrows,
  kits and potions by the `item` verb in a `FighterKit`; the name added to `CharPurgeRules.Graduated` and the
  stand-in; 5 of the account's 11 slots used (`M/PropertyManager.cs:626`).
- **The stand-in** answers a cast, a combat-mode change, the inventory actions and a tusker that fights back (M3
  3.2); M4 adds a swing and a shot in ACE's order, a corpse with contents, the container open and close, the item
  moves, a kit and a potion, a vendor with a stock and the appraisal reply, so `--play-checks` runs the hunt loop
  offline.
- **Verdict fields** (additive; report-only in the item that adds them): `fighter swings shots hits misses
  corpses_opened looted items_recovered corpse_looted heals kit_uses potions appraised sold bought pyreals
  attack_frame_max_ms loot_frame_max_ms vendor_frame_max_ms`, beside M3's `kills damage_dealt damage_taken
  hits_taken evades xp_gained died vitae respawn gear_restored`.
- **The hunt loop's character state.** The fighter is levelled by its build and the loop's kills (M3 D3's rule for
  BSD Mage); its vitae climbs with each death as the mage's did (M3 D35, D37: paid back by the `xp` verb); its
  pyreals and loot accumulate, which the sell at the loop's end bounds; a run that fails on the ground returns to
  Holtburg before its log off, and a run that cannot is reported `returned=no`.

## 6. Decisions

The decisions table will be `m4-plan.md` §7, numbered D1-Dn by the planner. Open questions are the owner's; each
carries a recommendation and **none is decided**. The M3 decisions M4 relies on are m3-plan.md §7 (D3, D5, D8, D13,
D19, D26, D34, D35, D37).

### 6.1 Open owner questions

| # | Question | Blocks | Options | Recommendation |
|---|---|---|---|---|
| Q1 | The fighter character (a new test character is the owner's decision, CLAUDE.md, Accounts) | 1.2 | (a) a new graduated character built by an idempotent scenario, as BSD Mage was; (b) levelling Lifestone Throwaway; (c) a melee build on BSD Mage | **(a)**, at a level the grounds fit (about 40: level-30 creatures hit it, so the recovery item sees real damage, and the forest's 50-60 band is a step up), with Healing trained (BSD Mage's is not, M3 D19, so a kit on it ends 0x04FC). Against (b): the throwaway leg (`login-throwaway`, docs/session.md §28) deletes and purges any `Lifestone Throwaway` it finds, so a fighter built on it is lost at the next `-Backend require` smoke. Against (c): D3 levels the mage only by its build and the kills, D19 trained no weapon skill and no Healing, and a melee mage breaks the buff loop's reading of its skills. The name is the owner's; the draft's placeholder is "Holtburg Fighter" |
| Q2 | Drag and drop in the inventory (M3 D13 said no drag: the UI runtime lacks it; M4 needs item moves) | 2.1, 2.2 | (a) a minimal drag in the UI runtime (an item widget taken on mouse down, released on a target element: a pack, a side pack, a container window, the paperdoll, the world view for a drop); (b) no drag: the panels show moves made through the Action API, wield stays double click, a labelled Lifestone-only key for pick up and drop | **(a)**, after the Action API and loops land (the moves work without it); the panels read the same read models either way, so an owner "no" costs no rework, only DoD 13's drag rows become `NotInM4`. Retail's drag functions have no address in the repository's docs: the first slice says "address unknown" and re-expresses the behaviour from the layouts |
| Q3 | Corpse looting: the death loop's own corpse, monster corpses, loot permission | 2.2, 3.1 | the hunt loop loots its own kills (ACE: the killer always has permission, `W/Corpse.cs:152-199`); the death loop (a) goes back for the mage's corpse by `place loc` before the re-gear's top-up, or (b) leaves it and keeps spawning gear; another player's corpse never (single player; the 180 s rule for monster corpses is the fallback, never relied on) | the hunt loop loots its kills; the death loop **(a)** when the corpse's lifetime allows it (not read: the lead pins `W/Corpse.cs`'s rot time first), so the top-up spawns only what the death destroyed and the corpse frame M3 lacks is taken; else (b) with the gap named |
| Q4 | Vendors: which of buy, sell and appraise land, and whether the healer's kits are the first purchase | 3.2, 2.2 | all three on the Action API (the open tracker and the stock model are shared); the panel's buy and sell; the examine panel over the appraisal model | **all three on the Action API; the panel's buy and sell; the examine panel if 3.2's estimate holds, else `NotInM4` with the model built.** The first purchase is a Handy Healing Kit 628 from Thelnoth Cort (715): the recovery item's input, at a price predictable from the 0062 (SellPrice 1.35 × Value), so one buy proves the whole chain; the first sale is the hunt's loot to Renald the Elder (718), whose mask covers weapons, armour, clothing and misc |
| Q5 | The testing-loop rule changes (M3 D26 filed "cut the testing loop down" under M4; nothing changed in M3) | 1.1 | which of the thirteen cuts the owner approves (§5.1 names the measured order 13, 10, 3, 11, 12, 9; the dat-tier split of Data.Tests; skipping PNG and WAV in the secret scan was **not** proposed, it loosens the scan); and the live-gate rule: today `-Only login-check,login` three times and the full smoke twice per item (G-A and the live gate), proposed **one `-Only login-check,login,<the item's legs>` backend run at the gate**, the worst-of-three kept only after an arrival-path change (the probe already covers those), the full `-Backend require` smoke once per wave | approve 13, 10, 3, 11 and 12 (structural, each with its text test and a before-and-after); 9 only after the first four are measured; adopt the one-run live gate with the worst-of-three kept for arrival-path items and the frame judges unchanged; every cut is report-only in its first gate by the rule |
| Q6 | LAN out | every item | the servers stay on 127.0.0.1 only; the memo's Shape B (automation, LAN) is not this milestone | **out**; goals.md §4 and the loopback policy stand; `LoopbackOnlyTests` keep failing any wildcard bind |
| Q7 | OpenAC's M4 codecs (docs/research/openac-m4-map.md §1: attack, cancel, drop, split, give, buy, sell, appraise, AttackDone, ViewContents, ApproachVendor) | 1.2, 2.1, 3.2 | (a) port under net.md's N-process at pin @6db05a2, each checked against ACE's writer or reader; (b) write every codec from ACE alone | **(a)**: the layouts are already read once by a client that plays against ACE, and M2's ports found four OpenAC defects by the same checks, so the N-process is the safeguard either way. `tests/Lifestone.Net.Tests/Hygiene/NetProvenanceTests.cs` then accepts a second OpenAC pin (a constant and the `Scopes` table), a small test change in the first net slice. Upstream's head is unsigned (only its GitHub merge commits are signed); the tree hash is checked against the API instead (foundation-openac.md §6.1) |
| Q8 | Test traits from OpenAC's lanes (foundation-openac.md §6.5) | the polish pass P5 | (a) `Tier=Timing` for tests whose outcome depends on load, out of the default filter and run alone by the lead, and `Status=KnownFailure` for a test written against an open TODO row, never in the green total; (b) neither | **(a)**, decided in P5 with the measurements, not here: both extend the existing `Tier` filter, and the frame-time rule already says rerun once then report |

### 6.2 Carried rules

In force for every M4 agent, lead or slice, from CLAUDE.md ("Hard rules" and "Rules for agent sessions") and M3's
decisions; a brief cites, never restates: BSD Mage is never reset or purged and is levelled only by its build, the
loops' kills and the vitae pay-back (M3 D3, D37); the graduated characters (`Lifestone Smoke`, `Lifestone Loopback`,
`BSD Mage`, and the fighter once built) are the baseline and are never reset; only `Lifestone Throwaway` may be
levelled freely or purged; automation uses `accounts.test` only; the database is read-only (SELECTs through the
existing `mariadb-ace.cnf`); the backend runs only from the main checkout and only a lead starts and stops it, never
`-AllowStaleMod`, `setup-server.ps1 -ModOnly` pre-authorised for the leads as in M3 (D6) if the planner keeps it;
every server binds 127.0.0.1; no dat-derived data is committed; no decompiled code; a judged field reads what it
names; a new judge is report-only in its first gate; the per-frame allocation rule on everything the session ticks;
commits through `tools/commit.ps1` on the item's branch, pushes only on the owner's word.

## 7. Status

Not started. The audit table is completed at the end of wave 4, one row per DoD item, with the verdict (met, met
with named gaps, partly met) and the evidence named in §2; the testing loop's numbers after M4's item 1.1 stand
beside §5.1's before.

| DoD | Verdict | Evidence |
|---|---|---|
