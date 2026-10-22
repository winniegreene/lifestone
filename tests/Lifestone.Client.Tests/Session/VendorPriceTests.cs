using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s1: the price rules of the open vendor (ACE <c>Vendor.GetSellCost</c> and <c>GetBuyCost</c>, W/Vendor.cs 573-609, in float arithmetic): you pay per created stack <c>max(1, ceil(SellPrice x Value - 0.1))</c>, the vendor pays
/// <c>max(1, floor(BuyPrice x Value + 0.1))</c> on the item's whole stack Value. The capture's figures (capture.md C1, C2): a Handy Healing Kit of Value 10 costs 14 and sells back for 9; the Leadership Ineptitude scroll of Value 1 pays 1.
/// </summary>
public sealed class VendorPriceTests
{
    private const float Buy = 0.9f;
    private const float Sell = 1.35f;

    private static WeenieDesc Desc(string name, uint wcid, uint itemType, uint value, ushort? stackSize = null, ushort? maxStack = null) => new()
    {
        Header = WeenieHeaderFlags.Value,
        Name = name,
        WeenieClassId = wcid,
        IconId = 0x06001234,
        ItemType = itemType,
        ObjectDescriptionFlags = 0,
        Value = value,
        StackSize = stackSize,
        MaxStackSize = maxStack,
    };

    /// <summary>A world object with an ItemType and a Value, the way a held item reads.</summary>
    private sealed class WorldObjectFixture(uint itemType, uint value)
    {
        public Lifestone.Client.Session.Contracts.WorldObject Object { get; } = Make(itemType, value);

