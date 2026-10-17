# Avatars: how server objects look and move (M2)

**Track:** avatars ([m2-plan.md](milestones/m2-plan.md) §3.3, steps AV0–AV14). **Owns:** `src/Lifestone.Data/Appearance/`,
`src/Lifestone.Data/Motion/` (after the lead's wave-0 types), `src/Lifestone.Client/Avatars/` (except
`SessionAvatarAdapter.cs` and `WorldContentCensus.cs`, which are G5's), `src/Lifestone.Data/Animation/{MotionPlayer,AnimSequence}.cs`, additive
members of `GfxObjMesh`, `ModelAssembly` and `TextureLoader`, their tests, this document and
[third-party/avatars.md](third-party/avatars.md).
**Builds on:** the models track (`ModelAssembly`, `LodChain`), the textures track (`TextureLoader`, `PaletteOverride`,
`SubPalette`, `PaletteSets`), the animation code (`MotionPlayer`, `MotionTableManager`, `AnimSequence`), the net track's
`MotionCommandTable`, the lead's `CellFrame`, `LandDefs` and `Sequence16`, and DatReaderWriter's CharGen and ClothingTable
records.
**Authority:** retail behaviour is re-expressed from the Mag-nus/acclient-AI-RE decompile of 11.4186 (PDB names) and
cited by function name with its 11.4186 and 11.6096 addresses (§8; the 11.6096 ones come from the pinned
`symbol_transplant_11.6096.tsv`, owner decision D4). Nothing from it is copied. ACE is used for test oracles, and for
the names and values of two small motion files ([third-party/avatars.md](third-party/avatars.md)).

**Status.** Stage av1 (AV0–AV3): object descriptions, the appearance of a part array under ObjDesc changes, clothing,
and the CharGen character. Stage av2 (AV4–AV6, checkpoint C-avatars): the motion states and their wire form, the motion
interpreter (`CMotionInterp`) with the 11.6096 jump rules, the movement system, and the position, interpolation and
constraint managers (§5, §6). Stage av3 (AV7–AV12, AV14): the server object's body and the avatar set that applies the
session's events (§9), children, parenting and Hidden (§10), the player's avatar and its body (§11), the draw output and
the stress crowd (§12), CharGen's 3D view (§13), and the MoveTo, sticky and target managers (§14). Nothing is on screen
yet; the first visible results are wave 2's (the spell-viewer caster in clothing, the CharGen 3D view, the
`--avatar-stress` crowd (dropped by the owner after wave-2 item 4), Holtburg's NPCs after login, the player walking). AV13, the session adapter, is G5's: it is built, and the game draws the server's objects (§17).

## 1. Layout and seams

| Type | Where | What |
|---|---|---|
| `ObjDesc`, `ObjDescBuilder`, `AnimPartChange`, `TextureMapChange`, `WireSubPalette` | `Data/Appearance/ObjDesc*.cs` | Object descriptions with retail's building rules (§2) |
| `PartArrayAppearance`, `PartAppearance`, `AppearanceResult` | `Data/Appearance/PartArrayAppearance.cs`, `PartAppearance.cs`, `IAppearanceSource.cs` | A part array under ObjDesc changes (§3) |
| `IAppearanceSource`, `DatAppearanceSource`; `IAppearanceDats`, `DatAppearanceDats` | `Data/Appearance/IAppearanceSource.cs`, `DatAppearanceSource.cs` | What the appearance code reads from the dats; tests supply fakes |
| `ClothingObjDesc`, `ShadePackage` | `Data/Appearance/ClothingObjDesc.cs` | What a ClothingTable adds to an ObjDesc (§4.1) |
| `CharGenAppearance`, `CharGenLook`, `GearChoice`, `CharGenComposition` | `Data/Appearance/CharGen*.cs` | The character-creation look (§4.2) |
| `GfxObjMesh.SurfaceIds` | `Data/Models/GfxObjMesh.cs` (additive) | A GfxObj's surface list in index order |
| `TextureLoader.GetOriginalPaletteId` | `Data/Textures/TextureLoader.cs` (additive) | A Surface's original palette (§3.2) |
| `RawMotionState`, `InterpretedMotionState` (behaviour), `MovementParameters`, `WeenieError` | `Data/Motion/*.Motion.cs`, `MovementParameters.cs`, `WeenieError.cs` | Motion states and how a motion applies to them (§5.1) |
| `WireMotion`, `WireInterpretedMotion`, `WireMotionItem`, `WireMotionFailure` | `Data/Motion/WireMotion.cs` | Wire low words to 11.6096 commands (§5.1) |
| `MotionInterpreter`, `IMotionInterpHost`, `IMotionWeenie`, `PendingMotion` | `Data/Motion/MotionInterpreter.cs`, `IMotionInterpHost.cs` | Retail `CMotionInterp` (§5.2, §5.3) |
| `MovementSystem`, `EncumbranceSystem` | `Data/Motion/MovementSystem.cs` | Run rate, jump height, stamina cost (§5.2) |
| `MotionPlayer` part-array operations | `Data/Animation/MotionPlayer.cs` | The `CPartArray` calls the interpreter makes (§5.2) |
| `PositionManager`, `InterpolationManager`, `ConstraintManager`, `PositionContext`, `PositionFrames` | `Data/Motion/Position*.cs`, `InterpolationManager.cs`, `ConstraintManager.cs` | Smooth server positions and the player's constraint (§6) |
| `IPlayerBody`, `IPlayerMovementBody` | `Client/Avatars/IPlayerBody.cs` | The player's body, implemented by the physics track (§5.3, §11) |
| `MovementManager`, `WireMovement`, `WireMoveTo`, `WireTurnTo` | `Data/Motion/MovementManager.cs`, `WireMovement.cs` | An object's interpreter and MoveTo, and the server's MovementData (§9.3, §14) |
| `MoveToManager`, `IMoveToHost`, `StickyManager`, `TargetManager`, `ITargetable`, `ITargetWorld` | `Data/Motion/MoveToManager.cs`, `StickyManager.cs`, `TargetManager.cs` | Server MoveTo and TurnTo, sticky follows, target reports (§14) |
| `AvatarBody`, `AvatarStep`, `AvatarStateBits` | `Client/Avatars/AvatarBody.cs` | One server object without collision (§9.1) |
| `AvatarSet`, `Avatar`, `AvatarApply`, `AvatarNotice`, `AvatarStats` | `Client/Avatars/AvatarSet.cs` | Every server object, from the session's events (§9.2) |
| `AvatarCreate`, `AvatarWire`, `AvatarSources` | `Client/Avatars/AvatarCreate.cs`, `AvatarWire.cs`, `AvatarSources.cs` | What an avatar is built from, the mapping from the net types, and the dats (§9.2) |
| `PlayerAvatar`, `KinematicPlayerBody` | `Client/Avatars/PlayerAvatar.cs`, `KinematicPlayerBody.cs` | The player's avatar and a placeholder body (§11) |
| `AvatarFrame`, `AvatarDraw`, `AvatarViewer` | `Client/Avatars/AvatarFrame.cs` | The draw output (§12) |
| `AvatarStress`, `AvatarStressDriver` | `Client/Avatars/AvatarStress.cs` | A synthetic crowd (§12.3) |
| `SessionAvatarAdapter` (AV13), `WorldContentCensus` | `Client/Avatars/SessionAvatarAdapter.cs`, `WorldContentCensus.cs` | The session's object events into the avatar set, in order and inside a budget; the census of the world content the server placed (§17) |
| `CreaturePreview`, `PreviewAnimations` | `Client/Avatars/CreaturePreview.cs` | CharGen's 3D view (§13) |

The appearance and motion code is in Lifestone.Data: plain data over dat records, no Godot, no I/O beyond `DatSet`, so
the wave-2 server objects layer (G5), CharGen's view (G3), the spell viewer and the physics track can all use it. The
objects, the player's avatar, the draw output and the preview are in `Lifestone.Client.Avatars`: they read the state
track's events (`Lifestone.Client.Session.Contracts`) and the effects track's draw types, and are engine-agnostic too.

**Consumers.** The G5 adapter maps the net track's ModelData records to `ObjDesc.FromWire` (or passes the bytes to
`ObjDesc.TryUnpack`), creates a `PartArrayAppearance` per object and applies `DoObjDescChangesFromDefault` on create and
on every ObjDescEvent. The renderer draws each `PartAppearance` (§3.4). CharGen's pages build a `CharGenLook` from their
state and call `CharGenAppearance.Compose`; the character-creation request itself is state's (`CharacterCreateIntent`).
The physics track's player object hosts a `MotionInterpreter` (`IMotionInterpHost`) and implements `IPlayerBody`; the
avatar body (AV7) hosts one per server object; the avatar set (AV8) maps UpdateMotion through `WireMotion`; state maps
the player's `RawMotionState` to the net track's `RawMotionStateWire` for MoveToState.

## 2. Object descriptions (AV1)

An ObjDesc is a base palette id plus three lists: sub-palette swaps, texture swaps (part, old SurfaceTexture, new
SurfaceTexture) and part swaps (part, GfxObj). It arrives in every CreateObject and ObjDescEvent as ModelData
([protocol.md](protocol.md) §5.3), and the CharGen record holds many of them.

**Retail never stores a list as received.** `ObjDesc::UnPack` (11.4186 0x005ABCF0) and `ObjDesc::operator+=` (0x005AC0A0)
pass every entry to `ObjDesc::AddSubpalette`, `AddTextureMapChange` or `AddAnimPartChange`. `ObjDescBuilder` re-expresses
them and `ObjDesc` holds the result:

- **Sub-palettes.** An incoming swap is dropped when an existing whole-palette swap supercedes it; otherwise every existing
  swap it replaces (same range, or it is whole-palette) is removed and it is appended (`Subpalette::replaces`,
  `Subpalette::supercedes`; the textures track's `SubPalette` already had both).
- **Texture swaps.** The first existing swap with the same part and old texture is removed, then the new one is appended
  (`TextureMapChange::replaces`: the new texture plays no part).
- **Part swaps.** The first existing swap equal to the new one is removed, then it is appended. The equality is
  `ContentProfile::operator==`, which the linker folded with `AnimPartChange`'s and whose body the 11.4186 split does not
  contain. Lifestone compares part and GfxObj (an `operator==`, not a `replaces`; ACE does the same). The other reading,
  the part alone, changes only the list, never what is drawn, because the last change for a part wins either way;
  `ObjDescTests.Both_part_duplicate_rules_draw_the_same_parts` shows it for every CharGen composition. **TODO(avatars):**
  pin the folded comparison from the 11.6096 bytes.
- **At most 255 entries per list.** The duplicate is removed first, then a 256th entry is dropped.
- **`operator+=` keeps the left palette id.** It never copies the right-hand one; `operator=` does.

**Units.** In memory retail keeps sub-palettes in colours; on the wire and in the dat the offset and length are bytes in
units of 8 colours, with a length byte of 0 meaning 2,048 colours (`Subpalette::UnPack`). `WireSubPalette` is the wire
form, `SubPalette` the colour form.

**Unpacking.** `ObjDesc.TryUnpack` reads ModelData bytes as `ObjDesc::UnPack` does: the header byte must be 0x11, the
base palette is read only when there are sub-palettes, ids are known-type packed (`Unpack_AsDataIDOfKnownType`, 11.4186
0x006844F0, adds the type to every value, so a null id on the wire reads back as the type itself), and the reader moves
to the next 4-byte boundary. `ObjDesc.FromDat` converts DatReaderWriter's CharGen ObjDescs: all 2,404 have header 0x11
and none repeats an entry.

## 3. A part array under ObjDesc changes (AV2)

### 3.1 The state

`PartArrayAppearance.Create(setup, source)` holds, per Setup part, retail's `CPhysicsPart` appearance state: the LOD
chain, the level-0 GfxObj's surface list, the base palette (`original_palette_id`), texture swaps and the modified
palette it holds (`shiftPal`). A part that does not load makes `IsLoadable` false: retail refuses the whole object
(`CPartArray::InitParts`).

`DoObjDescChanges` (`CPartArray::DoObjDescChanges`, 11.4186 0x00519210) applies part swaps, then texture swaps, then
the palette (only with sub-palettes), each change on its own: a failed change fails the call (`AppearanceResult`
lists why) and the rest still apply.

- **Part swap** (`CPartArray::SetPart`, `CPhysicsPart::SetPart`, `CPhysicsPart::SetGfxObjArray`). A part index beyond
  the Setup, the null id or a GfxObj that does not load fails and keeps the old part. Otherwise the part takes the new
  chain and its level-0 surfaces, **loses its texture swaps**, recomputes its base palette and **keeps its modified
  palette** (retail re-applies `shiftPal` to the new surfaces).
- **Texture swap** (`CPhysicsPart::SetTextureMap`). The null new id fails. Every surface whose **original** texture is
  the old id draws the new one; a later swap matches the original again, never an earlier swap's result. A matched
  surface without a texture (only possible with old id 0) fails and stops the change. No match is a success: 3,639 of the
  ClothingTables' texture changes match nothing on their Setups. A swap back to the original removes the entry (the
  Undead face strips are full of such identity swaps), so the part's meshes stay shared.
- **Palette** (`CPartArray::SetPalette`). The null palette id fails. The palettes made so far are searched in order; one
  made from the same palette id is **modified in place** (`Palette::Modify`), so a second ObjDesc's swaps land on top of
  the first one's; otherwise the first free slot gets a new palette (`Palette::makeModifiedPalette`: a copy of the base
  with each swap applied; a lone whole-palette swap is the dat palette itself, which retail never modifies). The result
  goes to every part whose base palette is the ObjDesc palette id. Parts share the palette object, so an in-place change
  shows on every part that holds it, including one whose part swap changed its base palette since.

`DoObjDescChangesFromDefault` (`CPartArray::DoObjDescChangesFromDefault`, 0x00519480) restores every part's palette and
drops the modified palettes, then applies the ObjDesc. It does **not** undo part or texture swaps: they last until the
part is set again. For players and dressed creatures ACE lists every part the clothing leaves uncovered too
(`Creature.CalculateObjDesc`), so their parts are fully described each time; an object whose ObjDesc names fewer parts
keeps the earlier swaps of the others, as in retail.

### 3.2 Base palettes

A part's base palette is the original palette of the first level-0 surface that has one (`CPhysicsPart::DetermineBasePal`,
0x0050DC80). A Surface's original palette is what `CSurface::InitEnd` (0x00536400) leaves after loading: the default
palette of its RenderSurface when that is INDEX16 or P8, else the id the Surface record stores (0 on every end-of-retail
Surface). `TextureLoader.GetOriginalPaletteId(surface, detail)` returns it for the RenderSurface the given texture level
resolves to.

**The texture level matters for two Surfaces.** 0x080009B1 and 0x0800129A use a SurfaceTexture whose high-res level
(0x0600628F) is not indexed and whose portal level (0x060045B4) is, with palette 0x040008B4. Only GfxObjs 0x01000C0F and
0x01002C11 use them, and in both the base palette is 0 at the high level and 0x040008B4 at the low one. Every other of
the 2,981 paletted Surfaces has the same palette at both levels (`AppearanceSweepTests`). `DatAppearanceSource` takes the
level the renderer draws, which keeps the result self-consistent: at the high level those parts have no indexed surface
for a palette to change.

### 3.3 What the sweeps show

- Every CharGen sex Setup applies its base ObjDesc, and ACE's ObjDesc for a naked human draws the Setup's parts with the
  hair style's head.
- Every ClothingTable on every Setup it lists (15,803 pairs) applies. Ten pairs fail only because the table names parts
  beyond the Setup's (28 changes); 101 Setups have no paletted surface, so no clothing palette reaches them.

### 3.4 Drawing a part (the hand-off to wave 2)

For each `PartAppearance`:

1. Pick the LOD level from `Chain` as today (`LodChain.Select`); draw its GfxObj mesh.
2. Every level draws with the level-0 surface array (models track, `LodChain` remarks), so look each sub-mesh's surface
   up by `SurfaceIndexRange.SurfaceIndex` in `GfxObjMesh.SurfaceIds` of `Chain.NearestGfxObjId`; on end-of-retail data
   that is the sub-mesh's own `SurfaceId`.
3. Decode each surface with `TextureLoader.DecodeSurface(surfaceId, mode, palette: part.Palette, surfaceTextureOverride:
   part.TextureOverrideFor(surfaceId))`. Only INDEX16/P8 textures use the palette.
4. Key meshes and materials by `gfxobj:0x..:Object` plus `VariantKey`. It is empty for a part that draws its GfxObj's own
   surfaces, so unchanged parts share M1's meshes; otherwise it names the swaps and the palette's cache key.

`PartArrayAppearance` is not thread-safe. Its `Parts` list is replaced, not mutated, by each call, and an unchanged part
keeps its `PartAppearance` object, so a consumer can compare references. `Version` increments on every call.

## 4. Clothing and the CharGen character (AV3)

### 4.1 ClothingTable::BuildObjDesc

`ClothingObjDesc.Build(table, setup, paletteTemplate, shades, dats, into)` re-expresses `ClothingTable::BuildObjDesc`
(11.4186 0x005A7900):

1. The Setup's entry in the table's base effects.
2. `ClothingBase::ApplyPartAndTextureChanges`: for each object effect a part swap, then a texture swap per texture effect.
   A null texture id is added anyway but fails the call before the palettes.
