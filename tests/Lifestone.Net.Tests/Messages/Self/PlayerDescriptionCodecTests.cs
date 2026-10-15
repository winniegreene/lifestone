using System.Buffers.Binary;
using System.Numerics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Self;

/// <summary>
/// GameEvent 0x0013 PlayerDescription (m2-plan §3.3 N10) against ACE's writer (<see cref="AceWireWriters.PlayerDescription"/>):
/// a fresh character and a seasoned one with every section ACE fills round-trip field for field and to the last byte;
/// retail's option-section rules (the spell-bar count, the defaults, the timestamp format, the generic options data, the
/// old spellbook form) are checked on hand-built bodies; an undecoded vector section and gameplay options that cannot be
/// delimited are malformed with a reason, never guessed.
/// </summary>
public sealed class PlayerDescriptionCodecTests
{
    private const uint Player = 0x50000001;

    [Fact]
    public void A_fresh_character_round_trips()
    {
        var ace = AcePlayerDescription.NewCharacter("Tester");
        var body = AceWireWriters.PlayerDescription(Player, 1, ace);
        Assert.True(body.Length > 1_400, $"a fresh character is {body.Length} bytes; it should span several fragments");

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.Equal(10u, d.WeenieType);
        Assert.Equal(0x1u | 0x80u | 0x2u | 0x4u | 0x10u | 0x8u, d.PropertyFlags);
        AssertTable(ace.Ints, d.IntProperties);
        AssertTable(ace.Int64s, d.Int64Properties);
        AssertTable(ace.Bools, d.BoolProperties);
        AssertTable(ace.Doubles, d.FloatProperties);
        AssertTable(ace.Strings, d.StringProperties);
        AssertTable(ace.DataIds, d.DataIdProperties);
        Assert.Empty(d.InstanceIdProperties);
        Assert.Empty(d.PositionProperties);
        Assert.Equal("Tester", d.StringProperties[1]);

        Assert.Equal(0x3u, d.VectorFlags);
        Assert.True(d.HasHealth);
        Assert.Equal(0x1FFu, d.AttributeFlags);
        Assert.Equal(
            ace.Attributes.Select((a, i) => new AttributeDesc((uint)i + 1, a.Ranks, a.Start, a.Experience)),
            d.Attributes);
        Assert.Equal(
            [new VitalDesc(1, 0, 0, 0, 30), new VitalDesc(3, 0, 0, 0, 60), new VitalDesc(5, 0, 0, 0, 40)],
            d.Vitals.ToArray());
        Assert.Equal(
            ace.Skills.Select(s => new SkillDesc(s.Id, s.Ranks, 1, s.AdvancementClass, s.Experience, s.Init, 0, 0)),
            d.Skills);
        Assert.Empty(d.Spells);
        Assert.Empty(d.Enchantments);
        Assert.Equal(0u, d.EnchantmentMask);

        Assert.Equal(0x40u | 0x400u | 0x20u, d.OptionFlags);
        Assert.Equal(0x50C4A54Au, d.Options1);
        Assert.Equal(0x00948700u, d.Options2);
        Assert.Equal(0x3FFFu, d.SpellbookFilters);
        Assert.Empty(d.Shortcuts);
        Assert.Equal(8, d.SpellBars.Length);
        Assert.All(d.SpellBars, bar => Assert.Empty(bar));
        Assert.Empty(d.DesiredComponents);
        Assert.Null(d.TimestampFormat);
        Assert.Null(d.OptionsData);
        Assert.True(d.GameplayOptions.IsEmpty);
        Assert.Empty(d.Inventory);
        Assert.Empty(d.Equipped);

        AssertExact(body);
    }

