# Handoff: M6 item 8 slice B1 (routes to Crater Lake and the stone cave, Silencia's roof, the Crater Caves ground)

## Done
- **Silencia's roof (no jump).** The tower is walked on foot: the door at x 60, then the spiral ramp inside it, seven flights, floor z 282 to roof z 297.2 through the shaft cell 0x90D00101. A body runs it in the dat test with no rung; the last point stands 3.7 m from Silencia, 4.7 m from the golem, inside the use reach 5 m (ACE's move-to walks the rest).
- **Routes as data** (`travel-routes.json`): places `glenden-wood`, `highland-manors`, `crater-pathway`, `crater-lake` (0x90D0 + 0x8FD2), `samsur`, `yinar` (0xA973 + 0xA66C); edges `network-to-glenden`, `glenden-to-highland`, `highland-to-pathway` (with `walk_in`: the Crater Pathway's door and stair, an authored way), `pathway-to-crater-lake` (with `walk_out`: out of the arrival building), `crater-lake-to-network`, `network-to-samsur`, `samsur-to-yinar`; way home `crater-lake`; ground `crater-caves` routes `from-the-village` (start crater-lake) and `by-the-crater-pathway`, both with a via round the crater lake.
- **Loader and runners**: `TravelEdge.WalkIn/WalkOut`, `RungPortal.WalkIn/WalkOut`, `TravelRequest.Way`, `AuthoredWaySource` (a fixed path the confirmer still checks), `TravelRoutes.FirstAdmitted(bodyPlace)`, `TownNetworkTrip` fewest-hops chains and a recall-only way back.
- **Crater Caves ground** `crater-caves.json`: frost plan, `wants_known` (Frost Bolt VI, Whirling Blade VI, Lightning Bolt VI, Fire Protection Self VI), `GroundWants.Check` loud lines and a named stop at the fight's start.
- Dat tests: every new edge through `TownNetworkWalkDatTests` (the theory), and `TownNetworkWalkDatTests.Crater.cs` (a body out of the arrival, up the tower, through the Crater Pathway's door; the Yinar run to the stone; the village to the Crater Caves portal). `CraterCavesProfileTests`.

## Next (B2 and the lead)
- B2 writes the quest files with these place cells: Silencia's stops are `go` steps with place cell 0x90D00100 at the points of `TownNetworkWalkDatTests.Crater.cs` `Flights` (seven, in order), then `buy`/`give` by weenie; the stone is place cell 0xA66C0100 point (83.55, 141.65, 16.8); the village is any place in landblock 0x90D0 or 0x8FD2; hunting motes is `hunt-until` on ground `crater-caves` wanting 6353.
- The fight casts only Flame Bolt: `crater-caves` stops the fight by name (TODO(m6-fight-element-line)) until the fight casts Frost Bolt; motes wait for it.
- The Crater Caves hunt reaches 121 of 493 cells: 28 of 63 slots, all 24 Nubilous Golems (the 4% mote droppers) and the 4 Cursed Wisps; the 26 Magma Golems and 9 Embers are past floorless ramps (`m6-crater-caves-lower`).
- The lead confirms by SELECT on the server database: the target spell books and body parts (the plan's source says so). The health, Magic Defense, attributes and damage types came from GroundDraft on the pinned SQL.

## Gotchas
- A portal's stored Destination cell can be stale (Crater Caves: 0x019A02CA, the point stands in 0x019A02D6); the profile names the real room and a test maps the draft's lookup back.
- The walk's `go` steps in the tower plan by naming cell 0x90D00100 for every flight (the physics takes the body through the floorless shaft).
- The crater lake floor reads water terrain: the outdoor plan walks along it and the physics refuses it; the via (40, 178) in 0x8FD20010 goes round by the north shore.

## Test count
14,160 default tests pass (floor 14,132 + 28: 8 named new facts, the rest the per-ground and per-edge theories over the new ground and edges); 1,088 exhaustive pass.

## Open TODO names
- `m6-fight-element-line` (widened), `m6-hop-walk-fight-back`, `m6-quest-go-fight-back`, `m6-crater-caves-lower` (new; the doc notes place them).
