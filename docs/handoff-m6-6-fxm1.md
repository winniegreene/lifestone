# Handoff: M6 item 6, slice FXM1 (the task's bounded fight-back, the errand's own resume, a task only at home or on the ground)

Branch `m6/6-fxm1` (cut from m6/6 at 5eb34ac8). Doc notes: `cache/m6/6/doc-notes-fxm1.md` (outside the repository). Review rows: `cache/m6/6/review-menu.md`.

## Done
- 6-M1 (blocker), `AutopilotScenario.Jobs.cs`: the task's fight-back is bounded to the attack that started it. `BeginTaskFightBack` records the start, the creature on him (`HostileWatch.Nearest`'s guid) and the attacker; `TaskFightBackOver` ends it (one line each) at the first of: "defend only" (the recorded attacker dead, gone from view or out of reach, and no attack for `TaskFightBackLullSeconds` = 5 s), no attack for `HostileWatch.AttackQuietSeconds` counted from the later of the last attack and the start, or the cap `TaskFightBackMaxSeconds` = 45 s (a warning line). One fight-back before the task begins and one more after each cut that runs the station again (`_taskFightBackSpent`): a busy ground cannot hold the task off. The read model has no creature's target, so "no hostile on him" is read as the lull.
- 6-M3, 6-M4: `ErrandTaskArrow` + `ErrandResume`: the errand runs its cut (or carried) trip again itself (`ResumeTrip`) at home or on the ground and counts the run again only there (a carried trip's first run is not counted); on the ground with the audit's attacker it fights back first with the task's flag, never the job's open fight.
- 6-M5: `TaskArrow` decides only at home or on the ground (logs `autopilot task: <name> waits: he is <place> (<cell>)`), so the place arrow recalls home and the audit finishes first; `_questTripTried` is set before that check, so the elsewhere arm never calls the D58 errand on a task's way home.
- 6-M11: `EnterKnown` clears `_taskFightBack` (`AutopilotScenario.cs`, one line).
- 6-M16 (second half): `autopilot task: quest:<key> begins at <home|ground> (the station from=home[ (the station's way home first)]) (<timer>)`; `autopilot task: errand begins at <place> (...)`.
- The D58 tusk errand is unchanged (TrophyQuestTests run).

## Tests
`AutopilotTaskTests` +5: the busy ground (a defender notification every 2 s for 60 s: the trip begins at the cap, before the attacks end, one den-fight), the defend-only lull, the errand cut away from home (counted once, no cut-again), the errand cut on the ground with a hostile in reach, a quest task found elsewhere. One pinned line updated (`begins at home (the station from=home)`). Default count 12,757 + 5 = 12,762 (slice-check -MinTests 12762 -Rerun PASS, 0 warnings). Exhaustive untouched. TrophyQuestTests (D58) pass unchanged.

## Gotchas
- The `TaskFightBackOver()` call site in the fight's stop decision (F2's region) is unchanged; the guid check reads the scenario's `_session`.
- A fight-back whose fight goes to upkeep and back keeps its start: the cap still holds.
- The errand's fresh begin with the audit's in-reach hostile and no attack heard still begins the trip (6-M4 was ruled for the resume only).

## Open TODO names
None new.
