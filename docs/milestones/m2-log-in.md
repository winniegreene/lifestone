# Milestone 2: log in

> **Handoff for the agent who leads M2** (written 2026-10-11, at the end of M1). Read [`CLAUDE.md`](../../CLAUDE.md)
> first: its hard rules are not negotiable. M1's "Architecture rules" and "Fidelity rules"
> ([`m1-explore-dereth.md`](m1-explore-dereth.md)) still apply unchanged, except the Client rule, amended in
> [`game-core.md`](../game-core.md) §1.1.

**Goal:** play the real game in Lifestone for the first time. The client:
- logs in to the bundled localhost ACE;
- creates or selects a character;
- enters the world through retail **portal space**;
- shows everything the server spawns;
- walks and teleports, with ACE accepting every move;
- does all of it in the retail user interface, rebuilt from the user's dats (§5).

The foundation of the built-in automation (phase A0) ships with it.

**The owner's requirement for this milestone: the old-school Asheron's Call portal effect at login.** It has three
parts:
- retail's purple portal-space tunnel;
- its sounds;
- the zoom out of the old view and into the new one.

It plays on login, on every teleport and on logoff. §4 specifies it, researched from the retail client (11.6096) and
the retail dats.

## 1. Where M1 left off

- **State.** M1 is complete, pending the owner's review ([status](m1-explore-dereth.md#status)).
  - `master` has four commits: M1, as one commit, and the M2 handoff documents.
  - The build has 0 warnings.
  - `dotnet test` passes (1,270 tests: Client 551, Data 719).
  - `tools\smoke.ps1` passes all 13 steps.
- **What M2 builds on.**
  - **`Lifestone.Data`.**
    - Every dat decoder M1 uses.
    - `DatSet.Iterations` holds the dat iterations that DDD must report.
    - `TextureLoader` already takes ObjDesc palettes (`PaletteOverride`) and texture swaps (`surfaceTextureOverride`).
    - Setup posing.
    - `Animation/` holds `AnimSequence`, the motion-table logic (`MotionTableManager`, `MotionPlayer`) and the hook
      decoders (`AnimHooks.cs`, including `SoundTweakedHook`).
  - **`Lifestone.Client`.**
    - Streaming, the worker pool and the frame budget.
    - Object LOD.
    - Living statics. Their sound hooks become events that are dropped today (`Objects/LivingStatics.cs`).
    - Interior visibility, the sky director, navigation and the option registry.
  - **`Lifestone.Game`.**
    - The world renderer and the teleport panel.
    - The spell viewer's actor path, which draws a Setup part by part and plays physics scripts
      (`SpellViewer/ActorMeshLibrary.cs`, `ActorView.cs`).
  - **`server/`: the backend.**
    - Setup, start and stop were verified in M1.
    - **Nobody has logged in yet.** ACE would make the first account an Admin. M2 prevents that, because real play
      uses a normal account (§7, decisions 3 and 8).
  - **[`docs/protocol.md`](../protocol.md): M2's wire spec.**
    - The stack decision (§0, §9).
    - The session flow (§4) and the message set (§5).
    - What ACE checks about movement (§6).
    - The 11.6096 command shift (§7).
    - Golden vectors (§10) and the open risks (§11).
  - **[`docs/automation-vision.md`](../automation-vision.md)** specifies A0 (§1–§2, §4).
- **Not there yet:**
  - networking and audio;
  - a player character and server objects;
  - collision;
  - any game UI beyond M1's panels.

## 2. Definition of done

1. **One command to play.** `tools\play.ps1`:
   - starts the backend if it is not running (`server\start-server.ps1`, which returns once ACE is up);
   - opens the client, which signs in by itself (item 2);
   - stops the backend on exit, if it started it.

   The session layer refuses any server endpoint that is not loopback.
2. **Log in.**
   - The handshake, ISAAC, acks, NAKs and TimeSync follow protocol.md §2 and §4.1, including the ConnectResponse race.
   - There is no login screen, as in retail, where the launcher signed in. Lifestone signs in with the play account
     kept in `server/runtime/secrets.json`, which setup creates with a random password. `--account` picks another.
   - The retail data-check screen shows during the DDD exchange (§5.3).
   - No password ever reaches a log, the console, a screenshot, a session recording or a test fixture.
   - DDD reports the iterations read from the dats at run time (`DatSet.Iterations`). A mismatch or a boot shows
     retail's disconnected screen with the reason, instead of hanging.
3. **Characters**, in the retail screens (§5.3):
   - **character management, retail-exact:** the list from F658, and enter, delete and restore. Retail's character
     screen has no 3D view of the selected character (`gmCharacterManagementUI`), and neither has Lifestone's (§7,
     decision 6);
   - **character creation** with all six retail pages: heritage, profession, skills, appearance, town and summary.
     - It is driven by CharGen 0x0E000002.
     - It keeps retail's 3D view of the character (`gmCG3DView`). As retail, the view loops its idle animation on the
       summary page and on the appearance page when zoomed out, and holds the rest pose when zoomed in; the idle
       emotes are never played (§7, decisions 12 and 14).
     - A new character enters the world at once, as retail does (§7, decision 19).
     - It sends a valid CharacterCreate (exactly 55 skills).
     - It shows ACE's errors (name taken, invalid name) in retail dialogs.
4. **The retail user interface** (§5), rebuilt from the dats. It covers the login flow and the in-game shell, with
   retail layouts, art, fonts, strings, cursors and key bindings. Panels whose content needs gameplay say so in a
   retail dialog. UI sounds are wired but are not part of M2's definition; they come with full sound in a later
   milestone (§7, decision 16).
5. **Portal space**, exactly as §4 specifies. It plays:
   - on entering the world;
   - on teleports;
   - on logoff;
   - offline, in explore mode.
6. **Server objects.** Everything ACE creates is drawn: CreateObject, UpdateObject, DeleteObject, UpdatePosition,
   SetState, ParentEvent, PickupEvent and ObjDescEvent.
   - Each object is drawn with its Setup and ObjDesc (palettes, texture swaps and part swaps), placed in its cell.
   - It idles in its motion table's default state. UpdateMotion is played at least for the default and locomotion
     states.
   - It moves smoothly between position updates.
   - Hidden objects follow §4.9.
   - Holtburg shows its lifestone, portals, NPCs, signs and doors. The Training Academy shows its NPCs and creatures.
7. **Your character.** The player is drawn from its own CreateObject: body, clothing and colours.
   - Its motion table animates it: ready, walk, run, turn, sidestep, jump and fall.
   - It has a third-person chase camera (orbit and zoom) and a first-person view.
8. **Movement ACE accepts.**
   - Keyboard movement sends:
     - MoveToState on every change;
     - AutonomousPosition on retail's cadence (found: every 1.0 s, compared strictly, when the position changed, `ShouldSendPositionEvent`, docs/physics.md §12; the "under 50 m per update" figure has no source);
     - Jump.
   - The player follows the ground outdoors and the floors of EnvCells indoors. It tracks its cell through portals
     and across landblocks, and it collides with walls.
   - Port the movement physics from ACE's port of the client physics (`ACE.Server/Physics/`). It is AGPL-3.0, so it
     is compatible; ported files carry the usual header.
   - **Scenario.** These walks produce **zero** server corrections (no force-position increments), and the tools'
     **Where am I** (item 10) agrees within 0.5 m:
     - three laps of a scripted route around Holtburg's lifestone;
     - a walk through the Academy's exit wing to its exit portal (the spawn cell and the wing are connected only
       through quest-gated portals, docs/session.md §25; §7, decision 18).
9. **Teleports.** Two paths, neither of which needs admin rights:
   - picking a place in the tools panel (item 10);
   - using a portal (GameAction Use), including the Academy's exit portal.

   Both run F751 → portal space → UpdatePosition → streaming → exit → LoginComplete. The client ends where ACE put it.
10. **Player tools, without admin rights** (§7, decision 3). Real play uses a normal Player account. A tools panel
    gives it admin-like conveniences through the Lifestone server mod:
    - **Places:** a searchable picker with ACE's points of interest (56 destinations under 62 names) and M1's
      curated dungeons and interiors. Picking one teleports through portal space.
    - **Where am I:** the server's view of the player's position. It replaces `@myloc` in the tests.
    - **Add XP**, confirmed by the server in the chat.
    - Every use is written to the chat and the action log. The panel refuses to work against a server that is not
      loopback.

    Item search and add, respec and the other tools come with the gameplay milestone, when inventory and character
    panels can show their results.
11. **Log off.**
    - Retail's logoff sequence (§4.8) returns to the character list.
    - Quitting sends CharacterLogOff, then Disconnect, so ACE drops the session at once.
12. **Robust transport.**
    - The loopback integration test of protocol.md §9.4, item 4, passes.
    - That includes 2 minutes of movement at 5% loss and 2% reordering, with:
      - no ISAAC desync;
      - no checksum failure in the Lifestone server mod's `net` counters beyond the duplicate deliveries the test
        itself counts. ACE's log cannot show them: ACE only counts failed encrypted packets, and logs cleartext ones
        at DEBUG.
    - A stopped server or a timeout shows a message and returns to retail's disconnected screen.
13. **Automation A0** (automation-vision.md §4):
    - the GameState reducer;
    - the event bus, with the raw message tap;
    - the Action API, with completion handles;
    - the session recorder and replayer;
    - a headless session host that logs in and walks a route with no Godot;
    - the loopback gate, with its S8 test.

    The Godot client goes through the same reducer and Action API for items 2–11, the player tools included.
    Nothing in the UI talks to the session directly.
14. **Tests.** `dotnet test` stays green. It now includes:
    - `tests/Lifestone.Net.Tests`: vectors, codecs, a fake ACE, fuzzing;
    - replay tests over recorded sessions;
    - the retail UI tests of §5.5.

    Loopback tests carry `[Trait("Category","Loopback")]` and skip when no backend is running. `tools\smoke.ps1` gains
    four steps:
    - `portal-space-checks`;
    - `portal-space`;
    - `retail-ui`;
    - `login`, which runs when the backend is up.
15. **Frame time.**
    - No frame takes over 50 ms between "Enter" and walking. The tunnel and the streaming share M1's 4 ms main-thread
      budget.
    - Holtburg with its NPCs holds 60 fps or more.

**Not in M2.** Each of these is named in the UI where it would show:
- combat and casting;
- inventory and the paperdoll;
- vendors and trade;
- chat channels beyond local speech, tells and system messages;
- the content of the retail panels that need gameplay (§5.3);
- sounds beyond §4.10;
- packaging.

## 3. Suggested plan

This is advice; the lead decides. **The lead's plan of record is [m2-plan.md](m2-plan.md):** where it differs from
this section (owners, folders, order), it wins. M1's way of working held up:
- parallel tracks in git worktrees, one owner per folder, with the lead owning shared files;
- an adversarial review and a fix pass for each track;
- merges in a fixed order, with the build, the tests and the smoke suite green after each one.

M1 ran its tracks as multi-agent workflows, which cost a lot. Confirm with the owner before starting one. (The owner
confirmed on 2026-10-12, with each wave kept lean: §7.)

**Wave 1** (parallel, no Godot):

| Track | Owns | Delivers |
|---|---|---|
| net | `src/Lifestone.Net/`, `tests/Lifestone.Net.Tests/` (new) | <ul><li>OpenAC's transport vendored at the pinned commit, with its four defects fixed (protocol.md §8.2, §9.1)</li><li>`AcSession`</li><li>codecs for the §5 message set</li><li>`MotionCommandTable`</li><li>the §10 vectors, a fake ACE and fuzzing</li></ul> |
| state | `src/Lifestone.Client/Session/`, a headless host project (new) | <ul><li>A0: the reducer, event bus and Action API</li><li>the recorder and replayer</li><li>the loopback gate</li><li>the headless host</li></ul> |
| portal | portal-space and audio folders in `Lifestone.Data` and `Lifestone.Client` | <ul><li>the UIASSET assets, through the enum-map resolver that the lead builds in wave 0 and portal and retail UI share (`Lifestone.Data/Dat/EnumIdMaps.cs`)</li><li>Wave and SoundTable decoding</li><li>AnimLevel, the state machine and the zoom maths</li><li>the readiness-predicate contract</li><li>the tests of §4.12</li></ul> |
| avatars | `src/Lifestone.Data/Appearance/` and `Data/Motion/`, `src/Lifestone.Client/Avatars/` (new) | <ul><li>ObjDesc application (Setup, AnimPartChange, TextureMapChange, SubPalettes) for server objects and the player</li><li>motion-table animation of remote objects (UpdateMotion) and the player</li></ul> |
| physics | `src/Lifestone.Data/Physics/` and `src/Lifestone.Client/Movement/` (new) | <ul><li>Player transitions ported from `ACE.Server/Physics/`: sphere against terrain, cell BSPs and object BSPs; step up and down; cell tracking</li><li>conformance tests against ACE's own results</li></ul> |
| retail UI | `src/Lifestone.Data/Ui/` and `Data/CharGen/` for decoding (m1, "Architecture rules"), `src/Lifestone.Client/RetailUi/` (new) | <ul><li>the layout model of §5: templates, anchoring, states, properties and media</li><li>retail fonts and string tables</li><li>input routing, focus and dialogs</li><li>the tests of §5.5</li></ul> |
| server mod | `server/mod/` (new) and the setup step that builds and installs it | <ul><li>the Lifestone server mod of §7, decision 3: places, where-am-I and add XP</li><li>works only on a loopback-bound ACE (it refuses in `Initialize` otherwise); no Harmony patches; hot reload off</li><li>turns off ACE's first-account auto-promotion</li><li>loopback tests</li></ul> |

