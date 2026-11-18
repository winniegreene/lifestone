# Milestone 6: the level ladder

**Status: planned 2026-11-01 on master c6b4221a (M5 complete).** The plan of record is [m6-plan.md](m6-plan.md):
the waves, items, gates, decisions and open TODOs. This spec holds the goal, the definition of done with a check per
item, the scope, the research the plan stands on, and the owner's questions. Section numbers are stable: append, never
renumber. M5's decisions are cited as "M5 D<n>" (m5-plan.md §7); this milestone's own are D1-Dn (m6-plan.md §7).

## 1. Goal

**A war mage that starts at level 1 and climbs to 126 by itself, cheat-free, and a menu where the person asks it for
a level or a quest.** M5 proved one mage in one dungeon at level 80 with a character the mod built. M6 takes the mod
out of the start of the story and runs the whole road: the autopilot creates a fresh mage on the test account with no
grants (D7), runs it through the Training Academy and the Pathwarden chest, climbs the tusker dungeons of Aphus Lassel
from level 10 to 126 (the island road, D17), buys its scrolls and scarabs, dresses itself, dies and recovers, and does
the quests of the first tranche when they are open: the kill tasks, the tusk turn-ins and the other trophy quests, the
fetches, the Atlan Wand, and the daily round from level 100. The person can take the keys at any moment (`/stop` and
`/auto`, M5 D145). The ladder, every ground and every quest are data.

- **The climber.** `Winnie` on the account `test` (D7): created by the autopilot at the start of each gate from the
  creation template (no Alchemy, D34; no Melee Defense, D5), no grants: the retail creation and ACE's starter kit
  only. Quest gates above the ladder's reach run on a character built to the level (granting for testing continues,
  D10); the quest's own steps run cheat-free.
- **The road** (D17; the corrected rungs of §5.2): the Academy and the Pathwarden chest to 5; Holtburg's outskirts and
  the Redoubt to 10; Tusker Burrow, Lodge and Cave to 32; the Tusker Temple 20-40 to 45; the Tusker Abode to 60; the
  Tusker Quarters and the Tusker Temple 40+ to 75; **the Tusker Holding from 75 to 126**. The alternations (Hills
  Citadel for the element per ground, Mountain Keep, the Habitat, the Barracks, Phyntos Menace, the den) are grounds the
  ladder may use at their band; the Olthoi Horde Nest is completion only, M7 (D13).
- **Spells by level.** The spell plan's level is the highest the character casts at 95% or better (D4, M5 D26
  amended): II from the start, III about 10, IV 20, V 30, VI 43, VII about 70. Scrolls II-V from Fort Tethana's
  scriveners, VI from Stonehold's; **VII is a loot rule** (no vendor sells it; tier 4 and 5 loot drops it). Scarabs by
  the bolt line; the carried-only scarabs (Pyreal, Platinum) restocked by a buy-trip (D12, M5 D148).
- **Dressed by rule.** The vendor floor, the bonded quest kit, scored loot upgrades, two casters always, savings in trade
  notes; from VI on the spells are the armour.
