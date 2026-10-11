# Terrain renderer (M1 wave 2)

**Tracks:**
- terrain renderer (wave 2a): `src/Lifestone.Game/scripts/World/Terrain/`, `src/Lifestone.Game/shaders/terrain/`, `scenes/TerrainPreview.tscn`, and the engine-agnostic helpers in `src/Lifestone.Data/Terrain/Blending/` (`TerrainAtlasBuilder`, `TerrainAtlasPixels`, `TerrainMipChain`, `TerrainRenderMesh`, `TerrainSeamlessNormals`);
- terrain integration (wave 2b): the streaming layer in the main scene, the global uniforms, ground samplers, start-up, measurements (§5, §7.1), and `World/TerrainLayer.cs` (the placeholder, now behind `--placeholder-terrain`).

**Spec:** [terrain-shader.md](terrain-shader.md) (what the shader computes). This document says how Lifestone does it in Godot, how it streams, and how it was checked.
**Authority:** retail functions are cited by name (and `acclient.c` line) from the Mag-nus/acclient-AI-RE decompile of acclient 11.4186. Nothing from it is copied. Other sources: [third-party/terrain-renderer.md](third-party/terrain-renderer.md) and [third-party/terrain-integration.md](third-party/terrain-integration.md).

Status:
- The shader reproduces the CPU reference to within rounding on 5 landblocks (golden test, §6).
- **The game draws it.** `tools\godot.ps1 -Run` opens at Holtburg on the TexMerge terrain, streamed by `TerrainLandblockLayer` and lit by the render contract's global uniforms. Walk mode follows the same ground.
- The screenshots look like retail AC: green-yellow grass, brown dirt and mud, orange desert sand, cobbled roads with soft verges, teal marsh water, snow fields with rock outcrops, sand beaches with shallow and deep water.

## 1. How it works

```
DatSet ──► TerrainAtlasBuilder (worker) ──► TerrainAtlasPixels ──► PreparedTerrainAtlas (Godot Images, worker)
                                                                       │
                                                                       ▼  main thread, once (CPU copies freed)
                                                     TerrainRenderer: 2 Texture2DArrays + 1 ShaderMaterial
LandblockTerrain ──► TerrainBlockBuilder (worker) ──► PreparedTerrainBlock (Godot axes) + TerrainSampler
                                                                       │
                                                                       ▼  main thread, per landblock (one step)
                                   MeshInstance3D (ArrayMesh, 256 vertices, 384 indices) + GroundHeights.Register
```

