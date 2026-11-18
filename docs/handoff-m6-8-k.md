# Handoff: M6 8 slice K, the survival layer

## Done

- `Session/Autopilot/SurvivalRule.cs`: the one decision a frame, pure: go, wait or leave. Its input already carries "in a fight" and the attackers; its result can already say heal, take mana (with a drain target), leave or go. Nothing in the fight calls it yet.
- `Session/Autopilot/AutopilotScenario.Survival.cs`: `SurvivalFrame`, called from `RunStep` in every state but the fight. Wait runs the existing keeper to the back lines; leave ends the station by name and takes the retreat's way home.
- Walks: `IJourneyTravel.HoldWalk` / `ReleaseWalk` over the leg's `Pause` / `Resume`; every walk starts at that seam.
- Stations: `IStationCommitted` (a portal's Use in flight), `IStationHeld` (`ScriptLeg` waits and the corpse trip's clocks do not count a hold).
- Ladder data: `survival` in `war-mage.json`, `LadderSurvival`.
- A ladder errand starts with the ladder's needs; with no enchantment up it is asked again at the next way home.
- `[ladder]`: `survival_holds=`, `survival_leaves=`.

## Next

- Live read of the layer in the next round (see the doc notes): the lines are first numbers.
- The fight comes under the layer later, in stages (TODO(m6-survival-fight)).

## Gotchas

- `SurvivalOn` does not need a rung: the start audit's trip runs before the first rung is read.
- The layer's wait is off for Fight, Buff and Upkeep (`SurvivalRule.KeepsItsOwnBars`); the guard's emergency heal still runs in Buff and Upkeep.
- Slice G's fight-back is a different path on purpose: a held station is not stepped, so it could not read G's `stopWhen`.
- A hand-written elapsed check in a station still counts a hold: TODO(m6-survival-kill-clock), in the kill loop slice L owns.
- The leave is read before the guard, because the guard waits for the lanes to be free and an action in flight must not keep her among attackers. It is not read under the guard's own cast.

## Tests

`SurvivalLayerTests` (31 cases), in the client test project. Default count 14,323 (floor 14,292 + 31). No loopback test added.

## Open TODO names

TODO(m6-survival-fight), TODO(m6-survival-kill-clock), TODO(m6-survival-lines-live).
