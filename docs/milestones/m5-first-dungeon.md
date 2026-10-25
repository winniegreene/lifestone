# Milestone 5: the first dungeon

**Status: planned 2026-10-25 on master bfe466b7 (M4 complete), not started.** Drafted in the M5 workshop with the owner
from a gap analysis of master (five read-only audits: the wire and interface coverage, the fight loop, the cheats and
the economy, days-long runs; their facts are §5 here; the working notes are gitignored under `cache/m5/`). The plan of
record (the read map, the seams, the waves, the slice tables, the gates, the decisions D1-D24, the "as run" record) is
**[m5-plan.md](m5-plan.md)**; where this spec and the plan disagree, the plan's decisions govern. M4
([m4-fights-and-loot.md](m4-fights-and-loot.md)) is complete; what it leaves to M5 is §3.3. The P5b lead's design for an
outcome-aware caster and a companion character (gitignored, `cache/m5-patron-design.md`) is an input: its caster is
M5's item 1, its companion is deferred (§3.2, D10).

## 1. Goal

**The game plays itself, and a person can take the keys at any moment.** Lifestone's long-term shape, set by the owner in
the workshop (m5-plan.md D1): a single-player Asheron's Call that can literally play itself from a character's start to
the game's end, with a lot of grinding, where the person interacts as much or as little as they like, from playing
entirely by hand to never touching it and watching it like a screensaver. M5 is the first step, and it is "the first
dungeon": one mage plays one dungeon by itself for a day, honestly, on screen.

- **One mage, one dungeon, for a day.** BSD Mage (D5) hunts the Black Spawn Den on its own: it buffs, recalls to the
  den, fights tuskers on every storey it is worth fighting on (never the drudge and virindi wing, D4), keeps its mana,
  health and buffs up inside the fight, loots its kills, recalls home when its pack or its components say so, sells,
  buys components and potions, portals back, spends the experience it earns, and when it dies it respawns, re-buffs,
  recovers its corpse and goes back. It does all of this **without the server mod's conveniences** (§5.6): the mod builds
  the character once; play uses only what a retail player could do.
- **Days, not minutes.** The run has no end but the person's. The client survives a day: the two bugs that end a long
  run today are fixed (§5.8), a dropped session reconnects and re-enters the world, a crash or a restart resumes from a
  checkpoint, memory and logs are bounded and measured, the frame rate is capped. A 24-hour run on screen is the
  milestone's final gate (DoD 13).
- **The dial.** The autopilot has a key and a panel beside the auto-buff key; the person's own movement keys interrupt
  it and a hand-back resumes it from the state it finds; the kill switch stops every automated action, including the
  casts it does not stop today (§5.5). The automation inspector shows the state and the last decision.
- **Dungeons as data.** Everything the den-specific code knows today (the landblock, the floors, the targets, the
  portal, the tie, the town, the vendors, the spells by level) becomes a hunting-ground profile, so that M6 adds a
  dungeon or a region by writing a profile and proving it in a gate, not by writing code (D3). Mages first; the other
  classes join the autopilot from M6 on (D2).
- **Retail for the person at the keys, first tranche.** The things a person meets in the first minute of playing by
  hand and that quests and crafting rest on: the Yes/No confirmation dialog, character options that are saved, spell
  bars that can be edited and have their hotkeys, the slash commands with tells and the lifestone recall, the public
  property updates that keep a door's lock honest, and the window's camera following a resize (§5.9).
- **A solid world.** Doors stop the player and open when used, a recall lands beside the lifestone, and creatures
  stop overlapping and snapping (§5.11), on the plan the hero-video diagnosis wrote.

The primary consumer is the autopilot loop. Every capability lands on the Action API and the loop first, the retail
UI second (§4), with every den-specific fact in the profile, and every step is proved by a check (§2). The patron and
service bots, the quest and crafting layers, the other classes, the social systems and LAN are out (§3.2) and recorded
as the direction for M6 and later (§5.1).

## 2. Definition of done

