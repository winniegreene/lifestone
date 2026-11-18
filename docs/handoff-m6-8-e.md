# M6 8 E handoff (the small open rows)

**Done.**
- `m6-minutes-stop-safe-point`: `JourneyFacts.MinutesStopDue` (the autopilot's `MinutesDue()`); the logistics trip reads it at home before its way back, skips the way back, and the run stops at home. Fights, station starts and heals already read the rule. The level rule is untouched.
- `m6-ladder-quests-done`: not a bug. `quests_done` is the quest rungs' own quests, `turn_ins` the trophy turn-ins; docs of both fixed, test pins `turn_ins=1 quests_done=0`.
- `m6-enter-check-tied-recall`: no code; a tied recall lands inside under the bar by its own mana cost, and the first pull's bar (`PullBarHolds`) is the arrival check. Reason recorded in the doc notes.
- `m6-keeper-attacker-reply`: closed by item 7 BD; evidence in the doc notes.

**Next.** Doc pass reads `cache/m6/8/doc-notes-e.md`. A live round whose limit falls inside a trip proves the safe point.

**Gotchas.**
- The buy-trip and a rung change's walk out read the minutes rule only at the next station start (named, not changed): TODO `m6-minutes-stop-buy-trip`.
- The trip test needs the dats (RequiresDatsFact).

**Tests.** 14,135 passed by slice-check (floor 14,133 = baseline 14,132 plus 1 new: `F3WayBackTests.M6_8_E_...`; one existing test gained an assertion). None deleted.

**Open TODOs.** m6-minutes-stop-buy-trip, keeper-fight-awake-read (existing).
