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
wielded, five armour pieces worn), the first casts live in Creature, Life and Item. Item 2.1 (the buff cycle and the cast on screen) is merged (2026-10-16; §8, §4.5): BSD Mage buffed in all three schools at VII by `--scenario=buff` and the `buff` leg, the cast's gestures, scripts and sounds on the player, the real expiry live. Items 2.2 and 2.3 (the panels, run as one item) are merged (2026-10-16; §8, §4.6): the retail panels live on BSD Mage: attributes and skills with raise and train buttons (D29), the effects pages with live countdowns, vitae, the three indicators, the inventory with the item widgets, the paperdoll with its 3D figure, the spellbook and components; every other page answers "Not in Lifestone M3". The checkpoint (D1): the owner accepted it on 2026-10-16 with no cut (lights stay); the re-estimate of waves 3-4 is $460 (3.1 $160, 3.2 $150, 4.1 $75, 4.2 $75; projected M3 total about $1,056; `cache/m3/2-2/checkpoint.md`). Item 3.1 (BSD and the fight loop) is merged (2026-10-17; §8, §4.7; the owner waived the wait for the weekly reset): BSD Mage in the Aluvian Black Spawn Den on the arrival's floor, Flame Bolt VII drawn in flight and hitting a Tusker Slave, the damage, evade and kill lines in the chat, the kill's experience; `--scenario=fight`, the `bsd-fight` leg and the smoke step `login-bsd`. Item 3.2 (damage taken, death and re-gear) is merged (2026-10-17; §8, §4.8; started before the weekly reset on the owner's word): the unbuffed BSD Mage hit and killed by the tuskers (DoD 9), the death by retail with the vitae, the corpse and portal space to Holtburg's lifestone (D33, D34), the re-gear and the vitae paid back (D37); `--scenario=death-and-regear`, the `bsd-death` leg and the smoke step `login-death`; the combat panel, the power bar and the spellcasting panel. Item 4.1 (lights) is merged (2026-10-19; §8, §4.9; wave 4 sliced by its lead, D38 the model mix under the new prices): the dynamic-light list (7, nearest first), the viewer light round the player, a Flame Bolt's Setup light lighting the tuskers and, per vertex as retail, the den's walls; particles lit in cells; the first-person fade; the `bsd-fight` leg prints `bolt_light= dyn_lights_max= viewer_light=` and a `-viewer` frame, `death_frame_max_ms` judged. Item 4.2 (the end, with the polish pass, D39-D43) is merged (2026-10-19; §8, §4.11): the cluster audit of DoD 1-15 (every row met or met with named gaps, spec §7), the polish pass P1-P7 (the cast bar bound, the empty target panel, the stance rules, bolts that hit the tusker, the testing-loop cuts: a full smoke 19 min from 27, the stand-in's humans dressed, BSD Mage's classic armour and Healer's Heart), the audit fixes and the 27 s M3 hero video with sound. **M3 is complete.** Next: M4 planning from `cache/m4-planning-prompt.md`. Budget: $1,250 API-equivalent ceiling for the whole milestone including planning (D5);
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

### 4.7 Item 3.1 BSD and the fight loop (branch `m3/3-1`; after 2.2)

Sliced by the 3.1 lead (P8) on master 61965e0, test baseline 7,457. **The den by generator (D12; the lead's read-only
SELECT of `landblock_instance` and `landblock_instance_link`, landblock 0x0105, 2026-10-16; `cache/m3/3-1/den-map.md`).**
Three generators (wcid 3955): 0x70105022 in cell 0x0154 (35 children: Drudge Stalker 1609 x8, Drudge Ravener 1610 x16,
Tusker Guard 1629 x11), 0x7010507A in 0x0382 (Virindi Master 237 x1) and 0x7010507B in 0x0382 (81: Virindi 23 x8,
Virindi Master x1, Tusker Slave 1628 x19, Tusker Guard x53). The arrival's floor (z 0; cell 0x0375 at (100, -210, 0)) holds
only Tusker Slaves: 17 spawns in cells 0x02E1, 0x02E2, 0x0303, 0x030D, 0x0310, 0x0320, 0x0325, 0x0338, 0x033F, 0x036F,
0x0377 and 0x0383 (x 49-121, y -60 to -168). The drudge and virindi portion is the north wing below it (x 33-68, y -49 to
-110, z -24 to -30: all 24 drudges, 6 virindi), with two outliers among the tusker guards: Virindi 23 and Virindi Master
237 in 0x02D6 (z -6, the chest corner) and Virindi 23 in 0x0244 (z -12). **So the loop keeps to the arrival's floor:**
its waypoints are that floor's cells only (planned on the cell graph, never a cell below z -1), and its target filter
takes wcid 1628 or 1629 whose cell is on that floor, never 23, 237, 1609 or 1610. The Tusker Guards below are not needed.

**The fight's shape.** Flame Bolt (regular levels I-VII, so `m3-volley-blast-levels` does not apply) at the highest level
by D10's chance from the buffed War Magic; the scenario tops up the buffs first through `BuffScenario` when any profile line
is missing or under ten minutes (a fight on a level-126 mage is a buffed fight; D17's unbuffed wait is 3.2's death loop).
ACE turns the caster to a targeted spell's target itself, so the loop walks only to bring a tusker within range and sight;
a cast that ends without a projectile, or a projectile with no damage line, is a named miss, and the loop moves on to the
next waypoint after three in a row. A kill is the KillerNotification 0x01AD naming the target (cross-checked by its Dead
motion); damage dealt is the sum of the server's spell-damage lines for the player's own casts (ACE `SpellProjectile.cs`
824-840 composes them; the slice pins the forms); experience is the 0x02CF delta. The verdict fields `damage_taken` and
`hits_taken` (DoD 9) are report-only here; 3.2 judges them.

