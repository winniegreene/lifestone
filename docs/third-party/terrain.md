# Third-party sources: terrain blending track

This track covers `src/Lifestone.Data/Terrain/Blending/`, `tests/Lifestone.Data.Tests/Terrain/` and `docs/terrain-shader.md`.

## Code ported into the repository

| Source | Licence | Pinned at | Used in | How |
|---|---|---|---|---|
| ACEmulator/ACViewer `ACViewer/Physics/Common/TexMerge.cs` (`GetTerrain`, `BuildTCodes`, `GetRoadCode`, `FindTerrainAlpha`, `FindRoadAlpha`, `GetTerrainTex`) | GPL-3.0 | c5c54fb3318cd956a330a920c105a0109aaeb3fd | `tests/Lifestone.Data.Tests/Terrain/Oracle/AcViewerTexMergeOracle.cs` | Ported as an independent **test oracle**. The arithmetic is kept verbatim, including the double literal 2.3283064e-10. It returns indices instead of `TerrainTex` objects. |
| ACEmulator/ACE `Source/ACE.Server/Physics/Common/LandblockStruct.cs` (`GetCellRotation`, `GetPalCode`) | AGPL-3.0 | 95ef426d412459ff619a12cc18f82bcf72c1c2a8 (v1.78.4816) | same oracle file | Ported as the oracle's palette-code derivation. |
| eriknihlen/OpenAC `src/AcDream.Core/Terrain/TerrainBlending.cs` (`PseudoRandomIndex`, `GetRoadCodes`, `RotateTerrainCode`) | MIT | 1ee5a08581a7f4b0b22effdf911171a76cf4391c | `src/Lifestone.Data/Terrain/Blending/TerrainTexMerge.cs` (product) | **API shape only.** The product keeps OpenAC's signatures: `PseudoRandomIndex(pal, count)` with its `index >= count → 0` guard, `GetRoadCodes` returning `(road0, road1, allRoad)` with the same mask switch, and `RotateTerrainCode` (renamed `RotateCode`). The arithmetic is re-expressed from the retail decompile, and the pick is the exact `(x·n) >> 32`, not OpenAC's double literal. An empty list throws instead of returning 0. The rest of the class is written from the decompile. |
| eriknihlen/OpenAC `src/AcDream.Core/Terrain/LandblockMesh.cs` (`BuildRetailVertexNormals`, `AccumulateFaceNormal`) and `TerrainBlending.cs` (`CalculateSplitDirection`) | MIT | 1ee5a08581a7f4b0b22effdf911171a76cf4391c | `tests/Lifestone.Data.Tests/Terrain/Oracle/OpenAcVertexNormalsOracle.cs` | Ported as an independent **test oracle** for the lighting normals. The geometry, loop order and float arithmetic are kept. It reads a Lifestone `LandblockTerrain` and returns a flat array. |

GPL-3.0 code can be combined with Lifestone's AGPL-3.0 licence (GPL-3.0 §13).

The OpenAC parts are used under its MIT licence, reproduced here as that licence requires:

