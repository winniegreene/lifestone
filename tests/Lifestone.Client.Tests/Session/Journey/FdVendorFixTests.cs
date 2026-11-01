using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// Fix FD (M4 P5b, live gate round 2's one station fault): the vendor-facts station "sold" nothing at Renald because it opened the wrong vendor. Renald's shop holds two NPCs whose names contain "Renald" (the Elder, wcid 718, guid 0x7A9B4021, the
/// shopkeeper whose 0062 mask is 0x480467A7; the Younger, a scribe, wcid 717, guid 0x7A9B4023, 16 rows), and the station's name match took the nearer: the Younger (3.9 m against the Elder's farther spot). Over the scripted session with the
/// real reducer: the walk names the Elder by his full name; the Elder's real 0062 shape takes a granted Shirt's CreateObject and the Leather Bracers (the hypotheses the stock read and the item's flags were ruled out here); a failure line names
/// the clause that refused (mask, value, retained, unsellable-list) or the stock that was not open.
/// </summary>
public sealed class FdVendorFixTests
{
    private const uint Elder = 0x7A9B4021;
    private const uint Younger = 0x7A9B4023;
    private const uint Cell = 0x860201AD;
    private const uint RenaldMask = 0x480467A7;
    private const uint ShirtGuid = 0x80000A41;
    private const uint BracersGuid = 0x8000099E;

    /// <summary>A leg that only walks to Renald by the name it is given and reads who it found (the station's own GoNear, nothing else).</summary>
    private sealed class GoNearProbe(JourneyContext context, string nameContains) : ShopLeg("go-near-probe", context)
    {
        public uint Found { get; private set; }

        public uint FirstNameMatch { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            var cell = S.State.Self.Position is { } here ? here.CellId : 0u;
            foreach (var h in GoNear("Renald", nameContains, [cell], RenaldAt, 2.0f, HuntScenario.RenaldUseMetres)) { yield return h; }

            Found = NpcGuid;
            FirstNameMatch = NearestContaining("Renald", 20);
        }
    }