    [Fact]
    public void A_character_with_every_section_ACE_fills_round_trips()
    {
        var multiplicative = new AceEnchantment(0x0A3, 1, 0x0019, 1, 150, -12.5, 1800, 0x50000001, 0.5f, 1f, 0, 0x0000C001, 1, 1.15f, 0x2A);
        var additive = new AceEnchantment(0x05F, 2, 0x0011, 0, 125, -3, 900, 0x80000123, 0f, 0f, 0, 0x00008002, 6, 10f, 0);
        var cooldown = new AceEnchantment(0x8001, 1, 0x8000, 0, 0, -1, 30, 0x50000001, 0f, 0f, 0, 0x00000004, 0, 0f, 0);
        var vitae = new AceEnchantment(0x29A, 0, 0x00CC, 1, 0, 0, -1, 0x50000001, 0f, 0f, 0, 0x00000801, 0, 0.95f, 0);
        var ace = AcePlayerDescription.NewCharacter("Seasoned") with
        {
            InstanceIds = [(0x02, 0x80000101), (0x07, 0x80000102)],
            LastOutsideDeath = new AcePosition(0xA9B40019, 84f, 7.1f, 94f, 0.996917f, 0f, 0f, -0.0784591f),
            Spells = [0x003B, 0x005F, 0x00A3],
            Enchantments = new AceEnchantmentRegistry([multiplicative], [additive, additive with { Layer = 3 }], [cooldown], vitae),
            Shortcuts = [new AceShortcut(0, 0x80000101), new AceShortcut(1, 0, 0x05F, 2)],
            SpellBars = [[0x3B, 0x5F], [], [0xA3]],
            FillComponents = [(0x0B, 25), (0x0C, -1)],
            Inventory = [(0x80000101, 0), (0x80000103, 1), (0x80000104, 2)],
            Equipped = [(0x80000102, 0x200000, 0), (0x80000105, 0x2, 0x10)],
        };
        var body = AceWireWriters.PlayerDescription(Player, 2, ace);

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        AssertTable([(0x02u, 0x80000101u), (0x07u, 0x80000102u)], d.InstanceIdProperties);
        var death = Assert.Single(d.PositionProperties);
        Assert.Equal(0x0Eu, death.Key);
        Assert.Equal(new AcPosition(0xA9B40019, new Vector3(84f, 7.1f, 94f), new Quaternion(0f, 0f, -0.0784591f, 0.996917f)), death.Value);

        Assert.Equal(0x3u | 0x100u | 0x200u, d.VectorFlags);
        AssertTable([(0x3Bu, 0f), (0x5Fu, 0f), (0xA3u, 0f)], d.Spells);

        Assert.Equal(0xFu, d.EnchantmentMask);
        Assert.Equal(
            [
                Desc(multiplicative, EnchantmentBucket.Multiplicative),
                Desc(additive, EnchantmentBucket.Additive),
                Desc(additive with { Layer = 3 }, EnchantmentBucket.Additive),
                Desc(cooldown, EnchantmentBucket.Cooldown),
                Desc(vitae, EnchantmentBucket.Vitae),
            ],
            d.Enchantments.ToArray());

        Assert.Equal(0x40u | 0x400u | 0x20u | 0x1u | 0x8u, d.OptionFlags);
        Assert.Equal([new ShortcutDesc(0, 0x80000101, 0), new ShortcutDesc(1, 0, 0x0002005F)], d.Shortcuts.ToArray());
        Assert.Equal([0x3Bu, 0x5Fu], d.SpellBars[0].ToArray());
        Assert.Empty(d.SpellBars[1]);
        Assert.Equal([0xA3u], d.SpellBars[2].ToArray());
        Assert.Equal(8, d.SpellBars.Length);
        Assert.Equal([new DesiredComponentDesc(0x0B, 25), new DesiredComponentDesc(0x0C, -1)], d.DesiredComponents.ToArray());
        Assert.Equal([new ContentDesc(0x80000101, 0), new ContentDesc(0x80000103, 1), new ContentDesc(0x80000104, 2)], d.Inventory.ToArray());
        Assert.Equal([new EquippedDesc(0x80000102, 0x200000, 0), new EquippedDesc(0x80000105, 0x2, 0x10)], d.Equipped.ToArray());

        AssertExact(body);
    }

