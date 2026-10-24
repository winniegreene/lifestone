# Portal space and sound

The engine-free half of portal space and of Lifestone's sound path: the teleport-animation state machine, the
SmartBox's teleport flags, the destination's readiness, the tunnel object and its hum, the zoom and the lens, retail's
sound rules, the hide and unhide rule, and the two ways a teleport starts (a server session, or the teleport panel in
explore mode). It is specified in [m2-log-in.md](milestones/m2-log-in.md) §4 and planned in
[m2-plan.md](milestones/m2-plan.md) §3.3 ("portal") and §4.8 (checkpoint C-portal).

**Status: wave 1 complete (steps PS1 to PS12); wave 2's G1 track (W1 to W5) has wired it to Godot (§11.6, §11.7).** §1 to §10 are built and tested in
`Lifestone.Data` and `Lifestone.Client`; the tunnel, the zoom, the sounds, the offline transition behind the teleport panel and
`--portal-space-checks` / `--portal-space-frames` run in the engine. The online side (the session bridge, G6) and the retail UI's spew box
(G4) are not wired yet. The provenance of
every re-expressed retail function is in [third-party/portal-space.md](third-party/portal-space.md); no third-party
code is ported.

## 1. Layout

| Namespace (folder) | Types | Step |
|---|---|---|
| `Lifestone.Data.Audio` (`src/Lifestone.Data/Audio/`) | `WaveFormat`, `WaveData`, `SoundEntry`, `SoundTableNode`, `SoundTableData`, `IAudioSource`, `DatAudioSource` | PS9 |
| `Lifestone.Client.Audio` (`src/Lifestone.Client/Audio/`) | `ICrtRandom`, `MsvcCrtRandom`; `ISoundSink`, `SoundOrigin`; `IAudioOutput`, `VoicePlay`, `AudioBus`; `IEffectEventSink`; `RetailSoundRules`, `VoicePool`, `RetailSoundPlayer`, `SoundHookRouter`, `AudioLevels`, `AudioSettings` | PS4, PS10 |
| `Lifestone.Client.PortalSpace` (`src/Lifestone.Client/PortalSpace/`) | `UiAssetIds`, `PortalSpaceAssets`; `AnimLevel`, `PortalZoom`, `PortalLens`, `PortalSpaceScene`; `TunnelSpin`, `IPortalTunnel`, `CreatureModeClock`, `TunnelObject`, `TimedTunnel`; `ITeleportProgress`, `TeleportProgress`; `TeleportAnimState`, `PortalCues`, `PortalFrame`, `PortalSpaceMachine`, `LogOffTunnelPolicy`, `LogOffSchedule`, `ExitWindow`, `IPortalRandom`; `ReadinessPolicy`, `ReadinessItem`, `ReadinessReport`, `DestinationReadiness`; `PortalSpaceDirector`, `ILoginCompleteSink`, `IPortalNoticeSink`; `ExploreTransition`; `PortalSessionBridge`; `HiddenChange`, `HiddenTransition` | PS1 to PS8, PS11 |
| `Lifestone.Client.Streaming` (`src/Lifestone.Client/Streaming/WorldAttachments.cs`) | `AttachState`, `IWorldAttachments` (the readiness contract; portal owns the file after wave 0, changes additive) | L7, PS8 |

**Threading.** Everything runs on the frame thread, once per frame. Only `DatAudioSource` is thread-safe (a host's
audio workers may decode waves through it). **Allocation.** A steady frame allocates nothing: the machine, the
readiness, the director with the retail sound player, the tunnel's spin and the hum routing are measured at 0 bytes
over 10,000 frames. Starting a tunnel spin allocates its sequence node; a warning's text allocates when the log is on.

## 2. One frame

A host (the wave-2 presenter, the headless host, a test) does this every frame, in this order:

1. **The session ticks** (`GameSession.Tick(now)`). Its reducer publishes the player's events, and
   `PortalSessionBridge` turns them into `TeleportProgress` calls (§8). Offline, the teleport panel's jump goes
   through `ExploreTransition.Jump` instead.
2. **The director steps**: `PortalFrame frame = director.Step(now, playerCellId, worldAttachments)`:
   1. `PortalSpaceMachine.Update` (retail `gmSmartBoxUI::UseTime`) reads `TeleportProgress.InProgress` as the last
      frame left it, begins, ends or advances the sequence, and returns the frame with its cues;
   2. `DestinationReadiness.Evaluate` decides whether the destination is ready, and `TeleportProgress.Tick` completes
      the update when it is (retail `SmartBox::UseTime`). Because the machine looked first, a destination that is
      ready at once still plays the whole sequence;
   3. while the tunnel shows, the tunnel advances to `now` and its hook events are drained; with a sound sink, the
      hum is played from the tunnel camera (retail `CreatureMode::Render` steps creature-mode objects while drawing
      them, with `CPhysicsObj::update_position` and the camera as the listener: §5);
   4. the director acts on its cues: the enter and exit whooshes (`UI_EnterPortal` 0x6A and `UI_ExitPortal` 0x6B on
      the UIASSET UI sound table), the notice ("In Portal Space - Please Wait...", text type 0x1A), and LoginComplete;
   5. Lifestone's loud warnings: `ReadinessStalled` every 10 s while state 3 waits (D30) and `ArrivedWithFailures`
      once per arrival with failed pieces.
3. **The presenter applies the frame** (§11.1): what is visible, the lens, the roll, and the cues the director does
   not act on.

`now` is the host's monotonic frame time in seconds, advanced once per frame: the same value `GameSession.Tick`
receives, never the speed-scaled Dereth clock.

## 3. The state machine

States keep retail's numbers (`TeleportAnimState`); t is the time in the state over 1.0 s.

| # | Name | Shows | Zoom | Leaves when |
|---|---|---|---|---|
| 0 | `Off` (`TAS_OFF`) | world | none | a trigger fires |
| 1 | `WorldFadeOut` (`TAS_WORLD_FADE_OUT`) | world (the tunnel too, when a logoff begins while it shows) | collapse | ≥ 1.0 s → 2 |
| 2 | `Tunnel` (`TAS_TUNNEL`) | tunnel | expand | ≥ 1.0 s → 3 |
| 3 | `InTunnel` | tunnel | none (override at f_game) | the update is no longer in progress → 4 (End) |
| 4 | `Holding` | tunnel | none (override cleared) | ≥ 2.0 s and the spin on frames 68 to 76, or ≥ 5.0 s → 5 |
| 5 | `LeavingTunnel` | tunnel | collapse | ≥ 1.0 s → 6: tunnel hidden and stopped, world shown, exit whoosh |
| 6 | `Arriving` | world | expand | ≥ 1.0 s → 0: LoginComplete, not busy, override cleared |

**Triggers**, before the states advance: a logoff whose 3 s have passed begins state 1; the teleport flag turning on
begins state 2; the flag turning off ends the sequence, but only in state 3. **Begin** captures f_game when the
machine was off, resets the zoom to f_game and zeroes the roll's segment, and plays the enter whoosh. **The roll**
(states 2 to 5) eases (AnimLevel) from where the last segment ended to a target in [0°, 360°] over [0.6, 1.8] s, both
drawn with retail's `RandDouble` over the C runtime's `rand()`, the duration first, ends included; each new segment
re-posts the notice.

**Timelines** (at least 5 s, at most about 8 s, plus the load):

| Sequence | Frames |
|---|---|
| Login | the player's CreateObject → Begin(2): enter whoosh, tunnel shown, world hidden, busy → 1 s expand → state 3 until the destination is ready → hold ≥ 2 s → 1 s collapse → exit whoosh, world shown → 1 s expand → LoginComplete |
| Teleport (F751) | the same, from the F751; state 3 also waits for the new position (or a SetState clearing Hidden) |
| Logoff | `LogOffRequested` → 3 s later Begin(1): enter whoosh → 1 s collapse of the world → state 2 (the tunnel shows on the next frame) → 1 s expand → state 3 until the character list resets portal space (D28) |
| Offline jump | `ExploreTransition.Begin` → Begin(2) on the hook's frame, the camera moves on the next frame → … as a login, with the viewer's cell as the destination and no LoginComplete |

**LoginComplete** goes out at most once per completed update, and never before state 6 ends; a teleport that
completed while the machine was off sends it at once (retail `SmartBox::teleport_occured` in state 0). A teleport
whose update is still in progress when state 6 ends runs the tunnel again from the next frame; one whose update
completes during state 6 shares that arrival's LoginComplete.

**Reset** (`PortalSpaceDirector.Reset`, the UI mode changed) is silent: no cue announces it, so a presenter follows
the next frame's steady fields (`TunnelVisible`, `WorldVisible`, `Busy`, `Lens`).

## 4. The teleport flags and the destination's readiness

