using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The sale rule (M4 3.2 s4a, <see cref="SellRules"/>; DoD 10 and 12): what Renald the Elder's mask takes, what is never sold (worn items, the fighter's kit, the template's weapons, arrows, pyreals, containers), a stack sold whole, and the
/// 0062's mask preferred over the shard's. Fed through the real reducer (<see cref="VendorRig"/>), so the inventory model and the stock are the ones the session builds.
/// </summary>
public sealed class SellRulesTests
{
    private const uint Armor = 0x2;
    private const uint Clothing = 0x4;
    private const uint Misc = 0x80;
    private const uint Scroll = 0x2000;
    private const uint Gem = 0x800;
    private const uint Money = 0x40;
    private const uint ContainerType = 0x200;
    private const uint Melee = 0x1;
    private const uint Missile = 0x100;
    private const uint ShardMask = 0x480467A7;
    private const uint Mace = 331;
    private const uint Shortbow = 307;
    private const uint Arrow = 300;
    private const uint HandyKit = 628;
    private const uint AdeptKit = 629;
    private const uint Potion = 377;
    private const uint Pyreal = 273;

    private sealed class Held
    {
        private readonly List<ContentDesc> _pack = [];
        private readonly List<EquippedDesc> _worn = [];
        private uint _next = 0x80000500;

        public Held() => Rig = new VendorRig();

        public VendorRig Rig { get; }

        public GameState State => Rig.State;

        /// <summary>An item the player holds in the pack.</summary>
        public WorldObject Pack(string name, uint wcid, uint itemType, uint value, ushort? stackSize = null, bool sidePack = false)
        {
            var guid = _next++;
            Rig.Arrive(guid, name, wcid, itemType, value, stackSize);
            _pack.Add(new ContentDesc(guid, sidePack ? 1u : 0u));
            Rig.R.Describe([.. _pack], [.. _worn]);
            return State.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException(name);
        }

        /// <summary>An item the player wields or wears.</summary>
        public WorldObject Worn(string name, uint wcid, uint itemType, uint value)
        {
            var guid = _next++;
            Rig.Arrive(guid, name, wcid, itemType, value);
            _worn.Add(new EquippedDesc(guid, 0x100000, 1));
            Rig.R.Describe([.. _pack], [.. _worn]);
            return State.Objects.TryGet(guid, out var o) ? o : throw new InvalidOperationException(name);
        }

        public VendorStock Stock(uint mask, uint min = 0, uint max = 1_000_000)
        {
            Rig.Stock(VendorRig.Thelnoth, mask, VendorRig.ThelnothRows(), min: min, max: max);
            return Rig.State.Vendor!;
        }

        public SellDecision Decide(WorldObject item, VendorStock? stock = null) => SellRules.Default.Decide(item, State.Self.Inventory, stock);
    }

    [Fact]
    public void An_item_of_a_type_the_shard_mask_takes_with_a_value_is_sold_and_one_outside_it_is_not()
    {
        var h = new Held();
        var armour = h.Pack("Leather Bracers", 36, Armor, 1100);
        var scroll = h.Pack("Scroll of Leadership Ineptitude", 9000, Scroll, 1);
        var gem = h.Pack("Garnet", 5000, Gem, 300);

        Assert.Equal(SellDecision.Sell, h.Decide(armour));
        Assert.Equal(SellDecision.Sell, h.Decide(scroll));
        Assert.Equal(SellDecision.Type, h.Decide(gem));
        Assert.Equal(ShardMask, SellRules.Default.FallbackMask);
        Assert.Equal(ShardMask, SellRules.Default.MaskOf(null));
        Assert.False(SellRules.FromWire(null));
    }

    [Fact]
    public void The_0062s_mask_is_preferred_over_the_shards_when_a_stock_is_read()
    {
        var h = new Held();
        var armour = h.Pack("Leather Bracers", 36, Armor, 1100);
        var gem = h.Pack("Garnet", 5000, Gem, 300);
        var wire = h.Stock(Gem); // a vendor that buys only gems

        Assert.True(SellRules.FromWire(wire));
        Assert.Equal(Gem, SellRules.Default.MaskOf(wire));
        Assert.Equal(SellDecision.Sell, h.Decide(gem, wire));
        Assert.Equal(SellDecision.Type, h.Decide(armour, wire));
        Assert.Equal(SellDecision.Type, h.Decide(gem)); // with no stock the shard's mask is used: no gems
        Assert.Equal(SellDecision.Sell, h.Decide(armour));
        Assert.Contains("the vendor's 0062", SellRules.Default.Describe(wire), StringComparison.Ordinal);
        Assert.Contains("the shard's value, no stock read", SellRules.Default.Describe(null), StringComparison.Ordinal);
        Assert.Contains("0x00000800", SellRules.Default.Describe(wire), StringComparison.Ordinal);
    }

