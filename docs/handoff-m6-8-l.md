# Handoff: M6 item 8 slice L (the quest fight's bugs, the shot check, the bands)

## Done

- Kill loop (`QuestStepLeg`): every turn of the loop gives the frame back; mana is checked before any walk, and an attacker no longer ends the wait for mana; a dead attacker's name is read while it lives; a fought attacker is the nearest of that name in reach (15 m for a blow, 40 m for a spell line).
- Shot check, one place for both fights: `Session/Casting/ShotCheck.cs` (`BoltLaunch`, `PhysicsShotCheck`, `ShotLedger`) over the new `PhysicsWorld.SweepBolt`. Set up by `PlayMode` and `HeadlessJourneyRunnerHost` through `IActionApi.UseShots`.
- Wasted-cast rule, both fights: two in a row on one target (no bolt, or no damage landing) drop it for 120 s. Climb's half: `FightScenario.ShotCheck.cs`.
- `wasted_casts=`, `shots_blocked=`, `shot_disagreements=` on the fight's verdict line and the quest's.
- Bands: Worcer [10,18), Nen Ai [11,18), Lubziklan [12,18).

## Why the wall rule missed the ledge

`WallHit` needs a bolt the tracker took for ours. A target almost straight below gives a bolt whose flat heading points back at her, so `RefuseProjectile` rejects it and the cast reads "no projectile". `WallStay` is outdoors only, and it lives in `FightScenario`, which the quest station does not run.

## Next

- Live read owed: `shot check:` lines and the three fields. A disagreement is a shot called clear that did not reach; a shot called blocked that would have flown cannot be seen, so watch `shots_blocked=` against kills for over-blocking.

## Gotchas

- A cast ending NoEffect with a damage line since the mark is not wasted: the tracker can refuse a bolt that then lands.
- The misses in a row stand across a drop: they say the spot is bad.
- The pick's `HasClearLine` is unchanged, as the cheap sieve over many creatures.
- Changed tests: the three-misses test now uses two targets, `Two_misses_and_a_hit_do_not_walk` became `One_miss_and_hits_do_not_walk`, the far-attacker test uses a spell line, the cannot-hurt test adds a resist, and the quest verdict's range pin now allows the shot fields after it. None deleted.
- `NavTileDatTests.Holtburgs_landblock_builds_a_tile_under_100_ms...` is load-sensitive: it failed once at 122.8 ms with the live game running, and passed on the next check.
- A check costs about 0.13 to 0.18 ms (mean on the dats, `BoltShotDatTests`), once per bolt.

## Test count

Floor for this slice 14,309 default (14,292 + 17 new), 1,124 exhaustive. The baseline file is the lead's to raise.

## Open TODO names

`m6-shot-arc-start`, `m6-shot-no-setup`.
