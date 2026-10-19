# Interiors and dungeons (M1 wave 2)

**Track:** interiors (EnvCells). **Owns:** `src/Lifestone.Game/scripts/World/Interiors/`, `src/Lifestone.Client/Interiors/` (engine-agnostic, tested by `tests/Lifestone.Client.Tests/Interiors/`) and this document. Sources: [third-party/interiors.md](third-party/interiors.md).
**Builds on:** the cells track's data layer (`src/Lifestone.Data/World/Cells/`, its [README](../src/Lifestone.Data/World/Cells/README.md)), the render contract ([render-contract.md](render-contract.md)) and the game core ([game-core.md](game-core.md)).

Retail behaviour is cited by function name and `acclient.c` line of the Mag-nus/acclient-AI-RE decompile of 11.4186 (the render core is unchanged in the user's 11.6096). Nothing from it is copied.

**Status.** Building interiors and dungeons render with their real surfaces, burned-in torch lighting and their furniture; only the cells retail's portal walk reaches are drawn; the landscape disappears in dungeons and shows only through doors and windows indoors; you can fly or walk in, teleport to curated dungeons, and dungeons run at 2,000–2,500 fps (vsync off). Outdoors only retail's landblock stab lists are meshed, and the per-cell draw state lives in a texture, not in Godot instance uniforms (the review found those overflowing the engine's buffer). Screenshots (gitignored, `docs/screenshots/m1/`):

| File | What it shows |
|---|---|
| `interiors-tour-town-network.png` | The Town Network hub: fountain, trees, red brick, lit by its wall lights |
| `interiors-tour-holtburg-redoubt.png`, `interiors-tour-mines-of-despair.png`, `interiors-tour-glenden-wood-dungeon.png`, `interiors-tour-marketplace.png` | Four more dungeons at their portal drop points |
| `interiors-tour-training-academy.png` | The Training Academy start room (books, carpet, desk): 2,163 cells, the densest landblock here |
| `interiors-tour-holtburg-shop.png` | Inside the Holtburg shop: counter, potions, lanterns, the far door open onto the street |
| `interiors-tour-holtburg-smithy.png` | Inside the Holtburg smithy: quench tub, grindstone, a window onto the hills |
| `interiors-holtburg-looking-out.png` | From inside the shop, out through its west door into the street |
| `interiors-holtburg-doorway-13m.png`, `interiors-holtburg-doorway-35m.png` | From the street into the shop's doors at 13 m and 35 m: no seams along the jambs and lintels (review views) |
| `interiors-arch-1134-near.png`, `interiors-arch-1134-far.png` | A 9-cornered arch (landblock 0x1134): the interior stays inside the arch (review views) |
| `interiors-8e51.png` | `--start=8E51`, the desert region that overflowed Godot's instance-uniform buffer before the review fixes: 636 meshed cells, no engine errors |
| `interiors-walk-redoubt.png`, `interiors-start-landblock-0163.png` | Walk mode in a dungeon; `--start=0163` (a dungeon landblock) starting inside |

Taken before integration, with the render-contract statics smoke layer where buildings matter and core's placeholder terrain (vertex-coloured green, so grass seen through doors and windows is flat green). The smoke layer (`--smoke-statics`) was retired in M1 wave 3: the objects layer always draws the building shells now.

## 1. What happens each frame

```
InteriorsLayer (ILandblockLayer)                    InteriorController (Node, priority 5: after the camera, before the world)
  worker: every cell as data, doorways, lights;       1. CellTracker.Update(eye)        which cell is the viewer in?
          for retail's stab list (or the viewer's     2. walk mode: CellWalker.FollowFloor
          dungeon): one merged mesh per cell (its     3. InteriorVisibility.Compute     retail portal walks (PView)
          polygons + its objects, burned lights)      4. Apply: show/hide cell nodes, cell-state texels (front,
  main:   per cell a state-table slot and one hidden      doorway rows), doorway planes, landscape on/off, ambient
          MeshInstance3D → InteriorController         5. InteriorFocus: indoor streaming focus in dungeon cells
```

Everything that decides *what* is engine-agnostic (`Lifestone.Client.Interiors`); the Godot side only builds nodes and sets flags.

## 2. Streaming and meshes (`InteriorsLayer`)

- **Which cells (retail).** Outdoors retail loads only what its buildings can show: `LScape` calls `CLandBlock::init_buildings` (acclient.c:352114) and `CLandBlock::grab_visible_cells` (351601) for every landblock of the grid (306602-306603); `init_buildings` merges every doorway's stab list into the landblock's (`CBuildingObj::add_to_stablist` 719041 → `CBldPortal::add_to_stablist` 362423), and `grab_visible_cells` loads exactly those cells. A town's cellars and the dungeons under or beside it are not loaded. Indoors, `CEnvCell::grab_visible_cells` (350172) loads the viewer's cell and its stab list on every cell change (plus the landscape's list in a seen_outside cell).
- **Which cells (Lifestone, `InteriorCellSets`).** Every EnvCell of a streamed landblock is read as **data** (B-tree range `CellDiscovery.EnumerateEnvCellIds`, not `num_cells`): the tracker finds the viewer's cell among them and a free camera can fly into a dungeon. **Meshes** are built only for retail's landblock stab list (`BuildingStabCells`). When the viewer's cell, or a cell of its stab list, is outside that list (a dungeon, a cellar, the stone room 0xA9B40183), the whole landblock is meshed instead (`NeedsAllCells`): a superset of any cell's stab list there (stab ids are landblock-local), loaded once rather than per cell change. The worker reads the viewer's cell when it prepares the landblock (a teleport into a dungeon places the cell before streaming starts); if the viewer enters such a cell after its landblock was streamed, `InteriorsLayer.RequestAllCells` meshes the rest on a worker and attaches it in budgeted steps. Around Holtburg 430 of the 695 cells are meshed, around 0x8E51 (desert) 636 of 3,154; in a dungeon the streaming focus is indoor (§5), so only the dungeon's landblock is loaded and all of it is meshed.
- **Worker** (`PrepareAsync`):
  - `LandblockCellSet.Load`, the buildings' doorway polygons (`BuildingPortalGeometry.Load`), and `LandblockInteriors` (cell bounds in landblock space; dungeon cells reach hundreds of metres outside their 192 m square; the landblock stab list).
  - Per meshed cell, **one mesh** (`CellMeshMerger`) in landblock space, one surface per material:
    - the cell's polygons: `CellMeshCache.Get` (retail `D3DPolyRender::ConstructMesh`: every drawing polygon, untextured subsets dropped, sides, WRAP/CLAMP; cells track) with the burned-in static lighting per vertex (§4.3). Cells whose struct has no drawing BSP get no polygons (`PView::DrawCells` skips them) but keep their objects;
    - its static objects, created and posed by the objects path (`PreparedCellObjects`, docs/objects.md §2.6; objects whose model does not load are not created) with the objects' draw rule (untextured subsets drawn), each part with its hardware-light sum (§4.4). A part with a degrade table (or of an animated static) contributes every LOD level, tagged in `CUSTOM2` with its slot in the objects' LOD-state table and its level; the cell-object shader draws only the level retail would and turns camera-facing levels to the viewer (M1 wave 3; before it, every part drew level 0). Their default scripts (torch flames, braziers) run as living statics, drawn on the cells' layer for cells in view.
  - The light RGB travels in `CUSTOM1.rgb`; `CUSTOM1.a` is left for the cell's slot in the cell-state table (§3.4).
  - Surfaces are resolved once per (Surface, addressing, lighting) for the whole landblock, so a texture used by 50 cells is decoded once. Temporary arrays come from `ArrayPool`; only the final vertex arrays are allocated.
- **Main thread** (`Attach`, stepwise): one step registers the landblock, then the cells are built in time slices of about 0.75 ms (one surface is never split): each gets a slot (`CellSlotAllocator`), its `ArrayMesh` and one hidden `MeshInstance3D` on Godot visual layer 11 (`InteriorController.CellsLayerMask`), registered with the controller once complete. Steps over 4 ms are logged with the GC pause that fell in them (`slow attach step ... gc_pause_ms`).
- **Why one mesh per cell.** The per-cell draw state (§3.4) must reach the shaders of the cell and of its objects. Godot's instance uniforms reserve a 16-vec4 block of the global shader-uniform buffer for every instance whose material declares one, hidden or not: about 4,000 instances with the default buffer size, shared by every track. The first version gave every cell and every object part such uniforms and overflowed the buffer in ordinary exploration (2,995 engine errors at `--start=8E51`; landblock 0x00D1 alone has 4,213 cells and 5,999 parts), losing the state past the limit. Now the state lives in a global texture and the slot is vertex data, which a shared part mesh could not carry, hence one merged mesh per cell. Side effects: far fewer nodes and draw calls; object parts shared by many cells are copied into each (memory below); blended parts of a cell sort with the cell, not one by one.
- **Start-up warm-up.** The 46 cell variants are compiled and each gets one surface of the cells' vertex format (with a fresh shader cache the compile otherwise landed in the first `AddSurface` of each variant, 58-79 ms each, measured); then the whole attach path runs once on the Holtburg smithy cell, including a view from inside it and one from outdoors through a doorway (`InteriorsLayer.WarmUp`, about 0.1 s).
- **Detach** releases the materials at once, frees the cell-state slots, and frees the cells' meshes 8 per budgeted step (a town's ~120 mesh frees took 10 ms in one go, measured).
- **Memory** (cell meshes with their objects, approximate GPU bytes): 4.9 MB for the 430 meshed cells around Holtburg, 5.7 MB for the Training Academy's 2,163; textures are in the render caches as usual.

## 3. Visibility

### 3.1 What retail does

| Viewer | Retail | Source |
|---|---|---|
| Outdoors | `LScape::draw`: terrain, objects, buildings; each drawn building is looked into through its doorways | `SmartBox::RenderNormalMode` (144867) |
| In an EnvCell | `DrawInside` → `PView::DrawInside`: the portal walk from the viewer's cell over its stab list; the landscape is drawn only through outside portals | `PView::DrawInside` (462467), `PView::DrawCells` (461450) |

- **Indoors** (`PView::DrawCells`): if the walk saw an outside portal, `LScape::draw` runs first (sun on), clipped to those views; then **the Z buffer is cleared** (`RenderDevice::Clear` flag 4 = Z, `RenderDeviceD3D::Clear` 457563); the outside portal polygons of the visible cells are written into depth only (`D3DPolyRender::DrawPortalPolyInternal(p, zClear = 0)`, `maxZ2 = 6`: actual depth, depth test ALWAYS, alpha 0); then the cells back to front and their objects (sun off).
- **Outdoors, a building** (`RenderDeviceD3D::DrawMeshInternal` 456960 → `BSPTREE::build_draw_portals_only` twice): pass 1 draws each visible doorway polygon with `zClear = 1` (`maxZ1 = 7`: depth 0.999999, i.e. **depth reset to far inside the doorway**, invisible); pass 2 runs `PView::ConstructView(CBldPortal*)` (462507) → `PView::DrawPortal` (462565): the walk from the doorway's cell over the doorway's stab list, cells drawn without a Z clear. A doorway is looked through only from its outer side (`portal_side`).
- **The walk** itself (view cones clipped portal by portal, stab lists, eye-side test, `OtherPortalClip`) is the cells track's `PortalVisibility`, re-expressed in 3D and tested against a ray oracle (cells README).

### 3.2 Which cell is the viewer in (`CellTracker`)

Retail moves objects between cells in physics: `CObjCell::find_cell_list` (346961) gathers the current cell plus what `find_transit_cells` adds (an EnvCell's neighbours whose BSP the sphere touches, `CEnvCell::find_transit_cells` 348250; outdoors, the doorway cells of nearby buildings, `CBuildingObj::find_building_transit_cells` 719068 → `CEnvCell::check_building_transit` 348110) and keeps the first EnvCell whose BSP contains the centre (`point_in_cell`). Lifestone treats the camera's eye as a point:

1. **In a cell:** stay while its BSP contains the eye; else a portal neighbour or a stab-list cell that contains it (`CEnvCell::find_visible_child_cell` 349698 order); else, if the eye's path crossed an outside portal polygon, outdoors.
2. **Outdoors:** if the eye's path crossed a building doorway polygon, the doorway's cell or a cell of its stab list that contains the eye (a fast camera can pass the doorway cell in one frame).
3. **Through a wall (free camera only; retail's collision prevents it):** every loaded cell whose BSP contains the eye **and** that encloses it (a drawing polygon of the cell above and below the eye, `CellQueries.IsEnclosed`). The BSP test alone is fooled: a cell BSP is a convex region and reaches outside non-convex rooms (the test `Outdoor_points_around_the_town_are_only_taken_for_rooms_inside_a_building` finds such grid points around Holtburg and checks that the search never takes them).
4. **Teleports and starts** set the cell directly (`Place`), reading the EnvCell from the dat before its landblock streams in, as retail's position updates carry the cell id.
5. **Jumps that bypass `Place`** (a teleport panel that calls `WorldRoot.Teleport` and moves the camera itself): the streamer drops every landblock, the viewer's included, which cannot happen otherwise (the focus keeps the viewer's landblock). `CellTracker.LandblockUnloaded` notices it and the next update starts from outdoors (and searches) instead of keeping the stale cell, so the focus follows the camera again. A dungeon destination still needs `PlaceCamera` (§6).

The Holtburg smithy's first three building portals are horizontal openings in its roof: dropping through one is a doorway crossing. Cell 0xA9B40183 is a small stone room set into the ground beside the town that belongs to no building; the search can find a camera in it, retail could only teleport there.

### 3.3 The walks (`InteriorVisibility`)

- **Viewer in a cell:** `PortalVisibility.FromInside` from the eye, clipped by the camera frustum (`ViewFrustum`: the four side planes through the eye; retail clips in screen space against the same rectangle; 1° margin). Drawn: the walk's cells. Landscape: drawn when the walk reached an outside portal (`OutsideVisible`); then the buildings outside are looked into as well, since retail's `LScape::draw` draws them with their doorway views.
- **Viewer outdoors:** every building in the landblocks within 2 rings of the eye whose doorway faces the eye, lies within 400 m, in front of the camera and inside the frustum gets `PortalVisibility.FromOutside`. Retail looks into every building it draws; a doorway two landblocks away covers a few pixels.
- Each visible doorway is reported with its plane, its cone from the eye and its distance (`DoorwayView`), and every cell seen through doorways with the doorways (at most 2) whose cones reach its bounds.
- **Cost:** 0.1–0.3 ms per view in steady state (dungeons and Holtburg, the tour's turn), at most 0.68 ms of work over a whole flight to Arwic with 40-odd doorways in view (§7; the "slow views" there were all GC pauses that fell in them, which the `slow view` log line now separates). Recomputed only when the eye, the view direction, the viewer's cell, the loaded set, the render origin, the viewport size or the field of view changes. The start-up warm-up (§2) runs one indoor and one outdoor view, so the first real ones pay no JIT.

### 3.4 Drawing it in Godot

Godot cannot clear depth between two draws of its opaque pass, clip a draw to a screen polygon, or order opaque draws. Each retail step is emulated:

| Retail | Lifestone |
|---|---|
| Only the walk's cells are drawn | Each cell is one node; only the visible ones are shown (only the difference is touched) |
| (per-cell state) | Each cell's mesh carries its slot in `CUSTOM1.a`; the global RGBA8 texture `ac_cell_states` (128 × 128 slots) holds one `CellDrawState` texel per slot: R = in front, G and B = doorway rows + 1. Only changed texels are written, uploaded once per frame. No Godot instance uniforms (§2) |
| Landscape not drawn from a cell unless an outside portal is in view | The camera's cull mask drops to layer 11 (cells and their objects only) and its environment clears to black, as retail's `SceneTool::BeginScene` (122402) clears to black every frame (the sky covers it outdoors) |
| Indoors: Z cleared, then the viewer's cells | Cells of the viewer's walk (with their objects) are marked "front" in their texel: the vertex shader moves their reversed-Z depth into [0.5, 1] (`z' = (z + w) / 2`), so they cover everything outdoors that is not closer than twice the near plane, while keeping their own order. This is what keeps terrain level with or above a floor out of the rooms: 11 of Holtburg's 12 buildings have floor vertices exactly at terrain height (the smithy's ground floor is the terrain's 66 m, which without this showed as a terrain-coloured floor), and three have terrain up to 4–5 m above parts of their floors |
| Outdoors: depth reset inside the doorway, then the cells through it, clipped to their views | Cells seen through doorways get up to two rows of the global texture `ac_doorway_planes` in their texel. A row holds a visible doorway's plane and its cone from the eye in world space: 1 + 10 columns, because building doorways have up to 10 corners (`DoorwayRows.MaxConePlanes`, pinned by an exhaustive test over all 16,937 doorways; the first version held 7 and drew interiors over the shell beside 143 arched doorways). 128 rows, given to the nearest doorways first; a cell whose doorways all lost their row is drawn unclipped and counted. Rewritten on every view change and on a rebase. The fragment shader moves the depth of what is inside the cone to just in front of the doorway's plane (`max(z, z_plane + 0.001 z)`), so the floor beats terrain inside the building while a tree in front of the door still covers the interior |
| (seams) | Retail rasterizes the doorway polygon and the shell with the same rules; Lifestone clips cells to an exact cone while the shell's opening is rasterized separately, and the two disagreed by a pixel along doorway edges (1-px green or sky lines, worse with MSAA, where a discarded pixel loses all its samples). A fragment within 1.5 px outside the cone (`DoorwaySeamPixels`, an angle × its distance) is kept at its **own** depth: where the shell covers it the shell wins, where the shell's opening is wider the cell fills the seam. Only fragments beyond the band are discarded |

Differences that remain (all small on screen):
- **Outside portal polygons are not written into depth.** Retail does it so that cells *behind* an opening (another wing of the same building seen through a window) cannot cover the landscape seen through it. Lifestone would draw such cells over it.
- **The landscape is not clipped to the outside views.** Through a door or window the whole landscape is drawn and the cells cover the rest, which is retail's picture except for outdoor geometry *inside* the opening's frame but in front of it (none seen).
- **A cell is clipped to at most two doorway cones** (retail keeps every view). A room seen through three doors at once would lose the third view.
- **More than 128 doorways in view**: the farthest ones' cells are drawn unclipped (no depth reset); the HUD's view counts them (`UnclippedCells`). The Arwic area shows 41-47 doorways at once.
- **Writing `DEPTH`** in the cell shaders turns off early depth testing for cells; dungeons still run at ~2,000 fps (§7).

## 4. Lighting

### 4.1 What retail does

- **World ambient** is chosen by the viewer's cell, `CellManager::ChangePosition` (146646):
  - outdoors or in a `seen_outside` cell: the sky's object ambient, `SmartBox::SetWorldAmbientLight(LScape::calc_object_light(), ambient_color)` — the render contract's `ac_ambient`;
  - in a cell that is not `seen_outside`: `SetWorldAmbientLight(0.2, 0xFFFFFFFF)`, and the landscape is released. The periodic relight that would overwrite it (`LScape::UseTime` 307222 → `set_landscape_lighting` 307003) only runs while the landscape is loaded (`loaded_cell_id`).
- **No sun on cells or their objects.** `SmartBox::RenderNormalMode` turns the sun on for `LScape::draw` only; `PView::DrawCells` calls `Render::useSunlightSet(0)` (380646) before drawing the cells and their objects, from inside and from outside alike.
- **Cells** are lit by their **burned-in static lights** (§4.3) as emissive plus the world ambient: `D3DPolyRender::RenderMeshSubset` (454633), for meshes built with `burn_in_static_lights` (`CEnvCell::UnPack` passes 1, acclient.c:349389), takes the emissive colour from the vertex colour (`FromVertex`) and sets material ambient = diffuse = `diffuseVal` (1.0, 45775). The Surface's luminosity does not reach cells. Dynamic lights would be hardware lights (`Render::minimize_envcell_lighting` 379652); M1 had none; M3 4.1 adds them (§4.6).
- **Objects in cells**: luminosity + world ambient + up to 8 hardware point lights (§4.4).

### 4.2 How Lifestone does it

The render contract's surface shaders gained one variant field (`SurfaceShaderVariant.Lighting`, `SurfaceLighting` in `Lifestone.Client.Rendering`; `SurfaceRequest.Lighting`), with the code in `scripts/Render/SurfaceLightingCode.cs`:

| Lighting | `v_light` | Used for |
|---|---|---|
| `Object` (default, unchanged) | `luminosity + ac_ambient + sun` | everything outdoors |
| `EnvCell` | `CUSTOM1.rgb + world_ambient` | cell polygons (CUSTOM1.rgb = burned static lights, bytes / 255) |
| `CellObject` | `luminosity + world_ambient + CUSTOM1.rgb` | objects in cells (CUSTOM1.rgb = hardware-light sum; 0 where no light reaches) |

`world_ambient = ac_indoor_ambient_on ? ac_indoor_ambient : ac_ambient`. Two global uniforms in the interiors block of `project.godot` (`ac_indoor_ambient` = 0.2 white, `ac_indoor_ambient_on`; the other two are the tables of §3.4), set by the controller while the viewer is in a cell that is not `seen_outside`. Nothing outdoors is drawn then, so the outdoor variants need no switch. The 46 cell variants (textured ones for `EnvCell`, all for `CellObject`) are compiled at start-up (65–410 ms, the larger with a cold shader cache), with one surface each of the cells' vertex format (§2).

### 4.3 Burned-in static lights (`CellStaticLighting`)

- **Which lights** (retail): a static object whose Setup has a light gets a `LIGHTLIST` (`CPartArray::InitLights` 326321) whose lights are static (state bit 1) and added to the object's cell (`CPartArray::AddLightsToCell` 325212); the light's frame is the object frame (`LIGHTLIST::set_frame` 325029) combined with the light offset. On a cell change `CEnvCell::flush_cells` (349880) adds the static lights of every cell in `visible_cell_table` (the viewer's cell and its stab list, `CEnvCell::grab_visible_cells` 350172) through `Render::insert_light` (380524), which keeps them sorted by the squared distance from the **object** origin to the viewer cell's origin and keeps the nearest `max_static_lights` (40, acclient.c:45530; the degrade controller moves it between 40 and 60). All 608 Setups with a light have one, of type 0 (point); the Town Network has 171, the Training Academy 323.
- **The burn** (`D3DPolyRender::SetStaticLightingVertexColors` 454918, `calc_point_light` 454579): per vertex and light in cell-local space, `range = falloff × 1.3` (`static_light_factor`, 45774); within range, `s = (0.5 d + n·D) / 1.5`, `k = s / d` (d ≤ 1) or `s / d³`, `× (1 − d / range) × intensity`, and each channel adds `min(k × colour, colour)`; the sums are clamped and truncated to bytes. With intensities of 20–100 a light paints its colour fully within most of its reach. Cell geometry is sparse (a 10 m dungeon room has 5–13 vertices), so light pools are vertex gradients across walls and floors, as in retail.
- **Approximation (deterministic):** each cell is burned once, on the worker, with the list it would get with the viewer in that cell. Retail burns every drawn cell with the *viewer's* list and re-burns only when the light count changes (`onlyOnce` = 1, 45776, and the count in `burnedInStaticLights`), so a cell's lighting depends on the path the player walked. The two agree for the viewer's own cell; they can differ only where a light outside the viewer cell's stab list (or beyond its nearest 40) reaches geometry visible from it.

### 4.4 Objects in cells (`CellObjectLighting`)

`RenderDeviceD3D::DrawMeshInternal` calls `Render::minimize_object_lighting` (380659) for every part drawn with the sun off: up to 8 lights, dynamic first, then the static list's lights that reach the part's drawing sphere (`Render::viewconeCheck(gfxobj->drawing_sphere)`). Each is a D3D point light (`PrimD3DRender::config_hardware_light` 453119): diffuse = colour × intensity, range = falloff × `rangeAdjust` (1.5, 45742), attenuation (0, 1, 0) = 1/d. The objects never move, so Lifestone sums `colour × intensity × max(0, N·L) / d` per vertex on the CPU, clamps it and gives parts that some light reaches a mesh of their own with the sum in CUSTOM1; the rest share the plain mesh. The comparison in the static-light test was lost by the decompiler in both builds (11.4186 and 11.6096 show the sum `falloff + radius` and only the x87 flags of the test); Lifestone keeps lights whose distance from the sphere centre is at most `falloff + radius`. **`TODO(interiors-light-reach)`**: that rule leaves out a light between `falloff + radius` and `falloff × 1.5 + radius` away although its hardware range reaches the object; whether retail does too needs the operands (one byte read of `Render::minimize_object_lighting`, retail-addresses-m2.md §3.1). **Moving objects** (the player, the server's creatures) get the same rule with one sphere for the whole object instead of one per part; for the human at the Training Academy's start both give one light of 22 (`MovingObjectLightEvidenceTests`). Their rows reach the shader through `ObjectLightTable`, which uploads itself before every draw (M2 5b b0: nothing used to call its `Flush`, so every moving object in a cell showed the ambient alone). Since M3 4.1 their rows hold the dynamic lights first, ahead of the static ones (§4.6).

### 4.5 Fog

Cells keep the render contract's fog (`ac_fog_*`). Retail stops updating fog when the landscape is released (`LScape::UseTime` needs `loaded_cell_id`), so a dungeon keeps the last outdoor fog; with dungeon distances well under the fog start (150 m at noon) neither shows any.

### 4.6 Dynamic lights (M3 4.1)

Dynamic lights are the lights that move: the viewer light, a projectile's light, a creature's own light. They form one global list (`DynamicLightList`, `Lifestone.Client.Interiors`, engine-agnostic), which `AvatarSet` rebuilds once per render frame after its `Advance` into `DynamicLightFrame`. The row writers and the cell glow read that frame.

- **The list.** At most 7 lights, nearest the viewer first, kept as retail's `insert_light` (11.6096 0x0054DDC0) keeps them, with the adds, moves and removals of `Render::add_dynamic_light` (0x0054E030) and `remove_object_light` (0x0054CDC0). The farthest is dropped when the list is full. The viewer light is never dropped (a named deviation: retail's list may push it out). The frame copies the list into fixed storage with no allocation.
- **The viewer light** (`SmartBox::set_viewer`, 11.6096 0x00452C80): white (0xFFFFFFFF), intensity 2.25, falloff 10, 2 m above the player (z only; 0 for a viewer that is not the player). `set_viewer` runs on every drawn frame, called from `SmartBox::update_viewer` through `SmartBox::DrawNoBlit`: it clears the dynamic list, adds the viewer light and calls `CObjCell::add_dynamic_lights`. That cadence is settled (`m3-viewer-light-cadence`, closed). `--viewer-light=on|off` (default on, group "Lights") turns the light off.
- **The sources.** A non-static object whose Setup has a light registers it on create, using the Setup's first light only (`m3-setup-multi-light`). Its position is the body's frame plus the light's offset, rotated. It is lit while its `LightingOn` state bit is set and `EffectVisualState.LightsOn` agrees. It leaves the list on F747 and goes off when `LightingOn` clears or the SetLight PhysicsScript runs (0x33000109, pinned by `SetLightScriptPinTests`). A parented or cell-less object's light is off, and a Static object adds none (its light is a cell's static light, §4.3). The list reports loudly past 64 sources (`m3-dynamic-light-sources`).
- **The object rows** (`ObjectLightSets.Select` with the frame, `PlayerView.WriteLights`, `ServerObjectsLayer.UpdateLights`): the dynamic lights first, then the static ones, 8 at most, as `Render::minimize_object_lighting` (0x0054E090) takes them (§4.4). Each light's reach is falloff x 1.5, the hardware range (`PrimD3DRender::config_hardware_light` 0x0059BD40). The row writer keeps the old reach rule for both kinds, distance at most falloff + radius (`TODO(interiors-light-reach)`). A row that holds a dynamic light is rewritten every frame, because the table skips an unchanged row. Outdoors the object shader takes no row, so the dynamic lights reach objects in cells only (`TODO(m3-dynamic-lights-outdoors)`; retail's outdoor condition is not read).
- **The cell glow** (`DynamicLightTable`, `scripts/Render`). One global `ac_dynamic_lights`, a 16 x 1 RGBA32F texture: texel 2i holds light i's position in Godot render space and its reach, texel 2i+1 its colour times intensity, for 8 lights. `FramePreDraw` repacks it from the frame (`DynamicLightTexels.Pack`, pure) and uploads it only when it changed. A texture narrower than 16 texels has no lights. The global is declared at run time by `DynamicLightTable.Declare()`. The term is per vertex (D16: no per-pixel term, no `OmniLight3D`): `dyn = min(1, sum of colour x intensity x max(0, N.L) / d)` over the lights with d at most the reach; a zero normal gets nothing.
  - EnvCell: `v_light = clamp(CUSTOM1.rgb + dyn + world_ambient x diffuse, 0, 1)`.
  - CellObject: `v_light = clamp(luminosity + world_ambient x diffuse + CUSTOM1.rgb + dyn, 0, 1)`.
  - DynamicCellObject is unchanged: its rows already hold the dynamic lights, and the table would count them twice.
  - Particles in cells take the same dynamic term. `CPhysicsPart::Draw` (0x0050E270) calls `RenderDeviceD3D::DrawMeshInternal` (0x005A0470), which runs `Render::minimize_object_lighting` for every part drawn with the sun off (11.4186 acclient.c 314587 and 456960); the `TODO(objects)` of `ParticleShaders` is closed. The static cell lights are not summed for a particle, because the particle mesh has no light sum (`TODO(particle-cell-static-lights)`).
  - No new shader variant: the cell variants are compiled by the interiors' and the server objects' warm-ups, so nothing compiles on first use.
  - The counter `dyn_light_cells` (report-only) counts the shown cells whose mesh bounding box is within a dynamic light's reach this frame (`InteriorController.DynLightCells`).
- **Open TODOs.** `interiors-light-reach` (the byte read of the reach comparison needs the owner, §4.4), `m3-dynamic-lights-outdoors`, `m3-bolt-cell-tracking` (a projectile's light follows its body's cell, which is the creation cell until the server moves the body), `particle-cell-static-lights`, `m3-setup-multi-light`, `m3-dynamic-light-sources` (the 64 cap), and `m3-showcase-lights` (the spell viewer owns no `AvatarSet`; its field is `lit_projectiles`, report-only).

**The projectile's cell (M3 4.2 x2).** `ProjectileCellFollower` (Avatars) hands a flying projectile from cell to cell by its centre point (retail `UpdateObjectInternal` 0x005161B0 -> `find_cell_list`); a step through a wall keeps the cell. `ServerObjectsLayer` gives every set its follower. `TODO(m3-bolt-cell-tracking)` is closed; the sphere test and client collision stay (`TODO(physics)`, D24).


## 5. Streaming focus

`InteriorFocus` wraps the camera as the world's `IStreamingFocusSource`: while the viewer is in a cell that is not `seen_outside`, the focus is `StreamingFocusKind.Indoor` at the centre of **the cell's landblock** (`IndoorLoadRadius` 0: just that landblock), which is retail's `LScape::release_all` in `CellManager::ChangePosition`. The cell's landblock, not the camera's: dungeon cells can lie hundreds of metres outside their square (Holtburg Redoubt reaches y = −85, Aerbax Haven −610), so the camera's own landblock would be the wrong one. In `seen_outside` cells the focus stays outdoor, as retail keeps the landscape.

## 6. Locations and teleporting

`InteriorLocations` (curated; `--start=KEY`, F7 / Shift+F7 cycles them in game):

| Key | Place | Cell, landblock-local feet position | Source |
|---|---|---|---|
| `town-network` | Town Network (498 cells) | 0x00070143 (70, −60, 0.005) | ACViewer `Locations.txt` @c5c54fb ("Town Network \| POI"); ACE `GameConfiguration` names 0x0007 "Town Network" |
| `holtburg-redoubt` | Holtburg Redoubt (178) | 0x016301AC (140, −40, 0) | ACViewer `Locations.txt` |
| `marketplace` | Marketplace (536) | 0x016C01BC (49.206, −31.935, 0.005) | ACViewer `Locations.txt` |
| `mines-of-despair` | Mines of Despair (537) | 0x01880307 (30, −70, 0) | ACViewer `Locations.txt` |
| `glenden-wood-dungeon` | Glenden Wood Dungeon (749) | 0x01E3033D (161, −150, 0) | ACViewer `Locations.txt` |
| `training-academy` | Training Academy, landblock 0x8602 | 0x860201AD (12.3199, −28.482, 0.005) | the portal dat's CharGen 0x0E000002 `StarterAreas[0].Locations[0]`, where ACE's `PlayerFactory` places a new character (ACE @95ef426d) |
| `holtburg-shop` | Holtburg shop, by the counter | 0xA9B40163 (100, 36, 94) | chosen inside LandBlockInfo 0xA9B4 building 7 |
| `holtburg-smithy` | Holtburg smithy | 0xA9B40100 (88.84, 131.54, 66) | `CellDiscovery.FindBuildingEntry` for building 0 |

- **`training-academy` moved in M2 (step ST12).** M1 pointed it at 0x860301AD, from ACE's older `CharacterPositionExtensions.StartingPosition`: the same room of the Holtburg start area, but in landblock 0x8603. ACE spawns a new character at the area's first location, 0x860201AD in landblock 0x8602 (protocol.md §4.3), so the key, the offline walks and the headless host now use that cell. This document's M1 numbers for "the Training Academy" (2,163 cells, the tour, the load times) are of landblock 0x8603, which `--start=8603` still reaches.
- Facings come from the sources' quaternions (`WorldLocations.HeadingFromQuaternion`). Every entry is checked against the dats (`InteriorLocationsTests`): the cell exists, the feet and eyes are inside it under a ceiling, a walkable floor is right under the feet, dungeons are `Dungeon` landblocks and interiors are building cells. ACViewer's "Hotel Swank" fails the check (its point is not in its cell) and is left out.
- The dats name no buildings: the two Holtburg ones are named by what stands inside them.
- **Any dungeon landblock:** `--start=XXYY` naming a landblock that `CellDiscovery.Summarize` calls a dungeon starts inside it at `CellDiscovery.FindDungeonEntry`'s floor point (a heuristic; `DungeonEntries`). Towns and mixed landblocks keep the outdoor start.
- **API for the teleport panel:** `InteriorController.PlaceCamera(WorldLocation, teleport: true)` teleports the world, places the camera at eye height on the floor and tells the tracker the cell (read from the dat at once, so the focus is indoor and the dungeon's landblock is meshed whole before anything streams). The teleport panel's `UI/Navigator.cs` sends EnvCell destinations through `PlaceCamera` (M1 wave-3 navigation fix), and its catalogue lists every curated location (docs/navigation.md §1).
- **Typed EnvCell positions** work too: `--start=016301AC@140,-40,0`.

## 7. Measurements

RTX 5090, Vulkan Forward+, 1280×720, 30 workers, 2026-10-10, after the review fixes. Other agents' Godot runs shared the machine during some of these.

**Tour** (`--interior-tour=DIR --vsync=off --max-fps=0`; measured with the statics smoke layer, retired in M1 wave 3, whose nearest equivalent today is `--objects=statics,buildings`): teleports to every curated location, waits until streaming is idle, every expected cell is attached and the main-thread queue is empty, settles 30 frames and 0.6 s (so the HUDs show the finished state), saves `interiors-tour-KEY.png`, then measures 240 frames standing still and 240 frames turning a full circle. Draw calls are Godot's `TotalDrawCallsInFrame` (with the smoke layer's statics in Holtburg).

| Location | Cells meshed / as data | Still: avg / max frame | Turning: avg / p99 / max | Draw calls (still / turn max) | Cells drawn | View max |
|---|---:|---|---|---|---:|---:|
| Town Network | 498 / 498 | 0.49 / 12.77 ms¹ (2,050 fps) | 0.47 / 0.78 / 1.54 ms | 137 / 138 | 27 | 0.30 ms |
| Holtburg Redoubt | 178 / 178 | 0.45 / 14.17 ms¹ | 0.39 / 0.73 / 1.02 ms | 68 / 68 | 5 | 0.07 ms |
| Marketplace | 536 / 536 | 0.40 / 1.20 ms | 0.40 / 0.69 / 0.77 ms | 60 / 78 | 12 | 0.11 ms |
| Mines of Despair | 537 / 537 | 0.47 / 14.58 ms¹ | 0.40 / 0.72 / 1.03 ms | 119 / 119 | 13 | 0.09 ms |
| Glenden Wood Dungeon | 749 / 749 | 0.45 / 12.47 ms¹ | 0.39 / 0.79 / 1.04 ms | 73 / 76 | 17 | 0.18 ms |
| Training Academy | 2,163 / 2,163 | 0.42 / 2.29 ms | 0.42 / 0.72 / 0.86 ms | 78 / 155 | 10 | 0.12 ms |
| Holtburg shop (town around) | 430 / 695 | 0.90 / 2.31 ms | 0.96 / 1.74 / 2.36 ms | 711 / 757 | 39 | 0.30 ms |
| Holtburg smithy | 430 / 695 | 0.69 / 10.86 ms¹ | 0.95 / 1.48 / 1.99 ms | 301 / 787 | 24 | 0.19 ms |

¹ The frame right after the screenshot is saved to disk.

- **The DoD's ≥ 60 fps in a dense dungeon** holds with a wide margin: 2,000–2,500 fps with vsync off in every dungeon, including the Training Academy (2,163 cells) and the Town Network (26 cells and their objects in view). One mesh per cell cut the Town Network's draw calls from 221 to 137.
- **Mesh memory** (cells with their objects): 3.9 MB for the Town Network, 5.7 MB for the Training Academy, 4.9 MB for the 430 meshed cells around Holtburg (the shared object parts are now copied per cell; the 695 cells around Holtburg used 3.4 MB without them before).

**Flight Holtburg → Arwic at 150 m/s** (`--fly-path=holtburg:arwic:150 [--vsync=off --max-fps=0]`, with the retired statics smoke layer, today `--objects=statics,buildings`; 680 landblocks, 561 of the 593 cells on the way meshed): **no frame over 50 ms** in any run, none over 33 ms.

| Run | Max frame | p99 | Slowest attach step (any layer) | Slowest main-thread item | Max drain | Gen 0 / 1 / 2 GCs |
|---|---:|---:|---:|---:|---:|---|
| Review (before the fixes), 2 runs | 29.3–31.0 ms | – | 13.8–19.3 ms (interiors) | 14.9–24.7 ms | 18.1 ms | – |
| Interiors on, vsync off, 6 runs | 9.9–16.3 ms | 0.94–1.07 ms | 1.6–2.9 ms, once 7.8 ms² | 3.2–7.8 ms | 4.6–11.0 ms | 71–75 / 38–42 / 34–38 |
| Interiors on, vsync on | 11.8 ms | 4.8 ms | 2.0 ms | 7.5 ms | 4.7 ms | 49 / 43 / 40 |
| `--interiors=off`, vsync off, 2 runs | 6.6–7.6 ms | 0.70 ms | 1.3–1.4 ms | 3.0–6.0 ms | 3.6 ms | 39 / 38 / 38 |

² 7.3 ms of it was a GC pause; the step's own work was 0.2 ms.

- **The interiors' own main-thread work** stays small: attach steps of at most 2.9 ms of work in these flights (time-sliced at 0.75 ms; one surface is never split), views of at most 0.66–0.68 ms of work. Every `slow attach step` and `slow view` over 2–4 ms in these flights was a GC pause that fell in it (the log lines carry `gc_pause_ms`). Dungeon loads are the exception: see "Dungeon loads (wave-3 performance pass)" below.
- **Dungeon loads (wave-3 performance pass, corrected in its review).** The tour logs each load's main-thread cost (`[interiors] tour load ... max_drain_work_ms slowest_item_ms slowest_item_work_ms gc_pause_ms`), `--log-level=debug` names every main-thread item over 6 ms (`[main] slow main-thread item`), and each `slow attach step` line names the slowest single material acquire (with the texture it uploaded) and the slowest single `AddSurfaceFromArrays` of the step. Five tours per build and five `--start=00D1` loads (the densest dungeon, 4,213 cells, not a tour stop) are in game-core.md §7.3. With all layers, pooled texture decodes and 8 workers, the slowest item of each load (without a GC in it) is 0.8–2.7 ms in the Redoubt, Marketplace, Mines of Despair, Holtburg shop and smithy, 1.4–6.6 ms in the Town Network, 4.5–5.3 ms in Glenden Wood Dungeon, 9.6–10.3 ms in the Training Academy, and 9.6–9.8 ms in 0x00D1, which has four items over 6 ms per load. Apart from the large surfaces and GC pauses below, every item over 4 ms among them is **one engine call that stalls whatever its size**: `AddSurfaceFromArrays` of a 4–34-vertex surface (9.1–9.6 ms; 0x00D1, Training Academy), of a 12–13-vertex one (6.7–7.2 ms), of a 5–208-vertex one (Glenden Wood, 4.3–4.6 ms), or `ImageTexture.CreateFromImage` of a 42–85 KB texture (6.6–8.2 ms in 0x00D1; once 5.6 ms in the Town Network), while larger surfaces and textures in the same loads take a fraction of that. One engine call cannot be split from Lifestone's side, and the step already yields after every surface once its 0.75 ms slice is spent (§10). The 7,276- and 8,760-vertex surfaces of 0x00D1 take 4.1–5.5 ms of real packing work each. GC pauses still land in some items: 4.4–5.0 ms in Mines of Despair, up to 7.6 ms at the Holtburg shop, 15.7–17.7 ms in 2 of 5 0x00D1 loads. The audit's 12–21 ms drains were these stalls plus GC pauses; GC-driven slow items are rarer and shorter on average now, not gone. The flight table above predates integration; game-core.md §7.3 has the integrated numbers.
- **What changed against the review:** the start-up warm-up (§2: every cell variant through the real vertex format, the attach path, an indoor and an outdoor view), one mesh per cell (fewer, cheaper steps), no per-view `ToArray`/list allocations in `Apply` and the doorway assignment, pooled worker temporaries, and only retail's stab lists meshed outdoors.
- **What remains against `--interiors=off`:** about twice the gen-0 collections (the cells' vertex arrays and the views), so GC pauses land more often in some main-thread item; max frames 10–16 ms against 7–8 ms.
- **Training Academy teleport:** one 9.6–10.3 ms main-thread item during its load (§10, TODO(perf)); 0x00D1 has four of 6.5–9.8 ms per load.
- **Start-up:** the cell variants compile in about 0.3 s (0.4 s with a fresh shader cache) and pass the mesh path in 3–22 ms; the attach warm-up takes about 0.1 s.

**Instance-uniform errors** (the HIGH finding): `--start=8E51` (with the retired statics smoke layer) printed 2,995 "Too many instances using shader instance variables" errors before the fixes (3,154 cells meshed, 14.5 MB); now none (636 meshed, 6.7 MB, 3,154 as data). No run in this section printed one, except the engine checks' announced negative control.

## 8. Walk mode indoors

F toggles walk mode as before. Indoors, `CellWalker.FollowFloor` keeps the eye 1.7 m above the highest walkable floor polygon (`CellStructure.Floors`, normal Z ≥ ACE `FloorZ`) under it, in the viewer's cell or a neighbour behind one of its portals, that is at most 0.6 m above the feet; with no floor under it the height is kept. Ramps and stairs are followed between cells (`CellWalkerTests` climbs 3 m in Holtburg Redoubt and back). Outdoors the camera's terrain following takes over again. **Not retail physics:** no collision, no step-down limit, and the 0.6 m step is a placeholder for the player Setup's `step_up_height` in M2's physics transition.

## 9. Keys, arguments and diagnostics

| Key / argument | Effect |
|---|---|
| F7 / Shift+F7 | Next / previous curated location |
| F9, `--interior-show-all` | Draw every loaded cell (portal visibility off) |
| `--start=KEY`, `--start=XXYY` | Start in a curated location, or inside a dungeon landblock |
| `--interior-eye=CELL@x,y,z`, `--interior-look=H,P` | Put the eye exactly there (in that cell, or outdoors for a land cell) and face heading H, pitch P (screenshots) |
| `--interior-tour=DIR` | The measurement tour of §7, then quit |
| `--interior-debug-ambient=X` | Indoor ambient X instead of 0.2 (0 shows the burned lights alone) |
| `--interior-engine-checks` | Render quads with the cell variants off screen and check what the shaders rely on, then quit (0 = all hold): the cell variants declare no instance uniform; a mesh without CUSTOM1 reads 0 there (light 0, slot 0 = no state); CUSTOM1 arrives unchanged; the indoor ambient switch works; through the slot and the cell-state table, a "front" cell quad that is farther away is drawn over a nearer object quad (reversed-Z), and a doorway-clipped cell is drawn over the object inside its cone, discarded beyond the seam band and kept within it, 0.15 m inside and 0.1 m outside the cone edge (which pins the band's sign: Godot's `P[1][1]` is negative); and the instance-uniform budget: after more cell-variant instances than the global buffer has instance blocks (4,096 with the default 65,536 vec4), an instance-uniform probe still gets its block, while in the negative control, after that many instance-uniform instances, the next probe does not (Godot prints its "Too many instances" errors for those, as announced in the log). All 15 hold on Vulkan (2026-10-10). |
| `--interiors=off` | No interiors at all (A/B measurements) |

The `--interior*` arguments are registered in the option registry (`OptionCatalog.Interiors`; `--help` lists them), so core's `GameOptions` no longer warns about them. The bottom-left HUD line shows the viewer's cell (environment, struct, seen outside or not), the cells drawn, the meshes attached, the cells meshed of those expected, the cells read as data, lights, mesh memory, walks, the view's cost and whether the landscape is on. The log gets a `summary` line at exit (views, slowest view and its work without GC, cells, slots, truncated cones), a `slow view` line for the first 20 views slower than 2 ms (with the GC pause in them), and a `slow attach step` line for the first 30 steps over 4 ms (GC pause, material and surface time, surfaces, vertices, collections, and since the wave-3 review the slowest single material acquire with the texture it uploaded and the slowest single `AddSurfaceFromArrays` with its vertices).

## 10. Open points

- **Environment detail texture: done (wave 3, render-fidelity fix; render-contract.md §2.4).** Cells and building shells get retail's detail pass (`SurfaceDetail`, a variant field). It is folded into the opaque draw; `--detail=off` turns it off.
  - **Corrections to this note's earlier reading.** The tiling is the Region's TMTerrainDesc[2] `detail_tex_tiling`, 4 in the user's dats; acclient.c:45536's 2.0 is only the initial value, which `LScape::SetDetailTexturing` overwrites. The UV factor of 3.0 from `CEnvCell::UnPack` is replaced by that tiling at the first draw (`D3DPolyRender::SetDetailTiling`): the detail UV is `tiling × UV`, not `3 × 2 × UV`.
  - **No distance fade on this path.** `ACRender::get_alpha_for_z`'s 10–50 m fade belongs to the software polygon paths, not to the mesh path that draws built cells.
  - **The blend.** `DESTCOLOR/INVSRCALPHA` gives the factor `detail × light + 1 − detail alpha`.
  - **Objects inside cells** get no detail (`DrawEnvCell` clears the detail surface after the cell mesh).
- **Dynamic lights** are M3 4.1: the viewer light, projectiles' lights and creatures' own lights (§4.6), with the TODOs listed there.
- **Cell statics' LOD** (objects track, M1 wave 3): retail LOD and camera-facing levels through the objects' LOD-state table (docs/objects.md §2.6). A camera-facing level or an animated part keeps the per-vertex light of its rest orientation (docs/objects.md §4).
- **Cell lighting is per cell, not per viewer** (§4.3).
- **Depth emulation** limits (§3.4).
- **Whole-landblock meshing for dungeons.** Retail re-grabs the viewer cell's stab list on every cell change; Lifestone meshes the viewer's whole landblock once (a superset). The densest landblock (0x00D1, 4,213 cells) fits the 16,384-slot table.
- **One 10–14 ms step when the Training Academy loads (TODO(perf); since the wave-3 review attributed to single engine calls, cause inside them unconfirmed).** Teleporting into it attaches 2,163 cells in about 0.6 s; in every run one step stalls 10–14 ms inside a single `AddSurfaceFromArrays` of a tiny surface (8–28 vertices), with no GC pause, collection or material upload in it, always after about 93,000–96,000 cell vertices (about 20 MB of GPU buffer memory). Neither disposing the per-surface `Godot.Collections.Array` and `Variant`s nor releasing the cells' arrays early changed it, so it looks like the engine or the driver taking a new block of GPU memory; to be confirmed with a GPU-allocator trace in the wave-3 performance pass. A GC pause of 11–17 ms (gen 0–2) lands near it in the same load. Neither occurs in the DoD flight (§7).
  - *Wave-3 performance pass and its review:* still there (9.4–9.6 ms for an 8–10-vertex surface, every tour). It is not specific to the Training Academy: Glenden Wood Dungeon has one (4.3–4.6 ms for a 5–208-vertex surface), and 0x00D1 has four per load, at the same places in every run: `AddSurfaceFromArrays` of a 4–34-vertex surface (9.1–9.4 ms) and of a 12–13-vertex one (6.7–7.2 ms), the 5,548-vertex surface (8.7–9.2 ms, about twice what the larger ones take), and `ImageTexture.CreateFromImage` of a 42–85 KB texture (6.6–8.2 ms). The duration does not follow the size, and it repeats at the same place, which fits a memory pool growing inside Godot's RenderingDevice or the driver; Godot's `RenderingInfo` memory figures did not move within the call (a probe read them around every surface), so that stays unconfirmed. No GPU-allocator trace was possible without new tools. TODO(perf): a GPU-side trace; if it is pool growth, reserving the memory up front at start-up could move the stalls before the start view.
- **Godot's instance-uniform budget** is shared by every track (render-contract.md §5, the Interiors row): the interiors use none.
- **Retail physics** for walking and for entering buildings (M2).
- Core's top-left `PositionHud` shows the outdoor cell under the camera even in dungeons (dungeon cells lie outside their landblock's square); the interiors HUD line shows the real cell.

## 11. Files

| File | Role |
|---|---|
| `Lifestone.Client/Interiors/LandblockInteriors.cs` | A landblock's cells, doorways, landblock stab list and bounds; `InteriorRegistry` |
| `Lifestone.Client/Interiors/InteriorCellSets.cs` | Which cells are meshed (§2) |
| `Lifestone.Client/Interiors/CellDrawState.cs`, `CellSlotAllocator.cs`, `DoorwayRows.cs` | The per-cell state texel, its slots, the doorway-plane rows (§3.4) |
| `Lifestone.Client/Interiors/CellTracker.cs` | The viewer's cell (§3.2) |
| `Lifestone.Client/Interiors/InteriorVisibility.cs` | The walks, `InteriorView`, `DoorwayView` (§3.3) |
| `Lifestone.Client/Interiors/ViewFrustum.cs` | Camera side planes |
| `Lifestone.Client/Interiors/CellQueries.cs` | Enclosure, floors, segment and ray tests |
| `Lifestone.Client/Interiors/CellStaticLighting.cs`, `CellObjectLighting.cs` | §4.3, §4.4 |
| `Lifestone.Client/Interiors/CellWalker.cs` | §8 |
| `Lifestone.Client/Interiors/InteriorLocations.cs`, `DungeonEntries.cs` | §6 |
| `Lifestone.Client/Rendering/SurfaceLighting.cs` | The variant's lighting rule |
| `Lifestone.Game/scripts/Render/SurfaceLightingCode.cs` | Shader code for the cell rules, the cell-state table, front depth, doorway clipping and the seam band |
| `Lifestone.Game/scripts/World/Interiors/InteriorsLayer.cs` | §2 |
| `Lifestone.Game/scripts/World/Interiors/InteriorController.cs`, `InteriorHud.cs` | §1, §3.4, §5 |
| `Lifestone.Game/scripts/World/Interiors/InteriorMeshes.cs` | `CellMeshMerger` (a cell and its objects into one mesh, worker) and the ArrayMesh surfaces (main thread) |
| `Lifestone.Game/scripts/World/Interiors/InteriorsSetup.cs` | One-call installation from `Main`, `InteriorFocus` |
| `Lifestone.Game/scripts/World/Interiors/InteriorArguments.cs`, `InteriorTourRunner.cs`, `InteriorEngineChecks.cs` | §9, §7 |

**Edits outside the track** (each small and marked): `Main.cs` (one call), `project.godot` (four shader globals in the interiors block), `SurfaceShaderVariant.cs` (the `Lighting` field), `SurfaceShaders.cs` (two calls into `SurfaceLightingCode`), `MaterialFactory.cs` (`SurfaceRequest.Lighting`), `RenderResources.cs` (applies it), `WorldLocation.cs` (curated names parse as `--start`), and the notes in `render-contract.md` (including the instance-uniform budget) and `game-core.md` that point here.

**Tests** (`tests/Lifestone.Client.Tests/Interiors/`, real dats): the light formulas against hand-computed values and their selection on the Town Network; the frustum; enclosure and floors; the tracker through Holtburg's shop door, through a wall, through a roof opening and between dungeon cells, and a 1 m outdoor grid over Holtburg; the walks in all six dungeons (stab-list subsets, no landscape, indoor ambient), in the smithy (landscape only with the window in view) and outdoors at the shop door (doorway cones, stab lists); the curated locations; dungeon entries; ramps; which cells are meshed (Holtburg's stab list, every cell seen from 512 outdoor views around Holtburg is in it, dungeons meshed whole); the draw-state encoding, slots and nearest-first doorway rows; every one of the 16,937 building doorways fits the doorway table (exhaustive); a teleport out of a dungeon that bypasses `PlaceCamera`.