Each item names the check that proves it, so the audit table (§7) is mechanical. "Loopback" is a test in
`tests/Lifestone.Loopback.Tests` (live, run by the item's lead); a "station" is a journey station's `ok` line and a
"cycle" is one pass of the autopilot's loop with its `autopilot cycle` line (docs/session.md gains a section for it);
"play-checks" is the offline stand-in step; a "frame" is a review frame at 800x600 (M2 D57; the soak frames at
1280x720, DoD 12). A new verdict field is report-only in the item that adds it and a new judge is report-only in its
first gate (CLAUDE.md, Checks); "judged" names the gate at which it is judged. Every retail behaviour cites the retail
function and its 11.6096 address where the repository's docs give one, else says "unknown"; every ACE rule cites its
file at 95ef426d. M4's decisions are cited as "M4 D<n>" (m4-plan.md §7); M5's own are m5-plan.md §7.

1. **The measuring run and the two live bugs** (item 0, the lead). The den's numbers measured live and recorded in
   m5-plan.md §8: the respawn delay of the three generators, mana per cast and the mage's regeneration at rest and in
   combat, cast time and damage per war spell level (Flame Bolt I-VII, Flame Streak, Arc, Cassius' Ring of Fire),
   Flame Bolt VII's component burn, the ring's kills per cast on a cluster, the north wing's caster damage against the
   buffed mage, a level-80 mage's buffed War Magic from `MageSpendingPlanner`. The two bugs fixed: the client drops
   objects the server has stopped sending (retail's client-side cull; §5.8 risk 1), and a route of one waypoint no
   longer throws in `NavRunner.Steer` (§5.8 risk 2), with Godot's own `ERROR:` lines counted by every check and the
   soak line. Checks: `ObjectCullTests` (an object unseen for the cull's time leaves the table; a creature the server
   re-sends is one entry, not two), `NavRunnerTests` (a one-waypoint route steers), the smoke summary's `godot_errors=`
   judged 0 from its first gate (a count, not a threshold: a named exception to the report-only rule, as M4 D31's audio
   fact was), the numbers in §8.
2. **The outcome-aware caster.** One cast policy above the Action API (`CastAttempt`, `CastRules`, `CastReport`,
   `CastTally`; the P5b design §2): a landed cast is done; a fizzle retries the same level up to a bound, then one
   level down once, then a named miss; a mana refusal inside the Mana Conversion tries is expected, outside them ends
   `ManaShort`; busy waits and resends; no components, out of range, a timeout and a refusal end named; a cast on
   another target is proved by the target's enchantment or the caster's chat line, else stays `Sent` and named. Every
   caller (the buff cycle, the re-buff driver, the fight, the vitals keeper, harm-heal, the recalls, the script legs)
   casts through it with its own preset, and **every automated cast is sent as `ActionSource.Automation`** (D13), so
   the kill switch and dry run reach buff passes, keeper casts and recalls. Checks: `CastAttemptTests` over every rule
   row and preset; the callers' existing tests unchanged under the presets; `KillSwitchCastTests` (a queued try is
   cancelled); live, the gate's three journeys with `fizzles=` within each preset's bound and the kill switch stopping a
   buff pass mid-cycle (`RebuffLiveTests` or the mage's buff station with `--kill-switch-at`).
3. **Mana inside the fight.** The fight no longer waits for its mana ceiling: it tries the real spell from the try
   floor (ACE rolls Mana Conversion after the client sends: `Player_Magic.cs:1026-1030`, `Creature_Magic.cs:14-53`,
   §5.4), transfers stamina to mana, drinks a Mana Potion, rests for stamina in peace, and re-chooses the bolt level
   after a buff or vitae change; the options `--cast-mana-tries`, `--cast-mana-try-chance`, `--cast-mana-try-spacing-s`
   (0 tries restores today's ceiling). Checks: `FightManaTests` over the scripted caster (the order, the floor, the
   spacing); live, the fight's `mana_wait_max_s<=10` judged from the second gate and `kills_per_hour` report-only
   against item 0's baseline.
4. **The hunting-ground profile.** `HuntingGround` as data (`src/Lifestone.Client/Session/Grounds/`): the landblock,
   the floors and the tour, the target weenies and the avoid rule (a cell set or a storey), the level band, the portal
   and its tie spell, the home town with its lifestone and the arrival point, the vendors with what each buys and
   sells, the spell plan by level, the component list, the stand-and-hold spots. The den is the first profile
   (`black-spawn-den-aluvian`); the Low B belt and the Low North Forest, which the fighters hunt from tables today, are
   the second and third, written as data only, and the fighters' hunt reads them. `BlackSpawnDen`, `DenPlanner` and
   M4 D12's floor rule become the profile's values (D4 supersedes D12). Checks: `HuntingGroundTests` (the den's profile
   equals today's constants; a profile with a missing field fails loudly at load); `HuntGroundDatTests` over the three;
   live, the melee journey's hunt on the belt's profile with its judged fields unchanged.
5. **The whole den.** The room graph of landblock 0x0105 over every storey (today's graph is the arrival floor's 158
   cells), the exit portals' rooms as no-go, the storeys' connectivity proved on the dats, the creature layer and the
   steering indoors (`nav-creatures-indoor`, `nav-indoor-steering`), and the profile's avoid rule keeping the mage out
   of the drudge and virindi wing (D4). Checks: `DenRoomGraphTests` (every tusker spawn cell reachable from the
   arrival, every excluded cell refused), `IndoorSteeringTests` on `MovementTestKit` worlds (the den's legs press a
   turn key at most once per 4 m, from about every 1.5 m today), `IndoorCreatureLayerTests`; live, the cycle's
   `storeys_visited>=2 basement_entries=0` judged from the second gate and the owner's eye on a den frame.
6. **The mage's logistics, cheat-free.** The mage loots its kills (`CorpseLooter` with a value-per-burden rule bounded
   by burden and slots), sells at Renald's front room, buys its components at Archmage Cindrue and its potions at
   Thelnoth Cort from a target list, carries a vendor robe and wand buffed by its own Item Magic (D2) and the four
   foci, so that every cast burns only its scarab and Prismatic Tapers (§5.7), counts its components and burden and
   starts the trip when either says so, spends earned experience by
   `MageSpendingPlanner`'s order whenever the unassigned pool covers the next rank, and does none of this through the
   mod: no `item`, `xp`, `place` or `corpses rot` after the build (§5.6). The Platinum and Pyreal Scarabs, which
   Holtburg does not sell, come from the build's first stack (D8); a stack under three is a loud, named stop. Checks:
   `MageLootRulesTests`, `ResupplyStationTests` over the scripted vendors (the buy list, the thresholds, the refusals),
   `XpSpendRuleTests`; live, one cycle `granted=0 places=0 sold>=1 bought>=1 spent_xp>0` judged from the second gate.
7. **Death inside the loop.** A death is handled by the loop, not a scenario: the respawn at the lifestone, the
   re-buff, the corpse trip by Primary Portal Recall and the runner, the items back, the return to the fight; the
   vitae clears by earned experience (ACE: `Player_Death.cs`, `Player_Xp.cs` at 95ef426d; §5.5: a death costs no
   experience, 5% vitae is about 21 kills at level 80), never by a grant; no decoy top-up, no re-grant. The gate's
   scripted death (M4 D37: undress and lie down) stays the way to cause one. Checks: `DeathHandlerTests` over the
   scripted ACE (the order, a corpse that has rotted, a second death before the first trip); live, the cycle after a
   scripted death `deaths=1 corpse_recovered=yes vitae_grants=0 rebuffed=yes` judged from the second gate.
8. **The outer loop.** `AutopilotScenario`: the state machine over known-state (Holtburg, the den, elsewhere, dead),
   buff, travel, fight, upkeep (buffs under their margin, health under its floor, components and burden), the trip
   (recall, sell, buy, portal back), death and resume; a checkpoint written on every state change and on exit and read
   at the next start (automation-vision §1.4 K); a reconnect that re-enters the world when the session drops, with a
   bounded retry; stop rules instead of kill and second limits (a duration, a level, a stop key), the kill cap lifted;
   per-state watchdogs; one `autopilot cycle` line per cycle and one `autopilot done` line at the end with the fields of
   §5.5. Checks: `AutopilotTests` over the scripted ACE (every arrow; a kill mid-cycle and a restart that resumes; a
   dropped session that reconnects); live, the gate's autopilot lane (§2 of the plan's §5) `cycles>=2 reconnects=0`
   report-only in its first gate.
9. **The dial.** A Lifestone key beside `Pause` (one retail's map leaves unbound; the lead picks it) and a panel button
   start and stop the autopilot; the person's movement keys interrupt it (as the runner does today) and the hand-back
   resumes from the state the loop finds; the kill switch stops every automated action within a tick; the inspector
   (`Scroll Lock`) shows the state, the last decision and the next trigger. Checks: `AutopilotHostTests`, the hotkey
   test, play-checks; live, the lead takes the keys mid-fight and hands them back on screen, read in the log
   (`interrupted=1 resumed=1`).
10. **A solid world.** `ServerObjectPhysicsHost` keeps a physics object for every server object with collision (doors,
    lifestones, NPCs, creatures), moved with the avatar and set ethereal with its state; the wayfinder treats a closed
    door as blocked and a leg that crosses one uses it and waits for it to open; a recall lands the player beside the
    lifestone where ACE puts it; the transitions for server objects (creatures colliding with each other and the
    player, the cause of the hero takes' flicker) land if their per-frame cost is measured within the frame rule, else
    stay a named TODO (§5.11). Checks: `ServerObjectPhysicsTests` (a closed door stops the walker, an ethereal one
    does not, a lifestone's cylinder stops it, an NPC's moves with it, zero allocation for a standing crowd); live, the
    melee journey's vendor station opens Renald's door on screen (`door_opened=yes`) and the mage's recall-home
    `stepped_off=no` since none is needed; `AvatarJumpWatch` counts read in the den frames.
11. **Retail for the person at the keys, first tranche.** The Yes/No confirmation dialog (GE 0x0274 and 0x0276, GA
    0x0275; `CM_UI::Event_ConfirmationRequest`, address unknown), character options saved to the server (GA 0x01A1,
    0x01A2) with the window layout and the chat masks saved beside the user data, the spellcasting bars editable (a
    spell dragged from the spellbook, removed, reordered; GA 0x01E3, 0x01E4) with retail's hotkeys (the next and
    previous tab and spell, 1-9 in magic mode), the slash commands (`/tell`, `/t`, `/r`, `/e`, `/lifestone` GA 0x0063,
    `/marketplace` GA 0x028D, `/afk`), the four public property updates (GM 0x02D2, 0x02D4, 0x02D5, 0x02D6), and the
    world camera's field of view recomputed on a resize (`cam-fov-resize`). Checks: codec tests from ACE's writers,
    client tests per panel, play-checks, frames; live, one login shows the options and the layout kept from the one
    before, and a chest unlocked by a key reads unlocked.
12. **Window sizes.** The shell frames and the play-checks run at 1280x720 and 1920x1080 as well as 800x600 in the
    gate's offline smoke (the frame judge stays at 800x600, M2 D57); the soak runs at 1280x720. Checks:
    `smoke.ps1 -AllSizes` in the gate's offline step; the owner's eye on one 1280x720 frame of the den.
13. **The 24-hour run.** BSD Mage on `test`, the autopilot, the Godot play host at 1280x720 with `--max-fps=60`,
    recording off, the backend under the lead for the day (D6), the `[soak]` line every 60 s (working set, private and
    managed bytes, the GC counts and pauses, the object and avatar counts, the light rows, the frame maximum and p99,
    the net counters, the phase, the cycles, Godot's errors, the log size) and an outside loop every 5 min (the three
    processes' memory, the log and database sizes, free disk), one 800x600 frame every 30 min for the owner's eye.
    Verdicts, report-only in this first soak: `autopilot done ok=true` after 24 h with every cycle accounted for, the
    working-set slope after the first hour, `objects` and `in_cell` bounded by the den's population, `light_rows<256`,
    `godot_errors=0`, the reconnects and their causes, the frame p99 flat. Logs capped and written once.
14. **Tests, gates and frame time.** Green `slice-check` at every slice; coverage never decreasing (M4 D28; the
    baseline 9,238 default and 736 Exhaustive, lowered only for named deletions); the live gate's four lanes kept
    (the three journeys and the tools lane) with the autopilot lane added in the `test` lane after the mage's journey
    (m5-plan.md §5); the frame judge solo; a new judge report-only in its first gate, the named exceptions in
    m5-plan.md §5.
15. **The end.** The audit table (§7) one row per DoD item; m4-plan.md §9's rows placed M5 closed or re-placed;
    docs/session.md's new sections (the caster, the profile, the autopilot, the soak), docs/automation-vision.md's
    "as built" for §1.4 E, K and R6; the README current in every section (the status table with M5 done and M6 named,
    the controls with the autopilot key, the options, the live gate and the soak); CLAUDE.md pointing at M6's planning;
    M6's seed list of hunting grounds drawn from the knowledge corpus (§5.10); the hero video (M2's, M3's and M4's
    had one): the mage's day in the den, cut from the soak's capture, under 30 s, no on-screen text (M4 D32).

## 3. Scope

### 3.1 In

The goal's seven parts; the cast policy and the Mana Conversion tries; the hunting-ground profile and the three
profiles; the whole-den graph, the indoor creature layer and steering; the mage's loot, resupply, vendor kit, counters
and experience spending; the death handler; the autopilot scenario with its checkpoint, reconnect and stop rules; the
dial's key, panel and inspector; the server-object physics host, doors on routes and the lifestone landing; the retail
tranche of DoD 11; the two sizes; the soak; the audit table and the end.

### 3.2 Not in M5

Named in the UI as `NotInM5` where they would show: the other classes in the autopilot (melee, missile, the hybrids: M6
and later, D2); the patron and the service bots of retail (the buff bot for a Platinum Scarab, the tinker, dye and ingot
bots; the companion design is their base: §5.1, D10); the goal planner and the quest layer (a quest picked from a menu and run end to end:
§5.1); the crafting bots and crafting itself (tinkering, salvage, the recipes, the craft dialogs beyond the Yes/No
box); fellowships, allegiances, secure trade, chat channels beyond tells, friends, titles, squelch; housing; books and
the quest journal; the map page; the examine of creatures and world objects (M6's retail tranche, with the rest of
§5.9); Void magic, summoning, augmentations and luminance (untested, not blocked); the server's `xp_modifier` (D7, retail
rates); a character of the owner's own for the autopilot (D5: the test character for now); LAN (goals.md §4; every
server stays on 127.0.0.1).

### 3.3 What M4 leaves to M5

M4 is complete. Its open TODO rows placed "M5" are in [m4-plan.md](m4-plan.md) §9 (each "Placed" reads "placed M5");
m5-plan.md §9 takes them up by area. The ones that shape M5's items:

| From | What | Where in M5 |
|---|---|---|
| M4 4.2 audit, "fights" | a projectile's caster is not on the wire (`m3-cast-projectile-attribution`); a buff on another target is `Sent` with no proof (`m3-cast-other-target-proof`); the auto-target by name (`m3-autotarget-*`) | item 1 (the proof hook; the attribution rule kept, the TODO re-placed to the patron's milestone) |
| M4 P5b, the owner's notes | the automation must be outcome-aware (a fizzle noticed and retried); keep pressing cast short of mana; the running looks unnatural and runs into enemies | items 1 and 3; item 2's indoor steering and creature layer (`nav-indoor-steering`, `nav-creatures-indoor`, `nav-creature-radius`) |
| M4 P5b, D44-D46 | fresh journey characters each gate; the 3-minute G-A | carried (m5-plan.md §5); BSD Mage is re-created at each gate and the soak runs on the gate's copy (D5) |
| M4 4.2, `TODO(d25-server-object-host)` and the hero takes' diagnosis (`docs/milestones/m4-hero-visual-bugs.md`, on the unmerged branch `m4/hero-visual-bugs`) | doors walked through, creatures overlapping and snapping, the lifestone landing | item 5 (the branch's diagnosis and `AvatarJumpWatch` merged first) |
| M4 polish pass, `cam-fov-resize` | the camera's field of view computed once at start | item 6 |
| M4 4.2 audit, "the testing loop" | the GC margin (`m3-ui-step-gc-margin`), the flakes under load, the wall-clock tests | item 7's soak measures them over a day; the Timing tier as M4 D8 |
| M4 4.2 audit, the combat options and tooltips, the chat channels, the panels' open names | m5-plan.md §9, placed M6 unless an M5 item touches the file |

