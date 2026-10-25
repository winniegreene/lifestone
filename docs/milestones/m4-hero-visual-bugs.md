# M4 hero video: three visual bugs seen in the takes

**Status (2026-10-25):** diagnosed from the existing takes and offline tests; one mechanism reproduced and a detector
added; the per-frame flicker itself not yet pinned (it needs a recorded take, §1.6); the camera is not at fault (§2); the
door is the known `TODO(d25-server-object-host)` and its fix is planned (§3, §4). Branch `m4/hero-visual-bugs`, cut from
`m4/hero`.

While the hero takes were filmed (`--journey=hero-archer|hero-brawler|hero-mage --journey-film`, an on-screen
1920x1080 window), the owner saw a creature flickering between two places, the camera snapping in while the Archer
looted, and the Brawler walking through a closed door. The evidence is the 60 fps screen captures and game logs of the
takes (gitignored: `cache/m4-hero/takes/<take>.mkv`, `.log`, `.json` in the hero session's checkout; a take's
`video_start_unix_ms` maps a log line's UTC time to the video time). Video times below are archer2's unless named.

## 1. A creature flickers between two places

### 1.1 What the frames show

- **The lugian (a Crude Monouga, weenie 2575, Setup 0x020002FF, motion table 0x09000027, scale 1.62).** At 93.55 s,
  beside the Archer as she loots the corpse 0x80000429, it is drawn standing in front of the corpse on some frames
  and, on the others, 1-2 m further left behind the Tusker in the foreground, where only its forearm (the dark bracer)
  shows over the Tusker's back. Per frame from 93.55 s: P P P A A P A P P A P A P P P P A P (P forward, A back). It
  happens in bursts (93.63-93.85 s, 96.08-96.53 s); between them the lugian stays back. Only the lugian changes: the
  Archer, her bow and the foreground Tusker are the same in both states.
- **The live Tusker (Tusker Crimsonback, weenie 1627, Setup 0x02000964, motion table 0x0900000C, scale 1.1).** At
  90.55-91.05 s it is drawn alternately at the Archer, facing away from the camera, and 1.5-2 m nearer the camera
  facing the lugian (frames n=15..28 after 90.55 s: A B B B B A A A A A B B B A), then settles right in front of the
  camera (§2).
- **Not stale frames.** The corpse panel is current in every frame across the second pick-up (the Sandals leave it at
  93.667 s and never come back), and no "absent" frame matches an earlier frame: the game drew both states.

### 1.2 Ruled out (offline, with the real dats)

| Candidate | Test or probe | Result |
|---|---|---|
| The creatures' own animations | `CreatureAttackPoseTests` (kept): every attack and hit reaction of both motion tables in every combat style, at ACE's speed 1.5 | no pose jump or flip; the largest one-frame pose move is 0.34 m (SlashHigh) |
| Reversed motions (TurnLeft, WalkBackwards, SideStepLeft, as negative-speed cycles) | probe | no pose jump |
| LOD (the avatar frame selects levels from the player's position, not the camera's) | probe of the lugian's 25 parts beside the player through a swing | the drawn part count never changes |
| An EnvCell skip (an object in a cell not shown is not drawn) | landblock 0x9FB5 | it has no EnvCells |
| Two avatars for one guid (a recreate) | `AvatarSet.Create` | a recreate removes the old avatar first |
| A whole-frame repeat in the capture or presentation | the corpse panel, frame matching | ruled out (§1.1) |
| The contact flag (D24: a server object's contact is the last UpdatePosition's Grounded flag) | probe: alternating Grounded flags | each flip restarts the swing pose (about 0.25 m), not a 1-2 m jump; and ACE sends a monster's positions at most every 0.2 s (its monster tick), too slow for a 1-3 frame flicker |
| The draw batches (shared meshes, upload skips, culling bounds) | `ServerObjectsLayer`, `DynamicObjectBatch` read | every slot is refilled and its bounds recomputed every frame |

### 1.3 Reproduced: a stuck creature snaps back to the server's position when its stick ends

ACE's monster melee (Monster_Melee.cs `MeleeAttack`/`DoSwingMotion`, Monster_Tick.cs) sticks the monster to its
target on the server and sends each swing as an interpreted motion with StickToObject; it sends the monster's position
on the first swing and while it moves (`Movement`, every 0.2 s), not while it stands stuck. The client then has two
sources for the same creature:

1. **The client's sticky follow** (`StickyManager`, retail `StickyManager::adjust_offset`): each swing presses the
   creature against the **client's** player cylinder, 0.3 m short of touching, at 5 x its maximum speed (about
   0.7-1.3 m a frame for these creatures), turning it to face the player.
