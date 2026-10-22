# OpenAC as a second source for M4: the map

Read 2026-10-19 against OpenAC v0.1.25 @6db05a2 (`vendor-eval/OpenAC`, tag `watch-2026-10-19`;
[foundation-openac.md](foundation-openac.md) §6). Status: done; the M4 planner re-reads §1 and §2 at each wave
boundary and the watch (foundation-openac.md §6.4) refreshes §2. **Watch of the M4 planning session (2026-10-19,
later):** the head is f22b048 (signed, `valid`; tag `watch-2026-10-19b`), 23 commits past 6db05a2; no file under
`src/AcDream.Core.Net/**` changed, so every §1 row stands and new ports pin f22b048 (m4-plan.md D7); #218 was reverted
and replaced (the §2 row below, D16); the new issues #227 (luminance auras), #225 (the shield slot with dual wield) and
#229 (chat tags) name no M4 feature.

[m4-fights-and-loot.md](../milestones/m4-fights-and-loot.md) §5 reads ACE alone and says every message order comes
from ACE's code, not a capture. OpenAC is a from-scratch client that plays exactly M4's features against ACE, so it is
the second, independent reading of the same wire and the same server behaviour. This page maps M4's own gap rows
("not sent", "not decoded", "no model" in §5's "Lifestone today" column) to the OpenAC files that cover them, and
OpenAC's bug history to M4's gates. Paths are under `vendor-eval/OpenAC/src/` unless they start with `tests/` or
`docs/`.

**How a row is used** (the rules are foundation-openac.md §6.2; a slice brief cites that section, not this page):

- **port**: the file is read, copied under docs/third-party/net.md's N-process (port header with pin @6db05a2 and the
  change list, `TryParse` over `WireReader`, decoupled from `AcDream.Core` types, named on the net page with the MIT
  text), and checked against ACE's writer or reader byte for byte. `tests/Lifestone.Net.Tests/Hygiene/NetProvenanceTests.cs`
  learns the second pin first (M4 §6.1 Q7).
- **re-express**: the file is read for the behaviour, Lifestone writes its own, and the doc page cites the OpenAC file
  and ACE's rule. Every `Retail*` file is in this class: its method names are retail's and it cites no address, so
  Lifestone's version cites the retail function and its 11.6096 address (CLAUDE.md, "Data and sources").
- **reference**: read for shape only; nothing on the page names it as a source of code.

What `src/Lifestone.Net/Messages/` already holds is not listed (UseWithTarget 0x0035 is not in it: 3.1 s0 wrote it from ACE's `GameActionUseWithTarget.cs:10-15` reader, not from OpenAC, and retail's `CM_Inventory::Event_UseWithTargetEvent` is 11.6096 0x006AD3E0, 11.4186 0x006AC480): Use 0x0036,
PutItemInContainer 0x0019, GetAndWieldItem 0x001A, StackableMerge 0x0054, the casts 0x0048 and 0x004A, SetStackSize
0197, InventoryRemoveObject 0024, the 0022 / 0023 / 02CE / 02DA updates, 00A0, 01C7, 01C0, the 01AC-01B4 combat
events, the PrivateUpdates and PlayerDescription. M4 §5's "Lifestone today" column governs; where it and this page
disagree, the code does.

## 1. The gap rows