1. **Atlas.** `TerrainAtlasBuilder.FromDats` decodes every colour layer and alpha map that `dats.TerrainAtlas` lists, in the manifest's layer order, and builds box-filtered mip chains. `TerrainRenderer.PrepareAtlas` wraps the bytes in Godot `Image`s on the worker. The `TerrainRenderer` constructor creates the two `Texture2DArray`s and the shared `ShaderMaterial` on the main thread, then frees the images and drops the managed copy (§2, "Memory").
   - *Wave-3 performance pass:* the layers are now read and decoded one after another (the mip chains are still built in parallel). Decoded on up to 30 threads, every thread kept the 2 MB read buffers it had rented in `ArrayPool<byte>.Shared` (DatReaderWriter rents one per read), so about 60 MB stayed alive for minutes after the upload although the atlas itself was freed (the audit's "terrain start-up retention"; in the game the terrain-only live heap after a full GC falls from 70 to 12 MB, game-core.md §7.3). Measured in a test (build the atlas from the dats in a fresh process, drop it, full compacting GC; 32 hardware threads, Debug, three runs each): 80–82 MB retained with the parallel decode, 5 MB with the sequential one, for 111–114 ms of wall time on the worker instead of 46–49 ms, which the main thread's warm-ups hide (the same figures as the `parallelDecode` comment in `TerrainAtlasBuilder`). docs/textures.md §9.
2. **Mesh.** `TerrainBlockBuilder.Prepare(coord)` (also `TerrainRenderer.PrepareBlock`) reads the landblock, builds `TerrainRenderMesh` (retail split, LandHeightTable heights, `calc_lighting` normals, packed recipes) and converts it with `AcToGodot`. It needs only the atlas *manifest*, so landblocks prepare while the atlas still decodes. `AttachBlock` turns it into an `ArrayMesh` under the landblock's root node.
3. **Shader.** `shaders/terrain/terrain.gdshader` evaluates retail's merged cell texture per pixel (docs/terrain-shader.md §5), multiplies it by the retail vertex light and applies D3D linear range fog. It is `unshaded`.
4. **Lighting and fog** are the render contract's **global** shader uniforms (§4), set by `RenderGlobals` (docs/render-contract.md §1): retail noon by default, the sky track's time of day later. Terrain and objects are lit by one driver.
5. **Streaming.** `TerrainLandblockLayer` is the game's terrain `ILandblockLayer` (§5). Each attach also registers the landblock's exact ground sampler, so walk mode follows the drawn ground.

All landblocks share one material, so each landblock is one draw call.

## 2. Texture arrays

| Array | Layers (Dereth) | Format | Size | Sampler hints |
|---|---|---|---|---|
| `terrain_layers` | 30 colour layers, `TerrainAtlasManifest.TerrainLayers` order | RGBA8, raw bytes | 512², full mip chain | `filter_linear_mipmap, repeat_enable` |
| `alpha_layers` | 8 alpha maps: 4 corner, 1 side, 3 road (`AlphaLayers` order) | R8, raw bytes | 512², full mip chain | `filter_linear_mipmap, repeat_disable` |
| `terrain_layers_nearest`, `alpha_layers_nearest` | the same arrays | | | `filter_nearest` (golden views only) |

- **Decoding.**
  - Colour layers use `TextureDecodeMode.Rgba8`. They are A8R8G8B8 (stored B,G,R,A), so they come out R,G,B,A.
  - Alpha maps are `PFID_CUSTOM_LSCAPE_ALPHA`, decoded `Native` to the raw byte that `ImgTex::MergeTexture` reads.
  - Anything else throws instead of guessing.
  - `TerrainAtlasBuilderTests` checks every level 0 against an independent raw BGRA decode (`TerrainTestData.DecodeColor`) and checks the colours themselves: grass greener than blue, dirt R > G > B.
- **Which SurfaceTexture level.**
  - The builder uses `TextureDetail.High`: level [0], from `client_highres.dat` first.
  - Retail's default `ImgTex::GetSurfaceDID` would drop to level [1] (docs/textures.md §4).
  - For terrain the choice changes nothing, and `The_SurfaceTexture_level_choice_does_not_change_the_terrain_pixels` pins this:
    - all 30 colour layers are single-level portal SurfaceTextures;
    - each alpha map's highres level [0] has the same bytes as its portal level [1].
- **Sizes.**
  - All 38 layers are 512², so nothing is resized.
  - A region whose colour layers (or whose alpha maps) differ in size **fails loudly**: `TerrainAtlasBuilder` throws a `TODO(terrain)` `NotSupportedException` naming every layer and its size. Retail's `ImgTex::TileCSI`/`ImgTex::MergeTexture` address each layer by its own size and only support layer × tiling = BaseTexSize, so resampling a layer into a shared array would be invented behaviour. (Until wave 2b a smaller layer was pixel-replicated, with only a logged warning.) `TerrainAtlasBuilderTests.Layers_of_different_sizes_fail_loudly_instead_of_being_resampled` pins it.
  - The colour array and the alpha array may differ from each other; each has one size.
  - Non-square or non-power-of-two layers throw.
- **Memory.** After the upload the renderer frees the Godot images (≈ 44 MB native) and drops its reference to the decoded `TerrainAtlasPixels` (≈ 44 MB managed). Only the golden test keeps the CPU copy (`new TerrainRenderer(..., keepAtlasPixels: true)`, `TerrainRenderer.AtlasPixels`), because its reference compositor samples it. `TerrainRenderer.AtlasSummary` keeps the layer counts and sizes.
  - **Checked once in the game** (temporary instrumentation, not committed): a weak reference to the pixels is dead after a full collection, and a full collection right after the upload takes the managed heap from 150 MB to 67 MB. Most of those 83 MB are the atlas build's large-object garbage (decode buffers and the mip chains).
  - **Without a forced collection** that garbage stays until the runtime's next gen-2 collection, which a Holtburg → Arwic flight does not reach; the perf overlay then shows ≈ 163 MB "managed" against ≈ 12 MB with the placeholder. A background collection requested after the upload (also tried: 0.2 + 0.3 ms pauses) returns it at start-up. Lifestone leaves GC policy to game core, so this is a request in the report, not a change here.
- **Layer identity.** Layers 9 (PatchyGrassland) and 26 (Moss) are different SurfaceTextures that point at the same RenderSurface 0x06006D3C. They keep separate layers because the manifest keys by SurfaceTexture.
- **Colour space (verified in Godot 4.7.2, Vulkan Forward+).** A one-off experiment (2026-10-10) rendered the colour array top-down with an orthographic camera and compared the screen with the texel bytes over 48,006 channel values on each of 0xA9B4 and 0x7E62. It used a temporary sampler with `source_color, filter_nearest` and wrote the sample straight to `ALBEDO`. The temporary code was then removed.

  | Sampler | Output | Screen vs raw bytes | Screen vs `linear_to_srgb(raw)` |
  |---|---|---|---|
  | `source_color` | `ALBEDO = sample` | max 0 | mean 47–68, max 73 levels |
  | no hint | `ALBEDO = sample` | mean 47–68, max 73 levels | max 0.56 |
  | no hint (the shipped shader) | arithmetic on raw values, then `ALBEDO = ac_srgb_to_linear(c)` | max 0.64 (golden test) | |

  - **Meaning.** In Godot 4.7, `source_color` makes the sampler decode sRGB to linear in hardware. Without the hint the shader receives byte / 255. The Linear tonemapper at exposure 1 re-encodes linear output to sRGB.
  - **Terrain.** The terrain samplers therefore have **no** hint, and every step (merge, modulate, fog) runs on raw values, as retail's byte arithmetic does (docs/terrain-shader.md §6). One exact `ac_srgb_to_linear` at the end makes the screen show retail's bytes. *(Wave 3.)* In the game's default byte blend space (render-contract.md §2.3) the shader hands over `c` itself (`ac_byte_space`), and the main view converts the whole frame once. The result for terrain is the same byte; the preview and this golden test stay in linear output space.
  - **Why not `source_color` with linear arithmetic?** It would blend in linear space and so change every partial blend; retail blended bytes.
  - **Guard.** `TerrainShaderModelTests.Shader_source_declares_the_contract_and_the_retail_rotations` fails if `source_color` appears in shader code.

### Mip policy

- **Retail.**
  - `ImgTex::CreateD3DTexture` (acclient.c:366008) creates `min(4, log2(size) + 1)` levels with `D3DXFilterTexture(D3DX_FILTER_BOX | D3DX_FILTER_MIRROR)`. The decompiled `CreateTexture` call is garbled, so the cap is very likely but not proven (docs/textures.md §7).
  - The texture it mip-maps is the *merged* 1024² cell texture (`TexMerge::MakeNewSurface` → `ImgTex::CreateLScapeTexture`).
  - So retail never samples terrain coarser than 128 texels per 24 m cell, and distant terrain shimmers.
- **Lifestone.**
  - Each layer has a full box-filtered chain (`TerrainMipChain`, `(a+b+c+d+2)>>2`), because a Godot `Image` cannot hold a partial chain.
  - The shader computes the merged texture's level of detail per pixel: `textureQueryLod(alpha_layers, UV).y + alpha_lod_offset`. Here `alpha_lod_offset = log2(1024 / 512) = 1`, because retail upscales the alpha map to the merged size.
  - It clamps that LOD to `[0, ac_max_mip_level]`, with **3 by default, i.e. retail's 4 levels**.
  - It then samples each array at the level with the same texel density:
    - colour layer: `lod − layer_lod_offset[l]`, where `log2(1024 / (512 × 2)) = 0`;
    - alpha map: `lod − 1`.
  - So Lifestone never minifies past retail's level 3.
  - `ac_max_mip_level = 16` gives Godot's full chain (less shimmer, not retail). Keys in the preview: `M`; `--max-mip=F`. This is the render contract's `ac_max_mip_level`, so objects and terrain switch together.
- **Comparison.** `terrain-mip-{0,3,16}.png` show the same grazing view with no mips, the retail cap and the full chain. 3 and 16 differ only far away.
- **Filtering.** Retail uses LINEAR min/mag/mip (trilinear), no anisotropy. `textureLod` with an explicit LOD is trilinear and isotropic, as retail. Grazing views blur along the view direction exactly as an isotropic D3D9 sampler does.

## 3. Mesh and per-vertex data

`TerrainRenderMesh.Build(terrain, region, texMerge, gridNormals?)` (engine-agnostic) → `TerrainBlockMesh.Prepare` (Godot axes) → `TerrainBlockMesh.Build` (main thread).

- **Indexed, 4 vertices per cell.**
  - Cell `c = cellX*8 + cellY` owns vertices `4c … 4c+3` = SW, SE, NE, NW.
  - 256 vertices and 384 indices per landblock.
  - The triangles are exactly `TerrainMeshBuilder`'s: same order, same per-cell diagonal from `TerrainSplit` (`CLandBlockStruct::ConstructPolygons`), counter-clockwise from above.
  - Both triangles of a cell share its corners, so the blend data is flat per cell and the UV is one affine map per cell. That is a third fewer vertices than the 384 non-indexed ones of `TerrainBlendVertices`.
- **Conversion.** Only through `AcToGodot`: `Positions` and `Directions` for positions and normals, and `TriangleIndices` for winding. Each AC triangle (a, b, c) becomes (a, c, b); the material uses `cull_back`.

| Attribute | Content |
|---|---|
| VERTEX | landblock-local position `(x·24, LandHeightTable[h], −y·24)` (Godot axes) |
| NORMAL | retail lighting normal of the source grid vertex (`TerrainVertexLighting.ComputeNormals` = `CLandBlockStruct::calc_lighting`) |
| UV | cell UV: SW (0,1), SE (1,1), NE (1,0), NW (0,0) (`land_uvs`, rotation 0) |
| CUSTOM0 (RGBA8 unorm) | base, overlay0, overlay1, overlay2 colour layers |
| CUSTOM1 (RGBA8 unorm) | overlay0, overlay1, overlay2, road0 alpha layers |
| CUSTOM2 (RGBA8 unorm) | road1 alpha layer, `rot0 \| rot1<<2 \| rot2<<4 \| roadRot0<<6`, `roadRot1 \| flags<<2`, road colour layer |

- **Packing.** The bytes are `TerrainBlendPacking.Pack` of the cell's `TerrainCellBlend`. 255 marks an unused slot, and slots are always a prefix.
- **Tests.** `TerrainRenderMeshTests` checks, on six real landblocks and Holtburg's 5×5, that every vertex decodes back to `TerrainTexMerge`'s recipe and the manifest's layers. It also checks geometry, normals, UVs, triangle order and winding.
- **No CUSTOM3.** Lighting is computed in the shader from NORMAL, so relighting is a uniform change. That is what retail's every-`LightTickSize` relight of all loaded blocks amounts to.

## 4. Uniforms

The lighting and fog inputs are the render contract's **global** shader uniforms (docs/render-contract.md §1): declared in `project.godot` `[shader_globals]`, read by `terrain.gdshader` as `global uniform`, and set by `RenderGlobals` (`Apply(SkyState)`, `ApplyDefaults` at start-up; the sky track drives them later). The terrain material has no copy of them, so one driver lights terrain and objects together. Values are raw retail colours (byte × 0.0039215689) and Godot-axis directions.

| Global uniform | Meaning | Set by `RenderGlobals` from `SkyState` |
|---|---|---|
| `ac_sun_dir` | unit vector towards the sun | `AcToGodot.Direction(Lighting.SunDirection)` |
| `ac_sun_color` | `sunlight_color × |sunlight|` | `Lighting.SunDiffuse` |
| `ac_land_ambient` | `ambient_color × ambient_level` (level floored at 0.2) | `Lighting.LandscapeAmbient` |
| `ac_fog_color`, `ac_fog_start`, `ac_fog_end`, `ac_fog_enabled` | D3D fog colour, start, end; world fog on | `Fog.Color`, `Fog.Start`, `Fog.End`, `Fog.Enabled` |
| `ac_max_mip_level` | highest merged-texture mip level (3 = retail) | `ApplyDefaults(…, maxMipLevel)` (`--max-mip`) |

- **Tools.** The preview and the golden test set the same globals: `TerrainRenderer.ApplySky(sky, fog)` is `RenderGlobals.Apply(sky)` with world fog forced off when `fog` is false (preview key `F`, golden test), and `TerrainRenderer.MaxMipLevel` reads and writes the global `ac_max_mip_level` (preview key `M`).
- **Pinned** by `TerrainShaderGlobalsTests`: every `global uniform` of the shader exists in `project.godot` with the same type, the eight contract inputs are globals, no material `ac_*` fallback remains, and terrain never reads `ac_ambient`.

- **Terrain ambient.** Terrain uses `ac_land_ambient`, **not** `ac_ambient`. In the core contract `ac_ambient` is the *object* ambient `ambient_color × (ambient_level + 0.2·|sunlight|)` (`LScape::calc_object_light`). Terrain is lit only by `calc_lighting`'s `ambient_color × ambient_level`.
- **Sun term.** `sunColor/255 · max(0, n·sunlight)` with the unnormalised `sunlight` equals `ac_sun_color · max(0, n·ac_sun_dir)`.
- **Per-vertex light** (`vertex()`):
  - `v_light = floor(min(ac_sun_color·d + ac_land_ambient, 1)·255)/255`. The truncation is `ACRender::landPolyDraw` sending `(uint8)(c·255.0)`.
  - Gouraud-interpolated.
- **Fog** (`vertex()`):
  - `f = clamp((end − d)/(end − start), 0, 1)`, with d the eye-to-vertex distance. That is `RenderDeviceD3D::SetDefaultD3DStates`: FOGVERTEXMODE LINEAR and RANGEFOGENABLE.
  - Interpolated, then `mix(fog_color, c, f)` after the modulate, in byte space.
- **Material-only uniforms** (set by `TerrainRenderer`):
  - `layer_tiling[64]` (TexTiling, 2 in Dereth: a texture repeats every 12 m);
  - `layer_lod_offset[64]`;
  - `alpha_lod_offset`;
  - `debug_view`;
  - `z_fight_adjust` (0.01 m: `landPolyDraw` lowers terrain by `zFightTerrainAdjust`).

### Debug views (`TerrainRenderer.DebugView`, `--debug-view=N`, keys 0-6 in the preview)

| N | View |
|---|---|
| 0 | retail: merged × light, fogged |
| 1 | raw merged colour, nearest, level 0 (golden) |
| 2 | raw merged colour, filtered |
| 3 | vertex light only |
| 4 | merged (nearest, level 0) × light, no fog (golden lighting) |
| 5 | fog factor |
| 6 | merged layer count per cell (0 black … 5 white) |

## 5. Streaming (`TerrainLandblockLayer`)

`TerrainLandblockLayer` is the game's terrain layer. It implements game-core's `ILandblockLayer` (docs/game-core.md §4) and is registered first by `Main`, in place of the placeholder:

```csharp
// Main._Ready, before the render warm-up: the atlas decodes on a worker while the main thread warms up.
var terrain = options.PlaceholderTerrain ? null : TerrainLandblockLayer.Start(_services, this, normalMode);
...
_world.RegisterLayer(terrain ?? (ILandblockLayer)new TerrainLayer(_services.Dats, _services.Ground));
```

| Member | Thread | What |
|---|---|---|
| `Start(services, owner, normalMode)` | main | Compiles the shader (`TerrainRenderer.WarmUpShader`), starts the atlas decode on the worker pool, and queues the renderer's construction as one `Urgent` main-thread item. The layer frees its renderer when `owner` leaves the tree. |
| `Ready` | | Completes once the renderer exists; faults if the atlas cannot be built. |
| `PrepareAsync(coord, token)` | worker | `TerrainBlockBuilder.Prepare` (≈ 0.1 ms warm) and the landblock's `TerrainSampler`. A prepare that finishes before the renderer exists awaits `Ready` (start-up only), so no attach ever runs without it. |
| `Attach(coord, prepared, root)` | main | Registers the sampler with `GameServices.Ground`, then adds the landblock's mesh. One step (≈ 0.05 ms), so it returns `null`. |
| `Detach(coord)` | main | Frees the mesh at once (`ArrayMesh.Dispose`, render-contract.md §5) and unregisters the sampler. |

- **Start-up without a long frame.**
  1. In `Main._Ready`, before the first frame: `TerrainRenderer.WarmUpShader` creates a material with the terrain shader, which starts Godot's compile (4–13 ms measured, with Godot's shader cache warm or cleared). The render warm-up's 26 variants (180–235 ms) run next anyway.
  2. On a worker, overlapping that warm-up: the atlas decodes and its images are created (85–96 ms).
  3. In the first frames, one urgent main-thread item: both texture arrays upload (10.7–10.8 ms) and the material and a warm-up mesh are created (1.6 ms). This is the largest single main-thread item of a whole run (13–15 ms; core's own items are ≤ 6.5 ms). It happens once, before any terrain is visible.
  4. The renderer is ready 0.30–0.35 s after `Start`; the 289-landblock start view is idle 0.25 s after the first frame.

  Without step 1, the renderer's own material creation took 97 ms in that item on the first run after a shader change (a 117 ms item), and about 10 ms otherwise. A much slower machine could still wait for the compile there, if the warm-up finishes first.
- **Failure is loud.** If the atlas cannot be built (for example mixed layer sizes, §2), the layer logs the exception once (`[terrain] the terrain atlas could not be built; no terrain will be drawn`), and every landblock's prepare rethrows it, so the streamer logs a failure per landblock. There is no silent fallback to the placeholder.
- **Ground heights.** Each attach registers the landblock's exact retail ground sampler (`TerrainSampler`: `CLandCell::find_terrain_poly` + `Plane::set_height`) with `GroundHeights`, and each detach unregisters it, as the placeholder did. Walk mode, the fly-path altitude and anything else that asks the ground therefore follow the drawn terrain. `TerrainGroundMatchesRenderMeshTests` pins that the sampler and `TerrainRenderMesh` describe the same 128 triangles: same heights within 0.1 mm (worst 0.036 mm, float resolution) at the grid vertices (those on the east and north edges belong to the neighbour) and at 39,000 random points on 13 real landblocks, and the same triangle numbering away from the diagonals. (The shader draws the surface 0.01 m lower, retail's `zFightTerrainAdjust`.)
- **Normals.** *(Wave 3, lead decision.)* Seamless normals (`TerrainNormalMode.SeamlessAcrossBlocks`, `TerrainRenderer.DefaultNormalMode`) are the default; `--retail-normals` selects retail's per-block normals (`CLandBlockStruct::calc_lighting`, §8).
- **The placeholder** (`World/TerrainLayer.cs`, vertex colours) is only used with `--placeholder-terrain`, as a comparison for streaming measurements.
- **Floating origin.** Meshes are landblock-local under the streamer's landblock root, so a rebase needs nothing.
- **Perf overlay.** The terrain arrays are not in `RenderResources`' texture cache, so the overlay's "textures" line excludes them; its "video" figure includes them (42.7 MB).

