using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 Q10d: the load the wear pick reads as kept after a sale (<c>ResupplyRun.KeptBurden</c>) asks the sale verdict, where it asked the table's middle part alone. The load is only ever the wear pick's load stop's input:
/// what the sale sells, drops, gives or takes off is not decided by it.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private uint OwnWithBurden(Home home, string name, uint wcid, uint itemType, uint value, ushort burden)
    {
        var guid = home.Vendor.NextGuid();
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        var header = WeenieHeaderFlags.Container | WeenieHeaderFlags.Value | WeenieHeaderFlags.Burden;
        home.Vendor.H.R.Reducer.OnCreateObject(
            home.Vendor.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ContainerId = VendorRig.Player, Value = value, Burden = burden } },
            isUpdate: false);
        home.Vendor.Put(guid);
        return guid;
    }

    [Fact]
    public void M6_16_Q10d_the_kept_load_counts_a_noted_keeper_and_a_refused_class_as_kept_where_the_table_alone_sold_them()
    {
        var floor = new FloorHome(coins: 3_000);
        var home = floor.Home;
        OwnWithBurden(home, "Black Garnet", 2394, 0x800, 373, 10); // loot: sold by both reads
        var noted = OwnWithBurden(home, "Black Garnet", 2394, 0x800, 373, 20); // the loot step noted it kept aside: the sale never offers it
        OwnWithBurden(home, "Bread", 259, MiscType, 5, 30); // a class ACE refuses to buy whatever the mask: the sale never offers it
        OwnWithBurden(home, "Oil of Rendering", 12711, MiscType, 0, 40); // value 0: dropped by both reads
        OwnWithBurden(home, "Health Tincture", HealthDrinks.TinctureWcid, MiscType, 6_000, 50); // a drink in use: kept by both
        OwnWithBurden(home, "Shirt", 130, ClothingType, 100, 60); // held gear: the wear pick weighs it itself, shed by both
        home.Memory.KeptAside.Add(noted);
        var run = home.Start(new ResupplyOptions(true, () => [], Memory: home.Memory));

        home.Stations.Run(run, 1, pump: home.Shops.Pump); // the first step binds the run to the session; nothing is sold or walked yet
        var kept = run.KeptBurden(home.Ground, 1_000);

        // the sale sells the garnet (10) and drops the oil (40) and counts the shirt (60) as the pick's own: 1,000 - 110 = 890, and the noted garnet (20) and the bread (30) stay in the pack
        Assert.Equal(890, kept);
        Assert.Equal(1_000 - 10 - 40 - 60, kept);
        Assert.Null(run.KeptBurden(home.Ground, 0)); // no load read: unread, as before
    }

    [Fact]
    public void M6_16_Q10d_a_worn_piece_the_sale_keeps_stays_in_the_pack_when_taken_off_and_a_plain_one_does_not()
    {
        var floor = new FloorHome(coins: 3_000);
        var home = floor.Home;
        var plain = home.Own("Pathwarden Boots", 33607, ClothingType, 0);
        home.Memory.KeptAside.Add(floor.Robe); // the robe is noted kept aside: the sale would never offer it once it is off
        var run = home.Start(new ResupplyOptions(true, () => [], Memory: home.Memory));
        home.Stations.Run(run, 1, pump: home.Shops.Pump);

        var stays = run.StaysInPackWhenOff(home.Ground);

        Assert.True(stays(new WornPiece(floor.Robe, "Pathwarden Robe", ClothingType, 0x7F00, 0, null)));
        Assert.False(stays(new WornPiece(plain, "Pathwarden Boots", ClothingType, 0x100, 0, null))); // no keep before the table: the sale would sell it, as the burden term has always read
        Assert.False(stays(new WornPiece(0xDEAD0001, "Not held", ClothingType, 0x100, 0, null))); // an unknown piece is read as before: sold
    }
}
