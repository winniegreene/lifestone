# OpenAC's navigation layer: port, re-express or leave (research for M5)

Status: superseded: Lifestone writes its own (D33-D36); see docs/wayfinding.md.

Read 2026-10-23, read-only, at OpenAC f22b048 (`f22b04851c5fcc3f38acfe50fb704ca38130c081`, MIT; `vendor-eval/OpenAC`,
read with `git show`, nothing checked out, built or run) against Lifestone master 51d1db7 and the in-flight P4 branch
`m4/p4` (e7fc4a9 and its merged lanes). Status: done; the M5 planner reads §5 and §4.3. The rules this page works under
are [foundation-openac.md](foundation-openac.md) §6.2 (the independence rule) and
[openac-m4-map.md](openac-m4-map.md) §3 (what is not taken); foundation-openac.md §6.3 already named navigation "a port
candidate at automation phase A1, after a read". This is that read.

**Verdict in one line.** Port the planner (`AcDream.Core/Navigation`: the grid, the router, the leaps), re-express the
capture that feeds it and a slim walk layer over Lifestone's own physics and `RouteDriver`, and leave the rest (the
plugin contract, the chat commands, the overlay, MossTank). About 7% of a week for the part the hunt needs, about 10%
with doors, following and leaps. Do it in M5 only if M5 walks the hunt (no `place loc` hops) or walks a dungeon other
than BSD; recorded routes alone do not need it (§5).

## 1. What OpenAC has

OpenAC's `docs/navigation.md` (read at the pin; `vendor-eval/` is gitignored) describes the layers. Line counts at
f22b048:

| Layer | Files | Lines | What it does |
|---|---|---|---|
| Planning (`AcDream.Core/Navigation`) | `NavGrid.cs` 1,497, `NavRoute.cs` 1,405 (`NavRouter`), `NavLeaps.cs` 1,655, `NavColumnTiles.cs` 64 | 4,621 | Plain C# with no client state. The collision is rasterised into 0.25 m columns of solid spans; a node is the top of a walkable span with headroom for the body; links join neighbouring columns within step-up and step-down height; a node is "clear" when no wall comes nearer than the body's radius less a quarter column and it is not on a ledge's edge. A* (three plans in parallel, from shortest to well clear of walls, the tidiest kept; 4,000,000 expansions at most), corners moved up to 1.5 m to keep 1 m from walls, routes that end in sight of the goal, keep-out spots, creatures to pass, and leaps solved from the character's jump and run skill only when no walk arrives. |
| Capture (`AcDream.Core/Navigation`) | `NavGeometry.cs` | 616 | Copies a region's fixed collision on the physics thread (terrain, cell polygons, building shells, placed objects, server objects a body stands on) so the grid builds on a worker; fingerprints the objects so a grid knows when it is stale. |
| Walks (`AcDream.Runtime`) | `NavigationWalkController.cs` 3,162 (+ `.Preview` 123), `RuntimeNavigationWalkSources.cs` 714, `SealedDungeonCells.cs` 28 | 4,027 | Locates the goal, captures, builds and searches off-thread, collects the route next tick, hands it to a driver; the recovery ladder, doors, crowds, waiting, staged far goals, following, narration. |
| Driving (`AcDream.Runtime/Gameplay`) | `RuntimeRouteDriver.cs` 1,160, `RuntimeScriptedMovement.cs` 528 | 1,688 | Legs to scripted moves on three channels (travel, strafe, turn), lead-point steering, running corners, standing leaps. |
| Plugin surface | `Plugin.Abstractions/NavigationAutomation.cs` 728, `RuntimeNavigationAutomation*.cs` 1,019, `NavigationChatCommands.cs` 876, `IScopedNavigationSource.cs` 18 | 2,641 | The contract, ownership between plugins, `/nav` and `/motor`. |
| Client | `NavigationWalkFramePhase.cs` 33, `NavMeshDebugOverlay.cs` 162 | 195 | Ticks walks before the player frame; draws the grid and route. |

