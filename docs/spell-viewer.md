# Spell viewer (M1)

**Track:** spell viewer: `src/Lifestone.Game/scripts/SpellViewer/`, `scenes/SpellViewer.tscn`,
`src/Lifestone.Game/scripts/Render/Particles/`, `src/Lifestone.Client/Effects/`, `tests/Lifestone.Client.Tests/Effects/`.
**Built on:** the effects track's simulation ([spell-visuals.md](spell-visuals.md)), the game core's render path
([render-contract.md](render-contract.md), [game-core.md](game-core.md)) and the terrain renderer
([terrain-renderer.md](terrain-renderer.md)).
**Authority:** retail behaviour is cited by function name from the Mag-nus/acclient-AI-RE decompile of acclient
11.4186; nothing from it is copied. Sources: [third-party/spellviewer.md](third-party/spellviewer.md).

An Aluvian male stands on the real terrain of Holtburg facing a Drudge Skulker 12 m away. Pick any of the 6,266 spells of
the user's SpellTable and he casts it: the words, the scarab wind-ups and talisman gesture from his MotionTable, the
hand glows the animations create, the caster and target effects, and war projectiles that fly, trail fire, smoke or
sparks, and explode on the drudge. Everything on screen comes from the user's dats (plus the small ACE world extract for
projectile weenies and the drudge, docs/spell-visuals.md §1).

## 1. Running it

All commands from the repository root, PowerShell 7. `tools\godot.ps1` builds first.

| What | Command |
|---|---|
| Open the viewer (window) | `pwsh -File tools\godot.ps1 -Run -GodotArgs 'res://scenes/SpellViewer.tscn'` |
| Same, through the main scene | `pwsh -File tools\godot.ps1 -Run -GameArgs:'--scene=spellviewer'` |
| Open it casting a spell | `pwsh -File tools\godot.ps1 -Run -GodotArgs 'res://scenes/SpellViewer.tscn' -GameArgs:'--spell=85'` |
| Stills of one spell at chosen times after the cast (automated) | `pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 240 -Resolution 1280x720 -GodotArgs 'res://scenes/SpellViewer.tscn' -GameArgs:'--spell=85 --no-ui --camera=0,-8,11 --shots=2.9,3.3,3.6 --screenshot=docs/screenshots/m1/spellviewer/bolt'` → `bolt-2.90.png`, … |
| Record the showcase (Movie Maker, PNG frames at a fixed 30 fps) | `pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 1200 -Resolution 1280x720 -GodotArgs 'res://scenes/SpellViewer.tscn --write-movie <repo>/docs/screenshots/m1/spellviewer/frames/f.png --fixed-fps 30' -GameArgs:'--showcase'` |
| Encode the video (user's ffmpeg, local files) | `ffmpeg -framerate 30 -start_number N -i docs/screenshots/m1/spellviewer/frames/f%08d.png -c:v libx264 -pix_fmt yuv420p -crf 18 -preset slow -movflags +faststart docs/screenshots/m1/spellviewer/showcase.mp4`, with N = the "showcase starts" frame of the log minus 1 (Movie Maker numbers frames from 0) |

Exit codes: 0 success; 1 failure (no dats, an exception in a frame, a screenshot not saved, or a viewer option whose
value does not parse in an automated run: `--shots=2.9,abc` fails at once instead of running to the timeout); 124 when
`godot.ps1 -TimeoutSeconds` kills the run. In an interactive run a bad value is logged and the option keeps its
default. GameServices also checks the command line against the option registry (game-core.md §6): the viewer's own
arguments are known there, and a main-scene option given to the viewer (`--start`, `--fly-path`) is reported as
ignored (an automated run with one quits with exit code 1).

### Options (`SpellViewerOptions`)

| Option | Effect |
|---|---|
| `--scene=spellviewer` | From the main scene: open the viewer (or start Godot on `res://scenes/SpellViewer.tscn`). |
| `--spell=ID`, `--fizzle` | Cast this spell (fizzling) once the scene is ready, and select it in the list. |
| `--speed=X` | Playback speed 0.25–2 (the slider). |
| `--showcase`, `--showcase-only=a,b` | Cast the showcase (or the listed indices) with a scripted camera, then quit (§5). |
| `--screenshot=PATH`, `--shots=T1,T2,…`, `--frames=N` | With `--shots`: save `PATH-<T>.png` for each time after the cast, then quit. Without: one PNG after N frames. |
| `--fixed-step` | Advance the stage by exactly 1/30 s per frame (implied by `--shots` and `--showcase`). |
| `--camera=YAW,PITCH,DIST`, `--focus=caster\|target\|mid` | Start view: compass heading looked along, pitch, distance, and what to orbit. |
| `--no-drudge`, `--no-statics`, `--no-ui`, `--seed=N` | Hide the drudge (still the target), skip Holtburg's objects (statics, buildings, scenery), hide the panels, random seed. |

GameServices' options apply too: `--time-of-day=F` (default 0.5, retail noon), `--max-mip`, `--dxt`, `--dat-dir`, `--vsync`,
`--max-fps`, and the render and objects options the viewer shares with the main scene: `--blend-space`, `--detail`,
`--retail-normals` and `--static-effects` (game-core.md §6).

### Controls

- **Spell list** (left): type a name, an id prefix or a school ("war", "life", "creature", "item", "void"); double-click
  or **Cast** casts the selection; **Fizzle** makes it fizzle; **Replay** (or `R`) casts the last spell again.
- **Showcase** buttons: the 15 spells of docs/spell-visuals.md §5 and a fizzle.
- **Speed** slider 0.25×–2×, **Pause** (or `Space`), toggles for the drudge, Holtburg's buildings and particles.
- **Camera**: hold the right mouse button and drag to orbit, wheel to zoom (1.5–45 m), middle drag to raise or lower
  the focus, arrow keys orbit, `Home` resets.
- **About** (`F1`, or "About [F1]" beside the spell list): the licence, no-warranty, source and credits notices
  (docs/navigation.md §5).
- **Info** (top right): FPS, particles drawn and their cost, draw calls, the spell's words, gestures, caster/target
  PlayScripts with intensities, projectile kind/Setup/speed. **Log** (bottom right): the simulation's events, with the
  chant as `Aluvian Male says, "Zojak Quaguz"`. The words also float over the caster's head for 3 s (a viewer aid;
  retail shows them in the chat window).

