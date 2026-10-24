# Game core (M1 wave 2a)

The shared Godot infrastructure every visual track plugs into: services, streaming, the floating origin, the render path, the camera, the main scene and the measurement harness. The render path has its own contract in [`render-contract.md`](render-contract.md).

## 1. Layers of the code

| Project | Engine | Role |
|---|---|---|
| `src/Lifestone.Data` | none | Decoding and retail algorithms (wave 1). |
| `src/Lifestone.Client` (new) | none | Client-side logic between data and presentation. Coordinates and map coordinates, the floating origin, the landblock streaming planner, the frame-budget scheduler, the worker pool, the refcounted resource cache, surface shader variants, DXT mip chains (box filter + BC1/2/3 re-encoding), frame-time statistics, named locations. From M2 also the session layer (A0: the game-state reducer, event bus and Action API, in `Session/`). No Godot, no sockets, no file I/O (§1.1). Tested by `tests/Lifestone.Client.Tests`. |
| `src/Lifestone.Net` (M2) | none | The AC protocol: the transport, the session (`AcSession`), the message codecs and the loopback gate (`LoopbackPolicy`). BCL only, with no package or project reference, so it does not see Data or Client. The only project that opens sockets. Tested by `tests/Lifestone.Net.Tests`. |
| `src/Lifestone.Headless` (M2) | none | The headless session host (DoD 13): a console program, run as `dotnet Lifestone.Headless.dll` (no app host), that logs in with the test account, enters the world with portal space's timing, checks the server mod, logs off and quits (`docs/session.md` §20). It references Data and Client, reads the files a session needs and hands Client their bytes. `--scenario=login` is built; `holtburg-laps` and `academy-exit` are loud stubs (exit 2) until step ST16. |
| `src/Lifestone.Game` | Godot 4.7 | Presentation only. Godot nodes and resources built from the libraries above. Axis and winding conversion happens only in `scripts/Core/AcToGodot.cs`. It reaches the session only through `Lifestone.Client` and never names `Lifestone.Net` (M2). |

`Lifestone.Client` keeps Godot replaceable: everything that decides *what* to load, *where* things are and *which* retail render state applies is engine-agnostic. Only resource creation and nodes are Godot-specific.

| Test project | What it tests |
|---|---|
| `tests/Lifestone.Data.Tests`, `tests/Lifestone.Client.Tests` | Data and Client. Tests that need the dats skip when they are missing. |
| `tests/Lifestone.Net.Tests` (M2) | Net, with no server: the protocol vectors, the codecs, a fake ACE, fuzzing and a virtual soak. |
| `tests/Lifestone.Loopback.Tests` (M2) | Every test that needs the local backend, in one assembly whose tests run one at a time, serialised across worktrees by a lock file in the main checkout's `cache\`. Each carries `[Trait("Category","Loopback")]` and skips, with the reason, unless the backend is up with the Lifestone server mod ready. |
| `tests/Lifestone.ServerMod.Tests` (M2) | The server mod's shared wire contract and its source rules. The mod itself builds against the pinned ACE, outside the solution. |

Helpers every test project shares live in `tests/Shared/` and are compile-linked into each. Long soaks and fuzz runs carry `[Trait("Tier","Soak")]`; the everyday run and the smoke suite's test step leave them and the loopback tests out with `--filter "Category!=Loopback&Tier!=Soak"`.

### 1.1 Project rules for M2

- **The Client rule.** `Lifestone.Client` may use `Lifestone.Net`'s records and interfaces; it opens no sockets and does no file I/O. Hosts supply bytes and Streams: the Godot game and the headless host read the files (the server's secrets and pids files, recordings) and pass their contents in. The architecture tests enforce it: GodotSharp only in Game, `System.Net.Sockets` only in Net, and no `File.`, `FileStream`, `Directory.` or `Process.` in Client.
- **The clock rule.**
  - Every network, session and action timeout reads `System.TimeProvider`. Tests drive `ManualTimeProvider` (`tests/Shared`), whose timers fire on `Advance`. Net and `Client/Session` read no ambient clock (`DateTime.Now`/`UtcNow`, `Environment.TickCount`, `Stopwatch`, `Thread.Sleep`); `SessionClock` is the one exception.
  - Frame-stepped machines (portal space, physics, avatars) take an explicit `double now`: monotonic real seconds, advanced once per frame by the host, as `LivingStatic.Animate` and `EffectActor.AdvanceTo` already do. They never read `DerethTimeController.ClientClock`, which the time panel scales and pauses.

### 1.2 Process priorities and frame budget (M2)

**Process priorities** (lower runs first). The M1 nodes keep theirs; the M2 rows are planned by the wave-2 tracks and are
claimed here so no two tracks pick the same number.

