# Sky, time of day and lighting (M1 wave 2)

**Track:** sky, time of day and navigation UI.
**Owns:** `src/Lifestone.Game/scripts/World/Sky/`, `src/Lifestone.Game/scripts/UI/`, `src/Lifestone.Client/Sky/`, `src/Lifestone.Client/Navigation/`, `src/Lifestone.Data/World/Data/`, this document and [navigation.md](navigation.md).
**Builds on:** the wave-1 sky data layer (`src/Lifestone.Data/World/Sky/`: `DerethSky`, `SkyEvaluator`, `SkyUpdateCadence`, `SkyTextureScroll`, `DerethCalendar`, `DerethClock`) and the [render contract](render-contract.md).
**Authority:** retail functions are cited by name and `acclient.c` line from the Mag-nus/acclient-AI-RE decompile of 11.4186. Nothing from it is copied. Sources: [third-party/sky-nav.md](third-party/sky-nav.md).

## 1. In one screen

- **Time.** The Dereth clock runs at retail's rate from ACE's server time by default (`DerethTimeController`). The time panel (bottom right) shows the date and time as retail prints them ("Solclaim 10, 10 P.Y.", "Morntide-and-Half"). It has a slider over the current day, Pause / 1x / 60x / 600x, "Live" and a day-group (weather) override.
- **Light and fog** come from the Region's SkyDesc keyframes at the current time. They are pushed into the render contract's global uniforms on retail's cadence: fog every 0.8 s and light every 15 s of client time, or at once after a jump.
- **Sky objects** (dome, stars, moons, clouds, sun, rain) are drawn as retail's `GameSky` draws them:
  - created, deleted, lit, faded and scrolled as in retail;
  - anchored where retail anchors them;
  - drawn before the world with depth test ALWAYS, in object order, with byte-space blending.
- **Checks.** `--sky-checks` pins the rendering chain on the running engine. 33 xUnit tests cover the logic, most of them on the real Region.

Screenshots (gitignored): `docs/screenshots/m1/golden/` (the golden viewpoints, [navigation.md](navigation.md) §3), `sky-ui.png`, `sky-checks.png`, `sky-tod-0.5.png` and `sky-tod-0.9.png` (`--screenshot --time-of-day=F`, §2), `sky-anchor-camera/`.

## 2. Time of day

`Lifestone.Client.Sky.DerethTimeController` is the clock everything reads.

- **Retail rate (speed 1, the default).** The game time is ACE's server clock plus an offset, exactly like retail's `Timer::cur_time + GameTime.TimeZeroDelta` (`GameTime::UseTime`, acclient.c:463394; `GameSky::OnTimeAdjustment`, 308535). ACE's clock zero is 2017-01-31 17:00 UTC (`DerethClock`). So a live run today shows a date in the 120s P.Y., as a client connected to ACE would.
- **Pause and time-lapse (Lifestone tools, not retail).** At speed 0 the clock stops. At 60x and 600x the whole client clock runs fast: retail's `Timer::cur_time` analogue, `ClientClock`, advances at the same rate. The fog and light cadence and the cloud scroll then keep their game-time pace, as a time-lapse film of retail would show.
- **Jumps.** The slider and explicit times move the game time without moving `ClientClock`. They count as a discontinuity, and the light and fog refresh on that frame. This is the effect of retail's `LScape::SetDay`, which zeroes both tick deadlines (acclient.c:306897). Without it, a paused clock at 0 would never relight.
- **Slider.** It changes the fraction of the current Dereth day (`DerethCalendar.WithDayFraction`). The date, and so retail's day group, stays.
- **The core's clock follows.** `GameServices.Clock.TimeZeroDelta` is set every frame, so the position HUD and anything else reading `DerethClock.Now` agree.
- **Automated runs are reproducible, whatever the real date** (`SkyClockStart`). With `--screenshot`, `--fly-path`, `--golden-viewpoints` or `--sky-checks`:
  - without `--game-time`, the clock starts on the first day of 10 P.Y. whose retail day group is 0 (Sunny), Solclaim 10, 10 P.Y., at `--time-of-day` (default 0.5, Midsong). That is the core's documented meaning of `--time-of-day=F`, "day group 0 at fraction F" (render-contract.md §1), and the lighting the core used before the sky existed, so every track's screenshots stay comparable;
  - with `--game-time`, it starts there, and `--time-of-day` moves within that day;
  - the clock is paused unless `--time-speed` is given.
