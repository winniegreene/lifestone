# Rendering Dereth faithfully: algorithms to reimplement in Godot

## 0. Where these findings come from

I verified each item below against at least two of these sources. Line numbers refer to the files I read through `gh api`.

- **ACEmulator/ACViewer** (GPL-3.0, HEAD c5c54fb, 2026-07-07). It is a MonoGame viewer. It ships its own copy of ACE physics under `ACViewer/Physics/**`, in the namespace `ACE.Server.Physics.*`.
- **ACEmulator/ACE** (AGPL-3.0, HEAD 7715c05, 2026-10-08):
  - `Source/ACE.Server/Physics/Common/{LandblockStruct,Landblock,ObjectDesc,LandDefs}.cs` matches ACViewer's copy, apart from client-only normals/UVs and `const` vs `static readonly`.
  - `Source/ACE.DatLoader/**` contains the file formats.
- **Mag-nus/acclient-AI-RE** (MIT label, pushed 2026-07-31). This is a Hex-Rays decompile of acclient 11.4186, named from the original Turbine PDB, split into `2013-09 11.4186/acclient-src/src/PORTAL/**`.
  - Its doc 13 states that the rendering core (RenderDeviceD3D, D3DPolyRender, ImgTex, PView, SurfaceWindow: 265 functions) has **0 changes** in 11.6096.
  - 11.6096 (MD5 116d9a66…) is exactly the user's `acclient.exe`.
  - This is the authority for retail formulas.
- **eriknihlen/OpenAC** (MIT, .NET 10/Vulkan client, HEAD 1ee5a08, 2026-10-08). It implements the retail sky, terrain blending and scenery. It credits Chorizite/WorldBuilder, which has **no license file**.
- **An earlier private prototype by the author** (AGPL-3.0, Godot 4.6 + .NET 8). Its sky is faked.
- **Checks against the user's own `client_portal.dat` / `client_cell_1.dat`.** I ran read-only Python scripts in the scratchpad `research/render/scripts/`:
  - `acdat.py` is a container reader.
  - `dump_region.py`, `dump_textures.py`, `scenery_check.py`, `split_check.py`, `winding_check.py` and `sky_extent.py` produce the checks below.
  - The RegionDesc 0x13000000 parse consumed exactly 58,956/58,956 bytes.

## 1. IDs and coordinates

- Cell id `0xXXYYCCCC`. `XX` = landblock X (**east**), `YY` = landblock Y (**north**). ACViewer `R_Landblock.GetWorldTransform` uses `x=ID>>24`, `y=(ID>>16)&0xFF`, translated by `(x*192, y*192, 0)`.
  - Check: Holtburg 0xA9B4 → `gy/240-101.95 = 42.45N`, `gx/240-101.95 = 33.65E`.
  - The earlier prototype's doc `terrain.md` labels the high byte "NS". That label is wrong; its code (`LandblockAxisMath.cs`) is correct.
- `CCCC`:
  - `0xFFFF` = CellLandblock: u32 id, u32 hasObjects, u16 terrain[81], u8 height[81], then align.
  - `0xFFFE` = LandblockInfo.
  - `0x0001-0x0040` = land cells, index = `x*8+y+1`.
  - `0x0100+` = EnvCells.
- Vertex index is `i = x*9 + y`, where x is east and y is north. `i=0` is the SW corner.
- Terrain word:
  - bits 0-1 road
  - bits 2-6 terrain type (32 types)
  - bits 7-10 unused
  - bits 11-15 scene slot
- The world is **right-handed, Z-up, X=east, Y=north**. Heading 0 is north, 90 is east (clockwise seen from above). `AFrame.set_heading(d)` gives a rotation about +Z by −d.
  - Check: in 3,000 GfxObjs, 39,996 of 40,283 single-sided polygons have `cross(v1−v0, v2−v0)` agreeing with their stored vertex normals (99.3%).
  - So the AC front face is **CCW under the right-hand rule**. The earlier prototype's claim that AC is left-handed is wrong.
- DAT `Frame` = Vector3 origin followed by a quaternion stored **W,X,Y,Z** (`ACE.DatLoader/Entity/Frame.cs`). Quaternion products are Hamilton products. Retail `Frame::combine` (CPartArray.c:1553) is `q_world = q_obj ⊗ q_anim`.

## 2. Terrain mesh

The source is `LandblockStruct.cs` (ConstructVertices, ConstructPolygons, ConstructNormals). The retail equivalent is `CLandBlockStruct.c` (ConstructPolygons, calc_lighting).

- **Vertices.** `vertex(x,y) = (x*24, y*24, LandHeightTable[Height[x*9+y]])`, with x and y in 0..8.
  - The user's RegionDesc table has steps of 2.0 m for bytes 0–200, 4.0 for 200–240, 8.0 for 240–250, 10.0 for 250–254 and 20.0 for 254–255. The maximum is 700 m.
  - Other LandDefs values: SquareLength 24, VertexPerCell 1, RoadWidth 5, SkyHeight 1000, MaxObjHeight 200.
- **Diagonal split per cell.** Inputs are global cell coordinates `gx = lbX*8+x` and `gy = lbY*8+y`. All arithmetic is uint32 and wraps.
  ```
  s = gy*(214614067*gx + 1813693831) - 1109124029*gx - 1369149221;
  SWtoNEcut = (double)s * 2.3283064e-10 >= 0.5;   // retail CLandBlockStruct.c, ACE ConstructPolygons
  ```
  - The bit test `(s & 0x80000000)!=0` (AC2D/OpenAC `CellSplitDirection`) gives **0 mismatches over all 4,161,600 map cells** (brute-forced).
- **Triangles (AC order, CCW from above).** Corners: SW=(x,y), SE=(x+1,y), NE=(x+1,y+1), NW=(x,y+1).
  - `!SWtoNEcut`: `(SW,SE,NW)` and `(NE,NW,SE)` (diagonal SE–NW).
  - `SWtoNEcut`: `(SW,SE,NE)` and `(SW,NE,NW)`.
