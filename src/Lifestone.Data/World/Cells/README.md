# World/Cells: statics, buildings, interiors and dungeons

Engine-agnostic decoding and algorithms for everything under a landblock that is not terrain or procedural scenery:

- LandBlockInfo statics and buildings;
- EnvCells (rooms) and their geometry;
- portal visibility;
- dungeon discovery.

Everything is in AC coordinates: right-handed, Z up, metres. Frames are landblock-local. Sources are cited in the code and listed in `docs/third-party/cells.md`.

Every number below was measured on the end-of-retail dats (cell 982, portal 2072). The tests in `tests/Lifestone.Data.Tests/Cells/` assert almost all of them.

## API map

| Need | Use |
|---|---|
| Statics and buildings of a landblock | `LandblockStatics.Read(dats, coord)`. Use its `Objects`, `Buildings` (with `BuildingPortalData`) and `RestrictionTable`. |
| One room | `EnvCellData.Read(dats, cellId)` |
| All rooms of a landblock (the streaming unit) | `LandblockCellSet.Load(structures, coord)`. It is also the `ICellSource` the portal walk uses. |
| Room shape (portals, BSP, floors, bounds) | `CellStructureLibrary.Get(cell)` returns a `CellStructure`. Create one library per `DatSet`; it is thread-safe. |
| Drawable mesh of a room | `CellMeshCache.Get(cell)` returns a `CellMesh`, which holds `MeshData` plus per-sub-mesh texture addressing. It is thread-safe and bounded (LRU). |
| Which cell contains a point | `LandblockCellSet.FindCell(point, hintCellId)` |
| Visible cells from indoors | `PortalVisibility.FromInside(cells, viewerCellId, eye, frustum)` |
| Visible cells when looking into a building from outdoors | `BuildingPortalGeometry.Load(dats, building)`, then `PortalVisibility.FromOutside(...)` |
| Potentially visible set | `PortalVisibility.PotentiallyVisibleSet(cell)` returns the cell plus its `VisibleCellIds`. |
| EnvCell ids of a landblock | `CellDiscovery.EnumerateEnvCellIds` (B-tree). Do **not** use `NumCells`. |
| Dungeon or interior landblocks | `CellDiscovery.Summarize` and `CellDiscovery.FindDungeons` |
| Teleport spawn points | `CellDiscovery.FindDungeonEntry` and `CellDiscovery.FindBuildingEntry`. These are heuristics. |

## Ids

A cell id has the form `0xXXYYCCCC`:

| Low word | Meaning |
|---|---|
| `0x0001-0x0040` | Outdoor land cells (`x*8+y+1`) |
| `0x0100+` | EnvCells, contiguous from 0x0100 (highest 0x1174) |
| `0xFFFE` | LandBlockInfo |
| `0xFFFF` | LandBlock |

Portal lists, visible lists and building stab lists store only the low word. The decoders return full ids.

An outside portal has `OtherCellId == CellIds.Outside` (0xFFFFFFFF). Flag bit 4 marks it, and in the data that bit coincides exactly with a stored other cell of 0xFFFF.

## LandBlockInfo (`0xXXYYFFFE`)

The layout follows `CLandBlockInfo::UnPack`. All 5,346 records decode byte-exact and agree with an independent decoder.

- **Statics.** There are 12,783 GfxObj and 30,159 Setup placements.
- **Buildings.** There are 6,979, all GfxObj (0x01). 1,515 of them have no doorways.
- **Doorways.** A `BuildingPortalData` holds:
  - flags (only 1 and 3 occur);
  - the entry EnvCell, plus the index of that cell's matching outside portal;
  - a stab list, which is the cells seen through the doorway. It always contains the entry cell.
- **Doorway side.** `PortalSide` names the side of the building's portal polygon a viewer must be on to look in: negative when true, positive when false. The interior is on the other side. That is the positive side for the 5,729 doorways with flags 1 and the negative side for the 11,208 with flags 3. Use `BuildingDoorway.CanLookIn` rather than assuming one side.
- **Restriction table.** 1,293 landblocks have one. Its keys are full cell ids:
  - 3,490 are land cells of the same block. Retail looks only these up (`CLandBlock::init_static_objs` calls `CLandBlockInfo::GetRestrictionIID` for each of the 64 land cells).
  - 3,000 are EnvCell ids and 650 are 0. Retail never looks these up.
