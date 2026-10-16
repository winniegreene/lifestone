using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Dispatch;

/// <summary>
/// Decodes one reassembled server-to-client message and calls the matching <see cref="IInboundMessageHandler"/>
/// member. The one decode path for live play, replay and fixtures. It never throws on bad input: a body it cannot parse
/// goes to <see cref="IInboundMessageHandler.OnMalformed"/>. Exceptions thrown by the handler itself propagate.
/// </summary>
/// <remarks>
/// <para>N3 decodes the login and character-select messages (F658, F7E1, F7E5, F7E7, F7EA, F643, F655, F659, F7DC,
/// F7C1, F7DF, F746, F653, F7E0). N7 adds the world (F745 and F7DB, F747, F748, F74C, F74E, F74B, F751, F749, F74A,
/// F625, F750, F755), local chat (0x02BB, 0x02BC, 0x01E0, 0x01E2) and six game events inside F7B0 (0x02BD Tell, 0x0004
/// PopupString, 0x028A and 0x028B WeenieError, 0x02EB TransientString, 0x01C7 UseDone, 0x01C0 UpdateHealth). N10 adds
/// the player's own state: GameEvent 0x0013 PlayerDescription and the PrivateUpdate messages 0x02CD PropertyInt, 0x02CF
/// PropertyInt64, 0x02DD Skill, 0x02E3 Attribute, 0x02E7 Vital and 0x02E9 Attribute2ndLevel. Every other game event
/// reaches <see cref="IInboundMessageHandler.OnGameEvent"/> with its header and payload; every other opcode reaches
/// <see cref="IInboundMessageHandler.OnUnknown"/>.</para>
/// <para>M3 1.2 s0 adds the inventory, combat, magic and admin messages: GM 0x0024, 0x0197, 0x019E, 0x02CE, 0x02DA and 0xEA60, and
/// the game events 0x0022, 0x0023, 0x00A0, 0x019A, 0x01AC, 0x01AD, 0x01B1-0x01B4, 0x02C1-0x02C8 and 0x0312, each to its own
/// callback (<c>OnInventoryRemoveObject</c> ... <c>OnMagicPurgeBadEnchantments</c>).</para>
/// <para>A message is decoded only when its parser read every byte (<see cref="MessageCodec"/>): a truncated message,
/// one with trailing bytes, or an opcode-only signal that carries a body is malformed, with a reason naming the message
/// (for a game event, the envelope's opcode and the event type).</para>
/// <para>The small world messages (F747, F748, F74E, F74B, F751, F749, F74A, F750, F755) and the PrivateUpdate messages
/// decode into structs and are dispatched without allocating; F748 UpdatePosition is the hot path (m2-plan §3.3 N7,
/// AllocationTests).</para>
/// </remarks>
public static class InboundDispatcher
{
    /// <summary>Dispatches <paramref name="body"/> (the whole message, starting with its u32 opcode) to <paramref name="handler"/>.</summary>
    public static DispatchResult Dispatch(in InboundContext context, ReadOnlySpan<byte> body, IInboundMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!MessageCodec.TryReadOpcode(body, out var opcode))
        {
            handler.OnMalformed(context, 0, body.Length, "shorter than a message opcode");
            return new DispatchResult(DispatchOutcome.Malformed, 0);
        }