    [Fact]
    public void A_worn_item_the_kit_the_template_weapons_the_arrows_pyreals_and_containers_are_never_sold()
    {
        var h = new Held();
        var worn = h.Worn("Leather Cap", 41, Armor, 400);
        var wielded = h.Worn("Mace", Mace, Melee, 260);
        var handy = h.Pack("Handy Healing Kit", HandyKit, Misc, 10);
        var adept = h.Pack("Adept Healing Kit", AdeptKit, Misc, 50);
        var potion = h.Pack("Potion of Healing", Potion, Misc, 170, 5);
        var spareMace = h.Pack("Mace", Mace, Melee, 260);
        var bow = h.Pack("Shortbow", Shortbow, Missile, 200);
        var arrows = h.Pack("Arrow", Arrow, Missile, 200, 200);
        var coins = h.Pack("Pyreal", Pyreal, Money, 9000, 9000);
        var bag = h.Pack("Bag", 9001, ContainerType, 20);
        var sidePack = h.Pack("Small Pack", 9002, Armor, 20, sidePack: true);
        var loot = h.Pack("Smock", 2589, Clothing, 25);

        Assert.Equal(SellDecision.Equipped, h.Decide(worn));
        Assert.Equal(SellDecision.Equipped, h.Decide(wielded));
        Assert.Equal(SellDecision.Kept, h.Decide(handy));
        Assert.Equal(SellDecision.Kept, h.Decide(adept));
        Assert.Equal(SellDecision.Kept, h.Decide(potion));
        Assert.Equal(SellDecision.Kept, h.Decide(spareMace)); // by weenie class, whatever the instance: a spare Mace is the template's weapon too
        Assert.Equal(SellDecision.Kept, h.Decide(bow));
        Assert.Equal(SellDecision.Kept, h.Decide(arrows));
        Assert.Equal(SellDecision.Kept, h.Decide(coins)); // the pyreal class is kept before the money test
        Assert.Equal(SellDecision.Container, h.Decide(bag));
        Assert.Equal(SellDecision.Container, h.Decide(sidePack));
        Assert.Equal(SellDecision.Sell, h.Decide(loot));
        Assert.True(SellRules.IsKept(Pyreal) && SellRules.IsKept(HandyKit) && SellRules.IsKept(27326) && !SellRules.IsKept(2589));
    }

    [Fact]
    public void Spell_components_and_the_pack_caster_are_never_sold_even_to_a_vendor_whose_mask_takes_them_M4_P5a_F_abil()
    {
        var h = new Held();
        var scarab = h.Pack("Lead Scarab", 691, 0x1000, 20, 29);
        var caster = h.Pack("The Healer's Heart", 8552, 0x8000, 1500);
        var loot = h.Pack("Smock", 2589, Clothing, 25);
        var everything = new SellRules { FallbackMask = uint.MaxValue };

        Assert.Equal(SellDecision.Kept, everything.Decide(scarab, h.State.Self.Inventory, null));
        Assert.Equal(SellDecision.Kept, everything.Decide(caster, h.State.Self.Inventory, null));
        Assert.Equal(SellDecision.Sell, everything.Decide(loot, h.State.Self.Inventory, null));
    }

    [Fact]
    public void Money_that_is_not_the_pyreal_class_is_never_sold_either()
    {
        var h = new Held();
        var odd = h.Pack("Trade Coin", 9100, Money, 50);

        Assert.Equal(SellDecision.Money, h.Decide(odd));
    }

    [Fact]
    public void A_stack_is_one_item_and_is_sold_whole_by_its_own_value()
    {
        var h = new Held();
        var wire = h.Stock(ShardMask);
        var scrolls = h.Pack("Scrolls", 9000, Scroll, 5, 5); // five scrolls of Value 1 each: the stack's Value is 5

        var selected = new List<uint>();
        var count = SellRules.Default.Select(h.State, wire, selected, []);

        Assert.Equal(1, count);
        Assert.Equal([scrolls.Guid], selected);
        Assert.Equal(SellDecision.Sell, h.Decide(scrolls, wire));
        Assert.Equal(4u, wire.PayoutFor(scrolls)); // floor(0.9 x 5 + 0.1) on the whole stack's Value, as capture C2 and s1's handoff say
    }

    [Fact]
    public void The_values_must_be_at_least_one_and_inside_the_vendors_least_and_greatest()
    {
        var h = new Held();
        var free = h.Pack("Worthless", 9200, Misc, 0);
        var cheap = h.Pack("Cheap", 9201, Misc, 50);
        var rich = h.Pack("Rich", 9202, Misc, 1_000_001);
        var fine = h.Pack("Fine", 9203, Misc, 4000);

        Assert.Equal(SellDecision.Value, h.Decide(free));
        Assert.Equal(SellDecision.Value, h.Decide(rich));
        Assert.Equal(SellDecision.Sell, h.Decide(cheap));
        Assert.Equal(SellDecision.Sell, h.Decide(fine));
        var strict = h.Stock(ShardMask, min: 100, max: 5000);
        Assert.Equal(SellDecision.Value, h.Decide(cheap, strict));
        Assert.Equal(SellDecision.Sell, h.Decide(fine, strict));
        Assert.Equal(SellDecision.Value, h.Decide(h.Pack("Over", 9204, Misc, 5001), strict));
    }

