using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s1: the stock model and OpenAC #218 (m4-plan D16). Upstream reverted "zero stock is unlimited" on 2026-10-19 (1c08ff7) for "list every shop row at open, whatever its amount" (38ef960); ACE's own rule, which the gate reads
/// (<c>W/Vendor.cs</c>, spec 5.8): the 0062 carries every CreateList row, the packed stack 0xFFFFFF is unlimited (ACE's -1) and a listed 0 is a 0. Fed through ACE's own 0062 writer and the real decoder.
/// </summary>
public sealed class VendorStockTests
{
    [Fact]
    public void OpenAC_218_every_CreateList_row_the_0062_carries_is_listed_in_the_servers_order()
    {
        var rig = new VendorRig();
        var rows = VendorRig.ThelnothRows();

        rig.ThelnothStock();

        var stock = Assert.IsType<VendorStock>(rig.State.Vendor);
        Assert.Equal(rows.Length, stock.Entries.Length);
        Assert.Equal(rows.Select(r => r.Item.Guid), stock.Entries.Select(e => e.Guid));
        Assert.Equal(rows.Select(r => r.Item.Name), stock.Entries.Select(e => e.Name));
        Assert.Equal(rows.Select(r => r.Item.WeenieClassId), stock.Entries.Select(e => e.WeenieClassId));
        Assert.Equal(rows.Select(r => (uint)r.Item.Value!.Value), stock.Entries.Select(e => e.Value));
        Assert.Equal(rows.Select(r => r.Item.ItemType), stock.Entries.Select(e => e.ItemType));
        Assert.All(stock.Entries, e => Assert.Equal(0x06001234u, e.IconId));
    }

    [Fact]
    public void OpenAC_218_the_packed_0xFFFFFF_reads_unlimited_and_is_buyable_at_any_amount()
    {
        var rig = new VendorRig();
        rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, [VendorRig.Row(0xFFFFFF, VendorRig.KitRow, "Handy Healing Kit", 628, VendorRig.MiscType, 10)]);

        var entry = Assert.Single(rig.State.Vendor!.Entries);

        Assert.Equal(VendorEntry.Unlimited, entry.Stack);
        Assert.True(entry.IsUnlimited);
        Assert.True(entry.IsBuyable(1));
        Assert.True(entry.IsBuyable(500));
        Assert.True(entry.IsBuyable(uint.MaxValue / 2));
        Assert.False(entry.IsBuyable(0));
    }

    [Fact]
    public void OpenAC_218_a_listed_zero_stays_zero_is_not_unlimited_and_is_not_buyable()
    {
        var rig = new VendorRig();
        rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, [VendorRig.Row(0, VendorRig.ZeroRow, "Out of Stock", 999, VendorRig.MiscType, 7)]);

        var entry = Assert.Single(rig.State.Vendor!.Entries); // listed, not dropped

        Assert.Equal(0, entry.Stack);
        Assert.False(entry.IsUnlimited);
        Assert.False(entry.IsBuyable(1));
        Assert.False(entry.IsBuyable(0));
    }

    [Fact]
    public void A_limited_row_is_buyable_up_to_its_stack_and_not_beyond()
    {
        var rig = new VendorRig();
        rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, [VendorRig.Row(3, VendorRig.KitRow, "Handy Healing Kit", 628, VendorRig.MiscType, 10)]);

        var entry = Assert.Single(rig.State.Vendor!.Entries);

        Assert.True(entry.IsBuyable(3));
        Assert.False(entry.IsBuyable(4));
    }

    [Fact]
    public void The_stock_carries_the_rates_the_mask_the_value_range_and_the_alternate_currency()
    {
        var rig = new VendorRig();

        rig.ThelnothStock();

        var stock = rig.State.Vendor!;
        Assert.Equal(VendorRig.Thelnoth, stock.Vendor);
        Assert.Equal(0.9f, stock.BuyPrice);
        Assert.Equal(1.35f, stock.SellPrice);
        Assert.Equal(VendorRig.ThelnothMask, stock.Mask);
        Assert.Equal(0u, stock.MinValue);
        Assert.Equal(1_000_000u, stock.MaxValue);
        Assert.True(stock.DealMagicalItems);
        Assert.Equal(0u, stock.AlternateCurrencyWcid);
        Assert.Equal(0u, stock.AlternateCurrencyCount);
        Assert.Equal(string.Empty, stock.AlternateCurrencyPluralName);
        Assert.Same(stock, rig.H.R.State.Vendor);
    }

    [Fact]
    public void Mask_is_what_LootRules_reads_and_BuysType_applies_it_Renalds_wire_value_is_the_shards()
    {
        // Capture C2: Renald's 0062 carries 0x480467A7, the value LootRules reads from the shard today (m4-loot-rules-shop-mask).
        var rig = new VendorRig();
        rig.Stock(VendorRig.Renald, VendorRig.RenaldMask, []);

        var stock = rig.State.Vendor!;

        Assert.Equal(0x480467A7u, stock.Mask);
        Assert.True(stock.BuysType(0x2)); // armour
        Assert.True(stock.BuysType(0x1)); // melee weapons
        Assert.True(stock.BuysType(0x2000)); // a scroll (Writable): capture C2 sold one
        Assert.False(stock.BuysType(0x10)); // creatures
        Assert.False(stock.BuysType(0));
    }

    [Fact]
    public void The_view_exposes_the_open_vendor()
    {
        var rig = new VendorRig();
        var view = (IGameSessionView)new ViewOf(rig.State);

        Assert.Null(view.Vendor);
        rig.ThelnothStock();
        Assert.Same(rig.State.Vendor, view.Vendor);
    }

    private sealed class ViewOf(GameState state) : IGameSessionView
    {
        public GameState State => state;

        public IEventBus Events => throw new NotSupportedException();

        public IActionApi Actions => throw new NotSupportedException();

        public PlayerTools Tools => throw new NotSupportedException();

        public SessionClock Clock => throw new NotSupportedException();

        public ActionLog ActionLog => throw new NotSupportedException();

        public Lifestone.Net.Diagnostics.LinkStats Link => throw new NotSupportedException();
    }
}