The tests are 74 `[Fact]`/`[Theory]` in `NavGridTests.cs` (1,545 lines, shapes built in code) and 2,674 lines of walk
controller tests over a simulated body, plus installed-dat corpora (never imported: §6.2 rule 4).

## 2. What it does that Lifestone's routes don't (question 1)

Lifestone's walking today: `RouteDriver` (317 lines; physics.md §14) turns toward the next goal, holds WalkForward,
and reports `Stuck` after 3 s without 5 cm of progress; `CellRoutePlanner` is a breadth-first search over one
landblock's cell portals with two waypoints per doorway; `DenPlanner` (in `FightWalk.cs`) is the same search over BSD's
floor cells that also accepts the plazas' leaning doorways; `RouteLibrary` has the Holtburg ring and the Academy exit;
`DeathApproach` is one straight leg to the lifestone. The hunt walks nowhere itself: it `place loc`s to its ground's
cells and (P4 seek) beside a sought creature, and lets ACE's MoveToObject carry the melee approach, which
`HuntFollow`'s body follows in the client's physics.

| Capability | Lifestone today | OpenAC |
|---|---|---|
| Outdoors among trees, buildings and rocks | Nothing plans outdoors. `RouteDriver` goes straight at the bearing and ends `Stuck` at a wall (the `wall-bump` check proves exactly that). The hunt teleports; ACE's MoveTo is a straight line (ACE has no pathing). | One grid holds terrain, scenery, statics, building shells and rooms; routes go round trees and through doorways in one search. |
| Steps, stairs, levels | Indoors only, by cell portals (upright doorways; `DenPlanner` adds the 11-degree lean); the controller climbs the stairs (`stairs-up-chase`). Waypoints sit at doorway centres, so a room's furniture is not seen. | Step links from the body's step-up and step-down heights; stairs, ramps, roofs, dungeon levels and object tops are one graph; a place goal "stands on the floor under it" by a defined rule. |
| Doors | Not modelled; no route uses a door. | A closed door on the route is used like a click and the walk goes on once it is open; a locked one (appraised quietly) is routed round, or ends the walk `Blocked` naming it. |
| Leaps | Scripted jumps for checks only (`JumpLeg`, `RoutePlay.Jump`); nothing plans one. | Leaps from the character's own jump and run skill and the physics step, aimed to come to rest 0.5 m from an edge with slack for power, heading and takeoff; a route jumps only when no walk arrives. |
| Being stuck | `Stuck` ends the leg; the scenario fails or moves on. `holtburg-ring-stall` is a guard row. | A recovery ladder before replanning: back 1 m, the roomiest spot within 2.5 m, a sidestep each way, a hop; keep-out spots for the first stops, dropped when they leave no route; `Blocked` only after six plans, naming the object beside the spot. |
| Creatures and players in the way | Not considered. | Passed round where there is room; a hostile one planned round at once; one that stops the walk gets two chances to move. |
| Arrival | Within a tolerance of a point. | Defined per goal: beside an object and in sight of its side (`Arrived`), else the nearest spot that sees it (with a reason), else `ArrivedWithoutSight`; a bot can trust "arrived". |
| Following | None. (`HuntFollow` is the client's own body following the server's view of the player, not a follow of another character.) | Follows a player by replanning toward where they are now; copies portals and jumps; holds behind them; never ends by itself. |
| Far goals | Teleports. | Staged grids (96 to 320 m a side outdoors, a sealed dungeon in one grid up to 2,048 m). |
| Giving way to the player | The scenarios own the keys; no "the player's keys end a walk" rule is stated in physics.md §14. | Every frame of the player's movement input ends any walk or move at once, in every state (planning, waiting, settling). |
| Waiting while busy | The loops sequence it themselves. | `PauseGoToWhile`: a walk stops while a fight, cast or buff needs the character and replans 1.5 s after. |
| Headless | Yes: `DenWalk` and `HuntFollow` run an `OfflineWalker` in a headless `PhysicsWorld`. Parity, not a gap. | Yes, the same objects in both hosts, with a grid-parity test between them. |
| Narration | The leg's failure reason. | `/nav debug` narrates enough to replay a report (start, inputs, every point and leap, each stop and the walls' distance). |