2. **ACE's position** (`InterpolationManager`): where ACE's own sticky and collisions put it on the server.

The two disagree because Lifestone's client gives server objects no collision (D24, D25: no host keeps a physics
object for them): two monsters stuck to the same player overlap on the client, while ACE pushes them apart. When a
stick ends (its one-second timeout, or the swing's `MotionDone`), the interpolation manager's progress check (every 5
frames, skipped while stuck) measures no progress over the stuck frames and, on two phases of five, fails the node and
blips the creature to ACE's stale position: a 1.5-3 m jump in one frame. The next swing lunges it back. This is retail's
code fed by positions that disagree; `AvatarJumpWatchTests` pins it (`A_creature_stuck_to_the_player_..._is_logged`).

It explains single jumps (one per ACE position) and the Tusker settling in front of the camera, not the 1-3 frame
alternation of §1.1, which needs a per-frame toggle that none of the offline probes found (a probe with the physics
player body, `OfflineWalker` stepped by its 30 Hz gate, the real Setups and ACE-like traffic shows no alternation).

### 1.4 The detector (`AvatarJumpWatch`)

Every frame the avatar set compares each server object's drawn position and pose (the parts' centroid in its own frame)
with the last two frames and reports, for objects within 40 m of the player, as a WARN in category `avatars` (`TODO(hero-creature-flicker)`, the first 40, then
counted in `AvatarStats.CreatureJumps` and `CreatureFlips`):

- `RootJump`: more than 1 m in one frame without a teleport (or a placement 96 m or more from the player);
  `RootFlip`: there and back on consecutive frames;
- `PoseJump`: the pose centroid more than 0.6 m in one frame; `PoseFlip`: a pose there and back.

Each line names what moved the object: `placed` (Teleport, Far, Blip or None), `sticky` (the target and whether it has
reported), `interp_queued`, `interp_fails`, `moving_to`, `contact`, `forward`, `pending_motions`, `quantum_ms`,
`from_server_m` and `from_player_m`. It changes nothing that is drawn and allocates nothing until it reports.

### 1.5 What fixes it

