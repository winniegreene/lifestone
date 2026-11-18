using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (<c>m6-sale-keeps-drinks</c>; the Cave's run of 2026-11-12: a restarted run begins on the ladder's first ground, whose stock lists no tincture, and its sale sold the tinctures bought for the Cave): the ladder's
/// character never sells a drink the vitals rules drink, whatever the ground's stock lists, and the rest of the loot is sold as before.
/// </summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void M6_14_the_sale_never_offers_a_drink_she_uses_whatever_the_grounds_stock_lists_and_sells_the_rest()
    {
        var f = new FloorHome(coins: 3_000, notes: false);
        PlaceSmith(f.Home);
        f.OwnSpareWand();
        var health = f.Home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 500, stack: 12, maxStack: 100);
        var mana = f.Home.Own("Mana Tincture", ManaDrinks.TinctureWcid, MiscType, 500, stack: 30, maxStack: 100);
        var potion = f.Home.Own("Potion of Healing", FighterKit.PotionWcid, MiscType, 150);
        var loot = f.Home.Own("Tusker Tusk", 99_001, MiscType, 300); // the control: loot, sold
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.True(f.Inventory.Contains(health) && f.Inventory.Contains(mana) && f.Inventory.Contains(potion), $"{f.Inventory.Contains(health)} {f.Inventory.Contains(mana)} {f.Inventory.Contains(potion)} " + string.Join(" | ", f.Home.Lines.Where(l => l.Contains("sale", StringComparison.Ordinal) || l.Contains("sold", StringComparison.Ordinal)).Select(l => l.Length > 200 ? l[..200] : l)));
        Assert.False(f.Inventory.Contains(loot));
        Assert.Equal(1, run.Sold);
        Assert.Single(f.Home.Lines, l => l.Contains("is a drink she uses (health, mana or stamina): kept, not offered", StringComparison.Ordinal) && l.Contains("m6-sale-keeps-drinks", StringComparison.Ordinal));
        Assert.Null(run.Failure);
    }

    [Theory]
    [InlineData(27319u, true)] // Health Tincture
    [InlineData(27322u, true)] // Mana Tincture
    [InlineData(377u, true)] // Potion of Healing
    [InlineData(379u, true)] // Mana Potion
    [InlineData(2457u, true)] // Health Draught
    [InlineData(27326u, false)] // Stamina Tincture: she drinks none (the owner, 2026-11-11)
    [InlineData(99_001u, false)]
    public void M6_14_a_drink_in_use_is_one_the_vitals_rules_drink(uint wcid, bool inUse) => Assert.Equal(inUse, ResupplyRun.DrinkInUse(wcid));

    [Fact]
    public void M6_14_the_stamina_potion_is_a_drink_in_use() => Assert.True(ResupplyRun.DrinkInUse(FighterKit.StaminaPotionWcid));
}
