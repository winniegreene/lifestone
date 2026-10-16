using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Dispatch;

/// <summary>
/// <see cref="InboundDispatcher"/> after M3 1.2 s0: each of the 6 GameMessages and 19 game events of the inventory, wield,
/// combat, magic and admin set reaches its own typed callback with the decoded record (and the event's header), is never
/// reported to OnUnknown or OnGameEvent, and a body with a trailing byte is malformed with a reason naming the message.
/// </summary>
public sealed class M3InboundDispatcherTests
{
    private const uint Player = 0x50000001;

    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    /// <summary>One ACE body per M3 code, the callback it must reach and the record it must decode to.</summary>
    public static TheoryData<string, byte[], object> Bodies => new()
    {
        { nameof(IInboundMessageHandler.OnInventoryRemoveObject), AceM3Writers.InventoryRemoveObject(0x80000123), new InventoryRemoveObjectMsg(0x80000123) },
        { nameof(IInboundMessageHandler.OnSetStackSize), AceM3Writers.SetStackSize(2, 0x80000123, 5, 70), new SetStackSizeMsg(2, 0x80000123, 5, 70) },
        { nameof(IInboundMessageHandler.OnPlayerKilled), AceM3Writers.PlayerKilled("It died.", Player, 0x80000456), new PlayerKilledMsg("It died.", Player, 0x80000456) },
        { nameof(IInboundMessageHandler.OnPublicUpdatePropertyInt), AceM3Writers.PublicUpdatePropertyInt(3, 0x80000123, 0x0A, 9), new PublicUpdatePropertyIntMsg(3, 0x80000123, 0x0A, 9) },
        { nameof(IInboundMessageHandler.OnPublicUpdateInstanceId), AceM3Writers.PublicUpdateInstanceId(4, 0x80000123, 3, Player), new PublicUpdateInstanceIdMsg(4, 0x80000123, 3, Player) },
        { nameof(IInboundMessageHandler.OnAdminEnvirons), AceM3Writers.AdminEnvirons(2), new AdminEnvironsMsg(2) },
        { nameof(IInboundMessageHandler.OnInventoryPutObjInContainer), AceM3Writers.PutObjInContainer(Player, 11, 0x80000123, Player, 1, 0), new InventoryPutObjInContainerEvent(0x80000123, Player, 1, 0) },
        { nameof(IInboundMessageHandler.OnWieldObject), AceM3Writers.WieldObject(Player, 12, 0x80000123, 0x100000), new WieldObjectEvent(0x80000123, 0x100000) },
        { nameof(IInboundMessageHandler.OnInventoryPutObjectIn3D), AceM3Writers.PutObjectIn3D(Player, 13, 0x80000123), new InventoryPutObjectIn3DEvent(0x80000123) },
        { nameof(IInboundMessageHandler.OnInventoryServerSaysFailed), AceM3Writers.InventoryServerSaysFailed(Player, 14, 0x80000123, 0x36), new InventoryServerSaysFailedEvent(0x80000123, 0x36) },
        { nameof(IInboundMessageHandler.OnVictimNotification), AceM3Writers.VictimNotification(Player, 15, "You died."), new VictimNotificationEvent("You died.") },
        { nameof(IInboundMessageHandler.OnKillerNotification), AceM3Writers.KillerNotification(Player, 16, "You killed it."), new KillerNotificationEvent("You killed it.") },
        { nameof(IInboundMessageHandler.OnAttackerNotification), AceM3Writers.AttackerNotification(Player, 17, "Drudge", 0x10, 0.5, 6, false, 4), new AttackerNotificationEvent("Drudge", 0x10, 0.5, 6, false, 4) },
        { nameof(IInboundMessageHandler.OnDefenderNotification), AceM3Writers.DefenderNotification(Player, 18, "Drudge", 0x10, 0.5, 6, 2, true, 4), new DefenderNotificationEvent("Drudge", 0x10, 0.5, 6, 2, true, 4) },
        { nameof(IInboundMessageHandler.OnEvasionAttackerNotification), AceM3Writers.EvasionAttackerNotification(Player, 19, "Drudge"), new EvasionAttackerNotificationEvent("Drudge") },
        { nameof(IInboundMessageHandler.OnEvasionDefenderNotification), AceM3Writers.EvasionDefenderNotification(Player, 20, "Drudge"), new EvasionDefenderNotificationEvent("Drudge") },
        { nameof(IInboundMessageHandler.OnMagicUpdateSpell), AceM3Writers.MagicUpdateSpell(Player, 21, 0x0C8B), new MagicUpdateSpellEvent(0x0C8B, 0) },
        { nameof(IInboundMessageHandler.OnMagicUpdateEnchantment), AceM3Writers.MagicUpdateEnchantment(Player, 22, Enchantment), new MagicUpdateEnchantmentEvent(ExpectedEnchantment) },
        { nameof(IInboundMessageHandler.OnMagicRemoveEnchantment), AceM3Writers.MagicRemoveEnchantment(Player, 23, 0x0C8B, 1), new MagicRemoveEnchantmentEvent(0x0C8B, 1) },
        { nameof(IInboundMessageHandler.OnMagicUpdateMultipleEnchantments), AceM3Writers.MagicUpdateMultipleEnchantments(Player, 24, [Enchantment]), new MagicUpdateMultipleEnchantmentsEvent([ExpectedEnchantment]) },
        { nameof(IInboundMessageHandler.OnMagicRemoveMultipleEnchantments), AceM3Writers.MagicRemoveMultipleEnchantments(Player, 25, [(0x0C8B, 1)]), new MagicRemoveMultipleEnchantmentsEvent([new LayeredSpellDesc(0x0C8B, 1)]) },
        { nameof(IInboundMessageHandler.OnMagicPurgeEnchantments), AceM3Writers.MagicPurgeEnchantments(Player, 26), new MagicPurgeEnchantmentsEvent() },
        { nameof(IInboundMessageHandler.OnMagicDispelEnchantment), AceM3Writers.MagicDispelEnchantment(Player, 27, 0x0C8B, 1), new MagicDispelEnchantmentEvent(0x0C8B, 1) },
        { nameof(IInboundMessageHandler.OnMagicDispelMultipleEnchantments), AceM3Writers.MagicDispelMultipleEnchantments(Player, 28, [(0x0C8B, 1)]), new MagicDispelMultipleEnchantmentsEvent([new LayeredSpellDesc(0x0C8B, 1)]) },
        { nameof(IInboundMessageHandler.OnMagicPurgeBadEnchantments), AceM3Writers.MagicPurgeBadEnchantments(Player, 29), new MagicPurgeBadEnchantmentsEvent() },
    };

