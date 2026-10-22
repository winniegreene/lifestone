using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M4 3.1 d0 (D20 and D22), the death loop's decoys (<see cref="DecoyKit"/>): the eight rows of the lead's read-only SELECTs (<c>cache/m4/3-1/decoys.tsv</c>, 2026-10-21), the burden of 452, the count rule by level, <see cref="DecoyKit.IsDecoy"/>, a distinct death category for each
/// decoy that equals what <see cref="DeathDropModel"/> derives from its ItemType, the kit line each makes, and the check over a live inventory (<see cref="DecoyKit.Check"/>) with a hand-built state.
/// </summary>
public sealed class DecoyKitTests
{
    // wcid, name, weenie type, ItemType, category, unit value, burden, vendor sold: decoys.tsv, in the file's order.
    private static readonly (uint Wcid, string Name, uint WeenieType, uint ItemType, DeathItemCategory Category, int Value, int Burden, bool Vendor)[] Rows =
    [
        (27329, "Massive Mana Charge", 37, 0x80000, DeathItemCategory.ManaStone, 65_000, 50, true),
        (9295, "Intricate Carving Tool", 44, 0x80, DeathItemCategory.Misc, 10_000, 40, true),
        (41396, "Handle Mold", 51, 0x4000000, DeathItemCategory.CraftingIngredient, 10_000, 100, true),
        (22565, "Tusker Island Shirt", 2, 0x4, DeathItemCategory.Clothing, 9_000, 57, true),
        (25522, "Skull Cap", 2, 0x2, DeathItemCategory.Armor, 8_500, 125, false),
        (11007, "Virindi Scalpel", 6, 0x1, DeathItemCategory.MeleeWeapon, 8_420, 30, false),
        (11129, "Healing Chocolate Olthoi Cake", 38, 0x20, DeathItemCategory.Food, 8_000, 25, false),
        (27771, "Entrancing Adjanite Gem", 38, 0x800, DeathItemCategory.Gem, 15_000, 25, false),
    ];

    [Fact]
    public void The_eight_decoys_are_the_rows_of_the_select_in_the_files_order()
    {
        Assert.Equal(8, DecoyKit.All.Count);
        for (var i = 0; i < Rows.Length; i++)
        {
            var d = DecoyKit.All[i];
            var r = Rows[i];
            Assert.Equal(r.Wcid, d.Wcid);
            Assert.Equal(r.Name, d.Name);
            Assert.Equal(r.WeenieType, d.WeenieType);
            Assert.Equal(r.ItemType, d.ItemType);
            Assert.Equal(r.Category, d.Category);
            Assert.Equal(r.Value, d.UnitValue);
            Assert.Equal(r.Burden, d.Burden);
            Assert.Equal(r.Vendor, d.VendorSold);
        }
    }

    [Fact]
    public void The_burden_of_the_eight_is_452_and_each_is_light()
    {
        Assert.Equal(452, DecoyKit.TotalBurden);
        Assert.Equal(DecoyKit.All.Sum(d => d.Burden), DecoyKit.TotalBurden);
        Assert.All(DecoyKit.All, d => Assert.InRange(d.Burden, 1, 125));
    }

    [Fact]
    public void Each_decoy_is_in_its_own_death_category_which_is_what_its_ItemType_gives()
    {
        Assert.Equal(8, DecoyKit.All.Select(d => d.Category).Distinct().Count());
        Assert.DoesNotContain(DecoyKit.All, d => d.Category == DeathItemCategory.None);
        Assert.All(DecoyKit.All, d => Assert.Equal(DeathDropModel.Category(d.ItemType), d.Category));
        Assert.Equal(
            [DeathItemCategory.ManaStone, DeathItemCategory.Misc, DeathItemCategory.CraftingIngredient, DeathItemCategory.Clothing, DeathItemCategory.Armor, DeathItemCategory.MeleeWeapon, DeathItemCategory.Food, DeathItemCategory.Gem],
            DecoyKit.All.Select(d => d.Category));
        // None is in a category the wand shares (MagicCaster) and none is a spell component (the Mana Scarab's category), so no decoy is halved behind the wand's or the scarab's.
        Assert.DoesNotContain(DecoyKit.All, d => d.Category is DeathItemCategory.MagicCaster or DeathItemCategory.SpellComponent);
    }