## 3. What it would close (question 2)

From m3-plan.md §9, m4-plan.md §9 and the P4 branch's new names:

| TODO | What it is | With navigation |
|---|---|---|
| `m4-seek-terrain-only` (P4) | The seek's `place loc` point is chosen from terrain only; a building, tree, static or the creature can stand on it. | **Closes**: a clear grid node at the weapon's distance is standable by construction; better, the seek walks there and needs no `place loc`. |
| `m4-missile-clear-line` (P4) | The missile pick checks no line; three environment misses in a row walk on. | **Closes**: the router's goal is a clear node that sees the target (eye 1.6 m to target 1.2 m against the grid's walls), so the archer walks to a spot with a line instead of shooting a trunk three times. The sight test is the grid's approximation; Lifestone can confirm it with its own sweep (`HasClearLine`), which the hunt then holds. |
| `m3-fight-planner-lean` | `CellRoutePlanner` refuses BSD's leaning doorways; `DenPlanner` is a second search. | **Closes**: the grid has no notion of doorway planes; `DenPlanner` and its BFS go. |
| `m3-fight-left-floor` | A guard: the walker left the den's floor. | **Closes the cause class**: legs stay on clear nodes with wall clearance; the guard stays. |
| `holtburg-ring-stall`, `physics-thin-doorway` | The walker's rare stall; a thin doorway. | **Mostly**: the recovery ladder; a doorway narrower than the body plus a quarter column can still look walkable (OpenAC's own known limit). |
| `m4-hunt-follow-collision`, `m4-hunt-follow-unplaced`, the live 0x003D retry of a long melee approach | ACE's MoveTo cap (15 m) and the body that follows it. | **Partly**: the client walks to within the cap and lets ACE's MoveTo finish, so a long approach never asks ACE for more than it does; the two loud paths stay. |
| `m3-bind-approach`, `m3-lifestone-radius` | The bind's straight leg. | **Partly**: an object goal that ends in sight of its side replaces `DeathApproach`; the radius stays a placeholder until the CreateObject carries it. |
| `m3-fight-walk-audit` | The fight's walks are not audited against ACE. | **No**: a separate check; a walk layer should be built with `MovementAudit` from the start so it closes alongside. |
| `m4-hunt-frame-target-hidden`, `m4-hunt-frame-camera` (the target "in a tree", or 0.1 m from the player) | The review frames' camera rule. | **No**: these are the camera's framing rule, not the walk. The grid's sight query could help the camera choose a side, but nothing here moves the camera. A target at 0.1 m is ACE's melee approach, which navigation leaves alone. |
| automation-vision.md §1.3 stuck recovery, §1.4 B (pathfinding inside dungeons), S1's blocked waypoint, goals.md's "AFK dungeon" | Requirements, not TODOs. | **Covers** the planner and the recovery ladder; S1's "stuck recovery, then a meta failure state" maps onto `Blocked` after the ladder. |

## 4. The port plan (question 3)

### 4.1 File by file

The planning files depend on very little: `NavGrid` uses `AcDream.Core.Physics` only for `TerrainSurface`
(`SampleSurfacePolygon`, `IsEntirelyWater`) and `PhysicsGlobals.FloorZ`; `NavRoute` and `NavLeaps` depend on nothing
outside the folder (`NavLeapPhysics` takes its step, elasticity and friction as parameters); `NavGeometry` is the one
file that reads OpenAC's `PhysicsEngine`, `ShadowObjectRegistry`, `CellSurface`, `PhysicsDataCache` and `GfxObjPhysics`.

