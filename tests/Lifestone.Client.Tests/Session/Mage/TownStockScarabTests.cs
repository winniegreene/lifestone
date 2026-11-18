using System.Text.Json.Nodes;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Travel;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (m6-goldscarab; live 2026-11-12: a level 38 mage with Flame Bolt V and Heal Self V in the book cast level IV of everything, because level V burns a Gold Scarab and no vendor of Holtburg sells one): the Gold Scarab (level V)
/// and the Pyreal Scarab (level VI) are town stock, bought at Archmage Sammam al-Ashiz of Uziz on the way out. The profiles' rows load; a plan component may be a town stock line; the stop is planned under the trip line and skipped
/// over it; a scarab is bought only while a spell of the book burns it; and the stop's portals are the Town Network's Uziz portal and Uziz's own.
/// </summary>
public sealed class TownStockScarabTests
{
    private const uint Gold = 687;
    private const uint Pyreal = 690;

    private static readonly IReadOnlyList<TownStockLine> Uziz = [.. GroundCatalog.Get("tusker-cave").TownStock.Where(l => l.Town == "Uziz" && !l.FeeOnly)];

    private static TownStockLine GoldLine => Uziz[0];

    private static TownStockLine PyrealLine => Uziz[1];

    private static string? NoFailure(uint wcid) => null;

    private static SpellInfo Bolt(uint id, uint scarab) => WorldActionRig.Spell(id, SpellType.Projectile) with { Name = "Bolt " + id, Formula = [scarab, 63, 34, 46, 0, 0, 0, 0], FormulaVersion = 1 };

    /// <summary>Flame Bolt IV, V and VI by their scarabs (the SpellComponentTable's ids 4, 5 and 6: Silver, Gold, Pyreal).</summary>
    private static Dictionary<uint, SpellInfo> Spells => new() { [83] = Bolt(83, 4), [84] = Bolt(84, 5), [85] = Bolt(85, 6) };

    [Theory]
    [InlineData("tusker-cave")]
    [InlineData("tusker-cavern")]
    [InlineData("tusker-abode")]
    [InlineData("tusker-habitat")]
    [InlineData("tusker-quarters")]
    [InlineData("tusker-barracks")]
    public void The_grounds_whose_way_out_passes_the_Town_Network_buy_the_two_scarabs_at_Sammam_of_Uziz_and_carry_no_stock_line_of_them(string key)
    {
        var ground = GroundCatalog.Get(key);
        var lines = ground.TownStock.Where(l => l.Town == "Uziz" && !l.FeeOnly).ToList();
        Assert.Equal(
            [(Gold, "Gold Scarab", 100, 40, 775, 4), (Pyreal, "Pyreal Scarab", 100, 40, 1550, 4)],
            lines.Select(l => (l.Wcid, l.Name, l.Keep, l.TripUnder, l.Price, l.Burden)));
        Assert.Equal(775, (int)Math.Max(1, Math.Ceiling((1.55 * 500) - 0.1))); // ACE's Vendor.GetSellCost on his SellPrice 1.55 and the Gold Scarab's Value 500
        Assert.Equal(1550, (int)Math.Max(1, Math.Ceiling((1.55 * 1000) - 0.1))); // and the Pyreal Scarab's Value 1,000
        Assert.All(lines, l =>
        {
            Assert.Equal(("Archmage Sammam al-Ashiz", 1824u, 0x7A260006u, 0xA2600112u), (l.Vendor.Name, l.Vendor.Wcid, l.Vendor.Guid, l.Vendor.Cell));
            Assert.Same(l, ground.TownStockOf(l.Wcid));
            Assert.Null(ground.Supplies.StockOf(l.Wcid)); // no stock line: nothing of it calls a trip or stops the run (LogisticsCounters reads the stock lines only)
            Assert.Null(ground.VendorSelling(l.Wcid)); // no home vendor sells it
            Assert.True(ComponentWeenies.TryGetByWcid(l.Wcid, out var component));
            Assert.Equal(l.Keep, component.MaxStack); // the keep is one stack
            Assert.Equal(("network-to-uziz", "uziz-to-network"), TownStock.EdgesOf(l, "town-network", TravelRoutes.Default) is { } e ? (e.Into.Key, e.Back.Key) : default);
        });
        Assert.Null(ground.TownStockOf(688)); // the Silver Scarab is the home walk's
        Assert.Contains("holtburg-to-network", TravelRoutes.Default.RoutesFor(key)[0].Steps.Select(s => s.Edge.Key)); // the first route out passes the stop's door

        // Every component of every plan row is a stock line or a town stock line (the loader's rule), and level V's scarab is the town stock's wherever the plan names level V.
        foreach (var row in ground.SpellPlan)
        {
            Assert.All(ground.Components.Where(c => c.SpellLevel == row.SpellLevel), c => Assert.True(ground.Supplies.StockOf(c.Wcid) is not null || ground.TownStockOf(c.Wcid) is not null, $"{key}: {c.Name} of level {row.SpellLevel}"));
        }
    }

