# Third-party sources: M2 net track

The net track ([m2-plan.md](../milestones/m2-plan.md) §3.3 "net", §4.6) builds `src/Lifestone.Net`: the session, the
inbound dispatcher, the raw tap, the loopback gate, the wire records and, from N1 on, OpenAC's reliable transport and
the message codecs. It also owns the 11.6096 motion command table in `src/Lifestone.Data/Animation`. Lifestone is
AGPL-3.0, and every source below is compatible with that. Nothing derived from the user's dats or exe is committed.

It covers every step that ports or re-expresses code: the contract stage (N0), the transport import (N1), the transport
fixes (N2), the login codecs (N3), the fake server (N4), the session (N5, N6), the world, movement, chat and event codecs
(N7), the robustness work (N8), the motion command table's checks (N9) and the player's own state (N10). N13 completed
it: the defects fixed, the files not taken and the licence texts are at the end. The live phases N11 and N12 port
nothing; the fixtures N11 captures from the client tap stay under `cache/net-fixtures/` and are never committed (owner
decision of 2026-10-12).

`Hygiene/NetProvenanceTests.cs` (N13) keeps this page honest: every file of the track whose first line is a port header
cites OpenAC @1ee5a08 or ACE @95ef426d (DatReaderWriter @a9345f2 for the command table alone) and is named here with
that source, and `src/Lifestone.Net/THIRD-PARTY-NOTICES.md` names no file that is gone or holds no OpenAC code.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| ACEmulator/ACE | AGPL-3.0 | v1.78.4816 @95ef426d (`vendor-eval/ACE`); its `ACE.Server/Network` tree is identical to the @7715c052 docs/protocol.md was written from | **Ported:** two enums (below), and the test-only `CryptoSystem` oracle of N4. **Reference:** the message layouts of docs/protocol.md §5, which the record shells follow field for field (`GameMessage*.cs`, `GameEvent*.cs`, `Motion/*.cs`, `Structure/RestrictionDB.cs`); the writers and handlers the N3, N7 and N10 test helpers mirror (`AceWireWriters`, `AceClientReaders`), including `WorldObject_Networking.SerializeCreateObject`, `Motion/*.cs`, `Structure/JumpPack.cs`, `GameEventPlayerDescription` and the `GameAction*` handlers of the world actions; the network code the N4 fake server reproduces and N8 works around (`NetworkSession.ProcessPacket`, `Update`, `ClientPacket.VerifyCRC`); `ACE.Entity/Enum/MotionCommand.cs`, against which N9 reconciles the command table; nothing else copied. |
| Chorizite/DatReaderWriter | MIT; Copyright 2024 ACClientLib | NuGet 2.1.9, built from @a9345f2 (`vendor-eval/DatReaderWriter`, a40acb4, has no file changes) | **Ported:** the motion command values and names (below). |
| eriknihlen/OpenAC | MIT; Copyright (c) 2026 Erik Nihlén and OpenAC contributors | v0.1.24 @1ee5a08581a7f4b0b22effdf911171a76cf4391c (`vendor-eval/OpenAC`, checked out at 1ee5a08, clean tree; v0.1.25 @6db05a2 is fetched there as the tag `watch-2026-10-19` for reading only, docs/research/foundation-openac.md §6) | **Ported (N1):** the reliable transport of `src/AcDream.Core.Net` (crypto, packets, transport; below), its 16 tests that need no ACE model, and the owning packet decoder and LoginRequest parser as test-side readers. **Partly ported (N2):** `NetClient.cs`'s socket setup, in `UdpLoopbackSocket`. **Ported (N3):** the login-phase message codecs and parts of two of their tests. **Partly ported (N4):** the transport tests that need no `WorldSession`. **Ported (N7):** the world, movement and chat codecs, with defects 1 and 2 fixed (below). **Changed (N8):** five of the ported transport files, for the tail probe, the held acks and the rejected admissions. **Ported (N10):** the PlayerDescription and PrivateUpdate parsers, decoupled from `AcDream.Core`. Each product file is also named in `src/Lifestone.Net/THIRD-PARTY-NOTICES.md`; both that file and this page (below) reproduce the MIT licence. |
| Mag-nus/acclient-AI-RE, `2013-09 11.4186` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | fetched 2026-10-12 | Function names and addresses cited below; the N3 layouts were checked against single files of `acclient-src/src/` (`ACCharGenResult.c`, `pUI.c`, `CharacterSet.c`, `CharacterIdentity.c`, `CLCache.c`, `IterationList.c`, `CliDatEvents.c`, `Archive.c`, `PlayerSystem.c`, `UI.c`, `CM_Login.c`, `CM_Character.c`, `CM_Communication.c`), the N5/N6 session behaviour against `ClientNet.c`, `pUI.c`, `PlayerSystem.c` and `CM_Character.c`, the N7 layouts against `CMovementManager.c`, `CMInterpPrim.c`, `CMovementDefs.c`, `CPackClasses.c`, `CPhysicsDesc.c`, `CPhysicsObj.c`, `CPhysics.c`, `ObjDesc.c`, `PublicWeenieDesc.c`, `SmartBox.c`, `ACSmartBox.c`, `CM_Physics.c`, `CM_Movement.c`, `CM_Communication.c`, `CM_Inventory.c` and `CM_Combat.c`, and the N10 layouts against `PlayerSystem.c`, `CACQualities.c`, `CBaseQualities.c`, `AttributeCache.c`, `Attribute.c`, `SecondaryAttribute.c`, `Skill.c`, `CSpellBook.c`, `SpellBookPage.c`, `CEnchantmentRegistry.c`, `Enchantment.c`, `PlayerModule.c`, `ShortCutManager.c`, `GenericQualitiesData.c` and `CM_Qualities.c`. Nothing copied. |
| Mag-nus/acclient-AI-RE, `2015-10 11.6096/docs/symbol_transplant_11.6096.tsv` | reference only | blob f0b2aae95c687ece822638208e7152821e2744a3 (docs/research/retail-addresses-m2.md) | The 11.6096 addresses cited below. |
| Mag-nus/acclient-AI-RE, `2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md` | reference only | fetched 2026-10-12 | The four commands 11.6096 added and the +3 shift from ordinal 0x10F, used to complete the command table. |

## Files

### Ported (each header names its source)

```text
src/Lifestone.Data/Animation/MotionCommandTable.cs
// Ported from Chorizite/DatReaderWriter@a9345f2 DatReaderWriter/Generated/Enums/MotionCommand.generated.cs (MIT; Copyright 2024 ACClientLib)
// with changes: the enum's 409 values and names as a table indexed by low word (the 11.6096 wire ordinal), completed to
// the 412 entries of acclient 11.6096 (...)

src/Lifestone.Net/Types/PhysicsState.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/PhysicsState.cs (AGPL-3.0)
// with changes: the names and values only, as a uint flags enum with a None member; HasPhysicsBSP is spelled
// HasPhysicsBsp; XML docs added.

src/Lifestone.Net/Messages/Login/CharacterErrorCode.cs
// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Network/Enum/CharacterError.cs (AGPL-3.0)
// with changes: the names and values only, as a uint enum; shorter XML docs.
```

