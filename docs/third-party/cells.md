# Third-party sources: cells track

Track: `src/Lifestone.Data/World/Cells/` and `tests/Lifestone.Data.Tests/Cells/`.

**No file was ported.** Behaviour is re-expressed from the sources below, and each algorithm or constant cites its source in a code comment.

The retail decompile is cited by function name and acclient.c line only; none of its code is in the repo.

## Libraries used at runtime

| Source | Licence | Use |
|---|---|---|
| Chorizite/DatReaderWriter 2.1.9 (NuGet `Chorizite.DatReaderWriter`; source clone `vendor-eval/DatReaderWriter` @ a40acb45) | MIT | Unpacks records: `EnvCell`, `LandBlockInfo`, `Environment`/`CellStruct`/`Polygon`/`CellBSPTree`, `GfxObj` (building portal polygons) and `Surface` (type bits). Lifestone reads the raw bytes itself through `DatSet.Read`, checks byte-exact consumption, and reinterprets the stored ids as retail does (array positions). |

## Behaviour sources (read, not copied)

### Retail client decompile

Mag-nus/acclient-AI-RE, `2013-09 11.4186` (MIT label; per its doc 13 the rendering core is unchanged in 11.6096, the user's exe). The functions used:

- **Record layouts:**
  - `CEnvCell::UnPack`, `CEnvCell::Pack`;
  - `CCellPortal::UnPack`;
  - `CLandBlockInfo::UnPack`;
  - `CBldPortal::UnPack`;
  - `CEnvironment::UnPack`, `CEnvironment::get_cellstruct`;
  - `CCellStruct::UnPack`, `CCellStruct::get_portal`;
  - `CPolygon::UnPack`, `CPolygon::make_plane`;
  - `BSPPORTAL::UnPackPortal`;
  - `Frame::UnPack`, `Frame::localtoglobal`.
- **Mesh rules:**
  - `D3DPolyRender::ConstructMesh` (acclient.c:455780);
  - `D3DPolyRender::DrawMesh` (acclient.c:455154, `skipNoTexture` acclient.c:45777);
  - `D3DPolyRender::SetSurface` (acclient.c:454437, stippled → WRAP addressing);
  - `copyVert`;
  - `RenderDeviceD3D::DrawEnvCell`;
  - `D3DPolyRender::RenderMeshSubset` (cull mode).
- **Visibility:**
  - `PView::DrawInside`, `ConstructView` (both overloads), `InitCell`, `ClipPortals`, `OtherPortalClip`, `AddViewToPortals`, `InsCellTodoList`, `DrawCells`, `DrawPortal`, `add_views`;
  - `RenderDeviceD3D::DrawBuilding`, `DrawMeshInternal`;
  - `BSPNODE::build_draw_portals_only`;
  - `SmartBox::RenderNormalMode`;
  - `CellManager::ChangePosition` (seen_outside, sunlight vs ambient 0.2);
  - `CEnvCell::grab_visible_cells`.
- **Containment:**
  - `BSPNODE::point_inside_cell_bsp` (acclient.c:362944);
  - `CCellStruct::point_in_cell`.
- **Buildings:**
  - `CLandBlock::init_buildings`;
  - `CBuildingObj::add_to_stablist`;
  - `CLandBlockInfo::GetRestrictionIID` and its only caller, `CLandBlock::init_static_objs` (land cells only).
- **Docs:** `docs/06-rendering.md` §6 (indoor and portal rendering), §10 (alpha and stipple).

### ACE

ACEmulator/ACE v1.78.4816 @ 95ef426d, AGPL-3.0, `vendor-eval/ACE`. Used as a cross-check and for facts:

- `Source/ACE.Server/Physics/Common/{EnvCell,BuildingObj,BldPortal,CellStruct,Landblock,LandDefs}.cs`:
  - `IsDungeon` (re-expressed in `CellDiscovery.Summarize`);
  - `get_envcells` (the `NumCells` id sequence);
  - `lcoord_to_gid`;
  - `get_building_cells` (the building-cell idea).
- `Source/ACE.Server/Physics/BSP/BSPNode.cs` `point_inside_cell_bsp`.
- `Source/ACE.Server/Physics/PhysicsGlobals.cs` `FloorZ`.
- `Source/ACE.Entity/Enum/{EnvCellFlags,PortalFlags,CullMode,StipplingType}.cs`.
- `Source/ACE.DatLoader/Entity/{CellPortal,CBldPortal}.cs`.
- **Test anchors:**
  - `Source/ACE.Entity/CharacterPositionExtensions.cs`: start position 0x860301AD (12.3199, -28.482, 0.005);
  - `Source/ACE.Common/GameConfiguration.cs`: 0x0007 "Town Network".

### ACViewer

ACEmulator/ACViewer @ c5c54fb, GPL-3.0, `vendor-eval/ACViewer`. Consulted:
- `ACViewer/Render/{R_CellStruct,R_EnvCell,InstanceBatch,Buffer}.cs`.

Its rule of skipping `Stippling == NoPos` was **not** followed. Retail filters by surface texture instead, and the two differ on 346 solid-colour faces.

### OpenAC

eriknihlen/OpenAC @ 1ee5a08, MIT, `vendor-eval/OpenAC`. Consulted:
- `src/AcDream.Core/World/Cells/{CellGraph,CellPortal,EnvCell}.cs`;
- `src/AcDream.App/Rendering/CellVisibility.cs`.

Nothing was taken from it. Its `EnvCell.FromDat` keeps the 16-bit other-cell id without the block mask and ignores portal flag 4. Lifestone does neither.

## Independent cross-check

`tests/Lifestone.Data.Tests/Cells/IndependentCellDecoder.cs` is written directly from the retail unpack routines. It shares no code with DatReaderWriter.

`tests/Lifestone.Data.Tests/Cells/PortalRayTracer.cs` is a ray oracle for the portal walk. It was written for this track and shares no clipping or cone code with `PortalVisibility`. It follows rays through portal polygons under the same retail rules (PVS, `PView::InitCell` side test, `PView::OtherPortalClip`), and `CellVisibilityExhaustiveTests` requires every cell a ray enters to be in the walk's result.

The per-record facts in `CellDecodeTests` come from the research Python reader, run read-only on the same dats: `acformats.py` (`parse_envcell`, `parse_landblock_info`) from the session scratchpad `research/scout`. They are small identifiers and counts, not data dumps.