The faithful fix for §1.3 is the client colliding server objects with each other and with the player, as retail does
(D24's deferred transitions for server objects), on top of the server-object physics host of §4: the client's sticky
spots then agree with ACE's. Changing retail's interpolation or sticky rules instead would hide the symptom and is not
proposed.

### 1.6 Next step to pin the per-frame flicker

Film one Archer hunt again with `--record-session` (owner decision D48: recordings only on request) on a build with
the detector. The detector's lines name the source in the log at once; the recording (`cache/sessions/*.lsrec`) can be
replayed offline through the avatar set (`SessionReplayer`, as `SessionAvatarAdapterReplayTests` does) to step the
lugian frame by frame.

## 2. The camera "snaps in" at each pick-up

The camera does not move. From 88.2 s to 89.4 s it catches up smoothly as the Archer stops at the corpse and turns
(retail's stiffness 0.35); from 89.8 s to 98 s the trees, the horizon, the rock at the top and the corpse keep their
screen places and sizes across all three pick-ups (90.26, 93.65 and 97.05 s). What reads as the camera snapping in is
the live Tusker jumping 1.5-2 m into the space between the camera and the Archer at 90.95-91.05 s (§1.1) and staying
there, its back filling the frame, with the lugian beside it. The chase camera (`ChaseCameraModel`, the journey's
`MissileSide` framing: scale 1.2, heading offset -40 degrees) behaves as retail's; it could not collide with the
creatures in any case (the viewer sweep meets static geometry only: no server object has a physics object, D25, and
retail's viewer passes through creatures). No camera change is made; the fix is §1's.

## 3. The Brawler walked through Renald the Elder's door

`brawler2.mkv` 86.0-87.25 s: the camera is pressed against the closed door of Renald's shop in Holtburg, then the
Brawler is inside; the door is drawn closed behind him. It happened during the journey's own vendor leg (`vendor-6`,
goal "object 0x7A9B4021 within 6.5 m" in cell 0xA9B4013F, the route 61.7 m and 10 waypoints), before ACE's own
approach (88.01 s). Cause: `TODO(d25-server-object-host)` (docs/physics.md "Other objects"): the door is a server
object, no host keeps a physics object for it, so neither the player's transitions nor the wayfinding see it, and the
planner routes through the doorway as if it were open. Retail and ACE collide with a closed door; a player opens it
with Use (ACE sets it Ethereal and plays its Open motion, and closes it again after its reset interval).

## 4. Plan: the server-object physics host (fixes §3, and with step 3 the cause of §1.3)

The physics side is built: moving objects' shadows, retail's object-collision rules (a creature is never stepped onto,
the camera passes through creatures, ethereal objects are passed through), doors' physics BSPs posed when they move,
`SetEthereal` with its overlap retry (docs/physics.md "Other objects"). What is missing is the host.

1. **The host (one slice).** A `ServerObjectPhysicsHost` in `Lifestone.Client` keeps one `PhysicsObj` in the player's
   `PhysicsWorld` for each server object whose Setup has collision (cylinders, spheres, or parts with physics BSPs:
   doors, lifestones, NPCs, creatures): made on the avatar's create, moved with `ForceIntoCell` when the avatar's
   position or cell changes (`RecalcCrossCells` on demand), its state from the object's PhysicsState (Ethereal: an
   open door), removed on delete, pickup or leaving the world. Wired where `PlayerRig` gives the avatar set to the
   player's physics. Tests: a closed door's BSP stops `OfflineWalker`; the same door Ethereal lets it through; a
   lifestone's cylinder stops it; an NPC's cylinder moves with the NPC; the allocation tests stay at 0 bytes for a
   standing crowd. Closes `TODO(d25-server-object-host)` for the player; the lifestone step-off (`TravelStations`)
   and its tests are revisited.
2. **Doors in the journeys (one slice).** The wayfinding treats a closed door as blocked; a leg on foot whose route
   crosses one Uses it first (an `IActionApi.Use` the server answers by opening it) and waits for the Ethereal state
   before walking on; the vendor station's Renald leg is the live check (the door opens on screen, the Brawler walks in
   through it). A door that does not open within its timeout fails the leg, named.
3. **Transitions for server objects (a larger item, D24's deferred part).** The avatar body's step runs a transition
   through its host object instead of taking the frame as is, so creatures collide with each other and with the
   player as in retail. This is the fix for §1.3: two creatures stuck to the player no longer overlap, so the client's
   sticky spots agree with ACE's and the stick's end no longer snaps them back. It also replaces the ground clamp
   (`PhysicsWorld.TryGroundZ`) for stepped objects. Its cost (a transition per moving object per frame) is measured
   against the 50 ms frame rule before it lands.

Steps 1 and 2 are needed for the hero video's vendor station; step 3 belongs to a milestone (M5 or later), and the
detector of §1.4 tells whether it removes the flicker.
