# Handoff: M6 item 7, slice BB

## Done
- The fight with a hostile on her or in reach (in the ground) starts at once: `AutopilotScenario.BeginRun` calls `FightBackNow`, which logs `autopilot fight-back: a hostile is on her or in reach (...)`, skips the keeper's vitals pass (`StartPhases(skipKeeper)`) and replaces the fight's needs with only the den (no health, mana or buff need). The fight's own emergency handles health and mana.
- The corpse trip: `CorpseCoinRule.DecideTrip` (in `CorpseCoinRule.cs`) with `CorpseTripAsk`. A corpse is walked to only if it holds something she needs (worn gear missing, caster out of hands, a healing kit in ACE's line, an item at or over `corpse_coin.min_item_value`, or an unread diff) AND its cell is safe by `corpse_fetch` AND the gate is open. Otherwise `DeathHandler.CorpseRunShut` leaves the corpse by name (`corpse_recovered=no`, `trip_abort=cell-unsafe` or `none-needed`) and goes on. Logged as `handler: corpse trip: ...`.
- Data: `min_item_value` (20000) added to `ladder/war-mage.json` `corpse_coin`, read in `LadderLoader`, held in `LadderCorpseCoin.MinItemValue`.

## Gotchas
- ACE's drop line names items but not their Value, so the item-value test cannot fire yet: `TODO(corpse-item-value)` is logged each time a corpse is left. No quest item is known: `TODO(corpse-quest-item)`.
- Only the ladder's character (Winnie, or `CorpseLadder` in options) goes through this; other characters walk as before.
- The new fight-back need override means a fight started under fire has no `Buffed` need either.

## Tests
- 11 new: `CorpseCoinRuleTests` (8-row theory, shut-gate and data-line fact) and `AutopilotFullAuditTests` (fight starts under fire with unmet needs).
- Count: baseline 13,926 + 11 (13,937).

## Open TODO names
`corpse-item-value`, `corpse-quest-item`.
