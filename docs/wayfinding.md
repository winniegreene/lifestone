# Wayfinding (M4 polish item P5a)

The wayfinder is how a character walks a planned route over the world's real collision: an A* over a per-landblock
navigation map, a room graph for the buildings, a runner that presses the movement keys through `IActionApi.Move`, and a
streamer that keeps the collision and the map loaded ahead of the body. It is Lifestone's own design, not retail's: retail
has no such layer. The scripted walks (`RoutePlay` and the other scripted walks) use `RouteDriver`, which is kept for them; since M5 item 2 the fight's walks and the journeys' den walk run on `NavRunner` over the ground's room graph (docs/session.md, section 52). The principle is **the map proposes,
the physics confirms.** The planner works on a tile that was built from the collision, so a route is what the map says is
open; before any movement key is pressed the route is checked against the collision (`RouteConfirmer`), and while the body
walks the runner watches where it actually is.

The OpenAC navigation assessment (docs/research/openac-navigation-assessment.md) read OpenAC's navigation files at a pinned commit, for their ideas (clearance, a recovery ladder, defined arrival, the player's keys win). The wayfinder's slices were not given that code, and Lifestone's code ported none of it.

Code: `src/Lifestone.Client/Movement/Wayfinding/` (the tile, the planner, the room graph, the runner, the confirmer, the
streamer) and `src/Lifestone.Client/Movement/Routes/CellRoutePlanner.cs` (the doorway test the room graph shares).
Physics notes: `docs/physics.md` section 16. The keys' binding: `docs/session.md` section 40.

## The tile

`NavTile`: one per landblock, 192 x 192 nodes at 1 m; node (x, y) is centred at landblock-local (x + 0.5, y + 0.5). Per
node: z, flags (Blocked, Slope, Water, Static, Building, DoorwayLane, SteepEdge, shadow), `SlopeCost`, clearance and an owner index,
stored as struct-of-arrays (about 9 bytes a node). `NavTileBuilder.Build(block, options)` is pure and thread-safe (it runs
on a `WorkerPool` worker) and reads only the immutable `LandblockCollision`.

- **Ground.** z is the terrain polygon's plane under the node centre (`PhysLandCell.FindTerrainPoly`, retail
  `CLandCell::find_terrain_poly` 11.6096 0x00533A30, as `RouteLibrary.TryStand` reads it). A node under
  `PhysicsGlobals.FloorZ` (0.664) is Blocked with `SlopeCost` 255. Slope flag from FloorZ to 0.85, with `SlopeCost` falling
  linearly from 255 to 0 in the normal's z (0.85 is Lifestone's own, not retail's). A node with no terrain polygon is
  Blocked and counted; a landblock with no land block record is blocked throughout.
- **Beside steep land.** An open node within 1 m of a too-steep land triangle, a neighbour landblock's included, takes
  `SlopeCost` 255 (`NavBuildOptions.SteepLandKeepOff`, counted `beside_steep=`), and one whose square overlaps such a
  triangle (16 points a quarter metre apart) is flagged SteepEdge (counted `steep_edge=`). Where land triangles meet at a
  corner, a sliver of a steep one can cut into ground the node centres call walkable, and the body wedges against it: it
  cannot step down onto a too-steep triangle however thin. Both are costs, never blocks, so no passage closes and the world
  routes stay valid. Lifestone's own (the world walk's land seams, `docs/milestones/world-walk.md`).
