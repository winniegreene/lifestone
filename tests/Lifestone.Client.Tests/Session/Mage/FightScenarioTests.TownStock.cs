using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14 (m6-goldscarab; live 2026-11-12: "war pick: the pack does not pay for level V: the fight starts at IV ... Flame Bolt V needs Gold Scarab 687, which the pack does not hold"): on a ground whose profile names the Gold Scarab
/// as town stock (bought at Uziz on the way out; no stock line, no stop line), the fight casts level V while the pack holds the scarab and level IV with none, and nothing stops. A war option whose formula burns the scarab is in the
/// pick while the pack holds a cast's worth and out of it with none, read again every few seconds.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint GoldScarabWcid = 687;

    /// <summary>The default ground without its carried Gold and Pyreal Scarab lines, with the Tusker Cave's Uziz town stock instead: the scarabs are bought in a town, as on the climber's grounds.</summary>
    private static HuntingGround TownStockGround()
    {
        var den = GroundCatalog.Get(FightOptions.DefaultGround);
        var uziz = GroundCatalog.Get("tusker-cave").TownStock.Where(l => l.Town == "Uziz" && !l.FeeOnly).ToList();
        Assert.Equal([GoldScarabWcid, 690u], uziz.Select(l => l.Wcid));
        return den with { Supplies = den.Supplies with { Stock = [.. den.Supplies.Stock.Where(l => l.Wcid is not (GoldScarabWcid or 690))] }, TownStock = uziz };
    }

    /// <summary>A fight capped at level V (<c>--fight-max-level 5</c>) on the town stock ground: Flame Bolt V burns the Gold Scarab (the rig's formula of the level's own scarab).</summary>
    private static Rig TownStockRig() => FormulaRig(new FightOptions { Kills = 1, Seconds = 600, MaxLevel = 5, GroundForTest = TownStockGround() });

    [Fact]
    public void With_the_Gold_Scarab_of_the_town_stock_in_the_pack_the_fight_casts_level_V()
    {
        var five = BoltLine[4];
        var r = TownStockRig();
        Assert.True(ComponentWeenies.TryGetByComponent(r.Table[five].Formula[0], out var scarab) && scarab.Wcid == GoldScarabWcid);
        Stock(r, BoltLine); // the pack pays for every level: the Gold Scarab among them
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.NotEmpty(r.Answered);
        Assert.All(r.Answered, id => Assert.Equal(five, id));
        Assert.Equal(0, LinesWith(r, "war pick: the pack does not pay"));
        Assert.Equal(0, LinesWith(r, "is dropped for the fight"));
    }

    [Fact]
    public void With_no_Gold_Scarab_in_the_pack_the_fight_stays_at_level_IV_says_why_once_and_nothing_stops()
    {
        var five = BoltLine[4];
        var four = BoltLine[3];
        var r = TownStockRig();
        Stock(r, BoltLine.Take(4)); // levels I to IV: no Gold Scarab
        r.RefusedBy[five] = 0x0400; // ACE would refuse it: it is never sent
        r.Add(SlaveA, TuskerSlave, Den, 30);
        var scenario = r.Scenario(new FakeWalk());

        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario)); // the kill is made: no scarab stops the run

        Assert.NotEmpty(r.Answered);
        Assert.All(r.Answered, id => Assert.Equal(four, id));
        Assert.Equal(1, LinesWith(r, "war pick: the pack does not pay for level V: the fight starts at IV ("));
        Assert.Equal(1, LinesWith(r, "needs Gold Scarab 687, which the pack does not hold"));
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.Tally.Misses);
    }

    [Fact]
    public void A_war_option_that_burns_the_town_stocks_scarab_is_left_out_with_none_in_the_pack_and_returns_when_the_pack_holds_a_casts_worth()
    {
        const uint ring = 1785;
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, ring], characterLevel: 80, fight: new FightOptions { GroundForTest = TownStockGround() });
        // The ring's formula here is the Gold Scarab's (component 5) and, on the foci formula, four Prismatic Tapers, a stock line of the ground.
        r.Table[ring] = WorldActionRig.Spell(ring, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Cassius' Ring of Fire", School = MagicSchool.WarMagic, Power = 200, BaseMana = 40, BaseRangeConstant = 4.5f, Formula = [5, 0, 0, 0, 0, 0, 0, 0] };
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40, hitsToKill: 1000);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.NotEmpty(r.CastTargets); // the bolt goes on
        Assert.DoesNotContain("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(1, LinesWith(r, "ring: left out, the kit does not carry its components: Gold Scarab (wcid 687) x1 a cast is bought at Archmage Sammam al-Ashiz in Uziz (the town stock of "));
        Assert.Equal(1, LinesWith(r, "and the pack holds 0"));
        Assert.Equal(0, LinesWith(r, "components: Gold Scarab 687 (town stock"));

        HoldStack(r, 0x80007687, GoldScarabWcid, 3); // the stop at Uziz bought some
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.Contains("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(1, LinesWith(r, "components: Gold Scarab 687 (town stock, bought at Archmage Sammam al-Ashiz in Uziz): the pack holds 3: the spells that burn it are castable (m6-goldscarab)"));
    }

    [Fact]
    public void A_ground_with_a_stock_line_of_the_scarab_is_judged_as_before_by_its_stop_line()
    {
        // The default ground carries the Gold Scarab as a stock line (stop under 2): the war option is judged on that line and its floor (D148), as before, and no town stock is read.
        const uint ring = 1785;
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, ring], characterLevel: 80);
        Assert.Empty(r.Fight.GroundProfile.TownStock);
        Assert.Equal(2, r.Fight.GroundProfile.Supplies.StockOf(GoldScarabWcid)!.StopUnder);
        r.Table[ring] = WorldActionRig.Spell(ring, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Cassius' Ring of Fire", School = MagicSchool.WarMagic, Power = 200, BaseMana = 40, BaseRangeConstant = 4.5f, Formula = [5, 0, 0, 0, 0, 0, 0, 0] };
        HoldStack(r, 0x80007687, GoldScarabWcid, 9); // over the carried line's floor
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40, hitsToKill: 1000);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));
        for (var i = 0; i < 40; i++)
        {
            r.Frame(scenario);
        }

        Assert.Contains("Cassius' Ring of Fire", scenario.WarOptionNames);
        Assert.Equal(0, LinesWith(r, "town stock"));
    }
}
