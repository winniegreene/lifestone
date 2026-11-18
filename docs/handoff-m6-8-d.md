# Handoff: M6 item 8 slice D (the pack-buying trip)

## Done
- Fact: a fresh climber holds ONE side pack, the starter Sack (starterGear.json, the Jump skill's gear), plus two foci; the layout wants three side packs, so two are short. The ladder build grants none.
- `PackLayout.PacksWanted`, `SidePacksShort`, `ContainerRoom`.
- `ResupplyPlanner.PackBuy` and `ResupplyStation.PackBuys`: the stock trip buys the Pack (weenie 136) at the home vendor that lists it (Renald 88, Yoshi Deshi 117), bounded by the container slots (7, foci counted) and the purse left; one log line either way.
- `ResupplyPlanner.KitBuyList` takes `held` and `containerRoom`: a pack kit line short of its count is bought; no counter, no buy (as before).
- `BuyLine`: a pack needs a container slot, not a main slot; check after reads the side slots; `TODO(m6-pack-unplaced)` names a pack held but not in a side slot.
- Profiles: Pack row in 13 Renald rows and Yoshi's, SELECT cited in `sources.vendors`; `GroundCatalog` accepts the ware.
- Tests: `ResupplyStationTests.Packs` (M6_8_D_*), `HuntingGroundTests` (the pack skip is an assertion; a price theory), `PackLayoutTests` (a bought pack assigns every job).

## Next
- The lead's live round: first trip logs `packs: 1 side packs held ... buys 2 Pack`, then `packs: check after: side packs 1 -> 3`, then the sort moves the kit and finds.
- Doc pass: `cache/m6/8/doc-notes-d.md` (rows `m5-side-packs-use` closed; `m5-pack-short` keeps its name).

## Gotchas
- `m5-pack-short` stays in `PackLayout.Assign`: no vendor, no container slot or a thin purse still reach it (each named by `PackBuy`).
- Test harnesses: `Home` holds three side packs by default; `Trip`/`ApWorld` default none (`sidePacks:`), because other autopilot tests count slots.
- `ContainerRoom` ignores the extra-pack-slot augmentation (7 is a floor).
- Yoshi Deshi's CreateList lists each pack twice (same row): the draft test reads it once.
- A test host left by a cut-off `dotnet test` locks the test dll: let it end before the next run.

## Test count
14,136 baseline plus the new tests (see the slice-check line); open TODO names: `m5-pack-short` (kept), `m6-pack-unplaced` (new).