## 2. How it works

```
SpellStage (Lifestone.Client, engine-agnostic)                     SpellViewer (Godot)
  idle caster + drudge (MotionTable default state)                    TerrainRenderer: 5×5 landblocks around 0xA9B4
  Cast(id, fizzle) → SpellCastSimulation (Lifestone.Data)             StaticObjectsView: Holtburg's 3×3 objects (objects.md §2.5)
  Advance(dt)      → actors, projectiles, effect objects              ActorView × (caster, drudge, projectiles)
  Actors           → (role, Setup, EffectActor, hidden)  ─────────►     ActorPartDraws → MeshInstance3D per part
  EffectObjects    → particles                           ─────────►   ParticleRenderer
                                                                        ParticleDrawList → MultiMesh per batch
```

- **Space.** The stage is landblock 0xA9B4's local AC frame; the Godot origin is its south-west corner. The caster
  stands at (93, 21) on the open ground in the south of Holtburg, facing east; the drudge at (105, 21) facing west
  (`StageSite`). The cast line runs 4.0 m south of the timbered house (building 0x01000C17 at (107.5, 36), whose south
  wall lies on y = 25) and 3.7 m north of the lamp post (Setup 0x020005D8 at (106, 17.2)).
  `StageSiteTests.The_stage_is_clear_of_Holtburgs_buildings_and_statics` poses every LandBlockInfo building and static
  of the 3×3 landblocks as the objects path draws them (`LandblockObjectSet`, objects.md §1.3) and checks that the caster, the drudge and the straight cast
  line keep at least 2 m of horizontal clearance from all of their drawn triangles between 0.5 m below and 3.5 m above
  the ground (measured: 3.68 m, to the lamp post's base; the caster is 10.3 m from the nearest wall), and that no
  procedural scenery stands within 5 m of the line. (The first placement, (93, 25)–(105, 25), ran exactly along that
  south wall: the drudge stood in it and the bolts' last metres flew inside it; the test keeps that as evidence.)
  Heights are the exact retail ground (`TerrainSampler`).
- **Lighting and fog** are the render contract's global uniforms at retail noon (`RenderGlobals.ApplyDefaults`, day
  group 0, Midsong); the terrain material gets the same sky state (`TerrainRenderer.ApplySky`).
- **Sky** (`SpellViewerSky`, M1 wave-3 fix): the Region's sky objects (dome, clouds, sun) drawn by the sky track's
  `SkyRenderer` behind the stage, as the main scene draws them, at the same moment as the lighting: the first day from
  Morningthaw 1, 10 P.Y. whose retail day group is 0, at `--time-of-day` (default Midsong). The clock stands still; the
  cloud texture scroll follows the stage time, so fixed-step captures are reproducible. The objects are centred on the
  camera (the sky track's camera anchor, the M1 default; retail's landblock-corner anchor jumps at landblock edges).
  Before, the background was the flat fog colour, so particle glows blended over a colour they never meet in retail.
- **Below the horizon** (`SpellViewerSky.BelowHorizon`, wave-3 review fix): the stage terrain ends about 480 m out,
  and between its edge and the horizon the sky dome's lower part showed through, a pale band with cloud streaks and hard
  edges that the main scene's landscape always covers. A lower hemisphere centred on the camera (radius 2,500 m, its rim
  exactly at eye height) is drawn unshaded in the time of day's fog colour (`RenderGlobals.FogBackground`, the colour
  fully fogged landscape takes), so the strip reads as distant fogged ground. Measured with
  `--spell=85 --no-ui --camera=0,-8,11 --shots=3.3`, pixel (120, 280) in the strip and (450, 150) in the sky:

  | `--time-of-day` | Strip before | Strip after (= fog colour) | Sky above |
  |---|---|---|---|
  | 0.08 (night) | (153, 127, 169) | (23, 23, 37) | (18, 13, 21) |
  | 0.3 (morning) | (230, 225, 223) | (195, 200, 220) | (104, 126, 158) |
  | 0.5 (noon) | (229, 225, 222) | (195, 200, 220) | (105, 126, 159) |
  | 0.9 (dusk) | (212, 200, 226) | (90, 60, 60) | (52, 56, 72) |

  The pale strip under a dark night sky is gone; at noon the strip is the fog colour #C3C8DC, as fully fogged ground
  is. The stage terrain's edge still meets that colour (as it met the fog-colour background before the sky was added).
