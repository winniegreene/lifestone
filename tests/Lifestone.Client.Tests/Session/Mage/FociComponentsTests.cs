using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 pack rules, the components rule (<see cref="FociComponents"/>) and the coin float (<see cref="CoinFloat"/>), on the climber's live pack (a read-only SELECT of ace_shard, character Qwinnie, 2026-11-13 about 12:50 UTC): all four foci
/// held, a book of levels I to VI, 39 component stacks (15 in the main pack, 24 in the components sack), 52,094 pyreals in three stacks. With the foci held only the scarab of each level the book holds and the Prismatic Taper are kept:
/// 6 stacks kept, 33 sold (the 3 peas among them, which ACE never lets stand in for a scarab).
/// </summary>
public sealed class FociComponentsTests
{
    /// <summary>ACE's ItemType SpellComponents.</summary>
    private const uint Component = 0x1000;

    /// <summary>The live pack's component stacks in the main pack: (wcid, name, the verdict the rule must give).</summary>
    public static TheoryData<uint, string, bool> MainPack => new()
    {
        { 686, "Copper Scarab", true },
        { 687, "Gold Scarab", true },
        { 688, "Silver Scarab", true },
        { 690, "Pyreal Scarab", true },
        { 741, "Birch Talisman", false },
        { 772, "Hawthorn", false },
        { 1643, "Blue Taper", false },
        { 1645, "Green Taper", false },
        { 1646, "Grey Taper", false },
        { 1649, "Pink Taper", false },
        { 1652, "White Taper", false },
        { 8326, "Copper Pea", false },
        { 8328, "Iron Pea", false },
        { 8331, "Silver Pea", false },
        { 20631, "Prismatic Taper", true },
    };

    /// <summary>The live pack's component stacks in the components sack 0x800001A4.</summary>
    public static TheoryData<uint, string, bool> Sack => new()
    {
        { 626, "Powdered Hematite", false },
        { 627, "Alder Talisman", false },
        { 691, "Lead Scarab", true },
        { 750, "Rowan Talisman", false },
        { 754, "Cadmia", false },
        { 755, "Cinnabar", false },
        { 759, "Quicksilver", false },
        { 760, "Realgar", false },
        { 762, "Turpeth", false },
        { 763, "Verdigris", false },
        { 764, "Vitriol", false },
        { 768, "Damiana", false },
        { 774, "Hyssop", false },
        { 778, "Saffron", false },
        { 782, "Powdered Agate", false },
        { 789, "Powdered Moonstone", false },
        { 790, "Powdered Onyx", false },
        { 1644, "Brown Taper", false },
        { 1647, "Indigo Taper", false },
        { 1648, "Orange Taper", false },
        { 1650, "Red Taper", false },
        { 1651, "Violet Taper", false },
        { 1653, "Yellow Taper", false },
        { 1654, "Turquoise Taper", false },
    };

    /// <summary>
    /// A book of levels I to VI (her book holds Flame Bolt I and Heal Self II among its 80 spells, and the level VI scrolls): a base formula of each level, its scarab first (component ids 1 Lead to 6 Pyreal), then a herb, a powder, a
    /// potion, a talisman and a coloured taper, as the dat's war and life formulas are laid out.
    /// </summary>
    private static Dictionary<uint, int> BookOfLevelsOneToSix() =>
        FociComponents.BookBurns([.. Enumerable.Range(1, 6).Select(level => (IReadOnlyList<uint>)[(uint)level, 15, 34, 46, 55, 63, 0, 0])]);

    private static bool Stocked(uint wcid)
    {
        var abode = GroundCatalog.Get("tusker-abode");
        return abode.Supplies.StockOf(wcid) is not null || abode.TownStock.Any(t => t.Wcid == wcid);
    }

    [Fact]
    public void The_book_of_levels_one_to_six_burns_the_six_scarabs_and_the_Prismatic_Taper_only()
    {
        var burns = BookOfLevelsOneToSix();

        Assert.Equal([686u, 687, 688, 689, 690, 691, FociComponents.PrismaticTaperWcid], burns.Keys.Order().ToArray());
        Assert.Equal(6, burns[FociComponents.PrismaticTaperWcid]);
    }

    [Theory]
    [MemberData(nameof(MainPack))]
    [MemberData(nameof(Sack))]
    public void With_every_focus_held_the_live_pack_keeps_the_scarabs_and_the_Prismatic_Taper_and_sells_the_rest(uint wcid, string name, bool kept)
    {
        var verdict = FociComponents.Judge(wcid, Component, allFociHeld: true, BookOfLevelsOneToSix(), Stocked(wcid));

        Assert.NotNull(verdict);
        Assert.True(verdict.Value.Action == (kept ? PackAction.Keep : PackAction.Sell), $"{name}: {verdict.Value.Rule}: {verdict.Value.Why}");
    }

    [Fact]
    public void The_live_pack_sells_33_of_its_39_component_stacks()
    {
        var burns = BookOfLevelsOneToSix();
        var stacks = MainPack.Concat(Sack).Select(row => (uint)row[0]).ToList();

        var sold = stacks.Count(w => FociComponents.Judge(w, Component, true, burns, Stocked(w))!.Value.Action == PackAction.Sell);
        var mainSold = MainPack.Count(row => FociComponents.Judge((uint)row[0], Component, true, burns, Stocked((uint)row[0]))!.Value.Action == PackAction.Sell);

        Assert.Equal(39, stacks.Count);
        Assert.Equal(33, sold);
        Assert.Equal(10, mainSold); // the main pack frees 10 at the sale and 5 more when its kept scarabs and tapers go to the emptied sack: 15 of 102
    }

