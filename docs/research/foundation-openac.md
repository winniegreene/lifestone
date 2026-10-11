# openac — status done

# OpenAC (eriknihlen/OpenAC) as a code source for Lifestone: read-only evaluation

## Summary
- **Clone.** `<repo>\vendor-eval\OpenAC`, shallow (`--depth 1`). Clone settings: LFS smudge off, `core.symlinks=false`, `hooksPath=/dev/null`, no credential helper.
  - HEAD **1ee5a08581a7f4b0b22effdf911171a76cf4391c** (2026-10-08T18:58:13Z, "Merge pull request #216"). Tree facd75e9b580a503a79a270a4e1e30e0fca12da6. The GitHub commit signature is verified (`reason: valid`).
  - 4,061 tracked files, 73 MB on disk. No symlinks, no submodules, no LFS.
- **Not built or run.** OpenAC itself was not built or run. I built and ran only my own 20-line C# snippet, in the scratchpad, to settle one language-semantics question.
- **History.** MIT licence, created 2026-09-08, 1,338 commits.
  - The first commit, 3b6bb588 "Initial OpenAC 0.1.0" (2026-09-09), is a squash of 3,487 files and 1,338,045 added lines, co-authored by Codex. There is no history from before that import.
  - Since 2026-09-24 main has 126 commits. Per folder: Runtime 42, Headless 18, Core.Net 11, Core/Physics 1, Core/Terrain 0, Core/World 0, Core/Rendering/Wb 1.
- **Size.**
  - src is about 430k lines of C#: App 211k, Runtime 86k, Core 68k, Core.Net 17k, Plugin.Abstractions 12.7k, Headless 10k, Content 8.4k.
  - tests are about 560k lines with 16,908 `[Fact]`/`[Theory]` tests.

## 1. Security review
**Build files** (52 csproj/props reviewed, plus NuGet.Config, global.json and the tool manifest):
- **No dangerous build constructs.** There is no `<Exec>`, `UsingTask`, `DownloadFile` or external `<Import>`.
- **Package sources.** NuGet.Config does `<clear/>` and then uses nuget.org v3 only; fallback folders are cleared. Restore uses lock files (`RestorePackagesWithLockFile`).
- **Directory.Build.props.** Sets net10.0, TreatWarningsAsErrors, AnalysisLevel=latest and Deterministic. Its only target, `ExcludeOwnDocumentationFilesFromPublish`, is harmless.
- **global.json.** Pins SDK 10.0.300 with `rollForward: latestPatch`. The installed **10.0.401 does not satisfy this** (different feature band), so building in place would fail.
- **Tool manifest.** `.config/dotnet-tools.json` lists docfx 2.78.5. It is only used if someone runs `dotnet tool restore`.
- **Launcher targets.** The only custom targets are in `src/AcDream.Launcher/AcDream.Launcher.csproj`: `CoDeployBakeToolToBuildOutput` (runs after Build) and `PublishCoDeployedBakeTool` (runs after Publish).
  - Both run a nested `<MSBuild Targets="Restore;Publish">` that builds AcDream.Bake as a self-contained single-file exe.
  - So building the Launcher or the whole .slnx downloads runtime packs from nuget.org. No shell commands are run.
- **tests/Directory.Build.props.** Adds `TestPathIsolation.cs`, a module initializer. It sets `ACDREAM_ROOT_DIR` for the test process only, writes to `%TEMP%\openac-tests\<pid-guid>`, and deletes it on exit.

**Scripts:**
- `tools/*.ps1` (15 scripts) and `build-linux.sh` are developer and release tooling. The build never calls them.
  - `build-macos-x64-vulkan.ps1` downloads MoltenVK with `Invoke-WebRequest`.
  - `prune-dev-prereleases.ps1` runs `gh release delete --yes --cleanup-tag`. **Never run it** with your own gh authentication.
- There are 4 GitHub workflows. Pushes run on self-hosted runners (`openac-windows`, `openac-linux`) with `permissions: contents: read`. They are not relevant to us.

**Committed binaries:**
- There are no DLL, EXE, nupkg, zip or pdb files.
- 70 compiled SPIR-V shaders (`.spv`):
  - They live in `src/AcDream.App/Rendering/Shaders/spv` and `samples/.../Shaders`.
  - Their GLSL sources are committed, `shaders.manifest.json` records a sha256 per source, and a test re-checks the hashes.
- Icons (PNG/ICO) and NotoSans-Regular.ttf (OFL licence).
- **Data derived from the retail game, which is IP-sensitive.** Do not import any of these:
  - 12 MB of JSON dumps of the retail UI layout data (LayoutDesc 0x21xxxxxx) in `tests/AcDream.App.Tests/UI/Layout/fixtures`.
  - 756 KB of EnvCell/GfxObj dumps in `tests/AcDream.Core.Tests/Fixtures`.
  - A retail debugger trace log, `Conformance/Fixtures/find-cell-list-threshold.log`.

