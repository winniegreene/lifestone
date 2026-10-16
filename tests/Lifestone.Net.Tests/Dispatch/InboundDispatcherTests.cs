using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Dispatch;

/// <summary>
/// <see cref="InboundDispatcher"/> after N3, N7 and N10: the login, character-select, world, chat and PrivateUpdate
/// messages and the decoded game events reach their typed callbacks, every other game event reaches OnGameEvent with its header and
/// payload, anything else reaches OnUnknown whole, and a body that does not parse exactly reaches OnMalformed with a
/// reason naming the message. It never throws on input; a handler's
/// own exception propagates. A handler that implements nothing compiles (every member has a default).
/// </summary>
public sealed class InboundDispatcherTests
{
    private static readonly InboundContext Context = new(7, MessageQueue.Ui, 1234);

    /// <summary>One ACE body per decoded opcode, and the callback it must reach.</summary>
    public static TheoryData<string, byte[]> DecodedBodies => new()
    {
        { nameof(IInboundMessageHandler.OnCharacterList), AceWireWriters.CharacterList([new AceCharacter(0x50000001, "Tester")], "lifestone") },
        { nameof(IInboundMessageHandler.OnServerName), AceWireWriters.ServerName("Lifestone") },
        { nameof(IInboundMessageHandler.OnDddInterrogation), AceWireWriters.DddInterrogation() },
        { nameof(IInboundMessageHandler.OnDddBegin), AceWireWriters.DddBegin(10, []) },
        { nameof(IInboundMessageHandler.OnDddEnd), AceWireWriters.DddEnd() },
        { nameof(IInboundMessageHandler.OnCharGenResponse), AceWireWriters.CharacterCreateResponse(3) },
        { nameof(IInboundMessageHandler.OnCharacterDeleteAck), AceWireWriters.CharacterDeleteAck() },
        { nameof(IInboundMessageHandler.OnCharacterError), AceWireWriters.CharacterError(0x15) },
        { nameof(IInboundMessageHandler.OnAccountBoot), AceWireWriters.AccountBoot(" because") },
        { nameof(IInboundMessageHandler.OnAccountBanned), AceWireWriters.AccountBanned(60) },
        { nameof(IInboundMessageHandler.OnEnterWorldServerReady), AceWireWriters.EnterWorldServerReady() },
        { nameof(IInboundMessageHandler.OnPlayerCreate), AceWireWriters.PlayerCreate(0x50000001) },
        { nameof(IInboundMessageHandler.OnCharacterLogOff), AceWireWriters.CharacterLogOff() },
        { nameof(IInboundMessageHandler.OnServerMessage), AceWireWriters.ServerMessage("#lifestone/1 hello", 0) },
    };