| Argument (Main) | Effect |
|---|---|
| `--placeholder-terrain` | The vertex-coloured placeholder layer instead of the TexMerge terrain |
| `--retail-normals` | Retail's per-block normals, with the landblock-edge light step (§8); seamless normals are the default (`--seamless-normals` is still accepted) |

## 6. Golden test

`pwsh -File tools\terrain-golden.ps1`. It wraps `tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300` with `res://scenes/TerrainPreview.tscn -- --golden=…`.

**Method.**

1. Each landblock is rendered top-down, with an orthographic camera (192 m) into an off-screen 1000×1000 `SubViewport`. It has no MSAA, no TAA, no debanding, and the Linear tonemapper.
2. It is rendered in two views:
   - **raw** (debug view 1): nearest samplers, level 0, no light, no fog;
   - **lit** (debug view 4): the same × the vertex light.
3. Each pixel centre maps to an exact cell UV.
4. The reference for each pixel:
   - **raw**: `TerrainCompositor.ComposeFloat`, the shader's float arithmetic, and `TerrainCompositor.Compose`, retail's truncating integer merge;
   - **lit**: the same × `TerrainCompositor.InterpolateVertexLight` of `TerrainVertexLighting.Light`, i.e. retail `calc_lighting` bytes, barycentric over the retail split.
