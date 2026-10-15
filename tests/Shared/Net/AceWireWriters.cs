using System.Text;

namespace Lifestone.Tests.Shared.Net;

/// <summary>
/// Server-to-client message bodies as the bundled ACE writes them, for tests in every project: feed them to the
/// dispatcher, to a stand-in session or to the fake server (m2-plan §3.3 N3, N4, N7 and N10: login, world, chat, game
/// events and the player's own state). Each returns the whole message, opcode first, built with a plain <see cref="BinaryWriter"/> from docs/protocol.md §5 and the cited ACE writers
/// (ACEmulator/ACE @95ef426d, read, not copied): the bytes are independent of Lifestone.Net's own codecs, which is the
/// point of a round-trip test. BCL only, so every test project can link it.
/// </summary>
/// <remarks>
/// <para>Strings follow ACE's <c>WriteString16L</c> (<c>ACE.Server/Network/Extensions.cs</c>): a u16 count of
/// characters, the Windows-1252 bytes (a character the code page lacks becomes '?'), then zero padding so that the
/// count and the bytes fill a multiple of 4. Alignment pads from the opcode, as ACE's <c>Align</c> pads its message
/// stream.</para>
/// <para>Only what ACE really sends is offered: for example CharacterList's deleted list is always empty here, as it is
/// in <c>GameMessageCharacterList</c>; a test of another layout writes its bytes itself.</para>
/// </remarks>
public static class AceWireWriters
{
    /// <summary>F658 CharacterList.</summary>
    public const uint CharacterListOpcode = 0xF658;

    /// <summary>F7E1 ServerName.</summary>
    public const uint ServerNameOpcode = 0xF7E1;

    /// <summary>F659 CharacterError.</summary>
    public const uint CharacterErrorOpcode = 0xF659;

    /// <summary>F643 CharacterCreateResponse, also the CharacterRestore response.</summary>
    public const uint CharacterCreateResponseOpcode = 0xF643;

    /// <summary>F7DC AccountBoot.</summary>
    public const uint AccountBootOpcode = 0xF7DC;

    /// <summary>F7C1 AccountBanned.</summary>
    public const uint AccountBannedOpcode = 0xF7C1;

    /// <summary>F7E5 DDD_Interrogation.</summary>
    public const uint DddInterrogationOpcode = 0xF7E5;

    /// <summary>F7E7 DDD_BeginDDD.</summary>
    public const uint DddBeginOpcode = 0xF7E7;

    /// <summary>F7EA DDD_EndDDD.</summary>
    public const uint DddEndOpcode = 0xF7EA;

    /// <summary>F7DF CharacterEnterWorldServerReady.</summary>
    public const uint EnterWorldServerReadyOpcode = 0xF7DF;

    /// <summary>F655 CharacterDelete (the server's acknowledgement).</summary>
    public const uint CharacterDeleteOpcode = 0xF655;

    /// <summary>F653 CharacterLogOff (the server's).</summary>
    public const uint CharacterLogOffOpcode = 0xF653;

    /// <summary>F746 PlayerCreate.</summary>
    public const uint PlayerCreateOpcode = 0xF746;

    /// <summary>F7E0 ServerMessage (ACE <c>GameMessageSystemChat</c>).</summary>
    public const uint ServerMessageOpcode = 0xF7E0;

    /// <summary>F7B0 GameEvent.</summary>
    public const uint GameEventOpcode = 0xF7B0;

    /// <summary>F745 ObjectCreate.</summary>
    public const uint ObjectCreateOpcode = 0xF745;

    /// <summary>F748 UpdatePosition.</summary>
    public const uint UpdatePositionOpcode = 0xF748;

    /// <summary>F74B SetState.</summary>
    public const uint SetStateOpcode = 0xF74B;

    /// <summary>F7DB UpdateObject.</summary>
    public const uint UpdateObjectOpcode = 0xF7DB;

    /// <summary>F747 ObjectDelete.</summary>
    public const uint ObjectDeleteOpcode = 0xF747;

    /// <summary>F74C UpdateMotion.</summary>
    public const uint UpdateMotionOpcode = 0xF74C;

    /// <summary>F74E VectorUpdate.</summary>
    public const uint VectorUpdateOpcode = 0xF74E;

    /// <summary>F751 PlayerTeleport.</summary>
    public const uint PlayerTeleportOpcode = 0xF751;

    /// <summary>F749 ParentEvent.</summary>
    public const uint ParentEventOpcode = 0xF749;

    /// <summary>F74A PickupEvent.</summary>
    public const uint PickupEventOpcode = 0xF74A;

    /// <summary>F625 ObjDescEvent.</summary>
    public const uint ObjDescEventOpcode = 0xF625;

    /// <summary>F750 Sound.</summary>
    public const uint SoundOpcode = 0xF750;

    /// <summary>F755 PlayEffect (ACE <c>GameMessageScript</c>).</summary>
    public const uint PlayEffectOpcode = 0xF755;

    /// <summary>0x02BB HearSpeech.</summary>
    public const uint HearSpeechOpcode = 0x02BB;

    /// <summary>0x02BC HearRangedSpeech.</summary>
    public const uint HearRangedSpeechOpcode = 0x02BC;

    /// <summary>0x01E0 EmoteText.</summary>
    public const uint EmoteTextOpcode = 0x01E0;

    /// <summary>0x01E2 SoulEmote.</summary>
    public const uint SoulEmoteOpcode = 0x01E2;

    /// <summary>GameEvent 0x02BD Tell.</summary>
    public const uint TellEventType = 0x02BD;

    /// <summary>GameEvent 0x0004 PopupString.</summary>
    public const uint PopupStringEventType = 0x0004;

    /// <summary>GameEvent 0x028A WeenieError.</summary>
    public const uint WeenieErrorEventType = 0x028A;

    /// <summary>GameEvent 0x028B WeenieErrorWithString.</summary>
    public const uint WeenieErrorWithStringEventType = 0x028B;

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    public const uint TransientStringEventType = 0x02EB;

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    public const uint UseDoneEventType = 0x01C7;

    /// <summary>GameEvent 0x01C0 UpdateHealth.</summary>
    public const uint UpdateHealthEventType = 0x01C0;

    /// <summary>GameEvent 0x01EA PingResponse.</summary>
    public const uint PingResponseEventType = 0x01EA;

    /// <summary>GameEvent 0x0013 PlayerDescription.</summary>
    public const uint PlayerDescriptionEventType = 0x0013;

    /// <summary>0x02CD PrivateUpdatePropertyInt.</summary>
    public const uint PrivateUpdatePropertyIntOpcode = 0x02CD;

    /// <summary>0x02CF PrivateUpdatePropertyInt64.</summary>
    public const uint PrivateUpdatePropertyInt64Opcode = 0x02CF;

    /// <summary>0x02DD PrivateUpdateSkill.</summary>
    public const uint PrivateUpdateSkillOpcode = 0x02DD;

    /// <summary>0x02E3 PrivateUpdateAttribute.</summary>
    public const uint PrivateUpdateAttributeOpcode = 0x02E3;

    /// <summary>0x02E7 PrivateUpdateVital.</summary>
    public const uint PrivateUpdateVitalOpcode = 0x02E7;

    /// <summary>0x02E9 PrivateUpdateAttribute2ndLevel.</summary>
    public const uint PrivateUpdateAttribute2ndLevelOpcode = 0x02E9;

    /// <summary>The ASCII bytes "HiFi" read as a little-endian i32: the high-res dat's type in the DDD messages.</summary>
    public const int HighResDatType = 0x69466948;

    /// <summary>Windows-1252 as ACE uses it: unrepresentable characters become '?'.</summary>
    public static Encoding Windows1252 { get; } =
        CodePagesEncodingProvider.Instance.GetEncoding(1252, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"))
        ?? throw new InvalidOperationException("the runtime has no code page 1252");

    // ---- Primitives (ACE.Server/Network/Extensions.cs) ----

    /// <summary>ACE's <c>WriteString16L</c>: u16 character count, Windows-1252 bytes, pad to 4 from the count.</summary>
    public static void WriteString16L(BinaryWriter writer, string? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        value ??= string.Empty;
        writer.Write((ushort)value.Length);
        writer.Write(Windows1252.GetBytes(value));
        writer.Write(new byte[PadTo4(sizeof(ushort) + value.Length)]);
    }

    /// <summary>ACE's <c>WritePackedDword</c>: one u16 up to 0x7FFF, else the high half with bit 15 set, then the low half.</summary>
    public static void WritePackedDword(BinaryWriter writer, uint value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value <= 0x7FFF)
        {
            writer.Write((ushort)value);
            return;
        }

        writer.Write((ushort)((value >> 16) | 0x8000));
        writer.Write((ushort)(value & 0xFFFF));
    }

    /// <summary>ACE's <c>WritePackedDwordOfKnownType</c>: strips <paramref name="knownType"/> when the value has it.</summary>
    public static void WritePackedDwordOfKnownType(BinaryWriter writer, uint value, uint knownType)
    {
        if ((value & knownType) != 0)
        {
            value -= knownType;
        }

        WritePackedDword(writer, value);
    }

    /// <summary>ACE's <c>Align</c>: zero bytes up to a multiple of 4 from the start of the message.</summary>
    public static void Align(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(new byte[PadTo4((int)writer.BaseStream.Length)]);
    }

    // ---- Login and character select ----

    /// <summary>
    /// F658 CharacterList (<c>GameMessageCharacterList</c>): u32 0, the characters, an empty deleted list, the slot
    /// count, the account, the use-TurbineChat word and 1 (Throne of Destiny). <see cref="AceCharacter.DeleteField"/>
    /// is written raw; ACE sends 1 for every character pending deletion.
    /// </summary>
    public static byte[] CharacterList(IReadOnlyList<AceCharacter> characters, string account, uint slotCount = 11, bool useTurbineChat = true)
    {
        ArgumentNullException.ThrowIfNull(characters);
        return Message(CharacterListOpcode, w =>
        {
            w.Write(0u);
            w.Write(characters.Count);
            foreach (var character in characters)
            {
                w.Write(character.Guid);
                WriteString16L(w, character.Name);
                w.Write(character.DeleteField);
            }

            w.Write(0u);
            w.Write(slotCount);
            WriteString16L(w, account);
            w.Write(useTurbineChat ? 1u : 0u);
            w.Write(1u);
        });
    }

