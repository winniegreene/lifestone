# Lifestone network protocol: AC1 client and local ACE

This is the wire spec and stack decision for `src/Lifestone.Net`, the layer that connects the Godot client to the bundled localhost ACEmulator (ACE). It specifies what ACE actually sends and accepts. Where ACE is lenient, retail behaviour (from the decompiled client) is noted next to it.

Research date: 2026-10-09. Nothing third-party was built or run for this document. All byte vectors in §10 come from our own stdlib-Python transliteration of the cited ACE code.

## Pinned sources

| Source | Version read | Licence | Role |
|---|---|---|---|
| ACEmulator/ACE | `master` @ `7715c052c2038a96e57c477789cafd4d870e0d4d` (2026-10-08). Sparse, blobless, shallow clone at `vendor-eval/ACE-proto/` (gitignored) | AGPL-3.0 | **Ground truth** for server behaviour. Read only |
| eriknihlen/OpenAC `src/AcDream.Core.Net` | `main` @ `1ee5a08581a7f4b0b22effdf911171a76cf4391c` (2026-10-08, v0.1.24). Clone at `vendor-eval/OpenAC/` made by another agent | MIT | Candidate 1 |
| Chorizite/Chorizite.ACProtocol | `master` @ `ff7dffd6afa53c4e2b2ba14bd89962258ebd4371` (2025-10-04). Shallow clone at `vendor-eval/Chorizite.ACProtocol/` | MIT; its code is generated from an unlicensed `protocol.xml` | Candidate 2 |
| merklejerk/holtburger | `main` (pushed 2026-10-03). Read via `gh api`: protocol/session ARCHITECTURE.md, FIXTURES.md, `movement/actions.rs`, `session/auth.rs` | AGPL-3.0 | Reference only |
| Mag-nus/acclient-AI-RE | `2013-09 11.4186/acclient-src`: `PORTAL/cphysobj/CPackClasses.c`, `PORTAL/canim/CMInterpPrim.c`, `GAME/game_cevents/CM_Movement.c`; `2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md` | MIT | Retail pack layouts. Networking is unchanged between 11.4186 and 11.6096 per doc 13 |
| User's `client/acclient.exe` | 11.6096, MD5 `116d9a66a70b6af449dc3a28d82f2f6d` | Retail, user-owned | Command table, parsed read-only (§7) |
| User's `client/client_portal.dat` | Iteration 2072 | Retail, user-owned | CharGen starter areas, read-only (§4.3) |

Path notation: `ACE:` means `Source/` in the ACE repo (for example `ACE:ACE.Server/Network/NetworkSession.cs`). `OAC:` means `src/AcDream.Core.Net/` in OpenAC. `CHZ:` means `Chorizite.ACProtocol/` in that repo.

## Conventions

- Everything is **little-endian**. Types are written u8/u16/u32/i32/u64/f32/f64.
- **String16L.** A u16 byte count, then the bytes (CP1252), then zero padding so that (2 + count) is a multiple of 4. Source: `ACE:ACE.Server/Network/Extensions.cs WriteString16L`, `ACE:ACE.Common/Extensions/BinaryReaderExtensions.cs ReadString16L`.
- **String32L.** Used only for the password in LoginRequest. A u32 equal to (n + 1); then the string length as a 1-byte prefix, or 2 bytes when n > 255; then n bytes; then padding to 4, counted from the u32.
- **PackedDword.** A value ≤ 0x7FFF is written as one u16. Anything larger is two u16s: first `(v >> 16) | 0x8000`, then `v & 0xFFFF`.
  - **Known-type PackedDword.** The writer subtracts the DAT type prefix (palette 0x04000000, surface texture 0x05000000, GfxObj 0x01000000, icon 0x06000000) when present. The reader ORs the prefix back on, except when the value is 0.
  - Source: `ACE:ACE.Server/Network/Extensions.cs`.
- **Align.** Pads with zero bytes to a 4-byte boundary, **measured from the first byte of the message, which is the u32 opcode**. It is not measured from the fragment or the packet. Retail does the same (`PackObj::ALIGN_PTR`).
- **Quaternions** are written W, X, Y, Z. Positions are `{u32 objCellId, f32 x, f32 y, f32 z}`, with coordinates relative to the landblock.
- **Object GUID ranges** (`ACE:ACE.Entity/ObjectGuid.cs`):

  | Range | Kind |
  |---|---|
  | 0x50000001–0x5FFFFFFF | Players |
  | 0x70000000–0x7FFFFFFF | Static world objects (`0x7XXYYnnn` follows the landblock) |
  | 0x80000000–0xFFFFFFFE | Dynamic objects |

---

## 0. Decision summary

1. **Vendor OpenAC's transport into `src/Lifestone.Net`** (`Cryptography/`, `Packets/`, `Transport/`, `NetClient.cs`; about 3.1k lines with no dependency on `AcDream.Core`). Also take a curated set of its message codecs, list in §9.
   - Write our own small session state machine; do **not** take OpenAC's 3,028-line `WorldSession.cs`.
   - Fix the four defects found in §8.2 before first use.
2. **Do not use Chorizite.ACProtocol for wire I/O.**
   - It has no transport layer.
   - Its command enum predates the 11.6096 shift: 136 of 408 IDs are wrong by exactly −3, verified against the user's exe.
   - Its RawMotionState and Jump codecs do not match retail or ACE.
   - It has not been touched since 2025-10-04.
   - It is still useful as a reference index of message names.
3. **ACE stays the oracle.**
   - Golden bytes come from ACE's serializers, via ACE's own packet log on the loopback server (§9.4).
   - Retail-only details come from the decompiled pack functions.
4. holtburger (AGPL) is a reference only. It independently confirms two quirks: the ConnectResponse delay and the start-at-2 sequence. Its fixture methodology is the model for our test strategy.

---

## 1. Topology: ports, endpoints, sessions

