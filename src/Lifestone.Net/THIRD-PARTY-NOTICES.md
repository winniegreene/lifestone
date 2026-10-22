# Third-party notices: Lifestone.Net

Lifestone.Net (AGPL-3.0, like the rest of Lifestone) contains code taken from **eriknihlen/OpenAC** at commit
`1ee5a08581a7f4b0b22effdf911171a76cf4391c` (v0.1.24), path `src/AcDream.Core.Net/`, under the MIT licence below. Each
file names its OpenAC source in its first lines and lists what was changed. `docs/third-party/net.md` explains the whole
import: what was taken, what was left out, and the defects fixed afterwards.

The only other third-party code in Lifestone.Net is two enums' names and values ported from **ACEmulator/ACE** at
`95ef426d` (AGPL-3.0, Lifestone's own licence): `Types/PhysicsState.cs` and `Messages/Login/CharacterErrorCode.cs`. The
ports in the tests (OpenAC's transport tests and ACE's `CryptoSystem` oracle) carry their own headers and are listed in
`docs/third-party/net.md`.

Since M4 1.2 s0 two files also hold code from the same repository at a second commit, the signed head
`f22b04851c5fcc3f38acfe50fb704ca38130c081` (see the second table below); the files in the first table stay at `1ee5a08`.

The files that hold OpenAC code at `1ee5a08`:

| Lifestone.Net file | OpenAC source (`src/AcDream.Core.Net/`) |
|---|---|
| `Crypto/Hash32.cs` | `Cryptography/Hash32.cs` |
| `Crypto/IsaacRandom.cs` | `Cryptography/IsaacRandom.cs` |
| `Messages/Actions/ChatActionCodecs.cs` | `Messages/ChatRequests.cs` (Talk and Tell) |
| `Messages/Actions/LoginCompleteCodec.cs` | `Messages/GameActionLoginComplete.cs` |
| `Messages/Actions/MovementActionCodecs.cs` | `Messages/MoveToState.cs`, `Messages/AutonomousPosition.cs`, `Messages/JumpAction.cs` |
| `Messages/Actions/RawMotionStateCodec.cs` | `Messages/RawMotionStatePacker.cs` |
| `Messages/Chat/ChatCodecs.cs` | `Messages/HearSpeech.cs`, `Messages/EmoteText.cs`, `Messages/SoulEmote.cs` |
| `Messages/Chat/ServerMessageCodec.cs` | `Messages/ServerMessage.cs` |
| `Messages/Events/GameEventEnvelope.cs` | `Messages/GameEventEnvelope.cs` |
| `Messages/Login/CharGenResponseCodec.cs` | `Messages/CharGenVerificationResponse.cs` |
| `Messages/Login/CharacterCreateCodec.cs` | `Messages/CharacterCreate.cs` |
| `Messages/Login/CharacterDeleteCodec.cs` | `Messages/CharacterDelete.cs` |
| `Messages/Login/CharacterEnterWorldCodec.cs` | `Messages/CharacterEnterWorld.cs` |
| `Messages/Login/CharacterErrorCodec.cs` | `Messages/CharacterError.cs` |
| `Messages/Login/CharacterListCodec.cs` | `Messages/CharacterList.cs` |
| `Messages/Login/CharacterLogOffCodec.cs` | `Messages/CharacterLogOff.cs` |
| `Messages/Login/CharacterRestoreCodec.cs` | `Messages/CharacterRestore.cs` |
| `Messages/Login/DddCodec.cs` | `Messages/DddInterrogationResponse.cs`, `Messages/DddBegin.cs` |
| `Messages/Login/ServerNameCodec.cs` | `Messages/ServerName.cs` |
| `Messages/Self/PlayerDescriptionCodec.cs` | `Messages/PlayerDescriptionParser.cs`, `Messages/EnchantmentWireReader.cs` |
| `Messages/Self/PrivateUpdateCodec.cs` | `Messages/PrivateUpdateVital.cs`, `Messages/PrivateUpdateAttribute.cs`, `Messages/PrivateUpdateSkill.cs`, `Messages/PrivateUpdatePropertyInt.cs`, `Messages/PrivateUpdatePropertyInt64.cs` |
| `Messages/World/CreateObjectCodec.cs` | `Messages/CreateObject.cs` |
| `Messages/World/ModelDataCodec.cs` | `Messages/CreateObject.cs` (ReadModelData) |
| `Messages/World/MovementDataCodec.cs` | `Messages/CreateObject.cs`, `Messages/UpdateMotion.cs` (the movement body) |
| `Messages/World/ObjDescEventCodec.cs` | `Messages/ObjDescEvent.cs` |
| `Messages/World/ObjectEventCodecs.cs` | `Messages/DeleteObject.cs`, `Messages/SetState.cs`, `Messages/VectorUpdate.cs`, `Messages/ParentEvent.cs`, `Messages/PickupEvent.cs`, `Messages/SoundEvent.cs` |
| `Messages/World/PhysicsDescCodec.cs` | `Messages/CreateObject.cs`, `Messages/PhysicsSpawnData.cs` |
| `Messages/World/UpdateMotionCodec.cs` | `Messages/UpdateMotion.cs` (the header) |
| `Messages/World/UpdatePositionCodec.cs` | `Messages/UpdatePosition.cs` |
| `Messages/World/WeenieDescCodec.cs` | `Messages/PublicWeenieDescBody.cs` |
| `Packets/BorrowedPacket.cs` | `Packets/BorrowedPacket.cs` |
| `Packets/FragmentAssembler.cs` | `Packets/FragmentAssembler.cs` |
| `Packets/GameMessageFragment.cs` | `Messages/GameMessageFragment.cs` |
| `Packets/LoginRequest.cs` | `Packets/LoginRequest.cs` |
| `Packets/MessageFragment.cs` | `Packets/MessageFragment.cs` |
| `Packets/MessageFragmentHeader.cs` | `Packets/MessageFragmentHeader.cs` |
| `Packets/PacketCodec.cs` | `Packets/PacketCodec.cs` |
| `Packets/PacketHeader.cs` | `Packets/PacketHeader.cs` |
| `Packets/PacketHeaderFlags.cs` | `Packets/PacketHeaderFlags.cs` |
| `Packets/PacketWriter.cs` | `Packets/PacketWriter.cs` |
| `Packets/TransportDisconnect.cs` | `Packets/TransportDisconnect.cs` |
| `Sockets/UdpLoopbackSocket.cs` | `NetClient.cs` (the socket setup only) |
| `Transport/AckNakScheduler.cs` | `Transport/AckNakScheduler.cs` |
| `Transport/InboundSequenceTracker.cs` | `Transport/InboundSequenceTracker.cs` |
| `Transport/OutboundFlowQueue.cs` | `Transport/OutboundFlowQueue.cs` |
| `Transport/ReliableTransport.cs` | `Transport/ReliableTransport.cs` |
| `Transport/RetailPacketLossAverager.cs` | `Transport/RetailPacketLossAverager.cs` |
| `Transport/SentPacketStore.cs` | `Transport/SentPacketStore.cs` |
| `Transport/SequenceMath.cs` | `Transport/SequenceMath.cs` |
| `Transport/TransportClock.cs` | `Transport/TransportClock.cs` |
| `Transport/TransportStats.cs` | `Transport/TransportStats.cs` |

## OpenAC licence

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

The files that hold OpenAC code at `f22b048` (M4 D7; OpenAC source `src/AcDream.Core.Net/`):

| Lifestone.Net file | OpenAC source (`src/AcDream.Core.Net/`) |
|---|---|
| `Messages/Actions/CombatActionCodecs.cs` | `Messages/AttackTargetRequest.cs` |
| `Messages/Combat/AttackEventCodecs.cs` | `Messages/GameEvents.cs` (`ParseAttackDone`, `ParseCombatCommenceAttack`) |
| `Messages/Inventory/ContainerCodecs.cs` | `Messages/GameEvents.cs` (`ParseViewContents`, `ParseCloseGroundContainer`) |
| `Messages/Actions/ItemMoveActionCodecs.cs` | `Messages/InventoryActions.cs` (`BuildStackableSplitToContainer`, `BuildStackableSplitTo3D`, `BuildStackableSplitToWield`, `BuildGiveObjectRequest`, `BuildDropItem`, `BuildNoLongerViewingContents`) |
| `Messages/Vendor/VendorCodecs.cs` | `Messages/VendorApproach.cs` |
| `Messages/Actions/VendorActionCodecs.cs` | `Messages/VendorRequests.cs` (`BuildBuy`, `BuildSell`), `Messages/AppraiseRequest.cs` |
| `Messages/Inventory/AppraiseCodecs.cs` | `Messages/AppraiseInfoParser.cs` |
