// Partly ported from eriknihlen/OpenAC@1ee5a08 tests/AcDream.Core.Net.Tests/Messages/CharacterListTests.cs (MIT;
// Copyright (c) 2026 Erik Nihlén and OpenAC contributors) with changes: namespace Lifestone.Net.Tests.Messages; the cases
// for order, the deleted list, a non-zero status, a count larger than the message, 255 minimal entries and every
// truncated prefix are kept, against CharacterListCodec.TryParse (false instead of FormatException); the
// TrySelectFirstAvailable cases are not taken; the docs/protocol.md §10 vector and the AceWireWriters round trips are
// Lifestone's.

using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages;

/// <summary>F658 CharacterList: the §10 golden vector, ACE's writer, the retail deleted list, and refusal of every malformed body.</summary>
public sealed class CharacterListCodecTests
{
    /// <summary>docs/protocol.md §10: ACE's writer for one character 0x50000001 "Tester", account "lifestone", 11 slots, TurbineChat on.</summary>
    private static readonly byte[] GoldenVector = Convert.FromHexString(
        "58F60000000000000100000001000050060054657374657200000000000000000B00000009006C69666573746F6E65000100000001000000");

    [Fact]
    public void The_protocol_vector_decodes_field_for_field()
    {
        Assert.Equal(56, GoldenVector.Length);

        Assert.True(CharacterListCodec.TryParse(GoldenVector, out var list));

        Assert.Equal(0u, list.Leading);
        var character = Assert.Single(list.Characters);
        Assert.Equal(new CharacterListEntry(0x50000001, "Tester", 0), character);
        Assert.Equal(0u, list.DeletedCount);
        Assert.Empty(list.DeletedCharacters);
        Assert.Equal(11u, list.SlotCount);
        Assert.Equal("lifestone", list.Account);
        Assert.Equal(1u, list.UseTurbineChat);
        Assert.Equal(1u, list.HasThroneOfDestiny);
    }

    [Fact]
    public void AceWireWriters_reproduces_the_protocol_vector()
    {
        var body = AceWireWriters.CharacterList([new AceCharacter(0x50000001, "Tester")], "lifestone");
        Assert.Equal(GoldenVector, body);
    }

    [Fact]
    public void Ace_bodies_round_trip_with_the_order_the_account_and_the_raw_deletion_word()
    {
        AceCharacter[] characters =
        [
            new(0x50000003, "Carol", 1),
            new(0x50000001, "+Admin"),
            new(0x50000002, "Bob"),
        ];
        var body = AceWireWriters.CharacterList(characters, "Account Name", slotCount: 20, useTurbineChat: false);

        Assert.True(CharacterListCodec.TryParse(body, out var list));

        Assert.Equal(
            characters.Select(c => new CharacterListEntry(c.Guid, c.Name, c.DeleteField)),
            list.Characters);
        Assert.Equal("Account Name", list.Account);
        Assert.Equal(20u, list.SlotCount);
        Assert.Equal(0u, list.UseTurbineChat);
    }

    [Fact]
    public void A_name_outside_ascii_decodes_as_windows_1252()
    {
        var body = AceWireWriters.CharacterList([new AceCharacter(0x50000001, "Ælfric")], "acct");
        Assert.True(CharacterListCodec.TryParse(body, out var list));
        Assert.Equal("Ælfric", list.Characters[0].Name);
    }

    [Fact]
    public void An_empty_account_list_decodes()
    {
        Assert.True(CharacterListCodec.TryParse(AceWireWriters.CharacterList([], "acct"), out var list));
        Assert.Empty(list.Characters);
    }

