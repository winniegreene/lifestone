# The session layer (A0)

The engine-free layer between the network session and everything that plays the game: the GameState reducer, the
event bus with its raw tap, the Action API with completion handles, the player tools, the recorder and replayer, the
loopback gate and the headless host ([automation-vision.md](automation-vision.md) §2 and §4, phase A0;
[m2-log-in.md](milestones/m2-log-in.md) DoD 13). It lives in `src/Lifestone.Client/Session/` and
`src/Lifestone.Headless/`, and is owned by the M2 state track.

**Status: contract (step ST0, checkpoint C-state) plus steps ST1 to ST17** (the game-thread context, the
loopback gate, the event bus, the per-object message gate, credentials and options, the recording format, the
reducer's login and world phases, offline sessions with stand-in data (checkpoint C-state-offline), the Action API with
its trackers, the movement channel and intents, the player tools, the live session and its bootstrap (§18), the
replayer (§19) and the headless host (§20)); ST16, the movement scenarios, is built too (§24 to §27). The
surface in §2 to §10
is what the other tracks code against: portal, avatars, physics and the retail UI in wave 1, and the Godot tracks in
wave 2. After the checkpoint it changes only additively, and a change goes through the lead and is recorded here
(§13). The behaviour lands step by step (§12); until a step lands, its members are **loud stubs**: they throw `NotSupportedException` with a `TODO(state): <member>
is not built yet (<step>)` message and, where a log sink is at hand, write the same line as an ERROR. Nothing fakes
success.

The network names come from the net track's contract (checkpoint C-net, `src/Lifestone.Net`): `IAcSession`,
`IAcSessionSink`/`IInboundMessageHandler`, `InboundDispatcher`, `IRawMessageTap`, `AcCredentials`, `SessionPhase`,
`SessionFailure`, `LinkStats` and the message records. §11 maps them onto this layer.

## 1. Layout

| Namespace (folder) | Holds | Used by |
|---|---|---|
| `Lifestone.Client.Session.Contracts` (`Session/Contracts/`) | everything consumers code against: `IGameSessionView`, `IEventBus` and the events, `GameState` and its parts, `IActionApi`, `ActionHandle`, `ActionOutcome`, `IMovementSink`, `MoveIntents`, `CharacterCreateIntent`, `PlayerTools`, `ToolHandle<T>`, `SessionClock`, `ActionLog` | everyone |
| `Lifestone.Client.Session` (`Session/`) | what hosts use: `GameSession`, `SessionBootstrap`, `SessionOptions`, `GameThreadSynchronizationContext`, `LoopbackGate` | the Godot session host, the headless host, the retail screens' offline host, tests |
| `Lifestone.Client.Session.Credentials` (`Session/Credentials/`) | the backend's files as the hosts read them: `PlayAccounts` (secrets.json), `BackendReadiness` (pids.json), `ServerRuntimePaths` | hosts, `SessionBootstrap` |
| `Lifestone.Client.Session.Recording` (`Session/Recording/`) | the `.lsrec` format (§14): `RecordingFormat`, `RecordingHeader`, `SessionRecordingReader` and its frame types, `GameStateDigest`, `SessionReplayer` and `ReplayResult` (public, §18, §19); the recorder, `ReplayAcSession` and `ReplayClock` (internal) | the session, the replayer, tests and tools |
| `Lifestone.Client.Session.StandIn` (`Session/StandIn/`) | `StandInSessions` and `StandInCharacter` (public): the stand-in data an offline session's reducer is fed (§16) | the retail screens' offline host (`--retail-ui=standin`), tests |
| `Lifestone.Client.Session.Tools` (`Session/Tools/`) | the player tools' helpers (public, §9): `PlaceRequests`, `AceLocText`, `PositionAgreement` | the tools panel (G7), the scenarios, tests |
| `Lifestone.Client.Session.Hosting` (`Session/Hosting/`) | the headless host's loop (public, §20): `HeadlessRunner`, `HeadlessRunOptions`, `HeadlessRunResult`, `DataOnlyAttachments`, `CountingLogSink` | the headless host, the Godot login checks (wave 2), tests |
| `Lifestone.Client.Session.Scenarios` (`Session/Scenarios/`) | scripted runs (public, §20, §24 to §27): `IScenario`, `ScenarioStatus`, `LoginScenario`, `HeadlessCharacter`, `RoutePlay` (with `RoutePlay.Exit.cs`), `RoutePlayHost` (the game's player as the walker), `HoltburgLapsScenario`, `AcademyExitScenario`, `PortalExit`, `SessionTargetWorld` (a server MoveTo's target lookup), `LoginCheckScenario` | the headless host, the game's login check, tests |
| `Lifestone.Client.Session.Bus`, `.State`, `.Actions` (`Session/Bus/`, `Session/State/`, `Session/Actions/`) | the implementation: the event bus, the per-object message gate, the reducer (§15) and its parking store, the Action API with its trackers and the movement channel (§17); later the scenarios | internal |

`SessionBootstrap` sits in `Session/SessionBootstrap.cs` because the architecture tests pin that file as the only
product caller of `AcSession.Create`; `GameSession` and `SessionOptions` sit beside it, since only hosts need them.

**Lifestone.Game never names Lifestone.Net** (DoD 13; `ArchitectureTests.Lifestone_Game_never_names_Lifestone_Net`).
The contract exposes some net types (the phase, the failure, the link counters, message records), and code in
Lifestone.Client may use them freely. A Godot script must not name them: when a Godot host needs a net type by name,
the state track adds a Client-side accessor instead.

## 2. Threading model

