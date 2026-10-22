using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Tests.Session;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Messages.Actions;

/// <summary>
/// The vendor and appraisal game actions (M4 3.2 s0) against hand-built bytes in ACE's reader order: 0x005F BuyItems and 0x0060
/// SellItems (<c>GameActionBuyItems.cs:12-34</c>, <c>GameActionSellItems.cs:12-32</c>: u32 vendor, u32 count, then per item an Int32
/// amount and a u32 guid, nothing after the last item) and 0x00C8 IdentifyObject (<c>GameActionIdentifyObject.cs:9-11</c>: u32),
/// behind the F7B1 envelope {opcode, sequence, type} every send uses. The amount comes BEFORE the guid.
/// </summary>
public sealed class VendorActionCodecTests
{
    private const string Envelope = "B1F70000";
    private const uint Vendor = 0x7A9B4F00;

    [Fact]
    public void Buy_is_vendor_count_then_amount_and_item_per_row_with_nothing_after()
    {
        var body = Build(w => VendorActionCodecs.WriteBuy(w, 9, Vendor, [(0x7A9B5001, 2), (0x7A9B5003, 1)]));

        Assert.Equal(
            Convert.FromHexString(Envelope + "09000000" + "5F000000" + "004F9B7A" + "02000000" + "02000000" + "01509B7A" + "01000000" + "03509B7A"),
            body);
    }

    [Fact]
    public void Sell_has_the_same_layout_under_type_0060()
    {
        var body = Build(w => VendorActionCodecs.WriteSell(w, 10, Vendor, [(0x80000100, 1)]));

        Assert.Equal(Convert.FromHexString(Envelope + "0A000000" + "60000000" + "004F9B7A" + "01000000" + "01000000" + "00010080"), body);
    }

    [Fact]
    public void An_empty_list_is_a_count_of_zero()
    {
        Assert.Equal(
            Convert.FromHexString(Envelope + "01000000" + "5F000000" + "004F9B7A" + "00000000"),
            Build(w => VendorActionCodecs.WriteBuy(w, 1, Vendor, [])));
    }

    [Fact]
    public void Identify_is_envelope_and_the_guid()
    {
        var body = Build(w => VendorActionCodecs.WriteIdentify(w, 11, 0x80000ABC));

        Assert.Equal(Convert.FromHexString(Envelope + "0B000000" + "C8000000" + "BC0A0080"), body);
    }

    [Fact]
    public void An_amount_ACE_would_read_as_negative_is_refused_before_anything_is_written()
    {
        var writer = new PacketWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => VendorActionCodecs.WriteBuy(writer, 1, Vendor, [(0x7A9B5001, 1), (0x7A9B5003, 0x80000000)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorActionCodecs.WriteSell(writer, 1, Vendor, [(0x7A9B5001, uint.MaxValue)]));
        Assert.Throws<ArgumentNullException>(() => VendorActionCodecs.WriteBuy(writer, 1, Vendor, null!));
        Assert.Equal(0, writer.Position);
        Assert.Equal(
            Convert.FromHexString(Envelope + "01000000" + "5F000000" + "004F9B7A" + "01000000" + "FFFFFF7F" + "01509B7A"),
            Build(w => VendorActionCodecs.WriteBuy(w, 1, Vendor, [(0x7A9B5001, int.MaxValue)])));
    }

    [Fact]
    public void The_opcodes_are_the_ones_ace_registers()
    {
        Assert.Equal([0x005Fu, 0x0060u, 0x00C8u], [GameActionTypes.BuyItems, GameActionTypes.SellItems, GameActionTypes.IdentifyObject]);
        Assert.Equal([0x0062u, 0x00C9u], [GameEventTypes.ApproachVendor, GameEventTypes.IdentifyObjectResponse]);
    }

    [Fact]
    public void The_session_sends_the_three_on_the_weenie_queue_with_consecutive_sequences()
    {
        using var rig = new SessionRig();
        rig.ToWorld();
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        rig.Session.SendBuy(Vendor, [(0x7A9B5001, 2)]);
        rig.Session.SendSell(Vendor, [(0x80000100, 1), (0x80000101, 3)]);
        rig.Session.SendIdentify(0x80000ABC);
        rig.RunFor(TimeSpan.FromMilliseconds(100));

        var actions = rig.Tap.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction)
            .ToList();
        Assert.Equal(4, actions.Count);
        Assert.All(actions, a => Assert.Equal(MessageQueue.Weenie, a.Queue));
        Assert.Equal(new uint[] { 0x00A1, 0x005F, 0x0060, 0x00C8 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 8)));
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, actions.Select(a => BitConverter.ToUInt32(a.Body, 4)));
        Assert.Equal(Convert.FromHexString(Envelope + "02000000" + "5F000000" + "004F9B7A" + "01000000" + "02000000" + "01509B7A"), actions[1].Body);
        Assert.Equal(
            Convert.FromHexString(Envelope + "03000000" + "60000000" + "004F9B7A" + "02000000" + "01000000" + "00010080" + "03000000" + "01010080"),
            actions[2].Body);
        Assert.Equal(Convert.FromHexString(Envelope + "04000000" + "C8000000" + "BC0A0080"), actions[3].Body);
    }

    [Fact]
    public void A_refused_amount_uses_no_sequence_and_the_sends_need_the_world()
    {
        using var rig = new SessionRig();
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendBuy(Vendor, [(1, 1)])).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendSell(Vendor, [(1, 1)])).Message, StringComparison.Ordinal);
        Assert.Contains("InWorld", Assert.Throws<InvalidOperationException>(() => rig.Session.SendIdentify(1)).Message, StringComparison.Ordinal);

        using var world = new SessionRig();
        world.ToWorld();
        world.Session.SendLoginComplete();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Session.SendBuy(Vendor, [(1, 0x80000000)]));
        world.Session.SendIdentify(7);
        world.RunFor(TimeSpan.FromMilliseconds(100));

        var identify = world.Tap.Messages.Single(m => m.Direction == MessageDirection.Outbound && BitConverter.ToUInt32(m.Body) == MessageOpcodes.GameAction && BitConverter.ToUInt32(m.Body, 8) == 0x00C8);
        Assert.Equal(2u, BitConverter.ToUInt32(identify.Body, 4));
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
