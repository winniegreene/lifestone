using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Vendor;

/// <summary>
/// GameEvent 0x0062 ApproachVendor as ACE writes it (<c>GameEventApproachVendor.cs:14-63</c>): the profile, the alternate
/// currency, then per row an i32 packed stack, the guid and a WeenieDesc. The stock rule of OpenAC #218 / m4-plan D16: every row
/// the message carries is listed and the packed 0xFFFFFF is unlimited, never 0.
/// </summary>
public sealed class VendorCodecTests
{
    private const uint Player = 0x50000001;
    private const uint Thelnoth = 0x7A9B4F00;
    private const uint HealingKit = 0x7A9B5001;
    private const uint Arrows = 0x7A9B5002;
    private const uint Potion = 0x7A9B5003;

    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    [Fact]
    public void A_vendor_with_no_stock_matches_the_hand_built_bytes()
    {
        // F7B0 {player, sequence 5, type 0x0062}, then ACE's writer: vendor, mask 0x44080, min 0, max 1,000,000, deals magic 0,
        // BuyPrice 0.9, SellPrice 1.35, alternate wcid 0, count 0, an empty String16L (u16 0 and two pad bytes), numItems 0.
        var expected = Convert.FromHexString(
            "B0F70000" + "01000050" + "05000000" + "62000000" +
            "004F9B7A" + "80400400" + "00000000" + "40420F00" + "00000000" +
            "6666663F" + "CDCCAC3F" + "00000000" + "00000000" + "00000000" + "00000000");

        Assert.Equal(expected, Vendor(rows: []));
        Assert.True(VendorCodecs.TryParseApproachVendor(expected, out var vendor, out var reason), reason);
        Assert.Equal(Thelnoth, vendor.Vendor);
        Assert.Equal(0x44080u, vendor.MerchandiseItemTypes);
        Assert.Equal(0u, vendor.MerchandiseMinValue);
        Assert.Equal(1_000_000u, vendor.MerchandiseMaxValue);
        Assert.False(vendor.DealMagicalItems);
        Assert.Equal(0.9f, vendor.BuyPrice);
        Assert.Equal(1.35f, vendor.SellPrice);
        Assert.Equal(0u, vendor.AlternateCurrencyWcid);
        Assert.Equal(0u, vendor.AlternateCurrencyCount);
        Assert.Equal(string.Empty, vendor.AlternateCurrencyPluralName);
        Assert.Empty(vendor.Items);
    }

    [Fact]
    public void Three_rows_keep_their_stack_guid_and_description_in_order()
    {
        var message = Vendor(rows:
        [
            new AceVendorRow(-1, Item(HealingKit, "Handy Healing Kit", 628, itemType: 0x80, value: 10)),
            new AceVendorRow(25, Item(Arrows, "Arrow", 300, itemType: 0x100, value: 5, stackSize: 25)),
            new AceVendorRow(-1, Item(Potion, "Potion of Healing", 377, itemType: 0x80, value: 170)),
        ]);

        Assert.True(VendorCodecs.TryParseApproachVendor(message, out var vendor, out var reason), reason);

        Assert.Equal([-1, 25, -1], vendor.Items.Select(i => i.StackSize));
        Assert.Equal([HealingKit, Arrows, Potion], vendor.Items.Select(i => i.Guid));
        Assert.Equal(["Handy Healing Kit", "Arrow", "Potion of Healing"], vendor.Items.Select(i => i.Desc.Name));
        Assert.Equal([628u, 300u, 377u], vendor.Items.Select(i => i.Desc.WeenieClassId));
        Assert.Equal([0x80u, 0x100u, 0x80u], vendor.Items.Select(i => i.Desc.ItemType));
        Assert.Equal([10u, 5u, 170u], vendor.Items.Select(i => i.Desc.Value ?? 0));
        Assert.Equal((ushort?)25, vendor.Items[1].Desc.StackSize);
        Assert.Equal([true, false, true], vendor.Items.Select(i => i.IsUnlimited));
    }

    [Fact]
    public void An_unlimited_row_is_listed_and_is_not_zero()
    {
        // #218 (OpenAC reverted "zero stock is unlimited"): ACE's -1 arrives as 0xFFFFFFFF, and every row is listed.
        var message = Vendor(rows:
        [
            new AceVendorRow(-1, Item(HealingKit, "Handy Healing Kit", 628, 0x80, 10)),
            new AceVendorRow(0xFFFFFF, Item(Potion, "Potion of Healing", 377, 0x80, 170)),
            new AceVendorRow(0, Item(Arrows, "Arrow", 300, 0x100, 5)),
        ]);

        Assert.True(VendorCodecs.TryParseApproachVendor(message, out var vendor, out _));

        Assert.Equal(3, vendor.Items.Length);
        Assert.Equal(VendorEntry.Unlimited, vendor.Items[0].StackSize);
        Assert.NotEqual(0, vendor.Items[0].StackSize);
        Assert.True(vendor.Items[0].IsUnlimited);
        Assert.Equal(VendorEntry.Unlimited, vendor.Items[1].StackSize);
        Assert.True(vendor.Items[1].IsUnlimited);

        // A finite 0 is a listed row of 0, not unlimited.
        Assert.Equal(0, vendor.Items[2].StackSize);
        Assert.False(vendor.Items[2].IsUnlimited);
    }

