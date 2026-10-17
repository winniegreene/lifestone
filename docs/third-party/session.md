# Third-party sources: M2 state track

The state track ([m2-plan.md](../milestones/m2-plan.md) §3.3 "state", §4.7) builds the session layer in
`src/Lifestone.Client/Session/` and the headless host in `src/Lifestone.Headless/` ([session.md](../session.md)). It
copies no third-party code. It re-expresses retail client behaviour from the decompile, citing each function by name
with its 11.4186 and 11.6096 addresses ([retail-addresses-m2.md](../research/retail-addresses-m2.md)), and pastes none
of it. Nothing derived from the user's dats or exe is committed.

It covers the contract stage (ST0) and steps ST1 to ST15 (ST17 completed it), and the movement scenarios and online flows
of M2 item 5b (ST16, G6 W11 and W12: "ST16 and G6 W11: the movement scenarios and the online flows", below). ST13 to ST15
assemble the parts above into a live session, a replayer and a headless host (below); the one retail function they add is
character creation's starting skill classes, for the headless host's character.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-12 | The function names and addresses below. Nothing copied. |
| Mag-nus/acclient-AI-RE, `2015-10 11.6096/docs/symbol_transplant_11.6096.tsv` | reference only | pinned in retail-addresses-m2.md | The 11.6096 addresses below. |
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d | **Reference only:** what ACE sends and expects (protocol.md); nothing copied. |

## Files

### Ported

None.

### Re-expressed retail behaviour (each type cites its functions)

The contract (ST0) names the behaviour; the reducer (ST7, ST8) and the actions (ST10, ST11) implement it.