        private static Lifestone.Client.Session.Contracts.WorldObject Make(uint itemType, uint value)
        {
            var basis = ReducerHarness.Create(0x80000001, ReducerHarness.Stamps(), null, name: "Thing", itemType: itemType);
            return new Lifestone.Client.Session.Contracts.WorldObject(basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Value, Value = value } }, false, 0);
        }
    }

    private static VendorStock Stock(float buy = Buy, float sell = Sell, params VendorEntry[] rows) =>
        new(new VendorApproachMsg(0x7A9B4027, 0x44080, 0, 1_000_000, true, buy, sell, 0, 0, string.Empty, [.. rows]), 1, 0);

    private static VendorEntry Row(uint guid, string name, uint value, int stack = -1, ushort? stackSize = null, ushort? maxStack = null, uint itemType = 0x80) =>
        new(stack, guid, Desc(name, 1, itemType, value, stackSize, maxStack));

    [Theory]
    [InlineData(10u, 14u)] // Handy Healing Kit: ceil(13.5 - 0.1)
    [InlineData(50u, 68u)] // Adept Healing Kit: ceil(67.5 - 0.1)
    [InlineData(170u, 230u)] // Potion of Healing: ceil(229.5 - 0.1)
    [InlineData(1u, 2u)] // ceil(1.35 - 0.1)
    [InlineData(0u, 1u)] // the floor: ceil(-0.1) is 0, the price is at least 1
    public void A_one_item_row_costs_the_ceiling_of_the_rate_times_the_value_less_a_tenth_and_at_least_one(uint value, uint price)
    {
        var stock = Stock(rows: Row(1, "Thing", value));

        Assert.Equal(price, stock.PriceToBuy(stock.Entries[0], 1));
        Assert.Equal(price, stock.PriceToBuy(1, 1));
    }

    [Fact]
    public void The_capture_prices_hold_for_the_two_kits_and_the_potion()
    {
        var stock = Stock(rows: [Row(1, "Handy Healing Kit", 10), Row(2, "Adept Healing Kit", 50), Row(3, "Potion of Healing", 170)]);

        Assert.Equal(14u, stock.PriceToBuy(1, 1));
        Assert.Equal(68u, stock.PriceToBuy(2, 1));
        Assert.Equal(230u, stock.PriceToBuy(3, 1));
    }

    [Fact]
    public void Buying_several_of_an_item_that_does_not_stack_is_one_priced_object_each()
    {
        var stock = Stock(rows: Row(1, "Handy Healing Kit", 10));

        Assert.Equal(14u * 3, stock.PriceToBuy(1, 3));
        Assert.Equal(0u, stock.PriceToBuy(1, 0));
    }

    [Fact]
    public void A_stackable_is_priced_per_created_stack_at_the_unit_value_times_its_size()
    {
        // An arrow of Value 5 stacks to 25: 30 arrows are a stack of 25 (Value 125: ceil(168.75 - 0.1) = 169) and a stack of 5 (Value 25: ceil(33.75 - 0.1) = 34).
        var stock = Stock(rows: Row(1, "Arrow", 5, stackSize: 1, maxStack: 25, itemType: 0x100));

        Assert.Equal(169u, stock.PriceToBuy(1, 25));
        Assert.Equal(169u + 34u, stock.PriceToBuy(1, 30));
        Assert.Equal(34u, stock.PriceToBuy(1, 5));
    }

    [Fact]
    public void A_guid_the_vendor_does_not_list_has_no_price()
    {
        var stock = Stock(rows: Row(1, "Handy Healing Kit", 10));

        Assert.Null(stock.PriceToBuy(99, 1));
        Assert.Null(stock.Find(99));
    }

    [Theory]
    [InlineData(10u, 9u)] // the kit sold back: floor(9.0 + 0.1), capture C1
    [InlineData(1u, 1u)] // the scroll: floor(0.9 + 0.1) = 1, capture C2
    [InlineData(0u, 1u)] // the floor: max(1, 0)
    [InlineData(2329u, 2096u)] // Leather Bracers: floor(2096.1 + 0.1)
    [InlineData(4000u, 3600u)] // an Essence (50) at Value 4,000
    public void The_payout_is_the_floor_of_the_rate_times_the_value_plus_a_tenth_and_at_least_one(uint value, uint payout)
    {
        var stock = Stock();

        Assert.Equal(payout, stock.PayoutFor(0x80, value));
    }

    [Fact]
    public void A_stack_is_paid_on_its_whole_stack_value_whatever_amount_a_sale_names()
    {
        // Five scrolls of Value 1 each are one stack of Value 5: the vendor pays floor(4.5 + 0.1) = 4 for the stack, where five separate scrolls would pay 5 x max(1, 1) = 5.
        var stock = Stock();

        Assert.Equal(4u, stock.PayoutFor(0x80, 5));
        Assert.Equal(5u, 5 * stock.PayoutFor(0x80, 1));
    }

    [Fact]
    public void The_payout_of_an_object_reads_its_current_stack_value()
    {
        var stock = Stock();
        var obj = new WorldObjectFixture(0x80, 50).Object;

        Assert.Equal(45u, stock.PayoutFor(obj));
        Assert.True(stock.Buys(obj));
        Assert.False(stock.Buys(new WorldObjectFixture(0x80, 0).Object)); // ACE: "has no value and cannot be sold"
        Assert.False(stock.Buys(new WorldObjectFixture(0x2, 500).Object)); // armour is not in Thelnoth's mask
    }

    [Fact]
    public void A_promissory_note_has_ACEs_own_rates_whatever_the_vendors()
    {
        var stock = Stock(buy: 0.5f, sell: 3f);

        Assert.Equal(100u, stock.PayoutFor(VendorStock.PromissoryNoteType, 100)); // rate 1.0: floor(100.1)
        Assert.Equal(115u, stock.PriceOfStack(100, VendorStock.PromissoryNoteType)); // rate 1.15: ceil(114.99999 - 0.1)... see below
    }

    [Theory]
    [InlineData(0x80u, true)]
    [InlineData(0x4000u, true)]
    [InlineData(0x40000u, true)]
    [InlineData(0x2u, false)]
    [InlineData(0x2000u, false)]
    [InlineData(0u, false)]
    public void BuysType_is_the_mask_test_of_the_vendors_item_types(uint itemType, bool buys)
    {
        var stock = Stock();

        Assert.Equal(buys, stock.BuysType(itemType)); // Thelnoth's mask 0x44080: Misc, Key, PromissoryNote
        Assert.Equal(0x44080u, stock.Mask);
    }
}