5. **Excluded pixels** (21% of the 1,000,000 here):
   - pixels within 1.5 px of a cell edge, where the flat per-cell data changes;
   - pixels whose sampling position is within 0.03 texel of a texel boundary of any array, where nearest sampling is ambiguous.
6. **Tolerance** (docs/terrain-shader.md §8): |GPU − ComposeFloat| ≤ 2 and |GPU − Compose| ≤ 6, per channel, on every compared pixel.
7. **Outputs:** `terrain-golden-<block>-{raw,lit}-{gpu,cpu,diff}.png` (diff ×32; excluded pixels dark blue) and `terrain-golden-report.txt`, in the repository's `docs/screenshots/m1/` (`-OutDir` to change it). The exit code is 0 for pass and 2 for fail.
   - `--golden` without a folder, or with a relative one, now resolves from the repository root. It used to resolve from Godot's working directory and land in `src/Lifestone.Game/docs/`.
   - `-OutDir` may contain spaces. `tools\godot.ps1` splits game arguments on whitespace, so the script percent-encodes the folder and `TerrainPreviewOptions` decodes it.
8. **Compile errors fail too.** A shader that does not compile renders nothing, and the test then fails with differences of up to 240 levels (seen once during development).
9. **Lighting comes from the global uniforms**, as in the game (`TerrainRenderer.ApplySky(sky, fog: false)` → `RenderGlobals`). The golden run keeps the CPU atlas (`keepAtlasPixels`) for its reference.

