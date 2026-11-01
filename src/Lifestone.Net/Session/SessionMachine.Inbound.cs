using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// The messages the session itself reacts to (m2-plan §3.3 N5): the data check (F7E5, F7EA, F7E7), the character list,
/// CharacterError, AccountBoot and AccountBanned. Every message goes through <see cref="SessionInbound"/>, which lets the
/// session update its state first and then forwards the message to the sink, so a sink reading
/// <see cref="CharacterList"/> or <see cref="Phase"/> inside a callback sees the message's effect; the session events it
/// causes follow the callback.
/// </summary>
/// <remarks>
/// <para><b>The data check.</b> F7E5 is answered with F7E6 from <see cref="AcSessionOptions.Iterations"/> (the high-res
/// dat only when the server's product id allows it) and raises <see cref="SessionEventKind.DataCheckStarted"/>; F7EA
/// raises <see cref="SessionEventKind.DataCheckComplete"/> and is never echoed back (OpenAC echoed it; retail does
/// not). F7E7, whether it parsed or not, means the server would patch: the session fails with
/// <see cref="SessionFailureKind.DataCheckMismatch"/>. ACE with patching off boots instead (F7DC "because the client's
/// dat files are older/newer"), so a boot that answers the F7E6 is reported as a mismatch too, with the server's
/// reason.</para>
/// <para><b>Character select</b> needs both F7EA and F658, in either order: ACE bundles its database queue (F7E5) and
/// its UI queue (F658, F7E1) separately, at most one bundle per 5 ms, so F7E5 usually comes first.</para>
/// <para><b>CharacterError</b> (F659) during login is classified by the retry policy (0x01, 0x0D and 0x15 retry; the
/// rest fail, a graceful shutdown's 0x04 at once). In character select and in the world, 0x01 (another login took the
/// account), 0x04 and 0x08 (the server is going down) end the session; any other code answers an operation and raises
/// <see cref="SessionEventKind.CharacterErrorReceived"/>. Retail shows these in
/// <c>CPlayerSystem::Handle_CharacterError</c> (11.4186 0x0055D5D0, 11.6096 0x0055E2F0).</para>
/// </remarks>
internal sealed partial class SessionMachine
{
    private bool _dataCheckAnswered;
    private bool _dataCheckComplete;
    private bool _listThisConnection;
    private long _dataCheckStartedAt;

    /// <summary>Forgets the data-check progress of the previous attempt.</summary>
    private void ResetLoginState()
    {
        _dataCheckAnswered = false;
        _dataCheckComplete = false;
        _listThisConnection = false;
    }

    private void OnCharacterListMessage(CharacterListMsg list)
    {
        CharacterList = list;
        switch (Phase)
        {
            case SessionPhase.Handshaking or SessionPhase.DataCheck:
                _listThisConnection = true;
                TryEnterCharacterSelect();
                break;
            case SessionPhase.LoggingOff:
                OnLogOffList();
                break;
            case SessionPhase.CharacterSelect:
                OnCharacterSelectList();
                break;
        }
    }

    private void OnDddInterrogationMessage(DddInterrogationMsg interrogation)
    {
        if (_connection?.Transport is null)
        {
            return;
        }

        var writer = BeginMessage();
        DddCodec.WriteInterrogationResponse(writer, _options.Iterations, interrogation.AllowsHighRes);
        SendMessage(writer, MessageQueue.Database);
        _dataCheckAnswered = true;
        Raise(SessionEvent.DataCheckStarted(_time.GetTimestamp(), Phase));
        Log(NetLogLevel.Info, $"data check: reported {DescribeIterations(interrogation.AllowsHighRes)}");
    }

    private void OnDddEndMessage()
    {
        _dataCheckComplete = true;
        if (Phase == SessionPhase.DataCheck)
        {
            Raise(SessionEvent.DataCheckComplete(_time.GetTimestamp(), Phase));
            TryEnterCharacterSelect();
        }
    }

    private void OnDddBeginMessage(string detail) =>
        Fail(SessionFailureKind.DataCheckMismatch,
            $"the server's dat iterations differ from the client's ({DescribeIterations(highResAllowed: true)}): it sent DDD_BeginDDD ({detail}); Lifestone never patches");

    private void TryEnterCharacterSelect()
    {
        if (Phase != SessionPhase.DataCheck || !_dataCheckComplete || !_listThisConnection)
        {
            return;
        }

        SetPhase(SessionPhase.CharacterSelect);
        ZeroPassword();
        var list = CharacterList!;
        Log(NetLogLevel.Info,
            $"reached character select: {list.Characters.Length} character(s), DDD {DescribeIterations(highResAllowed: true)} ok");
    }

