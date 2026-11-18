# The one engine: who decides what in the automation

M6 item 16 (m6-plan.md D139, the owner, 2026-11-17). The automation's decisions were built once per process: the fight,
the travel legs, the quest steps, the trips and the death handler each carry their own copy of the mana work, the
fight-back, the loot and the wear. This document is the design that gives each decision one owner, the order the copies
are removed in, and the tests that stop new ones. It is read with [session.md](session.md) (the outer loop and the
stations as they stand) and [milestones/m6-derived-rules.md](milestones/m6-derived-rules.md) (profiles hold facts only).

`S/` is `src/Lifestone.Client/Session/`. All of this is engine-agnostic client logic; nothing new goes in the Godot
project.

## 1. What was found (m6/15 at 93d25f9e, 2026-11-17)

| Decision | Copies |
| --- | --- |
| Fight back an attacker | 5 loops: the fight (`FightScenario.SelfDefence`), the travel leg (`TravelLegSafety.FightBackCore`), the quest kill loop (`QuestStepLeg.KillLoop`), the heal station, the death trip |
| Who attacks her, who is in reach | 8 detectors, with reaches of 20, 24 and 40 m and different quiet windows |
| The heal | 6 or more, each with its own lines |
| The mana | 5 engines (`ManaOrder`, `ManaLoopRule`, `VitalsPolicy`, `RechargeRule`, the travel dances), 5 copies of the Stamina to Mana level list, 4 rest loops, 3 way-out reserves |
| A target's health | 2 models; the fight's own read assumes 400 health and Magic Defense 0 for a creature outside its ground's list |
| A buff pass | 9 construction sites |
| What may be sold | one table (`PackRuleBook`) and 4 older lists that never ask it |
| The purse, free slots, capacity, the trip verdict | 5, 4, 2 and 3 a trip |
| A buy | 5 sequences; only the town trip's changes trade notes when coin is short |
| Wear | 6 constructions of the judge, 40 wield sends in 16 files |
| Creature facts | 3 tables, hand fields in 19 profiles, 74 pinned awareness ranges, run-time joins from the dats; no generator |
| Where she is and what she is doing | spec arguments as strings, side channels on `JourneyFacts`, private flags, and the character's name as a mode switch |