    [Fact]
    public void Gameplay_options_keep_themselves_and_the_tail_raw_and_say_so()
    {
        byte[] options = [0x01, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0xAB, 0xCD, 0xEF, 0x01, 0x10, 0x20, 0x30, 0x40];
        var ace = AcePlayerDescription.NewCharacter("Options") with
        {
            GameplayOptions = options,
            Inventory = [(0x80000101, 0)],
            Equipped = [(0x80000102, 0x200000, 0)],
        };
        var body = AceWireWriters.PlayerDescription(Player, 3, ace);

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.Equal(0x200u, d.OptionFlags & 0x200u);
        Assert.True(d.InventoryUndecoded);
        byte[] tail = [1, 0, 0, 0, 0x01, 0x01, 0x00, 0x80, 0, 0, 0, 0, 1, 0, 0, 0, 0x02, 0x01, 0x00, 0x80, 0x00, 0x00, 0x20, 0x00, 0, 0, 0, 0];
        Assert.Equal([.. options, .. tail], d.GameplayOptions.ToArray());
        Assert.Empty(d.Inventory);
        Assert.Empty(d.Equipped);

        // Everything before the options is decoded: the vitals, the skills, the module's words.
        Assert.Equal(3, d.Vitals.Length);
        Assert.Equal(ace.Skills.Count, d.Skills.Length);
        Assert.Equal(8, d.SpellBars.Length);
        Assert.Equal(0x3FFFu, d.SpellbookFilters);
    }

    [Fact]
    public void A_tail_search_would_be_a_guess()
    {
        // Why the options are not delimited by searching for a tail that ends the message: with the last content a
        // non-container, the tail read from that content's type word (0 contents, then the real equipped list) fits as
        // well as the real one. Shown on the tail alone, from two starts.
        var ace = AcePlayerDescription.NewCharacter("Suffix") with
        {
            Inventory = [(0x80000101, 0)],
            Equipped = [(0x80000102, 0x200000, 0)],
        };
        var body = AceWireWriters.PlayerDescription(Player, 4, ace);
        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.False(d.InventoryUndecoded);
        Assert.Equal([new ContentDesc(0x80000101, 0)], d.Inventory.ToArray());

        var realStart = body.Length - 28;
        Assert.True(TailEnds(body, realStart));
        Assert.True(TailEnds(body, realStart + 8));
    }

    [Theory]
    [InlineData(0x4u, "Body")]
    [InlineData(0x8u, "EventFilter")]
    [InlineData(0x10u, "EmoteTable")]
    [InlineData(0x20u, "CreationProfile list")]
    [InlineData(0x40u, "PageDataList")]
    [InlineData(0x80u, "GeneratorTable")]
    [InlineData(0x400u, "GeneratorRegistry")]
    [InlineData(0x800u, "GeneratorQueue")]
    public void A_vector_section_Lifestone_does_not_decode_is_refused_by_name(uint flag, string name)
    {
        var body = Build(vectorFlags: flag, module: static w => Module(w, 0x20, 0x3FFF));
        Assert.False(PlayerDescriptionCodec.TryParse(body, out _, out var reason));
        Assert.Contains($"0x{flag:X} ({name})", reason);
        Assert.Contains("TODO(net)", reason);
    }

    [Theory]
    [InlineData(0x000u, 1)]
    [InlineData(0x004u, 5)]
    [InlineData(0x010u, 7)]
    [InlineData(0x400u, 8)]
    [InlineData(0x414u, 5)]  // retail checks 0x4 first, then 0x10, then 0x400
    [InlineData(0x410u, 7)]
    public void The_spell_bar_count_follows_retails_three_flags(uint flags, int bars)
    {
        var body = Build(module: w =>
        {
            w.Write(flags);
            w.Write(0u);
            for (var i = 0; i < bars; i++)
            {
                w.Write(1u);
                w.Write(0x100u + (uint)i);
            }
        });

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.Equal(bars, d.SpellBars.Length);
        Assert.Equal(Enumerable.Range(0, bars).Select(i => 0x100u + (uint)i), d.SpellBars.Select(b => Assert.Single(b)));
    }

