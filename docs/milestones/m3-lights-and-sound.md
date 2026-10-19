# Milestone 3: fights and sound (file: lights and sound)

**Status: planned (2026-10-15), not started.** The plan of record, with the read map, the seams, the waves, wave 1's
slice tables, the gates, the decisions and the "as run" record, is [m3-plan.md](m3-plan.md). The scope stub of
2026-10-14 (`m3-live-and-fight.md`) is reconciled in §3.3. M2 ([m2-log-in.md](m2-log-in.md)) is complete on master.

## 1. Goal

Basic magical combat in the Black Spawn Den ("BSD"), on the bundled ACE, by a legal level-126 combat mage, driven by
test loops on the Action API rather than by a person playing:

- **All four magic schools cast for real.** Creature, life and item self-buffs as a full buff cycle that re-buffs
  before expiry; war magic (fire) to kill BSD's tuskers. Void magic is out (decision D2).
- **Stats.** Attributes, vitals and skills in the retail panels, and experience spent on them through the Action API.
- **They hit us.** The monsters attack, damage lands on our vitals, the combat messages reach the chat, and death
  follows retail: vitae, the lifestone, portal space back. No looting: a loop spawns fresh gear.
- **Lights and sound.** The game's sound from the settled sources (§5.5), and the lights that make a dungeon fight
  readable, defined from the dats and the decompile (§5.6).

The primary consumer is the three test loops (buff, fight, death-and-re-gear). Every capability lands on the Action
API and a loop first, the retail UI second (§4). This is the first step towards the built-in automation goal
([goals.md](../goals.md) §2).

## 2. Definition of done

Each item names the check that proves it, so the audit table (§7) is mechanical. "Loopback" is a test in
`tests/Lifestone.Loopback.Tests` (live, run by the item's lead); a "leg" is a `--login-check=<leg>` verdict field
(docs/session.md §27); "play-checks" is the offline stand-in step; a "frame" is a review frame at 800x600 (M2 D57).
Every retail behaviour cites the retail function and its 11.6096 address, or the ACE rule and its file. M2's
decisions are cited as "M2 D<n>" (m2-plan.md §10); M3's own are D1-D18 (m3-plan.md §7).

1. **The XP table and stats on the Action API.** XpTable 0x0E000018 decoded (the six lists); `RaiseAttribute`,
   `RaiseVital`, `RaiseSkill` and `TrainSkill` actions (GameActions 0x0045, 0x0044, 0x0046, 0x0047) that complete on
   the server's private update of the stat; the level, the available and total experience and the skill credits in
   the read model. Checks: `XpTableTests`, the action and reducer tests, loopback `StatsLiveTests` on Lifestone
   Throwaway (a raise on BSD Mage outside its build would break D3; the mage's own raises are proven by its build), leg
   field `level=`.
2. **The level-126 combat mage** ("BSD Mage", D3) on the test account, built by an idempotent scenario and its loopback
   test: the creation intent of §5.3, the `xp` verb to level 126, the template's attributes and skills spent, the
   profile's spells learned; a ready mage is only verified (its level and spent stats from the read model, its spells
   from the mod's `spell learn` replies, which say "already known"). Checks: `MageBuildLoopbackTests`, verdict
   `mage=ready level=126 template=ok`.
3. **Spellbook, spell bars and components in the read model** (from the PlayerDescription, which net decodes and the
   reducer dropped, and from GameEvent 0x02C1 when a spell is learned), and the **`Cast` action**: `Cast(spell)` (0x0048) and `Cast(spell, target)` (0x004A) complete
   on the cast's outcome (the enchantment add for a self spell, the projectile's CreateObject for a war spell, a
   fizzle 0x0402 or a refusal 0x0400/0x0401 as a server error). Checks: reducer tests, `CastTrackerTests`, loopback
   `CastLiveTests` (one spell per school), play-checks on the stand-in.
4. **Enchantments live.** GameEvents 0x02C2 (add), 0x02C3 (remove or expiry), 0x02C7 and 0x02C8 (dispel), 0x02C6 and
   0x0312 (purge) decoded and applied to the registry the vitals already read (`PlayerQualities.Enchantments`), with
   each enchantment's start and duration kept; closes `TODO(g4-enchantment-updates)`. Checks: codec and reducer tests,
   loopback (a cast buff appears, an expiry removes it), play-checks.
5. **The buff cycle.** An arena profile (BSD: the bludgeon set of §5.2) cast in all three schools at the highest level
   whose cast chance meets the threshold (D10), expiry tracked, re-buffed before it, all on the Action API, so the
   auto-buff goal reuses it; the headless `--scenario=buff` and the `buff` leg. Checks: `BuffCycleTests` (a fake
   clock), loopback `BuffLoopLiveTests`, leg `buffs=<active>/<wanted> rebuffed=yes min_remaining_s=` (`<active>` reads the lines active at the end).
6. **Gear.** The mod's `item <wcid> [<stack>]` verb (D11); a `GearKit` (the wand, the armour, the components the
   profile's formulas need) spawned by `PlayerTools.SpawnGear`; the inventory read model (the pack, the equipped
   items by location, burden) from the PlayerDescription and the inventory messages; `Wield`, `Unwield` and
   `MoveToPack` actions (0x001A, 0x0019); the wand wielded before any cast. Checks: mod tests, `GearLiveTests`, leg
   `gear=wielded worn=<n>`.
7. **Casts look and sound right.** The caster's wind-up and cast gestures (ACE's UpdateMotion through the motion
   table), the F755 scripts on the caster and the target through `EffectObject` on the avatar, the F750 sounds, the
   projectile in flight with its scripts and its hit. Checks: play-checks (the stand-in plays a cast and the counters
   are asserted), the counters `cast_scripts>=1 cast_sounds>=1` (judged in play-checks from item 2.1; reported by the live `buff` leg, D28), a review frame
   of a cast.
8. **The fight in BSD.** Combat mode Magic (0x0053), a target, Flame Bolt at a tusker until it dies; the spell damage
   lines, the resist lines and the kill notification (0x01AD) in the chat; the kill's experience (0x02CF). Checks:
   loopback `FightLiveTests` (one kill), leg `bsd-fight`: `kills>=1 damage_dealt>0 spells_cast=`. No resist line was
   seen live: the tally counts them as `resists=` (report-only); the blocked picks and wall hits are reported beside
   `misses=`.
9. **They hit us.** The tuskers' hits land on the vitals (0x02E9 in the meters) with the defender notification
   (0x01B2) and the evade (0x01B4) in the chat. Checks: loopback `DeathLiveTests` (DoD 9's evidence since M3 4.2 p5b,
   C9: the unbuffed mage waits among the tuskers and the loop stops 3 s after the first blow, so `hits_taken>=1`,
   `damage_taken>0` and `health_min_pct<100`; `IncomingDamageLiveTests` was dropped there), leg `bsd-death` (D36; the
   buffed `bsd-fight` reports them): `damage_taken>0 hits_taken>=1`.
