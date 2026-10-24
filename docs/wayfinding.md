# Wayfinding (M4 polish item P5a)

The wayfinder is how a character walks a planned route over the world's real collision: an A* over a per-landblock
navigation map, a room graph for the buildings, a runner that presses the movement keys through `IActionApi.Move`, and a
streamer that keeps the collision and the map loaded ahead of the body. It is Lifestone's own design, not retail's: retail
has no such layer. The older scripted walks use `RouteDriver`, which is kept for them. The principle is **the map proposes,
the physics confirms.** The planner works on a tile that was built from the collision, so a route is what the map says is
open; before any movement key is pressed the route is checked against the collision (`RouteConfirmer`), and while the body
walks the runner watches where it actually is.

The OpenAC navigation assessment (docs/research/openac-navigation-assessment.md) read OpenAC's navigation files at a pinned commit, for their ideas (clearance, a recovery ladder, defined arrival, the player's keys win). The wayfinder's slices were not given that code, and Lifestone's code ported none of it.

Code: `src/Lifestone.Client/Movement/Wayfinding/` (the tile, the planner, the room graph, the runner, the confirmer, the
streamer) and `src/Lifestone.Client/Movement/Routes/CellRoutePlanner.cs` (the doorway test the room graph shares).
Physics notes: `docs/physics.md` section 16. The keys' binding: `docs/session.md` section 40.

## The tile

`NavTile`: one per landblock, 192 x 192 nodes at 1 m; node (x, y) is centred at landblock-local (x + 0.5, y + 0.5). Per
node: z, flags (Blocked, Slope, Water, Static, Building, DoorwayLane, shadow), `SlopeCost`, clearance and an owner index,
stored as struct-of-arrays (about 9 bytes a node). `NavTileBuilder.Build(block, options)` is pure and thread-safe (it runs
on a `WorkerPool` worker) and reads only the immutable `LandblockCollision`.

- **Ground.** z is the terrain polygon's plane under the node centre (`PhysLandCell.FindTerrainPoly`, retail
  `CLandCell::find_terrain_poly` 11.6096 0x00533A30, as `RouteLibrary.TryStand` reads it). A node under
  `PhysicsGlobals.FloorZ` (0.664) is Blocked with `SlopeCost` 255. Slope flag from FloorZ to 0.85, with `SlopeCost` falling
  linearly from 255 to 0 in the normal's z (0.85 is Lifestone's own, not retail's). A node with no terrain polygon is
  Blocked and counted; a landblock with no land block record is blocked throughout.
- **Water.** The land cell's `WaterType` (24 m resolution): any water corner flags the whole cell's nodes Water. Water is
  costed, never blocked. Conservative; the per-corner words are private to the collision set (TODO(nav-water-corner)).
- **Statics.** Shapes are taken in retail's order (`CPhysicsObj::FindObjCollisions` 11.6096 0x0050FB20): the physics BSP
  parts' bounding boxes (an oriented rectangle when a part is only turned about the vertical), else cylinder-spheres, else
  spheres. Each is inflated by the body radius 0.48 plus a margin 0.1, so 0.58 m, with rounded corners. A static's top is
  never walkable (a 0.4 m step still blocks). Not blocking: a top within 0.05 m of the ground at the node (a decal), an
  underside 2.0 m or more above it (overhead). Skipped and counted: EnvCell statics (the room graph owns them), statics
  wholly below the ground, tiny ones (footprint under 0.3 m and lower than the 0.6 m step-up), and statics no node reaches.
- **Neighbours' shadows.** `NavBuildOptions.Neighbours`: the streamer passes the attached neighbours and each one's
  foreign shadows that fall inside this landblock are stamped as obstacles.