## 4. The automation-first order and the data-first rule

Every capability lands in M3's order (m3-lights-and-sound.md §4, carried by M4 §4), and a wave's gate reads it in this
order: the wire (codec, dispatcher, `IAcSession`), cited to ACE's writer and the retail dispatcher where known; the read
model and the event; the action and its tracker, with the stand-in's answer; the headless scenario and its loopback
test (live, the lead); the station or cycle with judged verdict fields and the smoke step; the retail UI, read-only
over the read model, acting only through the Action API; the frame at 800x600. Two rules are M5's own:

- **Data first.** A fact about one dungeon, town, vendor, spell list or route is a profile value, never a constant in
  code; code that reads a profile fails loudly on a missing field; a second profile written as data only is the proof
  that the shape holds (DoD 4). This is what makes M6 "rapidly".
- **Measure before building.** Item 0 measures the den and the loop before any loop slice runs (DoD 1), as M4 measured
  the testing loop before it cut it; every efficiency lever is sized against those numbers, and a lever whose measured
  yield is nil (§5.4: Fire Vulnerability, Imperil) is not built.

The waves as planned (m5-plan.md §3; each later wave is sliced at its boundary, M3 P8): **0** the measuring run and
the two live bugs; **1** the outcome-aware caster and mana inside the fight; **2** the hunting-ground profile and the
whole den; **3** the mage's logistics and the death handler; **4** the outer loop and the dial; **5** a solid world;
**6** the retail tranche and the two sizes; **7** the 24-hour run; **8** the end. Items 4 and 5 touch different seams
(the session and the physics) and run as parallel lanes under one lead. The autopilot cycle and the 24-hour run are
the acceptance tests of the milestone.