    [Fact]
    public void The_packed_word_is_the_stack_in_24_bits_and_pwdType_ff_on_the_wire()
    {
        var message = Vendor(rows: [new AceVendorRow(-1, Item(HealingKit, "Kit", 628, 0x80, 10)), new AceVendorRow(7, Item(Arrows, "Arrow", 300, 0x100, 5))]);

        // The first row starts right after numItems: bytes 16 + 40 + 4 (envelope, the profile, the count).
        var first = 16 + 4 * 5 + 8 + 4 + 4 + 4 + 4;
        Assert.Equal(0xFFFFFFFFu, BitConverter.ToUInt32(message, first));
        Assert.Equal(HealingKit, BitConverter.ToUInt32(message, first + 4));
        Assert.True(VendorCodecs.TryParseApproachVendor(message, out var vendor, out _));
        Assert.Equal(7, vendor.Items[1].StackSize);
    }

    [Fact]
    public void The_alternate_currency_and_the_deal_magic_flag_are_read()
    {
        var message = Vendor(rows: [], deals: true, altWcid: 9999, altCount: 12, altName: "Gold Coins");

        Assert.True(VendorCodecs.TryParseApproachVendor(message, out var vendor, out _));
        Assert.True(vendor.DealMagicalItems);
        Assert.Equal(9999u, vendor.AlternateCurrencyWcid);
        Assert.Equal(12u, vendor.AlternateCurrencyCount);
        Assert.Equal("Gold Coins", vendor.AlternateCurrencyPluralName);
    }

    [Fact]
    public void A_truncated_or_overlong_message_is_refused_with_a_reason_and_never_throws()
    {
        var whole = Vendor(rows: [new AceVendorRow(-1, Item(HealingKit, "Kit", 628, 0x80, 10)), new AceVendorRow(3, Item(Arrows, "Arrow", 300, 0x100, 5))]);

        for (var length = 0; length < whole.Length; length++)
        {
            Assert.False(VendorCodecs.TryParseApproachVendor(whole.AsSpan(0, length), out var vendor, out var reason), $"a prefix of {length} bytes was accepted");
            Assert.Null(vendor);
            Assert.NotEmpty(reason);
        }

        Assert.False(VendorCodecs.TryParseApproachVendor([.. whole, 0], out _, out var trailing));
        Assert.Contains("unread", trailing, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stock_count_beyond_the_body_is_refused_before_anything_is_sized_from_it()
    {
        var whole = Vendor(rows: []);
        var lying = (byte[])whole.Clone();
        BitConverter.GetBytes(uint.MaxValue).CopyTo(lying, whole.Length - 4);

        Assert.False(VendorCodecs.TryParseApproachVendor(lying, out _, out var reason));
        Assert.Contains("stock count", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pwdType_other_than_ff_is_refused_by_name()
    {
        var message = Vendor(rows: [new AceVendorRow(5, Item(HealingKit, "Kit", 628, 0x80, 10))]);
        var first = 16 + 4 * 5 + 8 + 4 + 4 + 4 + 4;
        message[first + 3] = 0x01;

        Assert.False(VendorCodecs.TryParseApproachVendor(message, out _, out var reason));
        Assert.Contains("TODO(m4-vendor-old-pwd)", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dispatcher_reaches_OnApproachVendor_with_the_record()
    {
        var handler = new Recorder();
        var message = Vendor(rows: [new AceVendorRow(-1, Item(HealingKit, "Handy Healing Kit", 628, 0x80, 10))]);

        var result = InboundDispatcher.Dispatch(Context, message, handler);

        Assert.Equal(new DispatchResult(DispatchOutcome.Decoded, MessageOpcodes.GameEvent, 0x0062), result);
        Assert.Equal(["vendor:7A9B4F00:1 rows:-1:seq5"], handler.Calls);
    }

    [Fact]
    public void A_bad_body_is_malformed_with_the_message_named_and_reaches_no_callback()
    {
        var handler = new Recorder();
        var message = Vendor(rows: [new AceVendorRow(-1, Item(HealingKit, "Kit", 628, 0x80, 10))]);

        var result = InboundDispatcher.Dispatch(Context, message.AsSpan(0, message.Length - 3), handler);

        Assert.Equal(DispatchOutcome.Malformed, result.Outcome);
        Assert.Single(handler.Malformed);
        Assert.StartsWith("F7B0 GameEvent 0x0062 ApproachVendor: ", handler.Malformed[0], StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }

    private static byte[] Vendor(IReadOnlyList<AceVendorRow> rows, bool deals = false, uint altWcid = 0, uint altCount = 0, string altName = "") =>
        AceVendorWriters.ApproachVendor(Player, 5, Thelnoth, 0x44080, 0, 1_000_000, deals, 0.9f, 1.35f, rows, altWcid, altCount, altName);

    private static AceCreateObject Item(uint guid, string name, uint wcid, uint itemType, int value, ushort? stackSize = null) =>
        new() { Guid = guid, Name = name, WeenieClassId = wcid, Icon = 0x06001234, ItemType = itemType, Value = value, StackSize = stackSize };

    private sealed class Recorder : IInboundMessageHandler
    {
        public List<string> Calls { get; } = [];

        public List<string> Malformed { get; } = [];

        public void OnApproachVendor(in InboundContext context, in GameEventHeader header, VendorApproachMsg message) =>
            Calls.Add($"vendor:{message.Vendor:X8}:{message.Items.Length} rows:{string.Join(',', message.Items.Select(i => i.StackSize))}:seq{header.Sequence}");

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
    }
}
