# Third-party sources: objects track (`src/Lifestone.Client/Objects/`, `src/Lifestone.Game/scripts/World/Objects/`)

No third-party code was copied or ported into this track, so no file carries a port header. Every retail behaviour is re-expressed and cited by function name (and `acclient.c` line) in the code comments and in [objects.md](../objects.md). The table shows what each source was used for.

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-10 | Behaviour of the functions listed below. Nothing copied. |
| Mag-nus/acclient-AI-RE, 11.6096 (the user's client, no names) | Turbine/WB IP; reference only | fetched 2026-10-10 | `sub_5059F0`: confirms `LScape::get_block_orient`'s ring thresholds are unchanged in the user's client. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`) | `Source/ACE.Server/Physics/Common/LandblockStruct.cs` `ConstructPolygons`: the land cell position is the cell centre (`originX/Y × HalfSquareLength`), cross-checked against the retail decompile. `Landblock.init_static_objs`: the same `adjust_to_outside` + `get_landcell` creation test. |
| ACEmulator/ACViewer | GPL-3.0 | @c5c54fb (`vendor-eval/ACViewer`) | `ACViewer/Data/Locations.txt` "Town" entries: the Yaraq (`0x7D64000D [31.9 104.6 11.9]`) and Glenden Wood positions used for screenshots. These are facts (positions). |
| Godot 4.7.2 (engine behaviour) | MIT | 4.7.2-stable | `MultiMesh` Transform3D buffer layout (12 floats per instance: basis row 0 and origin.x, row 1 and origin.y, row 2 and origin.z), `VisibleInstanceCount`, `CustomAabb`. The spatial shader's `MODEL_MATRIX`/`MODEL_NORMAL_MATRIX` include the MultiMesh instance transform; the normal matrix is multiplied by the instance's 3×3, not its inverse transpose, which is why non-uniform part scales are baked into the meshes (`PartShapes`, objects.md §2.3). A `MultiMesh` unbound from its node and disposed drops its mesh reference at once (checked at every start-up: `multimesh_release_frees_mesh_ref`). Not code. The layout is checked visually: the batched buildings coincide with the core smoke test's per-node buildings. |
| Microsoft Direct3D 9 documentation (fixed-function vertex processing) | documentation | — | Normals are transformed by the inverse transpose of the world-view matrix; `D3DRS_NORMALIZENORMALS` renormalises them after the transform. With retail's per-axis part scale in the world matrix this gives `normalize(R·S⁻¹·n)` (`PartShapes`). A fact about the API, not code. |

## Retail functions re-expressed (11.4186 `acclient.c` line numbers)

- **Which landblocks have objects:**
  - `LScape::update_block` (307786) and `LScape::get_block_orient` (306681)
  - `CLandBlockStruct::generate` (354762): `side_cell_count = 8 / poly_size`
  - `LScape::grab_visible_cells` (306571)
  - `CLandBlock::init_buildings` (352114) and `CLandBlock::init_static_objs` (352787): both run only at `side_cell_count == 8`
  - `CLandBlock::notify_change_size` (352430): destroys static objects and buildings
  - `CellManager::UpdateLoadPoint` (146439): the grid centre is the player's outdoor cell
- **Creation:**
  - `CLandBlock::get_landcell` (352098): the cell must be one of this block's
  - `LandDefs::adjust_to_outside` (via `AcCoordinates.AdjustToOutside`, core)
  - `CPhysicsObj::add_obj_to_cell` (322897): the stored frame becomes the object's frame
  - `CPhysicsObj::calc_cross_cells_static` (322405): the shadow cells, cited for the multi-cell TODO
- **LOD:**
  - `RenderDeviceD3D::DrawBlock` (458961), which calls `RenderDeviceD3D::UpdateObjCell` (458101) for every cell holding objects
  - `CLandCell::GetViewerDistance` (355104): horizontal distance to `CLandCell::pos`
  - `CLandBlockStruct::ConstructPolygons` (354001; the cell centre at 354094–354098, `flt_844C8C = 24.0 × 0.5` at 787086)
  - `CPhysicsObj::UpdateViewerDistance()` (317913) and `CPhysicsObj::UpdateViewerDistance(CYpt, heading)` (316319)
  - `CPartArray::UpdateViewerDistance` (325547, 325569)
  - `CPhysicsPart::UpdateViewerDistance()` (315098) and `CPhysicsPart::UpdateViewerDistance(CYpt, heading)` (315173)
  - `Position::get_offset` (311691, 314677)
  - `GfxObjDegradeInfo::get_degrade`, `Render::object_distance_2dsq` and the bias defaults: via the models track's `DegradeSelector`
- **Camera-facing levels:**
  - `CPhysicsPart::calc_draw_frame` (315066)
  - `Frame::set_vector_heading` (357668) and `Frame::euler_set_rotate` (357589, order 0)
  - `Frame::rotate_around_axis_to_vector` (357520)
- **Draw rules** (via the models track): `D3DPolyRender::DrawMesh` (`SurfaceDrawRules`) and `CPhysicsPart::SetGfxObjArray` (level-0 surfaces).
- **Normals of scaled parts:** `PrimD3DRender::CalcObjectMatrixInternal` (453636: the model-to-world matrix carries the per-axis object scale), `RenderDeviceD3D::SetModelToWorldMatrix` (460718: `SetTransform(D3DTS_WORLD)`), `RenderDeviceD3D::SetDefaultD3DStates` (460762; render state 143, `D3DRS_NORMALIZENORMALS`, set to TRUE at 460815; the same call is in the user's 11.6096).

- **Living statics and EnvCell statics (M1 wave 3):**
  - `CPhysicsObj::makeObject` (320829), `CPhysicsObj::InitObjectBegin` (317273), `CBuildingObj::makeBuilding` (719153)
  - `CPartArray::InitDefaults` (326158) and `CPhysicsObj::InitDefaults` (320854): default animation, script, motion table, `HAS_DEFAULT_ANIM`/`HAS_DEFAULT_SCRIPT`, `CPhysics::AddStaticAnimatingObject` (311440)
  - `CPhysicsObj::SetMotionTableID` (319693), `CPhysicsObj::MakeMovementManager` (317454), `MovementManager::EnterDefaultState` (339250), `CMotionInterp::enter_default_state` (344560)
  - `CPhysics::UseTime` (311335) and `CPhysicsObj::animate_static_object` (321150): the static step and its order; `MIN_QUANTUM` = 1/30 (784229)
  - `SetOmegaHook::Execute` (342548), `CPhysicsObj::set_omega` (316613), `Frame::grotate` (357422)
  - `ParticleEmitter::UpdateParticles` (331097), `CPhysicsObj::ShouldDrawParticles` (317184), `CPhysicsPart::GetMaxDegradeDistance` (314372), `GfxObjDegradeInfo::get_max_degrade_distance` (332193)
  - `CEnvCell::init_static_objects` (347955), `PView::DrawCells` (461450), `RenderDeviceD3D::DrawObjCellForDummies`, `CObjCell::GetViewerDistance` (346903)
  - `CellManager::ChangePosition` (146646) → `LScape::release_all` (306381) for cells that are not seen_outside
- **ACE `PhysicsObj.animate_static_object`** (`Source/ACE.Server/Physics/PhysicsObj.cs`, AGPL-3.0, @95ef426d) was read as a cross-check of the step's order; its `ShouldDrawParticles` returns true for every object that is not an examination object (inverted from retail) and its static-animation loop is commented out, so retail is followed.

## Own implementations (no source copied)

- **EnvCell statics' LOD states** (`CellObjectLod`, `CellObjectsSystem`, `CellObjectLodShader`): every LOD level of a part stays merged in its cell's mesh, tagged per vertex; a global texture says which level each part draws and how it is turned, and the vertex shader drops the rest. Own design to give merged, per-vertex-lit cell statics retail's LOD without unmerging them.
- **Living statics' budget and per-landblock particle renderers** (`LivingStaticSet`, `LivingStaticsSystem`): own design over the effects track's `EffectObject` and the spell viewer's `ParticleRenderer`.

- **Instance batching** (`InstanceBatches`, `LandblockObjectBatches`): one `MultiMesh` per (landblock, mesh), with swap-remove membership and a dirty set. Own design for Godot; retail drew each part with its own `DrawMesh` call.
- **Budgeted attach units** (one texture upload, or one mesh and its batch, per unit), the warm-up, `PreparedGeometryCache` and the prefetch ring: own design on top of the core's streaming and render contracts.
- **Attach-time texture pins** (`TexturePinSet`, `LandblockAttachTextures`): pin the resident textures when an attach is decided and decode the evicted ones on a worker first, so no attach step decodes. Own design.
- **Kept prepared data** for landblocks that leave the 3×3 but stay in the prefetch ring (object set, transforms, LOD state, instance buffers). Retail destroys the objects (`notify_change_size`), and so does Lifestone; only Lifestone's own CPU-side preparation is kept.
- **Baked part shapes** (`PartShapes`): the non-uniform part of a part's scale goes into the mesh, the uniform part into the instance transform. Own design to reproduce D3D's normals under Godot's MultiMesh.

## Deliberate differences from references

- **Multi-cell objects** take their LOD distance from the cell of their origin, not from the last of their shadow cells in the landblock's draw order (objects.md §1.4, `TODO(objects)`).