    [Fact]
    public void The_Temple_20_40_whose_way_out_passes_no_Town_Network_keeps_its_inert_Gold_Scarab_line_and_names_no_town_stock()
    {
        var temple = GroundCatalog.Get("tusker-temple-20-40");
        Assert.DoesNotContain(temple.TownStock, l => !l.FeeOnly); // D138: the buff bot's fee line is every ground's (BuffBotFeeTests); inert here
        Assert.Null(temple.TownStockOf(Gold));
        var line = temple.Supplies.StockOf(Gold)!;
        Assert.Equal((20, 0, 0), (line.Keep, line.TripUnder, line.StopUnder)); // never a trip, never a stop: it only marks the scarabs she brings as supplies
        Assert.DoesNotContain("holtburg-to-network", TravelRoutes.Default.RoutesFor("tusker-temple-20-40")[0].Steps.Select(s => s.Edge.Key)); // the New Colier Portal on foot: a stop there would never be made
    }

    [Fact]
    public void A_plan_component_bought_in_another_town_loads_and_one_that_is_neither_a_stock_line_nor_town_stock_fails_at_load_by_name()
    {
        const string file = "tusker-abode.json";
        var abode = GroundCatalog.Get("tusker-abode");
        Assert.Contains(abode.SpellPlan, r => r.SpellLevel == 5);
        Assert.Contains(abode.Components, c => c.Wcid == Gold && c.SpellLevel == 5);
        Assert.NotNull(GroundCatalog.Parse(file, GroundCatalog.ResourceText(file)!).TownStockOf(Gold));

        // Without the town stock section the level V row has a component nothing buys.
        var none = JsonNode.Parse(GroundCatalog.ResourceText(file)!)!;
        none.AsObject().Remove("town_stock");
        var gap = Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(file, none.ToJsonString()));
        Assert.StartsWith("$.spell_plan[", gap.JsonPath, StringComparison.Ordinal);
        Assert.Contains("TODO(ground-plan-gap)", gap.Message, StringComparison.Ordinal);
        Assert.Contains("687 (Gold Scarab), which is not a line of $.supplies.stock or of $.town_stock", gap.Message, StringComparison.Ordinal);

