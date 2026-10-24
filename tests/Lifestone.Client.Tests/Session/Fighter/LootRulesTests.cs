using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The loot rule (M4 2.2 s0, DoD 8): the default rule's value range, the vendor mask's boundaries, the burden limit, every skip reason, the options and the load the rule reads.</summary>
public sealed class LootRulesTests
{
    private const uint Misc = 0x80;
    private const uint Jewelry = 0x8;
    private const uint Gem = 0x800;
    private const uint Money = 0x40;
    private const uint Armor = 0x2;
    private const uint Creature = 0x10;

    // 1500 capacity: Strength 10 and no augmentations (EncumbranceSystem.EncumbranceCapacity(10, 0)).
    private static readonly LootContext Empty = new(Burden: 0, Capacity: 1500, Inventory: null);

    internal static WorldObject Item(uint itemType, uint? value, ushort? burden = null, uint guid = 0x80000211, string name = "Thing", uint wcid = 141, ushort? maxStack = null)
    {
        var header = WeenieHeaderFlags.None;
        if (value is not null)
        {
            header |= WeenieHeaderFlags.Value;
        }

        if (burden is not null)
        {
            header |= WeenieHeaderFlags.Burden;
        }

        if (maxStack is not null)
        {
            header |= WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize;
        }

        var weenie = new WeenieDesc
        {
            Header = header,
            Name = name,
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = itemType,
            ObjectDescriptionFlags = 0,
            Value = value,
            Burden = burden,
            StackSize = maxStack is null ? null : (ushort)1,
            MaxStackSize = maxStack,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions, SetupId = 0x02000001, Timestamps = ReducerHarness.Stamps() };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0);
    }

    // ------------------------------------------------------------------------------------------------ the default rule

    [Theory]
    [InlineData(0u, LootDecision.Value)]
    [InlineData(1u, LootDecision.Take)]
    [InlineData(1_000_000u, LootDecision.Take)]
    [InlineData(1_000_001u, LootDecision.Value)]
    public void The_value_boundaries_are_1_and_1000000_inclusive(uint value, LootDecision expected) =>
        Assert.Equal(expected, LootRules.Default.Decide(Item(Misc, value), in Empty));

    [Fact]
    public void An_item_with_no_value_in_its_description_is_worth_nothing()
    {
        Assert.Equal(LootDecision.Value, LootRules.Default.Decide(Item(Misc, null), in Empty));
    }

    [Fact]
    public void The_shop_mask_is_the_vendors_0x480467A7_and_its_boundaries_hold()
    {
        Assert.Equal(0x480467A7u, LootOptions.ShopMask);
        Assert.Equal(0x480467A7u, LootRules.Default.TypeMask);

        // every bit of the mask is taken, one at a time; the types the capture's corpse held that are not in it are left
        foreach (var bit in new uint[] { 0x1, 0x2, 0x4, 0x20, 0x80, 0x100, 0x200, 0x400, 0x2000, 0x4000, 0x40000, 0x08000000, 0x40000000 })
        {
            Assert.Equal(LootDecision.Take, LootRules.Default.Decide(Item(bit, 10), in Empty));
        }

        foreach (var bit in new uint[] { Jewelry, Creature, Money, Gem, 0x1000, 0x8000, 0x80000, 0x10000000, 0x20000000, 0x80000000 })
        {
            Assert.Equal(LootDecision.Type, LootRules.Default.Decide(Item(bit, 10), in Empty));
        }

        // one bit in the mask is enough: a combined type with a Gem and a Misc bit; an ItemType of 0 has none
        Assert.Equal(LootDecision.Take, LootRules.Default.Decide(Item(Gem | Misc, 10), in Empty));
        Assert.Equal(LootDecision.Type, LootRules.Default.Decide(Item(0, 10), in Empty));
    }

    [Fact]
    public void The_captures_four_corpse_items_give_two_taken_and_two_skipped_by_type()
    {
        // cache/m4/2-2/capture.md: Jewelry 0x8 (1777), Gem 0x800 (453), Misc 0x80 (4000) x 2
        var decisions = new[] { Item(Jewelry, 1777), Item(Gem, 453), Item(Misc, 4000), Item(Misc, 4000) }.Select(i => LootRules.Default.Decide(i, in Empty)).ToArray();

        Assert.Equal([LootDecision.Type, LootDecision.Type, LootDecision.Take, LootDecision.Take], decisions);
    }

    [Fact]
    public void The_checks_run_in_the_order_value_type_burden()
    {
        // worth nothing and the wrong type: Value comes first; the right value but the wrong type and too heavy: Type
        Assert.Equal(LootDecision.Value, LootRules.Default.Decide(Item(Gem, 0, 5000), in Empty));
        Assert.Equal(LootDecision.Type, LootRules.Default.Decide(Item(Gem, 10, 5000), in Empty));
        Assert.Equal(LootDecision.Burden, LootRules.Default.Decide(Item(Misc, 10, 5000), in Empty));
    }

    // ------------------------------------------------------------------------------------------------ the burden limit

    [Theory]
    [InlineData(1000, 500, LootDecision.Take)] // exactly the capacity
    [InlineData(1000, 501, LootDecision.Burden)] // one over
    [InlineData(1500, 0, LootDecision.Take)] // already at the limit: an item that weighs nothing still fits
    public void The_burden_limit_is_the_capacity_and_the_boundary_is_inclusive(int burden, int itemBurden, LootDecision expected)
    {
        var context = new LootContext(burden, 1500, null);

        Assert.Equal(expected, LootRules.Default.Decide(Item(Misc, 10, (ushort)itemBurden), in context));
    }

    [Fact]
    public void A_lower_burden_limit_is_a_share_of_the_capacity_and_zero_capacity_takes_nothing()
    {
        var half = new LootRules(LootOptions.ShopMask, 1, 1_000_000, 50);
        var context = new LootContext(500, 1500, null);

        Assert.Equal(LootDecision.Take, half.Decide(Item(Misc, 10, 250), in context)); // 750 = 50% of 1500
        Assert.Equal(LootDecision.Burden, half.Decide(Item(Misc, 10, 251), in context));
        Assert.Equal(LootDecision.Burden, LootRules.Default.Decide(Item(Misc, 10, 0), new LootContext(0, 0, null)));
        Assert.Equal(LootDecision.Take, new LootRules(LootOptions.AllTypes, 0, long.MaxValue, 0).Decide(Item(Misc, 10, 9999), new LootContext(0, 0, null))); // no limit
    }

    // ------------------------------------------------------------------------------------------------ the other skip reasons

    [Fact]
    public void An_unresolved_entry_and_an_item_the_player_holds_are_named_skip_reasons()
    {
        Assert.Equal(LootDecision.NotResolved, LootRules.Default.Decide(null, in Empty));

        var h = new ReducerHarness();
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Inventory = [new ContentDesc(0x80000211, 0)], Equipped = [] });
        var context = new LootContext(0, 1500, h.State.Self.Inventory);

        Assert.Equal(LootDecision.AlreadyHeld, LootRules.Default.Decide(Item(Misc, 10, guid: 0x80000211), in context));
        Assert.Equal(LootDecision.Take, LootRules.Default.Decide(Item(Misc, 10, guid: 0x80000212), in context));
    }

    [Fact]
    public void Take_all_takes_a_worthless_heavy_item_of_any_type_but_not_an_unresolved_one()
    {
        var heavy = Item(Creature, 0, 60000);

        Assert.Equal(LootDecision.Take, LootRules.TakeAll.Decide(heavy, new LootContext(1499, 1500, null)));
        Assert.Equal(LootDecision.Take, LootRules.TakeAll.Decide(Item(0, null), in Empty));
        Assert.Equal(LootDecision.NotResolved, LootRules.TakeAll.Decide(null, in Empty));
        Assert.Equal("take all", LootRules.TakeAll.Describe());
    }

    [Fact]
    public void The_reasons_have_log_names()
    {
        Assert.Equal(
            ["take", "not_resolved", "already_held", "value", "type", "burden"],
            Enum.GetValues<LootDecision>().Select(LootRules.ReasonName));
    }

    // ------------------------------------------------------------------------------------------------ the load the rule reads

    [Fact]
    public void The_context_is_the_inventory_pages_load_the_burden_property_over_the_strength_capacity()
    {
        var h = new ReducerHarness();
        Assert.Equal(new LootContext(0, 1500, h.State.Self.Inventory), LootContext.Of(h.State)); // nothing sent yet: Strength 10 (retail's InqLoad default), burden 0

        Assert.Equal(300, LootContext.Of(h.State, addedBurden: 300).Burden); // the looter's own running total, when the property has not caught up
    }

    // ------------------------------------------------------------------------------------------------ the options

    [Fact]
    public void The_options_default_to_the_dod_8_rule()
    {
        var o = LootOptions.FromArguments(CommandLineArgument.ParseAll([]));

        Assert.Empty(o.Errors);
        Assert.True(o.Enabled);
        Assert.Equal(LootOptions.ShopMask, o.TypeMask);
        Assert.Equal(1, o.MinValue);
        Assert.Equal(100, o.MaxBurdenPercent);
        Assert.Equal(LootRules.Default.TypeMask, o.Rules().TypeMask);
        Assert.Equal(1, o.Rules().MinValue);
        Assert.Equal(1_000_000, o.Rules().MaxValue);
        Assert.Equal(100, o.Rules().MaxBurdenPercent);
    }

    [Fact]
    public void The_options_parse_and_the_hunt_carries_them()
    {
        string[] args = ["--loot=off", "--loot-types=0x8C", "--loot-min-value=500", "--loot-max-burden=80"];
        var o = LootOptions.FromArguments(CommandLineArgument.ParseAll(args));

        Assert.Empty(o.Errors);
        Assert.False(o.Enabled);
        Assert.Equal(0x8Cu, o.TypeMask);
        Assert.Equal(500, o.MinValue);
        Assert.Equal(80, o.MaxBurdenPercent);

        var hunt = HuntOptions.FromArguments(CommandLineArgument.ParseAll([.. args, "--hunt-kills=2"]));
        Assert.Empty(hunt.Errors);
        Assert.Equal(2, hunt.Kills);
        Assert.Equal(o with { Errors = [] }, hunt.Loot with { Errors = [] });
        Assert.True(HuntOptions.FromArguments(CommandLineArgument.ParseAll(["--hunt-kills=2"])).Loot.Enabled, "--loot is on by default");
    }

    [Theory]
    [InlineData("shop", LootOptions.ShopMask)]
    [InlineData("all", LootOptions.AllTypes)]
    [InlineData("0x480467A7", 0x480467A7u)]
    [InlineData("480467a7", 0x480467A7u)]
    [InlineData("80", 0x80u)]
    public void The_type_option_takes_shop_all_or_a_hex_mask(string value, uint mask)
    {
        var o = LootOptions.FromArguments(CommandLineArgument.ParseAll(["--loot-types=" + value]));

        Assert.Empty(o.Errors);
        Assert.Equal(mask, o.TypeMask);
    }

    [Theory]
    [InlineData("--loot=maybe")]
    [InlineData("--loot-types=")]
    [InlineData("--loot-types=weapons")]
    [InlineData("--loot-types=0")]
    [InlineData("--loot-types=0x")]
    [InlineData("--loot-types=0x1FFFFFFFF")]
    [InlineData("--loot-min-value=-1")]
    [InlineData("--loot-min-value=1000001")]
    [InlineData("--loot-min-value=ten")]
    [InlineData("--loot-max-burden=0")]
    [InlineData("--loot-max-burden=301")]
    [InlineData("--loot-max-burden=half")]
    public void A_bad_value_is_an_error_and_the_option_keeps_its_default(string argument)
    {
        var o = LootOptions.FromArguments(CommandLineArgument.ParseAll([argument]));

        Assert.Single(o.Errors);
        Assert.Equal(LootOptions.Default with { Errors = [] }, o with { Errors = [] });

        var hunt = HuntOptions.FromArguments(CommandLineArgument.ParseAll([argument])); // the headless host and the leg read the loot options through the hunt's
        Assert.Single(hunt.Errors);
    }

    [Fact]
    public void The_four_options_are_registered_for_the_main_scene_and_the_headless_host()
    {
        var group = OptionCatalog.Hunt();

        Assert.Equal(OptionScenes.Main | OptionScenes.Headless, group.Scenes);
        foreach (var name in new[] { "--loot", "--loot-types", "--loot-min-value", "--loot-max-burden" })
        {
            Assert.Contains(group.Options, o => o.Name == name);
            Assert.True(OptionCatalog.Registry.IsKnown(name), name);
        }

        Assert.Empty(OptionCatalog.Registry.Problems(CommandLineArgument.ParseAll(["--loot=on", "--loot-types=all", "--loot-min-value=2", "--loot-max-burden=90"]), OptionScenes.Headless));
    }
}
