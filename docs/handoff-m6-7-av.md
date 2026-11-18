# M6 item 7 slice AV: handoff

Branch m6/7-av. Three live findings of 2026-11-06 (a restart picked the Lodge on the book alone; a recall home at the outskirts 180 m from town; Lifestone Recall refused for components stopped the run).

## Done
1. Run start gate: `AutopilotHostLogic.LevelUpGround(level, view, out why, atStart)` (the host's `PrepareRung` passes `atStart: true`; the move-on after a pass does not) reads `GateHeldLevel`. At a start every line "up" is a named NOT MET
   (`RungGateRule.AtStart`, text `RungGateRule.UnreadAtStart`); the gate of the rung whose ground she already stands on is not read (never stranded). So level 17 in town starts on the outskirts, and the run's own pass opens the gates.
2. `AutopilotTravelRule.Home` reads the plan, not the ground kind: the plan without the recall (`KnowsLifestoneRecall=false`) with legs, no portal and no stop is a rung-change on foot. `RungChangeStation` plans the same (home direction), so it walks, not recalls.
3. A recall refused 0x0400: `RecallHomeStation` sets `JourneyFacts.LifestoneRecallUnavailable` and logs `recall-unavailable:components`; `AutopilotTravelRule.Input` and the station's input drop the recall for the rest of the run;
   the Travel state's failure handler re-reads `HomeSpec()` and runs `rung-change-home` once (no repeat, no recovery, no stop). If no way home exists it falls through to the old named failure.

## Lifestone Recall's components (dats, test account formula; only the three taper colours depend on the account)
Copper Scarab, Comfrey, Powdered Malachite, Turpeth, Rowan Talisman, three tapers. **Comfrey and Powdered Malachite are on no ground's stock line** (Holtburg outskirts read; Comfrey in no profile): the trip cannot buy them.
Pinned by `GearKitTests.Lifestone_Recalls_components_and_the_two_the_outskirts_stock_lacks_M6_7_AV`. Open: `TODO(m6-recall-components-stock)`.

## Gotchas
- With the recall refused and no way home by portals or foot, the run still stops by the old name (flag already set, no loop).
- The scripted rig has `RefuseRecallComponents` (sets the fact as the real station does).

## Tests
10 new (6 gate, 3 travel, 1 dats). Count: see the slice-check line. Loopback: none added.
