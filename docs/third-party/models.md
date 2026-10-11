# Third-party sources: models track

Track: M1 wave 1, models (`src/Lifestone.Data/Models/`, tests in `tests/Lifestone.Data.Tests/Models/`).

No third-party file was copied or ported verbatim into the repo. Every file in `Models/` is original code. The
behaviour it re-expresses comes from the sources below, and each algorithm or constant is cited in a code comment.

| Source | Licence | Pinned at | Used for |
|---|---|---|---|
| Retail client decompile, Mag-nus/acclient-AI-RE, `2013-09 11.4186` (PDB-named Hex-Rays output; per its doc 13 the rendering core is unchanged in 11.6096, the user's exe) | Turbine IP, reference only | fetched 2026-10-10 via `gh api` into session scratch, not into the repo | The authority for every behaviour below. No decompiled code is in the repo. Functions read: `D3DPolyRender::ConstructMesh`, `copyVert`, `D3DPolyRender::SetSurface` (both overloads), `RenderMeshSubset`, `DrawMesh` (both overloads), `DrawPolyInternal`; `RenderDeviceD3D::DrawBuilding`, `DrawEnvCell`, `DrawMeshInternal`; `CPolygon::UnPack`, `CPolygon::make_plane`; `CGfxObj::Serialize`, `CGfxObj::init_end`; `BSPTREE::GetSphere`, `BSPLEAF::UnPackLeaf`; `CSurface::Serialize`; `GfxObjDegradeInfo::UnPack`, `get_degrade`, `get_max_degrade_distance`; `CPhysicsPart::LoadGfxObjArray`, `SetGfxObjArray`, `SetPart`, `makePhysicsPart`, `Draw`, `UpdateViewerDistance` (both), `calc_draw_frame`, `Always2D`, `GetMaxDegradeDistance`; `CPhysicsObj::UpdateViewerDistance`; `Render::SetDegradeLevelInternal`, `Render::CalcDegLevel`, the Render preference registration; `CSetup::UnPack`, `CCylSphere::UnPack`, `CSphere::UnPack`, `CSetup::makeSimpleSetup`, the `CSetup` constructor; `CPartArray::InitParts`, `UpdateParts`, `SetPlacementFrame`, `CreateSetup`, `InitDefaults`, `InitLights`, `SetScaleInternal`, `GetSortingSphere`, `GetSelectionSphere`; `Frame::combine`, `Frame::cache`, `Frame::set_vector_heading`, `Frame::rotate_around_axis_to_vector`; `LIGHTLIST::set_frame`; `calc_point_light`. Globals' defaults (`skipNoTexture = 1`, `Render::s_rDegradeDistance = 50`, `auto_update_deg_mul = true`, `force_level = -1`, `static_light_factor = 1.3`) come from its `acclient_data.h`. Struct layouts (`CPolygon`, `CSWVertex`, `LIGHTINFO`, `GfxObjInfo`, `CPhysicsPart`, `CPartArray`) come from its `acclient.h`. Its `docs/06-rendering.md` §7 and §10 were used as a reading guide and cross-checked against the code. |
| ACEmulator/ACE (`vendor-eval/ACE`) | AGPL-3.0 | v1.78.4816, 95ef426d | Cross-check of the posing rules: `Source/ACE.Server/Physics/PartArray.cs` (`UpdateParts`, `SetPlacementFrame` with Resting falling back to Default), `Physics/Animation/AFrame.cs` (`Combine`), `Physics/PhysicsPart.cs` (ACE omits degrades). Dat layouts: `Source/ACE.DatLoader/FileTypes/{GfxObj,SetupModel,GfxObjDegradeInfo}.cs`, `Entity/{Polygon,Frame}.cs`. |
| ACEmulator/ACViewer (`vendor-eval/ACViewer`, cloned for this track) | GPL-3.0 | c5c54fb | Cross-check only. `Model/Polygon.cs` uses the same fan, `(v0, v[i-1], v[i])`. `Model/Setup.cs` uses Resting, else Default, and the simple setup of a bare GfxObj. ACViewer deviates from retail: GfxObjs are drawn with `CullMode.None` (`Render/Buffer.cs`) and only the positive side is emitted. `Render/InstanceBatch.cs` skips `Stippling == NoPos` polygons, but only for EnvCells. None of this is reproduced. |
| eriknihlen/OpenAC (`vendor-eval/OpenAC`) | MIT | 1ee5a08 | Cross-check only. `src/AcDream.Core/Meshing/{GfxObjMesh,GfxObjDegradeResolver,SetupMesh,SetupPartTransforms}.cs`. It has deviations that were deliberately **not** copied: (1) it emits no back faces for `sides_type 1` and relies on disabled culling; (2) it treats the `Negative`/`Both` stippling bits as a request for a negative side; (3) `SetupPartTransforms` ignores `DefaultScale`, and `SetupMesh` does not scale part origins by the object scale; (4) when neither Resting nor Default exists it falls back to the first placement, whereas retail sets no frame; (5) for LOD it only ever picks `Degrades[0]` ("close"), with no distance selection. |
| Chorizite/DatReaderWriter (`vendor-eval/DatReaderWriter`, cloned for this track; the package itself is the lead's dependency) | MIT | source a40acb4; package 2.1.9 | Source read to confirm its decoding. `DatBinReader.ReadQuaternion` reads w, x, y, z. `Polygon` reads negative UVs only for `sides_type 2` without `NoNeg`. `Setup` reads 48-byte lights keyed by `LIGHTINFO::type`. `GfxObjDegradeInfo` and `GfxObjInfo` were also checked. The package's decoding is cross-checked byte-exactly on every record by the independent test reader (below). |
| An earlier private prototype (same author) | AGPL-3.0 | read only | Its known bugs, used as regression targets: the 44-byte Setup light skip (`Setup.cs:130`), which misreads the default ids of all 608 lit Setups, and the strict BSP skipper. |

## Test-only material

- `tests/Lifestone.Data.Tests/Models/RawModelReader.cs` is an independent C# reader for GfxObj, Setup and
  GfxObjDegradeInfo. It shares no code with DatReaderWriter. It transcribes the research python reader
  (`acparse.py`, written in an earlier session of this project, which consumed every end-of-retail record
  byte-exactly) and the layouts in `docs/research/dat-format.md` §4, re-checked against the retail functions above.
- Expected per-object ids and counts in the tests (sub-mesh vertex and triangle counts, Setup part ids, parent
  indices and flags, degrade level ids, whole-dat totals) were computed with read-only python scripts over the user's
  dats. These are session scratch `models/survey.py`, `survey2.py`, `dump_setup.py` and `dump_degrade.py`, built on
  `acdat.py`/`acparse.py`. The scripts are not in the repo.
- The tests embed ids, flags, counts and synthetic values only. Decoded values (part and light frames, light colours,
  intensities and falloffs, spheres, cylinder-spheres, Setup sizes, LOD switch distances, vertex positions, normals
  and UVs) are never written into the repo: every test that checks them re-reads the record from the user's dats at
  test time with `RawModelReader` and compares against that. The only decoded values asserted literally are
  documented properties: the empty last LOD level at FLT_MAX, the anchor table's all-zero distances, and the MSVC
  debug fill 0xCDCDCDCD in every light's cone angle.