**Project rules for M2.** `Lifestone.Client` may use `Lifestone.Net`'s records and interfaces, but it opens no sockets
and does no file I/O: hosts supply bytes and Streams (game-core.md §1). Every session, network and action timeout
reads `System.TimeProvider`.

**Wave 2** (Godot):
- the retail UI renderer, and the M2 screens of §5.3;
- the server-objects layer;
- the player and its camera;
- portal space: the tunnel viewport, the zoom and the sound;
- the tools panel (§2, item 10);
- a minimal automation inspector (A0's action log).

**Wave 3:** a review against this definition of done, then fix tracks and integration.

**Visible results first.** The owner judges progress by what is on screen:
1. portal space offline (it needs no server; see §4.11);
2. the retail screens offline: character management and creation, with stand-in data where the server would supply
   it;
3. logging in, as far as the tunnel;
4. the world with server objects, in the retail shell;
5. walking.

**First steps for the lead.**
1. Read `CLAUDE.md`, this file, protocol.md §0–§6 and §9, and automation-vision.md §1–§4.
2. Build, test and run the smoke suite once to confirm the M1 baseline (§5).
3. Start the backend, check that ACE's log says the world is open, and stop it.
4. Check the pinned sources in `vendor-eval/` (gitignored):
   - ACE at `95ef426d` (v1.78.4816);
   - OpenAC at `1ee5a08`.

   Re-clone them at those commits if they are missing (protocol.md, "Pinned sources"; `server/README.md`).
5. Teach `DatSet` to decode the language dat's records (§5.1). It needs no `DatCollection`: a reader bound to the
   portal database supplies the master property table. The retail UI depends on it, and `DatSet` is a shared file.
6. Plan the waves, and settle with the owner the open decisions (§7) that block them.

## 4. Portal space

### 4.1 What the player sees

1. On login, the character screen gives way to portal space as soon as the server creates the player, that is
   CreateObject for the player's own GUID. Locally that is a few milliseconds after "Enter". The enter-portal whoosh
   plays.
2. Over 1 s, the purple tunnel grows out of the centre of the SmartBox frame (the zoom, §4.6; the frame is the whole
   window, §7 decisions 7 and 11).
3. Inside the tunnel:
   - the tunnel streams past;
   - the view rolls slowly to random angles;
   - "In Portal Space - Please Wait..." is shown;
   - meanwhile, the destination streams in, hidden.
4. When the destination is ready, the tunnel holds for at least 2 s more. Then, over 1 s, it shrinks into the centre
   of the frame.
5. The exit whoosh plays, and over 1 s the world grows out of the centre of the frame.
6. The client sends LoginComplete. ACE then un-hides the player, and the character materialises out of pink bubbles
   (§4.9).

Teleports run the same sequence from step 1. Logging off is different:
- the world shrinks into the centre (1 s);
- the tunnel then stays until the server finishes the logoff (§4.8).

### 4.2 Assets

Retail finds these assets through the dats' enum maps, not through hard-coded IDs (`DBObj::GetDIDByEnum` →
`DBCache::GetDIDFromEnum`). The chain is:
1. master EnumIDMap 0x25000000;
2. group 7 → EnumIDMap 0x25000010 ("UIASSET");
3. enum → DID.

Resolve them the same way at run time, and pin the result in a test.

| UIASSET enum | DID | What it is |
|---|---|---|
| 0x10000001 | Setup 0x02000306 | <ul><li>`portalspace_background`: the tunnel object.</li><li>Two parts, both GfxObj 0x0100080B.</li><li>No default animation, script or script table.</li><li>Its Default placement frame puts the parts at (51, 0, 0); the animation's frames use (0, 0, 51).</li></ul> |
| 0x10000002 | Animation 0x030005AC | <ul><li>`portalspace_animation`: 120 frames, 2 parts, no position frames.</li><li>Both parts sit at (0, 0, 51) and turn 1.5° per frame about −X. That makes 180° per loop, so the two halves swap places and the loop is seamless.</li><li>A SoundTweaked hook on frame 2 plays Wave 0x0A000316 (the tunnel hum, 3.0 s) at volume 0.3, priority 1, probability 1.</li></ul> |
| 0x10000003 | SoundTable 0x2000004B | `UISoundTable`:<ul><li>`UI_EnterPortal` (0x6A) → Wave 0x0A000246 (1.11 s)</li><li>`UI_ExitPortal` (0x6B) → Wave 0x0A000245 (1.12 s)</li></ul> |

- **GfxObj 0x0100080B** is half a torus.
  - Its centre-line radius is about 50.9 m, and its tube radius about 5.6 m.
  - It has 352 vertices and 360 polygons, and its UVs tile about 43 × 7 times.
  - Surface 0x08000C31: Base1Image | Translucent, translucency 0.25, luminosity 1.0. SurfaceTexture 0x05000E0F →
    high-res RenderSurface 0x06003F29 (64×64, INDEX16). The texture also lists the portal dat's 32×32 fallback,
    0x06003F2A.
  - Surface 0x08000C32: Base1Image, opaque. SurfaceTexture 0x050001B2 → RenderSurface 0x06004D8C (128×128, INDEX16).
  - M1's object pipeline decodes and draws all of this as it is, byte-space blending included. Nothing needs special
    casing.
- **The three waves** are PCM, mono, 11,025 Hz, 16-bit.
- **The same UIASSET group** (20 entries, 0x10000001–0x10000014; the table above and this list name the first 11 and
  the next 9) holds character-screen assets that M2 can use:
  - PlayerIcon 0x0600127E (enum 0x10000004);
  - PaperDollAnimation 0x030003C0 (0x10000005), CharGen's preview rest pose, played at 0 fps;
  - CharGenAnimation 0x03000001 (0x10000006);
  - the idle emotes (0x10000007–0x1000000B): wave 0x030000AC, yawn 0x03000540, shrug 0x03000508, akimbo 0x03000523
    and tap foot 0x0300052E. Retail's `gmCG3DView` loads them and never plays one (§7, decision 12);
  - the character-creation art: PaperDollHitTest 0x06004D8B (0x1000000C), ColorBullet 0x06004D46 (0x1000000D),
    ColorRing 0x06004D45 (0x1000000E), ColorEmpty 0x06005DA5 (0x1000000F) and GradientPlug 0x06005DA6 (0x10000010);
  - the Olthoi heritage's CharGen animations (0x10000011–0x10000014): 0x03000045, 0x03000047, 0x03000992 and
    0x0300098F.

### 4.3 The scene (`gmSmartBoxUI::PostInit`)

- **The viewport.** Retail uses a separate viewport (UI element 0x10000436) with its own CreatureMode scene, which
  holds only the tunnel object, at the origin. It is the Viewport element 0x10000436 of the `classic_smartbox`
  layout (§5). In Godot, use a `SubViewport`. A separate `World3D` does not isolate Lifestone's lighting, which uses
  global shader uniforms (`project.godot` `[shader_globals]`, `RenderGlobals.cs`), so the tunnel gets its own
  CreatureMode lighting globals: no sun, no fog and degrades disabled, as `CreatureMode::Render` sets them.
- **Lights.**
  - Ambient (0.3, 0.3, 0.3): CreatureMode's default (`CreatureMode::CreatureMode`).
  - One distant light, intensity 2.0, direction (0.3, −1.9, 0.65) as retail gives it (not normalised; retail's
    `LIGHTINFO::SetDirection`, 11.4186 0x00452700, 11.6096 0x00452740, normalises it).
  - Use M1's object lighting (render-contract.md §2.1) and the sun's direction convention.
- **The camera** sits at (0.24, −2.7, 0.88), looking along +Y.
  - That puts it inside the tube, near its lowest point, looking down the tube, which curves upward ahead.
  - The FOV is the game camera's (`CreatureMode::UseSmartboxFOV`), including the zoom of §4.6.
- All coordinates here are AC coordinates (Z up). Convert only through `AcToGodot`.

### 4.4 Inside the tunnel (`gmSmartBoxUI::UseTime`)

- **The spin.** On the first frame the tunnel is shown, the tunnel object starts Animation 0x030005AC, interrupting
  anything else.
  - It loops from frame 1 to the last frame (119) at 40 fps: `CPhysicsObj::set_sequence_animation` with start frame
    1. That is 2.975 s per loop. Retail never shows frame 0 again, so the 119 → 1 step turns 3°, not 1.5°; kept, as
    retail has it.
  - Use M1's `AnimSequence`, so that the frame-2 sound hook fires once per loop.
  - The tunnel steps with `CPhysicsObj::update_position` (11.4186 0x00514520, 11.6096 0x00515020), which
    `CreatureMode::Render` calls, not with the world's `update_object`: at most one step per 1/30 s, of the whole
    elapsed time, and the object's clock carries over a restart of the spin. At 60 fps the spin moves 20 to 30 times a
    second, and the exit window (§4.5) reads the frame of the last step.
- **The roll.** The camera rolls about its view axis: +Y, the y component of `CreatureMode::SetCameraDirection_Degrees`.
  - Each segment eases from the current angle to a random target in [0°, 360°], over a random 0.6–1.8 s. Both draws
    are retail's `RandDouble` over the C runtime's `rand()`, the duration first, with both ends included, and the easing
    is AnimLevel (§4.6).
  - When a segment ends, the next one starts from there.
  - The angle is interpolated linearly, without wrapping, so 350° → 10° turns back 340°.
  - Every Begin zeroes the roll's segment, not the angle in use.
- **The message.** Each new roll segment (re)posts "In Portal Space - Please Wait..." as a UI notice
  (`ECM_UI::SendNotice_DisplayStringInfo`). The 0x1A is the text type, 26, not a notice id: retail shows it in the
  spew box (`gmSpewBoxUI`, class 0x10000016). With the in-game shell the notice goes to the shell's spew box; the status
  line that is labelled on screen as a placeholder (m2-plan D32) now covers only runs without the shell. There is no retail capture to compare with (§7, decision 4).
- **The world** is not drawn while the tunnel shows (`SmartBox::Hide`). It keeps streaming, though: that is the point
  of the tunnel.

### 4.5 The state machine

Sources: `gmSmartBoxUI::UseTime`, `BeginTeleportAnimation` and `EndTeleportAnimation`. States keep retail's numbers,
times are client seconds, and t is the time in the state divided by 1.0 s.

| # | Retail name | Shows | Zoom (§4.6) | Leaves when |
|---|---|---|---|---|
| 0 | TAS_OFF | world | none | a trigger below fires |
| 1 | TAS_WORLD_FADE_OUT | world | the world shrinks into the centre | 1.0 s → 2 |
| 2 | TAS_TUNNEL | tunnel | the tunnel grows from the centre | 1.0 s → 3 |
| 3 | (in the tunnel) | tunnel | none | the teleport is no longer in progress → 4 |
| 4 | (holding) | tunnel | none | → 5 when either:<ul><li>≥ 2.0 s in state 4 and the tunnel's frame is 68 to 76: the time left in the loop, (120 − frame) / 40, is between 1.1 and 1.3 s under retail's float arithmetic (0.1 and 0.3 are floats added to 1.0 in double; 67 and 77 are out). Exact arithmetic would give 69 to 75;</li><li>5.0 s in state 4.</li></ul> |
| 5 | (leaving the tunnel) | tunnel | the tunnel shrinks into the centre | 1.0 s → 6 |
| 6 | (arriving) | world | the world grows from the centre | 1.0 s → 0 |

