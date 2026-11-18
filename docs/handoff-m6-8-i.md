# Handoff: M6 item 8 slice I (the round's death and the coin she left behind)

Branch m6/8-i, cut from m6/8. Doc notes for the doc pass: cache/m6/8/doc-notes-i.md.

## Done
- The retreat that stood still: `RungChangeStation.Needs` is now direction-aware (`NeedsOf`): the way out keeps the 20% mana need, the way home (a walk or portals, never a cast) needs none, so a retreat with no mana walks home. `recall-home` keeps its need.
- The corpse trip's limit: a corpse worth fetching (coin rule met, or a needed item, gate open, safe cell) is given up only on no progress (`corpse_coin.trip_stall_seconds` 90, `trip_stall_metres` 10 in war-mage.json; `trip-stalled-90s`), not on elapsed time; fights and heal holds restart the clock. Other trips keep `--death-recover-abort-seconds`. One log line `handler: trip limit: ...`.
- The purse at every rung: outskirts coin 30,000 and min_note 10,000; the Tethana fort's Zisaha now sells the purse notes (she does, SELECT in the profile); a test over every rung and alternate of the war-mage ladder.

## Next
- Live proof at the next gate: a retreat with no mana walks; a corpse of 40,000+ coin is fetched; a note is bought above 30,000 coin.
- Owner question: should she fight on (wand or melee) when mana is gone? No such rule exists (TODO(m6-no-mana-fight-on)).

## Gotchas
- `Purse.Coin` is also what `GearKit.KitPlan.For` grants a build: a build on the outskirts now grants 30,000 coin. The climber is never granted anything.
- The coin a band holds in the purse test is an estimate (89,190 at level 11, scaled by the band's first level: TODO(m6-band-coin)).
- A stationary open or loot step counts as no movement for the stall limit (90 s in one step ends the trip).
- `OptionCatalog.Death.cs`'s text for `--death-recover-abort-seconds` is not updated (outside the slice's files).

## Tests
- New: 7 default tests (4 in DeathHandlerTests, 1 RungChangeTests, 1 purse-over-the-rungs, 1 shipped stall values among the 4). Updated expectations: CorpseCoinRuleTests, HuntingGroundDatTests, three ResupplyStationTests.
- Count: see the slice-check line in the reply.
