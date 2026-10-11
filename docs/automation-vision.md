# Automation vision: requirements for Lifestone's built-in automation

**Purpose:** turn Decal-era automation (VirindiTank/UtilityBelt-style botting, scripting, macros, multi-character play) into concrete requirements for Lifestone. Lifestone is a Godot AC client talking the AC protocol to a bundled localhost ACE.
**Status of outside facts:** statements about Decal, VirindiTank (VTank) and UtilityBelt (UB) behaviour come from general knowledge and have **not** been checked against their documentation. They are marked "(verify)".

> **Design decision: automation is built into the Lifestone client, not added through plugins.**
> - The capabilities that Decal-era plugins provided are first-class features of the Lifestone binary itself.
> - First targets:
>   - **AFK dungeon fighting and looting** (VirindiTank-style);
>   - **auto-buffing**.
> - Other UtilityBelt-style conveniences follow in the same spirit.
> - There is **no third-party plugin loading**: no Decal-style DLL host. This is also a security choice, since loading arbitrary code is exactly what this project avoids.
> - Customisation comes from data profiles (loot rules, buff lists, nav routes, metas) and, later, an optional sandboxed expression or script tier.

---

## 1. Requirements

### 1.1 Principles (non-negotiable)

- **R1. Localhost only.**
  - Automation and scripting may run only when the session's server endpoint is a loopback address (the bundled ACE). The session layer enforces this: it refuses to start runners otherwise and logs why.
  - Rationale: the project's security posture, and botting on public servers breaks their rules.
- **R2. Same path as the human.** Bots act only through the same intent API the UI uses, which the client turns into AC GameActions. Server-side shortcuts are allowed only as explicit ACE admin commands on the local server (see the dev layer, §1.4 I).
- **R3. Server-confirmed perception.** Conditions read state that the server has confirmed: enchantments, vitals, inventory, objects, chat. Client-side guesses never feed a condition. ACE is authoritative for every rule (`research/game-logic.md` §1).
- **R4. Headless core.**
  - Transport, session, game state and automation are plain net10.0 libraries with no Godot dependency.
  - The same code runs a windowed client, a headless bot session, and replay tests.
- **R5. Deterministic and testable.**
  - One "game thread" per session; network I/O posts decoded messages to a queue the game thread drains.
  - An injectable clock (no direct `DateTime.UtcNow`, so tests never sleep).
  - Every session can record its message stream for replay tests.
- **R6. Safety and control.**
  - A global kill switch (hotkey and UI); per-character opt-in.
  - A dry-run mode: evaluate rules and log intended actions without sending them.
  - Rate limits; a per-tick CPU budget for scripts.
  - An audit log of every action sent and its outcome.
- **R7. Loud stubs.** An unimplemented action fails with a visible error. It never fakes success.

### 1.2 Observation API: what the automation layer must be able to see

| Domain | Must expose | Source in an ACE session |
|---|---|---|
| Self | <ul><li>Vitals (current/max, %)</li><li>Base and buffed attributes and skills</li><li>Level, XP, unassigned XP</li><li>Burden % and capacity; pack slots free</li><li>Vitae %</li><li>Dead/alive</li><li>Combat mode; busy/casting state</li><li>Position (cell id, local xyz, heading); landblock</li><li>Motion state</li></ul> | PlayerDescription (GE `0x0013`) at login, then the property/vital update messages (`research/game-logic.md` §5 lists "~36 Qualities update messages") |
| Enchantments | <ul><li>Active enchantments: spell id, layer, caster, duration, time remaining</li><li>`TimeLeftOnSpell(name or id)` exact to the second</li><li>Vitae as an enchantment</li></ul> | Server magic and enchantment events (GE range `0x02C1`–`0x02C8` in `research/game-logic.md` §5; verify the exact opcodes) |
| Spells | Known spells, spell bars, component counts, castability (skill, mana, components, foci) | PlayerDescription plus the DAT SpellTable `0x0E00000E` for names and formulas |
| Inventory | <ul><li>Containers (main pack, side packs, foci), slots, stacks</li><li>Equipped items by slot</li><li>Every item's property bag (int, int64, float, bool, string, DID, IID, spells)</li><li>Whether the item has been **appraised**, and the cached appraisal</li></ul> | CreateObject `0xF745`, ViewContents, Parent / Pickup / SetState `0xF749`–`0xF74B`; appraise request `0x00C8` → response `0x00C9` |
| World objects | <ul><li>Object table: guid, name, weenie type / object class, position, owner/container, creature/monster/NPC/vendor/portal/door/corpse flags, health fraction (on query)</li><li>Spatial queries (within distance, in cell, by name regex, by type)</li><li>Current selection</li></ul> | CreateObject `0xF745`, UpdateObject `0xF7DB`, Delete `0xF747`, UpdatePosition `0xF748`, UpdateMotion `0xF74C`; health query `0x01C0` |
| Chat and events | <ul><li>Every channel and tell, emote text, system/combat/magic text</li><li>WeenieError codes</li><li>Confirmation and popup prompts</li><li>A regex search over recent history, with capture groups</li></ul> | HearSpeech / Tell / EmoteText; error and confirmation messages (`research/game-logic.md` §5 "NPC dialogue") |
| Travel | Portal-space enter/exit, teleport, landblock or cell change, logout/death/respawn | Teleport `0xF751` and related |
| Social and commerce | Fellowship (members, vitals, leader), allegiance, trade window state, vendor window (items, buy/sell rates) | ACE fellowship, allegiance, trade and vendor events |
| Time and world | Game time (from server ticks plus RegionDesc calendar), weather day-group | Server time ticks plus the DAT RegionDesc |
| Raw tap | A read-only stream of every decoded inbound and outbound message, with timestamps (the Decal "EchoFilter" idea; verify) | Codec layer |

