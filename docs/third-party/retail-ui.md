# Third-party sources: the retail user interface (M2 retail-ui track and the in-game shell)

This page covers the retail user interface as a whole: the data layer (`src/Lifestone.Data/Ui/`, `src/Lifestone.Data/CharGen/`), the element runtime, the compositor, the
text, widgets, input, dialogs and flow (`src/Lifestone.Client/RetailUi/Core`, `Rendering`, `Text`, `Widgets`, `Input`, `Dialogs`, `Flow`), the login screens
(`Screens/`), and the in-game shell (`Chat/`, `Radar/`, `Toolbar/`, `Panels/`, `Selection/`, `Options/`, `Indicators/`), with their Godot layer
(`src/Lifestone.Game/scripts/RetailUi/`) and tests ([retail-ui.md](../retail-ui.md), [retail-ui-data.md](../retail-ui-data.md)). The character creation screens
(`RetailUi/Screens/CharGen*.cs`) have their own page, [retail-ui-chargen.md](retail-ui-chargen.md). Lifestone's own panels (the tools panel and the automation
inspector, `RetailUi/Lifestone/`, retail-ui.md §22) re-express no retail function beyond the retail widgets they sit on: their layouts and words are Lifestone's.

The product code is re-expressed from the retail decompile by function; nothing is copied, no file carries a port header, and no decompiled code is pasted. Every
algorithm is cited by function name with its 11.4186 and 11.6096 addresses next to it in the source and in [retail-ui.md](../retail-ui.md) §16 (the Retail functions
tables), [retail-ui-data.md](../retail-ui-data.md) §11 and [retail-addresses-m2.md](../research/retail-addresses-m2.md) §2. Lifestone is AGPL-3.0. Nothing derived
from the user's dats or exe is committed: the tests read the dats at run time and pin counts, ids and first words only, and the review frames are gitignored.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src/ENGINE/` (`uielement_system/`, `uielement_types/`, `uielement_util_dialog/`, `render_base/`, and the other `ENGINE` and `_other/misc` files named in the tables; Hex-Rays output with PDB names) | Turbine/WB IP; reference only | commit 01ccd5be2d8fff40e25151d804fa97b3e8f27ec6, read 2026-10-12 to 2026-10-14 into session scratch | Behaviour of the element runtime (`UIElement`, `UIElementManager`, `UIRegion`, `MediaMachine`), the compositor (`SurfaceWindow`, `Graphic`), text (`UIElement_Text`, `GlyphList`, `Font`), the widgets (`UIElement_Button`, `_ListBox`, `_Menu`, `_Meter`, `_Scrollable`, `_Scrollbar`, `_Panel` and the rest of the 105 registered classes), input (`CInputManager`, `CMasterInputMap`, `UIElementManager`'s mouse and tooltip handlers) and the dialogs (`DialogFactory`, `Dialog` and its kinds). |
| Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src/GAME/` (`game_ui_misc/` and the player-system files, for example `AC/accui_misc/PlayerSystem.c`) | Turbine/WB IP; reference only | the same commit, read 2026-10-12 to 2026-10-14 | Behaviour of the login screens and flow (`UIFlow`, `gmUIFlow`, `gmDataPatchUI`, `gmDisconnectedUI`, `gmCharacterManagementUI`, the epilogue) and of the shell (`gmGamePlayUI`, `gmSmartBoxUI`, `gmSpewBoxUI`, `gmMainChatUI` and `ChatInterface`, `gmVitalsUI`, `gmRadarUI`, `gmToolbarUI`, `gmPanelUI`, `gmCharacterInfoUI`, `gmGameplayOptionsUI`, `PlayerOptionPage`, `VividTargetIndicator`, `LinkStatusHolder`). |
| Mag-nus/acclient-AI-RE, `symbol_transplant_11.6096.tsv` | Turbine/WB IP; reference only | the lead's pinned copy (owner decision D4), `docs/research/retail-addresses-m2.md` | The 11.6096 addresses cited everywhere. **The shell (G4) and the G7 panels were re-expressed from this 11.6096 pin**: their file headers cite 11.6096 addresses first, and 11.4186 addresses where the decompile has the function (the comment-only pass of wave 3 added them to the G4 shell files). A function the 11.4186 symbols lack is named so (for example `SmartBox::GetObjectBoundingBox`: 11.6096 0x00452E60 only). |
| Retail `acclient.exe` 11.6096, read-only reads of constants (owner decision D55, m2-plan.md §6.6; for example the radar blips' colours and floats) | Turbine/WB IP; reference only | the user's own copy; nothing from it is committed | The literal values the decompile does not show; each is cited at its address in the source. No code or data is copied out of it. |
| eriknihlen/OpenAC | MIT | @1ee5a08 (`vendor-eval/OpenAC`) | **Read only, as a cross-check, nothing copied:** `UiLayoutPolicy` (anchoring, `UiAnchor.cs`) and `RetailStringHash` (`StringHash.cs`; OpenAC adds the hash's bytes unsigned, retail adds them as signed `char`s, and Lifestone follows retail). |
| Chorizite/DatReaderWriter | MIT | NuGet 2.1.9 (already a project dependency) | Used, not ported: the dat records (`LayoutDesc`, `ElementDesc`, `StateDesc`, `MediaDesc`, `StringTable`, `Font`, `MasterProperty`, `CharGen`, ...), converted by `UiDescConverter` into the immutable desc model. Two places where it differs from retail's serializers are documented in retail-ui-data.md §1. |

## Where each area is re-expressed

| Area | Lifestone files | Retail functions (the full lists are in retail-ui.md §16 and retail-ui-data.md §11) |
|---|---|---|
| Desc model, anchoring, property tables, fonts, strings, cursors, key map | `Data/Ui/*` | `LayoutDesc`, `ElementDesc`, `StateDesc`, `MediaDesc`, `PropertyCollection`, `BaseProperty`, `MasterProperty`, `StringInfo`, `StringTable`, `Font` serializers; the PJW/ELF string hash; `CMasterInputMap` |
| Element runtime | `RetailUi/Core/*` | `UIElement` (state, properties, children, z order, focus, mouse and key handlers, resize and move), `UIElementManager` (`CreateElement*`, `Init`, activation, `DoVisibilityToggleAction`, cursor and tooltip), `UIRegion` and `MediaMachine` (`Update_Image`, `_Sound`, `_State`, `_Fade`, `_Anim`, `_Pause`), `ClientUISystem` (busy count, `UpdateCursorState`) |
| Compositor | `RetailUi/Rendering/*` | `SurfaceWindow` (`BlitAndColor`, `LegacyBlit`, `FillArea`, `CopyAlpha`, `DrawCharacter`, `PointTestOverlay`), `UIRegion::DrawHere`, `DrawSelf`, `DrawChildren`, `EraseSelf`, `UIObject::AddDirtyRect`, `Graphic`, `Box2D` |
| Text | `RetailUi/Text/*` | `UIElement_Text` (the whole editing surface), `GlyphList`, `Glyph`, `Font::GenerateCharacterMap`, `LinebreakUtils`, `TextTagFactory::MakeTag` (cited, not built) |
| Widgets | `RetailUi/Widgets/*` | `UIElement_Button`, `_ListBox`, `_Menu`, `_Meter`, `_Panel`, `_Scrollable`, `_Scrollbar`, `_Dragbar`, `_Resizebar`, `_GroupBox`, `_Field`, `_ColorPicker`, `_Browser` (frame only) |
| Input | `RetailUi/Input/*` | `CInputManager` and `CInputManager_WIN32`, `CMasterInputMap::Merge`, `ActionMap`, `ActionState`, `QualifiedControl`, `UIElementManager::KeyPressEvent`, `MouseDownEvent`, `MouseMoveHandler`, `StartTooltip`, `StartDragandDrop` (cited, not built) |
| Dialogs, flow and notices | `RetailUi/Dialogs/*`, `RetailUi/Flow/*` | `DialogFactory` (`MakeDialog`, `CreateDialog_`, `OpenNextDialog`, `DialogDone`, `CloseDialog`), `Dialog` and the kinds (`MessageDialog`, `Confirmation*`, `Menu`, `TextInput`, `Wait`), `UIFlow` (`QueueUIMode`, `UseNewMode`, `Update`), `gmUIFlow`, `UIGlobals::Init`, `ECM_UI::SendNotice_*` |
| Login screens | `RetailUi/Screens/` (not `CharGen*`) | `gmDataPatchUI`, `gmDisconnectedUI`, `gmCharacterManagementUI` (`RebuildCharacterList`, `UpdateButtons`, `EnterGame`), `CPlayerSystem::LogOnCharacter`, `LogOffCharacter`, `AccountHasThroneOfDestiny`, the epilogue |
| Shell: root, chat, vitals | `RetailUi/Screens/Game*`, `RetailUi/Chat/*` | `gmGamePlayUI` (`SetupChildren`, `HandleKeyPress`, `SaveScreenLayout`), `gmSmartBoxUI::PostInit`, `gmSpewBoxUI`, `gmMainChatUI` (`InitTalkFocusMenu`, `HandleSelection`), `ChatInterface`, `ClientCommunicationSystem::OnChatCommand`, `CM_Communication::Event_Talk`, `gmVitalsUI` |
| Shell: radar, toolbar, panels, indicators | `RetailUi/Radar/*`, `Toolbar/*`, `Panels/*`, `Indicators/*` | `gmRadarUI` (`DrawBlip`, `GetBlipColor`, `GetBlipShape`, `UseTime`), `CPlayerSystem::GetRadarRadius`, `ClientFellowshipSystem`, `gmToolbarUI`, `gmPanelUI`, `gmCharacterInfoUI`, `LinkStatusHolder::GetConnectionStatus` |
| Shell: selection, options, logout | `RetailUi/Selection/*`, `Options/*`, `Panels/GameLogout.cs` | `VividTargetIndicator` (`OnDraw`, `SetSelected`, `SetOnScreenColor`), `CM_Combat::Event_QueryHealth`, `CM_Item::Event_QueryItemMana`, `gmGameplayOptionsUI::ListenToElementMessage`, `PlayerOptionPage`, `UIOption_Checkbox`, `PlayerModule` options, `CM_UI::SendNotice_EndCharacterSession`, `gmGamePlayUI::RecvNotice_EndCharacterSession`, `MakeLogoutConfirmationDialog` |
| The combat-mode key (M4 4.1 v3) | `Panels/CombatToggleKey.cs` | `ClientCombatSystem::RegisterInputMaps` (11.6096 0x0056B760; 11.4186 0x0056AA20), `ClientCombatSystem::OnAction` (0x0056E640), `GetDefaultCombatMode` (0x0056C050), `ToggleCombatMode` (0x0056D610); the keymap dat (KEYMAP 0x14000000) | Behaviour re-expressed from the function names and addresses; no decompiled code and no ACE code. The key itself (action 0x1000005A, accent-grave) is read from the user's dat at run time. |
| The Godot layer | `Game/scripts/RetailUi/*` | `UiSoundAdapter` and the sound-table lookup (`ClientUISystem::GetUISoundTable`, `SoundManager::PlaySoundFromCenter`), the host's cursor and clipboard seams; the AC to Godot conversion is only in `Core/AcToGodot.cs` |

## Not from any source

The compositor's Godot upload, the staged construction of the shell over frames (`UiStagedRoot`), the layout files in Lifestone's own format
(`lifestone-layout/1`), the stand-in session screens, the placeholder labels and every Lifestone-only text are written for Lifestone. The literals that are retail's
own (strings, ids, colours) come from the user's dats at run time wherever a dat holds them; the few that the dats do not hold are named in retail-ui.md and cited at
their function.
