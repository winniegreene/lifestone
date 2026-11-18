# Hero video

**What:** a short trailer of Lifestone rendered from the user's own dats: Holtburg at dawn, morning, noon and dusk,
Arwic, Yaraq, Shoushi, Glenden Wood, the snowfields north-west of Holtburg, three dungeons and three spells, with a
title card, lower-third captions, crossfades and an end card. One command builds, records and edits it.
**Code:** `src/Lifestone.Client/Cinematic/` (engine-agnostic shot, spline and easing math, tested by
`tests/Lifestone.Client.Tests/Cinematic/`), `src/Lifestone.Game/scripts/Cinematic/` (the directors and the frame
recorder), `src/Lifestone.Game/cinematics/hero.json` (the shot list), `tools/hero-video.ps1` (record and edit).
**Hooks outside them** (each one commented "hero video tooling"): `Main.cs` (one call after the sky track),
`SpellViewer/SpellViewer.cs` (create the spell director, use its step, finish with it), `OptionCatalog.cs` (the
`Cinematic` group).

## 1. Running it

```
pwsh -File tools\hero-video.ps1                       # build, record every shot at 1920x1080/60, edit, extras
pwsh -File tools\hero-video.ps1 -NoBuild -QaSheets    # same, plus review sheets (a frame every 0.5 s)
pwsh -File tools\hero-video.ps1 -NoBuild -Only c-holtburg-street,k-holtburg-redoubt   # re-record two shots, re-edit
pwsh -File tools\hero-video.ps1 -NoBuild -SkipRecord  # re-edit only (captions, cards, timing of the crossfades)
pwsh -File tools\hero-video.ps1 -NoBuild -Stills      # framing stills of every world shot, no film
pwsh -File tools\hero-video.ps1 -Resolution 1280x720 -Fps 30   # a quick draft
```

Outputs (gitignored):

| File | What |
|---|---|
| `docs/screenshots/hero/lifestone-hero.mp4` | The film: H.264 High, yuv420p, BT.709, CRF 18, `+faststart`, no audio |
| `docs/screenshots/hero/lifestone-hero-720p.mp4` | 1280×720 for sharing, two-pass to stay under 25 MB |
| `docs/screenshots/hero/lifestone-hero-poster.png` | A poster frame (the list's `poster`: a shot and a moment in it) |
| `docs/screenshots/hero/lifestone-hero-contact.png` | Contact sheet, a frame every 2 s |
| `cache/hero/segments/<shot>.mkv` | Each shot, near-lossless RGB (libx264rgb CRF 8), reused by `-SkipRecord` / `-Only` |
| `cache/hero/logs/` | Each Godot run's and each ffmpeg step's output; `edit.filtergraph` is the edit |
| `cache/hero/qa/qa-NN.png` | With `-QaSheets`: frames every 0.5 s, 20 per sheet, time-stamped |

The game options behind it (registered in `OptionCatalog.Cinematic`; main scene and spell viewer):

| Option | Effect |
|---|---|
| `--cinematic=FILE` | Film the shots of a shot list (relative to the repository root), then quit (ends the run, so the run is automated). Run with the engine's `--fixed-fps` |
| `--cinematic-shot=ID[,ID]` | Only these shots; the main scene films world shots, the spell viewer spell shots |
| `--cinematic-capture=DIR` | Write each recorded shot's frames to `DIR/<shot>.rgba` (raw RGBA8 at the window's size) with a `.json` sidecar |
| `--cinematic-stills[=DIR]` | Instead of filming, save PNG stills of each world shot (default `docs/screenshots/hero/stills`) |
| `--cinematic-stills-at=F[,F]` | Where in each shot the stills are taken, as fractions of its length (default `0,0.5,1`) |

Each shot is its own Godot run (through `tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds`, so the
window never shows and a hung run is killed); a failed shot is retried once. Only the user's ffmpeg on `PATH` encodes,
and only our renders; the title font is read from `C:\Windows\Fonts` (Bahnschrift, else Segoe UI or Arial).

## 2. How a shot is filmed