    /// <summary>F7E1 ServerName (<c>GameMessageServerName</c>): i32 online, i32 maximum, the world's name.</summary>
    public static byte[] ServerName(string worldName, int currentConnections = 0, int maxConnections = -1) =>
        Message(ServerNameOpcode, w =>
        {
            w.Write(currentConnections);
            w.Write(maxConnections);
            WriteString16L(w, worldName);
        });

    /// <summary>F659 CharacterError (<c>GameMessageCharacterError</c>).</summary>
    public static byte[] CharacterError(uint code) => Message(CharacterErrorOpcode, w => w.Write(code));

    /// <summary>
    /// F643 CharacterCreateResponse (<c>GameMessageCharacterCreateResponse</c>): the result; on 1 (Ok) also the GUID,
    /// the name and u32 0.
    /// </summary>
    public static byte[] CharacterCreateResponse(uint result, uint guid = 0, string? name = null) =>
        Message(CharacterCreateResponseOpcode, w =>
        {
            w.Write(result);
            if (result == 1)
            {
                w.Write(guid);
                WriteString16L(w, name);
                w.Write(0u);
            }
        });

    /// <summary>The restore's answer (<c>GameMessageCharacterRestore</c>): F643 {1, GUID, name, seconds greyed out}.</summary>
    public static byte[] CharacterRestoreResponse(uint guid, string name, uint secondsGreyedOut = 0) =>
        Message(CharacterCreateResponseOpcode, w =>
        {
            w.Write(1u);
            w.Write(guid);
            WriteString16L(w, name);
            w.Write(secondsGreyedOut);
        });

    /// <summary>F7DC AccountBoot (<c>GameMessageBootAccount</c>): the reason only when there is one.</summary>
    public static byte[] AccountBoot(string? reason = null) =>
        Message(AccountBootOpcode, w =>
        {
            if (reason is not null)
            {
                WriteString16L(w, reason);
            }
        });

    /// <summary>F7C1 AccountBanned (<c>GameMessageAccountBanned</c>): u32 seconds left, then the reason when there is one.</summary>
    public static byte[] AccountBanned(uint secondsRemaining, string? reason = null) =>
        Message(AccountBannedOpcode, w =>
        {
            w.Write(secondsRemaining);
            if (reason is not null)
            {
                WriteString16L(w, reason);
            }
        });

    /// <summary>
    /// F7E5 DDD_Interrogation (<c>GameMessageDDDInterrogation</c>): region 1, name-rule language 1, product id 1 (5 when
    /// the high-res dat is allowed), and the supported languages {0, 1}.
    /// </summary>
    public static byte[] DddInterrogation(bool allowHighRes = false) =>
        Message(DddInterrogationOpcode, w =>
        {
            w.Write(1u);
            w.Write(1u);
            w.Write(allowHighRes ? 5u : 1u);
            w.Write(2u);
            w.Write(0u);
            w.Write(1u);
        });

    /// <summary>
    /// F7E7 DDD_BeginDDD (<c>GameMessageDDDBeginDDD</c>): u32 total size, u32 entry count, then each missing iteration as
    /// {i32 dat type, i32 dat id, i32 iteration, u32 count and ids to download, u32 count and ids to purge}.
    /// </summary>
    public static byte[] DddBegin(uint totalFileSize, IReadOnlyList<AceMissingIteration> iterations)
    {
        ArgumentNullException.ThrowIfNull(iterations);
        return Message(DddBeginOpcode, w =>
        {
            w.Write(totalFileSize);
            w.Write((uint)iterations.Count);
            foreach (var iteration in iterations)
            {
                w.Write(iteration.DatType);
                w.Write(iteration.DatId);
                w.Write(iteration.Iteration);
                w.Write(iteration.Download.Count);
                foreach (var id in iteration.Download)
                {
                    w.Write(id);
                }

                w.Write(iteration.Purge.Count);
                foreach (var id in iteration.Purge)
                {
                    w.Write(id);
                }
            }
        });
    }

    /// <summary>F7EA DDD_EndDDD: the opcode alone.</summary>
    public static byte[] DddEnd() => Message(DddEndOpcode, static _ => { });

    /// <summary>F7DF CharacterEnterWorldServerReady: the opcode alone.</summary>
    public static byte[] EnterWorldServerReady() => Message(EnterWorldServerReadyOpcode, static _ => { });

    /// <summary>F655 CharacterDelete, the server's acknowledgement (<c>GameMessageCharacterDelete</c>): the opcode alone.</summary>
    public static byte[] CharacterDeleteAck() => Message(CharacterDeleteOpcode, static _ => { });

    /// <summary>F653 CharacterLogOff, the server's (<c>GameMessageCharacterLogOff</c>): the opcode alone.</summary>
    public static byte[] CharacterLogOff() => Message(CharacterLogOffOpcode, static _ => { });

    /// <summary>F746 PlayerCreate (<c>GameMessagePlayerCreate</c>): the player's GUID.</summary>
    public static byte[] PlayerCreate(uint guid) => Message(PlayerCreateOpcode, w => w.Write(guid));

    /// <summary>F7E0 ServerMessage (<c>GameMessageSystemChat</c>): the text and the i32 chat type.</summary>
    public static byte[] ServerMessage(string text, int chatType) =>
        Message(ServerMessageOpcode, w =>
        {
            WriteString16L(w, text);
            w.Write(chatType);
        });

    /// <summary>An F7B0 GameEvent (<c>GameEventMessage</c>): player GUID, event sequence, event type, then <paramref name="payload"/>.</summary>
    public static byte[] GameEvent(uint playerGuid, uint sequence, uint eventType, ReadOnlySpan<byte> payload)
    {
        var bytes = payload.ToArray();
        return Message(GameEventOpcode, w =>
        {
            w.Write(playerGuid);
            w.Write(sequence);
            w.Write(eventType);
            w.Write(bytes);
        });
    }

    /// <summary>GameEvent 0x01EA PingResponse (<c>GameEventPingResponse</c>): no payload. ACE sends it at character select.</summary>
    public static byte[] PingResponse(uint playerGuid, uint sequence) => GameEvent(playerGuid, sequence, PingResponseEventType, []);

    // ---- World ----

    /// <summary>F74B SetState (<c>GameMessageSetState</c>): GUID, u32 physics state, u16 instance and u16 state sequences.</summary>
    public static byte[] SetState(uint guid, uint physicsState, ushort instanceSequence, ushort stateSequence) =>
        Message(SetStateOpcode, w =>
        {
            w.Write(guid);
            w.Write(physicsState);
            w.Write(instanceSequence);
            w.Write(stateSequence);
        });

    /// <summary>
    /// F748 UpdatePosition (<c>GameMessageUpdatePosition</c>, <c>PositionPack</c>): GUID, u32 flags, cell, origin, the
    /// quaternion components that are not zero (W, X, Y, Z order; a zero one sets its "has no" flag), the velocity when
    /// not zero, the placement when given, then the instance, position, teleport and force-position sequences.
    /// </summary>
    public static byte[] UpdatePosition(uint guid, AcePosition position, bool grounded, AceUpdatePositionSequences sequences, (float X, float Y, float Z) velocity = default, uint? placement = null) =>
        Message(UpdatePositionOpcode, w =>
        {
            var hasVelocity = velocity.X != 0f || velocity.Y != 0f || velocity.Z != 0f;
            uint flags = 0;
            if (hasVelocity) flags |= 0x01;
            if (placement is not null) flags |= 0x02;
            if (grounded) flags |= 0x04;
            if (position.Qw == 0f) flags |= 0x08;
            if (position.Qx == 0f) flags |= 0x10;
            if (position.Qy == 0f) flags |= 0x20;
            if (position.Qz == 0f) flags |= 0x40;
            w.Write(guid);
            w.Write(flags);
            w.Write(position.Cell);
            w.Write(position.X);
            w.Write(position.Y);
            w.Write(position.Z);
            if ((flags & 0x08) == 0) w.Write(position.Qw);
            if ((flags & 0x10) == 0) w.Write(position.Qx);
            if ((flags & 0x20) == 0) w.Write(position.Qy);
            if ((flags & 0x40) == 0) w.Write(position.Qz);
            if (hasVelocity)
            {
                w.Write(velocity.X);
                w.Write(velocity.Y);
                w.Write(velocity.Z);
            }

            if (placement is { } id) w.Write(id);
            w.Write(sequences.Instance);
            w.Write(sequences.Position);
            w.Write(sequences.Teleport);
            w.Write(sequences.ForcePosition);
        });

    /// <summary>
    /// F745 ObjectCreate (<c>WorldObject_Networking.SerializeCreateObject</c>): the GUID, the model data, the physics
    /// description (the fields <paramref name="desc"/> sets, flagged and written in ACE's order, then the nine sequences)
    /// and the weenie description (the header, the second header when one of its fields is set, then the optional fields
    /// that are set, in ACE's order).
    /// </summary>
    public static byte[] ObjectCreate(AceCreateObject desc) => CreateObjectBody(ObjectCreateOpcode, desc);

    /// <summary>F7DB UpdateObject (<c>GameMessageUpdateObject</c>): the same body as <see cref="ObjectCreate"/>.</summary>
    public static byte[] UpdateObject(AceCreateObject desc) => CreateObjectBody(UpdateObjectOpcode, desc);

    /// <summary>F747 ObjectDelete (<c>GameMessageDeleteObject</c>): GUID, u16 instance sequence, Align.</summary>
    public static byte[] ObjectDelete(uint guid, ushort instanceSequence) =>
        Message(ObjectDeleteOpcode, w =>
        {
            w.Write(guid);
            w.Write(instanceSequence);
            Align(w);
        });

