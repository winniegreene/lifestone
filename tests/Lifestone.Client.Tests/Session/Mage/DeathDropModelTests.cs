using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M4 3.1 d0 (D20), ACE's death-drop rule as <see cref="DeathDropModel"/> re-expresses it (ACE 95ef426d <c>Entity/DeathItem.cs</c>, <c>Player_Death.cs</c> 481-603 and 629-673): hand-worked cases of each part of the rule (the category by ItemType, the candidates, the per-unit
/// value of a stack, the sort, the halving once per item after the first of its category, the variance as bounds, the count of items by level), then the REAL numbers of BSD Mage's kit (the lead's read-only SELECTs, <c>cache/m4/3-1/facts.md</c>): the eight valuables protect the wand at
/// level 126 in every corner of the variance and the same kit without them does not; an independent simulation of ACE's draw agrees.
/// </summary>
public sealed class DeathDropModelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Armor = 0x2;
    private const uint Clothing = 0x4;
    private const uint Misc = 0x80;
    private const uint Gem = 0x800;
    private const uint Caster = 0x8000;
    private const uint Melee = 0x1;
    private const uint SpellComponents = 0x1000;

    /// <summary>The Mana Scarab (component 193): the M4 fixture's carried, valuable item that the retired loot kit did not need.</summary>
    private const uint ManaScarab = 37155;

    /// <summary>
    /// The needed set of the retired M4 loot kit (the Healer's Heart 8552, three armour pieces and every component of the pinned map but the Mana Scarab), kept here as the fixture of the model's worked cases, which are BSD Mage's M4 numbers.
    /// The production set is the mage's own kit (<see cref="DeathDropModel.NeededOf"/>, M5 3 d4).
    /// </summary>
    private static readonly IReadOnlySet<uint> LootKitNeeded = new HashSet<uint>([8552u, 3715u, 57u, 107u, .. ComponentWeenies.All.Where(c => c.Wcid != ManaScarab).Select(c => c.Wcid)]);

    private static CarriedItem I(uint wcid, uint type, int value, int stack = 1, bool wielded = false, bool bonded = false) => new(wcid, type, value, stack, wielded, bonded);

    // ---------------------------------------------------------------- the pieces of the rule, hand-worked

    [Theory]
    [InlineData(0x00000001u, DeathItemCategory.MeleeWeapon)]
    [InlineData(0x00000100u, DeathItemCategory.MissileWeapon)]
    [InlineData(0x00008000u, DeathItemCategory.MagicCaster)] // Caster
    [InlineData(0x00200000u, DeathItemCategory.MagicCaster)] // MagicWieldable
    [InlineData(0x00000002u, DeathItemCategory.Armor)]
    [InlineData(0x00000004u, DeathItemCategory.Clothing)]
    [InlineData(0x00000008u, DeathItemCategory.Jewelry)]
    [InlineData(0x00000020u, DeathItemCategory.Food)]
    [InlineData(0x00000800u, DeathItemCategory.Gem)]
    [InlineData(0x00001000u, DeathItemCategory.SpellComponent)]
    [InlineData(0x00080000u, DeathItemCategory.ManaStone)]
    [InlineData(0x00800000u, DeathItemCategory.CraftingIngredient)] // CraftAlchemyBase
    [InlineData(0x04000000u, DeathItemCategory.CraftingIngredient)] // CraftAlchemyIntermediate
    [InlineData(0x00400000u, DeathItemCategory.CraftingIngredient)] // CraftCookingBase
    [InlineData(0x02000000u, DeathItemCategory.CraftingIngredient)] // CraftFletchingBase
    [InlineData(0x08000000u, DeathItemCategory.CraftingIngredient)] // CraftFletchingIntermediate
    [InlineData(0x00002000u, DeathItemCategory.ParchmentBook)] // Writable
    [InlineData(0x00004000u, DeathItemCategory.Key)]
    [InlineData(0x00040000u, DeathItemCategory.TradeNote)] // PromissoryNote
    [InlineData(0x00000080u, DeathItemCategory.Misc)]
    [InlineData(0x00000200u, DeathItemCategory.None)] // Container: packs never drop
    [InlineData(0x00000010u, DeathItemCategory.None)] // Creature
    [InlineData(0x00000040u, DeathItemCategory.None)] // Money
    [InlineData(0x00000400u, DeathItemCategory.None)] // Useless
    [InlineData(0x00010000u, DeathItemCategory.None)] // Portal
    [InlineData(0x00000000u, DeathItemCategory.None)]
    [InlineData(0x00000280u, DeathItemCategory.None)] // Misc | Container is no single type: ACE's switch matches one value
    public void The_category_is_ACEs_GetCategory_of_the_ItemType(uint itemType, DeathItemCategory expected) =>
        Assert.Equal(expected, DeathDropModel.Category(itemType));

    [Fact]
    public void The_category_enum_has_ACEs_sixteen_members_in_ACEs_order()
    {
        Assert.Equal(
            ["None", "MeleeWeapon", "MissileWeapon", "MagicCaster", "Armor", "Clothing", "Jewelry", "Food", "Gem", "SpellComponent", "ManaStone", "CraftingIngredient", "ParchmentBook", "Key", "TradeNote", "Misc"],
            Enum.GetNames<DeathItemCategory>());
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(11, 0, 1)]
    [InlineData(20, 0, 1)]
    [InlineData(21, 1, 3)]
    [InlineData(126, 6, 8)]
    [InlineData(260, 13, 14)]
    [InlineData(280, 14, 14)]
    public void The_count_of_dropped_items_is_zero_up_to_ten_zero_or_one_to_twenty_and_level_over_twenty_plus_zero_to_two_at_most_fourteen(int level, int min, int max)
    {
        Assert.Equal(min, DeathDropModel.MinDrops(level));
        Assert.Equal(max, DeathDropModel.MaxDrops(level));
    }

    [Fact]
    public void A_stack_counts_at_its_per_unit_value_and_a_single_item_at_its_value()
    {
        Assert.Equal(100, DeathDropModel.UnitValue(I(1, Misc, 500, stack: 5)));
        Assert.Equal(333, DeathDropModel.UnitValue(I(1, Misc, 1000, stack: 3))); // integer division, as ACE's AdjustedValue /= StackSize
        Assert.Equal(500, DeathDropModel.UnitValue(I(1, Misc, 500)));
        Assert.Equal(500, DeathDropModel.UnitValue(I(1, Misc, 500, stack: 1)));
        Assert.Equal(0, DeathDropModel.UnitValue(I(1, Misc, 0, stack: 7)));
    }

    [Fact]
    public void Pyreals_bonded_items_containers_and_things_of_no_category_are_never_candidates()
    {
        var carried = new[]
        {
            I(100, Misc, 1000), // a candidate
            I(DeathDropModel.CoinWcid, Misc, 5000), // pyreals by weenie, whatever the type says
            I(101, 0x40, 5000), // Money
            I(102, Misc, 5000, bonded: true), // bonded
            I(103, 0x200, 5000), // a pack
            I(104, 0x10, 5000), // a creature
            I(105, 0x10000, 5000), // a portal
        };

        var candidates = DeathDropModel.Candidates(carried, 126);

        Assert.Equal([100u], candidates.Select(c => c.Wcid));
    }

    [Fact]
    public void Wielded_items_are_excluded_below_level_35_and_included_from_35()
    {
        var carried = new[] { I(1, Armor, 1000), I(2, Armor, 900, wielded: true), I(3, Caster, 800, wielded: true) };

        Assert.Equal([1u], DeathDropModel.Candidates(carried, 34).Select(c => c.Wcid));
        Assert.Equal([1u, 2u, 3u], DeathDropModel.Candidates(carried, 35).Select(c => c.Wcid));
        Assert.Equal([1u, 2u, 3u], DeathDropModel.Candidates(carried, 126).Select(c => c.Wcid));
        Assert.False(DeathDropModel.IsCandidate(carried[1], 20));
        Assert.True(DeathDropModel.IsCandidate(carried[1], DeathDropModel.WieldedDropLevel));
    }

    [Fact]
    public void The_list_is_sorted_by_category_then_value_highest_first_and_a_tie_keeps_the_carried_order()
    {
        var carried = new[]
        {
            I(1, Misc, 100),
            I(2, Melee, 50),
            I(3, Armor, 70),
            I(4, Armor, 90),
            I(5, Armor, 70), // ties item 3: the carried order holds
            I(6, Gem, 10),
        };

        var ranked = DeathDropModel.Rank(carried, new HashSet<uint>(), 126);

        Assert.Equal([2u, 4u, 3u, 5u, 6u, 1u], ranked.Select(r => r.Wcid)); // Melee, Armor 90, Armor 70, Armor 70, Gem, Misc
        Assert.Equal(
            [DeathItemCategory.MeleeWeapon, DeathItemCategory.Armor, DeathItemCategory.Armor, DeathItemCategory.Armor, DeathItemCategory.Gem, DeathItemCategory.Misc],
            ranked.Select(r => r.Category));
    }

    [Fact]
    public void Every_item_after_the_first_of_its_category_is_halved_once_not_progressively()
    {
        var carried = new[] { I(1, Armor, 8000), I(2, Armor, 6000), I(3, Armor, 4001), I(4, Gem, 3000) };

        var ranked = DeathDropModel.Rank(carried, new HashSet<uint>(), 126);

        Assert.Equal([8000, 3000, 2000, 3000], ranked.Select(r => r.HalvedValue)); // 8000; 6000 / 2; 4001 / 2 (integer division), not 6000 / 4; the gem is the first of its category
        Assert.Equal([8000, 6000, 4001, 3000], ranked.Select(r => r.UnitValue));
    }

    [Fact]
    public void A_stack_is_ranked_at_its_per_unit_value_and_halves_like_one_item()
    {
        // 18 scarabs worth 18,000 together are 1,000 each; behind a 15,000 scarab they are the second of the category.
        var carried = new[] { I(1, SpellComponents, 18_000, stack: 18), I(2, SpellComponents, 75_000, stack: 5) };

        var ranked = DeathDropModel.Rank(carried, new HashSet<uint>(), 126);

        Assert.Equal([2u, 1u], ranked.Select(r => r.Wcid));
        Assert.Equal([15_000, 500], ranked.Select(r => r.HalvedValue));
    }

    [Fact]
    public void The_variance_is_ten_percent_either_way_in_ACEs_float_arithmetic()
    {
        Assert.Equal(7200, DeathDropModel.VarianceMin(8000));
        Assert.Equal(8800, DeathDropModel.VarianceMax(8000));
        Assert.Equal(5940, DeathDropModel.VarianceMax(5400));
        Assert.Equal(0, DeathDropModel.VarianceMin(0));
        Assert.Equal(0, DeathDropModel.VarianceMax(0));
        Assert.Equal(9, DeathDropModel.VarianceMin(10)); // 10 - 1.0000000149
        Assert.Equal(11, DeathDropModel.VarianceMax(10));
        var ranked = DeathDropModel.Rank([I(1, Misc, 8000)], new HashSet<uint>(), 126);
        Assert.Equal(7200, ranked[0].MinFinal);
        Assert.Equal(8800, ranked[0].MaxFinal);
    }

    // ---------------------------------------------------------------- Survives: the interval rule, hand-worked

    private static readonly HashSet<uint> NeededOne = [999];

    /// <summary>A needed 1,000 caster (best case 1,100) and <paramref name="others"/> not-needed items, each in its own category (the types are distinct).</summary>
    private static List<CarriedItem> OneNeeded(params int[] others)
    {
        uint[] types = [Melee, Armor, Clothing, Misc, Gem, SpellComponents, 0x20, 0x80000, 0x2000, 0x4000];
        var list = new List<CarriedItem> { I(999, Caster, 1000) }; // carried in the pack: a wielded item is no candidate under level 35
        for (var i = 0; i < others.Length; i++)
        {
            list.Add(I((uint)(i + 1), types[i], others[i]));
        }

        return list;
    }

    [Fact]
    public void A_needed_item_survives_when_at_least_N_other_items_rank_above_its_best_case_in_every_corner()
    {
        // Level 25: N is 1 to 3. Three items worth 2,000 (min 1,800, over the needed best case 1,100) cover N_max; two do not.
        var three = DeathDropModel.Survives(OneNeeded(2000, 2000, 2000), NeededOne, 25);
        Assert.True(three.Survives, three.Describe());
        Assert.Equal(3, three.MaxDrops);
        Assert.Equal(3, three.Above);
        Assert.Equal(999u, three.BestNeededWcid);
        Assert.Equal(1100, three.BestNeededMax);
        Assert.Equal(1800, three.WeakestAboveMin);
        Assert.Equal(4, three.Candidates);

        var two = DeathDropModel.Survives(OneNeeded(2000, 2000), NeededOne, 25);
        Assert.False(two.Survives, two.Describe());
        Assert.Equal(2, two.Above);
    }

    [Fact]
    public void An_item_must_be_strictly_above_the_needed_best_case_over_the_whole_variance_to_count()
    {
        // The needed 1,000 reaches 1,100. An item of 1,223 bottoms out at 1,100 (not above), one of 1,224 at 1,101 (above).
        Assert.Equal(0, DeathDropModel.Survives(OneNeeded(1223), NeededOne, 25).Above);
        var above = DeathDropModel.Survives(OneNeeded(1224), NeededOne, 25);
        Assert.Equal(1, above.Above);
        Assert.Equal(1101, above.WeakestAboveMin);
    }

    [Fact]
    public void A_needed_item_counts_at_its_unhalved_best_case_though_it_is_the_second_of_its_category()
    {
        // A not-needed 2,400 armour is the first of the category, so the needed 2,200 helm would halve to 1,100 (best case 1,210); counted unhalved it reaches 2,420.
        // A not-needed gem of 2,300 (min 2,070) is above the halved best case and below the unhalved one: it must not count.
        var carried = new List<CarriedItem> { I(81, Armor, 2400), I(3715, Armor, 2200, wielded: true), I(7, Gem, 2300) };

        var v = DeathDropModel.Survives(carried, new HashSet<uint> { 3715 }, 126);

        Assert.Equal(3715u, v.BestNeededWcid);
        Assert.Equal(2420, v.BestNeededMax);
        Assert.Equal(0, v.Above); // the armour (min 2,160) and the gem (min 2,070) are both under 2,420; counted against the halved 1,210 both would have been above
        var helm = DeathDropModel.Rank(carried, new HashSet<uint> { 3715 }, 126).Single(r => r.Wcid == 3715);
        Assert.Equal(1100, helm.HalvedValue);
        Assert.Equal(990, helm.MinFinal);
        Assert.Equal(2420, helm.MaxFinal);
    }

    [Fact]
    public void Of_several_needed_items_the_one_with_the_highest_best_case_is_the_bar()
    {
        var carried = new List<CarriedItem> { I(1, Caster, 5400), I(2, Armor, 2200), I(3, Gem, 8000), I(4, Misc, 8000) };

        var v = DeathDropModel.Survives(carried, new HashSet<uint> { 1, 2 }, 25);

        Assert.Equal(1u, v.BestNeededWcid);
        Assert.Equal(5940, v.BestNeededMax);
        Assert.Equal(2, v.Above);
        Assert.False(v.Survives); // N_max 3 at level 25
        Assert.True(DeathDropModel.Survives(carried, new HashSet<uint> { 1, 2 }, 15).Survives); // N_max 1 at level 15
    }

    [Fact]
    public void A_needed_item_that_is_bonded_or_of_no_category_is_not_a_candidate_so_nothing_is_at_risk()
    {
        var carried = new List<CarriedItem> { I(1, Armor, 5000, wielded: true, bonded: true), I(2, 0x200, 5000), I(3, Gem, 100) };

        var v = DeathDropModel.Survives(carried, new HashSet<uint> { 1, 2 }, 126);

        Assert.True(v.Survives);
        Assert.Equal(0u, v.BestNeededWcid);
        Assert.Equal(0, v.BestNeededMax);
        Assert.Equal(1, v.Candidates);
    }

    [Fact]
    public void A_wielded_needed_item_is_not_at_risk_below_level_35_and_is_from_level_35()
    {
        var carried = new List<CarriedItem> { I(1, Caster, 5400, wielded: true), I(2, Gem, 9000) };

        Assert.Equal(0u, DeathDropModel.Survives(carried, new HashSet<uint> { 1 }, 34).BestNeededWcid); // wielded: not a candidate
        var at35 = DeathDropModel.Survives(carried, new HashSet<uint> { 1 }, 35);
        Assert.Equal(1u, at35.BestNeededWcid);
        Assert.Equal(1, at35.Above);
        Assert.Equal(3, at35.MaxDrops); // 35 / 20 + 2
        Assert.False(at35.Survives);
    }

    [Fact]
    public void Up_to_level_ten_nothing_drops_so_everything_survives_and_the_describe_line_names_the_numbers()
    {
        var v = DeathDropModel.Survives(OneNeeded(), NeededOne, 10);

        Assert.True(v.Survives);
        Assert.Equal(0, v.MaxDrops);
        Assert.Equal("survives=yes level=10 drops=0..0 candidates=1 above=0 best_needed=999 best_needed_max=1100 weakest_above_min=0", v.Describe());
    }

    [Fact]
    public void M5_3_d4_the_needed_set_is_the_lines_of_the_mages_own_kit_and_the_retired_loot_gear_is_not_in_it()
    {
        var kit = GearKit.Of([
            new GearItem(2472, "Wand", GearKind.Wand, GearKit.WeenieTypeCaster, GearKit.ItemTypeCaster, EquipMask.Held, 1, null, 50),
            new GearItem(6071, "Faran Robe", GearKind.Armour, 2, GearKit.ItemTypeClothing, EquipMask.ChestArmor, 1, null, 100),
            new GearItem(15268, "Focus of Enchantment", GearKind.Focus, 0, 0x1000, EquipMask.None, 1, null, 10),
            new GearItem(690, "Pyreal Scarab", GearKind.Component, 32, GearKit.ItemTypeSpellComponents, EquipMask.None, 100, 100, 5),
            new GearItem(8897, "Platinum Scarab", GearKind.Component, 32, GearKit.ItemTypeSpellComponents, EquipMask.None, 50, 50, 5),
        ]);

        Assert.Equal([690u, 2472u, 6071u, 8897u, 15268u], DeathDropModel.NeededOf(kit).Order());
        Assert.All(kit.Items, i => Assert.True(DeathDropModel.IsNeeded(kit, i.Wcid), i.Name));
        Assert.False(DeathDropModel.IsNeeded(kit, 8552)); // the Healer's Heart, the retired loot wand
        Assert.False(DeathDropModel.IsNeeded(kit, 3715)); // the Olthoi Helm, retired with it
        Assert.False(DeathDropModel.IsNeeded(kit, 81)); // the leather leggings a pack may keep
        Assert.False(DeathDropModel.IsNeeded(kit, 6600)); // bonded: never a candidate
        Assert.Throws<ArgumentNullException>(() => DeathDropModel.NeededOf(null!));
    }

    [RequiresDatsFact]
    public void M5_3_d4_the_needed_set_of_each_dungeons_real_kit_is_its_wand_robe_foci_and_stock_lines()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var key in new[] { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" })
        {
            var ground = GroundCatalog.Get(key);
            var kit = GearKit.Build(spells, "test", ground: ground);
            var needed = DeathDropModel.NeededOf(kit);

            Assert.Equal(kit.Items.Select(i => i.Wcid).Order(), needed.Order());
            Assert.Contains(kit.WandItem.Wcid, needed); // the vendor wand, not weenie 8552
            Assert.All(kit.ArmourItems, a => Assert.Contains(a.Wcid, needed));
            Assert.All(GearKit.MageFociWcids, f => Assert.Contains(f, needed));
            Assert.All(ground.Supplies.Stock, line => Assert.Contains(line.Wcid, needed));
            Assert.DoesNotContain(GearKit.Wand.Wcid, needed); // The Healer's Heart is retired
        }

        // The two kits differ (their robes), so the same listing counts differently under each.
        var den = GearKit.Build(spells, "test", ground: GroundCatalog.Get("black-spawn-den-aluvian"));
        var tethana = GearKit.Build(spells, "test", ground: GroundCatalog.Get("tusker-temple-fort-tethana"));
        Assert.True(DeathDropModel.IsNeeded(den, 5851) && DeathDropModel.IsNeeded(den, 56) && !DeathDropModel.IsNeeded(tethana, 5851) && !DeathDropModel.IsNeeded(den, 6071)); // M5 7 F13 (D87): the den's hooded robe and gauntlets
        Assert.True(DeathDropModel.IsNeeded(tethana, 5910) && !DeathDropModel.IsNeeded(den, 5910));
    }

    /// <summary>
    /// One ordinary, light, expensive carried item of the model's cases (M5 3 d3: the fixture of what M4 3.1 called its decoys, kept here because the model's cases need a set of eight valuables, each the first of its death category; 
    /// the decoys themselves are retired, D51). Values are the pinned world database's (read-only SELECTs, 2026-10-21).
    /// </summary>
    /// <param name="Wcid">The weenie class id.</param>
    /// <param name="Name">The weenie's name.</param>
    /// <param name="ItemType">ACE's <c>ItemType</c> bits.</param>
    /// <param name="UnitValue">The Value of one item.</param>
    internal sealed record Valuable(uint Wcid, string Name, uint ItemType, int UnitValue);

    /// <summary>Eight valuables, one in each of eight death categories, each worth 8,000 or more.</summary>
    internal static readonly IReadOnlyList<Valuable> Valuables =
    [
        new(27329, "Massive Mana Charge", 0x80000, 65_000),
        new(9295, "Intricate Carving Tool", 0x80, 10_000),
        new(41396, "Handle Mold", 0x4000000, 10_000),
        new(22565, "Tusker Island Shirt", 0x4, 9_000),
        new(25522, "Skull Cap", 0x2, 8_500),
        new(11007, "Virindi Scalpel", 0x1, 8_420),
        new(11129, "Healing Chocolate Olthoi Cake", 0x20, 8_000),
        new(27771, "Entrancing Adjanite Gem", 0x800, 15_000),
    ];

    // ---------------------------------------------------------------- the real numbers (cache/m4/3-1/facts.md)

    /// <summary>
    /// BSD Mage's carried items by the lead's SELECTs (facts.md, "Death drops"): the wand The Healer's Heart 5,400 (Caster, wielded), the Olthoi Helm 2,200, the Platemail Gauntlets 1,600 and the Sollerets 653 (Armor, worn), the Pyreal Scarab 1,000 (18 in a stack, a
    /// component), the Leather Leggings 2,400 (Armor, in the pack, not needed), the Mana Scarab 15,000 (5 in a stack; not needed); BONDED, so never candidates: the Greater Amuli Shadow Coat 6600 (2,610) and Leggings 6606 (3,040) and the Platinum Scarab 8897 (10,000, 5 in a stack).
    /// </summary>
    internal static List<CarriedItem> MageItems(IEnumerable<Valuable> valuables, bool manaScarab = true)
    {
        var list = new List<CarriedItem>
        {
            I(8552, Caster, 5400, wielded: true),
            I(3715, Armor, 2200, wielded: true),
            I(57, Armor, 1600, wielded: true),
            I(107, Armor, 653, wielded: true),
            I(6600, Armor, 2610, wielded: true, bonded: true),
            I(6606, Armor, 3040, wielded: true, bonded: true),
            I(81, Armor, 2400), // the leather leggings in the pack
            I(690, SpellComponents, 18_000, stack: 18), // the Pyreal Scarab, 1,000 each
            I(8897, SpellComponents, 50_000, stack: 5, bonded: true), // the Platinum Scarab, 10,000 each, bonded
        };
        if (manaScarab)
        {
            list.Add(I(ManaScarab, SpellComponents, 75_000, stack: 5)); // 15,000 each
        }

        foreach (var d in valuables)
        {
            list.Add(I(d.Wcid, d.ItemType, d.UnitValue));
        }

        return list;
    }

    [Fact]
    public void The_eight_valuables_protect_the_wand_at_level_126_in_every_corner_of_the_variance_and_the_same_kit_without_them_does_not()
    {
        var with = DeathDropModel.Survives(MageItems(Valuables), LootKitNeeded, 126);
        Assert.True(with.Survives, with.Describe());
        Assert.Equal(8, with.MaxDrops);
        Assert.Equal(6, with.MinDrops);
        Assert.Equal(9, with.Above); // the eight valuables and the unneeded Mana Scarab: nine above the wand against at most eight drops
        Assert.Equal(8552u, with.BestNeededWcid);
        Assert.Equal(5940, with.BestNeededMax); // the wand, 5,400 at +10%
        Assert.Equal(7200, with.WeakestAboveMin); // the 8,000 cake at -10%
        Assert.Equal(15, with.Candidates); // wand, helm, gauntlets, sollerets, leather leggings, Pyreal Scarab, Mana Scarab and the eight valuables (the coat, the leggings and the Platinum Scarab are bonded)

        var without = DeathDropModel.Survives(MageItems([]), LootKitNeeded, 126);
        Assert.False(without.Survives, without.Describe());
        Assert.Equal(1, without.Above); // the Mana Scarab alone
        Assert.Equal(7, without.Candidates);

        // Eight valuables alone cover the most a death drops at level 126 and the Mana Scarab is the spare (the manaScarab:false corner, folded in by the M4 polish fold).
        var eightAlone = DeathDropModel.Survives(MageItems(Valuables, manaScarab: false), LootKitNeeded, 126);
        Assert.True(eightAlone.Survives, eightAlone.Describe());
        Assert.Equal(8, eightAlone.Above);
        Assert.Equal(DeathDropModel.MaxDrops(126), Math.Min(Valuables.Count, DeathDropModel.MaxDrops(126)));

        var neither = DeathDropModel.Survives(MageItems([], manaScarab: false), LootKitNeeded, 126);
        Assert.False(neither.Survives);
        Assert.Equal(0, neither.Above);
        Assert.Equal(0, neither.WeakestAboveMin);
    }

    [Fact]
    public void The_bonded_gear_is_never_a_candidate_and_the_Platinum_Scarab_being_bonded_is_what_keeps_the_wand_the_bar()
    {
        var items = MageItems(Valuables);
        var ranked = DeathDropModel.Rank(items, LootKitNeeded, 126);
        Assert.DoesNotContain(ranked, r => r.Wcid is 6600 or 6606 or 8897);

        // Read as not bonded (what a check would see if the weenie description carried no bonded flag: TODO(m4-valuable-bonded-flag)), the Platinum Scarab is a needed 10,000 (best case 11,000) and every valuable is below it: the answer is no,
        // so the lead's SELECT that it is bonded carries the whole design (the owner's point, D22).
        var unbonded = items.Select(i => i with { Bonded = false }).ToList();
        var v = DeathDropModel.Survives(unbonded, LootKitNeeded, 126);
        Assert.False(v.Survives, v.Describe());
        Assert.Equal(8897u, v.BestNeededWcid);
        Assert.Equal(11_000, v.BestNeededMax);
    }

    [Fact]
    public void Seven_valuables_plus_the_Mana_Scarab_survive_and_six_do_not_at_N_max_8()
    {
        var all = Valuables;
        for (var skip = 0; skip < all.Count; skip++)
        {
            var seven = all.Where((_, i) => i != skip).ToList();
            var v = DeathDropModel.Survives(MageItems(seven), LootKitNeeded, 126);
            Assert.True(v.Survives, $"without {all[skip].Name}: {v.Describe()}");
            Assert.Equal(8, v.Above);
        }

        for (var a = 0; a < all.Count; a++)
        {
            for (var b = a + 1; b < all.Count; b++)
            {
                var six = all.Where((_, i) => i != a && i != b).ToList();
                var v = DeathDropModel.Survives(MageItems(six), LootKitNeeded, 126);
                Assert.False(v.Survives, $"without {all[a].Name} and {all[b].Name}: {v.Describe()}");
                Assert.Equal(7, v.Above);
            }
        }
    }

    [Fact]
    public void The_other_components_of_the_kit_change_nothing_they_are_needed_and_far_below_the_wand()
    {
        // facts.md says only "the rest lower": every component but the Mana Scarab at a stand-in 100 each (needed; halved to 50 behind the Mana Scarab).
        var items = MageItems(Valuables);
        foreach (var c in ComponentWeenies.All.Where(c => c.Wcid is not (690 or 8897 or 37155)))
        {
            items.Add(I(c.Wcid, SpellComponents, 100));
        }

        var v = DeathDropModel.Survives(items, LootKitNeeded, 126);

        Assert.True(v.Survives, v.Describe());
        Assert.Equal(9, v.Above);
        Assert.Equal(8552u, v.BestNeededWcid);
        Assert.Equal(5940, v.BestNeededMax);
    }

    [Fact]
    public void An_independent_simulation_of_ACEs_draw_agrees_the_wand_never_drops_with_the_valuables_and_drops_without_them()
    {
        // ACE's DeathItems and GetNumItemsDropped drawn 20,000 times at level 126 (a seeded Random; the sort, the halving, a variance in -10% to +10%, the top N with N = 6 + 0 to 2).
        Assert.Equal(0, WandDrops(MageItems(Valuables), 126, 20_000, 1));
        Assert.Equal(20_000, WandDrops(MageItems([]), 126, 20_000, 2)); // with the Mana Scarab alone above it the wand is the second of the list: every draw takes it (N is at least 6)
        Assert.Equal(0, WandDrops(MageItems(Valuables.Take(7).ToList()), 126, 20_000, 3)); // seven valuables and the Mana Scarab: eight above, N_max 8
    }

    [Fact]
    public void With_six_valuables_and_the_Mana_Scarab_the_wand_drops_in_the_draws_that_roll_the_most_items()
    {
        // Seven items above the wand and N_max 8: the wand is the eighth when N rolls 8 (one draw in three), never otherwise.
        var drops = WandDrops(MageItems(Valuables.Take(6).ToList()), 126, 30_000, 5);

        Assert.InRange(drops, 8_000, 12_000);
        Assert.False(DeathDropModel.Survives(MageItems(Valuables.Take(6).ToList()), LootKitNeeded, 126).Survives);
    }

    /// <summary>The draws in which the wand (8552) is among the dropped items: ACE's algorithm, written out again from the rule, over <paramref name="draws"/> seeded draws.</summary>
    private static int WandDrops(List<CarriedItem> carried, int level, int draws, int seed)
    {
        var ranked = DeathDropModel.Rank(carried, new HashSet<uint>(), level); // category order, halved: the draw below only adds the variance and sorts again
        var random = new Random(seed);
        var count = 0;
        var values = new (uint Wcid, int Value, int Order)[ranked.Length];
        for (var d = 0; d < draws; d++)
        {
            for (var i = 0; i < ranked.Length; i++)
            {
                var variance = (float)(-0.1 + (random.NextDouble() * 0.2));
                values[i] = (ranked[i].Wcid, (int)(ranked[i].HalvedValue + (ranked[i].HalvedValue * variance)), i);
            }

            var sorted = values.OrderByDescending(v => v.Value).ThenBy(v => v.Order).ToArray();
            var n = Math.Min(DeathDropModel.MaxItemsDropped, (level / 20) + random.Next(0, 3));
            for (var i = 0; i < n && i < sorted.Length; i++)
            {
                if (sorted[i].Wcid == 8552)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }
}