| Item | Value and behaviour | Source |
|---|---|---|
| Listeners | Two UDP sockets, `Host:Port` (default **9000**) and `Host:Port+1` (**9001**), both bound to `Config.js` `Server.Network.Host` | `ACE:ACE.Server/Network/Managers/SocketManager.cs` |
| Bind address | The shipped default is **`0.0.0.0`**. Lifestone's bundled `Config.js` **must** set `"Host": "127.0.0.1"`: security rule 4, and it avoids a Windows Firewall prompt | `ACE:ACE.Server/Config.js.example` L6–8 |
| Port 9000, client to server | All client traffic goes here: LoginRequest first, then every later client packet. Before the handshake completes, the server also *replies* from 9000 (ConnectRequest and login rejections) | `NetworkManager.ProcessPacket` (else-branch), `NetworkSession.SendPacketRaw` (uses the 9000 socket while `EndPointS2C` is null) |
| Port 9001, server to client | The client sends **only the ConnectResponse** here. ACE takes the source endpoint of that packet as `EndPointS2C`. From then on, *all* server traffic leaves the 9001 socket for that endpoint | `NetworkManager.ProcessPacket` (Port+1 branch), `Session.SetS2CEndpoint` |
| Client socket | Use **one** UDP socket for everything. ACE matches later 9000 packets by `sessionMap[header.Id]` *and* checks that the source endpoint equals the LoginRequest's endpoint. ConnectResponse is matched by cookie and source IP | `NetworkManager.ProcessPacket` |
| Client bind | Bind the client socket to `127.0.0.1:0` when the server is local (OpenAC binds `0.0.0.0:0`, which must be changed) | `OAC:NetClient.cs` L19 |
| Session id | ConnectRequest assigns a `u16` client id (the slot in ACE's session array). Every later client packet must carry it in header `Id`. All server packets carry `Id = 0x000B` (`NetworkManager.ServerId`), which the client can ignore | `NetworkManager.FindOrCreateSession`, `NetworkSession.FlushPackets` |
| Timeouts | Before login completes: **15 s** (`AuthenticationHandler.DefaultAuthTimeout`). After: `DefaultSessionTimeout`, default **60 s**. Any CRC-valid packet resets the timer, except a cleartext NAK, which returns before the reset. On expiry the session is terminated | `NetworkSession.ProcessPacket`, `Session.TickOutbound` |
| Datagram limits | Client to server: ACE's receive buffer is **1024 bytes** (`ClientPacket.MaxPacketSize`), and fragments over 464 bytes are rejected. Server to client: payload ≤ **464**, so datagrams ≤ **484** bytes | `ConnectionListener.cs`, `ClientPacketFragment.Unpack`, `ServerPacket.MaxPacketSize` |

Retail used 9000/9001 for the login server and separate port pairs for world servers. ACE does every role on one pair, so ServerSwitch, Referral and WorldLoginRequest exist only as dead code (`ACE:ACE.Server/Network/Packets/PacketOutboundReferral.cs`, `PacketOutboundServerSwitch.cs` have no callers).

---

## 2. Packet layer

### 2.1 Packet header (20 bytes)

| Offset | Type | Field | Notes |
|---|---|---|---|
| 0 | u32 | Sequence | §2.8 |
| 4 | u32 | Flags | §2.2 |
| 8 | u32 | Checksum | §2.6 |
| 12 | u16 | Id | Client to server: the client id from ConnectRequest (0 for LoginRequest, ConnectResponse and the pre-session Disconnect case). Server to client: 0x000B |
| 14 | u16 | Time | Server to client: `(ushort)PortalYearTicks`. Client to server: ignored by ACE (OpenAC sends its interval clock) |
| 16 | u16 | Size | Byte count after the header (optional section plus fragments) |
| 18 | u16 | Iteration | Server to client: always 1. Client to server: ignored (OpenAC echoes the ConnectRequest value) |

Source: `ACE:ACE.Server/Network/PacketHeader.cs`.

### 2.2 Header flags and optional sections

The optional sections appear **in ascending bit order**, immediately after the header and before any fragments. "C→S" is client to server; "S→C" is server to client.

| Bit | Name | Optional payload | Direction and ACE behaviour |
|---|---|---|---|
| 0x00000001 | Retransmission | – | Set on resent packets, in both directions |
| 0x00000002 | EncryptedChecksum | – | Checksum is keyed by ISAAC (§2.7). Never combined with RequestRetransmit |
| 0x00000004 | BlobFragments | – | Fragments follow the optional section |
| 0x00000100 | ServerSwitch | 8 bytes | Unused by ACE |
| 0x00000200 | LogonServerAddr | (unparsed by ACE) | Unused |
| 0x00000800 | Referral | (variable) | Unused |
| 0x00001000 | RequestRetransmit (NAK) | u32 n, then n × u32 sequences | Both directions. Must be **cleartext** |
| 0x00002000 | RejectRetransmit | u32 n, then n × u32 sequences | Server to client: "these sequences are no longer cached" |
| 0x00004000 | AckSequence | u32 highest sequence received | Both directions |
| 0x00008000 | Disconnect | – | Client to server: ACE terminates the session |
| 0x00010000 | LoginRequest | Rest of packet (§4.1) | Client to server, to 9000 |
| 0x00020000 | WorldLoginRequest | u64 | Unused |
| 0x00040000 | ConnectRequest | 32 bytes (§4.1) | Server to client, from 9000 |
| 0x00080000 | ConnectResponse | u64 cookie | Client to server, **to 9001** |
| 0x00100000 / 0x00200000 | NetError / NetErrorDisconnect | (ACE does not parse a body) | Client NetErrorDisconnect terminates the session |
| 0x00400000 | CICMDCommand | 8 bytes | Ignored |
| 0x01000000 | TimeSync | f64 | Server to client every 20 s (§2.10). ACE ignores the client's |
| 0x02000000 | EchoRequest | f32 client time | Client to server (optional) |
| 0x04000000 | EchoResponse | f32 echoed client time, f32 (server time − client time) | Server to client, only after an EchoRequest |
| 0x08000000 | Flow | u32 bytes, u16 interval | Client to server. ACE parses and ignores it |

Source: `ACE:ACE.Server/Network/PacketHeaderFlags.cs` and `PacketHeaderOptional.cs` (the parse order is the authority). ACE writes server optional sections as Ack, then TimeSync, then EchoResponse (`NetworkSession.WriteOptionalHeaders`).

### 2.3 Fragments (16-byte header, then data)

| Offset | Type | Field | Notes |
|---|---|---|---|
| 0 | u32 | Sequence | Message id. Every fragment of one message shares it. Server to client: `FragmentSequence++` per message, **starting at 0**. Client to server: it **must start at 1 and rise by exactly 1 per message**. ACE hands a message to its handler only when its sequence equals `lastReceivedFragmentSequence + 1` (initially 0, so 1 is expected first) and holds later ones in a buffer. A client that starts at 0 loses that message for good; skipping a number stalls every later message (`NetworkSession.ProcessFragment`, `CheckOutOfOrderFragments`). OpenAC starts at 1 |
| 4 | u32 | Id | Server to client: always 0x80000000. Client to server: ignored (OpenAC sends 0x80000000) |
| 8 | u16 | Count | Number of fragments in the message |
| 10 | u16 | Size | **Including** this 16-byte header. 16–464 |
| 12 | u16 | Index | 0..Count−1 |
| 14 | u16 | Queue | The `GameMessageGroup` (below). Ignored on client-to-server traffic |

- A message is split into 448-byte data chunks.
- **Quirk:** when a packet has room, ACE may send the *tail* fragment (the last index) before the middle ones (`NetworkSession.SendBundle`, `MessageFragment.GetTailFragment`). Reassembly must therefore key on (Sequence, Index) and not rely on arrival order.
- **Groups (queues):**

  | Value | Group |
  |---|---|
  | 0 | Invalid (used for ack and timesync bundles) |
  | 1 | Event |
  | 2 | Control |
  | 3 | Weenie |
  | 4 | Login |
  | 5 | Database |
  | 6 | SecureControl |
  | 7 | SecureWeenie |
  | 8 | SecureLogin |
  | 9 | UI |
  | 10 | Smartbox |
  | 11 | Observer |

  Source: `ACE:ACE.Server/Network/GameMessageGroup.cs`.
- ACE bundles each group separately, every tick (every 5 ms or more). UI-queue messages are kept strictly in order.

### 2.4 Checksum (Hash32)

`Hash32(bytes)`:

1. Start with `len << 16`.
2. Add every complete little-endian u32 word, mod 2³².
3. Add the 0–3 tail bytes, shifted to bits 24, 16 and 8 in that order.

Source: `ACE:ACE.Common/Cryptography/Hash32.cs`.

Packet checksum:

```
headerHash  = Hash32(20-byte header with Checksum field = 0xBADD70DD)
optionalHash = Hash32(bytes of the optional section)           # 0 if empty
fragHash_i  = Hash32(16-byte fragment header) + Hash32(fragment data)
payloadHash = optionalHash + Σ fragHash_i                      (mod 2^32)

cleartext:  Checksum = headerHash + payloadHash
encrypted:  Checksum = headerHash + (payloadHash XOR isaacKey)
verify(enc): isaacKey == (Checksum − headerHash) XOR payloadHash
```

Sources: `PacketHeader.CalculateHash32`, `ClientPacket.VerifyCRC`, `ServerPacket.CreateReadyToSendPacket`, `ClientPacketFragment.CalculateHash32`.

Note: when sending, ACE hashes its whole optional section as one block. When receiving, it hashes the optional bytes it re-serialises while parsing (`PacketHeaderOptional.CalculateHash32`). Both are byte-identical to what is on the wire, so the client can simply hash the raw optional bytes.

### 2.5 ISAAC keystream (AC variant)

`ACE:ACE.Common/Cryptography/ISAAC.cs` and `CryptoSystem.cs`.

**Initialisation:**

1. `mm[]` and `rsl[]` start at zero, and the eight mix words start at 0x9E3779B9.
2. Run four mixing rounds.
3. Run two passes over `mm`: the first folds `rsl` in (all zero), the second folds `mm`. This is standard ISAAC `randinit(flag = true)` with an all-zero seed array.
4. Then **set a = b = c = the u32 seed** (a non-standard step) and run one `isaac()` scramble.
5. `Next()` returns `rsl[255]`, `rsl[254]`, … down to `rsl[0]`, then re-scrambles. It reads **backwards**.

**Seeds** come from ConnectRequest:

- The first seed (server outgoing) keys **server-to-client** packets.
- The second seed (client outgoing) keys **client-to-server** packets.

**Server receive tolerance.** `CryptoSystem` keeps `CurrentKey`, which is the *first* `Next()` taken at construction, plus a set of up to 256 skipped keys. A client packet is accepted if its recovered key equals `CurrentKey` or is in the skipped set. Otherwise ACE advances up to (256 − |skipped|) keys looking for it. In practice the client may lose or reorder up to about 256 encrypted packets without desynchronising. An unrecoverable key means the packet is silently dropped and counted as a CRC error.

**Rules for the client:**

- Draw one client-to-server key per **new** encrypted packet, in sequence order.
- A retransmission reuses the original key: recompute only the header hash.
- On the receive side, the server draws one key per encrypted packet *in sequence order* (`FlushPackets` order equals sequence order). For gaps, park keys keyed by sequence; OpenAC's `InboundSequenceTracker` does this correctly.

Cross-check result: our transliteration of ACE's ISAAC reproduces OpenAC's `IsaacRandomTests` 16-word golden vector exactly (§10).

### 2.6 Which packets are encrypted

| Direction | Encrypted (ISAAC) | Cleartext |
|---|---|---|
| Server to client | Every bundle carrying messages, TimeSync or EchoResponse (`NetworkSession.EnqueueSend` and `Update` set `EncryptedChecksum`) | ConnectRequest; ack-only bundles; RequestRetransmit (server NAK); RejectRetransmit |
| Client to server (what ACE accepts) | Packets with fragments (retail and OpenAC always encrypt these) | LoginRequest, ConnectResponse, ack-only, NAK (**must** be cleartext: ACE only services `RequestRetransmit` when `EncryptedChecksum` is clear), Disconnect |

ACE also accepts *cleartext* client packets that carry fragments (`VerifyCRC` only takes the ISAAC path when the flag is set). Do not rely on this; send them encrypted as retail does.

### 2.7 Sequence numbers

**Client to server** (`NetworkSession.ProcessPacket`; `lastReceivedPacketSequence` starts at **1**):

| Packet | Sequence |
|---|---|
| LoginRequest | 0 |
| ConnectResponse | 1 (by convention: OpenAC and holtburger both use 1; port 9001 does not check it) |
| **First data packet** | **2**, then +1 for each new packet |

- A packet with sequence ≤ last received, other than 0, is **dropped as a duplicate**. The one exception is a packet whose flags are *exactly* `AckSequence` with sequence == last received.
- So client ack-only packets carry the sequence of the **last data packet sent**, with no increment (OpenAC: `Sequence = HighestIdSent`, initially 1).
- A packet that arrives ahead of the expected sequence is buffered.
  - If it is 2 or more ahead, ACE also NAKs the missing sequences, at most once per second, with up to 115 ids per NAK.
  - So a single missing packet N is NAKed only after packet N+2 arrives.
- If a packet is more than about 256 ahead, ACE **terminates** the session (`AbnormalSequenceReceived`).

**Server to client** (`NetworkSession.FlushPackets`):

| Packet | Sequence |
|---|---|
| ConnectRequest | 0 (`UIntSequence(false)` wraps from MaxValue to 0) |
| First encrypted packet | 2 (the counter resets to 1, then increments) |

- Ack-only and server NAK packets reuse the **current** sequence without incrementing.
- **Quirk:** a RejectRetransmit packet is cleartext but **does** consume a new sequence number, while consuming no ISAAC key. A client that parks keys for missing sequences must hand a parked key back when a cleartext sequence fills that gap (OpenAC: `InboundSequenceTracker.OnCleartextRejectSequence`).
- Server packets with sequence ≥ 2 that are not NAKs are cached for **120 s** for retransmission (`cachedPacketRetentionTime`).

### 2.8 Acks, NAKs, retransmit

| Side | Behaviour | Source |
|---|---|---|
| Server acks | Every **2 s** the server sets `AckSequence = lastReceivedPacketSequence` on the next bundle (this may be an ack-only cleartext packet). The first ack is delayed by 2 s after session start | `NetworkSession.Update` |
| Client acks | Send `AckSequence = highest server sequence received` about every 2 s (OpenAC's `AckNakScheduler`: 2 s ack gate, 0.6 s NAK gate). ACE uses it to prune its cache (`AcknowledgeSequence` removes entries < ack) | `OAC:Transport/AckNakScheduler.cs` |
| Client NAK | Cleartext packet: flags = RequestRetransmit, body `u32 n` then n sequences. ACE resends each cached packet with Retransmission (0x1) set, the same ISAAC key and a recomputed header hash. Missing ones are reported in a RejectRetransmit packet | `NetworkSession.ProcessPacket`, `Retransmit`, `PacketRejectRetransmit.cs` |
| Server NAK | When a client sequence gap of 2 or more appears, the server sends RequestRetransmit (cleartext). The client must resend from its own cache with the Retransmission flag, keeping the original ISAAC key | `DoRequestForRetransmission` |

### 2.9 TimeSync, Echo, Flow

- **TimeSync** (server to client): `f64 PortalYearTicks`, the server's in-game clock in seconds. Use it to drive Dereth time of day.
  - It is sent once immediately after ConnectResponse (`sendResync = true`), then every **20 s**.
  - The value inside ConnectRequest is the same clock, but ACE's own comment says the retail client ignores it.
  - Sources: `Timers.cs`, `WorldManager.cs` L391.
- **EchoRequest / EchoResponse.**
  - The client sends `f32 clientTime`. The next server bundle carries `f32 clientTime` and `f32 (PortalYearTicks − clientTime)`.
  - ACE also runs a **speed-hack check** (`NetworkSession.VerifyEcho`). Starting from the first echo, if the client clock drifts from the server's wall clock by more than 2 s, and the drift is still growing, on **10 consecutive** echoes, the player is logged off ("TimeSync: client speed error").
  - Either do not send echoes in M2, or send them from a monotonic wall-clock source.
- **Flow** (client to server): optional. ACE ignores it.

### 2.10 Disconnect

- Client: send a cleartext packet with Sequence 0, Flags = Disconnect (0x8000), Id = client id and an empty body. ACE terminates the session immediately (OpenAC `TransportDisconnect.cs`).
- The graceful order is: CharacterLogOff (F653), wait for the server's F653 and CharacterList, then Disconnect.

### 2.11 Delivery order

ACE processes **client** packets strictly in order (out-of-order packets are buffered). The retail client and OpenAC process **server** packets as they arrive and NAK the gaps. Stale updates are made harmless by per-object sequence numbers (§5.3).

Lifestone should do both:

- Apply object updates only when their sequence is newer (u16 wrap-safe compare).
- Process UI-queue messages in fragment-sequence order.

---

## 3. Message layer

- A **message** is the reassembled fragment data. It begins with a `u32 opcode`. Unknown opcodes are safe to skip, because every message is length-delimited by its fragments.
- **GameEvent envelope, opcode 0xF7B0, server to client:**

  | Type | Field | Notes |
  |---|---|---|
  | u32 | Player GUID | 0 before enter-world |
  | u32 | Event sequence | Reset to 1 at enter-world (`Session.InitSessionForWorldLogin`), +1 per event |
  | u32 | Event type | |
  | … | Payload | |

  Source: `ACE:ACE.Server/Network/GameEvent/GameEventMessage.cs`.
- **GameAction envelope, opcode 0xF7B1, client to server:**

  | Type | Field | Notes |
  |---|---|---|
  | u32 | Action sequence | ACE ignores it ("TODO: verify sequence") |
  | u32 | Action type | |
  | … | Payload | |

  Handled only in state `WorldConnected` with a player present (`GameActionPacket.cs`, `InboundMessageManager.HandleGameAction`).
- **Handler gating:** every top-level client message handler is tied to one `SessionState` (`GameMessageAttribute`). A message sent in the wrong state is **silently ignored** (`InboundMessageManager.HandleClientMessage`). The states are `AuthLoginRequest`, `AuthConnectResponse`, `AuthConnected` (character select) and `WorldConnected`.

---

## 4. Session flow: handshake to world

### 4.1 Handshake (packet level)

| # | Direction | Packet | Contents | Gotchas (ACE refs) |
|---|---|---|---|---|
| 1 | C→S :9000 | **LoginRequest**: seq 0, flags 0x00010000, Id 0, cleartext | String16L client version (**must be "1802"**, else BootAccount); u32 remaining byte count; u32 NetAuthType (2 = account + password; 1 = account only, rejected; 0x40000002 = GLS ticket, rejected); u32 AuthFlags (0); u32 timestamp; String16L account (≤ 50 chars); String16L login-as (empty; admin feature); String32L password | `PacketInboundLoginRequest.cs`, `AuthenticationHandler.cs`. An unknown account with a non-empty password is **auto-created** with its name **lower-cased** (`AllowAutoAccountCreation`, default true). The first account created while no admin exists becomes **Admin** (`DatabaseManager.cs` L30–36). Never log this packet: it holds the password in clear |
| 2 | S→C from :9000 | **ConnectRequest**: seq 0, flags 0x00040000, cleartext | f64 server time; u64 cookie; u32 client id; u32 **server-to-client ISAAC seed**; u32 **client-to-server ISAAC seed**; u32 0 | `PacketoutboundConnectRequest.cs`. Sent from a background task (`DoLogin`) |
| 3 | C→S **:9001** | **ConnectResponse**: seq 1, flags 0x00080000, Id 0, cleartext | u64 cookie | **Race:** ACE sets `State = AuthConnectResponse` only *after* queuing the ConnectRequest and doing a DB write. A ConnectResponse that arrives earlier is **silently ignored** (`NetworkManager` requires that state). Delay it by about 200 ms and resend about every 0.33 s until the first encrypted server packet arrives. OpenAC (`WorldSession.Connection.cs`) and holtburger (`ACE_HANDSHAKE_RACE_DELAY_MS`) both do this |
| 4 | S→C from :9001 | First encrypted bundle (seq 2) | TimeSync; then messages CharacterList F658, ServerName F7E1, DDD_Interrogation F7E5 | `AuthenticationHandler.HandleConnectResponse`. Requires the world to be **Open**; otherwise WorldClosed termination, so wait for ACE's "world open" before connecting |
| 5 | C→S :9000 | Data packets from seq 2 (encrypted), acks every 2 s | | §2.7 |

### 4.2 Character select

| # | Direction | Message | Notes |
|---|---|---|---|
| 6 | S→C | F658 CharacterList | Remember the **account string from this message**. CharacterCreate and EnterWorld must send exactly that string: `clientString != session.Account` causes a silent ignore (CharacterCreate) or `EnterGameCharacterNotOwned` (EnterWorld) |
| 7 | S→C | F7E5 DDD_Interrogation | |
| 8 | C→S | F7E6 DDD_InterrogationResponse | Report the **exact** iteration of each DAT: portal 2072, cell 982, language 994 for our end-of-retail DATs (local-scout.md). With `EnableDATPatching = false` (the default), a lower client value means "update required" and the session is booted; a higher value means "newer than server" and it is also booted. A cell value of 0 is tolerated. Send highres only if the server has `client_highres.dat` (`DDDHandler.cs`) |
| 9 | S→C | F7EA DDD_EndDDD | Data check complete. (An F7E7 BeginDDD instead means a mismatch: abort with an error) |
| 10a | C→S | *(optional)* F656 CharacterCreate | S→C F643 CharacterCreateResponse (§5.2). On success the new character is appended to the session, but the server does **not** resend CharacterList; add it locally |
| 10b | S→C | F659 CharacterError at any time | Error code, for example 0x0F (not owned), 0x15 (server full) |

### 4.3 Enter world

| # | Direction | Message | Notes |
|---|---|---|---|
| 11 | C→S | F7C8 CharacterEnterWorldRequest (opcode only) | |
| 12 | S→C | F7DF EnterWorld ServerReady (opcode only) | |
| 13 | C→S | F657 CharacterEnterWorld: u32 GUID, String16L account | State moves to `WorldConnected` (`CharacterHandler.CharacterEnterWorld`) |
| 14 | S→C | GameEvent 0x0013 PlayerDescription, 0x0029 CharacterTitle, 0x0021 FriendsListUpdate; **F746 PlayerCreate** (u32 GUID); **F745 CreateObject(self)**, whose PhysicsDesc carries the spawn Position; F745 for each inventory and wielded item plus GameEvent 0x0196 ViewContents; GameEvent 0x0314 ContractTrackerTable; TurbineChat notices (GameEvent 0x028A WeenieError, 0x0295 SetTurbineChatChannels; `use_turbine_chat` is true by default) | `Player_Networking.cs` `PlayerEnterWorld` and `SendSelf` |
| 15 | S→C | F745 CreateObject for every object the player's ObjMaint can see (landblock statics, NPCs, creatures); GameEvent 0x0004 PopupString (welcome, first login); F7E0 ServerMessage (MOTD) | `WorldManager.DoPlayerEnterWorld`, `LandblockManager.AddObject` |
| 16 | C→S | **GameAction 0x00A1 LoginComplete** (no payload), sent when the client leaves portal space | **Until this arrives the player is `Teleporting` and every MoveToState or AutonomousPosition position is ignored** (`SetRequestedLocation` is skipped). The server then materialises the player: F74B SetState with Hidden and IgnoreCollisions cleared (`GameActionLoginComplete.cs`, `Player_Location.OnTeleportComplete`) |
| 17 | C↔S | Movement loop (§5.4, §6) | |
| 18 | C→S | F653 CharacterLogOff, then S→C F653, F658 CharacterList, F7E1 ServerName (after about 6 s); then C→S Disconnect | `Session.LogOffPlayer` and `SendFinalLogOffMessages` |

**Where a new character appears.** This was verified by decoding CharGen 0x0E000002 from the user's `client_portal.dat` (§10):

- ACE places a new character at `CharGen.StarterAreas[startArea].Locations[0]` (`ACE:ACE.Server/Factories/PlayerFactory.cs` L356–362).
- In our DATs, StartArea 0 ("Holtburg") resolves to **cell 0x860201AD**. That is an indoor EnvCell (index ≥ 0x100) in landblock 0x8602, not Holtburg town 0xA9B4.
- It is almost certainly the **Training Academy**. That is an inference: 0x8602 is not a town landblock, and ACE separately sets the character's `Instantiation` to the "Free Ride to Holtburg" spell destination (`PlayerFactory.cs` L364–385).
- The other starter areas (Shoushi, Yaraq, Sanamar) resolve to the same in-cell offset, (12.32, −28.482, 0.005), in other 0x01AD cells.

To reach Holtburg for the milestone, either:

- use the academy exit portal; or
- as the auto-promoted Admin, send GameAction 0x0015 Talk with `@telepoi holtburg` (`AdminCommands.cs` L875). This yields F751 PlayerTeleport and F748 UpdatePosition, after which the client must send **LoginComplete again**.

`@myloc` (Developer and above) reports the server-side position as F7E0. ACE's hard-coded fallback location is `0xA9B40019 (84, 7.1, 94)` (`WorldManager.cs` L212).

---

## 5. Minimal message set for M2

M2: log in, create or select a character, enter the world at Holtburg, see objects spawn, and walk around with the server accepting movement.

"Need": **must** = required for M2; **should** = handle for robustness or test hooks; **skip** = parse-and-ignore is enough for M2.

### 5.1 Table

| Opcode | Name | Direction | Need | ACE builds or parses it in (`ACE:ACE.Server/Network/...`) |
|---|---|---|---|---|
| (packet) | LoginRequest / ConnectResponse / Ack / NAK / Disconnect | C→S | must | `Packets/PacketInbound*.cs`, `NetworkSession.cs`, `Managers/NetworkManager.cs` |
| (packet) | ConnectRequest / Ack / TimeSync / NAK / RejectRetransmit | S→C | must | `Packets/PacketoutboundConnectRequest.cs`, `NetworkSession.WriteOptionalHeaders`, `Packets/PacketRejectRetransmit.cs` |
| F658 | CharacterList | S→C | must | `GameMessages/Messages/GameMessageCharacterList.cs` |
| F7E1 | ServerName | S→C | should | `GameMessageServerName.cs` |
| F7E5 | DDD_Interrogation | S→C | must | `GameMessageDDDInterrogation.cs` |
| F7E6 | DDD_InterrogationResponse | C→S | must | `Handlers/DDDHandler.cs` + `Structure/CAllIterationList.cs`, `CMostlyConsecutiveIntSet.cs`, `PTaggedIterationList.cs` |
| F7EA | DDD_EndDDD | S→C (C→S only after BeginDDD) | must | `GameMessageDDDEndDDD.cs`, `DDDHandler.DDD_EndDDD` |
| F7E7 | DDD_BeginDDD | S→C | should (fail fast) | `GameMessageDDDBeginDDD.cs` |
| F656 | CharacterCreate | C→S | must | `Handlers/CharacterHandler.cs`; `ACE:ACE.Entity/CharacterCreateInfo.cs`, `Appearance.cs`; `ACE:ACE.Server/Factories/PlayerFactory.cs` |
| F643 | CharacterCreateResponse | S→C | must | `GameMessageCharacterCreateResponse.cs` |
| F659 | CharacterError | S→C | must | `GameMessageCharacterError.cs`, `Enum/CharacterError.cs` |
| F7DC | AccountBoot | S→C | must (error path) | `GameMessageBootAccount.cs` |
| F7C8 | CharacterEnterWorldRequest | C→S | must | `CharacterHandler.CharacterEnterWorldRequest` |
| F7DF | EnterWorld ServerReady | S→C | must | `GameMessageCharacterEnterWorldServerReady.cs` |
| F657 | CharacterEnterWorld | C→S | must | `CharacterHandler.CharacterEnterWorld` |
| F746 | PlayerCreate | S→C | must | `GameMessagePlayerCreate.cs` |
| F745 | ObjectCreate | S→C | must | `GameMessageCreateObject.cs` → `ACE:ACE.Server/WorldObjects/WorldObject_Networking.cs` |
| F7DB | UpdateObject (same body as F745) | S→C | should | `GameMessageUpdateObject.cs` |
| F747 | ObjectDelete | S→C | must | `GameMessageDeleteObject.cs` |
| F748 | UpdatePosition | S→C | must | `GameMessageUpdatePosition.cs`, `Structure/PositionPack.cs` |
| F74C | UpdateMotion | S→C | must (remote animation; can stub in M2) | `GameMessageUpdateMotion.cs`, `Motion/MovementData.cs`, `Motion/InterpretedMotionState.cs`, `Motion/MotionItem.cs` |
| F74E | VectorUpdate | S→C | should | `GameMessageVectorUpdate.cs` |
| F74B | SetState | S→C | must | `GameMessageSetState.cs` |
| F751 | PlayerTeleport | S→C | must (needed for `@telepoi`) | `GameMessagePlayerTeleport.cs` |
| F749 / F74A | ParentEvent / PickupEvent | S→C | should | `GameMessageParentEvent.cs`, `GameMessagePickupEvent.cs` |
| F625 | ObjDescEvent | S→C | should | `GameMessageObjDescEvent.cs` |
| F7B0 | GameEvent envelope | S→C | must (envelope); contents skip | `GameEvent/GameEventMessage.cs`, `GameEvent/Events/*` (0x0013 PlayerDescription, 0x0029, 0x0021, 0x0196, 0x0314, 0x028A, 0x0295, 0x0004, 0x0147, 0x01EA) |
| F7E0 | ServerMessage (system chat) | S→C | should (`@myloc` test hook) | `GameMessageSystemChat.cs` |
| 02BB / 01E0 | HearSpeech / EmoteText | S→C | skip | `GameMessageHearSpeech.cs`, `GameMessageEmoteText.cs` |
| 02CD–02E9 | Private/Public property, skill and vital updates | S→C | skip | `GameMessagePrivateUpdate*.cs`, `GameMessagePublicUpdate*.cs` |
| F750 / F754 / F755 / EA60 | Sound / PlayScript / PlayEffect / AdminEnvirons | S→C | skip | `GameMessageSound.cs`, `GameMessageScript.cs`, `GameMessageAdminEnvirons.cs` |
| F7B1 / 0x00A1 | LoginComplete | C→S | must | `GameAction/Actions/GameActionLoginComplete.cs` |
| F7B1 / 0xF61C | MoveToState | C→S | must | `GameActionMoveToState.cs`, `Motion/MoveToState.cs`, `Motion/RawMotionState.cs`, `Motion/MotionItem.cs` |
| F7B1 / 0xF753 | AutonomousPosition | C→S | must | `GameActionAutonomousPosition.cs` |
| F7B1 / 0xF61B | Jump | C→S | should | `GameActionJump.cs`, `Structure/JumpPack.cs` |
| F7B1 / 0x0015 | Talk (String16L; `@` = admin command) | C→S | should (test hooks) | `GameActionTalk.cs` |
| F653 | CharacterLogOff | C→S and S→C | must | `CharacterHandler.CharacterLogOff`, `GameMessageCharacterLogOff.cs` |
| F7B1 / 0x01E9 → GameEvent 0x01EA | PingRequest / PingResponse | C→S / S→C | skip | `GameActionPingRequest.cs`, `GameEventPingResponse.cs` |

### 5.2 Login-phase layouts

These are field tables paraphrased from the ACE serializers above. Opcodes are omitted.

| Message | Fields in order |
|---|---|
| F658 CharacterList | u32 0; u32 n; n × {u32 GUID, String16L name (`+` prefix for admin characters), u32 seconds-until-deletion (0 = active)}; u32 0 (deleted list); u32 slot count (11); String16L account; u32 use-TurbineChat; u32 1 (has Throne of Destiny) |
| F7E1 ServerName | i32 online; i32 max; String16L world name |
| F7E5 DDD_Interrogation | u32 region 1; u32 name-rule language 1; u32 product id (1, or 5 when highres is allowed); u32 language count 2; u32 0; u32 1 |
| F7E6 DDD_InterrogationResponse | u32 client language (1); i32 list count; per list {i32 DAT type (0, or ASCII "HiFi" for highres), i32 DAT id (1 portal or highres, 2 cell, 3 language), i32 iteration count, then run-length i32s until the count is covered: a positive value is one iteration, a negative −k covers k}; then u32 0, u32 0 (ACE does not read these). For iteration 2072, OpenAC encodes `2072, −2072, 1` |
| F656 CharacterCreate | String16L account; u32 1; u32 heritage; u32 gender; 14 × u32 appearance (eyes, nose, mouth, hair colour, eye colour, hair style, headgear style, headgear colour, shirt style, shirt colour, trousers style, trousers colour, footwear style, footwear colour); 6 × f64 shades (skin, hair, headgear, shirt, trousers, footwear); i32 template; 6 × u32 attributes (Str, End, Coord, Quick, Focus, Self); u32 slot; u32 class id; u32 skill count, **which must be 55 or ACE terminates the session** with "client is not the correct version" (`PlayerFactory.cs` L166); 55 × u32 SkillAdvancementClass; String16L name; u32 start area; u32 is-admin; u32 is-sentinel. ACE ignores trailing bytes (OpenAC appends a checksum u32) |
| F643 CharacterCreateResponse | u32 result (1 Ok, 2 Pending, 3 NameInUse, 4 NameBanned, 5 Corrupt, 6 DatabaseDown, 7 AdminPrivilegeDenied); if Ok: u32 GUID, String16L name, u32 0 |
| F659 CharacterError | u32 code (`Enum/CharacterError.cs`) |
| F7DC AccountBoot | String16L reason |
| F657 CharacterEnterWorld | u32 GUID; String16L account |
| F746 PlayerCreate | u32 GUID |

Name checks: the taboo table and the creature-name check are on by default (`taboo_table`, `creature_name_check`).

### 5.3 World-phase, server-to-client layouts

**F745 ObjectCreate and F7DB UpdateObject**: u32 GUID; ModelData; PhysicsDesc; WeenieDesc (`WorldObject_Networking.SerializeCreateObject`).

*ModelData:*

1. u8 0x11; u8 palette count p; u8 texture count t; u8 anim-part count a.
2. If p > 0: base palette, as a known-type (0x04000000) PackedDword.
3. p × {known-type 0x04000000 PackedDword subpalette, u8 offset, u8 length}.
4. t × {u8 part index, known-type 0x05000000 PackedDword old texture, the same for the new texture}.
5. a × {u8 part index, known-type 0x01000000 PackedDword GfxObj}.
6. Align.

*PhysicsDesc:* u32 flags, u32 PhysicsState, then in this exact order:

| Flag | Bit | Payload |
|---|---|---|
| Movement | 0x10000 | u32 byte length L. If L > 0: header-less MovementData (below), then u32 is-autonomous |
| AnimationFrame | 0x20000 | u32 placement. Only when the Movement flag is absent |
| Position | 0x8000 | u32 cell, 3 × f32 origin, 4 × f32 quaternion (W, X, Y, Z) |
| MTable | 0x2 | u32 |
| STable | 0x800 | u32 |
| PeTable | 0x1000 | u32 |
| CSetup | 0x1 | u32 |
| Parent | 0x20 | u32 parent GUID, u32 parent location |
| Children | 0x40 | u32 n, n × {u32 GUID, u32 location} |
| ObjScale | 0x80 | f32 |
| Friction | 0x100 | f32 |
| Elasticity | 0x200 | f32 |
| Translucency | 0x40000 | f32 |
| Velocity | 0x4 | 3 × f32 |
| Acceleration | 0x8 | 3 × f32 |
| Omega | 0x10 | 3 × f32 |
| DefaultScript | 0x2000 | u32 |
| DefaultScriptIntensity | 0x4000 | f32 |

After the flagged fields come **nine u16 sequences**, in order: position, movement, state, vector, teleport, server control, force position, visual description, instance. Then Align. (Flag 0x400 Timestamps is never written by ACE.)

PhysicsState bits that matter for M2:

| Bit | Meaning |
|---|---|
| 0x1 | Static |
| 0x4 | Ethereal |
| 0x10 | IgnoreCollisions |
| 0x20 | NoDraw |
| 0x400 | Gravity |
| 0x4000 | Hidden |
| 0x400000 | EdgeSlide |

A newly logged-in player is `IgnoreCollisions | Gravity | Hidden | EdgeSlide` until LoginComplete.

*WeenieDesc:*

1. u32 header flags; String16L name; PackedDword weenie class id; known-type 0x06000000 PackedDword icon; u32 ItemType; u32 ObjectDescriptionFlags; Align.
2. If ObjectDescriptionFlags has 0x04000000: u32 header2.
3. Optional fields, **in ACE's write order** (this is not bit order). Each field has the type shown:

   | Flag (header or header2) | Type |
   |---|---|
   | PluralName 0x1 | String16L |
   | ItemsCapacity 0x2 | u8 |
   | ContainersCapacity 0x4 | u8 |
   | AmmoType 0x100 | u16 |
   | Value 0x8 | u32 |
   | Usable 0x10 | u32 |
   | UseRadius 0x20 | f32 |
   | TargetType 0x80000 | u32 |
   | UiEffects 0x80 | u32 |
   | CombatUse 0x200 | i8 |
   | Structure 0x400 | u16 |
   | MaxStructure 0x800 | u16 |
   | StackSize 0x1000 | u16 |
   | MaxStackSize 0x2000 | u16 |
   | Container 0x4000 | u32 |
   | Wielder 0x8000 | u32 |
   | ValidLocations 0x10000 | u32 |
   | CurrentlyWieldedLocation 0x20000 | u32 |
   | Priority 0x40000 | u32 |
   | RadarBlipColor 0x100000 | u8 |
   | RadarBehavior 0x800000 | u8 |
   | PScript 0x8000000 | u16 |
   | Workmanship 0x1000000 | f32 |
   | Burden 0x200000 | u16 |
   | Spell 0x400000 | u16 |
   | HouseOwner 0x2000000 | u32 |
   | HouseRestrictions 0x4000000 | RestrictionDB |
   | HookItemTypes 0x20000000 | u32 |
   | Monarch 0x40 | u32 |
   | HookType 0x10000000 | u16 |
   | IconOverlay 0x40000000 | known-type PackedDword |
   | IconUnderlay (header2 0x1) | known-type PackedDword |
   | MaterialType 0x80000000 | u32 |
   | Cooldown (header2 0x2) | i32 |
   | CooldownDuration (header2 0x4) | f64 |
   | PetOwner (header2 0x8) | u32 |

4. Align.

The widths above come from the C# types of the `WorldObject` properties that the writer passes to `BinaryWriter.Write` (`WorldObject_Networking.cs` L75–205). They were checked one by one: for example `ItemCapacity` is `byte?`, `UseRadius` is `float?`, `Script` is `ushort?`, `CooldownId` is `int?` and `CooldownDuration` is `double?`.

**MovementData** (as used inside PhysicsDesc, without its header):

1. u8 movement type; u8 motion flags (0x1 StickToObject, 0x2 StandingLongJump); u16 current stance.
2. For movement type 0 (Invalid, the common "animation state" case): **InterpretedMotionState**:
   - u32 = flags | (command count << 7). Flags: 0x1 style, 0x2 forward, 0x8 sidestep, 0x20 turn, 0x4 forward speed, 0x10 sidestep speed, 0x40 turn speed.
   - Then u16 style, u16 forward, u16 sidestep, u16 turn (each present only if its flag is set, in that order).
   - Then f32 forward speed, f32 sidestep speed, f32 turn speed (each if flagged).
   - Then the commands, each {u16 command, u16 (sequence & 0x7FFF | autonomous << 15), f32 speed}.
   - **Align.** If StickToObject is set, a u32 sticky GUID follows **after** the alignment.
3. Movement types 6 and 7 (MoveToObject, MoveToPosition) and 8 and 9 (TurnToObject, TurnToHeading) have their own bodies (`Motion/MoveTo*.cs`, `TurnTo*.cs`).

Motion commands in the interpreted state are the **low 16 bits only**. Map them back to the full u32 through the 11.6096 command table (§7).

**F74C UpdateMotion:** u32 GUID; u16 instance sequence; then the MovementData *with* its header: u16 movement sequence, u16 server-control sequence, u8 is-autonomous, Align; then the body above.

**F748 UpdatePosition:**

1. u32 GUID; u32 PositionFlags:

   | Bit | Meaning |
   |---|---|
   | 0x1 | Has velocity |
   | 0x2 | Has placement |
   | 0x4 | Grounded |
   | 0x8 | W absent |
   | 0x10 | X absent |
   | 0x20 | Y absent |
   | 0x40 | Z absent |

2. u32 cell; 3 × f32 origin.
3. Each quaternion component W, X, Y, Z *unless* its "absent" bit is set. Absent components are 0.
4. If 0x1: 3 × f32 velocity. If 0x2: u32 placement.
5. u16 instance, u16 position, u16 teleport, u16 force-position sequences.

No trailing Align. A 4-byte multiple happens by construction (`PositionPack.cs`).

**Smaller S2C messages:**

| Message | Fields |
|---|---|
| F747 ObjectDelete | u32 GUID; u16 instance sequence; Align |
| F74B SetState | u32 GUID; u32 PhysicsState; u16 instance sequence; u16 state sequence |
| F74E VectorUpdate | u32 GUID; 3 × f32 velocity; 3 × f32 omega; u16 instance; u16 vector sequence |
| F751 PlayerTeleport | u16 teleport sequence; Align |
| F749 ParentEvent | u32 parent; u32 child; i32 parent location; i32 placement; u16 parent instance; u16 child position sequence |
| F74A PickupEvent | u32 GUID; u16 instance; u16 position sequence |
| F625 ObjDescEvent | u32 GUID; ModelData; u16 instance; u16 visual-description sequence |
| F7E0 ServerMessage | String16L text; i32 chat type |

**Sequences, for the client:**

- Every object update carries u16 per-object sequences.
- Drop an update when its sequence is not newer than the last applied one of the same kind. Use a wrap-safe compare.
- An instance-sequence change means the object was re-created.
- For the player's own object, ignore server UpdatePosition except when the **teleport** or **force-position** sequence has advanced; then snap to it. ACE echoes an UpdatePosition back to the player on every accepted position (`Player_Tick.UpdatePlayerPosition`).

### 5.4 World-phase, client-to-server layouts

These are retail layouts, checked against the 11.4186 decompilation; ACE reads them.

**F61C MoveToState** (sent on every change in input state): RawMotionState, then Position (u32 cell, 3 × f32 origin, 4 × f32 W, X, Y, Z), then u16 instance, u16 server-control, u16 teleport, u16 force-position (each echoing the player's latest values), then u8 (bit0 = contact/on-ground, bit1 = standing long-jump), then Align. Sources: `CMInterpPrim.c RawMotionState::Pack`, `CPackClasses.c MoveToStatePack::Pack`, `ACE Motion/MoveToState.cs`.

**RawMotionState:**

1. u32 = flags (11 bits) | (action count << 11).
2. Then, each present only when it differs from its default:

   | Bit | Field | Type | Default |
   |---|---|---|---|
   | 0x1 | Current hold key | u32 | None = 1 |
   | 0x2 | Current style | **u32** | 0x8000003D NonCombat |
   | 0x4 | Forward command | **u32** (full command, for example 0x45000005 WalkForward) | 0x41000003 Ready |
   | 0x8 | Forward hold key | u32 | 0 |
   | 0x10 | Forward speed | f32 | 1.0 |
   | 0x20 | Sidestep command | u32 | 0 |
   | 0x40 | Sidestep hold key | u32 | 0 |
   | 0x80 | Sidestep speed | f32 | 1.0 |
   | 0x100 | Turn command | u32 | 0 |
   | 0x200 | Turn hold key | u32 | 0 |
   | 0x400 | Turn speed | f32 | 1.0 |

3. Each action is **{u16 command low word, u16 (stamp & 0x7FFF | autonomous << 15), f32 speed}**, 8 bytes.

Running is forward = WalkForward plus current hold key = Run (2). ACE turns that into RunForward at the player's run rate (`Motion/MovementData.cs` L104–115). ACE accepts actions only for soul emotes at speed 1.0.

**F753 AutonomousPosition:** Position (32 bytes); u16 instance, server-control, teleport and force-position; u8 contact (1 = on ground); Align. Retail sends it periodically while moving; ACE's comment says about once a second. Send at least on every cell change, and keep each update within one landblock of the previous one (§6). Sources: `CPackClasses.c AutonomousPositionPack::Pack`, `GameActionAutonomousPosition.cs`.

**F61B Jump:** f32 extent (0–1); 3 × f32 velocity; **Position (32 bytes)**; u16 instance, server-control, teleport and force-position; Align. That is **56 bytes** after the action type (retail `JumpPack::Pack`, `CPackClasses.c` L599).

- ACE's parser only uses extent and velocity. It then reads `JumpPack` as 24 bytes plus two more u32s, so it needs at least 32 bytes.
- A 24-byte jump (Chorizite and `protocol.xml`) makes ACE's handler throw `EndOfStream`, and the jump is dropped.

**0x00A1 LoginComplete:** no payload. **0x0015 Talk:** String16L text.

---

## 6. What ACE validates for client movement

The client is authoritative for its own movement, and ACE checks very little. Source: `ACE:ACE.Server/WorldObjects/Player_Tick.cs` L411–564 and `ACE:ACE.Server/Physics/PhysicsObj.cs` L1766–1802 and L4252–4289.

- **While `Teleporting`** (after login or a portal, until LoginComplete) every requested position is ignored.
- **ValidateMovement** rejects these cases:
  - the player has no current landblock;
  - a move from one landblock to *another* where both cells are indoor (index ≥ 0x100), except two hard-coded bugged cells;
  - a dungeon-to-dungeon landblock change.
- **Speed:** rejected if the move distance is over **50 m** *and* it crosses more than one landblock in a single update (`MaxSpeed = 50`). Send AutonomousPosition often.
- **Z check:** if the player is more than 10 m above the last grounded Z, in the same landblock, not jumping for over 1 s and with Jump skill below 1000, a jump-hack check may snap them back to the last ground position. That snap is a force-position sequence increment plus an UpdatePosition.
- **Physics:** for non-PK players (`FastTick` is true only for PK types), ACE runs a server transition toward the requested position. It then **forces the requested position anyway** (`update_object_server(forcePos: true)`). Collision mismatches are therefore *not* rejected; only a transition across more than one landblock fails.
- **Client sequences** (instance, server-control, teleport, force-position) are read but **never checked**.
- **Acceptance signal:**
  - ACE sends F748 UpdatePosition for the player back to the player, with the accepted coordinates and an incremented position sequence.
  - It also broadcasts motion (F74C) to observers.
  - `@myloc` confirms the position over F7E0.

---

## 7. Command IDs and the 11.6096 "+3 shift"

> **Erratum (M1 effects track):** Chorizite.DatReaderWriter 2.1.9's `Enums.MotionCommand` already uses the 11.6096
> ids. Tests confirm `MagicPowerUp01Purple = 0x1000012B` and `AFKState = 0x4300011B`. The −3 offset described below
> affects only Chorizite.ACProtocol's enum, which is generated from `protocol.xml`.

- Mag-nus doc 13 reports that 11.6096 inserted SkillHealOther, CombatEat and CombatDrink at ordinal 0x10F. Every command at or above that ordinal moved by +3, in both the UI namespace (0x09xxxxxx) and the motion and action namespaces (0x10…, 0x13…, 0x40–0x45…, 0x80… and so on).
- **Verified locally.** `tools/protocol/dump_command_table.py` reads `string2command` from the user's `acclient.exe` (412 entries; MD5 is checked first). Results:

  | Command | ID in 11.6096 |
  |---|---|
  | AFKState | 0x4300011B |
  | MeditateState | 0x4300011C |
  | LogOut | 0x1000011E |
  | SnowAngelState | 0x43000118 |
  | Fishing | 0x10000165 |
  | AttackMed6 | 0x1000018D |
  | AI_TelegraphCast | 0x1000019B |
  | NextMonster | 0x09000112 |

**Which tables match the exe:**

| Table | Result against the exe |
|---|---|
| **ACE** `ACE:ACE.Entity/Enum/MotionCommand.cs` | 409 enum entries; 404 have a name in the exe table, and 398 of those match exactly (full u32). The 6 mismatches are five UI-only commands, PreviousMonster…ClosestPlayer (ACE 0x09000110–0x09000114, still unshifted), plus `Invalid` (0 vs 0x80000000). None of these is sent as motion. **ACE uses end-of-retail motion IDs.** |
| **Chorizite** `CHZ:Enums/Command.generated.cs` (from `protocol.xml`) | 408 entries; 404 named in the exe; **136 wrong, all by exactly −3** (for example AFKState 0x118 and LogOut 0x11B), 268 right. This is the pre-2015 table |
| **OpenAC** | Maps wire u16 to full IDs through `AcDream.Core/Physics/AceModernCommandCatalog.cs`, which is built from `DatReaderWriter.Enums.MotionCommand`. A pre-shift `Retail2013CommandCatalog` is kept separately. Not verified here: the DatReaderWriter package is not in the local NuGet cache |

**Wire impact:**

- Client to server: RawMotionState carries **full u32** commands, so the upper byte disambiguates.
- Server to client: InterpretedMotionState and motion items carry the **u16 low word**. The client must rebuild the u32 with the 11.6096 table.
  - With a 2013 table, every server motion ≥ 0x10F is misnamed. AFKState (0x011B) would decode as LogOut.
  - In the 11.6096 table, each command's low word equals its ordinal, and all 412 low words are unique (checked), so a u16 maps to exactly one command.
  - Collisions appear only in *mixed* tables such as ACE's enum, whose stale UI entries 0x09000110–0x09000114 share low words with motion commands. If such a table is ever used, prefer the motion classes, as OpenAC's class-priority rule does.
- Lifestone should check the 11.6096 table into source as data: 412 numeric IDs, taken from ACE's enum values plus the 2015 additions CombatEat, CombatDrink, AI_TelegraphCast and StretchUI, with the UI entries shifted. Verify it against the exe dump in a test (§9.4). Never derive it from `protocol.xml`.

---

## 8. Candidate evaluation

### 8.1 Summary

| | OpenAC `AcDream.Core.Net` | Chorizite.ACProtocol | Write our own | holtburger |
|---|---|---|---|---|
| Transport (checksum, ISAAC, sequence, ack, NAK, retransmit, handshake) | **Complete**; models ACE quirks (start-at-2, cleartext-reject key shift, ConnectResponse race, 256-key window) | **None** (it parses pcaps: header and optional fields only; no checksum or ISAAC) | All from scratch, with ACE as spec | Complete (Rust, tokio) |
| M2 message set (§5.1) | **All "must" and "should" present**, plus about 70 more. Missing by name: F746 PlayerCreate, F7DC AccountBoot (trivial) | Bodies for nearly all `protocol.xml` messages, read and write | – | Most |
| Correctness against ACE (spot checks, §8.2–8.3) | Hash32 and ISAAC identical; CharacterList, UpdatePosition and MoveToState (no actions) byte-identical; **4 defects** | CharacterList read and UpdatePosition read OK. **MoveToState and RawMotionState wrong**, **Jump wrong**, enum pre-shift, writer padding and string bugs | n/a | Mirrors ACE's handlers, including ACE's short Jump read |
| Targets end-of-retail IDs | Yes (ACE-modern catalog) | **No** (2013) | Yes, if built from the exe | ACE-based |
| API shape | Allocation-light receive (`ArrayPool`, borrowed spans); a background `ReceiveFromAsync` feeding a `Channel`; `Tick()` drains it on the caller's thread with a 4 ms budget and raises C# events. **But** `WorldSession` is a 3,028-line god class with blocking `Connect` helpers and dependencies on `AcDream.Core` | Class per message with `BinaryReader` and `BinaryWriter` (allocation-heavy); netstandard2.0 | Our choice | Async Rust |
| Licence | MIT | MIT, but generated from unlicensed `protocol.xml` (which says it is "based off … ACE enums / code comments") | Ours (porting ACE code would make it AGPL) | AGPL |
| Maintenance | Very active (created 2026-09-08, 1,338 commits, v0.1.24). Heavily AI-assisted and fast-moving | Stale (last commit 2025-10-04, 0 stars) | Ours | Active |
| Build and supply chain | Clean (§8.2) | GitVersion.MsBuild runs at build time; two minor packages (§8.3) | – | n/a |

### 8.2 OpenAC: details

**Correct against ACE (verified):**

- `Hash32` and `IsaacRandom` match line for line, and ACE's algorithm reproduces OpenAC's golden vector (§10).
- `PacketCodec` checksums: header hash with 0xBADD70DD, and per-fragment header and data hashes.
- `LoginRequest.Build` (String32L password).
- ConnectResponse: seq 1, Id 0, sent to port + 1, after a 200 ms delay with retries every 0.33 s.
- Outbound sequences start at 2.
- Ack-only packets use HighestIdSent.
- Cleartext NAK.
- Retransmit keeps the sealed key.
- Inbound key parking, including ACE's cleartext RejectRetransmit quirk.
- `FragmentAssembler` handles tail-first arrival and validates Index < Count.
- `CharacterList.Parse` is byte-for-byte consistent with ACE's writer (§10 vector).
- `UpdatePosition.TryParse` is consistent with `PositionPack`.
- `CreateObject` PhysicsDesc order matches ACE's write order exactly; known-type PackedDwords are restored.
- `MoveToState.Build` with no actions is **byte-identical** to the retail/ACE layout (§10).
- `AutonomousPosition.Build` matches.
- `JumpAction.Build` matches **retail** (56 bytes), which ACE tolerates.
- `CharacterCreate.BuildRequestBody` matches ACE's reader and enforces the 55-skill rule.
- `DddInterrogationResponse` encodes run lengths the way ACE decodes them.

**Defects to fix when vendoring:**

1. **RawMotionState actions are missing their f32 speed.** `OAC:Messages/RawMotionStatePacker.cs` writes 4 bytes per action; retail and ACE use 8. With one action, ACE reads the position cell as 0x42A80000 and the coordinates shift (§10). The field is latent until emotes or soul-emote actions are sent.
2. **The UpdateMotion sticky GUID is read without alignment.** `OAC:Messages/UpdateMotion.cs` reads it straight after the commands. ACE writes `Align()` first (`InterpretedMotionState.Write`, then `MovementInvalid.Write`), so whenever the u16 field count is odd the GUID is read 2 bytes early. This matters for StickToObject (melee approach). It was found by reading the code; confirm it with a golden fixture.
3. **Strings are written as ASCII.** `OAC:Packets/PacketWriter.WriteString16L` encodes ASCII, so non-ASCII becomes `?`, while reads use CP1252. Switch to CP1252 (`Messages/Encodings.cs` already registers it).
4. **The optional-header parser skips EchoResponse** (0x04000000, 8 bytes) and NetError. `PacketCodec.TryParseBorrowedOptional` does not consume them. The bug is latent because OpenAC never sends EchoRequest. Add both, or never send echoes.

**Security and hygiene:**

- `NetClient` binds to `0.0.0.0:0`. Change it to `127.0.0.1:0` for a local server.
- Core.Net reads 8 `ACDREAM_*` environment variables, including one per UpdateMotion in the hot path, and writes 29 lines to the console. Strip both.
- There is no `Process`, HTTP, registry, file-write or interop use in Core.Net (grep was clean).

**Build files (reviewed, not built):**

- `Directory.Build.props`: net10.0, `TreatWarningsAsErrors`, lock files, and one MSBuild target that only removes XML docs from publish output. No `Exec`.
- `Directory.Packages.props`: central versions; Core.Net's own closure is the AcDream.Core packages BCnEncoder.Net 2.2.1, Chorizite.DatReaderWriter 2.1.7, Serilog 4.0.2, StbImage*, plus AcDream.Plugin.Abstractions.
- `NuGet.Config`: `<clear/>` plus nuget.org only.
- `tests/Directory.Build.props`: compile includes only.
- `global.json`: SDK **10.0.300 with rollForward `latestPatch`**. This would **not** select our installed 10.0.401, which is a different feature band, so building OpenAC as-is would fail. It does not matter if we copy files.

**Provenance:**

- `NOTICE.md` says it is "verified against, but does not include code from" ACE.
- However, `tests/AcDream.Core.Net.Tests/Transport/AceCryptoModel.cs` is a near line-for-line C# port of ACE's AGPL `CryptoSystem` (same members and loop).
- Do **not** vendor OpenAC's `Ace*Model` test files into a permissive tree. Write our own fake server from this spec.

### 8.3 Chorizite.ACProtocol: details

- **No transport.** `CHZ:Packets/ACPacketHeader.cs` only parses headers. Its optional-section order skips **RejectRetransmit (0x2000)** and handles Referral and LogonServerAddr. There is no checksum, ISAAC, ack or retransmit, and no fragment *writer*.
- **RawMotionState** (`CHZ:Types/RawMotionState.generated.cs`):
  - It reads and writes style, forward, sidestep and turn **as u16**; retail and ACE use u32.
  - `CommandListLength = (Flags >> 11) & 0xF8` drops the low 3 bits of the count.
  - `Write` emits no commands.
  - Result (§10): ACE reads a Chorizite-built MoveToState as forward command 0x00020005 with the position cell 0x0000A9B4, which is garbage.
- **Jump** (`CHZ:Types/JumpPack.generated.cs`) is 24 bytes with no Position. ACE's handler throws and drops it.
- **Enums** are pre-shift: 136 of the 404 command names that exist in the exe are off by −3 (§7).
- **Writer bugs:**
  - Alignment is done by `BaseStream.Position +=` and `Seek`, which writes **no** pad bytes at the end of a message.
  - `WriteString16L` writes .NET `char`s (UTF-8 by default) and pads by seeking.
  - `Write(bool)` writes 1 byte where the protocol's bools are u32 (for example CharacterList's `UseTurbineChat`).
  - Known-type PackedDwords are returned without their type prefix.
- **Reads that are correct:** CharacterList (u32 bools, String16L padding) and UpdatePosition (PositionPack flag logic).
- **Build:**
  - netstandard2.0.
  - `GitVersion.MsBuild` 6.1.0 runs a versioning task during every build.
  - Other packages: `Chorizite.Common` 1.0.0, `Medo.PcapRW` 1.2.0, `System.CodeDom`, `Microsoft.Extensions.Logging.Abstractions`, `System.Memory`, `System.Numerics.Vectors`.
  - The T4 `.tt` generators run only at Visual Studio design time.
  - No `NuGet.config` and no `Directory.Build.*`.
  - The CI workflows push to GitHub Packages and nuget.org; they are irrelevant locally.
  - Grep found no risky APIs.
- **Verdict:** keep it as a name and catalogue reference only. Do not depend on it.

### 8.4 Writing our own

- This is feasible with this document plus ACE as the spec. The retail-only parts (pack layouts, the command table) are already covered by the decompilation and the exe.
- The cost is mainly in the transport edge cases that OpenAC already solved and tested (key parking, cleartext-reject shift, NAK gating, handshake race). Re-deriving them is roughly 1–2 weeks of risk for no gain.
- **Choose a hybrid:** vendor OpenAC's transport and simple codecs (MIT), and write the session state machine and the motion-command table ourselves.

### 8.5 holtburger (reference only, AGPL)

- Its protocol crate is pack and unpack only. Its session crate is async (tokio).
- It independently uses ConnectResponse seq 1 to port + 1 with a race delay, and a first data sequence of 2.
- Its `FIXTURES.md` produces golden fixtures by dumping ACE serializer output from an ACE test project. Our §9.4 follows that idea without modifying ACE.
- Its Jump struct mirrors ACE's 32-byte read rather than retail's 56 bytes.

---

## 9. Recommendation

### 9.1 What to vendor (MIT; keep OpenAC's LICENSE text in `src/Lifestone.Net/THIRD-PARTY-NOTICES.md` and a header in each copied file)

Copy with `AcDream.Core.Net` replaced by `Lifestone.Net`. Line counts are from the pinned commit.

| Area | OpenAC source → Lifestone.Net | Changes |
|---|---|---|
| Crypto | `Cryptography/Hash32.cs` (29), `Cryptography/IsaacRandom.cs` (113) | none |
| Packets | `Packets/{PacketHeader, PacketHeaderFlags, PacketHeaderOptional, PacketCodec, BorrowedPacket, Packet, MessageFragment, MessageFragmentHeader, FragmentAssembler, PacketWriter, LoginRequest, TransportDisconnect}.cs` (about 1.4k) | Defects 3 and 4 |
| Transport | `Transport/{ReliableTransport, OutboundFlowQueue, InboundSequenceTracker, AckNakScheduler, SentPacketStore, SequenceMath, TransportClock, TransportStats, RetailPacketLossAverager}.cs` (about 1.1k); `LossyTransportDecorator.cs` for tests only | Strip diagnostics |
| Socket | `NetClient.cs` (150) | Bind loopback; make the bind address an option |
| Handshake | Logic from `WorldSession.Connection.cs` (154), re-homed in our own `AcSession` | Rewrite |
| Login codecs | `Messages/{CharacterList, ServerName, CharacterError, CharGenVerificationResponse, CharacterCreate, CharacterEnterWorld, CharacterLogOff, DddInterrogationResponse, DddBegin, DddDataVersions, GameActionLoginComplete, ServerMessage, ChatRequests (Talk only), Encodings, StringReader, GameMessageFragment (GameMessageGroup), GameEventEnvelope, GameEventType}.cs` | Add F746 and F7DC |
| World codecs | `Messages/{CreateObject, PublicWeenieDescBody, PhysicsSpawnData, DeleteObject, UpdatePosition, UpdateMotion, VectorUpdate, SetState, ParentEvent, PickupEvent, ObjDescEvent, PrivateUpdatePosition}.cs` | Replace `AcDream.Core.Items` and `Physics` types with small Lifestone records; defect 2 |
| Movement | `Messages/{MoveToState, RawMotionStatePacker, AutonomousPosition, JumpAction}.cs` + `AcDream.Core/Physics/RawMotionState.cs` (181) | Defect 1 |
| Later | `Messages/PlayerDescriptionParser.cs` (684), `GameEvents.cs`, `PrivateUpdate*.cs` | Decouple from Core |

Do **not** vendor:

- `WorldSession.cs` (a god class, coupled to OpenAC's UI and combat model);
- `GameEventWiring.cs`, `ObjectTableWiring.cs`, `CombatStateWiring.cs`;
- the `tests/.../Ace*Model.cs` files (ACE-derived).

Our own code:

- **`MotionCommandTable`**: u16 to u32 for 11.6096, checked in as numeric data (§7). Build it from ACE's MotionCommand enum *values* (facts, not code) plus the four 2015 additions, and test it against the exe dump.
- **`AcSession`**: the state machine.
- the Godot adapter, which lives in the Godot project, not in `Lifestone.Net`.

### 9.2 API shape for `src/Lifestone.Net` (net10.0 class library, no Godot reference)

```csharp
public sealed class AcSession : IDisposable          // one per server connection
{
    public AcSession(AcSessionOptions o);              // Server = 127.0.0.1:9000, LocalBind = 127.0.0.1:0,
                                                       // ClientVersion "1802", DatIterations {2072, 982, 994}
    public SessionState State { get; }                 // Handshaking, CharacterSelect, EnteringWorld, InWorld, Failed, Closed
    public void BeginLogin(string account, ReadOnlySpan<char> password); // non-blocking
    public int Pump(TimeSpan budget);                  // call from Godot _Process (or a sim thread):
                                                       // drains the receive channel, parses, raises events,
                                                       // runs ack, NAK and resend sweeps
    // outbound
    public void CreateCharacter(in CharGenRequest r);
    public void EnterWorld(uint characterGuid);
    public void SendLoginComplete();
    public void SendMoveToState(in RawMotionState s, in AcPosition p, in ObjSeqs seqs, bool contact, bool longJump);
    public void SendAutonomousPosition(in AcPosition p, in ObjSeqs seqs, bool contact);
    public void SendJump(float extent, Vector3 velocity, in AcPosition p, in ObjSeqs seqs);
    public void SendTalk(string text);
    public void LogOff(); public void Disconnect();
    // inbound: typed readonly record structs, raised on the Pump thread
    public event Action<CharacterListMsg>? CharacterList;  /* CharGenResult, CharacterError, Booted,
       ObjectCreated, ObjectUpdated, ObjectDeleted, PositionUpdated, MotionUpdated, VectorUpdated,
       StateUpdated, Teleport, ParentChanged, ObjDescChanged, ServerTime, SystemChat, GameEvent(raw) */
    public event Action<RawMessage>? MessageTap;       // opcode + bytes, for built-in automation and debugging (no plugin host)
}
```

- Receiving runs on one background `ReceiveFromAsync` loop that copies into pooled buffers and writes to a `Channel` (OpenAC's model).
- Parsing and every event happen on the `Pump` caller, so no locks are needed in game code.
- Codecs work on `ReadOnlySpan<byte>`. They allocate only for strings and lists.

### 9.3 Required configuration on the server side (launcher)

- `Config.js`:
  - `Server.Network.Host = "127.0.0.1"`, `Port = 9000`;
  - `AllowAutoAccountCreation` stays true;
  - `DDD.EnableDATPatching = false`;
  - the server must use **the same DAT files** the client reports, so iterations match.
- Generate the test account and password into `server/runtime/` (gitignored). The first account becomes Admin, which enables `@telepoi` and `@myloc` for tests.
- **Never commit packet logs.** ACE's `Packets` logger prints the LoginRequest, including the **clear-text password**, in hex.

### 9.4 Test strategy

1. **Pure unit tests (no server)** in `tests/Lifestone.Net.Tests`, xUnit:
   - Hash32 and ISAAC vectors (§10), including the scramble boundary (outputs #256–#259, counting from 1).
   - Checksum encode and verify for cleartext and encrypted packets (§10 packets).
   - Round-trips through the `PacketCodec`.
   - Sequence rules: first data packet is 2; ack-only reuses the sequence; a NAK is cleartext; a resend keeps its key.
   - Our own **fake ACE**, written from §2 (not OpenAC's ACE-derived model): handshake race (ConnectResponse sent too early is ignored), tail-first fragment reassembly, a RejectRetransmit consuming a sequence, a gap over 256 causing termination.
   - Fuzzing: truncated or corrupted datagrams must make `TryParse` return false and never throw.
2. **Retail-layout tests for client-to-server messages.**
   - MoveToState with and without actions, AutonomousPosition and Jump are built and compared with the byte vectors derived from the decompiled `Pack` functions (§10).
   - ACE's reader logic is mirrored in a test helper that asserts the fields round-trip.
   - This catches defect 1 and any regression toward Chorizite-style u16 commands.
3. **Golden fixtures from ACE without modifying or building ACE.**
   - In the bundled server's `log4net.config`, set the `Packets` logger to DEBUG. ACE then hex-dumps every datagram in both directions (`ConnectionListener.OnDataReceive`, `NetworkSession.SendPacketRaw`) and logs each packet's header, checksum and ISAAC XOR (`ServerPacket.ToString`). Write the log under `server/runtime/`, because it contains the password.
   - Run the loopback scenario (item 4) once.
   - Then extract the **server-to-client message payloads** with a small script: CharacterList, CreateObject for the player and for an NPC, a static and a creature, UpdatePosition, UpdateMotion (including a sticky case, from a creature attack), SetState, VectorUpdate, PlayerTeleport, DeleteObject, ParentEvent.
   - Store them under `tests/Lifestone.Net.Tests/Fixtures/ace/*.bin`. Strip the LoginRequest. Fixtures contain world data from the AGPL world DB, so the lead decides whether to commit them or regenerate them locally.
   - Tests assert that each parser consumes every byte and decodes the expected fields.
   - The more exhaustive alternative, holtburger-style, is a tiny console that references ACE's serializers. It needs a reviewed build of ACE and can come later.
4. **Loopback integration test** (`[Trait("Category","Loopback")]`, opt-in; skipped when ACE is not running):
   1. Start ACE bound to 127.0.0.1 through the launcher harness and wait for "world open".
   2. Log in with the runtime test account. Assert TimeSync is received, CharacterList is received, and the DDD check is complete.
   3. If there is no character, create one (heritage Aluvian, 55 SACs, start area 0). Assert F643 Ok.
   4. Enter the world. Assert PlayerCreate, CreateObject(self) with a Position (in landblock 0x8602 for a new character), and a CreateObject count above 0.
   5. Send LoginComplete. Assert SetState clears Hidden.
   6. Talk `@telepoi holtburg`. Assert F751 and an UpdatePosition in landblock 0xA9B4. Send LoginComplete again.
   7. Assert at least N CreateObjects in 0xA9B4: Holtburg statics, NPCs and the lifestone.
   8. Send MoveToState (WalkForward + Run), then AutonomousPosition every 200 ms, stepping about 2 m per update for 5 s, then MoveToState (Ready).
   9. Assert that ACE echoes UpdatePosition(self) within 0.5 m of each sent position, that no force-position sequence increments, and that `@myloc` agrees.
   10. Log off and disconnect, then verify the ACE session is removed (ACE log).
   11. Repeat the movement under `LossyTransportDecorator` (5% drop, 2% reorder) for 2 minutes. Assert no ISAAC desync, no termination and no "Checksum Failed" in the ACE log.
5. **Command table.** `tools/protocol/dump_command_table.py` writes a TSV from the local exe into a gitignored folder; the TSV is never committed. A test reads that TSV and is skipped when it is absent. It asserts that `MotionCommandTable` maps every low word to the exe's full ID, and that the table is *not* the 2013 one: AFKState must be 0x4300011B.

---

## 10. Golden vectors

These were produced by a throwaway stdlib-Python transliteration of the cited ACE code (`golden.py`). It is deliberately kept out of the repo because it ports ACE (AGPL) logic. Re-confirm the vectors in C# against ACE when §9.4 item 3 lands.

**Hash32**

| Input | Hash32 |
|---|---|
| (empty) | 0x00000000 |
| 01 02 03 04 | 0x04070201 |
| AA BB CC DD EE | 0xCBD1BBAA |
| 42 | 0x42010000 |
| 42 7F | 0x42810000 |
| 01 02 03 | 0x01050300 |

These equal OpenAC's `Hash32Tests` expectations.

**ISAAC (ACE `ISAAC.cs`)**

- Seed bytes `78 56 34 12`, outputs 1–16:
  `FDD4AFEB 398311D2 C71829A4 B19DF96A 7ED4A1E7 B718A446 5FA77DF1 5CB793D2 52C95BAC A8F8FD7E 18582C04 D257F506 4D11A3F0 919DE7A7 1D809C6B E97F65F8`. This is **identical** to OpenAC's `IsaacRandomTests.GoldenVectorsSeed12345678`.
- Same seed, outputs #256–#259 (they cross the first re-scramble): `DF7CA7E9 A608787A 2271E865 F43B889E`.
- Seed `00 00 00 00`, outputs 1–4: `182600F3 300B4A8D 301B6622 B08ACD21`.

**Client-to-server ack-only packet** (seq 2, flags 0x4000, Id 0, Time 0x1234, Iteration 1, ack = 5; cleartext):
`02 00 00 00 00 40 00 00 E8 B0 2A CD 00 00 34 12 04 00 01 00 05 00 00 00`

**Client-to-server encrypted packet with a LoginComplete fragment.**

Parameters: client seed 0x0BADF00D, so the first key is 0x99D25AF6; packet seq 2, flags 0x6, Id 0x0001, Time 0x1234, Iteration 1; fragment seq 1, Id 0x80000000, count 1, index 0, queue 9.

```
02 00 00 00 06 00 00 00 A4 13 BA E6 01 00 34 12 1C 00 01 00
01 00 00 00 00 00 00 80 01 00 1C 00 00 00 09 00
B1 F7 00 00 00 00 00 00 A1 00 00 00
```

ACE's `VerifyCRC` key recovery gives 0x99D25AF6, as it should.

**F658 CharacterList** (ACE writer; one character 0x50000001 "Tester", account "lifestone", 11 slots, TurbineChat on); 56 bytes:
`58 F6 00 00 00 00 00 00 01 00 00 00 01 00 00 50 06 00 54 65 73 74 65 72 00 00 00 00 00 00 00 00 0B 00 00 00 09 00 6C 69 66 65 73 74 6F 6E 65 00 01 00 00 00 01 00 00 00`

The OpenAC and Chorizite read logic both consume all 56 bytes and recover every field.

**F748 UpdatePosition** (GUID 0x50000001; cell 0xA9B40019; (84, 7.1, 94); q = (0.996917, 0, 0, −0.0784591); grounded; sequences 1, 7, 0, 0). Flags 0x34; 44 bytes:
`48 F7 00 00 01 00 00 50 34 00 00 00 19 00 B4 A9 00 00 A8 42 33 33 E3 40 00 00 BC 42 F4 35 7F 3F 2A AF A0 BD 01 00 07 00 00 00 00 00`

**F7B1/F61C MoveToState** (action sequence 1; hold key Run, forward WalkForward, forward hold key Run; same position and quaternion; sequences 1, 0, 0, 0; contact):
`B1 F7 00 00 01 00 00 00 1C F6 00 00 0D 00 00 00 02 00 00 00 05 00 00 45 02 00 00 00 19 00 B4 A9 00 00 A8 42 33 33 E3 40 00 00 BC 42 F4 35 7F 3F 00 00 00 00 00 00 00 00 2A AF A0 BD 01 00 00 00 00 00 00 00 01 00 00 00`

| Library | Result |
|---|---|
| OpenAC | Identical |
| Chorizite | 4 bytes shorter, with u16 commands and no trailing pad. ACE decodes it to garbage (cell 0x0000A9B4) |
| With one action, retail | 80 bytes |
| With one action, OpenAC | 76 bytes. ACE then reads cell 0x42A80000 (defect 1) |

**CharGen starter areas** (`client_portal.dat` 0x0E000002). These were decoded read-only, using the local-scout DAT reader and the layout from `ACE.DatLoader` `CharGen`/`StarterArea`. Every academy entry is at (12.32, −28.482, 0.005) with q(0.3389, 0, 0, 0.9408).

| Index | Starter area | Location cells |
|---|---|---|
| 0 | Holtburg | 0x860201AD (plus 4 alternates, 0x8603…, 0x8604…, 0x8702…, 0x8703…) |
| 1 | Shoushi | 0x7F0301AD… |
| 2 | Yaraq | 0x8C0401AD… |
| 3 | Sanamar | 0x720201AD… |
| 4 | OlthoiLair | 0xE6D3001E |

---

## 11. Open questions and risks

- **Defect 2** (sticky-GUID alignment) and the full CreateObject layout still need golden fixtures from a live ACE. Both come from reading code: the field widths were checked against the property types, but no bytes have been captured yet.
- **Retail behaviour ACE does not enforce.** These are ACE-tolerant either way, but matter for any non-ACE server:
  - AutonomousPosition cadence;
  - whether retail sends EchoRequest and Flow, and how often;
  - client fragment queue ids.
- **DAT iterations.** Lifestone must read them at runtime from the iteration record (0xFFFF0001) in each of its own DATs. The server bundle must ship the same DATs, or the session is booted.
- **Character start.** New characters start in the Academy dungeon (0x8602), not Holtburg. M2 either walks out of the Academy or uses an admin teleport. Decide which flow the shipped game uses.
- **OpenAC drift.** OpenAC is about a month old and changes daily. Vendor at the pinned commit and do not track upstream automatically; re-diff on purpose.
- **Licensing.** OpenAC is MIT. The structure, though not the code, of `Lifestone.Net` follows ACE behaviour. Facts such as opcodes, layouts and enum values are not copyrightable code, but do not paste ACE source. Lifestone is AGPL-licensed, so these constraints relax.

## 12. Sources

- ACE (`7715c052`):
  - `ACE.Server/Network/{PacketHeader, PacketHeaderFlags, PacketHeaderOptional, Packet, ClientPacket, ServerPacket, PacketFragment, PacketFragmentHeader, ClientPacketFragment, ServerPacketFragment, MessageFragment, MessageBuffer, ClientMessage, NetworkBundle, NetworkSession, Session, SessionConnectionData, ConnectionListener, GameMessageGroup, Extensions}.cs`
  - `Network/Managers/{NetworkManager, SocketManager, InboundMessageManager}.cs`
  - `Network/Packets/*.cs`, `Network/Handlers/{AuthenticationHandler, CharacterHandler, DDDHandler}.cs`
  - `Network/GameMessages/{GameMessage, GameMessageOpcode}.cs` and `Messages/*.cs`
  - `Network/GameEvent/{GameEventMessage, GameEventType}.cs`, `Network/GameAction/{GameActionPacket, GameActionType}.cs` and `Actions/{GameActionMoveToState, GameActionAutonomousPosition, GameActionJump, GameActionLoginComplete, GameActionTalk}.cs`
  - `Network/Motion/*.cs`, `Network/Structure/{PositionPack, JumpPack, CAllIterationList, CMostlyConsecutiveIntSet, PTaggedIterationList, Origin}.cs`, `Network/Sequence/UIntSequence.cs`
  - `ACE.Common/Cryptography/{Hash32, ISAAC, CryptoSystem}.cs`, `ACE.Common/Extensions/BinaryReaderExtensions.cs`, `ACE.Common/DDDConfiguration.cs`
  - `ACE.Entity/{ObjectGuid, Position, CharacterCreateInfo, Appearance}.cs`, `ACE.Entity/Enum/{MotionCommand, PositionFlags, PhysicsState, MovementStateFlag, MotionFlags, AuthFlags}.cs`
  - `ACE.Server/WorldObjects/{WorldObject_Networking, Player_Networking, Player_Tick, Player_Location}.cs`, `ACE.Server/Physics/PhysicsObj.cs`
  - `ACE.Server/Managers/{WorldManager, PropertyManager}.cs`, `ACE.Server/Entity/Timers.cs`, `ACE.Server/Config.js.example`
  - Via `gh api`: `ACE.Server/Factories/PlayerFactory.cs`, `ACE.Database/DatabaseManager.cs`, `ACE.DatLoader/{FileTypes/CharGen, Entity/StarterArea, Entity/Position, Entity/Frame}.cs`
- OpenAC (`1ee5a08`):
  - `src/AcDream.Core.Net/**`
  - `src/AcDream.Core/Physics/{RawMotionState, MotionCommandResolver, AceModernCommandCatalog, Retail2013CommandCatalog}.cs`
  - `tests/AcDream.Core.Net.Tests/{Cryptography, Transport/AceCryptoModel}.cs`
  - `Directory.Build.props`, `Directory.Packages.props`, `NuGet.Config`, `global.json`, `NOTICE.md`
- Chorizite.ACProtocol (`ff7dffd`):
  - `Chorizite.ACProtocol/{Chorizite.ACProtocol.csproj, Packets/ACPacketHeader.cs, Types/{RawMotionState, MoveToStatePack, JumpPack, AutonomousPositionPack, PackedMotionCommand, PositionPack, Origin, ObjDesc, Subpalette, CharacterIdentity}.generated.cs, Messages/S2C/{Login_LoginCharacterSet, Movement_PositionEvent, Item_CreateObject}.generated.cs, Enums/{Command, MovementCommand, StanceMode}.generated.cs, Extensions/*.cs, protocol.xml (header only)}`
  - `.github/workflows/*.yml`
- Mag-nus/acclient-AI-RE: `2013-09 11.4186/acclient-src/src/PORTAL/cphysobj/CPackClasses.c` (`MoveToStatePack::Pack`, `AutonomousPositionPack::Pack`, `JumpPack::Pack`), `src/PORTAL/canim/CMInterpPrim.c` (`RawMotionState::Pack`), `src/GAME/game_cevents/CM_Movement.c` (`Event_Jump`), `include/acclient_protos.h`; `2015-10 11.6096/docs/13-client-differences-2013-vs-2015.md`.
- holtburger: `crates/holtburger-protocol/{ARCHITECTURE.md, FIXTURES.md, src/messages/movement/actions.rs}`, `crates/holtburger-session/{ARCHITECTURE.md, src/session/auth.rs}`.
- Local: `docs/research/{game-logic, rea-and-symbols, local-scout}.md`; `tools/protocol/dump_command_table.py`.