- **Actors** (`ActorView`, `ActorPartDraws`): one `MeshInstance3D` per Setup part, meshes from the shared render path
  (`RenderResources.AcquireMesh`, key `gfxobj:0x…:Object`), posed every frame from the actor's part frames through
  `AcToGodot.Transform`. LOD per part with retail's distances; the caster is the player's own character and always draws
  level 0 (`CPhysicsPart::UpdateViewerDistance`, `player_iid`). A projectile with neither a motion table nor a
  default animation shows its Setup's placement frame. Impacted projectiles and NoDraw objects hide their parts but keep
  their particles (ACE `ProjectileImpact`).
- **Particles** (`ParticleDrawList`, `ParticleRenderer`, §3): one `MultiMeshInstance3D` per (drawn particle GfxObj,
  part-material state); each instance is a 3×4 transform plus `INSTANCE_CUSTOM` = (1 − translucency, part-material
  flag). Materials are the surface's own (`MaterialFactory` parameters, `TextureCache` texture) with the particle variant
  of the contract's shader (`ParticleShaders`).
- **Warm-up.** Start-up compiles the 26 world surface variants (`RenderResources.WarmUp`) and every particle variant any
  of the dats' 342 particle GfxObjs can need, both part-material states (`ParticleMeshLibrary.WarmUp`, 26 variants,
  ≈ 150 ms), so no frame pays Godot's first-use shader compile. The caster's and the drudge's part meshes (61 GfxObjs,
  every LOD level the drudge can draw) are built in `_Ready` too (`ActorView.PreloadMeshes`). The 483 particle meshes
  are then prepared on the core `WorkerPool` (mesh conversion and texture decoding, at most 8 at a time) and finished on
  the main thread (materials, texture uploads) within 2 ms per frame (`ParticleMeshLibrary.PrewarmStep`); Holtburg's
  objects are prepared on the same pool. Terrain blocks are prepared on the pool while `_Ready` waits (loading phase).
