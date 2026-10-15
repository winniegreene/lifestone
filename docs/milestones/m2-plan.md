# Milestone 2: implementation plan

The plan of record for [M2](m2-log-in.md), which stays the specification: its definition of done, portal space (§4) and
the retail user interface (§5). This file says who builds what, in which order, against which contracts, and how each
step is checked.

- **Status.** Approved by the owner on 2026-10-12, with the decisions of §10 applied. Wave 1 is merged on master (f2bcecd) and the end-of-wave-1 documentation pass is applied. Wave 2 (the Godot tracks) is under way: items 1 to 4 are merged on master (0455907): portal space offline (G1 W1-W5 and W2-pre(a)), the retail login screens (W2-pre(b), G2 U2-U4 and U9), retail character creation (G3 U5) and the player walking offline (G5 W8 and part of W2-pre(c)). Two owner decisions followed wave 1: D36 (the shell) and D54 (CharGen's emotes), with D40 amended. m2-log-in.md §7 records them as decisions 11 and 12. After item 4 the owner dropped the stress crowd (`--avatar-stress`; `AvatarStress` stays as test code only), set the order of the rest of wave 2 (§6.2, item 12) and asked for an M2 hero video (m2-log-in.md, Status). Item 5a, real login to the world (G6 W9, W10, W12 `enter`, AV13, W6), is merged with its gate fix (136c9da); 5b, movement that ACE accepts (sliced in §6.5), is merged with its review fixes (026df3c) after its live gate; the owner decided that graduated (the Academy exit portal's emote set) is the test characters' baseline. The interim login video (login, the Academy, the exit portal to Holtburg, logoff; kept in `cache/video`, never committed) is made, with two capture options added for it (`--record-audio`, `--clean-hud`; the Video capture group). G4, the in-game shell, is merged (sliced in §6.6, with the splits, parallel lanes, review and live fixes recorded there as run): the GamePlay root, the spew box, chat, vitals, radar, toolbar, indicators, selection with health, character information, options and the logout confirmation; its live gate passed (`smoke.ps1 -Backend require` 21 of 21, `login-check` and `login` with `shell=ok`). Two more owner decisions came with it: D57 (tests and frames stay at 800x600) and D58 (the hero video has no sound). G7, the Lifestone panels, is merged too (the tools panel and the automation inspector, sliced in §6.7 with the lanes, review and live fixes recorded there as run). Wave 3 has run (§6.8): six reviews found 64 findings and no blockers, four fix lanes closed them on the branch `m2/w3`, and the gate passed (the DoD audit table is in m2-log-in.md, Status; eight owner decisions are open). The owner settled seven of them (D59-D65), and the decisions follow-up built what they require (§6.9): the mod's `char purge` verb and the live throwaway leg, CharGen's idle loop and the materialise fade; DoD 3 and 6 are now met with named gaps and DoD 10 is met. Next: the owner's review (§6.3 step 5), the hero video and the private push. §6.4 holds the cost-hygiene rules.
- **How it was made.** A lead-foundation plan, eight track plans (net, state, portal, avatars, physics, retail UI,
  server mod and the wave-2 Godot plans) and three critiques (DoD coverage, seams, risk) were merged into this one.
  Where ownership was contested, it names a single owner, and §1.4 records each choice.
- **Citations** are `path:line` against `master` at 5f33af2 (`m1:n` is a line of m1-explore-dereth.md), a section
  of m2-log-in.md (`m2 §n`), or a decompile function with its address. Addresses name their build (11.4186 or
  11.6096) where the track plans did; L10's table gives both addresses for every function the tracks cite.
- **Changes** after approval are additive. A change to a contract or an owner goes through the lead and is recorded
  here.

---

## 1. Context

### 1.1 Why

M1 made Lifestone an offline explorer of Dereth. M2 turns it into a client. It logs in to the bundled localhost ACE, shows the retail screens, plays portal space, draws what ACE creates and walks the player in a way ACE accepts. All of this runs through an automation-ready state layer (A0). The owner judges progress by what is on screen, so the work is ordered by visible results (m2 §3, "Visible results first"): portal space offline, then the retail screens offline, then login as far as the tunnel, then the world with objects in the retail shell, then walking.

### 1.2 What M2 delivers

These are the 15 Definition-of-Done items (m2 §2, with the wording amendments the owner approved on 2026-10-12):
- `tools\play.ps1`, with a loopback-only session layer (1);
- protocol-exact login with no password leaks, and DDD from `DatSet.Iterations` (2);
- retail character management (retail-exact, no 3D view) and creation (with its 3D view, the character standing still), with a valid 55-skill
  CharacterCreate (3);
- the retail UI rebuilt from the dats (4);
- portal space as §4 specifies it (5);
- every server object drawn, with ObjDesc and motion (6);
- the player character, with chase and first-person cameras (7);
- ACE-accepted movement with zero corrections on two scripted walks (8);
- teleports by tools and by portal Use (9);
- admin-free player tools through a server mod (10);
- logoff and quit (11);
- a transport robust to 5% loss and 2% reorder (12);
- A0 (reducer, bus, Action API, recorder and replayer, headless host, loopback gate) (13);
- the test and smoke additions (14);
- frame-time limits (15).

### 1.3 Baseline (verified read-only)

- `master` is at 5f33af2 with four commits. The handoff said one (m2 §1; corrected in L13).
- `Lifestone.slnx:2-10` holds Client, Data, Game, Client.Tests and Data.Tests.
- Directory.Build.props applies TWAE, AnalysisLevel 10.0 and net10.0 only under `src/` and `tests/` (Directory.Build.props:10-13).
- **Tests and smoke.**
  - The last run shows 1,270 tests (Client 551, Data 719; cache/smoke/02-test.log; m2 §1). m1-explore-dereth.md:58 still says 1,200.
  - Smoke has 13 steps (tools/smoke.ps1:97-98). A step can only PASS or FAIL; there is no SKIP and no backend awareness.
- **Not built yet:**
  - networking, audio, EnumIDMap/LayoutDesc/StringTable/Font use;
  - a player, server objects, collision;
  - a game UI.
- **Project rules today.**
  - `Lifestone.Client` declares "no I/O, no networking" (src/Lifestone.Client/Lifestone.Client.csproj:5-6; docs/game-core.md:10).
  - DatSet opens standalone databases and avoids DatCollection on purpose (src/Lifestone.Data/Dat/DatSet.cs:17-19).
- **Nobody has logged in.** ACE enables mods (`ModManager.Initialize`, vendor-eval/ACE/Source/ACE.Server/Program.cs:158) before `DatabaseManager.Initialize` (:250). That method sets `AutoPromoteNextAccountToAdmin = true` when no Admin exists (ACE.Database/DatabaseManager.cs:33). The flag is assigned before `Shard` (:58), and sockets open at Program.cs:312. A mod's `Initialize` therefore cannot clear the flag. This is the most important safety fact in M2.
- `tools/godot.ps1:224` echoes the full Godot command line, so no secret may ever be a game argument.
- **Pins.** ACE 95ef426d (v1.78.4816), OpenAC 1ee5a08, DRW 2.1.9 (built from a9345f2; the vendored a40acb4 has no file changes).

### 1.4 How the plans were merged: one owner per seam

| Seam | Single owner and location | Duplicates removed |
|---|---|---|
| EnumIDMap resolver | lead, `src/Lifestone.Data/Dat/EnumIdMaps.cs` (W0a.1), with a typed `EnumGroup` | portal PS1's own resolver, which took the arguments in the opposite order |
| Motion data types | lead creates them in `src/Lifestone.Data/Motion/` (W0a.1); avatars owns them afterwards | avatars AV4, physics S6 and the lead's Data/Animation copies; net's type is renamed `RawMotionStateWire` |
| CMotionInterp, MovementSystem, Position/Interpolation/Constraint/Sticky/MoveTo managers | avatars, `src/Lifestone.Data/Motion/` | physics S6/S8/S10 (physics consumes them through `IMotionInterpHost`) |
| SmartBox gating (stamps, parking, recreate, position verdicts) | state's reducer; it publishes verdict-tagged events | avatars' own queue and stamp gate in AvatarSet |
| Wrap-safe u16 compare | lead, `Data/World/Sequence16.cs` | three private copies |
| Hide and unhide rule | portal `HiddenTransition` (pure rule) | avatars AV9's own derivation |
| Action API, session facade, IMovementSink, CharacterCreateIntent | state, checkpoint C-state | lead F6 `IGameActions`, retail-ui `IUiActions`, renderer `IRetailGameActions`, physics' second `IMovementSink` |
| UI read model and stand-in data | screens read `IGameSessionView`; one stand-in, `GameSession.Offline` fed by `StandInSessions` (state) | retail-ui `IUiSessionModel`/`StandInSession`, renderer `IRetailGameModel`/`StandInGameModel`/`A0RetailGameModel` |
| Session, dispatcher, tap, credentials | net N0, checkpoint C-net; state adapts to net's names | state's assumed `IServerMessageHandler`, `BeginLogin` and similar; lead `SecretText` |
| Loopback gate | net, `src/Lifestone.Net/Sockets/LoopbackPolicy.cs` (C-net); server mod compile-links it; state wraps it for logging | lead `Loopback.cs`, server-mod `Shared/LoopbackPolicy.cs`, state's standalone gate |
| MotionCommandTable | net, but placed in `src/Lifestone.Data/Animation/` | the lead table's "stays in Lifestone.Net" |
| Readiness contract `IWorldAttachments` + `AttachState` | lead, `Client/Streaming/WorldAttachments.cs` in W0a.1 (portal owns it afterwards); it includes Collision, VisibleCells and SeenOutside | portal's copy in Client/PortalSpace, physics' `CollisionAttachState`, state's `IStreamingReadiness` |
| Collision streaming | physics `CollisionStreamer` (Client/Movement), used by both hosts | godot-world's `CollisionLayer` as an `ILandblockLayer` |
| Portal frame order | portal `PortalSpaceDirector` (Client); the Godot node only presents | godot-world's second director |
| Audio seam | portal owns `ISoundSink` (façade over `RetailSoundPlayer`), `IAudioOutput` and `IEffectEventSink`; godot-world implements `IAudioOutput` only | godot-world driving VoicePool itself |
| Retail UI render path | retail-ui owns one software compositor (`IUiPainter`/`IUiRegionView`/`UiCompositor`). It is both the CPU test oracle and the production path (Godot uploads dirty tiles) | retail-ui R10's retained `IUiRenderSink`, and a second compositor in the renderer's U1 |
| Lifestone layout reader | retail-ui R9 in `Data/Ui` | the renderer U7 copy in Client |
| Screens | wave-2 screen tracks (G2 login, G3 CharGen, G4 shell) | retail-ui R17/R18-pages/R19 |
| Inspector | G7, on the retail engine | state S18's plain panel |
| `scripts/Session/**` | godot-world track G6 | state S17/S18 |
| `tools/play.ps1` | lead (L18), using server-mod's `Get-LsBackendState` | server-mod S7, godot-world W12 |
| Routes and MovementAudit | physics `Client/Movement/Routes` | state S11/S16 copies, godot-world W12 copies |
| Scenario runner (headless and Godot) | state `IScenario`/`HeadlessRunner`; G6's `LoginCheckRunner` runs the same scenarios | separate runners |
| Loopback tests | all of them in `tests/Lifestone.Loopback.Tests/<Track>/` | loopback helpers inside Net.Tests, Client.Tests and ServerMod.Tests |
| Inspector, tools-panel layouts | G7 (`Data/Ui/Layouts/*.json`, read by the R9 reader) | a second reader |

### 1.5 Critic fixes accepted, modified or rejected

Accepted:
- one owner per file and one contract per seam (all three critics);
- typed EnumGroup;
- tests/Shared moved entirely in W0a.1;
- `ManualTimeProvider` timers fire on `Advance`;
- the allocation fixes (MotionPlayer, EffectObject) and the ObjectClock extraction in W0a.1;
- the readiness contract in W0a.1;
- forking at W0a.1 instead of W0a.2;
- checkpoints C-state, C-portal, C-ui and C-physics;
- a rolling merge DAG instead of a fixed seven-step order;
- Tier=Soak excluded from the default test run;
- provenance pins matched by SHA prefix, with DRW a9345f2 on the allow-list;
- a stricter DoD-13 scan (no `Lifestone.Net` token anywhere in Lifestone.Game);
- the CharGen-to-wire mapper moved into state;
- tests/Lifestone.ServerMod.Tests added to the solution;
- the Academy cell fix assigned (ST12);
- the 11.6096 address lookup made a lead step;
- a firewall-prompt check on the first live login;
- the sentinel Admin account (decided, D11);
- the DoD-wording amendments (approved by the owner on 2026-10-12: DoD 3, 8, 10 and 12).

Modified:
- **Cross-process serialisation.** A lock file (`<repo>\cache\loopback.lock` in the main checkout, which every worktree shares; opened with FileShare.None and released by the OS if the process dies) replaces both the named Mutex and the named Semaphore. A Mutex is thread-affine under xUnit fixture disposal, and a semaphore's count is not restored when the holder crashes.
- **Retail UI programme.** The risk critic proposed one merged programme. It is kept as two waves with an exact folder split and the C-ui checkpoint. Merging would serialise the Godot work behind the whole data/runtime layer.

Rejected:
- **Explicit Game→Net ProjectReference** (lead F1). The reference is transitive through Client, and with `SessionBootstrap` in Client/Session no Game file needs Net at all, so the scan forbids any `Lifestone.Net` token in Game.
- **`OptionScenes.All` including Headless** (state). `All` keeps its meaning of the three Godot scenes (OptionSpec.cs:39); otherwise `--vsync` and similar options would apply to headless runs.
- **Full collision transitions for remote objects** (physics owner decision 6). It is recommended only as a later step. M2 uses interpolation plus a ground probe (owner decision D24), because transitions for about 150 objects threaten DoD 15 and are not in the DoD.
- **Separate `--walk-physics` option.** Explore-mode walking uses the physics `OfflineWalker` once collision exists. M1's `CellWalker` is a labelled placeholder (CellWalker.cs:22), not a retail behaviour that needs a flag.
- **State's `ProtocolWalkScenario`.** Net's live soak (N12) already has a test-only kinematic walker. A second placeholder mover in product code is unnecessary.

### 1.6 Where the spec disagrees with code or sources

Section numbers in this list are m2-log-in.md's, as they stood at the handoff. The lead carries these into m2-log-in.md and protocol.md: in L13 for the wave-0 items (1-6, 18, 21-23 and the DoD 8 wording of 19), and in the doc pass at the end of wave 1 for the rest. The owner approved the DoD wording changes on 2026-10-12. Items marked *(L13)* are applied. The end-of-wave-1 doc pass then applied items 6 (the idle emotes: D54), 7-10, 12-15, 16, 17 (the gameplay root and the table count), 24 and 26, and the FOV clamp and near threshold of 11; what is left is the rest of item 11, and items 20, 25 and 27.

1. m2 §1 said one commit; there are four. *(L13)*
2. m1:58 says 1,200 tests; the count is 1,270. *(Wave 0, lead: m1-explore-dereth.md.)*
3. §3 step 5 and §5.1 say decoding needs a DatCollection. It does not: `BaseProperty.UnpackGeneric` accepts a reader bound to a PortalDatabase (DRW Types/BaseProperty.cs:53-63), and `DatBinReader`'s constructor is public (Lib/IO/DatBinReader.cs:40). DatCollection also always opens highres (DatCollection.cs:63-95) and reads the portal before highres (:214-229). *(L13)*
4. §3 gives the UIASSET resolver to portal only; it is shared. §3 gives retail UI only Client/RetailUi; decoding belongs in Data (m1:98-100). §3 gives avatars and physics only one folder each; they need Data/Motion, Data/Appearance and Client/Movement. *(L13)*
5. The Client rule (Lifestone.Client.csproj:5-6; game-core.md:10) must be amended to "may use Lifestone.Net records and interfaces; no sockets; no file I/O; hosts supply bytes and Streams". *(L13: game-core.md §1; the csproj comment in L1.)*
6. DoD 3 and §5.3: retail 11.4186 character management has no 3D view (`gmCharacterManagementUI` constructor, gmCharacterManagementUI.c:577-657). The UIASSET idle emotes are loaded only by `gmCG3DView` (gmCG3DView.c:86-92) and nothing ever selects one. Decided by D35: character management is retail-exact with no 3D view, and CharGen keeps its 3D view. *(L13: DoD 3, §5.3 and §5.4.)* The owner then decided that CharGen's view is retail-exact too, with no emote played (D54; m2 §7, decision 12).
7. §4.2: 0x10000005 is the CharGen preview's rest pose, played at 0 fps. Heritage branches use 0x10000011-0x10000014.
8. §4.3: a separate World3D does not isolate Lifestone lighting, which uses global shader uniforms (project.godot `[shader_globals]`; RenderGlobals.cs:56-67). `CreatureMode::Render` also turns off the sun and disables degrades (SmartBox.c:932-1010).
9. §4.4: 0x1A is text type 26 (notice 100005), shown only by `gmSpewBoxUI`, class 0x10000016 (gmSpewBoxUI.c:258-327).
10. §4.5:
    - The exit window is frames 68-76 under the decompile's float maths (gmSmartBoxUI.c:830-843).
    - `BeginTeleportAnimation` also resets `teleportCurVDist` and zeroes the roll fields (:150-176).
11. §4.6:
    - `--fov` is TerrainPreview-only (OptionCatalog.cs:209), and the main camera is fixed at 60° (FlyCamera.cs:134).
    - The FOV clamp is [0.001, π] and the near threshold 0.40000001, and the FOV is vertical (Render.c:200-262).
    - `f_game` uses `gameFOV/(aspect−0.1)` (SmartBox.c:115-125).
    - AnimLevel multiplies by the literal 0.0101010101010101 (UIGlobals::Init).
    - `ViewFrustum` throws at 179° or more (ViewFrustum.cs:37-40).
12. §4.7:
    - F751 is accepted when not older (SmartBox.c:463-490).
    - Retail's `blocking_for_cells` is a DDD prefetch state (CellManager.c:108-187), so the M2 rule is a Lifestone adaptation, and it must include collision.
13. §4.8:
    - During logoff, retail Ends at state 3 (gmSmartBoxUI.c:731-755).
    - The +20 s is weenie virtual slot 7, probably PK status.
14. §4.9:
    - A hidden object freezes animation and physics.
    - `set_state` reacts only to a change of the Hidden bit.
    - An object created Hidden plays the Hidden script.
    - `set_hidden` also changes IgnoreCollisions and ReportCollisions.
15. §4.10:
    - The last of N sound variants is unreachable.
    - The probability roll uses CRT rand.
    - UI sounds follow the effects slider.
    - Attenuation is in whole dB with VOL_MIN −50.
    - Sound is 2D.
    - DRW's SoundTable is correct only while no node has grandchildren.
16. §4.11: check suites run windowed off-screen, not headless (EngineChecks.cs:65-68). §4.12 names no capture option, so this plan adds `--portal-space-frames`, and smoke frames go under smoke's OutDir.
17. §5:
    - §5.2: type 3 Field is a container and drop target; text entry is Text 0x0C (UIElement_Field.c; UIElement_Text.c).
    - §5.1: 0x23000001-0x23000010 is 16 IDs, but there are 15 tables.
    - §5.3: whether the gameplay root UILAYOUT 0x10000006/0x10000495 is "classic" or floaty is unverified. R2 found it is the floating-window shell, and the owner chose it (D36; m2 §7, decision 11).
    - §5.4: the integer rule gives scale 1 at 1080p.
    - §5.6: `gmMainChatUI` lives in gmChatUI.c.
18. §6: ACE's Packets logger is at INFO, not off (server/templates/log4net.template.config:40-42). *(L13)*
19. DoD 8:
    - The cadence has been found. It sends every 1.0 s if the frame or cell changed, immediately on a cell change or a contact-plane change, and only while Contact && OnWalkable (CommandInterpreter ctor, ShouldSendPositionEvent, SendPositionEvent; 11.4186 0x006B3E40, 0x006B45E0, 0x006B4770).
    - ACE rejects a move only when it is over 50 m **and** crosses more than one landblock (Player_Tick.cs:451).
    - "Agree step by step" does not hold: ACE forces non-PK positions and uses a quantum of 0.1. *(L13: the claim is dropped; "agrees within 0.5 m" stays.)*
20. DoD 9: an out-of-range Use makes ACE send MoveToObject (Player_Move.cs:156-175).
21. DoD 10: the "56 points of interest" are 56 destinations with 62 names. *(L13)*
22. DoD 12:
    - "No Checksum Failed in ACE's log" cannot be observed. Encrypted-packet failures are only counted, and cleartext ones are logged at DEBUG (ClientPacket.cs:138-165). The oracle is the mod's `net` counters.
    - "Returns to the login screen" means retail's disconnected screen.
    - OpenAC's lossy decorator only drops packets.

    *(L13: the first two.)*
23. §7:
    - Open question 3 is answered by DoD 1.
    - Decision 3's "loads only when bound to 127.0.0.1" is worded as if the mod can veto loading. It cannot; it throws from `Initialize`, which leaves it Inactive (ModContainer.cs:107-121).

    *(L13)*
24. protocol.md:
    - Ground truth should be 95ef426d; its network files are identical to 7715c052.
    - CharacterDelete (F655 {account, list index}) and CharacterRestore (F7D9 → F643) are missing.
    - The F658 deletion field is always 1.
    - The F7DC reason is optional.
    - F7E6 uses type 1 for cell and language.
    - The transport line counts are wrong.
    - The String32L prefix is the length.
    - Only ASCII round-trips through ACE.
    - F7E5 can precede F658.
    - Failures arrive after ConnectRequest.
    - ACE never sends Disconnect.
    - `account_login_boots_in_use` is not covered.
    - The tail probe is needed.
    - Inbound order is global by fragment sequence.
    - Use 0x0036 and the chat, event and self opcodes are missing.
    - §5.3:586 omits retail's ConstrainTo for the player.
    - F7DB is a forced recreate.
    - Messages for unknown objects are parked.
    - §9.2 hard-codes the iterations.
    - §9.4 item 3 captures fixtures through the Packets DEBUG log, which leaks the password.
    - §9.3/§4.3 should use `@lifestone` instead of telepoi/myloc.
25. Server text:
    - server/README.md:112, server/templates/Config.template.js:24-26 and server/setup-server.ps1:152 still promise first-account Admin.
    - setup-server.ps1:92,94 lack `--disable-build-servers`.
    - ACE at the pinned tag reports itself as 1.77/4815 (ServerBuildInfo_Dynamic.cs:6-10).
26. src/Lifestone.Client/Interiors/InteriorLocations.cs:70-77 uses 0x860301AD. ACE spawns new characters at CharGen `StarterAreas[0].Locations[0]` = 0x860201AD (PlayerFactory.cs:356-362; protocol.md:348). Fixed in ST12; interiors.md, objects.md and third-party/interiors.md follow it.
27. Stale comments: TextureLoader.cs:29-33 (caching strategy) and MotionPlayer.cs:134 (nonexistent `ObjectClock`).

---

## 2. Wave 0 (lead)

### 2.1 Shape

- **W0a.1, the fork gate (about 2.5 agent-days): L0-L10.** All seven wave-1 tracks fork from this commit. Server-mod may fork right after L0, because it owns `server/` and needs only the solution entry and the Client link from L1.
- **W0a.2, the merge gate (about 2 agent-days, in parallel with wave-1 days 1-4): L11-L14.** It must be on master before the first **end-of-track** merge. Checkpoint merges only need the W0a.1 gate.
- **Wave 0 adds no NuGet package.** Every need is covered by the BCL, Godot or the approved xunit trio (Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4).
- **How each wave runs (D1).** Each wave runs as a multi-agent workflow, kept lean: the slices are implemented, one adversarial review reads them, and one fix pass answers it. Every slice works in its own worktree on its own files, and asks the lead for any change outside them.

### 2.2 W0a.1, in order

**L0. Baseline and pins (no edits).**
1. Run the following with `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1`, `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` and `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`:
   - `dotnet build Lifestone.slnx --disable-build-servers` → 0 warnings;
   - `dotnet test` → 1,270 (551 + 719);
   - `pwsh -File tools\smoke.ps1` → 13/13.
2. `server\start-server.ps1`; the ACE log shows the world open, the "no admin accounts" warning and no "Auto creating account" line; then `stop-server.ps1`. No client connects.
3. `git -C vendor-eval/<x> rev-parse HEAD` for ACE 95ef426d, OpenAC 1ee5a08, ACE-proto 7715c052, ACViewer c5c54fb and DRW a40acb4; `gh api repos/Chorizite/DatReaderWriter/compare/a9345f2...a40acb4` shows no file changes.
4. `git config --get core.longpaths` = true.
5. Record the dat iterations p2072-c982-l994-h497, fly-path max 21.5 ms and test time of about 62 s.

**L1. Projects and references.**
- `src/Lifestone.Net/Lifestone.Net.csproj`:
  - Microsoft.NET.Sdk, with no PackageReference or ProjectReference (BCL only);
  - `GenerateDocumentationFile` with NoWarn CS1591;
  - `InternalsVisibleTo` Lifestone.Net.Tests and Lifestone.Loopback.Tests.
- `tests/Lifestone.Net.Tests` (trio; references Net).
- `src/Lifestone.Headless`:
  - Exe with `UseAppHost=false`, run as `dotnet Lifestone.Headless.dll`; references Data and Client;
  - `Program.cs` is a loud stub: `--help` prints the Headless options; anything else writes `TODO(state): headless host not built yet` and exits 2.
- `tests/Lifestone.Loopback.Tests`:
  - references Client, Data and Headless;
  - `[assembly: CollectionBehavior(DisableTestParallelization = true)]`;
  - subfolders Net/, Session/, ServerMod/, Movement/.
- `tests/Lifestone.ServerMod.Tests` (trio; references Client for ToolsWire).
- `Lifestone.Client.csproj`:
  - ProjectReference to Net;
  - comment reworded per §1.6 item 5;
  - `<Compile Include="..\..\server\mod\Shared\**\*.cs" Link="ServerTools\%(RecursiveDir)%(Filename)%(Extension)" />`, which matches nothing until SM1.
- Lifestone.slnx gains the five projects. The server mod stays outside the solution and the props gate.

**L2. DatSet: language-dat records without DatCollection** (src/Lifestone.Data/Dat/DatSet.cs).
- `TryGetLocal<T>` runs under SyncRoot: `Local.TryGetFileBytes`, then `Unpack(new DatBinReader(bytes, Portal))`. The single SyncRoot stays, because a Local read touches `Portal.MasterProperty`.
- New members: `TryGetLocalBytes`, `GetAllLocalIds<T>`, `HasLocal`, `RequireLocal(feature)`, `EnumMaps`, and `internal Open(DatSetPaths)`.
- Local stays optional at the DatSet level (D2). Play mode, the retail UI and the headless host call `RequireLocal`.
- `DatSet.Open` turns a dat whose header names another type into `InvalidDataException`; `GameServices.OpenDats`
  keeps its M1 catch filter and logs `local=`.
