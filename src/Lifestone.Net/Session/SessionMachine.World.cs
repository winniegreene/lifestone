using System.Numerics;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// Entering the world and LoginComplete (m2-plan §3.3 N6; docs/protocol.md §4.3), and the world sends of N7.
/// </summary>
/// <remarks>
/// <para><b>EnterWorld</b> sends F7C8 (<c>Proto_UI::SendEnterWorldRequest</c>, 11.4186 0x00546A00, 11.6096 0x005475C0)
/// and moves to <see cref="SessionPhase.EnteringWorld"/>; the server's F7DF ServerReady
/// (<c>CM_Character::DispatchUI_EnterGame_ServerReady</c>, 11.4186 0x006A0E50, 11.6096 0x006A1C70) is answered with F657
/// {GUID, the F658 account string} (<c>Proto_UI::SendEnterWorld</c>, 11.4186 0x00546BC0, 11.6096 0x00547780); F746
/// PlayerCreate sets <see cref="PlayerGuid"/> and the phase becomes <see cref="SessionPhase.InWorld"/>. A CharacterError
/// 0x0D (a character still in the world after a crash) is retried by the policy: F7C8 again,
/// <see cref="LoginRetryPolicy.Delay"/> later. Any other code returns to character select with
/// <see cref="SessionEventKind.CharacterErrorReceived"/>.</para>
/// <para><b>LoginComplete</b> (GameAction 0x00A1, <c>CM_Character::Event_LoginCompleteNotification</c>, 11.4186
/// 0x006A1480, 11.6096 0x006A22A0) is never sent by the session itself: the portal-space machine decides when the
/// player leaves the tunnel (m2 §4.5). Sending it again with no PlayerCreate, self ObjectCreate or PlayerTeleport in
/// between still sends it, and logs a warning (<see cref="SessionWarning.LoginCompleteRepeated"/>).</para>
/// <para><b>The GameAction sequence</b> starts again from 1 at each world entry and after each logoff: retail's
/// <c>CPlayerSystem::LogOnCharacter</c> (11.4186 0x0055F890, 11.6096 0x00560600) and
/// <c>CPlayerSystem::ExecuteLogOff</c> (11.4186 0x0055D780, 11.6096 0x0055E4A0) call
/// <c>ClientNet::ExitWorldDisconnect</c> (11.4186 0x00541E00, 11.6096 0x005429E0), which resets the counter with
/// <c>Proto_UI::SetEventCounter(0)</c> (11.4186 0x00546800, 11.6096 0x005473C0). ACE does not check it.</para>
/// <para>Game actions go on the weenie queue (3), as retail's <c>Proto_UI::SendToWeenie</c> (11.4186 0x00546810,
/// 11.6096 0x005473D0) sends them; the login messages on the login queue (4, <c>Proto_UI::SendToLogon</c>, 11.4186
/// 0x005468F0, 11.6096 0x005474B0); F7D9 on the control queue (2, <c>Proto_UI::SendToControl</c>, 11.4186 0x00546880,
/// 11.6096 0x00547440); F7E6 on the database queue (5, <c>Proto_UI::SendToDatabase</c>, 11.4186 0x00546960, 11.6096
/// 0x00547520). ACE ignores the queue of client fragments.</para>
/// <para><b>The world sends</b> (N7) need <see cref="SessionPhase.InWorld"/>: MoveToState, AutonomousPosition and Jump
/// (<see cref="MovementActionCodecs"/>), Use and QueryHealth (<see cref="ObjectActionCodecs"/>), Talk and Tell
/// (<see cref="ChatActionCodecs"/>); each codec cites the retail sender it follows. Each takes the next GameAction
/// sequence and goes on the weenie queue, as every one of those senders does (<c>Proto_UI::SendToWeenie</c>, above).
/// Input a codec refuses (a RawMotionState with more than 31 actions, a non-ASCII Tell, a string too long for its count)
/// throws <see cref="ArgumentException"/> before anything is sent and uses no sequence. The session never decides when
/// to send movement: the state track's movement channel does.</para>
/// </remarks>
internal sealed partial class SessionMachine
{
    private EnterWorldRequest? _enterWorld;
    private bool _loginCompleteArmed;