### 1.3 Action API: what it must be able to do

Every action returns a **handle** that completes on server confirmation, an error code, or a timeout. Actions are queued, respect the server's busy state, and are rate-limited. Bots, scripts and the UI all use this one API.

| Group | Actions |
|---|---|
| Movement | <ul><li>Move to a point (cell id plus local xyz, or landblock coordinates)</li><li>Face a heading or object; stop; walk/run; jump</li><li>Follow an object; use a portal, door or lifestone</li></ul> The route follower adds **stuck detection and recovery**: no progress within N s → try a jump or strafe → re-path → fail up to the meta. All movement goes through the same client-authoritative controller as the keyboard (MoveToState `0xF61C`, AutonomousPosition `0xF753`, Jump `0xF61B`), so it depends on Lifestone's movement fidelity milestone. |
| Targeting | Select an object; **appraise and wait for the result**; use an object (NPC, chest, corpse, door); use an item on a target (healing kits, keys) |
| Combat | Set combat mode (peace, melee, missile, magic); attack (melee `0x0008`: height and power; missile `0x000A`: height and accuracy); stop; next-target helpers |
| Magic | Cast a spell on self or a target (`0x0048` / `0x004A`) and wait for success, fizzle, resist or interrupt; cast recalls; run a buff queue |
| Inventory | Pick up, drop, move to a container or slot, equip, unequip, split or merge stacks, give to an NPC or player, loot a corpse (open it, move items), salvage, use an item |
| Commerce | Open a vendor, buy a list, sell a list, wait for the vendor; trade window add / accept / decline |
| Chat | Say, tell, channel, emote; slash commands; ACE admin `@` commands when the local account has access |
| Session | Log in, select a character, log out, set character options |

### 1.4 Engines and features built on those APIs

**A. Loot engine.** Ordered rules produce Keep, Keep-up-to-N, Salvage, Sell or Ignore.
- Predicates are keyed on **AC property enums**: PropertyInt, Float, String, DataId, Bool and spell ids, so new criteria need no code.
- Other criteria: spells by id or name pattern, item name regex, object class, and derived values (each derived value must cite its formula source).
- If any predicate needs appraisal data, the engine appraises the item and waits before deciding.
- The engine runs when a corpse or chest opens and executes its decisions through the Action API.
- Import a VTank `.utl` subset once the format has been verified from real files.

**B. Nav engine.**
- Route modes: once, circular, linear (back and forth) and follow (verify against VTank).
- Waypoint actions: point, pause, chat or command, recall, use portal or object, open vendor, jump, checkpoint.
- Record a route from the player's own movement.
- Coordinates in AC frames (cell id plus local xyz). Display as AC map coordinates (e.g. "42.4N 33.6E").
- Stuck detection as in §1.3.
- Pathfinding inside dungeons needs collision data, so it depends on physics BSPs being decoded.

**C. Meta engine.** States, ordered rules (condition → action), first match wins, call/return, embedded nav routes, load profile.
- **Conditions** include:
  - vitals %, vitae, burden %, free pack slots, death;
  - inventory item count;
  - time left on a spell;
  - vendor open / closed;
  - monster count or priority within a distance, with a name filter;
  - target present;
  - cell or landblock equals;
  - distance to a route point;
  - nav route empty;
  - chat match with capture into variables;
  - expression true.