- Fix the stale comments: TextureLoader.cs:29-33 and MotionPlayer.cs:134.
- Tests (`tests/Lifestone.Data.Tests/Dat/LocalDatTests.cs`):
  - local iteration is 994;
  - all 101 LayoutDescs and 2,162 elements decode (98 at 800×600, 3 at 800×500; Tier=Exhaustive);
  - the string tables decode, with the missing ID in the 16-ID range named;
  - MasterProperty 0x39000001 decodes;
  - parallel local and texture reads give consistent results;
  - with no language dat, only local reads fail.
- Fallback if a DRW bump breaks the reader route: an internal DatCollection with HighResDatPath aliased to the portal. The public API is unchanged.

**L3. EnumIdMaps** (`src/Lifestone.Data/Dat/EnumIdMaps.cs`).
- Re-expresses DBObj::GetDIDByEnum (11.6096 0x00415640) and DBCache::GetDIDFromEnum (0x00413BF0).
- Takes a typed `EnumGroup`. It reads `ClientEnumToID` first and falls back to `ServerEnumToID`, which is retail's EnumToIDInternal.
- Group constants come from the decompile: StringTables 4, UiLayout 5, UiCursor 6, UiAsset 7, Font 9. KEYMAP is pinned by test from the master map's names. Every constant is pinned to its map DID: 7→0x25000010, 5→0x2500000E, 6→0x2500000F, 9→0x25000012.
- Tests:
  - the full §4.2 UIASSET chain;
  - UILAYOUT patch→0x21000000, charactermanagement→0x21000004, chargen_master→0x21000038, classic_smartbox→0x2100000F;
  - the Internal fallback, using a synthetic map;
  - an unknown enum throws with both numbers in the message;
  - `MasterMapId == Portal.Header.MasterMapId`.

**L4. Shared data types.**
- `src/Lifestone.Data/World/LandDefs.cs`: moved from AcCoordinates.cs:118-272. AcCoordinates delegates to it, so callers are unchanged.
- `src/Lifestone.Data/World/CellFrame.cs` and `src/Lifestone.Client/World/CellFrameExtensions.cs` (ToCellPosition, ToGlobal).
- `src/Lifestone.Data/World/Sequence16.cs`:
  - `IsNewer`, `NewerEvent` and `NotOlder`;
  - re-expresses CPhysicsObj::is_newer and newer_event (11.4186 0x00451AD0, 0x00451B10).
- `src/Lifestone.Data/Motion/{RawMotionState.cs, InterpretedMotionState.cs, MotionActionNode.cs, HoldKey.cs}`:
  - data and defaults only: two partial classes (the fields, `InitDefaults` and a `Clone`), a record struct and an enum;
  - each file carries its own single-source port header from ACE @95ef426d (AGPL-3.0): `Source/ACE.Server/Physics/Animation/RawMotionState.cs`, `InterpretedMotionState.cs` and `ActionNode.cs`, and `Source/ACE.Entity/Enum/HoldKey.cs`; docs/third-party/foundation.md lists them.
- `src/Lifestone.Data/Checkout/CheckoutLocator.cs`. `DatSet.FindDefaultDirectory` is rebuilt on it.
- Tests:
  - LandDefsTests at map edges 0x00 and 0xFE;
  - CellFrameTests (w,x,y,z order, heading, outdoor/EnvCell);
  - Sequence16Tests (0xFFFF→0, the 0x7FFF/0x8000 boundary);
  - MotionStateDataTests (defaults NonCombat 0x8000003D, Ready 0x41000003, speeds 1);
  - CheckoutLocatorTests with temp folders.

**L5. Small shared allocation and clock fixes.**
- `MotionPlayer.Update` and `DefaultAnimationPlayer.Update` return a reused hook list instead of `_fired.ToArray()` (MotionPlayer.cs:45,140).
- `EffectObject.DrainEvents(List<EffectEvent> into)`, and a two-list swap instead of `ToArray` (EffectObject.cs:265,308).
- `src/Lifestone.Data/Effects/ObjectClock.cs` is extracted from EffectActor.cs:77-109 (quanta 1/5 and 1/30, skip >2 s); EffectActor delegates to it.
- Tests: the existing EffectActor and motion tests are unchanged; allocation tests prove 0 bytes per `Update` after warm-up.

**L6. Logging and the clock rule.**
- `src/Lifestone.Client/Diagnostics/LogSink.cs`: `LogSeverity`, `LogField`, `ILogSink` and `NullLogSink`.
- A `GodotLogSink` adapter in `scripts/Core/Log.cs`; Game keeps its own `LogLevel`.
- Rules for game-core.md:
  - `System.TimeProvider` for every network, session and action timeout;
  - frame-stepped machines (portal, physics, avatars) take `double now`, which is monotonic real seconds advanced once per frame and never `DerethTimeController.ClientClock` (DerethTimeController.cs:72-76).
- `SecretText` is dropped. Net's `AcCredentials` holds the password, and no consumer needed SecretText.

**L7. Readiness contract** (`src/Lifestone.Client/Streaming/WorldAttachments.cs`).
- Contents: `AttachState` and `IWorldAttachments` (sketch in §4.5).
- The rule "Failed and Disabled count as settled" is documented in the XML docs.
- The portal track owns the file after the fork; changes are additive only.

**L8. Option registry for M2.**
- `OptionCatalog` becomes a `static partial class`. Empty per-owner groups are already listed in `Registry`:
  - Session, Headless (state);
  - PortalSpace, Audio (G1);
  - RetailUi (G2);
  - Avatars, Play (G5/G6).
- Add `OptionScenes.Headless = 8`. `All` is unchanged. Core's `--dat-dir`, `--log-level`, `--quit-after` and `--workers` get `AlsoIn: Headless`.
- `OptionRegistry.Problems` stops echoing `argument.Raw` (OptionRegistry.cs:88,92); it prints the name and "(value hidden)".
- `FormatHelp` skips empty groups.
- `OptionCatalogTests` scan (OptionCatalogTests.cs:21-23) widens to the parser files in `src/Lifestone.Client/**/*Options.cs` and `src/Lifestone.Headless/**`, excluding `src/Lifestone.Client/Options/`. It asserts more than 60 options are found, so the test cannot pass vacuously.
- Publish the option-name table (§5.4), including the single play switch (D9).
- Tests:
  - `An_unknown_argument_s_value_is_never_echoed`;
  - `No_option_takes_a_secret`, which rejects any name containing pass, secret, token or key;
  - empty groups are absent from `--help`.

**L9. tests/Shared, complete** (compile-linked into every test project with `<Compile Include="..\Shared\**\*.cs" LinkBase="Shared" />`).
- Moved, with namespaces unchanged:
  - `RequiresDats{Fact,Theory}`, `RequiresHighResDats{Fact,Theory}`;
  - `RepositoryFiles` (replaces the four private walkers: LegalNoticesTests.cs:64-74, BuiltinMaterialColourRuleTests.cs:64-70, TerrainShaderModelTests.cs:179, TerrainShaderGlobalsTests.cs:59);
  - `AllocationMeasurement`.
- New:
  - `RequiresLocalDats{Fact,Theory}`;
  - `ManualTimeProvider : TimeProvider` (`Advance`; `CreateTimer` fires on `Advance`);
  - `RecordingLogSink`;
  - `Loopback/{LoopbackFact, LoopbackTheory, LoopbackCollection, LoopbackLock, BackendProbe}`.
- **BackendProbe** never sends a packet and never reads a secret value. It is ready only when all of these hold:
  - the enclosing checkout's `server/runtime/secrets.json` exists and contains the key `accounts.test` (checked by key only);
  - pids.json `ace` is live (PID, path and start time, as in LifestoneServer.psm1:295-310);
  - `ace.modReady == true`;
  - `IPGlobalProperties.GetActiveUdpListeners()` contains 127.0.0.1:<acePort from server/versions.json>.
- **LoopbackLock** opens `<repo>\cache\loopback.lock` in the main checkout with FileShare.None and DeleteOnClose, waits at most 10 minutes, and gives a clear skip reason.
- **Conventions:**
  - `[Trait("Tier","Soak")]` marks long tests, which the default run excludes;
  - a reflection test enforces that every `LoopbackFact` user carries `[Trait("Category","Loopback")]` and `[Collection("Loopback")]` and lives in Lifestone.Loopback.Tests.
- The gate checks that per-project test counts equal the old counts plus the new tests.

**L10. 11.6096 address table** (D4, approved).
- Read Mag-nus/acclient-AI-RE `2015-10 11.6096/docs/symbol_transplant_11.6096.tsv` once into `cache/research/`, pinned by repository commit SHA and SHA-256.
- Add `tools/research/Find-RetailSymbol.ps1`, a read-only lookup.
- Publish `docs/research/retail-addresses-m2.md` with function, 11.4186 address and 11.6096 address for every function the tracks cite. The tracks submit their lists in their first two days.
- End-of-track merges reject any remaining `TODO(...): 11.6096` citation.

### 2.3 W0a.2 (merge gate)

**L11. Hygiene and architecture tests** (`tests/Lifestone.Client.Tests/Repository/`).
- **ProvenanceTests:**
  - the M1 header grammar (MotionCommands.cs:1-3);
  - the pin's SHA must start with an allow-listed prefix: ACE 95ef426d or 7715c052, OpenAC 1ee5a08, ACViewer c5c54fb, Chorizite/DatReaderWriter a9345f2. The 13 existing headers carry full SHAs;
  - MIT ports keep their copyright line;
  - the ported file is named in some `docs/third-party/*.md`;
  - OpenAC files under src/Lifestone.Net are listed in `src/Lifestone.Net/THIRD-PARTY-NOTICES.md`.
- **ArchitectureTests:**
  - GodotSharp only in Game;
  - `System.Net.Sockets` only in src/Lifestone.Net;
  - no `File.`, `FileStream`, `Directory.` or `Process.` in src/Lifestone.Client;
  - no `DateTime.Now`/`UtcNow`, `Environment.TickCount`, `Stopwatch` or `Thread.Sleep` in src/Lifestone.Net or Client/Session (except SessionClock.cs);
  - `DatCollection` and `QualifiedDataId...Get(` only in DatSet.cs;
  - no `Lifestone.Net` token in Lifestone.Game, and `GameSession.Create`/`SessionBootstrap.CreateLive` only in `scripts/Session/SessionHost.cs` (DoD 13).
- **LoopbackOnlyTests:** no `IPAddress.Any`, `IPv6Any` or a string literal of the IPv4 unspecified address in src/ or tests/.
- **NoPersonalDetailsTests** (D7): scans tracked text for the current user and machine name, drive-rooted profile paths, non-noreply e-mail addresses and non-loopback IPv4. It reports file and line only, never the value.

**L12. Tooling.**
- **smoke.ps1:**
  - a `SKIP` result (yellow, not a failure);
  - `-Backend auto|start|require`: auto runs login only when the backend is ready; start (D12) starts the backend and then stops what it started; require turns SKIP into FAIL;
  - `Invoke-Godot -Resolution`;
  - the test step uses `--filter "Category!=Loopback&Tier!=Soak"`;
  - `Measure-SecretLeaks` reads the passwords in memory and scans OutDir, `cache\godot\**\logs`, `cache\sessions` and `server\runtime\logs\ACE_Log*.txt` as UTF-8 and UTF-16LE. It reports counts and file names only;
  - stray-output gains .wav, .mp3, .ogg, .flac, .bin and .lsrec;
  - `$allSteps` keeps its 13 steps. Each new step joins in the merge that brings its harness (L19).
- **godot.ps1:** `-Offscreen` adds `--audio-driver Dummy` unless `-Audio` is given.
- **Toolchain.psm1:** export `Get-LifestoneCheckouts` (Toolchain.psm1:95 exports only two functions) and add `Find-LifestoneServerRuntime`.
- **.gitignore:** `*.wav *.mp3 *.ogg *.flac *.lsrec`, and `/cache/` already covers fixtures and recordings. **.gitattributes:** mark `*.mp3 *.bin *.lsrec` binary.
- **NuGet lock files (D6):** `RestorePackagesWithLockFile` in Directory.Build.props, with the lock files committed and no new packages.

**L13. Docs.**
- `docs/milestones/m2-plan.md` (this file), the lead plan of record:
  - the §1.4 owner table;
  - the per-file table in §2.4;
  - the contracts with their owners after the fork;
  - the checkpoints;
  - the merge DAG and gates;
  - the owner decisions of 2026-10-12 (§10).
- m2-log-in.md:
  - Status "in progress", pointing here;
  - the owner decisions of 2026-10-12 in §7;
  - the wave-0 corrections from §1.6 (items 1-6, 18, 21-23);
  - the approved DoD wording: DoD 3 (character management retail-exact, CharGen keeps its 3D view), DoD 8 (no "step by step" claim), DoD 10 (56 destinations with 62 names) and DoD 12 (the mod's net counters; retail's disconnected screen).
- game-core.md §1 (new projects and the Client rule), the clock rule, and a stub for the process-priority and frame-budget table.
- The README layout ("three projects", README.md:253) and the Development section (Loopback filter, Soak tier, `-Backend`).
- `docs/third-party/foundation.md` for the ACE motion-data port.

**L14. Fork.**
- For each track: `git worktree add .claude/worktrees/m2-<track> -b m2/<track> master`. `/.claude/worktrees/` is gitignored (.gitignore:110), and `core.longpaths` comes from the shared config.
- Tracks rebase after every checkpoint. Never `git stash`.
- The backend runs only from the main checkout, and the lead schedules mod reinstalls and ACE restarts.
- Commit with `TZ=UTC` and the repository identity. Push only `master`, only when the owner asks.

### 2.4 Owners of contested M1 files during M2