## 5. Research (2026-10-25, read-only; ACE paths are under `vendor-eval/ACE/Source/ACE.Server/` at 95ef426d)

### 5.1 The direction, and the layers above the first dungeon

The owner set the direction in the workshop (D1-D3, D10): single player first; patrons later. The product is a game
that plays itself from start to finish, with the person free to play by hand or to watch. M5 is the first dungeon; M6
adds dungeons and regions to idle in, rapidly, as data. The layers above the hunting loop, recorded here so that M5's
shapes do not close them off:

1. **A goal planner.** A wish for an item, a level or a quest becomes steps the engines run: hunt ground X for N of
   item Y, buy Z, travel to NPC W and hand in, craft with recipe R. The mission queue of automation-vision §1.4 E with
   goals as its front end.
2. **Quest knowledge as data.** ACE's world data holds the mechanics (NPC emote scripts, quest stamps and timers, quest
   items, portal restrictions), not the walkthroughs; a quest profile per quest is written from the community guides
   and checked against ACE's emote tables (§5.10). It rests on NPC dialogue, the journal, give-to-NPC, keys, doors,
   levers and chests, and the Yes/No dialog of DoD 11. A quest picked from the automation menu then runs end to end.
3. **The service bots of retail, run by the client.** Players ran the buff bots (a full buff for a Platinum Scarab, in
   the four starting towns and at allegiance mansions), the tinker bots, the dye bots and the bots that made Atlan
   ingots. Lifestone recreates that economy locally: characters on their own accounts, run beside the person's by the
   companion runner of the P5b design, each with a service, a fee and a preset; they need secure trade, give, the
   crafting systems and the retail customs as data.
4. **Alts that level themselves with a focus.** The person's own second character, levelled by the autopilot on a
   profile whose goals are trade skills and recipes.
5. **Crafting bots.** Alchemy, cooking, fletching, tinkering, salvaging and dyeing through the Action API, with their
   skills, materials and the recipe data.
6. **The dial** at every layer.

### 5.2 The experience table and the grind

Read from the owner's portal dat (XpTable 0x0E000018, `XpTableData`; the levels list is cumulative):

| Level | Total XP | Next step |
|---|---|---|
| 40 | 20,502,261 | 2,381,651 |
| 80 | 493,005,039 | 29,690,784 |
| 100 | 1,418,089,761 | 68,826,684 |
| 120 | 3,390,841,150 | 137,821,250 |
| 126 | 4,286,609,098 | 166,128,086 |

