# Terrain shader specification (M1)

This is what the wave-2 Godot terrain shader has to compute so that Lifestone's landscape matches the retail client. All the data it needs comes from `Lifestone.Data.Terrain.Blending`. The CPU reference in `TerrainCompositor` is the golden model to test the shader against.

Sources are cited by function name and line in the Hex-Rays decompile of acclient 11.4186 (Mag-nus/acclient-AI-RE, `acclient.c:NNNNN`). That decompile is reference only and none of it is in this repo. Mag-nus doc 13 reports that the rendering core (`ImgTex`, `D3DPolyRender`, …) did not change in 11.6096, the user's build. I did not diff `CTexMerge`, `CLandBlockStruct` or `ACRender` between the two builds myself.

## 1. What retail does

Retail does not blend terrain on the GPU:

- **Merged texture per palette code.** For every distinct cell *palette code* it builds one 1024² texture on the CPU (`TexMerge::FillTempTexBuffer`, acclient.c:305909).
- **Drawing.** The cell's two triangles are drawn with that texture, using fixed-function MODULATE with per-vertex light colours (`ACRender::landPolyDraw`, acclient.c:719994).
- **Lighting.** Lighting is off (`SetFFLighting(0)`). The vertex colours come from `CLandBlockStruct::calc_lighting` (acclient.c:353713).
- **Sampling.** The texture is sampled with LINEAR min, mag and mip filtering (`D3DPolyRender::SetSurface`, acclient.c:454385).

Lifestone does the same arithmetic per pixel in a shader, using two texture arrays. The result is the same function of (cell, u, v). The only differences are filtering and mip-mapping, covered in §7.

## 2. Conventions

- **Cell UV.**
  - `u` runs east and `v` runs south: SW = (0,1), SE = (1,1), NE = (1,0), NW = (0,0).
  - Retail fills `CLandBlockStruct::land_uvs` with exactly these values in `CLandBlock::init` (acclient.c:351988). `ConstructUVs` uses them with rotation 0, because a TexMerge region always gets rotation 0 from `LandSurf::SelectTerrain` (acclient.c:304328).
  - Texture row 0 is the cell's **north** edge and column 0 its **west** edge. `TerrainCompositorTests.Real_alpha_maps_cover_the_corners_and_sides_their_codes_name` confirms this against the real alpha maps.
- **Godot UVs need no flip.** Godot UV (0,0) is also the top-left texel.
- **Coverage codes.** Bits are SW = 1, SE = 2, NE = 4, NW = 8. A TCode says where the *overlay* terrain is. An RCode says which corners carry road.
- **Alpha.** An alpha byte of 255 keeps what is underneath. A byte of 0 is 100% of the new layer.
- **Axes.** The data layer works in AC axes (X east, Y north, Z up). `AcToGodot` maps a position to `(x, z, -y)`. Apply the same mapping to normals and to the sun vector; the dot product is unchanged, because the mapping is a proper rotation.

## 3. Inputs

### 3.1 Texture arrays (from `TerrainAtlasManifest`)

| Array | Layers | Format | Sampler (Godot hints) |
|---|---|---|---|
| `terrain_layers` | `TerrainLayers` (30 in Dereth, 512²) | RGBA8. **No** `source_color` hint (see §6) | `filter_linear_mipmap, repeat_enable` |
| `alpha_layers` | `AlphaLayers` (8: corner ×4, side ×1, road ×3, all 512²) | R8 (alpha byte) | `filter_linear_mipmap, repeat_disable` |

- **Layer identity.** Each colour layer is a unique (SurfaceTexture, TexTiling) pair. 33 TMTerrainDesc entries collapse to 30, because types 0/24/31 share one texture and 16/22 share another. `LayerForTerrainType(t)` implements retail `TexMerge::GetTerrainTex` (acclient.c:304870): the first desc with that type, else desc 0. The road is terrain type 32; its layer is `manifest.RoadLayer`.
- **Texture decoding.** The textures track turns the SurfaceTexture ids into pixels. Retail uses level `[0]` of a SurfaceTexture (`ImgTex::GetSurfaceDID`, acclient.c:366232). For the alpha maps that level is the copy in `client_highres.dat`.
- **Tiling.** `TilingOfLayer(layer)` is `TexTiling`, which is 2 for every Dereth terrain texture: the texture repeats every 12 m. Pass it as a `uniform float layer_tiling[N]`.