| File(s) | Owner | Rule |
|---|---|---|
| Data/Animation/MotionPlayer.cs, AnimSequence.cs | avatars (after L5) | portal uses AnimSequence as is: `Clear` + `AppendAnimation(new AnimDataRef(id,1,-1,40f))` |
| Data/Effects/** (EffectObject, EffectActor, ObjectClock, RetailRandom) | lead | changes by request only |
| Data/Models/GfxObjMesh.cs, ModelAssembly.cs; Data/Textures/TextureLoader.cs | avatars | additive only: `SurfaceIds`, `WithPartGfxObjs`, `GetOriginalPaletteId` |
| Data/World/Cells/** | physics | adds siblings (`CellStructPhysics`) and CellBsp extension methods; no edits |
| Client/World/AcCoordinates.cs | lead | L4 only |
| Client/Interiors/InteriorLocations.cs | state | ST12 Academy fix |
| Client/Effects/ActorPartDraws.cs | lead | avatars' per-part chains already fit the constructor at :42-57; `DegradesDisabled` added in W2-pre(a) |
| Client/Objects/LivingStatics.cs | nobody | unchanged in M2: statics stay silent |
| Client/Options/OptionCatalog.cs, OptionRegistry.cs, OptionSpec.cs | lead | each track edits only its own partial and parser |
| Game shared files (AcToGodot, GameServices, Main, GameOptions, FlyCamera, SkyView/Renderer, InteriorController/Layer, ObjectsSystem, LandblockStreamer, TeleportPanel, PerfOverlay, EngineChecks, RenderResources, MaterialFactory, SurfaceLighting*, SurfaceShaders, ActorMeshLibrary, project.godot) | lead | edited only in W2-pre(a/b/c) |
| tools/smoke.ps1, godot.ps1, lib/Toolchain.psm1, play.ps1; LegalNotices.cs + README.md | lead | LegalNotices and README change in lockstep (LegalNoticesTests) |
| server/** | server mod | |

Out-of-path edits the wave-1 tracks made, which master now holds:

- `Data/Models/GfxObjMeshBuilder.cs` (avatars): its one call of the `GfxObjMesh` constructor passes the new `SurfaceIds`; nothing else in the builder changed.
- `tests/Lifestone.Loopback.Tests/Harness/HeadlessHostStubTests.cs` (state) is deleted. It asserted the stub's TODO and exit code 2, which the real `Program` replaces; its `--help` assertion moved to `tests/Lifestone.Loopback.Tests/Session/HeadlessProgramTests.cs`.
- `tests/Lifestone.Client.Tests/Options/OptionCatalogTests.cs` (state): the unknown-option sample is `--acount`, because `--account` is now registered.
- The server mod: `tests/Lifestone.ServerMod.Tests` compile-links `server\mod\Lifestone.ServerMod\Core\**\*.cs` (its own path), and `server/mod/Lifestone.ServerMod/Lifestone.ServerMod.csproj`, outside the solution, gained one `DefaultItemExcludes` property.

### 2.5 Wave-0 gate

- Build with 0 warnings.
- `dotnet test` green: 1,270 plus the new tests; loopback tests skip with a reason.
- Hygiene tests green.
- Smoke 13/13.
- The secret-scan line reports `secrets=0`.
- `godot.ps1 -Offscreen` runs engine-checks 9/9 with Dummy audio (this verifies that Godot 4.7.2 accepts the driver).

### 2.6 Lead steps after wave 0

- **W2-pre(a), (b) and (c):** the shared Godot and Client edits before each wave-2 fork (§5.1).
- **L18 `tools\play.ps1`** (DoD 1, D13), after the server-mod merge and G6's `--play`:
  - refuses unless `server/versions.json` names 127.0.0.1;
  - starts the backend when ACE is not ready (`start-server.ps1` returns after `Lifestone mod ready`);
  - runs the client through `godot.ps1 -Run` with `--play` (and `--account=NAME`, a name only);
  - stops only what it started (`stop-server.ps1 -AceOnly` when MariaDB was already up; `-KeepServer` keeps both);
  - never puts a password on a command line (godot.ps1 echoes its arguments). Agents always pass `-Offscreen -IsolateUserData -TimeoutSeconds`.
- **L18 as built (G6 stage 1):** `tools\play.ps1` with `tools\lib\Play.psm1` (the decisions, tested without a backend in `PlayScriptTests`). It also refuses `--account`, `--character`, `--play` and `--server` inside `-GameArgs`, makes `-Offscreen` imply the test account (and refuse any other), and has `-PlanOnly`. `tools\godot.ps1` splits `-GameArgs` on whitespace except inside double quotes (`Split-LifestoneArguments`), so `--character="Lifestone Smoke"` works.
- **L19 smoke steps:** `server-mod`, `portal-space-checks`, `portal-space`, `retail-ui` and `login`, each added in the merge that brings its harness (§6.2 item 12); the verdict lines of §4.14 are frozen with them.

---

## 3. Wave 1 tracks

### 3.1 Overview

| Track | Owns | Delivers | Depends on | Size (prod/test lines, agent-days) |
|---|---|---|---|---|
| **net** | src/Lifestone.Net/**; tests/Lifestone.Net.Tests/**; tests/Shared/Net/AceWireWriters.cs; Data/Animation/MotionCommandTable.cs (+test); Loopback.Tests/Net/; docs/third-party/net.md | OpenAC transport vendored, with defects fixed; AcSession; §5 codecs plus the chat, event and self codecs; LoopbackPolicy; FakeAce; fuzzing; soak; live phases A and B | W0a.1; C-mod and server-mod S8 before any live test | 10.4k / 8.5k, 15 |
| **state** | src/Lifestone.Client/Session/**; src/Lifestone.Headless/** (except csproj); Loopback.Tests/Session/; docs/session.md, docs/third-party/session.md; Client/Interiors/InteriorLocations.cs fix | A0: reducer (SmartBox semantics), bus with raw tap, Action API, IMovementSink impl, PlayerTools, recorder and replayer, gate wrapper, SessionBootstrap, Offline + stand-ins, headless host, scenarios | W0a.1; C-net; C-mod; C-portal (bridge); C-physics (ST16) | 7.2k / 4.6k, 11 |
| **portal** | Data/Audio/; Client/PortalSpace/; Client/Audio/; Client/Streaming/WorldAttachments.cs (after the fork); docs/portal-space.md | UIASSET assets, Wave/SoundTable, AnimLevel/zoom/lens, CRT random, tunnel spin, TeleportProgress, state machine, readiness, sound rules + ISoundSink, director, ExploreTransition, HiddenTransition, PortalSessionBridge | W0a.1; C-state (PS11 bridge only) | 3.2k / 3.4k, 6.5 |
| **avatars** | Data/Appearance/; Data/Motion/ (after L4); Client/Avatars/; docs/avatars.md | ObjDesc and part appearance, clothing and CharGen composition, MotionInterpreter (CMotionInterp), position managers, MoveTo/Sticky, AvatarSet applying classified events, parenting and hidden, player seam, draw output and stress source, CreaturePreview (CharGen's 3D view) | W0a.1; C-net (MotionCommandTable); C-state (events); retail-ui R8 (CharGenData; can start on DRW CharGen) | 6.0k / 4.0k, 15 |
| **physics** | Data/Physics/; Client/Movement/ (incl. Routes/); tools/physics-oracle (if D22); docs/physics.md | primitives, physics BSP, CTransition, cells and PhysicsWorld, PhysicsObj, player controller and cadence, ServerPositionPolicy, JumpCharge, CollisionStreamer, OfflineWalker, IGroundProbe, routes, MovementAudit | W0a.1; C-avatars (PH6); C-state (PH7); C-net (MotionCommandTable) | 11k / 4.5k, 15 |
| **retail UI (core)**, two sub-owners: UI-a for Data, UI-b for the runtime | Data/Ui/; Data/CharGen/; Client/RetailUi/{Core,Rendering,Text,Widgets,Input,Dialogs,Flow,CharGen}; docs/retail-ui.md | §5 desc model, resolver and anchoring, master properties, fonts, strings, cursors, keymap, CharGenData, Lifestone layout reader, element runtime, the software compositor (oracle and production), text, widgets, input, dialogs, flow, CharGenState | W0a.1 (L2, L3); C-state (R16 architecture test; R18a intent type) | 14k / 5k, 18 (about 10 each with two owners) |
| **server mod** | server/** (mod, scripts, lib, templates, README); tests/Lifestone.ServerMod.Tests/**; Loopback.Tests/ServerMod/; docs/third-party/server-mod.md | the `@lifestone` mod (status, where, place poi/loc, xp, net, places), auto-promotion off, readiness line, pin check, setup build/install, sentinel Admin, secrets v2, start-server readiness, backend state | L0/L1; C-net (LoopbackPolicy file) | 2.2k / 1.0k, 5 |

### 3.2 Checkpoints (merged to master as soon as they are ready; consumers rebase)

| Checkpoint | When (estimate) | Content | Gate |
|---|---|---|---|
| C-net | day 2 | N0 contract surface plus `MotionCommandTable` API and data plus `LoopbackPolicy` | build, tests, consumer review by state, avatars, server mod and physics |
| C-state | day 3 | ST0 contract files (§4.7) adapted to C-net | review by portal, physics, retail UI, avatars |
| C-portal | day 3-4 | interface files: `PortalSpaceMachine`, `PortalFrame`, `TeleportProgress`, `PortalSpaceDirector`, `ISoundSink`, `IAudioOutput`, `IEffectEventSink`, `HiddenTransition` signatures | review by avatars, state, wave-2 planners |
| C-mod | day 3-4 | ToolsWire (SM1), SM2 skeleton, `ace.modReady`, secrets v2 schema (SM5) | live install (SM8) before any login |
| C-avatars | day 8-9 | AV4-AV6: motion types, `MotionInterpreter`, `IMotionInterpHost`, `IMotionWeenie`, position managers, `IPlayerBody` | review by physics |
| C-physics | day 4 (API sketch) | `IPlayerMovementIntents`, `CollisionStreamer.StateOf`, `IGroundProbe`, `RouteDriver`/`MovementAudit` signatures | review by state, avatars |
| C-ui | day 12-14 | R10-R12: element runtime, `IUiPainter`/`IUiRegionView`/`UiCompositor`, text | review by the wave-2 screen planners |
| C-state-offline | day 9-10 | ST7b: `GameSession.Offline` + `StandInSessions` | review by G2 |

### 3.3 Per-track steps

Format: **ID Title** — files; reuse; ports; tests.

#### net

- **N0 Contract (C-net).**
  - Files:
    - `Session/{IAcSession, AcSession (loud stubs), AcSessionOptions, SessionTimeouts, LoginRetryPolicy, SessionPhase, SessionEvent, SessionFailure, AcCredentials, DddIterations, OutboundTicket, PumpResult}`;
    - `Dispatch/{IInboundMessageHandler, IAcSessionSink, InboundContext, InboundDispatcher, DispatchResult}`;
    - `Taps/IRawMessageTap`;
    - `Diagnostics/{INetLog, LinkStats}`;
    - `Sockets/{IDatagramSocket, LoopbackPolicy (incl. IsPinnedConfigHost), LoopbackRequiredException}`;
    - `Types/*` (AcPosition, ObjSeqs, PhysicsTimestamps, PhysicsState, ModelData, SubPaletteSwap, TextureChange, AnimPartChange, PhysicsDesc, WeenieDesc, ServerMotionState, MotionItem, `RawMotionStateWire`, `RawMotionAction`, MessageQueue, MessageDirection, GameEventHeader);
    - `Messages/**/*Msg.cs` shells;
    - `Data/Animation/MotionCommandTable.cs` (API plus a first full table, verified in N9).
  - Behaviour: unimplemented operations throw `NotSupportedException("TODO(net): …")`. `AcSession.Create` runs `LoopbackPolicy.Require` before the socket factory is touched. The session is affine to the thread that first pumps it.
  - Tests:
    - LoopbackPolicyTests (S8): accepts 127.0.0.1, 127.5.5.5, ::1, ::ffff:127.0.0.1; refuses the IPv4 and IPv6 unspecified addresses, a private-range address, a public address, DnsEndPoint("localhost"), a list that starts with 127.0.0.1 and names a private-range address, and "" (the test file holds the literals; this doc does not);
    - `IsPinnedConfigHost("127.0.0.1")` only;
    - a throwing socket factory is never invoked for a private-range endpoint;
    - CredentialsTests (ToString is redacted; Dispose zeroes; non-ASCII or over-50-character accounts are refused).
- **N1 Vendor OpenAC transport, unchanged.**
  - Files: Crypto/{Hash32, IsaacRandom}; Packets/{PacketHeader, PacketHeaderFlags, PacketCodec (borrowed path), BorrowedPacket, MessageFragment(Header), FragmentAssembler, PacketWriter, TransportDisconnect, GameMessageFragment}; Transport/{ReliableTransport, OutboundFlowQueue, InboundSequenceTracker, AckNakScheduler, SentPacketStore, SequenceMath, TransportClock, TransportStats, RetailPacketLossAverager}; `THIRD-PARTY-NOTICES.md`.
  - Ports: `eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/...` (MIT, with header). Each file is read before copying.
  - Tests: the 16 OpenAC test files that have no ACE-model dependency. AceSessionModel, AceCryptoModel and FakeAceTransport are excluded (protocol.md §8.2). Also the §10 Hash32/ISAAC vectors (#256-#259), the 24-byte ack-only packet, and encrypted LoginComplete (seed 0x0BADF00D → key 0x99D25AF6).
- **N2 Hygiene and defects 3/4.**
  - TimeProvider replaces Stopwatch (FragmentAssembler.cs:12).
  - EchoResponse and the NetError flags are parsed in bit order (PacketCodec.cs:181-380).
  - CP1252 through `CodePagesEncodingProvider.Instance.GetEncoding(1252)`, with an ASCII guard on fields that other fields follow.
  - String32L writes its real length byte.
  - `LoginRequest.Write(Span<byte>, …)` writes into a pooled buffer that is zeroed after use.
  - Store-then-send (OutboundFlowQueue.cs:133-145).
  - `UdpLoopbackSocket` binds 127.0.0.1:0 (NetClient.cs:19 binds Any) and filters source ports to Port and Port+1.
  - `WireReader`.
  - Tests: OptionalHeaderTests, StringEncodingTests, LoginRequestTests (zeroed pool), UdpLoopbackSocketTests (two local sockets), store-then-send, `Hygiene/SourceRulesTests`.
- **N3 Login codecs and dispatcher.**
  - Codecs: F658 (account string kept; the deletion field is passed raw), F7E1, F643, F659, F655 (request + ack), F7D9, DDD (F7E5/F7E6, with HiFi only when product bit 0x4), F7EA, F7E7, F7DC (optional reason), F7C1, F7DF, F746, F7B0/F7B1 envelopes, F7E0.
  - Ports: OpenAC `Messages/{CharacterList, …}`.
  - Tests: the §10 F658 vector; a round trip against AceWireWriters for every parser; "every byte consumed"; fuzzing with truncation and 10k flips (1M at Tier=Soak).
- **N4 FakeAce.**
  - Files: `FakeAce/{FakeAceServer, FakeAceSession, FakeAceScript, AceCryptoSystemOracle, ClientPacketParser, FakeAceCounters}`, `Support/{InMemoryNetwork, LossModel, SessionHarness}`, and `tests/Shared/Net/AceWireWriters.cs` (BCL `BinaryWriter`, public to all test projects).
  - Uses tests/Shared `ManualTimeProvider`. OpenAC's `VirtualClock` is used only by the ported transport tests.
  - Ports, test-only: `ACEmulator/ACE@95ef426d ACE.Common/Cryptography/CryptoSystem.cs` (AGPL, D20); OpenAC PacketHeaderOptional/Packet/TryDecode as the fake's independent parser.
  - Tests: FakeAceConformanceTests (DB-delay race, first data packet 2, a 1-gap without NAK, a 2-gap NAKed, a 257-gap terminates, reject consumes a sequence, duplicate → CRC count, silent 2 s after Terminate).
- **N5 AcSession, part 1 (to CharacterSelect).**
  - Handshake: 200 ms, resent every 0.333 s, 10 s cap; a fresh socket per retry; LoginRequest never resent from the same endpoint.
  - DataCheck events. CharacterSelect requires F7EA and F658.
  - Retry policy: 0x01, 0x0D, 0x15 and a lost ConnectRequest, 3 retries (4 attempts) 3 s apart (D18; `LoginRetryPolicy.Default.Attempts` is 4, and `RetryTests` and `HandshakeTests` pin it).
  - Silence watchdog 20 s, or 140 s with `--retail-net`; it fires only if Pump ran within the window.
  - `InboundSequencer`, global by fragment sequence, with the gap-skip rule.
  - `Pump(budget)` always runs the transport sweeps.
  - Tests: HandshakeTests, LoginFailureTests, RetryTests, DataCheckTests, WatchdogTests, SequencerTests, PumpBudgetTests, SentinelPasswordTests.
- **N6 AcSession, part 2.**
  - Create (correlates F643; folds a duplicate NameInUse).
  - Delete by index (range-checked).
  - Restore, EnterWorld (F7C8 → F7DF → F657), LoginComplete (never automatic), LogOff (F653 → F653 + F658), Quit (F653, then cleartext Disconnect).
  - Tests: CharacterOpsTests, EnterWorldTests, LogoffQuitTests.
- **N7 World, movement, chat, event codecs; defects 1 and 2.**
  - F745/F7DB with the raw quaternion; F747, F748, F74C (align before the sticky GUID); F74E, F74B, F751, F749, F74A, F755, F750, F625.
  - MoveToState with 8-byte actions, AutonomousPosition, Jump (56 bytes), Use 0x0036, QueryHealth 0x01BF, Talk 0x0015, Tell 0x005D.
  - HearSpeech, HearRangedSpeech, EmoteText, SoulEmote.
  - Game events: Tell 0x02BD, PopupString 0x0004, WeenieError 0x028A/0x028B, TransientString 0x02EB, UseDone 0x01C7, UpdateHealth 0x01C0.
  - Tests: the §10 F748 and MoveToState vectors; `AceReaderMirror`; UpdateMotion odd-u16 alignment (OpenAC UpdateMotionTests.cs:533-600 inverted); CreateObject per-flag round trips; AllocationTests (0 bytes per UpdatePosition dispatch).
- **N8 Robustness.**
  - Tail probe (resend ack+1 once after 1.0 s, based on the raw server ack), with the acks it holds while a sent packet is unacked; `TailProbe=false` turns both off, and `--retail-net` sets it (D18).
  - `LossyDatagramSocket`, seeded.
  - Tests: TailProbeTests, RejectShiftTests, VirtualSoakTests (2 min, 5 seeds by default; 10 min × 100 seeds at Tier=Soak; CRC count equals duplicate deliveries); DatagramFuzzTests; SessionFuzzTests. Tier=Soak holds the 5M-datagram fuzz, the 1M body flips and the 10 min × 100 seed soak (the first draft of this plan called that tier Exhaustive).
- **N9 MotionCommandTable complete.**
  - 412 entries from the DRW 2.1.9 `MotionCommand` enum, reconciled with ACE `MotionCommand.cs` and the 2015 insertions.
  - Tests: low word equals index; the pinned values (AFKState 0x4300011B, WalkForward 0x45000005, AI_TelegraphCast 0x1000019B…); the exe TSV check from `tools/protocol/dump_command_table.py` into `cache/protocol/` (skipped when absent).
- **N10 Self codecs (D16).**
  - PlayerDescription 0x0013 and PrivateUpdate{Vital, Attribute, Attribute2ndLevel, Skill, PropertyInt, PropertyInt64}, decoupled from AcDream.Core.Items.
  - Ports: OpenAC PlayerDescriptionParser and the others (MIT).
  - Tests: round trips, fuzzing.
- **N11 Live phase A** (`tests/Lifestone.Loopback.Tests/Net/`, accounts.test).
  - **Written and run (G6 stage 2).** `LivePhaseATests` (the status test first, then the whole session life), `LiveNetClient`, `NetFixtureStore`, `NetFixtureTests` and `RequiresNetFixturesFact`, in `tests/Lifestone.Loopback.Tests/Net/`; see that folder's README. The plan's "immediate re-login without 0x01" is not what ACE does: it keeps a quit session for 2 s and answers 0x01 meanwhile, so the test asserts one 0x01 and one retry for the immediate login and none after the 2 s (docs/protocol.md section 4.4). `<CacheKey>` is `ace-<first 12 characters of the pinned ACE commit>-client1802`. A failing status test calls `LoopbackRun.Abort`, which skips every loopback test that has not started.
  - Login, create if needed (Aluvian, 55 SACs), enter (cell 0x8602), LoginComplete unhides, logoff, Quit, then an immediate re-login without 0x01.
  - Golden capture from the client tap into `cache/net-fixtures/<CacheKey>/`, refusing any body that contains password bytes.
  - `[RequiresNetFixturesFact]` reparse tests.
- **N12 Live phase B.**
  - **Written and run (5b slice b1).** `LivePhaseBTests` in `tests/Lifestone.Loopback.Tests/Net/` (see that folder's README), on the `Lifestone Loopback` character. The one additive csproj line was `<Compile Include="..\Lifestone.Net.Tests\Support\LossyDatagramSocket.cs;..\Lifestone.Net.Tests\Support\LossModel.cs" LinkBase="Net\Lossy" />`. Steps 10 and 11 (about 7 s and 25 s) run in the plain loopback run; step 12 (about 65 s) is the oracle's positive control; step 20 is the Soak (about 2.6 min). Every test ends with the character back at its Academy start.
  - **What the live runs showed (ACE @95ef426d).** `place poi "Holtburg"` brings one F751 (teleport sequence 1) and one F748 at cell 0xA9B40019 (84.00, 7.10, 94.00), then 54 or so CreateObjects; the server stays `Teleporting` until LoginComplete. Walking 47 m on the retail cadence (a MoveToState, then an AutonomousPosition about every second, 15 in 15 s, and at once on each cell or triangle change) gives a `where` exactly on the last position sent (0.00 m) at every check, and ACE echoes each message as an F748 for the player (17 echoes for 17 messages, each exactly a position sent); the force-position sequence stays 0 and no further F751 arrives. Over the 5%/2% link (327 m, 524 AutonomousPosition + 12 MoveToState in 2 minutes) the session never failed, the client counted 0 checksum failures and 0 gap skips (27 resends, 47 NAKs sent, 23 received), `where` was again exact, and ACE's bad-checksum count and the link's duplicate deliveries were both 0. That equality is vacuous (a drop-and-reorder link made no duplicate delivery, as docs/protocol.md section 9.4 step 11 predicts), so step 12 runs the same walk for 30 s over a link that only duplicates (3% each way): ACE counted 6 bad checksums, the link made 6 duplicate encrypted deliveries, and the session carried on (ISAAC stayed in step; the client dropped 13 duplicate server packets).
  - `@lifestone place poi "Holtburg"` → F751 + F748 in 0xA9B4.
  - A kinematic AutonomousPosition walker over `TerrainSampler` (test-only).
  - `@lifestone where` within 0.5 m; the force-position sequence never advances.
  - The 2-minute 5%/2% soak (Tier=Soak). The oracle is the `@lifestone net` CRC delta equal to the socket's duplicate-delivery count.
- **N13 Docs.** `docs/third-party/net.md`, THIRD-PARTY-NOTICES.md, and drafts of the protocol.md corrections and the LegalNotices/README line (OpenAC: "terrain blending code (TexMerge); network transport and message codecs"). Applied in the end-of-wave-1 doc pass: protocol.md drafts A-I, and the OpenAC line in `LegalNotices.cs` and `README.md`.

#### state

- **ST0 Contract freeze (C-state).**
  - `docs/session.md` plus `Client/Session/Contracts/*` as sketched in §4.7.
  - Adapts to C-net names: `IInboundMessageHandler`, `InboundDispatcher`, `Login`, `Pump(TimeSpan)`, `DeleteCharacter(int)`, `Quit`, `AcCredentials(string, ReadOnlySpan<char>)`.
  - Threading model: one game thread; continuations complete inside `Tick`.
- **ST1 Clock.** `SessionClock`, `GameThreadSynchronizationContext`, `NoAmbientTimeTests`. Uses tests/Shared `ManualTimeProvider`.
- **ST2 Gate wrapper.** `LoopbackGate` logs one WARNING with the runner name and the reason, then delegates to `LoopbackPolicy`. Tests: the S8 theory over GameSession.Create, HeadlessRunner, PlayerTools and Automation-source actions, with a factory spy that is never invoked.
- **ST3 EventBus with raw tap.** Allocation-free struct events; publishes from a handler are queued FIFO; handler exceptions are logged and counted.
- **ST4 ObjectMessageGate** on `Sequence16` (L4).
- **ST5 Credentials and options.**
  - `PlayAccounts.Parse(ReadOnlySpan<byte>, AccountChoice)` handles v1 and v2 secrets via `Utf8JsonReader.CopyString` into a pooled `char[]`.
  - `ServerRuntimePaths` on `CheckoutLocator`.
  - `BackendReadiness.ModReady(pidsJson)`.
  - `SessionOptions` parser for `OptionCatalog.Session.cs`.
  - Tests: v1/v2; a sentinel password never appears in errors; the server must be an IP literal defaulting to versions.json.
- **ST6 Recording `.lsrec`.** Writer and reader; the C→S body allowlist; truncation tolerance.
- **ST7 Reducer, login phase.**
- **ST7b Offline sessions (C-state-offline).** `GameSession.Offline` + `StandInSessions`, using Lifestone-authored names only.
- **ST8 Reducer, world phase. Sole owner of SmartBox semantics.**
  - Re-expressed from SmartBox.c (11.4186): HandleCreateObject 0x00454C80, HandleUpdateObject 0x00455580 (forced recreate), HandleDeleteObject 0x00451EA0 (self ignored), HandleReceivedPosition 0x00453FD0, ProcessObjectNetBlobs 0x00454B20.
  - Behaviour:
    - parking with bounds 32/guid, 4,096 total, 60 s;
    - verdicts ForceBlip, SelfTeleport, SelfEcho or Remote, with a TeleportAdvanced flag;
    - `SelfTravel`;
    - chat; ToolsWire reply routing;
    - `Self.Qualities`, UpdateHealth, `LinkStats`.
  - Tests: verdict tables including wrap; parked messages replayed in order; 1,000 updates allocate 0 bytes.
- **ST9 Action API core.** Handles, queue, rate limits, KillSwitch, DryRun, ActionLog ring of 1,024, NotImplemented loud stubs. The limits are the documented defaults of D49 (docs/session.md §7); ST9 to ST12 added two the decision did not list: QueryHealth 4 a second and automation's movement intents 20 a second.
- **ST10 Trackers.**
  - Login, Create (maps `CharacterCreateIntent` to net `CharacterCreateRequest`; exactly 55 skills; ASCII name), Delete (index), Restore, EnterWorld, LoginComplete (once per arrival), Use (UseDone), QueryHealth, Talk, Tell, Quit.
  - LogOff is the **sole F653 sender**: it adds "Logging off..." to chat and publishes `LogOffRequested(now, pkDelay=false)`.
- **ST11 MovementChannel** (implements `IMovementSink`, stamping `ObjSeqs` from `Self.Stamps`; logs once if sending while not Present) and the `MoveIntents` bridge to physics `IPlayerMovementIntents`.
- **ST12 PlayerTools.**
  - `PlaceRequests` (ACE rows → `place poi "<poiNames[0]>"`; curated → `place loc`), `AceLocText`, `PositionAgreement`, status probe (an access above Player is an ERROR).
  - Fix `InteriorLocations.TrainingAcademy` to 0x860201AD and update its tests.
- **ST13 GameSession facade and SessionBootstrap.**
  - `SessionBootstrap.CreateLive(options, secretsBytes, pidsJsonBytes, DatSet, TimeProvider, ILogSink)` is shared by Headless and Godot. It checks modReady and loopback, calls `RequireLocal`, and maps iterations.
  - `Reconnect`, `GameStateDigest`. `QualitiesWeenie : IPhysicsWeenie` moved to ST16 (docs/session.md §12) and is built there (b2, §24); ST13 is done.
  - Tests: SessionBoundaryTests; PasswordHygieneTests (sentinel absent from recordings, logs, events and exceptions; the `char[]` zeroed after CharacterSelect).
- **ST14 Replay.** `SessionReplayer`, `ReplayAcSession`, synthetic recordings via `AceWireWriters`, `[RequiresRecordingFact]` over `cache/sessions`.
- **ST15 Headless host.**
  - `HeadlessRunner`, `LoginScenario`, `DataOnlyAttachments`, `PortalSessionBridge` + `TimedTunnel` for the LoginComplete timing.
  - `Program` (I/O only here).
  - Loopback tests in `Loopback.Tests/Session/`: login, enter and leave; the recording contains no password (count only).
- **ST16 Scenarios** (after physics merges). `HoltburgLapsScenario` and `AcademyExitScenario` call physics `RouteLibrary`/`RouteDriver`/`MovementAudit`. Loopback tests check zero corrections and where-am-I within 0.5 m.
  - **As built (5b b2, docs/session.md §24):** `QualitiesWeenie`, `RoutePlay` (placing, teleporting, settling, walking, verdict, returning home), `HoltburgLapsScenario` (`LoginScenario`'s new `play` stage) and `--scenario=holtburg-laps` are done; live: laps x3, corrections=0, where_max_m 0.01. The game's walker still has the placeholder weenie (b4).
  - **As built (5b b3, docs/session.md §25):** `AcademyExitScenario`, `PortalExit`, `SessionTargetWorld`, the portal search, Use, the server MoveTo given to the walker and the F751 exit are done and unit-tested against the scripted server. The Academy start and the exit portal's wing are not connected, so the live scenario is sent to the wing with `place loc` (`PortalExit.WingStart`). Live it walks there, uses "Exit to Holtburg" from range, follows the server's MoveTo (0.00 m from the server's position) and corrections=0. ACE gates that portal behind the Sentry's quest, so the scenario asks the mod (`@lifestone quest has`), stamps `SentryTaskComplete` when it is missing, takes the portal (F751 to Holtburg) and erases the quest again only if it stamped it (5b slice C, `ExitQuestMode.Stamp`; `TODO(st16-exit-quest)` closed); the refusal (WeenieError 0x0474) is its own live test with `ExitQuestMode.Erase`.
  - `QualitiesWeenie : IPhysicsWeenie` (built in ST16, not ST13): the player's weenie read from `Self.Qualities`. Its interface (`IMotionWeenie`, now with `IsThePlayer`, `TryGetJumpVelocity` and `TryGetJumpStaminaCost`) and physics' `IPhysicsWeenie` are on master. Until it exists nothing binds the player's run rate, jump and burden from the session, and physics' PH7, G5 and G6 wait for it.
  - It switches `HeadlessCharacter.SkillClasses` to the retail UI's `CharGenState.ResetSkillLevels` and `CharGenData` skill costs (on master now) and deletes its copy.
- **ST17 Docs.** `docs/session.md`, `docs/third-party/session.md` (functions re-expressed with addresses; no third-party code).

#### portal

- **PS1 UI assets.** `UiAssetIds` and `PortalSpaceAssets.Resolve(EnumIdMaps)` in `Client/PortalSpace/` or `Data/Audio/`. Uses L3; the Internal-fallback test moved to L3.
- **PS2 Asset conformance** (real dats).
  - Setup 0x02000306 has 2 parts, both GfxObj 0x0100080B.
  - The GfxObj has 352 vertices and 360 polygons; surfaces 0x08000C31 and 0x08000C32 match §4.2.
  - Animation 0x030005AC: 120 frames, part origins at (0,0,51), 1.5° per frame about −X, one SoundTweaked hook on frame 2 (0x0A000316, volume 0.3).
  - The camera sits inside the tube.
- **PS3 `AnimLevel`, `PortalZoom`, `PortalLens`, `PortalSpaceScene`.** Re-expresses UIGlobals::Init 0x004EF100, GetAnimLevel 0x004EF1D0, Render::set_vdst 0x0054BE50 and gmSmartBoxUI::PostInit 0x004D77C0.
- **PS4 `MsvcCrtRandom`** (the public MSVC LCG) and `RandDouble`. Retail's endpoints are inclusive.
- **PS5 `TunnelSpin` / `TunnelObject` / `TimedTunnel`** (CPhysicsObj::set_sequence_animation 0x005101C0). Tests: frames 1..119; one hum per 2.975 s loop; 0 bytes allocated per steady step.
- **PS6 `TeleportProgress`** (SmartBox flags; uses `Sequence16.NotOlder`).
- **PS7 `PortalSpaceMachine` and `LogOffSchedule`.**
  - gmSmartBoxUI::UseTime 0x004D7A90.
  - A single `ExitWindow` constant (68-76, D27).
  - `LogOffTunnelPolicy` (D28).
  - `LogOffSchedule` holds timing only; the network send is state's.
  - Tests: login, teleport and logoff timelines; a 2.0 s hold; the window pinned at frames 67/68/76/77; the 5 s cap; LoginComplete exactly once; determinism; 0 bytes allocated.
- **PS8 `DestinationReadiness.Evaluate`** over L7's `IWorldAttachments`, with `ReadinessPolicy.OutdoorRingForSeenOutsideCells` (D29). Failed and Disabled count as settled. Allocation-free.
- **PS9 Wave/SoundTable raw decoders and `DatAudioSource`.** Census at Tier=Exhaustive: 786 waves (785 PCM, one MP3 0x0A000393) and 190 tables, none with grandchildren.
- **PS10 Sound rules and seams.** `RetailSoundRules`, `VoicePool`, `RetailSoundPlayer`, **`ISoundSink`** (façade), `IAudioOutput`, `SoundHookRouter`, `IEffectEventSink`, `AudioSettings`.
- **PS11 `PortalSpaceDirector`, `ExploreTransition`, `HiddenTransition`, `PortalSessionBridge`.**
  - The bridge maps state events to `TeleportProgress`: self create, F751, self teleport position, self state.
  - It calls `IActionApi.LoginComplete` on `SendLoginComplete` and forwards `LogOffRequested` to `machine.RequestLogOff`.
  - Tests: the frame-order test (an already-ready destination still plays the whole sequence); 10,000 steps allocate 0 bytes.
- **PS12 Docs.** `docs/portal-space.md` (including the wave-2 integration contract) and `docs/third-party/portal-space.md`.

#### avatars

- **AV0 Design doc and provenance list.** Address list submitted to L10.
- **AV1 ObjDesc** (`Data/Appearance/ObjDesc.cs`).
  - ObjDesc::UnPack 0x005ABCF0; Add* de-duplication; 255 cap.
  - A test-only `AceObjDescOracle` ported from ACE `Creature_Networking.cs` (AGPL, with header).
- **AV2 `PartArrayAppearance`.**
  - `DoObjDescChanges` 0x00519210 / FromDefault 0x00519480; SetTextureMap 0x0050DE30; DetermineBasePal 0x0050DC80.
  - Adds `GfxObjMesh.SurfaceIds` and `TextureLoader.GetOriginalPaletteId` (additive).
- **AV3 `ClothingObjDesc.Build` and `CharGenAppearance.Compose`** (ClothingTable::BuildObjDesc 0x005A7900; gmCG3DView::Update 0x004EE9D0). The heritage fallback constants stay loud TODOs until read in L10/D4.
- **AV4 Motion types.** Extends the L4 partials; `WireMotion.TryToInterpreted` via `MotionCommandTable`.
- **AV5 `MotionInterpreter`** (CMotionInterp, re-expressed from 11.4186 CMotionInterp.c) and `MovementSystem`.
  - Defines `IMotionInterpHost` and `IMotionWeenie`.
  - Includes the 11.6096 jump-gating set.
  - A test-only `AceMotionInterpOracle`.
- **AV6 Position, Interpolation and Constraint managers.**
  - Retail adjust_offset 0x00555D30, which overwrites the offset. The constraint offset accumulates.
  - ACE's InterpolationManager is not ported (InterpolationManager.cs:161-166 is a no-op). OpenAC is used for a reading cross-check only.
  - C-avatars = AV4-AV6.
- **AV7 `AvatarBody`.** ObjectClock stepping; Grounded-flag contact; an optional `IGroundProbe` from physics (D24).
- **AV8 `AvatarSet`.**
  - Applies state's **classified** events: create/recreate/delete, MoveOrTeleport on Remote verdicts (96 m snap, blip distances), motion, vector, ObjDesc.
  - It has no queue and does not re-gate stamps.
- **AV9 Children, parenting and hidden.** HoldingLocations; `HiddenTransition` from portal; census of the 0x3300032F/0x33000331 hooks (input to D45).
- **AV10 Player seam.** `PlayerAvatar`, `IPlayerBody` (implemented by physics), and `KinematicPlayerBody` as a test double.
- **AV11 Draw output and stress.** `AvatarFrame` (reused buffers, PoseVersion gating) and `AvatarStress.Generate(n, seed)`. Allocation test: 0 bytes for 60 humans plus 200 objects.
- **AV12 `CreaturePreview`**, CharGen's 3D view (gmCG3DView StartAnimation 0x004EE600 / StopAnimation 0x004EE640, light at 2.0, `PreviewAnimations.Resolve` with the UIASSET idle emotes). Character management has no 3D view (D35), so nothing stores appearances for the character list. CharGen's view is retail-exact (D54): the five UIASSET idle emotes are loaded as `gmCG3DView` loads them and none is played, so the character stands still (docs/avatars.md §13).
- **AV14 MoveTo and Sticky managers** (D17; CMoveToManager.c). A test-only ACE oracle.
- AV13, the session adapter, moves to G5.

#### physics

- **PH0 Decision table and docs.** Retail-first defaults, each with a test-only `PhysicsCompat` switch:
  - quantum 1/5 + 30 Hz gate;
  - check_walkable keeps a local backup;
  - no step cap;
  - the constraint accumulates;
  - interpolation is written back;
  - cross-landblock `add_cell_block`;
  - interval 1.0 s.

  MIN_JUMP_EXTENT comes from L10/D4.
- **PH1 Primitives.**
  - Ported from ACE `PhysicsGlobals`, `Sphere`, `CylSphere`, `Polygon`, `BBox`, `Ray`, the extensions and `Position` (structs).
  - `PhysPosition` is replaced by `CellFrame` (L4).
  - Tests: ported `SphereTests.cs` (250 lines); FloorZ; the PhysicsClock gate.
- **PH2 Physics BSP decode and census** (Tier=Exhaustive). The node-type histogram and reachable null children decide the rule. Uses `RawPhysicsBspReader` grown from RawModelReader.cs:273-330.
- **PH3 BSP queries and the CTransition machine** with a pool of 10. Synthetic tests: walls, steps, slopes at FloorZ±ε, edge slide, no tunnelling, 0 bytes allocated.
- **PH4 Physics cells and `PhysicsWorld`.**
  - Land cells come from `TerrainSampler`.
  - EnvCells, buildings, shadow registration, LandDefs from L4.
  - Tests:
    - Holtburg 0xA9B4 ±1: 576 cells settle at TerrainSampler height + radius;
    - doorways lead to OtherCellId;
    - Academy portals;
    - seams;
    - water blocks;
    - straddling statics collide.
- **PH5 `PhysicsObj`.** update_object, SetPosition (Placement/Teleport), handle_all_collisions; implements `IMotionInterpHost`; `IPhysicsWeenie : IMotionWeenie` adds IsImpenetrable.
- **PH6 Player motion integration (after C-avatars).**
  - PhysicsObj hosts avatars' `MotionInterpreter`.
  - Jump via `MovementSystem`.
  - `OfflineWalker` (human Setup 0x02000001, MotionTable 0x09000001).
- **PH7 Controller.**
  - `PlayerMovementController : IPlayerMovementIntents`.
  - Re-expresses CommandInterpreter::UseTime, ShouldSendPositionEvent and SendPositionEvent (11.4186 0x006B3BF0, 0x006B45E0, 0x006B4770).
  - `ServerPositionPolicy` acts on the reducer's player verdicts: blip keeping heading plus an immediate send; teleport placement; ConstrainTo (player 10/50 m outdoors, 5/20 m indoors per CPhysicsObj.c:327-371).
  - `JumpCharge` (ClientCombatSystem 0x0056ADE0/0x0056B110).
  - `CollisionStreamer` (3×3, or the cell's landblock and its 3×3 when seen outside; `PumpSynchronously` for headless; `StateOf → AttachState`).
  - Tests: the cadence matrix; ConstrainTo factor at loopback RTT; JumpCharge levels.
- **PH8 `IGroundProbe`** for remote grounded objects (D24); full transitions deferred.
- **PH9 Routes.** `ScriptedRoute`, `RouteDriver`, `CellRoutePlanner`, `RouteLibrary.HoltburgLaps` (12-point validated ring, D47) and `AcademyExit` (spawn 0x860201AD → exit portal), `MovementAudit` (decorates `IMovementSink`; consumes SelfEcho and ForceBlip). Offline route tests.
- **PH-O ACE oracle harness (D22).** `tools/physics-oracle` outside the solution; output to `cache/physics-oracle/`; `[Trait("Category","Oracle")]` tests.
- S10 MoveTo moves to avatars AV14.

#### retail UI (core)

UI-a takes R1-R9, R18a. UI-b takes R10-R16 and starts after R3.
- **R1 Desc model and census.**
  - Uses `DatSet.TryGetLocal` (L2); there is no separate `UiDatReader`.
  - `UiDescConverter` reinterprets StringInfo in retail order (StringInfo::Serialize 0x0042DEB0). A literal or has-strings value, or a null MediaDesc (`Stretch`), throws.
  - Census tests: 101 layouts, 2,162 elements, media and property types; a `RawLayoutWalker` cross-check.
- **R2 Layout catalogue and UILAYOUT chains.** Pins what 0x10000006/0x10000495 contains (input to D36). Dialog enum 2 → 0x2100003C.
- **R3 `UiDescResolver` and `UiAnchor`.** LayoutDesc::InqFullDesc 0x0069A520; StateDesc::UpdateSizeAndPosition 11.6096 0x0069CDB0 with float-stored width ratio. OpenAC UiLayoutPolicy is used as a cross-check only.
- **R4 Master property table and `UiPropertyIds`,** each pinned to its dat name.
- **R5 Fonts.** Font::GetCharDesc 0x00443360 ('?' fallback); GetCharWidthA byte arithmetic; font 0x40000000 has 1,050 glyphs, baseline 12.
- **R6 Strings.** `StringHash`, tables via group 4, StringInfo::GetString 11.6096 0x0042E980, and a meta-language subset (variables, escapes, StripMetaLetters). Any other construct is a loud TODO.
- **R7 Cursors, keymap, UI image census.**
  - UICURSOR has 41 entries.
  - The KEYMAP enum resolves to 0x14000000 "gmDefaultMap".
  - 624 images (623 resolve; the missing one is DID 0, an image entry that clears the element's image), 3 alpha masks, 30 animation frames and 5 layout cursors, all of which resolve; no UI image of any kind is in client_highres.dat (docs/retail-ui-data.md §7).
  - High-res duplicates are listed.
- **R8 `CharGenData`, `SkillTableData`** (`Data/CharGen/`). 13 heritages, 5 start areas, every chargen skill ID < 55. CharGen code lives in folders named CharGen, but its namespaces end in `.CharacterCreation`: no namespace under `Lifestone.Data` or `Lifestone.Client.RetailUi` may end in `.CharGen`, which would hide DatReaderWriter's `CharGen` record.
- **R9 `LifestoneLayoutReader`** (`Data/Ui/`). Layout files are owned by G7. Hygiene: IDs and literals only.
- **R10 Element runtime.**
  - `UiElement`, `UiElementManager`, `UiClassRegistry` (105 classes; unknown classes get a generic element with a loud log), `UiMediaMachine`, `UiRuntimeLayout`, `UiScalePolicy`, hit-testing.
  - First reads UIRegion::DrawChildren, MouseHitTester::Recurse and UIElementManager::DrawDirtyRegions to settle z-order.
  - Elements expose `IUiRegionView`.
- **R11 Software compositor** (the production path and the oracle).
  - `SoftwareUiPainter` (Graphic::Draw tiling, never stretching; blit modes 0/1/2 from UIRegion::SetImageByDID; LegacyBlit flags read first), `UiCompositor` (dirty rects, time-sliced per root, a root presented whole), `UiTileGrid` (128-pixel tiles).
  - Opt-in PNGs with `LIFESTONE_UI_FRAMES` into `cache/ui-frames`.
  - Tests: blit bytes, tiling offsets, order and clipping, slicing determinism, 0 bytes allocated for an idle caret.
- **R12 Text.** `GlyphLayout` (GlyphList::Recalculate) and `TextRasterizer` (SurfaceWindow::DrawCharacter: A8 tint, outline, selection), `UiTextElement` with editing, Name/Number filters, clipboard. C-ui = R10-R12.
- **R13 Widgets.** Button, Field, ListBox, Menu, Meter, Panel, ResizeBar, Scrollbar, Scrollable, Viewport, Dragbar, GroupBox, ColorPicker. Browser is a loud stub.
- **R14 Input.** `UiInputRouter` with the precedence modal, capture, text focus, element, input maps, unhandled. `DikScanCodes` plus a Godot-physical-keycode→DIK table as plain ints. `IInputActionSink` and `IWorldPointerSink` (pointer, mouse-look). `UiCursorController`.
- **R15 Dialogs, flow and notices.** DialogFactory (UILAYOUT 2 → 0x2100003C, type→element map); `UiFlow` (gmUIFlow::gmUIFlow 0x0047AA10 modes and error routing); `IUiNoticeSink` (type 26 → spew box); `NotInM2`.
- **R16 Architecture tests.** RetailUi uses only `IGameSessionView`/`IActionApi`; no Godot, no I/O, no `Lifestone.Net`.
- **R18a `CharGenState` and `CharGenVerifier`.** Produce state's `CharacterCreateIntent`. Tests: every heritage and gender verifies with 55 SACs and valid credits; a mirror of ACE PlayerFactory.cs:166-209, 606-631.

#### server mod

- **SM1 Shared wire contract.**
  - `server/mod/Shared/{ToolsWire, ToolRequests, ToolRequestParser, ToolReplies, ToolsWireJsonContext, PlaceRules}.cs`. BCL only; `LoopbackPolicy` is compile-linked from Net, not duplicated.
  - `server/mod/.editorconfig` sets CA1305, CA1304, CA1310 and CA1311 as errors.
  - Tests in ServerMod.Tests (which references Client, so there is no CS0436): round trips, rejection codes, the outdoor cell index, the worst-case `places` reply (56/62 names, at most 65,534 characters, ASCII), cultures de-DE/tr-TR/ar-SA/fr-FR.
- **SM2 Mod skeleton.**
  - csproj (net10.0, AnyCPU, TWAE, `NuGetAudit=false`, no PackageReference, ACE DLLs with `Private=false`, a build-time hash check).
  - `Meta.json` in PascalCase with `HotReload: false`.
  - `Mod.Initialize` refuses on: host not pinned, `DefaultAccessLevel != 0`, ACE DLL SHA mismatch, hot reload on.
  - `StartupWatch` polls `DatabaseManager.Shard != null`, clears the flag and logs; then waits for `WorldStatus == Open` and logs `Lifestone mod ready: ...`.
  - `[CommandHandler("lifestone", AccessLevel.Player, CommandHandlerFlag.None, true, -1, …)]`. Verbs `status` and `net`.
  - `server/build-mod.ps1` (compile only, output into cache).
  - ModSourceTests.
- **SM3 Verbs.** `where`, `place poi` (OrdinalIgnoreCase over the cache; null-destination guard; AdjustDungeon), `place loc` (cell validated against `CellDat.AllFiles`), `xp` (GrantXP; the reply is enqueued after the update), `places` (run on a Task). Port headers for the AdminCommands/DeveloperCommands call sequences.
- **SM4 Setup step.**
  - Build key = source hash + aceServerDllSha256.
  - Build with the setup `$dotnetEnv` + `--disable-build-servers`.
  - Assert the output holds no ACE/0Harmony/McMaster/log4net DLL; staging install.
  - Record in setup-state.
  - Account audit (names only). **Sentinel Admin account (D11)**, whose random password is discarded.
  - Add `--disable-build-servers` to setup-server.ps1:92,94.
  - InstalledModTests.
- **SM5 secrets.json schema 2.** `accounts.play` "lifestone" and `accounts.test` "lifestone-test"; v1 migration with protect-before-write; `-AddAccount`.
- **SM6 start/stop/state.**
  - Readiness = UDP binds + `Lifestone mod ready`; fail fast on mod-load patterns.
  - pids.json `ace.modReady`/`modVersion`/`modWire`/`modDllSha256`/`readyUtc`.
  - "Already running" requires modReady.
  - `stop-server.ps1 -AceOnly`; `Get-LsBackendState`; `Find-LsSecretLeaks`; `Test-LsServerModLive`.
- **SM7 Docs.** server/mod/README.md, server/README.md, the Config.template.js comment, docs/third-party/server-mod.md.
- **SM8 Live merge-gate check** (main checkout, lead's window). See §6. After a merge of m2/servermod that changes the mod's Core files, run `server\setup-server.ps1 -ModOnly` first: they change the mod's build key, and `start-server.ps1` refuses a stale install.
- **SM9 Loopback scenarios** (wave 3, `Loopback.Tests/ServerMod/`).

---

## 4. Cross-track contracts

### 4.1 Dat access and enum maps (lead L2/L3)

Consumers: portal, retail UI, avatars, the hosts.

```csharp
public sealed class DatSet {                     // existing API unchanged
  public bool HasLocal { get; }
  public bool TryGetLocal<T>(uint id, [NotNullWhen(true)] out T? v) where T : IDBObj, new(); // DatBinReader(bytes, Portal)
  public byte[]? TryGetLocalBytes(uint id);  public IReadOnlyList<uint> GetAllLocalIds<T>() where T : IDBObj;
  public void RequireLocal(string feature);   // InvalidOperationException naming client_local_English.dat
  public EnumIdMaps EnumMaps { get; }
}
public readonly record struct EnumGroup(uint Value) {
  public static readonly EnumGroup StringTables = new(4), UiLayout = new(5), UiCursor = new(6), UiAsset = new(7), Font = new(9);
}
public sealed class EnumIdMaps {                 // DBCache::GetDIDFromEnum 0x00413BF0
  public const uint MasterMapId = 0x25000000;
  public uint? GroupMapId(EnumGroup g);
  public bool TryResolve(EnumGroup g, uint enumValue, out uint did);   // ClientEnumToID, then ServerEnumToID (= EnumToIDInternal)
  public uint Resolve(EnumGroup g, uint enumValue);                    // throws, naming both
  public string? NameOf(EnumGroup g, uint enumValue);
  public IReadOnlyDictionary<uint, uint> Entries(EnumGroup g);
}
```

### 4.2 World and sync types (lead L4)

Consumers: everyone.

```csharp
public readonly record struct CellFrame(uint CellId, AcFrame Frame) {
  public static CellFrame FromWire(uint cell, Vector3 origin, float qw, float qx, float qy, float qz);
  public bool IsOutdoor { get; } public bool IsEnvCell { get; } public LandblockCoord Landblock { get; }
  public double HeadingDegrees { get; } public CellFrame Normalized();
}
public static class LandDefs { bool AdjustToOutside(ref uint cell, ref Vector3 local); Vector3 GetBlockOffset(uint a, uint b); /* … */ }
public static class Sequence16 {
  public static bool IsNewer(ushort stored, ushort incoming);       // |d| > 0x7FFF ? incoming < stored : stored < incoming
  public static bool NewerEvent(ref ushort stored, ushort incoming);
  public static bool NotOlder(ushort stored, ushort incoming);
}
```

### 4.3 Motion (lead L4 data; avatars behaviour)

Consumers: physics PH5-PH7, state (wire mapping), G5.

```csharp
namespace Lifestone.Data.Motion;
public enum HoldKey : uint { Invalid, None, Run }
public readonly record struct MotionActionNode(uint Action, float Speed, ushort Stamp, bool Autonomous);
public sealed partial class RawMotionState { /* hold keys, style 0x8000003D, forward 0x41000003, sidestep/turn, speeds 1, Actions */ public RawMotionState Clone(); }
public sealed partial class InterpretedMotionState { /* style, forward/sidestep/turn + speeds, Actions */ }
public interface IMotionWeenie { bool IsCreature {get;} bool IsPlayer {get;} bool TryGetRunRate(out float r);
  float JumpVelocityZ(float extent); bool CanJump(float extent); int JumpStaminaCost(float extent, out int adj); }