| Lifestone file | Source | Notes |
|---|---|---|
| `MotionCommandTable.cs` | DatReaderWriter `Generated/Enums/MotionCommand.generated.cs` (its 409 values equal ACE's `ACE.Entity/Enum/MotionCommand.cs`) | 412 entries indexed by low word. Corrections from doc 13: Invalid is 0x80000000; CombatEat 0x10000110, CombatDrink 0x10000111 and NextMonster 0x09000112 added; PreviousMonster…ClosestPlayer shifted to 0x09000113-0x09000117; SideBySideVitals and WoahDuplicate2 renamed StretchUI and AI_TelegraphCast. Checked by hand against a local 11.6096 exe dump: all 412 ids match; N9 makes that a test. |
| `PhysicsState.cs` | ACE `ACE.Entity/Enum/PhysicsState.cs` | The physics-state bits of CreateObject and SetState. |
| `CharacterErrorCode.cs` | ACE `ACE.Server/Network/Enum/CharacterError.cs` | The F659 codes (retail `ID_CHAR_ERROR_*`). |

### The OpenAC transport (N1)

Vendored from `eriknihlen/OpenAC@1ee5a08 src/AcDream.Core.Net/` (m2-plan §3.3 N1; docs/protocol.md §8.2 and §9.1).
Every file was read before it was copied and starts with a port header that lists its changes. The import changes no
behaviour. The mechanical changes in every file are the namespace (`AcDream.Core.Net.*` becomes `Lifestone.Net.*`,
with `Cryptography` shortened to `Crypto`) and the visibility: the public types become internal, so the public surface
of Lifestone.Net stays the N0 contract. The other changes at import (N2's fixes follow in the next section):

| Lifestone file | OpenAC source (`src/AcDream.Core.Net/`) | Changes beyond namespace and visibility |
|---|---|---|
| `Crypto/Hash32.cs`, `Crypto/IsaacRandom.cs` | `Cryptography/` | none |
| `Packets/PacketHeader.cs`, `PacketHeaderFlags.cs`, `BorrowedPacket.cs`, `MessageFragment.cs`, `MessageFragmentHeader.cs`, `PacketWriter.cs`, `TransportDisconnect.cs`, `LoginRequest.cs` | `Packets/` | none |
| `Packets/PacketCodec.cs` | `Packets/PacketCodec.cs` | The owning decoder (`TryDecode`, `PacketDecodeResult`, and the `Packet` and `PacketHeaderOptional` classes it fills) left the product for the tests, so `Encode` measures the optional section with the borrowed parser. The product keeps the borrowed, allocation-free path. |
| `Packets/FragmentAssembler.cs` | `Packets/FragmentAssembler.cs` | Its clock is a `System.TimeProvider` instead of a `Func<double>` over `Stopwatch`, because Lifestone.Net reads no `Stopwatch` (the architecture tests' clock rule). |
| `Packets/GameMessageFragment.cs` | `Messages/GameMessageFragment.cs` | Moved from `Messages` to `Packets`. Its `GameMessageGroup` enum is `Lifestone.Net.Types.MessageQueue`, which has the same values. |
| `Transport/TransportClock.cs` | `Transport/TransportClock.cs` | The timestamp source is a `TimeProvider`, not a `Func<long>` defaulting to `Stopwatch`. |
| `Transport/ReliableTransport.cs` | `Transport/ReliableTransport.cs` | The default clock is a `TransportClock` over `TimeProvider.System`. |
| `Transport/OutboundFlowQueue.cs` | `Transport/OutboundFlowQueue.cs` | The queue argument is `MessageQueue`. |
| `Transport/InboundSequenceTracker.cs`, `AckNakScheduler.cs`, `SentPacketStore.cs`, `SequenceMath.cs`, `TransportStats.cs`, `RetailPacketLossAverager.cs` | `Transport/` | none |

The tests, from `tests/AcDream.Core.Net.Tests/` at the same commit, are the 16 files that need neither OpenAC's
`WorldSession` nor its ACE-derived models:

| Lifestone test file (`tests/Lifestone.Net.Tests/`) | Changes beyond namespace |
|---|---|
| `Crypto/Hash32Tests.cs`, `Crypto/IsaacRandomTests.cs` | none |
| `Packets/ConnectRequestTests.cs`, `LoginRequestTests.cs`, `PacketCodecTests.cs`, `TransportDisconnectTests.cs` | The owning decoder is `Support.OwningPacketDecoder`. |
| `Packets/BorrowedPacketCodecTests.cs`, `PacketCodecEncodeTests.cs` | The same, plus `MessageQueue`. Both classes run in the non-parallel allocation-measurement collection, because each has a warm-path test that counts this thread's allocations. |
| `Packets/FragmentAssemblerTests.cs` | The two time-to-live tests drive a tests/Shared `ManualTimeProvider`. |
| `Packets/MessageFragmentHeaderTests.cs`, `MessageFragmentTests.cs`, `PacketHeaderFlagsConformanceTests.cs`, `PacketHeaderTests.cs`, `PacketWriterTests.cs` | none |
| `Transport/OutboundFragmentationTests.cs` | The owning decoder, `MessageQueue`, and a clock over `TimeProvider.System`. The `LongChannelMessages_AreTransmittedWhole` theory is not taken, because it drives `WorldSession`. |
| `Transport/RetailPacketLossAveragerTests.cs` | A `ManualTimeProvider` advanced in seconds. |
| `Support/OwningPacketDecoder.cs` | OpenAC's `PacketCodec.TryDecode`, `Packet.cs` and `PacketHeaderOptional.cs`, unchanged apart from the namespace. Like ACE's `PacketHeaderOptional.Unpack`, it knows no EchoResponse. That makes it the independent, ACE-shaped parser that the vendored tests check the product against. N4's fake server does not use it: it parses with its own `FakeAce/ClientPacketParser.cs` (below), which shares only `Hash32` with the product. |

`Crypto/GoldenVectorTests.cs` is Lifestone's own. It checks docs/protocol.md §10 against the import:
- the Hash32 table;
- ISAAC outputs #256 to #259, and seed 0;
- the 24-byte ack-only packet;
- the encrypted LoginComplete packet (client seed 0x0BADF00D, first key 0x99D25AF6), byte for byte.

**OpenAC files not taken.** These stay out of Lifestone (docs/protocol.md §8.2 and §9.1):
- `WorldSession.cs` and `WorldSession.Connection.cs`, a 3,028-line class coupled to OpenAC's UI and game model;
  Lifestone writes its own `AcSession` (N5, N6);
- the wiring files: `GameEventWiring.cs`, `ObjectTableWiring.cs`, `CombatStateWiring.cs`, `SubscriptionSet.cs`,
  `ConnectionProgress.cs`, `LinkStatusSnapshot.cs` and `CastTraceDiagnostics.cs`;
- `NetDiagnostics.cs` and `Transport/LossyTransportDecorator.cs`, which write to the console and read `Stopwatch`; the
  decorator also only drops, so N8's test-side `LossyDatagramSocket` (drop and reorder) takes its place;
- `NetClient.cs`, which binds every interface. N2 re-expresses its socket setup as a loopback-only socket;
- the message codecs, which N3, N7 and N10 port;
- the tests that drive `WorldSession` or the ACE models;
- `Transport/AceCryptoModel.cs` and `AceSessionModel.cs`, near line-for-line ports of ACE's AGPL `CryptoSystem`, and
  `FakeAceTransport.cs`. N4 writes Lifestone's own fake server instead;
- `Transport/VirtualClock.cs` and `LossyLink.cs`, helpers that only the excluded tests use.

### Transport hygiene and defects 3 and 4 (N2)

N2 fixes the defects docs/protocol.md §8.2 found in the vendored code. Each changed file's header lists its N2 changes.

| Lifestone file | N2 change |
|---|---|
| `Packets/PacketCodec.cs`, `Packets/BorrowedPacket.cs` | **Defect 4.** The borrowed parser reads EchoResponse (0x04000000: f32 echoed client time, f32 server minus client time) in bit order, between EchoRequest and Flow, as ACE's `NetworkSession.WriteOptionalHeaders` writes it. LogonServerAddr (0x200), Referral (0x800), NetError (0x100000) and NetErrorDisconnect (0x200000) have no defined server-to-client body, because ACE never sends them. A packet that carries one is refused as `InvalidOptionalHeader` instead of being parsed past a guessed size. The constant `PacketCodec.UndefinedOptionalSections` names the four flags. |
| `Packets/PacketWriter.cs` | **Defect 3.** Strings are Windows-1252, ACE's code page, where OpenAC wrote ASCII and turned every other character into `?`. `WriteAsciiString16L` is for the fields that other fields follow. The String32L prefix byte holds the length; OpenAC wrote 0. The string writers are also static span writers. |
| `Packets/LoginRequest.cs` | The layout and `NetAuthType` stay OpenAC's; the API changes. `Build(string, string)` and `Parse` are removed. `Write(Span<byte>, clientVersion, account, password, timestamp)` writes into the caller's buffer. `Send(...)` writes the whole packet into one pooled buffer, which `CryptographicOperations.ZeroMemory` clears before it goes back to the pool, even when the send throws. The client version, account and password must be ASCII. |
| `Transport/OutboundFlowQueue.cs` | **Store-then-send.** OpenAC sent a sealed packet and stored it afterwards, so a send that threw lost a packet whose sequence and ISAAC key were already spent, and ACE stalled every later message (protocol.md §2.3). The packet is now stored before it is sent. |
| `Transport/ReliableTransport.cs`, `Transport/TransportStats.cs` | A `SocketException` from the send counts as a datagram lost on the wire (`SendFailures`), and a NAK recovers it. Any other exception propagates. |

N2 adds these files. Only the socket setup in `UdpLoopbackSocket.cs` comes from OpenAC:

- **`Sockets/UdpLoopbackSocket.cs`** implements `IDatagramSocket`. It is partly ported from OpenAC's `NetClient.cs`, but
  only the socket setup is OpenAC's (an OS-assigned port, SIO_UDP_CONNRESET off, `SendTo`/`ReceiveFromAsync`). The rest
  is Lifestone's:
  - it binds the loopback address of the server's family (127.0.0.1:0 or [::1]:0) where OpenAC bound every interface;
  - its constructor runs the loopback gate itself;
  - it sends only to the server's Port and Port + 1;
  - it keeps only datagrams from those two ports and counts the rest;
  - one background receive loop fills a bounded channel of 8,192 pooled buffers, and an overflow drops the newest
    datagram and counts it;
  - its timeouts run on the session's `TimeProvider`.
- **`Wire/WireReader.cs`** is the codecs' reader (N3, N7, N10):
  - it never throws on input and leaves its position alone when a read fails;
  - it reads String16L with its pad, PackedDword and the known-type form, the last as retail's
    `Unpack_AsDataIDOfKnownType` (11.4186 0x006844F0, 11.6096 0x006854F0) reads it: the type plus the value, with 14
    high bits in the long form, so the packed 0 that ACE writes for a missing id reads as the bare type id;
  - it checks a count against the bytes left before anything is allocated;
  - it aligns from the opcode byte.
- **`Wire/Cp1252.cs`** takes Windows-1252 from `CodePagesEncodingProvider.Instance`, so no provider is registered for
  the whole process and no package is needed. Its encoding is strict: a character outside the code page throws.
- **`Wire/AsciiGuard.cs`** holds the ASCII rule. ACE reads client strings with `BinaryReader.ReadChars` over UTF-8
  (`ACE.Common/Extensions/BinaryReaderExtensions.cs`), so in a field that other fields follow, only ASCII keeps the rest
  of the message in place. The password is guarded too: nothing follows it, but ACE's decoding would turn different
  non-ASCII passwords into the same string.

**Tests for N2.**

Changed vendored tests:
- `PacketWriterTests.cs` now expects the String32L prefix to hold the length.
- `LoginRequestTests.cs` uses the new API; an empty account or password is now an error.
- `BorrowedPacketCodecTests.cs` and `PacketCodecEncodeTests.cs` get their login payloads from the product's writer.
- `OutboundFragmentationTests.cs`'s failed-send test asserts store-then-send.

New Lifestone tests:
- `Packets/OptionalHeaderTests.cs`;
- `Packets/StringEncodingTests.cs`;
- `Packets/LoginRequestPacketTests.cs`: the §4.1 layout, and the buffer zeroed after the send, with an instrumented
  `ArrayPool`;
- `Wire/WireReaderTests.cs`;
- `Sockets/UdpLoopbackSocketTests.cs`: two local sockets stand in for ACE's ports, and nothing else is contacted;
- `Transport/StoreThenSendTests.cs`;
- `Transport/TransportClockTests.cs`;
- `Hygiene/SourceRulesTests.cs`, which checks the sources with comments blanked and the built assembly's member
  references, so Lifestone.Net has:
  - no console output;
  - no environment reads, so OpenAC's `ACDREAM_*` switches stay out;
  - no wall clock, `Stopwatch` or sleep;
  - no wildcard address;
  - no file or process access;
  - no `Encoding.RegisterProvider`.

Test-side helpers:
- `Support/LoginRequestReader.cs`, ported from OpenAC's `LoginRequest.Parse`, reads a body the way ACE does;
- `Support/LoginRequestBodies.cs`, Lifestone's own, builds bodies with the product's writer.

None of the vendored files read an environment variable or wrote to the console: OpenAC's `ACDREAM_*` reads and console
writes are all in files that were not taken (`WorldSession`, `NetDiagnostics`, `LossyTransportDecorator`).

### Login codecs and the dispatcher (N3)

N3 ports OpenAC's login-phase message codecs from `src/AcDream.Core.Net/Messages/` at the same commit
(docs/protocol.md §9.1). Every file was read before it was copied. The changes common to all of them:
- the namespace is `Lifestone.Net.Messages`, every type is internal, and each class gets a `Codec` suffix, so the
  session's `CharacterList` property and the codec never share a name;
- a parser is `TryParse(ReadOnlySpan<byte> message, out T)` over N2's `WireReader`: it takes the whole message, opcode
  first, never throws (OpenAC threw `FormatException`), and accepts a message only when every byte was read;
- the results are the N0 records (`CharacterListMsg`, `CharGenResponseMsg`, ...), whose words stay raw;
- a builder writes into a `PacketWriter` instead of returning a new array, and the fields that other fields follow go
  through the ASCII guard.

| Lifestone file (`src/Lifestone.Net/Messages/`) | OpenAC source (`src/AcDream.Core.Net/Messages/`) | Changes beyond the common ones |
|---|---|---|
| `Login/CharacterListCodec.cs` | `CharacterList.cs` | The deleted list keeps OpenAC's decoding (it has the active list's layout in retail's `CharacterSet::UnPack`) and fills the new `CharacterListMsg.DeletedCharacters`. `TrySelectFirstAvailable` is not taken. |
| `Login/ServerNameCodec.cs` | `ServerName.cs` | none |
| `Login/CharacterErrorCodec.cs` | `CharacterError.cs` | The code enum is not taken; `CharacterErrorCode` (from ACE) names the codes. |
| `Login/CharGenResponseCodec.cs` | `CharGenVerificationResponse.cs` | The result enum is `CharGenResult`. |
| `Login/CharacterCreateCodec.cs` | `CharacterCreate.cs` | Takes the N0 `CharacterCreateRequest`, whose `Validate` holds the 55-skill and ASCII-name rules. Is-admin and is-envoy are always 0. The trailing checksum stays: it was checked against retail's `ACCharGenResult::CG_Pack` (below). |
| `Login/CharacterEnterWorldCodec.cs` | `CharacterEnterWorld.cs` | Also recognises the server's F7DF. |
| `Login/CharacterLogOffCodec.cs` | `CharacterLogOff.cs` | none |
| `Login/CharacterDeleteCodec.cs` | `CharacterDelete.cs` | The list index is retail's i32, and a negative one throws. |
| `Login/CharacterRestoreCodec.cs` | `CharacterRestore.cs` | After the GUID it writes the character's name and the account, the layout of retail's only F7D9 sender (`Proto_UI::SendAdminRestoreCharacter`); OpenAC sent the GUID alone. ACE reads only the GUID either way. |
| `Login/DddCodec.cs` | `DddInterrogationResponse.cs`, `DddBegin.cs` | The response is written from the N0 `DddIterations` and adds the high-res list ('HiFi', 1) when the server's product id has bit 0x4 and the client has the dat; OpenAC never sent it. The iterations are required. The F7E5 parser and the F7EA check are Lifestone's. |
| `Chat/ServerMessageCodec.cs` | `ServerMessage.cs` | The chat type is ACE's i32. |
| `Events/GameEventEnvelope.cs` | `GameEventEnvelope.cs` | A header reader over a span; the event type stays a raw u32. OpenAC's `GameEventType` enum is not taken. |
| `Actions/LoginCompleteCodec.cs` | `GameActionLoginComplete.cs` | It writes the session's action sequence where OpenAC wrote 0. |

Lifestone's own N3 files:
- `MessageCodec.cs`: the opcode helpers the codecs share;
- `Login/AccountCodecs.cs`: F7DC AccountBoot (an optional reason), F7C1 AccountBanned (seconds and an optional reason)
  and F746 PlayerCreate, the three messages OpenAC lacked;
- `Actions/GameActionEnvelope.cs`: the F7B1 header;
- `Dispatch/InboundDispatcher.cs`: decodes the login and character-select opcodes, hands every F7B0 event to
  `OnGameEvent` with its header, and reports anything it cannot parse exactly as malformed.

`Login/CharacterListMsg.cs` gains `DeletedCharacters` (an init property, so the N0 constructor is unchanged), and
`Packets/PacketWriter.cs` exposes its span String16L writer and gains `WriteInt32`.

**Tests for N3** (all Lifestone's unless noted):
- `Messages/CharacterListCodecTests.cs` is partly ported from OpenAC's
  `tests/AcDream.Core.Net.Tests/Messages/CharacterListTests.cs`: the order, deleted-list, non-zero status, oversize
  count, 255-entry and truncation cases. It adds the docs/protocol.md §10 vector and the round trips through
  `AceWireWriters`.
- `Messages/CharacterCreateCodecTests.cs` is partly ported from OpenAC's `CharacterCreateTests.cs`: the field order, the
  exact bytes, the checksum's fields and the skill counts, read back by ACE's reader instead of an inline one.
- `Messages/LoginMessageCodecTests.cs`, `DddResponseCodecTests.cs` and `CharacterRequestCodecTests.cs` check every
  other parser against ACE's writers and every builder against ACE's readers, byte for byte where the spec gives bytes.
- `Dispatch/InboundDispatcherTests.cs` is rewritten for the decoded opcodes.
- `Fuzz/MessageBodyFuzzTests.cs`: every truncation of every decoded message, 10,000 seeded bit flips (1,000,000 at
  `Tier=Soak`) and random bodies under every decoded opcode; nothing throws.

Test-side helpers:
- **`tests/Shared/Net/AceWireWriters.cs`** builds server-to-client bodies as ACE's writers do, with a plain
  `BinaryWriter`, for every test project. It was written from docs/protocol.md §5 and by reading ACE's `GameMessage*`
  writers, `WorldObject_Networking.SerializeCreateObject` and `PositionPack`; nothing was copied.
- `Support/AceClientReaders.cs` reads client-to-server bodies the way ACE's handlers do (a UTF-8 `BinaryReader`,
  strings by character count, no trailing-byte check): `CharacterHandler`, `CharacterCreateInfo.Unpack`, `DDDHandler`
  with `ReadCAllIterationList`, `GameActionPacket` and `Position(BinaryReader)`. Also read, not copied.

### The fake server (N4)

`tests/Lifestone.Net.Tests/FakeAce/` is a fake ACE for the session tests (m2-plan §3.3 N4). It is written from
docs/protocol.md §1 to §4 and from reading ACE's network code at @95ef426d: `NetworkManager`, `Session`,
`NetworkSession`, `AuthenticationHandler`, `CharacterHandler`, `DDDHandler`, `ClientPacket`, `PacketHeaderOptional`,
`ServerPacket`, `MessageFragment`, `MessageBuffer`, `NetworkBundle`, `SessionConnectionData` and `UIntSequence`. Each rule
it reproduces names the ACE member in its comments. Nothing was copied from those files, and nothing came from
OpenAC's ACE models (`AceSessionModel.cs`, `AceCryptoModel.cs`, `FakeAceTransport.cs`), as docs/protocol.md §8.2
requires.

| Lifestone file (`tests/Lifestone.Net.Tests/`) | Source | Notes |
|---|---|---|
| `FakeAce/AceCryptoSystemOracle.cs` | **Ported** from ACE `Source/ACE.Common/Cryptography/CryptoSystem.cs` @95ef426d (AGPL-3.0; owner decision D20) | ACE's client-key window, exactly: the current key, up to 256 skipped keys, and a search that skips keys even when it fails. It wraps Lifestone.Net's `IsaacRandom`, which the §10 vectors check against ACE's ISAAC, instead of deriving from ACE's `ISAAC`. Test-only. |
| `FakeAce/FakeAceServer.cs`, `FakeAceSession.cs` | Lifestone's own, from the ACE behaviour above | Both ports on an `InMemoryNetwork`, the session map, ConnectRequest and the handshake race, ISAAC per direction, packet and fragment sequences, bundling (one bundle per 5 ms, groups in order, tail first), the 120 s cache, acks every 2 s and time syncs every 20 s, NAKs both ways, RejectRetransmit, the 256-key and 256-packet windows, termination (two seconds of flushing, then silence; never a Disconnect), timeouts, and the scripted login, character and world handlers. |
| `FakeAce/FakeAceScript.cs`, `FakeAceCounters.cs` | Lifestone's own | ACE's defaults and the delays that make its races reproducible; ACE's `NetworkStatistics` counters plus the events ACE only logs. The fake's boot texts are its own wording, not ACE's. |
| `FakeAce/ClientPacketParser.cs` | Lifestone's own | Parses a client datagram as ACE's `ClientPacket.Unpack` does (optional sections in ACE's order; LoginRequest, WorldLoginRequest and ConnectResponse leave the reader in place; fragments of 16 to 464 bytes). Only `Hash32` is shared with the product. |
| `Support/InMemoryNetwork.cs`, `LossModel.cs`, `SessionHarness.cs`, `RawAceClient.cs` | Lifestone's own | A virtual loopback network whose client sockets implement `IDatagramSocket`; a seeded loss model (drop, swap by one place, duplicate) that counts duplicate encrypted deliveries; virtual time around the fake; a hand-driven client for the conformance tests. |

**Deviations from ACE, all test-side:** the fake handles a client message as soon as the packet that completes it is
processed, where ACE queues it for the world thread. It scripts no teleport yet (N12). N4 sent no PlayerDescription;
N10 added the writer, and the fake now sends one before PlayerCreate (below).

**Tests for N4:**
- `FakeAce/FakeAceConformanceTests.cs` (Lifestone's): the handshake race, sequence 0 then 2, the first data packet 2,
  a gap of one buffered without a NAK, a gap of two NAKed (cleartext, at the current sequence, once a second), the
  256-packet limit, RejectRetransmit taking a sequence and no key, a resend keeping its key, a duplicate costing one CRC
  error, two silent seconds after a termination, the bad-handshake rule, the cookie match, the ack and time-sync
  cadence, the data check, and the key window itself.
- `FakeAce/FakeAceScriptTests.cs` (Lifestone's): the login failures (auto-create, unknown account, account in use, world
  closed, wrong version, ban, server full), create (with ACE's double NameInUse), delete and restore, entering the
  world, LoginComplete, the movement echo, logoff, the tool replies, the character-select ping and shutdown.
- `FakeAce/FakeAceInteropTests.cs` (Lifestone's): the vendored transport and the fake, written independently, agree
  on every packet and on multi-fragment messages both ways.
- `Support/InMemoryNetworkTests.cs` (Lifestone's).
- Partly ported from OpenAC's `tests/AcDream.Core.Net.Tests/Transport/` at 1ee5a08 (MIT), the tests that drive the
  transport alone: `Transport/InboundSequenceTrackerTests.cs` (10 of 14), `Transport/NakEmissionTests.cs` (10 of 15)
  and `Transport/AckNakSchedulerTests.cs` (3 of 6). OpenAC's `VirtualClock` became the tests/Shared
  `ManualTimeProvider`; the tests that drive `WorldSession` or the ACE models are not taken.

N4 also fixes a race in N2's `Sockets/UdpLoopbackSocket.cs`: a socket disposed before its receive loop had started made
the loop read the cancellation token from an already disposed source, fault, and never complete the channel, so a
waiter hung. The loop now gets its token before it starts; `UdpLoopbackSocketTests` has a regression test.

### The session (N5, N6)

`src/Lifestone.Net/Session/SessionMachine*.cs`, `SessionConnection.cs` and `Transport/InboundSequencer.cs` are
Lifestone's own. They implement `IAcSession` from docs/protocol.md §2 to §4 and m2-plan §3.3 N5 and N6. OpenAC's
`WorldSession.cs` and `WorldSession.Connection.cs` (1ee5a08) were read for the shape of the flow (the delayed
ConnectResponse, the datagram path through the inbound tracker), as protocol.md §9.1 asks, and nothing was copied from
them. ACE's `AuthenticationHandler`, `CharacterHandler`, `NetworkManager`, `NetworkSession` and `Session` at 95ef426d were
read for the server's side of each exchange. The retail behaviours the session follows are cited in the table below.

What the session adds over the transport:
- one socket per login attempt, so a LoginRequest is never sent twice from one endpoint;
- the ConnectResponse 200 ms after ConnectRequest, again every 0.333 s, within 10 s;
- the retry policy (D18): 0x01, 0x0D, 0x15 and a lost ConnectRequest, 3 retries 3 s apart (4 attempts in all);
- the data check from `DddIterations`, character select once both F7EA and F658 are in;
- the silence watchdog, which judges silence only while the session is pumped;
- the inbound sequencer: delivery by fragment sequence (D18), zero copy in order, the gap-skip rule, and arrival
  order for `--retail-net`;
- the pump budget, which applies from entering the world on;
- the character operations, entering the world, LoginComplete on request only, logoff and quit (N6).

Two N6 rules follow from ACE's `CharacterHandler` at 95ef426d, which was read, not copied:
- ACE checks a new name twice on its serial database queue, so a taken name gets NameInUse twice. The second one is
  folded before the waiting operation is looked at, because nothing a later request queues can overtake it. A create sent
  from the first NameInUse's callback therefore gets its own answer.
- ACE appends a created character to the list that delete indexes count from, and sends no new F658. So every Ok F643
  updates the local list, even one that comes after the wait gave up: a new GUID is appended, a listed one is
  un-greyed.

N5 also adds `PacketWriter.Reset` (the session reuses one writer); the file's port header lists it.

**Tests for N5 and N6** (`tests/Lifestone.Net.Tests/Session/`, all Lifestone's), each a session on the N4 fake in
virtual time:
- `SessionRig.cs`: the rig, with a recording sink, tap and log;
- `HandshakeTests.cs`: the phases, the F7E6 bytes, the ConnectResponse race at 150 ms, 400 ms and 2 s, a lost
  LoginRequest or ConnectRequest, the handshake cap and no server at all;
- `LoginFailureTests.cs`: a wrong password, a wrong client version, older and newer dats, DDD_BeginDDD (parsed or not),
  a ban, an unknown account, a graceful shutdown in the same pump, a boot in the world;
- `RetryTests.cs`: 0x01 then success, 0x15 four times, a world that opens, the retail policy, waiting for a retry;
- `DataCheckTests.cs`: the high-res list, F7EA never echoed, F7EA before F658, a late F7E5, the data-check cap;
- `WatchdogTests.cs`: 20 s, 140 s with retail timeouts, a 30 s stall, a server that died during a stall;
- `SequencerTests.cs`: the sequencer alone, then reordered, duplicated and tail-first delivery, a reject that leads
  to one gap skip, and a 4 s gap that is waited out;
- `PumpBudgetTests.cs`: 500 creates over a 1 ms budget, a zero budget, no budget before the world, no re-entry;
- `WaitForInboundTests.cs`: a wait on an idle or failed session, or on a socket whose receive loop faulted, lasts
  its full length, and only an infinite one returns at once;
- `SentinelPasswordTests.cs`: a sentinel password through login (with a retry), create, enter, LoginComplete,
  logoff and quit; nowhere but in the two LoginRequests;
- `CharacterOpsTests.cs`, `EnterWorldTests.cs`, `LogoffQuitTests.cs` (N6).

The fake gained two test hooks for them, `FakeAceScript.EnterWorldErrors` and `FakeAceSession.ForgetCachedPackets`, and
the support code two more, `SessionHarness.ServerAlive` and `InMemoryNetwork.Drop`.

### World, movement, chat and event codecs; defects 1 and 2 (N7)

N7 ports OpenAC's world and movement codecs from `src/AcDream.Core.Net/Messages/` at the same commit (docs/protocol.md
§9.1; m2-plan §3.3 N7). Every file was read before it was copied. The changes common to all of them are N3's:
- the namespace is `Lifestone.Net.Messages`, and every type is internal;
- a parser takes the whole message, opcode first, over N2's `WireReader`, never throws, and accepts a message only
  when every byte was read (OpenAC returned half-read records and caught exceptions);
- the results are the N0 records, whose words stay raw;
- a builder writes into a `PacketWriter` through `GameActionEnvelope`, with the session's action sequence, and checks
  its input before it writes anything.

| Lifestone file (`src/Lifestone.Net/Messages/`) | OpenAC source (`src/AcDream.Core.Net/Messages/`) | Changes beyond the common ones |
|---|---|---|
| `World/CreateObjectCodec.cs` | `CreateObject.cs` | F745 and F7DB are one codec; the three blocks are the next three files. |
| `World/ModelDataCodec.cs` | `CreateObject.cs` (`ReadModelData`) | A first byte other than 0x11 is refused, as retail's `ObjDesc::UnPack` refuses it. The ids are read as retail's `Unpack_AsDataIDOfKnownType` reads them, so a packed 0 is the bare type id (0x04000000, 0x05000000, 0x01000000); OpenAC's `ReadPackedDwordOfKnownType` kept 0 as 0 and ORed the type into 15 high bits. |
| `World/PhysicsDescCodec.cs` | `CreateObject.cs`, `PhysicsSpawnData.cs` | The rotation stays raw: OpenAC's `AcDream.Core.Physics.PositionFrameValidation` (CreateObject.cs:302) scaled it to unit length, and normalisation belongs to the client. The movement buffer is parsed over a reader of exactly its length and must fill it. The children count is checked against the bytes left. |
| `World/WeenieDescCodec.cs` | `PublicWeenieDescBody.cs` | Every field keeps ACE's width, and its sign apart from Value, which ACE holds as an int and docs/protocol.md names a u32 (OpenAC widened several to int and read the capacities as signed); PScript is kept (OpenAC skipped it); the RestrictionDB is read as ACE writes it into the new `HouseRestrictions` record (OpenAC's `AcDream.Core.Items.HouseRestrictionRecord` is not taken); the closing Align is required; the icons are read as retail's `Unpack_AsDataIDOfKnownType` reads them, so a packed 0 is 0x06000000 (OpenAC kept 0). |
| `World/MovementDataCodec.cs` | `CreateObject.cs` (`TryParseMovementData`), `UpdateMotion.cs` (the body, `TryParseMoveToPayload`, `TryParseTurnToPayload`) | **Defect 2 fixed:** the interpreted state ends with Align before the sticky GUID, as ACE's `InterpretedMotionState.Write` and retail's `InterpretedMotionState::UnPack` align; OpenAC read the GUID 2 bytes early whenever an odd number of u16 fields was present (UpdateMotion.cs:156-160). The command count is retail's 5 bits; an unknown movement type is refused; MoveTo keeps every parameter. |
| `World/UpdateMotionCodec.cs` | `UpdateMotion.cs` (the header) | The `ACDREAM_DUMP_MOTION` environment read and its console write (UpdateMotion.cs:60-65) are gone; the header's Align is read as an Align. |
| `World/UpdatePositionCodec.cs` | `UpdatePosition.cs` | Decodes into the `UpdatePositionMsg` struct without allocating. |
| `World/ObjectEventCodecs.cs` | `DeleteObject.cs`, `SetState.cs`, `VectorUpdate.cs`, `ParentEvent.cs`, `PickupEvent.cs`, `SoundEvent.cs` (partly) | One class; DeleteObject's closing Align is required; ParentEvent's location and placement are ACE's i32. F751 PlayerTeleport and F755 PlayEffect are Lifestone's own: OpenAC's `PlayPhysicsScript` decodes F754. |
| `World/ObjDescEventCodec.cs` | `ObjDescEvent.cs` | The ModelData is `ModelDataCodec`'s. |
| `Chat/ChatCodecs.cs` | `HearSpeech.cs`, `EmoteText.cs`, `SoulEmote.cs` | No string length cap; the range is null for HearSpeech. |
| `Actions/RawMotionStateCodec.cs` | `RawMotionStatePacker.cs` | **Defect 1 fixed:** each action is 8 bytes, {u16 command low word, u16 stamp \| autonomous << 15, f32 speed}; OpenAC left out the speed, and with one action ACE read the position cell as 0x42A80000 (RawMotionStatePacker.cs:55-60). The speeds compare with the defaults by their bits, as retail's `RawMotionState::Pack` does; more than 31 actions (the 5-bit count) are refused. |
| `Actions/MovementActionCodecs.cs` | `MoveToState.cs`, `AutonomousPosition.cs`, `JumpAction.cs` | The N0 records `AcPosition`, `ObjSeqs` and `RawMotionStateWire`; the RawMotionState is validated before anything is written. |
| `Actions/ChatActionCodecs.cs` | `ChatRequests.cs` (`BuildTalk`, `BuildTell` only) | Tell's message and target must be ASCII (ACE counts UTF-8 characters, so anything else shifts the target) and the target must not be empty; Talk's text, the last field, stays Windows-1252. |

Lifestone's own N7 files:
- `PositionCodec.cs`: the 32-byte position, the origin, vectors and the four echoed sequences the codecs share;
- `Events/GameEventCodecs.cs`: the six events N7 decodes (0x02BD Tell, 0x0004 PopupString, 0x028A and 0x028B
  WeenieError, 0x02EB CommunicationTransientString, 0x01C7 UseDone, 0x01C0 UpdateHealth), from ACE's
  `GameEvent/Events/*` writers and the retail dispatchers below; OpenAC's `GameEvents.cs` was not taken;
- `Actions/ObjectActionCodecs.cs`: 0x0036 Use and 0x01BF QueryHealth, one GUID each;
- `Dispatch/InboundDispatcher.cs` decodes the world (F745/F7DB, F747, F748, F74C, F74E, F74B, F751, F749, F74A, F625,
  F750, F755), chat (0x02BB, 0x02BC, 0x01E0, 0x01E2) and the six events; every other event still reaches
  `OnGameEvent`;
- `Session/SessionMachine.World.cs` implements the seven world sends (in world only, on the weenie queue, each taking
  the next GameAction sequence only once its codec has accepted the input), and `SessionMachine.Inbound.cs` drops the
  N6 fallback that armed LoginComplete from undecoded F745/F751 bodies (the decoded callbacks arm it now).

`Types/WeenieDesc.cs` gains `HouseRestrictions.BucketCount` (an init property, additive). The N7 world sends no longer
throw `TODO(net)`; `SessionMachine.Todo` is gone with them.

The OpenAC tests named by the raw plan (`UpdatePositionTests`, `MoveToStateTests`, `CreateObjectTests`,
`UpdateMotionTests` and the others) were read; none was copied. Their checks are re-expressed against ACE's writers and
readers, which is the point of the round trips: the expected bytes do not come from a codec under test.

**Tests for N7** (all Lifestone's):
- `Messages/World/ObjectEventCodecTests.cs`: the docs/protocol.md §10 F748 vector (44 bytes, flags 0x34) parsed exactly
  and equal to ACE's writer; every flag combination ACE writes; the small object messages and F625.
- `Messages/World/UpdateMotionCodecTests.cs`: every interpreted field and command; **defect 2** with 0 to 4 u16 fields
  (the pad is 2 bytes exactly when the count is odd); the unpadded layout OpenAC's `UpdateMotionTests` asserted at
  lines 583-600 is refused, inverted; retail's 5-bit count; MoveTo and TurnTo; unknown movement types refused.
- `Messages/World/CreateObjectCodecTests.cs`: the player (Movement, Position, MTable, STable, PeTable, CSetup, ObjScale,
  nine sequences), an NPC, a static with an AnimationFrame, a wielded item with a Parent; every PhysicsDesc field and
  every WeenieDesc optional field one flag at a time; the second header; a movement body that does not fill its length.
- `Messages/Chat/ChatAndEventCodecTests.cs`: the chat messages and the six events.
- `Messages/Actions/WorldActionCodecTests.cs`: the §10 MoveToState vector byte for byte; **defect 1**: with one action
  the message is 80 bytes and ACE's reader finds cell 0xA9B40019, and the 76-byte counterfactual puts 0x42A80000 where
  ACE reads the cell; the 56-byte Jump, of which ACE reads 32; AutonomousPosition, Use, QueryHealth, Talk and Tell.
- `Messages/AllocationTests.cs`: 10,000 F748 dispatches, and the other struct messages, allocate 0 bytes.
- `Session/WorldSendTests.cs`: the sends on the fake's world, their answers decoded, the phase rule, refused input.
- Updated: `Dispatch/InboundDispatcherTests.cs`, `Fuzz/MessageBodyFuzzTests.cs` (every world, chat and event opcode),
  `Session/AcSessionCreateTests.cs` (no loud stubs left), `Session/SentinelPasswordTests.cs` (a walk through every world
  send).

Test-side helpers, read from ACE and not copied:
- `tests/Shared/Net/AceWireWriters.cs` gains every N7 message: the full `SerializeCreateObject` (movement, every
  PhysicsDesc field, every WeenieDesc field, the second header, `RestrictionDB`), UpdateMotion with
  `MovementData.Write`, and the smaller world, chat and event writers. The additions are new members and new optional
  record properties, so its earlier calls write the same bytes.
- `Support/AceClientReaders.cs` gains ACE's reader mirror for the world actions: `GameActionMoveToState` with ACE's
  `RawMotionState` reader, `GameActionAutonomousPosition`, `GameActionJump` with ACE's 32-byte `JumpPack`, and Use,
  QueryHealth, Talk and Tell.
- The fake server (`FakeAce/FakeAceSession.cs`) answers Use with UseDone, QueryHealth with UpdateHealth, a Tell with a
  Tell event (to the player itself) or WeenieError 0x052B, and a Talk with HearSpeech, and reads a Jump.

### Robustness (N8)

N8 adds no file from OpenAC. It changes five of the ported transport files; each header lists its N8 change after its
N1 and N2 ones. ACE's `NetworkSession.ProcessPacket`, `Update` and `ClientPacket.VerifyCRC` at 95ef426d were read for
the server's side, and nothing was copied.

| Lifestone file (`src/Lifestone.Net/Transport/`) | N8 change |
|---|---|
| `OutboundFlowQueue.cs` | **The tail probe** (owner decision D18; off with `--retail-net`). ACE NAKs a missing client packet only once a packet two ahead of it arrives, so a lost last packet (a LoginComplete, a final AutonomousPosition) stays lost while the client is idle. When a server ack is behind the last packet sent, and the packet right after it was last put on the wire at least 1.0 s before, that one packet is resent with the Retransmission flag and its sealed key: once per ack, never above ack + 1, because ACE counts any duplicate encrypted packet as a CRC error. It reads the raw maximum of the server's acks (`RawServerAck`); OpenAC folds a NAK's first id into `AckWatermark`, which therefore cannot be used. **Held acks** (`HoldsAcks`), part of the same deviation: an ack-only packet carries the last sequence sent, and ACE keeps the first packet it gets for a sequence ahead of a gap (`outOfOrderPackets`), so an ack-only packet can take a lost data packet's place; that data packet's resend is then checked (its key spent) and dropped, every later resend fails the checksum, and ACE stalls every later message. With the probe on, a due ack waits while the server has not acked every packet sent. The virtual soak found this. |
| `AckNakScheduler.cs` | Holds a due ack while the queue `HoldsAcks`; the gate stays open, so the ack goes out on the first sweep after. |
| `SentPacketStore.cs` | A cached packet remembers when it was last put on the wire (`LastSentAt`, `MarkSent`), for the probe's 1 s. |
| `InboundSequenceTracker.cs` | **Rejected admissions.** A server packet is admitted (its key drawn, the gap below it parked) before its checksum can be checked. OpenAC kept a failed packet's effects, so a corrupt or forged sequence a few thousand ahead moved the window there and parked a key for every id between; the real packets were then dropped as duplicates. `Reject` undoes an admission that advanced the window: the parked keys and the packet's own go back in draw order, and the highest sequence returns. A failed packet below the highest only gets its parked key back, as before. |
| `TransportStats.cs` | The `TailProbes`, `AdmissionsRejected` and `AcksHeld` counters. |

Lifestone's own N8 changes: `Session/AcSessionOptions.cs` documents `TailProbe` (true by default; false turns the probe
and the held acks off), `Session/SessionConnection.cs` turns it on per connection, `Session/SessionMachine.Receive.cs`
calls `Reject` on a checksum failure, and `Diagnostics/LinkStats.cs` reports `TailProbes` (also counted in `Resends`).

**Tests for N8** (all Lifestone's):
- `Transport/TailProbeTests.cs`: the queue alone (the probe after exactly 1 s, ack + 1 and never beyond, an older ack, a
  caught-up ack, a NAK in the same pass, off with `--retail-net`, the held acks), then on the fake server: a dropped
  LoginComplete with nothing after it delivered by one probe; with `--retail-net` it stays lost; a packet merely late by
  under a second is not probed; two lost packets probed one ack at a time; acks held, then resumed.
- `Transport/RejectShiftTests.cs`: ACE's cleartext RejectRetransmit takes a sequence and no key, so the parked key moves
  to the next packet (`OnCleartextRejectSequence`), in order and when the reject arrives after the packets behind it;
  and `Reject` (a failed packet far ahead or just ahead, cleartext or late).
- `Soak/VirtualSoakTests.cs`: two minutes of virtual time in the fake's world, an AutonomousPosition every 200 ms and a
  MoveToState on every change, 5% loss and 2% adjacent reordering both ways; no failure, no termination, every client
  message handled in order, no client checksum failure or gap skip, and the fake's CRC count equal to the link's
  duplicate encrypted deliveries. Five seeds by default, ten minutes over 100 seeds at `Tier=Soak`; a drop-only run makes
  no duplicate.
- `Fuzz/DatagramFuzzTests.cs`: random datagrams, every truncation of a whole recorded session's datagrams, and seeded
  bit flips (100,000 by default, 5,000,000 at `Tier=Soak`): the borrowed parser never throws, a truncation never parses,
  one flipped bit never verifies.
- `Fuzz/SessionFuzzTests.cs`: 10,000 garbage datagrams (random, truncated, flipped and replayed) from the server's two
  ports as the session first reaches each phase; the session never throws and completes the whole flow.

Test-side helpers: `Support/LossyDatagramSocket.cs`, a seeded decorator over any `IDatagramSocket` (armed by the first
encrypted send; parameters from its constructor only), which N12 puts over the real socket in place of OpenAC's
drop-only `LossyTransportDecorator`; `Support/LossModel.cs` became thread-safe for timers on a real clock; the fake's
counters gained `OutOfOrderSlotTaken` and `DataLostBehindAckOnly`, the events ACE does not log.

### The motion command table (N9)

`src/Lifestone.Data/Animation/MotionCommandTable.cs` (N0) is unchanged apart from its remarks. N9 adds the checks:
- the table against the user's own 11.6096 exe: `tools/protocol/dump_command_table.py` (read-only; it checks the exe's
  MD5 first) writes `cache/protocol/command-table-11.6096.tsv`, which is never committed, and
  `MotionCommandTableTests.Every_row_of_the_exe_table_matches` checks every row: all 412 ids match, and the exe spells
  three names differently (2HandedSwordCombat, 2HandedStaffCombat, BreatheFlame_). The test is skipped, naming the
  command that makes the dump, when the dump is absent from this checkout and the main one;
- the reconciliation with ACE's `ACE.Entity/Enum/MotionCommand.cs` at 95ef426d, checked once when N9 landed and pinned
  as data (`MotionCommandTableTests.Aces_stale_ids_lose_to_the_11_6096_table`): ACE's 409 values, the same as
  DatReaderWriter's, keep the five UI commands PreviousMonster..ClosestPlayer at their pre-2015 ids 0x09000110-0x09000114
  (whose low words 11.6096 gave to CombatEat, CombatDrink, NextMonster, PreviousMonster and ClosestMonster), comment
  NextMonster out, and name two entries SideBySideVitals and WoahDuplicate2; ACE's Invalid is 0, retail's 0x80000000.
  All 412 table ids and the pinned values (AFKState 0x4300011B, WalkForward 0x45000005, AI_TelegraphCast 0x1000019B and
  the others of m2-plan §3.3 N9) are checked by the tests N0 added.

### The player's own state (N10)

N10 ports OpenAC's PlayerDescription and PrivateUpdate parsers from `src/AcDream.Core.Net/Messages/` at the same commit
(m2-plan §3.3 N10; owner decision D16). Every file was read before it was copied. The changes common to all of them are
N3's: an internal static codec over N2's `WireReader` that takes the whole message, never throws, and accepts a message
only when every byte was read. The results are the N0 records, extended additively.

| Lifestone file (`src/Lifestone.Net/Messages/`) | OpenAC source (`src/AcDream.Core.Net/Messages/`) | Changes beyond the common ones |
|---|---|---|
| `Self/PlayerDescriptionCodec.cs` | `PlayerDescriptionParser.cs`, `EnchantmentWireReader.cs` | **Decoupled** from `AcDream.Core.Items` (`PropertyBundle`, `ShortcutEntry`): the result is `PlayerDescriptionMsg`, whose N0 members keep their meaning and which gains the flags, the positions, the enchantments, the player module and the tail. OpenAC returned partial results, caught `FormatException` and searched for the inventory heuristically; the codec reads every section as retail's readers do (below). A spellbook entry follows `SpellBookPage::UnPack`: a first f32 of 2 or more is the likelihood plus 2 (ACE writes 2.0), a smaller one opens the old 12-byte form (OpenAC read one f32). The timestamp format (0x80) and the generic options data (0x100) are read (OpenAC skipped them). The player module ends with retail's align. A vector section Lifestone does not decode (Body 0x4, EventFilter 0x8, EmoteTable 0x10, CreationProfile 0x20, PageDataList 0x40, GeneratorTable 0x80, GeneratorRegistry 0x400, GeneratorQueue 0x800; ACE sends none) makes the description malformed with the section named. An enchantment's values stay raw (OpenAC refused non-finite ones). The cache's vitals 7, 8 and 9 carry the ids PrivateUpdateVital uses (1, 3, 5). |
| `Self/PrivateUpdateCodec.cs` | `PrivateUpdateVital.cs`, `PrivateUpdateAttribute.cs`, `PrivateUpdateSkill.cs`, `PrivateUpdatePropertyInt.cs`, `PrivateUpdatePropertyInt64.cs` | One codec for the six messages (0x02CD, 0x02CF, 0x02DD, 0x02E3, 0x02E7, 0x02E9) into the N0 struct `PrivateUpdateMsg`, dispatched without allocating. The sequence byte is kept (OpenAC's int and int64 parsers dropped it); a message must be exactly its layout's length (OpenAC accepted trailing bytes). The skill shares the description's 28-byte reader. |

**The gameplay options (option flag 0x200) are not decoded.** Retail reads them as a property collection through a
versioned archive (`PackObjPropertyCollection::Serialize`), and ACE stores and returns the bytes the client last sent,
with no length in front. Their end cannot be found from the tail either: a suffix of the content list whose first word
reads as a small count (the last entry a non-container, or the one before it a container) is itself a valid tail, so a
search would be a guess (`PlayerDescriptionCodecTests.A_tail_search_would_be_a_guess`). So the options and the
inventory lists after them stay raw together: `PlayerDescriptionMsg.GameplayOptions` holds those bytes,
`InventoryUndecoded` is true, and the session logs a warning naming the TODO. Everything before them is decoded, vitals
included. ACE sets the flag only for a character a client sent options for; Lifestone sends none in M2.

The dispatcher decodes GameEvent 0x0013 into `OnPlayerDescription` and the six PrivateUpdate opcodes into
`OnPrivateUpdate`; the session forwards both.

**Tests for N10** (all Lifestone's; OpenAC's `PlayerDescriptionParserTests`, `PrivateUpdateVitalTests` and
`PrivateUpdateAttributeSkillTests` were read, and their checks are re-expressed against ACE's writers):
- `Messages/Self/PlayerDescriptionCodecTests.cs`: a fresh character and one with every section ACE fills (an instance id
  table, LastOutsideDeath, spells, all four enchantment buckets with and without a spell set id, shortcuts, spell bars,
  desired components, inventory and equipped objects) round-trip field for field, and every prefix and a trailing byte
  are refused; the spell-bar count by retail's three flags; retail's defaults; the timestamp format and the generic
  options data; the old spellbook form; each undecoded vector section refused by name; the gameplay options kept raw;
  the dispatcher.
- `Messages/Self/PrivateUpdateCodecTests.cs`: each message round-trips, is exact, has ACE's length, and reaches
  `OnPrivateUpdate`.
- `Messages/AllocationTests.cs`: the six PrivateUpdate messages dispatch without allocating.
- `Session/SelfStateTests.cs`: entering the fake's world delivers the description before PlayerCreate; PrivateUpdates
  reach the sink; gameplay options are logged as a warning.
- Updated: `Dispatch/InboundDispatcherTests.cs` and `Fuzz/MessageBodyFuzzTests.cs` (PlayerDescription and the six
  PrivateUpdates; the "undecoded event" sample is now 0x0029 CharacterTitle).

Test-side helpers, read from ACE and not copied: `tests/Shared/Net/AceWireWriters.cs` gains
`GameEventPlayerDescription.WriteEventBody` (with the enchantment registry and ACE's bucket counts) as
`PlayerDescription` with the `AcePlayerDescription` record and `AcePlayerDescription.NewCharacter` (a plausible fresh
character, about 2 KB), and the six `GameMessagePrivateUpdate*` writers. The fake server sends a PlayerDescription before
PlayerCreate, as ACE's `Player.SendSelf` does (`FakeAceScript.PlayerDescription` replaces it), which closes the N4
`TODO(net)`.

### Re-expressed retail behaviour (each type cites its functions)

| Lifestone file | Retail functions | Address |
|---|---|---|
| `src/Lifestone.Data/Animation/MotionCommandTable.cs` | `InterpretedMotionState::UnPack`, `RawMotionState::UnPack` (low word to full command through the command-id table) | 11.4186 0x0051F400, 0x0051EFC0; 11.6096 0x0051FF10, 0x0051FAD0 |
| `src/Lifestone.Net/Types/RawMotionStateWire.cs` | `RawMotionState::Pack` (the layout the N7 encoder writes), `RawMotionState::RawMotionState` (the defaults) | 11.4186 0x0051ED10, 0x0051E7F0; 11.6096 0x0051F820, 0x0051F300 |
| `Messages/Login/CharacterListCodec.cs`, `CharacterListMsg.cs` | `CharacterSet::UnPack`, `CharacterIdentity::UnPack`, `CPlayerSystem::Handle_Login__CharacterSet` | 11.4186 0x004FE340, 0x004FE880, 0x0055F6D0; 11.6096 0x004FEDC0, 0x004FF300, 0x00560440 |
| `Messages/Login/ServerNameCodec.cs` | `CM_Login::DispatchUI_WorldInfo`, `ClientUISystem::Handle_Login__WorldInfo` | 11.4186 0x006AD860, 0x005641A0; 11.6096 0x006AE7C0, 0x00564F40 |
| `Messages/Login/CharacterErrorCodec.cs` | `CPlayerSystem::Handle_CharacterError` | 11.4186 0x0055D5D0; 11.6096 0x0055E2F0 |
| `Messages/Login/CharGenResponseCodec.cs` | `CPlayerSystem::Handle_CharGenVerificationResponse` (only code 1 carries the identity) | 11.4186 0x0055E8B0; 11.6096 0x0055F620 |
| `Messages/Login/CharacterCreateCodec.cs` | `ACCharGenResult::CG_Pack` (field order and the trailing checksum), `Proto_UI::SendCharGenResult` (the account in front) | 11.4186 0x005C7200, 0x00546A70; 11.6096 0x005C81E0, 0x00547630 |
| `Messages/Login/CharacterEnterWorldCodec.cs` | `Proto_UI::SendEnterWorldRequest`, `Proto_UI::SendEnterWorld`, `CM_Character::DispatchUI_EnterGame_ServerReady` | 11.4186 0x00546A00, 0x00546BC0, 0x006A0E50; 11.6096 0x005475C0, 0x00547780, 0x006A1C70 |
| `Messages/Login/CharacterLogOffCodec.cs` | `Proto_UI::LogOffCharacter` (F653 with the character's GUID) | 11.4186 0x00546A20; 11.6096 0x005475E0 |
| `Messages/Login/CharacterDeleteCodec.cs` | `Proto_UI::SendDeleteCharacter` | 11.4186 0x00546B30; 11.6096 0x005476F0 |
| `Messages/Login/CharacterRestoreCodec.cs` | `Proto_UI::SendAdminRestoreCharacter` (GUID, name, account) | 11.4186 0x00546CF0; 11.6096 0x005478B0 |
| `Messages/Login/DddCodec.cs` | `DDD_InterrogationMessage::Serialize`, `DDD_InterrogationResponseMessage::Serialize`, `CAllIterationList::Serialize`, `CAllIterationList::PTaggedIterationList::Serialize`, `CMostlyConsecutiveIntSet::Serialize` (the run-length form), `DDD_BeginDDDMessage::Serialize` | 11.4186 0x004F85F0, 0x004F9700, 0x00678090, 0x00677C70, 0x006700A0, 0x004F9B40; 11.6096 0x004F9190, 0x004FA2A0, 0x006790F0, 0x00678CD0, 0x00671040, 0x004FA6E0 |
| `Messages/Login/AccountCodecs.cs` | `CPlayerSystem::Handle_AccountBooted` (absent reason), `CPlayerSystem::Handle_AccountBanned` (signed seconds, optional reason) | 11.4186 0x00562A70, 0x00562BD0; 11.6096 0x00563810, 0x00563970 |
| `Messages/Chat/ServerMessageCodec.cs` | `CM_Communication::DispatchUI_TextboxString` | 11.4186 0x006A4F30; 11.6096 0x006A5E90 |
| `Messages/Actions/GameActionEnvelope.cs` | `OrderHdr::Pack`, `Proto_UI::GetNextUICounter` (the sequence counts from 1) | 11.4186 0x006B30D0, 0x005467E0; 11.6096 0x006B4010, 0x005473A0 |
| `Messages/Actions/LoginCompleteCodec.cs` | `CM_Character::Event_LoginCompleteNotification` | 11.4186 0x006A1480; 11.6096 0x006A22A0 |
| `Messages/PositionCodec.cs` | `Position::UnPack`, `Position::UnPackOrigin` | 11.4186 0x005A9690, 0x005A9790; 11.6096 0x005AA740, 0x005AA840 |
| `Messages/World/ModelDataCodec.cs` | `ObjDesc::UnPack` (refuses a first byte other than 0x11) | 11.4186 0x005ABCF0; 11.6096 0x005ACDA0 |
| `Messages/World/MovementDataCodec.cs` | `MovementManager::unpack_movement`, `InterpretedMotionState::UnPack` (the 5-bit count, the closing align before the sticky GUID), `MovementParameters::UnPackNet` | 11.4186 0x00524440, 0x0051F400, 0x0052AC50; 11.6096 0x00525040, 0x0051FF10, 0x0052B860 |
| `Messages/World/PhysicsDescCodec.cs` | `PhysicsDesc::UnPack` (the field order; the movement buffer copied whole), `CPhysicsObj::unpack_movement` | 11.4186 0x0051DDD0, 0x00512040; 11.6096 0x0051E8E0, 0x00512B10 |
| `Messages/World/WeenieDescCodec.cs` | `PublicWeenieDesc::UnPack`, `RestrictionDB::UnPack` | 11.4186 0x005AD470, 0x005AE990; 11.6096 0x005AE520, 0x005AFA40 |
| `Messages/World/CreateObjectCodec.cs` | `CM_Physics::DispatchSB_CreateObject`, `CM_Physics::DispatchSB_UpdateObject` | 11.4186 0x006AC8D0, 0x006AC7B0; 11.6096 0x006AD830, 0x006AD710 |
| `Messages/World/UpdatePositionCodec.cs` | `ACSmartBox::DispatchSmartBoxEvent`, `SmartBox::UnpackPositionEvent`, `PositionPack::UnPack` | 11.4186 0x005595D0, 0x004542C0, 0x00516740; 11.6096 0x0055A210, 0x00454360, 0x00517240 |
| `Messages/World/UpdateMotionCodec.cs` | `ACSmartBox::DispatchSmartBoxEvent`, `CPhysics::SetObjectMovement` (the three-argument overload: the sequences, the autonomous byte, the align), `CPhysicsObj::unpack_movement` | 11.4186 0x005595D0, 0x00509790, 0x00512040; 11.6096 0x0055A210, 0x0050A260, 0x00512B10 |
| `Messages/World/ObjectEventCodecs.cs` | `CM_Physics::DispatchSB_DeleteObject`, `DispatchSB_SetState`, `DispatchSB_VectorUpdate`, `DispatchSB_PlayerTeleport`, `DispatchSB_ParentEvent`, `DispatchSB_PickupEvent`, `DispatchSB_SoundEvent`, `DispatchSB_PlayScriptType` (F755) | 11.4186 0x006AC6A0, 0x006ACC80, 0x006ACD20, 0x006AC730, 0x006ACAF0, 0x006ACBB0, 0x006AC760, 0x006AC6E0; 11.6096 0x006AD600, 0x006ADBE0, 0x006ADC80, 0x006AD690, 0x006ADA50, 0x006ADB10, 0x006AD6C0, 0x006AD640 |
| `Messages/World/ObjDescEventCodec.cs` | `CM_Physics::DispatchSB_ObjDescEvent` | 11.4186 0x006ACA20; 11.6096 0x006AD980 |
| `Messages/Chat/ChatCodecs.cs` | `CM_Communication::DispatchUI_HearSpeech`, `DispatchUI_HearRangedSpeech`, `DispatchUI_HearEmote`, `DispatchUI_HearSoulEmote` | 11.4186 0x006A4DB0, 0x006A4BB0, 0x006A4AC0, 0x006A4CC0; 11.6096 0x006A5D10, 0x006A5B10, 0x006A5A20, 0x006A5C20 |
| `Messages/Events/GameEventCodecs.cs` | `CM_Communication::DispatchUI_HearDirectSpeech` (the Tell's four words), `DispatchUI_PopUpString`, `DispatchUI_WeenieError`, `DispatchUI_WeenieErrorWithString`, `DispatchUI_TransientString`, `CM_Item::DispatchUI_UseDone`, `CM_Combat::DispatchUI_QueryHealthResponse` | 11.4186 0x006A49C0, 0x006A4EA0, 0x006A3AA0, 0x006A5070, 0x006A4FD0, 0x006A8510, 0x006A99A0; 11.6096 0x006A5920, 0x006A5E00, 0x006A48C0, 0x006A5FD0, 0x006A5F30, 0x006A9470, 0x006AA900 |
| `Messages/Actions/RawMotionStateCodec.cs` | `RawMotionState::Pack` (defaults compared by bits, 5-bit action count, 8-byte actions) | 11.4186 0x0051ED10; 11.6096 0x0051F820 |
| `Messages/Actions/MovementActionCodecs.cs` | `MoveToStatePack::Pack`, `AutonomousPositionPack::Pack`, `JumpPack::Pack`; their senders `CM_Movement::Event_MoveToState`, `Event_AutonomousPosition`, `Event_Jump` | 11.4186 0x005168F0, 0x00516AF0, 0x00516D10, 0x006AFC00, 0x006AF790, 0x006AFA70; 11.6096 0x005173F0, 0x005175F0, 0x00517810, 0x006B0AD0, 0x006B0660, 0x006B0940 |
| `Messages/Actions/ObjectActionCodecs.cs` | `CM_Inventory::Event_UseEvent`, `CM_Combat::Event_QueryHealth` | 11.4186 0x006AC3B0, 0x006A9B40; 11.6096 0x006AD310, 0x006AAAA0 |
| `Messages/Actions/ChatActionCodecs.cs` | `CM_Communication::Event_Talk`, `CM_Communication::Event_TalkDirectByName` (message, then target) | 11.4186 0x006A45C0, 0x006A4780; 11.6096 0x006A53E0, 0x006A55A0 |
| `Messages/Self/PlayerDescriptionCodec.cs` | `CPlayerSystem::Handle_PlayerDescription` (qualities, module, contents, placements; a predicted row), `CACQualities::UnPack`, `CBaseQualities::UnPack` (the property tables and their order), `AttributeCache::UnPack`, `Attribute::UnPack`, `SecondaryAttribute::UnPack`, `Skill::UnPack`, `CSpellBook::UnPack`, `SpellBookPage::UnPack` (the two entry forms), `CEnchantmentRegistry::UnPack` (multiplicative, additive, cooldown, vitae), `Enchantment::UnPack`, `PlayerModule::UnPack` (the option sections, the spell-bar count, the defaults, the align; a predicted row), `ShortCutManager::UnPack`, `GenericQualitiesData::UnPack`, `PackObjPropertyCollection::Serialize` (the gameplay options, not decoded) | 11.4186 0x005636A0, 0x00593420, 0x00595F10, 0x005CCFB0, 0x005CABF0, 0x005CAC90, 0x005CAAC0, 0x00595BB0, 0x005CC260, 0x00593FF0, 0x005CB040, 0x005D49D0, 0x005D5820, 0x006B79B0, 0x005D4F80; 11.6096 0x00564440, 0x005941D0, 0x00596E10, 0x005CDF00, 0x005CBB20, 0x005CBBC0, 0x005CBA30, 0x00596AB0, 0x005CD1B0, 0x00594E60, 0x005CBF70, 0x005D5B20, 0x005D6930, 0x006B8880, 0x005D60D0 |
| `Messages/Self/PrivateUpdateCodec.cs` | `CM_Qualities::DispatchUI_PrivateUpdateInt`, `DispatchUI_PrivateUpdateInt64`, `DispatchUI_PrivateUpdateSkill`, `DispatchUI_PrivateUpdateAttribute`, `DispatchUI_PrivateUpdateAttribute2nd` (0x02E7), `DispatchUI_PrivateUpdateAttribute2ndLevel` (0x02E9) | 11.4186 0x006AEA90, 0x006AEAD0, 0x006AEB20, 0x006AE800, 0x006AE8A0, 0x006AE930; 11.6096 0x006AF960, 0x006AF9A0, 0x006AF9F0, 0x006AF6D0, 0x006AF770, 0x006AF800 |
| `Session/SessionMachine.World.cs` (the world sends) | `Proto_UI::SendToWeenie` (every world action on queue 3), `Proto_UI::UICounterFailedSend` (a refused send uses no sequence) | 11.4186 0x00546810, 0x005467F0; 11.6096 0x005473D0, 0x005473B0 (a predicted row of the symbol table) |
| `Session/SessionConnection.cs` | `ClientNet::SendLoginRequest`, `ClientNet::SendConnectAck`, `ClientNet::HandleConnectionRequest` | 11.4186 0x00543F90, 0x005440F0, 0x00545AC0; 11.6096 0x00544B50, 0x00544CB0, 0x00546680 |
| `Session/SessionMachine.Handshake.cs` | `ClientNet::ProcessConnection` (the ConnectResponse every third of a second; the 140 s silence check, skipped when the last `UseTime` is older), `SharedNet::UseTime` (stamps the last use after reading every packet) | 11.4186 0x00545450, 0x00542450; 11.6096 0x00546010, 0x00543030 |
| `Session/SessionMachine.Inbound.cs` | `CPlayerSystem::Handle_CharacterError` | 11.4186 0x0055D5D0; 11.6096 0x0055E2F0 |
| `Session/SessionMachine.World.cs` | `Proto_UI::SendEnterWorldRequest`, `Proto_UI::SendEnterWorld`, `CM_Character::DispatchUI_EnterGame_ServerReady`, `CM_Character::Event_LoginCompleteNotification`; the queues: `Proto_UI::SendToWeenie` (3, game actions), `Proto_UI::SendToLogon` (4), `Proto_UI::SendToControl` (2, F7D9), `Proto_UI::SendToDatabase` (5, F7E6) | 11.4186 0x00546A00, 0x00546BC0, 0x006A0E50, 0x006A1480, 0x00546810, 0x005468F0, 0x00546880, 0x00546960; 11.6096 0x005475C0, 0x00547780, 0x006A1C70, 0x006A22A0, 0x005473D0, 0x005474B0, 0x00547440, 0x00547520 |
| `Session/SessionMachine.World.cs` (the GameAction sequence) | `CPlayerSystem::LogOnCharacter`, `CPlayerSystem::ExecuteLogOff`, `ClientNet::ExitWorldDisconnect`, `Proto_UI::SetEventCounter` (the counter restarts at each world entry and logoff) | 11.4186 0x0055F890, 0x0055D780, 0x00541E00, 0x00546800; 11.6096 0x00560600, 0x0055E4A0, 0x005429E0, 0x005473C0 |
| `Session/SessionMachine.Characters.cs` | `CPlayerSystem::RestoreCharacter` (calls the next with empty name and account), `Proto_UI::SendAdminRestoreCharacter` (the F7D9 layout) | 11.4186 0x0055D760, 0x00546CF0; 11.6096 0x0055E480, 0x005478B0 |
| `Session/SessionMachine.Logoff.cs` | `Proto_UI::LogOffCharacter`, `ClientNet::LogOffServer` (the Disconnect header) | 11.4186 0x00546A20, 0x00543EF0; 11.6096 0x005475E0, 0x00544AB0 |

The `Messages/` and `Session/` rows are under `src/Lifestone.Net/`. The 11.6096 addresses come from the pinned symbol
table that docs/research/retail-addresses-m2.md describes (`symbol_transplant_11.6096.tsv`, SHA-256
`cc19490d457fb8906c3c2bde58bd210830a64d5b632a1d696b401a639ff654d7`); every row used is "exact" or "verified" except
the three marked above as predicted (`CPlayerSystem::Handle_PlayerDescription`, `PlayerModule::UnPack` and
`Proto_UI::UICounterFailedSend`). The decompiled functions were read one file at a time and re-expressed; nothing is
pasted.

## Own implementations (no source copied)

- **The session contract** (`Session/`: `IAcSession`, `AcSession`, the options, timeouts, retry policy, phases,
  events, failures, `AcCredentials`), the **dispatcher** and its handler interface (`Dispatch/`), the **raw tap**
  (`Taps/`) and the **diagnostics** (`INetLog`, `LinkStats`): written for Lifestone from docs/protocol.md and
  m2-plan §4.6.
- **The session itself** (`Session/SessionMachine*.cs`, `SessionConnection.cs`) and the **inbound sequencer**
  (`Transport/InboundSequencer.cs`), N5 and N6: see "The session" above.
- **The loopback gate** (`Sockets/LoopbackPolicy.cs`, `LoopbackRequiredException.cs`) and the socket seam
  (`IDatagramSocket`): Lifestone's own rule; the retail client has no such check.
- **The wire records** (`Types/`, `Messages/`): field-for-field shells of the layouts in docs/protocol.md §5, whose
  sources are listed there.

## Defects fixed in the OpenAC import

The four defects docs/protocol.md §8.2 found, and the ones the steps found after it. Each fix is named in the file's
port header and in its step's section above.

| Defect in OpenAC @1ee5a08 | Fixed in | Checked by |
|---|---|---|
| **1.** RawMotionState actions lack their f32 speed (`RawMotionStatePacker.cs:55-60`): with one action ACE reads the position cell as 0x42A80000 | N7, `Messages/Actions/RawMotionStateCodec.cs` | `WorldActionCodecTests` (the §10 vector, 80 bytes, and the 76-byte counterfactual) |
| **2.** The UpdateMotion sticky GUID is read without the align before it (`UpdateMotion.cs:156-160`) | N7, `Messages/World/MovementDataCodec.cs` | `UpdateMotionCodecTests` (0 to 4 u16 fields; OpenAC's layout refused) |
| **3.** Strings are written as ASCII, and the String32L prefix byte is 0 (`PacketWriter.cs`) | N2, `Packets/PacketWriter.cs`, `Wire/Cp1252.cs`, `Wire/AsciiGuard.cs` | `StringEncodingTests`, `PacketWriterTests` |
| **4.** The optional-section parser skips EchoResponse and NetError (`PacketCodec.cs`) | N2, `Packets/PacketCodec.cs`, `Packets/BorrowedPacket.cs` | `OptionalHeaderTests` |
| `NetClient.cs:19` binds every interface | N2, `Sockets/UdpLoopbackSocket.cs` (loopback only, two source ports) | `UdpLoopbackSocketTests`, `LoopbackPolicyTests` |
| A sealed packet is sent before it is stored, so a send that throws loses a spent sequence and ACE stalls (`OutboundFlowQueue.cs:133-145`) | N2, `Transport/OutboundFlowQueue.cs` (store-then-send) | `StoreThenSendTests`, `OutboundFragmentationTests` |
| `Stopwatch` clocks, `ACDREAM_*` environment reads and console writes | N1 (a `TimeProvider`), N7 (`UpdateMotion.cs:60-65`); the other files were not taken | `SourceRulesTests`; the repository's `ArchitectureTests` |
| Codecs return half-read records, throw `FormatException` or catch exceptions, and accept trailing bytes | N3, N7, N10: every parser is `TryParse` over `WireReader` and must read every byte | each codec's tests, `MessageBodyFuzzTests` |
| `CreateObject.cs:302` scales the rotation to unit length | N7, `Messages/World/PhysicsDescCodec.cs` (raw) | `CreateObjectCodecTests` |
| `PublicWeenieDescBody.cs` widens or signs several fields and skips PScript | N7, `Messages/World/WeenieDescCodec.cs` | `CreateObjectCodecTests` (every field, one flag at a time) |
| `CharacterRestore.cs` sends the GUID alone; retail's `Proto_UI::SendAdminRestoreCharacter` sends GUID, name and account | N3, `Messages/Login/CharacterRestoreCodec.cs` | `CharacterRequestCodecTests` |
| `GameActionLoginComplete.cs` writes 0 as the action sequence | N3, `Messages/Actions/LoginCompleteCodec.cs` | `CharacterRequestCodecTests`, `EnterWorldTests` |
| `DddInterrogationResponse.cs` never reports the high-res dat | N3, `Messages/Login/DddCodec.cs` (with product bit 0x4) | `DddResponseCodecTests`, `HandshakeTests` |
| `PlayerDescriptionParser.cs` reads one f32 per spell, skips the timestamp format and the generic options data, refuses non-finite enchantment values and searches for the inventory heuristically | N10, `Messages/Self/PlayerDescriptionCodec.cs` | `PlayerDescriptionCodecTests` |
| A server packet whose checksum fails keeps its admission, so a corrupt sequence moves the inbound window (`InboundSequenceTracker.cs`) | N8, `Transport/InboundSequenceTracker.cs` (`Reject`) | `RejectShiftTests`, `SessionFuzzTests` |
| Not OpenAC's alone: against ACE, an ack-only packet can take a lost data packet's place, and the client cannot recover it | N8, `Transport/OutboundFlowQueue.cs` (held acks, with the tail probe) | `TailProbeTests`, `VirtualSoakTests` |

## Licence texts

The ACE code above (two enums and the test-only `CryptoSystem` oracle) is AGPL-3.0, Lifestone's own licence (`LICENSE`).
The OpenAC code is used under its MIT licence, reproduced here as that licence requires (and in
`src/Lifestone.Net/THIRD-PARTY-NOTICES.md`):

```text
MIT License

Copyright (c) 2026 Erik Nihlén and OpenAC contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The motion command table's values and names come from DatReaderWriter, under its MIT licence (`LICENSE.txt` at
a9345f2):

```text
Copyright 2024 ACClientLib

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the “Software”), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
