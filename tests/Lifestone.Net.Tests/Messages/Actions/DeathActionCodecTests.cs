using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The suicide and the spell-bar game actions (M3 3.2 s0) pinned byte for byte against ACE's handlers (v1.78.4816): each is F7B1, the action sequence, the type, then little-endian 32-bit fields.
/// <c>GameActionDie</c> (type 0x0279, <c>GameActionType.Suicide</c>) reads nothing; <c>GameActionAddSpellFavorite</c> (0x01E3) reads the spell id, the position on the bar and the bar id, in that order,
/// three <c>ReadUInt32</c>. The mirror in <see cref="ReadAce"/> re-expresses those handlers, so a codec that drifted from ACE fails here and not on a live server.
/// </summary>
public sealed class DeathActionCodecTests
{
    [Fact]
    public void Suicide_is_0x0279_and_nothing_else()
    {
        // F7B1, sequence 5, type 0x279: twelve bytes.
        var body = Build(w => MagicActionCodecs.WriteSuicide(w, 5));

        Assert.Equal(Convert.FromHexString("B1F70000" + "05000000" + "79020000"), body);
        var (sequence, type, fields) = ReadAce(body, 0);
        Assert.Equal((5u, 0x0279u), (sequence, type));
        Assert.Empty(fields);
    }

    [Fact]
    public void AddSpellFavorite_is_0x01E3_then_the_spell_the_index_and_the_bar()
    {
        // Spell 0x0B4F at the bar's fourth slot (index 3) on bar 2.
        var body = Build(w => MagicActionCodecs.WriteAddSpellFavorite(w, 9, 0x0B4F, 3, 2));

        Assert.Equal(Convert.FromHexString("B1F70000" + "09000000" + "E3010000" + "4F0B0000" + "03000000" + "02000000"), body);
        var (sequence, type, fields) = ReadAce(body, 3);
        Assert.Equal((9u, 0x01E3u), (sequence, type));
        Assert.Equal(new uint[] { 0x0B4F, 3, 2 }, fields);
    }

    [Fact]
    public void AddSpellFavorite_keeps_the_field_order_when_the_values_differ_in_every_byte()
    {
        var body = Build(w => MagicActionCodecs.WriteAddSpellFavorite(w, 0x01020304, 0xAABBCCDD, 0x11223344, 7));

        var (sequence, _, fields) = ReadAce(body, 3);
        Assert.Equal(0x01020304u, sequence);
        Assert.Equal(new uint[] { 0xAABBCCDD, 0x11223344, 7 }, fields); // spell, then position, then bar: ACE's order
    }

    [Fact]
    public void The_opcodes_are_the_values_ACE_and_retail_use()
    {
        // GameActionType.Suicide = 0x0279 and AddSpellFavorite = 0x01E3 (ACE GameActionType.cs); CM_Character::Event_Suicide and Event_AddSpellFavorite put the same values in the type word.
        Assert.Equal([0x0279u, 0x01E3u], [GameActionTypes.Suicide, GameActionTypes.AddSpellFavorite]);
    }

    /// <summary>ACE's two handlers re-expressed: the F7B1 envelope (opcode, sequence, type), then <paramref name="count"/> UInt32 fields, nothing after.</summary>
    private static (uint Sequence, uint Type, uint[] Fields) ReadAce(byte[] body, int count)
    {
        Assert.Equal(12 + (4 * count), body.Length);
        Assert.Equal(MessageOpcodes.GameAction, BinaryPrimitives.ReadUInt32LittleEndian(body));
        var fields = Enumerable.Range(0, count).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12 + (4 * i)))).ToArray();
        return (BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)), fields);
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