public interface IMotionInterpHost { bool HasContact {get;} bool OnWalkable {get;} bool HasGravity {get;} bool InCell {get;}
  Vector3 Velocity {get;} void SetLocalVelocity(Vector3 v); void RemoveLinkAnimations(); IMotionWeenie? Weenie {get;} }
public sealed class MotionInterpreter {      // CMotionInterp
  public MotionInterpreter(MotionPlayer parts, IMotionInterpHost host);
  public WeenieError DoMotion(uint motion, in MovementParameters p); public WeenieError StopMotion(uint motion, in MovementParameters p);
  public bool MoveToInterpretedState(InterpretedMotionState s);   // 0x005289C0
  public void ApplyRawMovement(bool cancelMoveTo, bool allowJump); // 0x005287E0
  public int ChargeJump(); public WeenieError Jump(float extent, out int stamina);
  public void HitGround(); public void LeaveGround(); public Vector3 GetStateVelocity(); public float GetMaxSpeed(); public float GetAdjustedMaxSpeed();
  public RawMotionState Raw {get;} public InterpretedMotionState Interpreted {get;} public bool StandingLongJump {get;}
}
public sealed class PositionManager { InterpolationManager Interpolation {get;} ConstraintManager? Constraint {get;}
  void AdjustOffset(ref AcFrame offset, double quantum, in PositionContext ctx); InterpolationBlip? UseTime(in PositionContext ctx); }
```

### 4.4 Logging and clock (lead L6)

```csharp
public enum LogSeverity { Debug, Info, Warning, Error }
public readonly record struct LogField(string Key, object? Value);
public interface ILogSink { bool IsEnabled(LogSeverity s); void Write(LogSeverity s, string category, string message, ReadOnlySpan<LogField> fields); }
// Net keeps INetLog (no Client reference); state adapts it. Tests: tests/Shared/ManualTimeProvider (timers fire on Advance), RecordingLogSink.
```

### 4.5 Readiness (lead L7; portal owns it afterwards)

Providers: the G5/G1 Godot adapter, state's `DataOnlyAttachments` and physics' `CollisionStreamer` (Collision). Consumer: portal `DestinationReadiness`.

```csharp
public enum AttachState : byte { Pending, Attached, Failed, Disabled }   // Failed and Disabled count as settled
public interface IWorldAttachments {
  AttachState Terrain(LandblockCoord lb); AttachState Objects(LandblockCoord lb); AttachState Collision(LandblockCoord lb);
  AttachState Cell(uint envCellId); IReadOnlyList<uint>? VisibleCells(uint envCellId); bool SeenOutside(uint envCellId);
}
```

### 4.6 Net (C-net)

Consumers: state (sole client-side caller), Loopback tests, server mod (`LoopbackPolicy` by link).

```csharp
public sealed record AcSessionOptions { required IPEndPoint Server; required DddIterations Iterations; string ClientVersion = "1802";
  TimeProvider Time = TimeProvider.System; SessionTimeouts Timeouts; LoginRetryPolicy Retry; bool TailProbe = true;
  InboundOrder Order = InboundOrder.FragmentSequence; Func<IPEndPoint, IDatagramSocket>? SocketFactory; INetLog? Log; }
public readonly record struct DddIterations(int Portal, int Cell, int Language, int? HighRes);
public interface IAcSession : IDisposable {
  SessionPhase Phase {get;} SessionFailure? Failure {get;} CharacterListMsg? CharacterList {get;} uint? PlayerGuid {get;} LinkStats Link {get;}
  void Login(AcCredentials c); PumpResult Pump(TimeSpan budget); ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken ct);
  OutboundTicket CreateCharacter(in CharacterCreateRequest r); OutboundTicket DeleteCharacter(int listIndex);
  OutboundTicket RestoreCharacter(uint guid); OutboundTicket EnterWorld(uint guid); OutboundTicket SendLoginComplete();
  OutboundTicket SendMoveToState(in RawMotionStateWire s, in AcPosition p, in ObjSeqs q, bool contact, bool standingLongJump);
  OutboundTicket SendAutonomousPosition(in AcPosition p, in ObjSeqs q, bool contact);
  OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition p, in ObjSeqs q);
  OutboundTicket SendUse(uint guid); OutboundTicket SendQueryHealth(uint guid);
  OutboundTicket SendTalk(ReadOnlySpan<char> text); OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text);
  void LogOff(); void Quit();
}
public static class AcSession { public static IAcSession Create(AcSessionOptions o, IAcSessionSink sink, IRawMessageTap? tap = null); }
public interface IInboundMessageHandler { /* default no-op On* per decoded message (records own their data; spans valid only during the call) */ }
public interface IAcSessionSink : IInboundMessageHandler { void OnSessionEvent(in SessionEvent e); }
public static class InboundDispatcher { public static DispatchResult Dispatch(in InboundContext c, ReadOnlySpan<byte> body, IInboundMessageHandler h); }
public interface IRawMessageTap { void OnMessage(MessageDirection d, long ts, uint fragSeq, MessageQueue q, ReadOnlySpan<byte> body); } // never sees LoginRequest
public sealed class AcCredentials : IDisposable { public AcCredentials(string account, ReadOnlySpan<char> password); /* ToString redacted; Dispose zeroes */ }
public static class LoopbackPolicy { bool IsAllowed(EndPoint ep, out string? why); IPEndPoint Require(EndPoint ep);
  bool TryParse(string text, out IPEndPoint ep, out string? err); bool IsPinnedConfigHost(string? host); /* exactly "127.0.0.1" */ bool IsLoopback(IPAddress? address); }
namespace Lifestone.Data.Animation; public static class MotionCommandTable { const int Count = 412; bool TryFromWire(ushort lw, out uint cmd); ushort ToWire(uint cmd); string? NameOf(uint cmd); }
```

Notes from wave 1:
- The server mod compile-links both `LoopbackPolicy.cs` and `LoopbackRequiredException.cs`, not into `Lifestone.Client` (which would give CS0436). The config-host check is `IsPinnedConfigHost(host)`; address checks are `IsLoopback(address)`.
- A known-type id whose packed value is 0 decodes as the bare type id (0x04000000, 0x05000000, 0x01000000, 0x06000000), as retail does and as `ObjDesc.TryUnpack` does, not as 0. A "0 means none" check on a WeenieDesc icon or a ModelData id tests for the bare type id (protocol.md, Conventions).
- `WaitForInboundAsync(max, ct)`: hosts pass a finite `max` and check `Phase`. A `Timeout.InfiniteTimeSpan` wait on a socket whose receive loop has faulted returns false at once.

### 4.7 Session facade (C-state)

Consumers: retail UI and screens, portal bridge, avatars and G5, physics PH7, G6, G7, Loopback tests.

```csharp
public interface IGameSessionView { GameState State {get;} IEventBus Events {get;} IActionApi Actions {get;} PlayerTools Tools {get;}
  SessionClock Clock {get;} ActionLog ActionLog {get;} LinkStats Link {get;} }
public sealed class GameSession : IGameSessionView, IDisposable {
  public static GameSession Offline(Action<IInboundMessageHandler> standIn, TimeProvider t, ILogSink log);
  public void Tick(double now);   // pump(budget) → reducer → bus → Actions.Update → recorder flush
  public void Reconnect(); public double LastTickMs {get;}
}
public static class SessionBootstrap { public static GameSession CreateLive(SessionOptions o, ReadOnlySpan<byte> secretsJson,
  ReadOnlySpan<byte> pidsJson, DatSet dats, TimeProvider t, ILogSink log, Stream? recording); }  // only caller of AcSession.Create
public interface IEventBus { IDisposable Subscribe<T>(BusHandler<T> h) where T : struct, IGameEvent; IDisposable SubscribeRaw(RawMessageHandler h); }
// Events: SessionPhaseChanged, ConnectionProgress, CharacterListChanged, CharacterCreateResult, CharacterErrorReceived, Booted,
//  PlayerCreated, ObjectCreated(guid,isSelf,inWorld,recreated), ObjectUpdated, ObjectDeleted,
//  ObjectMoved(guid, CellFrame, PositionVerdict {ForceBlip,SelfTeleport,SelfEcho,Remote}, teleportAdvanced, hasContact, velocity, placement),
//  ObjectMotionChanged, ObjectVectorChanged, ObjectStateChanged(guid,old,new), ObjectParented, ObjectPickedUp, ObjectDescChanged,
//  PlayerTeleportAnnounced(seq), SelfTravelChanged, ServerTimeUpdated, ChatReceived, ToolReplyReceived, ToolsUnavailable, UseDone,
//  HealthUpdated, QualitiesChanged, LogOffRequested(now, pkDelay), MessageDropped, UnknownMessage
public enum ActionSource { Host, Ui, Automation, Client, Test }
public readonly record struct ActionOutcome(ActionOutcomeKind Kind, uint Code = 0, string? Detail = null); // Confirmed, Local, Sent, ServerError, TimedOut, Refused, NotImplemented, Cancelled, DryRun
public interface IActionApi {
  ActionHandle Login(ActionSource s = ActionSource.Host); ActionHandle SelectCharacter(uint guid, ActionSource s);
  ActionHandle CreateCharacter(CharacterCreateIntent i, ActionSource s); ActionHandle DeleteCharacter(uint guid, ActionSource s);
  ActionHandle RestoreCharacter(uint guid, ActionSource s); ActionHandle EnterWorld(uint guid, ActionSource s);
  ActionHandle LoginComplete(ActionSource s = ActionSource.Client); ActionHandle Use(uint guid, ActionSource s);
  ActionHandle QueryHealth(uint guid, ActionSource s); ActionHandle Talk(string text, ActionSource s); ActionHandle Tell(string to, string text, ActionSource s);
  ActionHandle LogOff(ActionSource s); ActionHandle Quit(ActionSource s);
  IMovementSink Movement {get;} MoveIntents Move {get;} bool KillSwitch {get;set;} bool DryRun {get;set;}
}
public interface IMovementSink {   // physics' controller writes; state stamps ObjSeqs from Self.Stamps
  void MoveToState(RawMotionState raw, in CellFrame position, bool contact, bool standingLongJump);
  void AutonomousPosition(in CellFrame position, bool contact); void Jump(float extent, Vector3 velocity, in CellFrame position); }
public sealed record CharacterCreateIntent(uint Heritage, uint Gender, CharacterAppearance Appearance, int TemplateOption,
  uint Strength, uint Endurance, uint Coordination, uint Quickness, uint Focus, uint Self, uint ClassId,
  IReadOnlyList<uint> SkillAdvancementClasses /* exactly 55 */, string Name /* ASCII */, uint StartArea);
public sealed class PlayerTools { ToolHandle<ToolStatus> Status(...); ToolHandle<PlaceResult> Place(TeleportDestination d, ActionSource s);
  ToolHandle<WhereResult> WhereAmI(ActionSource s); ToolHandle<XpResult> AddXp(long amount, ActionSource s); ToolAvailability Availability {get;} }
```

Additive changes made in wave 1 (docs/session.md §13): `SessionBootstrap.CreateLive` takes an optional last parameter `RecordingHeader?` (the runner and scenario of a recording); `GameSession.Reconnects` and `WaitForInboundAsync`; local notice 7 (`SessionReset`), recorded so a replay resets too; `GameStateDigest`, `SessionReplayer` and `ReplayResult`; and the `Hosting` and `Scenarios` namespaces. G6's `SessionHost` calls `SessionBootstrap.CreateLive` and must not name the network types.

### 4.8 Portal (C-portal)

Consumers: G1, state ST15, avatars AV9, retail UI and G2/G4 (`ISoundSink`), G5 (`IEffectEventSink`).

```csharp
public enum TeleportAnimState { Off, WorldFadeOut, Tunnel, InTunnel, Holding, LeavingTunnel, Arriving }
[Flags] public enum PortalCues : uint { PlayEnterPortal=1, PlayExitPortal=2, RestartTunnelSpin=4, StopTunnelSpin=8, PlaceTunnelCamera=0x10,
  ShowTunnel=0x20, HideTunnel=0x40, HideWorld=0x80, ShowWorld=0x100, PostPortalNotice=0x200, SendLoginComplete=0x400,
  SelectionIndicatorOn=0x800, SelectionIndicatorOff=0x1000, BusyOn=0x2000, BusyOff=0x4000, ReadinessStalled=0x40000, ArrivedWithFailures=0x80000 }
public readonly record struct PortalFrame(TeleportAnimState State, double StateElapsed, bool TunnelVisible, bool WorldVisible,
  PortalLens Lens, double RollDegrees, PortalCues Cues, bool Busy);
public readonly record struct PortalLens(float Focal, bool Overridden) { double VerticalFovDegrees {get;} float Near {get;} float FrustumHeightAtNear {get;} }
public sealed class PortalSpaceDirector { public PortalFrame Step(double now, uint playerCellId, IWorldAttachments world);
  public TeleportProgress Progress {get;} public PortalSpaceMachine Machine {get;} public ReadinessReport LastReadiness {get;} }
public sealed class PortalSessionBridge : IDisposable { public PortalSessionBridge(IGameSessionView s, PortalSpaceDirector d); } // events → TeleportProgress; SendLoginComplete → Actions.LoginComplete
public interface ISoundSink { void PlayFromCenter(uint soundTableId, uint soundType); void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin o);
  void PlayFromTable(uint tableId, uint soundType, SoundOrigin o, float volume); }
