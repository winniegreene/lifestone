using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// Group 5 of the hunt rows over the scripted rig (M4 4.1 fix2c): the sale at Renald's, the purchase at Thelnoth's and the appraisal of the loot at the loop's end (M4 3.2 s4a), over the rig's scripted vendors
/// (<c>HuntScriptRig.Shops.cs</c>: the open, the buy, the sale and the Identify in the live capture's orders). The hunter holds one Handy Healing Kit, 10,000 pyreals and a pair of Leather Bracers, as the stand-in announced it;
/// the corpse holds the Bowl (Misc, value 10) and the Smock (Clothing, value 25).
/// </summary>
public sealed partial class HuntScenarioTests
{
    private const uint BracersWcid = HuntScriptRig.BracersWcid;
    private const uint HandyKitWcid = FighterKit.HealingKitWcid;
    private const uint PyrealWcid = HuntScriptRig.PyrealWcid;

    private static int SendsOf(HuntScriptRig rig, uint type) => rig.Sends.Count(s => s.Type == type);

    /// <summary>The hunt over a rig with the scripted shops (<paramref name="shops"/> false: no vendor is ever in the world) and one Handy Healing Kit in the pack.</summary>
    private static HuntScriptRig ShopRig(Func<RecordingLogSink, HuntScenario> make, bool shops = true)
    {
        var rig = Belt12(make);
        rig.Shops = shops;
        rig.HoldKits(1);
        return rig;
    }

    [Fact]
    public void M4_3_2_s4a_the_loop_sells_what_Renalds_mask_takes_after_the_return_keeps_its_own_kit_and_records_the_sale()
    {
        using var rig = ShopRig(Hunt(sell: true));
        var scenario = rig.Scenario;

        var status = rig.Run(900);
        var coinsBefore = HuntScriptRig.StartPyreals;

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Null(scenario.Failure);
        Assert.Equal(3, scenario.Sold); // the Leather Bracers the player holds and the corpse's Bowl and Smock
        Assert.Equal("ok", scenario.SellOutcome);
        Assert.Equal(rig.Coins - coinsBefore, scenario.Pyreals);
        Assert.Equal(990 + 9 + 22, scenario.Pyreals); // floor(0.9 x Value + 0.1) per stack: 1,100, 10 and 25
        Assert.Equal(coinsBefore + scenario.Pyreals, rig.Session.State.Self.Qualities.CoinValue); // the client's CoinValue followed the script's 02CD
        Assert.False(Holds(rig, BracersWcid) || Holds(rig, HuntScriptRig.BowlWcid) || Holds(rig, HuntScriptRig.SmockWcid), "what Renald takes is gone from the pack");
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace, "the Mace stays wielded");
        Assert.True(Holds(rig, FighterKit.ShortbowWcid) && Holds(rig, FighterKit.ArrowWcid) && Holds(rig, HandyKitWcid), "the template's weapons, the arrows and the kit are never sold");
        Assert.True(Holds(rig, PyrealWcid));
        Assert.Equal(1, SendsOf(rig, GameActionTypes.SellItems)); // one batch: the capture shows one sale is one UseDone either way
        Assert.Equal(0, rig.BusyRefusals);
        Assert.True(scenario.Returned);
        Assert.False(scenario.VendorWindow);
        Assert.Equal(0u, scenario.SaleVendor);
        Assert.Equal(0xA9B4u, rig.Session.State.Self.Position!.Value.CellId >> 16);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the verdict fields did not change shape
        Assert.Contains("sold=3 bought=0 pyreals=1021 appraised=2 sell=ok buy=off buy_pyreals=0", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.False(rig.Logged(", WARN "), "the sale's payout matched the prediction: " + rig.Tail());

        // the order: home first, then the shop, the open, the sale, the way home again
        var home = rig.LineAt("wind-down: back in Holtburg");
        var go = rig.LineAt("vendors: going to Shopkeeper Renald the Elder");
        var open = rig.LineAt("vendors: Renald the Elder is open");
        var done = rig.LineAt("sell: done sold=3 of 3 pyreals=1021");
        var back = rig.LineAt("vendors: done; back to Holtburg");
        Assert.True(home >= 0 && home < go && go < open && open < done && done < back, $"{home} {go} {open} {done} {back}" + rig.Tail());
        Assert.True(rig.LineAt("sell: rule types 0x480467A7 (the vendor's 0062)") >= 0, "the mask is the 0062's, not the shard's");
        Assert.Equal(1, rig.LineCount("sell: rule "));
        Assert.True(rig.LineAt("vendors: going to Shopkeeper Renald the Elder (shop) +1.5 m") >= 0, "3.2-R14: the hunter is placed 1.5 m beside the vendor, not on his spot");
        Assert.Equal(2, rig.Approaches.Count(a => a.Vendor == HuntScriptRig.RenaldGuid)); // the open's 0062 and the sale's refreshed one (after the UseDone, capture C2)
        Assert.Equal(0x480467A7u, rig.Approaches[0].MerchandiseItemTypes);
    }