- **Quests as data, four shapes.** A quest profile is a ground profile's sibling: level, timer, NPCs, items, ground,
  and steps from a small vocabulary (go, hunt-until, pick-up, give, talk, buy, combine, apply, use-key, use, wait,
  recall). The shapes: **kill task**, **trophy turn-in** (the island's tusks first: one tailor, a reward at every rung),
  **fetch**, **craft chain** (the Atlan Wand). A shape is code; a quest is a file.
- **The daily round.** The 20-hour timers kept per character; from level 100 the round planner runs the open quests at
  the character's level by experience per hour between hunts.
- **The automation menu.** The dial grows a job list: hunt here, level up, do a quest (the catalog filtered to the
  level and the open timers, the experience beside each), errand, stop.
- **The leveling optimizer.** One seat that owns the fastest 1-126 plan as data and a time model, kept current from the
  live logs (m6-plan.md §2.1, D18).

## 2. Definition of done

Each item names its check. A "rung" is one row of the ladder profile; a "climb" is the autopilot's `level up` job from
the character's creation; a "quest run" is one profile from its first step to its reward line; a "gate" is unattended,
with no grants on the ladder's character, judged on the `[ladder]` line (level, rung, kills, XP an hour, deaths,
scrolls bought, spent XP, quests done, `granted=`) and the frames. Every judged field reads the state it names (the
level from the character's own property, the rung from the profile the loop is on, the scrolls from the spellbook).

1. **The ladder as data.** `Ladder` (embedded JSON, one loader, a missing or unknown field fails at load naming the
   file): ordered rungs {ground key, level band, the move-on rule (a level, a kills-an-hour floor, a casts-a-kill
   ceiling), spell level by the fizzle rule, element, how to get there}. Every rung's ground is a `HuntingGround`
   profile passing the dat checks. `LadderTests`, `HuntingGroundDatTests` over every rung.
2. **The fresh character.** Created by the autopilot (the template without Alchemy, D34; no grants), the Training Academy by the tutorial's own skip (D37: Jonathan's Exit Token given back for the server's teleport to Holtburg; the full route's steps stay in the file for item 4, `TODO(m6-academy-sentry-ramp)`, `TODO(m6-academy-hive-ramp)`), which reaches level 5 (the live round of 2026-11-01: level 1 to 5 on the skip's 11,000 XP, 6 after the Pathwarden), the Pathwarden Token given, the chest opened, the trinket and robe worn, and the gauntlets and sollerets worn when her Strength carries them (the robe covers the feet, ClothingPriority 0x17F00, so ACE refuses the sollerets under it: F1, `TODO(m6-pathwarden-robe-covers-feet)`; the chest's burden gate: F8, `TODO(m6-climber-strength)`). Loopback: `academy_done=yes pathwarden_done=yes trinket_worn=yes` with `granted=0`.
3. **Spells by level, the buy-trip and the VII rule.** The plan's next level bought at the profile's scrivener and
   read; the level chosen by the fizzle rule as a plan value, never a level constant (D4); the components per line; the
   skill credits spent on the template's order and the experience by `XpSpendRule` from level 1; **the buy-trip**: a
   journey station to the town whose archmage sells the carried-only scarab at its floor (D12); **the VII loot rule**:
   a scroll of the plan's next level found in loot is kept and read. `SpellPlanTests`, `SkillCreditTests`,
   `BuyTripTests`, `ScrollKeepRuleTests`; loopback: the first VI learned at the band the plan names; one buy-trip line.
4. **Dressed by rule.** `GearScore` from an appraisal against the ground; the equip pass after every loot trip; two
   casters always (under two is a loud stop); the rest sold; attuned pieces returned or dropped; savings as trade
   notes; a caster's own mana recharged by rule (the Atlan wand's 500 drains in about 5.5 h). `GearScoreTests`,
   `GearFloorTests`, `CasterManaRuleTests`; the cycle line reads `casters=2`.
5. **Travel between rungs.** The Town Network (Oolutanga's Refuge from level 10), the portal gates by level (the Temple 20-40 by New Colier 12527, not Neydisa; the Facility Hub's portals to the island dungeons open by level, 15 to 40; the Holding's far half, past its pit, needs a jump: TODO(m6-abode-pits)), the island's
   surface runs with water, the first-visit tie (Primary Portal Tie from level 20; the island's entries are NoRecall, so the first-visit tie is the Temples', D51), the Aphus Lassel Portal Gem as the
   fallback (the gem is item 5's: Brighteyes's tusk turn-ins); the rung change as a journey station with check, act, check (M5 D141). Loopback: one rung change per gate;
   `RungChangeTests`.
6. **The grounds.** The spine's seven new profiles (Burrow, Lodge, Cave, the Temple 20-40, the Abode, the Quarters,
   **the Holding**) and the alternations of §5.4 the plan names, each from the draft builder plus the hand-set fields,
   with its element, its targets and their stats (health, Magic Defense, attributes, swing time), each passing the dat
   checks; every count confirmed by one SELECT (D28). The Holding's 30-minute read on a built level-80 character.