`TeleportProgress` keeps retail's SmartBox flags: a player exists, its position update is complete, a teleport's new
position is awaited, and a completed update is still to be reported. `InProgress` (retail
`SmartBox::teleport_in_progress`) is "a player and an incomplete update"; `Tick(ready)` completes the update when there
is a player, nothing is awaited and the destination is ready. Lifestone adds `BeginLocal` (a local jump stands in for
the server's player) and `Hold` (`--portal-space` keeps the update from completing).

**Readiness** is Lifestone's stand-in for retail's "the cell manager is not blocking for cells", a data-prefetch
state. `DestinationReadiness.Evaluate(cell, world, policy)` waits for:

- **outdoors** (cell low word 0x0001 to 0x0040): the player's landblock and its 8 neighbours, clipped at the map's
  edge, each with terrain, objects and collision;
- **indoors** (0x0100 to 0xFFFD): the cell and every cell of its visible-cell list (a list not known yet waits), and,
  for a cell that sees outside, the outdoor 3×3 too (D29, `ReadinessPolicy.OutdoorRingForSeenOutsideCells`).

Failed and disabled pieces count as settled and are counted, so a bad landblock or a missing layer never traps the
tunnel; server objects are never waited for. An invalid cell (0, off the map, neither kind) is never ready. The report
names the first waiting and first failed piece (`Describe()` is for the log only).

**The three providers of `IWorldAttachments`:**

| Provider | Owner | Terrain / Objects | Collision | Cells |
|---|---|---|---|---|
| The Godot world adapter `WorldAttachments` | G1 (W4, with `Client/Play/AttachStateMapping.cs`) | the streaming planner and the objects system | `Disabled` until physics' `CollisionStreamer` is wired, then its `StateOf` | the interior controller |
| The headless host's data-only attachments | state (ST15) | decoded from the dats | `Disabled`, or physics' streamer | decoded EnvCell data |
| Tests | portal | `FakeWorld` | settable | settable |

## 5. The tunnel

The tunnel is the UIASSET Setup 0x02000306 (two halves of a torus, GfxObj 0x0100080B) spinning the UIASSET Animation
0x030005AC: frames 1 to 119 at 40 fps, 2.975 s per loop (retail starts the sequence at frame 1 and never shows frame 0
again, so the 119 → 1 step turns 3°, not 1.5°; kept as retail, §14). Frame 2 carries a SoundTweaked hook: the hum,
Wave 0x0A000316 at volume 0.3, once per loop.

- `TunnelObject` is the real object over the dats (`TunnelObject.Create(dats, assets, createdAt: now)`); the wave-2
  tunnel view draws its `PartFrames` (AC coordinates, object at the origin).
- `TimedTunnel` is its frame counter without dats, bit-identical in frame numbers, for the headless host and tests.
  It has no hum.
- **Stepping** (`CreatureModeClock`). Retail's `CreatureMode::Render` steps its objects with
  `CPhysicsObj::update_position`, not the world's `update_object`: no step until at least 1/30 s has built up since
  the last one, then one step of the whole time (a float; never split into 0.2 s quanta); a gap over 2 s is skipped.
  At 60 fps the spin therefore moves every second or third frame, 20 to 30 times a second, as in retail. The clock is
  the object's `update_time`, started when the object is made (`createdAt`, retail `InitObjectBegin`) and moved only by
  these steps: a restart of the spin keeps it (and keeps the parts' pose until the next step), so a tunnel shown again
  within 2 s of its last step catches up on the hidden time in its first step, and one hidden longer skips it.
- The machine restarts and stops the tunnel; the director advances it and drains its events. The exit window
  therefore reads the frame of the tunnel's last step, taken on an earlier drawn frame, as retail does.

## 6. The zoom and the lens

Retail renders with a focal distance f = 1 / tan(FOV / 2). States 1 and 5 collapse f from f_game to 0.001; states 2
and 6 expand it back; the easing is AnimLevel (`UIGlobals::Init`'s 100-entry integer table). `Render::set_vdst` turns f
into a vertical FOV of 2·atan(1/f), clamped to [0.001, π] and rounded to a float, and moves the near plane to f/4 below
f = 0.40000001 (0.1 otherwise).

`PortalFrame.Lens` (`PortalLens`) is the projection of whichever view shows (retail's override is global, and only one
view shows at a time): `Overridden` says the zoom owns the projection this frame; `VerticalFovRadians` and `Near` are
retail's; `FrustumHeightAtNear` is 2·near·tan(FOV/2), which is 0.5 (to about 1e-4) for every f below 0.4 and 0.2/f
above it. **Godot mapping (D31):** use a Frustum projection with that height at that near plane, not `Camera3D.fov`
(capped at 179°; f = 0.001 is about 179.885°). With D40 the world's frustum is offset so that its centre is the
SmartBox frame's centre (`GameViewRect`), so the zoom collapses and expands about the frame's centre. When
`Overridden` is false, `Focal` is the game camera's own f and the view uses its normal projection.

**Engine facts** (W2-pre(a), pinned by `--portal-space-checks`; Godot 4.7.2, Forward+). A `Camera3D` set to the frustum with
`CameraLenses.Apply` projects its view's sides exactly where `CameraLens.Sides` says, with and without an offset, at
f = √3, 0.4, 0.01 and 0.001 (the offset is in the near plane's units; positive is right and up), and a floor drawn through
it covers exactly the rows below the horizon, so D31 holds with no cap. Godot logs one engine message,
`Condition "!res" is true. Returning: false` (`Projection::get_endpoints` cannot intersect three nearly parallel side planes),
per rendered frame and camera while f is below about 0.01 (178.9°); the picture is correct. The message is not a Lifestone
ERROR line, so the smoke suite does not count it; if it is unwelcome in the log, clamping f to 0.01 for the world camera is
the fix, and the owner's call (D31 asks for the exact lens). `Projection.Inverse()` fails on these projections: read the
sides from the matrix elements (X.X, Y.Y and the third column) as the checks do, and note that `Camera3D.ProjectRayNormal`
assumes a centred frustum, so picking under an offset needs the same element-wise unprojection.

`PortalFrame.RollDegrees` is the tunnel camera's roll about its view axis, AC +Y (retail
`CreatureMode::SetCameraDirection_Degrees` with the angle as y), not wrapped. `PortalSpaceScene` holds the rest of the
scene: the camera at (0.24, −2.7, 0.88) looking along +Y, the distant light (0.3, −1.9, 0.65) at intensity 2 (retail
normalises the direction), ambient 0.3, degrades disabled, and the hum's origin (`TunnelHumOrigin`, 2.85 m from the
camera).

## 7. Sound

### 7.1 The data

`DatAudioSource` decodes the portal's Waves and SoundTables with Lifestone's own byte-exact decoders (retail
`DBWave::UnPack`, `CSoundTable::UnPack`, `SoundTableData::UnPack`); DatReaderWriter is only a test oracle.

- **Waves** are a `WAVEFORMATEX` header and the data. The end-of-retail portal has 786: 785 PCM (8-bit unsigned or
  16-bit signed; 11 rates from 5,500 to 44,100 Hz; mono and stereo) and one MPEG layer 3 wave, 0x0A000393 (raw frames
  after a 30-byte `MPEGLAYER3WAVEFORMAT`). Retail converted non-PCM waves with MSACM; any tag other than PCM and MP3 is
  `WaveData.Unsupported`, a named TODO that is never played.
- **SoundTables** are a recursive node: key, entries (wave, priority, probability, volume), children. Retail looks up
  one level: the root's child whose key is the sound type. The portal has 190 tables, every root a packed default
  entry with key 0, 4,184 children and no grandchildren; one sound has two variants (Swoosh2 of 0x200000A8).
  DatReaderWriter's model of the same bytes is right only while the grandchild count it calls `Unknown` is 0.
- **The portal-space waves** are PCM, mono, 11,025 Hz, 16-bit: the hum 0x0A000316 (3.01 s), `UI_EnterPortal`
  0x0A000246 (1.11 s) and `UI_ExitPortal` 0x0A000245 (1.12 s), all at volume 1 in the UI table 0x2000004B.
- **A SoundTweaked hook's wire order** is the wave id, the probability, the priority, then the volume
  (`SoundTweakedHook::UnPack`, 11.6096 0x00528380, 11.4186 0x00527780; Pack 0x00528330). `AnimHookDecoder` read the
  priority before the probability (the order ACE names the fields), so every real tweaked hook, whose priority is 0, had
  probability 0 and never sounded; fixed in M3 1.3 fix B (`SoundTweakedHookDecodeTests`). The portal hum's hook
  (probability 1, priority 1) happened to decode the same either way.

### 7.2 The rules (`RetailSoundRules`, `RetailSoundPlayer`)

`RetailSoundPlayer` is the `ISoundSink` façade with retail's six entry points, each with its gate, its draws and its
volume:

| Entry point | Retail | Gate | Variant | Probability | Volume | Bus |
|---|---|---|---|---|---|---|
| `PlayFromCenter(table, type)` | `PlaySoundFromCenter(SoundType, CSoundTable*)` | interface | yes | entry's | entry's | UI |
| `PlayWaveFromCenter(wave, volume)` | `PlaySoundFromCenter(gid, float)` | interface | — | none | caller's | UI |
| `PlaySound(wave, origin)` | `PlaySoundA(gid, obj)` (Sound hook) | effects | — | none | the effects slider | Effects |
| `PlayWave(wave, prio, prob, vol, origin)` | `PlaySoundA(gid, obj, prio, prob, vol)` (SoundTweaked hook) | effects | — | hook's | hook's | Effects |
| `PlayFromTable(table, type, origin)` | `PlaySoundA(SoundType, obj)` (SoundTable hook) | effects | yes | entry's | entry's | Effects |
| `PlayFromTable(table, type, origin, volume)` | `PlaySoundA(SoundType, obj, float)` (`CPhysicsObj::play_sound`) | effects | yes | entry's | caller's | Effects |

Then, for every sound: the attenuation and the pan from the origin, and a voice from the pool.

The retail UI's sounds are the two UI entry points (`PlayFromCenter`, `PlayWaveFromCenter`); the UI counts them in `RetailUiSoundStats`.

- **Variant**: ⌊(n − 1) · RollDice(0, 1)⌋ on retail's `Random`; the last of several variants never plays.
- **Probability**: `rand() · 0.000030518509 < p` on the C runtime's `rand()`; the scale is the float nearest 1/32767
  (2⁻¹⁵·(1 + 2⁻¹⁵)), so p = 1 always plays and p = 0.5 plays for `rand()` 0 to 16383.
- **Attenuation** (`SoundManager::GetAttenuation`, 11.6096 0x00550C30), whole decibels: gain = volume within 5 m, 25·volume/d² from 5 m; capped at 1; times the effects
  slider; dB = ⌈log₂(gain) · 6.0206⌉; not played below −50 dB (`VOL_MIN`) or at gain ≤ 0. Examples: 0.5 → −6, 0.3 →
  −10 (the hum), 0.25 → −12 (also 1.0 at 10 m), 0.01 → −40, 0.003 → −50 (still plays), 0.0028 → dropped. Past 5 m a sound is therefore silent beyond about 94 m at volume 1 and about
  9.4 m at volume 0.01. The distance is `Position::distance(object position, player_position_)` in metres
  (`SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)`, 0x00550D80).
- **Pan**: from 5 m, sin(bearing) · −15 truncated toward 0, the bearing in degrees taken modulo 360 into
  (−360, 180]; 0 nearer and in mono. Retail's float π/180 is a hair small, so the pan never reaches ±15: it stops at ±14.
  The **bearing** is the compass heading from the source to the listener minus the listener's heading: retail's
  `Position::heading(source, listener) − Frame::get_heading(listener)`, where `Position::heading` measures the offset
  listener − source (`Position::get_offset`). Equivalently, it is the listener-to-source bearing + 180°. A source 10 m
  east of a listener facing north gives 270 (−90), pan +14, the left channel lowered: heard on the right.
  `SoundOrigin.Between(listener, source)` computes the distance and the bearing; callers should not derive them by hand.
  The avatars' sound origin (`AvatarSet`'s `SoundOriginOf`) once measured the listener-to-source bearing (the listener's heading from the source's position) and so mirrored every positioned sound; it now calls `SoundOrigin.Between` (M3 1.3 s1). Retail's own sign is the one `Between` has (source-to-listener heading minus the listener's heading: `SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)` 11.6096 0x00550D80 over `Position::heading` 0x005AA5D0), so this is a fix to match retail, not a glitch fix: no flag. Pinned by `AvatarSoundOriginTests` (east of a north-facing listener: bearing 270, pan +14; west: 90, pan -14; heading follows; a landblock seam).
