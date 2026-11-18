# M6 item 6, slice FXM2: the menu review's seed, probe, retreat and task-judging fixes

Branch m6/6-fxm2 (from m6/6 at 5eb34ac8). Fixes 6-M2, 6-M6, 6-M7, 6-M8, 6-M9, 6-M13, 6-M15 of the menu review.

## Done
- 6-M2: `AutopilotSeed` gains `TaskName` (append-only). `ApplySeed` applies `TaskDone` only when the name matches (a seed with no name keeps the old behaviour) and drops `PendingTrip` for a quest task that is not the seed's task; logs the change.
- 6-M7: `AutopilotSeed.TripCutWork` appended; `Seed` writes it, `ApplySeed` restores `_tripCutWork` with the pending trip.
- 6-M6: `_orphanWhere = null` in `ArmOrphanProbe` and in `Dropped`.
- 6-M15: a scenario built with a seed labels its orphan probe `hand-back` (`ArmOrphanProbe` takes an optional label).
- 6-M8: `HealRetreat` times its window from its own first ask (`_healRetreatAskedAt`), not `_phaseStart`.
- 6-M9: `JobMenu.Judge` is internal and shared. `AutopilotHostLogic.UseTask` (chat, menu) and `Start` (the `--autopilot-task` option) refuse a quest with its names (`ladder-quest`, `no-level`, `level-out-of-band`, `done-once`); `not-due` stays the scenario's.
- 6-M13: the audit's "a trip that was cut" line skips the quest task's own trip (`_questTripActive`).

## For the lead (wiring left)
- `AutopilotHostLogic.MenuInput` (`Func<JobMenuInput?>`) is null by default, so nothing is judged until the play host sets it (level from the state, the character's `QuestTimers`, the quest catalog, the ladder's quest keys; null while the level is unread). `AutopilotHost.cs` is not in this slice's files: set it where the host builds the logic (MD2's source can share the same input).

## Tests
Default count: 12765 passed (12757 before this slice + 8 new), floor 12566. New: `AutopilotFxm2Tests` (5: M2 errand-then-quest, M2 pending trip, M7, M9 chat, M9 option), `AutopilotOrphanTests` (2: M6, M15), `AutopilotRetreatTests` (1: M8).
Gotcha: the M8 test passes on the old code too: the rig answers the peace switch at once, so the old window (from the Peace phase start) was never short here. It proves the path (retreat, not the preconditions failure), not the window start; staging a slow peace switch needs a rig change.

## Open
No new TODO names.
