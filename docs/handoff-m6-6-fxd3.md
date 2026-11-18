# Handoff: M6 6 FXD3 (fixes 6-D8, 6-D9, 6-D15)

Done
- 6-D8: `FightWalkRefusals.Refused` bounded (`MaxKept` 64 + `Overflowed`); class doc says the sink is process-wide, set once by a host; `FightWalkSeamTests` counts only `PlannerlessWalk` lines; a bounded-log test.
- 6-D9: `FightScenario` loot Wearing attacker arm waits (up to `ActionSeconds`) for the in-flight wield, logs its outcome, then ends the stage. Two private fields added (`_wearWaitLogged`, `_wearingGuid`) and one assignment in `Wearing()` (outside the arm, needed for the log). Test: `FightScenarioTests.Wearing.cs`.
- 6-D15: `RetreatRule.Decide` answers `health-unread` for NaN health; `ChangedSince` notices it; `Line` prints `health=unread`. Two tests (one allocation-free, Timing tier).

Gotchas
- The Wearing test forces the stage by reflection (field names `_loot`, `_stage`, `_pending`, `_corpse`, `_stageStart`, `_corpseStart`); renaming them breaks it loudly.
- A wait of up to 10 s with an unanswered wield holds the loot step (the fight does not cast) only while the wield is unanswered and the attacker is recent; the action API times a wield out at 5 s.

Tests: 12760 passed (default), +5 new (2 retreat, 2 seam/bounded counted as 1 new, 1 wearing: 4 new test methods). Slice-check ok.
Open TODO names: none new.