    /// <summary>
    /// F74C UpdateMotion (<c>GameMessageUpdateMotion</c>, <c>MovementData.Write</c> with its header): GUID, u16 instance
    /// sequence, u16 movement sequence, u16 server-control sequence, u8 is-autonomous, Align, then the movement body.
    /// </summary>
    public static byte[] UpdateMotion(uint guid, ushort instanceSequence, ushort movementSequence, ushort serverControlSequence, bool isAutonomous, AceMovementData movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return Message(UpdateMotionOpcode, w =>
        {
            w.Write(guid);
            w.Write(instanceSequence);
            w.Write(movementSequence);
            w.Write(serverControlSequence);
            w.Write(isAutonomous ? (byte)1 : (byte)0);
            Align(w);
            WriteMovementData(w, movement);
        });
    }

    /// <summary>
    /// A MovementData body without its header (<c>MovementData.Write(writer, header: false)</c>): u8 type, u8 motion
    /// flags, u16 stance, then <c>MovementInvalid</c> (the interpreted state, <c>InterpretedMotionState.Write</c>'s Align,
    /// then the sticky GUID when flag 0x1 is set), <c>MoveToObject</c>, <c>MoveToPosition</c>, <c>TurnToObject</c> or
    /// <c>TurnToHeading</c>.
    /// </summary>
    public static void WriteMovementData(BinaryWriter writer, AceMovementData movement)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(movement);
        writer.Write(movement.MovementType);
        writer.Write(movement.MotionFlags);
        writer.Write(movement.Stance);
        switch (movement.MovementType)
        {
            case 0:
                uint flags = 0;
                if (movement.Style is not null) flags |= 0x1;
                if (movement.Forward is not null) flags |= 0x2;
                if (movement.ForwardSpeed is not null) flags |= 0x4;
                if (movement.Sidestep is not null) flags |= 0x8;
                if (movement.SidestepSpeed is not null) flags |= 0x10;
                if (movement.Turn is not null) flags |= 0x20;
                if (movement.TurnSpeed is not null) flags |= 0x40;
                writer.Write(flags | ((uint)movement.Commands.Count << 7));
                if (movement.Style is { } style) writer.Write(style);
                if (movement.Forward is { } forward) writer.Write(forward);
                if (movement.Sidestep is { } sidestep) writer.Write(sidestep);
                if (movement.Turn is { } turn) writer.Write(turn);
                if (movement.ForwardSpeed is { } forwardSpeed) writer.Write(forwardSpeed);
                if (movement.SidestepSpeed is { } sidestepSpeed) writer.Write(sidestepSpeed);
                if (movement.TurnSpeed is { } turnSpeed) writer.Write(turnSpeed);
                foreach (var item in movement.Commands)
                {
                    // MotionItem: the command's low word, the sequence with bit 15 set for a client-initiated motion, the speed.
                    writer.Write(item.Command);
                    writer.Write((ushort)((item.Stamp & 0x7FFF) | (item.Autonomous ? 0x8000 : 0)));
                    writer.Write(item.Speed);
                }

                Align(writer);
                if ((movement.MotionFlags & 0x1) != 0)
                {
                    writer.Write(movement.StickyGuid);
                }

                break;
            case 6 or 7:
                if (movement.MovementType == 6)
                {
                    writer.Write(movement.TargetGuid);
                }

                writer.Write(movement.OriginCell);
                writer.Write(movement.OriginX);
                writer.Write(movement.OriginY);
                writer.Write(movement.OriginZ);
                writer.Write(movement.ParameterFlags);
                writer.Write(movement.DistanceToObject);
                writer.Write(movement.MinDistance);
                writer.Write(movement.FailDistance);
                writer.Write(movement.Speed);
                writer.Write(movement.WalkRunThreshold);
                writer.Write(movement.DesiredHeading);
                writer.Write(movement.RunRate);
                break;
            case 8 or 9:
                if (movement.MovementType == 8)
                {
                    writer.Write(movement.TargetGuid);
                    writer.Write(movement.ObjectHeading);
                }

                writer.Write(movement.ParameterFlags);
                writer.Write(movement.Speed);
                writer.Write(movement.DesiredHeading);
                break;
            default:
                throw new ArgumentException($"ACE writes no movement type {movement.MovementType}", nameof(movement));
        }
    }

    /// <summary>F74E VectorUpdate (<c>GameMessageVectorUpdate</c>): GUID, velocity, omega, u16 instance and vector sequences.</summary>
    public static byte[] VectorUpdate(uint guid, (float X, float Y, float Z) velocity, (float X, float Y, float Z) omega, ushort instanceSequence, ushort vectorSequence) =>
        Message(VectorUpdateOpcode, w =>
        {
            w.Write(guid);
            WriteVector(w, velocity);
            WriteVector(w, omega);
            w.Write(instanceSequence);
            w.Write(vectorSequence);
        });

    /// <summary>F751 PlayerTeleport (<c>GameMessagePlayerTeleport</c>): u16 teleport sequence, Align.</summary>
    public static byte[] PlayerTeleport(ushort teleportSequence) =>
        Message(PlayerTeleportOpcode, w =>
        {
            w.Write(teleportSequence);
            Align(w);
        });

    /// <summary>F749 ParentEvent (<c>GameMessageParentEvent</c>): parent, child, i32 location, i32 placement, the parent's
    /// instance sequence and the child's position sequence.</summary>
    public static byte[] ParentEvent(uint parentGuid, uint childGuid, int parentLocation, int placement, ushort parentInstanceSequence, ushort childPositionSequence) =>
        Message(ParentEventOpcode, w =>
        {
            w.Write(parentGuid);
            w.Write(childGuid);
            w.Write(parentLocation);
            w.Write(placement);
            w.Write(parentInstanceSequence);
            w.Write(childPositionSequence);
        });

    /// <summary>F74A PickupEvent (<c>GameMessagePickupEvent</c>): GUID, u16 instance and position sequences.</summary>
    public static byte[] PickupEvent(uint guid, ushort instanceSequence, ushort positionSequence) =>
        Message(PickupEventOpcode, w =>
        {
            w.Write(guid);
            w.Write(instanceSequence);
            w.Write(positionSequence);
        });

    /// <summary>F625 ObjDescEvent (<c>SerializeUpdateModelData</c>): GUID, the model data, u16 instance and visual-description sequences.</summary>
    public static byte[] ObjDescEvent(uint guid, AceModelData model, ushort instanceSequence, ushort objDescSequence)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Message(ObjDescEventOpcode, w =>
        {
            w.Write(guid);
            WriteModelData(w, model.BasePalette, model.SubPalettes, model.TextureChanges, model.AnimPartChanges);
            w.Write(instanceSequence);
            w.Write(objDescSequence);
        });
    }

    /// <summary>F750 Sound (<c>GameMessageSound</c>): GUID, u32 sound, f32 volume.</summary>
    public static byte[] Sound(uint guid, uint sound, float volume = 1f) =>
        Message(SoundOpcode, w =>
        {
            w.Write(guid);
            w.Write(sound);
            w.Write(volume);
        });

    /// <summary>F755 PlayEffect (<c>GameMessageScript</c>): GUID, u32 PlayScript, f32 speed.</summary>
    public static byte[] PlayEffect(uint guid, uint script, float speed = 1f) =>
        Message(PlayEffectOpcode, w =>
        {
            w.Write(guid);
            w.Write(script);
            w.Write(speed);
        });

    // ---- Chat ----

    /// <summary>0x02BB HearSpeech (<c>GameMessageHearSpeech</c>): text, sender name, sender GUID, chat type.</summary>
    public static byte[] HearSpeech(string text, string senderName, uint senderGuid, uint chatType) =>
        Message(HearSpeechOpcode, w =>
        {
            WriteString16L(w, text);
            WriteString16L(w, senderName);
            w.Write(senderGuid);
            w.Write(chatType);
        });

    /// <summary>0x02BC HearRangedSpeech (<c>GameMessageHearRangedSpeech</c>): text, sender name, sender GUID, range, chat type.</summary>
    public static byte[] HearRangedSpeech(string text, string senderName, uint senderGuid, float range, uint chatType) =>
        Message(HearRangedSpeechOpcode, w =>
        {
            WriteString16L(w, text);
            WriteString16L(w, senderName);
            w.Write(senderGuid);
            w.Write(range);
            w.Write(chatType);
        });

    /// <summary>0x01E0 EmoteText (<c>GameMessageEmoteText</c>): sender GUID, sender name, text.</summary>
    public static byte[] EmoteText(uint senderGuid, string senderName, string text) => Emote(EmoteTextOpcode, senderGuid, senderName, text);

    /// <summary>0x01E2 SoulEmote (<c>GameMessageSoulEmote</c>): sender GUID, sender name, text.</summary>
    public static byte[] SoulEmote(uint senderGuid, string senderName, string text) => Emote(SoulEmoteOpcode, senderGuid, senderName, text);

    // ---- Game events (GameEvent/Events/*) ----

    /// <summary>GameEvent 0x02BD Tell (<c>GameEventTell</c>): text, sender name, sender, target, chat type, then u32 0.</summary>
    public static byte[] TellEvent(uint playerGuid, uint sequence, string text, string senderName, uint senderGuid, uint targetGuid, uint chatType) =>
        Event(playerGuid, sequence, TellEventType, w =>
        {
            WriteString16L(w, text);
            WriteString16L(w, senderName);
            w.Write(senderGuid);
            w.Write(targetGuid);
            w.Write(chatType);
            w.Write(0u);
        });

    /// <summary>GameEvent 0x0004 PopupString (<c>GameEventPopupString</c>): the text.</summary>
    public static byte[] PopupString(uint playerGuid, uint sequence, string text) =>
        Event(playerGuid, sequence, PopupStringEventType, w => WriteString16L(w, text));

    /// <summary>GameEvent 0x028A WeenieError (<c>GameEventWeenieError</c>): the code.</summary>
    public static byte[] WeenieError(uint playerGuid, uint sequence, uint error) =>
        Event(playerGuid, sequence, WeenieErrorEventType, w => w.Write(error));

    /// <summary>GameEvent 0x028B WeenieErrorWithString (<c>GameEventWeenieErrorWithString</c>): the code, then the text.</summary>
    public static byte[] WeenieErrorWithString(uint playerGuid, uint sequence, uint error, string text) =>
        Event(playerGuid, sequence, WeenieErrorWithStringEventType, w =>
        {
            w.Write(error);
            WriteString16L(w, text);
        });

    /// <summary>GameEvent 0x02EB CommunicationTransientString (<c>GameEventCommunicationTransientString</c>): the text.</summary>
    public static byte[] TransientString(uint playerGuid, uint sequence, string text) =>
        Event(playerGuid, sequence, TransientStringEventType, w => WriteString16L(w, text));

    /// <summary>GameEvent 0x01C7 UseDone (<c>GameEventUseDone</c>): the WeenieError (0 = success).</summary>
    public static byte[] UseDone(uint playerGuid, uint sequence, uint error = 0) =>
        Event(playerGuid, sequence, UseDoneEventType, w => w.Write(error));

    /// <summary>GameEvent 0x01C0 UpdateHealth (<c>GameEventUpdateHealth</c>): the object and its health fraction.</summary>
    public static byte[] UpdateHealth(uint playerGuid, uint sequence, uint guid, float health) =>
        Event(playerGuid, sequence, UpdateHealthEventType, w =>
        {
            w.Write(guid);
            w.Write(health);
        });

    // ---- The player's own state (N10) ----

    /// <summary>
    /// GameEvent 0x0013 PlayerDescription (<c>GameEventPlayerDescription.WriteEventBody</c>): the property flags and the
    /// weenie type, the property tables ACE fills (int, int64, bool, double, string, data id, instance id, then the
    /// LastOutsideDeath position), the vector flags (attributes and skills always, spells and enchantments when there
    /// are any), the has-health word, the full attribute cache, the skills, the spells (each written with 2.0, ACE's
    /// "new spell configuration"), the enchantment registry, then the player module (option flags, options, shortcuts,
    /// eight spell bars, desired components, spellbook filters, the second options word, the gameplay options bytes) and
    /// the inventory and equipped lists. The bucket counts are ACE's comparers'.
    /// </summary>
    public static byte[] PlayerDescription(uint playerGuid, uint sequence, AcePlayerDescription d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return Event(playerGuid, sequence, PlayerDescriptionEventType, w =>
        {
            var flagsPosition = w.BaseStream.Position;
            uint propertyFlags = 0;
            w.Write(0u);
            w.Write(d.WeenieType);
            if (d.Ints.Count > 0)
            {
                propertyFlags |= 0x1;
                WriteTableHeader(w, d.Ints.Count, 64);
                foreach (var (key, value) in d.Ints)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }

            if (d.Int64s.Count > 0)
            {
                propertyFlags |= 0x80;
                WriteTableHeader(w, d.Int64s.Count, 64);
                foreach (var (key, value) in d.Int64s)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }

            if (d.Bools.Count > 0)
            {
                propertyFlags |= 0x2;
                WriteTableHeader(w, d.Bools.Count, 32);
                foreach (var (key, value) in d.Bools)
                {
                    w.Write(key);
                    w.Write(value ? 1u : 0u);
                }
            }

            if (d.Doubles.Count > 0)
            {
                propertyFlags |= 0x4;
                WriteTableHeader(w, d.Doubles.Count, 32);
                foreach (var (key, value) in d.Doubles)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }

            if (d.Strings.Count > 0)
            {
                propertyFlags |= 0x10;
                WriteTableHeader(w, d.Strings.Count, 32);
                foreach (var (key, value) in d.Strings)
                {
                    w.Write(key);
                    WriteString16L(w, value);
                }
            }

            if (d.DataIds.Count > 0)
            {
                propertyFlags |= 0x8;
                WriteTableHeader(w, d.DataIds.Count, 32);
                foreach (var (key, value) in d.DataIds)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }

            if (d.InstanceIds.Count > 0)
            {
                propertyFlags |= 0x40;
                WriteTableHeader(w, d.InstanceIds.Count, 32);
                foreach (var (key, value) in d.InstanceIds)
                {
                    w.Write(key);
                    w.Write(value);
                }
            }

            if (d.LastOutsideDeath is { } death)
            {
                propertyFlags |= 0x20;
                WriteTableHeader(w, 1, 16);
                w.Write(0x0Eu);  // PositionType.LastOutsideDeath
                WritePosition(w, death);
            }

            var end = w.BaseStream.Position;
            w.BaseStream.Position = flagsPosition;
            w.Write(propertyFlags);
            w.BaseStream.Position = end;

            var vectorFlags = 0x1u | 0x2u;
            if (d.Spells.Count > 0)
            {
                vectorFlags |= 0x100;
            }

            if (d.Enchantments is not null)
            {
                vectorFlags |= 0x200;
            }

            w.Write(vectorFlags);
            w.Write(d.HasHealth ? 1u : 0u);

            w.Write(0x1FFu);  // AttributeCache.Full
            foreach (var (ranks, start, experience) in d.Attributes)
            {
                w.Write(ranks);
                w.Write(start);
                w.Write(experience);
            }

            foreach (var (ranks, start, experience, current) in d.Vitals)
            {
                w.Write(ranks);
                w.Write(start);
                w.Write(experience);
                w.Write(current);
            }

            WriteTableHeader(w, d.Skills.Count, 32);
            foreach (var skill in d.Skills)
            {
                w.Write(skill.Id);
                w.Write(skill.Ranks);
                w.Write((ushort)1);  // adjustPP
                w.Write(skill.AdvancementClass);
                w.Write(skill.Experience);
                w.Write(skill.Init);
                w.Write(0u);         // task difficulty, aka resistance_of_last_check
                w.Write(0d);         // last_time_used
            }

            if (d.Spells.Count > 0)
            {
                WriteTableHeader(w, d.Spells.Count, 64);
                foreach (var spell in d.Spells)
                {
                    w.Write(spell);
                    w.Write(2f);
                }
            }

            if (d.Enchantments is { } registry)
            {
                WriteEnchantmentRegistry(w, registry);
            }

            var optionFlags = 0x40u | 0x400u | 0x20u;  // CharacterOptions2 | SpellLists8 | SpellbookFilters
            if (d.Shortcuts.Count > 0)
            {
                optionFlags |= 0x1;
            }

            if (d.GameplayOptions is { Length: > 0 })
            {
                optionFlags |= 0x200;
            }

            if (d.FillComponents.Count > 0)
            {
                optionFlags |= 0x8;
            }

            w.Write(optionFlags);
            w.Write(d.CharacterOptions1);
            if (d.Shortcuts.Count > 0)
            {
                w.Write(d.Shortcuts.Count);
                foreach (var shortcut in d.Shortcuts)
                {
                    w.Write(shortcut.Index);
                    w.Write(shortcut.ObjectId);
                    w.Write(shortcut.SpellId);
                    w.Write(shortcut.Layer);
                }
            }

            for (var bar = 0; bar < 8; bar++)
            {
                var spells = bar < d.SpellBars.Count ? d.SpellBars[bar] : [];
                w.Write(spells.Count);
                foreach (var spell in spells)
                {
                    w.Write(spell);
                }
            }

            if (d.FillComponents.Count > 0)
            {
                WriteTableHeader(w, d.FillComponents.Count, 256);
                foreach (var (component, amount) in d.FillComponents)
                {
                    w.Write(component);
                    w.Write(amount);
                }
            }

            w.Write(d.SpellbookFilters);
            w.Write(d.CharacterOptions2);
            if (d.GameplayOptions is { Length: > 0 } options)
            {
                w.Write(options);
            }

            w.Write((uint)d.Inventory.Count);
            foreach (var (guid, containerType) in d.Inventory)
            {
                w.Write(guid);
                w.Write(containerType);
            }

            w.Write((uint)d.Equipped.Count);
            foreach (var (guid, location, priority) in d.Equipped)
            {
                w.Write(guid);
                w.Write(location);
                w.Write(priority);
            }
        });
    }

    /// <summary>0x02CD PrivateUpdatePropertyInt (<c>GameMessagePrivateUpdatePropertyInt</c>): u8 sequence, property, i32.</summary>
    public static byte[] PrivateUpdatePropertyInt(byte sequence, uint property, int value) =>
        Message(PrivateUpdatePropertyIntOpcode, w =>
        {
            w.Write(sequence);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>0x02CF PrivateUpdatePropertyInt64 (<c>GameMessagePrivateUpdatePropertyInt64</c>): u8 sequence, property, i64.</summary>
    public static byte[] PrivateUpdatePropertyInt64(byte sequence, uint property, long value) =>
        Message(PrivateUpdatePropertyInt64Opcode, w =>
        {
            w.Write(sequence);
            w.Write(property);
            w.Write(value);
        });

    /// <summary>
    /// 0x02DD PrivateUpdateSkill (<c>GameMessagePrivateUpdateSkill</c>): u8 sequence, skill, u16 ranks, u16 adjustPP
    /// (1), class, experience, init, resistance at last check, last used time.
    /// </summary>
    public static byte[] PrivateUpdateSkill(byte sequence, AceSkill skill) =>
        Message(PrivateUpdateSkillOpcode, w =>
        {
            w.Write(sequence);
            w.Write(skill.Id);
            w.Write(skill.Ranks);
            w.Write((ushort)1);
            w.Write(skill.AdvancementClass);
            w.Write(skill.Experience);
            w.Write(skill.Init);
            w.Write(skill.ResistanceAtLastCheck);
            w.Write(skill.LastUsed);
        });

    /// <summary>0x02E3 PrivateUpdateAttribute (<c>GameMessagePrivateUpdateAttribute</c>): u8 sequence, attribute, ranks, start, experience.</summary>
    public static byte[] PrivateUpdateAttribute(byte sequence, uint attribute, uint ranks, uint start, uint experience) =>
        Message(PrivateUpdateAttributeOpcode, w =>
        {
            w.Write(sequence);
            w.Write(attribute);
            w.Write(ranks);
            w.Write(start);
            w.Write(experience);
        });

    /// <summary>0x02E7 PrivateUpdateVital (<c>GameMessagePrivateUpdateVital</c>): u8 sequence, vital (1, 3, 5), ranks, start, experience, current.</summary>
    public static byte[] PrivateUpdateVital(byte sequence, uint vital, uint ranks, uint start, uint experience, uint current) =>
        Message(PrivateUpdateVitalOpcode, w =>
        {
            w.Write(sequence);
            w.Write(vital);
            w.Write(ranks);
            w.Write(start);
            w.Write(experience);
            w.Write(current);
        });

    /// <summary>0x02E9 PrivateUpdateAttribute2ndLevel (<c>GameMessagePrivateUpdateAttribute2ndLevel</c>): u8 sequence, vital (2, 4, 6), current.</summary>
    public static byte[] PrivateUpdateAttribute2ndLevel(byte sequence, uint vital, uint current) =>
        Message(PrivateUpdateAttribute2ndLevelOpcode, w =>
        {
            w.Write(sequence);
            w.Write(vital);
            w.Write(current);
        });

    /// <summary>The whole message: the opcode, then what <paramref name="body"/> writes.</summary>
    public static byte[] Message(uint opcode, Action<BinaryWriter> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(opcode);
            body(writer);
        }

        return stream.ToArray();
    }

    private static byte[] CreateObjectBody(uint opcode, AceCreateObject desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        return Message(opcode, w =>
        {
            w.Write(desc.Guid);
            WriteModelData(w, desc.BasePalette, desc.SubPalettes, desc.TextureChanges, desc.AnimPartChanges);
            WritePhysicsDesc(w, desc);
            WriteWeenieDesc(w, desc);
        });
    }

    /// <summary><c>SerializeModelData</c>: 0x11, the three counts, the base palette when there are subpalettes, the lists, Align.</summary>
    private static void WriteModelData(
        BinaryWriter w,
        uint basePalette,
        IReadOnlyList<(uint Palette, byte Offset, byte Length)> subPalettes,
        IReadOnlyList<(byte Part, uint OldTexture, uint NewTexture)> textureChanges,
        IReadOnlyList<(byte Index, uint GfxObj)> animPartChanges)
    {
        w.Write((byte)0x11);
        w.Write((byte)subPalettes.Count);
        w.Write((byte)textureChanges.Count);
        w.Write((byte)animPartChanges.Count);
        if (subPalettes.Count > 0)
        {
            WritePackedDwordOfKnownType(w, basePalette, 0x04000000);
        }

        foreach (var (palette, offset, length) in subPalettes)
        {
            WritePackedDwordOfKnownType(w, palette, 0x04000000);
            w.Write(offset);
            w.Write(length);
        }

        foreach (var (part, oldTexture, newTexture) in textureChanges)
        {
            w.Write(part);
            WritePackedDwordOfKnownType(w, oldTexture, 0x05000000);
            WritePackedDwordOfKnownType(w, newTexture, 0x05000000);
        }

        foreach (var (index, gfxObj) in animPartChanges)
        {
            w.Write(index);
            WritePackedDwordOfKnownType(w, gfxObj, 0x01000000);
        }

        Align(w);
    }

    /// <summary><c>SerializePhysicsData</c>: the flags, the state, the set fields in ACE's order, the nine sequences, Align.</summary>
    private static void WritePhysicsDesc(BinaryWriter w, AceCreateObject desc)
    {
        uint flags = 0;
        if (desc.Movement is not null) flags |= 0x10000;
        else if (desc.AnimationFrame is not null) flags |= 0x20000;
        if (desc.Position is not null) flags |= 0x8000;
        if (desc.MotionTable is not null) flags |= 0x2;
        if (desc.SoundTable is not null) flags |= 0x800;
        if (desc.PhysicsEffectTable is not null) flags |= 0x1000;
        if (desc.Setup is not null) flags |= 0x1;
        if (desc.Parent is not null) flags |= 0x20;
        if (desc.Children is not null) flags |= 0x40;
        if (desc.ObjScale is not null) flags |= 0x80;
        if (desc.Friction is not null) flags |= 0x100;
        if (desc.Elasticity is not null) flags |= 0x200;
        if (desc.Translucency is not null) flags |= 0x40000;
        if (desc.Velocity is not null) flags |= 0x4;
        if (desc.Acceleration is not null) flags |= 0x8;
        if (desc.Omega is not null) flags |= 0x10;
        if (desc.DefaultScript is not null) flags |= 0x2000;
        if (desc.DefaultScriptIntensity is not null) flags |= 0x4000;
        w.Write(flags);
        w.Write(desc.PhysicsState);
        if (desc.Movement is { } movement)
        {
            // ACE writes a placeholder length, the header-less MovementData, then patches the length and adds the
            // is-autonomous word.
            var lengthAt = w.BaseStream.Position;
            w.Write(0u);
            WriteMovementData(w, movement);
            var end = w.BaseStream.Position;
            w.BaseStream.Position = lengthAt;
            w.Write((uint)(end - lengthAt - 4));
            w.BaseStream.Position = end;
            w.Write(desc.MovementIsAutonomous ? 1u : 0u);
        }
        else if (desc.AnimationFrame is { } frame)
        {
            w.Write(frame);
        }

        if (desc.Position is { } p)
        {
            w.Write(p.Cell);
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.Z);
            w.Write(p.Qw);
            w.Write(p.Qx);
            w.Write(p.Qy);
            w.Write(p.Qz);
        }

        if (desc.MotionTable is { } motionTable) w.Write(motionTable);
        if (desc.SoundTable is { } soundTable) w.Write(soundTable);
        if (desc.PhysicsEffectTable is { } effectTable) w.Write(effectTable);
        if (desc.Setup is { } setup) w.Write(setup);
        if (desc.Parent is { } parent)
        {
            w.Write(parent.Guid);
            w.Write(parent.Location);
        }

        if (desc.Children is { } children)
        {
            w.Write(children.Count);
            foreach (var (guid, location) in children)
            {
                w.Write(guid);
                w.Write(location);
            }
        }

        if (desc.ObjScale is { } scale) w.Write(scale);
        if (desc.Friction is { } friction) w.Write(friction);
        if (desc.Elasticity is { } elasticity) w.Write(elasticity);
        if (desc.Translucency is { } translucency) w.Write(translucency);
        if (desc.Velocity is { } velocity) WriteVector(w, velocity);
        if (desc.Acceleration is { } acceleration) WriteVector(w, acceleration);
        if (desc.Omega is { } omega) WriteVector(w, omega);
        if (desc.DefaultScript is { } script) w.Write(script);
        if (desc.DefaultScriptIntensity is { } intensity) w.Write(intensity);
        foreach (var sequence in desc.Sequences)
        {
            w.Write(sequence);
        }

        Align(w);
    }

    /// <summary>
    /// The weenie description: the header and its fields, Align, the second header when one of its fields is set
    /// (ACE's <c>UpdateObjectDescriptionFlags</c> sets ObjectDescriptionFlags 0x04000000 exactly then), the set fields
    /// in ACE's write order, Align.
    /// </summary>
    private static void WriteWeenieDesc(BinaryWriter w, AceCreateObject d)
    {
        uint h = 0;
        if (d.PluralName is not null) h |= 0x1;
        if (d.ItemsCapacity is not null) h |= 0x2;
        if (d.ContainersCapacity is not null) h |= 0x4;
        if (d.Value is not null) h |= 0x8;
        if (d.Usable is not null) h |= 0x10;
        if (d.UseRadius is not null) h |= 0x20;
        if (d.Monarch is not null) h |= 0x40;
        if (d.UiEffects is not null) h |= 0x80;
        if (d.AmmoType is not null) h |= 0x100;
        if (d.CombatUse is not null) h |= 0x200;
        if (d.Structure is not null) h |= 0x400;
        if (d.MaxStructure is not null) h |= 0x800;
        if (d.StackSize is not null) h |= 0x1000;
        if (d.MaxStackSize is not null) h |= 0x2000;
        if (d.Container is not null) h |= 0x4000;
        if (d.Wielder is not null) h |= 0x8000;
        if (d.ValidLocations is not null) h |= 0x10000;
        if (d.CurrentlyWieldedLocation is not null) h |= 0x20000;
        if (d.Priority is not null) h |= 0x40000;
        if (d.TargetType is not null) h |= 0x80000;
        if (d.RadarBlipColor is not null) h |= 0x100000;
        if (d.Burden is not null) h |= 0x200000;
        if (d.Spell is not null) h |= 0x400000;
        if (d.RadarBehavior is not null) h |= 0x800000;
        if (d.Workmanship is not null) h |= 0x1000000;
        if (d.HouseOwner is not null) h |= 0x2000000;
        if (d.HouseRestrictions is not null) h |= 0x4000000;
        if (d.PScript is not null) h |= 0x8000000;
        if (d.HookType is not null) h |= 0x10000000;
        if (d.HookItemTypes is not null) h |= 0x20000000;
        if (d.IconOverlay is not null) h |= 0x40000000;
        if (d.MaterialType is not null) h |= 0x80000000;
        uint h2 = 0;
        if (d.IconUnderlay is not null) h2 |= 0x1;
        if (d.CooldownId is not null) h2 |= 0x2;
        if (d.CooldownDuration is not null) h2 |= 0x4;
        if (d.PetOwner is not null) h2 |= 0x8;
        var descriptionFlags = (d.ObjectDescriptionFlags & ~0x04000000u) | (h2 != 0 ? 0x04000000u : 0u);

        w.Write(h);
        WriteString16L(w, d.Name);
        WritePackedDword(w, d.WeenieClassId);
        WritePackedDwordOfKnownType(w, d.Icon, 0x06000000);
        w.Write(d.ItemType);
        w.Write(descriptionFlags);
        Align(w);
        if (h2 != 0) w.Write(h2);
        if (d.PluralName is { } plural) WriteString16L(w, plural);
        if (d.ItemsCapacity is { } items) w.Write(items);
        if (d.ContainersCapacity is { } containers) w.Write(containers);
        if (d.AmmoType is { } ammo) w.Write(ammo);
        if (d.Value is { } value) w.Write(value);
        if (d.Usable is { } usable) w.Write(usable);
        if (d.UseRadius is { } radius) w.Write(radius);
        if (d.TargetType is { } target) w.Write(target);
        if (d.UiEffects is { } effects) w.Write(effects);
        if (d.CombatUse is { } combatUse) w.Write(combatUse);
        if (d.Structure is { } structure) w.Write(structure);
        if (d.MaxStructure is { } maxStructure) w.Write(maxStructure);
        if (d.StackSize is { } stack) w.Write(stack);
        if (d.MaxStackSize is { } maxStack) w.Write(maxStack);
        if (d.Container is { } container) w.Write(container);
        if (d.Wielder is { } wielder) w.Write(wielder);
        if (d.ValidLocations is { } valid) w.Write(valid);
        if (d.CurrentlyWieldedLocation is { } wielded) w.Write(wielded);
        if (d.Priority is { } priority) w.Write(priority);
        if (d.RadarBlipColor is { } blip) w.Write(blip);
        if (d.RadarBehavior is { } behavior) w.Write(behavior);
        if (d.PScript is { } pscript) w.Write(pscript);
        if (d.Workmanship is { } workmanship) w.Write(workmanship);
        if (d.Burden is { } burden) w.Write(burden);
        if (d.Spell is { } spell) w.Write(spell);
        if (d.HouseOwner is { } owner) w.Write(owner);
        if (d.HouseRestrictions is { } restrictions)
        {
            // RestrictionDB: version, open status, monarch, then the PackableHashTable header (u16 count, u16 buckets).
            w.Write(restrictions.Version);
            w.Write(restrictions.OpenStatus);
            w.Write(restrictions.Monarch);
            w.Write((ushort)restrictions.Guests.Count);
            w.Write(restrictions.Buckets);
            foreach (var (guid, access) in restrictions.Guests)
            {
                w.Write(guid);
                w.Write(access);
            }
        }

        if (d.HookItemTypes is { } hookItemTypes) w.Write(hookItemTypes);
        if (d.Monarch is { } monarch) w.Write(monarch);
        if (d.HookType is { } hookType) w.Write(hookType);
        if (d.IconOverlay is { } overlay) WritePackedDwordOfKnownType(w, overlay, 0x06000000);
        if (d.IconUnderlay is { } underlay) WritePackedDwordOfKnownType(w, underlay, 0x06000000);
        if (d.MaterialType is { } material) w.Write(material);
        if (d.CooldownId is { } cooldown) w.Write(cooldown);
        if (d.CooldownDuration is { } duration) w.Write(duration);
        if (d.PetOwner is { } pet) w.Write(pet);
        Align(w);
    }

    private static byte[] Emote(uint opcode, uint senderGuid, string senderName, string text) =>
        Message(opcode, w =>
        {
            w.Write(senderGuid);
            WriteString16L(w, senderName);
            WriteString16L(w, text);
        });

    /// <summary>An F7B0 GameEvent whose payload <paramref name="payload"/> writes into the message stream.</summary>
    private static byte[] Event(uint playerGuid, uint sequence, uint eventType, Action<BinaryWriter> payload) =>
        Message(GameEventOpcode, w =>
        {
            w.Write(playerGuid);
            w.Write(sequence);
            w.Write(eventType);
            payload(w);
        });

    private static void WriteVector(BinaryWriter w, (float X, float Y, float Z) v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    /// <summary>ACE's <c>PackableHashTable.WriteHeader</c>: u16 count, u16 bucket count.</summary>
    private static void WriteTableHeader(BinaryWriter w, int count, int buckets)
    {
        w.Write((ushort)count);
        w.Write((ushort)buckets);
    }

    /// <summary>ACE's <c>Position.Serialize</c> with the quaternion: cell, origin, then W, X, Y, Z.</summary>
    private static void WritePosition(BinaryWriter w, AcePosition p)
    {
        w.Write(p.Cell);
        w.Write(p.X);
        w.Write(p.Y);
        w.Write(p.Z);
        w.Write(p.Qw);
        w.Write(p.Qx);
        w.Write(p.Qy);
        w.Write(p.Qz);
    }

    /// <summary>
    /// ACE's <c>EnchantmentRegistryExtensions.Write</c>: the mask of the non-empty lists, then multiplicative, additive and
    /// cooldown (each a u32 count and the enchantments), then the single vitae.
    /// </summary>
    private static void WriteEnchantmentRegistry(BinaryWriter w, AceEnchantmentRegistry registry)
    {
        uint mask = 0;
        if (registry.Multiplicative.Count > 0)
        {
            mask |= 0x1;
        }

        if (registry.Additive.Count > 0)
        {
            mask |= 0x2;
        }

        if (registry.Vitae is not null)
        {
            mask |= 0x4;
        }

        if (registry.Cooldown.Count > 0)
        {
            mask |= 0x8;
        }

        w.Write(mask);
        foreach (var list in new[] { registry.Multiplicative, registry.Additive, registry.Cooldown })
        {
            if (list.Count == 0)
            {
                continue;
            }

            w.Write(list.Count);
            foreach (var enchantment in list)
            {
                WriteEnchantment(w, enchantment);
            }
        }

        if (registry.Vitae is { } vitae)
        {
            WriteEnchantment(w, vitae);
        }
    }

    /// <summary>ACE's <c>EnchantmentExtensions.Write</c>: 60 bytes, then the spell set id when HasSpellSetId is not 0.</summary>
    private static void WriteEnchantment(BinaryWriter w, AceEnchantment e)
    {
        w.Write(e.SpellId);
        w.Write(e.Layer);
        w.Write(e.SpellCategory);
        w.Write(e.HasSpellSetId);
        w.Write(e.PowerLevel);
        w.Write(e.StartTime);
        w.Write(e.Duration);
        w.Write(e.CasterGuid);
        w.Write(e.DegradeModifier);
        w.Write(e.DegradeLimit);
        w.Write(e.LastTimeDegraded);
        w.Write(e.StatModType);
        w.Write(e.StatModKey);
        w.Write(e.StatModValue);
        if (e.HasSpellSetId != 0)
        {
            w.Write(e.SpellSetId);
        }
    }

    private static int PadTo4(int length) => (4 - (length & 3)) & 3;
}

