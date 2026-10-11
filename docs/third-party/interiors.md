# Third-party sources: interiors track

Track: `src/Lifestone.Game/scripts/World/Interiors/`, `src/Lifestone.Client/Interiors/`, `tests/Lifestone.Client.Tests/Interiors/` (plus the small marked extensions listed in [interiors.md](../interiors.md) §11).

**No file was ported.** Behaviour is re-expressed from the sources below, and each algorithm or constant cites its source in a code comment. The retail decompile is cited by function name and `acclient.c` line only; none of its code is in the repo.

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) and its `06-rendering.md` | Turbine/WB IP; reference only | fetched 2026-10-10 | Behaviour of the functions listed below. Nothing copied. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`) | Cell transitions (cross-check of the retail functions) and two positions; see below. |
| ACEmulator/ACViewer | GPL-3.0 | @c5c54fb (`vendor-eval/ACViewer`) | `ACViewer/Data/Locations.txt`: the drop points of five dungeons (facts, cited in `InteriorLocations`). |
| Godot 4.7.2 class reference | MIT | 4.7.2-stable | Engine behaviour relied on: camera cull masks and per-camera environments, global `sampler2D` uniforms read with `texelFetch` in vertex and fragment shaders, `flat` integer varyings, `POSITION` and `DEPTH` writes, the `CUSTOM1` RGBA-float vertex attribute. Not code. Checked by `--interior-engine-checks` (interiors.md §9): a missing CUSTOM1 reads 0, CUSTOM1 arrives unchanged, the depth buffer is reversed-Z (near = 1), the projection's `P[1][1]` is negative (the RenderingDevice flips Y; the seam band uses its absolute value), and instance uniforms reserve one block of 16 vec4 per instance of the global buffer (65,536 vec4 by default, so about 4,096 instances, hidden ones included) while the cell variants take none. Also measured there: the RenderingServer allocates the blocks of newly dirty instances newest first. |

## Retail functions re-expressed (11.4186 `acclient.c` line numbers)

- **Which cells are loaded:** `CLandBlock::init_buildings` (352114), `CBuildingObj::add_to_stablist` (719041), `CBldPortal::add_to_stablist` (362423), `CLandBlock::grab_visible_cells` (351601), `CEnvCell::grab_visible` (350001), and the `LScape` loop that calls `init_buildings` and `grab_visible_cells` for every landblock of the grid (306602-306603).
- **Render path and visibility:**
  - `SmartBox::RenderNormalMode` (144867): outdoors `LScape::draw`, indoors `DrawInside`; the sun is turned on for `LScape::draw` only.
  - `PView::DrawInside` (462467), `PView::DrawCells` (461450): landscape through outside views, Z clear, outside portal polygons into depth, cells, objects; `Render::useSunlightSet(0)` for cells.
  - `PView::DrawPortal` (462565), `PView::ConstructView(CBldPortal*, …)` (462507), `RenderDeviceD3D::DrawMeshInternal` (456960) with `BSPTREE::build_draw_portals_only` passes 1 and 2.
  - `D3DPolyRender::DrawPortalPolyInternal` (453882) with `maxZ1 = 7` / `maxZ2 = 6` (45771 / 45770): depth reset to far inside doorways, actual depth for outside portals.
  - `RenderDeviceD3D::Clear` (457563): the flag mapping (4 = Z).
  - `SceneTool::BeginScene` (122402): clear to black every frame.
- **Viewer cell (cross-checked with ACE `Physics/Common/ObjCell.cs`, `EnvCell.cs`, `BuildingObj.cs`):** `CObjCell::find_cell_list` (346961), `CEnvCell::find_transit_cells` (348250), `CEnvCell::check_building_transit` (348110), `CBuildingObj::find_building_transit_cells` (719068), `CEnvCell::find_visible_child_cell` (349698).
- **Lighting:**
  - `CellManager::ChangePosition` (146646): world ambient 0.2 white in a cell that is not `seen_outside`, the sky's otherwise; landscape released.
  - `SmartBox::SetWorldAmbientLight` (144222), `LScape::UseTime` (307222), `LScape::set_landscape_lighting` (307003).
  - `Render::useSunlightSet` (380646).
  - `RenderDeviceD3D::DrawEnvCell` (456878), `D3DPolyRender::SetStaticLightingVertexColors` (454918), `calc_point_light` (454579), `LIGHTINFO::convert_to_local` (454319), `D3DPolyRender::RenderMeshSubset` (454633), `D3DPolyRender::ConstructMesh` (455780) and its call from `CEnvCell::UnPack` (349389).
  - Constants: `static_light_factor = 1.3` (45774), `diffuseVal = 1.0` (45775), `onlyOnce = 1` (45776), `Render::max_static_lights = 40` (45530), `Render::max_dynamic_lights = 7` (45531), `rangeAdjust = 1.5` (45742); `Render::SetDegradeLevelInternal` (379790).
  - Light gathering: `CPartArray::InitLights` (326321), `CPartArray::AddLightsToCell` (325212), `LIGHTLIST::set_frame` (325029), `CObjCell::add_static_to_global_lights` (346859), `CEnvCell::flush_cells` (349880), `CEnvCell::grab_visible_cells` (350172), `CEnvCell::add_visible_cell` (349824), `Render::insert_light` (380524), `Render::add_static_light` (380618).
  - Objects: `Render::minimize_object_lighting` (380659), `Render::minimize_envcell_lighting` (379652), `PrimD3DRender::config_hardware_light` (453119), `Render::viewconeCheck` (379711), `RenderDeviceD3D::DrawMesh` (458209).
- **Detail textures (open point, not implemented):** `LScape::SetDetailTexturing` (308209), `LScape::GenerateDetailSurface` (307691), `Render::environment_detail_tiling = 2.0` (45536), the environment-detail preference `byte_81EF95 = 1` (45603).

## ACE files used

- `Source/ACE.Server/Physics/Common/ObjCell.cs` (`find_cell_list`), `EnvCell.cs` (`find_transit_cells`, `check_building_transit`, `find_visible_child_cell`, `point_in_cell`), `BuildingObj.cs` (`find_building_transit_cells`), `CellStruct.cs` (`point_in_cell`): how a moving object changes cell, read alongside the retail functions.
- `Source/ACE.Entity/CharacterPositionExtensions.cs`: the Holtburg start position (Training Academy, cell 0x860301AD).
- `Source/ACE.Common/GameConfiguration.cs`: landblock 0x0007 is "Town Network".
- `Source/ACE.Server/Physics/PhysicsGlobals.cs` `FloorZ` (through the cells track's `CellStructure.Floors`).

## ACViewer `Locations.txt` entries used

`Town Network | POI | 0x00070143 [70 -60 0.005] 0 0 0 -1`, `Holtburg Redoubt | Dungeon | 0x016301AC [140 -40 0] -0.707107 0 0 -0.707107`, `Marketplace | POI | 0x016C01BC [49.206 -31.935 0.005] 0.707107 0 0 -0.707107`, `Mines of Despair | Dungeon | 0x01880307 [30 -70 0] 1 0 0 0`, `Glenden Wood Dungeon | Dungeon | 0x01E3033D [161 -150 0] -0.983255 0 0 -0.182236`. Each is checked against the user's dats by `InteriorLocationsTests`; `Hotel Swank | POI | 0x018A024F [120 -79.9 0]` fails the check and is not used.
