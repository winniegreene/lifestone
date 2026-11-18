# The world walk: spec and record

The owner asked on 2026-11-06 for the whole of Asheron's Call to be walked, landblock by landblock, to prove the pathfinding. The tool is described in [../world-walk.md](../world-walk.md). This job runs beside M6, not as one of its items.

## Why

Walks were proven on about 14 island routes and a set of named town and dungeon walks. Each new area brought new walk failures: M6 item 4 took five live rounds and 12 walk fixes.

## Decisions

| Row | Decision | Who |
|---|---|---|
| W-D1 | Walk everything offline, plus a live sample. Plan the full live walk, and stop short of implementing it | Owner, 2026-11-06 |
| W-D2 | Start the offline work now, beside item 7 | Owner, 2026-11-06 |
| W-D3 | Fix by shared cause, not spot by spot. No fixes start before the owner sets the cap at checkpoint 2 | Owner, 2026-11-06 |
| W-D4 | Compare our physics with ACE's on a sample, because a physics bug otherwise agrees with itself | Owner, 2026-11-06 |
| W-D5 | Light mode only: 4 threads, on cores 16 to 31, below normal priority. The walk pauses for gates | Owner, 2026-11-06 |
| W-D6 | The live sample's driver takes a route file, so the full live walk is mostly a bigger file. No code is written only for the full walk | Lead's reading of W-D1 |
| W-D7 | Build W3 and W4 this weekend only if the meter reads under 80% after W2; otherwise after the 2026-11-09 reset | Lead, in the approved plan |

## What "verified" means

Every landblock, walkable area, room and doorway ends with a verdict, and none is unknown:

| Verdict | Meaning |
|---|---|
| OK | The planner and physics agree, and the walk arrived with no recovery moves |
| Falsely closed | The planner says no path; physics says the body can go |
| Falsely open | The planner gives a route; physics or the walk fails |
| Check refused wrongly | The route check says no, but a real step works |
| Walk failed | A recovery move, a block, or over time |
| Closed by nature | Steep, sea or solid, with the reason named |
| Jump only | Physics reaches it only with a jump; the planner doesn't jump |
| Needs the server | Depends on a door, lever or barrier; goes to the live sample |

**Done means:**
1. 100% of the census has a verdict.
2. Every failure sits under a named cause, marked fixed, retail-correct, or waived by the owner.
3. The checker catches 100% of planted faults and finds the known defects again.
4. A fixed sample runs in the Exhaustive tier, report-only in its first gate.

**The result will state these limits plainly:**
- Doors, monsters and NPCs are proven only by the live sample until a full live walk runs.
- Physics is compared with ACE on a sample, not everywhere.
- Housing barriers are not enforced today.
- Gaps narrower than the check grid (1 m at its finest) can be missed.

## The world, measured

From the W1 census on 2026-11-06 (65,025 ids, no errors). The cell, building and dungeon counts equal the figures the project already pins.

| Measure | Count |
|---|---|
| Land landblocks | 32,665 |
| Coast landblocks | 6,024 |
| Sea landblocks | 26,336 |
| Landblocks with walkable ground | 38,689 |
| Walkable ground on the tiles | 1,318.8 km² |
| Dungeon landblocks | 1,773 |
| Mixed landblocks | 553 |
| Landblocks with building interiors | 1,083 |
| Rooms | 734,976 |
| Buildings | 6,979 |
| Statics, with scenery | 2,008,728 |

## Measured costs

From the W0 pilot of 100 landblocks, optimized build, 4 threads, beside the owner's live climb and other agents' work.

| Measure | Median | 95th percentile |
|---|---|---|
| Collision build, a landblock | 0.7 ms | 38 ms |
| Tile build, with neighbours | 6.3 ms | 64 ms |
| Walk of a landblock, about 190 m | 57 ms | 120 ms |
| A placement probe | 9 µs | 22 µs |
| A 4 m hop | 45 µs | 130 µs |

Walks ran at about 490 times real speed. The census of all 65,025 ids took 0.9 minutes on 4 threads, with a peak of 729 MB. The pilot peaked at 609 MB.

The code comment giving 300 to 500 ms a landblock is out of date for this path; the docs' 15 to 40 ms is the right order.

## Phases, re-priced

Run times are at 4 threads, measured. "Points" means percent of the weekly meter.

| # | Phase | Build points | Run time, first plan | Run time, measured |
|---|---|---|---|---|
| W0 | Rig and pilot | 2.5 | | Seconds |
| W1 | Census | 1 | 15 min to 2 h | **0.9 min** |
| W2 | World graph | 1.5 | Minutes to 3 h | **1.1 min** |
| W3 | Physics oracle | 3 | 6 to 24 h | **2.5 h** (643 MB peak, 47 MB on disk) |
| W4 | Outdoor walks | 2 | 3 to 12 h | **71 min** (998 MB peak, 15 MB on disk) |
| W5 | Rooms | 3 | 3 to 18 h | **22 min** (3.2 GB peak) |
| W6 | Places | 2 | Under 3 h | **9 min** (2.4 GB peak) |
| W7 | ACE comparison | 2.5 | Under 3 h | **Under 2 min** for 9,832 moves |
| W8 | Fixes by cause | 6 to 10 | | Set at checkpoint 2 |
| W9 | Live sample | 3 | Hours | Hours |
| W10 | Full live walk plan | 0.5 | | |

A whole-world rerun of W1 to W4 takes about 3 hours 45 minutes. W3's first pass did its work in about 16 minutes. The closed-passage check, which steps from blocked ground too, costs about 8 times that, so a run that does not need it could leave it out.

The `detour` measure took 62 minutes on 2 threads. The long walks took 65 minutes for 2,000 starts in four distance bands.

## Findings so far

### The walks (W4)

293,688 walks over the 75,187 land edges, none threw: 266,345 clean (90.69%). Of the edges, 60,454 were clean, 12,968 had a failed walk, and 1,765 had ends that share no area, so they were recorded and not walked.

| Cause | Walks | What the evidence says |
|---|---|---|
| The route leaves the planner's view | 9,865 | The planner sees about 3 x 3 landblocks and says no path when the goal is in view but the way round is not. Widening the view found a route for 4,622 of 4,939 failing directions, mostly 2 to 6 rings out. Median route 1,399 m, longest 9,630 m. 72 needed more than 12 rings, and 245 were refused by the route check with a route in plain view |
| Recovery moves on the way | 8,799 | 984 at top speed, 7,815 at low-level speed. 78% and 48% of the moves lie within 1 m of a 24 m cell line, against about 16% by chance: `TODO(placement-cell-edge)` again |
| Arrived after the route check refused | 8,241 | The reasons were counted, not recorded. A sample rerun is owed |
| Physics refused outright | 303 | 301 by the ground probe, 2 by the placement probe |
| Over time | 104 | 15 at top speed, 89 at low-level speed |
| Started blocked, goal scarred | 31 | 19 and 12 |

### The physics oracle (W3)

Every one of the 38,689 walkable landblocks was scanned: 89.1 M points, of which 98.41% took a body, and 701.8 M hops, of which 82.11% landed. Of those landed, 97.46% stayed in their area, and 120,230 more went by a drop the map already holds. None threw.

| Cause | Findings | Landblocks | Reading |
|---|---|---|---|
| Blocked margin | 1,235,378 | 32,365 | The body stepped on ground the map blocks. 76% of the sample lies round statics: the planner's clearance, by design, unless it closes a passage (next row) |
| **Closed passage** | **450** | **333** | One patch of blocked ground the body crosses, with large areas on two sides. This is the falsely closed list. 415 lie on plain blocked ground, not round objects |
| **Unmapped drop between large areas** | **138,124** | **1,089** | 97% downhill and almost always one way, a median fall of 3 m over a 4 m hop. The body goes, and the map has no link |
| Unmapped drop to a sliver | 657,877 | 6,750 | The same into a small area, which matters far less |
| Refused on open ground | 16,344 | 1,199 | The map says open, and no body fits. 86% of the sample carries the water flag, about half at sea level and half inland. 13% is plain ground, usually two points 4 m apart, the likely mark of `TODO(nav-static-footprint)` |
| Trapped on open ground | 6,436 | 3,799 | A body stands and no step leaves. 84% of the sample is on slopes |

The 1,765 edges with no shared area: 1,509 have no oracle finding beside them, so the graph's split stands there. 256 touch a finding and stay open.

### The rooms (W5, master-merged code)

All 3,409 landblocks with rooms, 0 threw. 734,976 cells; 602,507 have a floor to stand on. 1,850,702 directed doorways, 1,142,568 passable between rooms with floors. 1,140,548 walks between rooms, **1,077,228 clean (94.45%)**.

Each cause below is a count of walks and of the landblocks it touches. Dungeons repeat their layout across landblocks, so the number of different faults is smaller than the landblock count.

| Cause | Spots | Landblocks |
|---|---|---|
| The route check stops the walk, and the walk fails without the check too | 43,856 | 962 |
| The route check stops the walk, but the walk arrives without the check | 15,685 | 1,014 |
| Arrives, with a recovery move | 3,239 | 740 |
| A room that cannot hold a body | 2,020 walks; 6,607 rooms with no place, 10,123 where the body lands outside the room | 335 |
| Over time | 496 | 221 |

Walking out of and into buildings: 27,092 walks, 15,541 failed. 10,705 are "no lane between the door and the ground" in 817 landblocks, 1,456 "start refused", 476 no place to stand, 327 no ground outside.

Closed doorways: 681,714 (360,112 flat portals, 228,700 no floor beyond, 54,022 too small, 38,880 no floor at the sill). The body was tried through 85,219 of them and crossed **24,542** (29%) in 2,489 landblocks: those are candidates for falsely closed. Walking joins fewer rooms than the map says in 907 landblocks.

### The long walks (W4)

2,000 starts on the main landmass, four bands, goals the world graph says are reachable: 7,866 walks.

| Band | Clean | Arrived at all |
|---|---|---|
| 1 to 1.5 km | 65.5% | 79% |
| 2 to 3 km | 54.8% | 78% |
| 4 to 6 km | 37.2% | 68% |
| 8 to 12 km | 16.3% | 55% |

