# Handoff: M6 7 slice AI, a walk keeps clear of portals it is not using

## Done
- `PortalKeepOff` (Movement/Wayfinding): the list of portals a walk keeps clear of, and the rule: no straight step of a plan comes within the clearance of a kept portal at its storey; a step that starts inside a disc may leave it.
- The clearance is 3.14 m: touch 1.84 m (portal sphere 1.357, the largest of the five portal Setups at scale 1, plus body sphere 0.48) plus wobble 1.3 m (measured: a running body overran a corner by up to 1.25 m). The hub's own portals are scale 0.8, touch 1.57 m.
- Room graph: `RoomGraph.KeepOff`: a step that meets a disc is off the plan; `FloorDetour` goes round inside a room; a doorway crossing that meets one is not walked. Lattice: `NavScars.SetKeepOff`, the outdoor portals as blocked discs.
- Runner: `NavRunner.KeepOff`: every plan's whole line is checked; a plan that cannot keep clear ends Blocked `no path: portal-keep-off:<portal>`; the straight run and a stuck rung's move stay out; `keep_off_nearest_m=` on the verdict; a report-only warning when the body comes inside the clearance.
- Hop: `RungPortal.KeepOffTheOthers` (every portal the travel data knows but this one) goes on the hop's front walks, its seek, and the rung change's via and entry-front walks (`TravelRequest.KeepOff`). A hop that cannot keep clear fails `<key>:portal-keep-off`; the route fallback reads it as any hop's named failure.
- The check after a walk, the fallback, the level gates, the routes' legs and `travel-routes.json` are unchanged.

## Proof (offline only: no live run has walked this)
- `HubPortalKeepOffDatTests`: the plan from the hub's real drop to each of the eight fronts stays 3.14 m from the others. Without the list, master's planner, 5 of 8 miss the clearance (Lodge, Cave, Cavern, Habitat, Abode); Lodge, Cave, Cavern and Abode failed the test when it was first run on the unchanged planner. `Without_the_list_...` pins it: inside the Burrow's touch on the walk to the Lodge, as live.
- The same file runs the real body at Run skills 60, 230, 450 to the front and on to the portal's 2 m: nearest 2.96 m, 1.13 m outside the largest touch.
- `Every_walk_between_...`: every arrival to every portal in the Town Network and the hub is planned clear, no plan refused.
- `TravelRoutesSqlTests.Every_portal_...` pins Setup, scale and physics state of every portal against the world SQL.

## Next
- The lead proves it live: a climber from level 15 on `by-facility-hub`; read `keep_off_nearest_m=` and `the plan keeps clear of the portals` in the log, and no `moved-elsewhere`.

## Gotchas
- Only walks to a portal carry a list. Any other walk in the hub (a quest giver, a trip) does not: TODO(m6-keep-off-all-walks).
- The list is the travel data's portals; a portal the data does not know (a summoned one, another dungeon's) is not kept off.
- `RoomGraph.KeepOff` sets the detour cache aside when it changes; the runner's naming plan flips it once on a failed plan.
- `FloorDetour` picks round-the-disc points from the floor polygons only: a tight room could leave no chain, which ends by name, not by a brush.

## Tests
- See the slice-check line in the reply. New: `PortalKeepOffTests`, `HubPortalKeepOffDatTests`, one each in `RungChangeTests`, `J5bJourneyPlannerTests`, `TravelRoutesSqlTests`. None deleted.

## Open TODO names
- `m6-keep-off-all-walks` (proposed, see doc notes).