- **Holtburg's objects** (M1 wave 3) are the world's objects path for the 3×3 landblocks around 0xA9B4
  (`StaticObjectsView`, objects.md §2.5): statics, buildings and scenery with retail LOD and camera-facing levels, and
  living statics (`LivingStaticsSystem` with the viewer's particle library: the lamp post 3.7 m from the cast line
  burns, chimneys smoke). Before wave 3 a render-contract smoke layer drew the statics and buildings at level 0, without
  scenery.

## 3. Particles: the retail rules

A retail particle is a `CPhysicsPart` of the emitter's particle object: `ParticleEmitter::SetInfo` makes
`max_particles` parts of the emitter's hardware GfxObj, and `Particle::Update` sets each part's frame, `gfxobj_scale`
(the particle scale) and translucency every update. The parts are then drawn like any part. Lifestone re-expresses
that drawing (`Lifestone.Client.Effects`):

1. **Viewer distance and heading** (`CPhysicsPart::UpdateViewerDistance`, `CPhysicsObj::UpdateViewerDistance`): to the
   part's sort centre scaled by `gfxobj_scale` and placed by its frame; when the particle object (the emitter position,
   `Frame::combine(parent part, parent offset)`) is at least 25 m away horizontally (`particle_distance_2dsq` at the
   steady-state degrade bias), all its particles take the object's distance and heading instead. A zero offset gives the
   heading (0, 0, 1).
2. **Level** (`GfxObjDegradeInfo::get_degrade` via `LodChain.Select`) at the LOD distance `CYpt / scale`. Particle
   GfxObjs have a billboard level 0 up to 64 or 128 m and an empty level after it, so a particle of scale 0.1 disappears
   beyond 11.4 m — as in retail.
3. **Draw frame** (`CPhysicsPart::calc_draw_frame`): mode 2 → `Frame::set_vector_heading(viewer_heading)` (local +Y
   along the view ray, no roll); modes 3/4/5 → `Frame::rotate_around_axis_to_vector(X/Y/Z)`; mode 1 → the part's
   frame. Particle quads lie in their local XZ plane with their front side facing local −Y, so a mode-2 particle shows
   its front to the camera. The showcase's armour shards (0x01001666–9) are mode 1 and keep their particles' orientation.
4. **Translucency** (`CPhysicsPart::SetTranslucency`, `CMaterial::SetTranslucencySimple`,
   `RenderDeviceD3D::SetCurrentMaterial`, `D3DPolyRender::SetSurface`): exactly 1.0 hides the particle; 0 draws it with
   the surface's own material; anything else gives it a part material whose alpha 1 − t *replaces* the surface's vertex
   alpha, and switches surfaces that do not already blend without an alpha test to SRCALPHA/INVSRCALPHA with no test and
   no depth write. Additive, Alpha and Translucent surfaces keep their factors; ONE/ONE surfaces ignore alpha, so their
   fade does not show (as in retail).
5. **Lighting and fog** are the surface's (render-contract §2.1): almost every particle surface has luminosity 1, so it
   shows its texel colour; Additive surfaces are not fogged.

The showcase draws 19 particle GfxObjs: 13 mode-2 billboard quads, the 4 mode-1 armour shards, and 2 meshes without a
degrade table (the lightning streak 0x01001732 and the ring arc 0x01001CB3), which keep their particle's orientation
(the rotating particle types take the parent's frame at birth, so they lie along the flight path or the ring). The
busiest moment has 256 live particles. `SpellShowcaseSmokeTests.Showcase_particles_are_camera_facing_quads` pins the
modes and the quads' XZ plane.

**Batches.** A batch is (drawn GfxObj, part-material state), but translucent particles get their own batch only when a
part material changes how one of the GfxObj's surfaces blends (`ParticleModel.PartMaterialSwitchesBlend`; in the
showcase only the frost flakes 0x0100112F, a ClipMap|Alpha surface). Every other particle shares its GfxObj's batch and
carries its alpha and part-material flag per instance.

**Why MultiMesh.** One `MultiMeshInstance3D` per batch costs one draw call per GfxObj sub-mesh whatever the particle
count (the quads have two sub-meshes, front and back), against two per particle with pooled `MeshInstance3D`s
(Flame Bolt VI peaks at about 200 particles in 3 batches: 6 draw calls instead of about 400). Collecting, billboarding
and uploading every batch costs 0.08 ms per frame on average (§6), so pooled nodes were not built.

