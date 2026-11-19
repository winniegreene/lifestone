using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 pack room (<c>m6-pack-room</c>; live 2026-11-14, <c>cache/m6/14/live/ladder18</c>): the burden budget for what she carries by choice (<see cref="PackBudgetRule"/>) on the climber's live shape in the Tusker Barracks: level 66,
/// Strength 79 and the bot's 40, a capacity of 17,850; a read-only snapshot of her pack (811 Prismatic Tapers, 100 Mana Potions, 29 Mana Tinctures, 21 Health Tinctures, 9 Health Elixirs, 33 Potions of Healing, four foci, 100 Gold
/// and 95 Pyreal Scarabs); between the 21:28 and 22:25 UTC visits (57 minutes) she burned 517 tapers, drank nothing, and the loot kept 3,735 burden and left 338 items (132,036 burden, 2,112,938 Value) over its load line.
/// </summary>
public sealed class PackBudgetTests
{
    private const uint Taper = GearKit.PrismaticTaperWcid;
    private const uint GoldScarab = 687;
    private const uint PyrealScarab = 690;
    private const uint HealthElixir = 2458;
    private const int Capacity = 17_850;

    /// <summary>Her worn gear, packs, coin and notes: her burden less the loot and the supplies (17,776 at the visit, 2,000 of loot, 12,576 of supplies).</summary>
    private const long Fixed = 3_200;

    private static IReadOnlyList<PackLine> HerLines() =>
    [
        new(Taper, "Prismatic Taper", PackLineKind.Stock, 811, 850, 300, 6, false),
        new(688, "Silver Scarab", PackLineKind.Stock, 20, 20, 5, 4, false),
        new(686, "Copper Scarab", PackLineKind.Stock, 20, 20, 5, 4, false),
        new(FighterKit.PotionWcid, "Potion of Healing", PackLineKind.Stock, 33, 30, 6, 15, true),
        new(FighterKit.ManaPotionWcid, "Mana Potion", PackLineKind.Stock, 100, 100, 10, 15, true),
        new(HealthDrinks.TinctureWcid, "Health Tincture", PackLineKind.Town, 21, 12, 6, 50, true),
        new(ManaDrinks.TinctureWcid, "Mana Tincture", PackLineKind.Town, 29, 30, 10, 50, true),
        new(GoldScarab, "Gold Scarab", PackLineKind.Town, 100, 100, 40, 4, false),
        new(PyrealScarab, "Pyreal Scarab", PackLineKind.Town, 95, 100, 40, 4, false),
        new(HealthElixir, "Health Elixir", PackLineKind.LootDrink, 9, 9, 0, 75, true),
        new(15268, "Foci of Enchantment", PackLineKind.Held, 1, 1, 0, 400, false),
        new(15269, "Foci of Artifice", PackLineKind.Held, 1, 1, 0, 400, false),
        new(15270, "Foci of Verdancy", PackLineKind.Held, 1, 1, 0, 400, false),
        new(15271, "Foci of Strife", PackLineKind.Held, 1, 1, 0, 400, false),
    ];

    private static long SuppliesOf(IReadOnlyList<PackLine> lines) => lines.Sum(l => (long)l.Held * l.Unit);

    /// <summary>The 57 minutes between her visits of 21:28 and 22:25 UTC.</summary>
    private static PackUseMeasure HerHour(long lootKept = 3_735, int leftItems = 338, long leftBurden = 132_036) =>
        new(57, new Dictionary<uint, int> { [Taper] = 517, [GoldScarab] = 25, [PyrealScarab] = 5 }, lootKept + leftBurden, lootKept, leftItems, leftBurden, 2_112_938);