    [Fact]
    public void Retails_defaults_fill_the_words_the_flags_omit()
    {
        var body = Build(module: static w =>
        {
            w.Write(0u);
            w.Write(0x12345678u);
            w.Write(0u);
        });

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.Equal(0x12345678u, d.Options1);
        Assert.Equal(PlayerDescriptionCodec.DefaultSpellbookFilters, d.SpellbookFilters);
        Assert.Equal(PlayerDescriptionCodec.DefaultOptions2, d.Options2);
        Assert.Equal(9733888u, PlayerDescriptionCodec.DefaultOptions2);
        Assert.Single(d.SpellBars);
    }

    [Fact]
    public void The_timestamp_format_and_the_generic_options_data_are_read()
    {
        var body = Build(module: static w =>
        {
            w.Write(0x80u | 0x100u);
            w.Write(0u);
            w.Write(0u);                       // spell bar 0
            AceWireWriters.WriteString16L(w, "%H:%M");
            w.Write(0xFu);                     // int, bool, float and string tables
            Table(w, 1, () =>
            {
                w.Write(7u);
                w.Write(-3);
            });
            Table(w, 1, () =>
            {
                w.Write(8u);
                w.Write(1u);
            });
            Table(w, 1, () =>
            {
                w.Write(9u);
                w.Write(2.5d);
            });
            Table(w, 1, () =>
            {
                w.Write(10u);
                AceWireWriters.WriteString16L(w, "abc");
            });
        });

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        Assert.Equal("%H:%M", d.TimestampFormat);
        var data = Assert.IsType<GenericQualitiesDesc>(d.OptionsData);
        Assert.Equal(-3, data.Ints[7]);
        Assert.True(data.Bools[8]);
        Assert.Equal(2.5d, data.Floats[9]);
        Assert.Equal("abc", data.Strings[10]);
    }

    [Fact]
    public void The_old_spellbook_form_reads_its_third_word()
    {
        // SpellBookPage::UnPack: a first f32 under 2 opens the old 12-byte form, whose third word is the likelihood.
        var body = Build(vectorFlags: 0x100, spellbook: static w =>
        {
            Table(w, 3, () =>
            {
                w.Write(0x3Bu);
                w.Write(2.75f);                 // the new form: likelihood + 2
                w.Write(0x5Fu);
                w.Write(1f);                    // the old form ...
                w.Write(0xDEADBEEFu);           // ... a skipped word ...
                w.Write(0.25f);                 // ... and the likelihood
                w.Write(0xA3u);
                w.Write(2f);
            });
        });

        Assert.True(PlayerDescriptionCodec.TryParse(body, out var d, out var reason), reason);
        AssertTable([(0x3Bu, 0.75f), (0x5Fu, 0.25f), (0xA3u, 0f)], d.Spells);
    }

    [Fact]
    public void The_dispatcher_decodes_it_with_its_header()
    {
        var body = AceWireWriters.PlayerDescription(Player, 9, AcePlayerDescription.NewCharacter("Dispatch"));
        var handler = new Recorder();

        var result = InboundDispatcher.Dispatch(new InboundContext(3, MessageQueue.Ui, 0), body, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, GameEventTypes.PlayerDescription), result);
        Assert.Equal(new GameEventHeader(Player, 9, GameEventTypes.PlayerDescription), handler.Header);
        Assert.Equal("Dispatch", handler.Description!.StringProperties[1]);

