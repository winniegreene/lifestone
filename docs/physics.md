# Physics and movement (M2)

**Track:** physics. **Owns:** `src/Lifestone.Data/Physics/`, `src/Lifestone.Client/Movement/` (with `Routes/`), the tests
under `tests/Lifestone.Data.Tests/Physics/` and `tests/Lifestone.Client.Tests/Movement/`, this document and
[third-party/physics.md](third-party/physics.md). **Plan:** [m2-plan.md](milestones/m2-plan.md) §3.3 (steps PH0-PH9) and
§4.10 (the C-physics contract); the specification is [m2-log-in.md](milestones/m2-log-in.md), DoD 7, 8 and 9.

Retail behaviour is re-expressed from the Mag-nus/acclient-AI-RE decompile of 11.4186 and cited by function name with
its 11.4186 address and, where `symbol_transplant_11.6096.tsv` resolves it (owner decision D4), its 11.6096 address.
Nothing from the decompile is copied. Code taken from ACE (ACEmulator/ACE @95ef426d, `Source/ACE.Server/Physics/`,
AGPL-3.0) carries a header and is listed in [third-party/physics.md](third-party/physics.md) with every deviation from
retail that was fixed.

## 1. Shape

```
Lifestone.Data.Physics            engine-free, allocation-free per step
  PhysicsGlobals, PhysicsClock      retail constants; the 30 Hz CPhysics::UseTime gate
  PhysicsCompat                     test-only ACE switches (§2)
  PhysicsState                      CPhysicsObj::state bits
  Primitives/                       sphere, cylinder-sphere, polygon, plane, box, ray (CSphere, CCylSphere, CPolygon, ...)
  Transitions/                      the CTransition machine (PH3): Transition, SpherePath, CollisionInfo, ObjectInfo,
                                    CellArray, TransitionPool (retail's ten)
  Bsp/, Geometry/                   physics BSPs of GfxObjs and cell structs, decoded from the dats (PH2), and their
                                    queries (PH3); GfxObj bounds and spheres for static objects (PH4)
  World/                            PhysicsWorld, LandblockCollision and its builder, the cells (PhysLandCell,
                                    PhysEnvCell), buildings, static objects and the cell searches (PH4)
  Objects/                          PhysicsObj (CPhysicsObj: stepping, placement, collisions, shadows; the host of the
                                    movement, MoveTo, position and target managers), PhysicsSetup, IPhysicsWeenie,
                                    IPhysicsEvents (PH5, PH6)
  IGroundProbe                      remote objects' ground clamp (D24); PhysicsWorld.TryGroundZ (PH8, §13)
  RetailFrames                      Frame/Position/Plane tests and headings the command interpreter uses (PH7)
Lifestone.Data.World.Cells
  CellBspPhysics                    the cell BSP's sphere and box tests (an additive file of the physics track)
Lifestone.Client.Movement
  OfflineWalker                     the explore-mode player: human Setup and motion table, placeholder skills (PH6)
  OfflinePlayerWeenie, PhysicsPlayerBody   its weenie; a PhysicsObj as the avatars' IPlayerMovementBody and
                                    ITargetable (PH6)
  IPlayerMovementIntents            keymap intents
  PlayerMovementController          retail's command interpreter, cadence, server positions and jump bar (PH7, §12)
  ServerPositionPolicy, JumpCharge  SmartBox::HandleReceivedPosition's player rules; ClientCombatSystem's jump bar
  MovementOptions, MovementStats, MovementNotice, NullMovementSink   the controller's options, counters, chat
                                    messages, and the sink of a player with no server
  CollisionStreamer                 3x3 collision around the player, readiness for portal space (PH7, §12)
  Routes/                           ScriptedRoute, RouteDriver, CellRoutePlanner, RouteLibrary, MovementAudit (PH9, §14)
```

The motion model (`CMotionInterp`, the movement, position, MoveTo, sticky and target managers) belongs to the avatars
track (`Data/Motion`); physics hosts it through `IMotionInterpHost` and `IMoveToHost` (PH5/PH6, §10-§11). The session's
movement sink, sequences and position verdicts belong to the state track (`IMovementSink`, `ObjectMoved`).

## 2. Decision table: retail first

Where ACE's physics differs from the retail client, Lifestone does what retail does. Each row has a test-only switch in
`PhysicsCompat` (`src/Lifestone.Data/Physics/PhysicsCompat.cs`); `PhysicsCompat.Retail` is the default and the only value
product code uses, and `PhysicsCompat.Ace` sets every row to ACE's value so tests can reproduce ACE. No command-line option
reaches them.

| # | Topic | Retail (Lifestone default) | ACE @95ef426d | Switch | Applied in |
|---|---|---|---|---|---|
| 1 | Physics step | `MAX_QUANTUM` = 1/5 s and the 30 Hz gate: `CPhysics::UseTime` (11.4186 0x00509950, 11.6096 0x0050A420) updates objects only once at least `MIN_QUANTUM` = 1/30 s has passed; `CPhysicsObj::update_object` (0x00515D10, 0x00516810) slices longer gaps into steps of at most 0.2 s (owner decision D21) | 0.1 s (PhysicsGlobals.cs:41) | `MaxQuantum` | PH1 (`PhysicsClock`), PH5 |
| 2 | `check_walkable` backup | `CTransition::check_walkable` (0x0050AFF0, 0x0050BAC0) saves the check position in a local and restores it, so the path's own backup survives | overwrites `SpherePath.BackupCheckPos` (Transition.cs:211) | `CheckWalkableOverwritesPathBackup` | PH3 (`Transition.CheckWalkable`; tested) |
| 3 | Transition step cap | `CTransition::find_transitional_position` (0x0050BDF0, 0x0050C8C0) has none | gives up above 1,000 steps (Transition.cs:506) | `MaxTransitionSteps` | PH3 (`Transition.FindTransitionalPosition`; tested) |
| 4 | Constraint offset | `ConstraintManager::adjust_offset` (0x00556180, 0x00556DA0) adds each offset's length to `constraint_pos_offset` | assigns it (ConstraintManager.cs:76) | `ConstraintOffsetAssigned` | avatars AV6 |
| 5 | Interpolation offset | `InterpolationManager::adjust_offset` (0x00555D30, 0x00556950) writes the clamped offset into the caller's frame | assigns a local, so the step is lost (InterpolationManager.cs:253) | `InterpolationOffsetDiscarded` | avatars AV6 |
| 6 | Cells near a landblock edge | `CLandCell::add_cell_block` (0x005331D0, 0x00533F10) adds every outdoor cell of the range that is on the map, across landblock edges | skips cells of other landblocks (LandCell.cs:197, "FIXME") | `AddCellBlockSameLandblockOnly` | PH4 (`CellLists.AddCellBlock`; under the switch `PhysicsWorld.Attach` drops the shadows a box search cast across an edge; tested) |
| 7 | AutonomousPosition interval | 1.0 s: `time_between_position_events`, set by the `CommandInterpreter` constructor (0x006B3E40, 0x006B4D80) and compared strictly (`interval + last < now`) by `ShouldSendPositionEvent` (0x006B45E0, 0x006B5520) | 1.875 s (CommandInterpreter.cs:32) | `PositionEventInterval` | PH7 |

