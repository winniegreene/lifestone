using System.Buffers.Binary;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Fuzz;

/// <summary>
/// The dispatcher and every codec it calls never throw on input (m2-plan §3.3 N3, extended by N7 to every world, chat
/// and event codec and by N10 to PlayerDescription and the PrivateUpdate messages): for every opcode and game event the dispatcher decodes, every truncation of a valid body and
/// seeded random bit flips end as a decoded, unknown or malformed message, never an exception. 10,000 flips by default;
/// 1,000,000 at <c>Tier=Soak</c>.
/// </summary>
public sealed class MessageBodyFuzzTests
{
    private const int DefaultFlips = 10_000;

    private const int SoakFlips = 1_000_000;

    private static readonly InboundContext Context = new(1, MessageQueue.Ui, 0);

    /// <summary>Valid bodies covering every decoded opcode and each optional branch.</summary>
    private static readonly byte[][] Samples =
    [
        AceWireWriters.CharacterList([new AceCharacter(0x50000001, "Tester"), new AceCharacter(0x50000002, "Second", 1)], "lifestone"),
        AceWireWriters.CharacterList([], "a"),
        AceWireWriters.ServerName("Lifestone", 1, 1000),
        AceWireWriters.DddInterrogation(),
        AceWireWriters.DddInterrogation(allowHighRes: true),
        AceWireWriters.DddBegin(99, [new AceMissingIteration(0, 1, 2073, [1, 2, 3], [4])]),
        AceWireWriters.DddEnd(),
        AceWireWriters.CharacterCreateResponse(1, 0x50000003, "Created"),
        AceWireWriters.CharacterCreateResponse(3),
        AceWireWriters.CharacterDeleteAck(),
        AceWireWriters.CharacterError(0x15),
        AceWireWriters.AccountBoot(),
        AceWireWriters.AccountBoot(" because the reason"),
        AceWireWriters.AccountBanned(60),
        AceWireWriters.AccountBanned(60, " - reason"),
        AceWireWriters.EnterWorldServerReady(),
        AceWireWriters.PlayerCreate(0x50000001),
        AceWireWriters.CharacterLogOff(),
        AceWireWriters.ServerMessage("#lifestone/1 status", 0),
        AceWireWriters.GameEvent(0x50000001, 3, 0x0314, [1, 2, 3, 4, 5, 6, 7, 8]),  // ContractTrackerTable: an undecoded event

        // World (N7).
        AceWireWriters.ObjectCreate(Messages.World.CreateObjectCodecTests.Player),
        AceWireWriters.ObjectCreate(new AceCreateObject
        {
            Guid = 0x80000123,
            AnimationFrame = 0x65,
            Parent = (0x50000001, 1),
            Children = [(0x80000124, 2)],
            Friction = 0.5f,
            Elasticity = 0.1f,
            Translucency = 0.25f,
            Velocity = (1, 2, 3),
            Acceleration = (0, 0, -9.8f),
            Omega = (0, 0, 1),
            DefaultScript = 7,
            DefaultScriptIntensity = 0.5f,
            Name = "Sword",
            PluralName = "Swords",
            Value = 100,
            StackSize = 1,
            HouseRestrictions = new AceRestrictionDb(1, 1, 0, [(0x50000002, 1)]),
            IconOverlay = 0x06001234,
            IconUnderlay = 0x06005678,
            CooldownDuration = 3.5,
            PetOwner = 0x50000001,
        }),
        AceWireWriters.UpdateObject(new AceCreateObject { Guid = 0x80000001, Movement = new AceMovementData { MovementType = 6, TargetGuid = 1, RunRate = 2f } }),
        AceWireWriters.ObjectDelete(0x80000001, 2),
        AceWireWriters.UpdatePosition(0x50000001, new AcePosition(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.0784591f), true, new AceUpdatePositionSequences(1, 7, 0, 0)),
        AceWireWriters.UpdatePosition(0x80000001, new AcePosition(0xA9B40019, 1, 2, 3, 0.5f, 0.5f, 0.5f, 0.5f), false, default, (1, 2, 3), 0x65),
        AceWireWriters.UpdateMotion(0x80000001, 1, 2, 3, false, new AceMovementData
        {
            MotionFlags = 0x1,
            Style = 0x3C,
            Forward = 0x07,
            Turn = 0x0D,
            ForwardSpeed = 2f,
            TurnSpeed = 1.5f,
            Commands = [new AceMotionItem(0x87, 1, true)],
            StickyGuid = 0x80000002,
        }),
        AceWireWriters.UpdateMotion(0x80000001, 1, 2, 3, false, new AceMovementData { MovementType = 7, OriginCell = 0xA9B40019, RunRate = 1f }),
        AceWireWriters.UpdateMotion(0x80000001, 1, 2, 3, false, new AceMovementData { MovementType = 8, TargetGuid = 2, ObjectHeading = 90f }),
        AceWireWriters.UpdateMotion(0x80000001, 1, 2, 3, false, new AceMovementData { MovementType = 9, DesiredHeading = 180f }),
        AceWireWriters.VectorUpdate(0x80000001, (1, 2, 3), (4, 5, 6), 1, 2),
        AceWireWriters.SetState(0x50000001, 0x408, 1, 2),
        AceWireWriters.PlayerTeleport(3),
        AceWireWriters.ParentEvent(0x50000001, 0x80000001, 1, 1, 1, 2),
        AceWireWriters.PickupEvent(0x80000001, 1, 2),
        AceWireWriters.ObjDescEvent(0x50000001, new AceModelData(0x04000001, [(0x04000002, 0, 8)], [(1, 0x05000001, 0x05000002)], [(2, 0x01000003)]), 1, 2),
        AceWireWriters.Sound(0x50000001, 0x77, 0.5f),
        AceWireWriters.PlayEffect(0x50000001, 0x8A, 1f),

        // Chat (N7).
        AceWireWriters.HearSpeech("hello", "Crier", 0x7A9B4001, 2),
        AceWireWriters.HearRangedSpeech("hear ye", "Crier", 0x7A9B4001, 30f, 12),
        AceWireWriters.EmoteText(0x50000001, "Hero", "waves."),
        AceWireWriters.SoulEmote(0x50000001, "Hero", "bows."),

        // The decoded game events (N7).
        AceWireWriters.TellEvent(0x50000001, 4, "psst", "Friend", 0x50000002, 0x50000001, 3),
        AceWireWriters.PopupString(0x50000001, 5, "Welcome!"),
        AceWireWriters.WeenieError(0x50000001, 6, 0x25),
        AceWireWriters.WeenieErrorWithString(0x50000001, 7, 0x518, "away"),
        AceWireWriters.TransientString(0x50000001, 8, "busy"),
        AceWireWriters.UseDone(0x50000001, 9),
        AceWireWriters.UpdateHealth(0x50000001, 10, 0x80000001, 0.5f),

        // The player's own state (N10).
        AceWireWriters.PlayerDescription(0x50000001, 11, AcePlayerDescription.NewCharacter("Fuzz")),
        AceWireWriters.PlayerDescription(0x50000001, 12, AcePlayerDescription.NewCharacter("Fuzzier") with
        {
            InstanceIds = [(0x02, 0x80000101)],
            LastOutsideDeath = new AcePosition(0xA9B40019, 1, 2, 3),
            Spells = [0x3B, 0x5F],
            Enchantments = new AceEnchantmentRegistry(
                [new AceEnchantment(0xA3, 1, 0x19, 1, 150, -1, 1800, 0x50000001, 0.5f, 1f, 0, 0xC001, 1, 1.1f, 0x2A)],
                [new AceEnchantment(0x5F, 2, 0x11, 0, 125, -3, 900, 0x80000123, 0f, 0f, 0, 0x8002, 6, 10f, 0)],
                [],
                new AceEnchantment(0x29A, 0, 0xCC, 1, 0, 0, -1, 0x50000001, 0f, 0f, 0, 0x801, 0, 0.95f, 0)),
            Shortcuts = [new AceShortcut(0, 0x80000101)],
            SpellBars = [[0x3B]],
            FillComponents = [(0x0B, 25)],
            Inventory = [(0x80000101, 0), (0x80000103, 1)],
            Equipped = [(0x80000102, 0x200000, 0)],
        }),
        AceWireWriters.PlayerDescription(0x50000001, 13, AcePlayerDescription.NewCharacter("Options") with { GameplayOptions = [1, 2, 3, 4] }),
        AceWireWriters.PrivateUpdatePropertyInt(1, 0x19, 2),
        AceWireWriters.PrivateUpdatePropertyInt64(2, 0x01, 1000),
        AceWireWriters.PrivateUpdateSkill(3, new AceSkill(0x06, 1, 2, 10, 10, 4, 12.5)),
        AceWireWriters.PrivateUpdateAttribute(4, 1, 1, 100, 110),
        AceWireWriters.PrivateUpdateVital(5, 1, 0, 0, 0, 30),
        AceWireWriters.PrivateUpdateAttribute2ndLevel(6, 2, 29),
    ];