    /// <summary>One ACE body per world and chat opcode N7 decodes, and the callback it must reach.</summary>
    public static TheoryData<string, byte[]> WorldBodies => new()
    {
        { nameof(IInboundMessageHandler.OnCreateObject), AceWireWriters.ObjectCreate(new AceCreateObject { Guid = 0x80000001, Name = "Thing" }) },
        { nameof(IInboundMessageHandler.OnCreateObject), AceWireWriters.UpdateObject(new AceCreateObject { Guid = 0x80000001, Name = "Thing" }) },
        { nameof(IInboundMessageHandler.OnDeleteObject), AceWireWriters.ObjectDelete(0x80000001, 1) },
        { nameof(IInboundMessageHandler.OnUpdatePosition), AceWireWriters.UpdatePosition(0x50000001, new AcePosition(0xA9B40019, 1, 2, 3), true, default) },
        { nameof(IInboundMessageHandler.OnUpdateMotion), AceWireWriters.UpdateMotion(0x50000001, 0, 1, 1, true, new AceMovementData { Forward = 0x0007 }) },
        { nameof(IInboundMessageHandler.OnVectorUpdate), AceWireWriters.VectorUpdate(0x80000001, (1, 0, 0), (0, 0, 0), 0, 1) },
        { nameof(IInboundMessageHandler.OnSetState), AceWireWriters.SetState(0x50000001, 0x408, 0, 1) },
        { nameof(IInboundMessageHandler.OnPlayerTeleport), AceWireWriters.PlayerTeleport(2) },
        { nameof(IInboundMessageHandler.OnParentEvent), AceWireWriters.ParentEvent(0x50000001, 0x80000001, 1, 1, 0, 1) },
        { nameof(IInboundMessageHandler.OnPickupEvent), AceWireWriters.PickupEvent(0x80000001, 0, 1) },
        { nameof(IInboundMessageHandler.OnObjDescEvent), AceWireWriters.ObjDescEvent(0x50000001, new AceModelData(0, [], [], []), 0, 1) },
        { nameof(IInboundMessageHandler.OnSound), AceWireWriters.Sound(0x50000001, 0x77) },
        { nameof(IInboundMessageHandler.OnPlayEffect), AceWireWriters.PlayEffect(0x50000001, 0x8A) },
        { nameof(IInboundMessageHandler.OnSpeech), AceWireWriters.HearSpeech("hi", "Crier", 0x7A9B4001, 2) },
        { nameof(IInboundMessageHandler.OnSpeech), AceWireWriters.HearRangedSpeech("hi", "Crier", 0x7A9B4001, 30f, 2) },
        { nameof(IInboundMessageHandler.OnEmote), AceWireWriters.EmoteText(0x50000001, "Hero", "waves.") },
        { nameof(IInboundMessageHandler.OnEmote), AceWireWriters.SoulEmote(0x50000001, "Hero", "bows.") },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdatePropertyInt(1, 0x19, 2) },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdatePropertyInt64(2, 0x01, 1000) },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdateSkill(3, new AceSkill(0x06, 1, 2, 10, 10)) },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdateAttribute(4, 1, 1, 100, 110) },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdateVital(5, 1, 0, 0, 0, 30) },
        { nameof(IInboundMessageHandler.OnPrivateUpdate), AceWireWriters.PrivateUpdateAttribute2ndLevel(6, 2, 29) },
    };

    /// <summary>One ACE event per game event N7 and N10 decode, and the callback it must reach.</summary>
    public static TheoryData<string, byte[]> EventBodies => new()
    {
        { nameof(IInboundMessageHandler.OnTell), AceWireWriters.TellEvent(0x50000001, 2, "psst", "Friend", 0x50000002, 0x50000001, 3) },
        { nameof(IInboundMessageHandler.OnPopup), AceWireWriters.PopupString(0x50000001, 3, "Welcome!") },
        { nameof(IInboundMessageHandler.OnWeenieError), AceWireWriters.WeenieError(0x50000001, 4, 0x25) },
        { nameof(IInboundMessageHandler.OnWeenieError), AceWireWriters.WeenieErrorWithString(0x50000001, 5, 0x518, "away") },
        { nameof(IInboundMessageHandler.OnTransientString), AceWireWriters.TransientString(0x50000001, 6, "busy") },
        { nameof(IInboundMessageHandler.OnUseDone), AceWireWriters.UseDone(0x50000001, 7) },
        { nameof(IInboundMessageHandler.OnUpdateHealth), AceWireWriters.UpdateHealth(0x50000001, 8, 0x80000001, 0.5f) },
        { nameof(IInboundMessageHandler.OnPlayerDescription), AceWireWriters.PlayerDescription(0x50000001, 9, AcePlayerDescription.NewCharacter("Tester")) },
    };

    [Theory]
    [MemberData(nameof(WorldBodies))]
    public void Each_world_and_chat_message_reaches_its_own_callback(string callback, byte[] body)
    {
        var handler = new RecordingHandler();

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, BitConverter.ToUInt32(body)), result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal((callback, Context), (call.Name, call.Context));

        var malformed = new RecordingHandler();
        Assert.Equal(DispatchOutcome.Malformed, InboundDispatcher.Dispatch(Context, [.. body, 0xAA], malformed).Outcome);
        Assert.StartsWith($"{BitConverter.ToUInt32(body):X4} ", Assert.Single(malformed.Calls).Reason);
    }

    [Fact]
    public void UpdateObject_is_flagged_as_an_update()
    {
        var handler = new RecordingHandler();
        InboundDispatcher.Dispatch(Context, AceWireWriters.ObjectCreate(new AceCreateObject { Guid = 1 }), handler);
        InboundDispatcher.Dispatch(Context, AceWireWriters.UpdateObject(new AceCreateObject { Guid = 1 }), handler);
        Assert.Equal(new[] { false, true }, handler.CreateUpdates);
    }

    [Theory]
    [MemberData(nameof(EventBodies))]
    public void Each_decoded_game_event_reaches_its_own_callback_with_its_header(string callback, byte[] body)
    {
        var handler = new RecordingHandler();
        var eventType = BitConverter.ToUInt32(body, 12);

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, eventType), result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(callback, call.Name);
        Assert.Equal(new GameEventHeader(0x50000001, BitConverter.ToUInt32(body, 8), eventType), call.Header);

        var malformed = new RecordingHandler();
        Assert.Equal(new DispatchResult(DispatchOutcome.Malformed, MessageOpcodes.GameEvent, eventType), InboundDispatcher.Dispatch(Context, [.. body, 0xAA], malformed));
        var reason = Assert.Single(malformed.Calls);
        Assert.Equal((nameof(IInboundMessageHandler.OnMalformed), MessageOpcodes.GameEvent), (reason.Name, reason.Opcode));
        Assert.StartsWith($"F7B0 GameEvent 0x{eventType:X4} ", reason.Reason);
    }

    [Theory]
    [MemberData(nameof(DecodedBodies))]
    public void Each_login_message_reaches_its_own_callback(string callback, byte[] body)
    {
        var handler = new RecordingHandler();

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(DispatchOutcome.Decoded, result.Outcome);
        Assert.Equal(BitConverter.ToUInt32(body), result.Opcode);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(callback, call.Name);
        Assert.Equal(Context, call.Context);
    }

    [Theory]
    [MemberData(nameof(DecodedBodies))]
    public void Each_login_message_with_a_trailing_byte_is_malformed_and_named(string callback, byte[] body)
    {
        _ = callback;
        var handler = new RecordingHandler();

        var result = InboundDispatcher.Dispatch(Context, [.. body, 0xAA], handler);

        Assert.Equal(DispatchOutcome.Malformed, result.Outcome);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(nameof(IInboundMessageHandler.OnMalformed), call.Name);
        Assert.Equal(body.Length + 1, call.Length);
        Assert.StartsWith($"{BitConverter.ToUInt32(body):X4} ", call.Reason);
    }

    [Fact]
    public void Decoded_records_carry_the_decoded_fields()
    {
        var handler = new RecordingHandler();
        InboundDispatcher.Dispatch(Context, AceWireWriters.CharacterList([new AceCharacter(0x50000001, "Tester")], "lifestone"), handler);
        InboundDispatcher.Dispatch(Context, AceWireWriters.CharacterError(0x0F), handler);
        InboundDispatcher.Dispatch(Context, AceWireWriters.DddInterrogation(allowHighRes: true), handler);

        Assert.Equal("lifestone", handler.CharacterList!.Account);
        Assert.Equal(CharacterErrorCode.EnterGameCharacterNotOwned, handler.CharacterError!.Value.Known);
        Assert.True(handler.Interrogation!.AllowsHighRes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_body_shorter_than_an_opcode_is_malformed(int length)
    {
        var handler = new RecordingHandler();
        var result = InboundDispatcher.Dispatch(Context, new byte[length], handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Malformed, 0), result);
        var malformed = Assert.Single(handler.Calls);
        Assert.Equal((nameof(IInboundMessageHandler.OnMalformed), Context, 0u, length), (malformed.Name, malformed.Context, malformed.Opcode, malformed.Length));
    }

    [Fact]
    public void A_truncated_character_list_is_malformed_with_its_opcode()
    {
        var handler = new RecordingHandler();
        byte[] body = [0x58, 0xF6, 0x00, 0x00, 0xAA, 0xBB];

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Malformed, MessageOpcodes.CharacterList), result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal((nameof(IInboundMessageHandler.OnMalformed), MessageOpcodes.CharacterList, 6), (call.Name, call.Opcode, call.Length));
        Assert.StartsWith("F658 CharacterList", call.Reason);
    }

    [Fact]
    public void An_undecoded_message_reaches_OnUnknown_with_its_opcode_and_whole_body()
    {
        var handler = new RecordingHandler();
        byte[] body = [0x54, 0xF7, 0x00, 0x00, 0xAA, 0xBB];  // F754 PlayScript, which Lifestone does not decode (EA60 AdminEnvirons is decoded since M3 1.2 s0)

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Unknown, 0xF754), result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal((nameof(IInboundMessageHandler.OnUnknown), 0xF754u), (call.Name, call.Opcode));
        Assert.Equal(body, call.Bytes);
    }

    [Fact]
    public void A_truncated_world_message_is_malformed_not_unknown()
    {
        var handler = new RecordingHandler();
        byte[] body = [0x45, 0xF7, 0x00, 0x00, 0xAA, 0xBB];

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Malformed, MessageOpcodes.ObjectCreate), result);
        Assert.StartsWith("F745 ObjectCreate", Assert.Single(handler.Calls).Reason);
    }

    [Fact]
    public void A_client_to_server_opcode_from_the_server_is_unknown()
    {
        var handler = new RecordingHandler();
        var result = InboundDispatcher.Dispatch(Context, [0xE6, 0xF7, 0x00, 0x00], handler);
        Assert.Equal(DispatchOutcome.Unknown, result.Outcome);
        Assert.Equal(nameof(IInboundMessageHandler.OnUnknown), Assert.Single(handler.Calls).Name);
    }

    [Fact]
    public void Every_undecoded_game_event_reaches_OnGameEvent_with_its_header_and_payload()
    {
        var handler = new RecordingHandler();
        const uint characterTitle = 0x0029;  // ACE GameEventType.CharacterTitle, which Lifestone does not decode
        var body = AceWireWriters.GameEvent(0x50000001, 9, characterTitle, [1, 2, 3, 4, 5]);

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Unknown, MessageOpcodes.GameEvent, characterTitle), result);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(nameof(IInboundMessageHandler.OnGameEvent), call.Name);
        Assert.Equal(new GameEventHeader(0x50000001, 9, characterTitle), call.Header);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, call.Bytes);
    }

    [Fact]
    public void A_game_event_with_an_empty_payload_is_still_an_event()
    {
        var handler = new RecordingHandler();
        var result = InboundDispatcher.Dispatch(Context, AceWireWriters.PingResponse(0, 1), handler);
        Assert.Equal(0x01EAu, result.EventType);
        Assert.Empty(Assert.Single(handler.Calls).Bytes);
    }

    [Fact]
    public void A_game_event_shorter_than_its_header_is_malformed()
    {
        var handler = new RecordingHandler();
        var body = AceWireWriters.GameEvent(1, 2, 3, []);
        var result = InboundDispatcher.Dispatch(Context, body.AsSpan(0, 15), handler);
        Assert.Equal(new DispatchResult(DispatchOutcome.Malformed, MessageOpcodes.GameEvent), result);
    }

    [Fact]
    public void A_handlers_own_exception_propagates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InboundDispatcher.Dispatch(Context, AceWireWriters.CharacterError(1), new ThrowingHandler()));
    }

    [Fact]
    public void A_handler_that_implements_nothing_is_accepted()
    {
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceWireWriters.DddEnd(), new EmptyHandler()).Outcome);
        Assert.Equal(DispatchOutcome.Unknown, InboundDispatcher.Dispatch(Context, AceWireWriters.PingResponse(0, 1), new EmptyHandler()).Outcome);
        Assert.Throws<ArgumentNullException>(() => InboundDispatcher.Dispatch(Context, [], null!));
    }

    private sealed class EmptyHandler : IInboundMessageHandler;

    private sealed class ThrowingHandler : IInboundMessageHandler
    {
        public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message) => throw new InvalidOperationException("handler");
    }

    /// <summary>Records every call by name; keeps a few decoded records for field checks.</summary>
    private sealed class RecordingHandler : IInboundMessageHandler
    {
        public List<Call> Calls { get; } = [];

        public CharacterListMsg? CharacterList { get; private set; }

        public CharacterErrorMsg? CharacterError { get; private set; }

        public DddInterrogationMsg? Interrogation { get; private set; }

        public void OnCharacterList(in InboundContext context, CharacterListMsg message)
        {
            CharacterList = message;
            Record(context);
        }

        public void OnServerName(in InboundContext context, in ServerNameMsg message) => Record(context);

        public void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message)
        {
            Interrogation = message;
            Record(context);
        }

        public void OnDddBegin(in InboundContext context, in DddBeginMsg message) => Record(context);

        public void OnDddEnd(in InboundContext context) => Record(context);

        public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message) => Record(context);

        public void OnCharacterDeleteAck(in InboundContext context) => Record(context);

        public void OnCharacterError(in InboundContext context, in CharacterErrorMsg message)
        {
            CharacterError = message;
            Record(context);
        }

        public void OnAccountBoot(in InboundContext context, in AccountBootMsg message) => Record(context);

        public void OnAccountBanned(in InboundContext context, in AccountBannedMsg message) => Record(context);

        public void OnEnterWorldServerReady(in InboundContext context) => Record(context);

        public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message) => Record(context);

        public void OnCharacterLogOff(in InboundContext context) => Record(context);

        public void OnServerMessage(in InboundContext context, in ServerMessageMsg message) => Record(context);

        public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) =>
            Calls.Add(new Call(nameof(OnGameEvent), context, MessageOpcodes.GameEvent, payload.Length, null, payload.ToArray(), header));

        public List<bool> CreateUpdates { get; } = [];

        public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate)
        {
            CreateUpdates.Add(isUpdate);
            Record(context);
        }

        public void OnDeleteObject(in InboundContext context, in DeleteObjectMsg message) => Record(context);

        public void OnUpdatePosition(in InboundContext context, in UpdatePositionMsg message) => Record(context);

        public void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message) => Record(context);

        public void OnVectorUpdate(in InboundContext context, in VectorUpdateMsg message) => Record(context);

        public void OnSetState(in InboundContext context, in SetStateMsg message) => Record(context);

        public void OnPlayerTeleport(in InboundContext context, in PlayerTeleportMsg message) => Record(context);

        public void OnParentEvent(in InboundContext context, in ParentEventMsg message) => Record(context);

        public void OnPickupEvent(in InboundContext context, in PickupEventMsg message) => Record(context);

        public void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message) => Record(context);

        public void OnSound(in InboundContext context, in SoundMsg message) => Record(context);

        public void OnPlayEffect(in InboundContext context, in PlayEffectMsg message) => Record(context);

        public void OnSpeech(in InboundContext context, in SpeechMsg message) => Record(context);

        public void OnEmote(in InboundContext context, in EmoteMsg message) => Record(context);

        public void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message) => RecordEvent(context, header);

        public void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message) => RecordEvent(context, header);

        public void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message) => RecordEvent(context, header);

        public void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message) => RecordEvent(context, header);

        public void OnUseDone(in InboundContext context, in GameEventHeader header, in UseDoneEvent message) => RecordEvent(context, header);

        public void OnUpdateHealth(in InboundContext context, in GameEventHeader header, in UpdateHealthEvent message) => RecordEvent(context, header);

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message) => RecordEvent(context, header);

        public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message) => Record(context);

        public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) =>
            Calls.Add(new Call(nameof(OnUnknown), context, opcode, body.Length, null, body.ToArray(), default));

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) =>
            Calls.Add(new Call(nameof(OnMalformed), context, opcode, length, reason, [], default));

        private void Record(in InboundContext context, [System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
            Calls.Add(new Call(name, context, 0, 0, null, [], default));

        private void RecordEvent(in InboundContext context, in GameEventHeader header, [System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
            Calls.Add(new Call(name, context, MessageOpcodes.GameEvent, 0, null, [], header));
    }

    private sealed record Call(string Name, InboundContext Context, uint Opcode, int Length, string? Reason, byte[] Bytes, GameEventHeader Header);
}
