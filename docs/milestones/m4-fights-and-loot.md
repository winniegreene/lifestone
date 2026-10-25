# Milestone 4: fights and loot

**Status: complete (2026-10-25, "Merge m4/4-2 (The end)"): built across items 1.1 to 4.1, the polish pass, P4, P5a, P5b and the fresh-gate item; the audit in §7 closed by item 4.2, one row per DoD item, every item met (2 met; the rest met with named gaps). The hero video step after it (D29, D32) is done too ("Merge m4/hero (The hero video)": a 30 s cut, no on-screen text; m4-plan.md §8). Planned 2026-10-19 on master 52e5fb8.** Drafted 2026-10-17 from the accepted scoping memo
(Shape A, "polished combat, not automation"), the ACE research pack, the testing-loop measurements and the
independent review of M3's items 3.1 and 3.2. The plan of record (the read map, the seams, the waves, the slice tables,
the gates, the decisions D1-D18, the "as run" record) is **[m4-plan.md](m4-plan.md)**; where this spec and the plan
disagree, the plan's decisions govern. M3 ([m3-lights-and-sound.md](m3-lights-and-sound.md)) is complete (2026-10-19;
its item 4.2 was the end, with the polish pass, M3 D39-D43; the milestone is "fights and sound" in prose and the file
names stay); what it changed in this spec's ground is §3.3 and DoD 1. M2 ([m2-log-in.md](m2-log-in.md)) is complete on
master.

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

1. **The testing loop's remainder.** The cuts and the instrumentation were done in M3 4.2 (M3 D41: C1-C11, not C12;
   the measured report `cache/m3/4-2/q5.md`; the loopback suite 22.1 to 20.7 min, the full smoke 27.1 to 19.2 min, a
   slice-check's tests 85 to 30 s) and the 3.1/3.2 review's findings were applied in M3 3.2's follow-ups (m3-plan.md
   §8). What is left (m4-plan.md §4.1): the live legs' contended rule and the shader cache under `-Parallel`
   (`TODO(p5a-live-frame-contended)`, `TODO(p5a-parallel-shader-cache)`), one bounded attempt at the UI-step GC
   margin (`TODO(m3-ui-step-gc-margin)`), `summary.json` beside `summary.md`, `dotnet restore --locked-mode`, a
   test-count baseline file, `frame_over_50=yes` surfaced by smoke, the `[net]` login line without the account name,
   the `Tier=Timing` and `Status=KnownFailure` traits (m4-plan.md D8), and the M2 soak run with its CRC and duplicate
   counts printed. Checks: a text test per script change; `slice-check` before and after with equal outcomes; the
   probe's UI-step maximum before and after the GC attempt; the Soak tier run once at the item's live gate.
