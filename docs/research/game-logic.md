# Where single-player game logic can come from (AC1 → Godot)

## 0. Bottom line
- **The premise holds.** Turbine's servers decided every combat roll, all damage, AI, spawns, loot, quest and NPC scripting, vendors, XP rewards and spell magnitudes. `acclient.exe` only sends intents and displays results. Reverse-engineering the client (REA/Ghidra) **cannot recover game rules**. It is useful only for client-side behaviour (physics, animation, UI, the transport layer).
- **The only realistic source of rules is ACEmulator/ACE** (AGPL-3.0, C#, net10.0) plus its world database (ACE-World-16PY-Patches, AGPL, SQL). The alternatives are dead or new. GDLE is unmaintained publicly. Empyrean (an ACE port to Rust) is about a month old.
- **An earlier private prototype by the author follows Architecture B.** It reads the DATs directly, imports the ACE world DB into SQLite and saves characters in an ACE shard schema copied into SQLite. Its game logic is placeholder-level, though: simplified melee math, no chase movement or pathfinding, a placeholder loot table, and no emote, quest, vendor or generator engine.
- **Recommendation: Architecture A, reusing the earlier prototype's renderer where useful, not its game logic.** Have the Godot client talk to a bundled local ACE over the AC protocol. Every modern client rebuild does this: OpenAC, AC:Unreal, acswarm, holtburger and Dereth.

## 1. Premise check: is AC server-authoritative?
**Confirmed, with one nuance: the client owns its own movement.**

Evidence:
1. **The protocol carries intents, not outcomes** (from ACClientLib/ACProtocol `protocol.xml`):
   - Melee attack `0x0008` sends only `{ObjectId, Height, Power}`; missile `0x000A` sends `{ObjectId, Height, Accuracy}`.
   - Casting `0x0048`/`0x004A` sends `{SpellId}` or `{ObjectId, SpellId}`.
   - The server replies with results: attacker notification `0x01B1` carries `{DefenderName, Type, DamagePercent, Damage, Critical, AttackConditions}`; there are evasion events `0x01B3`/`0x01B4`; a health query `0x01C0` returns only a fraction.
   - Item properties are unknown to the client until it asks: the appraise request `0x00C8` is answered by `0x00C9` with the property tables.
2. **Static analysis of the client** (Mag-nus/acclient-AI-RE, MIT, 2026-07, AI-assisted with caveats). It covers build 11.4186, which has an original Turbine PDB, and build 11.6096, whose MD5 `116d9a66…` matches the user's exe. Its `docs/04-combat-magic.md` says "Every roll is server-side". `00-architecture.md` §7 says:
   - "Client is authoritative for its own movement… The server corrects rather than simulates."
   - "No client-side chance-to-cast math and no client-side hit-location logic."
   - `CEmoteTable`, `CreationProfile`, the body-part armour model and the CombatManeuverTable contents are parsed but **never read**.
   - Swing animations come from the server.
   - Burning spell components is server-driven; fizzles and resists come back as error codes `0x3FE`–`0x404`.
3. **The world DB folders mirror Turbine's server-side data types.** They are not in the client DATs: `1 RegionDescExtendedData`, `2 SpellTableExtendedData`, `3 TreasureTable`, `4 CraftTable`, `6 LandBlockExtendedData`, `8 QuestDefDB`, `9 WeenieDefaults`, `B GameEventDefDB`, `C EmoteScript`. Weenies also reference server-only tables, for example `MutateFilter 0x0E000012` and `TsysMutationFilter 0x38000032`. ACE has to rebuild those as text tables (`ACE.Server/Entity/Mutations`, 103 files, 495 KB).

**What the client DATs do provide.** ACE itself reads these from `client_portal.dat` (`ACE.DatLoader/PortalDatDatabase.cs`):

| Table | DAT ID |
|---|---|
| XpTable | 0x0E000018 |
| SkillTable (skill formulas) | 0x0E000004 |
| SecondaryAttributeTable | 0x0E000003 |
| SpellTable (names, school, components, base mana; **no StatMod magnitudes**) | 0x0E00000E |
| SpellComponentsTable | 0x0E00000F |
| CharGen | 0x0E000002 |
| GeneratorTable (generator recipes only, no placements) | 0x0E00000D |
| ContractTable | 0x0E00001D |

Also available: motion tables (0x09), the combat maneuver tables, RegionDesc, and display-only formulas in the client: `SkillFormula::Calculate`, the vitae pool formula, and enchantment stacking (`Enchantment::Duel`).

## 2. ACEmulator/ACE

**Basics**
- Default branch master; last push 2026-10-08; latest release v1.78.4816 (2026-09-17).
- Release assets: `ACE.Server-v1.78.4816.zip` (7.8 MB). It is framework-dependent and needs the .NET 10 runtime.
- About 5,868 commits and 80+ contributors.
- Last 12 months: 178 commits, of which only 37 are substantive (bug fixes in combat, magic, appraisal, physics and performance). It is mature and in maintenance mode.

**License.** `LICENSE` is AGPLv3, followed by GPLv3 text that covers 8 early contributors (the two are compatible under §13).

**Projects** (`Source/`)

| Project | Target | Notes |
|---|---|---|
| `ACE.Server` | net10.0, x64 Exe | ServerGC; deps: Lib.Harmony 2.4.2, log4net, McMaster.NETCore.Plugins, MySqlConnector 2.6.2; Lifestoned.DataModel.dll |
| `ACE.Database` | net10.0 | EF Core 9.0.20 + Pomelo.EntityFrameworkCore.MySql 9.0.0 |
| `ACE.Entity` | netstandard2.0 | |
| `ACE.DatLoader` | netstandard2.0 | |
| `ACE.Common` | netstandard2.0 | |
| `ACE.Adapter` | — | Converters for GDLE and Lifestoned JSON |

**Size.** 1,771 `.cs` files, about 13.3 MB (roughly 250–300k lines). The game-logic map (sizes in KB):

| Area | Size |
|---|---|
| WorldObjects | 2,498 (145 files) |
| Factories (loot generation + tables) | 2,704 |
| ACE.Server/Entity | 1,210 |
| Physics, a port of the client's `CPhysicsObj`/`CTransition`/`SpherePath` | 896 (162 files) |
| Network | 701 (426 files) |
| Managers | 389 |
| `EmoteManager.cs` (handles 124 EmoteType values) | 90 |
| `WorldObject_Magic.cs` | 93 |
| `Player_Inventory.cs` | 195 |
| Monster_* (Awareness, Combat, Melee, Missile, Magic, Navigation) | about 125 |

**Coupling.** 123 files call `EnqueueSend` and 67 call `EnqueueBroadcast`. Game logic is written directly against protocol messages, so **the wire protocol is ACE's natural integration seam.**

**Databases**
- Three: `ace_auth`, `ace_shard`, `ace_world`. DDL is in `Database/Base/{AuthenticationBase,ShardBase,WorldBase}.sql`; the world schema has 53 tables.
- `Config.js.example` points all three at MySQL 127.0.0.1:3306.
- The wiki says MySQL ≥ 5.7.17 or MariaDB ≥ 10.2, but the same wiki still says ".NET 8", so it is stale. The docker-compose file uses `mysql:8.0`.

**Running headless**
- It is a console exe, `start_server.bat`.
- It can run non-interactively with env vars: `ACE_SQL_INITIALIZE_DATABASES`, `ACE_SQL_*_DATABASE_{NAME,HOST,PORT}`.
- `Offline.AutoUpdateWorldDatabase` downloads the latest world release from the GitHub API and imports it (`Program_DbUpdates.cs`).
- Network: UDP 9000, plus 9001 for server-to-client traffic.
- `AllowAutoAccountCreation: true`.
- 169 runtime server properties in `PropertyManager.cs` cover solo tuning: `xp_modifier`, `quest_xp_modifier`, `quest_mindelta_rate`, `vitae_penalty`, `rare_drop_rate_percent`, cantrip and aetheria drop rates, and more.
- Harmony mods (for example `aquafir/ACE.BaseMod`) allow behaviour changes without forking.

**Sidecar vs. in-process**
- **Sidecar (recommended).** Separate process over localhost UDP; latency is negligible.
- **In-process is possible but awkward:**
  - It relies on static singleton managers (`DatabaseManager`, `WorldManager`, …).
  - `Session` is constructed from a `ConnectionListener` and an `IPEndPoint`, so a loopback transport would mean changing ACE.
  - ServerGC is a process-wide setting.
  - Godot 4.7's GodotSharp packages target net8.0. A net10.0 Godot project reportedly works but is not officially supported, so the user must verify it.

**Swapping the DB for SQLite.** No ACE SQLite fork was found (code, repo and issue searches, plus web search). The work:
- The scaffolded contexts use Pomelo calls such as `.UseCollation("utf8_general_ci").HasCharSet("utf8mb3")` and `UseMySql(..., CachedServerVersionAutoDetect)`.
- Raw MySQL sits on the startup path:
  - `ShardDatabase.GetSequenceGaps` uses `@rownum` user variables and is called by `GuidManager` at startup.
  - `GetEstimatedBiotaCount` queries `information_schema`.
  - `Program_Setup.cs` and `Program_DbUpdates.cs` run `.sql` scripts through MySqlConnector.
- Other raw SQL is only in dev commands and `ShardDatabaseOfflineTools`.
- The world SQL uses MySQL-isms: `SET @parent_id = LAST_INSERT_ID();`, backticks, and `True`/`False` literals.
- **Head starts:**
  - `amoeba/ace-to-sqlite` (AGPL) publishes SQLite files converted from MySQL: `ace_world_patches.db` (131.5 MB), `ace_world_base.db` (92 MB), `ace_pcap_exports.db` (47 MB). They rebuild daily and their versions match the world releases.
  - The earlier prototype already has SQLite copies of the ACE schemas (`src/Data/AceWorld/WorldSchema.cs`, `WorldSqliteImporter.cs`, `src/Persistence/AceShardSchema/ShardSchema.cs`).
- Estimate: **2–4 engineer-weeks**, plus rebasing on each ACE release (about one per quarter).

## 3. World content

| Repo | License | Last activity | Content |
|---|---|---|---|
| ACEmulator/ACE-World-16PY-Patches | AGPL | 2026-10-08 (v0.9.296) | The complete world DB. Release asset `ACE-World-Database-v0.9.296.sql.zip` is 18.6 MB; one SQL file of about 150 MB unzipped (Empyrean SETUP.md). Repo tree has 35,147 files; `9 WeenieDefaults` holds 20,773 `.sql` + 1,937 `.es`. |
| ACEmulator/ACE-World-16PY | AGPL | 2025-10-04 (v0.8.8) | Base "Coldeve 16PY / Infiltration Feb 2005" data. Release zip 13 MB. |
| ACEmulator/ACE-PCAP-Exports (old URL ACEmulator/ACE-World) | AGPL | 2021-10 | Raw packet-capture-derived data. |

**Patch counts by folder:** RegionDescExtendedData 581, SpellTableExtendedData 3,364, TreasureTable 92, CraftTable 2,766, LandBlockExtendedData 1,195 (25.8 MB), QuestDefDB 3,655, GameEventDefDB 491, EmoteScript 17.

**Weenies by type:** Creature 8,318, Generic 2,895, MeleeWeapon 1,963, Gem 1,391, Clothing 1,271, Portal 1,165, Vendor 464, Door 231, Chest 208, …

**Format.** One `.sql` file per weenie, run as a DELETE then INSERTs into `weenie`, `weenie_properties_{int,int64,bool,float,string,d_i_d,i_i_d,position,attribute,attribute_2nd,skill,body_part,spell_book,create_list,generator,emote,emote_action,event_filter,anim_part,palette,texture_map,book,book_page_data}`. Each value carries an enum-name comment. Example: `09 WeenieDefaults/Creature/Drudge/00007 Drudge Skulker.sql` has 9 emote rows (heartbeat twitches), `XpOverride=1000` and `TargetingTactic`.

**Placements.** Landblock files (for example Holtburg `6 LandBlockExtendedData/A9B4.sql`) insert into `landblock_instance` (guid `0x7A9B4xxx`, wcid, cell, origin, quaternion) and `landblock_instance_link`.

**Other world tables:** `spell` (the server StatMod fields), `treasure_death`, `treasure_wielded`, `treasure_material_*`, `recipe`/`recipe_mod*`/`cook_book`, `quest`, `encounter`, `event`, `house_portal`, `points_of_interest`.

**Where loot rules live.** Much of the loot *logic* is ACE C# code (`Factories/LootGenerationFactory*.cs`, `Factories/Tables` 787 KB, `Factories/Enum` 1.6 MB), not DB data. Architecture B would have to port it.

**Correction to the earlier prototype's docs.** Its README says armour lacks ClothingBase in the world data. Basic armour does carry it (`00072 Platemail Hauberk.sql`: DID 7 `ClothingBase = 0x100000A5`). Its other size claims (a "~660 MB" SQL; 33,052 vs ~44k weenies) are also inconsistent. Treat the earlier prototype's PLAN/README claims as unverified.

## 4. Other servers

| Server | Language / license | Status | Notes |
|---|---|---|---|
| GDLE (gitlab Scribble/gdlenhanced) | C++, AGPL | Last code 2021-01; README says "THIS REPO IS NO LONGER MAINTAINED… closed fork" | |
| bDekaru/ClassicDereth | C++, AGPL | 2022 | GDL-based |
| GamesDeadLol/GDL | C++, "other" | 2018 | |
| bDekaru/ClassicACE | C#, AGPL | 2025-09 | ACE fork, Feb-2005 era |
| derandark/PhatAC | C++, GPL-3 | 2017 | Local launcher; old |
| LPC-language/AsheronsFall | LPC/DGD, AGPL | | |
| CrimsonMage/BetterACE | Rust, AGPL | Created 2026-10-07 | Uses PostgreSQL; "not yet a playable replacement server" |
| Awful-Waffle-Rofl/ACE-DreamWeave | C#, AGPL | | ACE fork |
| **dereth-network/dereth → `empyrean/`** | Rust, AGPL (empyrean/LICENSE) | v0.2.2, 2026-10-09; created 2026-09-16 | See below |

**Empyrean in detail.** It is a port of ACE in Rust:
- One executable, **no database server**: `empyrean-import --sql ACE-World-Database-v0.9.N.sql --out world.pack` builds the world, and characters and accounts go into SQLite `shard.db` and `auth.db`.
- Unfinished: it logs `not ported: ACE: <member>` for gaps, and lists 65 reproduced ACE bugs in `ACE-BUGS.md`.
- Very new but moving fast. It is the strongest candidate for a future "no-MySQL" sidecar.

**Tradeoff.** ACE is the only mature, maintained, end-of-retail-capable rules engine. Everything else is either abandoned or not yet complete.

## 5. Protocol surface (for Architecture A)

**Size.** `protocol.xml` (zegeger heritage; **the repo has no license file**) defines:
- 14 client-to-server and 91 server-to-client top-level messages;
- 156 GameActions inside `0xF7B1`, and 100 GameEvents inside `0xF7B0`;
- 124 enums and 128 types.

ACE implements 61 outbound GameMessages, 94 GameEvents and 149 GameAction handler files.

**Transport**
- 20-byte header `{u32 Seq, u32 Flags, u32 Checksum, u16 Id, u16 Time, u16 Size, u16 Iteration}`.
- 16-byte fragment header `{u32 Seq, u32 Id, u16 Count, u16 Size, u16 Index, u16 Queue}`.
- ISAAC-keyed checksums, acks/retransmit, TimeSync, Echo, and a ConnectRequest/Response handshake.

**MVP subset, about 100 message types**

| Area | Messages |
|---|---|
| Login | DDD interrogation `0xF7E5`/`0xF7E6`/`0xF7EA`, CharacterList `0xF658`, CharGen `0xF656`/`0xF643`, EnterWorld `0xF7C8`→`0xF7DF`→`0xF657`, PlayerCreate `0xF746`, PlayerDescription (GE `0x0013`), LoginComplete (GA `0x00A1`) |
| Objects | CreateObject `0xF745`, UpdateObject `0xF7DB`, Delete `0xF747`, UpdatePosition `0xF748`, UpdateMotion `0xF74C`, VectorUpdate `0xF74E`, Parent/Pickup/SetState `0xF749`–`0xF74B`, ObjDesc `0xF625`, Teleport `0xF751`, Script/Sound `0xF754`/`0xF755`/`0xF750`, about 36 Qualities update messages |
| Movement (client to server) | MoveToState `0xF61C`, AutonomousPosition `0xF753`, Jump `0xF61B` |
| Combat, magic, inventory, vendor, train | The GA and GE listed in §1, plus GA `0x0019`/`0x001A`/`0x001B`/`0x0035`/`0x0036`/`0x0054`–`0x0056`/`0x005F`/`0x0060`/`0x0044`–`0x0047`, and GE `0x0022`/`0x0023`/`0x0062`/`0x0196`/`0x01C7`/`0x02C1`–`0x02C8` |
| NPC dialogue | No dedicated system: it arrives as HearSpeech, Tell or EmoteText chat, plus Confirmation and Popup messages. |

**A extra benefit for A.** ACE computes each creature's or player's ObjDesc (palette, texture and anim-part changes) on the server. The client only applies it, which skips the clothing-composition work that took the earlier prototype a dozen iterations.

**Reusable implementations**

| Library | License | Fit |
|---|---|---|
| **eriknihlen/OpenAC** (`src/AcDream.Core.Net`: Transport/, Cryptography/IsaacRandom.cs, Messages/ with 78 entries) | **MIT**, net10.0, C# | Beta, "fully playable against an ACEmulator server". NOTICE says it "does not include code from ACEmulator". Includes a headless host. |
| Chorizite/Chorizite.ACProtocol | MIT, netstandard2.0 | Message codecs generated from protocol.xml |
| merklejerk/holtburger | Rust, AGPL | |
| enknamel/acswarm | Rust, GPL-3 | |
| AC:Unreal (thwargle.com/unreal) | License not stated | UE5 client talking to ACE/GDLE; "Community preview v106" (2026-10-09) |

## 6. Architecture comparison

Estimates are for one experienced engineer working with AI help. They count only work beyond what the earlier prototype already has (terrain, statics, DAT decoders, sky, character rendering, particles, basic HUD). **Client work every option needs:**
- Full player collision for buildings and dungeons, ported from ACE Physics (AGPL) or OpenAC (MIT): 4–8 weeks.
- MotionTable-driven animation for every creature: 3–6 weeks.
- The remaining UI panels: 4–8 weeks.

| | A: Godot client ↔ local ACE | B: own logic driven by ACE data | C: B now, port ACE systems in incrementally |
|---|---|---|---|
| Logic fidelity | ACE-level from day one: combat, magic, AI and pathing, loot, quests, emotes, vendors, crafting, housing, luminance and augmentations | Only what gets re-derived; risk of invented behaviour (see the earlier prototype's own "drift postmortems") | High per ported system, but each needs its messaging seam rewritten (123 `EnqueueSend` files) |
| New work beyond shared client | Transport and codecs 2–4 wk (with OpenAC/Chorizite reuse; 6–10 from scratch). Object table and ObjDesc 2–4 wk. Remote motion interpolation 3–6 wk. Wiring the UI to events 4–8 wk. Packaging 1–2 wk (portable MariaDB + scripted ACE bootstrap); SQLite port of ACE.Database +2–4 wk, or evaluate Empyrean 1 wk. **About 4–7 months to a retail-equivalent single-player game.** | Generators/spawns 2–4 wk; monster AI, navigation and server-side movement 6–10; combat 4–8; magic and enchantments 6–10; loot 4–8; emote, dialogue and quest engine 6–10; inventory rules 3–6; crafting 3–5; XP, death and vitae 2–4; portals, doors and locks 2–4. **Minimum game about 3–6 months; parity 12–24+ months.** | About 60% of B's cost per system for higher fidelity; **8–14 months**. Embedding all of ACE with a loopback session is really A run in-process. |
| Runtime deps | ACE (.NET 10 runtime) + MariaDB/MySQL (GPLv2 server), or a SQLite-ported ACE, or Empyrean | Godot + SQLite world file | Godot + SQLite |
| License | Client can use any license if it only talks over sockets and reuses MIT code (OpenAC, Chorizite). Shipping ACE binaries requires offering ACE source (AGPL §6, and §13 if modified and used over a network). World SQL is AGPL: bundling it or a derived DB means providing its source. MariaDB is GPLv2. | Clean-room code can use any license. Porting ACE code "verbatim" (as the earlier prototype does: `Spell.TryBurnComponents`, `SpellComponentsTable.GetSpellWords`, `Creature_Magic.cs` Mana Conversion) makes it a derivative, so AGPL; the earlier prototype is already AGPL. User-supplied world data is input, not linked code. | The whole Godot project is AGPL. Compatible with Godot (MIT) and with the earlier prototype's current license. |

Applies to all three: DAT files must be supplied by the user. The AC copyrights belong to the rights holders; "abandoned" has no legal status, although ACE's README says the trademark registration has expired.

## 7. Recommended plan
1. Keep the earlier prototype's renderer and DAT layer. Replace its game logic layer (combat, AI, loot, and later quests and vendors) with an AC-protocol client session. Keep the existing logic only as an optional "offline sandbox" backend behind one interface.
2. Develop against a running ACE server right away.
3. Ship single-player as a launcher with these steps:
   - start a bundled ACE and a portable MariaDB on localhost;
   - configure the database through env vars;
   - create an account automatically;
   - start the Godot client with credentials;
   - shut everything down when the client exits.
4. Later, remove MySQL: either port ACE.Database to EF Core SQLite (the earlier prototype's schema copies and the ace-to-sqlite DBs are head starts), or switch to Empyrean once it matures.
5. Single-player tuning through ACE `PropertyManager` properties and Harmony mods, not forks.
6. Point REA/Ghidra at client-side questions only (physics, animation, UI). acclient-AI-RE already has a PDB-symbolized decompilation of 11.4186 and a byte-matched function map for 11.6096.


## KEY FACTS
- ACE (ACEmulator/ACE) license is AGPL-3.0; LICENSE also appends GPLv3 text that covers 8 early contributors (source: gh api repos/ACEmulator/ACE/contents/LICENSE).
- ACE.Server and ACE.Database target net10.0. ACE.Entity, ACE.DatLoader and ACE.Common target netstandard2.0. ACE.Database uses EF Core 9.0.20 with Pomelo.EntityFrameworkCore.MySql 9.0.0 (source: Source/*/ *.csproj).
- Latest ACE release is v1.78.4816 (2026-09-17), asset ACE.Server-v1.78.4816.zip, 7.8 MB. It is a framework-dependent publish built in Docker from dotnet/sdk:10.0 (source: gh releases; Dockerfile).
- ACE activity: last push 2026-10-08; about 5,868 commits; 178 commits in the last 12 months, of which 37 are substantive (the rest are dependabot and CI) (source: gh commits API).
- ACE needs three MySQL/MariaDB databases: ace_auth, ace_shard and ace_world. Config.js.example points them at 127.0.0.1:3306; docker-compose uses mysql:8.0 (source: Source/ACE.Server/Config.js.example, docker-compose.yml).
- ACE startup runs raw MySQL-specific SQL: ShardDatabase.GetSequenceGaps uses @rownum user variables and is called by GuidManager; GetEstimatedBiotaCount queries information_schema. WorldDbContext uses UseMySql, UseCollation(utf8_general_ci) and HasCharSet(utf8mb3) (source: Source/ACE.Database/ShardDatabase.cs, Models/World/WorldDbContext.cs).
- No SQLite fork of ACE was found in GitHub code, repo or issue searches or in a web search. amoeba/ace-to-sqlite publishes SQLite conversions: ace_world_patches.db 131.5 MB, ace_world_base.db 92 MB, ace_pcap_exports.db 47 MB (source: gh releases amoeba/ace-to-sqlite v0.9.295).
- ACE game logic is coupled to protocol output: 123 files call EnqueueSend and 67 call EnqueueBroadcast (source: gh search code in ACEmulator/ACE).
- ACE C# source is 1,771 .cs files, about 13.3 MB. Sizes: Factories 2.7 MB, WorldObjects 2.5 MB (145 files), Physics 896 KB (162 files), Network 701 KB (426 files), EmoteManager.cs 90 KB, EmoteType has 124 values (source: git tree of ACEmulator/ACE master).
- ACE can run unattended: ACE_SQL_INITIALIZE_DATABASES and ACE_SQL_*_DATABASE_* env vars, auto-download of the latest world DB from the GitHub API, AllowAutoAccountCreation, and 169 runtime server properties such as xp_modifier, quest_mindelta_rate and vitae_penalty (source: Program_Setup.cs, Program_DbUpdates.cs, Managers/PropertyManager.cs).
- World DB latest release is ACE-World-16PY-Patches v0.9.296 (2026-10-08), asset ACE-World-Database-v0.9.296.sql.zip 18.6 MB, AGPL-3.0 (source: gh releases). Unzipped it is about 150 MB (source: dereth-network/dereth empyrean/SETUP.md).
- 16PY-Patches repo patch folders: RegionDescExtendedData 581, SpellTableExtendedData 3,364, TreasureTable 92, CraftTable 2,766, LandBlockExtendedData 1,195, QuestDefDB 3,655, WeenieDefaults 20,773 .sql + 1,937 .es, GameEventDefDB 491. Weenies include 8,318 creatures and 464 vendors (source: git tree of ACE-World-16PY-Patches).
- The world schema has 53 tables, including weenie_properties_emote/emote_action, weenie_properties_generator, landblock_instance(_link), spell, treasure_death/wielded/material_*, recipe*, quest, encounter and event (source: Database/Base/WorldBase.sql).
- World SQL uses MySQL-only syntax such as 'SET @parent_id = LAST_INSERT_ID();' (source: Patches/9 WeenieDefaults/Creature/Drudge/00007 Drudge Skulker.sql).
- Client-side analysis of acclient 11.4186 (with PDB) and 11.6096 (MD5 116d9a66a70b6af449dc3a28d82f2f6d, same as the user's exe) says every roll is server-side, the client is authoritative only for its own movement, and CEmoteTable, CreationProfile and the maneuver table contents are never read (source: Mag-nus/acclient-AI-RE docs/04-combat-magic.md, 00-architecture.md §7).
- protocol.xml defines 14 client-to-server and 91 server-to-client top-level messages, 156 GameActions and 100 GameEvents. Melee attack 0x0008 carries only ObjectId, Height and Power (source: ACClientLib/ACProtocol protocol.xml; the repo has no license).
- ACE implements 61 GameMessages, 94 GameEvents and 149 GameAction handler files (source: ACE git tree, Network/*).
- Portal.dat tables shared by client and server: XpTable 0x0E000018, SkillTable 0x0E000004, SecondaryAttributeTable 0x0E000003, SpellTable 0x0E00000E, SpellComponentsTable 0x0E00000F, CharGen 0x0E000002, GeneratorTable 0x0E00000D, ContractTable 0x0E00001D (source: ACE.DatLoader/FileTypes/*.cs FILE_ID).
- An earlier private prototype by the author is a single-player Godot 4 + .NET 8 AC recreation (AGPL-3.0). Its combat, AI and loot are placeholder-level; quest, dialogue and vendor work was only planned.
- GDLE (gitlab Scribble/gdlenhanced) is AGPL; last code commit 2021-01; README says 'THIS REPO IS NO LONGER MAINTAINED' and that later work moved to a closed fork (source: GitLab API).
- Empyrean (dereth-network/dereth/empyrean) is an AGPL Rust port of ACE: one executable, no DB server, a world.pack built from the ACE world SQL, SQLite shard and auth. It is unfinished ('not ported: ACE: <member>'); v0.2.2 released 2026-10-09; repo created 2026-09-16 (source: empyrean/README.md, SETUP.md, releases).
- OpenAC (eriknihlen/OpenAC) is an MIT, .NET 10 C# client, 'fully playable against an ACEmulator server'. It has a reusable AcDream.Core.Net (Transport, ISAAC crypto, messages) and its NOTICE says it does not include ACEmulator code (source: README.md, NOTICE.md).
- Chorizite/Chorizite.ACProtocol is MIT, netstandard2.0, with codecs generated from protocol.xml (source: gh repo).
- AC:Unreal (thwargle.com/unreal) is a UE5 client that connects to ACE and GDLE servers; Community preview v106 dated 2026-10-09; license not stated (source: WebFetch thwargle.com/unreal).
- Base armour weenies do carry ClothingBase, e.g. 00072 Platemail Hauberk has DID 7 = 0x100000A5. This contradicts the earlier prototype's README, which claims most armour lacks it (source: 16PY-Patches Clothing/Armor/00072 Platemail Hauberk.sql).

## RECOMMENDATIONS
- Use Architecture A: a Godot client that speaks the AC protocol to a bundled local ACE. Reuse the earlier prototype's renderer and DAT layer, and move the earlier prototype's placeholder combat, AI and loot behind an optional 'offline sandbox' backend interface.
- Develop against a running ACE server from day one; packaging can come later.
- Reuse MIT protocol code (OpenAC AcDream.Core.Net for transport and ISAAC; Chorizite.ACProtocol for codecs) so the client license stays independent of ACE's AGPL. Avoid copying ACClientLib/ACProtocol protocol.xml verbatim: it has no license.
- Package v1 as a launcher that runs a portable MariaDB plus ACE.Server (needs the .NET 10 runtime) on localhost. Configure it with the ACE_SQL_INITIALIZE_DATABASES / ACE_SQL_* env vars and AllowAutoAccountCreation, then start the Godot client with the credentials.
- For v2, remove MySQL: either port ACE.Database to EF Core SQLite (rewrite GetSequenceGaps and GetEstimatedBiotaCount, replace the MySqlConnector setup scripts, reuse the earlier prototype's WorldSchema.cs/ShardSchema.cs or the ace-to-sqlite DBs), or evaluate Empyrean's world.pack + SQLite server once it matures.
- Do single-player tuning through ACE PropertyManager settings (xp_modifier, quest_mindelta_rate, vitae_penalty, drop rates) and Harmony mods, not by forking ACE.
- Keep REA/Ghidra work on client-side behaviour only (physics, animation, UI, transport). Start from Mag-nus/acclient-AI-RE: a PDB-symbolized 11.4186 decompilation and a byte-matched function map for 11.6096, the user's exact build.
- If Architecture B or C continues, budget loot as a port of ACE's Factories code (about 2.4 MB of tables), not as DB data. Treat the emote engine (124 action types) as the core of NPCs and quests. Accept that the project stays AGPL wherever ACE code is ported.
- Re-check the earlier prototype's PLAN and README claims before relying on them. Its PLAN marks AI with pathfinding as done, but the code says no pathfinding; its ClothingBase and world-size claims are inconsistent with the data.

## RISKS
- Godot 4.7's GodotSharp packages target net8.0. Running ACE in-process needs a net10.0 Godot project, which forum reports say works but is not officially supported. This is unverified, which is part of why the sidecar is preferred.
- No SQLite variant of ACE exists. A port would need upkeep across ACE releases (about one per quarter). The SQLite type mapping of ace-to-sqlite output against ACE's EF models is untested.
- Empyrean is less than a month old, and its gap coverage ('not ported' items) and correctness are unknown. BetterACE, OpenAC, acswarm, Dereth and acclient-AI-RE are partly or wholly AI-generated, with their own documented error patterns.
- Effort estimates are judgment-based and assume one experienced engineer with AI help. They exclude the shared client work (player collision with building/dungeon BSP, full MotionTable animation, UI), which dominates every option.
- Legal: AC copyright is held by its rights holders and 'abandoned' has no legal status. DATs must be supplied by the user. Bundling the AGPL world SQL, or a DB derived from it, means providing source. MariaDB GPLv2 terms apply if bundled. The provenance of the 16PY base data (Feb 2005 'Coldeve 16PY') is not documented in its repo.
- No ACE memory or CPU footprint numbers for single-player were measured. Config notes that world DB precaching would take about 1.7 GB of RAM (off by default).
- ACClientLib/ACProtocol (protocol.xml) has no license file, so reuse terms are unclear. Chorizite.ACProtocol (MIT) builds generated code from it.
- The client is authoritative for its own movement, so any Godot client (A, B or C) must reproduce retail physics closely enough for ACE to accept AutonomousPosition updates. ACE corrects rather than simulates player movement, and the tolerance thresholds were not checked.

## SOURCES
- https://github.com/ACEmulator/ACE (README.md, LICENSE, Source/ACE.Server/ACE.Server.csproj, Source/ACE.Database/ACE.Database.csproj, Source/ACE.Database/ShardDatabase.cs, Source/ACE.Database/Models/World/WorldDbContext.cs, Source/ACE.Server/Config.js.example, Source/ACE.Server/Program.cs, Program_Setup.cs, Program_DbUpdates.cs, Managers/PropertyManager.cs, Network/Session.cs, Network/PacketHeader.cs, Network/PacketFragmentHeader.cs, Database/Base/WorldBase.sql, Dockerfile, docker-compose.yml, appveyor.yml)
- https://github.com/ACEmulator/ACE/releases/tag/v1.78.4816
- https://github.com/ACEmulator/ACE/wiki/ACE-Hosting
- https://github.com/ACEmulator/ACE/wiki/ACE-Hosting-Windows
- https://github.com/ACEmulator/ACE-World-16PY-Patches (README, Database/Patches/*, releases v0.9.294-v0.9.296)
- https://github.com/ACEmulator/ACE-World-16PY (README, Database/3-Core/*, release v0.8.8)
- https://github.com/ACEmulator/ACE-PCAP-Exports
- https://github.com/amoeba/ace-to-sqlite (README, releases v0.9.295)
- https://github.com/ACClientLib/ACProtocol (protocol.xml)
- https://github.com/Chorizite/Chorizite.ACProtocol
- https://github.com/Mag-nus/acclient-AI-RE (README.md, 2013-09 11.4186/docs/00-architecture.md, 03-object-model.md, 04-combat-magic.md)
- https://github.com/eriknihlen/OpenAC (README.md, NOTICE.md, src/AcDream.Core.Net)
- https://github.com/dereth-network/dereth (README.md, empyrean/README.md, empyrean/SETUP.md, empyrean/ACE-BUGS.md, empyrean/CHANGES.md, empyrean/LICENSE)
- https://gitlab.com/Scribble/gdlenhanced (README.md, LICENSE via GitLab API)
- https://github.com/bDekaru/ClassicDereth
- https://github.com/bDekaru/ClassicACE
- https://github.com/GamesDeadLol/GDL
- https://github.com/derandark/PhatAC
- https://github.com/LPC-language/AsheronsFall
- https://github.com/CrimsonMage/BetterACE
- https://github.com/Awful-Waffle-Rofl/ACE-DreamWeave
- https://github.com/enknamel/acswarm
- https://github.com/merklejerk/holtburger
- https://github.com/lkasser/Decal-ACUnreal
- https://www.thwargle.com/unreal/
- https://github.com/aquafir/ACE.BaseMod
- https://github.com/morluto/rea
- https://github.com/godotengine/godot/releases
- https://packages.nuget.org/packages/GodotSharp/
- https://forum.godotengine.org/t/net10-and-c-14/133412
- https://github.com/godotengine/godot-proposals/discussions/13076
- https://godotengine.org/article/godotsharp-packages-net8/