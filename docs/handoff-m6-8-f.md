# Handoff: M6 item 8 slice F, the ladder's trigger for quests (D118)

## Done
- A quest file may carry `ladder` `{from_level, before_level, once}` (`QuestLadder`; `QuestCatalog` refuses a bad one by its path).
- `AutopilotQuestRule.LadderCalls`: the second kind of call, one `autopilot quest: key= due= errand=ladder from= why=` line a decision. Refusals: `level-out-of-band`, `done-once`, `not-due`, `failed-this-run`, `no-route`. A quest with `ladder` is never a trophy.
- `QuestErrand`: trophies first, then ladder errands, once a trip, the band that closes first first, always `from=home`.
- A band passed with the quest undone: one `TODO(m6-ladder-errand-missed)` line a session (`LadderErrandMemory`), never run.
- `LadderErrandFailed`: a failed errand is remembered for the run (`_questFailed`), goes home by `RecoverTo`, and the climb goes on, with no recovery charged. A station that ended well with `done=no` counts as failed.
- A death in an errand goes to the death path and is remembered for the run.
- `QuestThenHome`: an errand from elsewhere that ended at home casts no recall.
- `[ladder]` has `errands=` just before `turn_ins=`.

## Next
- The quest-file slice gives the six quests their `ladder` objects; the two L1-20 portal quests need a band that closes below the portal cap.
- The first live read: `errand=ladder ... from=home`, then `autopilot quest: ended ... errands=1`.

## Gotchas
- `errands=` sits before `turn_ins=` because a test pins `turn_ins=... health=? reason=` as the line's end.
- A way home that itself fails still ends the run by that station's name. Below Lifestone Recall in a place with no route, the recovery's recall is refused and the run stops by name (`failed:travel:recall-home:...`). The travel file has to carry each quest's places.
- A cut (reconnect, watchdog) keeps the trophy's rule: not again this trip, called again on the next way home.
- `_questFailed` is per run: the next rung's run may try a failed errand again.

## Tests
- Default tests 14,184 (14,161 + 23): `LadderErrandTests` 21, `QuestProfileTests` 2. `TrophyQuestTests`' climber is now shared (`MakeClimber`).

## Open TODO names
- `m6-ladder-errand-missed` (new; a standing guard, placement in `cache/m6/8/doc-notes-f.md`).
