# Handoff: M6 item 6, fix slice FXD1 (the review's death fixes)

Branch `m6/6-fxd1`, cut from m6/6 at 120ade0a. Doc notes: `cache/m6/6/doc-notes-fxd1.md` (outside the repository).

## Done

- 6-D1 (blocker): the death handler's heal reads the spellbook before the cast. A read book gives the highest Heal Self level (the keeper's line, `VitalsMeans.HealSelf`) it knows, the SpellTable holds and the pack pays for; none: the heal is dropped with no cast, named. An unread book is not judged (Heal Self VII, logged once). Every end but Landed, Unproven, Fizzled, Busy and ManaShort (`DeathHandler.HealEndKeeps`) drops the heal for the trip after one cast, named with the end. The drop line is `Heal Self dropped for the trip: <why>`. No other unknown-spell recast in `DeathHandler`/`DeathScenario*` (the recall is one cast; `ManaDance` picks from the read book).
- 6-D3: `route=leg` adds the corpse leg's straight-line metres over `LegCorpseMinMetresPerSecond` (the wayfinder's 4.8 m/s over its 1.5 route factor) to the abort limit, logged once; the abort text names the place.
- 6-D5: the bind station records `JourneyFacts.BoundAt` at the Use that attuned; the respawn classifies against it. The Holtburg comparison and the `LifestoneTied` arm are gone.
- 6-D6: before the fight, a mage outside the ground goes `rung-back` (journey travel only; another seam warns `TODO(m6-death-fight-outside-ground)` and fights as before, since the handler never places). `rung-back`'s check after waits for a placed player (bounded by the arrival limit).
- 6-D11: the drop rule says "the worn side unread" when `WornBefore == 0`.
- 6-D13: `rung-back` has its own retry (`(the way back to the rung's own)` in the lines).

## Tests

24 new (DeathHandlerTests 21, DeathScenarioTests.RespawnRace +2, J3bStationTests 1): slice-check 12,781 passed (default, 12,757 before), 0 failed, floor 12,781. One renamed by replacement: `M6_6_C1_a_journey_that_knows_the_tie_*` is now `M6_6_FXD1_a_journey_whose_bind_station_bound_*` (its rule changed). Updated: the R1G drop line text, the den heal rigs learn 2073 (the book is read), the LowRungs outskirts case expects the TODO warning.

## Next (the lead)

- Merge; the doc pass folds the doc notes (session.md 84; plan section 9).
- `BoundAt` is not in the autopilot's checkpoint: after a restart with no bind the respawn falls back to the object rule with its 10 s re-reads (still race-safe).
- `DeathScenario.Heal.cs`'s comment ("the mage knows it") was outside this slice's files.

## Gotchas

- The den rigs' books are read (the description sets `IsKnown`); a test that casts Heal Self must learn it.
- `DeathScenarioTests.Order`'s seam answers a Ground leg with the mod's place: that is why `rung-back` is journey-travel only.

## Open TODOs

`m6-death-fight-outside-ground` (new; M7 or close as designed).