- **Interactive runs** keep retail's live clock at speed 1. There `--time-of-day=F` moves to fraction F of today's Dereth day, so the live weather stays. `SkyObjectEvaluatorTests` pins both rules, and the log line `[sky] time of day started` shows the result (`--screenshot --time-of-day=0.5`: `date="Solclaim 10, 10 P.Y." time=Midsong speed=0 day_group=0`).

| Option | Effect |
|---|---|
| `--game-time=S` | Start at game time S (seconds; 0 = Morningthaw 1, 10 P.Y., Morntide-and-Half) |
| `--time-of-day=F` | (core option) Day fraction F: of today's live day interactively, of the Sunny day above in automated runs |
| `--time-speed=X` | 0 pause, 1 retail (the interactive default; automated runs default to 0), 60, 600, up to 4096 |
| `--day-group=N\|auto` | Force a day group (a viewer tool; retail always uses `SkyDesc::CalcPresentDayGroup`) |
| `--sky=off` | No sky objects. Light and fog still follow the time; the background is the fog colour |
| `--sky-anchor=follow\|retail\|camera` | Sky anchor (§4.2). `follow` (default since wave 3) keeps the camera's x and y at retail's height; `retail` is retail's landblock corner; `camera` is a non-retail comparison mode |
| `--retail-sky-anchor` | Same as `--sky-anchor=retail`: the sky jumps 192 m at every landblock crossing, as in retail |
| `--weather=off` | Retail's weather option off (`LScape::weather_enabled`): no rain objects |
| `--ui=on\|off\|open` | Panels shown or hidden (default: shown, hidden in automated runs); `open` also opens the teleport panel |
| `--sky-checks` | Engine checks (§6), then quit |

## 3. Light and fog

`Lifestone.Client.Sky.SkyDirector.Update` runs once per frame. It follows `LScape::UseTime` (acclient.c:307221):

- **Every frame:** the sky objects are evaluated at the game time, so they move every frame. The evaluation is `DerethSky.Evaluate`'s (the day's group, `present_time_of_day`, `GetSky`) through paths that allocate nothing (§7); `SkyDirectorTests` pins the equality over three days and their boundaries.
- **Fog** is re-read every `tick_size` (0.8 s, the float 0.8f) of client time. A disabled reading leaves the previous fog in force, as retail's `GetWorldFog` returning 0 does. Every keyframe in the user's Region has fog on.
- **Light** is re-read every `light_tick_size` (15 s).
- **Refreshes:** the first frame, a time jump and a forced day group refresh both at once.

`SkyController` applies the result through two calls added to `RenderGlobals`: `ApplyLighting(SkyLighting)` and `ApplyFog(SkyFog)`. They set exactly the render contract's uniforms (render-contract.md §1):

| Uniform | From |
|---|---|
| `ac_sun_dir` | `SkyLighting.SunDirection` (Godot axes) |
| `ac_sun_color` | `SkyLighting.SunDiffuse` = sun colour x \|sun\| |
| `ac_ambient` | `SkyLighting.ObjectAmbient` = ambient colour x (level + 0.2 \|sun\|) |
| `ac_land_ambient` | `SkyLighting.LandscapeAmbient` = ambient colour x level (floored at 0.2) |
| `ac_fog_*` | `SkyFog` |

**What the user's Region gives** (day group 0, measured in the golden manifest):

