using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AR (the audit's rows 2, 3, 12, 15, 19, 28, 32): her money and supplies at level 16 are data: the loot rule keeps what pays by burden, and the breastplate she left now fits; the outskirts name every creature's health; the scarab lines of the III and IV buffs trip at none; one potion calls no trip; the jeweller buys what no shop would.
/// </summary>
public sealed class MoneyAndSuppliesTests
{
    private static HuntingGround Outskirts => GroundCatalog.Get("holtburg-outskirts");

    [Theory]
    [InlineData("holtburg-outskirts", 10.0, 125, 60)]
    [InlineData("tusker-burrow", 10.0, 110, 25)]
    [InlineData("tusker-lodge", 10.0, 110, 25)]
    public void Her_grounds_carry_the_loot_rule_as_data(string key, double floor, int line, int radius)
    {
        var loot = GroundCatalog.Get(key).Loot!;

        Assert.Equal((floor, line, radius), (loot.MinValuePerBurden, loot.LoadLinePercent, loot.RadiusMetres));
    }

    [Theory]
    [InlineData(7596, 663, true)] // the Studded Leather Breastplate she left: 11.46 a burden unit, 6,836 at Renald's
    [InlineData(6843, 889, false)] // the Cuirass: 7.7: left
    [InlineData(1253, 15, true)] // a ring
    [InlineData(815, 416, false)] // a pike
    public void The_floor_keeps_what_pays_by_burden(int value, int burden, bool kept) =>
        Assert.Equal(kept, (double)value / burden >= Outskirts.Loot!.MinValuePerBurden);

    [Fact]
    public void At_94_percent_of_the_pack_the_breastplate_fits_under_the_load_line()
    {
        const int Capacity = 4800;
        const int Held = 4503; // the 15:19 loot line of stretch 4
        Assert.True(Held + 663 <= Capacity * Outskirts.Loot!.LoadLinePercent / 100);
        Assert.True(Held + 663 > Capacity); // the old line, 100%, left it
    }

    [Theory]
    [InlineData(19257, 42)]
    [InlineData(19258, 40)]
    [InlineData(19259, 25)]
    [InlineData(19260, 20)]
    [InlineData(19256, 43)]
    [InlineData(19261, 47)]
    [InlineData(19262, 47)]
    [InlineData(19263, 15)]
    [InlineData(2566, 5)]
    [InlineData(24937, 3)]
    public void Every_outskirts_creature_names_its_health_so_drain_and_the_finishing_bolt_fire_outdoors(uint wcid, int health)
    {
        Assert.Equal(health, Outskirts.Outdoor!.Creatures.Single(c => c.Wcid == wcid).MaxHealth);
        Assert.Equal(health, FightTargets.Of(Outskirts).Single(t => t.Wcid == wcid).MaxHealth);
    }

    [Theory]
    [InlineData("holtburg-outskirts")]
    [InlineData("tusker-burrow")]
    [InlineData("tusker-lodge")]
    public void Three_scarabs_or_more_where_a_III_or_IV_line_is_wanted_and_a_trip_at_none(string key)
    {
        var stock = GroundCatalog.Get(key).Supplies;
        foreach (var wcid in new uint[] { 686, 688 })
        {
            var line = stock.StockOf(wcid)!;
            Assert.True(line.Keep >= 3, line.Name);
            Assert.Equal(1, line.TripUnder);
        }
    }

    [Fact]
    public void Under_two_mana_potions_a_trip_is_due_on_the_outskirts_M6_8() => Assert.Equal(2, Outskirts.Supplies.StockOf(379)!.TripUnder); // it was 0 while the line kept one potion; M6 8 (the owner, 2026-11-11): she drinks instead of resting, home is a short walk, and with none held she rested at mana 0 (live 06:45 UTC)

    [Theory]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(21, 3)]
    public void The_outskirts_plan_reaches_her_level_and_the_components_of_III_are_stocked(int level, int spellLevel)
    {
        Assert.Equal(spellLevel, Outskirts.PlanFor(level)!.SpellLevel);
        Assert.All(Outskirts.Components.Where(c => c.SpellLevel == 3), c => Assert.NotNull(Outskirts.Supplies.StockOf(c.Wcid)));
    }

    [Theory]
    [InlineData("holtburg-outskirts")]
    [InlineData("tusker-burrow")]
    [InlineData("tusker-lodge")]
    public void Monyra_buys_the_jewellery_no_other_shop_of_the_ground_buys(string key)
    {
        var ground = GroundCatalog.Get(key);
        var den = GroundCatalog.Get("black-spawn-den-aluvian").Vendors.Single(v => v.Wcid == 716);
        var here = ground.Vendors.Single(v => v.Wcid == 716);
        Assert.Equal((den.Guid, den.Cell, den.StopCell, den.BuysItemTypes, den.BuyPrice), (here.Guid, here.Cell, here.StopCell, here.BuysItemTypes, here.BuyPrice));
        Assert.Empty(here.Sells);
    }
}