- **Begin(state).**
  - If the machine was off, remember the game's focal distance f_game (§4.6); otherwise reset the current focal
    distance to it.
  - Zero the roll's segment (not the angle in use).
  - Enter the state and stamp its start.
  - Play `UI_EnterPortal` (2D, centred).

  Logoff and teleports both begin this way.
- **Triggers**, checked every frame before the states advance:
  - A logoff whose time has come (§4.8): Begin(1), and mark the teleport in progress and the logoff started.
  - "Teleport in progress" (§4.7) turns on: mark it, then Begin(2).
  - It turns off while in state 3: End. That means state 4, a new stamp, and the zoom override cleared. If it turns
    off during state 2, it takes effect on reaching state 3, so a fast load still plays the whole sequence.
- **Entering a tunnel state (2–5) while the tunnel is hidden:**
  - start the spin;
  - place the camera;
  - show the tunnel;
  - hide the world.
- **5 → 6:**
  - keep the zoom at its current, collapsed value;
  - hide the tunnel and show the world;
  - stop the tunnel's animation;
  - play `UI_ExitPortal`.
- **6 → 0:**
  - send LoginComplete (GameAction 0x00A1; `CPlayerSystem::SendLoginCompleteNotification`). Retail's send is
    conditional: it goes out only when the player's weenie exists and all its contained objects exist, else
    `CPlayerSystem::AttemptSendLoginCompleteNotification` (11.4186 0x00562F20, 11.6096 0x00563CC0) sends it later, once
    the player description has arrived and the contents exist. Lifestone sends on the cue, with a
    `TODO(portal)`; the state track's `IActionApi.LoginComplete` is the place for the rule;
  - clear "teleport in progress";
  - clear the zoom override;
  - consume the "teleport occurred" flag.
- **In state 0**, if a teleport occurred without the animation (`SmartBox::teleport_occured`), send LoginComplete at
  once.
- **Timing.** The whole sequence takes at least 1 + 2 + 1 + 1 = 5 s plus the load, and at most about 8 s plus the
  load.
- LoginComplete goes out exactly once per teleport, and never before state 6 ends. A teleport during the arrival
  shares that arrival's LoginComplete when its update completes before state 6 ends, and runs the tunnel again
  otherwise.

### 4.6 The zoom and AnimLevel

- **Retail's focal distance.** The renderer works with a focal distance f = 1 / tan(FOV / 2).
  - `SmartBox::GetOverrideFovDistance` returns the game's f when no override is set.
  - `Render::set_vdst` turns f back into FOV = 2·atan(1/f), clamped to [0.001, π].
  - It also moves the near plane to f/4 when f < 0.40000001 (a float; 0.1 otherwise).
  - **Careful:** the decompiler prints this atan2 with its arguments swapped. The 11.6096 machine code at 0x0054BE50
    (`fld 1.0`, `fld vdst`, `fpatan`) confirms atan(1/f).
- **The formulas.**
  - Collapse (states 1 and 5): f = f_game + L(t) · (0.001 − f_game).
  - Expand (states 2 and 6): f = 0.001 + L(t) · (f_game − 0.001).
- **The effect.** At f = 0.001 the FOV is about 179.9°, and the whole view sits in a few pixels at the centre of the
  SmartBox frame (§7, decision 7). Growing f back to f_game pulls the view out to fill the frame.
- **Which camera.** The zoom applies to the world camera in states 1 and 6, and to the tunnel camera in states 2 and 5.
- **Godot (D31: the exact frustum, no cap).** `Camera3D.fov` is vertical and capped at 179°, and f = 0.001 is about
  179.885°, so the zoom does not use it.
  - The projection is a Frustum with retail's height at retail's near plane: 0.2/f above f = 0.4 and 0.5 below it
    (2·near·tan(FOV/2)), near = f/4 below 0.4 and 0.1 above, centred on the SmartBox frame (`GameViewRect`, D40).
  - f_game is Lifestone's own (`--fov` is honoured); a uniform focal scale does not depend on which axis retail
    measured its FOV on.
  - The camera's own perspective is given back when the override ends (docs/portal-space.md §6).
- **AnimLevel** (`UIGlobals::Init`, `UIGlobals::GetAnimLevel`) is retail's easing for UI animations. It is a 100-entry
  table, built in integers:
  - raw[i] = ⌊1024 · sin(i · 3.141592 / 99)⌋;
  - level[i] = ⌊1024 · (raw[0] + … + raw[i]) / (raw[0] + … + raw[99])⌋;
  - L(t) = level[⌊99 · clamp(t, 0, 1)⌋] / 1024.

  It is a stepped ease-in-out close to (1 − cos πt) / 2: 0.143, 0.5 and 0.856 at t = ¼, ½ and ¾. Build the table the
  same way; later UI animations reuse it.

### 4.7 When a teleport is in progress

Retail's flag means "the player object exists and its position update is not complete"
(`SmartBox::teleport_in_progress`).
- **Login.** CreateObject for the player's own GUID places the player and applies its position as a teleport
  (`SmartBox::HandleCreateObject` → `PlayerPositionUpdated` with teleporting set). The flag turns on, and the tunnel
  starts.
- **Teleport.** F751 PlayerTeleport with a teleport sequence that is not older, equal included
  (`SmartBox::HandlePlayerTeleport`):
  - clears "complete" and sets "waiting for the new position";
  - the UpdatePosition that follows is applied as a teleport (`TeleportPlayer` → `PlayerPositionUpdated`), which ends
    the wait;
  - a SetState that clears Hidden on the player also ends it (`SmartBox::DoSetState`).
- **Complete.** Every frame (`SmartBox::UseTime`), the update completes once all of these hold: the cell manager is no
  longer blocking for cells, there is a player, and nothing is being waited for. The flag then turns off, and the exit
  begins. In retail "blocking for cells" is a data-prefetch state of the cell manager, not a wait for attached
  geometry; the rule below is Lifestone's adaptation.
- **Lifestone's "not blocking for cells"** is one readiness predicate in `Lifestone.Client`, tested on its own.
  - Outdoors: the player's landblock and its 8 neighbours have their terrain, statics, scenery and collision attached.
  - Indoors: the player's cell and every cell in its visible-cell list are attached; a cell that sees outside also waits
    for the outdoor 3×3 (m2-plan D29).
  - A piece that failed or is disabled counts as settled, so a bad landblock never traps the tunnel. A destination that
    is never ready waits, with a warning every 10 s and no cap (D30).
  - Server objects are not waited for. Retail did not wait for them, and they arrive during the 2 s hold.
- **ACE's side.**
  - Until LoginComplete arrives, the player is `Teleporting` and hidden, and every movement message is ignored
    (protocol.md §4.3 row 16, §6).
  - `Player_Location.OnTeleportComplete` then clears Hidden, once the landblock's objects are created.

### 4.8 Logoff

`CPlayerSystem::RequestLogOff`:
- prints "Logging off..." to the chat;
- sends CharacterLogOff at once;
- schedules the logoff transition 3 s later. It adds 20 s when a check on the player's weenie object holds, probably
  player-killer status; verify before relying on it.

When that time comes, state 1 begins: the world shrinks into the tunnel. The tunnel stays until ACE returns the
character list, about 6 s after the request (protocol.md §4.3, row 18). Retail Ends in state 3 and survives only
because the character list tears the UI down; Lifestone holds the tunnel until the list arrives (m2-plan D28), and
`--retail-logoff-tunnel` restores retail's. The +20 s is not applied until it is verified (D33): the check is the
player's weenie virtual slot 7, probably player-killer status, which M2 does not have.

### 4.9 Hiding and materialising

Retail changes an object's visibility in `CPhysicsObj::set_hidden` whenever PhysicsState Hidden (0x4000) changes,
through SetState (F74B) or CreateObject.
- **Hide:**
  - play the object's PhysicsScriptTable entry for PlayScript Hidden (0x76);
  - stop drawing the object, and set NoDraw on its children;
  - set Hidden and IgnoreCollisions and clear ReportCollisions, which ends its collisions.
- **Unhide:**
  - play the entry for UnHide (0x75);
  - draw the object and its children again;
  - clear Hidden and IgnoreCollisions and set ReportCollisions, which starts its collisions.
