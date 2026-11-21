# The fight simulator

`tools\fight-sim.ps1` runs ACE's own combat code offline and prints what the server would do. It exists so that
nobody guesses a game number: whether a spell lands, what a hit is worth, how long a kill takes, what a creature does
to the character, where the experience is. The rule that sends agents to it is in `CLAUDE.md`, "Numbers come from the
corpus".

## What it is

- **The server's code, not a model of it.** The tool loads the pinned ACE build (`server\runtime\ace` of the main
  checkout, ACE 95ef426d) and builds real server objects in memory:
  - a creature from its world-database weenie, as a generator builds it (`WorldObjectFactory.CreateNewWorldObject`);
  - the character from her saved shard rows, with the constructor a login uses: her skills, enchantments, armour and
    caster are what the server last saved (it saves a player every 300 s and at logout).
- **Each number is a call into ACE:**
  - a spell's strike: `SpellProjectile.CalculateDamage` (the resist roll, the critical roll, the skill bonus, the
    wand's modifiers, the target's resistance), then the ratings step of `SpellProjectile.DamageTarget`;
  - a physical attack: `DamageEvent.CalculateDamage` (the evade roll, the body part, the armour on it, the
    resistances);
  - a monster's choice of attack: `GetNextAttackType`, `GetCombatManeuver`;
  - timing: the dats' animation lengths through ACE's `MotionTable`;
  - spawns: the world database's placements, the generators rolled by ACE's own `SelectAProfile`.
