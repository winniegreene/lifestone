# Handoff: M6 item 7, slice BC (the bind step must not end a run)

## Done
- Tie short of mana: `TravelLeg.RestForSpell` (check mana vs the spell's cost, rest in place up to 120 s, check again) before the bind's tie and before the recall home.
- A tie that cannot be cast or does not link no longer fails the bind station: `bound=yes tied=no`, a logged warning `TODO(m6-bind-tie-mana)`.
- `AutopilotTravelRule.Home` returns `TravelWay.AtHome` at home; `RecoverTo` goes to the known state, no recall from home.
- Stock check: the climber holds all four foci, so Lifestone Recall burns the Copper Scarab and Prismatic Tapers, both outskirts stock lines (pinned by a dats test).

## Why the live run stopped
The bind took. Lifestone Tie was refused for mana (45 held, cost 50), the station failed, the recovery recalled from home and was refused for mana too (47 held, cost 50; the station's need was 20%, 37 of 185), and the mana-dance fix passed 120 s. Not a component refusal, so AV's rule never fired.

## Next / gotchas
- `TODO(m6-recall-components-stock)`: the full formula's Comfrey 767 and Powdered Malachite 788 have no outskirts stock line (Cindrue sells both); only a climber without a focus would burn them; a stock line costs a pack slot.
- Other `HomeSpec()` callers still get the recall spec when at home (only the recovery path skips it).
- J3bRig gained `ManaPerSecond` (default 0) for the rest tests.

## Tests
Default count 13,931 (13,926 + 5; one test needs the dats). Two J3b tests now expect the station ok on a tie that does not link or is refused 0x0400.

## Open TODO names
m6-bind-tie-mana (new), m6-recall-components-stock (narrowed).
