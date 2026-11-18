using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 pack rules on a whole trip (docs/pack-rules.md): a healing kit in the kit side pack is sold (the sale reads the side packs for the classes it sells there), the value-0 junk and the tool no Holtburg buyer takes are dropped at the
/// home after the sale with check, act, check, and every decision is logged once with its rule name.
/// </summary>
public sealed partial class ResupplyStationTests
{
    [Fact]
    public void M6_14_pack_rules_sell_a_side_pack_healing_kit_and_drop_the_junk_each_named_by_its_rule()
    {
        var home = new Home(Den, 1_000);
        var side = home.Vendor.NextGuid();
        home.Vendor.Arrive(side, "Pack", 136, 0x200, 65);
        home.Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 990, side, VendorRig.Player, 0, 1));
        var kit = home.Vendor.NextGuid();
        home.Vendor.Arrive(kit, "Adept Healing Kit", 629, MiscType, 50);
        home.Vendor.Put(kit, side);
        var oil = home.Own("Oil of Rendering", 12711, MiscType, 0);
        var ust = home.Own("Ust", 20646, 0x20000000, 10);
        var run = home.Start(new ResupplyOptions(true));

        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4000));

        var sent = home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.Contains(kit, sent);
        Assert.DoesNotContain(ust, sent); // no buyer's mask takes it: never offered, so never named outside a mask
        Assert.False(home.Vendor.State.Self.Inventory.Contains(oil));
        Assert.False(home.Vendor.State.Self.Inventory.Contains(ust));
        Assert.Equal(2, run.JunkDropped);
        Assert.Equal(0, run.DroppedItems); // the sale's own drop (a full pack) did not run
        Assert.Equal(1, home.Lines.Count(l => l.Contains("pack rule healing-kits (HealingKits): sell 'Adept Healing Kit'", StringComparison.Ordinal)));
        Assert.True(home.Logged("pack rule value-zero-drop: drop 'Oil of Rendering'"));
        Assert.True(home.Logged("pack rule tinkering-tool: drop 'Ust'"));
        Assert.True(home.Logged("check after: gone from the pack"));
        Assert.False(home.Logged("Ust (ItemType 0x20000000) is outside"));
        Assert.Contains("junk_dropped=2", run.VerdictFields(), StringComparison.Ordinal);
    }
}