**Network access and telemetry:**
- **HTTP is only in Launcher and Launcher.Core.** It contacts:
  - `treestats.net/servers.json` and `player_counts-latest.json`;
  - the GitHub releases manifest for self-update (`ReleaseManifestClient`, `VerifiedArtifactDownloader`);
  - GitHub plugin release feeds for repos the user enters (`PluginReleaseClient`);
  - servers, via a UDP reachability probe.
- Core, Content, Core.Net, Runtime, Headless and App contain no HTTP and no telemetry.
- **Wildcard UDP bind.** `src/AcDream.Core.Net/NetClient.cs:19` does `new UdpClient(new IPEndPoint(IPAddress.Any, 0))`.
  - That binds the game socket on all interfaces. It breaks our 127.0.0.1-only rule and will likely raise a Windows Firewall prompt.
  - Every Core.Net, Runtime and Headless test that creates a NetClient does the same. This needs a one-line patch to bind `IPAddress.Loopback` when the server address is loopback.
- **Peer hub process spawn.** `Runtime/Plugins/PeerHubEndpoint.StartHub()` starts `AppContext.BaseDirectory\acdream-headless.exe peer-hub <dir>` hidden.
  - It talks over a named pipe with `PipeOptions.CurrentUserOnly`. This spawn lives inside Runtime and should stay disabled in Lifestone.
- **Plugin loading.** `Core/Plugins/PluginLoader` and `PluginAssemblyLoadContext` load arbitrary plugin DLLs into collectible load contexts. Running third-party code is the design.

**Files written outside OpenAC's own folders:**
- The default root is `%LOCALAPPDATA%\OpenAC` (settings, logs, plugins, vtank, journal, cache). The env vars `ACDREAM_ROOT_DIR`, `_CONFIG_DIR`, `_DATA_DIR` and `_CACHE_DIR` override it.
- It detects the legacy layout in `%APPDATA%\acdream` and `%LOCALAPPDATA%\acdream`.
- App keeps keymaps in `Documents\Asheron's Call`.
- The DAT locator probes `%USERPROFILE%\Documents\Asheron's Call` and `C:\Turbine\Asheron's Call`.
- Bake writes `acdream.pak`. About 46 `ACDREAM_*` env-var switches turn on diagnostic dumps.
- **DATs are opened read-only** (`DatAccessType.Read`; DatReaderWriter uses `FileAccess.Read`).
- **Nothing system-level.** No registry, services, elevation, firewall rules, shortcuts, protocol handlers or OS credential stores. Headless credentials come from env vars, stdin or a file.

**Risk:**
- **Low** for reading the source or copying selected files.
- **Medium** for building or running it unmodified: wildcard UDP bind, HTTP self-update, plugin DLL loading, the peer-hub process spawn, and tests that start processes, open sockets and write to `%TEMP%`.
- About a million lines of AI-assisted code cannot be fully reviewed, so vendor narrowly.

## 2. Architecture
| Project | Lines | References | What it owns |
|---|---|---|---|
| Plugin.Abstractions | 12.7k | BCL only | Plugin contract: game state, events, commands, automation surfaces modelled on VirindiTank (`vtank` profiles), markup panels |
| Core | 68.3k | Plugin.Abstractions; DatReaderWriter 2.1.7, BCnEncoder 2.2.1, Serilog 4.0.2 (not used in Core source), StbImageSharp, StbImageWriteSharp | Physics (90 files, 31k lines), motion, terrain, scenery, sky, Vfx, lighting, meshing, texture decode, items/spells/combat/chat models, plugin loader (6.3k), navigation (5.1k) |
| Content | 8.4k | Core | Read-only DAT collection adapter, caches, `acdream.pak` reader/writer, magic/chargen readers |
| Core.Net | 17k | Core | UDP, ISAAC, fragments, reliable transport, 78 message parsers/builders, WorldSession (3k) |
| Platform | 0.8k | BCL | Application paths |
| Runtime | 85.7k | Core, Core.Net, Content, Platform, Plugin.Abstractions | `GameRuntime` kernel; includes Plugins (10.3k) |
| Headless | 10k | Runtime | Window-less host: bots, scheduler |
| UI.Abstractions | 4.4k | Core, Runtime, **Silk.NET.Input** | Input/bindings/view models — not engine-agnostic |
| App | 211k | everything + Silk.NET Vulkan/Windowing/Input/OpenAL, Arch ECS | Vulkan renderer, retail UI (63k), streaming, composition |
| Bake / Cli / Launcher.Core / Launcher | — | — | Builds the pak, DAT inspector, installer/updater, Avalonia launcher |

**Engine-agnostic projects** (no Silk, Vulkan or Avalonia references): Platform, Plugin.Abstractions, Core, Content, Core.Net, Runtime, Headless, Bake, Cli and Launcher.Core. A net10.0 Godot project can reference them.

