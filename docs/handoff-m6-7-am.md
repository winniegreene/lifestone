# Handoff: M6 7 slice AM (a bolt that hits the world outdoors keeps the fight on its target)

Done
- Why she walked off: the M5 7 F23 branch in the hit wait (`FightScenario.cs`, "a bolt hit a wall") always called `StartWalk`, which takes the tour's next cell. In a dungeon that is the way to a line; outdoors the cell is far off and the target stays put.
- Why the line check passed: `OutdoorFightWalk` had no line test (`IFightWalk.HasClearLine` default true, `TODO(m6-outdoor-line)`), so the pick took targets 50 to 70 m away behind a hill. The live case on the dats (`OutdoorLineDatTests`): the line passes 0.4 m over the hill's back at 30 m; the bolt died 44 m out.
- New: `OutdoorFightWalk.ClearOverTerrain` (the bolt line over the real terrain triangles every 2 m, 0.5 m clearance, ends 2 m unjudged; unread or indoor is clear), wired by `Create` from the dats. Trees and buildings are not in it: `TODO(m6-outdoor-line-objects)`.
- New rule (`FightScenario.WallStay.cs`): outdoors, a wall hit steps toward the target (half the way, at least 8 m, never inside 10 m; the second step also 6 m to the side) and the pick shoots again; the third wall hit on one target blocks it for 120 s and the pick takes the next. Counts reset on a damage line or kill. Every step logged `wall stay: ...`. A dungeon keeps the old rule.
- `OutdoorFightWalk.BeginLegTo`: a goal with Z = NaN means "on the terrain" (water or steep ground refuses; the old rule then runs, named TODO).
- Test rig: the scripted projectile now starts in the player's own cell (it started in the den's, 45,000 m from a belt player).

Tests: 6 default (FightScenarioTests.WallStay.cs) + 1 exhaustive dat test; default count 13,205.

Open
- Whether the stay rule's 8 m / 10 m / 6 m / 3 hits / 120 s fit live: watch `wall stay:` lines and `wall_hits=` in the next stretch.
- `TODO(m6-outdoor-line-objects)`, `TODO(m6-wall-stay-no-spot)`, `TODO(m6-wall-stay-no-leg)`, `TODO(m6-wall-stay-no-position)`.
- A hit wait of 5 s on a 68 m shot is long for a mage that could close in; a nearer first pick is a separate question.
