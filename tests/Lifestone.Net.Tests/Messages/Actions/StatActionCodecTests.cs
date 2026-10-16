using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The stat game actions (M3 1.1 s1a) pinned byte for byte against ACE's handlers: each is F7B1, the action sequence, the
/// type, then two 32-bit fields, little-endian, 20 bytes in all. ACE's <c>GameActionRaiseAttribute</c>, <c>GameActionRaiseVital</c>
/// and <c>GameActionRaiseSkill</c> read <c>ReadUInt32</c> twice; <c>GameActionTrainSkill</c> reads <c>ReadUInt32</c> then <c>ReadInt32</c>.
/// The mirror in <see cref="ReadAce"/> re-expresses those handlers, so a codec that drifted from ACE fails here and not on a live server.
/// </summary>
public sealed class StatActionCodecTests
{
    [Fact]
    public void RaiseAttribute_is_0x0045_attribute_then_xp()
    {
        // F7B1, sequence 3, type 0x45, attribute 5 (Focus), xp 1000 (0x3E8).
        var body = Build(w => StatActionCodecs.WriteRaiseAttribute(w, 3, 5, 1000));

        Assert.Equal(Convert.FromHexString("B1F70000" + "03000000" + "45000000" + "05000000" + "E8030000"), body);
        Assert.Equal((3u, 0x0045u, 5u, 1000u), ReadAce(body));
    }

    [Fact]
    public void RaiseVital_is_0x0044_vital_then_xp()
    {
        // The vital is named by its maximum's id: 5 is mana.
        var body = Build(w => StatActionCodecs.WriteRaiseVital(w, 4, 5, 77));

        Assert.Equal(Convert.FromHexString("B1F70000" + "04000000" + "44000000" + "05000000" + "4D000000"), body);
        Assert.Equal((4u, 0x0044u, 5u, 77u), ReadAce(body));
    }

    [Fact]
    public void RaiseSkill_is_0x0046_skill_then_xp()
    {
        // Skill 34 (0x22) is War Magic.
        var body = Build(w => StatActionCodecs.WriteRaiseSkill(w, 5, 34, 123456));

        Assert.Equal(Convert.FromHexString("B1F70000" + "05000000" + "46000000" + "22000000" + "40E20100"), body);
        Assert.Equal((5u, 0x0046u, 34u, 123456u), ReadAce(body));
    }

    [Fact]
    public void TrainSkill_is_0x0047_skill_then_a_signed_credit_count()
    {
        // Skill 31 (0x1F) is Creature Enchantment; 4 credits.
        var body = Build(w => StatActionCodecs.WriteTrainSkill(w, 6, 31, 4));

        Assert.Equal(Convert.FromHexString("B1F70000" + "06000000" + "47000000" + "1F000000" + "04000000"), body);
        Assert.Equal((6u, 0x0047u, 31u, 4u), ReadAce(body));
    }

    [Fact]
    public void TrainSkill_credits_are_signed_like_aces_ReadInt32()
    {
        var body = Build(w => StatActionCodecs.WriteTrainSkill(w, 1, 31, -2));

        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal(0xFFFFFFFEu, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
    }

    [Fact]
    public void The_xp_amount_is_unsigned_32_bit_so_the_xp_to_level_126_fits_in_one_raise()
    {
        // XpTable 0x0E000018: 4,286,609,098 is above int.MaxValue; an Int32 field would have turned it negative.
        const uint toLevel126 = 4_286_609_098;
        Assert.True(toLevel126 > int.MaxValue);

        var raise = Build(w => StatActionCodecs.WriteRaiseAttribute(w, 1, 1, toLevel126));
        Assert.Equal(toLevel126, ReadAce(raise).Second);
        Assert.Equal(Convert.FromHexString("CA7680FF"), raise[16..20]);
        Assert.Equal(toLevel126, ReadAce(Build(w => StatActionCodecs.WriteRaiseVital(w, 1, 1, toLevel126))).Second);
        Assert.Equal(toLevel126, ReadAce(Build(w => StatActionCodecs.WriteRaiseSkill(w, 1, 6, toLevel126))).Second);
    }

    [Fact]
    public void The_opcodes_are_the_retail_senders_values()
    {
        // CM_Train::Event_TrainAttribute2nd, Event_TrainAttribute, Event_TrainSkill and Event_TrainSkillAdvancementClass
        // put 68, 69, 70 and 71 in the message's type word.
        Assert.Equal([0x0044u, 0x0045u, 0x0046u, 0x0047u], [GameActionTypes.RaiseVital, GameActionTypes.RaiseAttribute, GameActionTypes.RaiseSkill, GameActionTypes.TrainSkill]);
    }

    /// <summary>ACE's four handlers re-expressed: the F7B1 envelope (opcode, sequence, type), then two 32-bit fields, nothing after.</summary>
    private static (uint Sequence, uint Type, uint First, uint Second) ReadAce(byte[] body)
    {
        Assert.Equal(20, body.Length);
        Assert.Equal(MessageOpcodes.GameAction, BinaryPrimitives.ReadUInt32LittleEndian(body));
        return (
            BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)),
            BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