Row 1 is applied by `PhysicsObj.UpdateObject` (PH5), which slices with `PhysicsCompat.MaxQuantum` (its test pins the
0.2 s and ACE's 0.1 s steps). Rows 4 and 5 are switches for the avatars track's position managers, which do not read
`PhysicsCompat` yet (a request to the lead, §10).

Each row was read in the 11.4186 decompile before it was written here: rows 2, 4 and 6 from `check_walkable`,
`ConstraintManager::adjust_offset` and `add_cell_block` themselves, row 5 from the frame assignment at the end of
`InterpolationManager::adjust_offset`, row 7 from the constructor's constant (the double 1.0) and the comparison, row 1
from `CPhysics::UseTime`, `update_object` and the static initialisers of their translation units, each with its own
copy (`MIN_QUANTUM` = 1/30 at 11.4186 0x006FB2D0 for CPhysics.obj and 0x006FBC60 for CPhysicsObj.obj, `MAX_QUANTUM` =
1/5 at 0x006FBC80; the 2 s limit is a literal in `update_object`).

**`MIN_JUMP_EXTENT`** (the jump extent used when the jump bar is below it) is the float 0.001, read by the lead from the
user's acclient.exe (L10, D4; docs/research/retail-addresses-m2.md §1.2) and used by `JumpCharge` (PH7, §12).

**Plan correction.** The physics track plan (steps S0 and S4) cites `CLandCell::add_cell_block` at 11.4186 0x00532F20. That
address is `CLandCell::find_env_collisions`; `add_cell_block` is 0x005331D0 (symbol transplant and the decompile agree).

## 3. Constants and the clock

`PhysicsGlobals` keeps only constants read in the decompile (each cited in the code): `Epsilon` 0.0002 (retail
`F_EPSILON`), `Gravity` -9.8 (a data constant; ACE's value), `FloorZ` = `(float)cos(3437.746770784939)` = 0.66417414
(GPhysics.obj's initialiser), `LandingZ` 0.0871557, the 0.04 m step-down of `step_up` and `transitional_insert`,
`MaxVelocity` 50 and `SmallVelocity` 0.25 (`UpdatePhysicsInternal`), and the quanta `MinQuantum` 1/30, `MaxQuantum` 1/5
and `HugeQuantum` 2 (each translation unit has its own copy; their initialisers are cited). The default friction (0.95),
elasticity (0.05) and dummy-sphere radius (0.1) are data constants (§10), whose 11.4186 values equal ACE's.

`PhysicsClock` is `CPhysics::UseTime`'s gate: physics runs on a frame only once 1/30 s has passed since it last ran (a
negative gap resets the clock). Each object then slices the gap on its own clock with retail `update_object`'s rule,
which `Lifestone.Data.Effects.ObjectClock` already implements (0.2 s steps, a remainder of at most 1/30 s carried, gaps
of 2 s or more skipped). Two consequences of retail's `>=` on doubles: at exactly 1/60 s per frame physics runs on every
second or third frame (21 runs in a second, not 30), and at 1/32 s per frame on every second frame. The tests pin both.

## 4. Primitives

`Lifestone.Data.Physics.Primitives`, ported from ACE and checked function by function against the 11.4186 decompile
(CSphere.c, CCylSphere.c, CPolygon.c, BBox.c, Plane.c, the Position and Ray functions):

- `PhysSphere` (`CSphere`), `PhysCylSphere` (`CCylSphere`): mutable structs, as retail passes pointers and writes
  centres in place. Their pure tests are public; their transition responses (collide, slide, step up and down, land,
  `intersects_sphere`) are internal and work on `Transitions.Transition`.
- `PhysPolygon` (`CPolygon`): immutable; `MakePlane` rounds as retail's x87 code (equal bit for bit to the scenery
  track's `TerrainSampler.MakePlane` on triangles, which 2,000 random triangles check).
- `BBox`, `Ray`, `PlaneMath` (`Plane`), `VectorMath` (`normalize_check_small` is the scenery track's x87 version),
  `PhysicsPositions` (`Position` arithmetic on `CellFrame`, with block offsets from `LandDefs`).
- `Transitions`: `TransitionState`, `ObjectInfoState`, `InsertType`, and the state the primitives read and write,
  `SpherePath`, `CollisionInfo`, `ObjectInfo`. Fixed arrays, no per-step allocation. The machine is §8.

The ACE deviations fixed here are listed in [third-party/physics.md](third-party/physics.md). None changes a
decision-table row, so they have no `PhysicsCompat` switch (the ACE oracle that would need one is deferred).

**Arithmetic.** Single precision through System.Numerics, as ACE; times of impact and ray parameters are doubles, as
retail returns them. Retail's x87 code keeps most intermediates in double registers, so results can differ from retail
in the last float bits; only `MakePlane` (planes are built once and decide every later test) and `normalize_check_small`
follow the x87 rounding exactly.

**Open questions (named TODOs).**
- `CPolygon::adjust_sphere_to_poly` (0x00538170, 0x00538EB0): the decompile keeps only the flags (less or equal) of the
  comparison that picks the sign of the contact distance. PH3 reads it as "the start distance is not positive" (contact at
  `+radius` from in front, `-radius` from behind). It is the only reading under which `BSPTREE::adjust_to_plane` clips a
  moving sphere to its first contact; reading it as "the movement points along the normal" gives the same time for every
  sphere that approaches the plane, the only case `adjust_to_plane` meets. ACE's reading (negative when the move is no
  longer than the radius) sends the camera's viewer sphere, which moves exactly one radius per step, through every wall.
  `TODO(physics)`: confirm the operands from the 11.6096 machine code at 0x00538EB0.
- `BSPTREE::adjust_to_plane` (0x00539BF0, 0x0053A950): the test that ends its contact-time loop early also lost its
  operands (the flags of an equality). It is read as "the contact time is 1" (ACE's constant): `adjust_sphere_to_poly`
  returns exactly 1 when the sphere already touches the plane, and the search then halves the whole path. ACE runs the
  loop's body only for 1 and gives up otherwise, which no consistent reading of the loop allows. Confirm with the above.
- Comparisons whose operands the decompile lost, where ACE's reading is kept because the surrounding code makes it the
  only consistent one: the edge-distance test in `polygon_hits_sphere`, `polygon_hits_sphere_slow_but_sure` and
  `check_walkable` (`|N x edge|² r² < side²`); the near-zero slide in the seven-argument `CSphere::slide_sphere` (the
  five-argument one makes the same test explicitly); the gate of `CGfxObj::find_obj_collisions` (0x00534700: squared
  distance less the squared radius sum below the epsilon); the step-length tests of `find_placement_pos` and
  `find_transitional_position` (a step of at least the epsilon is tried; a squared length below the squared epsilon
  ends the move); the slerp's sign test in `Frame::interpolate_rotation`; and the corner choice of
  `CLandBlockStruct::calc_water_depth` (0x005315F0: the far corner from half a cell on).
- `TERRAIN_SURF_CHAR`, the table of which terrain types are water, is data the decompile does not show; ACE's (types
  16-20, the five water types), which OpenAC repeats, is used.
- `CSphere::intersects` and `BBox::AdjustBBox` are not in the per-module decompile; ACE's strict `<` and component-wise
  reading are kept.
- Seven functions have an 11.4186 address but none in the symbol table: `SPHEREPATH::init_sphere`, `init_path`,
  `ConstraintManager::ConstrainTo`, `ClientCombatSystem::DoJump`, `CBuildingObj::find_building_collisions` and both
  `CBuildingObj::find_building_transit_cells` are marked `TODO(physics): 11.6096` where cited.

## 5. Physics BSPs and the null-child rule

**Decode** (`Lifestone.Data.Physics.Bsp`, `Lifestone.Data.Physics.Geometry`). `PhysicsGeometryLibrary` reads GfxObj and
Environment records as raw bytes under the `DatSet` lock and decodes them outside it, byte-exact (`CellRecordReader`),
caching each shape for its lifetime. `GfxObjPhysics` exists only for a GfxObj with the HasPhysics flag
(`CGfxObj::Serialize`, 11.4186 0x00534970); `CellStructPhysics` for every cell struct (`CCellStruct::UnPack`,
0x00533D00). Retail stores polygons and vertices in arrays and resolves stored ids as positions (`CPolygon::UnPack`
indexes the vertex array by vertex id, `BSPLEAF::UnPackLeaf` indexes the polygon array by a signed 16-bit index);
DatReaderWriter keys both by the stored ids, so the decoder checks that id equals position and refuses a record where it
does not. Every polygon's plane comes from `PhysPolygon.MakePlane` at decode, as `CPolygon::UnPack` calls `make_plane`.
A part collides with its degrade table's level-0 GfxObj, and only when that GfxObj has a physics BSP
(`CPhysicsPart::find_obj_collisions`, 0x0050D8D0, uses `gfxobj[0]`).

**Census** (`PhysicsBspCensusTests`, Tier=Exhaustive; the independent `RawPhysicsBspReader` matches every tree node for
node, plane and sphere bit for bit, and every polygon corner):

| | GfxObjs | Cell structs |
|---|---|---|
| Shapes | 1,258 of 15,318 GfxObjs have physics | 3,168 structs in 772 Environments |
| Physics polygons | 115,159 | 30,726 |
| Internal nodes | 124,690, all BPnN | 22,363, all BPnN |
| Leaves | 125,948: solid 1 always with polygons (60,380), solid 0 always empty (65,568) | 25,531, all solid 0 (16,173 with polygons, 9,358 empty) |
| Deepest leaf | 193 | 62 |
| Root is a leaf | never | never |

Every physics polygon has 3 to 12 corners and a finite plane (so ACE deviation 9, the empty polygon, cannot arise), and
every node's bounding sphere contains all the polygon corners of its subtree within 1e-3 m. Of the 530 Setups with
HasPhysicsBsp (2,522 parts), 973 parts' level-0 GfxObjs have a physics BSP; the others have none and do not collide. No
part has a physics BSP on its root GfxObj but not on its level-0 one, so using the root (as ACE does) would not differ
at end of retail.

**The null-child rule.** Retail `BSPNODE::UnPack` leaves a child null for the single-child and childless node types,
and its physics queries (`sphere_intersects_poly`, `sphere_intersects_solid`, `hits_walkable`, `find_walkable`,
`sphere_intersects_solid_poly`, 11.4186 0x0053CA30-0x0053CD50) call both children without a check. The census finds no such
node in any physics BSP: every internal node is BPnN, with two children. So the rule is **assert unreachable**: the
decoder accepts only BPnN internal nodes in a physics tree and refuses any other with a `TODO(physics)`
`InvalidDataException` naming the tree (data retail never shipped, with no retail behaviour to copy), and the PH3 queries
recurse into both children without null checks, as retail does. Single-child types remain in drawing and cell BSPs,
which physics does not query.

## 6. The C-physics contract

The checkpoint other tracks build against (m2-plan §3.2 C-physics, §4.10). It is additive from here on: members may be
added, none removed or changed without the lead.

| Type | Where | Consumers | State |
|---|---|---|---|
| `IPlayerMovementIntents` (`HandleCommand`, `SetHoldRun`, `SetHoldSidestep`, `ToggleAutoRun`, `CommenceJump`, `DoJump`, `Position`, `OnGround`) | `Lifestone.Client.Movement` | state ST11 (`MoveIntents` bridge), wave-2 input, `RouteDriver` | final; `PlayerMovementController` implements it (PH7) |
| `PlayerMovementController(PhysicsObj player, IMovementSink sink, MovementOptions options, ILogSink? log = null) : IPlayerMovementIntents` with `Tick(double now) -> bool` (= `StepPhysics(now)` then `UseTime()`), `Apply(in ObjectMoved, double now) -> ServerPositionOutcome`, `TakeControlFromServer`, `LoseControlToServer`, `StopCompletely`, `Disable`, `Enable`, `LoseKeyboardFocus`, `ShouldSendPositionEvent`, `Notice` (event of `MovementNotice`), `Stats` (`MovementStats`), `Jump` (`JumpCharge`), `ControlledByServer`, `Enabled`, `AutoRun`, `HoldRun`, `HoldSidestep`, `Now`, `Player`, `Motion`, `Options`, `PreviousPosition`, `PreviousStepTime`, `LastStepTime` (for drawing between 30 Hz steps, D26); `ServerPositionOutcome {Ignored, Constrained, Blipped, Teleported}`; `ServerPositionPolicy` (constraint distances and factor); `MovementOptions { RunByDefault }`; `NullMovementSink` | `Lifestone.Client.Movement` | state ST11/ST15/ST16 (sink, `Apply` from the reducer's `ObjectMoved`, `LoseControlToServer` on a server-controlled self motion), G5 (player controller), portal (`Teleported`) | built (PH7, §12). Deviation from §4.10: an optional `ILogSink` fourth argument |
| `JumpCharge` (`MinJumpExtent` 0.001, `FillSeconds`, `DualWieldFillSeconds`, `Start`, `GetPowerBarLevel`, `GetJumpPowerLevel`, `ToExtent`, `Finish`, `Pending`, `Building`, `BuildStartTime`) | `Lifestone.Client.Movement` | the UI's jump power bar (G4/G5) | built (PH7) |
| `CollisionStreamer(PhysicsWorld, Func<LandblockCoord, CancellationToken, LandblockCollision>, WorkerPool, ILogSink) : IDisposable` with `SetFocus`, `Pump(FrameBudgetScheduler)`, `PumpSynchronously`, `StateOf(LandblockCoord) -> AttachState`, `FocusCellId`, `RequestedCount`, `FailureCount` | `Lifestone.Client.Movement` | the readiness adapters (G1, state's `DataOnlyAttachments`), the headless host, G5 | built (PH7, §12) |
| `IGroundProbe.TryGroundZ(in CellFrame, float radius, out float z, out Plane contact)`; `PhysicsWorld.TryGroundZ(in CellFrame, PhysSphere foot, ...)`, `GroundProbeRise`, `GroundProbeDrop` | `Lifestone.Data.Physics`, `Lifestone.Data.Physics.World` | avatars AV7, G5 | built (PH8, §13) |
| `RetailFrames` (`GetHeading`, `SetHeading`, `FrameIsEqual`, `FrameIsValid`, `PlaneEquals`, `PositionIsValid`) | `Lifestone.Data.Physics` | PH7, PH9, G5 (headings) | built (PH7) |
| `PhysicsWorld(PhysicsCompat? = null)` with `Attach(LandblockCollision)`, `Detach(LandblockCoord)`, `IsAttached`, `GetCell(uint) -> PhysCell?`, `SweepViewer(in CellFrame, in CellFrame, float) -> ViewerSweepResult` | `Lifestone.Data.Physics.World` | `CollisionStreamer`, the headless host, G5 (chase camera) | built (PH3/PH4); `TryGroundZ` PH8. Added: `GetLandblock(LandblockCoord)`; PH5: `CurrentTime`, `UseTime(double) -> bool` (the 30 Hz gate), `SetTime`, `Player`/`SetPlayer`, `Objects`, `LostObjects`, `GetObject(uint)`; fix pass: `UnenforcedRestrictionEntries`, `LastUnenforcedRestrictionCell` (the housing-barrier TODO, §10) |
| `PhysicsObj(PhysicsWorld, PhysicsSetup?, uint id = 0, IPhysicsWeenie? = null, MotionPlayer? = null, float scale = 1) : IMotionInterpHost, IMoveToHost` (§10, §11), `PhysicsSetup` (`Load(DatSet, uint, PhysicsGeometryLibrary?)`, `FromParts`), `PhysicsSetupPart`, `IPhysicsWeenie : IMotionWeenie`, `IPhysicsEvents`, `ObjectCollision`, `TransientState`, `SetPositionFlags`, `SetPositionError` | `Lifestone.Data.Physics.Objects` | PH7 (`PlayerMovementController`), G5 (server objects that block the player, D25), avatars (`IPlayerBody`, `IPlayerMovementBody`, `ITargetable`) | built (PH5, PH6). After the C-avatars merge with AV14 (§11): `Movement`, `IsMovingTo`, `IsInterpolating`, `UnpackMovement`, `CancelMoveTo`, `UnstickFromObject`, `StickTo`, `GetStickyObjectId`, `Targets`, `TargetWorld`, `HandleUpdateTarget`, `Radius`, `Height`, `Heading`, `ExitWorld` |
| `OfflineWalker` (`Create(DatSet, PhysicsWorld, ILogSink, weenie, geometry, animations, parts, id)`: with `parts` (`PlayerAvatar.Parts`) and `id` (the player's guid) the physics object is hosted on the avatar's own part array, `Place`, `Tick`, `DoMotion`, `StopMotion`, `SetHoldRun`, `Jump`, `Body`, `Controller`, `Sink`), `OfflinePlayerWeenie`, `PhysicsPlayerBody : IPlayerMovementBody, ITargetable` | `Lifestone.Client.Movement` | G5 explore walk mode (W8), the offline route tests; G5 and state for the logged-in player (`AvatarSet.PlayerBody`) | built (PH6); `Controller` (a `PlayerMovementController` with a `NullMovementSink`) added in PH7; `PhysicsPlayerBody` takes server movements and can be targeted since the C-avatars merge with AV14 |
| `LandblockCollisionBuilder(DatSet, PhysicsGeometryLibrary? = null, CellStructureLibrary? = null, SceneryGenerator? = null, bool includeScenery = true)` with `Build(LandblockCoord, CancellationToken) -> LandblockCollision` (thread-safe; the `CollisionStreamer`'s build function) and the static `FromTerrain(LandblockTerrain, IReadOnlyList<float>)` | `Lifestone.Data.Physics.World` | `CollisionStreamer` (PH7), the headless host, tests | built (PH4) |
| `LandblockCollision` (`Coord`, `WaterType`, `LandCells`, `EnvCells`, `Buildings`, `StaticObjects`, `ForeignShadows`, `GetCell`), `PhysCell` (`Id`, `Position`, `WaterType`, `Landblock`, `RestrictionObject`, `StaticObjects`, `PointInCell`) with `PhysLandCell` and `PhysEnvCell`, `PhysBuilding`, `PhysStaticObject`, `PhysPart`, `PhysCellPortal`, `ForeignShadow`, `WaterType`; `PhysicsState`; `CellBspContact` and `CellBspPhysics` (Data.World.Cells) | `Lifestone.Data.Physics.World`, `Lifestone.Data.Physics`, `Lifestone.Data.World.Cells` | PH5-PH9, the objects track (cells an object reaches, objects.md:77) | built (PH4); immutable, shared across threads once built |
| `ScriptedRoute(Name, Goals, Laps)` with `Start`, `RouteGoal`, `RouteStatus`, `RouteDriver(IPlayerMovementIntents, ScriptedRoute, bool runByDefault = false)` with `Tick(double now) -> RouteStatus`, `FailureReason`, `Status`, `Lap`, `GoalIndex`, `CompletedLaps`, `StopsCompleted`, `Dwelling`, `Stopped` (event of `RouteStop`) | `Lifestone.Client.Movement.Routes` | state ST16 scenarios, the headless host, G6's login check | built (PH9, §14) |
| `CellRoutePlanner(LandblockCellSet)` (`Plan`, `FindPath`, `IsWalkable`, `Waypoint`), `CellRoute`; `RouteLibrary` (`HoltburgLaps(in CellFrame lifestone, PhysicsWorld, PhysicsSetup body, int laps = 3)`, `AcademyExit(in CellFrame spawn, in CellFrame exitPortal, LandblockCellSet, float stopWithin)`) | `Lifestone.Client.Movement.Routes` | state ST16, the headless host | built (PH9) |
| `MovementAudit(IMovementSink inner, IEventBus bus, Func<double>? clock = null, string route = "unnamed") : IMovementSink, IDisposable` with `Summary`, `SummaryLine()`, `ToJson()`, `RecordWhere(in CellFrame client, in CellFrame server, ushort? forcePositionSequence)`, `CompleteLap()`, `Route`, the static `FormatSummaryLine`; `MovementAuditSummary` | `Lifestone.Client.Movement.Routes` | state ST16, the headless host, smoke | built (PH9). Additions to §4.10: the optional clock and route arguments |

`ServerPositionOutcome Apply(in ObjectMoved, double now)` takes the state track's event directly (§4.10's sketch), so the
reducer's verdict is the only sequence judgement; the controller never looks at sequences.

## 7. Status

| Step | What | State |
|---|---|---|
| PH0 | Decision table (§2), this document, third-party list | done |
| C-physics | API sketch (§6) | done |
| PH1 | Primitives, transition state, clock | done |
| PH2 | Physics BSP decode and census | done |
| PH3 | BSP queries, the CTransition machine (§8) | done |
| PH4 | Cells, `LandblockCollision`, `PhysicsWorld` (§9) | done |
| PH5 | `PhysicsObj` (§10) | done |
| PH6 | Motion integration, `OfflineWalker` (§11) | done |
| PH7 | `PlayerMovementController`, cadence, `ServerPositionPolicy`, `JumpCharge`, `CollisionStreamer` (§12) | done |
| PH8 | `IGroundProbe` on `PhysicsWorld` (§13) | done |
| PH9 | Routes, `MovementAudit`, offline route tests (§14); the loopback scenarios are state's ST16 | done |
| PH-O | ACE oracle harness | deferred (scope, m2-plan §9 risk 17) |

## 8. The transition machine (PH3)

`Lifestone.Data.Physics.Transitions.Transition` is retail `CTransition` (11.4186 PORTAL/cphysics/CTransition.c), with
`SpherePath` (`SPHEREPATH`), `CollisionInfo` (`COLLISIONINFO`), `ObjectInfo` (`OBJECTINFO`) and `CellArray`
(`CELLARRAY`). The physics BSP queries (`BSPNODE`, `BSPLEAF`, `BSPTREE`) are on `PhysicsBspNode`, `PhysicsBspLeaf` and
`PhysicsBspTree`; the cell BSP's sphere and box tests are `CellBspPhysics` (an additive file in `Data/World/Cells`).
Every function is cited in the code with both addresses; the ACE deviations fixed are listed in
[third-party/physics.md](third-party/physics.md).

**Use.** A transition comes from the world's pool (`PhysicsWorld.Transitions`: retail's ten, `CTransition::makeTransition`;
returned in reverse order; exhausting it throws, where retail answers null). The caller (PhysicsObj, PH5) fills it as
`CPhysicsObj::transition` does: `InitObject` (flags from `get_object_info`: EdgeSlide, Contact and OnWalkable from the
carried contact, PathClipped for missiles, FreeRotate), `InitContactPlane`/`InitSlidingNormal`, `InitSphere`,
`InitPath`, then `FindValidPosition`; the result is the path's current position and cell, the contact plane and sliding
normal in `CollisionInfo`, and `ObjectInfo.VelocityKilled` (retail zeroes the velocity at once). Nothing allocates once
the cell array has grown (`PhysicsAllocationTests`).

**Retail behaviour worth knowing** (each read in the decompile and pinned by a synthetic test in
`TransitionSyntheticTests`):
- A move is cut into steps of at most the first sphere's radius (`calc_num_steps`), so a 50 m/s sphere does not tunnel
  through a 1 cm wall. A viewer always takes `floor(distance / radius) + 1` steps of exactly one radius.
- An object in contact is not clipped to the moment of contact: a refused step slides from where the object was
  (`step_up_slide`, `slide_sphere`), so it stops short of a wall by up to one step, and the sliding normal then keeps
  every later step off the wall (`adjust_offset`). Only PerfectClip objects (missiles, and the camera) are clipped to
  the contact.
- A step up is a step down by the step-up height onto what was hit (`step_up`, `step_down`).
  `CPolygon::adjust_sphere_to_plane` lets that landing use a walk interpolation down to -0.5, so a physics BSP step up to
  1.5 step-up heights is climbed (0.9 m for the human's 0.6 m); spheres and cylinders compare their heights exactly.
- With no contact plane after a step, an object in contact steps down by its step-down height (0.04 m off walkable
  ground); a one-sphere object whose step-down height exceeds its diameter steps down half its radius, and a step taller
  than the diameter is tried as two halves (`transitional_insert`, `find_placement_position`). A human (two spheres,
  1.5 m) steps down two halves of 0.75 m.
- When there is no ground in reach, an object without EdgeSlide (or off walkable ground) goes on and leaves the ground.
  One with both stops at a contact plane steeper than FloorZ (`cliff_slide`), and on a physics BSP floor walks on until
  its centre is half a radius past the edge (`check_walkables` halves the stored sphere each call, in place), then slides
  along the edge (`precipice_slide`). Terrain sets no walkable polygon, so on a terrain drop it cannot step down it
  stops (`edge_slide`'s last branch).
- A plane is walkable when its normal's Z is at least FloorZ (0.66417414): `PhysicsGlobals.FloorZ ± 1e-4` slopes are
  climbed and refused.
- The stationary-fall failsafe (`validate_transition`): an object with gravity whose step ends where it began counts the
  steps (the count is carried between transitions by the object); the third gets a horizontal plane under its sphere,
  and an object without contact also records it as an environment collision.
- On a cell corner, the cell is the last land cell found that contains the centre (`find_cell_list` keeps searching land
  cells; an EnvCell ends the search): the cell under the point, then its west, south-west and south neighbours.
- The camera sweep (`PhysicsWorld.SweepViewer`) is `SmartBox::update_viewer`'s transition: flags 92 (IsViewer,
  PathClipped, FreeRotate, PerfectClip), one sphere at the frame's origin (0.3 m in retail), so it relies on
  `adjust_to_plane` and `adjust_sphere_to_poly` (§4's open questions; the sweep test pins that it stops before a wall).
  The terrain only pushes a viewer out of the ground (`OBJECTINFO::validate_walkable`'s viewer branch).

## 9. Cells and the world (PH4)

**`PhysicsWorld`** replaces retail's static `LScape` and EnvCell tables with an instance: `Attach`/`Detach` per
landblock on the main thread, `GetCell` (a land cell or an EnvCell; null for a landblock that is not attached, as
retail's unloaded cells are), `SweepViewer`, and the pool of transitions. A static object whose shadow falls into a
neighbouring landblock's cell (decision table row 6) is registered there while both landblocks are attached.

**`LandblockCollisionBuilder.Build`** makes a landblock's `LandblockCollision` from the dats on any thread, in retail's
order (cited in the code):
1. 64 land cells from the scenery track's `TerrainSampler` (retail planes bit for bit, retail's point-in-triangle test),
   each positioned at its centre; their water from the four corners' terrain types (`CalcCellWater`), the landblock's
   from its cells (`CalcWater`).
2. Buildings (`init_buildings`): one part, the building's level-0 GfxObj at the building frame, given to the land cell
   that holds its origin (the first building only: `CSortCell::add_building`). A building collides only from that cell,
   and leads into its EnvCells through its doorways (`CBldPortal`, `CEnvCell::check_building_transit`).
3. The LandBlockInfo restriction table onto the land cells; EnvCells with their shape's physics BSP, its cell BSP (every
   end-of-retail cell BSP's positive chain ends at a node without a positive child, so the chain is the whole test),
   portals with retail `make_plane` planes, visible cells and restriction object. An EnvCell without portals contains no
   point (`CEnvCell::point_in_cell`).
4. Static objects (`CPhysicsObj::makeObject` as a static): LandBlockInfo statics in this landblock's cells, procedural
   scenery (`SceneryGenerator`, with its scale), then EnvCell statics. Each part is its level-0 GfxObj at the rest pose
   (Resting, else Default) scaled by the object; HasPhysicsBsp when a part has a physics BSP. Collision tests the BSP
   parts, else the Setup's cylinder-spheres, else its spheres. Objects that can stop nothing (no BSP, cylinder-sphere or
   sphere) are left out.
5. Each static joins every cell it reaches (`calc_cross_cells_static`): an object without a BSP searches with its
   cylinder-spheres (unscaled, as retail), any other with its parts' boxes (`find_bbox_cell_list`,
   `add_all_outside_cells`, `add_cell_block`, building and EnvCell transits). Cells of other landblocks are kept as
   `ForeignShadows`.

Holtburg (0xA9B4) has 64 land cells, 138 EnvCells, 12 buildings and 466 statics with a collision shape; the 17×17
landblocks around it build in about a quarter of a second once the shapes are cached (695 EnvCells, 60 buildings, 17,820
statics; 9 shadows cross landblock edges; 2 landblocks entirely under water).

**Water.** A cell under water lets a sphere sink 0.9 m, a partly flooded one 0.45 m at a water corner and 0.1 m
elsewhere (`get_water_depth`, `calc_water_depth`); a landblock entirely under water stops everything but the camera and
missiles (`CLandCell::find_env_collisions`).

**Entry restrictions.** A transition without a moving object enters no cell (retail `check_entry_restrictions`); housing
barriers (`RestrictionObject`) need a house weenie and are not enforced in M2 (`TODO(physics)` in `PhysCell`, counted and
logged: §10's "Not here").

**Tests** (`LandscapeCollisionTests`, `InteriorCollisionTests`, `LandblockCollisionSweepTests` (Exhaustive) on the dats):
- A human dropped on each of the 576 land cells of Holtburg and its eight neighbours rests with its lower sphere's centre
  one radius above `TerrainSampler`'s height (less the water depth; 131 drops are in cells with water) within 1e-4 m, on
  the terrain triangle's plane exactly.
- Walking east from 0xA9B4 into 0xAAB4 and north into 0xAAB5 changes the cell and wraps the local origin by 192 m.
- Walking in through each of Holtburg's 18 doors enters the doorway's entry cell (`OtherCellId`); its other 32 building
  portals are windows and openings in roofs and floors.
- From inside each upright portal a standing human fits through, in the 42 Training Academy rooms nearest the spawn cell
  0x860201AD, walking through enters the portal's neighbour (43 portals; 19 lie in floors or ceilings, 16 are slivers
  beside arches).
- A Holtburg building wall stops a walker with no sphere overlapping any of its polygons.
- The first landblock around Holtburg entirely under water (0xB0B0, beside 0xAFB0) stops a walker; a flooded cell lets a
  human sink 0.9 m.
- A static straddling a landblock edge stops a walker in the neighbouring landblock; under `PhysicsCompat.Ace` it does
  not (synthetic).
- Exhaustive: the 17×17 landblocks build and attach; a standing human is placed, on a floor, in every room of 0xA9B4 (54)
  and 0x8602 (333) whose largest floor has room for it at its centre.

## 10. Moving objects (PH5)

`Lifestone.Data.Physics.Objects.PhysicsObj` is retail `CPhysicsObj` (11.4186 PORTAL/cphysobj/CPhysicsObj.c), re-expressed
function by function and cited with both addresses; nothing is ported from ACE (whose `PhysicsObj.cs` was not used).
A `PhysicsWorld` holds what retail keeps in statics and the object maintainer: the time (`Timer::cur_time`), the player
(`player_object`), the objects by id, the shadows moving objects cast into cells, and the lost cell.

**Stepping.** The host calls `PhysicsWorld.UseTime(now)` every frame (`CPhysics::UseTime`'s 30 Hz gate) and, when it
answers true, `UpdateObject()` on the objects it steps (the player; owner decision D24 keeps server objects out).
`update_object`: an object outside a cell or frozen is inactive; one more than 96 m from the player is inactive; a gap
of at most 0.0002 s or more than 2 s is skipped; otherwise steps of at most `PhysicsCompat.MaxQuantum` (0.2 s) with a
remainder of at most 1/30 s carried. Each step (`UpdateObjectInternal`):
1. `UpdatePositionInternal`: unless hidden, the motion table's root motion (`MotionPlayer.Update`), scaled by the object's
   scale on walkable ground and dropped in the air; the position managers' adjustment; then `UpdatePhysicsInternal`
   (velocity capped at 50 m/s, ground friction `(1 - 0.95)^q`, zeroed below 0.25 m/s, `v q + a q^2 / 2`, `v += a q`, spin).
   Gravity applies except on walkable ground (`calc_acceleration`).
2. An object with spheres whose origin moved more than the epsilon on some axis runs a transition and takes its result
   (`SetPositionInternal`); otherwise it only turns. `OBJECTINFO::kill_velocity` zeroes the velocity right after the
   transition (Lifestone's transition records the request).
3. In retail's order: the target manager's reports to the objects that target this one (`TargetManager::HandleTargetting`,
   every 0.5 s of physics time), the movement manager's MoveTo (`MovementManager::UseTime`), the motion table's
   completion check (`CPartArray::HandleMovement`, `MotionPlayer.HandleMovement`), and the position manager's blip
   handshake (`PositionManager::UseTime`: a blip is a sliding `SetPositionSimple`) and sticky timeout. Detection, which
   retail runs first, is not physics.

**Taking a result** (`SetPositionInternal(transit)`): cell, frame, contact plane, Contact/WaterContact, OnWalkable from
the plane's normal (`set_on_walkable` calls the movement manager's `HitGround`/`LeaveGround`: the interpreter's, and on
landing the MoveTo manager's, which resumes its steps), the sliding normal, then
`handle_all_collisions`, then the shadows (the transition's cell array; an object with physics BSPs searches with its
parts' boxes).

**Placement** (`SetPosition`): the cell from the position (`AdjustPosition`: an EnvCell or one it sees that holds the
first sphere's centre, else the outdoor cell); a placement transition; without Slide the result must stay within 0.05 m
east and north (retail compares the signed differences) and in the same cell. A cell whose landblock is not attached
sends the object to the lost cell with its position (`PlacementPending`), answering OK; `PhysicsWorld.Attach` places it
again (`reenter_visibility`), and `Detach` sends the objects of its cells back there. `EnterWorld` (Placement|Slide) sets
an object down within its step-down height. Scatter placements throw a named TODO (retail's `Random::RollDice`).

**Collisions** (`handle_all_collisions`): each object run into is tracked by id and reported once, until a second passes
without touching it (an ethereal one at once); an environment collision is reported once while it lasts, landing counts
as one. Reports go to `IPhysicsEvents` for an object with ReportCollisions and a weenie (retail's weenie calls). The
velocity bounces off the collision normal unless the object was and is on walkable ground: elasticity 0.05, an Inelastic
object stops. The stationary-fall count zeroes the velocity from 2 on and keeps the transient stationary bits.

**Other objects** (owner decision D25, `CPhysicsObj::FindObjCollisions`): moving objects' shadows sit in cells after the
statics, and a transition meets them like statics, with retail's rules: a creature is never stepped onto and the camera
passes through it; two players pass through each other unless either is impenetrable or both are PK (or PK-lite); an
ethereal object is passed through but reported; a missile ignores missiles and, aimed, creatures other than its target.
Server objects are not stepped (D24), so a host that wants one to block the player keeps a PhysicsObj for it and moves it
with `ForceIntoCell` (retail recomputes the shadows only when the cell changes; `RecalcCrossCells` does it on demand).
An object with physics BSPs (a door) poses its parts when it moves; `SetEthereal(false)` fails while another moving object
overlaps it and is retried each update (CheckEthereal).

**Retail behaviour worth knowing** (each pinned by a test in `PhysicsObjTests`, `PhysicsObjCollisionTests`):
- Retail's vector equality is per axis within the epsilon (`AC1Legacy::Vector3::operator==`, `Vector3::operator!=`): a step
  that moves less than 0.0002 m on every axis runs no transition, and a velocity change that small is ignored.
- Landing bounces at 5% of the impact speed: a human falling 10 m (14 m/s) hops about 2 cm and lands again, each landing
  an environment collision.
- Walking head-on into an obstacle leaves a sliding normal opposite the walk; every later step is bent to nothing, which
  ends the transition at its first step without a result, so nothing clears the normal: the walker stays even when the
  obstacle goes away (a door turning ethereal), until it turns. Read in `find_transitional_position` and
  `UpdateObjectInternal`; whether retail players noticed is not known (an open question, not changed).
- The other object's collision profile is built with two arguments swapped in retail (`report_object_collision`); the
  report keeps retail's values and nothing in Lifestone reads them.

**Weenie** (`IPhysicsWeenie : IMotionWeenie`): `IsPlayer`, `IsPK`, `IsPKLite` and `IsImpenetrable` (vtable slots 4, 8, 9,
10, read where `OBJECTINFO::init` copies them). m2-plan §4.10 named only IsImpenetrable; the other three are needed for
the player rule above.

**Fixed in this stage:** `RetailHeadings.SetVectorHeading` (PH3) read the decompile's `atan2(x, y)` literally, which
turned a frame to 90 degrees minus the heading asked for (placement spirals started from another direction). The
decompile prints `Frame::get_heading`'s arctangent in the same order, where it must mean `atan2(y, x)`; both are now
read that way (ACE agrees), and `RetailHeadingsTests` pins that a frame turned to a heading faces it.

**Not here:** `MoveOrTeleport` and remote objects' `InterpolateTo` path (D24: avatars' AvatarBody), parenting, lights,
scripts, particles, detection and attacks (the game's or the renderer's), housing barriers (`TODO(physics)`
in `PhysCell.CheckEntryRestrictions`: no house weenie exists in M2, so restricted cells admit everyone, where retail
refuses a player until the barrier object is loaded and then asks the house weenie). The stub is loud: each time a player
(an object with a weenie and the IsPlayer flag, the only case retail checks) is let into a restricted cell, the world
counts it (`PhysicsWorld.UnenforcedRestrictionEntries`, `LastUnenforcedRestrictionCell`), and `PlayerMovementController`
logs the first one as a warning (category "movement", "TODO(physics): housing barrier not enforced", with the cell id).
`PhysicsObjTests` and `PlayerMovementControllerTests` pin both on a synthetic restricted cell.

**Open questions (named TODOs).** `DEFAULT_FRICTION`, `DEFAULT_ELASTICITY` and `DUMMY_SPHERE_RADIUS` are data
constants: the 11.4186 decompile's data declarations give 0.94999999, 0.050000001 and 0.1 (the floats 0.95, 0.05 and 0.1;
the first two also as `_0` copies), which `CPhysicsObj`'s constructor (0x005123F0) and CPhysicsObj.obj's static
initialiser (0x006FC370, `dummy_sphere`) read. They equal ACE's values; only their 11.6096 confirmation is open
(`TODO(physics)` in `PhysicsGlobals`, a D4-style read by the lead). `set_velocity`'s cap keeps
ACE's reading of a comparison whose operands the decompile lost. `MotionTableManager::UseTime` (called by
`CPartArray::HandleMovement`) is not in the symbol table; the avatars track reads it as ACE's
(`CheckForCompletedMotions`, in `MotionPlayer.HandleMovement`, a `TODO(avatars)`). `MovementManager::get_minterp` (11.4186
0x005242A0) has no 11.6096 address (`UnpackMovement` of an object without a movement manager).
`clear_transient_states` has no 11.6096 address (`ClientCombatSystem::DoJump`'s, 0x0056BE50, was settled in L10). The avatars track's position managers
do not read `PhysicsCompat` rows 4 and 5 (request to the lead).

## 11. Motion integration and the offline walker (PH6)

**Hosting the interpreter.** A `PhysicsObj` made with a `MotionPlayer` makes its motion interpreter as retail's
`MakeMovementManager(1)` does: `CMotionInterp::Create`, then `enter_default_state` twice (the movement manager enters it
once on creation and once more; the motion table's queue stays in step with the interpreter's). `PhysicsObj` implements
`IMotionInterpHost`: contact and walkable from the transient state, gravity from the state, `InCell`, the last move's
autonomy (`DoMotion`/`StopMotion` set it, as `CPhysicsObj::DoMotion` does), the velocity and orientation,
`SetLocalVelocity`, `SetOnWalkable` (`set_on_walkable`), `CancelMoveTo` (`cancel_moveto`: the MoveTo manager's cancel
with 0x36) and `UnstickFromObject` (`unstick_from_object`). `GetMinterp()`, `DoMotion`, `StopMotion`, `StopCompletely`,
`Jump`, `InterpolateTo`, `ConstrainTo`, `UnConstrain`, `StopInterpolating`, `TeleportHook`, `ExitWorld`, `UnpackMovement`,
`StickTo` and `HandleUpdateTarget` are retail's `CPhysicsObj` entry points.

**The movement manager, MoveTos and targets** (after the C-avatars merge brought AV14). The interpreter lives in the
avatars' `MovementManager` (`Movement`), made with it (`MakeMovementManager`); its MoveTo manager is made by the first
server MoveTo or TurnTo. `PhysicsObj` is an `IMoveToHost` and an `ITargetable`:
- `UnpackMovement` (`CPhysicsObj::unpack_movement`) applies a server MovementData: a type-0 state, or a MoveToObject,
  MoveToPosition, TurnToObject or TurnToHeading that the MoveTo manager then carries out in `UpdateObject` (steering with
  TurnRight/TurnLeft and walking or running until it arrives; `IsMovingTo` while it runs).
- The player's own command (default parameters carry CancelMoveTo), `StopCompletely` and a new interpreted state cancel
  it with 0x36; `TeleportHook` cancels it with 0x3C (`teleport_hook`: it also unsticks, stops interpolating,
  unconstrains, clears the target and tells the voyeurs `Teleported`); landing resumes its steps.
- `Targets` (made on first use, as `set_target`/`add_voyeur` make it) serves the objects that target this one: a new
  voyeur hears at once, then every 0.5 s of physics time when the predicted position moved more than its radius. The
  velocity a report carries is `get_velocity`'s, the measured `CachedVelocity`; `Radius`/`Height` are the Setup's times
  the scale.
- Target ids resolve in `TargetWorld` (the host sets the avatar set, so a server MoveTo of the player reaches a server
  object and a creature chasing the player finds the player body), else among the world's objects.
- `ExitWorld` (`exit_world`, which `Destroy` runs first as `CObjectMaint::DeleteObject` does) completes the motion table's
  and the interpreter's pending motions, unsticks, clears the target and tells the voyeurs `ExitWorld`.

**`PhysicsPlayerBody`** (`Lifestone.Client.Movement`) is the player's `PhysicsObj` as the avatars track's
`IPlayerMovementBody` (its `ApplyServerMovement` is `CPhysics::SetObjectMovement`: the autonomous bit becomes
`LastMoveWasAutonomous`, then `UnpackMovement`) and `ITargetable` (delegating to the physics object, so the avatar set's
`FindObject(player)` names it). The host makes the physics object with the player's guid, sets
`AvatarSet.PlayerBody = body` and `physics.TargetWorld = avatarSet`.

**Walking** moves by the motion table's root motion, on walkable ground only. The human walk cycle's root motion is
about 2.54 m/s on Holtburg's ground (the interpreter's state velocity, used in the air, says 3.12: avatars' finding);
running is the run cycle's 4 m/s times the run rate. Leaving the ground (a jump, or walking off a ledge without
EdgeSlide) sets the leave-ground velocity (the state velocity, with the jump's speed) and plays Falling; landing returns
to the forward command.

**Jumping** (`PhysicsObj.Jump(extent)`, the jump of `ClientCombatSystem::DoJump`): `CMotionInterp::jump`, which takes the
object off walkable ground (gravity at once) and `LeaveGround`'s velocity with the weenie's jump speed. The player's
weenie answers it with `sqrt(MovementSystem.GetJumpHeight(...) * 19.6)` (`CACQualities::InqJumpVelocity`); the apex is
then `v^2 / 19.6` (tested within 1 cm). The jump bar and `MIN_JUMP_EXTENT` are PH7's.

**`OfflineWalker`** (`Lifestone.Client.Movement`) is the explore-mode player with no server: the human Setup 0x02000001
(its collision from `PhysicsSetup.Load`) and motion table 0x09000001 as the world's player, with `OfflinePlayerWeenie`
(a creature and the client's player, not PK, carrying nothing, with **placeholder** Run and Jump skills of 100, logged as
a warning when the walker is made; a logged-in character's weenie comes from the state track, D16). Its last move is
always autonomous. It takes motion commands (`DoMotion`, `StopMotion`, `SetHoldRun`, `Jump`), `Tick(now)` steps it
through the 30 Hz gate, and `Body` is the avatars' `IPlayerBody`. PH7 puts `PlayerMovementController` (the keymap's
intents and the jump bar) in front of it.

**Tests** (`PhysicsObjMotionTests` synthetic; `OfflineWalkerTests` on the dats):
- Walking north for 10 s from Holtburg (60, 60) stays on the ground every frame, its origin 5 mm above `TerrainSampler`'s
  height (worst 2e-5 m) on the terrain triangle's plane, down 10.5 m of slope, at 2.54 m/s.
- Running moves at about 4 m/s times the placeholder run rate (1.917); a full jump rises by the placeholder jump height
  (1.636 m) within 2 cm and lands back on the ground in Ready.
- Walking at a Holtburg building wall stops the walker within a step, no sphere inside any wall polygon.
- A teleport into the Training Academy's first starting location (CharGen, cell 0x860201AD) rests on its floor and walks
  2 s indoors.
- Synthetic: the default state twice keeps the queues in step; walking off a ledge falls at the state velocity and lands
  walking; a jump peaks at `v^2 / 19.6`; walking along a wall allocates nothing per update once warm (a moving object's
  first shadow in a cell it never visited allocates that cell's list once).
- MoveTos and targets (`PhysicsObjMoveToTests`, `PhysicsPlayerBodyTests`, synthetic):
  - a server MoveToPosition walks the object to within 0.6 m in its own update and stops in Ready with the queues
    drained; one to the north-east turns to 45 degrees first;
  - a TurnToHeading of 90 takes a second at the synthetic 90 degrees a second, then snaps;
  - a MoveToObject starts on the target's first report and stops 0.6 m from its cylinder, clearing its target;
  - the player's own command cancels with 0x36; a teleport cancels with 0x3C and tells a watcher `Teleported`;
  - a watcher hears a walker's position every half second, with the measured velocity; `Destroy` drains the pending
    motions and clears the watcher's target;
  - a sticky MoveToObject ends in `StickTo`: the object keeps `StickyGap` from a target walking away, and the stick ends
    one second later (target cleared, the object stays put); a type-0 movement naming a sticky object sticks, the next
    one or `UnstickFromObject` unsticks; while stuck, an interpolation node the object cannot reach is not failed (the
    sticky id reaches the interpolation's progress check); `IsInterpolating` follows `InterpolateTo` and
    `StopInterpolating`;
  - target reports of a context other than 0 reach neither the MoveTo nor the stick; one of context 0 that is not Ok
    cancels the MoveTo (0x37) and ends the stick;
  - `UnpackMovement` on an object without a motion table makes its movement manager once, active, with the default
    state entered once (one Ready queued);
  - while the server's MoveTo runs, a held key does not take control back, and it does once the MoveTo ends;
  - through the avatar set, the player's server MoveTo reaches the physics body, and a creature chasing the player hears
    where it goes.

## 12. The player controller (PH7)

`Lifestone.Client.Movement.PlayerMovementController` is retail's command interpreter for the player: `CommandInterpreter`
(11.4186 PORTAL/cmdinterp/CmdInterp.c and CmdInterpDefs.c) with `ACCmdInterp`'s overrides (AC/accmdinterp/ACCmdInterp.c),
the jump half of `ClientCombatSystem` (AC/accui_misc/CombatSystem.c, `JumpCharge`) and the player branch of
`SmartBox::HandleReceivedPosition` (PORTAL/smartbox/SmartBox.c, `ServerPositionPolicy`). Every function is cited in the
code with both addresses; nothing is copied, and ACE has nothing to port here.

**A frame.** Retail's `SmartBox::UseTime` (11.4186 0x00455410, 11.6096 0x004554B0) runs physics, then the frame's network
messages, then `CommandInterpreter::UseTime`. `Tick(now)` is `StepPhysics(now)` (the world's 30 Hz gate and
`update_object` for the player) followed by `UseTime()`; a host that wants retail's order exactly calls the two halves and
gives the session's `ObjectMoved` events for the player to `Apply` in between. The controller owns the world's gate, so a
host must not call `PhysicsWorld.UseTime` as well. Its clock (`Now`) is the latest time it was given. For drawing the
player between the 30 Hz steps (D26) it keeps the position before the last step and the times of the last two steps
(`PreviousPosition`, `PreviousStepTime`, `LastStepTime`).

**Keys** (`HandleCommand`, `SetHoldRun`, `SetHoldSidestep`, `ToggleAutoRun`; `CommandInterpreter::HandleKeyboardCommand`,
11.4186 0x006B3690, 11.6096 0x006B45D0):

| Rule | Retail function (11.4186 / 11.6096) |
|---|---|
| Movement keys are kept in three lists, newest first: forward/backward (the locomotion substates, bit 0x04000000), turns, sidesteps. A key pressed twice is held twice. | `WhichList` 0x006B3950 / 0x006B4890, `CommandList::AddCommand` 0x006B4B80 / 0x006B5AC0 |
| Releasing a list's newest key falls back to the key pressed before it; releasing an older one changes no motion but still sends a MoveToState. | `NukeCommand` 0x006B3A20 / 0x006B4960, `CommandList::RemoveCommand` 0x006B4960 / 0x006B58A0, `BookkeepCommandAndModifyIfNecessary` 0x006B3AD0 / 0x006B4A10 |
| A substate without a list (Ready, an emote state) becomes the transient substate (all but Ready), and any new forward command ends autorun. | `AddCommand` 0x006B39A0 / 0x006B48E0, `HandleNewForwardMovement` 0x006B3D60 / 0x006B4CA0 |
| The sidestep modifier turns TurnRight/TurnLeft into SideStepRight/SideStepLeft when they are applied. | `ApplyHoldKeysToCommand` 0x006B3340 / 0x006B4280, `SetHoldSidestep` 0x006B3410 / 0x006B4350 |
| The run key (`SetHoldRun`, the keymap's input action 0x32) goes straight to the motion interpreter XOR the character's run-by-default option (`MovementOptions.RunByDefault`, `ACCmdInterp::UITogglesRun`), whoever controls the player; nothing is sent. Through the command path (`HandleCommand` with HoldRun 0x85000001) it is a key like the others below (a release is forgotten while the server is in control, a press takes control back) and a MoveToState follows unless the player stands still. | `ACCmdInterp::OnAction` 0x0058B370 / 0x0058C1A0, `SetHoldRun` 0x006B3370 / 0x006B42B0 |
| Autorun is WalkForward at the autorun speed as a mouse command with the run key (so it always runs); "AutoRun ON"/"AutoRun OFF" go to the chat window. | `SetAutoRun` 0x006B4850 / 0x006B5790, `ToggleAutoRun` 0x006B3CC0 / 0x006B4C00, `ApplyCurrentMovement` 0x006B3430 / 0x006B4370 |
| A pressed key is `CPhysicsObj::DoMotion` with the action stamp, the speed and the Autonomous flag (an action first stops the player completely and on success advances the stamp); a released one `StopMotion`. Refusals for crouching, sitting, lying down or emoting in combat, being too tired or an emote from this position are `MovementNotice`s. | `MovePlayer` 0x006B3F40 / 0x006B4E80, `StopCompletely` 0x006B3B50 / 0x006B4A90 |
| `Disable` drops the keys and stops a player the client controls, and nothing is taken or sent until `Enable`; `LoseKeyboardFocus` (the chat entry takes the keyboard) drops the keys, releases the modifiers and a pending jump, and stops the player. | `Disable` 0x006B3250 / 0x006B4190, `Enable` 0x006B3290 / 0x006B41D0, `LoseKeyboardFocus` 0x006B31F0 / 0x006B4130 |
| The server controls the player at first (`controlled_by_server` starts set) and after `LoseControlToServer` (autorun off, a pending jump dropped). A key released then is only forgotten; the next press, or `UseTime` once the server's motions are done while a key or autorun is held, takes control back: the player stops completely, stops interpolating, and the held keys move it. | `TakeControlFromServer` 0x006B32D0 / 0x006B4210 with `ACCmdInterp::TakeControlFromServer` 0x0058B160 / 0x0058BF90, `LoseControlToServer` 0x006B3170 / 0x006B40B0, `UseTime` 0x006B3BF0 / 0x006B4B30 |

**The cadence** (decision table row 7; `ShouldSendPositionEvent` 0x006B45E0 / 0x006B5520, `SendPositionEvent` 0x006B4770 /
0x006B56B0, `SendMovementEvent` 0x006B4680 / 0x006B55C0):
- A MoveToState after every key event above: the raw motion state, the position, the contact flag (Contact and OnWalkable)
  and the standing long jump. It restarts the position timer and leaves the last position sent alone.
- An AutonomousPosition when, more than 1.0 s (strictly) after the last message of either kind, the cell or the frame
  differs from the last position sent (`Frame::is_equal`: origins within the epsilon, quaternions strictly within it); or,
  before that, at once when the cell differs, or in the same cell the contact plane differs (`Plane::operator==` of
  CmdInterp.obj, 0x006B3DD0 / 0x006B4D10). Only with Contact and OnWalkable and a valid position (`Position::IsValid`):
  nothing in the air, and the position goes out on the landing frame. The last position sent starts nowhere, so the
  first frame on the ground sends one. `RetailFrames` holds these tests. The AutonomousPosition is not sent into a closed `IMovementSinkGate` (play mode's
  `PresenceGatedMovementSink`, closed until the server has the player present) and is not counted as sent, so the first frame after the unhide sends one; `MovementAudit` forwards the gate.
- A Jump when the jump key is released (below). It does not restart the timer.

**Server positions** (`Apply`, by the reducer's verdict; `ServerPositionPolicy`):
- `ForceBlip`: the position turned to the player's heading (`CPhysicsObj::get_heading` 0x00511540 / 0x00512010,
  `Frame::set_heading`), a sliding `SetPositionSimple` (`SmartBox::BlipPlayer` 0x00453940 / 0x004539E0), and an
  AutonomousPosition at once. `Stats.LastBlipDistance` is the distance for the camera (`PlayerPositionUpdated` moves the
  viewer only beyond the autonomy blip distance).
- `SelfTeleport`: a sliding `SetPositionSimple` (`SmartBox::TeleportPlayer` 0x00453910 / 0x004539B0), `teleport_hook`, the
  interpreter's teleport notice (autorun off, the keys applied, a MoveToState), then `ConstrainTo` and a zero velocity. Into
  a landblock without collision the player waits in the lost cell (`PlacementPending`) until the streamer attaches it.
- `SelfEcho`: `ConstrainTo` the echoed position with the start and maximum distances of the player's cell (10/50 m
  outdoors, 5/20 m in an EnvCell; `GetStartConstraintDistance` 0x0050EBC0 / 0x0050F690, `GetMaxConstraintDistance`
  0x0050EC10 / 0x0050F6E0). Interpolating toward it happens only at an autonomy level other than 2
  (`UsePositionFromServer` 0x006B3B40 / 0x006B4A80).
- `Remote`: ignored (the avatars' business).

**The jump bar** (`JumpCharge`): the key down is `ClientCombatSystem::CommenceJump` (0x0056AF90 / 0x0056BCD0): unless a
jump is pending, `CMotionInterp::charge_jump`; refused (0x48 the position, 0x49 the load, anything else "in the air") it is
a `MovementNotice`; allowed, the bar starts (`StartPowerBarBuild`) and a MoveToState goes out (carrying the standing long
jump the charge starts when the player stands still). The level is the time held over 1.0 s (0.8 s in DualWieldCombat
0x80000046), clamped to [0, 1] (`GetPowerBarLevel` 0x0056ADE0 / 0x0056BB20). The key up is `DoJump` (0x0056B110 /
0x0056BE50, settled from the bytes in L10): the level taken before the bar finishes, at least `MIN_JUMP_EXTENT` 0.001;
`FinishJump` (the bar, and the standing long jump, end); `CMotionInterp::jump`; on success a Jump with the extent, the local
velocity (`get_local_physics_velocity`) and the position, else a notice for the load, the position or the air. The charge
does not check for the air; the jump does.

**Inferred calls.** The decompile calls the interpreter's own functions through its vtable. Most slots are fixed by the
function they must be (the callers pass that function's arguments or test its result). Three are identified by name and
use only: slot 10, the first call of `Disable` and `StopCompletely`, is read as `ClearAllCommands`; slot 20, called by
`LoseControlToServer`, `LoseKeyboardFocus` and the Escape key while the jump bar is up, as `ACCmdInterp::FinishJump`
(0x0058B1D0 / 0x0058C000); slot 29, called by
`SmartBox::PlayerPositionUpdated` for a teleport, as `PlayerTeleported` (0x006B32B0 / 0x006B41F0); slot 47, called by
`MovePlayer` before an action and by the Escape key's handler, as `StopCompletely`. Slot 28, which `MovePlayer` calls for a
dead player, is not identified: the controller logs a `TODO(physics)` error once, drops the keys and ends autorun.

**The run key's two paths.** Retail's keymap reaches the run modifier through `ACCmdInterp::OnAction` (0x0058B370 /
0x0058C1A0): input action 0x32 calls `CommandInterpreter::SetHoldRun` directly (vtable slot 8, the slot `Disable`,
`Enable`, `LoseKeyboardFocus` and `TakeControlFromServer` call with the run key's state) while the interpreter is active,
where the movement keys (actions 0x29-0x2F) and autorun (0x30) go through `ACCmdInterp::SetMotion` to
`HandleKeyboardCommand`. `IPlayerMovementIntents.SetHoldRun` is that action: it changes the motion and nothing else. It
does not take control from the server (pressing it during a server's MoveTo, such as a Use approach, does not stop the
player), a release while the server is in control is not lost (the run key stays up when control comes back), and no
MoveToState goes out: the server learns of the run key with the player's next movement. The command path
(`HandleCommand(HoldRunCommand, ...)`) stays for callers that send the command itself; it behaves as retail's
`HandleKeyboardCommand` does with HoldRun.

**Retail's keymap** (`ACCmdInterp::OnAction`, 11.4186 0x0058B370, 11.6096 0x0058C1A0, while the interpreter is
active). Each movement input action becomes a motion command through `ACCmdInterp::SetMotion` (0x0058B310 / 0x0058C140),
which hands `HandleKeyboardCommand` the command and the key's start flag at speed 1; the wave-2 input binds keys the same
way:

| Input action | Retail call | `IPlayerMovementIntents` |
|---|---|---|
| 0x29, 0x2A | WalkForward 0x45000005, WalkBackwards 0x45000006 (press and release) | `HandleCommand(command, start)` |
| 0x2E, 0x2F | TurnRight 0x6500000D, TurnLeft 0x6500000E (press and release) | `HandleCommand(command, start)` |
| 0x2C, 0x2D | SideStepRight 0x6500000F, SideStepLeft 0x65000010 (press and release) | `HandleCommand(command, start)` |
| 0x2B | Ready 0x41000003, always as a press | `HandleCommand(Ready, true)` |
| 0x30 | AutoRun 0x090000C7, always as a press (a toggle) | `ToggleAutoRun()` |
| 0x31 | press: `CommenceJump`; release: `DoJump` (autonomous) | `CommenceJump(now)`, `DoJump(now)` |
| 0x32 | `CommandInterpreter::SetHoldRun(start)` | `SetHoldRun(start)` |
| the emote actions | the emote's motion command from `m_hashEmoteInputActionsToCommands`, always as a press | `HandleCommand(command, true)` |

There is no input action for the sidestep modifier: the sidestep keys are actions of their own, and HoldSidestep
0x85000002 reaches the interpreter only from a caller that sends the command (`SetHoldSidestep`, kept for that). Whether
the keymap delivers the release of the press-only actions (0x2B, 0x30) is the input layer's question; the interpreter
treats each delivery as a press.

**Retail behaviour worth knowing** (each pinned by a test in `PositionEventCadenceTests`, `PlayerMovementControllerTests`):
- A MoveToState restarts the position timer, so steering (turn keys pressed and released often) sends few
  AutonomousPositions: the offline Holtburg laps send 110 MoveToStates and 10 AutonomousPositions in 25 s, and the timer
  never runs past 0.75 s while the walker moves.
- Pressing the sidestep modifier while a turn key is held stops the turn and does not sidestep:
  `SetHoldSidestep` applies the held keys, which start SideStepRight from the turn list and then stop SideStepRight because
  the sidestep list is empty. The next press of a turn key sidesteps. Kept as retail; players never met it, because
  retail's keymap never sends the modifier (above).
- At exactly 60 frames a second the AutonomousPositions of a straight walk are 1.0167 s apart (the first frame after the
  strict 1.0 s).

**D23, measured** (`ServerPositionPolicyTests`): a player at the top run rate (4.5, 18 m/s) running outdoors with every
AutonomousPosition echoed one frame later runs at 97.5% of its unconstrained speed (17.55 m/s); the factor falls to 0.82 just
before each echo. Outdoors the slow-down is not visible. Indoors (5/20 m) the same rule, worked out rather than measured
(no synthetic EnvCell for a long straight run), would slow a top-speed runner far more: the factor would fall to about 0.4
within the second between echoes (about 25% slower on average), less where room changes send positions more often; at the
explore-mode placeholder run rate (1.917) about 5%. Retail is kept (D23), and the indoor figure goes to the lead.

**The host's frame (M2 G5, `--offline-player`).** `PlayerController` runs `PlayerMovementController.Tick` (so `StepPhysics`, whose
`update_object` makes the avatars' `CPartArray::HandleMovement` and the position manager calls itself, then `UseTime`) once a frame after the
streamer's pump, with a `DryRunMovementSink` for the messages; the keys reach the controller as `IPlayerMovementIntents` through
`Client/Play/MovementBindings` (the actions of input map 4, §12 above), and the drawn position is `DrawInterpolation.Interpolate` of
`PreviousPosition`, `PreviousStepTime` and `LastStepTime` (D26). The chase camera sweeps from the pivot (the player's origin plus
(0, 0, 1.5)) to where it wants to be with `PhysicsWorld.SweepViewer`, radius 0.3, and falls back to no sweep where no collision is attached.
A teleport (the panel, F7) is `Apply` with a `SelfTeleport` verdict.

**`CollisionStreamer`** keeps the collision around the player attached: `SetFocus(cell)` asks for the 3x3 around an outdoor
cell's landblock (the retail object rings), or an EnvCell's landblock and, once that is attached and the cell turns out to
see outside, its 3x3 (D29); builds run on the `WorkerPool`; `Pump` queues each finished build's attach on the frame
budget, nearest first, and detaches what the focus left; `PumpSynchronously` builds on the caller's thread and attaches
before it returns (the headless host). `StateOf` is `Attached` while attached, `Failed` once a build threw (logged once as
an error, category "physics"; settled), else `Pending`. Landblocks off the map are never asked for. Collision another
party attached (the host, a test) is left alone: the streamer neither builds it, nor replaces it with its own build, nor
detaches it; if that party detaches it while the focus still needs it, the streamer builds its own on the next pump. The
streamer owns a landblock only while `PhysicsWorld.GetLandblock` answers the very `LandblockCollision` it attached, so
collision another party attaches over the streamer's (`Attach` replaces) becomes theirs and is never detached.

**Not built** (each named in the code): autonomy levels other than 2 (`MovePlayer_NonAutonomous` sends DoMovement and
StopMovement events; retail's default is 2 and ACE never changes it); mouse movement and mouse-look turning
(`HandleMouseMovementCommand`, the `m_UseMouseTurning` branch of `MovePlayer`); the combat system's automatic-attack
abort. Control comes back from the server only while no server MoveTo runs (`CPhysicsObj::IsMovingTo`, §11).

**Tests:** the cadence matrix (`PositionEventCadenceTests`: standing still, one position a second in a straight walk, a
cell change, a contact-plane change on a saddle-shaped cell, the air and the landing, movements restarting the timer, the
run key sending nothing); the key lists, modifiers, autorun, control (the run key pressed or released while the server
is in control), the housing-barrier warning and jumps (`PlayerMovementControllerTests`, `JumpChargeTests`: levels at
0.25/0.5/1 s, 0.8 s in DualWieldCombat, the minimum extent); the server positions and D23 (`ServerPositionPolicyTests`);
0 bytes per frame once warm (`PlayerMovementControllerAllocationTests`); the streamer (`CollisionStreamerTests`: the 3x3,
release, failures, collision the host attached before the streamer or over the streamer's, and, with the dats, a
Holtburg room that sees outside and the Academy's dungeon).

## 13. The ground probe (PH8)

`PhysicsWorld.TryGroundZ` (the `IGroundProbe` of the avatars' AvatarBody, D24) finds the walkable ground under a remote
object that the avatars interpolate between UpdatePositions. It is retail's step down, not a new search: a single sphere
(the caller's foot sphere, by default `radius` above the origin) starts `GroundProbeRise` (0.6 m, the human's step-up
height) above the origin, in the cell `CPhysicsObj::AdjustPosition` finds for it, and `CTransition::step_down` (11.4186
0x0050B2A0, 11.6096 0x0050BD70) lowers it by 1.2 m; the cells put it on the first walkable surface the step meets, as they
put a walker down: terrain (sinking by the water depth), EnvCell floors and stairs, and the tops of static objects through
their physics BSPs. Creatures are never ground (IgnoreCreatures). The answers are in the asking position's landblock space.

The window is `GroundProbeRise` above the origin to `GroundProbeDrop` (0.6 m) below it, plus retail's step-down allowance
(a rise of up to 1.1 steps): ground up to about 0.72 m above the origin is still found, so an object sunk into a slope is
lifted, while one more than 0.6 m above any ground (a creature on a bridge, over the terrain) is not pulled down. Both
values are Lifestone's choices for the D24 deviation (retail steps every active object through full transitions).

**Tests** (`GroundProbeTests`): flat terrain within the window and outside it; a slope (the sphere's lowest point on the
terrain plane, as `OBJECTINFO::validate_walkable` sets a walker); a box's top within reach and the ground beside it; an
answer across a landblock edge in the asker's space; an unattached landblock; 0 bytes per probe; on the dats, 64 of 81 grid
points of Holtburg agree with `TerrainSampler` (the others stand on buildings and scenery) and 99 of 113 room floors are
found at their height (the others carry furniture).

## 14. Routes and the movement audit (PH9)

The DoD 8 scenarios walk the player like a person at the keyboard and audit what ACE makes of it. Nothing here is retail;
it drives retail's controller.

**`RouteDriver`** presses keys through `IPlayerMovementIntents` only. Each frame it compares the player's heading with the
bearing to its goal (horizontal, across landblocks): beyond 5 degrees it holds the turn key toward it, beyond 45 degrees it
lets go of WalkForward and turns in place, otherwise it holds WalkForward; it runs except within 2 m of a stop, where it
walks. The run key inverts the character's run-by-default option (§12), so the driver is told the option (`runByDefault`)
and holds the key when what it wants, a run or a walk, is not what the option gives: with run-by-default on it leaves the
key up to run and holds it to walk (a driver that held the key to run would walk). A goal is reached within its tolerance; a stop releases every key and dwells (`Stopped` is raised when the dwell
ends, the moment the scenarios ask where-am-I); other goals are passed through. No progress for 3 s (5 cm closer or 2
degrees better) is `Stuck`, a jump of more than 10 m in a frame (a teleport or a blip) is `Failed`; both release the keys
and name the route, lap, goal, cell and position in `FailureReason`.

**`CellRoutePlanner`** plans a walk through one landblock's rooms: breadth first over portals a standing human can walk
through (upright, the human fits through as in the Academy portal test), with two waypoints per doorway: 0.7 m before it
in the room being left and 0.5 m past it in the room entered, at the portal's centre over the floor (the highest floor
polygon under it). A point that would fall outside its room moves half as far, and an approach point that still does is
left out (a doorway's own thin cell).

**`RouteLibrary`**:
- `HoltburgLaps(lifestone, world, body, laps = 3)` (D47): a 12-point ring, point 0 due north, clockwise, of the smallest
  radius (8 to 28 m) whose ring the body can walk on the attached collision: every 0.25 m along it there is walkable
  ground within 0.3 m of the point before, outdoors, with room for the body (a placement of a test player that is destroyed
  afterwards). Each lap passes points 1-11 (1 m tolerance) and stops at point 0 (0.5 m, 1.5 s); the route's `Start` is
  point 0, where the scenario puts the player with `place loc`.
- `AcademyExit(spawn, exitPortal, cells, stopWithin)`: the planner's waypoints from the spawn's cell to the portal's, then a
  stop within `stopWithin` of the portal for 1.5 s.

**`MovementAudit`** decorates the session's `IMovementSink` (the controller is given the audit), counts MoveToState,
AutonomousPosition and Jump, and follows the bus: the player's `ForceBlip` is a correction, `SelfEcho` an echo,
`SelfTeleport` a teleport. With a clock it measures the longest time from a movement message to the next AutonomousPosition
while a movement is held (no Jump in between) and the longest echo delay. `RecordWhere(client, server, forcePositionSequence)`
measures each where-am-I in 3D across landblocks. `Passed` needs no corrections, every where-am-I within 0.5 m and the
force-position sequence of the last check equal to the first; `SummaryLine()` is the frozen verdict line, `ToJson()` the
summary for `cache/movement/<run>.json` (the host writes it: `LoginCheckRunner` writes each movement leg's `MovementAudit.ToJson` to `cache/movement/<UTC yyyyMMdd-HHmmss>-<leg>.json`, gitignored, and `RoutePlay.SummaryJson` carries it; `play.ps1` and the headless host do not write it).

**For the scenarios (state ST16).** Make the controller with the audit as its sink; give `Apply` every `ObjectMoved` of
the player and call `LoseControlToServer` for a server-controlled motion of the player; after `place loc` to the route's
`Start`, tick the controller and then the driver each frame; ask where-am-I once per stop and pass the answer to
`RecordWhere` (as built, `RoutePlay` asks 1.1 s into the 1.5 s dwell, not at `Stopped`, so the player is still standing when the answer comes) (a lap ends at each Holtburg stop: `CompleteLap`); print `SummaryLine()` at the end.

**Offline results** (`OfflineRouteTests`, the explore-mode walker with its placeholder skills): around Holtburg's arrival
point (ACE's Holtburg point of interest, beside the lifestone) the 8 m ring passes, and three laps take 25 s on the ground
every frame, stopping 0.6-0.9 m past point 0 (the stop's walk-to-ready link carries the walker on). From the Academy spawn
to the room 23 doorways away (0x8602028C, the target offline because the exit portal is a world-database object) the walk
takes about 24 s on the ground every frame, enters only the planned rooms, in the planned order, and stops 0.2 m from the
target; running round one doorway's corner it steps back over the portal it has just crossed and in again.

## 15. The offline player's walk checks (G5)

`--offline-player-checks` (a check suite of the main scene, `tools\smoke.ps1` step `offline-player-checks`) walks the real
offline player, the code that runs online, through Holtburg and judges what the controller and the camera did. It runs in the
live explore scene (`PlayerWalkChecks`, process priority -4, after the player and the camera); the scenarios themselves are
engine-agnostic (`Client/Movement/Checks`) and run headless in `WalkChecksTests` on the same collision.

**How.** A `MovementTrace` is tapped into the `DryRunMovementSink` and records every MoveToState, AutonomousPosition and Jump
with the controller's clock, ground state and contact plane. `RouteDriver` presses keys through `AuditedIntents`, which
passes each call to the controller and checks it: a press or release of a movement command is exactly one MoveToState, the run
key none, a jump charge one MoveToState, a jump not refused one Jump. Each frame a `WalkSample` is taken after the controller's
step; at the end `CadenceAudit` judges the messages against the samples.

| Check | Walk | What it asserts besides the common rules |
|---|---|---|
| `holtburg-lap` | `RouteLibrary.HoltburgLaps`, one lap, from point 0 | `Done`, one stop within 2 m of the start, no frame off the ground, the cadence |
| `jump-standing` | at point 0, hold the bar 0.4 s | leaves the ground at once, rises 0.2 m or more, lands within 0.15 m of its height and 0.75 m of its place, never below the ground the physics finds under it, one Jump |
| `jump-running` | the first four ring points at a run, a jump after 1 s | the same, and the jump carries the player 3 m or more from where the key came up (about 4.9) and the Jump message carries over 4 m/s forward (7.7); the walk goes on and finishes |
| `wall-bump` | `BuildingRoutes.WallBump`: 6.5 m from the shop's west wall, a goal behind it | ends `Stuck` outdoors at the wall, 7 m short of the goal in x, slid along the wall to the goal's y (1 m or more), on the ground |
| `stairs-up-chase`, `-first-person` | `BuildingRoutes.InAndOut` 7 m out from doorway cell 0xA9B40179, in to room 0xA9B4017A and back | the player's cells go outdoors, 0179, 017A, outdoors, the doorway cell holding the player on at least one of the two crossings; it climbs 1 m or more (about 2.4) and ends at its starting height |
| `cellar-down-chase`, `-first-person` | the same from doorway cell 0xA9B40177 to room 0xA9B40176 | the same cells' rule; it goes down 3 m or more (about 5.5) |

The rules every walk keeps: each key change is one MoveToState (above); an EnvCell the controller reports holds the player's
foot sphere (or a room it sees), and a player reported outdoors is in no room; no frame is over 50 ms; and **the player ran**
(`RouteWalk`, every walk but one that sets `RequireRun` false): over some half second on the ground it moved faster than 4 m/s
(a walk is 3.12, the stand-in's run 7.7), the MoveToState that starts the walk carries the Run hold key and a third or more of
the forward MoveToStates do (the steps within 2 m of a stop walk, and a driver that steers close to its goal sends many).
The running jump's carry is measured from where the jump key came up (`JumpTrack.Release`), not from the start of the charge.
The camera has two kinds of rule. The viewer, swept from the head, ends where it was put (so it is in no wall or floor); that
re-runs the function that produced the pose, so it catches a camera that skipped the sweep. The independent ones use the
landblock's room volumes and terrain alone: an eye in no room is above the terrain under it (found from its own x and y), and
the straight path from the head to the eye, stepped every 0.1 m, never runs from one room into another through two steps or
more of solid, and enters or leaves the building only through a doorway cell (a cell with a portal to the outside, or the
room beside one).

**The cadence rules** (`CadenceAudit`, from `ShouldSendPositionEvent` and `SendPositionEvent`): (1) an AutonomousPosition is
only sent with Contact and OnWalkable; (2) one sent less than 1.0 s after the last message that restarts the position timer (an
AutonomousPosition or a MoveToState) is only sent because the cell changed or the contact plane did, compared with the last
AutonomousPosition; (3) a player that moves on the ground is never more than 1.0 s plus the frame that notices past that
message; (4) a change of cell on the ground sends an AutonomousPosition in the same frame. A steering walk sends fewer
AutonomousPositions than seconds (a lap of 9 s sends 3): every key change is a MoveToState, which restarts the timer.
Control tests give each rule a stream that breaks it, and a camera moved through the wall must fail the wall walk.

**Findings.** (1) A teleport arrival constrains the player to the spot (`ConstrainTo`; retail's server moves the constraint with
every echo). Offline nothing echoes: a lap that goes 16 m from the spot is slowed to x0.04 and the player would stop at 50 m
(20 m indoors). `PlayerController.Teleport` now drops the constraint (`PhysicsObj.UnConstrain`);
`A_teleport_without_a_server_leaves_the_player_constrained_unless_the_host_drops_it` pins both halves. (2) The camera's
sweep can end in the player's cell id where the camera hangs in the open air outside the doorway (3 to 4 m out and above the
stairs; none to 5 frames of an 8 s walk in the game): no room's volume holds the eye, the path is clear, and the interiors'
tracker finds the camera's cell by geometry every frame (`SetViewer`'s cell is only a hint), so nothing is drawn wrongly; the
checks count these frames in their detail and do not fail them. It is probably the draw interpolation
(`DrawInterpolation.Interpolate` labels a position between two steps with the newer step's cell, a step early at a doorway;
the headless walks, which use the step positions, never show it); harmless, and not retail (retail draws the step). (3) A
running player (a step of 0.26 m and more) can cross the thin doorway cell between two physics steps, as retail's cell is the
one that holds the sphere after the step: 0032 to 017A without 0179 going in, and 017A to 0032 without it coming out, were
both seen in the headless walk and the game. The checks require the room, and the doorway cell on at least one of the two
crossings, and count how many it held. `TODO(physics-thin-doorway)`: whether retail's own player skips the doorway cell at a
run is not measured (the controller logs it once per controller, category `movement`, at the first step across a doorway without the doorway cell, for steps under 3 m). (4) **The walk checks drove the player at a walk until this fix.** The run key inverts the run-by-default
option, the offline player and the headless host run by default, and `RouteDriver` held the key to run: the lap went at 2.6
m/s, the running jump carried a walk's 3.1 m/s, and the first review frame was taken in the last 2 m of the lap, still in the
walk cycle. The driver now knows the option, and the checks assert the run (above). `RouteDriverTests` pin both halves.
(5) Seen, not fixed: a jump that lands while the route turns carries the player at up to 12 m/s for a few steps after the
landing (the 0.5 s peak of `jump-running` is 9.5 to 10.4 m/s against a run of 7.7): the air velocity seems to stay on top of the
run's. `TODO(physics-landing-velocity)`: check against retail (`CPhysicsObj::HitGround`'s velocity handling) before the
online player jumps (the controller logs it once per controller, category `movement`, at the first landing).

**Frame times.** The routes are planned once when the world around the player is complete (`plan_ms` in the log, about 11 ms:
the ring is validated against the collision) and that frame is not counted; every frame from the first check to the last is,
the teleports between checks included. Offscreen the 12000 frames of a run took at most 10.5 ms.
