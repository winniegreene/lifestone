using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Inventory;

/// <summary>
/// GameEvent 0x00C9 IdentifyObjectResponse as ACE writes it (<c>GameEventIdentifyObjectResponse.cs:9-16</c>,
/// <c>AppraiseInfo.cs:743-786</c> and the profile writers): u32 guid, u32 flags, u32 success, then by flag the Int, Int64, Bool,
/// Float, String and DataId tables, the SpellBook, the Armor, Creature, Weapon and Hook profiles, the armour, weapon and resist
/// bitfields and the nine armour levels, section by section. The whole message is read; a cut-off section, an unknown flag or a
/// leftover byte refuses it with the section and the offset named, never a partial record.
/// </summary>
public sealed class AppraiseCodecTests
{
    private const uint Player = 0x50000001;
    private const uint Target = 0x80000ABC;

    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    [Fact]
    public void Success_zero_with_no_flags_is_a_result_not_an_error()
    {
        // ACE: an object it cannot find gets flags 0 and success 0 (Player.cs:265). Twelve bytes after the envelope's sequence.
        var message = Message(new AceAppraisal());

        Assert.Equal(Convert.FromHexString("B0F70000" + "01000050" + "07000000" + "C9000000" + "BC0A0080" + "00000000" + "00000000"), message);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(message, out var response, out var reason), reason);
        Assert.Equal(Target, response.Guid);
        Assert.Equal(IdentifyResponseFlags.None, response.Flags);
        Assert.False(response.Success);
        Assert.Empty(response.IntProperties);
        Assert.Empty(response.SpellBook);
        Assert.Null(response.Armor);
        Assert.Null(response.Creature);
        Assert.Null(response.ArmorLevels);
    }

    [Fact]
    public void An_int_table_matches_the_hand_built_bytes()
    {
        // flags 0x1 and success 1, then u16 count 2, u16 buckets 16, then the rows sorted by key % 16 and key: {19, -3} (bucket 3) before {5, 10} (bucket 5).
        var expected = Convert.FromHexString(
            "B0F70000" + "01000050" + "07000000" + "C9000000" + "BC0A0080" + "01000000" + "01000000" +
            "0200" + "1000" + "13000000" + "FDFFFFFF" + "05000000" + "0A000000");
        var message = Message(new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [5] = 10, [19] = -3 } });

        Assert.Equal(expected, message);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(expected, out var response, out _));
        Assert.Equal(IdentifyResponseFlags.IntStatsTable, response.Flags);
        Assert.True(response.Success);
        Assert.Equal(new Dictionary<uint, int> { [5] = 10, [19] = -3 }, response.IntProperties);
    }

    [Fact]
    public void Each_table_is_read_alone_with_its_value_width()
    {
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [5] = 10 },
            Int64s = new Dictionary<uint, long> { [1] = 5_000_000_000L },
            Bools = new Dictionary<uint, bool> { [11] = true, [12] = false },
            Floats = new Dictionary<uint, double> { [29] = 1.5 },
            Strings = new Dictionary<uint, string> { [16] = "A healing kit.", [1] = "" },
            DataIds = new Dictionary<uint, uint> { [22] = 0x0E000001 },
        };

        // The union first, then each table's own flag alone (the same bytes with only that section announced).
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var all, out var reason), reason);
        Assert.Equal(
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.Int64StatsTable | IdentifyResponseFlags.BoolStatsTable
            | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.StringStatsTable | IdentifyResponseFlags.DidStatsTable,
            all.Flags);
        Assert.Equal(new Dictionary<uint, int> { [5] = 10 }, all.IntProperties);
        Assert.Equal(new Dictionary<uint, long> { [1] = 5_000_000_000L }, all.Int64Properties);
        Assert.Equal(new Dictionary<uint, bool> { [11] = true, [12] = false }, all.BoolProperties);
        Assert.Equal(new Dictionary<uint, double> { [29] = 1.5 }, all.FloatProperties);
        Assert.Equal(new Dictionary<uint, string> { [16] = "A healing kit.", [1] = "" }, all.StringProperties);
        Assert.Equal(new Dictionary<uint, uint> { [22] = 0x0E000001 }, all.DataIdProperties);

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Int64s = info.Int64s }), out var i64, out _));
        Assert.Equal(IdentifyResponseFlags.Int64StatsTable, i64.Flags);
        Assert.Equal(5_000_000_000L, i64.Int64Properties[1]);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Floats = info.Floats }), out var f64, out _));
        Assert.Equal(1.5, f64.FloatProperties[29]);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Strings = info.Strings }), out var str, out _));
        Assert.Equal("A healing kit.", str.StringProperties[16]);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { DataIds = info.DataIds }), out var did, out _));
        Assert.Equal(0x0E000001u, did.DataIdProperties[22]);
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Bools = info.Bools }), out var flags, out _));
        Assert.True(flags.BoolProperties[11]);
        Assert.False(flags.BoolProperties[12]);
    }

    [Fact]
    public void A_spell_book_keeps_the_item_enchantment_bit_on_the_id()
    {
        var info = new AceAppraisal { SpellBook = [2096, 0x80000000 | 3490, 0x80000000 | 1] };

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var response, out _));

        Assert.Equal(IdentifyResponseFlags.SpellBook, response.Flags);
        Assert.Equal([2096u, 0x80000DA2u, 0x80000001u], response.SpellBook.ToArray());
        Assert.Equal([false, true, true], response.SpellBook.Select(IdentifyResponseMsg.IsItemEnchantment));
        Assert.Equal([2096u, 3490u, 1u], response.SpellBook.Select(IdentifyResponseMsg.SpellIdOf));
    }

    [Fact]
    public void The_armor_profile_is_eight_floats_in_ACE_s_order()
    {
        var info = new AceAppraisal { Armor = [1.0f, 1.1f, 1.2f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f] };

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var response, out _));

        Assert.Equal(IdentifyResponseFlags.ArmorProfile, response.Flags);
        Assert.Equal(new AppraisalArmorProfile(1.0f, 1.1f, 1.2f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f), response.Armor);
    }

    [Fact]
    public void The_creature_profile_shows_its_attributes_and_buffs_only_when_its_flags_say_so()
    {
        uint[] attributes = [110, 120, 130, 140, 150, 160, 70, 80, 90, 100];

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Creature = new AceCreatureProfile(250, 300) }), out var bare, out _));
        Assert.Equal(new AppraisalCreatureProfile(0, 250, 300, null, null, null, null, null, null, null, null, null, null, null, null), bare.Creature);

        var full = new AceCreatureProfile(250, 300) { Attributes = attributes, Buffs = (0x0005, 0x0004) };
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Creature = full }), out var withAll, out _));
        Assert.Equal(
            new AppraisalCreatureProfile(0x9, 250, 300, 110, 120, 130, 140, 150, 160, 70, 80, 90, 100, 5, 4),
            withAll.Creature);

        var buffsOnly = new AceCreatureProfile(10, 20) { Buffs = (0x0001, 0x0001) };
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Creature = buffsOnly }), out var buffs, out _));
        Assert.Equal(new AppraisalCreatureProfile(0x1, 10, 20, null, null, null, null, null, null, null, null, null, null, 1, 1), buffs.Creature);

        // Flags 0x2 and 0x4 are unknown to ACE and carry no data; they are kept, not skipped over data.
        var odd = new AceCreatureProfile(10, 20) { ExtraFlags = 0x6 };
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Creature = odd }), out var extra, out _));
        Assert.Equal(0x6u, extra.Creature!.Flags);
    }

    [Fact]
    public void The_weapon_profile_has_four_words_five_doubles_and_a_word()
    {
        var weapon = new AceWeaponProfile(0x2, 40, 0x2D, 12, 0.25, 1.1, 0.0, 1.5, 0.05, 7);

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Weapon = weapon }), out var response, out _));

        Assert.Equal(IdentifyResponseFlags.WeaponProfile, response.Flags);
        Assert.Equal(new AppraisalWeaponProfile(0x2, 40, 0x2D, 12, 0.25, 1.1, 0.0, 1.5, 0.05, 7), response.Weapon);
    }

    [Fact]
    public void The_hook_profile_is_three_words()
    {
        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(new AceAppraisal { Hook = (1, 0x00000020, 0x100) }), out var response, out _));

        Assert.Equal(IdentifyResponseFlags.HookProfile, response.Flags);
        Assert.Equal(new AppraisalHookProfile(1, 0x20, 0x100), response.Hook);
    }

    [Fact]
    public void The_three_bitfields_arrive_in_armor_weapon_resist_order_whatever_their_flag_bits()
    {
        var info = new AceAppraisal { ArmorBits = (0x11, 0x12), WeaponBits = (0x21, 0x22), ResistBits = (0x31, 0x32) };

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var response, out _));

        Assert.Equal(
            IdentifyResponseFlags.ArmorEnchantmentBitfield | IdentifyResponseFlags.WeaponEnchantmentBitfield | IdentifyResponseFlags.ResistEnchantmentBitfield,
            response.Flags);
        Assert.Equal(new AppraisalBitfield(0x11, 0x12), response.ArmorEnchantments);
        Assert.Equal(new AppraisalBitfield(0x21, 0x22), response.WeaponEnchantments);
        Assert.Equal(new AppraisalBitfield(0x31, 0x32), response.ResistEnchantments);
    }

    [Fact]
    public void The_nine_armor_levels_are_head_to_foot()
    {
        var info = new AceAppraisal { ArmorLevels = [10, 20, 30, 40, 50, 60, 70, 80, 90] };

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var response, out _));

        Assert.Equal(IdentifyResponseFlags.ArmorLevels, response.Flags);
        Assert.Equal(new AppraisalArmorLevels(10, 20, 30, 40, 50, 60, 70, 80, 90), response.ArmorLevels);
    }

    [Fact]
    public void Every_section_at_once_is_read_in_ACE_s_order()
    {
        var info = Everything();

        Assert.True(AppraiseCodecs.TryParseIdentifyResponse(Message(info), out var r, out var reason), reason);

        Assert.Equal((IdentifyResponseFlags)0x7FFF, r.Flags);
        Assert.True(r.Success);
        Assert.Equal(10, r.IntProperties[5]);
        Assert.Equal(5_000_000_000L, r.Int64Properties[1]);
        Assert.True(r.BoolProperties[11]);
        Assert.Equal(1.5, r.FloatProperties[29]);
        Assert.Equal("Name", r.StringProperties[1]);
        Assert.Equal(0x0E000001u, r.DataIdProperties[22]);
        Assert.Equal([0x80000001u, 2u], r.SpellBook.ToArray());
        Assert.Equal(1.0f, r.Armor!.Value.Slashing);
        Assert.Equal(250u, r.Creature!.Health);
        Assert.Equal(12u, r.Weapon!.Damage);
        Assert.Equal(0x100u, r.Hook!.AmmoType);
        Assert.Equal(new AppraisalBitfield(1, 2), r.ArmorEnchantments);
        Assert.Equal(new AppraisalBitfield(3, 4), r.WeaponEnchantments);
        Assert.Equal(new AppraisalBitfield(5, 6), r.ResistEnchantments);
        Assert.Equal(90u, r.ArmorLevels!.Value.Foot);
    }

    [Fact]
    public void A_truncated_section_fails_loudly_naming_the_section_and_the_offset_for_every_prefix()
    {
        var whole = Message(Everything());

        for (var length = 0; length < whole.Length; length++)
        {
            Assert.False(AppraiseCodecs.TryParseIdentifyResponse(whole.AsSpan(0, length), out var response, out var reason), $"a prefix of {length} bytes was accepted");
            Assert.Null(response);
            Assert.NotEmpty(reason);
            if (length >= 28)
            {
                // Past the envelope, the guid, the flags and the success word the failure is a section's, with an offset.
                Assert.Contains("section", reason, StringComparison.Ordinal);
                Assert.Contains("offset", reason, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(0x1u, "Int")]
    [InlineData(0x2000u, "Int64")]
    [InlineData(0x2u, "Bool")]
    [InlineData(0x4u, "Float")]
    [InlineData(0x8u, "String")]
    [InlineData(0x1000u, "DataId")]
    [InlineData(0x10u, "SpellBook")]
    [InlineData(0x80u, "ArmorProfile")]
    [InlineData(0x100u, "CreatureProfile")]
    [InlineData(0x20u, "WeaponProfile")]
    [InlineData(0x40u, "HookProfile")]
    [InlineData(0x200u, "ArmorBitfield")]
    [InlineData(0x800u, "WeaponBitfield")]
    [InlineData(0x400u, "ResistBitfield")]
    [InlineData(0x4000u, "ArmorLevels")]
    public void A_flag_whose_section_is_missing_names_that_section(uint flag, string section)
    {
        // Announce one section and send nothing after the success word.
        var message = AceWireWriters.GameEvent(Player, 7, 0xC9, [.. BitConverter.GetBytes(Target), .. BitConverter.GetBytes(flag), .. BitConverter.GetBytes(1u)]);

        Assert.False(AppraiseCodecs.TryParseIdentifyResponse(message, out _, out var reason));
        Assert.Contains($"the {section} section", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_flag_bit_is_a_named_refusal_and_a_trailing_byte_is_refused()
    {
        var unknown = AceWireWriters.GameEvent(Player, 7, 0xC9, [.. BitConverter.GetBytes(Target), .. BitConverter.GetBytes(0x8000u), .. BitConverter.GetBytes(1u)]);
        var high = AceWireWriters.GameEvent(Player, 7, 0xC9, [.. BitConverter.GetBytes(Target), .. BitConverter.GetBytes(0x80000001u), .. BitConverter.GetBytes(1u)]);

        Assert.False(AppraiseCodecs.TryParseIdentifyResponse(unknown, out _, out var reason));
        Assert.Contains("TODO(m4-appraise-section-unknown-flag)", reason, StringComparison.Ordinal);
        Assert.False(AppraiseCodecs.TryParseIdentifyResponse(high, out _, out var highReason));
        Assert.Contains("TODO(m4-appraise-section-unknown-flag)", highReason, StringComparison.Ordinal);

        Assert.False(AppraiseCodecs.TryParseIdentifyResponse([.. Message(new AceAppraisal()), 0], out _, out var trailing));
        Assert.Contains("unread", trailing, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_count_beyond_the_body_is_refused()
    {
        // flags 0x1, a count of 60000 and no rows.
        var message = AceWireWriters.GameEvent(Player, 7, 0xC9, [.. BitConverter.GetBytes(Target), .. BitConverter.GetBytes(0x1u), .. BitConverter.GetBytes(1u), .. BitConverter.GetBytes((ushort)60000), .. BitConverter.GetBytes((ushort)16)]);

        Assert.False(AppraiseCodecs.TryParseIdentifyResponse(message, out _, out var reason));
        Assert.Contains("the Int section", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dispatcher_reaches_OnIdentifyResponse_and_names_a_bad_body()
    {
        var handler = new Recorder();

        var ok = InboundDispatcher.Dispatch(Context, Message(new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [5] = 10 } }), handler);
        var bad = InboundDispatcher.Dispatch(Context, Message(Everything()).AsSpan(0, 40), handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x00C9), ok);
        Assert.Equal(DispatchOutcome.Malformed, bad.Outcome);
        Assert.Equal(["identify:80000ABC:IntStatsTable:True:seq7"], handler.Calls);
        Assert.Single(handler.Malformed);
        Assert.StartsWith("F7B0 GameEvent 0x00C9 IdentifyObjectResponse: ", handler.Malformed[0], StringComparison.Ordinal);
    }

    private static byte[] Message(AceAppraisal info) => AceVendorWriters.IdentifyResponse(Player, 7, Target, info);

    private static AceAppraisal Everything() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [5] = 10 },
        Int64s = new Dictionary<uint, long> { [1] = 5_000_000_000L },
        Bools = new Dictionary<uint, bool> { [11] = true },
        Floats = new Dictionary<uint, double> { [29] = 1.5 },
        Strings = new Dictionary<uint, string> { [1] = "Name" },
        DataIds = new Dictionary<uint, uint> { [22] = 0x0E000001 },
        SpellBook = [0x80000001, 2],
        Armor = [1.0f, 1.1f, 1.2f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f],
        Creature = new AceCreatureProfile(250, 300) { Attributes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], Buffs = (1, 2) },
        Weapon = new AceWeaponProfile(0x2, 40, 0x2D, 12, 0.25, 1.1, 0.0, 1.5, 0.05, 7),
        Hook = (1, 0x20, 0x100),
        ArmorBits = (1, 2),
        WeaponBits = (3, 4),
        ResistBits = (5, 6),
        ArmorLevels = [10, 20, 30, 40, 50, 60, 70, 80, 90],
    };

    private sealed class Recorder : IInboundMessageHandler
    {
        public List<string> Calls { get; } = [];

        public List<string> Malformed { get; } = [];

        public void OnIdentifyResponse(in InboundContext context, in GameEventHeader header, IdentifyResponseMsg message) =>
            Calls.Add($"identify:{message.Guid:X8}:{message.Flags}:{message.Success}:seq{header.Sequence}");

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
    }
}