/// <summary>One character of <see cref="AceWireWriters.CharacterList"/>.</summary>
/// <param name="Guid">The GUID.</param>
/// <param name="Name">The name as ACE sends it ('+' first for an admin character).</param>
/// <param name="DeleteField">The raw deletion word: 0 for an active character, 1 while ACE has a deletion pending.</param>
public readonly record struct AceCharacter(uint Guid, string Name, uint DeleteField = 0);

/// <summary>One missing iteration of <see cref="AceWireWriters.DddBegin"/>.</summary>
/// <param name="DatType">0 portal; 1 cell or language; <see cref="AceWireWriters.HighResDatType"/> high-res.</param>
/// <param name="DatId">1 portal or high-res; 2 cell; 3 language.</param>
/// <param name="Iteration">The iteration.</param>
/// <param name="Download">The file ids to download.</param>
/// <param name="Purge">The file ids to purge.</param>
public sealed record AceMissingIteration(int DatType, int DatId, int Iteration, IReadOnlyList<uint> Download, IReadOnlyList<uint> Purge);

/// <summary>A position as ACE serializes it: the cell, the origin within its landblock and the quaternion (W, X, Y, Z).</summary>
/// <param name="Cell">The cell id.</param>
/// <param name="X">Origin x.</param>
/// <param name="Y">Origin y.</param>
/// <param name="Z">Origin z.</param>
/// <param name="Qw">Rotation w.</param>
/// <param name="Qx">Rotation x.</param>
/// <param name="Qy">Rotation y.</param>
/// <param name="Qz">Rotation z.</param>
public readonly record struct AcePosition(uint Cell, float X, float Y, float Z, float Qw = 1f, float Qx = 0f, float Qy = 0f, float Qz = 0f);

