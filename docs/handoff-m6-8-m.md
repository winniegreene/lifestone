# Handoff: M6 item 8 slice M (the Cathedral house, and getting home from inside a building)

## What `indoor-start-unreachable` meant here
The Cathedral portal's room 0xD8540102 joins no door in the room graph: its only link is the stair round the floorless shaft 0xD8540100. She got there by accident: `RaisedStart.FloorBelow` read the flight (a plan to a point 5.2 m lower, named in the door hall's own room) as a body on a counter, and the step-off walked her down the stair into the cut-off room. The leg's plan from there, and the way home from there, then both failed by that name. The dat tests never ran the step-off: they drive `AuthoredWaySource` or a bare runner, not `JourneyRunLeg`.

## Done
- **The cause:** `RaisedStart.FloorBelow` takes the room's own floor under the body (`RoomGraph.RoomFloorUnder`, polygons of the cell, a static such as a counter is not one): a body within 0.5 m of it is on the floor. Renald's counter still reads raised (dat test).
- **The file:** step 1 of `nen-ai-pet-drudge.json` carries a `way` (a replan on the stair goes on along it, as the plan cannot).
- **Getting home:** `travel-routes.json` has `ways_out` (key, rooms, way, source; read by `TravelRoutes`, handed to `IndoorOutdoorPathSource.WaysOut` by `JourneyLegPlanner`). A start whose plan failed, in a named room, walks the way then plans from its end, so a go, the walk home's first leg and a replan all leave the building first. Three ways: `cathedral-portal-house`, `nen-ai-upper-floor`, `sea-temple-portal-room`. A plan that finds a way is unchanged.
- **The Catacombs' Surface portal:** the hop's computed front points lie over no floor; the edge `sea-temple-to-yaraq` has a `walk_in` to the quest file's own stand (40, -48, 0).
- **What is left says so:** a room with no named way ends `indoor-start-unreachable:<cell>: ...; TODO(m6-room-way-out): the travel file names no way out of <cell>`.

## Next
- Live: `go: down the stair` then `the authored way of 2 points`, `quest_step: n=1 ... ok=yes`, no `raised start:` line in the house. A failed errand in the house: a walk home with `way-out` cells in its plan, `rung_change: ... ok=yes`.
- The lead or owner: `TODO(m6-cathedral-pile-way-home)` below, and whether a last-resort recall exists (no recall was added, as the brief says).

## Gotchas
- A way out keeps every point unless the body is within 0.1 m (`RoomWayOut.OnPointMetres`, not the 1 m of a go's way): the confirmer samples a segment every 1 m from its start, and on Nen Ai's flight a sample from a body beside the top point falls in the shaft.
- A way out's last point must not be in a room any way out starts from (the file refuses it).
- `HeadlessJourneyRunnerHost` plus the real `QuestStation` works as a test seam (`QuestGoProbe`): the first test that runs the station over the real leg.

## Tests
- New: 13 (2 default, 11 exhaustive: `TownNetworkWalkDatTests` and the J4 tests are the Exhaustive tier). Default 14,294, exhaustive 1,135 (floors 14,292 and 1,124). `TownNetworkWalkDatTests.WayOut.cs` (sweep of every errand place's first walk home; a body up the stair and out of the door; raised start vs counter; no go reads raised; a room with no way says so; Worcer's real pick-up end), `The_real_stations_go_walks_the_files_way_from_where_the_step_before_left_her`, `A_go_hands_the_files_way_to_the_walk`, `The_ways_out_load_and_a_bad_one_fails_by_its_path`.
- Deleted: none. Changed: `A_body_walks_the_cathedral_portals_stair_and_nen_ais_tower_flight_by_flight` picks the tower's first way (step 1 now has one too).
- Loopback tests added: none.

## Open TODO names
- `m6-room-way-out` (loud in the failure text: a room with no named way out; the file grows by data).
- `m6-cathedral-pile-way-home` (new): the Greenmire Cider's rooms (0x01540115): the walk to the Surface portal plans through the pile in the ramp room, the physics refuses it and the rooms have no other route. The errand's own via step walks it; the way home from there is pinned failing by name. Placement: the lead's call (a planner via, or the owner's last resort).
- `m6-room-way-out-pathway` (new): the Crater Pathway's house (`highland-to-pathway`'s walk_in) has no way out; a body stopped inside fails by name.