public interface IAudioOutput { void Play(in VoicePlay p); void Stop(int voice); bool IsPlaying(int voice); }   // Godot AudioService implements
public interface IEffectEventSink { void OnEffectEvent(in EffectEvent e, in SoundOrigin o); }
public static class HiddenTransition { HiddenChange Classify(uint oldState, uint newState); PlayScript Script(HiddenChange c);
  uint ApplyToObjectState(uint state, HiddenChange c); uint Play(EffectObject fx, HiddenChange c); }   // set_hidden 0x00515760
```

`SoundOrigin`'s bearing is retail's source-to-listener heading minus the listener's heading (docs/portal-space.md §7.2, §15.1), which is 180° from the listener-to-source bearing. Every consumer builds a `SoundOrigin` with `SoundOrigin.Between(listener, source)`, never by hand.

### 4.9 Avatars

Consumers: G5, retail UI CharGen (G3), physics (`IPlayerBody`).

```csharp
public sealed class ObjDesc { static ObjDesc FromWire(uint paletteId, ReadOnlySpan<WireSubPalette> s, ReadOnlySpan<TextureMapChange> t, ReadOnlySpan<AnimPartChange> a);
  static ObjDesc FromDat(DatReaderWriter.Types.ObjDesc d); ObjDesc Add(ObjDesc rhs); string CacheKey {get;} }
public sealed class PartArrayAppearance { static PartArrayAppearance Create(ModelAssembly s, IAppearanceSource src);
  AppearanceResult DoObjDescChanges(ObjDesc d); AppearanceResult DoObjDescChangesFromDefault(ObjDesc d); IReadOnlyList<PartAppearance> Parts {get;} }
public sealed record PartAppearance(int Index, LodChain Chain, uint BasePaletteId, IReadOnlyDictionary<uint,uint> SurfaceTextureBySurfaceId, PaletteData? Palette, string VariantKey);
public sealed class AvatarSet { void Apply(in ObjectCreated e, AvatarCreate c); void Apply(in ObjectMoved e); /* … one Apply per classified event */
  void Advance(double now, in AvatarViewer viewer, long deadlineTicks); AvatarFrame Frame {get;} int DrainNotices(List<AvatarNotice> into); }
public interface IPlayerBody { CellFrame Position {get;} bool HasContact {get;} bool OnWalkable {get;} MotionInterpreter Motion {get;} }
public sealed class CreaturePreview { static CreaturePreview Create(AvatarSources s, CharGenComposition c, PreviewAnimations a, double now);
  void SetAppearance(CharGenComposition c); void StartAnimation(double now); void StopAnimation(); void Advance(double now); ReadOnlySpan<PartDraw> Draws {get;} }
```

### 4.10 Physics (C-physics)

Consumers: state ST11/ST15/ST16, avatars (`IGroundProbe`), G5, G6, the readiness adapter.

```csharp
public sealed class PhysicsWorld { void Attach(LandblockCollision b); bool Detach(LandblockCoord c); PhysCell? GetCell(uint id);
  ViewerSweepResult SweepViewer(in CellFrame from, in CellFrame to, float radius); }        // chase camera (IsViewer)
public interface IPlayerMovementIntents { void HandleCommand(uint command, bool start, float speed = 1f); void SetHoldRun(bool on);
  void SetHoldSidestep(bool on); void ToggleAutoRun(); void CommenceJump(double now); void DoJump(double now); CellFrame Position {get;} bool OnGround {get;} }
public sealed class PlayerMovementController : IPlayerMovementIntents { PlayerMovementController(PhysicsObj p, IMovementSink sink, MovementOptions o);
  void Tick(double now); ServerPositionOutcome Apply(in ObjectMoved selfMove, double now); }
public sealed class CollisionStreamer { void SetFocus(uint playerCellId); void Pump(FrameBudgetScheduler b); void PumpSynchronously(); AttachState StateOf(LandblockCoord c); }
public interface IGroundProbe { bool TryGroundZ(in CellFrame at, float radius, out float z, out Plane contact); }
public sealed class RouteDriver { RouteDriver(IPlayerMovementIntents c, ScriptedRoute r); RouteStatus Tick(double now); string? FailureReason {get;} }
public sealed class MovementAudit : IMovementSink { MovementAudit(IMovementSink inner, IEventBus bus); MovementAuditSummary Summary {get;} string SummaryLine(); }
```

### 4.11 Retail UI (C-ui)

Consumers: G2, G3, G4, G7.

```csharp
public interface IUiRegionView { UiBox ScreenBox {get;} bool IsVisible {get;} UiImageRef Image {get;} uint AlphaImageDid {get;}
  ReadOnlySpan<IUiRegionView> ChildrenInDrawOrder {get;} void Draw(IUiPainter p, UiBox clip); uint ViewportElementId {get;} }
public interface IUiPainter { void DrawImage(UiImage i, UiBlitMode m, float alpha, UiBox bounds, UiBox dirty, (int X,int Y) tiling);
  int DrawCharacter(int x, int y, UiFont f, char c, uint argb, UiTextFlags fl, uint bk); void NotColorBits(UiBox b); void Fill(UiBox b, uint argb); }
public sealed class UiCompositor { void AddRoot(IUiRegionView r); void AddDirtyRect(IUiRegionView r, UiBox b);
  bool Composite(long deadlineTicks); void CopyDirtyTiles(IUiTileSink sink); }   // 128-px RGBA8 tiles
public sealed class UiElementManager { UiElement? CreateRootElement(uint layoutEnum, uint elementId); void Update(double now);
  UiInputRouter Input {get;} UiCursorController Cursor {get;} DialogFactory Dialogs {get;} void SetScale(UiScale s); }
