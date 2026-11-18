# The world walk

The world walk proves the pathfinding on the whole of Asheron's Call, landblock by landblock, with no server and no Godot. Its spec, phases and decisions are in [milestones/world-walk.md](milestones/world-walk.md). This page is the tool.

## Running it

```powershell
# From a checkout, with the .NET environment of CLAUDE.md set
dotnet build src\Lifestone.WorldWalk\Lifestone.WorldWalk.csproj -c Release --disable-build-servers
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll census --out=cache\world-walk\<run>
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll pilot --out=cache\world-walk\<run> --sample=100 --pick-seed=1
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll graph --out=cache\world-walk\<graph>
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll walks --out=cache\world-walk\<walks> --graph=cache\world-walk\<graph> --ignore-climb=on
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll oracle --out=cache\world-walk\<oracle> --graph=cache\world-walk\<graph> --ignore-climb=on
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll detour --out=cache\world-walk\<detour> --other=cache\world-walk\<walks> --graph=cache\world-walk\<graph>
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll map --out=cache\world-walk\map --census=cache\world-walk\<census> --graph=cache\world-walk\<graph> --walks=cache\world-walk\<walks> --oracle=cache\world-walk\<oracle>
dotnet src\Lifestone.WorldWalk\bin\Release\net10.0\Lifestone.WorldWalk.dll compare --out=cache\world-walk\<one> --other=cache\world-walk\<other>
```

For `rooms`, `--predictive=on` steers the dungeon legs with the predictive steering (docs/wayfinding.md, "Predictive steering"; default off, the ordinary steering this tool's runs measure), and `--trace-leg=FROM-TO` (the cells' low words, as the results name them) writes that leg's steps and decisions to `leg-<landblock>-FROM-TO.txt` in the run's folder, walked where the run walks it.

From a worktree, also pass `--dat-dir` with the main checkout's `client` folder. The options are registered in `src/Lifestone.Client/Options/OptionCatalog.WorldWalk.cs`.

| Verb | What it does |
|---|---|
| `census` | Classes every one of the 65,025 landblock ids and builds its collision and tile |
| `pilot` | A spread of landblocks of every kind: census, walks east and north at top speed, a walk east at low-level speed, and the physics probes |
| `graph` | Joins every tile into areas (ground the planner can go both ways across) and landmasses (ground joined at all) |
| `walks` | Walks each land edge both ways, at top speed and at low-level speed. An edge whose ends share no area is recorded, not walked |
| `oracle` | Tests the map against the body: stands and steps on a 4 m grid, ignoring the tiles, and refines to 1 m where the two disagree |
| `detour` | For each walk that found no path, widens the planner's view a ring of landblocks at a time and plans again, up to 12 rings |
| `rooms` | Every landblock with rooms: every doorway checked, every passable one walked, closed ones tried with a true physics step, and each way into and out of a building, through the journeys' own route selection. Needs `--census` |
| `long` | From a sample of the main landmass, a walk in each of four distance bands, to stress replanning. Needs `--graph`. `--routes=FILE` gives the walks the world routes file a `graph` run wrote (`world-routes.bin`), and `--trace=FILE` writes every walk's full log. A graph built by another tile builder is refused when a landblock has more regions than it knows ("stale graph": rebuild it) |
| `places` | Walks to every portal, lifestone, vendor and NPC of the pinned world data as a real solid object, with a no-objects control on each failure, and checks every portal drop. Needs `--world-sql` |
| `moves` | The awkward single steps of a sample of landblocks through our physics with ACE's switches on, for `tools/ace-compare`. Needs `--census` and `--graph` |
| `map` | Writes `map.html`: terrain, landmasses, areas and, with `--walks` and `--oracle`, each landblock's verdict |
| `compare` | Compares two runs' verdicts, leaving out timing. Exit code 1 means they differ |

`tools/ace-compare` is a separate program, outside the solution because it links the pinned ACE build under `server/runtime/ace` of the main checkout:

```powershell
dotnet build tools\ace-compare\AceCompare.csproj -c Release --disable-build-servers -p:AceDir=<main checkout>\server\runtime\ace\ -o cache\world-walk\bin-ace
dotnet cache\world-walk\bin-ace\AceCompare.dll <main checkout>\server\runtime\ace <dat dir> cache\world-walk\<moves>\results.jsonl cache\world-walk\<out>
```

It needs no server and no database, and listens on nothing. It prints a table and writes each difference to `differences.jsonl`.

Exit codes: 0 done, 1 a compare found differences, 2 a bad command line, 3 stopped by name at a cap.

## The run's folder

Everything in a run's folder derives from the dats, so it stays under `cache/` and is never committed.