7. **The quest profile and the kill task.** `Quest` (embedded JSON, one loader, the same loudness), the step vocabulary (item 5's six kinds: kill, hunt-until, buy, recall, confirm, wear), the NPC's reply read, the reward line read, the timers kept per character (a kill task's 20-hour wait is the giver's stamp), the server's Yes/No answered by the headless rule (`m5-6-headless-confirm-reply`, closed in item 5). The kill-task shape is proven on the Blood Shreth Kill Task (San Ming at Shoushi; 10 Blood Shreths at the Cave of Alabree, D54); the Tusker Guard Kill Task (Shoichi at Lin; 500 kills; the Temple 40+ and the Barracks) is a quest file. The climber's own call of a kill task is item 9's round (D54, DoD 12). `QuestProfileTests`, `KillTaskTests`; loopback: `KillTaskLiveTests` on the Throwaway, one kill task completed and its experience line read (D60, D62). Gate A needs no kill task.
8. **Trophy turn-ins.** `hunt-until {item, count}` on the corpse looter's want list, then `give`; **the island's tusks** (Brighteyes the Tailor in Oolutanga's Refuge; fourteen, Female to Rampager). Item 5 writes the nine whose grounds are profiled (`tusk-female` to `tusk-rampager`, one file each, checked by `QuestCatalogSqlTests`); the five unprofiled tusks (Armored, Silver, Plated, Assailer, Devastator) wait for item 10 (`TODO(m6-tusk-unprofiled-grounds)`). Each tusk's 20 hours is its pickup's (`Tusk<X>Pickup`, D57), not a stamp at the turn-in. Then the Insatiable and Ravenous Eater Jaws, Lost Pet and Faces of the Mukkir (Expert) as profiles, which item 9 places (not item 5). `TrophyQuestTests`; live: one tusk turned in on the climber in gate A (D62's loopbacks prove the hunt-until and the give on the Throwaway first); one of the four others on a built character in item 9, the reward line read.
9. **Fetches.** A dungeon route to a floor item or a keyed chest (the room graph and runner; the locked-door rows), the
   pick-up, the way back; the Repaired Shadow Stone, the Shield of Yanshi and the Minor Smoldering Stone as profiles.
   `FetchQuestTests`; live: one of the three run on a built character.
10. **The craft chain: the Atlan Wand.** `combine` and `apply` through `UseWithTarget` with the recipe outcome read
    (success, the skill-check failure, the item consumed, the untrained refusal), 16 Pyreal Motes hunted at Crater
    Caves, the glyph bought at Crater Lake, the ingot handed to the Magma Golem, the fire stone applied; run when base
    War Magic reaches 290. `RecipeOutcomeTests`, `AtlanChainTests`; live: the wand forged once on a built character.
11. **The quest draft builder and the catalog.** A quest's profile drafted from the corpus (the walkthrough for the
    steps and coordinates, the NPC emote scripts for the give-to-reward logic, the weenies for the wcids; read-only,
    cited by path, nothing of the corpus committed, M5 D11) and finished by hand; the tranche's quests in the catalog;
    the catalog's dat and world checks; each quest's experience checked against the server's own quest data.
    `QuestDraftTests` (`Tier=Tool`, lead-run), `QuestCatalogTests`.
12. **The daily round.** The round planner: the open quests at the character's level, ordered by experience per hour,
    run between hunts; the `round` line (quests, experience, hours). `RoundPlannerTests`; live (gate E): a built
    level-100 character runs one round of the tranche and the island's tusks, the experience a day against §5.6's
    420 M.
13. **Death on the ladder.** Vitae earned off, the corpse recovered by the runner when the mage holds no tie, the items
    back, the climb resumed; a retreat rule for grounds other than the den (`m5-fight-death-midrun`). Loopback: a
    scripted death at a low rung recovered.
14. **The automation menu.** The dial's job list on screen and in the inspector; `level up` runs the ladder and the
    round; `do a quest` lists the catalog filtered to the level and the open timers with the experience beside each;
    `/stop` and `/auto` made robust (`m6-chat-controls-robust`); the hand-back resumes. Play-checks and frames.