### 3.2 Per-vertex data (from `TerrainBlendVertices.Build(mesh, blend, normals)`)

The mesh is non-indexed: 384 vertices (128 triangles × 3) in `TerrainMesh.Indices` order, with AC winding. The Godot layer reverses each triangle.

| Attribute | Content |
|---|---|
| VERTEX | landblock-local position (convert with AcToGodot) |
| NORMAL | retail lighting normal of the source height-grid vertex (`TerrainVertexLighting.ComputeNormals`) |
| UV | cell UV from §2 |
| CUSTOM0 (RGBA8 unorm) | base layer, overlay0 layer, overlay1 layer, overlay2 layer |
| CUSTOM1 (RGBA8 unorm) | overlay0 alpha, overlay1 alpha, overlay2 alpha, road0 alpha |
| CUSTOM2 (RGBA8 unorm) | road1 alpha, `rot0 \| rot1<<2 \| rot2<<4 \| roadRot0<<6`, `roadRot1 \| flags<<2`, road texture layer |
| CUSTOM3 (optional) | CPU-baked light bytes (RGB, A = 255), if lighting is not computed in the shader |

- **Reading a byte.** In the shader a byte is `int(round(CUSTOMn.c * 255.0))`.
- **Unused slots.** 255 marks an unused slot. A cell's overlays and roads are always a prefix, so stop at the first 255.
- **Flags.** Flag 1 is retail's single-texture cell (WRAP addressing); flag 2 is all-road. Neither changes the colour formula.
- **Reference packer.** `TerrainBlendPacking.Pack` and `Unpack` are the reference implementation.
- **Alternative layout.** An 8×8 RGBA8 data texture per landblock (3 texels per cell) would also work. CUSTOM attributes are simpler to stream.

### 3.3 Uniforms (from the sky track: `LScape::sunlight`, `sunlight_color`, `ambient_level`, `ambient_color`)

| Uniform | Meaning |
|---|---|
| `sun_vector` | `LScape::sunlight` in Godot axes. **Not normalised**: its length is the keyframe DirBright. It points towards the sun. |
| `sun_color` | sun colour bytes × 0.0039215689 (the float 1/255 that calc_lighting uses) |
| `ambient_color` | ambient colour bytes × 0.0039215689 |
| `ambient_level` | `LScape::ambient_level` = **max(AmbBright, 0.2)**, not the raw AmbBright (see below) |

`LScape::set_landscape_lighting` (acclient.c:307003) writes these values. Retail then relights every loaded block. In a shader the relight is free: update the uniforms.

**Ambient floor.** Retail floors the ambient level at `LScape::min_ambient` before it calls `set_landscape_lighting`:

- `LScape::UseTime` (acclient.c:307261) does `if (ambBright < min_ambient) ambBright = min_ambient`. The always-daylight path in `set_landscape_lighting` does the same (acclient.c:307024).
- `LScape::min_ambient` has a single writer, which sets it to `LSCAPE_LIGHT_MINIMUM` = 0.2 (acclient.c:783184).
- `TerrainLightingEnvironment.FromSkyLighting` applies the floor; the constant is `TerrainVertexLighting.MinAmbientLevel`.
- The floor never triggers in Dereth's sky data today: all 232 SkyDesc keyframes have AmbBright ≥ 0.25 (`TerrainVertexLightingTests.Dereth_sky_keyframes_sit_above_the_ambient_floor`). Apply it anyway.
- After the floor, `UseTime` can replace or blend the ambient level and colour towards a server-sent override (`LScape::m_override_*`, set by `CPlayerSystem::Handle_Admin__Environs`, acclient.c:396298). That belongs to the sky/environs owner.

## 4. The per-cell recipe (`TerrainTexMerge`, `TerrainCellBlend`)