| Lifestone file | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `Session/Contracts/WorldEvents.cs` (`ObjectCreated`, the reducer's create path) | `SmartBox::HandleCreateObject`, `SmartBox::HandleUpdateObject`, `SmartBox::ProcessObjectNetBlobs` | 0x00454C80, 0x00455580, 0x00454B20 | 0x00454D20, 0x00455620, 0x00454BC0 |
| `Session/Contracts/WorldEvents.cs` (`ObjectDeleted`) | `SmartBox::HandleDeleteObject` | 0x00451EA0 | 0x00451EE0 |
| `Session/Contracts/WorldEvents.cs` (`ObjectMoved`, `PositionVerdict`) | `SmartBox::HandleReceivedPosition`, `SmartBox::UnpackPositionEvent`, `CPhysicsObj::is_newer`, `CPhysicsObj::newer_event` | 0x00453FD0, 0x004542C0, 0x00451AD0, 0x00451B10 | 0x00454070, 0x00454360, 0x00451B10, 0x00451B50 |
| `Session/Contracts/WorldEvents.cs` (`ObjectVectorChanged`) | `SmartBox::HandleVectorUpdate` | 0x00453480 | 0x004534C0 |
| `Session/Contracts/WorldEvents.cs` (`ObjectStateChanged`, `SelfTravel.Present`) | `SmartBox::HandleSetState`, `SmartBox::DoSetState` | 0x004533E0, 0x004520D0 | 0x00453420, 0x00452110 |
| `Session/Contracts/WorldEvents.cs` (`ObjectParented`) | `SmartBox::HandleParentEvent` | 0x004535D0 | 0x00453610 |
| `Session/Contracts/WorldEvents.cs` (`ObjectPickedUp`) | `SmartBox::HandlePickupEvent` | 0x00453530 | 0x00453570 |
| `Session/Contracts/WorldEvents.cs` (`ObjectDescChanged`) | `SmartBox::HandleObjDescEvent` | 0x00453340 | 0x00453380 |
| `Session/Contracts/WorldEvents.cs` (`PlayerTeleportAnnounced`) | `SmartBox::HandlePlayerTeleport` | 0x00452150 | 0x00452190 |
| `Session/Contracts/SessionEvents.cs` (`LogOffRequested`), `IActionApi.LogOff` | `CPlayerSystem::RequestLogOff` | 0x00562DD0 | 0x00563B70 |
| `Session/Contracts/IActionApi.cs` (`LoginComplete`) | `CPlayerSystem::SendLoginCompleteNotification` | 0x00562E90 | 0x00563C30 |
| `Session/Contracts/IMovementSink.cs` (the cadence physics owns; the sink stamps and sends) | `CommandInterpreter::SendMovementEvent`, `ShouldSendPositionEvent`, `SendPositionEvent` | 0x006B4680, 0x006B45E0, 0x006B4770 | 0x006B55C0, 0x006B5520, 0x006B56B0 |
| `Session/State/ObjectMessageGate.cs` (ST4: the instance gate the handlers share) | `SmartBox::HandleSetState`, `HandleObjDescEvent`, `HandleVectorUpdate`, `HandlePickupEvent`, `HandleParentEvent`, `UnpackPositionEvent`, `HandleDeleteObject` | 0x004533E0, 0x00453340, 0x00453480, 0x00453530, 0x004535D0, 0x004542C0, 0x00451EA0 | 0x00453420, 0x00453380, 0x004534C0, 0x00453570, 0x00453610, 0x00454360, 0x00451EE0 |
| `Session/State/ObjectMessageGate.cs` (ST4: the per-field stamps, `TryAdvance`) | `CPhysicsObj::is_newer`, `CPhysicsObj::newer_event`, `SmartBox::DoSetState`, `DoVectorUpdate`, `DoPickupEvent`, `DoParentEvent`, `UpdateVisualDesc`, `HandlePlayerTeleport` | 0x00451AD0, 0x00451B10, 0x004520D0, 0x004521C0, 0x00452240, 0x00452290, 0x00451F40, 0x00452150 | 0x00451B10, 0x00451B50, 0x00452110, 0x00452200, 0x00452280, 0x004522D0, 0x00451F80, 0x00452190 |

The reducer (ST7, ST8) re-expresses these, read from the 11.4186 per-file decompile (`PORTAL/smartbox/SmartBox.c`,
`AC/acsmartbox/ACSmartBox.c`, `PORTAL/cphysics/CPhysics.c`, `PORTAL/cphysobj/CPhysicsObj.c`,
`AC/accobjmaint/ACCObjMaint.c`, `PORTAL/weenie_net/WTimeStamper.c`); nothing was copied.

| Lifestone file | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `Session/State/GameStateReducer.World.cs` (`OnCreateObject`, `CreateNew`, `UpdateExisting`) | `SmartBox::HandleCreateObject`, `SmartBox::HandleUpdateObject`, `SmartBox::UpdateVisualDesc` | 0x00454C80, 0x00455580, 0x00451F40 | 0x00454D20, 0x00455620, 0x00451F80 |
| `Session/State/GameStateReducer.World.cs` (`ReceivePosition`, `OnUpdatePosition`) | `SmartBox::HandleReceivedPosition`, `SmartBox::UnpackPositionEvent`, `CPhysicsObj::MoveOrTeleport` (its teleport test) | 0x00453FD0, 0x004542C0, 0x00516330 | 0x00454070, 0x00454360, 0x00516E30 |
| `Session/State/GameStateReducer.World.cs` (`OnUpdateMotion`, `SetMovement`) | `ACSmartBox::DispatchSmartBoxEvent` (the F74C gate), `CPhysics::SetObjectMovement` | 0x005595D0, 0x00509690 | 0x0055A210, 0x0050A160 |
| `Session/State/GameStateReducer.World.cs` (`OnDeleteObject`, `OnSetState`, `OnVectorUpdate`, `OnParentEvent`, `OnPickupEvent`, `OnObjDescEvent`, `OnPlayerTeleport`) | `SmartBox::HandleDeleteObject`, `HandleSetState`, `DoSetState`, `HandleVectorUpdate`, `DoVectorUpdate`, `HandleParentEvent`, `DoParentEvent`, `HandlePickupEvent`, `DoPickupEvent`, `HandleObjDescEvent`, `HandlePlayerTeleport` | 0x00451EA0, 0x004533E0, 0x004520D0, 0x00453480, 0x004521C0, 0x004535D0, 0x00452290, 0x00453530, 0x00452240, 0x00453340, 0x00452150 | 0x00451EE0, 0x00453420, 0x00452110, 0x004534C0, 0x00452200, 0x00453610, 0x004522D0, 0x00453570, 0x00452280, 0x00453380, 0x00452190 |
| `Session/State/GameStateReducer.World.cs` (`OnSound`, `OnPlayEffect`) | `SmartBox::HandleSoundEvent`, `SmartBox::HandlePlayScriptType` | 0x00451FC0, 0x00452070 | 0x00452000, 0x004520B0 |
| `Session/State/GameStateReducer.World.cs` (`HoldUntilPlayer`, `Park`, `ReplayParked`), `Session/State/PendingObjectMessages.cs` | `ACSmartBox::IsReadyToDispatchEvent`, `SmartBox::QueueNetBlob`, `SmartBox::ProcessNetBlobs`, `CObjectMaint::QueueBlobForObject`, `SmartBox::ProcessObjectNetBlobs` | 0x00559590, 0x00453980, 0x00454A70, 0x005092D0, 0x00454B20 | 0x0055A1D0, 0x00453A20, 0x00454B10, 0x00509DA0, 0x00454BC0 |
| `Session/State/GameStateReducer.Login.cs` (`OnPlayerCreate`) | `SmartBox::HandleCreatePlayer` | 0x00455110 | 0x004551B0 |
| `Session/State/GameStateReducer.Chat.cs` (`OnPrivateUpdate`) | `ClientObjMaintSystem::UpdateStat<Int_QualityType,long>` (and its sibling instantiations' type tags), `ClientObjMaintSystem::Handle_Qualities__PrivateUpdateInt`, `WTimeStamper::UpdateTS` | 0x00558000, 0x00558FD0, 0x006B2D70 | 0x00558C40, 0x00559C10, 0x006B3CB0 |
| `Session/State/GameStateReducer.Chat.cs` (`AcceptQualityStamp`, the quality-stamp rule) | `WTimeStamper::UpdateTS`: a quality seen for the first time is accepted; afterwards an update is refused only when its 8-bit sequence is older (a distance above 127 means the counter wrapped), and equal is accepted | 0x006B2D70 | 0x006B3CB0 |

The headless host's character (ST15 review fix) re-expresses retail's starting skill classes, read from the 11.4186
per-file decompile (`AC/accharacter/CharGenState.c`, `AC/accharacter/ACCharGenData.c`); nothing was copied. The
11.6096 addresses are the retail-ui-data track's (docs/retail-ui-data.md on master, from the same pinned symbol table).

| Lifestone file | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `Session/Scenarios/HeadlessCharacter.cs` (`SkillClasses`, `RetailSkillClasses`) | `CharGenState::ResetSkillLevels`, `ACCharGenData::GetSkillTrainedCost`, `ACCharGenData::GetSkillSpecializedCost` | 0x005C43B0, 0x005C26D0, 0x005C27D0 | 0x005C5390, 0x005C36B0, 0x005C37B0 |

These are not in retail-addresses-m2.md yet; their 11.6096 addresses come from the same pinned symbol table (rows
marked exact, except `SmartBox::HandleSoundEvent` and `HandlePlayScriptType`, marked verified) and are submitted to the
lead for that page: `SmartBox::DoVectorUpdate`, `UpdateVisualDesc`, `HandleSoundEvent`, `HandlePlayScriptType`,
`QueueNetBlob`, `ProcessNetBlobs`, `HandleCreatePlayer`, `ACSmartBox::IsReadyToDispatchEvent`,
`ACSmartBox::DispatchSmartBoxEvent`, `ClientObjMaintSystem::UpdateStat<Int_QualityType,long>`,
`ClientObjMaintSystem::Handle_Qualities__PrivateUpdateInt` and `WTimeStamper::UpdateTS`.

The Action API (ST9 to ST12) re-expresses what retail sends for each action and where the client's own lines come
from; it was read from the 11.4186 per-file decompile (`AC/accui_misc/PlayerSystem.c`) and the pinned symbol table, and
nothing was copied.

| Lifestone file | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `Session/Actions/Trackers/LogOffTracker.cs` (`LogOffTracker`: "Logging off..." in the chat, then the logoff) | `CPlayerSystem::RequestLogOff`, `ClientSystem::AddTextToScroll` (the `AC/accui_misc/ClientSystem.obj` body), `Proto_UI::LogOffCharacter` | 0x00562DD0, 0x00563C50, 0x00546A20 | 0x00563B70, 0x005649F0, 0x005475E0 |
| `Session/Actions/Trackers/LoginCompleteTracker.cs` | `CPlayerSystem::SendLoginCompleteNotification`, `CM_Character::Event_LoginCompleteNotification`; cited as not reproduced (no inventory read model in M2; a named `TODO(state)`): `CPlayerSystem::AttemptSendLoginCompleteNotification`, `ACCWeenieObject::AllContainedObjectsExist` | 0x00562E90, 0x006A1480, 0x00562F20, 0x0058C000 | 0x00563C30, 0x006A22A0, 0x00563CC0, 0x0058CE30 |
| `Session/Actions/Trackers/ChatTrackers.cs` (`TalkTracker`, `TellTracker`) | `CM_Communication::Event_Talk`, `CM_Communication::Event_TalkDirectByName` | 0x006A45C0, 0x006A4780 | 0x006A53E0, 0x006A55A0 |
| `Session/Actions/Trackers/UseTracker.cs` (`UseTracker`, `QueryHealthTracker`) | `CM_Inventory::Event_UseEvent`, `CM_Combat::Event_QueryHealth` | 0x006AC3B0, 0x006A9B40 | 0x006AD310, 0x006AAAA0 |
| `Session/Actions/MovementChannel.cs` | `CM_Movement::Event_MoveToState`, `CM_Movement::Event_AutonomousPosition`, `CM_Movement::Event_Jump` (the sequences each message echoes) | 0x006AFC00, 0x006AF790, 0x006AFA70 | 0x006B0AD0, 0x006B0660, 0x006B0940 |

All of these are exact rows of the pinned symbol table. `CPlayerSystem::RequestLogOff` and
`SendLoginCompleteNotification` have rows in retail-addresses-m2.md (on master); the others are not in its table yet and
are submitted to the lead for it: `ClientSystem::AddTextToScroll` (the ClientSystem.obj body; the page mentions its
11.6096 address only in prose), `Proto_UI::LogOffCharacter`, `CM_Character::Event_LoginCompleteNotification`,
`CPlayerSystem::AttemptSendLoginCompleteNotification`, `ACCWeenieObject::AllContainedObjectsExist`,
`CM_Communication::Event_Talk`, `Event_TalkDirectByName`, `CM_Inventory::Event_UseEvent`, `CM_Combat::Event_QueryHealth`,
`CM_Movement::Event_MoveToState`, `Event_AutonomousPosition` and `Event_Jump` (the last also in prose only).

**ACE behaviour the trackers wait for** (ACEmulator/ACE@95ef426d, AGPL-3.0; read for what the server answers, nothing
copied): `GameActionTalk` (server commands, `Unknown command: <name>`), `Player.HandleActionTalk` and
`WorldObject.EnqueueBroadcast` (the speaker hears their own speech), `Player.SendGagError` (the gag line),
`GameActionTell` (the OutgoingTell confirmation, WeenieError CharacterNotAvailable), `Player_Use.HandleActionUseItem`
and `SendUseDoneEvent` (one UseDone per use), `Player.HandleActionQueryHealth` (an answer only for a creature),
`GameActionLoginComplete` and `Player_Location.OnTeleportComplete` (Hidden cleared), `Position.ToLOCString` and
`AdminCommands.HandleTeleportLOC` (the text forms `AceLocText` writes and reads), and `Factories/PlayerFactory.cs`
(a new character's position is CharGen's `StarterAreas[startArea].Locations[0]`, which fixes
`InteriorLocations.TrainingAcademy` to 0x860201AD). For ST15, `Factories/PlayerFactory.cs` again (`Create` and
`ValidateAttributeCredits`: what a create may hold, so `HeadlessCharacter`'s values are ones ACE accepts: heritage and
gender as CharGen table keys, list indices from 0, no headgear as the maximum u32, attributes 10 to 100 within the
heritage's credits, exactly 55 skill classes with inactive ones skipped, every other one a SkillTable skill, trained
ones paid from the heritage's credits), `WorldObjects/Player_Skills.cs` (`UntrainSkill`: an untrained skill at
creation) and `WorldObjects/Entity/CreatureSkill.cs` (`IsUsable`: an untrained skill counts when the SkillTable's
minimum level is 1, an inactive one never, which is why the character's skills must not be left inactive).

## Own implementations (no source copied)

- **The session-facade contract** (`Session/Contracts/`, `GameSession`, `SessionBootstrap`, `SessionOptions`): written
  for Lifestone from automation-vision.md (A0) and m2-plan §4.7, over the net track's contract.
- **`ActionHandle`, `ToolHandle<T>`, `ActionLog`, `ChatLog`, `RingBuffer<T>`, `SessionClock`, `ServerTimeState` and
  the read-model containers**: Lifestone's own; the retail client has no action handles or audit log.
- **`CharacterCreateIntent`**: Lifestone's own record of the CharGen result, mapped field for field onto net's F656
  request.
- **`GameThreadSynchronizationContext`, `GameThreadBinding`** (ST1): Lifestone's own, after the BCL's
  `SynchronizationContext` contract.
- **`LoopbackGate`** (ST2): a wrapper that logs and delegates to net's `LoopbackPolicy` (Lifestone's own rule; the
  retail client connects wherever its launcher points it).
- **`EventBus`, `EventTypeId<T>`, `SubscriptionSet`** (ST3): written for Lifestone. OpenAC has a similar
  `SubscriptionSet` (MIT); it was not consulted or copied.
- **`PlayAccounts`, `BackendReadiness`, `ServerRuntimePaths`, the Session and Headless option parsers** (ST5):
  Lifestone's own, over the formats server/setup-server.ps1 and start-server.ps1 write and the server-mod track's
  schema 2 (m2-plan §4.12).
- **The `.lsrec` format, `SessionRecorder`, `SessionRecordingReader`** (ST6): Lifestone's own format.
- **The reducer's own parts** (ST7, ST8): mutate-then-publish, the pairing of net's double reports (boots, character
  errors), the NameInUse folding window, the parking bounds and expiry, the local-notice codes, `SelfTravel` and the
  login-phase bookkeeping are Lifestone's own; retail has no event bus or read model.
- **Tool replies** (ST8, ST12): the reducer reads them with the server mod's own `ToolsWire` (server/mod/Shared,
  compiled into Lifestone.Client; docs/third-party/server-mod.md); ST8's temporary `ToolReplyText` is gone.
- **`StandInSessions`, `StandInSchedule`** (ST7b): Lifestone's own stand-in data; the names are Lifestone-authored.
  An offline session's Action API is the Action API without a link (ST9; ST7b's `OfflineActions` is gone).