**Results** (2026-10-10, Godot 4.7.2, Vulkan, RTX 5090). 2,378,970 channel values compared per image. After the switch to global uniforms (wave 2b) every figure below is unchanged, to the last digit, including with an `-OutDir` containing a space:

| Landblock | Content | raw: max / mean vs ComposeFloat | raw: max vs Compose | lit: max / mean vs ComposeFloat | lit: max vs Compose |
|---|---|---|---|---|---|
| 0xA9B4 Holtburg (42.5N 33.7E) | grass, rock, 33 road cells, 2-overlay cells, two road layers | 0.61 / 0.070 | 2.00 | 0.61 / 0.240 | 2.29 |
| 0x7E62 desert SE of Yaraq (23.2S 0.8W) | sand-yellow, rock, grass, roads | 0.64 / 0.009 | 2.00 | 0.64 / 0.181 | 2.18 |
| 0x7AC2 snowy mountains (53.7N 4.0W, 224–390 m) | snow, ice, sedimentary and semi-barren rock | 0.64 / 0.029 | 1.00 | 0.64 / 0.243 | 1.44 |
| 0x1FB1 (40.1N 76.8W) | a three-overlay cell, all-road cells, water, moss | 0.61 / 0.045 | 3.00 | 0.64 / 0.186 | 2.77 |
| 0x1EB1 (40.1N 77.6W) | 11 all-road cells, two road layers | 0.60 / 0.055 | 2.00 | 1.09 / 0.232 | 2.16 |

- **Result: PASS.** No channel differs from the float reference by more than 1.09 levels. The raw maximum of 0.64 is the reference's own rounding.
- **Lit differences.** In the lit view about 4% of channels round to a one-level difference. They sit on the light's `floor(·255)` truncation boundaries, computed in float on the GPU and in double on the CPU.
- **Other checks.** The shader's decode-and-merge logic is also modelled line by line in `TerrainShaderModelTests` against `ComposeFloat` (synthetic and real cells, 1e-5). The integer and float references agree, and `ComposeRetailTexture` agrees with retail's pointer walk (wave-1 `TerrainCompositorTests`).

## 7. Performance

The first table was taken with `--perf` in `TerrainPreview` (Debug build, RTX 5090, 32 threads, 1600×900). §7.1 measures the terrain in the game.

