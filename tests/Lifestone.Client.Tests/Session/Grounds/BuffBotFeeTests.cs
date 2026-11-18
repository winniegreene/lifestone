using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// D138: the buff bot's fee as a supply line (<see cref="BuffBotFee"/>): every ground carries the Platinum Scarab from the fee's level, whatever her ground and her book, at the counts the stop's own renew rule gives,
/// bought at the catalog's cheapest seller on a way out; a ground that names the ware keeps its own line (a home stock line as it is, a town line merged).
/// </summary>
public sealed class BuffBotFeeTests
{
    private static string? NoFailure(uint wcid) => null;

    [Fact]
    public void The_lines_counts_come_from_the_stops_renew_rule_four_stops_an_hour()
    {
        Assert.Equal(2700, BuffBotRule.RenewUnderSeconds); // a stop whenever a line has under 45 minutes of its hour: at most one each 15 minutes
        Assert.Equal((4, 4, 12), (BuffBotFee.StopsPerHour, BuffBotFee.TripUnder, BuffBotFee.Keep));
        Assert.Equal((60, 8897u, "Platinum Scarab"), (BuffBotWire.FeeLevel, BuffBotWire.FeeWcid, BuffBotWire.FeeName));
    }

    [Theory]
    [InlineData("holtburg-outskirts")]
    [InlineData("tusker-cave")]
    [InlineData("tusker-temple-20-40")]
    public void A_ground_that_names_no_Platinum_Scarab_carries_the_fees_line_at_Archmage_Sammam_al_Ashiz_of_Uziz(string key)
    {
        var line = GroundCatalog.Get(key).TownStockOf(BuffBotWire.FeeWcid);
        Assert.NotNull(line);
        Assert.Equal((BuffBotFee.Keep, BuffBotFee.TripUnder, 15_500, BuffBotFee.Burden, "Uziz"), (line.Keep, line.TripUnder, line.Price, line.Burden, line.Town)); // ceil(1.55 x 10,000 - 0.1): the cheapest seller
        Assert.Equal(("Archmage Sammam al-Ashiz", 1824u, 0x7A260006u), (line.Vendor.Name, line.Vendor.Wcid, line.Vendor.Guid));
        Assert.Equal((60, true), (line.FeeFromLevel, line.FeeOnly));
    }

    [Fact]
    public void The_Holdings_own_town_line_is_merged_and_a_dens_home_stock_line_is_kept_as_it_is()
    {
        var holding = GroundCatalog.Get("tusker-holding");
        var merged = holding.TownStockOf(BuffBotWire.FeeWcid)!;
        Assert.Equal((15, 6, 60, false), (merged.Keep, merged.TripUnder, merged.FeeFromLevel, merged.FeeOnly)); // its own keep and trip line are the larger; it asks from 60 and for her book
        Assert.Single(holding.TownStock, l => l.Wcid == BuffBotWire.FeeWcid);

        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        Assert.NotNull(den.Supplies.StockOf(BuffBotWire.FeeWcid)); // the den carries 100 as a stock line
        Assert.Null(den.TownStockOf(BuffBotWire.FeeWcid));
    }

    [Fact]
    public void The_fees_line_is_bought_from_level_60_whatever_her_book_and_never_under_it()
    {
        var line = GroundCatalog.Get("holtburg-outskirts").TownStockOf(BuffBotWire.FeeWcid)!;
        var book = new Spellbook { IsKnown = true }; // no level VII spell: nothing of her book burns the scarab
        var spells = new Dictionary<uint, SpellInfo>();

        Assert.Null(TownStock.NeedOf(line, 64, book, spells));
        Assert.Null(TownStock.NeedOf(line, 60, new Spellbook(), null)); // not even a book read is needed
        var under = TownStock.NeedOf(line, 59, book, spells);
        Assert.Equal("under-fee", under!.Value.Token);
        Assert.Contains("level 59 is under the buff bot's fee level 60: a round is free", under.Value.Why, StringComparison.Ordinal);

        // The trip: two held, under the trip line 4: ten to the keep of 12, at 15,500 each; at level 30 the line is said once and not bought.
        var due = TownStock.Plan([line], [2], 1_000_000, 2_000, 17_400, 110, NoFailure, l => TownStock.NeedOf(l, 64, book, spells));
        Assert.Equal((TownStockKind.Due, 10, 155_000L, "due"), (due[0].Kind, due[0].Count, due[0].Cost, due[0].Token));
        var free = TownStock.Plan([line], [0], 1_000_000, 2_000, 17_400, 110, NoFailure, l => TownStock.NeedOf(l, 30, book, spells));
        Assert.Equal((TownStockKind.NotNeeded, 0, "under-fee"), (free[0].Kind, free[0].Count, free[0].Token));

        // The Holding's merged line under 60 falls back to her book's rule (no VII spell: unused); from 60 the fee asks for it.
        var holding = GroundCatalog.Get("tusker-holding").TownStockOf(BuffBotWire.FeeWcid)!;
        Assert.Equal("unused", TownStock.NeedOf(holding, 59, book, spells)!.Value.Token);
        Assert.Null(TownStock.NeedOf(holding, 60, book, spells));
    }

    [Fact]
    public void The_line_is_never_at_a_home_vendor_nor_in_the_home_town()
    {
        var catalog = ScrivenerCatalog.Default;
        Assert.Equal("Uziz", BuffBotFee.LineAt(catalog, "Holtburg", [])!.Town);
        var notUziz = BuffBotFee.LineAt(catalog, "Uziz", [])!;
        Assert.NotEqual("Uziz", notUziz.Town);
        Assert.Equal(15_500, notUziz.Price); // Archmage Tawa Matema of Sawato sells at the same 1.55
        var sammam = new GroundVendor("Archmage Sammam al-Ashiz", 1824, 0x7A260006, 0xA2600112, default, 0xA2600112, null, 0, []);
        Assert.NotEqual(1824u, BuffBotFee.LineAt(catalog, "Holtburg", [sammam])!.Vendor.Wcid);
    }
}