2. **The fighter** (m4-plan.md D1: "Holtburg Hunter", level 40, Healing trained) on the test account, built in item
   1.1 by an idempotent scenario and its loopback test as BSD Mage was (M3 DoD 2): the creation intent of §5.10, the
   `xp` verb to its level, the template spent, the kit by the `item` verb; a ready fighter is only verified. Checks:
   `FighterTemplateDatTests` (the pinned costs and XP), loopback `FighterBuildLoopbackTests`, verdict `fighter=ready
   level=40 template=ok` (`level>=40` once the loops' kills level it: m4-plan.md D19).
3. **Melee on the Action API.** `Attack(target, height, power)` (0x0008) and `CancelAttack` (0x01B7) sent; 0x01A7
   AttackDone and 0x01B8 CommenceAttack decoded; the tracker of §5.2. Checks: `CombatActionCodecTests`,
   `MeleeAttackTrackerTests` over ACE's order (the success, each refusal); loopback
   `MeleeLiveTests` (one kill on the hunt ground); leg `hunt`: `kills>=1 damage_dealt>0` judged from item 1.2's gate
   (the fields exist in M3's shape), `swings>=1` report-only in 1.2 and judged from 1.3 (m4-plan.md §5 item 3).
   *(Since P5b: `MeleeLiveTests`, `HuntLiveTests` and the leg `hunt` are gone; the proof is the melee journey's `hunt`
   station, whose `ok` is `HuntLegVerdict` (`kills`, `damage_dealt>0` or `one_blow_kills>=1`, `swings>=1`); the
   verdict-line shape stays pinned in `HuntVerdictShapeTests`.)*
4. **Missile on the Action API.** `MissileAttack(target, height, accuracy)` (0x000A); the held ammo's stack in the
   read model; the arrow's F745 attributed by the war-spell rule (M3 §5.4; `m3-cast-projectile-attribution` shared,
   still open) or the ammo's 0197, whichever first; the out-of-range refusal 0x0550 as a ServerError. Checks: codec
   and tracker tests, `StandInMissileTests`; loopback `MissileLiveTests` (a bow and arrows from the `item` verb, one
   hit); leg `hunt` with `--hunt-weapon=missile`: `shots>=1 hits>=1` (report-only in its first gate).
   *(Since P5b: `MissileLiveTests` and the `--hunt-weapon=missile` leg are gone; the proof is the archer journey's
   `hunt` station, `shots>=1` and `hits>=1` or `one_blow_kills>=1`; `StandInMissileTests` holds only the bow/mace
   wield refusal, the shot is proved by the tracker and reducer tests.)*
5. **Targeting and the combat UI driven by real actions.** The 01C0 pushes (one per 5 s heartbeat) move the health
   meter; the combat window's attack buttons and height send `Attack` (closes `TODO(m3-melee-attack)`); the power
   bar is begun by the attack's send, levelled while the power charges and finished on 01B8 or 01A7, never by hand
   (M3's 0.6 placeholder removed); `TODO(m3-combat-use)` closed. Whether retail moves the meter on 01B1's damage
   share is unknown: the meter follows 01C0 only, `TODO(m4-health-from-01B1)`. Checks: `GameCombatPanelTests` (a
   click sends `Attack`), `GamePowerBarTests` (begun by the action), play-checks on the stand-in's fighting tusker,
   frames `c1-combat` and `c1-powerbar` of a real attack, a live frame of the target's meter lower than at selection.
   *(Since 4.1 t2 the stand-in tusker and `StandInHealthPushTests` are gone, and since P5b the frames are the journey
   `hunt` station's: `c1-combat` and the meter frame wait for a window with `0 < meter < selection`, which the
   one-blow kills of the fresh-r2 journeys never open; they were last taken live at P5b round 1: §7.)*
6. **Containers.** GE 0196 and 0052 decoded; GA 0195 sent; `OpenContainer(guid)` and `CloseContainer(guid)` with the
   tracker of §5.4; a `ViewContents` model (order, foci slot) that fills `PlayerInventory.SidePacks` at login and on
   a pack pickup (closes `TODO(m3-inventory-sidepack-contents)` and its two siblings); a corpse's and a chest's
   contents in the read model; server refusals as ServerError with the 02EB text, or the F750 0x94 'locked' sound for a locked chest. Checks: codec and reducer tests (ACE's
   bundle order, the 0022-before-F745 tolerance), `ContainerTrackerTests`, play-checks (a stand-in corpse opened),
   loopback `ContainerLiveTests` (a side pack at login; the fighter's own kill's corpse; one chest); leg `hunt`:
   `corpses_opened>=1`.
   *(Since 4.1 t2 and P5b: the play-check `container-corpse-opened` is gone with the stand-in tusker, and
   `ContainerLiveTests` keeps only the side-pack fact; the corpse is proved by the melee and archer journeys' `hunt`
   station (`corpse ... holds N item(s)`, `corpse: closed`, `looted>=1` inside its `ok`; `corpses_opened` is not
   printed on the journey line), the chest by the melee journey's `chests` station
   (`chest_opened=true closed=true locked=refused`).)*
7. **Item moves.** `PickUp` (0x0019 from the ground or an open container: `MoveToPack` loses its held-item guard,
   `TODO(m3-pickup)` closed), `Drop` 0x001B, `SplitToContainer` 0x0055, `SplitTo3D` 0x0056, `SplitToWield` 0x019B,
   `Give` 0x00CD, with the trackers of §5.5; loot moves sent one at a time. Checks: codec tests,
   `ItemMoveTrackerTests`, play-checks; loopback `ItemMovesLiveTests` on Lifestone Throwaway (pick up a dropped item,
   drop, split a stack the `item` verb made, give to an NPC: the success on the stand-in, live ACE's refusal 028B
   0x046A by an NPC with no Give emote unless the lead finds one that accepts, §5.5).
8. **Corpse looting in the loops.** A loot rule (`LootRules`: every item whose Value is above 0 and whose ItemType
   the shopkeeper buys, until the burden limit; options) over an open corpse, reading Value and ItemType from the
   item's CreateObject description (ACE writes both under WeenieDesc flags, `W/WorldObject_Networking.cs:56-221`;
   inferred, confirmed by the first slice); the hunt loop loots its own kills; the death loop is the loop of D20 and D22:
   the mage re-gears, re-buffs, kills the tuskers in the den, then goes back for its corpse by `place loc` and takes back what the death dropped, the decoys (§6.1 Q3).
   Checks: `LootRulesTests`, `HuntScenarioTests` and `DeathScenarioTests` over a scripted corpse; loopback
   `DeathLiveTests` gains `corpse_looted=yes|partial|no|skipped items_recovered=<n>`; leg `hunt`: `looted>=1` (judged from the second
   gate), `bsd-death`: `corpse_looted=` (report-only in its first gate); `gear_left=` (the listed items the loops need that the trip did not take from the corpse; n/a when no listing was read) is report-only in its first gate, and the gate's judge for gear left on the corpse is `gear_left` read by the lead at the gate, promoted to a judge from 3.2's gate if the live runs read 0.
   *(Since P5b: `DeathLiveTests` and the legs `hunt` and `bsd-death` are gone; `looted>=1` over two or more kills is
   judged inside the `hunt` station's `ok`, and `corpse_looted=` and `gear_left=` are report-only fields of the mage
   journey's `death` station line. The trip back to the corpse is on foot (`TravelPurpose.Corpse`, over the room
   graph) and not by `place loc`; D37 removed the suicide and the decoy drop, so the mage undresses, lies down and dies
   once, `decoys=0`; and in the three journey gates of P5b and the fresh-gate item that trip aborted `travel-failed`,
   so the death loop's corpse recovery is not proved live on master: `TODO(m4-death-corpse-travel)`, §7.)*
9. **Recovery.** `UseWithTarget(kit, self)` (0x0035) with the kit tracker of §5.7; potions by `Use` with a tracker
   that completes on the 0197 or 0024 and the UseDone; the heal and potion chat lines read; a recovery rule (a kit
   under a health share, a potion while the kit is busy or out; options). On BSD Mage, whose Healing is untrained
   (M3 D19), a kit use ends 0x04FC: proved as the refusal. Checks: codec tests, `HealTrackerTests` over ACE's
   timeline, `StandInHealTests`; loopback `HealLiveTests` (the fighter after hits, a Handy Healing Kit 628 from the
   `item` verb; the mage's 0x04FC); leg `hunt`: `heals>=1` (judged from the second gate), `kit_uses= potions=`.
   *(Since P5b: `HealLiveTests` and its two siblings are gone; the kit, the potion and the mage's refusal are proved by
   the journeys' `harm-heal` stations (`heals= kits= potions=`, judged inside their `ok`) and the mage's `kit-refused`
   station (ServerError 0x04FC, the kit's uses and the pack unchanged). The `hunt` station's own `heals>=1` judge is
   inert in a journey, which skips the health stage: `m4-hunt-heal-unjudged-in-journey`.)*
   The recovery options: `--heal-floor-chance P` (0 to 1, default 0.3): the least estimated chance at which the kit is used as the last resort when no potion can be drunk; a bad value is an error.
10. **The hunt loop near Holtburg.** `--scenario=hunt` and the leg `hunt`: the fighter bound at Holtburg's lifestone
    (M3 D34's way), teleported by `place loc` to a hunt ground row (`HuntGround`: the Low B belt or the Low North
    Forest Mix, §5.9, each a level band, a creature-type list and the camps' cells), the nearest creature of the band
    selected, attacked until it dies, the fighter healed by the recovery rule, the corpse looted, on to the next
    until a kill count or a time, then back to Holtburg by `place` before any log off (the 3.1/3.2 lesson: no loop
    leaves its character on the ground); the sell at the end (item 12). Checks: `HuntScenarioTests` on the stand-in
    (its tusker fights back since M3 3.2), loopback `HuntLiveTests`; leg `hunt` judged `ok=true kills>=1
    damage_dealt>0`, then `looted>=1 heals>=1` from the second gate, with `damage_taken= hits_taken= evades=
    xp_gained=` reported; smoke step `login-hunt` under `-Backend require`.
    *(Since P5b: `HuntLiveTests` and `login-hunt` are gone, and a journey does not `place` the fighter: the melee
    journey hunts the belt and the archer journey the forest, each travelling on foot by the wayfinder and returning
    by Lifestone Recall (the `recall-home` station, D36/D38); the smoke steps are `journey-melee` and `journey-archer`,
    and the leg's verdict is the `hunt` station's `ok` = `HuntLegVerdict`. `place` remains only in the standalone
    `--scenario=hunt`. The Hunter itself runs in no gate step.)*
11. **Appraisal.** `Identify(guid)` (0x00C8) and GE 00C9 decoded into an `Appraisal` model (success 0 is a result,
    not an error). Checks: `AppraiseCodecTests` over ACE's layout section by section, `IdentifyTrackerTests` (the 5 s
    cooldown on a failed creature roll), play-checks; loopback `AppraiseLiveTests` (a kit, a weapon, a creature);
    `appraised=<n>` reported.
    *(Since P5b: `AppraiseLiveTests` is gone; the archer journey's `appraise-items` (a kit and a bow) and
    `appraise-vendor` (Thelnoth, the creature profile) stations prove the three kinds and print `appraised=<n>`.)*
12. **Vendors.** GE 0062 decoded into a `VendorStock` model (the rates, the type mask, each entry's WeenieDesc through
    `WeenieDescCodec`, the predicted price per stack); `Buy` 0x005F and `Sell` 0x0060 with the trackers of §5.8.
    Checks: codec tests, `VendorTrackerTests`, play-checks; loopback `VendorLiveTests` (Thelnoth Cort opened, one
    Handy Healing Kit bought at the predicted price, the hunt's loot sold to Renald the Elder); leg `hunt`: `sold>=1
    pyreals=<delta>` (report-only in the first gate), `bought=` reported.
    *(Since P5b: `VendorLiveTests` is gone; the melee journey's `vendor-facts` station proves the open, the buy and the
    sell-back at the predicted prices, the unsellable refusal and the cleanup, and the `vendor` station of the melee and
    archer journeys sells the loot (`sold>=1`, judged inside its `ok`) and prints `pyreals=` and `bought=`.)*
13. **The retail panels with content**, read-only over the read models with actions through the Action API (M3 D13;
    drag per §6.1 Q2): the combat window and power bar real (item 5); inventory 0x10000023 and backpack 0x10000022
    with side-pack contents; a container window for a corpse or chest; the vendor panel with buy and sell; the
    examine panel over `Appraisal` (§6.1 Q4); everything else answers `NotInM4`. Checks: client tests per panel,
    play-checks, a frame per panel, `--play-panel-shots` live on the fighter.
    *(Since P5b the live frames of the container and vendor windows are the journeys' `hunt-corpse` and `vendor` frames;
    `--play-panel-shots` runs in no gate step, so the examine panel's and the filled hotbar's live frames are those of
    3.1 and 3.2: `m4-panel-live-frames`, §7.)*
14. **Auto-buff** (the cut candidate): a re-buff driver in play mode runs M3's `BuffCycle` on the session clock
    (closes `TODO(m3-buff-rebuff-driver)`), under a toggle and the `--buff-*` options; the first automation feature,
    acting only through `IActionApi`. Checks: `BuffCycleTests` on the fake clock (the driver's schedule),
    `RebuffDriverTests`; live, the `buff` leg with the driver on: `rebuffed=yes forced=no` from the margin of the
    shortest line (the real 1,800 s expiry stays Soak-tier, M3 D26).
    *(Since P5b: `RebuffLiveTests` and the `buff` leg are gone; the proof is the mage journey's `buff` station
    (`rebuffed=yes forced=no driver=on`); the fighters' stations use the forced pass.)*
15. **Tests, gates and frame time.** `dotnet test` green with coverage that never decreases (m4-plan.md D28, the owner's 2026-10-22: the count is a floor, and a drop names the deleted tests and where their behaviour is proved; 7,780 at M3 3.2); the loopback
    suite; the smoke steps; no frame over 50 ms from Enter to walking (M2 DoD 15 kept) nor in an attack, a
    container's F745 burst or a vendor open, judged on the worst frame of each window: `attack_frame_max_ms`,
    `loot_frame_max_ms`, `vendor_frame_max_ms` (report-only in their first gate, judged against 50 ms from the next);
    M3's fight and death frame fields kept as they stand.
    *(Since P5b the samplers lived only in the retired `--login-check=hunt` leg, so no journey printed the three window
    fields; the journeys report each station's `frame_max_ms`, and 4.2's slice J1 puts the three fields back on the hunt
    and vendor stations: §7.)*
16. **The end:** the audit table (§7) one row per item; the doc pass that retitles M3 "fights and sound" in prose
    (the file names stay) and points CLAUDE.md at M4; the testing-loop numbers after, beside §5.1's before.
    *(Done at item 4.2: the audit table and the numbers are §7, the README was brought current in full, and CLAUDE.md
    names M4 as complete.)*

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
| M3 2.1 (D26, the owner) | the testing loop takes very long (the loopback suite about 15 minutes, the Soak expiry 31): cut it down | done in M3 4.2 as its polish item P5 (D41: the default filter excludes Exhaustive, the loopback suite 31 tests in 20.7 min, the full smoke 19.2 min); item 1.1 (DoD 1) keeps only the remainder (m4-plan.md §4.1, §9) |
| M3 2.1 review R8 | the session's `[net]` lines log the account name at login, in gitignored logs only | item 1.1 s3 |
| the independent 3.1/3.2 review (2026-10-17) | no blocker; nine should-fix findings (a report-only field that does not read what it names, four robustness defects that fail a live leg or leave the mage in the den, two verdict fields that can misread, a test pinning an order ACE does not use, two dormant defects) and nine nits | done in M3 3.2's follow-ups (m3-plan.md §8: 17 patches applied; finding 15 closed in 4.1's docs) |
| M3 4.2 (D42, p5a, fix B, k7) | the live legs' contended rule, the shader cache under parallel runs, the UI-step GC margin, smoke's reading of `frame_over_50`, the test-count baseline | item 1.1 (DoD 1) |
| M3 4.2 (D43, the hero take) | the den's old corpses draw ACE's ObjDesc with skin on bare arms and legs (`q6-corpse-objdesc-live`) | DoD 8's corpse frame in 2.2 |
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

The four waves as planned (m4-plan.md §3; each later wave is sliced at its boundary, M3 P8): **1.1** the testing
loop's remainder and the fighter (m4-plan.md D1, D9: built before any combat slice); **1.2** melee and missile attacks
on the Action API, with the hunt loop in its first form (one kill on the Low B belt, D10); **1.3** targeting and the
combat UI (the health meter, the attack height and the power bar driven by real actions); **2.1** containers and item
moves (corpse and chest opening, pick up, drop, split, give); **2.2** loot in the loops and the inventory (corpse
looting, the panels with side-pack contents, the minimal drag, D4); **3.1** recovery (kits, potions) and the full hunt
loop near Holtburg; **3.2** vendors and appraisal (buy, sell, appraise, the panels, D6); **4.1** auto-buff, the cut
candidate; **4.2** the end: the audit, the doc pass. The budget is in shares of the owner's weekly meter: 38% build
plus about 2% planning against a 45% stop line (D2; M3 D5's rule: a lead asks before exceeding its item's share by
20%), with a checkpoint after every wave (D3). The hunt loop and the death loop with its corpse are the acceptance
tests of the milestone. Each wave's first slice reads its rows of
[docs/research/openac-m4-map.md](../research/openac-m4-map.md) before the wire; new ports pin the signed head f22b048
(D7).

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
  (`W/Player_Move.cs:16, 37-125`); no weapon is needed. **Corrected by item 1.2's live gate:** the walk has a
  distance cap too: an approach that carries the player more than 15 m from where it began is cancelled with 028A
  0x003D YouChargedTooFar, then 01A7 {0x0036} (`W/Player_Move.cs:167, 241` set `FailDistance = 15.0f`;
  `Physics/Managers/MoveToManager.cs:489`), so a client closes to within 15 m before it sends 0x0008.
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
- **Confirmed and corrected live (item 1.2's capture, 2026-10-20, Holtburg Hunter on the Low B belt; m4-plan.md §8
  item 1.2).** The melee order above holds: the server walks the player in (MoveToObject F74C 4 ms after the send,
  then F748 positions; the headless client sends no movement), the first swing has no 01B8, a lethal blow sends 01AD
  and no 01B1, and the sequence's 01A7 {0x0036} follows the corpse's F745 and the target's F747 by about 0.2 s.
  Three corrections: (1) **`AutoRepeatAttacks` is on for a new ACE character**: one 0x0008 gave five swings and one
  0x000A five arrows, each swing or shot followed by 01A7 {0} and the next by 01B8, with no client send between; (2)
  **a 01C0 rides with every 01B1 and 01AD** for a selected target (§5.3, corrected); (3) **at short range the
  arrow's hit precedes its 01A7** (34 ms at 4 m), and the missile launch tick is 02E9, 02CE, the ammo's 0197, 02CD,
  F750 own, the arrow's F745, F755, F74A in that order. **The weapon swap (1.1 review R13):** ACE refuses a bow wield
  while a melee weapon is wielded with GE 00A0 {bow, WeenieError None} (`W/Player_Inventory.cs:1663-1668`,
  `CheckWeaponCollision`: "the client handles dequipping"), so a client moves the melee weapon to the pack first;
  wielding a bow in a combat stance with no ammo wielded draws 02EB "You are out of ammunition!". The last 01A7
  {0x0036} can arrive after a teleport the client sent on the 01AD. ACE clamps power and accuracy to 0..1
  (`W/Player_Melee.cs:90`, `W/Player_Missile.cs:82`); OpenAC does not; Lifestone's codecs send the value given and
  refuse only NaN or infinity. The capture's note is `cache/m4/1-2/capture.md` (gitignored).
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
selected a player, only at that player's respawn (`W/Creature.cs:369-380`, `W/Player_Death.cs:282`). **Corrected
by item 1.2's live capture** (m4-plan.md §8 item 1.2): `Creature.OnHealthUpdate` is also called by every damage
record (`Entity/DamageHistory.cs:93, 131`), so **a 01C0 reaches every player who selected the creature in the same
tick as each 01B1 and the 01AD**; the sentence that follows, as first written, is wrong for a selected target. Not
pushed (as first written) after the player's own hit, in the monster's `TakeDamage`, on a death or on a selection
change: a hit reaches the meter up to about 5 s late; 01B1's f64 damage share would let a client move it sooner, and retail's use of it is
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
- **Rot (M4 2.2, read from the pinned ACE source and a read-only SELECT).** A player corpse rots at `RecalculateDecayTime` (`W/Corpse.cs:103-118`): an empty one within `EmptyDecayTime` = 15 s, one with items after `max(3600, level x 300)` s (BSD Mage's corpse lives at least an hour, more once its level passes 12). A monster corpse has no timer of its own: `W/WorldObject_Decay.cs:18,56-62` sets 300 s on its first decay tick (an empty one 15 s, `:75-81`), and a corpse that generated a rare is set to 900 s (`W/Corpse.cs:356`). The hunt loots at once: the kill's corpse appears 0.5 to 1 s after the 01AD.
- **Trackers (proposal).** OpenContainer: send Use; complete on the 0196 for that guid, then wait (cap about 3 s, as
  `LoginCompleteContentsCapSeconds`) until every listed guid has an object; fail when the UseDone arrives with no
  0196, the reason the 02EB text or the F750 0x94 seen since the send; timeout 20 s. CloseContainer: complete on the
  0052 {guid}; 5 s plus half a chest animation. **Retail:** `CM_Inventory::Event_UseEvent` 0x006AD310,
  `CM_Item::DispatchUI_UseDone` 0x006A9470; what retail's container window sends on close and the handlers of 0196
  and 0052: unknown. **Gaps:** the server properties the pack names are `M/PropertyManager.cs` defaults and
  the running shard may differ (a read-only SELECT).
- **Confirmed and corrected live (item 2.1's capture, 2026-10-21; m4-plan.md §8 item 2.1; the note
  `cache/m4/2-1/capture.md`, gitignored).** Three corrections: (1) **at login every side pack's 0196 arrives in the
  first event bundle** (after 0013, 0029, 0021), **before the F746 and before every F745**, so the 0196 names guids
  whose F745s (the pack's, then its contents' with `container = <pack>`) come later; a pack entering the inventory
  (the `item` verb) also gets a 0196 {pack, 0} before its 0022; (2) **on an open the order is 0196, then UseDone 0,
  then the contents' F745s** (a corpse: 0.03 s apart), so the UseDone does not end an open; (3) **the locked chest
  sends F750 0x94 and the UseDone, no 02EB text.** Confirmed: the corpse's F745 0.54 s after the 01AD (then the
  target's F747); the open accepted in Melee mode; 0195 gives a 0052 at once (a corpse) or after half the Off motion
  (a chest, 0.42 s), with no UseDone; a Use on an open container gives 0052 with a UseDone; a re-open resends the 0196
  and every F745; a chest's open adds its F74C On after the UseDone.
- **Confirmed live (item 2.2's live round 1, 2026-10-21; the lead's note `cache/m4/2-2/live1-findings.md`, gitignored; BSD Mage).** A player corpse is in the object table **1.0 s after the trip's arrival** (ACE re-creates it for the client; the trip needs it within 15 s). Its open order is the hunter's (0196, then the F745s, then UseDone), and the own corpse opens at once: six items (Sollerets, Pyreal Scarab, Platemail Gauntlets, Olthoi Helm, The Healer's Heart, Mana Scarab). Four were taken in about 21 s; then the trip aborted at 24% health (under 40%) and the mage was placed back in Holtburg, with no second death. **The arrival is unprotected:** the trip's own teleport ends the lifestone protection, and the mage lost about half its health (75% to 24%) in 25 s among the tuskers.

Value and ItemType of an item are the F745's weenie description fields, already exposed as `WorldObject.Weenie.Value` and
`WorldObject.ItemType` (and kept in step by 0197 and 02CE through Mirror). `m3-inventory-instance-properties` is closed in
2.1 s1; `LootRules` (2.2) needs no new exposure.

The six new GameAction layouts (0x001B, 0x0055, 0x0056, 0x019B, 0x00CD, 0x0195) need no change from the capture. Their retail
sender cites are `TODO(m4-item-move-retail-cites)` (none named at 11.6096 yet).

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
- **Confirmed and corrected live (item 2.1's capture, 2026-10-21; Lifestone Throwaway and Holtburg Hunter;
  `cache/m4/2-1/capture.md`).** The Give NPC is **Alfrin 30479** (0xA9B40169; Give emotes for the three seed bags, e.g.
  Bag of Rye Seed 30488; the lead's SELECT). Corrections: (1) **split to a container sends GE 0022 for the new guid
  first, then 0197 for the old stack, then the new stack's F745**; (2) **an NPC answers 028B {0x046A, its name} + 00A0
  for any item it has no Give emote for**, also when it has emotes for others (not 0x03EF); (3) **a move sent while
  the previous move's chain runs, or a give within about 3 s of a teleport, is refused 028A 0x001D + 00A0 at once**: a
  drop sent at the pick-up's 0022 was refused, so the next move waits for the chain's end (about two pickup-motion
  lengths: 0.74 s from the ground, 1.35 s from a corpse, each). Confirmed from code, now live: the pick-up's 02DA,
  0022, 02CD, F750 0x8F and return F74C, and a ground item's F74A 15 ms after them (none for a container's item); the
  drop's 02DA x2, 019A, **the dropper's F745 (container none)**, F748, F74B, F750 0x90; split to 3D: 0197, the new
  F745, F750 0x90, no 019A or 0022; split to wield: 0197, 02DA, 02CE, 0023 {new, mask}, F625, F750 0x8C, then the new
  F745 (refused with 02EB "You must remove your <ammo> to wield <ammo>" + 00A0 and the old stack's 0197 restored when
  the slot holds ammo); the accepted give: 0024, 02CD, 0022 {item, container = the NPC}, the chat line, F750 0x8E,
  no F747, then the NPC's emote (Alfrin's AwardXP levelled the throwaway).
- **Confirmed and corrected live (item 2.2's capture, 2026-10-21; Holtburg Hunter and Lifestone Throwaway; `cache/m4/2-2/capture.md`, gitignored).** Three facts. (1) **A back-to-back loot from a corpse is four pick-ups in a row with no refusal** (a Banderling Scout's corpse of four items): each sends, its 0022 arrives about 1.35 s after the send with 02DA, 02CD and F750 0x8F, and the Inventory lane sends the next move 1.46 to 1.48 s after that 0022 (the pick-up motion, a 0.1 s margin and the return F74C), so the loop runs at about 2.8 s an item (`busy_refusals=0`); the corpse's 0195 gives a 0052 with no UseDone, as in 2.1. The default shop mask loots the two essences and skips the necklace and the azurite (jewelry and gems are not in Renald's mask). (2) **ACE never merges at pick-up** (closes `m4-pickup-merge`): an arrow picked up beside a held stack of its class arrives as its own stack, with a 0022 and an F74A 15 ms later (45 held and 5 picked up stay two stacks; the second split is the same), and joining stacks is the explicit `StackableMerge` 0x0054. ACE's pick-up path (`W/Player_Inventory.cs:1320`) adds the item with `TryAddToInventory` and no merge, so a looted stackable is always its own stack. The PickUpTracker's merge branch was a speculation, retired in 2.2 s0; `TODO(m4-loot-merge-stacks)` names the unmerged loot stacks. (3) **Coins** (a Pyreal stack of 7 with 10,004 held, split to 3 and picked up): 0022, 02DA, 02CD EncumbranceVal and 02CD CoinValue (10007), F74A; the three coins are again a separate stack, and the CoinValue updates.

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
result; 5 s. **Lifestone has (closed in 3.2; M3 D8 had nothing):** the 00C9 codec (`AppraiseCodecs`, s0), the Identify sender and the
reader into the `Appraisal` model, the tracker (s2) and the examine panel (s4c); `PlayerDescriptionCodec.cs:273-423` has
the private readers for the same table shapes. **Retail:** `ClientUISystem::ExamineObject` 0x005657B0 (11.6096; the
address named by the review, 3.2-R4); the sender `CM_Item::Event_Appraise` 0x006A94A0; the handler of 00C9 is not read,
its candidate being `CM_Item::DispatchUI_AppraiseDone` 0x006A9400 (`TODO(m4-retail-handler-00c9)`); the unpacker
`AppraisalProfile::UnPack` 0x005B4A30; the consumer `gmExaminationUI::RecvNotice_SetAppraiseInfo` 0x004B6E20.
**Gaps:** the full `[AssessmentProperty]` list and `SkillCheck.GetSkillChance` were not read; how retail's panel shows a
failed assess is unknown, and a failed creature roll was not seen live (`TODO(m4-appraise-fail-live)`, below).

**Confirmed and corrected live (item 3.2's capture, 2026-10-22; cache/m4/3-2/capture.md, gitignored; Holtburg Hunter,
ACE 95ef426d).** Every answer came 1 to 10 ms after its send.

- **The four answers.** A pack item (a Handy Healing Kit): flags 0x5, success 1, the Int and Float tables, no profile. A
  wielded weapon (the Mace): flags 0x25, success 1, the weapon profile. A creature (Thelnoth, an NPC, never spawned: D23):
  flags 0x510D, success 1, the creature profile and the armour levels. A missing guid: flags 0 and success 0 at +2 ms.
  Guid 0: no reply in 8 s.
- **The repeat.** A second Identify of Thelnoth 266 ms after a successful one was answered at +1 ms with the same flags
  and success 1: ACE's cooldown rule gives a repeat on the current successful target no roll.
- **No busy hold.** No busy refusal came for any of the four, so the Query lane holds nothing.
- **Not seen live.** A failed creature roll: the hunter's Assess beat Thelnoth's Deception, so no roll failed. The failed
  answer (the tables without the profiles) and the 5 s cooldown stay read from ACE's source (`TODO(m4-appraise-fail-live)`).

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
  its 02CE Structure fell and its UseDone is 0 (a refusal line with no Structure change is the refusal, 3.1 s1); a heal is the target's 02E9 rising; a spent failed roll is
  the "You fail to heal" line (Confirmed with `FailedRoll`, 3.1 s1); UseDone ≠ 0 is a
  ServerError. A potion is Confirmed iff its 0197 or 0024 arrived (a 02E9 alone is not enough at a full vital).
  **Lifestone has:** Use and UseDone; 02E9, 02CE (Structure mirrored onto a held item, `GameStateReducer.Inventory.cs:
  370-413`), 0197, 0024, the chat kinds, F750, F74C; it lacked the 0x0035 sender and every tracker until 3.1 (the sender is 3.1 s0's: `IAcSession.SendUseWithTarget`, `ObjectActionCodecs.WriteUseWithTarget`, written from ACE's reader, `UseWithTargetCodecTests`; the trackers are 3.1 s1's). **Retail:**
  `CM_Inventory::Event_UseEvent` 0x006AD310 (Use only), `CM_Item::DispatchUI_UseDone` 0x006A9470; the UseWithTarget
  sender is `CM_Inventory::Event_UseWithTargetEvent` (11.6096 0x006AD3E0, 11.4186 0x006AC480; 3.1 s0), and the heal handlers: unknown. **Gaps:** the Eat animation length is dat data (the heal and drink busy times are measured, the block above),
  a kit's BoosterEnum and HealkitMod are world-database facts not read; its BoostValue (PropertyInt 90) is 50, read by 3.1 s0.

**Confirmed and corrected live (item 3.1's capture, 2026-10-21; cache/m4/3-1/capture.md, gitignored; Holtburg Hunter at 188 health and 283 stamina, ACE 95ef426d).**

- **A kit use costs 1.47-1.48 s of busy** (2.08 s at the latest). The order: F74C SkillHealSelf 0x010E at +10 ms; at +1.48 s the F7E0 line, the 02CE Structure and UseDone 0 together; the Ready F74C (no command) with or 10 to 30 ms after the UseDone. **ACE is busy until the Ready:** a second send 0.2 s after the first got UseDone 0x001D at +2 ms (no 028A, no 028B), and a repeat sent AT the UseDone (8 ms before the Ready) was refused 0x001D too, so the Heal lane holds the next heal until the UseDone, the Ready and 0.1 s.
- **A use is spent win or lose.** A lost roll is the line "You fail to heal yourself. Your Handy Healing Kit has 19 uses left.", 02CE Structure 19 and UseDone 0, with no 02E9 and no 02CF. A won roll is 02E9 Stamina (103 to 88), 02E9 Health (43 to 116), 02CF, 02DD, the line "You heal yourself for 73 Health points. Your Handy Healing Kit has 14 uses left.", 02CE Structure 14 and UseDone 0, and the Ready 30 ms later. The stamina cost is a fifth of the heal. No "expertly" in seven heals; amounts 73, 66, 65 and 50, capped at the missing health.
- **The roll depends hard on the missing health.** At 43/188 (145 missing, difficulty 319 in combat) five of six uses failed (about 17%); at 116/188 and above every use won. A kit used under about 45% health mostly burns uses, so the recovery rule heals early (under 75%) or drinks a potion (`HealChance`, 3.1 s3).
- **A potion** (Use 0x0036 on a Potion of Healing 377): the Drink F74C at +10 ms; at +0.74 s the 02E9, the line "The Potion of Healing restores 25 points of your Health." (exactly its BoostValue, no roll), F750 and the 0197 {stack - 1} (0024 for the last); the Ready at +0.76 s; **UseDone 0 at +1.48 s.** **A potion drunk at a full vital is spent:** at 188/188 the stack went 4 to 3 and the line read "restores 0 points of your Health.", with no 02E9; UseDone 0 at +1.48 s (the tracker confirms it as `Wasted`).
- **An untrained healer** (BSD Mage, Healing untrained): UseWithTarget on its own guid got UseDone **0x04FC at +2 ms**, with no 028A, no 02EB, no motion and no 02CE; the kit kept its 20 uses.
- **The forest is a damage ground, not a kill ground.** `--hunt-ground forest` (band 30-40): the fighter reached a creature in the first cell and took 147 damage in 3 hits (about 49 a hit) of its 188 health, and dealt 34 in 9 hits (about 4 a hit); the retreat rule (under 30%) ended both hunts at 22% and 24% with no kill. The kill ground stays the Low B belt. After the retreat, 94 s of regeneration brought the fighter from 22% to about 23%: potions and kits are the recovery, not waiting.
- **Corrected from ACE's source (3.1 s0 to s2):** the heal's roll reads the missing health when the use begins (`DoHealMotion`), not when the effect fires; the last use's line is "Your Handy Healing Kit is used up." and a single use left reads "has 1 use left."; the effect tick sends F7E0 and then 02CE; a target that exists and is not a player is UseDone 0x04FE (`HandleActionUseOnTarget` 63-67), and only a missing target or a kit not held is the silent UseDone 0.
- **Not captured:** the full-vital refusal (028B 0x04FF, read from `W/Healer.cs:94`) and the 02EB stamina and mana texts. They stay `TODO(m4-heal-full-vital-live)` until `HealLiveTests` records a kit use at 188/188 (its `heal_full=` line, 3.1 s4) and the lead copies it here.

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
  the F745 of each 0022 item (they follow the UseDone, as `SpawnGear` waits); 5 s. **Lifestone has (closed in 3.2):** Use and
  `UseTracker`; 01C7, 0022 (a foreign container reads as Removed), 0024, 0197, 00A0, F745, F750, F74C; the chat
  kinds; `WeenieDescCodec`; the 0062 codec and `VendorStock` (s0, s1); the 0x005F and 0x0060 senders (s0); the Open,
  Buy and Sell trackers, and the CoinValue reader with its `CoinValueChanged` event (s1; 02CD CoinValue was stored with
  no reader before); the price rules in `VendorStock`; the stand-in's vendors (s3); the vendor window (s4b,
  `docs/retail-ui.md` section 23.11). **Retail:** the 0062's consumer is `gmVendorUI::OpenVendor` 0x004C5790 (its sender
  `CM_Vendor::SendNotice_OpenVendor` 0x006AB220); the buy and sell senders are `CM_Vendor::Event_Buy` 0x006AB050 and
  `CM_Vendor::Event_Sell` 0x006AAF60 (11.6096; the addresses named by the review, 3.2-R4; the bodies not read); the close
  is the object-range exit, `gmVendorUI::OnObjectRangeExit` 0x004C0F70 (retail closes the window when the vendor leaves the object range; the range's distance is unread, `TODO(m4-vendor-close-rule)`), with `gmVendorUI::RecvNotice_CloseVendor` 0x004C0F40, `CM_Vendor::SendNotice_CloseVendor` 0x006AB1D0 and `VendorItemsUI::CloseVendor` 0x004C1210; the totals are
  `VendorBuyUI::UpdateTransactionValue` 0x004C3D40 and `VendorSellUI::UpdateTransactionValue` 0x004C41A0
  and `gmVendorUI::UpdateTotalValue` 0x004C54D0 (`TODO(m4-retail-vendor-price)`). **Gaps:** the coin stack's MaxStackSize (25,000 is the stand-in's assumption) is not
  read; the vendors' UseRadius is read (section 5.9); the order of the UseDone, the 0062 and the F745s is confirmed live
  (the block below).

**Confirmed and corrected live (item 3.2's capture, 2026-10-22; cache/m4/3-2/capture.md, gitignored; Holtburg Hunter,
ACE 95ef426d; the times are the session clock).**

- **The open.** Use at the vendor: UseDone 0 at +0.83 to 0.96 s, the 0062 0.65 to 1.04 s after that UseDone, and a GE
  0x02BD at once after every 0062 (`TODO(m4-ge-02bd)`; a Tell, not a refusal). Thelnoth's 0062: mask 0x00044080, min 0,
  max 1,000,000, DealMagicalItems true, rates 0.9 (what he pays) and 1.35 (what he charges), **38 rows**. Renald's: mask
  **0x480467A7**, **53 rows**, DealMagicalItems true, the same rates. **Every row is unlimited** (stack -1).
- **The buy** (one Handy Healing Kit from Thelnoth, C1). The coins and the new item come before the UseDone: 0197 (the
  pyreal stack, 10,000 to 9,986), 02CD EncumbranceVal, **02CD CoinValue 9,986**, GE 0022 {the kit, container the player},
  02CD EncumbranceVal. Then at +12 ms the refreshed 0062, the 02BD and **UseDone 0**. The kit's F745 (wcid 628, Value 10)
  and F750 0x8F follow about 3 ms after the UseDone. The coin delta is -14, the prediction.
- **The sale back to Thelnoth** (the kit, C1). 02CD EncumbranceVal, GE 0022 {the kit, container the vendor}, **the
  refreshed 0062 before the coins**, GE 0022 {a new pyreal stack, the player}, 02CD EncumbranceVal, **02CD CoinValue
  9,995**, UseDone 0. The payout's F745 is a **new** stack of 9, not merged into the 9,986 stack, about 5 ms after the
  UseDone, with F750 0x8F. The payout is 9, the prediction floor(0.9 x 10 + 0.1).
- **The sale to Renald** (a Scroll of Leadership Ineptitude, Value 1, C2). 02CD EncumbranceVal, 0022 {item, vendor}, 02BD,
  0022 {the payout stack, the player}, 02CD EncumbranceVal, 02CD CoinValue 9,996, **UseDone 0, and only then the
  refreshed 0062**. The payout is 1 (the max(1, ...) floor). A tracker must not wait for the 0062 after a trade.
- **The refusal** (Leather Bracers, ItemType 0x2, Value 2,329, offered to Thelnoth, whose mask 0x00044080 does not take
  it). At +5 ms **02EB "The Leather Bracers is unsellable."**, then GE 00A0 naming the **player's** guid (0x50000007)
  with error 0, then UseDone 0. No F7E0. The item stays in the pack.
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
   fighter's first hours; thin for level 50. **Corrected by item 1.1's SELECT against the pinned release**
   (m4-plan.md §8 item 1.1): ACE rolls a generator's children by cumulative probability
   (`W/WorldObject_Generators.cs:120-213`), so each row's share is its value minus the previous row's: the belt's
   band is **8-20** (six of 1999's direct spawns are level 8), the slave camp's share is 0.06, and generator 1999 is
   a ring 4.5-8 map units around the town (709 cells within 8 units); the box above holds only 6 cells. The forest
   table below was read the same way and is re-read by 3.1's lead before it is relied on.
2. **The Low North Forest Mix (generator 21183; corrected by 3.1 s0 from the pinned release and the dats).** 6,618 spots world-wide; the nearest strip is the 0x9FB* landblocks 7.7 to 8.6 map units west (its 33 nearest cells at 25.25-25.95E, 39.35-45.45N, all dry and flat), the band 30 to 40 for the level-40 hunter (m4-plan.md 4.6), and `HuntGround.LowNorthForestTable` holds 38 creatures; in use through `--hunt-ground=forest`. **The forest is a damage ground, not a kill ground** (about 49 damage a hit from its level-30 creatures, about 4 a hit dealt: the capture, in §5.7's block); the belt is the kill ground. The table below is the level
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
| **Thelnoth Cort the Healer (715)** | 0xA9B40111, at (30.02, 159.68, 66.01) in the cell; UseRadius 3 m | 42.7N 33.4E | Handy Healing Kit 628, Adept Healing Kit 629, Potion of Healing 377, Stamina Potion 378, Mana Potion 379, Health Draught 2457, Mana Draught 2460, Stamina Tincture 27326 | Misc, Key, PromissoryNote (0x44080 on the wire); BuyPrice 0.9, SellPrice 1.35; an unused kit sells back to him |
| **Shopkeeper Renald the Elder (718)** | 0xA9B40141, at (139.23, 18.33, 94.01) in the cell; UseRadius 5 m | 42.1N 33.8E | general goods | MeleeWeapon, Armor, Clothing, Food, Misc, MissileWeapon, Container, Useless, Writable, Key, PromissoryNote, CraftFletchingIntermediate, TinkeringMaterial: the loot buyer (0x480467A7 on the wire) |
| Sedor Wystan the Blacksmith (712) | 0xA9B40100 | 42.6N 33.6E | weapons, armour | MeleeWeapon, Armor, MissileWeapon, PromissoryNote, TinkeringMaterial |
| Sontella Dagroff the Bowyer (713) | 0xA9B40121 | 42.6N 33.7E | bows, arrows (the missile item's ammo, if not from the `item` verb) | MeleeWeapon, MissileWeapon, PromissoryNote |
| Monyra the Jeweler (716); Archmage Cindrue (2304) | | 42.5N 33.8E | jewelry; components | Jewelry, Gem; SpellComponents, Writable, Caster, ManaStone |

The two shops' cell positions and UseRadius (float 54: 3 m and 5 m) are read by 3.2's lead's SELECT (`cache/m4/3-2/vendors.tsv`, gitignored); the other vendors' radius is not read. The cells are indoors, so the loops place the player inside the shop (`TODO(m4-vendor-approach)`).

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
  hits_taken evades xp_gained died vitae respawn gear_restored`. 3.1's additions, report-only in their first gate: `looted>=1` is judged over two or more kills; the hunt leg prints `heals= kit_uses= potions= ground=belt|forest mode_mismatch=<n>` after `loot_skipped=`, where `mode_mismatch=<n>` is the moments the combat mode and the wielded weapon disagreed (`CombatModeWatch.Field`: melee with a caster or a launcher, magic with no caster, missile with no launcher), whoever caused them, and its first moment is `CombatModeWatch.FirstViolation` (mode, item, session clock). The death leg prints `rebuffed= den_kills= decoys= decoy_safe= decoys_dropped= gear_dropped= decoys_recovered= pretrip_heals=` and `mode_mismatch=` (`docs/session.md` §32). 3.2's additions, report-only in their first gate: the hunt leg prints `sold= bought= pyreals= appraised= vendor_frame_max_ms=` beside `mode_mismatch=` (its detail line also carries `sell= buy= buy_pyreals=`); `sold>=1` and `vendor_frame_max_ms` are judged at 4.2; `pyreals` is the sale's coin delta only, and the purchase's price is `buy_pyreals` (`docs/session.md` §35).
- **The hunt loop's character state.** The fighter is levelled by its build and the loop's kills (M3 D3's rule for
  BSD Mage); its vitae climbs with each death as the mage's did (M3 D35, D37: paid back by the `xp` verb); its
  pyreals and loot accumulate, which the sell at the loop's end bounds; a run that fails on the ground returns to
  Holtburg before its log off, and a run that cannot is reported `returned=no`.

## 6. Decisions

The decisions table is [m4-plan.md](m4-plan.md) §7, D1-D18. The questions below were the owner's; **all are
answered (2026-10-19)**: Q1 is D1 ("Holtburg Hunter", level 40, built in 1.1), Q2 is D4 (the minimal drag in 2.2),
Q3 is D5 (the death loop recovers the corpse when its lifetime allows), Q4 is D6 (all three, the panels, the examine
panel if the estimate holds), Q5 was answered by M3 D41 (recorded as D18), Q6 is D17 (LAN out, recorded), Q7 is D7
(port at the signed head f22b048), Q8 is D8 (the traits, locked-mode, `summary.json`, the installs). The table is kept
as the record of the options and the reasoning. The M3 decisions M4 relies on are m3-plan.md §7 (D3, D5, D8, D13,
D19, D26, D34, D35, D37, D38, D41, D42).

### 6.1 The owner questions, as asked (answered: see §6)

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
`-AllowStaleMod`, `setup-server.ps1 -ModOnly` pre-authorised for the leads as in M3 (M3 D6, kept by m4-plan.md D8);
every server binds 127.0.0.1; no dat-derived data is committed; no decompiled code; a judged field reads what it
names; a new judge is report-only in its first gate; the per-frame allocation rule on everything the session ticks;
commits through `tools/commit.ps1` on the item's branch, pushes only on the owner's word.

## 7. Status

Planned 2026-10-19 (m4-plan.md §0). Merged, in order: item 1.1 and 1.2 (2026-10-20: DoD 1 and 2; 3 and 4), 1.3, 2.1 and 2.2
(2026-10-21: DoD 5, with the judged fields of 3 and 4; 6 and 7; 8 and 13's part), 3.1 (2026-10-21: DoD 9, 10 and 15, the
death loop), 3.2 (2026-10-22: DoD 11 and 12, the vendor and examine panels, the owner's D24), 4.1 (2026-10-22: DoD 14, the
visual pass by D27, the rule of DoD 15 changed by D28), the polish items up to P4 (m4-plan.md §8), P5a (2026-10-24: wayfinding and the characters' abilities), P5b (2026-10-24: the three self-driving
journeys, the stations, the four-lane gate; the retired live classes' coverage table is in m4-plan.md §8) and the fresh-gate
item (2026-10-25: fresh characters at every gate, D44-D46; the last green live gate, fresh-r2, `done ok=true` in 1,613 s,
journeys mage 11/11, melee 9/9, archer 10/10).

Item 4.2 "The end" audited every DoD item on master 2ee9e6e3 with six cluster reviews that read the tests, the sources and
the fresh-r2 logs; this table is the result, **one row per DoD item**, merged from the rows each item left. Since P5b the live
classes the DoD text names (`MeleeLiveTests`, `MissileLiveTests`, `HuntLiveTests`, the chest fact of `ContainerLiveTests`,
`DeathLiveTests`, `HealLiveTests`, `AppraiseLiveTests`, `VendorLiveTests`, `RebuffLiveTests`) and the smoke steps `login-hunt`
and the other `login-*` ones are gone: the journeys' stations prove what they proved (each station's `ok` is its judge; the
gate reads `journey done ok=true failed=none` and any `station=<n> ok=false`; every other field is reported only). 4.2
itself added or promoted (m4-plan.md §4.9 as run, D47): the attack, loot and vendor window fields on the journeys' hunt and vendor
stations, judged by the frame rule when not contended (J1, J2; DoD 15); `mode_mismatch=0` judged on the hunt station (J1); the
driver judge on the mage's buff station (J1; DoD 14); the den corpse trip fixed (F1; DoD 8); D47's done-line judges
(`teleports<=1`, `recalls>=2` for the mage, `recovery_places=0`, `granted=0` for the mage and the archer); all of them proved in
4.2's live round 3 (`m4-42-r3`, 163a5fdf, `done ok=true` 3,908 s with the Soak tier).

| DoD | Verdict | Evidence |
|---|---|---|
| 1 The testing loop's remainder | met with named gaps | `summary.json` beside `summary.md` (`ScriptLoopRemainderTests`: its fields, no detail text, account or repo path; fresh-r2's `frame-judge-1/summary.json`); `dotnet restore --locked-mode` in smoke's build step with a lock file per project; the baseline file `tests/Shared/test-count-baseline.json` (9,130 default, 735 Exhaustive; read by slice-check); `frame_over_50` and `frame_judge` in smoke's summary, report-only (fresh-r2: `journey-melee=yes (contended)`); the contended rule and the shader cache under `-Parallel` (both TODOs closed in 1.1; the fresh-r2 gate ran three Godot lanes at once); the `[net]` login line without the account (`AccountNameLogTests` walks to the world and quits; review R11 closed); the `Tier=Timing` and `Status=KnownFailure` traits (the filters and `SliceCheckAndSmokeTests` pins; `Tier=Timing` is on one test, `PathPlannerTests`, `KnownFailure` on none); the UI-step GC attempt (`NoGcWindow`; live unit 8.9 to 2.4 ms in 1.1) and its number, read at last: fresh-r2's frame-judge and throwaway logs show 12 first collections after the no-GC window at gen0 3.6 to 7.5 ms against the 12 ms margin and 7 more reading "none in 900 frames (still watching)" first, which first read as closing `m3-ui-step-gc-margin`; the milestone-end offline smoke then read a 16.8 ms UI step once (the closing gen-0 collection 7.6 ms; the rerun passed), so it stays open; the soak line `soak crc_failures= duplicates=` (`LivePhaseBTests`; 1.1's run `crc_failures=0 duplicates=0 gap_skips=0`) and 4.2's own Soak run (round 3: `soak crc_failures=0 duplicates=0 gap_skips=0`, LivePhaseB's lossy walk and the expiry, 2 of 2 passed, 1,973 s); the numbers before and after are the table below. Gaps: eight known wall-clock and allocation flakes stay in the default tier (`m4-timing-tier-candidates`), `frame_over_50` prints `n/a` for login-check and login (review R14, cosmetic), `offline-walkcheck-flake` and `collision-streamer-settle-flake` (m4-plan.md §9) |
| 2 The fighter | met | `FighterTemplateDatTests` (costs 12/6/6/10 of 52, level 40 at 20,502,261 XP), `FighterTemplatesTests`, `FighterKitTests`, `FighterProfileTests`, `FighterBuildScenarioTests` (47 methods; the ready verdict pinned `fighter=ready level=40 template=ok kit=ok wielded=331 worn=5`); loopback `FighterBuildLoopbackTests` (fresh-r2: passed in 29 s, the second run `fighter=ready` with the same level and no step sent; the verdict accepts `level>=40` once the loops' kills level it, D19); `login-check` `fighter=present` (all three fresh-r2 frame-judge runs); the protected name live (`ModCharPurgeLiveTests` passed in fresh-r2's test-tools lane, `CharPurgeRulesTests`); `--fighter` on the wrong account is a loud error (`HeadlessFighterOptionsTests`, review R9 closed); the same build scenario re-creates the Brawler and the Archer at every gate (`fighter=ready level=56 template=ok kit=ok wielded=45456 worn=7`; `fighter=built level=56`, D44-D46) while the Hunter, the baseline, is never re-created and runs in no journey (the advancement-chat lines were asserted live on its 1.1 build run). Leftovers named: the build's arrow top-up adds a new stack, the creation fallbacks stay loud failures (`m4-healing-minlevel` and the others) |
| 3 Melee on the Action API | met with named gaps | `CombatActionCodecTests`, `AttackEventCodecTests` (0x0008, 0x01B7, 01A7, 01B8; ported from OpenAC@f22b048, D7), `MeleeAttackTrackerTests` (19: the approach, the swing, each refusal, the queued case, the silent refusal after a Done, the 20 s timeout), `AttackSequenceReducerTests`, `AttackRequestedTests`, `CancelAttackTests`; live, judged inside the melee journey's `hunt` station `ok` (`HuntLegVerdict`: `kills>=1`, `damage_dealt>0` or `one_blow_kills>=1`, `swings>=1`): fresh-r2 `ok=true kills=3 swings=3 ... one_blow_kills=3 looted=10` (fresh-r1 the same shape); the retired `MeleeLiveTests` and `HuntLiveTests` moved there (the coverage table, m4-plan.md §8 P5b), and the verdict-line shape stays pinned in `HuntVerdictShapeTests`. Gaps: `damage_dealt>0` is no longer positively seen live (the level-56 Brawler kills each band creature in one blow: `swings=3 hits=0 damage_dealt=0 one_blow_kills=3`, passed through the one-blow alternative; the last positive melee numbers are 1.3's gate, `damage_dealt=45 swings=4 hits=3`): `m4-melee-damage-line-live`; a live approach beyond 15 m (0x003D) unobserved (`m4-hunt-follow-collision`, `m4-hunt-follow-unplaced`); the nits of m4-plan.md §9. Since 4.2 (J1, J2) the hunt station prints `one_blow_kills=` and judges `mode_mismatch=0`, and `attack_frame_max_ms` is judged on it when not contended (round 3: melee solo `mode_mismatch=0 attack_frame_max_ms=4.2`) |
| 4 Missile on the Action API | met with named gaps | `CombatActionCodecTests` (0x000A), `MissileAttackTrackerTests` (14: the launch by 0197 or the arrow's F745 under the war-spell rule, 0x0550 as a ServerError, out of ammunition, the silent refusal, the 10 s timeout, the queued case), `HeldAmmoTests` and the stack rule of `AttackSequenceReducerTests`, `StandInMissileTests` (only the bow/mace wield refusal GE 00A0 now); live, judged inside the archer journey's `hunt` station `ok` (`shots>=1`, `hits>=1` or `one_blow_kills>=1`): fresh-r2 `ok=true kills=2 shots=3 hits=1 damage_dealt=82` (both fields positive); the retired `MissileLiveTests` moved there. Gaps: `m3-cast-projectile-attribution` (shared, open), `m4-last-arrow-launch` (logged in code, not seen live), `m4-missile-transient-text`; the real 0x0550 refusal is proved by a unit test only (no live out-of-range shot) |
| 5 Targeting and the combat UI | met with named gaps | the meter follows every 01C0 (`GameSelectionTests`: a heartbeat push with no query, another guid or a changed selection moves nothing, successive values); the height buttons send `Attack` or `MissileAttack` with `ActionSource.Ui` and say retail's line with nothing selected (`GameCombatPanelTests`; `TODO(m3-melee-attack)` closed); the power bar begun by the send, levelled while it charges and finished on 01B8 or 01A7, for any source (`GamePowerBarTests` 13, `AttackRequestedTests`; the 0.6 placeholder removed; retail's flow from `EndAttackRequest` 0x0056CE30, `AttemptStartBuildingAttack` 0x0056C850, `HandleCommenceAttackEvent` 0x0056BA60, `HandleAttackDoneEvent` 0x0056D250, `HidePowerBar` 0x0056B5F0); `GetDefaultCombatMode` reads CombatUse (`TODO(m3-combat-use)` closed); `TODO(m4-health-from-01B1)` named and moot under ACE. `StandInHealthPushTests` and the tusker play-check are gone with the stand-in tusker (4.1 t2). Frames: `c1-combat`, the power bar and the target's meter lower than at selection were taken live at 1.3's gate and at P5b round 1 (combat window in melee, power bar mid-charge at 0.5, meter partly down) and by no gate since: fresh-r2's one-blow kills leave no window with `0 < meter < selection`, so the journey frame conditions never hold (`m4-hunt-frame-camera`, `m4-hunt-frame-target-hidden`, `m4-powerbar-frame-live`). Gaps: `m3-combat-options` (boxes ghosted), `m4-combat-map-keys`, the bottom power bar under AdvancedCombatUI, the stale M3 comments in `GameCombatPanel.cs` and `GamePowerBar.cs` |
| 6 Containers | met with named gaps | `ContainerCodecTests` (0196, 0052; ported from OpenAC@f22b048), `ItemMoveActionCodecTests` (0x0195), `ReducerContainerTests` over the live capture's order (the side packs' 0196 before their F745s, a corpse's 0196 then UseDone then F745s, the foci type kept: OpenAC #226), `LoginCompleteWaitTests`, `ContainerTrackerTests` (the open past the UseDone, the locked chest's F750 0x94, the 02EB text, the closes, the timeouts; refusals are ServerError, the DoD's wording amended); live: `ContainerLiveTests` (the one loopback fact kept) passed in fresh-r2's test-tools lane, `side_packs=2 side_pack_items=1 login_complete=true`; the corpse by the melee and archer journeys' `hunt` station (`corpse: 0x... holds 2 item(s)` ... `corpse: closed`, `looted>=1` over two or more kills judged inside its `ok`; the frame `journey-melee-hunt-corpse` lists the contents the log names); the chest by the melee journey's `chests` station (`ok=true chest_opened=true closed=true locked=refused`, opened at 1.5 m). The play-check `container-corpse-opened` is gone with the stand-in tusker; `container-chest-opened`, `container-pickup`, `container-loot` and `StandInContainerTests` remain over a labelled stand-in chest. Gaps: `m4-retail-handler-0196` (no retail address), the stand-in placeholders (`m4-standin-corpse-contents`, `m4-standin-chest`, `m4-standin-side-pack-item`), a pack picked up from the ground proved offline only, `corpses_opened` is judged nowhere and not printed on the journey line (indirect: `looted>=1`), no frame of the chest open (the station-end frame shows a wall) |
| 7 Item moves | met with named gaps | `ItemMoveActionCodecTests` (0x0019, 0x001B, 0x0055, 0x0056, 0x019B, 0x00CD; ported at f22b048), `ItemMoveTrackerTests` over the capture's order (the pick-up's 0022, the drop's 019A, the split's new-guid 0022 before its F745, the 3D split's 0197, the wield split's 0023 and its 02EB refusal, the give's 0024 and foreign 0022, 028B 0x046A, the busy 0x001D), `CorpseLooterTests` (one `PickUp` at a time), `MoveToPack` of an un-held item is a pick-up (`m3-pickup` closed), `StandInItemMoveTests`, play-checks `container-pickup`, `item-drop` and `drag-item-to-side-pack`; live (fresh-r2, the test-tools lane): `ItemMovesLiveTests` passed in 45 s with `split_container=yes split_3d=yes pickup=yes drop=yes split_wield=yes give_accept=yes give_refuse=0x046A+0x046A` and no 0x001D (an accept, Bag of Rye Seed to Alfrin, and two refusals, Alfrin and Gawain Cooper), and the melee journey's `hunt` station picked up 10 items one at a time from a corpse. Gaps: `m4-give-chain` (the give's busy length is not captured; a WARN each run), `m4-item-move-retail-cites` (no retail addresses), OpenAC #217 (no destroying use in the move set); `m4-pickup-merge` closed in 2.2 |
| 8 Corpse looting in the loops | met in 4.2, with named gaps (the hunt's loot since 2.2; the death loop's corpse recovery fixed by 4.2 F1 and proved in live rounds 1-3) | **The hunt:** `LootRulesTests` (value 1 to 1,000,000, Renald's mask 0x480467A7 read from his 0062 on the wire, the burden at most 100%), `CorpseLooterTests`, `HuntScenarioTests` over a scripted corpse; live, judged inside the `hunt` station's `ok` (`looted>=1` over two or more kills): fresh-r2 melee `looted=10 loot_skipped=1` over 3 kills, archer `looted=5 loot_skipped=2` over 2. **The death loop** (D20, D22, D37): `DeathScenarioTests` (the corpse and order files), `DeathLegVerdictTests`, `DeathTrackerTests`, `DeathDropModelTests` and `DecoyKitTests` (the decoy path lives on only for `--death-unbuff`); live in the mage journey's `death` station: `rebuffed=yes den_kills=4 gear_restored=yes worn=5 died=1 deaths=1 decoys=0 undressed=7 lie_down=sent lie_echo=yes`. The corpse trip is on foot (`TravelPurpose.Corpse`), not by `place loc`, and D37 removed the suicide and the decoy drop; in all three journey gates since P5b (p5b-r3, fu1-r1, fresh-r2) the trip aborted at its first leg, `the physics refused the path name=journey-corpse-4 ... ground probe at 10.1 m`, `trip aborted (travel-failed)`, `corpse_looted=no items_recovered=0 trip_abort=travel-failed trip_s=13.7` (the corpse died in cell 0x01050377 and the planner's goal sat at 0x01040000, 201 m away). **4.2 F1 found the cause:** `NavMap.FromCellFrame` normalised every position, so a dungeon room's origin with a negative local y (the den's rooms lie outside 0-192 m) was wrapped into the neighbouring landblock and the room graph read the goal 192 m from the corpse; indoor positions now stay in their cell's frame (`NavMapTests`, `J5bJourneyPlannerTests`, an Exhaustive den-route test on the dats: 8.8 m through 036C, 0376, 0377). Live since: `corpse_looted=yes items_recovered=7` (round 1, the run `route_m=8.9`), `items_recovered=8` (round 3). `corpse_looted` and `gear_left` stay report-only on the station (`gear_left=n/a`; review R7). Gaps: `m4-loot-merge-stacks`, `q6-corpse-objdesc-live`, the stand-in's corpse values are placeholders, the decoy check's noisy WARN under D37 |
| 9 Recovery | met with named gaps | `UseWithTarget` 0x0035 (`UseWithTargetCodecTests`, from ACE's reader; retail `Event_UseWithTargetEvent` 0x006AD3E0), `HealTrackerTests` (18), `PotionTrackerTests` (7, the Wasted-at-full-vital case), `HealLaneTests`, `HealResultTests`, `HealTallyTests`, `HealActionApiTests`, `RecoveryRulesTests` (33: ACE's success chance, heal early, kit by chance with a last-resort floor, a potion otherwise, never at a full vital, `--heal-floor-chance` 0 to 1 and its errors), `HuntScenarioTests.Scripted` (the heal between kills, the potion before the first fight, `--heal off`, the retreat), `StandInHealTests`; live, judged inside the stations' `ok`: the `harm-heal` station of every journey (fresh-r2 melee and archer `heals=3 kits=2 potions=1`, `kit_points=116/96`, `potion_points=25`, `potion_stack=5->4`; the mage `heals=1`), whose keeper uses the recovery rule (`kit ... at health 60% (below 75%, until 90%), estimated chance 1.00, healing 339`; the Healing skill is read, `m4-heal-skill-read` closed: `healing 313` and `339`, never the fallback), and the mage's `kit-refused` station (`ServerError/0x04FC ... pack_before=66 pack_after=66 kit_uses=20 problems=0`). The retired `HealLiveTests`, `HealBrawlerLiveTests` and `HealArcherLiveTests` moved there. Gaps: the journey skips the health stage, so the `hunt` station's own `heals>=1` judge is inert and the between-kills heal is proved offline only (`m4-hunt-heal-unjudged-in-journey`); `infight_potions=0` in every fresh-r2 hunt (the in-fight potion is unit-tested, unseen live); `m4-heal-full-vital-live` (028B 0x04FF) and `m4-heal-ready-stance` (their closer was `HealLiveTests`; no station reads them); a kit on another player is not in M4 (`m4-heal-other-target`) |
| 10 The hunt loop near Holtburg | met with named gaps | `HuntScenarioTests` (nine files over the scripted ACE, `HuntScriptRig`), `HuntLegVerdictTests`, `HuntGroundTests`, `HuntGroundDatTests`; live, in the journeys: the `bind` station (`bound=yes tied=yes`), the hunt travelling on foot by the wayfinder (melee `route_m=569.3` to the belt, archer `route_m=2099.2 run_s=227.3` to the forest; `place` stays only in the standalone `--scenario=hunt`), the `hunt` station `ok=true` (melee `kills=3 swings=3 seeks=1 ... xp_gained=5732`, archer `kills=2 shots=3 hits=1`), the loot (DoD 8), the sale (`vendor`: `sold=11 pyreals=30027`, `sold=8 pyreals=31104`) and the return by Lifestone Recall (`recall-home`: `recalls=1 recall_s=11.1`, 10 to 11 s in all three journeys), `damage_taken= hits_taken= evades= xp_gained=` reported. The retired `HuntLiveTests` and `login-hunt` are covered by `journey-melee` and `journey-archer` (smoke steps; G-A and the gate: melee 9/9, archer 10/10). The belt is the Brawler's ground and the forest the Archer's (its level-30 creatures are damage, not kills, for the Hunter); the Hunter runs no gate step. Gaps: `m4-hunt-follow-collision` and `-unplaced` (no 0x003D in any fresh-r2 log), `m4-hunt-frame-camera` and `m4-hunt-frame-target-hidden`, the heal in the hunt (DoD 9), `m4-hunt-band-empty` (the archer found 2 kills in 202 s and passed), the archer's forest band is sparse with real spawns (round 2 found none in 300 s; since 4.2 the archer's hunt searches 540 s, round 3 `kills=2` in 149 s; `m4-hunt-band-empty`). 4.2 J1 fixed the wind-down line (`returned=n/a` when the journey's recall-home returns) and put `loot_frame_max_ms` and `attack_frame_max_ms` back (DoD 15) |
| 11 Appraisal | met with named gaps | `AppraiseCodecTests` (18; 00C9 section by section over ACE's layout; ported from OpenAC@f22b048), `AppraisalTests`, `IdentifyTrackerTests` (13: the answers, a missing guid as flags 0 success 0, guid 0, the timeout, two in flight, the 5 s cooldown after a failed roll), `StandInAppraiseTests`, the play-check `examine-identify-confirmed`; live: the archer journey's `appraise-items` (`kit=flags=0x5/success=1/kind=Appraised/profile=none ... bow=flags=0x203F/success=1/profile=weapon ... appraised=2 failed=0`) and `appraise-vendor` (`thelnoth=flags=0x510D/success=1/profile=creature ... appraised=1`) stations, which read the model, not the handle; the retired `AppraiseLiveTests` moved there; `appraised=` reported, never judged. Gaps: a failed creature roll and its cooldown never seen live (`failed=0 cooldowns=0`: `m4-appraise-fail-live`), retail's 00C9 handler unread (`m4-retail-handler-00c9`) |
| 12 Vendors | met with named gaps | `VendorCodecTests` (0062; OpenAC #218: every row listed, 0xFFFFFF unlimited), `VendorActionCodecTests` (0x005F, 0x0060), `ReducerVendorTests`, `VendorStockTests`, `VendorPriceTests` (the predicted price, the whole-stack payout, the floors), `VendorTrackerTests` (34, over the capture's order), `StandInVendorTests`, the play-checks `vendor-panel-*`; live: the melee journey's `vendor-facts` station (`ok=true buy=yes buy_price=14 buy_predicted=14 sellback_payout=9 sellback_predicted=9 refused=yes refused_kind=ServerError item_kept=yes sold=yes sold_payout=27 sold_predicted=27 gone=yes problems=0 granted=1`: the open, the buy, the sell-back, the unsellable refusal and the cleanup, so `m4-vendor-cleanup-live` is closed) and the `vendor` station of both fighters' journeys (`sold>=1`, judged inside the station's `ok` by `SaleHappened`: melee `sold=11 pyreals=30027`, archer `sold=8 pyreals=31104`, `bought=0` because the kits were enough); the retired `VendorLiveTests` moved there. `vendor_frame_max_ms` is printed on the vendor and vendor-facts stations since 4.2 J1 and judged by the frame rule when not contended (J2): round 3 melee solo 4.2 and 6.9 ms, the archer (contended, reported) 4.3 ms. Gaps: `m4-vendor-close-rule` (the window stays open after the visit), `m4-retail-vendor-price`, `m4-vendor-approach`, `m4-sell-unsellable` (narrowed to `m4-vendor-issellable-wire`: Bread and the arrow pinned by weenie class), `m4-ge-02bd`, `m4-sell-0062-order`, the vendor window's unbuilt parts |
| 13 The retail panels with content | met with named gaps | the combat window and power bar (DoD 5); inventory 0x10000023 and backpack 0x10000022 with a side pack's contents (`GameInventoryPageTests` 27, `GamePaperDollTests`, `PanelShotFramesTests`); the container window `GameContainerPanel` (gmExternalContainerUI 0x1000000D; `GameContainerPanelTests` 25; D24's empty boxes by `UiItemList.UpdateEmptySlots`, retail 0x004E4390); the vendor window `GameVendorPanel` (gmVendorUI 0x10000017, 11.6096 0x004C1590; `GameVendorPanelTests` 25); the examine panel `GameExaminePanel` (gmFloatyExaminationUI 0x100005F7, `gmExaminationUI::RecvNotice_ExamineObject` 0x004ABB30; `GameExaminePanelTests` 23; `--examine-icon` default off, retail's window, `RetailUiOptionsTests`); the shortcut bar and the number keys (D24: `GameToolbarTests`, `HotbarRig`, `GameDragTests`, `ShortcutBarTests`, `ShortcutActionTests`) and the minimal drag (`UiDrag`; the drops only through the Action API); the visual pass of 4.1 (D27: the stand-in's items wear their own Setups and icons, the Healer's Heart lit, the bow and mace drawn in their stances; the stand-in tuskers retired, D26); everything else answers `NotInM4` (`NotInM4Tests`). Live frames on master (fresh-r2, 800x600, viewed): the corpse window (`journey-archer-hunt-corpse`: three icons, then empty boxes to the window's width), the vendor window at Renald's (`journey-melee-vendor-vendor`, `journey-archer-vendor-vendor`: the tabs, real icons, `costs 6,750p (you have 41,104p)`), the shortcut bar (the archer's `shortcuts` station `added=2 listed=2 used=Confirmed/Wasted/Drink removed=2 bar_empty=yes problems=0` and `ShortcutLiveTests`' relog, passed; its end frame shows the empty bar). Gaps: no live frame of the examine panel (the default window, last live 2026-10-21), of the filled hotbar or of a fighter's inventory since 3.2, and `--play-panel-shots` runs in no gate step (`m4-panel-live-frames`); the panels' Buy and Sell clicks are proved offline only; retail's drag, container-window and floaty-stack addresses (`m4-retail-drag`, `m4-retail-container-window`, `m4-floaty-stack-retail`); the examine lines (`m4-examine-*`), the hotbar's parts (`m4-shortcut-*`), the vendor window's unbuilt parts, `m4-toolbar-examine` |
| 14 Auto-buff | met with named gaps | `RebuffDriver` on the session clock, never forced (`RebuffDriverTests` 13, `RebuffFixTests`, `RebuffHostLogicTests`, the schedule rows of `BuffCycleTests` on the fake clock; guards: not in the world, dead, portal space, a held lane, a fight; a 60 s back-off); `--buff-auto on\|off`, `--buff-auto-check-s` and the margin option (OptionCatalog.Buff, `BuffCycleTests` parse rows); the play host on the Pause key with Lifestone-labelled notices, acting only through `IActionApi` (`m4-autobuff-key-unwired` is a guard, the key's warning is logged only if pressed before the wiring); `TODO(m3-buff-rebuff-driver)` closed; the armour lines' memory (`ArmourBuffLedgerTests`, `ArmourLedgerFileStoreTests`); the fighters' buff by `CasterSwap` (`FighterBuffScenarioTests`, `C1bSwapRig`). Live: the mage journey's `buff` station (fresh-r2 `ok=true buffs=24/24 rebuffed=yes forced=no min_remaining_s=3527 driver=on rebuff_lines=1 rebuff_s=30`; `BuffLegVerdict` judges the driver's own pass, `buffs a/w` and `rebuffed=yes`, so a failed driver fails the station; `forced=no` and `driver=on rebuff_lines=` are printed, not asserted), the melee and archer `buff` stations (`buffs=12/12 rebuffed=yes forced=yes ... swapped=yes swap_s=1.1`: the swap is proved live, through the forced pass, not the driver); the retired `RebuffLiveTests` moved to the station, `F2JourneyFixTests`, `BuffCycleTests` and `BuffScenarioTests`. The real 1,800 s expiry is Soak-tier (`EnchantmentExpiryLiveTests` `The_shortest_buff_expires_and_leaves_the_registry_with_its_0x02C3_and_an_F750`; last live result 1,799 s at M3): 4.2's run, round 3, `expiry_live spell=2 duration_s=1800 removed_after_s=1798 f750=1` (round 2 proved the same expiry after 1,977 s and then failed disposing its subscriptions from the test thread; the leg now releases them on the game thread). The judge on the driver run (4.2 J1, `BuffLegVerdict`): with the driver on, no driver pass, a pass that cast nothing or a forced pass that landed is a problem; round 3's mage `driver=on forced=no rebuffed=yes`. Gaps: `m4-buff-driver-lane-holds`, `m4-buff-ledger-hand-legs`, `fighter-driver-live` (the fighters' journeys run the forced pass with the driver off, so the driver with the swap is proved offline only) |
| 15 Tests, gates and frame time | met in 4.2, with named gaps (the three window frame fields put back on the journeys and judged by 4.2 J1 and J2) | **Green, coverage never decreasing (D28):** 9,130 default tests and 735 Exhaustive (baseline file), from 7,780 at M3 3.2 through 9,607 at 4.1 (t2 deleted 83 creature-only tests), 8,815 after the polish pass's slimming (named in the coverage tables of `m4-polish-pass.md`), 9,011 at P5b (J6 deleted 2 default and 26 loopback tests by name, coverage in m4-plan.md §8) to 9,130 at the fresh-gate item (nothing deleted); fresh-r2's loopback lanes: `test` 19 passed and 1 skipped (565 s), `test-archer` 1, `test-tools` 6, live phase B 1 (core mode; 4.2's round 1 ran the full loopback suite, every class passed, and round 3 the Soak tier); the smoke steps: `journey-mage`, `journey-melee` and `journey-archer` with the offline steps, the item-end check `slice-check -Changed` passed everything but `offline-player-checks`, which failed twice on different checks (`offline-walkcheck-flake`, a watch). **No frame over 50 ms from Enter to walking** (the frame judge, 3 of 3 clean in fresh-r2: login-check 31.6 / 37.4 / 36.3 ms, login 33.3 / 41.1 / 28.2 ms; the teleport frames 37.7 / 30.8 / 28.5 ms, the throwaway's 18.0 / 21.1 / 15.7 ms; `m4-enter-walk-rise` and `login-burst-frame` are closed). **The attack, F745-burst and vendor windows:** `attack_frame_max_ms` and `loot_frame_max_ms` were judged on the retired `hunt` leg (3.1: 7.6 and 4.2 ms live) and `vendor_frame_max_ms` never had a judge; the samplers were wired only into the retired leg, so no journey prints the three fields, and the journeys report each station's `frame_max_ms` instead (report-only, flagged `frame_over_50=yes` when contended; fresh-r2: melee hunt 43.8, vendor 33.8, vendor-facts 35.6, chests 22.3; archer hunt 28.9, vendor 16.0, appraise-vendor 26.6; mage den-fight 20.6; the buff and death stations 80.8 and 89.4 ms, contended). **4.2 put them back:** J1 samples the three windows on the Godot journey host (the hunt station `attack_frame_max_ms= loot_frame_max_ms=`, the vendor and vendor-facts stations `vendor_frame_max_ms=`; `n/a` headless) and J2 judges them in `Get-JourneyVerdict` (over 75 ms fails, over 50 flags, reported only when contended); the gate runs the melee journey SOLO (`-JourneySolo melee`) so its windows are judged: round 3 attack 4.2, loot 11.9, vendor 4.2 and 6.9 ms (rounds 1-2 within 21 ms); the archer's, contended, reported (4.2, 11.9, 4.3 ms). M3's fight and death frame fields are the mage stations' maxima, report-only. The judges 4.2 promoted (D47): `teleports<=1` and `recovery_places=0` on every journey, `recalls>=2` on the mage, `granted=0` on the mage and the archer; still report-only: `route_m`, `rungs`, `blocked`, `sightless`, `vitals_passes`, `run_s`, the melee's `granted`, `sounds_server`, `gear_mana_min`, `recharged`, `corpse_looted`, `gear_left`. 4.2's count: 9,153 default (+23: J1, J2, F1, FS), 736 Exhaustive (+1, F1), nothing deleted; round 3: the frame judge 3 of 3 clean (login-check 35.9 / 26.5 / 40.3 ms, login 26.6 / 34.9 / 35.2 ms, the teleport frames 12.5 / 14.0 / 34.2 ms, Holtburg 238-239 fps), the throwaway's 3 of 3 (8.3 / 12.5 / 8.3 ms). Gaps: `m4-timing-tier-candidates`, `offline-walkcheck-flake`, the server objects' collision on the client (`d25-server-object-host`: round 1's laps walked through an NPC beside the ring) |
| 16 The end | met in 4.2 (the doc pass) | this table, one row per DoD item (the rows each item left, merged); the retitle: M3 reads "fights and sound" in prose (the m3-plan.md title and the status of m3-lights-and-sound.md, which read "planned, not started", now say complete; the file names stay); CLAUDE.md names M4 as the complete milestone (the lead); the README current in every section (slice D1: the status table, highlights, controls with the Pause key, the options, the layout, the live gate and the slice check; closes `m4-readme-full`); m4-plan.md §9 places every open row (closed with its evidence or placed M5); the testing-loop numbers after are the table below, with 4.2's own gate numbers in its last column |

### The testing loop's numbers, before and after

The "before" cells are §5.1 and `cache/m3/4-2/q5.md` (M3 D41's start); the other cells are m4-plan.md §8 (1.1, P4, the
fresh-gate item) and the fresh-r2 gate log.

| Measure | Before (§5.1) | M3 D41 after | After 1.1 | At the fresh-gate item (fresh-r2) | At M4's end (4.2 round 3) |
|---|---|---|---|---|---|
| slice-check, tests | 85 s (7,886 tests) | 30 s | about 27 s (8,009 tests) | 9,130 default tests and 735 Exhaustive; `slice-check -Gate` (build, default and Exhaustive, no Godot) 2 min 23 s to 4 min 11 s | 9,153 default and 736 Exhaustive; `-Gate` about 3 min |
| the check before the live gate (G-A) | the full smoke (10.9 to 13.4 min measured at 3.1 and 4.1) | | offline full smoke 10.8 to 10.9 min | `-Gate` as above (D46); the offline smoke once per item, `-Changed` 9 min 34 s | `-Gate` before each of three rounds; the milestone-end `slice-check -Smoke` 11.3 min (one play-check frame flake, rerun passed) |
| loopback suite | about 15 min estimated, 22.1 min measured | 20.7 min | 18.9 min (31 tests) | not run whole at the gate: lanes `test` 565 s (19 tests), `test-archer` 1, `test-tools` 6 | round 1 `-Loopback full`, rounds 2-3 `core`: `test` 564 s (19 passed, 1 skipped), `test-archer` 1, `test-tools` 6, live phase B 1 |
| full live smoke / the live gate | 27.1 min; the old order about 4,860 s | 19.2 min | 18.7 min | the three journeys replace the full smoke: mage 11/11, melee 9/9, archer 10/10; the whole gate 1,613 s (setup 18, parallel 1,059, solo 534); P4's parallel gates 3,304 and 3,446 s | mage 11/11, melee 9/9 (solo), archer 10/10; the gate 3,908 s with the Soak tier (setup 14, parallel 1,100, solo 803, Soak 1,973), 1,935 s without it |
| default test count | 7,780 (M3 3.2) | | 8,009 | 9,130 / 735 | 9,153 / 736 |
| enter-to-walk frame | | | at most 44.5 ms (4.1) | 28.2 to 41.1 ms | 26.5 to 40.3 ms |

4.2's own gate numbers are the last column (m4-plan.md §8 item 4.2 has every round).