/// <summary>The four sequences an UpdatePosition carries.</summary>
/// <param name="Instance">Instance sequence.</param>
/// <param name="Position">Position sequence (ACE advances it for each update).</param>
/// <param name="Teleport">Teleport sequence.</param>
/// <param name="ForcePosition">Force-position sequence.</param>
public readonly record struct AceUpdatePositionSequences(ushort Instance, ushort Position, ushort Teleport, ushort ForcePosition);

/// <summary>
/// The fields of <see cref="AceWireWriters.ObjectCreate"/>. Unset optional physics and weenie fields are not flagged; the
/// writer computes the flags from what is set, as ACE's <c>CalculatedPhysicsDescriptionFlag</c> and
/// <c>CalculateWeenieHeaderFlag</c> do.
/// </summary>
public sealed record AceCreateObject
{
    /// <summary>The object's GUID.</summary>
    public required uint Guid { get; init; }

    /// <summary>The physics state (a newly logged-in player is Hidden | IgnoreCollisions | Gravity | EdgeSlide).</summary>
    public uint PhysicsState { get; init; }

    /// <summary>The movement (PhysicsDesc flag 0x10000): written length-prefixed, header-less, then the is-autonomous word.</summary>
    public AceMovementData? Movement { get; init; }

    /// <summary>The is-autonomous word after <see cref="Movement"/>.</summary>
    public bool MovementIsAutonomous { get; init; }

