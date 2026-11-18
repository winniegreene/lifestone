# M6 item 7 slice AW: handoff

Branch m6/7-aw. One owner for the rung, a whole-climb gate test, the pass at rest. Scripted only: no live proof yet.

## Done
1. `Autopilot/RungOwner.cs`: `RungOwner.Decide(ladder, RungAsk)` is the one place. Inputs: level, cell, qualities, book, buffs (null = unknown), a stop's named rung, the run's ground, leaving. Output `RungChoice`: decision, ground, read level, reason (`band`, `gate-held:<rung>`, `gate-open:<rung>`, `stands-on:<ground>`, `stop-named:<rung>`, `quests-done`). Paths: host `AutopilotHostLogic.StartGround`, `MoveOnGround(view, stop, ...)`; scenario `AskRung` from the cycle's check, the fight's read and the new way out. Gone: `LevelUpGround(view)`, `GateHeldLevel`, `GatedLevel`, `MoveOnGround(ladder, stop)`, the book-only read ("after a pass"). `DecideRung`, `MoveOnDue`, `LadderGroundFor` are internal, so the Godot host cannot call them; `RungOwnerRepositoryTests` fails any other client caller, with a named allow-list for rung-data reads.
2. Two new behaviours the single owner needed: `BeginTravelOut` asks first (`LeavingHeld`): she leaves for the run's ground only on the owner's yes, so a death or trip at home with a lapsed buff stops `rung-moved:<lower>`, no walk. A run's first check that reads a gate open (buffs still up across a restart) is a move-on, not `failed:rung-ground-mismatch`. A stop naming no rung is asked as a start: buffs unknown.
3. `WholeClimbGateTests`: levels 15 to 22 by shut / half met / met by buffs known / unknown / lapsed (72 combinations), on six paths: start in town, on the ground or checkpoint resume (the checkpoint holds no rung, so a resume is a start), standing on the Burrow, restart after a stop, move-on in a fight, death at each rung. Plus three real scenario runs. Each proves no arrival behind a shut gate, always a run, and the same end rung as an independent table.
4. The pass. `Mage/PassPlaceRule.cs` with the ladder's `pass_home_over_lines` 4: more lines than that due and something attackable within 24 m walks home first, once, by name (`ladder: pass: place:`). Bug found: AT's cut never ended a pass: `PassCut` first leaves the wait for Magic, and the cut was read only inside the wait, so it flipped modes for 900 s (stretch 12: rests=730, no `cut at` line). Fixed. A pass in the ground now also cuts for a creature past the reach, inside 24 m. A stood wait logs `buff: pass: rest: posture stand`.
5. The 0.08 rest. It was net of presses: rest 209 s, stamina 27 to 43, three presses in it cost 44, 22, 44 = 110. Gained (16 + 110) / 209 = 0.60, exactly ACE's standing rate. `vitals: rest ended:` now says gained, spent and net.

## Deliberate breaks (run, reverted, not committed)
Start by band alone: 153 fail. Move-on by band alone: 116. The run's reads ignoring gates: 171. Way out not asked: 1 (the real run). Cut read only inside the wait: 2.

## Gotchas
- At level 22 the Cave has no gate, so a shut Lodge gate does not hold her: she goes on by the band. Unchanged data, but it is a way past an unmet gate. For the lead.
- A corpse run is not asked: it can return her to a gated ground for her corpse.
- A gate that reads open in a fight and shut at home sends her down and up again, one walk each time; nothing limits that.
- The town can be Home or Ground for an outskirts run by landblock: both run the pass.
- `AutopilotScenario.BuffsForTest` is the test seam for the buffs.

## Tests
13,832 by slice-check, about 453 new. Pins moved: `RungGateRuleTests` (restart theory, via the owner), `ClimberHostTests` (`NamedRung`), `TrophyQuestTests` (its climber now meets the gates, read on the way back). None deleted. Loopback: none.

## Open TODOs
m6-way-out-live, m6-pass-home-live, m6-gate-past-the-lodge (lead's decision), m6-corpse-run-gate.
