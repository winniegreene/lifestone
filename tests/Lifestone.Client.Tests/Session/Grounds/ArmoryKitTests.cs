using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Travel;
using Lifestone.Client.Session.Vitals;
using Xunit;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 14 armory (the owner, 2026-11-14: try the Tusker Armory as the climber's next ground; 2026-11-13: a profile holds facts only, every fight and kit number derived live): the Tusker Armory's profile. Proved: its two Armored Tusker
/// weenies carry their facts (health, Magic Defense, experience, the bludgeon attack the derived fight line reads); its fight block is only the owner's general rules and the place's awareness, with no hand-sized number; its home,
/// shops, supplies, components, plan and town stock are the Holding's (the same rung's general rule), with no loot block; its entry and exit are the world's; and its approach records the island's hostiles of its corridor, whose
/// crossing at the climber's live state of level 62 is survivable and paid where the Holding's is not.
/// </summary>
public sealed class ArmoryKitTests(ITestOutputHelper output)
{
    private static HuntingGround Armory => GroundCatalog.Get("tusker-armory");

    private static HuntingGround Holding => GroundCatalog.Get("tusker-holding");

    [Fact]
    public void The_two_Armored_Tusker_weenies_carry_their_facts_and_the_fight_block_is_only_the_general_rules()
    {
        var ground = Armory;
        Assert.Equal(("Tusker Armory", GroundKind.Dungeon, new LevelBand(100, 100)), (ground.Name, ground.Kind, ground.LevelBand));
        Assert.Equal([11540u, 22611u], ground.Targets.Select(t => t.Wcid));
        Assert.All(ground.Targets, t =>
        {
            Assert.Equal(("Armored Tusker", 100, GroundRole.Target, false), (t.Name, t.Level, t.Role, t.KillFirst));
            Assert.Equal((435, 275, 80_000, 0x4u, 90, 0.75, 230), (t.MaxHealth, t.MagicDefence, t.Xp, t.DamageTypes, t.DVal, t.DVar, t.Attributes!.Strength));
        });

        var hitter = ground.HardestHitter(out var types);
        Assert.Equal(("Armored Tusker", 90, 0.75, 230, 0x4u), (hitter!.Name, hitter.DVal, hitter.DVar, hitter.Strength, types));
        Assert.InRange(hitter.Hit, 164, 165); // 90 x 0.625 x 2.925: a Rampager's 293 is the Holding's
        Assert.Null(ground.FightRetreatHealthPercent);
        Assert.Null(ground.FightRingMinTargets);
        Assert.Equal(Holding.PullRest, ground.PullRest); // the owner's general pull rules and the awareness 25 (the Armored Tuskers' float 31, as the Rampagers')
        Assert.Null(ground.Loot); // the Quarters', the Abode's and the Barracks' shape: the Holding's loot block is the den's
        Assert.Equal(67, ground.Respawn.Slots.Count);
        Assert.Equal(5, ground.Respawn.Slots.Count(s => s.Wcid == 22611)); // the tusk carriers
        Assert.Equal((300.0, 60.0), (ground.Respawn.Generators.Single().DelaySeconds, ground.Respawn.Generators.Single().TickSeconds));
    }

    [Fact]
    public void Home_shops_supplies_components_plan_and_town_stock_are_the_Holdings_by_the_same_rung()
    {
        var (ground, holding) = (Armory, Holding);
        Assert.Equal(holding.Home, ground.Home);
        Assert.Equal("Holtburg", ground.Home.Town);
        Assert.Equal(holding.Vendors.Select(v => (v.Wcid, v.Guid, string.Join(",", v.Sells.Select(w => w.Wcid)))), ground.Vendors.Select(v => (v.Wcid, v.Guid, string.Join(",", v.Sells.Select(w => w.Wcid)))));
        Assert.Equal(holding.SpellPlan, ground.SpellPlan);
        Assert.Equal(holding.Components, ground.Components);
        Assert.Equal(holding.Supplies.Stock, ground.Supplies.Stock);
        Assert.Empty(ground.Supplies.Kit);
        Assert.Equal((holding.Supplies.Purse.Coin, holding.Supplies.BurdenTripPercent), (ground.Supplies.Purse.Coin, ground.Supplies.BurdenTripPercent));
        Assert.Equal(holding.TownStock.Select(t => (t.Wcid, t.Keep, t.TripUnder, t.Price, t.Town, t.Vendor.Guid)), ground.TownStock.Select(t => (t.Wcid, t.Keep, t.TripUnder, t.Price, t.Town, t.Vendor.Guid)));
        Assert.All(ground.Supplies.Stock, line => Assert.True(ground.VendorSelling(line.Wcid) is not null, $"{line.Name} is sold at home: no carried line can stop a climber never granted anything"));
        Assert.Contains("TODO(m6-derived-supplies)", ground.Sources["supplies"], StringComparison.Ordinal); // the burn is the ladder's model, named

        var rung = LadderLoader.WarMage.Rungs.Single(r => r.Key == "holding");
        for (var level = rung.Band.From; level <= rung.Band.To; level++)
        {
            Assert.NotNull(ground.PlanFor(level));
        }
    }

    [Fact]
    public void The_entry_and_the_exit_are_the_worlds_and_the_entry_is_not_tieable()
    {
        var ground = Armory;
        var entry = ground.Entry!;
        Assert.Equal((22657u, 0x7EF7F000u, 0xEF7F000Cu, 50, (int?)null, false), (entry.Wcid, entry.Guid, entry.Cell, entry.MinLevel, entry.MaxLevel, entry.Tieable));
        Assert.Equal(new System.Numerics.Vector3(31.9169f, 92.1597f, 12.005f), entry.Origin);
        Assert.Null(ground.Portal); // NoRecall (PortalBitmask 0x31): no tie, every trip back is the run
        Assert.Equal((0x594801ABu, new System.Numerics.Vector3(40f, -170f, 0.005f)), (ground.Arrival.Cell, ground.Arrival.Origin));
        Assert.Equal((GroundExitKind.Portal, 22658u, 0x59480124u, 0xEF7F000Bu), (ground.Exit!.Kind, ground.Exit.Wcid, ground.Exit.Cell, ground.Exit.Destination));
        Assert.True(ground.IsNoGo(ground.Exit.Cell));
        Assert.True(entry.Admits(50) && !entry.Admits(49));
    }

    [Fact]
    public void The_approach_records_the_corridors_island_hostiles_as_facts()
    {
        var approach = Armory.Approach!;
        Assert.Equal(new ushort[] { 0xF07E, 0xF07F, 0xEF7F, 0xEF7E }, approach.Landblocks);
        Assert.Equal((39, 1.4154), (approach.Encounters, approach.PerRoll));
        Assert.Equal([7184u, 11540u, 1629u, 4244u, 22746u], approach.Hostiles.Select(h => h.Wcid));
        Assert.All(approach.Hostiles, h => Assert.True(h.HasCrossingFacts && h.Hitter is not null, h.Name));
        Assert.Equal(0.6431, approach.Hostiles.Sum(h => h.PerRoll!.Value), 4);
        Assert.Equal((6, 0.38, 335), (approach.Hostiles.Single(h => h.Wcid == 22746).Pack, approach.Hostiles.Single(h => h.Wcid == 22746).Resist!["fire"], approach.Hostiles.Single(h => h.Wcid == 22746).MagicDefence)); // the Feral Carenzi camp's six
        Assert.DoesNotContain(approach.Hostiles, h => h.Wcid is 27800 or 22053 or 10810 or 11541 or 22747 or 22506); // the Holding's Extreme Mix creatures stand in none of these landblocks

        // each hostile's facts are the Holding's or the Barracks' record of the same weenie
        var recorded = Holding.Approach!.Hostiles.Concat(GroundCatalog.Get("tusker-barracks").Approach!.Hostiles).GroupBy(h => h.Wcid).ToDictionary(g => g.Key, g => g.First());
        Assert.All(approach.Hostiles, h =>
        {
            var r = recorded[h.Wcid];
            Assert.Equal((r.Level, r.MaxHealth, r.DamageTypes, r.DVal, r.DVar, r.Strength, r.MagicDefence, r.AwarenessMetres, r.Xp), (h.Level, h.MaxHealth, h.DamageTypes, h.DVal, h.DVar, h.Strength, h.MagicDefence, h.AwarenessMetres, h.Xp));
            Assert.Equal(r.Resist, h.Resist);
        });

        // the drop and the entry front lie in it
        Assert.Same(approach, Armory.ApproachAt(0xF07E0040));
        Assert.Same(approach, Armory.ApproachAt(Armory.Entry!.FrontCell));
        Assert.Null(Armory.ApproachAt(0xEA7B0001)); // the Holding's entry is not on this run
    }

    /// <summary>
    /// The crossing as the ladder's way out judges it (<see cref="ApproachCheck"/>, <see cref="ApproachCost"/>), at the climber's live state of 2026-11-14 (level 62: 197 health, War Magic 352 buffed, Mana Conversion 254, 387 mana regenerating
    /// about 0.6 a second, Flame Bolt VI, the way out's reserve 120, a run of 6.25 m/s, her armour 0.045 under the bot's protections): every camp of the corridor is survivable, and the 345 m run from the Barracks drop is paid where the
    /// Holding's 1,507 m is not. The model's numbers are printed; the live crossing is the trial's to measure.
    /// </summary>
    [Fact]
    public void At_her_level_62_state_the_crossing_is_survivable_and_paid_where_the_Holdings_is_not()
    {
        var approach = Armory.Approach!;
        var survivable = ApproachCheck.Judge(Armory.Key, approach, 197, recallReady: true, Her);
        output.WriteLine(survivable.Why);
        Assert.True(survivable.Survivable, survivable.Why);

        var route = TravelRoutes.Default.FirstAdmitted(Armory.Key, 65, Armory.Entry!.MinLevel, Armory.Entry.MaxLevel);
        Assert.Equal(("by-facility-hub-barracks", 345.1), (route.Route!.Key, route.Route.Run!.Metres));
        var cost = ApproachCost.Judge(Armory.Key, approach, route.Route.Run.Metres, Input(Armory))!;
        output.WriteLine(cost.Why);
        Assert.False(cost.Costly, cost.Why);
        Assert.True(cost.AtPortal > 120, cost.Why);
        var holding = ApproachCost.Judge(Holding.Key, Holding.Approach!, 1506.5, Input(Holding))!;
        Assert.True(holding.Costly, holding.Why);
        Assert.True(cost.Creatures < holding.Creatures / 3, $"{cost.Creatures:0.0} creatures against the Holding's {holding.Creatures:0.0}");
    }

    /// <summary>Her factor against the worst bit of <paramref name="types"/>: her live armour 0.045 times the bot's protection (bludgeon 0.315 under the Boon of the Mace Turner, the others 0.35; <c>RetreatAttackersTests</c>).</summary>
    private static (double Factor, uint Type) Her(uint types)
    {
        var (best, bit) = (-1.0, 0u);
        for (var b = 1u; b <= 0x40; b <<= 1)
        {
            if (((types == 0 ? 0x7Fu : types) & b) != 0 && 0.045 * (b == 0x4 ? 0.315 : 0.35) is var f && f > best)
            {
                (best, bit) = (f, b);
            }
        }

        return (best, bit);
    }

    /// <summary>Her live input at level 62 (<c>ApproachCostTests</c>' numbers), the rates the Quarters' rung and the holding rung's over <paramref name="ground"/>'s targets.</summary>
    private static CrossingInput Input(HuntingGround ground)
    {
        var ladder = LadderLoader.WarMage;
        Assert.True(WarSpellData.TryGet(85, out var row)); // Flame Bolt VI
        var previous = ApproachCost.RungRate(ladder.Rungs.Single(r => r.Key == "quarters"), GroundCatalog.Get("tusker-quarters"), out var previousWhy);
        var rung = ApproachCost.RungRate(ladder.Rungs.Single(r => r.Key == "holding"), ground, out var rungWhy);
        return new CrossingInput(352, 254, 387, 0.6, 120, 6.25, new CrossingBolt("Flame Bolt VI", "fire", row, 250, 30, ladder.VitalsPolicy.BoltSeconds(6)), BuffOptions.OneBuffCycleSeconds, previous, rung, previousWhy + "; " + rungWhy);
    }
}
