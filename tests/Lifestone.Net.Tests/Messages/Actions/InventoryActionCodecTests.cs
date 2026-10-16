using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The wield and move-to-pack game actions (M3 1.2 s4a) pinned byte for byte against ACE's handlers (v1.78.4816): F7B1, the action sequence,
/// the type, then little-endian 32-bit fields. <c>GameActionGetAndWieldItem</c> reads UInt32 (item) then Int32 (the <c>EquipMask</c>);
/// <c>GameActionPutItemInContainer</c> reads UInt32 (item), UInt32 (container) then Int32 (placement). The signed reads take the same four bytes
/// the codecs write as unsigned.
/// </summary>
public sealed class InventoryActionCodecTests
{
    [Fact]
    public void GetAndWieldItem_is_0x001A_item_then_the_equip_mask()
    {
        // 0x01000000 is EquipMask.Held (a caster).
        var body = Build(w => InventoryActionCodecs.WriteGetAndWieldItem(w, 4, 0x80000200, 0x01000000));

        Assert.Equal(Convert.FromHexString("B1F70000" + "04000000" + "1A000000" + "00020080" + "00000001"), body);
        var (sequence, type, fields) = ReadAce(body, 2);
        Assert.Equal((4u, 0x001Au), (sequence, type));
        Assert.Equal(new uint[] { 0x80000200, 0x01000000 }, fields);
    }

    [Fact]
    public void PutItemInContainer_is_0x0019_item_container_then_placement()
    {
        var body = Build(w => InventoryActionCodecs.WritePutItemInContainer(w, 5, 0x80000200, 0x50000001, 3));

        Assert.Equal(Convert.FromHexString("B1F70000" + "05000000" + "19000000" + "00020080" + "01000050" + "03000000"), body);
        var (sequence, type, fields) = ReadAce(body, 3);
        Assert.Equal((5u, 0x0019u), (sequence, type));
        Assert.Equal(new uint[] { 0x80000200, 0x50000001, 3 }, fields);
    }

    [Fact]
    public void The_placement_is_the_same_four_bytes_as_aces_signed_read()
    {
        // ACE reads ReadInt32; a placement of -1 and 0xFFFFFFFF are the same bytes.
        var body = Build(w => InventoryActionCodecs.WritePutItemInContainer(w, 1, 1, 1, 0xFFFFFFFF));

        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(20)));
    }

    [Fact]
    public void StackableMerge_is_0x0054_from_to_then_the_amount()
    {
        // M3 1.4 fix A (R2): ACE GameActionStackableMerge reads UInt32 (from), UInt32 (to), Int32 (amount).
        var body = Build(w => InventoryActionCodecs.WriteStackableMerge(w, 6, 0x80000300, 0x80000200, 12));

        Assert.Equal(Convert.FromHexString("B1F70000" + "06000000" + "54000000" + "00030080" + "00020080" + "0C000000"), body);
        var (sequence, type, fields) = ReadAce(body, 3);
        Assert.Equal((6u, 0x0054u), (sequence, type));
        Assert.Equal(new uint[] { 0x80000300, 0x80000200, 12 }, fields);
    }

    [Fact]
    public void The_merge_amount_is_the_same_four_bytes_as_aces_signed_read()
    {
        var body = Build(w => InventoryActionCodecs.WriteStackableMerge(w, 1, 1, 1, -1));

        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(20)));
    }

    [Fact]
    public void The_opcodes_are_the_retail_senders_values()
    {
        // CM_Inventory::Event_GetAndWieldItem, Event_PutItemInContainer and Event_StackableMerge put 26, 25 and 84 in the type word.
        Assert.Equal([0x001Au, 0x0019u, 0x0054u], [GameActionTypes.GetAndWieldItem, GameActionTypes.PutItemInContainer, GameActionTypes.StackableMerge]);
    }

    /// <summary>ACE's two handlers re-expressed: the F7B1 envelope (opcode, sequence, type), then <paramref name="count"/> 32-bit fields, nothing after.</summary>
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