- **Normals and retail lighting** (`calc_lighting`):
  - Vertex normal = normalize(sum of the plane normals of the block's triangles touching that vertex). If the length is below 2e-4, use (0,0,1).
  - Neighbouring blocks are **not** included. That produces lighting seams at block edges, which is retail behaviour.
  - Per vertex: `rgb = min(1, sunColor*max(0, dot(n, sunVec)) + ambColor*ambientLevel)`.
  - `sunVec` is **not normalised**; its magnitude is DirBright (section 8).
  - Terrain is Gouraud-lit with vertex colours and modulated by the merged texture.
- **Height lookup** (physics, ground snapping): find the triangle in the cell with `point_in_poly2D`, then `z = -(n.x*x + n.y*y + d)/n.z` (`Plane.set_height`).
- **Water.** Terrain types 16–20 count as water (`SurfChar`); `calc_water_depth` returns 0.45 m on water and 0.1 m otherwise. I believe water is drawn as ordinary terrain texture, but I have not verified that.
- **LOD.** Retail has low-detail blocks (`Generate(cellScale)`, `TransAdjust` stitching) and a `mid_width×mid_width` grid of blocks. LScape `MidRadius` is 5 in the port. I recommend skipping LOD at first.

## 3. Terrain texturing (TexMerge): exact retail algorithm

Retail merges textures on the CPU into one 1024² texture per unique palCode (`TexMerge::FillTempTexBuffer`, `ImgTex::TileCSI/MergeTexture`). ACViewer and OpenAC reproduce it on the GPU with texture arrays.

1. **Per-cell palCode** (`GetCellRotation` / `GetPalCode`). Corner order is t1=SW (x,y), t2=SE (x+1,y), t3=NE (x+1,y+1), t4=NW (x,y+1).
   ```
   pal = 1<<28 | r1<<26 | r2<<24 | r3<<22 | r4<<20 | t1<<15 | t2<<10 | t3<<5 | t4
   ```
   where `t = (word>>2)&0x1F` and `r = word&3`.
2. **GetTerrain(pal) → base layer and overlays with tcodes.** Corner bits are SW=1, SE=2, NE=4, NW=8.
   - If any two corner types are equal: let i be the first duplicated index. base = type[i]. Scan k = 0..3, skipping type[i]:
     - The first different type becomes overlay0 with `tcode0 = 1<<k`.
     - At the next differing corner: if it has the same type as overlay0 **and** `tcode0 == 1<<(k-1)`, then `tcode0 += 1<<k`. Otherwise overlay1 = that type with `tcode1 = 1<<k`. Either way, stop scanning.
   - If all four differ: base = SW, and the overlays are SE, NE, NW with tcodes 2, 4, 8.
3. **Alpha selection** (`FindTerrainAlpha`). Retail constants are confirmed at CTexMerge.c:242/265.
   ```
   maps = (tcode in {1,2,4,8}) ? CornerTerrainMaps : SideTerrainMaps;
   idx  = floor((uint32)(1379576222*pal - 1372186442) * 2.3283064e-10 * maps.Count); if(idx>=Count) idx=0;
   code = maps[idx].TCode; rot=0; while(code!=tcode){ code*=2; if(code>=16) code-=15; if(++rot>=4) fail; }
   ```
4. **Roads** (`GetRoadCode`). Build `mask` from corners with road != 0 (SW→1, SE→2, NE→4, NW→8).
   - 0xF → whole cell is road; the base is the RoadType (32) texture.
   - 0xE → (6,12); 0xD → (9,12); 0xB → (9,3); 0x7 → (3,6).
   - Otherwise → (mask, –).
   - `FindRoadAlpha` starts at the same prng index, tries each map in turn `(i+prng)%n`, and rotates each up to 4 times.
5. **Composite** (`FillTempTexBuffer`, `ImgTex::MergeTexture`, ImgTex.c:353). Order: base, then overlays 0..2, then road 0..1.
   ```
   c = T_base(uv*TexTiling)
   for each layer L with alpha map A at rotation r:  a = A(rot_r(uv));  c = a*c + (1-a)*T_L(uv*TexTiling)
   ```
   - Alpha 255 keeps what is underneath; 0 means 100% of the new layer. Bytes above 0x80 get +1, then the result is divided by 256.
   - Road layers use the road terrain texture with the same rule.
   - `rot_r(u,v)` is: r0 `(u,v)`, r1 `(1-v,u)`, r2 `(1-u,1-v)`, r3 `(v,1-u)`. This is checked against the stride table in MergeTexture and against OpenAC `terrain_modern.vert`.
   - Base UVs per corner: SW (0,1), SE (1,1), NE (1,0), NW (0,0). `LandblockStruct.LandUVs`: north is up and v=0 is north.
   - **Tiling.** `TileCSI` copies the 512² texture `tex_tiling×tex_tiling` times into the 1024² (`BaseTexSize`) cell texture.
     - In the user's region all 33 `TMTerrainDesc` entries have **TexTiling = 2**, so a terrain texture repeats every 12 m.
     - **ACViewer ignores this** (one repeat per 24 m cell, clamp sampler). OpenAC multiplies UVs by the tiling.
     - Alpha maps are scaled once over the cell (512 → 1024, pixel-replicated).
   - ACViewer and OpenAC use parad0x's `maskBlend3` approximation (`Content/texture_clamp.fx`). The sequential lerp above is the exact retail operation and is also simpler.
6. **Data from the user's DAT.**
   - BaseTexSize 1024.
   - Corner maps: four maps, all TCode 8 (0x05001371, 0x0500143E, 0x0500143F, 0x05001440).
   - Side map: one, TCode 9 (0x05001441).
   - Road maps: 0x0500168E (RCode 9), 0x0500168C (RCode 10), 0x0500168D (RCode 8).
   - Terrain textures: 512² A8R8G8B8. Road texture: TerrainType 32 → 0x05001458.
   - Alpha maps: 512² `PFID_CUSTOM_LSCAPE_ALPHA` (8-bit).
   - LandSurf.Type = 0, so there is no PalShift.
   - Landscape detail texturing is hard-disabled in retail (doc 06 §5), so ignore DetailTexGID.
   - I did not verify whether MinVertBright, MaxVertBright and the saturation/hue fields are consumed; I believe they are not.
7. **GPU implementation that matches ACViewer's approach.**
   - Buffer layout: overlay atlas 512² RGBA (Texture2DArray) and alpha atlas 512² A8 (`Render/Buffer.cs`).
   - Vertex layout `Model/LandVertex.cs`: TexCoord0 = (u, v, baseIdx) and TexCoord1..5 = (u', v', texIdx, alphaIdx), with −1 meaning unused.
   - Geometry is non-indexed: 3 vertices per triangle, 384 vertices per block (`Render/TerrainBatchDraw.cs`).

## 4. Procedural scenery: exact formulas

Sources are ACE/ACViewer `Landblock.get_land_scenes` and `ObjectDesc.cs`. Every constant also appears in retail `CLandBlock.c:1321-1360` and `region/Scene.c:33-85`. All integer arithmetic is uint32 and wraps; `R = 2.3283064e-10`.

```
for x,y in 0..8 (all 81 vertices):  w=Terrain[x*9+y]; tt=(w>>2)&31; st=w>>11
  scenes = SceneInfo.SceneTypes[ TerrainInfo.TerrainTypes[tt].SceneTypes[st] ].Scenes; if empty continue
  gx=lbX*8+x; gy=lbY*8+y
  idx = (int)(scenes.Count * R*(gy*(712977289*gx+1813693831) - 1109124029*gx + 2139937281)); if(idx>=Count) idx=0
  scene = Scene(0x12xxxxxx); m = 1360117743*gx*gy + 1888038839
  for j,obj in scene.Objects:
    if !(R*(-1109124029*gx + 1813693831*gy - m*(23399+j)) < obj.Freq) || obj.WeenieObj!=0: continue
    rnd(k) = R*(1813693831*gy - (j+k)*(1360117743*gy*gx+1888038839) - 1109124029*gx)
    px = DisplaceX>0 ? rnd(45773)*DisplaceX + Base.X : Base.X ;  py likewise with 72719
    q = R*(1813693831*gy - gx*(1870387557*gy+1109124029) - 402451965)
    (px,py) = q>=.75?(py,-px): q>=.5?(-px,-py): q>=.25?(-py,px):(px,py)
    lx=x*24+px; ly=y*24+py; reject if outside [0,192) or OnRoad(lx,ly)
    cell = landcell(lx,ly); reject if cell has a building (building origin lies in that 24 m cell)
    tri = terrain triangle at (lx,ly); reject unless MinSlope <= tri.n.z <= MaxSlope
    z = terrain height (BaseLoc.Z discarded by set_height)
    rot = Align ? heading facing -normal (ObjAlign) : (MaxRotation>0 ? set_heading(rnd(63127)*MaxRotation) : BaseLoc.q)
    reject unless obj_within_block() (unscaled sorting sphere/cylspheres inside 0..192)
    scale = Min==Max ? Max : Min*pow(Max/Min, rnd(32593))     // uniform
```

- **OnRoad** (`Landblock.OnRoad`): RoadWidth 5, tile 24. It is a 16-case test on the road bits of the cell's four corners, `GetRoad(x,y)=Terrain[x*9+y]&3`.
  - Example: corner r0 only → `dx+dy<5`.
  - Example: r0 and r3 → `|dx−dy|<5`.
  - Port the table verbatim.
- **set_heading(deg).** Orientation becomes a Z-rotation of −deg. xRot is `asin(M13+M23)` of the base orientation, which is 0 for identity. This **replaces** the BaseLoc orientation.
- **Region data.** 89 SceneTypes. Each TerrainType has 32 scene slots.
- **Spot check.** On Holtburg block 0xA9B3 the formulas give 177 candidates: 30 out of block, 5 on road, 142 survivors before the slope/building tests. Town block 0xA9B4 gives 0.

## 5. Static objects, buildings, EnvCells

- **LandblockInfo `0xXXYYFFFE`:**
  - u32 NumCells
  - `Objects[]` Stab {u32 id (0x01 GfxObj or 0x02 Setup), Frame}
  - u16 nBuildings, u16 PackMask
  - `BuildInfo{ModelId, Frame, NumLeaves, CBldPortal[]}`
  - CBldPortal: u16 flags, OtherCellId, OtherPortalId, StabList; align.
  - All frames are landblock-local. The placement cell is found with `AdjustToOutside`.
  - ACViewer places statics exactly at their frame; `adjust_scene_obj_height` only runs on re-init.
- **Building interiors.** Start from each portal's `OtherCellId` (entry EnvCell), then recurse through `EnvCell.VisibleCells` (`BuildingObj.get_building_cells`).
- **Dungeon test** (ACViewer `Landblock.IsDungeon`): all heights 0, NumCells > 0 and 0 buildings. There is an exception for the NW island (`BlockCoord.X<64 && Y>1976`).
- **EnvCell** (`0xXXYY0100+`):
  - Fields: flags (SeenOutside 1, HasStaticObjs 2, HasRestrictionObj 8); u8 numSurfaces → `0x08000000|u16`; `EnvironmentId = 0x0D000000|u16`; u16 CellStructure; Frame (landblock-local); CellPortals {flags, polyId, otherCellId, otherPortalId}; u16 VisibleCells (precomputed PVS); Stab statics with landblock-local frames.
  - Geometry = `Environment(0x0D).Cells[CellStructure]`, which is a CellStruct holding a vertex array, polygons, portal polygon ids and BSPs.
  - Polygon surface = `envCell.Surfaces[poly.PosSurface]`.
  - ACViewer skips polygons with `Stippling==NoPos` (portal and invisible faces) (`Render/R_CellStruct.cs`, `InstanceBatch.cs`). It also adds a +0.05 Z hack against floor z-fighting.
  - Caveat: `InstanceBatch.BuildModel` iterates **all** CellStructs of the Environment; use only the one indexed by CellStructure.
  - Retail draws interiors with portal culling (`PView::DrawCells`). Interior static lights are CPU-baked into vertex colours (D3DPolyRender::SetStaticLightingVertexColors) with ≤8 dynamic hardware lights.

## 6. Model assembly (Setup, GfxObj, Surface, palettes)

- **GfxObj (0x01):**
  - Flags (Physics 1, Drawing 2, DIDDegrade 8).
  - Surfaces: compressed-count array of 0x08 ids.
  - CVertexArray of SWVertex {u16 nUV, origin, normal, UV[]}.
  - Polygons {NumPts, Stippling, SidesType, PosSurface, NegSurface, VertexIds, PosUVIndices (unless NoPos=4), NegUVIndices (if SidesType==2 && !NoNeg=8)}.
  - Split render vertices by the (vertexId, uvIdx) pair (ACViewer `VertexArrayExtensions.BuildUVLookup`).
  - Triangulate each polygon as a fan `(v0, v[i-1], v[i])` (`Model/Polygon.cs`).
  - SidesType: 0 = single-sided (40,283 of 46,401 sampled polygons); 1 = both sides with the same surface (6,113); 2 = separate negative side (5). Emit a reversed copy for 1 and 2.
- **LOD.** `DIDDegrade → GfxObjDegradeInfo (0x11)` {gfxobj, degrade_mode, min/ideal/max dist}. degrade_mode 2 is a full billboard; 3–5 are axis-locked billboards (doc 06 §10).
- **Surface (0x08).** Type flags: Base1Solid 1, Image 2, ClipMap 4, Translucent 0x10, Luminous 0x40, Alpha 0x100, InvAlpha 0x200, Additive 0x10000.
  - Image/ClipMap surfaces carry OrigTextureId (0x05) and OrigPaletteId; other surfaces carry a solid ARGB ColorValue.
  - Common fields: Translucency, Luminosity, Diffuse.
  - Retail blending: Additive → ONE/ONE; Alpha+Additive → SRCALPHA/ONE; Alpha → SRCALPHA/INVSRCALPHA.
  - Clip maps use alpha test with ALPHAREF 200 for every textured clip map; ACViewer zeroes palette indices < 8.
    - **Erratum (wave 1):** this line said "ALPHAREF 100 (palettised) or 200 (DXT)". Retail `D3DPolyRender::SetSurface` uses `s_256AlphaTestRef` = 100 only while the bound texture still carries a palette (`ImgTex::m_pPalette`), and palette-expanded textures never do: only `ImgTex::CreateFromRenderSurface_Internal` sets it. So `s_ddsAlphaTestRef` = 200 applies to every textured clip map, palettised ones included (`docs/textures.md` §5).
  - Translucency t → alpha (1 − t), drawn with z-writes off.
- **SurfaceTexture (0x05)** → list of 0x06 Textures, highest resolution first.
  - **In the user's install, 2,284 of 7,221 SurfaceTextures have `Textures[0]` missing** because there is no `client_highres.dat`.
  - Every SurfaceTexture has at least one texture present.
  - ACViewer takes `Textures[0]` and falls back to a magenta texture, so pick the first id that exists.
- **Texture (0x06):** id, unk, w, h, PFID, len, data, then a palette id if INDEX16 or P8.
  - Format counts in the portal: A8R8G8B8 12,984; INDEX16 4,182; DXT1 1,971; R8G8B8 1,190; DXT5 127; A8 86; RAW_JPEG 79; LSCAPE_R8G8B8 33; LSCAPE_ALPHA 16; P8 6; DXT3 5; R5G6B5 3; A4R4G4B4 2.
  - Byte orders: A8R8G8B8 is BGRA in memory; R8G8B8 is BGR; CUSTOM_LSCAPE_R8G8B8 is R,G,B; JPEG is an ordinary JPEG (no channel swap).
    - **Erratum (wave 1):** this line said "JPEG needs an R/B swap". Retail `RenderSurface::CreateFromSourceData` decodes the JFIF with the Intel JPEG Library into a BGR D3D `R8G8B8` surface (`DIBColor = IJL_BGR`), which is an ordinary decode. ACViewer's `SwapRedAndBlueChannels` only undoes its own GDI+ BGRA to XNA RGBA copy (`docs/textures.md` §2).
    - **Erratum (wave 1):** CUSTOM_LSCAPE_R8G8B8 (0xF3) is stored R,G,B, not B,G,R like R8G8B8. Decoded as B,G,R the dirt and grass textures 0x0600382A and 0x06003794 come out blue and teal (`docs/textures.md` §1; `RenderSurfacePixelTests.Landscape_rgb_decodes_byte_0_as_red`).
- **Setup (0x02):**
  - Parts[]; ParentIndex (if flag 1, unused for posing); DefaultScale[] (flag 2); HoldingLocations; ConnectionPoints.
  - PlacementFrames {key: AnimationFrame with one Frame per part}. Use key 0x65 (Resting), else 0.
  - Spheres, Lights and Default{Animation, Script, MotionTable, SoundTable}.
  - Part 0x010001EC is an anchor and is skipped.
  - Part pose (retail `CPartArray::UpdateParts` / `Frame::combine`) is **flat, not hierarchical**:
    `part.origin = obj.origin + R(obj.q)*(anim.origin*objScale); part.q = obj.q ⊗ anim.q`
  - Mesh vertices are scaled by `GfxObjScale = DefaultScale[i]*objScale` in part-local space.
- **Clothing and palettes:**
  - `ClothingTable(0x10).ClothingBaseEffects[setupId].CloObjectEffects{Index=part, ModelId=new GfxObj, CloTextureEffects old0x05→new0x05}`.
  - `ClothingSubPalEffects[PaletteTemplate].CloSubPalettes{Ranges{Offset,NumColors}, PaletteSet}`.
  - `PaletteSet(0x0F).GetPaletteID(shade) = List[(int)((n-0.000001)*shade)]`.
  - Apply a swap by copying `pal[o..o+n) ← newPal[o..o+n)` (same indices) over the texture's DefaultPaletteId, then expand INDEX16 to ARGB (`TextureCache.IndexToColor`).
  - Weenie/ObjDesc SubPalette offset and length are in units of 8 (0 means 2048 colours).
  - Character defaults (`Model/ObjDesc.AddBaseModelData`): skin (0,24), hair (24,8), eyes (32,8). Head is part 16 for hair, eye, nose and mouth texture swaps.

## 7. Animation

- **Animation (0x03):** Flags (PosFrames 1), NumParts, NumFrames, optional PosFrames[] (root motion), PartFrames[] = {Frame[NumParts], Hooks[]}.
- **MotionTable (0x09):**
  - DefaultStyle, StyleDefaults{style→substate}.
  - `Cycles[(style<<16)|(motion&0xFFFFFF)]` (loops).
  - `Modifiers`.
  - `Links[(style<<16)|(from&0xFFFFFF)][to]` (transitions).
  - MotionData = {AnimData{AnimId, LowFrame, HighFrame (−1 = last), Framerate}[], Velocity, Omega}.
  - Idle example: NonCombat 0x8000003D + Ready 0x41000003. Walk 0x45000005, Run 0x44000007.
  - Masks: Style 0x80000000, SubState 0x40000000, Modifier 0x20000000, Action 0x10000000.
- **Playback** (`Physics/Animation/Sequence.cs` = retail CSequence; `MotionTable.GetObjectSequence`/`get_link`/`add_motion`):
  - Link animations play once, then the cycle loops from `FirstCyclic`.
  - Framerate is multiplied by speed; a negative rate plays in reverse (starting frame = High+1−ε).
  - The displayed pose is `PartFrames[floor(frameNumber)]`. **Retail does not interpolate between frames** (CSequence::get_curr_animframe uses `_floor`). Slerping is an optional deviation.
  - Hooks fire per crossed frame, filtered by direction.
  - PosFrames and Velocity/Omega move the object.

## 8. Sky, lighting and fog: exact retail behaviour

ACViewer has **none** of this. It uses a fixed light `xLightDirection=-UnitZ`, `xAmbient=0.5`, a clear colour, and no fog or sky (`Render/Buffer.cs:107-112`).

### Data and time
- User region data: DayLength 7,620 s; 16 named times of day (night for fractions <0.25 and ≥0.875); SkyDesc TickSize 0.8 s, LightTickSize 15 s.
- 20 DayGroups: Sunny ×6, Clear ×3, Cloudy ×3, Rainy ×8. Each has ChanceOfOccur 5.0, which is **unused** in selection.
- **Day group** (`SkyDesc::CalcPresentDayGroup`):
  ```
  idx = floor((uint32)(1782775218*(day + daysPerYear*year) - 1967253934) * 2.3283064e-10 * nGroups)
  ```
  Fall back to 0 if out of range.

### Keyframes and lighting
- **Keyframes** (`DayGroup::GetTimeOfDay`): `before` = last SkyTime with begin ≤ t.
  - `after` = next; if `before` is the last, `after` = first and `ratio = (t−b)/(1−b)`.
  - Heading lerps **linearly, not by shortest arc**. The data has a 0.610→0.611 keyframe pair that flips heading from 90° to 270° at zenith.
- **Lighting** (`SkyDesc::GetLighting`):
  - `ambLevel = lerp(AmbBright)`; colours lerp per byte.
  - `sunVec = DirBright*(sinH·cosP, cosH·cosP, sinP)` from lerped heading and pitch.
  - Defaults when there is no data: amb 0.3, white, (0.5, 0, 0.8).
  - `LScape::UseTime` clamps to `min_ambient` (value unverified) and applies every LightTickSize.
  - Terrain uses the per-vertex formula in section 2.
  - Objects get a D3D directional sunlight plus world ambient `(ambLevel + 0.2*|sunVec|) × ambColor` (`set_landscape_lighting`).
- **Fog** (`GetWorldFog`): **linear range fog** only, used when both keyframes have WorldFog ≠ 0. start = lerp(MinWorldFog), end = lerp(MaxWorldFog), colour = lerp.
  - Dat values: night [0, 400] m, dawn [90, 800] m, day [150, 2400] m.
- Example SkyTime row from DayGroup 0:
  `b=0.27 DirBright .7 H90 P20 DirColor FAD797 AmbBright .35 AmbColor E6E6FF fog[150,2400] C3C8DC`.

### Sky objects
- **Selection** (`SkyDesc::GetSky`, `GameSky::UseTime/CalcFrame/Draw`):
  - An object is visible if `Begin==End` or `Begin < t < End` (strict, no wrap).
  - `rotation = Begin==End ? BeginAngle : lerp(BeginAngle, EndAngle, (t−Begin)/(End−Begin))`.
  - Replace entries from the `before` keyframe override the GfxObj and heading (Rotate ≠ 0).
  - Luminosity, MaxBright and Transparent lerp between `before` and `after` replace entries for the same object, then ×0.01 → SetLuminosity / SetDiffusion / SetTranslucency (alpha = 1−t).
- **Transform:** `set_heading(heading)`, then a **global** rotation about +Y (north) by −rotation.
- **Geometry from the dat:** dome 0x010015EE (±1,050 m box, luminous), stars 0x010015EF (alpha), clouds 0x01004C36 (radius about 14 km, TexVelocity (−0.013, 0.013) UV scroll), sun 0x01001348 (quad at x=+1050, Additive|Alpha).
- **Drawing:** camera-centred, depth test ALWAYS, depth writes off, zfar×4, drawn **before** the landscape.
  - Property bits: 1 = drawn in the after pass; 4 = weather (requires weather enabled, pinned to the player's XY, z=−120 unless bit 8); 2 = hidden while the environment fog override is active.
  - Fog is disabled for the sky unless the override is on.
- OpenAC (`AcDream.Core/World/SkyState.cs`, `SkyDescLoader.cs`, `App/Rendering/Sky/SkyRenderer.cs`, `Shaders/sky.*`) implements this, with small deviations: shortest-arc heading lerp and wrap-around visibility.

## 9. Converting to Godot 4 (Y-up, right-handed, −Z forward, clockwise front faces)

```
pos_g  = Vector3(ac.x, ac.z, -ac.y)          # proper rotation (det +1); east=+X, up=+Y, north=-Z
quat_g = Quaternion(ac.qx, ac.qz, -ac.qy, ac.qw)   # dat order is W,X,Y,Z
scale_g = Vector3(s.x, s.z, s.y)
heading h (deg)  -> Quaternion(Vector3.UP, deg_to_rad(-h))
sky obj          -> Quaternion(Vector3.FORWARD, deg_to_rad(-rotation)) * Quaternion(Vector3.UP, deg_to_rad(-heading))
```

- AC model forward is local +Y, which maps to Godot −Z, so no extra yaw is needed.
- AC front faces are CCW. Godot's ArrayMesh docs say it renders front faces clockwise, so **reverse every triangle**: terrain becomes `(SW,NW,SE),(NE,SE,NW)` and `(SW,NE,SE),(SW,NW,NE)`, and fans become `(v0,v[i],v[i-1])`.
  - The earlier prototype forced every material double-sided on the false belief that the axis swap flips winding.
- UVs need no flip: both use a top-left origin.
- The world is about 49 km across, so use a floating origin. Retail does the same: `LScape::calc_frame` places blocks relative to the viewer's block.
- **Terrain:** one MeshInstance3D per block (384 vertices). Unshaded ShaderMaterial with:
  - `sampler2DArray` terrain (33 layers 512² RGBA8, mipmapped)
  - `sampler2DArray` alpha (8 layers 512² R8)
  - per-cell layer, rotation and split data in CUSTOM0..3 or an 8×8 data texture
  - the sequential merge from section 3, the vertex lighting from section 2, and linear distance fog.
- **Scenery and statics:** MultiMeshInstance3D per (GfxObj, texture-change set), as ACViewer `GfxObjInstance_Shared` does.
- **Sky:** a camera-orientation-only sky rendered in a separate SubViewport (own World3D), shown as the Environment background (BG_CANVAS). Godot cannot draw "depth-always first" meshes inside the opaque pass.
- Godot imports BC1/2/3 (DXT1/3/5) Images directly.

## KEY FACTS
- The terrain split is s = gy*(214614067*gx+1813693831) - 1109124029*gx - 1369149221 (uint32), with SWtoNEcut = s*2.3283064e-10 >= 0.5. This matches ACViewer/ACE LandblockStruct.ConstructPolygons and retail CLandBlockStruct.c ConstructPolygons. Brute force over all 4,161,600 cells showed 0 mismatches against the bit test (s&0x80000000) (scratchpad split_check.py).
- Landblock ID high byte = X (east), low byte = Y (north). Sources: ACViewer Render/R_Landblock.cs GetWorldTransform and ACE LandDefs.blockid_to_lcoord. Holtburg 0xA9B4 maps to 42.45N 33.65E. The earlier prototype's docs/format-notes/terrain.md label is inverted; its code is correct.
- Terrain vertex index is x*9+y (x east, y north). z = RegionDesc.LandDefs.LandHeightTable[height byte]. In the user's DAT the table has steps of 2 m for bytes 0-200, 4 m for 200-240, 8 m for 240-250, 10 m for 250-254 and 20 m for 254-255, with a maximum of 700 m (dump_region.py on client_portal.dat 0x13000000).
- The user's RegionDesc parsed exactly (58,956/58,956 bytes). PartsMask 0x21F, TexMerge BaseTexSize 1024, 4 corner alpha maps (TCode 8), 1 side map (TCode 9), 3 road maps (RCodes 9, 10, 8), 33 TMTerrainDesc entries all with TexTiling=2, 32 TerrainTypes, 89 SceneTypes, 20 DayGroups, DayLength 7620 s, SkyDesc TickSize 0.8 and LightTickSize 15 (dump_region.py).
- Retail TexMerge, as decompiled: CopyAndTile tiles the 512^2 base texture tex_tiling (2) times into a 1024^2 per-palCode texture. Overlays then roads are merged sequentially as dst = (a*dst + (256-a)*src)>>8 with a = alpha-map byte (MergeTexture, acclient-AI-RE PORTAL/image/ImgTex.c:353; FillTempTexBuffer, CTexMerge.c:1370). ACViewer ignores TexTiling and uses the maskBlend3 approximation (Content/texture_clamp.fx).
- Alpha-map choice: idx = floor((uint32)(1379576222*palCode - 1372186442)*2.3283064e-10*count). Rotation steps code*=2, minus 15 if >=16. Found in ACViewer Physics/Common/TexMerge.cs FindTerrainAlpha/FindRoadAlpha and retail CTexMerge.c:173/242/265.
- palCode = 1<<28 | r1<<26 | r2<<24 | r3<<22 | r4<<20 | t1<<15 | t2<<10 | t3<<5 | t4, with corners SW, SE, NE, NW (ACViewer Physics/Common/LandblockStruct.cs GetPalCode).
- Scenery magic constants: 712977289, 1813693831, 1109124029, 2139937281 (scene pick); 1360117743, 1888038839 and 23399+j (frequency); 45773/72719 (displacement); 1870387557 and 402451965 (quadrant); 63127 (rotation); 32593 (scale). Found in ACViewer Physics/Common/Landblock.cs get_land_scenes and ObjectDesc.cs, and confirmed in retail CLandBlock.c:1321-1360 and region/Scene.c.
- Scenery filters: WeenieObj==0, noise<Freq, inside [0,192), not OnRoad (RoadWidth 5), cell has no building, MinSlope<=n.z<=MaxSlope, z snapped to the terrain plane, obj_within_block, scale=Min*(Max/Min)^rnd. Holtburg-adjacent block 0xA9B3 gives 142 survivors before the slope/building tests (scenery_check.py).
- AC polygons are CCW-front under the right-hand rule. 39,996 of 40,283 single-sided polygons in 3,000 drawing-only GfxObjs have cross(v1-v0,v2-v0) agreeing with stored vertex normals. SidesType histogram: 0 = 40,283; 1 = 6,113; 2 = 5 (winding_check.py).
- Godot ArrayMesh renders front faces clockwise (docs.godotengine.org arraymesh tutorial), so AC triangles must be reversed when building meshes.
- The user has no client_highres.dat. 2,284 of 7,221 SurfaceTextures (0x05) have Textures[0] missing from client_portal.dat, and none are fully missing (count_hires.py). This includes all landscape alpha maps, e.g. 0x05001371 -> [0x060073A7 missing, 0x06006D60 512^2 LSCAPE_ALPHA]. ACViewer TextureCache uses Textures[0] and falls back to a magenta texture.
- Part posing is flat, not hierarchical: part.origin = obj.origin + R(obj.q)*(animFrame.origin*scale); part.q = obj.q ⊗ animFrame.q. Sources: retail CPartArray.c Frame::combine/UpdateParts, ACViewer Physics/PartArray.cs UpdateParts and Physics/Animation/AFrame.cs Combine. The default placement frame is 0x65 (Resting), falling back to 0.
- Retail shows PartFrames[floor(frameNumber)] with no inter-frame interpolation (CSequence.c get_curr_animframe; ACViewer Physics/Animation/Sequence.cs GetCurrAnimFrame).
- DAT Frame quaternions are stored W,X,Y,Z (ACE.DatLoader/Entity/Frame.cs).
- Day group: idx = floor((uint32)(1782775218*(day+daysPerYear*year) - 1967253934)*2.3283064e-10*nGroups) (CSkyDesc.c CalcPresentDayGroup). ChanceOfOccur is unused.
- Lighting: sunVec = DirBright*(sinH cosP, cosH cosP, sinP) with linear lerp of all fields (CSkyDesc.c GetLighting). Terrain vertex colour = min(1, sunColor*max(0,n·sunVec) + ambColor*ambLevel) (CLandBlockStruct.c calc_lighting). Object world ambient = ambLevel + 0.2*|sunVec| (LScape.c set_landscape_lighting).
- Fog is linear range fog only, from MinWorldFog/MaxWorldFog/WorldFogColor lerped between keyframes (CSkyDesc.c GetWorldFog; doc 06-rendering.md §9). User data: night [0,400] m, dawn [90,800] m, day [150,2400] m.
- Sky objects are visible if Begin==End or Begin<t<End. rotation = lerp(BeginAngle,EndAngle). Frame = set_heading(replace.Rotate), then a global rotation about +Y by -rotation. Replace Luminosity, MaxBright and Transparent are lerped and multiplied by 0.01. Drawn camera-centred with depth test ALWAYS, z-writes off and zfar*4 (CSkyDesc.c GetSky; lscape/Sky.c CalcFrame/UseTime/Draw).
- Sky geometry measured from the user's DAT: dome 0x010015EE ±1050 m; stars 0x010015EF; cloud layer 0x01004C36 with radius about 14.3 km; sun quad 0x01001348 at x=+1050 with surface type Additive|Alpha (sky_extent.py).
- ACViewer has no sky, fog or time-of-day. It uses a fixed light direction -UnitZ and ambient 0.5 (Render/Buffer.cs:107-112).
- Mag-nus/acclient-AI-RE contains a PDB-named Hex-Rays decompile of acclient 11.4186. Its doc 13 states 0 rendering-core function changes in 11.6096, whose MD5 116d9a66... matches the user's acclient.exe.
- eriknihlen/OpenAC (MIT, .NET 10, Vulkan) implements the retail sky (SkyState.cs, SkyDescLoader.cs, SkyRenderer.cs), terrain blending with per-layer TexTiling (TerrainBlending.cs, terrain_modern.frag) and scenery (SceneryGenerator.cs). It credits Chorizite/WorldBuilder, which has no license file.
- Licenses: ACViewer is GPL-3.0; ACE is AGPL-3.0; ACE-World-16PY-Patches is AGPL-3.0; OpenAC is MIT; Chorizite/DatReaderWriter is MIT; deregtd/AC2D is GPL-2.0; the earlier prototype is AGPL-3.0; Chorizite/WorldBuilder has no license.
- Surface blending: Additive gives ONE/ONE; Alpha+Additive gives SRCALPHA/ONE; Alpha gives SRCALPHA/INVSRCALPHA. Clip maps use alpha test with ALPHAREF 200 (every textured clip map; **Erratum (wave 1):** this said "100 (palettised) or 200 (DXT)", but `s_256AlphaTestRef` = 100 needs `ImgTex::m_pPalette`, which palette-expanded textures never carry; `docs/textures.md` §5). Retail does not sort the alpha list (doc 06-rendering.md §10).
- **Erratum (wave 1):** JPEG (CUSTOM_RAW_JPEG) needs no R/B swap; retail decodes it as an ordinary JPEG (IJL_BGR into a D3D R8G8B8 surface) and ACViewer's swap only undoes its own GDI copy. CUSTOM_LSCAPE_R8G8B8 is stored R,G,B (`docs/textures.md` §1–2).

## RECOMMENDATIONS
- Implement terrain as one MeshInstance3D per landblock (8x8 cells, 2 triangles each, non-indexed, 384 vertices) with an unshaded Godot ShaderMaterial. Use a Texture2DArray of terrain textures (33 layers, 512^2) and one of alpha maps (8 layers, R8). Do the exact retail sequential merge c = a*c + (1-a)*T_layer(uv*TexTiling) for base, then overlays 0-2, then roads 0-1, with alpha UVs rotated per rot_r (r1 = (1-v,u), r2 = (1-u,1-v), r3 = (v,1-u)). Do not copy ACViewer's maskBlend3 or its 1x tiling.
- Precompute each cell's SurfaceInfo on the CPU (base layer, up to 3 overlay layer/alpha/rotation triples, up to 2 road alpha/rotation pairs, split bit) from palCode and cache it by palCode. Pass it to the shader as flat CUSTOM0..3 vertex attributes or a small per-landblock data texture.
- Use the bit-test split (s & 0x80000000) != 0 for SWtoNEcut. It matches the retail double comparison on every map cell.
- Convert coordinates as pos_g = (x, z, -y) and quat_g = Quaternion(qx, qz, -qy, qw), reading the DAT quaternion as W,X,Y,Z. Reverse every triangle's winding for Godot rather than making materials double-sided. Emit an extra reversed copy only for polygons with SidesType 1 or 2.
- Use a floating origin: place landblocks relative to the player's current landblock, as retail LScape::calc_frame does. The world spans about 49 km, which single-precision floats cannot handle without jitter.
- Write the texture loader to pick the first 0x06 id in SurfaceTexture.Textures that exists in client_portal.dat (or client_highres.dat if added later). Decode A8R8G8B8 as BGRA, R8G8B8 as BGR, LSCAPE_R8G8B8 as RGB, INDEX16/P8 via palette, DXT1/3/5 directly as BC formats, LSCAPE_ALPHA/A8 as R8, and RAW_JPEG as an ordinary JPEG. (**Erratum (wave 1):** this recommended "RAW_JPEG with an R/B swap"; see KEY FACTS.)
- Port scenery generation verbatim from Section 4 using uint32 wrapping arithmetic. Unit-test it against the 0xA9B3 numbers (177 candidates, 30 out of block, 5 on road, 142 survivors before slope/building tests) and against ACViewer's output if possible. Instance scenery with MultiMeshInstance3D keyed by (GfxObj id, texture/palette change set).
- Implement retail time-of-day exactly: CalcPresentDayGroup hash, GetTimeOfDay keyframe search (wrap ratio (t-b)/(1-b)), linear lerp of colours/brightness/heading/pitch, unnormalised sunVec, terrain vertex lighting formula, object ambient = ambLevel + 0.2*|sunVec|, and linear distance fog. Do these in custom shaders instead of relying on Godot's PBR lighting.
- Render the sky in a SubViewport with its own World3D. Its camera copies only the main camera's rotation and FOV. Show the result as the Environment background via a canvas layer. Implement GetSky visibility and rotation, replace-entry lerps (x0.01), TexVelocity UV scroll, and property bits 1/2/4/8. Leave weather (rain objects) optional.
- Assemble models with flat per-part frames from Setup.PlacementFrames[0x65 or 0] or the current animation frame, applying DefaultScale*objScale to mesh vertices in part space. Show PartFrames[floor(frame)] to match retail; slerping between frames can be an option. Start the motion-table state machine with Links followed by Cycles.
- For EnvCells, use Environment(0x0D).Cells[envCell.CellStructure] only (not all CellStructs), surfaces from envCell.Surfaces[poly.PosSurface], skip polygons with Stippling NoPos, and place both cell and statics with landblock-local frames. Use EnvCell.VisibleCells as a PVS for visibility toggling.
- Decide the licensing posture before copying code. ACViewer (GPL-3.0) and ACE (AGPL-3.0) would make the Godot project copyleft. OpenAC and Chorizite.DatReaderWriter are MIT (OpenAC's WorldBuilder-derived parts are unlicensed). Treat the decompiled acclient as a reference for algorithms and constants only.
- If the Godot project uses C#, consider Chorizite.DatReaderWriter (MIT) or a small custom reader. The DAT container is simple: header at 0x140, block-chained sectors, B-tree directory of 62 branches and up to 61 entries. The scratchpad Python reader (research/render/scripts/acdat.py) is about 100 lines.
- Look at the earlier prototype for reusable pipeline pieces. Its sky is an approximation, its docs mislabel axes and handedness, and it renders all materials double-sided.

## RISKS
- client_highres.dat is absent: about 32% of SurfaceTextures will use their lower-resolution variant. Retail end-of-retail installs normally include it, so visual parity with a full install is reduced unless it is obtained. It may be inside ac1install.exe; I did not inspect that installer.
- Decompiled acclient code (Mag-nus/acclient-AI-RE) is Turbine/WB IP despite the MIT label. Use it only to derive formulas and constants, and do not copy it into the project.
- Exact object lighting under D3D fixed-function (material ambient/diffuse/emissive from Surface.Luminosity/Diffuse, how DirBright scales the D3D light colour, the LScape::min_ambient value) was not fully traced. Terrain lighting is exact.
- The retail sky draws camera-centred meshes with depth test ALWAYS. Godot 4 cannot reproduce that inside the main opaque pass, so a SubViewport plus background-canvas composite is needed and its cost and ordering should be prototyped.
- I did not verify whether Godot's Environment depth fog matches D3D linear range fog exactly. Fog in custom shaders is safer.
- Water: I believe retail renders water as ordinary terrain texture (types 16-20) with no animation or reflection. This was not verified visually.
- I did not determine whether TerrainTex MinVertBright/MaxVertBright/Saturate/Hue (values 90-100, 70-90, 90-150) are used by retail. They may add per-vertex colour variation.
- Retail per-block normals are not shared across landblock edges, which produces lighting seams. That is faithful, but users may perceive it as a bug.
- Retail uses lower-detail landblocks at distance (Generate cellScale, TransAdjust) and a mid_width grid. Draw distance and LOD policy in Godot must be designed; daytime fog reaches 2,400 m (about 12.5 landblocks).
- Particles and PhysicsScripts (Setup.DefaultScript, sky PES ids), animation hooks, Setup lights and baked EnvCell static lighting were only surveyed, not specified in detail.
- OpenAC's sky code deviates slightly from retail (shortest-arc heading lerp, wrap-around visibility) and its WorldBuilder-derived code has unclear provenance and licensing.
- I did not run ACViewer (no .NET SDK was available), so no image comparisons were possible. All formulas were cross-checked in source and by Python against the retail DATs.

## SOURCES
- https://github.com/ACEmulator/ACViewer (GPL-3.0, HEAD c5c54fb) — ACViewer/Physics/Common/LandblockStruct.cs, Landblock.cs, ObjectDesc.cs, LandDefs.cs, TexMerge.cs, TextureMergeInfo.cs, LandSurf.cs, EnvCell.cs, BuildingObj.cs, LScape.cs; ACViewer/Physics/PartArray.cs, PhysicsObj.cs; ACViewer/Physics/Animation/AFrame.cs, Sequence.cs, AnimSequenceNode.cs, MotionTable.cs; ACViewer/Render/R_Landblock.cs, TerrainBatch.cs, TerrainBatchDraw.cs, Buffer.cs, TextureCache.cs, TextureAtlas.cs, R_EnvCell.cs, R_CellStruct.cs, InstanceBatch.cs, GfxObjInstance_Shared.cs, Camera.cs; ACViewer/Content/texture_clamp.fx; ACViewer/Model/LandVertex.cs, Setup.cs, GfxObj.cs, Polygon.cs, ObjDesc.cs, PaletteChanges.cs; ACViewer/Extensions/*.cs; ACViewer/ACE.Server/Entity/LandblockMesh.cs
- https://github.com/ACEmulator/ACE (AGPL-3.0, HEAD 7715c05) — Source/ACE.Server/Physics/Common/{LandblockStruct,Landblock,ObjectDesc,LandDefs}.cs; Source/ACE.DatLoader/{DatDatabase,DatDatabaseHeader,DatDirectory,DatDirectoryHeader,DatFile,DatReader,BinaryReaderExtensions,UnpackableExtensions}.cs; Source/ACE.DatLoader/FileTypes/{CellLandblock,LandblockInfo,RegionDesc,Scene,SetupModel,GfxObj,Animation,MotionTable,ClothingTable,PaletteSet,Palette,Surface,SurfaceTexture,Texture,EnvCell,Environment}.cs; Source/ACE.DatLoader/Entity/*.cs; Source/ACE.Entity/Enum/{CullMode,StipplingType,SurfaceType,SurfacePixelFormat,GfxObjFlags,SetupFlags,EnvCellFlags,PortalFlags,MotionCommand,CommandMasks,Placement}.cs; Source/ACE.Common/DerethDateTime.cs
- https://github.com/Mag-nus/acclient-AI-RE — '2013-09 11.4186/docs/06-rendering.md', '2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md', acclient-src/src/PORTAL/cregion/CSkyDesc.c, CTexMerge.c; PORTAL/lscape/Sky.c, LScape.c; PORTAL/ccell/CLandBlockStruct.c, CLandBlock.c; PORTAL/region/Scene.c; PORTAL/canim/CSequence.c; PORTAL/cphysobj/CPartArray.c; PORTAL/image/ImgTex.c
- https://github.com/eriknihlen/OpenAC (MIT, HEAD 1ee5a08) — src/AcDream.Core/World/{SkyState,SkyDescLoader,SkyDayGroupSelector,SceneryGenerator}.cs; src/AcDream.Core/Terrain/TerrainBlending.cs; src/AcDream.Core/Rendering/Wb/SceneryHelpers.cs; src/AcDream.App/Rendering/Sky/SkyRenderer.cs; src/AcDream.App/Rendering/Shaders/{sky.vert,sky.frag,terrain_modern.vert,terrain_modern.frag}; src/AcDream.App/Rendering/TerrainTextureTilingTable.cs, TerrainAtlas.cs; README.md
- https://github.com/Chorizite/WorldBuilder (no license file)
- https://github.com/Chorizite/DatReaderWriter (MIT)
- https://github.com/deregtd/AC2D (GPL-2.0)
- https://github.com/derandark/DungeonViewerAC (GPL-3.0)
- https://github.com/ACEmulator/ACE-World-16PY-Patches (AGPL-3.0)
- https://github.com/merklejerk/holtburger (AGPL-3.0)
- https://docs.godotengine.org/en/stable/tutorials/3d/procedural_geometry/arraymesh.html (Godot clockwise front-face winding)
- Local read-only analysis: <repo>\client\client_portal.dat and client_cell_1.dat via scratchpad scripts in <scratch>\research\render\scripts\ (acdat.py, dump_region.py, dump_textures.py, count_hires.py, split_check.py, scenery_check.py, winding_check.py, sky_extent.py); outputs region_dump.txt, tex_dump.txt in research\render\