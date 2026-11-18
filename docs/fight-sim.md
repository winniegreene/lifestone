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
  - the experience a kill pays and the ceiling an hour of nothing but casting would reach;
  - her recovery: what each of her drains, heals and stamina spells gives a cast (ACE's own cast on the real objects),
    her own regeneration, and the stamina-to-mana dance at its best threshold;
  - all in: the rate with the health lost and the mana spent paid back between pulls, for one to six at once.
- **`matchup --with "Frost Bolt VI;Force Bolt VI"`** answers "what would a scroll buy": the named spells are tried
  as if she knew them and marked in the table.
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
  - **The way in** is the portal the world database places that leads to the landblock, with its level limits.
  - The last table lists the grounds she may enter now whose packs do not kill her.
  - The full tables go to `cache\fight-sim\rank-<name>-<levels>.tsv` (every creature class) and `.grounds.tsv` (every
    landblock).

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

## What it does not do yet

Each gap is named where it bites, in the output, never papered over.

- **Walking, looting, trips and waits** are not in any rate. `rank` and `matchup` say so beside the number. A ground's
  real rate is the simulated rate less that overhead, which a live log measures. At the Tusker Quarters on 2026-11-15
  the all-in simulated rate is about 336 kills an hour and the log shows about 150: more than half of her hour there
  is not fighting or recovering.
- **Her healing is not played inside a fight.** The death chances are "from full health with no healing": how hard a
  creature hits against how fast she kills it. `matchup` pays the health and mana back between pulls. A policy
  simulator that plays her drains and heals during the fight, on the same objects, is the next piece.
- **`rank` ranks by casting time,** not all in: her recovery is in `matchup` only.
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

`tools\fight-sim.ps1` builds `tools\fight-sim\Lifestone.FightSim.csproj` into `cache\fight-sim\bin` when its sources
are newer. The project is outside `Lifestone.slnx` (it needs the ACE build, which a fresh checkout does not have), uses
no NuGet package, and references ACE's assemblies in place. It follows the loot simulator's offline boot (M5 item 7).
