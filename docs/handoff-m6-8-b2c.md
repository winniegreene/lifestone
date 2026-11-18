# Handoff: M6 item 8 slice B2c (two more low-level fetches that walk)

## Done
- **On the ladder:** `nen-ai-pet-drudge.json` (band 8 to before 15, once, 30,000 XP) and the new `lubziklan-stolen-goods.json` (band 10 to before 16, once, 36,000 XP; reserve B). Both walk every leg in `A_fetch_quest_walks_end_to_end_from_its_own_file`.
- **Off the ladder:** the Four Stones, `TODO(m6-fetch-hive-drop)`: the Hive's levels join only by drops of 3 to 6 m and the confirmer takes 1.8 m, so a drop is a walker change, not a data edge. The Hive portal's stair is not the blocker (a body walks it).
- **The smallest data shape:** an optional `way` on a go step (`QuestStep.Way`, at least two points, the last the step's place, go steps only), handed to the runner as `TravelRequest.Way` (B1's `AuthoredWaySource`, still checked by the confirmer). Used for Nen Ai's stair up and down and for the Sea Temple's door and ramp.
- **Named flights:** the Cathedral portal's stair is two go steps, the second named in the door hall's room; a via at (23, -43, -22.3) in 0x01540104 plans round the pile, both ways.
- `TownNetworkTrip.Judge`: a trip to the start place needs no way back from it (a low-level fetch's last go home from another town).
- `travel-routes.json`: places `stone-cathedral`, `yaraq`, `sea-temple`; 0xD854 and 0xD853 in `shoushi`; six edges (47 in all), two with `walk_in`; three ways home; the Town Network way's hint for `yaraq-to-network`.

## Next
- Live: the first ladder runs read the stairs, the Sea Temple way, the floor pick-ups, the Strongbox 5162 and the pie's gift 5174.
- The lead's call: a drop move, if the Four Stones are wanted.

## Gotchas
- The stored Destination of portal 5198 is stale: 0x0156018F does not hold (49.1, -61.9, 0), which stands in 0x01560192. The dat tests start from the real room (`StreamRig.RealRoom`). Live, a body reporting 0x0156018F would stop `indoor-start-unreachable` by name and leave by the Surface portal.
- The confirmer samples every 1 m. A sample in Nen Ai's floorless shaft (y about 184.65 to 185 at x 84.1) finds no ground, so the stair is fixed way points. Do not move them without rerunning the body test.
- A way point within 1 m of the body is skipped (`AuthoredWaySource`), so the go before the way stops 1.3 m short of its first point.
- Each stair and way leg is walked with 0 recovery rungs. The planned ramp leg below Nen Ai's landing arrives but may use rungs.
- A failure on Nen Ai's upper floor below level 16 ends `failed:travel:recall-home` by name: no go leaves a room above a stair without its way.

## Tests
- Default 14,245 passed (floor 14,240 + 5: `FetchQuestTests` net +5). Exhaustive 1,119 passed.
- Deleted: `Nen_ais_pet_drudge_is_pinned_off_the_ladder_by_the_cathedral_ramp_and_the_tower_stairs`, and the Nen Ai row of `A_fetch_that_does_not_walk_is_off_the_ladder_with_its_named_todos`: both walks now reach, proved by the theory and `A_body_walks_the_cathedral_portals_stair_and_nen_ais_tower_flight_by_flight`.
- No loopback tests added.

## Open TODO names
- `m6-fetch-hive-drop`, `m6-fetch-tower-stairs` (only in the Four Stones file now), `m6-sea-temple-stale-arrival` (new, a live read).
