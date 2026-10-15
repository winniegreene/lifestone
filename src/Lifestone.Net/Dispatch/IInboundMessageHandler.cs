using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Dispatch;

/// <summary>
/// One callback per decoded server-to-client message; the single decode path that live play, replay and fixture tests
/// share (<see cref="InboundDispatcher"/>). Every member has a no-op default, so a handler implements only what it
/// needs.
/// </summary>
/// <remarks>
/// <para>Records own their data and may be kept. A <see cref="ReadOnlySpan{T}"/> argument is valid only during the
/// call. Small records come by <c>in</c>; records holding arrays or strings that a handler typically keeps come by
/// reference.</para>
/// <para>Called on the session's game thread, in delivery order (global by fragment sequence unless the session runs
/// with <see cref="Session.InboundOrder.Arrival"/>). Messages the dispatcher does not decode reach
/// <see cref="OnUnknown"/>, events it does not decode reach <see cref="OnGameEvent"/>, and bodies it cannot parse reach
/// <see cref="OnMalformed"/>: nothing is dropped silently.</para>
/// </remarks>
public interface IInboundMessageHandler
{
    // Login and character select.

    /// <summary>F658 CharacterList.</summary>
    void OnCharacterList(in InboundContext context, CharacterListMsg message)
    {
    }

    /// <summary>F7E1 ServerName.</summary>
    void OnServerName(in InboundContext context, in ServerNameMsg message)
    {
    }

    /// <summary>F7E5 DDD_Interrogation (the session answers it itself; handlers only observe).</summary>
    void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message)
    {
    }

    /// <summary>F7E7 DDD_BeginDDD (a data mismatch).</summary>
    void OnDddBegin(in InboundContext context, in DddBeginMsg message)
    {
    }

    /// <summary>F7EA DDD_EndDDD (the data check is complete).</summary>
    void OnDddEnd(in InboundContext context)
    {
    }

    /// <summary>F643 CharacterCreateResponse (also the answer to CharacterRestore).</summary>
    void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message)
    {
    }

    /// <summary>F655 CharacterDelete acknowledgement (no payload).</summary>
    void OnCharacterDeleteAck(in InboundContext context)
    {
    }

    /// <summary>F659 CharacterError.</summary>
    void OnCharacterError(in InboundContext context, in CharacterErrorMsg message)
    {
    }

    /// <summary>F7DC AccountBoot.</summary>
    void OnAccountBoot(in InboundContext context, in AccountBootMsg message)
    {
    }

    /// <summary>F7C1 AccountBanned.</summary>
    void OnAccountBanned(in InboundContext context, in AccountBannedMsg message)
    {
    }

    /// <summary>F7DF CharacterEnterWorldServerReady (no payload).</summary>
    void OnEnterWorldServerReady(in InboundContext context)
    {
    }

    /// <summary>F746 PlayerCreate.</summary>
    void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message)
    {
    }

    /// <summary>F653 CharacterLogOff from the server (no payload).</summary>
    void OnCharacterLogOff(in InboundContext context)
    {
    }

    /// <summary>F7E0 ServerMessage (system chat; carries the server mod's <c>#lifestone/1</c> tool replies).</summary>
    void OnServerMessage(in InboundContext context, in ServerMessageMsg message)
    {
    }

    // World.

    /// <summary>F745 ObjectCreate (<paramref name="isUpdate"/> false) or F7DB UpdateObject (true, a forced re-create).</summary>
    void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
    {
    }

    /// <summary>F747 ObjectDelete.</summary>
    void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message)
    {
    }

    /// <summary>F748 UpdatePosition.</summary>
    void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message)
    {
    }

    /// <summary>F74C UpdateMotion.</summary>
    void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message)
    {
    }

    /// <summary>F74E VectorUpdate.</summary>
    void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message)
    {
    }

    /// <summary>F74B SetState.</summary>
    void OnSetState(in InboundContext context, in SetStateMsg message)
    {
    }

    /// <summary>F751 PlayerTeleport.</summary>
    void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message)
    {
    }

    /// <summary>F749 ParentEvent.</summary>
    void OnParentEvent(in InboundContext context, in ParentEventMsg message)
    {
    }

    /// <summary>F74A PickupEvent.</summary>
    void OnPickupEvent(in InboundContext context, in PickupEventMsg message)
    {
    }

    /// <summary>F625 ObjDescEvent.</summary>
    void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message)
    {
    }

    /// <summary>F750 Sound.</summary>
    void OnSound(in InboundContext context, in SoundMsg message)
    {
    }

    /// <summary>F755 PlayEffect.</summary>
    void OnPlayEffect(in InboundContext context, in PlayEffectMsg message)
    {
    }

    // Chat.

    /// <summary>0x02BB HearSpeech and 0x02BC HearRangedSpeech.</summary>
    void OnSpeech(in InboundContext context, in SpeechMsg message)
    {
    }

    /// <summary>0x01E0 EmoteText and 0x01E2 SoulEmote.</summary>
    void OnEmote(in InboundContext context, in EmoteMsg message)
    {
    }

    // Game events (F7B0).

    /// <summary>GameEvent 0x02BD Tell.</summary>
    void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message)
    {
    }

    /// <summary>GameEvent 0x0004 PopupString.</summary>
    void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message)
    {
    }

    /// <summary>GameEvent 0x028A WeenieError and 0x028B WeenieErrorWithString.</summary>
    void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message)
    {
    }

    /// <summary>GameEvent 0x02EB CommunicationTransientString.</summary>
    void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message)
    {
    }

    /// <summary>GameEvent 0x01C7 UseDone.</summary>
    void OnUseDone(in InboundContext context, in GameEventHeader header, in UseDoneEvent message)
    {
    }

    /// <summary>GameEvent 0x01C0 UpdateHealth.</summary>
    void OnUpdateHealth(in InboundContext context, in GameEventHeader header, in UpdateHealthEvent message)
    {
    }

    /// <summary>GameEvent 0x0013 PlayerDescription.</summary>
    void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
    {
    }

    /// <summary>A game event the dispatcher does not decode; <paramref name="payload"/> follows the 12-byte header.</summary>
    void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload)
    {
    }

    // The player's own updates.

    /// <summary>A PrivateUpdate message (property, attribute, vital or skill).</summary>
    void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message)
    {
    }

    // Everything else.

    /// <summary>A message the dispatcher does not decode; <paramref name="body"/> is the whole message, opcode included.</summary>
    void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body)
    {
    }

    /// <summary>
    /// A message whose body could not be parsed (truncated, trailing bytes, an undocumented layout). Nothing was
    /// decoded from it. <paramref name="opcode"/> is 0 when the body is shorter than an opcode.
    /// </summary>
    void OnMalformed(in InboundContext context, uint opcode, int length, string reason)
    {
    }
}