public interface IInputActionSink { bool OnInputAction(uint inputMapId, uint actionId, RetailActivation a); }
public interface IWorldPointerSink { void OnWorldPointer(int x, int y, UiMouseButton b, bool down, bool dbl); void OnMouseLook(float dx, float dy); bool TextEntryActive { set; } }
public interface IUiViewportHost { void Bind(uint viewportElementId, UiBox screenBox, bool visible); }
public interface IUiNoticeSink { void DisplayStringInfo(int textType, in UiStringRef s); }   // type 26 → spew box
public sealed class CharGenState { CharacterCreateIntent GetResult(); /* setters, Randomize, credits */ }
```

### 4.12 Server tools (C-mod)

Consumers: state ST12, G7, net N12, Loopback tests, smoke, play.ps1.

```text
Request (Talk 0x0015, ASCII):  @lifestone <status|where|net|places|xp N|place poi "<name>"|place loc <cell> <x> <y> <z> [qw qx qy qz]|quest <stamp|erase|has> <name>> [#id]
Reply  (one F7E0, Broadcast):  "Lifestone: <human line>\n#lifestone/1 {json}"  (camelCase, non-ASCII escaped)
Errors: bad-args unknown-verb not-found invalid-cell busy not-in-world not-loopback needs-player failed
pids.json ace: { modReady, modVersion, modWire, modDllSha256, readyUtc }
Get-LsBackendState → { MariaDb, Ace, ModReady, ModVersion, ModWire, AcePid, MariaDbPid, Stray }
secrets.json v2: { schema:2, mariadb:{…unchanged}, defaultAccount:"play", accounts:{ play:{username,password}, test:{username,password}, <name>:{…} } }
ACE log lines: "Lifestone mod loaded: …", "Lifestone mod refused to load: <reason>", "Lifestone: ACE's first-account Admin auto-promotion is off…",
               "Lifestone mod ready: …", "Lifestone tool: account=… verb=… -> ok|<code>"
```

### 4.13 Wave-2 Godot contracts

| Interface | Provider | Consumers |
|---|---|---|
| `CreatureModeView : SubViewport` (Create, Active, Texture, SetCamera(roll), SetLens, SetLights, AddActor, Advance) | G1 | G1 tunnel, G3 CharGen 3D view, the paperdoll later |
| `AudioService : Node, IAudioOutput` (16 voices, UI and Effects buses, `--mute`/`--volume`) | G1 | portal `RetailSoundPlayer`/`ISoundSink` |
| `RetailUiLayer` (Scale, LogicalSize, GameViewRect, KeyboardCaptured, WantsWorldHidden, RegisterViewportSource(id, IUiViewportSource), SetSelectionProjector) | G2 | G1 (tunnel in viewport 0x10000436; `WorldLens` centres the world projection on GameViewRect, D40), G3, G5 (camera, picking, input), G6 |
| `WorldVisibility` arbiter (sole writer of `Viewport.Disable3D`; requests are reference-counted) | lead W2-pre(a) | G1 presenter, G6 PlayMode, G2 |
| `ISelectionProjector.TryProject(guid, out ScreenRect, out bool onScreen)`, `ObjectSelection` | G5 | G4 VividTargetIndicator, G7 inspector |
| `SessionHost : Node` (priority −900, `SessionHost.Current` → `IGameSessionView`) | G6 | G2, G3, G4, G5, G7 |
| `WorldAttachments : IWorldAttachments` (Game adapter) | G1 | portal director |

As built (G4 g0): the rectangle is the Client type `ScreenRect` (X, Y, Width, Height, logical UI pixels), not Godot's `Rect2`, so the engine-agnostic G4 screens can use it (docs/avatars.md §18).

### 4.14 Verdict lines (text contract, frozen in L19)

- `[portalcheck] PASS|FAIL <name> …` and `all portal-space checks hold`.
- `portal space frames done captured=4 failures=0 max_frame_ms=<x>`.
- `[uicheck] PASS|FAIL …` and `retail ui frames done size=<WxH> scale=<s> captured=<n> failures=0 missing_images=<k>`.
- `headless done scenario=<s> ok=<0|1> corrections=<n> where_max_m=<x> unmatched=<n> recording=<path>`. `where_max_m` is `none` only when `ok=0`, and `recording` can be `none`. Exit codes: 0 success, 1 bad arguments or a failed scenario, 2 could not start.
- `[logincheck] done ok=<true|false> enter_to_walk_max_ms=<x> corrections=<n> where_max_m=<x> teleports=<n> holtburg_min_fps=<x> academy_objects=<n> holtburg_objects=<n> logoff=ok`.
- `[logincheck] done` ends with four more judged fields since G4 (additive; the earlier fields and their order are unchanged): `... logoff_by=<key|action> shell=ok|missing vitals=<h>/<s>/<m>|empty spew_portal=yes|no vitals_window=stacked|side` (docs/session.md §27).
- `all play checks hold` / `PLAY CHECKS FAILED` carries `pass=<n> fail=<n> max_frame_ms_after_arrival=<x> enter_window_max_frame_ms=<x> list_to_walking_max_frame_ms=<x> slow_frames_over_50_ms=<n> teleport_max_frame_ms=<x> teleport_slow_frames_over_50_ms=<n> canvas_pipelines_from_enter=<n>`; `list_to_walking_max_frame_ms` is the longest frame from the character list to the first teleport (G4 live-gate fix 3).
- `movement route=<r> laps=<n> corrections=<n> where_max_m=<x> result=PASS|FAIL`.

No line ever contains an account password.

---

## 5. Wave 2 (Godot)

### 5.1 W2-pre: lead edits to shared Godot and Client files, in three slices

Each slice forks only the tracks that need it.

- **W2-pre(a), the portal view. After portal merges.**
  - `AcToGodot.CameraRotation(heading, pitch, roll)`.
  - `SurfaceLighting.CreatureMode`: its own `ac_cm_*` globals, no sun, no fog; added to warm-up. The direction and intensity conventions are verified by a check.
  - A `PortalLens` feed:
    - `ViewFrustum` accepts angles below 180°;
    - `InteriorController` builds its frustum and ViewKey from the lens;
    - `SkyView.Follow` copies Projection, Near, Size and FrustumOffset;
    - `SkyView.Paused`.
  - **The SmartBox frame (D40).** The lens also carries the frustum offset that centres the world's projection on `RetailUiLayer.GameViewRect`; the interiors' frustum, the sky and picking use that same projection. Without the in-game shell (M1's explore mode), the rect is the whole window. The same holds for retail's default shell (D36), whose SmartBox fills the screen: no offset is applied for now (m2 §7, decision 11).
  - Main-scene `--fov` (Rendering group, AlsoIn TerrainPreview; FlyCamera.cs:134 uses it).
  - `GameCheckSuite.PortalSpace` plus `--portal-space-checks` in Checks; update OptionCatalogTests.cs:67-69.
  - `TeleportPanel.JumpTo` gets an `ITeleporter` hook plus a "Portal transition" toggle (TeleportPanel.cs:170-181).
  - `ActorMeshLibrary` gets a `SurfaceLighting` parameter in its key and request (:45,59).
  - `ActorPartDraws.DegradesDisabled`.
  - The `WorldVisibility` arbiter.
  - A `GameServices.Audio` slot.
  - Attach queries: `ObjectsSystem.StateOf(coord)`, `InteriorController.IsCellAttached(id)`, `InteriorsLayer.NeedsAllCells` for the player's cell (InteriorsLayer.cs:91).
  - The Main install line for `PortalSpaceSetup`/`PlayWorldSetup` after Cinematic attach (Main.cs:151).
  - game-core.md: the process-priority table and the **frame-budget table**. MainThreadQueue keeps 4 ms. The tunnel and the session pump (default 2 ms, `--session-budget-ms`) are subtracted by `FrameBudgetShare` (priority −1100) down to a 1 ms floor. The UI composite has its own 1 ms slice per frame, time-sliced per root. All are reported in PerfOverlay.
- **W2-pre(b), the renderer. After C-ui.**
  - Main hook `RetailUiSetup.AttachExplore` (after `SkyTrack.Attach`, Main.cs:148).
  - Canvas-layer slot 10 for the retail UI.
  - A PerfOverlay UI line.
  - `Lifestone.Data.csproj` EmbeddedResource for `Ui\Layouts\*.json`.
- **W2-pre(c), world, objects and session. After avatars.**
  - `DynamicObject` and `DynamicCellObject` lighting variants, reading `INSTANCE_CUSTOM`: x is the cell slot, y the light-set row in an `ac_object_lights` sampler, z a luminosity override, w the translucency. Per-instance translucency is subject to D45.
  - Appearance-keyed mesh keys in `RenderResources` (RenderResources.cs:236-237).
  - PerfOverlay rows for session, portal and avatars.
  - EngineChecks dynamic-batch checks.
  - SessionHost install for `--play`.
  - Spread `LandblockStreamer.Reset` detaches over tunnel frames (:163-174), conditional on W13's measurement. **Measured (5b slice b5): not needed.** The legs' four server teleports per run (the Academy wing, Holtburg and back) give at most 12.7 ms per frame from the teleport to a second after the player is present (`teleport_frame_max_ms`); the heavy frame is the first world start at login (the explore start's landblocks dropped by the world's teleport), whose interiors placement now runs a frame later.

### 5.2 Tracks

| Track | Owns | Delivers | Depends on | Size |
|---|---|---|---|---|
| **G1 portal view and audio** | scripts/Audio/, scripts/CreatureMode/, scripts/PortalSpace/; OptionCatalog.PortalSpace.cs, OptionCatalog.Audio.cs | AudioService, CreatureModeView, tunnel, presenter, WorldLens, WorldAttachments adapter, offline transition, checks, frames runner | portal merge; W2-pre(a) | 2.5k / 0.4k, 5 |
| **G2 retail UI renderer and login screens** | scripts/RetailUi/; Client/RetailUi/Screens/Login/; OptionCatalog.RetailUi.cs | Godot tile layer, cursor, input feed, viewport host, image source, `[uicheck]`; data check, disconnected, character management, dialogs; frames runner | C-ui; W2-pre(b); C-state-offline | 4.5k / 1.5k, 8 |
| **G3 CharGen screens** | Client/RetailUi/Screens/CharGen/; scripts/RetailUi/CharGenViewportBinder.cs | six pages, Cg3DViewModel, F643 dialogs | G2 U2; retail-ui R18a; avatars AV12; G1 CreatureModeView | 3.5k / 1.2k, 6 |
| **G4 in-game shell** | Client/RetailUi/Screens/GamePlay/ | SmartBox frame and viewport, spew box, vitals, chat, radar, toolbar and panel host (NeedsGameplay, including chat channel tabs and combat/cast buttons), indicators, selection, character info, options, logout | G2; state; G5 ISelectionProjector | 4.5k / 1.3k, 7 |
| **G5 world objects and player** | scripts/World/ServerObjects/, scripts/Player/ (new files), Client/Camera/, Client/Play/, Client/Avatars/SessionAvatarAdapter.cs; OptionCatalog.Avatars.cs, OptionCatalog.Play.cs (player parts) | AV13 adapter, server-objects layer (MultiMesh + INSTANCE_CUSTOM), picking and selection, collision wiring, player controller, chase and first-person cameras, OfflineWalker in explore walk mode, frame-time hardening | avatars; W2-pre(c); physics (W8) | 4.5k / 0.8k, 9 |
| **G6 session and play mode** | scripts/Session/ | SessionHost (via SessionBootstrap), PlayMode, PlayerPlacement, PlayPresentationFlow, failures and quit, LoginCheckRunner | state merge; G1; G2 (or placeholder entry) | 2.5k / 0.6k, 6 |
| **G7 Lifestone panels** | Data/Ui/Layouts/{tools,inspector}.json, Client/RetailUi/Lifestone/ | tools panel (Places, Where am I, Add XP), automation inspector, LifestoneHotkeys | G2; state ST12; R9 | 1.5k / 0.6k, 3 |

If capacity is short, G3, G4 and G7 can share one owner, and G5 and G6 can share one owner. Folders never overlap.

**As built:** the screens of G2 and G3 share the flat folder `Client/RetailUi/Screens/` (namespace `Lifestone.Client.RetailUi.Screens`) and are told apart by file name instead of by sub-folder: G2's are `DataPatchScreen`, `DisconnectedScreen`, `CharacterManagement*`, `SkippedScreens`, `UiLoginScreen`, `LoginScreens` and `RetailUiScreenHost`, G3's are `CharGen*`. G4 follows the same rule with a `GamePlay` file prefix (no `Screens/GamePlay/` folder), so the owned names, not the folders, are what must not overlap.

### 5.3 Steps

#### G1 portal view and audio
- **W1 Skeleton.** `PortalSpaceSetup`/`PlayWorldSetup.Install`; `PortalSpaceOptions.cs`, `AudioOptions.cs`; `FrameBudgetShare` (priority −1100, `MainThread.Budget` settable at MainThreadQueue.cs:69-73); `Client/Play/BudgetShare.cs`. Tests: BudgetShareTests.
- **W2 AudioService.** Run-time buses UI and Effects → Master. `WaveStreams` builds AudioStreamWav and AudioStreamMP3 on workers; an unsupported tag gets a loud TODO. Implements `IAudioOutput` with 16 voices. Automated runs are muted unless `--volume` is given.
- **W3 CreatureModeView and PortalTunnel.**
  - Modelled on SkyView.cs:26-82, with `ByteSpaceBlending.Attach(env)` (ByteSpaceBlending.cs:104) and CreatureMode lighting.
  - Reuses portal's `TunnelObject` (`TunnelObject.Create(dats, assets, createdAt: now)`).
  - Camera at AC (0.24, −2.7, 0.88) with roll; one warm render at start-up.
  - `--portal-space` holds the tunnel in state 3.
- **W4 PortalSpacePresenter.**
  - Priority 15. Calls `PortalSpaceDirector.Step` and applies `PortalFrame`: WorldVisibility, lenses, tunnel visibility. It plays no sound for the cues: the director plays the enter and exit whooshes and the hum itself through the `ISoundSink` that W1 gives it, and a presenter that replayed them would double each whoosh (docs/portal-space.md §11.1, §11.2, §15.1). The presenter passes its sink to the director and plays nothing. Without a sink (the headless host) portal space is silent.
  - The notice goes to `IUiNoticeSink`, or to a labelled placeholder line before the UI exists (D32).
  - `WorldLens`: Frustum projection, `Near = f/4` below 0.4 (D31), centred on `GameViewRect` (D40): the frustum offset puts the view's centre at the frame's centre, the lens is sized for the frame as retail sized it for its device viewport, and the zoom expands and collapses about that centre. In retail's default shell the SmartBox fills the screen (D36), so `GameViewRect` is the whole window and the offset is zero; the hook stays.
  - The `WorldAttachments` adapter (uses `Client/Play/AttachStateMapping.cs`).
  - `OfflinePortalTransition` behind the TeleportPanel hook (`--portal-transition`).
  - Tests: AttachStateMappingTests.
- **W5 Checks and frames.** `PortalSpaceChecks`:
  - UIASSET chain in-engine;
  - byte-space composition readback;
  - CreatureMode lighting probe against different world globals;
  - lens matrices at f ∈ {f_game, 0.4, 0.01, 0.001}, for the whole window and for an off-centre GameViewRect;
  - interiors frustum;
  - Disable3D leaves the SubViewport rendering;
  - streams at 11,025 Hz mono 16-bit, with length within 1 ms;
  - buses and mute;
  - 3 loops give 3 hums;
  - a scripted 2→0 sequence sends one LoginComplete.

  `PortalSpaceFramesRunner` (`--portal-space-frames`): Holtburg→Arwic at a fixed 1/60 s step and a fixed seed, four captures.

#### G2 renderer and login screens
- **U2 `RetailUiLayer`.**
  - CanvasLayer 10; one Node2D per root with integer `Scale(s)`, Nearest filtering, no mipmaps.
  - `RetailUiTiles`: reused Images, dirty tiles only, priority 25.
  - `RetailUiInput`: `_Input` → DIK table (Client) → router; `SetInputAsHandled` only when consumed; `KeyboardCaptured` drives FlyCamera.InputEnabled. Godot's `NOTIFICATION_APPLICATION_FOCUS_OUT` and `FOCUS_IN` call `manager.Input.SetClientActive(false / true)`.
  - `RetailCursor` (`SetCustomMouseCursor`, scaled ×s, at most 256 px).
  - `RetailViewportHost`, `GodotUiImageSource` (worker decode).
  - Adapters: `ISoundSink` → AudioService; clipboard via DisplayServer.
  - `RetailUiOptions.cs`.
  - `[uicheck]`: byte-exact at s = 1 and 2, nearest, no tile seams, viewport hole, cursor, every gmDefaultMap key bound.
- **U3 Screen host.** UiFlow modes: data check → character management (intro skipped, D38); epilogue quits, or reconnect per D43. Reads `IGameSessionView`; offline via `GameSession.Offline` + `StandInSessions`.
- **U4 Login screens.**
  - DataPatch: 0x10000001/0x1000041A, meters 0x1000041E/0x1000041F.
  - Disconnected: 0x10000003/0x10000416.
  - CharacterManagement: 0x10000005/0x1000039A; list 0x1000039D with row template 0x100003A5; UpdateButtons rules; delete by index; restore by GUID; please-wait, entering, delete-confirm, exit-confirm and error dialogs.
  - Retail-exact: no 3D view of the selected character (D35; `gmCharacterManagementUI` has none).
  - Tests: the ghosting matrix, error-code routing, and RequiresLocalDats element-existence tests.
- **U9 Frames runner.** `--retail-ui-frames[=DIR]` (default `docs/screenshots/m2/ui/<W>x<H>`); fails if the window size is wrong; `retail ui frames done …`. PerfOverlay UI line.

#### G3 CharGen
- **U5.** CharGenMain 0x10000039/0x100003CC, pages 0x100003D1-0x100003D6.
  - Heritage, profession, skills, appearance, town and summary pages over `CharGenState`.
  - The Name filter. F643 NameInUse/NameBanned dialogs.
  - The 3D view binds `CreaturePreview` into `CreatureModeView`. Until both exist, an ActorView stand-in is labelled as a placeholder.
  - Tests: navigation and gating; RequiresLocalDats page roots; a loopback create in the login smoke step.

#### G4 in-game shell
- **U6.** GamePlay root 0x10000006/0x10000495 per D36.
  - SmartBox 0x1000049A, which fills the screen in retail's floating-window shell (D36); viewport 0x10000436 gets the tunnel source from G1; GameViewRect published (gmSmartBoxUI::RecvNotice_UpdateGameView) and, for now, the whole window (D40 as amended). The floaty windows draw over the world.
  - SpewBox (type 26 only; identical text replaced; capped).
  - Vitals 0x100005FA/0x100006D5 from `Self.Qualities` (D16).
  - Chat windows 0x10000505/0x1000050E-0x10000510; `#lifestone/1` lines filtered; entry sends Talk.
  - Radar 0x100006D2.
  - Toolbar and panel host: NeedsGameplay dialogs for every gameplay panel, the chat channel tabs, and combat and casting.
  - Indicators (LinkStats), selection with health (QueryHealth), character info, options (honoured settings live, the rest ghosted, D42), logout confirmation → `Actions.LogOff`.
  - The shell is composited during the "entering world" dialog, before it appears.
  - **As built:** docs/retail-ui.md §21 (the GamePlay root, spew box, chat, vitals, radar, toolbar and panel host, indicators, selection, character information, options, logout and the shell's checks); picking is docs/avatars.md §18.

#### G5 world objects and player
- **AV13 `SessionAvatarAdapter`.** Bus events → AvatarSet. Replay tests over `cache/sessions`.
  - **As built (G6 stage 3):** the adapter keeps the events in order in a queue and applies them inside a time budget (`Pump`); `SessionAvatarAdapterTests`, an allocation test, and a replay test over every real recording (skipped without one; `--record-session` makes one). docs/avatars.md section 17.
- **W6 ServerObjectsLayer.**
  - **As built (G6 stage 3):** `ServerObjectsLayer` (priority 12), `ServerObjectMeshes` (worker prepares, the main-thread queue attaches; an object is shown only when every part is attached), `ServerObjectCellLights`, `ServerObjectParticles` (`AvatarParticles`, the Hidden player's bubbles); MultiMesh batches per (mesh variant, layer) with the whole buffer uploaded only when a hash of the batch's instances changed. Per-node drawing was not chosen: INSTANCE_CUSTOM (slot, light row, translucency) exists only on MultiMesh instances. The Academy draws 8 of its 23 placed objects at the arrival (the cells in view). docs/avatars.md section 17.
  - Priority 12. MultiMesh per (mesh variant, layer bucket) with whole-buffer uploads and INSTANCE_CUSTOM.
  - Worker prepares; an avatar is shown only when fully attached.
  - `ObjectLightSets` (up to 8 lights, Render::minimize_object_lighting).
  - `AvatarParticles`; `--avatar-stress=N` (dropped by the owner after wave-2 item 4).
  - Tests: an allocation test of the layer path.
- **W7 Picking and selection.** `RayPicking` (Client/Play, through the off-centre projection of D40) and `ObjectSelection`; implements `ISelectionProjector`; Use through Actions.
- **W8 Player.**
  - `CollisionStreamer` wired into the frame budget (no ILandblockLayer). Collision a host attaches itself (the headless host, or G5 pre-attaching landblocks) is left alone: the streamer builds its own only if the host detaches it while the focus still needs it.
  - `PlayerController` (priority −20; `IInputActionSink` + `IWorldPointerSink` implementation; `MovementBindings` maps retail actions to intents as `ACCmdInterp::OnAction` does, docs/physics.md §12: the run key goes to `SetHoldRun`, and the sidestep keys to `HandleCommand(SideStepRight/Left, start)`, because retail's keymap has no sidestep modifier; it subscribes to `UiInputRouter.ClientActiveChanged` and clears its keyboard movement when it turns false, as `CommandInterpreter::LoseKeyboardFocus` does).
  - `PlayerFocus` (streaming; the dungeon rule moves to `Client/Play/PlayerStreamingFocus`).
  - `ChaseCamera` (priority −5; drives FlyCamera; `ChaseCameraModel` re-expresses CameraSet SetDefaultOffsets/Closer/Farther/Rotate/MouseLook/SetInHead/UpdateCamera at 11.4186 0x00458F80/0x004586D0/0x00458890/0x00458310/0x00458D80/0x00458CE0/0x00458AE0; sweep via `PhysicsWorld.SweepViewer`; viewer cell → `InteriorController.SetViewer`).
  - `FirstPersonView`.
  - `--offline-player`; explore walk mode uses `OfflineWalker`.
  - CAMERA_DEFAULT_PIVOT_Z is 1.5, read from the exe under D4 (docs/research/retail-addresses-m2.md §1.3); built in `ChaseCameraModel`.
  - Tests: ChaseCameraModelTests, PlayerStreamingFocusTests, MovementBindingsTests (RequiresDats over 0x14000000).
- **W10 Online objects.** The player from its own CreateObject; materialise with UnHide 0x75; `WorldContentCensus` (lifestone, portals, doors, NPCs, creatures, signs).
  - **As built (G6 stage 3):** play mode owns the session's one `AvatarSet`, the player's rig binds to the avatar the adapter made from the player's own CreateObject; the Hidden script's bubbles are drawn and the UnHide script plays when the server clears the bit (`materialised` in the login check); `WorldContentCensus` classifies from the weenie description (signs by name: a labelled heuristic) and `academy_objects` is its total. Gap: no fade (`TODO(g5-translucency)`).
- **W13 Frame-time hardening.** Variant warm-ups, human base-mesh preload, burst attach within the budget, jump-frame measurement.

#### G6 session and play mode
- **W9 SessionHost and PlayMode.**
  - SessionHost: `SessionBootstrap.CreateLive`; `GameOptions` calls `SessionOptions.Parse(userArgs)` and reports its `Errors`, since the Session group is registered for the main scene; the host reads secrets and pids bytes; quit via `NOTIFICATION_WM_CLOSE_REQUEST` → `Actions.Quit` (≤ 500 ms).
  - PlayMode: M1 panels hidden, world paused until PlayerCreated, `PlayerPlacement.Teleport` (world, interiors, physics, camera, TeleportProgress) and `PortalSessionBridge`.
  - `PlayPresentationFlow` (Client) with event-stream tests.
- **W11 Online flows.** Teleports (tools, portal Use), LogOff (calls `Actions.LogOff` only; the bridge drives the machine), teardown, boot, timeout, reconnect.
  - **As built (5b slice b4, docs/session.md §26):** the log-off key is retail's own, Shift+Escape "End Character Session" (`EndSessionKey`; no confirmation yet, `TODO(g6-logoff-confirm)`); the walker has the session's `QualitiesWeenie` and a server MoveTo takes control from the keys (`PhysicsPlayerBody.ServerControl`); a tools teleport and a portal Use are the Action API's and play portal space through the existing events (no UI starts them yet: the tools panel is G7, picking is G5 W7, `TODO(g5-picking)`); a boot and a silent server end on the disconnected screen, whose button reconnects (D43). `--play-checks` runs all of it on a stand-in server (`StandInSessions.AnswerWorld`, `StandInServer`), 51 checks; the live proof is `--login-check=enter` logging off through the key (`logoff_by=key`).
- **W12 `LoginCheckRunner`** (`--login-check[=academy-exit|holtburg-laps|all]`). Runs state's scenarios with the Godot controller; DoD 15 frame measurement; the verdict line.
  - **As built (stage 2): `--login-check=enter` only.** `LoginCheckScenario` (Client, tested on the scripted server) is the script, `LoginCheckRunner` (scripts/Session/) steps it and adds the screens, the player's body, portal space's LoginComplete count and the frame times; `PlayOptions` parses it. The other values were logged TODOs at this stage (built in slice b5, below). Smoke step `login-check` needs the backend. The frame time was only reported at this stage (`g6-frame-time`); stage 3 judges it.
  - **As built (stage 3):** the frame rule is judged (no frame over 50 ms from the retail character list's third frame to the end of the wait; measured 35 to 39 ms), `academy_objects` is the census total, `materialised` and `objects_drawn` join the verdict line, and three review frames are saved under `docs/screenshots/m2/login/`. `g6-frame-time` is closed: `PlayWarmUp` and spreading the arrival's work over frames removed the 108 to 136 ms frames (docs/avatars.md section 17). Smoke step `login` runs it through `tools\play.ps1`.
  - **As built (5b slice b5, docs/session.md §27):** `--login-check=academy-exit|holtburg-laps|all` (bare: `all`) play the b2 and b3 scenarios with the game's own player (`RoutePlayHost`: the player controller owns the walker; the scenario drives its controller and audits its sink), between the settle and the log off, on the live ACE. The verdict line adds `holtburg_min_fps` (judged against 60; measured 141 to 144), `corrections` (0), `where_max_m`, `exit`, `legs`, `leg_teleports` and `teleport_frame_max_ms` (judged against 50; measured 7.8 to 12.7: `g6-teleport-frame-time` is closed, no spreading needed). A failed leg sends the character back to its arrival and logs it off. The Academy exit is taken for real (5b slice C, docs/session.md §25 and §27): the leg stamps the Sentry's quest through the mod for the Use and erases it after, the verdict is `exit=teleported` (a refusal fails the leg, and the check requires the teleport), and ACE's occasional double F751 for a portal Use shares one portal-space visit (the LoginComplete count is per visit). Smoke step `login-check` runs `all` (about 135 s). The arrival's frame at about 1 s after Enter (38 to 59 ms) is the margin of the 50 ms rule: the world start after the player's build is split over two frames, and the build waits two tunnel frames (`PlayMode.TunnelLeadFrames`) so it does not share a frame with the tunnel's first-use canvas compile (first runs: 5 of 6 failed at 50 to 56 ms before, 0 of 8 after).

#### G7 Lifestone panels
- **U7.**
  - `tools.json` and `inspector.json`, built on retail templates by ID.
  - `ToolsPanel`: `TeleportCatalog.Default().Search` (TeleportCatalog.cs:106,203), Place, Where am I with `PositionAgreement`, Add XP; refusal when not loopback; mod-missing dialog.
  - `AutomationInspector`: ActionLog rows, counters, read-only kill switch and dry run.
  - `LifestoneHotkeys` (D41). Tests: the keys are unbound in 0x14000000.
  - **As built:** docs/retail-ui.md §22 (the tools panel §22.1, the inspector §22.2; the hotkeys are F7 and Scroll Lock); the run is in §6.7.

### 5.4 Option-name table (registered by owners in their partials; L8 publishes it)

| Group (owner) | Options |
|---|---|
| Core (lead) | existing options; `--dat-dir --log-level --quit-after --workers` AlsoIn Headless |
| Rendering (lead W2-pre a) | `--fov` (Main, AlsoIn TerrainPreview) |
| Checks (lead) | `--portal-space-checks` |
| Session (state; Main\|Headless) | `--play`, `--account`, `--character`, `--server`, `--record-session[=DIR]`, `--session-budget-ms`, `--retail-net` |
| Headless (state) | `--scenario=login\|holtburg-laps\|academy-exit`, `--laps`, `--create-character`, `--summary` |
| Portal space (G1; Main) | `--portal-transition`, `--portal-space`, `--portal-space-frames[=DIR]`, `--retail-logoff-tunnel` (AlsoIn Headless; D28) |
| Audio (G1; all scenes) | `--mute`, `--volume` |
| Video capture (lead; Main) | `--record-audio=FILE` (a `.wav` under `cache/`), `--clean-hud` |
| Retail UI (G2; Main) | `--ui-scale` (an integer override for `UiScalePolicy.Choose`; 1 restores retail), `--retail-ui=off\|standin`, `--retail-ui-start`, `--retail-ui-frames[=DIR]`, `--retail-ui-only`, `--retail-disconnect` (D43), and a flag for `UiServices.RetailGlitches` (G2 names it: `UiRetailGlitches.DialogQueueing`, `A8PointTest` and `FocusLossActions`, docs/retail-ui.md §14-§15) |
| Avatars / Play (G5, G6; Main) | `--avatar-stress=N` (dropped by the owner; not registered), `--offline-player`, `--player-camera=chase\|first` (the spell viewer already owns `--camera`), `--physics-overlay`, `--motion-stepping` (D26), `--remote-ground-clamp` (D24), `--walk-route` (physics' request; G5 defines it with the offline walker, W8), a glitch flag for `MotionInterpreter.RetailNegativeRunRateCap` (false by default; restores retail's negative run-rate cap; docs/avatars.md §7), `--login-check[=…]` |

`--play` is the play switch (D9). No option takes a password, and a test enforces this. The options the end-of-wave-1 pass added to the table (`--retail-logoff-tunnel`, `--retail-disconnect`, `--walk-route`, the two glitch flags and `--ui-scale`'s meaning) are requests: each owner registers its own in its partial of `OptionCatalog`, and a name is registered once. `--portal-space-frames[=DIR]` and `--retail-logoff-tunnel` are specified exactly in docs/portal-space.md §12.

---

## 6. Wave 3, merge order and gates

### 6.1 Gates

- **G-A (every merge, checkpoint or track):**
  - build with 0 warnings;
  - `dotnet test` (default filter `Category!=Loopback&Tier!=Soak`) green, with a non-decreasing count;
  - hygiene and architecture tests green;
  - `smoke.ps1 -Backend auto` green (the login step SKIPs while the backend is down);
  - the secret scan finds 0.
- **G-B (live: server-mod, net, state, physics, G5/G6 merges):**
  - from the main checkout: `start-server.ps1` waits for `Lifestone mod ready`, and pids.json shows `ace.modReady`;
  - `dotnet test --filter "Category=Loopback&Tier!=Soak"` is green;
  - its first test asserts `@lifestone status` reports access Player and autoPromotion false, and aborts the run otherwise;
  - then `stop-server.ps1`.
- **G-C (end of each wave, final):**
  - `smoke -Backend require` (or `start`, D12);
  - `dotnet test --filter Tier=Soak` (5M fuzz, 10-minute virtual soak, 2-minute live lossy soak);
  - Exhaustive sweeps;
  - ProvenanceTests;
  - the 11.6096 citation gate.

### 6.2 Rolling merge order (merge when ready, subject to these edges)

1. W0a.1 → all wave-1 forks. W0a.2 lands before the first end-of-track merge.
2. C-net → C-state → C-portal, C-physics sketch, C-mod (each with G-A and a consumer review).
3. **Server mod, complete**, then SM8 live install in the main checkout. **No login of any kind happens before this.**
4. **Portal, complete** → W2-pre(a) → G1 forks. This gives visible result 1.
5. C-avatars → physics PH6 proceeds.
6. **Net, complete** (G-A + G-B phase A). LegalNotices.cs:59 and README.md:389 are updated in lockstep (done in the end-of-wave-1 doc pass: "network transport and message codecs").
7. C-state-offline; C-ui → W2-pre(b) → G2, G7 and later G3 fork.
8. **Avatars, complete** → W2-pre(c) → G5 forks (offline crowd).
9. **State ST1-ST15** (G-A + G-B) → G6 forks. This gives visible result 3.
10. **Retail UI core, complete.**
11. **Physics, complete** (README/LegalNotices ACE use: "movement physics port") → G5 W8 (offline walking) → **state ST16** (G-B scenarios) → G6 W11-W12.
12. Wave-2 merges in this order: G1, G2, G3, G7, G5, G4, G6. G-A each time; G-B for G5 and G6. **As built, and the owner's order for the rest:** G1, G2, G3 and G5's offline walking are merged (items 1 to 4). Next: 5a, real login to the world (G6: the session host, play mode and the portal entry), 5b, movement that ACE accepts (G5 W11 and ST16), then the in-game shell (G4) and the tools panel and automation inspector (G7). The smoke steps join as their harnesses land (L19):
    - portal-space-checks and portal-space with G1;
    - retail-ui with G2;
    - login with G6;
    - server-mod with the server-mod merge.
13. Doc pass: the §1.6 corrections not yet applied (the DoD wording amendments were applied in L13). Done at the end of wave 1 for m2-log-in.md, protocol.md, the address table and the Academy docs; §1.6 lists what is left.

Rationale for the order:
- the server mod comes first because of auto-promotion;
- portal comes next because it is the first visible result and the smallest track;
- net comes before state because state's reducer tests need the codecs;
- state comes before G6;
- physics comes before ST16 and W8.

There is no edge from avatars to net beyond C-net, because AV13 moved to G5.

### 6.3 Wave 3

1. **Adversarial review per DoD item.** One reviewer per DoD cluster: 1-2 and 12-13, 3-4, 5, 6-7, 8-9, 10-11 and 14-15. The checklist covers:
   - every retail behaviour cites function, 11.4186 and 11.6096 address;
   - no decompiled code is pasted;
   - every unknown is a loud TODO;
   - placeholders are labelled on screen;
   - no secrets appear in any output;
   - no dat-derived files are tracked;
   - port headers are present and listed in docs/third-party;
   - AC→Godot conversion happens only through AcToGodot.
2. **Fix tracks.** One branch per finding cluster, merged in this order, each with G-A, plus G-B where it touches live behaviour: transport/session → state/reducer → physics/movement → avatars/objects → portal → UI → tooling/docs.
3. **Integration:**
   - SM9 server-mod loopback scenarios;
   - the full smoke with 18 steps: build, test, server-mod, cli-help, cli-bad-argument, terrain-golden, engine-checks, interior-engine-checks, sky-checks, blend-checks, portal-space-checks, spell-showcase, golden-viewpoints, portal-space, retail-ui, fly-path, login, stray-output.
4. **DoD audit table** in m2-log-in.md "Status": evidence for each of the 15 items (test names, smoke rows, frames).
5. **Owner review** through `tools\play.ps1` in a visible window (cleared by the owner, D66). Worktrees are removed only with the owner's go-ahead. Push only when asked.

### 6.4 Cost hygiene (agent slices, from wave 2 item 5 on)

Nearly all of M2's spend is context re-sent on every model call, so an agent's cost grows roughly with the square of
how long it runs. Workflow agents were about 93% of the spend: a median of 90 calls, up to 422, with contexts up to
about 960K. These rules are defaults for the lead's judgement, never a reason to skip a build, test, smoke run, review
or the reading a change needs.

1. **Small slices.** Size each agent's task to finish in about 100 calls and under about 300K of context. Split by
   feature or file set. An agent whose slice turns out much bigger finishes its current step, commits, writes a short
   handoff in its branch (done, next, gotchas) and ends, and a fresh agent continues from the handoff. Never cut an
   agent off mid-edit.
2. **Precise briefs.** Give each agent the exact files, doc sections and acceptance checks it needs. Point to docs
   rather than pasting them in.
3. **A small lead.** Do not poll running workflows; wait for the completion notice. At each wave boundary, update this
   plan and the handoff docs, then tell the owner it is a good moment to compact or start a fresh session.
4. **Scoped reviews.** A review reads the wave's diff and the docs it touches, not the whole repository.

Added by the owner during G4 (2026-10-14), after its slices measured 119 to 166 calls each with contexts of 280K to 400K:

5. **Size by context.** The limit that matters is about 300K of context; calls are a soft guide. Splitting much finer
   only repeats each fresh agent's start-up reading.
6. **One check command.** Slices run their acceptance checks through `tools/slice-check.ps1` (the build, the default
   tests and the Godot checks a slice names, one summary line per stage, failures only), not hand-run loops.
7. **Planned parallel lanes.** The slice table marks which slices may run side by side (disjoint files, each in its
   own worktree cut from the item's branch); shared hooks land first so parallel slices mostly add files. At most two
   or three agents run Godot at once (the 50 ms checks flake under load). Parallel runs save time, not tokens.
8. **A small lead.** Merges of parallel lanes and mechanical doc passes go to a Sonnet agent; the lead checks the
   result by the diff, the test counts and the checks. Decisions go into this plan as they are made.
9. **A fresh session per item** (G4, G7, wave 3), seeded by this plan's status and the item's handoff, rather than
   compacting in the middle of an item. At every slice transition the lead prints a compact roadmap (done, now, todo).

Small habits: grep, or read large files by line range; send long build and test output to a file and show only the
failures and the summary. Each wave summary names any agent that clearly ran long, and why, so later slices are sized
better.

Measured on item 5a: stages 1 and 3 each ran about 255 calls. Stage 1 bundled play.ps1, SessionHost, PlayMode,
placement and the live screens; stage 3 profiled frame hitches over many live runs. The one-agent gate fix that
followed was briefed with the exact log lines and files, and finished in 100 calls.

### 6.5 Item 5b handoff: movement that ACE accepts, in slices

5a is merged (136c9da): `--login-check=enter` passes live, and the full smoke passes with the backend (21 of 21). 5b runs
as one worktree (`m2/g6b`) with sequential slices. Each slice is a fresh agent that reads the previous slice's handoff
(`docs/handoff-m2-5b.md` in the branch, removed before the merge). One review then reads the 5b diff only. Live rules
as in 5a: the backend runs only from the main checkout, `-AllowStaleMod` is never used, and only `accounts.test` is
used.

| Slice | Scope | Read | Acceptance |
|---|---|---|---|
| b0 (optional) | Moving objects in cells look near-black: 1 of 22 Academy lights reaches the player (D46 selection) | `scripts/World/ServerObjects/ServerObjectCellLights.cs`, `scripts/Player/PlayerCellLights.cs`, D46 | Arrival frame lit like the cell's statics; retail reference cited |
| b1 | N12 live phase B | §3.3 net N12; `tests/Lifestone.Loopback.Tests/Net/` (LivePhaseATests idiom) | `place poi` Holtburg gives F751 + F748 in 0xA9B4; kinematic walker; `where` within 0.5 m; the force-position sequence unchanged; soak in Tier=Soak |
| b2 | ST16 part 1: `QualitiesWeenie`, `HoltburgLapsScenario` (headless), the `SkillClasses` switch | §3.3 state ST16; docs/session.md §12; `Client/Movement/Routes/`; `src/Lifestone.Headless/Program.cs` | Loopback: laps ×3, corrections=0, where_max_m ≤ 0.5 |
| b3 | ST16 part 2: `AcademyExitScenario` (Use, AV14 MoveTo) | the same, plus ST10 Use | Loopback: F751 after the portal Use, corrections=0 |
| b4 | G6 W11 online flows: tools teleport, portal Use, a logoff key (`TODO(g6-logoff-ui)`), boot, timeout, reconnect (D43) | §5.3 W11; `scripts/Session/PlayMode.cs`, `SessionHost.cs` | `--play-checks` extended and green; live logoff via the key |
| b5 | W12 `--login-check=academy-exit\|holtburg-laps\|all`; teleport frames judged (`g6-teleport-frame-time`); `physics-thin-doorway`, `physics-landing-velocity` only if corrections show | §5.3 W12; docs/physics.md §15 | `[logincheck] done ok=true` for `all`, corrections=0; smoke green with `-Backend start` |

### 6.6 Item G4 handoff: the in-game shell, in slices

5b is merged (026df3c) and the video capture options are in (d8c8950). G4 (§5.3 U6, with G5 W7 first because the shell's
selection needs it) runs as one worktree (`.claude/worktrees/m2-g4`, branch `m2/g4`, `core.longpaths true`) with
sequential slices. Each slice is a fresh Sonnet agent that reads the previous slice's handoff (a file in the branch, removed before
the merge), sized by §6.4 (about 100 calls, under about 300K of context). One Opus review then
reads the G4 diff only, and one fix slice follows it. Gates: G-A, then the live gate from the main checkout
(`start-server.ps1`, the loopback tests, `smoke.ps1 -Backend require` with `login-check` and `login` passing,
`stop-server.ps1` in a finally). Live rules as in 5b: the backend runs only from the main checkout and only the lead starts
and stops it, `-AllowStaleMod` is never used, only `accounts.test` is used, the graduated test characters are never reset,
and the database is never written (read-only SELECTs through `mariadb-ace.cnf` only).

G4's screens are `GamePlay*` files in `Client/RetailUi/Screens/` (§5.2, as built). The slices also edit files of other
owners, approved by the lead and named per slice: G5's (W7: `Client/Play/`, `scripts/Player/PlayerController.cs`), G2's
`scripts/RetailUi/RetailUiLayer.cs` (`SetSelectionProjector`) and `UiStandInScreens`, G6's `PlayGameScreen`,
`PlayStatusLine`, `PlayMode` and `PlayChecks`, `Client/Play/EndSessionKey.cs`, and G1's `PortalSpacePresenter` (its two G4
TODOs). The shell's documentation is a new docs/retail-ui.md §21 that each slice extends; picking goes in docs/avatars.md
§18 (new).

**Sources (after g1).** A read-only pass mapped each shell piece to what the dats hold and what the client's code does:
`cache/research/g4-shell-sources.md` in the main checkout (gitignored; it cites the 11.4186 decompile, kept in
`cache/research/decomp-11.4186/` with its SHA-256 list, and the 11.6096 symbol table). Its findings changed g2, g4, g5 and
g6 below and led to D55 (the exe reads G4 may make) and D56 (window positions last for the session).

| Slice | Scope | Read | Acceptance |
|---|---|---|---|
| g0 | G5 W7: `RayPicking` (Client/Play, through the world lens's projection, D40) and `ObjectSelection`; `ISelectionProjector.TryProject` (§4.13) and `RetailUiLayer.SetSelectionProjector` (closes `TODO(retailui-host)`); the player controller's world click selects, retail's double click Uses through `Actions.Use` (closes `TODO(g5-picking)`) | §5.3 W7; §4.13; `scripts/Player/PlayerController.cs`; `scripts/World/ServerObjects/`; `scripts/Session/PlayMode.cs`; docs/avatars.md §17 | Client tests for the ray and the pick order; `--play-checks` gains a pick-and-Use of the stand-in portal; the test baseline is recorded in the handoff |
| g1 | The GamePlay root (D36): `GamePlayScreen` replaces `PlayGameScreen` and the game stand-in, builds 0x10000006/0x10000495 at full window, finds the SetupChildren ids (`UiLayoutEnums.Gameplay`); SmartBox 0x1000049A with viewport 0x10000436 holding the tunnel source; `GameViewRect` published as the whole window (D40 as amended); the shell composited while the "entering world" dialog shows; windows without content yet are labelled placeholders | §5.3 U6; D36, D40; retail-ui.md §1, §11, §17; retail-ui-data.md §2, §3.2 | `[uicheck]`: the shell's windows at the anchored places of retail-ui-data §3.2 at 1920x1080; `--play-checks`: the shell shows over the world and portal space plays in the SmartBox; no frame shows the world without the shell after Enter |
| g2 | The spew box (type 26 only, identical text replaced, capped) registered on `UiNoticeRouter`, which retires the D32 notice line and, where the shell covers it, the play status line; the main chat and the four floating chats (0x10000505, 0x1000050E-0x10000510) with retail's per-window text-type masks and colours (D55 reads) and the talk-focus popup menu (retail has no chat channel tabs): the session's speech and system lines, `#lifestone/1` lines filtered, the entry sends `Actions.Talk` | §5.3 U6; D32; retail-ui.md §7, §9, §11; the session facade's chat events (§4.7) | Client tests (spew replacement and cap, the filter, Talk); `--play-checks`: "In Portal Space - Please Wait..." in the spew box, a stand-in speech line in the chat, a typed line sent as Talk |
| g3 | Vitals 0x100005FA and 0x100006D5 from `Self.Qualities` (D16: PlayerDescription, PrivateUpdate*); the radar 0x100006D2 (the player's heading, nearby objects' blips) | §5.3 U6; D16; §3.3 state ST8; retail-ui.md §9 (Meter) | Client tests over a scripted PlayerDescription and a vital update; `--play-checks`: the meters at the stand-in values, a blip for the stand-in portal |
| g4 | The toolbar and the panel host: every gameplay panel's button and the combat and casting buttons open the NeedsGameplay message (`NotInM2`, m2 §5.3's list); the indicators (LinkStats) | §5.3 U6; m2-log-in §5.3; retail-ui.md §11 (`NotInM2`), §15 | Client tests that each button names its panel; `--play-checks` presses one of each kind |
| g5 | Picking as retail (the click's ray tested against each drawn part's drawing sphere, then its polygons, a polygon hit winning: `Render::pick_ray`, `Render::GfxObjUnderSelectionRay`; the Setup's selection sphere only for the indicator's box; closes `TODO(g5-pick-bounds)`). Selection with health: the selected object's name and health (`Actions.QueryHealth`, D16) and the vivid target indicator through `ISelectionProjector` (closes `TODO(portal): G4's vivid target indicator`) | §4.13; D16; `scripts/PortalSpace/PortalSpacePresenter.cs`; g0's handoff | Client tests of the health flow; `--play-checks`: selecting the stand-in NPC shows its name, health and the indicator; the portal cue hides it |
| g6 | Character information (read only), options (honoured settings live for the session, the rest ghosted, D42), the logout confirmation (`ID_Client_EndCharacterSessionConfirm` → `Actions.LogOff`) on the options panel's Exit button (the toolbar has no logout button), as `gmGamePlayUI::MakeLogoutConfirmationDialog`; retail's Shift+Escape logs off without asking, so `TODO(g6-logoff-confirm)` closes as retail-exact; window positions last for the session (D56) and the UI's busy count from portal space (closes `TODO(portal): G2 busy count`) | §5.3 U6; D42; `Client/Play/EndSessionKey.cs`; docs/session.md §26 | Client tests; `--play-checks`: logoff asks, No stays, Yes logs off; the options list names what is honoured |
| g7 | The shell's checks and review frames at 800x600 (retail's native size; the owner's rule of 2026-10-14: tests, checks and frames stay there) and, opt-in with `-AllSizes`, 1920x1080 (scale 1) and 2560x1440 (scale 2); `--login-check` judges the shell (additive verdict fields: the shell up, vitals filled, the spew box heard portal space); review frames of the shell in the Academy under `docs/screenshots/m2/login/`; smoke wiring | §4.14; docs/session.md §27; `tools/smoke.ps1` | smoke green with `-Backend auto`; the frames at 800x600; the verdict fields documented |
| review | Opus, high effort: the G4 diff and the docs it touches only | this section, §5.3 U6 | Findings ranked; then one Sonnet fix slice |

**As run.** The slices ran as planned except for four changes. g3 split into g3a and g3b, and g5 into g5a and g5b, when the first
reading of each was bigger than one slice. g5b and g6 ran in parallel with g5a, each in its own worktree, and were merged
(df9ac97, f2efe5c). `tools/slice-check.ps1` (94f4250) was added after g6 so that every later slice and the lanes ran one command for the
gate. g7 (4b00c11) was the last slice. One Opus review read the diff (0 blockers, 8 should-fix, 10 nits; all 8 should-fix were
fixed, the nits R11 and R16 were left), and its fixes ran as two lanes in parallel, A (0f5533c) and B (beb4493, merged in e924bec),
not as one slice. The live gate then found three things the offline gates could not and three live-gate fixes followed: 2c537d1
(Godot's light-culler ERROR from the portal zoom's extreme frustum, older than G4; the canvas pipeline compiled at the arrival;
the vitals' inputs), 52adf0b (the PlayerDescription frame, and the vitals' maximums once the 138 enchantments of the graduated
test character apply) and a69df53 (the shell's first construction landing in the arrival window, so its warm-up is staged over
frames and the shell's own frame no longer paints). Gate G-A was green (build with 0 warnings, 6185 default tests, smoke with
`-Backend auto`); the live gate from the main checkout's backend passed: loopback 15 of 15, then `smoke.ps1 -Backend require`
21 of 21 on a69df53, with `login-check` ending `shell=ok vitals=50/100/100 spew_portal=yes vitals_window=stacked`
(`enter_to_walk_max_ms=28.8`; `login` 38.7), secrets=0. The sources research (read-only) took 74 calls and 322K of context.

| Agent | Calls / context | Agent | Calls / context |
|---|---|---|---|
| g0 | 70 / 213K | g5b | 166 / 360K |
| g1 | 119 / 338K | g6 | 153 / 400K |
| g2 | 160 / 390K | slice-check | 14 / 100K |
| g3a | 150 / 284K | g7 | 136 / 270K |
| g3b | 131 / 321K | review (Opus) | 105 / 305K |
| g4 | 144 / 323K | fix lane A, lane B | 111 / 248K, 109 / 257K |
| g5a | 47 / 169K | live fixes 1, 2, 3 | 109 / 255K, 99 / 273K, 104 / 298K |

Four ran long. g2 made the exe reads of D55 and needed a retry. g5b and g6 held the largest contexts, g6 because of the 49 options
and the character report. g1 took longer over the "entering world" dialog.
The two merges took 21 / 101K and 26 / 80K.

Open named TODOs after G4, none blocking: `g4-window-layout-save` (retail's two stores for window positions, D56),
`g4-erase-exe`, `g4-vitals-layout`, `g4-link-units` and `g4-link-offline` (the link indicator),
`g4-enchantment-updates`, `g4-enchantment-affects-skills`, `g4-quality-filter`, `g5b-heading`, `g6-options-save`, `g6-option-effects` and `g6-options-buttons`, `g6-character-info-augmentations`, `g6-duration-format` and
`g6-busy-cursor-image`. The chat's, radar's and toolbar's smaller ones are listed with their windows in docs/retail-ui.md §21.

### 6.7 Item G7 handoff: the Lifestone panels, in slices

G4 is merged (2a93b95). The owner authorised G7 and wave 3 to run overnight (2026-10-15) up to the hero video. G7 (§5.3 U7;
DoD 10 and 13) runs as two parallel lanes (§6.4 rule 7) with disjoint files, each a fresh Sonnet agent in its own worktree
cut from the branch `m2/g7` (lane B in the branch `m2/g7-insp`), then a Sonnet merge, one Opus review of
the G7 diff, one fix slice, the gates (G-A, then live from the main checkout) and the merge. Briefs carried the slice rules of
a handoff file (removed before the merge). Everything at 800x600 (D57). Budget estimate: about 1.5-2% of a week.

| Lane | Scope | Read | Acceptance |
|---|---|---|---|
| A tools | `tools.json` (R9 format, retail templates by ID) and `ToolsPanel` (Client/RetailUi/Lifestone/): Places (`TeleportCatalog.Default().Search`, ACE points of interest and the curated places, a teleport through `PlaceRequests` and portal space), Where am I (`PositionAgreement`), Add XP (confirmed in the chat); every use in the chat and the action log; refusal off loopback; the mod-missing dialog; `LifestoneHotkeys` (D41: keys unbound in gmDefaultMap 0x14000000) opening the tools panel and the inspector (lane B's `Open`); the embedded-resource line for `Ui\Layouts\*.json` | §5.3 U7; ST12; DoD 10; retail-ui-data §9; retail-ui §21 | Client tests (search, the requests, refusal, the hotkeys unbound in 0x14000000); `--play-checks`: open with the hotkey, search, place a teleport (stand-in `place poi`), Where am I, Add XP |
| B inspector | `inspector.json` and `AutomationInspector`: the ActionLog's rows (ring of 1,024), counters, the kill switch and dry run shown read-only; exposes `Open()` for lane A's hotkey | §5.3 U7; ST9; DoD 13; retail-ui-data §9 | Client tests (rows, counters, read-only state); `--play-checks`: the inspector shows the check's own actions |
| review | Opus, high effort: the G7 diff and the docs it touches only | this section | Findings ranked; then one Sonnet fix slice |

**As run.** The two lanes ran in parallel as planned: A, the tools panel (fbf69a7), and B, the automation inspector (db9e229). A Sonnet merge (7c6298e)
kept lane A's lazy layout loader and dropped lane B's second one, wired the inspector hotkey (`Hotkeys.OpenInspector`) and made the review frames 09 (the
inspector) and 10 (the tools panel). A ghost fix followed (140f6bf): the tools panel stayed painted after F7 because the shell root draws nothing and did not
erase, so the root now erases (docs/retail-ui.md §21.3). One Opus review read the diff (0 blockers, 5 should-fix S1 to S5, 9 nits); all were fixed except the
nit S10, the stand-in's replies not being labelled on screen (e833e74): Where am I now compares with the physics body, the embedded layouts fail loudly,
the model is back in the retail UI's contract, and the inspector's rows are built over frames. Two live fixes came from the lead's gate: a2ee528 (the key bindings
are built at start-up, the embedded layouts are registered on a worker) and c72ef44 (the real in-game shell is built in stages over frames, and portal notices are
held until the spew box exists; docs/session.md §27). One agent ran long: lane A (195 calls / 471K) because of the tools panel's first-open cost (about 35 ms for
the dialog templates' fonts and glyphs, which no warm-up on a worker removed) and the hotkey map; lane B took 114 / 313K, near the 300K guide. Gate G-A was green (build with 0 warnings, 6235 default tests, smoke with `-Backend auto`).
The live gate from the main checkout's backend: loopback 15 of 15 (e833e74), then `smoke.ps1 -Backend require` on c72ef44, where every step passed except one
`login-check` run whose frame at the world-entry message burst measured 53.0 ms (`stage=entering`, 1.3 s after Enter, before the tunnel; no collection, no pipeline
compile); two reruns of `login-check` and `login` measured 43.3 / 32.6 ms and 37.5 / 31.8 ms and passed, and the lead accepted the gate on the reruns
(`TODO(login-burst-frame)`, docs/session.md §27), secrets=0. Open named TODOs, none blocking: `login-burst-frame`, `g7-inspector-layout`,
`g7-inspector-unwired` (only for a host that wires no hotkey) and the stand-in's `g7-standin-where` (docs/retail-ui.md §22).


### 6.8 Wave 3 as run

Wave 3 (§6.3) ran on 2026-10-15, overnight, from master 53abd63 (G7 merged) on the integration branch `m2/w3`. Nothing was pushed.

**Reviews.** Six read-only reviews, one per DoD cluster (1-2 and 12-13, 3-4, 5, 6-7, 8-9, 10-11 and 14-15), each an agent that read the cluster's code and
docs against the §6.3 checklist and the DoD wording, and wrote a report with ranked findings and a "DoD evidence" section. They found 64 findings and 0 blockers
(most should-fix, the rest nits; a few flagged UNCERTAIN). The strongest were: Add XP and the `places` verb had never run against the real ACE (10-11-14-15-1);
the live gates judged neither the portal-space timeline, the logoff tunnel nor any sound (5-1); the checks did not show that everything ACE creates is drawn
nor Holtburg's content (6-7-1); Jump had no live evidence (8-9-2); the live create, delete and restore through the retail screens had never run (3-4-1).

**Triage.** The lead sorted the findings into a fix plan: lanes with disjoint file sets, a "set aside" list for the owner (changes to the test characters, a
decompile check, DoD wording), a deferred list with the DoD item each leaves partly met, and a list of live runs only the lead can do. One finding was
dropped (the tunnel colour was accepted by the owner on 2026-10-13). Docs belonged to lane D alone; lanes A to C handed over doc notes.

**Lanes** (fresh agents, one worktree each, merged in the order of §6.3 step 2 as they finished: A, C, B, D):

| Lane | Branch | Content | Size |
|---|---|---|---|
| A session | `m2/w3-a` | The headless host is always an automated run and signs in only to the test account; loopback wording; the replayer expires parked messages after each frame; `play.ps1` passes `--server`; the idle and SM9 loopback tests (`ModVerbsLiveTests`) | 22 files, +792 |
| B gate | `m2/w3-b` | The login check judges the portal rules (tunnel and portal seconds, the logoff tunnel held, the whooshes and the hum counted), the drawn census of every placed kind, the jump leg and the movement audits; four review frames; the burst frame names its parts; the run-by-default seam | 15 files, +1,202 |
| C world | `m2/w3-c` | Physics TODO logs, the position resend gated on presence, run-by-default, the play-mode HUD, bounds on lights, notices and particles, translucency 1, portal audio and hold warnings, citations | 26 files, +401 |
| D1 UI | `m2/w3-d` | The create flow enters the world at once, Exit Game confirms then plays the epilogue, the mid-air refusal as a named TODO, the UI sound sink TODO and an engine check, the tools panel's refusals in the chat, 11.4186 addresses | 30 files, +410 |
| D2 docs | `m2/w3` | Lane notes, `docs/third-party/retail-ui.md` and the camera and picking rows of `docs/third-party/avatars.md`, stale texts of session, portal-space and physics docs | 13 files, +159 |

Across the whole branch: 18 commits, 116 files changed (+3,718, -246). The run-by-default wire, which lanes B and C each needed in the shared `PlayMode.cs`, was joined in a commit of its own (c5aadf2).

**The gate fixes found live** (the lead ran the live runs after the merges):
- the SM9 refusal test used a cell the mod accepted; it now uses a cell the cell dat lacks, and the restore finishes a half-done arrival first (ca61da9);
- the login burst: the 51 ms frame was not the message burst itself but the first PlayerDescription, whose decode (21 ms) and reduce (16 ms) compiled cold on the
  game thread (0.23 ms warm). `SessionJitWarmUp` compiles that path on a worker at start-up, and any message over 5 ms and its subscribers over 1 ms are named in the log;
- the tunnel check: the rule said the tunnel lasts 5 s, but portal space's login sequence is 1 + 2 + 1 s of tunnel and 1 s of the world expanding, so the check now judges
  `portal_seconds` at least 5 and `tunnel_seconds` at least 4; and the black backdrop that holds the world while the shell is built covered the tunnel's first frames, so it now
  yields to the tunnel (`tunnel_covered_frames` judged, at most 1) (4e9f4a5).

**The gate numbers.** Build with 0 warnings; 6,311 default tests passed, 0 failed, 8 skipped; `smoke.ps1 -Backend require` with every step passing but `play-checks`, whose
only failure was its internal 12 ms UI-step sub-budget at 12.7 ms (no frame over the DoD's 50 ms), and the lead's rerun of `play-checks` passed 110 of 110 with a longest frame
of 21.3 ms; loopback 22 of 22; the soak tier 5 of 5; secrets=0. The verdict fields are copied in m2-log-in.md, Status (`enter_to_walk_max_ms` 37.0 and 30.0, `corrections=0`,
`where_max_m=0.03`, `tunnel_seconds` 4.5 and 5.2, `portal_seconds` 5.5 and 6.2, `logoff_tunnel=held`, `portal_sounds=ok`, 2 jumps).

**What remains.** The DoD audit table (m2-log-in.md, Status) gives each of the 15 items its verdict: items 1, 2 and 14 met; 3, 4, 6 and 10 partly met; the rest met with named gaps.
Eight owner decisions were open (listed there; decided as D59-D65), and the owner's review through `tools\play.ps1` in a visible window (§6.3 step 5; cleared, D66) and the hero video follow. Worktrees are
removed only with the owner's go-ahead; nothing is pushed until asked.


### 6.9 The decisions follow-up (D59-D65), in slices

The owner settled the wave-3 decisions on 2026-10-15 (D59-D65, master 175c008). This item builds what they require, so DoD 3, 6 and 10 can move from partly met to
met: the purge verb and a live throwaway leg (D59, D61, D65), CharGen's idle loop (D60) and the materialise fade (D63, D45). D62 and D64 needed only the DoD
amendments already made. Three lanes run as fresh Sonnet agents (§6.4 rules 1-9), each in its own worktree cut from the integration branch `m2/dec`
(`.claude/worktrees/m2-dec-p`, `-c`, `-f` on `m2/dec-p`, `m2/dec-c`, `m2/dec-f`), with a handoff file per branch, `docs/handoff-m2-dec-<lane>.md`, removed before the merge.
Slices in a row read the previous slice's handoff. Wave A runs p1, c1 and f1 side by side (only c1 runs Godot); wave B runs p2 and f2 side by side (two Godot agents).
Then a Sonnet merge into `m2/dec`, one Opus review of the `m2/dec` diff, one Sonnet fix slice, the gates and the merge. Everything is at 800x600 (D57). Budget estimate:
about 1.5% of a week.

The throwaway is `Lifestone Throwaway` on the test account; the graduated characters (`Lifestone Smoke`, `Lifestone Loopback`) are refused by name and stay as they are.
A pending-deletion character cannot enter the world and the purge is a chat verb, so the leg asks for it from `Lifestone Smoke`, which only enters and leaves.
ACE frees a name as soon as its character is pending deletion (`ShardDatabase.IsCharacterNameAvailable`); the purge removes the rows, so the account is left as it was.

| Slice | Scope | Read | Acceptance |
|---|---|---|---|
| p1 purge verb | `@lifestone char purge "<name>"`: the request, parser, wire usage, reply and JSON context (server/mod/Shared); `ToolRouting` (player scope: the caller is in the world); the rules as pure Core code: refuse the graduated names, a name not on the caller's account, the caller itself, a character that is online, and one that is not pending deletion (`DeleteTime` 0 and not `IsDeleted`); the ACE path in `Verbs/CharVerb.cs`: a read through the serialized shard queue first (so the throwaway's logoff saves have landed), `PlayerManager.ProcessDeletedPlayer` on the world's action queue, then `ShardDatabaseOfflineTools.PurgeCharacter` off the world thread, one reply with the counts; ported lines carry headers (`docs/third-party/`); the client's `PlayerTools` call and the stand-in's answer; a live test `ModCharPurgeLiveTests` (headless: create the throwaway through the Action API, the refusals, delete, purge from `Lifestone Loopback`, the list as before), run by the lead | D59, D61; m2-log-in §7 decisions 13 and 15; server/mod/README.md; ACE `CharacterHandler.cs` (delete, restore), `Session.CheckCharactersForDeletion`, `PlayerManager.ProcessDeletedPlayer`, `ShardDatabaseOfflineTools.PurgeCharacter`, `@deletecharacter` | `tests/Lifestone.ServerMod.Tests` (parser, wire, routing, every refusal in order); the mod builds (`server\build-mod.ps1`, no install); `slice-check.ps1 -Stages build,test` |
| p2 throwaway leg | `--login-check=throwaway` (not in `all`): an engine-agnostic scenario (Client/Session/Scenarios) over a small screen-action seam, and its Godot driver in a file of its own that clicks the retail screens: CharGen's six pages with ACE's name errors first (`Lifestone Smoke`: name taken; an invalid name), both in retail dialogs, then the throwaway; it enters at once (D65); the tools panel by its hotkey: Where am I, Add XP (2,500, confirmed in the chat; D61), a Places teleport through portal space; log off; on the character screen delete (typing "delete"), restore, delete again; enter `Lifestone Smoke`, purge, log off; the list equals the list at the start; quit. A leftover throwaway from a failed run is deleted and purged first and logged as `leftover`. One verdict line (`throwaway=purged name_errors=2 xp=added where_m= place=teleported deletes=2 restores=1 list=same` and the frame fields); smoke step `login-throwaway`, run with `-Backend require` only; docs/session.md section | p1's handoff; D59, D61, D65; DoD 3 and 10; `RetailUiChecks.CheckLoginFlowAsync` (delete, restore), `RetailUiFramesRunner` (CharGen clicks), `PlayChecks.Tools.cs` (the panel), `LoginCheckRunner.Install`, `tools/smoke.ps1` login steps | Client tests of the scenario with a fake seam (every stage, every failure, the leftover path); the stand-in path where it supports it; `slice-check.ps1 -Steps play-checks`; live by the lead |
| c1 CharGen loop | Retail's animation by default: `gmCG3DView::StartAnimation` (0x004EF290) loops the view's animation DID from frame 0 at 30 fps, `StopAnimation` (0x004EF2D0) holds the rest DID at 0 fps; started from `gmCGSummaryPage::InitializePage` (0x0047BFB0) and `gmCGAppearancePage::ZoomOut` (0x0047D400), stopped by `ZoomIn` (0x0047D2B0), and started or stopped by the zoom state in `gmCGAppearancePage::Update` (0x0047ECA0; the lane found that `SetSelection` calls neither); the Olthoi heritages' own DIDs; the idle emotes stay unplayed. `Cg3DViewModel`, the CharGenSetup seam, `CreaturePreview` if needed; closes `TODO(retailui-chargen-anim)`; the frames runner pins the animation's time so the frames stay comparable; docs/retail-ui.md and docs/avatars.md (AV12) | D54, D60; m2-log-in §7 decisions 12 and 14; the 11.4186 decompile `gmCG3DView.c` and `acclient.c` around the pages (re-expressed, cited by 11.6096 address) | `CharGenViewTests`, `CharGenAppearanceTests` (summary loops, zoom in holds the rest, zoom out and a selection while zoomed out loop again); `--retail-ui-checks` judges a pose change on the summary and none when zoomed in; `slice-check.ps1 -Steps retail-ui,retail-ui-checks` |
| f1 fade data | The UnHide script 0x3300032F's Transparent hook (start, end, time) and the Hidden script's, read from the dats and pinned in `AvatarHiddenTests`; the client side ramps `PartTranslucency` over that time for objects and the player, frame-rate independent (`EffectObject`); docs/avatars.md §17-§18 and m2-log-in §4.9 get the numbers | D45, D63; docs/avatars.md §17-§18; m2-log-in §4.9 | Client tests (the hook values, the ramp at two frame rates, a re-hide mid-fade); `slice-check.ps1 -Stages build,test` |
| f2 fade drawn | A blended twin for opaque surfaces while an instance's translucency is between 0 and 1 (`SurfaceShaders`, the mesh libraries, `ServerObjectsLayer`, `PlayerView`), as retail draws it; the twin pipelines warmed at start-up so the arrival compiles nothing; closes `TODO(g5-translucency)` for the materialise fade (the first-person fade keeps its own named TODO for M3); the login check reports `fade_ms` and judges the arrival frame under 50 ms; a review frame `02a-materialising` mid-fade; a `--play-checks` sub-check on the stand-in | f1's handoff; D45, D63; DoD 6; `TODO(arrival-pipelines)` | `--play-checks` (the fade's frames, none over 50 ms); `slice-check.ps1 -Steps play-checks,portal-space-checks -Rerun`; live by the lead |
| merge | Sonnet: `m2/dec-p`, `-c`, `-f` into `m2/dec` (only `LoginCheckRunner.cs`, `PlayOptions.cs` and `smoke.ps1` are expected to meet) | this section | Build with 0 warnings; the default test count never decreases |
| review | Opus, high effort: the `m2/dec` diff and the docs it touches only; the purge's guards first | this section | Findings ranked; then one Sonnet fix slice |

**Gates.** G-A from the `m2/dec` worktree: `slice-check.ps1 -Smoke` (build, default tests, `smoke.ps1 -Backend auto`). Then, with the owner's go-ahead,
`server\setup-server.ps1 -ModOnly -ModSource <the m2/dec worktree>\server\mod` installs the new mod. The live gate runs from the main checkout's backend (the lead
alone starts it and stops it in a finally): loopback (with `ModCharPurgeLiveTests`), `smoke.ps1 -Backend require` with `login-check`, `login` and `login-throwaway`
passing, the arrival frame under 50 ms, secrets=0. Merge to master; then, with the owner's go-ahead, a plain `setup-server.ps1 -ModOnly` points the installed
mod back at `server\mod`. Docs: m2-log-in.md's DoD audit rows 3, 6 and 10, this plan's status and a short "As run" here; the memory.

**As run** (2026-10-15, from master 421632c on the branch `m2/dec`). Wave A ran p1 (fd7464f), c1 (d34bc25) and f1 (65cbf12) side by side, wave B p2 (6ab5da1, 5488abb) and
f2 (5e15b60). f1 found that the client already ramped the fade (UnHide 0x3300032F: translucency 1 to 0, linear, over 0.75 s; Hidden 0x33000331: 1 at once), so only tests and docs
changed there. c1 found that `gmCGAppearancePage::SetSelection` calls neither start nor stop: `gmCGAppearancePage::Update` (0x0047ECA0) does, by the zoom state (D60 and the c1 row
above are corrected). The lead merged the three lanes (conflicts only in docs/session.md and the `--login-check` help, both kept). One Opus review of the diff found 1 blocker,
4 should-fix and 7 nits and no way for the purge to reach another account's, a graduated, an online or an unmarked character: the blocker was `--retail-unhide-flash` not wired into the
game; the should-fixes were a stale expired row chosen before the visible pending one, a stray invalid-name character the next run would not clean up, the smoke step's kill
timeout shorter than the leg's own recovery, and no check that the blended twin is really drawn. One Sonnet fix slice closed them all (d892d3c, 78b2c23, 16afcfa) but the nit on eager
server-object twins (`TODO(serverobjects-lazy-twins)`). Two agents ran long: p1 (102 calls, about 370K) and p2 (148 calls) because of the stand-in and the screen driver; the stand-in
cannot serve the whole leg offline (`TODO(throwaway-standin)`). Gate G-A was green (build with 0 warnings, 6,457 default tests, smoke with `-Backend auto`). With the owner's
go-ahead, `setup-server.ps1 -ModOnly -ModSource` installed the branch's mod. The live gate from the main checkout's backend: loopback 23 passed and 1 skipped (`ModXpLiveTests`, held off the
graduated characters by D61), `ModCharPurgeLiveTests` among them; `smoke.ps1 -Backend require` passed every step but `login-check`, whose one frame of 75.0 ms came on a leg's returning
teleport inside the tunnel with 5.4 ms of process time and no pipeline compile or attach (not this item's code); its rerun passed (`teleport_frame_max_ms=20.8 enter_to_walk_max_ms=28.0
corrections=0 where_max_m=0.03 fade_ms=753`), and the lead accepted the gate on it, as for G7. `login` gave `materialised=true fade_ms=752`; `login-throwaway` gave
`throwaway=purged name_errors=2 xp=added where_m=0.00 place=teleported deletes=2 restores=1 list=same leftover=no`, no frame over 50 ms; secrets=0. Then the merge to master, and,
with the owner's go-ahead, a plain `setup-server.ps1 -ModOnly`.

---

## 7. Visible-results checkpoints (in order; day numbers are agent-day estimates)

| # | What the owner sees | Needs | Estimate |
|---|---|---|---|
| 0 | Not on screen yet: the test-log census. 101 layouts decode, the UIASSET chain resolves, 786 waves and 190 sound tables decode | L2, L3, PS2, PS9, R1 | day 5-6 |
| 1 | **Portal space offline.** `tools\godot.ps1 -Run -GameArgs:'--portal-space'` holds the purple tunnel. Teleport-panel picks play the full sequence: enter whoosh, expand from the centre, spin, roll and hum, the "In Portal Space" placeholder line, a 2 s or longer hold, collapse, exit whoosh, the world grows in. Four review frames go to `cache/smoke/portal-space/` | portal PS1-PS11, W2-pre(a), G1 W1-W5 | day 14-17 |
| 2a | **Retail screens as PNGs from the dats.** Data check, disconnected, character management, dialog types at 800×600 and 1920×1080 in `cache/ui-frames/`, with retail fonts and strings and stand-in names | L2, L3, R1-R13, R15 | day 12-14 |
| 2b | **Retail screens live offline.** `--retail-ui=standin`: data check animates, character list (select, delete with typed confirmation, restore, the enter dialog), disconnected screen | C-ui, W2-pre(b), ST7b, G2 U2-U4 | day 21-23 |
| 2c | **Character creation offline.** All six pages; the 3D character on Appearance and Summary (placeholder until avatars and CreatureMode exist) | G3, R18a, AV12, G1 W3 | day 24-26 |
| 3 | **Login as far as the tunnel.** `tools\play.ps1` starts the backend, then the retail data check during DDD, the server's character list, Enter, and the tunnel growing in | SM1-SM8, N0-N6, ST1-ST15, G1, G2 (or a labelled placeholder entry), G6 W9, L18 | day 20-23 |
| 4a | **Crowd offline** (dropped by the owner after wave-2 item 4). `--avatar-stress=150`: clothed, animated humans at Holtburg with the fps on F3 | avatars AV1-AV11, W2-pre(c), G5 W6 | day 22-24 |
| 4 | **The world with server objects in the retail shell,** drawn in its full-screen SmartBox. A new character materialises from pink bubbles in the Academy among NPCs and creatures; Holtburg shows lifestone, portals, NPCs, signs and doors; chat, vitals, radar, spew box | N7, N10, ST8, G5 AV13/W10, G4 | day 26-29 |
| 5a | **Walking offline.** `--offline-player`: run, walk back, turn, sidestep, jump and fall around Holtburg and into buildings, chase and first-person cameras | physics PH1-PH7, avatars C-avatars, G5 W8 | day 24-26 |
| 5 | **Walking online.** ACE accepts every move; tools-panel teleport; Academy exit-portal Use; where-am-I within 0.5 m; logoff back to the list. `[logincheck] done ok=true` | physics complete, ST16, G6 W11-W12, G7 | day 29-32 |

---

## 8. DoD traceability

| DoD | Delivered by |
|---|---|
| 1 One command to play | L18 play.ps1; SM6 (start-server readiness, `Get-LsBackendState`, `-AceOnly`); N0 LoopbackPolicy + `AcSession.Create` gate; ST2 gate wrapper; ST13 SessionBootstrap; G6 W9 |
| 2 Log in | N1-N6 (handshake, ISAAC, acks, NAKs, TimeSync, ConnectResponse race, DDD with run-time iterations, boot/mismatch failures); L2 (Iterations.Local, RequireLocal); ST5 (PlayAccounts, `--account`); SM5 (secrets v2); ST7 (DataCheck); G2 U4 DataPatch/Disconnected; password: L8 (no secret options, no echo), N2/N5 (zeroed buffers, sentinel), ST13 (hygiene tests), L12 (scan), ST6 (allowlist), N11 (fixture guard) |
| 3 Characters | N3, N6; ST10 (create intent → 55 skills, delete/restore); R8, R18a; AV3, AV12 (CharGen's 3D view); G2 U4 (retail-exact list, no 3D view, D35); G3 U5 |
| 4 Retail UI | R1-R16; G2 U2-U4, U9; G4 U6 (NeedsGameplay); PS10 ISoundSink + G1 W2 (UI sounds); R7/R14 (key bindings); R5/R6 (fonts, strings); R7 (cursors) |
| 5 Portal space | PS1-PS11; W2-pre(a); G1 W1-W5 (offline in explore mode); ST10 LoginComplete guard; PS11 bridge; G6 W9-W11 (login, teleports, logoff); G4 spew box and smartbox viewport |
| 6 Server objects | N7 codecs; ST8 (SmartBox semantics); AV1-AV9, AV11, AV14; PS11 HiddenTransition; PH8 ground probe; W2-pre(c); G5 AV13, W6, W10 (content census) |
| 7 Your character | AV5, AV10; PH5-PH7; G5 W8 (chase camera orbit and zoom, first person), W10 |
| 8 Movement ACE accepts | PH1-PH9 (cadence PH7, routes and MovementAudit PH9); AV5; ST11 MovementChannel; ST12 where/PositionAgreement; ST16 scenarios; N7; SM3 `where`; G5 W8; G6 W11-W12 |
| 9 Teleports | ST10 Use; ST12 Place; SM3 `place`; AV14 MoveTo (out-of-range Use); G5 W7 picking; PS6/PS11; G6 W11 |
| 10 Player tools | SM1-SM4, SM8; L1 ToolsWire link; ST12 PlayerTools; G7 tools panel; ST9 ActionLog |
| 11 Log off | ST10 (sole F653 sender + chat line + LogOffRequested); PS7 LogOffSchedule; N6 LogOff/Quit; G6 W11 teardown and quit |
| 12 Robust transport | N4, N8, N11-N12 (soak with the net-counter oracle); SM2 `net` verb; ST13 Reconnect; G2 Disconnected; D43 |
| 13 Automation A0 | ST1-ST16; N0/N3 tap and dispatcher; L11 architecture scan; G6 SessionHost; G7 inspector |
| 14 Tests | Net.Tests (N1-N10); ST14 replay; R1-R16; L9 infrastructure; L12/L19 smoke steps; G1 W5; G2 U9; G6 W12; SM1-SM4, SM9 |
| 15 Frame time | G1 W1 FrameBudgetShare; G5 W6, W13; AV11 / PH3 / PS5 / ST8 allocation tests; R11 dirty-rect compositing; G6 W12 measurement |

---

## 9. Risks and de-risking spikes (priority order)

| # | Risk | Spike or mitigation |
|---|---|---|
| 1 | **First login creates an Admin.** Mods initialise before the flag is computed (Program.cs:158 vs :250; DatabaseManager.cs:33,58) | **SP1 (day 3-5):** SM2 minimal mod + sentinel Admin (D11) + live boot in the main checkout. Check the ACE log for `auto-promotion is off` and `mod ready`, and pids.json for `modReady`. BackendProbe, the smoke login step, play.ps1 and both hosts refuse without modReady. The first loopback test asserts Player and aborts otherwise. No manual login before this. |
| 2 | **Contract churn.** The track plans defined each seam two to four times | §1.4 owner table; checkpoints with consumer review; additive-only rule after the fork; a compile-only consumer check at each checkpoint. |
| 3 | **First live login end to end** (ConnectResponse race, DDD, F658, sequencing) | **SP2:** N0-N5 against FakeAce, then live to character select on accounts.test. Tell the owner beforehand that no Windows Firewall or UAC prompt may appear, and stop if one does. |
| 4 | **Physics closure and cost.** Null children in BSPs (BSPNode.cs:146-166 dereferences without checks); ACE allocates per step | **SP3:** PH2 census + PH3/PH4 sphere drop over all 576 Holtburg cells and an Academy floor; 0 bytes per step. |
| 5 | **Retail UI render path cost** (software compositor at 1080p) | **SP4:** compose data-patch and character management at 1920×1080. Measure full and dirty composites and tile uploads, and Godot readback byte-exactness. Fallback: per-element GPU quads behind the same `IUiRegionView` walk. |
| 6 | **Godot lens and isolation.** 179° Camera3D cap, near 0.00025, SubViewport composition, global-uniform lighting, Dummy audio | **SP5:** a frustum projection at f = 0.001, centred and off-centre (D40); reverse-Z depth; ByteSpaceBlending on the SubViewport; CreatureMode globals against a different world sun; AudioStreamWav at 11,025 Hz under Dummy. |
| 7 | **CharacterCreate rejected** (credit or skill maths) | **SP6:** after SP2, a live create with 55 SACs → F643 Ok, cross-read with ACE PlayerFactory.cs:141-209, 606-631. |
| 8 | **Zero-correction fragility** (contact flag, cadence, ConstrainTo slow-down) | **SP7:** a kinematic walker against live ACE with the retail cadence; the force-position sequence stays 0; ConstrainTo factor measured at loopback RTT. |
| 9 | **Exit-window precision** (68-76 vs 69-75) | **SP8 (closed):** retail admits frames 68-76, read from the 11.6096 machine code (retail-addresses-m2.md §1.1). |
| 10 | **Decoder traps:** DRW StringInfo mislabelled (dats.xml:3352-3360), `MediaType.Stretch` has no decoder, SoundTable grandchildren, ADPCM waves | **SP9:** R1, PS9 and N9 censuses fail loudly; raw walkers as cross-checks. |
| 11 | **DoD 15:** about 150 objects × 17 parts, login burst, shader compiles, teleport jump frame 40-71 ms (navigation.md:80-82) | **SP10:** `--avatar-stress=150` early (G5 W6; the crowd was dropped by the owner after wave-2 item 4); INSTANCE_CUSTOM batches; warm-ups; jump-frame spread (W2-pre(c), conditional). |
| 12 | **Input conflicts** (retail keymap, M1 hotkeys, text focus with FlyCamera polling at FlyCamera.cs:197-200) | **SP11:** router precedence tests; `KeyboardCaptured`; D41. |
| 13 | **Password leaks** (godot.ps1:224 echo, recordings, fixtures, logs, exceptions) | No password in arguments or options; credentials read from bytes; message-level taps; sentinel tests; smoke scan; ACE Packets logger never raised to DEBUG. |
| 14 | **Shared single backend** across worktrees and assemblies | One Loopback.Tests assembly; a lock file in the main checkout; the backend run only from the main checkout; the lead schedules ACE restarts. |
| 15 | **Test and smoke time growth** | Tier=Soak excluded by default; the login step skips when the backend is down; step times reported in the summary. |
| 16 | **Missing 11.6096 addresses** | L10 (D4); merge-gate rejection. |
| 17 | **Scope:** about 80k product lines after removing duplicates (down from about 95k) | Defer if needed, in this order: PH-O oracle, remote full transitions (already deferred), the shell's floaty windows beyond vitals, chat, spew box, smartbox and radar (the shell is made of them, D36). MoveTo stays (DoD 9). |
| 18 | **Decompiled code in the repository** | Nothing from the decompile is copied in: functions are cited by name and address and re-expressed, and the wave-3 review checks every track for pasted code (§6.3). |

---

## 10. Owner decisions

Decided by the owner on 2026-10-12. Every recommendation was accepted as written, except that the owner set D1, D11, D14, D35 and D40 as the last column gives them. m2-log-in.md §7 records the ones that change the specification (decisions 6-10). After wave 1 the owner settled D36 and D54 and amended D40 (decisions 11 and 12).

| # | Question | Blocks | Decided |
|---|---|---|---|
| D1 | Run M2 as multi-agent workflows (m2 §3) | W0 | Yes, with the seven wave-1 and seven wave-2 tracks above. Each wave runs lean: implement, then one adversarial review, then one fix pass |
| D2 | Language dat required only for play, the retail UI and the headless host | W0a.1 | Yes; explore mode works without it |
| D3 | DatSet decodes local records through a portal-bound reader, not DatCollection | W0a.1 | Yes, with the internal DatCollection fallback documented |
| D4 | One pinned read of `symbol_transplant_11.6096.tsv` into cache/research; read-only byte inspection of acclient.exe for MIN_JUMP_EXTENT, the exit-window constants, the ClothingTable fallbacks and CAMERA_DEFAULT_PIVOT_Z | W0a.1 (citation gate) | Yes, as M1 scenery did |
| D5 | Project names `Lifestone.Headless` (UseAppHost=false) and one `Lifestone.Loopback.Tests` | W0a.1 | Yes |
| D6 | NuGet lock files | W0a.2 | Yes; no new packages |
| D7 | Hygiene test reads the user and machine names at run time (prints file and line only) | W0a.2 | Yes |
| D8 | Automated off-screen runs silent (Dummy audio, `-Audio` to hear); interactive runs at full volume with `--mute`/`--volume` | W0a.2 | Yes |
| D9 | Play switch name and the no-argument default | W0a.1 option table | `--play`; no arguments stays explore mode; play.ps1 passes `--play` |
| D10 | Separate accounts.play ("lifestone") and accounts.test ("lifestone-test"); `--account` resolves keys, then usernames; `-AddAccount` | before any live login | Yes |
| D11 | How first-login Admin promotion is prevented (the mod cannot clear the flag in time, §1.3) | before any live login | A **sentinel Admin account** created at setup with a random password that is discarded, so an Admin always exists and ACE never sets the flag, **plus a mod-ready gate**: start-server, play.ps1, both hosts, BackendProbe and the smoke login step refuse without `ace.modReady` (`-AllowNoMod` for debugging). Consistent with m2 §7 decision 3 (a development admin is created on purpose and never used); m2 §7 decision 8 |
| D12 | smoke `-Backend start` mode for merge gates (default `auto` never starts the backend) | W0a.2 | Yes |
| D13 | play.ps1 stops only what it started (`-AceOnly` when MariaDB was already up; `-KeepServer`) | L18 | Yes. The backend's lifetime belongs to `tools\play.ps1`, not the client (was m2 §7 open question 3; m2 §7 decision 10) |
| D14 | Golden fixtures and real recordings from ACE: commit them, or regenerate them (was m2 §7 open question 2) | net N11, state ST14 | Regenerate locally under `cache/`; never commit them. Replay tests build their synthetic recordings in code (`AceWireWriters`) at run time; m2 §7 decision 9 |
| D15 | Keep one smoke character on lifestone-test (for example "Lifestone Smoke") | before G-B | Yes |
| D16 | PlayerDescription, PrivateUpdate* and QueryHealth in M2 (vitals, character info, health, run rate) | net N10, state ST8, G4 | Yes |
| D17 | MoveToManager in M2 (portal Use from range; creatures chasing) | avatars AV14 | Yes, owned by avatars |
| D18 | Transport deviations on by default, each restored by `--retail-net`: 20 s silence (retail 140 s), 3 retries (4 attempts) for 0x01/0x15/0x0D/lost ConnectRequest, tail probe (it holds the acks while a sent packet is unacked; `TailProbe=false` turns both off), ordered delivery. Also: send no Echo, Flow or TimeSync | net N5/N8 | Yes |
| D19 | The 2-minute lossy soak runs only at merge gates (Tier=Soak), not in every smoke | net N12, L12 | Yes |
| D20 | Port ACE's 55-line CryptoSystem as a test-only oracle (AGPL) | net N4 | Yes |
| D21 | Physics quantum 1/5 with the 30 Hz gate; ACE's 0.1 only in compat mode | physics PH0 | Yes |
| D22 | ACE physics oracle harness (loads the setup-built ACE DLLs in its own process, output to cache only) | physics PH-O | Yes, or defer under scope pressure; the fallback is transcribed oracles |
| D23 | Retail ConstrainTo on every echo, measured first | physics PH7 | Retail; revisit only if the slow-down is visible. Measured in wave 1 (docs/physics.md §12): outdoors it is not visible (97.5% of the top run speed). Indoors (5/20 m) it is worked out, not measured, and would be large at top speed (about 25% slower on average, about 5% at the explore-mode placeholder run rate). Open: once ST16 measures it, decide whether indoors keeps retail or a glitch fix becomes the default, with a flag that restores retail |
| D24 | Remote objects: interpolation plus a ground-probe clamp (flagged deviation); full transitions later | avatars AV7, physics PH8 | Yes |
| D25 | Player collides with server objects | physics PH4/PH5 | Yes |
| D26 | Draw player and object positions interpolated between 30 Hz ticks (`--motion-stepping=retail` restores stepping); poses stay retail keyframes | G5 | Yes (smoothing on, pose sampling retail) |
| D27 | Exit window frames 68-76 (decompile float maths) | portal PS7 | Yes; verified from the 11.6096 machine code (retail-addresses-m2.md §1.1), SP8 closed |
| D28 | Logoff holds the tunnel until the character list arrives (`--retail-logoff-tunnel`) | portal PS7 | Yes |
| D29 | Seen-outside interiors also wait for the outdoor 3×3 | portal PS8 | Yes |
| D30 | A never-ready destination: retail wait with a loud log every 10 s, no cap | portal PS11 | Yes |
| D31 | Exact frustum projection for the zoom (no 179° cap) | W2-pre(a) | Yes |
| D32 | Labelled placeholder line for "In Portal Space - Please Wait..." until the spew box | G1 | Yes |
| D33 | The +20 s PK logoff delay off until verified (no PK in M2) | portal PS7 | Yes |
| D34 | Capture option `--portal-space-frames[=DIR]` | G1 | Yes |
| D35 | Character management: retail-exact, or with a Lifestone 3D preview | avatars AV12, G2 U4 | **Retail-exact, with no 3D preview** (retail has none); DoD 3 is amended. Character creation keeps its 3D view (AV12, G3 U5); m2 §7 decision 6. Its idle emotes: D54 |
| D36 | Shell: retail's default gameplay root (whatever R2 finds), or force classic_* | G4 | Retail default. R2 found it is the end-of-retail floating-window shell (`classic_gameplay` 0x21000005, root element 0x10000495; docs/retail-ui-data.md §2); the owner chose it after wave 1 (m2 §7, decision 11) |
| D37 | UI scale: integer rule (1× at 1080p), `--ui-scale` override | G2 | Accept |
| D38 | Skip intro movies; Credits opens a "not in M2" dialog | G2 | Yes |
| D39 | Text input: chat, CharGen name, delete confirm, tool fields; copy, cut and paste; no IME | R12 | Yes |
| D40 | World framing: the whole window behind the UI, or the retail SmartBox frame | W2-pre(a), G1, G2, G4, G5 | **The SmartBox frame**, as retail. The world camera's projection is offset so that it centres on `GameViewRect` (the frame's window rectangle, published by G4 and G2), and the retail UI around the frame is opaque. The portal zoom expands and collapses about the frame's centre; picking, the interiors' frustum and the sky use the same projection; m2 §7 decision 7. Amended after wave 1: in retail's default shell (D36) the SmartBox fills the screen, so for now `GameViewRect` is the whole window and no offset is applied; the hook stays (m2 §7, decision 11) |
| D41 | Play-mode keys: keep F1 and F3; drop T, Tab, Esc, F, F7 and F9; tools and inspector hotkeys chosen from keys unbound in gmDefaultMap | G5, G7 | Yes |
| D42 | Options panel settings last for the session only | G4 | Yes |
| D43 | Disconnected button reconnects (deviation; `--retail-disconnect` restores the epilogue exit); amend DoD 12 to "retail's disconnected screen" | G2, state ST13 | Yes |
| D44 | Inspector on the retail engine with a Lifestone layout | G7 | Yes; a UiStyle fallback only if the engine misses wave 2 |
| D45 | Per-instance translucency in object shaders (UnHide fade, ghost objects) | W2-pre(c) | Approve; the AV9 census shows the scale |
| D46 | Indoor lighting of moving objects: retail's up to 8 lights | W2-pre(c), G5 | Yes |
| D47 | Holtburg route generated at run time and validated offline | physics PH9 | Yes |
| D48 | Recording only with `--record-session`; the headless host and login checks always record | state | Yes |
| D49 | Action timeouts and rate limits as documented defaults | state ST9 | Yes. ST9 to ST12 added two limits the decision did not list: QueryHealth 4 a second and automation's movement intents 20 a second (docs/session.md §7) |
| D50 | Retail UI porting policy: re-express from the decompile; small MIT helpers (edge maths, string hash, action map) may be ported with headers | retail UI | Yes |
| D51 | Command surface `status, where, net, places, xp, place poi, place loc`; xp 1..999,999,999,999 with ACE clamping; `DefaultAccessLevel` must be 0; accounts above Player only warned about | server mod | Yes |
| D52 | Pending deletion shown with Restore and no countdown (ACE reports 1) | G2 | Yes |
| D53 | Licence and publishing (m2 §7, still open 1 and 3) | publishing only | Still open; no wave blocked |
| D54 | CharGen's 3D view: play the idle emotes behind an option, or follow retail | avatars AV12, G3 U5 | **Retail-exact: the character stands still.** Amended by D60 after wave 3: retail loops an idle animation (not the emotes) where the decompile shows it. Retail's `gmCG3DView` loads the emotes and never plays one; DoD 3 is amended (m2 §7, decision 12). Decided after wave 1 |
| D55 | G4's reads of the user's `acclient.exe`: values the decompile garbled or hid | G4 g2-g6 | **Yes**, read-only byte reads, never run, as D4: `ChatInterface::PostInit` (the per-window text-type masks, the opacity defaults) and `BuildChatColorLookupTable`, `gmRadarUI::UseTime` (the update interval), `VividTargetIndicator`'s `s_BlitMethod` and `min_dist`, `gmGamePlayUI::MakeLogoutConfirmationDialog` (properties 0xC3 and 0xC5), the link-loss function's units, and the radar and chat colour globals. Each read is recorded in docs/research/retail-addresses-m2.md; no bytes in the repository. Decided during G4 |
| D56 | The floating windows' positions: save them as retail did (the `UI-<world>-<char>-<w>-<h>.txt` file and the character's options on the server), or keep them for the session | G4 | **For the session only**, as D42; a named TODO records retail's two stores. Decided during G4 |
| D57 | The size of tests, checks and frames | all checks, G4 g7 | **800x600**, retail's native size, for tests, checks and review frames; larger sizes are opt-in (`smoke.ps1 -AllSizes`, `--all-sizes`: 1920x1080 and 2560x1440) and a polish step at the end of the project. Decided during G4 (2026-10-14) |
| D58 | Sound in the M2 hero video | hero video (m2-log-in.md, Status) | **No sound.** The M2 hero video is silent; full sound effects move to a later milestone (likely M3). Decided during G4 (2026-10-14) |
| D59 | Proving character delete and restore live without changing the test account | server mod, a live CharGen leg | **A mod verb purges a throwaway**: it removes a character only when it is on the caller's own account and already marked for deletion through the retail screens, and refuses the graduated test characters. Installing it is an owner go-ahead (`setup-server.ps1 -ModOnly`). m2 §7, decision 13. Decided after wave 3 |
| D60 | CharGen's 3D view animation (amends D54) | G3 U5 | **Retail's**: `gmCG3DView::StartAnimation` (loops the idle animation at 30 fps) from `gmCGSummaryPage::InitializePage` and `gmCGAppearancePage::ZoomOut`, and from `gmCGAppearancePage::Update` while zoomed out; `StopAnimation` (the rest pose) from `ZoomIn` and from `Update` while zoomed in (`SetSelection` calls neither; corrected in the follow-up, §6.9). The idle emotes stay unplayed. m2 §7, decision 14. Decided after wave 3 |
| D61 | Where the live Add XP test runs | server mod loopback tests | **On the throwaway character only** (D59), never on the graduated test characters. m2 §7, decision 15. Decided after wave 3 |
| D62 | UI sounds in M2 | DoD 4 | **Not in M2**: DoD 4 is amended; they come with full sound in a later milestone (with D58). m2 §7, decision 16. Decided after wave 3 |
| D63 | The materialise fade (D45, `TODO(g5-translucency)`) | avatars, G5 | **Built in M2**, as retail. m2 §7, decision 17. Decided after wave 3 |
| D64 | DoD 8's Academy walk | DoD 8 | **The exit wing's walk**, as built (the spawn cell and the wing connect only through quest-gated portals). m2 §7, decision 18. Decided after wave 3 |
| D65 | A new character after creation | G3, G6 | **Enters the world at once, as retail** (the Entering World dialog on that path is `TODO(retailui-chargen-enter-dialog)`). m2 §7, decision 19. Decided after wave 3 |
| D66 | The owner's visible-window review (§6.3 step 5) | M2's end | **Cleared**: the owner watched the work in visible windows throughout M2, so it is not a blocker. The gaps that named it (DoD 5's sounds, DoD 11's window close) stay named for M3. m2 §7, decision 20. Decided 2026-10-15 |

---

## 11. Verification

Common environment, PowerShell 7, per process:

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'; $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
```

**Wave 0**

```powershell
dotnet build Lifestone.slnx --disable-build-servers                       # 0 warnings
dotnet test  Lifestone.slnx --disable-build-servers                       # 1,270 + new; Loopback skipped with reason
dotnet test  Lifestone.slnx --disable-build-servers --filter "FullyQualifiedName~LocalDatTests|FullyQualifiedName~EnumIdMapsTests|FullyQualifiedName~Repository|FullyQualifiedName~Options"
pwsh -File tools\smoke.ps1                                                # 13/13, secrets=0
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 120 -GameArgs:'--engine-checks'   # 9/9, Dummy audio
git -C .claude\worktrees\m2-<track> config --get core.longpaths           # true
```

**Wave 1, every merge (G-A):** the build, the default `dotnet test`, `smoke -Backend auto`. Track-specific suites:
- `--filter FullyQualifiedName~Lifestone.Net.Tests`: vectors, codecs, FakeAce, fuzz, virtual soak;
- `~Session`: S8, hygiene, boundary, allocation, replay;
- `~PortalSpace|~.Audio.`: timelines, pins, allocation;
- `~Appearance|~Motion|~Avatars`;
- `~Physics|~Movement`: synthetic transitions, cadence, offline routes;
- `~.Ui.|~RetailUi|~CharGen`: anchoring at 800×600, 1280×720, 1920×1080 and 2560×1440; inheritance; fonts; strings; 55 SACs;
- `~ServerMod`: wire, culture, source hygiene.

**Live (G-B, main checkout only):**

```powershell
pwsh -NoProfile -File server\setup-server.ps1            # after the server-mod merge: secrets v2, mod built + installed, sentinel admin, account audit
pwsh -NoProfile -File server\start-server.ps1            # returns after "Lifestone mod ready"; pids.json ace.modReady
Import-Module .\server\lib\LifestoneServer.psm1; Test-LsServerModLive (Get-LsConfig)   # console status/net parse; no "refused"; leaks = 0
dotnet test Lifestone.slnx --disable-build-servers --filter "Category=Loopback&Tier!=Soak"
pwsh -NoProfile -File server\stop-server.ps1
```

Loopback scenarios, all on accounts.test:
- **Net phase A:** login, create, enter cell 0x8602, unhide, logoff, Quit, immediate re-login without 0x01.
- **Net phase B:** Holtburg place, where within 0.5 m, force-position unchanged.
- **Session:** the headless login enters and leaves; the recording holds no password (count only).
- **Movement (after ST16):** holtburg-laps ×3 and academy-exit with corrections=0, where_max_m ≤ 0.5, and F751 after the portal Use.
- **ServerMod (wave 3):** status Player and autoPromotion false; place poi and loc; invalid-cell; not-found; xp 1000; net counters; places equal the client extract.

**Wave 2, Godot, always off-screen:**

```powershell
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--portal-space-checks'
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--portal-space-frames=cache/m2/portal-space'
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -Resolution 800x600   -GameArgs:'--retail-ui-frames=cache/m2/ui/800x600'
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -Resolution 1920x1080 -GameArgs:'--retail-ui-frames=cache/m2/ui/1920x1080'
pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--offline-player'
pwsh -File tools\play.ps1 -Offscreen -IsolateUserData -TimeoutSeconds 600 -GameArgs '--login-check=all'   # backend handling per D13
```

What each run must show:
- **Portal space:** all `[portalcheck]` PASS; `captured=4 failures=0`; the timeline log shows 2→3 at 1.0 s, a hold of 2 s or more, the 5 s cap, and no LoginComplete offline.
- **Retail UI:** all `[uicheck]` PASS; `failures=0`; the missing image named; the window at the requested size.
- **Avatar stress:** 60 fps or more in every one-second window after attach.
- **Offline player:** the dry-run sink logs MoveToState on each change and AutonomousPosition on the retail cadence.
- **Login check:** `[logincheck] done ok=true`, enter_to_walk_max_ms ≤ 50, holtburg_min_fps ≥ 60, corrections=0, logoff=ok.

**Final (G-C):**
- `pwsh -File tools\smoke.ps1 -Backend require` (or `start`): 18/18, login PASS, secrets=0, stray-output clean.
- `dotnet test --filter Tier=Soak` and the Exhaustive sweeps green, with the backend up and with it down.
- ProvenanceTests green, and every ported file listed in docs/third-party.
- LegalNotices and README in lockstep.
- No `TODO(...): 11.6096` left.
- The DoD audit table cites evidence for all 15 items.
- `git status` clean; nothing under client/, cache/ or server/runtime/ tracked.
- Owner sign-off by running `pwsh -File tools\play.ps1` in a visible window: with the backend stopped it starts and stops it; with it running it leaves it running.