    private void OnCharacterErrorMessage(in CharacterErrorMsg error)
    {
        var code = error.Code;
        var name = error.Known?.ToString() ?? "an unknown code";
        switch (Phase)
        {
            case SessionPhase.Connecting or SessionPhase.Handshaking or SessionPhase.DataCheck:
                if (_options.Retry.RetryableCharacterErrors.Contains(code) && TryScheduleRetry(code, $"the server refused the login with {name}"))
                {
                    return;
                }

                Fail(SessionFailureKind.CharacterError, $"the server refused the login with CharacterError 0x{code:X2} ({name})", code);
                return;
            case SessionPhase.EnteringWorld:
                OnEnterWorldError(code, name);
                return;
            case SessionPhase.CharacterSelect or SessionPhase.InWorld or SessionPhase.LoggingOff:
                if (IsSessionEnding(code))
                {
                    Fail(SessionFailureKind.CharacterError, $"the server ended the session with CharacterError 0x{code:X2} ({name})", code);
                    return;
                }

                CompleteCharacterOperationWithError(code);
                Raise(SessionEvent.CharacterErrorReceived(_time.GetTimestamp(), Phase, code));
                Log(NetLogLevel.Warning, $"CharacterError 0x{code:X2} ({name}) in {Phase}");
                return;
        }
    }

    private void OnAccountBootMessage(string? reason)
    {
        Raise(SessionEvent.Booted(_time.GetTimestamp(), Phase, reason));
        var shown = string.IsNullOrEmpty(reason) ? " for Code of Conduct Violations" : reason;
        if (Phase == SessionPhase.DataCheck && _dataCheckAnswered && !_dataCheckComplete)
        {
            Fail(SessionFailureKind.DataCheckMismatch,
                $"the server refused the dat iterations ({DescribeIterations(highResAllowed: true)}) and booted the session{shown}");
            return;
        }

        Fail(SessionFailureKind.Booted, "booted from the server" + shown);
    }

    private void OnAccountBannedMessage(in AccountBannedMsg banned)
    {
        var seconds = unchecked((int)banned.SecondsRemaining);
        var until = seconds > 0 ? $"banned for another {TimeSpan.FromSeconds(seconds)}" : "banned permanently";
        Fail(SessionFailureKind.AccountBanned, $"the account is {until}{banned.Reason}");
    }