This runs on the CPU, once per palette code; the result is cached, and Dereth has 9,059 distinct codes. The shader only consumes the result. In outline:

1. **Palette code.** `CLandBlockStruct::GetCellRotation`, acclient.c:354223:

   ```
   pal = 1<<28 | rSW<<26 | rSE<<24 | rNE<<22 | rNW<<20 | tSW<<15 | tSE<<10 | tNE<<5 | tNW
   ```

   - `t = (word>>2)&31` and `r = word&3`.
   - The `1<<28` size bits belong to full-detail blocks. They feed the hash in step 4.

2. **Base and overlays.** `TexMerge::GetTerrain`, acclient.c:305246:
   - **Some corners share a type.** The first duplicated type (scanning i = 0..2, j > i) is the base. Overlay 0 starts at the first other corner k with code `1<<k`.
   - **The next non-base corner.** If it has overlay 0's type and overlay 0's code is exactly `1<<(k-1)`, its bit is added to overlay 0. Otherwise it becomes overlay 1. The scan then stops.
   - **All four corners differ.** The base is SW and the overlays are SE, NE and NW with codes 2, 4 and 8.

3. **Roads.** `TexMerge::GetRoadCode`, acclient.c:304565:
   - A mask over corners with road bits ≠ 0.
   - `0xF` means all road: the base becomes the road texture and nothing else is merged.
   - `0xE` gives (6, 12), `0xD` gives (9, 12), `0xB` gives (9, 3) and `0x7` gives (3, 6).
   - Any other mask m gives (m, —).

4. **Alpha maps.** `TexMerge::FindTerrainAlpha` (acclient.c:304756) and `FindRoadAlpha` (acclient.c:304697).
   - **The hash.** `idx = floor((uint32)(1379576222*pal − 1372186442) · 2^-32 · n)`.
   - **Corner or side list.** Codes 1, 2, 4 and 8 use the corner list; anything else uses the side list.
   - **Rotation.** The picked map is rotated (`c*=2; if c>=16: c-=15`) until its code matches. The number of steps is the rotation.
   - **Roads.** The search starts at `idx` and tries each road map in turn.
   - **Exact arithmetic.** Retail's constant is the **float** 2.3283064e-10f = 2^-32 exactly, so the product is computed exactly as `(x*n)>>32`. ACE, ACViewer and OpenAC use a slightly smaller double literal. That difference never matters for a palette code that exists in Dereth; the exhaustive test proves it.

**Order.** The result is applied in this order: base → overlay 0 → overlay 1 → overlay 2 → road 0 → road 1.

**Distribution over the world** (4,161,600 cells):

- 3,429,233 cells have no overlay, 693,704 have 1, 38,387 have 2 and 276 have 3.
- 18,471 cells have road layers.
- 209 cells are all road.

## 5. The fragment algorithm

This is retail `ImgTex::TileCSI` (acclient.c:365513) and `ImgTex::MergeTexture` (acclient.c:365632), evaluated per pixel:

```
c = T[base](uv * tiling[base])                                   // TileCSI: base repeated tex_tiling times
for each overlay i (in order, stop at the first unused):
    a = A[alpha_i](rot(rotation_i, uv))                            // alpha map read through its rotation
    c = mix(T[layer_i](uv * tiling[layer_i]), c, W(a))             // keep W(a) of what is underneath
for each road j (in order, stop at the first unused):
    a = A[roadAlpha_j](rot(roadRotation_j, uv))
    c = mix(T[roadLayer](uv * tiling[roadLayer]), c, W(a))
```

**Rotations.** These come from the start offsets and strides in MergeTexture:

| rot | alpha-map UV |
|---|---|
| 0 | (u, v) |
| 1 | (1 − v, u) |
| 2 | (1 − u, 1 − v) |
| 3 | (v, 1 − u) |

**Weight.** Retail computes, per colour byte, `a' = a + (a > 128)` and `dst = (a'·dst + (256 − a')·src) >> 8`. Texels with a = 255 are skipped, but a' = 256 gives the same result. The exact weight is therefore

```
W(a8) = (a8 + clamp(a8 − 128, 0, 1)) / 256        // a8 = alpha * 255
```

