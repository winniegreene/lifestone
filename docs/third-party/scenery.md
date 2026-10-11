# Scenery track: sources and provenance

Code: `src/Lifestone.Data/World/Scenery/`. Tests: `tests/Lifestone.Data.Tests/Scenery/`.

## Sources

| Source | Version | Licence | How it is used |
|---|---|---|---|
| ACEmulator/ACE | v1.78.4816, `95ef426d` | AGPL-3.0 | **Ported.** `Source/ACE.Server/Physics/Common/{Landblock.cs (get_land_scenes, OnRoad, GetRoad, init_buildings), LandblockStruct.cs (ConstructVertices, ConstructPolygons, AddPolygon), LandCell.cs (find_terrain_poly), ObjectDesc.cs, LandDefs.cs}`, `Physics/Polygon.cs (make_plane, point_in_poly2D)`, `Physics/Extensions/PlaneExtensions.cs (set_height)`. Each ported file carries a header. The same ACE files, plus `PhysicsObj.cs (obj_within_block)`, `PartArray.cs`, `Setup.cs (MakeSimpleSetup)`, `Collision/GfxObj.cs`, `Animation/AFrame.cs` and `Extensions/{Vector3Extensions,FloatExtensions}.cs`, are transcribed with ACE's own arithmetic into the **test-only** oracle `AceSceneryOracle.cs`. |
| Retail client decompile (Mag-nus/acclient-AI-RE) | 11.4186 (PDB-named) | reference only | Behaviour was re-expressed from `CLandBlock::get_land_scenes`, `CLandBlock::on_road`, `CLandBlock::init_buildings`, `CLandBlock::InitLoad`, `LScape::grab_visible_cells`, `ObjectDesc::{Place, GetObjFrame, ObjAlign, ScaleObj, CheckSlope, UnPack}`, `Plane::set_height`, `CLandBlockStruct::{ConstructVertices, ConstructPolygons, AddPolygon}`, `CLandCell::find_terrain_poly`, `CPolygon::{make_plane, point_in_poly2D}`, `CPhysicsObj::{makeObject, InitPartArrayObject, CacheHasPhysicsBSP, obj_within_block}`, `CPartArray::{CreateMesh, CreateSetup, InitParts, GetSortingSphere}`, `CSetup::makeSimpleSetup`, `CPhysicsPart::LoadGfxObjArray`, `CGfxObj::Serialize`, `CTerrainDesc::{NumSceneType, SceneCount, GetScene}`, `CRegionDesc::UnPack`, `Frame::{cache, set_rotate, euler_set_rotate, set_vector_heading, set_heading, UnPack, IsValid}`, `AC1Legacy::Vector3::{get_heading, normalize_check_small}`, `LandDefs::{adjust_to_outside, get_outside_lcoord, within_block}`, `ClientMain::ConfigureFPU`. **No decompiled code is in the repo**; functions are cited by name. |
| The user's `acclient.exe` 11.6096 | MD5 116d9a66… | reference only | Read-only byte inspection (never executed) of the x87 code of the functions above, located with `symbol_transplant_11.6096.tsv` from the same Mag-nus repository, to confirm operand order and float/double rounding points. Findings are cited by address in code comments; no bytes are in the repo. |
| eriknihlen/OpenAC | `1ee5a08` | MIT | Reference only (its four deviations are fixed, see below). No code taken. |

## Retail facts established for this track