What is already one and stays: the outer loop (`AutopilotScenario`), the wire-fed inventory model the panels read
(`PlayerInventory`), the survival layer over every state but the fight (D119, stage 1; since E3.1 a quest's sub-fight is the fight's case, section 5's E3 row), the pure rules in `S/Vitals`,
`PackRuleBook`, the town trip (`ResupplyRun`), the wear judge and its scores, the corpse looter, and the one owner of
the rung (`RungOwner`, D93).

## 2. The design

1. **The bestiary.** One record of a creature kind's capabilities by weenie class, behind one reader: identity, the three
   vitals and their rates, defences and resistances, attacks and the spell book, timing facts, movement, awareness,
   reward, body. A `bestiary` command of `tools/fight-sim` writes the tables from the pinned world database (read-only),
   through ACE's own models. The tables stay one file a source table, with one `sources` header. What only the dats
   hold (attack timing, pace, the body's height) stays a run-time join. What neither holds is learned live, kept in a
   small checkpointed layer and logged by name.
2. **The threat board.** One picture a session of who attacks her, who is in reach and what each kind's hits cost, with
   one model of a target's health and one reader of the damage lines. It hangs off the session's state, not off a
   scenario: a scenario is rebuilt at every rung move.
3. **The survival layer over everything.** The heal, the drain, the mana, the rest, the way-out reserve and the leave
   are decided in one place for every activity, the fight included (D119's stages 2 to 4). The fight stays the
   executor and asks.
4. **One combat entry.** A combat brief says why she fights: hunt a ground, clear named kinds, recover a corpse, defend
   and go back, or pass through (D137: run past what she outpaces, fight when caught or blocked), with whether she may
   pull and loot, a time box and where to return. Two bodies carry it and share every decision above:
   - the fight scenario, on a mapped ground (it needs a profile, a tour and a walker);
   - the skirmish, anywhere else: the travel leg's fight-back as an engine of its own. It already has no heal inside
     and publishes its reserve for the layer.
5. **The quartermaster.** One service a session over the inventory model: the views (counts, purse, free slots,
   capacity, the load line), the keep, sell or drop verdict (`PackRuleBook` alone), one ledger of needs, one trip
   verdict carried through the trip, one buy (with note-changing, for every caller), one wear (one judge, one wield
   routine). An activity posts a need; it runs no sequence of its own.
6. **The thin director.** The outer loop keeps one order: the person, death, survival, the combat reflex, the
   quartermaster's needs, then the task's subroutine. A task (hunt a ground, a quest's steps, a walk, a buff pass, a
   villa errand, a steer order) states what it wants. One context object replaces the side channels and the name
   checks: how a character plays is data on the character.

The melee and archer paths (`HuntScenario` and its helpers) are left as they are; the joints above are shaped so they
can come under later.

## 3. The method

- **A strangler, never a rewrite.** Each slice ships and deletes the copy it replaces.
- **Shadow first.** A new decider runs beside the old one on the live climb and reports where they differ, on change
  only and without allocating; after one live gate it takes over by an option; then the old path goes (a new judge is
  report-only in its first gate).
- **Ratchets.** `tests/Lifestone.Client.Tests/Repository/EngineRatchetTests.cs` counts, for each concern, the lines
  that still decide it outside an owner, and holds every file at its ceiling (`engine-ratchets.json` beside it). A new
  copy fails the default tests. A stage lowers the ceilings it earns. Only a merge of work written before the ratchet
  raises one, by name in its message.
- **Numbers from the corpus.** A changed fight number has a `tools/fight-sim` note first.
- **Beside a live climb.** New files and thin call-site edits in the two files live fixes land in (`AutopilotScenario.cs`,
  `FightScenario.cs`); block moves right after a merge; the log tokens `guard:`, `survival:`, `fight-back:` and
  `emergency:` keep their names; the session's ledgers and the checkpoint keep their meaning across a restart.

## 4. The ratchets (2026-11-17)

| Concern | Lines counted | At writing | Owner to come | Stage |
| --- | --- | --- | --- | --- |
| ThreatDetectors | a read of a defence event; a new `AttackWatch` or `HostileWatch` | 23 in 15 files (24 in 16 since E2a: the board's own one read is counted) | the threat board | E2 |
| HealDeciders | `HealPick.Choose*`, a new `FightEmergency` | 5 in 4 | the survival layer | E3 |
| ManaDeciders | `ManaOrder.Decide`, `ManaLoopRule`, `RechargeRule`, `VitalsPolicy.Plan` | 20 in 5 | the survival layer | E3 |
| WayOutLine | `ManaWayOut.LineOf` | 3 in 3 | the survival layer | E3 |
| BuffPasses | a new `BuffCycle` | 9 in 6 | one owner of a pass | E4 |
| BoltPickers | `WarBoltPick.Choose*`, `WarSpellPicker`, `FightPicker` | 11 in 6 | one bolt pick | E4 |
| VendorSends | `Actions.Buy`, `Sell`, `OpenVendor` under `S/` | 25 in 8 | the vendor counter | E5 |
| WieldSends | `Actions.Wield`, `Unwield` under `S/` | 40 in 16 (15 in 7 since E5e1: the wear service's own two sends are counted; 13 in 6 since E5e2: the fight's replace in hand) | the wear service | E5 |
| EquipJudges | a new `SelfEquipJudge` | 6 in 6 (1 in 1 since E5e1: the factory's own construction) | one judge factory | E5 |
| TripVerdict | a new `LogisticsCounters` | 7 in 6 (1 in 1 since E5d: the trip desk's one construction) | one trip verdict | E5 |
| PackViews | `FreeSlots(`, the coin property, `EncumbranceCapacity(` under `S/` | 101 in 47 | the quartermaster's views | E5 |
| SaleDeciders | `PackRuleBook.Classify(`, `VendorStock.Unsellable(`, `NotSellable(`, `IsKeptAside(`, `WhyKept(`, `_notForSale`, `SaleItemsBefore(` under `S/` | 23 in 12 (written by E5c, on m6/14 alone; 5 in 5 since Q11: the verdict's own read, 1; `SurplusDrinks`' `NotSellable`, 2; the melee and archer paths, 2. It was 14 in 8 since E5c2: the trip's old list and its guard, 10 lines in 4 files; the wear pick's kept burden, 1; the verdict's own read, 1; the melee and archer paths, 2) | the sale verdict (`SaleVerdict.Of`) | E5 |
| CreatureReaders | `CreatureTable`, `CreatureResists`, `CreatureSkills`, `CreatureAwareness`, and from E1d `CreatureBehaviour` | 25 in 15 | the bestiary | E1 |
| NameAsMode | `IsClimber`, `ClimberName`, `PicksByNet` | 59 in 33 | the context object | E6 |
| ContextSideChannels | the `JourneyFacts` channels, spec-argument reads | 66 in 13 | the context object, the brief | E6 |

The counts are the larger of each file's on m6/14, m6/15 and m6/long-run, so all three pass as they stand.

## 5. The stages

Lane A is combat (E1, E2, E3, E4, in that order). Lane B is the quartermaster (E5), beside it, except its slices that
edit the fight's loot step or the outer loop's trip verdict, which wait their turn with lane A.

| Stage | What | Proof |
| --- | --- | --- |
| E0 | This document, D139, the ratchets, a baseline from the live run | the default tests |
| E0b | The climb runs headless: the kit rebuilt at a rung move comes from a factory in the client, where the play host's own code does it today | a headless test that climbs across one rung move; later shadows replay a whole climb offline |
| E1 | The bestiary: the facade over today's readers; the fight's own target read through it (the assumed 400 health goes); the generator, first reproducing today's tables exactly; the new columns (awareness, tolerance, friends and foes, experience, Run, the stamina and mana pools, the rates, drain resistances, scale); the readers moved one at a time; whether a hovering creature can be told from its body in the dats. Status 2026-11-17: the facade (E1a) and the generator (E1c: `fight-sim bestiary --check` writes the three tables again byte for byte, [fight-sim.md](fight-sim.md) "The bestiary") are in; the new columns are a fourth table, `creature-behaviour.json`, whose values the facade's record holds beside the ones the automation reads today (E1d: no reader moved; of the 47 kinds the profiles name, 37 are avoided at ACE's default 18 m where their own range is 15 to 30 m); the walker's creature feed reads a kind's own range beside the pinned one (E1f: `--walk-awareness=pinned|own`, default `pinned`, so no walk changes; in shadow it names each kind seen whose two ranges differ and, once a travel leg, the creatures passed inside their own range and outside the pinned radius with how many were then among her attackers, every line starting `awareness-shadow:`; the flip to `own` waits for a live gate, section 6); no number of a creature is assumed without a word outside the fight either, and the tables' holes are closed (E1e: a spell's resist is one function, ACE's own roll for a Magic Defense of 0 too, `Vitals/ResistRoll`; the legs name what they assume of a kind once a leg, `LegKindRead`; a creature's maximum health is ACE's for a weenie with no MaxHealth row or no Endurance, 623 kinds; the cattle are kinds, 17 weenies of WeenieType.Cow; the heartbeat's seconds are a column and the rates read a second) | the generator's parity; every weenie class a profile names unchanged or listed; `fight-sim creature` against the facade |
| E2 | The threat board, in shadow beside `HostileWatch`, then every detector reads it; one target-health model; one fullest-creature pick. Status 2026-11-17: the board is in and decides nothing (E2a: `S/Threat/ThreatBoard.cs`, one a session, hung off the session's state, fed once from the event bus through one reader of the damage lines, `ThreatLines`; a query's parameters are the detectors' differences, `ThreatQuery`: the reach, the quiet window, whether only a hit counts or every attack heard, whether the ground's target list counts without a hit, whether only attackers count). It stands in shadow beside the two detectors asked most: the outer loop's `HostileWatch.InReach` and the travel legs' attack watch (the attackers present each frame of a walk, and the lull test). A difference is one line when it appears, changes or ends (`threat-shadow: outer-loop: ...`, `threat-shadow: travel-leg: ...`, `threat-shadow: travel-leg-lull: ...`); the cycle line closes with `threat_shadow_agree=` and `threat_shadow_differ=`, and a travel leg writes the same two fields on a line of its own at its end. No detector reads the board yet, and no option turns the shadow off. E2b: every read of who is near takes a creature where it is drawn (section 6, `TODO(engine-threat-flip)`), and the rest of the detectors stand in shadow, each as one board query of its own reach, window and count (`S/Threat/ThreatDetectorShadow.cs`), one lane each: `fight-defence` (the fight's self-defence list), `fight-refresh` and `fight-awake` (its buff refresh's hostile read and awake read), `fight-pull-rest`, `keeper-awake`, `heal-station` (the pre-check), `quest-steps`, `death-path` and `death-corpse-leg`, `travel-fight-back` (the fight-back's pick), `retreat` (the retreat line's attackers) and `vitals-guard` (the guard's attackers on her). The cycle line's two fields now carry the outer loop's three lanes (the watch's, the retreat's, the guard's) and the travel leg's line the pick's with the walk's; a fight, a rest of the keeper, a heal pre-check, a quest leg and a death handling each write one line of their own at their end (`threat-shadow: <owner>: its asks: threat_shadow_agree= threat_shadow_differ= (agreed/differed by detector: ...)`). E2d: one picture of a target's health stands on the board and decides nothing (`S/Threat/HealthPicture.cs`, `ThreatBoard.Health`): a creature's maximum from the bestiary, corrected by the creature's own appraisal; its health that maximum less the damage her lines report, with the server's health updates and appraisals as the base and the creature's own regeneration between them, from its kind's rate a second. It is fed once from the event bus and by the casters of her drains, survives a fight's and a leg's end, and forgets a creature at its death or removal. The fight's model and the guard's and the legs' answer every read as before; beside each read the picture is asked for the same creature, one lane a reader (`S/Threat/HealthShadow.cs`: `fight-picker`, `fight-opener`, `fight-drain`, `fight-life-through`, `fight-mana-loop`, `fight-policy`, `fight-yield`, `fight-emergency`, `fight-war`, `guard-fullest`, `guard-race`, `guard-kill`, `travel-fight-back`, `travel-life-strike`; the last five gone in Q5, whose readers ask the picture itself). A difference beyond the tolerance (a point, a hundredth of the larger maximum, or one heartbeat's regeneration of the kind) is one line when it appears, when its cause changes and when it ends (`health-shadow: <reader>: <name> 0x<guid>: its model says H1 of M1, the one picture says H2 of M2 (<why>)`, the causes: the maximum, regeneration, a server update one did not take, damage one did not hear); a fight and a travel leg at their end, and the outer loop for its guard beside each cycle line, write their asks on a line of their own (`health-shadow: <owner>: its asks: health_shadow_agree= health_shadow_differ= (agreed/differed by reader: ...)`); the cycle line and the verdict lines keep their shape. The fullest pick (E2d): the comparison "is this one fuller than the best so far" is one function (`S/Threat/FullestPick.cs`), and each pick is a rule of its own numbers (`FullestRule`: the vital and its unit, the band, the tie, the floor, what an unknown counts as): the fight's emergency and its drain rule (one pick with two reaches: the health share, the nearest among those within a hundredth of the most), the ring's anchor (the health share, the picked target among equals), the mana loop's stamina source (the stamina, more than none, the first met among equals) and the vitals guard's drain target (the health a drain takes, the first met among equals). Each picks the creature it picked before, held against its old comparison over mixed creatures (`FullestPickTests`); each site still gathers its own candidates (section 6, `TODO(engine-fullest-gather)`) | a report-only live gate, then the flip against the baseline |
| E3 | The fight under the survival layer, one rule a slice: the shadow hook; the mana loop and the stamina drain; the drain rule and the heal; the emergency pick; leaving and the one way-out line. Decided first: what the guard does above a quest's sub-fight, where two heal deciders run today. Status 2026-11-17, the shadow hook (E3.0): the rule answers a fight (`S/Autopilot/SurvivalRule.Fight.cs`, `DecideFight`, pure: a lost race handed over, leave; about to die, heal by her own spell or by a drain on the layer's pick, or leave; cannot finish these attackers on the mana she has, take mana by the better sum of the press and of the stamina drain with its press, or leave; else go), and decides nothing: the den-fight station gives the fight the ask (`FightScenario.Survival`, null in every other construction), and beside each of its own deciders the fight asks and compares (`FightScenario.SurvivalShadow.cs`; each decider keeps its body under a name that ends in `Own`). The ask reads her attackers from the board (`ThreatBoard.Gather`), their health from the one picture and their stamina from their appraisals, and prices the means on the layer's own picks (`SurvivalRule.FullestHealth`, `FullestStamina`: the one fullest pick). Four sites: `emergency`, `drain`, `mana-loop`, `way-out`. A difference is one line when it appears or changes and one when it ends, at most one a site a second (`survival-shadow: <site>: the fight <move>, the layer says <verdict> (<why>; the numbers)`); a fight's first ask names the fight's lines, and its end writes `survival-shadow: fight: its asks: survival_shadow_agree= survival_shadow_differ= (agreed/differed by site: ...)`. The line pairs the two answer by, the guard over a sub-fight and the order of the flips are in section 6. One healer in a sub-fight (E3.1), a STAGING STEP and not the end state: the owner's rule is that keeping her alive is one layer above quests, walks, trips and fights, with the fights moving under it in stages (D119); E3.1 moves nothing under the layer. It makes a quest's sub-fight and the ground fight ONE case, so that each later stage flips one decider once, for both. While the Trip state's station steps a `FightScenario` with its emergency on and no stop asked (`IStationSubFight`, `FightScenario.EmergencyOn`), the layer's frame is not run (no heal pick of the guard, no wait), as in the Fight state; the outer loop watches it as it watches a ground fight for what is about her staying alive (`S/Autopilot/AutopilotScenario.SubFightWatch.cs`): the retreat on the fight line read on the sub-fight's own ground, the lost race, the mana way out with its reserve armed in the fight, each a leave carried out as the layer's leave (`failed:quest:<key>:retreat:<token>`, home by Lifestone Recall for the race and the way out) that keeps the quest callable as a ground fight's retreat goes back out (called again from home after the keeper's pass and her buffs; the ground fight's own bound kept for the quest: a leave for mana is not counted, the second leave on the fight line in a cycle gives the quest up for the run, `failed:quest:<key>:retreat:again:<token>`), and the health floor, which holds the station for the keeper's heal in place only with no hostile in reach. The ground's own stop reasons are not asked. A ground fight is unchanged | a report-only live gate a rule; deaths and kills an hour against the baseline; for E3.1 a quest with a kill step: the fight's own `emergency:` lines, no `guard: trip:` line while the fight runs, `survival: trip: stand-down:` once a sub-fight, any leave by name |
| E4 | The combat brief in place of the fight's spec arguments; the skirmish moved out of the travel leg unchanged; the heal station and the quest steps' defence on it; the named-kill loop's casts through it; one bolt pick; one owner of a buff pass | a recorded travel leg with a fight-back replayed before and after; a live errand |
| E5 | The views; capacity with augmentations (its own sourced slice); the vendor counter for every buy; the sale verdict from the rule book alone; one trip verdict; one judge and one wield routine; the needs ledger. Status 2026-11-17, the sale verdict (E5c): the town trip's sale list is the verdict's (`SaleVerdict.Of`; every filter of the sale is a row of the table, [pack-rules.md](pack-rules.md)), proved equal to the old filters item for item on every trip the tests run, and guarded in production for its first live gates (the old list is made beside it once a sale: `sale_parity=agree`, or an error line, `sale_parity=differ:N` and only the items both lists offer are sold); the field's make-room rule, the trip trigger's sale estimate, the start-up audit's sale gate, the trip's junk drop and the item mana drain's never-list ask the verdict too (E5c2: each a named behaviour change, [pack-rules.md](pack-rules.md) "Who asks the table"; the field keeps three named differences of its own in one place, `CantripRoom.FieldKeep`: a piece not appraised, a useful great cantrip of any family, a drink she uses); outside a trip the verdict's context reads the every-spell rule's scroll keeps and the drinks a trip's budget would sell from the places the trip reads them (`EverySpellKeep`, `PackBudgetRead`), and the job states who she is, the SpellTable and its facts once a session (`S/Quartermaster/SaleDesk.cs`); a class ACE refuses to buy has a row (`unsellable-class`); the report-only shadow E5c put beside the copies is gone with their own rules (section 6 for what still stands beside the verdict). The wear judge (E5e1): every site asks one factory (`S/Quartermaster/Wardrobe.cs`, `Wardrobe.Judge`) and states what it stated before (`WearJudgeAsk`), so no answer changed; the differences between the sites are named there and listed in section 6 (`TODO(engine-wear-differences)`). One was settled: a gear keep reads the chance the fight casts at, which the job states once a session (`Wardrobe.KeepCastChance`; the level up job: the ladder's `plan_cast_chance`, D41), where the town trip read 0.9 whatever the job. The wield (E5e1): one routine beside the judge (`S/Quartermaster/Wardrobe.Wield.cs`, `Wardrobe.PutOn`: the piece held and not worn, what blocks the slot off first, the slot by `WieldSlotChoice`, one wield, the worn state read after, a refusal by name), a coroutine in the vendor counter's shape; the town trip's gear and leather passes, the worn items' rewear, the quest steps and the Pathwarden station yield from it and keep their own lines and counters; the tick-driven callers (the recovery table, the worn restorer, the death handler, the fight's wear on loot, the bind leg) take its first half (`Wardrobe.Send`) and read the state themselves; what is left is in section 6 (`TODO(engine-wield-sites)`). One answer (E5e2): the session's wardrobe builds the judge from the session (who she is, her level, her book and skills, what is lit on her), the ground and the job, and a site passes no piece of it (`Wardrobe.For(state).JudgeOn(ground, spells)`): the useful cantrips are the keep-aside in force for her, the element the rung's then the plan's family, the armour scored against the ground's damage types, the caster terms the ladder's, the lit pieces read, the held-back casters one memory a session, each for every site (the trip, the worn restorer, the fight's own loot judge, the autopilot's caster pick). Two named rules stay in the factory: the gear keeps ask the element (`KeepJudges`), and a rearm where no job stated a ground judges on none (`RearmJudge`; the autopilot states its ground at its bind). The owner's rule is a guard on every judge of a session: a worn piece that casts a great cantrip useful to her is never taken off for a piece that casts none, in the armour rule and in the wear pick. The gear keeps read the chance the fight is built with (the command line's `--buff-cast-chance` when given). The fight's replace in hand sends through the wear service. The trip verdict (E5d): the logistics counters are built once for a ground of a session (`S/Quartermaster/TripDesk.cs`, `TripDesk.For(state).On(ground)`), where seven places built their own with different arguments, and every site asks that set: the outer loop's trigger, the fight's floor read, the logistics station, the town trip, the buy-trip and the panel. A scenario is built before its first step, so it states its counters and the desk adopts them at the first step (`AutopilotScenario.Trips.cs`). The reads that must move nothing (the panel's, the audit's kit read and the buy-trip decision after it, the fight's floor read) ask a reading twin: a second set built by the same line from the same statement, on which a learn hold is asked and never released; no count is shared with the set that decides. What the seven read differently is listed in the desk's summary; no decision changed with the move: the floor-only lines are the same whichever foci she is read to hold on every ground of the catalog (`TripDeskFloorDatTests`), the job's statement of them stands and a site that reads them differently is named (section 6), the visit's cut keeps end with its buy list (`LogisticsCounters.UncutKeeps`), and a rebuilt scenario states its own mark of the schedule. One difference was an accident and is settled: the job's order lines (the den run's travel spells, a quest's bolt line) were on the outer loop's counters alone, so the station counted a trip called for one of those scrolls without them; they are the session's now, the station names the learn the trip was called for, and a trip that buys no scroll holds that learn's trigger (before: the rung's own next scroll was held and the trigger called the job's line again at once). The verdict is carried (E5d): where the trigger calls a trip (the counters' verdict, the restockable stop, the hour, the buff bot's lines, a lapsed line) what it decided is made once into the trip's order (`TripOrder`: the reason, every stock line with its held and wanted counts, the purse read, the sale estimate when one was made) and said (`autopilot fight: trip: the verdict the trip carries: ...`); the logistics station takes it, says where its own count at the fight's end differs and goes on it (a trip the trigger called is made on the trigger's reason though the count at the fight's end finds nothing due, where it was skipped as `no-trip-due` and called again; `trigger=` names the trigger's reason; a stop line no trip restocks still stops, a pack that cannot be counted is still no trip, and a station no trigger called goes by its own count as before); the town trip counts again at home, acts on that count and says where it differs from what the trip was called for (`trip: the verdict said <x>, at home <y>: <each difference>`, or `trip: the verdict stands at home: <x>`); after the visit the station says whether the reason was served (`trip: check after: ...`, a warning when it still stands). A frame of the fight that asks nothing, and a death, void a call no station took | stand-in vendor tests a caller; the sale verdict's shadow over a live trip; a quest buy paid from notes |
| E6 | The context object; the director's order; one caller of the level-up spend; steer orders as intents | the default tests; a live cycle |
| E7 | Profiles hold facts only; the dead paths go; session.md brought up to date | every ratchet at its floor; a fresh climber from creation to 80, unattended and cheat-free, on the engine |

## 6. Open, by name

- Decided and carried out by E3.1, `TODO(engine-guard-over-subfight)`: the vitals guard and the fight's own emergency both decided a heal in a quest's sub-fight (the Trip state: the layer and the guard are on, and the
  fight under them has its emergency and its drain rule). E3.0 wrote the evidence (`survival-shadow: guard-over-subfight: <station>: the vitals guard ...; the fight under it ...`, `AutopilotScenario.SubFightShadow.cs`) and what two deciders cost, read
  from the code: while the guard held, the fight was not stepped (no bolt, no target, no self-defence) and was not told of the hold, so its clocks ran through it and a cast in flight ended "late" into its measured seconds; each kept its own
  run of the heal (two hystereses, lines raised by two incoming rates); the guard's drain took health from a creature the fight tracks without its model being told; and no mana was kept for the way home (the fight's way out is armed from the
  outer loop's fight watch, the guard's reserve is a travel leg's). The choice taken: the guard stands down over a sub-fight as over a ground fight, and the sub-fight gets the ground fight's leave asks (section 5, the E3 row). It is a staging
  step toward the owner's rule (one layer above quests, walks, trips and fights; the fights move under it in stages), not the end state: it removes the second case, so `TODO(engine-survival-flip)` flips each decider once for the ground fight
  and the sub-fight together. What E3.1 leaves, by name: (1) the guard still acts above a station's fight that is not the mage's `FightScenario`, has its emergency off (`--fight-emergency-floor 0`) or has been asked to stop, and E3.0's line
  still says so once a sub-fight; (2) the health floor's heal in place holds (does not step) the sub-fight while the keeper's pass runs, with no hostile in reach only, because a sub-fight cannot end and begin again as a ground fight does
  without failing its step: the fight's clocks run through that hold as they did through the guard's (given up after `SubFloorLaneSeconds` if the action lanes do not free); (3) a leave keeps the quest callable by the ground fight's rule, so the loop guard is the ground fight's too: a leave for mana is counted nowhere, as `retreat:again` never counts one, and a hunt that
  leaves for mana again and again without a gain is bounded by nothing but the stop rules (a ground fight's retreats for mana are not either); the quest is called again once a leave from home, is not carried over a restart, and a quest
  task and the villa's fetch are begun again by their own callers; the layer's own no-mana leave and a death fail the quest for the run as before; (4) the retreat rule's `aside=` in a sub-fight is the guard's set-aside, not the fight's own (`TODO(m6-retreat-fight-aside)`, as in a ground fight).
- `TODO(engine-survival-flip)`: the fight's deciders move under the layer one a slice, in the owner's order (D119), each by an option after a live gate read its site's `survival-shadow:` lines, then the old path goes:
  the mana loop and the stamina drain (site `mana-loop`), the drain rule and the heal (`drain`), the emergency (`emergency`), leaving and the one way-out line (`way-out`). The pairs of lines the two answer by today,
  which each flip settles for its question: heal, the fight's emergency line (floor 60%, safe 85%, margin 10%, raised by the recent hits) and its drain rule (50% to 75%) against the ladder's wait and back lines for
  health (50%, 80%); leave, the fight's way-out line in mana points (Lifestone Recall's need and one Drain Health Other I) and the retreat rule's derived health line against the ladder's leave lines (health 50%, mana
  15%); take mana, the mana loop's lines (`vitals`: a press worth half a bolt, bolts between, the stamina line at 80%) and the policy's floors against the mana that finishes her attackers, with the ladder's wait line
  for mana (25%) where that is not read. The rule holds none of: the bolt that kills the last attacker before a heal, the reserve a heal may not spend, the presses' least worth; each comes into the rule with its stage.
  The layer's attackers are the board's (heard within the quiet window, within 40 m, what may be attacked) and their health the one picture's, so a flip of a heal's pick wants `TODO(engine-health-flip)` and
  `TODO(engine-threat-flip)` read first. Since E3.1 a quest's sub-fight is the ground fight's case (the stand-down and the watch above), so each flip is made once and holds for both; E3.1 itself is a staging step and flipped nothing.
  The leave's numbers the `way-out` flip starts from: the fight's reserve is `ManaWayOut.LineOf(recall, drain)`, the recall's need the larger of the spell's 50 mana and the recall station's 20% of her pool (`RecallHomeStation.MinManaPercent`,
  `AutopilotScenario.RecallNeeds`), the drain Drain Health Other I's 10 (`tools\fight-sim.ps1 cast "Lifestone Recall"`: base mana 50; `cast "Drain Health Other I"`: base mana 10): 60 points while her pool is 250 or less, then a fifth of the
  pool and 10. The ladder's `leave_mana_percent` 15 is under Lifestone Recall's own 50 mana for every pool under 334, so a leave decided at 15% cannot pay the recall there: on the plan's pool (the spell plan's own projection,
  `SpellPlan`'s `MageSpendingPlanner.Compute` call, unbuffed) level 1 has 100 mana (reserve 60, 15% is 15), level 10 132 (60, 19.8), level 22 220 (60, 33; live at gate A 196: 60 against 29.4) and level 40 292 (69, 43.8). No level's pool is under
  the reserve; at level 1 the reserve is 60% of her pool. Two numbers of E3.1 are Lifestone's own and wait for that stage too: the health floor's hold over a sub-fight is given up after 6 s when the action lanes do not free
  (`SubFloorLaneSeconds`) and then starts no new hold for 30 s (`SurvivalAsideSeconds`, the layer's aside); both are to be derived (an action's own answer time, the heal's worth against the fight's pace) when the heals move under the layer. The reserve stands only while her book holds Lifestone Recall with its components and tie (`way out: off:` otherwise, the fight's old order); the level from which a fresh character's book holds it is not derived here.
  Stage 1 built, M6 16 Q6 (2026-11-18), behind `--fight-survival-mana` (`FightOptions.SurvivalMana`, default off): with it on, the mana loop's three rules (the fixed loop of another character, the climber's loop by the sum, her fills by the vitals policy and the upkeep's ask) hand the layer the reads they decided by (`SurvivalFight.ManaLoop`, `SurvivalManaLoop`: her bars, the bolt's most mana, the presses now, that fills and after the drain, the drain's give and mana and the fight's stamina source, the dying target, the attackers, the bolts owed, the rest's stamina line, the policy's facts and its two sources, and the ladder's vitals and policy lines) and `SurvivalRule.DecideFightMana` decides; the fight reads, guards and casts as before and says once a fight `survival-mana: the survival layer decides ...`. With the loop handed, `DecideFight` answers the mana by it and keeps E3.0's sum for the leave alone. Off, the fight decides by its own rule. Still the fight's and not this stage's: the reads (which creatures are asked for their stamina, the policy's facts, the fallback to the sum when they are not read), the gates before the reads (Mana Conversion trained or the mana over `mana_loop_under` for the fixed loop, the arena), the drain's pick (every creature of the ground in reach with a clear line, by appraisal or its kind's stamina as read, not the layer's own `FullestStamina` over the board's attackers: `TODO(engine-threat-flip)`), the presses after the loop in `BoltNeedsMana` (M6 7 AD's press before the bolt that would leave none, D27's pre-emptive transfer, the mana order's wait and drink), the pull's lines (`PolicyPullRule`), and the shadow's `mana-loop` site, which still compares E3.0's sum with the move. The live gate reads the fight's own `mana loop:` and `vitals policy:` lines against an off run before the default flips.
- `TODO(engine-hover-from-setup)`: whether "a bolt cannot reach it" follows from the body's collision shapes and the
  height a bolt is aimed at. Until proven against a creature learned live, the learned layer stands.
- `TODO(engine-quest-skirmish-watch)` (Q8, `S/Scenarios/Journey/QuestStepLeg.Skirmish.cs`): the quest steps host the skirmish with no attack
  watch of their own (a new watch is a new detector), so the dance under fire there reads no hits coming in and never judges
  her losing, and the fight-back keeps no tally of the hits; the threat board's incoming read takes the watch's place when the
  detectors move onto it (`TODO(engine-threat-flip)`). Q8 also left the quest steps' old kill loop behind
  `--autopilot-quest-own-kill-loop` (`TODO(engine-quest-old-loop)`): it goes, with `QuestStepLeg.Fight.cs`'s own dance's
  wait and its no-hurt counts, after the skirmish's first live gate on the errands.
- `TODO(engine-fighters)`: the melee and archer paths under the engine, a later milestone.
- `TODO(engine-bestiary-awareness)`, `TODO(engine-bestiary-run)`: the awareness reader still answers the 74 pinned ranges
  or ACE's default, and the pace reader a ground profile's `pace`; the bestiary's record holds the world database's
  own range and Run skill beside them (E1d). Each reader moves in a slice of its own, with a `tools/fight-sim` note.
  The awareness reader's first step is in (E1f): the walker's creature feed reads both ranges
  (`Bestiary.AwarenessMetres`, `Bestiary.OwnAwarenessMetres`) and the session's `WalkAwareness` says which the walk
  keeps (`--walk-awareness`, default `own` since Q3: a range is only a cost on a route, never a wall, so the flip needed no walk to be re-proved; `pinned` stays an option). The sprint's live run reads
  the shadow's lines (the leg lines' `passed=` and `attackers=`, and the deaths and fight-backs on travel legs against
  the baseline); then the pins go (all 74 are rows of the table and state its value: the cattle's three since E1e). The shadow does
  not answer whether a route would have had another shape under the own range (that is a second plan); a creature
  disc is a cost and never fails a plan, so a route's verdict cannot change with the radius. The run-past rule reads
  no awareness range for its verdicts; the crossing's cost reads the ground profiles' `awareness_m`, which state the
  table's value for all 19 they state (`CreatureBehaviourTests`), so neither has a shadow.
- Closed by E1e, `TODO(engine-bestiary-cattle)`: the bestiary's tables took `weenie.type` 10 (Creature) only. They take
  type 15 (Cow) too now, 17 weenies with the Low B belt's three cattle among them: ACE builds a Cow as a Creature
  subclass that a generator places and a player can fight. The four other types ACE builds as a Creature subclass are
  left out, each with why in the generator's list (`tools/fight-sim/BestiaryRows.cs`, `AceIds.LeftOut`): Vendor (1,179
  weenies), CombatPet (259), Pet (29) and GamePiece (12). A type moves in by moving its number in that list.
- Closed by E1e, `TODO(engine-bestiary-heartbeat)`: a creature's three regeneration rates are vital points a heartbeat,
  and the behaviour table now holds the heartbeat's seconds (`heartbeat_interval`: the weenie's HeartbeatInterval, ACE's
  5 where it states none). The record gives each rate a second (`CreatureVitals.HealthPerSecond` and its two fellows;
  0 for a creature with no heartbeat). 139 of the 7,848 kinds state an interval that is not 5 s, from 1 s to 480 s. No
  caller reads the rates yet.
- `TODO(engine-bestiary-unknown-kind)`, `TODO(m6-creature-health)`: what the fight and the legs take of a weenie class the
  bestiary does not hold, or holds with no number, is named once a kind (a fight's first read; a leg's `LegKindRead`),
  never taken without a word (E1b in the fight, E1e outside it). With E1e every creature weenie of the tables has a
  maximum health and a Magic Defense, so the lines stand for a weenie that is no creature of the tables.
- `TODO(engine-sale-copies)`: every copy the stage named asks the verdict since E5c2 (the field's make-room rule, the trip trigger's sale estimate, the start-up audit's sale gate, the junk drop, the item mana drain's
  never-list). Still beside the verdict, in the order they go:
  - Closed by Q11: the town trip's old sale list (`SaleItemsBefore`, `IsKeptAside`, the pack-rule read `PackRule`), its guard in production (`SaleParityMode`, the `sale_parity` field, the `sale-parity:` error) and the tests' parity check. Five live trips agreed with no `differ` (the owner's gate, 2026-11-18). `SaleContext.OffersRefusedClasses` STAYS: dropping it would stop the trip offering a Bread or an Arrow, which ACE refuses and the second refusal drops (`NoteRefusal`), so Q11's own hard line (what the trip sells, drops and gives is unchanged) keeps it.
  - `ResupplyRun.SurplusDrinks` still asks `NotSellable` of a drink stack (the trip's own choice of the stacks it sells; the same four clauses as `SaleVerdict.Unsellable`).
  - `ResupplyRun.KeptBurden` (the load the wear pick reads as kept after a sale) asked the table's middle part alone (`PackRuleBook.Classify`), a seventh reader E5c2 found and did not move; Q10d moved it onto the verdict (`SaleVerdict.Of` over `ReadBook`), so an item the sale keeps (a noted one, a refused class, a kit stone, a rotation piece) is kept load too. It feeds only the wear pick's load stop.
  - Two limits of the verdict outside a trip, by name: a wearable not yet appraised reads as loot in the sale estimate (the trip appraises first); a drink stack the trip would split is surplus only by its whole stacks.
  Q11 took the `SaleDeciders` ratchet from 13 to 5: the verdict's one line, `SurplusDrinks`' `NotSellable` (its definition and its one call), and the fighters' two.
- Closed by E5e2, `TODO(engine-wear-differences)`: the six inputs the sites read differently are one answer each, built in one place (`S/Quartermaster/Wardrobe.cs`, whose summary names what each site read before). A site passes
  the ground, the SpellTable and, for the trip, where its score notes go.
- Closed by Q10c, `TODO(engine-wear-terms)` (below, the account of what it was): the terms are made by `Wardrobe.WithWearTerms` (`S/Quartermaster/Wardrobe.Terms.cs`); the trip asks it over its own read-only sale context and still publishes the judge; the worn restorer's wearable pick and the corpse plan's verdict ask `Wardrobe.JudgeWithTerms` (the session's sale context; the kept burden only on a ground with jumps, where it alone decides). A cantrip piece may now come off at those two sites, as it could at the trip, and only with the pick's reason in its line (`cantrips first: ...` or `beats what it displaces by more than the swap margin ...`); the guards of E5e2/E5e5 stand. The caster pick of the restore keeps its own judge. It was: a seventh difference E5e1's table did not hold. The wear pick's terms (`WearTerms`: her load, the rung's hitter and gate lines, the ground's Magic Defense) are made by the town trip alone
  (`ResupplyRun.WithWearTerms`) and published on the journey's facts (`JourneyFacts.WearJudge`, a side channel the `ContextSideChannels` ratchet counts); the loot step and the lore rule take that published judge. Until a trip has
  published one, and always in the worn restorer and the corpse plan's verdict, armour and jewellery are judged by the rule of before the wear pick (a useful cantrip of the list's names in a free slot; armour by `GearScore`).
  The terms move into the wardrobe in a slice of its own (they read the trip's kept burden and the pack layout); the guard above holds on both paths meanwhile.
- `TODO(engine-wear-bolt-chance)`: the caster score's bolt is the level the ladder's `plan_cast_chance` casts (`CasterTerms.OfLadder`) whatever the command line's `--buff-cast-chance`; the gear keeps read the fight's chance (E5e2).
- `TODO(engine-trip-floor-statements)`: the floor-only lines (D148) are stated to the session's counters by the job (the outer loop), and by a station or a fight where no job runs. Each still works them out from its own reading of
  the foci she holds: the outer loop by her name (the climber: War and Life), the fight from its kit, the stations from the pack as it stands. On every ground of the catalog the readings give one set (`TripDeskFloorDatTests`); a site
  whose set differs from the job's logs this name and the job's stands. One reading (the pack as it stands) for every site is a step of its own.
- `TODO(engine-trip-order-acts)`: the town trip acts on its own count at home, and that count is compared with the verdict the trip carries (E5d). Its plan is still the planner's: every line under its keep, the stop lines first, then the
  cheapest lift clear of a trip line, inside the trip's share of the purse; the line the trip was called for is not bought first, so on a short purse a trip called for one line can come back with that line still on its trip line. The
  check after says so by name (`WARNING: trip: check after: the verdict's reason still stands after the visit`). Q10 closed the order: the buy list's budget passes (`ResupplyPlanner.BuyList`, its `called` argument, the carried order's named line) lift the named line clear of its trip line before the cheapest-lift pass, after the stop lines, inside the same trip share; with no order carried, or a purse that covers every line, the list is as it was (the visit order is still the vendors' walk). Also open: a trip that is cut
  leaves its order in hand until the next logistics station starts (a town trip run alone before that would compare its count with it); the audit's sale gate and the trigger's sale estimate ask the sale's own verdict since E5c2.
- `TODO(engine-trip-schedule-rebuild)`: the schedule's mark of the last trip (D96) is the scenario's count of its own buff cycles, so a scenario rebuilt on the same ground (a hand-back) counts from its own start, as before E5d. Whether the
  schedule should survive a rebuild is not decided.
- Q4 (done, closes `TODO(m6-bolt-element-caster-mod)`): the fight's line for a target counts the caster in her hands (`S/Mage/CasterWar.cs`: `CasterWar.Held`, read in `CastElement.BestOn` and so in the ground's answer
  `CastElement.OnGround` the wardrobe reads). The factor is one (`WarFactor`): the caster's elemental bonus on its own element (with the enchantments on it and her Spirit Drinker), a quest weapon's resistance cleaving, and the
  mastery bonus a caster that is tough to master lacks; the caster score's war term reads the same parts. A plain caster, none, or one not yet appraised (`TODO(m6-bolt-element-caster-unread)`, named in the ground's answer) is x1 on every line: the choice of before. By the simulator (level 82,
  `matchup --wield`): the Stinging Chittick (fire 0.65, acid 1) takes the fire line hardest with the Atlan wand (Ilservian's Flame 249.4 over Acid Stream VI 138.0) and the acid line with a plain Orb (145.1 over 111.1); the
  Gotrok Lithos (fire 0.25, electric 1) takes the lightning line with either. Open: `TODO(m6-caster-line-pair)`: the wardrobe picks the caster for the line the fight casts with the caster she holds, so a pair the held
  caster does not lead to (another caster whose own element beats the held one's line) is not found; the best pair of line and caster a ground is still one question for a slice of its own. The travel legs' bolt
  (`Journey/WarBoltPick.cs`) reads no caster (the same TODO).
- `TODO(m6-ground-kind-counts)`: an indoor profile holds no count of its target kinds, so where a ground's kinds take different lines hardest each kind counts once in the element's share (the Crater Caves); an outdoor
  ground's spawn mass is read. `TODO(m6-caster-cleave-under-vuln)`: the caster score counts a resistance cleaving whole; a target already under a larger vulnerability of that element (a profile's opener) takes that
  instead. `TODO(m6-caster-damage-linear)`: the score's damage term is linear in the bonus, which overstates a factor as large as a cleaving's; the pick reads the order alone.
- E5e3 (done, a live defect the owner saw: the fire line cast holding a piercing sceptre while her fire wand sat in her pack): the wear judge's element is the fight's own choice on the ground's creatures from her book (`S/Mage/CastElement.cs`, `Wardrobe.ElementOn`); the rung's label and the plan's family answer only where that is not known; a pick that reads no element while the fight there has a line is an error line, once a session (`TODO(m6-caster-element-blind)`); the caster score's war term is the whole factor ACE multiplies by (the elemental bonus, a resistance cleaving, less the mastery bonus of a caster that is tough to master), checked with `tools/fight-sim.ps1 matchup --wield`.
- E5e4 (done, a live defect: the fight's loot step took five worn pieces off and on twice in a dungeon and lit nothing, and the town trip's rewear did the same with six at home; ACE answered each of those sixteen wields
  that a skill of hers is too low for the piece's magic: her Missile Defense nine times, her Melee Defense six, her Arcane Lore once, the gloves at home). The cause: loot armour may carry an activation limit on a skill (`LootGenerationFactory_Magic.cs:177-203` @95ef426d), an appraisal carries that
  skill as PropertyInt 176 (`AppraiseInfo.cs:410-411`), and the judge looked for it in PropertyDataId 37, which no appraisal carries, so every such limit read as met. It is read now (`SelfEquipJudge.ActivationUnmet`),
  for every reader of it: the re-wield, the town trip's rewear, the wear pick, the lore spend. The read of "are this piece's spells on her" is one (`S/Mage/GearLight.cs` over `WearScore.LitOn`): ACE writes a worn
  piece's spell into her registry on a layer of its own with the piece as caster even under a stronger spell of hers (`EnchantmentManager.cs:134-203`), so a surpassed piece reads lit, for the re-wield and the wear
  pick alike, and no wear decision changed with that. The re-wield's check before (`GearLight.Relight`): a piece comes off only when ACE would activate it at a wield now, she could wield it back, and at least one
  spell it casts on her has nothing stronger standing in its category; a piece left on says which case applied, once a piece and case a run. The check after counts lit, lit under a stronger one, and nothing left to
  light as done. A piece a swap cannot put back on is named in an error line (`gear-left-off`) and put on again at every corpse until it is read worn. Open, by name:
  `TODO(engine-relight-watch-line)`: the worn-mana watch still says of a piece that holds mana and burns none that the next home visit wears it again, which is no longer so for a piece ACE would refuse; and the town
  trip's rewear says after its wield that the spells are cast again without reading her registry (`ItemManaRefillStep.RewearSilent`).
- Closed by E5e5, `TODO(engine-great-cantrip-unlit)` (the owner's decision of 2026-11-18): a piece with a useful great cantrip she cannot activate is kept, never sold and never dropped, and it holds no worn slot against a
  piece whose magic works for her: she wears what works. The one question is two, each asked by name. "Is this a keeper" is `CantripRoom.IsUsefulGreatPiece`, unchanged, for every keep list (the sale, the make-room rule,
  the junk drop, the pack sorter, the villa's store list). "Does its great cantrip count for her on her body" is `S/Mage/GreatOnBody.cs` (`GearContext.GreatOn`): a keeper lit on her, or that ACE would light now, or
  that cannot light yet; E5e2's two guards and the pick's tie between two sides ask it of both pieces. "Never" is said only of a requirement on a skill she neither holds trained nor plans, or of another heritage
  (`GreatOnBody.NeverActivates`); her Arcane Lore, her level, a held or planned skill, an attribute, a vital and the item's mana are "not yet", kept worn or wanted as before (the lore spend's case). The answer is read at
  each ask. A worn "never" piece is judged on what it gives: the wear pick's terms already read a piece's spells only when they are on, and the rule of before the pick scores it on its armour with its spells off
  (`GearScore.Armour`) where an unmet activation scored it 0. What E5e5 left, by name: `TODO(engine-keeper-burden)`, closed by Q10d: the pick's burden term counted a piece taken off as sold unless it was attuned
  (`WearScore.Burden`), so a keeper taken off read as burden saved though it stays in her pack; `WearTerms.StaysInPack` (the trip's `StaysInPackWhenOff`, the verdict's keeps before the table) now says which stay; a worn piece's table rows (retained, value 0) are not asked. `TODO(engine-lore-never)`: a piece whose ItemDifficulty no rank of Arcane Lore can reach still reads "not yet"
  (the rank cap is the lore spend's read, `LoreSpend.Decide`; the judge holds no XpTable). Closed in E5e5's second commit, `TODO(engine-armour-rule-unmet-zero)`: `GearScore.Armour` scored
  any piece with an unmet requirement 0, so the rule of before the pick (the loot step's, for a corpse's finds worth a look, until a trip has published its pick) could take a worn piece behind a skill limit off for
  any find that scored above 0. An armour piece is scored on what it gives now: an unmet wield requirement still scores 0 (she cannot put it on), an unmet activation requirement leaves its spells off (no
  Impenetrability or body armour of its own, no cantrip counted; named in `GearScoreResult.SpellsOff`) and its armour level counted, and a piece lit on her keeps its spells. The same holds for a find: one whose
  spells cannot light is a candidate on its armour alone, as a plain piece of its armour is, where it scored 0; a find whose named cantrip cannot light is judged so first and kept for the review when its armour
  wins nothing (`SelfEquipJudge.JudgeWearable`). Who read the 0: the armour rule (both sides) and the attuned return pass (`GearReturn.Plan`, which now compares a held attuned piece and the worn pieces in its
  slot on their armour). The sale's `gear-loot`, the gear floor and the leather set read no score. The jewellery score is unchanged (its number is its cantrips, none with an unmet requirement).
  `TODO(engine-review-never)`: the pick keeps a piece for the review when its cantrips, were they on, would be worth more than the swap margin behind any unmet
  requirement, a "never" one too (`WearScore.Judge`), so a piece that is no keeper whose cantrip she can never light may be reviewed at every visit and never released to the sale.
- `TODO(engine-gear-return-unlit-guard)` (E5e6): the attuned return pass gives a held attuned piece back to its NPC, and a Give cannot be undone. Since E5e5 a worn piece whose activation she does not meet scores its
  armour, so it could mark a held attuned piece "outgrown"; before E5e5 it scored 0 and the same piece went back as "blocked" wherever that worn piece covered its parts. The guard (`GearReturn.Plan`, `gear-return-unlit`): when
  every worn piece that scores above the held piece has its spells off for her (`GearScoreResult.SpellsOff`), the piece is kept, neither outgrown nor blocked, and the trip says so once a piece a visit:
  `gear: return: '<attuned>' kept: the worn '<piece>' beats it only on armour, its magic is off for her (guard: gear-return-unlit)`; the pass's own `return: kept` line carries both scores, the requirement and the NPC and
  reward it would have gone to. A worn piece whose magic works for her, or that has none to be off, marks it outgrown as before, and a piece the guard kept is given by no later table of the same visit
  (`GearReturnPlan.Withheld`, `ResupplyStation.ReturnGuard.cs`). The guard stands until a live trip has shown its lines and the owner has seen what would have been given back. Not covered, and unchanged: a held attuned
  piece that scores at or above a worn piece with its spells off is still "blocked" by it and given back, as before E5e5.
- `TODO(engine-wield-sites)`: the wield sends still outside the wear service (E5e1; the `WieldSends` ratchet's rows): the recall home's rearm (`Fighter/RecallHome.cs`, tick-driven and shared with the fighters; its send is not one the ratchet's
  pattern counts), the fighters' recharge swap (`Fighter/GearRecharge.cs`, 2), the hunt's weapon swaps (`HuntScenario.cs`, 3) and the build scenarios (`GearScenario`, `FighterBuildScenario`, `MageBuildScenario`, 6).
  The tick-driven callers that take `Wardrobe.Send` keep their own wait and check after; they move onto the whole routine when their step can hold a coroutine.
- `TODO(engine-threat-flip)` (Q1, done for three: the vitals keeper's awake read, the fight's awake read and the harm-heal station's pre-check ask the board and their lanes `keeper-awake`, `fight-awake` and `heal-station` are gone; the pre-check keeps its watch for its log lines only; the board's `TiesToLast` keeps the later of two creatures at one distance, as their old scans did; Q2, done for four more: the outer loop's watch (`HostileWatch.InReach`), the travel leg's attackers present on a walk (`TravelLeg.Present`), the corpse leg's read (`DeathHandler.LegPresent`) and the retreat line's attackers (`AutopilotScenario.RetreatPresent`) ask the board with their own reach, window and count, and their lanes `outer-loop`, `travel-leg`, `death-corpse-leg` and `retreat` are gone; the one change of answer is an attack heard before the asker's watch was built, now known for its window; the watches stay for their other reads, so the `ThreatDetectors` ratchet is unchanged; Q2b, done for two more: the travel fight-back's pick (`TravelLeg.Attacker`, the skirmish's `NextAttacker` on a travel leg) and the travel lull test (`TravelLeg.OnHer`) ask the board with the leg's question, its skip and the later of two at one distance (`TiesToLast`), and their lanes `travel-fight-back` and `travel-leg-lull` are gone with the leg's shadow line; the same one change of answer; the leg's watch stays, so the ratchet is unchanged): the threat board is in shadow beside every detector the stage names (E2a: the outer loop's watch and
  the travel legs' attackers and lull; E2b: the twelve lanes of section 5's E2 row). Still open, in this order: a live gate that
  reads the shadow's lines and counts; then each detector asks the board in place of its own read, one a slice, the outer loop's
  watch first. A difference is expected, and is no fault, where: a watch was built after an attack (the board is the session's; a
  watch dies with its scenario, its leg or its pre-check); a detector has a parameter a board query has not (the pull rest's line
  test and hurt target; the death trip's path ahead, measured to the path and not to her; the quest steps' two reaches, 15 m for
  a bite and 40 m for a spell line, and the shot rule's drops; the buff refresh's window, which runs from the decision that saw
  the hit and hears a damage notice only); a detector keeps a creature by its guid where the notices give the board a name (the
  fight's self-defence list: one creature a notice); a detector counts what the board does not (the guard counts the travel
  fight-back's target unheard, and a creature that may not be attacked). Not shadowed, by name: the fight's second watch (the
  target switch's `HostileWatch` of reach 0, read for a kind's hits a second: a rate, no question of who is near), the
  guard's incoming rate and its pick of the fullest creature (E2d: its comparison is the one function, `FullestPick`; its health read is the one picture's since Q5), the recharge station's two awake reads and the rest blocker's rule (awake reads of the
  keeper's kind, found by E2b), the travel legs' two other reads of the attackers present (the walk's line and the fight-back's
  end: the question the `travel-leg` lane compares), and the melee and archer paths. Each move lowers its file's ceiling in the
  `ThreatDetectors` ratchet. The three of those reads that took a creature where the server last reported it now take it where it is drawn, as
  the board does (E2b: the vitals keeper's awake read, the quest steps' nearest attacker, the death handler's path read; with
  them the rest blocker's rule behind the buff pass's lie-down and the pass's place, the quest kill's range test, and the
  count of attackers in the survival layer's leave line), so a move onto the board no longer changes their answer by the
  record read. Still on the server's report, by name: the quest kill's pick of its step's creature and the place its walk
  aims at (`QuestStepLeg.NearestLiving`, `WalkTo`), the walker's arrival test at an object (`JourneyTravel`), the tour's
  seen target (`GroundTour.SeenTarget`: a cell of the ground, read from the report) and the melee and archer paths.
- `TODO(engine-health-flip)`: the one picture of a target's health is in shadow beside both models (E2d). Q5 (2026-11-18): the vitals guard's three reads
  (its drain pick, the losing race's attackers, a killing blow's health before) and the travel legs' two (the fight-back's published target, the skirmish's life
  strike, so also the quest steps' and the harm-heal station's life strike) ask the picture (`Vitals/PictureHealth.cs`), their five lanes are gone, and
  `KnownTargetHealth` carries the facts and answers only where the picture holds no maximum; each reader keeps its rounding, so on a state with none of the
  differences below the answer is the old one to the point (`PictureHealthTests`). Still open: the fight last (its model also carries the
  drain yield gate, the freshness rule and the health query, which are no part of a health and stay the fight's until E3). The ways the two models differ, each
  pinned as a state in `HealthModelsDifferTests`, so a difference there is expected and is no fault: whose line it is (the fight gives a damage line to the
  nearest living creature of its name; the guard to the fight-back's target alone, and its own trackings count no line); when the server's word holds her
  damage (the fight: a line within 0.3 s after it; the guard: all she dealt before its first read of it, and an equal share sent again is no new word to it);
  what dies with its owner (a fight's model, a leg's tracking, the guard's own with its scenario); the maximum (the fight: its ground's row, one creature's
  appraisal for its whole class, the bestiary; the guard: its ground's row, the facts; neither takes a creature's own appraisal for that creature alone, though
  the server's share is of the creature's own maximum, `Player_Vitals.cs:171` and `WorldObject.cs:610-618` @95ef426d); regeneration (neither counts it:
  `TODO(m6-creature-regen)`; the picture does); what a creature never told reads (the fight: unknown, which its readers take as unhurt; the guard: its
  maximum); the least a standing creature reads (the fight 0, the guard 1). Two reads stood on neither model: the travel legs' life strike (the object
  table's share of the bestiary's maximum, none of her damage counted; the picture's since Q5) and the quest steps' (`QuestStepLeg.HealthPoints`:
  the same shape over the facts' maximum; no lane yet, still its own).
- `TODO(engine-health-attribution)`: a damage line names its creature by name alone. Q5 decided it for the casters that know their target: the skirmish
  (every host: the travel leg, the quest steps, the harm-heal station) tells the picture the creature its cast is at (`HealthPicture.Aim`, released after the
  bolt's wait, lapsing after `AimHoldSeconds`, or at that creature's death), and a line naming that creature's name while the aim holds is its own; else the
  picture gives it to the nearest living creature of that name where the client draws it (the fight's rule). Still open: the fight's bolts tell no aim (its
  several bolts in flight need each bolt's flight, the fight's in-flight list), so two creatures of one name near her in a fight can still trade damage until
  the server's next word on either; decided with the fight's flip.
- `TODO(engine-fullest-gather)`: the fullest pick's comparison is one (E2d); who is offered to it is still each site's own loop, because at each site the
  reach and the exclusions gate a side effect that must not move while the site decides: the fight's emergency and drain rule count the kinds that let no
  drain through inside the cast's range, read each candidate's health (which seeds the fight's model) and test the line best first, the next best when it is
  blocked, over at most its cap of candidates; the mana loop's stamina source asks each candidate's appraisal inside the range (a send) and tests the line as
  it meets a creature that beats the best so far; the vitals guard offers the ground's targets, the fight-back's target and every creature heard attacking
  her, and makes a tracking of each inside the watch's reach at first sight; the ring gathers what the ring will hit. The candidate sets differ too: a
  creature heard attacking her that is off the ground's list is the guard's candidate and the emergency's only as the fight's own target; a creature set
  aside for stamina drains is still the emergency's. One gather with the reach, the exclusions and the line order as parameters comes with E3 (the emergency pick under the survival
  layer), once the health each offers is the one picture's. The travel legs' dance under fire holds no pick of a fullest creature (its attacker is the
  fight-back's pick, the `travel-fight-back` lane of E2b).
  E3.0: the board lists who counts for a question (`ThreatBoard.Gather`), and the layer's two picks are over that list (`SurvivalRule.FullestHealth`, `FullestStamina`); no site reads it to decide yet.
- `TODO(engine-threat-lull)`: a lull has one definition on the board (`ThreatBoard.Lull`: no hit from anyone inside the window, the
  last one named). The lull tests of today are each a detector's own answer: the outer loop's stop and trip wait for no hit
  within 10 s and no target creature within 20 m, heard only since the scenario was built; the fight's buff refresh for no
  damage notice (a creature's spell is none to it) within 10 s of the decision that saw it and no target within the refresh's
  reach; the travel legs for no creature heard attacking, landed or not, within 10 s that stands alive within 40 m. They are not
  changed in E2a; which of them take the one definition is decided with the flip.
- `TODO(engine-headless-climb-live)`: the headless level up run (E0b: `HeadlessClimbScenario` under `--scenario=autopilot
  --autopilot-job=level-up`) is proved over the tests' scripted world only. Its real parts (the keeper, the runner host,
  the catalog's stations for each rung's ground) have never run against a server; it enters the ladder's character, so
  it is never run beside a live climb on that character.
- `TODO(engine-headless-menu-input)`: the headless climb gives the dial no menu input, so a quest task is judged by the
  scenario's own checks alone.
- `TODO(engine-climb-replay)`: the offline climb's stations are scripted (no walk between the grounds, no real fight,
  pass or trip). A recorded climb replayed through real stations is what the later stages' shadows need; E0b gives
  them the seam and the headless dial, not the recording.
- Closed by E5c2, `TODO(engine-perf-trip-shadow)`: the trip trigger's sale shadow went with the estimate's own list. The estimate is the rule book's answer now, so what the shadow cost beside it is the estimate's own cost
  where it is worked out afresh (a stamp changed, with a want the coin cannot pay): 435 microseconds and 163,264 bytes, and 0.13 microseconds and nothing allocated on a check that reads its cache (section 7).
- `TODO(engine-perf-room-verdict)`: the make-room rule's sell loot is the rule book's verdict since E5c2, and it decides the corpse plan, so it is asked on the plan's frame: 439 microseconds and 193,656 bytes for the room
  read of a pack of 100, where Eperf had brought that frame to 178 microseconds by asking the report-only shadow on the frames after (section 7). The shadow could wait because it decided nothing; the decision cannot. If
  the frame matters at two plans a minute, the part to keep between plans is the context (its scroll rule, her book's burns), which costs most of it; not done, and no decision would move.
- `TODO(engine-perf-detector-walks)`: each detector's own read still walks the whole object table (the outer loop's watch: 5 microseconds a frame in section 7's room; its `Living` is one walk for every hit of its ring
  when the guard reads the living's share). They go as each detector asks the board (`TODO(engine-threat-flip)`), which walks the creatures only.
- `TODO(engine-perf-outer-step)`: the outer loop's whole step is not measured (no rig drives `AutopilotScenario` through a real fight offline: `TODO(engine-climb-replay)`), nor are the host's cost of a log line, the
  bestiary's first use (once a process) and the heap its records hold. The soak line's `frames=` and `frame_mean_ms=` (section 7) are the live measure of the whole.

## 7. What it costs a frame (Eperf, 2026-11-18)

The live evidence was the soak line's worst frame of each minute, whose median over a run read 33.5 ms before item 16 and 43 to 48 ms with it, over three runs of 217, 36 and 29 minutes. Each suspect was measured
offline: `EngineFrameCostTests` (`Tier=Timing`; run it alone with `--filter "FullyQualifiedName~EngineFrameCost" --logger "console;verbosity=detailed"` and read the tables in its output). One call of each on a state like
the climber's in a dungeon: a room of 237 objects with 30 creatures, 5 heard attacking her, bodies drawn; a main pack of 100 items with the SpellTable's spells and a book of 600; and every step of a scripted fight timed
alone. A Debug build, as the game runs. Medians of three runs; quiet runs agreed within 5 percent, and runs beside another build read up to 2.4 times as much on the rows that allocate.

| What | Before | After | How often |
| --- | --- | --- | --- |
| All thirteen threat lanes asked in one frame | 80.5 us, 88 bytes | 62.4 us, 88 bytes | a frame at most (each lane once a frame, at its owner's ask) |
| The outer loop's watch with its lane, a quiet room (the watch alone: 5.0 us) | 10.0 us | 8.0 us | a frame |
| The frame that plans a corpse: the room read (173 us, 99,896 bytes before item 16) | 561 us, 262,320 bytes | 178 us, 99,896 bytes | a corpse plan |
| The frames after a corpse plan: the sale shadow's asks | none | 80, 257, then 42 to 63 us twice; 162,672 bytes in all | four frames a corpse plan |
| The trip trigger's sale estimate worked out afresh, with its shadow (17 us, 0 bytes without) | 494 us, 162,416 bytes | unchanged | a pack change seen by the counters, on two grounds |
| Since E5c2 (the make-room rule and the estimate ask the verdict; the shadow is gone): the frame that plans a corpse | | 439 us, 193,656 bytes (the gear keeps 77 us, the sell loot by the verdict 341 us); no frame after it | a corpse plan |
| Since E5c2: the trip trigger's sale estimate worked out afresh; read from its cache | | 435 us, 163,264 bytes; 0.13 us, 0 bytes | a stamp change with a want the coin cannot pay; a check a second |
| The walker's creature feed, one fill of 30 creatures (the awareness shadow: 0.09 us a creature) | 11 us | unchanged | every 0.5 s of a walk |
| One bestiary read; one live position read | 0.03 us; 0.14 us | unchanged | at a reader's ask |
| A decision of the fight (the mean rolled cost of a cast, 319 us a call, asked for every candidate) | 4.6 to 5.2 ms | not among the slowest steps; a mean asked again 0.02 us, and nothing allocated kept or worked out | a cast |
| A whole step of the scripted fight: median, 99th percentile | 19.6 us, 2,289 us | 14 to 19 us, 109 to 130 us | a frame (the scripted world answers a cast in 0.02 s, so its fight decides far more often than a live one) |

What was changed, each with the same measurement before and after and no decision moved:

- **The mean rolled cost of a cast is worked out once** (`ManaConversionModel.ExpectedCost`). Not item 16's: it was the measurement's largest number, every decision's frame. The mean is a pure function of the base mana, the
  power and the skill and is kept by them, in two arrays made with the model, so it allocates nothing; the scripted fight logs the same 9,692 lines before and after.
- **The fight's sale shadow asked later** (`SaleShadow.Later`): the plan's frame handed the copy's answers over and the rule book was asked on the frames after, 25 items a frame; the same items, lines and counts.
  E5c2 removed the shadow with the copy it stood beside (the make-room rule asks the verdict itself), and its later asks with it; the two rows "since E5c2" are the same measurement on what stands, medians of three
  runs alone at lowered priority (`TODO(engine-perf-room-verdict)`).
- **The threat board walks the table's creatures**, a list drawn again when the table says its objects changed (`WorldObjectTable.Version`). A creature's place is still read at every ask: a drawn body moves with no event
  the board hears, so no key could rule a stale place out.
- **Nothing was stated every step that needs stating on change only**: the wardrobe's cast chance is stated once a session's read model (one reference compare a step), the shadows are made once a scenario.

What the measurement says of the live number: item 16's code adds under 0.1 ms to a frame and added under 0.4 ms to the frame that plans a corpse in this room and pack; a worst frame 10 ms longer is not in it. The three
runs differ in length and in what ran beside them, and their one number is each minute's single worst frame. The soak line now ends with `frames=` and `frame_mean_ms=` (the minute's frame count and mean frame, exact;
`frame_p99_ms`, `over_50`, `gen0` to `gen2` and `gc_pause_ms` were on it already), so the next two runs are compared by the mean and the 99th percentile over equal stretches of the same activity.

### The long frame of a minute (Eperf2, 2026-11-18)

The same number read worse on the next live run: the median of each minute's worst frame was 33.5 ms before item 16, 42.8 ms on the run with E1d to E2a and 70.1 ms on the run that added E2b and E5e1, with the 99th
percentile, the collections and the collector's pause unchanged. The three logs were read for what the worst minutes hold (the soak lines, the slow-frame lines, and every line stamped inside a slow frame's interval).

| Stretch, minutes inside the dungeon | Minutes | Median of the minute's worst frame | Frames over 50 ms a minute | Minutes with none |
| --- | --- | --- | --- | --- |
| Before item 16 | 177 | 30.6 ms | 0.76 | 71 % |
| E1d to E2a | 23 | 33.8 ms | 0.61 | 73 % |
| Plus E2b and E5e1, before its town trip | 50 | 44.1 ms | 0.82 | 56 % |
| The same run and build, after the trip (00:59 to 01:30 UTC) | 32 | 119.2 ms | 3.78 | 15 % |

What the logs say:

- **The growth is one stretch of one run.** The third run's first 50 minutes are the first two runs' rate of long frames; its last 32 minutes, the same process and build, have four and a half times as many.
- **The long frames were waited, not worked.** Of the 127 slow-frame lines of that stretch, 107 had a process CPU time of at most 35 percent of the frame (every thread's; often 0 or one 16 ms step in a frame of 150 to
  750 ms) and none had 80 percent or more. No slow frame of the three runs held a collection pause of note or a pipeline compile, and the viewport's measured render time was a few milliseconds in each.
- **The wait is at no one place.** 37 of the 127 have no log line inside the frame. Where a frame holds a burst, its lines are 10 to 25 ms apart where the same burst is written inside 2 ms on a quiet frame.
- **One place is known, and it is the report's own.** Over the three logs whole, a slow frame whose wait was inside the player's own step (309 ms once, with nothing logged in it) was always the frame right after
  another slow frame: 21 of 33 such frames on the first run, 1 of 3 on the second, 9 of 34 on the third, and none of the 396 slow frames that did not follow one. That step begins by writing the slow-frame line, a
  warning, which flushes the log file; the frame after a long one also steps the bodies through the gap. Which of the two waits is what `log_ms` and `player_ms` on the new line say.
- **No line predicts it.** Of every candidate (a cycle line, a corpse plan, a `threat-shadow:` or `sale-shadow:` line, a re-wield pass, an appraisal, a rest, a decision, a respawn, a cull, the panel) at least 92
  percent of the occurrences fall outside every slow frame. The minutes after the trip with no long frame have the same kills and log volume as the ones with ten.
- **The server waited too.** A health ask answered outside every slow frame of the client took over 60 ms 0.3 percent of the time on the first run, 1.6 percent before the third run's trip and 3.2 percent after it
  (the 99th percentile 36, 85 and 169 ms), and the server is another process with none of this code.
- **The town trip's long frames are worked, and known**: a leg's first plan (576 ms once, between two lines of the journey runner) and the station's stock plan (241 ms, 135 lines in one frame). They are not this
  stretch and not item 16's.

So no one cause in the game explains 33 to 70, and none in item 16: the stretch is the game thread, and the server beside it, not being run. The logs cannot say by what (the builds and tests of the slices at work
on the same machine through those 32 minutes are the candidate; the logs hold no measure of the machine, and one short build of this slice's fell in quiet seconds of the run's last minutes, so one build alone
does not do it). The host now says it: a frame over 50 ms writes `[play] long frame ...` ([session.md](session.md) section 44) with the time of each of the host's top-level calls, the draw, the engine after it
and the rest, the logger's share, the game thread's own CPU time and the machine's busy share. On the next run a long frame whose `main_cpu_ms` is small beside a `machine_busy_pct` near 100 is the machine's; one
with the machine idle is a wait of the game's own, and its phase names where. The slice's offline run of the play checks, beside other work on the machine, already wrote one of the first kind: a frame of
73.7 ms with `main_cpu_ms=0 process_cpu_ms=0 machine_busy_pct=100` and 70.2 ms outside every timed call and the draw.