| OpenAC file | Class | Onto Lifestone | Notes |
|---|---|---|---|
| `NavColumnTiles.cs` | **port** | as is | A tiled 2-D store; no dependencies. |
| `NavGrid.cs` | **port** | `TerrainSurface` becomes a small Lifestone-owned terrain sampler over `PhysLandCell.Polygons` / `FindTerrainPoly` (surface z and normal at a point) and the landblock's `WaterType` for the open-sea rule; `PhysicsGlobals.FloorZ` is Lifestone's own (`Lifestone.Data/Physics/PhysicsGlobals.cs`, with its retail cite); `NavBody.Player` takes the player's radius and height from its Setup and the step heights from the controller (physics.md §12), not OpenAC's `DefaultPlayerBody`. | The span, clearance, ledge and sight constants are OpenAC's tuning, not retail's; they keep OpenAC's names in the port header's change list. |
| `NavRoute.cs` (`NavRouter`, `NavRoute`, `NavAvoidance`) | **port** | as is | `Parallel.For` over the three plans: each plan is independent, so the result does not depend on scheduling (R5); tests call it synchronously. `Stopwatch` feeds only the report's milliseconds. |
| `NavLeaps.cs` | **port** (with N5) | `NavLeapPhysics(step, elasticity, friction)` is filled from Lifestone's `PhysicsGlobals` and the controller's quantum, each with the retail cite Lifestone's physics already carries. | The finder can be ported with N1 and left unused until N5. |
| `NavGeometry.cs` | **re-express** | A `NavCapture` over `PhysicsWorld.GetLandblock(coord)` → `LandblockCollision`: `LandCells` → the terrain sampler; `EnvCells[*].Physics` (`CellStructPhysics` polygons, cell frame applied) → cell triangles; `Buildings[*].Part` (`GfxObjShape`, placed) → shell triangles; `StaticObjects[*]` (parts' physics polygons; `CylSpheres` and `Spheres` → `NavCylinder`, domes for spheres; scenery is already in `StaticObjects`) and `ForeignShadows` → object triangles and cylinders; the server's objects from `PhysicsWorld.Objects` with their fingerprint. | Its shape is OpenAC's, its every read is Lifestone's; it is the adapter, so it is written, not copied. About 400-600 lines. |
| `NavigationWalkController.cs`, `RuntimeNavigationWalkSources.cs`, `SealedDungeonCells.cs` | **reference, re-express** (§6.2 rule 4: `AcDream.Runtime` is not taken as code) | A slim `NavWalk` in `Lifestone.Client/Movement` that drives through `IPlayerMovementIntents` (the human's path, automation-vision.md R2): plan off-thread and collect next tick, legs with lead-point steering (an extension of `RouteDriver`), the recovery ladder, keep-out spots, the player's keys win, a `Waiting` state the loops raise, sequence-numbered reports, the arrival rules of §2. Doors, crowds and follow come in N5. | Design decisions taken as the acceptance list (foundation-openac.md §6.3). About 1,200-1,800 lines, against OpenAC's 4,000, because there is no plugin ownership, no `/nav`, no narration file. |
| `RuntimeRouteDriver.cs`, `RuntimeScriptedMovement.cs` | **reference** | `RouteDriver` gains the lead point (2.5 m ahead plus 2.75 m per metre off the leg), the running-corner test and the standing-leap sequence (N5). | The three-channel motor is a plugin convenience; Lifestone's scenarios already press keys. |
| Plugin contract, `RuntimeNavigationAutomation*`, `NavigationChatCommands.cs`, `IScopedNavigationSource.cs`, MossTank | **not taken** | none | No plugins, ever (automation-vision.md; openac-m4-map.md §3). A Lifestone debug option may later show the grid; it is an option in `OptionCatalog`, not `/nav`. |
| `NavMeshDebugOverlay.cs`, `NavigationWalkFramePhase.cs` | **reference** | A Godot overlay in `Lifestone.Game` if wanted (green clear nodes, red edges, the route line); the walk is ticked before the player's step, as the scenarios already do. | Presentation only. |

### 4.2 Licence, headers and checks

- **Licence.** MIT (`Copyright (c) 2026 Erik Nihlén and OpenAC contributors`); the four ported files carry the port
  header (repository, `f22b048…`, path, MIT, the change list) and are listed on a new `docs/third-party/navigation.md`
  with the MIT text, beside net.md's rows. The re-expressed capture and walk files cite the OpenAC files they were read
  from, as re-expressions do.
- **Decompile check** (§6.2 item 3), run here on all five `AcDream.Core/Navigation` files at f22b048 with
  `grep -nEi "offset|decompil|0x00[4-7]|Retail"`: `NavGeometry.cs` matches only a local `offset` vector (a landblock's
  origin) and a loop index; `NavGrid.cs` a local `offset` in a segment test; `NavLeaps.cs` one comment that names the
  retail human motion table; `NavRoute.cs` and `NavColumnTiles.cs` nothing. No decompile shape, no retail address, no
  `Retail` class. Nothing to re-express on that ground.
- **Provenance.** foundation-openac.md §4 found ACE-derived code in OpenAC's scenery and terrain files. ACE has no
  client-side grid planner for this folder to come from, and nothing in it resembles ACE's physics names; the port
  slice repeats the comment-line search of §4 on these files and records it.
- **Hygiene.** The provenance test that checks net ports' headers learns the navigation folder and the second pin.
- **Pin.** f22b048 is the last watched head and the last commit that touched the folder. The M5 planning watch
  (foundation-openac.md §6.4) re-reads `compare f22b048...<head>` for `src/AcDream.Core/Navigation/**` first; a newer
  head is the port's pin if the folder changed.

### 4.3 Slices and size

Sized against M4's as-run items (m4-plan.md §8: 1.2 about 2-3%, 2.1 4%, 2.2 3%, 3.1 6%, 3.2 5%, 4.1 6%, the polish
pass 2%; each with an Opus lead, slices, a review, fixes and a live gate). 1% of the week is about $22.5.

| Slice | Content | Share | About |
|---|---|---|---|
| N1 the planner | Port `NavColumnTiles`, `NavGrid`, `NavRoute`, `NavLeaps` into `Lifestone.Client/Movement/Navigation` with the type mapping of §4.1; Lifestone's own tests from shapes in code (ledges, gaps, stairs, a room with pillars, the sea, goal floors, object tops; about 40, folding OpenAC's 74 ideas, D28's coverage rule). | 1.5% | $34 |
| N2 the capture | `NavCapture` over `LandblockCollision` and the server's objects; dats tests in the gate tier: Holtburg's grid nodes agree with `TryGroundZ` (the §13 style), every BSD spawn cell is routed from the arrival (DenPlanner's 12), the Academy spawn to its exit room, a grid built twice is identical. | 1.5% | $34 |
| N3 the walk | `NavWalk` as §4.1 with `MovementAudit` built in; `FightWalk`'s legs and `DeathApproach` move onto it and `DenPlanner` goes; the offline walk checks of physics.md §15 gain a planned walk round the shop (where `wall-bump` sticks today). | 2.5% | $56 |
| N4 the hunt walks | Seek and cell changes walk instead of `place loc` within a staged distance (a far ground keeps the teleport: R2 allows the player tools); the missile spot with a line; a long melee approach walked to within ACE's cap; the live gate on the three accounts (D31), frames judged solo. | 1.5% | $34 |
| **Core (N1-N4)** | what the hunt needs | **7%** | **$158** |
| N5 doors, crowds, follow, leaps | Doors by `Use` and appraisal; creatures passed; follow for automation phase A3's mule; leaps driven standing (the rock-puzzle class of route). | 2.5-3% | $56-68 |
| **All** | | **about 10%** | **about $225** |

For comparison, writing the planner from scratch to the same behaviour (the column raster, clearances, corner tidying,
three-plan A*, the leap solver) is the expensive part OpenAC already tuned through playtests; at two to three times
N1's effort it would cost 3-4.5% for the same result, with no gain in independence beyond what a port header records.

## 5. Recommendation (question 4)

**Port the planner, re-express the capture and the walk, leave the plugin surface.**

- **Why port N1.** The four files are plain geometry with no client state, MIT, decompile-clean, and depend on
  OpenAC's physics only through two calls a small adapter supplies. They are the hard-won part (OpenAC's narration and
  playtest history tuned them) and the part Lifestone would otherwise rewrite to the same design. It is the same class
  of take as the net codecs: a file, a header, a change list.
- **Why re-express the rest.** The capture is an adapter to OpenAC's physics engine and must read Lifestone's; the walk
  controller is `AcDream.Runtime`, which §6.2 rule 4 keeps reference-only, and half of it is plugin ownership, chat
  commands and narration files Lifestone does not want. Its design decisions (the player always wins, arrival means
  something specific, recover before giving up, leaps from the character's own physics) are the acceptance checklist.
- **A Lifestone improvement over OpenAC.** OpenAC's first known limit is that legs and leaps are not checked against
  the physics before they are walked. Lifestone's physics is retail-shaped and already sweeps (`SweepViewer`,
  `HasClearLine`, the `OfflineWalker`), so N3 can check each leg with a sweep before walking it, and N5 each leap with a
  simulated jump: the grid proposes, the real physics confirms.
- **When.** automation-vision.md's phase A1 says "nav (recorded routes first)". Recorded routes need `RouteDriver` plus
  the recovery ladder, not a planner: if M5 is recorded routes outdoors, re-express only the ladder (about 1%, inside
  N3's design) and leave the port for later. If M5 walks the hunt (drops the `place loc` hops), fixes the archer's line,
  or walks a dungeon other than BSD (the AFK dungeon goal), take N1-N4 as one M5 item, with N5 when doors or a follow
  are in scope.
- **Leave.** The plugin contract, `INavigationAutomation`, `/nav` and `/motor`, ownership between plugins, the
  narration file, MossTank and its `.nav` reader (a published second reading of VTank's format, for phase A4 only).

## 6. Risks and limits not to promise

- **OpenAC's own known limits carry over**: no physics check of legs before walking (closed here by the sweep, §5); a
  staircase narrower than the body leans on the ladder; jump puzzles climbed but not descended; a thin 1.4 m post gets
  no jump; a follow takes the shortest way, not the leader's.
- **Memory and frame time.** A grid over a whole dungeon holds 46 to 149 MB while in use, and planning with leaps over
  a large grid can take seconds (OpenAC's numbers). Building runs on a worker, but large arrays are large-object-heap
  allocations: against Lifestone's 50 ms frame judge and the UI step's no-GC window (`m3-ui-step-gc-margin`), N2 must
  measure the build's allocations and collections on BSD and outdoors, keep the grid between walks, and plan outside
  the attack sequence.
- **The grid is an approximation.** A gap a quarter column narrower than the body can look walkable; the sweep check
  is the guard.
- **Not retail.** Retail has no client planner. Like `RouteDriver` (physics.md §14: "nothing here is retail; it drives
  retail's controller"), navigation is automation that presses the human's keys; its tuning constants are OpenAC's and
  are named as such, never cited as retail.
- **No spawned stand-ins.** Its live tests walk to real spawns on the belt, the forest, BSD and real dungeons, never to
  tool-spawned creatures.

## 7. Sources read

OpenAC at f22b048: `docs/navigation.md`; `src/AcDream.Core/Navigation/{NavGeometry,NavGrid,NavLeaps,NavRoute,NavColumnTiles}.cs`
(headers, APIs, dependencies, the terrain raster); `src/AcDream.Runtime/Navigation/NavigationWalkController.cs` (states,
interfaces, constants); `src/AcDream.Runtime/Gameplay/RuntimeRouteDriver.cs` (types); the test file sizes.
Lifestone: `src/Lifestone.Client/Movement/Routes/*`; `Session/Scenarios/{FightWalk,HuntFollow,DenExit,DeathApproach,RoutePlay*,HuntScenario}.cs`;
`src/Lifestone.Data/Physics/World/*` and `IGroundProbe.cs`; docs/physics.md §13-§15; docs/navigation.md (the teleport
panel and map coordinates: a different subject from this page); m3-plan.md and m4-plan.md §8-§9; the P4 branch's
`docs/session.md` hunt rules; docs/automation-vision.md; foundation-openac.md §4-§6; openac-m4-map.md §3.
