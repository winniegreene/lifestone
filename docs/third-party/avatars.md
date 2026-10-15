# Third-party sources: avatars track (M2)

This track owns `src/Lifestone.Data/Appearance/`, `src/Lifestone.Data/Motion/` (after wave 0),
`src/Lifestone.Client/Avatars/` and their tests ([avatars.md](../avatars.md)). Product code is re-expressed from the
retail decompile. ACE code is ported into test oracles, and into product code only for names and values: two small
files in `Data/Motion` (below), beside the wave-0 motion data types the lead brought from ACE
([foundation.md](foundation.md)). Lifestone is AGPL-3.0, and every source below is compatible with that. Nothing derived
from the user's dats is committed: the tests read the dats at run time and pin counts and ids only.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src` (Hex-Rays output with PDB names), individual module files | Turbine/WB IP; reference only | commit 01ccd5be2d8fff40e25151d804fa97b3e8f27ec6, read 2026-10-12 | Behaviour of the functions listed below. Nothing copied. |
| Mag-nus/acclient-AI-RE, `symbol_transplant_11.6096.tsv` | Turbine/WB IP; reference only | the lead's pinned copy (owner decision D4) | The 11.6096 addresses in [avatars.md](../avatars.md) §8. |
| Mag-nus/acclient-AI-RE, `2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md` | Reference only | commit 01ccd5be2d8fff40e25151d804fa97b3e8f27ec6, read 2026-10-12 | Sections 4 and 5: the 11.6096 jump-gating set and the functions that changed. Nothing copied. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d412459ff619a12cc18f82bcf72c1c2a8 (`vendor-eval/ACE`) | **Ported:** the test oracles and the two motion files below. Read: the server's ObjDesc composition, as the world look to compare with; MotionInterp, as the oracle of the motion interpreter. |
| eriknihlen/OpenAC | MIT | @1ee5a08 (`vendor-eval/OpenAC`) | Read only, as a cross-check of the interpolation manager (`src/AcDream.Core/Physics/InterpolationManager.cs`). Nothing copied. |
| Chorizite/DatReaderWriter | MIT | NuGet 2.1.9 (already a project dependency) | Used, not ported: CharGen, ClothingTable, PalSet, Setup, GfxObj and ObjDesc records. |

## Files

### Ported (header names the source)

ACEmulator/ACE, AGPL-3.0, @95ef426d. Each file carries a header (its first lines):

```text
AceObjDescOracle.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Creature_Networking.cs (CalculateObjDesc),
// WorldObject_Networking.cs (SerializeModelData, AddBaseModelData), ACE.Server/Factories/PlayerFactory.cs (the
// appearance part of Create), ACE.Server/Network/Extensions.cs (WritePackedDword, WritePackedDwordOfKnownType, Align),
// ACE.Entity/ObjDesc.cs, ACE.Entity/Enum/SetupConst.cs, ACE.DatLoader/Entity/SexCG.cs (the Get* helpers) and
// ACE.DatLoader/FileTypes/PaletteSet.cs (GetPaletteID) (AGPL-3.0) [with changes: ...].

AceMotionInterpOracle.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/MotionInterp.cs, RawMotionState.cs,
// InterpretedMotionState.cs, MovementParameters.cs and MovementSystem.cs (AGPL-3.0) [with changes: ...].

AceMoveToOracle.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Managers/MoveToManager.cs, Animation/MovementParameters.cs
// (get_command, towards_and_away, get_desired_heading) and Common/Position.cs (heading, CylinderDistance) (AGPL-3.0)
// [with changes: ...].

WeenieError.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/WeenieError.cs (AGPL-3.0)
// with changes: only the values the motion code returns, as a uint enum; ...

MovementParameters.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/MovementParamFlags.cs (AGPL-3.0)
// with changes: the flag names and values only; ...
```

