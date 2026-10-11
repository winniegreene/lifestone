# Local scouting of the AC1 client files (dats + acclient.exe)

All numbers below were produced by scripts I wrote and ran against `<repo>\client\` using only read access. The scripts use the Python stdlib only and run with `python -I`. The whole suite runs in about 19 s via `run_all.py`, and logs go to `scout\out\logs\*.log`.

Scripts live in `<scratch>\research\scout\`:

| file | purpose |
|---|---|
| `acdat.py` | Core reader: `DatHeader`, B-tree walk (`entries()`), point lookup (`find()`), block-chain reads, zlib path, and a `Reader` cursor with the AC compressed-uint, pstring and align4 primitives |
| `acformats.py` | Parsers: RenderSurface 0x06, Palette 0x04, Surface 0x08, SurfaceTexture 0x05, GfxObj 0x01 (including physics/drawing BSP), LandBlock xxyyFFFF, LandBlockInfo xxyyFFFE, EnvCell xxyy01nn, Region 0x13000000, Wave 0x0A plus a RIFF writer. Every parser reports `_consumed` so byte-exact checks are possible |
| `acimage.py` | Stdlib PNG writer and decoders for DXT1/3/5, A8R8G8B8, X8R8G8B8, R8G8B8, LSCAPE_R8G8B8/ALPHA, A8/L8, R5G6B5, A4R4G4B4, P8 and INDEX16 (with palette). JPEG is passed through unchanged |
| `s01_headers.py` | Dumps the header of each dat |
| `s02_walk_btree.py` | Full B-tree walk, counts by type, Iteration record, and the full ID list to `out\<dat>_ids.csv` (id,type,flags,version,offset,size,date,iteration) |
| `s03_samples.py` | Texture survey and one PNG per format; parses all GfxObjs; Holtburg LandBlock/Info/EnvCell; Region; OBJ+MTL+PNG export of two Holtburg buildings; Wave survey and .wav export |
| `s04_worldmap.py` | Decodes all 65,025 landblocks to `world_height.png`, `world_terrain.png` (2041x2041) and `world_cells.png` |
| `s05_pe_summary.py` | PE32 summary of acclient.exe |
| `s06_gotchas.py` | B-tree ordering, unused-slot contents, free-list marker, block contiguity, polygon winding/sides/stippling statistics |
| `s07_render_preview.py` | Tiny software rasterizer that renders a GfxObj straight from the dat (proves geometry, UVs and textures) |
| `run_all.py` | Runs everything and tees the logs |

`ref\` holds copies of the ACE and DatReaderWriter sources used for the layouts.

## 1. Dat container format (verified against the local files)

The file is laid out as follows:

- **0x000**: 256-byte ASCII version string. It is empty in all three dats.
- **0x100**: 64-byte transactions blob.
- **0x140**: the `DiskFileInfo_t` header (80 bytes), ending at 0x190. Its fields, in order:
  - u32 magic (=0x5442 "BT")
  - u32 block_size
  - u32 file_size
  - u32 data_set
  - u32 data_subset
  - u32 free_head
  - u32 free_tail
  - u32 free_count
  - u32 btree_root
  - u32 new_lru
  - u32 old_lru
  - u32 use_lru
  - u32 master_map_id
  - u32 engine_pack_version
  - u32 game_pack_version
  - byte[16] version_major (DatReaderWriter calls it a GUID)
  - u32 version_minor

The ACE file is `Source/ACE.DatLoader/DatDatabaseHeader.cs`. ACE starts reading at 0x140 (`DAT_HEADER_OFFSET`) and calls the magic `FileType`. Chorizite/DatReaderWriter's `Lib/IO/DatHeader.cs` reads the whole 400-byte header from offset 0.

**Header values:**

| field | client_portal.dat | client_cell_1.dat | client_local_English.dat |
|---|---|---|---|
| block_size | 1024 | **256** | 1024 |
| file_size (= on-disk size) | 926,941,184 (0x37400000) | 348,127,232 (0x14C00000) | 1,048,576 |
| data_set / subset | 1 Portal / 0 | 2 Cell / 1 | 3 Language / 1 |
| free_head / free_tail | 0x37386800 / 0x37381C00 | 0x14B35A00 / 0x14BFFF00 | 0x000FDC00 / 0x000BDC00 |
| free_count | 2,052 blocks (0.23%) | 3,238 (0.24%) | 270 (26%) |
| btree_root | 0x0232D400 | 0x01B77400 | 0x0002B000 |
| new/old LRU | 0 / 0 | 0 / 0 | 0 / 0 |
| use_lru | 0xCDCDCDCD | 0xCDCDCDCD | 0xCDCDCDCD |
| master_map_id | 0x25000000 (a DidMapper) | 0 | 0 |
| engine_pack_version | 110 | 22 | 110 |
| game_pack_version | 0 | 0 | 0 |
| version_major (16 bytes) | d2d7a7342f72464c8ab4ef514f856ffd | same | same |
| version_minor | 6657 (0x1A01) | 6657 | 6657 |
| **Iteration 0xFFFF0001** | **2072** (ranges [(1,-2072)]) | **982** ([(1,-982)]) | **994** ([(1,-994)]) |

The Iteration values match exactly what ACE's `DatDatabase.GetTotalIterations()` comment documents for the final end-of-retail dats (portal 2072, cell 982, language 994). Each Iteration record holds a single range, so ACE's "incomplete dat" check passes. These are complete end-of-retail dats.

**Block chaining.** Every block starts with a u32 file offset of the next block (0 marks the end of the chain), followed by block_size−4 payload bytes. A record of size S starts at `entry.offset` (always block-aligned, checked on all 885k entries) and spans ceil(S/(bs−4)) blocks. Free blocks have bit 31 set on their next pointer: the free_head pointers read 0xB7386C00, 0x94B35B00 and 0x8006E000. No in-use chain encountered that bit. Multi-block records are physically contiguous 97.2% of the time in portal and 99.3% in cell (sampled), so chains still have to be followed.

**B-tree directory.** Each node is a logical record of `NODE_SIZE` = 1716 bytes, read through the block chain. Its layout is `u32 branches[62]; u32 entry_count; Entry[61]`, with each entry 24 bytes: `u16 flags; u16 version; u32 id; u32 offset; u32 size; u32 date(unix); u32 iteration`.

- ACE reads the first 4 entry bytes as one u32 "BitFlags". DatReaderWriter splits it into flags and version, where flags&1 means zlib-compressed and the payload is then `u32 uncompressed_size` followed by a zlib stream.
- A node is a leaf iff `branches[0]==0`. Otherwise it has `entry_count+1` children.
- **Gotcha:** unused branch slots are *not* clean. Portal has 120,311 slots of 0xCDCDCDCD and 12,329 stale non-zero pointers; cell has 1,233,542 stale pointers. You must never infer children from non-zero slots.
- Keys are sorted within nodes and globally in order, so a binary-descent `find()` agreed with the full walk on 2,000 random IDs per dat.

**Compression:** zero entries have flags≠0 in any of the three dats (885,160 entries in total). The exe does import `zlib1.dll` (`uncompress`, `compress2`), so the reader should keep the zlib path, but these files never use it.

**Walk results:**
- Portal: 79,694 files, 2,231 nodes (2,171 leaves), depth 3.
- Cell: 805,348 files, 26,657 nodes, depth 4.
- Language: 118 files, 4 nodes.
- No duplicate IDs. A full Python walk takes 0.1–0.7 s once the OS has cached the files.
- Entry version histogram for portal is {1:1, 2:57,422, 3:22,271}.
- Entry dates: portal 2005-05-27..2015-06-12; cell ..2016-12-23; language ..2015-06-18.

## 2. File counts by type

**Portal** (high byte of the ID; stored MB in parentheses):

| type | count |
|---|---|
| 01 GfxObj | 15,318 (77.7 MB) |
| 02 Setup | 5,935 |
| 03 Animation | 2,066 (53.6 MB) |
| 04 Palette | 4,521 (35.4 MB) |
| 05 SurfaceTexture | 7,221 |
| **06 RenderSurface** | **20,684 (578.4 MB)** |
| 08 Surface | 6,152 |
| 09 MotionTable | 436 |
| 0A Wave | 786 (50.0 MB) |
| 0D Environment | 772 |
| 0E singletons | 12 tables: CharGen 0E000002, SecondaryAttributeTable 03, SkillTable 04, ChatPoseTable 07, ObjectHierarchy 0D, SpellTable 0E (1.4 MB), SpellComponentTable 0F, XpTable 18, BadData 1A, ContractTable 1D, TabooTable 1E, NameFilterTable 20 |
| 0E01xxxx QualityFilter | 2 |
| 0F PaletteSet | 2,681 |
| 10 Clothing | 1,917 |
| 11 GfxObjDegradeInfo | 4,131 |
| 12 Scene | 179 |
| 13 Region | 1 |
| 14 KeyMap | 2 |
| 15 RenderTexture | 2 |
| 16 / 17 / 18 (RenderMaterial / MaterialModifier / MaterialInstance) | 1 each |
| 20 SoundTable | 190 |
| 22 EnumMapper | 40 |
| 25 DidMapper | 22 |
| 26 ActionMap | 1 |
| 27 DualDidMapper | 5 |
| 30 CombatTable | 71 |
| 31 String | 28 |
| 32 ParticleEmitter | 2,051 |
| 33 PhysicsScript | 4,248 |
| 34 PhysicsScriptTable | 164 |
| 39 MasterProperty | 1 |
| 40 Font | 49 |
| 78 DbProperties | 2 |
| FFFF0001 Iteration | 1 |

**Cell:**
- LandBlock xxyyFFFF: 65,025 (= 255²), 15.6 MB
- EnvCell xxyy0100+: 734,976, 182.7 MB
- LandBlockInfo xxyyFFFE: 5,346
- Iteration: 1

**Language:**
- 21 UiLayout: 101
- 23 StringTable: 15
- 41 StringState: 1
- Iteration: 1

## 3. Sample decodes (all byte-exact: bytes consumed == record size)

### RenderSurface (0x06)

Layout: `u32 id; u32 data_category; i32 w; i32 h; u32 pixfmt; i32 len; byte[len]; [u32 palette_id if P8/INDEX16]`.

Format survey over all 20,684 textures:

| format | count |
|---|---|
| A8R8G8B8 | 12,984 |
| INDEX16 | 4,182 |
| DXT1 | 1,971 |
| R8G8B8 | 1,190 |
| DXT5 | 127 |
| A8 | 86 |
| CUSTOM_RAW_JPEG | 79 |
| CUSTOM_LSCAPE_R8G8B8 | 33 |
| CUSTOM_LSCAPE_ALPHA | 16 |
| P8 | 6 |
| DXT3 | 5 |
| R5G6B5 | 3 |
| A4R4G4B4 | 2 |

- Most common sizes: 32x32 (12,852, mostly icons/UI), 128², 256², 64², 128x256, 512².
- 785 textures are not power-of-two.
- No length mismatches against the format math. Only the top mip level is stored.
- I decoded one sample of each format to PNG and checked them visually: the DXT1 Holtburg timber wall 0x06004304, INDEX16 armor 0x06005747 with Palette 0x0400007E, A8R8G8B8 mistletoe with alpha, and a landscape grass texture.
- The JPEG-format record 0x06007576 has w=h=0 and holds a raw JFIF (ffd8ffe0).

Material chain:
- **Surface 0x08** has no id field: `u32 type; if type&6 {u32 SurfaceTexture id; u32 palette id} else {u32 ARGB}; f32 translucency, luminosity, diffuse`. All 6,152 parse byte-exact. Top type combinations: Base1Image 4,904, Base1ClipMap 640, Image|Alpha|Additive 161, Image|Translucent 156, Solid 79.
- **SurfaceTexture 0x05**: `u32 id; i32 unk; u8 unk; i32 n; u32 RenderSurface ids[n]`. All 7,221 parse byte-exact; 4,197 have 1 ref and 3,024 have 2 refs.
- **Gotcha:** 2,284 SurfaceTextures have their *first* reference (presumably the high-res variant) missing from portal.dat. No SurfaceTexture has *all* references missing. `client_highres.dat` is not present, and the exe references that filename. The resolver must pick the first ID that exists.

### GfxObj (0x01)

Layout:
- `u32 id`
- `u32 flags` (1=physics, 2=drawing, 8=degrade)
- `compressed-u32 n` + `u32 surfaces[n]`
- `CVertexArray`: `i32 type(=1); u32 n;` then n × `{u16 key; u16 nUV; vec3 pos; vec3 normal; nUV × (f32 u, f32 v)}`
- `[physics: compressed-u32 n` + `{u16 key; Polygon}` + physics BSP`]`
- `vec3 sort_center`
- `[drawing: same as physics, with drawing BSP]`
- `[u32 degrade id]`

Polygon: `u8 n; u8 stippling; i32 sides; i16 posSurf; i16 negSurf; i16 vid[n]; [u8 posUV[n] unless stip&4]; [u8 negUV[n] if sides==2 && !(stip&8)]`.

BSP tags are 4 ASCII characters stored reversed. The full set of tags seen:

| tag | count | children |
|---|---|---|
| BPnN | 255,971 | two |
| LEAF | 210,896 | — |
| BPIN | 151,134 | two |
| BpIn | 106,788 | none |
| BPOL | 106,620 | none |
| BpIN | 59,608 | one |
| BPIn | 22,129 | one |
| PORT | 623 | — |

**Gotcha:** `BpIn`, `BPOL` and `BPFL` have **no children**. ACE handles them only implicitly through `default:`. DatReaderWriter's `BSPNodeType` enum lists them explicitly.

Results:
- **All 15,318 GfxObjs parse byte-exact in 2.2 s.** Flags histogram: 0x2=10,546, 0xA=3,514, 0x3=641, 0xB=617.
- Totals: 615,119 vertices, 869,312 draw polygons, 115,159 physics polygons.
- Vertices per polygon: 3→735,453, 4→128,138, with fans up to 15.
- sides_type: 0=714,822 single-sided, 1=154,479 double-sided with the same surface, 2=11 with a distinct back surface. ACE's `CullMode` names for these (Landblock/None/Clockwise) are misleading.
- Stippling: 0x0=609,814, 0x1=254,841, 0x4=4,657 (no UVs).
- UVs per vertex: 0:17,358, 1:335,813, 2:127,637, 3–7:… (multiple UV sets).
- **Winding:** (v0,v1,v2) is CCW about the vertex normals in AC's right-handed Z-up frame for 863,095 polygons, versus 4,973 CW and 1,244 degenerate. `s07_render_preview.py` back-face culls with that rule and renders a closed, correct Holtburg building: `out\preview_01000C1E.png` shows the shingled roof, timber frame, stone base and chimney.
- UVs use the D3D convention (v=0 is the top row).
- Exported `out\gfxobj_01000C1E.obj/.mtl` has 103 vertices, 102 polygons, 170 triangles and 14 materials, plus `gfxobj_01000BC3.obj`. They are written in Y-up as (x,z,−y) with CCW faces.

### Holtburg LandBlock 0xA9B4FFFF

Layout: `u32 id; u32 has_objects; u16 terrain[81]; u8 height[81]; align4`, 252 bytes, which is exactly one 256-byte cell block. Index = x*9+y, with x running west→east and y running south→north.

Height indices, north row first:

```
y=8  20 23 25 25 25 24 24 20 15
y=7  25 33 33 33 33 33 33 27 24
y=6  29 33 33 33 33 33 33 33 32
y=5  33 33 33 33 33 33 33 33 32
y=4  35 35 35 37 40 40 33 33 32
y=3  39 39 39 41 40 40 40 39 36
y=2  39 39 39 47 47 47 47 47 41
y=1  42 42 42 47 47 47 47 47 45
y=0  45 45 45 47 47 47 47 47 48
```

Through the Region LandHeightTable these become 30–96 m. The town plateau is index 33, i.e. 66.0 m. The terrain word holds road bits 0–1, type bits 2–6 (LushGrass/Grassland/PatchyGrassland/SemiBarrenRock here) and scenery bits 11–15 (values 0/15/16).

**LandBlockInfo 0xA9B4FFFE** (5,876 bytes):
- num_cells 123, 114 static objects (28 GfxObj and 86 Setup), 12 buildings, pack_mask 0.
- Building 0x01000C1E sits at (84.1, 131.5, 66.0). Its height matches the terrain (66 m).

**EnvCell 0xA9B40100** (858 bytes):
- flags 0x3, Environment 0x0D00034F, 11 surfaces, 6 portals, 16 visible cells, 22 statics.
- Origin (84.09, 131.54, 66.00) is identical to the building's origin. The quaternion is stored w,x,y,z = (0.7071, 0, 0, −0.7071).

Validation:
- The EnvCell parser passed on every 25th EnvCell (29,400/29,400).
- The LandBlockInfo parser passed on all 5,346.

**Whole world** (`s04`):
- 65,025 landblocks decoded with 0 bad records and **0 shared-edge height mismatches**.
- `has_objects==1` on exactly 5,346 landblocks, which equals the LandBlockInfo count.
- Heights run 0–670 m (max index 253).
- 7,945 road vertices.
- 3,409 landblocks contain EnvCells. The largest is 0x00D1 with 4,213 cells, then 0x200F, 0x8C03, 0x7E03, 0x8603.
- `world_terrain.png` renders recognizable Dereth with north up, which confirms that `TerrainType.color` is 0xAARRGGBB.

### Region 0x13000000

58,956 bytes, parsed **byte-exact** with ACE's RegionDesc layout:
- name "Dereth", region_number 1, version 3, parts_mask 0x21F.
- LandDefs: 255x255 blocks, square_length 24.0, lblock_length 8, vertex_per_cell 1, max_obj_height 200, sky_height 1000, road_width 5. The LandHeightTable is monotonic: [0]=0, [1]=2, [64]=128, [128]=256, [255]=700.
- GameTime: day_length 7620 s, 360 days/year, zero_year 10, "P.Y.", 16 times of day (Darktide…Gloaming-and-Half), 6 weekdays, 12 seasons (Morningthaw…Wintersebb).
- Sky: 20 day groups (Sunny/Rainy/Clear/Cloudy).
- Sound: 37 ambient sound-table entries.
- 89 scene types.
- 32 terrain types.
- TexMerge: base_tex_size 1024, 4 corner alpha maps, 1 side map, 3 road maps, 33 terrain descriptions (e.g. type 0 tex 0x0500145C, detail 0x05001786).
- RegionMisc: game_map 0x0600127D.

### Wave (0x0A)

Layout: `u32 id; i32 fmtLen; i32 dataLen; WAVEFORMATEX; data`.
- 786 records, all byte-exact: 785 PCM and 1 MP3 (tag 0x55, 0x0A000393, with a 30-byte fmt header).
- Most common (rate, channels, bits): (11025,1,16) 446; (22050,1,16) 120; (44100,1,16) 79; (22050,1,8) 44.
- Exported `.wav` files are valid RIFF.

## 4. acclient.exe (PE summary)

- 4,841,472 bytes, MD5 116D9A66A70B6AF449DC3A28D82F2F6D, SHA-256 bca95bbebed4b9ed1ff09d0da83144e2fc4208f63ad7ada5cb47c3ca207ccba9.
- PE32 i386, TimeDateStamp 0x557A956C (2015-06-12 08:16:44 UTC). Version comment: "compiled Fri Jun 12 04:16:27 2015 : RETAIL". FileVersion 00.00.11.6096, Turbine, Inc.
- Linker 7.0 (VS.NET 2002, MSVCR70/MSVCP70). ImageBase 0x00400000, entry VA 0x005DF16A, GUI subsystem.
- **No ASLR and no NX** (DllCharacteristics 0), so VAs are stable for static analysis. It does carry a .reloc section.

Sections:

| section | VA | VSize | RawSize | entropy |
|---|---|---|---|---|
| .text | 0x1000 | 0x391CE0 | 0x392000 | 6.56 |
| .rdata | 0x393000 | 0x7755E | — | 5.43 |
| .data | 0x40B000 | 0xFB7D4 | 0x2D000 | 3.92 |
| .data1 | 0x507000 | 0x1BA0 | — | 4.16 |
| .rsrc | 0x509000 | 0xBB08 | — | 4.85 |
| .reloc | 0x515000 | 0x57400 | — | 5.97 |

The binary is not packed.

Imports: 18 DLLs, 372 functions. KERNEL32 103, MSVCR70 112, USER32 55, WINMM 25 (midiStream*), WSOCK32 18 (by ordinal), RPCRT4 12, GDI32 12, ole32 7, MSACM32 6, ADVAPI32 6, VERSION 3, IMM32 3, OLEAUT32 3, SHELL32 2, zlib1 2, DSOUND 1 (#1), COMCTL32 1, MSVCP70 1. `d3d9.dll` (Direct3DCreate9) and `DINPUT8.DLL` are loaded dynamically.

Debug directory: CodeView RSDS, **PDB `d:\ac1_acxp\output\bin\GAME\WIN32\retail\acclient.pdb`, GUID 08E25C14-E2A1-46D5-B056-92B2E43A7234, age 1** (symbol-server key 08E25C14E2A146D5B05692B2E43A72341). A GitHub code search for that key found nothing.

A third-party doc (salvia420-bit/WorldBuilder-ACME-Edition, `external/holtburger/apps/holtburger-web/CHORIZITE_PORTING_PLAN.md`) describes privately held `acclient.c/.h`, Binary Ninja and PDB dumps whose build path is `d:\ac1_sep13\output\`. That is a **different build** from this exe (`ac1_acxp`, June 2015). I treat this as unverified.

String probes found `client_portal.dat` and `client_highres.dat`. The cell and language dat names are built at runtime.

## 5. Other local observations

- The files in `client\` are the contents of `<repo>\ac-updates.zip`: the four files have identical sizes, the dats are zip-dated 2019 and the exe 2016-03-19. **There is no `client_highres.dat` anywhere locally.** `ac1install.exe` (599 MB) was not touched.
- Python performance is ample for an offline converter: 15k GfxObjs in 2 s and 65k landblocks in about 2 s. A runtime reader in C# or GDScript would be fast enough too.


## KEY FACTS
- Dat header is at file offset 0x140 (80 bytes, after a 256-byte empty version string and a 64-byte transactions blob); magic 0x00005442 in all three dats. Sources: s01_headers.py output, ACE Source/ACE.DatLoader/DatDatabaseHeader.cs, Chorizite DatReaderWriter Lib/IO/DatHeader.cs
- Block sizes are 1024 for portal, 256 for cell_1 and 1024 for local_English; the header file_size equals the on-disk size for each (926,941,184 / 348,127,232 / 1,048,576). Source: s01_headers.py
- B-tree roots: portal 0x0232D400, cell 0x01B77400, language 0x0002B000; free_count 2,052 / 3,238 / 270 blocks; use_lru is 0xCDCDCDCD (MSVC uninitialized fill) in all three. Source: s01_headers.py
- All three dats share version_major bytes d2d7a7342f72464c8ab4ef514f856ffd and version_minor 6657; engine_pack_version is 110 (portal, language) and 22 (cell); portal master_map_id is 0x25000000. Source: s01_headers.py
- Iteration records (0xFFFF0001): portal 2072, cell 982, language 994, each a single complete range. These are exactly ACE's documented end-of-retail values. Source: s02_walk_btree.py; ACE DatDatabase.cs comment
- File counts: portal 79,694 (2,231 B-tree nodes, depth 3); cell 805,348 = 65,025 LandBlocks + 734,976 EnvCells + 5,346 LandBlockInfos + Iteration (26,657 nodes, depth 4); language 118. Full ID lists are in scout/out/*_ids.csv. Source: s02_walk_btree.py
- Portal type counts: GfxObj 15,318; Setup 5,935; Animation 2,066; Palette 4,521; SurfaceTexture 7,221; RenderSurface 20,684 (578 MB); Surface 6,152; MotionTable 436; Wave 786; Environment 772; PaletteSet 2,681; Clothing 1,917; GfxObjDegradeInfo 4,131; Scene 179; ParticleEmitter 2,051; PhysicsScript 4,248; Font 49. Source: s02_walk_btree.py
- No compressed entries in any dat (directory flags are 0 on all 885,160 entries), although acclient.exe imports zlib1.dll uncompress/compress2 and DatReaderWriter supports flag 0x1 = u32 size + zlib. Source: s02_walk_btree.py, s05_pe_summary.py, DatReaderWriter DatDatabase.cs
- B-tree entry is 24 bytes: u16 flags, u16 version, u32 id, u32 offset, u32 size, u32 unix date, u32 iteration; node is 62 u32 branches + u32 count + 61 entries = 1716 bytes; leaf iff branches[0]==0, else count+1 children. Unused branch slots hold stale pointers and 0xCDCDCDCD. Source: s06_gotchas.py; ACE DatDirectoryHeader.cs; DatReaderWriter DatBTreeNode.cs
- Block chain: the first u32 of each block is the next block's offset (0 = end); free blocks have bit 31 set (free_head pointers read 0xB7386C00 / 0x94B35B00 / 0x8006E000); 97-99% of multi-block files are physically contiguous. Source: s06_gotchas.py
- RenderSurface formats over 20,684 textures: A8R8G8B8 12,984, INDEX16 4,182, DXT1 1,971, R8G8B8 1,190, DXT5 127, A8 86, RAW_JPEG 79, LSCAPE_R8G8B8 33, LSCAPE_ALPHA 16, P8 6, DXT3 5, R5G6B5 3, A4R4G4B4 2; no length mismatches; 785 are non-power-of-two. Source: s03_samples.py
- 2,284 of 7,221 SurfaceTextures have their first RenderSurface reference missing from portal.dat (presumably in the absent client_highres.dat), but none have all references missing. Source: s03_samples.py
- All 15,318 GfxObjs parse byte-exact (615,119 verts, 869,312 draw polys, 115,159 physics polys). BSP tags include zero-child BpIn/BPOL (plus BPFL per DatReaderWriter). Source: s03_samples.py; DatReaderWriter Enums/BSPNodeType.cs, Types/DrawingBSPNode.cs
- Polygon winding is CCW about vertex normals in AC's right-handed Z-up frame for 863,095 of 869,312 polys; sides_type 0=single-sided (714,822), 1=double-sided same surface (154,479), 2=distinct back surface (11). Source: s06_gotchas.py and the visual check in s07_render_preview.py
- Holtburg LandBlock 0xA9B4FFFF height indices are 15..48 (30..96 m); the town plateau is index 33 = 66 m; LandBlockInfo 0xA9B4FFFE has 123 cells, 114 statics and 12 buildings (building 0x01000C1E at 84.1,131.5,66.0, matching EnvCell 0xA9B40100's origin). Source: s03_samples.py
- Region 0x13000000 'Dereth' parses byte-exact (58,956 bytes): 255x255 landblocks, 24 m squares, 8 cells/block, LandHeightTable 0..700 m, day_length 7620 s, 360 days/year, 32 terrain types, TexMerge with 4 corner, 1 side and 3 road alpha maps and 33 terrain descriptions. Source: s03_samples.py
- Whole-world decode: 65,025 landblocks with 0 shared-edge height mismatches; has_objects=1 on exactly 5,346 (= LandBlockInfo count); max height 670 m; 3,409 landblocks contain EnvCells. Source: s04_worldmap.py
- Waves: 786 records, 785 PCM (mostly 11025 Hz mono 16-bit) and 1 MP3 (0x0A000393). Source: s03_samples.py
- acclient.exe: PE32, TimeDateStamp 2015-06-12 08:16:44 UTC, FileVersion 00.00.11.6096, linker 7.0/MSVCR70, ImageBase 0x400000, no ASLR/NX, 18 import DLLs / 372 functions, d3d9 and dinput8 loaded dynamically; RSDS PDB d:\ac1_acxp\output\bin\GAME\WIN32\retail\acclient.pdb GUID 08E25C14-E2A1-46D5-B056-92B2E43A7234 age 1. Source: s05_pe_summary.py
- client\ files are byte-size-identical to the contents of <repo>\ac-updates.zip; there is no client_highres.dat locally. Source: python zipfile listing
- Chorizite/DatReaderWriter is MIT, targets net8.0;netstandard2.0;net48, NuGet id Chorizite.DatReaderWriter, latest release/2.1.9 on 2026-08-29. ACEmulator/ACE is AGPL-3.0, last pushed 2026-10-08. Source: gh api

## RECOMMENDATIONS
- Build the Godot asset layer as a runtime reader of the user's own dats instead of shipping extracted assets, and cache converted resources under user://. The format is simple (no compression, fixed blocks, sorted B-tree), so either use Chorizite.DatReaderWriter (MIT, netstandard2.0/net8.0) from Godot 4 .NET (needs a .NET 8 SDK install), or port acdat.py/acformats.py (about 700 lines) to C# or GDScript. Avoid linking ACE.DatLoader into the game, since it is AGPL-3.0.
- Use the scout scripts as a test oracle: every parser reports bytes consumed, so a C#/GDScript port can be diffed against the Python output (counts, Holtburg grids, Region values) in CI.
- Textures: hand DXT1/DXT3/DXT5 straight to Godot (Image.create_from_data with FORMAT_DXT1/3/5, no decoding). Swizzle A8R8G8B8/R8G8B8 from BGRA/BGR. Keep INDEX16/P8 as index textures plus palette lookup in a shader, because clothing and creature recolors (ClothingTable 0x10 / PaletteSet 0x0F subpalettes) need runtime palette swaps. Resolve SurfaceTexture to the first RenderSurface present in the loaded dats.
- Geometry: one ArrayMesh per GfxObj, one surface per Surface index; fan-triangulate polygons; map AC (x,y,z) to Godot (x,z,-y). AC front faces are CCW about the normal. Godot's ArrayMesh docs say front faces are clockwise (recalled, verify), so reverse the triangle index order when building ArrayMesh directly. sides_type 1 means a double-sided material. UVs use D3D v-down, the same as Godot UV space, so no flip is needed.
- Terrain: stream 192 m landblocks (9x9 vertices, 24 m spacing) and look heights up in Region.LandHeightTable. Build the terrain blend shader from TexMerge (corner/side/road alpha maps plus per-type TerrainTex). Place buildings and statics from LandBlockInfo frames (origin + quaternion stored w,x,y,z) and interiors from EnvCell -> Environment 0x0D cell structs.
- Make Holtburg (landblock 0xA9B4: 12 buildings, 114 statics, 123 EnvCells) the first vertical slice. All its data already decodes, and out/gfxobj_01000C1E.obj with its PNGs can go straight into Godot as a smoke test.
- Next decoders to add, in priority order: Setup 0x02 (multi-part models; ACE SetupModel.cs layout), Animation 0x03 + MotionTable 0x09, Environment 0x0D/CellStruct, Scene 0x12 + Region scene types (procedural scenery), Palette/PaletteSet/Clothing, ParticleEmitter/PhysicsScript, Font 0x40 and StringTable/UiLayout from the language dat.
- For reverse engineering acclient.exe with REA/Ghidra, use the fixed image base 0x400000 (no ASLR) and look for a PDB matching GUID 08E25C14E2A146D5B05692B2E43A7234 age 1. Do not assume community symbol dumps (reported build path d:\ac1_sep13) match this June-2015 build without verifying.

## RISKS
- client_highres.dat is absent. The game works from portal fallbacks (no SurfaceTexture lacks every texture), but 2,284 materials will use their lower-resolution variant. Whether the retail client strictly requires the file was not tested; the exe does reference the filename.
- Terrain triangle split direction per cell and procedural scenery placement (terrain scenery bits -> Region SceneTypes -> Scene 0x12) are client algorithms not checked here. They are reportedly implemented in ACE's physics/landblock code and ACViewer; verify against those or the binary.
- Not decoded or validated locally: Setup, Animation, MotionTable, Environment/CellStruct BSP, ParticleEmitter, PhysicsScript, SoundTable, Clothing/PaletteSet, Font, UiLayout/StringTable, and the 0x0E tables (SpellTable, CharGen, SkillTable, XpTable).
- The dats contain only client-side assets and tables. Monsters, spawns, loot, quests, NPC dialog and server-side spell and combat rules live in ACE's world database and server code, which a single-player build must still source (ACE world DB or similar). Covered by other investigators.
- The Godot ArrayMesh clockwise-front-face convention is from memory of the Godot docs and must be checked in-engine. The AC side (CCW about the normal, right-handed Z-up) is measured.
- PDB availability for this exact build (GUID 08E25C14-E2A1-46D5-B056-92B2E43A7234, age 1) is unknown. The only lead found describes a different build path (ac1_sep13), so symbol addresses may not match this exe.
- Entry 'version' (u16 values 1/2/3) and the RenderSurface data_category field have unconfirmed semantics; neither is needed for reading.
- Turbine/WB still owns the assets regardless of abandonment, so redistributing extracted assets is a legal risk; runtime loading from the user's own dats reduces it.
- ACE.DatLoader is AGPL-3.0; its source was used only as a format reference. Any code directly derived from it inherits AGPL obligations, whereas DatReaderWriter is MIT.

## SOURCES
- <repo>\client\client_portal.dat
- <repo>\client\client_cell_1.dat
- <repo>\client\client_local_English.dat
- <repo>\client\acclient.exe
- <repo>\ac-updates.zip (listing only)
- <scratch>\research\scout\ (acdat.py, acformats.py, acimage.py, s01_headers.py, s02_walk_btree.py, s03_samples.py, s04_worldmap.py, s05_pe_summary.py, s06_gotchas.py, s07_render_preview.py, run_all.py, out\, out\logs\, ref\)
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatDatabaseHeader.cs
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatDatabase.cs
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatDirectory.cs
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatDirectoryHeader.cs
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatFile.cs
- https://github.com/ACEmulator/ACE/blob/master/Source/ACE.DatLoader/DatReader.cs
- https://github.com/ACEmulator/ACE/tree/master/Source/ACE.DatLoader/FileTypes (Iteration, CellLandblock, LandblockInfo, EnvCell, Texture, SurfaceTexture, Surface, GfxObj, Palette, RegionDesc, Wave, SetupModel)
- https://github.com/ACEmulator/ACE/tree/master/Source/ACE.DatLoader/Entity (CVertexArray, SWVertex, Polygon, BSPNode, BSPLeaf, BSPPortal, LandDefs, GameTime, SkyDesc, TerrainDesc, TexMerge, Stab, Frame, BuildInfo, CBldPortal, CellPortal)
- https://github.com/ACEmulator/ACE/tree/master/Source/ACE.Entity/Enum (SurfacePixelFormat, SurfaceType, GfxObjFlags, StipplingType, CullMode, BSPType, EnvCellFlags, PortalFlags)
- https://github.com/Chorizite/DatReaderWriter/blob/master/DatReaderWriter/Lib/IO/DatHeader.cs
- https://github.com/Chorizite/DatReaderWriter/tree/master/DatReaderWriter/Lib/IO/DatBTree (DatBTreeFile.cs, DatBTreeFileFlags.cs, DatBTreeNode.cs)
- https://github.com/Chorizite/DatReaderWriter/blob/master/DatReaderWriter/Lib/IO/BlockAllocators/BaseBlockAllocator.cs and StreamBlockAllocator.cs
- https://github.com/Chorizite/DatReaderWriter/blob/master/DatReaderWriter/DatDatabase.cs (zlib decompress path)
- https://github.com/Chorizite/DatReaderWriter/tree/master/DatReaderWriter/Types (DrawingBSPNode.cs, PhysicsBSPNode.cs, CellBSPNode.cs) and Enums/BSPNodeType.cs
- https://github.com/Chorizite/DatReaderWriter/blob/master/DatReaderWriter/DatReaderWriter.csproj
- https://github.com/salvia420-bit/WorldBuilder-ACME-Edition/blob/HEAD/external/holtburger/apps/holtburger-web/CHORIZITE_PORTING_PLAN.md (third-party doc; PDB/decomp claims unverified)
- https://github.com/merklejerk/holtburger (Rust AC client ecosystem, AGPL-3.0, created 2026-01-25, pushed 2026-10-03)