- **Creation.** An object created Hidden plays the Hidden script at creation (a new object's state is
  0x00400C08, Hidden clear, and the script table loads before the CreateObject's state is applied).
- **Where the scripts come from.**
  - The player's script table comes with its CreateObject; the human Setups have none of their own.
  - For example, table 0x34000004 maps UnHide → PhysicsScript 0x3300032F and Hidden → 0x33000331.
  - M1 already plays physics scripts (in the spell viewer and the living statics).
- **The result:**
  - After LoginComplete, ACE clears Hidden, and the player materialises in the third-person view.
  - Arriving NPCs and other objects do the same.
- **The fade, measured** (decisions D45 and D63; slice f1; docs/avatars.md §17):
  - UnHide script 0x3300032F: one Transparent hook at script time 0, start 1, end 0, over **0.75 s**, linear (`FPHook::Execute`); it also stops the 14 bubble emitters.
  - Hidden script 0x33000331: one Transparent hook at time 0, start 1, end 1, over 0 s (an immediate translucency of 1), and starts the 14 emitters.
  - The client side is done in the data layer: `EffectObject` ramps the object's translucency on the clock (the same values at 30 and 240 fps), ends at exactly
    0 (or at the object's own base translucency, which is a floor), and hands it to the Godot layer as `AvatarDraw.Translucency` and
    `AvatarFrame.PartTranslucency` for the player and every other object. A re-hide mid-fade stops the drawing at once and sets translucency 1 (retail quirk:
    the unfinished older fade hook keeps running unseen, docs/avatars.md §17).
  - **Drawn (slice f2; `TODO(g5-translucency)` closed for the materialise fade):** a part with translucency strictly between 0 and 1 is an instance of its mesh's
    blended twin (SRCALPHA/INVSRCALPHA, no alpha test, no depth write, as retail draws a part with a translucency material), back on the opaque mesh at 0, for
    the player (`PlayerView`) and every other object (`ServerObjectsLayer`); the twins' pipelines are warmed at start-up with the other moving-object variants, so the
    arrival compiles nothing for the fade. The first frame after an unhide is not drawn even after a re-hide mid-fade (a glitch fix; `--retail-unhide-flash`
    restores retail's one opaque frame). The login check reports `fade_ms=` (the first visible frame after the unhide to the first opaque one, accepted from 650 to
    900 ms around the hook's 750) and the frame `02a-materialising`; `--play-checks` checks the same on the stand-in for the player and an unhidden object.
    The first-person fade keeps its own named TODO for M3: `TODO(g5-first-person-fade)`. Details and the draw choices: docs/avatars.md §17.

### 4.10 Sound (new in M2)

M1 has no audio. Portal space needs a small sound path, which later sounds reuse.
- **`Lifestone.Data`.** Decode Wave (0x0A): the WAVEFORMATEX header and the data.
  - PCM goes to Godot's `AudioStreamWAV`.
  - The dats' single MP3 wave can go to `AudioStreamMP3`.
- **SoundTable lookup.** Pick an entry by its probabilities and apply its volume (`CSoundTable`). Retail's rules,
  as docs/portal-space.md §7.2 builds them, differ from a first reading in these ways:
  - the last of several variants of a sound never plays (one sound in the dats has two);
  - the probability roll uses `rand()` scaled by the float nearest 1/32767, not 2⁻¹⁵;
  - interface sounds follow the effects slider;
  - attenuation is whole decibels, rounded up, with nothing played below −50 dB (0.01 is −40 dB, and 0.003 still
    plays at −50);
  - sound is 2D and pans at most ±14;
  - table and SoundTweaked priorities are never used;
  - a Sound hook scales with the slider's square;
  - DatReaderWriter's sound-table model holds only while no node has grandchildren, which is true in the dats;
  - the tunnel's hum is heard from the tunnel camera, 2.85 m away: centred, at −10 dB.
- **Godot:**
  - a UI bus for 2D sounds (`PlaySoundFromCenter`);
  - a sink for animation sound hooks: the tunnel hum now, and later the living statics' events that M1 drops;
  - a master volume option and `--mute`.

### 4.11 Where Lifestone uses it

- **Online:**
  - login (the player's own CreateObject);
  - server teleports (F751): the tools panel's places, portals, and later recalls;
  - logoff.
- **Offline, in explore mode.** The teleport panel runs the same transition while the destination streams in.
  - The readiness predicate is the end trigger, and no LoginComplete is sent.
  - This shows the effect without a server.
  - Add a toggle to the panel and an option, `--portal-transition=on|off` (default on). The check suites, golden
    viewpoints and tours turn it off.
- **For review and golden frames:**
  - `--portal-space` holds the tunnel (state 3) until quit;
  - `--portal-space-checks` runs the checks of §4.12 in Main's check mode: a window run off-screen, not a headless
    host.
- Every option is registered in `OptionCatalog`.

### 4.12 Tests

- **Data (real dats):**
  - the UIASSET chain resolves to the IDs in §4.2;
  - the tunnel's Setup, GfxObj and animation (including the frame-2 hook) decode as described;
  - the three waves decode with the formats and lengths above.
- **Client (injected clock):**
  - the state machine for login, teleport and logoff;
  - the 2 s minimum hold, the exit window (frames 68 to 76) and the 5 s cap;
  - readiness that arrives during state 2;
  - LoginComplete exactly once, and only at the end of state 6;
  - AnimLevel equal to the retail construction, entry for entry;
  - the zoom maths: f ↔ FOV, the clamp and the near plane.
- **Godot:** the smoke steps `portal-space-checks` and `portal-space`.
  - `portal-space` captures frames at fixed points: mid-expand, in the tunnel, mid-collapse and mid-arrival.
  - The frames go to `docs/screenshots/m2/portal-space/` (gitignored), for review. The capture option is
    `--portal-space-frames[=DIR]` (m2-plan D34), which plays a scripted sequence at a fixed step and seed.
- **Retail:** no capture of the retail client is planned (§7, decision 4). The owner reviews the frames, and the retail
  client is recorded only if a specific doubt needs it.

### 4.13 Retail references

- **Source.** The decompile is of 11.4186: Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src/`. The files
  used are:
  - `GAME/game_ui_misc/gmSmartBoxUI.c` and `UIGlobals.c`;
  - `PORTAL/smartbox/SmartBox.c`;
  - `AC/accui_misc/PlayerSystem.c`.
- **Addresses.** The 11.6096 addresses come from that repository's symbol transplant.
- Cite these names in code comments, and never paste the decompiled code.

| Function | 11.6096 |
|---|---|
| `gmSmartBoxUI::PostInit` | 0x004D77C0 |
| `gmSmartBoxUI::UseTime` | 0x004D7A90 |
| `gmSmartBoxUI::BeginTeleportAnimation` | 0x004D6F60 |
| `gmSmartBoxUI::EndTeleportAnimation` | 0x004D7200 |
| `UIGlobals::Init` | 0x004EF100 |
| `UIGlobals::GetAnimLevel` | 0x004EF1D0 |
| `SmartBox::SetOverrideFovDistance` | 0x00451C00 |
| `SmartBox::GetOverrideFovDistance` | 0x00451C20 |
| `Render::set_vdst` | 0x0054BE50 |
| `SmartBox::teleport_in_progress` | 0x00451C60 |
| `SmartBox::teleport_occured` | 0x00451C80 |
| `SmartBox::DoSetState` | 0x00452110 |
| `SmartBox::HandlePlayerTeleport` | 0x00452190 |
| `SmartBox::PlayerPositionUpdated` | 0x00453910 |
| `SmartBox::TeleportPlayer` | 0x004539B0 |
| `SmartBox::HandleCreateObject` | 0x00454D20 |
| `SmartBox::UseTime` | 0x004554B0 |
| `CreatureMode::Render` | 0x00452A10 |
| `CreatureMode::SetCameraDirection_Degrees` | 0x004537A0 |
| `CreatureMode::CreatureMode` | 0x00454430 |
| `CPhysicsObj::set_sequence_animation` | 0x005101C0 |
| `CPhysicsObj::get_curr_frame_number` | 0x00510250 |
| `CPhysicsObj::update_position` | 0x00515020 |
| `CPhysicsObj::set_hidden` | 0x00515760 |
| `CPlayerSystem::SetTeleportInProgress` | 0x0055E2A0 |
| `CPlayerSystem::RequestLogOff` | 0x00563B70 |
| `CPlayerSystem::SendLoginCompleteNotification` | 0x00563C30 |
| `CPlayerSystem::AttemptSendLoginCompleteNotification` | 0x00563CC0 |
| `LIGHTINFO::SetDirection` | 0x00452740 |
| `DBObj::GetDIDByEnum` | 0x00415640 |
| `DBCache::GetDIDFromEnum` | 0x00413BF0 |
| `ECM_UI::SendNotice_DisplayStringInfo` | 0x00693500 |
| `gmCharacterManagementUI::EnterGame` | 0x004EE0D0 |
| `gmCharacterManagementUI::RecvNotice_BeginEnterWorld` | 0x004E8680 |
| `CM_Character::DispatchUI_EnterGame_ServerReady` | 0x006A1C70 |
| `CM_Physics::DispatchSB_PlayerTeleport` | 0x006AD690 |

## 5. The retail user interface

M2 shows the retail interface, rebuilt from the user's dats, for everything it does: the login flow and the in-game
shell. Retail's UI is data-driven. Layouts, art, fonts, strings and cursors are all in the dats; the behaviour comes
from the decompile's UI classes.

### 5.1 What the dats hold

Verified against the retail dats on 2026-10-11.

- **Layouts.** The 101 LayoutDesc records (0x21xxxxxx) are in the **language dat** (`client_local_English.dat`), not
  the portal dat.
  - They hold 2,162 elements. 98 layouts are laid out for 800×600 and 3 for 800×500.
  - The UILAYOUT enum group (EnumIDMap 0x2500000E, master group 5) names 81 of them, each a different layout, plus
    `Undef` (enum 0), which makes 82 names. The other 20 layouts are reached only as base layouts. For example:

    | Name | LayoutDesc |
    |---|---|
    | `patch` | 0x21000000 |
    | `disconnected` | 0x21000002 |
    | `charactermanagement` | 0x21000004 |
    | `classic_gameplay` | 0x21000005 |
    | `chargen_master` | 0x21000038 |
    | `classic_smartbox` | 0x2100000F |
    | `classic_chat` | 0x21000006 |
    | `classic_radar` | 0x2100000B |
    | `classic_vitals` | 0x21000014 |
    | `classic_toolbar` | 0x21000016 |
    | `classic_panel` | 0x21000017 |
    | `classic_characterinfo` | 0x2100001A |

    Most in-game screens also have a `classic_floaty*` variant, and retail's default gameplay root is built from them
    (§5.3).
  - The other layouts are shared templates. An element can name a base element in another layout (`BaseElement`,
    `BaseLayoutId`) and inherit from it. One template, 0x100002C2 of the shared layout 0x21000040, names a base
    element (0x10000480) that no layout holds. Nothing inherits from it, so retail never creates it either.
  - The dialog layout (UILAYOUT enum 2, 0x2100003C) holds six full-screen elements, one per dialog class.
- **Elements.** Each ElementDesc has:
  - an ID and a type (its UI class);
  - a position, a size and a z level;
  - four edge (anchoring) modes;
  - a default state and a state table;
  - children.

  Each StateDesc carries properties (155 kinds, typed by the master property table, which describes 383 properties and
  whose enum names 384 ids, the extra being id 0) and media.
- **Media.**
  - 1,310 images and 101 cursors.
  - A few pauses, fades, messages, sounds, state changes, alpha changes, animations, jumps and movies.
  - The images are 624 distinct RenderSurfaces, of which 623 resolve. The one that does not is DID 0, retail's
    INVALID_DID: an image entry that clears the element's image (named 43 times in 17 states of 11 layouts), drawn as
    nothing.
  - The media also name 3 alpha masks, 30 animation frames (two animations of 15) and 5 layout cursors, which are not
    among the 41 UICURSOR images. All of them resolve. No UI image of any kind is in the high-res dat.
- **Fonts.** 49 Font records (0x40xxxxxx) are in the portal dat, named through the FONT enum group (0x25000012),
  for example `Chat_Tahoma_Small`.
  - Each is a glyph atlas (foreground and background RenderSurfaces) with per-glyph metrics.
  - Font 0x40000000 has 1,050 glyphs, at most 16×16, with baseline 12.
  - Draw text with these bitmap fonts, not with system fonts.
- **Strings.** The language dat holds 15 StringTables (0x23000001–0x23000010, without 0x2300000F).
  - They are keyed by hashed IDs and take variables (`StringInfo::SetStringIDandTableEnum`, `StringInfo::GetString`).
  - For example, 0x0DCFFF73 is the frame-rate display's "FPS: … DEG: …".
- **Cursors.** UICURSOR (0x2500000F) has 41 entries. The default pointer is UICURSOR enum 1 (set in
  `gmUIFlow::gmUIFlow`).
- **Key bindings.** MasterInputMap 0x14000000, `gmDefaultMap` (KEYMAP enum 0x10000001), holds retail's default
  bindings. Online play uses them; M1's explore-mode keys stay as they are.
- **Character creation.** CharGen 0x0E000002 (13 heritages, 5 starting areas) and the CharGenAssets enum group.
- **Decoding needs the master property table, not a `DatCollection`.**
  - DatReaderWriter reads layout properties through the master property table, which is in the portal dat.
  - `DatSet` opens standalone databases, so every layout read from the language dat alone throws today.
  - A reader bound to the portal database supplies the table: `BaseProperty.UnpackGeneric` accepts it, and
    `DatBinReader`'s constructor is public. A `DatCollection` is not needed; it would also always open the high-res
    dat.
  - Changing `DatSet` is a lead task (m2-plan.md, L2), because it is a shared file.

### 5.2 How retail builds it

- **Modes.** The UI runs as a sequence of framework modes, registered in `gmUIFlow::gmUIFlow`:

  | Mode | Class | What it is |
  |---|---|---|
  | 0x10000003 | `gmDataPatchUI` | the data check; queued first |
  | 0x10000001 | `gmIntroUI` | the intro |
  | 0x10000002 | `gmDisconnectedUI` | the disconnected screen |
  | 0x10000005 | `gmCreditsUI` | the credits |
  | 0x10000008 | `gmGamePlayUI` | the game |
  | 0x10000009 | `gmEpilogueUI` | the epilogue |
  | 0x1000000A | `gmCharacterManagementUI` | character management |
  | 0x1000000B | `gmCharGenMainUI` | character creation |

- **Roots.** A mode creates its root element from a layout enum and an element ID
  (`UIMainFramework::CreateAndAddRootElement`, which resolves the enum through UILAYOUT). For example, character
  management uses layout enum 0x10000005 and root element 0x1000039A.
- **Classes.** An element's type selects its class. 105 classes are registered (`UIElement::RegisterElementClass`):
  - **base widgets** (0x01–0x19): Button, Dragbar, Field (text entry), ListBox, Menu, Meter, Panel, Resizebar,
    Scrollbar, Text, Viewport, Browser, ColorPicker, GroupBox, and the dialogs (Confirmation, ConfirmationMenu,
    ConfirmationTextInput, Menu, Message, TextInput, Wait);
  - **item and option widgets** (0x10000030–0x10000044);
  - **74 game classes** (`gm*`). Examples:
    - `gmSmartBoxUI` (0x10000014);
    - `gmVitalsUI`, `gmRadarUI`, `gmToolbarUI` and `gmPanelUI`;
    - `gmMainChatUI` and `gmFloatyChatUI`;
    - `gmCharacterInfoUI`;
    - the six CharGen pages, `gmCGHeritagePage` to `gmCGSummaryPage` (0x10000039–0x1000003E).

  Regenerate the full table (class ID → name) from the decompile, and pin it in a test rather than in prose.
- **Anchoring.**
  - Positions are in layout pixels, mostly 800×600.
  - When a parent changes size, each child moves and resizes by its four edge modes (values 0–4 in the data):
    `ElementDesc::UpdateSizeAndPosition` and `StateDesc::UpdateSizeAndPosition`.
  - That is how retail filled screens larger than 800×600. Port it exactly.

### 5.3 What M2 shows

- **The login flow:**
  - the data-check screen, during DDD;
  - character management, retail-exact: the list, and enter, delete and restore, with no 3D view (§7, decision 6);
  - character creation, with all six pages and the 3D view of the character, standing still (§7, decision 12);
  - the disconnected screen, for errors.

  There is no login screen. Retail's launcher signed in, and Lifestone does the same with the play account (§2,
  item 2). The intro movies and the credits are not part of M2.
- **In the game,** the shell retail's default gameplay root builds (`classic_gameplay` 0x21000005, root element
  0x10000495; §7, decision 11). It is the end-of-retail floating-window layout, not a set of `classic_*` screens
  (docs/retail-ui-data.md §2):
  - the SmartBox (`classic_smartbox`), which fills the screen, with the portal-space viewport; the 3D world shows in
    it (§7, decisions 7 and 11);
  - the vitals, stacked and side by side, and the radar;
  - the main chat and its four floating chat windows;
  - the toolbar and the panel host;
  - the environment panel, the indicators and the power bar;
  - the combat panel and the examination window, whose content waits for gameplay (below);
  - the keyboard and admin windows;
  - the selection indicator (VividIndicators), and the selected object's name and health;
  - the character information panel, read only;
  - the options panel, with the settings M2 can honour;
  - logging out.
- **Lifestone's own panels** use the same engine and the retail art: the tools panel now, and the automation panels
  later.
  - Their layouts live in Lifestone's own data files.
  - They refer to retail images and templates by ID, so no retail data is copied into the repository.
- **Panels whose content needs gameplay** wait for the gameplay milestone:
  - inventory, backpack and paperdoll;
  - spellbook and components; the spellcasting and combat bars;
  - examination;
  - vendor, trade and salvage;
  - allegiance and fellowship;
  - house and map;
  - books, journal and contracts;
  - barber, friends, squelch and titles.

  Their buttons open a retail message dialog that says so.

### 5.4 Fidelity and scaling

- **Scaling.** Retail draws the UI pixel for pixel, which is tiny on today's screens.
  - Lifestone's default is an integer scale: the largest that leaves at least 800×600 layout pixels, drawn with
    nearest-neighbour filtering.
  - `--ui-scale=1` restores retail exactly. That follows the fidelity policy: a documented default, and a flag for
    retail.
- **Look.** The retail screens use retail fonts, cursors and UI sounds, and no Godot theme.
- **Shared viewports.** The 3D Viewport elements share one CreatureMode implementation: portal space (§4.3), the
  character-creation view, and later the paperdoll.

### 5.5 Tests

- **Data (real dats):**
  - all 101 layouts decode;
  - every template, image, font, string and cursor they refer to resolves, except the dangling template and the empty
    image DID 0 of §5.1, which are reported and explained.
- **Client:**
  - the anchoring port, against cases worked out by hand at 800×600, 1280×720, 1920×1080 and 2560×1440;
  - template inheritance and state changes;
  - text layout with the retail fonts.
- **Godot:** a `retail-ui` smoke step renders each M2 screen offline, with stand-in data where the server would
  supply it. The frames go to `docs/screenshots/m2/ui/` (gitignored), at 800×600 and 1920×1080.

### 5.6 Retail references

These cite the same decompile as §4.13.

| Function | 11.6096 |
|---|---|
| `gmUIFlow::gmUIFlow` | 0x0047AA10 |
| `UIFlow::RegisterFrameworkClass` | 0x00479C50 |
| `UIFlow::QueueUIMode` | 0x004797C0 |
| `UIMainFramework::CreateAndAddRootElement` | 0x006A1A20 |
| `UIElement::RegisterElementClass` | 0x00460270 |
| `gmUIHelper::RegisterElements` | 0x0047A860 |
| `ElementDesc::UpdateSizeAndPosition` | 0x0069B6A0 |
| `StateDesc::UpdateSizeAndPosition` | 0x0069CDB0 |
| `StringInfo::SetStringIDandTableEnum` | 0x0042C9C0 |
| `StringInfo::GetString` | 0x0042E980 |
| `UIElementManager::SetCursor` | 0x0045A910 |
| `gmCharGenMainUI::gmCharGenMainUI` | 0x004E8B40 |
| `gmCharGenMainUI::Create` | 0x004E9070 |
| `gmGamePlayUI::gmGamePlayUI` | 0x004EACA0 |
| `gmGamePlayUI::Create` | 0x004EAE20 |
| `gmVitalsUI::Create` | 0x004C0A60 |
| `gmRadarUI::Create` | 0x004D95D0 |
| `gmToolbarUI::Create` | 0x004BE550 |
| `gmMainChatUI::gmMainChatUI` | 0x004CDD60 |

## 6. Build, run and test

From the repository root, in PowerShell 7:

```powershell
# Per terminal, for a per-user .NET SDK that is not on PATH (README.md, "Development")
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

dotnet build Lifestone.slnx --disable-build-servers
dotnet test Lifestone.slnx --disable-build-servers
pwsh -File tools\smoke.ps1    # 22 steps (login-check and login need the backend); logs in cache\smoke\

# Godot, always off-screen in automated runs
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--engine-checks'

# The backend (server\README.md)
pwsh -NoProfile -ExecutionPolicy Bypass -File server\setup-server.ps1    # idempotent
pwsh -NoProfile -ExecutionPolicy Bypass -File server\start-server.ps1    # returns once ACE is up
pwsh -NoProfile -ExecutionPolicy Bypass -File server\stop-server.ps1
```

- **ACE's logs** are `server\runtime\logs\ACE_Log.txt` and `ACE_Log-WARN.txt`.
- **ACE's `Packets` logger** is at INFO, not off (`server/templates/log4net.template.config`). At DEBUG it hex-dumps
  the LoginRequest with the password in clear text, so it is never raised to DEBUG, its output stays under
  `server/runtime/`, and it never feeds a fixture or a commit.
- **The account.** The test account's name and password are in `server/runtime/secrets.json`. Read them in code;
  never print them.

## 7. Decisions and open questions

### Decided by the owner (2026-10-11)

1. **New characters start in the Training Academy**, as in retail. They leave through its exit portal or with the
   tools panel.
2. **A built-in tool offers common places** from the first login (§2, item 10), so a new character is never stuck.
3. **Real play never uses admin rights, but it gets admin-like tools.**
   - **The account.** The play account is a normal Player account. On an account above Advocate, ACE adds a "+" to
     the character's name and treats it as staff.
   - **The tools:**
     - places (teleport);
     - item search and add;
     - add XP;
     - respec;
     - similar conveniences, each one added on purpose.
   - **How: the Lifestone server mod.** ACE has its own mod loader (`ACE.Server/Mods`). A mod is an `IHarmonyMod`
     class plus a `Meta.json`, loaded from `server/runtime/ace-mods/`. Our mod, built from this repository,
     registers one Player-level command with subcommands, so no account needs elevated access. It:
     - works only when ACE is bound to 127.0.0.1. A mod cannot stop its own loading, so it refuses in `Initialize`
       instead: it throws, which leaves it inactive, and logs why;
     - applies no Harmony patches;
     - calls ACE's own APIs, the same ones behind the admin commands. These include `telepoi`, `teleloc`, `ci`,
       `grantxp` and `myloc`; `Player.ResetSkill` and `RefundXP`; and the skill-alteration and attribute-transfer
       devices. Ported code carries the usual header;
     - turns off ACE's rule that makes the next account an Admin when none exists
       (`DatabaseManager.AutoPromoteNextAccountToAdmin`), backed by a sentinel Admin account (decision 8). A
       development admin account, if one is ever needed, is created on purpose and never used for play;
     - has hot reload off, and is built against the pinned ACE.
   - **Logging.** The tools go through the Action API, so automation can use them too. Every use is logged.
   - **When.**
     - **M2:** the mod, places, where-am-I and add XP.
     - **The gameplay milestone:** item search and add, and respec.
       - Item search uses a local index of the world database's items, built at setup, with icons from the dats.
       - Respec resets attributes and skills and refunds XP and credits through ACE's own code paths, never by
         editing the database.
4. **No recordings of the retail client for now.** Portal space and the rest are built from the specification and
   reviewed by the owner. The retail client is recorded only if a specific doubt needs it.
5. **The retail user interface is part of M2:** the login flow and the in-game shell (§5.3). Panels whose content
   needs gameplay come with the gameplay milestone.

### Decided by the owner (2026-10-12)

With the approval of the implementation plan, [m2-plan.md](m2-plan.md) (its §10 lists every decision):

6. **Character management is retail-exact.** Retail's character screen (`gmCharacterManagementUI`) has no 3D view
   of the selected character, and Lifestone adds none: the list, enter, delete and restore. Character creation keeps
   its 3D view of the character (`gmCG3DView`); decision 12 settles its emotes. DoD 3 and §5.3–§5.4 say so.
7. **The 3D world is confined to the SmartBox frame,** as in retail, where `gmSmartBoxUI::RecvNotice_UpdateGameView`
   sets the device viewport to the frame.
   - The world camera's projection is offset so that it centres on the frame's rectangle (`GameViewRect`), and the
     lens is sized for the frame.
   - The retail UI around the frame is opaque.
   - The portal-space zoom (§4.6) expands and collapses about the frame's centre.
   - Without the in-game shell (explore mode), the frame is the whole window. In retail's default shell the SmartBox
     fills the screen too (decision 11), so for now the world camera is full screen and `GameViewRect` is the whole
     window; the hook stays, and no offset is applied.
8. **A sentinel Admin account prevents first-login promotion.** ACE sets its promote-the-next-account flag when no
   Admin exists, after mods have initialised, so the mod can only clear it later, in a race with the first login.
   - Setup creates a sentinel Admin account with a random password that is discarded at once. An Admin then always
     exists, so ACE never sets the flag. Nobody ever logs in with the sentinel.
   - A mod-ready gate backs it up: `server\start-server.ps1` fails unless the mod reports ready (`ace.modReady` in
     the server's pids file), and `tools\play.ps1`, the client, the headless host and the loopback tests refuse to
     log in without it.
9. **Golden fixtures and session recordings from ACE are regenerated locally,** under `cache/`, and never committed
   (was open question 2). Replay tests build their synthetic recordings in code.
10. **The backend's lifetime belongs to `tools\play.ps1`** (was open question 3, which DoD 1 answers): it starts the
    backend if needed and stops only what it started.

Every other recommendation in m2-plan.md §10 is accepted as written. Each wave runs as a lean multi-agent workflow:
the tracks are implemented, one adversarial review reads them, and one fix pass answers it.

### Decided by the owner (2026-10-12, after wave 1)

11. **The in-game shell is retail's default floating-window layout.** The gameplay root (`classic_gameplay`
    0x21000005, root element 0x10000495) is the end-of-retail shell, not the `classic_*` set §5.3 listed before the
    census (m2-plan D36; docs/retail-ui-data.md §2). Its SmartBox fills the screen, so the world camera is full
    screen and `GameViewRect` is the whole window. The `GameViewRect` hook stays and no offset is applied, so a
    framed shell can be added later (this narrows decision 7 and m2-plan D40).
12. **Character creation's 3D view is retail-exact: the character stands still.** Retail's `gmCG3DView` loads the
    five idle emotes (§4.2) but never plays one, and Lifestone plays none. DoD 3 is amended; decision 6 said "with
    its idle emotes".

### Decided by the owner (2026-10-15, after wave 3)

13. **Character delete and restore are proven live with a throwaway, purged by the mod.** A Lifestone mod verb
    removes a character server side only when it is on the caller's own account and already marked for deletion
    through the retail screens; it refuses the graduated test characters. Installing it is an owner go-ahead
    (`setup-server.ps1 -ModOnly`). The graduated characters stay the baseline (m2-plan D59).
14. **Character creation's 3D view follows retail's animation.** The decompile shows `gmCG3DView::StartAnimation`
    looping an idle animation at 30 fps, called from the summary page and the appearance page when zoomed out, and
    `StopAnimation` holding the rest pose when zoomed in. Decision 12 read that wrong; it is amended (m2-plan D54, D60).
15. **Live Add XP runs on the throwaway character only,** never on the graduated test characters (m2-plan D61).
16. **UI sounds are not part of M2.** DoD 4 is amended; they come with full sound (m2-plan D62).
17. **The materialise fade is built in M2** (retail's fade-in when the server unhides an object or the player;
    m2-plan D45, D63).
18. **DoD 8's Academy walk is the exit wing's,** as built (m2-plan D64).
19. **A new character enters the world at once after creation, as retail** (m2-plan D65).
20. **The visible-window review is cleared.** The owner watched the work in visible windows throughout M2, so the
    separate review through `tools\play.ps1` (m2-plan §6.3 step 5) is not a blocker for M2's end (m2-plan D66).

### Still open

1. **The licence.**
   - AGPL-3.0-only or -or-later, and the copyright line (`src/Lifestone.Client/LegalNotices.cs`, TODO(lead)).
   - It is needed before anything is published.
2. **The automation questions** in automation-vision.md §5: the authoring model, VTank import, multiple characters
   and runtime modes. A0 does not depend on them; A1 does. Question 5 (admin access) is answered by decision 3.
3. **Publishing:** when, and under which account. That is the owner's call.

## Status

**In progress: wave 1 is merged (master f2bcecd); wave 2, the Godot tracks, has items 1 to 5a merged (master 136c9da).** The plan of record, with the
owners, contracts, checkpoints, merge order and gates, is [m2-plan.md](m2-plan.md).

- **On master:**
  - `Lifestone.Net`: the transport, the session and the codecs;
  - A0 in `Client/Session` (reducer, event bus, Action API, recorder and replayer) and the headless host, whose
    `login` scenario is built;
  - portal space's logic and audio (`Client/PortalSpace`, `Client/Audio`, `Data/Audio`);
  - avatars, physics, the retail UI's data and runtime model, and the server mod.
- **Wave 2, items 1 to 4 merged:**
  - portal space offline (`--portal-space`, G1 W1-W5 and W2-pre(a): the tunnel, the zoom, the sound, the presenter and the offline jump);
  - the retail login screens (W2-pre(b) and G2 U2-U4, U9: the data check, character management, the dialogs, the disconnected screen, on the stand-in session);
  - retail character creation (G3 U5: the six pages and the 3D character, on the stand-in session);
  - the player walking offline (G5 W8 and part of W2-pre(c)): the stages below.
- **Wave 2, G5 (world objects and player), stage 1 built:** `--offline-player`: your own stand-in character drawn in
  explore mode (placeholder look, labelled), standing in its ready state on retail's collision, lit by the sun outdoors and by
  the cell's lights (up to 8) indoors; streaming follows the player (the dungeon rule in `Client/Play/PlayerStreamingFocus`);
  walking, the cameras and picking are the next G5 stages. See docs/avatars.md §11.
- **Wave 2, G5, stage 2 built (walking offline):** with `--offline-player` retail's keys walk the character through the movement
  controller (W/X run and back, A/D turn, Z/C strafe, Space jump with the charge bar, Left Shift walk, Q autorun), whose MoveToState,
  AutonomousPosition and Jump go to a dry-run sink that logs them and sends nothing; the camera is retail's chase camera
  (`Client/Camera/ChaseCameraModel`, number pad and middle mouse button) or first person (`--player-camera=first`), swept through the
  collision (`PhysicsWorld.SweepViewer`) so it never goes through walls, with the viewer's cell given to the interiors; the character is drawn
  interpolated between physics steps (D26, `--motion-stepping=retail` restores stepping); the teleport panel and F7 place the player;
  `--physics-overlay` shows the physics. Named TODOs: `g5-run-by-default`, `g5-camera-overhead`, `g5-camera-map-view`, `g5-camera-slope`,
  `g5-mouselook-prefs`, `g5-first-person-turn`, `g5-wheel-zoom`, `g5-translucency`, `g5-picking` (built in G4 slice g0, docs/avatars.md §18); the run-by-default, first-person
  turn and mouse-look ones are logged when they first take effect. Open physics questions the walk checks found (docs/physics.md §15):
  `physics-thin-doorway`, `physics-landing-velocity`.
- **Wave 2, item 5a (G6), stage 1 built:** `tools\play.ps1` (L18: starts the backend when ACE is down, refuses an ACE without its mod, runs the
  game with `--play`, stops only what it started) and the game's `SessionHost` (signs in, ticks the session, quits within 0.5 s of a close request); the retail screens run on the live
  session (data check, character management from F658, character creation answered by the server, Enter to EnterWorld); play mode builds your character from the server's own CreateObject
  with the real movement sink, starts the world around it, plays portal space through the session bridge and moves the world on a server teleport; M1's panels and keys are off (D41) and a
  labelled placeholder status line stands in for the in-game shell. `--play-checks` (smoke step `play-checks`) runs all of it in the engine on a scripted stand-in server. Not run against the
  real ACE in stage 1. See docs/session.md section 21.
- **Wave 2, item 5a (G6), stage 2: the first live login.** Run against the real ACE on the test account:
  - net's live phase A (N11, `tests/Lifestone.Loopback.Tests/Net/`) holds: the mod's `status` reports access Player and auto-promotion off (the first test, which aborts the loopback run otherwise), a
    character is created when the account has none (Aluvian, 55 skills), the world entry lands in the Training Academy's landblock 0x8602, LoginComplete makes the server clear Hidden, the log off
    brings the character list back, Quit ends the session; and the client's raw tap is captured into `cache/net-fixtures/<CacheKey>/` (never committed, a body holding the password is refused)
    and decodes again with today's codecs;
  - `--login-check=enter` (G6 W12; `tools\play.ps1 -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--login-check=enter'`, smoke step `login-check`) runs the whole client path off-screen
    and ends `[logincheck] done ok=true ... teleports=0 academy_objects=34 logoff=ok`; the frame time (`enter_to_walk_max_ms`, 127 to 136 ms, five frames over 50 ms) is reported, not judged, until G5 W13;
  - **a finding that changes the plan's N11 line:** an immediate re-login after Quit cannot avoid error 0x01. ACE keeps a quit or booted session findable for 2 s
    (`SessionTerminationDetails.TerminationEndTicks`) and, while it does, answers a login of the same account with 0x01 (`account_login_boots_in_use`). The login policy's first retry (3 s later) gets in,
    and a login after the 2 s meets no 0x01. The loopback test asserts exactly that (docs/protocol.md section 4.4);
  - a fix the live run asked for: play mode no longer lets the player's body talk to the server while the server has it hidden (`PresenceGatedMovementSink`).
- **Wave 2, item 5a (G6), stage 3 and its gate: the server's world drawn.** The Academy's objects come from the server (AV13 `SessionAvatarAdapter`, the W6
  server-objects layer, W10's `WorldContentCensus`); your character is built from its own CreateObject, created hidden and materialised by the server's UnHide;
  `--login-check=enter` judges the 50 ms rule (live: 32 to 42 ms) and writes three review frames (tunnel, arrival, character list after logoff) under
  `docs/screenshots/m2/login/` (gitignored); smoke step `login`. Automation refuses any account but `accounts.test` (`AutomatedAccountRule`). The live gate found
  one 76 ms arrival frame: the moving objects' lighting variants were in no start-up warm-up; `ServerObjectsWarmUp` now compiles and draws them, and a slow frame
  names its pipeline compiles. Named TODOs: `arrival-pipelines` (a canvas pipeline at the tunnel's start), `g6-teleport-frame-time` (closed by 5b), `g5-translucency`,
  `g5-object-lights`, `g5-w10-scripts`, `g5-w10-sounds`; the player and NPCs looked near-black in the Academy; slice b0 found the cause (the light-set table was
  never uploaded to the GPU, the selection of 1 of 22 cell lights is what retail's rule gives, D46) and fixed it. Full smoke with the backend: 21 of 21 PASS, secrets=0.
- **Decided by the owner after item 4:**
  - the stress crowd (`--avatar-stress`) is dropped; `AvatarStress` stays as test code only;
  - the order of the rest of wave 2 is 5a, real login to the world (G6), 5b, movement that ACE accepts (G5 W11, ST16), then the in-game shell (G4) and the tools panel and automation inspector (G7);
  - an M2 hero video at the end of the milestone, after wave 3: character creation, login, the portal, the Academy and a walk, built on `tools/hero-video.ps1` (docs/hero-video.md) with a dedicated video character and no sound (D58).
- **Wave 2, item 5b (G5 W11, ST16, G6 W11 and W12; slices b0 to b5 in the branch `m2/g6b`, sliced in m2-plan.md §6.5): movement that ACE accepts.** The player's
  weenie reads the character's own qualities (`QualitiesWeenie`; docs/session.md §24), the Holtburg laps and the Academy exit run headless and in the game with the game's
  own player (`HoltburgLapsScenario`, `AcademyExitScenario`, `RoutePlayHost`; §24 to §27), the online flows work in the game (log-off key, boot, silent server, reconnect,
  tools and portal teleports; §26), and `--login-check=academy-exit|holtburg-laps|all` is the gate (§27; live: corrections=0, teleport frames judged, `g6-teleport-frame-time`
  closed). The Academy exit is taken for real (the leg stamps the Sentry's quest through the mod for the Use, and after the way home asks the registry and brings it back to what it found: `exit=teleported quest=put-back`; `TODO(st16-exit-quest)` closed). The exit portal's own emote set (ACE `Portal.cs:287`) erases ten Academy quests, moves the sanctuary to Holtburg and enables recalls, so every exit run graduates the character: both test characters are graduated and the mod cannot undo it (docs/session.md §25). The owner decided that graduated is the test characters' baseline (`TODO(st16-graduation)` closed). Open: `TODO(st16-quest-restore)` (logged only when a play ends with the quest registry possibly changed and not verified back), `TODO(st16-refused-where)`, `TODO(arrival-pipelines)`, `TODO(holtburg-ring-stall)`. The review's fixes are in (the portal's emote set documented and handled, the cleanup after a failure, the live erase-and-relog test). Merged on master (026df3c)
  after the live gate: 15 of 15 loopback tests, then smoke with `-Backend require` 21 of 21 PASS (`login-check` and `login` included), secrets=0.
- **Wave 2, item G4: the in-game shell** (slices g0 to g7 in the branch `m2/g4`, sliced in m2-plan.md §6.6, documented in docs/retail-ui.md §21; picking in docs/avatars.md §18). After Enter the
  screen is retail's end-of-retail floating-window shell (D36), composited over the world and portal space and made ready while the "entering world" dialog shows:
  - the spew box (the server's type-26 notices and portal space's "In Portal Space - Please Wait...") and the main chat and four floating chats (speech and system lines, the typed line sent as Talk, `#lifestone/1` lines filtered);
  - the vitals (stacked or side by side, as the character's option chooses), read from the character with its enchantments applied, and the radar (heading and nearby objects' blips);
  - the toolbar and panel host, where every panel's button names what is not in M2 (the `NotInM2` message), and the link-status indicator;
  - selection by a click in the world (retail's ray, then the part's sphere and polygons; `--retail-picking` keeps retail's two glitches), the selected object's name and health, the vivid target indicator, and a double click that Uses;
  - character information (read only), the options (honoured settings live for the session, the rest ghosted) and the logout confirmation on the options panel's Exit button;
  - windows without content yet are labelled placeholders, and the offline stand-in server is labelled as such over the shell.

  The gate: build with 0 warnings, 6185 default tests, smoke with `-Backend auto` green; live, loopback 15 of 15 and `smoke.ps1 -Backend require` 21 of 21 PASS, with `login-check` ending `done ok=true enter_to_walk_max_ms=28.8 ... shell=ok vitals=50/100/100 spew_portal=yes vitals_window=stacked` and `login` at 38.7 ms, secrets=0 (the judged shell fields: docs/session.md §27). One Opus review (0 blockers, 8 should-fix, all fixed) and three live-gate fixes (Godot's light-culler ERROR from the portal zoom's frustum, the vitals' maximums with the graduated character's 138 enchantments, the shell's construction staged over frames) came after the slices. Open named TODOs, none blocking: `g4-window-layout-save` (retail's two stores for window positions), `g4-link-units`, `g4-link-offline`, `g4-erase-exe`, `g4-vitals-layout`, `g4-enchantment-updates`, `g4-enchantment-affects-skills`, `g4-quality-filter`, `g5b-heading`, `g6-options-save`, `g6-option-effects`, `g6-options-buttons`, `g6-character-info-augmentations`, `g6-duration-format`, `g6-busy-cursor-image`, and the chat's, radar's and toolbar's smaller ones (docs/retail-ui.md §21).
- **Wave 2, item G7: the Lifestone panels** (a tools lane and an inspector lane in the branch `m2/g7`, sliced in m2-plan.md §6.7, documented in docs/retail-ui.md §22). Two floating windows over the shell, built from layout files in the R9 format (`tools.json`, `inspector.json`) on retail's templates, and opened by keys retail leaves unbound (D41): F7 the tools panel, Scroll Lock the inspector.
  - **The tools panel (DoD 10):** Places searches the teleport catalogue (ACE's points of interest and the curated places) and a pick teleports through portal space; Where am I compares the server's position with the walker's physics body and says whether they agree within 0.5 m; Add XP is confirmed in the chat first. Off loopback the panel refuses, and a server without the mod gets the mod-missing dialog. Every use goes through the Action API and is written to the chat and the action log (DoD 13: the UI never talks to the session directly).
  - **The automation inspector (DoD 13):** the action log's rows (a ring of 1,024, newest first, built over frames), the lifetime counters, and the kill switch and dry run shown read-only.
  - **The gate:** build with 0 warnings, 6235 default tests, smoke with `-Backend auto` green; live, loopback 15 of 15 and `smoke.ps1 -Backend require` with every step passed except one `login-check` run whose frame at ACE's world-entry message burst took 53.0 ms (`stage=entering`, before the tunnel, no collection or pipeline compile); two reruns of `login-check` and `login` measured 43.3 / 32.6 ms and 37.5 / 31.8 ms and passed, and the lead accepted the gate on them (docs/session.md §27), secrets=0. One Opus review (0 blockers, 5 should-fix, 9 nits; all fixed but the stand-in's unlabelled replies) and two live fixes (the key bindings built at start-up and the embedded layouts registered on a worker; the real shell built in stages over frames) came after the lanes.
  - **Open named TODOs, none blocking:** `login-burst-frame` (profile the frame that handles ACE's world-entry burst: the PlayerDescription with 138 enchantments, the first CreateObjects and their log lines; it sits near the 50 ms limit live), `g7-inspector-layout`, `g7-inspector-unwired` (a host that wires no inspector key) and `g7-standin-where` (the offline stand-in refuses Where am I until the player has a position).
- **Decided by the owner during G4 (2026-10-14):**
  - D55 and D56 (m2-plan.md §10): G4's read-only exe reads, and window positions kept for the session only;
  - D57: tests, checks and frames stay at 800x600, retail's native size; 1920x1080 and 2560x1440 are opt-in (`smoke.ps1 -AllSizes`, `--all-sizes`) and a polish step at the end of the project;
  - D58: the M2 hero video has no sound; full sound effects move to a later milestone (likely M3).
- **UI layouts with screen logic built: 14 of 81, plus Lifestone's own 2.** Method: of the 81 UILAYOUT layouts (docs/retail-ui-data.md §2) it counts the five mode roots (`patch`, `disconnected`, `charactermanagement`, `chargen_master`, `classic_gameplay`), the dialog layout, and the eight layouts the shell's windows inherit from directly and a Lifestone controller drives (`classic_smartbox`, `classic_floatyvitals`, `classic_floatysidevitals`, `classic_floatyradar`, `classic_floatymainchat`, `classic_floatytoolbar`, `classic_floatypanel`, `classic_floatyindicators`); a layout reached only through template inheritance (the spew box, the toolbar's and the panel pages' contents) or only by an id that other code shares is not counted, so the number is a floor. G7 drives no retail layout: its two windows, the tools panel (0x7F000010) and the inspector (0x7F000002), are Lifestone's own layouts, counted apart, so the retail count stays 14.
- **Wave 3, the fix lanes (merged on the branch `m2/w3`, from the review reports of the DoD items; the end-of-wave gate and the audit table follow):**
  - **Session (lane A):** the headless host is always an automated run and signs in only to the test account (`AutomatedAccountRule` is applied in `Program.Run` and in `HeadlessRunner.StartWith`); `LoopbackPolicy`'s messages say "loopback"; the replayer expires parked messages after each frame (`TODO(replay-tick-boundary)`); `tools\play.ps1` passes the game the backend's port; the live refusal test uses a cell the cell dat lacks (docs/session.md).
  - **Gate (lane B):** the login check judges the portal rules (tunnel at least 5 s, the logoff tunnel held, the whooshes and the hum counted), a census of every placed object at the Academy settle, the jump leg and the movement audits written to `cache/movement/`; four review frames; the burst frame names its parts (`TODO(login-burst-frame)`); the run-by-default option reaches the rig (docs/session.md section 27).
  - **World (lane C):** the walk checks' open questions log themselves (`TODO(physics-landing-velocity)`, `TODO(physics-thin-doorway)`), the AutonomousPosition is not sent into a closed gate, the logoff hold warns every 10 s (`TODO(portal-logoff-hold)`), avatar limits and counters, the movement notices carry a placeholder line (`TODO(movement-notice-chat)`).
  - **UI (lane D1):** the create flow logs the new character in at once (stand-in and action log only; a live create is set aside, `TODO(retailui-chargen-enter-dialog)`); Exit Game asks the logout's confirmation and queues the epilogue (`TODO(g6-exit-game-confirm)`); the mid-air logoff refusal is not built (`TODO(g6-logoff-midair)`); `TODO(ui-sound-sink)`; the tools panel's refusals reach the chat; 11.4186 addresses in the G4 shell headers (docs/retail-ui.md section 21).
  - **Docs (lane D2):** `docs/third-party/retail-ui.md` and the camera and picking rows of `docs/third-party/avatars.md`; the stale texts of docs/session.md, docs/portal-space.md and docs/physics.md.
- **Not done:** nothing of wave 2 or wave 3 is left, and the owner decisions below are decided (§7, decisions 13-20). The owner's visible-window review was cleared (decision 20); the hero video and the private push follow.
- **The end-of-wave-1 documentation pass** applied the corrections of m2-plan.md §1.6 and docs/portal-space.md §15 to
  §4 and §5, and records decisions 11 and 12 (§7).

- **Wave 3, the reviews, the fixes and the gate (branch `m2/w3`, cut from master 53abd63; run as recorded in m2-plan.md §6.8):**
  - **Reviews:** six read-only reviews, one per DoD cluster, found 64 findings and 0 blockers. Four fix lanes (A session, B gate instruments, C world, D UI and docs; the lane list above has each one's content) closed them in 116 changed files (+3,718, -246).
  - **The gate** (docs/session.md §27; local evidence under `cache/`, gitignored): build with 0 warnings; 6,311 default tests passed, 0 failed, 8 skipped (each needs a recording, an exe or a larger window); `smoke.ps1 -Backend require`: every step passed (secrets=0) except `play-checks`, whose one failure was its internal 12 ms UI-step sub-budget at 12.7 ms (the DoD rule is the 50 ms frame, and no frame was over it); the lead's rerun of `play-checks` passed 110 of 110 with a longest frame of 21.3 ms. Live loopback: 22 of 22, including the SM9 verb tests, the standing and running jump after the laps, and the 45 s idle on the character list. The soak tier passed 5 of 5: the datagram and message-body fuzz, the 10-minute virtual soak over 100 seeds, and the live 2-minute walk at 5% loss and 2% reordering. The final live loopback run on the merged tree (after the gate fixes, which touched Lifestone.Net) passed 22 of 22.
  - **The verdict lines.** `login-check` (it walks the Academy exit and three Holtburg laps, then the jump leg) ended `done ok=true enter_to_walk_max_ms=37.0 holtburg_min_fps=240.0 corrections=0 where_max_m=0.03 exit=teleported quest=put-back legs=academy-exit,holtburg-laps leg_teleports=5 teleport_frame_max_ms=28.1 academy_objects=23 objects_drawn=37 academy_undrawn=0 holtburg_npcs=17/32 holtburg_max_frame_ms=4.2 tunnel_seconds=4.5 portal_seconds=5.5 tunnel_covered_frames=0 logoff_tunnel=held portal_sounds=ok sound_enter=1 sound_exit=1 sound_hum=2 shell=ok vitals=50/100/100 spew_portal=yes vitals_window=stacked`; its jump leg sent 2 jumps, both landed, 0.03 m from the server, no force-position increment. The shorter `login` ended `enter_to_walk_max_ms=30.0 materialised=true objects_drawn=8 academy_undrawn=0 tunnel_seconds=5.2 portal_seconds=6.2 tunnel_covered_frames=1 logoff_tunnel=held portal_sounds=ok shell=ok`. The Academy and Holtburg census lines are in `cache/smoke/17-login-check.log`.
  - **Found live and fixed at the gate:** the SM9 refusal test used a cell the mod accepted (it now uses one the cell dat lacks, and the restore finishes a half-done arrival first); the 51 ms frame of the login burst was the first PlayerDescription's decode and reduce compiling cold on the game thread (a start-up warm-up on a worker now compiles that path, and a message over 5 ms is named in the log); the tunnel check was wrong (the tunnel is visible 4 s of the 5 s sequence, so the checks judge `tunnel_seconds` at least 4 and `portal_seconds` at least 5) and the black backdrop that holds the world while the shell is built covered the tunnel's first frames (it now yields to the tunnel; `tunnel_covered_frames` is judged).
  - **Owner decisions pending (each is named in the table):**
    1. a live create, delete and restore through the retail character screens (it changes the test account's characters);
    2. CharGen's idle: retail's breathing loop against D54's "stands still" (a decompile check first);
    3. a live Add XP on a real test character (it levels the character for good);
    4. whether UI sounds belong to DoD 4, since D58 moved full sound effects to M3;
    5. the materialise fade (D45): accept the pop-in, or schedule the blended twin;
    6. DoD 8's wording: the Academy walk starts at the wing, 4 doorways from the exit portal, because the portals are quest-gated;
    7. the new character enters at once after a create: built as retail does, to confirm (`TODO(retailui-chargen-enter-dialog)`);
    8. the 20 s silence watchdog on the character list (D18) needed a decision only if the idle test had failed. It passed (45 s), so nothing is asked.

**DoD audit table (wave 3, 2026-10-15; rows 3, 6 and 10 updated by the decisions follow-up, m2-plan §6.9).** A verdict is "met" only when nothing in the item is open. A deferred piece or an owner decision makes it "met with named gaps"; an unproven core path makes it "partly met". "Stand-in" is the offline stand-in server of `--play-checks`. "Frames" are the gitignored review frames under `docs/screenshots/m2/login/` (`01-portal-tunnel`, `02-arrival-among-academy-objects`, `03-shell-after-the-settle`, `04-character-list-after-logoff`) and `cache/smoke/shell-frames/800x600/` (01 to 10) and `cache/smoke/retail-ui/`.

| # | Item | Verdict | Evidence | Open gaps (what they wait for) |
|---|---|---|---|---|
| 1 | One command to play | **met** | `tools\play.ps1` starts the backend only when ACE is down, refuses an ACE without its mod, stops only what it started, and now passes the game the backend's port: `PlayScriptTests`; the loopback refusal: `LoopbackGateTests`, `S8LoopbackRunnerTests`, `UdpLoopbackSocket`. Smoke `login` (through `play.ps1 -Offscreen`) `done ok=true ... logoff=ok`, secrets=0. | None. |
| 2 | Log in | **met** | Handshake with the ConnectResponse race, ISAAC, acks, NAKs, TimeSync, run-time DDD iterations: `Net.Tests` (`FakeAce`, `WatchdogTests`, `AcSessionCreateTests`), `PasswordHygieneTests`, `LivePhaseATests` (create, enter, unhide, log off, quit, re-login); the data-check screen is in DoD 4's frames; a mismatch or boot shows the disconnected screen (`--play-checks`). Live: 30 s (the 20 s watchdog plus 10 s; the test is `Idling_in_the_character_list_past_the_silence_watchdog_does_not_trip_it`, M3 4.2 p5b) idle on the character list does not trip the 20 s silence watchdog (`Idling_in_the_character_list_for_45_seconds_...`), so D18 stands. The headless host now signs in only to the test account (`AutomatedAccountRule`). | None. |
| 3 | Characters | **met with named gaps** | Management (list from F658, enter, delete, restore, no 3D view): `CharacterManagementScreenTests`, `CharacterOpsTests`, `FakeAce`. Creation, six pages, exactly 55 skills (`CharacterCreateIntent.RequiredSkillCount`), ACE's errors in retail dialogs: `CharGenScreenTests`, `CharGenSummaryTests`, `CharGenViewTests`; smoke `retail-ui` 25 frames, `missing_images=0`, `uicheck pass=72 fail=0`. CharGen's 3D view loops retail's animation (D60: `gmCG3DView::StartAnimation` from the summary page, `ZoomOut` and `gmCGAppearancePage::Update` while zoomed out; the rest pose when zoomed in): `CharGenViewTests`, `CharGenAppearanceTests`, `--retail-ui-checks` judges the pose. **Live (decisions follow-up, 2026-10-15):** `login-throwaway` (`--login-check=throwaway`, docs/session.md §28) creates a throwaway on the test account through the retail screens after ACE's two refusals in retail dialogs (name taken, a creature's name), enters at once, logs off, deletes it with the typed word, restores it, deletes it again and purges it with the mod's `char purge` verb from a graduated character, leaving the list as it was: `throwaway=purged name_errors=2 deletes=2 restores=1 list=same leftover=no enter_to_walk_max_ms=32.8`; `ModCharPurgeLiveTests` (the verb's refusals and the purge) passed live. | The Entering World dialog on the create-and-enter path is not retail's yet (`TODO(retailui-chargen-enter-dialog)`). |
| 4 | The retail user interface | **met with named gaps** | Layouts, art, fonts, strings and cursors from the dats: `Data.Tests/Ui/*` (101 layouts decode; every reference resolves but two documented exceptions), `UiRuntimeCensusTests`, `RealKeyMapTests`; engine `--retail-ui-checks` pass=69 fail=0; the in-game shell: `GamePlayScreenTests`, `GameShellG4Tests`, `GameChatTests`, `GameVitalsTests`, `--play-checks` 110 of 110 (the rerun); `login-check` `shell=ok vitals=50/100/100 spew_portal=yes`; shell frames 01 to 10. A gameplay panel's button says "Not in Lifestone M2" in a retail dialog. Exit Game now asks the logout's confirmation. | UI sounds are out of M2 (D62; wired, `TODO(ui-sound-sink)`). **M3:** 14 of 81 layouts have screen logic (a floor); the window layout save, the options save, chat channels and the busy pointer image (the `g4-*` and `g6-*` TODOs). |
| 5 | Portal space | **met with named gaps** | Machine, zoom, exit window, bridge, director, asset conformance on the real dats, sound rules: `PortalSpaceMachineTests`, `PortalSessionBridgeTests`, `PortalZoomTests`, `PortalSpaceDirectorTests`, `PortalSpaceAssetConformanceTests`, `RetailSoundRulesTests`. Smoke `portal-space-checks` pass=54 fail=0; `portal-space` captured=4, max frame 30.3 ms. Live, now judged: `portal_seconds` 5.5 and `tunnel_seconds` 4.5 (`login-check`), 6.2 and 5.2 (`login`), `tunnel_covered_frames` 0 and 1, `logoff_tunnel=held`, `portal_sounds=ok` (enter 1, exit 1, hum 2), 5 teleports with `teleport_frame_max_ms=28.1`; frames `01-portal-tunnel`, `04-character-list-after-logoff`; offline in explore mode: `ExploreTransitionTests` and the Holtburg to Arwic frames. The tunnel colour was accepted by the owner against retail footage (2026-10-13). | The sounds are judged by counters; no gate hears them (`TODO(record-audio-check)`, M3); the owner's visible review was cleared (decision 20). **M3:** LoginComplete does not wait for the player description and contents (`TODO(portal)`; ACE does not need it). Deviation D28 (the logoff tunnel ends at the character list) is documented. |
| 6 | Server objects | **met with named gaps** | All eight message kinds reach the avatars: `SessionAvatarAdapterTests`, `AvatarSetTests`, `AvatarBodyTests`, `AvatarHiddenTests`, `AvatarChildrenTests`, `AvatarMotionCompletionTests`, the allocation tests. Live census at the Academy settle compares every placed kind with the drawn guids: `academy_undrawn=0`, `academy_objects=23`, `objects_drawn=38` in Holtburg. **The materialise fade (D63, decisions follow-up):** retail's UnHide script 0x3300032F fades the object from translucency 1 to 0 over 0.75 s (pinned from the dats in `AvatarHiddenTests`, the ramp in `AvatarMaterialiseTests`); opaque surfaces are drawn through a blended twin while they fade, warmed at start-up (docs/avatars.md §17); live `materialised=true fade_ms=753` (and 752), every fading frame drawn through the twin, no frame over 50 ms; `--play-checks` watches the player and an unhidden object through the fade; review frame `02a-materialising`. The glitch fix for a re-hide in mid-fade is the default; `--retail-unhide-flash` restores retail. Frames `02-arrival-among-academy-objects`, `03-shell-after-the-settle`. | Holtburg drawn of placed: npcs 12/32, portals 2/3, lifestones 2/3, doors 7/15, signs 0/1 (the rest are excused by reason: no part to draw, or a cell that is not shown); Holtburg's signs and the other excused objects are not shown drawn by any check or frame. Server-object twins are built eagerly (`TODO(serverobjects-lazy-twins)`); the first-person fade belongs to DoD 7 (`TODO(g5-first-person-fade)`, M3). |
| 7 | Your character | **met with named gaps** | The rig is built from the player's own CreateObject (`AvatarWire.FromWorldObject`, `PlayerView`); ready, walk, run, turn, sidestep, jump, fall and land play through the human motion table (`PlayerAvatarTests`, `PlayerWalkingTests`); live in the laps and the Academy exit (`corrections=0`, 2 jumps landed). Chase camera orbit and zoom, first person: `ChaseCameraModelTests`. Frame `02-arrival-among-academy-objects` shows the body and clothing. | **M3:** wheel zoom, overhead and map views, slope, first-person turn and mouse-look preferences (the `g5-*` TODOs); the first-person fade (`g5-translucency`, with DoD 6); a wielded item's children and particles are not interpolated with the player. No review frame of the walk or run animations. |
| 8 | Movement ACE accepts | **met with named gaps** | MoveToState on every change, AutonomousPosition every 1.0 s when the position changed, Jump: `PlayerMovementControllerTests`, `PositionEventCadenceTests`, `JumpChargeTests`, `MovementChannelTests`, `WalkChecksTests`; the ACE port with headers (the `Physics` tests, 172 of 172). Live: three Holtburg laps `corrections=0 where_max_m=0.03` and the Academy walk (`HoltburgLapsLoopbackTests`, `AcademyExitLoopbackTests`, the walking test within 0.5 m); the standing and running jump after the laps ran live for the first time (`A_standing_and_a_running_jump_...`: accepted, no corrections). | **Owner:** the DoD says the Academy walk starts at the spawn cell; it starts at the wing, 4 doorways from the exit portal (quest-gated portals): amend the wording or accept. `TODO(holtburg-ring-stall)` (a rare stall of the ring, about 1 in 10 in earlier runs) is not root-caused; `TODO(physics-landing-velocity)` and `TODO(physics-thin-doorway)` are open questions (docs/physics.md §15) that now log themselves. |
| 9 | Teleports | **met with named gaps** | A portal by GameAction Use, including the Academy exit: `UseTracker`, `AcademyExitScenarioTests`, `AcademyExitLoopbackTests`, `login-check` `exit=teleported quest=put-back`, with F751, portal space, UpdatePosition, streaming, exit and one LoginComplete per visit counted; `teleport_frame_max_ms=28.1`. A tools-panel pick: the panel's Places (G7) over `Tools.Place`; live through `PlayerTools` (`place poi` Holtburg and `place loc` back: `LivePhaseBTests`), the body against `where` within 0.03 m, and a curated interior cell (`Place_loc_to_a_curated_interior_cell_...`). | The panel itself (the picker) has run only on the stand-in (`--play-checks` tools teleport, `ToolsPanelTests`); a live pick through the Godot panel is proven by `login-throwaway` (`place=teleported`, DoD 10). After a portal Use the client's end position is compared only through the server's `where` (`TODO(st16-refused-where)` for the refused leg). |
| 10 | Player tools | **met** | Places over 56 destinations under 62 names plus the curated places (`AcePointsOfInterestTests`, `ToolsModelTests`, `ToolsPanelTests`; live `Places_lists_56_destinations_under_62_names_...`); Where am I within 0.5 m (`WhereVerb`, `PositionAgreement`); the mod's refusals come back as failed replies and change nothing; every use is in the chat and the action log and the panel refuses off loopback (`ToolsModelTests`; the mod refuses too). **Live through the Godot panel (decisions follow-up, 2026-10-15, on the throwaway only, D61):** `login-throwaway` opens the tools panel by its hotkey, Where am I `where_m=0.00`, Add XP 2,500 confirmed by the server in the chat (`xp=added`), a Places teleport through portal space (`place=teleported`, `teleport_frame_max_ms=7.6`). Frames shell 10 (the panel) and 09 (the inspector). | None for the Godot client. A host with no physics body (the headless host) answers Where am I from the server alone (`g7-where-client-body`); the stand-in's own replies are not labelled on screen (`g7-standin-where`). |
| 11 | Log off | **met with named gaps** | `LogOffTracker` (the one F653 sender), the 3 s wait, portal space's logoff transition and the character list: `LogOffScheduleTests`, `--play-checks`, live `logoff=ok logoff_by=key`, `logoff_tunnel=held`, frame `04-character-list-after-logoff`. Quit sends F653, then the cleartext Disconnect, so ACE drops the session: `LogoffQuitTests`, `LivePhaseATests` (an immediate and a delayed re-login). Exit Game confirms, then plays the epilogue. | **M3:** the mid-air refusal ("Cannot log off while in mid-air.") is not built (`TODO(g6-logoff-midair)`); the player-killer delay is a logged TODO (D33). Closing the window while in the world has no live end-to-end proof (the owner's visible review was cleared, decision 20; M3). |
| 12 | Robust transport | **met with named gaps** | The 2-minute walk at 5% loss and 2% reordering over a virtual link in the default run (`Two_minutes_at_five_percent_loss_and_two_percent_reordering`: bytes equal at the fake ACE, no client checksum failure, `C2SCrcErrors == DuplicateEncryptedDeliveries`); the soak tier, 5 of 5 (the datagram and message fuzz, the 10-minute virtual soak over 100 seeds, and the live 2-minute walk `Two_minute_walk_over_a_five_percent_loss_two_percent_reorder_link_leaves_ACEs_CRC_count_equal_to_the_duplicate_deliveries`); the live duplicates test (`Duplicated_encrypted_client_packets_are_counted_by_ACE_...`). A silent server, a boot or a timeout show the disconnected screen (`--play-checks`). | The tests assert the CRC-equals-duplicates oracle and pass, but the soak log does not print the two counts, so this audit has no number for them; print them in the next soak. A really stopped ACE is shown only on the stand-in. |
| 13 | Automation A0 | **met with named gaps** | Reducer (`ReducerLoginTests`, `ReducerWorldTests`), event bus with the raw tap (`EventBusTests`), Action API with handles (`ActionApiCoreTests`, `ActionHandleTests`), recorder and replayer with a digest (`ReplayTests`, `RecordingFormatTests`), the headless host (`HeadlessLoopbackTests`, `HoltburgLapsLoopbackTests`, `AcademyExitLoopbackTests`), the gate with its S8 test; the Godot client reaches the session only through them (`ArchitectureTests`), the tools panel and the inspector included. | The replay tests run over synthetic recordings on a clean checkout; the real-recording tests skip without `cache/sessions` and this audit has no run of them. `TODO(replay-tick-boundary)` records one live-versus-replay difference in when parked messages expire. |
| 14 | Tests | **met** | 6,311 default tests passed, 0 failed, 8 skipped (each needs a recording, an exe or a larger window). `Net.Tests` has vectors, codecs, a fake ACE and fuzzing; the retail UI tests of §5.5; loopback tests carry the trait and skip without a backend; the smoke steps `portal-space-checks`, `portal-space`, `retail-ui` and `login` exist and judge their fields. SM9 has live verb tests (`ModVerbsLiveTests`: status, places, refusals, busy, a curated interior). | None. |
| 15 | Frame time | **met with named gaps** | `enter_to_walk_max_ms=37.0` (`login-check`) and 30.0 (`login`), no frame over 50 ms; the earlier 51 to 53 ms frame was the first PlayerDescription compiling cold, now warmed up at start-up; `--play-checks` judges the 50 ms rule; `FrameBudgetShare` holds the shared 4 ms budget (`BudgetShareTests`); Holtburg `holtburg_min_fps=240.0` (the display's rate; 17 NPCs in that sample) with `holtburg_max_frame_ms=4.2`. | The frame at ACE's world-entry burst still sits at 37 ms (`TODO(login-burst-frame)`, its parts are logged), and the `play-checks` 12 ms UI-step sub-budget failed once at 12.7 ms (a rerun passed; its worst frame was 21.3 ms). The 60 fps is a one-second average at 800x600 on a high-rate display, not a worst frame. |