- **Actions** include:
  - set state, call and return, set watchdog;
  - load a loot profile or nav route;
  - cast a spell (and recalls), use an item or object, equip and unequip;
  - attack, set combat mode;
  - chat and commands, expression / set variable;
  - buy item, sell item, wait for vendor refresh;
  - logout.
- **Watchdog semantics must be specified explicitly.** Support both a named timer that jumps to a state and VTank's movement-based watchdog (no movement of R metres in T seconds → state; verify), under distinct names.

**D. Combat and buff behaviours.**
- A combat module: target priority list (name or class patterns, distance, health), melee/missile/war rotation, and vital thresholds for heal, stamina and mana, plus an option to peel away or flee.
- A buff manager that computes which buffs to refresh from server enchantment timers.
- Both are exposed as meta actions or states, not hard-coded loops.

**E. Mission queue.** A top-level meta made of steps, for example: buff → recall → nav → combat → loot → vendor → repeat until time or condition → logout. Missions are saved and named. No calendar scheduling and no offline progress.

**F. Scripting.**
1. An **expression language** for conditions, actions and chat templates (the UB-style need).
2. A **full scripting tier** with a sandbox and budgets, for example: 250 µs per tick per character; 2 MB per script state; no file, network or reflection access; coroutines yield on every action. The language is an open question (§5): Lua (MoonSharp) or C# scripts.

**G. Built-in feature panels, not a plugin API.**
- Lifestone does not load third-party plugins.
- The features Decal plugins used to provide are compiled into the client:
  - the combat/buff/loot/nav/meta engines above;
  - their UI panels;
  - the inspector.
- Internally they use the same event bus and Action API, so the module boundaries stay clean.
- If extensibility is needed later, it will be data profiles plus the sandboxed script tier (F). It will never be arbitrary DLL loading.

**H. Multi-character / vassal bots.**
- Run N **headless sessions** (R4), each logged into its own account on the local ACE, each with its own meta.
- Coordination happens in-game (tells, fellowship) or over a local in-process bus.
- Presets: a buff bot (with a selfless-buffer variant), a trade broker, and a follow-and-carry mule.
- ACE supplies real fellowship, allegiance and XP pass-up, so none of that is re-implemented.
- Check ACE's session and multi-account limits before relying on them (verify the config).

**I. Dev / cheat layer.**
- Commands such as give, level, set skill, teleport, godmode or learn all spells map onto **ACE's admin and developer commands**. These run on the local server for an account with elevated access level, and ACE persists their effects.
- Lifestone provides a console with completion and help for them.
- Each command is verified against ACE's command handlers at a pinned SHA; none are guessed.

**J. Runtime modes.**
- The bot keeps running while the window is unfocused or minimized.
- Rendering stops when the window is hidden.
- Closing the game (and so the launcher-managed ACE) means nothing accrues.
- Resource targets: ≤400 MB RAM; ≤5% of one core while hidden.

**K. Persistence and resume.**
- Profiles and missions are user files: JSON plus scripts, in a per-user profiles folder. Bundled samples live in the repo; user profiles do not. Every spell or item name in a sample is checked against the dats.
- Runner checkpoints (state, call stack, watchdogs, nav index, variables) are saved on every state change and on exit, so a session can resume.
- Character state belongs to ACE.

**L. Observability and UX.**
- A live inspector: current state, the rule that fired, last action and its outcome, pending action handles, watchdogs, call stack, nav waypoint, variables.
- A scrolling action log; step mode; break on state entry.
- Loot dry-run over a corpse.
- Editors: a table for loot, a map-pinned editor for nav, and a graph or list for metas.

---

## 2. How Lifestone's architecture supports this

```
UDP 127.0.0.1 ⇄ Transport (fragments, acks, ISAAC)            ┐
           → Codecs (typed message records)                    │ net10.0 libraries,
           → Session (login, sequencing, loopback gate R1)     │ no Godot (R4)
           → GameState reducer (self, objects, enchantments,   │
             inventory, chat, social, vendor/trade)            │
           → Event bus (typed events + raw tap)                │
           → Action API (intents → GameActions, handles,       │
             busy-state, rate limit, audit log)                │
           → Built-in automation (loot/nav/meta/combat/buff    │
             engines, expressions, sandboxed scripts)          ┘
           → Presentation: Godot client (HUD, editors, inspector) ── uses the SAME
             event bus and Action API as automation; headless bot sessions skip it.
```