- **`NumCells` is unreliable.** It is lower than the real EnvCell count in 38 landblocks and never higher. Examples: Holtburg 123 vs 138, the Town Network 0x0007 205 vs 498, the Academy 0x8603 568 vs 2,163. Retail never uses it to load cells.

## EnvCell (`0xXXYY0100+`)

The layout follows `CEnvCell::UnPack`. All 734,976 records decode byte-exact and agree with the independent decoder.

**Flags.** `SeenOutside` = 1, `HasStaticObjs` = 2 and `HasRestrictionObj` = 8.
- Only the values 0-3 and 8-B occur.
- Bit 4 ("HasWeenieObjs") is never set, and retail neither reads nor writes it.

**Surfaces** are stored as u16 values. Each is ORed with 0x08000000 and resolved as a Surface. Polygon `PosSurface` and `NegSurface` index this list.

**Geometry** is `Environment(0x0D000000|env).cells[structIndex]`:
- `CEnvironment::get_cellstruct` indexes the array.
- For polygons, vertices and structs alike, the stored ids equal their array positions in all 772 Environments. The loader rejects a record where they differ.

**Portals** (`CCellPortal::UnPack`):
- `PolygonId` names one of the struct's portal polygons.
- `OtherPortalIndex` indexes the neighbour's portal list.
- `PortalSide = !(flags & 2)`: when it is true, the room lies on the *negative* side of the portal plane. This holds for all 1,867,699 portals; the cell BSP was probed on both sides of each portal.
- All 1,706,795 reciprocal ExactMatch pairs coincide in landblock space, to within 3.4 mm.
- 1,778 inside portals are not reciprocal (697 with an index past the end of the neighbour's list, 1,081 pointing at a different portal). 1,662 of them are in cells beyond `NumCells`.

**Visible cells** form a precomputed PVS. Every id is in the same landblock and is never the cell itself.

**Statics** carry landblock-local frames.

**Frames** store the quaternion as w, x, y, z. A local point maps to the landblock as `origin + rotate(q, p)`; the portal-pair check above confirms this convention.

## Geometry (`CellMeshBuilder`)

The builder re-expresses retail `D3DPolyRender::ConstructMesh`, which `CEnvCell::UnPack` calls with the struct's drawing polygons, and the env-cell path of `D3DPolyRender::DrawMesh`.

*(M1 wave 3, render-fidelity fix.)* The ConstructMesh rules exist once: `Models/RetailConstructMesh.cs`. `CellMeshBuilder` adapts a `CellStructure` to it (with a build-time filter for the untextured subsets retail skips in cells) and `GfxObjMeshBuilder` adapts DatReaderWriter's GfxObj records. `CellExhaustiveTests.Every_distinct_cell_mesh_is_identical_through_the_cell_and_gfxobj_paths` builds all 17,134 distinct cell meshes through both adapters (and through the GfxObj path unfiltered with `SurfaceDrawRules` applied afterwards) and requires identical output. Sub-meshes now come in retail's subset order (first surface index, positive side first) for cells too, and a malformed UV table (neither empty nor one index per corner) fails loudly in both paths.

- **All drawing polygons go into the mesh, portal polygons included.** Retail does not filter by stippling.
- **Texture filter.** Retail then skips every subset whose Surface has neither Base1Image (2) nor Base1ClipMap (4). The flag `skipNoTexture` is 1 and nothing ever clears it. Over the 17,134 distinct (struct, surface list) meshes this drops:
  - 46,873 invisible NoPos portal faces;
  - 346 solid-colour faces. ACViewer, which skips only `Stippling == NoPos`, would draw these.

  It keeps 1,218 textured portal polygons, which retail draws.
- **Sides.**
  - Type 0: the positive side only.
  - Type 1: also the negative side (reversed winding, negated normals, same surface and UVs).
  - Type 2: the negative side uses the neg surface and UVs. No cell polygon has type 2.
  - Any other value throws, in GfxObjs too. Retail's ConstructMesh would draw it single-sided (`numSides = sides_type == 2 ? 2 : 1`, `numWindings = sides_type == 1 ? 2 : 1`), but no record in the dats has one, so how `CPolygon::UnPack` reads such a polygon is unverified.
- **Winding.** It is kept as authored: CCW from the positive side, which faces into the room. For 95.7% of drawn front-face triangles, a point 5 cm in front lies inside the cell BSP. Retail culls cells exactly like objects.
- **UVs** are passed through. A side stored without UV indices (NoPos) uses UV index 0, which is the vertex's first UV when it has one. A UV index outside the vertex's UV list gives (0,0), as in `copyVert`.
- **Texture addressing** is carried per sub-mesh in `SubMesh.Addressing` (`TextureAddressMode.Wrap` or `Clamp`). A subset whose polygons have any stippling bit uses WRAP; otherwise it uses CLAMP (`D3DPolyRender::SetSurface`).
  - 339 meshes list the same Surface twice with different addressing. They get two sub-meshes for that Surface, as the `MeshData` contract allows (one sub-mesh per surface, side and addressing). Key materials by (SurfaceId, `Addressing`), never by the Surface id alone.
- **Three structs have no drawing BSP:** 0x0D00006B[0], 0x0D00027F[0] and 0x0D0002F1[0], used by 8,236 cells. Retail never draws these cells (`PView::DrawCells`). `CellMesh.HasDrawingBsp` is false for them, and their meshes are empty anyway.
- **Not done here:**
  - Static lighting baked into vertex colours (`D3DPolyRender::SetStaticLightingVertexColors`).
  - The environment detail texture. It is drawn by the surface shaders (docs/render-contract.md §2.4); the 3.0 UV factor `CEnvCell::UnPack` passes is only the mesh's initial detail UV scale, which the first draw replaces with the region's environment detail tiling (`D3DPolyRender::SetDetailTiling`).

## Visibility: what retail does

**Choosing the path.** `SmartBox::RenderNormalMode` decides per frame:
- If the viewer cell is outdoors (low word < 0x100), it calls `LScape::draw`.
- Otherwise it calls `DrawInside(viewer cell)`.
- If the indoor cell is `seen_outside`, the landscape viewpoint is also kept updated, so the landscape can be drawn through doors.

**Indoors** (`PView::DrawInside`):
1. `add_views(cell.stab_list)`. Only the viewer cell and its VisibleCells can be drawn.
2. `ConstructView` runs a best-first walk (`InsCellTodoList`, nearest entry portal first).
   - `InitCell` looks through a portal only when the eye is on the room's side of its plane, or within 0.0002 of the plane.
   - It never looks back through the portal it came in by.
3. `ClipPortals` clips each portal to the current view in screen space.
   - A non-ExactMatch portal is clipped again by the neighbour's own polygon (`OtherPortalClip`).
   - An outside portal adds to `outside_view`.
   - A cell can collect several views.
4. `DrawCells` draws in this order:
   - the landscape clipped to `outside_view`, if there is one;
   - outside portal polygons into depth;
   - cells back to front (only structs with a drawing BSP);
   - each cell's objects, culled as sphere vs view cone per view.

**Looking in from outdoors.** `DrawBuilding` draws the building, then walks its GfxObj drawing BSP for PORT nodes. Each node gives a polygon index and a `portal_index`. `PView::DrawPortal` then:
- takes `CBldPortal = building.portals[portal_index]`;
- requires the eye on the outside of the polygon (negative side when `portal_side`);
- clips the polygon;
- runs the same walk from `other_cell_id`, entering by `other_portal_id`, over the doorway's stab list.

The outdoor PView never draws the landscape.

**Measured.** All 16,937 doorways have exactly one such polygon, and it coincides with the entry cell's outside portal.

**`seen_outside` beyond drawing.**
- `CEnvCell::grab_visible_cells` loads the landscape for such cells.
- `CellManager::ChangePosition` handles a viewer entering an indoor cell:
  - If the cell is not `seen_outside`, it releases the landscape.
  - It lights `seen_outside` cells with sunlight and the LScape ambient.
  - Every other indoor cell gets world ambient **0.2, white**.

## Visibility: what the API provides

**`PortalVisibility.FromInside` and `FromOutside`** reproduce the retail walk in 3D. A view is the cone of planes through the eye and the edges of the clipped portal polygon; portals are clipped with Sutherland-Hodgman (`ViewVolume.ThroughPortal`). Pass camera frustum planes, in landblock space with the positive side inside, to cull by frustum.

A view's planes are never rebuilt from clipped corners. Each edge of a clipped portal lies on a plane of the incoming view, which is reused unchanged, or on an edge of the portal, whose plane is built from the portal's own corners. An earlier version rebuilt the planes from the corners. A clip that produced two corners a few micrometres apart then gave a plane of rounding noise, which cut the next portal away and lost every cell behind it.

The result is:
- visible cells in visit order (draw them in reverse for back to front), each with its view volumes for object culling;
- `OutsideVisible` and `OutsideViews` (indoor walks only).

**Deviations, all toward showing more:**
- A portal whose plane passes through the eye (within 0.0002) passes the view on unchanged.
- An out-of-range neighbour portal index skips the extra clip.
- A cell reached through more than 32 views is treated as seen through the whole frustum. This happens for 215 cells over a sample of 7,790 walks.
- Clipping keeps corners up to 0.1 mm outside a plane. It merges corners closer than 0.1 mm and drops the plane of the short edge between them.

**Measured over those 7,790 walks** (one from every 97th cell): mean 22.1 cells, max 227, about 1.5 s in total.

**Completeness check** (`CellVisibilityExhaustiveTests`). This is an independent ray oracle (`PortalRayTracer`) with none of the walk's clipping code:
- Rays from the eye are followed from cell to cell through portal polygons, and any other polygon stops them.
- The rays obey the same retail rules: the PVS, the eye-side test and the neighbour-polygon clip.
- Every cell a ray enters must be in the walk's result, and a ray leaving by an outside portal requires `OutsideVisible`.

Results:
- About 139,000 walks (every 4th cell) with 400 rays each, and as many inside random camera frusta, miss nothing.
- A one-off denser probe (8,693 walks with 20,000 rays each, not in the suite) also missed nothing.
- The earlier version failed for 48 of those viewpoints.

**Wave 2 options.** Use the walk for culling (toggle cell and object visibility per frame). Alternatively, for a first cut, load and draw the viewer's PVS. Draw terrain and outdoor objects indoors only when `OutsideVisible` is true. Retail draws them only through outside portals; Godot needs a stencil or a plain depth-ordered draw to reproduce that.

## Discovery

**Classification** (`LandblockCellSummary.Kind`, Lifestone's own definition):

| Kind | Rule | Count |
|---|---|---|
| `Dungeon` | No buildings and no `SeenOutside` cell | 1,773 |
| `BuildingInteriors` | Every cell reachable through portals from a building doorway, and none below the landblock's lowest terrain vertex | 1,083 |
| `Mixed` | Everything else: towers over dungeons, basements, portal-less "box" cells beside buildings | 553 |

Every ACE `Landblock.IsDungeon` landblock (1,718: heights all 0, `num_cells` > 0, no buildings, not the NW island) is a `Dungeon`.

**Spawn points** (`FindDungeonEntry`, `FindBuildingEntry`). These are heuristics, because retail and ACE take teleport targets from the server database.
- For a dungeon: the lowest-id cell with a floor, in the largest portal-connected group of non-building cells.
- The point is the centroid of the cell's largest physics floor polygon (normal Z ≥ ACE `FloorZ` 0.664), checked with the cell BSP.
- This finds a point for all 1,773 dungeons.

**Known anchors for the teleport panel:**

| Place | Where |
|---|---|
| Holtburg inn | Building 0 of 0xA9B4, entry cell 0xA9B40100 |
| Academy | ACE's character start: cell 0x860301AD at (12.3199, -28.482, 0.005) (`CharacterPositionExtensions`). The test checks that this point is inside that cell. |
| Town Network | 0x0007 (ACE `GameConfiguration` "Town Network") |
| Largest cell landblocks | 0x00D1 (4,213 cells), 0x200F (2,468, `Mixed`), 0x8C03 (2,295), 0x7E03 (2,260), 0x8603 and 0x8803 (2,163 each). The test checks these six and their kinds. |

## Threading and caching

- `CellStructureLibrary` decodes each Environment once.
- `CellMeshCache` builds each key once, outside its lock, and evicts least-recently-used entries.
- Records are read as raw bytes through `DatSet.Read`, which holds the lock, and decoded outside it.
- DatReaderWriter's own object cache is not used for cells: it is unbounded and would keep every EnvCell ever read.
- A full scan of all 5,346 LandBlockInfos and 734,976 EnvCells with two decoders takes about 3 s in parallel.
