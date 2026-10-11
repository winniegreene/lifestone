# Third-party sources: textures track

**No third-party code was copied.** Every file in `src/Lifestone.Data/Textures/` and `tests/Lifestone.Data.Tests/Textures/` was written for Lifestone. The sources below were read to establish behaviour, byte layouts and constants. Each rule is cited in the code by function or file name. Rationale and measurements are in [../textures.md](../textures.md).

| Source | Version | Licence | What it was used for |
|---|---|---|---|
| Retail client decompile, [Mag-nus/acclient-AI-RE](https://github.com/Mag-nus/acclient-AI-RE) `2013-09 11.4186` | fetched 2026-10-10 | Turbine/WB IP; repo labelled MIT. **Reference only: never copied.** Behaviour is re-expressed and cited by function name. | Pixel formats and masks, Serialize sizes, clip-map rule, palette expansion and modification, ObjDesc sub-palette units and de-duplication, SurfaceTexture level choice, Surface load fix-ups, and the D3D blend/alpha-test/fog state (functions listed below). |
| [ACEmulator/ACE](https://github.com/ACEmulator/ACE) (`vendor-eval/ACE`) | v1.78.4816 @95ef426d | AGPL-3.0 | Cross-checks: `ACE.DatLoader/FileTypes/Texture.cs` (byte orders, LSCAPE_R8G8B8 = R,G,B), `ACE.DatLoader/Entity/SubPalette.cs` (units of 8, 0 = 256), `ACE.Server/WorldObjects/WorldObject_Networking.cs` `SerializeModelData` and `CalculateObjDesc` (wire writer, character default ranges), `Creature_Networking.cs` (ClothingTable range / 8), `ACE.DatLoader/FileTypes/PaletteSet.cs` (shade formula), `ACE.Server/Physics/PhysicsGlobals.cs` (`EPSILON` = 0.0002f, the value of retail's `F_EPSILON`). `ACE.DatLoader/DxtUtil.cs` is MonoGame code under Ms-PL; it was read for comparison only and nothing was taken from it. |
| [ACEmulator/ACViewer](https://github.com/ACEmulator/ACViewer) (`vendor-eval/ACViewer`) | @c5c54fb | GPL-3.0 | Cross-checks: `ACViewer/Render/TextureCache.cs` (`IndexToColor` clip-map rule, highres fallback, JPEG red/blue swap explained in textures.md §2, `AddAlpha` reading LSCAPE_R8G8B8 as B,G,R, which textures.md §1 overrules), `ACViewer/Model/PaletteChanges.cs`, and `ACViewer/Model/ObjDesc.cs` (`AddPaletteChange` multiplies offset and length by 8). |
| [eriknihlen/OpenAC](https://github.com/eriknihlen/OpenAC) (`vendor-eval/OpenAC`) | @1ee5a08 | MIT (WorldBuilder-derived parts: see foundation-openac.md) | Cross-checks: `src/AcDream.Core/Textures/SurfaceDecoder.cs` and `src/AcDream.Core/Rendering/Wb/TextureHelpers.cs` (byte orders, index-below-8 rule). Not adopted: OpenAC's "pure black DXT texel is transparent on clip maps" rule, which retail does not have. |
| [Chorizite/DatReaderWriter](https://github.com/Chorizite/DatReaderWriter) | NuGet 2.1.9 (source @a40acb4 for reading) | MIT | Record types used directly (`RenderSurface`, `Palette`, `SurfaceTexture`, `Surface`, `PalSet`, `ClothingTable`), the `SurfaceType`/`PixelFormat` enums, and the non-caching `TryGet<T, TResult>` overload. Already a project dependency. |
| Microsoft, "Block Compression (Direct3D 10)" / S3TC | public documentation | – | DXT1/3/5 block layout implemented in `BlockCompression.cs`. |
| Lifestone research scout `acimage.py` (session scratchpad `research/scout/`) | 2026-10-09 | project research code | Independent reference decoder. Transcribed into `tests/Lifestone.Data.Tests/Textures/ScoutReferenceDecoder.cs` for pixel-exact tests. Its Python output CRCs on the user's dats are the expected values. |

## Retail functions cited

- **Image and palette**
  - `ImgTex::CopyIntoData`
  - `ImgTex::Combine`
  - `ImgTex::CreateCombinedTexture`
  - `ImgTex::CreateFromRenderSurface_Internal`
  - `ImgTex::GetSurfaceDID`
  - `ImgTex::GetSubDataIDs`
  - `ImgTex::CreateD3DTexture`
  - `ImgTex::MergeTexture`
  - `Palette::InitLoad`
  - `Palette::get_color32`
  - `Palette::Modify` (both overloads)
  - `Palette::makeModifiedPalette`
  - `PalSet::GetPaletteID`
- **Render surface and pixel formats**
  - `RenderSurface::Serialize`
  - `RenderSurface::CreateFromSourceData`
  - `RenderSurface::SelectSurfaceFormat`
  - `RenderSurface::IsD3DFormat`
  - `PixelFormatDesc::SetFormat`
- **Surface**
  - `CSurface::Serialize`
  - `CSurface::InitEnd`
  - `CSurface::SetTextureAndPalette`
  - `CSurface::UsePalette`
- **Object description and parts**
  - `Subpalette::UnPack`
  - `Subpalette::replaces`
  - `Subpalette::supercedes`
  - `ObjDesc::UnPack`
  - `ObjDesc::AddSubpalette`
  - `CPartArray::DoObjDescChanges`
  - `CPartArray::SetPalette`
  - `CPhysicsPart::DetermineBasePal`
  - `CPhysicsPart::UsePalette`
- **Draw state**
  - `D3DPolyRender::SetSurface` (both overloads)
  - `D3DPolyRender::ConstructMesh`
  - `D3DPolyRender::DrawMesh` (both overloads)
  - `D3DPolyRender::RenderMeshSubset`
  - `D3DPolyRender::DrawPolyInternal`
  - `D3DPolyRender::polyListFinishInternal`
  - `ACRender::landPolyDraw`
  - `D3DPolyRender::AddMeshToAlphaList`
  - `copyVert`
  - `RenderDeviceD3D::SetFFFogAlphaDisabled`
  - `RenderDeviceD3D::SetSolidColorTextureColor`
  - `Render::ShouldDropHighDetail`
  - `Render::SetOverallGraphicsQuality`
  - `RenderTexture::DropUnwantedLevels`
- **Globals**
  - `D3DPolyRender::s_256AlphaTestRef` (100)
  - `D3DPolyRender::s_ddsAlphaTestRef` (200)
  - `D3DPolyRender::s_AlphaDelayMask` (14)
  - `dword_81EF9C`, the "Environment texture detail level" preference (`Render_EnvironmentTextureDetail`, default 1, values {4, 3, 2, 1, 0})
  - `F_EPSILON` (0.0002f, `CSurface::InitEnd`)
- **Enums** from `acclient.h`
  - `SurfaceType`
  - `PixelFormatID`
  - `BlendMode`
  - `TexAddress`
  - `_IJL_COLOR`