15. **Gate A, the start.** Level 1 to 20 unattended in one night on a fresh `Winnie`, no grants: the Academy, the chest,
    the outskirts, the Burrow from 16, the Lodge from 17 behind its gate, the rung changes the ladder asks, the first
    scrolls, one tusk turned in (the Burrow's Female Tusk, by the owner's decision of 2026-11-06: m6-plan.md D100, which
    changes D76's Male Tusk); the display held on. Item 7's run reached level 20 with `granted=0` (the record is
    m6-plan.md section 8).
16. **Gate B, the middle.** Level 30 to 45 over a weekend on a character built to 30, at the Temple 20-40 and the Abode
    (the Minions' Acid Stream, the first level-60 targets): the rates file's first measured rows against §5.3, a death
    recovered.
17. **Gate C, the rungs.** Every other rung 30 minutes at its band on a character built to the band (the Cave, the
    Quarters, the Temple 40+, the alternations the plan keeps): kills an hour, casts a kill, deaths, the frames.
18. **Gate D, the Holding (the proof of 80-126).** The first trip to the Holding (the run and the tie), a 10-hour soak at
    level 80 with VI and the vulnerability opener, then 6 hours at 100 with VII and the Atlan wand (granted for the test,
    D10): 14 M XP an hour or a named reason, tier 5 loot kept, the VII loot rule and the buy-trip seen once, 0 reconnects,
    M5 item 7's memory and GC lines. Then the standing job starts (D15): `Winnie` climbs from 1 on
    the owner's machine, its log read into the rates file daily; its finish is M7's.
19. **Docs and the optimizer's plan.** docs/session.md's ladder and quest sections, the README's status and controls,
    automation-vision "as built"; the rates file and the optimizer's memo (measured beside modelled, every rung); M7's
    seed: the quest scan and the rows this milestone places M7.

## 3. Scope

### 3.1 In

The nineteen items; the mage only (D8); the island road from 1 to 126 (D3); the spine's grounds and the alternations
the plan keeps; the golem ground; the four quest shapes; the tranche's quests and the island's tusks; the daily round;
the menu; the buy-trip; the VII loot rule; the leveling optimizer seat; the local model lane (m6-plan.md §2.2).

### 3.2 Not in M6

- **The fighters' ladders** (M7, D8; M5 D2 amended): they reuse M6's files.
- The Olthoi Horde Nest (completion only), the Major Atlan stones (jumps and levers), the catalog beyond the tranche
  (M7 adds files), the War Room and the Abhorrent Vault at 110+ (M7's upgrade grounds, §5.4).
- Melee Defense (D5); the Temple of Forgetfulness refund.
- Patrons, crafting as a layer, the social systems, the retail panels nobody needs for the ladder (m6-plan.md §9's
  triage places them M7), LAN.

### 3.3 What M5 leaves to M6

The hunting-ground profile and its builder (M5 item 2), the logistics and the death handler reading the profile (item
3), the outer loop and the dial (item 4), a solid world (item 5), the soak's measures (item 7), and section 9's rows:
every row placed M6 and the "At M5's end" table, triaged in m6-plan.md §9.

## 4. The order and the data-first rule

M5's §4 holds: every capability lands on the Action API and the autopilot first, the retail UI second; every fact of a
rung, a ground or a quest is a profile value; a lead refuses a slice that adds a rung-, ground- or quest-specific
constant. New in M6: **a quest is data**, in the same form as a ground, drafted from the corpus and finished by hand; a
shape is code, a quest is a file. The runner judges by what the client can see: the NPC's reply, the item arriving, the
experience line. Every automated action is check, act, check (M5 D141); a walk always has a purpose or holds (M5 D47).

## 5. Research (read-only, 2026-10-27 to 2026-11-01; the notes are gitignored under `cache/m6/`)

The working notes: `cache/m6/level-ladder-note.md` (the first survey), `cache/m6/m6-strategy.md` (the strategist's
memo of 2026-10-31, which corrects the rungs), `cache/m6/dungeon-shortlist.md`, `cache/m6/atlan-wand-plan.md`,
`cache/m5/xp-optimization.md`. Tags: **(measured)** in M5's live logs; **(computed)** from ACE's source as pinned, the
client's tables and the corpus's world data; **(assumed)**. The corpus is the end-of-retail world; this server runs the
pinned world SQL v0.9.296 and the two differ in places, so every number the plan builds on gets one SELECT in item 0
(D28).

### 5.1 The arithmetic

- **The curve** (the client's experience table; computed): level 80 is 493,005,039 XP, 100 is 1,418,089,761, 126 is
  4,286,609,098. **1-80 is 11.5% of the road; 80-126 is 88.5%; 100-126 alone is 67%.** The optimizer's leverage is above
  80.
- **The kill** (measured, M5 item 7): a den kill takes 19.6 s of fight time (7.6 attack casts' worth, 2.6 healing, 1.3
  stamina-to-mana, 1.6 loot, 0.3 buffs, 6.2 walking and choosing); the long run made 189 kills an hour over 606 minutes
  with 13 trips and 0 deaths. Cast times by level were measured in M5 item 0 (I 0.63 s to VI 2.40 s, plus 0.85 s of
  loop).
- **Damage and resists** (ACE `SpellProjectile.CalculateDamage`; computed): every bolt line of a level does the same
  base damage; resists against Magic Defense run 0.5-5% for a mage on the planner's spend, 1.6% on a Rampager.
- **Fizzles** (ACE `GetMagicSkillChance`; computed): comfortable (95%) at 43 over the spell's power (M6 item 1; 42 over is 94.98%): II from the start,
  III about 10, IV 20, V 30, VI 43, VII about 70 (a mage holds the dance from the first trip, and Mana Conversion only makes it cheaper: D74; M6 item 1: on the base skill the plan reads III at 21, IV at 31, V at 44, VI at 74 and VII at 126; session.md section 66).

### 5.2 The road (computed; every count a DB check in item 0)

| Band | Ground (landblock), gate, slots, respawn | Target: level, XP | Spell | XP/h | Hours |
|---|---|---|---|---|---|
| 1-5 | the Training Academy, the Pathwarden chest (Holtburg) | the quest's own | I | | 1 |
| 5-15 | Holtburg's outskirts and the Redoubt (item 7's measure: 53,700 to 72,800 XP/h at level 10 from the Shreth list; 1 to 15 took 22.2 h on the first run, 1,040 XP a kill, about 14 kills an hour against a floor of 30 until the spawn-list fix: D85) | drudges, mites L8, the Gnawer Shreth L8, 1,000 | I-II | 0.09 M (assumed; measured above) | 1 (22 as first run) |
| 16 | Tusker Burrow (0x5748), its own gate (Armor Self IV, Bludgeoning Protection IV, the bootstrap), 42 Female, 5 min; the Female Tusk (100,000 XP) is gate A's tusk by a pull from a stand (D92, D100) | L15, 2,000 | II-III | 0.03 M by the model at level 10 (30,500 XP/h, AB; 80,500 with the dance, AD): not a hunting rung past the tusk | 1 |
| 17-21 | Tusker Lodge (0x5749), gate 5 and the ladder's conditions (D89), 57 Male, 4 min; the Cave's gate (Armor Self V, Bludgeoning Protection V) holds her past 21 | L20, 3,500 | II-III | 0.14 to 0.17 M modelled (AD), unmeasured long; stretch 16: 36 kills in one 21-minute fight | 2 |
| 22-32 | Tusker Cave (0x5B46), gate 12, 81 Crimsonback, 5 min | L30, 5,000 | IV | 1.2 M | 5 |
| 32-40 | Tusker Temple 20-40 (0x5D43), 59 Minion + 85 L30, 5 min; the portal admits 20 to 40 (ACE Portal.cs:172-176 ef426d refuses a player above a portal's MaxLevel while use_portal_max_level_requirement is true, read as 1 by SELECT in M6 3) | Minion L40, 7,000 (casts Acid Stream III) | IV | 1.6 M | 17 |
| 41-60 | Tusker Abode (0x5C45), gate 20, 121 Redeemer, 5 min; from 41, after the Temple 20-40 ends at its portal's MaxLevel 40 | L60, 17,500 | IV-V | 3.1 M | 30 |
| 60-75 | Tusker Quarters (0x5B4A), gate 30, 79 Slave; the Temple 40+ (M5's profile) | Slave L80, 30,000 | VI | 5.0 M | 47 |
| 75-126 | **Tusker Holding (0x5B48), gate 60, 184 Rampager, 5 min** | **L100, 80,000**, 435 health, no spells, Magic Defense 230 (about 251 with Focus and Self) | VI + vuln, then VII | 14.3 M | 74 to 100, then 100-126 |

1-80 is about 113 hours by the model and up to 170 with M5's kind of slippage; 80-126 is about 265 hours with today's
kit and about 219 with the Atlan wand and VII; with the daily round from 100, spawn to 126 is about 11-13 days of
unattended play, about 16-18 without it (computed; the low rungs assumed). The draft's rungs were mis-described (the
Abode holds Redeemers at 60, not Minions at 40; the Quarters and Barracks hold level-80 Slaves and Guards); the
strategist's memo §4 lists each correction.

### 5.3 The Holding (computed; gate D measures it)

A Rampager pays 80,000 XP for 435 health (item 0's SELECT: the start value 280 plus half its Endurance of 310, as ACE's
`CreatureVital.MaxValue` adds them; the corpus's 590 was wrong), 184 XP a hit point, against the Guard's 115; its Magic
Defense is 230 at start, about 251 with the (Focus + Self) / 7 part ACE's `CreatureSkill.Current` adds. Strength 230, attack
about 250 against the mage's untrained defence: it lands about 82% of its swings, a Guard 94%; no spells, no missiles;
the robe with Impenetrability VI and Armor Self VI takes 86% off each hit. **The den with bigger pay, not a harder
fight.** Tier 5 loot: every scroll drop is level VII. No portal leads in: the route is the Facility Hub's Barracks portal
(gate 40) and about 1.5 km over water to the west and south, or 2.8 km from Oolutanga's Refuge; the portal is NoRecall, so there is no tie (ACE's Portal.NoTie is NoRecall, Portal_Properties.cs:14-16 ef426d; tusker-holding.json entry.tieable is false): every trip back is the water run again (TODO(m6-holding-tie-first-visit), item 11, re-scoped to that cost; D51). Since M6 item 4 the travel file runs the 2.8 km from the Refuge first (2,870 m planned on the dats): the Facility Hub's way down from its drop does not plan yet (`TODO(m6-facility-hub-rooms)`, M7), and item 4's live round 5 met a level-80 Tusker Slave on that run (`TODO(m6-holding-run-slaves)`, item 11). The move is placed at 75 (the optimizer may pull it to 65 on the live read). If the Holding fails its
30-minute read, the Quarters and the Temple 40+ are the 80-100 ground and the hours above 80 double.

### 5.4 The grounds (the seed list amended; m5-first-dungeon.md §7.1 is the first issue)

Spine: the outskirts and the Redoubt, Burrow, Lodge, Cave, the Temple 20-40, the Abode, the Quarters, the Temple 40+,
the Holding. Alternations: Tusker Cavern (0x5C43), Tusker Habitat (0x5B47), Tusker Emporium (0xF784, outdoor), Mountain
Keep (0x01D2; the trap and avoid-zone proof), Hills Citadel (0x02F0; lightning, the element-per-ground proof), Tusker
Barracks (0x5A48), the Black Spawn Den (home and fallback), Phyntos Menace (0x0143; crowds, casters, the owner's
dungeon), Plated Rifts (0x5B49), Rampage Bluff (0x5C48). Quest grounds: Crater Caves (0x019A; frost, the Atlan hunt),
Vengeance Caverns (0x5D46) and the Chapel of Mowen (0x594A) for the Silver and Armored tusks (item 0's SELECT puts those tusks' holders in the Tusker Pits 0x5A4A and the Tusker Armory 0x5948, the Plated tusk's in the Tusker Tunnels 0x5D45: §5.7). M7: the War Room (0x0010,
level 115 drudges, about 25 M an hour computed), the Abhorrent Vault (0x02D4), the Crystalline Portal (0x8803), the
Elemental Caverns (0x5F47), the Olthoi Horde Nest.

### 5.5 The steps between the bands

| Step | When | What | Source |
|---|---|---|---|
| The Academy and the chest | 1-5 | the Calling Stone, three leather pieces, the token to Thorolf; the robe, gauntlets, sollerets and trinket (+4% XP) | ladder note §7, §11 |
| The casters (two, always) | from 5 | Holtburg's archmage Cindrue (2304) sells the Wand 2472 at 270 (Value 200 at her 1.35), so the outskirts' loot trips buy it; her Mana Charges 4612-4615 are the casters' mana (her stones are sold empty) | pinned world SQL v0.9.296, create list row 21071; m6-plan.md section 9 (item 2) |
| Scrolls I-VI (M6 item 1; was II-V from Fort Tethana and Sanamar) | by 10, 20, 30 | Holtburg sells every school I-IV on foot (Apprentice I and II, Journeyman III, Scrivener IV); Sanamar sells I-IV; level V only at Plateau Village, Qalaba'r, Dryreach (Town Network) and Westwatch; level VI only at the Grand Master Scriveners (Stonehold, Fort Tethana and four more). Prices are 50 times the Value: VI is 50,000 pyreals | pinned world SQL (`cache/m6/1/sel-scriveners.tsv`); session.md section 68 |
| The travel spells (M6 item 1; was by 20) | by 16 | Lifestone Recall and Tie, Portal Recall, Primary Portal Tie (level 3 Item Enchantment, Fort Tethana). Item Enchantment is trained at 16 from the credits (D43: Creature at 5, Item at 16, Mana Conversion at 32). The island's entries are NoRecall (PortalBitmask 49), so the first-visit tie is the Temples' (D51) | ladder note §3; session.md section 67 |
| Scarabs I-V | with the scrolls | town goods (Holtburg's Cindrue: Lead, Iron, Copper, Silver; Fort Tethana: Copper and Gold) | M5 spec §5.7 |
| **The buy-trip** | from VI (Pyreal), VII (Platinum) | the pinned world SQL decides (`cache/m6/1/sel-scarabs.tsv`): Zisaha bint Jaidka (2314) at Fort Tethana (rows 70986 and 70987, rate 1.8) is the trip's; Archmage Philomon (5646) at Neydisa and six more town archmages are in the catalog (M6 item 1; session.md section 69). The climber's buy-trip goes by the Town Network (M6 4 t4a): Zisaha's with Lifestone Recall in the book; without it, since M6 item 14 (m6-goldscarab) Archmage Sammam al-Ashiz's at Uziz (a travel place for the town stock's stop, his walk confirmed on the dats), where it was Philomon's at Neydisa, whose walk from the drop finds no path yet (TODO(m6-neydisa-walk)). On the climber's grounds from the Cave to the Barracks the Gold Scarab (V) and the Pyreal Scarab (VI) are town stock, bought at Uziz on the way out, not carried lines. Diamond Scarabs have no shop: the ring is put away at their floor | m5-plan.md §9 `m6-carried-scarab-restock` |
| Level VI (M6 item 1) | 43 (the climber's trip waits for item 4's Town Network hop, D40) | the Grand Master Scriveners: Stonehold, Fort Tethana and four more, at 50,000 pyreals (was Stonehold's four, Town Network, no gate) | the shortlist; session.md section 68 |
| **Level VII** | about 70 | no vendor; tier 4 loot (half the scroll drops) and every scroll drop of tier 5 (the Holding); about 12 Platinum Scarabs an hour at all-VII fighting, under 10% of loot income | ACE `ScrollLevelChance`, `CanReadScroll` |
| **The Atlan wand** | base War 290 (mid-70s) | 16 motes at Crater Caves (4-5 h at den-level XP), the combines, the one Alchemy check (91% with 50 raises and Alchemy Mastery VI; the named grant of D34, Alchemy by play is M7's `m7-atlan-alchemy-by-play`), the glyph, the golem, the Minor Smoldering Stone; +16% at the Holding, +30% at the den | cache/m6/atlan-wand-plan.md |
| **The tusk quests** | each rung; the 20 h is the tusk's pickup's (D57), not a turn-in stamp | the tusk from the dungeon's end to Brighteyes the Tailor (2.2N 95.6E): Female 100,000 XP up to Rampager 8,000,000; each also gives a title and an Aphus Lassel Portal Gem (item 5's turn-ins, D57: nine written in item 5, five unprofiled for item 10; item 4's travel file names it, D50) | corpus NPC emotes |
| The daily round | from 100 | the Eater Jaws, Lost Pet, the Repaired Shadow Stone, the Shield of Yanshi, Faces of the Mukkir and the island's tusks | §5.6 |

### 5.6 The quests (the ladder note §12, 778 pages scanned)

633 solo, 352 plain, 145 kill tasks; the four shapes cover the plain ones. The first tranche: the kill tasks near the
rungs; the Insatiable and Ravenous Eater Jaws and Lost Pet (125, 110, 100; 40 M, 35 M, 30 M); the Repaired Shadow Stone
and the Shield of Yanshi (125, 100; 161 M, 58 M); the Atlan Wand; Faces of the Mukkir (Expert) (100; 99 M). At level 126
a round of the trophy quests, fetches and the Mukkir pays about 420 M in under two hours (the quest pages' figures;
item 9 checks them against the server's quest data).

### 5.7 Not established (item 0's W0 and the gates)

Every slot count, delay and creature stat against this server's `ace_world`; whether the corpus's health is a start
value or the maximum (a Rampager 435 or 590); the Holding's inside and the water run; the mana economy below level 30;
which Platinum Scarab seller this server holds; the tusks' drop rates; every rate at the Holding.

**Answered by item 0's W0 (2026-11-01, read-only SELECTs on this server's `ace_world`; the rows in m6-plan.md §8):**
every spine and alternation count of §5.2 and §5.4 matches (Burrow 42, Lodge 57, Cave 81, Cavern 63, the Temple 20-40
59 Minions and 85 level-30 tuskers, the Abode 121, the Habitat 57, the Quarters 79, the Temple 40+ 144 and its statue,
the Barracks 54, the Holding 184), each behind one linkable generator of 5 minutes (the Lodge's 4); the portal gates
match (the Burrow none, the Lodge 5, the Cave 12, the Cavern 15, the Temple 20-40 20 to 40, the Abode 20, the Habitat 25,
the Quarters 30, the Barracks 40, the Holding 60). Health is the maximum: a Rampager has 435 (§5.3 amended), a Slave
400, a Redeemer 250, a Minion 90 (and 110 stamina; the other Minion has 190 mana). Fort Tethana's Zisaha (2314) sells
Pyreal (base value 1,000) and Platinum Scarabs (10,000), as do 28 other archmages including Neydisa's Philomon (5646);
the only Diamond Scarab seller is Jeeves (6826) in the Advocate Dungeon, which a player cannot enter, so "no shop"
stands. No vendor sells a level VII scroll; the Grand Master Scriveners sell VI (war: 35 scrolls with Flame Bolt VI) at
Stonehold, Fort Tethana and four other towns, and a second set beside the Destroyed Portal to Eastwatch. Brighteyes
(22642) pays exactly the strategy's tusk table and gives a title and an Aphus Lassel Portal Gem (22729) with each; her
Give emotes stamp no quest, so a turn-in timer, if any, is not hers. Each tusk drops always from its dungeon's four or
five named variants (a contained item: the Female tusk from 22589 in the Burrow, the Rampager's from 22599 in the
Holding); the Armored, Plated and Silver tusks' holders stand in the Tusker Armory (0x5948), the Tusker Tunnels (0x5D45)
and the Tusker Pits (0x5A4A), not the Chapel of Mowen, the Plated Rifts or Vengeance Caverns as §5.4 assumed: item 3
and item 5 correct the quest grounds. The tusks' drop rate is 100% from those variants; their spawn share is item 3's.

## 6. Decisions

### 6.1 The owner's questions

Decided before planning (recorded in m6-plan.md §7 as D1-D13): the ladder and the quest tranche in one milestone over
two meter weeks; 80-126 in M6 with a written cut line; VII when the fizzle rule allows; no Melee Defense; Alchemy at
creation; the climber `Winnie`; the fighters in M7; the hero's form deferred; granting for testing continues; the
optimizer seat; the buy-trip; the Nest completion only.

Asked by the planner on 2026-11-01 (at most four, the recommendation first) and answered the same day (D14-D16):

1. **The local model's rule.** The model runs on another computer; recommended was a CLAUDE.md exception for that one
   inference endpoint. **Answered: not yet.** The network rule stands; the owner runs the model's jobs by hand and
   agents verify the outputs.
2. **The standing job.** After gate D, `Winnie` climbs from 1 on the owner's machine for about two weeks with the
   backend up and the display held on, the optimizer reading its log daily; it finishes under M7. **Answered: yes.**
3. **When item 0 starts.** In this meter week's remainder or after the reset. **Answered: now.**

Later the same day (D32): the local model runs only LM2, then LM1; LM4's §9 sweep was done by the planner (m6-plan.md
§9.1) and LM3's long-run measures by a Claude agent with a read-only script (m6-plan.md §8).

### 6.2 Carried rules

M5's §6.2 and §4 hold: check, act, check (M5 D141); a walk has a purpose or holds (M5 D47); looting on in the long
runs and off by default (M5 D155); a carried-only scarab at its floor stops the run until the buy-trip exists (M5
D148); the display held on for every gate; the corpus is read-only and nothing of it is committed (M5 D11).

## 7. Status

Planned 2026-11-01 (m6-plan.md §0). No item has started.