    /// <summary>
    /// M6 14 (the owner, 2026-11-14: at her level the potions go; Revitalize then Stamina to Mana restores the mana): when the stamina dance covers her mana, every drink's keep is 0 and every one held is sold, measured or not; the
    /// other wares are cut as before.
    /// </summary>
    [Fact]
    public void When_the_stamina_dance_covers_her_mana_every_drink_is_sold_and_none_is_kept()
    {
        var lines = HerLines();
        var burden = Fixed + SuppliesOf(lines) + 2_000;
        var drinks = lines.Where(l => l.Drink).ToList();
        Assert.Equal(5, drinks.Count);

        foreach (var measure in (PackUseMeasure?[])[HerHour(), null])
        {
            var budget = PackBudgetRule.Plan(Capacity, MageLootThresholds.DefaultLoadLinePercent, burden, 2_000, lines, measure, ditchDrinks: true);
            foreach (var d in drinks)
            {
                Assert.Equal((d.Held, 0), (budget.SellOf(d.Wcid), budget.Cuts.Single(c => c.Line.Wcid == d.Wcid).Target));
            }

            var kept = PackBudgetRule.Plan(Capacity, MageLootThresholds.DefaultLoadLinePercent, burden, 2_000, lines, measure);
            Assert.Equal(kept.KeepOf(Taper), budget.KeepOf(Taper)); // the wares that are not drinks are cut as before
            Assert.Equal(kept.Planned - kept.Cuts.Where(c => c.Line.Drink).Sum(c => (long)c.Target * c.Line.Unit), budget.Planned);
        }
    }

    /// <summary>
    /// M6 audit (the owner, 2026-11-19: about 75 minutes of consumables, the town trip hourly with the bot's rebuff): her live shape with the loot wanting 135,771 in 57 minutes. The rule of before cut every ware to its
    /// line and one interval's use (the tapers to 817) and gave the rest to the loot; now each ware's need is its line and 75 minutes of her use (<see cref="PackBudgetRule.SupplyMinutes"/>), the needs come first, and the
    /// loot's room is what is left (<see cref="PackBudgetVerdict.KitFirst"/>).
    /// </summary>
    [Fact]
    public void Her_live_shape_keeps_75_minutes_of_her_use_before_the_loots_room_and_sells_the_drinks_she_did_not_drink()
    {
        var lines = HerLines();
        Assert.Equal(12_576, SuppliesOf(lines)); // the snapshot's supplies, about 14,000 with the smaller lines
        var burden = Fixed + SuppliesOf(lines) + 2_000;

        var budget = PackBudgetRule.Plan(Capacity, MageLootThresholds.DefaultLoadLinePercent, burden, 2_000, lines, HerHour());

        Assert.Equal(PackBudgetVerdict.KitFirst, budget.Verdict); // 135,771 burden of loot wanted in 57 minutes: no capacity holds it, so the needs come first and the loot gets the rest
        Assert.Equal((17_850L, Fixed, 135_771L), (budget.Line, budget.Fixed, budget.Wanted));
        Assert.Equal(12_880, budget.AtTop);
        Assert.Equal(8_810, budget.AtLow);
        Assert.Equal(17_850 - Fixed - 8_810, budget.Room); // the loot's room: the line less the fixed and the needs

        Assert.Null(budget.KeepOf(Taper)); // her line 300 and 75 minutes of the 517 she burned in 57 (681): 981, over the profile's 850, so the keep stands (no cut; the profile's keep is the cap)
        Assert.Equal(850, budget.Cuts.Single(c => c.Line.Wcid == Taper).Need);
        Assert.Equal(0, budget.SellOf(Taper)); // a ware that is not a drink is never sold
        Assert.Equal(95, budget.KeepOf(PyrealScarab)); // need 47 (40 and 75 minutes of 5 in 57); held 95 is never sold, so the buys stop where she stands
        Assert.Null(budget.KeepOf(GoldScarab)); // held 100 over her need of 73: nothing to cut, nothing sold
        Assert.Null(budget.KeepOf(688));

        Assert.Equal(11, budget.KeepOf(FighterKit.ManaPotionWcid)); // one over its trip line 10: she drank none, and a line left on its trip line would call a trip at her first drink
        Assert.Equal(89, budget.SellOf(FighterKit.ManaPotionWcid));
        Assert.Equal(26, budget.SellOf(FighterKit.PotionWcid));
        Assert.Equal(14, budget.SellOf(HealthDrinks.TinctureWcid));
        Assert.Equal(18, budget.SellOf(ManaDrinks.TinctureWcid));
        Assert.Equal(9, budget.SellOf(HealthElixir)); // no line names it and she drank none
        Assert.All(budget.Cuts, c => Assert.True(c.Target >= c.Line.Floor, $"{c.Line.Name}: {c.Target} under its line {c.Line.Floor}"));
        Assert.All(budget.Cuts.Where(c => c.Cuts && c.Line.Kind != PackLineKind.LootDrink), c => Assert.True(c.Target > c.Line.Floor, $"{c.Line.Name}: {c.Target} on its trip line {c.Line.Floor}"));

        Assert.Equal(8_810, budget.Planned);
        Assert.Equal(5_840, budget.RoomLeft); // from 1,770 at the profile's keeps
        Assert.Contains("the needs come first, so the loot's room is cut to 5840", budget.Summary(), StringComparison.Ordinal);
        Assert.Contains("the loot wanted 135771 (57.0 min of it since the last visit)", budget.Summary(), StringComparison.Ordinal);
        Assert.DoesNotContain(budget.CutLines(), l => l.StartsWith("pack budget: cut Prismatic Taper", StringComparison.Ordinal));
        Assert.Contains(budget.CutLines(), l => l.StartsWith("pack budget: cut Pyreal Scarab (690, Town): 100 -> 95 (held 95, keep 100; need 47: its line 40 and 75 min of her use (5 used in 57.0 min)", StringComparison.Ordinal));
        Assert.Contains(budget.CutLines(), l => l.StartsWith("pack budget: cut Health Elixir (2458, LootDrink): 9 -> 0", StringComparison.Ordinal) && l.Contains("sells 9", StringComparison.Ordinal));
        Assert.Equal(new Dictionary<uint, int> { [FighterKit.PotionWcid] = 7, [FighterKit.ManaPotionWcid] = 11, [HealthDrinks.TinctureWcid] = 7, [ManaDrinks.TinctureWcid] = 11, [PyrealScarab] = 95 }, budget.Keeps);
    }