- **The hash scale is exactly 2^-32.** The binary holds the float constant 0x2F800000 at VA 0x7C6F10, used by 14 `fmul dword ptr` instructions; no double 2.3283064e-10 exists in the image. ACE uses the double literal (1.6e-8 smaller).
- **x87 runs at 53-bit precision.** `ClientMain::ConfigureFPU` sets `_PC_53` and round-to-nearest, and the D3D9 device is created with `D3DCREATE_FPU_PRESERVE` (behaviour flags `|= 0x106`). Register arithmetic is therefore IEEE double; we round to float exactly where the x87 code stores to 4-byte slots.
- **Heading convention.** The machine code computes `atan2(y, x)` and Hex-Rays prints the two operands swapped. In `Vector3::get_heading` (0x45771F) and `Frame::set_vector_heading` (0x536B1B) the code loads y and then x before `fpatan` (no `fxch`), so ST1 = y and ST0 = x. `Frame::get_heading` (0x5364A0), which scenery does not use, computes x and then y and swaps them with `fxch`. Heading 0 = +Y (north), clockwise; `set_heading(d)` = rotation of -d about +Z. This matches ACE.
- **`CLandBlock::on_road`.** All 16 cases were verified against the x87 code at 0x530D30-0x530F79, including the six diagonal ones (|dx-dy|, |dx+dy-24|, dx+dy, 24+dx-dy, 24-dx+dy, 48-dx-dy), the strict comparisons and the evaluation order. They are identical to ACE. dx stays in a register and dy is stored as float, which is exact (y - 24 * iy is representable in float).
- **Evaluation order.** Where the order of x87 additions can change a rounding (`CPolygon::point_in_poly2D`, `CPolygon::make_plane`, `Frame::cache`, the cylinder-sphere transform in `obj_within_block`), the order was read from the 11.6096 machine code. It is kept and documented next to the code, which is written independently. For `point_in_poly2D` the order matters only within 2^-18 of a landblock's west or south edge (see `TerrainSamplerTests.Retail_evaluation_order_decides_a_point_a_femtometre_off_a_diagonal`).
- **`set_heading` pitches about X.** It normalises `(sin d, cos d, M[5])` (M[5] = z of the base forward axis) and calls `euler_set_rotate(asin(n.z), 0, -heading, order 0)`, i.e. Shoemake EulOrdXYZs: R = Rz * Rx.
- **Building exclusion is per cell, not per footprint.** `CLandBlock::init_buildings` adds each building to the one land cell containing its frame origin (after `adjust_to_outside`; buildings whose origin lies outside the block, or whose model fails to load, are dropped). `get_land_scenes` rejects a candidate when its cell `has_building`. `LScape::grab_visible_cells` runs `init_buildings` for all blocks before `init_static_objs` (which calls `get_land_scenes`).
- **z is the terrain plane height.** `Plane::set_height` overwrites the BaseLoc z; `adjust_scene_obj_height` re-applies the same snap on re-init only.
- **`obj_within_block`** tests, before scaling: the sorting sphere for objects with a physics BSP, else every Setup cylinder-sphere, else the sorting sphere when the Setup has spheres, else the origin; X/Y only, against [r, 192 - r).
- **Object creation** fails (the candidate is skipped) for a missing GfxObj/Setup, a Setup with no parts, a part whose level-0 GfxObj is missing, or a non-0x01/0x02 type byte. At end of retail no scenery or building object fails.

## ACE deviations (fixed here; measured over all 65,025 landblocks, 5,912,342 candidates, 3,786,622 placements)

| # | ACE | Retail / Lifestone | Measured effect |
|---|---|---|---|
| 1 | double 2.3283064e-10 | float 2^-32 | no decision changes anywhere; x/y differ by about 1 ulp |
| 2 | float `point_in_poly2D` | x87 double in retail's evaluation order (exact for terrain except within 2^-18 of the west/south block edges) | 2 decisions: 0x7A8B and 0x7A8C, points 3.8e-6 off a cell diagonal; ACE picks the flatter triangle and places them, retail rejects on slope |
| 3 | float `make_plane` / `set_height` | double with retail's float stores | z within 0.43 mm (370 placements over 0.1 mm) |
| 4 | `set_heading`: z = M23 + M13, `CreateFromYawPitchRoll(xRot, 0, zRot)` (tilt about Y) | z = M[5], Euler X pitch | 25,722 of the 25,727 placements of the 8 tilted ObjectDescs (scenes 0x1200024A, 0x1200029D, 0x120002A3, 0x120002B2, 0x120002B3) differ from ACE by more than 1e-5. All 25,727 are checked against the retail `set_heading` invariants (`SceneryTiltedOrientationTests`) |
| 5 | `ScaleObj` divides max/min in float | double quotient | within 1 float ulp |
| 6 | `AdjustToOutside` snaps \|v\| < 0.0002 to 0 in the placed position | snaps only a copy | 1 placement |
| 7 | GfxObj physics BSP from `PhysicsPolygons.Count`, degrades ignored | `HasPhysics` flag, level-0 degrade GfxObj | no effect at end of retail |
| 8 | no failure handling in `makeObject` / `makeBuilding` | skip, as retail | no effect at end of retail |

## OpenAC deviations (foundation-openac.md §3c), all fixed

1. Slope test with the wrong split: the slope uses `TerrainSampler`, built on `Lifestone.Data.Terrain.TerrainSplit` and checked against `TerrainMeshBuilder` (`TerrainSamplerTests`).
2. z = ground + BaseLoc.Z: z is the plane height (`SceneryWorldSweepTests` asserts it for all placements, 79,191 of which come from ObjectDescs with BaseLoc.Z != 0).
3. No `obj_within_block`: implemented (3,619 rejections world-wide, 39 in the Holtburg region).
4. Quadrant in float: computed in double (`SceneryRandomTests` shows float would never flip a quadrant on Dereth's grid, so this one has no visible effect).

## Known limits

- Transcendentals (`fsin`/`fcos`/`fpatan` and the msvcr `_CIasin`/`_CIpow` the client imports) may differ from .NET `Math` in the last double ulp. Every such result is rounded to float before use, so a difference needs a value within about 1e-16 of a float rounding boundary.
- Two retail paths are refused loudly rather than guessed: a scenery id with type byte 0 (`makeAnimObject`), and a GfxObj with an empty degrade table. Neither occurs at end of retail.