    /// <summary>The placement (AnimationFrame, flag 0x20000), written only without <see cref="Movement"/>.</summary>
    public uint? AnimationFrame { get; init; }

    /// <summary>The position (PhysicsDesc flag 0x8000), always with all four quaternion components.</summary>
    public AcePosition? Position { get; init; }

    /// <summary>The motion table (0x2).</summary>
    public uint? MotionTable { get; init; }

    /// <summary>The sound table (0x800).</summary>
    public uint? SoundTable { get; init; }

    /// <summary>The physics-effect table (0x1000).</summary>
    public uint? PhysicsEffectTable { get; init; }

    /// <summary>The setup (0x1).</summary>
    public uint? Setup { get; init; }

    /// <summary>The parent and the location on it (0x20).</summary>
    public (uint Guid, uint Location)? Parent { get; init; }

    /// <summary>The children and their locations (0x40).</summary>
    public IReadOnlyList<(uint Guid, uint Location)>? Children { get; init; }

    /// <summary>The scale (0x80).</summary>
    public float? ObjScale { get; init; }

    /// <summary>The friction (0x100).</summary>
    public float? Friction { get; init; }

    /// <summary>The elasticity (0x200).</summary>
    public float? Elasticity { get; init; }

    /// <summary>The translucency (0x40000).</summary>
    public float? Translucency { get; init; }