    [Fact]
    public void Each_decoy_is_worth_8000_or_more_and_after_the_variance_still_over_the_wands_best_case()
    {
        Assert.All(DecoyKit.All, d => Assert.True(d.UnitValue >= 8000, d.Name));
        var wandBest = DeathDropModel.VarianceMax(5400);
        Assert.Equal(5940, wandBest);
        Assert.All(DecoyKit.All, d => Assert.True(DeathDropModel.VarianceMin(d.UnitValue) >= 7200 && DeathDropModel.VarianceMin(d.UnitValue) > wandBest, d.Name));
    }

    [Fact]
    public void Four_decoys_are_sold_by_vendors_in_the_database_and_four_are_not()
    {
        Assert.Equal(["Massive Mana Charge", "Intricate Carving Tool", "Handle Mold", "Tusker Island Shirt"], DecoyKit.All.Where(d => d.VendorSold).Select(d => d.Name));
        Assert.Equal(4, DecoyKit.All.Count(d => !d.VendorSold));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(15, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 3)]
    [InlineData(25, 3)]
    [InlineData(40, 4)]
    [InlineData(60, 5)]
    [InlineData(100, 7)]
    [InlineData(120, 8)]
    [InlineData(126, 8)]
    [InlineData(140, 8)]
    [InlineData(260, 8)]
    [InlineData(400, 8)]
    public void The_count_is_the_most_a_death_drops_at_the_level_and_never_more_than_the_eight_the_kit_has(int level, int expected)
    {
        Assert.Equal(expected, DecoyKit.CountFor(level));
        Assert.Equal(expected, DecoyKit.For(level).Count);
        Assert.Equal(DecoyKit.All.Take(expected).Select(d => d.Wcid), DecoyKit.For(level).Select(d => d.Wcid));
    }

    [Fact]
    public void At_level_126_the_kit_carries_all_eight_which_with_the_Mana_Scarab_is_nine_items_above_the_wand_against_at_most_eight_drops()
    {
        Assert.Equal(126, MageTemplate.TargetLevel);
        Assert.Equal(8, DecoyKit.CountFor(MageTemplate.TargetLevel));
        Assert.Equal(8, DeathDropModel.MaxDrops(MageTemplate.TargetLevel));
    }

    [Fact]
    public void IsDecoy_is_true_for_the_eight_and_false_for_the_gear_and_the_components()
    {
        Assert.All(Rows, r => Assert.True(DecoyKit.IsDecoy(r.Wcid), r.Name));
        Assert.All(DeathDropModel.NeededGear, w => Assert.False(DecoyKit.IsDecoy(w)));
        Assert.All(ComponentWeenies.All, c => Assert.False(DecoyKit.IsDecoy(c.Wcid), c.Name));
        Assert.False(DecoyKit.IsDecoy(0));
        Assert.False(DecoyKit.IsDecoy(6600));
        Assert.False(DecoyKit.IsDecoy(DeathDropModel.ManaScarabWcid));
    }

    [Fact]
    public void A_decoys_kit_line_is_one_item_with_no_valid_location_and_no_maximum_stack()
    {
        Assert.All(
            DecoyKit.All,
            d =>
            {
                var line = d.ToGearItem();
                Assert.Equal(GearKind.Decoy, line.Kind);
                Assert.Equal(d.Wcid, line.Wcid);
                Assert.Equal(d.Name, line.Name);
                Assert.Equal(d.WeenieType, line.WeenieType);
                Assert.Equal(d.ItemType, line.ItemType);
                Assert.Equal(EquipMask.None, line.ValidLocations);
                Assert.Equal(1, line.Stack);
                Assert.Null(line.MaxStack);
                Assert.Equal(d.Burden, line.UnitBurden);
                Assert.Equal(d.Burden, line.Burden);
                Assert.Null(line.Palette);
                Assert.Null(line.Shade);
            });
    }

    // ---------------------------------------------------------------- the check over a live inventory (a hand-built state)

