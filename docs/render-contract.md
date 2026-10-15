# Render contract (M1 wave 2)

**Owner:** game core (`src/Lifestone.Game/scripts/Render/`, `src/Lifestone.Client/Rendering/`).
**Audience:** the terrain, objects, interiors, sky and spell-viewer tracks. Every Lifestone 3D shader follows this contract so that one sky/time-of-day driver lights and fogs the whole world the way retail's fixed-function pipeline did.

Retail behaviour is cited by function name from the Mag-nus/acclient-AI-RE decompile (11.4186 with PDB names, line numbers from `acclient.c`; the render core is unchanged in the user's 11.6096). Nothing from it is copied.

## 0. Rules in one screen

- **No Godot PBR.** Every surface shader is `unshaded` and computes retail lighting and fog itself from the global uniforms below. Do not add Godot lights or `WorldEnvironment` ambient, fog, glow or adjustments; `Main` sets a `Linear` tonemapper at exposure 1 and clears to the fog colour.
- **Raw colour arithmetic.** Sample textures *without* `source_color`, do every multiply/mix on raw values (bytes / 255), and hand the result over with `ALBEDO = ac_output(c)` (`RenderGlobals.OutputFunctionCode`; the terrain shader has the same switch). *(Wave 3, render-fidelity fix.)* By default the main view blends in retail's byte space: `ac_output` passes the raw value through and the tonemap pass decodes the finished frame once (§2.3). With `--blend-space=linear` it applies the exact `ac_srgb_to_linear` decode per draw (docs/terrain-shader.md §6). Either way the linear tonemapper re-encodes, so an opaque surface shows retail's bytes.
- **Built-in Godot 3D materials** (`Label3D`, `Sprite3D`, `StandardMaterial3D`, ...) in the main view take their colours through `ByteSpaceBlending.BuiltinColor`, or are not used (§2.3). *(Wave 3, render-fidelity review fix.)*
- **Axes and winding** change only in `scripts/Core/AcToGodot.cs` (positions, directions, rotations, per-axis scales and whole transforms). `MeshData` builders emit explicit back faces, so meshes always use `cull_back`.
- **Main thread** creates every RenderingServer resource (textures, materials, meshes) and every node, through `GameServices.MainThread` (budgeted) or a layer's `Attach` steps. Workers do the CPU work.
  - **One exception, on purpose:** workers create Godot `Image`s (`Image.CreateFromData`, `LoadJpgFromBuffer`, `GenerateMipmaps` in `TextureCache.Prepare`). An `Image` is a CPU-side resource that touches no server, and Godot allows it off the main thread. Whoever owns a `PreparedTexture` disposes it: the upload does it at once, and the streamer does it for prepared objects it drops.
- **Zero normals need the flag.** Godot cannot store a zero vertex normal, so surface shaders read a per-vertex flag in `CUSTOM0.x` (§2.1). Build surface-shader meshes with `MeshFactory`, or emit the same attribute.
- **Key materials by (Surface id, addressing)** — `SubMesh.Addressing` — never by Surface id alone.

## 1. Global shader uniforms

Declared in `project.godot` (`[shader_globals]`) so shaders compile against them; set at run time through `RenderGlobals` (`scripts/Render/RenderGlobals.cs`). Paste `RenderGlobals.ShaderDeclarations` into a shader to declare them.

| Uniform | Type | Meaning | Source (`RenderGlobals.ApplyDefaults` at start-up, then the sky's `SkyController` on retail's cadence) |
|---|---|---|---|
| `ac_sun_dir` | vec3 | Unit vector **towards** the sun, Godot axes | `SkyLighting.SunDirection` (`LScape::sunlight` normalised) |
| `ac_sun_color` | vec3 | Sun light diffuse = `sunlight_color × |sunlight|` | `SkyLighting.SunDiffuse` (`PrimD3DRender::UpdateLightsInternal`) |
| `ac_ambient` | vec3 | World ambient for **objects** = `ambient_color × (ambient_level + 0.2 |sunlight|)` | `SkyLighting.ObjectAmbient` (`LScape::calc_object_light`, `SmartBox::SetWorldAmbientLight`) |
| `ac_land_ambient` | vec3 | **Terrain** ambient = `ambient_color × ambient_level` (level floored at 0.2) | `SkyLighting.LandscapeAmbient` (`CLandBlockStruct::calc_lighting`) |
| `ac_fog_color` | vec3 | D3DRS_FOGCOLOR | `SkyFog.Color` |
| `ac_fog_start` | float | D3DRS_FOGSTART, metres | `SkyFog.Start` |
| `ac_fog_end` | float | D3DRS_FOGEND, metres | `SkyFog.End` |
| `ac_fog_enabled` | bool | World fog on | `SkyFog.Enabled` |
| `ac_max_mip_level` | float | Highest mip level sampled (3 = retail's ≤4 levels) | `--max-mip`, default 3 |
| `ac_byte_space` | bool | Shaders output raw byte-space values; the tonemap pass decodes the frame once (§2.3). Project default false (linear) for scenes that never select a space | `ByteSpaceBlending.Select`, from `--blend-space` (default byte) |
| `ac_cm_ambient` | vec3 | *(M2.)* Creature mode's ambient (retail 0.3 white). Read only by `CreatureMode` variants | `CreatureModeLighting.Apply` (`CreatureModeView`, wave 2) |
| `ac_cm_light_dir` | vec3 | *(M2.)* Unit vector **towards** creature mode's distant light, Godot axes (the sense of `ac_sun_dir`) | `CreatureModeLighting.Apply`; the portal tunnel's is the AC vector (0.3, −1.9, 0.65) normalised |
| `ac_cm_light_color` | vec3 | *(M2.)* The distant light's diffuse colour × its intensity (the tunnel's: white × 2.0) | `CreatureModeLighting.Apply` |

- **Colours are raw retail values** (byte / 255 with retail's float 0.0039215689), not sRGB-decoded. Directions are already in Godot axes.
- **Terrain uses the same sun uniforms.** Retail terrain lighting is `sunColor/255 × max(0, n·sunlight)` with the unnormalised `sunlight`; that equals `ac_sun_color × max(0, n·ac_sun_dir)`. So docs/terrain-shader.md §3.3's `sun_vector`/`sun_color`/`ambient_color × ambient_level` map to `ac_sun_dir`/`ac_sun_color`/`ac_land_ambient`.
- **Defaults now:** `RenderGlobals.ApplyDefaults` evaluates `DerethSky.EvaluateAtTimeOfDay(dayGroup 0, 0.5)` (Midsong, retail's midday time of day) at start-up (`--time-of-day=F` overrides the fraction). In the user's dats that is the sun at 67° elevation in the east, sun `(0.753, 0.647, 0.455)`, object ambient `(0.454, 0.454, 0.504)`, fog 150–2,400 m colour `#C3C8DC`.
- **The sky track** calls `RenderGlobals.Apply(SkyState)` on its cadence (`SkyUpdateCadence`) and sets the `WorldEnvironment` background as it sees fit. Retail relights only every `LightTickSize` (15 s) and re-reads fog every `TickSize`; uniforms make the relight free.
  - *(Sky track, wave 2.)* It now does so through `RenderGlobals.ApplyLighting` and `ApplyFog` (light every 15 s, fog every 0.8 s, both at once after a time jump). It sets the background to its sky view. See [sky.md](sky.md) §3-4.

## 2. Surface shaders and variants

`SurfaceShaderVariant.For(SurfaceMaterial, passingTexelsBlend, addressing, cull)` (`src/Lifestone.Client/Rendering/`, engine-agnostic) maps retail's fixed-function state (`SurfaceMaterial.RenderState`, docs/textures.md §5) to a variant; `SurfaceShaders` (`scripts/Render/SurfaceShaders.cs`) generates and caches one Godot `Shader` per variant. Everything Godot fixes at compile time is in the key:

| Field | Values | Godot |
|---|---|---|
| Blend | Opaque (ONE/ZERO) | opaque pass, `depth_draw_opaque`, ALPHA not written |
| | Mix (SRCALPHA/INVSRCALPHA) | `blend_mix`, ALPHA = a |
| | Premultiplied (ONE/INVSRCALPHA) | `blend_premul_alpha` (ONE/ONE_MINUS_SRC_ALPHA), ALPHA = a, colour not premultiplied (retail's "ONE") |
| | AddOne (ONE/ONE) | `blend_add` (Godot SRC_ALPHA/ONE) with ALPHA = 1 |
| | AddSourceAlpha (SRCALPHA/ONE) | `blend_add`, ALPHA = a |
| | AddInverseSourceAlpha (INVSRCALPHA/ONE) | `blend_add`, ALPHA = 1 − a (unused by the data) |
| AlphaTest | on/off | `discard` when `round(a × 255) < 200` (D3D GREATEREQUAL 200 on the 8-bit alpha) |
| DepthWrite | on/off | blended variants: `depth_draw_always` / `depth_draw_never` |
| Cull | Back / None | `cull_back` / `cull_disabled` |
| Addressing | Wrap / Clamp | sampler hint `repeat_enable` / `repeat_disable` |
| Textured | yes/no | `surface_texture` sampler / `solid_color` uniform (retail's 1×1 solid texture) |
| Fog | on/off | off for Additive surfaces (`SetFFFogAlphaDisabled`) |
| Lighting | Object / EnvCell / CellObject / CreatureMode / DynamicObject / DynamicCellObject | the vertex lighting rule; `For` gives Object, the interiors set the next two (docs/interiors.md §4.2). **DynamicObject / DynamicCellObject** *(M2 W2-pre(c), moving objects: the player, later the server's)*: the Object and CellObject rules for MultiMesh instances whose per-instance values arrive in `INSTANCE_CUSTOM` (x the cell slot in `ac_cell_states`, y one plus the row of the object's light set in `ac_object_lights`, z a luminosity override, w the translucency; all 0 = a plain instance). Outdoors: sun and ambient as Object. In a cell: no sun, `clamp(luminosity + world ambient + lights)`, the lights (at most 8, `Render::minimize_object_lighting`, D46) summed per vertex from the set with `CellObjectLighting.Light`'s formula, because the object moves; the doorway clipping and front depth of cells through the slot. Translucency scales the alpha of blended variants only; a part that fades (`0 < w < 1`) is drawn with the blended twin of its mesh (`RenderResources.FadeTwin`, the materialise fade, D45 and D63; docs/avatars.md section 17), whose surfaces are blended without a depth write. `--interior-engine-checks` pins all four values against the CPU formula. **CreatureMode** *(M2, portal view)*: `clamp(luminosity + ac_cm_ambient + ac_cm_light_color × max(0, N·ac_cm_light_dir))`, with no sun, no world ambient and no fog: `CreatureMode::Render` switches the sun and the other lights off. Made with `SurfaceShaderVariant.WithLighting(CreatureMode)`, which clears fog and the detail pass; part of the warm-up. The world's sun, ambient, indoor ambient and fog cannot reach it (`--portal-space-checks` pins this under hostile world globals). The sign of the tunnel light's vector is read as "towards the light", unverified against retail (`TODO(portal)`, `CreatureModeLighting`) |
| Detail | None / Building / Environment | retail's detail-texture pass folded into the opaque draw (§2.4); set only through `SurfaceShaderVariant.WithDetail` / `RenderResources.WithDetail` |

- **INVSRCALPHA/SRCALPHA** has no Godot blend mode; `For` throws a `TODO(render)` `NotSupportedException` (no end-of-retail surface uses InvAlpha).
- **Cut-out shortcut (deliberate deviation, `--cutouts=blend` turns it off).** By default an alpha-tested surface gets the Opaque variant with the alpha test (depth prepass, no sorting) when all of these hold:
  - its blend is Mix or Premultiplied (retail ONE/INVSRCALPHA or SRCALPHA/INVSRCALPHA after the test);
  - its material alpha is 255;
  - its level-0 texture has no texel with alpha in [200, 254] (`TextureInfo.PassingTexelsBlend`, from the decode).

  Additive alpha-tested surfaces always blend.
  - **This is exact per texel only.** Bilinear filtering and the box-filtered mips produce *sampled* alphas in [200, 254] at cut-out edges, for example a 2×2 average of three opaque texels and one transparent one, averaged again at the next level. Retail passes those samples and blends them, so up to (255 − 200) / 255 = 21.6% of the background shows through at edge pixels. Lifestone writes them fully opaque.
  - **Visible effect:** a thin band along leaf and fence edges, where the filtered alpha crosses [200, 254]. That is about a fifth of a texel wide under magnification, wider in the lower mips. Edges look slightly harder than retail's.
  - **Why it stays the default:** 662 surfaces use it, mostly foliage. Blending them would move every tree into Godot's sorted transparent pass. Revisit in the visual review with `--cutouts=blend`, which gives every such surface its blended variant (alpha test, then blend), exactly as retail draws it.
- **Coverage.** `SurfaceShaderVariantTests.Every_end_of_retail_surface_maps_to_a_variant_with_the_documented_counts` maps all 6,152 Surfaces: Opaque 4,983; cut-out 662; ClipMap+SRCALPHA/ONE 19; ClipMap+ONE/ONE 12; SRCALPHA/ONE 163; ONE/ONE 8; Mix 305 — the docs/textures.md §5 table. Fog off: 203. Depth write: 5,676.
- **Draw order.** Godot sorts its transparent pass back to front; retail drew its deferred lists unsorted after the opaque ones (`AddMeshToAlphaList`). Sorting is a deliberate improvement; it only changes overlapping translucent surfaces.

### Material parameters (per surface, set by `MaterialFactory`)

| Parameter | Value |
|---|---|
| `surface_texture` | the `TextureCache` texture (textured variants) |
| `solid_color` | `SurfaceMaterial.SolidColorTexel`: RGB of the stored colour, alpha `(1 − t) × 255` (untextured variants). Pass it as a `Vector4`: Godot linearises a `Color` given to a `vec4` parameter (pinned by `--sky-checks`, [sky.md](sky.md) §4.3) |
| `material_alpha` | `RenderState.MaterialAlpha / 255`: retail's vertex alpha `(1 − t) × 255` (`copyVert`) |
| `luminosity` | `Surface.Luminosity` when > 0, else 0 (`RenderMeshSubset` writes it into the D3D material's emissive only when positive; otherwise the part material's emissive, 0 by default, stays) |
| `diffuse` | 1.0 (see §2.1) |
| `alpha_ref` | 200 (`SurfaceMaterial.ClipMapAlphaTestReference`) |
| `detail_texture`, `detail_tiling` | detail variants only: the Region's building or environment detail texture and its tiling (§2.4, `DetailTextures`) |

### 2.1 Lighting formula (objects, buildings, cells, sky objects)

Per vertex (Gouraud), clamped to [0, 1], as D3D9 fixed function computes it for a mesh subset (`D3DPolyRender::RenderMeshSubset`, acclient.c:454633):

```
light = clamp(luminosity + ac_ambient * diffuse + ac_sun_color * diffuse * max(0, dot(N, ac_sun_dir)), 0, 1)
colour = texel.rgb * light                         // TEXOP_MODULATE
alpha  = texel.a * material_alpha                  // TEXOP_MODULATE of texture and vertex alpha
colour = mix(ac_fog_color, colour, fog)            // fog variants only
colour = detail(colour)                            // detail variants only (§2.4)
ALBEDO = ac_output(colour)                         // raw in byte space, ac_srgb_to_linear in linear space (§2.3)
```

- **Material ambient and diffuse are white.** `copyVert` (acclient.c:454109) writes vertex colour `0xFFFFFF | (1 − t)·255 << 24` and the colour sources stay `FromVertex` for ordinary meshes. `Surface.Diffuse` only reaches `Render::diffuse` in `D3DPolyRender::SetSurface`, and no D3D code reads that global (it is written at acclient.c:454458–454467 and nowhere read). So Lifestone does **not** use `Surface.Diffuse`; this corrects docs/textures.md §5's "diffuse = sunlight colour × diffuse". The `diffuse` parameter (default 1) is there for a part's `CMaterial` diffuse (`CPhysicsObj::SetDiffusion`), which only matters on the burned-in-lights path that sets the material colour source.
- **Zero normals.** 16,043 stored GfxObj normals are exactly zero. D3D gives them N·L = 0, so they get ambient + emissive only.
  - **Godot cannot store them.** It keeps vertex normals octahedral-encoded, so a zero normal decodes to the unit vector (0, 0, −1), which is AC north (+Y). Testing `NORMAL`'s length therefore never detects one, and such vertices would be lit as if they faced north.
  - **The flag.** `MeshFactory.Prepare` records, per vertex, whether the AC normal is zero (squared length ≤ 1e-12). `MeshFactory.Build` uploads the record as `CUSTOM0` in R-float format (`MeshFactory.ZeroNormalFormat`). The shader skips the sun term where `CUSTOM0.x ≥ 0.5`.
  - **Missing attribute.** A mesh without `CUSTOM0` reads 0 and is lit normally.
  - **Pinned** by `--engine-checks` (game-core.md §6):
    - a zero normal reads back with unit length, and the flags read back unchanged;
    - rendered under a sun pointing north, a flagged zero-normal quad shows ambient only, while an unflagged one shows ambient + sun.
  - **Terrain** needs no flag: `TerrainMeshBuilder` never emits a zero normal (it falls back to +Z).
- **Normals are normalised** after `MODEL_NORMAL_MATRIX`, so part scales do not change brightness.
- **Cells (interiors track, docs/interiors.md §4):** EnvCell burned-in static lights, objects' hardware static lights inside cells and `CellManager::ChangePosition`'s indoor ambient (0.2 white, no sun) are the `SurfaceShaderVariant.Lighting` rules `EnvCell` and `CellObject`.
- **Not modelled yet (owners noted):** part-level translucency/luminosity overrides (PhysicsDesc, `SetTranslucency`/`SetLuminosity`, sky objects) — objects/sky tracks, as extra `MaterialKey` fields; point lights (`calc_point_light`) — later.

### 2.2 Fog

Retail uses linear **range** fog per vertex (`RenderDeviceD3D::SetDefaultD3DStates`, acclient.c:460787–460812: FOGVERTEXMODE LINEAR, RANGEFOGENABLE): `f = clamp((end − d) / (end − start), 0, 1)` with d the eye-to-vertex distance, interpolated across the triangle. The shader computes d as `length(MODELVIEW_MATRIX × VERTEX)` in `vertex()`.

### 2.3 Colour space and blending

*(Wave 3, render-fidelity fix: `scripts/Render/ByteSpaceBlending.cs`.)* All arithmetic happens on raw values, so opaque surfaces show retail's bytes. Retail also **blended** bytes: D3D9 fixed function into an 8-bit framebuffer, no sRGB blending (`RenderDeviceD3D::SetBlendFunction`; D3DRS_SRGBWRITEENABLE is never set). Until wave 3 Lifestone converted every draw to linear and let Godot blend in its linear HDR buffer. Additive glows then lit a dark wall far more than grass or sky, so a flame bolt's glow read as a box following the wall behind it (spell-viewer.md §7), and half-transparent surfaces let more light through than retail.

**Byte space (the default, `--blend-space=byte`).** Measured on Godot 4.7.2 Forward+ (Vulkan, RTX 5090):
1. `ByteSpaceBlending.Select` sets the global `ac_byte_space`. Every Lifestone shader then outputs its raw value (`ac_output`, the terrain shader, the sky after pass and the sky background).
2. The main view's colour buffer is floating point (RGBA16F), so every blend is float arithmetic on those raw values: retail's byte arithmetic without the 8-bit rounding.
3. `ByteSpaceBlending.Attach` turns on the scene environment's adjustments with brightness, contrast and saturation at 1 (an exact identity). It also sets a 1D colour-correction LUT holding the exact sRGB decode: 4,096 float texels, texel *i* = `srgb_to_linear((i + 0.5) / 4096)`.
4. Godot's tonemap pass (Linear, exposure 1) sRGB-encodes the buffer for the window and then applies the LUT, which decodes it again. So the raw value reaches the screen, saturated at 1 as retail's framebuffer was. MSAA is resolved before the tonemap pass.
   - **No pass of its own and no per-frame code.** It costs one texture fetch per channel in a pass Godot runs anyway.
   - **Interpolation error** is below 1e-7.
5. Godot linearises the clear colour, so in byte space the fog background goes in sRGB-encoded (`ByteSpaceBlending.BackgroundColor`, used by `RenderGlobals.FogBackground`).

*Spike note.* A first version resolved the frame with a C# `CompositorEffect` instead: a compute pass at `PostTransparent` over the resolved `render_buffers/color`. `GetColorLayer` returns the 4× MSAA target, which has no storage bit. That version passed the same checks with the same pixels. The LUT does the same with no render-thread managed code, no compute pipeline and no GPU objects to free, so it replaced the compute pass.

**Checks.** `--blend-checks` (`scripts/Render/BlendChecks.cs`) renders known quads with the real surface shaders, plus two built-in Godot materials, and verifies 10 checks in each space (20 in all). All hold:

| Check | Byte space (retail) | Linear space |
|---|---|---|
| Opaque raw (0.8, 0.4, 0.2) | (204, 102, 51) | (204, 102, 51) |
| Fog clear colour (0.3, 0.5, 0.7) | (76, 127, 178) | (76, 127, 178) |
| Black Mix 0.5 over grey 0.6 | 76 | 111 |
| Black Mix 0.5 over the clear colour | (38, 64, 89) | (54, 92, 130) |
| AddOne glow (0.4, 0.2, 0.05) over a dark wall (0.1, 0.1, 0.12) | (127, 76, 43) | (106, 59, 36) |
| The same glow over grass (0.35, 0.45, 0.15) | (191, 166, 51) | (133, 125, 42) |
| Known deviation: grey 0.6, AddOne white, black Mix 0.7 (retail's 8-bit buffer: 77) | 123 (expected 122) | 169 |
| Detail pass, grey 0.5 × (0.2 + 1 − 0.1) (§2.4) | 140 | 140 |
| Built-in `StandardMaterial3D` (unshaded), albedo `BuiltinColor(0.9, 0.75, 0.35)` | (229, 191, 89) | (229, 191, 89) |
| Built-in `Label3D` glyph, modulate `BuiltinColor(0.9, 0.75, 0.35)` | (228, 190, 88) | (229, 191, 89) |

In byte space the glow adds the same bytes over the wall and over the grass (+102 red, +51 green), as in retail. In linear space it adds +80 red over the wall and +44 red over the grass, so it reads as a box over dark surfaces.

**Built-in Godot materials (rule).** *(Review fix.)* Godot's own 3D materials (`Label3D` / `Sprite3D` modulate, `StandardMaterial3D` / `ORMMaterial3D` colours) sRGB-decode the colours they are given. In linear space the tonemap re-encodes them; in byte space the decode LUT decodes the frame once more, so a colour handed over unchanged is decoded twice and shows darker and more saturated. The first byte-space build did this to the spell viewer's words: cream (1, 0.95, 0.75) showed as golden, median (237, 214, 133) against (240, 228, 183) on master.
- **Rule:** a built-in 3D material in the main view gets its colours through `ByteSpaceBlending.BuiltinColor(raw)` (sRGB-encoded in byte space, unchanged in linear space; alpha unchanged), set after `ByteSpaceBlending.Select`. Otherwise it must not be used: draw with a Lifestone shader that ends in `ac_output`. Scene files (`.tscn`/`.tres`) must not declare such materials, because their colours cannot follow the selected space.
- **Guards:** the two built-in probes above. Without `BuiltinColor` they read (201, 133, 26) and (200, 133, 25) in byte space, the double decode, so they fail. `BuiltinMaterialColourRuleTests` (Client tests) fails if a script creates a `Label3D`, `Sprite3D`, `AnimatedSprite3D`, `StandardMaterial3D` or `ORMMaterial3D` without using `BuiltinColor`, or if a scene file declares one.
- **Today** the spell viewer's words are the only such material in a main view. After the fix they measure median (235, 227, 183) in byte space and (240, 228, 183) in linear space; the residual red difference is the anti-aliased glyph edges blending in byte space. See `docs/screenshots/m1/fix-render/review/label-pair.png`, which shows master, the first byte-space build and the fix. 2D UI (`CanvasLayer`) is drawn after the tonemap pass and is not affected.

**Comparisons.** Screenshots are gitignored (`docs/screenshots/m1/fix-render/`: `before/` master, `after/` byte space, `after-linear/` this branch with `--blend-space=linear`, pairs in `pairs*/`).
- Byte against linear over the 14 golden viewpoints and 17 extra views: mean absolute difference 0.04–0.3 levels where no translucent or additive surface is in view (11 goldens and views measured). Those residual differences are the fp16 round trip, MSAA edges and the HUD's frame counters. The terrain is unchanged: the terrain golden test still passes.
- Where blending happens it changes as intended: the flame bolt crop changes by 9.7 mean (local contrast up from 52 to 60), and the rain streaks of golden 07 show more.
- **Framed translucent instances** *(review fix)*. Both are fountain water, Surface 0x080001A1, Translucent tr 0.25, so Mix at alpha 0.75 over the basin floor. They are taken from the audit's sweep of translucent instances. Measured as linear → byte over water-only crops (`docs/screenshots/m1/fix-render/review/{linear,byte}/`, pairs `review/pair-tr-*.png`):
  - **0x0200027B at 0x0EBA** (`--start=0EBA --interior-eye=0EBA0023@105.4,61.2,4.4 --interior-look=0,-45`, crop x 455–600, y 165–290): mean (50.7, 61.5, 75.0) → (48.6, 60.7, 71.1). Mean absolute difference 2.24, p99 11, 4.9% of pixels over 10 levels.
  - **0x02000117 at 0x40BF** (`--start=40BF --interior-eye=40BF0024@108.46,80.5,11 --interior-look=0,-40`, crop x 500–780, y 415–460): mean (48.9, 67.7, 91.7) → (46.4, 66.4, 86.3). Mean absolute difference 3.05, p99 11; the other two water-only crops differ by 2.85 and 2.39.
  - **Direction.** In linear space the lighter floor tiles show through the water; in byte space the water is darker and more even. That is the expected direction: a linear-light mix of a dark source over a lighter floor is brighter than the byte mix retail computed. The exact arithmetic is pinned by the Mix checks above.

**Deviations that remain** (pinned by the checks):
- **No saturation between blends.** Retail's buffer clamped to 255 after every blend; the float buffer clamps once at the end. An additive surface followed by a blend over it gives a brighter result than retail. This is the same deviation docs/sky.md §8 pins for the sky view.
- **No 8-bit rounding between blends**, so results can differ by a level.

**Rule for every viewport.** A viewport that renders Lifestone shaders outside the main `WorldEnvironment` (its own `World3D` or its own camera `Environment`) must either run in linear space or get the decode LUT (`ByteSpaceBlending.Attach(Environment)`). `InteriorEngineChecks` selects linear space for its run. The terrain preview and its golden test never select a space, so they keep the project default, linear. The sky view outputs raw values by design (docs/sky.md §4.3).

**Cost.** Three LUT fetches per pixel in the existing tonemap pass, and one 48 KB texture.

Fly path Holtburg → Arwic at 200 m/s, vsync off, 1280×720, Debug build, rounds alternated (2026-10-10, final build):

| Configuration | Runs | Avg frame (each run) | Mean | p99 range |
|---|---:|---|---:|---|
| Default: byte space, detail, seamless normals, following sky | 5 | 1.02, 0.93, 1.09, 0.94, 0.76 ms | 0.95 ms | 1.47–3.12 ms |
| `--blend-space=linear --detail=off --retail-normals --retail-sky-anchor` (master's pictures) | 5 | 1.06, 0.82, 0.95, 0.96, 0.77 ms | 0.91 ms | 1.50–2.65 ms |
| `--blend-space=linear` | 2 | 1.00, 0.94 ms | 0.97 ms | 2.29–2.38 ms |

- **No measurable difference.** The configurations differ by less than the spread between rounds.
- **Noise.** Other agents' Godot processes shared the machine. In every configuration, single frames of 40–138 ms appear in some flights: in the default and linear runs, and up to 48 ms in the master-like ones. None of them had a GC, a main-thread drain or a rebase in it, so they are external stalls; re-measure on a quiet machine before reading anything into them.
- **The compute-pass spike** measured the same way (three rounds against linear: 1.11 against 0.97 ms mean). It costs more than the LUT, which has no per-frame code.
- **Start-up.**
  - The render warm-up (detail textures and the two building-detail variants) grew from a median 183 to 205 ms over 18–22 runs.
  - The interiors' cell-variant compile grew from 309 to 321 ms.

`--blend-space=linear` restores the wave-2 pipeline for comparison.

### 2.4 Detail textures (buildings and EnvCells)

*(Wave 3, render-fidelity fix: `Client/Rendering/SurfaceDetail.cs`, `scripts/Render/DetailTextures.cs`.)* Retail draws a detail texture over every textured subset of a building shell and of an EnvCell's own mesh, so magnified wall and floor texels get stone grain. Re-expressed from acclient 11.4186:
- **Which texture.** `LScape::SetDetailTexturing` (acclient.c:308209) builds `Render::building_detail_surface` from TMTerrainDesc[1] and `Render::environment_detail_surface` from TMTerrainDesc[2] (`LScape::GenerateDetailSurface`). It also sets `Render::*_detail_tiling` from their `detail_tex_tiling`. This happens while the "Environment Detail Textures" preference is on, which is its default (acclient.c:45603). Landscape and object detail are never generated (docs/terrain-shader.md §7).
  - In the user's dats both are SurfaceTexture 0x05001787 (RenderSurface 0x06006D58, A8R8G8B8 256×256), tiling 4.
- **When.** `RenderDeviceD3D::DrawEnvCell` makes the environment surface current for the cell's own mesh, and `RenderDeviceD3D::DrawBuilding` makes the building surface current for the shell. Both use blend `DESTCOLOR/INVSRCALPHA` (`curr_detail_src_blend` 9, `curr_detail_dst_blend` 6) and clear the surface afterwards.
  - Objects inside cells, statics and scenery get no detail pass.
  - With a detail surface current, `D3DPolyRender::DrawMesh` defers nothing to the alpha list.
- **How.** `D3DPolyRender::RenderMeshSubset` draws each subset a second time. Single-pass detailing is off: `trysinglepass_2` is never set. The second pass works like this:
  - stage 0 samples the detail texture with WRAP and linear filtering (`ACRender::SetDetailSurfaceInternal(0)`);
  - the texture coordinates are UV set 1, which `D3DPolyRender::SetDetailTiling` sets to `tiling × UV`;
  - the sample is MODULATEd with the same lit vertex colour, and its alpha with the same diffuse alpha;
  - it is fogged like the base, because the fog state stays;
  - it is drawn with depth LESSEQUAL.
- **Two corrections to the interiors notes.** The 3.0 that `CEnvCell::UnPack` passes to `ConstructMesh` (and the 1.0 GfxObjs pass) is only the initial UV factor, which the first draw replaces with the tiling. `ACRender::get_alpha_for_z`'s 10–50 m fade belongs to the software polygon paths (`DrawPolyInternal`, `landPolyDraw`), not to the mesh path used for built cells and GfxObjs.
- **The formula.** The framebuffer becomes `c × (detail.rgb × light + 1 − detail.a × alpha)`, saturated. The texture's colour and alpha are close (means 42 and 34 of 255), so the factor at full light is 1.03 ± 0.10: grain, not a tint.
- **Lifestone.**
  - `RenderResources.PrepareSurface` adds `SurfaceDetail.Environment` to every EnvCell-lighting request; the objects layer (and with it the spell viewer) asks for `SurfaceDetail.Building` for building shells.
  - `SurfaceShaderVariant.WithDetail` keeps it only on textured, opaque surfaces without an alpha test. On alpha-tested ones the pass never survives the test, since the detail alpha of 24–46 is below 200.
  - The detail variant folds the second pass into the opaque draw, which is exact in one pass for an opaque base. `--detail=off` turns it off.
- **Left out (documented).** The pass over blended subsets would also scale what shows through them. The dats have no textured blended building surface and one textured blended cell surface (12 of 734,976 cells), so it is not drawn there.
- **Measured.** At close range (about 1 m) it adds the stone grain retail shows; see `pairs-detail/redoubt-x11-crop.png`, `hotel-floor-crop.png` and `bldg-f-crop.png`. At 3–10 m the texture is minified to its mean, so walls change by under 1 level on average (about 7% darker in dim dungeons, `1 − 0.13 + 0.17 × light`).
  - Cost: 2 extra shader variants (opaque WRAP/CLAMP with building detail) plus their EnvCell counterparts. Some materials are no longer shared between buildings and statics: Holtburg uses 513 materials instead of 507.

## 3. Textures (`TextureCache`)

- **Key** (docs/textures.md §7): `(RenderSurface id, palette key, clip-map flag)` plus, only under the passthrough policy, an "alpha-critical" flag. The palette key is `"default"` for the texture's own palette, else `PaletteData.CacheKey`.
- **Worker** (`TextureCache.Prepare`): `TextureLoader.DecodeRenderSurface` → Godot `Image` → `GenerateMipmaps()`. The image and `TextureInfo` (size, has-alpha, passing-texels-blend) travel in a `PreparedTexture`.
  - *Wave-3 performance pass:* `RenderResources.PrepareSurface` calls `TextureCache.PrepareShared`, which decodes through pooled buffers (`TextureLoader.DecodeRenderSurfacePooled`) and lets concurrent or pending requests for the same texture share one decode. A `PreparedTexture` is then one lease on a shared image; disposing it is unchanged (the image goes with the last lease). docs/textures.md §9.
- **Main thread:** `ImageTexture.CreateFromImage`, then the `Image` is disposed at once (the pixels now live in the RenderingServer). Textures are refcounted in the shared `RenderResourceCaches` (§4.1): LRU by bytes, default budget 768 MB (`--texture-budget-mb`). Idle textures are evicted least recently released first and disposed, which frees their GPU memory at once.
- **Upload forms:**
  - Everything but DXT and JPEG becomes RGBA8, decoded by the textures track (palettes, clip maps, channel orders).
  - **DXT, default `--dxt=passthrough`:** level 0 is the dat's blocks, uploaded unchanged (`FORMAT_DXT1/3/5`). Levels 1 and below are box-filtered from the RGBA8 decode and re-encoded by `Lifestone.Client.Textures.MipChain`/`BlockEncoder`.
    - This is what retail's `D3DXFilterTexture` does to a DXT texture: decompress, box-filter, recompress. Our encoder is a simple range fit, so lower levels can differ from D3DX by encoder rounding.
    - Godot itself cannot generate mipmaps for compressed images (`core/io/image.cpp`).
    - **Exception:** DXT1 with punch-through texels on a blended or alpha-tested surface. Godot uploads `FORMAT_DXT1` as BC1 *without* alpha (RenderingDevice format `BC1_RGB_UNORM_BLOCK`), which makes retail's transparent texels opaque black. That texture therefore gets an RGBA8 twin under the key flag `AlphaCritical`, and opaque surfaces keep sharing the block upload. `--engine-checks` verifies both the format and the rendered result (game-core.md §6).
    - **Measured:** the Holtburg view uploads 102 of its 211 textures as blocks, and texture memory is 14 MB instead of 48 MB expanded.
  - **DXT, `--dxt=expand`:** every DXT texture is decoded to RGBA8 with Godot-generated mips. It uses 4–8× the memory and has no re-encoding.
  - JPEG: `Image.LoadJpgFromBuffer`, an ordinary decode with no channel swap.
- **Mipmaps.** Godot's `GenerateMipmaps` builds the full chain with a 2×2 box filter on raw bytes. Retail builds at most 4 box-filtered levels (`ImgTex::CreateD3DTexture`, `D3DX_FILTER_BOX`; docs/textures.md §7). Instead of truncating the chain, which an `ImageTexture` cannot do, the surface shader samples `textureLod(tex, uv, clamp(textureQueryLod(tex, uv).y, 0, ac_max_mip_level))`, which never minifies past level 3, as retail. Distant textures therefore shimmer as they did in retail; `--max-mip=16` shows Godot's full chain.
- **Filtering:** `filter_linear_mipmap` (retail LINEAR min/mag/mip), no anisotropy.

## 4. Meshes and the main-thread split

`RenderResources` (`GameServices.Render`) is the facade:

| Step | Thread | Call |
|---|---|---|
| Convert `MeshData` to Godot arrays (`AcToGodot` positions, normals, reversed triangles; UVs unchanged; zero-normal flags; AABB from `MeshData.Bounds`) | worker | `MeshFactory.Prepare(mesh, include?)` |
| Resolve a sub-mesh's surface and decode its texture if not resident | worker | `RenderResources.PrepareSurface(SurfaceRequest.For(subMesh))` |
| A shared mesh with its materials and textures: built if missing, materials re-bound if it was idle | main | `AcquireMesh(key, preparedMesh, preparedSurfaces)` |
| Or, for a mesh of one's own: a material handle per sub-mesh, then the ArrayMesh | main | `AcquireMaterial(prepared)`, `MeshFactory.Build(prepared, i => material)` |
| Release on detach | main | `ReleaseMesh(key)` / `ReleaseMaterial(handle)` |
| Free decoded images that were not uploaded | any | `PreparedSurface.Dispose()` (the streamer disposes `IDisposable` prepared objects) |

- **Errors.** A surface that cannot be resolved (missing record, unresolvable SurfaceTexture, unsupported blend) is logged as an error and drawn with the unlit magenta error material, so it is visible rather than silently wrong. The error material is not counted (its handle has no key).
- **Draw rules.** Buildings and EnvCells skip untextured sub-meshes (`SurfaceDrawRules`, `MeshDrawContext`); pass the filter to `MeshFactory.Prepare`.
- **Budgeted attach.** One `AcquireMesh` (materials, texture uploads, mesh build) is the natural unit of an attach step: return the attach as an iterator that yields after each part (game-core.md §4).
- **Smoke test.** Retired in M1 wave 3 (objects track): `--smoke-statics` is reported as a bad argument and ignored; the objects layer and the spell viewer draw every static through this path.
- **Objects.** The objects layer ([objects.md](objects.md)) draws statics, buildings and scenery through exactly this path, as `MultiMesh` batches of shared meshes with retail LOD. The statics inside EnvCells stay merged into their cells' meshes; their LOD levels are selected through the global `ac_object_lod` table (objects.md §2.6).

### 4.1 Memory: chained caches (`Lifestone.Client.Rendering.RenderResourceCaches`)

Textures, materials and shared meshes live in three refcounted LRU caches that are **chained**:

- **A held entry holds its dependencies:** a mesh holds its materials, and a material holds its texture.
- **An idle entry holds nothing.** When the last holder releases a mesh, its surfaces' materials are unbound and released. A material that goes idle unbinds and releases its texture.
- **Each budget therefore bounds its own resource:**

  | Cache | Budget | Default | Flag |
  |---|---|---|---|
  | Textures | GPU bytes | 768 MB | `--texture-budget-mb` |
  | Materials | count | 4,096 | — |
  | Meshes | geometry bytes | 256 MB | `--mesh-budget-mb` |

  A cache exceeds its budget only by what is in use.
- **Re-acquiring an idle entry** binds its dependencies again. An idle mesh takes material handles from the landblock's freshly prepared surfaces. An idle material takes its texture from the worker's decode, the cache, or (rarely, logged) a main-thread decode.
- **Freeing is immediate.** Evicted `ImageTexture`s, `ShaderMaterial`s and `ArrayMesh`es are `Dispose`d, so their GPU memory goes at once instead of when the garbage collector finalises the C# wrapper. Nothing still references them, because idle entries were unbound when they went idle.
- **Tested:** `RenderResourceCachesTests` simulates a 600-landblock flight with budgets far below what the flight touches. It asserts:
  - every cache stays within its budget plus what is in use;
  - thousands of evictions happen at each level;
  - nothing is freed while bound, and everything is freed exactly once.

  The fly-path report prints the cache counts and evictions (game-core.md §7).
- **Mesh keys** must identify the geometry *and* its surfaces. Once ObjDesc palette and texture overrides exist (M2), they belong in the key.

## 5. What each track must use

| Track | Must |
|---|---|
| Terrain | Register an `ILandblockLayer` (done: `TerrainLandblockLayer`; `TerrainLayer` is the vertex-coloured placeholder, `--placeholder-terrain`); light with `ac_sun_color × max(0, n·ac_sun_dir) + ac_land_ambient` (clamped, truncated to bytes as docs/terrain-shader.md §5 shows); fog with the `ac_fog_*` uniforms; convert with `ac_srgb_to_linear`; register `TerrainSampler`s with `GameServices.Ground`; `Dispose` per-landblock meshes on detach. |
| Objects (scenery, statics, buildings) | `RenderResources` for every surface (`AcquireMesh` for shared meshes); materials keyed by `SubMesh.Addressing`; meshes from `MeshFactory` (zero-normal flag); placement through `AcToGodot.Transform`; LOD by `LodChain.Select`; building draw rules; a stepwise `Attach` (one part or a few per step); `MultiMesh` instances may share `ShaderMaterial`s (instance uniforms for per-object overrides once they exist). |
| Interiors | Same surface path with `MeshDrawContext.EnvCell`; add the indoor lighting rule and burned-in lights as variant/material extensions (extend `SurfaceShaderVariant` + `SurfaceShaders.GenerateCode`, keep the names). Done: `SurfaceRequest.Lighting` / `SurfaceShaderVariant.Lighting`, code in `SurfaceLightingCode` (docs/interiors.md §4). Per-cell state uses no instance uniforms (a slot in `CUSTOM1.a` into the global `ac_cell_states` table): Godot reserves a 16-vec4 block of the global uniform buffer for every instance whose material declares an instance uniform, hidden or not, about 4,000 instances with the default buffer size and shared by every track (docs/interiors.md §3.4). |
| Sky | Drive `RenderGlobals.Apply(SkyState)`; sky objects use `RenderResources` with their own render state (camera-centred, depth always, no z-write) as an extension of the variant key. *Done as [sky.md](sky.md) describes: retail anchors the sky at the viewer's landblock corner rather than the camera, and draws it in its own view with byte-space blending and `SkySurfaceVariant` shaders. Since wave 3 the default anchor follows the camera's x and y at retail's height (`--retail-sky-anchor` restores retail's; sky.md §4.2).* |
| Spell viewer | Same as objects; particles' additive surfaces are AddOne/AddSourceAlpha variants with fog off. |

Extending the contract: add fields to `SurfaceShaderVariant` (engine-agnostic) and branches to `SurfaceShaders.GenerateCode`, keep existing uniform names, and add a test next to `SurfaceShaderVariantTests`.