A tusker of the den gives 30,000 experience (§5.3). Level 80 to 100 is 925 M, 30,800 kills; 100 to 120 is 1,973 M,
65,800 kills; 80 to 120 is 2,898 M, **96,600 kills**. At the loop's measured rate (§5.4) that is 67 to 90 days as it
stands and 27 to 40 days with mana managed in the fight. The owner's answer (D7): retail rates, no `xp_modifier`
(ACE's default 1.0, `Player_Xp.cs:22-34`); players did grind one dungeon for a month at several points in the game's
history, and a month in the den is the plan, not a problem. A death costs no experience (§5.5).

### 5.3 The den

Landblock 0x0105 (the Aluvian Black Spawn Den; the Gharu'ndim 0x0104 and Sho 0x0103 are identical); the arrival cell
0x0375 at (100, -210, 0); three generators (wcid 3955, "Linkable Monster Gen (15 min.)", `RegenerationInterval` 60 s;
each slot's delay 900 s; measured by item 0: 903-955 s from a death to the slot's next creature, the generator's 60 s
tick added; m5-plan.md §8) with 117 linked creatures (m3-lights-and-sound.md §5.1, the M3 3.1
lead's read-only SELECT):

| wcid | Name | Count | Level | XP | Health | Where |
|---|---|---|---|---|---|---|
| 1629 | Tusker Guard | 64 | 80 | 30,000 | 260 | the storeys below the arrival floor |
| 1628 | Tusker Slave | 19 | 80 | 30,000 | 400 | the arrival floor: 17 spawns in 12 cells (z 0) |
| 1610 | Drudge Ravener | 16 | 80 | 30,000 | | the north wing (z -24 to -30): casts acid, force, blade |
| 1609 | Drudge Stalker | 8 | 50 | 10,000 | | the north wing |
| 23 | Virindi Servant | 8 | 40 | 7,000 | | the north wing and two outliers (0x02D6 at z -6, 0x0244 at z -12) |
| 237 | Virindi Master | 2 | 50 | 10,000 | | the north wing |

The tuskers are melee only; their damage-taken multipliers are Fire 1.0, Slash 0.5, Bludgeon 0.75, Cold and Acid 0.75,
Electric 0.25 (the knowledge corpus, from the same world data). M4 D12's floor rule confined the loop to the arrival
floor's Tusker Slaves; D4 keeps the mage out of the north wing (not worth the time, as retail players found) and lets
it fight the Tusker Guards on the storeys between, which needs the room graph of every storey (today's is the arrival
floor's 158 cells, built on the dat's cell graph; the storeys' connectivity and the walker's step rules over them are
item 2's first question). The exit portals' rooms (0x0341-0x0343, 0x035C at z 0; one at z -6) are no-go.

### 5.4 The fight loop as measured

The mage's `den-fight` station in M4 4.2's live rounds: one Tusker Slave in 17 to 28 s of fight time, 3 to 4 Flame Bolt
VII casts, 309 to 482 damage, `xp_gained=30646`, no damage taken (buffed, level 127). Measured in round 3's log and
derived from ACE (the den-loop audit; the soft spots are item 0's list): Flame Bolt VII is 3.33 s from the request to
the projectile (the 1.85 s often cited is a buff's cast); 2.75 casts a kill in steady state, 10.5 s a kill cast-bound;
a kill costs 24 to 33 mana and the mage regenerates about 0.43 mana a second, and `FightScenario` waits for mana at or
above its 35-mana ceiling with no transfer, rest or drink (`FightScenario.cs:938-953`), so after 17 to 23 kills the
loop is **regeneration-bound at 45 to 60 kills an hour.**

- **Retail's rule.** The client never checks mana before a cast (`gmSpellcastingUI::Cast` 11.6096 0x004C6C40,
  `ClientMagicSystem::CastSpell` 0x00568DE0: the spell, the components, the target type, then the send); the server
  rolls Mana Conversion after the send (`Player_Magic.cs:1026-1030`: the fizzle pre-check at 528-545, then
  `CalculateManaUsage`, `Creature_Magic.cs:14-53`, two rolls against half the power and the power, factor 0.03), answers
  UseDone 0x0401 at once and spends nothing when the rolled cost exceeds the mana held (`:581-585`), and charges 5 mana
  for a fizzle. So pressing cast again when short of mana is retail's client behaviour; today's ceiling is Lifestone's own conservatism.
  The P5b design's scratch model (100,000 seeded rolls, reproducing session.md §40's expected cost) puts a try at half
  the base cost landing three times in four; item 1 pins an exact model.
- **The levers, sized** (item 0 measures each before item 1 or 3 builds it): the in-fight mana manager (M, about 2 to
  2.5 times); hold a spot and wait for aggro or a respawn instead of touring (S-M: the tour's 11 to 14 s walks);
  pipeline the next cast at the projectile's confirmation (S-M, 0.15 to 2 s of 3.4); choose the spell by kills per mana
  per second (Flame Streak's projectile flies 45 m/s against the bolt's 15; S); Ring of Fire 1785 on three or more
  adjacent tuskers (M; the hero run's 6 casts and 54 projectiles gave 3 kills, no better than bolts yet: in ACE it is
  power 200, 9 projectiles, no splash); a war wand with a damage modifier (S). Not worth building unless item 0 says
  otherwise: Fire Vulnerability Other VII 2170 saves a cast only at a multiplier of about 2.4; Imperil has no armour
  term in `SpellProjectile`'s damage. The level rule: the 0.9 chance needs the skill at the power plus about 31
  (`SkillCheck.cs:7-22`); buffed War Magic is 410 at level 127, a level-80 mage's is estimated 330 to 360, so VII
  (power 300) is borderline and VI (250) the safe pick; item 0 computes it.
- **The limits that end a run today**, every one of them: `--fight-seconds` (a failure at the limit, not a stop),
  `--fight-kills` at most 1,000, the headless `FightQuitAfterSeconds` 3,600, the Godot leg's whole-check seconds, the
  journey limits, a walk over 90 s, a death, buffs decaying unwatched (3,600 s), components running out (the kit holds
  about 25 to 75 war casts), the client's 20 s silence watchdog with no reconnect, ACE's 60 s session timeout.
- **Item 0's measures (m5-plan.md §8; they amend this section where they disagree).** Flame Bolt VII is 2.46 s from the
  request to the projectile when the mage already faces its target (2.9 s with the turn and the pick; the 3.33 s above
  included both); VI is 2.40 s, Flame Streak VII 0.61 s. The arrival floor is **respawn-bound, not mana-bound**: its 17
  Slaves died in 4.4 min (14.5 s a kill, 3.5 casts a kill, 136-212 a landed bolt) and each slot returned 903-955 s after
  its death, so the floor alone gives about 64-68 kills an hour and the 30-minute run never waited for mana. The mana
  sums from the dat and ACE's `GetManaCost`: Flame Bolt VII costs 7.1 on average at Mana Conversion 311 (about 25 a
  kill), VI 4.0; regeneration alone (0.44 a second, measured at rest and in magic stance) carries about 63 kills an
  hour, the stamina-to-mana dance (m5-plan.md D27; stamina 0.66 a second in magic stance, 0.875 of it back as mana at
  Meditative Trance) about 145 at VII and 230 at VI, near the cast-bound 240. So the lever is targets: the whole den and
  the timed return to the floor (D25). Ring of Fire 1785 has a 4.5 m range and gave about 1.5 kills a cast on 6-7
  adjacent tuskers (bolts 3.5 casts a kill): worth building with a pull, which standing still does not give (no tusker
  came in 10 minutes). A level-80 mage's buffed War Magic is 361 (VII at 0.986), not 330-360; the owner sets VI at
  level 80 (D26). The north wing's casters hit the buffed mage for 106 (a critical Lightning Bolt IV) and 50 in 129 s.

### 5.5 The loop's arrows: what exists for each

| Arrow | Exists | Missing |
|---|---|---|
| start to known state | the journey's known-state (present, Holtburg, peace; else a recovery place) | the real starts classified (Holtburg, the den, elsewhere, dead) and resumed |
| buff | `BuffCycle` (24 lines, 51 to 80 s, 27 casts), `RebuffDriver` with its guards | the driver inside a fight (never installed in a leg or a journey) |
| to the den | `DenTravelStation`: Primary Portal Recall 48 lands inside the den; the untied answer 0x04A3 walks to the portal | the first tie without a budgeted `place` (the walk from Holtburg, about 1 km) |
| fight | `FightScenario` (§5.4) | mana, health, buffs, components and burden inside it; a recoverable walk failure; a clean stop |
| loot | the fighters' `CorpseLooter`, `LootRules` | the mage loots nothing |
| the trip's trigger | the fighters' `why=pack-full` guard | the component and burden counters |
| recall home | `RecallHomeStation` (Lifestone Recall 1635, the step off the lifestone) | nothing |
| sell | the vendor station (Renald, `sold=`), `VendorPrice` | a mage station (the catalog needs a fighter template, `JourneyStations.cs:471`) |
| buy | `Buy`; the kits at Thelnoth | a component and potion purchase from a target list (§5.7) |
| portal back | Primary Portal Recall | nothing |
| death | `DeathScenario` (the respawn, the regear, the vitae stage, the corpse trip, the re-buff, the composed fight) | a handler that does only what a player can: no regear by the `item` verb (the regear grants 9 items and the trip returns the same 8), no decoy top-up, no vitae grant |
| vitae | ACE: earned experience is added in full and counts toward the vitae pool (`Player_Death.cs`, `Player_Xp.cs`), so a death costs no experience; 5% clears at 615,855 XP at level 80 (20.5 kills), 1,075,788 at 100 (35.9), 1,696,941 at 120 (56.6); ACE drops level/20 + 0 to 2 items (4 to 6 at 80, 6 to 8 at 120) and half the pyreals (`corpse_destroy_pyreals` true); M4 4.2's round 3 paid 1,835,327 for its 5%, which matches | the real costs are the 5% skill cut until it clears, the re-buff, the trip and the downtime |
| spend experience | `RaiseSkill`, `RaiseAttribute`, `RaiseVital`, `TrainSkill` and `MageSpendingPlanner` (the builds) | no loop spends during play (the level-127 mage held 169 M unassigned) |
| the person takes the keys | a movement key interrupts the runner and releases its keys; `Pause` toggles auto-buff; the inspector is read-only; the kill switch and dry run reach `Automation` actions | the automated casts are `Host` (`BuffCycle.cs:1138`, `RebuffDriver.cs:426`, `VitalsKeeper.cs:691`, `HarmHealScript.cs:605`, `FightScenario.cs:898`, `RecallHome.cs:316`), so the kill switch stops a run but not a buff pass or a recall; no hand-back; no autopilot control |
| days | one login per journey; every host quits at its limit | a run without a limit, reconnect and re-entry (`GameSession.Reconnect` is wired to the disconnected screen's button only), a checkpoint |

The autopilot's lines: `autopilot cycle n= state= kills= xp= deaths= trips= sold= bought= spent_xp= mana_wait_max_s=
fizzles= mana_tries= mana_landed= storeys_visited= basement_entries= granted= places= interrupted= resumed=
reconnects= s=` and `autopilot done ok= cycles= stop= ...` with the totals; the fields are report-only in their first
gate and judged as §2 says from the second.

### 5.6 The cheats on the path, and the server

Every use of the mod's verbs during play (the cheat ledger, from the code at master): the gear re-grants (`SpawnGear` in
`GearScenario.cs:429`, in the fight, buff, death and journey gear stages), the death's regear (`DeathScenario.Order.cs:
281`) and decoy top-up (`DeathScenario.TopUp.cs:58`), the vitae-clearing `xp` grant (`DeathScenario.cs:1566`; M4 D42),
the `place` fall-backs (`PlaceTravel`'s default, the shop entry by `place loc`, the pre-journey and failure recovery
places), `corpses rot` (the hero plan). Build time, which the build keeps as the owner's one cheat layer (D11): `xp` to
the start level, `spell learn` (262 spells; level VII scrolls have no vendor), the kit grants, the Academy quest stamp,
the post-build `place`, and the one budgeted `place` to the den portal for the first tie until the walk replaces it.

The bundled server runs ACE's defaults: Lifestone sets no property. Retail-faithful where it matters: `require_spell_comps`
true, `safe_spell_comps` false, `vitae_penalty` 0.05 (cap 0.40), `xp_modifier` 1.0, `corpse_destroy_pyreals` true
(`Managers/PropertyManager.cs:457-687`). The one default ACE flags as non-retail, `creatures_drop_createlist_wield`
false, does not touch tuskers.

### 5.7 The component economy and Holtburg's vendors (the pinned world SQL v0.9.296, parsed read-only)

- **The foci (the owner's point, D8).** Retail's mages carried the four foci, one per school: Foci of Enchantment
  15268 (Creature), Artifice 15269 (Item), Verdancy 15270 (Life) and Strife 15271 (War) (and Shadow 43173 for Void);
  each value 500 (675 pyreals), burden 400, attuned and bonded (never dropped at a death, never traded), sold by the
  Scriveners of each school and by three archmages (the corpus: Eastwatch, Fort Tethana, the Scrivener of War Magic in
  landblock 0xA9B3 beside Holtburg). With a school's focus in the main pack, ACE casts that school's spells on the foci
  formula (`Player_Spells.cs:451-488` `HasFoci`, `SpellFormula.cs:383-385`): the base scarab plus one to four
  Prismatic Tapers by the scarab's level (four for Platinum, level VII), and **nothing else burns**. BSD Mage holds
  only the Foci of Strife (creation gear for its one creation school), so today every buff and travel spell burns its
  full formula of herbs, powders, potions, talismans and coloured tapers: 38 component ids in the kit. With all four
  foci the kit is **two kinds**: the scarabs of the levels cast (Platinum for VII, Pyreal for VI, Gold for V; rarely
  burned, CDM 0.03 for Platinum) and Prismatic Tapers 20631 (Cindrue, 30 pyreals, a stack of 1,000). Item 3's vendor
  kit holds the four foci, granted by the build until the mage can buy them.
- **Burn.** Flame Bolt VII (2128, power 300, `ComponentLoss` 0.15) burns about 0.45 component units a cast on the foci
  formula (0.69 on the full one); at 3 to 3.5 bolts a kill that is about 1.6 units a kill plus 0.012 Platinum Scarab,
  about 210 pyreals a kill (51 of Holtburg stock, 160 of Platinum). A 24-line level VII buff cycle burns about 17.75
  units on the full formula today (8.7 of them coloured tapers) and about 11 Prismatic Tapers plus 0.09 Platinum on
  the foci formula. Burn per slot is `ComponentLoss x CDM x min(1, Power/skill)` (`Spell.cs:157-189`).
- **Stock and burden.** Today's kit weighs about 17,000 of 21,000 burden (item 0 read 22,034 on BSD Mage: m5-plan.md §8) and leaves about 4,000 for loot, so burden,
  not money, sets the trip rate; with the foci a trip-sized kit is a few stacks of tapers and scarabs (under 2,000
  burden with the foci's 1,600) and the trip rate is set by the loot. Platinum Scarab 8897: value 10,000 (13,500
  pyreals at a vendor's 1.35), burden 4, a stack of 100.
- **Holtburg's vendors** (landblock 0xA9B4; `BuyPrice` 0.9 and `SellPrice` 1.35 unless noted): Shopkeeper Renald the
  Elder 718 (56 m from the lifestone; buys melee, armour, clothing, food, misc, missile, container, useless, writable,
  key: the loot buyer); Sedor Wystan the Blacksmith 712 (0.95 / 1.25; buys weapons, armour, tinkering material);
  Monyra the Jeweler 716 (jewelry, gems); **Archmage Cindrue 2304** (142 m from the lifestone; buys components, scrolls,
  casters, mana stones; sells Lead 691, Iron 689, Copper 686 and Silver 688 scarabs, the Prismatic Taper 20631 at 30
  pyreals, the 12 coloured tapers, the herbs, powders, potions and talismans, Minor and Lesser Mana Stones, the Tiny to
  High Mana Charges, Wands 2472 and 5539, apprentice robes, trade notes, nine town Portal Gems); Thelnoth Cort the
  Healer 715 (161 m; Mana Potion 379 at 230 pyreals, Mana Draught 2460, Healing Potion 377, the kits).
- **What Holtburg does not sell:** Gold 687, Pyreal 690, Platinum 8897 and Mana 37155 scarabs, Diamond, the Foci. The
  nearest Platinum, Pyreal and Gold seller is Archmage Philomon 5646 (cell 0x95D6012B), 31.6 map units away; the
  Foci of Strife 15271 is sold by the Scriveners of War Magic in the neighbouring landblock 0xA9B3. So every level VII
  and VI cast needs a scarab Holtburg cannot restock; at 1.7 to 6 Platinum a two-hour trip it is a stock of 20 to 50
  (270,000 to 675,000 pyreals) that lasts 10 to 30 trips, or a long trip. D8: the build grants the first stack; a
  stock-up trip is a profile step for a later item, planned over the corpus's portal router.
- **The trip with today's pieces:** Lifestone Recall 10 s to present, 170 to 250 m of walking (20 to 40 s), a sale of
  about 13 s at Renald, a purchase the same, Primary Portal Recall 11 s and a 3 s settle: **70 to 110 s a trip**. At
  a sale every 30 kills the trip costs about a fifth of the fighting time; every 100 kills, 6%.
- **Unknown until item 0:** the average sale value of the den's tier-4 loot (the tuskers carry Tusker Spit, Tusker
  Head, letters and the treasure profile's tier-4 rolls), the travel spells' formulas, `ComponentLoss` below level VII
  (the live cast facts suggest about twice as high), the mage's run speed, the respawn delay. Item 0 took the run speed
  (10.4 m/s on the archer's legs), the respawn delay (903-955 s) and Flame Bolt VII's burn on the Foci of Strife (0.43
  Prismatic Tapers a cast, no scarab in 118 casts) and confirmed Cindrue's and Thelnoth's stock and prices and the four
  Scriveners in 0xA9B3 (152-158 m from the lifestone); the loot value, `ComponentLoss` below VII and the travel spells'
  formulas stay open (m5-plan.md §9).

### 5.8 Days-long runs

The longest live runs to date are about 25 to 65 minutes (a gate's journey lanes, the 1,800 s buff expiry test, the
Soak tier's 33 minutes). The long-run audit's ranked risks, with the fix size:

| # | Risk | Evidence | Fix |
|---|---|---|---|
| 1 | Objects the server has stopped sending are never dropped: the client lacks retail's 25 s client-side cull, which ACE relies on (`Managers/ObjectMaint.cs:21,41`); a den reloaded after any absence over 5 min (`Landblock.cs:113,118,532`) fills with frozen ghosts the fight and the wayfinder treat as live; at 256 drawn objects the light table fills (`ObjectLightTable.cs:27`); from about 6 h ACE reuses guids (`GuidManager.cs:126`) and new objects merge into stale entries | the hosts, ACE | M (the cull; the interim is a log-off and on each cycle) |
| 2 | `NavRunner.Steer` throws every frame on a route of one waypoint (`:1022-1029`): the last gate's melee journey wrote 8,642 traces and 43.8 MB in 277 s while `smoke.ps1:691-693` read "ALL PASS, Errors logged 0", since it counts Lifestone's own ERROR lines only | the gate log | S, plus Godot's errors counted |
| 3 | Nothing loops; every host quits at its plan or its limit | the hosts | L (item 4) |
| 4 | A dropped session is never recovered: the client gives up after 20 s of silence (`SessionTimeouts.cs:13,49`), ACE drops a client silent for 60 s (`NetworkSession.cs:336-338`), `Reconnect` is the disconnected screen's button (`DisconnectedScreen.cs:81`) | | M |
| 5 | The world and the character drift: spawns deplete (`m4-hunt-band-empty` seen live), vitae, components, item mana and pack space build up | the journeys | M (the upkeep arrows) |
| 6 | Memory is 3 to 5 times the 400 MB target and unmeasured over time: a fly path measured a 1,462 MB working set; an 8.5-minute journey ran about 10 gen-0 collections a second; each shell build commits up to 512 MiB (the no-GC window) | `PerfOverlay`, the gate logs | S to measure, M to fix |
| 7 | Logs unbounded and written three times (`lifestone.log`, Godot's `godot.log`, the console): 1.6 MB an hour a copy, 4 GB an hour under risk 2 | | S |
| 8 | The frame rate uncapped: the gates ran at 239 to 241 fps | the gate logs | S (`--max-fps=60`) |
| 9-14 | an unsupervised backend; checks that miss Godot's own errors and summaries written only at exit; Windows sleep and updates (the owner's machine settings); server objects not solid (§5.11); the sky scroll in float; recording growth (about 8 MB an hour headless) | | S each |

Already in place: the no-GC window and the allocation-free ticks, the session recorder, the kill switch, `Reconnect`,
the frame samplers, the audio mute, the ring caps (the action log 1,024, the chat 500 lines, the avatar notices 1,024).
The soak's measurement plan is DoD 13; its three prerequisites are risks 1, 2 and 3 with 4.

### 5.9 Retail coverage

The wire (every count a grep over ACE's enums and Lifestone's codecs): ACE sends 86 of its 104 GameEvent types and
handles 149 of its 163 GameAction types; Lifestone decodes and reduces 31 of the 86 (36%) and sends 36 of the 149
(24%); of the top-level messages ACE sends it covers 46 of 57 (81%). Complete: combat, inventory and containers,
vendors, movement, raising and training, the system text. Nearly: magic (7 of 8 events), world objects (the four
public property updates missing). Three events arrive unknown at every login: SetTurbineChatChannels 0x0295,
CharacterTitle 0x0029, FriendsListUpdate 0x0021.

The interface: of 98 retail rows, 33 built, 25 partial, 38 not built (59% with a controller); 8 of the panel host's 16
pages work; 1 of 9 persistence rows (the shortcut bar); 47 of 49 character options ghosted.

| System | Wire gaps | Interface | Size | M5 |
|---|---|---|---|---|
| character options and settings | SetCharacterOptions 0x01A1, 0x01A2 | 2 of 49 options honoured; layouts, masks, key maps unsaved | M | DoD 11 |
| Yes/No confirmation | 0x0274, 0x0276, 0x0275 | none | S | DoD 11 |
| spellcasting bars | SpellbookFilter, RemoveSpell, SetDesiredComponentLevel | not editable, hotkeys unbound | M | DoD 11 (the bars and hotkeys) |
| chat | TurbineChat F7DE, ChannelBroadcast, Emote, SoulEmote, squelch, AFK (18) | speech and `@` only | M | DoD 11 (tells, `/e`, `/afk`, the recalls); channels M6 |
| recall commands | TeleToLifestone 0x0063, TeleToMarketPlace 0x028D | | S | DoD 11 |
| public property updates | 0x02D2, 0x02D4, 0x02D5, 0x02D6 | a lock stays stale | S | DoD 11 |
| examine everything | QueryItemMana, SetInscription | only from the pack and paperdoll | M | M6 |
| books, journal, contracts, map | 10 + 3 messages | pages Not in M3 | M each | M6 |
| crafting, tinkering, salvage | SalvageOperationsResult, CreateTinkeringTool | no use-on-target mode, no salvage window | M-L | M6 and the crafting bots |
| fellowship, secure trade, friends, titles | 13 + 14 + 8 | tabs not built | M | the service bots' milestone |
| allegiance, housing | 31 + 26 | not built | L each | later |
| chess, barber, PK, /age | 19 | not built | S-M | later |

### 5.10 The knowledge corpus

The lore, history and world data the layers above need exist in the owner's ACOracle corpus, a sibling checkout at
`<repo>\..\ACOracle` (not part of Lifestone; nothing of it is committed here): the end-of-retail world as Markdown,
JSONL and a SQLite full-text index (4,723 NPCs with their dialogue and quest logic, 4,608 creature templates with
stats, resistances, spells, spawn points by area and drops, 33,111 items, 4,237 quest flags, 6,266 spells, 7,272
recipes, the towns, portals and dungeons, 1,462 books), four sourced history reports (the shutdown, the player culture,
the lore and systems, the Decal ecosystem with its buff, portal, trade and service bots), three wikis and the wiki
frozen on shutdown day, a travel router over every portal's position and destination, and an MCP server a Claude
session can query. Rules for its use in Lifestone (D11): a planning session or a lead queries it and writes what it
took into the brief or a doc with the corpus path cited; a derived profile in this repository names its source (ACE's
world data, which Lifestone already reads by SELECT, is the same data); wiki text is summarised, never copied; the data
wins for mechanics and the wikis for walkthroughs; the corpus's emulator-custom pages are never presented as retail.
M6's seed list of grounds (DoD 15) comes from its creatures' spawn points, levels and experience.

### 5.11 The world's solidity

Server objects have no physics on the client (`TODO(d25-server-object-host)`, docs/physics.md "Other objects"): the
player walks through closed doors (the Brawler walked into Renald's shop through its door in the hero takes), creatures
stuck to the player overlap and snap back when a stick ends (the hero takes' flicker, reproduced offline:
`AvatarJumpWatchTests`), and a recall lands inside the lifestone, where ACE places the character 2.2 m out at the next
login. The plan is written on the branch `m4/hero-visual-bugs` (docs/milestones/m4-hero-visual-bugs.md §4): the host
(one slice: a `PhysicsObj` per server object with collision, made on create, moved with `ForceIntoCell`, ethereal with
its state, removed on delete), doors in the journeys (one slice: a closed door blocks the planner, a leg uses it and
waits for the ethereal state), and the transitions for server objects (a larger item: the avatar body's step runs a
transition through its host object, measured against the 50 ms frame rule before it lands). The physics side is built
(the shadows, retail's object-collision rules, doors' BSPs posed when they move, `SetEthereal` with its overlap retry).

## 6. Decisions

The owner's answers in the workshop are m5-plan.md §7 D1-D11; the planner's defaults D12-D24 there may be overruled at
any checkpoint.

### 6.1 The owner's questions, as asked (answered: see m5-plan.md §7)

| # | Question | Answer |
|---|---|---|
| Q1 | The whole dungeon, or the arrival floor as M4 D12 says | capable of the whole den; in practice never the drudge and virindi wing (D4) |
| Q2 | Which character runs the autopilot | the test character, BSD Mage; a character of the owner's own later (D5) |
| Q3 | The backend up for days during the soak | yes (D6) |
| Q4 | The server's experience multiplier | no; retail rates (D7) |
| Q5 | The Platinum Scarabs Holtburg does not sell | the build grants the first stack; a stock-up trip is a profile step later (D8) |
| Q6 | The window size to watch | the window is resizable; fix the camera's field of view on resize; test 1280x720 and 1920x1080 (D9) |

### 6.2 Carried rules

M4 D23 (no creatures spawned for testing; the combat mode matches the weapon), D28 (coverage, not count), D31 (the
parallel live gate), D37 (a character that must die undresses and lies down), D39 and D43 (the four accounts and
Lifestone Tools), D41 (the fighters' retail magic gear), D44-D46 (fresh journey characters each gate; `slice-check
-Gate` as G-A), D47 (the journeys' judges); M3 D42 (frames judged solo); M2 D57 (800x600 for every check). CLAUDE.md's
"Hard rules" and "Rules for agent sessions" stand for every agent.

## 7. Status

Planned 2026-10-25 (m5-plan.md §0). Item 0 done 2026-10-25 (its row below); item 1 next. The audit table below is filled by the end item, one row per DoD item,
with the verdict (met, met with named gaps, not met) and the evidence (tests, loopback classes, stations and cycles,
frames, the soak's numbers).

| DoD | Verdict | Evidence |
|---|---|---|
| 1 The measuring run and the two live bugs | met with named gaps | m5-plan.md §8 item 0: the measures ([M], [D], [S]; the loot value, `ComponentLoss` below VII and the travel formulas open); `ObjectCullTests`, `ObjectCullCellsTests` (the den's PVS on the dats), `NavRunnerTests` (one waypoint), `LegFaultGuardTests`, `GodotErrorTallyTests`, `SoakLineTests`; the gate's `godot_errors=0` on all 12 Godot rows (D19); the cull's fields report-only; the archer's hunt flake (D28) |
| 2 The outcome-aware caster | | |
| 3 Mana inside the fight | | |
| 4 The hunting-ground profile | | |
| 5 The whole den | | |
| 6 The mage's logistics, cheat-free | | |
| 7 Death inside the loop | | |
| 8 The outer loop | | |
| 9 The dial | | |
| 10 A solid world | | |
| 11 Retail for the person at the keys | | |
| 12 Window sizes | | |
| 13 The 24-hour run | | |
| 14 Tests, gates and frame time | | |
| 15 The end | | |