- **Where it re-expresses a sequence** (the order of a monster's attacks, the casts of a kill), the code cites the
  ACE file it follows.
- **What it never does:** write to a database, open a socket, enter an object into a world, or save anything. It
  writes only under `cache\fight-sim\`. It runs beside the live server, and any agent may run it from any worktree.
- **It also writes the client's creature tables again** from the world database: the `bestiary` command, below.

## Commands

```powershell
pwsh -File tools\fight-sim.ps1 her                        # her sheet as last saved
pwsh -File tools\fight-sim.ps1 creature "Shadow Wraith"   # a creature as the server builds it
pwsh -File tools\fight-sim.ps1 cast "Flame Bolt VI"       # one cast, timed piece by piece
pwsh -File tools\fight-sim.ps1 matchup 22593              # her against one creature
pwsh -File tools\fight-sim.ps1 matchup 22593 --explain    # with ACE's working for its attacks
pwsh -File tools\fight-sim.ps1 matchup 1628 --wield "Minor Smoldering Atlan Wand"   # as if she held that caster of her packs
pwsh -File tools\fight-sim.ps1 matchup 11 --at-once 9     # the fight with her heals for nine on her too
pwsh -File tools\fight-sim.ps1 matchup --pack "11x3,22510x2,236x1"   # a mixed pack on her at once, with her heals
pwsh -File tools\fight-sim.ps1 matchup 11 --trace         # every cast and vital of the first fight with her heals
pwsh -File tools\fight-sim.ps1 rank --min-level 60        # every placed creature and ground, for her
pwsh -File tools\fight-sim.ps1 bestiary --check           # the tracked creature tables, written again and compared
pwsh -File tools\fight-sim.ps1 help                       # every option
```

- **`her`** answers "what does she have": base and buffed skills, vitals, every enchantment on her and its time left,
  what she wields, the attack spells in her book. Read it before stating anything about her.
- **`creature`** answers "what is it": health, stamina and mana, what a heartbeat gives its health and how far apart
  its heartbeats are, its Run skill, its scale, how near it sees a player
  and hears its kind call, its tolerance, its defences, the damage it takes by type, its attacks, its spell book and
  how often it casts. A name that fits several weenies is refused with the list; pick one by its id.
- **`cast`** answers "how long does a cast take and can she cast it": the windup, the cast gesture and the return to
  ready, and her chance the cast does not fizzle.
- **`matchup`** answers "can she fight it":
  - her spells on it: the chance each lands, the hit sizes, casts, seconds and mana a kill;
  - its attacks on her: each attack's rate, land chance and hit size, sampled over separate fights that each start
    with the creature fresh (a caster spends its mana in its opening);
  - the race: her health lost a kill, and her chance of dying with one to six on her at once if she never heals;
  - with her heals: the same one to six on her (and `--at-once N`) with every cast of hers chosen by the client's own
    rules and played through ACE: her chance of dying, of running out of mana with creatures alive, her lowest health,
    the heals she casts, the mana she spends and the seconds to clear (below, "With her heals");
  - the experience a kill pays and the ceiling an hour of nothing but casting would reach;
  - her recovery: what each of her drains, heals and stamina spells gives a cast (ACE's own cast on the real objects),
    her own regeneration, and the stamina-to-mana dance at its best threshold;
  - all in: the rate with the health lost and the mana spent paid back between pulls, for one to six at once; then
    the same with the fight played with her heals ("ALL IN WITH HER HEALS"), whose rate is the one to read: a pull
    earns only while no more than one fight in ten ends with her dead or out of mana.
- **`matchup --pack "11x3,22510x2,236x1"`** answers "what does this mix do to her": the creatures named (a weenie class
  id or an exact name, then `x` and how many) on her at once, each with her bolt on it and its damage a second, the
  kill order, and the fight with her heals beside the same fight with bolts only and with the vitals policy's fills
  left out ("no fills": not a client rule, a measure of what the fills add or cost).
- **`--trace`** (on `matchup`) prints every cast, its reason, its outcome and her bars of the first fight with her
  heals of each pack: the way to check a verdict by eye.
- **`matchup --with "Frost Bolt VI;Force Bolt VI"`** answers "what would a scroll buy": the named spells are tried
  as if she knew them and marked in the table.
  - The same option gives the fight with her heals the life spells she lacks (on `matchup`, `matchup --pack` and
    `rank`): a Stamina to Mana Self, Drain Health Other, Heal Self or Drain Stamina Other level named there joins the
    levels her rules may choose, cast through ACE with her own skill as a learned one, and is marked `*` in her spell
    line. Use it to stand a saved character in for a climber's kit (for example
    `matchup --pack "11x3,22510x2,236x1" --her Winnie --with "Stamina to Mana Self II"`): her vitals and skills stay
    the saved character's unless `--bars` sets the vitals.
- **`--bars "health=84,stamina=154,mana=161"`** (on `her`, `cast`, `matchup` and `rank`) answers "what if her bars
  were a climber's": each named maximum is set by raising or lowering her vital ranks in memory until ACE's own
  maximum reaches it, and the bar is filled, so every cast, drain and heartbeat reads it through ACE. The line printed
  first says what changed. Her attributes and skills stay the saved character's. Take the figures from a live log
  (the `[fight] vitals policy` lines give `health a/b stamina c/d mana e/f`), and name the log in the note.
- **`--wield "<caster>"`** (on `her`, `cast`, `matchup` and `rank`) answers "what if she held that caster": the caster
  of her packs named (by its name, or its object id as `0x...` when two share a name) goes into her hands in place of
  the one she holds, in memory only, by ACE's own calls: the unwield takes the old caster's spells off her, the wield
  casts the new one's when it has mana and she meets its activation requirement. Its wield requirement is printed and
  not checked. Run the command with and without it and compare.
  - `her` lists what ACE reads of the caster in her hands: its own elemental bonus, Mana Conversion bonus and melee
    defence; a quest weapon's resistance cleaving (an Atlan weapon strikes as if its target had a vulnerability of
    1 + its ResistanceModifier to its element); whether she has mastered it (the x1.05 on every spell, which a
    weapon that "seems tough to master" does not give); and the three modifiers with her auras in, as a fight reads
    them.
  - First use (2026-11-18, level 82, a Tusker Slave): her fire line's level VII hits for a mean of 169.2 holding the
    Piercing Sceptre and 381.5 holding the Minor Smoldering Atlan Wand; a kill takes 2.93 casts and 9.9 s against
    1.58 casts and 5.3 s.
- **`rank`** answers "where is the experience": every creature the world database places, fought once each, by
  experience for her casting time, and every landblock by what clearing it over and over would pay.
  - **How many come at once** is computed, not assumed. The server's rule (Monster_Awareness.cs `AlertFriendly`): a
    creature that wakes alerts every sleeping creature of its own kind standing within that creature's hearing range.
    `rank` applies it to the placements' own coordinates, and takes a generator's camp as one pack. For the Tusker
    Quarters it gives 5.4 at once; the live log of 2026-11-15 shows five on her.
  - **Her chance of dying** is given against one, and against that pack, from full health with no healing.
  - **With her heals** (`dies pack healed`, `oom pack`): the same pack with the fight played by her own rules, 300
    fights (`--healed-fights`) at each of the two whole sizes around the census's mean pack, taken in proportion, at
    most 12 at once and not capped at six. The fights bolt with the client's war pick (below), screened from her
    spells as the race's two are. They cost little: `rank --her Winnie --min-level 5 --max-level 40` took 96.6 s before
    them and 102.6 s with them (2026-11-19, the same machine beside the live run).
  - **The way in** is the portal the world database places that leads to the landblock, with its level limits.
  - The last two tables list the grounds she may enter now whose packs do not kill her: with no healing (no creature
    that makes up a twentieth of the ground killed her in more than 1 of 200 fights against its own pack), and with
    her heals (no such creature ended more than 1 fight in 100 against its own pack with her dead or out of mana).
  - Every existing column keeps its place; the healed ones come last on each row and in each `.tsv`
    (`dies_pack_healed`, `out_of_mana_pack`; the grounds' `.tsv` adds `worst_not_won_healed`).
  - The full tables go to `cache\fight-sim\rank-<name>-<levels>.tsv` (every creature class) and `.grounds.tsv` (every
    landblock).

## With her heals

The race above asks "how hard does it hit against how fast she kills it, if she never heals". The fights with her heals
ask "what happens when she fights it as she does": one to N creatures on her at once, every cast of hers chosen by the
client's own rules, every cast played through ACE on the real objects. Outcomes per fight: won, died, or **out of mana**
(creatures alive and no bolt or press her mana lets her try: the moment she must flee or die, counted apart, never a
win), or stalled (900 s with neither side won: Lifestone's own bound).

**Who decides (one engine: the client's rules, called, not copied).** The tool references `Lifestone.Client` and
`tools/fight-sim/InFightRules.cs` calls its rules in the fight's own order (`FightScenario.Choose`):

| Step | The client's rule called | Source in the client | What the fight gives it |
|---|---|---|---|
| The heal line | `FightEmergency.Wanted` and its hysteresis | `Session/Vitals/FightEmergency.cs`; the lines are `FightOptions`' defaults: floor 60%, safe 85%, margin 10% (D91) | her health; the incoming rate of `RecentHits` (the fight's `Picker.Rates.Hits`), fed every hit |
| The heal | `HealPick.ChooseNow` over Heal Self and each Drain Health Other level | `FightScenario.Emergency.cs` `EmergencyStepOwn`, `FightHealPick` | the drain at the fullest creature a drain takes from (`FightEmergency.Fullest`: health share; a kind whose `ResistHealthDrain` is 0 left out), its heal by `BuffStatMods.Transfer` (ACE's transfer arithmetic) at its live health, chance = cast chance x its resist roll; a wound-up level only within `EmergencySecondsMargin`; Heal Self at the highest level cast at `BuffOptions.DefaultCastChance` (`VitalsMeans.Pick`), its heal `MeasuredHeals.SelfSeed` |
| The fills | `VitalsPolicy.Decide` on `LadderLoader.WarMage.VitalsPolicy` | `FightScenario.VitalsPolicy.cs` `TryPolicyFacts` | presses she may try at the mana order's try chance (`ManaConversionModel.LandingChance`, `ManaOrderOptions.DefaultTryChance`); the health drains whose base mana she holds; one Drain Stamina Other level at `BuffOptions.DefaultCastChance`; each level's cast chance and a drain's share past its source's resist; the creatures on her (M6 heal spend, [session.md](session.md) section 119); the fight's own tally (attacks and damage a second, the burst, the biggest hit) |
| The kill order | `TargetSwitch.Decide`, folded over the creatures on her | `Session/Vitals/TargetSwitch.cs` | each creature's damage a second on her (the race's sampled mean) and her bolt's damage a second on it |
| The bolt | the war pick's price of a kill (`InFightRules.KillPrice`: casting seconds, each cast's `WarPickSettings.CastOverheadSeconds`, its mana at `WarPickSettings.DanceSecondsPerMana`) | `Session/Mage/WarSpellPicker.cs` (D89) | ACE's sampled kill of each of her single-projectile spells |

**What ACE plays.** Her bolt's damage is drawn from ACE's sampled strikes (as the race draws it). A heal, a drain, a
press is `TryCastSpell` on her and on the creature with their vitals set to the fight's: a drain takes its share of
what its target holds now. Every cast's mana is ACE's `CalculateManaUsage` (Mana Conversion rolled; a fizzle 5; a cost
over the mana held is refused, as the server refuses it). Her regeneration is ACE's `VitalHeartBeat` on her every 5 s
(from a random phase); each creature regains health and stamina every 5 s. The creatures' attacks are the race's
sampled fights (`Blows.Sample`) landing at their own times until the creature falls; past a sampled fight's end the
creature goes on with the second half of another (its opening, a caster's mana among it, is not played twice). Every
melee or missile attack thrown at her costs a point of stamina (`Player_Combat.cs`: an evade is spared it at
Endurance's chance when her defence is trained). Cast times are the server's (`fight-sim cast`), plus
`--cast-overhead`.

**Two differences from the live fight, named.** The vitals policy's seconds a kill are left unmeasured (its seed: a
fresh pull), and its "damage a second over the fight" is this pull's own: at a long hunt's later pulls the client's
mean includes walking and looting and is lower, so its health floor sits lower there. Her bars start full.

**The tables.** `matchup` prints, for one to six on her (and `--at-once N`): dies, out of mana, stalled, her lowest
health (the mean over every fight, a death counted 0, and the 10th percentile), the heals she casts, the mana her casts
cost and the seconds to clear (won fights); then the casts a fight by kind, and when she runs out, when and with how
many killed. `bolts only` is the same engine with no heal, no fill and no mana count: the race's model played again, a
check of the engine (it reads a few points under the race: she regenerates, the race does not).

**First readings (2026-11-19, Winnie at level 8: health 39, mana 160, stamina 63, no Mana Conversion; 2,000 fights a
row).** The death chance the race gave is mostly gone with her heals; what is left is mana:

| Command | On her | Race, no healing: dies | With her heals: dies / out of mana | Lowest health mean / 10th pct |
|---|---|---|---|---|
| `matchup 11 --her Winnie` (Male Tusker) | 4 / 6 | 44.3% / 100% | 0.0% / 37.3%; 0.7% / 99.3% | 19.4 / 15; 15.4 / 9 |
| `matchup 22510 --her Winnie` (Tusker Kin, level 20) | 4 / 6 | 8.4% / 55.6% | 1.7% / 0.0%; 12.4% / 1.1% | 21.4 / 10; 13.2 / 0 |
| `matchup 22509 --her Winnie` (Tusker Kin, level 15) | 6 | 0.0% | 0.0% / 0.0% | 36.5 / 34 |
| `matchup 236 --her Winnie` (Female Tusker) | 4 / 6 | 23.4% / 99.8% | 0.0% / 16.3%; 0.3% / 99.6% | 20.6 / 16; 17.1 / 12 |
| `matchup --pack "11x3,22510x2,236x1" --her Winnie` (the Tusker Retreat's six) | 6 | bolts only 92.6% | 1.0% / 94.2% | 15.9 / 9 |

- **Where the mana goes.** Against the Retreat's six her casts cost 418.7 mana a fight on her 160; she runs out at 58.7 s
  with 3.2 of the 6 dead. The vitals policy's health floor (the biggest hit and the burst over a bolt and a drain) sits
  at 38 of her 39 health with five or six on her, so it drains between nearly every bolt, and its presses and stamina
  drains (12.1 and 6.1 a fight) feed that. With its fills left out (`no fills`: the emergency's heals and the bolt) she
  dies in 1.9%, runs out in 50.4%, at 28.5 s with 4.9 dead, on 165.5 mana: the hand count of about 178 mana against her
  160. The policy's fills cost her more than they give at this level and pack size: a finding about the policy, not
  the simulator. Fixed on `m6/heal-spend` (M6 heal spend, the rule in [session.md](session.md) section 119): the same
  command reads 199.8 mana, 33.5 s and 86.2% out of mana (the mean of four runs: 427.4, 77.6 and 91.9% before), and
  the Retreat's six at run 5's level-10 bars 293.7 mana and 43.6 s (387.0 and 60.5 before); no row of section 119's
  table dies or runs out of mana more often beyond its run-to-run band (the closest, the Tusker Cave's five: out of mana
  in 32 against 36 fights of 8,000).
- **The rank** (`rank --her Winnie --min-level 5 --max-level 40`): the Tusker Retreat 0x5C49 (pack 6.3) dies pack 69.9%
  with no healing, 5.2% with her heals, out of mana 34.8%; the Tusker Lodge 0x5749 (57 Male Tuskers, pack 8.9) 99.7%,
  0.5% and 74.8%. Live, run 5 hunted the Lodge at levels 15 to 20 with 0 deaths and her lowest health 49%: the race's
  100% there is the no-heal model's, and the fights with her heals do not kill her.
- **The Holding's approach, a sanity check only.** The level-77 climber of the two deaths (2026-11-15 04:13 and 06:17,
  `cache/m6/takeover/notes.md`) no longer exists: the climber was rebuilt, so those fights cannot be played again. On
  the saved characters, `matchup --pack ... --her "BSD Mage"` (level 127, health 222): an Infected Assailer 27800 with an
  Iron Spined Chittick 22506 kills her in 48.5% of fights with her heals (96.7% bolts only); two Assailers and a
  Chittick in 77.9%; an Assailer with a Reaper 27504 in 100% (the Reaper takes 35.85 health a second from her). The
  fights with her heals agree with the live deaths that these packs kill a healing mage. `Lifestone Throwaway`
  (level 68) dies to every one of them before her first cast ends: she wears nothing that stops an Assailer's acid
  (196 health a second on her), so she says nothing about the climber.
- **Run to run.** ACE's own random rolls are not seeded, so the race's sampled attacks move between runs: Male Tusker,
  four on her, read 27.2% and 44.3% no-heal in two runs of the same command. Read a verdict near a cliff as a band.

**What is not played** (printed under every table): the finishing bolt over the emergency's heal
(`FightScenario.Emergency.cs` `FinishStep`: one attacker about to die, `TODO(fight-sim-finishing-bolt)`); the drain
rule behind an emergency set aside after five casts that did not land; health and mana potions; the way out's mana
reserve and the leave itself; the buffs' upkeep; the components' level floor; the refill's Revitalize Self (the live
fight casts it between bolts outside the vitals policy, `ManaOrder`'s refill under `--fight-stamina-full`, hooked in
`FightScenario.BoltNeedsMana`; here her Revitalize levels only price the stamina a press takes, `VitalsPolicy.RefillPrice`,
[session.md](session.md) section 121); walking and arrival (every creature attacks from the first second); the time between pulls, where the pull's
rest and dance pay back what a fight ended without (`TODO(fight-sim-between-pulls)`: a rule that moves fills out of a
fight shows its saving here and not its cost there).

## The bestiary

`bestiary` writes the client's creature tables again from the world database (M6 item 16,
[automation-engine.md](automation-engine.md) section 2.1). The first three tables under
`src\Lifestone.Client\Session\Grounds\` (`creatures.json`, `creature-resists.json`, `creature-skills.json`) were first
made by one-off SELECTs, quoted in each table's `sources` header; this command is the script that makes them. The
fourth, `creature-behaviour.json` (E1d), was made by this command from the start.

```powershell
pwsh -File tools\fight-sim.ps1 bestiary                  # write the tables under cache\fight-sim\bestiary\
pwsh -File tools\fight-sim.ps1 bestiary --check          # write them, then compare each with the tracked table
pwsh -File tools\fight-sim.ps1 bestiary --columns        # list every table's columns: source and default; reads nothing
pwsh -File tools\fight-sim.ps1 bestiary --weenie 20,181  # what the tables read of these weenie classes; writes nothing
```

- **What it reads:** the world database through ACE's own models, queries only (`BestiaryWorld.Read`):
  - every weenie's type;
  - of every weenie, the properties the columns name (int, float, bool, data id, string, attribute, vital, skill);
  - of the creature weenies (`weenie.type` 10, Creature, and 15, Cow), the body parts, the create lists and the spell
    books;
  - the spell table.

  It builds no server object and reads no dat. A second row for one weenie and property stops the run.
- **What it writes:** `creatures.json`, `creature-resists.json`, `creature-skills.json` and
  `creature-behaviour.json` under `cache\fight-sim\bestiary\` of the checkout it runs in, each in the tracked table's
  layout to the byte. Nowhere else.
- **`creature-behaviour.json`** (E1d) has one row a creature weenie: its kind of creature, its body's setup and scale,
  how near it sees and hears, its tolerance, targeting tactic, friend and foe, the start and ranks of its stamina and
  mana, its three regeneration rates and the seconds of the heartbeat they are given at (E1e: `heartbeat_interval`, ACE's
  5 where the weenie states none), whether its casts spend mana, what a stamina or a mana drain takes of it, its Run
  skill's row and the experience of its kill.
  - A weenie without a property holds what ACE gives such a creature, and the column's line names the file and line of
    ACE's source that says so (`bestiary --columns`); -1 where ACE holds no value.
  - A value ACE computes from several rows is not stored. The client's reader computes maximum stamina, maximum mana
    and the Run skill from the table's terms and the attributes `creatures.json` holds
    (`Session\Grounds\CreatureBehaviour.cs`).
  - The proof of the reader is ACE's own objects: `creature <wcid>` prints the server's stamina, mana, Run, scale,
    ranges, tolerance and experience, and `CreatureBehaviourTests` pins fifteen kinds of them against the record.
- **The column lists** are one file, `tools\fight-sim\BestiaryTables.cs`. A column is one line: its name, its source
  table, the property by its name and number in ACE's enum, and what it prints where the database has no row.
  - A new column is a line there; a new table is a column list and a `TableFile`.
  - Every name and number is checked against the ACE build at each run (`BestiaryWorld.VerifyNames`): a property the
    list misnames or misnumbers stops the run before anything is written.
  - A column whose default is ACE's own says where ACE's source states it (`Column.Ace`), and the line is printed
    after the default.
  - The picks a creature's attack columns read (the wielded weapon that hits hardest, the earliest wielded launcher,
    the hardest ammunition, the body part that hits hardest) are in `BestiaryRows.cs`, each beside the clause of the
    first SELECT it follows.
- **The `sources` header** is not data. Each of the first three tables carries the header of the tracked table, byte
  for byte, so the prose is kept once. A new table states its own (`TableFile.StatedSources`):
  `creature-behaviour.json`'s is built in `BestiaryTables.cs` from what was read, the date (`BehaviourRead`, which a
  refresh moves) and every column's own line.
- **`--weenie`** answers "why is this weenie in a table, or not": its `weenie.type` by ACE's name, whether each table
  of creature rows has a row for it, and what the columns read of it either way.
- **The scope** is one list, `AceIds.CreatureTypes` in `BestiaryRows.cs`: the weenie types whose weenies are "creature
  weenies" in every table. ACE builds six weenie types as a Creature or a subclass of it
  (`WorldObjectFactory.cs:49-52, 57-58, 69-70, 127-130`). The tables take two: `weenie.type` 10 (Creature) and, from
  M6 item 16 E1e, 15 (Cow: the Low B belt's cattle), the types a generator places as a creature a player can fight.
  The other four are in `AceIds.LeftOut`, each with why: Vendor, GamePiece, Pet and CombatPet. Every run prints one
  line with how many weenies each type has and, for a type left out, its lowest weenie class. `creature` and `matchup`
  find the same two types; `rank` and its census still count `weenie.type` 10 alone (`TODO(fight-sim-rank-cattle)`).
- **`--check`** prints one verdict a table and ends with exit code 1 when one differs:
  - `identical (byte for byte, N bytes)`;
  - `identical as JSON`, with the first byte that differs;
  - `DIFFERENT`: for each member, how many weenie classes differ, the fields that differ, and the first ten with both
    values.
- **Refreshing the tracked tables** is a lead's step, after a world database update or a new column:
  1. `bestiary --check`, and read the differences: each is a changed row of the world database or a changed column.
  2. Copy the tables from `cache\fight-sim\bestiary\` over those in `src\Lifestone.Client\Session\Grounds\`.
  3. Where a column was added, edit the table's `sources` header in the copy (or state it in `BestiaryTables.cs`).
  4. `bestiary --check` again: `identical`. Run the default tests, and commit the tables with the differences in the
     message.
- **Tests:** `BestiaryGeneratorTests` proves the column lists, the picks, the writer and the check with no database (the
  tool's plain files are compiled into `Lifestone.Client.Tests` too). `BestiaryParityRunTests` runs `bestiary --check`
  itself; it is `Tier=Tool`, a lead-run command.

Parity when the generator was written (2026-11-17, `bestiary --check`): the three tables are identical byte for byte
(7,831 creatures and 1,140 spells; 6,451 resistance rows; 5,912 skill rows and 317 weapons). With E1d the check reads
`4 of 4 tables identical`: `creature-behaviour.json` has 7,831 rows. With E1e's cattle (2026-11-17): 7,848 creatures and
1,141 spells; 6,468 resistance rows; 5,929 skill rows and 317 weapons; 7,848 behaviour rows. The refresh added 17 rows
to each table and one spell, and changed no other row (`bestiary --check` before it: `0 with a different row, 17 only
generated, 0 only tracked` for each). The heartbeat's column then added one cell to each of the 7,848 behaviour rows and
changed no other cell.

## How far to trust it

Checked against the live log of 2026-11-15 (the Tusker Quarters, Tusker Slaves, the character at level 69 to 71):

| What | Simulated | Measured live |
|---|---|---|
| Her bolt's plain hit, least to most | 138 to 221 | 138 to 221 |
| Her bolt lands | 98.9% | 98.7% |
| A Slave's plain hit on her chest, mean | 1.47 | 1.46 |
| On her lower leg | 2.55 | 2.54 |
| On her head | 2.02 | 2.08 |
| On her foot | 1.94 | 1.90 |
| On her upper arm | 1.47 | 1.50 |
| Critical share of its hits | 10.3% | 10.5% |
| Its critical hits, least to most | 4 to 8 | 4 to 8 |
| One bolt cycle | 3.36 s (the server's minimum) | 3.56 s mean (the client's loop adds 0.2 s) |
| Drain Health Other I on a Slave | +60 health | +60 in 173 of 181 casts |
| Stamina to Mana Self VI, mana gained for stamina spent | 1.50 | 1.50 |
| How many Slaves come at once | 5.4 (the alert rule on the placements) | five on her (13:20 UTC) |

One difference is the log's, not the simulator's: the game prints no damage line for a killing blow, so the log
under-counts big hits and criticals. Her bolts are critical 5.2% of the time; the log shows 2.5%, and the simulator
predicts 3.0% for the hits that are printed.

**Corrected 2026-11-18 (M6 16 E5e3): her combat mode.** The mode ACE's combat code reads is the creature's own
`CombatMode`, which a constructor leaves at NonCombat; the tool set only the property of the same name. In NonCombat
her caster's Mana Conversion bonus and its melee defence bonus are not hers (`WorldObject_Weapon.cs` 86-90 and 241), so
every mana cost before that date was computed without the caster's bonus: against a Tusker Slave with the Piercing
Sceptre (her Mana Conversion x1.1232 with her Hermetic Link) the fire line's level VII read 25.2 mana a kill and now
reads 19.9. Hit sizes and kill times did not change. The rows of the table above were taken before the correction;
none of them is a mana cost of a war spell or an evade.

**Corrected 2026-11-19: a creature's dispel outlived its fights.** `Blows.Sample` took off what a creature laid on her
between its sampled fights but did not put back what it took off: a dispel (cast through `TryCastSpell` whether or not
its debuffs stay) left her unbuffed for every later fight of that sampling and, in `rank`, for every later creature.
In `rank --her Winnie --min-level 5 --max-level 40` the Tumerok North Vanguard Leader (9145) took her 35 enchantments to
6, her health from 39 to 26, her stamina from 63 to 38 and her mana from 160 to 135, and every creature ranked after it
met that her (the rank's header read "from full health (26)"). The restore now puts the taken entries back as ACE adds
one, and `rank` names any change to her that outlives a creature's fights (`TODO(fight-sim-rank-her-drift)` in its
NOT SIMULATED list; none in that run since).

## What it does not do yet

Each gap is named where it bites, in the output, never papered over.

- **Walking, looting, trips and waits** are not in any rate. `rank` and `matchup` say so beside the number. A ground's
  real rate is the simulated rate less that overhead, which a live log measures. At the Tusker Quarters on 2026-11-15
  the all-in simulated rate is about 336 kills an hour and the log shows about 150: more than half of her hour there
  is not fighting or recovering.
- **Her healing inside a fight is played only in the fights with her heals** ("With her heals", above): the race's
  death chances stay "from full health with no healing", and say so. What those fights do not play is printed under
  every table of them and listed in that section ("What is not played").
- **`rank` ranks by casting time,** not all in: her recovery is in `matchup` only. Its healed columns are verdicts,
  not part of the rate.
- **A creature has to reach her.** The simulator starts every fight with the creature already attacking, which is
  right for a caster and harsh on her for a melee creature she shoots on its way in.
- **Walls are not read for pack sizes.** ACE alerts only neighbours in a visible cell; the census counts every
  neighbour in hearing range, so indoors the pack size is the upper side. A mixed camp is fought as that many of the
  one creature.
- **Events and quests.** A generator that only runs during an event, and a portal a quest flag opens, are not told
  apart from the rest yet (`TODO(fight-sim-events)`): check a ground's way in before building on it.
- **Volleys and rings:** a spell with several projectiles is counted as one projectile striking her
  (`TODO(fight-sim-volley-geometry)`).
- **Wielded treasure** is rolled once for the creature built; another spawn of the same class may hold another weapon.
- **A creature ACE cannot fight offline** is listed under "NOT SIMULATED" with the reason and left out of the ranking.
- **A creature's own heartbeat is not played.** 139 creature weenies state a HeartbeatInterval that is not ACE's 5 s
  (from 1 s to 480 s; one states 0 and has no heartbeat). `creature` and `matchup` print the creature's own interval;
  the kill times, the race and a creature's stamina and mana in its attacks still tick every 5 s, and `matchup` says so
  for such a creature (`TODO(fight-sim-heartbeat)`). The bestiary's table and the client's reader hold the real
  interval.
- **Cattle are not ranked.** `rank` and its census count `weenie.type` 10 (Creature) alone; a Cow (`weenie.type` 15: the
  Low B belt's cattle) is found by `creature` and `matchup` and is a row of the bestiary's tables, and is no row of the
  ranking yet (`TODO(fight-sim-rank-cattle)`).

## Build

`tools\fight-sim.ps1` builds `tools\fight-sim\Lifestone.FightSim.csproj` into `cache\fight-sim\bin` when its sources,
or those of the client projects it calls (`src\Lifestone.Client`, `Lifestone.Data`, `Lifestone.Net`, their `.cs`,
`.csproj` and `.json`), are newer. The project is outside `Lifestone.slnx` (it needs the ACE build, which a fresh
checkout does not have) and references ACE's assemblies in place. It follows the loot simulator's offline boot (M5 item
7). Since 2026-11-19 it also references `Lifestone.Client` (engine-free: no Godot), so the fights with her heals call
the client's own rules; that brings Lifestone.Data's one package, Chorizite.DatReaderWriter, the repository's own. The
two transitive packages `Directory.Build.props` keeps out of every Lifestone build output are kept out of the tool's the
same way (the props do not reach a project outside `src\` and `tests\`): no package is added.