    /// <summary>M6 audit: even with no room for the loot, needs over the line fall back toward one interval's use by one scale, never under it (the rule of before at worst).</summary>
    [Fact]
    public void Needs_over_the_line_even_with_no_loot_room_scale_back_toward_one_intervals_use()
    {
        var lines = HerLines();
        var budget = PackBudgetRule.Plan(11_911, 100, Fixed + SuppliesOf(lines) + 2_000, 2_000, lines, HerHour()); // the line less the fixed: 8,711

        Assert.Equal(PackBudgetVerdict.AtNeeds, budget.Verdict); // over one interval's needs (8,612), under 75 minutes' (8,810)
        Assert.Equal(0, budget.Room);
        Assert.InRange(budget.Scale, 0.49, 0.51); // 99 of 198
        Assert.Equal(817 + 16, budget.KeepOf(Taper)); // one interval's 817 and half the 33 to its need of 850
        Assert.True(budget.Planned <= 11_911 - Fixed, $"{budget.Planned}");
    }

    [Fact]
    public void A_budget_between_the_needs_and_the_keeps_scales_every_cut_by_one_share_so_the_larger_lines_give_the_most()
    {
        var lines = HerLines();
        var budget = PackBudgetRule.Plan(Capacity, 100, Fixed + SuppliesOf(lines) + 2_000, 2_000, lines, HerHour(lootKept: 3_000, leftItems: 0, leftBurden: 0));

        Assert.Equal(PackBudgetVerdict.Scaled, budget.Verdict); // the budget 11,650 sits between the needs at 75 minutes of her use (8,810) and the keeps (12,880)
        Assert.Equal(11_650, budget.Budget);
        Assert.InRange(budget.Scale, 0.69, 0.70); // 2,840 of 4,070
        Assert.True(budget.Planned <= budget.Budget, $"{budget.Planned} over {budget.Budget}");
        Assert.Equal(73, budget.KeepOf(FighterKit.ManaPotionWcid)); // 11 + 69.8% of the 89 between its need and its top
        Assert.Equal(27, budget.SellOf(FighterKit.ManaPotionWcid));
        Assert.Null(budget.KeepOf(Taper)); // its need, 75 minutes of her use, is its keep: never cut
    }