    /// <summary>The game events the dispatcher decodes; every other one is an optional-tail envelope.</summary>
    private static readonly uint[] DecodedEvents =
    [
        GameEventTypes.Tell, GameEventTypes.PopupString, GameEventTypes.WeenieError, GameEventTypes.WeenieErrorWithString,
        GameEventTypes.TransientString, GameEventTypes.UseDone, GameEventTypes.UpdateHealth, GameEventTypes.PlayerDescription,
    ];

    [Fact]
    public void The_samples_cover_every_decoded_opcode_and_decode()
    {
        var handler = new SilentHandler();
        foreach (var sample in Samples)
        {
            var outcome = InboundDispatcher.Dispatch(Context, sample, handler).Outcome;
            Assert.Equal(IsUndecodedEvent(sample) ? DispatchOutcome.Unknown : DispatchOutcome.Decoded, outcome);
        }

        uint[] decoded =
        [
            MessageOpcodes.CharacterList, MessageOpcodes.ServerName, MessageOpcodes.DddInterrogation, MessageOpcodes.DddBegin,
            MessageOpcodes.DddEnd, MessageOpcodes.CharacterCreateResponse, MessageOpcodes.CharacterDelete, MessageOpcodes.CharacterError,
            MessageOpcodes.AccountBoot, MessageOpcodes.AccountBanned, MessageOpcodes.CharacterEnterWorldServerReady,
            MessageOpcodes.PlayerCreate, MessageOpcodes.CharacterLogOff, MessageOpcodes.ServerMessage, MessageOpcodes.GameEvent,
            MessageOpcodes.ObjectCreate, MessageOpcodes.UpdateObject, MessageOpcodes.ObjectDelete, MessageOpcodes.UpdatePosition,
            MessageOpcodes.UpdateMotion, MessageOpcodes.VectorUpdate, MessageOpcodes.SetState, MessageOpcodes.PlayerTeleport,
            MessageOpcodes.ParentEvent, MessageOpcodes.PickupEvent, MessageOpcodes.ObjDescEvent, MessageOpcodes.Sound,
            MessageOpcodes.PlayEffect, MessageOpcodes.HearSpeech, MessageOpcodes.HearRangedSpeech, MessageOpcodes.EmoteText,
            MessageOpcodes.SoulEmote, MessageOpcodes.PrivateUpdatePropertyInt, MessageOpcodes.PrivateUpdatePropertyInt64,
            MessageOpcodes.PrivateUpdateSkill, MessageOpcodes.PrivateUpdateAttribute, MessageOpcodes.PrivateUpdateVital,
            MessageOpcodes.PrivateUpdateAttribute2ndLevel,
        ];
        Assert.Equal(decoded.Order(), Samples.Select(s => BinaryPrimitives.ReadUInt32LittleEndian(s)).Distinct().Order());
        Assert.Equal(DecodedEvents.Order(), Samples.Where(s => BinaryPrimitives.ReadUInt32LittleEndian(s) == MessageOpcodes.GameEvent && !IsUndecodedEvent(s))
            .Select(s => BinaryPrimitives.ReadUInt32LittleEndian(s.AsSpan(12))).Distinct().Order());
    }