- **The Action API** (ST9 to ST12: `ActionApi`, `ActionTracker` and the trackers, `ActionPolicy`, `RateLimiter`,
  `ActionLink`, `ActionNotices`, `MovementChannel`, `MoveIntents`, `PlayerTools`, `ToolTracker`): Lifestone's own (A0's
  action layer); retail has no handles, lanes, rate limits, kill switch or dry run. The timeouts and limits are owner
  decision D49's documented defaults.
- **`PlaceRequests`, `AceLocText`, `PositionAgreement`, `ToolResultReader`** (ST12): Lifestone's own, over the server
  mod's ToolsWire and the M1 catalogue (`TeleportCatalog`, `AcePointsOfInterest`, `InteriorLocations`).
- **The live session** (ST13: `GameSession`'s live mode, `SessionBootstrap.CreateLive` and `NetworkOptions`,
  `NetLogAdapter`, the raw tap, reconnect and dispose, `GameStateDigest`): Lifestone's own, over the net track's
  contract (checkpoint C-net). The digest's canonical form is Lifestone's.
- **Replay** (ST14: `SessionReplayer`, `ReplayResult`, `ReplayAcSession`, `ReplayFrame`, `ReplayClock`): Lifestone's own,
  over its own `.lsrec` format and net's `InboundDispatcher`.