## 4. What the showcase spells look like (and why that is right)

Colours come from the textures (`docs/textures.md`): no tint is applied anywhere.

| Spell | On screen | Data behind it |
|---|---|---|
| Wind-up (every non-FastCast spell) | blue-white star sparkles at the hands, and a pale blue glow ball growing through `MagicPowerUp*` | `CreateParticle` hooks in the cast animations (e.g. 0x030005A0 → 0x3200011E…): 0x010010F2 (a blue-white four-point star) and 0x01001101 (a light-blue glow), both SRCALPHA/ONE with luminosity 1 on R8G8B8 sprites (alpha 1, so the black border adds nothing) |
| Flame Bolt I/VI, Streak, Arc | orange fireball with a grey smoke trail; burst of fire on the drudge | 0x01000FF4 fire (additive, red-orange R8G8B8 sprite), 0x01000FBF smoke (SRCALPHA/INVSRCALPHA, a dark grey puff with a soft alpha); the bolt Setup 0x0200040D's only part is the invisible anchor 0x010001EC |
| Frost Bolt I | a spiky white ice ball with white-blue frost flakes | Setup 0x020003F4's opaque shard 0x0100112B (scale 0.4 → 0.5 by a Scale hook), frost 0x0100112F (ClipMap\|Alpha: alpha-tested, depth-writing) |
| Lightning Bolt I | violet crackling streak with sparks, a violet flash on the drudge; the bolt itself is an opaque lavender zig-zag ribbon with a white bolt drawn on it | particles 0x01001732 / 0x0100108E / 0x0100111A, additive violet sprites. The projectile model is Setup 0x020003F0's only part 0x01001116 (degrade chain 0x01001116 → 0x0100111C → nothing; level 0 is on surface 0x08000316, level 1 on 0x08000317): 0x08000316 is a **non-clip** Base1Image (translucency 0, luminosity 1: opaque, unlit) whose SurfaceTexture 0x05001685 holds the 128×64 INDEX16 RenderSurface 0x06003967, a white bolt on a uniform lavender backdrop, so the backdrop draws opaque — see §7 |
| Cassius' Ring of Fire | a ring of fire bursting out around the caster and fading after about 2 s, while the nine (invisible) projectiles fly on at 2 m/s | 9 × Setup 0x02000881 (only the anchor part; default script 0x330008C3: Scale to 2, then emitter 0x320004BC: arc particle 0x01001CB3, a flat 12-triangle arc, shot forward at 6 m/s, emitting for 1.5 s, each living 0.4 s, scale 3 → 0.1, fading in) |
| Heal Self I, Harm Other I | red star sparkles around the body (the caster's, the drudge's) | 0x010010B2 (additive red eight-point star) |
| Strength Self I / Aura of Blood Drinker Self I | red glows rising around the caster / red and white stars | 0x01001067 (red glow); 0x010010B2 + 0x010010F9 (white star) |
| Armor Self I / Imperil Other I | a grey streaked shell growing up around the caster / closing on the drudge | 0x01001666–9 (mode 1 shards, SRCALPHA/INVSRCALPHA, lum 0.5, an opaque dark-grey streak texture whose transparency is all particle translucency) |
| Portal / Lifestone Recall | magenta / blue glows swirling up from the feet | 0x01001073 / 0x01001062 (additive glow sprites) |
| Fizzle | white stars swarming away in front of the caster at head height | caster `PlayScript.Fizzle` @0.5 → 0x33000103: one emitter at offset (0, 1, 1.7) from the caster, Swarm particles of 0x010010F9 (white star) fading to half |

## 5. Showcase capture

`--showcase` (`ShowcaseDirector`) casts the 16 entries in turn with a scripted, smoothed camera: war bolts open close on
the caster's hands for the wind-up, follow the projectile from the south and settle on the drudge for the explosion; the
ring is seen from above; self spells and recalls from the caster's front with the town behind; spells on the drudge swing
to it when the effect lands. Shots on the drudge aim 1.5 m short of it and the follow cam stops 2.5 m short, so the
camera stays west of the lamp post 3.7 m south-east of the drudge and the post stays out of frame. Each segment ends a
fixed time after the effect or the first impact. Time advances by exactly 1/30 s per frame, so Godot's Movie Maker (`--write-movie … --fixed-fps 30`) produces a smooth, deterministic
PNG sequence; the log's "showcase starts" line gives the first frame to encode from.