10. **Death by retail.** The victim notification (0x01AC), the Dead motion, the vitae (0x02C2 Vitae and the vitae
    panel's numbers), the corpse, the respawn at the lifestone by F751 through portal space, the lifestone
    protection, the 75% vitals. Checks: loopback `DeathLiveTests`, leg `bsd-death`: `died=1 vitae=<pct>
    respawn=lifestone`.
11. **The three loops as acceptance tests:** `--scenario=buff|fight|death-and-regear` on the headless host, the legs
    `buff`, `bsd-fight`, `bsd-death` in `--login-check`, and the smoke steps `login-buff` and `login-bsd` under
    `-Backend require`. Checks: the steps' verdict lines, judged.
12. **The retail panels with content**, read-only over the read models with actions through the Action API (D13):
    attributes 0x1000002A, skills 0x1000002B and character information live; beneficial and harmful effects (pages 4
    and 5) and vitae (15); the spellbook 0x1000002E and components 0x1000002F; inventory 0x10000023, backpack
    0x10000022 and paperdoll 0x10000024; the combat panel 0x1000000C, the spellcasting panel 0x10000015 and the
    power bar 0x1000000F. Checks: client tests per panel, play-checks, a frame per panel; everything else answers
    `NotInM3`.
13. **Sound.** The server's F750 and F755 audible on their objects; the avatars' animation sound hooks through the
    sink with the body's sound table (footsteps, casts) and animated scenery's hooks; the pan sign fixed; UI sounds
    (M2 D62); the region's ambient sound outdoors; a check that hears audio. Checks: `--record-audio` judged non-silent
    (`TODO(record-audio-check)` closed); the leg counters per source, each judged `>=1` where its source can occur
    (`sounds_server` in the `buff` leg (D27: ACE sends no F750 at login), `sounds_hooks` in any walk, `sounds_ui` in `--retail-ui-checks`, `ambient`
    by engine-checks' fixed listener (the Holtburg laps report it)), so a recording of the portal sounds alone does not pass the item.
14. **Lights** (§5.6): a dynamic-light list (at most 7, nearest first) merged ahead of the static lights for moving
    objects; the viewer light; the SetLight hook and the projectile's Setup light where the dats give the projectile
    one (item 1.1 s0 pinned it: the extract names 9 projectile Setups, not 10, and all 9 carry one point light,
    Flame Bolt's 0x0200040D included); the dynamic lights' glow on the cell's polygons and
    statics. Checks: `DynamicLightsTests`, a judged frame of a dynamic light (the viewer light, or a Flame Bolt when
    lit) on a BSD wall, `--spell-showcase` light counters.
15. **Tests, gates and frame time.** `dotnet test` green with a non-decreasing count; the loopback suite; the smoke
    steps; frame time judged as §5 states it: Enter to walking is judged per run against 50 ms (the worst of three
    runs; two runs over 50 ms or any run over 75 ms fail the gate; one run over 50 ms is reported with its
    attribution); no frame over 75 ms in a cast, a projectile hit or a death, judged on the worst frame of each window,
    a frame over 50 ms printed as `frame_over_50=yes`; BSD's frame rate is reported as a one-second window. Checks:
    `enter_to_walk_max_ms`, `cast_frame_max_ms`, `fight_frame_max_ms` and `death_frame_max_ms` (judged),
    `bsd_min_fps` (reported).


## 3. Scope

### 3.1 In

The goal's four parts; the Action API additions (raise, train, cast, combat mode, wield, unwield, move to pack,
learn spells and spawn items through the mod); the message set of §5.4; the read models (inventory, enchantments
live, combat state, spellbook); the mod verbs `spell learn`, `item` and the protected name; the BSD arena profile and
the Places row (the Aluvian den, D12); the three loops and their legs; the panels of DoD 12; the sound sources of §5.5;
the lights of §5.6; the M3 hero video with sound (D15) and the audit table.

### 3.2 Not in M3

Named in the UI as `NotInM3` where they would show: melee and missile combat; Void magic (D2); looting corpses, chests,
side packs and other containers; picking up, dropping, splitting and appraising items (D8); vendors and trade;
crafting, tinkering and salvage; fellowships, allegiances, housing and player killing; other arenas; drag and drop in
the panels (D13); window-layout and options saves (`g4-window-layout-save`, `g6-options-save`).

### 3.3 The stub of 2026-10-14 against the goal of 2026-10-15

| # | Stub | Goal | M3 |
|---|---|---|---|
| 1 | war magic only | all four schools cast for real | three enchantment schools as buffs, war to kill; Void out (D2) |
| 2 | inventory: pick up, drop, stacks, splitting, appraise | no looting; a loop spawns gear | the read model, wield, unwield, move to pack; the rest split off (D8) |
| 3 | paperdoll, casters, armour with appearance, burden | unchanged | kept; wield by double click or the Action API (D13) |
| 4 | a gear verb, kits, later a tools button | unchanged | the `item` verb and kits; the button is wave-2 polish |
| 5 | stances, targeting, spellbook and bar, components, projectiles, the combat messages, vitals | plus "they hit us" | kept; the incoming side is DoD 9 |
| 6 | buffs before the fight, highest castable level, re-buff | the full cycle | the same; "highest castable" is by cast chance, since ACE has no level rule (D10) |
| 7 | BSD first; later arenas open | BSD | BSD only; one profile type per arena; the loop keeps to the tuskers' part (D12) |
| 8 | death: vitae, lifestone, the corpse stays | death follows retail with portal space back | the same |
| 9 | XP from kills, levels, spending | stats in the panels, XP spent | the same plus the three stat panels |
| 10 | the template, created through character creation | a legal level-126 mage | the same; the path is §5.3 |
| 11 | F755 and F750 on objects | the game's sound and the lights | sound widened to §5.5; lights are new (§5.6) |
| 12 | open: how much of the loops | test loops drive it | all three loops are acceptance tests |
| 13 | open: M2's wave-3 changes | n/a | the inherited gaps are m3-plan.md §9 |
| 14 | M2 current until its audit | M2 done | this file replaces the stub |

## 4. The automation-first order

Every capability lands in this order, and a wave's gate reads it in this order:

1. the wire (codec, dispatcher, `IAcSession`), cited to ACE's writer and the retail dispatcher;
2. the read model and the event (the reducer; `IGameSessionView`);
3. the action and its tracker (`IActionApi`), with the stand-in's answer;
4. the headless scenario and its loopback test (live, the lead);
5. the leg in `--login-check` with judged verdict fields, and the smoke step;
6. the retail UI, read-only over the read model, acting only through the Action API;
7. the frame at 800x600.

The buff loop, the fight loop and the death-and-re-gear loop are the acceptance tests of the milestone.

## 5. Research (2026-10-15, read-only; ACE paths are under `vendor-eval/ACE/Source/ACE.Server/`)

### 5.1 The Black Spawn Den

Three identical dens, from the world database v0.9.296: the portals 7242 (Aluvian, in landblock 0x1B90) to cell
0x01050375, 7244 (Gharu'ndim, 0x1338) to 0x01040375 and 7246 (Sho, 0x3734) to 0x01030375, all to (100, −210, 0) with
the quaternion (0.707107, 0, 0, −0.707107); MinLevel 20. Each den holds three generators (wcid 3955, "Linkable Monster
Gen (15 min.)", regeneration every 60 s) with 117 linked creatures, 2 doors, 2 chests and 2 exit portals.

| wcid | Name | Count | Level | XpOverride | Setup | Attacks |
|---|---|---|---|---|---|---|
| 1629 | Tusker Guard | 64 | 80 | 30,000 | 0x02000964 | bludgeon (melee only, no spellbook) |
| 1628 | Tusker Slave | 19 | 80 | 30,000 | 0x02000964 | bludgeon (melee only) |
| 1610 | Drudge Ravener | 16 | 80 | 30,000 | 0x020007DD | bludgeon; casts acid, force and blade spells |
| 1609 | Drudge Stalker | 8 | 50 | 10,000 | 0x020007DD | the same |
| 23 | Virindi Servant | 8 | 40 | 7,000 | 0x02000041 | slash; casts fire, frost, lightning, acid, harm, drain |
| 237 | Virindi Master | 2 | 50 | 10,000 | 0x02000041 | the same |

The damage type is the body part's `d_Type` in `weenie_properties_body_part` (4 bludgeon, 1 slash). ResistFire (float
67) is 1.0 on all; ArmorModVsFire (17) is 0.7 on tuskers, 0.9 on drudges, 1.0 on virindi: "weak to fire" is the armour
modifier. The drudges and virindi occupy one small, easily avoided portion of the den (owner decision D12,
2026-10-15), which the fight loop never enters; it targets tuskers only. The wave-3 lead maps the den's cells by
generator before slicing (a read-only SELECT of the generators' link children). The arena is the Aluvian den, a
curated Places row (`InteriorLocations`, cell 0x01050375).

### 5.2 The buff list and the level rule

The spells are plain SpellTable 0x0E00000E entries (none is in a spell set: the 139 sets are equipment-set bonuses).
Ids are levels I-VI, then VII, VIII, from ACE's `SpellId` enum; wave 1 pins them by name with a dat test.

| Line | School | Ids |
|---|---|---|
| Strength / Endurance / Coordination / Quickness / Focus / Willpower Self | creature | 2, 1328-1332, 2087, 4325 / 1349-1354, 2061, 4299 / 1373-1378, 2059, 4297 / 1397-1402, 2081, 4319 / 1421-1426, 2067, 4305 / 1445-1450, 2091, 4329 |
| War Magic / Creature Enchantment / Item Enchantment / Life Magic Mastery Self | creature | 629-634, 2323, 4638 / 557-562, 2215, 4530 / 581-586, 2249, 4564 / 605-610, 2267, 4582 |
| Mana Mastery / Arcane Enlightenment / Magic Resistance / Invulnerability / Impregnability Self | creature | 653-658, 2287, 4602 / 678-683, 2195, 4510 / 274-279, 2281, 4596 / 18, 245-249, 2245, 4560 / 256-261, 2243, 4558 |
| Armor Self / Bludgeon Protection Self / Regeneration / Rejuvenation / Mana Renewal Self | life | 24, 1308-1312, 2053, 4291 / 1018-1023, 2153, 4464 / 165-170, 2185, 4496 / 54, 189-193, 2187, 4498 / 212-217, 2183, 4494 |
| Impenetrability, Bludgeon Bane (on the armour); Hermetic Link, Defender (on the wand) | item | 51, 1482-1486, 2108, 4407 / 1511-1516, 2098, 4397 / 1475-1480, 2117, 4418 / 1599, 1601-1605, 2101, 4400 |
| Flame Bolt (war, fire); Flame Streak, Volley, Blast, Arc as options | war | 27, 81-85, 2128, 4439 / 1796-1801, 2129, 4440 / 3663, 3664, 143-146, 2130, 4441 / 3662, 115-118, 2127, 4438 / 2739-2745, 4423 |

On this server the wand's item spells (Hermetic Link, Defender) are Auras that target the caster and land as the player's enchantment (ACE's spell 1479 is "Aura of Hermetic Link Self V"), so they are self casts (the cast form follows the SpellTable's target type, `GearCasts.FormOf`); the armour's (Impenetrability, Bludgeon Bane) stay item spells.

**ACE has no per-skill maximum spell level.** The difficulty is each spell's own `Power` from the SpellTable
(`WorldObjects/Player_Magic.cs:528-540`: `GetCastingPreCheckStatus`): a cast fails outright when the skill is below
the power minus 50, and otherwise succeeds with the chance `1 − 1 / (1 + e^(0.07 (skill − power)))`
(`WorldObjects/SkillCheck.cs:7-22`, `GetMagicSkillChance`). The levels' minimum powers in `Entity/SpellFormula.cs:
92-104` (I 1, II 50, III 100, IV 150, V 200, VI 250, VII 300, VIII 400) are a different thing (scroll readability) and
the buffs' real powers are pinned per id by a wave-1 dat test together with their `Duration`. At skill 350 against a
power of 400 the chance is about 3%; at 390 about 33%; at 433 about 90%. Lifestone's cycle therefore picks, per line,
the highest level whose chance from the client's own skill value is at least `--buff-cast-chance` (default 0.9) and
retries a fizzle once one level down (D10); the durations decide the re-buff schedule, and an expiry is proved live
with the shortest-duration buff the pins give (or a dispel). **A spell must be in the spellbook** (`Player_Magic.cs:339-344`); scrolls
teach only with the skill trained and high enough (`Player_Skills.cs:458-471`), and the granting commands are
Developer-level, so the mod's `spell learn` verb over `LearnSpellWithNetworking` (`Player_Spells.cs:48`) is the
path (D11). **Components are required** (`require_spell_comps` true, `Managers/PropertyManager.cs:588`; the burn is
`Player_Magic.cs:1183-1215`); a kit carries what the profile's formulas need (`SpellFormulaRules`), D9.

The first casts (1.4 s2) use the rule at chance 0.999 from the read model's current skill (`GearCasts.Pick`); the buff cycle of 2.1 uses `--buff-cast-chance` (0.9) and the retry one level down.

**The 2.1 cycle (as built).** The mana lines (D24): Stamina to Mana Self (1676-1681, VII Meditative Trance 2345, VIII 4981) and Revitalize Self (1177-1182, VII Robustification 2083, VIII 4321) join the profile (31 lines); the cycle casts them when the next cast needs more mana than the character has, and the build scenario learns them (D3). The armour lines are cast at the caster (D23): ACE redirects each to every worn clothing piece and shield, one "You cast <spell> on <item>" chat line per piece, and the cycle proves them by those lines and UseDone 0. The predicted chain (`BuffChainDatTests`): Focus and Willpower at VI, Creature Enchantment Mastery at VII (91% from 333, +40), every other line at VII (99%); the skills end at 373 (Creature, Item, Life) and 402 (War), and VIII stays below 0.9 (13%; War 53%); an upgrade pass then casts Focus and Willpower again at VII (skills 376 and 405). The cycle's mana at the full price is 1,680 (24 casts of 70; 1,848 with ACE's armour surcharge for six worn items), the planning worst case (about 1,325 s of session time, 48 mana casts); as run it is far cheaper: every profile buff is FastCast and ACE's Mana Conversion lowers each cast's cost (`Creature_Magic.cs` `GetManaCost` 58-100), so the first live cycle cast 26 lines in 56 s of session time with 3 transfers and 1 Revitalize (mana_min 63). Docs: `docs/session.md` section 29 (the cycle and its economy) and section 30 (the leg).

### 5.3 The mage's path

- **Creation** through the Action API's `CreateCharacter` with the intent `HeadlessCharacter.Intent` builds
  (Aluvian, docs/session.md §8), amended: Focus, Self and Coordination 100, Strength, Endurance and Quickness 10
  (330 credits, CharGen 0x0E000002, `CharGenData.cs:178`); 55 skill classes with War Magic (34) and Melee Defense (6)
  specialised and Arcane Lore (14) free. Of the 52 skill credits, War Magic specialised (28) plus
  Melee Defense specialised (20) spend 48 (item 1.1 s0 pinned the costs over `CharGenData.GetSkillTrainedCost`; 4 spare), so Creature Enchantment (31), Item Enchantment (32), Life
  Magic (33) and Mana Conversion (16) are trained later with the credits levels give, by `TrainSkill` (D14).
- **Experience** by the mod's `xp` verb (`PlayerTools.AddXp`, up to the level cap, `Player_Xp.cs:189-192`); the XP for
  level 126 and the credits by then come from XpTable 0x0E000018 (`CharacterLevelXPList` u64,
  `CharacterLevelSkillCreditList` u32; layout in `ACE.DatLoader/FileTypes/XpTable.cs:25-56`), decoded in wave 1.
- **Spending** by `RaiseAttribute`, `RaiseVital`, `RaiseSkill` and `TrainSkill` (ACE's `@raise` and `@trainskill` are
  empty, so the wire actions are the only way), confirmed by the private updates 0x02E3, 0x02E7, 0x02DD and the
  "Your base X is now N!" line (`Player_Attributes.cs:13`, `Player_Vitals.cs:19-62`, `Player_Skills.cs:21, 114-127`).
- **Spells** learned by `spell learn` (0x02C1 and "You learn the X spell.").
- **The character** is "BSD Mage" (D3), protected in `CharPurgeRules.Graduated` and the stand-in, never reset; 4 of
  the account's 11 slots used at most (`PropertyManager.cs:626`).

### 5.4 The message set

Decoded today: GM F74C, F745, F7DB, F747, F748, F74B, F74E, F751, F749, F74A, F625, F750, F755, F7E0, 02BB, 02BC, 01E0,
01E2, 02CD, 02CF, 02DD, 02E3, 02E7, 02E9; GE 0013, 01C0, 01C7, 028A, 028B, 02EB, 0004, 02BD (docs/protocol.md §5.1).
M3 adds:

| Need | Message (direction S→C unless said) | ACE | Lifestone today |
|---|---|---|---|
| cast gestures | F74C UpdateMotion per gesture, stance Magic 0x80000049, speed 2.0; the gestures are the components' Gesture fields; Ready after a 1 s recoil | `WorldObject_Networking.cs:1078`, `Player_Magic.cs:601, 977-990`, `Entity/SpellFormula.cs:245-287` | decoded, played by the motion table |
| spell words | 02BB HearSpeech | `Player_Magic.cs:598` | decoded |
| combat mode | F74C + 02CD CombatMode (int 40: 1 NonCombat, 2 Melee, 4 Missile, 8 Magic) | `Creature_Combat.cs` | decoded; the property not read |
| launch | F745 of the projectile, F755 Launch; caster and target F755 | `WorldObject_Magic.cs:1823, 1833, 359, 365` | decoded and played (`TODO(g5-w10-scripts)` closed in 1.3 s0) |
| hit | F74B, F755 Explode, F74E, F747 after 5 s | `SpellProjectile.cs:209-248` | decoded |
| spell damage and resist text | F7E0 type Magic; resist: chat + F750 ResistSpell | `SpellProjectile.cs:824, 840`; `WorldObject_Magic.cs:188-201` | decoded |
| fizzle and refusals | F755 Fizzle + GE 028A 0x0402; UseDone 01C7 0x0400 (components), 0x0401 (mana) | `Player_Magic.cs:917, 406, 583` | decoded; `Cast` resolves on the 028A and keeps its lane until UseDone (the 0x0402 UseDone from `FailCast` and the two refusals are UseDone codes; M3 1.2 s4b) |
| component burn | GM 0024 InventoryRemove, 0197 stack size | `Player_Magic.cs:1221` | decoded, applied (M3 1.2 s2) |
| damage to the player | 02E9 | `Player_Vitals.cs:130-139` | decoded |
| melee on the player | GE 01B2 DefenderNotification + F750 + F755 Splatter; evade 01B4; own 01B1, 01B3 | `Player_Combat.cs:542-546, 346, 151, 163` | decoded, applied (M3 1.2 s3) |
| kill | GE 01AD KillerNotification; XP by 02CF (no text) | `Creature_Death.cs:76, 189-195`; `Player_Xp.cs:106` | decoded, applied (M3 1.2 s3) |
| level up | 02CD Level and credits, F755 LevelUp, "You are now level N!" | `Player_Xp.cs:314-345` | decoded, unread |
| enchantments | GE 02C2 add, 02C3 remove or expiry (+ F750 SpellExpire), 02C7/02C8 dispel, 02C6 purge, 0312 purge bad; 02C4/02C5 never sent | `WorldObject_Magic.cs:405-452`, `Managers/EnchantmentManager.cs:315-331, 481-509`, `Player_Death.cs:213-221` | decoded, applied (M3 1.2 s1; `TODO(g4-enchantment-updates)` closed) |
| inventory | 02DA Container + GE 0022 (put); 0024 + F74A (pickup), 0197 (stacks); 02DA + 019A (drop) | `Player_Inventory.cs:1359, 124, 404, 138, 1441` | decoded, applied (M3 1.2 s2) |
| wield | 02DA Wielder + 02CE CurrentWieldedLocation + GE 0023 + F750; broadcast F749 + F625; failure GE 00A0 + 02EB | `Player_Inventory.cs:297-306, 1807`; `Creature_Equipment.cs:354-368` | decoded, applied (M3 1.2 s2), F749/F625 as before; ACE sends 02DA Wielder 0 and 02CE 0 before 0023 names the location, so membership comes from 0023 and 0022 only (the plan's "a wield's 02CE and 0023 agree" does not hold on this server) |
| raises and training | 02E3, 02E7, 02DD + "Your base X is now N!" + F750 RaiseTrait; train 02DD + 02CD credits + "X trained." | §5.3 | decoded; the texts unread |
| learn | GE 02C1 + "You learn the X spell." | `Player_Spells.cs:48-80` | decoded, applied (M3 1.2 s1) |
| death | GE 01AC VictimNotification; GM 019E PlayerKilled; F74C Dead; 02E9 0; 02CD NumDeaths, DeathLevel, VitaeCpPool; 02C2 Vitae; 02C6; the corpse F745 + the drop list; F751 + F748 to the Sanctuary; 60 s protection (028A 0x0502 + F755 ShieldUpBlue on a hit); 02E9 ×3 at 75% | `Player_Death.cs:31-277`, `Player_Location.cs:654-707`, `Entity/Strings.cs:511` | **01AC, 019E decoded, applied (M3 1.2 s3)**; the delay is the Dead animation's length + 1.0 s (the motion table gives the length) |

**Combat lines (M3 1.2 s3).** The lines are `ClientCombatSystem`'s (11.6096 0x0056C160 attacker, 0x0056D670 defender, 0x0056D4F0 and 0x0056D370 evasion, 0x0056D160 victim, 0x0056D070 player death). Chat types: 0x16 for the player's own hit and own evaded attack, 0x15 for a hit on the player and an evaded attack, 0 for the death notices (01AD's chat type is assumed 0: `TODO(retail-handler-01AD)`); 019E shows a line only when the player is neither victim nor killer. Fidelity (presentation, not a glitch fix): a `ChatLine` has no trailing newline, but retail appends one to every combat and death line because its scroll is a text buffer.

Actions to send (F7B1; handlers in `Network/GameAction/Actions/`): CastTargetedSpell 0x004A (target, spell),
CastUntargetedSpell 0x0048, ChangeCombatMode 0x0053, RaiseVital 0x0044, RaiseAttribute 0x0045, RaiseSkill 0x0046,
TrainSkill 0x0047 (skill, credits), GetAndWieldItem 0x001A (guid, EquipMask), PutItemInContainer 0x0019 (item,
container, placement), DropItem 0x001B and IdentifyObject 0x00C8 (both out, D8), AddSpellFavorite 0x01E3 and
RemoveSpellFavorite 0x01E4 (the bar), TeleToLifestone 0x0063. RaiseVital, RaiseAttribute, RaiseSkill and TrainSkill are sent since item 1.1 (s1a, s1b). Since M3 1.2 s4b, Cast (0x0048 {spell}; 0x004A {target, spell}, the target written first), SetCombatMode (0x0053 {mode}), Wield (0x001A {item, mask}) and Unwield and MoveToPack (0x0019 {item, container, placement}) are sent too. The wire layouts are ACE v1.78.4816's: `GameActionMagicCastUnTargetedSpell` (u32 spell), `GameActionMagicCastTargetedSpell` (u32 target, u32 spell), `GameActionChangeCombatMode` (u32 mode), `GameActionGetAndWieldItem` (u32 item, i32 mask), `GameActionPutItemInContainer` (u32 item, u32 container, i32 placement). The others are not sent yet.

A Cast completes by its spell's kind. A Use and a Cast are never out together: ACE's UseDone 0x01C7 carries no request id, so a Use waits while a cast is in flight or its lane is held, and a Cast waits while a Use is in flight (several Uses may be out together; ACE answers them in order). Chosen over "the first sent takes it" because a cast's own UseDone comes seconds after its effect, and a Use sent in that window could not be told from the cast's end. A war spell is confirmed by the first Missile-state object created while the cast is in flight that started within 5 m of the player (3D, from the CreateObject position), has a velocity that points away from the player (horizontal dot product not negative) and within 60 degrees of the aim (the target's direction when the target's position is known, else the player's heading), and wears the spell's projectile Setup where the host has it (the ACE world extract, 13 war spells; no entry: not checked). An unknown player position, an object with no position or a zero velocity is refused. A self spell is never confirmed by a projectile, even without a spell table (an untargeted or self-targeted cast of unknown kind). A cast that ends without a projectile names, in the `ServerError` detail, each Missile object it saw and why it was not taken. A cast or Use that ended without its UseDone (a timeout in flight, or a hold let go at 10 s from the send) owes one: a UseDone that arrives within `ActionApi.OwedUseDoneWindowSeconds` (5 s, session clock) of that moment is dropped (logged at Info), not given to the next action. A UseDone after the window belongs to the action in flight.

### 5.5 Sound (settled before planning; sized here)

The engine, retail's sound rules and the UI-sound wiring exist (`src/Lifestone.Data/Audio/`, `src/Lifestone.Client/
Audio/`, `scripts/Audio/AudioService.cs`; docs/portal-space.md §7, §11.3). What was missing at the start of 1.3, each a slice of wave 1's
item 1.3 (all built; the statements below describe the starting state): the server's Sound F750 and PlayScript F755 are decoded and published but nothing subscribes
(`SessionAvatarAdapter.cs:63-65`); the avatars' animation sound hooks are dropped because `AvatarSet.EffectEvents` is
never assigned (`AvatarSet.cs:284, 802-805`) and `IEffectEventSink.OnEffectEvent` has no sound-table parameter; the
pan is reversed (`AvatarSet.cs:829`); animated scenery drops its hooks on purpose (`LivingStatics`); the region's
ambient sound (RegionDesc 0x13000000 `SoundInfo`: `AmbientSTBDesc` with `STBId` and `AmbientSoundDesc` of type,
volume, base chance, min and max rate, as DatReaderWriter names them) had no decoder (about 50 lines) and no player (now decoded, `RegionSoundData`, and played, `AmbientSoundPlayer`);
`AdminEnvirons` 0xEA60 was not decoded (now applied, the sounds played, with the fog a named TODO, `TODO(m3-environs-fog)`); UI sounds waited on `TODO(ui-sound-sink)` (M2 D62; closed by 1.3 s2a); no check heard audio
(`TODO(record-audio-check)`, closed by 1.3 s3). M2 D8 stays: automated runs are silent unless `--volume` is given, and the recording
captures the full mix anyway.

### 5.6 Lights

**Retail's model** (11.6096 addresses; 11.4186 in `cache/research/symbol_transplant_11.6096.tsv`). One light type,
`LIGHTINFO`: type, offset frame, colour, intensity, falloff, cone angle. Every lit Setup (608 of them) carries exactly
one, a point light (type 0); `ModelAssembly` already decodes it as `ModelLight`. `CPartArray::InitLights` (0x00519730)
gives an object a `LIGHTLIST` that follows its frame (`LIGHTLIST::set_frame` 0x00518790) and is added to its cell
(`CPartArray::AddLightsToCell` 0x005189D0, `CObjCell::add_light` 0x0052BDE0). A light is **static** when its owner has
the static physics-state bit (a cell's torches: these are what M2 calls the cell's lights, M2 D46), **dynamic** otherwise
(a projectile or a creature carrying a Setup light). The `SetLight` hook (`SetLightHook::Execute` 0x00527BD0) only
switches an object's lights on or off (`CPhysicsObj::set_lights` 0x005107C0, state bit 0x800); colour, intensity and
falloff never change. Dynamic lights live in `Render::world_lights`, at most 7 (`Render::add_dynamic_light` 0x0054E030,
`insert_light` 0x0054DDC0, `remove_object_light` 0x0054CDC0; `CObjCell::add_dynamic_to_global_lights` 0x0052BFA0).
**Cells:** the static lights are burned per vertex once (`D3DPolyRender::SetStaticLightingVertexColors` 0x0059E0F0),
and every dynamic light is a hardware light on the cell's polygons (`Render::minimize_envcell_lighting`
0x0054CD80). **Moving objects:** per drawn part, `Render::minimize_object_lighting` (0x0054E090) fills up to 8
hardware point lights, dynamic first, then static, keeping those that reach the part's sphere; a hardware light is
colour times intensity, range falloff times 1.5, attenuation 1/d (`PrimD3DRender::config_hardware_light`
0x0059BD40). **The viewer light:** every frame `SmartBox::UseTime` (0x004554B0) adds a dynamic point light 2 m above the
player with falloff 10 (`s_fViewerLightFalloff`; the intensity `s_fViewerLightIntensity` and the colour are to be read
from the decompile, acclient.c near line 145535): this is what makes a dungeon readable round the player. Particle
emitters carry no light.

**What M2 draws and what M3 adds**, with a size per piece (small: under 60 calls; medium: one slice; large: two
slices). The pieces add up to item 4.1's $60: about four slices plus the review.

| Piece | Retail | M2 | M3 | Size |
|---|---|---|---|---|
| static cell lights on cells | burned per vertex | done (`CellStaticLighting`, docs/interiors.md §4.3) | nothing | none |
| static lights on moving objects | up to 8 per part with a reach test | done, one set per object (`ObjectLightSets.Select`, `SurfaceLightingCode.ObjectLightSum`) | `TODO(interiors-light-reach)` (falloff or falloff times 1.5: one byte read, docs/research/retail-addresses-m2.md §3.1); per-part selection stays a documented deviation | small |
| dynamic lights on moving objects | a projectile's or creature's Setup light, dynamic first | nothing (`TODO(g5-dynamic-lights)`) | a per-frame dynamic list (position, colour, intensity, falloff, on), nearest first, at most 7, merged ahead of the static lights in the row writers (`PlayerView.cs:358`, `ServerObjectsLayer.cs:462`; no shader change); follows a projectile across cells; dropped on SetLight off or destruction | medium |
| dynamic lights on cell geometry and statics | every dynamic light as a hardware light on the cell's polygons | nothing | a global dynamic-light table (RGBA32F, up to 8 lights, rewritten per frame) read by an additive per-vertex term in the EnvCell and CellObject shader variants: the glow on the walls, as coarse as retail's per-vertex lighting (a cell mesh is 5-13 vertices per room; D16: no `OmniLight3D`) | medium to large |
| the viewer light | a dynamic light 2 m above the player, falloff 10 | not drawn | one more entry of the list; intensity and colour from the decompile | small |
| SetLight and projectile lights | the hook toggles; the Setup light on the projectile | `SetLightHook` and `EffectVisualState.LightsOn` tracked (`EffectObject.cs:484`), nothing drawn | `LightsOn` linked to the list; the projectile's `ModelLight` added when it is created (1 of 12,268 script hooks is a SetLight) | small to medium |

Item 1.1 s0 pinned the projectile lights (`ModelAssemblyTests`): the extract names 9 projectile Setups (not 10) and all 9
carry one point light. Unknowns: the wave-4 lead resolves first (each
under 10 calls) which script holds the SetLight, the viewer light's intensity and colour, and whether particles get
the hardware lights (`TODO(objects)` in `ParticleShaders`).

### 5.7 The loops and the checks

- **Shape.** A loop is an `IScenario` (like `LoginScenario`) run by the headless host (`--scenario=`) and as a leg of
  `LoginCheckScenario`'s `playing` stage; legs are `RoutePlay`s today, so wave 1 adds a small leg interface for
  non-movement legs. Scenarios act through `IActionApi` and `PlayerTools` only; the screens are touched only through a
  screens seam (`IThrowawayScreens`, docs/session.md §28).
- **Steps.** `login-buff` and `login-bsd` are backend steps in `tools/smoke.ps1`, run only under `-Backend require`
  (they change the character's state); `slice-check.ps1` never runs them (`-Backend auto` only).
- **The stand-in** answers only the tool verbs, Use, chat and the login messages today (`StandInWorld.cs`); it answers a cast (an enchantment add, a placeholder projectile, a fizzle), a combat-mode change, a wield, an unwield and a move to the pack since item 1.2 s4c; a tusker with health and a damage reply, a death and a lifestone teleport remain to come,
  the `item` and `spell learn` replies, so `--play-checks` exercises every loop offline.
- **The live-shaped rehearsal** (m3-plan.md §5): the stand-in already builds 138 synthetic layered enchantments
  (`StandInVisit.cs:160-175`) and holds Enter until 0.1 s after character management shows; wave 1 records one headless
  `login` of the graduated `Lifestone Smoke` (whose registry really holds 138 enchantments; the character is only
  logged in and out) and loads that PlayerDescription frame (gitignored, M2 D14), and holds Enter 0.2 s.
- **The gear kit** needs the wcids of a wand, an armour set and the component weenies (unknown until the wave-1 lead
  reads them from the world database, read-only, before item 1.4 is sliced), and the components of a formula differ
  per account (ACE's `GetPlayerFormula` randomises the tapers by account name, `Entity/SpellFormula.cs:224`;
  Lifestone's `SpellFormulaRules.CustomizeForAccount`): the kit is built from the account-customised formula and
  carries every taper colour, so a wrong customisation fails a test rather than a live cast.
- **Verdict fields** (additive; report-only in the item that adds them): `level mage gear cast buffs rebuffed
  min_remaining_s kills spells_cast damage_dealt damage_taken hits_taken died vitae respawn gear_restored returned
  cast_scripts cast_sounds sounds_played sound_tables cast_frame_max_ms fight_frame_max_ms bsd_min_fps`.

## 6. Decisions

The decisions table is [m3-plan.md](m3-plan.md) §7 (D1-D18); the M2 decisions it relies on are m2-plan.md §10
(M2 D8, D14, D16, D46, D49, D51, D57, D58, D62).

## 7. Status

All items are merged: 1.1 (2026-10-15), 1.2, 1.3, 1.4, 2.1, 2.2 and 2.3 (2026-10-16), 3.1 and 3.2 (2026-10-17),
4.1 (2026-10-19) and 4.2 (2026-10-19: the end, with the polish pass, D39-D43). M3 is complete. The audit table has one
row per DoD item, with the verdict (met, or met with named gaps) and the evidence named in §2; every gap is a named
TODO placed in [m3-plan.md](m3-plan.md) §9. The live numbers are those of the 4.2 live gate and its full smoke (§8).

| DoD | Verdict | Evidence |
|---|---|---|
| 1 | met | `XpTableTests` (the six lists; level 126 = 4,286,609,098; 37 credits); `StatActionCodecTests`, `WorldSendTests`, `StatTrackerTests` (each action completes on the stat's private update; a train on the skill and the credits updates; ACE's told refusals as ServerError, its silent ones as a 5 s timeout), `ReducerStatsTests`; loopback `StatsLiveTests` 1/1 on Lifestone Throwaway (each cost equals the XpTable's or SkillTable's price on the live server; the specialised and vital prices are proved by DoD 2's build); leg field `level=` (report-only). Not asserted live: the advancement chat lines and the raise sound (`m3-advancement-chat-check`) |
| 2 | met | `MageBuildScenarioTests` (offline: created and built from nothing in plan order; a ready or past-126 mage is only verified; a short one gets only its missing steps; refusals, a full account, a stale mod and a level/experience disagreement fail loudly); `MageTemplateDatTests`; loopback `MageBuildLoopbackTests`: `mage=built level=126 template=ok spells=249/249 built_steps=1`, then `mage=ready built_steps=0` (249 is the profile's count after 4.2 p7 added Fire Vulnerability Other VII and Heal Self VII). `template=ok` means every target's ranks are reached, the six attribute starts and skill classes match, and the level is at least 126; the template is the owner's D19 split (m3-plan §7) |
| 3 | met with named gaps | the read model: `Spellbook` from the description and 0x02C1 (`ReducerSpellbookTests`); `Cast(spell)` 0x0048 and `Cast(spell, target)` 0x004A (`MagicActionCodecTests`, `CastTrackerTests`, `CastAttributionTests`: the enchantment add, the projectile, the fizzle 0x0402, UseDone 0x0400/0x0401 as ServerError). Live: `CastLiveTests` (Creature, Life and Item confirmed with the SpellTable's duration; mana falls); War Magic live in `FightLiveTests` (3.1); the certain fizzle was proved live in 1.4 and reads `n/a:buffed` on the buffed mage. The failure chat lines now show their text (`WeenieErrorText`, 4.2 x1). Gaps: `m3-cast-projectile-attribution`, `m3-cast-other-target-proof`, `m3-spell-removal` (a guard), `m3-cast-components` and `m3-cast-compat` (the client does not refuse before sending: ACE answers) |
| 4 | met with named gaps | 0x02C2-0x02C8 and 0x0312 decoded and applied to `PlayerQualities.Enchantments` by retail's registry rules (`UpdateEnchantment` 0x00594CE0, `RemoveEnchantments` 0x005959B0), each entry's start and duration kept and its expiry counted on the session clock (`MagicCodecTests`, `ReducerEnchantmentTests`, a 138-entry registry); `TODO(g4-enchantment-updates)` closed. Live: `CastLiveTests` (each cast buff appears with its SpellTable duration); `EnchantmentExpiryLiveTests` (Soak: a buff leaves the registry with its 0x02C3 and the SpellExpire F750). The "has expired." line is printed for a 0x02C3 or 0x02C5 (4.2 x1; ACE sends no chat for a player's own expiry); `m3-enchant-expiry-name` names the no-table case; an empty purge publishes its empty `EnchantmentChanged(Purged)` (`m3-purge-empty`, closed in 4.2 x1) |
| 5 | met with named gaps | `BuffCycle` (the owner's order; the D10 level per cast from the buffed skill; a fizzle one level down; the armour lines at the caster (D23); expiry from the registry and per armour piece; the re-buff margin; the D24 mana loop), `BuffCycleTests` on a fake clock, `BuffScenarioTests`, `BuffChainDatTests`; loopback `BuffLoopLiveTests`: `buffs=24/24`, every line VII, `rebuffed=yes forced=yes`, `min_remaining_s` asserted above 0 (a recorded run read 3547 s, 26 casts in 56 s), a second run casting no self line; the `buff` leg judged `ok=true buffs=24/24 rebuffed=yes`; `--scenario=buff`. Gaps: nothing reaches the timed re-buff pass outside the tests (`TODO(m3-buff-rebuff-driver)`), the armour lines' expiry lives in the cycle object (`TODO(m3-buff-armour-memory)`), the leg ignores the `--buff-*` options (`TODO(m3-buff-leg-options)`) |
| 6 | met with named gaps | the read-model part: `PlayerInventory` (pack, side packs, equipped by location, burden from `EncumbranceVal`) from the description and 0x0022, 0x0023, 0x019A, 0x0024, 0x0197, 0x02CE, 0x02DA, 0x00A0 (`ReducerInventoryTests`); `Wield`, `Unwield`, `MoveToPack` 0x001A / 0x0019 (`InventoryTrackerTests`); loopback `InventoryLiveTests`; `GearKit` (the account-customised and foci components, every taper; `GearKitTests`), `SpawnGear` (top-up and StackableMerge 0x0054, the slot guard; play-checks `kit=58/58 kit_topup=0`); loopback `GearLiveTests` (`gear=wielded worn=5`, a second run `gear_steps=0`). The kit is D43's classic armour and The Healer's Heart (`worn=5` live). The `buff` leg carries `gear=wielded worn=5`. The wand is wielded by the scenarios' order, not by a client or ACE rule. Gaps: side-pack contents and instance properties are not read, and the foci containers are not read (`m3-inventory-sidepack-contents`, `m3-inventory-instance-properties`, `m3-inventory-foci`); a wield sends the item's whole valid mask (`m3-wield-slot-choice`, closed in 4.2 x4: the second side is tried, `TODO(m3-wield-second-choice)` to confirm live); the burden capacity is derived from Strength in the character panel (`m3-burden-capacity`, closed in 4.2 x1) |
| 7 | met with named gaps | the stand-in plays ACE's cast (words, gestures by `CastGesturePlanner`, the effect, F755, Ready, UseDone: `StandInCastSequenceTests`); the player's body plays the server's gestures in the Magic stance with the held item (`PlayerCastMotionTests`); F755 on caster and target and F750 through `EffectObject` (`ServerEffectsTests`, `CastWindowTests`, `BuffCastScriptsTests`); the projectile flies on its own velocity, stills on the hit's vector change, leaves on F747, and plays Launch and Explode from its own table (`ProjectileFlightTests`, `StandInFightTests`; play-check `a-stand-in-flame-bolt-is-shown-and-flies`). Judged offline: `cast_scripts>=1 cast_sounds>=1` (D28). Report-only live: the `buff` leg's `cast_scripts=` counts the scripts played on the player in a cast window (self casts; an armour cast's effect lands on a piece with no script table, counted as `ServerScriptsMissing`), `cast_sounds=`, `cast_frame_max_ms=`. The review frames of the bolt in flight and its hit are judged by eye. Gaps: Flame Bolt's one GfxObj draws nothing, so the bolt on screen is its particles; a bolt keeps its creation cell in flight, closed in 4.2 x2 (`ProjectileCellFollower`; the remaining collision is `TODO(physics)`); the sphereless-missile glitch fix with `--retail-sphereless-missile` |
| 8 | met with named gaps | the read-model part: `PlayerQualities.CombatMode` and `CombatModeChanged`, `SetCombatMode` 0x0053 (`CombatModeTrackerTests`), the kill notification 0x01AD in the chat (`ReducerCombatTests`). The fight (3.1, hardened in 4.2 p4): `FightScenario` (Magic mode; the nearest tusker with a clear line, `IFightWalk.HasClearLine`, else the nearest; a wall hit is a bolt that stops short of its target, counted as `wall_hits=`); `FightTally` (the spell-damage lines, resists included, 0x01AD, the 0x02CF delta; `FightTallyTests`); `FightScenarioTests`; `FightLineOfSightTests`. Loopback `FightLiveTests` (kills, damage, a confirmed projectile, the level). Leg `bsd-fight` in the 4.2 full smoke: `kills=1 damage_dealt=490 spells_cast=4 misses=0 blocked_picks=3 wall_hits=0`; the standalone leg `misses=0 blocked_picks=2 wall_hits=0`, the first bolt hitting the tusker face-on ("You burn Tusker Slave for 163 points with Ilservian's Flame."); review frames `-first`, `-bolt`, `-hit`, `-kill`, `-viewer` (2026-10-19). No resist line was seen live (counted as `resists=`); the kill's experience is asserted offline only (`FightTallyTests`) and reported live (`xp_gained=`) |
| 9 | met | the read-model part: 0x01B2 and 0x01B4 decoded, `CombatEvent` and retail's chat lines (`ReducerCombatTests`); `FightTally` and `DeathTally` count `damage_taken`, `hits_taken` and `evades`. Loopback `DeathLiveTests` (DoD 9's evidence since M3 4.2 p5b, C9: `hits_taken>=1`, `damage_taken>0`, `health_min_pct<100`; `IncomingDamageLiveTests` was dropped there). The loop stops 3 s after the first blow, as the code does (`StopAfterHitsSeconds`), not after a minute: the unbuffed mage dies in 10 to 40 s otherwise. The leg `bsd-death` judges `damage_taken>0 hits_taken>=1` (D36; live `login-death` `hits_taken=8 damage_taken=105`); the review frame `-hit` (the vitals down, the blow in the chat, the splatter) |
| 10 | met with named gaps | 0x01AC and 0x019E applied (`ReducerCombatTests`); `DeathTally` over ACE's order (`Player_Death.cs` 34-292): the Dead motion, the vitae (0x02C2, the vitae page opening as retail's `gmVitaeUI::RecvNotice_VitaeChanged` does), 0x02C6, the death's own corpse, the F751 through portal space to the lifestone, the vitals share (live 74.9 %; the test asserts 60 to 90 %); `DeathScenario` binds BSD Mage at Holtburg's lifestone (D34) and unbuffs by a suicide (0x0279, D33); loopback `DeathLiveTests`; leg `bsd-death` judged `died=1 respawn=lifestone gear_restored=yes` (live `login-death`: `died=1 respawn=lifestone gear_restored=yes worn=5 hits_taken=8 damage_taken=105 health_min_pct=0.0 death_frame_max_ms=15.9 protection=dispelled vitae_after=0`); `vitae=` is printed, not judged (D35). The Dead motion, the vitae panel's numbers and the corpse's pose are proved offline and by the review frames `-dead` and `-lifestone`, not by a live assertion. Gaps: the lifestone protection's blocked blow is proved offline only (ACE dispels it on every teleport, so live it reads `protection=dispelled`); no corpse frame (portal space follows the corpse by 0.3 s); the vitae is paid back by the `xp` verb after each death run (D37) |
| 11 | met | the buff loop (2.1: `--scenario=buff`, the `buff` leg, `login-buff`), the fight loop (3.1, hardened in 4.2: `--scenario=fight`, the `bsd-fight` leg, `login-bsd`, judged `kills>=1 damage_dealt>0` and the fight frames by §5) and the death-and-regear loop (3.2: `--scenario=death-and-regear`, the `bsd-death` leg, `login-death`, judged `died=1 respawn=lifestone gear_restored=yes damage_taken>0 hits_taken>=1`); the three steps pass under `-Backend require` in the 4.2 full smoke (19.2 min). The headless `--scenario=` options are proved by option-parse tests and by the loopback tests that build the same scenario classes. Offline, play-checks run the whole `FightScenario` and `DeathScenario` against the stand-in |
| 12 | met with named gaps | the panels (2.2, 2.3 and 3.2; docs/retail-ui.md §23): attributes 0x1000002A and skills 0x1000002B with base and buffed values and the raise and train buttons (D29; `GameAttributesTabTests`, `GameSkillsTabTests`, `SkillRulesTests`; the augmentation bonuses are closed in 4.2 x4 by `SkillRules.AugBonusBase/AugBonusCurrent`); character information as retail's text report (`GameShellG6Tests`), without the augmentation lines (`TODO(g6-character-info-augmentations)`); the effects pages 4 and 5 with live countdowns and vitae 15, and the effects, vitae and burden indicators (`GameEffectsPageTests`); the spellbook 0x1000002E and components 0x1000002F (`GameSpellPageTests`; the first open paints at once after 4.2 x4, the live check pending); inventory 0x10000023, backpack 0x10000022 and paperdoll 0x10000024 with its figure (`GameInventoryPageTests`, `GamePaperDollTests`); the combat panel 0x1000000C and spellcasting panel 0x10000015 (`GameCombatPanelTests`, `GameSpellcastingPanelTests`; the toolbar's combat buttons change the mode through the Action API); the power bar 0x1000000F as a window only (nothing begins it: `TODO(m3-melee-attack)`; its frame is a placeholder at 0.6). Every other page answers NotInM3 (`GamePanelsTests`, `NotInM3Tests`; the play-check). Play-checks 196/0 solo after 4.2 fix B. The buttons are proved against the stand-in's Action API; the actions themselves live by `StatsLiveTests`, `InventoryLiveTests`, `CastLiveTests`. Open: the placeholders named in §23 (the titles tab, the PK line, the heritage line, part lighting), no use or drag from the pack (`m3-use-from-pack`), the combat option boxes ghosted (`m3-combat-options`) |
| 13 | met with named gaps | F750 and F755 on the avatars (`ServerEffectsTests`, `StandInServerEffectsTests`; play-checks `server_scripts=1 server_sounds=1`); the hook sink with each object's own sound table and retail's pan (`AvatarSoundOriginTests`, `SoundHookRouterTests`, `SoundTweakedHookDecodeTests`); animated scenery's hooks (`LivingStaticsTests`); UI sounds (`RetailUiSoundStatsTests`; the retail-ui checks count a click on the character-management screen); ambient outdoors and EA60 (`RegionSoundDataTests`, `AmbientSoundPlayerTests`, `AmbientScanCadenceTests`, `ReducerAdminEnvironsTests`). Judged: smoke `record-audio` (server>=1 and hooks>=1 in its sources, non-silent; `TODO(record-audio-check)` closed; the mix clips at 0.0 dBFS, RMS -20.3 dBFS, placed in §9), `sounds_hooks>=1` over the walking legs, `sounds_server>=1` in the `buff` leg (D27), `sounds_ui` in the retail-ui checks, `ambient_draws>=1` in engine-checks on a fixed listener. Report-only: the login-check `sounds_server=` of the Academy arrival and `ambient=` of the Holtburg laps (live: hooks 256-260, ambient 82-87). Gaps: the player's footsteps are silent on these dats: a read-only SELECT confirms the player's SoundTable is 0x20000001 (the human table), which has no Footstep1/2 variant, so `TODO(footstep-sound-table)` is confirmed live; the listener is the player's camera in play mode (`avatars-listener-camera`, closed in 4.2 x2); the ambient has no slider of its own (`ambient-slider`) and fog is not drawn (`m3-environs-fog`) |
| 14 | met with named gaps | the dynamic-light list (at most 7, nearest first; `Render::add_dynamic_light` 0x0054E030) merged ahead of the static lights for moving objects, 8 at most (`DynamicLightsTests`, `ObjectLightRowTests`); the viewer light (white, 2.25, falloff 10, 2 m above the player, every drawn frame; `--viewer-light`; never pushed out of the seven, a named deviation); a projectile's Setup light while LightingOn and the SetLight hook (`ModelAssemblyTests`, `SetLightScriptPinTests`: script 0x33000109); the glow on the cell's polygons per vertex and on particles in cells (shown by the frames only; no test asserts the shader term); the first-person fade (`FirstPersonFadeTests`). Live `bsd-fight`: `bolt_light=yes dyn_lights_max=5 viewer_light=on` (judged from 4.2, as §8 records); the judged frames `-bolt` (the den's wall and floor lit orange by Flame Bolt VII, 2026-10-19) and `-viewer` by eye. `bolt_light` and `viewer_light` read the dynamic-light list, not pixels. The showcase's `lit_projectiles=` counts projectiles, not lights (`m3-showcase-lights`). Gaps: `interiors-light-reach` (the byte read needs the owner), no dynamic lights outdoors (`m3-dynamic-lights-outdoors`), particles get no static cell sum (`particle-cell-static-lights`); the bolt's cell follows the server (`m3-bolt-cell-tracking`, closed in 4.2 x2) |
| 15 | met with named gaps | the default tests green with a non-decreasing count (6,311 at M3's start, 7,883 at 4.1, 7,965 at 4.2, plus 76 Exhaustive tests; recorded per gate, with no mechanical floor); the loopback suite (31 tests: 30 passed, 1 skipped, 20.7 min) and every smoke step pass at the 4.2 gate, the BSD steps under `-Backend require`. Frame time, judged as §5 states it: Enter to walking against 50 ms per run, the worst of three (the 4.2 live runs: 37.0 / 29.8 / 36.3 ms; the teleport 20.8 / 34.0 / 15.9; login 31.6 / 30.9 / 36.5); `cast_frame_max_ms`, `fight_frame_max_ms` and `death_frame_max_ms` judged on the worst frame of each window, failing over 75 ms and printing `frame_over_50=yes` over 50 ms (the 4.2 live `login-bsd` frames 6.3 ms; `login-death` 15.9 ms); `bsd_min_fps` reported (233.9 at 4.1). Gaps: the buff leg's cast frame is report-only (`m3-buff-cast-frame-judge`, M4); the offline checks judge no cast, hit or death frame; smoke does not fail on `frame_over_50=yes`; the non-decreasing test count is a record, not a check |
