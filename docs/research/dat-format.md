# AC1 .dat formats: a reader/exporter spec checked against the user's own files

## 0. How this was checked
I didn't stop at reading other people's code. I wrote a small, read-only Python reader:
- `<scratch>\research\datspec\scripts\acdat.py` handles the container: header, B-tree and block chains.
- `...\scripts\acparse.py` holds the struct parsers.

I ran them over the user's three EoR dats. Every layout marked **[V]** below consumed exactly the stored byte count:
- **client_portal.dat:** all 15,318 GfxObj, 5,935 Setup, 772 Environment, 2,066 Animation, 436 MotionTable, 179 Scene, 2,681 PaletteSet, 1,917 Clothing, 4,131 DegradeInfo, 786 Wave, 190 SoundTable, 2,051 ParticleEmitter, 4,248 PhysicsScript, 164 PhysicsScriptTable, the Region, all Palettes, Surfaces and SurfaceTextures, and every RenderSurface header.
- **client_cell_1.dat:** all 65,025 LandBlocks, all 5,346 LandBlockInfos, and a 1-in-7 sample of 105,005 EnvCells.
- **client_local_English.dat:** all 15 StringTables.

Iteration records confirm end-of-retail data: portal 2072, cell 982, local 994.

## 1. Reference readers (state checked 2026-10-09)
| Repo | Lang | License | Last push | Notes |
|---|---|---|---|---|
| ACEmulator/ACE `Source/ACE.DatLoader` | C# | **AGPL-3.0** | 2026-10-08 (commit 7715c05) | Canonical. Read-only. The client-logic ports live in `Source/ACE.Server/Physics/Common/*` |
| Chorizite/DatReaderWriter | C# | **MIT** | 2026-08-29 (release/2.1.9) | Reads and writes. NuGet `Chorizite.DatReaderWriter`. Targets net8.0, netstandard2.0, net48. **`DatReaderWriter/dats.xml`** (4,300 lines) is a machine-readable schema, and the C# is generated from it. It describes itself as "Heavily based off of ACE.DatReader FileTypes". RenderMaterial is unsupported. BSP parsing was fixed in #71 |
| ACEmulator/ACViewer | C# MonoGame 3.8 | **GPL-3.0** | 2026-08-13 | Pulls in ACE as a submodule. Its renderer is the best reference: `Render/TextureCache.cs`, `Model/Polygon.cs`, `Content/texture.fx` (technique LandscapeSinglePass), `Render/TerrainBatch*.cs`. Uses AssimpNet and FbxSharpie for export |
| amoeba/asheron-rs | Rust | MIT | 2026-09-25 | `crates/asheron-rs/src/dat/reader/*` |
| enknamel/acswarm | Rust | GPL-3.0 | 2026-10-08 | `crates/ac-dat`, `crates/ac-formats`, `docs/subsystems/dat.md`. Its header table swaps the portal and cell values of data_subset and engine version (see §2) |
| dereth-network/dereth | Rust | MIT | 2026-10-09 | `core/assets` |
| merklejerk/holtburger | Rust | AGPL-3.0 | 2026-10-03 | `holtburger-dat` |
| immortalbob/Landblock | Python | **no license** | 2026-08 | `landblock/dat.py`. Also reads the pre-2005 container (header at 0x12C, 12-byte directory entries) |
| jkolb/Asheron | Swift | MIT | 2019 | |
| OptimShi/Marshmallow | C# | GPL-3.0 | 2026-04 | Dat viewer |
| derandark/PhatAC, DungeonViewerAC | C++ | GPL-3.0 | 2017 | PhatSDK, the ancestor of GDLE |