**Is Runtime a clean game-state kernel? Partly.** In shape it is one:
- `GameRuntime` exposes read views: `Entities`, `Inventory`, `Character`, `Chat`, `Movement`, `Environment`, `Portal`, `Fellowship`, `Trade` and more.
- Commands go through `DirectGameRuntimeCommandAdapter` and `IRuntimeSessionCommands`, and events arrive in order through `Subscribe(IRuntimeEventObserver)`.
- `AdvanceFrameClock(dt)` drives time. There are no engine types.

**What the Headless host shows.** It proves Runtime runs full ACE sessions with no window: login, movement and physics, navigation, combat, looting, plugins. It also shows how heavy the host contract is:
- **Per-frame order.** `HeadlessSessionHost.Tick` (line ~597) is a strictly ordered sequence of about 15 steps: AdvanceFrameClock, navigationWalk.Tick, localPlayerFrame.AdvanceBeforeNetwork, remoteBodies.Tick, FinishRemoteBodyPass, liveSession.Tick, PumpFirstEntry, PumpPortalCompletion, RetryPending, liveness.Tick, RunPostNetworkCommandPhase, CombatAttack.Tick, policy, plugins, logout.
- **Host-supplied interfaces.** The host must implement the combat, spell and session operations listed in `GameRuntimeDependencies`, a local-player frame host, event and command routes, and landblock collision streaming.
- **Collision streaming uses internal APIs.** The host streams collision through a transaction (`HeadlessCollisionGenerationTransaction`) that calls **internal** Runtime methods, `PrepareCollisionGeneration` and `CommitCollisionGeneration`. Those are visible only to the assemblies named `acdream-headless` and `AcDream.App`.
- **Size of the glue.** About 3–5k of Headless's 10k lines are this glue. The App's equivalent (World, Physics, Streaming, Composition, Runtime and Net folders) is about 36k lines, and some gameplay-adjacent logic lives there, e.g. `LiveEntityNetworkUpdateController` and `ProjectileController`.

**Problems for a Godot host:**
1. Needs an `InternalsVisibleTo` patch.
2. Production collision requires `acdream.pak` built by Bake, through `IPreparedCollisionSource`. Only tests contain a source that reads collision straight from the DATs (`tests/AcDream.App.Tests/Navigation/InstalledDatCollisionSource.cs`).
3. `client_highres.dat` is effectively required, and the user doesn't have it:
   - DatDirectoryLocator requires all four DAT files.
   - DatReaderWriter's `DatCollection` always builds a HighRes `PortalDatabase`. Its `MemoryMappedBlockAllocator` opens the file with `FileMode.Open`, which throws if the file is missing. (That is from upstream HEAD source, not the 2.1.7 package.)
4. The DAT reader is not thread-safe; Headless uses one global `DatLock`.
5. Plugins and the peer hub are woven through Runtime (14 files outside `Runtime/Plugins`).
6. Core.Tests references the AcDream.App csproj, so Core's tests need Silk and Vulkan to build.
7. With TreatWarningsAsErrors and AnalysisLevel=latest, an SDK upgrade can break the build.

## 3. Quality spot-checks
**(a) Terrain**
- **Split diagonal is correct.** `Core/Terrain/TerrainBlending.CalculateSplitDirection` uses 0x0CCAC033=214614067, 0x421BE3BD=1109124029, 0x6C1AC587=1813693831 and 0x519B8F25=1369149221. The formula `x*y*A - x*B + y*C - D` equals the ACE/retail `gy*(A*gx+C) - B*gx - D` (ACE `LandblockStruct.ConstructPolygons` checked). It uses the bit-31 test.
- **Rest of the cell blending matches.** palCode, GetRoadCodes (0xE→6,12 …), RotateTerrainCode, FindTerrainAlpha/FindRoadAlpha and the duplicate-corner overlay logic match rendering.md §3 and WorldBuilder's `LandSurfaceManager`.
- **PseudoRandomIndex is correct.** I first suspected that int×uint would widen to long, so I built a 20-line C# check. `1379576222 * palCode - 1372186442` is UInt32 and wraps, matching retail CTexMerge.c:242/265. The corner-map index is uniform over 0..3 (25,228 / 24,866 / 24,947 / 24,959 out of 100k).
- **Shader:**
  - Correct: per-layer TexTiling, alpha rotations r1 `(1-v,u)`, r2 `(1-u,1-v)`, r3 `(v,1-u)`, and corner UVs. Road coverage `1-a0*a1` is exactly what the sequential merge gives.
  - **Approximate:** overlays use parad0x's `maskBlend3`, not retail's sequential merge. It gives the first overlay priority, and its weights don't sum to 1 (three alphas of 0.5 give 1.125, which is then clamped).
  - Correct: per-block vertex normals (`BuildRetailVertexNormals`) and vertex lighting.
- **Bug inherited from WorldBuilder upstream.** `Core/Rendering/Wb/TerrainUtils.CalculateSplitDirection` computes `214614067*gx + 1813693831 - 1109124029*gy - 1369149221`: it drops the ×gy term and uses gy where gx belongs.
  - Over all 4,161,600 cells it picks the **wrong diagonal for 2,080,124 (49.98%)** (my script `wb_split_check.py`).
  - It is used by the scenery slope test (`GetNormal`). The mesh and physics use the correct split.