| Lifestone file | ACE source (under `Source/`) | Notes |
|---|---|---|
| `tests/Lifestone.Data.Tests/Appearance/AceObjDescOracle.cs` | `ACE.Server/WorldObjects/Creature_Networking.cs` (`CalculateObjDesc`), `ACE.Server/WorldObjects/WorldObject_Networking.cs` (`SerializeModelData`, `AddBaseModelData`), `ACE.Server/Factories/PlayerFactory.cs` (appearance part of `Create`), `ACE.Server/Network/Extensions.cs` (`WritePackedDword`, `WritePackedDwordOfKnownType`, `Align`), `ACE.Entity/ObjDesc.cs`, `ACE.Entity/Enum/SetupConst.cs`, `ACE.DatLoader/Entity/SexCG.cs`, `ACE.DatLoader/FileTypes/PaletteSet.cs` | Test-only oracle: the ObjDesc ACE sends for a newly created player and its ModelData bytes. Reduced to that case; DatReaderWriter records; worn clothing given in ACE's final order; no database. |
| `tests/Lifestone.Data.Tests/Motion/AceMotionInterpOracle.cs` | `ACE.Server/Physics/Animation/MotionInterp.cs`, `RawMotionState.cs`, `InterpretedMotionState.cs`, `MovementParameters.cs` (`CopySome`), `MovementSystem.cs` | Test-only oracle (AV5): ACE's MotionInterp reduced to the interpreted and raw paths, the speeds, the jump rules and `MovementSystem`; its PhysicsObj and WeenieObject are a recording stand-in; `motion_allows_jump` is a replaceable delegate so the random comparison can isolate the other differences. |
| `tests/Lifestone.Data.Tests/Motion/AceMoveToOracle.cs` | `ACE.Server/Physics/Managers/MoveToManager.cs`, `ACE.Server/Physics/Animation/MovementParameters.cs` (`get_command`, `towards_and_away`, `get_desired_heading`), `ACE.Server/Physics/Common/Position.cs` (`heading`, `CylinderDistance`) | Test-only oracle (AV14): ACE's MoveToManager over an adapter of its PhysicsObj, WeenieObject and PhysicsTimer; Lifestone's `MovementParameters`, `CellFrame` and `MotionInterpreter` stand in for ACE's. |
| `src/Lifestone.Data/Motion/WeenieError.cs` | `ACE.Entity/Enum/WeenieError.cs` | Names and values only: the subset the motion code returns (stage av3 adds the MoveTo errors 0x0B, 0x36-0x38, 0x3C and 0x3D). The values are also the literals of the 11.4186 decompile. |
| `src/Lifestone.Data/Motion/MovementParameters.cs` | `ACE.Server/Physics/Animation/MovementParamFlags.cs` | The flag names and values only. The parameters and their defaults are re-expressed from retail `MovementParameters::MovementParameters` (11.4186 0x00524380), which differ from ACE's constructor (no CanCharge; walk/run threshold 15). |

### Re-expressed retail behaviour (each type cites its functions)

Addresses are 11.4186 / 11.6096 ([avatars.md](../avatars.md) §8 has the full list).

