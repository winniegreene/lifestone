using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>M5 7 D121: the sale reads the main pack only: a side pack's items (the kit's gear, the review finds) are never offered, whatever they are worth.</summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void The_sale_offers_the_main_packs_loot_only_and_never_a_side_packs_item()
    {
        var home = new Home(Den, 1000);
        var shirt = home.Own("Shirt", 130, ClothingType, 100);
        var pack = home.Vendor.NextGuid();
        home.Vendor.Arrive(pack, "Pack", 136, 0x200, 65);
        home.Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 9_001, pack, VendorRig.Player, 0, 1)); // a side pack (ContainerType 1)
        var robe = home.Vendor.NextGuid();
        home.Vendor.Arrive(robe, "Spare Robe", 9_998, ClothingType, 500);
        home.Vendor.Put(robe, pack);
        var run = home.Start(new ResupplyOptions(true, () => [shirt, robe]));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run));

        Assert.True(home.Logged("(0 lines to buy, 1 items to sell)"));
        Assert.Equal(1, run.Sold);
        Assert.False(home.Vendor.State.Self.Inventory.Contains(shirt));
        Assert.True(home.Vendor.State.Self.Inventory.Contains(robe)); // in the side pack: not loot to sell
    }
}
