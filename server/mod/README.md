# Lifestone server mod

An ACE mod that gives a single player admin-free tools through one chat command, `@lifestone`, and keeps ACE's
first-account Admin auto-promotion off. The design is [m2-plan.md](../../docs/milestones/m2-plan.md) §3.3
("server mod") and §4.12; the third-party sources are listed in
[docs/third-party/server-mod.md](../../docs/third-party/server-mod.md).

What it is and is not:

- One **Player-level** command. It answers the requesting session only, and only one that comes from `127.0.0.1`.
- It uses ACE's public mod API only: **no Harmony patching**, and none of ACE's assemblies ship with it.
- **Hot reload is off**: ACE loads the DLL once at boot.
- It runs only in a **loopback-only** ACE with Player-level new accounts, and only in the exact ACE build it was
  compiled against (the load gate below).

| Folder | What |
|---|---|
| `Shared/` | The wire contract (`Lifestone.ServerTools`): the request grammar, the two-line reply and its JSON, the cell rules. Base class library only; compiled into the mod and, by a compile link, into Lifestone.Client. Tested by `tests/Lifestone.ServerMod.Tests`. |
| `Lifestone.ServerMod/` | The mod: `Mod` (the load gate), `StartupWatch` (auto-promotion off, then the ready line), `LifestoneCommand` (the one command), `ToolCall` (one answer per request), `Reply`, `AceFacts` (everything read from ACE's internals, cited) and `Verbs/`. Built against the setup-built ACE in `server/runtime/ace`; outside `Lifestone.slnx`. |
| `Lifestone.ServerMod/Core/` | The command's decisions that need nothing from ACE: `ToolRouting` (the guards in order, and which verb runs), `OneAnswer` (the one-answer gate), `PlacesBuilder` (the `places` list), `XpGrant` (what `xp` added), `QuestChange` (when `quest` is refused, its counts and the check that ACE's registry ended as asked) and `CharPurgeRules` (which characters `char purge` may remove, and the check that the purge did) and `ItemCreateRules` (which weenies `item` refuses, and how its stack settles). The mod compiles them; `tests/Lifestone.ServerMod.Tests` compiles them by link and runs them. Nothing from ACE may appear here. |
| `.editorconfig` | Culture-sensitive calls (CA1304, CA1305, CA1310, CA1311) are build errors for every file here, in both builds. |

## The command

```text
@lifestone status | where | net | places | xp <amount> | place poi "<name>" | place loc <cell> <x> <y> <z> [<qw> <qx> <qy> <qz>] | quest stamp <name> | quest erase <name> | quest has <name> | char purge "<name>" | spell learn <id>[,<id>...] | item <wcid> [<stack>] [palette=<n>] [shade=<f>]  [#id]
```

- Words are separated by spaces. A name with spaces goes in double quotes (one level, no escapes); a one-word name
  may go without.
- The optional last word `#<id>` (1 to 16 of `A-Z a-z 0-9 -`) is echoed in the reply, so a client can match them.
- Verbs ignore case; numbers are invariant (`84.5`, never `84,5`); only printable ASCII is accepted.
- `xp` takes digits only, 1 to 999,999,999,999 (ACE's own `@grantxp` limit). The amount is 64-bit end to end (the request, the parser, the
  reply's `requested`, `added`, `total` and `available`; ACE's `GrantXP` takes a long): the 4,286,609,098 that level 126 needs, which is above
  `int.MaxValue`, is one request.
- `spell learn` takes 1 to 128 whole spell ids (0 to 4,294,967,295) separated by commas, no spaces: `spell learn 2,3,5`.
- `item` takes a weenie class id (a whole number) and optionally a stack size from 1 to 65,535 (`@ci`'s own limit): `item 273 5`. It also takes `palette=<1 to 93>` (ACE's `PaletteTemplate`; Gold is 21, read from `PaletteTemplate.cs`) and `shade=<0 to 1>`, as `@ci`'s own third and fourth arguments do, in either order after the stack: `item 57 palette=21`. An out-of-range or repeated one is `bad-args` with the rule in the reply, and nothing is created.
- A `quest` name is 1 to 64 of `A-Z a-z 0-9 _ - ( )` (every quest name in ACE's world database has only those, bar one with
  an apostrophe), unquoted. Never `@`: ACE reads what follows it as a comment.