- **A GameState reducer over messages** makes automation testable without a server. Recorded sessions (stored under the gitignored `/cache/`) are replayed into the reducer, and engine decisions are asserted against real server traffic, not self-consistent synthetic data.
- **Action handles** tie each intent to its server outcome: completion events, WeenieError codes, enchantment adds, inventory moves. An action is never reported done just because it was queued.
- **Movement and pathing** reuse the player movement controller. Nav fidelity therefore rides on Lifestone's movement milestone: a scripted route with zero ACE position corrections. Dungeon pathing needs physics polygons and BSPs from the DAT layer.

---

## 3. Acceptance scenarios (definition of done)

All scenarios run against the bundled ACE on 127.0.0.1 with a test account whose credentials stay in gitignored `server/runtime/`.

1. **S1 Holtburg loop.** A circular nav route with Pause and ChatCmd waypoints completes 3 laps with zero server position corrections. A deliberately blocked waypoint triggers stuck recovery and then a meta failure state.
2. **S2 Buff → hunt → loot → vendor.**
   - The meta refreshes self-buffs when `TimeLeftOnSpell < 60 s`, using server enchantment data.
   - It hunts drudges near Holtburg and loots corpses with a profile: keep the matching items, ignore the rest, and log why.
   - At burden ≥ 90% it navigates to a vendor, sells the sell list, and returns to hunting.
3. **S3 Death recovery.** On death it recalls (or respawns), rebuffs and resumes the route.
4. **S4 Vassal buffer.** A second, headless session buffs the main character when the main sends a tell, inside a fellowship.
5. **S5 Resume.** Killing the client mid-route and restarting resumes from the runner checkpoint.
6. **S6 Import.** One small real VTank `.met` and `.utl` import and run within the supported subset. Unsupported elements are reported, not silently dropped.
7. **S7 Sandbox.** A script that exceeds its budget is paused with a warning. File and network access from scripts is refused.
8. **S8 Loopback gate.** A unit test with a fake non-loopback endpoint shows runners refusing to start. It never contacts a real remote host.
9. **S9 AFK dungeon (the headline feature).** The character is placed at the entrance of a low-level dungeon. One click on "AFK hunt" then runs without intervention for 30 minutes:
   - the buff manager keeps the configured self-buffs up from server enchantment timers;
   - the combat module clears monsters along a recorded or computed route, using melee, missile or war magic;
   - it recovers health, stamina and mana at thresholds;
   - it loots corpses through the loot profile;
   - it handles death by recalling, rebuffing and resuming.

   The action log accounts for every decision.
10. **S10 Auto-buff alone.** With nothing else running, auto-buff keeps a 6-spell profile up for 2 hours of game time. No buff lapses for more than 10 s, it never casts while busy, and components or mana running out raise a visible alert.

---

## 4. Proposed phasing (aligned to the Lifestone milestones)

| Phase | Ships alongside | Content | Scenarios |
|---|---|---|---|
| A0 | Protocol client milestone (login, move) | GameState reducer, event bus, Action API with handles, headless session host, session recorder/replayer, loopback gate | S8 |
| A1 | Core gameplay milestone | Built-in **auto-buff** (buff manager + panel), then combat module + loot engine + nav (recorded routes first) → **AFK dungeon**; inspector and action log; dry-run | S10, S9, S1–S3 |
| A2 | — | Meta engine generalisation, expressions, sandboxed scripting tier (no plugin DLLs) | S7 |
| A3 | — | Multi-session vassal bots and presets | S4 |
| A4 | — | VTank import, mission queue, editor polish, checkpoint/resume | S5, S6 |

A0 must not slip behind rendering work: the automation core ships with the protocol client, not after the visuals.

---

## 5. Open design questions

1. ~~**Plugin model**~~ — **decided:** the features are built in, with no third-party plugin loading.
2. **Authoring model:** JSON metas with visual editors first, or a script-first approach with JSON/VTank as an import layer?
3. **VTank compatibility:** how important is it to import real `.met`, `.utl` and `.nav` files? File formats must come from real files, not memory.
4. **Multi-character:** how many concurrent bot sessions? Is a separate local ACE account per bot acceptable?
5. **Dev commands:** may the local single-player account have ACE admin access so the cheat/dev console works?
6. **Runtime modes:** confirm that bots keep running while the window is minimized, and that nothing accrues while Lifestone (and its ACE) is closed.