    [Fact]
    public void M4_3_2_s4a_a_missile_hunt_sells_its_loot_but_never_the_bow_or_the_arrows_the_restore_put_in_the_pack()
    {
        using var rig = ShopRig(Hunt(HuntWeapon.Missile, sell: true));
        var scenario = rig.Scenario;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.True(scenario.Restored);
        Assert.Equal(3, scenario.Sold);
        Assert.True(rig.Held(FighterKit.ShortbowWcid, out var bow) && !bow, "the Shortbow is in the pack and stays there");
        Assert.True(Holds(rig, FighterKit.ArrowWcid));
        Assert.True(rig.Held(FighterKit.MaceWcid, out var mace) && mace);
    }

    [Fact]
    public void M4_3_2_s4a_sell_off_keeps_the_loot_and_visits_no_one()
    {
        using var rig = ShopRig(Hunt(sell: false));
        var scenario = rig.Scenario;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(0, scenario.Sold);
        Assert.Equal("off", scenario.SellOutcome);
        Assert.True(Holds(rig, HuntScriptRig.BowlWcid) && Holds(rig, HuntScriptRig.SmockWcid) && Holds(rig, BracersWcid), "nothing was sold");
        Assert.Equal(0, SendsOf(rig, GameActionTypes.SellItems));
        Assert.Equal(0, rig.LineCount("vendors: "));
        Assert.Contains(" sold=0 bought=0 pyreals=0 appraised=2 sell=off buy=off", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_3_2_s4a_with_fewer_kits_than_buy_kits_the_loop_buys_the_missing_ones_at_Thelnoth_at_the_predicted_price()
    {
        using var rig = ShopRig(Hunt(sell: false, buyKits: 3)); // the player holds one Handy Healing Kit: two are missing
        var scenario = rig.Scenario;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(2, scenario.Bought);
        Assert.Equal("ok", scenario.BuyOutcome);
        Assert.Equal(3, rig.Guids(HandyKitWcid).Count);
        Assert.Equal(HuntScriptRig.StartPyreals - (2 * 14), rig.Coins); // ceil(1.35 x 10 - 0.1) = 14 a kit, each its own stack
        Assert.Equal(HuntScriptRig.StartPyreals - (2 * 14), rig.Session.State.Self.Qualities.CoinValue);
        Assert.Contains("bought=2 pyreals=0", scenario.DetailFields(), StringComparison.Ordinal); // the sale's delta only; the price is buy_pyreals
        Assert.Contains("buy=ok buy_pyreals=28", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(1, SendsOf(rig, GameActionTypes.BuyItems));
        Assert.Equal(0, SendsOf(rig, GameActionTypes.SellItems));
        Assert.True(rig.LineAt("vendors: going to Thelnoth Cort the Healer") > rig.LineAt("wind-down: back in Holtburg"));
        Assert.True(rig.LineAt("buy: done bought=2 of 2 cost=28") >= 0, rig.Tail());
        Assert.False(rig.Logged(", WARN "), "the purchase's price matched the prediction and both kits arrived: " + rig.Tail());
        Assert.Equal(2, rig.Approaches.Count(a => a.Vendor == HuntScriptRig.ThelnothGuid)); // the open's 0062 and the buy's refreshed one
        Assert.True(scenario.Returned);
    }

    [Fact]
    public void M4_3_2_s4a_the_sale_comes_first_and_the_purchase_follows_in_the_same_trip_each_vendor_has_its_own_window()
    {
        using var rig = ShopRig(Hunt(sell: true, buyKits: 3));
        var scenario = rig.Scenario;
        var sampler = new VendorFrameSampler();
        var windows = 0;
        var previous = false;
        var sawSaleVendor = false;
        var status = ScenarioStatus.Running;
        for (var t = 0.0; t < 900 && status == ScenarioStatus.Running; t += 0.1)
        {
            status = rig.Run(0.1);
            sampler.Track(scenario.VendorWindow);
            sawSaleVendor |= scenario.SaleVendor != 0 && scenario.VendorWindow;
            windows += scenario.VendorWindow && !previous ? 1 : 0;
            previous = scenario.VendorWindow;
        }

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(3, scenario.Sold);
        Assert.Equal(2, scenario.Bought);
        Assert.Equal(2, windows);
        Assert.Equal(2, sampler.Windows);
        Assert.True(sawSaleVendor, "SaleVendor names Renald while his window is open");
        Assert.False(scenario.VendorWindow);
        Assert.True(rig.LineAt("sell: done") < rig.LineAt("vendors: going to Thelnoth Cort the Healer"), "Renald first, Thelnoth after the sale's coins");
        Assert.Equal(1, rig.LineCount("vendors: done; back to Holtburg"));
    }

    [Fact]
    public void M4_3_2_s4a_buy_kits_0_is_off_and_enough_kits_held_means_no_trip_to_Thelnoth()
    {
        using var off = ShopRig(Hunt(sell: false, buyKits: 0));
        var zero = off.Scenario;
        Assert.True(off.Run(900) == ScenarioStatus.Succeeded, zero.Failure);
        Assert.Equal(0, zero.Bought);
        Assert.Equal("off", zero.BuyOutcome);
        Assert.Equal(0, SendsOf(off, GameActionTypes.BuyItems));
        Assert.Equal(0, off.LineCount("vendors: "));

        using var enough = ShopRig(Hunt(sell: false, buyKits: 1)); // the one kit the hunter holds is enough
        var held = enough.Scenario;
        Assert.True(enough.Run(900) == ScenarioStatus.Succeeded, held.Failure);
        Assert.Equal(0, held.Bought);
        Assert.Equal("enough", held.BuyOutcome);
        Assert.Equal(0, SendsOf(enough, GameActionTypes.BuyItems));
        Assert.Equal(0, enough.LineCount("vendors: "));
        Assert.True(enough.LineAt("buy: 1 Handy Healing Kit(s) held, --buy-kits 1; no trip to Thelnoth") >= 0);
    }

    [Fact]
    public void M4_3_2_s4a_appraise_loot_identifies_each_looted_item_once_and_off_identifies_nothing()
    {
        using var on = ShopRig(Hunt());
        var scenario = on.Scenario;
        Assert.True(on.Run(900) == ScenarioStatus.Succeeded, scenario.Failure + on.Tail());
        Assert.Equal(2, scenario.Looted);
        Assert.Equal(2, scenario.Appraised); // the Bowl and the Smock, not the pack's older items
        Assert.Equal(2, SendsOf(on, GameActionTypes.IdentifyObject));
        Assert.Equal(2, on.LineCount("appraise: identifying the looted "));
        Assert.Contains("appraised=2", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Equal(2, scenario.Tally.Appraised);
        Assert.Equal(2, on.Identified.Distinct().Count());

        using var off = ShopRig(Hunt(appraise: false));
        var quiet = off.Scenario;
        Assert.True(off.Run(900) == ScenarioStatus.Succeeded, quiet.Failure);
        Assert.Equal(2, quiet.Looted);
        Assert.Equal(0, quiet.Appraised);
        Assert.Equal(0, SendsOf(off, GameActionTypes.IdentifyObject));
        Assert.Contains("appraised=0", quiet.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_3_2_s4a_a_shop_that_is_not_in_view_is_reported_and_the_hunt_still_returns_and_succeeds()
    {
        using var rig = ShopRig(Hunt(sell: true, buyKits: 2), shops: false); // no vendor is ever in the world
        var scenario = rig.Scenario;

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Null(scenario.Failure);
        Assert.True(scenario.Returned);
        Assert.Equal(0, scenario.Sold);
        Assert.StartsWith("failed:", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.Contains("is not in view", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.StartsWith("failed:", scenario.BuyOutcome, StringComparison.Ordinal);
        Assert.Contains("sell=failed:", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(" ", scenario.DetailFields().Split("sell=")[1].Split(' ')[0], StringComparison.Ordinal);
        Assert.True(rig.LineAt("a failed shop visit never fails the hunt") >= 0);
        Assert.True(rig.LineAt("vendors: done; back to Holtburg") > 0, "the way home and the log off are still reached");
        Assert.Equal(0, SendsOf(rig, GameActionTypes.SellItems));
    }

    [Fact]
    public void M4_3_2_s4a_a_sale_the_server_refuses_is_reported_as_failed_and_the_loop_goes_on_to_its_log_off()
    {
        using var rig = ShopRig(Hunt(sell: true));
        var scenario = rig.Scenario;
        var busied = false;

        // the runner steps the hunt to Renald's open; from there the test acts between the tick and the step: as soon as Renald's stock is in, a Use of Thelnoth goes straight to the wire (not through the action lanes),
        // so the hunter is busy when the sale arrives and the script answers 00A0 then UseDone 0x001D
        Assert.True(rig.RunUntil(() => scenario.Stage == "vendor-open"), "the hunt reached Renald's open: " + scenario.Failure + rig.Tail());
        for (var t = 0.0; t < 30 && !busied; t += 0.05)
        {
            Assert.True(rig.RunHooked(() =>
            {
                if (!busied && rig.Session.State.Vendor is { } stock && stock.Vendor == scenario.SaleVendor)
                {
                    busied = true;
                    rig.Server.SendUse(HuntScriptRig.ThelnothGuid);
                }
            }) == ScenarioStatus.Running);
        }

        Assert.True(busied, "the hook ran");
        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Null(scenario.Failure);
        Assert.Equal(0, scenario.Sold);
        Assert.StartsWith("failed:", scenario.SellOutcome, StringComparison.Ordinal);
        Assert.True(scenario.Returned);
        Assert.True(rig.BusyRefusals >= 1, "the script refused the sale as busy");
        Assert.True(Holds(rig, HuntScriptRig.BowlWcid), "nothing was sold");
        Assert.True(rig.LineAt("sell: nothing was sold (failed:") >= 0, rig.Tail());
        Assert.True(rig.LineAt("vendors: done; back to Holtburg") > 0);
    }

    [Fact]
    public void M4_3_2_s4a_a_purchase_the_server_refuses_for_want_of_pyreals_is_reported_and_the_hunt_succeeds()
    {
        using var rig = ShopRig(Hunt(sell: false, buyKits: 3));
        var scenario = rig.Scenario;
        rig.SetCoins(0); // the server's purse is empty; the client's model still shows 10,000 (SetCoins sends nothing)

        var status = rig.Run(900);

        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Null(scenario.Failure);
        Assert.Equal(0, scenario.Bought);
        Assert.StartsWith("failed:", scenario.BuyOutcome, StringComparison.Ordinal);
        Assert.Single(rig.Guids(HandyKitWcid));
        Assert.True(scenario.Returned);
        Assert.True(rig.LineAt("buy: failed:") >= 0, rig.Tail());
    }

    [Fact]
    public void M4_3_2_s4a_nothing_the_shop_takes_means_no_trip_and_a_failed_hunt_sells_nothing()
    {
        using var rig = ShopRig(Hunt(sell: true, loot: new LootOptions { Enabled = false }));
        var scenario = rig.Scenario;
        // with loot off the pack holds only the hunter's Leather Bracers to sell: one item
        Assert.True(rig.Run(900) == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Sold);

        using var failed = HuntScriptRig.Create(Hunt(sell: true)); // no creature at all near: the hunt fails loudly, still returns
        failed.Shops = true;
        var hunt = failed.Scenario;
        Assert.True(failed.Run(900) == ScenarioStatus.Failed);
        Assert.True(hunt.Returned);
        Assert.Equal(0, hunt.Sold);
        Assert.StartsWith("skipped:", hunt.SellOutcome, StringComparison.Ordinal);
        Assert.Equal(0, SendsOf(failed, GameActionTypes.SellItems));
    }
}