- **Voices**: 16, round-robin from a cursor; the first empty, priority-0 or finished voice is taken, else one with a
  lower stored priority is stopped, else the sound is dropped. Every voice stores the cached buffer's priority, which
  is always 0, so in practice the 17th sound replaces the oldest.
- **Gates**: the interface and effects categories (`AudioLevels.InterfaceEnabled`, `EffectsEnabled`), mono, and
  play-only-when-active with `RetailSoundPlayer.ApplicationActive`.

**Findings kept as retail has them:** interface sounds follow the effects slider (retail's interface slider is never
read); a Sound hook's wave is scaled by the slider twice (its volume is the slider, and the attenuation multiplies by
it again); the priority of a table entry or a SoundTweaked hook is never used; sound is 2D. **Lifestone's
differences:** a wave is decoded on first use (retail only plays waves a loaded table or hook named); a missing table,
type or wave, a malformed record and an unsupported format are a once-per-id WARNING (category `audio`), never an
exception; each player has its own seeded `Random` and `rand()` (retail shares one of each with the whole client).

### 7.3 Hooks, listener and output

`SoundHookRouter` (an `IEffectEventSink`) routes an object's SoundTweaked, Sound and SoundTable hooks, and the sound the server asks an object to play (`ServerSoundTableHook`, F750), to the hook entry points; every other hook is ignored. `OnEffectEvent(event, origin, soundTableId)` carries the emitting object's sound table (0x20xxxxxx, or 0): a SoundTable hook plays from the table of the object that fired it (`SoundTableHook::Execute`, 11.6096 0x005276B0), and from an object without one it is silent (counted, `NoTable`). The router has no table of its own any more (the old `SoundTableId` property is folded into the parameter; the portal tunnel's call of the static `Route` passes 0). The server's sound plays `PlayFromTable(table, type, origin, volume)` with the message's volume (`SmartBox::HandleSoundEvent` 11.6096 0x00452000, then `SoundManager::PlaySoundA(SoundType, obj, float)` 0x00551700).
The **listener is the camera** (retail `SmartBox::set_viewer` → `SoundManager::SetPlayerPosition`); while creature
mode updates the tunnel, it is the tunnel camera, so the hum is 2.85 m away: centred, at −10 dB. The caller puts the
listener into each `SoundOrigin` (distance, bearing) with `SoundOrigin.Between(listenerFrame, sourceFrame)`, which
uses retail's source-to-listener heading (§7.2); a sound does not follow its object after it starts.

The router's public read-only counters are what was handed to the sound player, independent of the volume (M2 D8 keeps automated runs silent): `HooksPlayed` (animation and script sound hooks), `Footsteps` (the SoundTable hooks of type Footstep1 55 and Footstep2 56, a subset of `HooksPlayed`), `ServerSounds` (F750), `NoTable`, `Routed`, `Ignored`. Play mode owns one router (`PlayMode.HookSounds`) over `AudioService.Sound`, shared by the avatars (`AvatarSet.EffectEvents`) and the living statics (`LivingStaticsSystem.EffectEvents`). The avatars' listener is the player's body, not the camera (`TODO(avatars-listener-camera)`, docs/avatars.md section 17); the living statics use the camera.

`IAudioOutput` is the sound card: `Play(VoicePlay)` starts a wave on a voice (stopping what it played),
`Stop(voice)`, `IsPlaying(voice)`. `VoicePlay.AttenuationDb` applies to both channels; `VoicePlay.Pan` lowers the right
channel by |pan| dB when negative and the left by pan dB when positive (DirectSound's pan, ×100 in hundredths of a
decibel). `RetailSoundRules.ChannelGains(pan, dB)` gives both linear gains. `AudioSettings` is Lifestone's master
volume and mute, applied by the host to its master bus after the rules (`MasterDecibels`, −80 dB when muted).

### 7.4 Ambient sound (M3 1.3 s2b)

The region's `SoundInfo` (37 `AmbientSTBDesc` tables of sounds with a type, volume, base chance and min and max rate; a base chance of 0 is a constant sound) is `RegionSoundData`. `AmbientSoundPlayer` re-expresses retail's outdoor rule: each of the 3 x 3 landblocks' 64 cells' south-west vertex adds weight (1 inside 20 m, 400 over the squared distance to 120 m, else 0) to the table its terrain type and scene slot name; an intermittent sound plays with chance base chance / total weight x its weight, placed in a compass sector at a quadratic-biased distance, then again after a draw between its min and max rate; a constant sound plays from the centre at volume / total weight x its weight every min-rate seconds; outdoors only (`Ambient::*` 11.6096 0x00551430-0x005526xx, `LScape::add_ambient_sounds` 0x00506270, `CLandBlock::add_ambient_sounds` 0x00531050).

**Cadence and gate.** The scan (`InitSounds`, `add_ambient_sounds`, `UpdatePlayQueue`) runs when the listener's cell, the outdoors flag or the terrain revision changes, as `CellManager::ChangePosition` does in its new-cell branch (11.6096 0x00455AD0, 11.4186 0x004559B0); `Ambient::UseTime` runs every frame, and a draw plays through `Ambient::PlaySoundA` (11.6096 0x005519B0; 0x00550D90 in 11.4186). The ambient is gated on the world being drawn and the player being in the world (an object, `SelfTravel` not `NotInWorld`): before the gate a play-checks run made 47 draws before the player had an object, after it none (the summary line's `draws_before_world=0`). The terrain under the 3 x 3 scan is read on a pool thread (`BackgroundAmbientTerrain`), never in the frame.

Lifestone's differences: the 3 x 3 scan, the effects slider and bus (no ambient slider: `TODO(ambient-slider)`), EnvCells are indoors (`TODO(ambient-seen-outside)`), ConstantSound's PlayNow and GetSoundPos taken from the base class (`TODO(ambient-constant-defaults)`). All three TODOs (with `TODO(ambient-slider)`) are logged once at install.

