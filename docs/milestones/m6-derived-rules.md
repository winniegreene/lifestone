# M6 14: the per-character constants and the rules that replace them

The owner, 2026-11-13: a ground profile holds facts only (layout, creatures and their world stats, entry, exit, routes, home,
vendors). Every number about how a character fights or what she carries is computed at run time from her live state and the
creatures' facts. This is the inventory of what still sits in the profiles and the ladder, who it was sized for, and what it
should come from. Already derived (the pattern): the mana policy (`VitalsPolicy`, the climber's pull lines live through
`FightScenario.PolicyPullRule`), the live bolt cap, `ClimberHealthRule`, the caster score, the pack rules.

## Inventory

| Constant | Where | Sized for | Should come from |
| --- | --- | --- | --- |
| `fight.retreat_health_percent` | Burrow 40, Cave 40, Abode 45, Habitat 50, Temple 20-40 40, Lodge 0, den and Holding 0 | the climber at the rung's first level (Abode: projected health 136 at 40, the Cave's live hits of 2026-11-11); the den's 0 is BSD Mage at 80 (D91) | her live maximum health, the hardest hitter's DVal, DVar and Strength, the place's pack, her armour and protection factor: `FightLines.RetreatPercent` (built) |
| `fight.pull_mana_percent` | outskirts 20, Burrow 60, Lodge 50, Cave 80, Temple 85, Abode and Habitat 100 | one pull's mana over the climber's pool at one level (Abode: 316 of 323 at 35-37) | `VitalsPolicy.Lines` on her live pool, bolt and Mana Conversion (live for the climber already) |
| `pull_health 70`, `until 90/100`, `full 100`, `pull_rest_potions 1`, `potion mana 75`, `potion stamina 15`, `posture lie`, outskirts' `stamina 30` and `until 80` | every fight block | the owner's rules for the climber (M6 7 AN, M6 8), copied per ground | one general constant each (not per ground); the policy's lines override the numbers live |
| `fight.pull_aggro_metres` | seven grounds | none: the creatures' VisualAwarenessRange | stays: a fact of the place |
| `fight.ring_min_targets 4` | den, Holding | BSD Mage at 80 (M5 F22) | the Ring's mana against the bolt's at her live Mana Conversion |
| stock keep / trip / stop (Prismatic Taper 200 to 1,000; scarabs; Mana Potion 5 to 100; Potion of Healing 30) | every profile | the rung's modelled kills an hour x the move-on ceiling's casts a kill, worst-case burn, for ten minutes (climber); the den's for BSD Mage's 3-hour read | the measured burn an hour (the logistics counters over time) x the hours to the next trip (the buff bot's line length) x a margin; stop = one cast's need |
| account-formula lines 2/2/2 | climber grounds | a backup she does not burn | general: one line |
| `town_stock` keeps (tinctures 12/6, 30/10; Gold and Pyreal Scarab 100/40) | Cave, Abode, Habitat, Burrow, Lodge, Quarters, Barracks, Cavern | the Cave's 2026-11-11 drink and cast rates | the measured burn as above |
| `components` per-level rows | every profile | the spell levels the plan named for one projected skill | the formulas of the spells she will cast (`GearKit.FociFormulaCounts` over the live cap's bolt and her book) |
| `spell_plan` rows | every profile | `SpellPlan.ProjectedWarBase` per level | the live bolt cap (already governs the level up job's fight) |
| `supplies.burden_trip_percent` 150 / 90 | every profile | the climber's Strength 75 / BSD Mage's capacity | her live capacity and the trip's load |
| `purse.coin` (30,000 to 73,000) | every profile | one restock from nothing at the keeps above | the derived keeps x the vendors' prices |
| `loot` floors (min_payout 4,000, value a burden 60, max burden 150) | den, Holding | BSD Mage at 80 (D95, D96) | her capacity and the trip cadence |
| `rung_gates` lines (Armor V and Protection V for the Cave, the Abode and the Quarters; VII, the buff bot's, for the Holding: D135) | ladder | the Crimsonback model (AX) at level 22 to 39; the study's safe levels under the bot (D135) | creature damage against what she holds: the gate shut while `FightLines` puts her retreat line over the drain line |
| ladder `vitals` (`drain_under 50`, `mana_loop_under 45`, `stamina_keep 80`, the drain gives) | ladder | the climber's measured fights | general rules (one each), kept |
| ladder rung `health.pack 1` past the Cave | ladder | no survey (TODO(m6-pack-past-the-cave)) | the place's spawn survey: a fact |

## Done in this slice

- The Holding's plates as facts (nine avoid boxes, sourced), the walk's goal and in-room detour round them, and the Quarters'
  layout (no plate, no jump, every slot reached): `HoldingPlatesTests`.
- `FightLines`: the retreat line from her health, her damage factor, the hitter and the pack (`FightLinesTests`).

## Done in M6 14 holding safety

- **The derived line is wired** (`LiveFightLine`, closes the wiring half of `TODO(m6-derived-fight-wiring)`): the autopilot's
  retreat line (`RetreatLineNow`), its health floor (`HealthFloorNow`: the upkeep and the startup audit) and the travel
  legs' retreat (`TravelLegSafety.RetreatLine`) read `FightLines.Derive` on her live state: her maximum health; her
  armour (the worn pieces' appraisals, Armor Self's body armour from her registry, the Impenetrability and bludgeon bane
  the session's armour ledger holds on every worn piece); her live resistance (`FightLines.ResistanceMod`: the
  protections times any vulnerability, so a landed Tusker's Gift raises the line while it lasts, closes
  `TODO(m6-holding-gift-live)`); and the fight's measured damage a second and biggest hit (`RecentHits`), the larger of
  the model and the measure counting. The place's facts are its targets' `d_val`, `d_var` (each row's `attack_source`)
  and Strength (`HuntingGround.HardestHitter`), the pack the ladder's survey names (1 past the Cave,
  `TODO(m6-pack-past-the-cave)`). Where the facts are given the derived line wins and a profile's
  `fight.retreat_health_percent` is logged as ignored; where they are not (every profile but the Holding today) the
  callers' own line stands as before, named.
- **The Holding** (`HoldingKitTests`): no hand-sized fight block (the den's `retreat_health_percent 0` and
  `ring_min_targets 4`, BSD Mage's at 80, are gone; the block holds the owner's general pull rules and the Rampagers'
  awareness range, the Barracks' shape); its three Rampager rows carry `d_val 160`, `d_var 0.75` (the pinned world SQL's body parts);
  its supplies, town stock (the Abode's four and the Platinum Scarab of VII at Uziz), components (the foci and account
  formulas of VI and VII; VII's account formula read from the dats) and vendors (no Scrivener, no learn line) are the
  Abode's general rule; its plan rows are the projection (VI 65-123, VII 124-126), the live cap governing the ladder's
  fight (closes `TODO(m6-holding-spell-vii-crossing)` and `TODO(m6-holding-supplies)`; the burns stay the ladder's model,
  `TODO(m6-derived-supplies)`).
- **Still hand-sized for one character** (left in place, each overridden at run time once its targets carry their
  attack's facts): the `fight.retreat_health_percent` of the Burrow 40, the Lodge 0, the Cave 40, the Temple 20-40 40, the
  Abode 45 and the Habitat 50 (the climber at a rung's first level), the den's 0 (BSD Mage at 80, D91) on both dens; the
  `ring_min_targets 4` of the dens (BSD Mage); the pull and rest fields of every fight block (the owner's rules, copied per
  ground); the Holding's `loot` floors (BSD Mage at 80, D95, D96).

## Done in M6 14 retreat attackers

Live 2026-11-14 10:59:18 UTC (`cache/m6/14/live/holdtrial1`): on the Holding's island approach two Infected Assailers
(acid) killed the climber, level 62 and 197 health, while the run leg's line was the destination's Rampager in bludgeon
(10%) and the vitals guard's heals had spent the mana under Lifestone Recall's 50.

- **The line from the attackers present** (`LiveFightLine.ReadAttackers`, `FightLines.DeriveAttackers`): each creature
  attacking her or in reach, by its weenie class across every profile's targets and approaches (`CreatureFacts`), its own
  damage type, her armour against that type (`ArmorReport.Read` with a damage type and that type's bane; closes
  `TODO(m6-fight-lines-damage-types)`) and her live resistance to it. The travel legs read it in place of the
  destination's line; a derived fight takes the larger of the place's and theirs (a place with no facts keeps its own,
  the den's D91 0 among them).
- **Unknown attackers**: the general fallback, 50% (`FightLines.GeneralFallbackPercent`, the autopilot's old default), named.
- **The measured floor**: the hits this fight or fight-back has seen over the seconds she needs to leave plus the largest
  (`FightLines.MeasuredPercent`); the travel legs' fight-backs feed it (`AttackWatch.Measured`).
- **The retreat's own price**: a fight-back keeps Lifestone Recall's cost plus a heal's (`ManaWayOut`, the fight's way-out
  rule): it leaves at that line with the recall paid (`mana-line`), refuses a bolt into it, and the vitals guard refuses a
  heal that would take the mana under the recall's cost while it stands (`JourneyFacts.FightBackReserve`).
- **Approach facts**: the Holding's `approach` (eleven landblocks, eleven hostiles with level, health, damage types,
  `d_val`, `d_var`, Strength and camp size from the pinned world SQL) and the Barracks' three hostiles' facts.
- **The approach check** (`ApproachCheck`): at the way out and the trial switch, a ground whose worst camp would take 95%
  or more of her full bar while she recalls is held by name (`approach-held:<rung>`, a trial refused `approach: ...`) and
  she hunts her own ground; the corpse trip leaves a corpse in such an approach (`trip_abort=approach`), naming the rot
  (`CorpseApproachRule`: max(3600 s, level x 300 s), ACE Corpse.cs:110 @95ef426d).
- Still a model: `FightLines.SwingSeconds` (2.75 s, `TODO(f11-swing-seconds)`); the Infected Assailers swung about every
  1.5 s live, which the measured floor covers in a fight and the approach check does not.

## Done in M6 14 approach cost

The owner, 2026-11-14, watching the climber cross the Tusker Island to the Holding: she was fighting things with high
magic resistance, so she should recall and go back to the previous ground. Live (`cache/m6/14/live/holdtrial2`, 12:54 to
13:03 UTC): level 62, War Magic 352, Flame Bolt VI; four fight-backs took 3, 6, 20 and 28 casts for 1, 2, 4 and 3 kills (a
Reaper alone 21 bolts, 11 resisted), and the mana line recalled her at 111 of 387, 590 m short of the portal.

- **Facts**: each approach hostile carries its Magic Defense, its seven resistances (floats 64 to 70), its awareness range
  (float 31), what one roll of the approach's encounter generators stands up of it (`per_roll`) and its kill's xp (int
  146); the approach its encounter rows and the creatures a roll stands up in all (`encounters`, `per_roll`, with
  `cost_source`). Each rung's targets carry their xp. All from the pinned world SQL v0.9.296, read offline.
- **The crossing's cost before she leaves** (`ApproachCost.Judge`, rule 1): the creatures she meets (the generators'
  density over the approach times the run's metres times twice each kind's awareness, times its `per_roll`; the unlisted
  creatures at the listed kinds' mean), each kind's kill by the fight's own model (`VitalsPolicy.BoltsToKill`) over its
  health, her bolt's mean landed damage on it (`WarSpellPicker.MeanHit`: her live War Magic, its resistance to the rung's
  element, the held caster's damage as the caster pick scores it) and the chance the bolt lands (the cast and its Magic
  Defense against her War Magic); the mana at the bolt's expected cost after her Mana Conversion; she starts full (the
  top-up before the hostile drop) and regenerates. Held by name (`approach-costly:<rung>`) when it would bring her mana to
  the way out's reserve before the portal (`mana`) or cost more seconds than the rung's own rate allows in a cycle
  (`time`: `cycle x (1 - previous rung's xp/h / the rung's) + the crossing's own xp / the rung's rate`). Read at the
  ladder's way out, the trial switch and the corpse trip (`ApproachCrossing.Judge`), with what would change it (her War
  Magic with the bolt the cap rule and her pack give, a held caster's damage, a shorter run).
- **During the crossing** (`ApproachCost.Measured`, rule 2, check, act, check on a rung change's run out): after the fight's
  seed kills (3), the run's measured kills a metre and net mana a kill project the rest of the run; when that ends under the
  reserve she recalls (`why=approach-abandoned`) before the mana line, and the crossing is held for the rest of the run
  (`JourneyFacts.AbandonApproach`; the next way out holds `approach-abandoned:<rung>` and she hunts the previous rung).
- For her at 62 (War Magic 352, Mana Conversion 254, 387 mana, regenerating about 0.6 a second, Flame Bolt VI because her
  pack holds no Platinum Scarab for Ilservian's Flame): 28.9 creatures, 227 casts, 1,554 mana, 1,131 s over the 1,507 m run;
  -488 at the portal against the reserve 120: held. No War Magic up to 752 passes with Flame Bolt VI; a held fire caster of
  x2.9 would; a run of 459 m or less would.
- Still a model: the heals are not in the prior (the measured rule counts them); the run is the route's straight line, not
  the walk the planner makes round the creatures.

## Next (TODO(m6-derived-fight-wiring), TODO(m6-derived-supplies))

1. Read her damage factor live (worn pieces, Armor Self, the bludgeon resistance product including a Gift) and wire
   `FightLines` into `AutopilotScenario.RetreatLineFor`, `HealthFloorFor` and `TravelLegSafety.RetreatLine`; add the pack
   as a fact to each fight block; refuse `retreat_health_percent` and the owner-rule fields in the loader.
2. Supplies: a burn-per-hour measure in `LogisticsCounters`, the keep and trip rule, the loader refusing the counts, and
   the 38 test files that read catalog stock numbers.
3. Components and gates from the spells she will cast and the derived retreat line.

## The Tusker Barracks (M6 14 guard task)

- Facts only, checked on the dats and the pinned world SQL (`BarracksLayoutTests`): no pit, no jump and no unreached half (every
  storey cell but the exit portal's and every slot joins the arrival on foot); the ten "Magic traps" are unlinked Switches that
  fire on nothing on ACE @95ef426d, and their spell row 1877 would take every line, the buff bot's level VII included, so the
  six avoid boxes stay (1 m round each trap: its 0.104 m radius, her body, a margin) and no leg of the tour comes near one.
- No hand-sized fight block: its `fight` holds only the owner's general pull rules and the Guards' awareness range 25 m (a
  fact); the retreat line is the autopilot's general floor until `TODO(m6-derived-fight-wiring)`.
- Its vendors, plan rows, components, supplies and town stock are the general default, the Abode's lines verbatim (the rung
  she climbs from while the Tusker Guard Kill Task sends her there), not sized for the ground: `TODO(m6-derived-supplies)`.

## The caster's points (M6 14 caster spend)

The owner's ruling, 2026-11-14: she should definitely use the fire wand she has. The climber at 62 held a Fire Sceptre that wants
base War Magic 290 with 287, and her spend gave War Magic its 13.4% share, so the three points were hours away.

- **The gain** (`SelfEquipJudge.ShortCasters`): every held caster short of a skill-base wield requirement (ACE's RawSkill) is
  scored as if wielded, her skills with the points added, by the caster score of a usable one (its element and damage bonus on
  the plan's element, its Mana Conversion, its defences), against her held caster. The pick's line names each one's gain.
- **The spend** (`CasterSpend`, `XpSpendRule`'s `casterFacts`, the level-up spend's and the trip's): when that gain beats the
  swap margin and the points can be bought, rank by rank, from this allocation (less what the floors take), her next
  level-up's or the one after, the best of them by its gain a million experience has its ranks pushed first in the magic skills'
  share: after the floors, before every share, the caps standing and the rest of the allocation spent as before. Priced in
  experience and in hours at her measured rate (the run's experience over its seconds), beside the hours at the skill's own
  running share. Out of reach: logged with the numbers, nothing changes.
- **The swap**: the pick wields it at the next home visit, in peace mode. A wield that does not land (the server's answer, the
  caster in the hands) is `caster-wield-failed`, and the caster is held back for the run (`CasterHeldBack`): the pick, the rearm
  and the spend pass it over by name.
- On the retail XpTable her three points are ranks 167 to 170, 5,222,530 experience: about 1.2 hours of her play at 4.5 M an
  hour first, against about 8.7 at War Magic's share.

## The Tusker Armory and a held rung's alternate (M6 14 armory)

The owner, 2026-11-14: try the Tusker Armory as the climber's next ground (`cache/m6/next-ground/survey.md` sections 3.1
and 4).

- **The ground** (`profiles/tusker-armory.json`, landblock 0x5948): facts only, drafted by GroundDraft and checked on the dats
  and the pinned world SQL (`ArmoryLayoutTests`, `ArmoryKitTests`): 67 Armored Tuskers (level 100, 435 health, Magic Defense
  275, 80,000 experience, melee bludgeon d_val 90), five storeys with no pit or jump, all 67 slots reached; a body runs all 53
  legs of the tour. Its two "Magic traps" are the Barracks' unlinked Switches, boxed as walls the walk never crosses. No
  hand-sized fight block; its home, shops, supplies, components, plan and town stock are the Holding's (the same rung).
- **The route**: the Facility Hub's Barracks drop, then a 345 m run to the entry (travel-routes.json, `IslandRunDatTests`
  `hub-barracks>armory`). The approach records the corridor's five island hostiles; at her level-62 state the crossing is
  survivable and paid (6.4 creatures, 221 mana, 275 at the portal against the reserve 120).
- **The held rung's alternate** (`RungOwner`): at the way out, a rung of her band held by its approach takes its first
  alternate whose own approach is recorded, judged open on her live state, and whose portal and route admit her
  (`TakeAlternate`, `AlternateRefusal`), on the rung's band, gate, bolt plan and margin; an alternate with no approach is never
  taken so (unknown never sends her), and a rung taken ahead of its band falls to the previous rung as before. A check that is
  not a way out keeps the session's answer (`approach-session:<rung>`: the alternate, else the previous rung's ground), so a
  held rung no longer stops each cycle's check to move there and back; a way out that judges a held key open releases it
  (`ApproachHolds.Release`). The holding rung's alternates are the Armory (first), the Temple, the den and the Quarters; only
  the Armory records its approach.
