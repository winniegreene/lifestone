using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 holding safety (the owner, 2026-11-13: a ground profile holds facts; every number about how she fights or what she carries is computed from her live state and the creatures' facts): the Tusker Holding, the climber's rung from 65,
/// no longer carries the Black Spawn Den's kit for BSD Mage. Its hand-sized fight block is gone and its Rampagers carry their attack's facts, so the retreat line is derived on her live state (<c>FightLinesTests</c>); its supplies, town stock,
/// components and vendors are the Abode's general rule; its plan rows are the ladder's projection (the VII crossing settled), and the live cap governs the ladder's fight.
/// </summary>
public sealed class HoldingKitTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    private static HuntingGround Abode => GroundCatalog.Get("tusker-abode");

    [Fact]
    public void The_hand_sized_fight_block_is_gone_and_the_Rampagers_carry_their_attacks_facts()
    {
        var ground = Holding;
        Assert.Null(ground.FightRetreatHealthPercent); // the den's 0 (D91, BSD Mage at 80) no longer says "never": the derived line governs
        Assert.Null(ground.FightRingMinTargets); // the den's 4 for BSD Mage's Ring of Fire: the general default (WarPickSettings.DefaultRingMinTargets)
        Assert.Equal(GroundCatalog.Get("tusker-barracks").PullRest, ground.PullRest); // only the owner's general pull rules, and the Rampagers' VisualAwarenessRange 25 (the Guards' too)
        Assert.Equal((0, 25.0), (ground.PullRest!.ManaPercent, ground.PullRest.AggroMetres)); // the pull's mana is the vitals policy's live line

        var hitter = ground.HardestHitter(out var types);
        Assert.NotNull(hitter);
        Assert.Equal(("Rampager", 160, 0.75, 230, 293), (hitter.Name, hitter.DVal, hitter.DVar, hitter.Strength, hitter.Hit)); // the ladder's holding rung names the same hit 293 (sources.health)
        Assert.Equal(0x4u, types);
        Assert.All(ground.Targets, t => Assert.Equal((160, 0.75), (t.DVal, t.DVar))); // the three weenies' rows of the pinned world SQL
    }

    [Fact]
    public void No_carried_line_can_stop_a_climber_who_was_never_granted_anything()
    {
        var ground = Holding;
        Assert.All(ground.Supplies.Stock, line => Assert.True(ground.VendorSelling(line.Wcid) is not null, $"{line.Name} {line.Wcid} is sold at home (a carried line under its stop line stops the run by name)"));
        foreach (var wcid in new uint[] { 687, 690, 8897, 7299, 691 }) // the den's carried Gold, Pyreal, Platinum, Diamond and Lead Scarab lines
        {
            Assert.Null(ground.Supplies.StockOf(wcid));
        }

        Assert.Empty(ground.Supplies.Kit); // the den's robe, gauntlets, wand and packs were BSD Mage's
        Assert.Equal(66_000, ground.Supplies.Purse.Coin);
        Assert.Equal(new[] { 2625u }, ground.Supplies.Purse.Notes.Select(n => n.Wcid));
        foreach (var wcid in new uint[] { 377, 379, 688, 686 }) // the Potion of Healing, the Mana Potion, the Silver and Copper Scarabs: the Abode's lines
        {
            Assert.Equal(Abode.Supplies.StockOf(wcid), ground.Supplies.StockOf(wcid));
        }
    }

    [Fact]
    public void The_Prismatic_Taper_line_is_the_general_rules_from_the_rungs_move_on_numbers()
    {
        var rung = LadderLoader.WarMage.Rungs.Single(r => r.Key == "holding");
        var casts = 2 * rung.MoveOn.KillsAnHourFloor!.Value * rung.MoveOn.CastsAKillCeiling!.Value; // twice the floor at the ceiling, the worst case
        var tenMinutes = Math.Ceiling(casts * 4 / 6); // four tapers a cast on the foci formula, ten minutes
        var keep = (int)Math.Min(1000, Math.Ceiling(tenMinutes / 50) * 50);
        var trip = (int)(Math.Ceiling(keep / 3.0 / 50) * 50);
        Assert.Equal(new StockLine(20631, "Prismatic Taper", keep, trip, 5), Holding.Supplies.StockOf(20631)); // a cast needs 4
        Assert.Equal((850, 300), (keep, trip));
    }

    [Fact]
    public void The_town_stock_is_the_Abodes_four_lines_and_the_Platinum_Scarab_of_level_VII()
    {
        var ground = Holding;
        foreach (var abode in Abode.TownStock.Where(l => !l.FeeOnly)) // D138: the Abode's fee line is merged into the Holding's own Platinum Scarab line (BuffBotFeeTests)
        {
            var here = ground.TownStockOf(abode.Wcid);
            Assert.NotNull(here);
            Assert.Equal((abode.Name, abode.Keep, abode.TripUnder, abode.Price, abode.Burden, abode.Town, abode.Vendor.Wcid, abode.Vendor.Guid), (here.Name, here.Keep, here.TripUnder, here.Price, here.Burden, here.Town, here.Vendor.Wcid, here.Vendor.Guid));
        }

        var platinum = ground.TownStockOf(8897)!;
        Assert.Equal(("Platinum Scarab", 15, 6, 15_500, 4, "Uziz", 1824u), (platinum.Name, platinum.Keep, platinum.TripUnder, platinum.Price, platinum.Burden, platinum.Town, platinum.Vendor.Wcid)); // ceil(1.55 x 10,000 - 0.1)
        var seller = ScrivenerCatalog.Default.ScarabSellers.Single(s => s.Wcid == 1824);
        Assert.Equal(15_500, (int)Math.Ceiling((seller.SellRate * seller.SaleOf(8897)!.Value) - 0.1)); // the catalog's seller row (Vendor.GetSellCost)
        Assert.Equal(5, ground.TownStock.Count);
    }

    [Fact]
    public void Levels_VI_and_VII_carry_the_foci_and_the_account_formula_and_each_component_is_bought()
    {
        var ground = Holding;
        Assert.Equal(Abode.Components.Where(c => c.SpellLevel == 6).OrderBy(c => c.Wcid), ground.Components.Where(c => c.SpellLevel == 6).OrderBy(c => c.Wcid));
        Assert.Equal(new uint[] { 741, 762, 772, 790, 1643, 1647, 1649, 8897, 20631 }, ground.Components.Where(c => c.SpellLevel == 7).Select(c => c.Wcid).Order()); // Ilservian's Flame: foci and the account test's (the dats, 2026-11-14)
        Assert.Equal(4, ground.Components.Single(c => c.SpellLevel == 7 && c.Wcid == 20631).PerCast);
        Assert.Equal(new[] { 6, 7 }, ground.Components.Select(c => c.SpellLevel).Distinct().Order());
        foreach (var c in ground.Components)
        {
            Assert.True(ground.Supplies.StockOf(c.Wcid) is not null || ground.TownStockOf(c.Wcid) is not null, $"{c.Name} {c.Wcid} is bought");
        }
    }

    [Fact]
    public void The_vendors_are_the_Abodes_Holtburg_rows_without_the_dens_scrivener_and_learn_line()
    {
        var ground = Holding;
        Assert.Equal(Abode.Vendors.Select(v => v.Wcid).Order(), ground.Vendors.Select(v => v.Wcid).Order());
        Assert.DoesNotContain(ground.Vendors, v => v.Wcid == 20214); // the Scrivener of Item Magic, the den's (D143's learn line)
        Assert.Empty(ground.Learn);
        Assert.Equal(new[] { 377u, 379u }, ground.Vendors.Single(v => v.Wcid == 715).Sells.Select(w => w.Wcid)); // Thelnoth sells the Potion of Healing now
        Assert.Equal(new StockLine(377, "Potion of Healing", 30, 6, 0), ground.Supplies.StockOf(377));
        Assert.DoesNotContain("TODO(m6-holding-supplies)", ground.Sources["supplies"], StringComparison.Ordinal);
        Assert.Contains("closes m6-holding-supplies", ground.Sources["supplies"], StringComparison.Ordinal);
        Assert.Contains("TODO(m6-derived-supplies)", ground.Sources["supplies"], StringComparison.Ordinal); // the burn is still the ladder's model, named
    }

    [Fact]
    public void The_plan_names_VI_from_60_and_VII_at_the_projections_crossing_not_the_dens_100()
    {
        var ground = Holding;
        Assert.Equal(new SpellPlanRow(60, 123, "Flame Bolt", 6), ground.PlanFor(60)); // M6 holding at 60 (the owner's decision, 2026-11-20): the rung, and so its first row, starts at 60
        Assert.Null(ground.PlanFor(59));
        Assert.Equal(6, ground.PlanFor(100)!.SpellLevel); // the den's "VII from 100" is gone
        Assert.Equal(new SpellPlanRow(124, 126, "Flame Bolt", 7), ground.PlanFor(124));
        Assert.DoesNotContain("TODO(m6-holding-spell-vii-crossing)", ground.Sources["spell_plan"], StringComparison.Ordinal);
        Assert.Contains("closes m6-holding-spell-vii-crossing", ground.Sources["spell_plan"], StringComparison.Ordinal);
        var rung = LadderLoader.WarMage.Rungs.Single(r => r.Key == "holding");
        for (var level = rung.Band.From; level <= rung.Band.To; level++)
        {
            Assert.NotNull(ground.PlanFor(level));
        }
    }

    [RequiresDatsFact]
    public void Each_plan_row_is_the_computation_at_every_level_of_the_Holdings_band()
    {
        var table = XpTableData.Load(fixture.Dats);
        var ladder = LadderLoader.WarMage;
        var ground = Holding;
        var rung = ladder.Rungs.Single(r => r.Key == "holding");
        var carried = ground.Components.Select(c => c.SpellLevel).ToHashSet();
        for (var level = rung.Band.From; level <= rung.Band.To; level++)
        {
            var skill = SpellPlan.ProjectedWarBase(table, level);
            var want = SpellPlan.LevelAt(skill, ladder.PlanCastChance);
            want = carried.Contains(want) ? want : carried.Where(l => l > want).Min(); // D135: a level the kit does not carry (V at 65 and 66) is the lowest it carries above it
            var row = ground.PlanFor(level);
            Assert.True(row?.SpellLevel == want, string.Create(CultureInfo.InvariantCulture, $"level {level}: base {skill} casts {want} at {ladder.PlanCastChance}, the row says {row?.SpellLevel.ToString(CultureInfo.InvariantCulture) ?? "nothing"}"));
        }

        output.WriteLine(string.Join(", ", ground.SpellPlan.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.MinLevel}-{r.MaxLevel} {r.SpellLevel}"))));
    }
}