Every slice: the Action API and `PlayerTools` only; retail functions cited by name and 11.6096 address (the combat
system's `ClientCombatSystem::ObjectIsAttackable` 0x0056B340, `GetAttackTarget` 0x0056B630,
`Handle_Combat__QueryHealthResponse` 0x0056B2A0); per-frame code allocates nothing; each slice its own test files.
Check: `slice-check -MinTests <previous count> -Steps <steps> -Rerun`. At most three slices at once.

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 the fight seam | (1) `WorldObjectTable.NearestCreature(wcids, centre, maxMetres, cellFilter)` (no allocation; the creature test `ItemType & Creature`, as `AvatarWire` reads it) and a `HealthOf(guid)`; (2) `BlackSpawnDen` (`Session/Mage/`): the floor's cells, the excluded cells and `IsTarget(wcid, cell)` from the map above, and the curated Places row (`InteriorLocations`, cell 0x01050375, (100, -210, 0), the portal's quaternion); (3) `FightTally` (`Session/Scenarios/`): a bus subscriber counting the player's casts, the spell-damage lines dealt and taken (the forms pinned from ACE's writers), 0x01AD kills, 0x01B2 hits taken, 0x01B4 evades, the 0x02CF delta, resettable, no per-frame allocation; (4) `FightLegVerdict` (`[logincheck] bsd-fight done ok= kills= damage_dealt= spells_cast= misses= xp_gained= damage_taken= hits_taken= cast_frame_max_ms= fight_frame_max_ms= bsd_min_fps= projectiles= ...`, judged `kills>=1 damage_dealt>0`, the rest report-only) with its tests. Stubs: `LoginCheckKind.BsdFight` parsed (`--login-check=bsd-fight`, registered), the `LoginCheckRunner` hook line to a stub `FightLegRunner.Install` that fails loudly (`TODO(m3-fight-leg)`), and two play-check hook files called after the tools teleport | edit `Contracts/WorldObjectTable.cs`, `Interiors/InteriorLocations.cs` (one row), `Play/PlayOptions.cs`, `OptionCatalog.Play.cs`, `scripts/Session/LoginCheckRunner.cs` (the hook), `scripts/Session/PlayChecks.cs` (two calls); create `Mage/BlackSpawnDen.cs`, `Scenarios/FightTally.cs`, `Scenarios/FightLegVerdict.cs`, `scripts/Session/FightLegRunner.cs` (stub), `PlayChecks.Fight.cs`, `PlayChecks.Projectile.cs` (stubs); tests | `cache/m3/3-1/den-map.md`; the spec §5.4 (the combat rows, the combat lines); `GameStateReducer.Combat.cs`; `BuffLegVerdict.cs`; `ArenaProfile.cs` (87-96) | `slice-check -MinTests 7457 -Steps play-checks -Rerun` | none (first) |
| fa the fight scenario | (1) `FightScenario` (`IScenario`, name `fight`): the gear (`GearScenario`), the buff top-up (`BuffScenario`), `PlayerTools.Place` to the den's row, settle, Magic mode, then per target: `NearestCreature` through `BlackSpawnDen.IsTarget`, `Cast(FlameBolt, target)` until the 0x01AD or the limits (`--fight-kills`, default 1; `--fight-seconds`, default 300; both registered), a walk along the floor's waypoints (`CellRoutePlanner` over the floor's cells, the route driver as `RoutePlay` uses it) when nothing is in range or three casts missed, NonCombat at the end; (2) the Flame Bolt level by D10's rule from the buffed skill (the buff cycle's chance code, not a copy); (3) headless `--scenario=fight` and its summary line; (4) tests: `FightScenarioTests` over a scripted session (a target dies, a miss moves on, a non-tusker or a cell off the floor is never targeted, the limits end it loudly) and loopback `FightLiveTests` (BSD Mage, one kill: `kills>=1 damage_dealt>0`, the war cast Confirmed by its projectile, which is `TODO(m3-cast-projectile-attribution)`'s live check: the projectile's create position, velocity and Setup logged) | create `Scenarios/FightScenario.cs`, `FightScenarioTests.cs`, `Loopback.Tests/Session/FightLiveTests.cs`; edit `Lifestone.Headless/HeadlessOptions.cs`, `Program.cs`, `OptionCatalog*.cs` (append); tests | s0's handoff; docs/session.md §7 (`Cast`), §29, §30; `BuffScenario.cs`; `RoutePlay.cs`; `MagicTrackers.cs` (the attribution rule) | `slice-check -MinTests <s0's> -Rerun` (no Godot step) | with fb, fc |
| fb the stand-in's fight | (1) a stand-in tusker (wcid 1628, Setup 0x02000964, health, attackable), placed by the fight play-check; (2) the war cast's projectile as ACE sends it: its Launch F755, a flight at the spell's speed to the target, then the hit (the projectile's F755 Explode, its state change and F747 as `SpellProjectile.cs` 209-248 order them), the damage line in ACE's form, the target's health (0x01C0 on a query), and at zero the Dead motion, 0x01AD, the 0x02CF experience; closes `TODO(m3-standin-projectile-model)`; (3) the cast planner plans any known spell on demand (`TODO(m3-standin-cast-plan-lazy)`); (4) `PlayChecks.Fight.cs`: Magic mode, Flame Bolt at the stand-in tusker until it dies, judged by `FightTally` (`kills=1 damage_dealt>0`) | create `StandIn/StandInWorld.Combat.cs`, `StandInFightTests.cs`; edit `StandInWorld.Actions.cs` (the projectile), `StandInWorld.Cast.cs`, `StandInVisit.cs`, `PlayChecks.Fight.cs`; tests | s0's handoff; docs/session.md §16 (the stand-in, 846-854); the spec §5.4 (launch, hit, kill rows); `StandInWorld.Actions.cs` (303) | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with fa, fc |
| fc the projectile on screen | (1) the projectile in flight as retail moves a Missile object (`CPhysicsObj::UpdateObjectInternal` and the missile state; the object's own velocity, no gravity for a bolt), stopped by the hit's state and vector changes and removed on F747; a projectile Setup without spheres still moves (the body's rule today keeps its origin: the slice says what retail does); (2) its scripts: the Setup's default script at create, the Launch and Explode F755 from its own script table; (3) the warm-up: the 13 war spells' projectile Setups (meshes, materials, particle emitters) prepared at start-up on a worker, so a bolt draws on its first frame (`login-burst-frame`'s checklist); (4) `AvatarStats` counters (`ProjectilesCreated`, `ProjectilesDrawn`, `ProjectileExplodes`) and a projectile window beside the cast window (for `fight_frame_max_ms`); `ProjectileFlightTests`; `PlayChecks.Projectile.cs` judges `projectiles_drawn>=1` on the stand-in's bolt (D28) | edit `Avatars/AvatarBody.cs`, `AvatarSet.cs`, `SessionAvatarAdapter.cs`, `scripts/World/ServerObjects/*.cs`, `scripts/Session/PlayWarmUp.cs`, `PlayChecks.Projectile.cs`; create `ProjectileFlightTests.cs` and at most two files under `Avatars/` or `ServerObjects/`; tests | s0's handoff; docs/avatars.md §17 (1024-1145), §14 (855); `AvatarBody.cs` (363-406, 958); docs/spell-visuals.md §2-§5 | `slice-check -MinTests <s0's> -Steps play-checks -Rerun`; the probe (the lead) | with fa, fb |
| fd the leg and the loop offline | (1) `FightLegRunner` (`--login-check=bsd-fight`, the buff leg's shape: BSD Mage, the arrival, `FightScenario` in the game's session, log off, the verdict), sampling `cast_frame_max_ms`, `fight_frame_max_ms` (the worst frame while a cast or projectile window is open or a target is engaged) and `bsd_min_fps` (one-second windows in the den), all report-only (§5); (2) the review frames with `--play-screenshot`: the bolt in flight, the hit and the kill line in the chat, 800x600, the developer HUD hidden; (3) the smoke step `login-bsd` (backend step, `-Backend require` only, as `login-buff`); (4) the whole `FightScenario` offline in play-checks against fb's tusker (an "arena here" option: no teleport), judged by the verdict's two fields; closes `TODO(m3-fight-leg)` | edit `FightLegRunner.cs`, `PlayChecks.Fight.cs`, `tools/smoke.ps1`, `OptionCatalog*.cs`; create `FightLegRunnerTests` for what runs offline (the verdict and the sampling), tests | fa's, fb's, fc's handoffs; docs/session.md §30; `BuffLegRunner.cs` | `slice-check -MinTests <merged count> -Steps play-checks -Rerun`; the probe (the lead) | after fa, fb, fc are merged |

Order: s0; then fa, fb and fc as lanes (disjoint files; the lead merges them into `m3/3-1`); the probe after fc and after
the merge (the warm-up is on the play path); `FightLiveTests` live as soon as fa merges (BSD Mage's kills give it XP: D3);
fd; one Opus review (T4); Sonnet fixes (T5) for the blockers and should-fixes; the doc pass (the slices' notes,
`cache/m3/3-1/doc-notes-*.md`; docs/session.md gains §31 "The fight loop" and avatars.md §17 the projectile); the gates
(§5; the live gate adds `FightLiveTests` to the loopback suite and `login-bsd` to the full smoke). The live frames: the
leg's three review frames, sent to the owner with the placeholders named.

### 4.8 Item 3.2 Damage taken, death and re-gear (branch `m3/3-2`; after 3.1)

Sliced by the 3.2 lead (P8) on master 9f6c861, test baseline 7,592. **What ACE does (read before slicing).** A player's
death (`Player_Death.cs` 34-292): `OnDeath` sends the VictimNotification 0x01AC (and 019E to the others); `Die` sends the
Dead motion (stance NonCombat), 0x02E9 health 0, 0x02CD NumDeaths, DeathLevel and VitaeCpPool, the vitae 0x02C2 (spell
666, 5% a death, at most 40%: `EnchantmentManager.cs` 391-443), then **0x02C6 and every enchantment removed** (211-216:
BSD Mage has no `AugmentationSpellsRemainPastDeath`), and after the Dead animation plus 1 s the corpse (`CreateCorpse`,
`Creature_Death.cs` 428; a level-126 player drops level/10 plus 0-2 items by value, wielded ones included: 347-540) and
the teleport through portal space to `Sanctuary ?? Instantiation` (247-292: NonCombat, the lifestone protection, 3 s,
then the vitals at 75%). **BSD Mage's sanctuary is its creation spot** (`PlayerFactory.cs` 393: the starter area, not a
lifestone) until it uses one (`Lifestone.cs` 55-75: the Sanctuary motion, the attune line, half the stamina). **ACE has a
player-side suicide** (GameAction 0x0279, `GameActionDie.cs`; `Player_Death.cs` 299-345: five speech lines 3 s apart,
then `Die` without 0x01AC; retail `CM_Character::Event_Suicide` 11.6096 0x006A2920), so D17's "no suicide" premise does
not hold (D33). **The loop's shape (D33, D34):** gear; bind at Holtburg's lifestone (place there, Use the nearest
`BF_LIFESTONE` object, the attune line); the den; if any enchantment but the vitae is active, `Suicide` there (the purge
is what a death does, so the mage is unbuffed; the corpse stays in the den, never in Holtburg where the baseline's laps
run); the respawn at the lifestone; the den again, unbuffed: the tuskers' blows land (0x01B2, 0x02E9: DoD 9). **ACE
dispels the lifestone protection on the teleport to the den** (`Player_Location.cs` 702-703, `Player_Death.cs` 937-942:
"Your actions have dispelled the Lifestone's magic!"), so the loop never sees the protected blow (0x028A 0x0502 +
ShieldUpBlue, `Player_Combat.cs` 299, 416-418, 480-482) live: `protected=` stays 0, `protection=dispelled|expired|none`
reports the two Magic lines (review R2), and the protected blow is proved offline only (`PlayChecks.Death.cs`); the death
(0x01AC: DoD 10), bounded by `--death-seconds` (default 600, fails loudly, D17); the respawn at the lifestone (75%
vitals); **the re-gear**: `SpawnGear`'s top-up of the dropped kit items through the mod's `item` and the wand and the
five pieces wielded again (the corpse is not looted: pick-up is `NotInM3`, D8); log off. `respawn=lifestone` reads the
arrival after the death's F751 within 15 m of a `BF_LIFESTONE` object. **The panels** (DoD 12) are floaty windows of the
gameplay layout, not host pages: `gmCombatUI` 0x1000000C (11 functions), `gmPowerbarUI` 0x1000000F (12) and
`gmSpellcastingUI` 0x10000015 (34); the toolbar's four combat buttons stop answering `NotInM3` and change the mode.

Every slice: the Action API and `PlayerTools` only; retail functions by name and 11.6096 address; per-frame code
allocates nothing; each slice its own test files. Check: `slice-check -MinTests <previous count> -Steps <steps> -Rerun`.
At most two slices that run Godot at once (3.1's lanes ran long under three-way contention).

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 the death seam | (1) `IActionApi.Suicide()` (0x0279, no payload) with its tracker: done on the player's death (0x02E9 health 0 and the NumDeaths 0x02CD), refused on a WeenieError (YoureTooBusy); `IActionApi.AddSpellFavorite(spell, index, bar)` (0x01E3, ACE `GameActionAddSpellFavorite`; done when sent, ACE answers nothing); codec and tracker tests; (2) `WorldObjectTable.NearestWithFlags(descFlags, centre, maxMetres, out guid)` (the CreateObject's description flags: `BF_LIFESTONE` 0x4000, `BF_CORPSE` 0x2000, as `WorldContentCensus` 62-102 reads them; no allocation); (3) `DeathTally` (`Session/Scenarios/`): 0x01AC, the player's Dead motion, the NumDeaths delta, the vitae (spell 666) and its percentage, 0x02C6, the corpse's F745 (`BF_CORPSE`, "Corpse of <name>"), the death's F751 and the arrival, 0x028A 0x0502, the vitals after the arrival as shares of their maxima, the lowest health share, with `FightTally`'s hits, damage and evades; resettable, no per-frame allocation; (4) `DeathLegVerdict` (`[logincheck] bsd-death done ok= died= suicides= vitae= respawn=lifestone|elsewhere|none corpse= purged= protected= vitals_pct= gear_restored= worn= hits_taken= damage_taken= evades= health_min_pct= death_s= death_frame_max_ms= ...`), judged `died=1 respawn=lifestone gear_restored=yes` and DoD 9's `damage_taken>0 hits_taken>=1` (D36; report-only in `bsd-fight`); `vitae=` printed (D35); the rest report-only; and `FightLegVerdict` judges the frames by §5 (a run over 75 ms fails, over 50 ms is `frame_over_50=yes` for the lead's worst of three); (5) stubs: `LoginCheckKind.BsdDeath` (`--login-check=bsd-death`, registered), the `LoginCheckRunner` hook to a stub `DeathLegRunner.Install` failing loudly (`TODO(m3-death-leg)`), `PlayChecks.Death.cs`, the panel hooks `GamePanels.C1.cs`, `.C2.cs`, `PlayChecks.PanelC1.cs`, `.PanelC2.cs`, and the three classes registered as plain containers, with a `PanelLayoutDumpTests` row per window (`cache/m3/3-2/layout-combat.txt`, `-powerbar.txt`, `-spellcasting.txt`). The stubs make the creates more than four | edit `Contracts/IActionApi.cs`, `Actions/ActionApi.cs`, `Lifestone.Net/Session/IAcSession.cs` and the action codec, `Contracts/WorldObjectTable.cs`, `Play/PlayOptions.cs`, `OptionCatalog.Play.cs`, `scripts/Session/LoginCheckRunner.cs` (the hook), `PlayChecks.cs` (calls), `Panels/GamePanels.cs`, `Screens/GamePlayScreen.cs` (`RegisterClasses`), `Scenarios/FightLegVerdict.cs`; create `Actions/Trackers/SuicideTracker.cs`, `Scenarios/DeathTally.cs`, `Scenarios/DeathLegVerdict.cs`, `scripts/Session/DeathLegRunner.cs` (stub), the stubs; tests | this preamble; the spec §5.4 (the death row); `Player_Death.cs` 34-292; docs/session.md §31 (the verdict and tally shape); `FightLegVerdict.cs`, `FightTally.cs` | `slice-check -MinTests 7592 -Steps play-checks -Rerun` | none (first) |
| da the death-and-regear scenario | (1) `DeathScenario` (`IScenario`, `death-and-regear`; `DeathStages` flags Gear, Bind, Den, Unbuff, Death, Regear; All): the stages of the preamble, the den walk through the fight's `IFightWalk` seam to the nearest tusker cell on the floor (`BlackSpawnDen`), the death wait bounded by `--death-seconds` (registered), each stage logged; a respawn elsewhere, a lifestone not found within 60 m of the Holtburg place, a re-gear short of `gear=wielded worn=5`, the walker leaving the floor fail loudly; (2) headless `--scenario=death-and-regear` and its summary line; (3) a short mode for DoD 9 (`StopAfterHits`, stop after the first 0x01B2 and 60 s, then log off); (4) the mage's spell bars in the gear stage: the profile's lines on bar 1 and Flame Bolt I-VII on bar 2 through `AddSpellFavorite`, idempotent, reported `bars=` (the build scenario and `mage=ready` stay as they are); (5) tests: `DeathScenarioTests` (scripted: a buffed start suicides first, an unbuffed one does not; the death, the respawn, the re-gear; the time limit; a respawn elsewhere) and loopback `IncomingDamageLiveTests` (DoD 9: unbuffed, health falls, a 0x01B2 arrives) and `DeathLiveTests` (DoD 10: `died=1 vitae>0 respawn=lifestone gear_restored=yes`, a corpse seen, 0x02C6 seen) | create `Scenarios/DeathScenario.cs` (and at most one `Death*.cs` helper), `DeathScenarioTests.cs`, `Loopback.Tests/Session/DeathLiveTests.cs`, `IncomingDamageLiveTests.cs`; edit `Lifestone.Headless/HeadlessOptions.cs`, `Program.cs`, `OptionCatalog*.cs` (append); tests | s0's handoff; docs/session.md §29 (the build), §31 (`FightScenario`, the walk); `FightScenario.cs`; `GearScenario.cs` | `slice-check -MinTests <s0's> -Rerun` (no Godot step) | with db, pa |
| db the stand-in's death | (1) the stand-in tusker attacks a player within reach as ACE's monster does (`Monster_Melee.cs`, `Player_Combat.cs` 140-160, 299-346, 416-546): the attack motion, then 0x01B2 with F750 and F755 Splatter and 0x02E9, or 0x01B4, by a seeded chance; the protection's 0x0502 within 60 s of a respawn; (2) the player's death in ACE's order (`Player_Death.cs` 34-292, the preamble), the corpse's F745 with the drop list's removals, the F751 to the stand-in's lifestone, the 75% vitals; the suicide's answer (five speech lines, then the death without 0x01AC); (3) a stand-in lifestone (`BF_LIFESTONE`) and its Use (the Sanctuary motion, the attune line); `AddSpellFavorite` updates the stand-in's bars; closes `TODO(m3-standin-corpse)` for the player; (4) `PlayChecks.Death.cs`: the stand-in tusker kills the player, judged by `DeathTally` (`died=1 respawn=lifestone vitae>0 corpse=1`) | create `StandIn/StandInWorld.Death.cs`, `StandInDeathTests.cs`; edit `StandInWorld.Combat.cs`, `StandInWorld.Actions.cs`, `StandInVisit.cs`, `StandInServer.cs`, `PlayChecks.Death.cs`; tests | s0's handoff; docs/session.md §16 (the stand-in's fight, 854-857); `Player_Death.cs` 34-292; `StandInWorld.Combat.cs` | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with da, pa |
| pa the combat panel and the power bar | `gmCombatUI` 0x1000000C and `gmPowerbarUI` 0x1000000F: the toolbar's four combat-mode buttons change the mode through `IActionApi.SetCombatMode` (`gmToolbarUI::ListenToElementMessage` 0x004BFB20, `ToggleCombatMode`), the combat window shown per mode as retail shows it, its controls as the layout gives them (the melee and missile attack has no action in M3: its buttons answer `NotInM3`, logged), the power bar's meter as retail drives it; live on `CombatModeChanged`; tests over the layout; play-checks (Melee and Magic modes on the stand-in); frames (`--play-panel-shots` gains the combat windows) | create `Panels/GameCombatPanel.cs`, `Panels/GamePowerBar.cs`; edit `GamePanels.C1.cs`, `PlayChecks.PanelC1.cs`, `Toolbar/GameToolbar.cs`, `Play/PanelFramesPlan.cs` (its rows only); tests | s0's handoff and `layout-combat.txt`, `-powerbar.txt`; docs/retail-ui.md §21.5 (1317-1322), §23.1, §23.7 | `slice-check -MinTests <s0's> -Steps play-checks -Rerun` | with da, db |
| pb the spellcasting panel | `gmSpellcastingUI` 0x10000015: shown in Magic mode as retail does; the spell bars from `Spellbook.SpellBars` on b1's item list (icons and names from the SpellTable), the tabs, the selected spell; a click casts through `IActionApi.Cast` at the selection (or self for a self spell; no selection for a war spell is retail's message); live on `SpellbookChanged` and `CombatModeChanged`; tests; play-checks (bars from the stand-in's favourites); a frame. No automation clicks it live (D29's rule) | create `Panels/GameSpellcastingPanel.cs`; edit `GamePanels.C2.cs`, `PlayChecks.PanelC2.cs`, `Play/PanelFramesPlan.cs` (its rows only, after pa); tests | s0's handoff and `layout-spellcasting.txt`; docs/retail-ui.md §23.4, §23.6; `Spellbook.cs` | `slice-check -MinTests <pa's> -Steps play-checks -Rerun` | after pa; with dd |
| dd the leg, the death on screen and the loop offline | (1) `DeathLegRunner` (`--login-check=bsd-death`, the fight leg's shape: BSD Mage, the arrival, `DeathScenario` in the game's session with `GameFightWalk`, log off, the verdict), sampling `death_frame_max_ms` (the worst frame from the Dead motion to the first frame after the arrival, portal space included; report-only, §5) and the fight's fields; (2) the death on screen, each checked on db's stand-in and fixed where it fails (named): the Dead motion on the player's body and the stand-up, the corpse drawn (the player's Setup and ObjDesc, dead), portal space on the death's F751, the vitals meters falling; (3) the review frames with `--play-screenshot`: `-hit.png` (a 0x01B2 plus 0.2 s, the vitals down), `-dead.png` (the Dead motion plus 1.5 s, the death line in the chat), `-corpse.png` (the corpse's F745 plus 0.3 s, where it comes before portal space), `-lifestone.png` (the arrival plus 3.5 s: the lifestone and the 75% vitals); (4) the smoke step `login-death` (backend step, `-Backend require` only, after `login-bsd`); (5) the whole `DeathScenario` offline in play-checks against db's stand-in (no teleports), judged by the verdict; closes `TODO(m3-death-leg)` | edit `DeathLegRunner.cs`, `PlayChecks.Death.cs`, `tools/smoke.ps1`, `OptionCatalog*.cs`, the avatar or vitals file a check finds wrong (named in the handoff); create `DeathLegRunnerTests`; tests | da's and db's handoffs; docs/session.md §30, §31 (the leg, the frames, the sampler); `FightLegRunner.cs` | `slice-check -MinTests <merged count> -Steps play-checks -Rerun`; the probe (the lead) | after da and db merge; with pb |

Order: s0; then da, db and pa as lanes (da runs no Godot); pb when pa merges; dd when da and db merge (pb and dd may
overlap: two Godot runs); the probe after dd (the death is on the play path); `IncomingDamageLiveTests` and
`DeathLiveTests` live as soon as da merges (vitae and a corpse on BSD Mage: D17, D33); one Opus review (T4); Sonnet fixes
(T5) for the blockers and should-fixes; the doc pass (`cache/m3/3-2/doc-notes-*.md`; docs/session.md gains §32 "The death
loop", retail-ui.md §23.8 the combat windows); the gates (§5; the live gate adds both live tests to the loopback suite and
`login-death` to the full smoke). The live frames: the leg's four review frames and the three windows (Melee and Magic
mode on BSD Mage), sent to the owner with the placeholders named.