| Lifestone file | Retail functions |
|---|---|
| `src/Lifestone.Data/Appearance/ObjDesc.cs` | `ObjDesc::UnPack` (0x005ABCF0 / 0x005ACDA0), `ObjDesc::operator+=` (0x005AC0A0 / 0x005AD150), `Subpalette::UnPack`, `TextureMapChange::UnPack`, `TextureMapChange::replaces`, `AnimPartChange::UnPack`, `Unpack_AsDataIDOfKnownType` |
| `src/Lifestone.Data/Appearance/ObjDescBuilder.cs` | `ObjDesc::AddSubpalette`, `AddTextureMapChange`, `AddAnimPartChange`, `RemoveDuplicateSubpalette`, `RemoveDuplicateTextureMapChange`, `RemoveDuplicateAnimPartChange`, `operator+=`, `Wipe` |
| `src/Lifestone.Data/Appearance/PartArrayAppearance.cs`, `PartAppearance.cs` | `CPartArray::DoObjDescChanges`, `DoObjDescChangesFromDefault`, `SetPart`, `SetTextureMap`, `SetPalette`, `InitObjDescChanges`, `InitPals`, `DestroyPals`, `InitParts`; `CPhysicsPart::SetPart`, `SetGfxObjArray`, `DetermineBasePal`, `SetTextureMap`, `UsePalette`, `RestorePalette`, `InitObjDescChanges`, `CopySurfaces`, `RestoreSurfaces`; `Palette::makeModifiedPalette`, `Palette::Modify`; `CSurface::UseTextureMap`, `UsePalette` |
| `src/Lifestone.Data/Appearance/IAppearanceSource.cs`, `DatAppearanceSource.cs` | `CPhysicsPart::LoadGfxObjArray`, `CSurface::GetOriginalTextureMapID`, `CSurface::GetOriginalPaletteID` |
| `src/Lifestone.Data/Textures/TextureLoader.cs` (`GetOriginalPaletteId` only) | `CSurface::Serialize`, `CSurface::InitEnd`, `CSurface::GetOriginalPaletteID` |
| `src/Lifestone.Data/Appearance/ClothingObjDesc.cs` | `ClothingTable::BuildObjDesc` (0x005A7900 / 0x005A89B0) and its heritage fallback groups (values read under D4, docs/research/retail-addresses-m2.md §1.4), `ClothingBase::ApplyPartAndTextureChanges`, `ShadePackage::ShadePackage`, `ShadePackage::GetVal`, `PalSet::GetPaletteID` |
| `src/Lifestone.Data/Appearance/CharGenAppearance.cs`, `CharGenLook.cs` | `gmCG3DView::Update` (0x004EE9D0 / 0x004EF660) with its `HUMAN_SETUP_ID` (read under D4), `gmCG3DView::gmCG3DView`, `CharGenState::GetSetupID`, `CharGenState::GetBaldState`; `CharGenState::StoreColorInformation` and `Get*PaletteTemplateID` are cited for the CharGen screens |
| `src/Lifestone.Data/Motion/InterpretedMotionState.Motion.cs`, `RawMotionState.Motion.cs` | `InterpretedMotionState::ApplyMotion`, `RemoveMotion`, `copy_movement_from`, `AddAction`, `RemoveAction`, `GetNumActions`; `RawMotionState::ApplyMotion`, `RemoveMotion`, `AddAction`, `RemoveAction` (CMInterpPrim.c) |
| `src/Lifestone.Data/Motion/WireMotion.cs` | `InterpretedMotionState::UnPack` (0x0051F400 / 0x0051FF10) and `Pack` (0x0051F1D0 / 0x0051FCE0); `MovementManager::unpack_movement` (cited for AV8) |
| `src/Lifestone.Data/Motion/MotionInterpreter.cs`, `IMotionInterpHost.cs` | `CMotionInterp` (CMotionInterp.c): `Create`, `enter_default_state`, `PerformMovement`, `DoMotion`, `DoInterpretedMotion`, `StopMotion`, `StopInterpretedMotion`, `StopCompletely`, `move_to_interpreted_state`, `apply_raw_movement`, `apply_interpreted_movement`, `apply_current_movement`, `ReportExhaustion`, `SetWeenieObject`, `SetHoldKey`, `set_hold_run`, `HitGround`, `LeaveGround`, `charge_jump`, `jump_charge_is_allowed`, `jump_is_allowed`, `jump`, `motion_allows_jump` (11.6096 set), `contact_allows_move`, `is_standing_still`, `adjust_motion`, `apply_run_to_command`, `get_state_velocity`, `get_jump_v_z`, `get_leave_ground_velocity`, `get_max_speed`, `get_adjusted_max_speed`, `HandleExitWorld`, `MotionDone`, `add_to_queue`, `InqStyle`; the `CPhysicsObj` and `CWeenieObject` calls they make |
| `src/Lifestone.Data/Animation/MotionPlayer.cs` (the part-array operations) | `CPartArray::DoInterpretedMotion`, `StopInterpretedMotion`, `StopCompletelyInternal`, `InitializeMotionTables`, `CheckForCompletedMotions`, `HandleMovement`, `RemoveLinkAnimations`; `MotionTableManager::RemoveLinkAnimations`, `PerformMovement`, `UseTime` (as ACE's, which calls `CheckForCompletedMotions`); `CPhysicsObj::RemoveLinkAnimations` |
| `src/Lifestone.Data/Motion/MovementSystem.cs` | `MovementSystem::GetRunRate`, `GetJumpHeight`, `JumpStaminaCost`; `EncumbranceSystem::EncumbranceCapacity`, `Load`, `LoadMod` |
| `src/Lifestone.Data/Motion/PositionFrames.cs` | `Position::get_offset`, `Position::distance`, `Position::subtract2`, `Position::localtolocal`, `Frame::subtract2`, `Frame::get_heading`, `Frame::set_heading`, `Frame::set_vector_heading` |
| `src/Lifestone.Data/Motion/InterpolationManager.cs` | `InterpolationManager::InterpolateTo`, `adjust_offset`, `UseTime`, `NodeCompleted`, `StopInterpolating` (CInterpolationManager.c) |
| `src/Lifestone.Data/Motion/ConstraintManager.cs` | `ConstraintManager::ConstrainTo`, `adjust_offset`, `UnConstrain`, `IsFullyConstrained` (CConstraintManager.c) |
| `src/Lifestone.Data/Motion/PositionManager.cs`, `PositionContext.cs` | `PositionManager::adjust_offset`, `UseTime`, `InterpolateTo`, `StopInterpolating`, `ConstrainTo`, `UnConstrain`, `IsInterpolating`, `IsFullyConstrained`, `StickTo`, `UnStick`, `GetStickyObjectID`, `HandleUpdateTarget` (CPositionManager.c); `CPhysicsObj::GetAutonomyBlipDistance`, `GetStartConstraintDistance`, `GetMaxConstraintDistance`, `UpdatePositionInternal` |
| `src/Lifestone.Data/Motion/PositionFrames.cs` (stage av3 additions) | `Position::heading`, `Position::cylinder_distance`, `Position::cylinder_distance_no_z` |
| `src/Lifestone.Data/Motion/MoveToManager.cs`, `MovementParameters.MoveTo.cs` | `MoveToManager` (CMoveToManager.c): `MoveToObject`, `MoveToObject_Internal`, `MoveToPosition`, `TurnToObject`, `TurnToObject_Internal`, `TurnToHeading`, `PerformMovement`, `CancelMoveTo`, `CleanUp`, `CleanUpAndCallWeenie`, `InitializeLocalVariables`, `BeginNextNode`, `BeginMoveForward`, `BeginTurnToHeading`, `HandleMoveToPosition`, `HandleTurnToHeading`, `HandleUpdateTarget`, `HitGround`, `UseTime`, `CheckProgressMade`, `GetCurrentDistance`, `_DoMotion`, `_StopMotion`, `AddTurnToHeadingNode`, `AddMoveToPositionNode`, `RemovePendingActionsHead`, `heading_greater`, `heading_diff`; `MovementParameters::get_command`, `towards_and_away`, `get_desired_heading` (CMovementDefs.c) |
| `src/Lifestone.Data/Motion/StickyManager.cs` | `StickyManager` (CStickyManager.c): `Create`, `StickTo`, `adjust_offset`, `UseTime`, `HandleUpdateTarget`, `HandleExitWorld` |
| `src/Lifestone.Data/Motion/TargetManager.cs` | `TargetManager` (CTargetting.c): `SetTarget`, `ClearTarget`, `SetTargetQuantum`, `AddVoyeur`, `RemoveVoyeur`, `ReceiveUpdate`, `HandleTargetting`, `CheckAndUpdateVoyeur`, `SendVoyeurUpdate`, `GetInterpolatedPosition`, `NotifyVoyeurOfEvent`; `CPhysicsObj::set_target`, `clear_target`, `get_target_quantum`, `set_target_quantum`, `receive_target_update`, `add_voyeur`, `remove_voyeur` |
| `src/Lifestone.Data/Motion/MovementManager.cs`, `WireMovement.cs` | `MovementManager` (CMovementManager.c): `unpack_movement`, `MakeMoveToManager`, `CancelMoveTo`, `UseTime`, `HitGround`, `LeaveGround`, `HandleExitWorld`, `HandleUpdateTarget`, `IsMovingTo`; `MovementParameters::UnPackNet`; `CPhysicsObj::MoveToObject`, `TurnToObject`, `TurnToHeading`, `stick_to_object` |
| `src/Lifestone.Data/Animation/AnimSequence.cs` (`SetSequenceAnimation`) | `CPhysicsObj::set_sequence_animation` |
| `src/Lifestone.Client/Avatars/AvatarBody.cs` | `CPhysicsObj` (CPhysicsObj.c): `update_object`, `UpdateObjectInternal`, `UpdatePositionInternal`, `UpdatePhysicsInternal`, `set_active`, `set_state`, `set_nodraw`, `set_lights`, `set_parent`, `unset_parent`, `add_child`, `UpdateChild`, `UpdateChildrenInternal`, `leave_world`, `exit_world`, `teleport_hook`, `MoveOrTeleport`, `SetPlacementFrame`, `set_description`, `InitDefaults`, `SetMotionTableID`, `MakeMovementManager`, `MakePositionManager`, `prepare_to_enter_world`, `unpack_movement`, `animate_static_object` (read), `DoObjDescChangesFromDefault`, `set_velocity`, `set_local_velocity`, `set_on_walkable`, `set_heading`, `HandleUpdateTarget`; `SmartBox::DoVectorUpdate`, `DoPickupEvent`; `CPhysics::SetObjectMovement`; `ACCObjectMaint::CreateObject` (read: server objects are made dynamic); `CPartArray::HandleMovement`; `CObjCell::hide_object` (read) |
| `src/Lifestone.Client/Avatars/AvatarSet.cs`, `AvatarWire.cs`, `AvatarCreate.cs` | `SmartBox::HandleCreateObject`, `HandleUpdateObject`, `HandleDeleteObject`, `UpdateVisualDesc`, `HandleReceivedPosition`, `DoSetState`, `DoParentEvent`, `HandleObjDescEvent`; `ACCObjectMaint::CreateObject`, `SetVisualDesc`; `CObjectMaint::SetChildren` |
| `src/Lifestone.Client/Avatars/PlayerAvatar.cs`, `KinematicPlayerBody.cs` | `CPhysics::SetObjectMovement` (the player branch); `CPhysicsPart::UpdateViewerDistance` (the player's level 0) |
| `src/Lifestone.Client/Avatars/CreaturePreview.cs` | `gmCG3DView::StartAnimation`, `StopAnimation`, `SetPlayerHeading`, `gmCG3DView`, `Initialize`, `Update` (the object and the animations); `CPhysicsObj::update_position`, `set_sequence_animation`, `clear_sequence_anims`, `makeObject`; `UIElement_Viewport::SetLight` |

## Camera and picking (G5 W7 to W9, G4 slice g0)

The player's cameras and the click that selects (docs/avatars.md §18, docs/physics.md §12). Product code re-expressed from the retail decompile
(Mag-nus/acclient-AI-RE, same commit and rules as above); nothing is copied, and the constants are the literals of the functions or were read from the
11.6096 exe. Addresses: 11.4186 / 11.6096. "TODO(camera): 11.6096" in a source comment marks a function with a 11.4186 address only.

| File | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `src/Lifestone.Client/Camera/ChaseCameraModel.cs` | `CameraSet::SetDefaultOffsets`, `Closer`, `Farther`, `Rotate`, `Raise`, `Lower`, `MouseLookHandler`, `SetInHead`, `UpdateCamera` (the smoothing, stiffness 0.35 and adjustment speed 41; first person's `InHead` offset), `CameraManager::UpdateCamera` | 0x00458F80, 0x004586D0, 0x00458890, 0x00458310, 0x00457980, 0x00457CF0, 0x00458D80, 0x00458CE0, 0x00458AE0, 0x00456660 | 0x004590A0, 0x004587F0, 0x004589B0, 0x00458430, 0x00457AA0, 0x00457E10, 0x00458EA0, 0x00458E00, 0x00458C00, 0x00456780 |
| `src/Lifestone.Client/Camera/ChaseCameraModel.cs` (named, not built) | `CameraSet::LookDown` (the overhead view, `TODO(g5-camera-overhead)`), `CameraSet::SetMapMode` (`TODO(g5-camera-map-view)`), `CameraManager::UpdateCamera`'s ALIGN_WITH_PLANE (`TODO(g5-camera-slope)`) | 0x00457FF0, 0x00458200, 0x00456660 | not looked up (`TODO(camera)`), none, 0x00456780 |
| `src/Lifestone.Client/Camera/ChaseCameraModel.cs` (constants read from the exe) | `CAMERA_DEFAULT_PIVOT_Z` 1.5, the factors -2.5 and 0.75, the rotation step (8 degrees: the static initialiser sets `angle` to pi / (180 / 8)), `CAMERA_MOUSELOOK_LIMIT` 0.8 | | 0x0079CC2C, 0x0079CC24 and 0x0079CC28, 0x006EBAC0, 0x0079CC08 |
| `src/Lifestone.Client/Camera/CameraBindings.cs` | `CameraManager::OnAction` (the camera actions of input maps 5 and 6, alternate mode 0x3E), `CInputManager::SetMouseLookMode` | 0x00456180 | not looked up (`TODO(camera)`) |
| `src/Lifestone.Game/scripts/Player/ChaseCamera.cs` | `SmartBox::update_viewer` (the viewer's transition: the camera swept as a 0.3 m sphere through the collision), `CameraSet::Closer` and `Farther` (the wheel's zoom is Lifestone's own 10 percent notch: the camera map binds no wheel; `TODO(g5-wheel-zoom)`) | not recorded in the source | not recorded in the source |
| `src/Lifestone.Game/scripts/Player/FirstPersonView.cs` | `CameraSet::UpdateCamera` (the body hidden in the head with `SetTranslucencyHierarchical`, the fade from 0.45 m to 0.2 m; the fade itself is `TODO(g5-translucency)`) | 0x00458AE0 | 0x00458C00 |
| `src/Lifestone.Client/Play/RayPicking.cs` | `Render::GfxObjUnderSelectionRay` (the per-part sphere test, then the polygons), `CSphere::sphere_intersects_ray`, `CPolygon::polygon_hits_ray`, `Render::GetMouseSelectionObjectID` and `GetMouseSelectionPartIndex` (a polygon hit if there is one, else the sphere hit) | not looked up | 0x0054D350, 0x005384E0, 0x0053A320, 0x0054D560 / 0x0054D590 |
| `src/Lifestone.Client/Play/PickShape.cs` | `CGfxObj::drawing_sphere` and the drawing polygons (`sides_type` by winding); `Render::GfxObjUnderSelectionRay` | not looked up | 0x0054D350 |
| `src/Lifestone.Client/Play/PickView.cs` | none: Lifestone's own types (`PickRay`, `ScreenRect`, the world camera as picking and the selection projection see it, the same `CameraLens` the world is drawn with) | | |
| `src/Lifestone.Game/scripts/Player/WorldPicking.cs` | `SmartBox::find_object` and `Render::update_viewpoint` (retail tests in the next draw; Lifestone tests at the click against the last drawn frame: one frame of pose, a deliberate deviation), `CPhysicsObj::GetSelectionSphere` (the selection box: the Setup's selection sphere, else a sphere over its height and radius) | not looked up | not looked up |

The picking glitches retail has (two, `--retail-picking`) are listed in docs/avatars.md §18.

## Own implementations (no source copied)

`GfxObjMesh.SurfaceIds` (the surface list the GfxObj builder already read), `AppearanceResult`, `CharGenComposition`,
`GearChoice` and the `VariantKey` and `CacheKey` formats are written for Lifestone. So are `WireInterpretedMotion`,
`WireMotionItem`, `WireMotionFailure`, `PendingMotion`, `MotionMovementType`, `PositionContext`, `InterpolationBlip`,
`InterpolateToResult`, the `MotionPlayer` pending count and `IPlayerBody` (the m2-plan section 4.9 contract). Stage av3
adds, as Lifestone's own: `WireMovement`, `WireMoveTo`, `WireTurnTo`, `MovementUnpackResult`, `IMoveToHost`,
`ITargetable`, `ITargetWorld`, `MoveToNode`, `AvatarSources`, `AvatarStep`, the anchor landblock, `AvatarFrame` and
`AvatarDraw`, `AvatarNotice`, `AvatarStats`, `IPlayerMovementBody`, `KinematicPlayerBody` (a placeholder body),
`AvatarStress` and `AvatarStressDriver` (a synthetic crowd), and `PreviewAnimations`.
