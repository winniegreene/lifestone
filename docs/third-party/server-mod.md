# Third-party sources: M2 server-mod track

The server-mod track ([m2-plan.md](../milestones/m2-plan.md) §3.3 "server mod", §4.12) builds Lifestone's ACE server
mod in `server/mod`: the `@lifestone` wire contract shared with the client (`server/mod/Shared`, namespace
`Lifestone.ServerTools`) and the mod itself (`server/mod/Lifestone.ServerMod`). It also extends the backend scripts
(`server/setup-server.ps1`, `start-server.ps1`, `stop-server.ps1`, `build-mod.ps1` and `server/lib/LifestoneServer.psm1`)
to build, install and check the mod, keep secrets.json in schema 2 and create the sentinel Admin account. Lifestone
is AGPL-3.0, and every source below is compatible with that.

**What is ported:** five verbs re-express the call sequences of six ACE admin and character commands, listed under "Ported code"
with their port headers. Nothing else is copied: the rest calls ACE's public API or re-expresses facts about ACE's
behaviour, cited by file and line below.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d412459ff619a12cc18f82bcf72c1c2a8 (`vendor-eval/ACE`, the commit `server/versions.json` pins) | The verbs' call sequences (ported, below); the mod API the mod is written against and the boot order it relies on; the account rules the setup scripts rely on. The mod compiles against the setup-built ACE assemblies and ships none of them. |
| natemcmaster/DotNetCorePlugins (McMaster.NETCore.Plugins, as ACE ships it) | Apache-2.0 | the version in ACE 95ef426d's build | **Reference only:** the loader behaviour ACE configures (`PreferSharedTypes`, `EnableHotReload` from Meta.json, ModContainer.cs:53-65): shared ACE types win, so the mod must not carry its own copy of an ACE assembly (the type-identity trap), and hot reload is what `HotReload: false` turns off. Not referenced by the mod. |
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` and `symbol_transplant_11.6096.tsv` | Turbine/WB IP; reference only | the pin of [retail-addresses-m2.md](../research/retail-addresses-m2.md) | The `LandDefs` function names and addresses cited by `PlaceRules.cs`. Nothing copied. |
| Lifestone.Net (this repository) | AGPL-3.0 | | `src/Lifestone.Net/Sockets/LoopbackPolicy.cs` and `LoopbackRequiredException.cs`, compile-linked into the mod (owned by the net track; one loopback gate, m2-plan §1.4). |

## Ported code

| Lifestone file | Ported from (ACEmulator/ACE@95ef426d, AGPL-3.0) | What was taken, and the changes |
|---|---|---|
| `server/mod/Lifestone.ServerMod/Verbs/WhereVerb.cs` | `Source/ACE.Server/Command/Handlers/DeveloperCommands.cs` `HandleMyLoc` (:1767-1773) | What `@myloc` reads (the landblock, Location, the physics position). Changes: Player level, the requesting player only; the teleporting flag and the force-position and teleport sequences added; one machine-readable reply formatted invariantly instead of three chat lines. |
| `server/mod/Lifestone.ServerMod/Verbs/PlaceVerb.cs` | `Source/ACE.Server/Command/Handlers/AdminCommands.cs` `HandleTeleportPoi` (:875-909), `HandleTeleportLOC` (:912-962) | The call sequences: point of interest, portal weenie, Destination, `WorldObject.AdjustDungeon`, `Player.Teleport`; and `new Position(cell, x, y, z, qx, qy, qz, qw)`, `Player.Teleport`. Changes: Player level; the name is matched ordinally ignoring case over the cached rows' own names (ACE lower-cases a cache key in the thread's culture and caches misses as nulls); a missing weenie or destination answers `failed` (`@telepoi` has no such guard); the location comes from the shared invariant parser (no `float.TryParse` in the server's culture) and its cell must be in the cell dat index; teleporting, logging-out and dead players are refused as `busy`; one machine-readable reply. |
| `server/mod/Lifestone.ServerMod/Verbs/QuestVerb.cs` | `Source/ACE.Server/Command/Handlers/AdminCommands.cs` `Handleqst` (:3571-3730) | The call sequences `QuestManager.Stamp`, `QuestManager.Erase` and a read of the registry row (`QuestManager.GetQuest`) on the player's own `QuestManager`. Changes: Player level, the requesting player's own character only (`@qst` acts on the targeted creature); `stamp` is `QuestManager.Stamp`, a quest script's call, never `@qst stamp`'s forced completion count; no `erase *`; the name comes from the shared parser (a bounded character set, no `@`); erasing a quest the character does not hold is an answer; a player who is logging out is refused as `busy`; one machine-readable reply with the completions before and after. Update and Erase mark the character changed and ACE saves it; only `char purge` (`CharVerb.cs`) touches the shard database. |
| `server/mod/Lifestone.ServerMod/Verbs/XpVerb.cs` | `Source/ACE.Server/Command/Handlers/DeveloperCommands.cs` `HandleGrantXp` (:787-827) | The call sequence `Player.GrantXP(amount, XpType.Admin, ShareType.None)` and `@grantxp`'s limit of 999,999,999,999. Changes: Player level, the requesting player only; the experience before is read on the player's action queue just ahead of ACE's own update and the reply is queued right behind it, so it reports what this request added; no audit-channel broadcast; one machine-readable reply. |
| `server/mod/Lifestone.ServerMod/Verbs/CharVerb.cs` | `Source/ACE.Server/Command/Handlers/CharacterCommands.cs` `HandleCharacterForcedDelete` (@deletecharacter, :51-110); `Source/ACE.Database/ShardDatabaseOfflineTools.cs` `PurgeCharacter` (:76-150); `Source/ACE.Server/Managers/PlayerManager.cs` `ProcessDeletedPlayer` (:484-502) | The call sequence: the shard database's serialized queue (`GetCharacters`, `GetCharacter`), `PlayerManager.ProcessDeletedPlayer` on `WorldManager.ActionQueue`, then `ShardDatabaseOfflineTools.PurgeCharacter`. Changes: Player level; only a character on the requesting player's own account that is already marked for deletion (`@deletecharacter` marks it deleted itself and boots it), never an online one, the requester's own or a graduated test character (the rules are Lifestone's own, `Core/CharPurgeRules.cs`); the purge runs on the shard database's worker after a second read of the row, never on the world thread, and is checked by a read afterwards (PurgeCharacter logs a failed save and returns its counts anyway); `HandlePlayerDelete` is not repeated (ACE ran it when the character was marked); one machine-readable reply with PurgeCharacter's three counts. The one verb that writes the database. |

## ACE APIs and facts the mod relies on (ACE 95ef426d)

| What | Where in ACE | Used by |
|---|---|---|
| `IHarmonyMod` (`Initialize`, `Dispose`); the type `<assembly>.Mod`, the DLL `<folder>\<folder>.dll` and `Meta.json` | `ACE.Server/Mods/IHarmonyMod.cs`, `ModContainer.cs:28-40` | `Mod.cs`, the project's assembly name |
| An exception from `Initialize` leaves the mod inactive with no commands | `ModContainer.cs:107-121`, `ModManager.cs:218-222` | the load gate in `Mod.cs` |
| Meta.json is read case-sensitively; `HotReload` defaults to true | `ModManager.cs:147`, `ACE.Common/ConfigManager.cs:76-82`, `ModMetadata.cs:26` | `Meta.json` (PascalCase, every field) |
| ACE loads every `ace-mods` subfolder with a Meta.json | `ModManager.cs:113-123` | the install keeps its staging folder outside `ace-mods`; start-server warns about other mods |
| `ModManager.Log`, `GetModContainer`; the loggers `ACE.Server.Mods.ModManager` and `ModContainer` and their mod-loader lines | `ModManager.cs:141-159, 248, 261-305`, `ModContainer.cs:52-207` | every log line; the hot-reload check; start-server's readiness and fail-fast patterns |
| Mods are enabled before the databases, sockets and world | `Program.cs:155-158, 250, 312-315, 333-343` | `StartupWatch.cs` |
| `AutoPromoteNextAccountToAdmin` is set when no Admin exists, before `Shard` | `ACE.Database/DatabaseManager.cs:30-36, 58, 63` | `StartupWatch.cs` (`AutoPromotion`); the sentinel Admin account |
| `WorldManager.WorldStatus`, opened last unless `world_closed`; "World is now open" on the audit channel | `Managers/WorldManager.cs:39-45, 74-78`, `Program.cs:340-343` | the ready line; start-server `-AllowNoMod` |
| `Server.Network.Host` falls back to every interface when unparsable | `Network/Managers/SocketManager.cs:22-35` | the host check |
| `Server.Accounts.DefaultAccessLevel`, `AllowAutoAccountCreation` | `ACE.Common/AccountDefaults.cs:16, 22` | the load gate |
| `ServerBuildInfo` does not identify 95ef426d (it says 1.77 / 4815) | `ServerBuildInfo_Dynamic.cs:6-10` | why the gate hashes ACE.Server.dll instead |
| Commands: `CommandHandlerAttribute` (includeRaw), the OrdinalIgnoreCase table, quote-stripping `ParseCommand`, `StuffRawIntoParameters`, console invocation with a null session, "Command created" | `Command/CommandHandlerAttribute.cs:26`, `CommandManager.cs:22, 74, 158-191, 193-238, 241-321` | `LifestoneCommand.cs`, `ToolRequestParser.cs`, `Test-LsServerModLive` |
| `@` commands from Talk on the world thread; "Unknown command"; silence for refused access or RequiresWorld | `Network/GameAction/Actions/GameActionTalk.cs:21-96`, `Network/Managers/InboundMessageManager.cs:88-110` | the flags of the command; `ToolsWire.IsModMissing` |
| `GameMessageSystemChat` (F7E0) written as a code-page-1252 String16L; `NetworkSession.EnqueueSend` locks per message group | `Network/GameMessages/Messages/GameMessageSystemChat.cs`, `Network/Extensions.cs:12-21`, `Network/NetworkSession.cs:117-135` | `Reply.cs`; the ASCII reply of at most 65,534 characters; `places` answering from a task |
| `Player.SendMessage` drops squelched messages | `WorldObjects/Player.cs:1079-1085` | why `Reply.cs` queues on the session instead |
| `NetworkStatistics` counters | `Network/NetworkStatistics.cs:19-69` | `NetVerb.cs` |
| `Session.EndPointC2S`, `Account`, `AccessLevel`, `Player` | `Network/Session.cs:28-49` | the loopback guard, `StatusVerb.cs`, `ToolCall.cs` |
| `Player.Location` (`ACE.Entity.Position`: cell, origin, rotation; the constructor takes x, y, z, w), `CurrentLandblock.Id`, `PhysicsObj.Position.ObjCellID` | `ACE.Entity/Position.cs:18-70, 220-238`, `Entity/Landblock.cs:51`, `Physics/Common/Position.cs:14` | `WhereVerb.cs`, `PlaceVerb.cs` |
| Sequences: `SequenceManager.GetCurrentSequence` gives the bytes of a `UShortSequence` for ObjectForcePosition and ObjectTeleport | `Network/Sequence/SequenceManager.cs:12-15, 151-183`, `SequenceType.cs:9-11`, `UShortSequence.cs` | `AceFacts.CurrentSequence` |
| `WorldObject.Teleporting`, `Player.IsLoggingOut`, `Creature.IsDead` | `WorldObjects/WorldObject.cs:263`, `Player.cs:493`, `Monster_Combat.cs:72` | `AceFacts.TeleportBusy` |
| Points of interest: `CacheAllPointsOfInterest`, `GetPointsOfInterestCache` (keys lower-cased in the thread's culture; misses cached as null); `GetCachedWeenie`; `Weenie.GetPosition(PositionType.Destination)` | `ACE.Database/WorldDatabaseWithEntityCache.cs:96, 650-702`, `ACE.Entity/Models/WeenieExtensions.cs:104-113` | `AceFacts.cs`, `PlaceVerb.cs`, `PlacesVerb.cs` |
| `WorldObject.AdjustDungeon` (loads the destination landblock); `Player.Teleport` (+0.005 x scale on z; may wait 1 s for fog) | `WorldObjects/WorldObject.cs:738-742`, `Physics/Common/LScape.cs:54-63`, `Player_Location.cs:654-708` | `PlaceVerb.cs` |
| The cell dat's file index (landblock files `0xXXYYFFFF`, EnvCell files), filled when ACE opens the dat | `ACE.DatLoader/DatDatabase.cs:30, 57`, `DatManager.cs:20-38` | `AceFacts.CellExists` |
| `Player.GrantXP` queues `UpdateXpAndLevel` on the player's action queue (FIFO, one action at a time on one thread), which caps at the maximum level and levels up; Olthoi players are skipped | `WorldObjects/Player_Xp.cs:53-82, 87-121, 276-306`, `Player_Tick.cs:616-619`, `Entity/Actions/ActionEventDelegate.cs`, `Entity/Actions/ActionQueue.cs` `RunActions` | `XpVerb.cs` |
| `Creature.QuestManager` (a Player's is made with it); `QuestManager.Stamp` is `Update` (adds the row with one completion, or one more completion, unless `IsMaxSolves` for the quest's world definition); `Erase` removes the row; `GetQuest` reads it (case-insensitive); `GetQuestName` drops `@` and what follows; the registry is guarded by `Player.CharacterDatabaseLock`; Update and Erase set `CharacterChangesDetected` | `WorldObjects/Creature.cs:27-40`, `Managers/QuestManager.cs:135-143, 172-218, 281-292, 407-433, 484-504`, `ACE.Database/Models/Shard/CharacterExtensions.cs:347-415`, `WorldObjects/Player_Database.cs:25, 55, 82-121` | `QuestVerb.cs` |
| A portal's `QuestRestriction` needs the quest held and not solvable again; quest `SentryTaskComplete` (world quest 4554) has 1 max solve and no delay | `WorldObjects/Portal.cs:246-255`, `Managers/QuestManager.cs:334-364` | the note in `QuestVerb.cs`; why one stamp opens the Academy's "Exit to Holtburg" |
| The world thread runs in the machine's culture (en-US is set on the main thread only) | `Program.cs:56`, `Managers/WorldManager.cs:57-64` | `server/mod/.editorconfig` (CA1304/CA1305/CA1310/CA1311 are errors) |
| The `grantxp` limit 999,999,999,999 | `Command/Handlers/DeveloperCommands.cs:823` | `ToolRequestParser.MaxXp` (owner decision D51) |
| A character is marked for deletion by `CharacterDelete` (`DeleteTime` set, `IsDeleted` false, saved through the shard queue, then `HandlePlayerDelete`); `Session.CheckCharactersForDeletion` sets `IsDeleted` once the delay has run out; ACE frees a name as soon as its character is pending (`IsCharacterNameAvailable`) | `Network/Handlers/CharacterHandler.cs:273-324`, `Network/Session.cs:195-210`, `ACE.Database/ShardDatabase.cs:583-595` | `Core/CharPurgeRules.cs` (what pending deletion means; a pending and a live character may share a name) |
| The shard database runs its queued work on one worker thread in order; `GetCharacters(accountId, includeDeleted)` and `GetCharacter(id)` call back on it | `ACE.Database/SerializedShardDatabase.cs:33-78, 205-221` | `CharVerb.cs` (earlier saves have landed before the lookup; the purge runs after a second read) |
| `PlayerManager.ProcessDeletedPlayer` removes the offline player from the offline, name and account caches under the manager's lock; `GetOnlinePlayer` reads under it too; `@deletecharacter` calls the former on the world's action queue | `Managers/PlayerManager.cs:339-352, 484-502`, `Command/Handlers/CharacterCommands.cs:90-104`, `Managers/WorldManager.cs:47, 108` | `CharVerb.cs` |
| `PurgeCharacter` removes the inventory and wielded biotas, the player biota and the character record in one `SaveChanges` on a context of its own, logs a failed save and returns its counts anyway | `ACE.Database/ShardDatabaseOfflineTools.cs:76-150` | `CharVerb.cs`, `CharPurgeRules.Outcome` (the read afterwards) |

## ACE facts the backend scripts rely on (ACE 95ef426d)

| What | Where in ACE | Used by |
|---|---|---|
| An account's password check: bcrypt when `passwordSalt` is "use bcrypt", else Base64 SHA-512 of the password's UTF-8 bytes and the Base64-decoded salt | `ACE.Database/Models/Auth/AccountExtensions.cs` (`PasswordMatches`, `GetPasswordHash`) | `New-LsSentinelAdminSql`: the sentinel's hash of a random password that is then discarded |
| The `account` table (`passwordHash` and `passwordSalt` 88 characters, `accessLevel` a key of `accesslevel` 0-5) | `Database/Base/AuthenticationBase.sql:51-96`, `ACE.Entity/Enum/AccessLevel.cs` | the sentinel Admin account and the account audit |
| A banned account is refused after its password check | `Network/Handlers/AuthenticationHandler.cs:215-227` | the sentinel is banned until 2099 as well |
| New accounts: names at most 50 characters, lower-cased when ACE creates them; created at `DefaultAccessLevel` unless the promotion flag is set | `Network/Handlers/AuthenticationHandler.cs:38-43, 55-83` | the `-AddAccount` name rule; why play and test accounts are Players |
| `account_login_boots_in_use`: a login boots another session of the same account | `Network/Handlers/AuthenticationHandler.cs:157-196` | why the tests use their own account (secrets.json `accounts.test`) |
| The log4net layout `%date [%thread] %-5level (%logger) %message` | `server/templates/log4net.template.config` (Lifestone's) | start-server's anchored log patterns |

## Own implementations (no source copied)

- **The wire contract** (`server/mod/Shared`): `ToolsWire`, the request records and `ToolRequestParser` (its own
  tokenizer over ACE's raw command text), the reply records and their sentences, the source-generated JSON context and
  `PlaceRules`. Written for Lifestone from m2-plan §4.12.
- **The mod** (`server/mod/Lifestone.ServerMod`): the load gate, `StartupWatch`, the command, `ToolCall`, the reply
  sender, `AceFacts`, and the `status`, `net` and `places` verbs; and `Core/` (`ToolRouting`, `OneAnswer`,
  `PlacesBuilder`, `XpGrant`, `QuestChange`, `CharPurgeRules`), the command's decisions that need nothing from ACE, which Lifestone.ServerMod.Tests
  compiles by link and runs. Calls ACE's public API only; no Harmony patching.
- **The backend scripts**: the mod build key, build and install; secrets.json schema 2 and its protected write; the
  sentinel Admin account and the account audit; start-server's readiness, stop-server's `-AceOnly`,
  `Get-LsBackendState`, `Find-LsSecretLeaks` and `Test-LsServerModLive`; `server/build-mod.ps1`.

## Re-expressed retail behaviour

| Lifestone file | Retail functions | 11.4186 | 11.6096 |
|---|---|---|---|
| `server/mod/Shared/PlaceRules.cs` | `LandDefs::gid_to_lcoord`, `LandDefs::lcoord_to_gid` (outdoor cell index from x and y) | 0x00497A90, 0x004A19A0 | 0x00497D70, 0x004A1CD0 |
| | `LandDefs::get_outside_lcoord` (cell column `floor(local / 24.0)` in double) | 0x005A9B00 | 0x005AABB0 |
| | `LandDefs::in_bounds`, `LandDefs::blockid_to_lcoord` (landblock 0xFF is off the map) | 0x0043D650, 0x0043D680 | 0x0043D7F0, 0x0043D820 |
