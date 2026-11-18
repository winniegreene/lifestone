using System.Numerics;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too): the shop step of a stop in another town (<see cref="ResupplyOptions.TownShop"/>) on the resupply's own rig: the player at the Town Network's drop in
/// Dryreach, Ludora the Healer at her profile place with the two tinctures on her list. Both lines are bought by the home walk's own steps and each count is checked after; a vendor who is not there fails the step by name.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const string Cave = "tusker-cave";

    /// <summary>The Cave's home rig moved to Dryreach: the player at the drop of network-to-dryreach, Ludora placed (or not) with her two tinctures at the profile's price.</summary>
    private static Home InDryreach(int coins, bool ludora = true)
    {
        var home = new Home(Cave, coins);
        var seller = home.Ground.TownStock[0].Vendor;
        home.Vendor.StandAt(0xDA75002B, new Vector3(132f, 60f, 18f));
        if (ludora)
        {
            home.Vendor.PlaceAt(seller.Guid, seller.Name, seller.Cell, seller.Point, 3f, seller.Wcid);
        }

        var shop = home.Shops.Add(seller.Guid, 0);
        var row = 0x80006F00u;
        foreach (var line in home.Ground.TownStock)
        {
            shop.Rows.Add((-1, ++row, line.Name, line.Wcid, MiscType, line.Price));
        }

        return home;
    }

    [Fact]
    public void The_stop_in_Dryreach_buys_the_Health_and_the_Mana_Tinctures_at_Ludora_and_checks_each_count_after()
    {
        var home = InDryreach(coins: 50_000);
        var lines = home.Ground.TownStock;
        var run = home.Start(new ResupplyOptions(TownShop: [new TownStockOrder(lines[0], 12), new TownStockOrder(lines[1], 30)]));

        var status = home.Run(run);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + string.Join(" | ", home.Lines.TakeLast(12)));
        Assert.Equal((42, 42L, 32_550L, 1), (run.TownBought, run.Bought, run.Spent, run.Vendors));
        Assert.True(home.Logged("walking: purpose=vendor:Ludora the Healer (the stop in Dryreach: 12 x Health Tincture, 30 x Mana Tincture; the coin 50000)"));
        Assert.True(home.Logged("town stock: check after: Health Tincture 0 -> 12 held at Ludora the Healer in Dryreach (12 asked, 9300 pyreals paid, the coin 40700)"));
        Assert.True(home.Logged("town stock: check after: Mana Tincture 0 -> 30 held at Ludora the Healer in Dryreach (30 asked, 23250 pyreals paid, the coin 17450)"));
    }

    /// <summary>M6 item 14 (m6-goldscarab): the Cave's home rig moved to Uziz: the player at the drop of network-to-uziz, Archmage Sammam al-Ashiz in his room with the Gold and the Pyreal Scarab at the profile's prices.</summary>
    private static Home InUziz(int coins)
    {
        var home = new Home(Cave, coins);
        var lines = home.Ground.TownStock.Where(l => l.Town == "Uziz").ToList();
        var seller = lines[0].Vendor;
        var drop = Lifestone.Client.Session.Travel.TravelRoutes.Default.Edge("network-to-uziz");
        home.Vendor.StandAt(drop.DestCell, drop.DestOrigin);
        home.Vendor.PlaceAt(seller.Guid, seller.Name, seller.Cell, seller.Point, 3f, seller.Wcid);
        var shop = home.Shops.Add(seller.Guid, 0);
        var row = 0x80006F80u;
        foreach (var line in lines)
        {
            shop.Rows.Add((-1, ++row, line.Name, line.Wcid, MiscType, line.Price));
        }

        return home;
    }

    [Fact]
    public void The_stop_in_Uziz_buys_the_Gold_Scarabs_at_Sammam_and_checks_the_count_after_M6_14_goldscarab()
    {
        var home = InUziz(coins: 200_000);
        var gold = home.Ground.TownStock.Single(l => l.Wcid == 687);
        var run = home.Start(new ResupplyOptions(TownShop: [new TownStockOrder(gold, 100)]));

        var status = home.Run(run);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + string.Join(" | ", home.Lines.TakeLast(12)));
        Assert.Equal((100, 100L, 77_500L, 1), (run.TownBought, run.Bought, run.Spent, run.Vendors));
        Assert.True(home.Logged("walking: purpose=vendor:Archmage Sammam al-Ashiz (the stop in Uziz: 100 x Gold Scarab; the coin 200000)"));
        Assert.True(home.Logged("town stock: check after: Gold Scarab 0 -> 100 held at Archmage Sammam al-Ashiz in Uziz (100 asked, 77500 pyreals paid, the coin 122500)"));
        Assert.Equal(100, GearInventory.CountOf(home.Vendor.H.State, [], 687));
    }

    [Fact]
    public void A_vendor_who_is_not_met_fails_the_town_stop_by_name_and_nothing_is_bought()
    {
        var home = InDryreach(coins: 50_000, ludora: false);
        var lines = home.Ground.TownStock;
        var run = home.Start(new ResupplyOptions(TownShop: [new TownStockOrder(lines[0], 12)]));

        var status = home.Run(run, frames: 4_000);

        Assert.NotEqual(ScenarioStatus.Succeeded, status);
        Assert.Equal(0, run.TownBought);
        Assert.Contains("Ludora the Healer 0x7DA75026 was not met in reach", run.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_town_stop_asked_for_away_from_the_vendors_town_fails_by_name_before_any_walk()
    {
        var home = new Home(Cave, 50_000); // at Holtburg's lifestone
        var run = home.Start(new ResupplyOptions(TownShop: [new TownStockOrder(home.Ground.TownStock[0], 12)]));

        Assert.NotEqual(ScenarioStatus.Succeeded, home.Run(run));
        Assert.Contains("town stock: not in Dryreach", run.Failure, StringComparison.Ordinal);
        Assert.Empty(home.Travel.Requests);
    }
}