- A `char purge` name is 1 to 32 of `A-Z a-z 0-9`, space, apostrophe and hyphen, in double quotes (a one-word name may go without), matched ignoring case.
- A `place loc` cell is 8 hex digits (with or without `0x`). An outdoor cell (index 0x0001-0x0040) must be the one
  that holds x and y, which must be in [0, 192); an EnvCell is 0x0100-0xFFFD. The rotation (w, x, y, z) must have
  length 1 ± 0.01 and defaults to the identity.

Every request gets exactly one F7E0 server message (chat type 0, Broadcast), to the requesting session only:

```text
Lifestone: <a sentence for the chat window>
#lifestone/1 {"v":1,"id":"a1","verb":"status","ok":true,"data":{...}}
```

The JSON is camelCase with nulls left out and every non-ASCII character escaped. The whole text is ASCII and at
most 65,534 characters, so ACE's code-page-1252 String16L carries it unchanged: its length word stays below 0xFFFF,
which a reader following retail's string unpack takes as the escape for a 32-bit length.

### The verbs

| Request | Does | Reply data |
|---|---|---|
| `status` | Nothing; reports the mod, its ACE and the caller | `wire`, `mod`, `aceCommit`, `aceServerDllSha256`, `host`, `port`, `autoPromotion`, `access`, `account`, `character`, `world`, `verbs` |
| `where` | Reads where the server has the player (as ACE's `@myloc`) | `cell`, `x` `y` `z`, `qw` `qx` `qy` `qz`, `landblock`, `physicsCell`, `teleporting`, `forcePositionSeq`, `teleportSeq` |
| `net` | Reads ACE's packet counters, server-wide since boot | `c2SPackets`, `s2CPackets`, `c2SCrcErrors`, `c2SRetransmitRequests`, `s2CRetransmitRequests` |
| `places` | Lists ACE's points of interest, one entry per portal weenie, with its raw Destination | `count`, `names`, `places[]` of `names`, `wcid`, `cell`, `x`..`qz` |
| `xp <n>` | Adds experience (as ACE's `@grantxp`, without its audit broadcast) and answers after ACE has added it | `requested`, `added` (by this request alone, even with other experience queued in the same tick; less at the level cap), `total`, `available`, `level` |
| `spell learn <id>[,<id>...]` | Teaches the caller's own character spells by id through ACE's `Player.LearnSpellWithNetworking` (as `@addspell`), 1 to 128 ids, in order, the quiet learn message (no effect and chat line per spell). One typed outcome per id, from the spell table and the spellbook around ACE's call: `learned` (the character did not know it and now does), `known` (it already knew it: nothing changed, so a ready spellbook is verified by an all-`known` reply) or `unknown` (the id is not in ACE's spell table, which has no id above 65,535: nothing learned). The reply is `ok` whenever the verb ran: a client checks `unknown` is 0. An id may repeat; the repeat is `known`. A spell ACE did not add after the call is `failed` | `spells[]` of `id`, `outcome`, `name` (the spell table's name; absent for `unknown`), and the counts `learned`, `known`, `unknown` |
| `item <wcid> [<stack>] [palette=<n>] [shade=<f>]` | Creates an item in the caller's own pack through ACE's `@ci` path (the cached weenie, `VerifyCreateWeenieType`, `WorldObjectFactory.CreateNewWorldObject`, `SetStackSize`, then `PaletteTemplate` and `Shade` when named (AdminCommands.cs:2559-2563), `Player.TryCreateInInventoryWithNetworking`), without its audit broadcast. **A stack above the item's `MaxStackSize` is clamped to it, and a stack for an item that does not stack is ignored (one is created), as `@ci` does: the reply says so (`clamped`).** A weenie class id the world database lacks is `unknown-wcid`; a weenie type ACE never spawns or one that cannot be picked up is `not-creatable`; a full pack is `no-room` (nothing is created). Creatures are not refused (TODO(item-creature)) | `guid` (the new object's id), `wcid`, `name`, `stack` (what it was created with), `requested` (absent when no stack was asked), `maxStack` (absent for an item that does not stack), `clamped`, `palette` and `shade` (read back from ACE's object; present only when the request named them) |
| `place poi "<name>"` | Teleports to a point of interest (as ACE's `@telepoi`), the name matched ignoring case | `kind` `poi`, `name` (ACE's spelling), `cell`, `x`..`qz` (the destination after ACE's dungeon adjustment) |
| `place loc <cell> <x> <y> <z> [<q>]` | Teleports to an exact location (as ACE's `@teleloc`) | `kind` `loc`, `cell`, `x`..`qz` |
| `quest stamp <name>` | Stamps a quest on the caller's own character, as a quest script's StampQuest emote does (ACE's `QuestManager.Stamp`): the registry row is added with one completion, or gains one, unless the quest's world definition is already at its maximum solves (ACE then leaves it) | `action` `stamp`, `name`, `heldBefore`, `before`, `held`, `after` |
| `quest erase <name>` | Removes the quest's row from the caller's own character (ACE's `QuestManager.Erase`); a quest it does not hold is answered (`heldBefore` false), not an error | the same fields, `action` `erase` |
| `quest has <name>` | Reads whether the caller's own character holds the quest; changes nothing | the same fields, `action` `has` |
| `char purge "<name>"` | Removes a character that is **already marked for deletion** from the shard database, for good, through ACE's own purge (`ShardDatabaseOfflineTools.PurgeCharacter`); the one verb that writes the database. Only a character on the caller's own account is purged (see below) | `name`, `characters`, `playerBiotas`, `possessions` (ACE's three counts; a purge that removed nothing is `failed`, not a reply with zeros) |

Examples (the place reply is Holtburg's real destination; the xp numbers are illustrative):

```text
@lifestone place poi "Holtburg" #a1
Lifestone: teleporting to Holtburg, cell 0xA9B40019 at (84.00, 7.10, 94.00)
#lifestone/1 {"v":1,"id":"a1","verb":"place","ok":true,"data":{"kind":"poi","name":"Holtburg","cell":"0xA9B40019","x":84,"y":7.1,"z":94,"qw":0.996917,"qx":0,"qy":0,"qz":-0.078459}}

@lifestone xp 1000
Lifestone: added 1,000 experience (asked 1,000); total 1,000, unassigned 1,000, level 1
#lifestone/1 {"v":1,"verb":"xp","ok":true,"data":{"requested":1000,"added":1000,"total":1000,"available":1000,"level":1}}

@lifestone place poi "Nowhere"
Lifestone: no place is called "Nowhere"; @lifestone places lists them
#lifestone/1 {"v":1,"verb":"place","ok":false,"error":"not-found","message":"no place is called \"Nowhere\"; @lifestone places lists them"}
```

```text
@lifestone quest stamp SentryTaskComplete #q1
Lifestone: quest SentryTaskComplete stamped: added, completions 1
#lifestone/1 {"v":1,"id":"q1","verb":"quest","ok":true,"data":{"action":"stamp","name":"SentryTaskComplete","heldBefore":false,"before":0,"held":true,"after":1}}

@lifestone quest erase SentryTaskComplete #q2
Lifestone: quest SentryTaskComplete erased (it had 1 completions)
#lifestone/1 {"v":1,"id":"q2","verb":"quest","ok":true,"data":{"action":"erase","name":"SentryTaskComplete","heldBefore":true,"before":1,"held":false,"after":0}}
```

```text
@lifestone char purge "Lifestone Throwaway" #p1
Lifestone: purged Lifestone Throwaway: 1 character record, 1 player biota, 14 possessions
#lifestone/1 {"v":1,"id":"p1","verb":"char","ok":true,"data":{"name":"Lifestone Throwaway","characters":1,"playerBiotas":1,"possessions":14}}

@lifestone char purge "Lifestone Smoke"
Lifestone: Lifestone Smoke is a graduated test character and stays as it is
#lifestone/1 {"v":1,"verb":"char","ok":false,"error":"graduated","message":"Lifestone Smoke is a graduated test character and stays as it is"}
```

Examples of the M3 verbs (the ids are ACE's: spell 2 is Strength Self I; 273 is the pyreal, `coinstack`; the guid is illustrative):

```text
@lifestone spell learn 2,99999 #s1
Lifestone: spells: 1 learned, 0 already known, 1 unknown (not in the spell table: 99999)
#lifestone/1 {"v":1,"id":"s1","verb":"spell","ok":true,"data":{"spells":[{"id":2,"outcome":"learned","name":"Strength Self I"},{"id":99999,"outcome":"unknown"}],"learned":1,"known":0,"unknown":1}}

@lifestone item 273 5 #i1
Lifestone: created 5 x Pyreal (wcid 273, 0x80000123) in your pack
#lifestone/1 {"v":1,"id":"i1","verb":"item","ok":true,"data":{"guid":2147483939,"wcid":273,"name":"Pyreal","stack":5,"requested":5,"maxStack":25000,"clamped":false}}

@lifestone item 4000000000
Lifestone: no weenie has class id 4000000000
#lifestone/1 {"v":1,"verb":"item","ok":false,"error":"unknown-wcid","message":"no weenie has class id 4000000000"}
```

The `spell` and `item` verbs act on **the requesting player's own character** only and write nothing to the database themselves: ACE saves the spellbook with the
character on its own schedule, and its own `TryCreateInInventoryWithNetworking` saves the new item. The M3 mage build (m3-plan D11) uses them: `spell learn` for the profile's spells, `item` for the kits.

The `quest` verbs act on **the requesting player's own character only**: there is no target and no other character's name to
give, and no `erase *`. `before` and `after` are the registry row's completions (0 without a row; a row can hold 0, which
`heldBefore` and `held` tell apart). The `quest` verbs write nothing to the database themselves: ACE marks the character changed and saves it on
its own schedule. A gate that tests a quest may want more than the row: ACE's portals need the quest held **and not solvable
again** (`Portal.cs:246-255`), and `SentryTaskComplete`, which the Academy's "Exit to Holtburg" portal tests, allows one
solve, so one stamp opens it and an erase closes it. A second stamp of a quest already at its maximum changes nothing (`after`
equals `before`).

**The exit portal changes more than the quest.** Using "Exit to Holtburg" runs the portal's own emote set (weenie 29338, EmoteCategory 4, emote 86996 in
world database v0.9.296; ACE calls it from `Portal.ActOnUse` to `EmoteManager.OnPortal` after the teleport, `Portal.cs:287`). It erases ten Academy quests
(`AcademyTokenGiven`, `SentryTaskComplete`, the leggings, cap and gauntlets academy pickups, `CarpWing`, `WaspWingGiven`, `WaspWingDone`,
`bellowsnewbiepickedup`, `protectionorb`), sets the sanctuary to Holtburg (0xA9B40019), sets `RecallsDisabled` (PropertyBool 107) to false (ACE creates
characters with it true, `PlayerFactory.cs:393-394`), shows a pop-up and sets the last portal. So a character that took the exit is graduated for good: a
`quest stamp` before the Use is erased by the portal itself, and these verbs cannot undo the rest (no verb sets a sanctuary, a flag or the nine other
quests). Graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25). The login check and the loopback tests therefore leave a test character's position and the
Sentry's quest as found, nothing more.

**`char purge` and its guards** (owner decision D59, m2-log-in §7 decision 13). The verb exists so the live tests can create a throwaway character
on the test account, delete it through the retail screens and leave the account as it was. It acts on the **requesting player's own account** only,
and answers one reply once ACE has removed the rows. The rules (`Core/CharPurgeRules.cs`, unit tested) refuse in this order, each with its own code:

| Code | Refused when |
|---|---|
| `graduated` | the name is `Lifestone Smoke`, `Lifestone Loopback`, `BSD Mage` or `Holtburg Hunter` (any case): the graduated test characters are the baseline and are never purged |
| `own-character` | the name is the character the caller is playing |
| `unknown-character` | no character of that name is on the caller's account (another account's characters are never read, so they look the same as no character) |
| `character-online` | the character is online |
| `not-pending-deletion` | the character is not marked for deletion: `DeleteTime` is 0 and it is not `IsDeleted` (delete it on the character screen first; the verb never deletes) |

The ACE path (`Verbs/CharVerb.cs`, each step safe while the server runs):

1. **Lookup** through ACE's serialized shard queue (`GetCharacters(accountId, includeDeleted)`): the queue runs in order on one worker, so every save queued earlier, such as the throwaway's logoff saves and the delete's own, has landed before the lookup answers.
2. **ACE's bookkeeping** on the world's action queue, as ACE's `@deletecharacter` does: `PlayerManager.ProcessDeletedPlayer` takes the character out of the offline-player, name and account caches (it locks them itself), and the character is dropped from the caller's session's own character list, as ACE does for a deletion that has run out, so the list sent at the caller's next log off is the account's real one.
3. **The purge** on the shard worker, never on the world thread: a second read of the character's row (the rules run again on it), then `ShardDatabaseOfflineTools.PurgeCharacter` (inventory and wielded biotas, the player biota and the character record in one save), then a read to confirm the record is gone, because ACE's purge logs a failed save and returns its counts anyway. Running in the queue's order means nothing queued for the character can land after it and put the rows back.
4. **One reply** with the counts, from that worker thread (the session's send queue takes messages from any thread); an exception answers once as `failed`.

A `place` success means the teleport **started**: the client sees it finish as ACE's teleport (F751) and position
(F748) messages. ACE may hold a teleport for a second to clear fog first.

### Errors

| Code | When |
|---|---|
| `bad-args` | The request does not parse, or an argument is out of range |
| `unknown-verb` | The first word is not a verb |
| `not-found` | `place poi`: no point of interest has that name |
| `invalid-cell` | `place loc`: the cell is not in ACE's cell dat (an outdoor cell's landblock, or the EnvCell itself) |
| `busy` | `place`: the player is already teleporting, logging out or dead; `xp`, `quest stamp`, `quest erase`, `spell learn`, `item`: logging out (`quest has` is never refused) |
| `not-in-world` | `where`, `place`, `xp`, `quest`, `spell`, `item`: the character is not in a landblock yet |
| `not-loopback` | The session is not from a loopback address |
| `needs-player` | ACE's console asked for anything but `status` or `net` |
| `graduated`, `own-character`, `unknown-character`, `character-online`, `not-pending-deletion` | `char purge` only: the guards above |
| `unknown-wcid`, `not-creatable`, `no-room` | `item` only: no weenie has that class id; the weenie is of a type ACE never spawns or cannot be picked up; the pack has no room |
| `failed` | Anything else: the mod caught an exception (logged in ACE_Log.txt) or ACE lacks the data (a point of interest without a destination), or ACE's registry did not end as the quest verb promises (a stamp that left no row, an erase that left one) |

A client treats an unknown code as `failed`.

### Wire versioning

The reply's second line starts `#lifestone/<version> `; this is version 1 (`ToolsWire.Version`). A change that an
older client would misread (a renamed or retyped field, a new meaning) bumps the version, in the mod and the client
together. Adding a field does not: readers ignore fields they do not know. A client that sees another version reports
"unsupported wire version" and does not show the text as chat. When the mod is missing, ACE answers
`Unknown command: lifestone` (chat type 0x0F) instead.

### ACE's console

`lifestone status` and `lifestone net` typed at ACE's console (session null) answer into ACE's log, as one
`Lifestone console:` line with the reply's two lines joined by ` | `. The other verbs need a player
(`needs-player`). `Test-LsServerModLive` in `server/lib/LifestoneServer.psm1` uses this to check a running backend
without a client.

## Load gate

The mod refuses to load, logs `Lifestone mod refused to load: <reason>` and throws, so ACE leaves it inactive and
registers none of its commands, unless:

- `Config.js` `Server.Network.Host` is exactly `127.0.0.1` (ACE binds every interface when the host does not parse);
- `Server.Accounts.DefaultAccessLevel` is 0 (Player);
- the running `ACE.Server.dll` has the SHA-256 the mod was built against (the mod relies on ACE's internals and boot
  order at one pinned commit);
- `Meta.json` has `HotReload` false.

Then a background thread turns ACE's first-account Admin auto-promotion off as soon as ACE has computed it, and
logs the ready line once the world is open and `@lifestone` is registered. The sentinel Admin account that setup
creates (owner decision D11) keeps ACE from setting the flag in the first place; the mod is the second lock.

## ACE log lines

```text
Lifestone mod loaded: version 0.1.0, wire 1, ACE 95ef426d
Lifestone mod refused to load: <reason>
Lifestone: ACE's first-account Admin auto-promotion is off; new accounts get DefaultAccessLevel 0 (Player).
Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off
Lifestone tool: account=<a> character=<c> verb=<v> id=<id> args=<raw text, at most 200 characters> -> ok|<code>
Lifestone console: <the reply's two lines joined by " | ">
```

`server\start-server.ps1` waits for the ready line and fails at once on the refusal line or on any of ACE's own
mod-loader errors about Lifestone. When ACE says nothing about the mod at all (it never found it, for example because
`Config.js` `ModsDirectory` points elsewhere), it fails 30 s after ACE's `World is now open` instead of at its
timeout: the mod logs its ready line within about 100 ms of the world opening. None of these lines ever holds a password. They are a contract: change them only
with their readers (`ModLog` in `ModInfo.cs`, `Get-LsAceLogSignal` in the server module).

## Building and installing

`server\setup-server.ps1` builds and installs the mod in its "Lifestone server mod" step, right after the ACE build:

1. The **build key** is the SHA-256 of the mod's build files (the `.cs`, `.csproj`, `.json` and `.editorconfig`
   files here, outside `bin` and `obj`, plus the two `Lifestone.Net` files the project links) and of
   `ACE.Server.dll`. Setup skips the step while the key and the installed DLL match `setup-state.json`.
2. `dotnet build` into `server\runtime\mod-build`, with the .NET opt-outs, `--disable-build-servers` and no node
   reuse, against `server\runtime\ace`. The project has no package references, so nothing is downloaded. Restore and
   the intermediate files go to the sibling `mod-build.obj` (`-p:BaseIntermediateOutputPath`), never to this
   project's own `obj\`, so a setup run, a compile check and a test's build never share them.
3. The output must be exactly `Lifestone.ServerMod.dll`, its `.pdb` and `Meta.json` (HotReload false). **The
   type-identity trap:** a copy of `ACE.Server.dll` (or any ACE, Harmony, McMaster or log4net assembly) beside the
   mod would make ACE's plugin loader load the mod's own `ACE.Server`, whose `IHarmonyMod` ACE does not recognise,
   so the mod would silently not load. The project references ACE with `Private=false` for that reason.
4. Install: `server\runtime\ace-mods\Lifestone.ServerMod` is replaced by those three files and their hashes are
   checked. The staging folder stays outside `ace-mods`, because ACE loads every subfolder of it that has a
   `Meta.json`.
5. `setup-state.json` records `modBuildKey`, `modDllSha256` and `modVersion`.

`server\setup-server.ps1 -ModOnly` runs only this step (and the preflight); `-RebuildMod` forces it; `-ModSource
<a server\mod folder>` builds another checkout's or worktree's mod, for trying a branch in the main checkout without
merging it. `server\start-server.ps1` refuses a missing mod, a DLL that is not the recorded one, and a stale mod
(sources or ACE changed since setup; `-AllowStaleMod` warns instead).

In a git worktree, check that the mod compiles without installing anything:

```powershell
pwsh -NoProfile -File server\build-mod.ps1     # into cache\server-mod; exit 3 = skipped (no ACE set up)
```

It builds against the main checkout's ACE (the only one that runs), with its intermediate files in
`cache\server-mod.obj`, and makes the same output checks.

## Culture rules

ACE runs a player's command on its world thread, which takes the machine's culture (ACE sets en-US on its main
thread only), and the client runs in the player's culture. Wire text must not change with either, so every number
is formatted and parsed with the invariant culture and every word is compared ordinally. The `.editorconfig` makes
the culture analyzers build errors here and in Lifestone.Client's build of `Shared/`, and the tests run the contract
under de-DE, tr-TR, ar-SA and fr-FR.

## ACE APIs used (ACEmulator/ACE 95ef426d)

The full table, with file and line for each, is in
[docs/third-party/server-mod.md](../../docs/third-party/server-mod.md). In short: `IHarmonyMod`, `ModManager.Log`
and `GetModContainer` (Mods/); `CommandHandlerAttribute` with the raw text, and console commands with a null session
(Command/); `Session` and `GameMessageSystemChat` (Network/); `DatabaseManager.AutoPromoteNextAccountToAdmin`,
`Shard` and the world database's point-of-interest and weenie caches (ACE.Database); `WorldManager.WorldStatus`;
`NetworkStatistics`; `Player.Location`, `Teleport`, `GrantXP`, `EnqueueAction`, `Sequences`, `Teleporting`,
`IsLoggingOut`, `IsDead`, `QuestManager` (`Stamp`, `Erase`, `GetQuest`); for `char purge` the shard queue (`SerializedShardDatabase.GetCharacters`, `GetCharacter`), `PlayerManager` (`GetOnlinePlayer`, `ProcessDeletedPlayer`), `WorldManager.ActionQueue`, `Session.Characters` and `ShardDatabaseOfflineTools.PurgeCharacter`; `WorldObject.AdjustDungeon`; `DatManager.CellDat.AllFiles` (ACE.DatLoader).
