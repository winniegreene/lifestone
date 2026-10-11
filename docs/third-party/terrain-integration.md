# Third-party sources: terrain integration track (M1 wave 2b)

This track covers:
- `src/Lifestone.Game/scripts/World/Terrain/` (`TerrainLandblockLayer`, `TerrainBlockBuilder`, and the changes to `TerrainRenderer`, `TerrainBlockMesh`, `TerrainTextureArrays`, `TerrainPreview`, `TerrainPreviewOptions`, `TerrainGoldenRunner`);
- `src/Lifestone.Game/scripts/World/TerrainLayer.cs` (the placeholder, now behind `--placeholder-terrain`);
- `src/Lifestone.Game/shaders/terrain/terrain.gdshader` (global uniforms);
- the loud size check in `src/Lifestone.Data/Terrain/Blending/TerrainAtlasBuilder.cs`;
- `tests/Lifestone.Data.Tests/TerrainRender/{TerrainGroundMatchesRenderMeshTests,TerrainShaderGlobalsTests}.cs` and the updated `TerrainAtlasBuilderTests.cs`;
- `tools/terrain-golden.ps1`;
- `docs/terrain-renderer.md` §5 and §7.1;
- the marked one-line registrations in `scripts/Main.cs` and `scripts/Core/GameOptions.cs`.

## Code ported into the repository

None. Every file is written for Lifestone. The ground sampler the layer registers (`Lifestone.Data.World.Scenery.TerrainSampler`) is the scenery track's port of ACE's landscape height code; its header and [scenery.md](scenery.md) list that source.

## References, not copied

| Source | Licence | Pinned at | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, Hex-Rays decompile of acclient 11.4186 (`acclient.c`) | MIT label; the code is Turbine IP | fetched 2026-10-10 | Retail behaviour, cited by function name (table below). **No decompiled code is in the repository.** |
| Lifestone's render contract (`docs/render-contract.md`, `scripts/Render/RenderGlobals.cs`) and game core (`docs/game-core.md`, `ILandblockLayer`, `LandblockStreamer`, `GroundHeights`, `MainThreadQueue`, `WorkerPool`) | Lifestone (AGPL-3.0) | master 93dbe80 | The global uniform names and the layer, threading and ground-height contracts this track implements. |
| Godot 4.7 class reference (`RenderingServer.GlobalShaderParameterSet/Get`, `Texture2DArray.CreateFromImages`, `Image.CreateFromData`, `ShaderMaterial`, `ArrayMesh.AddSurfaceFromArrays`, `Node.TreeExiting`), via the GodotSharp 4.7.2 XML docs in the NuGet cache | MIT (engine), CC-BY 3.0 (docs) | 4.7.2 | API usage only. |
| .NET `GC.GetGCMemoryInfo` / `GCMemoryInfo` documentation | MIT | .NET 10 | A one-off instrumented measurement of the in-flight collection (docs/terrain-renderer.md §7.1); the instrumentation was not committed. |

## Retail functions re-expressed or cited (product code)

| Lifestone | Retail function (acclient.c line, 11.4186) |
|---|---|
| `terrain.gdshader` reads `ac_land_ambient`, never `ac_ambient` | `CLandBlockStruct::calc_lighting` (353713): `ambient_color × ambient_level`; the object ambient is `LScape::calc_object_light` (render-contract.md §1) |
| `TerrainLandblockLayer` ground samplers | `CLandCell::find_terrain_poly`, `Plane::set_height` (through `TerrainSampler`) |
| `TerrainAtlasBuilder` refuses layers of different sizes (`TODO(terrain)`) | `ImgTex::TileCSI` (365513) and `ImgTex::MergeTexture` (365632) address each layer by its own size |
| `TerrainNormalMode.RetailPerBlock` default | `CLandBlockStruct::calc_lighting`'s per-block normal sum (docs/terrain-renderer.md §8) |
| The 0.01 m lowering noted in `TerrainGroundMatchesRenderMeshTests` | `ACRender::landPolyDraw` (719994), `zFightTerrainAdjust` |