| Time | Sun | Ambient | Fog |
|---|---|---|---|
| Midsong (0.5) | 67° up in the east, #FAD797 x 0.77 | #E6E6FF x 0.35 | 150-2,400 m #C3C8DC |
| Dawnsong (0.3) | 26° up in the east, #FAD797 x 0.71 | #E6E6FF x 0.35 | 150-2,400 m |
| Gloaming (0.9) | 10° up in the west, #FFFF96 x 0.5 | #C8B4FF x 0.4 | 90-800 m #5A3C3C (red dusk) |
| Darktide-and-Half (0.08) | 0.9° up in the east, #DCDCDC x 0.31 | #C886FF x 0.45 (purple) | 0-400 m #171725 |

Rainy day group 3 has 150-1,500 m day fog.

The light's direction and the sun sprite are independent in the data. The light pitch comes from keyframes (90° at 0.61, then a flip from east to west). The sprite follows its own arc: -23° to 203° over 0.16-0.94, so it rises at 0.239, culminates at 0.55 and sets at 0.861.

## 4. Sky objects

### 4.1 Retail behaviour

Re-expressed from the decompile; `Lifestone.Client.Sky` holds the engine-agnostic part.

**Lifetimes** (`SkyObjectTracker`):
- `GameSky::CreateDeletePhysicsObjects` (acclient.c:308687) keeps a slot's physics object while its GfxObj id and properties are unchanged (and, for weather objects, while the weather option did not change).
- Otherwise it deletes the object and `GameSky::MakeObject` (308427) makes a new one. Nothing is made for id 0, or for a weather object while weather is off.
- An object kept across a change of day group keeps its state.

**Sticky lighting** (`SkyPartMaterial`). `GameSky::UseTime` (308826) calls `SetLuminosity`, `SetDiffusion` and `SetTranslucency` (x 0.01) only for the values the bracketing keyframes set, so the others keep their last values. The part-level state follows `CPhysicsPart::SetLuminosity/SetDiffusion/SetTranslucency` (315454/315420/315488), `CurSettingsAreDefault` (314711), `CopyMaterial` (314472) and `CMaterial` (360585-360653):
- **No material while every setting is default.** Ambient and diffuse then come from the white vertex colour, and the alpha is the Surface's (1 - t).
- **Otherwise a `CMaterial`** with emissive l, diffuse d, ambient 1 and alpha 1 - t. `RenderDeviceD3D::SetCurrentMaterial` (457648) switches the colour sources to the material, so the vertex alpha no longer applies.
- **Translucency 1.0 only hides the part** (`draw_state |= 1`).
- **A Surface's own luminosity > 0 replaces the emissive** while it draws (`D3DPolyRender::RenderMeshSubset`, 454633). The dome's surfaces have luminosity 1, so the dome always shows its texture at full brightness. Night comes from the stars layer.
- **A material alpha below 1 forces SRCALPHA/INVSRCALPHA with the alpha test off**, unless the surface already blends without an alpha test (`D3DPolyRender::SetSurface`, 454385, the test at 454531).