3. A palette template of 0 stops here. A template the table lacks fails (ACE would use the table's first one).
4. For sub-palette effect *i*: its PaletteSet (missing fails), the palette for shade `ShadePackage.GetVal(i)`
   (`PalSet::GetPaletteID`; the null id for a shade outside [0, 1]), and one sub-palette per range, already in colours.

**The heritage fallback.** When the Setup has no entry, retail maps the Umbraen, Penumbraen and Undead body Setups and
the two Anakshay Setups to per-heritage default clothing Setups and looks that up instead; any other Setup, or a default
the table lacks too, gets nothing, successfully. The lead read the groups and defaults from the 11.6096 data (owner
decision D4, [research/retail-addresses-m2.md](research/retail-addresses-m2.md) §1.4), and
`ClothingObjDesc.GetHeritageFallbackSetupId` holds them (`HeritageFallbacksKnown` is true):

| Group | Setups | Default |
|---|---|---|
| Umbraen male / female | 0x0200196F, 0x02001972, 0x02001A5F, 0x02001A6F / 0x02001970, 0x02001A5E, 0x02001A6E | 0x0200196F / 0x02001970 |
| Penumbraen male / female | 0x0200196E, 0x02001971, 0x02001A5D, 0x02001A70 / 0x0200196D, 0x02001A5C, 0x02001A71 | 0x0200196E / 0x0200196D |
| Undead male / female | 0x02001A0F, 0x02001A9C, 0x02001A9E, 0x02001A9D, 0x02001A96 / 0x02001A0D, 0x02001AA0, 0x02001A9F, 0x02001AA1, 0x02001AA2 | 0x02001A0E / 0x02001A0C |
| Anakshay male / female | 0x02001AA3 / 0x02001AA4 | `HUMAN_MALE_CLOTHING_DEFAULT` 0x02000001 / `HUMAN_FEMALE_CLOTHING_DEFAULT` 0x0200004E |

CharGen misses for eight Setups: the Undead skeleton and zombie bodies 0x02001A9C, 0x02001A9D, 0x02001AA0 and
0x02001AA1, which the fallback dresses as the Undead bodies, and the Gear Knight (0x02001907, 0x020019ED) and Olthoi
(0x02001A20, 0x02001A21) bodies, which are in no group, so retail draws them without that garment. ACE's
`Creature.CalculateObjDesc` maps the same groups through its `SetupConst`, except Umbraen female void, which it spells
0x2001A6EE.

### 4.2 gmCG3DView::Update

`CharGenAppearance.Compose(charGen, look, dats)` re-expresses `gmCG3DView::Update` (11.4186 0x004EE9D0):

1. The ObjDesc palette id is the sex's base palette.
2. Add the sex's base ObjDesc, then the hair style's.
3. Headgear, trousers, shirt, footwear, in that order, each through `ClothingObjDesc.Build` with its palette template and
   a `ShadePackage` of its one shade. A garment whose ClothingTable is missing is skipped.
4. The eye strip, or its bald variant when the hair style is bald; then the nose strip; then the mouth strip.
5. Sub-palettes, in colours: skin (the skin PaletteSet at the skin shade) over 0–191, hair (the hair-colour PaletteSet at
   the hair shade) over 192–255, eyes (the eye-colour palette) over 256–319.

The Setup is the hair style's alternate Setup when it has one, else the sex's (`CharGenState::GetSetupID`), and
`HUMAN_SETUP_ID` (0x02000001, `CharGenAppearance.HumanSetupId`, read under D4) when both are the null id
(`ResolveViewSetupId`; no sex of the end-of-retail data has a null Setup); the background is the heritage's environment
Setup. The view applies the result with `DoObjDescChangesFromDefault`.
`CharGenLook` carries palette templates, not colour-button indices: retail's buttons index the templates
`CharGenState::StoreColorInformation` collects from the ClothingTable's template hash, which is the CharGen screen
state's to reproduce (G3).

### 4.3 The preview against the world look ACE sends

The player must look in the world as in the preview. `CharGenAppearanceTests` composes the first choice of every list
for all 26 heritage and sex pairs, builds ACE's ObjDesc for the same character with the test oracle (§10) and compares
part by part (GfxObj, texture swaps, palette colours). They match everywhere except:

- **Gear Knights**: palettes of all 16 parts. ACE gives Gear Knights no starter clothing ("pcap verified" in its
  PlayerFactory); the preview dyes their shirt and trousers.

The Undead's first hair styles use the skeleton bodies, which neither ClothingTable lists; retail's heritage fallback
(§4.1) and ACE's `SetupConst` mapping dress them alike.

The comparison assumes ACE applies trousers before shirts (the starter weenies' coverage priorities are not available
offline); with the reverse order the human torso part differs too.

## 5. Motion (AV4, AV5)

### 5.1 States, parameters and the wire

The wave-0 data types (`RawMotionState`, `InterpretedMotionState`, `MotionActionNode`, `HoldKey`) gained their
behaviour in partial files, re-expressed from CMInterpPrim.c:

- **Interpreted** `ApplyMotion` records TurnRight as the turn and SideStepRight as the sidestep; any other substate
  becomes the forward command (TurnLeft and SideStepLeft included, a retail quirk only a non-creature reaches); a style
  resets the forward command to Ready and keeps its speed; an action is queued with the parameters' speed, action stamp
  and autonomous bit. `RemoveMotion` resets only what is current. `CopyMovementFrom` copies everything but the actions.
- **Raw** `ApplyMotion` keeps both hands of sidesteps and turns, stores the hold key (Invalid when the parameters set
  the hold key, else theirs), never stores RunForward (running is a walk with the run key), and changes the style only
  when it differs.

`MovementParameters` is retail's parameter block as a readonly record struct. `new MovementParameters()` gives retail's
constructor: bitfield 0x1EE0F, distance to object 0.6, fail distance `float.MaxValue`, speed 1, walk/run threshold 15,
hold key Invalid. ACE's constructor differs (it sets CanCharge and a threshold of 1). `default(MovementParameters)` is all
zeros and never what retail means.

`WireMotion.TryToInterpreted(in WireInterpretedMotion, ReadOnlySpan<WireMotionItem>, InterpretedMotionState, out
WireMotionFailure)` is `InterpretedMotionState::UnPack`: absent fields take the defaults (NonCombat, Ready, no sidestep
or turn, speeds 1), low words go through the net track's `MotionCommandTable` (the 11.6096 table; the 2013 one is off
by three from ordinal 0x10F), action stamps keep 15 bits. Retail indexes its table unchecked; here an unknown low word
fails, names the field and the word, and leaves the state alone. `ToWire` writes only the fields that differ from their
defaults, as `Pack` does. `WireInterpretedMotion` and `WireMotionItem` mirror the net track's `ServerMotionState` and
`MotionItem` field for field (Lifestone.Data cannot reference Lifestone.Net); `WireMotionItem` is unmanaged, so a mapper
can `stackalloc` the actions.

**The stance is not in the interpreted state.** `MovementManager::unpack_movement` (0x00524440 / 0x00525040) maps the
MovementData header's stance and calls `DoMotion(stance)` only when it differs from `InqStyle()`, then
`move_to_interpreted_state`, then sticks to the sticky object, then sets the standing-long-jump flag from bit 0x200 of the
header. That sequence is the avatar set's (AV8).

### 5.2 The interpreter

`MotionInterpreter` is `CMotionInterp` (CMotionInterp.c). It turns what an object is told to do into motion-table calls
on its `MotionPlayer` and keeps the raw and interpreted states.

- **Lifecycle.** The constructor is `CMotionInterp::Create`; call `EnterDefaultState()` when the host is ready (as
  `MovementManager` does right after creating one). It resets both states, re-initialises the motion table, queues Ready
  and leaves the ground (which, for a creature with gravity, sets the leave-ground velocity, drops the links and applies
  the current movement). The interpreter subscribes to `MotionTableManager.MotionDone` itself.
- **Two paths.** `ApplyCurrentMovement` uses the raw state for the player (weenie slot 5, `IsThePlayer`, or no weenie)
  whose movement is its own (`MovementIsAutonomous`), and the interpreted state otherwise. The player's input goes
  through `DoMotion`/`StopMotion`/`SetHoldRun` (raw); every other object's motion through `MoveToInterpretedState`.
- **`adjust_motion`** (creatures only): TurnLeft → TurnRight × −1, WalkBackwards → WalkForward × −0.65, SideStepLeft →
  SideStepRight × −1, every sidestep × 1.248; then with the run key `apply_run_to_command`: WalkForward at a positive
  speed → RunForward × run rate (backwards stays a walk, just faster), turns × 1.5, sidesteps × run rate capped at ±3.
- **`apply_interpreted_movement`**: style; then, when contact allows the forward command, the forward command and the
  sidestep (or a stop of SideStepRight), or in a standing long jump Ready and a stop of the sidestep; without contact
  Falling; then the turn, or a stop of TurnRight straight on the motion table. Running records the forward speed as
  `MyRunRate`. One parameter block carries its speed from call to call, as retail's does.
- **Contact** (`contact_allows_move`): Falling, Dead and turns always; anything for a non-creature or without gravity;
  otherwise walkable contact. Without it a non-action is only recorded and an action fails (0x24).
- **Pending motions** mirror the motion table's queue one for one. Each carries the jump error it imposes (0x48 when
  the parameters forbid a jump during the links, else `motion_allows_jump` of the motion or, for a non-action, of the
  current forward command). `MotionDone` pops the oldest; an action also leaves both states and ends a sticky follow.
- **Server actions** (`move_to_interpreted_state`): an action plays when its stamp is newer under the 15-bit rule
  (`Sequence16.IsNewerAction`) and the object is not the player, or the action is not autonomous; only then is the stamp
  stored. The player already played its own autonomous actions. The stored action's stamp is the parameter block's,
  which is never set (0), as in retail.
- **Jumps** (11.6096). `motion_allows_jump` blocks Fallen, 0x40000016-18, 0x4000001E-39, Crouch to Sleeping,
  MagicPowerUp01-10, MagicPowerUp01-10Purple (0x1000012B-0x10000134), Sanctuary and AI_TelegraphCast (doc 13 §5; 11.4186
  blocks 0x10000128-0x10000131 and has neither of the last two; Falling is in neither set). `jump_charge_is_allowed` adds
  CanJump (0x49) and Fallen/Crouch/Sitting/Sleeping/Sanctuary (0x48). `jump_is_allowed`: in the air 0x24; fully
  constrained 0x47; the oldest pending motion's error; the charge; the forward command; the stamina (0x47).
  `charge_jump` starts a standing long jump when standing still. `jump` keeps the extent and clears OnWalkable, whose
  host calls `LeaveGround`.
- **Speeds.** `get_state_velocity`: x = 1.25 × sidestep speed, y = 3.12 × walk speed or 4 × run speed, capped at 4 ×
  the run rate (the weenie's, else `MyRunRate`; 1 without a weenie). `get_max_speed` = 4 × run rate;
  `get_adjusted_max_speed` uses forward speed / `CurrentSpeedFactor` while running. `get_jump_v_z`: 0 below an extent of
  0.0002, else the weenie's jump velocity for an extent capped at 1 (10 without a weenie). These are retail's constants,
  not the animations': the human walk cycle's root motion moves about 2.6 m/s (its pos frames), the interpreter says 3.12.
- **`MovementSystem`**: `GetRunRate` = (LoadMod × (skill/(skill+200) × 11) + 4) / scale / 4, `GetJumpHeight` (power
  clamped, at least 0.35), `JumpStaminaCost`; `EncumbranceSystem` gives the load and its modifier. Doc 13 found them
  byte-identical in 11.6096. The player's weenie (state's `QualitiesWeenie`) answers `InqRunRate` with them; until the
  qualities arrive it cannot tell and the interpreter runs at `MyRunRate`.

The motion table side lives in `MotionPlayer`: `DoInterpretedMotion`, `StopInterpretedMotion`, `StopCompletelyInternal`,
`InitializeMotionTables`, `CheckForCompletedMotions` (the `CPartArray` calls, without the completion check that only
`PerformMovement` makes) and `RemoveLinkAnimations` (drop the links, complete every pending motion as a failure). It counts
the manager's pending motions so that the last can run without enumerating the queue. `HandleMovement` is
`CPartArray::HandleMovement` (11.4186 0x00517D60, 11.6096 0x00518890) → `MotionTableManager::UseTime`, the completion
check every object update makes (§5.3). `ApplyInterpretedMotion` stays the spell viewer's shortcut.

### 5.3 Hosting it