    [Fact]
    public void Select_lists_what_sells_in_the_inventorys_order_and_counts_the_reasons()
    {
        var h = new Held();
        var wire = h.Stock(ShardMask);
        h.Worn("Mace", Mace, Melee, 260);
        var a = h.Pack("Bowl", 141, Misc, 10);
        h.Pack("Handy Healing Kit", HandyKit, Misc, 10);
        h.Pack("Garnet", 5000, Gem, 300);
        var b = h.Pack("Smock", 2589, Clothing, 25);
        h.Pack("Pyreal", Pyreal, Money, 9000, 9000);

        var selected = new List<uint> { 0xDEADBEEF }; // cleared first
        var skipped = new int[Enum.GetValues<SellDecision>().Length];
        var count = SellRules.Default.Select(h.State, wire, selected, [], skipped);

        Assert.Equal(2, count);
        Assert.Equal([a.Guid, b.Guid], selected);
        Assert.Equal(1, skipped[(int)SellDecision.Equipped]);
        Assert.Equal(2, skipped[(int)SellDecision.Kept]); // the kit and the pyreals
        Assert.Equal(1, skipped[(int)SellDecision.Type]);
        Assert.Equal(0, skipped[(int)SellDecision.Sell]);
        Assert.Equal("kept", SellRules.ReasonName(SellDecision.Kept));
        Assert.All(Enum.GetValues<SellDecision>(), d => Assert.NotEqual("unknown", SellRules.ReasonName(d)));
    }

    [Fact]
    public void An_item_the_player_does_not_hold_is_not_sold()
    {
        var h = new Held();
        h.Rig.Arrive(0x80000999, "Stray", 141, Misc, 10); // announced, but no pack list names it

        Assert.True(h.State.Objects.TryGet(0x80000999, out var stray));
        Assert.Equal(SellDecision.NotHeld, h.Decide(stray));
    }

    [Fact]
    public void LootRules_reads_the_open_vendors_mask_in_place_of_the_shards_only_for_the_default_shop_rule()
    {
        // m4-loot-rules-shop-mask closes: the shop rule takes the 0062's mask when a stock is read, a custom mask stays as asked
        var armour = LootRulesTests.Item(Armor, 100);
        var gem = LootRulesTests.Item(Gem, 100);
        var none = new LootContext(0, 1500, null);
        var gemsOnly = new LootContext(0, 1500, null, VendorMask: Gem);

        Assert.Equal(LootDecision.Take, LootRules.Default.Decide(armour, in none));
        Assert.Equal(LootDecision.Type, LootRules.Default.Decide(gem, in none));
        Assert.Equal(LootDecision.Type, LootRules.Default.Decide(armour, in gemsOnly));
        Assert.Equal(LootDecision.Take, LootRules.Default.Decide(gem, in gemsOnly));
        Assert.Equal(Gem, LootRules.Default.EffectiveMask(in gemsOnly));
        Assert.Equal(ShardMask, LootRules.Default.EffectiveMask(in none));

        var custom = new Lifestone.Client.Session.Fighter.LootOptions { TypeMask = Armor }.Rules();
        Assert.Equal(Armor, custom.EffectiveMask(in gemsOnly)); // a custom mask is the owner's
        Assert.Equal(LootDecision.Take, LootRules.TakeAll.Decide(gem, in gemsOnly));
        Assert.DoesNotContain("TODO(", LootRules.Default.MaskNote, StringComparison.Ordinal);
        Assert.Contains("m4-loot-rules-shop-mask closed", LootRules.Default.MaskNote, StringComparison.Ordinal);
    }

    [Fact]
    public void LootContext_Of_carries_Renalds_mask_from_the_state_and_an_open_Thelnoth_never_changes_it()
    {
        // 3.2-R16: the loot rule follows the LOOT vendor (weenie class 718), whichever vendor is open.
        var h = new Held();
        h.Rig.Place(VendorRig.Thelnoth, "Thelnoth Cort the Healer", x: 12, y: 20, wcid: 715);
        h.Rig.Place(VendorRig.Renald, "Shopkeeper Renald the Elder", x: 14, y: 20, wcid: LootOptions.ShopkeeperWcid);
        Assert.Null(LootContext.Of(h.State).VendorMask);

        h.Rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, VendorRig.ThelnothRows()); // Thelnoth open: his 0x44080 is not the loot rule's
        Assert.Equal(VendorRig.ThelnothMask, h.State.Vendor!.Mask);
        Assert.Null(LootContext.Of(h.State).VendorMask);

        h.Rig.Stock(VendorRig.Renald, Gem, VendorRig.ThelnothRows()); // Renald read: his mask is the rule's
        Assert.Equal(Gem, LootContext.Of(h.State).VendorMask);

        h.Rig.Stock(VendorRig.Thelnoth, VendorRig.ThelnothMask, VendorRig.ThelnothRows()); // Thelnoth opened after: Renald's mask stays
        Assert.Equal(VendorRig.ThelnothMask, h.State.Vendor!.Mask);
        Assert.Equal(Gem, LootContext.Of(h.State).VendorMask);
        var armour = LootRulesTests.Item(Armor, 100);
        Assert.Equal(LootDecision.Type, LootRules.Default.Decide(armour, LootContext.Of(h.State)));
    }
}