    public OutboundTicket EnterWorld(uint guid)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.CharacterSelect, "EnterWorld");
        if (IndexOf(CharacterList!, guid) < 0)
        {
            throw new ArgumentException($"character 0x{guid:X8} is not in the character list", nameof(guid));
        }

        RequireNoOperation("EnterWorld");
        _connection!.NextActionSequence = GameActionEnvelope.FirstSequence;
        var ticket = SendEnterWorldRequest();
        _enterWorld = new EnterWorldRequest(guid, ticket.Timestamp) { Attempt = 1 };
        SetPhase(SessionPhase.EnteringWorld);
        Log(NetLogLevel.Info, $"enter world: F7C8 sent for 0x{guid:X8}");
        return ticket;
    }

    public OutboundTicket SendLoginComplete()
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendLoginComplete");
        if (!_loginCompleteArmed)
        {
            const string Text = "LoginComplete sent again with no PlayerCreate, self ObjectCreate or PlayerTeleport since the last one";
            Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.LoginCompleteRepeated, Text));
            Log(NetLogLevel.Warning, "world: " + Text);
        }

        _loginCompleteArmed = false;
        var connection = _connection!;
        var writer = BeginMessage();
        LoginCompleteCodec.Write(writer, connection.NextActionSequence++);
        return SendMessage(writer, MessageQueue.Weenie);
    }

    public OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendMoveToState");
        var connection = _connection!;
        var writer = BeginMessage();
        MovementActionCodecs.WriteMoveToState(writer, connection.NextActionSequence, state, position, sequences, contact, standingLongJump);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendAutonomousPosition");
        var connection = _connection!;
        var writer = BeginMessage();
        MovementActionCodecs.WriteAutonomousPosition(writer, connection.NextActionSequence, position, sequences, contact);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendJump");
        var connection = _connection!;
        var writer = BeginMessage();
        MovementActionCodecs.WriteJump(writer, connection.NextActionSequence, extent, velocity, position, sequences);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendUse(uint guid)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendUse");
        var connection = _connection!;
        var writer = BeginMessage();
        ObjectActionCodecs.WriteUse(writer, connection.NextActionSequence, guid);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendQueryHealth(uint guid)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendQueryHealth");
        var connection = _connection!;
        var writer = BeginMessage();
        ObjectActionCodecs.WriteQueryHealth(writer, connection.NextActionSequence, guid);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendRaiseAttribute(uint attribute, uint xp)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendRaiseAttribute");
        var connection = _connection!;
        var writer = BeginMessage();
        StatActionCodecs.WriteRaiseAttribute(writer, connection.NextActionSequence, attribute, xp);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendRaiseVital(uint vital, uint xp)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendRaiseVital");
        var connection = _connection!;
        var writer = BeginMessage();
        StatActionCodecs.WriteRaiseVital(writer, connection.NextActionSequence, vital, xp);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendRaiseSkill(uint skill, uint xp)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendRaiseSkill");
        var connection = _connection!;
        var writer = BeginMessage();
        StatActionCodecs.WriteRaiseSkill(writer, connection.NextActionSequence, skill, xp);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendTrainSkill(uint skill, int credits)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendTrainSkill");
        var connection = _connection!;
        var writer = BeginMessage();
        StatActionCodecs.WriteTrainSkill(writer, connection.NextActionSequence, skill, credits);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendCastUntargetedSpell(uint spellId)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendCastUntargetedSpell");
        var connection = _connection!;
        var writer = BeginMessage();
        MagicActionCodecs.WriteCastUntargetedSpell(writer, connection.NextActionSequence, spellId);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendCastTargetedSpell(uint target, uint spellId)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendCastTargetedSpell");
        var connection = _connection!;
        var writer = BeginMessage();
        MagicActionCodecs.WriteCastTargetedSpell(writer, connection.NextActionSequence, target, spellId);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendChangeCombatMode(uint mode)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendChangeCombatMode");
        var connection = _connection!;
        var writer = BeginMessage();
        MagicActionCodecs.WriteChangeCombatMode(writer, connection.NextActionSequence, mode);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendGetAndWieldItem(uint item, uint equipMask)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendGetAndWieldItem");
        var connection = _connection!;
        var writer = BeginMessage();
        InventoryActionCodecs.WriteGetAndWieldItem(writer, connection.NextActionSequence, item, equipMask);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendPutItemInContainer(uint item, uint container, uint placement)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendPutItemInContainer");
        var connection = _connection!;
        var writer = BeginMessage();
        InventoryActionCodecs.WritePutItemInContainer(writer, connection.NextActionSequence, item, container, placement);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendStackableMerge(uint mergeFrom, uint mergeTo, int amount)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendStackableMerge");
        var connection = _connection!;
        var writer = BeginMessage();
        InventoryActionCodecs.WriteStackableMerge(writer, connection.NextActionSequence, mergeFrom, mergeTo, amount);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendTalk(ReadOnlySpan<char> text)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendTalk");
        var connection = _connection!;
        var writer = BeginMessage();
        ChatActionCodecs.WriteTalk(writer, connection.NextActionSequence, text);
        return SendAction(connection, writer);
    }

    public OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text)
    {
        EnterGameThread(capture: false);
        RequirePhase(SessionPhase.InWorld, "SendTell");
        var connection = _connection!;
        var writer = BeginMessage();
        ChatActionCodecs.WriteTell(writer, connection.NextActionSequence, target, text);
        return SendAction(connection, writer);
    }

    /// <summary>
    /// Sends a game action the codec has written with the connection's next action sequence, on the weenie queue, and
    /// only then takes the sequence: a codec that refused its input has written nothing that is sent and used no
    /// sequence, as retail hands a failed send's counter back (<c>Proto_UI::UICounterFailedSend</c>, 11.4186 0x005467F0,
    /// 11.6096 0x005473B0, a predicted row of the symbol table).
    /// </summary>
    private OutboundTicket SendAction(SessionConnection connection, PacketWriter writer)
    {
        var ticket = SendMessage(writer, MessageQueue.Weenie);
        connection.NextActionSequence++;
        return ticket;
    }

    private OutboundTicket SendEnterWorldRequest()
    {
        var writer = BeginMessage();
        CharacterEnterWorldCodec.WriteRequest(writer);
        return SendMessage(writer, MessageQueue.Login);
    }

    private void OnEnterWorldServerReadyMessage()
    {
        if (Phase != SessionPhase.EnteringWorld || _enterWorld is not { EnterWorldSent: false, RetryAt: null } request)
        {
            Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.UnexpectedMessage, "F7DF ServerReady with no enter-world request waiting"));
            return;
        }

        var writer = BeginMessage();
        CharacterEnterWorldCodec.Write(writer, request.Guid, CharacterList!.Account);
        SendMessage(writer, MessageQueue.Login);
        _enterWorld = request with { EnterWorldSent = true };
        Log(NetLogLevel.Info, $"enter world: F657 sent for 0x{request.Guid:X8}");
    }

    private void OnPlayerCreateMessage(uint guid)
    {
        if (Phase != SessionPhase.EnteringWorld)
        {
            Raise(SessionEvent.Warned(_time.GetTimestamp(), Phase, SessionWarning.UnexpectedMessage, $"F746 PlayerCreate 0x{guid:X8} outside entering the world"));
            return;
        }

        if (_enterWorld is { } request && request.Guid != guid)
        {
            Log(NetLogLevel.Warning, $"enter world: asked for 0x{request.Guid:X8}, the server created 0x{guid:X8}");
        }

        _enterWorld = null;
        PlayerGuid = guid;
        _loginCompleteArmed = true;
        SetPhase(SessionPhase.InWorld);
        Log(NetLogLevel.Info, $"entered the world as 0x{guid:X8}");
    }

    private void OnEnterWorldError(uint code, string name)
    {
        if (IsSessionEnding(code))
        {
            Fail(SessionFailureKind.CharacterError, $"the server ended the session with CharacterError 0x{code:X2} ({name})", code);
            return;
        }

        if (_enterWorld is { } request && _options.Retry.RetryableCharacterErrors.Contains(code) && request.Attempt < _options.Retry.Attempts)
        {
            var delay = _options.Retry.Delay;
            _enterWorld = request with { Attempt = request.Attempt + 1, EnterWorldSent = false, RetryAt = _time.GetTimestamp() + Ticks(delay) };
            Raise(SessionEvent.RetryScheduled(_time.GetTimestamp(), Phase, request.Attempt + 1, delay, code, $"the server refused to enter the world with {name}"));
            Log(NetLogLevel.Warning,
                $"enter world: CharacterError 0x{code:X2} ({name}); attempt {request.Attempt + 1} of {_options.Retry.Attempts} in {delay.TotalSeconds:0.#} s");
            return;
        }

        _enterWorld = null;
        SetPhase(SessionPhase.CharacterSelect);
        Raise(SessionEvent.CharacterErrorReceived(_time.GetTimestamp(), Phase, code));
        Log(NetLogLevel.Warning, $"enter world: refused with CharacterError 0x{code:X2} ({name}); back to character select");
    }

    /// <summary>A PlayerCreate, a self ObjectCreate or a PlayerTeleport: the next LoginComplete is expected.</summary>
    private void ArmLoginComplete() => _loginCompleteArmed = true;

    /// <summary>The enter-world retry and cap, the logoff cap and the character operation's wait.</summary>
    private void RunWorldTimers()
    {
        var now = _time.GetTimestamp();
        if (Phase == SessionPhase.EnteringWorld && _enterWorld is { } request)
        {
            if (_time.GetElapsedTime(request.StartedAt, now) >= _options.Timeouts.EnterWorld)
            {
                Fail(SessionFailureKind.EnterWorldTimeout,
                    $"no PlayerCreate within {_options.Timeouts.EnterWorld.TotalSeconds:0.#} s of entering the world (attempt {request.Attempt})");
                return;
            }

            if (request.RetryAt is { } due && now >= due && _connection?.Transport is not null)
            {
                _enterWorld = request with { RetryAt = null };
                SendEnterWorldRequest();
                Log(NetLogLevel.Info, $"enter world: F7C8 sent again for 0x{request.Guid:X8} (attempt {request.Attempt})");
            }
        }

        RunLogOffTimer(now);
        RunCharacterOperationTimer();
    }

    /// <summary>The enter-world request waiting for PlayerCreate.</summary>
    private sealed record EnterWorldRequest(uint Guid, long StartedAt)
    {
        /// <summary>Which try this is (1 for the first).</summary>
        public int Attempt { get; init; }

        /// <summary>True once F657 went out for this try.</summary>
        public bool EnterWorldSent { get; init; }

        /// <summary>When the next F7C8 is due after a retryable error; null otherwise.</summary>
        public long? RetryAt { get; init; }
    }
}
