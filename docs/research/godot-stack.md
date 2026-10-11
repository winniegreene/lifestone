# Godot stack choice and prior-art ports for single-player Asheron's Call

## 0. Prior work by the author
An earlier private prototype by the author (Godot 4.6.2 mono + .NET 8, AGPL-3.0) reached a single-player Holtburg slice: runtime DAT decoders, Holtburg terrain in a 3×3 landblock ring, buildings and EnvCells, NPCs from an ACE-World SQL→SQLite import, armoured player composition (a port of ACViewer's palette and clothing pipeline), melee, spells, particles, a HUD and SQLite saves.

Weaknesses found in its code:
- **Terrain:** one 4-vertex ArrayMesh per cell, 64 per landblock, each with a `StandardMaterial3D` per terrain type; no retail TexMerge corner/road alpha blending.
- **Streaming:** a fixed 3×3 ring with no unloading and no floating origin.
- **Player:** a `Node3D` with custom gravity, not `CharacterBody3D` and not the AC physics model.
- **Textures:** DXT decoded to RGBA on the CPU.

Lifestone started fresh and harvests from it only where useful.

## 1. Current Godot release and .NET requirements (checked 2026-10-09 via `gh api`)
- **Current stable: 4.7.2-stable**, published 2026-08-18.
  - Earlier 4.7 releases: 4.7-stable 2026-06-18, 4.7.1 2026-07-14. 4.6.3 shipped 2026-05-20.
  - The 3.x line is at 3.6.3 (2026-08-22).
  - 4.8 is still in development: 4.8-dev7 on 2026-10-01, in `godotengine/godot-builds`.
- **Windows x64 assets** on the 4.7.2-stable release (sizes in bytes):
  - `Godot_v4.7.2-stable_win64.exe.zip`: 86,013,866
  - `Godot_v4.7.2-stable_mono_win64.zip`: 116,582,061 (the .NET build to use)
  - `Godot_v4.7.2-stable_export_templates.tpz`: 1,281,349,702
  - `Godot_v4.7.2-stable_mono_export_templates.tpz`: 1,202,598,411
  - Also available: `_mono_windows_arm64.zip`, `_mono_win32.zip`.
- **.NET requirements at the 4.7.2 tag:**
  - `GodotSharp.csproj` targets `net8.0` with LangVersion 12.
  - `ProjectGenerator.cs` sets `GodotMinimumRequiredTfm => "net8.0"` and adds `net9.0` for Android.
  - The 4.7 docs say: "Godot 4.5 requires .NET 8 or later, but exporting to Android requires .NET 9 or later." C# projects cannot export to the web.
- **.NET release status:** 10.0 is the current LTS (runtime 10.0.12, SDK **10.0.401**, EOL 2028-11-14). **.NET 8 reaches EOL on 2026-11-10**, which affects the earlier prototype's pin.
- **`net10.0` projects work.**
  - godot#111246 ("Failed to load project assembly") only happened with the .NET 10 RC; commenters confirm the release SDK works.
  - `bclnet/GameX` ships `engines/Godot/Shell2D/Shell2D.csproj` with `Godot.NET.Sdk/4.6.2` and `<TargetFramework>net10.0`.
  - One known caveat is still open on 4.7: godot#120843. Assemblies brought in by `<FrameworkReference>` (for example `Microsoft.AspNetCore.App`) are not resolved in editor or debug runs. Avoid FrameworkReference. Normal NuGet PackageReferences are fine.
- Install the .NET SDK 10.0.4xx; .NET runtimes alone are not enough.
- **Defaults for new 4.6+ projects:** D3D12 is the default rendering driver on Windows, falling back to Vulkan and then OpenGL. Jolt is the default 3D physics engine (4.6 release notes).

## 2. What is actually in the user's data files
I wrote a read-only B-tree census script (`scratchpad/research/datcount.py`).

**`client_portal.dat`:** 79,694 files, block size 1024.

| ID range | Type | Files | Size |
|---|---|---|---|
| 0x06 | RenderSurface | 20,684 | 606.5 MB |
| 0x01 | GfxObj | 15,318 | 81.5 MB |
| 0x02 | Setup | 5,935 | |
| 0x03 | Animation | 2,066 | 56 MB |
| 0x04 | Palette | 4,521 | |
| 0x05 | SurfaceTexture | 7,221 | |
| 0x08 | Surface | 6,152 | |
| 0x0A | Wave | 786 | 52 MB |
| 0x0D | Environment | 772 | |
| 0x11 | Degrade | 4,131 | |
| 0x32 / 0x33 / 0x34 | particle / physics-script types | 2,051 / 4,248 / 164 | |

**`client_cell_1.dat`:** 805,348 files, block size 256.
- **65,025 LandBlocks (0xXXYYFFFF), exactly 255×255**
- 5,346 LandBlockInfo records
- 734,976 EnvCells
- The total across both files, about 885k, matches ACWeb's "885k files".

**RenderSurface pixel formats** (header layout `{id, category, int32 W, int32 H, uint32 PixelFormat, int32 len, byte[len], [uint32 DefaultPaletteId]}`, verified against `Chorizite/DatReaderWriter` `Generated/Enums/PixelFormat.generated.cs`):

| Format | Count | Size |
|---|---|---|
| A8R8G8B8 | 12,984 (mostly 32×32 icons and UI) | 137.6 MB |
| **INDEX16** (paletted) | 4,182 | 182.4 MB |
| DXT1 | 1,971 | 54.8 MB |
| R8G8B8 | 1,190 | 129.7 MB |
| DXT5 | 127 | |
| A8 | 86 | |
| CUSTOM_RAW_JPEG (0x1F4) | 79 | |
| CUSTOM_LSCAPE_R8G8B8 (0xF3) | 33 | |
| CUSTOM_LSCAPE_ALPHA (0xF4) | 16 | |
| P8 | 6 | |
| DXT3 | 5 | |
| R5G6B5 | 3 | |
| A4R4G4B4 | 2 | |

- Most common sizes: 32² (12,852), 128² (2,035), 256² (1,145), 512² (357).
- **What this means:** only about 10% of textures are DXT. Palette handling for INDEX16 matters far more than Godot's DXT support.
- `client_highres.dat` is **not present** in `<repo>\client`. OpenAC and the earlier prototype list it; AC:Unreal says it is optional.

## 3. Evaluating the implementation options
**GDScript.** Rejected as the core language.
- It is too slow for parsing and decoding about 885k records, CPU palette expansion, and a port of AC physics.
- There is no GDScript AC library; code search finds no GDScript code touching AC dats.
- It is fine for UI glue only.

**C# / .NET 10 inside Godot. Recommended.**
- It is the only stack where all of the major prior art drops in as source or NuGet:
  - `Chorizite.DatReaderWriter` 2.1.9 (2026-08-29, MIT; targets net8.0, netstandard2.0 and net48; schema-driven from `DatReaderWriter/dats.xml`).
  - OpenAC's `AcDream.Core`, `Runtime` and `Core.Net` (MIT, net10).
  - ACE `ACE.DatLoader` (netstandard2.0) and `ACE.Server/Physics/*` (AGPL, net10).
  - ACViewer (GPL-3, net8.0-windows, MonoGame 3.8.0.1641).
  - The earlier prototype's code.
- C# BinaryReader, Span and MemoryMappedFile are ideal for the DAT container.
- Keep all decoding and simulation in plain net10.0 class libraries with no Godot references, so they can be tested without the engine. OpenAC (`Core` / `Runtime` / `App`) and the earlier prototype (`AssetCore` / `Game.Core` / `Game`) both use this pattern.

**C++ GDExtension.** Not worth it as the primary language.
- godot-cpp is at 10.0.0-stable (2026-09-15).
- The only C++ AC code is old and GPL: AC2D (GPL-2, last push 2021), bzr (GPL-2, 2015), DungeonViewerAC (GPL-3, 2017), and GDLE/PhatAC.
- It needs a C++ toolchain, and iteration is slower.
- AC's polygon counts are tiny, so native speed is not needed. Use it only for proven hot spots.

**Rust GDExtension (godot-rust/gdext v0.5.5, MPL-2.0).** A viable alternative only if you build on `dereth-network/dereth`.
- Its `core/*` crates are MIT: dat, terrain, landscape, physics, animation, protocol, client-runtime.
- Against it: the repo is three weeks old, it needs a Rust toolchain, and the FFI adds complexity.

**Offline converter (dats → glTF/PNG/JSON imported by the Godot editor).** Rejected as the primary path.
- With about 885k records, editor import would be unworkable.
- glTF cannot express AC semantics: Surface translucency and luminosity flags, palette swaps through ClothingTable and PaletteSet, rigid multi-part Setups animated per part, physics BSP, and EnvCell portals.
- Shipping derived assets is an IP risk.
- Keep a dev-only exporter for debugging: ACViewer `FileExport.cs` writes OBJ, MTL and PNG; the earlier prototype has `tools/AssetForge`.

**Runtime DAT loading plus a per-machine bake cache. Recommended.**
- Read the DATs directly at runtime.
- Cache expensive derived data in a local file built once from the user's own DATs. OpenAC does this with `acdream.pak`, built by `AcDream.Bake`.
- The terrain for the whole world is only 65,025 × (81 terrain words + 81 height bytes), roughly 16 MB, so it can stay resident for far-LOD terrain.

### Godot specifics (checked at the 4.7.2 tag)
**Textures**
- `Image.FORMAT_DXT1` / `DXT3` / `DXT5` = 17 / 18 / 19. These accept AC DXT payloads unchanged.
- `Image.generate_mipmaps()` fails on compressed images ("Cannot generate mipmaps from compressed image formats", `core/io/image.cpp:2161`).
- Runtime BC compression is not available in export templates: etcpak's `_compress_bc` sits behind `TOOLS_ENABLED`, and cvtt and betsy are editor-only unless you build custom templates.
- So for DXT, either:
  - upload DXT without mipmaps (distant aliasing),
  - `Image.Decompress()` (bcdec ships), generate mipmaps and upload RGBA8, or
  - bake to DDS with mipmaps (BCnEncoder.Net, MIT, as used by OpenAC) and load with `Image.LoadDdsFromBuffer` (available in 4.7).
- Other formats:
  - A8R8G8B8 / R8G8B8: BGRA→RGBA swizzle, then `FORMAT_RGBA8` / `RGB8`.
  - RAW_JPEG: `Image.LoadJpgFromBuffer`.
  - INDEX16 / P8: CPU palette expansion with an LRU cache (the earlier prototype's `PalettedTextureCache`, 256 entries). Alternatively use `FORMAT_R16I` (43), which is new in 4.x, with a `usampler2D` index texture plus a palette texture lookup in the shader, giving palette swaps without re-decoding.
- `Texture2DArray.create_from_images` needs the same size, format and mip setting for every layer. That suits a terrain atlas.

**Terrain**
- Build one ArrayMesh per landblock: 8×8 cells × 2 triangles, non-indexed at 6 vertices per cell (384 vertices).
- The triangle split per cell comes from a hash. OpenAC `src/AcDream.Core/Terrain/TerrainBlending.cs` `CalculateSplitDirection`:
  - `dw = x*y*0x0CCAC033 - x*0x421BE3BD + y*0x6C1AC587 - 0x519B8F25`
  - `x = lbX*8 + cellX`; sign bit set gives SW→NE.
- Pack per-vertex data into `ARRAY_CUSTOM0..3` as `ARRAY_CUSTOM_RGBA8_UNORM` (verified enum). The values are the base layer, three overlay layers with alpha-mask index and rotation, and two road layers. This mirrors `AcDream.App/Rendering/Shaders/terrain_modern.vert`, which uses `uvec4 aPacked0..3` and `gl_VertexID % 6`. Godot spatial shaders expose `VERTEX_ID` and `CUSTOM0` (verified in `servers/rendering/shader_types.cpp`).
- The palCode, terrain/road code and rotation algorithms are in `TerrainBlending.cs` (`GetPalCode`, `PseudoRandomIndex`, `FindTerrainAlpha`, `FindRoadAlpha`); OpenAC says they were copied from WorldBuilder (MIT).
- The DAT layout is `LandBlock = {bool HasObjects; TerrainInfo[81] (ushort: Road mask 0x3, Type 0x7C>>2, Scenery 0xF800>>11); byte[81] Height; align}`. Heights map through `RegionDesc.LandDefs.LandHeightTable[256]`.
- Do not use Terrain3D (TokisanGames, MIT, v1.0.2): its regular triangulation does not match AC's per-cell split, it caps at 32 textures, and it is built around editing.
- Jolt `HeightMapShape3D` has the same triangulation mismatch. Use a `ConcavePolygonShape3D` (128 triangles per landblock) or AC's own terrain test.

**Meshes**
- GfxObj polygons are n-gon fans: `{byte numVerts; Stippling; SidesType; short PosSurface; short NegSurface; short[] VertexIds; byte[] PosUVIndices; …}`.
- SWVertex is `{ushort numUVs; Vector3 Origin; Vector3 Normal; Vec2[] UVs}`, so vertices must be split per (vertexId, uvIndex) and per surface.
- Physics polygons and BSP are separate from the drawing ones.
- Setups are rigid parts animated by per-part frames, with no skinning. Use a Node3D per part, or `RenderingServer` instances for crowds.
- Put repeated scenery in a `MultiMeshInstance3D` per (landblock, GfxObj). Godot culls a MultiMesh as one AABB, which is fine at landblock granularity.
- Use AC's own Degrade (0x11) for LOD. Use `VisibilityRange` for mid and far rings.

**Threading**
- Per the Godot docs, building resources and node subtrees off-thread is fine; attach them with `CallDeferred`. "Creating scene chunks outside the active tree is fine", but "only reliable with one loading thread".
- Avoid creating GPU textures off-thread. Decode on worker Tasks and do the final `ImageTexture` creation and `AddChild` on the main thread with a per-frame budget.

**Large world**
- Dereth is 255 × 192 m = 48,960 m across.
- The Godot docs place 32–65 km in the "limit for any 3D game" band for single precision (step about 0.0039).
- A `precision=double` build means compiling a custom editor and export templates (`.double` suffix), and it changes the GDExtension ABI. Do not do it.
- Use **landblock-granular floating origin** instead. AC Positions are already landblock-relative (objcell_id plus a local frame of 0–192 m), so rebase when the player is more than about 2–4 landblocks from the origin landblock. This keeps everything within about ±1 km (step 0.0001).
- Rebasing must also shift physics bodies, world-space particles and any world-space shader UVs.
- Coordinate mapping (the earlier prototype's convention): AC (x, y, z) with Z up maps to Godot (x, z, −y).

**Interiors**
- Godot 4 has no rooms/portals system, and `OccluderInstance3D` needs baked occluders.
- Implement an EnvCell portal flood-fill that toggles cell visibility. ACWeb, OpenAC and the earlier prototype's PLAN §3.4 all do this.

**Movement**
- Quick prototype: `CharacterBody3D` with Jolt and trimesh shapes from GfxObj PhysicsPolygons.
- Faithful version: port AC's sphere-sweep Transition physics and drive it yourself. Sources:
  - OpenAC `src/AcDream.Core/Physics/*`: `CollisionTraversal`, `CellTransit`, `BSPQuery`, `MotionInterpreter`, `Motion/MoveToManager` …; MIT, engine-agnostic.
  - ACE `Source/ACE.Server/Physics/{Animation,BSP,Collision,Common,…}`: AGPL.

## 4. Prior art (verified on GitHub 2026-10-09)
| Project | Stack / license | Last push | How far it got | Reuse |
|---|---|---|---|---|
| An earlier private prototype by the author | Godot 4.6.2 mono + .NET 8, AGPL-3.0 | — | Single-player Holtburg slice, combat, spells, HUD, saves | Harvest selectively |
| **eriknihlen/OpenAC** | C# .NET 10 (global.json 10.0.300), Vulkan via Silk.NET 2.23, MIT, v0.1.24 (2026-10-05), about 1,338 commits, about 18k tests, heavily AI-assisted | 2026-10-08 | "Fully playable against an ACEmulator server" (beta): streaming, terrain, scenery, sky, interiors, retail-like collision, spells, UI | **Very high.** `AcDream.Core` depends only on BCnEncoder.Net 2.2.1, Chorizite.DatReaderWriter 2.1.7, Serilog and Stb. `AcDream.Runtime` is a "presentation-independent kernel" that the headless host already runs without a window. A Godot frontend could replace `AcDream.App`. NOTICE says it contains WorldBuilder code (MIT) and "does not include code from" ACE, ACViewer or holtburger. |
| Chorizite/DatReaderWriter | C# MIT, NuGet 2.1.9 | 2026-08-29 | Full read and write of end-of-retail dats | Use directly |
| Chorizite/WorldBuilder | C# Avalonia/OpenGL; README says MIT but GitHub detects no LICENSE file | 2026-04-23 | Dat editor and renderer (terrain blending source) | Reference |
| ACEmulator/ACViewer | C# MonoGame 3.8, GPL-3.0 | 2026-08-13 (last commit 2026-07-07) | Viewer with terrain, models and particles; OBJ export (`FileExport.cs`); `Data/LootArmor.txt` | Reference (GPL) |
| ACEmulator/ACE | C# net10, AGPL-3.0, MySQL via Pomelo 9.0.0 | 2026-10-08 | Server, DatLoader, physics port | Server logic and reference |
| FictitiousReality/ACWeb | TypeScript + Three.js (WebGL2), **no license file** | 2026-09-16 | Browser client vs ACE: TexMerge terrain, PRNG scenery, interiors with BSP and portal visibility, animation, particles, collision | Read-only reference |
| dereth-network/dereth | Rust, Vulkan/wgpu (WGSL via naga) plus a web client; core/dereth/tools MIT, server (Empyrean) AGPL | 2026-10-09 (dereth-v0.4.0) | Client targeting acclient 11.6096 behaviour, plus a server | Rust alternative |
| enknamel/acswarm | Rust, GPL-3.0 | 2026-10-08 | Renders world, plays vs ACE, multi-session autoplay | Reference |
| merklejerk/holtburger | Rust, AGPL-3.0 | 2026-10-03 | TUI client; 3D client planned (Tauri) | Protocol and dat reference |
| salvia420-bit/WorldBuilder-ACME-Edition | C# + Rust/WASM + Three.js r184, AGPL-3.0 | 2026-10-10 | Browser client vs ACE (`external/holtburger/apps/holtburger-web`) | Reference |
| AC:Unreal (thwargle.com/unreal) | Unreal Engine 5, closed source; licence not stated | "Community preview v106" (2026-10-09) | Replacement client vs ACE and GDLE | None (closed) |
| darinh/ac-unreal | UE 5.7.4 C++, MIT | 2026-06-02 | "Likely never to be finished" experiment | Little |
| CrimsonMage/Vitaeum_AC | Rust client, distribution-only (no source) | 2026-10-09 | Launcher binaries | None |
| Y0oshi/MacAC | C# .NET 10 Vulkan, MIT | 2026-09-23 | macOS client vs ACE | Reference |
| w5ohr/Dereth | Three.js single HTML + Python server, no license, 665 MB repo of baked AC assets | 2026-07-21 | "Homage" action-RPG, not faithful | Avoid (redistributes derived assets) |
| bclnet/GameX | C# MIT, multi-engine (Godot 4.6.2, Unity, Unreal, Stride, OpenGL adapters) | 2026-10-04 | AC family "WB": read + GL only | Minor |
| jkolb/Asheron (Swift, MIT, 2019); Hentaya/acrender and cmoski/ACRender (C#, 2016–17); a409197988/bzr (C++ GPL-2, 2015); deregtd/AC2D (C++ GPL-2, 2021); derandark/DungeonViewerAC (C++ GPL-3, 2017) | various | stale | Early viewers and clients | Historical |
| Mag-nus/acclient-AI-RE | C, MIT | 2026-07-31 | Hex-Rays decomp of 11.4186 (from the original Turbine PDB) and **11.6096, MD5 116d9a66…, the same build as the user's** | RE reference |

- **No public Godot, Unity or Blender port exists.** `gh search repos` and `gh search code` found none, and a web search pairing AC with Godot found nothing.
- One unverified lead: a Discord scan (`WorldBuilder-ACME-Edition/external/holtburger/apps/holtburger-web/docs/ac-tools-discord-scan-2026-05-30.md`) mentions a "Godot client — Vanquish420 — Godot Engine + Rust GDExtension + hybrid physics". No repo was found.

## 5. Recommendation
1. **Engine:** Godot **4.7.2-stable .NET** (`Godot_v4.7.2-stable_mono_win64.zip`) with the Forward+ renderer (D3D12 by default on Windows).
2. **Language:** C# targeting `net10.0` on the .NET SDK **10.0.4xx** (LTS). The solution layout:
   - `Ac.Data`: DatReaderWriter wrappers, decoders, bake cache. No Godot.
   - `Ac.Sim`: physics, motion, rules. No Godot; port from OpenAC Core (MIT) and ACE (AGPL).
   - `Game` (Godot.NET.Sdk/4.7.2): presentation only.
   - xUnit tests on the non-Godot libraries.
3. **Assets:** read the DATs at runtime with a per-machine bake cache (DDS with mipmaps, flattened collision, terrain blend tables). No editor import pipeline, and nothing derived gets committed.
4. **Rendering:**
   - Custom terrain ShaderMaterial (Texture2DArray atlas + alpha-mask array + CUSTOM0..3 packing).
   - ArrayMesh per GfxObj surface, MultiMesh for scenery, landblock streaming rings.
   - Landblock-granular floating origin.
   - Manual EnvCell portal culling.
5. **Architecture choice** (this decides what gets reused):
   - **(A)** Godot frontend over OpenAC's `AcDream.Runtime` + `Core.Net` (MIT) talking to a localhost ACE server. This is the fastest route to full game content.
   - **(B)** Prototype-style in-process simulation that reads the ACE-World SQL directly (true offline, but much more game-logic work).
   - .NET 10 serves both. Put a game-state interface between the simulation and presentation layers so A can ship first and B can replace it later.
6. **Decision:** extend the earlier prototype, or start fresh and port its useful pieces (Lifestone started fresh).

## KEY FACTS
- Current Godot stable is 4.7.2-stable, published 2026-08-18. 4.8 is still in development (4.8-dev7, 2026-10-01). Source: gh api repos/godotengine/godot/releases and godotengine/godot-builds releases.
- Windows x64 assets on the 4.7.2 release: Godot_v4.7.2-stable_mono_win64.zip is 116,582,061 bytes; Godot_v4.7.2-stable_win64.exe.zip is 86,013,866; mono export templates (.tpz) are 1,202,598,411; standard export templates are 1,281,349,702. Source: gh api releases/tags/4.7.2-stable.
- At the 4.7.2 tag, GodotSharp targets net8.0 and GodotTools sets GodotMinimumRequiredTfm to net8.0, with net9.0 for Android. The docs say Godot requires .NET 8 or later and C# cannot export to the web. Sources: modules/mono/glue/GodotSharp/GodotSharp.csproj, GodotTools.ProjectEditor/ProjectGenerator.cs, docs c_sharp_basics.
- Godot net10.0 projects work with the release .NET 10 SDK: godot#111246 failed only on the RC; bclnet/GameX Shell2D.csproj uses Godot.NET.Sdk/4.6.2 with net10.0. godot#120843 (FrameworkReference assemblies not resolved in editor runs) is still open on 4.7.
- .NET 10 is LTS (SDK 10.0.401, EOL 2028-11-14). .NET 8 reaches EOL on 2026-11-10. Source: dotnet releases-index.json.
- An earlier private prototype by the author (Godot 4.6.2 mono + .NET 8, AGPL-3.0) is a single-player AC recreation with a Holtburg slice, combat, spells and saves.
- The earlier prototype's terrain uses one 4-vertex ArrayMesh per cell with a StandardMaterial3D per terrain type and no TexMerge blending. Streaming is a fixed 3x3 ring and floating origin is not done yet. Source: LandblockRenderer.cs, HeightmapMeshBuilder.cs, LandblockStreamer.cs.
- The user's client_cell_1.dat has 805,348 records: 65,025 LandBlocks (exactly 255x255), 5,346 LandBlockInfo and 734,976 EnvCells. client_portal.dat has 79,694 records, and its 20,684 RenderSurfaces total 606.5 MB. Source: local read-only B-tree census (scratchpad/research/datcount.py).
- RenderSurface formats in the user's portal.dat: A8R8G8B8 12,984; INDEX16 4,182 (182 MB); DXT1 1,971; R8G8B8 1,190; DXT5 127; A8 86; RAW_JPEG 79; LSCAPE_R8G8B8 33; LSCAPE_ALPHA 16; P8 6; DXT3 5. Only about 10% are DXT. Source: local census plus DatReaderWriter PixelFormat.generated.cs.
- client_highres.dat is not present in <repo>\client. Source: local directory listing.
- Godot 4.7.2 Image provides FORMAT_DXT1/DXT3/DXT5 (17/18/19), FORMAT_R16I (43), load_dds_from_buffer and save_dds. generate_mipmaps() fails on compressed images (core/io/image.cpp:2161). Source: doc/classes/Image.xml and image.cpp at the 4.7.2 tag.
- BC (S3TC) compression is not available in official export templates: etcpak's _compress_bc is under TOOLS_ENABLED, and cvtt and betsy are editor-only unless *_export_templates=yes. Decompression is available. Source: modules/etcpak/register_types.cpp, cvtt/betsy config.py at 4.7.2.
- Godot spatial shaders expose VERTEX_ID and CUSTOM0..3, and Mesh supports ARRAY_CUSTOM_RGBA8_UNORM through RGBA_FLOAT. Source: servers/rendering/shader_types.cpp and doc/classes/Mesh.xml at 4.7.2.
- The Godot docs put 32-65 km in the 'limit for any 3D game' band for single precision (step about 0.0039). Double precision needs custom builds with precision=double and breaks GDExtension ABI compatibility. Source: docs large_world_coordinates.
- Godot 4.6 made Jolt the default 3D physics engine and D3D12 the default Windows rendering driver for new projects. Source: Godot 4.6 release coverage.
- OpenAC (eriknihlen/OpenAC) is MIT, .NET 10, renders with Vulkan via Silk.NET 2.23, is at v0.1.24 (2026-10-05), and claims to be fully playable against ACE. AcDream.Core has no GPU dependency: it uses BCnEncoder.Net 2.2.1, Chorizite.DatReaderWriter 2.1.7, Serilog and Stb. AcDream.Runtime is a presentation-independent GameRuntime. Source: README, docs/architecture.md, csproj files, Directory.Packages.props.
- OpenAC's terrain blending is in src/AcDream.Core/Terrain/TerrainBlending.cs (palCode, the split-direction hash and alpha rotation), copied from WorldBuilder (MIT) per NOTICE.md. OpenAC states it was 'verified against, but does not include code from' ACE, ACViewer and holtburger.
- Chorizite.DatReaderWriter latest is release/2.1.9 (2026-08-29). It is MIT and targets net8.0, netstandard2.0 and net48. Source: gh releases and README.
- ACE is net10.0, uses MySQL via Pomelo.EntityFrameworkCore.MySql 9.0.0, and is AGPL-3.0. ACE.DatLoader targets netstandard2.0. Physics lives in Source/ACE.Server/Physics/{Animation,BSP,Collision,Common,...}. Source: ACE csproj files and tree.
- ACViewer is GPL-3.0, net8.0-windows, MonoGame 3.8.0.1641 plus AssimpNet, last commit 2026-07-07, and has OBJ/MTL/PNG model export in ACViewer/FileExport.cs. Source: gh api.
- No public Godot, Unity, Blender or Bevy port of AC was found on GitHub. Web ports: FictitiousReality/ACWeb (Three.js, no license), and the WorldBuilder-ACME-Edition browser client (Three.js r184, AGPL). Rust clients: dereth-network/dereth (MIT core), enknamel/acswarm (GPL-3), merklejerk/holtburger (AGPL, TUI). AC:Unreal (UE5, closed, preview v106) is listed at thwargle.com/unreal. Source: gh search repos/code, thwargle.com.
- Mag-nus/acclient-AI-RE (MIT) contains decompilations for acclient 11.6096 with MD5 116d9a66a70b6af449dc3a28d82f2f6d, identical to the user's binary, and for 11.4186, which has the original Turbine PDB. Source: its README.
- Terrain3D (TokisanGames) v1.0.2-stable (2026-05-19, MIT) supports up to 65.5 km and 32 textures, but uses a regular-grid triangulation. Source: gh api and README.

## RECOMMENDATIONS
- Use Godot 4.7.2-stable .NET (Godot_v4.7.2-stable_mono_win64.zip) with C# targeting net10.0 on the .NET SDK 10.0.4xx. Install the SDK first; runtimes alone are not enough. Do not adopt .NET 8, which reaches EOL on 2026-11-10.
- Keep all DAT decoding and simulation in plain net10.0 class libraries with no Godot references, with xUnit tests. The Godot project should be presentation only, following OpenAC's Core/Runtime/App and the earlier prototype's AssetCore/Game.Core/Game layering.
- Read DATs at runtime with Chorizite.DatReaderWriter 2.1.9 (MIT) and keep a per-machine bake cache: DDS with mipmaps via BCnEncoder.Net and Image.LoadDdsFromBuffer, flattened collision, terrain blend tables. Do not build a glTF/editor-import pipeline for about 885k records.
- Terrain: one custom ArrayMesh per landblock (6 non-indexed verts per cell, AC split-direction hash) and a ShaderMaterial using Texture2DArray terrain and alpha-mask arrays, with per-vertex CUSTOM0..3 RGBA8 packing and VERTEX_ID. Port TerrainBlending.cs from OpenAC (MIT). Do not use Terrain3D or HeightMapShape3D.
- Textures: DXT1/3/5 straight to Image.FORMAT_DXT*, or decompress and mipmap. Swizzle A8R8G8B8/R8G8B8. Use CPU palette expansion with an LRU cache for INDEX16 (20% of textures, 182 MB), optionally an R16I index texture plus a palette shader for live palette swaps. Load RAW_JPEG with Image.LoadJpgFromBuffer.
- Use a landblock-granular floating origin (AC Position is already landblock-relative), not a precision=double engine build. Rebase physics, world-space particles and shader UVs on origin shifts.
- Stream on worker Tasks with a main-thread attach budget via CallDeferred. Use a MultiMesh per (landblock, GfxObj) for scenery, VisibilityRange for mid and far rings, and a resident whole-world coarse terrain (65,025 x 81 heights is about 16 MB).
- Implement EnvCell portal flood-fill culling manually; Godot 4 has no rooms/portals system.
- Movement: prototype with CharacterBody3D, Jolt and trimesh shapes from GfxObj PhysicsPolygons. For retail feel, port the AC Transition physics from OpenAC src/AcDream.Core/Physics (MIT) or ACE Source/ACE.Server/Physics (AGPL).
- Pick the single-player architecture early. (A) Godot frontend over OpenAC AcDream.Runtime + Core.Net talking to a localhost ACE is the fastest route to full content. (B) An in-process simulation over the ACE-World SQL is a true offline single binary but much more work. Put a game-state interface between simulation and presentation so A can ship first and B can replace it later.
- Use FictitiousReality/ACWeb (no license) and ACViewer (GPL-3) as read-only behaviour references. Copy code only from MIT sources (OpenAC, DatReaderWriter, WorldBuilder), or accept AGPL if porting from ACE or holtburger; the earlier prototype is already AGPL.
- Use Mag-nus/acclient-AI-RE, which decompiles exactly the user's acclient 11.6096 (MD5 match), before spending effort on new Ghidra or REA analysis.

## RISKS
- Not yet tested: Godot 4.7.2 + net10.0 builds and runs. The evidence is issue comments and bclnet/GameX; do a smoke test first. godot#120843 (FrameworkReference not resolved in editor runs) is still open.
- OpenAC, dereth-network/dereth, acswarm, holtburger, MacAC and the earlier prototype are all 1-2 months old, heavily AI-assisted and changing fast. Their 'retail-faithful' and 'fully playable' claims and OpenAC's provenance claim (no ACE code) are unverified. API stability for reusing AcDream.Runtime as a library is unknown.
- Not verified: whether OpenAC's AcDream.Runtime can be driven by a non-Silk host without pulling in App-layer assumptions. Its Headless host suggests it can.
- Chorizite/WorldBuilder's README says MIT, but GitHub detects no LICENSE file, so the license is ambiguous. FictitiousReality/ACWeb and w5ohr/Dereth have no license (all rights reserved).
- client_highres.dat is missing from the user's install. OpenAC lists it among the data files; it may be needed for some high-res surfaces or SurfaceTexture levels.
- Official Godot export templates cannot BC-compress at runtime, and compressed images cannot get generated mipmaps. Choose between uncompressed RGBA8 VRAM cost and a bake step.
- The Godot client by 'Vanquish420' (Godot + Rust GDExtension) is only a Discord lead; no repo was found.
- AC:Unreal and Vitaeum are closed source, so nothing can be learned from their code.
- Single-player via a local ACE means bundling or running MySQL/MariaDB (ACE uses Pomelo MySQL), which adds heavy runtime dependencies. The in-process alternative means re-implementing or porting a large amount of AGPL game logic.
- Draw-call and node counts for dense towns at retail view distance in Godot Forward+/D3D12 are unmeasured. Some objects may need merging into per-landblock meshes or RenderingServer instances.
- The RenderSurface census assumes a header of {id, dataCategory, W, H, format}. The decoded formats and sizes were all plausible, which supports the assumption, but it has not been cross-checked with DatReaderWriter on the user's dats.

## SOURCES
- https://github.com/godotengine/godot/releases/tag/4.7.2-stable
- https://github.com/godotengine/godot-builds/releases
- https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/glue/GodotSharp/GodotSharp/GodotSharp.csproj
- https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs
- https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Image.xml
- https://github.com/godotengine/godot/blob/4.7.2-stable/core/io/image.cpp
- https://github.com/godotengine/godot/blob/4.7.2-stable/modules/etcpak/register_types.cpp
- https://github.com/godotengine/godot/blob/4.7.2-stable/servers/rendering/shader_types.cpp
- https://github.com/godotengine/godot/blob/4.7.2-stable/doc/classes/Mesh.xml
- https://github.com/godotengine/godot/issues/111246
- https://github.com/godotengine/godot/issues/112701
- https://github.com/godotengine/godot/issues/120843
- https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html
- https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html
- https://docs.godotengine.org/en/stable/tutorials/performance/thread_safe_apis.html
- https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json
- https://godotengine.org/releases/4.6/
- https://godotengine.org/releases/4.7/
- https://github.com/eriknihlen/OpenAC (README.md, docs/architecture.md, NOTICE.md, Directory.Packages.props, src/AcDream.Core/Terrain/TerrainBlending.cs, src/AcDream.App/Rendering/Shaders/terrain_modern.vert)
- https://github.com/Chorizite/DatReaderWriter (README.md, DatReaderWriter/dats.xml, Generated/Enums/PixelFormat.generated.cs, Generated/DBObjs/RenderSurface.generated.cs)
- https://github.com/Chorizite/WorldBuilder
- https://github.com/ACEmulator/ACE
- https://github.com/ACEmulator/ACViewer (ACViewer/FileExport.cs, ACViewer.csproj)
- https://github.com/ACEmulator/ACE-World-16PY-Patches
- https://github.com/FictitiousReality/ACWeb
- https://github.com/dereth-network/dereth
- https://github.com/enknamel/acswarm
- https://github.com/merklejerk/holtburger
- https://github.com/salvia420-bit/WorldBuilder-ACME-Edition (external/holtburger/apps/holtburger-web/docs/ac-tools-discord-scan-2026-05-30.md)
- https://www.thwargle.com/unreal/
- https://github.com/lkasser/Decal-ACUnreal
- https://github.com/darinh/ac-unreal
- https://github.com/CrimsonMage/Vitaeum_AC
- https://github.com/Y0oshi/MacAC
- https://github.com/w5ohr/Dereth
- https://github.com/bclnet/GameX
- https://github.com/Mag-nus/acclient-AI-RE
- https://github.com/Chorizite/ACBindings
- https://github.com/amoeba/asheron-rs
- https://github.com/CrimsonMage/DATRusterWriter
- https://github.com/deregtd/AC2D
- https://github.com/a409197988/bzr
- https://github.com/derandark/DungeonViewerAC
- https://github.com/jkolb/Asheron
- https://github.com/TokisanGames/Terrain3D
- https://github.com/godot-rust/gdext
- https://github.com/godotengine/godot-cpp
- https://github.com/radek-sprta/awesome-game-remakes
- Local read-only census: <scratch>\research\datcount.py run on <repo>\client\client_portal.dat and client_cell_1.dat