- **Tests.**
  - `ClientConformanceTests` checks the split over 65,536 cells and checks palCode.
  - The PseudoRandomIndex and BuildSurface tests only check range and smoke behaviour.
  - The shader has no conformance test. TerrainUtils has no tests.

**(b) Physics.** I compared OpenAC `TransitionTypes.DoStepUp/DoStepDown/DoCheckWalkable` with ACE `Transition.StepUp/StepDown/CheckWalkable` and with retail `CTransition::step_up/step_down/check_walkable` (Mag-nus 11.4186 decompile).
- **step_down:** all three have the same control flow.
- **step_up:** OpenAC matches, with one **deviation**: when the step fails, OpenAC restores the contact plane it had before the step (valid flag, plane, cell, water). Retail and ACE leave the contact plane invalid. No retail-oracle test covers the failure path.
- **check_walkable:** retail uses a local backup position. ACE overwrites `SpherePath.BackupCheckPos`, which is ACE's own deviation. OpenAC preserves the previous backup, so it is closer to retail here than ACE.
- **Polygon/sphere hit test.** BSP leaves use `PolygonHitsSpherePrecise`, which matches retail `polygon_hits_sphere_slow_but_sure` (EPSILON 0.0002, `<=`). The unused `CollisionPrimitives.SphereIntersectsPoly` uses 1e-4 and `<`.
- **Constants.**
  - MaxQuantum 0.2, MinQuantum 1/30 and HugeQuantum 2.0 (`PhysicsBody.cs:61-63`). That matches the retail client; ACE uses 0.1.
  - LandingZ 0.0871557. FloorZ 0.6642 (ACE has 0.66417414…).
- **Code health.** There are two parallel BSP implementations (BSPQuery 1.8k lines and FlatBspQuery 2.1k), with agreement tests. `ShadowObjectRegistry` is 3.4k lines. About 1.7k lines of inline diagnostics, including Console.WriteLine.
- **Tests:** 1,882 physics tests in 165 files, with retail breakpoint-trace replay (`Conformance/RetailTrace.cs`) and fixtures dumped from the DATs.

**(c) Scenery**
- **Constants all match rendering.md §4 and the retail code**, with explicit uint wraparound: 712977289/1813693831/1109124029/2139937281 (scene pick), 1360117743/1888038839/23399+j (frequency), 45773/72719 (displacement), 1870387557/402451965 (quadrant), 63127 (rotation), 32593 (scale). `OnRoad` is identical to ACE `Landblock.OnRoad`.
- **Deviations from retail.** I checked them against `CLandBlock.c:1362-1400`, where `Plane::set_height` overwrites z, `GetObjFrame` copies the location, and `obj_within_block` runs. I measured on 380 landblocks around Holtburg (0xA0–0xB2 × 0xAA–0xBD, 49,157 candidates that survive the road and block-edge checks):
  1. **Slope test uses the wrong split.** 620 objects (1.26%, about 1.6 per landblock) get a different keep/reject decision.
  2. **Height is offset.** OpenAC places objects at ground + `BaseLoc.Z` (`LandblockBuildFactory.cs` ~236). 1,711 of the 42,032 kept objects (4.1%) have `BaseLoc.Z = 0.5`, so they render **0.5 m too high**.
  3. **No `obj_within_block` check**, so objects that cross the landblock edge are kept.
  4. The quadrant test is computed in float instead of double. The effect is negligible.
- **Tests:** `SceneryGeneratorTests` only tests `IsRoadVertex`.

**Other deviations:**
- **Sky:** OpenAC's sun heading lerp uses the shortest arc and its sky-object visibility is inclusive and wraps at midnight. Retail lerps linearly and uses strict Begin<t<End with no wrap.
- **Animation:** `AnimationSequencer` interpolates between keyframes (lerp/slerp). Its `CSequence` uses `floor`, as retail does.

## 4. Provenance and licensing
- **NOTICE claims WorldBuilder MIT code in two folders.** `src/AcDream.Core/Rendering/Wb/` and `src/AcDream.App/Rendering/Wb/` hold 60 files. WorldBuilder has **no LICENSE file**; its README says MIT. NOTICE says "see WorldBuilder's LICENSE file", which doesn't exist.
- **WorldBuilder-derived code outside those folders:**
  - `Core/Terrain/TerrainBlending.cs`: same structure as WorldBuilder `LandSurfaceManager`, including `primaryTerrain`/`secondaryTerrain`/`duplicateIndex`.
  - `Content/EdgeLineBuilder.cs`: also in WorldBuilder `Chorizite.OpenGLSDLBackend/Lib`.
- **ACE-derived code (AGPL-3.0) labelled MIT:**
  - `Core/Rendering/Wb/SceneryHelpers.cs` repeats 5 of ACE `ObjectDesc.cs`'s XML summaries word for word, with the same structure and parameter names.
  - `TerrainUtils.OnRoad` is identical to ACE's.
  - Both came in through WorldBuilder, where the same files exist. NOTICE's "does not include code from ACEmulator" is therefore **not true for these files**.
