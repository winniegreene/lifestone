# M3 plan: lights and sound

The plan of record for [m3-lights-and-sound.md](m3-lights-and-sound.md) (the spec: goal, DoD, research). Section
numbers are stable: append, never renumber, so briefs can cite "§4.2". The process (§6) is the owner's settled one:
one fresh Opus 5.5 lead session per item, Sonnet slices of 60-120 calls under 300K of context in their own worktrees,
one Opus review per item, the live gate by the lead alone, a commit per slice through `tools/commit.ps1`. The rules
are CLAUDE.md, "Hard rules" and "Rules for agent sessions". M2's decisions are cited as "M2 D<n>" (m2-plan.md §10);
this plan's own are D1-D18 (§7).

## 0. Status

Planned 2026-10-15 on master 4de2b58. Item 1.1 (stats and the mage) is merged (2026-10-15; §8): BSD Mage exists at
level 126 with the owner's D19 template. Item 1.2 (the message set and the read models) is merged (2026-10-16; §8): the
M3 messages decoded and applied to the enchantment, spellbook, inventory and combat read models, the Cast,
SetCombatMode, Wield, Unwield and MoveToPack actions, the live-shaped rehearsal. Item 1.3 (sound on) is merged
(2026-10-16; §8): the server's F750 and F755 on the avatars, the hook sink with the sound table and the pan, animated
scenery's hooks, UI sounds, the region's ambient sound and EA60, the record-audio check and the login-check sound fields
(report-only). Item 1.4 (gear, wield and the first casts) is merged (2026-10-16; §8): the
kit for the account's formulas and the foci formula, SpawnGear with its top-up and merge, `--scenario=gear` (the wand
wielded, five armour pieces worn), the first casts live in Creature, Life and Item. Item 2.1 (the buff cycle and the cast on screen) is merged (2026-10-16; §8, §4.5): BSD Mage buffed in all three schools at VII by `--scenario=buff` and the `buff` leg, the cast's gestures, scripts and sounds on the player, the real expiry live. Items 2.2 and 2.3 (the panels, run as one item) are merged (2026-10-16; §8, §4.6): the retail panels live on BSD Mage: attributes and skills with raise and train buttons (D29), the effects pages with live countdowns, vitae, the three indicators, the inventory with the item widgets, the paperdoll with its 3D figure, the spellbook and components; every other page answers "Not in Lifestone M3". Next: the checkpoint (D1, waiting on the owner); then item 3.1 from `cache/m3-w3-3-1-lead-prompt.md`. Budget: $1,250 API-equivalent ceiling for the whole milestone including planning (D5);
the estimate is $1,215 (§3), planning about $125 of it. The checkpoint after wave 2 pings the owner with the numbers
(D1).

## 1. The read map (master 4de2b58; read by section or line range, never a whole file)

| Area | Read | What it tells |
|---|---|---|
| Rules | CLAUDE.md, "Hard rules" and "Rules for agent sessions" | everything a brief must not restate |
| The spec | m3-lights-and-sound.md §2 (the DoD items of the item), §5.1-§5.7 (the research the item needs) | what to build and the pointers |
| M2's process | m2-plan.md §6.4 (cost rules 1-9, 1365), §6.6 (G4 as run: the slicing pattern and the agent sizes, 1421-1489), §10 (M2 D1-D66, 1676) | how slices were sized and what was decided |
| M2's gaps | m2-log-in.md, Status, the DoD audit table (1081-1099): each row's "Open gaps" | what M3 inherits (§9) |
| Spell data | `src/Lifestone.Data/Magic/` (`SpellTableData.cs`, `SpellInfo.cs` with `Power` and `Duration`, `SpellFormulaRules` with `CustomizeForAccount`, `CastGesturePlanner`); docs/spell-visuals.md §1 (11), §6 (184), §7 (202) | 6,266 spells, 139 equipment sets, 163 components; formulas, their per-account taper customisation, gestures; no StatMod magnitudes, no XP table |
| Spell visuals | `src/Lifestone.Data/Effects/EffectObject.cs` (`PlayScript` 174, `LightsOn` 484), `src/Lifestone.Client/Effects/`; docs/spell-visuals.md §2-§5 (61-151); docs/spell-viewer.md §1-§7 | the M1 showcase plays scripts and particles on an effect object; collision is simulated |
| The Action API | `src/Lifestone.Client/Session/Contracts/IActionApi.cs` (28-103), `ActionKind.cs`, `Session/Actions/Trackers/UseTracker.cs` (the tracker pattern, 97 lines), `PlayerTools.cs` (86-178); `src/Lifestone.Net/Session/IAcSession.cs` (93-171), `SessionMachine.World.cs` (114); docs/session.md §7 (295), §9 (379), §17 (763) | what exists (login, characters, EnterWorld, Use, QueryHealth, Talk, Tell, LogOff, Quit, movement, the tools) and the shape of a new action |
| The reducer and qualities | `GameStateReducer.Chat.cs` (`OnPlayerDescription` 103, `OnPrivateUpdate` 189), `Session/Contracts/PlayerQualities.cs` (enchantments 134, attributes and skills 178-199), `EnchantmentRules.cs`; `src/Lifestone.Net/Messages/Self/PlayerDescriptionMsg.cs` (spellbook, bars, shortcuts, components, pack and equipped: 59-131); docs/session.md §5 (184), §15 (639); docs/retail-ui.md §21.3 (1207) | the description's registry applied to the vitals; the dropped lists; `TODO(g4-enchantment-updates)` |
| The wire | `src/Lifestone.Net/Messages/Opcodes.cs`, `Dispatch/InboundDispatcher.cs` (41-391, unknown at 402); docs/protocol.md §5.1 (393-445), §5.3 WeenieDesc (520-560); the spec §5.4 | what is decoded, and the M3 message set with ACE's writers |
| Server objects | `src/Lifestone.Client/Avatars/AvatarSet.cs` (`EffectEvents` 284, the sound origin 802-835), `SessionAvatarAdapter.cs` (55-74), `src/Lifestone.Data/Motion/`; docs/avatars.md §9 (577), §14 (855), §17 (1024) | bodies, motions, MoveTo and targets; what the adapter maps and does not (F750, F755) |
| The shell's panels | `src/Lifestone.Client/RetailUi/Screens/GamePlayScreen.cs` (`RegisterClasses` 78-110, the build 229-294), `Panels/GamePanelHost.cs` (`Pages` 45-63, `Open` 215), `Core/UiClassRegistry.cs` (161-196, 280-290); docs/retail-ui.md §21 (1098; the pages table 1293-1303), §11 (362), §15 (523); docs/retail-ui-data.md §8 (299), §10 (359) | the 16 pages, the 13 panel classes (all plain stand-ins), `NotInM2`, the loud TODOs, CharGen data |
| Inventory and equipment | docs/protocol.md §5.3 (520-560: Container, Wielder, WieldedLocation, Burden, Spell); docs/avatars.md §2-§4, §10, §13; `src/Lifestone.Game/scripts/CreatureMode/CreatureModeView.cs` | the wire pieces; a paperdoll reuses the creature-mode view |
| Experience and the mod | `server/mod/README.md` (verbs 52-66), `server/mod/Shared/ToolRequests.cs`, `ToolRequestParser.cs`, `Lifestone.ServerMod/Verbs/XpVerb.cs`, `QuestVerb.cs`, `CharPurgeRules.cs` (65); `src/Lifestone.Client/Session/StandIn/StandInWorld.cs` | the verb pattern, the protected names, the stand-in's answers |
| Death and lifestones | `WorldContentCensus.cs` (62-102), `EnchantmentRules.cs` (76); the spec §5.4 (death) | pieces only |
| Sound | `src/Lifestone.Data/Audio/`, `src/Lifestone.Client/Audio/` (`IEffectEventSink.cs`, `SoundHookRouter.cs`), `scripts/Audio/AudioService.cs`; docs/portal-space.md §7 (175), §11.3 (346), §15 items 9-10; m2-log-in.md §4.10 (519); the spec §5.5 | the engine and the rules; the six unwired points |
| Lights | `src/Lifestone.Client/Interiors/ObjectLightSets.cs` (11-70), `CellObjectLighting.cs`, `CellStaticLighting.cs`; `scripts/World/ServerObjects/ObjectLightTable.cs`, `scripts/Render/SurfaceLightingCode.cs` (100, 237-300), `scripts/Player/PlayerView.cs` (358), `ServerObjectsLayer.cs` (462); docs/interiors.md §4.3-4.4; docs/objects.md (451, 463); the spec §5.6 | the static lights as drawn; where a dynamic list plugs in |
| Checks and loops | `tools/slice-check.ps1` (params 41-53), `tools/smoke.ps1` (the steps 194-197); docs/session.md §20 (884), §24, §25, §27 (1184), §28 (1290); `src/Lifestone.Client/Session/Scenarios/` (`LoginScenario.cs`, `LoginCheckScenario.cs`, `HeadlessCharacter.cs`, `RoutePlay.cs`), `tests/Lifestone.Loopback.Tests/`, `Session/StandIn/` (docs/session.md §16, 733), `scripts/Session/PlayChecks.cs` | the headless host, the legs, the verdict line, the stand-in |
| Retail research | `cache/research/symbol_transplant_11.6096.tsv` (names to addresses), `cache/research/decomp-11.4186/func_index.tsv` (by name) and the `.c` files, only for a question the docs cannot answer; re-express, never paste | |

## 2. Seams and owners

One owner per file set; another owner's need goes under "For the lead" in a reply, never as an edit. Within an item,
a slice may edit only the files its §4 row names.

| File set | Owner | Contract the others use |
|---|---|---|
| `src/Lifestone.Net/Messages/**`, `Dispatch/**`, `Session/IAcSession.cs`, `SessionMachine.*.cs` | the wire slices (1.1 s1a, 1.2 s0-s4) | `IInboundMessageHandler` callbacks (append only; 1.2 s0 adds every M3 stub at once); `IAcSession.Send*`; every codec cites ACE's writer and the retail dispatcher |
| `src/Lifestone.Client/Session/**` (reducer, contracts, actions, trackers) | 1.1 s1a-s1b, 1.2 | `IGameSessionView`, `IActionApi`, `PlayerTools`, the events of docs/session.md §5; one reducer partial per area (`GameStateReducer.Enchantments.cs`, `.Inventory.cs`, `.Combat.cs`), laid down by 1.2 s0 |
| `src/Lifestone.Client/Session/StandIn/**`, `scripts/Session/PlayChecks.cs` | one slice per item (named in §4); within an item the stand-in is never edited by two slices at once | `StandInWorld`'s answers, `StandInVisit`, the play-checks list |
| `src/Lifestone.Client/Session/Scenarios/**`, `Session/Mage/**`, `src/Lifestone.Headless/**` | the item's scenario slice | `IScenario`, the leg interface, the `--scenario` list, the verdict line (additive fields) |
| `server/mod/**` | 1.1 s2a only in wave 1 (one install, D6) | `ToolsWire` verbs and replies; `CharPurgeRules.Graduated` |
| `src/Lifestone.Client/Avatars/**`, `Effects/**`, `Audio/**`; `scripts/Audio/**`, `scripts/Session/PlayMode.cs`, `scripts/World/ServerObjects/**` | 1.3 (sound); 2.1 (cast visuals); 3.1 (projectiles) | `IEffectEventSink` (gains the sound table in 1.3 s1), `ISoundSink`, `AvatarSet.EffectEvents`, `SessionAvatarAdapter` |
| `src/Lifestone.Client/RetailUi/**`, `scripts/RetailUi/**` | 2.2, 2.3, 3.2 | `GamePanelHost.SetPageHandler(panelId, open)`, `NotInM3`; a panel reads `IGameSessionView` and acts through `IActionApi` only |
| `tools/smoke.ps1`, `tools/slice-check.ps1` | the item's check slice | the step list and `$backendSteps` |
| `src/Lifestone.Data/CharGen/**` (XpTable), `Data/Magic/**`, `Data/Audio/**`, `Data/World/**` (region sound) | 1.1 s0, 1.3 s2b | immutable projections; a test pins each dat fact |
| `src/Lifestone.Client/Interiors/**`, `scripts/Render/**`, `scripts/World/Interiors/**`, `scripts/Player/PlayerView.cs` | 4.1 | `ObjectLightSets.Select`, the `ObjectLightTable` rows, the `SurfaceLightingCode` variants |
| `src/Lifestone.Client/Options/OptionCatalog.cs` | every slice registers its options there | append only |
| `docs/**` | the item's last slice and the lead's doc pass; lanes write doc notes under `cache/m3/<item>/` (gitignored; the lead copies what matters into the docs before removing a worktree) | stable section numbers; append |

## 3. The waves

A wave is two to four items with a theme and a gate; an item is one fresh lead session; a slice is one Sonnet agent.
Shares are of a week (1% is about $22.5); the figures are estimates against M2's measured sizes (m2-plan §6.6).

| Wave | Item | Theme and content | Gate | Item | Lead |
|---|---|---|---|---|---|
| 1 | 1.1 | **Stats and the mage:** `tools/commit.ps1`, the worktree cleanup (D7), the XpTable decoder and the dat pins, the raise and train actions, the mod verbs (`spell learn`, `item`, the protected name), the mage build | loopback: `mage=ready level=126`; the mod installed (D6) | $90 | $20 |
| 1 | 1.2 | **The message set and the read models:** the hooks first, then enchantments live and the spellbook, inventory and wield, combat and death, the cast and combat-mode actions, the live-shaped rehearsal | codec, reducer and tracker tests; `--play-checks` over the recorded description; the arrival probe | $100 | $25 |
| 1 | 1.3 | **Sound on:** F750 and F755 to the avatars, the hook sink with the sound table, the pan fix, animated scenery, UI sounds, the region's ambient sound, the audio check | `record-audio` non-silent; live `login-check` counters | $55 | $15 |
| 1 | 1.4 | **Gear, wield and the first casts:** kits (account-customised components), `SpawnGear`, Wield, one buff per school live, `--scenario=gear` | loopback: `gear=wielded`, `CastLiveTests` | $70 | $20 |
| 2 | 2.1 | **The buff cycle and the cast on screen:** the arena profile, the level-by-chance rule, expiry and re-buff, `--scenario=buff`, the `buff` leg; the caster's gestures, scripts and sounds on the player | live `buff` leg: `buffs=n/n rebuffed=yes`; a frame of a cast | $70 | $20 |
| 2 | 2.2 | **Panels A:** attributes, skills, character information live, beneficial and harmful effects, vitae | client tests, play-checks, frames | $60 | $15 |
| 2 | 2.3 | **Panels B:** spellbook, components, inventory, backpack, paperdoll | client tests, play-checks, frames | $80 | $20 |
| — | checkpoint | **"Buffed, stats shown, sound on."** The wave-2 lead reports the spend and the state and pings the owner (D1); the owner's visible review; the owner decides any cut | | | |
| 3 | 3.1 | **BSD and the fight loop:** the den mapped by generator (the drudge and virindi portion avoided, D12), the Places row, combat mode, targeting, Flame Bolt, projectiles drawn, the combat chat, kill XP; `--scenario=fight`, the `bsd-fight` leg | live `bsd-fight`: `kills>=1 damage_dealt>0`; the frame rule in the fight | $120 | $25 |
| 3 | 3.2 | **Damage taken, death and re-gear:** the vitals under attack, death and vitae, the lifestone respawn through portal space, the re-gear loop; the combat panel, spellcasting and power bar; `--scenario=death-and-regear`, the `bsd-death` leg | live `bsd-death`: `died=1 respawn=lifestone gear_restored=yes` | $110 | $25 |
| 4 | 4.1 | **Lights:** the dynamic list, the viewer light, the projectile and SetLight links, the cell glow (spec §5.6, sized per piece there) | `DynamicLightsTests`; a judged frame of a dynamic light on a BSD wall | $60 | $15 |
| 4 | 4.2 | **The end:** cluster reviews (one per DoD cluster: 1-2, 3-5, 6-7, 8-11, 12, 13-14, 15), the audit table, the M3 hero video with sound, the private push on the owner's word | the audit table complete; the video | $60 | $15 |
| | | **Total** | | $875 | $215 |