    [Theory]
    [InlineData(8326u)]
    [InlineData(8328u)]
    [InlineData(8331u)]
    public void A_pea_is_sold_because_ACE_never_lets_it_stand_in_for_its_scarab(uint pea)
    {
        var verdict = FociComponents.Judge(pea, Component, true, BookOfLevelsOneToSix())!.Value;

        Assert.Equal(PackAction.Sell, verdict.Action);
        Assert.Equal(FociComponents.RulePea, verdict.Rule);
        Assert.Contains("HasComponentsForSpell", verdict.Why, StringComparison.Ordinal);
        Assert.True(FociComponents.IsPea(pea));
        Assert.False(FociComponents.IsFociSlot(pea));
    }

    [Fact]
    public void An_account_formula_stock_line_is_sold_while_every_focus_is_held_and_kept_when_one_is_missing()
    {
        var burns = BookOfLevelsOneToSix();

        Assert.Equal(FociComponents.RuleAccountOnly, FociComponents.Judge(1643, Component, true, burns, stocked: true)!.Value.Rule); // the abode's Blue Taper line, keep 2
        var missing = FociComponents.Judge(1643, Component, false, burns, stocked: true)!.Value;
        Assert.Equal(PackAction.Keep, missing.Action);
        Assert.Equal(FociComponents.RuleFociMissing, missing.Rule);
    }

    [Fact]
    public void An_unread_book_keeps_every_component_and_a_non_component_has_no_verdict()
    {
        Assert.Equal(FociComponents.RuleBookUnread, FociComponents.Judge(1643, Component, true, new Dictionary<uint, int>())!.Value.Rule);
        Assert.Null(FociComponents.Judge(2625, 0x40000, true, BookOfLevelsOneToSix())); // a trade note
    }

    [Fact]
    public void A_scarab_of_no_book_level_is_sold_unless_the_ground_buys_it()
    {
        var levelsOneToFour = FociComponents.BookBurns([.. Enumerable.Range(1, 4).Select(level => (IReadOnlyList<uint>)[(uint)level, 15, 0, 0, 0, 0, 0, 0])]);

        Assert.Equal(FociComponents.RuleScarabUnused, FociComponents.Judge(690, Component, true, levelsOneToFour)!.Value.Rule); // Pyreal Scarab, level VI
        Assert.Equal(FociComponents.RuleStocked, FociComponents.Judge(690, Component, true, levelsOneToFour, stocked: true)!.Value.Rule); // the abode's town stock buys it
    }

    [Fact]
    public void An_unmapped_component_that_is_no_pea_is_kept_and_named()
    {
        var verdict = FociComponents.Judge(37117, Component, true, BookOfLevelsOneToSix())!.Value; // the Dark Scarab, the void school's

        Assert.Equal(PackAction.Keep, verdict.Action);
        Assert.Equal(FociComponents.RuleUnknown, verdict.Rule);
        Assert.Contains("TODO(m6-pack-rule-unknown)", verdict.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_verdict_line_names_the_rule_the_action_and_the_item()
    {
        var line = FociComponents.Judge(8331, Component, true, BookOfLevelsOneToSix())!.Value.Line("Silver Pea", 0x80001F5F, 2);

        Assert.StartsWith("pack rule components-pea: sell 'Silver Pea' x2 0x80001F5F: a pea", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 25_000)]
    [InlineData(1, 25_000)]
    [InlineData(25_000, 25_000)]
    [InlineData(25_001, 50_000)]
    [InlineData(65_406, 75_000)]
    public void The_float_is_the_spend_rounded_up_to_whole_coin_stacks(long spend, long expected) => Assert.Equal(expected, CoinFloat.Of(spend));

    [Fact]
    public void The_live_pack_at_the_abode_spends_17070_at_home_and_16275_in_town_so_the_float_is_two_stacks()
    {
        var held = new Dictionary<uint, int> { [20631] = 281, [688] = 20, [686] = 20, [377] = 33, [379] = 100, [27319] = 13, [27322] = 9, [687] = 100, [690] = 63, [1643] = 2, [772] = 4 };

        var spend = CoinFloat.Spend(GroundCatalog.Get("tusker-abode"), w => held.GetValueOrDefault(w), allFociHeld: true, out var home, out var town);

        Assert.Equal(569 * 30, home); // the Prismatic Taper to its keep of 850; the account formula's lines are not bought while every focus is held
        Assert.Equal(21 * 775, town); // the Mana Tincture under its trip line 10, to its keep 30
        Assert.Equal(50_000, CoinFloat.Of(spend)); // 52,094 held: 2,094 over it, under the 10,000 note's 11,500, so no note; the slots come from the components sold
    }

    [Fact]
    public void Without_every_focus_the_account_formula_lines_join_the_spend()
    {
        var held = new Dictionary<uint, int> { [20631] = 850, [688] = 20, [686] = 20, [377] = 30, [379] = 100 };
        var ground = GroundCatalog.Get("tusker-abode");

        CoinFloat.Spend(ground, w => held.GetValueOrDefault(w), allFociHeld: true, out var withFoci, out _);
        CoinFloat.Spend(ground, w => held.GetValueOrDefault(w), allFociHeld: false, out var without, out _);

        Assert.Equal(0, withFoci);
        Assert.Equal(ground.Supplies.Stock.Where(l => FociComponents.IsAccountOnly(l.Wcid)).Sum(l => (long)l.Keep * (ground.PriceOf(l.Wcid) ?? 0)), without);
        Assert.True(without > 0);
    }
}
