# Handoff: M6 8 slice N (`/lifestone` as the survival layer's last resort, D120)

## Done
- `LastResortRule.cs` (pure: check before, arrival after, stop name, ACE's 8 m threshold) and `AutopilotScenario.LastResort.cs` (the step machine: `RunPhase.LastResort`).
- Armed by `LadderErrandFailed` (one line in `AutopilotScenario.Ladder.cs`); taken by `LastResortOnTravelFailed`, called in the Travel case before `RungEnded` (`AutopilotScenario.cs`, 4 small edits: the phase, its switch arm, the hook, `EnterKnown` clears the arming, `NoRecallStop` knows the stop).
- Once per failed errand; only for a failed errand's way home (`RungHomeSpec`/`RecallHomeSpec`, not a retreat's, not the final recall).
- `[ladder]` line: `lifestone_recalls=` after `health=` (`LadderLine.LifestoneRecalls`).
- 12 tests, `LastResortTests.cs`.

## Gotchas
- The client cannot read ACE's Sanctuary: only `Facts.Bound` is known, so the server's refusal line is the check (`no-sanctuary`).
- Busy is `IActionApi.BusyReason`, not the queue counts (a pending mode change held it past 5 s in the rig).
- The phase is not in the watchdog's list; the tracker's own time-out (ACE's delay plus its margin) bounds it.
- `LadderErrandTests` is now `partial` (the new tests share its helpers).
- No loopback test written; the live check is one failed errand with no recall spell (round 2's case).

## Next / open TODO names
- `TODO(m6-lastresort-watchdog-home)`, `TODO(m6-lastresort-precondition)`, `TODO(m6-lastresort-sanctuary-read)` (see `cache/m6/8/doc-notes-n.md`).