    /// <summary>The velocity (0x4).</summary>
    public (float X, float Y, float Z)? Velocity { get; init; }

    /// <summary>The acceleration (0x8).</summary>
    public (float X, float Y, float Z)? Acceleration { get; init; }

    /// <summary>The angular velocity (0x10).</summary>
    public (float X, float Y, float Z)? Omega { get; init; }

    /// <summary>The default script (0x2000).</summary>
    public uint? DefaultScript { get; init; }

    /// <summary>The default script's intensity (0x4000).</summary>
    public float? DefaultScriptIntensity { get; init; }

    /// <summary>The nine sequences: position, movement, state, vector, teleport, server control, force position, visual
    /// description, instance.</summary>
    public IReadOnlyList<ushort> Sequences { get; init; } = new ushort[9];

    /// <summary>The base palette, written only when there are subpalettes.</summary>
    public uint BasePalette { get; init; }

    /// <summary>The subpalettes: palette, offset, length.</summary>
    public IReadOnlyList<(uint Palette, byte Offset, byte Length)> SubPalettes { get; init; } = [];

    /// <summary>The texture changes: part index, old texture, new texture.</summary>
    public IReadOnlyList<(byte Part, uint OldTexture, uint NewTexture)> TextureChanges { get; init; } = [];

    /// <summary>The part changes: part index, GfxObj.</summary>
    public IReadOnlyList<(byte Index, uint GfxObj)> AnimPartChanges { get; init; } = [];

    /// <summary>The name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The weenie class id.</summary>
    public uint WeenieClassId { get; init; }

    /// <summary>The icon (0x06xxxxxx).</summary>
    public uint Icon { get; init; }

    /// <summary>The item type.</summary>
    public uint ItemType { get; init; }

    /// <summary>The object description flags. Bit 0x04000000 (a second header follows) is set by the writer exactly when
    /// one of the second header's fields is set, as ACE does; the value given here for that bit is ignored.</summary>
    public uint ObjectDescriptionFlags { get; init; }

    /// <summary>PluralName (header 0x1).</summary>
    public string? PluralName { get; init; }

    /// <summary>ItemsCapacity (0x2), u8.</summary>
    public byte? ItemsCapacity { get; init; }

    /// <summary>ContainersCapacity (0x4), u8.</summary>
    public byte? ContainersCapacity { get; init; }

    /// <summary>AmmoType (0x100), u16.</summary>
    public ushort? AmmoType { get; init; }

    /// <summary>Value (0x8), i32.</summary>
    public int? Value { get; init; }

    /// <summary>Usable (0x10), u32.</summary>
    public uint? Usable { get; init; }

    /// <summary>UseRadius (0x20), f32.</summary>
    public float? UseRadius { get; init; }

    /// <summary>TargetType (0x80000), u32.</summary>
    public uint? TargetType { get; init; }

    /// <summary>UiEffects (0x80), u32.</summary>
    public uint? UiEffects { get; init; }

    /// <summary>CombatUse (0x200), i8.</summary>
    public sbyte? CombatUse { get; init; }

    /// <summary>Structure (0x400), u16.</summary>
    public ushort? Structure { get; init; }

    /// <summary>MaxStructure (0x800), u16.</summary>
    public ushort? MaxStructure { get; init; }

    /// <summary>StackSize (0x1000), u16.</summary>
    public ushort? StackSize { get; init; }

    /// <summary>MaxStackSize (0x2000), u16.</summary>
    public ushort? MaxStackSize { get; init; }

    /// <summary>Container (0x4000), u32.</summary>
    public uint? Container { get; init; }

    /// <summary>Wielder (0x8000), u32.</summary>
    public uint? Wielder { get; init; }

    /// <summary>ValidLocations (0x10000), u32.</summary>
    public uint? ValidLocations { get; init; }

    /// <summary>CurrentlyWieldedLocation (0x20000), u32.</summary>
    public uint? CurrentlyWieldedLocation { get; init; }

    /// <summary>Priority (0x40000), u32.</summary>
    public uint? Priority { get; init; }

    /// <summary>RadarBlipColor (0x100000), u8.</summary>
    public byte? RadarBlipColor { get; init; }

    /// <summary>RadarBehavior (0x800000), u8.</summary>
    public byte? RadarBehavior { get; init; }

    /// <summary>PScript (0x8000000), u16.</summary>
    public ushort? PScript { get; init; }

    /// <summary>Workmanship (0x1000000), f32.</summary>
    public float? Workmanship { get; init; }

    /// <summary>Burden (0x200000), u16.</summary>
    public ushort? Burden { get; init; }

    /// <summary>Spell (0x400000), u16.</summary>
    public ushort? Spell { get; init; }

    /// <summary>HouseOwner (0x2000000), u32.</summary>
    public uint? HouseOwner { get; init; }

    /// <summary>HouseRestrictions (0x4000000), a RestrictionDB.</summary>
    public AceRestrictionDb? HouseRestrictions { get; init; }

    /// <summary>HookItemTypes (0x20000000), u32.</summary>
    public uint? HookItemTypes { get; init; }

    /// <summary>Monarch (0x40), u32.</summary>
    public uint? Monarch { get; init; }

    /// <summary>HookType (0x10000000), u16.</summary>
    public ushort? HookType { get; init; }

    /// <summary>IconOverlay (0x40000000), a known-type 0x06xxxxxx id.</summary>
    public uint? IconOverlay { get; init; }

    /// <summary>IconUnderlay (second header 0x1), a known-type 0x06xxxxxx id.</summary>
    public uint? IconUnderlay { get; init; }

    /// <summary>MaterialType (0x80000000), u32.</summary>
    public uint? MaterialType { get; init; }

    /// <summary>Cooldown (second header 0x2), i32.</summary>
    public int? CooldownId { get; init; }

    /// <summary>CooldownDuration (second header 0x4), f64.</summary>
    public double? CooldownDuration { get; init; }

    /// <summary>PetOwner (second header 0x8), u32.</summary>
    public uint? PetOwner { get; init; }
}

/// <summary>A ModelData block for <see cref="AceWireWriters.ObjDescEvent"/>.</summary>
/// <param name="BasePalette">The base palette, written only when there are subpalettes.</param>
/// <param name="SubPalettes">The subpalettes: palette, offset, length.</param>
/// <param name="TextureChanges">The texture changes: part index, old texture, new texture.</param>
/// <param name="AnimPartChanges">The part changes: part index, GfxObj.</param>
public sealed record AceModelData(
    uint BasePalette,
    IReadOnlyList<(uint Palette, byte Offset, byte Length)> SubPalettes,
    IReadOnlyList<(byte Part, uint OldTexture, uint NewTexture)> TextureChanges,
    IReadOnlyList<(byte Index, uint GfxObj)> AnimPartChanges);

/// <summary>
/// A MovementData body as ACE writes it (<c>Motion/MovementData.cs</c> and the five body writers). For type 0 a field is
/// flagged and written exactly when it is set; motion commands are low words.
/// </summary>
public sealed record AceMovementData
{
    /// <summary>0 (interpreted), 6 MoveToObject, 7 MoveToPosition, 8 TurnToObject, 9 TurnToHeading.</summary>
    public byte MovementType { get; init; }

    /// <summary>0x1 StickToObject (a sticky GUID follows the interpreted state), 0x2 StandingLongJump.</summary>
    public byte MotionFlags { get; init; }

    /// <summary>The stance's low word (NonCombat 0x003D by default).</summary>
    public ushort Stance { get; init; } = 0x003D;

    /// <summary>Type 0: the style (flag 0x1).</summary>
    public ushort? Style { get; init; }

    /// <summary>Type 0: the forward command (flag 0x2).</summary>
    public ushort? Forward { get; init; }

    /// <summary>Type 0: the sidestep command (flag 0x8).</summary>
    public ushort? Sidestep { get; init; }

    /// <summary>Type 0: the turn command (flag 0x20).</summary>
    public ushort? Turn { get; init; }

    /// <summary>Type 0: the forward speed (flag 0x4).</summary>
    public float? ForwardSpeed { get; init; }

    /// <summary>Type 0: the sidestep speed (flag 0x10).</summary>
    public float? SidestepSpeed { get; init; }

    /// <summary>Type 0: the turn speed (flag 0x40).</summary>
    public float? TurnSpeed { get; init; }

    /// <summary>Type 0: the queued commands.</summary>
    public IReadOnlyList<AceMotionItem> Commands { get; init; } = [];

    /// <summary>Type 0 with StickToObject: the object to stick to.</summary>
    public uint StickyGuid { get; init; }

    /// <summary>Types 6 and 8: the target object.</summary>
    public uint TargetGuid { get; init; }

    /// <summary>Types 6 and 7: the origin's cell.</summary>
    public uint OriginCell { get; init; }

    /// <summary>Types 6 and 7: the origin's x.</summary>
    public float OriginX { get; init; }

    /// <summary>Types 6 and 7: the origin's y.</summary>
    public float OriginY { get; init; }

    /// <summary>Types 6 and 7: the origin's z.</summary>
    public float OriginZ { get; init; }

    /// <summary>Types 6 to 9: the MovementParams bits.</summary>
    public uint ParameterFlags { get; init; }

    /// <summary>Types 6 and 7: the distance to the object.</summary>
    public float DistanceToObject { get; init; }

    /// <summary>Types 6 and 7: the minimum distance.</summary>
    public float MinDistance { get; init; }

    /// <summary>Types 6 and 7: the fail distance.</summary>
    public float FailDistance { get; init; }

    /// <summary>Types 6 to 9: the speed.</summary>
    public float Speed { get; init; } = 1f;

    /// <summary>Types 6 and 7: the walk/run threshold.</summary>
    public float WalkRunThreshold { get; init; }

    /// <summary>Types 6 to 9: the desired heading.</summary>
    public float DesiredHeading { get; init; }