The physics track's `PhysicsObj` (the player) and the avatar body (AV7, server objects) implement `IMotionInterpHost`:
contact, walkable, gravity, in a cell, `MovementIsAutonomous`, `IsFullyConstrained`, the velocity and orientation, the
weenie, `SetLocalVelocity`, `SetOnWalkable` (which calls back `LeaveGround`/`HitGround` on a change, as
`CPhysicsObj::set_on_walkable` calls `MovementManager`), `CancelMoveTo` and `UnstickFromObject` (AV14). Each update of the
object (`CPhysicsObj::UpdatePositionInternal`) then does: the part array's root motion for the quantum, scaled by the
object's scale on walkable ground and zeroed elsewhere; `PositionManager.AdjustOffset` (§6); combine onto the position.
After the new frame, in retail's order (`CPhysicsObj::UpdateObjectInternal`): the target reports
(`TargetManager.HandleTargetting`), `MovementManager.UseTime`, **`MotionPlayer.HandleMovement`** and
`PositionManager.UseTime`. The third is `CPartArray::HandleMovement`: it completes the queued motions without animations
(a turn modifier, a stop, a style re-issued, `enter_default_state`'s Ready). The interpreter's own `PerformMovement` checks
too, but `move_to_interpreted_state` and the MoveTo's motions do not, so without this call they stay pending until the
next animation-done hook, and the jump gate and the MoveTo's steering (which waits while motions are pending) wait with
them. Every host makes it: the avatar body, `KinematicPlayerBody` and the physics track's player object.
`IPlayerBody` (`Client/Avatars`) is the player's body as the avatars read it: position, contact, walkable and its
interpreter.

### 5.4 Where retail and ACE differ

ACE's MotionInterp (the test oracle, `AceMotionInterpOracle`) follows retail closely; 500 seeded random scripts, half
server-driven creatures and half player input, give the same motion-table calls, states, pending motions and speeds. The
differences, each shown by its own test:

| | Retail (here) | ACE |
|---|---|---|
| Weenie slot 5 in `apply_current_movement` | `IsThePlayer`: only the player plays its raw state | `IsCreature`: any creature with autonomous movement does |
| Actions in `move_to_interpreted_state` | not the player, or not autonomous | a creature, or autonomous |
| Jump-gating set | 11.6096 (Purple range, Sanctuary, AI_TelegraphCast, Fallen) | 2013 range, Falling for Fallen |
| `SetHoldKey(Run)` | switches to Run | ignored |
| `DoMotion` parameters | copied whole | `CopySome` drops the jump ban, the context, the action stamp and the autonomous bit |
| `jump_is_allowed`'s pending check | whenever a motion is pending | only with two or more |
| `charge_jump`'s set | Fallen, Crouch to Sleeping | Falling, Crouch to Sitting |
| State velocity without a weenie | capped at run rate 1 | capped at `MyRunRate` |
| `enter_default_state` | appends Ready to the pending queue | clears it first |
| `GetRunRate` at Run skill above 800 | formula (the decompile tests `== 800`) | 4.5 |

## 6. Positions (AV6)

`PositionManager` is retail's `PositionManager`: interpolation, then sticky (AV14), then constraint, applied to each
update's offset (`adjust_offset`, 0x00555190 / 0x00555DB0). The managers read their object through a `PositionContext`
snapshot (position, contact, the player or not, the interpreter's two speeds, the sticky object) and report what the
object must do in return values, so they hold no reference to it. `PositionFrames` holds the position arithmetic:
distances and offsets across landblocks (`Position::distance`, `get_offset`), `Position::subtract2` and the heading
functions.

**Interpolation** (`InterpolationManager`, CInterpolationManager.c):

- `InterpolateTo` measures from the newest queued node (or the object). Beyond the autonomy blip distance (outdoors
  100 m; indoors 25 m for the player, 20 m for others) the position is queued and the failure count set to 4, so the next
  `UseTime` blips. Within 0.05 m of the object interpolation stops and the object takes the position's heading unless
  it keeps its own (`InterpolateToResult.SetHeading`). Otherwise tail nodes within 0.05 m are dropped, the queue is cut
  to 19 and the node is queued (with the object's heading when keeping it).
- `AdjustOffset` works only in contact. It **overwrites** the offset with a step towards the oldest node: the frame from
  the object to the node in local axes (`subtract2`, so a step also turns to the node's heading unless keep-heading
  zeroes the turn), its origin cut to 2 × the (adjusted) max speed × quantum, 7.5 m/s when that is below 0.0002. The
  animation's root motion is ignored until the node completes. Within 0.05 m the node completes. Every 5 frames the
  progress since the last check must reach 30% of the step speed (or the object sticks to something); if not, the node
  completes as reached under 0.2 m and as a failure beyond.
- `UseTime` blips after more than 3 failures (to the newest node) or when a failure emptied the queue (to that node).
  The host places the object and calls `CompleteBlip(placed)`; a failed placement is asked for again next time, as retail
  retries `SetPositionSimple`.
- ACE's version is not used: its `adjust_offset` assigns the step to its own parameter (InterpolationManager.cs:249), so
  ACE never interpolates. OpenAC's MIT version was read as a cross-check.

**Constraint** (`ConstraintManager`, CConstraintManager.c): `ConstrainTo` seeds the distance with the distance from the
confirmed position to the object. In contact, an offset is scaled by (max − distance) / (max − start) once the distance
passes the start and zeroed at the maximum; in contact or not, the step's length is **added** to the distance (ACE
assigns it). It is a path length, lowered only by the next `ConstrainTo`. `IsFullyConstrained` (distance > 0.9 × max)
refuses jumps. Retail's distances: start 10 m outdoors and 5 m indoors, max 50 m and 20 m, for the player and others
alike (ACE swaps the start distances). The physics track calls `ConstrainTo` for the player's newer server positions.

## 7. Deviations and loud TODOs

| Item | Status |
|---|---|
| `ContentProfile::operator==` (part-swap equality) | TODO(avatars): unpinned; part and GfxObj compared; cannot change what is drawn (§2) |
| ClothingTable heritage fallback Setups | Read by the lead (research/retail-addresses-m2.md §1.4) and applied (§4.1) |
| `HUMAN_SETUP_ID` (CharGen Setup when the sex's is null) | Read by the lead (0x02000001) and applied (§4.2); no CharGen sex has a null Setup |
| `gmCG3DView::m_alternateSetupID` | Not modelled: the constructor sets the null id and no function read sets it |
| A textured surface whose texture fails to load (`CSurface::UseTextureMap` refuses it) | Treated as a success: Lifestone does not know at apply time; no end-of-retail data has one |
| Object translucency re-applied after ObjDesc changes (`CPhysicsObj::DoObjDescChanges`) | Implicit: `AvatarFrame` gives every part the object's translucency each frame (a TransparentPart hook's value overrides one part's) |
| `ObjDesc.TryUnpack` bounds checks | Added; retail reads without them |
| `WireMotion` unknown low words | Fail and name the word; retail reads past its table |
| 11.6096 addresses of `charge_jump` and `motion_allows_jump` (predicted) and `jump_charge_is_allowed` (unmapped) | TODO(avatars): confirm from the 11.6096 bytes; the behaviour follows doc 13 §5 |
| `jump_charge_is_allowed`'s Sanctuary test (11.6096) | Kept as doc 13 states it; the forward command is never an action, so it cannot match. TODO(avatars): read the comparison from the bytes |
| `MovementSystem::GetRunRate`'s comparison with 800 | The decompile tests equality (ACE: 800 and up). TODO(avatars): confirm from the 11.6096 bytes; no M2 character comes near 800 |
| `MAX_INTERPOLATED_VELOCITY` (7.5), `BIG_DISTANCE` (999,999), `fUseAdjustedSpeed_` (true) | ACE's values; data the symbol table does not map. TODO(avatars): read from the 11.6096 data. Only a run rate of 0 reaches the first; any large value behaves as the second; for a server object both speeds are equal, so the third only matters for the player |
| Interpolation node types 2 and 3 (velocity nodes) | Not modelled: `InterpolateTo`, the module's only producer, makes position nodes |
| Sticky manager | Built (§14.3); hosts pass `PositionManager.GetStickyObjectId()` as `PositionContext.StickyObjectId` |
| A negative run rate in `get_state_velocity` (`MyRunRate` from a server MoveTo's run rate or a RunForward speed, unchecked) | **Glitch fix** (the default): retail and ACE apply the negative cap, so a standing object's velocity is NaN and a moving one's turns round; Lifestone leaves the velocity uncapped. `MotionInterpreter.RetailNegativeRunRateCap` restores retail (an option for it is requested from the lead). A cap of 0 is retail's either way |
| `Frame::set_heading` on a pitched frame | Kept: the pitch flattens slightly (retail pairs the forward axis's vertical part with a unit horizontal) |
| Server objects without collision transitions (owner decision D24) | Contact and walkable ground are the last UpdatePosition's Grounded flag; gravity is not integrated; landing stops the velocity (standing in for the contact plane and friction); outdoors the cell follows the origin, indoors it is kept until the object reaches the server's position, whose cell it then takes; an optional ground probe clamps a grounded object's height. TODO(physics): full transitions for server objects are deferred (§9.1) |
| `calc_friction` | Not modelled for server objects (EffectActor's choice too) |
| The heading rule of `UpdateObjectInternal` for an unnamed state bit | Not modelled: the decompile shows the mask as a string address; ACE reads it as Sledding, which M2 never sees |
| A server object without spheres | Retail, not a deviation: `UpdateObjectInternal` (0x005156B0 / 0x005161B0) moves only an object whose part array has spheres (`CPartArray::GetNumSphere`); for one without, it puts the old origin back into the new frame, so only the rotation changes. Root motion, interpolation and velocity never move it, AlignPath never turns it, and a server position reaches it only by a placement or the blip that follows its failed interpolation (§9.1). The ground probe, a stand-in for the transition, does not clamp it either |
| A server object without spheres and without a movement manager, on walkable ground | Not modelled: retail's sphereless branch also clears its active bit; `update_object`'s `set_active(1)` re-activates a non-Static one the next frame and restarts its clock, so that frame steps nothing; a Static one stays inactive. Lifestone's walkable ground for a server object is the Grounded flag (D24), not the contact of a transition, which a sphereless object never runs, so the rule could throttle objects retail does not; Lifestone keeps stepping them. TODO(avatars): find where a retail sphereless object gets `OnWalkable`, if anywhere |
| `MotionTableManager::UseTime` (from `CPartArray::HandleMovement`, 11.4186 0x00517D60, which `UpdateObjectInternal` calls after `MovementManager::UseTime`) | Re-expressed as `CheckForCompletedMotions`, as ACE's `MotionTableManager.UseTime` is (§5.3). Its body is not in the 11.4186 per-file decompile, and the pinned 11.6096 table names it only as the second candidate of `RemoveLinkAnimations` (0x0051CAD0), which cannot be its body (links would never play). TODO(avatars): its 11.6096 address, from `CPartArray::HandleMovement`'s (0x00518890) call |
| Static server objects | Retail, not a deviation: a Static object is active from creation only when it has a motion table or a movement (its movement manager set the bit before the described state made it Static), and is never made active again once out of range; otherwise it is never stepped, and its default animation does not play (server objects are made dynamic, so `InitDefaults` never registers them as static animating objects) (§9.1) |
| `GfxObjMeshBuilder.Build` (`Data/Models`, outside the track's paths) | Owned-path exception, for the lead to accept: av1 changed its one call of the internal `GfxObjMesh` constructor to pass the new `SurfaceIds` (§3.2); nothing else in the builder changed |
| Particles of a Hidden object | Drawn: the Hidden script's 14 emitters are the bubbles round a hidden player (§10.2). Whether retail's renderer hides anything else of a Hidden object is not pinned; its parts are not drawn. TODO(avatars): find where retail skips a Hidden object's parts (`CObjCell::hide_object` only tells detection voyeurs) |
| How first person hides the player's body | Found in M2 G5: `CameraSet::UpdateCamera` (11.4186 0x00458AE0, 11.6096 0x00458C00) calls `CPhysicsObj::SetTranslucencyHierarchical(player, 1.0)` while the camera is in the head, and fades the player out from 0.45 m to 0.2 m of the pivot (`translucency = 1 - (distance - 0.2) / 0.25`). It is a translucency of 1, not NoDraw. Lifestone hides the body's parts and keeps its held items (§11) when the translucency reaches 1 (`FirstPersonView`); a fade in between is drawn by the blended twin of each part's mesh (section 17, D45 and D63), which `FirstPersonView` does not use yet: it keeps hiding the body at translucency 1 only |
| The avatar's ordering inside one update | `EffectObject.Update` runs the hooks, particles and scripts together, after the new frame and before the target, MoveTo and blip managers; retail runs the hooks before the frame and the particles and scripts after the managers |
| A child whose parent is not there yet | Waits, out of the world, for the parent's create (retail queues the child's create for the parent; whether the reducer parks it is the state track's) |
| The scale a Scale hook sets | `EffectVisualState.Scale` (lead-owned) starts at 1, so a hook's scale replaces the description's only when it is not 1 |
| `MoveToManager` in an unpack that names an unknown word | Nothing is applied, not even the stance or the MoveTo cancel (retail reads past its table); reported as `MovementUnpackResult.UnknownCommand` |
| `IEffectEventSink` and SoundTable hooks | The sink (portal's) takes no sound table, so an avatar's SoundTable hooks cannot be resolved by it yet; requested from the lead (closed in M3 1.3 s1: `OnEffectEvent` carries the emitting body's sound table). `ServerSoundTableHook` (Client/Avatars) is the F750 event; before s1 the `SoundHookRouter` ignored it (it routed only SoundTweaked, Sound and SoundTable hooks): M3 1.3 s1 teaches the sink to play it with the caller's volume (`PlayFromTable(table, type, origin, volume)`) |

## 8. Retail functions

The functions this track re-expresses or relies on, for the lead's address table (L10). Addresses are 11.4186 / 11.6096.

| Function | 11.4186 | 11.6096 | Used in |
|---|---|---|---|
| `ObjDesc::UnPack` | 0x005ABCF0 | 0x005ACDA0 | `ObjDesc.TryUnpack`, `FromWire` |
| `ObjDesc::AddSubpalette` | 0x005AB970 | 0x005ACA20 | `ObjDescBuilder.AddSubPalette` |
| `ObjDesc::AddTextureMapChange` | 0x005ABA10 | 0x005ACAC0 | `ObjDescBuilder.AddTextureMapChange` |
| `ObjDesc::AddAnimPartChange` | 0x005ABA80 | 0x005ACB30 | `ObjDescBuilder.AddAnimPartChange` |
| `ObjDesc::RemoveDuplicateSubpalette` | 0x005AB640 | 0x005AC6F0 | `ObjDescBuilder` |
| `ObjDesc::RemoveDuplicateTextureMapChange` | 0x005AB6B0 | 0x005AC760 | `ObjDescBuilder` |
| `ObjDesc::RemoveDuplicateAnimPartChange` | 0x005AB720 | 0x005AC7D0 | `ObjDescBuilder` |
| `ObjDesc::operator+=` | 0x005AC0A0 | 0x005AD150 | `ObjDesc.Add`, `ObjDescBuilder.Add` |
| `ObjDesc::Wipe` | 0x005AB820 | 0x005AC8D0 | `ObjDescBuilder.Wipe` |
| `Subpalette::replaces` / `supercedes` | 0x005AD830 / 0x005AD870 | 0x005AE8E0 / 0x005AE920 | `SubPalette` (textures track) |
| `Subpalette::UnPack` | 0x005ADAB0 | 0x005AEB60 | `WireSubPalette`, `SubPalette.FromWire` |
| `TextureMapChange::replaces` | 0x005AD980 | 0x005AEA30 | `TextureMapChange.Replaces` |
| `TextureMapChange::UnPack` / `AnimPartChange::UnPack` | 0x005AD920 / 0x005ADA10 | 0x005AE9D0 / 0x005AEAC0 | `ObjDesc.TryUnpack` |
| `ContentProfile::operator==` | 0x006AFEB0 | 0x006B0D80 | TODO (§2) |
| `Unpack_AsDataIDOfKnownType` | 0x006844F0 | 0x006854F0 | `ObjDesc.TryUnpack` |
| `CPartArray::DoObjDescChanges` | 0x00519210 | 0x00519D40 | `PartArrayAppearance.DoObjDescChanges` |
| `CPartArray::DoObjDescChangesFromDefault` | 0x00519480 | 0x00519FB0 | `PartArrayAppearance.DoObjDescChangesFromDefault` |
| `CPartArray::SetPart` | 0x00518580 | 0x005190B0 | part swaps |
| `CPartArray::SetTextureMap` | 0x005185E0 | 0x00519110 | texture swaps |
| `CPartArray::SetPalette` | 0x00518E10 | 0x00519940 | palettes |
| `CPartArray::InitObjDescChanges` | 0x00518540 | 0x00519070 | `DoObjDescChanges` |
| `CPartArray::InitPals` / `DestroyPals` | 0x005181C0 / 0x00518210 | 0x00518CF0 / 0x00518D40 | palette slots |
| `CPartArray::InitParts` | 0x00517F40 | 0x00518A70 | `PartArrayAppearance.IsLoadable` |
| `CPhysicsPart::SetPart` | 0x0050E700 | 0x0050F1D0 | part swaps |
| `CPhysicsPart::LoadGfxObjArray` | 0x0050DCF0 | 0x0050E7C0 | `IAppearanceSource.GetChain` (`LodChain`) |
| `CPhysicsPart::SetGfxObjArray` | 0x0050E2E0 | 0x0050EDB0 | part swaps |
| `CPhysicsPart::DetermineBasePal` | 0x0050DC80 | 0x0050E750 | `PartAppearance.BasePaletteId` |
| `CPhysicsPart::SetTextureMap` | 0x0050DE30 | 0x0050E900 | texture swaps |
| `CPhysicsPart::UsePalette` | 0x0050DED0 | 0x0050E9A0 | palettes |
| `CPhysicsPart::RestorePalette` | 0x0050D5B0 | 0x0050E080 | `DoObjDescChangesFromDefault` |
| `CPhysicsPart::InitObjDescChanges` | 0x0050E3B0 | 0x0050EE80 | `DoObjDescChanges` |
| `CPhysicsPart::CopySurfaces` / `RestoreSurfaces` | 0x0050D6A0 / 0x0050D730 | 0x0050E170 / 0x0050E200 | texture swaps |
| `Palette::makeModifiedPalette(palID, subs)` | 0x0053E3C0 | 0x0053F120 | palettes |
| `Palette::Modify(subs)` / `Modify(id, offset, n)` | 0x0053E1F0 / 0x0053E160 | 0x0053EF50 / 0x0053EEC0 | palettes |
| `CSurface::UseTextureMap(id, force)` | 0x005362E0 | 0x00537020 | texture swaps |
| `CSurface::UsePalette` | 0x00535F20 | 0x00536C60 | palettes |
| `CSurface::GetOriginalTextureMapID` / `GetOriginalPaletteID` | 0x005363E0 / 0x005363F0 | 0x00537120 / 0x00537130 | `IAppearanceSource` |
| `CSurface::InitEnd` / `Serialize` | 0x00536400 / 0x005365F0 | 0x00537140 / 0x00537330 | `TextureLoader.GetOriginalPaletteId` |
| `CPhysicsObj::DoObjDescChanges` / `DoObjDescChangesFromDefault` | 0x0050FA30 / 0x0050F9B0 | 0x00510500 / 0x00510480 | AV7 (translucency) |
| `ClothingTable::BuildObjDesc` | 0x005A7900 | 0x005A89B0 | `ClothingObjDesc.Build` |
| `ClothingBase::ApplyPartAndTextureChanges` | 0x005A8EB0 | 0x005A9F60 | `ClothingObjDesc.Build` |
| `ShadePackage::ShadePackage` / `GetVal` | 0x005A8290 / 0x005A82C0 | 0x005A9340 / 0x005A9370 | `ShadePackage` |
| `PalSet::GetPaletteID` | 0x005AC570 | 0x005AD620 | `PaletteSets.GetPaletteId` (textures track) |
| `gmCG3DView::Update` | 0x004EE9D0 | 0x004EF660 | `CharGenAppearance.Compose` |
| `gmCG3DView::gmCG3DView` / `Initialize` | 0x004EE680 / 0x004EE760 | 0x004EF310 / 0x004EF3F0 | AV12 |
| `gmCG3DView::StartAnimation` / `StopAnimation` | 0x004EE600 / 0x004EE640 | 0x004EF290 / 0x004EF2D0 | AV12 |
| `CharGenState::GetSetupID` | 0x005C5A90 | 0x005C6A70 | `CharGenAppearance.ResolveSetupId` |
| `CharGenState::GetBaldState` | 0x005C52A0 | 0x005C6280 | `CharGenAppearance.Compose` |
| `CharGenState::StoreColorInformation` | 0x005C44D0 | 0x005C54B0 | G3 (colour buttons, §4.2) |
| `CharGenState::GetHeadgearPaletteTemplateID` (and Shirt, Trousers, Footwear) | 0x005C38F0-0x005C3980 | 0x005C48D0-0x005C4960 | G3 |

| `CMotionInterp::Create` / `enter_default_state` | 0x00528C00 / 0x00528C80 | 0x00529810 / 0x00529890 | `MotionInterpreter` constructor, `EnterDefaultState` |
| `CMotionInterp::PerformMovement` | 0x00528E80 | 0x00529A90 | `PerformMovement` |
| `CMotionInterp::DoMotion` / `DoInterpretedMotion` | 0x00528D20 / 0x00528360 | 0x00529930 / 0x00528F70 | `DoMotion`, `DoInterpretedMotion` |
| `CMotionInterp::StopMotion` / `StopInterpretedMotion` / `StopCompletely` | 0x00528530 / 0x00528470 / 0x00527E40 | 0x00529140 / 0x00529080 / 0x00528A50 | `StopMotion`, `StopInterpretedMotion`, `StopCompletely` |
| `CMotionInterp::move_to_interpreted_state` | 0x005289C0 | 0x005295D0 | `MoveToInterpretedState` |
| `CMotionInterp::apply_raw_movement` / `apply_interpreted_movement` / `apply_current_movement` | 0x005287E0 / 0x00528600 / 0x00528870 | 0x005293F0 / 0x00529210 / 0x00529480 | the `Apply*Movement` methods |
| `CMotionInterp::ReportExhaustion` / `SetWeenieObject` | 0x005288D0 / 0x00528920 | 0x005294E0 / 0x00529530 | `ReportExhaustion`, `OnWeenieChanged` |
| `CMotionInterp::SetHoldKey` / `set_hold_run` | 0x00528BB0 / 0x00528B70 | 0x005297C0 / 0x00529780 | `SetHoldKey`, `SetHoldRun` |
| `CMotionInterp::HitGround` / `LeaveGround` | 0x00528AC0 / 0x00528B00 | 0x005296D0 / 0x00529710 | `HitGround`, `LeaveGround` |
| `CMotionInterp::charge_jump` | 0x005281C0 | 0x00528DD0 (predicted; TODO(avatars): 11.6096 confirm) | `ChargeJump` |
| `CMotionInterp::jump_charge_is_allowed` | 0x00527A50 | TODO(avatars): 11.6096 (unmapped) | `JumpChargeIsAllowed` |
| `CMotionInterp::jump_is_allowed` / `jump` | 0x005282B0 / 0x00528780 | 0x00528EC0 / 0x00529390 | `JumpIsAllowed`, `Jump` |
| `CMotionInterp::motion_allows_jump` | 0x005279E0 | 0x005285E0 (predicted; TODO(avatars): 11.6096 confirm) | `MotionAllowsJump` |
| `CMotionInterp::contact_allows_move` / `is_standing_still` | 0x00528240 / 0x00527FA0 | 0x00528E50 / 0x00528BB0 | `ContactAllowsMove`, `IsStandingStill` |
| `CMotionInterp::adjust_motion` / `apply_run_to_command` | 0x00528010 / 0x00527BE0 | 0x00528C20 / 0x005287F0 | `AdjustMotion`, `ApplyRunToCommand` |
| `CMotionInterp::get_state_velocity` / `get_jump_v_z` / `get_leave_ground_velocity` | 0x00527D50 / 0x00527AA0 / 0x005280C0 | 0x00528960 / 0x005286B0 / 0x00528CD0 | `GetStateVelocity`, `GetJumpVelocityZ`, `GetLeaveGroundVelocity` |
| `CMotionInterp::get_max_speed` / `get_adjusted_max_speed` | 0x00527CB0 / 0x00527D00 | 0x005288C0 / 0x00528910 | `GetMaxSpeed`, `GetAdjustedMaxSpeed` |
| `CMotionInterp::MotionDone` / `HandleExitWorld` / `add_to_queue` / `motions_pending` | 0x00527EC0 / 0x00527F30 / 0x00527B80 / 0x00527FE0 | 0x00528AD0 / TODO(avatars): 11.6096 / 0x00528790 / 0x00528BF0 | pending motions |
| `CMotionInterp::InqStyle` | 0x00527B10 | TODO(avatars): 11.6096 (folded) | `InqStyle` |
| `InterpretedMotionState::ApplyMotion` / `RemoveMotion` / `copy_movement_from` | 0x0051EA40 / 0x0051E790 / 0x0051E750 | 0x0051F550 / 0x0051F2A0 / 0x0051F260 | `InterpretedMotionState` |
| `InterpretedMotionState::AddAction` / `RemoveAction` / `GetNumActions` | 0x0051E9E0 / 0x0051EAD0 / 0x0051EB00 | 0x0051F4F0 / 0x0051F5E0 / 0x0051F610 | `InterpretedMotionState` |
| `InterpretedMotionState::UnPack` / `Pack` | 0x0051F400 / 0x0051F1D0 | 0x0051FF10 / 0x0051FCE0 | `WireMotion` |
| `RawMotionState::ApplyMotion` / `RemoveMotion` / `AddAction` / `RemoveAction` | 0x0051EB60 / 0x0051E6E0 / 0x0051E840 / 0x0051E8A0 | 0x0051F670 / 0x0051F1F0 / 0x0051F350 / 0x0051F3B0 | `RawMotionState` |
| `MovementParameters::MovementParameters` | 0x00524380 | 0x00524F80 | `MovementParameters` |
| `MovementManager::unpack_movement` | 0x00524440 | 0x00525040 | AV8 (§5.1) |
| `MovementSystem::GetRunRate` / `GetJumpHeight` / `JumpStaminaCost` | 0x006B0950 / 0x006B09B0 / 0x006B0A40 | 0x006B1820 / 0x006B1880 / 0x006B1910 | `MovementSystem` |
| `EncumbranceSystem::EncumbranceCapacity` / `Load` / `LoadMod` | 0x004FCC00 / 0x004FCC40 / 0x004FCC70 | 0x004FD7A0 / 0x004FD7E0 / 0x004FD810 | `EncumbranceSystem` |
| `CPartArray::DoInterpretedMotion` / `StopInterpretedMotion` / `StopCompletelyInternal` | 0x00518750 / 0x005187F0 / 0x00518890 | 0x00519280 / 0x00519320 / 0x005193C0 | `MotionPlayer` |
| `CPartArray::InitializeMotionTables` / `CheckForCompletedMotions` | 0x00517D10 / 0x00517D50 | TODO(avatars): 11.6096 | `MotionPlayer` |
| `CPhysicsObj::RemoveLinkAnimations` → `CPartArray::RemoveLinkAnimations` → `MotionTableManager::RemoveLinkAnimations` | 0x0050FE20 / 0x00517D70 / 0x0051BDD0 | 0x005108F0 / 0x005188A0 / 0x0051CAD0 | `MotionPlayer.RemoveLinkAnimations` |
| `InterpolationManager::InterpolateTo` / `adjust_offset` / `UseTime` | 0x00555B20 / 0x00555D30 / 0x00555F20 | 0x00556740 / 0x00556950 / 0x00556B40 | `InterpolationManager` |
| `InterpolationManager::NodeCompleted` / `StopInterpolating` | 0x005559A0 / 0x00555950 | 0x005565C0 / 0x00556570 | `InterpolationManager` |
| `ConstraintManager::adjust_offset` | 0x00556180 | 0x00556DA0 | `ConstraintManager.AdjustOffset` |
| `ConstraintManager::ConstrainTo` / `UnConstrain` / `IsFullyConstrained` | 0x00556240 / 0x005560C0 / 0x005560D0 | TODO(avatars): 11.6096 (unmapped) | `ConstraintManager` |
| `PositionManager::adjust_offset` / `UseTime` / `InterpolateTo` / `StopInterpolating` / `UnConstrain` | 0x00555190 / 0x00555160 / 0x005551F0 / 0x00555220 / 0x005552B0 | 0x00555DB0 / 0x00555D80 / 0x00555E10 / 0x00555E40 / 0x00555ED0 | `PositionManager` |
| `PositionManager::ConstrainTo` / `IsFullyConstrained` / `IsInterpolating` | 0x00555280 / 0x005552C0 / 0x005553B0 | TODO(avatars): 11.6096 | `PositionManager` |
| `CPhysicsObj::GetAutonomyBlipDistance` / `GetStartConstraintDistance` / `GetMaxConstraintDistance` | 0x0050EB70 / 0x0050EBC0 / 0x0050EC10 | 0x0050F640 / 0x0050F690 / 0x0050F6E0 | `PositionManager` |
| `CPhysicsObj::UpdatePositionInternal` | 0x00512C30 | 0x00513730 | the host's update order (§5.3) |
| `Position::distance` / `Position::subtract2` | 0x005A94B0 / 0x00555A90 | 0x005AA560 / 0x005566B0 | `PositionFrames` |
| `Frame::subtract2` / `get_heading` / `set_heading` / `set_vector_heading` | 0x00535650 / 0x00535760 / 0x00535E40 / 0x00535DB0 | 0x00536390 / 0x005364A0 / 0x00536B80 / 0x00536AF0 | `PositionFrames` |

Stage av3:

| Function | 11.4186 | 11.6096 | Used in |
|---|---|---|---|
| `CPhysicsObj::update_object` / `UpdateObjectInternal` / `UpdatePositionInternal` / `UpdatePhysicsInternal` | 0x00515D10 / 0x005156B0 / 0x00512C30 / 0x00510700 | 0x00516810 / 0x005161B0 / 0x00513730 / 0x005111D0 | `AvatarBody.Advance` |
| `CPartArray::GetNumSphere` | 0x00518060 | TODO(avatars): 11.6096 (0x00518B90 if it keeps the +0xB30 shift of its neighbours `InitParts` and `SetPart`) | `AvatarBody`: only an object with spheres moves |
| `CPhysicsObj::set_active` / `set_state` / `set_nodraw` / `set_lights` / `set_hidden` | 0x0050FC40 / 0x00514DD0 / 0x0050FCA0 / 0x0050FCF0 / 0x00514C60 | 0x00510710 / 0x005158D0 / 0x00510770 / 0x005107C0 / 0x00515760 | `AvatarBody.SetState` (set_hidden through portal's `HiddenTransition`) |
| `CPhysicsObj::set_parent` / `unset_parent` / `add_child` / `UpdateChild` / `UpdateChildrenInternal` | 0x00515A90 / 0x00513470 / 0x0050F870 / 0x00512D50 / 0x00513DA0 | 0x00516590 / 0x00513F70 / 0x00510340 / 0x00513850 / 0x005148A0 | `AvatarBody.SetParent` and its children |
| `CPhysicsObj::leave_world` / `exit_world` / `teleport_hook` / `MoveOrTeleport` | 0x005155A0 / 0x00514E60 / 0x00514ED0 / 0x00516330 | 0x005160A0 / 0x00515960 / 0x005159D0 / 0x00516E30 | `AvatarBody` |
| `CPhysicsObj::SetPosition` / `SetPositionSimple` / `InterpolateTo` / `ConstrainTo` / `SetPlacementFrame` / `HasAnims` | 0x005160C0 / 0x005162B0 / 0x005104F0 / 0x00510520 / 0x00511380 / 0x0050F770 | 0x00516BC0 / 0x00516DB0 / 0x00510FC0 / 0x00510FF0 / 0x00511E50 / 0x00510240 | `AvatarBody.MoveOrTeleport`, `Place`, `SetPlacementFrame` |
| `CPhysicsObj::set_description` / `InitDefaults` / `SetMotionTableID` / `MakeMovementManager` / `makeObject` | 0x00514F40 / 0x005139D0 / 0x00512780 / 0x00510270 / 0x00513970 | 0x00515A40 / 0x005144D0 / 0x00513280 / 0x00510D40 / 0x00514470 | `AvatarBody.Describe` |
| `CPhysicsObj::MakePositionManager` / `prepare_to_enter_world` / `animate_static_object` | 0x00510210 / 0x00511FA0 / 0x00513DF0 | 0x00510CE0 / 0x00512A70 / 0x005148F0 | `AvatarBody.Describe` (Static objects, §9.1) |
| `CPartArray::HandleMovement` → `MotionTableManager::UseTime` | 0x00517D60 / not in the per-file decompile | 0x00518890 / TODO(avatars): 11.6096 | `MotionPlayer.HandleMovement`, every host's update (§5.3) |
| `MotionTableManager::CheckForCompletedMotions` | 0x0051BE00 | 0x0051C900 | `MotionPlayer` |
| `CPhysicsObj::set_velocity` / `set_local_velocity` / `set_on_walkable` / `set_heading` / `get_heading` / `get_velocity` | 0x005113F0 / 0x005114D0 / 0x00511310 / 0x00514160 / 0x00511540 / 0x005113C0 | 0x00511EC0 / 0x00511FA0 / 0x00511DE0 / 0x00514C60 / 0x00512010 / 0x00511E90 | `AvatarBody` |
| `CPhysicsObj::set_sequence_animation` / `clear_sequence_anims` / `update_position` | 0x0050F6F0 / 0x0050F760 / 0x00514520 | 0x005101C0 / 0x00510230 / 0x00515020 | `AnimSequence.SetSequenceAnimation`, `CreaturePreview` |
| `CPartArray::InitDefaults` / `HandleEnterWorld` (= `RemoveLinkAnimations`) / `SetNoDrawInternal` / `SetPlacementFrame` | 0x00518980 / 0x00517D70 / 0x00518300 / 0x005193D0 | 0x005194B0 / 0x005188A0 / 0x00518E30 / 0x00519F00 | `AvatarBody` |
| `CPartArray::GetRadius` / `GetHeight` | 0x005180A0 / 0x005180B0 | 0x00518BD0 / 0x00518BE0 | `AvatarBody.Radius`, `Height` |
| `CPhysics::SetObjectMovement` / `CPhysicsObj::unpack_movement` | 0x00509690 / 0x00512040 | 0x0050A160 / 0x00512B10 | `AvatarSet.SetMovement`, `AvatarBody.ApplyMovement` |
| `SmartBox::HandleCreateObject` / `HandleUpdateObject` / `HandleDeleteObject` / `UpdateVisualDesc` | 0x00454C80 / 0x00455580 / 0x00451EA0 / 0x00451F40 | 0x00454D20 / 0x00455620 / 0x00451EE0 / 0x00451F80 | `AvatarSet.Create`, `Delete`, `Apply(ObjectUpdated)` |
| `SmartBox::HandleReceivedPosition` / `DoSetState` / `DoVectorUpdate` / `DoPickupEvent` / `DoParentEvent` / `HandleObjDescEvent` | 0x00453FD0 / 0x004520D0 / 0x004521C0 / 0x00452240 / 0x00452290 / 0x00453340 | 0x00454070 / 0x00452110 / 0x00452200 / 0x00452280 / 0x004522D0 / 0x00453380 | `AvatarSet` |
| `ACCObjectMaint::CreateObject` / `SetVisualDesc` / `CObjectMaint::SetChildren` | 0x00558870 / 0x00557660 / 0x00509370 | 0x005594B0 / 0x005582A0 / 0x00509E40 | `AvatarSet.Create`, `AvatarBody.SetVisualDesc` |
| `CObjCell::hide_object` / `unhide_object` | 0x0052BE30 / 0x0052BE40 | 0x0052CA40 / 0x0052CA50 | read (§7) |
| `CPhysicsPart::UpdateViewerDistance` | 0x0050E030 | 0x0050EB00 | `AvatarFrame` (the player's level 0) |
| `gmCG3DView::SetPlayerHeading` | 0x004EE590 | 0x004EF220 | `CreaturePreview.SetHeading` |
| `UIElement_Viewport::SetLight` / `CreatureMode::Render` | 0x0046BB90 / 0x004529D0 | 0x0046BE40 / 0x00452A10 | `CreaturePreview` |
| `MovementManager::unpack_movement` / `PerformMovement` / `MakeMoveToManager` / `CancelMoveTo` / `UseTime` / `HitGround` / `LeaveGround` / `HandleExitWorld` / `HandleUpdateTarget` / `IsMovingTo` | 0x00524440 / 0x005240D0 / 0x00524000 / 0x005241B0 / 0x005242F0 / 0x00524300 / 0x00524320 / 0x00524350 / 0x00524790 / 0x00524260 | 0x00525040 / 0x00524CD0 / 0x00524C00 / 0x00524DB0 / 0x00524EF0 / 0x00524F00 / 0x00524F20 / 0x00524F50 / 0x00525390 / 0x00524E60 | `MovementManager` |
| `MovementParameters::UnPackNet` / `get_command` / `towards_and_away` / `get_desired_heading` | 0x0052AC50 / 0x0052AA00 / 0x0052A9A0 / 0x0052AAD0 | 0x0052B860 / 0x0052B610 / 0x0052B5B0 / 0x0052B6E0 | `MovementParameters` |
| `MoveToManager::MoveToObject` / `MoveToObject_Internal` / `MoveToPosition` / `TurnToObject` / `TurnToObject_Internal` / `TurnToHeading` / `PerformMovement` | 0x00529680 / 0x0052A400 / 0x0052A240 / 0x005297D0 / 0x0052A550 / 0x0052A630 / 0x0052A900 | 0x0052A290 / 0x0052B010 / 0x0052AE50 / 0x0052A3E0 / 0x0052B160 / 0x0052B240 / 0x0052B510 | `MoveToManager` |
| `MoveToManager::CancelMoveTo` / `CleanUp` / `CleanUpAndCallWeenie` / `InitializeLocalVariables` / `BeginNextNode` / `BeginMoveForward` / `BeginTurnToHeading` | 0x00529930 / 0x005295C0 / 0x00529650 / 0x00529250 / 0x00529CB0 / 0x00529A00 / 0x00529B90 | 0x0052A540 / 0x0052A1D0 / 0x0052A260 / 0x00529E60 / 0x0052A8C0 / 0x0052A610 / 0x0052A7A0 | `MoveToManager` |
| `MoveToManager::HandleMoveToPosition` / `HandleTurnToHeading` / `HandleUpdateTarget` / `CheckProgressMade` / `GetCurrentDistance` / `_DoMotion` / `_StopMotion` | 0x00529D80 / 0x0052A0C0 / 0x0052A7D0 / 0x005290F0 / 0x005291B0 / 0x00529010 / 0x00529080 | 0x0052A990 / 0x0052ACD0 / 0x0052B3E0 / 0x00529D00 / 0x00529DC0 / 0x00529C20 / 0x00529C90 | `MoveToManager` |
| `MoveToManager::HitGround` / `UseTime` | 0x00529D70 / 0x0052A780 | TODO(avatars): 11.6096 (unmapped) | `MoveToManager` |
| `MoveToManager::AddTurnToHeadingNode` / `AddMoveToPositionNode` / `RemovePendingActionsHead` / `is_moving_to`; `heading_greater` / `heading_diff` | 0x00529530 / 0x00529580 / 0x00529380 / 0x00529220; 0x00528F60 / 0x00528FB0 | 0x0052A140 / 0x0052A190 / 0x00529F90 / 0x00529E30; 0x00529B70 / 0x00529BC0 | `MoveToManager` |
| `StickyManager::Create` / `StickTo` / `adjust_offset` / `HandleUpdateTarget` | 0x00555800 / 0x00555710 / 0x00555430 / 0x00555780 | 0x00556420 / 0x00556330 / 0x00556050 / 0x005563A0 | `StickyManager` |
| `StickyManager::HandleExitWorld` / `UseTime` | 0x00555400 / 0x00555610 | TODO(avatars): 11.6096 (unmapped) | `StickyManager.Unstick`, `UseTime` |
| `PositionManager::StickTo` / `UnStick` / `GetStickyObjectID` / `HandleUpdateTarget` | 0x00555230 / 0x005551E0 / 0x00555270 / 0x005553D0 | 0x00555E50 / 0x00555E00 (named `HandleExitWorld`) / TODO(avatars): 11.6096 / 0x00555FF0 | `PositionManager` |
| `TargetManager::SetTarget` / `ClearTarget` / `SetTargetQuantum` / `AddVoyeur` / `ReceiveUpdate` / `HandleTargetting` | 0x0051AC30 / 0x0051A7E0 / 0x0051A4A0 / 0x0051A830 / 0x0051A930 / 0x0051AA90 | 0x0051B760 / 0x0051B310 / 0x0051B040 / 0x0051B360 / 0x0051B460 / 0x0051B5C0 | `TargetManager` |
| `TargetManager::CheckAndUpdateVoyeur` / `SendVoyeurUpdate` / `GetInterpolatedPosition` / `NotifyVoyeurOfEvent` / `RemoveVoyeur` | 0x0051A650 / 0x0051A4F0 / 0x0051A5E0 / 0x0051A6F0 / 0x0051AD90 | 0x0051B1F0 / 0x0051B090 / 0x0051B180 / 0x0051B290 / TODO(avatars): 11.6096 | `TargetManager` |
| `CPhysicsObj::set_target` / `clear_target` / `set_target_quantum` / `get_target_quantum` / `receive_target_update` / `add_voyeur` / `remove_voyeur` / `HandleUpdateTarget` | 0x0050ED30 / 0x0050ED90 / 0x0050EDA0 / 0x0050EDC0 / 0x0050EDE0 / 0x0050EE00 / 0x0050EE50 / 0x00512BC0 | 0x0050F800 / 0x0050F860 / 0x0050F870 / 0x0050F890 / 0x0050F8B0 / 0x0050F8D0 / 0x0050F920 / 0x005136C0 | `TargetManager`, `AvatarBody` |
| `CPhysicsObj::MoveToObject` / `TurnToObject` / `TurnToHeading` / `stick_to_object` / `unstick_from_object` / `cancel_moveto` / `StopCompletely` | 0x00512860 / 0x00512940 / 0x00512980 / 0x005127E0 / 0x0050EAE0 / 0x005101F0 / 0x00510180 | 0x00513360 / 0x00513440 / 0x00513480 / 0x005132E0 / 0x0050F5B0 / 0x00510CC0 / 0x00510C50 | `MovementManager`, `AvatarBody` |
| `Position::heading` / `cylinder_distance` / `cylinder_distance_no_z` | 0x005A9520 / 0x005A97F0 / 0x005A98B0 | 0x005AA5D0 / 0x005AA8A0 / 0x005AA960 | `PositionFrames` |

## 9. Server objects (AV7, AV8)

### 9.1 The body

`AvatarBody` is one server object as retail's `CPhysicsObj` holds it, without collision: its part array (appearance and
animation), its effects, its `MovementManager` (interpreter and MoveTo), its `PositionManager` (interpolation, sticky,
constraint), its `TargetManager`, its children and its parent. It implements `IMotionInterpHost` and `IMoveToHost`.

**Stepping** (`CPhysicsObj::update_object`). A body with a parent, outside a cell or Frozen is not stepped (a held object
moves with its parent, §10). One more than 96 m from the player is inactive: its clock still runs, so its particles
and scripts do too (retail runs them outside `UpdateObjectInternal`'s active block), but nothing moves; coming back
within 96 m restarts its clock, so the time away is not replayed. A Static body is never made active (`set_active(1)`
refuses it): it is active only from creation, and only with a motion table or a movement, whose movement manager set the
bit before the described state made it Static (`ACCObjectMaint::CreateObject` makes every server object dynamic, and
`set_description` sets the state last); once out of range it stays inactive. A Static body without either is never
stepped, its particles and scripts aside, so neither interpolation nor its default animation moves it (a server object
is never one of `animate_static_object`'s static animating objects). Otherwise the object clock slices the frame into
quanta of at most 0.2 s and each quantum is `UpdateObjectInternal`:

1. `UpdatePositionInternal`: unless Hidden, the part array's update, whose root motion is scaled by the object's scale on
   walkable ground and zeroed elsewhere, and whose hooks go to the effects; `PositionManager.AdjustOffset`, always; the
   offset combined onto the position; unless Hidden, `UpdatePhysicsInternal` (50 m/s cap, under 0.25 m/s zeroed,
   acceleration, angular velocity; no friction).
2. The new frame. An object whose Setup has no spheres (`CPartArray::GetNumSphere` 0) keeps its origin and takes only
   the new rotation, as retail does: root motion, interpolation and velocity never move it, so a server position reaches
   it only by a placement or by the blip once its interpolation has failed (two progress checks, ten updates); AlignPath
   does not turn it and the ground probe does not clamp it. Any other object takes the new frame without a transition
   (D24): AlignPath turns it along its motion; outdoors the cell follows the origin (`LandDefs.AdjustToOutside`); indoors
   it is kept until the object stands at the server's last position, whose cell it then takes; with a ground probe a
   grounded object's height is clamped to the ground under its lowest sphere. The measured velocity (`cached_velocity`)
   is the step over the quantum for an object with spheres that moved, as retail's transition gives it, and zero
   otherwise.
3. The parts posed, the children moved with them.
4. The effects' hooks, particles and scripts; then retail's order: the target reports (`HandleTargetting`); the MoveTo
   (`MovementManager::UseTime`); the part array's completion check (`CPartArray::HandleMovement`, §5.3); the
   interpolation's blip, placed at once (`PositionManager::UseTime`).

**Contact.** No transition runs for a server object in M2, so contact and walkable ground are the last UpdatePosition's
Grounded flag (ACE sets it from its own `OnWalkable`). A change calls `HitGround` or `LeaveGround` as
`CPhysicsObj::set_on_walkable` does; landing also stops the physics velocity, standing in for the contact plane and the
friction a transition would apply. Gravity is not integrated.

**The anchor landblock.** The body's drawn frames (its frame, its parts, its particles, its children) are expressed in
one landblock's space, `AnchorCellId`. It moves only on a placement (a teleport, a blip, entering the world) or when the
object is more than two landblocks away, so walking across a landblock seam keeps every frame and every particle
continuous. `AvatarDraw.AnchorCellId` tells the renderer which landblock the frames are in.

**`MoveOrTeleport`** (for a newer server position, after `SmartBox::HandleReceivedPosition` unset the parent and set the
placement of an object with no animations): a newer teleport sequence, or no cell, places the object (with the teleport
hook: the MoveTo cancelled with 0x3C, the stick, interpolation and constraint ended); no contact ignores the position;
96 m or more from the player places it (`SetPositionSimple`); otherwise it interpolates there, keeping its heading while a
MoveTo is under way. Then it is constrained to its own position. A position for a held object unparents it; it keeps the
cell its parent gave it, so it interpolates from the hand to where it fell.

### 9.2 The set

`AvatarSet` applies the state track's classified events with retail's object semantics (`SmartBox`), and has **no queue
and no stamp gate**: the reducer owns those (m2-plan §1.4), so every event is already newer and in order. Each `Apply`
returns `AvatarApply`: `Applied`, `Ignored` (retail ignores it), `UnknownObject` (counted and logged; the reducer should
have parked it) or `Failed` (logged and noticed). The same operations take Lifestone.Data types (`Create`, `Move`,
`SetMovement`, `SetState`, `SetParent`, `PickUp`, `SetVisualDesc`, `Delete`), which the stress crowd and the tests use.

**Create** (`SmartBox::HandleCreateObject`, `ACCObjectMaint::CreateObject`, `CPhysicsObj::set_description`):

1. The Setup (missing: `CreateFailed`) and its parts (one that does not load refuses the object, as
   `CPartArray::InitParts` does), the resting placement, the default animation and the default script
   (`CPhysicsObj::InitDefaults`).
2. The motion table: the description's, with a movement manager in its default state. **The description's table
   replaces the Setup's**: 0 means none (`set_description` calls `SetMotionTableID` with the description's id); when the
   Setup had one, its default state keeps cycling without an interpreter, as retail's sequence keeps its animations.
3. The physics script table, the movement (unpacked as an UpdateMotion would be) or else the placement, the state, the
   scale, the translucency, the vectors and the default script.
4. The ObjDesc (`DoObjDescChanges`; an ObjDescEvent later uses `DoObjDescChangesFromDefault`, through
   `ACCObjectMaint::SetVisualDesc`).
5. The children the description lists (`CObjectMaint::SetChildren`) and the parent; with neither, it enters the world at
   its position, when it has one. A child whose parent is not there yet waits, out of the world, for the parent's create.
6. A recreate (`Recreated`, F7DB or a newer instance) removes the old avatar first; its children come back through the
   new description's list.

An object created Hidden goes through portal's `HiddenTransition` (§10.2) when `set_description` applies its state, after
its script table: it is registered hidden and plays its Hidden script.

`AvatarCreate` holds what a create needs in Lifestone.Data terms; `AvatarWire.FromWorldObject` builds it from the
session's `WorldObject` (its CreateObject's physics description with its current visual description, state, position,
parent, vectors and motion). `AvatarWire` also maps `ModelData` to an `ObjDesc` and a `ServerMotionState` to a
`WireMovement` with its actions copied into a caller's span. `AvatarSources` holds the dats (`FromDats`) and caches the
Setups, motion tables, placement poses and sort centres.

### 9.3 Movements

`AvatarSet.SetMovement` is `CPhysics::SetObjectMovement` after the stamps: the player's autonomous echo is ignored; the
player's server movements go to its body (§11); every other object records the autonomous bit as its last movement's and
unpacks the movement. An object without a motion table ignores it, as retail's movement manager without an interpreter
does (counted).

`MovementManager.UnpackMovement` is `unpack_movement`: the MoveTo is cancelled and the stick ended; the stance's command is
played when it differs from the interpreted style; then type 0 moves to the interpreted state, sticks to the sticky object
and sets the standing-long-jump flag; types 6 and 7 set the run rate and move to the object (when it is known here) or to
the origin given; type 8 turns to the object, or, when it is unknown, to the heading the message carries; type 9 turns to
the heading. Every word is checked before anything is applied: an unknown stance, command or action applies nothing and
is reported with its field and word (retail reads past its table).

## 10. Children, parenting and Hidden (AV9)

### 10.1 Parenting

`AvatarBody.SetParent` is `CPhysicsObj::set_parent(obj, where)`: the parent's Setup must have the holding location
(`CSetup::GetHoldingLocation`, `ModelAssembly.HoldingLocations`); a missing one fails (logged and noticed) and the child
keeps its state. The child leaves its old parent and the world, follows the location's part with the location's frame,
takes its placement (`SmartBox::DoParentEvent`), and is NoDraw while the parent is Hidden. Every update of the parent
moves it (`UpdateChild`: the part's frame, or the object's for a part index beyond the parts, combined with the holding
frame) and runs its particles and scripts; a held object is never stepped on its own (`update_object` returns for a
child). A pickup (`DoPickupEvent`) unparents it and takes it out of the world. Deleting the parent lets its children go,
out of the world.

### 10.2 State, NoDraw and Hidden

`AvatarBody.SetState` is `CPhysicsObj::set_state`: only changed bits act. LightingOn turns the Setup's lights on or off
(`AvatarDraw.LightsOn`); NoDraw hides the parts; Hidden goes through the portal track's `HiddenTransition` (m2-plan
§1.4: the rule lives there once): the Hidden or UnHide script on the object's PhysicsScriptTable, the collision bits; the
avatars add the children's NoDraw and drop the link animations (`CPartArray::HandleEnterWorld`, which is
`RemoveLinkAnimations`). A change of any other bit never touches the rule. While Hidden, the animation and the physics
stop but the interpolation still moves the object, and its parts are not drawn.

**The census** (RequiresDats, input to owner decision D45). The human's table 0x34000004 maps UnHide to 0x3300032F and
Hidden to 0x33000331. Hidden starts 14 particle emitters and fades the object with one Transparent hook; UnHide stops the
14 emitters and fades it back. The 14 emitters are the bubbles round a hidden player, so a Hidden object's particles must
be drawn; the fades are whole-object translucency, so drawing them needs per-instance translucency in the object shaders.

## 11. The player (AV10)

The player's own CreateObject makes an avatar like any other (appearance, clothing, colours, children, Hidden and its
scripts, draw output) and a `PlayerAvatar`. Its position and animation are the player body's (`IPlayerBody`, the physics
track's player object):

- the avatar makes no interpreter of its own; the body builds its `MotionInterpreter` on `PlayerAvatar.Parts` (the part
  array made from the player's motion table), steps it in its own update and forwards each update's hooks with
  `PlayerAvatar.QueueHooks`;
- once a body is bound (`AvatarSet.PlayerBody`), every frame takes the body's position and contact, draws the pose of the
  body's part array, serves the voyeurs and runs the effects and the children;
- the player's own positions (every verdict but Remote) are the body's (physics' `ServerPositionPolicy`); until a body is
  bound the set places the player at each one, so the player shows before physics lands;
- the player's server movements (a non-autonomous UpdateMotion: an emote, an action, or the MoveTo the server sends after
  an out-of-range Use) go to an `IPlayerMovementBody` (a body with its own `MovementManager`); without one they are counted
  and dropped; the autonomous echo of the player's own input is ignored, as retail does;
- a target id that names the player finds the body when it is an `ITargetable`, so the creatures that chase the player
  and the player's own MoveTo reports reach the right target manager.

The player always draws LOD level 0 (`CPhysicsPart::UpdateViewerDistance`'s player check). `FirstPerson` hides the body
and keeps its held items (§7).

**Online, in the game (M2 G6 W9, `--play`).** `scripts/Player/PlayerRig.cs` is the offline setup's body, shared: `PlayMode` binds the player's rig to the player's avatar in the session's one avatar set (the
`SessionAvatarAdapter` created it from the player's own CreateObject and applies every event for it, section 17) with the session's movement channel as the walker's sink in place of the dry-run one, and
forwards the player's positions to `PlayerMovementController.Apply` (docs/session.md section 21). The walker's weenie is the session's `QualitiesWeenie` (slice b4, docs/session.md section 26), and a server MoveTo for the player first loses control to the server (`PhysicsPlayerBody.ServerControl`).

**Offline, in the game (M2 G5, `--offline-player`)**, built by `scripts/Player/OfflinePlayerSetup.cs` the way a logged-in player is:
the stand-in's look (`Client/Play/OfflinePlayerLook`: an Aluvian man in the first garments CharGen lists, a placeholder, labelled
on screen) through `CharGenAppearance.Compose`; `AvatarSet.Create` of an `AvatarCreate` with `IsSelf` (`OfflinePlayerLook.Describe`);
`OfflineWalker.Create(parts: player.Parts, id: guid)`, so the physics object hosts its interpreter on the part array the avatar
draws and `PhysicsObj.UpdateObject` makes the §5.3 calls itself; `walker.Physics.Events = PlayerPhysicsEvents` (the hooks of each
step reach `PlayerAvatar.QueueHooks`); `AvatarSet.PlayerBody = walker.Body`; `Physics.TargetWorld = avatars`. Each frame
(`PlayerController`, process priority -20): the collision streamer's focus and pump on the main-thread budget, the controller's
`Tick`, `AvatarSet.Advance`, `PlayerView.Update`. `PlayerView` draws one `DynamicObjectBatch` (a MultiMesh of one instance) per part,
with the meshes of an `ActorMeshLibrary` per lighting rule keyed by the part's `PartAppearance.VariantKey`; both rules' meshes are
built before the first frame so their shaders compile at start-up. The character is hidden while the walker waits for its landblock's
collision (`PhysicsObj.PlacementPending`), and in a cell while the interiors do not show that cell (`InteriorController.IsCellShown`).
Draw positions are the 30 Hz physics positions interpolated between steps (D26, `Client/Play/DrawInterpolation`; `--motion-stepping=retail`
restores stepping): the body's frame is the step before the last plus the time since the last step, `PlayerView.Update` moves each part
with the body (`DrawInterpolation.Offset`), the poses stay retail's keyframes, and the chase camera follows the same drawn frame.

**Walking (M2 G5, the second stage).** `PlayerController` is also the player's input. Retail's keys come through the retail UI's input
router (`PlayerInput`: the router of an element runtime with no elements, fed by the same Godot feed as the retail UI, or the retail UI's
own router with `--retail-ui`) with the controller registered for movement map 4 and camera map 5 (map 6 while the alternate camera mode is held).
`MovementBindings` (`Client/Play`) turns map 4's actions into `IPlayerMovementIntents` calls as `ACCmdInterp::OnAction` does (docs/physics.md §12):
W/Up and X/Down walk forward and back, A/D (and the arrows) turn, Z/C (and Alt+A/D) strafe, S stops, Q is autorun (idempotent here), Space is
`CommenceJump` on the key down and `DoJump` on the key up, Left Shift is `SetHoldRun`, and the four emote actions send the motion of their name. The walker's
`PlayerMovementController` sends its MoveToState, AutonomousPosition and Jump to a `DryRunMovementSink`, which counts and logs them and sends
nothing. The player runs by default and Shift walks. Online, the character's ToggleRun option is read when the rig is built (`PlayMode.RunByDefaultOf`, `PlayerRigRequest.RunByDefault`; a later toggle does not reach the rig); the offline player has no description and always runs by default (`TODO(g5-run-by-default)`, logged only while the request stays true). `--retail-run-rate-cap`
sets `MotionInterpreter.RetailNegativeRunRateCap` on the walker's interpreter. Teleports (the teleport panel, F7, the interior tour) place the player
through `PlayerMovementController.Apply` with a `SelfTeleport` verdict, the code a server teleport takes. A teleport arrival constrains the player to the spot
(`CPhysicsObj::ConstrainTo`), which retail's server keeps moving by echoing every accepted position; offline nothing echoes, so `PlayerController.Teleport`
then calls `PhysicsObj.UnConstrain`, or a walk of more than 10 m (5 m indoors) from the spot would be slowed and one of 50 m (20 m) stopped (`WalkChecksTests` measures the
slow-down with and without the call, docs/physics.md §15).

`KinematicPlayerBody` is a placeholder body: no collision, no gravity, the jump's velocity recorded but not integrated. It
hosts the interpreter and a movement manager on the player avatar's part array, steps it with retail's object clock,
moves by the root motion (zeroed off walkable ground) and the position managers, and takes contact from whoever drives it.
It is the test double of the physics track's player object and a stand-in for offline tools until that object exists.

## 12. Draw output and the stress crowd (AV11)

### 12.1 Advance and the budget

`AvatarSet.Advance(now, viewer, deadline)` steps the player first, then the other avatars round-robin from where the last
call stopped, checking the deadline (`Stopwatch` ticks) every eighth avatar as `LivingStaticSet` does. An avatar skipped
by the budget catches up on its next turn: its object clock steps the whole gap, up to 2 s. Then every avatar in a cell
is drawn from its latest pose, and the effect events go to an `IEffectEventSink` with the object's distance and bearing
from the viewer, or are counted as dropped without one.

### 12.2 The frame

`AvatarFrame` holds reused buffers, valid until the next advance:

- `Avatars`: one `AvatarDraw` per avatar in a cell: the guid, the cell (EnvCell or not, for the interiors), the anchor
  cell and the frame in its landblock space, `AppearanceVersion` (rebuild the meshes and materials when it changes),
  `PoseVersion` (rewrite the transforms only when it changes), whether it is drawn, the player flag, the lights flag, the
  object's translucency, and where its parts and particles are;
- `Parts`: the effects track's `PartDraw` rows (GfxObj of the selected LOD level per `DrawFrameRules`, frame, scale,
  degrade mode), the GfxObj 0 for a part that draws nothing;
- `PartTranslucency`: each part's own (a TransparentPart hook's value, else the object's), for `PartTranslucencyRules`;
- `Particles`: the live particles, in each avatar's anchor landblock space.

`PoseVersion` changes when the drawn keyframe changes (the retail floor sample returns the keyframe's own list, so a
reference comparison suffices) or the frame moves.

**Allocation.** What is measured (`AvatarAllocationTests`, `MotionAllocationTests`, `SequenceAllocationTests`):

- An advance that queues no motion allocates nothing once warm: 60 steadily walking humans and 200 idle objects,
  synthetic and from the dats, allocate 0 bytes per advance (1.8 ms in a Debug build for the dats crowd of 8,840 parts).
- The sequence reuses the animation nodes it removes, so the links and cycles a motion change swaps allocate nothing
  once warm.
- Every motion queued in a motion table still allocates in M1's motion-table code: `MotionTableManager`'s pending entry
  (72 B), the boxed enumerator of `MotionTableLogic.AddMotion`'s `foreach` per motion data it appends (32 B), and
  `ReModify`'s copy of the state while modifiers are on. So a server movement (`AvatarSet.Apply(ObjectMotionChanged)`)
  allocates, and so does the MoveTo crowd of `--avatar-stress`, whose MoveTos and steering queue motions in its ticks
  and its advances: the crowd of 60 measures about 94 B per queued motion, about 95 B per frame. The tests bound the
  bytes per queued motion (`MotionPlayer.MotionsQueued`) at 200 B; an allocation-free queue is requested from the lead,
  and the tests then pin 0 bytes.
- A body's first link or action can still grow a list once (`EffectObject`'s hook lists), so "once warm" means once
  every avatar has done what it does.

`EffectObject.CollectParticles` enumerates its emitters through an interface, which allocates, so it is called only for
an object with emitters (requested from the lead).

### 12.3 The stress crowd

`AvatarStress` is Lifestone's own tool, for the allocation and frame-time tests (wave 2's `--avatar-stress=N` was dropped by the owner and is not registered):
`RandomLooks` draws seeded CharGen looks, `Human` and `Object` make templates, and `Generate(n, seed, centre, radius,
templates)` scatters creates with guids from 0x7E000001. `AvatarStressDriver` sends each walker a MoveToPosition (type 7,
walk or run past an 8 m threshold) to a random point every 3-9 s, which retail's MoveTo turns into turns and walks. The
driver allocates nothing of its own; the motions its MoveTos queue do (§12.2).

## 13. CharGen's 3D view (AV12)

`CreaturePreview` is `gmCG3DView`, engine-agnostic. `PreviewAnimations.Resolve(maps, heritage)` reads the UIASSET map:
for heritage 12 the Olthoi animation (also the rest) and its twitch as every idle emote; 13 the acid Olthoi's; otherwise
the CharGen animation 0x03000001, the rest 0x030003C0 and the five idle emotes 0x030000AC, 0x03000540, 0x03000508,
0x03000523 and 0x0300052E.

- **The object** is `CPhysicsObj::makeObject(setup)`: the parts, the resting placement, the default script, and the
  Setup's default motion table in its default state. It is an examination object: every part draws level 0 and degrades
  are off. `SetAppearance` makes a new object only when the Setup changes (it then plays its default state until the page
  starts or stops the animation again) and applies the ObjDesc from the default; problems are loud, never fatal (retail
  ignores them).
- **Animation.** `StartAnimation` clears the sequence and plays the animation from frame 0 at 30 fps; `StopAnimation`
  holds frame 0 of the rest. `Advance` is `CPhysicsObj::update_position`, which `CreatureMode::Render` calls: nothing
  until 1/30 s has passed, then one step of the whole elapsed time, none beyond 2 s; no root motion.
- **The view.** Heading 180° on the CharGen pages; one distant light of intensity 2 towards (0.3, 1.9, 0.65) (portal space's
  points the other way in Y); the heritage's environment Setup behind the character (`BackgroundSetupId`).
- **The idle emotes are never played.** Retail loads them into `m_didAnimArray`, and `StartAnimation` stores the time in
  `m_dLastAnimTime`, but no 11.4186 code reads either; character management has no 3D view (owner decision D35), so no
  appearance store exists. This conflicts with the wording of DoD 3 in [m2-log-in.md](milestones/m2-log-in.md) ("with
  its idle emotes"); it is raised with the lead and the owner before G3: either DoD 3 drops the emotes, citing
  `gmCG3DView`, or the view plays `PreviewAnimations.IdleEmotes` behind an option as a documented deviation (with a
  cadence of Lifestone's own, since retail has none to copy) and retail's still view as the other setting. Until then
  `CreaturePreview` does what retail does.
  **Decided (D54, m2 §7 decision 12), then amended (D60, m2 §7 decision 14): retail's animation, no emote.** Character creation (retail-ui.md §20)
  does what `gmCG3DView` does. `StartAnimation` (0x004EE600 / 0x004EF290) clears the sequence and loops `PreviewAnimations.Animation` (the 33-frame 0x03000001, about
  3 cm of movement; the Olthoi's own for heritages 12 and 13) from frame 0 at 30 fps; `StopAnimation` (0x004EE640 / 0x004EF2D0) clears it and holds frame 0 of
  `PreviewAnimations.Rest` at 0 fps. The pages start it from the summary page's `InitializePage` and the appearance page's `ZoomOut` and `Update` when zoomed
  out, and stop it from `ZoomIn` and `Update` when zoomed in (the call sites and addresses are in retail-ui.md §20); `m_didAnimArray` (the emotes) is still only loaded. There is no seam.
  `CreaturePreview.StartAnimation(now)` also marks the object as updated up to `now`, so after a held rest pose the first step is the time since the
  start, as retail's every-frame `update_position` gives. For a review frame `Cg3DViewModel.PinAnimation` holds the loop at a chosen time (not in retail; the frames
  runner uses 0.5 s, frame 15).
- **Where it is drawn.** `Cg3DViewModel` (Client) composes `CharGenState.ToLook()` and poses a `CreaturePreview`; `CharGenViewportBinder` (Game) draws it in a
  `CreatureModeView` the size of the viewport element's hole, dressed per part through `ActorMeshLibrary.Get(gfxObj, PartAppearance)`; the light's sign is
  `TODO(retailui-chargen-light)` there.

## 14. MoveTo, sticky and targets (AV14)

### 14.1 Targets

`TargetManager` (CTargetting.c) is an object's own target and its voyeurs. `SetTarget(context, id, radius, quantum)`
drops the old target, records the new one (Undefined, stamped now) and joins the target's voyeurs, which reports to it at
once with the target's own position. Every half second of physics time, `HandleTargetting` checks each voyeur: the
target's position predicted `quantum` seconds ahead at its measured velocity is reported when it moved more than the
voyeur's radius from the last report; a target that has stayed Undefined for 10 s reports TimedOut to its owner. Events
(`NotifyVoyeurOfEvent`: ExitWorld when an object leaves the world) are reported to every voyeur; an ExitWorld report
clears the voyeur's target. `ITargetWorld` finds objects by id (the avatar set; the player's body for the player).

Note what the prediction does to a chase: as a chaser closes in, it shortens its quantum (`set_target_quantum`: the time
to get there) about as fast as a walking target moves, so the prediction stands still and no new report comes; the
chaser arrives at the last report. Retail's server sends a new MoveTo; the tests pin the behaviour.

### 14.2 MoveTo

`MoveToManager` (CMoveToManager.c) carries out types 6-9. A movement queues steps: turn to the target, move to it, and
turn to a final heading with `UseFinalHeading`. A MoveToObject or TurnToObject first asks the top-level object (its parent
when it is held) for reports (radius 0.5, quantum 0) and queues its steps on the first Ok; later reports retarget a
MoveToObject; a report that is not Ok cancels (0x37, or 0x38 before the first).

- **The command** (`MovementParameters.GetCommand`): towards, WalkForward beyond the distance to object; away, WalkForward
  (moving away) inside the minimum distance; both, WalkBackwards when within the distance and below the minimum. The hold
  key is Run when charging, or when running is allowed and either walking is not or the distance is more than the walk/run
  threshold beyond the distance to object.
- **Turning** issues TurnRight or TurnLeft (adjusted to TurnRight at a negative speed), waits while motions are pending,
  snaps to the heading once it passes it, and stops the turn.
- **Moving** issues the command at the parameters' speed, steers with an auxiliary turn whenever the heading is more than
  20° off (180 added moving away), and arrives within the distance to object (or past the minimum moving away), stopping
  both commands; the distance is the cylinder distance with `UseSpheres`. Going past the fail distance from the start
  cancels (0x3D). Every second the progress since the last check and since the start must reach 0.25 m/s, else the
  failure count rises (retail never reads it back).
- **Ending**: cleaned up (the commands stopped, the target cleared, the flags cleared, the distances kept) and stopped
  completely; with `Sticky`, stuck to the target. A new movement cancels the one under way (0x36); a teleport too (0x3C).
- **Time** is the host's frame clock (retail's `Timer::cur_time`).

### 14.3 Sticky

`StickyManager` (CStickyManager.c) keeps an object pressed against the object it stuck to: once the target has reported,
each update's offset becomes a horizontal step towards the target's cylinder, 0.3 m short of touching, at most 5 × the
interpreter's maximum speed (15 m/s without one) × the quantum, turning to face it. The stick lasts one second from
`StickTo` (the decompile never extends it) and ends on any report that is not Ok. `PositionManager.StickTo`, `UnStick`,
`GetStickyObjectId` and `HandleUpdateTarget` reach it; hosts pass `GetStickyObjectId()` as
`PositionContext.StickyObjectId`.

### 14.4 Where retail and ACE differ

ACE's MoveToManager (the test oracle, `AceMoveToOracle`) follows retail with server customisations. 120 seeded moves and
turns end in the same place facing the same way (within 0.35 m and 1°), and a chase of a moving target ends beside it in
both. The differences, each shown by its own test:

| | Retail (here) | ACE |
|---|---|---|
| `TurnToHeading` | begins the turn at once | waits for the next update |
| `MoveToPosition` with `UseFinalHeading` | the new parameters' flag | the previous movement's parameters (so ACE never turns to the final heading) |
| Hold key with `CanCharge` | Run | ignored |
| `CleanUp` | clears the flags, keeps the distances | keeps the flags, zeroes the distance to object |
| `cylinder_distance` with one cylinder above the other but within reach | the vertical gap | the reach |
| Waiting | `motions_pending` | `IsAnimating` (the same in the tests) |
| `UseTime` while a target has not reported | waits | runs (no steps are queued then, so nothing differs) |
| Arriving | within the distance | also within 1 m while the distance grows (no spheres), and snaps the heading when the steering turn stops |

## 15. Later steps

From [m2-plan.md](milestones/m2-plan.md) §3.3.

- **AV13**, the session adapter (`SessionAvatarAdapter`), is built (G5, §17): it subscribes to the bus and calls
  `AvatarSet.Apply` with `AvatarWire.FromWorldObject` for creates and updates; replay tests over `cache/sessions`.
- **Physics**: implement `IPlayerBody` (and `IPlayerMovementBody`, `ITargetable` and `IMoveToHost` for the player's MoveTo)
  on the player object, build its interpreter on `PlayerAvatar.Parts`, forward the hooks, and call
  `PlayerAvatar.Parts.HandleMovement()` each quantum after `MovementManager.UseTime()` (§5.3); supply `IGroundProbe` (PH8).
- **Lead**: make `MotionTableManager`'s pending queue and `MotionTableLogic`'s `AddMotion` and `ReModify`
  allocation-free (§12.2); the motion allocation tests then pin 0 bytes.

## 16. Tests

`tests/Lifestone.Data.Tests/Appearance/` (filter `~Appearance`):

- `ObjDescTests`: the `Add*` rules and the cap on synthetic lists, `TryUnpack` on hand-made and ACE-serialised bytes, every
  CharGen ObjDesc (2,404 records, 4,479 entries; Tier=Exhaustive), and the two part-swap readings.
- `PartArrayAppearanceTests`: the part, texture and palette rules on a synthetic source; every CharGen sex Setup with its
  base ObjDesc; ACE's naked human.
- `ClothingObjDescTests`: `BuildObjDesc`'s steps, shades and failures; the heritage fallback of every group, the Setup's own
  entry winning, and the Setups in no group; the CharGen Setups that need the fallback and those it leaves unclothed.
- `CharGenAppearanceTests`: composition order, bald eyes, alternate Setups, invalid choices; every choice of every
  heritage and sex composed and applied (2,932 looks, none with a problem; Tier=Exhaustive); `HUMAN_SETUP_ID`; the preview
  against ACE's world look (§4.3).
- `AppearanceSweepTests` (Tier=Exhaustive): every ClothingTable on every Setup it lists; original palettes at both
  texture levels (needs `client_highres.dat`).
- `AppearanceSourceTests`: `GfxObjMesh.SurfaceIds`, `TextureLoader.GetOriginalPaletteId`, `DatAppearanceSource`.

`AceObjDescOracle` (test-only, ported from ACE, [third-party/avatars.md](third-party/avatars.md)) composes the ObjDesc
ACE would send for a newly created player and serialises it as ModelData.

`tests/Lifestone.Data.Tests/Motion/` (filter `~Motion`):

- `MotionStateBehaviourTests`: the states' `ApplyMotion`/`RemoveMotion` rules and `MovementParameters`' defaults.
- `WireMotionTests`: the unpack defaults, the 11.6096 ids past ordinal 0x10F, 15-bit stamps, unknown words in every field,
  `ToWire` and a 200-state round trip.
- `MotionInterpreterTests`: the interpreter on a recording part array, each rule's calls worked out by hand: the default
  state, the player walking, running, backwards, turning, sidestepping (the 1.248 scale and the ±3 cap), jumping and
  landing; falling without contact; standing long jumps; server actions across the 0x7FFF wrap and the autonomous rule;
  the 11.6096 jump set; the jump refusals; combat refusals; seven actions; stops; cells; speeds.
- `MotionInterpreterOracleTests`: 500 seeded scripts against `AceMotionInterpOracle` (test-only, ported from ACE), and one
  test per difference in §5.4; the movement system against ACE's.
- `MotionInterpreterPlaybackTests`: the interpreter on a real `MotionPlayer`, a synthetic table and the human table (with
  dats): queues in step, links, falling and landing, actions completing, root-motion speeds at the run rate.
- `PositionManagerTests`: interpolation (convergence at 2 × max speed, completion, stalls, failures, blips indoors and
  out, the queue, keep-heading, a landblock seam, the speed switch), the constraint (curve, accumulation, contact), the
  retail distances and `PositionFrames`.
- `MotionAllocationTests`: 0 bytes per offset adjustment and per server motion state once warm (on a recording part
  array); on a real `MotionPlayer`, the bytes per queued motion bounded (§12.2).

`tests/Lifestone.Data.Tests/Animation/MotionPlayerPartsTests` covers the part-array operations, and
`SequenceAllocationTests` the sequence's reuse of its nodes (0 bytes per link and cycle swap once warm).

Stage av3, `tests/Lifestone.Data.Tests/Motion/`:

- `MoveToManagerTests`: `heading_diff` and `heading_greater`, the command and hold-key rules, turning both ways (snapping
  to the heading), moving to a position walking and running, the final heading, the fail distance, following a moving
  object, a target that never reports, a sticky arrival, a new movement cancelling, the progress check. The mover is
  `MoverHost` (`MoveToTestDoubles.cs`): an interpreter on recording parts whose motions have no animations, so they
  complete in the part array's completion check (each tick's `HandleMovement`, or `PerformMovement`'s), and a kinematic
  body.
- `TargetAndStickyTests`: the first report, reports only past the radius every half second, the prediction, leaving the
  world, the 10 s timeout and id 0; the sticky step (the gap, the speed cap, the turn), its one-second life and its end on
  a report that is not Ok.
- `MovementManagerTests`: `unpack_movement` of every type, the stance rule, unknown words, the bodies' fallbacks.
- `MoveToOracleTests`: 120 seeded moves and turns and a chase against `AceMoveToOracle` (test-only, ported from ACE,
  [third-party/avatars.md](third-party/avatars.md)), and one test per difference in §14.4.

`tests/Lifestone.Client.Tests/Avatars/` (filter `~Avatars`; `AvatarRig` is a synthetic world: a 3-part human with a right
hand and spheres, a walking motion table, a sword and a lamp with a default animation and no spheres, and a script
table with empty Hidden and UnHide scripts):

- `AvatarBodyTests`: stepping like the spell viewer's actor, root motion scaled on walkable ground and zeroed off it, a
  landblock seam under one anchor, teleports, positions without contact, interpolation taking the server's cell, the 96 m
  rules, Static objects with and without a motion table, the ground probe, the pose version; an object without spheres
  that never moves, blips to a server position after its interpolation fails, and turns by its angular velocity only.
- `AvatarMotionCompletionTests` (with the dats, the human Setup and table): no motion pending a frame after a create, and
  none after a turn's links; a MoveToObject steering towards a target that steps sideways.
- `AvatarSetTests`: creates, failures, recreates and deletes, inventory items, a child before its parent, a missing
  holding location, remote and unknown motions, the player's echo, positions remote and self, ObjDesc changes, dropped
  effect events, target lookup, the budget's round-robin.
- `AvatarChildrenTests`: a wielded item following its hand through a walk, pickup, a dropped item, the parent's delete and
  recreate.
- `AvatarHiddenTests`: other bits play no hidden script; an object created Hidden is registered hidden and plays its
  Hidden script, and UnHide after it; freezing while interpolating, the children's NoDraw, repeated states and the
  player's notices; with the dats, the Hidden and UnHide scripts and their hooks (§10.2).
- `PlayerAvatarTests`: the kinematic body walking the player's avatar, the player's server movement and echo, level 0
  and first person, a server MoveTo for the player; with the dats, every DoD-7 state through the human motion table.
- `AvatarAllocationTests` (not parallel): 0 bytes per advance for 60 walkers and 200 idle objects, synthetic and from the
  dats; the MoveTo crowd's ticks and advances, bounded per queued motion (§12.2).
- `AvatarStressTests`: the seeded scatter, the driver's walks, and a timing report for 150 avatars.
- `CreaturePreviewTests`: the animations at 30 fps and the rest at 0 fps, `update_position`'s steps, level-0 draws and
  the light, appearance and Setup changes; with the dats, the UIASSET animations and a CharGen character animating.
- `AvatarWireTests`: ModelData, motion states and MoveTo bodies, and a `WorldObject` mapped field for field.

- `SessionAvatarAdapterTests`: AV13 on the reducer's real event stream and the synthetic world: queued in order and applied inside the budget (one event at
  least per pump), the player created hidden and seen unhiding, positions, motion, vectors, state, parenting, pickup, descriptions and deletes, an object deleted
  before the queue reaches it, a recreate, immediate mode, clear and dispose. `SessionAvatarAdapterAllocationTests`: 0 bytes for a standing object's positions,
  vectors and states through the queue once grown. `SessionAvatarAdapterReplayTests` (`[RequiresRecordingAndDatsFact]`, skipped without `cache/sessions`
  recordings or dats): every real recording replayed into an avatar set over the dats: when the player is present the set holds an avatar for every object the
  session holds (35 of 35 in a login to the Academy), none failed. `WorldContentCensusTests`: each kind, and the census of a world (the pack left out).
  `StandInVisitTests`: the visit's objects. `ServerEffectsTests` (M3 1.3 s0: a script played and its own hooks run; a missing type counted and logged once; a sound reaching a test sink with the body's table and volume; no sink counted; arrival before the create; an unknown avatar dropped; no allocation delivering) and `StandInServerEffectsTests`. In the engine: `--play-checks` (docs/session.md section 21): the objects reach the avatar set, are drawn when fully
  attached, the hidden player materialises, the objects leave with the log off, and no frame is over 50 ms from the arrival to the server's teleport.
- `ProjectileFlightTests` (M3 3.1 fc, fix B): a bolt created with a velocity moves by it each step and stops on the hit's vector change; no gravity unless the state says Gravity; the Launch and Explode scripts counted from the projectile's own table and a missing type counted once; the projectile window open while any projectile lives and closed by the F747; a projectile counted once when the layer first shows it; a sphereless Setup flying by default and retail's frozen origin as the flag; NoDraw hiding the bolt and the vector update alone stilling it; and, with the dats, every war-spell projectile Setup having a sphere, the Flame Bolt's own table playing Launch and Explode, and its part degrading to nothing. `StandInFightTests` (3.1 fb) covers the stand-in's side (`docs/session.md` section 16).

## 17. The server's objects on screen (G5: AV13, W6, W10)

What the game does with the session's objects, in the order they travel. The engine-agnostic parts are in `Lifestone.Client.Avatars`, the Godot parts in
`scripts/World/ServerObjects/`, and play mode (`scripts/Session/PlayMode.cs`) ties them together.

| Piece | Where | What it does |
|---|---|---|
| `SessionAvatarAdapter` (AV13) | `Client/Avatars/` | Subscribes to the bus (`ObjectCreated`, `ObjectUpdated`, `ObjectDeleted`, `ObjectMoved`, `ObjectMotionChanged`, `ObjectVectorChanged`, `ObjectStateChanged`, `ObjectParented`, `ObjectPickedUp`, `ObjectDescChanged`) and keeps the events **in order** in a struct queue (nothing allocated per event once grown). `Pump(deadline)` applies them to the `AvatarSet` inside the caller's time budget, at least one per call, so a CreateObject burst never costs one frame its whole cost; `Immediate` applies inside the dispatch (tests, replays). A create's `AvatarCreate` is built when the event is published (`AvatarWire.FromWorldObject`), so an object deleted before the queue reaches it is still created as it was. Counters: applied, ignored, unknown, failed, the slowest event, and the player's hidden create and unhides. F755 (`ObjectScriptPlayed`) plays the script type on the body's `EffectObject` through its PhysicsScriptTable (`AvatarSet.PlayScript`; `SmartBox::HandlePlayScriptType`, 11.6096 0x004520B0), counted in `AvatarStats.ServerScriptsPlayed`; a type the table lacks plays nothing (retail's `play_script` is a silent no-op) but is counted (`ServerScriptsMissing`) and logged once per table and type. F750 (`ObjectSoundPlayed`) becomes a `ServerSoundTableHook` (the body's `SoundTableId`, the sound type, the volume) queued on the body and handed to `AvatarSet.EffectEvents` by the next `Advance` with the body's other hook events (`AvatarSet.PlaySound`; `SmartBox::HandleSoundEvent`, 11.6096 0x00452000), counted in `ServerSoundsPlayed` (whether or not a sink exists: without one the event is counted in `EffectEventsDropped`). An effect that arrives before its object's CreateObject is parked by the reducer (retail parks it too) and published after the create; one for an avatar the set lacks is dropped and counted (`ServerEffectsDropped`), never a crash. `TODO(g5-w10-scripts)` is closed (M3 1.3 s0) and `TODO(g5-w10-sounds)` is closed (M3 1.3 s1). |
| `WorldContentCensus` (W10) | `Client/Avatars/` | What kind of thing each placed object is, from its weenie description: NPCs, creatures, portals, doors, lifestones, signs, corpses, items, others (`Classify`). A sign has no bit of its own, so it is a fixed (Stuck) object whose name has "sign": a heuristic, labelled, diagnostic only. `LoginCheckScenario` reports its total as `academy_objects`. |
| `ServerObjectMeshes` | `scripts/World/ServerObjects/` | The meshes of the objects' parts as their ObjDescs dress them, one per (GfxObj, `PartAppearance.VariantKey`, lighting rule); the same key as the player's `ActorMeshLibrary`, so identical dress shares a resource. **A worker prepares** (`MeshFactory.Prepare`, `PrepareSurface` with the dress's palette and texture swaps) and **the main-thread queue attaches** (`AcquireMesh`) inside its frame budget: asking never builds on the frame that asks. A mesh unasked for 600 frames is released. |
| `ServerObjectsLayer` | `scripts/World/ServerObjects/` | Process priority 12. Draws every avatar of the frame except the player's (which `PlayerView` draws, interpolated with the physics steps): one `DynamicObjectBatch` (a MultiMesh) per (mesh variant, layer), each instance a part with `INSTANCE_CUSTOM` = cell slot, light-set row, luminosity, translucency. **An object is shown only when every one of its parts' meshes is attached** (`Waiting` counts the others); once shown, a dress change keeps drawing the part meshes it had until the new ones arrive. Lit like every moving object: `SurfaceLighting.DynamicObject` outdoors; in an EnvCell `DynamicCellObject` with a row of the shared `ObjectLightTable` (the up to eight lights of the object's cell that reach it, D46, read per cell on a worker by `ServerObjectCellLights`; the table uploads itself before every draw, `ObjectLightTable`), drawn only while the interiors show that cell. A batch is uploaded only on a frame in which a hash of its instances changed, so idle people cost no upload. It does not advance the set: the player's controller does (`AvatarSet.Advance`: the player first, then the others round-robin), and play mode switches the layer on with the player and off at the log off. |
| `ServerObjectParticles` | `scripts/World/ServerObjects/` | `AvatarParticles`: the particles of every avatar with emitters, the Hidden player's bubbles included (an object that is Hidden is not drawn but its particles are), with the world's `ParticleRenderer`: one per (anchor landblock, in a cell or not), placed at the landblock's render origin, `CellObject` lighting with the cell's slot as each object's tag in a cell. A mesh not ready is queued and skipped for the frame. |
| `PlayMode` | `scripts/Session/` | Owns the session's **one** `AvatarSet` (made again at every log off) and the adapter that feeds it, including the player's own events. The player's rig (`PlayerRig.Build` with `Avatars:`) binds to the player's avatar in that set: the character, its clothing and colours are its own CreateObject's. Applies the adapter's events after the work it queued, and not in a frame whose work took over 8 ms. |

**MultiMesh batches, not one node per object, and why.** The per-instance values the lighting rules read (`INSTANCE_CUSTOM`: slot, light row, translucency) are
only a MultiMesh instance's; Godot's per-node instance uniforms would each reserve a block of the shared shader-uniform buffer (SurfaceLightingCode remarks). The
Academy's counts (35 objects, up to 34 parts each) would pass per node, but the shaders and the batch are the real ones, and Holtburg's crowd, with the same dress on
many bodies, shares batches.

**Hidden and materialising.** The server creates the player Hidden and clears the bit after LoginComplete. `AvatarSet` plays the Hidden script when it creates the
object (the 14 particle emitters of the human's script table: pink bubbles) and the UnHide script when the bit clears (`HiddenTransition`, docs/portal-space.md); the
adapter counts both (`PlayerCreatedHidden`, `PlayerUnhides`). `--login-check=enter`'s `materialised` is true when the player was created hidden, unhidden, its body
drew and the bubbles' emitters were handed to the particle renderer.

*The fade, measured (M2 decisions follow-up, slice f1; D45, D63).* Read from the user's dats and pinned in `AvatarHiddenTests`
(`The_materialise_fade_hooks_are_a_linear_ramp_from_1_to_0_over_0_75_s`):

| Script | Hooks | The Transparent hook (`CPhysicsObj::SetTranslucency2`, hook time in the script) |
|---|---|---|
| UnHide `0x3300032F` | 14 StopParticle, 1 Transparent | at 0 s: start 1, end 0, time 0.75 s |
| Hidden `0x33000331` | 14 CreateParticle, 1 Transparent | at 0 s: start 1, end 1, time 0 s (an immediate translucency of 1: invisible) |

Both scripts are 0 s long (every hook is at 0), so the fade is the hook's own 0.75 s. The curve is linear: `FPHook::Execute` computes
`(end - start) * s + start` with `s = min(1, (now - created) / time)`; there is no easing. Client side, `EffectObject` already does this (`TryAddFp`,
`ExecuteFpHook`, `Visual.Translucency = max(value, TranslucencyOriginal)`, the clamp of `CPhysicsObj::SetTranslucencyInternal`):
- **Timing.** The hook is queued in the first update after the bit clears (the script runs inside that update, `Update` runs scripts last) and is applied
  from the next one, so the first frame after the bit clears is still translucency 1 (no pop-in) and the ramp starts at that update's clock. It is a function
  of the clock, not of frames: at 30, 60 and 240 fps the same instants give the same values, up to the one frame's delay in starting (`AvatarMaterialiseTests`).
- **End state.** Exactly 0 once 0.75 s have passed, and then constant; a base translucency from the CreateObject (`AvatarCreate.Translucency`, `TranslucencyOriginal`)
  is a floor: the fade ends there.
- **What the Godot layer reads.** `AvatarDraw.Translucency` (the object's, `max(Visual.Translucency, TranslucencyOriginal)`) and `AvatarFrame.PartTranslucency`
  (one per part, parallel to `AvatarFrame.Parts`: a TransparentPart hook's value, else the object's). The player is one of the frame's avatars
  (`AvatarDraw.IsPlayer`) and `AvatarFrame.Add` computes both the same way; a first-person player is `Visible == false` whatever its translucency says.
  `PartTranslucencyRules` says how a value draws: 1 not at all, 0 on the ordinary opaque path, anything between a blended part material of alpha 1 - t.
- **Re-hide mid-fade.** `set_hidden` plays the Hidden script, whose hook sets translucency 1 at once, and stops drawing the object (`Visible` false); the
  UnHide hook, still running, is not cancelled. Retail's FP hooks run newest first (`SetTranslucency2` puts the new hook at the list head; `process_hooks`
  walks from the head), so the older hook is applied last and wins while it lives: hidden, the object's translucency keeps ramping toward 0 unseen. If it is
  unhidden again before that hook ends, the old fade finishes first and the new one then continues from where its own clock is (a visible step back);
  if after, the first frame would be translucency 0 (opaque) for one frame, then the new fade starts at 1. `EffectObject` reproduces this. It needs a hide and
  a second unhide within 0.75 s, which the server does not do on login.
- **The one opaque frame, fixed by default (slice f2; `--retail-unhide-flash` restores it).** Under the glitch-fix policy the first frame drawn after an unhide
  holds translucency 1 until the new fade's hook applies. `AvatarBody.ApplyState` notes the unhide (`EffectObject.TranslucencyFadesQueued`, a counter of whole-object
  fades queued, is the only change to the shared effects layer: the hooks run exactly as before), and `AvatarFrame.Add` draws the first frame after it with
  translucency 1 when the unhide queued a fade in that update (`AvatarBody.ConsumeUnhideHold`; also while the body has not been updated since the unhide, which a
  frame budget can cause). An unhide that plays no fade (an object with no script table) is not held and shows at once. For an ordinary unhide the held value
  is the one the Hidden script left, so default and retail draw the same frames. `AvatarSet.RetailUnhideFlash` (`PlayOptions.RetailUnhideFlash`,
  `--retail-unhide-flash`) draws retail's frame; `AvatarMaterialiseTests` pins both, and that the fix changes nothing for an ordinary unhide.

**How the fade is drawn (slice f2; closes `TODO(g5-translucency)` for the materialise fade, D45 and D63).** An opaque surface has no blend to scale, so each mesh
that can fade has a **blended twin**, a second `ArrayMesh` of the same geometry whose materials use `PartTranslucencyRules.WithPartMaterial(variant)`: SRCALPHA /
INVSRCALPHA, no alpha test, **no depth write**, retail's rule for a part with a translucency material (`D3DPolyRender::SetSurface`, 11.6096; surfaces that already
blend without a test keep their variant, ONE/ONE ones ignore the alpha, as in retail). The twin's shader is the ordinary dynamic-lighting one: `SurfaceShaders`
applies `v_instance_alpha = 1 - INSTANCE_CUSTOM.w` to every blended dynamic variant, so the twin honours the per-instance alpha with no shader change.
- **Choice of depth and sort.** Depth writes off and Godot's own transparent-pass order (back to front by each batch's bounds, within a batch in instance order),
  because that is what retail draws a translucent part with: a part with a translucency material is a blended, depth-write-off draw, so the limbs of a fading
  character blend over one another and over the ground instead of hiding what is behind them. Nothing is sorted per triangle (retail does not either). The
  character looks like a ghost through the fade, which is what retail shows; it is not a depth-correct body at half alpha.
- **Hand-over.** `ServerObjectsLayer.DrawObject` puts a part with `0 < t < 1` (`PartTranslucencyRules.HasPartMaterial`) in the twin's batch and any other part in the
  opaque mesh's. `PlayerView` has a second batch per part (`PartFadeNN`) for the same. Every batch is refilled and uploaded in the same frame, so a part is in exactly
  one of the two in every frame: no gap frame, no double draw. At t = 0 the part is back on the opaque path (alpha 1, depth writing), and at t = 1 it is not drawn.
  A fading instance changes every frame, so its batch uploads every frame for the 0.75 s (the layer hashes the instances; that is the cost of a changing value).
- **Where the twins are built.** `ServerObjectMeshes` builds the twin in the same main-thread attach as the mesh (`FadeOf(mesh)`); the player's `ActorMeshLibrary` (made
  with `fadeTwins: true`) queues each twin when it builds the mesh and builds four a frame afterwards (`PumpFades`), or at once if a fade needs one first, so the arrival's
  frame that builds the player's 34 parts does not carry both. The twin is held under the mesh's key plus `:fade` in the shared mesh cache (`RenderResources.FadeTwin`).
- **Known limit: one twin MultiMesh per mesh.** The twin batch of a mesh holds every fading object that uses that mesh, in instance order, and nothing sorts the instances by depth, so
  two objects (or an object and the player) fading at the same time with the same mesh can blend over one another in the wrong order for those 0.75 s. Harmless at login (one object
  fades at a time); not fixed. The twins of server objects are built eagerly for every mesh, which doubles that attach step and the mesh's GPU memory (`TODO(serverobjects-lazy-twins)`,
  `ServerObjectMeshes.Attach`, with the reason it was left). The fade checks (`--play-checks`, `--login-check`) count the parts drawn through the twin on the fading frames
  (`PlayerView.TwinParts`, `ServerObjectsLayer.TwinParts`) and fail on a `TODO(g5-translucency-twin)` warning, which logs when a fading part had no twin and was drawn solid.
- **Warmed at start-up.** `ServerObjectsWarmUp` compiles and draws the twin of every reachable variant under both dynamic rules with the plain ones, so the first fade
  frame compiles nothing (`TODO(arrival-pipelines)` for the twins is closed; the play checks log no pipeline compilation in the fade window). A cold compile at the arrival is a failure.
- **A hidden object's meshes are prepared early.** `ServerObjectsLayer` asks for the meshes of an object whose Hidden bit is set (`Prefetch`), so the fade is not spent
  waiting for the workers; nothing is drawn.
- **What is checked.** `--play-checks` (stand-in): the player and `Stand-in Apparition` (an object the stand-in server creates hidden with the human's script table and
  unhides with the player, `StandInObject.UnhideAfter`) pass through intermediate alpha for at least five frames, never rise, are never drawn opaque before the fade, end
  opaque, take 750 ms within 100 ms plus a frame, and no frame in the window is over 50 ms; with `--play-frames` the frame `02a-materialising` (half way, both fading) is
  saved to `extra/` next to the shell's frames (gitignored; it shows both as see-through figures with the Hidden script's bubbles). The login check reports `fade_ms=` (from the
  first visible frame after the unhide to the first opaque one, 650 to 900 ms accepted) and saves `02a-materialising` beside its other frames (docs/session.md section 27).
- **Still open:** the first-person fade (the camera within 0.45 m to 0.2 m of the player) keeps its own `TODO(g5-first-person-fade)` for M3: the camera's value is not an
  effect on the player's avatar, so the avatar set has nowhere to take it.

**Gaps, each a logged TODO:** `g5-first-person-fade` (above; `g5-translucency` is closed for the materialise fade), `g5-object-lights` (an object whose lights are on, `LightingOn` or the Setup's own lights, lights nothing
yet; only the cell's static lights reach other objects, D46), `g5-w10-scripts` and `g5-w10-sounds` (the scripts and sounds the server plays on an object: both closed in M3 1.3, s0 and s1; see the AV13 row above).

**Limits added in wave 3 (lane C).** `AvatarSet.MaxNotices` (1024) caps the notice list: the oldest are dropped with a warning and counted in `Stats.NoticesDropped`. `Stats.SpherelessMotionObjects` counts the objects that have a motion table and no spheres (retail's sphereless branch keeps their origin, so they will not move when the server moves them); each Setup is named once as `TODO(avatars)` (the census line, `WorldContentCensus`, should print the count). A new object's Hidden change is read against `HiddenTransition.ConstructedState`, the state `CPhysicsObj::CPhysicsObj` gives a fresh object (`AvatarBody.SetCreationState`, the rule of `ClassifyCreation`). `ServerObjectParticles` frees a landblock's renderer after 600 idle frames. `PlayerView` skips the player at translucency 1 (and its parts at 1), like `ServerObjectsLayer`; the fade itself is drawn by the blended twins (slice f2, above).

**The frame budget (DoD 15): what was measured and what was done.** Between Enter and walking the first live login saw frames of 108 to 136 ms. Attributed
(Godot's process time and slices by process priority), they were: the player's avatar create (25 ms, cold), `PlayerRig.Build` (26 to 41 ms, cold), and the world's start
and the tunnel's first frame in the same frame as the burst of CreateObjects. Now: `PlayWarmUp` pays the first-use costs at start-up (a scratch human through the avatar
code, a physics body on the human Setup, CharGen's composition, a hidden creature unhidden, walking, moved, redressed and deleted; the shared `PhysicsGeometryLibrary`
keeps the human's collision shape); play mode runs the work an event asks for in the next frame (never in the session tick's frame), builds the player in one frame (13 ms),
starts the world in the next, sets up the keys in a third, and spreads the objects' creates over frames inside a 3 ms budget. `--login-check=enter` measures 35 to 39 ms
at most, and judges the rule; `--play-checks` measures 29 to 42 ms at most from the arrival to the server's teleport and judges it (the teleport's own frames, 140 to 150 ms now and then when the dungeon's first cells attach, are reported with `TODO(g6-teleport-frame-time)`, G6 W11's). Not counted by the login check: the first frames after the
retail character list appears (it presses Enter in the very frame that screen is built, the retail UI's 43 to 59 ms; a person presses it later). A character whose Setup
is not the male human's still reads its own parts from the dats the first time (a smaller cost than the code's first run, which the warm-up pays).

**Sound and server effects reach a sink (M3 1.3 s1).**

- F750 and the avatars' hooks now reach a sink in play mode (closes `TODO(g5-w10-sounds)`); `IEffectEventSink.OnEffectEvent` gains the body's sound table (`AvatarBody.SoundTableId`: the CreateObject's STable, else the Setup's default).
- The sound origin is `SoundOrigin.Between(viewer, body)`. The viewer is the player's position passed to `Advance`, not the camera: retail's listener is the camera (`SmartBox::set_viewer` -> `SoundManager::SetPlayerPosition`). For the player's own footsteps the distance is 0 either way; other avatars' pans differ from retail by the camera offset in third person. Open: `TODO(avatars-listener-camera)`, to pass the chase camera's frame as the viewer for sound only (logged once).
- `AvatarSet.ScriptTableSeen(tableId)` fires when a body with a PhysicsScriptTable is made; play mode uses it to queue the table's scripts' particle meshes (`ServerObjectParticles.PrepareScriptTable`: a worker walks the table and resolves the particle models, the main thread queues `ParticleMeshLibrary.Request`), so an F755 script shows on the frame it lands. `AvatarSet.PlayerSoundTableSeen(tableId)` fires for the player's body (the sound prefetch, docs/portal-space.md section 11.3).
- The avatars' sound origin used to measure the listener-to-source bearing and so mirrored every positioned sound; it now calls `SoundOrigin.Between` (docs/portal-space.md section 7.2), pinned by `AvatarSoundOriginTests`.

**The cast on the player (M3 2.1 s2b, fix A).**

- The player's cast arrives as ACE's UpdateMotion 0xF74C per wind-up and for the talisman's gesture (stance and style Magic, speed 2.0, `Player_Magic.cs` 598-610, 977-990, `EnqueueMotionMagic`), not autonomous, so `AvatarSet.SetMovement` hands it to `IPlayerMovementBody.ApplyServerMovement` (`CPhysics::SetObjectMovement`, 11.4186 0x00509690, 11.6096 0x0050A160). Nothing needed a fix: `PlayerCastMotionTests` (dats; the physics player body of `OfflineWalker` with the avatar's hook forwarding) prove that the stance goes to Magic with the first motion, that the wind-up action plays (its hand particles and sound hooks reach the avatar's effects), that the talisman's gesture is the substate at forward speed 2.0, that Ready follows, and that a held item stays parented and follows the hand part through the whole cast.
- *The cast window.* `AvatarSet.OpenCastWindow()` and `CloseCastWindow()`, `CastWindowOpen`, and `CastTarget` (host-set; 0 means the player only). The `SessionAvatarAdapter` opens the window from the bus: on the player's own HearSpeech (`ChatReceived`, kind Speech, chat type Spellcasting 0x11, sender the player), or on the player's first non-autonomous Interpreted UpdateMotion in the Magic stance whose forward command is not the stance's Ready (or that carries commands). It closes it on `UseDone` (any code), queued in order with the F755 and F750 events around it. The close takes effect after the next `Advance` has delivered the frame's effects, so a voice the cast's last message started still counts. The adapter's counters are `CastWindowsOpened` and `CastWindowsClosed`.
- *The counters.* `AvatarStats.CastWindows`; `CastScripts` (F755 played from the body's table on the player, or on `CastTarget`, inside a window); `CastSounds` (voices that started inside a window from the player's body or `CastTarget`'s: the delta of `ISoundVoiceCounter.VoicesStarted`, which `SoundHookRouter` implements as `HooksSounded + ServerSounded`; 0 when the effect sink counts no voices). No per-frame allocation (`CastWindowTests` pins 0 bytes). A voice is counted in the frame its event is delivered, so a hook that fires in a window frame counts whenever the cast started it.
- *Where it is read.* The play-checks judge `cast_scripts>=1 cast_sounds>=1` (offline, Revitalize Self VI: `windups=1 gestures=3/3 cast_scripts=1 cast_sounds=6 cast_windows=1`; `docs/session.md` section 16), and the buff leg's `cast_scripts= cast_sounds= cast_windows=` are the deltas of these counters around the leg's scenario (`docs/session.md` section 30). The cast path of the adapter (`OnChat`, `OnUseDone`, `IsCastGesture` through `OnMotion`, `ApplyCast`) is in the JIT warm-up (fix A, review 2.1-R4; `docs/session.md` section 27).


**The projectile (M3 3.1 fc, fix B).**

- *Flight.* A CreateObject in the Missile state (0x40) makes the body a projectile (`AvatarBody.IsProjectile`, sticky: the hit's state keeps the bit). It flies on its own velocity from the create and the vector updates through `UpdatePhysicsInternal` (11.6096 0x005111D0), with no collision transition (D24): it flies until the server's hit messages. ACE's order at a hit (`SpellProjectile.ProjectileImpact`, `SpellProjectile.cs` 209-248): SetState (Ethereal, IgnoreCollisions, NoDraw, lights off), F755 Explode, a zero VectorUpdate, and F747 five seconds later. NoDraw hides the body (`IsDrawn`); the zero vector stills it. The stand-in sends the same order (`docs/session.md` section 16).
- *Gravity.* `calc_acceleration` (11.6096 0x00511420): the acceleration is (0, 0, -9.8) only when the state has Gravity, else 0, recomputed on a Gravity change. A bolt has none.
- *Sphereless Setups (glitch fix, default on; flag `--retail-sphereless-missile`).* Retail's `CPhysicsObj::UpdateObjectInternal` (11.6096 0x005161B0, 11.4186 0x005156B0) keeps the origin of any object whose `CPartArray::GetNumSphere` (11.6096 0x00518B90, 11.4186 0x00518060) is 0, a Missile included, so such a bolt never flies. All nine war-spell projectile Setups have exactly one sphere (pinned in `ProjectileFlightTests`), so no retail bolt meets the branch. Default: a sphereless projectile flies anyway (a fix for placeholders and unknown Setups; named once per Setup in a glitch-fix notice, no TODO). `AvatarSet.RetailSpherelessMissile` restores retail; the option `--retail-sphereless-missile` (`OptionCatalog.Play.cs`, `PlayOptions.RetailSphereless`, set where `PlayMode` sets `RetailUnhideFlash`) is registered, and `TODO(m3-sphereless-missile)` is closed.
- *A bolt may draw nothing.* The Flame Bolt's one part (GfxObj 0x010001EC) has a degrade table whose second level is "nothing" with a maximum degrade distance of 0, so the part never draws; the bolt you see is the particle trail of its Launch script (table 0x34000005). The layer therefore counts a projectile as shown when it is shown (every part mesh ready), not when a part has a mesh (`ServerObjectsLayer`; `IsDrawn` is true for it). Review 3.1-R6 renamed the counter `ProjectilesDrawn` to `ProjectilesShown` (it was named for what it does not prove); a shown Flame Bolt is not checked for its particles, because `ServerObjectParticles` hands the draw list per landblock group, not per object.
- *Scripts.* The Setup's default script plays at create (Lightning Bolt 0x33000104 and the rings 0x330008C3 have one; confirmed). Launch and Explode (F755) play from the projectile's own PhysicsScriptTable (the create's PES: ACE sends the weenie's `PhysicsTableId`), counted in `ProjectileLaunches` and `ProjectileExplodes` when received for a projectile body, and in `ProjectileScriptsMissing` (and `ServerScriptsMissing`, logged once per table and type) when the table lacks the type. The trail emitters are the ordinary avatar particles (`ServerObjectParticles`).
- *Counters and window.* `AvatarStats.ProjectilesCreated`, `ProjectilesShown` (once per projectile, `AvatarSet.NoteDrawn` from the layer), `ProjectileLaunches`, `ProjectileExplodes`, `ProjectileScriptsMissing` and `ProjectileWindows`; `AvatarSet.ProjectileWindowOpen` (any projectile between its create and its F747, ACE's five seconds after the hit included), `LiveProjectiles` and `LastProjectileGuid`. The layer reports `ProjectilesShown`, `LastProjectileWaitFrames` and `MaxProjectileWaitFrames` and logs one `projectile shown ... waited_frames= warm=` line per bolt. The fight leg reads these (`docs/session.md` section 31: `projectiles`, `fight_frame_max_ms`, the review frames' triggers); recreating a live projectile closes and reopens the window (a new window is counted).
- *The warm-up.* `ProjectileWarmUp` (started by `ServerObjectsLayer`'s constructor, polled every frame): a worker reads the ACE extract's 13 war-spell projectiles (9 Setups, 7 tables), collects every level of every part's degrade chain (8 GfxObjs) and the default scripts; the main thread then asks `ServerObjectMeshes.Warm` for each under both lighting rules (16 pinned meshes, with their blended twins; `Sweep` never releases them) and queues the particle meshes of the tables and default scripts (`ServerObjectParticles.PrepareScriptTable`, `PrepareScripts`). `PlayWarmUp.WarmProjectiles` creates a scratch bolt per Setup in the scratch avatar set (create, Launch, flight, hit, Explode, delete) so the Setups, scripts and emitters are read and the avatar code is jitted before play. Log lines: `projectile paths warmed ... setups=9 ms=~6` and `projectile warm-up done ... setups=9 tables=7 gfxobjs=8 meshes=16 ms=~375` (worker start to last attach). The done line waits, up to the existing patience, for the particle models of the tables and default scripts to be resolved and queued (`ServerObjectParticles.PrepareBacklog`, review 3.1-R9) and logs `particle_models_queued` and `particle_backlog`; the particle library's own worker builds of those meshes still run after it. If the plan cannot be read, `TODO(m3-bolt-warm-up)` is logged and a bolt's meshes are prepared when it first appears. Waiting for part meshes offline at 800x600: a Shock Wave I (four parts) waited 1 frame without the warm-up and 0 with it; the Flame Bolt waits 0 either way (nothing to attach).
- *Limit.* A projectile keeps its creation cell while it flies (`TODO(m3-bolt-cell-tracking)`, logged once for a bolt created in a dungeon cell).
- *Where it is checked.* `a-stand-in-flame-bolt-is-shown-and-flies` (play-check; judged: `projectiles_created>=1`, `projectiles_shown>=1`, moved, the window opened and closed; report-only: `wait_frames`, launches, explodes, pipelines) and the report-only line `projectile with meshes` (Shock Wave I); `docs/session.md` section 31.

---

**The player's death on screen (M3 3.2 dd; no avatar fix was needed).** The player's Dead motion (the server's F74C, stance NonCombat, forward Dead) reaches the player's body through `PhysicsPlayerBody.ApplyServerMovement` (`CPhysics::SetObjectMovement`, 11.6096 0x0050A160) and plays: the player avatar's motion state reads `Substate = Dead (0x40000011)` within a frame, and the Ready of the respawn (`SetCombatMode(NonCombat)`, `HandleSwitchToPeaceMode`) stands it up (`Substate = Ready 0x41000003`) after the arrival (`the-player-plays-the-dead-motion-on-its-own-body-and-stands-up-after-the-respawn`; `PlayerIsDead`, 11.6096 0x006B4CB0, already stops the player's own keys while the forward command is Dead). The corpse is an ordinary server object: its CreateObject (flag `BF_CORPSE`, "Corpse of <name>", the player's Setup 0x02000001 and motion table, forward motion Dead) makes an avatar with its own motion player, drawn by the server-objects layer in the Dead pose; the player's own body does nothing with it. The ObjDesc (clothing) on the stand-in's corpse is a placeholder (`TODO(m3-standin-corpse-weenie)`); whether ACE's corpse carries the player's clothing is first seen live. Portal space plays on the death's F751 like any teleport's, and the vitals meters (`GameVitals`) follow the 0x02E9 updates. The whole account is `docs/session.md` section 32.

## 18. Picking and selection (G5 W7, G4 slice g0)

A click on the world finds the object under the pointer, the selection remembers it, and the shell asks where it is on the screen.

| Piece | Where | What it does |
|---|---|---|
| `PickView`, `ScreenRect`, `PickRay` | `Client/Play/PickView.cs` | The world camera as a value: the `CameraLens` the camera is drawn with (the portal zoom's frustum or the game's perspective, **with the D40 off-centre offset, zero today**), the view's size in logical UI pixels and the camera's pose. It takes the lens's rectangle from `CameraLens.Sides`, the one place that is computed, so there is no second projection. `Ray(x, y)` turns a window point into a ray; `TryProject` and `TryProjectSphere` go the other way (false behind the near plane; `onScreen` false when the rectangle misses the view). |
| `RayPicking`, `PickShape` | `Client/Play/RayPicking.cs`, `PickShape.cs` | `Pick(ray, parts, shapes, excludeGuid, polygons)`: retail's per-part test (below). A `PickPart` is one drawn part placed as drawn (object, part index, the GfxObj id drawn, origin, orientation, scale); a `PickShape` is the GfxObj's drawing sphere and its drawn triangles (`PickShape.FromMesh(GfxObjMesh)`: the mesh's arrays, no copy). The player is the excluded guid. |
| `ObjectSelection`, `ISelectionProjector` | `Client/Play/ObjectSelection.cs` | The one selected guid (0 none), `Changed(previous, current)`, `Select`, `Clear`, `Attach(IEventBus)`. `ISelectionProjector.TryProject(guid, out ScreenRect, out bool onScreen)` is m2-plan §4.13's contract with the shell's own rectangle type in place of Godot's `Rect2` (the G4 screens are engine-agnostic), in logical UI pixels. |
| `WorldPicking` | `scripts/Player/WorldPicking.cs` | The game side, implementing `ISelectionProjector`: reads the camera through `CameraLenses.Of`, turns the parts the avatar set drew in the last frame (`AvatarSet.Frame`: the GfxObj id of the level drawn, the frame, the scale) of the avatars the `ServerObjectsLayer` drew into `PickPart`s in AC axes (render space is the AC axes permuted, `AcToGodot.ToAc`), moves the click ray to AC axes the same way, and answers `TryPick(x, y)` and `TryProject(guid)`. `PlayMode` owns it and the `ObjectSelection` (`PlayMode.Picking`, `PlayMode.Selection`) and gives the picking to `RetailUiLayer.SetSelectionProjector`. |
| The click | `PlayerController.OnWorldPointer` | A left press picks: the object under the pointer is selected, empty space clears; a double click's press also Uses it, `Actions.Use(guid, ActionSource.Ui)` (the handle is `LastUse`). Other buttons and releases do nothing (the mouse look is the middle button's). Without picking (the explore scene) a click is logged once and ignored. |

**How retail picks (G4 g5a; the 11.4186 decompile, read; addresses are 11.6096).** Retail has no pick list: a click stores a screen position (`SmartBox::find_object` 0x00451CA0, `Render::set_selection_cursor` 0x0054C360) and the next draw does the test while it draws. `Render::update_viewpoint` (0x0054D9E0) turns the position into a world ray (`Render::pick_ray` 0x0054C220, the projection's inverse: the same lens as D40's `CameraLens`). Each drawn part of each drawn object (the draw calls `Render::GfxObjUnderSelectionRay`, 0x0054D350, once per part that passes the view cone) then does this, in the part's own frame and scale:
1. The ray is tested against the part's `CGfxObj` **drawing sphere** (`CSphere::sphere_intersects_ray`, 0x005384E0). An eye inside the sphere is a miss; a hit has a time along the ray. The ray length is 10000.
2. A sphere hit is dropped when a polygon hit already found is nearer than it; otherwise it becomes the best sphere hit if it is nearer than the best so far (a strict less-than, so the part drawn first keeps a tie).
3. For a **click** (hover searches pass flag 0) every polygon of the part is tested (`CPolygon::polygon_hits_ray`, 0x0053A320: a single-sided polygon only from its front, a double-sided one from both, the plane's time not negative, the point inside the polygon). A polygon hit becomes the best polygon hit if it is nearer than the best so far.
4. After the draw `SmartBox::DrawNoBlit` (11.4186 0x00454C20; it has no 11.6096 row, its neighbours are +0xA0 from 11.4186) reads the result (`Render::GetMouseSelectionObjectID` 0x0054D560, `GetMouseSelectionPartIndex` 0x0054D590): **a polygon hit if there is one, else the nearest sphere hit, else nothing.**

`RayPicking.Pick` is that, per part in draw order, over the parts of every pickable object. The Setup's `selection_sphere` is not used for picking: retail uses it only for the vivid target indicator's box (`CPhysicsObj::GetSelectionSphere` 0x0050F510), which is `ISelectionProjector.TryProject` here.

**What can be picked:** every avatar the layer drew in the last frame (`ServerObjectsLayer.IsDrawn`), in the world and not parented (a wielded item is not drawn on its own), never the player; of it, the parts the avatar set drew (a GfxObj, translucency below 1) at the level of detail they were drawn at. Hidden, NoDraw and undrawn objects are not pickable; a part fading in (translucency strictly between 0 and 1, drawn by its blended twin, section 17) is picked like any drawn part, as retail picks what it draws, and one at translucency 1 is not. Objects do not block each other from being picked beyond the nearest-wins rule: no occlusion by the terrain or buildings (retail has none either, but it tests only what its draw reached through its portal list, and Lifestone tests every object the layer drew).

**Deviations from retail (all deliberate).**
- *When it is tested.* Retail tests during the draw after the click; Lifestone tests at the click against the last drawn frame's poses (`AvatarSet.Frame`). The selection and a double click's Use are handled at the click, and the difference is one frame of motion.
- *The polygon of a part, and the units of a hit time (glitch fixes, review R4; `--retail-picking` restores retail's).* Retail's per-part loop stops at the first polygon (in the part's order) that the ray hits (`Render::GfxObjUnderSelectionRay`, 0x0054D350), and compares the hit times of each part's own local ray, whose direction is rescaled by the part's scale. Both can pick the wrong object: a part's far surface can lose to another object that is nearer than that surface but farther than the part's front, and a scaled part's time is not comparable with an unscaled part's. So by default `RayPicking` keeps the nearest polygon of a part and compares hit times in world units along the unit ray. `--retail-picking` (`PlayOptions.RetailPicking`; `RayPicking.Pick(..., retailRules: true)`) restores both: the first hit triangle of a part in the mesh's order, and times scaled to the part's local units (the world time times the length of the ray's direction in the part's scaled frame, so `PickHit.Distance` is in those units). The restored mode is an approximation of retail in two ways: the mesh's triangle order stands for the polygon order (the mesh groups triangles by surface), and the decompile builds the local direction with one uniform factor (1 over the length of the scale vector) where this uses the per-axis inverse scale; the picks agree when the scale is 1. `RayPickingTests` pins each mode.
- *Behind the eye.* Retail's sphere test reports a sphere wholly behind the ray's origin (with a negative time); the draw's view cone culls such a part first. Here it is a miss.
- *Polygons.* They are the drawn triangles of `GfxObjMesh` (a convex polygon's fan), not `CPolygon` itself; a polygon drawn from both sides has a reversed copy there, so the front-face rule is the winding.
- *The ray length* is applied to both classes of hit; retail's sphere test does not read it (nothing is drawn that far).

**What remains approximated.** The indicator's box (`TryProject`): a square of the selection sphere's centre and radius over depth, not retail's box (g5b reads `s_BlitMethod` and `min_dist`). The cursor change over an object and the hover search (the `polygons: false` mode exists and is tested; nothing calls it yet). A part's drawing sphere is trusted as in retail; a GfxObj without a drawing sphere cannot be picked.

**When the selection clears** (the rule this port uses; retail's own is not researched): the selected object's `ObjectDeleted`, a `PlayerTeleportAnnounced` (the world around the player is replaced), a log off (`PlayMode.NewObjects`, which renews the avatar set), and a click on empty space.

**Checks.** Client tests (`RayPickingTests`): the ray from the centre and the corners, an off-centre frustum, the projection returning the pixel a ray came from; the picking: a polygon hit beating a nearer sphere whose polygons the ray misses, the hover search (spheres only), the nearest sphere with no polygon hit and the nearest polygon, in either list order, the two `--retail-picking` rules against their defaults (a part's first against its nearest polygon, a scaled part's local-unit time against world units), a part hit where its frame puts it (offset, scaled, turned so that it shows a back face), the player excluded, the 10000 ray length, the eye inside a sphere and a sphere behind it; and the selection's clearing on delete and teleport. `--play-checks`: the stand-in portal projects onto the screen, a click at the nearest in-window point to its projected centre selects it, a click on empty space clears, a double click Uses it and the server confirms the Use (the teleport follows as before).

**Not built:** terrain and wall occlusion of a pick, hover highlight and the cursor change over a selectable object, the keyboard targeting commands. The shell's target indicator and the health bar are G4 slice g5b.
