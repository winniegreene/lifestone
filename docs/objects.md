# Objects: scenery, statics and buildings (M1 wave 2)

**Track:** objects. **Owns:** `src/Lifestone.Client/Objects/` (engine-agnostic, tested), `src/Lifestone.Game/scripts/World/Objects/` (Godot), the shared particle renderer's world use (`scripts/Render/Particles/`), this document and [third-party/objects.md](third-party/objects.md). It is the one way statics are drawn: the landscape's, the ones inside EnvCells (with the interiors track, §2.6) and the spell viewer's Holtburg (§2.5). The core's render-contract smoke layer (`StaticsSmokeLayer`, `--smoke-statics`) was retired in M1 wave 3.
**Builds on:** the render contract ([render-contract.md](render-contract.md): `RenderResources`, `MeshFactory`, surface shaders), streaming ([game-core.md](game-core.md) §4), the models track (`ModelAssembly`, `LodChain`, `DegradeSelector`, `SurfaceDrawRules`), the cells track (`LandblockStatics`, `EnvCellData`), the scenery track (`SceneryGenerator`), the effects track (`EffectObject`, `ParticleEmitter`, `AnimSequence`, [spell-visuals.md](spell-visuals.md)) and the spell viewer's particle renderer ([spell-viewer.md](spell-viewer.md) §3).
**Authority:** retail functions are cited by name and `acclient.c` line from the Mag-nus/acclient-AI-RE decompile of 11.4186 (`get_block_orient` was also checked against the user's 11.6096). Nothing from it is copied.

Status: every LandBlockInfo static and building, all procedural scenery and every static inside an EnvCell render through the shared surface path with retail's creation rules, posing, LOD and camera-facing impostor levels. Statics whose Setup has a default PhysicsScript or animation run it as retail does (§1.7): torches and braziers burn, chimneys smoke, fountains spray, birds circle, flags and windmills turn. Screenshots of Holtburg, Glenden Wood, Yaraq and Arwic look like Asheron's Call (§3).

**Not in M1: server-spawned objects.** Lifestones, portals, doors, signs, wells, statues, forges, chests and NPCs are ACE weenies that the server creates with `CreateObject` (ACE `landblock_instance`); they arrive with M2's login to the local ACE. M1 draws what the dats hold (LandBlockInfo and EnvCell statics, scenery), so a town seen in M1 lacks them where retail shows them (visual audit, `server-objects-absent`; lead decision, M1 wave 3).

## 1. What retail does

### 1.1 Only the 3×3 landblocks around the viewer have objects

This is the biggest single fact about retail objects, and it decides both the look and the cost.

1. `LScape::update_block` (307786) lays out the `mid_width × mid_width` landblock grid around the load point. The load point is the player's outdoor cell (`CellManager::UpdateLoadPoint`, 146439).
2. For every block, `LScape::get_block_orient` (306681) picks a polygon size from the block's Chebyshev ring around the centre: 1 for rings 0–1, 2 for ring 2, 4 for rings 3–4 and 8 beyond. The function is identical in the user's 11.6096 (`sub_5059F0`).
3. `CLandBlockStruct::generate(block, poly_size)` (354762) sets `side_cell_count = 8 / poly_size`. Only rings 0–1 keep their 8×8 cells.
4. `LScape::grab_visible_cells` (306571) calls `CLandBlock::init_buildings` (352114) and `CLandBlock::init_static_objs` (352787) for every block. Both return at once unless `side_cell_count == 8`. `init_static_objs` also runs `get_land_scenes`, the scenery.
5. A block that leaves ring 1 is regenerated at a lower detail, and `CLandBlock::notify_change_size` (352430) destroys its static objects and buildings.

So retail creates statics, buildings and scenery for the **3×3 landblocks around the viewer** and destroys them when they leave. Objects pop in and out at landblock boundaries, between 192 m and 384 m away along an axis. Within that, each object's own LOD table hides it beyond its last level (§1.4).

Lifestone does the same (`ObjectRings`, radius 1). `--object-radius=N` gives N rings instead; anything above 1 is a non-retail option.

6. **In a dungeon there is no landscape at all.** `CellManager::ChangePosition` (146646) calls `LScape::release_all` when the viewer's cell is an EnvCell that is not `seen_outside` (and keeps the landscape for building interiors, which are), and `CellManager::UpdateLoadPoint` (146439) only moves the grid from outdoor cells. Lifestone releases the ring for an indoor (dungeon) streaming focus and centres it again at the first outdoor focus (`ObjectRings.CenterFor`). Before wave 3 the ring stayed on the start landblock, so starting in a dungeon created that landblock's outdoor scenery under the cells-only cull mask (audit `objects-active-in-dungeon-landblock`).

### 1.2 Which objects are created

| Kind | Source | Retail rule | Lifestone |
|---|---|---|---|
| Statics | `LandBlockInfo` objects (cells track `LandblockStatics`) | `init_static_objs`: `CPhysicsObj::makeObject` must succeed; the frame is **landblock-local** and becomes the object's frame unchanged (`add_obj_to_cell`); `LandDefs::adjust_to_outside(landblock id, origin)` then `CLandBlock::get_landcell` (352098) must find one of *this* block's cells, else the object is destroyed | `LandblockObjectSet.Build`, `IsInOwnLandblock` |
| Buildings | `LandBlockInfo` buildings | `init_buildings`: the same cell test, `CBuildingObj::makeBuilding`, `set_initial_frame` | same, drawn with the building draw rule (§1.6) |
| Scenery | scenery track `SceneryGenerator` | `get_land_scenes`: retail filters, terrain z, scale, orientation | taken as generated; the stored quaternion is normalised (retail accepts \|q\|² within 0.001 of 1) |

- **Loadability.** `CPartArray::InitParts` creates an object only if every part loads (`CPhysicsPart::makePhysicsPart`): the part id must be a GfxObj whose LOD chain is loadable (`LodChain.IsLoadable`). `ObjectModelLibrary` reports the reason when it is not.
- **On end-of-retail data no LandBlockInfo object is rejected.** All 42,942 statics and 6,979 buildings of the 5,346 LandBlockInfos are created: none sits outside its landblock and none fails to load. The 77,069 resulting parts all have loadable chains (`Every_LandBlockInfo_object_in_the_world_is_created_with_loadable_parts`).
- **Frames are landblock-local, not cell-local.** In Holtburg every one of the 114 statics lies between 0 and 12.5 m above the exact terrain at its own (x, y), never below it; at least 20 stand exactly on it. Their x and y span the whole landblock. The highest are chimney-smoke emitters at roof height (`Holtburg_creates_every_LandBlockInfo_object_at_its_stored_landblock_local_frame`).

### 1.3 Parts

An object's parts are posed by `ModelAssembly` (models track):
- the rest pose is `CPartArray::SetPlacementFrame(0x65)`: Resting if present, else Default;
- the pose is flat, every part frame relative to the object (`CPartArray::UpdateParts`, `Frame::combine`);
- the object scale `s` (scenery's `CPhysicsObj::SetScaleStatic`) multiplies the part origins and the part scales: `gfxobj_scale = DefaultScale × s` (`CPartArray::SetScaleInternal`).

Each part is placed in landblock-local space: `origin = obj.origin + R(obj)·(pose.origin × s)`, `rotation = obj.q ⊗ pose.q`. `Part_frames_compose_the_object_frame_the_rest_pose_and_the_object_scale` checks this against `PartPose.ToMatrix` for every part of Glenden Wood.

**One posing path.** `ObjectPlacement` (Client/Objects) is the only code that places a static's parts, at rest or from an animation frame. The landscape objects (`LandblockObjectSet`), the statics inside EnvCells (`LandblockObjectSet.BuildCellObjects`, §2.6), the spell viewer's Holtburg (§2.5) and its stage clearance test (`StaticClearance`) all go through it; `Part_placement_is_Frame_combine_of_the_object_frame_and_the_rest_frame_at_the_object_scale` pins it to `AcFrame.Combine`, which moving actors (`ActorPartDraws`) and animated statics (§1.7) use. (Wave 2 restated it in the interiors' cell merger, the smoke layer and `StaticClearance`; audit `static-posing-duplicated-lod0`.)

Default animations and default scripts run as retail runs them (§1.7).

### 1.4 LOD: which level each part draws

Retail evaluates every frame, per land cell that holds objects, from the landblock draw loop (`RenderDeviceD3D::DrawBlock`, 458961) before the cell is drawn:

1. **Cell rule** (`RenderDeviceD3D::UpdateObjCell`, 458101). `d` = horizontal distance from the viewer to the cell centre (`CLandCell::GetViewerDistance`, 355104: the offset to `CLandCell::pos` with z zeroed). The cell centre is `((2i + 1)·12, (2j + 1)·12, 0)` (`CLandBlockStruct::ConstructPolygons`, 354094; ACE `LandblockStruct.ConstructPolygons`).
   - **d > 50 m** (a literal, whatever the bias): every object in the cell gets CYpt = d and the horizontal heading to the cell centre (`CPhysicsObj::UpdateViewerDistance(CYpt, heading)`, 316319). Far objects therefore switch level cell by cell.
   - **d ≤ 50 m:** the object rule.
2. **Object rule** (`CPhysicsObj::UpdateViewerDistance()`, 317913). If the horizontal distance to the object's origin is at least `Render::object_distance_2dsq`'s root (25 + 25·bias = 50 m at bias 1), every part gets the object's 3-D distance and heading.
3. **Part rule** (`CPhysicsPart::UpdateViewerDistance()`, 315098). Otherwise each part measures the 3-D distance to its level-0 GfxObj's sort centre, scaled by `gfxobj_scale` and placed by the part frame.

Then, per part (`DegradeSelector`, models track):
- with a degrade table, `level = GfxObjDegradeInfo::get_degrade(CYpt / gfxobj_scale.z)`: with the steady-state bias 1, level i is drawn while `CYpt / scale.z − 50 < max_dist_i`;
- without one, level 0 and mode 1, at any distance;
- a level whose GfxObj is 0 draws nothing. Every end-of-retail table ends with one, so objects vanish past their last level + 50 m (times the part's z scale).

Examples (tests in `LandblockObjectLodTests`):

| Object | Table (max_dist) | Drawn levels at bias 1 |
|---|---|---|
| Holtburg building `0x01000C1E` | `0x110004EE`: 48, 64, 390, nothing | level 0 to 98 m, 1 to 114 m, 2 to **440 m**, then nothing |
| Holtburg tree `0x02000258` (3 parts) | 50, 100, 200, nothing | to `250 × scale` m (a scale-1.98 tree: 495 m) |
| Lamp post part `0x010007CE` | `0x110001B1`: 12, then `0x01000B68` mode 5 to 45 | level 0 to 62 m, a camera-facing card to 95 m |

**TODO(objects): objects in several cells.** Retail adds an object to every cell its cylinder-spheres or bounding box touch (`CPhysicsObj::calc_cross_cells_static`, 322405), and `UpdateObjCell` runs for each of them in the landblock's draw order, so the last such cell sets its distance. Lifestone uses the cell holding the object's origin, which is always one of them. A large building can therefore switch level up to about one cell (24 m) away from where retail switches it.

### 1.5 Camera-facing levels (degrade modes 2–5)

`CPhysicsPart::calc_draw_frame` (315066) copies the part frame and, by the level's mode:

| Mode | Retail | Meaning |
|---|---|---|
| 1 | — | the part's own frame |
| 2 | `Frame::set_vector_heading(viewer_heading)` (357668), Euler order 0 (`euler_set_rotate`, 357589) | the rotation is replaced: local +Y points along the viewer→part direction, pitched, no roll |
| 3, 4, 5 | `Frame::rotate_around_axis_to_vector(0, 1 or 2, viewer_heading)` (357520) | the part keeps its local X, Y or Z axis; the previous axis in the cycle (Z, X, Y) turns to the direction's component perpendicular to it; the third axis is their cross product |

- `viewer_heading` is the unit vector from the viewer to whatever point set the distance (§1.4). It is (0, 0, 1) within 0.0002 m.
- **Census over the drawable part-levels of every LandBlockInfo static and building:** mode 1 130,961, mode 2 10, mode 5 12,394; modes 3 and 4 do not occur among them. Mode 5 is the common one: far levels of buildings, lamp posts and torches are cards that turn about their vertical axis.
- `DegradeFrames` re-expresses this; `DegradeFramesTests` pins the geometric meaning of each mode.

### 1.6 Draw rules

- **Buildings skip untextured sub-meshes** (`D3DPolyRender::DrawMesh` with `ObjBuildingOrBuildingPart`; `SurfaceDrawRules`, `MeshDrawContext.Building`), at every LOD level. This is how doorway and window portal polygons vanish. Statics and scenery draw everything.
- **Every LOD level is drawn with the level-0 surface list** (`CPhysicsPart::SetGfxObjArray`). On end-of-retail data every level lists the same surfaces as its level 0 (models track's exhaustive sweep), so a level's own sub-mesh surfaces are what retail draws, and meshes are keyed by the level's GfxObj id plus the draw context: `gfxobj:0x01000C1E:Building`. A part with a non-uniform `DefaultScale` adds its baked shape (§2.3): `gfxobj:0x…:Object:shape(0.594,0.514,1)`.
- **Lighting of scaled parts.** `PrimD3DRender::CalcObjectMatrixInternal` (453636) puts the part's per-axis scale into the D3D world matrix and `RenderDeviceD3D::SetDefaultD3DStates` (460762) enables `D3DRS_NORMALIZENORMALS`, so D3D lights a part with frame R and scale S with the normal `normalize(R·S⁻¹·n)` (fixed-function normals go through the inverse transpose of the world-view matrix).

### 1.7 Living statics: default scripts and default animations

Retail statics are not inert. A Setup can name a default PhysicsScript (torch flames, chimney smoke, braziers, fountains, sparkles), a default animation (birds circling, flags, windmill sails) and a default motion table, and every static object runs them for as long as it exists.

**Creation** (`CPhysicsObj::makeObject(id, 0, bDynamic = 0)`, 320829, called by `CLandBlock::init_static_objs` (352787), `CLandBlock::get_land_scenes` (352530) and `CEnvCell::init_static_objects` (347955); buildings go through `CBuildingObj::makeBuilding` (719153), which runs the same `InitObjectBegin(0, 0)` and `InitPartArrayObject`, so a Setup-based building gets the same defaults):
1. `InitObjectBegin` (317273) marks the object STATIC (`state |= 1`) and sets `update_time` to now.
2. `CPartArray::InitDefaults` (326158) appends the default animation to the part array's sequence: frames 0 to the last, 30 fps.
3. `CPhysicsObj::InitDefaults` (320854) plays the default script (`play_script_internal`), installs the default motion table (`SetMotionTableID` → `MakeMovementManager(1)` → `MovementManager::EnterDefaultState` → `CMotionInterp::enter_default_state` → `InitializeMotionTables`, which replaces the sequence with the table's default state), the sound table and the PhysicsScriptTable. For a STATIC object with a default animation it sets `HAS_DEFAULT_ANIM` (0x40000), with a default script `HAS_DEFAULT_SCRIPT` (0x80000), and with either it registers the object with `CPhysics::AddStaticAnimatingObject` (311440). `CPhysicsObj::Destroy` unregisters it.

**Stepping.** `CPhysics::UseTime` (311335) calls `CPhysicsObj::animate_static_object` (321150) for every registered static, every frame, wherever it is:
- `dt = now − update_time`. At most ε (0.0002 s): only the clock moves. Below `MIN_QUANTUM` (1/30 s): nothing, time accumulates. Above 2 s: only the clock moves. Otherwise **one step of dt**.
- With `HAS_DEFAULT_ANIM`: `CPartArray::Update(dt)` advances the sequence (its hooks are queued; root motion is not applied), `Frame::grotate(frame, omega)` turns the object by the omega a SetOmega hook set (**once per step, not scaled by dt**), then `UpdatePartsInternal` poses the parts.
- With `HAS_DEFAULT_SCRIPT`: `ScriptManager::UpdateScripts`. Then `ParticleManager::UpdateParticles`, then `process_hooks`.

So on a machine faster than 30 fps statics step at about 30 Hz, and a SetOmega bird turns by its omega per step: Holtburg's birds (`0x02000494`, omega −0.0268 rad) circle 12.2 m out once every 7-8 s.

**Which statics run, and how far.** Every created static runs (the 3×3 landblocks around the viewer, §1.1, and the EnvCells it has loaded); nothing else is distance-limited except particles: `ParticleEmitter::UpdateParticles` (331097) asks `CPhysicsObj::ShouldDrawParticles(degrade_distance)` (317184) of the emitter's particle object, false when that object's viewer distance `CYpt` exceeds the particle GfxObj's maximum degrade distance (`CPhysicsPart::GetMaxDegradeDistance`, 314372: `degrades[0].max_dist` for 1-2 levels, else the second-to-last level's, 100 m without a table; 64 or 128 m for the sprites statics use). The emitter is then *degraded out*: an emitter with a total count or time loses its particles; an endless one freezes them (their birth times reset) until the viewer comes back. `CYpt` comes from the draw pass (`RenderDeviceD3D::UpdateObjCell`, 458101, §1.4): the horizontal distance to the land cell's centre beyond 50 m, else the 3-D distance to the particle object (the emitter position); EnvCells keep `CObjCell::GetViewerDistance` (346903, returns 0), so their objects always take the 3-D distance.

**Inventory** (probe over the user's dats; LandBlockInfo statics, buildings and generated scenery of the 3×3, or every static of a dungeon landblock's EnvCells; "particles" = the steady-state count if every emitter is in range):

| Area | Objects | Default script | Emitters | Particles | Default animation | Most visible |
|---|---:|---:|---:|---:|---:|---|
| Holtburg 3×3 (A9B4) | 622 | 24 | 35 | ~224 | 27 | lamp posts `0x020005D8` ×11 (flame `0x3200002F` + glow `0x32000829`), chimney smoke `0x0200043B` ×6, birds `0x02000494`/`0x02000493`, high birds `0x020005AC` |
| Arwic 3×3 (C6A9) | 860 | 50 | 59 | ~910 | 35 | scenery `0x020007C4` ×41 (`0x32000478`, 300 m), the fountain `0x02000AA3` (4 emitters, ~144 particles), smoke stacks `0x020010B0` |
| Yaraq 3×3 (7D64) | 587 | 18 | 21 | ~67 | 4 | desert lamps `0x020009A3`, wall torches `0x020001E5` |
| Shoushi 3×3 (DE51) | 2,136 | 71 | 113 | ~608 | 101 | scenery `0x020004BE` ×41, birds ×101 |
| Glenden Wood 3×3 (A1A4) | 719 | 18 | 35 | ~252 | 4 | lamp posts ×9, 4-flame lamp `0x02000706` |
| Town Network (0007) | 1,218 | 131 | 197 | ~1,232 | 3 | wall torches `0x020001E5` ×35, braziers `0x0200190D` ×14, the fountain `0x02000AA3` |
| Holtburg Redoubt (0163) | 576 | 123 | 205 | ~1,303 | 0 | wall torches ×29 + `0x0200026E` ×21 + `0x020001EA` ×16 |
| Mines of Despair (0188) | 644 | 157 | 300 | ~2,221 | 1 | wall torches ×113, lightning spouts `0x020007C5` |
| Glenden Wood Dungeon (01E3) | 791 | 186 | 348 | ~2,469 | 0 | wall torches ×88, braziers `0x0200034C` ×28 |
| Training Academy (8603) | 975 | 235 | 250 | ~5,109 | 0 | `0x02000691` ×139 (`0x320003F9`) |

World-wide (visual audit sweep): 6,332 LandBlockInfo statics have a default script, 226 a default animation, 67 TextureVelocity hooks; 1,506 of 9,069 sampled EnvCell statics have a script. Default motion tables are rare (2 in the Town Network, 4 in the Academy).

### 1.8 Statics inside EnvCells

`CEnvCell::init_static_objects` (347955) creates a cell's statics with the same `CPhysicsObj::makeObject` and places each at its stored frame with `add_obj_to_cell` (landblock-local like LandBlockInfo frames; no inside-the-landblock test, dungeon cells lie far outside their square). They are drawn with the cell (`PView::DrawCells`, 461450) and take their LOD from `DrawObjCellForDummies` → `UpdateObjCell` for each drawn cell (§1.4 with `CObjCell::GetViewerDistance` = 0: the object and part rules, never the far-cell rule), so a dungeon's far objects degrade and vanish exactly as outdoor ones do: in the Holtburg Redoubt 568 of 584 cell-static parts have degrade tables (median first switch at 60 m, median vanishing distance 105 m), and 112 have a camera-facing level. A wall torch `0x020001E5` (`0x01000938`: 9, 15, 40, nothing) draws level 0 to 59 m, then levels 1 and 2, and nothing beyond 90 m. Cell statics are lit by the cells' static lights (docs/interiors.md §4.4).

A few cell statics are never drawn at all: their level 0 has min, ideal and max distance 0, and `GfxObjDegradeInfo::get_degrade` (332356) moves past such a level at any distance (`while (distance >= ideal − (ideal − max) × bias) ++level`) to a level whose GfxObj is 0. The structure behind the Town Network's fountain (`0x02000C39`, GfxObj `0x010028CA`) and the ceiling light holders `0x02000361`/`0x02000365` are such statics. The wave-2 LOD-0 merge drew them; retail does not, so their absence since wave 3 is faithful, not a regression (`before-town-network` against `after-town-network`, §3.1).

## 2. How Lifestone does it

### 2.1 Code

| File | Engine | Role |
|---|---|---|
| `Client/Objects/ObjectRings.cs` | none | The 3×3 rule (§1.1) |
| `Client/Objects/ObjectModelLibrary.cs` | none | Thread-safe cache of resolved models (parts, LOD chains, rest poses, sort centres, loadability) and LOD chains |
| `Client/Objects/LandblockObjectSet.cs` | none | A landblock's created objects and their placed parts (structs, one flat level table), plus the distinct meshes they can draw (§1.2, §1.3) |
| `Client/Objects/LandblockObjectLod.cs` | none | Per-frame retail LOD (§1.4) feeding `InstanceBatches` |
| `Client/Objects/DegradeFrames.cs` | none | Camera-facing draw frames (§1.5) |
| `Client/Objects/InstanceBatches.cs` | none | Which parts each mesh batch holds; O(1) moves and a dirty set |
| `Client/Objects/PartShapes.cs` | none | Non-uniform part scales split into a baked mesh shape and a uniform instance scale (§2.3) |
| `Client/Objects/TexturePinSet.cs` | none | The textures one attach holds resident until its meshes hold them (§2.2) |
| `Client/Objects/ObjectPlacement.cs` | none | The one static posing rule (§1.3) |
| `Client/Objects/LivingStatics.cs` | none | Living statics (§1.7, §2.7): `LivingStatic` (one object's `animate_static_object`), `LivingStaticPlan` (worker: which objects, their records and particle GfxObjs), `LivingStaticSet` (a landblock's, stepped within a budget, with the emitters' draw range), `LivingStaticSources` |
| `Client/Objects/CellObjectLod.cs` | none | EnvCell statics' LOD parts and their per-frame draw states (§2.6) |
| `Game/World/Objects/ObjectsShared.cs` | Godot | The model library and living-static sources every object path shares |
| `Game/World/Objects/LivingStaticsSystem.cs` | Godot | Steps every registered landblock's living statics and draws their particles (§2.7) |
| `Game/World/Objects/CellObjects.cs` | Godot | EnvCell statics (§2.6): the shader code, the worker-side merge (`PreparedCellObjects`) and the LOD-state table (`CellObjectsSystem`) |
| `Game/World/Objects/StaticObjectsView.cs` | Godot | The objects path for a fixed set of landblocks, without streaming (the spell viewer, §2.5) |
| `Game/World/Objects/ObjectsLayer.cs` | Godot | The `ILandblockLayer`, `Install` and the prepared-objects holder |
| `Game/World/Objects/ObjectsSystem.cs` | Godot | Per-landblock activation states, prepares, budgeted attaches, per-frame LOD, statistics |
| `Game/World/Objects/PreparedLandblockObjects.cs` | Godot | Worker-side preparation: object set, meshes, surfaces, transforms, batch bounds |
| `Game/World/Objects/PreparedGeometryCache.cs` | Godot | Bounded LRU of converted GfxObj geometry shared by all landblocks |
| `Game/World/Objects/LandblockObjectBatches.cs` | Godot | One active landblock's `MultiMeshInstance3D` batches; stepwise attach, per-frame rewrite, release |
| `Game/World/Objects/LandblockAttachTextures.cs` | Godot | An attach's texture pins, worker re-decodes of evicted textures, and texture upload units |
| `Game/World/Objects/ObjectsOverlay.cs` | Godot | The HUD line under the perf overlay (F3 toggles both) |

### 2.2 Flow

```
ObjectsLayer (streamer)          ObjectsSystem (per frame, priority 5)                 main-thread queue
  Prepare: near the centre? ──►  states per landblock:                                (budgeted, nearest first)
    yes: prepare objects            idle ─(ring ≤ 2)─► preparing (worker) ─► prepared
    no : marker                     prepared ─(ring ≤ 1)─► pin resident textures
  Attach: register root               all there ─────────────────────► attaching ───►  attach units
  Detach: unregister                  some evicted ─► decoding (worker) ─► attaching
                                    attaching ─(done)─► active: LOD every frame
                                    active/attaching ─(ring 2)─► objects destroyed (retail notify_change_size);
                                                                  prepared data kept ─► prepared
                                    prepared ─(ring > 2)─► idle (prepared data freed)
```

- **Centre.** The ring centre is the streaming focus's landblock, as retail centres on the player; in M1 the camera is the focus. An indoor (dungeon) focus has no ring: every landscape object is released, and the first outdoor focus brings the ring back (§1.1, `ObjectRings.CenterFor`). The system runs at process priority 5: after the camera moves (0) and before the world rebases and plans streaming (10). The ring is therefore current when the streamer starts its prepares, and the viewer-to-landblock offsets are the same before and after a rebase.
- **Two paths, one state machine.**
  - At start-up and after a teleport, landblocks within two rings of the centre are prepared inside the streamer's own prepare, and the 3×3 attaches through the streamer's budgeted attach. "Streaming idle", which screenshots and the fly path wait for, therefore includes the first objects.
  - After that, the system prepares ring-2 landblocks ahead on the worker pool. Crossing a landblock boundary then only costs the budgeted main-thread attach of the new ring-1 landblocks.
  - A landblock that leaves the 3×3 loses its drawn objects at once, as in retail, but keeps its CPU-side prepared data (object set, transforms, culling bounds, LOD state, instance buffers) while it stays in ring 2. Coming back costs only the attach; its images were uploaded, so its textures are pinned or decoded again (below). Only landblocks that leave ring 2 are freed and prepared from scratch later.
- **Prepare** (worker, `PreparedLandblockObjects.Prepare`), in this order:
  1. read the LandBlockInfo and generate the scenery;
  2. build the `LandblockObjectSet`;
  3. for every mesh any LOD level can draw, take the converted geometry from the shared `PreparedGeometryCache` (or build it with `MeshFactory.Prepare` under the draw rule);
  4. resolve its surfaces once per landblock (`RenderResources.PrepareSurface`, which decodes textures that are not resident);
  5. compute each part's instance transform (`AcToGodot.Transform`) and each batch's bounds;
  6. plan its living statics (`LivingStaticPlan`, §2.7): which objects run a default script or animation, with their scripts, emitters, animations and particle GfxObjs decoded here.
- **Textures when the attach is decided** (main thread, `LandblockAttachTextures.PinResident`). A worker skips decoding a texture that is resident when it prepares (`RenderResources.PrepareSurface`), and a prepared landblock can wait in ring 2 for any length of time, so such a texture may have been evicted by the time the landblock enters the 3×3. Before the attach starts, every texture the landblock's meshes name is:
  - **pinned** (one cache reference, `TexturePinSet`) if it is resident, so nothing can evict it during the attach; or
  - an **image in hand** from the prepare, uploaded by an attach unit; or
  - **decoded again on a worker** (state *decoding*, `LandblockAttachTextures.Decode`) before the attach is queued. A streamer attach that finds a texture missing hands the landblock to this path instead.

  So `MaterialFactory`'s main-thread decode fallback never runs inside an attach step. The pins go when the attach ends (every mesh then holds its materials, and they their textures) or when the landblock leaves early.
- **Attach** (main thread, `LandblockObjectBatches.AttachStep`, slice 0.75 ms):
  1. The first step evaluates the LOD for the current viewer and orders the meshes: those drawn now first, nearest member first.
  2. Each step then runs units until its slice is spent. A unit is one texture made resident (`LandblockAttachTextures.Ensure`: already pinned, or its image uploaded and pinned), or one shared mesh (`AcquireMesh`) with its `MultiMeshInstance3D` and first fill.
  3. The last step re-evaluates, flushes, drops the pins and disposes the prepared images, and creates the landblock's living statics (retail creates them with the objects; they go with them, `Release`).
  4. A step costs the slice plus at most one unit.
- **Warm-up.** At start-up the system runs the attach path once on an empty landblock and a throw-away `MultiMesh`, in about 15–30 ms. Without it the first real step paid 15 ms of first-use JIT and interop (measured).

### 2.3 Batches

- **One `MultiMeshInstance3D` per (landblock, mesh).** Its instances are the parts whose current level draws that mesh. Instance count = the number of parts that can ever draw it; `VisibleInstanceCount` = how many do now. Empty batches are hidden.
- **A per-landblock Objects node** sits under the landblock root, so the floating origin moves it with the landblock.
- **Culling bounds.** Each batch has a conservative custom AABB: the union of its candidate parts' spheres (part origin, mesh radius × largest part scale). It holds in any camera-facing orientation, and Godot never recomputes it from the instance buffer. A part of an animated object takes a sphere around its object's origin grown by the object's pose reach (the farthest part origin over its rest pose and every frame of its default animation or motion table, `LivingStaticSources.PoseReach`), which also holds under SetOmega turns.
- **Per frame**, for each active landblock:
  1. `LandblockObjectLod.Evaluate` runs when the viewer moved more than 1 mm;
  2. parts move between batches (O(1));
  3. only the batches whose membership changed, or that hold camera-facing or moving parts, rewrite their buffer (Godot's 3×4 row layout, `PreparedLandblockObjects.WriteTransform`).
- **Moving parts.** After a living static's step moved its parts (§2.7), `LandblockObjectLod.SetPartFrame` takes their frames, `EvaluateObjects` re-selects only those objects' levels (from the moved sort centres), and their batches are rewritten.
- **Materials** are the contract's shared `ShaderMaterial`s. The surface shaders read `MODEL_MATRIX`/`MODELVIEW_MATRIX`/`MODEL_NORMAL_MATRIX`, which include the MultiMesh instance transform, so lighting and fog work unchanged.
- **Non-uniform part scales** (`PartShapes`). Godot multiplies a MultiMesh instance's normals by the instance's 3×3 (R·S), where D3D uses its inverse transpose (R·S⁻¹, §1.6). For a uniform scale both normalise to the same normal; for a non-uniform one they do not. So a part's scale S = DefaultScale × s is split into a shape σ = DefaultScale / DefaultScale.z, baked into the mesh (positions σ·v, normals normalize(σ⁻¹·n), zero normals kept), and a uniform instance scale S.z. Positions come out as R·S·v and normals as normalize(R·S⁻¹·n), exactly retail's (`A_baked_shape_reproduces_retail_positions_and_normals_under_any_part_frame`).
  - σ depends only on the Setup part, so all instances of a part share one baked mesh. On end-of-retail data 30 Setups have such parts (66 parts, 77 distinct GfxObj and shape pairs), for example the trunks of the tree `0x020007A3`, scaled (0.208, 0.18, 0.35). Arwic's 3×3 has 141 such parts, Holtburg's 29.
  - The LOD keeps using the full scale (`CYpt / gfxobj_scale.z`).

### 2.4 Memory and caches

- **Shared meshes** (and through them materials and textures) are held only while a landblock is active and released when it leaves the 3×3. Nothing pins them afterwards (render-contract.md §4.1).
  - The batch nodes' `MultiMesh`es are unbound and disposed at release, so their instance buffers and their references to the shared meshes go at once, not at a garbage collection. Every start-up checks that this drops the mesh reference (`multimesh_release_frees_mesh_ref=True` in `[objects] objects system ready`).
  - Attach pins (§2.2) hold one landblock's textures for the length of its attach; the texture cache can sit above its budget by that much meanwhile.
  - **Measured:** after a Holtburg → Arwic flight, 161 of 284 textures are idle.
  - With `--texture-budget-mb=8 --mesh-budget-mb=1`, 243 textures and 288 meshes are evicted on the way, and the texture cache stays at 10.1 MB, its budget plus what is in use.
- **Decoded images** are disposed when the attach ends, when a prefetched landblock leaves ring 2, or when a cancelled prepare's or decode's result arrives.
- **Kept prepared data.** Ring-2 landblocks keep their prepared data (§2.2): object set, transforms, bounds, LOD state and the per-batch instance buffers, which the next attach reuses. That is 16 landblocks at most, and the geometry they reference is shared through `PreparedGeometryCache`.
- **`PreparedGeometryCache`** is a bounded LRU: 2,048 entries and 1.5 M vertices. The Holtburg → Arwic flight converts 564 GfxObjs once and reuses them 21,844 times, which keeps the workers' allocations, and the garbage collector's work, down.
- **`ObjectModelLibrary`** caches models and LOD chains without a bound. The dat bounds it at 5,935 Setups and 15,318 GfxObjs, a few hundred bytes each.
- **Parts are structs** in one array per landblock, with a flat level table. Every part as a heap object would lengthen each collection while a landblock's set is alive.

### 2.5 Options and diagnostics

| Argument | Effect |
|---|---|
| `--objects=all\|off\|statics,buildings,scenery` | Which kinds to create (default all) |
| `--object-radius=N` | Rings of landblocks around the viewer's that have objects (default 1, retail; up to 8) |
| `--static-effects=on\|off` | Living statics (§1.7): default scripts and animations of statics, landscape and EnvCell alike (default on; off draws them at rest, as before wave 3) |
| `--smoke-statics` | Retired with the render-contract smoke layer (wave 3): reported as a bad argument and ignored (an automated run with it exits 1) |

- **HUD lines** (under the perf overlay): the ring centre; landblocks active, attaching, decoding, prepared and preparing; statics, buildings and scenery; parts drawn and camera-facing; batches shown and their instances; shared meshes held; the LOD update time; the slowest attach step. Then the living statics: how many and in how many sources, stepped this frame; emitters and how many are out of range; particles drawn and live; batches; the step and draw times and their maxima.
- **Logs:**
  - `[objects] summary` at exit: those counts, plus draw calls, FPS, memory and GC counts and pause time, the system's own prepares, re-attaches from kept prepared data and textures decoded again;
  - `[objects] living statics summary` and `[objects] frame times (steady state)` at exit: sources, statics, emitters, drawn particles (average, maximum), step and draw times (average, maximum), frames the step budget ran out, particle meshes prepared and requested late; the average, p99 and maximum frame time from frame 600 on (with or without living statics, for on/off comparisons);
  - debug lines for slow LOD updates, slow batches and slow attach steps (with the garbage collections inside them), and for landblocks whose textures were evicted since their prepare;
  - the objects retail would not create, once per landblock and reason.

**The spell viewer** draws Holtburg's 3×3 through the same path (`StaticObjectsView`: `PreparedLandblockObjects.Prepare` on the workers, `LandblockAttachTextures`, `LandblockObjectBatches` attach steps within 3 ms a frame, the LOD every frame), scenery and living statics included: the lamp post 3.7 m from the cast line burns. Its `LivingStaticsSystem` shares the viewer's particle meshes.

### 2.6 Statics inside EnvCells

The interiors track merges each cell's statics into the cell's mesh, lit per vertex by the cell's static lights (docs/interiors.md §4.4); that keeps a dungeon to a few draw calls per cell. The objects path now creates, poses and LODs them (§1.8), without unmerging them:

- **Creation and posing** (worker, `PreparedCellObjects.Create`): the statics of the cells being prepared form one `LandblockObjectSet` (`BuildCellObjects`: kind `CellStatic`, each object with its cell), placed by `ObjectPlacement`. Objects whose model does not load are not created (retail `makeObject`); wave 2 drew the parts that loaded.
- **Which parts need a state** (`CellObjectLod`): a part with a degrade table (its level changes with distance, and some levels face the camera) or a part of a posed object (a default animation or motion table). The others (3% of the Redoubt's parts) have one level, drawn at any distance, and are merged as before.
- **Merge** (`PreparedCellObjects.AddCellObjects`): a part with a state contributes every level's geometry, each vertex tagged in CUSTOM2 with (1 + the part's slot, its level); its cell's bounds grow by where a camera-facing level can turn or an animated part can move.
- **State table** (`CellObjectsSystem`, process priority 7): a global RGBA32F texture `ac_object_lod` (project.godot `[shader_globals]`), 85 slots of 3 texels per row: the level drawn, then the rotation q and translation t from the part's rest frame to its draw frame (Godot axes). Each attached batch of cells gets a range of slots (`Registration.SlotBase`, added to the tags when the cell meshes are built) and gives it back when its landblock detaches. Every frame the levels and draw frames of the statics in the cells of the portal view are re-selected (`LandblockObjectLod.EvaluateObjects`, the rules of §1.8); only changed slots are written, and the texture is uploaded once when anything changed.
- **Shader** (`CellObjectLodShader`, spliced into the cell-object lighting rule, `SurfaceLightingCode.VertexLight`): a vertex whose level is not the part's current one goes to one clip-space point outside the view, so its triangles collapse; a drawn vertex moves to `q·v + t`. Vertices with CUSTOM2 = 0 are untouched, so every mesh made without tags (the warm-ups, the engine checks) draws as before.
- **Their living statics** (the torches' flames, the braziers' fire, the Town Network's fountain) register with `LivingStaticsSystem` per batch of cells, draw on the cells' layer (`InteriorController.CellsLayerMask`, hidden with them when the landscape is), and are simulated and drawn only for cells in the portal view (§1.7: the cell is drawn).
- **Their particles take their cell's depth rules** (wave-3 review fix). The interiors draw a cell with remapped depth: the viewer's cells in front of the landscape while it is in view, and cells seen from outdoors clipped to the doorway's cone with their depth pulled to the doorway plane (docs/interiors.md §3.4). A particle drawn with the plain rules failed the depth test against its own room's walls: a torch inside a seen_outside building went dark whenever a doorway or window was on screen. Retail draws a cell's objects, particle objects included, in the cell pass after the same depth reset (`PView::DrawCells` → `DrawObjCell`), with the sun off (`Render::useSunlightSet(0)`). So:
  - cell sources draw with the cell-object particle variants (`ParticleShaders.GenerateCode` for `SurfaceLighting.CellObject`: `SurfaceLightingCode`'s slot lookup, front remap and doorway prologue);
  - the cell's slot comes per instance in `INSTANCE_CUSTOM.z`, because a particle mesh has no CUSTOM1. `LivingStaticSet.DrawnCells` names each drawn static's cell, and `InteriorController.SlotOf` gives the cell's slot, looked up every frame because a cell gets its slot when its mesh attaches, after its statics register;
  - the lit colour is luminosity + world ambient (§4).
  
  Landscape statics keep the object variants. `ParticleMeshLibrary` keeps one mesh per lighting rule, and a world without cells never builds a cell mesh.

### 2.7 Living statics

- **Plan** (worker, with the landblock's prepare or a batch of cells): `LivingStaticPlan.Create` lists the objects whose model has a default script, animation or motion table and decodes their records into the shared caches (`LivingStaticSources`: scripts following CallPES, emitters, animations, motion tables), the particle GfxObjs they can create, and each animated object's pose reach; `LivingStaticsSystem.WarmParticleModels` resolves those GfxObjs' LOD chains and degrade distances.
- **Creation** (main thread, when the objects attach): `LivingStaticSet` creates one `LivingStatic` per object as `makeObject` does (§1.7): an `EffectObject` with the Setup's PhysicsScriptTable and its own seeded generator, the default script played at once, a `DefaultAnimationPlayer` (or a `MotionPlayer` for a default motion table). The particle meshes are requested (`ParticleMeshLibrary.Request`: prepared on workers, finished within 1 ms a frame).
- **Step** (`LivingStaticsSystem`, process priority 6: after the camera, the objects' LOD and the interiors' view): each source's statics run `LivingStatic.Animate` (retail `animate_static_object`, §1.7) round-robin until the frame's 1.5 ms is spent; one left over steps on a later frame with its longer elapsed time, as retail does at a lower frame rate. The clock is the engine's process time (Movie Maker's fixed frame time in recordings). Each emitter's draw range is retail's `ShouldDrawParticles` (§1.7) with `CYpt` measured to the emitter (or the far-cell distance outdoors) and the cell-drawn condition. The static's effect update keeps retail's order (`EffectObject.UpdateStatic`: scripts, particles, then hooks). Events the hooks raise (sounds, lights, NoDraw) are dropped (§4).
- **Draw**: one `ParticleRenderer` per source under its landblock root (landblock-local; the floating origin moves it), batches per particle GfxObj, retail's particle LOD and billboards (`ParticleDrawList`), culled by their bounds. A batch whose mesh is not prepared yet is skipped (`BuildMissingMeshes = false`), never built in a frame.
- **Warm-up** (start-up): every particle shader variant is compiled, and Holtburg's statics are stepped and drawn once on throw-away objects, so the first frames do not pay first-use JIT. That is 42 variants since the review fix: 21 object and 21 cell-object, from each particle sub-mesh's own addressing. Before the fix it was 26 object variants, from both addressing modes of every particle surface.

## 3. Verification

**Unit and conformance tests** (`tests/Lifestone.Client.Tests/Objects/`: 37 test methods after the wave-3 review fixes; the real-dat ones use `RequiresDats`):

- `ObjectRingsTests`:
  - `get_block_orient`'s sizes per ring and the resulting `side_cell_count`;
  - exactly 9 landblocks have objects.
- `DegradeFramesTests`, the geometric meaning of each camera-facing mode:
  - +Y along the heading with no roll, for 7 headings and pitches;
  - the kept axis stays; the previous axis takes the perpendicular component; the result is a proper rotation;
  - small directions leave the rotation unchanged.
- `InstanceBatchesTests`: 500 rounds of random moves against a reference model, with exactly the touched batches dirty.
- `LandblockObjectSetTests`:
  - Holtburg creates all 114 statics and 12 buildings at their stored frames, which lie on or above the exact terrain;
  - building versus object draw contexts;
  - part composition against `ModelAssembly`;
  - scenery equals the generator's output;
  - the `get_landcell` edge cases;
  - a world sweep of all 5,346 LandBlockInfos: counts, loadability, level tables and batch capacities.
- `LandblockObjectLodTests`:
  - Holtburg's building switches at 98, 114 and 440 m;
  - trees vanish at 250 × scale m;
  - every part of Glenden Wood follows the cell, object and part rules from a viewer in the middle;
  - a mode-5 lamp post turns with the viewer and dirties its batch only when the viewer moves.
- `PartShapesTests`:
  - shapes and instance scales of uniform and non-uniform part scales;
  - a baked shape under 20 random part frames and object scales gives retail's positions (R·S·v) and lit normals (normalize(R·S⁻¹·n)) within 1e-5, keeps zero normals zero, and differs from the unbaked MultiMesh normal;
  - in Arwic's 3×3 every part's meshes carry its shape, instance scale × shape is its scale, and the tree `0x020007A3` has the (0.208, 0.18, 0.35) trunk.
- `TexturePinSetTests`: on the real `ResourceCache`, a texture pinned at attach time survives later uploads over budget, one evicted since the prepare is reported missing, adopted uploads and duplicates keep one reference per pin, and releasing the pins lets the budget apply again.
- Wave 3:
  - `ObjectPlacementTests`: every part of 0xA9B3 (statics, buildings, scaled scenery) is `AcFrame.Combine(object, rest frame, scale)`, and an animation frame goes through the same rule.
  - `ObjectRingsCenterTests`: a dungeon (indoor) focus has no object grid; an outdoor one centres it on its landblock.
  - `LivingStaticsTests`: Holtburg's lamp posts and chimneys are living statics and their scripts' particle GfxObjs are known; `animate_static_object` stepping (nothing below 1/30 s, one step of the elapsed time, only the clock above 2 s); the lamp-post flame burns 2.15 m up the post while the viewer is near and is degraded out (nothing drawn) 300 m away; the birds of 0xA8B4 circle their origin 12.2 m out, turned by their SetOmega hook once per step; a budget steps the rest on a later call.
  - `CellObjectLodTests`: the Holtburg Redoubt's 576 cell statics are all created, each with its cell, posed by the shared rule; the 568 parts with degrade tables get state slots; a wall torch draws levels 0, 1 and 2 to 59, 65 and 90 m and nothing beyond (no far-cell rule in an EnvCell); unchanged views write nothing and cells out of view are not evaluated; a mode-5 level turns with the viewer.
  - `StageSiteTests` now poses Holtburg through `LandblockObjectSet` (the clearance still holds).
- Wave-3 review fixes:
  - `CellObjectLodTests.Living_cell_statics_name_the_cell_of_each_drawn_effect_object_and_skip_cells_out_of_view`: the Redoubt's 123 living cell statics. `DrawnCells` is parallel to `Drawn` and names each one's cell; a cell out of view draws nothing.
  - `ParticlePackingTests.A_tag_rides_with_every_draw_of_its_object_into_the_instance_custom_z`: the per-object tag (the cell's slot) reaches every draw and `INSTANCE_CUSTOM.z`, exact up to 16,383.
  - `ParticleSimulationTests.A_finished_emitter_whose_id_was_allocated_again_leaves_the_per_emitter_update` (Data tests): after the allocated ids wrap onto a live emitter's id, the per-emitter update drops the finished emitter and keeps the other. It used to spin forever. The surviving emitter keeps the id, so it can still be destroyed by id.

**Screenshots** (gitignored, `docs/screenshots/m1/objects-*.png`, 1280×720, the default noon lighting). Retaken after the review fixes on this branch, which keeps core's placeholder terrain (flat green and vertex colours); docs/terrain-renderer.md §5 has the TexMerge terrain.

| File | View (arguments) | What to look for |
|---|---|---|
| `objects-holtburg-overview.png` | the default start, 45 m up, south of Holtburg | town buildings textured; conifers in the foreground with alpha-tested needles |
| `objects-holtburg-town.png` | `--start=holtburg --walk` | thatched and tiled roofs, timber framing, stone bases (taken before the interiors merged, so doorways opened onto the ground) |
| `objects-holtburg-centre.png` | `--start=A9B4001B@84,60,0 --walk` | the town from its south edge: buildings, lifestone, the well |
| `objects-holtburg-hill.png` | `--start=A9B3001F@84,150,0 --walk` | the town from the hill south of it |
| `objects-building-close.png` | `--start=A9B4001D@84,116.5,66 --walk`, 15 m from `0x01000C1E` | a complete, textured building at level 0: roof tiles, stone, beams with rivets, barrels |
| `objects-trees-close.png` | `--start=A9B30015@70,110,0 --walk` | upright conifers at the right scale, bark trunks, alpha-tested needles |
| `objects-forest.png`, `objects-forest-walk.png` | `--start=A1A4`, and with `--walk` | Glenden Wood: dense conifers, the town's half-timbered buildings among them |
| `objects-yaraq.png`, `objects-yaraq-overview.png` | `--start=7D64001C@85.5,92,12 --walk`, `--start=7D64` | desert buildings with domes and arches, palms, cacti, the portal |
| `objects-arwic.png` | `--start=arwic --walk` | the town gate with its ARWIC sign, walls and towers |
| `objects-arwic-shaped-tree.png` | `--start=C6AA002B@138.6,52,53 --walk` | the tree `0x020007A3`, whose trunk parts are scaled non-uniformly (§2.3) |
| `objects-perf-{on,off}.png`, `objects-perf-town{,-off}.png` | the overview and town views with `--vsync=off` (uncapped then; today `--vsync=off --max-fps=0`), 4,000 frames | the measurement views below |

**Measurements** (RTX 5090, Vulkan Forward+, 1280×720, Debug build, 30 workers, load radius 8, placeholder terrain, 2026-10-10, after the review fixes). Every run waited until no other Godot process had run for 5 s; a sibling's run could still start during one (§4).

Holtburg views, vsync off and uncapped (today `--vsync=off --max-fps=0`), after 4,000 frames:

| View | FPS | Draw calls | Objects created (statics / buildings / scenery) | Parts drawn (of) | Batches shown (of) | Video memory (textures) | Godot static memory |
|---|---:|---:|---|---:|---:|---:|---:|
| Overview, objects off | 2,112 | 109 | — | — | — | 138 MB (66 MB) | 63 MB |
| Overview, objects on | 2,604 | 363 | 200 / 18 / 720 | 1,297 (2,175) | 250 (579) | 153 MB (77 MB) | 116 MB |
| Town (`--start=holtburg --walk`), objects off | 2,017 | 122 | — | — | — | 139 MB (66 MB) | 63 MB |
| Town, objects on | 2,498 | 349 | 223 / 16 / 383 | 808 (1,297), 2 camera-facing | 220 (582) | 155 MB (78 MB) | 115 MB |

- **Frame times** are about 0.4 ms in all four views; FPS differences at that rate are noise.
- **Memory.** Objects add about 15 MB of video memory and 53 MB of Godot static memory; 236–291 shared meshes take 1 MB of geometry.
- **Objects add about 240 draw calls**, roughly one per shown batch and surface. That is the retail 3×3 at full detail: only the 9 landblocks around the viewer carry objects.

Fly path Holtburg → Arwic at 150 m/s (5,921 m, 39.5 s; 680 landblocks attached; 127 object activations and 118 deactivations as the 3×3 follows the camera). "Objects attach step" is the slowest step of the objects layer over the whole run, start-up included (`max_attach_step_ms` in `[objects] summary`).

| Run | Avg | p99 | Max | > 33 ms | > 50 ms | Slowest objects attach step | GC during the flight (0/1/2) |
|---|---:|---:|---:|---:|---:|---:|---|
| objects on, vsync off | 0.68 ms | 1.12 ms | 11.9 ms | 0 | **0** | 2.5 ms | 9/7/6 |
| objects on, vsync off, again | 0.65 ms | 1.22 ms | 17.4 ms | 0 | **0** | 9.4 ms (a 9.1 ms collection inside) | 8/6/5 |
| objects on, vsync on | 4.17 ms | 4.66 ms | 22.9 ms | 0 | **0** | 17.2 ms (a 16.3 ms collection inside) | 6/5/3 |
| objects on, vsync on, again | 4.19 ms | 5.17 ms | 60.5 ms | 1 | 1 | 1.8 ms | 7/6/4 |
| objects on, vsync on, third run | 4.20 ms | 4.98 ms | 38.8 ms | 1 | **0** | 7.5 ms (a 6.9 ms collection inside) | 9/7/6 |
| objects on, vsync on, fourth run | 4.20 ms | 4.86 ms | 31.7 ms | 0 | **0** | 10.4 ms (a 9.9 ms collection inside) | 10/8/7 |
| objects off, vsync on | 4.24 ms | 5.07 ms | 30.0 ms | 0 | **0** | — | 0/0/0 |
| objects on, vsync off, 8 MB / 1 MB budgets | 0.60 ms | 1.07 ms | 30.8 ms | 0 | **0** | 2.6 ms | 11/9/8 |
| objects on, vsync off, 8 MB / 1 MB budgets, again | 0.58 ms | 0.89 ms | 16.8 ms | 0 | **0** | 10.7 ms (a 10.3 ms collection inside) | 12/11/9 |
| *before the fixes*, 8 MB / 1 MB budgets (review) | | | 53.2 / 71.6 ms | | FAIL | 50.0 ms (main-thread texture decodes) | |
| *before the fixes*, 8 MB / 1 MB budgets (this pass) | 0.78 ms | 1.43 ms | 28.9 ms | 0 | 0 | 25.4 ms (main-thread texture decode) | 14/12/10 |

- **No texture is decoded on the main thread any more.** Before the fixes, the tiny-budget flights logged `texture not resident at attach, decoding on the main thread` and attach steps of 25–67 ms. Now those landblocks log `textures evicted since the prepare; a worker decodes them before the attach` (2 textures in each tiny-budget flight here), and no flight logs a main-thread decode.
- **The one frame over 50 ms was not work.** That frame (60.5 ms) and the 26.3 ms frame before it drained 0.002 ms of main-thread work, attached nothing and ran no collection (`flypath-frames.csv`): a presentation stall outside Lifestone, like the vsync pacing frames the wave-2a review noted. Objects' own maxima in that run were 1.8 ms (attach step) and 1.1 ms (LOD update).
- **Slow attach steps are garbage collections.** Every objects attach step over 4 ms in these runs had a collection inside it (6.5–16.3 ms, ephemeral, promoting 7–11 MB); the steps themselves stay at the 0.75 ms slice plus one unit.
- **Re-attaches** (a temporary round-trip version of the fly path, not committed: Holtburg → 0xABB4 → Holtburg at 40 m/s): 3 landblocks came back into the 3×3 and attached from their kept prepared data. With 8 MB / 1 MB budgets 88 of their textures had been evicted and were decoded again on workers first; max frame 10.9 ms (12.0 ms with the default budgets), no main-thread decode, and the 3×3 ended with the same 223 / 16 / 383 objects it started with.
- **Fewer prepares.** A landblock that leaves the 3×3 is no longer prepared again at once: the system starts 200 prepares per flight, and the shared geometry cache serves 16,767 lookups instead of 21,839 before the fix (one landblock's worth for each of the 118 deactivations).
- **Work per frame is small.** The LOD update for the 3×3 averages well under 0.5 ms (its maxima of 1–11 ms had a collection inside); batch rewrites cost microseconds.

### 3.1 Wave 3: living statics and EnvCell statics

**Screenshots** (gitignored, `docs/screenshots/m1/fix-objects/`, 1280×720, Vulkan, the TexMerge terrain). `before-*` were taken on master `110ad6e`, `after-*` on this branch, same arguments, 600 frames after streaming went idle.

| Pair | View (arguments) | Before | After |
|---|---|---|---|
| `before-torch-close` / `after-torch-close` (crops `crop-before-torch`, `crop-wip1-torch`) | `--start=holtburg --interior-eye=A9B40021@102.5,13.5,95.8 --interior-look=45,-5` | a bare lamp post | the flame `0x3200002F` and glow burn on top |
| `before-chimney2` / `after-chimney2` | `--start=holtburg --interior-eye=A9B30028@110,190,101 --interior-look=70,8` | a cold chimney | smoke puffs `0x3200020D` rise |
| `before-redoubt-torch` / `after-redoubt-torch` (crops `crop-before-redoubt-torch`, `crop-wip2-redoubt-torch`) | `--start=0163 --interior-eye=01630110@8,-50,-16.4 --interior-look=90,5` | the handle of a wall torch | flame and the soft glow `0x32000141` around it |
| `before-lanterns-night` / `after-lanterns-night` | `--start=holtburg --time-of-day=0.08 --interior-eye=A9B40021@116,0,96.8 --interior-look=50,3` | | the chimney smokes against the night sky |
| `before-town-network` / `after-town-network` | `--start=town-network` | a dry fountain, unlit torches; the HUD shows an objects ring around 0x0006 in the dungeon | the fountain sprays, the torches burn; no landscape objects (`ring around -`) |
| `before-windmill2` / `after-windmill2` + `seq-windmill` | `--start=2438 --interior-eye=2438001E@88,132,14 --interior-look=0,0` | the sails at rest | the sails turn (`0x020003E5`, default animation `0x0300061B`) |
| `before-flags` / `after-flags` (crop `crop-flags-before-after`, before above) | `--start=8469 --interior-eye=84690009@40,5,29 --interior-look=90,0` | the pennants `0x020007BB` at their rest pose | the pennants wave (default animation `0x030007A7`; the still catches another frame) |
| `before-academy-lod` / `after-academy-lod` (crops `crop-before-academy`, `crop-after-academy`) | `--start=8603 --interior-eye=860301AD@12,-28,90 --interior-look=219,-55 --interior-show-all` | every cell static at level 0, 90+ m away | far plants at their lower levels, small debris gone (retail LOD, §1.8) |
| `sv-before-2.90` / `sv-after-2.90` | the spell viewer, `--spell=85 --no-ui --shots=2.9` | statics and buildings only | scenery too; the lamp post right of the drudge burns |

The academy pair is in landblock 0x8603, where M1's `training-academy` start was. Since M2 that key starts at ACE's spawn cell, 0x860201AD in landblock 0x8602 (interiors.md §6), so the recipe names `--start=8603`; its `--interior-eye` cell is unchanged. The "Training Academy" rows of §1.7 and of the measurements below are also of landblock 0x8603.

Frame sequences (Movie Maker, fixed 30 fps; GIF and an 8-frame sheet each): `seq-torch` (the lamp-post flame flickering), `seq-chimney` (smoke rising), `seq-windmill` (the sails turning).

**Measurements** (RTX 5090, Vulkan Forward+, 1280×720, Debug build, 30 workers, load radius 8, vsync off and uncapped (today `--vsync=off --max-fps=0`), 6,000 frames after streaming went idle, the steady state from frame 600; sibling agents' Godot runs shared the machine).

| View | Living statics (sources) | Emitters (out of range) | Particles drawn, avg / max | Step, avg ms | Draw, avg / max ms | Draw calls on / off |
|---|---:|---:|---:|---:|---:|---:|
| Holtburg overview (default start) | 127 (9) | 188 | 12.6 / 25 | 0.025 | 0.020 / 0.30 | 653 / 647 |
| Holtburg town (`--start=holtburg --walk`) | 118 (22) | 160 | 38.9 / 63 | 0.025 | 0.042 / 0.31 | 632 / 624 |
| Arwic (`--start=arwic --walk`) | 223 | 345 | 21.1 / 28 | 0.041 | 0.059 / 0.30 | 764 / 758 |
| Town Network | 134 (1) | 197 (177) | 168 / 195 | 0.022 | 0.138 / 0.56 | 165 / 153 |
| Training Academy | 239 | 250 | 36.9 / 41 | 0.029 | 0.041 / 0.36 | 109 / 103 |
| Holtburg Redoubt | 123 (1) | 205 | 67.7 / 88 | 0.019 | 0.068 / 0.34 | 87 / 81 |

- **Cost.** The living statics' own main-thread work is the step and draw columns: 0.04-0.2 ms a frame. Every step over 4 ms in these runs and the flights below had a garbage collection inside it (11-20 ms, logged as `slow living statics step ... gc_pause_ms`); without it the slowest step was 1.5-1.9 ms. The whole-frame on/off averages (0.80-0.98 ms with them, 0.80-1.57 ms without) are within the machine's run-to-run noise: three of the six "off" runs were the slower ones.
- **Particles are few on screen** because only emitters within their degrade distance (64-128 m) simulate and draw, as in retail: Holtburg's 188 emitters show 13-63 particles; the Town Network's fountain hall 168-195.
- **Draw calls** grow by the particle batches (one per particle GfxObj and source, two sub-meshes each) and, in cells, by LOD levels whose GfxObj has its own surface (drawn as collapsed triangles when hidden): +6 to +16 in normal views; the Academy overview with every cell shown went from 851 to 964 draw calls and from 26k to 42k primitives (every level's triangles are submitted).
- **Start-up**: compiling the 26 particle variants and stepping a throw-away Holtburg set adds 230-250 ms (`[objects] living statics ready`).
- **Fly path** Holtburg → Arwic at 150 m/s (680 landblocks; up to 25 sources and 631 living statics on the way; step 0.10-0.16 ms and draw 0.016 ms a frame on average):

| Run | Avg | p99 | Max | > 33 ms | > 50 ms |
|---|---:|---:|---:|---:|---:|
| living statics on, vsync on | 4.26 ms | 8.95 ms | 32.0 ms | 0 | **0** |
| living statics off, vsync on | 4.42 ms | 11.6 ms | 90.4 ms | 6 | 3 (frames with no work and no collection: presentation stalls) |
| living statics on, vsync off | 1.51 ms | 3.55 ms | 45.9 ms | 3 | **0** |

- **The spell viewer** attaches Holtburg's 622 objects (statics, buildings, scenery) by frame 116; slowest attach step 2.6 ms, slowest loading frame 7.5 ms (a particle mesh), as with the smoke layer (1.1-4.2 ms, 7.3-10.0 ms).
- **Regression checks**: `--interior-engine-checks` gave the same 11 holds and the same 4 failures as master at the time (`interior-engine-checks-fail-at-head`, the sky overwriting the checks' ambient; the cell-object quads draw); since the M1 integration (infra's check mode) all 15 hold. The interior tour renders all 8 stops.

**Review fixes (`m1/fix-living`).** Screenshots are in `docs/screenshots/m1/fix-living/` (gitignored, 1280×720, Vulkan, 600 frames after streaming went idle). `before-*` were taken on `5b41731` (this branch before the fixes), `after-*` with the fixes, same arguments.

| Pair | View (arguments) | Before | After |
|---|---|---|---|
| `before-tower-torch-landscape-on` / `after-tower-torch-landscape-on` (crop `crop-tower-torch-before-after`; `seq-tower-torch`, frames 600/603/606/609) | `--start=holtburg --interior-eye=AAB30107@106,60,113.5 --interior-look=123,0`: inside Holtburg's tower (seen_outside) with the doorway on screen, HUD "landscape on" | standing torch `0x020005D8` dark | flame and glow burn, flickering |
| `before-shop-lamp-from-outside` / `after-shop-lamp-from-outside` (crop `crop-shop-lamp-before-after`) | `--start=holtburg --interior-eye=A9B4001A@96.7,36,94.9 --interior-look=104,30`: outdoors, through the west door of `0x01000C17` at the chandelier `0x02000081` in cell `0xA9B40166` | candles unlit | candle flames |
| `before-tower-torch-landscape-off` / `after-tower-torch-landscape-off` (crop `crop-tower-torch-landscape-off-before-after`) | same eye, `--interior-look=85,0`, HUD "landscape off" | burns | burns (unchanged) |
| `after-arwic-hall`, `after-redoubt-torch`, `after-torch-close` | `--start=arwic --interior-eye=C6A9016E@108,84,43.6 --interior-look=270,8`; the Redoubt and lamp-post views of the table above | | unchanged: the hall's torches, the wall torch's flame and glow (sun off now, the same picture), the outdoor lamp post |
| `sv-before-2.90` / `sv-after-2.90` (crops `crop-sv-lamp-before-after`, `crop-sv-bolt-before-after`) | the spell viewer, `--spell=85 --no-ui --shots=2.9` (before: the review's capture of `5b41731`) | | identical: the bolt and the lamp post |

Measurements, before and after the fix: vsync off and uncapped (today `--vsync=off --max-fps=0`), 4,000 frames after streaming went idle, steady state from frame 600, same session. Sibling agents' Godot runs shared the machine.

| View | Frame avg / p99 / max, before | after | Particle draw, avg ms | Particles drawn, avg |
|---|---|---|---:|---:|
| Tower, landscape on (above) | 0.598 / 1.667 / 15.0 ms | 0.580 / 1.667 / 10.2 ms | 0.043 → 0.041 | 35.2 → 34.1 |
| Holtburg Redoubt torch | 0.450 / 0.804 / 1.39 ms | 0.455 / 0.877 / 1.39 ms | 0.018 → 0.019 | 8.8 → 9.0 |
| Holtburg town (`--walk`) | 0.785 / 1.389 / 18.7 ms | 0.800 / 1.389 / 17.8 ms (first run 0.813 / 2.083 / 14.3) | 0.032 → 0.032 | 27.0 → 26.9 |

- **Per frame: no change.** The slot lookup is one dictionary read per drawn cell static. Draw calls are unchanged (332, 59, 632).
- **Particle meshes.** The tower view builds 12 instead of 10: the flame and glow in their cell-object form.
- **Start-up.** `[objects] living statics ready` went from 230–237 ms (26 variants) to 350–368 ms (42 variants), once, before streaming.
- **Engine checks.** `--interior-engine-checks`: the same 11 holds and 4 known failures on the branch; all 15 hold on the integrated build (check mode).

## 4. Deviations, open points and requests

- **Objects in several cells** take their LOD distance from the cell of their origin (§1.4, `TODO(objects)`).
- **Living statics** (§1.7) run retail's rules; what they do not do yet, each named here and in the code:
  - **Hooks executed but not drawn.** A sweep of every LandBlockInfo static and building plus six dungeons' cell statics (default scripts with their CallPES chains, and default animations) finds two kinds; both are logged once per Setup at run time (`TODO(objects): living static uses hooks M1 does not draw yet`):
    - **TextureVelocity** (3 Setups, 67 instances, all in landblock 0x2C30: `0x02001BF0`, `0x02001BEF`, `0x02001BF3`, luminous surface `0x08001903`): a scrolling surface needs a UV-offset parameter on the shared surface shaders (render contract; the sky's `SkyTextureScroll` has one for its own shaders).
    - **TransparentPart** (1 Setup, `0x020011C3`, 25 instances): its default animation fades its one part in and out; drawing that needs a per-instance part translucency on the object shaders (the particle variant has one, `ParticleShaders`).
    No static uses Scale, Luminous, Diffuse, NoDraw, SetLight or ReplaceObject hooks.
  - **Sounds** (Sound, SoundTable, SoundTweaked hooks: about 800 instances) are dropped: M1 has no audio. The Setups' own lights are not lit (point lights are a later milestone, render contract §2.1).
  - **Default motion tables** (6 statics in the sampled dungeons) take the table's initial state (`MotionTableManager.InitializeState`, `MotionPlayer`); `CMotionInterp::enter_default_state` also queues Ready and calls `LeaveGround`, assumed not to change what a static shows (`TODO(objects)` in `LivingStatic`).
  - **`ShouldDrawParticles`'s cell test** calls a `CObjCell` virtual whose name the decompile lost; it is taken to be "the cell is drawn" (outdoors: the landblock is active; indoors: the cell is in the portal view), which is also what keeps `CYpt` current in retail (`TODO(objects)` in `LivingStaticSet`).
  - **The particle object's `CYpt`** is measured to the emitter's position; the far-cell rule uses the static's own land cell, not the cells its particles may cross.
  - **Random numbers**: each static has its own generator seeded from its landblock and index (retail seeds one global generator with the time); emitters update in creation order (spell-visuals.md §7).
  - **Budget**: statics step round-robin within 1.5 ms a frame; one that waits steps later with the longer elapsed time, as retail does at a lower frame rate (never needed in the measured views: the step averages 0.02-0.04 ms).
- **EnvCell statics** (§2.6):
  - **Lighting of moved and turned levels.** A part's per-vertex light (the cell's static lights, docs/interiors.md §4.4) is computed once, at its rest frame. A camera-facing level or an animated part is drawn turned or moved with the light of its rest orientation; retail's hardware lights lit the drawn normals. Camera-facing levels are the far, small levels (cards), and animated cell statics are rare (3 in the Town Network).
  - **Hidden levels still cost vertices**: every level of a LOD part is in the cell's mesh and the vertex shader drops the ones not drawn (about +30% object vertices in cells), against one draw call per cell and material as before.
  - **Lighting of their particles.** Their particles are lit like the cell pass: luminosity + world ambient, no sun (§2.6). Whether retail also adds the cell's hardware point lights (`Render::minimize_object_lighting`) to particle parts is not established (`TODO(objects)` in `ParticleShaders`). It rarely shows. Over the cell statics of 8 landblocks (the Redoubt, the Town Network, Holtburg 0xA9B4 and 0xAAB3, Arwic, the Mines of Despair, the Glenden Wood Dungeon, the Academy) there are 1,415 (static, particle GfxObj) pairs. In only 8 of them does the GfxObj have a non-luminous surface: `0x010016C0` ×6, `0x01001960` and `0x01000FB9`. Every other sprite is luminous and looks the same under either rule.
  - **A cell without a state slot** (the interiors' table full, or a cell with no geometry) draws its statics' particles without front depth or doorway clipping, as it draws its own geometry.
  - **Hard-edged glows on side-wall torches** (M1 final review, golden view 18, the Holtburg Redoubt entry): seen obliquely, the glow sprite of a wall torch ends in a straight edge across the wall (about 70 levels at one column, the bricks continuing across it), in byte and linear blend space alike; seen head-on the same glow is soft (`final/living/redoubt-torch-600.png`). In a dungeon no cone clips a cell's particles: the doorway rows, the only discard in the cell-object variants (`SurfaceLightingCode`), are given only to cells seen through a building's doorway from outdoors, and with no landscape in view no cell is moved to the front either (`InteriorController`, docs/interiors.md §3.4), so torch, glow and wall share the plain depth rules. The likely cause is therefore the depth test: a particle GfxObj is drawn turned to the viewer's heading (degrade mode 2, `DrawFrameRules.DrawOrientation`: retail `CPhysicsPart::calc_draw_frame` → `SetVectorHeading`), so when the view runs along a wall its torch's glow quad stands across the wall and the part inside it fails the depth test (the edge is the line where quad and wall meet: straight, and in the `--start=holtburg-redoubt` shot at pitch 0 tilted by about 9 px over 165 rows, so the quad is not quite vertical); head-on, the quad lies parallel to the wall and stays whole. Retail draws a cell's objects, particles included, with the depth test after the same reset (`PView::DrawCells` → `DrawObjCell`) and had no soft particles, so it would show the same edge; not established without a retail capture of the view (on the owner's comparison list, docs/milestones/m1-explore-dereth.md).
- **Setup lights** (608 Setups) are not modelled; point lights are a later milestone (render contract §2.1).
- **Transparent batches** are sorted per batch by Godot, not per instance. Retail drew its alpha lists unsorted (`AddMeshToAlphaList`), so neither is exact.
- **Measurement noise.**
  - Five tracks share the development machine. Other agents' Godot runs show up as GPU or CPU contention, and vsync presentation can stall a frame with no work in it (the 60.5 ms frame above).
  - The runs above waited until no other Godot process had run for 5 s; a sibling could still start during a 40 s flight.
  - The wave-3 performance pass should re-measure on an idle machine.
- **Garbage collection.**
  - Without objects a flight runs no collection at all; with objects, 6–12 ephemeral collections. Every new landblock allocates its object set, scenery and prepared surfaces on the workers, and core's texture decode (`TextureCache.Prepare`) allocates large managed buffers for every texture a new landblock brings.
  - In this Debug build an ephemeral collection that promotes 7–11 MB pauses every thread for 6–16 ms. Such a pause shows as a slow attach step or LOD update when it lands in one.
  - Already cut: parts are structs with one flat level table, the builder sizes its arrays once, converted geometry is shared through `PreparedGeometryCache`, the per-frame paths allocate nothing, and (this pass) a landblock that leaves the 3×3 keeps its prepared data and instance buffers instead of being prepared again.
  - Further cuts (pooled decode buffers in the texture path, prefetching one ring less) are for the wave-3 pass. So is the process-wide question of GC settings and Release builds, which is core's.
  - *Wave-3 performance pass:* texture decodes now use pooled buffers and are shared between concurrent prepares (`TextureCache.PrepareShared`, docs/textures.md §9); `LandblockAttachTextures.Decode` goes through the same shared path. Server GC is the default for `tools\godot.ps1` launches, and the default worker count is 8. Integrated measurements (all layers) are in game-core.md §7.3; the tables above predate integration.
- **Terrain level of detail (note for the terrain owner).** The same retail chain (§1.1) also draws ring-2 landblocks with 4×4 cells, rings 3–4 with 2×2 and farther ones with 1×1 (`CLandBlockStruct::generate`, `TransAdjust` for the seams). docs/terrain-renderer.md §8 says Dereth does not use reduced-detail blocks; the decompile says every block beyond ring 1 is one.

## 5. Merge notes for the lead

**M1 wave 3 (`m1/fix-objects`):**
- **`StaticsSmokeLayer` is deleted**, with the spell viewer moved onto the objects path (§2.5). `Main.cs` loses its smoke block (one comment line marks the place); `GameOptions.SmokeStatics` is gone, and so is `SmokeStaticsRequested` (since `m1/fix-living`: `GameOptions.Parse` adds the retirement message to `Errors`, logged at start-up whatever `--objects` says, and an automated run with it quits with exit code 1; the objects layer no longer warns). A sibling branch that still names `StaticsSmokeLayer` or `options.SmokeStatics` will not compile after the merge; none did at `110ad6e`.
- **Shared files, minimal and commented:** `project.godot` (one `[shader_globals]` entry, `ac_object_lod`), `GameOptions.cs` (`--static-effects`, the smoke option retired), `Main.cs` (the smoke block removed), `Render/SurfaceLightingCode.cs` (the cell-object rule splices in `CellObjectLodShader`: two `if` blocks and the global's declaration).
- **Interiors files** (coordinated edits): `InteriorsLayer.cs` (cell statics through `PreparedCellObjects`; the registrations with `CellObjectsSystem`; the old `AddModel`/`PartLights` removed), `InteriorMeshes.cs` (CUSTOM2 LOD tags on object surfaces, bounds spheres, the slot base at attach), `InteriorsSetup.cs` (the warm-up mesh gets the object surfaces' vertex format).
- **Effects files** (two additions, no behaviour change for the spell viewer): `EffectObject.UpdateStatic` (retail `animate_static_object` order) and a `ParticleManager.UpdateParticles` overload with a per-emitter draw test.
- **Spell viewer**: `SpellViewer.cs` uses `StaticObjectsView` and installs `LivingStaticsSystem` with its own particle library; `StaticClearance` poses through `LandblockObjectSet`.
- No `.uid` files were added (the lead regenerates them).

**M1 wave 3 review fixes (`m1/fix-living`, on top of `m1/fix-objects`):**
- **Interiors files** (two lines each): `InteriorController.SlotOf(cellId)` (read-only), and `InteriorsLayer.RegisterObjects` passes it to `CellObjectsSystem.Register`.
- **Effects files**: `ParticleDraw.Tag` / `ParticleDrawList.Add(..., tag)` and `InstanceBuffer.ParticleCustom(..., tag)` (`INSTANCE_CUSTOM.z`), optional and 0 by default, so the spell viewer is unchanged. In `ParticleManager`, a finished emitter is removed by index; its id is dropped only if the id still maps to it.
- **Particles**: `ParticleShaders` has a cell-object form, and `ParticleMeshLibrary` keeps meshes per lighting rule. `WarmUp` now compiles the variants of each particle sub-mesh's own addressing (21 instead of 26 for the spell viewer).
- **`GameOptions.cs`**: one line, the `--smoke-statics` retirement warning.
- **Stale `--smoke-statics` command lines** were swept at the M1 integration: the measurement records taken with the smoke layer (`docs/interiors.md` §7, `docs/sky.md` §7.3, `docs/game-core.md` §7, `docs/terrain-renderer.md`) now say so and give `--objects=statics,buildings` as today's nearest equivalent. In an automated run the retired flag is a bad argument (exit 1).

**Wave 2 (kept for the record):**
- **`Main.cs`.** The objects block sits after the terrain registration that `m1/terrain-integration` rewrites; it does not touch that line.
- **Checked on 2026-10-10** against the sibling branches as they are now:
  - `git merge-tree` of `m1/objects` with each of `m1/spellviewer`, `m1/interiors`, `m1/sky` and `m1/terrain-integration`: no conflict.
  - `m1/objects` + `m1/spellviewer`, built from the merged tree: 0 warnings, 0 errors.
  - `m1/objects` + `m1/terrain-integration` + `m1/interiors` + `m1/sky`, merged in that order (the siblings' docs conflict below taken from one side for the check): builds with 0 warnings, and all 1,011 tests pass.
  - Two problems between siblings, not involving this branch: `m1/interiors` and `m1/sky` conflict in `docs/render-contract.md` (the same hunk with or without `m1/objects`), and `m1/terrain-integration` + `m1/spellviewer` do not compile together (`SpellViewer.cs` lines 436–437 call `TerrainRenderer.MaxMipLevel` and `ApplySky` on an instance; terrain-integration made them static).
- **Core files** (wave 2). The branch added lines to `GameOptions.cs` (objects options, in a commented block), `Main.cs` (the install block) and the core docs' option tables. The earlier request to change the surface shader's normal matrix is withdrawn (§2.3 bakes the shapes instead).