- **Water.** The land cell's `WaterType` (24 m resolution): any water corner flags the whole cell's nodes Water. Water is costed in a partly flooded landblock, never blocked there (retail sinks the body 0.9, 0.45 or 0.1 m, `CalcWaterDepth`, and walks on). A landblock entirely under water is a wall: retail's `CLandCell::find_env_collisions` (11.6096 0x00533C60) collides every body but the camera and missiles, so `NavTileBuilder` (`StampSea`) blocks all its nodes, and the nodes of a neighbouring tile within the body's reach (0.58 m) of it when the neighbour is given (`NavBuildOptions.Neighbours`); the streamer rebuilds those tiles when a sea landblock attaches. The island walks (`IslandRunDatTests`) wade up to 1,083 m through partly flooded landblocks, confirmed by the physics.
- **Statics.** Shapes are taken in retail's order (`CPhysicsObj::FindObjCollisions` 11.6096 0x0050FB20): the physics BSP
  parts' bounding boxes (an oriented rectangle when a part is only turned about the vertical), else cylinder-spheres, else
  spheres. Each is inflated by the body radius 0.48 plus a margin 0.1, so 0.58 m, with rounded corners. A static's top is walkable only as a step: a flat top (a cylinder-sphere's, or a box turned about the vertical only) within the step-up of the ground at a node does not block it, as the body walks up onto it (retail CCylSphere::step_sphere_up, 11.6096 0x0053C070; M6 4 f3: the 0.26 m dais 0x020019E3 under Holtburg's Portal to Town Network was stamped a wall, so no walk reached the portal). Any other static (a sphere, a tilted part, a top over the step-up) still blocks, a named conservatism; the physics confirmation decides. The build line counts the nodes left open (`stepped_nodes=`). Not blocking: a top within 0.05 m of the ground at the node (a decal), an
  underside 2.0 m or more above it (overhead). Skipped and counted: EnvCell statics (the room graph owns them), statics
  wholly below the ground, tiny ones (footprint under 0.3 m and lower than the 0.6 m step-up), and statics no node reaches. A tilted part (one not turned only about the vertical) with physics polygons is asked node by node instead of by its box: spheres of 0.58 m spanning the walking band (0.05 to 2.0 m over the ground), placed against the part's own BSP as retail places a body (`PhysPart.PlacementCollides`: the placement test of `BSPTREE::find_collisions`, 11.6096 0x0053B1A0, with `BSPNODE::sphere_intersects_solid` 0x0053D850 and the centre check), so a rock's overhang above the band and the space between an arch's legs stay open and its inside is blocked. The island's rock arches (0x020007D9) stamp 9 to 14 nodes each where their tilted world box stamped 234 to 600, and the four portals they frame are reachable at 4 m (M6 4 w5).
- **Neighbours' shadows.** `NavBuildOptions.Neighbours`: the streamer passes the attached neighbours and each one's
  foreign shadows that fall inside this landblock are stamped as obstacles.
- **Neighbours' statics reaching over.** A neighbour's outdoor static whose solid, inflated 0.58 m, reaches over the border
  is stamped too, even with no foreign shadow here: the physics lists a shadow only where the static's own sphere enters a
  cell, but the body's sphere reaches the neighbour's cell before its centre does. Statics standing more than 40 m past the
  border are not looked at (`NeighbourStaticReach`), and the stamp clips to this tile, so only the strip within the body's
  reach of the border marks (`reach_over=` in the build line). The world walk found it: a static of 0x521A's reaching 0.07 m
  into 0x5219 stopped every walk heading north there while 0x5219's nodes were open.
- **Buildings.** The footprint is the union of the shell's collision polygons projected on the ground, roofs and eaves
  included, inflated 0.58 m. The bounding box is used only when the shell has no polygons (counted `by_bounds`). Reason:
  Holtburg building 6 (Renald's shop) has a 20 x 18 m bounding box round a footprint a third of that, and with the box
  both doorway lanes were islands. A node the projection blocked, and nothing before it, is opened again where the body stands clear of the shell's own physics (the body's spheres over the walking band placed against the shell, as a tilted static's, `BodyCollides`) and its middle is in none of the building's rooms (M6 4 w6a): a roofed passage through a gatehouse is walkable (Fort Tethana's gate, 0x2581 x 165 to 182, y 83 and 84), the inside of a closed building stays the room graph's. Counted `open_nodes=` (0x2581 706, Holtburg 0xA9B4 236).
- **Doorway lanes.** For each building portal, the entry EnvCell's outside portal polygon (`PhysCellPortal.Vertices`) must
- **Doorway lean (M6 5 W).** A doorway is walked when its normal leans no more than the buildings' doorway lean, 0.3 (`NavBuildOptions.DoorwayLean`, `RoomGraph.BuildingDoorwayLean`, the den's lean), for the buildings and the tile lanes alike; the upright rule's 0.1 was the M6 4 value. The dungeon-entrance caves on the land lean their mouths (environment 0x0D0003F2: the Cave of Alabree's 0xA7B30104, normal Z 0.24; 0x0D0003F4: Holtburg's 0xA9B4017F, 0x0184 and 0x0189, and Shoushi's three, 0.22 to 0.23), so under 0.1 no lane was cut. Holtburg 0xA9B4 now has 19 lanes (the door of 0xA9B40170, normal Z 0.10, joins); its three cave mouths fit but cut no lane on this build (not diagnosed: not on a walk). A scan of the dats' 1,501 landblocks with buildings found 1,232 outside and 1,663 inside portals that lean 0.1 to 0.3 and fit, in 742 landblocks, almost all cave-mouth environments (0x0D0004EC 570, 0x0D000191 528, 0x0D0003F2 276, 0x0D000196 260, 0x0D0004C7 257, 0x0D0003F4 252).
  pass a standing human as `CellRoutePlanner.IsWalkable` probes it (upright within 0.1 of the vertical; 0.2 m and 1.8 m up
  the centre; 1 m up half a body width either side). A passing portal gets a 1.5 m strip from the sill, 2.5 m out along the
  outside direction, sampled every 0.25 m; its nodes lose Blocked, Static and Building (a steep-terrain block stays) and
  gain DoorwayLane. A lane is also cut only when the sill steps down to the ground along the lane's line over the shell's floors (`LaneClimbs`, M6 4 w6a: from the sill outward every 0.25 m the floor under the line is the terrain or a walkable polygon of the shell, the body keeps to the floor nearest its last height within the 0.6 m step-up, and the lane passes once the terrain is within a step within 8 m of the sill; a balcony door over a street gets none, counted `above_ground=`; a door the straight rule refuses keeps a lane when a walk turns aside down to the ground (`LaneStairs`, M6 7 A): a shortest-path search on 0.25 m squares over the floors from just past the sill to the terrain. The floors are the up-facing walkable polygons (normal z at least `PhysicsGlobals.FloorZ`) of the shell and of the outdoor physics-BSP statics within 24 m (`LaneStairReach`) of it, since the stairs are often not the shell's. A square counts when the floor is within the 0.6 m step-up of the last height, there is floor under the body's 0.48 m radius on all four sides (so the walk keeps off a brink and to a flight's middle), and the body's two spheres over the step-up clear those solids. The step off a floor onto the terrain costs 20 squares a metre of step, so the walk leaves a flight by its foot, not its side. The corners are pulled straight, and the lane carries the walk as `NavDoorwayLane.Climb` (outer end on the ground first, a point about every metre, at the floors' heights); `LanePoints` walks it. Only the 1.5 m strip on the ground past the foot, up to 2.5 m, is cut into the tile; the raised part has no nodes, because the tile is the ground. Counted `lanes=N(by_stairs=M)`. A door whose steps run straight along the lane's line but meet the ground past the 2.5 m strip (the world walk's W8: a hut 2 m up on a deck, 77 doors over the dats, Holtburg's 0xA9B40179 among them) is walked the same way, down its floors (`raised_line=`): the straight strip had ended on the deck among the deck's blocked nodes, its points at the ground's height under the body, so no walk joined it and a walk out stood still under its next point. A true balcony has no such walk and is counted `above_ground=` as before. Named limits: a static with no physics BSP (a chair on a deck) is not looked at, and the stairs must lie in the door's landblock; the confirmer refuses a walk through either. A goal back through the same door from a start on the stairs goes down and up again. Lifestone's own rule; no retail planner exists; the physics confirms as before. The 145 lanes this adds over the dats are in session.md section 94. Holtburg 0xA9B4: 20 lanes since the world walk's head-room fix (by_stairs 1, 0xA9B40179); 25 portals are impassable (horizontal hatches, narrow doors, skylights) and skipped, 5 more are above the ground; 0xA9B3's balcony doors 0xA9B30106 and 0xA9B30110 have none.
- **The Refuge's huts (M6 7 A).** Building model 0x01000D67 stands on a deck 18 by 24 m at z 63.60, over ground at 58.00. The two rooms face each other across a covered walk 6 m wide, and the doors (sills 63.60) open onto that walk, so the straight lane runs 6 m to the opposite door and stops. The stairs are five statics 0x02000300 (1.5 m wide, normal z 0.78, 0.8 m rise a metre) leaving the walk at its open end, at right angles to both doors. All six doors get a `Climb` of about 13 m (12.7 and 13.2). Each door also has a Sliding Door weenie 0.025 m inside its sill (landblock_instance 0x7F682000 to 0x7F682005), which the door crossings already take. The offline runs have no doors, so the live rehearsal is their proof. Brighteyes's walks from the Refuge's arrival (44.8 m) and from the Burrow's drop (616.6 m) are pinned (`BrighteyesWalkDatTests`).
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
`FromCellFrame` normalises an outdoor origin past an edge into the neighbour, never an indoor one: an EnvCell's origin is in
its own landblock's frame whatever its value (the Black Spawn Den's rooms lie at y -139 and beyond), and the room graph and
the sight probe read the goal's local origin in the goal cell's frame (M4 4.2 F1: the normalised den corpse's goal sat 192 m
from the corpse and the trip planned 201 m into the rock).

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
2), + 18 more at SlopeCost 255 (`SteepEdgeWeight`), + 40 on a SteepEdge node (`SteepTouchWeight`) + 0.3 per node of clearance under 5 (`ClearanceWeight`, `ClearanceSoftNodes`) + 3 on water (`WaterWeight`)), plus 1 per
metre that a drop exceeds the step-up (`DropWeight`). Every addend is non-negative, so the octile heuristic stays admissible
and open flat ground costs exactly the octile distance. `TieBreak` 1.001 weights the heuristic so that equal-f nodes go
deepest first; a plan may cost up to 0.1 percent over the optimum.

**Pooling.** The node states, the decrease-key open heap, the corridor stamps and the coarse arrays live between plans and
grow only with the map. A warm replan allocates only its result (352 B in Release for a three-vertex route).

**String pulling and the route.** The node path is pulled to vertices by lattice line of sight (0.5 m samples, clearance at
least 1, legal steps between samples), galloping and then binary-searching from each vertex. A pulled segment never crosses
water, a slope, a steep edge or a scar the node path kept off, nor more of the creature layer than the node path between it (below). Each
vertex carries its node's z. Then the route is **straightened** (nav-natural-steering): a vertex goes when the vertex kept
before it sees the one after it by the same rule at clearance 2 (`StraightenMinClearance`), up to four passes. The gallop
stops at the first point it cannot see, and the lattice's sight is not monotone along a winding node path, so the pull left
vertices a straight run does not need; the clearance is one more than the pull's because a small static's collision can
reach past its stamp (TODO(nav-static-footprint)). `NavRoute` exposes the vertices, the horizontal arc length, `PointAt(s)`,
`NearestArcLength(p)` and `DistanceFrom(p)`, which the runner and its 3 m off-route test (`PathPlanner.OffRouteMetres`) use.

**Goals.** A point goal on a blocked or scarred node is `GoalBlocked`; there is no silent snap. An object goal arrives at
any open node within its `Range` (at least 0.75 m, `MinObjectRange`); sight is left to the runner's sweep.

**Scars.** `NavScars` are discs with a life (30 s by default, on an injectable `TimeProvider`). They are the run's own
overlay and are never written into a tile. The planner refuses a step into a scar, except outward from inside the scar the
start stands in.

**The horizon contract.** A goal beyond the attached tiles plans to a horizon goal: the open node on the attached area's
outer edge that the start reaches on the coarse graph and that lies nearest the segment from the start to the goal
(`Status = Horizon`, `HorizonPoint`). The runner walks it and, once `NavMap.Version` has changed and the body is within
60 m of the route's end (`PathPlanner.HorizonReplanMetres`), plans again to the real goal. At a horizon with no new tiles the
body stands and the keys are released; no rung is taken. A room goal works the same way (M6 4 W1, `m6-walk-goal-tile`): when the goal room's landblock has no tile attached, `IndoorOutdoorRoute.Plan` plans the outdoor part toward the room's **land point** (`IndoorOutdoorRoute.LandPoint`: the outer end its door's lane will have, 2.5 m out from the sill along the outward direction, known from the room graph's `ExitsToOutside` alone) as a horizon, and the runner's horizon replan builds the lane and room route once the tile attaches. Of the room graph's exits whose rooms reach the goal, the shortest straight walk (start to land point, then the rooms) is planned toward; the result is always `Horizon` (never an arrival), with `Outdoor` and `Exit` set and no lane, no rooms and nothing inside.

**The tiles still loading (M6 7 N).** A plan that fails `no-path` or `no-horizon` while a landblock on the straight line from the body to the goal has no tile waits: keys up and the route dropped, while the host's corridor feed goes on. Each map change plans again (`why=tiles`). Every tile there and still no way is a genuine no-path at once. The bound is `HorizonWaitSeconds` (30 s), and the failure then names the tiles. The cause seen live was `CollisionStreamer`'s kept corridor landblock (`KeepBehind` 1): it kept the last dropped landblock attached across teleports, so the goal's tile stood alone beside the ring round the Refuge and the planner could not reach it.

**Named failures** (`NavPlanResult.Reason`): start-not-attached, start-blocked, goal-blocked, goal-scarred,
no-open-node-in-range, no-horizon, no-path. The creature layer never fails a plan: it only costs.

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

**Floors at doorways (M6 4 W4).** A portal is walked as a doorway when a human fits through it and the floors either side meet its sill within the step-up, 0.6 m (`HasFloorsAtSill`); a portal no doorway by the fit test is walked when the two cells' walkable floors share an edge on it, opposite each other and within the step-up in height, at least 0.96 m long, with a standing human's column over it in one cell or the other (`FloorCrossingOf`): Tusker Cave's ramp rises through a hole in the floor above (0x5B460113 to 0x114), Tusker Burrow's corridors part along slanted planes over a level floor (0x5748020E to 0x20F, normal Z 0.857), and the den's plazas' leaning doorways are floor crossings too (the den reaches all 12 spawn cells at lean 0). A straight walk inside a room that leaves its floor or runs into a static object goes round through the floor polygons' centres and corners (`FloorDetour`). The physics confirms every floor crossing of the island's dungeons, the den and the Academy's hive (IslandDungeonWalkDatTests).

**Floors below, stairs and pits (M6 4 W6b).** A cell with no floor polygon stands on the floor of the cell under a level portal in its floor when that floor lies within the step-up under it (Tusker Abode's corridor 0x5C4501AE to 0x1B2 over its bridge); a real cell with no physics polygons at all is no floor (the Abode's crossroads 0x1B7 over a 6 m pit). A level portal a stair or ramp climbs through is walked when the physics' ground (the fixed world's step down, which sees the stair's static object) runs from the cell below's floor through the hole to where a body stands in the cell above, each 0.25 m sample within the step-up of the last (`CellRoutePlanner.StairOf`): Holtburg's inner stair 0xA9B30103 to 0x10A (the static Setup 0x020001B4, a ramp of about 46 degrees) and Archmage Cindrue's 0xA9B40117 to 0x11B; the climb's points are walked between the approach and the push. A drop is never walked: a walking body stops at a pit's brink (retail's edge slide, `CTransition::edge_slide`, 11.6096 0x0050BEA0), and the client's port has no fall-damage rule (ACE's `HandleFallingDamage` is the server's), so the Abode's far half is reached by a jump (M6 14 pits: a profile's `jumps`, `CellRoutePlanner.JumpsFrom`, `NavRunner.Jumps.cs`) and its lower level only by a drop, one way (`TODO(m6-abode-lower-level)`).

**A hole over a ramp floor (M6 5 W).** A level portal with no static object under it is probed as a stair when a cell under it has a sloping walkable floor polygon (normal Z from retail's FloorZ 0.664 to under 0.99, `CellRoutePlanner.RampMaxNormalZ`) reaching under the hole: the Cave of Alabree's mouth floor 0xA7B30103 (z 30.12) opens through a hole into the slab 0xA7B30102 (0.12 m), and a second hole onto the ramp floor of 0xA7B30101 (normal Z 0.79, z 30.12 down to 24.80 over 6.9 m), which ends at the portal room 0xA7B30100's doorway. The foot of such a stair is found on the ramp itself: a body standing on a slope rests 0.48 x (1/normal Z - 1) over the plane under its centre (0.13 m on the ramp), `CellRoutePlanner.FootRestOn`. Over a ramp the stair's tops, taken in 0.05 m bands of height (`StairTopBandMetres`), are tried nearest the ramp's centre line first, so the descent runs down the ramp's middle and not off the hole's lip; a static stair keeps the plane's order. The room graph now plans 0xA7B30104, 0103, 0102, 0101 and 0100, and a walking body goes down the ramp into the portal room and out by the mouth (`AlabreeEntryCaveDatTests`). The physics does not yet let the confirmer walk it: see `TODO(m6-ramp-step-down)`.


**Floors a static carries (M6 7 J).** A cell can have no floor polygon and still have a walkable floor, when a static object standing in the cell under its floor portal rises to that portal: the Facility Hub's two towers, static 0x02000623 (part 0x0100189E, the world's own rows: a block 3.9 m deep and 6 m high whose top is the landing, and a ramp running 11.9 m off it, solid to the floor, with raised kerbs), in 0x8A0201C9 under 0x8A020211 and in 0x8A02015F under 0x8A0201C1. Two rules read it, neither a list of cells. A doorway into such a cell has a floor at its sill when the physics finds one within the 1 m step-up past it (`CellRoutePlanner.StaticFloorPoint`; only a floorless cell with a static's origin inside its floor portal's hole is asked, so no other room builds the physics). A ramp that ends past its hole's own cells is a `StaticRun` (`CellRoutePlanner.RunsFrom`): the room graph's edge from the floorless cell to the cell at the ramp's foot and back, whatever cells it passes over, because the doorways of those cells lead under the ramp, not onto it. A run follows the ground in a straight line of probes every 0.25 m (`Follow`, with no stack: each step into the same cell or one its portals join) down to a cell's own floor more than the step-up below the doorway; a level walk across a floorless cell is its doorways' own. Named limits: a ramp that turns is no straight line, so it has no run; a static standing in the floorless cell itself is not read; a body high on a ramp starts in the cell it stands in, so the plan's next point is the doorway's approach under the ramp, and it takes a rung or two (pinned at two) to pass it. The hub's nine portals are walked from the drop (`FacilityHubWalkDatTests`: front walks of 100.8 m to 163.9 m, 19 s to 34 s, rung 0, no wall frame).

**The join with the tile.** `ExitsToOutside` lists the portals that lead outside (`PhysEnvCell.cs:24`,
`CellPortalData.LeadsOutside`) that a human fits through: the cell, the sill point (the polygon's centre at its lowest z) and
the outward direction. `TryJoin(tile, entryCellId, out lane)` finds the tile's `NavDoorwayLane` for the same door: the same
entry cell, its sill within 0.75 m of the exit's sill, the same way out. Holtburg has 24 exits, and all 18 of the tile's
lanes join (the five doors above the ground have no lane). The doorway test of `CellRoutePlanner.Fits` (normal within the limit, then four probes: 0.2 m and 1.8 m above the
sill, and 0.48 m either side at 1 m) is the same test the tile's lane cutter uses outdoors, so a lane exists exactly where
the room graph has an exit a human fits. The lintel probe is the same too: the cutter probes 5 cm under 1.8 m, as the room graph does (`NavBuildOptions.HeadSlack`,
the world walk's W8): it probed at the full 1.8 m, so a door 2.5 m wide and 1.79 m tall, a design found across the dats (0xE74E01A7), was an exit with no lane,
and every walk through it ended no-path though the body walks it both ways. Holtburg 0xA9B4 gains the lane of 0xA9B40177 (20 lanes).

**Doors that open into each other.** Where buildings stand wall to wall (Viamont 0x31D6 and 101 other landblocks over the dats), a door of one opens straight
into a door of the next, with no ground between; each door's lane runs into the other building, so no plan joined the two rooms. Two exits of different cells
whose sills lie within 0.75 m, face opposite ways and stand within the 0.6 m step-up are a pair (`RoomGraph.PairedDoors`), and the room graph walks a pair as a
doorway, from 0.7 m inside the first sill to 0.5 m inside the second. Viamont's largest group of rooms joined on foot grew from 8 to 40
(`RoomGraphDatTests.Two_buildings_doors_that_open_into_each_other_are_walked_as_a_doorway`). Lifestone's own; the physics passes the outdoor cell between the
two portals.

`IndoorOutdoorRoute.Plan(planner, rooms, from, toCell, to)` composes the two graphs. Of the tile's lanes whose door the room
graph has and whose rooms reach the goal, the outdoor `PathPlanner` plans to the lane's outer end (2.5 m out), the lane's
centre line leads in to the sill, and `RoomGraph` plans from the entry cell to the goal; the cheapest whole walk wins. The
result carries `Outdoor`, `Lane`, `Exit`, `LanePoints`, `Rooms`, `Inside` (everything after the outdoor route, as retail
positions) and a status (`Found`, `Horizon`, `NoDoorway`, `OutdoorFailed`) with a constant reason. `NoTile` is gone (M6 4 W1): a missing tile of the rooms' landblock plans a horizon toward the rooms' land point (see the horizon contract); with no tile at all the start is not attached either and the plan fails `start-not-attached` (`OutdoorFailed`). From the
Holtburg lifestone, Renald's shop 0xA9B40141 is reached by the lane into 0x150 (51 m outdoors, cells 150, 13F, 140, 141)
and Thelnoth's cellar 0xA9B40111 by the lane into 0x115 (155 m, cells 115, 111). A start outdoors more than the step-up over the node below it, and within 2.5 m of a climb's point (across and in height), is on the stairs or the deck (`IndoorOutdoorRoute.DownTheStairs`, M6 7 A): the tile is the ground, so the walk goes down that climb to the lane's outer end first and plans from there, for an outdoor goal and a room goal alike. Only a replan or a resume up there meets it.

**The room graph's lean (M6 5 W).** `IndoorOutdoorPathSource` builds its graphs at `RoomGraph.BuildingDoorwayLean` by default (both constructors), so the town walks and the lanes agree; `RoomGraphDatTests` joins Holtburg's 19 lanes at that lean.

**Two landblocks' rooms** (M6 4 W1, closes `cross-landblock-rooms`). `IndoorOutdoorPathSource(planner, goalCells, startCells)` holds the goal's landblock's rooms and the start's of another landblock. A start in the start's rooms walks out as `PlanOut` does (the rooms to a door the tile has a lane for, the sill, the lane out to its outer end), then `IndoorOutdoorRoute.Plan` in to the goal's room over the goal's rooms (a horizon toward their land point while their tile is missing); an outdoor start plans as before over the goal's rooms. `Rooms` is the graph of the rooms the last plan started in (the start's until a plan starts outside them); each plan gives both graphs the creature layer the runner set on `Rooms`; `Avoid` leaves a cell out of its own landblock's graph. Measured on the dats (`GoalTileDatTests`): Holtburg's healer room 0xA9B40111 to the Life apprentice's catalog stop 0xA9B30100 plans as one walk, cells A9B40111 A9B40115 A9B30104 A9B30100, and the physics confirms it; on into his room 0xA9B30102 it plans too and the physics refuses the push onto his counter (`HoltburgScrivenerWalkDatTests`, the reason the catalog has the stop).

**Renald's back room is not walkable.** Renald's back room 0xA9B40141 lies behind the counter. The room graph plans 13F to
140 to 141, but the floor of 140 and its doorways sit one metre above 13F's: the ground probe finds no walkable ground there,
and the placement in 140 is refused as NoValidPosition. With 140 left out the graph plans 145, 143, 142, 141, and the ground
probe refuses the crossing at 142 (139.40, 13.35). Unconfirmed, the walker climbs onto the step at (135.82, 13.93, 94.97) in
13F and is Blocked after 12 rungs. So 0x141 is not walkable on foot. **The shop goal is the front room 0xA9B4013F**, 57 m
from the lifestone, reached in 0 rungs. Since M6 4 W4 the plan refuses it: the thresholds 0x140 and 0x142 sit 1 m over the floors either side, so their doorways fail the sill rule and 0x141 is NoDoorway. A step inside a room stays the runner's to judge (TODO(room-steps)). Since M6 4 W6b a stair or ramp through a level portal is walked too (Cindrue's own cell routes).

**Creatures.** `RoomGraph.Creatures` (a run's `NavCreatures`, set by the runner at each plan; null for a run without a feed) raises the cost of every straight walk of the search (start to approach, approach to push, push to the goal) by its exposure to the discs, with the outdoor weights. `Plan(fromCell, from, toCell, to, ignoreCreature)` leaves the goal's own creature out (`RoomPathSource` passes the goal's guid). Without a layer, or with an empty one, every plan is today's (`IndoorCreatureLayerTests`). A source with a room graph (`RoomPathSource`, `IndoorOutdoorPathSource`) is an `INavRoomSource`: the runner gives its graph the creature layer and runs straight through its rooms.

**Statics by their shape (M6 7 Q).** `CellRoutePlanner.InStatic` and the crossing's `StaticNear` read a static as a `StaticBody`: its origin's disc as before (the origin no more than 0.5 m under the floor nor above a standing human), and one more disc per collision cylinder and sphere of its Setup, by the shape's real height (its top more than a step over the floor, its bottom under a standing human) and its real centre, radius and a body's width (`FloorCornerClearanceMetres`). An origin need not be a foot: the Cave of Alabree's pillars 0x02000205, 0x02000208 and 0x02000209 have a cylinder from 1.87 m under the origin, 3.75 m tall, so the old rule left them out. The discs only add. A crossing's approach stands too (`CrossingStands` samples the walk's first point): the rock 0x020000A5 in 0x016201CF sits on the centre crossing's approach. `RoomGraph.RoomFrame` takes, of floors equal within 1%, the first whose centre no static stands on (`InStatic`, no physics, so the frame builds no world). A larger floor is never passed over: a walk stops short of a centre it cannot stand on.

**Objects of shapes only (the world walk's rooms, W8).** Retail tests an object's physics BSP parts when it has any, else its cylinders, else its spheres (`PhysStaticObject.FindObjCollisions`). So an object with no physics part and a shape taller than the step-up is read by those shapes alone (cylinders if it has any, else spheres), and its Setup radius round the origin is not added: the pillar 0x02000248, a cylinder 3 m in radius and 10 m tall, has a Setup radius of over 5 m, which closed whole 10 m rooms round it, so the detour found no way and the walk was left straight through the pillar. An object with a physics part keeps the rough discs, and low clutter (every shape under the step-up) keeps its radius, because the foot cannot cross it either (0x02000175, a ball 0.15 m across). An end of a detour inside an exact disc (a body beside the pillar, a doorway's point by a ball) first steps straight out of it, at most `StaticEscapeMetres` (1 m), and the steps from that point are probed for ground every 0.25 m (`GroundAlong`), since the floor test sees no wall. Pinned by `RoomStaticShapesDatTests` (0x00AF017A to 0179, 0x00020198 to 019C).

**Objects with physics parts (the world walk's rooms, W8).** For an object with physics BSP parts the planner's disc (its Setup radius or the GfxObj's physics sphere) only bounds the solid, so inside the disc and out to a body's half-width past it (`ProbedReachMetres`) the body itself judges (`StaticBody.Probed`, `FootStands`): the foot finds ground within the step-up and the human Setup 0x02000001 is placed there, as the pre-check's placement probe, moving no more than 0.1 m. A staircase 0x010000D6 rising across a 10 m corridor (env 0x0D0000D8, repeated over 120 landblocks) had a disc covering the whole room, so no detour stood clear of it and the walk ran into the stairs past a lane 1.5 m wide beside them; the body test finds the lane. The whole body matters under a bunk's top board 1.49 m up (0x01060182: the foot fits, the body does not), and the reach past the disc on Holtburg's upper landing (0xA9B3010A), where a 2 m table's edge lay past its disc. A walk's end inside such a disc has no centre to step out from, so the nearest point within 1 m where the body stands is searched ring by ring (`NearestStanding`). The answers are cached per 5 cm. Dryreach's Master Scrivener of Creature Magic is now walked to at his own point, round his counter (`TownNetworkTripDatTests`). A lone GfxObj with physics parts stands in the way by its solid's real height, the vertex bounds placed as it stands, not its origin's (the world walk's building exits): 0x01004DAE in 0x59660204 has its origin 2.3 m over the floor and its solid reaching under it, so the origin rule called it overhead and walks across the room ran into it; 20 room walks are clean for it. A Setup with no radius of its own (`CSetup::radius` 0) is solid only by its parts' physics BSP, and had no disc at all: it is read by the box of its physics parts' vertex bounds in its rest pose, seen from above, as a probed disc (`PartsReach`), when its box reaches no more than 4 m from its centre to a corner (`PartsReachMostMetres`), since a ramp is such a Setup too and the body walks on it (the Facility Hub's 0x02000623, 9.3 m). A staircase in a room is read too: 0x02000396, 5.7 m long and 4.8 m tall, runs along a room's side in a house layout repeated over a hundred landblocks. Inside its disc the foot's probe judges, so walks go round its side. A first rule, at most 3 m and no taller than a standing human, left the stairs out. A bed 3 m long across 0x0043010C's doorway (0x02001051, repeated over many dungeons) had walks run straight across it; with the deeper corners below, 1,715 room walks are clean and 93 worse. Pinned by `RoomStaticShapesDatTests` (0x000C011F to 0120, 0x01060182 to 0184, 0x59660204 to 0205, 0x01EB032E to 032F, the bed in 0x0043010C).

**Ledges (the world walk's rooms, W8).** A cell can have a floor and doorways and hold no body: a plinth 0.5 m wide and 0.5 m high along a room's walls (0x62440115 and 0118, in a layout repeated over hundreds of landblocks). The room graph walked legs onto it, which no body stands on. `CellRoutePlanner.IsLedge`: every walkable floor of the cell is narrower than a body (0.96 m, its smallest extent across any edge), and the foot finds ground within the step-up at none of the points every 0.25 m over them (`LedgeSampleMetres`, the pre-check's probe); a doorway into or out of a ledge is closed. A doorframe cell is as narrow, but its floor holds the foot: probing only each floor's centre called 0x01680137 a ledge, because the mesh 0x01000A69 stands in its mouth with a lane open beside it, and so split rooms a body walks between in three landblocks; the grid keeps it open. The samples keep 0.05 m in from every edge of the floor (`LedgeRimMetres`): at the outer corner of an L-shaped edging round a room's corner (0x00110107 and 0102, env 0x0D000599, repeated over dozens of dungeons) the body balanced with its centre on the floor's diagonal edge, mostly off it, and the edging counted as a room. False without the dats. Pinned by `RoomStaticShapesDatTests`.

**Wide openings (M6 7 G).** A doorway is walked through its polygon's centre unless a static object stands there. `CellRoutePlanner.CrossingOffsets` asks the physics (the same ground samples as the pre-check, gated by a static object of either cell near the centre) and gives the nearest clear crossing either side; `DoorwayPairs` hands both to the room graph's search. The pillar 0x0200020E stands on the portal's centre in the scrivener halls: Holtburg 0xA9B30100 and 0103, Ecutha's lobby 0xA9B40127 and 012A, Stonehold 0x64D50133 and 0136, Fort Tethana 0x2581016D and 0170. `FloorDetour`'s vias must stand (the foot's ground probe), and each floor corner has a second candidate on its bisector, 0.58 m from both edges, and where that one does not stand a third twice as far in, at most halfway to the floor's centre (`FloorCornerDeepMetres`; the world walk's W8: walls thicker than the floor's edge held the foot off every corner of 0x0043010C). A sample refused on a walk inside one room the path entered leaves out that room, not the door after it (`NavRunner.AvoidCellOf`); the Town Network's 0x00070144 is the case, where a crossing beside its stone ring stands and the walk across its central dais does not. Where no chain over the centres and corners walks, points 1 m and 1.5 m round each end of the walk are tried too (`EndRingMetres`), each step to or from one sampled for ground (the world walk's W8: a place in a pocket beside a dais, 0x720201F7); only as a fallback, since taken first they replaced detours that walked. A doorway's push, 0.5 m into the room entered, must find ground every 0.1 m from the doorway, or it is taken at half the distance (`PushStands`; an arch through a thick wall, 0x0287010E). The planner's probe world is one per landblock per structure library (a `ConditionalWeakTable`), because a graph is rebuilt for each cell a run leaves out.
## The runner

`NavRunner` (`Movement/Wayfinding/NavRunner.cs`) acts only through `IActionApi.Move` (Command forward, back and turn;
sidestep; ChargeJump and Jump; `SetRun(run, Automation)`) and reads only `INavBody`, the body as the controller reports it
(`ControllerNavBody` in both hosts). It is stepped: `Start(goal, now)`, then `Step(now)` once a frame after the physics step,
with `Pause()`, `Resume(now)` and `Dispose()` (which unsubscribes the human key). It never blocks.

Paths come from an `INavPathSource`: `OutdoorPathSource(PathPlanner)`, `IndoorOutdoorPathSource(planner, RoomGraph or
LandblockCellSet, lean)`, or `RoomPathSource`. A `NavPath` is waypoints (cell frame and a doorway flag), with status Found,
Horizon or Failed, and the cells it enters. Sources made from a cell set are `INavCellAvoider`s: a cell the physics refuses is
left out and the room graph is rebuilt without it.

With a two-landblock source the runner gives its creature layer to `Rooms` before each plan and reads `Rooms` again after it (M6 4 W2): a plan made from outdoors runs straight in the goal's rooms. Over a composed path planned from inside the start's rooms, `Rooms` is still the start's graph, so in the goal's rooms the indoor straight run aims at the next vertex only (never through a wall, only slower) until a plan starts outside the start's rooms.

**The straight run** (nav-natural-steering; the owner's ruling, 2026-10-24: the characters were always turning, where players ran in straight lines and
turned rarely). Outdoors over the lattice, towards a vertex that is not a doorway or indoors, the body aims at a route
**vertex**, not a point a few metres ahead: the farthest one, from the next up to the first past max(10 m, 3 times the speed)
of arc length, that it may run straight to. Every skipped vertex lies within 2 m of the line, the lattice sees the line at
clearance 2 off water, slope, steep edges and the run's scars, and the line crosses no more of the creature layer than the route does.
Within max(3 m, 0.4 s of running) of its aimed vertex it aims at the next one when it sees it, so the turn is taken at the
corner. No turn key is pressed within 6 degrees of the aim's bearing (`HeadingDeadBandDegrees`), so the body runs straight and
turns at the corners.

**Indoors** (nav-indoor-steering, M5 item 2), over a room path with the body in a room: the same straight run. The body aims at the farthest room vertex, up to the first past the look-ahead, that it may run straight to. Every skipped doorway vertex lies within 0.5 m of the line (`DoorwayPassMetres`, the room graph's push tolerance), so the body still goes through each doorway. The line, at the body's radius (0.48 m: the centre and both sides every 0.5 m, 1 m above the floor), stays inside the body's cell and the cells the route enters between (retail's `CEnvCell::point_in_cell`, as `LandblockCellSet.Contains` tests it). With a `RouteConfirmer`, it runs no further than the physics' sweep reached at plan time (a 0.48 m sphere, 1 m up, from each room vertex to each later one until one is stopped: `PhysicsWorld.SweepViewer`, the confirmer's wall-check transition), and it takes no more of the creature layer than the route does. The turn rules are the outdoor run's (the 6 degree dead-band, the release rule, tracking past 1 m off), and a press also waits while the heading's line passes the aimed vertex within its tolerance (0.5 m; 0.25 m for the goal), since near a vertex a few centimetres off are many degrees. Outdoors towards a doorway it is P5a's pursuit, as before. **Through a doorway at a run** (M5 item 2 fix F6, D47): on a room graph every cell-to-cell portal is a doorway vertex, and the thin-doorway walk within 3 m of each (`WalkNearDoorwayMetres`) made the live round-2 mage walk 52 m of a den leg with no creature near. Indoors the body now runs through a doorway between two rooms (`NavRunner.RunsThroughDoorway`) and walks near it only where the route turns more than 60 degrees there (`WalkSharpTurnDegrees`, the turn-in-place angle) or the physics' sweep from the vertex before it does not reach the vertex after it; the building's door (a neighbour vertex outdoors) keeps the thin-doorway walk. Measured on the dats, 12 legs from the arrival: the den 94 % of the metres walked at 2.56 m/s before, 4 % at 6.03 m/s after; Tusker Temple 97 % at 2.42 m/s before, 10 % at 5.18 m/s after, with no frame of wall contact and no rung (`IndoorRunThroughDatTests`; `IndoorSteeringTests.Doorways_between_rooms_are_run_through_not_walked_near`, `A_sharp_turn_at_a_doorway_is_walked_and_the_doorway_after_it_is_run`). The track passes a doorway vertex up to 1.4 m off at a run (0.5 m walking): the live watch looks for a jamb clipped at a corner.

**In a dungeon the walk near a doorway is half a metre (M6 7 F).** In rooms with no way outside (`RoomGraph.ExitsToOutside` empty, `NavRunner.DungeonRooms`), a doorway the body does not run through cost up to 6 m of walk, 3 m each side. Three things changed there. (1) The body walks only within 0.5 m of such a doorway (`WalkNearDungeonDoorwayMetres`), only before it, and not at all where its straight run aims past the vertex (the line passes it within 0.5 m and the cells and the sweep hold it; a corner in progress does not count as aiming past). (2) That walk lasts through the turn in place (`_doorwayWalk`). A measured fact of the physics: forward let go from a walk stops the body in about 0.3 m, forward let go from a run coasts it about 2 m over the turn, and the run key pressed while forward is up glides it on at run speed with no forward key (the Lodge's leg 0x0283 left its route that way, a replan, before this rule). (3) Within 3 m of such a doorway forward waits for 30 degrees of heading error, not 60 (`TurnInPlaceNearDoorwayDegrees`): with no walk there, a run begun 50 degrees off the aim swung metres wide. A building's door and rooms and the land keep the full 3 m and the 60 degrees, since those passed live. The 2 m walk before a stop is unchanged. A body standing in a dungeon that would run off a turn in place still 60 degrees off its aim first predicts the curve the run sweeps, from its top run speed (`INavBody.RunSpeed`, retail's `get_max_speed`) and the measured turn rate (`NavRunner.CurveClear`): at 7 m/s and 127 degrees a second the curve bulges 1.6 m off the straight line the plan confirmed. While a point of the curve leaves the rooms or meets a static where the straight run the same distance out does not, it turns on in place (`CurveTurns`, logged). The cave of Alabree's tour leg to 0x0162015C, begun facing away beside the pillar 0x02000209, ran into the gap between the pillar and a pot (two rungs) and now arrives clean (`AlabreeCaveWalkDatTests.A_leg_begun_facing_away_beside_the_pillar_turns_before_it_runs`). Buildings are left out: a building's doorway leads off its rooms onto the land, which the rooms do not hold. Measured on the dats (`IndoorRunThroughDatTests`, 144 legs of twelve dungeon profiles at run skill 230), the walked share of all legs falls from 0.189 to 0.039 in the Burrow, 0.030 to 0.016 in the Lodge, 0.220 to 0.066 in the Quarters, 0.171 to 0.076 in the Abode and 0.175 to 0.067 in the Holding. The 127 legs that arrive keep 0 rungs and 0 wall frames; the 17 legs the physics refuses replan as before.

**A body standing still turns before it runs (the world walk's rooms, W8).** On the way to an indoor corner (D134) the runner lets a heading error of up to 15 degrees ride and presses no new turn key in the last 2.5 m, since the body turns in place at the corner. Both rules are for a body already running along the leg. A body that starts a leg standing still, beside a static and facing up to 60 degrees off the leg, ran forward on its old heading and hit the static before it turned. For example, a staircase 0x02000396 runs along a room's side in a house layout repeated over a hundred landblocks; a body 0.33 m from its side, facing 12 or 25 degrees off its first leg, ran into the stairs and needed a recovery move. So the two rules apply only while forward is held, and a standing body takes the ordinary 6 degree dead-band. The stuck-pocket legs of M5 7 P7i, which began inside a corner's approach, now turn by the ordinary press, not by P7i's fallback. Pinned by `RoomStaticShapesDatTests` (0x42AE0109 and 0x46A50109) and `StuckPocketDatTests`.

**Tracking.** A heading is good only to half a physics step (below), about 3.4 degrees, which drifts about 6 m in 100 m, and
the physics confirmed the route's line, not the drift: the confirmer's placements allow only 0.5 m sideways. So while it aims at
the next vertex the body tracks back onto the route: past 1 m off (`CrossTrackMetres`) it aims at the route's point 2.5 s ahead
(never past the vertex) with a 4 degree dead-band, until it is within 0.3 m again. Near a static, where the lattice's clearance at
the body or 6 m ahead on the route is 3 nodes or less (`TightClearanceNodes`), it tracks always, with the pursuit's short lead,
so it keeps within about half a metre of the line. Without it, the belt walk through the streamer drifted 1.2 m off its route
and stuck against static 0x02000258 in 0xA8B3, whose collision reaches past its stamp (TODO(nav-static-footprint)), in about
one run in four. (The item first let a straight run drift to 6 m while it saw its aim; that made the stuck worse and is gone.)

**The turn key.** The body turns only when the physics steps (`PhysicsGlobals.MinQuantum`, 1/30 s; retail's), so a running turn
of about 150 to 230 degrees a second (the runner measures it) moves the heading 5 to 8 degrees a step, and a heading is good to
half a step. A held key is let go when the error is within half a step (`TurnReleaseQuanta`), or once the error has changed
side, and **a release is never held back**. The dead-band is never under three quarters of a step (`DeadBandMinQuanta`), and a
press waits 0.2 s after the last change unless the error is past 60 degrees. P5a held releases back for 0.2 s too: a 0.2 s
press overshot the 5 degree tolerance into the other side's key, 9 to 11 key changes a second round a corner. Forward is held
within 60 degrees of error, otherwise the body turns in place.

**The pursuit** (outdoors towards a doorway vertex: P5a's). Pure pursuit with a lead of max(2 m, 0.6 times the speed) in arc
length, never past the next doorway vertex; outdoors the lead is clamped to the farthest route vertex the lattice can see. A
press needs 5 degrees of error. The body walks within 2 m of a stop and within 3 m of a doorway vertex, half a metre in a dungeon (M6 7 F). Keys are sent only on
change: `KeyChanges`, `MaxKeysPerSecond`, `Turns` (turn keys pressed) and `KeyChangesPerMinute` report the rate.

**Plans.** The runner replans when it is 3 m off the route, when a creature
walks onto the route ahead (below), and follows the horizon contract above; each plan's log line names why (`start`,
`off-route`, `horizon`, `creature`, `refused`, `stuck`). Every path is confirmed (`RouteConfirmer`) before a key is pressed. A
refusal outdoors scars the sample (1.5 m); a refusal indoors leaves out the cell the segment was entering. When the refused sample's way on is the goal's own room, the room it stands in is left out instead (M6 4 w6a: Fort Tethana's Grand Master's east door room 0x2581016D, a step; the plan then takes the west door). Three refusals **in a
row** (no path confirmed between them) end the run Blocked. P5a counted them over the whole run, and the 2 km forest run meets
two at its start (small statics the tile under-stamps, TODO(nav-static-footprint)), so one more anywhere ended it.

**The same plan refused (M6 7 Q).** A replan that reproduces the refused path ends the run at once: Blocked, "the physics refused the path and the replan reproduced it" (`NavRunner.SameWaypoints`, to the centimetre, in `StepConfirm`). The world has not changed, so the same samples would be refused; the live log had the same 35.702 m twice. A doorway's other crossings are already candidates of the search (`DoorwayPairs`), so a plan that stays the same has none left. The cap of three refusals in a row stays for plans that differ.

**The ladder.** Progress is arc length (in the last 2 m: the arc minus the distance to the end). A `Blocked` run is one leg's failure: the fight's and the journeys' den walks recover it (`LegRecovery`; three in a row end the walk; section 52). If progress stays under
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
corrections= where_max_m= turns= key_changes_per_min= creatures_max= creature_replans= creature_min_m=`. `corrections` and
`where_max_m` come from the `MovementAudit`, and read `none` without one; `creature_min_m` (the nearest a hostile creature's centre
came at a refresh, the goal's own left out) reads `none` before one was seen.

**Measured** (offline walker, explore mode, 2026-10-23): Holtburg lifestone to Thelnoth's cellar 0xA9B40111, 166 m in
23.8 s, 0 rungs; to Renald's front room 0xA9B4013F, 57 m in 9.6 s, 0 rungs; to the belt 0xA5B00032, 1,028.5 m in 136.3 s
(7.55 m/s), one plan, 0 rungs, 950 MoveToStates (7 a second, at most 10), 0 corrections; the den's 12 legs, 4 to 55 m each,
0 rungs.

**The straight run, measured** (the same walker through the streamer, 7.5 m/s, 2026-10-24; `HeadlessStreamerDatTests`):

| Walk | P5a's pursuit | The straight run |
|---|---|---|
| Holtburg to the belt 0xA5B00032 | 1,029.5 m, 3 plans, **468 turns**, 415 key changes a minute | 1,018.5 m, 3 plans, **53 turns**, 52 a minute |
| Holtburg to the forest 0x9FB50039 | 1,864.0 m, 6 plans, **806 turns**, 394 a minute | 1,844.5 m, 6 plans, **76 turns**, 42 a minute |

Both arrive with 0 rungs, the same in twenty runs of the class (the straight run before its tracking near statics turned 24 and
39 times, and stuck in about one belt run in four). Live: see m4-plan.md section 8, "the wayfinder's feel".

**A route of one waypoint.** The start and the goal snapped to one lattice point: `Steer` takes the next vertex as `min(1, count - 1)`,
so it never indexes past the list, and the arrival rule ends the run as for any route. A horizon route's one waypoint is its edge, so
that one is steered to. This closes `TODO(m5-nav-one-waypoint)`: before it, the straight-run clause indexed waypoint 1 of a list of one
and threw every frame (the last gate's melee journey wrote 8,642 traces).

**An empty path.** A path with no waypoints is never stored: the plan ends the run Blocked with `no path: empty-path (the path source
returned no waypoints)`, a named failure, not an exception. `Steer` repeats the check and ends Blocked with `empty-path (Steer was given
no waypoints)`; that is unreachable today.

**Indoors, measured.** The den's 12 tour legs (the explore-mode walker at the fighter's run rate, physics confirming; `IndoorSteeringDatTests`): P5a's pursuit makes 205 turns over 305.8 m (0.670 a metre, one every 1.5 m); the straight run makes 40 turns over 294.6 m (0.136 a metre, one every 7.4 m), every leg at most 0.25 a metre, with 0 rungs. A den leg on the runner over the dats (`GroundWalkRecoverTests.A_den_leg_of_the_walk_arrives_on_the_runner_with_its_turns_a_metre_logged`: the first six legs of the den's tour from the arrival, fighter run skill 230) arrives on every leg, with 0 rungs, 82 turns over 647.7 m (0.127 a metre; legs 0.100 to 0.140).

### Predictive steering

The owner, 2026-11-15: walking that looks flawless, indoors and out, with legal physics, "because it's JUST MATH". Legal means the player's own keys only (forward, turn left, turn right, run), through the client's own movement and collision code, with no position ever written to the server; the key timings are computed, not guessed and corrected. `NavRunnerOptions.PredictiveSteering` (`Movement/Wayfinding/NavRunnerPredictive.cs`) is that controller for a dungeon's indoor route. It is on for the fight walks, the headless fight walks and the journeys; the world walk keeps the ordinary steering unless given `--predictive=on` (docs/world-walk.md), since its rooms run is the measure the ordinary steering's fixes are judged by.

**What it knows.** The body's motion, measured from its own moves (`NotePredictMotion`): the physics moves the body once an update (when 1/30 s or more of the host's clock has passed, so an offline walk at 30 steps a second updates every other step, at 60 every second or third); a key pressed between two updates acts for the whole of the next; a held turn key turns at a fixed rate (the walker's run measured at about 134 degrees a second, 9 degrees an update at 1/15 s; standing about 129); forward ramps the speed with a time constant of about 0.1 s, and a run let go coasts about 0.8 m.

**What it does.** After each update it plays the body's motion forward update by update over 1.5 s for each plan of keys it could hold: a turn now for so many updates with forward held or let go; none; a second turn that lines the heading up with the route's leg there or the next; near a stopping goal, forward let go now and the coast. Each plan's cost: distance off the route's line past a 0.4 m band (0.15 m in a stop's last 4 m), the heading off the route ahead at the end, the ground gained, a stop's miss of the goal, and each key change on the way (a reversal, a flip and a stutter cost more). The cheapest plans' tracks are swept at the body's radius (0.48 m) through the route's cells, the statics the room plans go round, and the physics (`PhysicsWorld.SweepViewer`): a block within max(2.5 m, 0.6 s of running) refuses the plan, one beyond costs. A turn now must beat holding on by a margin (no taps); a turn is held to the heading it was planned to; while the body is where the plan put it (0.15 m, a degree), the plan is followed for up to three updates without planning again. The route's line is simplified first (doorway pairs, a staircase's samples, a doorway's jog where the straight line keeps to the cells). Running turns onto the next leg are counted apart (`PredictCornerTurns`), so the D134 counters keep measuring the ordinary steering's turns in place.

**The fallback.** The ordinary steering takes the step, unchanged and every update, when no plan is clear after holding on 0.2 s (`PredictFallbacks`), on a route shorter than 3 m, on a dungeon's route with a leg within 1.2 m of a static the room plans go round (a pot, a pillar, a dais: the plans' discs are not the physics' shapes), outdoors within 8 m of a building's door ahead (`PredictDoorHandOverMetres`: its doorway rules line the body up and walk it through), where the route ahead passes over or under itself or doubles back in a hairpin or a U, while the body is in the air, after a second of no ground gained with forward held (`PredictGaveUp`), and on a runner with no `RouteConfirmer` to sweep with (`PredictHandedSteps`).

**Measured** (2026-11-16, the explore-mode walker at 30 steps a second, physics confirming):

| Bench | Ordinary steering | Predictive steering |
|---|---|---|
| Hubs, 40 legs (`HubWalkDatTests`) | 0 rungs; 1,718 key changes, 245 turn reversals, 208 presses with forward up, 78 forward re-presses, 176 corner turns in place, 697.8 s | 0 rungs; 1,602, 211, 184, 57, 154, 686.7 s |
| The den, 35 legs at run skill 273 (`IndoorSteeringTests`) | 0 rungs; 696 key changes, 125 presses with forward up, 0.0024 flips a metre, 273 s | 0 rungs; 624, 39, 0, 181 s |
| The eleven hunting grounds' every tour leg, 625 legs and 25 km (`IndoorRunThroughDatTests`' legs) | 0 rungs, 0 replans; 12,906 key changes, 1,373 turn reversals, 2,197 presses with forward up, 629 forward re-presses, 202 flips, 4,146 s | 0 rungs, 1 replan; 11,236, 1,002, 1,292, 325, 73, 3,411 s |
| The Cave of Alabree's tour, 16 legs (`AlabreeCaveWalkDatTests`) | 0 rungs; 734 key changes, 124 presses with forward up, 187.7 s | 0 rungs; 748, 127, 187.6 s (its routes run by statics: nearly every step handed over) |

Nine of the eleven grounds beat the ordinary steering on every count. The two caves whose routes mostly run by statics, and so are mostly handed over, are mixed: the Tusker Cavern 1,438 key changes to 1,400, 80 forward re-presses to 60 and 27 flips to 25, but fewer reversals and turns in place and 11 s faster; the Tusker Cave 1,694 key changes to 1,634, 87 forward re-presses to 85 and one replan (leg 0x0108), but 43 fewer reversals, 34 fewer turns in place and 13 s faster. The den's non-corner reversals rise from 0.019 to 0.029 a metre (the gate is 0.039): the controller's small trims onto a leg count there. The world walk's rooms run (`--predictive=on`, 60 landblocks, 24,608 legs): 3 more clean legs, 14 fixed and 11 made worse (rung arrivals in hubs' corridors), so the world walk keeps the ordinary steering. Not retail: Lifestone's own. 

**Outdoors** (2026-11-16, the owner's go-ahead for the land; `NavRunnerOptions.PredictiveOutdoors`, off by default, on for the fight walks and journeys by the owner's call the same day: fewer keys and longer straight runs are what looks human, and a slightly longer open-country run is fine). The same plans over the land's routes, with the land in place of the rooms: off the route's line by more than 0.4 m a track must stand on the land map at the straight run's clearance (2 nodes), off water, slopes and steep edges where the route itself does not cross them, and off the run's scars; within 0.4 m the route's own plan and confirmation stand. There is no physics sweep outdoors: at chest height over rising ground it meets the ground, and the land map stamps the statics by their physics. The free band is the straight run's own drift before it tracks back (1 m), and the window reaches the horizon past the next vertex, since a land leg may be 50 m long. The ordinary steering keeps a start from a standstill (its turn in place to within 60 degrees, then a run that turns the rest), the last 8 m to a building's door (its doorway rules), and a body standing short of its stop; an object goal's coast (a portal's 4 m) aims inside its range so the body is still running when it enters it.

| Outdoor bench (2026-11-16, 60 frames a second, the streamer and the physics confirming) | Ordinary steering | Predictive steering |
|---|---|---|
| Open country: the island's 17 drop-to-portal runs (`IslandRunDatTests`' cases) and the Barracks drop to Tusker Armory (`OutdoorPredictiveDatTests`), 18 legs, 11 km | 0 rungs, 19 replans; 1,552 key changes, 291 reversals, 32 turns in place, 21 forward re-presses, 1 flip, 3 swings, 1,427 s | 0 rungs, 19 replans; 1,168, 94, 22, 17, 1, 0, 1,429 s |
| The outdoor hunting grounds' hunts (the outskirts, the Low B belt, the north forest), 53 legs from Holtburg's lifestone, cell to cell | 0 rungs, 14 replans; 1,650, 254, 73, 26, 4 flips, 11 swings, 1,488 s | 0 rungs, 14 replans; 1,290, 71, 58, 7, 1, 3, 1,468 s |
| Holtburg's streets: every station's walk in and out and the walk-out rings (`HoltburgWalkOutDatTests`), 92 walks | 0 rungs, 20 replans; 4,190, 665, 279, 133, 50, 73 swings, 2,349 s | 0 rungs, 20 replans; 4,094, 624, 281, 133, 50, 74, 2,348 s |

The hunting grounds win on every count. Open country wins on every count but time, 2 s slower in 1,427 s (the horizon replans fall at other places, and the routes come out 4 m longer). The streets are even: most of their walks pass a building's door and stay the ordinary steering's. The owner switched it on for the fight walks and journeys the same day (fewer keys and longer straight runs look human; a slightly longer open-country run is fine). The world walk's outdoor walks are not yet measured with it.

**The polish** (2026-11-16, the owner: walking like a human is critical, and fewer key presses and longer straight runs are the way there). The runner now counts the keys the predictive steering pressed itself (`PredictKeyChanges`), the ground covered (`MovedMetres`) and the ground covered in straight runs of 10 m or more with no turn key (`LongStraightMetres`), all in `SteeringFields`. Counting which hand-over rule pressed the ordinary steering's keys showed where they were: the whole-route rules. A route with one leg within 1.2 m of a static was the ordinary steering's from end to end (7,100 of the benches' 19,800 keys: Holtburg's walks out of its halls, whose indoor pillars made their street runs the ordinary steering's too, the Facility Hub, the Tusker Cave, Cavern and Habitat), and outdoors a route that passed a building's door was too. Four changes:

- **Outdoors no route is handed over for its statics.** The rule now holds in dungeons only; outdoors the land map stamps the statics by their physics.
- **Doors by distance.** Outdoors the ordinary steering takes over 8 m before a building's door, not for the whole route (6 m: a recovery move at Dryreach; the whole window ahead: 1,251 of Holtburg's keys still the ordinary steering's).
- **Top speed through a doorway.** The run's top speed measured indoors (a hall's doorway walk, 5.6 to 5.9 m/s) was carried onto the land, where the body ran 9 to 10 m/s off the porch: a turn planned at the slower speed swung 3 m wide and the runner replanned off-route (Holtburg's Journeyman and Scrivener of Creature Magic, Archmage Cindrue). A cell change between indoors and outdoors now starts the measure again from the body's own run speed.
- **The curve check in buildings** (the ordinary steering's). The walk in now ends at another spot, and the ordinary steering's start from a standstill inside Dryreach's Master Scrivener of Creature Magic's hall ran its turning curve into the counter (two rungs). `CurveClear`, the dungeons' turn in place while a run's curve would meet a static, now runs in buildings too on a runner with `PredictiveOutdoors`.

| All benches, 2026-11-16 (Holtburg's walks, the hubs, the island and the Armory run, the outdoor hunts, the eleven grounds; predictive steering on, as the journeys and fight walks run) | Before the polish | After |
|---|---|---|
| Recovery moves (rungs), replans | 0, 54 | 0, 54 |
| Key changes (a 100 m) | 19,824 (28.5) | 19,160 (27.6) |
| Turn reversals, flips, swings | 2,100, 132, 206 | 1,792, 127, 156 |
| Turns in place, forward re-presses | 1,862, 553 | 1,867, 493 |
| Ground in straight runs of 10 m or more | 61 % | 64 % |

Holtburg's walks carry it: 4,556 keys to 3,832, reversals 721 to 408, swings 88 to 40, forward re-presses 147 to 85, straight runs 71 to 80 % of the ground; 6 more turns in place and 3 s slower in 2,518. The dungeons are unchanged; the island's runs vary by some 40 keys from run to run (the confirmer's time budget), and the outdoor hunts are even.

**Tried and set aside** (the numbers for the next step):

- **Driving the dungeons' routes by statics.** With the rule gone in dungeons too, the Hub went 1,602 keys to 1,324, the Tusker Cave 1,694 to 1,472, the Cavern 1,438 to 1,248, the Habitat 1,098 to 914, with fewer reversals and turns in place (all benches: 18,178 keys, 1,649 reversals, 135 swings). Handing over only the stretch near a static was worse than either steering alone, as the rule's note said. But driving them is fragile: the Cavern's leg 0x0221 gained a replan, and once the turn bug below was fixed, a corner cut 1.5 m past the confirmed line ran into a static the sweeps did not see (Tusker Cave 0x011F, a rung). Next: a static test by the physics' shapes, the band held to the confirmed line on a leg by statics, and a stop with forward held handed over at once.
- **A turn past 180 degrees.** The plans' longest turn is half a circle at the slower rate, so at the faster rate it reaches 184 to 200 degrees; the held turn's end is measured the short way round, so such a turn is let go after one update. Counting the turn down by the heading's steps and capping it at 180 degrees fixed the Cavern's U (0x0221: 94 keys to 68, no replan) but, with the static rule gone, let the body spin round in the Cavern's pots and later cut the Tusker Cave corner above. With the rule kept, the cap moved the dungeons both ways; it waits for the static work.
- **Dearer keys.** Doubling the key weight (with reversals, flips and stutters) cut another 5 to 8 % of the keys, but a 7 % nudge of the off-line weight then gave one to three recovery moves where the weights here give none.

**Shopping walks and the Town Network** (2026-11-16, the owner watching Qwinnie live: spinning while shopping, running into walls in the hub). A bench replays the live run's legs as she walks them, chained: the buff bot to Shopkeeper Renald the Elder, on to Archmage Cindrue, Monyra the Jeweler and back, out to the Network portal; the Network's arrival to the Facility Hub, Uziz and Refuge fronts and back; the Facility Hub's arrival to the Quarters front and back. Its keys, turns and swings match the live log's (the hub leg 24 keys to the live 22).

- **Wall touches are logged.** The physics' own step now records what stopped it, with the normal (`PhysicsObj.LastTransitionObstacle`, as `TryTransitionTo` did); the body reads it (`INavBody.Contact`, `ContactNormal`), and the runner counts and logs each touch of a wall or an object while it moves the body (`the body touched a wall`, with the place, heading, keys and which steering held them), with `short_taps=` (turn presses under 0.1 s), `wall_touches=`, `object_touches=` and `contact_s=` in `SteeringFields`, now printed on every `run ended` line. Report-only.
- **A run starts only where its turn reaches the way ahead** (`RunStartReaches`, indoors). A body that runs and turns follows a circle of radius speed over turn rate (some 4 m), and a point inside it is never reached: from the Network's arrival facing away, the start at 60 degrees off ran round the doorway 4.3 m ahead, turned 240 degrees, stopped and turned back. The run now waits, turning in place, until the next vertex at 2.5 m or more lies outside that circle with half a metre to spare. Nearer than 2.5 m the old start stands: in a cramped shop the rule asked for a heading within a few degrees and the body shuffled to a recovery move (Ecutha the Tailor's corner).

| Bench, 2026-11-16 | Before | After |
|---|---|---|
| The live legs (16): keys, wall touches, swings, seconds | 422, 17, 5, 198 | 398, 13, 4, 192 |
| All benches (15 sets): rungs, replans, keys | 0, 54, 19,160 | 0, 53, 18,944 |
| All benches: reversals, forward re-presses, swings, seconds | 1,792, 493, 156, 9,467 | 1,733, 416, 153, 9,464 |

The Holtburg walks swing 25 times where they swung 40; the Cavern and the Habitat swing more (24 and 9, from 13 and 7).

**Found and left for the next step** (the bench's numbers):

- **The pedestal in the Network's corridor** (0x00070144): a round room with a dais and a pedestal 3.5 m across between two doorways split by a post, each lane a body's width. The room plan's vertex beside the pedestal stands at the body's contact distance (a probed static: the foot stands there), so a run on the line grazes it; most of the hub's touches are there and at the doorways' posts. A margin from statics where the room allows is the planner's (the world walk's) to weigh.
- **The run key changed as forward goes up glides the body on.** Approaching Renald's door from the street, forward went up 1 m short of the corner as the doorway's walk began, and the body glided 2.5 m on at a walk with no forward key (into the door's frame on the way back); with the run key left as it was, the coast was 0.8 m and no touch. Holding the run key while forward is up fixed that door but moved two fragile Holtburg starts to recovery moves (the Journeyman Scrivener of Creature Magic's stair head, the tailor's corner), and in the dungeons it undid the Lodge's doorway walk (the Facility Hub's ramp top, two rungs). Letting forward go a coast short of a sharp corner, with no turn key held while coasting (a turn held through the coast carries the body 2.3 m from 9 m/s), cleaned the door but took the tailor's start to a recovery move.
- **One-update taps.** At 60 updates a second a third of the predictive steering's turn presses last one update (17 ms, about 2 degrees). A shortest press of 0.07 to 0.15 s in the plans raised the keys 7 to 16 % and the reversals with them; the plans steer by those taps.

## The creature layer (nav-creature-avoid)

The owner's ruling, 2026-10-24: the characters seemed unaware of the enemies and ran right into them. `NavCreatures`
(`Movement/Wayfinding/NavCreatures.cs`) is a run's own layer of the hostile creatures the client sees, refreshed as they move
and never written into a tile (as the scars are not).

- **Which creatures.** `HostileCreatureFeed` reads the session's object table: the creature item type (0x10), the attackable
  bit (0x10), neither the player (0x8) nor the vendor (0x200) bit, a position, and not known dead. That is the radar's red blip
  and the census's creature. The run's own goal creature (a hunt's seek names it as the object goal) is left out, so the hunt
  still walks to its target. ACE alerts only an idle creature without a move-exclude tolerance (`AlertMonster`,
  Creature_Combat.cs:1121-1151); the client sees neither, so every hostile creature counts.
- **The disc.** ACE @95ef426d wakes a monster when the player comes within its `VisualAwarenessRange` (`Player.CheckMonsters`,
  Player_Monster.cs:17-31, as the player moves), measured between the two cylinders (`get_distance_sq_to_object(obj, true)`,
  PhysicsObj.cs:2519) and scaled by `mob_awareness_range` (1.0; the bundled shard's row is 1). The client is not sent the
  property, so `CreatureAwareness` pins it for the 74 classes of the two hunt grounds' tables (a read-only SELECT of the
  bundled world database, 2026-10-24: 8 m for a Blood Shreth to 35 m for a Shadow Wisp, every one of them set). Any other
  creature takes ACE's default, 18 m (`VisualAwarenessRange_Default`, Monster_Awareness.cs:377, "the most common value from
  retail"). The disc's radius is the awareness plus the player's 0.48 m plus the creature's own radius. Its radius is the creature's Setup radius (retail `CSetup::radius`) times its scale (the physics description's ObjScale), as the physics object's is (`HostileCreatureFeed.SetupRadii(dats)`, cached per Setup). A creature whose Setup cannot be read is assumed 1 m, logged once; a host that gives the feed no Setup lookup logs one warning and every aggro disc assumes 1 m (every host passes one; section 52).
  **The kind's own range** (M6 16 E1f, docs/automation-engine.md E1). The bestiary's behaviour table holds every creature kind's own
  `VisualAwarenessRange` from the world database. Of the 47 kinds the ground profiles name, 37 have a range that is not the 18 m
  they are read at: 30 notice farther (to 30 m) and 7 less far (`CreatureBehaviourTests`). `--walk-awareness=pinned|own` (read by the
  session, listed with the autopilot's options; default `own` since M6 16 Q3) says which range the disc uses: the kind's own, or the pinned one, as above
  (`Bestiary.OwnAwarenessMetres`: the table's range times `mob_awareness_range`; the pin, else the default, for a weenie class
  the tables do not hold; the belt's three cattle are rows from M6 16 E1e on, and their pin is the table's 22 m). The setting and its report are the session's `WalkAwareness`, on the object table
  every feed is built over. In both modes it decides nothing and reports: once a session for each kind seen whose two ranges
  differ, `awareness-shadow: <name> (class N): the walk keeps R1 m, its own range gives R2 m`; and, for a travel leg's guarded
  walk, once at its end when the layer saw a creature inside its own disc and outside the pinned one,
  `awareness-shadow: the <leg> leg to <label>: <n> creature(s) passed inside their own range and outside the pinned radius, <m>
  of them among her attackers; closest ... mode= passed= attackers= closest_m= session_legs= session_passed= session_attackers=`
  (the attackers are the run past's own read of the leg's attack watch). A larger disc is the same rule with a bigger number
  (`WalkAwarenessTests`): the route goes farther round it in open ground, a creature in the only gap costs more and is still
  passed, and no plan fails or waits for it.
- **The cost.** The planner stamps the layer per plan (`PathPlanner.Creatures`, which the runner sets at each plan). A step into
  a disc costs 1 more a metre at its edge, rising linearly to 5 at the creature, and overlapping discs add. Going round a disc
  in open ground is at most pi/2 of the chord, so the planner always goes round when it can, and a creature in a gap costs the
  route but never fails it. A pulled or straightened segment never crosses more of the layer than the node path between its
  ends (0.5 m of slack), and the runner's straight lines obey the same rule. Every weight is Lifestone's own.
- **Moving creatures.** With a feed (both hosts: `HeadlessBody.NewRunner`, `GodotJourneyRunnerHost.NewRunner`), the runner
  refreshes the layer every 0.5 s within 120 m. It replans, at most once every 2 s, when a creature the last plan did not know
  there (new, or moved over 3 m) has a disc that meets the route's next 60 m. A creature whose disc already holds the body is not
  a reason: it has noticed the body.
- **Indoors** (nav-creatures-indoor, closed in M5 item 2). Over a room source the runner refreshes the layer as outdoors (0.5 s within 120 m), gives it to the room graph at each plan, and replans for a creature walking onto the route ahead (at most once every 2 s); the goal's own creature is left out. Indoors, going round is another doorway sequence, taken when its extra length is less than the exposure it saves. Measured on the den's dats: the leg 0x0320 to 0x0303 with a creature of the default 18 m awareness in 0x0308 goes round through 0x031B to 0x0300 (28.2 m, 2 turns, 0 rungs), the creature's centre never nearer than 6.0 m.
- **NPC bodies** (M6 4 W2, closes `m6-npc-in-door-lane`). The creature layer counts hostile creatures only, and the confirmer checks the fixed world only, so a plan ran through an NPC's body: live (M6 item 1's climb 3) the walks to Holtburg's Life and Creature apprentices stuck 5.5 m before the front door 0x7A9B3005 against the Scrivener of Item Magic 0x7A9B301B, 0.96 m from her centre. `NpcBodyFeed` (`Movement/Wayfinding/NpcBodyFeed.cs`) reads the object table's other creatures: the creature item type, placed, not a player, not ethereal (PhysicsState 0x4), not known dead, and not hostile by `HostileCreatureFeed.IsHostile` (not attackable, or a vendor). Each is a `NavBody`: its centre, its own radius (its Setup's radius times its scale, `HostileCreatureFeed.SetupRadii`; 1 m logged once when unread), its height, and whether it stands outdoors. With `NavRunner.BodyFeed` set (both journey hosts: `GodotJourneyRunnerHost.NewRunner`, `HeadlessJourneyRunnerHost`'s leg body) and a lattice, each plan (`NpcBodies`): (1) reads the bodies within max(120 m, the goal's distance plus 30 m), leaving out the request's own object (a walk to an NPC still reaches her), a body whose disc holds the goal point (the walk ends at its stand), a body in a room (`TODO(m6-npc-indoor-bodies)`, logged once a run) and a body more than 1.5 m over or under the lattice's ground at its feet (a balcony over a street); (2) stamps those within 30 m of the straight line to the goal as blocked discs on the run's scars (`NavScars.SetBodies`: never expired, replaced whole at each plan; the timed scars' `All`, `LiveCount` and `Expire` do not count them): the walker's 0.48 m plus the body's own radius (1.16 m for the human Setup 0x02000001, radius 0.68); (3) checks the path against every body read: a path segment that starts outdoors and passes a body's disc by more than 0.2 m (`MeetSlackMetres`: the Setup's bounding radius over the human cylinder's 0.48) meets it, unless the start stands inside that disc. A body met and not stamped stamps the bodies within 30 m of the path and plans again (at most 3 plans); (4) a body the plan cannot go round (met though stamped: a doorway lane, a gap narrower than the body and its disc; or a plan the bodies closed whose plan without them meets it) is waited for 3 s (`LaneWaitSeconds`) with every key released, then planned once more (`why=npc-in-lane`); still there, the run ends Blocked `no path: npc-in-lane:0x<guid>: ...` naming her, her place and her disc. The walker never pushes. The bodies are read at each plan only: an NPC who walks onto the route after it is met by the stuck ladder, as before. Each plan with bodies near logs one line (`seen= stamped= at_goal= off_level= indoor= plans=`). Measured on the dats (`HoltburgScrivenerWalkDatTests`, her body standing in the walker's physics as a server object at her landblock_instance point): without the bodies the walk to the Life apprentice's stop sticks against her 0.96 m from her centre (3 plans, 12 rungs, Blocked, as live); with them it is one plan round her (nearest 1.05 m, her disc 1.16 m), arriving 1.97 m from the Life apprentice. A creature whose Setup the lookup cannot read is not a body (the physics has no shape for it): it is logged once as `npc-setup-unread` (the npc, its guid and Setup), and no lookup, or no Setup id, keeps the 1 m disc and its warning (M6 4 f3: Holtburg's creature-typed 'Bottle', ACE weenie 40526). Tests: `NpcBodyTests`. The fight walks (`FightWalk`, `GameFightWalk`) and the wayfind scenario's legs set the feed too (M6 4 w6a).

- **The storey band** (M5 item 2, fix F2; review 2-R6). A creature has a height, `NavCreature.Z`: its feet's z in the landblock's frame, NaN when unknown (a creature of unknown height counts at every height). Indoors a creature counts for a route only within half a storey of the route's height: `NavCreatures.StoreyBandMetres` (3 m) either side, half of `StoreyHeightMetres` (6 m, the stacked storeys of the den and the Tusker Temple), so a creature a storey above or below costs nothing and never forces a replan (`NavCreatures.SameStorey`). `RoomGraph.Exposure` and `CostAt(Vector2, float, uint)` apply the band at each sample's height, interpolated along a ramp, so a ramp's lower end sees a creature on the lower storey. The replan test (`NavRunner.RouteMeets`) applies it to a segment between two indoor waypoints; any other segment, outdoor or the doorway between, keeps the flat disc. Outdoors nothing changes: `PathPlanner` and `CanRunStraightTo` use the overloads that take no height. Not changed: `NavCreatures.Nearest` and the report-only `creature_min_m` still measure in 2D, and `Refresh` and `Fill` still list the creatures of every storey (`creatures_max` counts them). The band is Lifestone's own soft-cost choice, not ACE's: ACE measures awareness between the two cylinders in three dimensions (`PhysicsObj.cs:2519` @95ef426d), so by ACE's arithmetic a creature 6 m below an 18 m awareness would notice through the floor. It stays UNCERTAIN until the live turns-per-metre measure says whether it helps. Tests: `IndoorCreatureLayerTests.The_room_graph_costs_a_creature_only_on_the_routes_storey`, `IndoorCreatureLayerTests.The_exposure_of_a_walk_counts_the_creatures_within_the_band_at_each_samples_height` (with a ramp), `IndoorCreatureLayerTests.The_runner_replans_for_a_creature_on_the_routes_storey_not_for_one_above_or_below`, `NavCreatureTests.The_feed_gives_each_creature_its_height_for_a_room_routes_storey_band`.

Tests: `IndoorCreatureLayerTests` (no layer: today's plans; a route round a creature; a creature in the only doorway; the goal creature left out; one replan over the rooms; a warm indoor runner with a layer allocating nothing), `IndoorCreatureLayerDatTests` (the den leg round a tusker; the Setup radii from the dats), `IndoorSteeringTests` and `IndoorSteeringDatTests` (the corridor with a bend, the leaning plazas at lean 0.3, the den's legs), `NavCreatureTests`: the cost, a route going round a creature it would have crossed, the goal creature left out, a creature in
the only gap, the feed's rule, the pinned awareness, a creature walking onto the route ahead (one replan, the body outside its disc),
no replan for a creature the plan knew, and a warm runner with a layer allocating nothing a step.

## The confirmer

`RouteConfirmer` (`Movement/Wayfinding/RouteConfirmer.cs`) checks every path against the collision before the runner presses
a key. Since M5 item 5 F3 it checks the fixed world only (`PhysicsObj.FixedWorldOnly`, `PhysicsWorld.TryFixedGroundZ`): server objects in the player's world (doors, NPCs, creatures) are met by the runner while it walks, not by the planner (docs/session.md §55). Since M6 4 W2 an outdoor NPC's body is on the plan as a blocked disc ("The creature layer", NPC bodies); the confirmer still does not check it. Every metre it asks `PhysicsWorld.TryGroundZ` with the body's foot sphere, from the previous sample's z (the step rule
is `CTransition::step_down`'s 0.6 m up and 0.6 m down window). A miss tries the half sample (a walkable slope steeper than 0.6 m
a metre), then a probe lowered 1.2 m (a ledge up to 1.8 m). Every 2 m it places the player's `Setup`
(`PhysicsObj.SetPosition(Placement)`, the `TryStand` pattern), 0.05 m above the ground found, at most 0.5 m sideways. Outdoor
samples take the land cell under them; indoor segments take the nearer end's cell. The probe body is a non-player-killer
that is never blocked by the player, and it leaves the world after every call. The goal's last `StopTolerance` is not
confirmed (the floor centre of Thelnoth's cellar is not standable, and the body stops 0.4 m short of it).

**Stop points and statics (M5 item 7, `m5-renald-shop-floor`, D120).** A profile's vendor `stop_point` must stand where the confirmer's probes stand: on
the cell's floor through its statics, at the point and over the whole stop tolerance round it, because the body stops anywhere within that ring.
A floor polygon under the point is not enough: Renald's first stop (135.6, 15.8) lay on a floor polygon of his front room 0xA9B4013F among three
barrels (Setup 0x020000A5) beside his counter, and the ground probe refused the sample a metre before it, three plans in a row, while the mage stood
at the lifestone (the soak of 2026-10-29). The stop moved to the clear floor by the door, (133.5, 15.5); `VendorStopConfirmDatTests` (Exhaustive) confirms
the planned leg from the lifestone to every den vendor and the stop ring through the statics, and runs the same check over Holtburg's other vendors.
A vendor's own point is the other case: since item 5 his body stands on it in the player's physics world, where the confirmer does not look (F3),
so a point goal there with the 0.5 m tolerance jams against him and ends stuck (round 10, Thelnoth's cellar). The resupply station's point request
to a same-cell vendor not yet in view stops `ShopLeg.UnseenVendorReachMetres` (1.6 m) short of his point; a vendor in view keeps his object goal.

**Cost** (Debug, 2026-10-23): 150 m of flat ground takes 1.35 ms (9 microseconds a metre); Holtburg to the belt, 1,016 m, takes
12.8 ms (12.6 microseconds a metre); the shops, 57 to 164 m, take 1.5 to 7.5 ms. Headless confirms at once (`ConfirmAll`,
`ConfirmBudgetMilliseconds` infinite). Godot confirms in slices (`Continue(budgetMs)`, at least one sample a call); the body
stands while a path is pending.

`SweepViewer` is used only for `InSight` and `WallBetween` (radius 0.3, 1 m above the floor).

**TODO(placement-cell-edge).** A placement whose origin lies exactly on a land cell's edge (x or y a multiple of 24) is
refused (Collided) on flat open ground; 0.1 m off is accepted. The confirmer retries 0.1 m off in x and y, counts the retry
(`PlacementNudges`) and logs the TODO once per path. Traced in the port (the world walk, 2026-11-10): a point on the line is
inside both land cells, the cell search keeps the last one holding it (the west or south one), and a placement without Slide
must end in the cell it was asked for, which the caller took as the east or north one, so it is refused. With Slide it is
placed, in the west or south cell. Whether retail picks the same cell is not checked. The walks' stalls near the lines are
a different cause (steep slivers, `NavFlags.SteepEdge`).

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
  later rebuilds every attached neighbour's tile (`TileRebuilds`): its shadows, sea wall, too-steep land near the border and
  statics reaching over all change that tile, and a filter that skipped the tiles it cannot change would risk a stale one. A
  dat test proves that every tile then equals a build with all its attached neighbours. The cost: up to 8 tile builds of a few
  milliseconds each per attach, on the workers; the world walk's long walks took about 35% longer a start, mostly these rebuilds; an attach batch still rebuilds a shared neighbour once per attach, and coalescing them is open. On detach, the tile is detached and its
  build cancelled. The neighbours keep the departed block's shadows until it attaches again: conservative, an obstacle that is
  no longer there is avoided, never walked into.
- **Peaks.** The belt run (1,030 m, 33 attaches, 24 releases) peaked at 12 landblocks and 12 tiles, about 21 MB of managed heap
  above the start (the dat caches included). A collision build takes 15 to 40 ms a landblock on a worker; a tile build 5 to 9
  ms; `NavMap.Attach` 5 to 9 ms. The pump stalled 8 to 12 ms in the run.
- **Long runs** (M6 4 W5). The island's 2,796 m Refuge to Holding run is planned in 8 plans (7 horizon replans, 66 ms in all, 12 ms at most), 2,870 m walked, 380 simulated seconds.
- **Fort Tethana, measured** (M6 4 W1, `GoalTileDatTests`). From the lifestone 0x27800006 with only its 3 x 3 attached, the walk to Zisaha's room 0x24810102 (three landblocks west) is two horizons (to 0x26810001 (0.5, 20.5), then 0x25810004 (0.5, 88.5)), each confirmed, then the whole route in, cells 24810101 24810100 24810102, confirmed; 17 attaches, 8 releases, never more than 9 landblocks.

## The wayfind scenario (the lead's live tool)

```text
dotnet run --project src/Lifestone.Headless -- --scenario=wayfind --to=<cell>[@x,y,z] [--from=<cell>[@x,y,z]] [--wayfind-back] [--account=test-melee|test-archer]
```

One login as the account's fighter (test: Holtburg Hunter; test-melee: Holtburg Brawler; test-archer: Holtburg Archer; a
`--character` is ignored). The start is known: when `--from` is more than 5 m away, one server-mod `place loc` puts the
character there (counted `teleports=1`). The client's own body (`OfflineWalker`) moves on a `HeadlessStreamer` world, with
`Move` bound, a `NavRunner` over `OutdoorPathSource(PathPlanner(map))`, the corridor fed twice a second, a where-am-I at the
start and the end of each leg, then logoff. An end may be indoors (`--to`, `--from`, and the start the back leg runs to). An indoor end needs a position (`--to=0xA9B4013F@x,y,z`; the scenario does not guess a room's floor: `TODO(wayfind-indoor-default)`). The leg's path source is the journeys' own, `JourneyLegPlanner.SourceFor(bodyCell, goalCell, map)`: the outdoor planner when both ends are outdoors, the dungeon ground's room graph when an end is in one, else `IndoorOutdoorPathSource` over the landblock's room graph with its exit route out of a room. The goal is `NavGoal.ToPoint(point, cell)`, with the room as its cell when the end is indoors. A start in a room the graph cannot leave ends the leg Blocked, named `indoor-start-unreachable` (`TODO(room-steps)`: Renald's back room 0xA9B40141); the shop's goal is the front room 0xA9B4013F. A login indoors with no `--from` is no longer refused: the body is placed where the server has it and the leg plans from that cell.

The lines:

- `wayfind leg=out|back ok= route_m= run_s= speed_mps= plans= replans= rungs= blocked= stuck= corrections= where_start_m= where_end_m= where_max_m= settle_s= teleports= landblocks_peak= turns= key_changes_per_min=`
  for each leg. A leg is ok when the runner Arrived, the server sent no correction, and the server's where-am-I agrees within
  0.5 m. Report-only in its first gate.
- `wayfind streamer landblocks_peak= tiles_peak= heap_peak_mb= pump_max_ms= attach_max_ms= tile_build_max_ms= tile_attach_max_ms= attaches= releases= tile_builds= tile_rebuilds= tile_failures= step_gap_max_ms=` (report-only).
- `wayfind done ok= legs=`.

The default limit is 180 s plus, for each leg, 1.5 times the straight line at 4.8 m/s plus 300 s, counted from `--from` or the
Holtburg lifestone; ``--quit-after`` overrides it.

Indoors, measured (the scripted server over the dats, `WayfindScenarioTests.An_indoor_end_is_planned_through_the_room_graph_and_arrived_in_its_room_and_the_back_leg_leaves_by_the_door_M5_2_k1`): the Holtburg lifestone (0xA9B40019, at 84, 7.7, 94) to Renald's front room 0xA9B4013F at its room frame: out 56.2 m in 11.4 s, 0 rungs, 0 corrections, the server's last position on the shop floor (z 94); back with `--wayfind-back` 58.4 m in 14.7 s, 0 rungs, ending outdoors at the lifestone. The scripted body runs at the scenario's run skill (about 4.8 m/s), so its times are slower than the fighters' rate.

## The journeys' planner, the settle gate and the indoor exit (M4 P5b J5b, F1)

A journey's legs on foot go through one planner for both hosts, `JourneyLegPlanner` (`Session/Scenarios/Journey`, in Lifestone.Client): it gives the
goal, the path source (`SourceFor`), arrival, the failure name and whether the body moved. `JourneyRunLeg` runs a leg over `IJourneyLegBody`: the
headless body (`HeadlessBody`, shared with the wayfind scenario) or the play's player. The two hosts differ only in the body and in where collision and
tiles come from (the Godot host feeds the nav tiles from the player's own CollisionStreamer; the headless streamer keeps its external-collision form).

- **The settle gate (Godot).** A leg waits before it plans until the player is present, the body is within 25 m of the server's position, and
  `HeadlessStreamer.IsFocusSettled` holds over the play's CollisionStreamer and tile feeder (`JourneySettleGate`). The wait is at most 120 s, then the leg
  fails `travel:run:not-settled:<cell>: not ready within 120 s (<reason>)`; the reasons are `not-present`, `not-placed:<cell>` and
  `focus-not-settled:<cell>`. A leg's start no longer reports `no-tile` or `start-not-attached`.
- **Failure names match in both hosts:** `travel:run:<state>: <reason>` (interrupted, blocked, arrivedwithoutsight, failed),
  `travel:run:indoor-start-unreachable:<cell>`, `travel:run:moved` and `travel:run:no-path-source` (no den graph). `ArrivedWithoutSight` counts as an
  arrival when the request does not need sight.
- **Object goals are confirmed to their range (F1).** A station's object goal (a vendor, a chest, a portal) is arrived at only when the body is within
  its use range of the object, not at the nearest planned node.
- **The indoor exit route (F1).** A body that starts inside a building leaves by the route the indoor source plans to its door, and only then the
  outdoor planner takes over (`IndoorOutdoorPathSource`). An outdoor point or object goal plans to its landblock id, so `SourceFor` gives the
  indoor-outdoor source over that landblock, which hands a goal with no room to the outdoor planner. A room start and a room goal in two landblocks (M6 4 W1) take both landblocks' rooms (`IndoorOutdoorPathSource(planner, goalCells, startCells)`), with the choice named `indoor-outdoor over two landblocks' rooms: ...` on the leg's line. `indoor-start-other-landblock` now names only a source made for one landblock and asked from another.

- **The rooms' memo and the worker (M6 14 hub plan).** Each leg builds its own source and room graph, and before this every pass through the Town Network
  (0x0007) worked the same doorways and physics probes again: the first walk's plan held the game's thread 7.6 s, then 4.5 s (live 2026-11-16 16:54:52 to
  16:55:04 UTC; measured offline: 1,128 floor detours, 15,592 ground probes and 10,359 straight-step probes a plan; the 34,560-node nav tile took 0.2 s). The
  leg planner keeps one `CellRoutePlanner.Memo` a landblock (`JourneyLegPlanner.RoomsMemoOf`): the doorway tests, the probes and the bare detours, the answers of
  the cells, lean and head slack alone, so a later leg starts from them (a graph that leaves cells out is other cells, with a memo of its own). A walk with kept
  portals reuses the bare detour when none of its steps is refused, else searches again. A host with workers (`IJourneyLegBody.OffThread`, the Godot host's
  pool) works a leg's rooms out on a worker first (`JourneyLegPlanner.RoomsWarmUp`, for a leg from a room to a room of one landblock), logging
  `the leg's rooms are worked out on a worker before it plans` and `... were worked out on a worker ms=`; a server move meanwhile fails the leg
  `travel:run:moved`. The routes are the ones a graph of its own plans (`HubFirstPlanDatTests`). The slow-frame and soak lines now measure the wall time between
  frames (`FrameWallClock`): the engine's delta read 130.9 ms over the 12 s hold.

The resupply's way out (M6 1 L3, `ResupplyPlanner.NeedsWayOut`) is the fallback since M6 4 W1: the composed walk goes first; when it fails and the home lifestone is within one landblock of the goal, the station walks out to the lifestone, then in again, its line naming the composed walk's failure.

## Doors on the routes (M5 item 5 d1, d2)

A closed door on a route is a stop, not a wall. `DoorCrossings` (`Movement/Wayfinding/DoorCrossings.cs`) reads the doors of the session's object table: a `NavDoor` has the guid, the position, the extent (the Setup's selection sphere radius times the scale: 0.89 m for the Aluvian house door, Setup 0x020019FF, not the 1 m fallback), Open (the Ethereal bit of its PhysicsState) and Locked (from its property update).

`DoorCrossings.Map` joins a door to the nearest sill within its extent: a tile lane, a room-graph exit, or a room-to-room portal that fits. `StepsOf` finds the first path segment that crosses the sill plane within the extent (`CrossArc`), and the runner stops at `CrossArc - DoorStandOffMetres` (1.3 m: the walker's radius plus 0.5 m, inside ACE's 2 m use radius).

`NavRunner` takes the doors through `INavDoorFeed` (`SessionDoorFeed`), which `JourneyRunLeg`, the fight walks and `WayfindScenario` set on every leg's runner. At a door it logs `purpose=door`, stops, sends the Use and waits, keys released, for the door's Ethereal state, then walks on. One more Use follows after `DoorRetrySeconds` (2 s; a door still animating ignores the first); no open within `DoorOpenSeconds` (5 s) ends the run Blocked `door-closed-timeout`. A door that closes again mid-crossing is Used once more, then the same failure. A door the server says is locked is blocked at the plan (`door-locked`), and a refused Use (the kill switch, a dry run) is `door-use-refused`. With a feed the verdict gains `door_steps= door_uses= doors_opened= doors_found_open=`. `INavDoorFeed.Version` is a number that moves when the set of doors changes; a feed that never changes its doors leaves it 0 (`SessionDoorFeed.Version`, F2).

Sight: an object goal whose sight line passes a closed door's doorway ends `ArrivedWithoutSight` ("closed door 0x.. stands between"). That closes the door half of `TODO(nav-sight-no-physics)`; the statics half stays open.

Locks: a CreateObject carries no lock (ACE `WorldObject_Networking.cs:845-892` @95ef426d), so Locked is known only from a property update, and a door never appraised reads as unlocked (`TODO(door-locked-unknown)`). ACE opens a locked door used from behind (`Door.cs:104-107`), which the runner does not use (`TODO(door-locked-behind)`). A locked door is not routed round (`TODO(door-locked-replan)`).

Extents: `DoorCrossings.SetupExtents(dats)` is built once per host (headless and game) and returned as `IJourneyLegBody.SetupExtent`, closing `TODO(door-extent-host)` (d2); the fight walks and the death leg's walk take it too (F2).

Station line: the journey's `vendor` station and `VendorFactsRun` print `door_opened=yes|no` on the vendor line, after `vendor_frame_max_ms=`, report-only. It is yes only for a door the walk itself Used that then gained Ethereal while the watch was live (F2, 5-R2); a trip by place prints nothing.

The den's two doors (0x70105004 and 0x70105005, impregnable and locked) sit in no-go cell 0x104 and map to no doorway, and Tusker Temple has no door (d1). Tests: `DoorHostTests`, `DoorFeedTests`, `WayfindScenarioTests`, `WindowFramesStationTests`.

A door whose CreateObject says it cannot be used (Usable No) is never Used: the run ends Blocked `door-lever` naming it, at the plan or at its stop (M6 4 w6a; Fort Tethana's gate door 0x72581042, opened only by its lever 0x72581044, TODO(door-lever)). ACE would open it on a Use (`Door.cs:86-101`), which retail's player cannot do.

## Open TODOs

| TODO | What is open |
|---|---|
| `TODO(room-steps)` | a step inside a room (not at a doorway) is judged by the runner's physics only; doorways' sills are checked since M6 4 W4, stairs through level portals are walked since W6b, and since M6 14 (the guard task) a doorway whose sill stands over the room's floor is walked down static steps the physics follows from the sill (Shoichi's street door in Lin, 0xDA3B0109 over 0xDA3B0118: `CellRoutePlanner`'s steps down past a doorway). Renald's back room 0xA9B40141 is refused at the plan; the shop goal is 0xA9B4013F. Doors do not change it (d1): the den's two doors map to no doorway, and the room-to-room test uses a placeholder door |
| `TODO(wayfind-indoor-default)` | an indoor end of the `wayfind` scenario needs a position (`--to=<cell>@x,y,z`); the scenario does not guess a room's floor |
| `TODO(placement-cell-edge)` | a placement on a land cell's edge is refused (Collided) on flat ground; not traced to retail or the port |
| `TODO(nav-sight-no-physics)` | an object goal's sight is judged by the sweep only, not by physics. The door half is closed (d1): a closed door in the sight line is `ArrivedWithoutSight`; the statics half is open |
| `TODO(door-locked-unknown)` | a CreateObject carries no lock, so a door is read as unlocked until a property update says otherwise (ACE `WorldObject_Networking.cs:845-892`) |
| `TODO(door-locked-behind)` | ACE opens a locked door used from behind (`Door.cs:104-107`); the runner does not use it |
| `TODO(door-locked-replan)` | a locked door is blocked at the plan rather than routed round; the planner does not yet plan round a locked door |
| `TODO(hunt-crossing-stall)` | the hunt follower waits for a landblock crossing at most `HuntFollow.CrossingWaitSeconds` while the net pump runs, then builds it at once and logs this |
| `TODO(m4-caster-value)` | the caster's Value is not read |
| `TODO(m4-harm-heal-resist)` | a drain-resistance enchantment above 1.0 scales a harm past its table range (the margin covers a small one) |
| `TODO(nav-static-footprint)` | some small statics collide about a metre past their stamp (0x02000246 in 0xA3B4, 0x020005C9 in 0xA6B4 on the forest route): the confirmer refuses a line at clearance 1 beside them; the straightening keeps clearance 2 and refusals Block only three in a row. A yaw-only physics part is still its oriented box (a tilted one is asked by its physics since M6 4 w5) |
| `TODO(m6-npc-indoor-bodies)` | an NPC body in a room is not on the plan (the room graph knows no bodies); the runner logs it once a run. The vendors' stops before their counters keep the walks off them |
| `TODO(m6-abode-pits)` | Tusker Abode and the Holding (one layout), M6 14 pits: the upper level's east half (166 floored cells) and west half (139) are joined by a running jump over the pit 0x2A9 (10 m brink to brink): the profiles' `jumps` section, the room graph's jump edge (`CellRoutePlanner.JumpsFrom`, taken only within the body's flight, `JumpReach`) and the runner's jump (`NavRunner.Jumps.cs`: line up, run with the bar filling, held at the brink by the edge slide, let go on a full bar; checked after: `jump-short`, `jump-fell`, `jump-bar`, `jump-reach`). Proven offline (`PitJumpDatTests`); open: the live jump (ACE's acceptance of the flight, the landing, the client's buffed run beside the server's) and the stub for any pit no profile names |
| `TODO(m6-abode-lower-level)` | the Abode's and the Holding's lower level (230 cells, the exit portal, 59 and 81 slots) is reached from the upper level only by a drop off the pit's brink onto the bridge 0x10C at z -12 (a walking jump, measured in M6 4 W6b; a running jump that falls short hits the far wall and ends on the pit floor at -18 beside the bridge, 15 cells joined to nothing), one way: hunting it needs a tour that drops in and leaves by the exit portal (which sets her down beside the entry) |
| `TODO(m6-abode-pit-fall)` | a jump that falls into the Abode's or the Holding's pit ends the run `jump-fell` on the pit floor (z -18, 0x...010F or 0x...0110: 15 cells joined to nothing) and the fight walk then fails by name; that the autopilot recalls (Lifestone Recall) and comes back to the ground is not proven offline |
| `TODO(m6-neydisa-network-front)` | from Neydisa's drop 0x95D60033 the outdoor plan finds no path to either front of the castle's Portal to Town Network (73.93, 74.99) in 0x95D6001C (not diagnosed: a wall or a building stamp); pinned by `TownNetworkWalkDatTests` |
| `TODO(m6-runner-sweep-rooms-frame)` | the runner's room sweeps after a plan (`NavRunner.SweepRooms`, the sight table between a route's room vertices) take 65 to 85 ms on the Facility Hub's 119-waypoint walks from the drop, in the frame of the runner's first plan (measured offline, `HubFirstPlanDatTests`); the plan itself takes 16 ms |
| `TODO(door-lever)` | a door opened only by a linked lever or switch (Usable No) stops the walk by name; the walk does not work the lever (Fort Tethana's gate 0x72581042, lever 0x72581044) |
| `TODO(m6-ramp-step-down)` | the physics' ground probe misses the Cave of Alabree's ramp. One step down of the 1.2 m window from the mouth's cell 0xA7B30103 leaves the sphere wholly past the 0.12 m slab 0xA7B30102, so the cell search (`CEnvCell::find_transit_cells`, 11.6096 0x0052D430) from 0x0103 never reaches the ramp's cell 0xA7B30101 and `CTransition::check_other_cells` finds no cell (proved by instrumenting it). A second cause: retail's no-Slide placement refuses a stand found in another cell than the one it started in (`CPhysicsObj::CheckPositionInternal`, 11.6096 0x00512960), lifted on the ramp under the holes. So the confirmer refuses the walk into the cave; the planner and the walker are not the cause. A work-in-progress fix passes the walk but breaks Archmage Cindrue's stair (0xA9B40117), so it is not committed (`cache/m6/5/w2-ramp-wip.patch`). Pinned by `TownNetworkWalkDatTests` (`alabree`), which goes green when it is fixed; `TODO(m6-alabree-entry-walk)` waits on the same pin. Placed item 9 (D62): a physics change in Lifestone.Data |