| What | Measured |
|---|---|
| Atlas decode + mips (worker, parallel) | 79–93 ms (Godot `Image` creation adds ~10 ms) |
| Atlas upload, 2 `Texture2DArray`s (main) | 9–14 ms, once |
| VRAM: colour array | 40.0 MiB (30 × 512² RGBA8 + mips) |
| VRAM: alpha array | 2.67 MiB (8 × 512² R8 + mips) |
| `PrepareBlock` per landblock, warm, single thread | median 0.071 ms, max 0.11 ms (dat read 0.004 ms) |
| First landblocks (cold: JIT, first recipes) | ~10 ms each, 25 in parallel in 13–15 ms wall |
| `ArrayMesh` build + upload per landblock (main) | median 0.025–0.030 ms, max 0.12–0.57 ms |
| Mesh memory | 256 × 36 B vertex streams + 384 × 2 B indices ≈ 10 KiB per landblock |
| Draw calls | 1 per visible landblock. 5×5: 25 attached, 20 drawn from the default camera. 11×11: 121 attached, 93 drawn |
| GPU time, 5×5 / 11×11 | 0.083 ms / 0.089 ms per frame (viewport-measured) |

- **Streaming budget.** The terrain is far below a per-frame main-thread budget: ~0.03 ms per landblock.
- **The real costs** are the one-off atlas (100 ms on a worker, 10 ms main) and the cold first `PrepareBlock` calls.

### 7.1 In the game: Holtburg → Arwic at 150 m/s

*These are terrain-only numbers from before integration; the wave-3 performance pass measured the integrated game (all layers) in game-core.md §7.3.*

`tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--fly-path=holtburg:arwic:150 [--vsync=off --max-fps=0]'` (game-core.md §7): 5,921 m in 39.5 s, 60 m above the exact ground. RTX 5090, Vulkan Forward+, 1280×720, 30 workers, load radius 8, 4 ms budget, Debug build, 2026-10-10. Real TexMerge terrain unless stated; no statics. The machine was shared with other agents' builds and runs.

| Run | Frames | Avg | p50 | p99 | Max | > 33 ms | > 50 ms | Attached | Throughput | Max attach step | Max drain | Overruns | GCs 0/1/2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| vsync on (default) | 9,471 | 4.17 ms | 4.17 ms | 4.54 ms | 14.8 ms | 0 | **0** | 680 | 17.2 /s | 0.82 ms | 1.9 ms | 0 | 1/1/0 |
| vsync on, again | 9,471 | 4.17 ms | 4.17 ms | 4.61 ms | 14.7 ms | 0 | **0** | 680 | 17.2 /s | 0.82 ms | 2.0 ms | 0 | 1/1/0 |
| vsync off | 100,421 | 0.39 ms | 0.36 ms | 0.77 ms | 13.6 ms | 0 | **0** | 680 | 17.2 /s | 0.85 ms | 2.0 ms | 0 | 1/1/0 |
| vsync off, run 2 | 100,331 | 0.39 ms | 0.36 ms | 0.73 ms | **80.0 ms** | 1 | **1** | 680 | 17.2 /s | 0.79 ms | 1.8 ms | 0 | 1/1/0 |
| vsync off, run 3 | 104,304 | 0.38 ms | 0.35 ms | 0.83 ms | 12.2 ms | 0 | **0** | 680 | 17.2 /s | 0.85 ms | 2.4 ms | 0 | 1/1/0 |
| vsync off, run 4 | 87,901 | 0.45 ms | 0.39 ms | 1.00 ms | 24.0 ms | 0 | **0** | 680 | 17.2 /s | 0.79 ms | 2.3 ms | 0 | 1/1/0 |
| vsync off, run 5 | 97,232 | 0.41 ms | 0.37 ms | 0.79 ms | 25.5 ms | 0 | **0** | 680 | 17.2 /s | 0.83 ms | 2.1 ms | 0 | 1/1/0 |
| `--vsync=mailbox` | 18,226 | 2.17 ms | 3.37 ms | 4.25 ms | 27.9 ms | 0 | **0** | 680 | 17.2 /s | 0.89 ms | 2.0 ms | 0 | 1/1/0 |
| vsync off, `--placeholder-terrain` | 103,575 | 0.38 ms | 0.35 ms | 0.69 ms | 14.6 ms | 0 | **0** | 680 | 17.2 /s | 0.18 ms | 1.7 ms | 0 | 2/2/2 |
| vsync on, final build, contended | 9,323 | 4.23 ms | 4.17 ms | 5.31 ms | **76.4 ms** | 3 | **2** | 680 | 17.2 /s | 0.74 ms | 1.7 ms | 0 | 1/1/0 |
| vsync on, final build, another Godot started mid-flight | 9,214 | 4.28 ms | 4.17 ms | 8.08 ms | **69.2 ms** | 2 | **2** | 680 | 17.2 /s | 2.61 ms | 4.0 ms | 1 | 1/1/0 |