The route check refuses at the same rate per kilometre in every band (0.15 to 0.18 per km), so the fall in clean walks with distance is mostly the same local faults met more often. The failures that are particular to long walks:

| Cause | Walks | Note |
|---|---|---|
| No path late in the walk | 805 | Stops a median of 533 m from the goal, about where the attached map first holds it. That the map's window is the cause is the likely reading, not yet confirmed |
| Goes round without getting there | 646 | Over time. Walked about 4.9 times the straight line and made a median of 7% of it good, with 119 replans |
| The route check stops it | 414 | |
| The map's next tile never arrives | 459 | 270 for the goal's tile, 189 between |

### The long-walk fixes (W8, offline only so far)

The same 2,000 starts and goals, after each fix:

| Band | Arrived before | With the world route | With the corner rule | With the water rule | Clean at the end |
|---|---|---|---|---|---|
| 1 to 1.5 km | 79% | 93% | 97% | 97.5% | 78.0% |
| 2 to 3 km | 78% | 90% | 96% | 97.1% | 64.5% |
| 4 to 6 km | 68% | 87% | 93% | 94.4% | 41.6% |
| 8 to 12 km | 55% | 84% | 89% | 92.5% | 18.9% |

- **The world route.** The attached map is too small to know the way round a mountain or a lake, and the old far goal, the open edge nearest the straight line, went in circles. The `graph` verb now also writes `world-routes.bin`: each landblock's ground gathered into the parts a body can walk round both ways inside it, and the steps between those parts. The file is about 27 MB and is built from the dats, so it stays in `cache/`. A far goal follows the route, and the streamer is told the route's next landblocks. Walks over time fell from 646 to 54. The median walk is about 5% longer than before against the straight line, because it now goes where the land goes.
- **The corner rule.** A route that crosses a landblock's corner needs ground in a side landblock that is not attached yet, so the goal was in view and the plan failed. After such a failure the walk goes to the furthest edge on the route that the failed search reached, and the side landblocks attach on the way. No-path stops fell from 396 to 10.
- **The water rule.** A body in deep water stands up to 0.9 m below the ground, so the route check's probe, measured from that height, could begin under the ground and find nothing. After a sample in a water cell the probe also starts 0.8 m higher. Walks the check ended fell from 339 to 217, and none of those left is in water below sea level. The exact way the low start fails has not been traced. Flat test terrain does not reach the rule, so its proof is this run and not a unit test.

Why the 364 walks that do not arrive stopped:

| Cause | Walks | Note |
|---|---|---|
| The route check ended it | 217 | 211 arrive with the check off, 99 of them with no recovery move, so most of these refusals are wrong |
| Over time | 57 | |
| Landblocks between the body and the goal never attached | 41 | |
| The goal's landblock never attached | 24 | |
| No path | 10 | Includes goals the world route says the land does not join |
| The start is blocked | 10 | |
| Other | 5 | |

A walk the route check ended is now repeated with the check off, and its result is kept with the walk. The product does not use world routes yet: only the offline rig does, through `--routes`.

The same fix on the short walks, the whole world (293,688 walks over 75,187 land edges; the rerun before the fix matched the first pass):

| Outcome | Before | With the world route |
|---|---|---|
| Arrived, no recovery move | 266,345 (90.69%) | 269,369 (91.72%), or 271,068 (92.30%) counting second tries |
| Arrived at all | 283,385 (96.49%) | 291,662 (99.31%) counting second tries |
| No path | 9,864 | 290, of which 242 carry a world route note |
| Land edges that pass | 60,454 | 60,828 |

No edge got worse and no walk clean before now fails. A walk's time limit comes from the straight line, so 4,886 of the old no-path walks ran out of time on the way round. Each walk over time now gets a second try with a limit of 1,200 simulated seconds, recorded beside the first (`longer_limit`): of 4,990, 4,125 arrive (1,699 clean), 645 run out again and 220 fail otherwise. The second try does not change a verdict.

Open: 566 walks reach the land edge the world route names, the detailed map finds no crossing, and the body waits out the 30 s horizon wait with nothing left to attach. The world route and the detailed search disagree there. 2,426 of the 4,125 second-try arrivals needed a recovery move.

### The route-check fixes (W8, offline only so far)

The check ended 217 long walks, and 211 of those arrive with it off. Causes by walk: the narrow tip of a steep land triangle 140, a static's edge the foot sphere clips 37, a start height about a metre under the ground on a steep slope 25, other slope spots 15. Two rules: the start is looked for higher up when the first sample finds nothing, and a refused outdoor sample may take ground half a metre to a metre beside it or just ahead, unless a wall stands between, at most two samples in a row. Indoors a thin wall or a sill matters, so rooms have only the first rule.

The same 2,000 starts and goals (7,866 walks), before and after:

| Band | Arrived before | Arrived after | Clean before | Clean after |
|---|---|---|---|---|
| 1 to 1.5 km | 97.5% | 98.2% | 78.0% | 91.8% |
| 2 to 3 km | 97.1% | 98.1% | 64.5% | 86.6% |
| 4 to 6 km | 94.4% | 97.0% | 41.6% | 77.1% |
| 8 to 12 km | 92.5% | 96.0% | 18.9% | 62.2% |

- Walks that do not arrive: 364 before, 212 after. Ended by the check: 217 before, 79 after, and 76 of those 79 still arrive with the check off.
- Stuck for good: 5 before, 5 after. The six walks the old check stopped rightly (no arrival with it off) still do not arrive.
- 157 walks that were blocked now arrive; 9 that arrived no longer do (the check elsewhere on the route 3, start blocked 2, no path 2, over time 1, stuck 1).
- **The cost: recovery moves rose.** Walks with one: 767 before, 985 after; moves in all: 2,070 before, 2,851 after (5.0 to 6.8 per 100 km). All 313 walks with a recovery move for the first time had already been refused and replanned, so no clean walk got worse. 666 of the moves, in 260 walks (248 arrive), sit within 8 m of a spot the old check refused, typically under a metre from it. They fall on 185 places, 150 of them within 0.6 m of a 24 m land grid line and 54 at a corner, and only 1 within 3 m of a static: the body stalls on some headings where land triangles meet. The old check made the planner go round them; a stall there is now the price of not ending the walk. The tiles learn some of these creases (the next section).

Rooms (1,140,548 legs) did not move: 94.45% clean before and after, and doorway-to-outdoors failures 15,541 before and 15,522 after. Their wrong refusals are not start-height ones and need their own look. Holtburg's known room faults are still found.