    private static WorldObject Object(uint guid, CarriedItem item, bool valueInDescription, string name = "Thing")
    {
        var header = WeenieHeaderFlags.None;
        if (valueInDescription)
        {
            header |= WeenieHeaderFlags.Value;
        }

        if (item.StackSize > 1)
        {
            header |= WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize;
        }

        var weenie = new WeenieDesc
        {
            Header = header,
            Name = name,
            WeenieClassId = item.Wcid,
            IconId = 0x06000001,
            ItemType = item.ItemType,
            ObjectDescriptionFlags = 0,
            Value = valueInDescription ? (uint)item.Value : null,
            StackSize = item.StackSize > 1 ? (ushort)item.StackSize : null,
            MaxStackSize = item.StackSize > 1 ? (ushort)1000 : null,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.CSetup, State = PhysicsState.IgnoreCollisions, SetupId = 0x02000001, Timestamps = ReducerHarness.Stamps() };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0);
    }

    /// <summary>A state whose player holds <paramref name="items"/>: the wielded ones equipped, the rest in the pack. The value reaches the model in the description, or (when <paramref name="valueFromServerStack"/>) only in a SetStackSize for the stacks.</summary>
    private static GameState StateOf(IEnumerable<CarriedItem> items, bool valueFromServerStack = false, int unsettled = 0)
    {
        var list = items.ToList();
        var state = new GameState();
        var pack = new List<InventoryEntry>();
        var equipped = new List<EquippedItem>();
        uint guid = 0x80000100;
        foreach (var item in list)
        {
            var viaStack = valueFromServerStack && item.StackSize > 1;
            state.Objects.Set(Object(guid, item, valueInDescription: !viaStack));
            if (item.Wielded)
            {
                equipped.Add(new EquippedItem(guid, 1, 0));
            }
            else
            {
                pack.Add(new InventoryEntry(guid, 0));
            }

            guid++;
        }

        for (var i = 0; i < unsettled; i++)
        {
            pack.Add(new InventoryEntry(0x80009000 + (uint)i, 0)); // a pack entry whose world object has not arrived
        }

        state.Self.Inventory.Replace(pack, equipped, complete: true);
        if (valueFromServerStack)
        {
            var g = 0x80000100u;
            foreach (var item in list)
            {
                if (item.StackSize > 1)
                {
                    state.Self.Inventory.SetStack(g, (uint)item.StackSize, (uint)item.Value);
                }

                g++;
            }
        }

        return state;
    }

    [Fact]
    public void The_check_over_the_mage_with_all_eight_decoys_is_safe_and_says_nine_items_are_above_the_wand()
    {
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All));

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Made);
        Assert.True(check.Verdict.Survives, check.Text);
        Assert.Equal("yes", check.Field);
        Assert.Equal(9, check.Verdict.Above);
        Assert.Equal(8552u, check.Verdict.BestNeededWcid);
        Assert.Equal(5940, check.Verdict.BestNeededMax);
        Assert.Equal(7200, check.Verdict.WeakestAboveMin);
        Assert.Equal(0, check.Unsettled);
        Assert.Equal(DeathDropModelTests.MageItems(DecoyKit.All).Count, check.Carried);
        Assert.Equal("decoy check: 9 items above the wand (best case 5940, the weakest of the 9 at least 7200); 6 to 8 drop at level 126 of 15 candidates: safe; TODO(m4-decoy-bonded-flag): the bonded gate rests on the kit's three known bonded weenies, any other bonded item counts as a candidate", check.Text);
        Assert.DoesNotContain("TODO(m4-decoy-value-unknown)", check.Text, StringComparison.Ordinal); // every description sent its Value: that gap is named only when it happened
    }

    [Fact]
    public void The_check_over_the_mage_without_decoys_is_not_safe_and_says_so_loudly()
    {
        var state = StateOf(DeathDropModelTests.MageItems([]));

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Made);
        Assert.False(check.Verdict.Survives);
        Assert.Equal("no", check.Field);
        Assert.Equal(1, check.Verdict.Above); // the Mana Scarab
        Assert.Contains("NOT SAFE", check.Text, StringComparison.Ordinal);
        Assert.StartsWith("decoy check: 1 items above the wand", check.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stacks_value_is_read_from_the_servers_last_SetStackSize_when_the_description_has_none()
    {
        // The stacks' descriptions carry no Value here; only the 0x0197 does. Read as 0, the Mana Scarab would drop out of the count (8 instead of 9 above) and the Pyreal Scarab would have no value.
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All), valueFromServerStack: true);

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Made);
        Assert.Equal(9, check.Verdict.Above);
        Assert.True(check.Verdict.Survives, check.Text);
        Assert.DoesNotContain("TODO(m4-decoy-value-unknown)", check.Text, StringComparison.Ordinal); // the SetStackSize gave the stacks their value: nothing was counted as 0
    }

    [Fact]
    public void The_checks_line_names_its_two_known_gaps_by_their_TODO_names_so_they_are_logged_and_not_only_commented_3_1_fix_R3()
    {
        var items = DeathDropModelTests.MageItems(DecoyKit.All).ToList();
        var state = StateOf(items);
        state.Objects.Set(Object(0x80000100, items[0], valueInDescription: false)); // one description that sends no Value (counted as 0, as ACE's wo.Value ?? 0)

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Made);
        Assert.EndsWith("; TODO(m4-decoy-bonded-flag): the bonded gate rests on the kit's three known bonded weenies, any other bonded item counts as a candidate; TODO(m4-decoy-value-unknown): 1 held object(s) sent no Value and count as 0", check.Text, StringComparison.Ordinal);
        Assert.StartsWith("decoy check: ", check.Text, StringComparison.Ordinal);
        Assert.Equal(DecoyKit.BondedGapText, check.Text[(check.Text.IndexOf("TODO(m4-decoy-bonded-flag)", StringComparison.Ordinal))..check.Text.IndexOf("; TODO(m4-decoy-value-unknown)", StringComparison.Ordinal)]);

        // The same line reaches the log unchanged: the gear stage logs Text as it is.
        Assert.Contains("TODO(m4-decoy-bonded-flag)", DecoyKit.Describe(new DropVerdict(126, 6, 8, 0, 0, 0, 0, 0, false), 0), StringComparison.Ordinal); // even a line with no needed candidate carries the bonded gap
    }

    [Fact]
    public void The_check_reads_the_stack_from_the_description_when_the_server_sent_no_SetStackSize_and_a_stacks_value_per_unit()
    {
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All));
        Assert.False(state.Self.Inventory.TryGetStack(0x80000100 + 7, out _, out _)); // the Pyreal Scarab stack: no SetStackSize

        var check = DecoyKit.Check(state, 126);

        Assert.Equal(9, check.Verdict.Above); // the Mana Scarab: 75,000 over 5, 15,000 each
    }

    [Fact]
    public void The_kits_three_bonded_weenies_are_read_as_bonded_because_the_description_carries_no_flag()
    {
        // The Platinum Scarab (10,000 each) read as not bonded would be a needed best case of 11,000 and the answer no (DeathDropModelTests): the check holds the kit's bonded weenies as bonded (TODO(m4-decoy-bonded-flag)).
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All).Select(i => i with { Bonded = false }));

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Verdict.Survives, check.Text);
        Assert.Equal(8552u, check.Verdict.BestNeededWcid);
    }

    [Theory]
    [InlineData(0, 0.0, false)]
    [InlineData(1425, 95.0, false)] // exactly 95%: not over
    [InlineData(1426, 95.07, true)] // one burden unit over
    [InlineData(1450, 96.67, true)]
    [InlineData(1500, 100.0, true)]
    [InlineData(2250, 150.0, true)]
    public void The_live_load_share_is_the_burden_property_over_the_strength_capacity_and_warns_over_95_percent_3_1_fix_R7(int burden, double percent, bool over)
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnPlayerDescription( // the description binds the player's qualities and carries the burden property (EncumbranceVal, int 5), as the server's does
            h.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { IntProperties = System.Collections.Immutable.ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, burden), Inventory = [], Equipped = [] });

        var load = DecoyKit.LoadOf(h.State);

        Assert.Equal(burden, load.Burden);
        Assert.Equal(1500, load.Capacity); // no Strength sent yet: retail's InqLoad default of 10, 150 each
        Assert.Equal(percent, load.Percent, 2);
        Assert.Equal(over, load.Over);
        var text = DecoyKit.LoadText(in load);
        Assert.StartsWith("load: " + burden + " of 1500 burden", text, StringComparison.Ordinal);
        Assert.Equal(over, text.Contains("the decoy kit and duplicates leave the mage at", StringComparison.Ordinal));
        Assert.Equal(95, DecoyKit.LoadWarnPercent);
    }

    [Fact]
    public void The_load_warning_names_the_decoy_kit_and_the_duplicates_and_the_percent_3_1_fix_R7()
    {
        var text = DecoyKit.LoadText(new LoadShare(1450, 1500, 96.666666));

        Assert.Equal("load: 1450 of 1500 burden: the decoy kit and duplicates leave the mage at 96.7% of its capacity (over 95%; over 100% slows the run and the corpse trip)", text);
        Assert.Equal("load: 1400 of 1500 burden, 93.3% of the capacity (the inventory page's load; warned over 95%)", DecoyKit.LoadText(new LoadShare(1400, 1500, 93.333333)));
    }

    [Fact]
    public void An_unknown_inventory_an_unknown_level_and_no_state_are_not_made_never_a_pass_and_never_throw()
    {
        var empty = new GameState();
        Assert.False(empty.Self.Inventory.IsKnown);

        foreach (var check in new[] { DecoyKit.Check(empty, 126), DecoyKit.Check(null, 126), DecoyKit.Check(StateOf(DeathDropModelTests.MageItems(DecoyKit.All)), 0), DecoyKit.Check(StateOf(DeathDropModelTests.MageItems(DecoyKit.All)), -3) })
        {
            Assert.False(check.Made);
            Assert.False(check.Verdict.Survives);
            Assert.Equal("no", check.Field);
            Assert.StartsWith("decoy check: not made, ", check.Text, StringComparison.Ordinal);
        }

        Assert.Equal("decoy check: not made, the inventory is not known yet", DecoyKit.Check(empty, 126).Text);
        Assert.Equal("decoy check: not made, no state", DecoyKit.Check(null, 126).Text);
        Assert.Equal("decoy check: not made, the level is not known", DecoyKit.Check(StateOf([]), 0).Text);
    }

    [Fact]
    public void A_held_object_with_no_world_object_yet_is_counted_as_unsettled_and_the_line_says_the_answer_is_provisional()
    {
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All), unsettled: 2);

        var check = DecoyKit.Check(state, 126);

        Assert.True(check.Made);
        Assert.Equal(2, check.Unsettled);
        Assert.Contains("provisional: 2 held objects not yet known", check.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pack_with_a_container_and_pyreals_reads_them_as_no_candidate()
    {
        var items = DeathDropModelTests.MageItems(DecoyKit.All);
        items.Add(new CarriedItem(DeathDropModel.CoinWcid, 0x40, 90_000, 90_000, false, false)); // pyreals
        items.Add(new CarriedItem(1234, 0x200, 99_999, 1, false, false)); // a pack
        var state = StateOf(items);

        var check = DecoyKit.Check(state, 126);

        Assert.Equal(15, check.Verdict.Candidates);
        Assert.True(check.Verdict.Survives, check.Text);
    }

    [Fact]
    public void The_check_names_a_needed_item_that_is_not_the_wand_by_its_name_or_class_id()
    {
        // Without the wand (the wand lost or never spawned) the bar is the best of what is left: the Olthoi Helm, named by the kit.
        var state = StateOf(DeathDropModelTests.MageItems(DecoyKit.All).Where(i => i.Wcid != 8552));

        var check = DecoyKit.Check(state, 126);

        Assert.Equal(3715u, check.Verdict.BestNeededWcid);
        Assert.Contains("above Olthoi Helm", check.Text, StringComparison.Ordinal);
        Assert.Equal("weenie 4242", DecoyKit.NeededName(4242));
        Assert.Equal("the wand", DecoyKit.NeededName(8552));
        Assert.Equal("Pyreal Scarab", DecoyKit.NeededName(690));
    }
}