Client projects I noticed but did not dig into (another investigator's topic): eriknihlen/OpenAC (.NET 10, MIT), Y0oshi/MacAC (Vulkan, MIT), CrimsonMage/Vitaeum_AC, lkasser/Decal-ACUnreal (refers to a "UE5 AC client"), Vanquish-6/ACME WorldBuilder (MIT).

## 2. Container
**Header.** All values are little-endian.

The first 0x140 bytes:
- `0x000`: char[256] version string. All zero in the user's files.
- `0x100`: 64-byte transaction area.

Starting at 0x140 (acclient's DiskFileInfo_t):

| off | field | portal | cell | local |
|---|---|---|---|---|
| 0x140 | u32 magic (FileType) | 0x5442 "BT" | same | same |
| 0x144 | u32 BlockSize | **1024** | **256** | 1024 |
| 0x148 | u32 FileSize | 926,941,184 | 348,127,232 | 1,048,576 |
| 0x14C | u32 DataSet | 1 | 2 | 3 |
| 0x150 | u32 DataSubset | 0 | 1 | 1 |
| 0x154/158/15C | u32 FreeHead, FreeTail, FreeCount | | | |
| 0x160 | u32 BTree root offset | 0x232D400 | 0x1B77400 | 0x2B000 |
| 0x164/168 | u32 NewLRU, OldLRU | 0 | 0 | 0 |
| 0x16C | u32 UseLRU | 0xCDCDCDCD (uninitialized fill; ACE tests ==1) | same | same |
| 0x170 | u32 MasterMapID | 0x25000000 | 0 | 0 |
| 0x174 | u32 EnginePackVersion | 110 | 22 | 110 |
| 0x178 | u32 GamePackVersion | 0 | 0 | 0 |
| 0x17C | u8[16] VersionMajor (GUID) | d2d7a734… in all three | | |
| 0x18C | u32 VersionMinor | 6657 | | |

The header ends at 0x190.

**Blocks.** Each block is `[u32 next][BlockSize-4 payload]`, and next=0 ends the chain. To read a file, read `size` bytes of payload starting at the first block. Free blocks carry the 0x80000000 flag (DRW `BaseBlockAllocator`), so mask with 0x7FFFFFFF.

**B-tree node.** A node is a block-chained object of 1,716 bytes:
- `u32 branch[62]`
- `u32 count` (at most 61)
- `Entry[61]`, 24 bytes each

A node is a leaf if `branch[0]==0`. Otherwise it has count+1 children, and entries are sorted by id.
- **Observed:** portal has 2,231 nodes (depth 2) and 79,694 entries. Cell has 26,657 nodes (depth 3) and 805,348 entries. Local has 4 nodes and 118 entries.
- **How ACE uses it:** ACE walks the whole tree once into a `Dictionary<uint,DatFile>` (DatDatabase.cs, DatDirectory.cs).

**Entry (24 bytes):**
- `u16 flags`: bit0 = compressed, payload `[u32 rawSize][zlib]` (DRW). ACE reads flags plus version as a single u32 "BitFlags".
- `u16 version`: observed values 1, 2, 3.
- `u32 id`, `u32 offset` (first block), `u32 size`.
- `u32 date`: unix time (portal dates run 2005-05-27 to 2015-06-12).
- `i32 iteration`.

All 885,160 entries have flags=0. **Nothing is compressed in the EoR dats.**

**Iteration file `0xFFFF0001`** (present in every dat): `i32 total`, then `(i32 consecutive, i32 start)` pairs. The values here are (2072,-2072,1), (982,-982,1) and (994,-994,1).

**Primitives:**
- **CompressedUInt** ("SmartArray" count): if `b0&0x80==0` it is 1 byte. If `b0&0x40==0` it is 2 bytes, `((b0&0x7F)<<8)|b1`. Otherwise it is 4 bytes, `(((b0&0x3F)<<8|b1)<<16)|u16`.
- **PString:** `u16 len` + cp1252 bytes, with `len==0xFFFF` meaning a u32 length follows. Usually followed by `align4`.
- **align4** is relative to the **start of the file's buffer**, not the dat offset.
- **UnicodeString:** CompressedUInt char count + UTF-16LE.
- **DataIdOfKnownType:** u16; if bit 0x8000 is set, it is `((v&0x3FFF)<<16 | u16) + base`.
- **PackedHashTable:** `u16 count, u16 buckets`, then entries.
- **HashTable** (local dat): `u8 bucketIdx, CompressedUInt count`.
- **Frame** (28 bytes): `vec3 origin; f32 qw,qx,qy,qz`. **W comes first.**
- **Sphere** = vec3 + f32 r. **Plane** = vec3 N + f32 D.

## 3. File-ID map
**Cell dat ids:** `0xXXYYFFFF` = LandBlock (65,025, the full 255×255 grid), `0xXXYYFFFE` = LandBlockInfo (5,346), `0xXXYY0100+` = EnvCell (734,977, low word 0x100–0x1174). Outdoor cell ids 0x0001–0x0040 are implicit (8×8 per block) and not stored.

**Portal dat:** type = `id>>24`. Each entry gives the type, its ACE name, and the count in the user's file.
- 0x01 GfxObj 15,318
- 0x02 Setup 5,935
- 0x03 Animation 2,066
- 0x04 Palette 4,521
- 0x05 SurfaceTexture 7,221
- 0x06 (to 0x07) RenderSurface ("Texture") 20,684, 606 MB
- 0x08 Surface 6,152
- 0x09 MotionTable 436
- 0x0A Wave 786
- 0x0D Environment 772
- 0x0E singleton tables (14):
  - CharGen 0x0E000002
  - Vital(2ndAttr) 0x0E000003
  - Skill 0x0E000004
  - ChatPose 0x0E000007
  - ObjectHierarchy 0x0E00000D
  - SpellTable 0x0E00000E (1.48 MB)
  - SpellComponents 0x0E00000F
  - Xp 0x0E000018
  - BadData 0x0E00001A
  - Contract 0x0E00001D
  - Taboo 0x0E00001E
  - NameFilter 0x0E000020
  - QualityFilter 0x0E010001/2
- 0x0F PaletteSet 2,681
- 0x10 ClothingTable 1,917
- 0x11 GfxObjDegradeInfo 4,131
- 0x12 Scene 179
- 0x13 Region 1 (0x13000000)
- 0x14 KeyMap 2
- 0x15 RenderTexture 2
- 0x16/17/18 RenderMaterial / MaterialModifier / MaterialInstance, 1 each
- 0x20 SoundTable 190
- 0x22 EnumMapper 40
- 0x25 DidMapper 22
- 0x26 ActionMap 1
- 0x27 DualDidMapper 5
- 0x30 CombatTable 71
- 0x31 String 28
- 0x32 ParticleEmitterInfo 2,051
- 0x33 PhysicsScript 4,248
- 0x34 PhysicsScriptTable 164
- 0x39 MasterProperty 1
- 0x40 Font 49
- 0x78 DbProperties 2

The mapping comes from `ACE.DatLoader/DatFile.cs GetFileType()` and `DatFileType.cs`.

**Server-only types that are absent here (verified):** WeenieDefaults (0x0000xxxx), Treasure 0x0E000011, Craft 0x0E000019, Quests 0x0E00001B, GameEvents 0x0E00001C, ItemMutation 0x38. So monsters, NPCs, items, spawns and loot must come from an ACE world DB.

Most portal objects start with `u32 id`. The exceptions are **Surface (0x08) and LanguageInfo (0x41), which have no id**. SurfaceTexture, RenderSurface and RenderTexture have `u32 id, u32 DataCategory`.

## 4. Geometry structures [V]
**VertexArray** (`Entity/CVertexArray.cs`, `SWVertex.cs`):
- `i32 vertexType` (always 1), `u32 n`.
- n × `{u16 index; u16 numUV; vec3 pos; vec3 normal; numUV×(f32 u, f32 v)}`.

**Polygon** (`Entity/Polygon.cs`):
- `u8 numPts; u8 stippling; i32 sidesType; i16 posSurface; i16 negSurface; i16 vertexIds[n];`
- then `u8 posUV[n]` if `!(stip&0x4)`,
- then `u8 negUV[n]` if `sides==2 && !(stip&0x8)`.

`sidesType` meanings (ACE's CullMode names are misleading):
- 0: single-sided (714,822 in GfxObjs)
- 1: double-sided, same surface (154,479)
- 2: separate back face (11)

`posSurface` indexes the owner's surface list. `posUV[i]` indexes `vertex[vertexIds[i]].UVs`. Index bounds were checked across all 869,312 draw polygons with zero violations.

**Triangulation and winding:** each polygon is a convex fan (v0, v[i-1], v[i]); ACViewer `Model/Polygon.cs` builds unique render vertices keyed by (vertexId, uvIdx). Front faces are **counter-clockwise viewed from the normal side, in right-handed Z-up**: measured 68,097 CCW vs 593 CW on 4,000 GfxObjs.

**BSP** (`Entity/BSPNode.cs`, `BSPLeaf.cs`, `BSPPortal.cs`; DRW `Types/*BSPNode.cs`): a u32 tag, ASCII stored reversed.
- `BPnn`/`BPIn`: positive child only. `BpIN`/`BpnN`: negative child only. `BPIN`/`BPnN`: both.
- `BpIn`, `BPOL`, `BPFL`: **no children**. These are frequent (108,920 / 107,840 / 3,168 seen). A parser that rejects unknown tags will fail on 14,304 of the 15,318 GfxObjs.
- **Drawing tree.** Internal node: plane, children, sphere, `u32 n, u16 polys[n]`. `LEAF`: `i32 idx`. `PORT`: plane, pos, neg, sphere, `u32 nPoly, u32 nPortal, u16 polys[], nPortal×(i16 portalIdx, i16 polyId)`.
- **Physics tree.** Internal node: plane, children, sphere. `LEAF`: `i32 idx, i32 solid, sphere, u32 n, u16[n]`.
- **Cell tree.** Plane and children only. `LEAF` is `i32`.

**GfxObj 0x01** (`FileTypes/GfxObj.cs`):
1. `id; u32 flags` (1 physics, 2 drawing, 8 degrade);
2. `CompressedUInt nSurf; u32 surfaceIds[]` (0x08);
3. VertexArray;
4. if physics: `CompressedUInt n; n×(u16 key, Polygon); PhysicsBSP`;
5. `vec3 sortCenter`;
6. if drawing: `CompressedUInt n; n×(u16 key, Polygon); DrawingBSP`;
7. if flag 8: `u32 degradeId` (0x11).

Flags seen: 2 (10,546), 10, 3, 11.

**Setup 0x02** (`FileTypes/SetupModel.cs`):
1. `id; u32 flags` (1 hasParent, 2 defaultScale, 4 freeHeading, 8 physicsBSP); `u32 nParts; u32 partIds[n]`;
2. `[u32 parent[n]]; [vec3 scale[n]]`;
3. `i32 n × (i32 key, i32 partId, Frame)` holding locations;
4. the same layout again for connection points;
5. `i32 n × (i32 placementKey, n×Frame per part, u32 nHooks, hooks)`;
6. `u32 n × CylSphere(vec3, r, h)`; `u32 n × Sphere`;
7. `f32 height, radius, stepUp, stepDown`; `Sphere sorting, selection`;
8. `i32 n × (i32 key, Frame, u32 color, f32 intensity, falloff, cone)`;
9. `u32 defaultAnim, defaultScript, motionTable, soundTable, scriptTable`.

Rest pose is PlacementFrames[101 Resting], falling back to [0 Default] (ACViewer `Model/Setup.cs`). A part's world frame is `objFrame ∘ frame[i]`, flat and not hierarchical (ACE `Physics/PartArray.cs UpdateParts`).

**Animation 0x03:**
- `id; u32 flags` (1 = posFrames); `u32 nParts; u32 nFrames; [Frame pos[nFrames]]`;
- nFrames × `(Frame[nParts], u32 nHooks, hooks)`.

Hook format is `u32 type, i32 dir` + payload. Payload bytes by type:
- 0 bytes: 4 (AnimationDone), 17 (DefaultScript)
- 4 bytes: 1, 2, 6, 14, 15, 16, 18, 25
- 8 bytes: 12, 19, 23
- 12 bytes: 8, 10, 20, 22, 24
- 16 bytes: 7, 9, 11, 21
- 28 bytes: 3 (Attack)
- 40 bytes: 13, 26
- variable: 5 is `u16 part + DataIdOfKnownType(0x01000000)`

Source: `Entity/AnimationHooks/*`.

**MotionTable 0x09:**
- `id; u32 defaultStyle; u32 n×(u32 style, u32 motion)`;
- `u32 n×(u32 key, MotionData)` cycles, then the same for modifiers;
- `u32 n×(u32 key, u32 m×(u32 motion, MotionData))` links.

MotionData: `u8 nAnims, u8 bits, u8 flags, align4, nAnims×(u32 animId, i32 lo, i32 hi(-1=end), f32 fps)`, then `[vec3 vel if 1] [vec3 omega if 2]`.

## 5. Materials and textures [V]
**Surface 0x08** (no id):
- `u32 type`. Flags: 1 solid, 2 image, 4 clipmap, 0x10 translucent, 0x20 diffuse, 0x40 luminous, 0x100 alpha, 0x200 invAlpha, 0x10000 additive, 0x20000 detail.
- If `type&6`: `u32 surfaceTextureId(0x05), u32 paletteId`. Otherwise `u32 ARGB`.
- `f32 translucency, luminosity, diffuse`.

**SurfaceTexture 0x05:** `id; u32 dataCat(0); u8 texType(2=2D); i32 n; u32 renderSurfaceIds[n]`. When n=2, **[0] is the high-res copy that lives in client_highres.dat, which the user does not have.** 2,284 of 10,245 references are unresolvable. ACViewer takes `Textures[0]` and shows magenta when it is missing, so **choose the first id that exists** (in practice the last one).

**RenderSurface 0x06** (`FileTypes/Texture.cs`):
- `id; u32 dataCat; i32 w; i32 h; u32 fmt; i32 len; u8 data[len]`;
- `[u32 defaultPaletteId]` if INDEX16 or P8.

There is one mip only. `len == w*h*bpp` exactly for every file.

Formats seen (count, then decode):
- A8R8G8B8 (21): 12,984. Bytes B,G,R,A.
- INDEX16 (101): 4,182. u16 index into a Palette.
- DXT1 (0x31545844): 1,971. Raw BC blocks.
- R8G8B8 (20): 1,190. Bytes B,G,R.
- DXT5: 127.
- A8 (28): 86.
- RAW_JPEG (500): 79. A whole JPEG file.
- CUSTOM_LSCAPE_R8G8B8 (243): 33. Bytes R,G,B.
- CUSTOM_LSCAPE_ALPHA (244): 16. 8-bit terrain alpha maps.
- P8 (41): 6.
- DXT3: 5.
- R5G6B5: 3.
- A4R4G4B4: 2.

**ClipMap rule** (ACViewer `IndexToColor`): for a Base1ClipMap surface, INDEX16 index < 8 is fully transparent.

**Palette 0x04:** `id; i32 n (always 2048); u32 ARGB[n]`.

**PaletteSet 0x0F:** `id; i32 n; u32 paletteIds[n]`. The index is `(int)((n-1e-6)*hue)`.

**ClothingTable 0x10:**
- `id`, then PackedHashTable<u32 setupId, `u32 n×{u32 partIdx, u32 gfxObjId, u32 m×(u32 oldTex, u32 newTex)}`>;
- then PackedHashTable<u32 palEffect, `{u32 icon, u32 n×{u32 k×(u32 offset, u32 numColors), u32 paletteSetId}}`>.

ACViewer applies offset and numColors directly as palette indices.

## 6. Interiors [V]
**Environment 0x0D:** `id; u32 n × (u32 key, CellStruct)`. Each file is a library of room shapes, not a landblock.

**CellStruct:**
1. `u32 nPoly, nPhysPoly, nPortal`; VertexArray;
2. `nPoly×(u16, Polygon)`; `u16 portalPolyIds[nPortal]`; align4;
3. CellBSP;
4. `nPhysPoly×(u16, Polygon)`; PhysicsBSP;
5. `u32 hasDraw`; `[DrawingBSP]`; align4.

**EnvCell** (cell dat):
1. `id; u32 flags` (1 seenOutside, 2 staticObjs, 8 restriction); `u32 id again`;
2. `u8 nSurf; u8 nPortal; u16 nVisible; u16 surf[n]` (OR with 0x08000000); `u16 envId` (OR with 0x0D000000); `u16 cellStructKey`;
3. `Frame pos` (landblock-local);
4. `nPortal×(u16 flags, u16 polyId, u16 otherCell (0xFFFF = outside), u16 otherPortal)`;
5. `u16 visible[n]`;
6. `[u32 n × Stab(u32 id, Frame)]`; `[u32 restrictionObj]`.

Every EnvCell→CellStruct reference in Holtburg resolved.

## 7. Landscape [V]
**LandBlock** (`FileTypes/CellLandblock.cs`), 252 bytes: `id; u32 hasObjects` (1 exactly when an LBI exists); `u16 terrain[81]; u8 height[81]; align4`.
- **Index:** `x*9+y`, with x = east and y = north.
- **Terrain word:** bits 0–1 road; bits 2–6 terrain type (0–31); bits 7–10 unused (verified always 0); bits 11–15 scene-type index.
- **Height:** `z = Region.LandHeightTable[h]`.
- **Vertex position:** `(x*24, y*24, z)`; world origin of block XX,YY is `(XX*192, YY*192)`.

The cell split is **pseudo-random** (ACE `Physics/Common/LandblockStruct.cs ConstructPolygons`). With gx = XX*8+x and gy = YY*8+y:

`s = (u32)(gy*(214614067*gx + 1813693831) - 1109124029*gx - 1369149221)`

If `s*2^-32 < 0.5` the cell is cut SE–NW; otherwise SW–NE.

**LandBlockInfo:**
1. `id; u32 numCells`. **This is unreliable:** it differs from the actual EnvCell count in 38 of 5,346 blocks, so enumerate cells by id range.
2. `u32 n × Stab` (GfxObj or Setup);
3. `u16 nBld; u16 packMask`;
4. nBld × `{u32 modelId, Frame, u32 numLeaves, u32 n × {u16 flags, u16 otherCell, u16 otherPortal, u16 k, u16 stabs[k], align4}}`;
5. `[PackedHashTable<u32,u32> restrictions if packMask&1]`.

Holtburg (A9B4) has 114 objects, 12 buildings and 138 cells.

**Region 0x13000000** (`FileTypes/RegionDesc.cs`, 58,956 bytes):
1. `id, u32 number(1), u32 version(3), PString "Dereth", align`.
2. **LandDefs:** `i32 255, i32 255, f32 24.0, i32 8, i32 1, f32 maxObjH 200, f32 skyH 1000, f32 roadW 5, f32 LandHeightTable[256]`. The table runs 0→700 m: 200 steps of 2, 40 of 4, 10 of 8, 4 of 10, 1 of 20.
3. **GameTime:** `f64 3600, u32 10, f32 dayLen 7620, u32 360, PString "P.Y.", align`; then 16 TimeOfDay `{f32, u32 night, PString, align}`; 6 weekday PStrings (each aligned); 12 Season `{u32, PString, align}`.
4. `u32 partsMask` = 0x21F.
5. If 0x10, **Sky:** `f64, f64, align`, then 20 DayGroups. Each is `{f32 chance, PString, align, u32×SkyObject(6 f32, 3 u32, align), u32×SkyTimeOfDay(f32 begin, dirBright, dirHeading, dirPitch, u32 dirColor, f32 ambBright, u32 ambColor, f32 minFog, maxFog, u32 fogColor, fogMode, align, u32×Replace(u32, u32, 4 f32, align))}`.
6. If 0x01, **Sound:** 37 × `{u32 stb, u32 n×(u32, 4 f32)}`.
7. If 0x02, **Scenes:** 89 SceneTypes × `{u32 stbIdx, u32 n×u32 sceneId}`.
8. **TerrainDesc** (always present): 32 × `{PString name, align, u32 color, u32 32×sceneTypeIdx}`, then `u32 landSurfType(0)`.
9. **TexMerge:** `u32 baseTexSize 1024`; 4 corner alpha maps `{u32 tcode=8, u32 0x05 id}`; 1 side map `{tcode=9}`; 3 road maps `{u32 rcode, u32 id}`; 33 TMTerrainDesc `{u32 type, u32 texGID, u32 tiling, 6×u32 vertex bright/sat/hue, u32 detailTiling, u32 detailTexGID}`.
10. If 0x200, RegionMisc: 6 u32.

Terrain textures are 512² A8R8G8B8. Alpha maps are 512² LSCAPE_ALPHA.

**Terrain texturing** (ACE `Physics/Common/TexMerge.cs`, `LandSurf.cs`, `LandblockStruct.GetPalCode`):
- palCode = `t1<<15|t2<<10|t3<<5|t4 | r1<<26|r2<<24|r3<<22|r4<<20 | 1<<28` over the SW, SE, NE, NW corners.
- Result: a base texture, up to 3 rotated corner/side alpha overlays, and up to 2 road overlays.
- ACViewer does this on the GPU (`Content/texture.fx` LandscapeSinglePass, Texture2DArray `xOverlays`/`xAlphas`). It ports directly to a Godot `sampler2DArray` shader.

**Scene 0x12:** `id; u32 n × ObjectDesc` (76 bytes): `u32 objId, Frame, f32 freq, dispX, dispY, minScale, maxScale, maxRot, minSlope, maxSlope, u32 align, orient, weenieObj`.

**Scenery is procedural, not stored** (ACE `Physics/Common/Landblock.cs get_land_scenes` + `ObjectDesc.cs`):
1. For each of the 81 vertices: `sceneInfo = TerrainTypes[type].SceneTypes[terrain>>11]`.
2. `idx = (u32)(gy*(712977289*gx+1813693831) - 1109124029*gx + 2139937281) * 2^-32 * count`.
3. For each object j, place it if `(u32)(-1109124029*gx + 1813693831*gy - (1360117743*gx*gy+1888038839)*(23399+j)) * 2^-32 < freq` and `weenieObj==0`.
4. Then displace, reject it if it lands on a road, outside the block, in a building cell, or on a failing slope, and finally rotate and scale.

## 8. Other [V]
- **Wave 0x0A:** `id, i32 hdrSize, i32 dataSize, WAVEFORMATEX hdr, data`. 785 are PCM, 1 is MP3 (tag 0x55).
- **SoundTable 0x20:** `id, u32, u32 n×{u32 wave, f32 prio, prob, vol}, PackedHashTable<u32, {u32 n×same, u32}>`.
- **PhysicsScript 0x33:** `id, u32 n×(f64 t, Hook)`.
- **PhysicsScriptTable 0x34:** `id, u32 n×(u32 type, u32 m×(f32 mod, u32 scriptId))`.
- **ParticleEmitterInfo 0x32:** fixed-size; field list in `FileTypes/ParticleEmitterInfo.cs`.
- **DegradeInfo 0x11:** `id, u32 n×(u32 gfx, u32 mode, f32 min, ideal, max)`.

## 9. client_local_English.dat
It holds 118 files:
- **101 LayoutDesc** (0x21000000–75, 265 KB): the UI element trees. DRW notes the structure is incomplete.
- **15 StringTables** (0x23000001–10, 6,899 strings): UI labels, option help, emote text, credits (…08/09), keymap template (…07), character titles (…0E), network errors (…10).
- **LanguageInfo 0x41000000** (166 bytes, no id): number formatting and IME settings.

StringTable layout, validated against DRW:
1. `id, u32 lang(1), u8 bucketIdx, CompressedUInt n`;
2. n × `{u32 key, u32 dataId(0), u32 k × UnicodeString, u32 v, u32 vars[v], u8 flag}`.

ACE reads the dataId slot as two u16 counts. That gives the same bytes only because the value is always 0.

## 10. Coordinate conversion for Godot
- AC is right-handed Z-up: X east, Y north, units in metres.
- Godot position: `(x, z, -y)`. This preserves handedness; apply the same basis change to the (w,x,y,z) quaternions.
- AC front faces are CCW. Godot 4 documents clockwise front faces, so reverse the index order.

## KEY FACTS
- The dat header sits at absolute 0x140 after 256 zero bytes and a 64-byte transaction area. Magic is 0x5442 and the header runs to 0x190. Source: ACE DatDatabase.cs DAT_HEADER_OFFSET plus DRW Lib/IO/DatHeader.cs; read from the user's three files.
- Block size is 1024 in portal and local but 256 in client_cell_1.dat. Each block is [u32 next][payload], with next=0 ending the chain. Verified on the user's files and in ACE DatReader.cs.
- A B-tree node is u32 branch[62] + u32 count + 61 × 24-byte entries; it is a leaf if branch[0]==0. Entry = u16 flags (bit0 zlib-compressed), u16 version, u32 id, u32 offset, u32 size, u32 unix date, i32 iteration. Sources: DRW DatBTreeNode.cs and DatBTreeFile.cs; ACE DatDirectoryHeader.cs.
- All 885,160 directory entries in the user's dats have flags=0, so nothing is compressed. Verified by the Python walk.
- Entry counts: portal 79,694 (2,231 nodes, depth 2), cell 805,348 (26,657 nodes, depth 3: 65,025 LandBlocks, 5,346 LBIs, 734,977 EnvCells), local 118. Verified.
- Iteration file 0xFFFF0001 reads portal 2072, cell 982, local 994, matching end-of-retail. Verified; ACE DatDatabase.cs comments agree.
- Surface (0x08) and LanguageInfo (0x41) have no leading id. SurfaceTexture, RenderSurface and RenderTexture have id + DataCategory. Sources: DRW dats.xml DBObjHeaderFlags and ACE Surface.cs; layouts consumed exactly on all 6,152 Surfaces.
- RenderSurface formats in the user's portal: A8R8G8B8 12,984; INDEX16 4,182; DXT1 1,971; R8G8B8 1,190; DXT5 127; A8 86; RAW_JPEG 79; LSCAPE_R8G8B8 33; LSCAPE_ALPHA 16; P8 6; DXT3 5; R5G6B5 3; A4R4G4B4 2. Every file is a single mip with len==w*h*bpp. Verified.
- All 4,521 palettes have exactly 2048 ARGB entries. Verified.
- client_highres.dat is absent. 2,284 of 10,245 SurfaceTexture references (always element [0] of 2-element lists) do not resolve in portal.dat; element [1] does. Verified. ACViewer TextureCache.cs uses Textures[0] and draws magenta when it cannot find it.
- BSP node tags BpIn, BPOL and BPFL are childless nodes and are common (107,840 / 108,920 / 3,168 occurrences). A parser that rejects them fails on 14,304 GfxObjs. Sources: DRW Enums/BSPNodeType.cs and DrawingBSPNode.cs default branch; verified.
- Every GfxObj (15,318), Setup (5,935), Environment (772), Animation, MotionTable, Scene, PaletteSet, Clothing, DegradeInfo, Wave, SoundTable, ParticleEmitter and PhysicsScript(Table) parses byte-exact with the documented layouts. Verified by the scratch scripts acdat.py and acparse.py.
- Polygon sidesType 0 = single-sided (714,822), 1 = double-sided with the same surface (154,479), 2 = separate back face (11). Verified; ACE Entity/Polygon.cs and ACE.Entity/Enum/CullMode.cs.
- AC polygon fans are counter-clockwise when viewed from the vertex-normal side in right-handed Z-up (68,097 CCW vs 593 CW in a 4,000-GfxObj sample). Measured.
- LandBlock is 252 bytes: id, u32 hasObjects, u16 terrain[81], u8 height[81], align4. Index = x*9+y. Terrain bits: 0-1 road, 2-6 type, 11-15 scene; bits 7-10 are never set. Verified; ACE CellLandblock.cs and LandblockStruct.cs ConstructVertices.
- LandBlock.hasObjects==1 exactly when an LBI exists (5,346 = 5,346). LBI.NumCells disagrees with the actual EnvCell count in 38 landblocks. Verified.
- Region 0x13000000 (58,956 bytes): 'Dereth', 255x255 blocks, 24 m squares, 8 cells per block, LandHeightTable 0..700 m, dayLength 7620 s, 360-day year, partsMask 0x21F, 32 terrain types, 89 scene types, 20 sky day-groups, TexMerge baseTexSize 1024. Verified.
- Terrain triangle split: s=(u32)(gy*(214614067*gx+1813693831)-1109124029*gx-1369149221); if s*2^-32<0.5 the cell is cut SE-NW, else SW-NE. Source: ACE Source/ACE.Server/Physics/Common/LandblockStruct.cs ConstructPolygons.
- Scenery (trees and rocks) is generated procedurally from terrain scene bits and Region scene tables, not stored in the LBI. Source: ACE Source/ACE.Server/Physics/Common/Landblock.cs get_land_scenes and ObjectDesc.cs.
- Server-only types are absent from the user's portal: WeenieDefaults, TreasureTable 0x0E000011, CraftTable 0x0E000019, Quests 0x0E00001B, GameEventTable 0x0E00001C, ItemMutation 0x38. Verified by the ID listing.
- client_local_English.dat holds 101 LayoutDesc (0x21), 15 StringTables (0x23; 6,899 UTF-16 strings) and 1 LanguageInfo (0x41000000). Verified with the DRW HashTable layout.
- Licenses: ACE AGPL-3.0; ACViewer GPL-3.0; Chorizite/DatReaderWriter MIT (release/2.1.9, 2026-08-29; targets net8.0, netstandard2.0, net48); asheron-rs MIT; dereth MIT; acswarm GPL-3.0; holtburger AGPL-3.0; immortalbob/Landblock has no license. Source: gh api repo metadata.
- DatReaderWriter ships DatReaderWriter/dats.xml, a 4,300-line machine-readable schema of every dat type, under MIT. Source: Chorizite/DatReaderWriter repo.
- In acswarm's docs/subsystems/dat.md the portal and cell columns for data_subset and engine version are swapped. The actual values are portal subset 0 / engine 110 and cell subset 1 / engine 22. Verified on the user's files.

## RECOMMENDATIONS
- Treat Chorizite/DatReaderWriter's dats.xml (MIT) as the canonical schema. Generate the Godot-side reader (GDScript, C# or Rust) from it rather than porting ACE.DatLoader (AGPL) or ACViewer (GPL) line by line, unless a GPL/AGPL project is acceptable.
- Fastest path: a Godot 4 .NET project referencing the NuGet package Chorizite.DatReaderWriter 2.1.9 (net8.0) for runtime streaming of landblocks and cells. This needs a .NET 8+ SDK (runtimes alone are not enough). The fallback is an offline Python exporter grown from the validated scratch scripts (acdat.py / acparse.py), writing glTF + PNG/DDS + JSON.
- Do not pre-export all 805k cell files. Pre-convert portal assets (GfxObj/Setup to meshes, RenderSurface to textures) into a cache, and stream LandBlock, LBI and EnvCell per landblock at runtime with a 3x3 or 5x5 window.
- Texture loader: resolve Surface -> SurfaceTexture -> first RenderSurface id that exists, because highres is missing. Pass DXT1/3/5 straight through to Godot's DXT image formats. Swizzle A8R8G8B8 (bytes BGRA) and R8G8B8 (bytes BGR) to RGBA8. Expand INDEX16/P8 through the 2048-entry palette, applying ClothingTable sub-palette ranges for characters, and make index<8 transparent on Base1ClipMap surfaces. Decode RAW_JPEG with Godot's JPEG-from-buffer loader.
- Mesh builder: one surface per posSurface index. Build unique vertices per (vertexId, uvIdx) and fan-triangulate. Emit back faces for sidesType 1 (same material) and 2 (negSurface/negUV). Convert positions (x,y,z) to Godot (x,z,-y) and reverse the triangle index order.
- Setup posing: use PlacementFrames[101] or else [0] for the rest pose. The part transform is objFrame * frame[i] * DefaultScale (flat, not hierarchical). Animations replace per-part frames per keyframe; MotionTable selects animations by (style<<16 | motion).
- Terrain: build a 9x9 vertex grid per block using LandHeightTable and ACE's pseudo-random split formula. Port ACViewer's LandscapeSinglePass shader (base texture + 3 alpha-masked overlays + 2 road overlays from TexMerge, texture arrays) to a Godot spatial shader. Port get_land_scenes/ObjectDesc deterministically for scenery.
- Interiors: EnvCell gives envId|0x0D000000 with Environment.Cells[cellStructure] as geometry, EnvCell.Surfaces as the material list, and Position as the landblock-local transform. Use CellPortal/VisibleCells for portal culling or simply load all cells of a dungeon block. Enumerate cells by B-tree id range, never by LBI.NumCells.
- Skip parsing BSP for rendering beyond structurally walking it. Treat every non-LEAF/PORT tag other than the 6 child-bearing ones as childless. Use physics polygons as Godot trimesh or convex colliders if needed.
- Plan for gameplay data the client dats do not contain (weenies, spawns, treasure, quests): source it from ACE world-database exports. The dats do provide SpellTable, Skill/Vital/Xp tables, CharGen, combat tables and string tables, which are useful for a single-player rules layer.
- Keep the retail .dat files user-supplied (read from <repo>\client at runtime or import time) and never bundle extracted assets in any distributable build.

## RISKS
- The Godot 4 'clockwise = front face' convention comes from memory of the Godot docs (ArrayMesh/SurfaceTool) and was not re-checked in this session; confirm with a test mesh. The AC side (CCW front, ~98.3%) was measured.
- The ClipMap transparency rule (INDEX16 index < 8 transparent) is ACViewer's implementation, not confirmed against acclient. A8R8G8B8 surfaces with Translucency are premultiplied by ACViewer. Exact client blending (Additive, InvAlpha, Luminous, Detail flags) still needs RE or visual comparison.
- The units of CloSubPaletteRange Offset/NumColors (raw palette indices vs ×8 groups) were not independently verified; ACViewer applies them as raw indices.
- The terrain texture-merge algorithm (FindTerrainAlpha rotations, road codes) is only partially read here. Port it from ACE TexMerge.cs/LandSurf.cs or ACViewer texture.fx and compare against screenshots from the retail client.
- Only 1 in 7 EnvCells (105,005) were parse-validated, not all 734,977; the rest are expected to follow the same layout.
- LayoutDesc (UI, 0x21) and RenderMaterial / MaterialModifier / MaterialInstance (0x16-0x18) are not fully understood; DRW marks RenderMaterial unsupported. These matter only if the retail UI is reproduced.
- ParticleEmitterInfo field semantics (A/B/C vectors per emitter type), PhysicsScript timing and SkyObject properties are only structurally parsed. Reproducing effects and sky faithfully needs ACE/ACViewer behaviour code (ParticleEmitter.cs, Render/Particle*) or acclient RE.
- Licensing: DRW is MIT but says it is 'heavily based off ACE.DatReader FileTypes' (AGPL); there is a small provenance question if the project must stay permissive. The game data itself remains copyrighted (Turbine/WB/Daybreak) even though the service is defunct, so keep assets user-supplied.
- Many of the alternative clients (OpenAC, acswarm, dereth, Vitaeum_AC, the 'AC:Unreal' UE5 client) appeared in Aug–Oct 2026 and change rapidly. Their dat code was not audited beyond metadata.
- Scratch scripts mask the block 'next' pointer with 0x7FFFFFFF (per DRW's free-block flag); ACE does not mask. This made no difference on the user's files.

## SOURCES
- https://github.com/ACEmulator/ACE/tree/master/Source/ACE.DatLoader (commit 7715c052c2038a96e57c477789cafd4d870e0d4d, 2026-10-08)
- ACE: Source/ACE.DatLoader/{DatDatabase.cs,DatDatabaseHeader.cs,DatDirectory.cs,DatDirectoryHeader.cs,DatFile.cs,DatReader.cs,DatFileType.cs,BinaryReaderExtensions.cs,UnpackableExtensions.cs}
- ACE: Source/ACE.DatLoader/FileTypes/{GfxObj,SetupModel,Animation,MotionTable,Surface,SurfaceTexture,Texture,Palette,PaletteSet,ClothingTable,Environment,EnvCell,CellLandblock,LandblockInfo,RegionDesc,Scene,Wave,SoundTable,ParticleEmitterInfo,PhysicsScript,PhysicsScriptTable,GfxObjDegradeInfo,StringTable,LanguageInfo,Iteration,RenderTexture}.cs
- ACE: Source/ACE.DatLoader/Entity/{Polygon,CVertexArray,SWVertex,BSPNode,BSPLeaf,BSPPortal,CellStruct,CellPortal,Stab,BuildInfo,CBldPortal,LandDefs,GameTime,SkyDesc,SkyObject,SkyTimeOfDay,TerrainDesc,TerrainType,TexMerge,TerrainTex,TMTerrainDesc,ObjectDesc,MotionData,AnimationFrame,AnimationHook,StringTableData}.cs and Entity/AnimationHooks/*.cs
- ACE: Source/ACE.Entity/Enum/{GfxObjFlags,StipplingType,CullMode,SetupFlags,SurfaceType,SurfacePixelFormat,EnvCellFlags,PortalFlags,AnimationHookType,Placement,AnimationFlags,MotionDataFlags}.cs
- ACE: Source/ACE.Server/Physics/Common/{LandblockStruct.cs,Landblock.cs,TexMerge.cs,LandSurf.cs,LandDefs.cs,ObjectDesc.cs} and Source/ACE.Server/Physics/PartArray.cs
- https://github.com/Chorizite/DatReaderWriter (MIT; commit a40acb45, release/2.1.9 2026-08-29): DatReaderWriter/dats.xml, Lib/IO/DatHeader.cs, Lib/IO/DatBTree/{DatBTreeNode,DatBTreeFile,DatBTreeFileFlags}.cs, Lib/IO/BlockAllocators/{StreamBlockAllocator,BaseBlockAllocator}.cs, DatDatabase.cs (zlib Decompress), Types/{DrawingBSPNode,PhysicsBSPNode,HashTable,PStringBase,AC1LegacyPStringBase}.cs, Enums/BSPNodeType.cs, README.md
- https://github.com/ACEmulator/ACViewer (GPL-3.0): ACViewer/Render/TextureCache.cs, ACViewer/Model/{GfxObj,Polygon,Setup}.cs, ACViewer/Render/TerrainBatch.cs, ACViewer/Content/texture.fx, ACViewer/Render/Camera.cs, ACViewer/Render/Render.cs, ACViewer/ACViewer.csproj, .gitmodules
- https://github.com/enknamel/acswarm docs/subsystems/dat.md (GPL-3.0)
- https://github.com/amoeba/asheron-rs crates/asheron-rs/src/dat/reader/types/dat_database_header.rs (MIT)
- https://github.com/immortalbob/Landblock README.md, landblock/dat.py (no license)
- https://github.com/dereth-network/dereth (MIT), https://github.com/merklejerk/holtburger (AGPL-3.0), https://github.com/eriknihlen/OpenAC (MIT), https://github.com/OptimShi/Marshmallow (GPL-3.0), https://github.com/jkolb/Asheron (MIT), https://github.com/derandark/PhatAC (GPL-3.0), https://github.com/lkasser/Decal-ACUnreal (MIT), https://github.com/Vanquish-6/ACME-AC-Map-Editor-WorldBuilder (MIT), https://github.com/Y0oshi/MacAC (MIT), https://github.com/CrimsonMage/Vitaeum_AC
- Local files (read-only): <repo>\client\client_portal.dat, client_cell_1.dat, client_local_English.dat
- Validation scripts: <scratch>\research\datspec\scripts\acdat.py and acparse.py; source clones in ...\scratchpad\research\datspec\ace and ...\datspec\drw