    [Fact]
    public void Every_truncation_of_every_sample_is_handled_without_throwing()
    {
        var handler = new SilentHandler();
        foreach (var sample in Samples)
        {
            for (var length = 0; length < sample.Length; length++)
            {
                var result = InboundDispatcher.Dispatch(Context, sample.AsSpan(0, length), handler);
                if (length < MessageCodec.OpcodeSize)
                {
                    Assert.Equal(DispatchOutcome.Malformed, result.Outcome);
                }
                else if (!HasOptionalTail(sample))
                {
                    Assert.True(result.Outcome == DispatchOutcome.Malformed,
                        $"a {length}-byte prefix of the {sample.Length}-byte {result.Opcode:X4} sample was {result.Outcome}");
                }
            }
        }
    }

    [Fact]
    public void Ten_thousand_seeded_bit_flips_never_throw() => Flip(DefaultFlips, seed: 0x4E3);

    [Fact]
    [Trait("Tier", "Soak")]
    public void A_million_seeded_bit_flips_never_throw() => Flip(SoakFlips, seed: 0x4E3_50AC);

    [Fact]
    public void Random_bodies_under_every_decoded_opcode_never_throw()
    {
        var random = new Random(0x4E3_0001);
        var handler = new SilentHandler();
        var opcodes = Samples.Select(s => BinaryPrimitives.ReadUInt32LittleEndian(s)).Distinct().ToArray();
        for (var i = 0; i < DefaultFlips; i++)
        {
            var body = new byte[4 + random.Next(0, 64)];
            random.NextBytes(body);
            BinaryPrimitives.WriteUInt32LittleEndian(body, opcodes[i % opcodes.Length]);
            Dispatch(body, handler, i);
        }
    }