- **One game thread per session.** The host ticks the session on its game thread (Godot's main thread; the headless
  host's loop). The first `Tick` binds the session to that thread, as the network session binds to the thread that
  first pumps it; ticking, acting or subscribing from another thread then throws `InvalidOperationException`. A0 code
  takes no locks. Only `ILogSink` implementations are thread-safe.
- **Everything happens inside `GameSession.Tick(now)`**, in this order:
  1. the network session pumps for at most `SessionOptions.BudgetMs` of dispatch time (its transport sweeps always
     run, even with the budget spent). Each message goes first to the raw tap (the bus's raw subscribers and the
     recorder), then to the reducer, which updates `GameState` and publishes its events;
  2. the Action API resolves the confirmations it saw this tick, expires timeouts, sends queued actions within their
     rate limits, and completes handles;
  3. the recorder flushes, at most once a second.
- **Events** are dispatched synchronously, in subscription order. `GameState` already reflects the whole message when
  a handler runs. An event published while a handler runs is queued and dispatched after the current dispatch, first
  in, first out (each event's handlers finish before the next event starts). Unsubscribing during a dispatch is safe:
  the handler is not called again, even later in the same dispatch; a handler subscribed during a dispatch first sees
  the next event. A handler that throws is logged as an ERROR once per subscription (category `session`, with the
  handler's method and the event type; later failures of the same subscription at DEBUG), counted, and the other
  handlers still run. The bus (ST3, `EventBus`) keeps one channel per event type behind a per-type index and
  copy-on-write handler arrays, so publishing allocates nothing; raw subscribers are a separate list, called at once
  (the bytes are valid only during the call), and with none the tap costs nothing. `SubscriptionSet` (in the
  contract) releases many subscriptions together, newest first, for a Godot node's `_ExitTree`.
- **Handles complete inside the tick**, after the tick's events: first `Outcome` and `Status`, then the `Completion`
  task, then the `Completed` event. The task is completed without forcing continuations onto another thread, so code
  that awaits a handle on the game thread resumes inline, inside that tick, under Godot's synchronization context or
  the headless host's game-thread context. A continuation may issue new actions; they queue like any other.
- **`GameThreadSynchronizationContext`** (ST1) is that context for hosts without one (the headless host, tests). It
  belongs to the thread that creates it. `Post` queues a callback from any thread; the host calls `RunPending` once
  per loop iteration after `Tick`, which runs the callbacks queued before the call, oldest first, with the context
  current (a callback posted meanwhile waits for the next call). `Install()` makes it current until disposed. A
  callback that throws ends `RunPending` with its exception (an `async void` failure is loud), and the callbacks not
  yet run stay queued in order. `Send` runs inline on the game thread and throws on any other (it would block).
- **The binding** (`GameThreadBinding`, internal): unbound until the session's first `Tick`, so a host may set up and
  subscribe from any thread before its loop; then subscribing, unsubscribing and publishing from another thread throw
  `InvalidOperationException` naming the member and both threads.
- **Between ticks** nothing changes. A host without a frame loop (the headless host, §20) waits for the next
  datagram or a short delay between ticks (`GameSession.WaitForInboundAsync`, over net's `IAcSession.WaitForInboundAsync`;
  it always waits the whole delay when nothing arrives, also before the login and offline), rather than spinning.

## 3. Clocks

| Clock | What it is | Who reads it |
|---|---|---|
| `SessionClock` (`IGameSessionView.Clock`) | monotonic seconds since the session was created, from a `TimeProvider` (`TimeProvider.System` in the hosts, a manual provider in tests and replays) | action timeouts and rate limits, handle times (`QueuedAt`, `SentAt`, `CompletedAt`), chat line times, the raw tap's times, recording times, `ServerTimeUpdated.ClientSeconds`. The network session is given the same `TimeProvider`, so `SessionClock.ToSeconds` converts its timestamps (`InboundContext.ReceivedTimestamp`, `SessionEvent.Timestamp`). |
| the frame time, `now` | the host's monotonic frame clock in seconds: the value it gives portal space, physics and the avatars this frame (game-core.md, the clock rule) | `GameSession.Tick(now)` stores it as `FrameTime`, and events that frame-stepped machines consume carry it: `LogOffRequested.Now` |

Neither is the speed-scaled world clock (`DerethTimeController.ClientClock`). No file under `Session/` reads the wall
clock, sleeps or uses a stopwatch, except `SessionClock.cs` (`ArchitectureTests`), so replays are deterministic.

## 4. Creating and running a session (hosts)

```csharp
// Live (the Godot session host, the headless host). The host reads the two files as bytes; no I/O in Client.
GameSession session = SessionBootstrap.CreateLive(options, secretsJsonBytes, pidsJsonBytes, dats, TimeProvider.System, log, recordingStreamOrNull);
ActionHandle login = session.Actions.Login();          // returns at once; completes at the character list
// Offline (the retail screens without a backend; ST7b, §16): stand-in data through the same reducer.
GameSession offline = GameSession.Offline(h => StandInSessions.DataCheckThenCharacterSelect(h, StandInSessions.DefaultDataCheckDuration), TimeProvider.System, log);
// Every frame, on the game thread:
session.Tick(frameNow);
// After a failure, from the disconnected screen (D43):
session.Reconnect();
// On exit: quits the network session if open, cancels pending handles, flushes the recorder, zeroes the credentials.
session.Dispose();
```

`SessionBootstrap.CreateLive` runs these checks, in this order, before anything touches the network. Each refusal logs
one WARNING (category `session`) naming the runner and the reason, then throws an `InvalidOperationException`
subtype; nothing after a refused gate runs. With every gate passed, `CreateLive` logs an INFO line (server, account
name, mod version, dat iterations), creates the network session (net's `AcSession.Create`: no socket until the login)
and returns the session idle, with a second INFO line (§18).

1. **Loopback** (`LoopbackGate`, ST2). The server must be a loopback IP literal. The gate logs the WARNING, then
   applies net's `LoopbackPolicy`, which throws `LoopbackRequiredException`. Host names are never resolved. No socket
   factory is ever invoked for a refused endpoint, and nothing after the gate runs (the S8 test, §10).
2. **Mod ready** (`BackendReadiness`, ST5). pids.json's `ace.modReady` must be the JSON literal `true`, or ACE's first
   account would be promoted to Admin (D11); `BackendNotReadyException` names the remedy. There is no escape hatch in
   the hosts. The gate reads the file as written: a stale pids.json of a stopped ACE passes it and the login then
   times out.
3. **Language dat.** `DatSet.RequireLocal` (an `InvalidOperationException` naming the file and the remedy): the data
   check reports client_local_English.dat's iteration. Its WARNING reads `language dat: SessionBootstrap.CreateLive
   refused: <reason>`.
4. **Account** (`PlayAccounts`, ST5). secrets.json must hold the chosen account (below); `PlayAccountException` names
   keys and usernames only.

**Accounts** (`PlayAccounts.Parse(secretsBytes, AccountChoice)`, owner decision D10). `SessionOptions.Account` null
means `AccountChoice.Default`; any other value is `AccountChoice.Named`. The headless host passes `test` when no
`--account` was given; the game passes nothing.

| Choice | Schema 2 (`schema: 2`, `defaultAccount`, `accounts.{play,test,<name>}`) | Schema 1 (`testAccount`, no `schema`) |
|---|---|---|
| `Default` | the key `defaultAccount` names (`play` when absent) | `testAccount` |
| `Play` | `accounts.play` | `testAccount` (migration makes it the play account) |
| `Test` | `accounts.test` | refused: schema 1 has no test account, and the tests never share the play account (ACE boots a second login of an account) |
| `Named(n)` | the key `n` (exact), else the account whose username is `n` (case-insensitive: ACE lower-cases names) | `testAccount` when `n` is `play` or its username |

The file is read twice with `Utf8JsonReader` (a byte order mark, comments and trailing commas are accepted): first
everything but the passwords (schema, default, keys and usernames), then the chosen account's password, copied with
`CopyString` into a pooled `char[]` that is zeroed before it goes back to the pool; net's `AcCredentials` keeps its
own zeroable copy. No password ever becomes a string. A malformed file is reported by line and byte, never with the
JSON reader's message (it can quote the file); an unknown `--account` lists the keys and usernames without repeating
the name given (a mistyped option could have been handed a password).

**Where the files are** (`ServerRuntimePaths.TryFind(environmentValue, searchRoots)`): the folder named by
`LIFESTONE_SERVER_RUNTIME` (absolute) when set, else the nearest checkout around the search roots whose
`server\runtime` holds secrets.json (`CheckoutLocator.FindServerRuntime`), so a worktree uses the main checkout's
backend. `SecretsFile` and `PidsFile` name the two files; the host reads them as bytes and zeroes the secrets bytes
after the call.

The data-check iterations come from `DatSet.Iterations` (Local becomes Language), never hard-coded. The session holds
the credentials until it is disposed, so `Reconnect` needs no secret from anyone; the network session's own copy is
zeroed at character select. The host zeroes its copy of the secrets bytes after the call. The recording stream, when
given, belongs to the host, which disposes it after the session.

`SessionOptions` (the Session option group, registered in `OptionCatalog.Session` for the main scene and the headless
host). `SessionOptions.Parse(args)` (or `FromArguments` for arguments already split) is the one parser both hosts
call; it ignores other groups' arguments, and each bad value adds a line to `Errors` and keeps the default. An
`--account` or `--character` value is never repeated in an error.

| Option | Property | Default |
|---|---|---|
| `--play` | `Play` | false (explore mode; D9) |
| `--account=NAME` | `Account` | null: secrets.json's `defaultAccount` (D10) |
| `--character=NAME` | `Character` | null: stop at the character list |
| `--server=IP:PORT` | `Server` | 127.0.0.1:9000 (server/versions.json, pinned by a test); an IP literal in canonical form on loopback (net's `LoopbackPolicy.TryParse`), port at most 65534 |
| `--record-session[=DIR]` | `RecordSession`, `RecordDirectory` | off (D48); the headless host always records; DIR defaults to `cache/sessions` |
| `--session-budget-ms=X` | `BudgetMs` | 2 (more than 0, at most 100) |
| `--retail-net` | `RetailNet` | false; true maps to net's `SessionTimeouts.Retail`, `LoginRetryPolicy.Retail`, no tail probe, arrival order (D18) |

No option takes a secret (`OptionCatalogTests.No_option_takes_a_secret`, `SessionOptionsTests`).

The headless host's own group (`OptionCatalog.Headless`, parsed by `src/Lifestone.Headless/HeadlessOptions.cs`, which
also reads the Session group and Core's `--help`, `--dat-dir`, `--log-level`, `--quit-after` and `--workers`):
`--scenario=login|holtburg-laps|academy-exit` (default `login`), `--laps=N` (default 3, 1 to 1,000),
`--create-character=NAME`, `--summary=PATH`. Arguments of no group, or of the game's scenes only, are reported with
their values hidden; a bad command line exits 1 before anything runs. The host itself is §20.

## 5. Events

Every event is a `readonly record struct` implementing `IGameEvent`, published after the state changed. Subscribe with
`session.Events.Subscribe<T>((in T e) => ...)`; the result is an `IDisposable`. `Events.SubscribeRaw` taps every
message in both directions as bytes (never a LoginRequest, so never a password); `Events.Count<T>()` counts
publications for the inspector.

| Event | Published when | Retail function re-expressed (11.4186 / 11.6096) |
|---|---|---|
| `SessionPhaseChanged(Previous, Phase, Failure)` | the session's phase changed; `Failure` set when Failed | |
| `ConnectionProgress(Stage, Attempt, Delay, Code, Detail)` | Connecting, Handshaking, Retrying, DataCheckStarted, DataCheckComplete | |
| `CharacterListChanged(Version, Reason)` | F658 replaced the list; a create appended locally; a delete was acknowledged; a restore cleared a pending deletion | |
| `CharacterSelected(Guid)` | `SelectCharacter` changed the selection | |
| `CharacterCreateResult(Result, Guid, Name, Restore)` | F643 answered a create or a restore | |
| `CharacterErrorReceived(Code)` | F659 | |
| `Booted(Reason, Banned)` | F7DC AccountBoot or F7C1 AccountBanned | |
| `PlayerCreated(Guid)` | F746 | |
| `ObjectCreated(Guid, IsSelf, InWorld, Recreated)` | a new object, a newer instance, or F7DB (`Recreated`: replace everything held for the guid; no `ObjectDeleted` precedes it) | `SmartBox::HandleCreateObject` 0x00454C80 / 0x00454D20; `HandleUpdateObject` 0x00455580 / 0x00455620 |
| `ObjectUpdated(Guid, Changes)` | a CreateObject with the same instance refreshed the descriptions | `HandleCreateObject` |
| `ObjectDeleted(Guid)` | F747 (never for the player: that is `MessageDropped(SelfDelete)`) | `SmartBox::HandleDeleteObject` 0x00451EA0 / 0x00451EE0 |
| `ObjectMoved(Guid, Position, Verdict, TeleportAdvanced, HasContact, Velocity?, Placement?)` | F748 accepted, with its verdict (below) | `SmartBox::HandleReceivedPosition` 0x00453FD0 / 0x00454070; `UnpackPositionEvent` 0x004542C0 / 0x00454360 |
| `ObjectMotionChanged(Guid, IsSelf, Autonomous, Motion)` | F74C accepted (or a create carried movement) | |
| `ObjectVectorChanged(Guid, Velocity, Omega)` | F74E accepted | `SmartBox::HandleVectorUpdate` 0x00453480 / 0x004534C0 |
| `ObjectStateChanged(Guid, IsSelf, Previous, State)` | F74B accepted and changed the state | `SmartBox::HandleSetState` 0x004533E0 / 0x00453420; `DoSetState` 0x004520D0 / 0x00452110 |
| `ObjectParented(Child, Parent, Location, Placement)` | F749 accepted | `SmartBox::HandleParentEvent` 0x004535D0 / 0x00453610 |
| `ObjectPickedUp(Guid)` | F74A accepted (the object stays, not in the world) | `SmartBox::HandlePickupEvent` 0x00453530 / 0x00453570 |
| `ObjectDescChanged(Guid, Model)` | F625 accepted | `SmartBox::HandleObjDescEvent` 0x00453340 / 0x00453380 |
| `ObjectSoundPlayed(Guid, SoundId, Volume)` | F750 for a known object (parked until the object exists) | `SmartBox::HandleSoundEvent` 0x00451FC0 / 0x00452000 |
| `ObjectScriptPlayed(Guid, ScriptType, Speed)` | F755 for a known object (parked until the object exists) | `SmartBox::HandlePlayScriptType` 0x00452070 / 0x004520B0 |
| `PlayerTeleportAnnounced(TeleportSequence)` | F751 accepted (not older) | `SmartBox::HandlePlayerTeleport` 0x00452150 / 0x00452190 |
| `SelfTravelChanged(Previous, Travel)` | the player's travel state changed (below) | |
| `ServerTimeUpdated(ServerSeconds, ClientSeconds)` | ConnectRequest or TimeSync | |
| `ChatReceived(Line)` | a chat line was added | |
| `ToolReplyReceived(RequestId, Verb, Ok, ErrorCode, Message)` | a `#lifestone/1` reply arrived (kept out of the chat) | |
| `ToolsUnavailable(Availability, Reason)` | the server answered `Unknown command: lifestone` (`ModMissing`), or a tool request met a server that is not loopback (`NotLoopback`, once per session); pending tool requests are refused | |
| `UseDone(WeenieError)` | GameEvent 0x01C7 | |
| `HealthUpdated(Guid, Health)` | GameEvent 0x01C0 | |
| `QualitiesChanged(Change, Key)` | PlayerDescription or a PrivateUpdate changed the player's qualities (D16) | |
| `LogOffRequested(Now, PlayerKillerDelay)` | `LogOff` sent F653; portal space plays the logoff transition | `CPlayerSystem::RequestLogOff` 0x00562DD0 / 0x00563B70 |
| `MessageDropped(Opcode, Guid, Reason)` | a message was not applied: older instance, stale, stale teleport, parking full or expired, a delete of the player, malformed | |
| `UnknownMessage(Opcode, EventType)` | a message or game event net does not decode | |

**Sequences.** The reducer is the one owner of SmartBox semantics (m2-plan §1.4; §15). It compares 16-bit sequences
with `Lifestone.Data.World.Sequence16`, which re-expresses `CPhysicsObj::is_newer` (0x00451AD0 / 0x00451B10) and
`newer_event` (0x00451B10 / 0x00451B50). Messages for an object that does not exist yet (or for a newer instance of
it) are parked and applied, in arrival order, right after its `ObjectCreated` (`SmartBox::ProcessObjectNetBlobs`
0x00454B20 / 0x00454BC0); every SmartBox message that arrives before F746 PlayerCreate waits for it the same way
(`ACSmartBox::IsReadyToDispatchEvent` 0x00559590 / 0x0055A1D0). The bounds are a Lifestone choice: 32 per object,
4,096 in all, 60 s; a message that does not fit is `MessageDropped(ParkingFull)`, an expired one
`MessageDropped(ParkingExpired)`.

**The per-object gate** (`ObjectMessageGate`, ST4, internal) is the head retail's handlers share
(`SmartBox::HandleSetState`, `HandleObjDescEvent`, `HandleVectorUpdate`, `HandlePickupEvent`, `HandleParentEvent`,
`UnpackPositionEvent`, `HandleDeleteObject`; addresses in docs/third-party/session.md). Its verdicts keep retail's
result numbers:

| Verdict | When |
|---|---|
| `Apply` (1) | the object exists and the message's instance stamp equals the object's; each field then changes only when its own stamp is newer (`newer_event`, `TryAdvance`): state for SetState, vector for VectorUpdate, position for PickupEvent and the child of a ParentEvent, visual description for ObjDescEvent |
| `Drop` (2) | the message's instance stamp is older: `MessageDropped(OlderInstance)` |
| `IgnoredSelf` (3) | a delete of the player: `MessageDropped(SelfDelete)` |
| `Park` (4) | the object is unknown, or the message's instance is newer (its CreateObject comes first) |

A ParentEvent gates the parent with its (parent) instance stamp, then parks under the child when the child is
unknown, before comparing the instances for equality, as retail does. PlayerTeleport is the exception to the field
rule: retail accepts a teleport stamp that is not older and does not store it.

**Position verdicts** (`PositionVerdict`). The reducer only classifies; physics acts on the player's verdicts
(`ServerPositionPolicy`) and the avatars on `Remote`.

| Verdict | When |
|---|---|
| `ForceBlip` | the player; the force-position sequence is newer (it is stored at once) and the teleport sequence is not older. The position sequence is set to the update's outright, and the teleport sequence is not stored. A server correction: it counts against DoD 8 |
| `SelfTeleport` | the player; the position sequence is newer and so is the teleport sequence (both stored) |
| `SelfEcho` | the player; the position sequence is newer, the teleport sequence unchanged: ACE echoing an accepted move |
| `Remote` | any other object with a newer position sequence; `TeleportAdvanced` says whether its teleport sequence moved (the `newer_event` in `CPhysicsObj::MoveOrTeleport`, 0x00516330 / 0x00516E30) |
| (dropped) | a newer position sequence with an older teleport sequence (`StaleTeleport`: the position sequence is put back; a force-position sequence stored on the way stays), or no newer position sequence (`Stale`; with a newer teleport sequence it also raises retail's error count, `ReducerCounters.RetailErrorCount`) |

An applied position also takes the object off its parent. A CreateObject for an object already known with the same
instance runs the same classification (with contact) when it carries a position.

**Self travel** (`SelfTravel`, the A0 view of retail's SmartBox teleport flags; portal's `TeleportProgress` keeps
retail's own): `NotInWorld` → `Arriving` (the player's CreateObject with a position) → `Materialising` (LoginComplete
sent) → `Present` (a SetState cleared Hidden). A teleport goes `Present` → `TeleportAnnounced` (F751) →
`TeleportArrived` (a `SelfTeleport` position) → `Materialising` → `Present`. Logoff returns to `NotInWorld`. A
SetState without Hidden ends an arrival or a teleport from any of `Arriving`, `TeleportAnnounced`, `TeleportArrived` and
`Materialising`, as it clears retail's `waiting_for_teleport` (`SmartBox::DoSetState`). A re-created player (F7DB) that
is not hidden keeps `Present`.

## 6. The read model

`IGameSessionView.State` is a `GameState`: the server-confirmed state, changed only by the reducer (A0's R3).

| Member | Holds |
|---|---|
| `Phase`, `Failure` | net's `SessionPhase` and `SessionFailure` |
| `IsOffline` | true for a stand-in session |
| `DataCheck` | NotStarted, Checking, Complete, Mismatch |
| `WorldName` | F7E1's world name |
| `Characters` | `CharacterListState`: `Entries` (`CharacterSlot(Guid, Name, DeleteTimeout)` with `PendingDeletion`) in server order, `IndexOf`, `SlotCount`, `SelectedGuid`, `Version` |
| `Self` | `SelfState`: `Guid`, `Name`, `Travel`, `Position` (the server's last view), `Stamps`, `PhysicsState`, `Hidden`, `Sequences` (what movement messages echo), `Qualities` (`PlayerQualities`: properties, attributes, vitals and skills by id) |
| `Objects` | `WorldObjectTable`: `TryGet`, `Contains`, `Count`, `All` (struct enumerator), `Within(centre, metres, into)` |
| `Chat` | `ChatLog`: the last 500 `ChatLine(Time, Kind, ChatType, Sender, SenderGuid, Text)`; tool-reply JSON never appears; a `WeenieError` line carries the WeenieError code in `ChatType` (its text is empty when the server sent none: the retail UI looks it up) |
| `Time` | `ServerTimeState`: the server's time at a session-clock time |
| `Version` | rises with every change |

A `WorldObject` holds the last CreateObject for its instance (`Created`: net's `ModelData`, `PhysicsDesc` and
`WeenieDesc`, with `Weenie`, `Physics` and `Name` shortcuts) and its current `Model`, `State`, `Position`, `Parent`
(`ObjectAttachment`), `InWorld`, `Velocity`, `Omega`, `Motion` (net's `ServerMotionState`, raw u16 commands),
`MotionAutonomous`, `Stamps`, `Health` and `CreatedAt`. Read it; never keep it across a reconnect.

## 7. Actions

`IGameSessionView.Actions` is the one way to act (A0's R2): the retail UI, the portal bridge, the tools panel, the
scenarios and the automation engines all use it. Each call returns an `ActionHandle` at once and never blocks or
throws for a rejected request (built by ST9 to ST11; the implementation is §17).

| Action | Sends (retail sender, 11.4186 / 11.6096) | Completes on | Refused when | Timeout |
|---|---|---|---|---|
| `Login` | LoginRequest and the handshake (net), with the credentials the session holds | the character list in, the data check complete and the session at character select | not Idle | 30 s |
| `SelectCharacter` | nothing (`Local`, at once, through notice 1) | at once | unknown character | |
| `CreateCharacter` | F656 in the next free slot | F643 Ok, else `ServerError(CharGenResult)` | the intent does not validate (at once); not at the character list; every slot taken | 15 s |
| `DeleteCharacter` | F655 with the server index (found and range-checked here) | the F655 ack and the F658 after it; a character error is a `ServerError` | not at the character list; unknown or pending character | 10 s |
| `RestoreCharacter` | F7D9 | F643 for that restore | not at the character list; not pending deletion | 10 s |
| `EnterWorld` | F7C8, then F657 on F7DF (net) | F746 and the player's CreateObject; a character error is a `ServerError` | not at the character list; unknown; pending deletion (retail ghosts Enter) | 30 s |
| `LoginComplete` | GameAction 0x00A1 (`CPlayerSystem::SendLoginCompleteNotification` 0x00562E90 / 0x00563C30, `CM_Character::Event_LoginCompleteNotification` 0x006A1480 / 0x006A22A0) | Hidden cleared (`Present`) | not in the world; not `Arriving` or `TeleportArrived`; a second call while the same arrival materialises (an ERROR) | 15 s |
| `Use` | GameAction 0x0036 (`CM_Inventory::Event_UseEvent` 0x006AC3B0 / 0x006AD310) | UseDone (the oldest use in flight): 0 confirms, other codes are `ServerError`; a teleport it started is in the detail | object 0 (at once); not in the world | 20 s |
| `QueryHealth` | GameAction 0x01BF (`CM_Combat::Event_QueryHealth` 0x006A9B40 / 0x006AAAA0) | UpdateHealth for the object; object 0 (deselect) is `Sent` | not in the world | 5 s |
| `Talk` | GameAction 0x0015 (`CM_Communication::Event_Talk` 0x006A45C0 / 0x006A53E0) | the player's own HearSpeech echo; ACE's gag line is a `ServerError`; a line starting `@` (a server command) is `Sent` | empty text (at once); not in the world | 5 s |
| `Tell` | GameAction 0x005D (`CM_Communication::Event_TalkDirectByName` 0x006A4780 / 0x006A55A0) | the OutgoingTell line (or the incoming tell, to oneself); WeenieError CharacterNotAvailable (0x052B) and the gag line are `ServerError` | no target or no text (at once); not in the world | 5 s |
| `LogOff` | "Logging off..." to the chat (notice 3), F653 (the only sender), then `LogOffRequested(frame time, false)` (`CPlayerSystem::RequestLogOff` 0x00562DD0 / 0x00563B70) | the server's F653 and F658, the session back at character select | not in the world | 30 s |
| `Quit` | F653 then a cleartext Disconnect in the world, else the Disconnect only (net) | `Sent` at once (ACE never answers) | Idle, already closing, already failed | |

**A known gap.** ACE does not resend F658 after a create, so the reducer appends the new character locally (§15), while
net range-checks a delete's index against the last F658 it received. Deleting a character created in this session is
therefore refused, with net's own out-of-range message, until the next F658 (the next login or logoff);
TODO(state): ask net to count the characters it created, or refuse it here with that reason.

**Lanes and limits** (owner decision D49: Lifestone's documented defaults; retail has none). Session-phase actions
(login, the character operations, entering the world, logoff) run one in flight at a time, in call order. Talk and Tell
share 4 a second with bursts of 8; Use 4 a second; QueryHealth 4 a second (added here: the state plan did not list it);
the player tools 2 a second; automation's movement intents 20 a second (added here). An action over its limit waits in
its lane, in order; nothing is dropped. LoginComplete and Quit wait for nothing: a quit goes before everything queued
and cancels every other action waiting or in flight. Timeouts run from the send, on the session clock.

**When a call is made** these checks run first, in order, and complete the handle there and then: the kill switch
(automation), the arguments, the loopback gate (automation and the player tools, live sessions: one WARNING naming the
action, §10), dry run (automation), and an offline session's refusal of every server action. Otherwise the action
waits in its lane, and its precondition (the phase, the list, the travel state) is checked again when its turn comes,
since the state may have changed meanwhile.

**Outcomes** (`ActionOutcome(Kind, Code, Detail)`; `Succeeded` is true for the first three): `Confirmed`, `Local`,
`Sent`, `ServerError` (the server's code in `Code`), `TimedOut`, `Refused` (why, in `Detail`), `NotImplemented` (a loud
stub, logged as an ERROR: an unbound movement intent, or a net operation that throws its own `TODO(net)`), `Cancelled`
(the kill switch, a quit, a reconnect, the session ended, or the session failed under an action that was not its own),
`DryRun`. A session failure ends the session-phase action in flight with the failure: the network session's timeouts
and silence are `TimedOut`; a boot, a ban, a data mismatch, a character error after the retries and the transport's
failures are `ServerError` with the failure's code.

**Sources and safety** (A0's R1 and R6). `ActionSource` is `Host`, `Ui`, `Automation`, `Client` (the client's own
behaviour: LoginComplete, the tools' status probe) or `Test`. `KillSwitch` refuses new automation actions, cancels the
queued ones, and releases every movement command, hold and autorun that automation left on through `Move`, so the player
stops; it logs a WARNING. `DryRun` logs automation actions with what they would have sent and completes them with
`DryRun`. Neither touches other sources. Automation runs only against a loopback server; an offline session refuses
every server action.

**The action log** (`IGameSessionView.ActionLog`) keeps the last 1,024 completions as
`ActionLogEntry(Id, Kind, Source, Summary, Outcome, QueuedAt, CompletedAt)`, and each completion also writes one
`session` log line (`action #7 EnterWorld (Ui) Confirmed: EnterWorld guid=0x50000001`, with the source and the
milliseconds as fields): an ERROR for `NotImplemented`, a WARNING for `TimedOut`, INFO otherwise. `Summary` describes
the arguments without secrets or typed text (`Talk chars=12`, `EnterWorld guid=0x50000001`, `ToolPlace Holtburg #a7`).

**Movement.** `Actions.Movement` is the `IMovementSink` that physics' `PlayerMovementController` writes MoveToState,
AutonomousPosition and Jump to, on retail's cadence (`CommandInterpreter::SendMovementEvent`, `ShouldSendPositionEvent`
and `SendPositionEvent`: 0x006B4680 / 0x006B55C0, 0x006B45E0 / 0x006B5520, 0x006B4770 / 0x006B56B0). The sink stamps
each send with `Self.Sequences` and hands it to the network session at once (`CM_Movement::Event_MoveToState`,
`Event_AutonomousPosition`, `Event_Jump`: 0x006AFC00 / 0x006B0AD0, 0x006AF790 / 0x006B0660, 0x006AFA70 / 0x006B0940):
no handle, no allocation (a MoveToState carrying queued actions copies them once). A send while the player is not
`Present` still goes out (ACE ignores it until it has handled LoginComplete) and is counted, with a WARNING once per
arrival; with no network session or outside the world nothing is sent, and that is counted, with a WARNING once per
arrival too. `Actions.Move` (`MoveIntents`) is the automation path into the same controller: `Command`, `HoldRun`,
`HoldSidestep`, `ToggleAutoRun`, `ChargeJump`, `Jump`, each with a handle that completes `Local` in the next tick, when
the controller took it (the jump bar gets the tick's frame time). The host binds the controller with `Move.Bind`
(added: m2-plan §4.7 left the binding to ST11); with none bound an intent completes as `NotImplemented` with an ERROR.
Intents need no server, so an offline session's controller takes them too.

## 8. Character creation

The CharGen screens produce a `CharacterCreateIntent(Heritage, Gender, Appearance, TemplateOption, Strength,
Endurance, Coordination, Quickness, Focus, Self, ClassId, SkillAdvancementClasses, Name, StartArea)`, with
`CharacterAppearance` holding the 14 choices and 6 shades in wire order. `TryValidate` checks what the session checks:
exactly **55** skill advancement classes (ACE ends the session for any other count) and a non-empty printable-ASCII
name. The session maps it field for field onto net's `CharacterCreateRequest`, adding the slot; net adds the account
string from F658 and writes the privilege words as 0.

## 9. Player tools

`IGameSessionView.Tools` (`PlayerTools`, ST12) gives a Player account the server mod's conveniences (DoD 10):
`Status`, `Place(TeleportDestination)`, `WhereAmI`, `AddXp`, the quest verbs `StampQuest`, `EraseQuest` and `HasQuest`, and
`PurgeCharacter(name)`, each returning a `ToolHandle<T>` (an `ActionHandle` with a typed `Result`: `ToolStatus`, `PlaceResult`,
`WhereResult`, `XpResult`, `QuestResult`, `CharPurgeResult`), plus `Availability` and `LastStatus`.

- **The wire is the mod's own.** A request is the Talk line `ToolsWire.Request(...)` writes (`@lifestone <verb> ...
  #a<handle id>`), and the reducer reads the F7E0 reply with `ToolsWire.ParseReply` (server/mod/Shared, compiled into
  Lifestone.Client; checkpoint C-mod). A reply completes the request with the same id: `ok` is `Confirmed` with the
  typed result (the reply's human line in `Detail`), an error is `ServerError` with `<code>: <sentence>` in `Detail`
  (`not-found: ...`; `Code` is 0, since the mod's codes are words), data without its verb's shape is `ServerError`
  too. The timeout is 5 s and the lane's rate 2 a second.
- **Places** (`PlaceRequests`). An ACE row of the teleport catalogue (an entry of the embedded ACE extract with the
  destination's name at its place, so also the rows a curated interior was merged into: the Town Network, the
  Marketplace) goes by its first ACE name, `place poi "<name>"`, so ACE teleports to its own Destination. Every other
  destination (the curated dungeons and building interiors, the Training Academy) goes by `place loc <cell> <x> <y> <z>
  [<qw> <qx> <qy> <qz>]`, with a rotation about Z built from its heading, written invariantly by ToolsWire. A request
  ToolsWire would refuse is refused at once with its sentence.
- **Quests** (`ActionKind.ToolQuest`; wire: `@lifestone quest stamp|erase|has <name>`). One character, always the caller's
  own: there is no target. `StampQuest(name)` is ACE's `QuestManager.Stamp` (what a quest script's StampQuest emote does:
  the registry row is added with one completion, or gains one, unless the quest's world definition is already at its
  maximum), `EraseQuest(name)` removes the row (a quest the character did not hold is answered, not an error), and
  `HasQuest(name)` only reads. The `QuestResult` carries the registry row before and after (`HeldBefore`,
  `CompletionsBefore`, `Held`, `Completions`; `Changed`; a completion count is 0 without a row, and a row can hold 0). A
  name is 1 to 64 of letters, digits, `_`, `-`, `(` and `)` (no `@`, no space, no quote, no `*`): anything else is refused
  at once with the wire's sentence and never sent. The mod refuses a stamp or an erase while the player is logging out
  (`busy`); `has` is never refused. A gate that tests a quest wants more than the row: a portal's `QuestRestriction` needs it
  held and not solvable again, and `SentryTaskComplete` (the Academy's "Exit to Holtburg", section 25) allows one solve, so
  one stamp opens it and an erase closes it again (the portal's own emote set also erases it after the teleport, section 25). The mod must be built and installed with the verb (server\setup-server.ps1
  -ModOnly from the main checkout; an older mod answers `unknown-verb`, and `ToolStatus.Verbs` lists `quest stamp`,
  `quest erase` and `quest has` when it has them). The loopback tests' helper is `LiveQuests` (stamp, erase, has, and
  `PutBackAsFound`, which returns the registry to what an exit scenario found). The one live test of an erase of a held quest is
  `AcademyExitLoopbackTests.An_erase_of_a_held_quest_and_the_stamp_before_it_are_saved_across_a_relog`: stamp, a fresh login still holds it, erase
  (`heldBefore` true), a second fresh login holds nothing. The offline stand-in does not answer quests (no tool but `place` is
  answered offline).
- **Character purge** (`ActionKind.ToolCharPurge`; wire: `@lifestone char purge "<name>"`; owner decision D59). `PurgeCharacter(name)` asks the mod to remove
  a character that is already marked for deletion from the server's database, so a live test leaves the test account as it found it. The
  caller must be in the world as a different character from the one purged (a pending-deletion character cannot enter), and the character must be
  on the caller's own account. The result, `CharPurgeResult(Name, Characters, PlayerBiotas, Possessions)`, is ACE's three counts. The mod refuses with
  `ServerError` and `Detail` `<code>: <sentence>`: `graduated` (`Lifestone Smoke` and `Lifestone Loopback`, any case), `own-character`,
  `unknown-character` (no such character on the account), `character-online`, `not-pending-deletion`; a name outside 1 to 32 of letters, digits, space,
  apostrophe and hyphen is refused at once and never sent. Installing a mod with the verb is the owner's go-ahead (`server\setup-server.ps1 -ModOnly`); an older
  mod answers `unknown-verb`. Offline, the stand-in answers it over its own character list with the same rules, codes and sentences
  (`StandInCharPurge`; the mod's `CharPurgeRulesTests` runs one table through both): a pending-deletion stand-in character is dropped from the list (and from the
  list after the next log off), with placeholder counts (1, 1, 0). The live test is `ModCharPurgeLiveTests`.
- **The trail** (DoD 10). Every use adds its own chat line when it is sent (`Lifestone: teleporting to Holtburg...`,
  `Lifestone: asking the server where you are...`, `Lifestone: asking for 1,000 experience...`, `Lifestone: asking the
  server to stamp quest SentryTaskComplete...`), then the reply's human
  line follows as a `ToolReply` chat line, and the completion is in the action log (`ToolPlace Holtburg #a7`).
- **Availability**, in this order: `NotLoopback` for a server that is not loopback (every request refused, with one
  WARNING naming it, and `ToolsUnavailable(NotLoopback)` once); `NotInWorld` outside the world (always, offline);
  `ModMissing` once the server answered `Unknown command: lifestone` (pending requests are refused); `Ready` once the mod
  answered anything; `Unknown` before that. Requests go out while it is `Unknown` or `Ready`.
- **The status probe.** When the player's CreateObject arrives (`NotInWorld` to `Arriving`), the session asks the mod
  for its status, silently (`ActionSource.Client`; notice 6 keeps the reply out of the chat). An access level other
  than `Player` (`PlayerTools.PlayerAccess`), or ACE's first-account auto-promotion on, is logged as an ERROR and
  engages the kill switch at the next tick, which stops automation (owner decision D11).
- **Where am I** (`PositionAgreement`). `DistanceMetres(a, b)` is the global distance (landblock times 192 plus the
  local origin, for every cell kind, in three dimensions), `Agrees` checks DoD 8's 0.5 m, and `Describe` gives the
  panel's line. `AceLocText` writes a position as ACE prints it (`0xA9B40019 [84.500000 7.100000 94.000000] 0.996917
  0.000000 0.000000 -0.078459`, ACE's `Position.ToLOCString`) and reads every form `@teleloc` accepts; both invariant.

## 10. Security

- **Credentials** exist only as net's `AcCredentials` (a private `char[]`, `ToString` redacted), built from the secrets
  bytes by `SessionBootstrap` and zeroed when the session is disposed. No contract type has a member that could carry
  a password (`SessionContractTests.No_contract_type_can_carry_a_secret`). No option takes one. Logs, events, handle
  summaries, the action log and exception messages never contain one (ST13's sentinel-password tests).
- **The raw tap and recordings** see game messages only. LoginRequest is a packet-level section and never reaches
  them; the recorder keeps the bodies of an allowlist of client-to-server opcodes only (§14), and the recording header
  holds no account, user, machine or path.
- **Loopback first**: the gate runs before any socket exists. The S8 test (`S8LoopbackRunnerTests`) runs every runner
  against refused endpoints (TEST-NET-1, both wildcards, an IPv4-mapped public address, two private addresses,
  broadcast, multicast; host names are refused by the gate's own tests) and asserts the refusal, one WARNING naming
  the runner, a socket-factory spy never invoked and nothing past the gate run. Its rows so far:
  `SessionBootstrap.CreateLive`, the headless host's `--server` at parse time (`HeadlessOptionsTests`), and, refusing
  through their handle with nothing sent and no movement controller called, automation's actions and movement intents
  (`IActionApi.<kind> (automation)`) and the player tools (`PlayerTools.<verb>`, any source); and the headless runner
  (`HeadlessRunner`, whose own gate runs before `SessionBootstrap`'s). The scenarios (ST16) add theirs. Past the gate,
  for a loopback endpoint, the session is created idle and no socket factory is invoked either.
- **Mod-ready gate**: no live session without `ace.modReady` (D11). The headless host checks it twice: when it reads
  pids.json (before the dats are opened) and in `HeadlessRunner.Start`.
- **The headless verdict line** holds the frozen keys only (§20): no account, character, secret or failure text. A
  gate's refusal message (which may list account keys and usernames) goes to the log; the summary's failure is a fixed
  phrase chosen by the exception's type (`HeadlessRunResult.StartFailure`).
- **Password hygiene, tested** (`PasswordHygieneTests`, ST13): with a sentinel password in secrets.json, a whole live
  session (login, entering the world, LoginComplete, the tools, a chat line, logoff, quit, dispose) leaves the sentinel
  in none of: the recording (as ASCII, UTF-8, UTF-16LE or a LoginRequest's String32L), the log, the action log, the text
  of every published event and handle, and the gates' exceptions. The session's credentials are all zeros once it is
  disposed. The network session's own copy is zeroed at character select by net (Lifestone.Net.Tests
  `SentinelPasswordTests`). The loopback suite repeats the recording check against the bundled ACE with the real test
  password, counted in memory and never printed (`HeadlessLoopbackTests`).
- **The boundary** (`SessionBoundaryTests`, DoD 13): the network session's types (`IAcSession`, `AcSession`,
  `IAcSessionSink`, `IRawMessageTap`, `InboundDispatcher`, `AcCredentials`, `AcSessionOptions`) are named only in
  `Client/Session/` and, in the game, in `scripts/Session/`; the session's internals (`SessionBootstrap`, the reducer,
  the Action API, the bus) only there too; the headless host goes through `HeadlessRunner` and `SessionBootstrap`.

## 11. Adapting to the network session (C-net)

The reducer implements net's `IAcSessionSink`. The session (`IAcSession`) does the protocol work itself: it answers the
data check, echoes the account string in character requests, retries logins (D18) and keeps its own phase. The
reducer follows it:

| Net | Session layer |
|---|---|
| `OnSessionEvent(PhaseChanged)` | `GameState.Phase`, `SessionPhaseChanged`; entering Connecting gives `ConnectionProgress(Connecting)` (and restarts the data check), entering Handshaking `ConnectionProgress(Handshaking)`, entering DataCheck the data check's start |
| `OnSessionEvent(ConnectRequestReceived)` | `GameState.Time`, `ServerTimeUpdated` |
| `OnSessionEvent(DataCheckStarted, DataCheckComplete)`, `OnDddInterrogation`, `OnDddEnd` | `DataCheck`, and `ConnectionProgress(DataCheckStarted / DataCheckComplete)` once, whichever reports it first |
| `OnSessionEvent(RetryScheduled)` | `ConnectionProgress(Retrying)` with the attempt, delay, code and reason |
| `OnSessionEvent(Booted)`, `OnAccountBoot`, `OnAccountBanned` | `Booted`: net reports a boot both as the message and as its own event, in an order its contract leaves open, so the two are paired and published once |
| `OnSessionEvent(CharacterErrorReceived)`, `OnCharacterError` | `CharacterErrorReceived`, paired the same way |
| `OnSessionEvent(ServerTime)` | `GameState.Time`, `ServerTimeUpdated` |
| `OnSessionEvent(LoggedOff)`, `OnCharacterLogOff` | the logoff completes; `SelfTravel.NotInWorld` |
| `OnSessionEvent(Failed)` | `Phase` Failed, `Failure`, `SessionPhaseChanged` |
| `OnSessionEvent(Warning)` | a WARNING line (category `session`); malformed messages also `MessageDropped(Malformed)` |
| `OnCharacterList`, `OnServerName`, `OnDddBegin`, `OnDddEnd`, `OnCharGenResponse`, `OnCharacterDeleteAck`, `OnPlayerCreate` | §5's login events and §6's state |
| the world callbacks (`OnCreateObject` ... `OnPlayEffect`) | §5's object events, gated by sequence (`OnSound`, `OnPlayEffect`: `ObjectSoundPlayed`, `ObjectScriptPlayed`) |
| `OnSpeech`, `OnEmote`, `OnServerMessage`, `OnTell`, `OnPopup`, `OnWeenieError`, `OnTransientString` | `ChatLog`, `ChatReceived`; tool replies split out |
| `OnUseDone`, `OnUpdateHealth`, `OnPlayerDescription`, `OnPrivateUpdate` | `UseDone`, `HealthUpdated`, `QualitiesChanged` |
| `OnUnknown`, `OnGameEvent` | `UnknownMessage` |
| `OnMalformed` | `MessageDropped(Malformed)` |
| `IRawMessageTap.OnMessage` | the bus's raw subscribers and the recorder, with the time converted by `SessionClock.ToSeconds` |
| `INetLog` | an adapter onto `ILogSink`, category `net` |

`InboundDispatcher.Dispatch` is the one decode path for live play and replay: the replayer (ST14) feeds recorded
bodies through it into the same reducer.

## 12. Steps

| Step | Delivers | Status |
|---|---|---|
| ST0 | this contract (checkpoint C-state) | done |
| ST1 | `GameThreadSynchronizationContext`, `GameThreadBinding`, the clock tests, `NoAmbientTimeTests` | done |
| ST2 | the loopback gate wrapper and the S8 test (rows grow with the runners) | done |
| ST3 | the event bus with the raw tap; `SubscriptionSet` | done |
| ST4 | the per-object message gate on `Sequence16` | done |
| ST5 | credentials (`PlayAccounts`), server-runtime paths, backend readiness, the Session and Headless option parsers; `CreateLive`'s gates | done |
| ST6 | the `.lsrec` recording writer and reader | done |
| ST7 | the reducer, login phase (§15) | done |
| ST7b | `GameSession.Offline` and `StandInSessions` (checkpoint C-state-offline; §16) | done |
| ST8 | the reducer, world phase (SmartBox semantics; §15) | done |
| ST9 | the Action API core: handles, lanes, rate limits, kill switch, dry run, action log (§7, §17) | done |
| ST10 | the action trackers, including the intent-to-request mapper | done |
| ST11 | the movement channel and `MoveIntents` | done |
| ST12 | the player tools (§9); the Training Academy cell fix in `InteriorLocations` (0x860201AD) | done |
| ST13 | `GameSession`'s live mode and `SessionBootstrap.CreateLive`, reconnect, `GameStateDigest`, the boundary and password-hygiene tests (§18); `QualitiesWeenie` moved to ST16 and is built (§24) | done |
| ST14 | `SessionReplayer`, `ReplayAcSession`, synthetic recordings, `[RequiresRecordingFact]` (§19) | done |
| ST15 | the headless host: `HeadlessRunner`, `LoginScenario`, `DataOnlyAttachments`, `HeadlessCharacter`, `Program`, the loopback tests (§20) | done |
| ST16 | the movement scenarios (after physics); `QualitiesWeenie` | part 1 done (b2): `QualitiesWeenie`, `RoutePlay`, `HoltburgLapsScenario`, the `SkillClasses` switch (§24); part 2 done (b3, 5b C): `AcademyExitScenario`, the portal search, Use and the server MoveTo, the exit live with the quest stamped for the Use (§25) |
| ST17 | docs (this page, docs/third-party/session.md) | done |

**`QualitiesWeenie` moved to ST16.** m2-plan lists it in ST13 as `QualitiesWeenie : IMotionWeenie`, the player's weenie
read from `Self.Qualities` (run and jump skills, burden, stamina). Its interface (`IMotionWeenie`, now with
`IsThePlayer`, `TryGetJumpVelocity` and `TryGetJumpStaminaCost`, and physics' `IPhysicsWeenie` above it) and the
formulas it must call (`MovementSystem.GetRunRate`, `GetJumpHeight`, `JumpStaminaCost`; `EncumbranceSystem`) are the
avatars' and physics' (on master since the wave-1 merges), not on this branch; writing them again here would make a
second copy. It is built with ST16, after the branch takes the physics and avatars merges (master 0adb3d9, d118581).
Until then nothing binds the player's run rate, jump and burden from the session: physics' PH7, G5 and G6 wait for it,
and ST13 stays partial.

## 13. Differences from the m2-plan §4.7 sketch

- `GameSession`, `SessionBootstrap` and `SessionOptions` are in `Lifestone.Client.Session`, not in `Contracts/` (§1).
- The events use net's types where net has one (`SessionPhase`, `SessionFailure`, `CharGenResult`, `PhysicsState`,
  `ServerMotionState`, `ModelData`), instead of copies.
- Added: the `CharacterSelected` event; `IEventBus.Count<T>`; `GameSession.FrameTime`; `ActionHandle.Summary` and
  `ActionStatus`; `ActionKind`; `ToolHandle<T>` derives from `ActionHandle`; `ToolAvailability` travels in
  `ToolsUnavailable`; `Booted.Banned`; `CharacterCreateResult.Restore`; `ObjectStateChanged.IsSelf`; the motion and the
  model in `ObjectMotionChanged` and `ObjectDescChanged`.
- `ObjectMoved.Velocity` and `Placement` are nullable (the update may carry neither).
- A re-create publishes one `ObjectCreated(Recreated: true)`, with no `ObjectDeleted` before it.
- `Tick(now)` takes the host's frame time; timeouts read the session clock (§3).
- The session keeps the credentials until it is disposed, so `Reconnect()` takes no secret.
- Added in ST1 to ST6 (all additive): `SubscriptionSet` in the contract; `GameThreadSynchronizationContext` and
  `LoopbackGate` beside `GameSession`; `SessionOptions.Errors`, `Parse`, `FromArguments` and the constants
  `MaxBudgetMs`, `MaxCharacterNameLength`, `DefaultRecordDirectory`; the `Credentials` and `Recording` namespaces.
  Internal, for tests and ST13: `SessionBootstrap.CreateLiveWith` (the dat check as a function and the socket
  factory), `SessionBootstrap.Runner`, `LanguageIterations` and `ToDddIterations`.
- Added in ST7 to ST8 (all additive): the events `ObjectSoundPlayed` and `ObjectScriptPlayed` (retail parks sounds and
  scripts for unknown objects like any SmartBox message, so they belong to the reducer); the `StandIn` namespace
  (`StandInSessions`, `StandInCharacter`); `GameSession.Reducer` (internal). `GameSession.Reconnect` on an offline
  session resets it and runs its stand-in again (the disconnected screen's button works offline too). The m2-plan
  sketch's `StandInSessions.CharacterSelect(h, params string[] names)` takes `StandInCharacter`s instead, so a stand-in
  can be pending deletion.
- Added in G2 U3 (additive): `StandInSessions.AnswerLocally(h, delay)` and `DefaultAnswerDelay`, the opt-in stand-in responder for
  `--retail-ui=standin`. An offline session that calls it answers `DeleteCharacter`, `RestoreCharacter` and `CreateCharacter` itself,
  0.6 s later on the session clock, through the same reducer: a synthetic F655 acknowledgement and F658 (the character pending deletion,
  ACE's 1), an F643 `Ok` for a restore, an F643 `Ok` or `NameInUse` for a create. Every other server action stays refused
  (`ActionOutcomeKind.Refused`), a session that did not call it refuses all of them as before, and a live session cannot have one
  (the call needs an offline reducer; the Action API reaches the responder only when it has no network session). Also additive in the
  contract: `GameState.ConnectionLost` and `FailureMessage` (the screens' view of a failed session) and `CharacterCreateResult.ResultCode` and
  `Succeeded`, so the retail UI names no network type.
- Corrected documentation only: `DropReason.StaleTeleport` and `Stale` (retail's error count belongs to a stale position
  with a newer teleport sequence, not to `StaleTeleport`), and `ChatLine.ChatType` for WeenieError lines.
- Added in ST9 to ST12 (all additive): `MoveIntents.Bind(IPlayerMovementIntents?)` (the sketch left the binding
  open); `PlayerTools.LastStatus` and `PlayerTools.PlayerAccess`; the `Lifestone.Client.Session.Tools` namespace
  (`PlaceRequests`, `AceLocText`, `PositionAgreement`). `ToolsUnavailable` is published for `ModMissing` (the reducer)
  and `NotLoopback` (the tools, once); leaving the world refuses pending tool requests without one. `Quit` has no
  timeout: it is `Sent` at once. QueryHealth and the movement intents have rate limits D49 did not list (4 and 20 a
  second).
- Added in ST13 to ST15 (all additive): `SessionBootstrap.CreateLive`'s optional last parameter `RecordingHeader?
  recordingHeader` (the sketch had no way to name the runner or the scenario of a recording); `GameSession.Reconnects`,
  `GameSession.WaitForInboundAsync` (the headless loop's wait; the sketch left the host's wait to net, which a Client
  host cannot name); the `Hosting` and `Scenarios` namespaces; `GameStateDigest`, `SessionReplayer` and `ReplayResult`
  in `Recording`. `GameSession.Link` is the current network session's counters (a reconnect starts a new one at zero).
  Local notice 7 (`SessionReset`) is new: a reconnect's reset, recorded so a replay resets too. `QualitiesWeenie` moved
  to ST16 (§12).
- Added in the ST13 to ST15 review fixes (additive): `GameSession.Corrections` (the player's force-position
  corrections, the verdict lines' `corrections=`); `IScenario.WhereMaxMetres` and `IScenario.Unmatched`;
  `HeadlessRunResult.Corrections`, `WhereMaxMetres`, `Unmatched`, `CouldNotStart` and `StartFailure`;
  `HeadlessCharacter.RetailSkillClasses`, its skill-class constants and `Intent(name, skillClasses)` (the one-argument
  `Intent`, which left every skill inactive, is gone); `LoginScenario`'s `skillClasses` parameter (required with
  `createCharacter`) and its `where` step, `Where` and `WhereMaxMetres`. Internal, for tests: `SessionBootstrap.CreateLiveWith`'s
  `credentialsObserver`.
- **The headless verdict line** follows m2-plan §4.14 exactly (`headless done scenario= ok= corrections= where_max_m=
  unmatched= recording=`). The ST15 draft printed `stage=`, `phases=`, `logincompletes=`, `errors=`, `warnings=`,
  `seconds=` and a `failure=` text instead; those now live in the log and the `--summary` JSON only. Remaining gaps:
  `where_max_m` is one check after arriving (the login scenario does not move), `none` when the run failed before it;
  `unmatched` is 0 because the login scenario sends no movement (physics' `MovementAudit` feeds it from ST16).

## 14. Recordings (`.lsrec`, ST6)

A recording holds what a replay needs to rebuild a session: every game message the raw tap saw, in both directions,
the network session's own events, and the session's local notices, each with its session-clock time (owner decision
D48: recorded only with `--record-session`; the headless host and the login checks always record). Hosts choose the
file: `RecordingFormat.FileName(createdUtc, runner)` gives `<UTC>-<runner>.lsrec` (for example
`20261012T142233Z-headless.lsrec`) under `cache/sessions` or `--record-session=DIR`, checked with
`OutputPaths.TryResolve` (never under `src/` or `tests/`). Recordings are never committed (D14).

Layout, version 1, little-endian:

| Part | Bytes |
|---|---|
| file start | magic `LSREC` CR LF Ctrl-Z (8), version u16 = 1, flags u16 = 0, header length u32 |
| header | UTF-8 JSON: `format` `lsrec`, `formatVersion`, `runner`, `createdUtc`, optional `lifestoneVersion`, `clientVersion` (`1802`), optional `aceCommit`, optional `iterations` {`portal`, `cell`, `local`, `highRes`}, optional `scenario` |
| frame | kind u8, flags u8, reserved u16, payload length u32, time f64 (session-clock seconds), payload |
| message payload (kind 1) | direction u8, reserved u8, queue u16, fragment sequence u32, opcode u32, length u32, body (flags bit 0: body omitted) |
| session event payload (kind 2) | net's `SessionEvent`: kind, phase, previous phase, warning (u8 each), code u32, attempt i32, delay ticks i64, server time f64, timestamp i64, failure present u8, failure kind u8, failure phase u8, reserved u8, failure code u32, text, failure message |
| notice payload (kind 3) | code u16, reserved u16, value u32, text (`RecordingNotice`; the codes are assigned by the steps that make local changes, ST7 to ST10) |
| trailer (kind 0xFF) | frame count u64, SHA-256 of every byte before the trailer (32), state digest present u8, state digest (32) |
| text | length u16 (0xFFFF for null), UTF-8 bytes (at most 4 KiB; longer text is cut at a character boundary) |

- **No secret.** The header's fields all have fixed shapes (tokens, versions, a hexadecimal commit, numbers), checked
  before writing, so no name or path can get in. A client-to-server message keeps its body only for the opcodes
  F7E6, F656, F655, F7D9, F7C8, F657, F653 and F7B1 (`RecordingFormat.KeepsClientBody`); any other is recorded as
  opcode and length only, so even a LoginRequest's bytes, were they ever tapped, would not be kept.
- **The recorder** (`SessionRecorder`, internal; the session creates it from the stream the host passed to
  `CreateLive`, with the host's header or a default one) writes the file's start and the header to the stream and
  flushes it at once, so a session that dies at any point leaves a readable recording; frames go through a 64 KiB
  buffer: the buffer goes to the stream when full, and the stream is flushed by the end-of-tick `FlushIfDue` at most
  once a second on the session clock, and when the recording completes. Its
  `Message` method has the raw tap's shape. Writing a frame allocates nothing. A failing stream is logged once as an
  ERROR and stops the recording, not the session; dropped frames are counted. The host owns and disposes the stream.
- **The reader** (`SessionRecordingReader`) reads frame by frame with one reused body buffer. A recording cut anywhere
  after its header reads to its last whole frame and reports `RecordingEnd.Truncated` (or `NoTrailer` when cut at a
  frame boundary), never an exception; it checks the trailer's frame count and stream hash (`RecordingTrailer.IsIntact`).
  Anything that is not a version-1 recording (a file cut inside its 16-byte start or its header included), a malformed
  frame or data after the trailer throws `InvalidDataException`.

## 15. The reducer (ST7, ST8)

`GameStateReducer` (internal, `Session/State/`) is the one writer of `GameState`. It implements net's `IAcSessionSink`,
so the live network session (ST13), a replay (ST14) and the stand-in data (§16) feed it the same decoded messages and
session events. For each one it **changes the state first and publishes after**: a handler always sees the whole
message applied, and `GameState.Version` has risen. Times (chat lines, creation times, parking) are the session clock's
`Now`, never the message's receive timestamp, so a replay that drives the clock reproduces them.

**Login phase** (`GameStateReducer.Login.cs`).

- F658 replaces the list in server order and keeps the deletion field raw (any non-zero value is pending deletion, D52).
  A selection the new list no longer holds is cleared (`CharacterSelected(0)`).
- F7E1 sets `WorldName` (no event of its own: F658 follows it).
- F643 answers a create or a restore. An `Ok` create appends the character (ACE does not resend F658); an `Ok` restore
  clears the pending deletion. A restore is recognised by the restore notice (below) or, without one, by an `Ok` whose
  guid is already listed. A NameInUse within 2 s of another, with no create sent in between, is folded into the first
  (ACE can answer one create twice; net folds the same window).
- The F655 acknowledgement publishes `CharacterListChanged(DeleteAcknowledged)`; the F658 that follows shows the
  pending deletion.
- F7E7 sets `DataCheck` to `Mismatch` and logs a WARNING; the failure itself arrives as net's `Failed` event.
- Net's `Failed` event sets `Phase` and `Failure` and logs a WARNING. When a phase change to Failed came first, without
  its failure, a second `SessionPhaseChanged(Failed, Failed, failure)` carries the reason.
- F746 is `SmartBox::HandleCreatePlayer` (0x00455110 / 0x004551B0): a second one is ignored with a WARNING; the first
  replays every SmartBox message held until then (`SmartBox::ProcessNetBlobs`, 0x00454A70 / 0x00454B10).
- The server's F653 and net's `LoggedOff` both leave the world, once: every object but the player gets
  `ObjectDeleted`, then the player `SelfTravelChanged(..., NotInWorld)`; the player's guid, name, position, stamps and
  qualities are forgotten, and the parked messages dropped.

**World phase** (`GameStateReducer.World.cs`): §5's tables, re-expressed from `SmartBox.c`, `ACSmartBox.c` and
`CPhysics.c` (11.4186; addresses in docs/third-party/session.md). Beyond §5:

- A CreateObject naming a parent the client does not know parks under the parent. An unknown object, F7DB, or a newer
  instance creates the object anew (`ObjectCreated`, with `Recreated` when one was replaced; a create that carries
  movement adds `ObjectMotionChanged`), then replays what was parked for it. The same instance refreshes it in
  retail's order, each part on its own sequence: the visual description; then the parent, or the position (§5's
  classification with contact), or a pickup when it has neither; then the movement, the state and the vectors. The
  events follow in that order after `ObjectUpdated`.
- UpdateMotion follows `CPhysics::SetObjectMovement` (0x00509690 / 0x0050A160): a newer movement sequence is stored;
  an older server-control sequence then leaves the motion unchanged (`MessageDropped(Stale)`; the movement sequence
  stays advanced); otherwise the server-control sequence is stored and the motion taken. The player's own motion is
  stored like any other with `IsSelf` and `Autonomous`; retail does not replay an autonomous echo of the player's own
  input, and the consumers follow it.
- VectorUpdate is recorded for the player too; retail applies the player's own velocity only when its command
  interpreter allows it, which is physics' decision.
- A message that cannot apply yet is parked (boxed: the rare path) in `PendingObjectMessages`. A message parked again
  during a replay (for a still newer instance) keeps its first parking time. `GameSession.Tick` expires parked messages
  once per tick, and every park does too.
- `ObjectSoundPlayed` and `ObjectScriptPlayed` change nothing in the state.
- For a known object, applying UpdatePosition, UpdateMotion and VectorUpdate and publishing their events allocates
  nothing (`ReducerAllocationTests`: 1,000 of each kind, the player and another object).

**Chat, tool replies and qualities** (`GameStateReducer.Chat.cs`).

- HearSpeech, HearRangedSpeech, EmoteText, SoulEmote, ServerMessage, Tell, PopupString, WeenieError,
  WeenieErrorWithString and CommunicationTransientString each add a `ChatLine` of their `ChatKind` and publish
  `ChatReceived`.
- An F7E0 whose second line starts `#lifestone/1 ` is a tool reply: its human line goes to the chat as a `ToolReply`
  line (unless its request id was marked silent), the JSON never does; `ToolReplyReceived` follows, and the player tools
  (ST12) receive the parsed reply and its human line through the internal `ToolReplyRouted` (`ToolReplyLine`). The
  reply is read with the server mod's own `ToolsWire.ParseReply` and `IsModMissing` (checkpoint C-mod). A reply of
  another wire version, or one that does not parse, logs a WARNING naming the problem and shows its human line only.
  `Unknown command: lifestone` adds its line and publishes `ToolsUnavailable(ModMissing)`.
- PlayerDescription replaces every quality (`QualitiesChanged(Description)`), the enchantment registry included (`PlayerQualities.Enchantments`, wire order; the vitals' maximums apply it, docs/retail-ui.md section 21.3; the messages that change it later are `TODO(g4-enchantment-updates)`). A PrivateUpdate applies when its 8-bit
  sequence is not older than the last one for the same quality (equal is accepted; a distance above 127 is a wrap), as
  retail's `ClientObjMaintSystem::UpdateStat` does through `WTimeStamper::UpdateTS` (0x006B2D70 / 0x006B3CB0). A
  vital's ranks and its current value share one sequence, as in retail. A refused update is `MessageDropped(Stale)`.
- UpdateHealth sets the object's `Health` when the object is known and always publishes `HealthUpdated`; UseDone
  publishes `UseDone`.
- A message or game event net does not decode publishes `UnknownMessage`, and its first sighting is logged (INFO);
  a malformed one is `MessageDropped(Malformed)` with a WARNING.

**Local notices.** Changes without a server message come in as `RecordingNotice`s through the internal
`GameStateReducer.ApplyLocal`, which records them when a recorder is attached (and records net's session events the
same way), so a replay reapplies them. Codes 1 to 15 belong to the reducer (`SessionNotices`); the Action API takes
16 and up (`ActionNotices`, applied by `ActionApi.ApplyRecordedNotice`); any other code throws `InvalidDataException`.

| Code | Notice | Effect |
|---|---|---|
| 1 | `CharacterSelected` (`Value`: guid, 0 for none) | `CharacterListState.SelectedGuid`, `CharacterSelected`; a guid not in the list is ignored with a WARNING |
| 2 | `LoginCompleteSent` | `Arriving` or `TeleportArrived` becomes `Materialising`; otherwise a WARNING and no change |
| 3 | `ClientChatLine` (`Text`) | a `Notice` chat line ("Logging off...") |
| 4 | `CharacterCreateSent` | the next F643 answers a create (no NameInUse folding for it) |
| 5 | `CharacterRestoreSent` (`Value`: guid) | the next F643 answers that restore |
| 6 | `ToolRequestSilent` (`Text`: request id) | that request's reply line stays out of the chat |
| 7 | `SessionReset` | the state goes back to empty before login (`GameSession.Reconnect`, §18) |
| 16 | `ActionNotices.LogOffRequested` (`Value`: 1 with the player-killer delay) | the Action API publishes `LogOffRequested` again (with the replay's frame time) |

Who sends them (ST10, ST12): `SelectCharacter` 1; `LoginComplete` 2; `LogOff` 3 ("Logging off...") and 16; every
non-silent tool request 3 (its chat line); `CreateCharacter` 4 with every F656; `RestoreCharacter` 5 with every F7D9;
the silent status probe 6.

**Counters** (`ReducerCounters`, internal): parked, replayed, refused by the bounds, expired, dropped, force blips,
and retail's position error count.

## 16. Offline sessions (ST7b, checkpoint C-state-offline)

`GameSession.Offline(standIn, time, log)` builds a session with no network session: `GameState.IsOffline` is true, the
stand-in callback is called at once with the session's reducer, and `Tick` runs the stand-in's timed steps on the
session clock (then expires parked messages). `Link` is all zero. `Reconnect` resets the state to Idle (subscribers
kept) and calls the stand-in again. `Dispose` drops the pending steps.

`StandInSessions` (public, `Session/StandIn/`) makes up the network session's events and the server's login messages
and feeds them to the reducer. Every name is Lifestone-authored; nothing comes from the dats, a server or a recording.

| Method | Feeds |
|---|---|
| `CharacterSelect(h, params StandInCharacter[])` | at once: Connecting, Handshaking, DataCheck, F7E5, F7EA, F7E1 (`Lifestone Stand-in`), F658, CharacterSelect |
| `DataCheckInProgress(h)` | at once: up to the data check (F7E5 answered), and no further |
| `DataCheckThenCharacterSelect(h, duration, params StandInCharacter[])` | the data check now, the rest `duration` later in `Tick` (`DefaultDataCheckDuration` is 2 s); needs an offline session's reducer |
| `Disconnected(h, failure)` | the login's phases up to `failure.Phase`, then `Failed(failure)`; `ServerStopped` and `NoServer` are stand-in failures a Godot host can pass without naming a net type |

Characters are `StandInCharacter(Name, PendingDeletion)`, with guids from 0x50000001 in list order; names are 1 to 32
printable ASCII characters. With none given, `DefaultCharacters` are "Stand-in Alpha", "Stand-in Beta" and
"Stand-in Gamma" (pending deletion). The handler must be a network-session sink (the reducer is); a plain
`IInboundMessageHandler` is refused with `ArgumentException`.

**Actions offline.** An offline session's Action API is the one of §7 and §17 without a network session. Every server
action (login, create, delete, restore, enter, LoginComplete, use, query health, talk, tell, logoff, quit) and every
player tool completes at once with `Refused` ("offline session: there is no server (stand-in data only)");
`SelectCharacter` works (`Local`, through notice 1) and refuses a character not in the list. Each completion is
written to the action log and one `session` log line. The kill switch and dry run apply to automation as documented.
`Actions.Movement` counts what it cannot send and logs the first as a WARNING; `Actions.Move` drives a bound controller
(an offline player) and fails loudly without one; `Tools.Availability` is `NotInWorld`.

## 17. The Action API (ST9 to ST12)

`ActionApi` (internal, `Session/Actions/`) implements `IActionApi`, and owns the movement channel, `MoveIntents` and
`PlayerTools`. The session creates it with its reducer, bus, action log and log, and, for a live session (ST13), an
`ActionLink`: the server endpoint, the current network session and a callback that logs in with the credentials the
session holds (no secret passes through the Action API). Without a link it is an offline session's (§16).

- **Trackers.** Each action is an `ActionTracker` (`Session/Actions/Trackers/`): its lane, its timeout, the
  precondition checked when its turn comes, what it sends (an immediate outcome such as `Sent` or `Local`, or none to
  wait), and its observations. The Action API subscribes once to the events the trackers need (phase, character list,
  create results, character errors, travel, teleports, UseDone, health, chat, tools unavailable) and to the reducer's
  tool replies, and hands each to the trackers in flight: an event one tracker takes (a UseDone, a speech echo, a tell
  confirmation, a tool reply) goes to the oldest unresolved one that takes it; the others reach every tracker in flight.
  An observation only resolves its tracker; nothing completes during the bus dispatch.
- **`Update(frameTime)`**, which `GameSession.Tick` calls after the reducer took the tick's messages (offline: after the
  stand-in's steps): a kill switch the status probe asked for engages; (1) trackers whose confirmation was observed, or
  whose condition now holds (the login's list and phase, the player's object for EnterWorld), resolve, and a failed
  session resolves every tracker in flight; (2) trackers past their timeout end `TimedOut`; (3) queued actions go out
  in call order, a quit first, one session-phase action in flight, the others within their lane's rate limit, each
  after its precondition (a tracker is in flight before its send, so a failure the send raises at once is seen); then
  the step-1 check runs again; (4) the handles settled by these steps complete, in that order, with their continuations
  inline on the game thread. Net's exceptions from a send are outcomes: `NotSupportedException` (a `TODO(net)` stub) is
  `NotImplemented`, `ObjectDisposedException` `Cancelled`, `InvalidOperationException` and `ArgumentException`
  `Refused`.
- **Arrivals.** The Action API counts the player's arrivals (each move to `Arriving` or `TeleportArrived`): LoginComplete
  is sent at most once per arrival, the movement channel's warnings repeat once per arrival, and each world entry
  (`NotInWorld` to `Arriving`) probes the tools.
- **Reconnect and dispose.** `GameSession.Reconnect` resets the Action API (every pending action `Cancelled`, the rate
  limits full again, the arrivals and what the server said about the tools forgotten; the movement controller stays
  bound). `Dispose` cancels what is pending; later calls complete as `Cancelled` ("the session ended").
- **Counters** (`MovementChannel`, internal): MoveToState, AutonomousPosition and Jump sent, sent while not present, and
  not sent; `ActionApi.QueuedCount` and `InFlightCount`.

## 18. The live session (ST13)

`SessionBootstrap.CreateLive` (§4) runs its gates, then builds a `GameSession` over net's network session
(`AcSession.Create`, called in `SessionBootstrap.cs` only; ArchitectureTests). The session holds the credentials, the
reducer as the network session's sink, a raw tap, the bus, the Action API with a live `ActionLink` (the server, the
current network session, and a login callback that hands the session's credentials to net), the player tools and,
when the host passed a stream, the recorder.

**The network session's options** (`SessionBootstrap.NetworkOptions`): the gated server, the data check's iterations
from the dats, the session clock's `TimeProvider` (one clock for timeouts, transport timers and recordings), net's log
adapted to the session's (`NetLogAdapter`: category `net`, severities one to one), the socket factory (null: net's
loopback UDP socket), and Lifestone's defaults; `--retail-net` gives `SessionTimeouts.Retail`, `LoginRetryPolicy.Retail`,
no tail probe and `InboundOrder.Arrival` (D18). The dispatch budget per tick is `--session-budget-ms`.

**The tap.** Net calls the session's tap with every game message, inbound just before its dispatch, outbound inside
the call that sends it. The tap converts net's timestamp to session-clock seconds and hands the bytes to the recorder
(client bodies off the allowlist are dropped there, §14) and then to the bus's raw subscribers. With neither, it
returns at once.

**`Tick(now)`** (§2): `Pump(budget)` (each message: tap, then the reducer, which publishes), the parked messages'
expiry, `ActionApi.Update(now)`, then `SessionRecorder.FlushIfDue`. `LastTickMs` times it on the session clock.

**`Reconnect()`** (D43): the Action API is reset (everything pending `Cancelled`, the rate limits full, the arrivals
and the tools' knowledge forgotten); the old network session is disposed (a Disconnect when it was still connected);
local notice 7 resets the reducer (the world left as on a logoff, then Idle, the list, chat, server time and parked
messages forgotten; the bus and its subscribers kept); a new idle network session is created with the same factory.
The host then calls `Actions.Login()`; the credentials are still the session's, so no secret is needed. The recording
goes on in the same stream. Offline, `Reconnect` resets and runs the stand-in again.

**`Dispose()`**: the Action API cancels what is pending; the network session quits (net's `Quit`: F653 in the world,
then a Disconnect; skipped, with a WARNING, when called off the game thread) and is disposed; the recorder writes its
trailer with the final `GameStateDigest` and flushes (one INFO line with the frame count and the digest); the
credentials are zeroed. Safe to call twice.

**`GameStateDigest`** (public, `Recording/`): SHA-256 over a canonical snapshot of the read model, tagged
`lsdigest/1`: the phase, the offline flag, the data check and the failure; the world's name; the character list (slot
count, selection, each entry in server order); the player (guid, name, travel, position, the nine sequences, physics
state); the qualities (each table sorted by key; floating-point values bit for bit); every object sorted by guid (name,
setup, self flag, state, position, parent, motion presence and autonomy, velocity, angular velocity, sequences,
health); the chat (total, then each kept line's kind, type, sender and text). Positions, quaternions, velocities and the
health fraction are quantised to 1e-4 and written as integers. Times are left out (a replay runs on its own clock), and
so are the action log and the counters. Integers are little-endian, strings length-prefixed UTF-8.

**Tests**: `GameSessionLiveTests` (idle creation, the tick order, the retail-net mapping, net's log, a whole
login-to-logoff, reconnect with subscribers kept, dispose, the frame wait), `GameStateDigestTests`,
`PasswordHygieneTests` and `SessionBoundaryTests` (§10), `SessionBootstrapGateTests` and `S8LoopbackRunnerTests` (the
gates). The network session in them is `ScriptedAceSession` (tests): bodies written by the shared `AceWireWriters`
(tests/Shared/Net), dispatched through net's `InboundDispatcher` like live ones, with net's event order;
Lifestone-authored values only.

## 19. Replay (ST14)

A recording (§14) replays two ways, both through net's `InboundDispatcher`, the decode path of live play.

**`SessionReplayer`** (public) replays a recording into a fresh reducer and bus, with no Action API and no network
session. Frame by frame: the replay's clock (`ReplayClock`, a `TimeProvider` that stands still between frames) moves
to the frame's recorded time and parked messages expire after each frame, as the live session expires them at the end of its tick (`TODO(replay-tick-boundary)`: the recording keeps no tick ends, so a frame earlier in the same live tick than the creating message can expire a message in replay that live keeps, a window of less than a frame); a server message goes to the raw
subscribers and through the dispatcher into the reducer; a client message goes to the raw subscribers and to
`OutboundCheck` (a test's decoder) and changes nothing; a session event is applied with the frame's time; a local
notice is applied again (codes 1 to 15 by the reducer, 16 and up by publishing what the Action API published). An
unknown notice code, an unknown frame kind or a server message recorded without its body throws
`InvalidDataException`. `Replay` returns a `ReplayResult`: the header, the counts (frames, server and client messages,
session events, notices, decoded, unknown and malformed messages with their opcodes), how the recording ended, its
trailer, the replayed digest, `DigestMatches` (null without a digest), and the handler failures. A recording cut short
replays to its last whole frame. One replay per instance.

**`ReplayAcSession`** (internal) is an `IAcSession` that plays a recording into a whole `GameSession`
(`ReplayAcSession.CreateSession`), so the Action API's handles run against recorded traffic. Frames are delivered when
the session clock reaches their recorded time plus the replay's lag, and stop at the client's own steps until the
session takes them: the first connect waits for `Login`, a recorded close for `Quit`, and each client message the
client chose to send (F656, F655, F7D9, F7C8, LoginComplete, Use, QueryHealth, Talk, Tell, F653) for the same send
(same opcode; for a GameAction, the same action type). The lag grows by however late the client acted. Client messages
net sends by itself (F7E6, F657) and movement are passed over. A send that does not match the next recorded step is a
divergence (counted, described and logged as a WARNING); the replay goes on. Recorded notices are skipped: the
session's own Action API makes them again.

**Synthetic recordings** are made at run time by a live session over `ScriptedAceSession` with a recorder (a login, the
world entry with a PlayerDescription, three stand-in objects and an UpdatePosition, LoginComplete, the status probe, a
chat line, a logoff, a quit), never committed (D14). `ReplayTests`: a synthetic recording replays to its trailer's
digest; twice to the same event-stream hash; client messages reach `OutboundCheck`; a cut recording replays to its last
frame; a `ReplayAcSession` completes Login, EnterWorld, LoginComplete, Talk, LogOff and Quit handles and ends at the
live session's digest; a send the recording did not make is one divergence. **Real recordings** in `cache/sessions/`
of this checkout or the main checkout (the headless host's) run under `[RequiresRecordingFact]`, which skips without
them: no malformed message, no handler failure, an intact trailer whose digest matches, and the same event stream
twice. A `.lsrec` that ends before its header does (left by a host before the recorder wrote its start) is not a
recording: `RealRecordings` leaves it out and names it in the test output (or in the skip reason when nothing else
is there).

## 20. The headless host (ST15)

DoD 13's session host with no Godot: `dotnet src/Lifestone.Headless/bin/Debug/net10.0/Lifestone.Headless.dll
--scenario=login` logs in to the bundled ACE with the test account, enters the world, materialises, checks the server
mod's status and the server's view of the player, logs off and quits, and prints the verdict line. Run it with the `DOTNET_*` opt-out variables set
(CLAUDE.md); the build writes no .exe (`UseAppHost=false`).

**`Program`** (`src/Lifestone.Headless/`, the only file with I/O): parses the command line (§4; `--scenario` other than
`login`, `holtburg-laps` (§24) and `academy-exit` (§25) is exit 2); finds the backend's runtime folder (`ServerRuntimePaths`), reads pids.json
and stops unless `ace.modReady` (before the dats are opened); opens the dats (`--dat-dir`); reads the ACE commit from
server/versions.json for the recording header; opens the recording (`cache/sessions/<UTC>-headless.lsrec`, or
`--record-session=DIR`; always, D48) and the log file (`cache/headless/<UTC>-headless.log`), both checked with
`OutputPaths` against the checkout's root; reads secrets.json's bytes and zeroes them as soon as the session holds its
credentials; installs a `GameThreadSynchronizationContext`; runs `HeadlessRunner` on `TimeProvider.System` on its own
thread (Ctrl+C cancels the run); prints the verdict line on standard output and, with `--summary=PATH`, the whole
result as JSON (scenario, ok, stage, failure, phases, loginCompletes, errors, warnings, seconds, recordedFrames,
corrections, whereMaxMetres, unmatched, recording, exitCode). The headless host is always an automated run, so it signs in only to the test account: no `--account` means `test` (D10), and `AutomatedAccountRule.TryApply` is applied twice, in `Program.Run` (before the backend or the dats are touched; exit 2, "the run is refused: an automated run ... uses only the test account") and in `HeadlessRunner.StartWith` (every caller; `PlayAccountException`, one WARNING, summary phrase "the account gate refused (see the log)"). Log lines
go to standard error (`--log-level`, default info) and to the log file. Exit codes: 0 the scenario succeeded; 1 a bad
command line (nothing ran) or a failed scenario; 2 the run could not start (no dats, no backend, a refused gate, a
scenario not built yet). Every message names files and remedies, never a secret. When `HeadlessRunner.Start` refuses
(once the backend was found and the dats opened), the host logs the refusal as an ERROR, prints the verdict line with
`ok=0` and `recording=none` (the failure in the JSON is a fixed phrase, `HeadlessRunResult.StartFailure`: the
refusal's message can list account keys and usernames, so it stays in the log), and deletes the recording file it had
opened, which no session wrote to. `--workers` is accepted (a Core option the game shares) and has no effect: one
WARNING says the headless host decodes the destination on the game thread.

**`HeadlessRunner`** (public, `Hosting/`). `Start(options, scenario, secrets, pids, dats, time, log, recording)` runs
its own loopback gate (runner `HeadlessRunner`, A0's S8) and mod-ready gate, then `SessionBootstrap.CreateLive` with a
recording header (runner `headless`, the scenario's name, the Lifestone version, the ACE commit) and a
`CountingLogSink` around the log. It builds portal space as the game runs it, drawing nothing: a `TimedTunnel` (the
tunnel's frame counter without the tunnel), a `PortalSpaceMachine` (seeded roll, Lifestone's 60° camera focal), a
`PortalSpaceDirector` with no sound and no notice sink, and the `PortalSessionBridge`, so LoginComplete goes out when
the destination is ready and the arrival's sequence has played, exactly as on screen. One frame (`Step(now)`):
`session.Tick(now)`; the world focused on the player's cell; `director.Step(now, cell, world)`; `scenario.Step`; the
run's time limit (`--quit-after`, default 180 s on the session clock). `Run(context, token)` repeats it with the
session clock's now as the frame time, runs the context's posted callbacks, and waits for a datagram for at most 16 ms
between frames. The run ends when the scenario succeeds or fails, the limit passes, a frame throws (logged as an
ERROR) or the token is cancelled; `Result` (`HeadlessRunResult`) holds whether it succeeded, the scenario's stage and
failure, the phases entered, the ERROR and WARNING counts, the seconds, the LoginCompletes sent, the frames recorded,
the player's force-position corrections (`GameSession.Corrections`), the scenario's largest server-to-client distance
(`IScenario.WhereMaxMetres`) and its unmatched movement (`IScenario.Unmatched`). `SummaryLine` is m2-plan §4.14's
frozen verdict line, keys in this order and nothing else: `headless done scenario=login ok=1 corrections=0
where_max_m=0.000 unmatched=0 recording=cache/sessions/20261012T142233Z-headless.lsrec` (`where_max_m` with three
decimals, or `none` when the scenario measured nothing, which only a failed run can show; `recording=none` without a
recording). Disposing the runner disposes the bridge and the session.

**`DataOnlyAttachments`** (public, `Hosting/`): the destination's readiness from the dats (m2-plan §4.5). `Focus(cell)`
decodes, for an outdoor cell, the LandBlock records of its landblock and the eight around it; for an EnvCell, the cell
and every cell it can see (`EnvCellData`), plus the 3×3 terrain when it sees outside. Queries read what was decoded:
terrain and cells `Attached`, or `Failed` with a WARNING for a missing or malformed record (settled, so the tunnel never
hangs on it), `Pending` before the focus reached them; objects `Disabled` (a data-only host has none); collision
`Disabled` until physics' collision streamer lands in the headless host.

**`LoginScenario`** (public, `Scenarios/`), stage by stage: `login` (`Login`, confirmed at the list); `choosing`
(`--character` by name, case-insensitive; else `--create-character`'s, created once when missing as `creating`; else
the first character not pending deletion; none is a failure naming the remedy, never the name); `entering`
(`EnterWorld`); `arriving` (until the player is `Present`, at most 90 s: portal space's director sends LoginComplete
through the bridge); `status` (`Tools.Status`; an access other than `Player` fails it, and the probe has already engaged
the kill switch, D11); `where` (`Tools.WhereAmI`: the server's position of the player must agree with the client's
within 0.5 m, `PositionAgreement`; nothing moved the player, so a disagreement is a read-model defect and fails the
run with both positions; the distance is `WhereMaxMetres`); `logging-off` (`LogOff`, confirmed at the list);
`quitting` (`Quit`, `Sent`); `done`. `HeadlessCharacter.Intent(name, skillClasses)` is the character
`--create-character` makes: Aluvian, male, the first template and appearance choices, no headgear, 55 in each
attribute, Holtburg's start, and the skill classes retail's character creation starts with before a template
(`HeadlessCharacter.RetailSkillClasses(dats)`, re-expressed from `CharGenState::ResetSkillLevels`, 11.4186 0x005C43B0,
11.6096 0x005C5390, with `ACCharGenData::GetSkillTrainedCost` and `GetSkillSpecializedCost`: for every skill of the
SkillTable, the heritage's costs when it lists the skill, else the table's; a skill that costs something to train is
untrained, a free one trained, one free to specialise specialised). At the end of retail that trains Arcane Lore, Magic
Defense, Jump, Run, Loyalty and Salvaging and leaves every other skill untrained, so Run and Jump are usable (ACE's
`CreatureSkill.IsUsable`); no credit is spent and no template skill is trained. The bundled ACE accepts it as its
`PlayerFactory.Create` reads it (ACE @95ef426d, reference only). A character created before this fix (every skill
inactive, run rate 1.0) keeps its skills: delete it in the game to have it made again.

**Tests**: `HeadlessRunnerTests` (the login scenario against `ScriptedAceSession` with portal space's real timings, its
recording replaying to its digest, the verdict line against §4.14's pattern for a passed and a failed run, counted
corrections, a server position that disagrees, character creation and choice, an Admin access, a missing mod, the time
limit, the mod-ready refusal, a refused account's fixed failure, each refusal's phrase, a throwing frame,
cancellation), `HeadlessCharacterTests` (the skill classes over stand-in costs and, with the dats, the end of
retail's), `DataOnlyAttachmentsTests` (with a table and, with the dats, the Training Academy), and in
Lifestone.Loopback.Tests: `HeadlessProgramTests` (help, bad arguments, holtburg-laps and academy-exit checking the backend first, no backend, no mod, a relative
runtime folder, `--workers`'s warning, and, with the dats, a refused account on a stand-in checkout: the verdict line,
no username in it, no recording left; no network) and `HeadlessLoopbackTests` (`[LoopbackFact]`,
skipped without a backend with the mod ready: a real login that enters and leaves the world with one LoginComplete and
a recording that replays to its digest, and the real test password's count in the recording and the log, which must
be 0).

## 21. The game's session host and play mode (G6 W9, first stage)

`tools\play.ps1` starts the backend and runs the game with `--play`; the game's `SessionHost` signs in, the retail screens run on that session, and play
mode builds your character from the server's own CreateObject. Nothing in this stage needs a live server to be tested: the flow has event-stream tests,
and `--play-checks` runs the whole path in the engine on a scripted stand-in server.

| Piece | Where | What it does |
|---|---|---|
| `tools\play.ps1`, `tools\lib\Play.psm1` | `tools/` | L18. Refuses unless `server\versions.json` names 127.0.0.1, and after that check passes the game `--server=127.0.0.1:<network.acePort of server\versions.json>` (`Get-PlayServerEndpoint`), so the client and the backend cannot disagree on the port; reads `Get-LsBackendState` of the main checkout's module (a worktree plays against the one backend); uses a running ACE whose mod is ready (`pids.json` `ace.modReady`), refuses one that runs without it, starts the backend (`start-server.ps1`) when ACE is down; runs `tools\godot.ps1 -Run` with `--play` (and `--account=NAME`, `--character=NAME`, names only); stops only what it started (`stop-server.ps1`, or `-AceOnly` when MariaDB was up before; `-KeepServer` keeps both). `-Offscreen` (agents and tests) makes the account `test` and refuses any other: the play account is the owner's. `-GameArgs` may not carry `--play`, `--account`, `--character`, `--server` or anything named for a password. godot.ps1 splits its argument strings on whitespace except inside double quotes, so a character name with spaces works. |
| `PlayPresentationFlow` | `Client/Play/` | The rules, engine-agnostic and tested (`PlayPresentationFlowTests`). From the session's events it calls an `IPlayWorld`: `PlayerArriving` (the player's own CreateObject, once per arrival, also a re-create), `PlayerPresent` (the unhide), `PlayerTeleported` (a self-teleport position, after F751), `PlayerPositioned` (a force-position or an echo), `PlayerStateChanged`, `PlayerMotionChanged`, `PlayerDescChanged`, `PlayerLeft` (a logoff, a lost connection or a boot). It enters the world by itself when `--character` names a character (selected and entered once per connection; an unknown name or one pending deletion is a logged ERROR that does not repeat the name). `StatusText` is the placeholder status line. |
| `SessionHost` | `scripts/Session/SessionHost.cs` | The only file that creates a session (the architecture test). Priority -900. `Install` reads `pids.json` and `secrets.json` as bytes, calls `SessionBootstrap.CreateLive` (the four gates), zeroes its copy of the secrets, logs in at once, and ticks the session each frame with the portal clock (`PortalSpaceSetup.Now`) inside the `"session"` frame-budget share. No session (no backend, a refused gate): `StartFailure` says why, the main scene shows it and an unattended run exits 2. `NOTIFICATION_WM_CLOSE_REQUEST` calls `Actions.Quit` and ticks for at most 0.5 s; leaving the tree disposes the session, which quits the same way. `Reconnect` (the disconnected screen's button) resets the session and logs in again. `CreateStandIn` makes the offline stand-in of `--retail-ui=standin`; `InstallStandIn` is the host over the scripted visit of `--play-checks`. |
| `PlayMode` | `scripts/Session/PlayMode.cs` | Hides M1's panels and keys (D41: the time and teleport panels are not made, so T is gone; the free camera ignores Tab, Esc and F; the interiors ignore F7 and F9; the position HUD is hidden; F1 and F3 stay), pauses the world until the server creates the player, then queues each `IPlayWorld` call for a later frame than the event's (so building the player never happens in the session's tick frame). Owns the session's one `AvatarSet`, the `SessionAvatarAdapter` that feeds it from the bus (the player's own state, motion and description events included, so the three matching `IPlayWorld` calls do nothing here) and the `ServerObjectsLayer` that draws it (docs/avatars.md section 17; section 23 below). Warms its first-use costs at start-up (`PlayWarmUp`). Arrival: `PlayerRig.Build` from the server's description (`AvatarWire.FromWorldObject`) with the session's `Actions.Movement` (the movement channel) as the sink, the retail UI's router for the keys, the collision streamer given to the portal view's readiness (`WorldAttachments.CollisionSource`), `PlayerPlacement.StartWorldAround`, and the UI sent to the game screen. A server teleport: `PlayerPlacement.Teleport` (world, interiors, physics, camera; the constraint is kept, as the server echoes positions). Leaving: the rig is freed and the world is paused again. The `PortalSessionBridge` is made here (`AttachPortal`); it alone feeds `TeleportProgress` and sends LoginComplete (per arrival, which includes each teleport: ACE takes it as "the client has arrived"). |
| `PlayerRig`, `PlayerPlacement` | `scripts/Player/`, `scripts/Session/` | `PlayerRig.Build` is what `OfflinePlayerSetup` built before, shared: the avatar set, the view, the physics world with its collision streamer, the walker and controller, the input, the chase camera and the on-screen text. Offline it gets the stand-in's description and a dry-run sink; online the server's description and the real sink. |
| `GamePlayScreen` | `Client/RetailUi/Screens/` | The game mode (G4 g1; `PlayGameScreen` before it): it releases the forced display, builds the in-game shell's root and finds its windows (docs/retail-ui.md section 21), names the windows without content in a placeholder note, and returns to character management when the server sends the character list again. |
| `PlayStatusLine` | `scripts/Session/` | The placeholder status line (top left), under a prefix that says the shell's spew box and chat are not built yet (M2 G4; g2 retires it). |
| `StandInVisit` | `Client/Session/StandIn/` | A scripted visit of the world through the offline session's own reducer: the player's F746 and a hidden CreateObject (human Setup and motion table, no description: the base body), the unhide, teleports, a logoff. `StandInSessions.PlayerVisits` schedules it. |
| `--play-checks` | `scripts/Session/PlayChecks.cs` | 56 checks in the engine on that visit (25 in the first stage; section 26 has the online flows added by slice b4), no backend: the world waits; M1's panels and keys are off; the retail UI runs on the session; the player is built from its own CreateObject with the session's sink (not a dry run); the world starts around it and streams; physics places it on the ground; the game screen shows; portal space plays and sends LoginComplete once before the unhide and once more for the teleport; the teleport moves the world, the interiors, the physics and the camera; a logoff takes everything away and the UI is back at character management. Verdict `[playcheck] PASS|FAIL <name>`, then `all play checks hold`; smoke step `play-checks`. |

**The options.** The Session group is parsed by `GameOptions` (`GameOptions.Session`; its problems join `Errors`). `--play` alone is interactive. An
automated run passes `--quit-after=N`; the session is disposed when the tree closes. With `--play` the retail UI is always on (`RetailUiMode.Live`):
`--retail-ui-start` is ignored (the login flow starts at the data check), `--offline-player` is ignored, and `--retail-ui-only` shows the screens over the live session with no world.

**Exit codes of the game.** 0 normal; 1 a bad argument in an automated run, `--quit-after` reached, a check failed; 2 play mode could not start (no backend,
a refused gate, no account). `tools\play.ps1` exits with the game's code, 2 when it could not start the backend, and 1 when it refused.

**Loud TODOs (logged):** `g6-logoff-confirm` (closed in M2 G4 g6: the key does not ask, as retail; section 26),
`g5-w10-scripts` and `g5-w10-sounds` (the scripts and sounds the server plays on an object are not played; only the Hidden and UnHide scripts of the state bit are), `g5-translucency` (closed for the materialise fade by slice f2, section 27: a part fading in is drawn by its blended twin; the first-person fade is `g5-first-person-fade`), `g5-object-lights` (an object's own lights light nothing), `g6-arrival-position` (a player CreateObject with no position), `g6-portal-fallback` (no portal view: LoginComplete at arrival),
`retailui-ingame-shell` (G4), `portal` (target indicator; the busy count is closed, M2 G4 g6). (`g6-qualities-weenie` and `g6-logoff-ui` were closed by slice b4, section 26; `g6-teleport-frame-time` by slice b5, section 27: the teleport frames were measured and hold.)
Not built in this stage (the key came with slice b4, section 26): a logoff panel (the in-game shell and the tools panel), a retry of a refused LoginComplete, G5 W11 (movement ACE accepts: the cadence and the
server's corrections are physics' `ServerPositionPolicy`, which `PlayerPositioned` already calls) and the live login scenario (`--login-check`, W12: built in stage 2, section 22).

## 22. The first live login (G6 stage 2)

The client was run against the real ACE for the first time: net's live phase A as loopback tests, and `--login-check=enter` in the engine. Both use the test account only.

| Piece | Where | What it does |
|---|---|---|
| `LoginCheckScenario` | `Client/Session/Scenarios/` | The script of `--login-check=enter`, an `IScenario` that acts only through the Action API and never blocks. The host owns the login (a session idle for 5 s fails the check). Stages: `login` (the character list, with the data check complete), `choosing` (`--character`, else `Lifestone Smoke`; created once when missing with `HeadlessCharacter.Intent`: Aluvian, 55 skills), `entering` (`SelectCharacter`, `EnterWorld`), `arriving` (until the server unhides the player, at most 90 s; the arrival cell must be in landblock 0x8602 and LoginComplete must have been sent once), `settling` (the player stays present 5 s: in the world, not hidden, no teleport, one LoginComplete; the objects the server sent are counted), `logging-off` (back at the character list, intact), `quitting`, `done`. Counts LoginCompletes (`SelfTravel.Materialising` entered), teleports (`PlayerTeleportAnnounced`) and character errors. Tested on `ScriptedAceSession` (`LoginCheckScenarioTests`). |
| `LoginCheckRunner` | `scripts/Session/` | The Godot node that steps the scenario each frame after the session host and play mode. It adds what only the engine sees: the retail screens the run went through (data check, character management, game, character management again), the player's body built from its own CreateObject when the server unhides it and gone after the log off, portal space's LoginComplete count, and the largest frame time between Enter and the end of the wait. Verdict line: `[logincheck] done ok=<true\|false> enter_to_walk_max_ms=<x> teleports=<n> academy_objects=<n> logoff=<ok\|failed> materialised=<true\|false\|n/a> objects_drawn=<n\|n/a>` (a field the run did not reach is `n/a`), then a detail line and a `world content` line (the census, the objects drawn and waiting, the meshes built, the adapter's counters), then exit 0 (held) or 1. Since stage 3 it also judges the frame rule (DoD 15: no frame over 50 ms from the moment the retail character list has been up three frames to the end of the wait; each frame over it is logged with what it held), `materialised` and that the layer drew some of the creatures and items the census counted, and it saves three review frames under `docs/screenshots/m2/login/` (gitignored; the PNG is encoded on a worker, the main thread reads the viewport back in about 9 ms): `01-portal-tunnel` (portal space live), `02-arrival-among-academy-objects` (the shell around the world), `03-shell-after-the-settle` (g7: the Academy, the shell, the radar's blips), `04-character-list-after-logoff`. |
| `PlayOptions`, `--login-check` | `Client/Play/`, `OptionCatalog.Play` | `--login-check=enter` implies `--play`; a login check makes play mode skip the automatic entry of `--character` (the script enters). `academy-exit`, `holtburg-laps`, `all` and the bare form (= `all`) are built (section 27); `throwaway` is a separate check, never part of `all` (section 28). `tools\play.ps1 -Offscreen -IsolateUserData -TimeoutSeconds 300 -GameArgs:'--login-check=enter'` runs it on the test account; the game enforces that itself (`AutomatedAccountRule`, applied by `SessionHost.Start` to every run with a login check or that ends by itself: no `--account` means `accounts.test`, any other account is a start failure, exit 2), so a bare `godot.ps1 -GameArgs:'--login-check=enter'` cannot reach the play account either; smoke step `login-check` (needs the backend) runs it with `--account=test`; smoke step `login` runs it through `tools\play.ps1` (SKIP when the backend is not ready under `-Backend auto`) and counts the frames it wrote. |
| `PresenceGatedMovementSink` | `Client/Play/` | What the first live run found: the player's body, placed on the ground inside portal space, sent an AutonomousPosition while the server still had the player hidden, and the channel warned (ACE ignores positions until it has handled LoginComplete). Play mode now gives the body this sink: it forwards to the session's movement channel only while `SelfTravel.Present`, and counts what it held (`PlayMode.MovementGate.Held`; one message in a normal arrival). |
| N11 | `tests/Lifestone.Loopback.Tests/Net/` | Net's live phase A through `AcSession` itself (that folder's README). The first test asks the mod for its status and aborts the loopback run when the access is not Player or auto-promotion is on. |
| N12 (5b slice b1) | `tests/Lifestone.Loopback.Tests/Net/` | Net's live phase B on the `Lifestone Loopback` character: `place poi "Holtburg"` (F751 + F748 in landblock 0xA9B4), a test-only `KinematicWalker` over `TerrainSampler` driven by a `WalkDriver` on retail's AutonomousPosition cadence (`PositionCadence`, re-expressed from `CommandInterpreter::ShouldSendPositionEvent`, 11.6096 0x006B5520), `@lifestone where` within 0.5 m of the last position sent, the force-position sequence unchanged, and the lossy soak (`Tier=Soak`) with the `@lifestone net` bad-checksum oracle. Not product code: the game's own mover is `PlayerMovementController`. |

**What the live runs showed** (ACE @95ef426d, the Lifestone mod 0.1.0):

- the mod's status on the test account: `access Player`, `auto-promotion off`, wire 1; ACE created the account at the first login (`Auto creating account`) and logged the mod's own "auto-promotion is off" line at start-up;
- the character is created and entered: the Training Academy's start cell is 0x860201AD; the server sends the player hidden and clears Hidden after LoginComplete; it sends 34 other objects in the Academy;
- Quit, then an immediate login of the same account, is answered with error 0x01 once, whatever the client does: ACE keeps a terminated session for 2 s (docs/protocol.md section 4.4). The default login policy retries and gets in; a login after the 2 s meets no 0x01. The plan's "immediate re-login without 0x01" cannot hold against ACE;
- the frame time between Enter and the end of the wait reached 127 to 136 ms in stage 2 (five frames over 50 ms); stage 3 attributed and removed them (docs/avatars.md section 17), and it is now 35 to 39 ms;
- Godot prints `Condition "!res" is true. Returning: false` about ten times in an off-screen run, with or without a server (also in `--play-checks`); it is the engine's, not the session's;
- (5b slice b1) a client that walks the way retail does is accepted exactly: after `place poi "Holtburg"` (one F751, one F748 at cell 0xA9B40019 (84.00, 7.10, 94.00), then the landblock's objects) and LoginComplete, 47 m on the retail cadence leave `@lifestone where` 0.00 m from the last position sent, ACE echoes every message as an F748 for the player, and the force-position sequence stays 0. The same walk for 2 minutes over the 5% drop, 2% reorder link never failed the session. Details and the soak's oracle: m2-plan.md section 3.3, N12.

## 23. The server's objects in the game (G6 stage 3 with G5 AV13, W6, W10)

Play mode no longer builds the player in a set of its own: the session has **one** `AvatarSet`, and everything the server creates is in it (the pieces and why are in
docs/avatars.md section 17).

- **Flow of an event.** The reducer publishes `ObjectCreated` and the rest inside the session's tick; the `SessionAvatarAdapter` queues them in order; `PlayMode` applies
  them (3 ms budget) at the start of its process, after its queued work and not in a frame whose work took over 8 ms; the player's controller advances the set; the
  `ServerObjectsLayer` (priority 12) draws it. The player's own CreateObject is the first object event of an arrival, so the player's build (`PlayerRig.Build`,
  `Avatars:`) waits (a frame at a time, 600 at most, then a logged error and exit 1 in an automated run) until the adapter has applied it, and a re-create (F7DB) until the
  set has made the new avatar.
- **Frames of an arrival** (DoD 15): tick (the burst of CreateObjects queued) -> next frame: `PlayerArriving`'s work (the build, 13 ms) -> next: the world starts around the
  player, `rig.UseAsWorldFocus` (the focus is not set by the build) -> next: the keys (`rig.AttachKeys`) -> the objects' creates over the frames that follow.
- **A log off** (or a boot, a lost connection): the rig is freed and the layer switched off, and `PlayMode` makes a new `AvatarSet` and adapter at once (the old adapter
  unsubscribed, its queue forgotten), before the next login's first CreateObject.
- **The census.** `WorldContentCensus.Count(state)` over the objects the server placed (a position or a parent), other than the player. The Academy of the test
  character: 4 NPCs, 7 creatures, 3 doors, 9 items (no portal, lifestone or sign among the 23 placed; 35 objects are held with the player and the pack). The layer drew 8
  of them at the arrival (the objects of the cells the interiors show).
- **Stand-in.** `StandInVisit.Objects` lets the scripted visit of `--play-checks` create objects beside the player; the stand-in player now carries the human's
  PhysicsScriptTable (`StandInVisit.HumanScriptTable`, 0x34000004), as ACE's does, so its Hidden and UnHide scripts play.
- **The arrival's first-use compiles (DoD 15).** One live gate once logged a 76 ms frame at the arrival whose whole cost was the render server's CPU set-up. The
  `[play] slow frame` and `[logincheck] frame over 50 ms` lines now carry `RenderingServer`'s pipeline-compilation counters (`pipelines_canvas`, `_mesh`, `_surface`,
  `_draw`, `_specialization`: their difference to the previous frame; `Diagnostics/PipelineCompilations.cs`), and the login check logs every frame between Enter and
  walking that compiled anything, with its stage, and the window's total in `enter_to_walk_pipelines`. The cause: the moving objects' lighting rules
  (`SurfaceLighting.DynamicObject` and `DynamicCellObject`) were in no start-up warm-up, so the first mesh dressed with each variant compiled its shader and 1 to 7
  pipelines on the arrival's frames (the Academy's first three variants: during the tunnel, at the world's first frames and when the player's body appeared).
  `ServerObjectsWarmUp` (called from `PlayMode.Install`) compiles all 26 reachable variants under both rules (about 300 ms of start-up) and draws one sub-pixel
  triangle of each through a MultiMesh with `INSTANCE_CUSTOM` once, in a viewport of its own (the main viewport draws no 3D before the arrival). The window's surface
  compilations went from 11 to 4. What remains is named in the check's `TODO(arrival-pipelines)` line: a canvas pipeline at the tunnel's start (15-38 ms; the notice
  line's panel and the tunnel screen's texture rect were tried and are not it), the player figure's creature-mode surfaces in the tunnel (about 4 ms) and the first
  object's 4 draw-time pipelines (about 4 ms). Also seen once in 17 runs: a 50 ms frame of main-thread work (not render set-up) when the world is built around the
  player, just after the tunnel starts.

## 24. The movement scenarios, part 1 (ST16, slice b2)

**`QualitiesWeenie : IPhysicsWeenie`** (`Session/QualitiesWeenie.cs`) is the logged-in character's weenie for physics,
answered from `Self.Qualities` and read afresh at every question (a PrivateUpdate is felt by the next movement). The load
is the burden (PropertyInt 5) over `EncumbranceSystem.EncumbranceCapacity` of the current Strength (start plus ranks); the
Run (24) and Jump (22) skills are `CACQualities::InqSkill` (11.4186 0x00591DF0, 11.6096 0x00592AF0): the SkillTable formula over
the current attributes plus the skill's start level (`init`) plus its ranks, and nothing for the class (0 below the skill's
minimum class). The 5 (trained) or 10 (specialised) of the creation screen (`CharGenState::GetSkillScore`) is not added: the live
ACE record of a Run trained at creation is class 2, ranks 5, init 0, experience 526 (read from every recording in
`cache/sessions`, `QualitiesWeenieRecordingTests`), so the bonus is already in the record (in the ranks there; a server that
puts it in `init` gives the same level); `TryGetRunRate` is `MovementSystem.GetRunRate`, `TryGetJumpVelocity`
`sqrt(GetJumpHeight x 19.6)`, `CanJump` a load below 2 and `TryGetJumpStaminaCost` `JumpStaminaCost` payable while vital 3's
current value covers it. Before the PlayerDescription it cannot tell (the interpreter keeps `MyRunRate`). Named TODOs,
logged once: `state-enchantments` (buffs are not applied), `state-enlightenment` (11.6096 adds it to Run and Jump),
`state-carry-augmentation` and `state-pk-status` (`IsPK` and `IsPKLite` are false). `OfflineWalker.Create` takes any
`IPhysicsWeenie` (null keeps explore mode's placeholder). The game's walker has it since slice b4 (section 26):
`PlayerRigRequest.Weenie`, passed by `PlayMode` (`TODO(g6-qualities-weenie)` is closed).

**`HeadlessCharacter.RetailSkillClasses`** now reads the retail UI's `CharGenState` (`RetailSkillClassesOf(CharGenCatalog,
heritage)`): a state for the heritage, `ResetSkillLevels`, `GetSkillLevel` for 0 to 54. The headless copy of the rule
(`SkillClasses`) and its stand-in cost tests are deleted; the dats tests remain.

**`RoutePlay`** (`Session/Scenarios/RoutePlay.cs`) is the in-world part: it makes an `OfflineWalker` (the session's
`QualitiesWeenie`; sink = `MovementAudit` over a `PresenceGatedMovementSink` over `Actions.Movement`) in the physics world
it was given, sends the player to the route's start with `place loc`, applies the player's own `ObjectMoved` events to the
controller (`PlayerMovementController.Apply`; positions outside the world's landblocks are ignored), asks `where` until
the server no longer has the player teleporting, walks the route with `RouteDriver` (the controller ticks, then the driver),
asks where-am-I once per stop 1.1 s into the 1.5 s dwell (the 1.0 s cadence has sent the rest position; not at `Stopped`, so
the player is still standing when the server answers) and records it against the client's position at the time of the ask,
then requires `MovementAuditSummary.Passed` and one check per lap. Unless `returnHome` is off it then sends the character
back with `place loc` to where the login found it, stops sending movement from that moment, and requires `where` within
1 m. `Unmatched` is the position messages the gate sent minus the player's echoes (reported, not asserted:
`TODO(st16-unmatched)`; it was 0 to 5 over live runs).

**`HoltburgLapsScenario`** builds the physics world in its constructor (Holtburg's landblock and the eight around it from
the dats, a few seconds) and the ring (`RouteLibrary.HoltburgLaps` around ACE's Holtburg arrival point), and runs
`LoginScenario` with the `RoutePlay` as its new `play` stage (between `where` and `logging-off`). `--scenario=holtburg-laps`
in the headless host builds it after the backend and the dats are checked; add `--character=NAME` (the Loopback tests use
`Lifestone Loopback`) or `--create-character=NAME`. The live result: `headless done scenario=holtburg-laps ok=1
corrections=0 where_max_m=0.010 unmatched=4`, three laps in about 28 s, run rate 1.67 at Run 65 (measured before the class bonus was dropped from the skill's level, above:
the same character is Run 60 and runs at 1.635 now; the live gate is rerun with it).

**Tests**: `QualitiesWeenieTests`, `HoltburgLapsScenarioTests` (against `ScriptedAceSession`, which now plays ACE for a walk
with `AcceptsMovement`: `place loc`, F751, echoes, `where` from the tracked position; a correction and a disagreeing
server fail the audit; the character is left home), and `HoltburgLapsLoopbackTests` (live, `Lifestone Loopback`).


## 25. The movement scenarios, part 2: the Academy exit (ST16, slice b3)

`AcademyExitScenario` is `LoginScenario` with a `RoutePlay` that has a `PortalExit` (`--scenario=academy-exit`; the headless
default limit is 600 s). The physics world is the Academy landblock (0x8602) only. The Academy start (CharGen start area 0, cell
0x860201AD) and the wing that holds the exit portal are **not connected**: the 176 rooms around the start share no doorway with
the portal's cell 0x86020169 (measured on the dats, `PortalExitWingTests`; the Academy is a set of separate sections joined by
quest-gated portals). So the live scenario (`portalCell: PortalExit.ExitToHoltburgCell`) is sent with `place loc` to a room of the
portal's wing, `PortalExit.WingStart` (the room four doorways from the portal cell, 0x86020148 at (137.8, -140.0, -6.0)), and
walks from there; it is sent home to the Academy start at the end (`PortalExit.Home`). Without a portal cell the start is the
Academy start (the scripted-server tests). The stages after `walking` are `using`, `exiting` and `exit-where`, and `refused-where`
when the portal refuses (`RoutePlay.Exit.cs`):

- **Finding the portal.** The server tells the client only of the objects the player's cell can see, and the exit portal is a
  world-database object, so the route cannot be planned in the constructor. If the portal named `PortalExit.PortalName` is in
  `State.Objects` (`WorldContentCensus.Classify` = Portal) when the player has settled at the start, the walk goes to it
  (`RouteLibrary.AcademyExit`, stopping `StopWithinMetres` = 4 m short). Otherwise the walk searches: to the room most
  doorways away, then (up to 8 legs) to the farthest room not yet visited, and the first frame the portal is in view the
  search is dropped (`RouteDriver.Abandon`) and a walk to the portal planned from where the player stands. Every portal that
  comes into view is logged (`a portal is in view`). Failing to find it is `TODO(st16-which-portal)`.
- **Use and MoveTo.** `IActionApi.Use` of the portal, from range. A server MoveToObject, MoveToPosition or TurnTo for the player
  (a non-autonomous `ObjectMotionChanged`) is given to the walker: `Controller.LoseControlToServer`, then
  `IPlayerMovementBody.ApplyServerMovement`, and the physics object's `MoveToManager` walks (AV14). The target is found through
  `SessionTargetWorld` (`PhysicsObj.TargetWorld`): the session's objects as static `ITargetable`s, and the player's id
  resolving to the walker's own body so that the MoveTo's reports reach it. Other server movements are counted
  (`ServerMovementsIgnored`).
- **Leaving.** The F751 is expected after the Use (`ExitAnnounced`): from then on the presence gate holds the walker's movement
  (it stays in the dungeon), the stage ends on a self teleport to another landblock (`ExitArrival`) with the player present and
  the Use confirmed, and `exit-where` checks the server agrees (within 0.5 m of the arrival position, outside 0x8602).
  The verdict, and the way home, are the walk's: the Academy scenario sends the character to the Academy start (not to where
  the login found it), so a run that failed in the dungeon is repaired by the next that passes.

**What ACE's world database says (landblock 0x8602, v0.9.296, read-only queries of the local backend).** Three portals.
"Exit to Holtburg" (weenie 29338, cell 0x86020169 at (158.64, -149.52, -6.06), destination Holtburg 0xA9B40019) is the only one with
a destination; it needs the quest `SentryTaskComplete`, which the Holtburg Sentry (30994, cell 0x860202C2) stamps when he is given
the Protection Orb of the Adolescent Olthoi deep in the hive. "Central Courtyard" (31061, cell 0x86020273) needs `AcademyTokenGiven`
and "Outer Courtyard" (29334, cell 0x86020331) has no restriction; neither has a destination row. Live, a character without the
quest that uses "Exit to Holtburg" from range is walked to the portal by the server (a MoveToPosition, type 7; the walker follows
it and ends 0.00 m from the server's idea of where it is, `refused-where`), gets UseDone and WeenieError 0x0474 once a second
(the portal also activates on contact) and no F751. So a character without the quest gets no F751. **Closed (5b slice C):** the scenario holds the quest for the Use, through the mod's quest verb (section 9; no test writes the database): `RoutePlay`'s `quest-check` stage asks (`has`), `quest-change` stamps it when it is missing (`ExitQuestMode.Stamp`, the default), the Use then ends in the portal's teleport (F751 to Holtburg, 0xA9B40019), and after the way home `quest-restore` asks again and brings the registry to what the scenario found (`RoutePlay.QuestFound`, `QuestChange`, `QuestPutBack`, and `QuestVerdict` for the login check's `quest=`): nothing to send when it already is (the portal's emote set, below, erased the stamped quest), a stamp for a quest that was found held (the portal erased it; only a quest held once can be put back, any other count fails loudly) and an erase for one that was not held and still is. A play that fails or is disposed after its own stamp or erase may have taken sends the opposite verb once without waiting (`TODO(st16-quest-restore)` in the log), never after an answer that said the change did not happen, never from `quest-restore`, and never a blind stamp for a quest found held (it only warns); the live tests' `finally` puts the registry back from what the scenario found (`LiveQuests.PutBackAsFound`). Each quest request is limited by the tools' own 5 s timeout (`ActionPolicy.ToolSeconds`), and the failure names it. The refusal is kept as its own path, `ExitQuestMode.Erase`: the quest is erased first (only a quest held with exactly one completion, which a stamp can put back), the Use is refused with WeenieError 0x0474 and no F751 (the refusal comes before the emote set), that refusal is the outcome, and the quest is stamped back when it had been held; a teleport in this mode fails the run. Live observations: a stamp is accepted by the portal (the Use ends in F751 about 1.4 s after the request, the MoveTo having walked the player in), and the erase that followed 14 s later (slice C's first design) answered "was not held": that is the portal's emote set, below, not an unexplained ACE behaviour. ACE sometimes announces the portal's teleport twice in a row (the Use's MoveTo and the contact with the portal each activate it): the two F751 share one portal-space visit and its LoginComplete (docs/portal-space.md, "LoginComplete"), so the login check counts visits (`LoginCheckScenario.LegTeleportEpisodes`), not announcements.

**What the exit portal does to the character (review finding; the owner's decision: graduated is the baseline).** Using "Exit to Holtburg" runs the portal's own emote set: weenie 29338, EmoteCategory 4 (Portal), emote 86996 in world database v0.9.296, which ACE calls from `Portal.ActOnUse` to `EmoteManager.OnPortal` after the teleport (ACE `Portal.cs:287`). The set (1) erases ten Academy quests (`AcademyTokenGiven`, `SentryTaskComplete`, the leggings, cap and gauntlets academy pickups, `CarpWing`, `WaspWingGiven`, `WaspWingDone`, `bellowsnewbiepickedup`, `protectionorb`), (2) sets the sanctuary to Holtburg (0xA9B40019, (84, 7.1, 94)), (3) sets `RecallsDisabled` (PropertyBool 107) to false, (4) shows a congratulations pop-up, and (5) sets `LastPortalDID` unless the portal is no-recall. ACE creates a character with its sanctuary at the start location and `RecallsDisabled` true (`PlayerFactory.cs:393-394`). So every successful exit run graduates the character for good: both test characters, `Lifestone Loopback` and `Lifestone Smoke`, have been graduated since the first passing run of slice C (the loopback acceptance test and each `--login-check=all|academy-exit` run, the smoke's included), and the mod has no verb to undo it. What the exit tests leave as found is therefore: the position (the Academy start, 0x860201AD), and the Sentry's quest as it was found (absent for both test characters; a held one is stamped again after the way home, and the run fails loudly when its count cannot be restored). What they do not restore: the sanctuary, the recall flag, `LastPortalDID` and the other nine quests. **The owner's decision: graduated is the test characters' baseline from now on.** The mod gets no verbs to put the sanctuary, the flag or the nine quests back, and no test expects an ungraduated test character.

**Tests**: `AcademyExitScenarioTests` (against `ScriptedAceSession`, which now has an "Exit Portal" that sends a MoveToObject
when the Use is out of reach and teleports the player when its positions come in reach: the walk, the Use from 4 m, the
followed MoveTo, the F751, outside, home; a Use within reach needs no MoveTo; no portal fails loudly), and
`AcademyExitLoopbackTests` (live: the acceptance test, the exit through the portal with the quest stamped for the Use, taken by the portal's emote set and verified after the way home; the refusal test, `ExitQuestMode.Erase`, whose walk, Use, followed MoveTo, WeenieError 0x0474 and server-versus-walker agreement are the old gate test; the keeper test puts the Loopback character at the Academy start from anywhere in the dungeon, and both call it, with the quest registry put back as the scenario found it, when they end; the end-state test reads that neither test character holds the quest; the relog test stamps, relogs, erases a held quest and relogs again). The scripted server plays the portal's emote set (it erases the portal's quest, moves the sanctuary, enables recalls: `ScriptedAceSession.PortalRunsEmoteSet`), so the offline tests see what live ACE does. `PortalExitWingTests` (the topology; `WingStart`).

## 26. The online flows in the game (G6 W11, slice b4)

The flows between the world and the character list, each with an engine check on the stand-in server (`--play-checks`, 56 checks: slice g0 added the picking checks) and, for the log off, a live
one. Nothing here needs a new live behaviour of ACE: a teleport is whatever F751 the server sends, and the game already turned those into portal space and a
new arrival (section 21).

| Flow | How it works in the game | Where |
|---|---|---|
| Teleport by the tools | `Tools.Place(destination)` sends `@lifestone place ...` (section 9); the mod answers and ACE teleports: F751, the new position, portal space, the arrival, LoginComplete per arrival. Play mode needs nothing of its own (`PlayerTeleported`). No UI starts it yet: the tools panel is G7 (the checks and the headless scenarios call it) | `PlayMode.PlayerTeleported`, `PlayerPlacement.Teleport` |
| Portal Use | `Actions.Use(guid)` through the Action API, as the headless Academy exit does. A server `MoveToObject` or `MoveToPosition` for the player (an out-of-range Use) is applied by the avatar set to the walker's body, which first **loses control to the server**: `PhysicsPlayerBody.ServerControl` (set by `OfflineWalker`) calls `PlayerMovementController.LoseControlToServer` for movement types 6 to 9 (re-expressing `ACSmartBox::DispatchSmartBoxEvent`, 11.4186 0x005595D0 and 11.6096 0x0055A210, which calls `CommandInterpreter::LoseControlToServer`, 11.4186 0x006B3170 and 11.6096 0x006B40B0; `RoutePlay` used to make that call itself). **Picking (G5 W7) is built** (G4 slice g0, docs/avatars.md section 18): a left click selects, a double click Uses through the same `Actions.Use` | `PhysicsPlayerBody`, `OfflineWalker` |
| The walker's skills | `PlayerRigRequest.Weenie`: play mode gives each arrival a `QualitiesWeenie` over the session's qualities (the server's Run and Jump), with the SkillTable read once at install; closes `TODO(g6-qualities-weenie)` | `PlayMode`, `PlayerRig` |
| Log off | Retail's key: **Shift+Escape, "End Character Session"** (action 0x10000026 of input map 0x10000009 in `gmDefaultMap`, decoded in the key map, `KeyMapTests`). `EndSessionKey` (Client) is registered on the retail UI's router at install; in the world it calls `Actions.LogOff(Client)` and nothing else (the Action API prints "Logging off...", sends F653, publishes `LogOffRequested`; `PortalSessionBridge` starts the machine's 3 s wait and the logoff transition, which holds the tunnel until the server's character list), outside the world it is not taken. **No confirmation, as retail:** `gmGamePlayUI::HandleKeyPress` (11.6096 0x004EAA80) sets the request, the confirmation and the end together for this action, so the key never asks; `TODO(g6-logoff-confirm)` is closed as retail-exact (M2 G4 g6). The confirmation (`ID_Client_EndCharacterSessionConfirm`, `MakeLogoutConfirmationDialog` 0x004EC330) belongs to the notice path: the options panel's "Exit to Character Selection" button asks, Yes calls `Actions.LogOff`, No stays (`GameLogout`, docs/retail-ui.md section 21.8). Closing the window still quits (`SessionHost`) | `Client/Play/EndSessionKey.cs`, `PlayMode.Install` |
| Boot, silent server | The session fails (`Booted` after F7DC, or `Silence` after `SessionTimeouts.Silence`; both in Net); the flow's `PlayerLeft` frees the player and the objects, the bridge resets portal space (`WorldExits`), and `RetailUiScreenHost` queues the disconnected screen with the lost-connection text (`CheckConnection`). The play status line says "the server booted you: <reason>" or "connection lost: <message>" | `PlayPresentationFlow`, `RetailUiScreenHost` |
| Reconnect (D43) | The disconnected screen's button (label "Reconnect") calls `IUiLoginHost.Reconnect` -> `SessionHost.Reconnect`: `GameSession.Reconnect` (a new network session, same credentials), the login again, the data check, the list. `--retail-disconnect` restores the epilogue exit | `DisconnectedScreen`, `RetailUiLayer`, `SessionHost` |

**The stand-in server for the checks.** `StandInSessions.AnswerWorld(handler, visit)` (opt-in; an offline session that does not call it refuses these actions as
before, `StandInWorldTests`) lets the offline session answer: a `place poi/loc` request (the reply line, then F751 and the position, through the reducer), a Use
of one of the visit's portal objects (`StandInObject.PortalTo`: UseDone, then the teleport) and a log off (the phases, then F653 and F658 after
`StandInVisit.LogOffDelay`). `PlayerTools.Availability` is Ready on such a session. `StandInSessions.Server(handler)` gives the levers `Boot` and `GoQuiet`
(`StandInServer`, so the game never names the net layer). `SessionHost.InstallStandIn` runs the visit, and `PlayChecks.ReplayVisit` after each reconnect.

**What `--play-checks` runs** (56 checks, about 90 s): the arrival and the scripted teleport as before; then the **tools teleport** (place Holtburg: confirmed with
the destination, the physics body at Holtburg, portal space plays, LoginComplete 3), a **portal pick and Use** (the portal projects onto the screen, a click on it selects it, a click on empty space clears, a double click Uses it: confirmed, teleported to the Academy, the tunnel, LoginComplete 4),
a **boot** (the player, the objects and the tunnel go; the disconnected screen shows "the server booted you"), the **button** (reconnects, the list returns, the
player arrives again with a new rig), a **silent server** (the same screen with the connection-lost text), the button again and a third arrival, then the
**log-off key** (Shift+Escape on the router: one request, portal space's logoff transition starts and its tunnel shows, the log off is confirmed by the character
list, the player and the objects are gone, the UI is back at character management). Judged: no frame over 50 ms from the arrival to the first teleport
(measured 38 ms). Reported by this slice (`TODO(g6-teleport-frame-time)`), judged by slice b5 (section 27: at most 12.7 ms over the teleports of the live login check; the TODO is closed): every later frame (measured at most 39 ms over all the flows).

**The live proof of the key** is the login check: `--login-check=enter` now asks for its log off with the key. `LoginCheckScenario` takes a
`requestLogOff` delegate (null keeps the direct `Actions.LogOff`); the runner passes one that presses Shift+Escape on the retail UI's router and returns the log
off the key asked for, and the verdict line ends with `logoff_by=key`. A key that asks for nothing fails the check ("the host's log-off key did not ask for a log
off"). Live (the test account, `Lifestone Smoke`, which stays at its Academy start): `done ok=true enter_to_walk_max_ms=45.2 teleports=0 academy_objects=23 logoff=ok
materialised=true objects_drawn=8 logoff_by=key`.

## 27. The full login checks (G6 W12, slice b5)

`--login-check=academy-exit|holtburg-laps|all` (the bare form is `all`) run the movement scenarios of sections 24 and 25 **with the game's own player**:
the walker, its collision streaming, the teleports' placement and the server's moves and MoveTos belong to the player controller, and the scenario only drives
its `PlayerMovementController` (`RouteDriver`) and audits what it sends. The seam is `RoutePlayHost` (Client): `RoutePlay`/`HoltburgLapsScenario`/
`AcademyExitScenario` take a `host`; `PlayMode.ScenarioHost` makes the player's sink the host's forwarder (the scenario's `MovementAudit` while a leg walks, else
the presence gate) and lets the scenario hold the movement back (`Hold`: while the character is being sent home). A hosted `RoutePlay` does not tick the walker,
apply the player's positions or server moves, or place it (play mode and the avatar adapter do); it waits for the body to be placed after each teleport.
The scenarios still build physics worlds of their own, only to plan the routes (a few seconds at start-up).

`LoginCheckScenario` takes `legs` (the `RoutePlay`s) and plays them in the new `playing` stage between the settle and the log off; its verdict adds
`Corrections`, `WhereMaxMetres`, `LegTeleports`, `ArrivalPosition`. `LoginCheckRunner` adds the engine's measurements: `holtburg_min_fps` (the slowest one-second
window of the laps' walking, judged against 60), `teleport_frame_max_ms` (every frame from the server having the player teleporting to a second after it is
present again, over the legs' teleports; each over 50 ms is logged with what it held and judged), and the verdict line is
`done ok=<..> enter_to_walk_max_ms=<x> holtburg_min_fps=<x|n/a> corrections=<n|n/a> where_max_m=<x|n/a> exit=<teleported|failed|n/a> quest=<put-back|unchanged|unverified|n/a> legs=<names|none>
teleports=<n> leg_teleports=<n> teleport_frame_max_ms=<x|n/a> academy_objects=<n> logoff=<ok|failed> materialised=.. objects_drawn=.. logoff_by=key`.
`quest` is the exit leg's registry verdict (`RoutePlay.QuestVerdict`): `put-back` (the registry was asked after the way home and is as found), `unchanged` (nothing was sent that could have changed it), `unverified` (a failure, a timeout or a cleanup sent without waiting left it possibly different: check `@lifestone quest has SentryTaskComplete`), `n/a` (no exit leg). `teleports` is the count at the end of the settle (still wanted 0); LoginComplete is wanted once for the arrival and once per portal-space visit of the legs (teleports ACE announces while an earlier one is in flight share the visit). A leg that fails sends
the character back to where it arrived and logs it off before the verdict (`StartRecovery`), so a failed run leaves the test character at its start and not
"in world" on the server.

**The exit is taken (5b slice C; `TODO(st16-exit-quest)` closed).** `all` and `academy-exit` build the exit leg with the default `ExitQuestMode.Stamp` (section 25): the character is made to hold the Sentry's quest for the Use through the mod's verb and the leg ends with ACE's F751 to Holtburg, `exit=teleported`; a refusal fails the leg, and `LoginCheckScenario.Ok` also requires the teleport when the Academy exit is a leg, so `exit=quest-refused` no longer exists in the verdict line. The refusal path is covered by the loopback test with `ExitQuestMode.Erase`. Each exit run graduates the character (the portal's emote set, section 25: sanctuary, recalls, ten quests): the leg leaves the position and the Sentry's quest as found, nothing else. The settle after a refusal (where-am-I asked up to 4 times 1.5 s apart, a walker still in its MoveTo not using up an ask: `TODO(st16-refused-where)`, the 2.3 to 2.4 m disagreement of b3 and b4, now retried unconditionally) is unchanged.

Measured live (the test account, `Lifestone Smoke`, ends at 0x860201AD): `all` takes about 135 s; corrections=0 in every run, where_max_m 0.00 to 0.27,
holtburg_min_fps 141 to 144, `teleport_frame_max_ms` 7.8 to 12.7 over 4 server teleports per run (Holtburg and back included): the teleport frames are judged
and hold, no spreading of `LandblockStreamer.Reset` was needed. **The frame that fails runs is the arrival's, not the teleports':** the frame at about one second
after Enter (the one after the player's build, tunnel up, 36 to 48 adapter events pending) measured 38 to 59 ms over 25 runs of `enter` and `all` with or without
legs; the world start after the player's build is now two frames (`PlayerPlacement.StartWorldAroundSplit`: the world's teleport, then the interiors' placement),
which moved the failures from about 4 of 9 runs to about 1 of 6. `TODO(arrival-pipelines)` covers it; it is the margin of the 50 ms rule, not a movement fault.

**The arrival's frame, settled (b5 continuation).** The slow-frame line now also carries the garbage collector's work (`gc_pause_ms`, `gc_gen0/1/2`: `GcFrameWatch`) and the
physics time, and a frame over 30 ms in the window is logged at information level ("near the 50 ms limit"). Six first runs of `enter` (5 failed, 50.0 to 56.4 ms) named the
cause: no GC, no pipeline compile and no render set-up in the slow frame; it is the frame after the player's build, and the sum is a render thread still busy with the tunnel's
first-use canvas compile (30 ms, the frame after the tunnel starts) plus 17 ms of the main thread building the player's 34 parts, which 60 Hz vsync rounds up to three
refreshes. `PlayMode.BuildPlayer` now waits `TunnelLeadFrames` (2) frames after the arrival event, so the build no longer shares those frames: five of five first `enter`
runs passed (28.6 to 36.0 ms) and `all` passed twice in a row (34.3 and 38.4 ms). The canvas compile itself stays under `TODO(arrival-pipelines)`.

**The walker's rare stall (5b slice C; `TODO(holtburg-ring-stall)`, open).** On this machine (display now 240 Hz: frames of 4 to 5 ms, `holtburg_min_fps` 217 to 240) a walker now and then stops
making progress while its keys are held: the route driver reports `Stuck` ("no progress toward goal N for 3 s", a few degrees off its heading, ground contact true). It was seen in the
game's Holtburg ring, once in the Academy wing, and offline without any server (`--offline-player-checks`, the `holtburg-lap` check, 1 of 11 runs; the cellar check's cell-tracking
failure, 1 of 11, may be the same family: both depend on the frame timing). It is not ACE, the quest or the exit. The walk check's failure line and the scenario's warning now end with
`PlayerMovementController.DescribeMotion()`: the held-key counts, the raw and interpreted forward command and speed, the velocity, the transient state, whether the player is in a cell
and whether its placement is pending, so the next stall tells a lost cell, a lost motion and a wall apart. Run after run of `all` (6) and `holtburg-laps` (8) passed in this slice with
corrections=0, so the earlier rate (4 of 10 runs of `all`) was not seen again.

**The arrival frame's canvas pipeline (G4 live-gate fix; `TODO(arrival-pipelines)` closed for the canvas).** With the shell drawing at 800x600 and the tunnel in the SmartBox viewport, the live `--login-check` showed the first tunnel frame at 75 to 83 ms with `pipelines_canvas=1` (before G4 it was 38 to 59 ms and passed). Drawing candidate canvas items one at a time while the stand-in login ran found the variant: a rounded-corner `StyleBoxFlat` panel compiles it, and with that panel drawn once at start-up the tunnel-start compile is gone (the notice panel, the labels, the tunnel screen's texture rect, a viewport picture and the shell's tiles do not account for it; which item of the tunnel's first frame asks for the same variant is not known, `TODO(arrival-canvas-source)`). `CanvasWarmUp` (RetailUi, called from `PlayMode.Install` after `ServerObjectsWarmUp`) draws that panel (4 x 4 px, nearly transparent, canvas layer -100, 3 frames) so the render server compiles it at start-up. `--play-checks` now watches the compile counters every frame (a `pipeline compilation` line per frame that compiled, with its stage and frame time) and judges `no-canvas-pipeline-is-compiled-from-enter-on` (`canvas_pipelines_from_enter=0`, on the verdict line). Before the fix the stand-in run compiled one canvas pipeline on the tunnel's first frame (112 to 142 ms, outside the judged window); after it, none.

**What `vitals=` reports (live-gate note).** `vitals=<h>/<s>/<m>` is the three *current* values the shell shows, not current over maximum: the live frame's `50/100/100` are currents. The "shell after the settle" line now also logs `vitals_max=<h>/<s>/<m>` (the formula's maximums from the session's qualities) and the session logs one `the player description's vital inputs` line per PlayerDescription (the attributes' start+ranks, the vitals' start+ranks/current, the number of enchantments the description carried and the first few). A fresh test character's PlayerDescription from the live ACE (Endurance 55, Self 55, vital ranks 0) has currents 28, 55 and 55, equal to the formula's maximums (`TODO(g4-vital-rounding)` is closed: half of 55 rounds up to 28 live); a current above its maximum (the live frame's 50/100/100 over 28/55/55) was the enchantments: the maximum now applies the registry the state keeps (Endurance and Self enchanted to 100 give exactly `50/100/100`), and the vital-inputs line also logs `maximums_with_enchantments=<h>/<s>/<m>`.

**The PlayerDescription frame (G4 live-gate fix 2).** The live rerun still logged an 80 ms frame right after the 138-enchantment PlayerDescription (`stage=arriving`, no pipeline compile, no GC). Reproduced offline: the stand-in's description now carries a live-shaped registry (`StandInPlayerStats.LiveShapedEnchantments`, 138 entries with layered duplicates) and `--play-checks` measures every frame from Enter to the arrival (`enter_window_max_frame_ms`; each frame over 30 ms is logged as `a slow frame in the enter window`). Stopwatches found the description's handlers cheap (the reducer's fill, the vital-inputs log line, `Changed` and the `QualitiesChanged` subscribers: 0.5 to 3 ms, the 3 ms being the log line's first-use compile), and the frame was the same 72 to 75 ms with and without the enchantments. It was the arrival's frame, which the shell's construction joined: `GamePlayScreen` takes 31 ms the first time (root 18, chat 3.3, options page 3.3, vitals 2, panel host 1.7, radar, character information, toolbar and selection about 2) and 7 ms the second, the difference being first-use compilation of the panels' classes; `ShellWarmUp` paints the layout only and, live, the check presses Enter within three frames of the character list, so it hardly runs. Two changes. (1) `ShellJitWarmUp` (called in `PlayMode.Install` beside `CanvasWarmUp`) compiles every method of `Lifestone.Client.RetailUi` on a below-normal worker thread with `RuntimeHelpers.PrepareMethod` (3501 methods in about 110 ms; no code of the UI runs): the shell's construction falls from 31 ms to 17 ms (the layout's 11 ms is the rest). (2) `UiFlow.QueueUIModeAfterAFrame` starts the in-game shell one frame after the player is created (`CharacterManagementScreen.OnPlayerCreated`, `PlayMode.PlayerArriving`; retail's `QueueUIMode` starts it "at the next frame", which here was the same tick), so its construction is a frame of its own and not part of the description's, the world's start and the tunnel's. Offline, first arrival, 800x600: the longest frame from Enter to the arrival was 75 ms before, 55 ms with the deferral alone and 37 ms with both (the shell's frame; the arrival frame is under 30). `--play-checks` judges it (`no-frame-over-50-ms-from-enter-to-the-arrival-with-a-heavy-player-description`) and the heavy description's maximums (`the-description-s-enchantments-are-kept-and-applied-to-the-vitals-maximums`). The live run's extra 10 ms is the same costs on a slower frame; the enchantments were not it.

**The warm-up's frame and the shell's frame (G4 live-gate fix 3).** The live rerun (vitals right, no ERROR line) still failed the 50 ms rule: 52.8 ms (`login-check`) and 59.6 ms (`login`) at about a second after Enter, `stage=arriving`, `tunnel=True`, no compile, no GC. Verified: live, character management lasts about 0.2 s before the check presses Enter (the character list at :59.074, the shell warm-up's classes at :59.112 to :59.155, Enter at :59.158, the shell at :59.270), while the offline stand-in used to answer Enter 3 s after its list, a second after the screen showed, so the warm-up had finished and the checks never saw it. Reproduced offline: `StandInVisit.HoldEntry` (the stand-in server waits to be released) and `--play-checks` releases it 0.1 s after character management shows, as a live server answers; the new judged check `enter-right-after-character-select` covers every frame from three frames after the list (the login check's own settle) to the arrival, and from the arrival to the first teleport (`list_to_walking_max_frame_ms` on the verdict line). Before the change that run showed 42.7 and 38.7 ms (the two frames before the shell is made and the one that makes it), the second logged by the layer as `a slow retail ui step`: update 20 ms (the shell's construction), warm-up 4 (deleting the half-built warm-up tree), composite 7 (the 8 ms slice) and copy 12 (the first upload of the whole screen's tiles), 44 ms of main-thread UI work in one frame, plus the rest of the frame; live that is 52.8 and 59.6. The warm-up's own one-call build was a frame of 94 ms at character management (outside the judged window by the settle, but a visible hitch).

The 52.8 ms frame with `process_ms_previous_frame=17`: that monitor is not read per frame (two consecutive frames in the logs show the same value, and the same stale 96.9 ms showed in offline lines), so 17 ms says nothing about the render side. The offline breakdown accounts for the frame on the main thread alone: it is the shell's frame (tunnel up, `rig=False`: the frame between the tunnel's start and the player's build), whose margin was never large: section 27 records 38 to 59 ms for it before G4 (the frame after the player's build, tunnel up, 36 to 48 adapter events pending), brought to 28 to 38 ms by `TunnelLeadFrames`, and G4 put the shell's construction, paint and upload into it.

The fix, at the root, so no frame anywhere costs that much (docs/retail-ui.md section 21.10): the warm-up builds the shell a few elements at a time (`UiStagedRoot`, 4 ms a frame) and paints in 2 ms slices, starting at the first login screen; it is dropped, not deleted, if Enter comes first; the frame that builds the shell does not paint or upload, and the paint and upload slices after it are 3 ms (`ShellPaintSliceMilliseconds` 8 to 3, `ShellCopySliceMilliseconds`). Offline, 800x600, Enter 0.1 s after the character list, longest frame from the list to the arrival: 42.7 ms before, 21.4 to 21.8 ms after (34.6 ms in the run before the build frame stopped painting; the build frame is now 17 to 20 ms of retail UI work); arrival to teleport 22 to 30 ms (the player's build and the cold paint). Two further rules fell out: the shell is never marked painted in the frame that built it (that frame skips the composite, which is also what syncs the compositor with the new surfaces), and the tunnel-viewport check waits for its frames. The cost is visible only as a few more frames of the backdrop before the world shows when Enter beat the warm-up (cold paint 43 frames, 234 ms; 5 frames, 35 to 50 ms when it finished first). Not run live: the lead's rerun is the proof. The false `TODO(retailui-runtime)` lines for shell windows G4 builds under another name are gone (`GamePlayScreen.ControllerClasses`).

**The shell is judged (G4 slice g7).** Four fields end the verdict line (additive to section 4.14's; the earlier fields and their order are unchanged), all judged: `... logoff_by=<key|action> shell=ok|missing vitals=<h>/<s>/<m>|empty spew_portal=yes|no vitals_window=stacked|side`.

| Field | Meaning | Fails the check when |
|---|---|---|
| `shell` | the in-game shell (`GamePlayScreen`) was up after Enter, painted in time (not a timed-out paint) with its root shown, and no frame showed the world without it painted (`ShellWatch.ShellUp`) | `missing`: it never came up, its paint timed out, or a frame showed the bare world |
| `vitals` | the current health, stamina and mana the shell's chosen vitals window shows: read from the meters and labels on screen (`VitalsWindow.ReadLevels`, the first full reading; label and meter must agree), not from the session's qualities | `empty`: a meter or label never filled (the scenario separately fails at the settle when the description left a vital missing or without a maximum; the qualities' string stays in the detail line's `vitals_on_screen`/`vitals` fields) |
| `spew_portal` | the spew box held portal space's notice ("In Portal Space - Please Wait...") in some frame | `no` |
| `vitals_window` | the vitals window the shell showed, which the character's option (`SideBySideVitals`, option bit 21) chooses: `stacked` (the default) or `side` | it differs from the window the option chooses (the value is also the owner's answer to "which window did this character pick") |

`LoginCheckScenario` samples `Vitals` and `VitalsWindow` at the end of the wait from `state.Self.Qualities` (the `vitals=` field of the verdict is `ShellWatch.VitalsOnScreen` when the shell exists, R5); `LoginCheckRunner` adds `ShellWatch`'s facts (it steps every frame; `--play-checks` runs the same watch on the stand-in session, so the wording and the judgement are proved without a server) and the `detail` line gains `shell_frames`, `shell_bare_world_frames`, `vitals_window_chosen` and `radar_blips_max`. Client tests on the scripted server (`LoginCheckScenarioTests`): the vitals and the stacked window from its description, the side window from the option, and a description with empty vitals failing the check at the settle.
The smoke `login` step now wants four frames and `shell=ok` and `spew_portal=yes` in the verdict. Not run live in this slice (no backend): the lead's gate runs `--login-check` and reads the four fields.

**The shell's review frames: `--play-frames[=DIR]`.** Slices g1 to g6 saved their frames with an unregistered environment variable; it is now the option `--play-frames[=DIR]` (the Play mode group, `PlayOptions`), valid with `--play-checks` only. Default folder `docs/screenshots/m2/shell/<W>x<H>` (gitignored; derived from the dats, never committed). A run with it saves ten named frames (docs/retail-ui.md section 21.9), checks each is the window's size and not blank, prints `shell frames done size=<W>x<H> scale=<s> captured=<n> failures=<f> frame_time=reported` and, because the read-back stalls frames, reports the 50 ms judgement without judging it, so a frames run can pass. Smoke step `shell-frames` runs it at 800x600 (retail's native size, D57) and, with `-AllSizes`, also at 1920x1080 and 2560x1440.

**G7 and the shell's frame.** Lifestone's key bindings (`LifestoneHotkeys.Bindings`, F7 and Scroll Lock) are now added to retail's when the layer is created (`RetailUiLayer.Create`) and no longer in `LifestoneHotkeys.Attach` on the frame that builds the shell, where `RetailKeyBindings.With` rebuilt the whole table (1.1 ms and an allocation burst), and Lifestone's embedded layouts are read and registered on a worker at the same time (`UiLayouts.WarmEmbeddedLayouts`, 20 to 25 ms, which the first F7 used to pay on a frame): offline, 800x600, the shell build frame's `update=` went from 21.6 to 25.5 ms (G7 before) to 21.2 to 23.5 ms (after), against 19.6 to 22.2 ms at 2a93b95 measured the same day on the same machine, and the stand-in's teleport frames fell back from 61 to 74 ms to 29 to 43 ms.

**The shell is built over frames (G7 live fix 2).** The live rerun still had one frame over 50 ms (60.9 ms, `a slow retail ui step update=28.3` with a gen2 collection) because Enter beat the warm-up and the real shell, 21 to 29 ms, was built in one call. `GamePlayScreen` now makes its root through `UiStagedRoot` (the live host sets `UiLoginContext.StageShell`; tests and the stand-in layers still build at once) and then wires its windows a controller at a time (chat, vitals, radar, panel host, options, toolbar, selection, indicators, tools), at most 3 ms a frame (`BuildBudgetMilliseconds`; a frame is that plus one unit, 6 ms at worst offline) while portal space plays; the world stays hidden behind the backdrop until the shell is built and painted (`IsBuilt` gates `TrackShellPaint`, so `shell_bare_world_frames` and `bare_world_frames` stay 0). The dropped warm-up's partial tree is not reused: it belongs to a second element runtime, so only its caches (images, glyphs, descriptions) carry over. The portal notice that arrives before the spew box exists is held by `UiNoticeRouter` (`ExpectWindows`, up to 16) and shown when the box registers (`spew_portal=yes`); `PortalNoticeLine` hands it to the router instead of showing its placeholder line. Offline, `enter-right-after-character-select`: `list_to_arrival_max_ms` 33.0 before, 13.3 to 20.5 after; the shell build is 3 to 9 frames; the new check `no-ui-step-after-enter-is-over-12-ms` (the whole layer step, Enter to the first teleport) reads 7.7 to 7.9 ms.

**The G7 gate and the burst frame (`TODO(login-burst-frame)`).** The live gate on c72ef44 (`smoke.ps1 -Backend require`) passed every step except one `login-check` run: its frame at the world-entry message burst (`stage=entering`, 1.3 s after Enter, before the tunnel; no collection, no pipeline compile) took 53.0 ms, over the 50 ms rule. Two reruns of `login-check` and `login` measured 43.3 / 32.6 ms and 37.5 / 31.8 ms and passed, and the lead accepted the gate on the reruns. The frame is the one that handles ACE's world-entry burst (the PlayerDescription with 138 enchantments, the first CreateObjects and their log lines) and it sits near the 50 ms limit live; profiling it is the named TODO `login-burst-frame`. It is recorded here as it happened, not as a pass: one failed run in the gate, two clean reruns.

**Wave 3 (M2 W3, lanes A to C): what the login check now judges.**
- **The review frames are 4** (`01-portal-tunnel`, `02-arrival-among-academy-objects`, `03-shell-after-the-settle` and `04-character-list-after-logoff`; the run fails with "only N of the 4 review frames were saved"). The verdict line of `login-check` and `login` gains, before `logoff_by=`: `academy_undrawn=<n>`, `holtburg_objects_drawn_max=<n|n/a>`, `holtburg_npcs=<drawn>/<placed>`, `holtburg_max_frame_ms=<x|n/a>`, `tunnel_seconds=<x>`, `logoff_tunnel=held|dropped|never-shown`, `portal_sounds=ok|bad`, `sound_enter=<n>`, `sound_exit=<n>` and `sound_hum=<n>`. Smoke's `login-check` and `login` steps demand `logoff_tunnel=held` and `portal_sounds=ok`.
- **The portal rules (DoD 5).** Portal space lasts at least 5 s on the arrival (`portal_seconds`: the machine not Off, from the create to the end of the world's expansion; 1 s expand, 2 s hold, 1 s collapse, 1 s world expand, docs/portal-space.md section 3) and the tunnel is visible at least 4 s of it (`tunnel_seconds`; see "The tunnel is shown from its first frame" below); the logoff tunnel is held from its first showing until the retail character list (`logoff_tunnel=held`, D28; a gap of at most 0.25 s is allowed); the enter and exit whooshes play once each and the hum at least once (`portal_sounds`, counted from the presenter's cues and the director's tunnel events: the check does not hear the sounds, `TODO(record-audio-check)` stays open and the owner hears them in the visible review). The stand-in's `--play-checks` gained `the-logoff-tunnel-is-held-until-the-character-list`.
- **The census (DoD 6).** At the Academy settle the check compares every placed object of every kind (portals, doors, lifestones, signs, corpses, items, creatures) with the layer's drawn guids, waiting up to 8 s, and fails on a mismatch. An object is excused, and counted by reason in the `academy_drawn_census` log field, when it is hidden or NoDraw, translucent 1 or more, has no part to draw, or sits in an interior cell that is not shown; an object with no avatar or no cell is reported as missing. The Holtburg laps leg takes the same census every 0.5 s and fails when no NPC is drawn; `holtburg_objects_drawn_max`, `holtburg_npcs` and `holtburg_max_frame_ms` are reported, not judged beyond that.
- **Movement (DoD 8).** `LoginCheckRunner` writes each movement leg's `MovementAudit.ToJson` to `cache/movement/<UTC yyyyMMdd-HHmmss>-<leg>.json` (gitignored). The hosted Academy exit leg's where-am-I waits for the game's physics body to be placed outside the dungeon and records the body's position against the server's `where`; unhosted runs still compare the teleport's announced position. `HoltburgLapsScenario(..., jumps: true)` (the login check passes it) adds a jump leg after the laps (`JumpLeg`): a standing jump (0.5 s charge) and a running jump (1 s run-up, 0.5 s charge) through the player's own intents, then where-am-I, folded into the movement verdict. `TODO(jump-live)`: the Jump action's 56-byte pack and ACE's answer have not run live; ACE is expected to answer with a vector update, so the audit's `unmatched` rises by one per jump (reported, not judged). `HoltburgLapsLoopbackTests.A_standing_and_a_running_jump_after_the_laps_are_accepted_with_no_corrections` is the first live run.
- **Run by default.** `PlayMode.RunByDefaultOf` reads the character's ToggleRun option (word 1, mask 0x400; true until the PlayerDescription is known) and gives it to the rig as `PlayerRigRequest.RunByDefault`. The option is read when the rig is built, so a later toggle in the options panel does not reach it. `TODO(g5-run-by-default)` is logged only while the request stays true.
- **The burst frame (`TODO(login-burst-frame)`).** The slow-frame lines of `[logincheck]` ("frame over 50 ms between Enter and walking" and "frame near the 50 ms limit between Enter and walking") carry the literal `TODO(login-burst-frame)` and the parts of the frame before the one the engine's `ms` measures: `session_tick_ms`, `pump_messages`, `pump_ms`, `retail_ui_step_ms`, `play_mode_step_ms`, `play_mode_work_ms`, `play_mode_adapter_ms`, `play_mode_events_applied` (with `adapter_pending`). The verdict repeats the TODO once as a Warn line with the worst frame's parts when any frame passed 30 ms. `GameSession.LastPumpMessages` and `LastPumpMilliseconds` and `PlayMode.LastStepMilliseconds`, `LastWorkMilliseconds`, `LastAdapterMilliseconds` and `LastEventsApplied` are read-only properties for it, and the Debug line "play mode took long this frame" is now Information and also carries `session_tick_ms`. Bounding the burst is still open: DoD 15 is reported as met on N of M gate runs until these numbers say what to bound.
- **The stamp rule.** The quality stamp rule in `Session/State/GameStateReducer.Chat.cs` follows `WTimeStamper::UpdateTS` (docs/third-party/session.md).
- **Not run here:** the tools panel's Places and Where am I (`--tools-check`) are done by hand in the visible-window review.

**The 51 ms message was the first PlayerDescription's compilation (M2 W3 gate fix; `TODO(login-burst-frame)` closed for it).** The live gate's one failing frame (`stage=entering`, 1.29 s after Enter, `session_tick_ms=51.193 pump_messages=1 pump_ms=51.185`, every other part near 0) was one message, the graduated test character's PlayerDescription (138 enchantments). Measured offline with `PlayerDescriptionCostTests` (a live-shaped description of 16.5 KB: 138 layered enchantments, 400 spells, 49 skills, 120 ints and the other tables, 18 shortcuts, 8 spell bars, 60 inventory entries, built with the test writers and run through the real decoder and the real reducer, in a fresh process): **the first decode took 21 ms and the first reduce 16 ms (37 ms), the second 0.23 ms for both.** So the size of the message and the number of enchantments were never the cost; the stand-in's description never ran the decoder (it builds the decoded record), which is why the offline runs could not show it. It is first-use compilation: the decoder reads its tables into `ImmutableDictionary<uint, T>` over eight value types (each its own generic instantiation to compile) and the reducer's vital-inputs log line is LINQ over `ImmutableArray` of structs; live, with Godot's debug runtime beside it, the same compilation took 51 ms on the game thread. The fix is at the root: `SessionJitWarmUp` (Client, `Session/`) starts on a below-normal worker in `PlayMode.Install` and (1) decodes a small well-formed PlayerDescription (`WarmUpMessages.PlayerDescription`: one entry in every table, a skill, a spell, two enchantments, a shortcut, a spell bar, a component, an inventory and an equipped entry) with the real decoder and reduces it in a throwaway reducer over a state, bus and clock of its own (nothing shared with the live session but static code), then (2) prepares every method of `Lifestone.Net.Messages`, `.Dispatch`, `.Wire` and of `Session.State`, `.Bus` and `.Contracts` with `RuntimeHelpers.PrepareMethod` (about 2400 methods; the other messages of the burst); it logs `the session's message path compiled in the background` with `sample_description_decoded`, `methods`, `run_ms` and `ms` (35 to 100 ms; the login takes seconds to reach the world). Offline, with the warm-up run first, the first live-shaped description costs 1.5 ms to decode and 2 ms to reduce (3.5 ms against 37 ms), 0.23 ms warm; `PlayerDescriptionCostTests` judges the warmed cost under 10 ms and that the sample decodes (`sample_description_decoded=false` in the log means the sample no longer matches the decoder). **Attribution stays on:** `IRawMessageTap.OnDispatched` (Net, called by `SessionMachine.Dispatch` for any inbound message whose dispatch took 5 ms or more) gives the session its opcode, event type, total milliseconds and the milliseconds up to the reducer's first call (decode); `GameSession` logs one `a server message took long on the game thread (TODO(login-burst-frame))` WARN per message type with `total_ms`, `decode_ms`, `reduce_and_subscribers_ms` and `subscribers_over_1ms` (`Type.Method(Event)=ms` for each bus handler that took 1 ms or more while the message was handled, kept by `EventBus` in a fixed table with two clock reads per handler and no allocation unless something is slow). If a later live run still has a slow message, that line names it; the Game-side subscribers (`PlayMode`, the avatar adapter) are not in the warm-up's sweep and would show there.

**The tunnel is shown from its first frame (M2 W3 gate fix; DoD 5).** The live gate's `tunnel_seconds` of 4.4 had two causes. (1) The rule was wrong: portal space's login timeline is 1 s expand, the wait for the destination, a hold of at least 2 s, a 1 s collapse and then 1 s of the world expanding, so the tunnel itself is visible 1 + 2 + 1 = 4 s at least and the "at least 5 s" is the whole sequence (dod-5.md: "1+2+1+1"). The live log: Tunnel at :14.604, LeavingTunnel at :18.013, Arriving at :19.014 (tunnel 4.41 s), Off at :20.018 (sequence 5.41 s). `LoginCheckRunner` now judges `portal_seconds >= 5` (the machine not Off from the create until the arrival ends, before the settle) and `tunnel_seconds >= 4`, both on the verdict line. (2) The tunnel was in fact covered at the start: its own layer is below the retail UI's (`PortalTunnelView.TunnelLayer` -1, the UI at 10) and the UI's black backdrop, which holds the world while the staged shell is built and painted (`ShellHoldsWorld`), covered it until the shell's viewport existed (about 0.16 s live, 65 frames offline, 1 s on the stand-in) although the world behind the tunnel is hidden by portal space in any case. `RetailUiLayer.TunnelShowing` (set by `Main` to `portal.View.Shown`) makes the backdrop yield while the tunnel shows and the shell holds the world; `WantsWorldHidden` and the world's hide request are unchanged, so `bare_world_frames` stays 0, and the tunnel's own full-window picture draws until the shell's viewport takes over (`DrawnByViewport`). Judged from the engine: `tunnel_covered_frames` (frames in which the tunnel layer showed while the backdrop was drawn, over the arrival) is on the verdict line and fails the check above 1 (the one frame is the order of the presenter and the layer inside a frame); `--play-checks` judges `the-arrival-tunnel-shows-from-its-first-frame-and-the-sequence-lasts-5-seconds` (the stand-in: `portal_seconds=6.37 tunnel_seconds=5.37 tunnel_covered_frames=1`; with the line in `Main` removed the same check read 65 covered frames and failed).

**The materialise fade, drawn (M2 decisions follow-up, slice f2; D45 and D63).** Docs/avatars.md section 17 has the draw (the blended twin, no depth write, the hand-over between batches, the warm-up).
What the checks say about it:
- **`[logincheck] ... materialised=<..> fade_ms=<x|n/a> objects_drawn=..`** (`fade_ms` is new; additive). `LoginCheckRunner.SampleFade` reads the player's draw every frame: hidden frames, then from the first frame
  the player is visible after the unhide the translucency must fall through at least five intermediate frames to 0, and `fade_ms` is the time from that first visible frame to the first opaque one. It is judged
  between `LoginCheckRunner.FadeMinMilliseconds` (650) and `FadeMaxMilliseconds` (900): the hook is 750 ms from the update after the unhide, so the measure reads 750 plus one to two frames, and a frame can be up to
  50 ms at either end. A fade that never shows (`n/a`), an opaque frame before any intermediate one (the pop-in, or retail's re-hide flash) or fewer than five intermediate frames fails the run. The 50 ms rule is the
  existing one (every frame from Enter to the end of the wait, which holds the fade). The smoke `login-check` and `login` steps need no change: they match `done ok=true` and the fields they already read. The detail
  line carries `fade_ms`, `fade_intermediate_frames` and `fade_popped`. The review frame `02a-materialising` (the player at about half translucency) is saved beside the other four in
  `docs/screenshots/m2/login/` (gitignored); the run needs only the four it already demands.
- **`--play-checks`** (stand-in, no backend) has `an-unhidden-object-and-the-player-pass-through-intermediate-alpha-and-end-opaque-with-no-frame-over-50-ms`: `Stand-in Apparition`
  (`PlayChecks.ApparitionGuid`; `StandInObject.UnhideAfter` makes the stand-in server create it hidden with the human's script table and unhide it with the player) and the player are watched frame by frame
  (`PlayChecks.FadeWatch`): hidden first, at least five frames at intermediate alpha, never rising, never drawn opaque before the fade, 750 ms within 100 ms plus a frame, and no frame over 50 ms in the window (reported,
  not judged, in a `--play-frames` run, whose read-back stalls a frame). The same run with `--play-frames=DIR` saves `DIR/extra/02a-materialising.png` (both figures about 60 % translucent: see-through with the Hidden
  script's bubbles); the shell's ten frames are counted by file and are unchanged. Measured on the stand-in: `fade_ms=749/749` (player/object), the longest frame in the window 4.2 ms (offscreen, 240 Hz).
- **Option `--retail-unhide-flash`** (Play mode group): see docs/avatars.md section 17.

## 28. The throwaway leg: `--login-check=throwaway` (M2 decisions follow-up, slice p2; D59, D61, D65; DoD 3 and 10)

A live check that a character can be created, used, deleted, restored and purged through the retail screens and the tools panel, on the test account, leaving the account as it
was. It is a kind of its own: **never part of `--login-check=all`** (the bare form stays `all`), with its own script (`ThrowawayLegScenario`), its own node
(`ThrowawayLegRunner`, hooked by one line at the top of `LoginCheckRunner.Install`) and its own verdict. It needs the dats, the backend, the test account, the retail UI and the
server mod installed **with the purge verb** (`@lifestone char purge`, slice p1; an older mod answers `unknown-verb`, and the leg says so and fails).

**The sequence** (stage names are in `ThrowawayLegScenario.Stage` and begin every failure message):

| Stage | What happens |
|---|---|
| `login`, `inspect` | The character list is reached and remembered (`Baseline`). The account must hold a ready `Lifestone Smoke`. A `Lifestone Throwaway` already on the list is a **leftover** of a failed run: if it is not pending deletion it is selected and deleted through the screen (`leftover-select`, `leftover-delete`), then `Lifestone Smoke` enters, purges it and logs off (`leftover=yes`); the baseline is the list without it. |
| `chargen-open` | Create, the Aluvian heritage, Next through profession, skills, appearance and start town to the summary page, all by clicks (as `RetailUiFramesRunner` does). |
| `name-taken`, `name-invalid`, `name-throwaway` | On the summary page the name is typed and Finish pressed three times. `Lifestone Smoke`: ACE answers NameInUse, shown in a retail message dialog that is read and dismissed. `Drudge Skulker` (`ThrowawayLegScenario.InvalidName`): the client's only name rules are the 32-character limit and the trim, so it sends the name; ACE's `creature_name_check` (on by default) finds a creature of that name in the world database (weenie 7) and answers NameBanned, in another retail dialog. `Lifestone Throwaway`: accepted; the client logs the new character in at once (D65). A refusal that does not come (ACE accepting either name) fails the leg and the recovery deletes the stray character. |
| `arriving`, `settling` | The server unhides the player, who must be the throwaway; 3 s on the session clock. |
| `tools-open`, `tools-where`, `tools-xp`, `tools-place`, `place-close`, `place-leaving`, `place-arriving` | The tools panel by F7. Where am I: the metres between the server's position and the client body's (`where_m`, within 0.5 m). Add XP 2,500 (D61): the scenario refuses to ask for it unless `Self.Name` is the throwaway, and the driver checks again before it presses the button, then reads the server's reply from the chat. A Places teleport (search `holtburg`, click the first place) through portal space, confirmed by the player's arrival in Holtburg's landblock (0xA9B4). |
| `logging-off`, `char-select`, `char-delete`, `char-restore` | Log off (retail's Shift+Escape) to the list. The throwaway is selected and deleted (Delete, the typed word `delete`, Done, Please Wait), selected and restored, selected and deleted again: `deletes=2 restores=1`. |
| `entering`, `smoke-arriving`, `purging`, `logging-off` | `Lifestone Smoke` enters (it only enters and leaves: a pending character cannot enter the world), `PlayerTools.PurgeCharacter("Lifestone Throwaway")` and its reply (a character record or a player biota removed), and a log off. |
| `quitting` | The list after that log off equals the baseline (name and pending flag, in any order; ACE sends the list at the log off, so the comparison reads it there), then quit. |

**Failure and recovery.** Every failure names its stage. From the first moment the leg may have changed the account, a failure runs a recovery before it is reported: a client
that is in (or entering) the world logs off; then `Lifestone Throwaway`, and a character made under the invalid name that was not on the list at the start, are deleted if they
are ready (pending deletion is what the next run's leftover path purges). It never touches any other character, graduated or not. The host's time limit (600 s, then 120 s for the
recovery) goes through the same path (`ThrowawayLegScenario.Abort`). The process ends with 0 only when the leg held.

**The seam.** The scenario never clicks: it acts through the Action API and `PlayerTools` where the screens are not the subject (select, Enter, log off, quit, the purge) and through
`IThrowawayScreens` where they are. Each member starts a `ScreenOp` and returns; the scenario advances it each frame and looks at `Done`, `Failure`, `Text` (a refused name's dialog text)
and `Number` (the Where metres, the XP added). The game's driver (`ThrowawayScreens`, in `src/Lifestone.Game/scripts/Session/`) runs each op as a frame-stepped coroutine
(`ScreenFrameOp`) over the shell's own input router: the pointer moves, two frames pass, the button goes down and up. A test supplies a fake (`ThrowawayLegFakeAccount`).

**The verdict.** One line, `[logincheck] throwaway done ok=<true|false> throwaway=<purged|failed> name_errors=<n> xp=<added|n/a> where_m=<m|n/a> place=<teleported|n/a> deletes=<n> restores=<n>
list=<same|different|n/a> leftover=<yes|no> enter_to_walk_max_ms=<x|n/a> teleport_frame_max_ms=<x|n/a> slow_frames=<n>`. Judged: `throwaway=purged name_errors=2 xp=added where_m<=0.5
place=teleported deletes=2 restores=1 list=same`, and no frame over 50 ms in either window (from the new character's Enter to the end of its 3 s settle, and from the Places click to a second after
the player is present again; every frame over 30 ms is logged with its stage). No name appears in the verdict (names are in the `throwaway:` log lines only).

**Run it live** (the lead only; the backend running with the new mod): `pwsh -File tools\smoke.ps1 -Backend require -Only login-throwaway`, or
`pwsh -File tools\godot.ps1 -Run -Offscreen -IsolateUserData -TimeoutSeconds 840 -Resolution 800x600 -GameArgs:'--account=test --login-check=throwaway'` (840 s: the runner aborts the leg at 600 s and allows 120 s more to recover, so the host's kill comes after both, with start-up and shutdown inside the margin; smoke's step uses the same 840). The smoke step is skipped under
`-Backend auto` and `start` (it creates and purges a character), and runs, after `login-check` and `login`, under `-Backend require`.

**For a video.** `--video-pace=S` (0 to 10 s, the main scene's video options) holds each character-creation page, and each typed name before Finish, for S seconds with the pointer out of the window (no tooltip), so a screen capture shows them (the M2 hero video used 1.8 with `--clean-hud`). Nothing else in the leg changes; the holds fall outside both judged frame windows.

**Stray rows and the name dialogs.** A leftover is either `Lifestone Throwaway` or a character under the invalid name (only created if ACE accepted it): each live one is deleted through the screen, then each is purged from
`Lifestone Smoke`. A name is purged until the verb answers `unknown-character` (at most five purges, `ThrowawayLegScenario.MaxPurges`), so an old expired row of the same name that the list never shows is cleared too; the verb
takes the visible pending row first (`CharPurgeRules`). The invalid-name step fails when its dialog says the same as the taken-name dialog (NameInUse) or nothing: it must be the invalid-name refusal.

**Offline.** The scenario's whole script is tested against a scripted account (`ThrowawayLegScenarioTests`: the happy path, a leftover ready and pending, the Add XP guard, the list
comparison, every stage's failure with its recovery, the host's abort). The screen driver is run against the stand-in's real screens by `--play-checks`
(`PlayChecks.Throwaway.cs`): the tools ops in the world (open, Where, Add XP refused for another name and pressed for the right one, the place click, close) and, once the log off is back at the
list, character creation to the summary, the taken name, the banned name (the stand-in answers NameBanned for `InvalidName` only), a free name, then select, Delete, Restore, Delete. **What the stand-in cannot
serve is the whole leg**: it enters only its first character (a scheduled visit released by Enter), so entering the new character and then the graduated one by an action is missing, and its log
off sends the default list, not the reducer's current one (a created character would be lost; only a purge is carried, through `StandInSessions.BeginLogOff(..., purged)`). Building those is a
stand-in rewrite (entering any listed character, a world per character, a log off list from the reducer), not a slice-sized extension; it is the named gap `TODO(throwaway-standin)`.