**As built (doc pass).** `Suicide` completes on the death, not on 0x01AC (a suicide sends none); `Purges` misses a purge
that removed nothing (`TODO(m3-purge-empty)`), so DoD 10's "0x02C6 seen" is read at the suicide's purge of a buffed mage;
the bind walks the player to the lifestone's use radius first and is done on the attune line (ACE answers UseDone before
its chain ends and moves an out-of-range player only while the client follows); `StopAfterHits` stops 3 s after the first
blow, not after a minute (review R6); the profile's spell bar and the Flame Bolt bar are bars 1 and 2, the second and
third tabs of the spellcasting window, because ACE's bar id is 0-based (R7); `corpse=` counts the death's own corpse only
(R1); the verdict freezes its measures in the world (R3); the corpse review frame is given up by name (ACE sends the
corpse and the teleport in one tick). docs/session.md §32 and docs/retail-ui.md §23.8 are the full account.

### 4.9 Item 4.1 Lights (branch `m3/4-1`; after 3.2)

Sliced by the 4.1 lead (P8) on master 40dc296, test baseline 7,820. **The spec's three unknowns, resolved (11.4186
decompile, read-only).** (1) **The viewer light** is a point light, white (0xFFFFFFFF), intensity 2.25, falloff 10, 2 m
above the player (z only; 0 for a non-player viewer), set in `SmartBox::set_viewer` (11.6096 0x00452C80), which clears
the dynamic list, adds the viewer light and calls `CObjCell::add_dynamic_lights`; not in `SmartBox::UseTime`
(0x004554B0) as the spec guessed. Both values are registry entries (`SmartBox.ViewerLightIntensity`, `.Falloff`,
registered by `SmartBox::SmartBox` 0x00454640); how often `set_viewer` runs is s0's to read (under 10 calls), named
`TODO(m3-viewer-light-cadence)` until then. (2) **The SetLight hook** is in one PhysicsScript of 4,248
(`PhysicsScriptTests` asserts the count, not the id): s0 pins the id and its on/off value. (3) **Particles are lit**:
`RenderDeviceD3D::DrawMeshInternal` (0x005A0470) calls `Render::minimize_object_lighting` (0x0054E090) for every mesh
drawn with the sun off, and particle parts draw through `CPhysicsPart::Draw` (0x0050E270); r2 confirms the route and
lights them by the same term (`TODO(objects)` in `ParticleShaders.cs` 50-51). **The landscape fog override
(`m3-environs-fog`) is not drawn** (dungeon distances are under the fog start, interiors.md §4.5), so it stays out of M3.

Every slice: per-frame code allocates nothing (fixed arrays, no LINQ or capturing lambdas); retail functions by name and
11.6096 address; each slice its own test files; new shader variants compiled at start-up or on a worker (the warm-up
checklist, §5). Check: `slice-check -MinTests <previous count> -Steps <steps> -Rerun`. At most two slices run Godot at once.