- **Work stays small.** The terrain's attach is one step of at most 0.9 ms for a landblock (average 0.05 ms), and no drain exceeded 2.4 ms. No budget overruns. The real terrain costs about the same per frame as the placeholder (p99 0.73–1.0 ms against 0.69 ms with vsync off).
- **Throughput.** 17.2 landblocks/s, exactly the demand: crossing a landblock every 1.28 s at 150 m/s brings in a new 17-landblock row or column. At start-up the 289-landblock view is attached 0.25 s after the first frame.
- **Vsync on ran at 240 Hz** in this session (4.17 ms frames). Core's measurements ran at 60 Hz (game-core.md §7); the presentation mode depends on the desktop.
- **Every frame over 10 ms was checked** against the per-frame CSV (drain time, attaches, GC counts, rebases):
  - **Ours: one ephemeral GC per flight.** In every terrain run exactly one gen-0/gen-1 collection falls in the flight, in a frame that also attaches a 17-landblock row. Those frames take 11.2–20.2 ms. Instrumented once (`GC.GetGCMemoryInfo`): a blocking gen-1 collection, 10.4 ms pause, 6.1 MB promoted, gen 0 at 47.5 MB before it. The allocations are the per-landblock prepare (render mesh, packing, Godot arrays, ground sampler: tens of KB per landblock). The placeholder allocates less and only saw cheap background gen-2 collections. This is for the wave-3 performance pass: fewer intermediate arrays in `TerrainRenderMesh`/`TerrainBlockMesh.Prepare`, pooling, or a GC latency mode during flight (core).
  - **Not ours: everything else.** All other frames over 10 ms ran no drain, no attach, no GC and no rebase. The placeholder flight has one too (14.6 ms).
  - **The machine was shared with other agents.** Another agent's Godot instance was seen running during three flights: an experiment not in the table (two 56–57 ms frames, 22 frames over 10 ms, attach steps three times slower), and the two "final build" rows. In the last row the other Godot process started 20 s into the flight; that flight has 73 frames over 10 ms, and its 69 ms and 56 ms frames ran no Lifestone work. In the row before, every frame over 10 ms apart from the GC frame (13.9 ms) falls in one 2.4 s burst (frames 7,248–7,814: 37 frames of 10–76 ms) in which no Lifestone work ran.
  - **The 80 ms frame** (vsync off, run 2, frame 54,194) ran no Lifestone work: no drain, no attach, no GC, no rebase (the next row attached two frames later in 1.0 ms). The mean frame time over 3,000 frames before and after is unchanged (0.384 ms against 0.389 ms), so it is not a presentation-mode switch either. It did not reproduce in four more vsync-off runs. It matches the unreproduced 83.7 ms frame that core reported (game-core.md §7).
  - **Verdict.** The DoD's 50 ms limit holds in 8 of 11 flights. In the other 3, every frame over 50 ms ran no Lifestone work, and 2 of them overlapped another Godot instance. Every frame in which Lifestone worked stayed at or under 20.2 ms (the GC frame). The wave-3 performance pass should re-measure on an otherwise idle machine.
- **Start-up** (§5): one 13–14 ms main-thread item (texture-array upload 10.7 ms, material and warm-up 1.6 ms), the largest of the run, before any terrain is visible.
- **Memory.** Godot texture memory is 108.7 MB at Arwic with the terrain (65.8 MB with the placeholder): the 42.7 MB of the two arrays. The decoded CPU copies are freed after the upload (§2).

## 8. Known deviations and open points

- **Filtering order** (docs/terrain-shader.md §7):
  - Retail filters and mip-maps the *merged* texture. Lifestone filters each layer and then merges.
  - They are identical with nearest sampling (the golden views) and at magnification. At distance the average differs slightly.
  - Alpha maps are bilinear at 512² instead of retail's 2× pixel replication followed by bilinear at 1024², so edges are smoother by under one alpha texel (4.7 cm).
- **Edge addressing.** Colour layers REPEAT and alpha maps CLAMP. Retail CLAMPs multi-texture cells' merged texture and WRAPs single-texture cells. Only the half-texel border of a cell differs. The single-texture flag is packed but unused.
- **Landblock-edge lighting seam (retail).**
  - `calc_lighting` sums only the block's own triangles, so the vertex light steps at landblock edges.
  - It is clearly visible in the light-only view (`terrain-seam-light-retail.png`) and barely visible in the textured view (`terrain-seam-lit-retail.png`).
  - `TerrainNormalMode.SeamlessAcrossBlocks` removes it (`terrain-seam-light-seamless.png`).
  - ~~Owner decision (wave 2b): retail fidelity.~~ **Lead decision (wave 3): seamless normals are the default** in the game, the preview and the spell viewer (`TerrainRenderer.DefaultNormalMode`). The step is a rendering artifact retail players would call a glitch, and DoD 3 asks for no visible seams. This is a documented glitch-fix deviation; `--retail-normals` (game, spell viewer and preview) restores retail's per-block normals.
  - **Measured** on a top-down view of the A8B4/A9B4 edge (`--interior-eye=A9B40001@2,2,190 --interior-look=0,-89`, mean luminance of rows 340–520):
    - retail normals: steps from 70–71 to 82–83 within 1–2 px at x = 626 (largest neighbouring-column step 6.75);
    - seamless normals: stays at 75–79 (largest step 2.3, which is texture).
    - Screenshots: `docs/screenshots/m1/fix-render/{before,after}/seam-topdown.png`.
  - **Not affected.** The golden test compares against whatever normals the block was built with (`block.Source.GridNormals`), so it holds in either mode.
  - **Prepare cost (worker side only).** The edge vertices read up to eight neighbouring landblocks' heights. `TerrainPreview --center=A9B4 --size=9 --perf`, two runs per mode, Debug build, RTX 5090, 32 threads (2026-10-10):

    | | Seamless (default) | `--retail-normals` |
    |---|---:|---:|
    | `PrepareBlock` warm, single thread, median | 0.119–0.123 ms | 0.068–0.069 ms |
    | Per-block CPU, cold (first reads, JIT), median | 1.47–1.49 ms | 0.34–0.36 ms |
    | 81 landblocks, prepare wall on the worker pool | 16.6–16.7 ms | 12.2–12.5 ms |

    The main-thread attach is the same mesh in both modes (median 0.024–0.033 ms), so the 4 ms budget is unaffected; the extra ~0.05 ms per landblock (warm) is worker time.