    [Fact]
    public void Ample_capacity_changes_nothing()
    {
        var lines = HerLines();
        var budget = PackBudgetRule.Plan(30_000, 100, Fixed + SuppliesOf(lines), 0, lines, HerHour(lootKept: 4_000, leftItems: 0, leftBurden: 0));

        Assert.Equal(PackBudgetVerdict.Within, budget.Verdict);
        Assert.All(budget.Cuts, c => Assert.False(c.Cuts, c.Line.Name));
        Assert.Empty(budget.Keeps);
        Assert.All(lines, l => Assert.Equal(0, budget.SellOf(l.Wcid)));
        Assert.Contains("within it, no keep is cut", budget.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_visit_of_a_session_is_not_measured_and_cuts_nothing_by_name()
    {
        var lines = HerLines();
        var budget = PackBudgetRule.Plan(Capacity, 100, Fixed + SuppliesOf(lines) + 2_000, 2_000, lines, measure: null);

        Assert.Equal(PackBudgetVerdict.Unmeasured, budget.Verdict);
        Assert.Empty(budget.Keeps);
        Assert.Contains("m6-pack-budget-unmeasured", budget.Summary(), StringComparison.Ordinal);
        Assert.Contains("at them the supplies weigh 12880 and leave 1770 for the loot", budget.Summary(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_memory_measures_her_use_from_the_last_visit_the_town_buys_and_the_loot_and_clears_its_loot_record_at_the_next()
    {
        var room = new PackRoomMemory();
        Assert.Null(room.Measure(100, [Taper], _ => 0)); // no visit yet
        room.BoughtAfterVisit(ManaDrinks.TinctureWcid, 5); // before any visit: nothing to add to

        room.Visited(0, new Dictionary<uint, int> { [Taper] = 850, [ManaDrinks.TinctureWcid] = 25 }, new Dictionary<uint, int> { [ManaDrinks.TinctureWcid] = 30 });
        room.BoughtAfterVisit(ManaDrinks.TinctureWcid, 5); // the town stock on the way out
        room.NoteLoot(leftItems: 2, leftValue: 23_464, leftBurden: 2_165, keptBurden: 825);
        room.NoteLoot(leftItems: 0, leftValue: 0, leftBurden: 0, keptBurden: 632);
        Assert.Equal(30, room.KeepOf(ManaDrinks.TinctureWcid));

        var held = new Dictionary<uint, int> { [Taper] = 333, [ManaDrinks.TinctureWcid] = 29, [HealthElixir] = 9 };
        var measure = room.Measure(57 * 60, [Taper, ManaDrinks.TinctureWcid, HealthElixir], w => held[w])!;

        Assert.Equal(57, measure.Minutes, 6);
        Assert.Equal(517, measure.UseOf(Taper));
        Assert.Equal(1, measure.UseOf(ManaDrinks.TinctureWcid)); // 25 + 5 bought - 29
        Assert.Equal(0, measure.UseOf(HealthElixir)); // picked up from the loot, never at the departure: not a use
        Assert.Equal((2, 2_165L, 1_457L, 3_622L), (measure.LeftItems, measure.LeftBurden, measure.LootKept, measure.LootWanted));
        Assert.Contains("57.0 min since the last visit", measure.Line(), StringComparison.Ordinal);

        room.Visited(57 * 60, held, new Dictionary<uint, int>());
        Assert.Equal((0, 0L, 0L), (room.LoadLeftItems, room.LoadLeftBurden, room.KeptBurden));
        Assert.Null(room.KeepOf(ManaDrinks.TinctureWcid));
        Assert.Equal(2, room.Visits);
    }

    [Theory]
    [InlineData(Taper, 6)]
    [InlineData(GoldScarab, 4)]
    [InlineData(FighterKit.ManaPotionWcid, 15)]
    [InlineData(HealthDrinks.TinctureWcid, 50)]
    [InlineData(HealthElixir, 75)]
    [InlineData(15271u, 400)]
    [InlineData(99_001u, 0)]
    public void The_burden_of_one_ware_is_read_from_the_world_facts(uint wcid, int burden) => Assert.Equal(burden, PackBudgetRule.UnitBurdenOf(wcid, GroundCatalog.Get("tusker-barracks")));
}