        // A ware is bought at home or in a town, not both: the old carried line beside the town stock line is refused.
        var both = JsonNode.Parse(GroundCatalog.ResourceText(file)!)!;
        ((JsonArray)both["supplies"]!["stock"]!).Add(JsonNode.Parse("""{ "wcid": 687, "name": "Gold Scarab", "keep": 20, "trip_under": 2, "stop_under": 2 }"""));
        Assert.Equal("$.town_stock[2].wcid", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(file, both.ToJsonString())).JsonPath); // M6 14: the tinctures, the Cave's rows, come first

        // A keep under the trip line is refused, as any town stock line's.
        var low = JsonNode.Parse(GroundCatalog.ResourceText(file)!)!;
        low["town_stock"]![0]!["trip_under"] = 101;
        Assert.Equal("$.town_stock[0].trip_under", Assert.Throws<GroundProfileException>(() => GroundCatalog.Parse(file, low.ToJsonString())).JsonPath);
    }

    [Fact]
    public void A_scarab_is_needed_only_while_a_spell_of_the_book_burns_it_and_a_drink_always()
    {
        var spells = Spells;
        var book = new Spellbook { IsKnown = true };
        book.Learn(83); // level IV only: the Silver Scarab's

        var unused = TownStock.UnusedWhy(GoldLine, book, spells);
        Assert.Equal("unused", unused!.Value.Token);
        Assert.Equal("no spell of the book burns it (component 5; 1 of the book's 1 spells read in the spell table)", unused.Value.Why);

        book.Learn(84); // Flame Bolt V
        Assert.Null(TownStock.UnusedWhy(GoldLine, book, spells));
        Assert.Equal("unused", TownStock.UnusedWhy(PyrealLine, book, spells)!.Value.Token); // no level VI spell yet: no Pyreal Scarab is bought

        book.Learn(85); // Flame Bolt VI
        Assert.Null(TownStock.UnusedWhy(PyrealLine, book, spells));

        // A drink is no spell component: always needed, whatever the book.
        var tincture = GroundCatalog.Get("tusker-cave").TownStock.First(l => l.Town == "Dryreach");
        Assert.Null(TownStock.UnusedWhy(tincture, new Spellbook { IsKnown = true }, spells));
        Assert.Null(TownStock.UnusedWhy(tincture, new Spellbook(), spells));

        // A book that is not read is not guessed at; nor is a spell table that holds none of the book's spells: both are named, and nothing is bought on them.
        Assert.Equal("book-unread", TownStock.UnusedWhy(GoldLine, new Spellbook(), spells)!.Value.Token);
        var stray = new Spellbook { IsKnown = true };
        stray.Learn(9_999);
        var unread = TownStock.UnusedWhy(GoldLine, stray, spells)!.Value;
        Assert.Equal("table-unread", unread.Token);
        Assert.StartsWith("TODO(m6-town-stock-spell-table): none of the book's 1 spells is in the spell table given to the stop", unread.Why, StringComparison.Ordinal);
        Assert.Equal(TownStockKind.NotNeeded, TownStock.Decide(GoldLine, new TownStockInput(0, 900_000, 0, 17_400, 110, null, Unused: unread)).Kind);
        stray.Learn(83); // one spell the table reads, which burns no Gold Scarab: the plain answer again
        Assert.Equal("unused", TownStock.UnusedWhy(GoldLine, stray, spells)!.Value.Token);
    }

    [Fact]
    public void The_stop_is_planned_under_the_trip_line_and_skipped_over_it_and_the_Pyreal_Scarab_waits_for_a_level_VI_spell()
    {
        // A level V spell in the book and no level VI one: the Pyreal Scarab is not needed.
        (string Token, string Why)? OnlyGold(TownStockLine l) => l.Wcid == Pyreal ? ("unused", "no spell of the book burns it") : null;

        // None held: 100 Gold Scarabs, 77,500 pyreals, burden 400; the Pyreal Scarab neither calls the stop nor rides it.
        var first = TownStock.Plan(Uziz, [0, 0], 900_000, 2_000, 17_400, 110, NoFailure, OnlyGold);
        Assert.Equal([(TownStockKind.Due, 100, 77_500L, "due"), (TownStockKind.NotNeeded, 0, 0L, "unused")], first.Select(v => (v.Kind, v.Count, v.Cost, v.Token)));
        Assert.Equal("0 Gold Scarab held, under the trip line 40: buys 100 of the 100 to the keep 100 at Archmage Sammam al-Ashiz in Uziz, 77500 pyreals at 775 each, burden 400", first[0].Why);
        Assert.Equal("0 Pyreal Scarab held, under the trip line 40, but no spell of the book burns it: nothing is bought", first[1].Why);

        // 39 held: one under the trip line, the stop is made and buys the 61 to the keep. 40 held: the line holds, no line is due, no stop.
        Assert.Equal((TownStockKind.Due, 61, 47_275L), TownStock.Plan(Uziz, [39, 0], 900_000, 2_000, 17_400, 110, NoFailure, OnlyGold).Select(v => (v.Kind, v.Count, v.Cost)).First());
        var holds = TownStock.Plan(Uziz, [40, 0], 900_000, 2_000, 17_400, 110, NoFailure, OnlyGold);
        Assert.Equal([(TownStockKind.NotDue, "holds"), (TownStockKind.NotNeeded, "unused")], holds.Select(v => (v.Kind, v.Token)));
        Assert.DoesNotContain(holds, v => v.Kind == TownStockKind.Due);

        // A level VI spell in the book: the Pyreal Scarab is due from none (100, 155,000 pyreals) and the Gold line, holding 60, rides to its keep.
        var both = TownStock.Plan(Uziz, [60, 0], 900_000, 2_000, 17_400, 110, NoFailure);
        Assert.Equal([(TownStockKind.Due, 40, 31_000L, "riding"), (TownStockKind.Due, 100, 155_000L, "due")], both.Select(v => (v.Kind, v.Count, v.Cost, v.Token)));

        // A purse that pays fewer than the 40 to the trip line makes no stop for it (the rule of M6 7 V): 30,000 pays 38.
        var poor = TownStock.Plan(Uziz, [0, 0], 30_000, 2_000, 17_400, 110, NoFailure, OnlyGold);
        Assert.Equal((TownStockKind.HeldBack, 0, "purse"), (poor[0].Kind, poor[0].Count, poor[0].Token));
        Assert.Equal(TownStockKind.Due, TownStock.Plan(Uziz, [0, 0], 31_000, 2_000, 17_400, 110, NoFailure, OnlyGold)[0].Kind); // 40 for 31,000: the trip line is reached

        // A stop given up for the run is not planned again; it never stops anything.
        var failed = TownStock.Plan(Uziz, [0, 0], 900_000, 2_000, 17_400, 110, w => w == Gold ? "hop:network-to-uziz: no teleport; 3 stops failed, the bound 3" : null, OnlyGold);
        Assert.Equal([TownStockKind.Failed, TownStockKind.NotNeeded], failed.Select(v => v.Kind));
    }

    [Fact]
    public void The_stop_is_made_from_the_Town_Network_by_its_Uziz_portal_and_back_by_Uzizs_own()
    {
        var routes = TravelRoutes.Default;
        var edges = TownStock.EdgesOf(GoldLine, "town-network", routes);
        Assert.NotNull(edges);
        Assert.Equal(("network-to-uziz", "uziz-to-network"), (edges.Value.Into.Key, edges.Value.Back.Key));
        Assert.Equal((42821u, 0x7000700Au, 0xA260003Cu), (edges.Value.Into.Wcid, edges.Value.Into.Guid, edges.Value.Into.DestCell));
        Assert.Equal((43066u, 0x7A260022u, 0x00070133u), (edges.Value.Back.Wcid, edges.Value.Back.Guid, edges.Value.Back.DestCell));
        Assert.Equal((null, null, null, null), (edges.Value.Into.MinLevel, edges.Value.Into.MaxLevel, edges.Value.Back.MinLevel, edges.Value.Back.MaxLevel)); // no gate either way
        Assert.Equal("uziz", routes.PlaceOf("0xA260")!.Key);
        Assert.Null(TownStock.EdgesOf(GoldLine, "holtburg", routes)); // no portal of Holtburg leads to Uziz
        Assert.Null(TownStock.EdgesOf(GoldLine, "facility-hub", routes));
        Assert.Null(TownStock.EdgesOf(GoldLine, "uziz", routes)); // she is in the town already: no hop
        var home = routes.WayHomeFrom("uziz", 1);
        Assert.Equal(TravelOutcome.Admitted, home.Outcome);
        Assert.Equal(["uziz-to-network", "network-to-holtburg"], home.WayHome!.Steps.Select(s => s.Edge.Key)); // a stop whose portal back failed has a way home from Uziz
    }

    [Fact]
    public void A_kit_built_for_a_ground_names_no_burn_of_a_scarab_the_town_stock_buys_and_without_the_town_stock_the_gap_is_named()
    {
        // The Abode's plan casts level V at 42 to 59. Every spell of the profile burns the Copper Scarab here (a stock line of the Abode) but Flame Bolt V, which burns the Gold Scarab, the town stock's.
        var abode = GroundCatalog.Get("tusker-abode");
        var boltV = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Single(b => b.Position == 5).Id;
        var spells = ArenaProfile.Lines.Concat(ArenaProfile.Extras).SelectMany(l => l.Spells).GroupBy(b => b.Id).ToDictionary(
            g => g.Key,
            g => WorldActionRig.Spell(g.Key, SpellType.Enchantment) with { Name = g.First().Name, Formula = [g.Key == boltV ? 5u : 3u, 0, 0, 0, 0, 0, 0, 0] });

        var burns = GearKit.CastBurns(abode, spells, heroRing: false, keepers: false);

        Assert.DoesNotContain(burns, b => b.Wcid is Gold or Pyreal); // a build grants none: she buys it at the stop, and casts level IV until then (the level floor)
        Assert.Contains(burns, b => b.SpellId == boltV && b.Wcid == 20631 && b.PerCast == 4); // the same cast's four Prismatic Tapers stay the home walk's
        Assert.Contains(burns, b => b.Wcid == 686);

        // The same ground with no town stock: the level V row's scarab is a gap, by name, as before.
        var gap = Assert.Throws<InvalidOperationException>(() => GearKit.CastBurns(abode with { TownStock = [] }, spells, heroRing: false, keepers: false));
        Assert.Contains("ground-plan-gap", gap.Message, StringComparison.Ordinal);
        Assert.Contains("Gold Scarab (wcid 687)", gap.Message, StringComparison.Ordinal);
    }
}