- **The headless host** (ST15: `HeadlessRunner`, `HeadlessRunOptions`, `HeadlessRunResult`, `CountingLogSink`,
  `DataOnlyAttachments`, `IScenario`, `LoginScenario`, `HeadlessCharacter`, and `src/Lifestone.Headless`'s `Program` and
  `TextLogSink`): Lifestone's own. Portal space's timing in it is the portal track's (`PortalSpaceDirector`,
  `TimedTunnel`, `PortalSessionBridge`; docs/third-party/portal-space.md); `DataOnlyAttachments` answers the readiness
  contract of m2-plan §4.5 from the dats (`EnvCellData`, the LandBlock records) with the same 3×3 rule as portal's
  `DestinationReadiness`. OpenAC's `AcDream.Headless` was not consulted.
- **Test support** (ST13 to ST15, in tests/Lifestone.Client.Tests/Session): `ScriptedAceSession`, `LiveHarness` and
  `HeadlessTestWorld` are Lifestone's own; `ScriptedAceSession` builds its server messages with the net track's shared
  `AceWireWriters` (tests/Shared/Net; docs/third-party/net.md), and its tool replies with the server mod's
  `ToolsWire`.

## ST16 and G6 W11: the movement scenarios and the online flows (M2 item 5b)

The player's weenie, the server's control of the player's walker, the MoveTo target lookup, the log-off key, the lighting of
moving objects in cells and the retail cadence the oracle tests check re-express the retail behaviour below, read from the
11.4186 per-file decompile and the pinned symbol table; nothing was copied. Addresses are in
[retail-addresses-m2.md](../research/retail-addresses-m2.md) or come from `tools/research/Find-RetailSymbol.ps1`; every
11.6096 address below is an exact row of the pinned table unless the row says otherwise.