- This equals retail at every byte value: 0 → 0, 128 → ½, 129 → 130/256, 255 → 1.
- It stays continuous under filtering.
- Plain `W = a` is within 1/256 of it.
- The alpha byte of the merged texture is never changed. Terrain is opaque.

**Tiling nuance.**
- `TileCSI` repeats the base `tex_tiling` times.
- `MergeTexture` ignores its tiling argument. It repeats the overlay every *texture width* buffer texels: 1024 / 512 = 2.
- Both rules give 2, because BaseTexSize = TexTiling × 512 for every Dereth texture (`TerrainAtlasManifestTests`). The shader can therefore use `uv * tiling[layer]` for every layer.

### Reference Godot shader sketch (not compiled; the wave-2 owner must validate it)

```glsl
shader_type spatial;
render_mode unshaded, cull_back;   // triangles reversed to Godot's winding

uniform sampler2DArray terrain_layers : filter_linear_mipmap, repeat_enable;
uniform sampler2DArray alpha_layers : filter_linear_mipmap, repeat_disable;
uniform float layer_tiling[64];
uniform vec3 sun_vector;
uniform vec3 sun_color;
uniform vec3 ambient_color;
uniform float ambient_level;

varying flat ivec4 v_tex;    // base, ovl0, ovl1, ovl2 layers
varying flat ivec4 v_alpha;  // ovl0, ovl1, ovl2, road0 alpha layers
varying flat ivec4 v_misc;   // road1 alpha, rotations, road1 rot | flags << 2, road layer
varying vec3 v_light;

int byte_of(float x) { return int(round(x * 255.0)); }

void vertex() {
    v_tex = ivec4(byte_of(CUSTOM0.r), byte_of(CUSTOM0.g), byte_of(CUSTOM0.b), byte_of(CUSTOM0.a));
    v_alpha = ivec4(byte_of(CUSTOM1.r), byte_of(CUSTOM1.g), byte_of(CUSTOM1.b), byte_of(CUSTOM1.a));
    v_misc = ivec4(byte_of(CUSTOM2.r), byte_of(CUSTOM2.g), byte_of(CUSTOM2.b), byte_of(CUSTOM2.a));
    // calc_lighting (needs a mesh node whose basis is identity, so model space == world axes)
    float d = max(dot(NORMAL, sun_vector), 0.0);
    vec3 c = min(sun_color * d + ambient_color * ambient_level, vec3(1.0));
    v_light = floor(c * 255.0) / 255.0;   // landPolyDraw truncates to D3DCOLOR bytes; Gouraud-interpolated
}

vec2 rot_uv(int r, vec2 uv) {
    return r == 0 ? uv : r == 1 ? vec2(1.0 - uv.y, uv.x) : r == 2 ? vec2(1.0 - uv.x, 1.0 - uv.y) : vec2(uv.y, 1.0 - uv.x);
}
vec3 layer_rgb(int l, vec2 uv) { return texture(terrain_layers, vec3(uv * layer_tiling[l], float(l))).rgb; }
float keep(int a, int r, vec2 uv) {
    float a8 = texture(alpha_layers, vec3(rot_uv(r, uv), float(a))).r * 255.0;
    return (a8 + clamp(a8 - 128.0, 0.0, 1.0)) / 256.0;
}
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
    vec3 c = layer_rgb(v_tex.x, UV);
    int rots = v_misc.y;
    for (int i = 0; i < 3; i++) {
        int l = v_tex[i + 1];
        if (l == 255) break;
        c = mix(layer_rgb(l, UV), c, keep(v_alpha[i], (rots >> (2 * i)) & 3, UV));
    }
    if (v_alpha.w != 255) {
        c = mix(layer_rgb(v_misc.w, UV), c, keep(v_alpha.w, (rots >> 6) & 3, UV));
        if (v_misc.x != 255) c = mix(layer_rgb(v_misc.w, UV), c, keep(v_misc.x, v_misc.z & 3, UV));
    }
    c *= v_light;                 // fixed-function MODULATE
    // retail fog (sky track) goes here, still in retail colour space
    ALBEDO = srgb_to_linear(c);   // see §6
}
```

