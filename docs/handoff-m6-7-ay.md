# M6 item 7 slice AY: handoff

Branch m6/7-ay. Three owner decisions of 2026-11-06. Scripted only: nothing here ran live, no loopback added.

## Done
1. The pack in the health rule. `ClimberHealthRule.PackHits`: the want is the hit x (N + (pack - 1) x (N - 1)); at N = 2, the hardest hitter's two hits and one more from each other attacker. `health.pack` is rung data in `war-mage.json`: outskirts 1, Burrow 3, Lodge 3, Cave 5, the Temple on 1 (TODO m6-pack-past-the-cave). A road hitter is met alone.
   Wants against pay limit 30: outskirts 72 (paid at 16), Burrow 136 (health 75 at 16; paid at 31), Lodge 144 (81 at 17, 101 at 21; paid at 34), Cave 390 (106 at 22, 138 at 31; 7,996,103,161 XP: never paid). **The limit blocks the want from the Burrow to the Cave. It is unchanged.** Before: health 80 at 20, now 98 by the planner; War Magic 163 to 157 there, 198 to 196 at 30; the plan's III comes one level later at 17 (142 of 143), so the Lodge's `spell_level` is II-III.
2. Corpse runs. `RungOwner.CorpseRun` (additive; closes m6-corpse-run-gate): every corpse run of the ladder's character asks once, before any way is taken, on the buffs after the rebuff. Shut: `corpse_recovered=no trip_abort=gate-shut`, no way taken, an answer (the climb goes on). The corpse's own gate holds past the band (level 22, a Lodge corpse). `Mage/CorpseCoinRule.cs`: coin from ACE's line, `corpse_coin.min_coin` 2000, safe cells in the profile's `corpse_fetch` (outskirts anywhere; Burrow 0x0210, 0x020F; Lodge 0x0102, 0x0104, 0x0100, 0x0101), named on `handler: corpse fetch: ... -> fetch:met` or `no-fetch:<reason>`.
3. Big packs. `Mage/BigPackRule.cs`, `ladder big_pack` {awake 5, reach_m 24, deaths 2, mana_grows 20}. Lodge's `hunt` section: `keep_out` empty, `drop_back_keep_out` the cell of every slot but 1, 2, 3. Awake is read at the victim notice (`DeathTally.AwakeAtDeath`). Base mana is unbuffed. The rule never touches the rung, so the tusk goes ahead (AX's job). `pack: drop-back:` and `pack: drop-back lifted` are the lines; the fight's arg is `pack-hold=yes`.
   **State:** `AutopilotCheckpoint.PackHold` (ground, deaths, held, mana_at_drop), read at each start by `ReadPackHold`, as the tie memory is: it crosses restarts and run tags, not a re-created character.

## UNSOLVED: the coin is not on the corpse
ACE destroys a dead player's coin while `corpse_destroy_pyreals` is on (Player_Death.cs:569-578 @95ef426d; default true). SELECT on ace_shard.config_properties_boolean read 1. The m4 plan and m5 spec had recorded it. So decision 2's premise fails on this server: the fetch finds nothing, and a corpse with nothing rots in 15 s. The rule is built and tested, but `coin_on_corpse` is false in the data, so it answers `no-fetch:coin-destroyed` and she never walks for coin. Turning it on needs the server setting changed: the owner's call, and a database write no slice makes. Then set `coin_on_corpse` true.

## Gotchas
- Awake is distance (24 m, the awareness range) over what the object table holds: a creature in a cell that cannot see her would count.
- Cave pack 5 is distance and height only, no visibility: TODO m6-cave-pack. AX's Cave gate reads "health by the rule": 390 can never be met; it needs a figure of its own.
- A death during the handler is counted once by the station, on the last notice.
- `rearm=corpse` (no caster, no kit) behind a shut gate stays without a caster: only from level 35, where no gate stands today.
- `Ladder.WithCorpseCoin` is a test seam.

## Tests
13,888 by slice-check on a4203252 (build 0 warnings; 54 over the 13,834 floor). The `Rerun` was clean. Moved pins, none deleted: the want rows, the level 20 and 30 rows, `LateBolts` adds 17, `LadderTests`' spell plan row, `XpSpendRuleTests`' line, and `GroundTourKeepOutTests`' ground without a hunt section is now the Cave (the Lodge has one).

## Open TODOs
m6-pack-past-the-cave, m6-cave-pack, m6-corpse-coin-server (the owner), m6-pack-hold-live, m6-corpse-gate-live.
