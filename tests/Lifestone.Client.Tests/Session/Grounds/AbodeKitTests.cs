using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 (the owner, 2026-11-13: the Abode follows the Cave at 40, its kit to the Cave's standard): the Tusker Abode's profile and its alternate the Tusker Habitat carry the Cave's fight section shape (a pull rest, the full bar, a
/// retreat line of their own), the Cave's drinks and scarab lines, the Cave's two tinctures of the town stock, level VI's formulas (the Quarters' rows) and a plan row for level 40. With the retail dats, each plan row of the three
/// grounds the slice touched is the computation (SpellPlan.LevelAt over SpellPlan.ProjectedWarBase at the ladder's plan chance) at every level of its band.
/// </summary>
public sealed class AbodeKitTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static HuntingGround Cave => GroundCatalog.Get("tusker-cave");

    public static TheoryData<string> AbodeAndHabitat() => new("tusker-abode", "tusker-habitat");

    [Theory]
    [InlineData("tusker-abode", 45)]
    [InlineData("tusker-habitat", 50)]
    public void The_Abode_and_the_Habitat_carry_a_fight_section_of_the_Caves_shape(string key, int retreat)
    {
        var ground = GroundCatalog.Get(key);

        Assert.Equal(retreat, ground.FightRetreatHealthPercent); // over the Cave's 40: the Redeemer's 82 and the Liberator's 90 against the Crimsonback's 65, 4 s to leave by Lifestone Recall
        Assert.Equal(new GroundPullRest(100, 70, 100, RestPosture.Lie, 20, 0, 100, Potions: true, PotionManaPercent: 75, PotionStaminaPercent: 15), ground.PullRest); // the Redeemers' and Liberators' VisualAwarenessRange 20
        var cave = Cave.PullRest!;
        Assert.Equal((cave.HealthPercent, cave.Posture, cave.FullPercent, cave.Potions, cave.PotionManaPercent, cave.PotionStaminaPercent), (ground.PullRest!.HealthPercent, ground.PullRest.Posture, ground.PullRest.FullPercent, ground.PullRest.Potions, ground.PullRest.PotionManaPercent, ground.PullRest.PotionStaminaPercent));
        Assert.True(ground.PullRest.UntilPercent >= ground.PullRest.ManaPercent);
    }

    [Theory]
    [MemberData(nameof(AbodeAndHabitat))]
    public void The_drinks_and_the_scarab_lines_are_the_Caves(string key)
    {
        var ground = GroundCatalog.Get(key);

        foreach (var wcid in new uint[] { 377, 379, 688 }) // Potion of Healing, Mana Potion, Silver Scarab
        {
            Assert.Equal(Cave.Supplies.StockOf(wcid), ground.Supplies.StockOf(wcid));
        }

        var stock = ground.Supplies.Stock.Select(s => s.Wcid).ToList();
        Assert.True(stock.IndexOf(377) < stock.IndexOf(379), $"{key}: the Potion of Healing is listed before the Mana Potion, so a short trip budget fills it first");
        Assert.Equal(new StockLine(377, "Potion of Healing", 30, 6, 0), ground.Supplies.StockOf(377));
        Assert.Equal(new StockLine(379, "Mana Potion", 100, 10, 0), ground.Supplies.StockOf(379));
        Assert.Equal(new StockLine(688, "Silver Scarab", 20, 5, 2), ground.Supplies.StockOf(688));
        Assert.Equal(230, ground.PriceOf(377));
        Assert.Equal(338, ground.PriceOf(688));
        Assert.Equal(66_000, ground.Supplies.Purse.Coin);
    }

    [Theory]
    [MemberData(nameof(AbodeAndHabitat))]
    public void The_tinctures_are_the_Caves_town_stock_and_the_Pyreal_Scarab_stays(string key)
    {
        var ground = GroundCatalog.Get(key);

        foreach (var wcid in new uint[] { 27319, 27322 }) // Health Tincture, Mana Tincture (Ludora the Healer, Dryreach)
        {
            var cave = Cave.TownStockOf(wcid)!;
            var here = ground.TownStockOf(wcid);
            Assert.NotNull(here);
            Assert.Equal((cave.Name, cave.Keep, cave.TripUnder, cave.Price, cave.Burden, cave.Town, cave.Vendor.Name, cave.Vendor.Wcid, cave.Vendor.Guid), (here.Name, here.Keep, here.TripUnder, here.Price, here.Burden, here.Town, here.Vendor.Name, here.Vendor.Wcid, here.Vendor.Guid));
        }

        Assert.Equal((100, 40, 1550, "Uziz"), (ground.TownStockOf(690)!.Keep, ground.TownStockOf(690)!.TripUnder, ground.TownStockOf(690)!.Price, ground.TownStockOf(690)!.Town)); // the Pyreal Scarab: level VI's
        Assert.Equal(Cave.TownStock.Select(t => t.Wcid).Order(), ground.TownStock.Select(t => t.Wcid).Order());
    }

    [Theory]
    [MemberData(nameof(AbodeAndHabitat))]
    public void Level_VIs_formulas_are_the_Quarters_rows_and_their_tapers_are_stock_lines(string key)
    {
        var ground = GroundCatalog.Get(key);
        var quarters = GroundCatalog.Get("tusker-quarters");

        Assert.Equal(quarters.Components.Where(c => c.SpellLevel == 6).OrderBy(c => c.Wcid), ground.Components.Where(c => c.SpellLevel == 6).OrderBy(c => c.Wcid));
        foreach (var c in ground.Components.Where(c => c.SpellLevel == 6))
        {
            Assert.True(ground.Supplies.StockOf(c.Wcid) is not null || ground.TownStockOf(c.Wcid) is not null, $"{key}: {c.Name} {c.Wcid} is bought");
        }

        Assert.Equal(new StockLine(1653, "Yellow Taper", 2, 2, 2), ground.Supplies.StockOf(1653));
        Assert.Equal(new StockLine(1651, "Violet Taper", 2, 2, 2), ground.Supplies.StockOf(1651));
        Assert.DoesNotContain(ground.SpellPlan, r => r.SpellLevel == 6); // the plan is the base skill's projection; VI is cast by the ladder's live cap
    }

    [Theory]
    [MemberData(nameof(AbodeAndHabitat))]
    public void The_plan_has_a_row_from_25_and_covers_the_Abode_rungs_band(string key)
    {
        var ground = GroundCatalog.Get(key);
        var rung = LadderLoader.WarMage.Rungs.Single(r => r.Key == "abode");

        Assert.Equal(new SpellPlanRow(25, 41, "Flame Bolt", 4), ground.PlanFor(25)); // M6 14 (D135): the Abode from 25
        Assert.Equal(5, ground.PlanFor(42)!.SpellLevel);
        for (var level = rung.Band.From; level <= rung.Band.To; level++)
        {
            Assert.NotNull(ground.PlanFor(level));
        }
    }

    [RequiresDatsFact]
    public void Each_plan_row_of_the_Cave_the_Abode_and_the_Habitat_is_the_computation_at_every_level_of_the_band()
    {
        var table = XpTableData.Load(fixture.Dats);
        var ladder = LadderLoader.WarMage;
        foreach (var (key, rungKey) in new[] { ("tusker-cave", "cave"), ("tusker-abode", "abode"), ("tusker-habitat", "abode") })
        {
            var ground = GroundCatalog.Get(key);
            var rung = ladder.Rungs.Single(r => r.Key == rungKey);
            var carried = ground.Components.Select(c => c.SpellLevel).ToHashSet();
            for (var level = rung.Band.From; level <= rung.Band.To; level++)
            {
                var skill = SpellPlan.ProjectedWarBase(table, level);
                var want = SpellPlan.LevelAt(skill, ladder.PlanCastChance);
                want = carried.Contains(want) ? want : carried.Where(l => l > want).Min(); // M6 14 (D135): a level the ground's kit does not carry (the Abode's III at 25 and 26) is the lowest it carries above it; the ladder's fight casts by the live cap (D129)
                var row = ground.PlanFor(level);
                Assert.True(row?.SpellLevel == want, string.Create(CultureInfo.InvariantCulture, $"{key} level {level}: base {skill} casts {want} at {ladder.PlanCastChance}, the row says {row?.SpellLevel.ToString(CultureInfo.InvariantCulture) ?? "nothing"}"));
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key}: {string.Join(", ", ground.SpellPlan.Select(r => $"{r.MinLevel}-{r.MaxLevel} {r.SpellLevel}"))}"));
        }
    }
}