```
hero.json ──► ShotList.Parse (Lifestone.Client)                 tools\hero-video.ps1
               │                                                  │ one run per shot: --fixed-fps 60, -Resolution 1920x1080,
               ▼                                                  │ --cinematic=hero.json --cinematic-shot=ID
  CinematicDirector (main scene) / SpellCinematic (spell viewer)  │ --cinematic-capture=cache/hero/raw
   1. clock: day group, start time (paused)                       ▼
   2. jump there (indoor starts through the interiors)       cache/hero/raw/ID.rgba (+ .json) ──► segments/ID.mkv
   3. black cover until complete + sky built                       │
   4. pre-roll (living statics get going)                          ▼
   5. record: per frame pose(t = i / fps), clock, capture      edit: captions, title, end card, xfade ──► mp4
```

- **Deterministic.** The engine is stepped by `--fixed-fps` (every frame's delta is exactly 1/60 s, whatever the wall
  time), and the camera's pose for frame *i* is `CameraPath.Sample(i / fps)`, a pure function of the shot. Living statics
  (torches, smoke, birds) and the sky advance by the same fixed delta, so a re-render gives the same film.
- **Waiting.** Before recording, the director waits under a full-screen black cover until
  `CaptureReadiness.IsComplete` (streamer idle, objects ring idle, every expected EnvCell attached, main-thread queue empty;
  the screenshot and golden-viewpoint rule) and `SkyController.IsReady`, then runs the shot's `preroll` (default 2 s here)
  so chimney smoke and torch flames are under way. None of these frames is captured. The log's
  `[cinematic] recording` and `shot recorded ... first_frame= last_frame=` lines give each shot's frames.
- **Capture.** `FrameRecorder` reads the viewport after each recorded frame is drawn (`RenderingServer.FramePostDraw`)
  and a writer thread appends it, raw RGBA8, to `cache/hero/raw/<shot>.rgba`; a sidecar `.json` gives the size, rate
  and frame count, which the script checks. About 10 ms a frame at 1920×1080 (measured).
- **Why not Movie Maker's files.** Godot 4.7's `--write-movie` (verified with `--help`: `--write-movie <file>`, which
  forces `--fixed-fps`, `--disable-vsync` speeds it up) records at the *project's* viewport size, 1280×720 here, whatever
  `--resolution` says (the window was 1920×1080, the PNGs 1280×720), writes every frame of the run (loading included),
  and PNG-compresses on the main thread: 230 ms a frame measured, 23 times the recorder's. The directors still work under
  Movie Maker (they detect its `movie` feature tag): `first_frame`/`last_frame` are Movie Maker's own frame numbers, and
  `tools\hero-video.ps1 -MovieMaker` records that way and trims with `ffmpeg -start_number <first_frame> -i f%08d.png
  -frames:v <frames>` (checked on a two-shot list: both trimmed segments open and close on scene frames, mean luma 73-83,
  where the cover around them is 0). The script also warns when any segment opens on a black frame.
- **The clock.** Each world shot forces its `dayGroup` (default 0, Sunny; forced so a time-lapse past midnight keeps
  the weather) and starts at its `time` on the first day from Morningthaw 1, 10 P.Y. whose retail day group is that
  group (as the golden viewpoints). It runs at `timeSpeed` (default 1) or, with `timeEnd`, at the rate that reaches
  that day fraction at the end of the shot. Light and fog are refreshed every frame (`SkyDirector.RefreshNow`; retail
  refreshes them every 15 s and 0.8 s of client time), so a time-lapse dims smoothly. A speed of exactly 1 would put
  the clock in retail's live mode (it then follows the real clock, which a frame-stepped recording must not), so it
  runs at 1 + 1e-9.
- **The camera** is the main scene's `FlyCamera` with input off, placed every frame (`PlaceAt`, field of view on its
  `Camera3D`). Outdoors the eye is kept `clearance` (default 2 m) above the exact ground; a path that comes closer is
  logged at the start of the shot (`the path comes closer to the ground ...`) and lifted while it plays. Indoor starts
  go through `Navigator.PlaceIndoors` (the cell known at once, the dungeon streamed indoor), and the interiors' cell
  tracker follows the eye through the portals as it moves.