**The rooms' wrong refusals.** Rerun on the land-seam and border build, the rooms were unchanged (94.45% clean, 15,685 walks the check stopped that arrive without it). 14,909 of those are the ground probe. In a sample of 400, 271 lay within a metre of a static's collision shape, 151 of them one model, 0x02000248: a pillar, a cylinder 3 m in radius and 10 m tall, in the middle of a 10 m room of a layout many dungeons repeat. The plan ran through the pillar, a body without the check slid round its edge, and the check rightly refused the line. The room planner's detour had found no way round, for two reasons. The pillar's Setup radius (over 5 m) closed nearly the whole room, though retail collides an object without physics parts by its cylinders alone. And a walk's end inside a static's disc (the body stood 3 mm inside the pillar's) left every step from it refused. Three rules (docs/wayfinding.md, "Objects of shapes only"): such objects are read by their shapes alone unless every shape is under the step-up; an end inside an exact disc steps straight out of it, at most 1 m; and the steps from that point are probed for ground, since the floor test sees no wall.

| | Before | After |
|---|---|---|
| Clean | 1,077,228 (94.45%) | 1,083,889 (95.03%) |
| Stopped by the check, arrive without it | 15,685 | 9,212 |
| Stopped by the check, fail without it too | 43,855 | 43,647 |
| Arrive with a recovery move | 3,239 | 3,252 |

- 6,687 walks became clean; 26 that were clean are not (16 now stopped by the check, 10 with a recovery move). Two earlier versions show what the last two rules buy: stepping out of rough discs too made 414 walks worse (0x00150174's wall fittings), and no ground probe on the step out let one graze a doorway's corner by 5 cm (0x001501CD).
- Ways in and out of buildings are unchanged (15,522 failed of 27,092): they are the door-to-ground lanes, a cause of their own.
- The 43,647 walks that fail with and without the check are the larger room count and are next.

**Ledges.** Of the 43,647 walks that fail with the check and without it, 42,035 run out of time without it, and 37,101 of those cross 0.5 m between rooms. In a sample of 600, 542 had a room at one end whose every floor is narrower than a body: a plinth 0.5 m wide and 0.5 m high along a room's walls, in layouts many dungeons repeat (0x6243 to 0x6245, 0x5C4D to 0x5C4F, 0x00F9 to 0x00FE each failed about 1,200 of some 3,000 legs). The room graph took them for rooms, and the world walk placed a goal in them a body cannot reach. They are now ledges, and their doorways are closed (docs/wayfinding.md, "Ledges"). The rule's first version probed only each floor's centre and split rooms a body walks between in three landblocks (a doorframe with a mesh in its mouth); probing every 0.25 m over the floor split none.

| | Before | After |
|---|---|---|
| Legs walked | 1,140,548 | 1,119,712 |
| Clean | 1,083,889 | 1,084,157 |
| Fail with the check and without it | 43,647 | 23,119 |
| Stopped by the check, arrive without it | 9,212 | 8,681 |
| Doorways closed as a ledge | 0 | 41,795 |
| Groups of rooms joined, by the map and by walking | 83,923 and 106,721 | 94,490 and 106,724 |

- 17 walks that were clean are not (9 now stopped by the check, 8 with a recovery move). No landblock's largest walking group shrank.
- The map now joins far fewer rooms that walking cannot (10,830 more groups by the map, 3 more by walking). The legs not walked are the ledge doorways, which no body stands beyond.
- Open: 23,119 walks still fail both ways. In a sample of 600, 464 still have a room narrower than a body at one end whose floor the foot finds somewhere; a tighter rule (the foot may not come to rest below the floor) changed nothing, so these need their own trace.

**Edgings.** The 23,119 sorted by room shape: three dungeon shapes (env 0x0D000599, 0592 and 0593) held 75% of them. They are edgings 0.5 m wide and 0.5 m high that turn round a room's corner in an L. At the L's outer corner the body balances on the edging with its centre on the floor's diagonal edge, mostly over the room below, so the ledge rule found ground and kept them as rooms; the world walk then stood the goal 0.05 m behind the wall, where no walk reaches. The ledge rule's samples now keep 0.05 m in from the floor's edges (docs/wayfinding.md, "Ledges").

| | Before | After |
|---|---|---|
| Legs walked | 1,119,712 | 1,100,644 |
| Clean | 1,084,157 | 1,084,214 |
| Fail with the check and without it | 23,119 | 6,524 |
| Stopped by the check, arrive without it | 8,681 | 6,151 |
| Arrive with a recovery move | 3,251 | 3,253 |
| Doorways closed as a ledge | 41,795 | 70,449 |
| Groups of rooms joined, by the map and by walking | 94,490 and 106,724 | 104,018 and 106,723 |

- 30 walks that were clean are not (20 now stopped by the check, 10 with a recovery move). No landblock's largest walking group shrank; one pair of cells that walked to each other (0x526C) is now two, one of them an edging.
- Open: 6,524 walks still fail both ways. Most stop where the plan goes straight through an object with a physics part whose rough disc covers its whole room (a staircase across a corridor, env 0x0D0000D8), so no detour stands clear of it. Next.

**Objects with physics parts and the turn's curve.** Inside the disc of an object with physics BSP parts, and a body's half-width past it, the body itself now judges where it stands (docs/wayfinding.md, "Objects with physics parts"). The first version, the foot alone, gained 1,277 clean walks but made 248 worse, and two dat tests found why: the foot fits where the body does not (under a bunk's board), a walk's end inside a disc had no way out, and a table's edge lay past its disc. With those fixed, one cave tour leg still needed two rungs. It was not the plan: the leg ends 10 cm elsewhere after the change, and from where the body rests it needed the same two rungs before the change. A body that runs off a turn in place 60 degrees off its aim sweeps a curve 1.6 m wide, and it met a pillar. The runner now checks that curve in a dungeon first (docs/wayfinding.md, "In a dungeon the walk near a doorway").

| | Before | After |
|---|---|---|
| Clean | 1,084,214 | 1,086,005 |
| Fail with the check and without it | 6,524 | 6,291 |
| Stopped by the check, arrive without it | 6,151 | 5,048 |
| Arrive with a recovery move | 3,253 | 2,977 |
| Ways in and out of buildings that fail | 15,522 | 15,517 |
| Groups of rooms joined by walking | 106,723 | 106,432 |

- 2,155 walks got better and 364 worse (193 with a recovery move, 171 now stopped by the check). In four landblocks (0x0155, 0x019A, 0x01E9, 0x3960) the largest walking group lost one room, each to a walk the check refuses, mostly at its first step though the body walks it without the check. They need a trace of their own.
- Dryreach's Master Scrivener of Creature Magic is now reached at his own point, round his counter; his catalog stop stays.
- Open: 6,291 walks still fail both ways, among them cluttered rooms (benches, tables and chairs, 0x01E902BF) where the detour's candidates, the floor centres and corners, find no way the body walks.

**Building exits.** 15,517 of the 27,092 walks out of and into buildings failed. 12,125 were at doors the tile cut no lane for, and in a sample of 3,244 such doors 99% were refused by one rule: the sill does not step down to the ground. Walking the body alone at 444 of them, straight in and out from five directions, two walked both ways. The rest drop only (balconies), get in only with a recovery move, or not at all: many houses have a second door a metre over flat ground, into a raised nook, and retail's edge slide keeps a walking body from stepping off such a ledge or onto it. So the planner was right, and the world walk now judges every failed door with the body alone (docs/world-walk.md, "Doors the body cannot walk"); a door with no lane that the body cannot walk both ways is recorded as shut by nature. Two real faults turned up beside it:

- **Head room.** The lane cutter probed a door's lintel at the full 1.8 m, the room graph 5 cm under it, so a door design 2.5 m wide and 1.79 m tall was an exit with no lane, walked both ways by the body. The cutter now probes as the graph does (docs/wayfinding.md, "The join with the tile"). Holtburg gains the lane of 0xA9B40177.
- **Doors that open into each other.** Where buildings stand wall to wall (102 landblocks, Viamont 0x31D6 among them), a door opens straight into the next building's door. Each lane ran into the other building, so no plan joined the rooms, and 1,128 of the doors left failing were these. The room graph now walks such a pair as a doorway (`RoomGraph.PairedDoors`), and the world walk walks it as a room leg.

| | Before | After |
|---|---|---|
| Walks out of and into buildings | 27,092 | 23,470 (1,811 paired doors walked as room legs instead) |
| Of them failed | 15,517 | 1,145 |
| Doors shut by nature (drop only, or not walked) | not judged | 5,334 |
| Room legs walked | 1,100,682 | 1,102,600 |
| Clean | 1,086,005 | 1,087,838 |
| Groups of rooms joined by walking | 106,432 | 105,720 |

- The 1,918 new room legs are the paired doors; 1,833 walk clean. Every leg walked before ends as before. The largest walking group grew in 103 landblocks and shrank in none; Viamont's grew from 8 rooms to 40.
- 52 of the 85 paired legs that fail are in 0x3A11, whose buildings sit 10 to 18 m below the land round them; nearly all are stopped by the route check's ground probe, and they join that cause.
- Open after this pass (the next pass follows): 1,145 failed walks. 622 are doors with no place to stand inside (430) or no ground near outside (192); 493 have a lane (no path 213, the check 172, a refused start 58, a scarred goal 19, arrived with a recovery move 19), and at 268 of them the body alone walks the door both ways; 30 walks at 15 doors with no lane that the body walks both ways.

**Laned door faults.** Of the 493 failed walks at doors with a lane, the body walked the door both ways at 268. Two causes were fixed:

- **Doors onto a raised deck.** 153 of them were 77 doors 2 m up on a deck whose steps run straight out along the lane and meet the ground about 5 m from the sill. The sill-to-ground check passed, but the 2.5 m lane strip ended on the deck among the deck's blocked nodes, with its points at the ground's height under the body: no walk joined it, and a walk out stood still under its next point. Such a door is now walked like a raised door whose stairs turn aside, down its floors, with the strip cut on the ground past their foot (docs/wayfinding.md, `raised_line=`). Holtburg's 0xA9B40179 is one.
- **Tall objects whose origin is high.** A lone model with physics parts was judged by its origin's height, so one whose origin is over a standing human was overhead though its solid reaches the floor (0x01004DAE in 0x59660204). It is now judged by its solid's real height range (docs/wayfinding.md, "Objects with physics parts").

| | Before | After |
|---|---|---|
| Walks out of and into buildings that fail | 1,145 | 984 |
| Of them at raised doors the body walks | 156 | 3 |
| Room legs clean | 1,087,838 | 1,087,857 |

- 161 building-exit walks fixed and none made worse; 20 room walks fixed and 1 made worse (0x596B, 0x012D to 0x012A); no landblock's largest walking group shrank.
- **The room's standing place, tried and set aside.** Many of the route check's wrong refusals stand at a walk's start or goal: 977 of the 4,668 ground-probe refusals lie within 1 m of the goal and 1,799 within 1 m of the start. The world walk stands a room's body at its largest floor's centre, which can lie in a solid the placement does not catch (a long table, 0x2D5A0105; a spiral staircase's column reaching under the floor, 0x0B700118), where the check rightly finds no floor. Requiring the check's own ground probe at the standing place, and trying up to 2 m out, gained 3,415 walks and lost 891, and the largest walking group shrank in 35 landblocks; keeping to 0.75 m was worse (1,749 against 817). It changes what the walk measures as much as how well, so it needs its own pass. The probe finds no floor near the middle of 65,917 rooms: slivers a body overhangs, and sloping floors.
- Open: 984 failed walks. 622 are doors with no place to stand inside or no ground near outside; at 109 laned doors the body walks both ways (the route check 77, no path 17, arrived with a recovery move 12); 13 walks at 0x4FF1, a door into a narrow passage whose chosen outside point lies past a wall.

**Furniture the planner did not see.** The standing-place trial's lost walks were mostly one fault in the planner, which the old standing places hid by starting inside the furniture. Two causes were fixed:

- **Furniture with no radius of its own.** A bed 3 m long across the middle of 0x0043010C, end on to its doorway (Setup 0x02001051, in a layout repeated over many dungeons), has a Setup radius of 0: it is solid only by its part's physics mesh, so the planner drew no disc for it and walks ran straight across it. Such a Setup now stands in the way by its parts' real reach, and the body test judges inside it (docs/wayfinding.md, "Objects with physics parts"). Only furniture: at most 3 m across and no taller than a standing human. The Facility Hub's ramps are such Setups too (0x02000623, 9 m across and 6 m tall); read as obstacles they turned walks down them off their sides, so the full rule's 2,639 walks better and 66 worse came down to the figures below.
- **Corners held off by thick walls.** The way round a static goes through the floor's corners drawn in 0.58 m from both edges, but a wall thicker than the floor's edge holds the foot off such a corner (0x0043010C's floor runs to x 23; the foot touches its walls 0.5 m in), so no way round stood. Such a corner is now drawn in twice as far, at most halfway to the floor's centre.

| | Before | After |
|---|---|---|
| Room legs clean | 1,087,857 | 1,089,479 |
| Walks the route check refuses that the body walks | 5,070 | 3,720 |
| Walks refused that fail without the check too | 6,333 | 6,030 |
| Groups of rooms joined by walking | 105,716 | 105,267 |

- 1,715 room walks fixed and 93 made worse; the largest walking group grew in 76 landblocks and shrank in 1 (0x9E43, 19 rooms to 18). Building exits are unchanged.
- The standing place, again on top of the full rule: 1,526 walks better and 604 worse, 12 groups shrank. Most of the worse (298) now start beside furniture and arrive only after a recovery move, so it stayed set aside until the fixes below.

**Standing places beside furniture.** The world walk now stands a body where the route check's own ground probe finds the floor (`RoomWalks.Stand`): of the usual places round a room's centre, the first where the probe finds the floor within 0.3 m. Before, it used the first place the body fitted, and a room's centre can lie inside a table or a staircase that the placement does not catch. On its own this gained 1,740 walks and lost 781. The survey of the 422 newly worse walks that arrived only after a recovery move found one place behind most of them: a staircase (Setup 0x02000396) along a room's side in a house layout repeated over a hundred landblocks, where the moved place stands a body 0.33 m from the stairs' side. Two causes were fixed:

- **A staircase with no radius of its own.** Its box is 5.7 m by 2 m and 4.8 m tall, so the furniture rule above left it out, and the planner drew no disc for it. The rule now takes any such Setup whose box reaches no more than 4 m from its centre to a corner. Height is no longer a limit. The Facility Hub's ramp (9.3 m) is still left out, and its walks pass.
- **A body standing still ran before it turned.** On the way to an indoor corner the runner lets 15 degrees of heading error ride and presses no new turn key in the last 2.5 m (D134). Both rules are meant for a body already running along the leg. A body that started standing still, facing 12 to 36 degrees off its first leg, ran forward on its old heading and hit the stairs' side or a doorway's. Both rules now apply only once forward is held. The M5 P7i stuck-pocket legs now turn by the ordinary press instead of P7i's fallback.

Of the 422, 372 are clean with both fixes, 186 with the turning fix alone, and 30 with the staircase fix and the bend check below but not the turning fix. A third change, letting the runner skip a planned bend only where the planner also sees the line clear, fixed 1 of the 422 for no cause found, so it was left out.

| | Before | After |
|---|---|---|
| Room legs clean | 1,089,479 | 1,093,223 |
| Walks that arrive only after a recovery move | 3,019 | 1,110 |
| Walks the route check refuses that the body walks | 3,720 | 3,024 |
| Walks refused that fail without the check too | 6,030 | 4,967 |
| Groups of rooms joined by walking | 105,267 | 105,185 |
| Building exits failing | 984 | 977 |

- 4,046 room walks fixed and 302 made worse. The largest walking group grew in 45 landblocks and shrank in 12, among them 0x376A (29 rooms to 20) and 0x0168 (118 to 104).
- Each of those shrinks is one walk that cuts a bridge between groups, where the route check refuses a walk to or from a moved place that the body walks without the check. Examples are 0x376A0114 (the place lifted 0.25 m) and 0x01680137 (an arch through a thick wall, the place 0.3 m aside). About 130 of the 302 are this kind, the next cause, fixed below.

**Route-check refusals at moved places.** 151 walks to or from a moved standing place were refused by the route check. Two causes held most of them:

- **A pocket beside a dais (83 walks, one layout).** In 0x720201F7 and the same room in 82 other landblocks, the room's centre is on a dais, so the place moves 0.75 m east of it, between the dais and a wall fitting. The straight walk from the doorway into the room clips the dais's edge. The in-room detour (`FloorDetour`) tries only the floor's centre and corners, and none of them sees both ends, so the walk was left straight and refused. When no detour is found that way, points 1 m and 1.5 m round each end of the walk are tried too, and a step to or from one must find ground all along it. They are a fallback only: taken first, they replaced detours that already worked and bent 146 clean walks into a doorway's side. Points 0.5 m out were no use, since the runner counts a waypoint reached within 0.5 m and aimed straight past it.
- **The push into an arch (the next largest group).** Some rooms are arches through a thick wall, 0.7 m deep (0x0287010E, 0x01680137). The route steps 0.5 m past a doorway before turning, and in these rooms that point lies in, or just beyond, a band a few centimetres deep where the foot does not fit. The body slides past it, but the check refused every walk through. The push now has to find ground every 0.1 m from the doorway; where it does not, the push is taken at half the distance (`InsideWaypoint`).

Of the 151, 114 are now clean. 18 are still refused though the body walks them. 19 are refused and fail without the check too: places in a nook, such as between a bookshelf and a central column in 0x00D00115, where the check is right and the standing place is poor.

| | Before | After |
|---|---|---|
| Room legs clean | 1,093,223 | 1,093,843 |
| Walks the route check refuses that the body walks | 3,024 | 2,445 |
| Walks refused that fail without the check too | 4,967 | 4,859 |
| Groups of rooms joined by walking | 105,185 | 104,898 |
| Building exits failing | 977 | 977 |

- 655 room walks fixed and 35 made worse. The largest walking group grew in 87 landblocks and shrank in none. Against the run before the standing-place change, it shrinks in 3, by one or two rooms each (0x5418, 0x5660, 0x8D4D).
- Walks that arrive only after a recovery move went from 1,110 to 1,195, mostly walks that were refused before and now arrive.
- Pinned by `RoomStaticShapesDatTests.A_walk_to_a_moved_standing_place_passes_the_check` (the dais and two arches). Each row fails without its own fix.

**Standing places cut off from a doorway.** Most of the walks refused that fail without the check too at a moved place were one room layout: in 0x80900102 and 12 copies, the place moved 0.3 m off the floor's centre into a pocket behind a raised block, cut off from the room's only doorway. The world walk now also asks of a standing place that the ground leads from it to every doorway the room is walked through (`RoomWalks.Stand`, `Floods`): the route check's own ground probe, each step within 0.6 m up or down of the last, first in a straight line to a point 0.5 m inside each doorway, then, where a line fails, over the room's floor on a 0.25 m grid. Of the places round the centre, the first that reaches every doorway is taken. When none does, the places up to 2 m out are tried too, and the place reaching the most is taken. The test only moves a place to one that reaches more, so a gap narrower than the grid at most leaves a place where it was. The library room 0x00D00115 was not a nook after all: the floor runs round its bookshelves, and the planned walk runs straight into one. That is the cluttered-room routing still open.

| | Before | After |
|---|---|---|
| Room legs clean | 1,093,843 | 1,094,096 |
| Walks the route check refuses that the body walks | 2,445 | 2,484 |
| Walks refused that fail without the check too | 4,859 | 4,627 |
| Walks that arrive only after a recovery move | 1,195 | 1,138 |
| Groups of rooms joined by walking | 104,898 | 104,874 |
| Building exits failing | 977 | 977 |

- 268 room walks fixed and 15 made worse. The largest walking group grew in 6 landblocks and shrank in 1 (0x01BB, 173 rooms to 172: 0x01BB013D's place moved 2 m and the walk from 0x01BB013F no longer arrives).
- The wrong refusals rose by 39 because some walks that failed both ways now walk without the check. They stay refused.
- At moved places, the walks refused that fail without the check too went from 21 to 12: the library room, two rooms split in two in 0x0116, and four walks in 0xF838 whose places moved and now fail.
- 2,592 rooms have no place that reaches every doorway (3,088 doorways in all; `doors_short` in each landblock's result). They are rooms split by a step or a solid, or doorways the probe cannot reach. They are the next list to read.
- The walk's own choice, not the product's, so no test pins it: the runs check it, as for the standing places before.

**The rooms short of a doorway, read.** A diagnostic build of the walk (not kept) reran the 584 landblocks that hold the 2,592 rooms, and for each doorway the flood missed it noted where the doorway's point lay, the floor nearest it and any static object within 2 m of it, from the room's own cell or another's. Of the 3,027 doorways it read:

| Kind | Doorways | In rooms with a walk failing both ways | In rooms with a walk wrongly refused |
|---|---|---|---|
| The point lies outside the room: the sill is the portal's foot, 3 m under the stair that meets it (0x0D00041D, 0x0D00041E, 0x0D00041F) | 1,607 | 21 | 14 |
| A static object within 2 m: standing on the doorway's point, or no floor there | 1,086 | 275 | 256 |
| No floor in the doorway and no object near it | 255 | 190 | 17 |
| Floor there, at another height | 77 | 23 | 10 |
| Other | 2 | 2 | 0 |

- **The walk's own false alarms.** 1,674 of the 2,592 rooms have no failing walk at all. The stair rooms are most of them: their portals reach 3 m below where the stair meets them, so the doorway point (at the sill) is not in the room. The flood should look for the doorway where the floor meets the portal, between its foot and its top, and along its width, not only at its centre.
- **Objects in doorways.** 1,086 doorways have a static object within 2 m. Some the room planner cannot see: it reads a room's own statics only (`CellRoutePlanner.StaticsOf`), though physics, as retail, meets an object in every cell it overlaps. In 0x001501CD the next room's Setup 0x02000274 (a solid 2 m long and 1 m tall) stands across the doorway 0.1 m past the portal, and the route check finds no floor in a band 1.25 m deep inside 0x001501CD. Next-room objects stand within 2 m of 359 of the missed doorways. Others the planner sees and plans through anyway: in 0x02E6's 70 alcoves (0x0D0000F0) a statue (0x020000CE) fills all but a 1 m gap each side of the doorway, the planned line runs into it, the check refuses, and the body slides round it and arrives. That is the cluttered-room routing.
- **Bands with nothing in them.** 255 doorways have no object near and no floor for the probe within about a metre of the portal, on both sides. In 0x00560119 (0x0D000094) to 0x0056010D (0x0D000065) the two floors meet flat at the portal, no wall or object is near, and the probe still finds no floor from 0.75 m inside one room to 0.25 m inside the other. 190 of them are in rooms with a walk that fails both ways. Not yet explained.

**Objects that reach in from the next room.** The room planner now reads, beside a room's own static objects, those of the rooms through its portals whose discs reach a portal (`CellRoutePlanner.StaticsOf`, `ReachesPortal`). Retail meets an object in every cell its shadow is in (`CPhysicsObj::add_shadows_to_cells`), so the physics already did; the planner did not, and planned straight into an object standing across a doorway from the far side. Two such walks are now tests (`RoomStaticShapesDatTests`, 0x00CB0151 to 0x00CB015A and 0x8A030135 to 0x8A030137); both fail without the change.

| | Before | After |
|---|---|---|
| Room legs clean | 1,094,096 | 1,094,414 |
| Walks the route check refuses that the body walks | 2,484 | 2,187 |
| Walks refused that fail without the check too | 4,627 | 4,639 |
| Walks that arrive only after a recovery move | 1,138 | 1,149 |
| Building exits failing | 977 | 977 |

- 348 room walks fixed and 30 made worse. The largest walking group grew in 10 landblocks and shrank in 5: one layout repeated in 0x5746, 0x5846, 0x5851, 0x5946 and 0x5A46 (503 rooms to 499 each).
- Most of the walks made worse start or end in a room whose place the probe does not find (0x665E's 0x012F, 0x014D and 0x0187: a central object the probe stands on nowhere; 0x57460275). A start that lies inside a next-room object's exact disc now steps 1 m out of it, onto no floor the check finds. The body still walks most of them.
- 2,610 rooms are short of a doorway (2,592 before).

**Rooms round a solid at their centre.** Of the 4,639 walks refused that fail without the check too, 3,101 went to or from a room where the route check's ground probe found no floor at the standing place. The one read in detail, 0x89040106 (184 such walks in its landblock), is a room built round a 2 m round solid at the centre (Setup 0x020003C7): every place near the centre stood on top of it, where the probe rightly finds no floor and no walk reaches. The world walk took the first such place the body fitted. It now tries the places 1.25 m and 2 m out for one the probe finds, the most-reaching as before, and keeps an unprobed place only when none is found (`RoomWalks.Stand`). It is the walk's own choice, not the product's, so no test pins it.

| | Before | After |
|---|---|---|
| Room legs clean | 1,094,414 (99.25%) | 1,096,412 (99.43%) |
| Walks the route check refuses that the body walks | 2,187 | 2,220 |
| Walks refused that fail without the check too | 4,639 | 2,672 |
| Walks that arrive only after a recovery move | 1,149 | 1,137 |
| Building exits failing | 977 | 958 |

- 2,049 room walks fixed and 51 made worse: 26 now arrive with a recovery move, 21 are refused wrongly, 3 fail both ways and 1 runs over time. They are walks from places that moved, meeting the faults already listed, such as the check refusing a step from a place beside a wall (0x01AB0115).
- The largest walking group grew in 55 landblocks and shrank in 3 (0x01AB 134 rooms to 133, 0x868C 11 to 10, 0x8E4C 2 to 1).

**Cluttered rooms: the grid search.** Of the 2,220 walks the route check refused that the body walks, 822 were refused at the first ground sample of the plan's first step, about a metre from the body. Traced, the body stood beside an object in a room the room planner's points could not get round: the planner's floor centres, corners and rings round the ends all lay in the object's disc, or the way round was a gap of half a metre between the object and a wall that none of them saw (0x720001AD and its 39 copies, a fountain's ring of posts in 0x00070144, the alcove statues of 0x02E6). With no chain, the walk was left straight through the object, which the check then refused while the body slid round it. When no chain is found, `CellRoutePlanner.GridDetour` now searches the room with the foot's own probe (the check's) on a 0.25 m grid: an A* over the points where the foot finds ground within the step-up of the point before, a diagonal past a blocked side only where the probe finds ground along it, the path pulled straight wherever the probe finds ground along a straight step, at most 4,000 points. It sees every solid the physics holds, where the discs only bound them. An end more than 0.5 m over its room's floor is not searched from: the full test suite caught Renald's counter (0xA9B4013F), where a path along the counter's top kept the runner's step-off from bringing the body down first.

| | Before | After |
|---|---|---|
| Room legs clean | 1,096,412 (99.43%) | 1,096,984 (99.48%) |
| Walks the route check refuses that the body walks | 2,220 | 1,675 |
| Walks refused that fail without the check too | 2,672 | 2,595 |
| Walks that arrive only after a recovery move | 1,137 | 1,177 |
| Building exits failing | 958 | 958 |

- 581 room walks fixed and 9 made worse (7 now arrive with a recovery move). The largest walking group grew in 25 landblocks and shrank in none. 0x02E6's 70 wrong refusals all went.
- 40 walks the check refused now pass it and arrive with a recovery move, which is most of the rise in that row.
- Of the five walks traced, two are fixed. 0x001501CD's walk is refused by the placement probe on the grid's path (the foot fits, the body does not), 0x00110128's plan steps 0.2 m into a wall to a doorway point on the room's far side, and 0x72000257's is not yet read.
- Two test rows (`A_walk_to_a_moved_standing_place_passes_the_check`: 0x720001AD and 0x00070144) fail with the search switched off.

**Doorway points where the foot stands.** Of the 1,675 walks the route check refused that the body walks, a survey of their plans found the refusal at a doorway's pair point in about 900. Two causes, both fixed in the product's planner:

- A start already past the doorway's approach. A body that came in through a doorway stands its push from it (0.5 m), nearer than the approach (0.7 m), so a walk back out went 0.2 m back first, where a thin room's wall stood (0x00110128 to 0x00110111; 344 walks in 219 room layouts). `RoomGraph.PastApproach`: a start on the line from approach to push, within 0.35 m of it and a step's height, goes straight to the push.
- A pair point inside an object or a wall. The pair is drawn square through the doorway's polygon, blind to what stands behind it: a statue's base past a narrow doorway (0x72000149 and its 39 copies, 0x72000257), an approach in a wall's thickness (0x011D010F). `CellRoutePlanner.Settled` moves a point the foot's probe refuses to the nearest point within 0.5 m in the same room where it stands, and keeps it when there is none. The crossing search (`CrossingOffsets`) tries the square pairs first and the moved pairs only when no square crossing stands, so the Cave of Alabree's rock still moves its crossing.

| | Before | After |
|---|---|---|
| Room legs clean | 1,096,984 (99.48%) | 1,097,710 (99.54%) |
| Walks the route check refuses that the body walks | 1,675 | 983 |
| Walks refused that fail without the check too | 2,595 | 2,567 |
| Walks that arrive only after a recovery move | 1,177 | 1,178 |
| Building exits failing | 958 | 958 |

- 814 room walks fixed and 88 made worse: 51 refused wrongly, 35 arrive with a recovery move, 2 fail both ways. The largest walking group grew in 13 landblocks and shrank in one (0x376A, 29 rooms to 28: a walk that arrived after two recovery moves is now refused by the placement probe).
- Tried and dropped: an approach the move could not place was first left out, which sent 90 walks the world walk had walked clean across the room to the push at a slant (191 made worse in all); and the check probing a sample past a doorway in the room that holds it changed none of the 1,675.
- Four test rows (`A_walk_to_a_moved_standing_place_passes_the_check`: 0x00110128, 0x72000149, 0x011D010F, 0x72000257) fail with the two fixes switched off, each with its own fix.
- Left: about 700 walks whose step crosses something between two points that stand, and 229 refused by the placement probe (the foot fits, the body does not).

**Steps that cross objects.** Of the 983 walks the route check refused that the body walks, 594 were refused partway along a step whose two ends stand. Profiled every 12.5 cm, most of those steps run through a solid at floor height (a statue's plinth, a table, a crate) that the planner's floor test does not see; the body slides round it. 371 of the 983 arrive only after a recovery move without the check, so for them the check is right and the plan is what was wrong. Five fixes, in the product's planner and check:

- The straight step inside a room is asked the check's own probe (`CellRoutePlanner.ProbeWalks`, the samples of `RouteConfirmer`), not only the floor test; refused, the room's detour search runs.
- Every step of a detour is asked for ground along it (`GroundAlong`). Steps between floor corners were not, and a step from the start to a corner crossed a table's edge (0x009C0191).
- A straight step refused with no way round inside its room costs 25 m more (`RoomGraph.UnwalkedLegMetres`), so the search takes a way through the neighbouring rooms when there is one: a statue as wide as its niche stands between a start and the niche's doorway, and the way round lies through the hall beside it (0x72000257 to 0x72000253 and its 39 copies). It is still taken when nothing else is.
- A doorway point moved where the foot stands (`Settled`) is kept only when the crossing through it still passes the probe; else only one point moved, then the square pair (`SettledPairAt`).
- The check's samples can climb onto something beside an indoor step (a crate's top within the step-up) and then probe the next sample from up there, starting inside the thing's side. Climbed more than 0.2 m over the step's line, a refused sample is probed again from the line's height (`RouteConfirmer.TryLineHeight`; the planner's probe does the same).

| | Before | After |
|---|---|---|
| Room legs clean | 1,097,710 (99.54%) | 1,098,428 (99.61%) |
| Walks the route check refuses that the body walks | 983 (ground 754, placement 229) | 560 (ground 333, placement 227) |
| Walks refused that fail without the check too | 2,567 | 2,272 |
| Walks that arrive only after a recovery move | 1,178 | 1,179 |
| Building exits failing | 958 | 958 |

- 735 room walks fixed and 17 made worse (12 now arrive after a recovery move, 5 refused wrongly). The largest walking group grew in 23 landblocks and shrank in none. The run took 25.0 minutes, as before.
- Five test rows (`A_walk_to_a_moved_standing_place_passes_the_check`: 0x72000257, 0x009C0191, 0x002E018F, 0x00440150, 0x5660019A) each fail with their own fix switched off.
- The Holtburg walk-out ring's last gap, `m6-stair-top-start` (placed M7 in m6-plan.md), now walks out; its row is gone from `HoltburgWalkOutDatTests.RingGaps`. More than one of the fixes closes it.
- Left: 333 refused by the ground probe (about 100 doorway crossings whose line meets something, steps through a solid with no way round that the body slides past, samples on an object's edge) and 227 by the placement probe (the foot fits, the body does not).

**No room for the body.** Of the 560 walks the route check still refused that the body walks, 227 were refused by the placement probe ("no room to stand"), 180 of them arriving with no recovery move without the check. A placement without slide may push the body no more than 0.05 m east or north (`CPhysicsObj::CheckPositionInternal`, 11.6096 0x00512960), so a sample within a body's width of a door's jamb, a counter or a table's edge is refused where the walking body slides along it. Two fixes:

- A refused placement is passed when the body, placed where it last stood (else at the path's start) and stepped there by the walking body's own transition, ends on walkable ground within 0.5 m of it, pushing on up to three times while each push gains 5 cm (`RouteConfirmer.Step`, `SweepSlackMetres`, `SweepPushes`). A gap narrower than the body still stops the step short.
- The planner places the body too, with the same step, at every sample of its straight room steps, its detours' steps and its doorway crossings (`CellRoutePlanner.ProbeWalks` with `RouteConfirmer.StandsOrSteps`), so a straight step whose line runs into something the body cannot pass takes the room's way round.

| | Before | After |
|---|---|---|
| Room legs clean | 1,098,428 (99.61%) | 1,098,593 (99.62%) |
| Walks the route check refuses that the body walks | 560 (ground 333, placement 227) | 427 (ground 355, placement 72) |
| Walks refused that fail without the check too | 2,272 | 2,128 |
| Walks that arrive only after a recovery move | 1,179 | 1,204 |
| Building exits failing | 958 | 917 |

- 167 room walks fixed and 2 made worse (both now arrive after a recovery move). 12 walks the check refused wrongly now pass it and arrive after a recovery move; 72 that failed with or without the check now run over time instead of being refused. The largest walking group grew in 12 landblocks and shrank in none. The run took 28.7 minutes (25.0 before), partly shared with a test run.
- Two test rows (`A_walk_to_a_moved_standing_place_passes_the_check`: 0x01690156, 0x00E80104) each fail with their own fix switched off. In a rewalk of the 227, the check's step alone carried 73 of the 123 clean arrivals and the planner's placement alone 33.
- `HoltburgScrivenerWalkDatTests`: the old walk into the Life apprentice's counter room now passes the check, and the body walks it and ends 1.58 m from him in the front room, within his reach; the test now walks both counter rooms (Creature and Life) and still expects the balcony's old walk refused.
- Left: 72 refused by the placement probe, where the planned line runs into something the body stops against with no way round inside the room; 355 by the ground probe.

**Ground the foot cannot find.** Of the 427 walks the route check still refused that the body walks, 355 were refused by the ground probe, 222 of them arriving with no recovery move without the check. Profiled at each refused sample, 275 found no ground from any start height in any of the path's rooms, with the sample in the room it was probed in: the foot sphere meets a wall at every height, the step's line running within a foot's width of a doorway's jamb or a wall, where the walking body slides along. Three fixes, all in the check:

- An indoor sample the foot refuses passes when the body, stepped there from where it last stood (else from the last sample the foot took), ends on walkable ground within 0.5 m of it; its ground is taken (`RouteConfirmer.TryBodyStep`, with last round's `Step`).
- A step stopped flat against something on its line steps half a metre aside each way and pushes on (`SweepSideMetres`): the walking body, coming at it a little askew, slides off one side (0x01AE0251; 22 walks arrive with it, none only without).
- A sample nearer a thin post's centre than a body can stand is passed, not stood on: the step to the next sample of the same step is taken instead (a post in the middle of a doorway, 0x013D010E and 0x0153026B, layouts of many dungeons).

The planner was given the same step at first: it then kept straight steps the probe's body gets past and the walking body got stuck on (92 room walks made worse, 47 stuck in recovery moves until over time), where its way round walked clean. So the planner keeps the strict probe and goes round where it can; such a step is taken only when nothing else is (`RoomGraph.UnwalkedLegMetres`), and then the check's step passes it.

| | Before | After |
|---|---|---|
| Room legs clean | 1,098,593 (99.62%) | 1,098,744 (99.64%) |
| Walks the route check refuses that the body walks | 427 (ground 355, placement 72) | 238 (ground 189, placement 49) |
| Walks refused that fail without the check too | 2,128 | 1,954 |
| Walks that arrive only after a recovery move | 1,204 | 1,242 |
| Building exits failing | 917 | 890 |

- 152 room walks fixed and 1 made worse. 35 walks the check refused wrongly now pass it and arrive after a recovery move, and 166 that failed with or without the check now run over time instead of being refused. The largest walking group grew in 17 landblocks and shrank in none. The run took 23.2 minutes.
- Three test rows (`A_walk_to_a_moved_standing_place_passes_the_check`: 0x0015016D, 0x01AE0251, 0x013D010E) each fail with their own fix switched off. The row's test now runs the retail physics the product and the world walk run (it ran ACE's, `PhysicsCompat.Ace`, which exists only to reproduce ACE).
- `TownNetworkWalkDatTests`: the run from Holtburg's lifestone to the Cave of Alabree's entry, pinned as `TODO(m6-alabree-entry-walk)` (the check refused the cave's ramp, `TODO(m6-ramp-step-down)`), now reaches the front point (403.8 m) and the seek reaches the portal; the pin is gone.
- Left: 189 refused by the ground probe and 49 by the placement probe.

**Every building exit a verdict.** Of the 890 failing exit entries (679 doors), 622 were never walked: 430 doors' rooms had no standing place, 192 found no walkable land node outside. And 61 walks in were refused their start: the body could not be placed at the outside node. Three changes, in the world walk's harness only (`RoomWalks`; no product change, and the room legs are unchanged):

- A door whose own room has no standing place (268 a vestibule under the door's frame whose spot lies in the next room, 162 a cell with no floor of its own) is walked from and to the nearest room through its openings that has one, at most three rooms away (`NearestStanding`).
- A door with no walkable land node outside is closed by nature, with the reason named, when the land before it lies under a building's shell more than 3 m below the sill (`over-building`, 170: a roof's or an upper floor's door, where the body can only drop) or is water (`onto-water`, 29: a dock's door); else it stays a failure, now with the sill, the land's flags and the height over the land.
- A walk in tries up to five outside nodes, nearest first, before its start is called refused (`OutsidePoints`): the map holds a node walkable where no body fits (under a balcony's edge).

| | Before | After |
|---|---|---|
| Exit walks | 23,470 | 24,244 |
| Failing exit entries (doors) | 890 (679) | 411 (223) |
| No standing place / no land outside / start refused | 430 / 192 / 61 | 21 / 15 / 49 |
| Closed by nature | 5,312 | 5,624 (over-building 170, onto-water 29) |
| Failures where the body alone walks the door both ways | 91 | 200 |

- The doors newly walked show more real failures: 200 entries (91 before) at doors the body alone walks both ways, refused by the route check at the room's standing place or with no plan. 122 more are at doors with a lane where the body alone cannot walk in from the land (64 `none`, 58 `drop-only`): a lane the map should not offer. These are the next causes.
- No test project covers the world walk's harness; the run is its check.

**Doors the body walks, and the cells it is in.** Of the 200 exit failures at doors the body alone was judged to walk both ways, two causes are fixed:

- **Structures no building owns.** 20 doors (two failing walks each) open from cells no building's portal list names, in the raw dats too (`LandblockStatics`), at 0xA9B4, 0x7E64, 0x7D64, 0xDA56, 0xDA55, 0xAAB4, 0xA1A4 and others. A body stepped in from the land reaches the room's point but stays in the land cell (0xA9B40035, 0xA9B4000B...): the physics never enters such a cell from outside, so the planner's "no lane" is right. The body's walk at a door now counts as in only when it ends in an indoor cell, and out only in an outdoor one (`OfflineWalkResult.EndCellId`, `RoomWalks.BodyAtDoor`); these doors are closed by nature.
- **A porch the shell covers.** A hut 0x0100110A (0x1747, 0x2D8A, 0x454A and its copies) covers 6 m of porch before its door, its floor 0.4 m over the land. The straight 2.5 m doorway strip ended among the shell's blocked nodes, so no plan reached the door. A straight strip now goes on along its line over its own building's shell while the body, at the sill's height, stands clear of the shell's physics, until a row opens onto the land, at most 10 m from the sill (`NavTileBuilder.ExtendOverShell`, `NavBuildOptions.LaneOverShellMetres`, `NavBuildStats.LanesOverShell`). Holtburg's tile still builds in 23 ms.
- The tile now records every door it cuts no lane for, and why (`NavBuildStats.PortalSkips`), and an exit failure with no lane names it (`lane_skip`).

| | Before | After |
|---|---|---|
| Failing exit entries (doors) | 411 (223) | 344 (189) |
| Failures where the body alone walks the door both ways | 200 | 132 |
| Closed by nature | 5,624 | 5,646 |
| Room legs clean | 1,098,744 | 1,098,744 (no leg changed) |

- `NavTileDatTests.A_huts_door_lane_goes_on_over_its_porch_to_the_land` checks 0x1747010F's lane reaches the land, and does not with the extension switched off.
- Left at those doors, 132 failing walks: 38 arrive only after a recovery move (most started from a neighbour room), 43 are refused by the check (16 at their first sample, the room's standing place: cottages 0x2B11, 0xB470), 34 have no plan (20 with a lane: statics before the door, 0x49B60121; 14 without), 16 run over time and 1 is blocked.

**Walking in the hubs (measured, not changed).** The 40 walks between each hub's arrival room and its portal rooms (the Town Network 0x00070143 to its 11 portals' rooms and back, the Facility Hub 0x8A020212 to its 9 and back) all arrive with no recovery move, replan or refusal, but in 698 s of running they press 1,718 keys: 208 turns in place, 245 turn reversals, 78 forward presses within 0.3 s of letting forward go (a false start), 35 swings. 176 of the turns in place are D134's corners: the body runs in turning, stops at the corner's point and turns the rest in place (the Town Network's arrival room to its Holtburg portal stood to turn 25 degrees of a 68-degree corner).

- Tried and backed out: a corner with less than 45 degrees left to turn at its point run instead of stood. In the hubs it cut forward re-presses 78 to 25 and turns in place 208 to 143, but it broke `IndoorRunThroughDatTests` (a dungeon's legs run through its doorways: a running turn reaches the next doorway askew and walks it) and `AlabreeCaveWalkDatTests`, and sent one Facility Hub walk into the stair top by 0x8A02015E (three recovery moves). Kept to corners with no doorway within 6 m past them, it runs no hub corner: nearly every hub corner has a doorway just past it.
- So the hubs' false starts are the dungeon steering rules themselves, together: D134's stand-and-turn at a corner and the doorway's line-up (m6-dungeon-run-share). Changing them is the owner's decision.

**The walk bench and the hub crossing (2026-11-18, the owner: finish the hub fix).** The live climber's crossing of the Facility Hub, from its drop 0x8A020212 to the Tusker Quarters portal's front, ran 165 m in about 31 s with 18 turns, and its last rooms turned in place and swung before the stop. A bench now replays fixed walks exactly, before and after a change (`tools/walk-bench.ps1`; `WalkBenchDatTests`, a tool, not a check): the 40 hub legs with each steering, the hop's front walk from each hub's arrival to every portal's front as the journeys run it live (27, the live crossing among them), every dungeon ground's whole tour with each steering (937 legs each), and with `-Rooms` this tool's rooms verb on 77 fixed landblocks (the first 60 with rooms, the dungeon grounds and both hubs: 60,788 legs). It writes `cache/walk-bench/` and prints every walk whose verdict or counts changed. Two causes of the live crossing's stumble were fixed:

- **A kept portal over an opening's middle.** The Facility Hub's halls are built of 10 m cells joined by openings as wide as the cells, and the room graph crosses an opening only through its middle. The Quarters portal's reach covers the middle of the 10 m opening into its front's room, so the walk went the long way: through the room beside it, 4 m south, 5 m east, 4 m north and back west, a U of stand-and-turn corners in the last 10 m (the live swing and turns in place before the stop). An opening whose middle a kept portal or a plate refuses now also offers the nearest clear crossing either side, square through the opening and keeping the body's half width and the 0.5 m doorway tolerance off its sides at its narrowest (`RoomGraph.Beside`, `CellRoutePlanner.CrossingSpan`). Nothing changes where nothing is kept off.
- **The predictive steering handed whole storeys over.** A window of the route with a leg passing over or under another leg is left to the ordinary steering, since the plans' tracks are flat. Every leg of such a pair was marked, so the crossing's middle hall at -6 m, over its last rooms at -12 m that the walk reaches 100 m later, was the ordinary steering's from end to end (870 steps). The rule now holds only for a window holding both legs of a pair.

| Bench, 60 steps a second | Before | After |
|---|---|---|
| The live crossing: route m, run s, keys, turns in place, reversals, false starts, swings, D134 corner turns, wall touches | 162.5, 23.8, 66, 8, 7, 0, 0, 6, 1 | 144.4, 18.2, 38, 3, 4, 0, 0, 1, 2 |
| Hop front walks (27): rungs, keys, turns in place, reversals, false starts, swings, corner turns, wall touches, run s | 0, 906, 105, 99, 24, 5, 77, 19, 327.6 | 0, 892, 102, 104, 27, 5, 63, 24, 325.5 |
| Hub legs (40), either steering | ordinary 0, 1,668, 208, 194, 93, 23, 174, 38; predictive 0, 1,560, 183, 178, 72, 22, 153, 38 | unchanged |
| Dungeon tours, ordinary (937): rungs, replans, keys, turns in place, reversals, false starts, swings, corner turns, wall touches, run s | 0, 10, 18,904, 3,199, 1,918, 836, 164, 1,707, 129, 6,012 | 0, 10, 18,860, 3,176, 1,908, 837, 161, 1,709, 128, 6,007 |
| Dungeon tours, predictive (937) | 0, 10, 16,292, 1,861, 1,416, 414, 120, 431, 103, 4,863 | 0, 11, 16,450, 1,861, 1,436, 449, 124, 385, 86, 4,823 |

- The live crossing is 18 m shorter: 18.2 s at the bench's Run skill 230 (top speed about 9.9 m/s). Live it also stops at the hub's two doors, which the offline rig walks through open, and its door stops are not in these numbers.
- **Worse, by name.** The predictive Tusker Holding tour replans twice at 0x02A8 where it replanned once (a jump leg: the plates' landings replan by design). The hop walks' wall touches rose 19 to 24 (not traced walk by walk).
- **The hub legs did not move.** Their stand-and-turns are D134's corners and the doorway line-up, as the measurement above found; neither fix touches a leg with no kept portal and no layered pair. Two changes to them were measured and set aside:
  - *A taut route.* Each crossing of a flat doorway moved along its opening, pass after pass, to where the straight line between its neighbours meets it (the route's zigzag between the openings' middles drawn straight). Kept to crossings the line passes straight through, to the straight run's drift (1 m) and the body's half width off the opening's sides, and to rooms joined across their whole side: the hub legs' false starts fell 93 to 41 and reversals 194 to 171, but turns in place only 208 to 193 and swings 23 to 22, the hub leg 0x8A020123 to the drop took 2 recovery moves at the stair top by 0x8A02015E, the rooms verb lost 2 clean legs net (8 worse, 6 better), and `AlabreeCaveWalkDatTests`' tour and `IndoorRunThroughDatTests`' Crater Caves and den legs failed: the next leg starts 0.2 m elsewhere and meets a fragile corner (0x016201B3). Looser, against the openings' sides, it brought the body against the jambs (the Tusker Lodge's 0x57490268, the Crater Caves' 0x019A028D).
  - *Corners run round.* A corner run at a run, the turn key down where the arc of radius speed over turn rate meets the next leg. Judged by the rooms' cells alone it cut the hub legs' corner turns 174 to 38 but grazed the 3.4 m doorways' jambs (wall touches 38 to 77, 24 wall frames, recovery moves). Judged by the physics' sweep of the body and by the straight run's drift from the confirmed route, almost no hub corner qualifies: at the climber's 10 m/s the arc's radius is about 4 m, and a 90 degree corner cuts 1.7 m inside the route. In the dungeons it added swings (190 to 213).
  - So the hubs' stand-and-turns stay until the corner itself is rebuilt with room to run it; the bench is the measure for that work.
- Lifestone's own, not retail.

**Letting the game walk her in (2026-11-18, the owner's go-ahead to hand a portal walk to the game's own walk once the way is clear).** ACE answers a Use of a portal from beyond its use radius with its own walk (`CreateMoveToChain`, `Player_Move.cs`: `MoveToPosition` to the portal's location; `MoveToObject` for anything else), which the client runs straight, the server checking the radius every 0.1 s. A hop's walks now carry the portal as their Use target (`TravelRequest.Use`, `NavRunner.UseTarget`): the walk to the front and the seek end as soon as the straight line from her body to the portal is one the body runs (`UseHandOver.Refusal`), and the hop sends the Use as a player's click would. Checked every metre she moves, cheapest first: the server's walk ends inside ACE's 15 s MoveTo timeout at her run speed; no hostile creature's awareness disc touches her or the line; the line meets no other portal's keep-off disc at its storey (the walk's own portal is its end) and no plate; no closed door stands on it; it stays on one floor (every 0.5 m a floor within a step of the last, out of the statics, its ends within 1.5 m of height); and the body, swept at its width through the physics, meets no wall or door frame. Indoors only; outdoors the walk goes on as before. One log line at the hand-over: the distance, the line's clearance (the widest of the body, a quarter metre and a half metre more that the sweep passes) and why. After the Use her body's progress toward the portal is read: a game's walk that brings her no nearer by 0.5 m for 3 s is stopped by name, and her own steering walks up and uses the portal once more (recovery=own-steering, the hop's one recovery).

| Bench (`hops-seek`, `hops-use`: the hop's front walk and its seek to the portal's 2 m; with the hand-over, ACE's walk-to-use run on the client's own MoveTo from where she hands over) | Own steering to 2 m | Hand-over |
|---|---|---|
| 27 hops: not arrived, refusals, replans, run s, keys, turns in place, reversals, false starts, swings, corner turns, wall touches | 2, 1, 2, 367.0, 1,156, 155, 115, 38, 5, 64, 40 | 1, 0, 0, 312.3, 798, 90, 76, 18, 5, 37, 27 |
| The live crossing, `facility-hub/hub-quarters`: route m, run s, keys, turns in place, reversals, corner turns, wall touches | 155.7, 21.9, 50, 5, 5, 3, 0 | 133.5, 18.9, 42, 5, 4, 2, 0 (handed over 10.8 m out; the game's walk 1.9 s) |

- Every hop handed over. 25 were faster; the Blackmire hop, whose seek the physics refuses with her own steering, now arrives; the Eastham hop is 0.3 s slower with one more turn in place (the game's walk turns to face first). The Tusker Cave hop has no plan from the drop with or without it, as before.
- The live crossing hands over 10.8 m from the portal, in the front's room: the rest of the hub crossing (two ramps, three floors) is walked by her own steering as before.
- The 40 hub legs walk room to room with no Use target, and the dungeon tours have none: both unchanged to the key.
- `UseHandOverTests`: a clear line hands over; another portal's disc, a closed door, a ledge or a storey, ground no room holds, walls, a creature and a line past the timeout are each refused by name.
- Lifestone's own, not retail.

### The land seams (W8, offline only so far)

After the route-check fixes, 2,265 of the long walks' 2,851 recovery moves (79%) were on or within half a metre of a land triangle too steep to walk, though such ground is about 2% of the walkable ground; 705 were on the triangle itself. The tiles marked steep land only at node centres, and a route could run anywhere in the open node beside it, which brought the foot sphere against it. The fix: every open node within 1 m of a too-steep land triangle, a neighbour landblock's included, costs the top of the slope ramp, and the top of the ramp costs 18 more per metre (`NavBuildOptions.SteepLandKeepOff`, `PathPlanner.SteepEdgeWeight`; Lifestone's, not retail). It is a cost and not a block, so no passage closes and the world routes stay valid. The same 2,000 starts and goals, with a soft cost first (the ramp's top only) and then the heavier one:

| | Route check | Soft cost | Heavier cost |
|---|---|---|---|
| Recovery moves | 2,851 | 2,591 | 2,458 |
| Walks with one | 985 | 913 | 859 |
| Not arrived | 212 | 217 | 210 |
| Clean, 1 to 1.5 km | 91.8% | 92.1% | 92.4% |
| Clean, 8 to 12 km | 62.2% | 63.4% | 65.8% |
| Moves near steep land | 2,265 | 2,033 | 1,884 |

- Arrivals by band moved by 0.1 to 0.3 points, all up. Against the route check alone, 28 walks now arrive and 26 that arrived no longer do (the check elsewhere on the new route 15, no path 8, over time 2, stuck 1): a different route meets different faults. Walks ended by the check went from 79 to 87; walks not arriving from 212 to 210.
- Walked distance did not grow: the median walk is the same length, the 99th percentile 1.5% longer.
- The run took 57 minutes, against 62 for the route check alone. The 1 km planner timing test reads the same with and without it (about 16 ms alone).
- **Most of what is left is forced.** At the 326 near-steep places that keep recovery moves in both runs, the moves did not drop (1,457 with the route check, 1,460 with the heavier cost); the cost removed places, not moves at the places that remain. A cost seven times heavier than the soft one bought only 5% more. These are crossings the route cannot avoid, such as narrow necks between steep faces, and the next fix there is how the body steers through them, not where the route goes.
- Because one cost marks both kinds, natural walkable slopes at the walkable limit also cost the extra 18 a metre. That is a choice to keep off ground where the body slides, and the walk lengths above show it costs little.

**Steep slivers.** Walking two of the remaining spots from 16 headings on the real ground (a scratch probe, not kept) showed what is forced there. Three or four land triangles meet at a corner, one of them too steep, and a sliver of it a few tenths of a metre wide cuts into the line between walkable node centres. The body cannot step down onto a too-steep triangle however thin, so heading down across the sliver it wedges at the corner; a line 0.3 m over passes. The 1 m nodes cannot see such a sliver, and the cost above could not move the route off it, because every node near the corner costs the same. So an open node whose square overlaps a too-steep triangle at any of 16 points a quarter metre apart is flagged `NavFlags.SteepEdge`. It costs 40 more a metre (`PathPlanner.SteepTouchWeight`), and neither the planner's pull nor the runner's straightening crosses one the node path kept off. It is still a cost and not a block. The same 2,000 starts:

| | Heavier cost | Steep edges too |
|---|---|---|
| Recovery moves | 2,458 | 1,482 |
| Walks with one | 859 | 566 |
| Not arrived | 210 | 199 |
| Clean, 1 to 1.5 km | 92.4% | 95.1% |
| Clean, 2 to 3 km | 87.5% | 91.3% |
| Clean, 4 to 6 km | 78.5% | 84.8% |
| Clean, 8 to 12 km | 65.8% | 75.0% |
| Moves on a 24 m line, at a corner | 976, 729 | 527, 402 |

- 498 walks became clean and 69 stopped being clean (57 of those now arrive with a recovery move). 19 blocked walks now arrive; 8 that arrived no longer do (no path 3, stuck 2, the check 2, over time 1). Walked distance did not grow (99th percentile 0.8% longer). The run took 58 minutes.
- Against the route check alone, recovery moves fell from 2,851 to 1,482 (6.8 to 3.5 per 100 km), and clean 8 to 12 km walks rose from 62.2% to 75.0%.
- **A new open spot.** The two walks now stuck for good (and 27 recovery moves) are at one place, landblock 0x5219 at about (21, 191.5), on its north edge: from the south the body stops at y 191.45 on every heading from north to north-west, so a solid of the next landblock's reaches about 0.07 m over the border where this tile's nodes are open. Earlier runs met it twice; the new routes go that way more often. It is the cross-border shadow of a neighbour's static, not the steep edges. Fixed below.

**Statics reaching over the border.** The physics lists a neighbour's static in a cell of this landblock (a foreign shadow) only where the static's own sphere enters it, but the body's sphere reaches the neighbour's cell before its centre does, so a solid just past the border still stops the body there. The tile now also stamps any neighbour's outdoor static whose solid, inflated 0.58 m, reaches over the border (`reach_over=` in the build line; docs/wayfinding.md), and the streamer rebuilds every attached neighbour's tile when a landblock attaches, since shadows, sea walls, steep land near the border and these statics all change it (the steep-land commits had left the last two unrefreshed). The same 2,000 starts, on a world graph rebuilt with this build:

| | Steep edges | Border statics too |
|---|---|---|
| Recovery moves | 1,482 | 1,402 |
| Walks with one | 566 | 550 |
| Not arrived | 199 | 199 |
| Stuck for good | 7 | 3 |
| Clean, 1 to 1.5 km | 95.1% | 95.4% |
| Clean, 2 to 3 km | 91.3% | 92.2% |
| Clean, 4 to 6 km | 84.8% | 85.4% |
| Clean, 8 to 12 km | 75.0% | 78.2% |
| Moves on a 24 m line, at a corner | 527, 402 | 421, 417 |

- Five of the seven stuck walks, at four border spots (0x5219's north edge, and edges of 0x2340, 0x4DB2 and 0x8694), were stopped by one model, 0x02000258, reaching over the border; all five now arrive with no recovery move. 179 walks became clean and 80 stopped being clean (60 of those arrive with a recovery move). 13 blocked walks now arrive and 14 that arrived no longer do. Walked distance did not grow (99th percentile 0.9% longer).
- **A new open spot:** one walk is stuck for good in 0xB2D3 at about (96, 35), 2.6 m from a building's static 0x020005D9, mid-landblock and not at a border.
- **The cost.** The same 24 starts back to back: 0.8 minutes before, 1.0 with the rebuilds alone, 1.1 with the reach-over stamp too, so about 35% more time a start, mostly the rebuilds (the 2,000 took 75 minutes, not 58). An attach that comes with others still rebuilds the shared neighbours once for each; coalescing them is open.
- **A stale graph.** The first run of this fix used the saved world graph, whose regions came from the earlier tiles; the new stamps split some regions, so 43 starts threw an index fault and about 145 walks were not picked. The walker now refuses a stale graph by name and logs where a start threw.
- **The exact-line placement fault**, traced on the way (`TODO(placement-cell-edge)`, docs/wayfinding.md): a point exactly on a land cell's edge lies in both cells, the cell search keeps the west or south one, and a placement must end in the cell it was asked for, so it is refused. It touches only the route check's placement probe, which already steps 0.1 m off, and not the walks' stalls.

### The places (W6)

From the pinned world data with no database: 26,430 objects and 2,349 places portals drop a body, each object standing solid in the walk's world, 0 threw. "NPC" is a creature not flagged attackable, so it includes some that are not people.

| Kind | Places | Reached | Walks clean |
|---|---|---|---|
| Portal | 4,530 | 93.1% | 94.2% |
| Lifestone | 255 | 94.5% | 95.8% |
| Vendor | 1,043 | 77.9% | 79.1% |
| NPC | 20,602 | 85.7% | 84.4% |
| Portal drop | 2,349 | 89.8% | 93.1% |

Every failed walk was repeated with no objects in the world. 2,171 walks go wrong only with the objects there, and are clean without them. 2,141 go wrong either way. Of the 2,213 drops with somewhere to walk to, every one holds a body and none needed raising. The other 136 have nowhere within reach to walk to, so they were not placed. 4,084 indoor places have no way outside and no drop joined to them by the map, which also counts places behind closed doors.

Generator-made creatures move, so they are not here. Doors are not loaded: their state is the server's, and W9's.

### The ACE comparison (W7)

9,832 single steps from a sample of 598 landblocks (walls, slopes, landblock edges, doorways open and closed), each placed and stepped through our physics with ACE's switches on, then through ACE's own, from the dats alone. **9,607 agree (97.7%).** Doorways agree except for 28 of the 4,636 (14 doors, each run twice); landblock edges all 884. Every doorway step was run twice, 13 mm apart, and the pairs came out the same.

| Difference | Moves | Reading |
|---|---|---|
| Against a wall on a slope, the two end apart (median 0.33 m); ours is the further in 178 | 190 | Most hit a diagonal terrain edge. Fits row 15 of the fixed ACE deviations (ACE's edge slide discards the slide), not proven |
| Through a doorway, the same x and y, ours 0.3 to 0.43 m lower | 24 (12 doors, twice) | Not read yet |
| In another cell from ACE, up to 2.7 m higher; in 2 of the 3 ours ends 2.3 m from a 1.4 m step | 3 | Not read yet, and the one to read first: a 1.4 m step should not go 2.3 m |
| Other: placement or a slope's end | 8 | Not read yet |

ACE is not ground truth: 26 of its deviations from retail are fixed here. These differences are not yet judged against retail. Housing barriers are skipped on both sides.

### The known defects

| Defect | Found again? |
|---|---|
| 24 m cell edge | **Yes**, by the walks' recovery moves |
| Neydisa's front | **Yes**, by the oracle: refused on open ground at (74, 74) and (74, 78), beside the portal at (73.93, 74.99). Its cause is still not named |
| Alabree ramp | **Partly.** W5 flags the same mouth: 0xA7B30104 cannot hold a body in its own room, so the walk between 0103 and 0104 has nowhere to stand, and 0102 has no floor frame. That is not the ramp's step as the live walk met it. Whether they are one fault is W8's question |
| Abode pits | **Yes**, by W5: 0x5C45 splits into groups of 166, 152, 139 and 78 rooms, matching `TODO(m6-abode-pits)` |
| Holtburg cave mouths | **Yes**, by W5: 0xA9B40184 and 0189 cannot hold a body in their rooms, and 017F has no lane between its door and the ground |
| Renald's back room | **Yes**, by W5: 0xA9B4013F, 0140, 0141 and 0142 are closed as `no-floor-at-sill` and the body does not cross either. It is a closed door, so it shows in the closed-door list rather than among the failed walks |

The strips along the map's south row and west column are 466 rim landblocks. They are small landmasses of their own, apart from the main one, and only 1 of their 480 edges failed.

### The machine

The climb showed no frame drop beside the walk (see the tool page). The default test `PathPlannerTests.A_one_kilometre_plan_takes_low_single_digit_milliseconds` (120 boxes a tile, a 50 ms limit) failed in three full-suite runs at this branch: twice beside the oracle and once after it ended, with other work holding the machine at about 55% busy. Alone it passes every time, at 18 ms with the oracle running. Master at 7206b1be fails it the same way in the full suite on this machine, so neither the walk nor this branch is its cause. It is a limit that is too tight for a full-suite run on a busy machine, and the item that next owns the planner should decide what to do with it.

The walk used to pause on any command line that named a gate script. The oracle's second run paused about 80 times, 10 minutes in all, with no sign of a real gate: each pause lasted seconds. `WalkPauseRule` now requires a line to run the script. A line that only looks like a run still pauses, as that is the safe side.

## Live sample and the full live walk

W9 needs the backend free, item 7's fix for a player removed when a landblock unloads, and a lead to run it. It uses `Lifestone Throwaway` on `test-tools`, one lane, with deaths expected.

W10 is a written plan only: the walking order for each landmass, parallel lanes and the accounts they need, keeping the walker alive, ACE's load, resume after death or disconnect, sharing the server with the standing climb, and a time and cost figure from W9's measured legs.