Two alternatives:

- **World-space UVs.** `uv * tiling` can be computed from world position as `pos / 12 m`. It is identical for tiling 2 and avoids precision issues if a future LOD merges cells.
- **CPU-baked lighting.** Lighting can be baked on the CPU into CUSTOM3 or COLOR with `TerrainVertexLighting`. Retail relights every `LightTickSize` (15 s), which would mean re-uploading the meshes. The shader path avoids that.

## 6. Colour space

Retail does every step on raw texture bytes, with no sRGB decoding:

- the merge;
- the vertex-colour modulate;
- D3D fog.

**Recommendation for wave 2 (not verified here).** To get the same screen bytes in Godot:

1. Sample the colour array without the `source_color` hint.
2. Do all the arithmetic above on those raw values.
3. Convert once at the end: `ALBEDO = srgb_to_linear(c)`.
4. Use a Linear tonemapper with exposure 1 and no glow. The output transform then re-encodes the colour to the bytes retail would have shown.

## 7. Where Lifestone differs from retail, and why that is acceptable

- **Filtering order.** Retail mip-maps and bilinearly filters the *merged* 1024² texture. The shader filters each layer and then merges. The two are the same with nearest sampling and at magnification. At distance, the result is a slightly different average.
- **Alpha upscaling.** Retail upscales the 512² alpha maps to 1024 by pixel replication before filtering. Sampling the 512² map with linear filtering is smoother by less than one alpha texel (4.7 cm).
- **Edge addressing.**
  - Retail samples a multi-texture cell's merged texture with CLAMP. A single-texture cell gets stippling 3 in `ConstructUVs` (acclient.c:354677), which `D3DPolyRender::SetSurface` turns into WRAP.
  - With per-layer REPEAT and alpha CLAMP, only the half-texel border of each cell differs.
  - The flag is packed in case it is ever needed.
- **Not implemented, because nothing in Dereth needs them:**
  - reduced-detail blocks (palette size 4, `TransAdjust`);
  - PalShift regions;
  - lower `LandscapeTextureDetail` settings, where the buffer is smaller than 1024.
  - `TerrainCompositor.ComposeRetailTexture` throws on configurations it does not model.
- **Detail textures.** `LScape::SetDetailTexturing(landscape, building, environment, object)` (acclient.c:308209) reads TMTerrainDesc[0..3]'s detail texture and tiling by position: 0 = landscape, 1 = building, 2 = environment, 3 = object. It always sets the four tilings, but generates a detail surface only for the flags that are non-zero. `TerrainAtlasManifest.DetailTextures` lists all four for the other tracks, with a `RetailUse` field:
  - **Landscape detail: `Never`.** The texture is TMTerrainDesc[0]'s `DetailTexGID`, 0x05001786, tiling 4. Retail never draws it. `Render::UpdateFromPreferences` forces the preference off and calls `SmartBox::SetDetailTexturing(0, …)` (acclient.c:381020–381026; Mag-nus doc 06 §5). The other callers also pass landscape = 0. The terrain shader must not draw it.
  - **Object detail: `Never`.** Every caller passes object = 0: `SmartBox::SetDetailTexturing` (acclient.c:143221) forwards `(landscape, environment, environment, 0)`, and `SmartBox::ResetDetailTexturing` (acclient.c:144205) and `LScape::ChangeRegion` (acclient.c:308276) pass `(0, pref, pref, 0)`. `Render::SetObjectDetailSurface` is otherwise only called with 0 (`LScape::CleanupDetailSurfaces`, acclient.c:306372).
  - **Building and environment detail: `EnvironmentDetailPreference`.** Both follow the "Environment Detail Textures" user preference (`byte_81EF95`, registered at acclient.c:382015). It defaults to on (acclient.c:45603). The quality presets set it to 0/0/0/1/1 (`Render::SetOverallGraphicsQuality`, acclient.c:378743), and safe render settings turn it off (acclient.c:382204).