| M4 row (DoD, §5) | Message | OpenAC file (@6db05a2) | Verdict | Read it for |
|---|---|---|---|---|
| DoD 3, 4 (§5.2): melee, missile, stop are not sent | GA 0x0008, 0x000A, 0x01B7 | `AcDream.Core.Net/Messages/AttackTargetRequest.cs` | port | the three layouts beside ACE's `GameActionTargetedMeleeAttack`, `TargetedMissileAttack`, `CancelAttack` readers |
| DoD 3 (§5.2): 01A7 and 01B8 are not decoded | GE 01A7 AttackDone, 01B8 CombatCommenceAttack | `AcDream.Core.Net/Messages/GameEvents.cs` (`ParseAttackDone`), `GameEventType.cs`, `AcDream.Core.Net/GameEventWiring.cs`; `AcDream.Runtime/Gameplay/RuntimeCombatAttackState.cs`, `AcDream.Core/Combat/CombatState.cs` | port the parse; re-express the state | how a second client sequences 01B8, the swing and 01A7 {0} against 01A7 {0x0036}, and when it repeats |
| DoD 6 (§5.4): 0196 is not decoded | GE 0196 ViewContents | `AcDream.Core.Net/Messages/GameEvents.cs` (`ParseViewContents`); `AcDream.Core/Items/ExternalContainerState.cs`, `AcDream.Runtime/Gameplay/RuntimeItemInteraction.cs` | port the parse; re-express the model | the open-container model, and the 2026-10-10 fix that a carried spell focus no longer blocks inventory completeness (#226) |
| DoD 7 (§5.5): drop, split, give have no sender | GA 0x001B, 0x0055, 0x0056, 0x019B, 0x00CD | `AcDream.Core.Net/Messages/InventoryActions.cs` | port | the layouts beside ACE's `GameActionDropItem`, `StackableSplit*`, `GiveObjectRequest` readers |
| DoD 11 (§5.6): appraisal | GA 0x00C8; GE 00C9 | `AcDream.Core.Net/Messages/AppraiseRequest.cs`, `AppraiseInfoParser.cs`, `GameEvents.cs` (the 00C9 header) | port | the 00C9 flag-driven property blocks beside ACE's `GameEventIdentifyObjectResponse` |
| DoD 12 (§5.8): 0062 falls to Unknown; no vendor model; buy and sell not sent | GE 0062 ApproachVendor; GA 0x005F, 0x0060 | `AcDream.Core.Net/Messages/VendorApproach.cs`, `VendorRequests.cs`; `AcDream.Runtime/Gameplay/RuntimeVendorAutomation.cs`, `RuntimeVendorRangeQuery.cs`, `VendorShopItemMaterializer.cs` | port the codecs; re-express the model | the stock model, the UseDone-before-0062 order §5.8 derives from the tick, the zero-stock-is-unlimited rule (#218) and the vendor range fix (0.1.15) |
| DoD 14 (§5.10): auto-buff | none to port (the casts exist) | `AcDream.Core/Spells/RetailSpellFormula.cs`, `RetailSpellTargetPolicy.cs`; `AcDream.Runtime/Gameplay/RuntimeSpellCastState.cs` | re-express, with retail cites | the cast-recovery fix (0.1.23, #201) against M3's `BuffCycle` |
| every wave (§5's caveat): a GE can name an object before its F745 arrives | | `AcDream.Runtime/Entities/RuntimeEntityObjectLifetime.cs`, `AcDream.Runtime/GameRuntime.cs`, `AcDream.Runtime/Gameplay/RuntimeCombatAttackState.cs` | reference | how OpenAC parks an event for an object it has not created yet, beside the reducer's own park-or-tolerate rule |
| DoD 8, 10 (§5.10): the loops | | `tests/AcDream.Headless.Tests/*LiveTests.cs`, `AcDream.Headless/Hosting/HeadlessLogoutAutomation.cs` | reference | the shape of a live scenario against ACE (login, act, assert on the session's state, log off), beside Lifestone's own `login-bsd` legs |

## 2. OpenAC's bugs as gate checks

Issues of eriknihlen/OpenAC that name an M4 feature, read from the issue list on 2026-10-19 (titles paraphrased). Each
is a check that Lifestone does the retail thing, run in the wave's gate; none is a port. A failed check becomes a
fix slice or a named gap in the audit table.

| Issue | What OpenAC got wrong, then fixed | M4 row | The check |
|---|---|---|---|
| #171 | a combat-mode change acted before the body was in position | DoD 3, 5 | the stance change waits for the motion before the first attack is accepted (the polish pass P3 rule) |
| #180 | combat mode could not be entered with a bow, crossbow or atlatl | DoD 4 | the fighter enters Missile with each launcher |
| #174 | with the combat bar below full speed, no attack with a shield and a thrown weapon | DoD 4 | thrown weapon plus shield attacks at every power |
| #173 | thrown weapons showed no range | DoD 4 | the range reads for thrown weapons as for bows |
| #198 | combat text omitted some damage output | DoD 3, 5 | every 01AC-01B4 line reaches the chat |
| #220 | next-monster cycling was limited by elevation | DoD 5 | `NextMonster` cycles by radar range, not height |
| #219 | items stayed on the hotbar after leaving the inventory | DoD 7 | a sold, dropped or given item leaves the shortcut bar (applies since M4 3.2: the h1 model and the h2 panel; the row is evicted, one RemoveShortcut is sent, and the slot draws empty; pinned by `GameDragTests.OpenAC_219_an_item_that_leaves_the_inventory_leaves_the_hotbar_and_the_slots_are_drag_sources_and_targets` (the 18 slots are drag sources and targets now) and `GameToolbarTests.A_missing_object_comes_back_unghosted_and_an_item_that_leaves_the_inventory_leaves_the_bar`) |
| #221 | dragging the main pack into the salvage bar took every pack | DoD 7, 13 | a pack move moves that pack (`GameDragTests.OpenAC_221_*`: a side pack dragged to the main pack sends one `MoveToPack` for that pack; the player's own cell is not draggable; a press on a side pack's cell also opens it, so a drop back on that list is refused as "in itself") |
| #226 | a carried spell focus blocked inventory completeness | DoD 6 | the pack contents are complete with a focus carried |
| #206 | a quick press-and-drag picked up the item that was pressed | DoD 13 (Q2) | a drag starts from the pressed item (`GameDragTests.OpenAC_206_*`) |
| #146 | a container did not show its full icon when an item hovered over it | DoD 13 | the hover highlight on a pack (`GameDragTests.OpenAC_146_*`: side-pack cell, player's cell, the container window's own cell, paperdoll slot accept and reject) |
| #214 | logging out near buildings or trees logged back in on top of them | DoD 10 | the loops' logout and login near Holtburg's buildings |
| #217 | no confirmation before destroying an item to charge a mana stone | DoD 7 | a destroying use asks first (if the item move set includes it; else a named gap) |
| #139 | lifestone-recall arrival acted before the destination's collision was back | DoD 10 | the hunt loop's recall waits for the arrival (M3 3.2's rule) |
| #154 | the character could stay stuck in the lifestone after a recall | DoD 10 | the loop moves off the lifestone after every recall |
| #185 | run speed did not change in real time | DoD 10 | a run buff or burden change takes effect on the next frame |
| #218 | a shop listing with zero stock was read as unlimited (087f346), then reverted on 2026-10-19 (1c08ff7) for "list every shop row at open, whatever its amount" (38ef960) | DoD 12 | the stock model lists every CreateList row the 0062 carries and reads ACE's packed stack 0xFFFFFF as unlimited (`W/Vendor.cs`, M4 §5.8); never 0 (m4-plan.md D16); checked by `VendorStockTests.OpenAC_218_*` (M4 3.2 s1) |
| #143 | the mana-cost text had a typo | DoD 11 | the appraisal panel's mana line |
| #201 | cast recovery after a failed or interrupted cast | DoD 14 | the re-buff driver resumes after a 02C4 or a stance break |

## 3. Not taken, so a slice does not re-litigate it

- `AcDream.Runtime` and `AcDream.App` as code (foundation-openac.md §5, superseded verdict; Lifestone's game state is
  its own).
- The plugin contract, the markup UI, the render packs, the launcher, the peer hub (no plugins, ever:
  docs/automation-vision.md).
- `AcDream.Core/Physics` beyond what M1 settled from ACE (decompile-shaped comments; Lifestone's physics cites ACE and
  retail).
- Every OpenAC test fixture (dat-derived JSON, a retail trace): never imported; Lifestone's fixtures come from the
  user's own dats at test time.
- OpenAC's `Retail*` constants as pasted code: re-expressed with the retail cite, or named `TODO(...)` when the
  address is unknown.
