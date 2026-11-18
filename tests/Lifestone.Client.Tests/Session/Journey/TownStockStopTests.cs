using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health tinctures, and Mana Tinctures too): the stop in Dryreach on the way out, over the rung change's stand-in ACE (<see cref="RungRig"/>: the portals of the travel file answer a Use with the
/// teleport to their destination; it holds no vendor). The stop is made in line, from the Town Network and back to it, before the route goes on to the Facility Hub; a purse too poor holds it back and says so once; a failure is named
/// once, remembered for the run and the way out goes on. The buy itself is the resupply's shop step (<c>ResupplyStationTests.TownStock</c>).
/// </summary>
public sealed class TownStockStopTests
{
    private static readonly TravelRoutes Routes = TravelRoutes.Default;

    private static GroundEntry BurrowEntry => new(22663, 0x7F981000, 0xF9810040, new Vector3(176.323f, 186.044f, 12.5f), 0xF9810040, new Vector3(176.3f, 183.0f, 12.5f), null, null, false);

    /// <summary>The rung change tests' Burrow (the Temple 40+ profile re-keyed with the Burrow's entry) with the Tusker Cave's town stock.</summary>
    private static HuntingGround Burrow => GroundCatalog.Get("tusker-temple-fort-tethana") with
    {
        Key = "tusker-burrow",
        Name = "tusker-burrow",
        Landblocks = [0x01D4],
        Arrival = new GroundArrival(0x01D40100, new Vector3(10, 10, 0), 1, 0),
        Portal = null,
        Entry = BurrowEntry,
        Exit = new GroundExitSection(GroundExitKind.Portal, 42999, 0x01D40105, new Vector3(5, 5, 0), 0xF9810038, null),
        TownStock = GroundCatalog.Get("tusker-cave").TownStock,
    };

    private static string Use(string edge) => "use:" + Routes.Edge(edge).Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    private static RungRig Rig(int coins)
    {
        var t = new RungRig(level: 15);
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow", "network-to-dryreach", "dryreach-to-network");
        t.Entry(BurrowEntry, Burrow);
        t.R.Raw(AceWireWriters.PrivateUpdatePropertyInt(1, PlayerQualities.CoinValueProperty, coins));
        t.R.Tick();
        return t;
    }