| Lifestone file | Retail function | 11.4186 | 11.6096 |
|---|---|---|---|
| `Session/QualitiesWeenie.cs` (`TryGetRunRate`) | `CACQualities::InqRunRate` | 0x00592800 | not in the table (`TODO(physics)`, retail-addresses-m2.md) |
| `Session/QualitiesWeenie.cs` (`TryGetJumpVelocity`) | `CACQualities::InqJumpVelocity` | 0x00592980 | not in the table (`TODO(physics)`) |
| `Session/QualitiesWeenie.cs` (`CanJump`) | `CACQualities::CanJump` | 0x00591B50 | 0x00592850 |
| `Session/QualitiesWeenie.cs` (`TryGetJumpStaminaCost`) | `CACQualities::JumpStaminaCost` | 0x00591B90 | 0x00592890 |
| `Session/QualitiesWeenie.cs` (`RunSkill`, `JumpSkill`: formula + start level + ranks, no class bonus) | `CACQualities::InqSkill` (the exact row; a second, predicted row at 0x00592660 and 0x00593380 is not the one cited) | 0x00591DF0 | 0x00592AF0 |
| not used in the game: the creation screen's sum (`Client/RetailUi/CharGen/CharGenState.cs`) | `CharGenState::GetSkillScore` | 0x005C4B50 | 0x005C5B30 |
| `Session/Scenarios/HeadlessCharacter.cs` (`RetailSkillClassesOf`, from `CharGenState`) | `CharGenState::ResetSkillLevels` | 0x005C43B0 | 0x005C5390 |
| `Session/Scenarios/HeadlessCharacter.cs` (the costs `CharGenData` re-expresses) | `ACCharGenData::GetSkillTrainedCost`, `ACCharGenData::GetSkillSpecializedCost` | 0x005C26D0, 0x005C27D0 | 0x005C36B0, 0x005C37B0 |
| `Movement/PhysicsPlayerBody.cs` (`ServerControl`, `ApplyServerMovement`), `Session/Scenarios/RoutePlay.Exit.cs` (`ApplyServerMotions`) | `ACSmartBox::DispatchSmartBoxEvent` (a server movement of the player) | 0x005595D0 | 0x0055A210 |
| the same two (the controller loses control before the walker obeys) | `CommandInterpreter::LoseControlToServer`, called by `DispatchSmartBoxEvent` | 0x006B3170 | 0x006B40B0 |
| `Session/Scenarios/RoutePlay.cs` (the player's position updates go to the walker in order) | `SmartBox::HandleReceivedPosition` | 0x00453FD0 | 0x00454070 |
| `Session/Scenarios/SessionTargetWorld.cs` (the MoveTo target lookup) | `CObjectMaint::GetObjectA` (both overloads) | 0x00507D60, 0x00507F70 | 0x00508890, 0x00508A40 |
| `Session/Scenarios/SessionTargetWorld.cs` (what the target world answers for) | `CPhysicsObj::set_target` | 0x0050ED30 | 0x0050F800 |
| `Play/EndSessionKey.cs` (Shift+Escape, "End Character Session") | `gmDefaultMap` (KEYMAP `gmDefault`, input map 0x10000009, action 0x10000026): map data in the dat | (dat) | (dat) |
| `Play/EndSessionKey.cs` (what the action calls) | `CPlayerSystem::RequestLogOff` | 0x00562DD0 | 0x00563B70 |
| `tests/Lifestone.Loopback.Tests/Net/KinematicWalker.cs` (`PositionCadence`, test-only: the cadence ACE is sent on) | `CommandInterpreter::ShouldSendPositionEvent` | 0x006B45E0 | 0x006B5520 |
| the same (the position message it sends) | `CommandInterpreter::SendPositionEvent` | 0x006B4770 | 0x006B56B0 |
| `Interiors/CellObjectLighting.cs`, `Interiors/ObjectLightSets.cs`, `src/Lifestone.Game/scripts/World/ServerObjects/ObjectLightTable.cs` (slice b0: moving objects in a cell are lit, `TODO(interiors-light-reach)`) | `Render::minimize_object_lighting` | 0x0054D480 | 0x0054E090 |
| the same (a light leaving the set) | `Render::remove_object_light` | 0x0054C1B0 | 0x0054CDC0 |
| the same (the drawing sphere's test) | `Render::viewconeCheck` | 0x0054C250 | 0x0054CE60 |
| the same (the hardware light and its upload) | `PrimD3DRender::config_hardware_light`, `PrimD3DRender::UpdateLightsInternal` | 0x0059AD30, 0x0059AED0 | 0x0059BD40, 0x0059BEE0 |
| the same (the ambient light) | `SmartBox::SetWorldAmbientLight` | 0x004530A0 | 0x004530E0 |

The lighting rows continue [interiors.md](interiors.md), which lists the same functions by their decompile line numbers.

The scenarios themselves (`RoutePlay`, `HoltburgLapsScenario`, `AcademyExitScenario`, `LoginCheckScenario`,
`PortalExit`, `RoutePlayHost`) and the stand-in server (`StandIn*`) are Lifestone's own, with four exceptions: `StandInWorld.Cast.cs`, `StandInWorld.Combat.cs`, `StandInWorld.Death.cs` (M3 3.2) and
`StandInDamageText.cs` re-express ACE's cast, projectile, damage-line and death behaviour (listed in [effects.md](effects.md); their headers
name ACE@95ef426d and AGPL-3.0). ACE (AGPL, v1.78.4816 @95ef426d) was otherwise read only for what it sends and accepts (the Academy
portal rows of its world database, its position handling; for the fight scenario, the range rule of `Player_Magic.cs` 495-505 and the
spell-damage line forms of `SpellProjectile.cs` 810-845, which `FightScenario` and `FightTally` state as constants and parse) and
nothing else was copied.