The public counter is `AmbientSoundNode.Current?.Player` (`Draws`, `Played`, `Skipped`); the node logs `ambient sound summary ambient_draws= ambient_played= ambient_skipped=` at exit. GM 0xEA60 AdminEnvirons, which sets the fog and ambient override and plays UI sounds, is in docs/session.md section 15 (the fog: `TODO(m3-environs-fog)`).

## 8. Online: the session bridge

`new PortalSessionBridge(session, director)` subscribes to the session's events and becomes the director's
`ILoginCompleteSink`. Create it with the session, before the player enters the world; dispose it with the session.

| Session event (C-state) | Bridge | Retail |
|---|---|---|
| `ObjectCreated` with `IsSelf` (create, newer instance, F7DB re-create) | `TeleportProgress.OnPlayerCreated()` | `SmartBox::HandleCreateObject` / `HandleUpdateObject`, the player branch |
| `PlayerTeleportAnnounced(seq)` (published only when accepted) | `OnPlayerTeleport(seq, seq)`: the reducer already applied the not-older rule | `SmartBox::HandlePlayerTeleport` |
| `ObjectMoved` with `PositionVerdict.SelfTeleport` | `OnPlayerPositionTeleported()` | `SmartBox::TeleportPlayer` |
| `ObjectStateChanged` with `IsSelf` | `OnPlayerStateApplied((uint)State)` | `SmartBox::DoSetState` |
| `SessionPhaseChanged` from `EnteringWorld`, `InWorld` or `LoggingOff` to any other phase | `OnPlayerRemoved()` and `director.Reset()` | `SmartBox::Reset`; the UI torn down |
| `LogOffRequested(now, pk)` | `machine.RequestLogOff(now, pk)` | `CPlayerSystem::RequestLogOff` |
| the director's `SendLoginComplete` | `session.Actions.LoginComplete(ActionSource.Client)`, at once | `CPlayerSystem::SendLoginCompleteNotification`, which waits for the inventory (§14) |

The Action API refuses a second LoginComplete per arrival, loudly; the bridge keeps the last handle
(`LastLoginComplete`).

**In the game (G6 W9, `--play`).** `PlayMode.AttachPortal` makes the bridge with the director `PortalSpaceSetup.Install` now returns (`PortalSpaceHandles`: the director,
the progress, the world adapter and the presenter); the presenter's destination cell is `PlayMode.DestinationCell` (the cell the server put the player in, then the
player's own once the physics body is placed). `PlayerPlacement` moves the world, the interiors, the physics and the camera and does **not** feed `TeleportProgress`: the
bridge does, from the same events, and feeding it twice would run retail's flags twice. LoginComplete goes out once per arrival, and a teleport is an arrival too
(ACE takes the message as "the client has arrived", `Player.OnTeleportComplete`): `--play-checks` sees one for the login and one more for the teleport. The player's collision
streamer is given to `WorldAttachments.CollisionSource`, so the readiness rule now waits for the collision around the destination (explore mode, with no player, still
counts collision as settled and says so once).

## 9. Offline: the explore transition

`ExploreTransition.Begin()` wraps only the teleport panel's jump (G1's `OfflinePortalTransition`, behind
`--portal-transition`). Enabled and with no server player, it calls `TeleportProgress.BeginLocal()`; the director steps later
in the same frame, so state 2 begins and the world hides on the hook's frame and the destination never flashes. **The camera
move runs on the next frame**, at the start of it (the scene tree's `process_frame` signal, armed by the presenter at the end of
its first step, so it does not matter where in the frame the hook was called): a bare jump costs about 40 ms (the old world's
teardown), the tunnel's own first frame about 20, and the two in one frame measured 52 to 66 ms against DoD 15's 50 (§11.7).
Until the move has run the presenter gives the director cell 0 (never ready), or the viewer's old cell would complete the update
before the camera moves. The readiness cell is then the viewer's cell after the jump, and LoginComplete is suppressed
(logged at INFO, counted in `LoginCompletesSuppressed`). Golden viewpoints, tours and the check suites move the camera
directly and never play it. With a server player the jump runs bare: portal space then belongs to the session.

## 10. Hiding and materialising

`HiddenTransition` is retail's rule (`CPhysicsObj::set_state` acts only when PhysicsState Hidden 0x4000 changes;
`CPhysicsObj::set_hidden` does the work); the avatars track applies it to server objects (AV9).

| Change | Script (intensity 1) | The object | Its children |
|---|---|---|---|
| `Hide` | `PlayScript.Hidden` 0x76 | Hidden and IgnoreCollisions (0x10) set; ReportCollisions (0x8) cleared, ending its collisions; not drawn | NoDraw (0x20) set |
| `Unhide` | `PlayScript.UnHide` 0x75 | Hidden and IgnoreCollisions cleared; ReportCollisions set, starting its collisions; drawn | NoDraw cleared |

An object created Hidden plays the Hidden script at creation: a new object's state is 0x00400C08 (Hidden clear), and
`set_description` loads the script table before it applies the CreateObject's state (`ClassifyCreation`). The
logging-in player does so unseen, in the tunnel; after LoginComplete ACE clears Hidden and the player materialises.
Script table 0x34000004, for example, maps UnHide to 0x3300032F and Hidden to 0x33000331. `HiddenTransition.Play`
plays through the object's `EffectObject`.

## 11. The wave-2 integration contract (G1, with G2, G4, G5 and G6)

### 11.1 The presenter (`PortalSpacePresenter`, G1 W4)

A Godot node whose process priority runs after the session host (−900), the teleport panel and `WorldRoot`. Per
frame: `var frame = director.Step(now, playerCellId, worldAttachments)`, then:

| Frame field or cue | The presenter |
|---|---|
| `TunnelVisible` | shows or hides the tunnel view (the `CreatureModeView` SubViewport in the SmartBox's viewport element 0x10000436); the view draws `TunnelObject.PartFrames` |
| `WorldVisible` | requests world visibility from the `WorldVisibility` arbiter (the only writer of `Viewport.Disable3D`; the world keeps streaming) |
| `Lens` | applies it to the visible view: the tunnel camera while `TunnelVisible`, else the world camera (`WorldLens`, a frustum centred on `GameViewRect`, D31/D40); the sky follows the world lens |
| `RollDegrees` | rolls the tunnel camera about AC +Y (through `AcToGodot`) |
| `PlaceTunnelCamera` | puts the tunnel camera at `PortalSpaceScene.CameraPosition` |
| `SelectionIndicatorOn` / `Off` | enables or disables G4's vivid target indicator |
| `BusyOn` / `BusyOff`, `Busy` | the retail UI's busy count (`IncrementBusyCount` / `DecrementBusyCount`) |
| `RestartTunnelSpin`, `StopTunnelSpin` | nothing: the machine already restarted or stopped `TunnelObject` |
| `PlayEnterPortal`, `PlayExitPortal`, `PostPortalNotice`, `SendLoginComplete` | nothing: the director acted on them (through its `ISoundSink`, `IPortalNoticeSink` and `ILoginCompleteSink`); playing the whooshes here too would double them (§15.1) |
| `ReadinessStalled`, `ArrivedWithFailures` | optional on-screen hint; the director has logged them |

A presenter that starts mid-sequence, or after a `Reset`, follows the steady fields rather than the cues. The notice
sink is G2's spew box (`IUiNoticeSink.DisplayStringInfo` with text type 26), or a labelled placeholder line until it
exists (D32). `--portal-space` calls `progress.BeginLocal()` and `progress.Hold(true)` at start-up.

### 11.2 Construction (G1 W1)

```csharp
var assets = PortalSpaceAssets.Resolve(dats.EnumMaps);
var tunnel = TunnelObject.Create(dats, assets, createdAt: now);               // new TimedTunnel(createdAt: now) headless
var machine = new PortalSpaceMachine(tunnel, new MsvcCrtRandom(seed), () => worldCamera.Focal,
    retailLogoffTunnel ? LogOffTunnelPolicy.Retail : LogOffTunnelPolicy.HoldUntilCharacterList);
var progress = new TeleportProgress();
var sound = new RetailSoundPlayer(audioService, new DatAudioSource(dats), new RetailRandom(seed), new MsvcCrtRandom(seed), AudioLevels.Default, log);
var director = new PortalSpaceDirector(machine, progress, assets, ReadinessPolicy.Default, sound, noticeSink, log);
// Online: new PortalSessionBridge(SessionHost.Current, director). Offline: new ExploreTransition(progress) { Enabled = portalTransition }.
```

The game camera's focal distance is `PortalZoom.FocalFromVerticalFov(verticalFov)` (√3 for Lifestone's 60°). Seeds
come from a clock in live play and are fixed for captures and tests. The same `RetailSoundPlayer` is the `ISoundSink`
the retail UI plays its sounds through.

### 11.3 The audio service (`AudioService : Node, IAudioOutput`, G1 W2)