        var bad = Build(vectorFlags: 0x4, module: static w => Module(w, 0, 0));
        Assert.Equal(DispatchOutcome.Malformed, InboundDispatcher.Dispatch(new InboundContext(4, MessageQueue.Ui, 0), bad, handler).Outcome);
        Assert.StartsWith("F7B0 GameEvent 0x0013 PlayerDescription: vector flag 0x4 (Body)", handler.Malformed);
    }

    private static EnchantmentDesc Desc(AceEnchantment e, EnchantmentBucket bucket) =>
        new(e.SpellId, e.Layer, e.SpellCategory, e.HasSpellSetId, e.PowerLevel, e.StartTime, e.Duration, e.CasterGuid,
            e.DegradeModifier, e.DegradeLimit, e.LastTimeDegraded, e.StatModType, e.StatModKey, e.StatModValue,
            e.HasSpellSetId != 0 ? e.SpellSetId : 0, bucket);

    /// <summary>True when a content list and an equipped list read from <paramref name="start"/> end the message exactly.</summary>
    private static bool TailEnds(byte[] body, int start)
    {
        var contents = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(start));
        var equippedAt = start + 4 + (8 * (long)contents);
        return equippedAt + 4 <= body.Length
            && equippedAt + 4 + (12 * (long)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan((int)equippedAt))) == body.Length;
    }

    /// <summary>A decoded table holds exactly <paramref name="expected"/>.</summary>
    private static void AssertTable<T>(IEnumerable<(uint Key, T Value)> expected, IReadOnlyDictionary<uint, T> actual) =>
        Assert.Equal(expected.OrderBy(e => e.Key), actual.Select(kv => (kv.Key, kv.Value)).OrderBy(e => e.Key));

    /// <summary>The whole body decodes; a trailing byte and every shorter prefix do not.</summary>
    private static void AssertExact(byte[] body)
    {
        Assert.True(PlayerDescriptionCodec.TryParse(body, out _, out _));
        Assert.False(PlayerDescriptionCodec.TryParse([.. body, 0], out _, out _), "a trailing byte was accepted");
        for (var length = 0; length < body.Length; length++)
        {
            Assert.False(PlayerDescriptionCodec.TryParse(body[..length], out _, out _), $"a {length}-byte prefix of a {body.Length}-byte description was accepted");
        }
    }

    /// <summary>
    /// A hand-built description: the envelope, empty property flags, the vector flags with only the spellbook (when
    /// <paramref name="spellbook"/> writes one), the has-health word, then the module <paramref name="module"/> writes and
    /// an empty tail.
    /// </summary>
    private static byte[] Build(uint vectorFlags = 0, Action<BinaryWriter>? spellbook = null, Action<BinaryWriter>? module = null) =>
        AceWireWriters.Message(AceWireWriters.GameEventOpcode, w =>
        {
            w.Write(Player);
            w.Write(1u);
            w.Write(AceWireWriters.PlayerDescriptionEventType);
            w.Write(0u);          // property flags
            w.Write(10u);         // weenie type
            w.Write(vectorFlags);
            w.Write(1u);          // has health
            spellbook?.Invoke(w);
            (module ?? (static m => Module(m, 0, 0)))(w);
            w.Write(0u);          // contents
            w.Write(0u);          // equipped
        });

    /// <summary>A minimal player module: the flags (only 0x20 is honoured here), options1 0, one empty spell bar, the filters.</summary>
    private static void Module(BinaryWriter w, uint flags, uint filters)
    {
        w.Write(flags);
        w.Write(0u);
        w.Write(0u);
        if ((flags & 0x20) != 0)
        {
            w.Write(filters);
        }
    }

    private static void Table(BinaryWriter w, ushort count, Action entries)
    {
        w.Write(count);
        w.Write((ushort)32);
        entries();
    }

    private sealed class Recorder : IInboundMessageHandler
    {
        public GameEventHeader Header { get; private set; }

        public PlayerDescriptionMsg? Description { get; private set; }

        public string Malformed { get; private set; } = string.Empty;

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message) =>
            (Header, Description) = (header, message);

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed = reason;
    }
}