- **Unused per-terrain fields.** `MinVertBright`, `MaxVertBright`, `Saturate` and `Hue` are only read by `TerrainTex::UnPack` and `Pack`. Nothing in the client uses them, so ignore them.
- **Depth offset.** `landPolyDraw` lowers every terrain vertex by `zFightTerrainAdjust` = 0.01 m when it submits the triangles. The shader can do the same in `vertex()`, or use a small depth bias.
- **Lighting seams at block edges.** `calc_lighting` sums only the block's own triangles. A vertex shared by two blocks therefore has two normals, and the lighting is discontinuous across the block edge. This is retail behaviour (see `Block_edge_normals_differ_between_neighbours_as_in_retail`), and the M1 definition of done asks for "no visible seams". Whether to keep it is a product decision for the lead.

## 8. Golden-testing the shader

`TerrainCompositor` is the reference:

| Method | What it computes |
|---|---|
| `Compose(blend, manifest, u, v, texels, alphas)` | Retail's integer merge. With nearest samplers and texel-centre UVs it is identical to `ComposeRetailTexture`, the full 1024² buffer that `FillTempTexBuffer` builds. Tested on synthetic textures and on every Holtburg cell. |
| `ComposeFloat(...)` | The same merge in float with `RetailWeight`, which is what the shader does. It stays within 2 levels of `Compose` on real Holtburg cells, and within 6 levels in the worst synthetic case, because retail truncates after each of up to 5 layers. |
| `InterpolateVertexLight(lights, cx, cy, split, u, v)` and `Modulate(texel, light)` | The lit colour before fog. |

`Compose` and `ComposeRetailTexture` are both product code, so their agreement alone would not catch a misreading they share. `ComposeRetailTexture` is therefore also checked texel for texel against `RetailImgTexOracle`, a test-only re-expression of `ImgTex::TileCSI` and `ImgTex::MergeTexture` as retail walks memory. The oracle works on raw BGRA bytes, with a per-rotation alpha cursor start, x/y strides and 2× replication counters. Two cases are covered:

- 600 synthetic cells with random, asymmetric alpha maps;
- 44 real cells: Holtburg's, plus cells that between them use every reachable pair of alpha-map list and rotation. The side map is only ever used at rotations 2 and 3, because `GetTerrain` never builds the side codes 9 or 3.

Suggested procedure:

1. Render cells with `filter_nearest` samplers, at texel-centre UVs.
2. Compare against `ComposeFloat` with a tolerance of 1–2 levels.
3. Compare against `Compose` with a tolerance of 6 levels.

## 9. Lighting reference (`TerrainVertexLighting`)

This is `CLandBlockStruct::calc_lighting` (acclient.c:353713):

1. **Normals.**
   - Each triangle's plane normal comes from `CPolygon::make_plane` (acclient.c:359628): unit `cross(v1 − v0, v2 − v0)`.
   - The triangles are the 128 from `ConstructPolygons` (acclient.c:354001).
   - The normals are summed per vertex in polygon order and normalised. A sum shorter than 2e-4 becomes (0, 0, 1).
2. **Light.** `d = max(0, n · sunlight)`, then `rgb = min(1, sunColor/255 · d + ambientColor/255 · ambientLevel)` per channel. There is no lower clamp on the result. `ambientLevel` is `LScape::ambient_level`, already floored at 0.2 (§3.3).
3. **Vertex colour.** `landPolyDraw` sends `(uint8)(c · 255.0)` (truncated) with alpha 0xFF.

`TerrainVertexLightingTests` checks the port three ways:

- **OpenAC normals.** The normals are compared with `OpenAcVertexNormalsOracle`, a test-only port of OpenAC's `LandblockMesh.BuildRetailVertexNormals` (MIT). It has its own split formula and triangle lists, so it shares nothing with `TerrainSplit` or the product's triangles. The comparison runs on all 65,025 landblocks (Exhaustive tier) and on the Holtburg area.
- **Double-precision reference.** The lighting is compared with an independent double-precision implementation, which uses the oracle's split. This also runs on all 65,025 landblocks.
- **Mesh normals.** The normals are compared with the `TerrainMeshBuilder` normals on the Holtburg area.
