# Third-party sources: terrain renderer track

This track covers:
- `src/Lifestone.Game/scripts/World/Terrain/`;
- `src/Lifestone.Game/shaders/terrain/terrain.gdshader`;
- `src/Lifestone.Game/scenes/TerrainPreview.tscn`;
- `src/Lifestone.Data/Terrain/Blending/{TerrainAtlasBuilder,TerrainAtlasPixels,TerrainMipChain,TerrainRenderMesh,TerrainSeamlessNormals}.cs`;
- `tests/Lifestone.Data.Tests/TerrainRender/`;
- `tools/terrain-golden.ps1`;
- `docs/terrain-renderer.md`.

## Code ported into the repository

None. Every file is written for Lifestone.

- **The shader** implements the in-repo spec, [docs/terrain-shader.md](../terrain-shader.md) §5, including its own reference sketch.
- **The data-layer helpers** build on the wave-1 terrain and textures code. Its sources are listed in [terrain.md](terrain.md) and [textures.md](textures.md).

## References, not copied

| Source | Licence | Pinned at | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, Hex-Rays decompile of acclient 11.4186 (`acclient.c`) | MIT label; the code is Turbine IP | fetched 2026-10-10 | Retail behaviour, re-expressed and cited by function name (table below). **No decompiled code is in the repository.** |
| eriknihlen/OpenAC `src/AcDream.App/Rendering/Shaders/terrain_modern.vert`, `terrain_modern.frag` | MIT | 1ee5a08581a7f4b0b22effdf911171a76cf4391c | Read as a cross-check only. It agrees on the alpha-map rotations and on the 0.01 m terrain lowering. Its known deviations from retail were **not** adopted: the `maskBlend3` overlay approximation instead of the sequential merge, per-pixel fog instead of D3D per-vertex range fog, no truncation of the vertex light to bytes, and no mip-level cap. |
| Godot 4.7 class reference (`Mesh.ArrayCustomFormat`, `ArrayMesh.AddSurfaceFromArrays`, `Texture2DArray.CreateFromImages`, `RenderingServer.ViewportGetMeasuredRenderTimeGpu`), via the GodotSharp 4.7.2 XML docs in the NuGet cache | MIT (engine), CC-BY 3.0 (docs) | 4.7.2 | API usage only. |
| The game-core track's render contract (`docs/render-contract.md`, `scripts/Render/RenderGlobals.cs` on its branch) | Lifestone (AGPL-3.0) | worktree `wf_a50c451a-57b-1`, read 2026-10-10 | Uniform names and meanings (`ac_sun_dir`, `ac_sun_color`, `ac_land_ambient`, `ac_fog_*`, `ac_max_mip_level`) and the `ILandblockLayer` member shapes that `TerrainLandblockLayer` mirrors. |

## Retail functions re-expressed (product code)

| Lifestone | Retail function (acclient.c line, 11.4186) |
|---|---|
| `terrain.gdshader` merge order, tiling and weight | `TexMerge::FillTempTexBuffer` (305909), `ImgTex::TileCSI` (365513), `ImgTex::MergeTexture` (365632), via docs/terrain-shader.md §5 |
| `terrain.gdshader` vertex light and its byte truncation; `z_fight_adjust` | `CLandBlockStruct::calc_lighting` (353713), `ACRender::landPolyDraw` (719994) |
| `terrain.gdshader` fog | `RenderDeviceD3D::SetDefaultD3DStates` (460787–460812): FOGVERTEXMODE LINEAR, RANGEFOGENABLE |
| `TerrainMipChain.RetailMaxLevels`; `ac_max_mip_level` = 3 | `ImgTex::CreateD3DTexture` (366008): `min(4, log2 + 1)` levels, `D3DXFilterTexture` box filter. Terrain goes through `TexMerge::MakeNewSurface` (306276) → `ImgTex::CreateLScapeTexture` (367758) |
| `TerrainAtlasBuilder` level choice | `ImgTex::GetSurfaceDID` (366232), `Render::ShouldDropHighDetail` (docs/textures.md §4) |
| `TerrainSeamlessNormals` (**not retail**, opt-in) | The contrast with `CLandBlockStruct::calc_lighting`'s per-block sum; plane normals as `CPolygon::make_plane` (359628) |