    private void OnMalformedMessage(uint opcode, string reason)
    {
        switch (opcode)
        {
            case MessageOpcodes.DddBegin:
                OnDddBeginMessage("malformed");
                return;
            case MessageOpcodes.AccountBoot:
                OnAccountBootMessage(null);
                return;
            case MessageOpcodes.AccountBanned:
                Fail(SessionFailureKind.AccountBanned, "the account is banned (the message could not be read)");
                return;
            case MessageOpcodes.CharacterList or MessageOpcodes.DddInterrogation or MessageOpcodes.DddEnd
                when Phase is SessionPhase.Handshaking or SessionPhase.DataCheck:
                Fail(SessionFailureKind.ProtocolError, "a login message could not be read: " + reason, opcode);
                return;
        }

        Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.MalformedMessage, reason));
        Log(NetLogLevel.Warning, "inbound: skipped a malformed message: " + reason);
    }

    private static bool IsSessionEnding(uint code) =>
        code is (uint)CharacterErrorCode.Logon or (uint)CharacterErrorCode.ServerCrash1 or (uint)CharacterErrorCode.ServerCrash2;

    private string DescribeIterations(bool highResAllowed)
    {
        var i = _options.Iterations;
        var highRes = highResAllowed && i.HighRes is { } h ? $" h{h}" : string.Empty;
        return $"p{i.Portal} c{i.Cell} l{i.Language}{highRes}";
    }

    /// <summary>
    /// The session's handler: every message reaches the session's own bookkeeping first, then the sink (except a folded
    /// duplicate answer). It implements every member of <see cref="IInboundMessageHandler"/>, so none falls through to
    /// a no-op default (a test checks this whenever the interface grows).
    /// </summary>
    private sealed class SessionInbound(SessionMachine machine, IAcSessionSink sink) : IInboundMessageHandler
    {
        /// <summary>The sink; its first use in a message stamps the time (decode and the session's own bookkeeping end, the sink's work begins: <see cref="SessionMachine.SinkStamp"/>).</summary>
        private IAcSessionSink Sink
        {
            get
            {
                machine.StampSink();
                return sink;
            }
        }

        // Login and character select.

        public void OnCharacterList(in InboundContext context, CharacterListMsg message)
        {
            machine.OnCharacterListMessage(message);
            Sink.OnCharacterList(context, message);
        }

        public void OnServerName(in InboundContext context, in ServerNameMsg message) => Sink.OnServerName(context, message);

        public void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message)
        {
            machine.OnDddInterrogationMessage(message);
            Sink.OnDddInterrogation(context, message);
        }

        public void OnDddBegin(in InboundContext context, in DddBeginMsg message)
        {
            machine.OnDddBeginMessage($"{message.MissingIterations} missing iteration(s), {message.TotalFileSize} bytes");
            Sink.OnDddBegin(context, message);
        }

        public void OnDddEnd(in InboundContext context)
        {
            machine.OnDddEndMessage();
            Sink.OnDddEnd(context);
        }

        public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message)
        {
            if (machine.OnCharGenResponseMessage(message))
            {
                Sink.OnCharGenResponse(context, message);
            }
        }

        public void OnCharacterDeleteAck(in InboundContext context)
        {
            machine.OnCharacterDeleteAckMessage();
            Sink.OnCharacterDeleteAck(context);
        }

        public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message)
        {
            machine.OnCharacterErrorMessage(message);
            Sink.OnCharacterError(context, message);
        }

        public void OnAccountBoot(in InboundContext context, in AccountBootMsg message)
        {
            machine.OnAccountBootMessage(message.Reason);
            Sink.OnAccountBoot(context, message);
        }

        public void OnAccountBanned(in InboundContext context, in AccountBannedMsg message)
        {
            machine.OnAccountBannedMessage(message);
            Sink.OnAccountBanned(context, message);
        }

        public void OnEnterWorldServerReady(in InboundContext context)
        {
            machine.OnEnterWorldServerReadyMessage();
            Sink.OnEnterWorldServerReady(context);
        }

        public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message)
        {
            machine.OnPlayerCreateMessage(message.Guid);
            Sink.OnPlayerCreate(context, message);
        }

        public void OnCharacterLogOff(in InboundContext context)
        {
            machine.OnServerLogOffMessage();
            Sink.OnCharacterLogOff(context);
        }

        public void OnServerMessage(in InboundContext context, in ServerMessageMsg message) => Sink.OnServerMessage(context, message);

        // World.

        public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
        {
            if (!isUpdate && message.Guid == machine.PlayerGuid)
            {
                machine.ArmLoginComplete();
            }

            Sink.OnCreateObject(context, message, isUpdate);
        }

        public void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message) => Sink.OnDeleteObject(context, message);

        public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message) => Sink.OnUpdatePosition(context, message);

        public void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message) => Sink.OnUpdateMotion(context, message);

        public void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message) => Sink.OnVectorUpdate(context, message);

        public void OnSetState(in InboundContext context, in SetStateMsg message) => Sink.OnSetState(context, message);

        public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message)
        {
            machine.ArmLoginComplete();
            Sink.OnPlayerTeleport(context, message);
        }

        public void OnParentEvent(in InboundContext context, in ParentEventMsg message) => Sink.OnParentEvent(context, message);

        public void OnPickupEvent(in InboundContext context, in PickupEventMsg message) => Sink.OnPickupEvent(context, message);

        public void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message) => Sink.OnObjDescEvent(context, message);

        public void OnSound(in InboundContext context, in SoundMsg message) => Sink.OnSound(context, message);

        public void OnPlayEffect(in InboundContext context, in PlayEffectMsg message) => Sink.OnPlayEffect(context, message);

        // Chat.

        public void OnSpeech(in InboundContext context, in SpeechMsg message) => Sink.OnSpeech(context, message);

        public void OnEmote(in InboundContext context, in EmoteMsg message) => Sink.OnEmote(context, message);

        // Game events.

        public void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message) => Sink.OnTell(context, header, message);

        public void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message) => Sink.OnPopup(context, header, message);

        public void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message) =>
            Sink.OnWeenieError(context, header, message);

        public void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message) =>
            Sink.OnTransientString(context, header, message);

        public void OnUseDone(in InboundContext context, in GameEventHeader header, in UseDoneEvent message) => Sink.OnUseDone(context, header, message);

        public void OnUpdateHealth(in InboundContext context, in GameEventHeader header, in UpdateHealthEvent message) =>
            Sink.OnUpdateHealth(context, header, message);

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
        {
            if (message.InventoryUndecoded)
            {
                machine.Log(NetLogLevel.Warning,
                    "inbound: PlayerDescription carries gameplay options (option flag 0x200); they and the inventory lists after them are not decoded (TODO(net): retail's options archive)");
            }

            Sink.OnPlayerDescription(context, header, message);
        }

        public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) =>
            Sink.OnGameEvent(context, header, payload);

        // The player's own updates.

        public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message) => Sink.OnPrivateUpdate(context, message);

        // M3 1.2 s0 messages.

        public void OnInventoryRemoveObject(in InboundContext context, in InventoryRemoveObjectMsg message) => Sink.OnInventoryRemoveObject(context, message);

        public void OnSetStackSize(in InboundContext context, in SetStackSizeMsg message) => Sink.OnSetStackSize(context, message);

        public void OnPlayerKilled(in InboundContext context, in PlayerKilledMsg message) => Sink.OnPlayerKilled(context, message);

        public void OnPublicUpdatePropertyInt(in InboundContext context, in PublicUpdatePropertyIntMsg message) => Sink.OnPublicUpdatePropertyInt(context, message);

        public void OnPublicUpdateInstanceId(in InboundContext context, in PublicUpdateInstanceIdMsg message) => Sink.OnPublicUpdateInstanceId(context, message);

        public void OnAdminEnvirons(in InboundContext context, in AdminEnvironsMsg message) => Sink.OnAdminEnvirons(context, message);

        public void OnInventoryPutObjInContainer(in InboundContext context, in GameEventHeader header, in InventoryPutObjInContainerEvent message) =>
            Sink.OnInventoryPutObjInContainer(context, header, message);

        public void OnWieldObject(in InboundContext context, in GameEventHeader header, in WieldObjectEvent message) =>
            Sink.OnWieldObject(context, header, message);

        public void OnInventoryPutObjectIn3D(in InboundContext context, in GameEventHeader header, in InventoryPutObjectIn3DEvent message) =>
            Sink.OnInventoryPutObjectIn3D(context, header, message);

        public void OnInventoryServerSaysFailed(in InboundContext context, in GameEventHeader header, in InventoryServerSaysFailedEvent message) =>
            Sink.OnInventoryServerSaysFailed(context, header, message);

        public void OnVictimNotification(in InboundContext context, in GameEventHeader header, in VictimNotificationEvent message) =>
            Sink.OnVictimNotification(context, header, message);

        public void OnKillerNotification(in InboundContext context, in GameEventHeader header, in KillerNotificationEvent message) =>
            Sink.OnKillerNotification(context, header, message);

        public void OnAttackerNotification(in InboundContext context, in GameEventHeader header, in AttackerNotificationEvent message) =>
            Sink.OnAttackerNotification(context, header, message);

        public void OnDefenderNotification(in InboundContext context, in GameEventHeader header, in DefenderNotificationEvent message) =>
            Sink.OnDefenderNotification(context, header, message);

        public void OnEvasionAttackerNotification(in InboundContext context, in GameEventHeader header, in EvasionAttackerNotificationEvent message) =>
            Sink.OnEvasionAttackerNotification(context, header, message);

        public void OnEvasionDefenderNotification(in InboundContext context, in GameEventHeader header, in EvasionDefenderNotificationEvent message) =>
            Sink.OnEvasionDefenderNotification(context, header, message);

        public void OnMagicUpdateSpell(in InboundContext context, in GameEventHeader header, in MagicUpdateSpellEvent message) =>
            Sink.OnMagicUpdateSpell(context, header, message);

        public void OnMagicUpdateEnchantment(in InboundContext context, in GameEventHeader header, in MagicUpdateEnchantmentEvent message) =>
            Sink.OnMagicUpdateEnchantment(context, header, message);

        public void OnMagicRemoveEnchantment(in InboundContext context, in GameEventHeader header, in MagicRemoveEnchantmentEvent message) =>
            Sink.OnMagicRemoveEnchantment(context, header, message);

        public void OnMagicUpdateMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicUpdateMultipleEnchantmentsEvent message) =>
            Sink.OnMagicUpdateMultipleEnchantments(context, header, message);

        public void OnMagicRemoveMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicRemoveMultipleEnchantmentsEvent message) =>
            Sink.OnMagicRemoveMultipleEnchantments(context, header, message);

        public void OnMagicPurgeEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeEnchantmentsEvent message) =>
            Sink.OnMagicPurgeEnchantments(context, header, message);

        public void OnMagicDispelEnchantment(in InboundContext context, in GameEventHeader header, in MagicDispelEnchantmentEvent message) =>
            Sink.OnMagicDispelEnchantment(context, header, message);

        public void OnMagicDispelMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicDispelMultipleEnchantmentsEvent message) =>
            Sink.OnMagicDispelMultipleEnchantments(context, header, message);

        public void OnMagicPurgeBadEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeBadEnchantmentsEvent message) =>
            Sink.OnMagicPurgeBadEnchantments(context, header, message);

        public void OnAttackDone(in InboundContext context, in GameEventHeader header, in AttackDoneEvent message) =>
            Sink.OnAttackDone(context, header, message);

        public void OnCommenceAttack(in InboundContext context, in GameEventHeader header, in CommenceAttackEvent message) =>
            Sink.OnCommenceAttack(context, header, message);

        public void OnViewContents(in InboundContext context, in GameEventHeader header, in ViewContentsEvent message) =>
            Sink.OnViewContents(context, header, message);

        public void OnCloseGroundContainer(in InboundContext context, in GameEventHeader header, in CloseGroundContainerEvent message) =>
            Sink.OnCloseGroundContainer(context, header, message);

        public void OnApproachVendor(in InboundContext context, in GameEventHeader header, VendorApproachMsg message) =>
            Sink.OnApproachVendor(context, header, message);

        public void OnIdentifyResponse(in InboundContext context, in GameEventHeader header, IdentifyResponseMsg message) =>
            Sink.OnIdentifyResponse(context, header, message);

        // M5 6 w1: the confirmation dialog, the login notices and the property updates.

        public void OnConfirmationRequest(in InboundContext context, in GameEventHeader header, ConfirmationRequestEvent message) =>
            Sink.OnConfirmationRequest(context, header, message);

        public void OnConfirmationDone(in InboundContext context, in GameEventHeader header, in ConfirmationDoneEvent message) =>
            Sink.OnConfirmationDone(context, header, message);

        public void OnTurbineChatChannels(in InboundContext context, in GameEventHeader header, TurbineChatChannelsEvent message) =>
            Sink.OnTurbineChatChannels(context, header, message);

        public void OnCharacterTitle(in InboundContext context, in GameEventHeader header, CharacterTitleEvent message) =>
            Sink.OnCharacterTitle(context, header, message);

        public void OnFriendsListUpdate(in InboundContext context, in GameEventHeader header, FriendsListUpdateEvent message) =>
            Sink.OnFriendsListUpdate(context, header, message);

        public void OnPublicPropertyBool(in InboundContext context, in PublicPropertyUpdate<bool> message) => Sink.OnPublicPropertyBool(context, message);

        public void OnPublicPropertyInt64(in InboundContext context, in PublicPropertyUpdate<long> message) => Sink.OnPublicPropertyInt64(context, message);

        public void OnPublicPropertyFloat(in InboundContext context, in PublicPropertyUpdate<double> message) => Sink.OnPublicPropertyFloat(context, message);

        public void OnPublicPropertyString(in InboundContext context, in PublicPropertyUpdate<string> message) => Sink.OnPublicPropertyString(context, message);

        public void OnPublicPropertyDataId(in InboundContext context, in PublicPropertyUpdate<uint> message) => Sink.OnPublicPropertyDataId(context, message);

        public void OnPrivatePropertyBool(in InboundContext context, in PrivatePropertyUpdate<bool> message) => Sink.OnPrivatePropertyBool(context, message);

        public void OnPrivatePropertyInstanceId(in InboundContext context, in PrivatePropertyUpdate<uint> message) => Sink.OnPrivatePropertyInstanceId(context, message);

        public void OnPrivatePropertyDataId(in InboundContext context, in PrivatePropertyUpdate<uint> message) => Sink.OnPrivatePropertyDataId(context, message);

        public void OnPrivatePosition(in InboundContext context, PrivatePositionUpdate message) => Sink.OnPrivatePosition(context, message);

        // Everything else.

        public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) => Sink.OnUnknown(context, opcode, body);

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason)
        {
            machine.OnMalformedMessage(opcode, reason);
            Sink.OnMalformed(context, opcode, length, reason);
        }
    }
}