| File | Holds |
|---|---|
| `results.jsonl` | One line per landblock, repeating exactly for the same input |
| `timing.jsonl` | What does not repeat: wall-clock costs |
| `notes.log` | The run's story: start, progress, pauses, end, peak memory |
| `PAUSE` | Create this file to pause the run; delete it to resume |

A landblock is on disk the moment it finishes, so running the same command again resumes where the run stopped.

## Light mode

The owner set Light mode on 2026-11-06, and there is no heavier mode. It applies to every run, and the run checks it took effect:

- 4 threads at most, on cores 16 to 31 only. Cores 0 to 15 hold the 96 MB cache games favour, so they stay free for Godot.
- Below-normal priority.
- An 8 GB heap cap and a 5 GB cap on what a run writes. Past either, the run stops by name. The cap is on the heap, so the whole process can pass it a little: `detour` peaked at 8.2 GB on 4 threads and 6.8 GB on 2.
- The PC stays awake only while a run is going. Nothing is written to the machine.

A run checks the process list every 5 seconds and pauses, between landblocks, for a live gate, smoke, soak, a slice-check gate or loopback tests. A live gate's "contended" pass counts only other Godot runs, so this tool's load there would be a real gate failure, not an excused one. It also pauses for `tools/climb.ps1` unless given `--ignore-climb=on`.

Every pause and resume goes in `notes.log` with the time. If a gate fails on frame time while the log shows the walk running, that is the walk's bug. `WalkPauseRule` decides, and a script counts when a command line runs it, not when it only names it.

`--ignore-climb=on` keeps a run going beside `tools/climb.ps1`. Measured on 2026-11-06, the climb had 0.51 and 0.33 slow frames a minute beside the walk against 0.67 from an earlier run, at 59.9 fps. Its experience rate was not measured.

## How a walk works

`OfflineWalkRig` (`src/Lifestone.Client/Movement/Wayfinding/OfflineWalkRig.cs`) walks one request on an offline session: the headless streamer, the retail physics walker, the route check and the runner, as the island walks run. Each walk gets a new world, map and session, so no walk depends on the one before.

**Repeatability.** The clock is `SimulatedTime`, and simulated time stands still while the streamer has work, so how long a landblock took to build cannot change what the body meets. The runner's scars live on the same clock (`NavRunnerOptions.ScarClock`).

**What counts as OK.** Only `OfflineWalkResult.Clean`: arrived, with no recovery rung, no route refusal and no tile failure. A walk that arrives after a recovery move is a finding, not a pass.

**Sharing.** Every thread's world shares one store of finished landblocks (`CollisionStore`), because a `LandblockCollision` is immutable once built. The store keeps the most recently used 800, about three rows of the map.

## Doors the body cannot walk

`rooms` walks every way out of a building out and back through the journeys' own source. A door where either walk fails is then walked by the body alone, with no planner and no route check (`RoomWalks.BodyAtDoor`): straight in from the ground and straight out to it, from points 2.5 m and 4.5 m past the door's outer approach, straight out and at 45 and 80 degrees either side, so that stairs along the wall are found. The verdict, in `body`:

| Verdict | The body |
|---|---|
| `walks` | Some walk in and some walk out arrive with no recovery move |
| `in-only` | Only a walk in does |
| `drop-only` | Only a walk out does: a balcony, the body drops |
| `recovery-only` | A walk in arrives only with a recovery move, a jump onto a sill over the step-up |
| `none` | Neither |

A door with no lane whose verdict is not `walks` or `in-only` is the planner's right answer, not a failure: it goes to `exit_natural`, not `exit_bad`. Retail's edge slide (`CTransition::edge_slide`, 11.6096 0x0050BEA0) keeps a walking body on a ledge over a drop with a steep face, so a door a metre over the ground with no step is jump-only both ways; many houses have such a second door, a raised nook inside it. A door with a lane keeps its failures in `exit_bad`, with the verdict beside them.

A door that opens straight into another building's door (`RoomGraph.PairedDoors`) has no ground outside. It is not walked to the land; the rooms walk it as a leg between the two rooms, and `exit_kinds` counts it as `paired`.

## The checker's own checks

`OfflineWalkRigDatTests` proves the rig is not blind. Each planted fault is a wrong map the walk must report:

- **A lost wall.** Holtburg's tile forgets its buildings, so the plan runs through one. The physics refuses it and the walk ends blocked.
- **A sealed goal.** The goal is walled in on the map alone, and the walk ends no-path.
- **A lost passage.** A blocked band cuts a tile between two areas, and the oracle must report one `closed-passage`. The same band with one area on both sides must report none.
- **A known link.** Hops along a link the map already holds count as known drops, not findings.

`IslandRunDatTests.The_offline_rig_agrees_with_the_walk_through_the_streamer` runs the island's 14 routes through the rig, which must end them the same way as the test it was lifted from.

A pilot proves `compare` too: the same sample on 2 and on 4 threads gave identical results.