| Slice | Scope | Files | Read | Acceptance | Parallel |
|---|---|---|---|---|---|
| s0 the dynamic-light seam | (1) `DynamicLightList` (engine-agnostic): at most 7 entries (position, colour, intensity, falloff, owner, on), nearest the viewer first, by `Render::add_dynamic_light` 0x0054E030, `insert_light` 0x0054DDC0 and `remove_object_light` 0x0054CDC0; rebuilt per frame into fixed arrays; (2) the sources: the viewer light (the values above, `set_viewer`'s cadence read and recorded), a server object's Setup `ModelLight` (a projectile's on its CreateObject, dropped on F747 or a SetLight off), `EffectVisualState.LightsOn` linked; the SetLight script's id pinned by a test; (3) `ObjectLightSets.Select` merges the dynamic entries ahead of the static ones, 8 at most (`minimize_object_lighting`); `TODO(interiors-light-reach)` settled by the operand read (retail-addresses-m2.md §3.1) or left named; (4) `DynamicLightFrame`, the snapshot the Game layer reads (positions, radiance, reach; no allocation); (5) `DynamicLightsTests` (order, the cap, removal, the viewer light, the merge, a bolt's light moving with it); `--showcase` gains `dyn_lights=` counters (report-only) | create `Interiors/DynamicLightList.cs`, `Interiors/DynamicLightFrame.cs`, `DynamicLightsTests.cs`; edit `Interiors/ObjectLightSets.cs`, `CellObjectLighting.cs`, `Avatars/AvatarSet.cs` (the projectile's light on create and destroy only), `Effects/` (the `LightsOn` link only), `OptionCatalog*.cs` (append) | this preamble; the spec §5.6; docs/interiors.md §4.3-4.4; docs/avatars.md §17 (1025); `ObjectLightSets.cs`, `CellObjectLighting.cs`, `EffectObject.cs` 484 | `slice-check -MinTests 7820 -Rerun` (no Godot step) | none (first) |
| r1 the objects lit | the row writers merge the dynamic list ahead of the static lights per frame, no shader change (`PlayerView.cs` 358, `ObjectLightTable.cs`, the server objects' row writer): the player and the tuskers lit by a passing bolt and by the viewer light; the light follows the bolt across cells, since the list is global (`TODO(m3-bolt-cell-tracking)` stays for the bolt's own cell, named); `TODO(g5-dynamic-lights)` and `TODO(g5-object-lights)` closed; a play-check: the stand-in's Flame Bolt in a cell puts its light in the player's row while it flies (`dyn_lights_rows>=1`, judged offline) | edit `scripts/Player/PlayerView.cs` (the row writer only), `scripts/World/ServerObjects/ObjectLightTable.cs`, `ServerObjectsLayer.cs` (the row writer only), `PlayChecks.Lights.cs` (create) and one call in `PlayChecks.cs`; tests | s0's handoff; docs/interiors.md §4.4; `SurfaceLightingCode.cs` 100-155 | `slice-check -MinTests <s0's> -Steps play-checks -Rerun`; the probe (the lead) | with r2 |
| r2 the cell glow | a global dynamic-light table (RGBA32F, 8 lights, rewritten per frame from `DynamicLightFrame`) read by an additive per-vertex term in the EnvCell and CellObject shader variants (retail's hardware lights on the cell's polygons, `Render::minimize_envcell_lighting` 0x0054CD80; colour times intensity, range falloff times 1.5, attenuation 1/d, `PrimD3DRender::config_hardware_light` 0x0059BD40; D16: per vertex, no `OmniLight3D`); the same term on particle parts in cells (unknown 3); the variants compiled at start-up; an engine-check counter `dyn_light_cells=` (report-only) and an offline review frame of a stand-in bolt in a dungeon cell | edit `scripts/Render/SurfaceLightingCode.cs`, `scripts/Render/RenderGlobals.cs` (the table's upload), `scripts/World/Interiors/**`, `scripts/Render/Particles/ParticleShaders.cs`; create `scripts/Render/DynamicLightTable.cs`; tests | s0's handoff; docs/interiors.md §4.3-4.5; `SurfaceLightingCode.cs` 100-300 | `slice-check -MinTests <s0's> -Steps play-checks,engine-checks -Rerun`; the probe (the lead: new variants are on the arrival path) | with r1 |
| f the first-person fade | retail's fade of the player from 0.45 m to 0.2 m of camera distance on the body's parts (`TODO(g5-first-person-fade)`, `FirstPersonView.cs` 15, 46) through a camera-driven translucency the avatar set takes; wielded children follow the player's interpolated frame (`g5-translucency`'s remainder); a play-check at three camera distances | edit `scripts/Player/FirstPersonView.cs`, `PlayerView.cs` (the translucency path only), `Avatars/AvatarSet.cs` (one camera-translucency input); create `PlayChecks.Fade.cs`; tests | s0's handoff; `FirstPersonView.cs`; docs/avatars.md (the translucency section) | `slice-check -MinTests <merged count> -Steps play-checks -Rerun` | after r1 merges; with c |
| c the checks and frames | (1) the `bsd-fight` leg: `bolt_light=` (the bolt's entry in the dynamic list while it flies, read from the state; a new judge, report-only in this gate), `dyn_lights_max=`, `viewer_light=on`; (2) the review frames by `--play-screenshot`: the existing `-bolt` and `-hit` (now lit) and a new `-viewer.png` (the mage in a den corridor, no bolt, the viewer light round it); (3) `death_frame_max_ms` judged against 50 ms in `bsd-death` (§5: over 75 ms fails, over 50 ms prints `frame_over_50=yes`); (4) DoD 14's judged frame: the lead judges `-bolt` (the wall lit by the bolt) with the verdict's `bolt_light=yes` | edit `Scenarios/FightLegVerdict.cs`, `DeathLegVerdict.cs`, `FightFrameSampler.cs` (fields only), `scripts/Session/FightLegRunner.cs` (the frame plan), `OptionCatalog*.cs`; create `LightLegFieldsTests.cs`; tests | r1's and r2's handoffs; docs/session.md §31-§32; `FightLegRunner.cs` 116, 523-547 | `slice-check -MinTests <merged count> -Steps play-checks -Rerun` | after r1 and r2 merge; with f |

Order: s0; r1 and r2 as lanes (two Godot runs); one Haiku merge (T6); the probe after the merge (rows and shader
variants are on the arrival path); f and c as lanes; a Haiku merge; one Opus review at high effort (T4); Sonnet fixes
(T5) by disjoint files; the Haiku doc pass from `cache/m3/4-1/doc-notes-*.md` (docs/interiors.md gains §4.6 "Dynamic
lights", avatars.md §17 the bolt's light, session.md §31 the new fields, the §9 row 2.2-R16 addresses: 0x005981D0 and
0x005C93B0, the 3.2 cloud review's finding 15); the gates (§5: the loopback suite keeps Gear, Cast, BuffLoop, Fight,
IncomingDamage and Death; the full smoke's `login-buff`, `login-bsd`, `login-death`). The live frames: `-bolt`, `-hit`,
`-viewer` on BSD Mage in the den, sent to the owner with the placeholders named.

### 4.10 Item 4.2 The end (sketch; its lead slices it)

Seven cluster reviews, one per DoD cluster (1-2, 3-5, 6-7, 8-11, 12, 13-14, 15), each a read-only Sonnet agent with the
audit row, its evidence and the code it names, returning gaps as met / met with named gaps / not met; Sonnet fix slices
only for a "not met" or a wrong row; the audit table completed by the lead (§7 of the spec, every row with evidence); the
M3 hero video with sound (D15: BSD Mage buffs, fights with Flame Bolt lighting the den, dies, respawns at Holtburg and
re-gears; captured at 800x600 by the existing video tools into `cache/video/`, A/V sync measured before it is sent); §9
placed for M4; the Haiku doc pass; the gates as §5; the private push on the owner's word only.

### 4.11 Item 4.2 The end, with the polish pass (branch `m3/4-2`; after 4.1)

Sliced by the 4.2 lead on master ca77c67 (test baseline 7,881; play-checks 195). The owner's D39: the polish pass of
[m3-m4-polish-pass.md](m3-m4-polish-pass.md) (P1-P5) runs inside M3 as part of this item, before the M3 hero video,
which stays M3's (D15) and is cut to at most 30 s of action. Weekly meter at the start 3%; the stop line is 12% of a
week for the item (the meter at 15%). Models by D38.

**Phase 1, research (read-only, in parallel).** Seven cluster reviews (Sonnet), one per DoD cluster (1-2, 3-5, 6-7,
8-11, 12, 13-14, 15): the DoD items (spec §2), the audit rows (spec §7), the evidence they name and the code behind it;
each returns `cache/m3/4-2/cluster-<n>.md` with met / met with named gaps / not met, every claim checked against a test
or a recorded verdict line, every gap's TODO, the row's wording errors and its area's §9 rows placed for M4. Four
polish researchers (Sonnet): q2 retail's empty target panel (the layout in the dats, the 11.6096 functions that draw it
with no selection); q3 the combat and peace stance rules (what retail's client refuses in each mode and what ACE
refuses, by function and file); q4 the wall shots (4.1's live leg cast from `range_m=72.2` and logged `misses=3` of 7:
the target pick, the line of sight, the aim); q5 the testing loop measured in its own worktree (per-test durations of
the default suite, the smoke steps' times from the recorded logs, the slice-check stages; M4 §5.1), with a cut list for
the owner (M4 §6.1 Q5, asked here).

**Phase 2, the slices** (rows completed by the lead from phase 1's reports; disjoint files; at most two Godot runs at
once): p1 the cast bar bound for BSD Mage (tab I the buffs: Focus, Willpower and Creature Enchantment Mastery VI, then
the VIIs in the cycle's order; tab II the fight: Fire Vulnerability, Flame Bolt VII, Revitalize, Stamina to Mana, Heal
Self), by the death loop's gear stage that already writes the bars (3.2), BSD Mage only; p2 the target panel as retail
draws it with no selection; p3 the stance rules (client refusals by retail, server refusals read, each unknown a named
TODO); p4 the wall shots fixed so the frame shows a hit on the tusker; p5 the cuts the owner approves, each with a
before and after of the same step list; x the audit fixes (a "not met", a wrong row, a judge the reviews agree to turn
on); h the hero video's offline preparation (the story chain, capture, edit and the sync measure).

**Phase 3, the end.** A Haiku merge per lane set; the probe after an arrival-path slice; one Opus review (effort high)
of the item's diff and one fix round; G-A from the item worktree; the live gate (T7) from the main checkout and, in
the same backend window, the hero takes; the 30 s edit with A/V sync and durations measured; the Haiku doc pass (§8,
§0, the audit rows, §9 placed for M4, DoD 9's wording, the prose retitle "fights and sound"); the merge "Merge m3/4-2
(The end)"; D31's cleanup; the private push (the owner's word, D39).

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
  frame is what is judged); the same worst-of-three rule applies to the `bsd-fight` leg. 3.2 adds `death_frame_max_ms`
  (the worst frame from the player's Dead motion to the first frame after the death's arrival, portal space included;
  report-only in its first gate, the lead decides when it is judged against 50 ms) and judges `fight_frame_max_ms` in
  `bsd-fight` (over 75 ms fails, over 50 ms prints `frame_over_50=yes`).

**Mod installs (D6):** when an item changes `server/mod`, the lead runs `setup-server.ps1 -ModOnly -ModSource <the
item worktree>\server\mod` after the mod tests pass, and a plain `setup-server.ps1 -ModOnly` after the merge; each
install is a line in §8; never `-AllowStaleMod`.

**The checkpoint (D1):** after 2.3's merge, the wave-2 lead writes the spend per item against §3, the DoD items met so
far, the frames, and pings the owner; nothing is cut or started until the owner answers.


**Changed in M3 4.2 (D41, D42).** `smoke.ps1 -Backend require` implies `-SoloFrames` and `-FullScan` and prints
`frames: solo (-SoloFrames implied by -Backend require)` on its first line; the probe and the hero takes still need
`-SoloFrames` named on the command line. The live gate's smoke command is `smoke.ps1 -Backend require -SoloFrames
-FullScan -Parallel 2`; the probe adds `-SoloFrames`. G-A is `slice-check -Smoke -MinTests <default baseline>`: it
implies `-Exhaustive`, `-SoloFrames` and `-FullScan` and runs `dotnet test` once (smoke's test step is the run's
test). The default filter is `Category!=Loopback&Tier!=Soak&Tier!=Exhaustive`; `tools\slice-check.ps1 -Stages
exhaustive` (or `-Exhaustive`) runs `Tier=Exhaustive`. A `--play-frames` run is `--play-checks --play-skip-scenarios
--play-frames` in smoke. The frame judge of §5 is unchanged: over 75 ms fails, over 50 ms is flagged, and two of
three runs over 50 ms fail the gate.

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
| D33 | How the death loop gets an unbuffed mage (D17 assumed ACE has no player-side suicide; it has: GameAction 0x0279, `Player_Death.cs` 299-345, retail `CM_Character::Event_Suicide` 0x006A2920) | 3.2 da | a suicide when buffed | **The lead's, 3.2:** a death removes every enchantment (`Player_Death.cs` 211-216), so when any enchantment but the vitae is active the loop takes a `Suicide` in the den first (its corpse stays there); waiting out the buffs (an hour logged in) and a mod verb (an install and a non-retail tool) were rejected. A buffed run dies twice (two vitae steps) |
| D34 | Where the mage respawns (its sanctuary is its creation spot, `PlayerFactory.cs` 393) | 3.2 da | Holtburg's lifestone | **The lead's, 3.2:** each run binds BSD Mage at Holtburg's lifestone by a Use (`Lifestone.cs` 55-75; a change of its sanctuary, not a level or a reset, D3), so the death's portal space ends at a lifestone; `respawn=lifestone` reads a `BF_LIFESTONE` object within 15 m of the arrival |
| D35 | BSD Mage's vitae (5% a death, ACE's cap 40%; at level 126 a 1% step costs about 345,000 XP at 5% vitae and 35,000 at 40%, a kill gives 30,000), so it climbs to the cap and stays near it | 3.2, 4.2 | accept | **The owner's, 2026-10-17:** accepted (retail; D3 and D17 stand); the live tests assume no fixed spell level, and the verdicts print `vitae=` |
| D36 | Where DoD 9's `damage_taken>0 hits_taken>=1` is judged (the `bsd-fight` leg is a buffed fight: 0 hits in 3.1's three legs) | 3.2 s0 | the death leg | **The owner's, 2026-10-17:** judged in the unbuffed `bsd-death` leg; report-only in `bsd-fight`; DoD 9's text follows |
| D37 | BSD Mage at the vitae cap (40% after 3.2's live deaths) kills nothing in the den: the fight leg died at War 239, Flame Bolt V, health 123; kills cannot pay it back | 3.2 gate | a mod verb that clears it | **The owner's, 2026-10-17:** the vitae is paid back with the mod's existing `xp` verb (ACE `GrantXP` pays the vitae pool, `Player_Xp.cs` 53-137): the death loop's last stage grants the XP its death cost (about 7.1 million from 40% at level 126), never across a level; an exception to D3's "levelled only by the build and the kills" for the vitae alone |
| D38 | The model mix under the 2026-10-19 prices (per MTok in/out: Opus 5.5 $4/$20, Sonnet 5.5 $2/$10, Haiku 5.5 $0.10/$0.50) | 4.1, 4.2 | Haiku for the mechanical steps | **The owner's, 2026-10-19** (P3 amended): Haiku 5.5 for merges without code conflicts, doc passes, read-only lookups and cleanup; Sonnet 5.5 for code slices, lanes, fixes and cluster reviews; Opus 5.5 for the item review with effort set to high explicitly (its default is medium); the templates carry the prices |
| D39 | The polish pass (m3-m4-polish-pass.md) and the hero video | 4.2 | close M3 first, the video after the polish | **The owner's, 2026-10-19:** all of the polish pass (P1-P5) inside M3 as part of item 4.2, then the M3 hero video (D15 stays M3's): punchy, more action, at most 30 s; the item's stop line 12% of a week; the private one-commit push at the end; the six merged `worktree-wf_*` branches of 2026-10-11 deleted |
| D40 | A cast through the Action API outside Magic mode (retail's client has no mode check in `ClientMagicSystem::CastSpell` 0x00568DE0; its UI cannot send one, since the spell window and the cast keys exist only in magic mode; ACE refuses it with a silent UseDone, `Player_Magic.cs` 84-130) | 4.2 p3 | a client guard | **The lead's, 4.2:** refused at once on the client with a named reason (no wait for ACE's UseDone); `--retail-cast-any-mode` sends it as retail's client would. A Lifestone guard, not a glitch fix |
| D41 | The testing-loop cuts (P5; M4 §6.1 Q5 asked here), from the measured report `cache/m3/4-2/q5.md` (default suite 85 s, full smoke 27.1 min, loopback 22.1 min) | 4.2 p5a, p5b | the speed-ups and two removals | **The owner's, 2026-10-19:** C1 (the default filter skips `Tier=Exhaustive`; the tier runs once at G-A and before a merge), C2 (the three slowest Exhaustive tests parallel), C3 (play-checks and record-audio in one Godot run), C4 (shell-frames without the checks), C5 (the stand-in's tuskers at ACE's rate), C6 (per-test timings logged), C7 (one smoke process in slice-check; G-A's duplicate test run dropped), C8 (the secret scan of changed files between gates; parallel backend-free steps), C9 (`IncomingDamageLiveTests` dropped, its health assert moved to `DeathLiveTests`), C10 (the loopback idle 30 s), C11 (the mod-verb tests in 3 sessions); not C12 (the script tests keep Windows PowerShell 5.1). Estimated: a full smoke about 16-17 min, a loopback run about 19 min, a slice-check's tests 30 s |
| D42 | Parallel Godot runs and the frame judges (three runs share the machine; x3's play-checks failed twice at 52.8 ms in the known offline arrival-to-teleport window) | 4.2 p5a, every slice after | frames judged solo | **The owner's, 2026-10-19:** slices keep up to three Godot runs at once (CLAUDE.md unchanged); `slice-check.ps1` reports a frame-time check without failing on it while another Godot run is up (the step says `frames=contended`), and the frame judges count only in solo runs: the probe, G-A, the live gate and the hero takes. A Godot run is counted by its engine process (each run has a console wrapper too) |
| D43 | BSD Mage's look (the owner's additions to the polish pass: P6 the stand-in's humans dressed, P7 the classic armour; research `cache/m3/4-2/q6.md`, `q7.md`) | 4.2 p6, p7 | the classic rows | **The owner's, 2026-10-19:** BSD Mage wears the Shadow Amuli coat 6600 and leggings 6606 (armour level 190, the pre-nerf rows; the later family 7658/7689 is 150), the olthoi helm 3715 (the earlier id), Platemail Gauntlets 57 and Sollerets 107 in the Gold palette, and holds the orb Healer's Heart; the mod's `item` verb takes a palette and a shade as ACE's `@ci` does (one `-ModOnly` install, D6) |

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

**Item 3.1, BSD and the fight loop (2026-10-17).** Sliced in §4.7 on the lead's den map (D12). Changes from the table: fa
merged first (the lanes' files were disjoint); fa built its own `DenPlanner` (the stock `CellRoutePlanner` rejects the den's
11-degree doorways: `TODO(m3-fight-planner-lean)`) and widened the floor to 158 cells from the dat (the exit portal's room
excluded); the buff top-up runs `BuffCycle` directly (600 s margin); the range is ACE's unbuffed-skill rule. The lead's frame
review of the first live leg found the chat not following new lines (fix B: `UiTextElement.IsAtVerticalEnd` tested the
paper's bottom, retail 0x004695E0 the last glyph: a shared RetailUi widget fixed in this item) and the bolt and hit frames on
a wall hit (retargeted to a landing cast). Review (Opus): 0 blockers, 6 should-fix, 5 nits; fix A (R1 fight on until a damage
line, since ACE writes none for the killing blow; R2 the dying tusker; R4 the floor guard; R7), fix B (R3, R4, R6
`ProjectilesDrawn` renamed `ProjectilesShown`, R8, R9, R11, the chat); R5 and R10 to the doc pass and §9. Agents (calls / peak
context): survey 58/158K, s0 76/207K, fa 107/378K, fb 137/400K, fc 130/339K, fd 122/341K, review 74/269K, fix A 65/198K, fix B
71/184K, docs 50/189K; fa, fb, fc and fd ran over 300K (live ACE orders read from source, the den's planner, the warm-up, the
leg's stage machine, play-checks under three-way Godot contention). Probes: after fc login-check 66.5 ms once (the player's own
build under contention, the review found), rerun 45.5; after the merge 37.2 / 32.3. Live before the gate: `FightLiveTests`
`kills=1 damage_dealt=322 spells_cast=6 misses=3 xp_gained=30646 level=VII range_m=72.2` (the three bolts from the arrival hit
walls; one walk leg to 0x0383); the leg r1 `ok=true kills=1 damage_dealt=475`, r2 (fix B) the frames sent to the owner. G-A: 0
warnings, 7,592 default tests (base 7,457), smoke all pass `-Backend auto`, secrets 0. Live gate: loopback 33 passed, 1 skipped
(`ModXpLiveTests`), `mage=ready level=126 template=ok spells=247/247`, Gear, Cast, BuffLoop and Fight live tests pass; frames
login-check 35.8 / 33.5 / 32.9, login 30.8 / 26.4 / 37.9 (run 3's login-check failed on the known walker stall, its rerun in the
full smoke passed); full smoke `-Backend require` (login-throwaway skipped: untouched) all pass, 7,594 tests, `login-buff`
`buffs=24/24 rebuffed=yes`, `login-bsd` `ok=true kills=1 damage_dealt=576 spells_cast=7 cast_frame_max_ms=8.8
fight_frame_max_ms=8.3 bsd_min_fps=234.1`, secrets 0. No mod install. Spend: about 2% of a week by the meter (91% to 93%),
against 7.1%.

**Item 3.2, Damage taken, death and re-gear (2026-10-17; started before the weekly reset on the owner's word).** Sliced in
§4.8 (D33, D34 the lead's; D35, D36, D37 the owner's). Changes from the table: the spell bars moved from the build to the
death loop's gear stage (they land on the second and third tabs, review R7); da took five live fixes by message (the bind
waits for the attune line; an approach walk to the lifestone, since ACE waits for the client to follow its MoveToObject;
the walker placed from the settled position; an F747 releases a held item, `ReleaseDeletedHeld`; the respawn read in the
stage); the protection is dispelled by every teleport, so `protection=` reports it (R2); D37's vitae payback stage. Out of
the rows without a note (R5): `PlayMode.PanelShots.cs` (pa, pb), `StandInWorld.cs` and `StandInResponder.cs` (db),
`PlayChecks.Panels.cs` and `SessionMachine.World.cs` (s0), `GameStateReducer.Inventory.cs` (da). Review (Opus): 0 blockers,
3 should-fix, 7 nits; fix A (R1 R2 R3 R6 R8 R10, then two gate rounds), fix B (R4 R7 R9). Agents (calls / peak context): s0
129/413K, da 81/340K then five fixes (about 162 calls / 494K), db 141/406K, pa 139/375K, pb 124/350K, dd 136/399K then a fix
(161/432K), review 62/295K, fix A 72/206K then two rounds (182 calls / 366K), fix B 32/126K, docs 75/290K; s0, da, db, pa,
pb and dd ran over 300K (ACE's death order read across files, the stand-in's death, 34-function retail classes, the leg's
stage machine, play-checks under contention, and da's live fixes kept in one context). Live before the gate: six rounds of
the death tests (the bind, the approach, the re-gear, the respawn reading) and two of the leg (r2 `ok=true`, frames hit,
dead, lifestone; the corpse frame given up by name: portal space follows the corpse's F745 by 0.3 s). Probe after dd 53.3 ms
under three Godot runs, quiet rerun 33.5 / 33.5. G-A: 0 warnings, 7,760 default tests (base 7,592), smoke all pass with one
rerun of record-audio (its arrival-to-teleport frame at 74.5 ms beside the doc pass's tests), secrets 0. Live gate r1: frames
login-check 34.7 / 29.0 / 27.8, login 32.5 / 39.5 / 36.0; full smoke all pass, 7,762 tests, `login-death ok=true`, but
`login-buff` and `login-bsd` (and Buff and Fight live tests) failed at BSD Mage's 40% vitae (D37). r2 after fix A: the three
BSD legs pass, the vitae paid (6,875,357 from 40%). r3: legs `login-buff` 24/24 VII, `login-bsd` `kills=1`, `login-death`
`ok=true died=1 respawn=lifestone gear_restored=yes vitae_paid=3388767 vitae_after=0 death_frame_max_ms=6.2`, secrets 0;
loopback 34 passed, 1 skipped, `GearLiveTests` failed: `IncomingDamageLiveTests` logs off in the den unbuffed and the next
BSD test logged in among the tuskers (three live rounds: the owner asked, §5, and said go). r4 after fix A (a death run
leaves the den before its log off): loopback 35 passed, 1 skipped (`ModXpLiveTests`), `mage=ready`; the three BSD legs pass
(`login-death` `vitae_paid=3388767 vitae_after=0 death_frame_max_ms=6.0`), secrets 0; 7,780 default tests at the last fix.
No mod install. Spend: 93% to about 98% of a week by the meter, against 6.7%.

**3.2 follow-ups from the cloud session (2026-10-17, the owner's free cloud credit).** An independent review of 3.1 and 3.2 (no blockers, 9 should-fix, 9 nits; `cache/m4-cloud-results/PACKAGE-2.md`) and 17 patches for findings 1-14 and 16-18 (15 needs the 11.6096 symbol table), each reviewed and applied by the lead: the death frame window runs to the player's presence; a death mid-leg abandons the walk; both scenarios leave the den on failure and the fight on success (`DenExit`); the bind needs the attune line and the stamina halving, and the respawn is read against the bound lifestone; the hit wait follows the bolt's flight; the unbuff ignores lasting item spells and cooldowns; the spellcasting tab's readiness comes from the table; the c1/c2 frames and the power bar's placeholder; the fight sampler's flags; one projectile log line; the combat-mode fallback named; recklessness in melee only; the fizzle drop by the book; the NumDeaths baseline; the test rig's blow order (a one-blow death reads `hits_taken=0`: D36 kept, the lead's recommendation); ACE citations. Also the draft M4 spec `docs/milestones/m4-fights-and-loot.md` (a draft for the M4 planner). Checks: 0 warnings, 7,820 default tests, play-checks 193/0 (one rerun); live loopback 35 passed, 1 skipped; `login-buff` 24/24 VII, `login-bsd` `kills=1`, `login-death` `ok=true respawn=lifestone gear_restored=yes vitae_paid=3388767 vitae_after=0`, secrets 0.

**Item 4.1, Lights (2026-10-19).** Sliced in §4.9 (the owner's answers: retail's per-vertex glow kept; the meter share is
the stop line; D38 the model mix under the new prices). Changes from the table: the viewer light is set every drawn
frame by `SmartBox::set_viewer` (s0 read the cadence); r1 wired `--viewer-light` and r2's table source in PlayMode, and
the stand-in's bolt carries LightingOn as ACE's does (`SpellProjectile.cs` 218 clears it on impact); r2 found particles
lit by retail's route and lights them; f's held-item interpolation and the stand-in's wield parent moved to fix A.
Live before the review: `bolt_light=yes` (7 of 7 bolts lit their whole flight) but `fight_frame_max_ms=61.7` with one
surface pipeline compiled mid-fight: particle variants were compiled at start-up and never drawn (r2's fix: each drawn
once in a throwaway view; then 12.2); the `-viewer` frame fired at the arrival with the developer HUD drawn (c's fix).
Review (Opus): 0 blockers, 6 should-fix, 10 nits; fix A (R1, R3-R6) and the r2 fix (R2). The owner saw the player
holding a copy of itself in fix A's offline frame: the stand-in gave every created item the human Setup (fix A2: the
wand's own 0x020000FC, asserted). Doc pass by Haiku. G-A: one engine ERROR (the warm-up view's AddChild while the root
was busy; the lead deferred it), then play-checks 195/0. Agents (calls / tokens): research (Haiku) 35/85K, s0 66/208K,
r1 94/181K, r2 83/221K then the fix (112/277K), f 57/153K, c 63/171K then the fix (80/194K), review 50/219K, fix A
95/191K then A2 (123/213K), docs (Haiku) 33/172K; none over 300K; lane c's last report never reached the lead (its fix
sat merged-ready for 1.5 h). Live gate: loopback 35 passed, 1 skipped; frames login-check 48.3 / 37.4 / 25.5, login
34.9 / 25.9 / 36.9; full smoke 7,883 tests, `login-buff` 24/24, `login-bsd` `ok=true`, `login-death` `ok=true`, and
`login-check` failed on two 62 ms teleport frames (no compile), its rerun on the known walker stall, and live round 2 passed (35.4 ms, teleport 20.8), secrets 0. No mod install. Spend: 0% to 3% of a week by the meter, against 3.3%.

**Item 4.2, The end, with the polish pass (2026-10-19).** Sliced in §4.11; the owner's D39 (the polish pass inside M3,
the 30 s video after it), D41 (the testing-loop cuts), D42 (frames judged solo), D43 (P6 the dressed stand-in, P7 the
classic armour and Healer's Heart, added mid-item); the lead's D40 (the cast guard). Research: seven cluster reviews
(Sonnet, `cache/m3/4-2/cluster-k*.md`) and q2-q7, four of them continued by message as their slices. Agents (calls /
tokens): p1 77/221K, p2 94/214K, p3 55/272K, p4 79/219K, p5a 110/343K, p5b 78/210K, p6 61/307K (490K over four rounds),
p7 90/251K, x1 85/218K, x2 128/290K, x3 56/171K, x4 66/180K, h 89/234K (359K with round 2), fix A 54/155K, fix B 94/173K
(2.6 h over four rounds: the 12 ms UI step, then a GC margin the lead stopped), review (Opus, high) 93/300K (0 blockers,
6 should-fix, 7 nits), docs (Haiku) 61/264K; p5a, p6 and h ran over 300K (rounds kept in one context). Found on the way:
the wall shots were 44 m casts through the den's rock (live `misses=0 blocked_picks=3`); the hole was a misread `SetState`;
the naked figures were the offline stand-in's; the 53 s offline death wait was the lifestone protection; the player
holding a copy of itself again was the stand-in's human-Setup fallback (now a coin placeholder, judged). Mod install:
`setup-server.ps1 -ModOnly -ModSource <the item worktree>\server\mod` (the item verb's palette and shade). G-A: 0
warnings, 7,965 default and 76 Exhaustive tests, smoke all pass after one solo rerun (the 12 ms UI step at 13.2 ms).
Live gate r1: `mage=built spells=249/249`; loopback 30 passed, 1 skipped (20.7 min, was 22.1); frames login-check 37.0 /
29.8 / 36.3, login 31.6 / 30.9 / 36.5; full smoke 19.2 min (was 27.1), all pass but play-checks (the UI step at 14.8 ms:
fix B, then 196/0 at 8.1 and 9.9 ms solo); `login-bsd` `misses=0 wall_hits=0 bolt_light=yes`; the SELECT confirmed the
player's sound table 0x20000001. Hero video (D15): live takes with a sync marker each (-29 / -379 / -2 ms applied), 27 s,
audio and video equal, bolt +90 ms and portal -53 ms; the first takes (master's build, then no marker) were discarded.
The den's old corpses draw ACE's own ObjDesc (skin on the arms and legs): `q6-corpse-objdesc-live`, M4. Spend: 3% to 10%
of a week by the meter, against the owner's 12%.
## 9. Open TODOs

**Inherited from M2** (the audit table's "Open gaps", m2-log-in.md 1081-1099, placed by item):

| Gap | TODO | Placed |
|---|---|---|
| a buff cast or ended in the world shows only at the next description | `g4-enchantment-updates` | 1.2 s1 (closed in 1.2: 02C1 to 02C8 and 0312 are applied to the registry, and `QualitiesChanged(QualityChange.Enchantments)` follows each change) |
| the server's scripts and sounds on objects are not played | `g5-w10-scripts`, `g5-w10-sounds` | 1.3 s0, s1 (closed in 1.3: `g5-w10-scripts` by s0, `g5-w10-sounds` by s1) |
| UI sounds wired, unheard; no check hears audio | `ui-sound-sink`, `record-audio-check` | 1.3 s2a, s3 (closed in 1.3: `ui-sound-sink` by s2a, the UI's sounds reach the UI bus; `record-audio-check` by s3, the smoke `record-audio` step judges the recording non-silent) |
| no dynamic lights; the object light reach rule | `g5-dynamic-lights`, `g5-object-lights`, `interiors-light-reach` | M4 (combat visuals; the owner's byte read first) |
| the portal's per-vital enchantable table; the skill groups in the cull | `g4-quality-filter`, `g4-enchantment-affects-skills` | closed in 2.2 (EnchantmentRules.cs:159, SkillRules.cs:19); removed from the 1.4 R12 row |
| 14 of 81 layouts have screen logic | `retailui-runtime` (the 13 panel classes) | 2.2, 2.3, 3.2 (done for the three combat windows in 3.2: `gmCombatUI`, `gmPowerbarUI`, `gmSpellcastingUI`, retail-ui.md §23.8) |
| LoginComplete does not wait for the description and contents | `portal` (docs/portal-space.md §15 item 15) | 1.2 s5 (closed in 1.2: the wait for the description and the contents, with a 3 s cap, in `LoginCompleteTracker.WaitReason`; the director's cue is unchanged) |
| the first-person fade; a wielded item's children are not interpolated with the player | `g5-first-person-fade`, `g5-translucency` | 4.1 (D30; was 2.3): closed in 4.1 (f: the fade; fix A: the held item's frame, now `g5-held-item-interpolation`) |
| server-object twins built eagerly; Holtburg's signs and the other excused objects not shown drawn | `serverobjects-lazy-twins` | not in M3 (no M3 item touches the Holtburg census) |
| the camera and look TODOs (wheel zoom, overhead and map views, slope, first-person turn, mouse-look preferences); no frame of the walk or run | `g5-wheel-zoom`, `g5-camera-overhead`, `g5-camera-map-view`, `g5-camera-slope`, `g5-first-person-turn`, `g5-mouselook-prefs` | not in M3 (the fight is third person; a walk frame joins 4.2's frames) |
| the refused leg's end position compared only through `where` | `st16-refused-where` | not in M3 |
| Where am I from the server alone on the headless host; the stand-in's replies unlabelled | `g7-where-client-body`, `g7-standin-where` | not in M3 |
| a really stopped ACE shown only on the stand-in | (DoD 12 of M2) | not in M3 (no fight run stops the server) |
| the mid-air logoff refusal; the PK logoff delay | `g6-logoff-midair`, M2 D33 | not in M3 (no fight ends in mid-air; no PK) |
| the window close in the world has no live proof | (DoD 11 of M2) | 4.2's audit, through `play.ps1` by the owner |
| the soak log does not print the CRC and duplicate counts | (DoD 12 of M2) | 4.2's final soak |
| the real-recording replay tests skip without `cache/sessions` | `replay-tick-boundary` | none (a guard) |
| the burst frame near 50 ms; the 12 ms UI-step sub-budget | `login-burst-frame` | M4 (testing and frame attribution, the owner's D26 note) |
| the 60 fps rule is a one-second average, not a worst frame | (DoD 15 of M2) | §5: the fight judges the worst frame; `holtburg_min_fps` stays a report |
| the Entering World dialog on the create path | `retailui-chargen-enter-dialog` | not in M3 |
| window layout and options saves, chat channels, the busy pointer | `g4-window-layout-save`, `g6-options-save`, `g4-chat-*`, `g6-busy-cursor-image` | not in M3 |
| the walker's rare stall; the physics open questions | `holtburg-ring-stall`, `physics-thin-doorway`, `physics-landing-velocity` | none (a guard) |

**Added by M3's items:** (appended by each lead with the review's nits.)

| From | Gap | TODO | Placed |
|---|---|---|---|
| 1.1 s0 | Flame Volley's ids 3663/3664 repeat levels III/IV and Flame Blast has no level II, so neither runs I-VI by id | `m3-volley-blast-levels` | M4 (fights; only if Volley or Blast is ever cast) |
| 1.1 s0 | the XpTable decoder's loud failures on a short or malformed table | `m3-xp-table` | none (a guard) |
| 1.1 s2a | `item` does not refuse creature weenies (`@ci` does not either) | `item-creature` | M4 (loot: the item verb and creature weenies) |
| 1.1 s1b, s2b | the stand-in's raise chat line, skill names, XpTable fallback, SpellTable fallback, weenie knowledge and pack wire are placeholders | `m3-standin-raise-chat`, `m3-standin-skill-names`, `m3-standin-xp-table`, `m3-standin-spell-table`, `m3-standin-weenies`, `m3-standin-pack-wire` | closed in 4.2 x3 (logged once at runtime) |
| 1.1 s3b | a trained-later skill without a `TrainCredits` entry fails loudly | `m3-template-credits` | none (a guard) |
| 1.1 (owner's question) | the buff cycle must cast in dependency order (Focus and Willpower, then Creature Enchantment Mastery, then the other masteries, then the rest) and recompute each line's chance from the buffed skill after every cast; with retail's magnitudes (VI +35, VII +40) BSD Mage reaches level VII at 91% or better in every school after the three VI buffs, while VIII stays near 16% (War about 59%) (refined by 2.1 s0a: the third buff, Creature Enchantment Mastery, is already VII at 0.9097 from 333; VIII is 13% for Creature, Item and Life and 53% for War at the end of the chain); the StatMod magnitudes are not in the dats and must be pinned by a read-only SELECT of ACE's spell table | (none yet) | closed in 2.1: the order is `BuffCycle`'s; the chain pinned by `BuffChainDatTests` (Focus VI, Willpower VI, Creature Enchantment Mastery VII, the rest VII; VIII 13%, War 53%) and the magnitudes by `BuffStatMods` (the SELECT of 2026-10-16) |
| 1.2 s0 | a live enchantment's bucket is derived (vitae spell, cooldown category, stat-mod bit) | `m3-enchant-bucket` | closed in 1.2 s1 (retail's rule is the type bits, UpdateEnchantment 0x00593E70 / 0x00594CE0; the derived bucket agrees for real data) |
| 1.2 s0 | the retail handlers of 0024, 0023 and 01AD are not in the 11.6096 symbol table (01AD's chat type is assumed 0) | `retail-handler-0024`, `retail-handler-0023`, `retail-handler-01AD` | none (a guard) |
| 1.2 s0 | EA60 AdminEnvirons is decoded and counted, not applied | `m3-admin-environs` | M4 (outside the goal; backlog) |
| 1.2 s1 | an overdue enchantment is reported only on demand (`CountOverdueEnchantments`), counted at the next enchantment message | `m3-enchant-overdue` | none (a guard: report-only) |
| 1.2 s1 | the "X has expired." line is left to a layer with the spell table | `m3-enchant-expiry-chat` | closed in 4.2 x1 (the expiry line is printed for 0x02C3 and 0x02C5) |
| 1.2 s1 | the registry's qualities event | `m3-enchant-qualities-event` | closed in 1.2 s4b |
| 1.2 s1 | bucket parity between the derived bucket and the type bits | `m3-enchant-type-parity` | none (a guard: report-only) |
| 1.2 s1 | a spell removal (retail's `Handle_Magic__RemoveSpell`, 11.4186 0x00567890) is not built | `m3-spell-removal` | none (a guard: ACE sends it only on a deliberate forget) |
| 1.2 s2 | side-pack contents arrive as CreateObjects with a container; they are not waited for at login | `m3-inventory-sidepack-contents` | M4 (M4 5.4 containers: side packs show their contents); its WARN removed in 4.2 x4; **closed in 2.1 s1** (the model; the panels are 2.2's) |
| 1.2 s2 | the instance properties other than Container and Wielder (02DA) are not read | `m3-inventory-instance-properties` | M4 (loot: read when a pack item's properties matter); **closed in 2.1 s1** (Value and ItemType are on `WorldObject`) |
| 1.2 s2 | a foci's container type 2 cannot be told from its CreateObject | `m3-inventory-foci` | none (a guard: the kit carries the foci formula's components) |
| 1.2 s2 | the server sends no burden capacity; retail derives it from Strength | `m3-burden-capacity` | closed in 4.2 x1 (the character panel derives it from Strength: GameCharacterInfo.LoadOf) |
| 1.2 s3 | retail skips the combat lines for a squelched name | `m3-squelch-combat` | M4 (chat) |
| 1.2 s3 | retail auto-targets the attacker after 01B2 and 01B4 | `m3-combat-autotarget` | closed in 4.2 p2 (GameStateReducer.RequestAutoTarget; the attacker-id and select-next names stay) |
| 1.2 s4a | the six action stubs in `ActionApi` (`NotBuiltTracker`) | `m3-1-2-action-trackers` | closed in 1.2 s4b (the trackers replace the stubs) |
| 1.2 s4b | a projectile's caster is not on the wire: still open, so a missile another creature launches from within 5 m of the player along the same line, wearing the same Setup, would be taken (session.md section 7 has the rule) | `m3-cast-projectile-attribution` | M4 (missile and melee fights: the wire names no caster) |
| 1.2 s4b | a buff on another target is `Sent` when UseDone 0 ends the cast, with no proof of its effect | `m3-cast-other-target-proof` | M4 (recovery: heal others, needs a proof source) |
| 1.2 s4b | the host did not set the SpellTable, so every cast was "Unknown" | `m3-cast-no-spell-table` | closed (renamed m3-cast-no-spell-table in 4.2 x1: every use) |
| 1.2 s4b | picking an item up from the ground is not an action yet | `m3-pickup` | M4 (loot and trade); **closed in 2.1 s2** (`PickUp`) |
| 1.2 s5 | the recorded XP placeholder: the experience `Stats` carries is made up to the recorded character's (the raise check spends it; logged once: 1.2 review R8) | `m3-standin-recorded-xp` | none (stand-in placeholder, labelled) |
| 1.2 s5 | LoginComplete does not wait for side-pack contents | `m3-login-complete-side-pack-contents` | none (a guard: no check needs it yet); **closed in 2.1 s1** (LoginComplete waits for the side packs' contents by their 0196) |
| 1.2 s5 | the stand-in does not send side-pack contents | `m3-standin-side-pack-contents` | none (stand-in placeholder, labelled); **closed in 2.1 s3** (`RecordedDescription.cs:27`) |
| 1.2 review | `Cast(uint, int)` refuses every negative int, so an int-typed guid of 0x80000000 or more (every monster) becomes the refused "no target" (R7, `IActionApi.cs:163`) | none | closed in 1.4 s2 (`Cast(uint, int)` refuses only 0; `IActionApi.cs` and `ActionApi.cs`; session.md section 7) |
| 1.2 review | the stand-in's recorded-XP placeholder is silent: nothing is logged when it raises PropertyInt64 1 and 2 (R8, `StandInVisit.cs:473`) | `m3-standin-recorded-xp` | closed in 1.3 s0 (logged once) |
| 1.2 review | stale comments name closed TODOs (`GameStateReducer.Enchantments.cs:21`; the `AdoptHeldObject` "not wired yet" comment), and `m3-cast-no-spell-table` is still the name of the no-table outcome (R9) | `m3-enchant-qualities-event`, `m3-cast-no-spell-table` | closed (renamed m3-cast-no-spell-table in 4.2 x1: every use) |
| 1.2 review | a held item deleted by F747 alone stays in the inventory model; each later LoginComplete then waits the 3 s cap (R10, UNCERTAIN: ACE sends 0024 first) | none | closed in 1.4 s0 (not reproducible on ACE's order; `m3-inventory-delete-alone` names the F747-alone case) |
| 1.2 review | the description's registry is stamped with the clock, and the 02C2 and 02C4 path with the message's timestamp (R11) | none | the next slice that edits those files |
| 1.2 review | out-of-glob edits (the description's spellbook and inventory calls, `SelfState.cs`) were not flagged in the lane handoffs; each was verified in the review (R12) | none | none (verified in review) |
| 1.3 s2b | AdminEnvirons' fog and ambient override (options 0, 1-6, 9999) are published and logged, not drawn | `m3-environs-fog` | M4 (outside the goal; backlog) |
| 1.3 s2b | the ambient sound has no slider or enable flag of its own: it plays on the effects bus and slider | `ambient-slider` | none (a guard) |
| 1.3 s2b | an EnvCell that sees outside (a doorway) counts as indoors for the ambient sound, where retail counts it as outdoors | `ambient-seen-outside` | M4 (world sound: the doorway rule) |
| 1.3 s2b | `ConstantSound`'s PlayNow and GetSoundPos are not in the 11.6096 symbol table: taken from the base class (always, none) | `ambient-constant-defaults` | none (a guard: no check needs it yet) |
| 1.3 s1 | the avatars' sound listener is the player's body, not the camera (retail's `SmartBox::set_viewer` -> `SoundManager::SetPlayerPosition`): other avatars' pans differ by the camera offset in third person | `avatars-listener-camera` | closed in 4.2 x2 (AvatarSet.Listener is the camera in play mode) |
| 1.3 s1 | the human sound table 0x20000001 has no variant for Footstep1/Footstep2 (types 55 and 56) or for type 1: the hooks reach the sound player, which warns once per type and plays nothing; answered in 2.1 s2b, no change: retail has no other source (`SoundTableHook::Execute` 11.4186 0x00526AB0, 11.6096 0x005276B0 calls `SoundManager::PlaySoundA(SoundType, CPhysicsObj*)` 0x00550B70, 11.6096 0x00551780, which reads only `CPhysicsObj::sound_table`, set by `CPhysicsObj::InitDefaults` 0x005139D0, 11.6096 0x005144D0, from the Setup's default sound table and by `CPhysicsObj::set_description` 0x00514F40, 11.6096 0x00515A40, from the CreateObject's stable id, then `SoundManager::GetSound` 0x00550680, 11.6096 0x00551290, and `CSoundTable::Lookup` 0x00552100, 11.6096 0x00552D20, and returns nothing when the type is missing: no default table, no terrain or surface type, no fallback). The dats: the human Setup 0x02000001 has no default sound table, the human walk and run cycles (0x03000003, 0x03000002) carry SoundTable hooks of types 55 and 56, 414 animations carry them, and of the 190 sound tables only six have type 55 or 56 (0x20000016, 0x2000001E, 0x2000008C, 0x20000090 (55 only), 0x20000093, 0x200000AA: creature tables); the human table 0x20000001 has neither. The router's `PlayerFootsteps` (handed) counts them and `PlayerFootstepsSounded` stays 0, so the DoD's "footsteps sounded" cannot be judged on the human | `footstep-sound-table` | named in 4.2 x2 (SoundHookRouter, logged once); the SELECT confirms SoundTable 0x20000001; footsteps stay silent: none (a guard) |
| 1.3 review | portal sounds alone pass the `record-audio` step: it judges non-silence only, and the per-source counts (`sources=`) are report-only (R7; DoD 13's last clause) | none | closed in 2.1 s3: record-audio judges server>=1 and hooks>=1 in `sources=` (`Test-RecordedSources`, `BuffLegJudgesTests`) |
| 1.3 review | the stand-in's full mix peaks at 0.0 dBFS (summed voices with no limiter, as retail's DirectSound buffers saturate; 1095 hook voices in 98 s is a dense mix): add a clipped-sample count to `WaveLevels` (R8, UNCERTAIN) | none | none (stand-in placeholder, labelled) |
| 1.3 review | `CountDecodedNotApplied` lost its test with `ReducerM3HooksTests`' empty-data theory; `AdminEnvironChanged` (a `Session.Contracts` type) sits in `Client/Audio/AmbientEnvirons.cs` and `ReducerHarness` does not record it (R13) | none | the next slice that edits those files |
| 1.3 fix B | the arrival's frame attribution has no counter for the server objects' particle meshes (`particle_meshes_server=n/a`) | `server-particle-meshes-counter` | none (a guard: no check needs it yet) |
| 1.3 live gate | the arrival's burst frame: 53.4 ms once in five live login-checks (stage arriving, 1.41 s after Enter; one message pumped in 14.9 ms; the player's F745 subscribers 7.1 ms; the s1 probe saw an 11.2 ms single-message pump) | `login-burst-frame` | M4 (testing and frame attribution, the owner's D26 note) |
| 1.3 live gate | live, the Academy arrival leg heard no server sound (`sounds_server=0` in 3 of 3 login-checks; one login saw 1), so a `>=1` judge there would fail: check whether ACE sends an F750 in that window or the leg needs to be wider | none | 2.1 (D27): ACE sends no F750 at login; judged in the buff leg (the wand re-seat's wield sounds); the arrival's field stays a report |
| 1.4 s0 | a held item deleted by F747 alone stays in the inventory model (not seen on ACE's 0024-then-F747 order; a test pins the case) | `m3-inventory-delete-alone` | closed in 3.2 da fix 4: ACE does send F747 with no 0x0024 for a wielded item dropped at a death; `ReleaseDeletedHeld` releases it (session.md §32) |
| 1.4 s0 | `m3-standin-weenies` is answered for the kit's weenies only (`StandInWorld.KitWeenies`); every other class id behaves as before | `m3-standin-weenies` | none (stand-in placeholder, labelled) |
| 1.4 s0, s1 | the kit's component burdens were an upper bound of 6 | `m3-kit-burdens` | closed in 1.4 s1 (the SELECT of 2026-10-16: every kit component weighs 4; `GearKit.ComponentBurden`) |
| 1.4 s1 | the stand-in's refused wield is a test hook (`RefusedWieldWcids`), not ACE's wield rules | `m3-standin-wield-rules` | none (stand-in placeholder, labelled) |
| 1.4 s2 | the stand-in's magic skill for the fizzle rule is a fixed 300 | `m3-standin-magic-skill` | none (stand-in placeholder, labelled) |
| 1.2 s4c, named in 1.4 | the stand-in's spell stat modifiers, mana rules and component burn are placeholders (its casts burn nothing; `StandInWorld.Burn` is a test hook) | `m3-standin-spell-statmods`, `m3-standin-cast-mana`, `m3-standin-cast-components` | none (stand-in placeholder, labelled) |
| 1.4 review R8 (fix A) | a held focus item (15268-15271, 43173) switches ACE's casts to the foci formula, which needs the Prismatic Taper (188) the kit lacks; `SpawnGear` refuses loudly while one is held | `m3-kit-foci` | closed in 1.4 fix A2 (live: BSD Mage holds the Foci of Strife, 15271; the kit carries the foci formula's components, the Prismatic Taper among them, and the refusal is gone) |
| 1.4 fix A2 | a headless run whose scenario fails disconnects without logging off (the runner disposes the session), so ACE keeps the character in the world and the next login is refused `CharacterInWorld`; `GearScenario` now logs off on a failure, LoginScenario, MageBuildScenario, ThrowawayLeg and the login check do not | `headless-failed-logoff` | none (a guard: no check needs it yet) |
| 1.4 review R12 (fix B) | the current-skill rule omits the augmentation bonuses (`GetAugBonus_Base`, `GetAugBonus_Current`; BSD Mage has none) and, as before, the attack and defence groups | `m3-skill-aug-bonus`, `g4-enchantment-affects-skills` | closed in 4.2 x4 (SkillRules.AugBonusBase/AugBonusCurrent) |
| 1.4 fix B | a `GearScenario` built without a SpellTable skips the mana check, logged (the headless host and the live tests always pass it) | `m3-cast-mana-unknown` | none (a guard) |
| 1.4 fix A | a whole merge inside the pack sends 0024 and 0197 with no F747, so the source's world object lingers in the client's object table (harmless: the inventory model forgets it) | `retail-handler-0024` | none (a guard) |
| 1.4 s2 (D20) | the plan's login-check `cast=` field: login-check plays Lifestone Smoke, which has no spells, wand or components | none | closed in 2.1: the buff leg carries `gear=` and `cast=` |
| 1.4 s2 (D21) | War Magic's first live cast: ACE refuses an untargeted war spell (`Player_Magic.cs` 1379-1383) | `m3-cast-projectile-attribution` | M4 (missile and melee fights: the wire names no caster) |
| 1.4 lead (D22) | DoD 6's `gear=wielded worn=<n>` leg: the field is the `--scenario=gear` verdict, judged by `GearLiveTests` | none | closed in 2.1: the buff leg carries `gear=` and `cast=` |
| 1.4 review R9 | s1 edited `src/Lifestone.Client/Play/PlayOptions.cs` (the `--play-screenshot` parse) outside its file list, without a note | none | none (verified in review; noted in session.md section 21) |
| 1.4 G-A | `GamePlayScreenTests.A_staged_shell_is_built_over_frames_and_holds_the_portal_notice_for_its_spew_box` failed twice under the full parallel test run ("the held notice reaches the spew box"), passed 3 of 3 full reruns and 6 of 6 alone; this item touched no RetailUi file | `gameplay-staged-notice-flake` | polish P5 (test slimming) |
| 2.1 (D26, the owner) | the testing loop takes a very long time to run (the loopback suite about 15 minutes, the Soak expiry about 31): look at cutting it down | none | M4 (a note only; nothing changes in M3) |
| 2.1 s2a | the stand-in's cast gestures (F74C per wind-up and gesture, the words, the caster and target effects) | `m3-standin-cast-gestures` | closed in 2.1 s2a (`StandInWorld.Cast.cs`); opened there: `m3-standin-cast-plan` (a visit with no cast planner), `m3-standin-vital-spell-table` (below) |
| 2.1 s2b | the stand-in keeps no enchantment registry for items, so its armour cast adds nothing to a piece | `m3-standin-item-enchantments` | none (stand-in placeholder, labelled) |
| 2.1 s3 (live) | the walking legs' `sounds_hooks>=1` judge is new in the walking legs; the 1.3 live gate's per-leg split was never printed, so whether the Academy exit and the Holtburg laps sound hooks on their own (not only the arrival's scripts) is first seen live; if a leg's tally is 0 the judge fails and the line `walking legs' sounds judged` says the sum (D28; the owner decides: keep, or judge the whole run) | none | the 2.1 gate |
| 2.1 review R1 (fix A) | the stand-in's cast planner loads a second SpellTable (the session's own `SpellTableHost` loads one too) and keeps it for the run; the 12 ms UI-step misses of s3's runs did not reproduce in fix A's six runs (7.1-10.4 ms; the planner's worker ends 0.19-0.76 s before the arrival), so they are taken as machine load; one resident table needs a `GameSession` accessor | `m3-standin-spell-table-shared` | none (stand-in placeholder, labelled) |
| 2.1 review R2 (fix B) | the scripted caster models ACE's mana cost with Mana Conversion (`ScriptedCaster`, 59-61 s for a cycle against the live 56 s), but casts 0-1 mana lines where the live run cast 4 (the live Mana Conversion skill was not recorded) | none | none (a guard: no check needs it yet) |
| 2.1 review R8 | the session's `[net]` lines log the account name at login ("LoginRequest for account ..."), in gitignored logs only; the buff leg keeps the name inside its loading worker (fix A) | none | **closed in M4 1.1 s3** (no session log line carries the account name: `AccountNameLogTests`) |
| 2.1 review R10 (fix B) | nothing drives the timed re-buff (`BuffCycle.RebuffDue`, `StartRebuff(forced: false)` are called only by tests); DoD 5's "re-buffed before it expires" is proved by the fake clock and the forced pass (D25); the auto-buff goal will own the driver | `m3-buff-rebuff-driver` | M4 (auto-buff; the cut candidate) |
| 2.1 s0b | the armour lines' expiry lives in the cycle object, so a new object recasts both armour lines (the client cannot read an item's enchantments without an appraisal) | `m3-buff-armour-memory` | M4 (auto-buff) |
| 2.1 s0b | a surpassed armour piece line is counted a miss | `m3-buff-armour-surpassed` | closed in 3.2 gate round 1 (the piece counts active, `surpassed=`) |
| 2.1 s3 | the game's buff leg ignores the `--buff-*` options (it uses the defaults) | `m3-buff-leg-options` | none (a guard: the leg uses its defaults) |
| 2.1 s2a, fix A | the stand-in changes no vital for the other five vital lines' level VII spells, the heals and the other boosts (the Stamina to Mana rows are `BuffStatMods`'s since fix A) | `m3-standin-vital-spell-table` | none (stand-in placeholder, labelled) |
| 2.1 s2a, s2b | the stand-in's offline `--play` plans a spell outside the profile when it is cast; the projectile's Launch F755 and hit effect are not sent | `m3-standin-cast-plan-lazy`, `m3-standin-projectile-model` | closed in 3.1 fb (`StandInVisit.CastPlannerFactory` plans any spell once, on the casting thread; `StandInWorld.Combat.cs` launches ACE's projectile with the Launch F755, the hit in `ProjectileImpact`'s order and the damage line, for a spell the host knows a projectile for; a spell it has none for keeps the labelled placeholder; session.md section 16) |
| 2.1 lead | the offline play-checks' teleport-window frame reads 58-72 ms in every run since at least 1.4 (report-only there; the live teleport frames read 7.9 ms) | `login-burst-frame` | M4 (testing and frame attribution, the owner's D26 note) |
| 2.2 review R12 | the "idle layer frame allocates nothing" check runs on a synthetic screen; only the effects and vitae pages' `Step`s are pinned at 0 bytes | none | none (a guard: no check needs it yet) |
| 2.2 review R14 | the skills list (38-42 rows) is built in one unit on the tab's first open (about 6 ms cold, under the 8 ms rule, not filled over frames) | none | none (accepted; split at the section boundaries if a frame needs it) |
| 2.2 review R15 | s0 edited `scripts/Session/PlayMode.cs` and created `PlayMode.PanelShots.cs` outside its row, without a note (every other out-of-glob file carries one in its handoff) | none | none (noted here) |
| 2.2 review R16 | `CSpellBase::InqSpellLevelByRoughHeuristic` (`GameSpellbookPage.cs`) and `ExperienceSystem::ExperienceToSkillLevel` (`SkillRules.cs`) are cited without an 11.6096 address | none | closed in 4.1 docs: `CSpellBase::InqSpellLevelByRoughHeuristic` 11.6096 0x005981D0 and `ExperienceSystem::ExperienceToSkillLevel` 0x005C93B0 (the 3.2 cloud review's finding 15; cited in the source) |
| 2.2 review R17 | a double click wields with the item's whole `ValidLocations`; an item of two alternative slots (a ring) sends both bits, and whether ACE refuses that is unverified | `m3-wield-slot-choice` | closed in 4.2 x4 (the first free location, the right side first; `TODO(m3-wield-second-choice)` to confirm live) |
| 2.2 live gate | on BSD Mage the spellbook's first open (247 spells, 8 a frame) does not repaint the page area for more than 8 frames: the page before shows meanwhile | `m3-spellbook-first-paint` | closed in 4.2 x4 (the fill waits for each icon; the live check of the first open is pending) |
| 2.2 slices | the panels' open names: the stat page's heritage line, PK status and titles tab; skill tooltips; effects and vitae strings; the item list's use, examine and extras; side-pack contents unloaded until opened; the paperdoll's part lighting, click map, tooltips, sharp mode and scale; the spellbook's filter send, delete and shortcut; the components' icon, category and buy rate | `m3-attribute-heritage`, `m3-pk-status`, `m3-character-titles`, `m3-skill-tooltips`, `m3-effects-strings`, `m3-vitae-strings`, `m3-itemlist-use`, `m3-itemlist-examine`, `m3-uiitem-extras`, `m3-inventory-sidepack-contents`, `m3-paperdoll-part-lighting`, `m3-paperdoll-clickmap`, `m3-paperdoll-tooltips`, `m3-paperdoll-sharp`, `m3-paperdoll-scale`, `m3-spellbook-filter-send`, `m3-spellbook-delete`, `m3-spellbook-shortcut`, `m3-component-icon`, `m3-component-category`, `m3-component-buy-rate` | M4 (M4 5.4 containers: side packs show their contents); its WARN removed in 4.2 x4 |
| 2.2 live gate | login-check failed once on two frames after the academy-exit teleport (129.1 and 76.2 ms) whose process work was 4.8 ms (no pipelines, no collection): the time was outside the frame's own work; the three frame runs of the same gate read 20.8-34.1 ms there | `login-burst-frame` | M4 (testing and frame attribution, the owner's D26 note) |
| 3.1 s0 | the Black Spawn Den's fight leg was a stub that failed loudly | `m3-fight-leg` | closed in 3.1 fd (`FightLegRunner`, `--login-check=bsd-fight`; session.md section 31) |
| 3.1 fa | the stock `CellRoutePlanner.IsWalkable` refuses the den's plaza doorways (planes leaning 11 degrees, normal Z 0.196), so it reaches 1 spawn cell of 12; `DenPlanner` (a BFS over the floor cells) replaces it for the fight; the planner could take the lean as a parameter | `m3-fight-planner-lean` | M4 (a hunt loop that walks another dungeon) |
| 3.1 fa, fd | the fight's walks (`DenWalk`, `GameFightWalk`) are not audited against ACE's positions as `RoutePlay`'s are | `m3-fight-walk-audit` | M4 (a hunt loop that walks another dungeon) |
| 3.1 fa | a fight leg with no walker fails loudly | `m3-fight-no-walker` | closed in 3.1 fd for the game (`GameFightWalk`); the headless host keeps `DenWalk` |
| 3.1 fa, fix A | the walker left the den's floor (landblock 0x0105, not a floor cell): the failure names the TODO for a planner or physics gap | `m3-fight-left-floor` | none (a guard: a live leg fails loudly) |
| 3.1 s0, fix A | a target with no name in its CreateObject counts no kill toward `target_kills` (report-only) | `m3-fight-target-name` | none (a guard) |
| 3.1 fb | the Tusker Slave's weenie row (maximum health 400 here, level, motion table) is ACE's world database's; the check reads the motion table and height from the Setup | `m3-standin-tusker-stats` | none (stand-in placeholder, labelled) |
| 3.1 fb | the stand-in's damage is `[0.275, 0.45] x power` from a seeded generator, with no resist, critical, rating or skill bonus, and the damage type is read from the spell's name | `m3-standin-spell-damage` | none (stand-in placeholder, labelled) |
| 3.1 fb | levels the extract lacks (Flame Bolt VII) wear their line's projectile; the weenie's base physics state, the speed for an unknown line and the extra projectiles of volleys and blasts are not known | `m3-standin-projectile-weenies` | none (stand-in placeholder, labelled) |
| 3.1 fb | a stand-in projectile's miss ends at `min(range, 75 m)`; ACE's physics ends it | `m3-standin-projectile-range` | none (stand-in placeholder, labelled) |
| 3.1 fb | the caster's, target's and projectile's radii and heights are placeholders (1.8 m, 0.48 m, 0.1 m); no intercept solver (the target stands still) | `m3-standin-object-sizes` | none (stand-in placeholder, labelled) |
| 3.1 fb | the stand-in makes no corpse; the creature is removed 2 s after the Dead motion (the real animation length is the motion table's) | `m3-standin-corpse` | none (stand-in placeholder, labelled) |
| 3.1 fb | the kill's experience is added without ACE's `CheckForLevelup` (level, credits, script, vitals) or the level cap; the stand-in logs when a level boundary is crossed | `m3-standin-level-up` | none (a guard) |
| 3.1 fd | the stand-in's spell learn sends no magic update, so its spellbook never holds Flame Bolt II to VII; the offline arena run takes the level from a fixed skill (`FightOptions.ArenaSkill`, 300) with no book | `m3-standin-spellbook-updates` | none (stand-in placeholder, labelled) |
| 3.1 fc | a projectile keeps its creation cell while it flies (logged once for a bolt created in a dungeon cell) | `m3-bolt-cell-tracking` | closed in 4.2 x2 (ProjectileCellFollower hands a bolt cell to cell; the sphere test and collision stay `TODO(physics)`) |
| 3.1 fc | the projectile warm-up could not read the Setups or its meshes were not all ready within the patience; a bolt's meshes are prepared when it first appears (logged only on failure) | `m3-bolt-warm-up` | none (a guard) |
| 3.1 fc | retail keeps the origin of a Missile with no sphere (`CPhysicsObj::UpdateObjectInternal` 11.6096 0x005161B0, `CPartArray::GetNumSphere` 0x00518B90); a placeholder or unknown Setup would never fly | `m3-sphereless-missile` | closed in 3.1 fc and fd: the glitch fix is the default (a sphereless projectile flies) and `--retail-sphereless-missile` restores retail (avatars.md section 17) |
| 3.1 fb | the stand-in's projectile was a labelled placeholder with no Launch or hit effect | `m3-standin-projectile-model` | closed in 3.1 fb (see the 2.1 s2a row) |
| 3.1 fb | a cast with no cast planner planned only the profile's spells | `m3-standin-cast-plan-lazy` | closed in 3.1 fb (see the 2.1 s2a row) |
| 3.1 review R10 | fb also changed `StandInServer.cs`, `StandInWorld.cs` and the new `StandInDamageText.cs`, and s0 added a `source` parameter to `InteriorLocations.Dungeon`, all outside their §4.7 rows and without a note (the stand-in files are within the item's one stand-in owner, §2, and listed in the lane handoff; the parameter is harmless); `GameFightWalk.BeginLeg` and `Tick` copy `DenWalk`'s lines, so the R4 guard went in twice | none | none (noted here; factor the walk code over an `OfflineWalker` and `RouteDriver` pair when a slice next edits `FightWalk.cs`) |
| 3.1 fb, fc | the new ported files `StandInWorld.Combat.cs` and `StandInDamageText.cs` were missing from docs/third-party/ (review R5) | none | closed in the 3.1 doc pass (`third-party/effects.md`, `third-party/session.md`) |
| 3.2 s0 | the reducer publishes no `EnchantmentChanged` for an empty purge, so `purged=` misses the tuskers' death of an unbuffed mage (ACE did send 0x02C6); DoD 10's 0x02C6 is read at the suicide's purge | `m3-purge-empty` | closed in 4.2 x1 (an empty purge publishes EnchantmentChanged(Purged)) |
| 3.2 s0 | the death leg was a stub that failed loudly | `m3-death-leg` | closed in 3.2 dd (`DeathLegRunner`, `--login-check=bsd-death`; session.md §32) |
| 3.2 da | the lifestone's use radius is read from its CreateObject (`UseRadius`, at least 0.8 m), else a placeholder 1.5 m (ACE's own test is a cylinder distance) | `m3-lifestone-radius` | none (a guard: no check needs it yet) |
| 3.2 da | a scenario built with no approach walker sends the Use from where the player stands (a warning) | `m3-bind-approach` | none (a guard; the headless host and the game leg both pass a walker) |
| 3.2 da | the walker may not be re-placed after the suicide's teleport (the controller's SelfTeleport handling is not proved offline) | `m3-death-walk-replace` | only if a live leg shows it (not seen in the live rounds) |
| 3.2 da | a death stage with a null walker fails loudly | `m3-fight-no-walker` | none (a guard; the headless host passes `DenWalk`, the game `GameFightWalk`) |
| 3.2 da, fix A (review R10) | whether retail's weenie destruction hook (the virtual call `CObjectMaint::DeleteObject` 0x00508FA0 makes before it removes the weenie) takes a deleted held item out of its placement list was not read; `ReleaseDeletedHeld` cites `CM_Physics::DispatchSB_DeleteObject` 0x006AD600 and the object-maintenance deletes, and logs the first release | `m3-inventory-delete-retail` | none (a guard: no check needs it yet) |
| 3.2 da | the lane's reading of "bar 1": bar 1 (the second tab) holds the highest known of levels I-VII per arena profile line and bar 2 (the third tab) Flame Bolt I-VII; ACE's bar id is 0-based, so the first tab keeps what the character had (R7) | none | the owner, if a different bar or level is wanted (one constant each in `DeathScenario`) |
| 3.2 db | the stand-in's tusker attacks in a placeholder rhythm: reach 2 m, a swing every 2 to 3 s, 70% hits for 8 to 16 piercing damage | `m3-standin-tusker-attack` | none (stand-in placeholder; the live per-hit value is for the owner's fidelity pass) |
| 3.2 db | the vitae spell's row is a placeholder (0.95, then 5% lower a death to the level's floor, no deeper than 40%) | `m3-standin-vitae-spell` | none (stand-in placeholder, labelled) |
| 3.2 db | a player who never bound returns to the visit's start (ACE: `Sanctuary ?? Instantiation`) | `m3-standin-instantiation` | none (stand-in placeholder, labelled) |
| 3.2 db | the lifestone weenie's class id, look and use text are ACE's world database's; the stand-in's lifestone and its Magic line are labelled placeholders | `m3-standin-lifestone-weenie` | none (stand-in placeholder, labelled) |
| 3.2 db | the player's corpse weenie (class id, look, physics, clothing) is ACE's world database's; the stand-in's corpse wears the player's Setup, lies in the Dead pose and holds nothing the client can open | `m3-standin-corpse-weenie` | none (stand-in placeholder, labelled) |
| 3.2 db | the lost items follow ACE's count rule over the stand-in's own pack in a seeded order; no pyreals, burden update, values or bonding | `m3-standin-death-items` | none (stand-in placeholder, labelled) |
| 3.2 db | the stand-in made no corpse for the player, and its suicide threw | `m3-standin-corpse` (the player's), `m3-standin-death` | closed in 3.2 db (the player's corpse, the Dead animation placeholder of 2 s, the death, the suicide, the lifestone); a creature's corpse is still `m3-standin-corpse` |
| 3.2 pa | the melee and missile attack has no action in M3: the attack-height buttons answer NotInM3 "Melee and missile attacks" and nothing begins the power bar | `m3-melee-attack` | M4 (fights: the attack action and the power bar) |
| 3.2 pa | `GetDefaultCombatMode` reads the wielded slot: a missile weapon gives missile, a melee or two-handed weapon melee (retail also reads the weapon's own type) | `m3-combat-use` | M4 (fights: weapon kinds) |
| 3.2 pa | the combat window's three option check boxes are ghosted like the options page, and their tooltips are not built | `m3-combat-options`, `m3-combat-tooltips` | M4 (fights: the combat window's option boxes) |
| 3.2 pa | retail also ghosts the spell shortcuts outside magic mode; the 18 shortcut slots wait for items | `m3-toolbar-shortcut-ghost` | M4 (with the quickslots) |
| 3.2 pa | guards: the gameplay layout lacks the combat window or the power bar; the power bar's download text does not render in the string tables | `m3-combat-window-missing`, `m3-powerbar-window-missing`, `m3-powerbar-strings` | none (guards) |
| 3.2 pb | retail asks the spell's formula for its targeting type and casts a spell with none untargeted; here every spell that is not SelfTargeted needs a selection | `m3-cast-untargeted` | none (a guard: ACE answers; no bound spell needs it) |
| 3.2 pb | retail refuses a spell whose components the pack lacks before sending, and checks the target against the spell; ACE answers both through the cast's UseDone | `m3-cast-components`, `m3-cast-compat` | none (a guard: ACE answers) |
| 3.2 pb | the cast button's tooltip follows the spell selection, not the object selection; the endowment icon is hidden; next and previous tab and spell, the quickslots, the drop handler and favourite add and remove by drag are not built (D13: no drag) | `m3-spellcast-selection-notice`, `m3-spellcast-endowment`, `m3-spellcast-hotkeys` | M4 (with the quickslots and the spell window) |
| 3.2 pb | a favourite the character does not know, or a spell the SpellTable lacks, is left out as retail's player module drops it; a cast of an unknown spell is refused | `m3-spellcast-unknown-favourite`, `m3-spellcast-unknown-spell`, `m3-cast-unknown-spell` | M4 (with the quickslots and the spell window) |
| 3.2 pb | guards: the gameplay layout lacks the spellcasting window, its item list or a cast control | `m3-cast-selection-missing`, `m3-spellcast-list-missing`, `m3-spellcast-window-missing` | M4 (with the quickslots and the spell window) |
| 3.2 pb | the stand-in sends its spell bars only with its next description, so a play-check cannot list them live (the fix: a lever on `StandInServer` that re-sends the description, or bars on `StandInPlayerStats`) | `m3-standin-bars-live` | closed for BSD Mage in 4.2 p1 (the bars are bound); the stand-in's own bars: none (stand-in placeholder) |
| 3.2 review R1 | the corpse counters counted the den's old corpses (live `corpse=7`, `corpse=9`) | none | closed in fix A: a corpse counts between a death signal and that death's arrival; the others are `corpses_stale=` (session.md §32) |
| 3.2 review R2 | ACE dispels the lifestone protection on every teleport, so the live loop never sees 0x028A 0x0502 (`protected=0` in every round) | none | closed in fix A: `protection=dispelled|expired|none` (report-only); DoD 10's protected blow is offline evidence only |
| 3.2 review R3 | the late "the lifestone object leaves the table" WARN and `respawn=elsewhere` at a run's end were a false alarm: the re-measure ran after the log off, whose `LeaveWorld` had cleared the object table; the object table forgets nothing | none | closed in fix A: `DeathScenario.FreezeMeasures` in the world; do not change `WorldObjectTable` or the reducer's delete gate for it |
| 3.2 review R4 | pb's 11.6096 addresses for `PostInit`, `UpdateFromPlayerModule`, `SetSelected` and `gmSpellcastingUI::Register` were 11.4186 ones | none | closed in fix B (retail-ui.md §23.8 has the corrected addresses) |
| 3.2 review R5 | files edited outside their rows without a handoff note: `PlayMode.PanelShots.cs` (pa c936cc8, pb 55d5d4e), `StandInWorld.cs` and `StandInResponder.cs` (db f23875a), `PlayChecks.Panels.cs` and `Lifestone.Net/Session/SessionMachine.World.cs` (s0 ee8fe11), `GameStateReducer.Inventory.cs` (da 78456de); every edit is additive and the shared seams keep their contracts | none | none (noted here; the lead records them in §8's as-run line) |
| 3.2 review R6 | DoD 9 and §4.8 row da say `StopAfterHits` stops "after the first 0x01B2 and 60 s"; the code stops 3 s after the first blow (`StopAfterHitsSeconds`), and an early death leaves the mage un-geared | none | the lead amends DoD 9's wording in the spec (this doc pass leaves the spec alone); `IncomingDamageLiveTests`' remarks were fixed in fix A |
| 3.2 review R7 | the profile's spell bar and the bolt bar are tabs II and III (ACE's bar id is 0-based) | none | fix B: the panel shot opens the first tab with spells, play keeps retail's first tab; noted in session.md §32 and retail-ui.md §23.8 |
| 3.2 review R8 | `vitals_pct` kept moving with the regeneration during the re-gear | none | closed in fix A: the first 0x02E9 of each vital after the arrival |
| 3.2 review R9 | the two floaty windows were hidden only when their staged unit ran | none | closed in fix B: hidden in `PostInit`, shown by mode in `Attach` (a test fails without it) |
| 3.2 dd | the corpse review frame is given up by name: ACE creates the corpse and sends the teleport in one tick, so portal space is up 0.3 s after the corpse's F745 | none | none (ACE's order; the corpse is proved by the tally and, drawn in the Dead pose, by the offline loop) |
| 3.2 dd | the red "In" at the vitals' left in the lifestone frame is the spew box's "In Portal Space - Please Wait..." row under the dats' vitals window; whether retail clears it at the tunnel's end is not read | none | none (a guard: no check needs it yet) |
| 3.2 dd | the vitae page opens by itself in the dead and lifestone frames: retail's behaviour (`gmVitaeUI::RecvNotice_VitaeChanged` 11.6096 0x004A7AA0) | none | none (retail-ui.md §23.8) |
| 3.2 dd | `death_frame_max_ms` is report-only in its first gate (live 11.0 and 16.7 ms) | none | closed in 4.1 c: judged, over 75 ms fails and over 50 ms prints `frame_over_50=yes` |
| 3.2 live | the combat windows' frames are staged by the UI's own notices, not by clicks or session actions, and the stand-in's bars never reach them offline | none | the owner is told when the frames are sent |
| 4.1 s0 | the viewer light's cadence (`SmartBox::set_viewer`) was unread | `m3-viewer-light-cadence` | closed in 4.1 docs: every drawn frame, from `SmartBox::update_viewer` (interiors.md §4.6) |
| 4.1 r2 | particles were not lit in cells (`ParticleShaders.cs` 50-51) | `objects` (`TODO(objects)`) | M4 (combat visuals) |
| 4.1 fix A | a wielded item was drawn at the physics step's frame, not the player's interpolated one | `g5-held-item-interpolation` (the rest of `g5-translucency`) | closed in 4.1 fix A (`ServerObjectsLayer` applies the player's `DrawInterpolation.Offset`; the particles and light are sub-step, unmeasured) |
| 4.1 fix A | the stand-in sent no parent event on a wield | `m3-standin-wield-parent` | closed in 4.1 fix A (F749 first, then 02DA, 02CE, 0023; the parent location from the equip mask) |
| 4.1 r1 | outdoors the object shader takes no light row; the dynamic lights reach objects in cells only; retail's outdoor condition not read | `m3-dynamic-lights-outdoors` | M4 (combat visuals: the outdoor hunt) |
| 4.1 r1 | a projectile's light follows its body's cell (the creation cell until the server moves it) | `m3-bolt-cell-tracking` | closed in 4.2 x2 (ProjectileCellFollower hands a bolt cell to cell; the sphere test and collision stay `TODO(physics)`) |
| 4.1 r2 | a particle in a cell takes the dynamic term but not the static cell lights (the particle mesh has no light sum) | `particle-cell-static-lights` | M4 (combat visuals) |
| 4.1 c | a flight under 3 frames is counted, not judged, by `bolt_light=` | `m3-bolt-light-short-flight` | none (a guard: counted, not judged) |
| 4.1 fix A | the spell viewer draws no dynamic light; the showcase's field is `lit_projectiles` (report-only) | `m3-showcase-lights` | none (a guard: the showcase counts projectiles) |
| 4.1 s0 | a Setup with more than one light: only its first light is used | `m3-setup-multi-light` | none (a guard: no case in the dats) |
| 4.1 s0 | more than 64 light sources: the registry reports it loudly | `m3-dynamic-light-sources` | none (a guard) |

**Added in M3 4.2** (the review nits R7-R13, the open TODO names of the 4.2 slices, and the cluster placements; the TODO names are in the gap text):

| From | Gap | Placed |
|---|---|---|
| 4.2 review R7 | nit: the viewer light's default cites `SmartBox::set_viewer` 0x00454640 (the constructor, `SmartBox::SmartBox`; set_viewer is 0x00452C80) in `FightLegVerdict.cs:98` | the next slice that edits the file (a comment, one line) |
| 4.2 review R8 | nit: the `Spellbook.cs:47` comment says the client's sends do not change the bars (p1 made them change at send) | the next slice that edits the file (a comment, one line) |
| 4.2 review R9 | nit: `GameInventoryPage.cs:22` cites `gm3DItemsUI::PostInit` at 0x004A7190 (11.4186); 11.6096 is 0x004A7510 | the next slice that edits the file (a comment, one line) |
| 4.2 review R10 | nit: `WorldObjectTable.NearestNamed` selects any object of the attacker's name: a dead twin, an item, a nearer twin (`GameStateReducer.Combat.cs` RequestAutoTarget) | M4 (melee targeting: skip non-creatures and Dead motions; `m3-autotarget-attacker-id`) |
| 4.2 review R11 | nit: `DeathOptions.StopAfterHits` has no caller after C9 (its branches in `DeathScenario.cs` are dead code) | none (a guard: remove with the next death-loop change) |
| 4.2 review R12 | nit: the expiry line is composed only for entries the registry removed; retail calls `NotifyOfEnchantmentRemoval` for every id in the message; a no-table expiry logs once, not per id | closed in 4.2 x1 for the chat line; the per-id call is none (a guard) |
| 4.2 review R13 | nit: `GearKit.cs:162` says the leather set is 2,550 (it is 2,560), and the swap test leaves out the Foci of Strife's burden (`GearKitTests.cs:133`) | the next slice that edits `GearKit.cs` (a comment and a test margin) |
| 4.2 review R1-R6 | should-fix findings of the 4.2 review: the cast guard's mutable static (R1), the resisted bolt counted as a wall hit (R2), `humans_dressed` (R3), the solo flag (R4), the clear-line sweep cost (R5, measured in fix A: about 6 ms worst case, no cache), the texture fallback labelled a glitch fix (R6) | closed in 4.2 (fixes p1-p7, fix A, fix B; R5 measured, no cache) |
| 4.2 p1 | the bars' tab I and tab II layout; `RemoveSpellFavorite` 0x01E4 (retail `CM_Character::Event_RemoveSpellFavorite` 0x006A26B0) | closed in 4.2 p1 (the bars bound for BSD Mage; the client updates its bars at each send) |
| 4.2 p2 | `TODO(p2-stack-initial)` (the stack box and slider are hidden at construction), `TODO(p2-use-button-state)` (the Use button states) | M4 (the toolbar with the quickslots) |
| 4.2 p2 | `TODO(m3-autotarget-attacker-flag)`, `TODO(m3-autotarget-selectnext)`, `TODO(m3-autotarget-attack-paths)`, `TODO(m3-autotarget-attacker-id)` (auto-target) | M4 (melee targeting) |
| 4.2 p3 | `TODO(m3-combat-pending-mode)`, `TODO(m3-portal-space-flag)`, `TODO(m3-combat-mode-word)`, `TODO(m3-wield-held-rule-bit)` | none (a guard) |
| 4.2 p4 | `TODO(m3-fight-los-radius)`, `TODO(m3-fight-los-target-height)`, `TODO(m3-fight-los-approximation)`, `TODO(m3-fight-wall-impact-point)`, `TODO(m3-fight-approach-target)`, `TODO(m3-standin-wall-clip)`, `TODO(m3-den-room-statics)`: the wall-shot pick and its judge | as named on each row; the approach and impact-point names are M4 (the hunt loop) |
| 4.2 p5a | `TODO(p5a-live-frame-contended)` (the live legs' contended rule), `TODO(p5a-parallel-shader-cache)` | **both closed in M4 1.1 s3** (m4-plan.md §8 item 1.1: the rule settled; each `-Parallel` slot its own shader cache, measured) |
| 4.2 p5b | `TODO(q5-death-wait-protection)`: the death scenario's 53 s wait is the lifestone protection (ACE dispels it only by the player's own actions); the stand-in's tusker placeholder stays `TODO(m3-standin-tusker-attack)` (live per-hit damage 35 to 40 against its 8 to 16) | M4 (the testing loop: the death wait) and none (stand-in placeholder) |
| 4.2 p6 | `TODO(m3-standin-objdesc-on-wield)` (the stand-in sends no F625 on a wield, an unwield or the death's lost items), `TODO(m3-standin-human-look)` (every garment is a placeholder) | none (stand-in placeholders, logged once each) |
| 4.2 p7 | `TODO(q7-burden)` (the leather kit burden, 2,560 as the rows sum) | closed in 4.2 p7 (the prose corrected; the focus burden is R13) |
| 4.2 fix A | `TODO(q6-texture-override-fallback)`, `TODO(m3-fight-target-radius)` (the wall-stop distance: `FightScenario.WallStopShortMetres`) | closed in 4.2 fix A (texture: retail's rule, no flag); the radius: none (a guard) |
| 4.2 fix B | `TODO(m3-ui-step-gc-margin)`: the 12 ms UI-step margin is thin; the remaining spikes are gen0/gen1 GC pauses inside one element's construction (a no-GC region and a pre-Enter collection both failed) | M4 1.1 s4 made the bounded attempt (a heap-sized no-GC window over the staged build: the live unit 8.9 to 2.4 ms); the TODO stays open until the post-window collection is read live (m4-plan.md §8 item 1.1, §9) |
| 4.2 fix B | `TODO(footstep-sound-table)` and the RenderingServer shader parameter call (an engine ERROR outside the editor; never called) | named; see the footstep row |
| 4.2 x1 | `TODO(m3-weenie-error-type)` (the codes whose chat type could not be read), `TODO(m3-enchant-expiry-name)`, `TODO(m3-weenie-error-text)` (unknown codes) | none (a guard; logged once) |
| 4.2 x2 | `TODO(physics)` at AvatarBody (client-side projectile collision) | M4 (combat visuals) |
| 4.2 x3 | `TODO(m3-buff-cast-frame-judge)`: the buff leg judges `cast_frame_max_ms` over 75 ms (over 50 ms is flagged) | M4 (combat frames) |
| 4.2 x3 | `TODO(m3-bolt-light-short-flight)`, `TODO(gameplay-staged-notice-flake)`: named beside their code | none (a guard) / polish P5 |
| 4.2 x4 | `TODO(m3-wield-second-choice)`: retail's double-click passes no second side; we try both (to confirm live) | none (confirm live at the next gate) |
| 4.2 x4 | `TODO(m3-skill-aug-bonus)`: closed (ACE's augmentation properties 300-302, 326, 344, 365, 390) | closed in 4.2 x4 |
| 4.2 live gate | `TODO(m3-advancement-chat-check)`: the advancement chat lines and raise sounds are not asserted live (k1 gap 1) | M4 (the testing loop) |
| 4.2 live gate | `TODO(m3-ambient-judge)`: `ambient=` in the Holtburg laps is report-only; the judge is engine-checks' `ambient_draws>=1` on a fixed listener (k6 gap 1) | M4 (world sound) |
| 4.2 live gate | the stand-in's bolts fly through walls, the stand-in's tuskers attack on a placeholder rhythm (`m3-standin-wall-clip`, `m3-standin-tusker-attack`); the wall-shot judge reads `wall_hits=` report-only beside `misses=` | as named |
| 4.2 k3 | the buff leg's `cast_scripts=2` for 26 casts: every recorded buff run has 3 to 6 cast windows; a self cast is one F755 on the player; an armour cast's effect lands on a piece with no script table (`ServerScriptsMissing`); `BuffCastScriptsTests` runs a cycle | closed in 4.2 x2 (the explanation; the row says so) |
| 4.2 k6 | the `-viewer` and `-bolt` frames have no verdict line in §8 (the lead records them at the 4.2 gate) | closed by the 4.2 gate (§8) |
| 4.2 k7 | DoD 15 and the table said "judged against 50 ms"; the fight and death legs fail at 75 ms and flag 50 (§5 is the rule) | closed in 4.2 (DoD 15 reworded to §5) |
| 4.2 k7 | `smoke.ps1` does not fail on `frame_over_50=yes` (login-bsd and login-death pass on `ok=true` alone) | M4 (the testing loop: a flag that reads in the verdict) |
| 4.2 k7 | the offline checks judge no cast, hit or death frame (play-checks waive the judges while review frames are saved) | M4 (the testing loop) |
| 4.2 k7 | the non-decreasing test count is a record, not a check (no baseline file) | M4 (the testing loop) |
| 4.2 k3 | the spec's DoD 7 says `cast_scripts>=1 cast_sounds>=1` are judged from 2.1: they are report-only live (D28) | closed in 4.2 (the audit row says so) |
| 4.2 hero take | the den's old "Corpse of BSD Mage" objects draw grey-pale skin on bare arms and legs in the live take; a replay of the recorded CreateObjects (five corpses: base palette 0x0400007E, 15-18 sub-palettes, skin textures on parts 3, 4, 7, 8, 12, 15) through the reducer, adapter and avatar set applies every palette and part as for the living mage, so the data path is right; the drawn result was not checked against a frame of the same ModelData | `q6-corpse-objdesc-live` | M4 (a game frame of a recorded corpse ModelData, then the render path if it differs) |