- **HUD.** Every canvas layer is hidden (`NavigationUi.SetHudVisible`); `--cinematic` ends the run, so the game treats
  it as automated (no tool panels, the sky's fixed clock). The spell viewer's panels, caption and About layer are hidden
  the same way, and its chant label over the caster unless the shot sets `words`.
- **Anti-aliasing** is set for the run only on the viewport (`render.msaa`, default 4×; `screenSpaceAA` off, fxaa or
  smaa; `scale3d` above 1 supersamples), never in `project.godot`. TAA is kept off: it smears the moving camera and the
  particles.
- **Spell shots** run in `scenes/SpellViewer.tscn`: the viewer's stage advances by exactly the film's 1/60 s per frame
  (the showcase director's fixed step, here at 60 fps), the orbit camera follows the shot's keys (`OrbitTrack`), and the
  spell is cast at `castAt` (negative: during the pre-roll, so the film opens part-way through a long wind-up). The log
  gives each cast's timeline in shot time (`[cinematic] cast ... effect_shot_t=` and `first impact ... shot_t=`) for
  keying the camera. The viewer's time of day comes from `--time-of-day` at start-up, so the shot passes it in `args`.
- **Stills** (`--cinematic-stills[=DIR]`, `--cinematic-stills-at=F,...`, the script's `-Stills`): the same set-up, then
  the shot is played frame by frame (so streaming and the cell tracker follow the motion as in the film) and a PNG is
  saved at each fraction of it. This is how the shots below were framed.

## 3. The shot list

`src/Lifestone.Game/cinematics/hero.json`, parsed by `Lifestone.Client.Cinematic.ShotList` (every problem is reported
with its JSON path; unknown properties are errors, so a typo cannot pass silently; `ShotListTests` parses the committed
list and resolves every world shot's start).

| Level | Properties |
|---|---|
| list | `title`, `fps` (60), `width`/`height` (1920×1080), `render` {`msaa`, `screenSpaceAA`, `scale3d`}, `preroll`, `clearance`, `args` (game arguments for every run), `shots`; for the edit: `crossfade` (s), `font`, `titleCard` {`shot`, `title`, `subtitle`, `in`, `out`}, `endCard` {`duration`, `title`, `subtitle`}, `poster` {`shot`, `t`, `title`}; `note` |
| shot | `id`, `scene` (world, spell, or clip: for the edit only, see §9), `caption`, `start`, `duration`, `time` / `timeEnd` / `timeSpeed`, `dayGroup`, `fov` (60), `ease`, `timing`, `preroll`, `clearance`, `args`, `keys`; spell shots: `spell`, `fizzle`, `castAt`, `smooth`, `words`; clip shots: `source` (a captured take under `cache/`), `from` (its second); for the edit: `captionIn`, `captionOut`, `captionPosition` (bottom, top), `transition` (fade, fadeblack, cut); `note` |
| world key | `pos` [east, north, up] metres from the start location's spot, `ground` (up is above the ground there), `yaw`/`pitch` or `look` [east, north, up] (`lookGround`), `fov`, `t` |
| spell key | `t`, `yaw`, `pitch`, `distance`, `focus` (caster, target, mid, hands, projectile; resolved every frame), `offset`, `fov` |

- **`start`** is anything the teleport panel or `--start` knows: an ACE point-of-interest name (`Yaraq`,
  `Glenden Wood`), an interior key (`town-network`, `mines-of-despair`), a landblock (`7AC2`), a cell, optionally with an
  exact origin (`01630110@8,-50,-18.1`), or map coordinates. Indoors the anchor is the floor point and `pos` [0, 0, 1.7]
  is eye height there.
- **The path** (`CameraPath`) is a centripetal Catmull-Rom spline through the key positions. The facing is a look-at
  spline when every key has `look`, else monotone (no overshoot) heading and pitch curves through the keys, unwrapped
  so a pan takes the short way round. **Timing** `arc` (the default for a moving camera) moves at constant speed along
  the path; `uniform` gives each key segment equal time (pans from a fixed point); `keys` reaches each key at its `t`.
- **Easing** shapes the shot's progress: `linear`, `in`, `out`, `inOut`, `sine`, `smoother`, `ramp:A[,B]` (accelerate
  over the first A of the shot, glide, decelerate over the last B; `ramp:0.15,0` leaves moving into the next crossfade).

## 4. The edit (tools\hero-video.ps1)

- **Segments** are joined with `xfade` (`crossfade` 0.7 s; `fadeblack` 0.9 s into the dungeons, into the spells and
  into the end card); the film fades in from black over the title shot's first second.
- **Captions** (lower left, Bahnschrift 58 px at 1080p, soft shadow) fade in at `captionIn` (default 0.9 s) and out
  at `captionOut` (default one crossfade before the shot ends). Behind each caption sits a soft dark halo, so white
  text reads over snow, sand and bright sky: the caption drawn fattened (`borderw` 10 px) into a grey mask with the
  same position and fade, blurred (`gblur` σ 18) and scaled to 62 %, is the alpha of a black layer laid over the shot
  before the caption is drawn. Text goes through `drawtext` `textfile`s (UTF-8, no expansion), so apostrophes and the
  dashes and middle dots of the cards need no escaping.
- **Title card** over the dawn shot: `LIFESTONE` and the subtitle, fading in and out. **End card**: the wordmark, the
  stack line and "Next: logging in to Dereth" on near-black, fading out.
- **Colour:** the segments stay RGB until one conversion to BT.709 limited-range yuv420p, tagged as such, so players
  show the same colours the game rendered.
- **Extras:** the 720p version is two-pass at the bitrate that fits the size limit (at most 8 Mb/s), re-encoded smaller
  if it still does not fit; the poster is a clean frame of the `poster` shot (no caption) with the title card's text; the
  contact sheet has a frame every 2 s.

## 5. The shots

| # | Shot | Where (start, camera) | Time |
|---|---|---|---|
| a | Title: dawn tilt from the sky onto the lakeside houses | Holtburg, 200 m west of the drop point, 50 m up, heading 78° → 72°, pitch 16° → −7° | 0.245 → 0.255 |
| b | Holtburg: in over the lake, down onto the lakeside houses | from (240, 340) 45 m above the drop point to (30, 152) 13 m below it, about 20 m above the chimneys, always looking at (10, 122, −22); easing `out`, so it settles where the smoke draws | 0.32 |
| c | Along the cobbled road past a burning lamp post to a smoking chimney | 2 → 3 m above the road, from (14, 13) to (29, 13), looking from the lamp post at (22, 10.1) up to the chimney smoke at (39.8, 4.7) | 0.5 |
| d | Sunset time-lapse over the lakeside houses, tilting up into the stars | (150, 140, 25) → (132, 137, 23), heading 262° → 266°, pitch −1° → 23° | 0.83 → 0.925 |
| e | Arwic's walls and great tree | up the south road from 250 m out, 22 → 45 m above the ground | 0.42 |
| f | Yaraq and the mesas, late afternoon | 25 → 40 m above the desert, east over the domes towards the mesas | 0.84 |
| g | Shoushi's pagoda | rising arc 4 → 16 m above the ground, 40 m from the pagoda, field of view 50° | 0.45 |
| h | Glenden Wood forest road | 3 m above the road, 80 m down it towards the town | 0.42 |
| i | Snowfields north-west of Holtburg (0x7AC2), no caption | from 230 m to 170 m above the landblock's centre, 480 m south over the snow towards the rock spires | 0.36 |
| j | Town Network fountain hall | an arc in on the fountain from 14 m to 9 m | dungeon |
| k | Holtburg Redoubt | lower level, cell 0x01630110: a slow dolly from the passage's mouth into the cauldron room | dungeon |
| l | Mines of Despair | 19 m down the timbered entry tunnel | dungeon |
| m | Flame Bolt VI | from above the caster's right shoulder (heading 48° → 76°, pitch −27° → −29°): the wind-up, the bolt, the burst on the drudge | 0.83 |
| n | Cassius' Ring of Fire | craning up and back from the south-west (9 → 15.5 m, pitch −31° → −38°), the caster right of centre | 0.83 |
| o | Portal Recall | tight on the shoulders and hands from behind (2.6 → 3.3 m, field of view 32°), tilting up with the glows | 0.83 |

The spell shots look north-east, at the timbered house and the road, and down, so the spell viewer's stage edge (its
terrain ends about 480 m out, the fog colour beyond) never comes into frame; Portal Recall looks up at the house's
roof and the sky.

Server-spawned objects (lifestones, portals, doors, NPCs) arrive from ACE in M2, so the Town Network's portals and the
towns' lifestones are absent, as in every M1 view.

## 6. Measurements and known visual issues

**The draft** (this branch: master @32f400a plus the tooling; RTX 5090, Vulkan Forward+, Debug build, 2026-10-10):
`pwsh -File tools\hero-video.ps1 -QaSheets` took **217 s** from the build to every output (second draft, after the
trailer review).

| | |
|---|---|
| Film | 75.9 s, 1920×1080, 60 fps (4,554 frames), H.264 High, yuv420p BT.709, CRF 18, `+faststart`, no audio: **148.2 MB** (148,223,671 bytes, 15.6 Mb/s) |
| 720p | 1280×720, 60 fps, 2.49 Mb/s two-pass: **23.7 MB** (23,659,785 bytes; MB here and in the script's summary are 1,000,000 bytes) |
| Poster / contact sheet | 3.0 MB / 4.5 MB PNG |
| Recording | 136 s for the build check and 15 Godot runs of 6-12 s each (start-up, 0.4-1.4 s of streaming under the cover, 2 s pre-roll, the shot); capture 8-13 ms a frame at 1080p; each shot's raw frames (1.8-3.7 GB) are encoded and deleted at once |
| Edit | about 50 s (one pass over 15 segments: caption halos and drawtext, 15 xfades, x264 slow), then 24 s for the 720p two-pass and a few seconds for the poster and sheets |
| Movie Maker fallback | about 230 ms a frame at 720p; the two-shot test list took 54 s |

Shot starts in the film (s): 0 title/dawn, 6.8 lake, 13.1 street, 18.4 sunset, 23.7 Arwic, 29.0 Yaraq, 34.3 Shoushi,
39.1 Glenden Wood, 43.9 snowfields, 49.0 Town Network, 53.8 Redoubt, 58.1 Mines, 62.2 Flame Bolt VI, 65.1 Ring of
Fire, 68.2 Portal Recall, 70.9 end card.

**Seen in the draft (frames every 0.5 s), for the final master to check:**

- **Alpha-tested foliage shimmers.** MSAA does not smooth alpha-tested edges, so the pines of Glenden Wood and Arwic
  crawl a little as the camera moves. Options to try on the final master: `render.scale3d` 1.5-2 (supersampling; the
  interiors' 1.5 px doorway seam band is in output pixels, so check shot k's doorways), or `screenSpaceAA: "smaa"`.
- **Wall-torch glow rectangles.** The soft glow around a wall torch's flame shows as a faint lighter rectangle on the
  stone: the glow sprite's quad (`0x32000141`) reads as a box against a dark wall. Shot k now starts past the doorway's
  torch, so only the cauldron room's torch shows it, small, on the far right wall (about x 1350-1440, y 400-540 at
  1080p); the Mines' torches (shot l) show it too. Compare with retail once byte-space blending and the detail textures
  are merged.
- **Chimney smoke** shows in shot b (grey puffs on the lakeside chimneys as the camera settles about 20 m above them)
  and shot c (a plume against the sky over the chimney at (39.8, 4.7)). The puffs are small and grey; check how they
  read with the final master's blending.
- **Birds are not in the film.** Holtburg's circling birds (`0x02000494`, `0x02000493`) live in the neighbouring
  landblocks at about lake level: in 0xA9B5 between 25 and 77 m west and 226 and 292 m north of the drop point, and in
  0xA8B4 about 265 m west; the "high birds" `0x020005AC` of 0xA9B3 stand in the forest 40-90 m south. Their wing parts
  are a metre or two long, so they only read from within a few tens of metres; probes from (36, −40) looking south and
  from (−10, 225) over the north shore showed none. A close shot on one of them is the way to add them.
- **Yaraq is late afternoon, not golden hour.** At 0.875 (the first draft) the stars were already out over a sunlit
  desert; retail's warm dusk light (the 0.9 keyframe, red fog) arrives with the stars layer, so no moment has warm light
  without stars. The shot is at 0.84 (daylight, a lower sun, no stars) and its caption claims no time of day.
- **The sunset time-lapse ends at 0.925**, tilting up 24° as it runs, so the last second is mostly stars and clouds
  over a thin band of fogged valley (retail's night fog is a flat dark maroon). No moon is in frame.
- **Arwic's road**: the camera starts low between two pines on the south road; the first half second has a pine close
  on the left. Acceptable, but a slightly higher first key would read cleaner.
- **The spell stage** is the spell viewer's (5×5 landblocks around Holtburg, its own sky at 0.83). Its terrain ends
  about 480 m out with the fog colour beyond (docs/spell-viewer.md §2), so every spell shot looks down at the town or
  up at a roof, never at the horizon. The caster wears only its Setup's underwear (ObjDesc clothing is M2), so the
  shots favour the effects: Flame Bolt from above the caster's right shoulder (the wind-up turns his chest towards the
  camera for about half a second; keying the camera further behind him did not avoid it), Portal Recall tight on the
  shoulders from behind.
- Every M1 limitation shows: no server objects (portals, lifestones, NPCs), no characters other than the spell viewer's,
  no audio.

**On the final master** (after the remaining M1 fixes are merged): `pwsh -File tools\hero-video.ps1 -QaSheets`, then
read `cache\hero\qa\qa-*.png`. The tooling depends only on stable APIs (`FlyCamera`, `WorldRoot` through `Navigator`,
`TeleportCatalog`/`InteriorLocations` names, the sky controller's clock, `CaptureReadiness`, the spell viewer's stage and
camera, `NavigationUi.SetHudVisible`, the option registry). If framing changes with the merged features (new statics,
different terrain normals), `-Stills -Only <ids>` re-checks the world shots in seconds.

## 7. Files

| File | Role |
|---|---|
| `Lifestone.Client/Cinematic/Vec3d.cs` | Double-precision vector for paths |
| `Lifestone.Client/Cinematic/Easing.cs` | Easing curves and their parser |
| `Lifestone.Client/Cinematic/Splines.cs` | Centripetal Catmull-Rom, monotone cubic (PCHIP) |
| `Lifestone.Client/Cinematic/CameraPath.cs` | World camera path: positions, facing, field of view, timing |
| `Lifestone.Client/Cinematic/OrbitTrack.cs` | Spell-viewer orbit camera with a follow filter |
| `Lifestone.Client/Cinematic/ShotList.cs` | The shot list model, parser and checks; `TimeLapse` |
| `Lifestone.Client/Cinematic/ShotPaths.cs` | Keys to paths (anchor-relative and ground-relative), clearance check |
| `Lifestone.Game/scripts/Cinematic/CinematicOptions.cs` | `--cinematic`, `--cinematic-shot`, `--cinematic-capture`, `--cinematic-stills`, `--cinematic-stills-at` |
| `Lifestone.Game/scripts/Cinematic/CinematicSetup.cs` | The main scene's one call |
| `Lifestone.Game/scripts/Cinematic/CinematicDirector.cs` | World shots |
| `Lifestone.Game/scripts/Cinematic/SpellCinematic.cs` | Spell shots in the spell viewer |
| `Lifestone.Game/scripts/Cinematic/CinematicView.cs` | Shot list loading, capture settings, the cover, log lines |
| `Lifestone.Game/scripts/Cinematic/FrameRecorder.cs` | Raw frame capture |
| `Lifestone.Game/cinematics/hero.json` | The hero shot list |
| `tools/hero-video.ps1` | Record and edit |

## 8. M3 live takes

The M3 hero video (M3 4.2 h) is filmed at 800x600 on the live backend (D15, D57). The offline preparation and the live takes
use the scripts under `cache/m3/4-2/hero` (ignored by git): `capture.ps1` films the takes, `edit.ps1` cuts them, `measure.ps1`
measures the durations and the A/V sync. The capture flashes the in-band clock once so the edit can find the start of each take. `beats.json` holds the anchors of the story: the cast landed, the
cast confirmed (the projectile), the fight is won, the death (the tuskers killed the player) and the regear done; the anchors are
the legs' own log lines (`docs/session.md` §30 to §32). `audioOffsetMs` is the measured offset between the recorded sound and the
picture, applied by the edit; the edit is at most 30 s of action (m3-plan §4.11) and its sync is measured before it is sent.

## 9. M4 live takes and clip shots

The M4 hero video (the hero step after M4 4.2; D32: at most 30 s, the camera only as retail allows, real spawns only) is cut from live
takes of the three hero journeys (`--journey=hero-archer|hero-brawler|hero-mage`, `docs/session.md` §41). The journeys' frame bursts
(`--journey-shots`) cannot give motion at 1920x1080: saving one frame stalls the game about 0.5 s. So each take runs as M3's did: the
game in an on-screen borderless window at a known place (not `-Offscreen`, which moves it off the screen), muted with
`--audio-driver Dummy`, captured at 60 fps (the first round by a screen-region ddagrab after a clock flash; since 2026-11-16 only by
window capture, below); `--journey-film` gives the hero camera (each station's
turn and zoom through retail's chase camera) and saves no frame, and the log's `showpiece '<event>'` lines place the moments in the
take. The take scripts live under `cache/m4/hero` (ignored by git): `capture2.ps1` films one journey (the screen-region `capture.ps1`
is retired and refuses to run), `calibrate.ps1` finds the flash, `run-takes.ps1` runs the backend and the takes, `events.ps1` lists a
take's events at their video times.

A **clip shot** (`"scene": "clip"`) records nothing: `source` names a take under `cache/` and `from` the second it starts at, and the
composer encodes its `duration` at the list's fps and size into the same near-lossless segment a recorded shot gets, so captions,
crossfades, the cards, the 720p and the poster are unchanged. A clip list is for the composer only (the game's `ShotList` reads world
and spell shots). The M4 list is `cache/video/m4-hero/m4-hero-shotlist.json`; the cut is 29.87 s (seven clips, the store joined by hard cuts). It has no text on screen (the owner's word): a list with no `titleCard`, captions or `endCard` fades in on its first shot and out on its last.

**Window capture (the second round).** A screen-region capture films whatever covers the game window; a long take (the mage's death and
re-buff, 12 minutes) caught other windows. `cache/m4/hero/capture2.ps1` records the game window itself with ffmpeg's `gfxcapture`
(Windows Graphics Capture, the window's handle once it has been stable for 5 s), so a window on top does not reach the take; the game's
`--video-sync-marker` (one white frame, logged with its unix time) aligns the take with the log. `run-takes.ps1` always uses it.

**Every recording is a window capture** (the owner's ruling, 2026-11-16: all video recording works that way, especially the hero
camera). A take, a soak's `-Record` and any one-off recording read the game window itself through `tools/WindowCapture.psm1`
(`Find-LifestoneGameWindow`: the Godot "Engine" window, matched by a text of its command line and stable for 5 s;
`Get-WindowCaptureInput`: ffmpeg's `gfxcapture` input at a constant frame rate, no cursor, no border). Never gdigrab or ddagrab: they
film whatever covers the window. It needs ffmpeg 8 or later on PATH; a minimised window gives no new frames.

**The climb records too** (2026-11-17): `tools/climb.ps1 -Record [-RecordDir <dir>] [-RecordSegmentSeconds 3600]` records step 3, the level
up run, the way `soak.ps1 -Record` does: the shared recording functions in `tools/WindowCapture.psm1` (`Test-WindowCaptureFfmpeg`,
`Start-WindowRecording`, `Stop-WindowRecording`, `Get-WindowRecordingFiles`), segmented MPEG-TS `rec-NNN.ts` plus `segments.csv`,
`record.txt` and `ffmpeg.log`, and `--video-sync-marker` in the game arguments. Steps 1 and 2 are headless and are not recorded. The
folder is a launch argument only (default `<OutDir>ecord`). Without ffmpeg 8 or later the script refuses before anything starts;
`-PlanOnly` prints the recording plan with a placeholder for the window handle.

The M4 list's stats shot is an edit crop (1.67x) of a take made with ffmpeg before the composer, so the attribute numbers read at 720p.