| Priority | Node | Owner |
|---|---|---|
| −2000 | `TeleportTourRunner` (its `FrameTail` node, `int.MaxValue`, runs last) | M1 |
| −1100 | `FrameBudgetShare` (subtracts the tunnel and the session pump from the main-thread budget, below) | M2 G1 |
| −1000 | `GameServices` (drains `MainThreadQueue`) | M1 |
| −900 | `SessionHost` (ticks the live session) | M2 G6 |
| −800 | `PlayMode` (runs the queued play-mode work: builds the player, teleports, tears down) | M2 G6 |
| −20 | the player controller | M2 G5 |
| −10 | `CinematicDirector`, `InteriorTourRunner` | M1 |
| −5 | the chase camera | M2 G5 |
| 0 | `FlyCamera` | M1 |
| 5 | `ObjectsSystem`, `InteriorController` | M1 |
| 6 | `LivingStaticsSystem` | M1 |
| 7 | `CellObjects` | M1 |
| 10 | `WorldRoot` | M1 |
| 12 | the server-objects layer | M2 G5 |
| 15 | `PortalSpacePresenter` | M2 G1 |
| 20 | `SkyController` | M1 |
| 25 | `RetailUiLayer` (the retail UI's frame: runtime tick, composite in its own 1 ms slice (a surface that has started always finishes), tile upload; retail-ui.md §17) | M2 G2 |
| 30 | `GoldenViewpointsRunner` | M1 |
| 40, 50 | `SkyChecks`, `BlendChecks` | M1 |
| 100 | `SpellViewerSky` (spell-viewer scene) | M1 |

**Frame budget** (one frame's main-thread time that the game's own work may take; the 16.7 ms frame at 60 fps also holds
Godot's rendering, which these numbers do not include):

| Share | Default | How it is bounded |
|---|---|---|
| `MainThreadQueue` (attach steps, uploads, everything queued through `GameServices.MainThread`) | 4 ms (`--budget-ms`) | Settable (`MainThread.Budget`). It keeps its 4 ms until a share below is taken, and is never cut below 1 ms |
| The portal tunnel (the creature-mode view's update and its materials) | taken from the queue while it shows | `FrameBudgetShare` subtracts it from the queue's budget |
| The session pump (decode and reduce the server's messages, 2 ms) | 2 ms (`--session-budget-ms`) | `FrameBudgetShare` subtracts it from the queue's budget |
| The retail UI's composite | its own 1 ms slice per frame, round-robin over the roots (`RetailUiLayer.CompositeSliceMilliseconds`). `UiCompositor.Composite` always paints at least one whole surface, and each login screen is one 800x600 root, so the slice only bounds the surfaces after the first: a first paint takes 3 to 8 ms (the data check 7.6, character management 4.1, disconnected 2.9; Debug, fonts cold) and a warm full repaint 1.4 to 3.7 ms, well under the 50 ms frame limit, so the F3 overlay's "retail ui" line (last and slowest composite, tiles uploaded) will often show a max over its 1.0 slice | Separate from the queue; not subtracted |

`FrameBudgetShare` (priority −1100, so it sets the budget before the queue drains at −1000) sets `MainThread.Budget` to the
configured 4 ms less the tunnel's and the session pump's shares, **down to a floor of 1 ms**, so attach work never starves
while a portal transition runs. Every share and the budget actually used are reported in the F3 overlay (`PerfOverlay`).
W2-pre(a) records the table; the arithmetic is `Client/Play/BudgetShare.cs` and the node is `scripts/PortalSpace/FrameBudgetShare.cs`
(G1 W1, installed by `PortalSpaceSetup`). The tunnel takes 1 ms while it shows; the session track's pump takes its share with
`FrameBudgetShare.Take("session", ms)` (a static no-op when the node is not installed). The F3 overlay's "budget shares" line shows them
while any is taken.

## 2. Lifestone.Client

### 2.1 Coordinates (`World/AcCoordinates.cs`, `World/WorldLocation.cs`)

The retail `LandDefs` functions live in `src/Lifestone.Data/World/LandDefs.cs` since M2 wave 0, so the data layer
and the physics port can use them; `AcCoordinates` delegates to them.

| Type | Meaning |
|---|---|
| `CellPosition(cellId, local)` | Retail `Position`: an object cell id plus an origin in the landblock's local frame. |
| `GlobalPosition(x, y, z)` | Double-precision metres from landblock 0x0000's SW corner (ACE `ToGlobal`: `landblock × 192 + local`). |
| `MapCoordinates` | Retail map coordinates; `ToString()` gives retail's text. |

- **Cell ids** use retail's `LandDefs::gid_to_lcoord` and `LandDefs::lcoord_to_gid`: an lcoord is a 24 m cell index in [0, 2040), and outdoor cell index = `cellX × 8 + cellY + 1`.
- **Moving between landblocks** uses `LandDefs::adjust_to_outside`: the lcoord plus `floor(local / 24)`, then the origin is wrapped with `local − floor(local / 192) × 192` after snapping |x|, |y| < 0.0002 to 0. The wrap constant `flt_8EDE44` is initialised to `24.0 × 8.0` at acclient.c:800518.
- **Map coordinates** follow retail `CPlayerSystem::InqPlayerCoords` (acclient.c:398264): `EW = (lx − 1024) × 0.1 + 0.5` and `NS = (ly − 1024) × 0.1 + 0.5`, from the **outdoor cell**, not the exact position. They are formatted as `LandDefs::CellidToCoordinateString` does, with `"%.1f%s, %.1f%s"`, latitude first, and no letter at exactly 0.
  - Holtburg's drop cell 0xA9B40019 is **42.1N, 33.6E**, as retail shows it.
  - ACE's `GetMapCoords` (`global / 240 − 102`) is continuous and says 42.0N.
  - Indoor cells have no lcoord, so retail shows no coordinates there; neither do we.
- **Headings** are compass degrees, clockwise from north (`AC1Legacy::Vector3::get_heading`).
- **Named locations** (`WorldLocations`): Holtburg `0xA9B40019 [84 7.1 94]` and Arwic `0xC6A90009 [46.805 4.219 42.005]`, the "Town" entries of ACViewer's `Data/Locations.txt` (@c5c54fb).
  - Arwic is landblock 0xC6A9, 5,921 m from Holtburg on heading 110.9°. Its map coordinates are 33.3N, 56.6E.
  - The parser also accepts landblocks (`A9B4`), cells (`A9B40019`) and exact origins (`A9B40019@84,7.1,94`).

### 2.2 Floating origin (`World/FloatingOrigin.cs`)

- **Landblock-granular.** The origin is one landblock's SW corner, and render-local = global − origin × 192, rounded to float once.
- **Rebasing.** The origin rebases onto the focus landblock when the focus is more than `RebaseDistance` landblocks away (Chebyshev, default 2). Render coordinates therefore stay under 3 × 192 = 576 m, where a float resolves 0.06 mm (at 49 km it would resolve 4 mm).
- **No drift.** Truth lives in `GlobalPosition` doubles and landblock coordinates, and render positions are recomputed from them, never accumulated. `OriginShift.Delta` (exact multiples of 192) is there for nodes that must shift instead.
- **Retail does the same** (`LScape::calc_frame`, `LScape::update_loadpoint`).
- **Tested:** 200,000 random steps across the map with more than 1,000 rebases. Render coordinates stay within 576 m, and the round-trip error is at most 2⁻¹⁵ m.

### 2.3 Streaming planner (`Streaming/StreamingPlanner.cs`)

- **Square rings** of landblocks around the focus landblock, the shape of retail's `mid_width × mid_width` grid.
- **Load radius** defaults to retail's `mid_radius` of 8, which gives 17 × 17 = 289 landblocks. The constant is at acclient.c:45608 and is applied by `SmartBox::SetRegion` → `set_mid_radius`. The quality presets use 3/5/8/11/15 (`Render::SetOverallGraphicsQuality`). `--load-radius` changes it.
- **Unload** happens when a landblock is more than `LoadRadius + UnloadHysteresis` rings away (default 1 extra ring), so moving back and forth over a boundary does not thrash.
- **Nearest-first.** Candidates are ordered by distance from the focus point to the landblock square, then to its centre, then by (X, Y), which is deterministic.
- **In-flight limit:** `MaxInFlight` loads at a time (`Main` uses 2 × workers).
- **Cancellation.** Loads that leave the unload radius are returned in `ToCancel`. `MarkLoaded` returns false for a cancelled landblock, so a late result is discarded.
- **Failures.** A failed landblock is not retried until it leaves the range and comes back.
- **Indoor focus.** A focus in a dungeon cell keeps only `IndoorLoadRadius` rings (default 0) because retail does not draw the landscape from such cells. The interiors track's `InteriorFocus` reports it while the camera is in a dungeon cell (docs/interiors.md §5).
- **Map edge.** Rings are clipped to landblocks 0x00–0xFE (`LandDefs::in_bounds`).

### 2.4 Scheduling

- **`FrameBudgetScheduler`** is a priority queue of main-thread work, drained once per frame within a budget.
  - Lower priority values run first, FIFO within a priority. Any thread may enqueue.
  - At least one item runs per drain. A drain that ends past the budget is counted as an overrun.
  - Stepwise items (`Func<bool>`) keep their place.
  - Cancelled items are skipped, and errors go to a handler.
- **`WorkerPool`** caps concurrent CPU work. The game creates it with `--workers` threads (default min(8, cores − 2), `WorkerPool.DefaultConcurrency`: 8 on the development machine; wave-3 performance pass: 8 workers stream as fast as 30, because dat reads serialise on one lock and the demand is 17–35 landblocks/s; more workers only add allocating threads for every GC to suspend; §7). Waiters start FIFO, so nearest-first plans stay nearest-first. Cancellation is cooperative.
- **`ResourceCache<TKey, TValue>`** holds refcounted engine resources with LRU eviction of idle entries by bytes. Entries in use are never evicted.
  - `onIdle` runs when the last reference goes, before any eviction; `onEvict` frees.
- **`RenderResourceCaches<TTexture, TMaterial, TMesh>`** (`Rendering/`) chains the texture, material and mesh caches.
  - A held mesh holds its materials, and a held material holds its texture. Idle entries hold nothing, so every budget holds on its own (render-contract.md §4.1).
  - The engine binds and frees through `IRenderResourceHost`.
  - `RenderResourceCachesTests` runs a 600-landblock simulated flight.

## 3. GameServices (autoload)

`scripts/Core/GameServices.cs` is registered in `project.godot` `[autoload]`. It is created before any scene node and reached through `GameServices.Instance`.

| Member | What |
|---|---|
| `Options` | Command-line options (§6) |
| `Dats` | `DatSet` opened with `--dat-dir`, else `LIFESTONE_DAT_DIR`, else `<repo>\client`: the `client` folder of the checkout, found by walking up to `Lifestone.slnx` from the working directory and the program's folder (`DatSet.ResolveDirectory`; a git worktree inside a checkout uses the enclosing checkout's dats). On failure it is null and `StartupError` says why; `Main` shows the error and exits with 1 in automated runs. |
| `Textures`, `GfxObjMeshes`, `Setups`, `CellStructures`, `CellMeshes`, `Scenery`, `Sky`, `Clock` | Shared wave-1 loaders and caches: `TextureLoader`, `GfxObjMeshCache`, a `ModelAssembly` cache, `CellStructureLibrary`/`CellMeshCache`, `SceneryGenerator`, `DerethSky`, `DerethClock`. All are thread-safe. |
| `Ground` | Exact retail ground height (`TerrainSampler`) for streamed landblocks, on demand elsewhere |
| `Render` | `RenderResources`: textures, materials, shaders, shared meshes (render-contract.md §4). `Main` calls `Render.WarmUp(dats)` before streaming. It compiles the 26 shader variants the dats' 6,152 Surfaces can map to, and runs the mesh path once on a real surface (≈ 230 ms at start-up). No attach step then pays Godot's first-use shader compile (7–11 ms per variant, measured) or first-call JIT (3–14 ms). |
| `Workers` | Bounded worker pool for decoding |
| `MainThread` | `MainThreadQueue`, drained first thing each frame (process priority −1000) within `--budget-ms` (default 4 ms). `InvokeAsync` lets a worker await a main-thread result. |
| `WorldVisibility` | *(M2.)* The arbiter of whether the 3D world is drawn: the **sole writer** of the main viewport's `Disable3D`. Hide requests are reference-counted by owner (`Hide`/`Unhide`, or `Follow(owner, worldVisible)` for an owner that follows a per-frame state), the world is hidden while any is held, and the sky view pauses with it (`SkyView.Paused`). A `SubViewport` keeps rendering while it is hidden (the tunnel's view). Always present. |
| `Audio` | *(M2.)* `IAudioOutput?`: the sound card (16 voices, UI and Effects buses). Null until the portal track's `AudioService` installs itself (`PortalSpaceSetup`); consumers are silent while it is null. |

**Logging** (`scripts/Core/Log.cs`) writes one structured line per event, such as `2026-10-10T06:53:50.206Z INFO  [flypath] report frames=2856 ... max_ms=37.353`.
- Lines go to Godot's output and to `user://logs/lifestone.log`; the previous run's log is kept as `lifestone.1.log`.
- Worker-thread lines are queued and printed from the main thread.
- With `tools\godot.ps1 -IsolateUserData`, user:// is `<repo>\cache\godot\roaming\Godot\app_userdata\Lifestone\`.

## 4. World

- **`WorldRoot`** (`scripts/World/WorldRoot.cs`) owns the `FloatingOrigin` and the `LandblockStreamer`.
  - Each landblock gets a root node `Landblocks/LB_XXYY`, placed at its SW corner in render space.
  - After the focus source moves (process priority 0 vs the world's 10), it rebases if needed: it moves the roots, calls `IStreamingFocusSource.SyncToOrigin()`, and emits `OriginShifted(delta, oldOrigin, newOrigin)`.
  - `ToRender(GlobalPosition)`, `FromRender(Vector3)` and `Teleport(GlobalPosition)` complete the API.
- **`ILandblockLayer`** is how tracks add content:

  ```csharp
  public interface ILandblockLayer
  {
      string Name { get; }
      Task<object?> PrepareAsync(LandblockCoord coord, CancellationToken cancellation); // worker
      IEnumerable? Attach(LandblockCoord coord, object prepared, Node3D landblockRoot);   // main thread, budgeted steps
      void Detach(LandblockCoord coord);                                                  // main thread
  }
  ```

  - Register layers with `WorldRoot.RegisterLayer` before streaming starts.
  - **Prepare:** all layers prepare in parallel on the worker pool. A layer that throws is logged and skipped for that landblock.
  - **Attach is stepwise.** The streamer queues one main-thread work item per landblock, at the landblock's distance priority. It advances one step per budget slot:
    1. create the root;
    2. for each layer, call `Attach`, then run one step per `MoveNext` of the sequence `Attach` returned (null means done in the call);
    3. report the landblock to the planner.

    Write a heavy attach as an iterator that does `yield return null;` after each chunk of a millisecond or less. The objects layer yields after each 0.75 ms slice of texture uploads and batches (docs/objects.md §2.2), the interiors after each 0.75 ms of cell surfaces.
  - **Cancellation stays ordered.** A layer counts as attached once `Attach` is called. A landblock cancelled part-way has its sequence stopped first (the enumerator is disposed, so `finally` blocks run), its root hidden, then every attached layer detached, newest first. Its queued item then retires. Unloads are queued at `Urgent` priority (before any attach). Layers record what each step acquired, so `Detach` after any step releases exactly that.
  - **Disposal:** prepared objects that implement `IDisposable` are disposed once their layer's attach finishes or fails, or when the landblock is dropped. Disposal is atomic, because a cancelled load can drop them from a worker and the main thread at once.
  - **Pinned** by `--engine-checks`, which uses a probe layer of 6-step attaches drained one step at a time and cancelled part-way. Every attach is detached exactly once, no step runs after its `Detach`, every started sequence is stopped and every prepared object is disposed once.
  - **Diagnostics:** steps slower than 4 ms are logged at debug level with their layer; the fly-path report gives the slowest step.
- **`TerrainLayer`** is the **placeholder** terrain, used only with `--placeholder-terrain`: vertex-coloured `TerrainMeshBuilder` meshes lit with the retail terrain formula from the global uniforms (render-contract.md §5). The real terrain is the terrain track's `TerrainLandblockLayer` (TexMerge, [terrain-renderer.md](terrain-renderer.md)).
  - It registers each landblock's `TerrainSampler` with `GameServices.Ground`.
  - It disposes each landblock's mesh on detach, so its vertex buffers go at once rather than at a GC.
  - Its material is created up front, and its first build runs at construction, which takes the one-time JIT out of the first attach.
- **`StaticsSmokeLayer`** (`--smoke-statics`) was the render-contract smoke test that drew LandBlockInfo statics and buildings at LOD 0 before the objects track. It was retired in M1 wave 3 (the flag is reported as a bad argument and ignored); the measurements below that name the smoke layer were taken with it.
- **Objects** (statics, buildings, scenery) are the objects track's `ObjectsLayer` (`World/Objects/`, [objects.md](objects.md)), on by default.

## 5. Camera and HUD

- **`FlyCamera`** (`scripts/Player/FlyCamera.cs`) is the world's streaming focus. Its truth is a double `GlobalPosition` plus heading and pitch.
  - **Move:** WASD; Q/E down/up.
  - **Speed:** Shift fast (×5), Ctrl slow (×0.2); the wheel changes the base speed (×1.25 per notch, 1–5,000 m/s, default 40).
  - **Mouse look:** hold the right button. Tab toggles capture and Esc releases it.
  - **F** toggles walk mode.
  - **Outdoors, fly mode keeps the eye at least 0.5 m above the exact ground** (`FlyCamera.FlyClearance`, rules in `Lifestone.Client.Navigation.FlyGroundClearance`), so the camera never sits under the back-face-culled terrain and sees the sky through it. Retail has no free camera (its player never goes below the terrain), so this is the M1 stand-in, not retail behaviour. Dungeons and cellars lie below the ground, so steps that stay in EnvCells (the interiors' cell tracker, `FlyCamera.ViewerCell`) are never clamped, and after leaving a dungeon or cellar cell through a wall or a floor the eye moves freely around that landblock's cells instead of being thrown up to the surface; from a building interior it stops on the floor (docs/navigation.md §4, with before/after measurements).
- **Walk mode** keeps the eye 1.7 m above the exact ground (`TerrainSampler`) and moves at 4 m/s, ACE's `MotionInterp.RunAnimSpeed` at run rate 1. There is no collision in M1.
  - Retail's first-person camera sits 0.18 m in front of the head pivot (`CameraSet::InHead`, acclient.c:148094). The head's height needs the player Setup, so 1.7 m is a placeholder (`TODO(M2)`).
- **The player's camera (M2 G5, `--offline-player`).** `ChaseCamera` (priority -5, after `PlayerController` at -20) drives the `FlyCamera` node
  (`FlyCamera.Driven`: M1's keys, mouse and clamp do nothing; `InputEnabled` is left alone, so the panels' typing flag and T, F3, F7 and F9 work as in M1).
  It is retail's chase camera (`Client/Camera/ChaseCameraModel`, a re-expression of `CameraSet` and the viewer half of `CameraManager`:
  pivot 1.5 m above the player, viewer offset (0, -2.5, 0.75), stiffness 0.35, orbit, raise, lower and zoom by the number pad or the mouse look,
  first person 0.18 m in front of the pivot) or first person (`--player-camera=first`, the point key, `FirstPersonView` hides the body).
  Each frame the smoothed viewer is swept from the pivot to where it wants to be by `PhysicsWorld.SweepViewer` (a 0.3 m sphere), so it never goes
  through walls or the ground; the cell of the sweep goes to `InteriorController.SetViewer`. It follows the player as drawn (D26, interpolated between
  physics steps), so the character does not shake against the view. The wheel zooms by 10 percent a notch when nothing else takes it
  (retail's route from the wheel to the camera is not read: `TODO(g5-wheel-zoom)`). The overhead and map views, the slope alignment and the mouse-look preferences are named TODOs in
  the model. `--physics-overlay` shows the physics and the camera sweep.
- **`PositionHud`** shows:
  - landblock, outdoor cell and retail map coordinates; inside an EnvCell its own landblock and cell and "indoors" with no map coordinates, as retail's radar shows none there (`Lifestone.Client.Navigation.PositionReadout`);
  - the local x/y/z (in an EnvCell, relative to the cell's own landblock: dungeon cells often lie outside its square);
  - heading and pitch;
  - the mode and speed;
  - the Dereth date and time from the Region calendar and the local clock (server time in M2).
- **`PerfOverlay`** (F3) shows:
  - FPS, the average frame time and the maximum over the last 240 frames;
  - draw calls, objects and primitives (`RenderingServer.GetRenderingInfo`);
  - static, managed and video memory;
  - texture, material, mesh and shader cache sizes;
  - streaming state: loaded, preparing, waiting, attach queue, landblocks/s and origin;
  - the main queue: pending items, budget, last drain, overruns and slowest item;
  - workers.

## 6. Main scene and command line

`scenes/Main.tscn` is the project's main scene: `WorldRoot`, `Player` (FlyCamera with a Camera3D), `WorldEnvironment`, and `Hud` (PositionHud, PerfOverlay). `tools\godot.ps1 -Run` opens it at Holtburg.

The M0 `HoltburgSmoke` scene is retired; `Main` supports its `--screenshot`/`--frames`/`--dat-dir` arguments.

Arguments go after `--`. With `tools\godot.ps1`, use `-GameArgs:'...'` or `-Screenshot`/`-Frames`/`-DatDir`.

**One option registry** (M1 wave 3). Every option of every feature is registered with one line of help in
`src/Lifestone.Client/Options/OptionCatalog.cs`, one group per feature (core, world, checks, sky, interiors, spell viewer,
terrain preview); `--help` prints them all and quits. Each feature still parses its own values (GameOptions,
SkyOptions, InteriorArguments, SpellViewerOptions, TerrainPreviewOptions) with the shared tokenizer and value parsers
(`CommandLineArgument`, `OptionValues`):
- an argument no group registers is reported once as `unknown argument`; a value that does not parse, including a
  mistyped choice (`--vsync=of`), is an error, never a silent fallback (the option keeps its default);
- each group names the scenes that read it (`OptionScenes`: main scene, spell viewer, terrain preview; an option two
  scenes read, such as `--seamless-normals`, says so). The run's scene is the scene file Godot was started with, or the
  spell viewer after `--scene=spellviewer` (`OptionCatalog.SceneOf`); an option of another scene (the spell viewer's
  `--showcase` or the terrain preview's `--golden` given to the main scene) is reported as ignored, like an unknown one;
- errors are warnings in an interactive run; an automated run with an error quits at once with exit code 1;
- "automated" (the run ends by itself) is derived from the registry: an option of the run's scene registered as ending
  the run (`--screenshot`, `--fly-path`, `--golden-viewpoints`, `--interior-tour`, `--showcase`, `--golden`, the check
  suites); another scene's ending option does not count, since nothing in this scene would end the run. The sky uses
  `GameOptions.IsAutomated` for its fixed clock and hidden panels (the spell viewer still has its own equivalent
  `SpellViewerOptions.IsAutomated`);
- `OptionCatalogTests` scans the Game parsers' `case "--..."` labels: a parsed option missing from the catalog, or a
  registered one no parser reads, fails `dotnet test`. Adding an option means adding both.

**Check mode.** `--engine-checks`, `--interior-engine-checks`, `--sky-checks`, `--blend-checks` and
`--portal-space-checks` and `--retail-ui-checks` are check suites, one per run. A suite owns the render globals, so `Main` installs nothing live
that writes them: the engine, interior and portal-space suites run without the world (and without dats); the sky suite
gets the sky controller over an initialised world that streams no layers; the blend suite gets only the byte-space
environment (render-contract.md §2.3). The portal-space suite (`scripts/PortalSpace/PortalSpaceChecks.cs`, M2) pins the
camera roll, the widest lens, the world arbiter, creature-mode lighting under hostile world globals, the frustum lens
(f = √3, 0.4, 0.01 and 0.001, centred and off-centre), that the zoom renders, and the sky view's follow of the lens
(portal-space.md §11.5). The retail-ui suite (`scripts/RetailUi/RetailUiChecks.cs`, M2) pins the tile layer byte for byte at
scales 1 to 3, nearest filtering, the viewport hole, the cursor, the key table, the fonts, the real screens and input
(retail-ui.md §17); its dat-based checks skip without the dats, and `Main` runs it before the dats check like the others.

**Output paths.** Relative output paths (`--screenshot`, `--golden-viewpoints`, `--interior-tour`, ...) are taken from
the repository root, never from `src/Lifestone.Game` (`Diagnostics/ScreenshotWriter.cs` with
`Lifestone.Client.Diagnostics.OutputPaths`, which also refuses any path inside `src/` or `tests/`). Godot makes its
`--path` folder (`src/Lifestone.Game`) the working directory at start-up, so in a source checkout GameServices makes
the repository root the working directory for every runner that resolves a relative path itself.

The core and main-scene options (the other features' options are in their docs: sky.md §2, navigation.md, interiors.md
§9, objects.md §2.5, spell-viewer.md, terrain-renderer.md; `--help` prints every one):

| Argument | Effect |
|---|---|
| `--dat-dir=DIR` | Retail dat folder (else `LIFESTONE_DAT_DIR`, else `<repo>\client`, §3) |
| `--scene=spellviewer` | Open the spell viewer instead of the world (spell-viewer.md) |
| `--start=LOC` | Start location (default `holtburg`). The camera starts 45 m up and 90 m back, looking at it. |
| `--walk` | Start in walk mode on the spot, facing its heading |
| `--screenshot=PATH`, `--frames=N` | Once the place is complete (`CaptureReadiness`: streaming, objects ring, the interiors' cells and the main-thread queue; at most 180 s), render N frames (default 30) and at least 2 s (`CaptureReadiness.SettleSeconds`, the golden viewpoints' settle: the living statics' flames and smoke need it), save a PNG and quit |
| `--fly-path=FROM:TO:SPEED` | Automated flight (§7), then quit (with `--screenshot`, captures on arrival) |
| `--load-radius=N` | Streaming rings (default 8) |
| `--budget-ms=X` | Main-thread budget (default 4) |
| `--workers=N` | Worker threads (default min(8, cores − 2); the perf audit measured the same throughput with 8 as with 30) |
| `--vsync=on\|off\|mailbox\|adaptive` | Vsync mode (default on) |
| `--max-fps=N` | Frame cap, 0 = none. Default: the screen's refresh rate with vsync off or mailbox, none with vsync on or adaptive (`FramePacing`, §7.2). A malformed or negative value keeps the default (logged as a bad argument). Uncapped measurements need `--max-fps=0`; the vsync-off measurement commands in the other docs carry it |
| `--max-mip=N` | Highest sampled mip level (default 3, retail) |
| `--dxt=passthrough\|expand` | DXT upload policy: blocks with re-encoded mips (default) or RGBA8 (render-contract.md §3) |
| `--time-of-day=F` | Day fraction for the default lighting (default 0.5) |
| `--smoke-statics` | Retired (M1 wave 3): reported as a bad argument and ignored (an automated run with it exits 1) |
| `--objects=all\|off\|statics,buildings,scenery` | Landscape objects to create (default all; objects.md §2.5) |
| `--object-radius=N` | Landblock rings around the viewer's that have objects (default 1, the retail 3×3) |
| `--static-effects=on\|off` | Living statics: statics' default scripts and animations (default on; objects.md §1.7) |
| `--cutouts=opaque\|blend` | Alpha-tested blending surfaces: opaque cut-out shortcut (default) or retail's blend after the test (render-contract.md §2) |
| `--texture-budget-mb=N`, `--mesh-budget-mb=N` | Texture cache budget (default 768) and shared-mesh cache budget (default 256), render-contract.md §4.1 |
| `--engine-checks` | Verify the engine behaviours the render path and the streamer rely on, then quit (0 = all hold); run windowed |
| `--interior-engine-checks`, `--sky-checks` | The interiors' (interiors.md §9) and the sky's (sky.md §6) check suites; one suite per run |
| `--portal-space-checks` | *(M2.)* The portal view's engine checks (above, portal-space.md §11.5), then quit; run off-screen |
| `--retail-ui-checks` | *(M2.)* The retail UI layer's engine checks (above, retail-ui.md §17), then quit; run off-screen |
| `--retail-ui=off\|standin`, `--ui-scale=N`, `--retail-ui-start=SCREEN`, `--retail-ui-frames[=DIR]`, `--retail-ui-only`, `--retail-ui-glitches`, `--retail-effects-stale-info`, `--retail-disconnect` | *(M2; `--retail-effects-stale-info` M3 2.2.)* The retail UI over the world, default off, which leaves the explore scene unchanged (retail-ui.md §17) |
| `--fov=DEG` | *(M2.)* The world camera's vertical field of view, 1 to 179 (default 60 in explore mode: `FlyCamera`; the spell viewer's own default is 55; the terrain preview's `--fov` is the same option). **In play mode the default is retail's** (M4 polish pass P2, `Camera/RetailLens.cs`, `Main.ApplyRetailFov`): the 90-degree `Render.FieldOfView` preference turned into a vertical angle of 90 over (aspect - 0.1), 72.97 degrees at 800x600 (`SmartBox::RenderNormalMode` 11.6096 0x00453B40, `SmartBox::SetDefaultFov` 0x00451EA0, `SmartBox::GetOverrideFovDistance` 0x00451C20; the aspect is the viewport's width over its height, `RenderDevice::ComputeAspectForViewport` 0x0054FD60); `--fov` given wins (`--fov=60` is the old play view); the angle is computed once at start (`TODO(cam-fov-resize)`). The portal zoom is a frustum and ignores it: its rest state is this field of view |
| `--blend-space=byte\|linear` | *(Wave 3.)* Blend in retail's byte space (default) or Godot's linear light (render-contract.md §2.3) |
| `--detail=on\|off` | *(Wave 3.)* Retail's building and environment detail textures (default on; render-contract.md §2.4). Also accepts 1/0 and true/false; any other value is reported as a bad argument |
| `--blend-checks` | *(Wave 3.)* Verify byte-space blending, the decode, the detail pass and built-in-material colours on the engine in both blend spaces, then quit (0 = all hold); run windowed |
| `--retail-normals` | *(Wave 3.)* Retail's per-block terrain normals; seamless normals are the default (terrain-renderer.md §8; `--seamless-normals` is still accepted) |
| `--placeholder-terrain` | The vertex-coloured placeholder terrain instead of the TexMerge terrain (terrain-renderer.md §5) |
| `--help` | Print every option of every scene (the registry above) and quit |
| `--quit-after=S` | Safety net for automated runs (exit 1) |
| `--log-level=debug\|info\|warning\|error` | Log level |

Exit codes: 0 success; 1 failure (no dats, screenshot not saved, `--quit-after` reached, an engine check failed, a bad argument in an automated run); 2 a fly path with a frame over 50 ms; 124 when `tools\godot.ps1 -TimeoutSeconds` kills the run.

**Integration suite:** `pwsh -File tools\smoke.ps1` runs, sequentially and off-screen, the build (0 warnings), `dotnet test`,
`--help`, a bad-argument run, `tools\terrain-golden.ps1`, the four check suites, a short spell-viewer showcase
(exceptions=0), the golden viewpoints and the Holtburg→Arwic fly path (DoD 8), then checks that no image landed under
`src/` or `tests/`. It prints a summary table, writes logs to `cache\smoke\` and exits non-zero on any failure; a
Godot step that logs a Lifestone ERROR line fails too (except the bad-argument run). The fly path is judged on the raw
maximum, as the DoD words it: a frame over 50 ms fails. On a shared machine another Godot instance starting stalls
presentation for 50-210 ms in frames without any Lifestone work, so the step waits for a quiet machine and reports the
slowest frame with and without Lifestone work; it retries once only when every frame over 50 ms had no Lifestone work
in it (a frame over 50 ms with a GC, a drain of 1 ms or more, an attach or a rebase fails at once), and a pass on the
retry shows as `PASS (retry)`. `-JudgeLifestoneFramesOnly` judges on the attributed maximum instead (`PASS (attributed)`).

**Engine checks** (`scripts/Diagnostics/EngineChecks.cs`): `tools\godot.ps1 -Run -IsolateUserData -TimeoutSeconds 120 -GameArgs:'--engine-checks'`. All 9 hold on Vulkan and D3D12 (2026-10-10):

- **Zero normals:**
  - a zero normal reads back as (0, 0, −1);
  - the `CUSTOM0` zero-normal flags read back unchanged.
- **Rendered quads,** under ambient 0.2, sun 0.5 and a north sun:
  - the clear colour reaches the screen unchanged;
  - a flagged zero-normal quad shows ambient only (51);
  - a quad facing the sun shows ambient + sun (178);
  - a zero-normal quad without the flag shows 178, which is the bug the flag fixes.
- **DXT1:**
  - a punch-through texture renders opaque black;
  - the RenderingDevice format is `BC1_RGB_UNORM_BLOCK` (BC1 without alpha).
- **Stepwise attach:** a probe layer is attached step by step and cancelled part-way (§4).

## 7. Fly-path measurement

`--fly-path=holtburg:arwic:150`:
- **Settle:** the camera starts at Holtburg 60 m above the ground and waits for streaming to go idle, then 60 more frames.
- **Fly:** it flies straight to Arwic at 150 m/s (5,921 m, 39.5 s), 60 m above the exact ground, looking along the path. Every frame time is recorded.
- **Arrive:** it waits for idle and optionally takes a screenshot.
- **Report:** written to the log and to `user://logs/flypath-report.json` and `flypath-frames.csv`. Per frame: time, drain time, attached count, GC counts, rebases, the GC pause inside the frame and the bytes allocated in it (§7.1).

*The table below is the wave-2a measurement on placeholder terrain; §7.3 has the integrated game (all layers) before and after the wave-3 performance pass.*

Measured on the development machine (RTX 5090, Vulkan Forward+, 1280×720, 30 workers, load radius 8, default 4 ms budget, placeholder terrain), 2026-10-10, on the code after the review fixes:

> **Reproducing these rows** (M1 wave 3 defaults): the table predates the frame cap and the 8-worker default. Today
> `--vsync=off` and `--vsync=mailbox` are capped at the screen's refresh rate unless `--max-fps=0` is also given, so the
> uncapped vsync-off and mailbox rows need `--max-fps=0`, and the table's 30 workers need `--workers=30`. The "smoke
> layer" rows drew LandBlockInfo statics and buildings through the render-contract smoke layer (`--smoke-statics`,
> retired in M1 wave 3); `--objects=statics,buildings` is the nearest equivalent today.

| Run | Frames | Avg | p50 | p99 | Max | > 33 ms | > 50 ms | Attached | Throughput | Overruns | Max drain | Slowest attach step | Gen-2 GCs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| vsync on (default) | 2,878 | 13.71 ms | 16.16 ms | 28.7 ms | 35.1 ms | 6 | **0** | 680 | 17.2 /s | 0 | 1.8 ms | 0.7 ms | 0 |
| vsync off | 86,887 | 0.45 ms | 0.35 ms | 3.5 ms | 6.3 ms | 0 | **0** | 680 | 17.2 /s | 0 | 1.9 ms | 0.2 ms | 2 |
| vsync on, smoke layer | 2,797 | 14.11 ms | 16.19 ms | 31.5 ms | 35.5 ms | 8 | **0** | 679 | 17.2 /s | 0 | 3.3 ms | 1.3 ms | 65 |
| vsync off, smoke layer | 73,974 | 0.53 ms | 0.41 ms | 3.7 ms | 7.6 ms | 0 | **0** | 680 | 17.2 /s | 0 | 3.5 ms | 1.3 ms | 50 |
| `--vsync=mailbox`, smoke layer | 69,706 | 0.57 ms | 0.44 ms | 3.7 ms | 7.5 ms | 0 | **0** | 680 | 17.2 /s | 1 | 4.0 ms | 3.4 ms | 41 |
| to 0x5A5A (22.9 km) at 600 m/s, vsync off, smoke layer | 78,054 | 0.49 ms | 0.36 ms | 3.8 ms | 8.5 ms | 0 | **0** | 2,873 | 75.2 /s | 1 | 4.3 ms | 3.7 ms | 33 |
| same, `--texture-budget-mb=8 --mesh-budget-mb=1` | 74,992 | 0.51 ms | 0.36 ms | 3.9 ms | 7.4 ms | 0 | **0** | 2,872 | 75.2 /s | 0 | 3.6 ms | 1.7 ms | 48 |

- **The DoD limit holds.** No frame exceeds 50 ms in any run.
- **Work stays small.** With vsync off or in mailbox mode the slowest frame is 6.3 ms on placeholder terrain and 7.4–8.5 ms with textured statics streaming.
- **Main-thread steps fit the budget.** No attach step during a flight exceeded 3.7 ms.
  - **Before the fixes,** a landblock's statics attached in one step. At start-up the slowest step took 20–27 ms: a town's worth of uploads, plus Godot compiling each new shader variant on first use (7–11 ms each) and first-call JIT.
  - **Now** each part is one step, and `Render.WarmUp` (§3) compiles the shaders and runs the upload path before streaming starts.
  - **Result:** the slowest main-thread item of a whole run, start-up included, is 5.2–6.5 ms.
- **Frames over 33 ms with vsync on are presentation pacing, not work.**
  - Not one of them coincides with an attach, a drain over 0.5 ms, a GC or a rebase in the per-frame CSV.
  - The vsync-on distribution varies between runs and sessions with the desktop's presentation mode (p50 12.4 ms before, 16.2 ms now, with identical work). Mailbox or vsync off removes these frames.
  - This is for the wave-3 performance pass, which might choose mailbox or a frame cap.
- **One run hit an 83.7 ms frame and did not reproduce.** In a first smoke-layer vsync-off flight, frame 8,697 took 83.7 ms. It had no drain, attach, GC or rebase. From that frame on, every frame took exactly 6.9 ms (144 Hz) instead of about 0.55 ms: the OS or driver switched the window's presentation mode, for example because the window was covered. An immediate re-run gave the row above (max 7.6 ms).
- **Throughput.** 17.2 landblocks/s matches the demand: crossing a landblock every 1.28 s at 150 m/s brings in a new 17-landblock row or column. At 600 m/s the streamer attaches 75 landblocks/s with no frame over 8.5 ms.
- **Memory is bounded** (render-contract.md §4.1).

  | Run | Textures at arrival | Idle | Evicted | Meshes (evicted) | Godot texture memory |
  |---|---:|---:|---:|---:|---:|
  | Arwic, smoke layer | 276 (19.9 MB) | 144 | 0 | 270 (0) | 86 MB |
  | 0x5A5A, default budgets | 367 (27.5 MB) | 367 | 0 | 379 (0) | 94 MB |
  | 0x5A5A, 8 MB / 1 MB budgets | 142 (7.9 MB) | 142 | 336 | 204 (190) | 74 MB |

  - **Arwic:** 144 of 276 textures are idle. Before the fixes every texture stayed pinned (0 idle).
  - **0x5A5A** (open ocean, no statics in range): every texture is idle and held only by the LRU, within the 768 MB budget.
  - **Small budgets:** with an 8 MB texture budget and a 1 MB mesh budget, 336 textures and 190 meshes are evicted on the way.
  - **Freeing is immediate.** Godot's texture memory drops by the 20 MB that was evicted (74 MB against 94 MB), so the GPU memory goes at the eviction, not at a GC. 66 MB of that figure is Godot's own baseline (render targets), as the placeholder-only runs show.
- **Upload costs** with the smoke layer (real textures and meshes, DXT kept as blocks with re-encoded mips):
  - A landblock waits up to about 1.2–1.8 s on the workers for its decodes and mip encodes. It has to keep 9 rings of headroom.
  - A landblock's main-thread attach totals at most 4.3 ms, spread over frames.
  - The decode buffers cause about 30–65 gen-2 collections per flight. None of these produced a hitch.
  - Pooling them needs a buffer-taking decode API in the data layer (`TextureLoader`); that is for the textures and objects tracks.
- **Holtburg static view:** 289 landblocks streamed in 0.22 s from start; with the smoke layer, about 0.9 s. Add about 0.23 s of warm-up before streaming.

Screenshots (gitignored, `docs/screenshots/m1/`):
- `core-holtburg.png`: the overview with the placeholder terrain.
- `core-holtburg-smoke-statics.png`: the same view with statics and buildings through the render path (the retired smoke layer).
- `core-holtburg-walk.png`: walk mode at the drop point.
- `core-flypath-arwic.png`: arrival at Arwic.
- `core-flypath-5a5a-small-budget.png`: arrival at 0x5A5A with 8 MB / 1 MB budgets. The overlay shows the evictions.

### 7.1 What the report measures (wave-3 performance pass)

- **The flight only.** `slowest_item_in_flight_ms` (JSON `SlowestMainThreadItemMs`) restarts when the flight starts (`FrameBudgetScheduler.ResetPeaks`); before, every report showed the start-up's terrain atlas upload (12–13 ms).
- **GC per frame.** Each frame's GC pause (`GC.GetTotalPauseDuration` delta) and allocation (`GC.GetTotalAllocatedBytes`) go into the CSV (`gc_pause_ms`, `alloc_kb`). `[flypath] report gc` gives the frames with a GC, the total and the largest per-frame pause, the slowest frame with and without a GC, the managed bytes allocated during the flight, and the drains that ended more than 1 ms past the budget (`overruns_over_1ms`; the raw overrun count includes drains that end 0.0–0.7 ms late because the item that crosses the budget always finishes). `texture_decodes`/`texture_decodes_shared` count the shared texture decodes (docs/textures.md §9).
- **Start-up.** The settle phase is reported as `[flypath] startup` and `flypath-startup.csv`: seconds from process start to the first frame and to streaming idle with the main-thread queue empty, its frames, the largest frame and drain, the slowest item, managed allocation, collections and pauses.
- **Memory.** After arrival (the flight's figures are already taken) `[flypath] memory` reports the GC heap (committed, size, fragmentation), private bytes and working set, then forces one full compacting collection and reports the live managed heap, Godot's static memory and video memory.
- **Naming slow items.** With `--log-level=debug`, every main-thread item over 6 ms is logged with its delegate and the GC pause inside it (`[main] slow main-thread item`); `[interiors] tour load` gives each interior tour load's main-thread cost. The scheduler also keeps the slowest item's own work without the GC pause that fell in it (`FrameBudgetScheduler.MaxItemWorkMilliseconds`; `slowest_item_work_ms` in the tour lines, `slowest_item_work_in_flight_ms` in the fly-path report), and the interiors' `slow attach step` lines name the slowest single material acquire (with the texture it uploaded) and the slowest single `AddSurfaceFromArrays` of the step (review pass).
- **Build.** `tools\godot.ps1` builds Debug (unoptimised) assemblies, which the editor binary runs. Every number in this section is from Debug builds; optimised code is about a third faster per frame (wave-3 audit), with the same GC pauses.

### 7.2 Defaults chosen by the wave-3 performance pass

- **Vsync on** stays the default. **`--vsync=off` and `--vsync=mailbox` are capped at the screen's refresh rate** (`FramePacing`; `Engine.MaxFps`, 60 when the rate is unknown). Uncapped, they render 350–2,500 frames per second that nobody sees, and every one of them runs the per-frame work and its allocations again (the interiors' portal walk allocates about 18 KB per frame while flying, §7.3). `--max-fps=N` sets any cap, `--max-fps=0` removes it for measurements; Godot's own engine argument `--max-fps N` is respected when `--max-fps` is not given.
- **Workers: cores − 2, at most 8** (§2.4).
- **Main-thread budget 4 ms and load radius 8** are kept: streaming keeps up at 150 and 300 m/s with them (wave-3 audit).
- **Server garbage collection.** `tools\godot.ps1` starts Godot with `DOTNET_gcServer=1` (that process only; `-WorkstationGC` restores the default), and `Lifestone.Game.csproj` sets `ServerGarbageCollection` (with background GC) for exported builds. The Godot editor binary hosts .NET through its own `GodotPlugins.runtimeconfig.json` and ignores the project's runtimeconfig, so the environment variable is the only way to reach editor and `-Run` launches; Godot started any other way (and a game it launches) runs with workstation GC. `[core] Lifestone starting ... gc=` logs which one runs. TODO(perf): verify the exported runtimeconfig once export templates exist. Server GC in .NET 10 adapts its heap count to the application (DATAS) and collects ephemeral generations on several threads, which roughly halves the per-frame GC pause (table below).
- **Not adopted** (measured on the same flights): `GCSettings.LatencyMode = SustainedLowLatency` (it only avoids blocking gen-2 collections; the pauses are ephemeral), alone or with server GC; `DOTNET_GCNoAffinitize` with server GC (no clear gain, more variance); a fixed `GCHeapCount` (the audit: 612–842 MB committed); a larger `GCgen0size` (the audit: longer pauses). **Adopted (M4 P4 g3fix):** `DOTNET_GCgen0MaxBudget=0x2000000` (32 MB) for the Godot process under server GC (`tools\godot.ps1`): the arrival at Holtburg after a teleport paid one 50-58 ms gen-0 pause (`gc_pause_ms` on the `slow frame` line, `gc_gen0=1`, 4.6 ms of process time), which failed the offline 50 ms check in three of four runs and the throwaway's teleport frame about half the time; with the cap the play-checks passed in the next two runs (max 45.8 and 37.2 ms). `TODO(m4-throwaway-teleport-frame)`: confirm on the throwaway at the next live gate.

### 7.3 Integrated measurements, before and after (wave-3 performance pass)

RTX 5090, Ryzen 9 9950X3D (32 threads), Vulkan Forward+, 1280×720, Debug assemblies, all layers (TexMerge terrain, objects, interiors, sky), load radius 8, 4 ms budget, 2026-10-10. **Before** is master @110ad6e plus only the §7.1 diagnostics (30 workers, workstation GC); **after** is the wave-3 branch (8 workers, server GC through `tools\godot.ps1`). The two builds ran **interleaved**, two rounds, so both saw the same background load: other agents' Godot processes ran during almost every run (1–3 at start or end), which is why frames with no Lifestone work in them reach 40–260 ms in some runs of both builds; the GC, allocation and memory figures do not depend on that. Vsync-off rows are uncapped (`--max-fps=0`) to compare like with like. Ranges are the two runs.

**GC and allocation during the flight** (`[flypath] report gc`; 150 m/s):

| Flight | Build | Collections 0/1/2 | GC pause, total | Largest GC pause in one frame | Slowest frame with a GC | Allocated | Committed at arrival |
|---|---|---|---:|---:|---:|---:|---:|
| Holtburg → Arwic, vsync off | before | 21–27 / 9–10 / 2 | 94–106 ms | 12.4–21.2 ms | 19.8–22.4 ms | 1.06–1.32 GB | 362–436 MB |
| | after | 24–27 / 7 / 3 | 57–75 ms | 7.2–14.7 ms | 14.1–21.6 ms | 1.03–1.11 GB | 147–150 MB |
| Holtburg → Arwic, vsync on | before | 12–13 / 7–8 / 2 | 101–109 ms | 17.5–19.5 ms | 23.2–25.6 ms | 611–648 MB | 481–580 MB |
| | after | 9–12 / 3–4 / 2 | 55–64 ms | 12.1–17.0 ms | 21.2–33.7 ms | 482–489 MB | 151–202 MB |
| Shoushi → Mayoi, vsync off | before | 15–17 / 9 / 2 | 99–100 ms | 12.3–14.7 ms | 21.6–23.7 ms | 715–816 MB | 364–424 MB |
| | after | 13–15 / 5 / 2 | 66–86 ms | 9.0–17.7 ms | 18.0–22.4 ms | 635–712 MB | 164–171 MB |
| Glenden Wood forest, vsync off | before | 16–18 / 5–7 / 2–3 | 67–92 ms | 10.9–20.6 ms | 19.1–23.2 ms | 832–854 MB | 355–610 MB |
| | after | 12–30 / 4–6 / 2 | 47–53 ms | 7.6–14.7 ms | 13.8–16.9 ms | 642–748 MB | 137–169 MB |
| Holtburg → Arwic, vsync off, default cap (240 Hz) | after | 8 / 3 / 2 | 44 ms | 14.3 ms | 16.7 ms | 489 MB | 173 MB |
| Holtburg → Yaraq (17.5 km), vsync off, quiet machine, one run each (not interleaved) | before | 69 / 14 / 4 | 232 ms | 13.6 ms | 20.8 ms | 3.34 GB | 358 MB |
| | after | 228 / 20 / 7 | 205 ms | 11.3 ms | 15.7 ms | 2.27 GB | 109 MB |

- **The GC pause per flight is 35–45 % shorter** and the largest single pause usually drops from 12–21 ms to 7–15 ms. The flights still allocate about as much as before: texture decoding was most of the start-up's garbage, not the flight's.
- **What the flights allocate.** An allocation-sampling probe (temporary, `AllocationTick` events by type, Holtburg → Arwic, vsync off) found the main thread allocating 860–890 MB of the flight's 1.1 GB: `ViewVolume.Corner[]`, `CellPlane[]`, `List<Corner>`, `HashSet<int>`, enumerators and LINQ iterators of the interiors' portal walk (`PortalVisibility`, `InteriorVisibility`), which reruns every frame while the eye moves (about 18 KB per frame). The workers' prepares allocated about 190 MB (objects 83 MB, interiors 80 MB, terrain 29 MB). This is a hand-off for the interiors track (pooled clip buffers and no LINQ in the per-frame walk); here the default frame cap bounds it: capped at 240 Hz the same flight allocates 489 MB and spends 44 ms in GC.
- **What makes a GC pause long** is what survives it (the probe: 10–17 ms for blocking gen-1 collections that promoted 8–15 MB, a few ms for 1–4 MB), not the garbage: prepared landblocks waiting to attach and the caches. Server GC promotes on several threads, so the same promotion pauses for less.
- **Frame times** (avg, p99, p99.9) did not change measurably under this background load: Holtburg → Arwic vsync off averaged 0.82–1.05 ms before and 0.79–0.98 ms after, p99.9 7.0–10.3 against 7.9–8.8 ms. Throughput is unchanged (17.2, 16.7 and 18.2 landblocks/s).
- **Slowest main-thread item of the flight** (now the flight's own, no longer the start-up's 12–13 ms atlas upload): 1–15 ms with vsync off after, against 1–13 ms before; 3–5 ms against 2–20 ms with vsync on. GC pauses that fall inside an item count in it; `slowest_item_work_in_flight_ms` (review pass) leaves them out: in a review-pass Holtburg → Arwic flight (vsync off, uncapped) the slowest item took 5.8 ms and no item's own work exceeded 1.2 ms.

**Start-up** (process start to idle at Holtburg, Shoushi or Glenden Wood; the 10 all-layer start-ups of each build):

| | Before | After |
|---|---:|---:|
| Managed allocation | 1.07–1.55 GB | 0.23–0.29 GB |
| GC pause, total / largest in one frame | 49–80 ms / 9.7–16.2 ms | 27–37 ms / 6.5–9.8 ms |
| `Main._Ready` (Lifestone starting → ready) | 1.00–1.26 s | 0.86–1.19 s |
| Sky models' synchronous wait in it | 131–157 ms | 6–9 ms |
| First frame / streaming idle after process start | 2.14–2.68 s / 3.10–3.55 s | 2.05–2.79 s / 2.54–3.68 s |
| Largest start-up frame (the first) | 61–73 ms | 62–88 ms |

- The first frame costs 51–54 ms even in a terrain-only start-up (`--objects=off --interiors=off --sky=off`), so it is the engine's first frame, not the sky's build. It happens before the start view is shown.
- **The sky's debug line at start-up is not a frame time.** `[sky] slow frame ms≈150 ... created=5 shader_swaps=8` (the wave-3 audit's evidence for the sky hitch, unchanged on this build) logs Godot's process delta, which at start-up carries the whole of `Main._Ready`, capped at about 150 ms with the rest spread over the next deltas (the 87–122 ms line after it). The line now also logs `wall_ms`, the sky controller's own stopwatch since its previous update: 59–90 ms and 51–64 ms for those two lines (docs/sky.md §4.3).
- On a quieter machine (the before/after batches that did not run interleaved) the start view was idle 2.6–2.8 s after process start before and 2.3 s after.

**Memory** (forced full compacting collection; `[flypath] memory`):

| | Before | After |
|---|---:|---:|
| Holtburg idle, all layers: live managed heap | 103–104 MB | 45–46 MB |
| Holtburg idle: GC committed / process private bytes | 420–487 MB / 2,109–2,176 MB | 87–94 MB / 1,745–1,770 MB |
| Holtburg idle, terrain only: live managed heap | 70 MB | 12 MB |
| Glenden Wood after the forest flight: live | 113–114 MB | 50–53 MB |
| After the Arwic or Shoushi flight: live | 34–38 MB | 41–45 MB |

- **Terrain start-up retention is gone** (terrain-renderer.md §1): the 58 MB by which the live heap at Holtburg drops is the terrain-only difference (70 → 12 MB), the atlas read buffers that 30 threads left in `ArrayPool<byte>.Shared`.
- **After a flight** the live heap is a few MB larger: the pooled decode buffers that `ArrayPool<byte>.Shared` keeps until it trims them (docs/textures.md §9). The GC's committed memory, which is what the process actually holds, is 60–80 % lower throughout.

**GC modes** on the after build (Holtburg → Arwic and Shoushi → Mayoi, vsync off, uncapped, one run each, run one after another under the same kind of background load):

| Mode | Largest GC pause in one frame | GC pause, total | Committed at arrival |
|---|---:|---:|---:|
| Workstation, concurrent (the .NET default) | 17.4–18.7 ms | 110–141 ms | 191–194 MB |
| Workstation + `SustainedLowLatency` | 19.8–20.0 ms | 120–137 ms | 141–185 MB |
| **Server (DATAS)**, the new default | 4.7–14.8 ms | 35–72 ms | 175–187 MB |
| Server + `SustainedLowLatency` | 14.6 ms | 82 ms | 188 MB |
| Server + `DOTNET_GCNoAffinitize=1` | 10.4–10.5 ms | 42–91 ms | 158–209 MB |

Server GC runs more, shorter collections (at start-up 13–21 gen-0 collections against 5–7 with workstation GC on the same build), which is why its collection counts above are higher.

**Dungeon loads** (`--interior-tour=DIR --vsync=off --max-fps=0 --log-level=debug`; five tours per build, interleaved, 2026-10-10, review pass; no other Godot process ran during the after tours). Per load: the slowest main-thread item, for the after build also that item's own work without a GC pause inside it (`slowest_item_work_ms`, §7.1), and the GC pause during the whole load. Ranges are over the five tours.

| Location | Before: slowest item | Before: GC pause in the load | After: slowest item | After: its own work | After: GC pause in the load |
|---|---:|---:|---:|---:|---:|
| Town Network | 1.4–16.3 ms | 6.7–9.6 ms | 1.4–6.6 ms | 1.4–6.6 ms | 3.1–4.2 ms |
| Holtburg Redoubt | 0.8–1.3 ms | 10.8–12.2 ms | 0.8–1.3 ms | 0.8–1.3 ms | 0 |
| Marketplace | 0.8–1.6 ms | 0–1.5 ms | 0.8–1.6 ms | 0.8–1.6 ms | 6.3–7.6 ms |
| Mines of Despair (537 cells) | 2.5–10.0 ms | 0–9.2 ms | 2.4–6.2 ms | 2.3–2.7 ms | 0–5.0 ms |
| Glenden Wood Dungeon | 4.6–5.5 ms | 0 | 4.5–5.3 ms | 4.5–5.3 ms | 0–2.2 ms |
| Training Academy | 9.8–10.2 ms | 10.2–19.2 ms | 9.6–10.3 ms | 9.6–10.3 ms | 2.9–16.2 ms |
| Holtburg shop | 11.2–17.5 ms | 50.0–57.9 ms | 1.7–9.5 ms | 1.7–2.3 ms | 14.8–26.1 ms |
| Holtburg smithy | 0.8–13.4 ms | 10.0–15.1 ms | 0.9–3.7 ms | 0.9–1.3 ms | 3.5–18.3 ms |

**0x00D1** (the densest dungeon, 4,213 cells, 12.8 MB of cell meshes; not a tour stop): `--start=00D1 --vsync=off --max-fps=0 --log-level=debug` with `--screenshot`, five runs per build, interleaved with the tours. The whole landblock attaches in 0.26–0.32 s over about 300 steps. Per load:

| | Before | After |
|---|---:|---:|
| Slowest attach step without a GC in it | 9.6–19.2 ms | 9.4–9.7 ms |
| Steps over 6 ms without a GC | 3–4 | 4 |
| Steps with a GC pause inside | none | 2 of 5 loads: one step of 16.1–18.3 ms, 15.7–17.7 ms of it a gen-0 collection |

**What the remaining slow items are.** The interiors' `slow attach step` lines now name the slowest single material acquire (with the texture it uploaded) and the slowest single `AddSurfaceFromArrays` in the step (interiors.md §9). Every main-thread item over 6 ms in these after runs is a single-call stall, a GC pause or the start-up atlas upload; between the 4 ms budget and 6 ms there are also shorter stalls (Glenden Wood), large single surfaces and, twice in five tours, a 4.6 ms objects attach step at Holtburg:
- **One engine call that stalls whatever its size.** In 0x00D1, in every run and at the same point of the load: `AddSurfaceFromArrays` of a 4–34-vertex surface takes 9.1–9.4 ms, that of a 12–13-vertex surface 6.7–7.2 ms, and the 5,548-vertex surface 8.7–9.2 ms while the larger 7,276- and 8,760-vertex ones take 4.1–5.5 ms. `ImageTexture.CreateFromImage` of a 64×128 (42 KB) or 256×512 DXT1 (85 KB) texture takes 6.6–8.2 ms (4 of 5 loads; in the fifth a 3-vertex surface took 6.4 ms instead), while a 256×256 (341 KB) upload in the same load takes 0.03–0.04 ms. The Training Academy's item is an 8–10-vertex surface (9.4–9.6 ms, every tour), Glenden Wood's a 5–208-vertex surface (4.3–4.6 ms), and one Town Network tour had a 5.6 ms upload of a 256×256 texture. The stall is inside Godot's RenderingDevice or the driver (the duration does not follow the size, and it repeats at the same place, so it looks like a memory pool growing); one engine call cannot be split from Lifestone's side, and the attach already yields after every surface once its 0.75 ms slice is spent. The before build shows the same steps at the same places (and one material acquire of 18.7 ms, unattributed there). TODO(perf): needs a GPU-side trace (interiors.md §10).
- **Large single surfaces** (over the budget, under 6 ms): 4.1–5.5 ms for the 7,276- and 8,760-vertex surfaces of 0x00D1. Godot packs the arrays on the main thread; building the surface data on the worker (`RenderingServer.MeshAddSurface` with pre-packed arrays) is the remaining step, a hand-off for the interiors track.
- **GC pauses** that land in an item: Mines of Despair (4.4–5.0 ms of a 5.6–6.2 ms item), Holtburg shop (up to 7.6 ms), 0x00D1 (15.7–17.7 ms in 2 of 5 loads).
- **Outside dungeon loads:** the start-up's terrain atlas upload, 12.3–14.4 ms (one urgent item before the start view, terrain-renderer.md §5).

**GC-driven slow items are rarer and shorter on average, not gone.** In the after tours a GC pause still falls inside a main-thread item now and then (the Holtburg shop's 9.5 ms item in one tour was 7.6 ms of GC and 1.9 ms of work), and the GC pause during each load is lower in most locations but not all (Marketplace 6.3–7.6 ms against 0–1.5 ms, Glenden Wood 0–2.2 ms against 0, 0x00D1 above). **Server-GC pauses stretch under CPU contention:** the wave-3 review measured one 27 ms server-GC pause inside a 28.6 ms item at the Holtburg shop while two other Godot processes ran (longer than any per-frame pause it saw on the before build, 9.4–19.4 ms); how long they get under contention has not been measured systematically. A plausible reason (not verified): server GC collects on one thread per heap, and a pause lasts until the last of them has run, so busy cores delay it more than workstation GC's single thread.

## 8. Deviations and open points

- **Mip levels.** Retail has at most 4 levels. Godot has the full chain and the shader clamps sampling to level 3 (render-contract.md §3).
- **DXT mips are re-encoded** by Lifestone's simple BC encoder rather than D3DX's (render-contract.md §3). Level 0 is bit-exact.
- **Blending.** *(Wave 3.)* Blends now run in retail's byte space by default (render-contract.md §2.3; `--blend-space=linear` restores Godot's linear blending). Godot still sorts its transparent pass, where retail drew it unsorted, and its float buffer saturates only at the end of the frame.
- **Cut-outs.** Alpha-tested blending surfaces whose texels never blend draw as opaque cut-outs; retail also blends their filtered edge samples (render-contract.md §2). `--cutouts=blend` draws them as retail does.
- **Shader warm-up** compiles all 26 variants at start-up (≈ 0.2 s). A variant outside the dats' surfaces still compiles on first use; none exists in the end-of-retail data.
- **Eye height** is a 1.7 m placeholder (§5).
- **Terrain lighting seams** at landblock edges are retail (docs/terrain-shader.md §7). The placeholder layer shows them too. *(Wave 3, lead decision.)* The TexMerge terrain now uses seamless normals by default; `--retail-normals` restores the step (terrain-renderer.md §8).
- **Rendering driver.** Godot picked Vulkan on the development machine. The project leaves the driver at Godot's default, and `godot.ps1 -Driver d3d12` is available.
