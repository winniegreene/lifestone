# Third-party sources: game core track (`src/Lifestone.Client/`, `src/Lifestone.Game/`)

No third-party code was copied or ported into this track, so no file carries a port header. Every retail behaviour is re-expressed and cited by function name in the code comments. The table shows what each source was used for.

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-10 | Behaviour of the functions listed below. Nothing copied. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`) | See the ACE list below. |
| ACEmulator/ACViewer | GPL-3.0 | @c5c54fb (`vendor-eval/ACViewer`) | `ACViewer/Data/Locations.txt`, "Town" entries: the Holtburg (`0xA9B40019 [84 7.1 94]`) and Arwic (`0xC6A90009 [46.805 4.219 42.005]`) positions in `WorldLocations`. These are facts (positions), cited in the code. |
| Godot 4.7.2 class reference (`GodotSharp.xml` shipped with the engine) | MIT | 4.7.2-stable | Engine behaviour relied on: `Image.GenerateMipmaps` refuses compressed images, global shader parameters, `CUSTOM0` R-float vertex attributes (`Mesh.ArrayCustomFormat.RFloat`), `RenderingServer.GetRenderingInfo`. Not code. Behaviours the reference does not settle are measured by `--engine-checks` (game-core.md §6) instead. Godot uploads `Image.FORMAT_DXT1` as BC1 **without** alpha: the RenderingDevice format is `BC1_RGB_UNORM_BLOCK` on Vulkan and D3D12, and punch-through texels render opaque black. The class reference's "only providing 1 bit of alpha" describes the S3TC format, not Godot's upload. Vertex normals are stored octahedral-encoded, so a zero normal reads back as (0, 0, −1). |

**ACE files used:**
- `Source/ACE.Server/Physics/Common/LScape.cs`: `MidRadius = 5`, the server-side port's radius, contrasted with the client's default of 8.
- `Source/ACE.Server/Entity/PositionExtensions.cs`: `ToGlobal` (the global-metre convention) and `GetMapCoords`, the continuous formula Lifestone does *not* use.
- `Source/ACE.Entity/Position.cs` and `Physics/Common/LandDefs.cs`: `BlockLength = 192`.
- `Source/ACE.Server/Physics/Animation/MotionInterp.cs`: `RunAnimSpeed = 4.0`, the walk-mode speed.

## Retail functions re-expressed (11.4186 `acclient.c` line numbers)

- **Coordinates:**
  - `LandDefs::gid_to_lcoord` (209521) and `LandDefs::lcoord_to_gid` (218182)
  - `LandDefs::blockid_to_lcoord` (122260) and `LandDefs::in_bounds` (122254)
  - `LandDefs::get_outside_lcoord` (467411) and `LandDefs::adjust_to_outside` (467434); the `flt_8EDE44 = 24.0 * 8.0` initialiser is at 800518
  - `CPlayerSystem::InqPlayerCoords` (398264), `LandDefs::CellidToCoordinateString` (467472) and `gmRadarUI::UpdateCoordinates` (263786)
  - `CPhysicsObj::get_landscape_coord` (318098)
- **Streaming radius:**
  - the global `mid_radius = 8` (45608)
  - `SmartBox::SetRegion` (144294) and `SmartBox::set_mid_radius` (144258)
  - `LScape::SetMidRadius` (306429)
  - `Render::SetOverallGraphicsQuality` presets (378753–378793)
- **Floating origin:** `LScape::calc_frame` (306986) and `LScape::update_loadpoint` (308283), cited for the landblock-relative placement.
- **Render state and lighting:**
  - `D3DPolyRender::SetSurface` (454385) and `D3DPolyRender::RenderMeshSubset` (454633)
  - `copyVert` (454109)
  - `CMaterial::CMaterial`, `SetDiffuseSimple` and `SetLuminositySimple` (360613–360648)
  - `PrimD3DRender::UpdateLightsInternal` (453206)
  - `RenderDeviceD3D::SetDefaultD3DStates` (linear range fog, 460762; the fog states at 460787-460812)
  - `ImgTex::CreateD3DTexture` (366008; at most 4 mip levels, via docs/textures.md §7)
- **Camera:** `CameraSet::InHead` (148094), for the first-person offset (0, 0.18, 0) and the eye-height note.

## Own implementations (no source copied)

- **BCn encoder** (`Lifestone.Client.Textures.BlockEncoder`): written from the public BC1/BC2/BC3 block layout, the same layout `Lifestone.Data.Textures.BlockCompression` decodes. Endpoints are a range fit (farthest pair, optionally inset by 1/16). The tests decode with an independent reference decoder written from the format description.
- **Mip box filter** (`MipChain.Downsample`): rounded 2×2 mean, the rounding Godot's `Image.GenerateMipmaps` uses for RGBA8.

- **Engine checks** (`Diagnostics/EngineChecks.cs`): probes of Godot's own behaviour (mesh read-back, a rendered 128×32 sub-viewport, `RenderingDevice.TextureGetFormat`), written for this project.
- **Chained render caches** (`Lifestone.Client.Rendering.RenderResourceCaches`): own design; idle entries drop their dependencies so each LRU budget holds.

## Deliberate differences from references

- **Map coordinates:** retail's cell-quantised formula, not ACE's continuous `global / 240 − 102`.
- **Streaming radius:** retail's client default of 8, not ACE's server-side 5.
- **Surface diffuse:** `Surface.Diffuse` is not used in lighting. Retail's D3D path never reads `Render::diffuse`, which corrects docs/textures.md §5 (erratum there).
- **Cut-out shortcut:** alpha-tested blending surfaces whose texels never blend draw as opaque cut-outs by default; retail also blends filtered edge samples (render-contract.md §2, `--cutouts=blend` for retail's behaviour).