Outputs (gitignored, in `docs/screenshots/m1/`):

| File | What |
|---|---|
| `spellviewer/showcase.mp4` | The 16 showcase casts, 1280×720, 30 fps, H.264 yuv420p, about 51 s |
| `spellviewer/highlights.webp`, `spellviewer/highlights.gif` | Flame Bolt VI, Cassius' Ring of Fire and Portal Recall, looping |
| `spellviewer/frames/f########.png` | The Movie Maker frames the video was encoded from |
| `spellviewer-{windup,flamebolt,explosion,lightning,ring,heal,armor,recall,fizzle}.png` | Stills picked from the frames |
| `spellviewer-ui.png` | The interactive interface during Flame Bolt VI |

## 6. Measurements

RTX 5090, Vulkan Forward+, 1280×720, Debug build, 2026-10-10. Other agents were using the machine at the same time, so
run-to-run spread is wide; both runs are shown.

**Start-up** (to the first frame, with the dats already open by GameServices; this loading phase blocks): surface shader
warm-up 0.2–0.4 s, terrain (25 landblocks, prepared on the WorkerPool) 0.1–0.26 s, simulation warm-up 33–65 ms
(`SpellStage.WarmUp`), particle shader warm-up 0.17–0.35 s (342 particle GfxObjs, 26 variants), actor mesh preload
22–50 ms (61 GfxObjs); ready after 0.7–1.5 s. Then, before the first cast, Holtburg's 3×3 landblocks of statics attach
and the 483 particle meshes (384 materials) finish in the background; both are done within about 1 s. These loading
frames are logged separately ("loading frames", because the timing table below covers only frames with a cast on
stage): **slowest loading frame 7.3–10.0 ms** over four runs (it was 35–40 ms when the particle textures decoded on the
main thread), slowest single statics attach step 1.1–4.2 ms (six runs), slowest particle mesh finish 2.6–7.1 ms (the
first upload of 0x01000FC4's texture). Steps start only while the frame has spent less than 3 ms (statics) or 2 ms
(particle meshes), and a step is never split, so those figures are the real worst case, not the budget. Starting a cast
costs 0.2–1.3 ms on the main thread. (These were measured with the old statics smoke layer. With the objects path, M1
wave 3: Holtburg's 622 objects, scenery included, are attached by frame 116, slowest attach step 2.6 ms, slowest loading
frame 7.5 ms; the living statics step in 0.05 ms and draw in 0.03 ms a frame on average, docs/objects.md §3.1.)

**The showcase with vsync off** (`--showcase --vsync=off --max-fps=0`, 1,542 frames with a cast on stage, fixed 1/30 s steps; two
runs):

| Per frame | Average | p99 | Max |
|---|---:|---:|---:|
| Particles: collect, billboard, pack, upload | 0.062–0.064 ms | 0.17–0.18 ms | 3.7–3.8 ms |
| Stage step + actor poses + particles | 0.12 ms | 0.27–0.28 ms | 3.8–3.9 ms |
| Whole frame (`_Process` delta) | 0.46–0.47 ms | 1.4 ms | 3.7–8.0 ms |

- Peak 256 particles, peak 246 draw calls (terrain, Holtburg's buildings, two actors, particles).
- The maxima are single frames and were not traced further; with vsync on, presentation pacing dominates anyway
  (game-core.md §7).
- **Fixed-step recording** (Movie Maker): the per-frame costs are the same order (particles 0.10 ms, stage 0.23 ms
  average); Movie Maker writes each 1280×720 PNG on the main thread, so recording the 51 s showcase takes five to six
  minutes.

**Tests:** `tests/Lifestone.Client.Tests/Effects/` — 35 tests: the billboard and axis-locked rotations, viewer
distance and LOD rules (with the degrade bias reaching level selection), translucency rules and blend switches, instance
packing and capacity, batching, a real-dats check of the showcase particle GfxObjs, the stage's clearance from
Holtburg's buildings and statics (`StageSiteTests`, §2), and the smoke test that casts all 16 showcase entries at 30 fps
for 6 s each through `SpellStage`, `ActorPartDraws` and `ParticleDrawList` from an orbiting viewer (no Godot) with no
exception, only drawable output, impacts for every bolt and the fizzle script 0x33000103.

## 7. Deviations and open points

| Item | Status |
|---|---|
| Spell words over the head | A viewer aid (Label3D, 3 s). Retail shows the chant as chat text; the log line uses the speech form. Its colour goes through `ByteSpaceBlending.BuiltinColor` (a built-in Godot material, render-contract §2.3), so it shows the same cream in both blend spaces. |
| Character appearance | The Setup's own part textures and palettes (tan skin, grey underwear, red hair). ObjDesc clothing, CharGen skin/hair/eye palettes and face textures are M2 (server ObjDesc). |
| Draw order | Godot sorts transparent batches back to front by their bounds; retail drew its alpha lists unsorted (render-contract §2). Within a batch, retail order (object, emitter, slot) is kept. Only overlapping alpha-blended (not additive) particles can differ. |
| Blending colour space | **Raised to the lead (render contract, not this track).** Godot blends in its linear HDR buffer, retail blended bytes (render-contract §2.3). For additive particles the difference is not slight: retail adds the same byte amount over any background, while a linear add of the same glow raises a dark stone wall a lot and bright grass or sky very little. In the showcase the bolt glows therefore read as a box that follows the dark wall's silhouette behind them, and look fainter than retail over grass. The particles themselves are the same with and without the walls (difference images of `--no-statics` and normal renders of the same frame match). Fixing it needs an sRGB-space (byte) render target for the scene, which every track's shaders depend on. **Resolved in wave 3 (render-fidelity fix, render-contract §2.3).** The viewer, like the game, blends in byte space by default. The bolt's glow adds the same bytes over grass and wall, and its smoke darkens what is behind it as retail's does; see `docs/screenshots/m1/fix-render/pairs-blend/sv-flamebolt.png-3.30-crop.png`. `--blend-space=linear` shows the old look. (The viewer also takes the game's other render-fidelity switches, `--detail=off` and `--retail-normals`, terrain-renderer.md §8.) |
| `ParticlesInDrawRange` | Left at true: the camera stays within 45 m of the actors, inside every particle GfxObj's degrade distance (≥ 64 m), so retail's `ShouldDrawParticles` would be true too. |
| Part material overrides from hooks | Transparent/Luminous/Diffuse hooks on actors are simulated but not drawn yet (none in the showcase; logged once if one occurs) — objects track. |
| Scale hooks | `EffectVisualState.Scale` starts at 1 instead of the object's scale; the viewer uses it only once a hook changed it (TODO(effects) in `SpellStage.DrawScale`). |
| Lights and sounds | Projectile `SetLight` hooks and Setup lights (point lights) are not lit; Sound hooks are not played (no audio in M1). |
| Godot argument warnings | GameServices warns about the viewer's arguments; harmless (request to the lead in the track report). |
| Stage | One cast at a time: a new cast replaces the previous one's actors and particles. |
| Lightning Bolt projectile backdrop | **Open question for the lead / textures track** (not changed here). Surface 0x08000316 (part 0x01001116 of Setup 0x020003F0) is a non-clip Base1Image, so its INDEX16 texture 0x06003967 decodes without a colour key and the lavender backdrop is drawn opaque. The same SurfaceTexture 0x05001685 (so the same RenderSurface) is also used by the clip-map surfaces 0x08000153 (Base1ClipMap), 0x0800098F (Base1ClipMap \| Additive) and 0x08000D80 (Base1ClipMap \| Alpha) (measured on the user's dats). Retail's texture cache shares one decoded texture per id, so whether retail showed a keyed (transparent) or an opaque backdrop may depend on which surface loaded it first; docs/textures.md decodes per surface as a deliberate divergence. Until that is settled the ribbon looks like a solid lavender zig-zag in the video. |
| Start-up hitches | Loading work happens before the first cast: terrain (25 landblocks) prepares on the core WorkerPool and attaches in `_Ready`; the caster's and drudge's part meshes are preloaded there too; statics attach steps start only while the frame has spent < 3 ms on them, but one step is never split, so a frame can overrun (the slowest step and the slowest loading frame are logged, §6). Projectile Setups build their meshes on first appearance. |