    /// <summary>Flips one to four random bits of a random sample, <paramref name="count"/> times, and dispatches each.</summary>
    private static void Flip(int count, int seed)
    {
        var random = new Random(seed);
        var handler = new SilentHandler();
        var outcomes = new int[3];
        for (var i = 0; i < count; i++)
        {
            var body = (byte[])Samples[random.Next(Samples.Length)].Clone();
            var flips = 1 + random.Next(4);
            for (var f = 0; f < flips; f++)
            {
                var bit = random.Next(body.Length * 8);
                body[bit >> 3] ^= (byte)(1 << (bit & 7));
            }

            outcomes[(int)Dispatch(body, handler, i)]++;
        }

        // Flips land everywhere: some bodies still decode, some become unknown opcodes, some become malformed.
        Assert.All(outcomes, n => Assert.True(n > 0, $"outcomes {string.Join("/", outcomes)}"));
    }

    private static DispatchOutcome Dispatch(byte[] body, IInboundMessageHandler handler, int iteration)
    {
        try
        {
            return InboundDispatcher.Dispatch(Context, body, handler).Outcome;
        }
        catch (Exception e)
        {
            Assert.Fail($"iteration {iteration}: {Convert.ToHexString(body)} threw {e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    /// <summary>Messages whose last field is optional: a shorter prefix can itself be a whole message.</summary>
    private static bool HasOptionalTail(byte[] sample)
    {
        var opcode = BinaryPrimitives.ReadUInt32LittleEndian(sample);
        return opcode is MessageOpcodes.AccountBoot or MessageOpcodes.AccountBanned
            || IsUndecodedEvent(sample)
            || IsGameplayOptionsDescription(sample)
            || (opcode == MessageOpcodes.CharacterCreateResponse && sample.Length > 8);
    }

    /// <summary>A PlayerDescription with gameplay options: they and the tail after them are kept raw, so a prefix still decodes.</summary>
    private static bool IsGameplayOptionsDescription(byte[] sample) =>
        BinaryPrimitives.ReadUInt32LittleEndian(sample) == MessageOpcodes.GameEvent
        && BinaryPrimitives.ReadUInt32LittleEndian(sample.AsSpan(12)) == GameEventTypes.PlayerDescription
        && PlayerDescriptionCodec.TryParse(sample, out var description, out _) && description.InventoryUndecoded;

    /// <summary>An F7B0 envelope around an event the dispatcher does not decode: its payload is opaque.</summary>
    private static bool IsUndecodedEvent(byte[] sample) =>
        BinaryPrimitives.ReadUInt32LittleEndian(sample) == MessageOpcodes.GameEvent
        && !DecodedEvents.Contains(BinaryPrimitives.ReadUInt32LittleEndian(sample.AsSpan(12)));

    /// <summary>Touches every argument it gets, so a span or record the dispatcher built badly would fail here.</summary>
    private sealed class SilentHandler : IInboundMessageHandler
    {
        private long _sink;

        public void OnCharacterList(in InboundContext context, CharacterListMsg message) =>
            _sink += message.Characters.Sum(c => c.Name.Length) + message.Account.Length + message.DeletedCharacters.Length;

        public void OnServerName(in InboundContext context, in ServerNameMsg message) => _sink += message.WorldName.Length;

        public void OnDddInterrogation(in InboundContext context, DddInterrogationMsg message) => _sink += message.SupportedLanguages.Length;

        public void OnCharGenResponse(in InboundContext context, in CharGenResponseMsg message) => _sink += message.Name?.Length ?? 0;

        public void OnAccountBoot(in InboundContext context, in AccountBootMsg message) => _sink += message.Reason?.Length ?? 0;

        public void OnAccountBanned(in InboundContext context, in AccountBannedMsg message) => _sink += message.Reason?.Length ?? 0;

        public void OnServerMessage(in InboundContext context, in ServerMessageMsg message) => _sink += message.Text.Length;

        public void OnGameEvent(in InboundContext context, in GameEventHeader header, ReadOnlySpan<byte> payload) => _sink += payload.Length;

        public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate) =>
            _sink += message.Weenie.Name.Length + message.Model.SubPalettes.Length + message.Physics.Children.Length
                + (message.Physics.Movement?.Commands.Length ?? 0) + (message.Weenie.HouseRestrictions?.Guests.Length ?? 0)
                + (message.Weenie.PluralName?.Length ?? 0);

        public void OnUpdateMotion(in InboundContext context, UpdateMotionMsg message) => _sink += message.Motion.Commands.Length;

        public void OnObjDescEvent(in InboundContext context, ObjDescEventMsg message) =>
            _sink += message.Model.SubPalettes.Length + message.Model.TextureChanges.Length + message.Model.AnimPartChanges.Length;

        public void OnSpeech(in InboundContext context, in SpeechMsg message) => _sink += message.Text.Length + message.SenderName.Length;

        public void OnEmote(in InboundContext context, in EmoteMsg message) => _sink += message.Text.Length + message.SenderName.Length;

        public void OnTell(in InboundContext context, in GameEventHeader header, in TellEvent message) => _sink += message.Text.Length + message.SenderName.Length;

        public void OnPopup(in InboundContext context, in GameEventHeader header, in PopupEvent message) => _sink += message.Text.Length;

        public void OnWeenieError(in InboundContext context, in GameEventHeader header, in WeenieErrorEvent message) => _sink += message.Text?.Length ?? 0;

        public void OnTransientString(in InboundContext context, in GameEventHeader header, in TransientStringEvent message) => _sink += message.Text.Length;

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message) =>
            _sink += message.Skills.Length + message.StringProperties.Sum(p => p.Value.Length) + message.Enchantments.Length
                + message.SpellBars.Sum(b => b.Length) + message.GameplayOptions.Length + message.Inventory.Length + message.Equipped.Length
                + (message.TimestampFormat?.Length ?? 0) + (message.OptionsData?.Strings.Count ?? 0);

        public void OnPrivateUpdate(in InboundContext context, in PrivateUpdateMsg message) => _sink += message.Sequence + message.Key;

        public void OnUnknown(in InboundContext context, uint opcode, ReadOnlySpan<byte> body) => _sink += body.Length;

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => _sink += reason.Length;
    }
}