        switch (opcode)
        {
            case MessageOpcodes.CharacterList:
                if (CharacterListCodec.TryParse(body, out var characterList))
                {
                    handler.OnCharacterList(context, characterList);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F658 CharacterList: truncated, trailing bytes or a count larger than the message");

            case MessageOpcodes.ServerName:
                if (ServerNameCodec.TryParse(body, out var serverName))
                {
                    handler.OnServerName(context, serverName);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7E1 ServerName: truncated or trailing bytes");

            case MessageOpcodes.DddInterrogation:
                if (DddCodec.TryParseInterrogation(body, out var interrogation))
                {
                    handler.OnDddInterrogation(context, interrogation);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7E5 DDD_Interrogation: truncated, trailing bytes or a language count larger than the message");

            case MessageOpcodes.DddBegin:
                if (DddCodec.TryParseBegin(body, out var begin))
                {
                    handler.OnDddBegin(context, begin);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7E7 DDD_BeginDDD: truncated, trailing bytes or a missing-iteration list that does not fit");

            case MessageOpcodes.DddEnd:
                if (DddCodec.IsEnd(body))
                {
                    handler.OnDddEnd(context);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7EA DDD_EndDDD: carries a body (it is the opcode alone)");

            case MessageOpcodes.CharacterCreateResponse:
                if (CharGenResponseCodec.TryParse(body, out var charGen))
                {
                    handler.OnCharGenResponse(context, charGen);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F643 CharacterCreateResponse: truncated or trailing bytes");

            case MessageOpcodes.CharacterDelete:
                if (CharacterDeleteCodec.IsAcknowledgement(body))
                {
                    handler.OnCharacterDeleteAck(context);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F655 CharacterDelete acknowledgement: carries a body (it is the opcode alone)");

            case MessageOpcodes.CharacterError:
                if (CharacterErrorCodec.TryParse(body, out var characterError))
                {
                    handler.OnCharacterError(context, characterError);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F659 CharacterError: not exactly one u32 code");

            case MessageOpcodes.AccountBoot:
                if (AccountBootCodec.TryParse(body, out var boot))
                {
                    handler.OnAccountBoot(context, boot);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7DC AccountBoot: neither empty nor one String16L reason");

            case MessageOpcodes.AccountBanned:
                if (AccountBannedCodec.TryParse(body, out var banned))
                {
                    handler.OnAccountBanned(context, banned);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7C1 AccountBanned: not a u32 and an optional String16L reason");

            case MessageOpcodes.CharacterEnterWorldServerReady:
                if (CharacterEnterWorldCodec.IsServerReady(body))
                {
                    handler.OnEnterWorldServerReady(context);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7DF CharacterEnterWorldServerReady: carries a body (it is the opcode alone)");

            case MessageOpcodes.PlayerCreate:
                if (PlayerCreateCodec.TryParse(body, out var playerCreate))
                {
                    handler.OnPlayerCreate(context, playerCreate);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F746 PlayerCreate: not exactly one u32 GUID");

            case MessageOpcodes.CharacterLogOff:
                if (CharacterLogOffCodec.IsServerLogOff(body))
                {
                    handler.OnCharacterLogOff(context);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F653 CharacterLogOff: carries a body (the server's is the opcode alone)");

            case MessageOpcodes.ServerMessage:
                if (ServerMessageCodec.TryParse(body, out var serverMessage))
                {
                    handler.OnServerMessage(context, serverMessage);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F7E0 ServerMessage: not a String16L and an i32 chat type");

            case MessageOpcodes.GameEvent:
                if (GameEventEnvelope.TryReadHeader(body, out var header))
                {
                    return DispatchGameEvent(context, body, header, handler);
                }

                return Malformed(context, opcode, body, handler, "F7B0 GameEvent: shorter than its 16-byte header");

            // World (N7).

            case MessageOpcodes.ObjectCreate or MessageOpcodes.UpdateObject:
                if (CreateObjectCodec.TryParse(body, opcode, out var create))
                {
                    handler.OnCreateObject(context, create, opcode == MessageOpcodes.UpdateObject);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, opcode == MessageOpcodes.UpdateObject
                    ? "F7DB UpdateObject: truncated, trailing bytes, a ModelData marker other than 0x11, an unknown movement type or a movement body that does not fill its length"
                    : "F745 ObjectCreate: truncated, trailing bytes, a ModelData marker other than 0x11, an unknown movement type or a movement body that does not fill its length");

            case MessageOpcodes.ObjectDelete:
                if (ObjectEventCodecs.TryParseDelete(body, out var delete))
                {
                    handler.OnDeleteObject(context, delete);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F747 ObjectDelete: not exactly a GUID, a u16 instance sequence and its pad");

            case MessageOpcodes.UpdatePosition:
                if (UpdatePositionCodec.TryParse(body, out var position))
                {
                    handler.OnUpdatePosition(context, position);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F748 UpdatePosition: truncated, or trailing bytes after the four sequences");

            case MessageOpcodes.UpdateMotion:
                if (UpdateMotionCodec.TryParse(body, out var motion))
                {
                    handler.OnUpdateMotion(context, motion);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F74C UpdateMotion: truncated, trailing bytes or an unknown movement type");

            case MessageOpcodes.VectorUpdate:
                if (ObjectEventCodecs.TryParseVector(body, out var vector))
                {
                    handler.OnVectorUpdate(context, vector);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F74E VectorUpdate: not exactly a GUID, two vectors and two sequences");

            case MessageOpcodes.SetState:
                if (ObjectEventCodecs.TryParseSetState(body, out var setState))
                {
                    handler.OnSetState(context, setState);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F74B SetState: not exactly a GUID, a state and two sequences");

            case MessageOpcodes.PlayerTeleport:
                if (ObjectEventCodecs.TryParseTeleport(body, out var teleport))
                {
                    handler.OnPlayerTeleport(context, teleport);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F751 PlayerTeleport: not exactly a u16 teleport sequence and its pad");

            case MessageOpcodes.ParentEvent:
                if (ObjectEventCodecs.TryParseParent(body, out var parent))
                {
                    handler.OnParentEvent(context, parent);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F749 ParentEvent: not exactly two GUIDs, a location, a placement and two sequences");

            case MessageOpcodes.PickupEvent:
                if (ObjectEventCodecs.TryParsePickup(body, out var pickup))
                {
                    handler.OnPickupEvent(context, pickup);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F74A PickupEvent: not exactly a GUID and two sequences");

            case MessageOpcodes.ObjDescEvent:
                if (ObjDescEventCodec.TryParse(body, out var objDesc))
                {
                    handler.OnObjDescEvent(context, objDesc);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F625 ObjDescEvent: truncated, trailing bytes or a ModelData marker other than 0x11");

            case MessageOpcodes.Sound:
                if (ObjectEventCodecs.TryParseSound(body, out var sound))
                {
                    handler.OnSound(context, sound);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F750 Sound: not exactly a GUID, a sound and a volume");

            case MessageOpcodes.PlayEffect:
                if (ObjectEventCodecs.TryParseEffect(body, out var effect))
                {
                    handler.OnPlayEffect(context, effect);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "F755 PlayEffect: not exactly a GUID, a script and a speed");

            // Chat (N7).

            case MessageOpcodes.HearSpeech or MessageOpcodes.HearRangedSpeech:
                if (ChatCodecs.TryParseSpeech(body, out var speech))
                {
                    handler.OnSpeech(context, speech);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, opcode == MessageOpcodes.HearRangedSpeech
                    ? "02BC HearRangedSpeech: not two String16L, a GUID, a range and a chat type"
                    : "02BB HearSpeech: not two String16L, a GUID and a chat type");

            case MessageOpcodes.EmoteText or MessageOpcodes.SoulEmote:
                if (ChatCodecs.TryParseEmote(body, out var emote))
                {
                    handler.OnEmote(context, emote);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, opcode == MessageOpcodes.SoulEmote
                    ? "01E2 SoulEmote: not a GUID and two String16L"
                    : "01E0 EmoteText: not a GUID and two String16L");

            // The player's own updates (N10).

            case MessageOpcodes.PrivateUpdatePropertyInt or MessageOpcodes.PrivateUpdatePropertyInt64 or MessageOpcodes.PrivateUpdateSkill
                or MessageOpcodes.PrivateUpdateAttribute or MessageOpcodes.PrivateUpdateVital or MessageOpcodes.PrivateUpdateAttribute2ndLevel:
                if (PrivateUpdateCodec.TryParse(body, out var update))
                {
                    handler.OnPrivateUpdate(context, update);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, PrivateUpdateCodec.MalformedReason(opcode));

            // M3 1.2 s0: inventory, combat, public updates and admin.

            case MessageOpcodes.InventoryRemoveObject:
                if (InventoryCodecs.TryParseRemoveObject(body, out var inventoryRemoveObject))
                {
                    handler.OnInventoryRemoveObject(context, inventoryRemoveObject);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "0024 InventoryRemoveObject: not exactly a u32 GUID");

            case MessageOpcodes.SetStackSize:
                if (InventoryCodecs.TryParseSetStackSize(body, out var setStackSize))
                {
                    handler.OnSetStackSize(context, setStackSize);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "0197 SetStackSize: not exactly a u8 sequence, a GUID, a stack size and a value");

            case MessageOpcodes.PlayerKilled:
                if (CombatCodecs.TryParsePlayerKilled(body, out var playerKilled))
                {
                    handler.OnPlayerKilled(context, playerKilled);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "019E PlayerKilled: not a String16L, a victim GUID and a killer GUID");

            case MessageOpcodes.PublicUpdatePropertyInt:
                if (InventoryCodecs.TryParsePublicUpdatePropertyInt(body, out var publicUpdatePropertyInt))
                {
                    handler.OnPublicUpdatePropertyInt(context, publicUpdatePropertyInt);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "02CE PublicUpdatePropertyInt: not exactly a u8 sequence, a GUID, a property and an i32 value");

            case MessageOpcodes.PublicUpdateInstanceId:
                if (InventoryCodecs.TryParsePublicUpdateInstanceId(body, out var publicUpdateInstanceId))
                {
                    handler.OnPublicUpdateInstanceId(context, publicUpdateInstanceId);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "02DA PublicUpdateInstanceId: not exactly a u8 sequence, a GUID, a property and a u32 value");

            case MessageOpcodes.AdminEnvirons:
                if (AdminEnvironsCodec.TryParse(body, out var adminEnvirons))
                {
                    handler.OnAdminEnvirons(context, adminEnvirons);
                    return Decoded(opcode);
                }

                return Malformed(context, opcode, body, handler, "EA60 AdminEnvirons: not exactly one u32 environ id");

            default:
                handler.OnUnknown(context, opcode, body);
                return new DispatchResult(DispatchOutcome.Unknown, opcode);
        }
    }

    /// <summary>The game events N7 and N10 decode reach their callbacks; every other one reaches OnGameEvent.</summary>
    private static DispatchResult DispatchGameEvent(in InboundContext context, ReadOnlySpan<byte> body, in GameEventHeader header, IInboundMessageHandler handler)
    {
        switch (header.EventType)
        {
            case GameEventTypes.Tell:
                if (GameEventCodecs.TryParseTell(body, out var tell))
                {
                    handler.OnTell(context, header, tell);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02BD Tell: not two String16L and four words");

            case GameEventTypes.PopupString:
                if (GameEventCodecs.TryParsePopup(body, out var popup))
                {
                    handler.OnPopup(context, header, popup);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x0004 PopupString: not exactly one String16L");

            case GameEventTypes.WeenieError or GameEventTypes.WeenieErrorWithString:
                var withString = header.EventType == GameEventTypes.WeenieErrorWithString;
                if (GameEventCodecs.TryParseWeenieError(body, withString, out var error))
                {
                    handler.OnWeenieError(context, header, error);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, withString
                    ? "F7B0 GameEvent 0x028B WeenieErrorWithString: not a u32 code and one String16L"
                    : "F7B0 GameEvent 0x028A WeenieError: not exactly one u32 code");

            case GameEventTypes.TransientString:
                if (GameEventCodecs.TryParseTransientString(body, out var transient))
                {
                    handler.OnTransientString(context, header, transient);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02EB CommunicationTransientString: not exactly one String16L");

            case GameEventTypes.UseDone:
                if (GameEventCodecs.TryParseUseDone(body, out var useDone))
                {
                    handler.OnUseDone(context, header, useDone);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01C7 UseDone: not exactly one u32 code");

            case GameEventTypes.UpdateHealth:
                if (GameEventCodecs.TryParseUpdateHealth(body, out var health))
                {
                    handler.OnUpdateHealth(context, header, health);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01C0 UpdateHealth: not exactly a GUID and a health fraction");

            case GameEventTypes.PlayerDescription:
                if (PlayerDescriptionCodec.TryParse(body, out var description, out var reason))
                {
                    handler.OnPlayerDescription(context, header, description);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, reason);

            // M3 1.2 s0: inventory, combat and magic events.

            case GameEventTypes.InventoryPutObjInContainer:
                if (InventoryCodecs.TryParsePutObjInContainer(body, out var inventoryPutObjInContainer))
                {
                    handler.OnInventoryPutObjInContainer(context, header, inventoryPutObjInContainer);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x0022 InventoryPutObjInContainer: not exactly an item, a container, a placement and a container type");

            case GameEventTypes.WieldObject:
                if (InventoryCodecs.TryParseWieldObject(body, out var wieldObject))
                {
                    handler.OnWieldObject(context, header, wieldObject);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x0023 WieldObject: not exactly an item and an equip mask");

            case GameEventTypes.InventoryPutObjectIn3D:
                if (InventoryCodecs.TryParsePutObjectIn3D(body, out var inventoryPutObjectIn3D))
                {
                    handler.OnInventoryPutObjectIn3D(context, header, inventoryPutObjectIn3D);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x019A InventoryPutObjectIn3D: not exactly one u32 GUID");

            case GameEventTypes.InventoryServerSaysFailed:
                if (InventoryCodecs.TryParseServerSaysFailed(body, out var inventoryServerSaysFailed))
                {
                    handler.OnInventoryServerSaysFailed(context, header, inventoryServerSaysFailed);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x00A0 InventoryServerSaysFailed: not exactly an item GUID and a WeenieError");

            case GameEventTypes.VictimNotification:
                if (CombatCodecs.TryParseVictim(body, out var victimNotification))
                {
                    handler.OnVictimNotification(context, header, victimNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01AC VictimNotification: not exactly one String16L");

            case GameEventTypes.KillerNotification:
                if (CombatCodecs.TryParseKiller(body, out var killerNotification))
                {
                    handler.OnKillerNotification(context, header, killerNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01AD KillerNotification: not exactly one String16L");

            case GameEventTypes.AttackerNotification:
                if (CombatCodecs.TryParseAttacker(body, out var attackerNotification))
                {
                    handler.OnAttackerNotification(context, header, attackerNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01B1 AttackerNotification: not a String16L, a damage type, a double, a damage, a critical word and a u64");

            case GameEventTypes.DefenderNotification:
                if (CombatCodecs.TryParseDefender(body, out var defenderNotification))
                {
                    handler.OnDefenderNotification(context, header, defenderNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01B2 DefenderNotification: not a String16L, a damage type, a double, a damage, a location, a critical word and a u64");

            case GameEventTypes.EvasionAttackerNotification:
                if (CombatCodecs.TryParseEvasionAttacker(body, out var evasionAttackerNotification))
                {
                    handler.OnEvasionAttackerNotification(context, header, evasionAttackerNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01B3 EvasionAttackerNotification: not exactly one String16L");

            case GameEventTypes.EvasionDefenderNotification:
                if (CombatCodecs.TryParseEvasionDefender(body, out var evasionDefenderNotification))
                {
                    handler.OnEvasionDefenderNotification(context, header, evasionDefenderNotification);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x01B4 EvasionDefenderNotification: not exactly one String16L");

            case GameEventTypes.MagicUpdateSpell:
                if (MagicCodecs.TryParseUpdateSpell(body, out var magicUpdateSpell))
                {
                    handler.OnMagicUpdateSpell(context, header, magicUpdateSpell);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C1 MagicUpdateSpell: not exactly a u16 spell and a u16 layer");

            case GameEventTypes.MagicUpdateEnchantment:
                if (MagicCodecs.TryParseUpdateEnchantment(body, out var magicUpdateEnchantment))
                {
                    handler.OnMagicUpdateEnchantment(context, header, magicUpdateEnchantment);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C2 MagicUpdateEnchantment: not exactly one 60 or 64 byte enchantment");

            case GameEventTypes.MagicRemoveEnchantment:
                if (MagicCodecs.TryParseRemoveEnchantment(body, out var magicRemoveEnchantment))
                {
                    handler.OnMagicRemoveEnchantment(context, header, magicRemoveEnchantment);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C3 MagicRemoveEnchantment: not exactly a u16 spell and a u16 layer");

            case GameEventTypes.MagicUpdateMultipleEnchantments:
                if (MagicCodecs.TryParseUpdateMultipleEnchantments(body, out var magicUpdateMultipleEnchantments))
                {
                    handler.OnMagicUpdateMultipleEnchantments(context, header, magicUpdateMultipleEnchantments);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C4 MagicUpdateMultipleEnchantments: not a count and that many enchantments");

            case GameEventTypes.MagicRemoveMultipleEnchantments:
                if (MagicCodecs.TryParseRemoveMultipleEnchantments(body, out var magicRemoveMultipleEnchantments))
                {
                    handler.OnMagicRemoveMultipleEnchantments(context, header, magicRemoveMultipleEnchantments);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C5 MagicRemoveMultipleEnchantments: not a count and that many spell and layer pairs");

            case GameEventTypes.MagicPurgeEnchantments:
                if (MagicCodecs.HasNoPayload(body))
                {
                    handler.OnMagicPurgeEnchantments(context, header, default);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C6 MagicPurgeEnchantments: carries a payload (it is the envelope alone)");

            case GameEventTypes.MagicDispelEnchantment:
                if (MagicCodecs.TryParseDispelEnchantment(body, out var magicDispelEnchantment))
                {
                    handler.OnMagicDispelEnchantment(context, header, magicDispelEnchantment);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C7 MagicDispelEnchantment: not exactly a u16 spell and a u16 layer");

            case GameEventTypes.MagicDispelMultipleEnchantments:
                if (MagicCodecs.TryParseDispelMultipleEnchantments(body, out var magicDispelMultipleEnchantments))
                {
                    handler.OnMagicDispelMultipleEnchantments(context, header, magicDispelMultipleEnchantments);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x02C8 MagicDispelMultipleEnchantments: not a count and that many spell and layer pairs");

            case GameEventTypes.MagicPurgeBadEnchantments:
                if (MagicCodecs.HasNoPayload(body))
                {
                    handler.OnMagicPurgeBadEnchantments(context, header, default);
                    return DecodedEvent(header);
                }

                return MalformedEvent(context, body, header, handler, "F7B0 GameEvent 0x0312 MagicPurgeBadEnchantments: carries a payload (it is the envelope alone)");

            default:
                handler.OnGameEvent(context, header, body[GameEventEnvelope.HeaderSize..]);
                return new DispatchResult(DispatchOutcome.Unknown, MessageOpcodes.GameEvent, header.EventType);
        }
    }

    private static DispatchResult DecodedEvent(in GameEventHeader header) => new(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, header.EventType);

    private static DispatchResult MalformedEvent(in InboundContext context, ReadOnlySpan<byte> body, in GameEventHeader header, IInboundMessageHandler handler, string reason)
    {
        handler.OnMalformed(context, MessageOpcodes.GameEvent, body.Length, reason);
        return new DispatchResult(DispatchOutcome.Malformed, MessageOpcodes.GameEvent, header.EventType);
    }

    private static DispatchResult Decoded(uint opcode) => new(DispatchOutcome.Decoded, opcode);

    private static DispatchResult Malformed(in InboundContext context, uint opcode, ReadOnlySpan<byte> body, IInboundMessageHandler handler, string reason)
    {
        handler.OnMalformed(context, opcode, body.Length, reason);
        return new DispatchResult(DispatchOutcome.Malformed, opcode);
    }
}
