# Textures and materials

**Track:** M1 wave 1, `textures` (`src/Lifestone.Data/Textures/`).
**Scope:** RenderSurface (0x06) decoding, palettes and ObjDesc palette overrides, SurfaceTexture (0x05) resolution, Surface (0x08) to material, and the derived-data cache decision.
**Authority:** retail behaviour comes from the Mag-nus/acclient-AI-RE decompile (11.4186, PDB names; the render core is unchanged in the user's 11.6096). It is cited by function name and never copied. ACE @95ef426d, ACViewer @c5c54fb and OpenAC @1ee5a08 were cross-checked. Every number below was measured on the user's end-of-retail dats (portal 2072, highres 497).

---

## 0. API map for wave 2

| Need | Call |
|---|---|
| Decode a Surface for drawing | `TextureLoader.DecodeSurface(surfaceId, mode, palette?, surfaceTextureOverride?)` returns `DecodedSurface { Material, Resolution, Texture, Palette }` |
| Material and blend state only | `TextureLoader.GetSurface(surfaceId)` returns `SurfaceMaterial` (`.RenderState`, `.SolidColorTexel`, `.IsClipMap`) |
| Does an alpha-tested texture also blend? | `DecodedTexture.AnyAlphaInRange(SurfaceMaterial.ClipMapAlphaTestReference, 254)` (see §5 rule 2) |
| SurfaceTexture to RenderSurface | `TextureLoader.ResolveSurfaceTexture(stId, TextureDetail.High)` (retail's default is `Low`; see §4) |
| One RenderSurface | `TextureLoader.DecodeRenderSurface(id, mode, clipMap, palette?)`, or the pure `RenderSurfaceDecoder.Decode(...)` |
| One RenderSurface, no texel allocation (wave 3, §9) | `using var t = TextureLoader.DecodeRenderSurfacePooled(id, mode, clipMap, palette?)`: the texels (`t.Data`) live in a buffer rented from `ArrayPool<byte>.Shared` until disposed |
| ObjDesc palette (M2) | `PaletteOverride.FromObjDesc(paletteId, subs.Select(s => SubPalette.FromWire(id, offsetByte, lengthByte)))`, then `TextureLoader.TryBuildPalette`, then pass the `PaletteData` to `DecodeSurface` for parts where `AppliesToPartBasePalette(partBasePalette)` holds |

- `TextureDecodeMode.Native`: DXT1/3/5 blocks and JPEG files pass through unchanged, A8 and LSCAPE_ALPHA become `Alpha8`, and everything else becomes `Rgba8`.
- `TextureDecodeMode.Rgba8`: everything except JPEG is expanded to R,G,B,A on the CPU.
- Thread safety: everything is thread-safe. Dat reads take the `DatSet` lock and decoding runs outside it.
- `DecodedTexture.HasAlpha` is true when any decoded texel has alpha below 255.
- Cache keys for decoded textures must include **RenderSurface id, `PaletteData.CacheKey` and the clip-map flag** (see §7).

## 1. RenderSurface pixel formats

Record layout: `id, dataCategory, i32 w, i32 h, u32 format, i32 len, u8 data[len]`, then `u32 defaultPaletteId` for INDEX16/P8 (`RenderSurface::Serialize`). Retail rejects a record unless `len == w*h*bpp >> 3` (JPEG excepted). All 22,978 records pass.

| Format (id) | Portal | Highres | Bytes per texel, memory order | RGBA8 expansion | Native output |
|---|---:|---:|---|---|---|
| A8R8G8B8 (0x15) | 12,984 | 3 | B,G,R,A | swizzle | Rgba8 |
| INDEX16 (0x65) | 4,182 | 1,283 | u16 palette index | palette (§3) | Rgba8 |
| DXT1 | 1,971 | 986 | BC1 blocks | §2 | Dxt1 |
| R8G8B8 (0x14) | 1,190 | 13 | B,G,R | A = 255 | Rgba8 |
| DXT5 | 127 | 1 | BC3 blocks | §2 | Dxt5 |
| A8 (0x1C) | 86 | – | alpha | (255,255,255,a) | Alpha8 |
| CUSTOM_RAW_JPEG (0x1F4) | 79 | – | whole JFIF file; header w = h = 0 | none | Jpeg |
| CUSTOM_LSCAPE_R8G8B8 (0xF3) | 33 | – | **R,G,B** | A = 255 | Rgba8 |
| CUSTOM_LSCAPE_ALPHA (0xF4) | 16 | 8 | alpha / terrain weight | (255,255,255,a) | Alpha8 |
| P8 (0x29) | 6 | – | u8 palette index | palette | Rgba8 |
| DXT3 | 5 | – | BC2 blocks | §2 | Dxt3 |
| R5G6B5 (0x17) | 3 | – | u16 r5 g6 b5 | bit replication | Rgba8 |
| A4R4G4B4 (0x1A) | 2 | – | u16 a4 r4 g4 b4 | x17 | Rgba8 |

- **Byte orders** follow the channel masks in retail `PixelFormatDesc::SetFormat` (little-endian texels). ACE `Texture.GetImageColorArray`, OpenAC `TextureHelpers` and the scout `acimage.py` agree.
- **Rows** are stored top row first (D3D surface order). Texture coordinates need no flip.
- **LSCAPE_R8G8B8 is R,G,B.**
  - Retail gives format 243 the same masks as R8G8B8. However, nothing in retail reads 243 through those masks: it is not a D3D format (`RenderSurface::IsD3DFormat`), and the only other references are in `SetFormat`.
  - Decoding 0x0600382A (dirt) and 0x06003794 (grass) as B,G,R gives blue soil and teal grass. As R,G,B they look natural.
    - Stored byte 0 averages 41 (dirt) and 55 (grass) above byte 2: byte means are 84.9/66.7/43.7 and 71.6/72.0/16.5.
    - `RenderSurfacePixelTests.Landscape_rgb_decodes_byte_0_as_red` pins this. The scout CRC tests cannot catch a wrong order here, because the scout makes the same R,G,B assumption.
  - The references disagree:
    - ACE reads LSCAPE_R8G8B8 as R,G,B.
    - ACViewer @c5c54fb reads it as B,G,R. Its `TextureCache.AddAlpha` handles it exactly like R8G8B8 and swaps bytes 0 and 2. The colour evidence above overrules it.
  - No Surface references these 33 records. Only 4 SurfaceTextures do.
- **Alpha-only formats** expand to white with alpha. This is a convention, not a retail rule, because D3D alpha-only textures have no colour.
  - The 86 A8 records are data-category-10 atlases that no Surface references, probably font or UI art for a later milestone. Verify the convention against retail UI drawing before relying on it.
  - The terrain blend reads LSCAPE_ALPHA as a raw byte (`ImgTex::MergeTexture`), so use `Native`.
- **X8R8G8B8** is not stored in the dats, but it is decoded because retail expands non-clip-map palettes into it.
- **Any other format** throws `NotSupportedException` instead of guessing.

## 2. DXT and JPEG

- **DXT.** Retail never decodes DXT on the CPU: `RenderSurface::CreateFromSourceData` copies the blocks into a D3D texture. The CPU decoder follows the BCn block layout. Rounding is not bit-specified by D3D, so we use the research reference's rules:
  - bit-replicated RGB565 endpoints;
  - truncating 1/3–2/3 colour interpolation and truncating DXT5 alpha interpolation;
  - DXT1 `c0 <= c1` index 3 decodes to transparent black (0,0,0,0), which is D3D9's 1-bit alpha;
  - DXT3 and DXT5 colour blocks always use four-colour mode.
- **DXT alpha counts.** These DXT records decode to some alpha below 255:

  | Format | Portal | Highres |
  |---|---:|---:|
  | DXT1 | 257 | 21 |
  | DXT3 | 5 | – |
  | DXT5 | 116 | 1 |

  The counts were taken independently with the Python reference and match.
- **JPEG.** `RenderSurface::CreateFromSourceData` reads the size with the Intel JPEG Library and rejects images with `JPGChannels != 3`. It decodes YCbCr into a BGR D3D `R8G8B8` surface (`DIBColor = IJL_BGR`, `JPGColor = IJL_YCBCR`). That is an ordinary JPEG decode, so **no red/blue swap**.
  - `docs/research/rendering.md`'s "JPEG needs an R/B swap" comes from ACViewer, whose `SwapRedAndBlueChannels` only undoes its own GDI+ BGRA to XNA RGBA byte copy.
  - All 79 JPEGs are 3-component: 73 baseline and 6 progressive. Their sizes run from 400x300 to 800x600 (one is 471x471). They are data-category-6 UI art.

## 3. Palettes, clip maps and palette overrides

- **Palette (0x04):** `u32 count, count x ARGB`. All 4,521 portal palettes have 2,048 entries, and every entry has alpha 255.
  - `Palette::InitLoad` would widen a 256-entry palette to 2,048 by repeating each entry 8 times. This is implemented but never triggered.
  - P8 indexes the first 256 entries (`Palette::get_color32`).
- **Which palette.** `CSurface::InitEnd` uses the RenderSurface's default palette.
  - It then overwrites the Surface's stored palette id with that palette's id.
  - Every textured end-of-retail Surface stores 0 there anyway (all 2,981 paletted ones were checked).
  - ACViewer also uses the texture's default palette.
- **Expansion** (`ImgTex::CopyIntoData`, `ImgTex::Combine`):
  - Non-clip-map surfaces build an **X8R8G8B8** texture: palette alpha is ignored and the texel is opaque.
  - Base1ClipMap surfaces build an **A8R8G8B8** texture: each **index below 8 becomes 0x00000000** (transparent black), and other indices keep the palette ARGB.
  - 484 of the palette clip-map surfaces have such texels.
  - ACViewer `IndexToColor` and OpenAC `FillIndex16` implement the same index-below-8 rule. The earlier prototype lacked it.
- **ObjDesc sub-palettes on the wire are in units of 8 colours.**
  - `Subpalette::UnPack` computes `offset = 8 * byte` and `numcolors = 8 * byte`, where a length byte of 0 means 256 groups (2,048 colours).
  - ACE writes `(byte)Offset, (byte)Length` (`WorldObject_Networking.SerializeModelData`) after dividing ClothingTable ranges by 8 (`Creature_Networking`).
  - ClothingTable `CloSubPaletteRange` values are already in colours. All 1,917 tables use multiples of 8 within 2,048, so ACE's division loses nothing.
  - ACE's character defaults (skin `0x00/0x18`, hair `0x18/0x08`, eyes `0x20/0x08`) are therefore colours 0–191, 192–255 and 256–319.
- **Applying an override** (`CPartArray::DoObjDescChanges`, after AnimPart changes and texture-map changes, calls `CPartArray::SetPalette(objdesc.paletteID, subs)`):
  1. `Palette::makeModifiedPalette` copies the ObjDesc base palette (2,048 entries) and applies each swap in list order: entry i comes from entry i of the swap palette (`Palette::Modify`). A single whole-palette swap (offset 0, 2,048 colours) just uses that palette.
  2. Any failure (null base, missing palette, a range past 2,048) produces no palette. Retail then leaves every part unchanged. `TryBuild` returns false with the reason.
  3. The palette goes to every part whose base palette equals the ObjDesc palette id (`CPhysicsPart::DetermineBasePal`: the first surface with a non-zero original palette id, i.e. its texture's default palette). Only palette-shifted (INDEX16/P8) surfaces accept it (`CSurface::UsePalette`). It **replaces** the texture's default palette.
- **De-duplication on receipt** (`ObjDesc::UnPack` → `ObjDesc::AddSubpalette`; `PaletteOverride.FromObjDesc` implements it):
  - A new swap is dropped if an existing whole-palette swap supersedes it (`Subpalette::supercedes`).
  - Otherwise, existing swaps with the same range are removed, or all of them if the new swap is whole-palette (`Subpalette::replaces`).
  - The new swap is then appended. The list holds at most 255.
- **PaletteSet:** `PalSet::GetPaletteID(shade) = ids[(u64)((n − 1e-6) · shade)]` for a shade in [0, 1], else the null id. `PaletteSets.GetPaletteId` implements it, matching ACE `PaletteSet.GetPaletteID`.

## 4. SurfaceTexture → RenderSurface

- **SurfaceTexture (0x05)** holds `id, dataCategory, u8 type (2), i32 n, n x RenderSurface id`, highest detail first.
  - 7,221 records with 10,245 references: 4,197 single-entry lists and 3,024 two-entry lists.
  - 2,284 first entries exist only in `client_highres.dat`. No RenderSurface id is in both dats.
- **Retail** (`ImgTex::GetSurfaceDID`):
  - With one level, it uses level 0.
  - With two levels, it uses level 1 if `Render::ShouldDropHighDetail()`, else level 0.
  - `ShouldDropHighDetail` returns `!AreOnDisk("HiFi") || Render_EnvironmentTextureDetail`.
    - "HiFi" is the high-res dat.
    - `Render_EnvironmentTextureDetail` is the global `dword_81EF9C`, registered as the "Environment texture detail level" preference. Its choices map to the values {4, 3, 2, 1, 0}, so 0 is the maximum.
    - **The built-in default is 1.** `Render::SetOverallGraphicsQuality` also sets 1 for its two highest presets (4 and 5), and "safe render settings" set 2.
    - `RenderTexture::DropUnwantedLevels` uses the same global to drop up to that many levels.
  - **So retail by default draws level 1** (the portal texture) for all 3,024 two-level SurfaceTextures. Level 0 (the high-res texture) needs both the high-res dat and Environment Texture Detail at its maximum.
- **Lifestone:**
  - It walks the list in a preferred order and looks each id up in `client_highres.dat`, then the portal. It stops at the first id found.
    - `TextureDetail.High` starts at level 0.
    - `TextureDetail.Low` starts at level 1 of a two-level list, which is retail's `ShouldDropHighDetail` branch.
  - **Deliberate divergence:** Lifestone defaults to `High`, i.e. retail with Environment Texture Detail at maximum. Pass `Low` to match retail's default.
  - **Without `client_highres.dat`** the loader behaves as `Low` whatever is requested, as retail does. `HighDetailDropped` reports this case.
  - **`MissingIds`** lists only ids the walk tried and did not find, so a non-empty list on a resolved texture means a real fallback.
    - A level-0 id skipped because highres is absent is not reported. Retail never asks for it, so reporting it would raise 2,284 false alarms on such an install.
- **Coverage with the user's dats:**
  - With `High`, all 7,221 resolve at level 0, 2,284 of them from highres.
  - All 10,245 listed references exist; none is missing.
  - With `Low`, or with a portal-only lookup simulating a missing highres dat, all 3,024 two-level lists resolve to level 1 from the portal, with no missing ids.

## 5. Surface → material and render state

- **Surface (0x08) record** (`CSurface::Serialize`): `u32 type`, then either `u32 SurfaceTexture id, u32 palette id` (if `type & 6`) or `u32 ARGB colour`, then `f32 translucency, luminosity, diffuse`. It has no id field.
- **Flag values** (retail `enum SurfaceType` = DatReaderWriter `SurfaceType`):

  | Flag | Value |
  |---|---|
  | Base1Solid | 0x1 |
  | Base1Image | 0x2 |
  | Base1ClipMap | 0x4 |
  | Translucent | 0x10 |
  | Diffuse | 0x20 |
  | Luminous | 0x40 |
  | Alpha | 0x100 |
  | InvAlpha | 0x200 |
  | Additive | 0x10000 |
  | Detail | 0x20000 |
  | Gouraud | 0x10000000 |
  | Stippled | 0x40000000 |
  | Perspective | 0x80000000 |

- **Unused flags.** Only 16 type combinations occur. None uses Diffuse, Luminous, InvAlpha, Detail, Stippled or Perspective. Gouraud is OR'd in at draw time.
- **`CSurface::InitEnd` fix-up:** a translucency above 0.0002 forces the Translucent flag. In the data the flag and the value always agree (261 surfaces).
  - The constant is the float `F_EPSILON` = 0.0002f, the same value as ACE's `PhysicsGlobals.EPSILON`. The comparison is float against float, so exactly 0.0002f does not set the flag.
  - The decompile prints it as "0.00019999999". That is how it prints floats, with 8 significant digits: the declared `const float F_EPSILON_37` prints the same way, as does `CAMERA_MOUSELOOK_LIMIT = 0.80000001`.
  - Doubles print with 16 significant digits there (e.g. `0.0174532925199433`). So the constant is not the double 0.00019999999.

Render state, from `D3DPolyRender::SetSurface` with no clip-map override and no single-pass detail texture:

1. **Base blend:**
   - Alpha: SRCALPHA / (Additive ? ONE : INVSRCALPHA).
   - Otherwise InvAlpha: INVSRCALPHA / (Additive ? ONE : SRCALPHA).
   - Otherwise Additive: ONE/ONE.
   - Otherwise ONE/ZERO, with blending off.
2. **Base1ClipMap:** turns on the alpha test (GREATEREQUAL, ref **200**) and keeps blending on. If nothing above blends, it blends ONE/INVSRCALPHA; Alpha or Additive factors set above are kept.
   - The reference is `s_ddsAlphaTestRef` = 200 unless the bound texture still carries a palette, in which case it is `s_256AlphaTestRef` = 100.
   - Palette-expanded textures never carry one, because only `ImgTex::CreateFromRenderSurface_Internal` sets `m_pPalette`. So 200 applies to every textured clip map.
   - This corrects rendering.md's "100 for palettised".
   - **Alpha-tested surfaces still blend.** The texels that pass the test are blended with the surface's factors. An `AlphaTestBlend` surface (ONE/INVSRCALPHA or SRCALPHA/INVSRCALPHA) equals a plain cut-out only when every passing texel has alpha 255. An `AlphaTestAdditive` surface never does.
     - **Palette-expanded (INDEX16/P8) clip maps** have only alpha 0 or 255 texels (checked on every one), so they are exact cut-outs.
     - **Non-paletted clip maps** can have texels with alpha in [200, 254]. On plain ClipMap surfaces (ONE/INVSRCALPHA), 90 unique textures have them: 61 DXT5, 26 A8R8G8B8, 2 DXT3 and 1 A4R4G4B4. Retail draws those texels as src × 1 + dst × (1 − a).
     - Counted per surface, 109 `AlphaTestBlend` surfaces and 1 `AlphaTestAdditive` surface have such texels. `SurfaceMaterialTests.Non_paletted_clip_maps_have_texels_that_pass_the_alpha_test_and_blend` pins these counts, which match an independent Python count.
     - **ClipMap|Additive** (ONE/ONE, 12 surfaces) and **ClipMap|Alpha|Additive** (SRCALPHA/ONE, 19 surfaces) are additive effects that also alpha-test and write depth. They are classed `AlphaTestAdditive`, not cut-outs.
     - For an `AlphaTestBlend` surface, `DecodedTexture.AnyAlphaInRange(200, 254)` tells whether its texture can be drawn as a pure alpha-scissor material.
3. **Translucent:**
   - If not yet blending, or alpha-testing, it switches to SRCALPHA/INVSRCALPHA with the alpha test off.
   - Material alpha = `(u64)((1 − t) · 255)` (`copyVert` vertex alpha). Otherwise it is 255.
   - So ClipMap|Translucent loses its alpha test, and ClipMap|Translucent|Alpha|Additive loses its additive factors.
4. **Depth write** is on when the alpha test is on or blending is off.
5. **Fog** is disabled for Additive surfaces (`SetFFFogAlphaDisabled` sets `D3DRS_FOGENABLE` false).
6. **Alpha source:** stage-0 alpha is `TEXOP_MODULATE` of texture alpha × vertex alpha.
   - Untextured surfaces bind a 1x1 solid texture whose RGB is the stored colour and whose alpha is `(1 − t) · 255`; the stored alpha byte is discarded.
   - Translucent solid surfaces therefore come out at about (1 − t)².

The Mode column is `SurfaceRenderState.BlendMode`. Both alpha-test modes still apply their blend factors (rule 2), so neither is an opaque cut-out in general.

| Type combination (count) | Src / Dst | Test | Z-write | Fog | Mode |
|---|---|---|---|---|---|
| Image (4,904), Solid (79) | ONE / ZERO | – | on | on | Opaque |
| ClipMap (640) | ONE / INVSRCALPHA | ≥200 | on | on | AlphaTestBlend |
| ClipMap\|Alpha (22) | SRCALPHA / INVSRCALPHA | ≥200 | on | on | AlphaTestBlend |
| ClipMap\|Alpha\|Additive (19) | SRCALPHA / ONE | ≥200 | on | off | AlphaTestAdditive |
| ClipMap\|Additive (12) | ONE / ONE | ≥200 | on | off | AlphaTestAdditive |
| Image\|Alpha\|Additive (161) | SRCALPHA / ONE | – | off | off | Additive |
| Image\|Additive (7) | ONE / ONE | – | off | off | Additive |
| Image\|Translucent\|Alpha\|Additive (2) | SRCALPHA / ONE, alpha 1−t | – | off | off | Additive |
| Solid\|Translucent\|Additive (1) | ONE / ONE, alpha 1−t | – | off | off | Additive |
| Image\|Translucent (156), Solid\|Translucent (73) | SRCALPHA / INVSRCALPHA, alpha 1−t | – | off | on | AlphaBlend |
| Image\|Alpha (47) | SRCALPHA / INVSRCALPHA | – | off | on | AlphaBlend |
| ClipMap\|Translucent (27) | SRCALPHA / INVSRCALPHA, alpha 1−t | – | off | on | AlphaBlend |
| Image\|Translucent\|Alpha (1) | SRCALPHA / INVSRCALPHA, alpha 1−t | – | off | on | AlphaBlend |
| ClipMap\|Translucent\|Alpha\|Additive (1) | SRCALPHA / INVSRCALPHA, alpha 1−t | – | off | off | AlphaBlend |

**Not surface properties.** The model and cell builders own these:
- **Culling and two-sidedness** belong to the polygon. Retail always culls CW (`RenderMeshSubset`; `override_cull_state` is never set) and draws SidesType 1 polygons unculled.
- **Texture addressing** (WRAP or CLAMP, from the polygons' stippling byte) follows one of two retail rules, depending on the draw path. **Models and cells tracks: use the mesh rule.**
  - **Mesh path: GfxObj and EnvCell.** Retail draws these through `D3DPolyRender::ConstructMesh`, `DrawMesh` and `RenderMeshSubset`. The rule is per *surface subset*:
    - While building the mesh, `ConstructMesh` ORs `stippling > 0` into `isStippledOrAlphaedMask[pos_surface]` for every polygon.
    - `RenderMeshSubset` passes `mask & 1` to `SetSurface` as `stippled`, which selects WRAP when set and CLAMP otherwise.
    - So a subset WRAPs if **any** polygon whose *pos* surface is that surface has a non-zero stippling byte. That includes NoPos (0x4), not just bit 0x1.
    - A surface used only as a neg surface never gets the bit, so it CLAMPs.
    - Mixed subsets do occur. In a review sample of every 20th GfxObj (42,742 polygons), stippling is 0 on 30,945, 1 on 11,550 and 4 on 247.
  - **Per-polygon path.** `D3DPolyRender::SetSurface(CPolygon*, side, …)` and `DrawPolyInternal` select WRAP when the side's bit is set (pos 0x1, neg 0x2), else CLAMP. Retail uses this path only for `ACRender::landPolyDraw` (terrain) and the deferred poly list (`D3DPolyRender::polyListFinishInternal`).
  - The decompile doc notes that the D3D build has no stipple path, so the byte only chooses the address mode.
- **Part translucency** (PhysicsDesc → `CMaterial` alpha) forces SRCALPHA/INVSRCALPHA with no test and no z-write, unless the subset already blends without a test.
- **Lighting:** material emissive = luminosity (grey); diffuse = sunlight colour × diffuse.
  - **Erratum (wave 2, core):** the "diffuse" half does not reach the D3D lighting. `D3DPolyRender::SetSurface` writes `Surface.diffuse` (times the sunlight colour) only into the global `Render::diffuse` (acclient.c:454458–454467), which no D3D code reads. Mesh vertices carry white diffuse with alpha (1 − t) × 255 (`copyVert`), and `RenderMeshSubset` keeps the colour sources at `FromVertex`, so the material diffuse and ambient are white. The emissive half holds: `RenderMeshSubset` sets emissive = luminosity when it is > 0. See `docs/render-contract.md` §2.1.
- **Draw order:** blended and alpha-tested subsets go to two unsorted deferred lists (`AddMeshToAlphaList`, `s_AlphaDelayMask` = 14).

## 6. Derived-data cache: not implemented

We measured decode cost first. All runs used a Release build on a single thread with the OS file cache warm, on the development machine.

| Workload | Read from dat | Decode Native | Decode Rgba8 |
|---|---:|---:|---:|
| Whole portal: 20,684 RenderSurfaces, 606 MB | 0.69 s | 0.25 s (835 MB out) | 0.60 s (1.45 GB out) |
| Whole highres: 2,294 RenderSurfaces, 130 MB | 0.17 s | 0.14 s | 0.54 s |

On a sample of every 10th portal RenderSurface (2,069 textures):

| Path | Time per texture |
|---|---:|
| Dat read + decode, Native | 62 µs |
| Dat read + decode, Rgba8 | 118 µs |
| Read the decoded bytes back from per-texture files (warm) | 32–38 µs |
| Write those files | about 250 µs |

**Why we did not build a cache:**
- **The saving is too small.** A texture cache would save at most 30–85 µs per texture. That is under 0.2 s for a town's worth of textures, spread over worker threads.
- **The costs are larger.** About 1 GB of disk for the Native set, a quarter of a millisecond per texture to populate it (plus antivirus scanning of about 23,000 new files), and invalidation logic.
- **The dat is already the compact form.** The source data (DXT, INDEX16) is smaller than any decoded form, so a cold-disk read favours the dat too.

**When to revisit:** add a cache only for work that is expensive and engine-side. Candidates are BC compression of the 12,984 A8R8G8B8 textures, mip generation, or terrain atlases. Those would be Godot-format resources, keyed as the M1 doc says (`DatIterations.CacheKey` plus a decoder version, under `<repo>\cache\`).

## 7. Notes for wave 2

- **DXT1 alpha in Godot.** If Godot maps `Image.FORMAT_DXT1` to BC1 *RGB*, punch-through texels become opaque black. Retail's D3D9 DXT1 gives them alpha 0.
  - **Verified (game core, `--engine-checks`, Godot 4.7.2 on Vulkan and D3D12): it does.** The RenderingDevice format is `BC1_RGB_UNORM_BLOCK`, and a punch-through block renders opaque black.
  - The render path therefore uploads such textures as RGBA8 wherever alpha matters (render-contract.md §3).
  - This matters for the 278 DXT1 records with `HasAlpha`, but only on blended or alpha-tested surfaces (`RenderState.IsDeferred`).
  - Use `Rgba8` for those, or verify BC1 RGBA support.
- **Mipmaps.** `ImgTex::CreateD3DTexture` computes `min(4, log2(max side) + 1)` mip levels, sizes the texture for that many, and fills them with `D3DXFilterTexture(..., D3DX_FILTER_BOX | D3DX_FILTER_MIRROR)` (0x70005).
  - The decompiled `CreateTexture` arguments are garbled, so "at most 4 levels" is very likely but not proven.
  - If it holds, retail never minifies below 1/8 resolution. Distant textures shimmer more in retail than with a full Godot mip chain.
- **Retail quirk: clip-map state can leak between surfaces.** Retail caches palette-expanded textures by (palette id, image id) only (`ImgTex::CreateCombinedTexture`, `texture_table`).
  - 59 paletted RenderSurfaces (74 SurfaceTextures) are used by both clip-map and non-clip-map surfaces. In retail, whichever surface loads first decides whether indices below 8 are transparent for both.
  - Lifestone decodes per surface. Key caches by (RenderSurface, palette, clip-map) and accept this deliberate, deterministic divergence.
- **Alpha-tested surfaces blend too** (§5, rule 2). Do not map `AlphaTestBlend` or `AlphaTestAdditive` to an opaque alpha-scissor material by default.
  - For `AlphaTestBlend`, a scissor-only material is exact only when `DecodedTexture.AnyAlphaInRange(200, 254)` is false. That holds for every palette-expanded clip map. `AlphaTestAdditive` is always additive.
  - Otherwise, keep the alpha test (≥ 200/255) and depth write, and blend:
    - ONE/INVSRCALPHA is premultiplied-style "over".
    - SRCALPHA/INVSRCALPHA is ordinary alpha blending.
    - ONE/ONE and SRCALPHA/ONE are additive.
- **Texture addressing** for GfxObj and EnvCell meshes is per surface subset, not per polygon (§5, "Not surface properties").
- **Translucent solid surfaces** multiply two alphas (§5, rule 6).
- **Additive surfaces** are drawn without fog.

## 8. Open questions

- **A8 sampling colour.** How retail samples the colour of A8 textures (D3D9 alpha-only formats) is unverified. No Surface uses A8.
- **DXT CPU rounding.** CPU DXT rounding differs from GPU decoders by at most a step. It only matters for the RGBA8 path.
- **Fixed-function lighting** with `burnedInStaticLights` (vertex-colour versus material diffuse source) and how it interacts with material alpha belong to the lighting work in wave 2.

## 9. Decoding without garbage (M1 wave-3 performance pass)

The wave-3 audit found that garbage-collection pauses were the only hitches Lifestone caused itself, and that texture decoding produced most of the garbage: 1.1 GB in the first 3 s of start-up, mostly large-object-heap arrays. Every texture cost a copy of its record's pixels, an RGBA8 expansion, for DXT a second read of the record plus a mip-chain array and per-level arrays, and the same texture was decoded again by every landblock prepare that ran at the same time.

- **Raw record reads** (`RenderSurfaceRecord`). DatReaderWriter's `RenderSurface.Unpack` allocates a new pixel array per record and fills it one byte at a time, inside the dat lock every worker waits on. `TextureLoader` now reads the record's file bytes (`DatDatabase.TryGetFileBytes` into a span) into a buffer rented from `ArrayPool<byte>.Shared` and parses the header itself: u32 id, u32 data category, i32 width, i32 height, u32 format, i32 byte count, the pixel bytes, then the default palette id for INDEX16/P8 only. That is the layout DatReaderWriter's generated `Unpack` and ACE's `Texture.Unpack` read. A compressed file entry (none exists in the end-of-retail dats) goes through DatReaderWriter's inflate and is then copied into a pooled buffer.
  - `RenderSurfaceRecordTests` reads all 22,978 RenderSurfaces of the user's portal and high-res dats both ways and compares fields, decoded bytes, `HasAlpha` and size in both decode modes.
- **Buffer-taking API.** `TextureLoader.DecodeRenderSurfacePooled` returns a `PooledTexture`: DXT, JPEG and Alpha8 are a slice of the pooled record, an RGBA8 expansion goes to a rented buffer of its own. `DecodeRenderSurface`/`DecodeSurface` keep their API and now allocate only their result array.
- **No second read for DXT mips.** `TextureCache` decodes the RGBA8 it needs for the mip levels and the alpha checks from the same blocks (`BlockCompression.DecodeToRgba8`, which is what the RGBA8 decode mode does for DXT). `MipChain.BuildBlockChain(blocks, rgbaScratch, w, h, format, destination)` box-filters in place in the scratch buffer and encodes into a pooled destination; its bytes equal the allocating form's (`MipChainTests`). Godot copies texels from spans (`Image.CreateFromData(..., ReadOnlySpan<byte>)`, `LoadJpgFromBuffer(ReadOnlySpan<byte>)`), so nothing of the decode outlives the call.
- **Shared decodes** (`TextureCache.PrepareShared` over the engine-agnostic `Lifestone.Client.Caching.SharedDecodes`, `SharedDecodesTests`). Requests are keyed by what changes the decode: RenderSurface, palette key, clip-map flag, whether the surface uses alpha (DXT1 punch-through expansion), DXT policy. A request that arrives while the same decode runs waits for it; one that arrives while some lease still holds the image takes a lease on it. The table holds images weakly once decoded, so a lease that is never disposed still lets the GC free its image, as before.
- **`TextureInfo.PassingTexelsBlend`** no longer depends on the requesting material: it says whether the texels have alpha in [200, 254], and the shader variant applies it only to alpha-tested surfaces (`SurfaceShaderVariant.For`), as before. That makes one decode valid for every surface that uses the texture.
- **Pooled buffers stay in the pool.** `ArrayPool<byte>.Shared` keeps returned arrays per thread and per core and trims them after 30–60 s without use. That is the cost of pooling; game-core.md §7 has the measured heap plateau.
- **Terrain atlas.** `TerrainAtlasBuilder.Build` no longer decodes its 38 layers on up to 30 threads (`parallelDecode`, off by default): each thread that read a 512² layer left 2 MB read buffers in `ArrayPool<byte>.Shared`, which kept about 60 MB alive for minutes after the upload (80–82 MB against 5 MB sequentially in an isolated test; terrain-renderer.md §1). The mip chains are still built in parallel.