- **Physics is less clear.**
  - I searched for 108 distinctive ACE physics comment lines and ACE's physics XML docs; there were 0 verbatim hits.
  - The naming points to translation informed by ACE: a `PhysicsGlobals` class with ACE's member set, and `PolygonHitsSpherePrecise` (ACE's name; retail calls it `slow_but_sure`).
  - But MaxQuantum and the step-up/check_walkable details differ from ACE, so it is **not** a straight copy.
- **Other licensing notes:**
  - NOTICE lists DotRecast packages that are not in `Directory.Packages.props`, so it is stale.
  - With the history squashed into one import, provenance can't be audited.
  - Comments reference private research notes ("r12 §4").

## 5. Recommendations by subsystem
| Subsystem | Verdict | Upstream files @1ee5a08 | Size | Notes |
|---|---|---|---|---|
| DAT access | Use the Chorizite.DatReaderWriter 2.1.7 NuGet package (MIT); port the adapter with changes | Content/{RuntimeDatCollectionFactory, DatCollectionAdapter, BoundedDatObjectCache, IDatReaderWriter}.cs | ~630 | Open the Portal, Cell and Local databases individually so highres is optional; keep read-only; one global lock |
| Terrain mesh and blending | Port with changes | Core/Terrain/*.cs (638), App/Rendering/TerrainTextureTilingTable.cs (31), terrain_modern.vert/.frag (259 GLSL) | ~900 + Godot shader | Logic verified; reverse winding for Godot, pack into CUSTOM0..3, use the sequential merge instead of maskBlend3; **do not** use Wb/TerrainUtils' split, GetNormal or GetHeight |
| Scenery | Port with changes, or reimplement from rendering.md §4 if Lifestone needs a permissive licence | Core/World/SceneryGenerator.cs, Wb/SceneryHelpers.cs, Wb/TerrainUtils OnRoad/GetRoad, Wb/TerrainEntry.cs, World/WbSceneryAdapter.cs | ~600 | Fix the 4 deviations above; add a 0xA9B3 test (177/30/5/142) |
| Statics, buildings, EnvCells | Port the cell graph; renderer is reference-only | Core/World/Cells/* (547), World/LandblockLoader.cs (124); reference App/Rendering/Wb/EnvCell*, Building* (~2.5k) | ~700 + new Godot renderer | App renderer is tied to Vulkan, bindless textures and multi-draw indirect |
| Model assembly and palettes | Port Core pieces; App pipeline is reference-only | Core/Meshing/* (1,157), Core/Textures/* (619), Wb/TextureHelpers.cs (166); reference App DatLiveEntityProjectionMaterializer / TextureCache / CompositeTextureArrayCache (~2.8k) | ~2k | Clothing and palette compositing lives in App |
| Animation and motion | Copy as-is, pinned | Core/Physics/Motion/* (3,543), MotionInterpreter (1,279), AnimationSequencer (443), InterpolationManager (357) | ~5.6k | Make keyframe interpolation (floor vs slerp) a switch |
| Physics and collision | Copy as-is, pinned, with a minimal patch | Core/Physics/** (90 files, 31,058 lines) + World/Cells + Items flags; a collision source (Content/Pak 1.5k, or one that reads the DATs directly) | ~33k | The most valuable piece: 0.2 quantum and retail-shaped transitions; make the step-up contact-plane restore a switch |
| Sky and lighting | Port with changes | Core/World/{Sky*, WeatherState, DerethDateTime, DerethCalendar} (1,140), Core/Lighting/* (731); reference App/Rendering/Sky (671), sky shaders (246) | ~1.9k | Fix the heading lerp and the visibility test |
| Particles | Port with changes | Core/Vfx/* (2,902); reference ParticleRenderer.cs (1,236) | ~2.9k | Not checked against retail |
| UI | Reference-only | App/UI/** (~63k) | — | Useful as a map of how the retail UI layouts work |
| Net protocol | Copy as-is, pinned, plus the loopback patch | src/AcDream.Core.Net/** (113 files) | ~17k | Required for ACE |
| Runtime and game state | **1–2 week spike first.** If it works, vendor it pinned as a black box behind a Lifestone-owned facade; otherwise reference-only | Runtime (85.7k) + Content + Platform + Plugin.Abstractions; host template Headless/Hosting (HeadlessSessionHost 1,319, WorldProjection 914, GameplayOperations 269, LocalPlayerFrameHost 122) | ~110k kernel + ~3–5k Godot host | Fastest route to inventory, combat, spells, chat and trade against ACE. Costs: the patches above, the pak requirement, plugin entanglement, and the highest churn |
| ~~Decal-style plugins (future)~~ *Superseded:* Lifestone has no plugin host; automation is built in (docs/automation-vision.md) | Reference only, for the API's shape | Plugin.Abstractions, Core/Plugins, Runtime/Plugins, docs/plugin-api.md | — | API shaped after VirindiTank |

**How to vendor.** Don't add a live submodule that tracks upstream main.

*Tier A, the kernel* (Core, Core.Net, Content, Platform, Plugin.Abstractions, and Runtime if the spike succeeds):
- **Copy.** Copy those project folders unchanged into `third_party/openac/` at SHA 1ee5a08. Include the upstream LICENSE and NOTICE.md as-is.
- **Manifest.** Add `UPSTREAM.json` recording the repo, SHA, tree, each imported path and its sha256.
- **Patches.** Keep `patches/*.patch`, one commit per change:
  - NetClient loopback bind;
  - `InternalsVisibleTo Lifestone.*`;
  - highres-optional DAT opening;
  - step-up switch.
- **Build settings.** Lifestone's own global.json, Directory.Build.props and NuGet.Config govern the build. Regenerate the lock files.
- **Updating.** Shallow-fetch the new SHA into vendor-eval and diff the vendored paths. Grep for HttpClient, Process.Start, IPAddress.Any, Assembly.Load and Registry. Then re-copy and re-apply the patches.

*Tier B, the algorithms* (terrain, scenery, sky, Vfx, meshing, textures):
- Copy individual files into Lifestone namespaces with an attribution header.
- Add THIRD_PARTY_NOTICES carrying the OpenAC MIT, WorldBuilder MIT and Chorizite MIT texts, plus a note on the ACE-derived scenery code.
- Those folders have had almost no churn since the import.

*Alternatives:*
- If you want to follow upstream closely: a submodule pointing at a user-owned fork (`<you>/OpenAC`, branch `lifestone`), pinned to a commit. It drags in App, Launcher and the self-update/HTTP code, the 12 MB of DAT-derived fixtures and the self-hosted CI, and it needs network access at clone time.
- `git subtree` is not recommended: it brings the full size, and its merges conflict with local patches.

## VERIFICATION
- Clone integrity: HEAD 1ee5a08581a7f4b0b22effdf911171a76cf4391c equals the gh api commits/main SHA, and the tree is facd75e9b580a503a79a270a4e1e30e0fca12da6. GitHub reports the commit signature as verified=true, reason=valid.
- Clone contents: `git ls-files -s` shows no symlinks (120000) and no submodules (160000). .gitattributes has only eol rules, so no LFS. .git/hooks contains only the default git .sample files.
- Build-file grep across all csproj/props/slnx for <Exec, <Target, DownloadFile, UsingTask, <Import, RestoreSources, <Copy, <Delete: the only targets found are the Directory.Build.props doc-exclusion target and the two Launcher bake co-deploy targets (nested MSBuild Restore;Publish). tests/Directory.Build.props imports the parent props and compile-includes two files.
- Network grep (HttpClient, Socket, UdpClient, Tcp, HttpListener, Process.Start) per file: HTTP only in Launcher(.Core). UDP only in Core.Net/NetClient.cs, bound to IPAddress.Any:0 at line 19. Named pipes and a Process.Start in Runtime/Plugins/PeerHub*.
- Hard-coded URL grep in src: the only URLs are treestats.net (2), the GitHub releases manifest, an Atom namespace, and a Turbine support URL used as a text string.
- Registry, special-folder and credential-store grep: no Microsoft.Win32.Registry, CredRead, ProtectedData or keychain use. Special folders used: LocalApplicationData, ApplicationData (legacy layout), MyDocuments (keymaps) and UserProfile (DAT locator).
- Committed binaries: running `file` over all tracked files finds only SPIR-V (70), PNG/ICO icons and one TTF. No DLL, EXE, nupkg, zip or pdb.
- Terrain split constants: hex-to-decimal conversion confirms 0x0CCAC033=214614067, 0x421BE3BD=1109124029, 0x6C1AC587=1813693831 and 0x519B8F25=1369149221, the same as ACE LandblockStruct.ConstructPolygons (fetched via gh api).
- C# semantics check (my own net10.0 snippet, built and run in the scratchpad): `1379576222 * pal - 1372186442` has type UInt32 (value 930983752 for the sample pal). Corner-map index histogram over 100k random palCodes is 25,228/24,866/24,947/24,959/0, so OpenAC's PseudoRandomIndex equals retail's uint32 math. My initial suspected bug was disproven and not reported.
- WorldBuilder split bug: wb_split_check.py over 4,161,600 cells finds 2,080,124 diagonal mismatches (49.98%) between TerrainUtils.CalculateSplitDirection and the retail formula. The identical formula exists in WorldBuilder upstream (WorldBuilder.Shared/Modules/Landscape/Lib/TerrainUtils.cs, lines 44-53).
- Scenery impact on the user's own DATs (read-only, python -I): scenery_slope_impact.py over landblocks 0xA0-0xB2 x 0xAA-0xBD (380 blocks) gives 49,157 candidates that pass the road and block-edge checks. 42,032 are kept with the retail split and 42,020 with the WorldBuilder split; 620 decisions differ. 1,711 kept objects have BaseLoc.Z=0.5.
- Retail scenery z: retail CLandBlock.c lines 1362-1400 (Mag-nus 11.4186) show Plane::set_height overwriting obj_vector.z, ObjectDesc::GetObjFrame copying loc into the frame origin (Scene.c), and a CPhysicsObj::obj_within_block check. This confirms OpenAC's +BaseLoc.Z and its missing within-block check are deviations.
- Step-up comparison: OpenAC DoStepUp/DoStepDown/DoCheckWalkable compared line by line with ACE Transition.cs (lines 206-777) and retail CTransition.c (check_walkable at line 967, step_down at 1121, step_up at 1286). The contact-plane restore on failure is OpenAC-only. ACE's BackupCheckPos overwrite is ACE-only.
- Polygon/sphere test: OpenAC BSPQuery.PolygonHitsSpherePrecise matches retail CPolygon::polygon_hits_sphere_slow_but_sure (epsilon 0.00019999999 and `<=`). CollisionPrimitives.SphereIntersectsPoly uses 1e-4 and `<`, but BSP leaves do not call it.
- Provenance: of 108 distinctive ACE physics/landblock comment lines, 0 appear verbatim in OpenAC src. Of 45 ACE XML doc summaries, 4 appear verbatim in Core/Rendering/Wb/SceneryHelpers.cs (Displaces…, Returns the scale…, Returns the rotation…, Aligns an object…). The CheckSlope summary is also identical by inspection. The same text exists in WorldBuilder's SceneryHelpers.cs (gh code search).
- WorldBuilder licence: the gh api repo licence field is null. README line 47 says MIT. TerrainBlending.cs mirrors WorldBuilder LandSurfaceManager.cs, and EdgeLineBuilder.cs exists in WorldBuilder; both sit outside the folders NOTICE names.
- Tests: 16,908 [Fact]/[Theory] in total; Core.Tests/Physics has 1,882; Core.Tests/Terrain has 54; SceneryGeneratorTests covers only IsRoadVertex. tests/AcDream.Core.Tests csproj references src/AcDream.App.
- Churn via gh api since 2026-09-24: 126 commits on main; Runtime 42, Headless 18, Core.Net 11, Core/Physics 1, Terrain 0, World 0, Rendering/Wb 1.
- DatReaderWriter (upstream HEAD source via gh api): the DatCollection constructor always creates the HighRes PortalDatabase, and MemoryMappedBlockAllocator in read mode opens with FileMode.Open. Inference: a missing client_highres.dat throws. Not confirmed against the 2.1.7 package binary.

## ISSUES
- Core.Net/NetClient.cs:19 binds the game UDP socket on IPAddress.Any:0. This must be patched to Loopback for Lifestone; otherwise it breaks the 127.0.0.1 rule and likely raises a Windows Firewall prompt. OpenAC's own Core.Net, Runtime and Headless tests have the same problem.
- client_highres.dat is effectively required: the Launcher's DatDirectoryLocator lists it as required, and DatReaderWriter's DatCollection always opens a HighRes PortalDatabase with FileMode.Open (upstream source). The user's install lacks this file, so DAT access needs a highres-optional adapter.
- Production collision in Runtime and Headless requires the baked acdream.pak (IPreparedCollisionSource from Content/Pak). A source that reads collision straight from the DATs exists only in tests. Baking writes data derived from the DATs to disk, which conflicts with the earlier prototype's stated no-derived-cache posture; the user decides.
- The Headless host relies on internal Runtime APIs (PrepareCollisionGeneration, CommitCollisionGeneration), visible only to the acdream-headless and AcDream.App assemblies. A Godot host needs an InternalsVisibleTo patch.
- Bug inherited from WorldBuilder upstream: Core/Rendering/Wb/TerrainUtils.CalculateSplitDirection picks the wrong diagonal for 49.98% of cells. It feeds the scenery slope filter: 620 of 49,157 decisions (1.26%) differ in a 380-block Holtburg sample.
- Scenery placement deviates from retail:
- - z is ground + BaseLoc.Z instead of the terrain height. 1,711 of 42,032 kept objects (4.1%) have BaseLoc.Z=0.5 and render 0.5 m too high.
- - There is no obj_within_block rejection.
- - There are no scenery conformance tests.
- The terrain shader uses the maskBlend3 approximation for overlays, not retail's sequential merge (road compositing is exact).
- Sky deviates from retail: the heading lerp uses the shortest arc and object visibility is inclusive and wraps at midnight. AnimationSequencer interpolates between keyframes, while retail CSequence uses floor; whether retail interpolates is still unresolved.
- Physics: on a failed step-up, DoStepUp restores the previous contact plane, which retail and ACE do not. No retail-oracle test covers it.
- Provenance problems:
- - Core/Rendering/Wb/SceneryHelpers.cs (verbatim ACE ObjectDesc XML docs and structure) and TerrainUtils.OnRoad (identical to ACE) are ACE-derived (AGPL-3.0) code labelled MIT, inherited from WorldBuilder.
- - TerrainBlending.cs and Content/EdgeLineBuilder.cs derive from WorldBuilder but sit outside the folders NOTICE names.
- - NOTICE points to a WorldBuilder LICENSE file that doesn't exist.
- - NOTICE lists DotRecast packages that aren't used.
- - The squashed initial import (1.34M lines, co-authored by Codex) makes history-based provenance impossible.
- - Physics naming (PhysicsGlobals, PolygonHitsSpherePrecise) suggests translation informed by ACE, but no verbatim comments were found.
- AcDream.Core.Tests references the AcDream.App csproj, so Core's tests cannot be built without Silk.NET/Vulkan. Vendoring tests needs surgery.
- OpenAC's global.json (10.0.300 latestPatch) is incompatible with the installed SDK 10.0.401 if anyone builds it in place. TreatWarningsAsErrors plus AnalysisLevel=latest makes builds fragile across SDK updates.
- UI.Abstractions depends on Silk.NET.Input, so it is not engine-agnostic. Plugins and the peer hub are woven into Runtime (14 files outside Runtime/Plugins), and the peer hub spawns acdream-headless.exe.
- Test fixtures include about 12.8 MB of data dumped from the retail DATs (LayoutDesc JSON, EnvCell/GfxObj JSON) and a retail debugger trace. These are IP-sensitive; do not import them into the Lifestone repo.

## RECOMMENDATIONS
- Do not take OpenAC as a whole. Vendor in two tiers:
- - Tier A (kernel): copy Core, Core.Net, Content, Platform and Plugin.Abstractions folders unchanged at 1ee5a08 into third_party/openac/. Add UPSTREAM.json (SHA, tree, per-file sha256) and patches/*.patch, with upstream LICENSE and NOTICE kept as-is.
- - Tier B (algorithms: terrain, scenery, sky, Vfx, meshing, textures): copy individual files into Lifestone namespaces with attribution headers and a THIRD_PARTY_NOTICES file.
- Required kernel patches, each as its own commit:
- - bind NetClient to IPAddress.Loopback for loopback servers;
- - add InternalsVisibleTo for the Lifestone host assembly;
- - open the DATs with client_highres.dat optional (Portal, Cell and Local databases individually, always DatAccessType.Read);
- - put the step-up contact-plane restore behind a switch, defaulting to retail behaviour.
- Copy Core/Physics wholesale (90 files, 31k lines, including Motion) and Core.Net (17k). They are the highest-value, engine-agnostic pieces, use the retail 0.2 s quantum, and have about 1.9k and 1k tests. For collision, either accept the Bake pak (needs the user's decision on a derived-data cache) or port the test-only InstalledDatCollisionSource to read collision directly from the DATs.
- Treat AcDream.Runtime as a 1–2 week spike, not a commitment. Write a minimal Godot host modelled on Headless/Hosting (HeadlessSessionHost.Tick order, HeadlessSessionWorldProjection collision streaming, HeadlessGameplayOperations, HeadlessLocalPlayerFrameHost) and log in to the bundled 127.0.0.1 ACE.
- - If it works, pin Runtime as a black box behind a Lifestone-owned IGameState facade so it can be replaced later.
- - Otherwise, keep it as reference only and build a thinner runtime on Core.Net plus Core models.
- Terrain: port TerrainBlending and LandblockMesh (verified correct) and rewrite the shader in Godot shading language using the exact sequential merge rather than maskBlend3. Never use Core/Rendering/Wb/TerrainUtils' CalculateSplitDirection, GetNormal or GetHeight.
- Scenery: port SceneryGenerator with four fixes:
- - use the correct split for the slope normal;
- - set z to the terrain height (drop the BaseLoc.Z offset);
- - add the obj_within_block check;
- - compute the quadrant in double.
- Add a conformance test for 0xA9B3 (177 candidates / 30 out of block / 5 on road / 142 survivors). If Lifestone wants a permissive licence, reimplement SceneryHelpers and OnRoad from rendering.md §4 rather than copying the ACE-derived files.
- Sky and lighting: port SkyState, SkyDescLoader and Lighting, but switch the heading lerp to linear and object visibility to strict Begin<t<End with no wrap, to match retail.
- Keep App (renderer, UI, palette/clothing pipeline, EnvCell renderer) as reference only. Its Vulkan, bindless and multi-draw-indirect design does not map onto Godot.
- Never run OpenAC's tests, Launcher, tools/*.ps1 or the self-hosted workflows on the development machine. If tests must run, first patch NetClient, and set TEMP and ACDREAM_ROOT_DIR to the scratchpad per process.
- Do not import OpenAC's test fixtures, which hold LayoutDesc/EnvCell JSON dumped from the DATs and a retail trace log. Re-derive any Lifestone test fixtures from the user's own DATs at test time.
- Decide the Lifestone licence before copying anything. If it is permissive (MIT), exclude or reimplement Wb/SceneryHelpers.cs and TerrainUtils.OnRoad (ACE-derived, AGPL) and record that the WorldBuilder licence rests only on its README. If it is AGPL (as the earlier prototype is), these provenance concerns become moot.
