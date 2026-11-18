# Handoff, M6 8 H: fights feed her mana from the enemy

Branch `m6/8-h`, from m6/8 at 5a8a744e. Sums first: `cache/m6/8/drain-sums.md` (conclusion at its top).

## Done

- `ManaLoopRule.DecideBySum` (pure), for the ladder's climber only: the bolt first when the target dies to it, at more than 2 attackers, or until 2 bolts have gone since the last press, unless the bolt cannot be paid; at half a stamina bar or more a press that nets half a bolt, with the overfill counted; under it, the drain from the creature that gives the most, if the drain and the press after it net a whole bolt, more than the press alone, and she can pay both.
- Ladder values in `vitals`: `mana_press_min_bolts`, `stamina_drain_min_bolts`, `mana_loop_max_attackers`, `mana_loop_bolt_first_hits`, `mana_loop_bolts_between`, with their source sentence.
- `FightScenario.ManaLoopBySum`: reads the appraisal's stamina, the press by the sum and the press after the drain; one line before and one line after each cast with the amounts; a none line by reason, once per reason.
- End line: `loop_presses= loop_drains= mana_end= mana_end_pct=` (`drains=` is Drain Health's already).
- The fixed rule (`ManaLoopRule.Decide`, D95) is unchanged for every other character; its tests stay as they were.

## Sums in one line

The drain never pays on the outskirts (-4 to +7 for a pair; she has too small a bar). It pays from the Burrow up (+14 at L16, +26 to +30 at L18-22). On the outskirts her own stamina press, regularly, is the lever: +6 to +19 a press.

## Tests

- `Session/Vitals/RechargeRulesTests`: 18 new (table, bolt-first cases, overfill, the data).
- `Session/Mage/FightManaLoopBySumTests`: 5 new fight-rig tests: the drain casts when the sum says so, with amounts logged; the press after it; a target too small is not drained; the end line's fields; another character keeps today's rule.
- The test count is 14,234 (the floor file still reads 14,211: the lead sets it from the merged branch).

## Next

- A live round reads it: `m6-drain-stamina-live` stays open until `mana loop: drain by the sum` and `mana loop: drain landed` appear at the Burrow or Lodge, with the amounts as the sums say, and `mana_end_pct=` reads on the end line.
- Level 30 and up in the sums are projections: read the bar and Life Magic when a round gets there.

## Gotchas

- A drain has 10 m of range at level I, so it only happens when a creature is close; the reading of the target's stamina is the appraisal's and is 5 seconds old at most.
- The first decision in reach only asks the appraisal and passes; the drain comes at the next decision.
- No loopback tests added.