    private static void Npc(J3aStationRig rig, uint guid, string name, uint wcid, float x)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, 20), state: PhysicsState.Gravity, name: name);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>The server's CreateObject of an item the item verb made in the pack (the weenie header carries the Value and the container, as ACE writes it), then the pack entry.</summary>
    private static WorldObject PackItem(J3aStationRig rig, uint guid, string name, uint wcid, uint itemType, uint? value, uint descriptionFlags = 0)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        var header = WeenieHeaderFlags.Container | (value is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Value);
        rig.R.H.R.Reducer.OnCreateObject(
            rig.R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = header, ContainerId = WorldActionRig.Player, ItemType = itemType, WeenieClassId = wcid, Value = value, ObjectDescriptionFlags = descriptionFlags } },
            isUpdate: false);
        rig.R.H.R.Reducer.OnInventoryPutObjInContainer(rig.R.H.R.Context, new GameEventHeader(WorldActionRig.Player, 0, 0x0022), new InventoryPutObjInContainerEvent(guid, WorldActionRig.Player, 0, 0));
        Assert.True(rig.View.State.Objects.TryGet(guid, out var item));
        return item;
    }

    /// <summary>A 0062 through the real reducer: the shape of Renald the Elder's (mask 0x480467A7, value 0 to 1,000,000, he pays 0.9 and charges 1.35) unless the mask says otherwise.</summary>
    private static VendorStock Open(J3aStationRig rig, uint vendor, uint mask = RenaldMask)
    {
        rig.R.H.R.Reducer.OnApproachVendor(rig.R.H.R.Context, new GameEventHeader(WorldActionRig.Player, 0, 0x0062), new VendorApproachMsg(vendor, mask, 0, 1_000_000, true, 0.9f, 1.35f, 0, 0, string.Empty, []));
        return rig.View.State.Vendor!;
    }

    [Fact]
    public void The_Younger_stands_nearer_than_the_Elder_and_the_station_walks_to_the_Elder_by_his_full_name_FD()
    {
        var rig = new J3aStationRig();
        Npc(rig, Younger, "Scribe Renald the Younger", 717, 13.9f); // 3.9 m from the player at x 10: what the live log read ("the NPC 0x7A9B4023 at 3.9 m")
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f); // inside the 6.5 m range, farther than the scribe
        var probe = new GoNearProbe(rig.Context("Holtburg Brawler"), ShopLeg.RenaldName);

        var status = rig.Run(probe, 60);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(Younger, probe.FirstNameMatch); // the cause: "Renald" alone names both, the nearer is the scribe
        Assert.Equal(Elder, probe.Found); // the fix: the station's name is the shopkeeper's
        Assert.NotEmpty(rig.Travel.Requests);
        Assert.All(rig.Travel.Requests, r => Assert.Equal(Elder, r.ObjectGuid)); // the walk's object goal is him too, not the scribe
        Assert.Equal("Renald the Elder", ShopLeg.RenaldName);
        Assert.Equal("Thelnoth Cort", ShopLeg.ThelnothName);
    }

    [Fact]
    public void The_vendor_stations_walks_name_Renald_the_Elder_and_Thelnoth_Cort_in_full_never_a_first_name_FD()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Lifestone.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var shop = File.ReadAllText(Path.Combine(dir!, "src", "Lifestone.Client", "Session", "Scenarios", "Journey", "ShopStations.cs"));
        Assert.Contains("NearestContaining(RenaldName, 20)", shop, StringComparison.Ordinal); // M5 7 P7p: the walk to Renald is GoToRenaldStand's point goal, and it finds him by his full name
        Assert.DoesNotContain("GoNear(\"Renald\"", shop, StringComparison.Ordinal);
        Assert.Equal(2, shop.Split("GoNear(\"Thelnoth\", ThelnothName, ").Length - 1); // the appraise-vendor station and the vendor-facts station
        Assert.DoesNotContain("NearestContaining(who,", shop, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_first_name_match_would_have_opened_the_scribe_FD()
    {
        var rig = new J3aStationRig();
        Npc(rig, Younger, "Scribe Renald the Younger", 717, 13.9f);
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        var probe = new GoNearProbe(rig.Context("Holtburg Brawler"), "Renald"); // the live call before the fix

        rig.Run(probe, 60);

        Assert.Equal(Younger, probe.Found); // reproduced: the scribe is the NPC the station would open and sell to
    }

    [Fact]
    public void Renalds_real_0062_takes_a_granted_Shirt_and_the_Leather_Bracers_and_the_Elder_is_the_stock_it_wants_FD()
    {
        var rig = new J3aStationRig();
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        var shirt = PackItem(rig, ShirtGuid, "Shirt", 130, 0x4, 10); // the item verb's Shirt: Clothing, Value 10
        var bracers = PackItem(rig, BracersGuid, "Leather Bracers", 25637, 0x2, 30); // Armor
        var stock = Open(rig, Elder);

        Assert.True(rig.View.State.Self.Inventory.Contains(ShirtGuid));
        Assert.Equal(SaleRefusal.None, stock.Refusal(shirt));
        Assert.Equal(SaleRefusal.None, stock.Refusal(bracers));
        Assert.True(VendorFactsRun.SaleCandidate(shirt, stock.Buys));
        Assert.True(VendorFactsRun.SaleCandidate(bracers, stock.Buys));
        Assert.Null(VendorFactsRun.SaleBlock(ShirtGuid, rig.View.State.Self.Inventory.Contains(ShirtGuid), shirt, stock, Elder));
        Assert.Null(VendorFactsRun.SaleBlock(BracersGuid, rig.View.State.Self.Inventory.Contains(BracersGuid), bracers, stock, Elder));
        Assert.Equal(9u, stock.PayoutFor(shirt));
        Assert.Equal(27u, stock.PayoutFor(bracers));
    }

    [Fact]
    public void A_stock_that_takes_neither_refuses_both_by_the_mask_and_the_failure_names_the_vendor_whose_stock_was_open_FD()
    {
        var rig = new J3aStationRig();
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        Npc(rig, Younger, "Scribe Renald the Younger", 717, 13.9f);
        var shirt = PackItem(rig, ShirtGuid, "Shirt", 130, 0x4, 10);
        var bracers = PackItem(rig, BracersGuid, "Leather Bracers", 25637, 0x2, 30);
        var scribe = Open(rig, Younger, mask: 0x2000); // illustrative: a mask without Clothing (0x4) or Armor (0x2), as the live refusal of both showed

        Assert.Equal(SaleRefusal.Mask, scribe.Refusal(shirt));
        Assert.Equal(SaleRefusal.Mask, scribe.Refusal(bracers));
        Assert.False(VendorFactsRun.SaleCandidate(shirt, scribe.Buys));
        Assert.StartsWith("mask:", scribe.DescribeRefusal(shirt), StringComparison.Ordinal);

        // Asked as the station asks for Renald the Elder, the Younger's open stock is named as the wrong vendor.
        var wrongVendor = VendorFactsRun.SaleBlock(ShirtGuid, true, shirt, scribe, Elder);
        Assert.Contains("0x7A9B4023's, not 0x7A9B4021's", wrongVendor, StringComparison.Ordinal);
        // The same stock for the vendor it belongs to is refused by its mask, and the line says so with the numbers.
        var byMask = VendorFactsRun.SaleBlock(BracersGuid, true, bracers, scribe, Younger);
        Assert.StartsWith("mask:", byMask, StringComparison.Ordinal);
        Assert.Contains("ItemType 0x2", byMask, StringComparison.Ordinal);
        Assert.Contains("0x00002000", byMask, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_clause_that_refuses_a_sale_has_its_own_name_and_a_closed_stock_or_a_missing_item_is_told_apart_FD()
    {
        var rig = new J3aStationRig();
        var worthless = PackItem(rig, 0x80000A50, "Worthless Shirt", 130, 0x4, 0);
        var retained = PackItem(rig, 0x80000A51, "Retained Shirt", 130, 0x4, 10, descriptionFlags: VendorStock.RetainedFlag);
        var bread = PackItem(rig, 0x80000A52, "Bread", VendorStock.BreadWcid, 0x20, 5);
        var shirt = PackItem(rig, ShirtGuid, "Shirt", 130, 0x4, 10);
        var stock = Open(rig, Elder);

        Assert.Equal(SaleRefusal.Value, stock.Refusal(worthless));
        Assert.Equal(SaleRefusal.Retained, stock.Refusal(retained));
        Assert.Equal(SaleRefusal.UnsellableWeenie, stock.Refusal(bread));
        Assert.Equal(SaleRefusal.None, stock.Refusal(shirt));
        Assert.StartsWith("value:", stock.DescribeRefusal(worthless), StringComparison.Ordinal);
        Assert.StartsWith("retained:", stock.DescribeRefusal(retained), StringComparison.Ordinal);
        Assert.StartsWith("unsellable-list:", stock.DescribeRefusal(bread), StringComparison.Ordinal);
        Assert.Contains("weenie class 259", stock.DescribeRefusal(bread), StringComparison.Ordinal);
        Assert.Equal(stock.Buys(shirt), stock.Refusal(shirt) == SaleRefusal.None); // Buys is the clauses' conjunction, nothing else

        Assert.Equal("no item: the grant gave no guid", VendorFactsRun.SaleBlock(0, false, null, stock, Elder));
        Assert.Contains("is not in the pack", VendorFactsRun.SaleBlock(ShirtGuid, false, shirt, stock, Elder), StringComparison.Ordinal);
        Assert.Contains("is not in the object table", VendorFactsRun.SaleBlock(ShirtGuid, true, null, stock, Elder), StringComparison.Ordinal);
        Assert.Contains("his stock is not open", VendorFactsRun.SaleBlock(ShirtGuid, true, shirt, null, Elder), StringComparison.Ordinal); // the model closed the vendor (the close rule) or none ever opened
    }
}