    [Fact]
    public void The_stop_is_made_in_line_from_the_Network_to_Dryreach_and_back_and_a_shop_that_buys_nothing_is_named_once_and_the_way_out_goes_on()
    {
        var t = Rig(coins: 60_000);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        var tail = string.Join(" | ", t.Lines("town_stock").Select(l => l.Length > 500 ? l[..500] : l));
        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + tail);
        Assert.Equal(0x01D4u, t.Landblock); // she is in the Burrow: the way out went on
        // In line: Holtburg's portal once, then Dryreach and back, then the Facility Hub: no second trip from home.
        var uses = t.Events.Where(e => e.StartsWith("use:", StringComparison.Ordinal)).ToList();
        Assert.Equal([Use("holtburg-to-network"), Use("network-to-dryreach"), Use("dryreach-to-network"), Use("network-to-facility-hub"), Use("hub-burrow")], uses.Take(5));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: check before: in town-network", StringComparison.Ordinal) && l.Contains("0 Health Tincture held, under the trip line 6: buys 12 of the 12", StringComparison.Ordinal)
            && l.Contains("0 Mana Tincture held, under the trip line 10: buys 30 of the 30", StringComparison.Ordinal) && l.Contains("32550 pyreals in all; hops network-to-dryreach,dryreach-to-network", StringComparison.Ordinal));
        // This rig holds no Ludora: the shop step fails by name, both lines are remembered, and the stop's line says so.
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: town=Dryreach vendor=2229 wares=27319+27322 asked=12+30 bought=0+0 hops=network-to-dryreach,dryreach-to-network", StringComparison.Ordinal) && l.Contains("ok=no reason=shop", StringComparison.Ordinal)
            && l.Contains("the way out goes on without it; it is tried again on the next way out (at most 3 failed stops a ware a run)", StringComparison.Ordinal));
        Assert.Null(context.Facts.TownStockGivenUp(HealthDrinks.TinctureWcid)); // one failed stop: the next way out tries again
        Assert.StartsWith("shop: ", context.Facts.TownStockFailure(HealthDrinks.TinctureWcid), StringComparison.Ordinal);
        Assert.StartsWith("shop: ", context.Facts.TownStockFailure(ManaDrinks.TinctureWcid), StringComparison.Ordinal);
        Assert.Contains("Ludora_the_Healer_0x7DA75026_was_not_met_in_reach", context.Facts.TownStockFailure(ManaDrinks.TinctureWcid), StringComparison.Ordinal); // the shop step's own failure, in its verdict fields
    }

    [Fact]
    public void A_purse_too_poor_makes_no_stop_and_says_so_once_a_run()
    {
        var t = Rig(coins: 3_000);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields());
        Assert.DoesNotContain(Use("network-to-dryreach"), t.Events);
        Assert.Single(t.Lines("town_stock: no Health Tincture at Ludora the Healer in Dryreach (reason=purse)"));
        Assert.Single(t.Lines("town_stock: no Mana Tincture at Ludora the Healer in Dryreach (reason=purse)"));
        Assert.Contains("held back for the purse; said once a run, the way out goes on", t.Lines("no Health Tincture")[0], StringComparison.Ordinal);
        Assert.False(context.Facts.TownStockSayOnce(HealthDrinks.TinctureWcid, "purse")); // said: the next way out of this run stays silent
        Assert.Null(context.Facts.TownStockFailure(HealthDrinks.TinctureWcid)); // held back is no failure: the next way out with the coin makes the stop
    }

    /// <summary>M6 item 14 (m6-purse; live 2026-11-12: 0 of 102 slots free): the shop step buys nothing into a full main pack, so the stop is not made: named once, no failed stop counted, the way out goes on.</summary>
    [Fact]
    public void A_full_main_pack_makes_no_stop_says_so_once_and_counts_no_failed_stop_M6_14_purse()
    {
        var t = Rig(coins: 60_000);
        for (uint i = 0; Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(t.R.H.R.State) > 0; i++)
        {
            var filler = 0x80010000 + i;
            var basis = ReducerHarness.Create(filler, ReducerHarness.Stamps(), null, name: "Malachite");
            t.R.H.R.Reducer.OnCreateObject(t.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = 2416, ItemType = 0x800 } }, isUpdate: false);
            t.R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, filler, WorldActionRig.Player, 0, 0));
            t.R.Tick();
        }

        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields());
        Assert.Equal(0x01D4u, t.Landblock); // the way out went on
        Assert.DoesNotContain(Use("network-to-dryreach"), t.Events);
        var said = Assert.Single(t.Lines("(reason=pack-full)"));
        Assert.Contains("town_stock: no Health Tincture, Mana Tincture at Ludora the Healer in Dryreach (reason=pack-full): the main pack has no free slot and a ware bought needs one: no stop", said, StringComparison.Ordinal);
        Assert.Null(context.Facts.TownStockFailure(HealthDrinks.TinctureWcid)); // no stop, so no failed stop: the next way out with room makes it
        Assert.Null(context.Facts.TownStockFailure(ManaDrinks.TinctureWcid));
    }

    [Fact]
    public void A_portal_into_the_town_that_does_not_take_her_costs_the_stop_once_and_the_way_out_goes_on()
    {
        var t = Rig(coins: 60_000);
        t.Silent.Add(Routes.Edge("network-to-dryreach").Guid);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + string.Join(" | ", t.Lines("town_stock").Select(l => l.Length > 400 ? l[..400] : l)));
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.DoesNotContain(Use("dryreach-to-network"), t.Events); // she never left the Network
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: town=Dryreach vendor=2229 wares=27319+27322 asked=12+30 bought=0+0", StringComparison.Ordinal) && l.Contains("ok=no reason=hop", StringComparison.Ordinal));
        Assert.StartsWith("hop:", context.Facts.TownStockFailure(HealthDrinks.TinctureWcid), StringComparison.Ordinal);
        Assert.StartsWith("hop:", context.Facts.TownStockFailure(ManaDrinks.TinctureWcid), StringComparison.Ordinal);
        Assert.Contains("route=by-facility-hub ", run.VerdictFields(), StringComparison.Ordinal); // the route itself did not fail or fall back
    }

    private const uint FlameBoltV = 84;
    private const uint GoldScarab = 687;
    private const uint PyrealScarab = 690;

    /// <summary>M6 item 14 (m6-goldscarab): the rig with Uziz's two portals beside Dryreach's; with <paramref name="levelV"/> the book holds Flame Bolt V, whose formula in the rig's table is the Gold Scarab's (component 5).</summary>
    private static RungRig UzizRig(int coins, bool levelV, int gold = 0)
    {
        var t = new RungRig(level: 38, spells: levelV ? [(ushort)FlameBoltV] : null);
        t.Caster.Table[FlameBoltV] = t.Caster.Table[FlameBoltV] with { Formula = [5, 0, 0, 0, 0, 0, 0, 0] };
        t.Portals(Routes, "holtburg-to-network", "network-to-facility-hub", "hub-burrow", "network-to-dryreach", "dryreach-to-network", "network-to-uziz", "uziz-to-network");
        t.Entry(BurrowEntry, Burrow);
        t.R.Raw(AceWireWriters.PrivateUpdatePropertyInt(1, PlayerQualities.CoinValueProperty, coins));
        if (gold > 0)
        {
            const uint stack = 0x80000687;
            var basis = ReducerHarness.Create(stack, ReducerHarness.Stamps(), null, name: "Gold Scarab");
            t.R.H.R.Reducer.OnCreateObject(t.R.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize, ContainerId = WorldActionRig.Player, WeenieClassId = GoldScarab, ItemType = 0x1000, StackSize = (ushort)gold } }, isUpdate: false);
            t.R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, stack, WorldActionRig.Player, 0, 0));
        }

        t.R.Tick();
        return t;
    }

    [Fact]
    public void With_a_level_V_spell_in_the_book_and_no_Gold_Scarab_the_stop_at_Uziz_follows_Dryreachs_in_line_and_the_way_out_goes_on_M6_14_goldscarab()
    {
        var t = UzizRig(coins: 400_000, levelV: true);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        var tail = string.Join(" | ", t.Lines("town_stock").Select(l => l.Length > 500 ? l[..500] : l));
        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + tail);
        Assert.Equal(0x01D4u, t.Landblock); // she is in the ground: no scarab stops the way out
        // In line: Holtburg's portal once, Dryreach and back, Uziz and back, then the Facility Hub.
        var uses = t.Events.Where(e => e.StartsWith("use:", StringComparison.Ordinal)).ToList();
        Assert.True(uses.Take(7).SequenceEqual([Use("holtburg-to-network"), Use("network-to-dryreach"), Use("dryreach-to-network"), Use("network-to-uziz"), Use("uziz-to-network"), Use("network-to-facility-hub"), Use("hub-burrow")]), string.Join(' ', uses) + " | " + tail);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: check before: in town-network", StringComparison.Ordinal)
            && l.Contains("0 Gold Scarab held, under the trip line 40: buys 100 of the 100 to the keep 100 at Archmage Sammam al-Ashiz in Uziz, 77500 pyreals at 775 each, burden 400; 77500 pyreals in all; hops network-to-uziz,uziz-to-network", StringComparison.Ordinal));
        // No level VI spell: the Pyreal Scarab is not bought and is said once.
        Assert.Single(t.Lines("town_stock: no Pyreal Scarab at Archmage Sammam al-Ashiz in Uziz (reason=unused): 0 Pyreal Scarab held, under the trip line 40, but no spell of the book burns it"));
        Assert.Empty(t.Lines("town_stock: no Gold Scarab"));
        // This rig holds no Sammam: the shop step fails by name, the line is remembered for the next way out, and the stop's line says so.
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: town=Uziz vendor=1824 wares=687 asked=100 bought=0 hops=network-to-uziz,uziz-to-network", StringComparison.Ordinal) && l.Contains("ok=no reason=shop", StringComparison.Ordinal)
            && l.Contains("the way out goes on without it; it is tried again on the next way out (at most 3 failed stops a ware a run)", StringComparison.Ordinal));
        Assert.Contains("Archmage_Sammam_al-Ashiz_0x7A260006_was_not_met_in_reach", context.Facts.TownStockFailure(GoldScarab), StringComparison.Ordinal);
        Assert.Null(context.Facts.TownStockGivenUp(GoldScarab));
        Assert.Null(context.Facts.TownStockFailure(PyrealScarab)); // not needed is no failure
    }

    [Fact]
    public void With_the_trip_line_of_Gold_Scarabs_in_the_pack_no_stop_is_made_at_Uziz_M6_14_goldscarab()
    {
        var t = UzizRig(coins: 400_000, levelV: true, gold: 40);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields());
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.DoesNotContain(Use("network-to-uziz"), t.Events);
        Assert.Contains(Use("network-to-dryreach"), t.Events); // the healer's stop is its own: made as before
        Assert.Empty(t.Lines("town_stock: no Gold Scarab")); // a line that holds its trip line says nothing
        Assert.Empty(t.Lines("town=Uziz"));
    }

    [Fact]
    public void With_no_spell_that_burns_it_no_scarab_is_bought_and_each_line_says_so_once_a_run_M6_14_goldscarab()
    {
        var t = UzizRig(coins: 400_000, levelV: false);
        t.Portals(Routes, "refuge-to-network", "network-to-holtburg");
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields());
        Assert.DoesNotContain(Use("network-to-uziz"), t.Events);
        Assert.Single(t.Lines("town_stock: no Gold Scarab at Archmage Sammam al-Ashiz in Uziz (reason=unused)"));
        Assert.Single(t.Lines("town_stock: no Pyreal Scarab at Archmage Sammam al-Ashiz in Uziz (reason=unused)"));
        Assert.Contains("nothing is bought; said once a run, the way out goes on", t.Lines("no Gold Scarab")[0], StringComparison.Ordinal);
        Assert.False(context.Facts.TownStockSayOnce(GoldScarab, "unused")); // said: the next way out of this run stays silent
        Assert.Null(context.Facts.TownStockFailure(GoldScarab));
    }

    [Fact]
    public void A_portal_into_Uziz_that_does_not_take_her_costs_that_stop_only_and_the_way_out_goes_on_M6_14_goldscarab()
    {
        var t = UzizRig(coins: 400_000, levelV: true);
        t.Silent.Add(Routes.Edge("network-to-uziz").Guid);
        var context = t.Context(Burrow);

        var (run, status) = t.RunStation(new RungChangeStation(), context);

        Assert.True(status == ScenarioStatus.Succeeded, run.VerdictFields() + " | " + string.Join(" | ", t.Lines("town_stock").Select(l => l.Length > 400 ? l[..400] : l)));
        Assert.Equal(0x01D4u, t.Landblock);
        Assert.DoesNotContain(Use("uziz-to-network"), t.Events); // she never left the Network
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("town_stock: town=Uziz vendor=1824 wares=687 asked=100 bought=0", StringComparison.Ordinal) && l.Contains("ok=no reason=hop", StringComparison.Ordinal));
        Assert.StartsWith("hop:", context.Facts.TownStockFailure(GoldScarab), StringComparison.Ordinal);
        Assert.StartsWith("hop:", context.Facts.TownStockFailure(PyrealScarab), StringComparison.Ordinal); // the hop's failure is every line's of that vendor
        Assert.StartsWith("shop: ", context.Facts.TownStockFailure(HealthDrinks.TinctureWcid), StringComparison.Ordinal); // Dryreach's stop was made before it (this rig holds no Ludora)
    }

    [Fact]
    public void A_way_home_makes_no_stop()
    {
        var t = Rig(coins: 60_000);
        t.Portals(Routes, "refuge-to-network", "network-to-holtburg");
        var context = t.Context(Burrow);
        t.RunStation(new RungChangeStation(), context);
        var before = t.Events.Count(e => e == Use("network-to-dryreach"));

        t.RunStation(new RungChangeStation(RungDirection.Home), context);

        Assert.Equal(before, t.Events.Count(e => e == Use("network-to-dryreach")));
    }
}