```text
MIT License

Copyright (c) 2026 Erik Nihlén and OpenAC contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## References, not copied

| Source | Licence | Pinned at | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, Hex-Rays decompile of acclient 11.4186 (`PORTAL/cregion/CTexMerge.c`, `CLandSurf.c`; `PORTAL/ccell/CLandBlockStruct.c`, `CLandBlock.c`; `PORTAL/image/ImgTex.c`; `PORTAL/acrender/ACRender.c`; full `acclient.c`), and docs 06 and 13 | MIT label; the code is Turbine IP | fetched 2026-10-10 | The authority for every algorithm in `Terrain/Blending`. Each behaviour is re-expressed and cited by function name and `acclient.c` line. **No decompiled code is in the repository.** |
| eriknihlen/OpenAC `src/AcDream.Core/Terrain/TerrainBlending.cs` (`BuildSurface` and the rest of the file), `src/AcDream.App/Rendering/Shaders/terrain_modern.frag` | MIT | 1ee5a08581a7f4b0b22effdf911171a76cf4391c | Cross-check of `TerrainTexMerge`, read line by line. The parts whose API shape was kept are listed above. Its `maskBlend3` overlay approximation is deliberately **not** used: Lifestone uses retail's sequential merge. `Core/Rendering/Wb/TerrainUtils` is not used. |
| ACEmulator/ACViewer `ACViewer/Physics/Common/LandSurf.cs`, `LandblockStruct.cs` | GPL-3.0 | c5c54fb | Confirms that a TexMerge region selects `pal_code[0]` with rotation 0. |
| Chorizite/DatReaderWriter | MIT | package 2.1.9 (source checked at a40acb4) | The existing dependency. It provides the `Region` → `TexMerge` / `TMTerrainDesc` / `TerrainAlphaMap` / `RoadAlphaMap` types. |

## Algorithms re-expressed from the retail decompile (product code)

| Lifestone | Retail function (acclient.c line, 11.4186) |
|---|---|
| `TerrainPalCode.ForCell`, `IsSingleTexture` | `CLandBlockStruct::GetCellRotation` (354223), `LandSurf::SelectTerrain` (304328) |
| `TerrainTexMerge.GetTerrain` | `TexMerge::GetTerrain` (305246) |
| `TerrainTexMerge.GetRoadCodes` | `TexMerge::GetRoadCode` (304565) |
| `TerrainTexMerge.FindTerrainAlpha`, `PseudoRandomIndex`, `RotateCode` | `TexMerge::FindTerrainAlpha` (304756) |
| `TerrainTexMerge.FindRoadAlpha` | `TexMerge::FindRoadAlpha` (304697) |
| `TerrainTexMerge.ComputeCellBlend` (order, failure handling) | `TexMerge::FillTempTexBuffer` (305909) |
| `TerrainAtlasManifest.LayerForTerrainType` | `TexMerge::GetTerrainTex` (304870) |
| `TerrainAtlasManifest.DetailTextures`, `TerrainDetailRetailUse` | `LScape::SetDetailTexturing` (308209), `TexMerge::GetDetailTex` (304939) / `GetDetailTiling` (304957), and its callers `SmartBox::SetDetailTexturing` (143221), `SmartBox::ResetDetailTexturing` (144205), `LScape::ChangeRegion` (308276) and `Render::UpdateFromPreferences` (380924); the preference default (45603) and presets (`Render::SetOverallGraphicsQuality`, 378743) |
| `TerrainCompositor.ComposeRetailTexture`, `Merge`, `RotateAlphaUv` | `ImgTex::TileCSI` (365513), `ImgTex::MergeTexture` (365632), `ImgTex::CopyCSI` (366393) |
| cell UVs (`TerrainBlendVertices`) | `CLandBlock::init` (351988, `land_uvs`), `CLandBlockStruct::ConstructUVs` (354677) |
| `TerrainVertexLighting` | `CLandBlockStruct::calc_lighting` (353713), `CPolygon::make_plane` (359628), `CLandBlockStruct::ConstructVertices` (354621) / `ConstructPolygons` (354001), `ACRender::landPolyDraw` (719994) |
| `TerrainLightingEnvironment` | `LScape::set_landscape_lighting` (307003) |
| `TerrainLightingEnvironment.FromSkyLighting`, `TerrainVertexLighting.MinAmbientLevel` | `LScape::UseTime` (307261), `LScape::set_landscape_lighting` (307024), `LScape::min_ambient = LSCAPE_LIGHT_MINIMUM` (783184) |

## Algorithms re-expressed from the retail decompile (test oracles)

| Lifestone | Retail function (acclient.c line, 11.4186) |
|---|---|
| `tests/Lifestone.Data.Tests/Terrain/Oracle/RetailImgTexOracle.cs` | `ImgTex::TileCSI` (365513) and the scale-up branch of `ImgTex::MergeTexture` (365632), driven as in `TexMerge::FillTempTexBuffer` (305909). The oracle restates the memory walk: a per-rotation alpha cursor start, x/y strides and replication countdowns. |