Build $1,090 (about 48% of a week) plus planning $125: **$1,215, against the $1,250 ceiling (D5).** The margin is
$35, so (D5 as refined after the plan review) a lead asks before exceeding its item's estimate by 20%, and the
checkpoint after wave 2 re-estimates waves 3 and 4 against what is left; 3.1 and 3.2 already carry the "twice" of an
item that needs live trial and error against ACE (M2's 5b).

Order, all serial: 1.1 → 1.2 → 1.3 → 1.4 (1.3 and 1.4 both touch the stand-in and the play checks, so they do not run
as parallel leads). Wave 2: 2.1 first (it needs 1.2 and 1.4), then 2.2 and 2.3 (disjoint panel files: may run as
lanes of one item). Wave 3: 3.1 then 3.2. Wave 4: 4.1 then 4.2. Each later wave is sliced by its first lead at the wave
boundary (P8, §6), in the shape of §4, with at most four owner questions in one call.

## 4. Wave 1's slice tables

Every slice: a fresh Sonnet 5.5 agent, 60-120 calls, under 300K of context, in its own worktree cut from the item
branch; at most four files to create and eight to edit; one acceptance command (`slice-check.ps1 -MinTests <count>
-Steps <steps> -Rerun`), one Godot step at most; the handoff file `docs/handoff-m3-<item>.md` in the branch (removed
before the merge). A new judge or verdict field is report-only in the slice that adds it. The test baseline is the
count `slice-check.ps1` reports on master at the item's start (at least 6,311). "Read" lists at most five pointers
beyond CLAUDE.md's rules section and the slice's own row.

### 4.1 Item 1.1 Stats and the mage (branch `m3/1-1`)

Precondition: the backend down; D3, D6 and D7 stand. Slices run in the order listed; none in parallel.

| Slice | Scope | Files (create / edit) | Read | Acceptance |
|---|---|---|---|---|
| s0 tools, cleanup, the dat pins | `tools/commit.ps1` (sets TZ=UTC, uses the checkout's identity, `-Message`, refuses any push); the 18 M2 worktrees removed and their `m2/*` branches deleted (D7; after `git branch --merged master` confirms all 18); `XpTableData` for 0x0E000018 (the six lists) with a dat test printing `Levels[126]` and the credit sum; a dat test pinning, for the Aluvian heritage, the seven skill costs (ids 6, 14, 16, 31-34) and, for every spell of the spec §5.2 lines, its id by name, its `Power` and its `Duration`; a dat test saying which of the 10 projectile Setups of `ace-world-spell-visuals.json` carry a `ModelLight` (for DoD 14) | create `tools/commit.ps1`, `src/Lifestone.Data/CharGen/XpTableData.cs`, `tests/Lifestone.Data.Tests/CharGen/XpTableTests.cs`, `tests/Lifestone.Data.Tests/Magic/BsdProfileSpellsTests.cs`; edit `tests/Lifestone.Data.Tests/CharGen/CharGenDataTests.cs`, `tests/Lifestone.Data.Tests/Models/ModelAssemblyTests.cs` | spec §5.2, §5.3, §5.6 (the unknowns); `SkillTableData.cs`; docs/retail-ui-data.md §8 (299); `ModelAssemblyTests.cs:112` | `slice-check -MinTests <base>`; the pinned numbers (costs, XP, credits, powers, durations, lit projectiles) in the handoff: s3 and D14 depend on them |
| s1a the raise wire | `ActionKind` RaiseAttribute, RaiseVital, RaiseSkill, TrainSkill; `IActionApi.Raise*`, `TrainSkill` (declared; the trackers come in s1b, so the Action API answers `NotImplemented` until then); `IAcSession.SendRaise*`, `SendTrainSkill` and the codecs (0x0044-0x0047, citing ACE's handlers); net tests | edit `ActionKind.cs`, `IActionApi.cs`, `IAcSession.cs`, `SessionMachine.World.cs`, `Net/Messages/Actions/*Codecs.cs`; create the net tests | docs/session.md §7 (295), §17 (763); spec §5.4 (the actions) | net tests; the build green |
| s1b the raise trackers and the state | the four trackers on the `UseTracker` pattern, completing on the stat's private update (0x02E3, 0x02E7, 0x02DD) and the credits' 0x02CD; `PlayerQualities.Level`, `AvailableExperience`, `TotalExperience`, `AvailableSkillCredits` read from the private updates; the stand-in answers a raise; client tests; a `--play-checks` raise | edit `Session/Actions/**` (the trackers, the Action API's observation list), `GameStateReducer.Chat.cs`, `PlayerQualities.cs`, `StandIn/StandInWorld.cs`, `scripts/Session/PlayChecks.cs`; tests | `UseTracker.cs`; docs/session.md §15 (639); spec §5.3 | client tests; `--play-checks` with the raise |
| s2a the mod verbs | `spell learn <id>[,<id>...]` over ACE's `LearnSpellWithNetworking`; `item <wcid> [<stack>]` over the `HandleCI` path (`GetWeenieForCreate`, `CreateObjectForCommand`, `SetStackSize`, `TryCreateInInventoryWithNetworking`; the audit broadcast skipped as `xp` does; AGPL port header; a `docs/third-party/` row); "BSD Mage" in `CharPurgeRules.Graduated`; the README's verb rows; `ModVerbsLiveTests` rows for the lead | edit `server/mod/**` (Shared, Verbs, routing, README), `docs/third-party/server-mod.md` (or the file that lists the mod's ports); tests | server/mod/README.md (52-66); `XpVerb.cs`; `CharPurgeRules.cs`; spec §5.2 (the spellbook), §5.3 | mod tests; then the lead installs with `setup-server.ps1 -ModOnly -ModSource <worktree>\server\mod` (D6) and runs the live verb tests |
| s2b the client side of the verbs | `PlayerTools.LearnSpells`, `SpawnItem` with typed results; `ActionKind` ToolSpell, ToolItem; "BSD Mage" in `StandInCharPurge` (`CharPurgeRulesTests` runs the mod's and the stand-in's tables); the stand-in answers both verbs; client tests | edit `PlayerTools.cs`, `ToolResults.cs`, `ActionKind.cs`, `StandIn/StandInWorld.cs`, `StandIn/StandInCharPurge.cs`; tests | docs/session.md §9 (379); s2a's handoff (the wire shapes) | client tests |
| s3 the mage build | `ArenaProfile` (BSD: the spell lines of spec §5.2 by name with the ids, powers and durations s0 pinned; the war line; the target wcids 1628 and 1629) as data in `Session/Mage/`; `MageTemplate` (the creation intent of spec §5.3 and the spending plan from the XpTable: attributes to the template's values, the four magic skills trained first with the credits, then skills by priority); `MageBuildScenario` (`--scenario=mage-build`: find "BSD Mage" or create it, `xp` to level 126, train, raise per plan, learn the profile's spells; a ready mage is verified by the mod's `spell learn` replies (already known) and the level; idempotent; never touches another character); `MageBuildLoopbackTests` | create `Session/Mage/ArenaProfile.cs`, `Session/Mage/MageTemplate.cs`, `Session/Scenarios/MageBuildScenario.cs`, the loopback test; edit `Headless/Program.cs` | `HeadlessCharacter.cs`, `LoginScenario.cs` (236); spec §5.3; s0's handoff | loopback (the lead): `mage=ready level=126 template=ok` |
| s4 check and docs | `login-check` gains `level=` and `mage=` (report-only); docs/session.md §29 (the mage, the raise actions) and §9 (the verbs); the handoff | edit `LoginCheckScenario.cs`, docs/session.md | docs/session.md §27 (1184); the item's handoff | smoke `-Backend auto`; then the item's gates (§5) |

### 4.2 Item 1.2 The message set and the read models (branch `m3/1-2`; after 1.1)

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 the hooks | every M3 message of spec §5.4 named in `Opcodes.cs` (GM 0024, 0197, 019E, 02CE, 02DA, EA60; GE 0022, 0023, 019A, 00A0, 01AC, 01AD, 01B1-01B4, 02C1-02C8, 0312), dispatched to new `IInboundMessageHandler` stubs that the reducer takes as "decoded, not yet applied" (counted, not unknown); one reducer partial per area (`GameStateReducer.Enchantments.cs`, `.Inventory.cs`, `.Combat.cs`) and one events file per area, empty but compiled; codec tests for the field layouts | edit `Opcodes.cs`, `Dispatch/InboundDispatcher.cs`, `IInboundMessageHandler.cs`; create `Net/Messages/{Magic,Inventory,Combat}/*Codec.cs`, the three reducer partials, the events files, codec tests | docs/protocol.md §5.1 (393); spec §5.4; `PlayerDescriptionCodec.cs` (the style) | codec tests; the build green; the session's unknown count for these codes is 0 | none (first) |
| s1 enchantments and the spellbook | 02C2, 02C3, 02C7, 02C8, 02C6, 0312 applied to `PlayerQualities.Enchantments` (start time and duration kept, expiry on the session clock); 02C1 and the description's spellbook, spell bars and desired components kept in a `Spellbook` read model; `EnchantmentChanged`, `SpellbookChanged` events; closes `TODO(g4-enchantment-updates)` | edit `GameStateReducer.Enchantments.cs`, the enchantment and spellbook events, `PlayerQualities.cs`, `Session/Contracts/Spellbook.cs` (create); tests | docs/session.md §15 (639); `EnchantmentRules.cs`; spec §5.4 | reducer tests over scripted messages | with s2, s3 (disjoint partials) |
| s2 inventory | 0024, 0197, 02CE, 02DA, 0022, 0023, 019A, 00A0 and the description's `Inventory` and `Equipped` applied to a `PlayerInventory` read model (the pack, the equipped items by location, burden); `InventoryChanged` event | edit `GameStateReducer.Inventory.cs`, the inventory events; create `Session/Contracts/PlayerInventory.cs`; tests | docs/protocol.md §5.3 (520); `PlayerDescriptionMsg.cs` (127-131); spec §5.4 | reducer tests | with s1, s3 |
| s3 combat and death | 01B1-01B4, 01AD, 01AC, 019E applied: `CombatEvent`, `PlayerDied` events; the combat-mode property read (`PlayerQualities.CombatMode`); the chat lines the retail client composes for the four notifications, re-expressed from `ClientCombatSystem`'s handlers (11.6096 addresses from the symbol table; one slice, since the strings are the dats' string table) | edit `GameStateReducer.Combat.cs`, the combat events, `PlayerQualities.cs` (the combat-mode line only, under a named seam with s1), the chat line writer; tests | spec §5.4; docs/session.md §5 (184) | reducer tests | with s1, s2 |
| s4 the actions | `Cast(spellId)` (0x0048), `Cast(spellId, target)` (0x004A): Confirmed on the enchantment add (a self spell) or the projectile's CreateObject from the player (a war spell), ServerError on a fizzle (0x028A 0x0402) or a refusal (UseDone 0x0400, 0x0401), 10 s timeout; `SetCombatMode(mode)` (0x0053) completing on the player's CombatMode update; `Wield(guid, location)`, `Unwield(guid)`, `MoveToPack(guid)` (0x001A, 0x0019) completing on the inventory message or 0x00A0; lanes and limits documented (M2 D49's style); the stand-in answers every one | edit `ActionKind.cs`, `IActionApi.cs`, `Session/Actions/**`, `IAcSession.cs`, `SessionMachine.World.cs`, `Net/Messages/Actions/*Codecs.cs`, `StandIn/StandInWorld.cs`; tests | docs/session.md §7, §17; `UseTracker.cs`; spec §5.4 (the actions) | tests; `--play-checks`: a stand-in cast, a wield and a combat-mode change complete | after s1-s3 |
| s5 the rehearsal | the lead records one headless `login` of `Lifestone Smoke` (graduated: 138 enchantments; the character is only logged in) under `cache/sessions/`; the stand-in loads that PlayerDescription frame when the file exists (a committed loader; the test skips without it; M2 D14: never committed); `HoldEntry` released 0.2 s after the list; `SessionJitWarmUp` covers the new handlers; the arrival warm-up checklist applied | edit `StandIn/StandInVisit.cs`, `scripts/Session/PlayChecks.cs`, `SessionJitWarmUp.cs`; tests | docs/session.md §14 (600), §27 (1184); §5 below | `--play-checks` green over the recorded frame; the arrival probe (§5) | after s4 |

### 4.3 Item 1.3 Sound on (branch `m3/1-3`; after 1.2)

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 server sounds and scripts | `SessionAvatarAdapter` maps `ObjectSoundPlayed` (F750) and `ObjectScriptPlayed` (F755): the sound from the body's sound table through the sink (`SmartBox::HandleSoundEvent`, 11.6096 0x00452000); the script through `EffectObject` on the body (`SmartBox::HandlePlayScriptType` 0x004520B0); closes `TODO(g5-w10-scripts)` (s0, as run) and, with s1's sink, `TODO(g5-w10-sounds)`; the stand-in sends one of each in `--play-checks` | edit `SessionAvatarAdapter.cs`, `AvatarSet.cs`, `AvatarBody.cs`, `StandIn/StandInVisit.cs`, `scripts/Session/PlayChecks.cs`; tests | docs/avatars.md §17 (1024); docs/spell-visuals.md §1 (11) | tests; `--play-checks`: both counted | none (first) |
| s1 the hook sink | `IEffectEventSink.OnEffectEvent` gains the body's sound table; `PlayMode` assigns `AvatarSet.EffectEvents` to a sink over `AudioService.Sound` (`PlayFromTable`, `PlaySound`, `PlayWave`); the pan sign fixed (`AvatarSet.cs:829`; retail's bearing cited); animated scenery plays its hooks (`LivingStatics`; the M1 drop removed); footsteps counted in `--play-checks` | edit `Audio/IEffectEventSink.cs`, `Audio/SoundHookRouter.cs`, `AvatarSet.cs`, `scripts/Session/PlayMode.cs`, the living-statics files, `scripts/Session/PlayChecks.cs`; tests | docs/portal-space.md §7.2 (193), §11.3 (346) | tests; `--play-checks`: footsteps counted | after s0 |
| s2a UI sounds | the retail UI's sound sink to the UI bus (`TODO(ui-sound-sink)`, M2 D62): button and dialog sounds as the retail UI runtime already requests them; `--retail-ui-checks` counts a click (as run: `TODO(ui-sound-sink)` closed in 1.3 s2a for every run with an audio service) | edit the UI sound path (`RetailUi/**`'s sink interface and `scripts/RetailUi/**`'s wiring), `AudioService.cs`; tests | docs/retail-ui.md §15 (523) (the sink TODO); docs/portal-space.md §7.2 | tests; `--retail-ui-checks` with the count | with s2b |
| s2b ambient sound | `RegionSoundData` (the region's `SoundInfo`: ambient sound tables with type, volume, base chance, min and max rate) and an `AmbientSoundPlayer` outdoors re-expressing retail's chance and rate rule (cited by name and 11.6096 address); the M3 message `AdminEnvirons` 0xEA60 (named in 1.2 s0) applied as a named TODO unless the rule is found (as run: `TODO(m3-admin-environs)` closed in 1.3 s2b; `TODO(m3-environs-fog)`, `TODO(ambient-slider)`, `TODO(ambient-seen-outside)` and `TODO(ambient-constant-defaults)` opened) | create `Data/World/RegionSoundData.cs`, `Client/Audio/AmbientSoundPlayer.cs`, tests; edit `GameStateReducer.World.cs` (EA60 only) | spec §5.5; docs/portal-space.md §7 (175); DatReaderWriter's `Region.generated.cs` (60, 81) | tests; `--engine-checks` hears an ambient draw (counter) | with s2a |
| s3 the audio check | a `record-audio` smoke step: a run with `--record-audio` whose file is judged non-silent (`TODO(record-audio-check)` closed); `login-check` counters per source `sounds_server= sounds_hooks= sounds_ui= ambient=` (report-only here, judged `>=1` from 2.1 except `ambient`, outdoors only, judged in the Holtburg laps); M2 D8 kept | edit `tools/smoke.ps1`, `LoginCheckScenario.cs`, a wave reader; tests | docs/portal-space.md §11.3; docs/session.md §27 | smoke `-Backend auto` with the new step | after s1 |

### 4.4 Item 1.4 Gear, wield and the first casts (branch `m3/1-4`; after 1.3)

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 kits | the lead first pins the kit's wcids by a read-only SELECT of the world database (a wand, an armour set of leather or similar, the component weenies: scarabs, herbs, powders, potions, talismans and every taper colour) and records them in the handoff; `GearKit` (the wcids and stack sizes; the components from the profile's formulas **customised for the account** through `SpellFormulaRules.CustomizeForAccount`, since ACE's `GetPlayerFormula` randomises the tapers per account; every taper colour is in the kit so a wrong customisation shows as a test failure, not a fizzle); `PlayerTools.SpawnGear(kit)` over `item`; the stand-in answers | create `Session/Mage/GearKit.cs`; edit `PlayerTools.cs`, `StandIn/StandInWorld.cs`; tests | spec §5.2 (components), §5.3; `SpellFormulaRules.cs` (86); `ArenaProfile.cs` | tests (the customised component list against a fixed account name); `--play-checks`: a stand-in kit arrives in the pack | none (first) |
| s1 the gear scenario | `GearScenario` (`--scenario=gear`): spawn the kit, wield the wand, wear the armour; verdict `gear=wielded worn=<n>`; `GearLiveTests` | create `Session/Scenarios/GearScenario.cs`, the loopback test; edit `Headless/Program.cs` | `MageBuildScenario.cs`; docs/session.md §20 (884) | loopback (the lead) | after s0 |
| s2 the first casts | `CastLiveTests`: one self buff per school through `Cast` with the enchantment seen in the read model; a fizzle: the spell whose pinned `Power` is above the mage's skill by more than 50 (a certain fizzle, from s0's numbers), as a ServerError; mana falls; `--scenario=gear` extended with one cast; the gear scenario's verdict field `cast=` (report-only; the login-check field is 2.1's, D20) | edit `GearScenario.cs`, the loopback tests | 1.2's `IActionApi` (the cast); spec §5.2; the item's handoff | loopback; then the item's gates | after s1 |

### 4.5 Item 2.1 The buff cycle and the cast on screen (branch `m3/2-1`; after 1.4)

Sliced by the 2.1 lead (P8) on master 0684c1f, test baseline 7,206; the owner's answers of 2026-10-16 are D23, D24,
D26 and D27. The lead's read-only SELECTs of ACE's spell table (2026-10-16) pin the StatMods: the attribute and skill
lines +35 / +40 / +45 at VI / VII / VIII; Armor Self 200 / 225 / 250; Impenetrability 200 / 220 / 240; Bludgeon Bane
x1.5 / 1.7 / 2.0; Mana Renewal, Regeneration and Rejuvenation x1.85 / 2.15 / 2.45; Hermetic Link x1.6 / 1.7 / 1.8;
Defender +0.15 / 0.17 / 0.20; Bludgeoning Protection 0.4 / 0.35 / 0.32. They pin the mana lines (D24) too: Stamina to
Mana Self I-VI 1676-1681 and VII 2345 "Meditative Trance" (source Stamina, destination Mana, proportion 0.5, loss 0.1 /
0 / -0.1 / -0.2 / -0.35 / -0.5 / -0.75: VI gives 1.5 times the stamina drained); Revitalize Self I-VI 1177-1182, VII
2083 "Robustification", VIII 4321 (VI boosts 80 plus up to 80); Stamina to Mana's VIII by name from the SpellTable. The
shortest buff lasts 1,800 s. ACE sends no F750 at login; it sends one on a wield (`Creature_Equipment.cs`), a potion
(`Food.cs:100`) and an expiry (`EnchantmentManager.cs`). ACE's stance modifier does not apply to mana
(`Creature_Vitals.cs` `GetStanceMod`). Impenetrability and Bludgeon Bane cast at the caster go to every worn clothing
piece and shield (`WorldObject_Magic.cs` 2067-2121, `TryCastItemEnchantment_WithRedirects`; `Player_Magic.cs`
1391-1393 accepts a creature for Vestements), one "You cast <spell> on <item>" line per piece.

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0a the profile and the pins | `ArenaProfile` gains two lines of new roles (`ManaTransfer`: Stamina to Mana Self; `StaminaRestore`: Revitalize Self), pinned by name, power and duration in `BsdProfileSpellsTests`, so `SpellsToLearn` grows and the build learns them on its next run (D3: the build scenario); `GearKit` carries their components; `BuffStatMods` (the SELECT's table as data, cited, with the transfer numbers); `BuffChainDatTests` prints and pins the predicted chain for BSD Mage's skill 316 (the level per line after each cast) and one cycle's mana (the SpellTable's base mana of each chosen spell) | edit `Session/Mage/ArenaProfile.cs`, `Session/Mage/GearKit.cs`, `BsdProfileSpellsTests.cs`, `ArenaProfileTests.cs`, `GearKitTests.cs`; create `Session/Mage/BuffStatMods.cs`, `BuffChainDatTests.cs` | §4.5's pins; spec §5.2; docs/session.md §29 (the profile, the kit) | `slice-check -MinTests 7206` | with s2a |
| s0b the cycle | `BuffCycle` (engine-agnostic, the Action API and `PlayerTools` only, no per-tick allocation): the 24 buff lines in the owner's dependency order (Focus and Willpower, then Creature Enchantment Mastery, then the other masteries, then the rest; §9's 1.1 row); per line the highest level whose ACE chance from the read model's current skill, recomputed after every cast, is at least `--buff-cast-chance` (0.9; D10), a fizzle retried once one level down; the armour lines as one cast at the caster each (D23), proved by the chat line per worn piece and UseDone 0, the expiry per piece from the cast; a self cast's expiry from the registry; the re-buff of each line whose remaining time is under `--buff-rebuff-margin-s` (300 s); the mana loop (D24): below the next cast's cost, Stamina to Mana at the rule's level, tried again until it fires (at most `--buff-transfer-tries`, 8: gameplay, not a check retry), Revitalize Self when stamina is short, a named bounded wait for regeneration when neither can run; options registered; `CastTracker` only if a self Transfer or Boost is not already ended by its UseDone | create `Session/Mage/BuffCycle.cs`, `BuffCycleTests.cs` (a fake clock on `ScriptedAceSession`); edit `OptionCatalog.cs`, (if needed) `Session/Actions/Trackers/CastTracker.cs` | s0a's handoff; `GearScenario.cs` (`GearCasts`: `Chance`, `TryCurrentSkill`, `ManaCeiling`, `FormOf`); docs/session.md §7 (the cast), §29 | `slice-check -MinTests <s0a's>` | after s0a; with s2b |
| s1 the buff scenario | `BuffScenario` (`--scenario=buff`, BSD Mage only): the gear first (`GearScenario`, cast off), Magic mode, the cycle, a re-buff pass with the margin forced over the remaining time (D25), NonCombat, log off; a failure logs off (as the gear scenario); verdict `buff gear=<..> worn=<n> buffs=<cast>/<wanted> levels=<line:level,..> rebuffed=yes\|no min_remaining_s=<s> transfers=<fired>/<tried> fizzles=<n> mana_min=<n>`; steppable inside the game's session (`endSession: false`) for s3; `BuffLoopLiveTests` (Loopback) and `EnchantmentExpiryLiveTests` (Tier Soak, D26: the shortest buff expires and leaves the registry with 0x02C3 and its F750) | create `Session/Scenarios/BuffScenario.cs`, `BuffScenarioTests.cs`, the two loopback tests; edit `Headless/Program.cs` | s0b's handoff; `GearScenario.cs`; docs/session.md §20, §29 | `slice-check -MinTests <s0b's>`; loopback (the lead) | after s0b |
| s2a the stand-in's cast | the stand-in plays ACE's cast (closes `TODO(m3-standin-cast-gestures)`): F74C per gesture (stance Magic 0x80000049, a wind-up per scarab at speed 2.0, the talisman's gesture, Ready after the 1 s recoil: `Player_Magic.cs` 598-601, 977-990, `SpellFormula.cs` 245-287, through `CastGesturePlanner`), 02BB the words, F755 the caster's and the target's effects; a self Boost or Transfer answered with ACE's vital updates; play-checks: a stand-in cast's gestures and scripts counted | edit `StandIn/StandInWorld.Actions.cs`, `StandIn/StandInWorld.cs`, `scripts/Session/PlayChecks.cs`; tests | docs/spell-visuals.md §1; spec §5.4 (the cast rows); `StandInWorld.Actions.cs` (the cast) | `slice-check -MinTests 7206 -Steps play-checks` | with s0a |
| s2b the cast on the player | the player's body plays the server's cast gestures in the Magic stance with the wand held (`IPlayerMovementBody.ApplyServerMovement`), the F755 scripts on the player and its target through `EffectObject`, the hook sounds; avatar counters `CastScripts` and `CastSounds` (sounded, in a cast's window); play-checks assert `cast_scripts>=1 cast_sounds>=1`; an 800x600 mid-cast frame by `--play-screenshot`; the footstep table question (§9, read-only: does retail sound Footstep1/2 from another table) answered in the handoff | edit `Avatars/AvatarSet.cs`, `Avatars/PlayerAvatar.cs`, `Avatars/SessionAvatarAdapter.cs`, `Audio/SoundHookRouter.cs`, `scripts/Session/PlayChecks.cs`; tests | docs/avatars.md §17; docs/spell-visuals.md §1, §7; s2a's handoff | `slice-check -MinTests <s2a's> -Steps play-checks`; the probe after it (the lead) | after s2a; with s0b |
| s3 the buff leg and the judges | `--login-check=buff` (a kind of its own, never in `all`): the game enters BSD Mage and runs `BuffScenario`'s steps; fields `gear= worn= cast= buffs= rebuffed= min_remaining_s= cast_scripts= cast_sounds= cast_frame_max_ms=` (`ok=` judges gear=wielded, every wanted line active, rebuffed=yes and sounds_server>=1; the cast_* fields and cast= are report-only; D28); the smoke step `login-buff` (`-Backend require` only); the sound judges: `sounds_hooks>=1` in the walking legs, `sounds_server>=1` in the buff leg (D27), record-audio's sounded `sources=` (1.3 R7). As built: `BuffLegRunner`, `BuffLegVerdict`, `PlayOptions.BuffShotPath`, `LoginCheckScenario.JudgeWalkingSounds`, smoke `login-buff` and `Test-RecordedSources` | create `scripts/Session/BuffLegRunner.cs`; edit `LoginCheckRunner.cs` (one hook line), `LoginCheckScenario.cs`, `tools/smoke.ps1`, `OptionCatalog.cs`; tests | docs/session.md §27, §28 (a kind of its own); s1's and s2b's handoffs | `slice-check -MinTests <s2b's> -Smoke`; the probe and the leg live (the lead) | after s1 and s2b |

Order: s0a beside s2a; then s0b beside s2b; then s1; then s3. Before the first live buff run the lead runs
`--scenario=mage-build` once so BSD Mage learns the two new lines. The lead's doc pass applies the slices' notes
(`cache/m3/2-1/doc-notes-*.md`) after the review's fixes.

As run, the live cycle is far cheaper than the planning numbers: every profile buff is FastCast (no wind-up, no caster
effect: the gesture and the target effect), and ACE's Mana Conversion lowers each cast's cost
(`Creature_Magic.cs` `GetManaCost` 58-100), so the first live cycle cast 26 lines in 56 s of session time with 3
transfers and 1 Revitalize (mana_min 63). The planning figures (about 1,900 mana a cycle, about 1,325 s) are the
worst case at the full price (`ScriptedCaster.CeilingCost`).

### 4.6 Items 2.2 and 2.3 Panels A and B (one item, branch `m3/2-2`; after 2.1)

Sliced by the 2.2 lead (P8) on master 6a21c80, test baseline 7,305. §3 lets the two run as lanes of one item: their
pages are disjoint (A: the skill page 11, the effects pages 4 and 5, the vitae page 15; B: the inventory page 7, the
spell page 13). What they share is the shell: `GamePanelHost` (the page kinds and the `NotInM2` answer),
`GamePlayScreen` (the staged wiring and `RegisterClasses`), `PlayChecks.cs` and the frames plan. s0 lays that seam
first, serially, with one hook file per slice, so no two parallel slices edit one file (the one exception is
`EnchantmentRules.cs`: a1 edits the secondary-attribute filter only, a2 the cull's key match only). Retail's widgets
`UIElement_ItemList` (0x10000031, 54 functions) and `UIElement_UIItem` (0x10000032, 30) are B's: s0's layout dump
says whether an A page uses them; if one does, that A slice waits for b1.

Every panel slice: the panels read `IGameSessionView` and act through `IActionApi` only (§2); a controller's
construction is one staged unit of the shell's wiring (no unit over 8 ms; `shell_longest_unit_ms` was 11.7 at the 2.1
gate), and a long list is filled over frames when its page first opens; nothing is built per frame while a page is
hidden, and a shown page rebuilds a line's text only when what it shows changes (the effects pages' countdown on a
whole-second change), so "an idle layer frame allocates nothing" holds; retail's functions cited by name and 11.6096
address; every unknown a named, logged TODO; client tests per panel over the real layout and a scripted description;
one play-check per page on the stand-in; a frame per page through s0's panel frames. Check: `slice-check -MinTests
<the previous slice's count> -Steps play-checks -Rerun` (s0 adds `shell-frames`). At most three slices run at once.

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 the panel seam | (1) `PanelPageKind` gains `Built` and `NotInM3`: a page with no controller answers `NotInM3.Show` ("Not in Lifestone M3: <panel>", logged as a placeholder); the toolbar's combat and examine buttons say M3. (2) `GamePanels` (`Client/RetailUi/Panels/`): `RegisterPage(panelId, controller)` (several controllers a page: the tabs), the page's open handler set by s0 for each registered page, the staged wiring (one `yield` per controller, after the host) and the class registrations, filled by one partial hook file per slice (`GamePanels.A1.cs` ... `.B3.cs`, empty stubs); `GamePlayScreen.Wire` and `RegisterClasses` call it once. (3) `PlayChecks.Panels.cs` calling one partial hook file per slice (`PlayChecks.PanelA1.cs` ... `.PanelB3.cs`, stubs); `PanelFramesPlan`: `--play-frames` also takes one frame per registered page under `panels/` (the shell's ten stay ten); `--play-panel-shots=DIR` (registered) opens each registered page after the arrival and saves an 800x600 frame of each, for the lead's live frames. (4) `PanelLayoutDumpTests` (a dat test, skips without the dats): each page's subtree (ids, classes, templates, string ids, images, states) to `cache/m3/2-2/layout-<panel>.txt`, and the classes each page holds pinned. (5) `gameplay-staged-notice-flake` (§9): reproduced under the parallel run and fixed, or its cause named. The hook files make the creates more than four: they are stubs | edit `Panels/GamePanelHost.cs`, `Screens/GamePlayScreen.cs`, `Toolbar/GameToolbar.cs`, `scripts/Session/PlayChecks.cs`, `Play/PlayOptions.cs`, `Play/ShellFramesPlan.cs`, `OptionCatalog.Play.cs`, `GamePlayScreenTests.cs`; create `Dialogs/NotInM3.cs`, `Panels/GamePanels*.cs`, `scripts/Session/PlayChecks.Panel*.cs`, `PanelLayoutDumpTests.cs`; tests | docs/retail-ui.md §21.5 (1286), §21.10 (1442), §11 (362); `GamePanelHost.cs`; `GamePlayScreen.cs` (78-308) | `slice-check -MinTests 7305 -Steps play-checks,shell-frames -Rerun`; the probe (the lead) | none (first) |
| a1 attributes | the attributes tab of the skill page (11), `gmAttributeUI` (0x1000002A): the six attributes and the three vitals, base and current (the registry's rule, `EnchantmentRules`), unassigned and total experience; `TODO(g4-quality-filter)` closed (the portal's per-vital enchantable table 0x1000000C read from the dat, applied in `EnchantAttribute2nd`); the raise buttons through `IActionApi.RaiseAttribute` / `RaiseVital` (owner question 1); live on `QualitiesChanged`; tests; play-check and frame | create `Panels/GameAttributesTab.cs`; edit `GamePanels.A1.cs`, `PlayChecks.PanelA1.cs`, `Session/Contracts/EnchantmentRules.cs` (the filter only); tests | s0's handoff and `layout-11.txt`; docs/retail-ui.md §21.3 (1209-1243); docs/session.md §29 (the stats); `GameVitals.cs` | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with a2, b1 |
| a2 skills | the skills tab, `gmSkillUI` (0x1000002B): the skills by advancement class (specialised, trained, untrained, unusable as retail groups them), base and current, the skill credits; the current-skill rule moved to one engine-agnostic `SkillRules` in `Session/Contracts` (ACE's `CreatureSkill.Current` as `GearCasts.TryCurrentSkill` has it, which then calls it), `TODO(g4-enchantment-affects-skills)` closed (the attack and defence groups: the Defender aura lifts Melee Defense); the raise and train buttons through `IActionApi.RaiseSkill` / `TrainSkill` (owner question 1); live; tests; play-check and frame | create `Panels/GameSkillsTab.cs`, `Session/Contracts/SkillRules.cs`; edit `GamePanels.A2.cs`, `PlayChecks.PanelA2.cs`, `EnchantmentRules.cs` (the cull's key match only), `Session/Mage/GearCasts.cs` (the call only); tests | s0's handoff and `layout-11.txt`; docs/session.md §29 (`TryCurrentSkill`, 1555); `CharGenSkillsPage.cs` (the skill list's art) | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with a1, b1 |
| b1 the item widgets and the pack | `UIElement_ItemList` and `UIElement_UIItem` runtimes: the icon composed from the object's icon, underlay, overlay and effects ids (the CreateObject's WeenieDesc), the stack count, a selection and a double click (D13: no drag); the inventory page (7): the main pack (`gmInventoryUI`, 0x10000023) with its burden readout and the side packs (`gmBackpackUI`, 0x10000022) from `PlayerInventory`, live on `InventoryChanged`; a double click on a pack item with valid locations wields it through `IActionApi.Wield`; tests; play-check (the stand-in kit's lines) and frame | create `Widgets/UiItemList.cs`, `Widgets/UiItem.cs`, `Panels/GameInventoryPage.cs`; edit `GamePanels.B1.cs`, `PlayChecks.PanelB1.cs`; tests | s0's handoff and `layout-7.txt`; docs/protocol.md §5.3 (520-560); `PlayerInventory.cs`; docs/session.md §29 (the kit, 1546) | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with a1, a2 |
| a3 effects, vitae and indicators | the effects pages 4 and 5 (`gmEffectsUI`, 0x1000001B): the registry's enchantments split beneficial and harmful as retail does, each line's spell icon and name from the SpellTable and its remaining time, the surpassed entries as retail shows them; the vitae page 15 (`gmVitaeUI`, 0x10000020): the penalty and the experience to clear it, or none; the effects, vitae and burden indicators (`g4-indicators-gameplay`: lit from the registry and the load, a click opens the page); character information refreshed on a qualities change (§21.8); tests; play-checks and frames | create `Panels/GameEffectsPage.cs`, `Panels/GameVitaePage.cs`; edit `GamePanels.A3.cs`, `PlayChecks.PanelA3.cs`, `Indicators/GameIndicators.cs`, `Panels/GameCharacterInfo.cs`; tests | s0's handoff and `layout-4.txt`, `-5`, `-15`; docs/retail-ui.md §21.3 (the registry), §21.5 (the indicators, 1317), §21.8 (1381); `PlayerQualities.cs` (134) | `slice-check -MinTests <a1's> -Steps play-checks -Rerun` | after a1; with b2, b3 |
| b2 the paperdoll | `gmPaperDollUI` (0x10000024): the slots by equip mask, each worn or wielded item on b1's `UiItem` (the wand and the five leather pieces on BSD Mage); a double click unwields through `IActionApi.Unwield`; the figure as the layout gives it (a 3D view's hole stays the named placeholder unless CharGen's 3D view binder serves it cheaply: the slice says which); tests; play-check and frame | create `Panels/GamePaperDoll.cs`; edit `GamePanels.B2.cs`, `PlayChecks.PanelB2.cs`; tests | b1's handoff; `layout-7.txt`; `EquipMask.cs`; docs/avatars.md §10 | `slice-check -MinTests <b1's> -Steps play-checks -Rerun` | after b1; with a3, b3 |
| b3 spellbook and components | the spell page (13): `gmSpellbookUI` (0x1000002E): the known spells by school and level with retail's filters (`Spellbook.Filters`), icons and names from the SpellTable, on b1's item list (BSD Mage's 247 filled over frames); `gmSpellComponentUI` (0x1000002F): the pack's components with their counts and the desired components (`Spellbook.DesiredComponents`); live on `SpellbookChanged` and `InventoryChanged`; tests; play-check and frame | create `Panels/GameSpellbookPage.cs`, `Panels/GameComponentsPage.cs`; edit `GamePanels.B3.cs`, `PlayChecks.PanelB3.cs`; tests | b1's handoff; `layout-13.txt`; `Spellbook.cs`; docs/spell-visuals.md §1 (11) | `slice-check -MinTests <b1's> -Steps play-checks -Rerun` | after b1; with a3, b2 |

Order: s0; then a1, a2 and b1; then a3, b2 and b3; one Sonnet merge (T6) of the six branches; the probe after s0
and after the merge (the shell's wiring is on the arrival path); the review, the fixes, the doc pass (the slices'
notes, `cache/m3/2-2/doc-notes-*.md`; docs/retail-ui.md gains §23 "The M3 panels"); the gates. The live frames:
`tools\play.ps1 -Character 'BSD Mage'` with `--play-panel-shots`, one per page, sent to the owner with the
placeholders named.

As run, two rows changed: a2's `GearCasts` lives in `Session/Scenarios/GearScenario.cs` (not `Session/Mage/`), and a2 also
added the public spell lookup (`IGameSessionView.Spell`) that a3 and b3 read; a3 merged a1's and a2's table loaders into
`GameSession.UseStatTablesFrom`. The lead added **b4 the paperdoll's figure** (after b2 left it a named placeholder: the 3D
view's seam was the shell's): `PaperDollFigure`, `ICreatureView` shared with CharGen's binder, `AttachFigure`, the page
opening on the figure as retail does.

## 5. Gates and the live-gate rules

**Per item, in order** (the runbook is template T7, §6):

1. Per slice: `slice-check.ps1` (build with 0 warnings, the default tests with a non-decreasing count, the named Godot
   steps at 800x600, `-Rerun` once).
2. After any slice that touches the arrival path (Enter to walking: the shell, the session's first messages, new
   shaders or materials, warm-ups): the lead runs the three-minute probe in the background while the next slice runs.
   The backend runs from the main checkout (the lead starts it); the client steps (`smoke.ps1 -NoBuild -Only
   login-check,login -Backend require`) run from the item worktree so the probe measures the slice's build. If the
   worktree cannot see the main checkout's backend (`ServerRuntimePaths` and pids.json), item 1.1's lead settles the
   way and records it here (§8); until then the fallback is M2's: the branch merged into the main checkout's
   integration branch for the probe. A frame over 50 ms is briefed to the next slice or a fix agent with the exact
   log lines now, not at the gate.
3. Lanes merged by a Sonnet merge agent (T6): build, tests, the UTC check, handoffs removed; the lead verifies by the
   numbers.
4. One Opus review (T4) on the item's diff and the docs it touches, then one Sonnet fix slice (T5) only for a blocker
   or a should-fix; nits go to §9.
5. Gate G-A from the item's worktree: `slice-check.ps1 -Smoke` (the full smoke with `-Backend auto`), secrets 0.
6. The live gate by the lead alone: `start-server.ps1` from the main checkout; the loopback tests
   (`Category=Loopback&Tier!=Soak`, the first test asserts access Player and auto-promotion off); the frame judge; the
   full smoke with `-Backend require` once; `stop-server.ps1` in a finally.
7. Merge to master with `tools/commit.ps1` ("Merge m3/<item> (<title>)"): the one exception to "never touch master",
   reserved to the lead (CLAUDE.md, Rules for agent sessions, Git); §8's "as run" lines; §0; the memory line; the
   roadmap. Push only when the owner asks.

**Live gates that do not loop** (from M2's measured failures: 7 of 10 live runs in one item failed on a 51-83 ms
frame, none a flake):

- **Probe left:** the arrival probe after every arrival-path slice, not first at the item's gate.
- **A live-shaped rehearsal offline:** item 1.2 s5 gives `--play-checks` a recorded PlayerDescription with the real
  enchantment count (the graduated character's 138) and an Enter 0.2 s after the list, so the offline check sees the
  live costs.
- **A warm-up checklist in every arrival-path brief:** new shaders, materials and pipelines compiled at start-up or
  on a worker; new message handlers JIT-warmed on a worker (`SessionJitWarmUp`); no allocations in the first frames
  after Enter; UI built in stages over frames; the frame attribution fields reported.
- **Judge the worst of three:** the gate runs `-Only login-check,login` three times; it fails on two runs over 50 ms
  or any run over 75 ms; one run over 50 ms is reported with its attribution and passes; the full smoke runs once. A
  new judge or sub-budget is report-only for its first gate. Engine ERROR lines fail every Godot step.
- **Rerun only what failed.** After a fix, the failing steps rerun; the loopback suite reruns only if the fix touched
  `Lifestone.Net` or `Client/Session`; the full smoke is not rerun unless the fix touched a smoke step's code.
- **Fix with a reproduction.** The fix brief carries the exact log lines; the fix agent reproduces the frame offline
  or in the probe before changing code, and the same agent continues by message for the next round. At most three
  live rounds per item; then the lead stops, writes the numbers into §8 and asks the owner.
- **Flakes get one rerun**, and a frame failure while other lanes run Godot is treated as contention first.
- **The fight's frames:** 3.1 adds `cast_frame_max_ms` and `fight_frame_max_ms` (the worst frame in the window;
  report-only in 3.1, judged against 50 ms from 3.2) and `bsd_min_fps` (a one-second window, report-only: the worst
  frame is what is judged); the same worst-of-three rule applies to the `bsd-fight` leg.

**Mod installs (D6):** when an item changes `server/mod`, the lead runs `setup-server.ps1 -ModOnly -ModSource <the
item worktree>\server\mod` after the mod tests pass, and a plain `setup-server.ps1 -ModOnly` after the merge; each
install is a line in §8; never `-AllowStaleMod`.

**The checkpoint (D1):** after 2.3's merge, the wave-2 lead writes the spend per item against §3, the DoD items met so
far, the frames, and pings the owner; nothing is cut or started until the owner answers.

## 6. The rules and the process

The rules are CLAUDE.md, "Hard rules" (fidelity under "Quality") and "Rules for agent sessions"; a brief cites them
and restates nothing. The prompt templates in `cache/m3-prompt-templates.md` (gitignored) carry a rules block whose
lines must equal CLAUDE.md's; a lead compares the two at the start of its item and corrects the template, so the
canonical text is CLAUDE.md's.

The process decisions (the owner's, 2026-10-15, recorded in `cache/m3-strategy.md` §0 and repeated here so a fresh
lead has them): **P1** the planning session is Fable 5.1 at high effort in plan mode with at most four read-only
researchers; **P2** one fresh Opus 5.5 review of the plan docs with a fixed checklist; **P3** Opus 5.5 high as the
per-item lead, Sonnet 5.5 for slices, lanes, merges, doc passes and cluster reviews, Opus for item reviews, one
measured Haiku 5.5 trial on an end-of-item doc pass; **P4** the 50 ms frame gate as §5 says (the probe after every
arrival-path slice, the worst of three runs); **P5** a go-ahead on an item's slice table authorises the lead to run
every slice, review, fix, merge and gate of the item, stopping only for an owner decision or a rule it cannot satisfy,
with a commit per slice and a morning summary; **P6** decisions in batches (at most eight when the plan is ready, then
at most four at each wave boundary, never one the code, the dats, the fidelity rule or a recorded decision answers);
**P7** the session rules live in CLAUDE.md and `tools/commit.ps1`; **P8** the plan holds the DoD, waves, seams and
decisions for the whole milestone and slice tables for wave 1 only; each later wave is sliced at its boundary.

The templates (`cache/m3-prompt-templates.md`): **T1** the wave lead prompt (one fresh session per item, with its
settings table); **T2** the implementer slice brief; **T3** the parallel lane brief; **T4** the review brief; **T5** the
fix-slice brief; **T6** the merge brief; **T7** the live-gate runbook. Each lead writes the next item's T1 into
`cache/` before it ends. Should `cache/` be lost, the templates are rebuilt from this section, §4's shape and §5.

## 7. Decisions

Decided 2026-10-15. D1-D7 by the owner in the planning batch; D8-D18 are the planner's defaults, which the owner may
overrule at any wave boundary. D20-D22 are the 1.4 lead's sequencing decisions, overrulable by the owner (no scope cut); D23, D24, D26 and D27 are the owner's answers to the 2.1 lead, D25 and D28 the 2.1 lead's; D29-D31 the owner's answers to the 2.2 lead, D32 the 2.2 lead's. D9 and D16 restate what the rules and the spec already give and are kept as rows only
so briefs can cite them.

| # | Question | Blocks | Recommendation | Decided |
|---|---|---|---|---|
| D1 | The checkpoint and the cut line | wave 3 | after wave 2; lights the cut candidate | **After wave 2**, with no automatic reaction: the wave-2 lead reports the spend and the state and pings the owner, who decides what, if anything, is cut |
| D2 | Void magic | the profile, the mage's skills | out | **Out** of M3 |
| D3 | A new test character and its name | 1.1 | yes | **Yes: "BSD Mage"**, levelled by its build and by the loops' kills, never reset or purged (CLAUDE.md, Accounts) |
| D4 | The CLAUDE.md edits (the rules section, the Godot correction, the commit wrapper, the milestone pointer) | planning | all | **All applied** |
| D5 | The budget ceiling | every item | $1,250 | **$1,250** API-equivalent including planning. The owner's batch said a lead asks before exceeding its item's estimate by half; the plan review found the margin under the ceiling too small for that, so the planner tightened it to **20%** (overrulable) and the checkpoint re-estimates waves 3-4 |
| D6 | Mod installs by the leads | 1.1, wave 3 | pre-authorise | **Pre-authorised for M3:** `setup-server.ps1 -ModOnly` from the main checkout after the mod tests pass, each install logged in §8, never `-AllowStaleMod` |
| D7 | M2's 18 worktrees and branches | 1.1 s0 | remove all | **Remove all 18 worktrees and the `m2/*` branches** in 1.1 s0 (all merged into master) |
| D8 | Inventory scope | 1.2 s2, 2.3 | the minimum the loops need | the read model, wield, unwield, move pack to paperdoll and back; pick up, drop, split and appraise are `NotInM3` |
| D9 | Spell components | 1.4 | retail | `require_spell_comps` stays true (fidelity; a server property is a database write); the kits carry the components the profile's account-customised formulas need |
| D10 | The buff level rule | 2.1 | by cast chance | the highest level per line whose ACE cast chance from the client's own skill value is at least `--buff-cast-chance` (default 0.9): a fizzle when the skill is below the spell's own `Power` minus 50, else a logistic chance with factor 0.07 (`WorldObjects/Player_Magic.cs:528-540`, `WorldObjects/SkillCheck.cs:7-22`); the powers come from the SpellTable (pinned by 1.1 s0); a fizzle retries once one level down |
| D11 | New mod verbs | 1.1 s2a | `spell learn`, `item` | M2 D51's surface grows by `spell learn <ids>` (`LearnSpellWithNetworking`) and `item <wcid> [<stack>]` (the `HandleCI` path, no audit broadcast); the protected names gain "BSD Mage" |
| D12 | The BSD profile, the arena and the targets | 2.1, 3.1 | the bludgeon set, tuskers | the profile is the bludgeon set (Armor Self, Bludgeon Protection, Impenetrability, Bludgeon Bane, the creature buffs of spec §5.2); the loop targets tuskers only (wcids 1628, 1629). Owner decision (2026-10-15): the drudges and virindi occupy one small, easily avoided portion of the den, which the loop never enters; the wave-3 lead maps the den's cells by generator (a read-only SELECT) before slicing, and the loop's waypoints and target filter keep to the tusker cells. The arena is the Aluvian den (portal 7242, cell 0x01050375) |
| D13 | The panels' interaction | wave 2 | read-only with Action API actions | the panels show the read models; wield by double click through the Action API; no drag and drop in M3 (the UI runtime lacks it) |
| D14 | The creation build | 1.1 s3 | specialise two, train four later | War Magic and Melee Defense specialised at creation (expected 32 + 20 = 52), the four magic skills trained with level credits by `TrainSkill`; the lead adjusts to the costs 1.1 s0 pins. **As built (1.1):** the pinned costs make it 28 + 20 = 48 at creation (4 spare); the four trains cost 34 of the 41 credits by level 126 (7 left, unspent) |
| D15 | The hero video | 4.2 | with sound | an M3 hero video with sound at the end (closes M2 D58), after the audit |
| D16 | What lights are | 4.1 | spec §5.6 | the dynamic-light list (at most 7, nearest first) merged ahead of the static lights; the viewer light; the projectile's Setup light where the dats give one (pinned by 1.1 s0) and the SetLight hook; the cell glow as a per-vertex term (retail's per-vertex lighting, not `OmniLight3D`) |
| D17 | How the death loop dies | 3.2 | unbuffed in the den | the loop enters the den without buffs and waits among the tuskers; no death within a time limit fails loudly (ACE has no player-side suicide) |
| D18 | The sound scope | 1.3 | the settled list | F750 and F755, the animation hooks (the pan fixed), animated scenery, UI sounds (M2 D62), the region's ambient sound outdoors, `AdminEnvirons` decoded as a named TODO, the audio check; M2 D8 (silent automated runs unless `--volume`) kept |
| D19 | The mage's spending of the level-126 XP and credits | 1.1 s3 | (the planner's 50/30/10/10 split) | **The owner's, 2026-10-15:** spend on everything spendable or the character cannot survive. 90% to magic (the four magic skills 50%, kept level; Focus and Self 30%; Mana Conversion 5%; Max Mana 5%); 10% in equal shares to every non-critical spendable stat (Strength, Endurance, Coordination, Quickness, Max Health, Max Stamina, Melee Defense, Arcane Lore and the other creation-trained skills: 13 shares); leftovers roll forward. Healing is not trained (the owner); the 7 spare credits stay unspent. Implemented by 1.1 s3c and the fix slice; the targets are pinned by `MageTemplateDatTests` |
| D20 | Where the plan's login-check `cast=` field lives | 1.4 s2, 2.1 | 2.1's `buff` leg | **The lead's, 1.4 (2026-10-16):** login-check plays Lifestone Smoke, the baseline, which has no spells, wand or components; the `cast=` field arrives with item 2.1's `buff` leg on BSD Mage; in 1.4 `cast=` is on the gear scenario's verdict (report-only) |
| D21 | War Magic's first live cast (DoD 3's "one spell per school") | 1.4 s2, 3.1 | 3.1 | **The lead's, 1.4:** ACE refuses an untargeted Flame Bolt (`Player_Magic.cs:1379-1383`: an untargeted cast needs NonComponentTargetType None) and a targeted one needs a monster, so War Magic's first live cast is 3.1's Flame Bolt at a tusker, with the live check of `TODO(m3-cast-projectile-attribution)`; `CastLiveTests` covers Creature, Life and Item |
| D22 | DoD 6's "leg `gear=wielded worn=<n>`" | 1.4, 2.1 | the `buff` leg carries it | **The lead's, 1.4:** the field is the headless `--scenario=gear` verdict, judged live by `GearLiveTests`; the `--login-check` leg that carries `gear=` is 2.1's `buff` leg on BSD Mage (the leg runs the gear first); DoD 6 is partly met until then |
| D23 | The armour lines (Impenetrability, Bludgeon Bane) | 2.1 s0b | every worn piece | **The owner's, 2026-10-16:** one cast at the caster per line; ACE redirects it to every worn clothing piece and shield (`WorldObject_Magic.cs` 2067-2121, the later-retail convenience; mages wore robes for the same reason); proved by the chat line per piece |
| D24 | The cycle's mana (the ceiling economy needs about 1,900 mana a cycle against a maximum of 426, the conservative need before a cast; ACE's Mana Conversion roll charges far less: live, a cycle took 56 s with 4 mana casts, `BuffLoopLiveTests`, 2026-10-16; regeneration about 0.2 a second) | 2.1 s0a, s0b | potions | **The owner's, 2026-10-16:** retail's way: Stamina to Mana Self at VI or VII, tried until it fires, then Revitalize Self for the stamina, and carry on; both lines join `ArenaProfile` and are learned by the build scenario; a bounded, named regeneration wait only when neither can run. `GearKit.ManaCastsPerCycle` stays 28 as the worst case, kept on purpose (the live count is 4) |
| D25 | How the live run proves "re-buffed before expiry" without waiting 25 minutes | 2.1 s1 | a forced margin | **The lead's, 2.1 (narrowed after s0b's measure):** the forced re-buff pass recasts the one line closest to expiry, with a margin of its remaining time plus one second, so that line is recast while still active at the cost of one cast (when no line is active every line is due; the schedule itself is proved on a fake clock by `BuffCycleTests`); `rebuffed=` and `forced=` read the pass's own recasts of an active line (`BuffResult.RecastWhileActive`, review 2.1-R5), not a flag over the cycle object's life; the verdict says `forced=yes` |
| D26 | DoD 4's live "an expiry removes it" | 2.1 s1 | the real expiry | **The owner's, 2026-10-16:** the real expiry of the shortest buff (1,800 s) in a Soak-tier test the lead runs at the gate; and a note for M4: the testing loop takes very long, look at cutting it down (§9) |
| D27 | Where `sounds_server>=1` is judged (ACE sends no F750 at login) | 2.1 s3 | the buff leg | **The owner's, 2026-10-16:** in the buff leg (F750 on a wield, a potion or an expiry); the Academy arrival's field stays a report |
| D28 | Judges added and judged in the same item (review 2.1-R7) | 2.1 s2b, s3 | judged in their suites | **The lead's, 2.1:** as 1.3's R9: a check that §4.5's acceptance names is judged in its suite from the slice that adds it (play-checks' `cast_scripts>=1 cast_sounds>=1`, offline and deterministic); the new login-check and leg verdict fields stay report-only except the four D27 and DoD 5 name (`gear=wielded`, every wanted line active, `rebuffed=yes`, `sounds_server>=1`); the walking legs' `sounds_hooks>=1` is judged from its first live sight at the 2.1 gate, where a 0 is read as the split's first sight before a product defect |
| D29 | The raise and train buttons of the attributes and skills tabs | 2.2 a1, a2 | live, as retail | **The owner's, 2026-10-16:** the buttons work through `IActionApi.RaiseAttribute`, `RaiseVital`, `RaiseSkill` and `TrainSkill` as retail's do; tests and play-checks click them on the stand-in only, and no automation clicks them live, so BSD Mage's experience is spent only by a person's click (D3 stands) |
| D30 | `g5-first-person-fade` and `g5-translucency` (§9 placed them in 2.3) | 2.3, 4.1 | move to 4.1 | **The owner's, 2026-10-16:** world rendering, not panel code: moved to 4.1, which owns `PlayerView.cs` and the render files |
| D31 | The M3 worktrees of merged items | every item | remove | **The owner's, 2026-10-16:** the worktrees and branches of merged M3 items are removed (each branch checked with `git branch --merged master`), this item's after its merge |
| D32 | The effects page's info box after the last effect ends (review 2.2-R7) | 2.2 a3, fix A | the glitch fix, flagged | **The lead's, 2.2:** retail's `RebuildList` (11.6096 0x004B8F70) returns before `UpdateSelection` when nothing is in effect, so the box keeps describing an expired spell; the fix (the selection and the box cleared) is the default and `--retail-effects-stale-info` (`UiRetailGlitches.EffectsStaleInfo`) restores retail (the fidelity rule) |

## 8. As run

(Appended per item by its lead, at most 20 lines each: what changed from the table, the agent size table (calls and
peak context), the gate numbers, the live rounds, the mod installs, how the probe saw the slice's build.)

**Item 1.1, Stats and the mage (2026-10-15).** Changes from §4.1: s2a ran as a lane beside s1a (disjoint files; one
Sonnet merge); s3 split into s3a (`ArenaProfile`, `MageTemplate`), s3b (`MageBuildScenario`) and s3c (the owner's D19
split, decided mid-item); `StatsLiveTests` (DoD 1) went to s1b on Lifestone Throwaway; D14 is 48 credits, not 52. s0
removed the 18 M2 worktrees and `m2/*` branches (D7). §5 item 2 settled: a worktree nested in the main checkout finds the
main backend through `CheckoutLocator`, so the live gate ran loopback and smoke from the item worktree. No slice touched
the arrival path, so no probe ran. Review (Opus): 0 blockers, 5 should-fix, 7 nits, all fixed in one fix slice
(Healing dropped by the owner there). Agents (calls / peak context): s0 58/177K, s1a 39/143K, s2a 76/278K, s1b 88/299K,
merge 5/65K, s2b 60/200K, s3a 47/197K, s3b 60/259K, s3c 54/160K, s4 39/171K, review 57/233K, fix 68/225K; s1b and s2a
ran near the 300K limit (s1b: four trackers plus every IAcSession double; s2a: two verbs with the port and live rows).
Gates: G-A build 0 warnings, 6,672 default tests (base 6,458), smoke all pass with `-Backend auto`, secrets 0. Live,
round 1 of 1: `ModVerbsLiveTests` 8/8 (the 128-id multi-fragment `spell learn`, review R9); loopback 28 passed, 1
skipped (`ModXpLiveTests`, owner-approved tier); `MageBuildLoopbackTests` first run `mage=built level=126 template=ok
spells=231/231 built_steps=28`, second `mage=ready ... built_steps=0`; frames, three runs: login-check
`enter_to_walk_max_ms` 41.0 / 34.7 / 38.0, login 27.9 / 36.0 / 35.0; full smoke `-Backend require` (login-throwaway
skipped: untouched) all pass, `level=1 mage_char=present`, secrets 0. Mod installs (D6): 1, `-ModOnly -ModSource` the
s2a worktree (DLL SHA-256 7a0b90e5...); 2, a plain `-ModOnly` from master after the merge.

**Item 1.2, The message set and the read models (2026-10-16).** Changes from §4.2: s4 split three ways (s4a the wire,
s4b the trackers, s4c the stand-in's answers and play-checks) after 1.1's s1b sizing; the lead recorded `Lifestone Smoke`
and `Lifestone Loopback` (login only) during the lanes; the recordings made three M2 recording tests run, which read the
state after the logoff: s5 fixed them (they were parked outside `cache/sessions` until s5). The recorded description holds
144 enchantments, not 138. s5 implemented retail's conditional LoginComplete (portal-space §15 item 15) with a 3 s cap; it
never fired live. Review (Opus): 0 blockers, 6 should-fix (R4 by the doc pass, the rest by one fix slice), 6 nits to §9.
Doc passes: the slices' notes by Haiku 5.5 (P3's measured trial: 27 calls / 191K, one pass, quality kept), the fix's notes
by Sonnet. Agents (calls / peak context): s0 92/295K, s1 88/299K, s2 76/280K, s3 62/227K, merge 18/87K, s4a 52/185K,
s4b 92/300K, s4c 97/284K, s5 112/349K, review 51/243K, fix 94+9/279K, docs 27/191K, live fix 57/155K, docs2 20/89K; s5 ran
over the 300K limit (seven items on the arrival path, the recording tests and the LoginComplete rule) and s0, s1, s4b
near it. Probe after s5 (main backend, the item worktree's build): `enter_to_walk_max_ms` login-check 29.7, login 31.4.
G-A round 1: retail-ui's "an idle layer frame allocates nothing" failed (96 B a frame: a closure in the fix's own
`ActionApi.ExpireTimeouts`), fixed (a9b2df8); round 2: build 0 warnings, 7,028 default tests (base 6,672), smoke all pass
with `-Backend auto` after one flake (offline-player-checks cellar-down-first-person, `physics-thin-doorway`: the
single-step rerun passed), secrets 0; play-checks' 12 ms UI-step sub-budget missed once at 12.1 ms (rerun passed;
`login-burst-frame`). Live, round 1: loopback 28 passed, 1 skipped (`ModXpLiveTests`), 1 failed (`InventoryLiveTests`: the
test read the grant's world object between ACE's two bundles: 0022 and 02CD on the UI queue, the CreateObject a bundle
later; the model was right; fix b25f688: the test waits, the stand-in sends ACE's order); round 2: loopback 29 passed,
1 skipped (`MageBuildLoopbackTests` `mage=ready`), frames three runs login-check 28.8 / 33.2 / 29.8, login 27.1 / 26.1 /
33.2, full smoke `-Backend require` (login-throwaway skipped: untouched) all pass, 7,031 tests, secrets 0. No mod
install. Spend: about 7% of a week against the 5.6% estimate (the s4 split, three fix rounds, a second doc pass).

**Item 1.3, Sound on (2026-10-16).** Changes from §4.3: the lanes s2a and s2b ran beside s0 (disjoint files); s0 also
drops and counts an F750/F755 whose avatar is missing and took 1.2's R8; s1 also routes s0's `ServerSoundTableHook`; s2b
applied EA60's sounds (fog: `TODO(m3-environs-fog)`; decoded-not-applied reads 0). Review (Opus): 0 blockers, 7
should-fix, 7 nits (R7 to 2.1, R8, R13 to §9). R9 decided: the checks §4.3's acceptance named are judged in their
suites; the new verdict fields stay report-only. Fixes in two parallel agents: A moved the arrival's costs off the
frame, scanned ambient on a cell change (`CellManager::ChangePosition` 0x00455AD0) and gated it on the world (47 draws
before the player existed, now 0); B made the counters read what they name (player-only footsteps, sounded vs handed,
per leg) and found why no hook ever sounded: `SoundTweakedHook` was decoded in ACE's field order, not retail's
(`SoundTweakedHook::UnPack` 0x00528380), so every tweaked hook had probability 0 (now 127 of 1,096 sound on the
stand-in; the rest are quiet scenery refused by distance). Agents: s0 108/285K, s2a 55/149K, s2b 106/341K, s1 117/283K,
merge 14/67K, s3 58/171K, docs 46/149K, review 65/259K, fix A 60/178K, fix B 114/253K, docs2 17/98K; s2b ran over 300K.
Probes: after s0 login-check 54.7 ms (stage arriving, `play_mode_step_ms` 41; rerun 44.7), login 29.2; after s1 41.9 /
35.5. G-A round 1: 0 warnings, 7,128 tests (base 7,031), smoke all pass `-Backend auto` (record-audio peak 0.0 dBFS, RMS
-20.3, 1,920/1,973 windows, muted), secrets 0. Live round 1 of 1: loopback 29 passed, 1 skipped (`ModXpLiveTests`;
mage=ready); frames login-check 39.2 / 27.1 / 36.5, login 29.2 / 31.7 / 31.9; sound fields (report-only) `sounds_hooks`
256-260, `ambient` 82-87, `sounds_server` 0 in the Academy arrival leg (3 of 3), `sounds_ui` 0; full smoke `-Backend
require` all pass but two flakes: offline-player-checks `cellar-down-chase` (rerun passed) and login-check's walker
stall on academy-exit goal 7 (rerun walked; its 53.4 ms frame, stage arriving 1.41 s after Enter, `pump_ms` 14.9 for one
message, the player's F745 subscribers 7.1 ms: `login-burst-frame`, one run of five over 50 ms, passed by the judge);
secrets 0. No mod install. Spend: about 2% of a week by the meter, against 3.1%.

**Item 1.4, Gear, wield and the first casts (2026-10-16).** Changes from §4.4: the lead's read-only SELECTs pinned the kit
before s0 (wand 2472; leather 45, 47, 81, 56, 115; 77 component wcids, every one burden 4, the Prismatic Taper 6); s0 also
decoded the DualDidMapper 0x27000002 and closed 1.2's R10 (not reproducible on ACE's order); s1 added `--play-screenshot`;
s2 closed 1.2's R7; D20-D22 moved the login-check `cast=` and DoD 6's leg to 2.1 and War Magic's first live cast to 3.1.
Review (Opus): 0 blockers, 5 should-fix, 7 nits; two parallel fix agents on disjoint files: A built StackableMerge 0x0054 end
to end (ACE never merges a top-up, so the loops would have filled the pack) with a slot guard; B made the cast form follow
the SpellTable's target type, the mana wait, `topup=` apart from `gear_steps`, no per-tick allocation, a frame without the
developer HUD. Lead live rounds before the gate: 1, 1479 is "Aura of Hermetic Link Self V", a self cast (ACE ended it
cast at the wand; fixed by B); 2, BSD Mage holds the Foci of Strife (15271): the foci guard refused the kit and the failed
run did not log off, so the next login was refused (fix A2: the kit carries the foci formula's components, the Prismatic
Taper among them; a failed gear run logs off; `headless-failed-logoff` for the rest, §9); 3, fragments of the pre-merge
top-ups were not merged on a run that granted nothing (lead fix f0ffb39). Agents (calls / peak context): s0 124/341K, s1
90/249K, s2 90/255K, review 53/223K, fix A 110/313K and A2 32/369K, fix B 108/308K, docs 38/157K; s0 (six items and the
dat decode), fix A (a new action through every IAcSession) and fix B (eight items) ran over 300K. G-A: 0 warnings, 7,206
default tests (base 7,128; one intermittent `GamePlayScreenTests` failure under the parallel run, §9), smoke all pass
`-Backend auto` (play-checks `kit=58/58 kit_topup=0`, max frame 44.0 ms at the arrival: `login-burst-frame`), secrets 0.
Live round 1 of 1: loopback 31 passed, 1 skipped (`ModXpLiveTests`): `GearLiveTests`, `CastLiveTests` (`creature=1331
life=1311 item=1479` confirmed in the registry, `fizzle=4325:0x0402`, mana 175->119 after a 174 s mana wait, `burned=2`
report-only at P(no burn) 0.03, `merges=2 slots=60->60`), `MageBuildLoopbackTests` mage=ready; frames login-check 31.4 /
35.1 / 27.0, login 35.2 / 34.2 / 28.0; full smoke `-Backend require` all pass, 7,208 tests, secrets 0. No mod install.
Spend: about 1% of a week by the meter, against 4.0%.

**Item 2.1, The buff cycle and the cast on screen (2026-10-16).** Changes from §4.5: the owner's D23, D24, D26, D27; the
lead's D25 narrowed to one line, the first cycle's upgrade pass, NonCombat waits after 5 s, the wand re-seat as the
leg's F750 fixture, D28; s0b's fix to `MagicTrackers.Classify` (it waited for a 0x02C2 ACE never sends for the armour
cast at self). Live, every profile buff is FastCast and Mana Conversion cuts the cost: a cycle is 26 casts in 56 s, not
the scripted 1,325 s (review R2). Review (Opus): 0 blockers, 3 should-fix, 7 nits; two parallel fixes (A: R3, R4, R8,
R9; R1, the 12 ms UI step, did not reproduce in six runs: §9; B: R2, R5, R6, R10) and one doc pass. Agents (calls / peak
context): s0a 83/250K, s2a 106/321K, s0b 79/352K, s2b 159/406K, s1 94/343K, s3 123/306K, review 62/257K, fix A 63/171K,
fix B 81/222K, docs 68/198K; s2b (six items, a decompile search, a whole-disk find the lead stopped), s0b (a 1,128-line
cycle and its scripted ACE), s1, s2a and s3 ran over 300K. Probe after s2b: login-check 29.7, login 29.9 ms. G-A: 0
warnings, 7,305 default tests (base 7,206), smoke all pass `-Backend auto` but offline-player-checks (holtburg-lap: a
walker stall, then a 75 ms frame; two clean reruns), secrets 0. Early live rounds: mage-build `spells=247/247`;
`BuffLoopLiveTests` `buffs=24/24` all VII, transfers 3/3, revitalizes 1, mana_min 63, the forced recast at 3,547 s left,
a second run casting no self line; `EnchantmentExpiryLiveTests` Strength Self I (layer 3) removed after 1,799 s with
0x02C3 and its F750. Gate round 1: loopback 30 passed, 1 skipped, 2 failed (a buffed mage, skill 376, has no certain
fizzle, and the bail left ACE's character in the world for the next test); frames login-check 35.7 / 32.7 / 38.0, login
24.9 / 32.8 / 30.3; the leg `ok=true` without a frame (its forced line was a wand Aura); full smoke `-Backend require`
all pass but record-audio (the UI step at 13.3 ms), 7,307 tests, `login-buff` `ok=true`. Lead fixes 867da1c, 42a2057;
round 2 `CastLiveTests` `fizzle=n/a:buffed`, record-audio pass, secrets 0; round 3 `BuffLoopLiveTests` pass, the leg's
three frames. No mod install. Spend: about 2% of a week by the meter, against 4.0%.

**Items 2.2 and 2.3, Panels A and B (2026-10-16).** Run as one item (§4.6, three agents at a time). Changes from §4.6:
the owner's D29-D31, the lead's D32; b4 added (the paperdoll's figure); the panel play-checks moved after the tools
teleport (lead fix 7be7fba: before it, the panel phase outlasted the stand-in's fixed teleport time); the panel shots'
settle 8 to 40 frames (lead fix 7d37f2a: the spellbook's first open on BSD Mage repaints late, `m3-spellbook-first-paint`).
The 48 merged M3 worktrees and branches were removed (D31). Review (Opus): 0 blockers, 10 should-fix, 8 nits; fix A
(R1 R2 R3 R7 R8), fix B (R4 R5 R6 R9 R10 and nits R11 R13 R18; its live-shaped vitae check found the vitae page built
after the description saying "no vitae": the lead's one-line fix and test); five nits to §9; one doc pass. Agents (calls /
peak context): s0 97/270K, a1 142/425K, a2 159/474K, b1 167/475K, a3 149/392K, b2 77/246K, b3 91/324K, b4 108/274K,
review 102/261K, fix A 76/244K, fix B 103/249K, docs 46/175K; a1, a2, b1, a3 and b3 ran over 300K (retail classes of 16 to
84 functions each plus the contract helpers the IL architecture test forced into `Session/Contracts`). Probes: after s0
(three slices compiling) login-check 56.0 ms once (the world start, `play_mode_step_ms` 39.2; the panels' wiring 5.0 ms),
rerun 46.6 / login 36.0; after the merge 35.7 / 24.2; the shell's staged build 7 frames, longest unit 2.7 ms. G-A round 1:
0 warnings, 7,457 default tests (base 7,305), smoke all pass `-Backend auto` (play-checks 162/0, max 26.4 ms), secrets 0.
Live round 1: loopback 32 passed, 1 skipped (`ModXpLiveTests`), Gear, Cast and BuffLoop live tests pass; `mage=ready
level=126 template=ok spells=247/247`; frames login-check 31.8 / 31.9 / 30.3, login 34.9 / 39.7 / 39.1; the panel shots
(eight, `--play-panel-shots` on BSD Mage); full smoke `-Backend require` (login-throwaway skipped) all pass but two:
offline-player-checks `cellar-down-chase` (`physics-thin-doorway`) and login-check's two frames after the academy-exit
teleport (129.1 and 76.2 ms with 4.8 ms of process work: outside the frame's own work); the single rerun of the two passed (offline-player-checks 10/0, login-check `ok=true`, teleport frame 15.2 ms); `login-buff` `ok=true
buffs=24/24 rebuffed=yes forced=yes sounds_server=2`; 7,459 tests; secrets 0. No mod install. Spend: about 4% of a
week by the meter (87% to 91%), against 6.2%.

## 9. Open TODOs

**Inherited from M2** (the audit table's "Open gaps", m2-log-in.md 1081-1099, placed by item):

| Gap | TODO | Placed |
|---|---|---|
| a buff cast or ended in the world shows only at the next description | `g4-enchantment-updates` | 1.2 s1 (closed in 1.2: 02C1 to 02C8 and 0312 are applied to the registry, and `QualitiesChanged(QualityChange.Enchantments)` follows each change) |
| the server's scripts and sounds on objects are not played | `g5-w10-scripts`, `g5-w10-sounds` | 1.3 s0, s1 (closed in 1.3: `g5-w10-scripts` by s0, `g5-w10-sounds` by s1) |
| UI sounds wired, unheard; no check hears audio | `ui-sound-sink`, `record-audio-check` | 1.3 s2a, s3 (closed in 1.3: `ui-sound-sink` by s2a, the UI's sounds reach the UI bus; `record-audio-check` by s3, the smoke `record-audio` step judges the recording non-silent) |
| no dynamic lights; the object light reach rule | `g5-dynamic-lights`, `g5-object-lights`, `interiors-light-reach` | 4.1 |
| the portal's per-vital enchantable table; the skill groups in the cull | `g4-quality-filter`, `g4-enchantment-affects-skills` | 2.2 (the effects and skills panels need both) |
| 14 of 81 layouts have screen logic | `retailui-runtime` (the 13 panel classes) | 2.2, 2.3, 3.2 |
| LoginComplete does not wait for the description and contents | `portal` (docs/portal-space.md §15 item 15) | 1.2 s5 (closed in 1.2: the wait for the description and the contents, with a 3 s cap, in `LoginCompleteTracker.WaitReason`; the director's cue is unchanged) |
| the first-person fade; a wielded item's children are not interpolated with the player | `g5-first-person-fade`, `g5-translucency` | 4.1 (D30; was 2.3) |
| server-object twins built eagerly; Holtburg's signs and the other excused objects not shown drawn | `serverobjects-lazy-twins` | not in M3 (no M3 item touches the Holtburg census) |
| the camera and look TODOs (wheel zoom, overhead and map views, slope, first-person turn, mouse-look preferences); no frame of the walk or run | `g5-wheel-zoom`, `g5-camera-overhead`, `g5-camera-map-view`, `g5-camera-slope`, `g5-first-person-turn`, `g5-mouselook-prefs` | not in M3 (the fight is third person; a walk frame joins 4.2's frames) |
| the refused leg's end position compared only through `where` | `st16-refused-where` | not in M3 |
| Where am I from the server alone on the headless host; the stand-in's replies unlabelled | `g7-where-client-body`, `g7-standin-where` | not in M3 |
| a really stopped ACE shown only on the stand-in | (DoD 12 of M2) | not in M3 (no fight run stops the server) |
| the mid-air logoff refusal; the PK logoff delay | `g6-logoff-midair`, M2 D33 | not in M3 (no fight ends in mid-air; no PK) |
| the window close in the world has no live proof | (DoD 11 of M2) | 4.2's audit, through `play.ps1` by the owner |
| the soak log does not print the CRC and duplicate counts | (DoD 12 of M2) | 4.2's final soak |
| the real-recording replay tests skip without `cache/sessions` | `replay-tick-boundary` | 1.2 s5 (closed in 1.2: the real-recording tests run when `cache/sessions` holds the recordings; `TODO(replay-tick-boundary)` stays named, it did not trip on the two recordings) |
| the burst frame near 50 ms; the 12 ms UI-step sub-budget | `login-burst-frame` | every arrival-path slice (§5); 1.2 s5 measured, not closed: max frame 26.7 to 34.2 ms at stage Arriving, 0.009 s after the arrival, on 3 offline runs (one run failed `no-ui-step-after-enter-is-over-12-ms` at 13.4 ms) |
| the 60 fps rule is a one-second average, not a worst frame | (DoD 15 of M2) | §5: the fight judges the worst frame; `holtburg_min_fps` stays a report |
| the Entering World dialog on the create path | `retailui-chargen-enter-dialog` | not in M3 |
| window layout and options saves, chat channels, the busy pointer | `g4-window-layout-save`, `g6-options-save`, `g4-chat-*`, `g6-busy-cursor-image` | not in M3 |
| the walker's rare stall; the physics open questions | `holtburg-ring-stall`, `physics-thin-doorway`, `physics-landing-velocity` | not in M3 unless a BSD leg trips them |

**Added by M3's items:** (appended by each lead with the review's nits.)

| From | Gap | TODO | Placed |
|---|---|---|---|
| 1.1 s0 | Flame Volley's ids 3663/3664 repeat levels III/IV and Flame Blast has no level II, so neither runs I-VI by id | `m3-volley-blast-levels` | 3.1 (only if the fight uses Volley or Blast; Flame Bolt is regular) |
| 1.1 s0 | the XpTable decoder's loud failures on a short or malformed table | `m3-xp-table` | none (a guard) |
| 1.1 s2a | `item` does not refuse creature weenies (`@ci` does not either) | `item-creature` | 1.4 s0: the kit holds items only (a test); the mod unchanged in wave 1; open |
| 1.1 s1b, s2b | the stand-in's raise chat line, skill names, XpTable fallback, SpellTable fallback, weenie knowledge and pack wire are placeholders | `m3-standin-raise-chat`, `m3-standin-skill-names`, `m3-standin-xp-table`, `m3-standin-spell-table`, `m3-standin-weenies`, `m3-standin-pack-wire` | 1.2 s2 (the pack wire, closed in 1.2 s2 with the inventory read model), 1.4 s0 (weenies: `m3-standin-weenies` answered for the kit's weenies in 1.4 s0); the rest when a check needs them |
| 1.1 s3b | a trained-later skill without a `TrainCredits` entry fails loudly | `m3-template-credits` | none (a guard) |
| 1.1 (owner's question) | the buff cycle must cast in dependency order (Focus and Willpower, then Creature Enchantment Mastery, then the other masteries, then the rest) and recompute each line's chance from the buffed skill after every cast; with retail's magnitudes (VI +35, VII +40) BSD Mage reaches level VII at 91% or better in every school after the three VI buffs, while VIII stays near 16% (War about 59%) (refined by 2.1 s0a: the third buff, Creature Enchantment Mastery, is already VII at 0.9097 from 333; VIII is 13% for Creature, Item and Life and 53% for War at the end of the chain); the StatMod magnitudes are not in the dats and must be pinned by a read-only SELECT of ACE's spell table | (none yet) | closed in 2.1: the order is `BuffCycle`'s; the chain pinned by `BuffChainDatTests` (Focus VI, Willpower VI, Creature Enchantment Mastery VII, the rest VII; VIII 13%, War 53%) and the magnitudes by `BuffStatMods` (the SELECT of 2026-10-16) |
| 1.2 s0 | a live enchantment's bucket is derived (vitae spell, cooldown category, stat-mod bit) | `m3-enchant-bucket` | closed in 1.2 s1 (retail's rule is the type bits, UpdateEnchantment 0x00593E70 / 0x00594CE0; the derived bucket agrees for real data) |
| 1.2 s0 | the retail handlers of 0024, 0023 and 01AD are not in the 11.6096 symbol table (01AD's chat type is assumed 0) | `retail-handler-0024`, `retail-handler-0023`, `retail-handler-01AD` | when a check needs it |
| 1.2 s0 | EA60 AdminEnvirons is decoded and counted, not applied | `m3-admin-environs` | closed in 1.3 s2b (the sound options play from the UI sound table, the fog options are published; the fog itself is `m3-environs-fog`) |
| 1.2 s1 | an overdue enchantment is reported only on demand (`CountOverdueEnchantments`), counted at the next enchantment message | `m3-enchant-overdue` | when a check needs it |
| 1.2 s1 | the "X has expired." line is left to a layer with the spell table | `m3-enchant-expiry-chat` | when a check needs it |
| 1.2 s1 | the registry's qualities event | `m3-enchant-qualities-event` | closed in 1.2 s4b (`QualityChange.Enchantments`, on every registry change) |
| 1.2 s1 | bucket parity between the derived bucket and the type bits | `m3-enchant-type-parity` | when a check needs it |
| 1.2 s1 | a spell removal (retail's `Handle_Magic__RemoveSpell`, 11.4186 0x00567890) is not built | `m3-spell-removal` | when a check needs it |
| 1.2 s2 | side-pack contents arrive as CreateObjects with a container; they are not waited for at login | `m3-inventory-sidepack-contents` | when a check needs it |
| 1.2 s2 | the instance properties other than Container and Wielder (02DA) are not read | `m3-inventory-instance-properties` | when a check needs it |
| 1.2 s2 | a foci's container type 2 cannot be told from its CreateObject | `m3-inventory-foci` | when a check needs it |
| 1.2 s2 | the server sends no burden capacity; retail derives it from Strength | `m3-burden-capacity` | when a check needs it |
| 1.2 s3 | retail skips the combat lines for a squelched name | `m3-squelch-combat` | when a check needs it |
| 1.2 s3 | retail auto-targets the attacker after 01B2 and 01B4 | `m3-combat-autotarget` | when a check needs it |
| 1.2 s4a | the six action stubs in `ActionApi` (`NotBuiltTracker`) | `m3-1-2-action-trackers` | closed in 1.2 s4b (the trackers replace the stubs) |
| 1.2 s4b | a projectile's caster is not on the wire: still open, so a missile another creature launches from within 5 m of the player along the same line, wearing the same Setup, would be taken (session.md section 7 has the rule) | `m3-cast-projectile-attribution` | 3.1 (D21), re-placed from 1.4: the live check; ACE's projectile CreateObject carries a Velocity (the rule refuses a projectile without one; if the live run shows none, the heading test needs another source) |
| 1.2 s4b | a buff on another target is `Sent` when UseDone 0 ends the cast, with no proof of its effect | `m3-cast-other-target-proof` | when a check needs it |
| 1.2 s4b | the host did not set the SpellTable, so every cast was "Unknown" | `m3-cast-spell-table-host` | closed in 1.2 s4c (`GameSession.UseSpellsFrom` sets it; the decode runs on a worker thread, session.md section 27) |
| 1.2 s4b | picking an item up from the ground is not an action yet | `m3-pickup` | when a check needs it |
| 1.2 s5 | the recorded XP placeholder: the experience `Stats` carries is made up to the recorded character's (the raise check spends it; logged once: 1.2 review R8) | `m3-standin-recorded-xp` | when a check needs it |
| 1.2 s5 | LoginComplete does not wait for side-pack contents | `m3-login-complete-side-pack-contents` | when a check needs it |
| 1.2 s5 | the stand-in does not send side-pack contents | `m3-standin-side-pack-contents` | when a check needs it |
| 1.2 review | `Cast(uint, int)` refuses every negative int, so an int-typed guid of 0x80000000 or more (every monster) becomes the refused "no target" (R7, `IActionApi.cs:163`) | none | closed in 1.4 s2 (`Cast(uint, int)` refuses only 0; `IActionApi.cs` and `ActionApi.cs`; session.md section 7) |
| 1.2 review | the stand-in's recorded-XP placeholder is silent: nothing is logged when it raises PropertyInt64 1 and 2 (R8, `StandInVisit.cs:473`) | `m3-standin-recorded-xp` | closed in 1.3 s0 (logged once) |
| 1.2 review | stale comments name closed TODOs (`GameStateReducer.Enchantments.cs:21`; the `AdoptHeldObject` "not wired yet" comment), and `m3-cast-spell-table-host` is still the name of the no-table outcome (R9) | `m3-enchant-qualities-event`, `m3-cast-spell-table-host` | the next slice that edits those files |
| 1.2 review | a held item deleted by F747 alone stays in the inventory model; each later LoginComplete then waits the 3 s cap (R10, UNCERTAIN: ACE sends 0024 first) | none | closed in 1.4 s0 (not reproducible on ACE's order; `m3-inventory-delete-alone` names the F747-alone case) |
| 1.2 review | the description's registry is stamped with the clock, and the 02C2 and 02C4 path with the message's timestamp (R11) | none | the next slice that edits those files |
| 1.2 review | out-of-glob edits (the description's spellbook and inventory calls, `SelfState.cs`) were not flagged in the lane handoffs; each was verified in the review (R12) | none | none (verified in review) |
| 1.3 s2b | AdminEnvirons' fog and ambient override (options 0, 1-6, 9999) are published and logged, not drawn | `m3-environs-fog` | 4.1 (only if the lights slice draws the landscape override; else not in M3) |
| 1.3 s2b | the ambient sound has no slider or enable flag of its own: it plays on the effects bus and slider | `ambient-slider` | when a check needs it |
| 1.3 s2b | an EnvCell that sees outside (a doorway) counts as indoors for the ambient sound, where retail counts it as outdoors | `ambient-seen-outside` | when a check needs it (the Holtburg laps' `ambient` judge) |
| 1.3 s2b | `ConstantSound`'s PlayNow and GetSoundPos are not in the 11.6096 symbol table: taken from the base class (always, none) | `ambient-constant-defaults` | when a check needs it |
| 1.3 s1 | the avatars' sound listener is the player's body, not the camera (retail's `SmartBox::set_viewer` -> `SoundManager::SetPlayerPosition`): other avatars' pans differ by the camera offset in third person | `avatars-listener-camera` | when a check needs it |
| 1.3 s1 | the human sound table 0x20000001 has no variant for Footstep1/Footstep2 (types 55 and 56) or for type 1: the hooks reach the sound player, which warns once per type and plays nothing; answered in 2.1 s2b, no change: retail has no other source (`SoundTableHook::Execute` 11.4186 0x00526AB0, 11.6096 0x005276B0 calls `SoundManager::PlaySoundA(SoundType, CPhysicsObj*)` 0x00550B70, 11.6096 0x00551780, which reads only `CPhysicsObj::sound_table`, set by `CPhysicsObj::InitDefaults` 0x005139D0, 11.6096 0x005144D0, from the Setup's default sound table and by `CPhysicsObj::set_description` 0x00514F40, 11.6096 0x00515A40, from the CreateObject's stable id, then `SoundManager::GetSound` 0x00550680, 11.6096 0x00551290, and `CSoundTable::Lookup` 0x00552100, 11.6096 0x00552D20, and returns nothing when the type is missing: no default table, no terrain or surface type, no fallback). The dats: the human Setup 0x02000001 has no default sound table, the human walk and run cycles (0x03000003, 0x03000002) carry SoundTable hooks of types 55 and 56, 414 animations carry them, and of the 190 sound tables only six have type 55 or 56 (0x20000016, 0x2000001E, 0x2000008C, 0x20000090 (55 only), 0x20000093, 0x200000AA: creature tables); the human table 0x20000001 has neither. The router's `PlayerFootsteps` (handed) counts them and `PlayerFootstepsSounded` stays 0, so the DoD's "footsteps sounded" cannot be judged on the human | `footstep-sound-table` | 2.1 s2b: retail sounds a hook only from the object's own table (`SoundTableHook::Execute` 0x005276B0 ... `CSoundTable::Lookup` 0x00552D20; no default or terrain table), and the human table has neither type, so the player's footsteps are silent on these dats; `TODO(footstep-sound-table)`: confirm live that ACE gives the player 0x20000001 |
| 1.3 review | portal sounds alone pass the `record-audio` step: it judges non-silence only, and the per-source counts (`sources=`) are report-only (R7; DoD 13's last clause) | none | closed in 2.1 s3: record-audio judges server>=1 and hooks>=1 in `sources=` (`Test-RecordedSources`, `BuffLegJudgesTests`) |
| 1.3 review | the stand-in's full mix peaks at 0.0 dBFS (summed voices with no limiter, as retail's DirectSound buffers saturate; 1095 hook voices in 98 s is a dense mix): add a clipped-sample count to `WaveLevels` (R8, UNCERTAIN) | none | when a check needs it |
| 1.3 review | `CountDecodedNotApplied` lost its test with `ReducerM3HooksTests`' empty-data theory; `AdminEnvironChanged` (a `Session.Contracts` type) sits in `Client/Audio/AmbientEnvirons.cs` and `ReducerHarness` does not record it (R13) | none | the next slice that edits those files |
| 1.3 fix B | the arrival's frame attribution has no counter for the server objects' particle meshes (`particle_meshes_server=n/a`) | `server-particle-meshes-counter` | when a frame needs it |
| 1.3 live gate | the arrival's burst frame: 53.4 ms once in five live login-checks (stage arriving, 1.41 s after Enter; one message pumped in 14.9 ms; the player's F745 subscribers 7.1 ms; the s1 probe saw an 11.2 ms single-message pump) | `login-burst-frame` | the next arrival-path slice (find the message and the subscriber with the new attribution fields) |
| 1.3 live gate | live, the Academy arrival leg heard no server sound (`sounds_server=0` in 3 of 3 login-checks; one login saw 1), so a `>=1` judge there would fail: check whether ACE sends an F750 in that window or the leg needs to be wider | none | 2.1 (D27): ACE sends no F750 at login; judged in the buff leg (the wand re-seat's wield sounds); the arrival's field stays a report |
| 1.4 s0 | a held item deleted by F747 alone stays in the inventory model (not seen on ACE's 0024-then-F747 order; a test pins the case) | `m3-inventory-delete-alone` | when a check needs it |
| 1.4 s0 | `m3-standin-weenies` is answered for the kit's weenies only (`StandInWorld.KitWeenies`); every other class id behaves as before | `m3-standin-weenies` | when a check needs it |
| 1.4 s0, s1 | the kit's component burdens were an upper bound of 6 | `m3-kit-burdens` | closed in 1.4 s1 (the SELECT of 2026-10-16: every kit component weighs 4; `GearKit.ComponentBurden`) |
| 1.4 s1 | the stand-in's refused wield is a test hook (`RefusedWieldWcids`), not ACE's wield rules | `m3-standin-wield-rules` | when a check needs it |
| 1.4 s2 | the stand-in's magic skill for the fizzle rule is a fixed 300 | `m3-standin-magic-skill` | when a check needs it |
| 1.2 s4c, named in 1.4 | the stand-in's spell stat modifiers, mana rules and component burn are placeholders (its casts burn nothing; `StandInWorld.Burn` is a test hook) | `m3-standin-spell-statmods`, `m3-standin-cast-mana`, `m3-standin-cast-components` | when a check needs it |
| 1.4 review R8 (fix A) | a held focus item (15268-15271, 43173) switches ACE's casts to the foci formula, which needs the Prismatic Taper (188) the kit lacks; `SpawnGear` refuses loudly while one is held | `m3-kit-foci` | closed in 1.4 fix A2 (live: BSD Mage holds the Foci of Strife, 15271; the kit carries the foci formula's components, the Prismatic Taper among them, and the refusal is gone) |
| 1.4 fix A2 | a headless run whose scenario fails disconnects without logging off (the runner disposes the session), so ACE keeps the character in the world and the next login is refused `CharacterInWorld`; `GearScenario` now logs off on a failure, LoginScenario, MageBuildScenario, ThrowawayLeg and the login check do not | `headless-failed-logoff` | when a check needs it (the runner is the place for all scenarios); the buff scenario logs off on a failure too (2.1 s1) |
| 1.4 review R12 (fix B) | the current-skill rule omits the augmentation bonuses (`GetAugBonus_Base`, `GetAugBonus_Current`; BSD Mage has none) and, as before, the attack and defence groups | `m3-skill-aug-bonus`, `g4-enchantment-affects-skills` | `m3-skill-aug-bonus`: when a check needs it; the other: 2.2 (inherited row) |
| 1.4 fix B | a `GearScenario` built without a SpellTable skips the mana check, logged (the headless host and the live tests always pass it) | `m3-cast-mana-unknown` | none (a guard) |
| 1.4 fix A | a whole merge inside the pack sends 0024 and 0197 with no F747, so the source's world object lingers in the client's object table (harmless: the inventory model forgets it) | `retail-handler-0024` | when a check needs it |
| 1.4 s2 (D20) | the plan's login-check `cast=` field: login-check plays Lifestone Smoke, which has no spells, wand or components | none | closed in 2.1: the buff leg carries `gear=` and `cast=` |
| 1.4 s2 (D21) | War Magic's first live cast: ACE refuses an untargeted war spell (`Player_Magic.cs` 1379-1383) | `m3-cast-projectile-attribution` | 3.1 (Flame Bolt at a tusker) |
| 1.4 lead (D22) | DoD 6's `gear=wielded worn=<n>` leg: the field is the `--scenario=gear` verdict, judged by `GearLiveTests` | none | closed in 2.1: the buff leg carries `gear=` and `cast=` |
| 1.4 review R9 | s1 edited `src/Lifestone.Client/Play/PlayOptions.cs` (the `--play-screenshot` parse) outside its file list, without a note | none | none (verified in review; noted in session.md section 21) |
| 1.4 G-A | `GamePlayScreenTests.A_staged_shell_is_built_over_frames_and_holds_the_portal_notice_for_its_spew_box` failed twice under the full parallel test run ("the held notice reaches the spew box"), passed 3 of 3 full reruns and 6 of 6 alone; this item touched no RetailUi file | `gameplay-staged-notice-flake` | 2.2 (the panels touch the shell) |
| 2.1 (D26, the owner) | the testing loop takes a very long time to run (the loopback suite about 15 minutes, the Soak expiry about 31): look at cutting it down | none | M4 (a note only; nothing changes in M3) |
| 2.1 s2a | the stand-in's cast gestures (F74C per wind-up and gesture, the words, the caster and target effects) | `m3-standin-cast-gestures` | closed in 2.1 s2a (`StandInWorld.Cast.cs`); opened there: `m3-standin-cast-plan` (a visit with no cast planner), `m3-standin-vital-spell-table` (below) |
| 2.1 s2b | the stand-in keeps no enchantment registry for items, so its armour cast adds nothing to a piece | `m3-standin-item-enchantments` | when a check needs it |
| 2.1 s3 (live) | the walking legs' `sounds_hooks>=1` judge is new in the walking legs; the 1.3 live gate's per-leg split was never printed, so whether the Academy exit and the Holtburg laps sound hooks on their own (not only the arrival's scripts) is first seen live; if a leg's tally is 0 the judge fails and the line `walking legs' sounds judged` says the sum (D28; the owner decides: keep, or judge the whole run) | none | the 2.1 gate |
| 2.1 review R1 (fix A) | the stand-in's cast planner loads a second SpellTable (the session's own `SpellTableHost` loads one too) and keeps it for the run; the 12 ms UI-step misses of s3's runs did not reproduce in fix A's six runs (7.1-10.4 ms; the planner's worker ends 0.19-0.76 s before the arrival), so they are taken as machine load; one resident table needs a `GameSession` accessor | `m3-standin-spell-table-shared` | when a check needs it |
| 2.1 review R2 (fix B) | the scripted caster models ACE's mana cost with Mana Conversion (`ScriptedCaster`, 59-61 s for a cycle against the live 56 s), but casts 0-1 mana lines where the live run cast 4 (the live Mana Conversion skill was not recorded) | none | when a check needs it (record the skill in the next live buff run) |
| 2.1 review R8 | the session's `[net]` lines log the account name at login ("LoginRequest for account ..."), in gitignored logs only; the buff leg keeps the name inside its loading worker (fix A) | none | M4 (with the owner's testing-loop note) |
| 2.1 review R10 (fix B) | nothing drives the timed re-buff (`BuffCycle.RebuffDue`, `StartRebuff(forced: false)` are called only by tests); DoD 5's "re-buffed before it expires" is proved by the fake clock and the forced pass (D25); the auto-buff goal will own the driver | `m3-buff-rebuff-driver` | the auto-buff work (after M3) |
| 2.1 s0b | the armour lines' expiry lives in the cycle object, so a new object recasts both armour lines (the client cannot read an item's enchantments without an appraisal) | `m3-buff-armour-memory` | when a check needs it |
| 2.1 s0b | a surpassed armour piece line is counted a miss | `m3-buff-armour-surpassed` | when a check needs it |
| 2.1 s3 | the game's buff leg ignores the `--buff-*` options (it uses the defaults) | `m3-buff-leg-options` | when a check needs it |
| 2.1 s2a, fix A | the stand-in changes no vital for the other five vital lines' level VII spells, the heals and the other boosts (the Stamina to Mana rows are `BuffStatMods`'s since fix A) | `m3-standin-vital-spell-table` | when a check needs it |
| 2.1 s2a, s2b | the stand-in's offline `--play` plans a spell outside the profile when it is cast; the projectile's Launch F755 and hit effect are not sent | `m3-standin-cast-plan-lazy`, `m3-standin-projectile-model` | 3.1 (the projectile) |
| 2.1 lead | the offline play-checks' teleport-window frame reads 58-72 ms in every run since at least 1.4 (report-only there; the live teleport frames read 7.9 ms) | `login-burst-frame` | when an offline frame judge needs it |
| 2.2 review R12 | the "idle layer frame allocates nothing" check runs on a synthetic screen; only the effects and vitae pages' `Step`s are pinned at 0 bytes | none | when a check needs it (a test over `GamePlayScreen.Update` with each page shown) |
| 2.2 review R14 | the skills list (38-42 rows) is built in one unit on the tab's first open (about 6 ms cold, under the 8 ms rule, not filled over frames) | none | none (accepted; split at the section boundaries if a frame needs it) |
| 2.2 review R15 | s0 edited `scripts/Session/PlayMode.cs` and created `PlayMode.PanelShots.cs` outside its row, without a note (every other out-of-glob file carries one in its handoff) | none | none (noted here) |
| 2.2 review R16 | `CSpellBase::InqSpellLevelByRoughHeuristic` (`GameSpellbookPage.cs`) and `ExperienceSystem::ExperienceToSkillLevel` (`SkillRules.cs`) are cited without an 11.6096 address | none | the next slice that edits those files |
| 2.2 review R17 | a double click wields with the item's whole `ValidLocations`; an item of two alternative slots (a ring) sends both bits, and whether ACE refuses that is unverified | `m3-wield-slot-choice` | when a check needs it (retail's `ItemHolder` picks the first free bit) |
| 2.2 live gate | on BSD Mage the spellbook's first open (247 spells, 8 a frame) does not repaint the page area for more than 8 frames: the page before shows meanwhile | `m3-spellbook-first-paint` | 3.2 (it edits RetailUi) or when a check needs it |
| 2.2 slices | the panels' open names: the stat page's heritage line, PK status and titles tab; skill tooltips; effects and vitae strings; the item list's use, examine and extras; side-pack contents unloaded until opened; the paperdoll's part lighting, click map, tooltips, sharp mode and scale; the spellbook's filter send, delete and shortcut; the components' icon, category and buy rate | `m3-attribute-heritage`, `m3-pk-status`, `m3-character-titles`, `m3-skill-tooltips`, `m3-effects-strings`, `m3-vitae-strings`, `m3-itemlist-use`, `m3-itemlist-examine`, `m3-uiitem-extras`, `m3-inventory-sidepack-contents`, `m3-paperdoll-part-lighting`, `m3-paperdoll-clickmap`, `m3-paperdoll-tooltips`, `m3-paperdoll-sharp`, `m3-paperdoll-scale`, `m3-spellbook-filter-send`, `m3-spellbook-delete`, `m3-spellbook-shortcut`, `m3-component-icon`, `m3-component-category`, `m3-component-buy-rate` | docs/retail-ui.md §23 lists each; when a check needs it |
| 2.2 live gate | login-check failed once on two frames after the academy-exit teleport (129.1 and 76.2 ms) whose process work was 4.8 ms (no pipelines, no collection): the time was outside the frame's own work; the three frame runs of the same gate read 20.8-34.1 ms there | `login-burst-frame` | when it recurs (the frame attribution has no GPU or present time) |
