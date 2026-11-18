# Handoff: M6 item 8 slice G (she fights back on the way to a quest)

Branch `m6/8-g`, cut from m6/8 866b5a2f. Doc notes: `cache/m6/8/doc-notes-g.md` (not tracked).

## Done

- `m6-quest-go-fight-back`: `QuestStepLeg.TravelFightingBack` (a stop on `StationLeg.TravelTo`, `DefendIfAttacked`, the same walk again). The quest station's every walk (go, approach, walk to the trip's start); the Academy keeps its old rule (`WalksFightBack` false).
- `m6-hop-walk-fight-back`: `TravelLeg.HopThrough`'s front walks, way in and way out are `WalkGuarded`; the seek to the portal stops too (`FightBackOnWalk`, the middle of `GuardedWalk`, shared).
- A walk that cannot resume: hop and run `recovery=replan-after-fight` once, then the walk's own failure; quest the step-away recovery, then the named stop.
- Rung change: a hop's `retreat:`, `retreat-failed:` and `fight-back:` names end the run (`EndsTheRun`): no route fallback, no gem.
- Fixed: `WalkToPoint` left a stale `WalkInterrupted` when the next walk had nowhere to go.

## Rules kept

- "Attacked": heard within 10 s (damage or evasion notice, or hostile spell line; the quest station now hears spell lines), alive, attackable, within 40 m.
- Cannot be hurt: hop or run, the fight-back's bound, health line or mana, then the leg's retreat; quest, `kill:timeout` at 180 s. Never runs past it.
- Stands down: hop in flight, a teleport, health 0, the urgent recall, inside a kill.

## Next

- Live: read `walk fight-back 1: check before` (quest) and `fight-back 1 on the hop leg` (hop) in the first round with an attacker on those walks.

## Gotchas

- `WalkGuarded` now takes `way`, `knownLife`, `moveWhat`; a hop passes its own life check and the "hop <key>" words so its death lines read as before.
- `HopThrough` rethrows a failure whose name a guarded walk set (`FailName != named`): no front-wall recovery after a death or a retreat.
- `KillOne`'s walks are not stopped (`_killing`): the kill's own order fights attackers first; a defence's chase must not re-enter itself.

## Tests

15 new (RungChangeTests 7 cases, QuestWalkFightBackTests 8). The slice check on this branch: build 0 warnings, exhaustive 1,093 passed, default 14,224 passed, 2 failed, 6 skipped, so `ok=false`. Both failures are `HuntingGroundTests` rows for `crater-caves` (the profile sells no Pack): this branch's cut predates the item branch's 5a8a744e, which fixes that profile. Not re-run on a tree with that fix; the floor after it is 14,226 (14,211 + 15).

## Open TODO names

None new.