Built (`scripts/Audio/AudioService.cs`, `WaveStreams.cs`; `PcmStreams` and `AudioOptions` in `Lifestone.Client.Audio`).

- 16 voices, each an `AudioStreamPlayer`; run-time buses UI and Effects, both into Master (`AudioService.Create`).
- `Play(in VoicePlay)`: stop the voice and ask `WaveStreams` for the wave's stream (a cached one is used at once; otherwise a
  worker builds it from `DatAudioSource.GetWave` and hands it over through the main-thread queue). On arrival `volume_db` =
  `AttenuationDb` (floored at retail's −50), the bus from `Bus`, then play. PCM → `AudioStreamWav` with the rate, the channels
  and 8 or 16 bits, the 8-bit unsigned samples re-centred to signed; MP3 → `AudioStreamMP3` with `WaveData.Samples`;
  `Unsupported`, a missing wave or an unsupported PCM layout → a once-per-wave warning (`TODO(audio)`) and silence. A pan is baked
  into 16-bit stereo with `RetailSoundRules.ChannelGains` (Godot's non-spatial players have no pan; one cached stream per wave and
  pan); an MP3 plays centred with a `TODO(audio)` warning. Every portal-space sound is centred.
- `IsPlaying(voice)` is true while a request is in flight or the player is playing, so the retail rules see a started sound; a
  later `Play` or `Stop` on the voice supersedes the request.
- `AudioSettings` from `--volume` and `--mute` (`AudioOptions.Resolve(automated)`) sets the Master bus (`MasterDecibels`, and its mute);
  automated runs are silent unless `--volume` is given (D8). `-Offscreen` also selects the Dummy audio driver. The voices still
  run when Master is silent, so the retail rules and the checks behave the same.
- `--record-audio=FILE` (a relative `.wav` path under `cache/`; the main scene's Video capture group, for short clips) puts an
  `AudioEffectRecord` last on Master and writes the recording when the game quits normally (`AudioService.ShutdownGuard`); a
  killed process (godot.ps1's `-TimeoutSeconds`) writes nothing. A bus's effects run before its level and mute, so a silent
  automated run records the full mix and nothing reaches the speakers. The `audio recording started` line gives the start's
  wall-clock time (`unix_ms`) for lining the sound up with a screen capture; that holds with a real audio driver only (the
  Dummy driver of `-Offscreen` sleeps a fixed time after each mix and drifts). The audio check reads the file back after the write (`Lifestone.Client.Audio.WaveLevels`: duration, peak and RMS in dBFS, the 50 ms windows above -60 dBFS) and logs `audio recording levels file= seconds= peak_dbfs= rms_dbfs= active_windows= sources=<server:n,hooks:n,footsteps:n,footsteps_sounded:n,ui:n,ambient:n,server_handed:n,hooks_handed:n|n/a> windows= muted=<true|false>`; smoke's `record-audio` step (the play checks with `--record-audio`, muted as every automated run, D8) passes on the play checks' own verdict and a non-silent recording (peak above -60 dBFS, at least one active window, `muted=true`); the per-source counts are printed, not judged (`TODO(record-audio-check)` closed). `sources` has eight fields: `server`, `hooks` and `ui` and `ambient` are what sounded (a voice started), `footsteps` is the player's own footstep hooks handed to the sound player, `footsteps_sounded` the subset that chose a wave, and `server_handed` and `hooks_handed` what was handed whether or not a wave was chosen (docs/session.md sections 27 and 29): the human sound table has no wave for footsteps 55/56. Portal sounds alone would pass this step (1.3 review R7): item 2.1's gate judges the `sources=` counts.
- `AudioService.ReadSources` reads the per-source counters; while a recording runs the service remembers the counters' owners each frame, because play mode, the retail UI layer and the ambient node leave their statics before the shutdown guard writes the file.
- `AudioService.IsSilent` is true while Master is muted or at volume 0; `AudioService.Install(parent, services, args, seed, portalView)` is the one install path: `PortalSpaceSetup` calls it with `portalView: true`, and a run without a portal view (`--retail-ui-only`, `--retail-ui-frames`) calls it with false; it reads `--volume`, `--mute` and `--record-audio` and installs the shutdown guard. The audio options' bad-argument warnings are logged under category `audio`. `AudioService._Process` runs only while recording (`SetProcess` in `_Ready`, `StartRecording` and `FinishRecording`).
- Play mode (M3 1.3 s1) hands the avatars' and the living statics' hooks to the retail sound player through the router of section 7.3; the player's sound table is prefetched on workers when its body is made (`PlayMode.PrefetchPlayerSounds`: every wave of every type in the table, then `AudioService.Prefetch` on the main thread), so the arrival's first step and server sound do not wait behind the world's streaming. A run with no audio service logs once that the hooks are dropped and counts them in `AvatarSet.Stats.EffectEventsDropped`.
- The arrival's prefetch is spread over frames (`PlayMode.WavesPerFrame` = 6, `ServerObjectParticles.ModelsPerFrame` = 4, on frames whose adapter pump took under 2 ms).
- The ambient sound of section 7.4 is played by `AmbientSoundNode` through the same sink on the effects bus; its public counter and summary line are described there.
- `AudioService.Sound` is the `RetailSoundPlayer` over the dats (seed from a clock live, fixed 1 in automated runs): the
  `ISoundSink` the director, and later the retail UI, play through. `AudioService.Current` and `GameServices.Audio` hold the
  service; `Prefetch` builds the portal whooshes and the hum before they are needed (a stream first asked for while the world
  streams in would queue behind it on the worker pool: 0.6 s late in the first run). A `Prefetch` failure in `PortalSpaceSetup` is caught and logged, and the tunnel still builds. `WaveStreams.Request` always completes: on any non-fatal failure the stream is silence.
- **Shutdown.** A voice that is audible when its player leaves the tree leaves a playback and its stream to Godot's leak report
  ("2 ObjectDB instances were leaked at exit") whatever is done afterwards. The tree exits children in reverse order and a
  parent's `_ExitTree` runs after its children's, so `AudioService.ShutdownGuard` (a sibling added after the service) stops the
  voices first and waits for the mixer to run twice (100 to 180 ms, only when a sound was audible).

### 11.4 The world adapter (`WorldAttachments : IWorldAttachments`, G1 W4)

Terrain from the streaming planner's state, objects from the objects system, cells and visible-cell lists from the
interior controller, collision `Disabled` (`AttachStateMapping.Collision()`) where no `CollisionStreamer` runs (explore mode, the portal checks); play mode uses `CollisionStreamer.StateOf`. Queries read state the
providers already track, once per frame, and never start a load (`Client/Streaming/WorldAttachments.cs`).

### 11.5 What the shared files provide (W2-pre(a), lead)

The seams below are in place on master-to-be; the wave-2 portal stage fills `PortalSpaceSetup.Install` (called from
`Main._Ready` after the cinematic attach) and uses them.

| Seam | Where | Use |
|---|---|---|
| `AcToGodot.CameraRotation(heading, pitch, roll)` | `Core/AcToGodot.cs` | The tunnel camera's roll: a right-handed turn about the view axis, AC +Y. `TODO(portal)`: retail's sign is unverified (unobservable in play) |
| `SurfaceLighting.CreatureMode`, `CreatureModeLighting` | `Client/Rendering/SurfaceLighting.cs`, `Render/CreatureModeLighting.cs` | Creature-mode materials: `SurfaceShaderVariant.WithLighting(CreatureMode)` (no fog, no detail), lit by the `ac_cm_*` globals (`CreatureModeLighting.ApplyPortalScene()` writes the tunnel's). `ActorMeshLibrary(services, SurfaceLighting.CreatureMode)` builds meshes with those materials. The light vector is read as pointing **towards** the light (`TODO(portal)`, unverified). All variants are in the start-up warm-up |
| `CameraLens`, `CameraLenses.Of/Apply` | `Client/Rendering/CameraLens.cs`, `Render/CameraLenses.cs` | The world camera's projection as one value. `WorldLens` calls `CameraLenses.Apply(camera, CameraLens.FromPortal(portalLens, offsetX, offsetY))` while `PortalLens.Overridden` and restores `CameraLens.Perspective(fov, near)` after. The interiors' frustum and view key (`InteriorController`), `SkyView.Follow` (projection, near, size, offset) and the checks all read the camera through `CameraLenses.Of`. `GameViewRect` is the whole window for now: pass `CameraLens.OffsetForCentre(portal.FrustumHeightAtNear, windowAspect)`, which is exactly zero for the middle of the window and puts the view's axis (the point the zoom collapses to) at any other fraction of the window (the D40 hook) |
| `ViewFrustum.FromLens` | `Client/Interiors/ViewFrustum.cs` | Accepts fields of view below 180° and off-centre frusta |
| `SkyView.Paused` | `World/Sky/SkyView.cs` | Set by the sky controller whenever `WorldVisibility` hides the world |
| `GameServices.WorldVisibility` | `Core/GameServices.cs`, `Render/WorldVisibility.cs` | The presenter calls `Follow(WorldVisibility.PortalSpaceOwner, frame.WorldVisible)` once per frame; nobody else writes `Disable3D` |
| `GameServices.Audio` | `Core/GameServices.cs` | The slot `AudioService` fills (`IAudioOutput?`) |
| `TeleportPanel.Teleporter`, `ITeleporter` | `UI/TeleportPanel.cs`, `UI/ITeleporter.cs` | `OfflinePortalTransition : ITeleporter` wraps `ExploreTransition.Jump(Action)`; the panel shows its "Portal transition" toggle once one is set (`SkyUi/TeleportPanel`), and jumps through it. Off-map coordinates are rejected before the hook runs |
| `ObjectsSystem.StateOf(coord)`, `InteriorController.IsCellAttached(id)`, `InteriorController.SetPlayerCell(id)` | `World/Objects/ObjectsSystem.cs`, `World/Interiors/InteriorController.cs` | The `WorldAttachments` adapter's answers. `StateOf`: `Active`/`Empty` attached, `Failed`, `Disabled` beyond `--object-radius`, else pending. `IsCellAttached`: the cell's node (or nothing to draw) is registered. `SetPlayerCell` makes the interiors layer mesh the rest of the landblock for the player's cell, as for the viewer's (`NeedsAllCells`), before the viewer reaches it |
| `ActorPartDraws.DegradesDisabled` | `Client/Effects/ActorPartDraws.cs` | `PortalSpaceScene.DegradesDisabled`: level 0, mode `Normal` for every part |
| `--fov` | `GameOptions.Fov` | The world camera's rest field of view (default 60 in explore mode; in play mode retail's, 90 over (aspect - 0.1), 73 degrees at 800x600, unless `--fov` is given: docs/game-core.md); `FlyCamera` and the spell viewer read it, and the tunnel's rest lens reads the world camera's own angle (`PortalTunnelView.Create(..., restFovDegrees)`) |
| `--portal-space-checks`, `GameCheckSuite.PortalSpace` | `PortalSpace/PortalSpaceChecks.cs` | The suite the stage extends with its own list (m2-plan G1 W5) |

### 11.6 What G1 W1 to W3 built

| Piece | Where | Notes |
|---|---|---|
| Options | `Client/PortalSpace/PortalSpaceOptions.cs`, `Client/Audio/AudioOptions.cs`; `OptionCatalog.PortalSpace.cs`, `OptionCatalog.Audio.cs` | §12's specs, all registered. `--portal-transition` is parsed and logged; it takes effect with the offline transition (W4). `--portal-space-frames` is a loud stub (error line, exit 1) until W5's runner. `--retail-logoff-tunnel` maps to `LogOffTunnelPolicy` (the headless host may read the same parser) |
| `FrameBudgetShare`, `BudgetShare` | `scripts/PortalSpace/FrameBudgetShare.cs`, `Client/Play/BudgetShare.cs` | Priority −1100. `FrameBudgetShare.Take(name, ms)` / `Release(name)` are static and no-ops when the node is absent; the session pump takes `"session"` with `--session-budget-ms`, the tunnel takes `"tunnel"` (1 ms) while shown. `effective = min(base, max(1 ms, base − shares))`. The F3 overlay shows "budget shares: queue 3.0 ms of 4.0 (tunnel 1.0)" while any is taken |
| `AudioService` | §11.3 | |
| `CreatureModeView : SubViewport` | `scripts/CreatureMode/CreatureModeView.cs` | Own world, black clear, no sun/ambient/fog; its own environment with `ByteSpaceBlending.Attach(env)` (the view's texture is the finished picture, shown as it is). `SetLights` / `SetPortalSceneLights` feed the `ac_cm_*` globals when the view becomes `Active`; `RenderOnce` is the start-up warm render. The node's own `RenderTargetUpdateMode` keeps reading "once" after the server drew the frame, so `RenderPending` counts engine frames instead |
| `PortalTunnelView : CanvasLayer` | `scripts/PortalSpace/PortalTunnelView.cs` | Layer −1 (behind the default layer, so the F3 overlay and every interface layer stay on top), one full-window `TextureRect` of the view's texture; the view is the window's size and follows resizing. Parts: the Setup's two GfxObjs through `ActorMeshLibrary(services, SurfaceLighting.CreatureMode)`, posed from `TunnelObject.PartFrames` with the Setup's part scales. Camera at AC (0.24, −2.7, 0.88) looking along +Y, rolled through `AcToGodot.CameraRotation`; `SetLens` takes a `PortalLens` as a frustum on the whole window. One warm render at install (the debug log says how many of 14,400 sampled pixels it lit) |
| `PortalSpaceHold` | removed in W4 | The W3 placeholder driver for `--portal-space`; `PortalSpacePresenter.StartHeld()` replaces it (§11.7) |
| `PortalSpaceSetup.Install` | `scripts/PortalSpace/PortalSpaceSetup.cs` | Budget share, audio, tunnel, then (W4) the notice line, world lens, world adapter, presenter, offline transition and, with `--portal-space-frames`, the runner; an unbuildable tunnel is an error line (exit 1 with `--portal-space` or `--portal-space-frames`), never a crash |

Engine facts found on the way: Godot logs `Condition "!res" is true` from `rendering_light_culler.cpp` (`create_frustum_points`)
once, at the first frame of the tunnel's expansion (f from 0.001); it is the same family as the message in §6. A `SubViewport`
inside an invisible `CanvasLayer` still draws a `RenderOnce` frame.

### 11.7 What G1 W4 and W5 built

| Piece | Where | Notes |
|---|---|---|
| `PortalSpacePresenter` | `scripts/PortalSpace/PortalSpacePresenter.cs` | Priority 15. `director.Step(now, playerCell, worldAttachments)`, then: `WorldVisibility.Follow(PortalSpaceOwner, WorldVisible)`; the tunnel view shown, lensed (`SetLens(frame.Lens)`: with no override, Focal is the game's own f), rolled and posed; the world camera's lens through `WorldLens` **only while the world is drawn** (hidden, its camera keeps the lens it had); the notice line active while the tunnel shows (states 2 to 5; gone when the world is back). It plays no sound: the director has the audio service's `RetailSoundPlayer` and plays the whooshes and the hum. The selection indicator and the busy count are named `TODO(portal)` WARNING lines, once per run, the first time each cue arrives (G4, G2); until G2's busy count exists the busy state holds the explore camera's input (`FlyCamera.InputSuspended`, which the teleport panel's typing flag does not release: `InputEnabled` reads both), so WASD cannot fly the hidden camera and move the readiness destination under the wait. The clock is real time, or a fixed step that advances only while a sequence runs (`--portal-space-frames`), so waiting for the world never changes the tunnel's spin or the roll. `--portal-space` is `StartHeld()` (BeginLocal + Hold). |
| `WorldLens` | `scripts/PortalSpace/WorldLens.cs` | The zoom as a frustum on the world camera (D31: height 0.2/f above 0.4, 0.5 below; near f/4 below 0.4; no cap) and the rest perspective given back when the override ends. `RestFocal` (f_game) is the camera's own, so `--fov` is honoured. `ViewCentre` is the `GameViewRect` hook (D40), default (0.5, 0.5): the offset is exactly zero, as the owner decided. The sky (priority 20) copies the camera in `SkyView.Follow`; the interiors (priority 5) read it a frame late, which only matters for the first frame of the arrival's expansion. |
| `CameraLens.OffsetForCentre` (a fix) | `Client/Rendering/CameraLens.cs` | It had the signs reversed: the offset moves the frustum's rectangle, so the *picture* moves the other way, and the view's axis (what the zoom collapses to) must land at the frame's centre. Now `(0.5 - cx) x height x aspect`, `(cy - 0.5) x height`. Dormant (nothing applied an offset); `--portal-space-checks` pins it through a real camera at all four focal distances. |
| `WorldAttachments` | `scripts/PortalSpace/WorldAttachments.cs`, `Client/Play/AttachStateMapping.cs` | Terrain from the planner (`Loaded`/`Failed`/else pending), objects from `ObjectsSystem.StateOf` (`Disabled` with `--objects=off`), cells from `InteriorController.IsCellAttached`, visible-cell lists and SeenOutside from the interiors registry (the cell's own array, no allocation), collision `Disabled` (`TODO(portal)`: physics' `CollisionStreamer` is not wired into the scene; a WARNING line says so once per run, since the wait counts collision as settled). Every piece has a settled answer when it can never attach: a failed landblock, a layer that is off, a cell its registered landblock does not contain (`Failed`, not pending for ever). Offline the destination's cell is the viewer's (EnvCell from the tracker, else the outdoor cell under the camera, else 0 = never ready, warned every 10 s). |
| `OfflinePortalTransition` | `scripts/PortalSpace/OfflinePortalTransition.cs` | `ITeleporter` over `ExploreTransition.Begin`; `Install` sets it on `SkyUi/TeleportPanel` when the panel exists (it shows the "Portal transition" toggle), with `Enabled` from `--portal-transition`. The panel (priority 0) calls the hook and the presenter steps later in the same frame (tunnel up, world hidden, busy); the camera move is queued and runs at the start of the next frame (§9), with `Pending` true meanwhile and the destination cell 0. |
| `PortalNoticeLine` | `scripts/PortalSpace/PortalNoticeLine.cs` | D32: the `IPortalNoticeSink`, covering only runs without the shell (with the shell, play mode gives the notice to the shell's spew box: `PortalNoticeLine.Shell`), a label at the lower left of the window (canvas layer 8) with "In Portal Space - Please Wait..." and a second, smaller line saying it is a placeholder for the retail spew box. Shown while the tunnel shows (states 2 to 5), gone when the world is back (m2-log-in §4.1 step 3, §4.4); the frame `04-mid-arrival` no longer carries it. |
| `PortalSpaceFramesRunner` | `scripts/PortalSpace/PortalSpaceFramesRunner.cs` | `--portal-space-frames[=DIR]` (default `docs/screenshots/m2/portal-space`): waits for the start place (Holtburg) to be complete, hides the debug overlays, jumps to Arwic through the teleport hook at a fixed 1/60 s step and seed 1, and saves four frames at a state and an elapsed time: `01-mid-expand` (state 2, 0.5 s), `02-in-tunnel` (state 4, 1.0 s), `03-mid-collapse` (state 5, 0.5 s), `04-mid-arrival` (state 6, 0.5 s). Frames 1 to 3 are byte-identical from run to run; frame 4 shows the live world (living statics and particles run on the real clock). Verdict, frozen: `portal space frames done captured=4 failures=0 max_frame_ms=<x>` (exit 0 only then). A failure is a missed or blank frame, a sequence that does not end, the world or lens not given back, a LoginComplete sent offline, or the transition switched off. `slow_frames=<n>` follows `max_frame_ms=<x>`: a frame over 50 ms that is not the runner's own capture fails the run. A frame is the time between two presented frames (post-draw to post-draw) less the runner's own capture (the GPU readback and the PNG encode, 100 to 300 ms with the first capture: **that, not the tunnel, was the "jump frame of about 180 ms" earlier runs reported**, because the interval after each capture included it). Logged on their own lines: the start frame (the hook's frame), the jump frame (the camera move) and the first arrival frame, each split into `process_ms` and `draw_ms`, and one `frame times` line per state. Measured off-screen at 1280x720 on the development machine: a bare jump with no tunnel 40 ms (process 31, draw 4); the hook with the move in the same frame 52 to 66 ms (move 18, director step 4, apply 2, the world's reaction, a first tunnel draw of 12 against 1 to 3 in steady state, which is not the pipeline warm-up: a few real frames at start-up did not change it); with the move on the next frame, the start frame 20 to 29 ms and the jump frame 35 ms, steady tunnel frames 1 to 6 ms (the hold up to 27 ms at the hum), the first arrival frame 8 to 10 ms. The run's own limits (60 s to settle, 120 s to run) stay under the smoke step's 240 s. |
| `PortalSpaceChecks` (extended) | `scripts/PortalSpace/PortalSpaceChecks.cs` | 54 checks, verdict lines `[portalcheck] PASS\|FAIL <name>` and `all portal-space checks hold`. W5 added: `WorldLens` on a real camera (rest lens, zero offset for the whole window, an off-centre `GameViewRect` puts the axis at its centre, release, `--fov`); byte-space composition readback (a `CreatureModeView` composes in byte space and the window shows its picture unchanged); the UIASSET chain in the engine's decode (ids, the Setup's two parts and their creature-mode meshes, the spin's 120 frames and 1 to 119 range, the sound table and the hum wave); a readback of the lit tunnel view at six spin frames (the wall's blue-grey covers at least 8 % of the picture in every frame and the small purple shapes show in at least one, and a view with every part hidden fails both, so a missing surface fails; the look itself is not judged); the portal waves as 11,025 Hz mono 16-bit `AudioStreamWav`s with length within 1 ms (and panned, same length); the audio buses (UI and Effects into Master), master level, mute, automated-run silence and a voice on the bus it was given; three loops of the spin starting the hum three times through the real audio service; a scripted 2 to 0 offline sequence with no LoginComplete (suppressed, counted) and the notice posted; the placeholder line; and (the review fix) the real presenter on a test camera, an arbiter over a `SubViewport`, the real tunnel view and the notice line, stepped through one fixed-clock jump, asserting in every frame `Disable3D`, the tunnel shown and lensed in states 2 to 5, the notice line shown exactly while the tunnel is, the world lens only while the world is drawn and released at state 0, and the busy state told once on and once off (it fails with the old notice rule: the line stayed up through state 6); and the hook's deferred camera move (not run when the hook returns, not run before the presenter arms it, run once at the start of the next frame, a second call runs the first at once and in order, off runs at once). The earlier checks (camera roll, widest lens, world arbiter, lighting against hostile world globals, lens matrices and the interiors' frustum at f_game, 0.4, 0.01, 0.001, the zoom's floor renders, the sky view) stay. |
| Smoke steps | `tools/smoke.ps1` | `portal-space-checks` (verdict `all portal-space checks hold`) and `portal-space` (`--portal-space-frames=<OutDir>\portal-space`, verdict `captured=4 failures=0`), after `blend-checks`. |

`PortalSpaceHold` (the W3 placeholder driver) is gone; the presenter replaces it. Engine messages to expect in a transition's log: `Condition "!res" is true` from
`rendering_light_culler.cpp` (`create_frustum_points`) while f is below about 0.01 (§6); they are not Lifestone ERROR lines.

## 12. Options (for the owners to register; exact `OptionSpec`s)

| Group (file, owner) | `OptionSpec` |
|---|---|
| Portal space (`OptionCatalog.PortalSpace.cs`, G1; Main) | `new("--portal-transition", "on\|off", "Play portal space when the teleport panel jumps (default on). Check suites, golden viewpoints and tours never play it.")` |
| | `new("--portal-space", null, "Hold the portal tunnel (state 3) until quit, for review.")` |
| | `new("--portal-space-frames", "DIR", "Play a scripted portal-space sequence at a fixed step and seed, capture four review frames (default docs/screenshots/m2/portal-space) and quit.", OptionRole.EndsRun, ValueOptional: true)` |
| | `new("--retail-logoff-tunnel", null, "Retail's logoff tunnel: the tunnel ends and the world shows again before the character list arrives (default: hold the tunnel until it does, D28).", AlsoIn: OptionScenes.Headless)` |
| Audio (`OptionCatalog.Audio.cs`, G1; all scenes) | `new("--mute", null, "Silence all sound.")` |
| | `new("--volume", "0..1", "Master volume, linear (default 1; automated off-screen runs are silent unless it is given).")` |
| Video capture (`OptionCatalog.Video.cs`, lead; Main) | `new("--record-audio", "FILE", "Record the game's own sound to FILE, a relative .wav path under cache/ (retail sounds are derived data, never committed), for short clips: the recording is held in memory and written when the game quits normally (--quit-after, a check's own quit or closing the window); a killed process writes nothing. The Master bus is recorded before its level, so a silent automated run records too and nothing reaches the speakers. The log's 'audio recording started' line gives the start's wall-clock time (unix_ms) for lining the sound up with a screen capture; that holds with a real audio driver only (-Offscreen's Dummy driver drifts from the wall clock). A run without the audio service (a check suite, --retail-ui-only, a failed start) logs an error and records nothing.")` |
| | `new("--clean-hud", null, "Hide the diagnostic text over the world, for videos: the position line, the F3 overlay, the objects rows, the interiors line and the player's key hints. Placeholder notices stay.")` |
| Checks (`OptionCatalog.cs`, lead; registered in W2-pre(a)) | `new("--portal-space-checks", null, "Run the portal-space checks and quit.", OptionRole.CheckSuite)` |

`--retail-logoff-tunnel` (D28) is not in m2-plan.md §5.4's table; it belongs with the portal-space options. No option
takes a secret.

## 13. Logging

| Category | Severity | When |
|---|---|---|
| `portal` | WARNING | every 10 s while state 3 waits for an update that is not held, with the reason: the readiness report, the server's position still awaited, or no player (D30) |
| `portal` | WARNING | once per arrival with failed pieces, naming the first |
| `portal` | WARNING | a player-killer logoff delay requested (the TODO of D33) |
| `portal` | WARNING | every 10 s while D28's logoff hold waits for the character list (`TODO(portal-logoff-hold)`; `PortalSpaceDirector.LogOffHoldWarnings` counts them) |
| `portal` | INFO | each LoginComplete sent or suppressed |
| `audio` | WARNING | once per missing table, type or wave, malformed record or unsupported format |

## 14. Lifestone's fixes and choices

**The light culler's `create_frustum_points` ERROR.** Godot's light culler builds the eight corners of the camera frustum by intersecting its planes and logs `Condition "!res" is true ... create_frustum_points (rendering_light_culler.cpp:682)` once per frame the planes are nearly parallel. The portal zoom's frustum is retail's (179.9 degrees, near f/4 = 0.00025 m at f = 0.001; the side planes are almost parallel to the near plane), so every frame of the widest part of the zoom logged one (10 in `--portal-space-checks`, 157 in `--play-checks`), and G4 let the lines reach the live login run, which fails on any ERROR line. The game draws with no Godot light (all lighting is in the shaders), so the culler has nothing to cull: `rendering/lights_and_shadows/tighter_shadow_caster_culling=false` in `project.godot` switches it off (Godot has no per-viewport switch; confirmed by running `--portal-space-checks` with and without the line: 10 errors, then 0). The lens is not clamped or reshaped.

| Decision | Default | Retail behaviour |
|---|---|---|
| D27 exit window | frames 68 to 76 (retail's float arithmetic, settled from the 11.6096 machine code) | the same |
| D28 logoff tunnel | held in state 3 until the character list resets portal space | Ends in state 3 and runs out to state 0, sending LoginComplete, unless the UI is torn down first (`LogOffTunnelPolicy.Retail`) |
| D29 seen-outside interiors | also wait for the outdoor 3×3 | prefetch of the landscape there |
| D30 never-ready destination | wait, with a WARNING every 10 s; no cap | wait |
| D33 player-killer logoff delay | not applied, logged as a TODO | +20 s when a check on the weenie holds (unverified) |
| Seeding | one `rand()` and one `Random` per consumer, seeded by the host | one of each, shared by the whole client |
| Wave loading | on first use; every outcome cached, a malformed record's included (decoded once; the player warns once and asks no more) | when a table or hook naming it loads |
| Tunnel stepping | retail's: `CPhysicsObj::update_position` in `CreatureMode::Render` (§5), so the spin moves 20 to 30 times a second at 60 fps and the exit window reads the frame of the last step; the clock outlives a restart | the same. Not a glitch fix: the frame numbers, and so the exit frame, stay retail's. Smoothing, if ever wanted, belongs to the drawing alone (interpolating `PartFrames` between steps), behind a flag, and must not touch `CurrentFrame` |
| The spin's wrap | kept: frames 1 to 119, so the 119 → 1 step turns 3°, once per 2.975 s loop | the same. Retail behaviour, not treated as a glitch: one extra 1.5° every 3 s is not visible at the spin's speed (60° a second), and the exit window's (120 − frame) / 40 arithmetic is defined on the 1-to-119 loop |
| LoginComplete's inventory wait | sent at once on the cue (the tunnel's 5 s or more normally covers the inventory's arrival); a `TODO(portal)` on `PortalSessionBridge.SendLoginComplete` | `CPlayerSystem::SendLoginCompleteNotification` sends only when the player's weenie exists and `ACCWeenieObject::AllContainedObjectsExist`; otherwise it sets `sendLoginCompletePending`, and `CPlayerSystem::AttemptSendLoginCompleteNotification` sends it once the player description has arrived and every contained object exists |

## 15. Corrections to the specification (for the lead to carry into m2-log-in.md)

1. **Exit window**: frames 68 to 76 under retail's float arithmetic, not 69 to 75 (§4.5; D27).
2. **Roll**: `RandDouble` over the C runtime's `rand()`, both ends included ([0.6, 1.8] s, [0°, 360°]), the duration
   drawn first; not `RollDice` over [0°, 360°) (§4.4).
3. **FOV clamp**: [0.001, π], not (0, π]; the near-plane threshold is the float 0.40000001 (§4.6).
4. **Begin** also resets the current focal distance to f_game; it zeroes the roll's segment, not the current angle
   (§4.5).
5. **F751** is accepted when its sequence is not older (equal included), not only when newer (§4.7).
6. **"Not blocking for cells"** is a data-prefetch state in retail; Lifestone's attach-based rule is an adaptation
   (§4.7).
7. **Logoff**: retail Ends in state 3 and survives only because the character list tears the UI down; D28 holds the
   tunnel instead (§4.8).
8. **Hiding** (§4.9): an object created Hidden plays the Hidden script at creation, and `set_hidden` also changes
   IgnoreCollisions and ReportCollisions and its children's NoDraw.
9. **Sound** (§4.10): the last variant of a sound never plays (one sound in the dats); the probability roll uses
   `rand()` with the float 1/32767 (not 2⁻¹⁵); interface sounds follow the effects slider; attenuation is whole dB,
   rounded up, with nothing below −50 dB (0.01 is −40 dB, and 0.003 still plays at −50); sound is 2D and pans at most
   ±14; table and SoundTweaked priorities are never used; a Sound hook scales with the slider's square;
   DatReaderWriter's sound-table model holds only with no grandchildren (true in the dats).
10. **The hum** is heard from the tunnel camera, 2.85 m away: centred, at −10 dB.
11. **"Notice 0x1A"** is text type 26 for the spew box (`gmSpewBoxUI`), not a notice id (§4.4).
12. **The spin's wrap** turns 3°, not 1.5°: retail starts the loop at frame 1 (§4.4).
13. **A teleport during the arrival** shares that arrival's LoginComplete when its update completes before state 6
    ends, and runs the tunnel again otherwise (§4.5).
14. **The tunnel steps** with `CPhysicsObj::update_position` (11.4186 0x00514520, 11.6096 0x00515020), called by
    `CreatureMode::Render`, not with the world's `update_object`: at most one step per 1/30 s, of the whole elapsed
    time, and the object's clock carries over a restart of the spin (§4.4; §4.5's exit window reads the last step's
    frame).
15. **LoginComplete** (§4.5, 6 → 0): retail's `CPlayerSystem::SendLoginCompleteNotification` (11.4186 0x00562E90,
    11.6096 0x00563C30) is conditional. It sends only when the player's weenie exists and all its contained objects
    exist; otherwise `CPlayerSystem::AttemptSendLoginCompleteNotification` (11.4186 0x00562F20, 11.6096 0x00563CC0)
    sends it later, once the player description has arrived and the contents exist. Lifestone sends on the cue
    (§14); the state track's `IActionApi.LoginComplete` is the place for the rule.
    **Resolved by M3 1.2 s5:** the rule lives in `LoginCompleteTracker.WaitReason` (Client/Session/Actions/Trackers), with a 3 s cap from the call; the director's cue is unchanged and remains the trigger.
16. **The zoom's lens** (§4.6): Godot's `Camera3D.fov` is capped at 179° and f = 0.001 is about 179.885°, so the zoom is an exact Frustum projection
    (D31): height 0.2/f above f = 0.4 and 0.5 below it, near plane f/4 below 0.4, no cap, centred on `GameViewRect` (D40; §6).

### 15.1 Corrections to m2-plan.md (for the lead to carry into it)

1. **G1 W4 plays no portal sound** (m2-plan §5.3). W4 says the presenter maps the cues to
   `ISoundSink.PlayFromCenter(UISoundTable, 0x6A/0x6B)`. The director plays the whooshes (`PlayEnterPortal`,
   `PlayExitPortal`) and the tunnel's hum itself whenever it has an `ISoundSink`, and §11.2 gives it one, so a presenter
   that also plays them plays each whoosh twice. The presenter does nothing for these cues (§11.1); without a sink (the
   headless host) portal space is silent. W4 should read "cues: none for sound (the director plays them)".
2. **`SoundOrigin`'s bearing** (the C-portal contract, m2-plan §4.8, left its direction to PS10) is retail's
   source-to-listener heading minus the listener's heading (§7.2), not the listener-to-source bearing; the two differ
   by 180° and mirror the pan. Every consumer that builds a `SoundOrigin` from two positions calls
   `SoundOrigin.Between(listener, source)`. The avatars track's `AvatarSet.SoundOriginOf` (on master) computes
   `PositionFrames.HeadingTo(viewer, body)`, the listener-to-source heading, so every positioned world sound it routes
   pans mirrored: it should call `SoundOrigin.Between(viewer.Position, body.Position)` (or swap the arguments to
   `HeadingTo(body, viewer)`).

## 16. Tests

| Project, folder | What they pin |
|---|---|
| `Lifestone.Data.Tests/Audio` | the wave and sound-table census (Tier=Exhaustive) and DatReaderWriter agreement; the three portal waves; the UI table's whooshes; malformed records, and the cache that decodes each once |
| `Lifestone.Client.Tests/Audio` | the CRT generator and `RandDouble`; the variant, probability, attenuation and pan rules; the voice pool; the six entry points, their gates, draws and warnings; the router; the whooshes and the hum on the real dats; 0 bytes per steady sound |
| `Lifestone.Client.Tests/PortalSpace` | the UIASSET chain and the tunnel's assets on the real dats; AnimLevel, the zoom and the lens; the spin and its `update_position` clock; the flags; the machine's timelines, exit window, cap, logoff, roll and determinism; readiness; the director's frame order, sinks, warnings, offline jump and hum; the bridge's mapping and online login and teleport; the hide rule and its scripts on the real dats; 0 bytes per frame |
| `Lifestone.Client.Tests/Play` | `AttachStateMapping`: every landblock and cell state, so a wait is never stuck (§11.7) |
| `Lifestone.Client.Tests/Rendering` | `CameraLens` sides and offsets, including `OffsetForCentre`'s axis placement |
| `--portal-space-checks` (engine) | the 54 checks of §11.7 |

Tests that need the dats skip without them; nothing writes audio or dat-derived data to disk.
