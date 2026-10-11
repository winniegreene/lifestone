# Third-party sources: spell viewer track

This track covers:
- `src/Lifestone.Game/scripts/SpellViewer/` and `src/Lifestone.Game/scenes/SpellViewer.tscn`;
- `src/Lifestone.Game/scripts/Render/Particles/`;
- `src/Lifestone.Client/Effects/`;
- `tests/Lifestone.Client.Tests/Effects/`;
- `docs/spell-viewer.md`;
- one commented block at the top of `Main._Ready` in `src/Lifestone.Game/scripts/Main.cs` (the `--scene=spellviewer` switch).

## Code ported into the repository

None. Every file is written for Lifestone. The simulation it draws is the effects track's
(`Lifestone.Data/{Animation,Effects,Magic}`, sources in [effects.md](effects.md)); the render path is the game-core
track's ([core.md](core.md), [render-contract.md](../render-contract.md)); the terrain is the terrain renderer's
([terrain-renderer.md](terrain-renderer.md)).

## References, not copied

| Source | Licence | Pinned at | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, Hex-Rays decompile of acclient 11.4186 (`acclient.c`) | MIT label; the code is Turbine IP | fetched 2026-10-10 | Retail behaviour, re-expressed and cited by function name (table below). **No decompiled code is in the repository.** |
| ACEmulator/ACE `Source/ACE.Server/WorldObjects/Player_Magic.cs` (`DoCastSpell_Inner`, `FailCast`) | AGPL-3.0 | v1.78.4816, 95ef426d | What a fizzle looks like: the whole cast plays, then `GameMessageScript(Guid, PlayScript.Fizzle, 0.5f)` on the caster instead of any effect (`SpellStage.ResolveVisual`). |
| Godot 4.7 class reference (`RenderingServer.MultimeshSetBuffer` layout, `MultiMesh`, `INSTANCE_CUSTOM`, Movie Maker `--write-movie`), via the GodotSharp 4.7.2 XML docs in the NuGet cache | MIT (engine), CC-BY 3.0 (docs) | 4.7.2 | API usage only. |
| FFmpeg 8.1.1 (a local install on PATH; not bundled) | LGPL/GPL | 8.1.1 | Run locally, outside the product, to encode our own rendered frames into `docs/screenshots/m1/spellviewer/showcase.mp4` and the highlight GIF/WebP (gitignored). Not shipped, not linked. |

## Retail functions re-expressed (product code)

| Lifestone | Retail function (11.4186) | What |
|---|---|---|
| `Client/Effects/DrawFrameRules.cs` | `CPhysicsPart::calc_draw_frame` (acclient.c:315066), `CPhysicsPart::UpdateViewerDistance()` / `(float, Vector3*)` (315098, 315173), `CPhysicsObj::UpdateViewerDistance` (317913), `Position::get_offset` (314677), `Frame::set_vector_heading` (357668, via the effects track's `EffectActor.SetVectorHeading`), `Frame::rotate_around_axis_to_vector` (357520), `Render::SetDegradeLevelInternal` thresholds (via `DegradeSelector`) | Degrade modes 2–5 (billboards), viewer distance and heading per part or per object, LOD distance `CYpt / gfxobj_scale.z`, the player's own parts at level 0. |
| `Client/Effects/PartTranslucencyRules.cs` | `CPhysicsPart::SetTranslucency` (315488), `CPhysicsPart::CurSettingsAreDefault` (314711), `CMaterial::CheckAlphaValues` (360585), `SetTranslucencySimple` (360594) and constructor (360635), `RenderDeviceD3D::SetCurrentMaterial` (457648), `D3DPolyRender::SetSurface` (454385) | A particle's translucency: hidden at exactly 1.0, a part material of alpha 1 − t that replaces the vertex alpha, and the blend switch to SRCALPHA/INVSRCALPHA for surfaces that do not already blend without an alpha test. |
| `Client/Effects/ParticleDrawList.cs` | `ParticleEmitter::SetInfo` (330909: one `CPhysicsPart` of the hardware GfxObj per particle, `degrade_distance`), `Particle::Update` (330313: part frame, `gfxobj_scale`, translucency), `CPhysicsObj::ShouldDrawParticles` (317184) | Which particles are drawn, with which GfxObj level and frame. |
| `Client/Effects/ActorPartDraws.cs` | `CPartArray::UpdateParts` (326601), `CPartArray::SetPlacementFrame(0x65)`, `CPartArray::SetScaleInternal`, `CPhysicsPart::Draw` (314587) | Per-part frames, mesh scale, LOD level and draw frame of an actor. |
| `Client/Effects/StageSite.cs` | none (a Lifestone geometry check, not retail behaviour) | Where the viewer stands its actors in Holtburg, and the clearance probe over the LandBlockInfo buildings and statics (posed like the statics layer: `ModelAssembly.ComputeRestPose`, the static's frame, `SurfaceDrawRules` for buildings) that keeps them out of walls. |
| `Game/scripts/Render/Particles/ParticleShaders.cs` | `copyVert`, `RenderMeshSubset` (via the render contract's surface shaders) | The contract's shader plus the per-instance part-material alpha. |