    /// <summary>Types 6 and 7: the run rate.</summary>
    public float RunRate { get; init; } = 1f;

    /// <summary>Type 8: the heading of the object to face.</summary>
    public float ObjectHeading { get; init; }
}

/// <summary>One command of an <see cref="AceMovementData"/> (ACE <c>MotionItem</c>).</summary>
/// <param name="Command">The command's low word.</param>
/// <param name="Stamp">The 15-bit sequence.</param>
/// <param name="Autonomous">True for a client-initiated motion (bit 15).</param>
/// <param name="Speed">The speed.</param>
public readonly record struct AceMotionItem(ushort Command, ushort Stamp, bool Autonomous, float Speed = 1f);

/// <summary>A house's RestrictionDB as ACE writes it (<c>Structure/RestrictionDB.cs</c>).</summary>
/// <param name="Version">The version word.</param>
/// <param name="OpenStatus">The open status.</param>
/// <param name="Monarch">The monarch, or 0.</param>
/// <param name="Guests">The guests and their access words, in the order to write them.</param>
/// <param name="Buckets">The bucket count in the hash table header (ACE writes 768).</param>
public sealed record AceRestrictionDb(uint Version, uint OpenStatus, uint Monarch, IReadOnlyList<(uint Guid, uint Access)> Guests, ushort Buckets = 768);

/// <summary>
/// The fields of <see cref="AceWireWriters.PlayerDescription"/>, as ACE takes them from the player: empty tables are not
/// flagged, the spells and the enchantment registry are flagged only when present, and the option flags follow what is
/// set, as <c>GameEventPlayerDescription</c> computes them.
/// </summary>
public sealed record AcePlayerDescription
{
    /// <summary>The weenie type (ACE writes the player's; 10, Creature, here).</summary>
    public uint WeenieType { get; init; } = 10;

    /// <summary>PropertyInt values, written in this order.</summary>
    public IReadOnlyList<(uint Key, int Value)> Ints { get; init; } = [];

    /// <summary>PropertyInt64 values.</summary>
    public IReadOnlyList<(uint Key, long Value)> Int64s { get; init; } = [];

    /// <summary>PropertyBool values.</summary>
    public IReadOnlyList<(uint Key, bool Value)> Bools { get; init; } = [];

    /// <summary>PropertyFloat values.</summary>
    public IReadOnlyList<(uint Key, double Value)> Doubles { get; init; } = [];

    /// <summary>PropertyString values.</summary>
    public IReadOnlyList<(uint Key, string Value)> Strings { get; init; } = [];

    /// <summary>PropertyDataId values.</summary>
    public IReadOnlyList<(uint Key, uint Value)> DataIds { get; init; } = [];

    /// <summary>PropertyInstanceId values.</summary>
    public IReadOnlyList<(uint Key, uint Value)> InstanceIds { get; init; } = [];

    /// <summary>The LastOutsideDeath position, if the player has one.</summary>
    public AcePosition? LastOutsideDeath { get; init; }

    /// <summary>The has-health word (a player always has health).</summary>
    public bool HasHealth { get; init; } = true;

    /// <summary>The six primary attributes, Strength to Self (exactly six: ACE writes the full cache).</summary>
    public IReadOnlyList<(uint Ranks, uint Start, uint Experience)> Attributes { get; init; } =
        [(0, 100, 0), (0, 10, 0), (0, 100, 0), (0, 10, 0), (0, 100, 0), (0, 10, 0)];

    /// <summary>The three vitals, health, stamina and mana (exactly three).</summary>
    public IReadOnlyList<(uint Ranks, uint Start, uint Experience, uint Current)> Vitals { get; init; } =
        [(0, 0, 0, 5), (0, 0, 0, 10), (0, 0, 0, 10)];

    /// <summary>The skills.</summary>
    public IReadOnlyList<AceSkill> Skills { get; init; } = [];

    /// <summary>The known spells.</summary>
    public IReadOnlyList<int> Spells { get; init; } = [];

    /// <summary>The enchantment registry; null when the player has no enchantments.</summary>
    public AceEnchantmentRegistry? Enchantments { get; init; }

    /// <summary>The character options word.</summary>
    public uint CharacterOptions1 { get; init; } = 0x50C4A54A;

    /// <summary>The second character options word.</summary>
    public uint CharacterOptions2 { get; init; } = 0x00948700;

    /// <summary>The spellbook filters.</summary>
    public uint SpellbookFilters { get; init; } = 0x3FFF;

    /// <summary>The shortcut bar.</summary>
    public IReadOnlyList<AceShortcut> Shortcuts { get; init; } = [];

    /// <summary>The spell bars, up to eight (missing ones are written empty).</summary>
    public IReadOnlyList<IReadOnlyList<uint>> SpellBars { get; init; } = [];

    /// <summary>The desired components.</summary>
    public IReadOnlyList<(uint Component, int Amount)> FillComponents { get; init; } = [];

    /// <summary>The gameplay options bytes the client last sent; null or empty when there are none.</summary>
    public byte[]? GameplayOptions { get; init; }

    /// <summary>The main pack's contents.</summary>
    public IReadOnlyList<(uint Guid, uint ContainerType)> Inventory { get; init; } = [];

    /// <summary>The equipped objects.</summary>
    public IReadOnlyList<(uint Guid, uint Location, uint Priority)> Equipped { get; init; } = [];

    /// <summary>
    /// A newly created character as ACE describes it on its first entry into the world: a few properties, the attributes
    /// and vitals, 49 skills (every third one trained), no spells, no enchantments, eight empty spell bars and an empty
    /// pack. The values are plausible, not ACE's exact starting tables; about 2 KB, so it travels in several fragments.
    /// </summary>
    public static AcePlayerDescription NewCharacter(string name) => new()
    {
        Ints = [(0x19, 1), (0x05, 1234), (0x14, 0), (0x18, 0), (0xB5, 330), (0x8F, 2)],
        Int64s = [(0x01, 0), (0x02, 0)],
        Bools = [(0x34, true), (0x41, false)],
        Doubles = [(0x89, 0d)],
        Strings = [(0x01, name), (0x23, "Holtburg")],
        DataIds = [(0x01, 0x02000001), (0x03, 0x20000001), (0x04, 0x30000000), (0x22, 0x0E000002)],
        Attributes = [(0, 100, 0), (0, 60, 0), (0, 50, 0), (0, 40, 0), (0, 40, 0), (0, 40, 0)],
        Vitals = [(0, 0, 0, 30), (0, 0, 0, 60), (0, 0, 0, 40)],
        Skills = [.. Enumerable.Range(1, 54).Where(id => id is not (17 or 25 or 26 or 42 or 53))
            .Select(id => new AceSkill((uint)id, 0, id % 3 == 0 ? 2u : 1u, 0, id % 3 == 0 ? 10u : 0u))],
    };
}

/// <summary>A skill as <see cref="AceWireWriters.PlayerDescription"/> and <see cref="AceWireWriters.PrivateUpdateSkill"/> write it.</summary>
/// <param name="Id">The skill id.</param>
/// <param name="Ranks">Ranks raised.</param>
/// <param name="AdvancementClass">1 untrained, 2 trained, 3 specialised (ACE <c>SkillAdvancementClass</c>).</param>
/// <param name="Experience">Experience spent.</param>
/// <param name="Init">The starting level.</param>
/// <param name="ResistanceAtLastCheck">Written by PrivateUpdateSkill only; the description writes 0.</param>
/// <param name="LastUsed">Written by PrivateUpdateSkill only; the description writes 0.</param>
public readonly record struct AceSkill(uint Id, ushort Ranks, uint AdvancementClass, uint Experience, uint Init, uint ResistanceAtLastCheck = 0, double LastUsed = 0);

/// <summary>A shortcut bar entry: index, object, then the layered spell (u16 spell, u16 layer).</summary>
/// <param name="Index">The slot.</param>
/// <param name="ObjectId">The object.</param>
/// <param name="SpellId">The spell.</param>
/// <param name="Layer">The spell's layer.</param>
public readonly record struct AceShortcut(uint Index, uint ObjectId, ushort SpellId = 0, ushort Layer = 0);

/// <summary>The enchantment registry of <see cref="AceWireWriters.PlayerDescription"/>.</summary>
/// <param name="Multiplicative">The multiplicative list.</param>
/// <param name="Additive">The additive list.</param>
/// <param name="Cooldown">The cooldown list.</param>
/// <param name="Vitae">The vitae, if any.</param>
public sealed record AceEnchantmentRegistry(
    IReadOnlyList<AceEnchantment> Multiplicative,
    IReadOnlyList<AceEnchantment> Additive,
    IReadOnlyList<AceEnchantment> Cooldown,
    AceEnchantment? Vitae = null);

/// <summary>An enchantment as ACE's <c>Network/Structure/Enchantment.cs</c> writes it.</summary>
/// <param name="SpellId">The spell.</param>
/// <param name="Layer">The layer.</param>
/// <param name="SpellCategory">The category.</param>
/// <param name="HasSpellSetId">ACE's flag word, 1 by default; the spell set id follows when it is not 0.</param>
/// <param name="PowerLevel">The power level.</param>
/// <param name="StartTime">The start time.</param>
/// <param name="Duration">The duration.</param>
/// <param name="CasterGuid">The caster.</param>
/// <param name="DegradeModifier">The degrade modifier.</param>
/// <param name="DegradeLimit">The degrade limit.</param>
/// <param name="LastTimeDegraded">The last time degraded.</param>
/// <param name="StatModType">The stat mod type.</param>
/// <param name="StatModKey">The stat mod key.</param>
/// <param name="StatModValue">The stat mod value.</param>
/// <param name="SpellSetId">The spell set id.</param>
public sealed record AceEnchantment(
    ushort SpellId,
    ushort Layer,
    ushort SpellCategory,
    ushort HasSpellSetId,
    uint PowerLevel,
    double StartTime,
    double Duration,
    uint CasterGuid,
    float DegradeModifier,
    float DegradeLimit,
    double LastTimeDegraded,
    uint StatModType,
    uint StatModKey,
    float StatModValue,
    uint SpellSetId);