    [Fact]
    public void A_non_zero_status_and_the_deleted_list_keep_the_retail_layout()
    {
        var w = new PacketWriter(160);
        w.WriteUInt32(MessageOpcodes.CharacterList);
        w.WriteUInt32(7);
        w.WriteUInt32(1);
        WriteEntry(w, 0x50000001, "Active", 0);
        w.WriteUInt32(2);
        WriteEntry(w, 0x50000002, "Deleted One", 90);
        WriteEntry(w, 0x50000003, "Deleted Two", 180);
        WriteTail(w, 13, "CanonicalAccount", 1, 0);

        Assert.True(CharacterListCodec.TryParse(w.AsSpan(), out var list));

        Assert.Equal(7u, list.Leading);
        Assert.Equal("Active", Assert.Single(list.Characters).Name);
        Assert.Equal(2u, list.DeletedCount);
        Assert.Equal(
            new[] { new CharacterListEntry(0x50000002, "Deleted One", 90), new CharacterListEntry(0x50000003, "Deleted Two", 180) },
            list.DeletedCharacters);
        Assert.Equal(13u, list.SlotCount);
        Assert.Equal(0u, list.HasThroneOfDestiny);
    }

    [Fact]
    public void Raw_words_stay_raw()
    {
        var w = new PacketWriter(64);
        w.WriteUInt32(MessageOpcodes.CharacterList);
        w.WriteUInt32(0);
        w.WriteUInt32(0);
        w.WriteUInt32(0);
        WriteTail(w, unchecked((int)uint.MaxValue), "Account", 2, uint.MaxValue);

        Assert.True(CharacterListCodec.TryParse(w.AsSpan(), out var list));
        Assert.Equal(uint.MaxValue, list.SlotCount);
        Assert.Equal(2u, list.UseTurbineChat);
        Assert.Equal(uint.MaxValue, list.HasThroneOfDestiny);
    }

    [Fact]
    public void Two_hundred_fifty_five_minimal_entries_fit_by_the_message_bound_not_a_cap()
    {
        var w = new PacketWriter(4096);
        w.WriteUInt32(MessageOpcodes.CharacterList);
        w.WriteUInt32(0);
        w.WriteUInt32(255);
        for (uint i = 1; i <= 255; i++)
        {
            WriteEntry(w, i, string.Empty, 0);
        }

        w.WriteUInt32(0);
        WriteTail(w, 255, "Account", 1, 1);

        Assert.True(CharacterListCodec.TryParse(w.AsSpan(), out var list));
        Assert.Equal(255, list.Characters.Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_count_larger_than_the_message_is_refused_before_anything_is_allocated(bool activeList)
    {
        var w = new PacketWriter(20);
        w.WriteUInt32(MessageOpcodes.CharacterList);
        w.WriteUInt32(0);
        if (!activeList)
        {
            w.WriteUInt32(0);
        }

        w.WriteUInt32(256);
        Assert.False(CharacterListCodec.TryParse(w.AsSpan(), out var list));
        Assert.Null(list);
    }

    [Fact]
    public void Every_truncated_prefix_and_a_trailing_byte_are_refused()
    {
        var w = new PacketWriter(160);
        w.WriteUInt32(MessageOpcodes.CharacterList);
        w.WriteUInt32(3);
        w.WriteUInt32(1);
        WriteEntry(w, 0x50000001, "A", 0);
        w.WriteUInt32(1);
        WriteEntry(w, 0x50000002, "Deleted", 22);
        WriteTail(w, 11, "Account", 1, 1);
        var valid = w.ToArray();

        for (var length = 0; length < valid.Length; length++)
        {
            Assert.False(CharacterListCodec.TryParse(valid.AsSpan(0, length), out _), $"a prefix of {length} bytes was accepted");
        }

        Assert.True(CharacterListCodec.TryParse(valid, out _));
        Assert.False(CharacterListCodec.TryParse([.. valid, 0], out _));
    }

    [Fact]
    public void Another_opcode_is_refused()
    {
        var bytes = (byte[])GoldenVector.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xDEADBEEF);
        Assert.False(CharacterListCodec.TryParse(bytes, out _));
    }

    private static void WriteEntry(PacketWriter w, uint guid, string name, uint greyedOut)
    {
        w.WriteUInt32(guid);
        w.WriteString16L(name);
        w.WriteUInt32(greyedOut);
    }

    private static void WriteTail(PacketWriter w, int slotCount, string account, uint useTurbineChat, uint hasThroneOfDestiny)
    {
        w.WriteInt32(slotCount);
        w.WriteString16L(account);
        w.WriteUInt32(useTurbineChat);
        w.WriteUInt32(hasThroneOfDestiny);
    }
}
