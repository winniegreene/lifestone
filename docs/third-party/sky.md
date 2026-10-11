# Third-party sources: sky track (`src/Lifestone.Data/World/Sky/`)

No third-party code was copied or ported into this track. Every algorithm was re-expressed from the sources below, and each one is cited by function name in the code comments. The table shows what each source was used for.

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-10 | The behaviour of every sky, light, fog and calendar function. See the function list below. |
| Mag-nus/acclient-AI-RE, `2015-10 11.6096` (the user's client build) | same | same | Checking that `CalcPresentDayGroup`, the `GetSky` visibility test, the `GetTimeOfDay` wrap ratio, `GameTime::CalcDayBegin` and `SmartBox::SetWorldAmbientLight` are unchanged in 11.6096. The symbol addresses come from `symbol_transplant_11.6096.tsv`. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`) | `Source/ACE.Common/DerethDateTime.cs` provides the calendar reference points used in the tests: lines 23–27, MinValue at lines 56–64 and MaxValue. The same file defines the EMU sync-time zero (line 1062). `Source/ACE.Server/Entity/Timers.cs` and `Managers/WorldManager.cs:391` show that game time is in seconds and advances 1:1 with real time. `Physics/Animation/AFrame.cs` gives the `set_heading`/`get_heading` operand order. The `ACE.DatLoader/Entity/{SkyDesc,DayGroup,SkyObject,SkyObjectReplace,SkyTimeOfDay,GameTime}.cs` files were used to cross-check the dat field order. |
| eriknihlen/OpenAC | MIT | @1ee5a08 (`vendor-eval/OpenAC`) | Comparison only (`AcDream.Core/World/{SkyState,SkyDescLoader,SkyDayGroupSelector,DerethDateTime,DerethCalendar}.cs`). Its deviations were **not** reproduced. |
| Chorizite/DatReaderWriter | MIT | NuGet 2.1.9 (already a dependency) | Parses `Region.SkyInfo` and `Region.GameTime`. |

ACViewer has no sky, fog or time of day (see `docs/research/rendering.md` §8), so it was not used.

## Retail functions re-expressed (11.4186 `acclient.c` line numbers)

- **`PORTAL/cregion/CSkyDesc.c`**
  - `DayGroup::GetTimeOfDay` (301423)
  - `SkyDesc::GetLighting` (301484)
  - `SkyDesc::GetWorldFog` (301601)
  - `SkyDesc::CalcPresentDayGroup` (301663)
  - `SkyDesc::GetSky` (302961)
  - the UnPack functions for `SkyObject`, `SkyObjectReplace`, `SkyTimeOfDay`, `DayGroup` and `SkyDesc` (layout only)
- **`PORTAL/gametime/GameTime.c`**
  - `GameTime::CalcDayBegin` (463202)
  - `GameTime::CalcTimeOfDay` (463242)
  - `GameTime::GetDateTimeString` (463285)
  - `GameTime::UseTime` (463394)
  - `GameTime::UnPack` (464013)
- **`PORTAL/lscape/Sky.c`**
  - `GameSky::UseTime` (308825)
  - `GameSky::CalcFrame` (308458)
  - `GameSky::MakeObject` (308427)
  - `GameSky::Draw` (308474)
  - `GameSky::UpdatePosition` (308366)
  - `GameSky::OnTimeAdjustment` (308534)
- **`PORTAL/lscape/LScape.c`**
  - `LScape::UseTime` (307221)
  - `LScape::set_landscape_lighting` (307002)
  - `LScape::SetDay` (306897)
  - `LScape::calc_object_light` (146452)
  - `LScape::min_ambient` is `LSCAPE_LIGHT_MINIMUM` = 0.2: the constant is at 40344 and its static initialiser at 783180.
- **`PORTAL/cgeometry/CFrame.c`**
  - `Frame::set_heading` (357691)
  - `Frame::set_vector_heading` (357667)
  - `Frame::euler_set_rotate`
  - `Frame::grotate` (357421)
  - `Frame::set_rotate`
- **`PORTAL/cphysics/CPhysics.c`**
  - `CPhysics::AddGfxVelocity` (311467)
  - `CPhysics::UseTime` (311335)
  - `CPhysics::UpdateTexVelocity` (311248)
  - `MIN_QUANTUM` = 1/30 (783717)
- **`CORE/timer/Timer.c`** (`Timer::set_time`, `Timer::update_time`) and **`PORTAL/clinet/ClientNet.c`** (`ClientNet::HandleTimeSynch`, 371515): how game time is sourced.
- **Rendering and lighting:**
  - `RenderDeviceD3D::SetDefaultD3DStates` (460762): linear vertex range fog.
  - `RenderDeviceD3D::SetFFFogProperties` (460308).
  - `PrimD3DRender::UpdateLightsInternal` (453206): D3D sun light.
  - `SmartBox::SetWorldAmbientLight` (144222; 11.6096 at 0x004530E0).
  - `CLandBlockStruct::calc_lighting` (353712): the landscape ambient term.
- **`ClientMain::ConfigureFPU` (60885)** and the D3D `CreateDevice` behaviour flags (`|= 0x106`, which includes D3DCREATE_FPU_PRESERVE; device init around 459976). Together these show that the x87 FPU runs at 53-bit precision, which justifies doing the arithmetic in double.

## OpenAC deviations deliberately not reproduced

From `docs/research/foundation-openac.md`, "Other deviations", plus review in this track:

- Sun heading uses a shortest-arc lerp. Retail lerps linearly, so heading 350 → 10 passes through 180.
- Sky-object visibility is inclusive and wraps at midnight. Retail uses a strict `Begin < t < End` with no wrap.
- Keyframe wrap uses span `1 + first.Begin - last.Begin`. Retail uses `(t - b) / (1 - b)`.
- Day-group hash is computed in float. Retail is exact at 53-bit precision, which equals `(hash * n) >> 32`.
- Colours are lerped as floats. Retail truncates each byte.
- The keyframe ratio is clamped. Retail does not clamp.
- Replacement luminosity, diffusion and translucency are not lerped against the next keyframe's matching replacement. OpenAC's `App/Rendering/Sky/SkyRenderer.PickReplaces` uses the values from the before keyframe as they are, and clamps transparency.
- The calendar's origin offset is 3333.75 (7/16 of a day). The dat's `ZeroTimeOfYear` is 3600.
