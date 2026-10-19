# Polish pass between M3 and M4

**Status: done inside M3 item 4.2 (D39-D43), 2026-10-19.** The owner asked for a polish pass after M3 closes and before
M4 ([m4-fights-and-loot.md](m4-fights-and-loot.md)) starts; the owner's D39 put it inside M3's item 4.2 (the end), and
its five items were built there as 4.2 slices p1 to p5 (the what and the where are in §1b). Every row below is the
owner's ask; the "Detail to settle" column is what was decided or measured, and the decisions are in m3-plan.md §7.

## 1. The items

| # | Area | What the owner asked for | Detail to settle |
|---|---|---|---|
| P1 | UI: cast bar | Bind spells to the cast bar automatically for the mage test character (BSD Mage). **Tab I, buffs:** Focus, Willpower and Creature Enchantment at level VI, then the level VII spells laid out in order, everything the player needs for BSD. **Tab II, fighting:** Fire Vulnerability, the level VII fire spell that kills the monsters, Revitalize, Stamina to Mana and Heal Self. | Which VII spells "everything for BSD" means (the buff cycle's list in M3 §4.5 is the starting point); the order; whether the bind is done by the existing gear/buff scenario or by a client rule; keep it limited to the test character. |
| P2 | UI: target | On entry there is a hole in the UI where the target goes. The owner believes retail showed a blank target panel there. | Confirm retail's empty-target look from the dats and the 11.6096 docs before drawing anything (a placeholder is labelled as one). |
| P3 | Client rules: stances | The client enforces combat and peace stances, and enforces what you cannot do in combat. | Name the rule set from retail and ACE (which actions refuse in combat mode: casting needs magic mode, item and inventory moves, logging off, and so on); where the refusal comes from (client check, server answer, or both). Every unknown is a named, logged TODO (CLAUDE.md, Quality). |
| P4 | Tests: the wall shots | A sequence fires at tuskers but only hits the wall. It is embarrassing. | Find why the aim or the target is wrong in that sequence (the cast, the target pick, or the den's geometry) and fix it so the frame shows a hit on the creature. |
| P5 | Tests: slimming | There are very many tests; drop redundancies. The testing improvements planned as M4 DoD 1 come here. | Measure first (M4 §5.1: stage clock, timed sink, per-test durations), then cut what the owner approves, each cut with a before-and-after of the same step list. The M3 3.1/3.2 review's should-fix findings (M4 §3.3) are a natural pairing. Three candidates from OpenAC's test process, each the owner's to accept or decline here (docs/research/foundation-openac.md §6.5): a `Tier=Timing` trait for tests whose outcome depends on load, a `Status=KnownFailure` trait for tests written against an open TODO row, and `dotnet restore --locked-mode` in the smoke's build step so a restore that disagrees with the lock files fails instead of rewriting them. |

## 1b. What was built (M3 4.2)

- **P1, the cast bar (4.2 p1; docs/session.md, the action list; m3-plan §9 bar rows).** BSD Mage only. Tab I (ACE bar 0):
  Focus, Willpower and Creature Enchantment Mastery at VI, then the 24 buff lines at VII in the buff cycle's order. Tab II
  (bar 1): Fire Vulnerability Other VII, Flame Bolt VII, Revitalize Self VII, Stamina to Mana Self VII and Heal Self VII.
  Bound by the gear stage of the death loop (`GameAction` 0x01E4 `RemoveSpellFavorite` added; the client updates its bars at each
  add or remove, as retail's player module does). The spellbook's first paint is fixed in 4.2 x4 (live check pending).
- **P2, the target panel (4.2 p2; docs/retail-ui.md §21.7).** With nothing selected the field stays visible and shows its
  base image, the blank target panel, and the stack box and slider are hidden. Auto-target when attacked in melee or
  missile mode (`ClientCombatSystem::AutoTarget` 11.6096 0x0056C9D0, `PlayerModule::AutoTarget` 0x005D3E90): the attacker by
  name, `TODO(m3-autotarget-attacker-id)` for the id that is not decoded.
- **P3, the stance rules (4.2 p3; docs/retail-ui.md §23.8, docs/session.md §7).** The toolbar refuses a combat mode the
  wielded items do not allow (retail `SetCombatMode` 0x0056CB80: "You can't enter <mode> mode while wielding the <item>")
  and refuses combat mode in portal space. A cast outside magic mode is refused at once, nothing sent (retail's client
  makes no such check; ACE would answer with a silent UseDone): `--retail-cast-any-mode` restores retail. A double click on
  a held item in combat is refused ("Cannot hold <name> while in combat"). Named and not coded: pending mode requests,
  the exact portal-space flag, and the mode words.
- **P4, the wall shots (4.2 p4; docs/interiors.md, the fight loop).** The bolt goes at the nearest tusker with a clear line
  (`IFightWalk.HasClearLine`, a 0.3 m sphere sweep from 2/3 of a 1.8 m body); none clear walks at once; a bolt that stops
  short of its target is counted as a wall hit (`wall_hits=`, report-only beside `misses=`). `--fight-ignore-line` restores
  the old pick. The cause is the arrival cell and the tuskers' cell being 44 m apart through rock: ACE and the client check no
  line, so the fix is the loop's judgement, not a retail glitch. The frames show a hit on the tusker (`-first`, `-bolt`, `-hit`).
- **P5, the testing loop (4.2 p5a and p5b; docs/session.md §32, README.md, tests/Shared/README.md).** Measured first, then
  cut: the default filter excludes `Tier=Exhaustive` (76 tests, the dat sweeps; `slice-check -Exhaustive` runs them), the
  script-side cuts (slice-check no longer runs `dotnet test` twice; frame checks run solo under `-Backend require`), the
  test-side cuts (the Exhaustive sweeps about 25% faster; `IncomingDamageLiveTests` folded into `DeathLiveTests`; the eight
  mod-verb loopback logins become three; the idle test 30 s, not 45 s). Before and after: the loopback suite 36 tests and
  22.1 min to 31 tests and 20.7 min; the full smoke 27.1 min to 19.2 min (the step's test time 25 s, 7,967 passed). The
  default suite is 7,965 tests at G-A, plus 76 Exhaustive.
- **p6 and p7 (4.2 slices outside the owner's five).** p6 dresses the stand-in's humans (a placeholder garment set, named
  `TODO(m3-standin-human-look)`) and makes the texture-override fallback retail's rule (fix A). p7 gives BSD Mage its
  classic armour kit and The Healer's Heart, with the wand, the orb and the foci formula (docs/avatars.md, the gear kit).

## 2. Effect on M4

- M4 DoD 1 (the testing loop measured and cut; the 3.1/3.2 debt paid) is done here as P5 (D41). M4 §6.1 Q5 (which testing
  cuts the owner approves) is answered here (D41): the cuts listed in §1b.
- The M3 plan's own open TODOs are placed in m3-plan.md §9: M4 where the goal names them, none (a guard) otherwise.

## 3. Not decided

The order of the five items, the budget, the slice sizes and whether this is one item or two. The fighter character
and the other M4 questions stay M4's (m4-fights-and-loot.md §6.1).