    private static AceEnchantment Enchantment => new(0x0C8B, 1, 3, 1, 350, -1.5, 3600.0, Player, 0.25f, -666f, 0.0, 0x00000101, 1, 35f, 0x77);

    private static EnchantmentDesc ExpectedEnchantment => new(0x0C8B, 1, 3, 1, 350, -1.5, 3600.0, Player, 0.25f, -666f, 0.0, 0x00000101, 1, 35f, 0x77, EnchantmentBucket.Additive);

    [Fact]
    public void The_table_covers_the_six_messages_and_nineteen_events()
    {
        var rows = Bodies.Select(row => (byte[])row[1]!).ToList();

        Assert.Equal(25, rows.Count);
        Assert.Equal(6, rows.Count(body => BitConverter.ToUInt32(body) != MessageOpcodes.GameEvent));
        Assert.Equal(19, rows.Where(body => BitConverter.ToUInt32(body) == MessageOpcodes.GameEvent).Select(body => BitConverter.ToUInt32(body, 12)).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void Each_code_reaches_its_own_callback_with_its_decoded_record(string callback, byte[] body, object expected)
    {
        var handler = new RecordingHandler();
        var opcode = BitConverter.ToUInt32(body);
        var isEvent = opcode == MessageOpcodes.GameEvent;

        var result = InboundDispatcher.Dispatch(Context, body, handler);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(callback, call.Name);
        Assert.Equal(Context, call.Context);
        Assert.Equal(Normalize(expected), Normalize(call.Message));
        if (isEvent)
        {
            var eventType = BitConverter.ToUInt32(body, 12);
            Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, eventType), result);
            Assert.Equal(new GameEventHeader(Player, BitConverter.ToUInt32(body, 8), eventType), call.Header);
        }
        else
        {
            Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, opcode), result);
        }
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void Each_code_with_a_trailing_byte_is_malformed_and_named(string callback, byte[] body, object expected)
    {
        _ = (callback, expected);
        var handler = new RecordingHandler();
        var opcode = BitConverter.ToUInt32(body);

        var result = InboundDispatcher.Dispatch(Context, [.. body, 0xAA], handler);

        Assert.Equal(DispatchOutcome.Malformed, result.Outcome);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(nameof(IInboundMessageHandler.OnMalformed), call.Name);
        Assert.Equal(body.Length + 1, call.Length);
        Assert.StartsWith(opcode == MessageOpcodes.GameEvent ? $"F7B0 GameEvent 0x{BitConverter.ToUInt32(body, 12):X4} " : $"{opcode:X4} ", call.Reason);
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void Each_code_truncated_by_a_byte_is_malformed_never_unknown(string callback, byte[] body, object expected)
    {
        _ = (callback, expected);
        var handler = new RecordingHandler();

        var result = InboundDispatcher.Dispatch(Context, body[..^1], handler);

        Assert.NotEqual(DispatchOutcome.Unknown, result.Outcome);
        Assert.DoesNotContain(handler.Calls, call => call.Name is nameof(IInboundMessageHandler.OnUnknown) or nameof(IInboundMessageHandler.OnGameEvent));
    }

    [Fact]
    public void Another_game_event_still_reaches_OnGameEvent()
    {
        var handler = new RecordingHandler();

        var result = InboundDispatcher.Dispatch(Context, AceWireWriters.GameEvent(Player, 1, 0x0314, [0, 0, 0, 0]), handler);

        Assert.Equal(DispatchOutcome.Unknown, result.Outcome);
        Assert.Equal(nameof(IInboundMessageHandler.OnGameEvent), Assert.Single(handler.Calls).Name);
    }

    /// <summary>Records holding an ImmutableArray compare by reference, so they are compared by their entries.</summary>
    private static object Normalize(object message) => message switch
    {
        MagicUpdateMultipleEnchantmentsEvent m => "update:" + string.Join("|", m.Enchantments),
        MagicRemoveMultipleEnchantmentsEvent r => "remove:" + string.Join("|", r.Spells),
        MagicDispelMultipleEnchantmentsEvent d => "dispel:" + string.Join("|", d.Spells),
        _ => message,
    };

    /// <summary>Records the M3 callbacks by name, with the decoded record and the event header; the others are not expected.</summary>
    private sealed class RecordingHandler : IInboundMessageHandler
    {
        public List<Call> Calls { get; } = [];

        public void OnInventoryRemoveObject(in InboundContext context, in InventoryRemoveObjectMsg message) => Add(context, message, default);

        public void OnSetStackSize(in InboundContext context, in SetStackSizeMsg message) => Add(context, message, default);

        public void OnPlayerKilled(in InboundContext context, in PlayerKilledMsg message) => Add(context, message, default);

        public void OnPublicUpdatePropertyInt(in InboundContext context, in PublicUpdatePropertyIntMsg message) => Add(context, message, default);

        public void OnPublicUpdateInstanceId(in InboundContext context, in PublicUpdateInstanceIdMsg message) => Add(context, message, default);

        public void OnAdminEnvirons(in InboundContext context, in AdminEnvironsMsg message) => Add(context, message, default);

        public void OnInventoryPutObjInContainer(in InboundContext context, in GameEventHeader header, in InventoryPutObjInContainerEvent message) => Add(context, message, header);

        public void OnWieldObject(in InboundContext context, in GameEventHeader header, in WieldObjectEvent message) => Add(context, message, header);

        public void OnInventoryPutObjectIn3D(in InboundContext context, in GameEventHeader header, in InventoryPutObjectIn3DEvent message) => Add(context, message, header);

        public void OnInventoryServerSaysFailed(in InboundContext context, in GameEventHeader header, in InventoryServerSaysFailedEvent message) => Add(context, message, header);

        public void OnVictimNotification(in InboundContext context, in GameEventHeader header, in VictimNotificationEvent message) => Add(context, message, header);

        public void OnKillerNotification(in InboundContext context, in GameEventHeader header, in KillerNotificationEvent message) => Add(context, message, header);

        public void OnAttackerNotification(in InboundContext context, in GameEventHeader header, in AttackerNotificationEvent message) => Add(context, message, header);

        public void OnDefenderNotification(in InboundContext context, in GameEventHeader header, in DefenderNotificationEvent message) => Add(context, message, header);

        public void OnEvasionAttackerNotification(in InboundContext context, in GameEventHeader header, in EvasionAttackerNotificationEvent message) => Add(context, message, header);

        public void OnEvasionDefenderNotification(in InboundContext context, in GameEventHeader header, in EvasionDefenderNotificationEvent message) => Add(context, message, header);

        public void OnMagicUpdateSpell(in InboundContext context, in GameEventHeader header, in MagicUpdateSpellEvent message) => Add(context, message, header);

        public void OnMagicUpdateEnchantment(in InboundContext context, in GameEventHeader header, in MagicUpdateEnchantmentEvent message) => Add(context, message, header);

        public void OnMagicRemoveEnchantment(in InboundContext context, in GameEventHeader header, in MagicRemoveEnchantmentEvent message) => Add(context, message, header);

        public void OnMagicUpdateMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicUpdateMultipleEnchantmentsEvent message) => Add(context, message, header);

        public void OnMagicRemoveMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicRemoveMultipleEnchantmentsEvent message) => Add(context, message, header);

        public void OnMagicPurgeEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeEnchantmentsEvent message) => Add(context, message, header);

        public void OnMagicDispelEnchantment(in InboundContext context, in GameEventHeader header, in MagicDispelEnchantmentEvent message) => Add(context, message, header);

        public void OnMagicDispelMultipleEnchantments(in InboundContext context, in GameEventHeader header, in MagicDispelMultipleEnchantmentsEvent message) => Add(context, message, header);

        public void OnMagicPurgeBadEnchantments(in InboundContext context, in GameEventHeader header, in MagicPurgeBadEnchantmentsEvent message) => Add(context, message, header);

        public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) =>
            Calls.Add(new Call(nameof(OnGameEvent), context, new object(), header));

        public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) =>
            Calls.Add(new Call(nameof(OnUnknown), context, new object(), default));

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) =>
            Calls.Add(new Call(nameof(OnMalformed), context, new object(), default, length, reason));

        private void Add(in InboundContext context, object message, in GameEventHeader header, [System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
            Calls.Add(new Call(name, context, message, header));
    }

    private sealed record Call(string Name, InboundContext Context, object Message, GameEventHeader Header, int Length = 0, string? Reason = null);
}