- **Fog when the sky disables it.** When `SkyFog.Enabled` is false, `RenderGlobals.Apply` turns fog off. Retail instead leaves the previous fog state (`SkyDesc::GetWorldFog` returns 0). Whoever drives the sky owns that state.
- **One gen-1 GC per Holtburg → Arwic flight** (10.4 ms pause, §7.1), from the per-landblock prepare allocations. Within the hitch limit; for the wave-3 performance pass.
- **No anisotropic filtering**, as retail. Grazing views are blurry, as they were in retail. Anisotropy would be a deliberate, non-retail option and needs `textureGrad` with clamped gradients.
- **Not implemented, because Dereth does not use them:**
  - reduced-detail blocks (`TransAdjust`);
  - PalShift regions;
  - landscape texture detail below 1024;
  - landscape detail textures (retail never draws them; docs/terrain-shader.md §7).
- **The sky is not drawn here.** The preview clears to the fog colour, as the core contract's `Main` does. The sky track owns the dome.
- **TODO(terrain): `ImgTex::CreateD3DTexture`'s 4-level cap** is inferred from a garbled `CreateTexture` call. If it turns out wrong, raise `ac_max_mip_level`.

## 9. Preview scene and screenshots

Run it with `tools\godot.ps1`:

```
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 120 -Resolution 1600x900 `
  -GodotArgs 'res://scenes/TerrainPreview.tscn' -Screenshot docs\screenshots\m1\terrain-holtburg.png -Frames 30 `
  -GameArgs:'--center=A9B4 --size=5 --time=0.5'
```

**Options** (`TerrainPreviewOptions`):

| Option | Meaning |
|---|---|
| `--center=hex` | centre landblock |
| `--size=N` | N×N landblocks (default 5) |
| `--time=F` | day fraction |
| `--day-group=N` | day group |
| `--look-at=x,y` | camera target, AC metres from the centre block's SW corner |
| `--distance` | camera distance |
| `--pitch` | camera pitch |
| `--yaw` | compass heading the camera looks towards |
| `--fov` | field of view |
| `--debug-view=N` | debug view (§4) |
| `--max-mip=F` | highest mip level |
| `--no-fog` | turn fog off |
| `--retail-normals` | retail's per-block normals with the edge light step (§8; seamless is the default) |
| `--perf` | log build timings and render statistics |
| `--golden[=dir]` | run the golden test (§6) |
| `--golden-blocks=XXYY,...`, `--golden-size=N` | the golden test's landblocks (default A9B4,7E62,7AC2,1FB1,1EB1) and its render size per landblock edge (default 1000) |
| `--seamless-normals` | still accepted (seamless is the default) |
| `--screenshot=path`, `--frames=N`, `--dat-dir=DIR` | the godot.ps1 convention; the dat folder |

Interactive keys:
- arrows orbit;
- PageUp and PageDown zoom (or use the wheel);
- 0-6 select the debug view;
- `M` toggles the mip cap;
- `F` toggles fog.

Screenshots in `docs/screenshots/m1/` (gitignored), at noon unless stated:

| File | Where |
|---|---|
| `terrain-holtburg.png`, `terrain-holtburg-11x11.png` | 5×5 and 11×11 around Holtburg 0xA9B4 |
| `terrain-holtburg-close.png` | Holtburg town |
| `terrain-crossroads.png` | road junction close-up, 0xA9B4 local (26, 118). It is a three-way junction: a search for straight four-way crossings within 6 landblocks of Holtburg found none, and Glenden Wood's junctions are three-way too |
| `terrain-desert.png` | 0x7E63, south-east of Yaraq (22.4S 0.8W) |
| `terrain-snow.png` | snowy mountains 0x7AC2 (53.7N 4.0W) |
| `terrain-swamp.png` | Holtburg marsh 0xA8B5 (43.3N 32.9E) |
| `terrain-forest.png` | Glenden Wood 0xA1A4 (29.7N 27.3E) |
| `terrain-coast.png` | Hebian-To coast 0xE74F (38.4S 83.3E) |
| `terrain-holtburg-t0.3.png`, `terrain-holtburg-t0.9.png` | morning and night lighting and fog |
| `terrain-mip-{0,3,16}.png` | mip policy comparison (§2) |
| `terrain-seam-*.png` | landblock-edge lighting, retail versus seamless normals (§8) |

**In the game** (`scenes/Main.tscn`, 1280×720, noon), taken with `tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 180 -Screenshot docs\screenshots\m1\<file> -GameArgs:'--start=…'`. The camera stands 90 m south of the spot, 45 m up, looking north at it (game-core.md §6):

| File | Where | What it shows |
|---|---|---|
| `terrain-integration-holtburg.png` | Holtburg (default start) | The cobbled roads into town, the river and the marsh with its brown banks, grass hills fading into the fog |
| `terrain-integration-holtburg-statics.png` | Holtburg with the statics smoke layer (retired in M1 wave 3; the objects layer draws them now) | Holtburg's buildings standing on the TexMerge ground |
| `terrain-integration-holtburg-walk.png` | Holtburg, `--walk` | Walk mode: the eye 1.7 m above the drawn grass at the drop point (z 95.7 = ground 94.0 + 1.7) |
| `terrain-integration-desert.png` | `--start=7E63`, south-east of Yaraq (22.7S 0.7W) | Orange sand, grass patches along a cobbled road, rock mesas |
| `terrain-integration-snow.png` | `--start=7AC2` (53.3N 3.9W) | Snow fields with brown rock outcrops, cracked earth in the foreground |
| `terrain-integration-coast.png` | `--start=E74F`, Hebian-To coast (38.7S 83.3E) | Grass, a sand beach, teal shallows and deep water |
| `terrain-integration-holtburg-placeholder.png` | Holtburg, `--placeholder-terrain` | The old vertex-coloured placeholder, for comparison |
| `terrain-integration-flypath-arwic.png` | arrival of `--fly-path=holtburg:arwic:150` | Arwic's grassland 60 m up after the flight (240 fps, 321 landblocks loaded) |
| `terrain-integration-preview-holtburg.png` | `TerrainPreview`, 5×5 around Holtburg | The preview lit by the global uniforms, as before |