**Draw** (`GameSky::Draw`, 308475; `LScape::draw`, 307754). The sky is drawn before the landblocks with:
- depth test ALWAYS and no depth writes;
- fog off unless the server fog override is on;
- `zfar` x 4 (16,000 m with retail's 4,000 m `Render::zfar`, 45527; `znear` 0.1, 45526);
- lighting forced on;
- nothing deferred to the alpha list (`D3DPolyRender::DrawMesh`, 455154).

The order is: object order, then part order, then subset order. The after pass (`Draw(1)`, the "weather cell") is drawn after the landblocks and before the alpha-list flush, only while weather is enabled. The frame is cleared to black (`SceneTool::BeginScene`, 122402).

**Lighting formula** (render-contract.md §2.1 with the material above):
```
light = clamp(emissive + ac_ambient + ac_sun_color x diffuse x max(0, N.L), 0, 1)
```

**Texture scroll:** `SkyTextureScroll` (wave 1, `CPhysics::UpdateTexVelocity`), run on the client clock and added to the UVs. It wraps into [0, 1) for WRAP addressing, which is invisible and keeps float precision.

### 4.2 Where retail puts the sky (an unexpected finding)

Retail does **not** centre the sky on the camera. Every sky object sits at the **south-west corner of the viewer's landblock, at height 0**.

The evidence chain:
1. `GameSky::UseTime` builds each object's frame with origin (0, 0, 0) every frame (308826).
2. `GameSky::UpdatePosition` (308367), called from `SmartBox::set_viewer` (143995) through `LScape::set_sky_position`, keeps that origin: `Frame::interpolate_origin(..., t = 0.0)` (357229) returns the object's own origin. It then sets the object's cell to the viewer's cell (`set_cell_id_recursive`).
3. The draw path places an object at `LandDefs::get_block_offset(viewer cell, object cell) + origin` (`PrimD3DRender::positionPushInternal`, 453522). The view matrix keeps the viewer's full translation (`Render::update_viewpoint`, 380352).

The same reading in 11.6096 gives the same constant 0.0. Weather objects are the exception: they take the viewer's x and y, with z forced to -120 m unless property 8 keeps the viewer's z (308415).

**Consequences**, all of them retail:
- The dome (±1,050 m, z -500 to 1,050), the clouds (20 km planes at 750-780 m) and the sun (a 600 m sprite 1,050 m out) shift a little as you move inside a landblock.
- They jump by 192 m when you cross into the next landblock.
- They sit lower the higher you stand. From Holtburg's drop point, 94 m up, the sunrise sprite clears the horizon later than its arc says.

`--sky-anchor=camera` centres the sky on the camera instead, for comparison. `docs/screenshots/m1/sky-anchor-camera/` holds views 02, 05 and 12 rendered that way; compare them with `golden/`.

**Wave 3 (render-fidelity fix): the default is now `follow`; `--retail-sky-anchor` restores retail.**
- **Re-verified.** The chain above was re-read in the 11.4186 decompile:
  - `GameSky::UseTime` zeroes the origin;
  - `Frame::interpolate_origin` with t = 0 returns `from`, the object's own frame;
  - `GameSky::Draw` adds no special placement.
- **Measured.** The visual audit measured the consequence: `sky-before-boundary` → `sky-after-boundary` (A9B4 x = 191 → AAB4 x = 1, z 150 m, heading 90°, pitch 25°), rows 340–640. With retail's anchor the sky changes by a mean 13.0 levels, 46% of pixels by more than 10, max 123 (clouds visibly jump). A 95 m move inside the block changes it by a mean 7.5.
- **The glitch-fix policy.** Retail players would call a sky that jumps at a landblock edge a glitch, so the lead's fidelity policy makes the fix the default and keeps retail behind a flag.
- **`follow` (`SkyController.AnchorFor`)** puts the anchor at the camera's x and y and retail's height 0. Nothing jumps or drifts horizontally. The clouds stay at 750–780 m above sea level, and the dome's horizon sinks the higher you stand, as in retail.
- **Measured after the fix:**
  - across the boundary: mean 0.02, 0.08% of pixels above 10 (terrain at the bottom edge of the crop);
  - inside the block: mean 0.00.

  Screenshots are in `docs/screenshots/m1/fix-render/{before,after}/sky-*.png`.
- **Retail screenshots.** A retail capture running east across a boundary would still settle whether retail's sky visibly jumps. The decompile says it does.

### 4.3 How Lifestone draws it (Godot 4.7)

- **The before pass is drawn in its own view** (`SkyView`). It is an off-screen HDR-2D `SubViewport` with its own `World3D` holding the before-pass objects, with the anchor at the origin. Its camera copies the main camera's orientation, FOV and size, with the eye at (viewer - anchor), and uses retail's sky clip planes (near 0.1 m, far 16,000 m). The view clears to black and has the Linear tonemapper.
- **Its image becomes the main view's background** through a sky shader (`SkyShaders.BackgroundCode`). The world covers it exactly as "sky first, depth ALWAYS, world over it" does.
- **Byte-space blending.** The before-pass shaders write raw colour values with no sRGB conversion. Blends in the view's float buffer are therefore retail's byte arithmetic, and the background converts once so the screen shows those bytes. One difference remains: the float buffer does not saturate between draws, where retail's 8-bit one clamps after every blend (§8).
  - This matters: the night stars layer is an opaque black texture faded in by translucency (alpha 0.7 at Gloaming). Linear blending left the dusk sky about twice as bright as retail before this was done.
  - *(Wave 3.)* The main view now blends in byte space as well (render-contract.md §2.3). The background shader then hands the sky view's raw values over unconverted, and the main view's tonemap pass decodes the whole frame once (a 1D colour-correction LUT). With `--blend-space=linear` it converts here, as before.
- **The after pass** (rain cylinders that draw over the landscape) lives in the main view with the depth test off. It blends in the main view's blend space: byte space by default, so rain adds retail's bytes (`--sky-checks` expects byte-space adds then, linear-space adds with `--blend-space=linear`; both hold).
- **Order** is `render_priority` from -128 upwards in slot, part and subset order. In the main view it stays below 0, so after-pass objects draw before the world's translucent surfaces, as retail draws the weather cell before flushing its alpha list.
- **Placement** is recomputed from global coordinates every frame, so it follows the floating origin.
- **Culling.** Meshes get a 40 km cull box; retail draws sky objects whatever their bounds say.
- **Start-up** (before streaming, about 95 ms):
  - all 21 sky models the Region can show (every day group, every keyframe replacement) are prepared on the workers and uploaded (22 ms);
  - the 8 shader variants they can need, with and without a translucent part material, are compiled (72 ms);
  - one sub-pixel triangle per variant is drawn for 3 frames, so the GPU pipelines for both views exist before a day-group change or a fading part needs them.
  - *Wave-3 performance pass:* the models' worker part (assembly, mesh conversion, texture decodes) starts at the top of `Main._Ready` (`SkyTrack.Prefetch` → `SkyModels.Prefetch`), so it runs during the render, terrain and interiors warm-ups; `LoadAllNow` then only waits for what is left and builds on the main thread. Before, `LoadAllNow` blocked the start-up for 111–137 ms (`[sky] sky models loaded ms=`), most of it waiting for the decodes. The frames after the warm-up are unchanged: the first frame costs about 60 ms with or without the sky (`--sky=off` and terrain-only start-ups measure the same; game-core.md §7.3), so it is the engine's first frame rather than the sky's build.
  - *Reading the debug log:* the start-up `[sky] slow frame ms≈150 ... created=5 shader_swaps=8` line (and the 87–122 ms one after it), which the wave-3 audit quoted as the hitch, is not a frame of that length. `ms` is Godot's process delta, the previous frame's length as the engine measured it; the first deltas after start-up carry the whole of `Main._Ready`, capped at about 150 ms with the rest spread over the next ones. The line now also has `wall_ms`, the sky controller's own stopwatch since its previous update: 59–90 ms for the first line (it includes the rest of `Main._Ready` after the sky starts) and 51–64 ms for the second (fix-perf review runs, 2026-10-10; game-core.md §7.3).
- **Solid colours are passed as `Vector4`.** Godot converts a `Color` given to a plain `vec4` shader parameter from sRGB to linear, which darkens raw retail colours. `--sky-checks` pins this. The core's `MaterialFactory` had the same bug for untextured surfaces and is fixed (one line, commented).

## 5. The user's Region

| Data | Values |
|---|---|
| Day groups | 20: Sunny 0-2 and 4-6, Clear 8, 10, 11, Cloudy 12-14, Rainy 3, 7, 9, 15-19 |
| Objects per group | 7, or 17-19 on rainy days |
| Dome | 0x010015EE/F0/F1/F2: four textured sides at ±1,050 m, z -500 to 1,050, Surface luminosity 1, opaque, CLAMP. 0xF0 is the overcast (Cloudy and Rainy) dome |
| Stars | 0x010015EF: an opaque black 128² texture with white dots, alpha-blended. Hidden by day (tr 100), faded in at dusk (tr 30 to 0) |
| Moons | 0x01001F67 and 0x01001F6A: clip-mapped quads 2-2.7 km out, visible 0.04-0.21 and 0-0.23. 0x1F6A has an additive glow |
| Clouds | 0x01004C35-3A: 20 km planes at 750-780 m. Luminosity and diffusion go 15% at night to 100% at noon. Drawn before the sun on Sunny days and after it (covering it) on Cloudy and Rainy days |
| Sun | 0x01001348: a ±300 m additive quad at x = +1,050 m with a small white disc in a black texture |
| Rain | 0x01004C42 (before pass) and 0x01004C44 (after pass): 113 m cylinders, UV scroll -1.7 to -2 per second |
| Setups | 0x02000588/89/714/BA6: 5 cm fully translucent markers carrying a PhysicsScript id that retail never plays (`CelestialPosition::pes_id`, docs/research). Built and drawn invisibly, as retail |

## 6. Checks and tests

**`--sky-checks`** (`World/Sky/SkyChecks.cs`). Run it windowed, as the core's `--engine-checks`:
```
tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 180 -GameArgs:'--sky-checks --load-radius=1'
```

All six hold on Godot 4.7.2 Vulkan (2026-10-10):

| Check | Got | Expected |
|---|---|---|
| Opaque raw sky colour reaches the screen unchanged | (204, 102, 51) | (204, 102, 51) |
| Black Mix at 0.7 over it blends in byte space, in priority order | (61, 31, 15) | (61, 31, 15); linear blending would give (119, 85, 59) |
| An opaque world surface covers the sky; the after pass (depth test off) adds over it | byte space (default since wave 3): (76, 178, 76); `--blend-space=linear`: (59, 155, 59) | (77, 179, 77); linear (59, 155, 59) |
| The after pass adds over the sky background | byte: (87, 56, 41); linear: (67, 42, 33) | (87, 56, 41); linear (68, 42, 33) |
| A `Color` on a `vec4` parameter arrives linearised (why raw colours go as `Vector4`) | (81, 81, 81) | (81, 81, 81) |
| Known deviation, pinned: grey 0.6, AddOne white, black Mix 0.7 do not saturate in between (§8) | (122, 122, 122) | (122, 122, 122); retail shows (77, 77, 77) |

The core's `--engine-checks` still pass (9/9).

**xUnit** (`tests/Lifestone.Client.Tests/Sky/`, 33 tests):
- `DerethTimeControllerTests`: live, pause and time-lapse; jumps; clamping.
- `SkyMaterialTests`: retail material rules; the Region's surfaces map to the expected variants.
- `SkyObjectTrackerTests`: lifetimes, sticky lighting, hiding, the weather option, the fog override, placement; day group 0 from noon to night; rainy passes.
- `SkyDirectorTests`: the cadence on the real Region, refreshes with a paused clock, forced groups, model lists, scroll; every frame equal to `DerethSky.Evaluate` over three days and their boundaries; no allocation at all in a minute at the retail rate, and at 600x only on frames that create or delete an object or start a day.
- `SkyObjectEvaluatorTests`: the allocation-free `SkyObjectEvaluator` gives the same bits as `SkyEvaluator.GetSky` for all 20 day groups at keyframes, window edges and 2,001 times each; the clock start rule (automated: Sunny day, paused, at `--time-of-day`; interactive: live, speed 1).

## 7. Performance

### 7.1 The hitches and their cause

A review measured main-thread stalls of 20-53 ms while the clock ran: 5-6 frames over 20 ms per 600x flight, two runs over the 50 ms DoD limit, and GC frames up to 41 ms at 1x. A paused clock and `--sky=off` never showed them. Every one coincided with a gen-0/gen-1 GC, and the sky's own stopwatch saw them inside its update with nothing changed. This branch's earlier note blamed contention from other agents; alternated runs on the same machine did not support that.

The cause was garbage the GC is slow to collect:
- **String-named Godot calls.** `ShaderMaterial.SetShaderParameter("emissive", ...)` and `RenderingServer.GlobalShaderParameterSet("ac_fog_color", ...)` convert the `string` to a new `StringName` on every call. Each one is a finalizable object with a weak-reference registration (GodotSharp's `DisposablesTracker`) and a native string. While the clock runs, the cloud scroll and the keyframe-lerped luminosity and diffusion change every frame, so this was dozens per frame. At 600x the light and fog ticks fire nearly every frame too, with eight more.
- **The sky evaluation.** `DerethSky.Evaluate` built a `SkyState`, an object record per sky object, six arrays and the calendar date strings every frame: about 1.5 KB.

### 7.2 What changed

- **Cached names.** The per-frame calls take `static readonly StringName`s: `SkyRenderer.SurfaceInstance` for `vertex_alpha`, `emissive`, `diffuse` and `uv_offset`; `RenderGlobals.ApplyLighting`/`ApplyFog` for the `ac_*` globals. The last written values are plain fields.
- **Fewer writes.** While the clock runs, a surface's light parameters are re-written only once they have moved by half a byte step (0.5/255), and the scroll offset by 1/4096 of the texture. A standing clock writes the exact values (§8).
- **No garbage from the evaluation.** `SkyDirector` evaluates exactly what `DerethSky.Evaluate` does without allocating:
  - the day group is cached for its Dereth day;
  - `present_time_of_day` comes from `DerethCalendar.DayFractionAt`;
  - the objects go through `SkyObjectEvaluator`, `GetSky`'s arithmetic into re-used buffers, bit-identical to `SkyEvaluator.GetSky` (tested);
  - light and fog are read only on their ticks;
  - one `SkyFrame` is re-used, and its calendar `Date` is built only when the UI or a log asks.
  
  Only a new day (one calendar lookup) and a change of the drawn objects (the scroll registry's sync) allocate. `SkyDirectorTests` asserts zero bytes over a minute at 1x.
- **The time panel** reads the date strings ten times a second, not every frame.
- **A probe.** The sky counts what its per-frame update allocates and logs it at exit (`[sky] per-frame allocations`). With `--log-level=debug` it names every steady frame that allocated.

### 7.3 Measurements after the fix

`--fly-path=holtburg:arwic:150 --vsync=off --max-fps=0 --objects=statics,buildings --time-speed=1|600 [--sky=off]` (measured with the statics smoke layer, `--smoke-statics`, retired in M1 wave 3; `--objects=statics,buildings` is its nearest equivalent), placeholder terrain, RTX 5090, Vulkan, 1280x720, 2026-10-10. The four configurations were run in turn, three rounds, the same flags as the review. "GC frames" are frames in which a GC count rose (the per-frame CSV).

| Run (3 each) | Avg | p99 | Max | GC frames | GC-frame max | GC frames > 20 ms | Frames > 50 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| Sky on, 1x | 0.48-0.69 ms | 0.83-1.06 ms | 6.4-32.0 ms | 41-46 | 5.0-6.1 ms | 0 | 0 |
| `--sky=off`, 1x | 0.44-0.45 ms | 0.75-0.83 ms | 6.0-39.9 ms | 30-40 | 4.4-7.5 ms | 0 | 0 |
| Sky on, 600x | 0.49-0.76 ms | 0.81-1.48 ms | 7.2-47.3 ms | 38-45 | 5.9-7.2 ms | 0 | 0 |
| `--sky=off`, 600x | 0.43-0.45 ms | 0.72-0.81 ms | 6.3-43.4 ms | 29-38 | 5.1-8.8 ms | 0 | 0 |

Before the fix, with the same flags: sky on at 600x had a GC-frame max of 47.6-53.0 ms and 5-6 GC frames over 20 ms per run; sky on at 1x had GC frames up to 23-41 ms.

- **GC frames now cost the same with the sky as without** (under 9 ms), and no run has a frame over 50 ms.
- **The few frames over 15 ms are not the sky's.** Each run has 0-5 of them. None coincides with a GC, a main-thread drain, an attach or a rebase, and they appear with `--sky=off` as often as with the sky on: OS scheduling or presentation.
- **The sky view costs 0.04-0.06 ms per frame on average** in like-for-like runs (1x: 0.479 against 0.438 ms). Two sky-on runs averaged 0.69 and 0.76 ms; their GC frames were as short as the others'.
- **What the sky still allocates** (the probe, 17 flights, 52,000-92,000 frames each):
  - **Objects created or deleted, or a new day group:** 41-46 frames per 600x flight, up to 20 KB each (the scroll registry's sync, the model preload). None at 1x within a flight.
  - **Steady frames:** 1-5 per flight, all small or one-off. 24 B on the scroll registry's first step (its `Dictionary` value collection). About 32 B once a run, most likely .NET's `DateTime.UtcNow` refreshing its leap-second cache (the live-clock offset reads it every frame). And in some runs one or two frames of about 8 KB (8,072-8,104 B), with `--sky=off` too, never in a frame with a GC; the probe could not attribute them to sky code.
  - None of it is finalizable, which is what made the old GCs slow.

## 8. Deviations and open points

- **The sky anchor** (§4.2). Retail's anchor is what the decompile says. Since wave 3 the default follows the camera's x and y (a glitch-fix deviation); `--retail-sky-anchor` restores retail.
- **Time-lapse and the slider** are viewer tools. At speed 1 with no jumps everything is retail.
- **Weather objects are re-pinned every frame.** Retail re-pins them in `GameSky::UpdatePosition` whenever `SmartBox::set_viewer` runs. Between calls, `GameSky::UseTime` gives them object 0's origin (11.6096 shows the same `sky_obj[0]`), which a stationary retail viewer might briefly see.
- **The after pass blends in the main view's blend space**: byte space by default since wave 3, linear with `--blend-space=linear` (render-contract.md §2.3).
- **No saturation between sky blends.** Retail draws into an 8-bit framebuffer, which clamps to 255 after every blend. The sky view is a float (RGBA16F) buffer, which does not clamp until the background shader does at the end. Where an additive surface pushes the colour above 1 and a later Mix surface blends over it, Lifestone blends against the unclamped value:
  - the cases are the sun (AddOne) over the luminosity-1 dome and the moon glow;
  - on Cloudy and Rainy days the clouds draw after the sun (§5), so the sun shows through them brighter than in retail. A dome of 0.6 plus a sun core of 1.0 under a cloud with alpha 0.7 and colour 0.5 gives 0.83 here and 0.65 in retail;
  - `--sky-checks` pins it (grey 0.6, AddOne white, black Mix 0.7: 122 here, 77 in retail). If that check ever fails, the buffer saturates and this note can go;
  - the viewport's buffer format is not the fix: Godot 4.7's Forward+ blends 3D draws in its float internal buffer whatever `UseHdr2D` says. With `UseHdr2D` off, the same check reads 184, the sRGB encoding of 0.48; a clamping buffer would give 149, the encoding of 0.3 (measured 2026-10-10);
  - TODO(sky): decide whether to emulate the clamp, for example by clamping each additive surface against its known destination, or by splitting the sky view's draw where an additive surface is followed by a blended one.
- **Sky material parameters lag by at most half a byte step while the clock runs.** Each surface's light parameters are re-written only once they have moved by 0.5/255, and the scroll offset by 1/4096 of the texture (§7). A standing clock writes the exact values, so paused views and screenshots are exact.
- **Indoors.** Retail draws the landscape and sky only while the viewer's cell is outdoors (`SmartBox::RenderNormalMode`). Done by the interiors: in a cell that is not seen outside, `InteriorController.SetLandscapeHidden` culls everything but the cells (the sky objects included) and clears to black (interiors.md §3).
- **Server overrides.** The environment fog override (`LScape::m_override_enabled`, `Handle_Admin__Environs`) and "always daylight" (`LScape::SetDay`) are M2 and later. The tracker already honours the override flag.
- **Scroll precision.** Retail's scroll offset runs unbounded for negative velocities. At 600x for hours the float accumulator loses precision (0.002 texture units after about an hour); at retail speed it is irrelevant.
- **Requests to the core:** see the report. They are the `RenderGlobals.ApplyLighting`/`ApplyFog` additions, the `MaterialFactory` `Vector4` fix, and the terrain layer wiring, which this branch leaves to the lead.
