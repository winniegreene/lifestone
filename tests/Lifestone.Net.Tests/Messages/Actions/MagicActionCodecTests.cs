using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The spell and combat-mode game actions (M3 1.2 s4a) pinned byte for byte against ACE's handlers (v1.78.4816): each is F7B1, the action
/// sequence, the type, then little-endian 32-bit fields. <c>GameActionMagicCastUnTargetedSpell</c> reads one UInt32 (the spell);
/// <c>GameActionMagicCastTargetedSpell</c> reads the target guid first, then the spell; <c>GameActionChangeCombatMode</c> reads one UInt32.
/// The mirror in <see cref="ReadAce"/> re-expresses those handlers, so a codec that drifted from ACE fails here and not on a live server.
/// </summary>
public sealed class MagicActionCodecTests
{
    [Fact]
    public void CastUntargetedSpell_is_0x0048_then_the_spell_id()
    {
        // F7B1, sequence 7, type 0x48, spell 0x0B4F (2895).
        var body = Build(w => MagicActionCodecs.WriteCastUntargetedSpell(w, 7, 0x0B4F));

        Assert.Equal(Convert.FromHexString("B1F70000" + "07000000" + "48000000" + "4F0B0000"), body);
        var (sequence, type, fields) = ReadAce(body, 1);
        Assert.Equal((7u, 0x0048u), (sequence, type));
        Assert.Equal(new uint[] { 0x0B4F }, fields);
    }

    [Fact]
    public void CastTargetedSpell_is_0x004A_target_first_then_the_spell_id()
    {
        // The target comes first, as ACE's handler reads targetGuid before spellId.
        var body = Build(w => MagicActionCodecs.WriteCastTargetedSpell(w, 8, 0x80000123, 0x0B50));

        Assert.Equal(Convert.FromHexString("B1F70000" + "08000000" + "4A000000" + "23010080" + "500B0000"), body);
        var (sequence, type, fields) = ReadAce(body, 2);
        Assert.Equal((8u, 0x004Au), (sequence, type));
        Assert.Equal(new uint[] { 0x80000123, 0x0B50 }, fields);
    }

    [Fact]
    public void ChangeCombatMode_is_0x0053_then_the_mode_bit()
    {
        // 8 is magic; ACE's CombatMode: 1 non-combat, 2 melee, 4 missile, 8 magic.
        var body = Build(w => MagicActionCodecs.WriteChangeCombatMode(w, 9, 8));

        Assert.Equal(Convert.FromHexString("B1F70000" + "09000000" + "53000000" + "08000000"), body);
        var (sequence, type, fields) = ReadAce(body, 1);
        Assert.Equal((9u, 0x0053u), (sequence, type));
        Assert.Equal(new uint[] { 8 }, fields);
    }

    [Fact]
    public void The_opcodes_are_the_retail_senders_values()
    {
        // CM_Magic::Event_CastUntargetedSpell, Event_CastTargetedSpell and CM_Combat::Event_ChangeCombatMode put 72, 74 and 83 in the type word.
        Assert.Equal([0x0048u, 0x004Au, 0x0053u], [GameActionTypes.CastUntargetedSpell, GameActionTypes.CastTargetedSpell, GameActionTypes.ChangeCombatMode]);
    }

    /// <summary>ACE's three handlers re-expressed: the F7B1 envelope (opcode, sequence, type), then <paramref name="count"/> UInt32 fields, nothing after.</summary>
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