- **Buildings.** The footprint is the union of the shell's collision polygons projected on the ground, roofs and eaves
  included, inflated 0.58 m. The bounding box is used only when the shell has no polygons (counted `by_bounds`). Reason:
  Holtburg building 6 (Renald's shop) has a 20 x 18 m bounding box round a footprint a third of that, and with the box
  both doorway lanes were islands.
- **Doorway lanes.** For each building portal, the entry EnvCell's outside portal polygon (`PhysCellPortal.Vertices`) must
  pass a standing human as `CellRoutePlanner.IsWalkable` probes it (upright within 0.1 of the vertical; 0.2 m and 1.8 m up
  the centre; 1 m up half a body width either side). A passing portal gets a 1.5 m strip from the sill, 2.5 m out along the
  outside direction, sampled every 0.25 m; its nodes lose Blocked, Static and Building (a steep-terrain block stays) and
  gain DoorwayLane. Holtburg: 23 lanes; 27 portals are impassable (horizontal hatches, narrow doors, skylights) and skipped.
- **Clearance.** A two-pass chamfer with integer weights 5 (axis) and 7 (diagonal); the distance in nodes is the sum over 5,
  rounded, capped at 10, and 0 on blocked nodes. The tile border is open. It is within 1.5 nodes of the true distance (a
  5-7 chamfer overestimates knight moves by up to 8 percent). `ComputeClearance` can be re-run after a hand stamp.
- **Cost.** Holtburg 0xA9B4 builds in about 5 ms (median 5.3 ms; the first build with the JIT about 15 ms); a synthetic flat
  tile about 3 ms. The design had estimated 20 to 40 ms for a dense landblock; the cost is the 36,864 terrain probes and the
  stamps. The build's log line is `NavBuildStats.ToString()`: `navtile ms= statics= shadows= buildings=(by_bounds=) lanes=
  skipped(...) portals_skipped(...) no_terrain=`.

Every constant above is Lifestone's own, with the reason beside it; none is a retail value unless a retail function is named.

## The planner

**Addressing.** A `NavPoint` is a landblock (0xXXYY0000) plus landblock-local metres, as a retail position is. The map works
in global metres: `LandDefs.GetBlockOffset` from landblock 0x0000 plus the local x and y. The global node (gx, gy) is the
1 m node whose south-west corner is global metre (gx, gy): landblock x is gx / 192 and node x is gx % 192. The node east of
node 191 of one landblock is node 0 of the next; no carry between the id's bytes is ever formed. `NavMap.FromCellFrame` and
`ToCellFrame` convert retail positions; an indoor position maps to the land cell above it (the room graph owns indoor cells).

**Steps.** A step goes to one of 8 neighbours. A diagonal needs both orthogonal nodes open (no corner cutting). A rise over
0.6 m (`NavMap.StepUpMetres`, the body's step-up, the one definition: the builder's `NavBuildOptions.StepUp` takes its default from it) or a drop over 2 m
(`NavMap.MaxDropMetres`, a conservatism) blocks the step.

**The coarse graph (level one).** Each tile's 64 land cells (24 m) are flood-filled into regions over the steps that pass
both ways, so a wall or a ledge splits a cell into several regions. Regions link where a step leads from one to the next
(a drop links one way only), inside the tile and across its edges. The graph is rebuilt per tile on `Attach`; `Detach`
drops the links into the tile. `NavMap.Version` counts attaches and detaches.

**The fine search (level two).** Coarse A* over the regions (cost and heuristic: the distance between region centroids)
gives a corridor of land cells. Fine 8-neighbour A* (octile heuristic) runs first bounded to the corridor plus one land
cell (`CorridorRing`), then once at plus three (`WidenedRing`), then unbounded over every attached tile. `NavPlanResult.Widened`
says which. A coarse graph with no corridor goes straight to unbounded.

**The cost.** A step costs its length (1, or the square root of 2) times (1 + 2 x SlopeCost/255 on a slope node (`SlopeWeight`
2) + 0.3 per node of clearance under 5 (`ClearanceWeight`, `ClearanceSoftNodes`) + 3 on water (`WaterWeight`)), plus 1 per
metre that a drop exceeds the step-up (`DropWeight`). Every addend is non-negative, so the octile heuristic stays admissible
and open flat ground costs exactly the octile distance. `TieBreak` 1.001 weights the heuristic so that equal-f nodes go
deepest first; a plan may cost up to 0.1 percent over the optimum.

**Pooling.** The node states, the decrease-key open heap, the corridor stamps and the coarse arrays live between plans and
grow only with the map. A warm replan allocates only its result (352 B in Release for a three-vertex route).

**String pulling and the route.** The node path is pulled to vertices by lattice line of sight (0.5 m samples, clearance at
least 1, legal steps between samples), galloping and then binary-searching from each vertex. A pulled segment never crosses
water, a slope, or a scar the node path kept off. Each vertex carries its node's z. `NavRoute` exposes the vertices, the
horizontal arc length, `PointAt(s)`, `NearestArcLength(p)` and `DistanceFrom(p)`, which the runner's pure pursuit and its
3 m off-route test (`PathPlanner.OffRouteMetres`) use.

**Goals.** A point goal on a blocked or scarred node is `GoalBlocked`; there is no silent snap. An object goal arrives at
any open node within its `Range` (at least 0.75 m, `MinObjectRange`); sight is left to the runner's sweep.

**Scars.** `NavScars` are discs with a life (30 s by default, on an injectable `TimeProvider`). They are the run's own
overlay and are never written into a tile. The planner refuses a step into a scar, except outward from inside the scar the
start stands in.

**The horizon contract.** A goal beyond the attached tiles plans to a horizon goal: the open node on the attached area's
outer edge that the start reaches on the coarse graph and that lies nearest the segment from the start to the goal
(`Status = Horizon`, `HorizonPoint`). The runner walks it and, once `NavMap.Version` has changed and the body is within
60 m of the route's end (`PathPlanner.HorizonReplanMetres`), plans again to the real goal. At a horizon with no new tiles the
body stands and the keys are released; no rung is taken.

**Named failures** (`NavPlanResult.Reason`): start-not-attached, start-blocked, goal-blocked, goal-scarred,
no-open-node-in-range, no-horizon, no-path.

**Measured** (warm medians of ten plans, 2026-10-23): a 1 km plan over six hand-built tiles through a
dense wood (120 boxes a tile, 4 percent blocked, about 40 percent of the ground under the clearance cost; 64k nodes expanded,
the whole 72 m corridor) takes 6.3 ms in Release (15.6 ms in Debug); open country (20 boxes a tile) takes 1.1 ms in Release
(3.0 ms Debug). String pulling is 0.1 ms of it. The test is `PathPlannerTests.A_one_kilometre_plan_takes_low_single_digit_milliseconds`
(Tier=Timing).

## The room graph and the doorway-lane join

Indoors the wayfinder is `RoomGraph`. Nodes are the landblock's EnvCells; the vertices are the doorway pairs of
`CellRoutePlanner` (a waypoint 0.7 m before a portal in the room being left, one 0.5 m past it in the room entered, both at
the portal's centre over the floor). `Plan(fromCell, from, toCell, to)` is a shortest-walk search: Dijkstra over the
crossings, where each crossing costs a further 0.5 m, so that of two equal walks the one through fewer doorways wins. It
returns the `CellRoute` that the Academy route uses. `FindPath` is the fewest-doorways search; `PlanScripted` is the den's
leg (goals at 0.5 m, a stop at the room's `RoomFrame`).

One planner serves every room: the Academy, the shops and the Black Spawn Den. `CellRoutePlanner(cells, maxLean, headSlack)`:

- `maxLean` (default 0: the upright rule, normal z at most 0.1) is the largest vertical part of a doorway's normal that is
  accepted. The den uses 0.3, because its plazas' doorways lean about 11 degrees (normal z 0.196). The fit test is projected
  on the flat normal, so a leaning doorway is judged by the opening a human walks through, not the tilted polygon.
- `headSlack` (default 0 on `CellRoutePlanner`, 0.05 m on `RoomGraph`) is how far under 1.8 m the lintel probe lies. Renald's
  room 0xA9B40141 is reached through doors exactly 1.8 m tall (13F to 140 to 141), which the 1.8 m probe misses by no margin.
  Across the Academy (0x8602) and the den (0x0105) no portal changes with it; in Holtburg 17 of 319 do (the shops').
- `CrossingMetres`, `JoinToleranceMetres` and `DefaultHeadSlackMetres` are Lifestone's own constants.

**The join with the tile.** `ExitsToOutside` lists the portals that lead outside (`PhysEnvCell.cs:24`,
`CellPortalData.LeadsOutside`) that a human fits through: the cell, the sill point (the polygon's centre at its lowest z) and
the outward direction. `TryJoin(tile, entryCellId, out lane)` finds the tile's `NavDoorwayLane` for the same door: the same
entry cell, its sill within 0.75 m of the exit's sill, the same way out. Holtburg has 24 exits, and all 23 of the tile's
lanes join. The doorway test of `CellRoutePlanner.Fits` (normal within the limit, then four probes: 0.2 m and 1.8 m above the
sill, and 0.48 m either side at 1 m) is the same test the tile's lane cutter uses outdoors, so a lane exists exactly where
the room graph has an exit a human fits.

`IndoorOutdoorRoute.Plan(planner, rooms, from, toCell, to)` composes the two graphs. Of the tile's lanes whose door the room
graph has and whose rooms reach the goal, the outdoor `PathPlanner` plans to the lane's outer end (2.5 m out), the lane's
centre line leads in to the sill, and `RoomGraph` plans from the entry cell to the goal; the cheapest whole walk wins. The
result carries `Outdoor`, `Lane`, `Exit`, `LanePoints`, `Rooms`, `Inside` (everything after the outdoor route, as retail
positions) and a status (`Found`, `Horizon`, `NoTile`, `NoDoorway`, `OutdoorFailed`) with a constant reason. From the
Holtburg lifestone, Renald's shop 0xA9B40141 is reached by the lane into 0x150 (51 m outdoors, cells 150, 13F, 140, 141)
and Thelnoth's cellar 0xA9B40111 by the lane into 0x115 (155 m, cells 115, 111).

**Renald's back room is not walkable.** Renald's back room 0xA9B40141 lies behind the counter. The room graph plans 13F to
140 to 141, but the floor of 140 and its doorways sit one metre above 13F's: the ground probe finds no walkable ground there,
and the placement in 140 is refused as NoValidPosition. With 140 left out the graph plans 145, 143, 142, 141, and the ground
probe refuses the crossing at 142 (139.40, 13.35). Unconfirmed, the walker climbs onto the step at (135.82, 13.93, 94.97) in
13F and is Blocked after 12 rungs. So 0x141 is not walkable on foot. **The shop goal is the front room 0xA9B4013F**, 57 m
from the lifestone, reached in 0 rungs. The room graph does not yet learn sills (TODO(room-steps)): a doorway whose sill lies
over the step-up above the floor before it should not be passable, and the runner already goes round at run time.

## The runner

`NavRunner` (`Movement/Wayfinding/NavRunner.cs`) acts only through `IActionApi.Move` (Command forward, back and turn;
sidestep; ChargeJump and Jump; `SetRun(run, Automation)`) and reads only `INavBody`, the body as the controller reports it
(`ControllerNavBody` in both hosts). It is stepped: `Start(goal, now)`, then `Step(now)` once a frame after the physics step,
with `Pause()`, `Resume(now)` and `Dispose()` (which unsubscribes the human key). It never blocks.

Paths come from an `INavPathSource`: `OutdoorPathSource(PathPlanner)`, `IndoorOutdoorPathSource(planner, RoomGraph or
LandblockCellSet, lean)`, or `RoomPathSource`. A `NavPath` is waypoints (cell frame and a doorway flag), with status Found,
Horizon or Failed, and the cells it enters. Sources made from a cell set are `INavCellAvoider`s: a cell the physics refuses is
left out and the room graph is rebuilt without it.

**Pursuit.** Pure pursuit with a lead of max(2 m, 0.6 times the speed) in arc length, never past the next doorway vertex;
outdoors the lead is clamped to the farthest route vertex the lattice can see. Forward is held within 60 degrees of error,
otherwise the body turns in place. A turn key starts past 5 degrees and is let go within 1.5 degrees (hysteresis), and the
turn key changes at most every 0.2 s unless the error is past 60 degrees. (Round a corner the bang-bang turn key flickered
17 times a second; with the rule it changes 9 to 11 times at most.) The body walks within 2 m of a stop and within 3 m of a
doorway vertex. Keys are sent only on change: `KeyChanges` and `MaxKeysPerSecond` report the rate.

**Plans.** The runner replans when it is 3 m off the route, and follows the horizon contract above. Every path is confirmed
(`RouteConfirmer`) before a key is pressed. A refusal outdoors scars the sample (1.5 m); a refusal indoors leaves out the cell
the segment was entering. Three refusals end the run Blocked.

**The ladder.** Progress is arc length (in the last 2 m: the arc minus the distance to the end). If progress stays under
0.25 m for 1.5 s with forward held and the body on the ground (or 6 s whatever the keys), that is a rung:

1. back 1 m;
2. sidestep left 1 m, then right 2 m;
3. a charged jump forward (`JumpCharge.FillSeconds`, 1 s);
4. scar the node a metre past the furthest progress (radius 1.5 m) and replan.

Each move gives up after 3 s. Two metres of progress past the point where the ladder began end it. The fourth rung of the
third plan is Blocked, with the message "stuck after 3 plans and 12 rungs; nearest static 0x... in cell ... at (...), d m
away" (`RouteConfirmer.NearestStatic` over the 3 x 3 attached landblocks). The failure reason has the shape of `RouteDriver`'s:
`run '<name>' to <goal> plan <n> rung <n>: <why>; player in cell 0x... at (x, y, z)`.

**States.** Idle; Running; Waiting (`Pause`: keys released, route kept; `Resume` replans if 3 m off); Yielding (the body is
`ServerDriving`: keys released, nothing pressed until it ends); Arrived; ArrivedWithoutSight; Blocked; Interrupted
(`MoveIntents.HumanKey`: every runner key except the one the person pressed is released, because the bindings apply the
person's key before they report it); Failed (over 10 m in one step). An intent that ends DryRun, Refused, Cancelled or
NotImplemented is Blocked, with its summary and kind.

**Arrival.** A point goal arrives within `StopTolerance` (0.5 m; the keys are released) or `PassTolerance` (1 m; the keys are
kept when `Stop` is false), and an indoor goal also within 1.5 m of height. An object goal arrives within its `Range`; then,
when it needs sight, `SweepViewer` (radius 0.3 m) runs from the eye (1.5 m) to the chest (1.0 m above the target point), then
the two tangents (0.5 m either side). That is Arrived, else ArrivedWithoutSight. No physics confirms the sight yet
(TODO(nav-sight-no-physics)).

**Verdict fields** (report-only in their first gate): `route_m= run_s= speed_mps= plans= replans= rungs= blocked=
corrections= where_max_m=`. `corrections` and `where_max_m` come from the `MovementAudit`, and read `none` without one.

**Measured** (offline walker, explore mode, 2026-10-23): Holtburg lifestone to Thelnoth's cellar 0xA9B40111, 166 m in
23.8 s, 0 rungs; to Renald's front room 0xA9B4013F, 57 m in 9.6 s, 0 rungs; to the belt 0xA5B00032, 1,028.5 m in 136.3 s
(7.55 m/s), one plan, 0 rungs, 950 MoveToStates (7 a second, at most 10), 0 corrections; the den's 12 legs, 4 to 55 m each,
0 rungs.

## The confirmer

`RouteConfirmer` (`Movement/Wayfinding/RouteConfirmer.cs`) checks every path against the collision before the runner presses
a key. Every metre it asks `PhysicsWorld.TryGroundZ` with the body's foot sphere, from the previous sample's z (the step rule
is `CTransition::step_down`'s 0.6 m up and 0.6 m down window). A miss tries the half sample (a walkable slope steeper than 0.6 m
a metre), then a probe lowered 1.2 m (a ledge up to 1.8 m). Every 2 m it places the player's `Setup`
(`PhysicsObj.SetPosition(Placement)`, the `TryStand` pattern), 0.05 m above the ground found, at most 0.5 m sideways. Outdoor
samples take the land cell under them; indoor segments take the nearer end's cell. The probe body is a non-player-killer
that is never blocked by the player, and it leaves the world after every call. The goal's last `StopTolerance` is not
confirmed (the floor centre of Thelnoth's cellar is not standable, and the body stops 0.4 m short of it).

**Cost** (Debug, 2026-10-23): 150 m of flat ground takes 1.35 ms (9 microseconds a metre); Holtburg to the belt, 1,016 m, takes
12.8 ms (12.6 microseconds a metre); the shops, 57 to 164 m, take 1.5 to 7.5 ms. Headless confirms at once (`ConfirmAll`,
`ConfirmBudgetMilliseconds` infinite). Godot confirms in slices (`Continue(budgetMs)`, at least one sample a call); the body
stands while a path is pending.

`SweepViewer` is used only for `InSight` and `WallBetween` (radius 0.3, 1 m above the floor).

**TODO(placement-cell-edge).** A placement whose origin lies exactly on a land cell's edge (x or y a multiple of 24) is
refused (Collided) on flat open ground; 0.1 m off is accepted. The confirmer retries 0.1 m off in x and y, counts the retry
(`PlacementNudges`) and logs the TODO once per path. It is not yet traced to retail or to the port (a physics question).

## The headless streamer

Walking a route needs the collision and the navigation map ahead of the body, and the old hunt loaded a fixed 3 x 3 of
landblocks around it. `HeadlessStreamer` (`Movement/Wayfinding/`) replaces that where a route is walked.

- **The corridor.** `CollisionStreamer.SetCorridor(ahead, keepBehind)` takes the landblocks (or cells) in the order of
  travel. They are requested and kept beside the focus 3 x 3, and the streamer's update needs the union of the focus ring,
  the corridor and a trail. A landblock an earlier corridor named and this one does not goes to the trail (the most recently
  dropped first, `keepBehind` long); the others are released like any landblock nothing needs. With no corridor the streamer
  behaves as before. Events: `BlockAttached` and `BlockReleased` (raised only for collision the streamer attached or
  detached), and `Corridor`. The corridor is `AheadLandblocks` = 2 beyond the ring and `KeepBehind` = 1: Lifestone's own values.
- **The hysteresis.** `SetFocus` keeps the old focus while the body is in the edge row or column of the next landblock (one
  land cell). A route hugging a border attached 39 landblocks over a 124 m walk without it, and 9 with it.
- **Workers.** The streamer runs over `WorkerPool(2)` and is pumped once a frame on a 4 ms `FrameBudgetScheduler`
  (`Pump()`). A landblock build runs on a worker; the game thread pays only the attach (0.1 to 2 ms measured) and the tile
  attach (5 to 9 ms in Debug). `EnsureNow(cell)` is the synchronous form, used when the body is placed by a teleport and by
  the laps' first ring; the builds run in parallel on the workers and the call waits.
- **Tiles on attach.** After each attach the landblock's `NavTile` is built on a worker with the 8 neighbours attached at that
  moment, and attached to the shared `NavMap` on the pumping thread (`NavMap` is not thread-safe). A landblock that attaches
  later and has foreign shadows falling in an already-built neighbour rebuilds that neighbour's tile (`TileRebuilds`); a dat
  test proves that every tile then equals a build with all its attached neighbours. On detach, the tile is detached and its
  build cancelled. The neighbours keep the departed block's shadows until it attaches again: conservative, an obstacle that is
  no longer there is avoided, never walked into.
- **Peaks.** The belt run (1,030 m, 33 attaches, 24 releases) peaked at 12 landblocks and 12 tiles, about 21 MB of managed heap
  above the start (the dat caches included). A collision build takes 15 to 40 ms a landblock on a worker; a tile build 5 to 9
  ms; `NavMap.Attach` 5 to 9 ms. The pump stalled 8 to 12 ms in the run.

## The wayfind scenario (the lead's live tool)

```text
dotnet run --project src/Lifestone.Headless -- --scenario=wayfind --to=<cell>[@x,y,z] [--from=<cell>[@x,y,z]] [--wayfind-back] [--account=test-melee|test-archer]
```

One login as the account's fighter (test: Holtburg Hunter; test-melee: Holtburg Brawler; test-archer: Holtburg Archer; a
`--character` is ignored). The start is known: when `--from` is more than 5 m away, one server-mod `place loc` puts the
character there (counted `teleports=1`). The client's own body (`OfflineWalker`) moves on a `HeadlessStreamer` world, with
`Move` bound, a `NavRunner` over `OutdoorPathSource(PathPlanner(map))`, the corridor fed twice a second, a where-am-I at the
start and the end of each leg, then logoff. The run is outdoors only (TODO(wayfind-indoor)).

The lines:

- `wayfind leg=out|back ok= route_m= run_s= speed_mps= plans= replans= rungs= blocked= stuck= corrections= where_max_m= teleports= landblocks_peak=`
  for each leg. A leg is ok when the runner Arrived, the server sent no correction, and the server's where-am-I agrees within
  0.5 m. Report-only in its first gate.
- `wayfind streamer landblocks_peak= tiles_peak= heap_peak_mb= pump_max_ms= attach_max_ms= tile_build_max_ms= tile_attach_max_ms= attaches= releases= tile_builds= tile_rebuilds= tile_failures= step_gap_max_ms=` (report-only).
- `wayfind done ok= legs=`.

The default limit is 180 s plus, for each leg, 1.5 times the straight line at 4.8 m/s plus 300 s, counted from `--from` or the
Holtburg lifestone; `--quit-after` overrides it.

## Open TODOs

| TODO | What is open |
|---|---|
| `TODO(room-steps)` | the room graph does not learn sills: Renald's back room 0xA9B40141 is not walkable on foot; the shop goal is 0xA9B4013F; the runner logs it when it leaves a refused cell out |
| `TODO(placement-cell-edge)` | a placement on a land cell's edge is refused (Collided) on flat ground; not traced to retail or the port |
| `TODO(nav-sight-no-physics)` | an object goal's sight is judged by the sweep only, not by physics |
| `TODO(wayfind-indoor)` | the `wayfind` scenario is outdoors only |
| `TODO(m4-keeper-swap)` | a fighter's keeper casts do not swap the caster in (`needs-swap`); P5b runs them inside a swap |
| `TODO(nav-water-corner)` | water is flagged per land cell (24 m), not per corner; the streamer logs it once per landblock that has water nodes |
| `TODO(hunt-crossing-stall)` | the hunt follower waits for a landblock crossing at most `HuntFollow.CrossingWaitSeconds` while the net pump runs, then builds it at once and logs this |
| `TODO(m4-caster-value)` | the caster's Value is not read |
| `TODO(m4-harm-heal-resist)` | a drain-resistance enchantment above 1.0 scales a harm past its table range (the margin covers a small one) |
