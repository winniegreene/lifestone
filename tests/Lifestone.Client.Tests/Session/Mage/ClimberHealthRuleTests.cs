using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 R (the owner's correction): the climber's health floor. What these prove: a rung's want is <see cref="Ladder.HealthHits"/> of ACE's mean hit of its hardest hitter, the road's on the Burrow's rung and no other; each purchase goes to whichever of Max Health
/// and Endurance prices one health cheaper at that moment; the pay limit binds and what is short is the line's; the path is one path however far it runs; the planner buys the floor first and each stat's share buys on from it; with the retail dats, the numbers
/// the owner asked for at levels 5, 10, 15, 20 and 30, where each rung's want is paid, the bolt plan unmoved, and <see cref="MageTemplate"/>'s plans untouched. The dat tests skip without the dats.
/// </summary>
public sealed class ClimberHealthRuleTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly XpTableData Synthetic = ScriptedMage.SyntheticTable();
    private static readonly Ladder WarMage = LadderLoader.WarMage;

    /// <summary>The levels where the floor delays the 95% bolt by a level or two (the rest of the climb casts the same bolt; every rung's plan stands).</summary>
    private static readonly int[] LateBolts = [123]; // M6 item 14: the climber's split (the named list; 5% for each health stat) leaves one level whose planned bolt comes later than the plan with no floor; before it: 16, 27, 42, 67, 68, 123, 124, 125 (M6 7 finish: the per-stat 5% aim and the 10% cap leave more to the magic skills than the old 30% pair, so 8, 17, 28, 43, 44, 69 and 70 keep their bolt; 124 and 125 join (the cost is at most 5 points))

    [Theory]
    [InlineData(5, "outskirts", 71, 142, "Goldenback Tusker", true)] // M6 14 (D135): the next rung is the Burrow at 10 by the Refuge's walk: the outskirts name that road's Goldenback, as the Burrow does
    [InlineData(9, "outskirts", 71, 142, "Goldenback Tusker", true)]
    [InlineData(10, "burrow", 71, 142, "Goldenback Tusker", true)] // the road's two hits (142) outweigh the Female Tuskers' pack of 3 (136): the island road of M6 7 R again, met alone
    [InlineData(14, "burrow", 71, 142, "Goldenback Tusker", true)]
    [InlineData(15, "lodge", 36, 144, "Male Tusker", false)] // the hub's gate: no road; M6 7 AY: the entry trio
    [InlineData(20, "lodge", 36, 144, "Male Tusker", false)]
    [InlineData(21, "cave", 65, 390, "Tusker Crimsonback", false)] // M6 7 AY: a pack of 5
    [InlineData(24, "cave", 65, 390, "Tusker Crimsonback", false)]
    [InlineData(25, "abode", 82, 164, "Tusker Redeemer", false)] // the Abode from 25
    [InlineData(41, "abode", 82, 164, "Tusker Redeemer", false)]
    [InlineData(42, "quarters", 102, 204, "Tusker Slave", false)] // the Quarters from 42
    [InlineData(64, "quarters", 102, 204, "Tusker Slave", false)]
    public void A_rungs_want_is_the_packs_hits_times_the_mean_hit_of_its_hardest_hitter(int level, string rung, int hit, int want, string hitter, bool road)
    {
        var need = ClimberHealthRule.NeedAt(WarMage, level);
        Assert.NotNull(need);
        Assert.Equal((rung, hit, want, hitter, road, ClimberHealthRule.PackHits(2, need.Pack), 5), (need.Rung, need.Hit, need.Want, need.Hitter, need.Road, need.Hits, need.PayPercent));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)] // the quest rung
    [InlineData(65)] // the Holding (from 65, D135): the Rampager's hit is not for sale
    [InlineData(75)]
    [InlineData(126)]
    [InlineData(127)] // past the top
    public void A_rung_with_no_health_asks_for_nothing(int level) => Assert.Null(ClimberHealthRule.NeedAt(WarMage, level));

    [Theory]
    [InlineData(25, 0.75, 160, 34)] // Female Tusker: 25 x 0.625 x 2.155 = 33.67
    [InlineData(40, 0.75, 200, 65)] // Tusker Crimsonback: 40 x 0.625 x 2.595 = 64.9
    [InlineData(40, 0.75, 220, 71)] // Goldenback Tusker: 40 x 0.625 x 2.815 = 70.4
    [InlineData(10, 0.75, 20, 7)] // the attribute modifier floors at 1 under Strength 55
    public void The_hit_is_ACEs_mean_uncritical_unarmoured_hit(int dVal, double dVar, int strength, int hit) => Assert.Equal(hit, new LadderHitter("x", 1, dVal, dVar, strength).Hit);

    [Fact]
    public void Base_health_is_half_the_Endurance_rounded_up_plus_the_ranks()
    {
        Assert.Equal(5, ClimberHealthRule.BaseHealth(10, 0)); // the fresh climber: 'vitals=1:0+0/5' in the log
        Assert.Equal(6, ClimberHealthRule.BaseHealth(11, 0));
        Assert.Equal(6, ClimberHealthRule.BaseHealth(12, 0));
        Assert.Equal(16, ClimberHealthRule.BaseHealth(13, 9));
    }

    [Fact]
    public void Over_a_flat_table_the_cheaper_stat_buys_and_a_tie_of_price_goes_to_Max_Health()
    {
        // The synthetic table: a vital rank 73, an attribute rank 110. A health by Endurance costs 110 on an even value and 220 from an odd one: Max Health is cheaper at each purchase.
        var buy = ClimberHealthRule.Buy(Synthetic, 20, 10, 0, 0, ulong.MaxValue);
        Assert.Equal((15, 0, 20, "Max Health"), (buy.HealthRanks, buy.EnduranceRanks, buy.Health, buy.Buyer));
        Assert.Equal((73ul, 110ul), (buy.HealthPrice, buy.EndurancePrice));
        Assert.False(buy.Limited);
        Assert.Equal(220ul, ClimberHealthRule.Buy(Synthetic, 20, 11, 0, 0, ulong.MaxValue).EndurancePrice); // from an odd value two ranks buy the next health
    }

    [Fact]
    public void The_pay_limit_ends_the_buying_and_says_so_and_a_character_over_the_want_buys_nothing()
    {
        var limited = ClimberHealthRule.Buy(Synthetic, 100, 10, 0, 0, 730);
        Assert.Equal((10, 5, 18, true), (limited.HealthRanks, limited.EnduranceRanks, limited.Health, limited.Limited)); // M6 7 finish: the budget is each stat's own: 730 buys ten Max Health ranks and Endurance as far as its own 730 goes
        Assert.Equal(1280ul, limited.Xp); // 730 for Max Health and 550 for Endurance
        var held = ClimberHealthRule.Buy(Synthetic, 10, 10, 2, 6, ulong.MaxValue);
        Assert.Equal((6, 2, "none"), (held.HealthRanks, held.EnduranceRanks, held.Buyer)); // ranks never fall
        Assert.Equal(30_000ul, ClimberHealthRule.Budget(100_000, 30));
        Assert.Equal(24ul, ClimberHealthRule.Budget(83, 30)); // the low digits count, rounded down
    }

    [Fact]
    public void The_line_names_the_hit_N_the_rung_the_buyer_and_the_prices()
    {
        // M6 7 AC: the dropped Burrow rung's need, built by hand: the line's road annotation is still the line's format (a rung may carry a road_hitter again)
        var need = ClimberHealthRule.NeedAt(WarMage, 10)! with { Rung = "burrow", Hit = 71, Want = 142, Hitter = "Goldenback Tusker", Road = true };
        var buy = new HealthBuy(29, 11, 40, 24_484, "Endurance", 901, 880, true);
        var line = ClimberHealthRule.Line(need, 16, buy);
        Assert.Equal("health: now=16 want=142 reach=40 short=102 why=2x71(Goldenback-Tusker,road) rung=burrow max_health_ranks=29 endurance_ranks=11 buyer=Endurance price_health=901 price_endurance=880 pay_limit=5% limited=yes: the floor raises health 16 -> 40 before the shares", line);
        Assert.EndsWith("held: no floor rank to buy", ClimberHealthRule.Line(need with { Want = 10 }, 16, HealthBuy.None), StringComparison.Ordinal);
    }

    [Fact]
    public void The_planner_lists_the_floor_first_and_each_stats_share_buys_on_from_it()
    {
        var floor = new HealthBuy(40, 10, 0, 0, "x", 0, 0, false);
        var plain = MageSpendingPlanner.Compute(Synthetic, 6_000, [], MageShares.Dance, MageBuild.Traditional);
        var plan = MageSpendingPlanner.Compute(Synthetic, 6_000, [], MageShares.Dance, MageBuild.Traditional, healthFloor: floor);
        Assert.Equal(("Max Health", 40, true), (plan.Targets[0].Name, plan.Targets[0].Ranks, plan.Targets[0].Floor));
        Assert.Equal(("Endurance", 10, true), (plan.Targets[1].Name, plan.Targets[1].Ranks, plan.Targets[1].Floor));
        var share = plan.Targets.Single(t => t.Name == "Max Health" && !t.Floor);
        Assert.True(share.Ranks >= 40, $"the share's ranks {share.Ranks} start at the floor's");
        Assert.True(plan.Targets.Single(t => t.Name == "War Magic").Ranks < plain.Targets.Single(t => t.Name == "War Magic").Ranks, "the floor comes out of what the shares split");
        Assert.Equal(6_000ul, plan.TotalXp);
        Assert.DoesNotContain(plain.Targets, t => t.Floor); // no floor, the plan of before
        Assert.Equal(plain.Spent + plain.Remainder, plan.Spent + plan.Remainder);
    }

    [Fact]
    public void A_floor_the_total_cannot_pay_is_bought_as_far_as_it_goes()
    {
        var plan = MageSpendingPlanner.Compute(Synthetic, 1_000, [], MageShares.Dance, MageBuild.Traditional, healthFloor: new HealthBuy(100, 100, 0, 0, "x", 0, 0, false));
        Assert.Equal(13, plan.Targets[0].Ranks); // 13 x 73 = 949
        Assert.True(plan.Spent <= 1_000);
    }

    [RequiresDatsFact]
    public void With_the_XpTable_each_purchase_goes_to_the_cheaper_price_of_a_health_and_both_buy()
    {
        var table = XpTableData.Load(fixture.Dats);
        var vital = table.VitalXpList;
        var attribute = table.AttributeXpList;
        Assert.Equal(73u, vital[1]); // the log's 'raise Max Health 0 -> 1 for 73', '1 -> 2 for 110', '2 -> 3, 148'
        Assert.Equal(183u, vital[2]);
        var (h, e) = (0, 0);
        var seen = new HashSet<string>();
        for (var want = 6; want <= 142; want++)
        {
            var buy = ClimberHealthRule.Buy(table, want, MageTemplate.LowAttribute, e, h, ulong.MaxValue);
            Assert.Equal(want, buy.Health);
            var cheaper = buy.HealthPrice <= buy.EndurancePrice ? "Max Health" : "Endurance";
            Assert.Equal(cheaper, buy.Buyer);
            seen.Add(buy.Buyer);
            (h, e) = (buy.HealthRanks, buy.EnduranceRanks);
        }

        Assert.Equal(["Endurance", "Max Health"], seen.Order(StringComparer.Ordinal));
        var whole = ClimberHealthRule.Buy(table, 142, MageTemplate.LowAttribute, 0, 0, ulong.MaxValue);
        Assert.Equal((whole.HealthRanks, whole.EnduranceRanks), (h, e)); // one path, however far it runs: a later rung's want extends it
        Assert.Equal((96, 81), (h, e));
        Assert.Equal(2_419_816ul, whole.Xp);
    }

    [RequiresDatsFact]
    public void The_numbers_the_owner_asked_for_at_levels_5_10_15_20_and_30()
    {
        var table = XpTableData.Load(fixture.Dats);
        int Ranks(SpendingPlan plan, MageStatKind kind, uint id) => plan.Targets.Where(t => t.Kind == kind && t.Id == id).Select(t => t.Ranks).DefaultIfEmpty(0).Max();
        int Health(SpendingPlan plan) => ClimberHealthRule.BaseHealth(MageTemplate.LowAttribute + (uint)Ranks(plan, MageStatKind.Attribute, MageTemplate.Endurance), Ranks(plan, MageStatKind.Vital, MageTemplate.MaxHealth));
        uint War(SpendingPlan plan) => (uint)Math.Round((200 + Ranks(plan, MageStatKind.Attribute, MageTemplate.Focus) + Ranks(plan, MageStatKind.Attribute, MageTemplate.Self)) / (double)MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero) + SpellPlan.SpecializedInitLevel + (uint)Ranks(plan, MageStatKind.Skill, MageTemplate.WarMagic);

        var rows = new[]
        {
            // M6 item 14 (the owner's standing rule, read against the allocation of 2026-11-11): Max Health and Endurance are held to 5% of the experience earned each, floor and share together (the share used to buy on to the 10% line: 26, 44, 48, 75 and 115 health at
            // levels 10 to 30), and the climber's split gives her magic what Jump, Magic Defense, Run, Arcane Lore, Coordination and Quickness took: War Magic is at or within 2 of the plan with no floor at every one of these levels (it was 3 to 5 under from 14 on).
            (Level: 5, HealthBefore: 9, HealthAfter: 10, Mh: 3, En: 3, WarBefore: 79u, WarAfter: 81u, Bolt: 1),
            (Level: 10, HealthBefore: 16, HealthAfter: 23, Mh: 13, En: 10, WarBefore: 109u, WarAfter: 110u, Bolt: 2),
            (Level: 14, HealthBefore: 26, HealthAfter: 39, Mh: 24, En: 19, WarBefore: 134u, WarAfter: 133u, Bolt: 2),
            (Level: 15, HealthBefore: 29, HealthAfter: 43, Mh: 27, En: 22, WarBefore: 139u, WarAfter: 139u, Bolt: 2),
            (Level: 20, HealthBefore: 46, HealthAfter: 68, Mh: 44, En: 37, WarBefore: 166u, WarAfter: 164u, Bolt: 3), // M6 7 AY: the pack's want; M6 7 finish: held at the 5% aim of each stat
            (Level: 30, HealthBefore: 84, HealthAfter: 109, Mh: 71, En: 65, WarBefore: 203u, WarAfter: 203u, Bolt: 4),
        };
        foreach (var row in rows)
        {
            var total = table.XpForLevel(row.Level);
            var shares = MageTemplate.SharesAt(row.Level);
            var before = MageSpendingPlanner.Compute(table, total, [], shares, MageBuild.Traditional);
            var after = MageSpendingPlanner.Compute(table, total, [], MageTemplate.SharesFor(row.Level, climber: true), MageBuild.Traditional, healthFloor: ClimberHealthRule.ProjectedBuy(table, WarMage, row.Level), cap: WarMage.StatCap); // M6 7 finish: the ladder's character is planned with its cap
            var (warBefore, warAfter) = (War(before), War(after));
            var (boltBefore, boltAfter) = (SpellPlan.LevelAt(warBefore, WarMage.PlanCastChance), SpellPlan.LevelAt(warAfter, WarMage.PlanCastChance));
            var power = SpellPlan.WarLinePowers[boltAfter - 1];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"level {row.Level}: health {Health(before)} -> {Health(after)}; Max Health {Ranks(before, MageStatKind.Vital, MageTemplate.MaxHealth)} -> {Ranks(after, MageStatKind.Vital, MageTemplate.MaxHealth)}; Endurance {Ranks(before, MageStatKind.Attribute, MageTemplate.Endurance)} -> {Ranks(after, MageStatKind.Attribute, MageTemplate.Endurance)}; War Magic {warBefore} -> {warAfter}; bolt {boltBefore} -> {boltAfter}; chance {SpellPlan.Chance(warBefore, power):0.0000} -> {SpellPlan.Chance(warAfter, power):0.0000}"));
            Assert.Equal((row.HealthBefore, row.HealthAfter), (Health(before), Health(after)));
            Assert.Equal((row.Mh, row.En), (Ranks(after, MageStatKind.Vital, MageTemplate.MaxHealth), Ranks(after, MageStatKind.Attribute, MageTemplate.Endurance)));
            Assert.Equal((row.WarBefore, row.WarAfter), (warBefore, warAfter));
            Assert.Equal((row.Bolt, row.Bolt), (boltBefore, boltAfter)); // the cost in kill speed: not one of these levels loses its bolt
        }
    }

    [RequiresDatsFact]
    public void The_cost_in_War_Magic_is_at_most_ten_points_and_the_bolt_comes_later_at_a_few_named_levels()
    {
        var table = XpTableData.Load(fixture.Dats);
        var late = new List<int>();
        for (var level = 1; level <= WarMage.TopLevel; level++)
        {
            var plan = MageSpendingPlanner.Compute(table, table.XpForLevel(level), [], MageTemplate.SharesAt(level), MageBuild.Traditional);
            int Ranks(MageStatKind kind, uint id) => plan.Targets.Where(t => t.Kind == kind && t.Id == id).Select(t => t.Ranks).DefaultIfEmpty(0).Max();
            var before = (uint)Math.Round((200 + Ranks(MageStatKind.Attribute, MageTemplate.Focus) + Ranks(MageStatKind.Attribute, MageTemplate.Self)) / (double)MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero) + SpellPlan.SpecializedInitLevel + (uint)Ranks(MageStatKind.Skill, MageTemplate.WarMagic);
            var after = SpellPlan.ProjectedWarBase(table, level);
            Assert.True((int)before - (int)after <= 10, $"level {level}: {before} -> {after}"); // signed: under the cap a level can plan above the no-floor plan (level 5: 79 -> 81)
            if (SpellPlan.LevelAt(after, WarMage.PlanCastChance) < SpellPlan.LevelAt(before, WarMage.PlanCastChance))
            {
                late.Add(level);
            }
        }

        output.WriteLine("levels whose planned bolt comes later: " + string.Join(", ", late));
        Assert.Equal(LateBolts, late);
    }

    /// <summary>
    /// M6 7 AY: the pack's want at each rung up to the Cave, against the pay limit: what the projection reaches at the band's ends and the level the limit would pay the whole want. M6 14 (D135, the study's bands): the island road's 142 binds
    /// the outskirts and the Burrow, and the projection reaches it only in the Quarters' band; the Lodge's 144 after it; the Cave's 390 never.
    /// </summary>
    [RequiresDatsFact]
    public void The_packs_want_up_to_the_Cave_and_where_the_pay_limit_blocks_it()
    {
        var table = XpTableData.Load(fixture.Dats);
        var rows = new (string Rung, int Pack, int Want, int From, int HealthFrom, int To, int HealthTo, int PaidAt)[]
        {
            ("outskirts", 1, 142, 5, 10, 9, 20, 43), // M6 14 (D135): the Burrow's road's Goldenback, met alone
            ("burrow", 1, 142, 10, 23, 14, 39, 43), // the road outweighs the Female Tuskers' pack of 3 (136)
            ("lodge", 3, 144, 15, 43, 20, 68, 44),
            ("cave", 5, 390, 21, 72, 24, 86, 0), // 7,996,103,161 experience: no level pays it
        };
        foreach (var row in rows)
        {
            var need = ClimberHealthRule.NeedAt(WarMage, row.From)!;
            var whole = ClimberHealthRule.Buy(table, need.Want, MageTemplate.LowAttribute, 0, 0, ulong.MaxValue);
            var paidAt = Enumerable.Range(row.From, WarMage.TopLevel + 1 - row.From).FirstOrDefault(l => ClimberHealthRule.ProjectedBuy(table, WarMage, l).Health >= need.Want); // M6 7 finish: the first level the projection (each stat's own 5% aim) reaches the want; 0 when none does
            var (from, to) = (ClimberHealthRule.ProjectedBuy(table, WarMage, row.From).Health, ClimberHealthRule.ProjectedBuy(table, WarMage, row.To).Health);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{row.Rung}: pack {need.Pack}, want {need.Want} = {need.Hits}x{need.Hit}, costs {whole.Xp:N0}; health {from} at {row.From}, {to} at {row.To}; the limit pays it at level {paidAt}"));
            Assert.Equal(row, (need.Rung, need.Pack, need.Want, row.From, from, row.To, to, paidAt));
        }

        Assert.Equal(1, ClimberHealthRule.NeedAt(WarMage, 25)!.Pack); // TODO(m6-pack-past-the-cave): the Abode on still counts one attacker (M6 14: the Temple 20-40's rung, which did from 32, left the ladder)
        Assert.Equal(5, ClimberHealthRule.NeedAt(WarMage, 24)!.Pack); // the Cave's pack holds to its band's end, 24 (D135)
        Assert.Equal(118, ClimberHealthRule.ProjectedBuy(table, WarMage, 33).Health); // M6 7 finish: 136 at level 40: the 5% aim of each stat, not the old 30% pair, paces it (the Cave's 390 binds the limit as the Temple's 142 did)
        Assert.Equal(136, ClimberHealthRule.ProjectedBuy(table, WarMage, 40).Health);
    }

    [Theory]
    [InlineData(2.0, 1, 2.0)] // one attacker: the rule of before
    [InlineData(2.0, 3, 4.0)] // the hardest hitter's two hits and one each from the two others
    [InlineData(2.0, 5, 6.0)]
    [InlineData(3.0, 3, 7.0)]
    [InlineData(2.0, 0, 2.0)] // no pack is still one attacker
    public void The_pack_formula_is_N_plus_the_others_hits_in_the_same_window(double n, int pack, double hits) => Assert.Equal(hits, ClimberHealthRule.PackHits(n, pack));

    [Fact]
    public void The_heavier_of_the_rungs_pack_and_its_road_decides_and_the_line_names_it()
    {
        // M6 14 (D135): the Burrow at 10 to 14 is reached by the Refuge's walk: the road's Goldenback, met alone (2 x 71 = 142), outweighs the Female Tuskers' pack of 3 (4 x 34 = 136)
        var road = ClimberHealthRule.NeedAt(WarMage, 10)!;
        Assert.Equal(("Goldenback Tusker", 1, true, 142), (road.Hitter, road.Pack, road.Road, road.Want)); // a road's hitter is met alone
        Assert.Contains("why=2x71(Goldenback-Tusker,road) rung=burrow", ClimberHealthRule.Line(road, 40, HealthBuy.None), StringComparison.Ordinal);
        var pack = ClimberHealthRule.NeedAt(WarMage, 15)!; // the Lodge by the hub: no road, the entry trio
        Assert.Equal(("Male Tusker", 3, false, 144), (pack.Hitter, pack.Pack, pack.Road, pack.Want));
        Assert.Contains("why=4x36(Male-Tusker,pack=3) rung=lodge", ClimberHealthRule.Line(pack, 75, HealthBuy.None), StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void Each_rungs_want_is_paid_inside_its_band_and_the_limit_ends_a_road_it_cannot_pay()
    {
        var table = XpTableData.Load(fixture.Dats);
        // M6 7 finish: the per-stat 5% aim paces the floor: 72 is reached at level 21. M6 14 (D135, the study's bands, each shorter): no rung's want is reached inside its band now (the Abode's 164 came at 54 inside 40-59; its band is
        // 25-41): the stat cap binds every band, and the floor spends what the cap allows.
        Assert.Equal(21, Enumerable.Range(1, WarMage.TopLevel).First(l => ClimberHealthRule.ProjectedBuy(table, WarMage, l).Health >= 72));
        foreach (var key in new[] { "outskirts", "burrow", "lodge", "cave", "abode", "quarters" })
        {
            var rung = WarMage.Rungs.Single(r => r.Key == key);
            var want = ClimberHealthRule.NeedAt(WarMage, rung.Band.From)!.Want;
            Assert.True(ClimberHealthRule.ProjectedBuy(table, WarMage, rung.Band.To).Health < want, $"{key}: its want {want} is not paid inside its band");
        }

        // The Burrow's road (M6 7 R, again from D135): its 142 is not paid inside 10-14: the pay limit binds (39 at level 14, the 5% aim of each stat).
        Assert.Equal(142, ClimberHealthRule.NeedAt(WarMage, 14)!.Want);
        Assert.Equal(39, ClimberHealthRule.ProjectedBuy(table, WarMage, 14).Health);

        // The limit's purpose (the Burrow's road): a Goldenback's 142 prices 2.4M and level 14 has earned 274k; the limit ends the buying under its budget.
        var budget = ClimberHealthRule.Budget(table.XpForLevel(14), WarMage.HealthPayPercent);
        Assert.True(ClimberHealthRule.Buy(table, 142, MageTemplate.LowAttribute, 0, 0, ulong.MaxValue).Xp > table.XpForLevel(14));
        var road = ClimberHealthRule.Buy(table, 142, MageTemplate.LowAttribute, 0, 0, budget);
        Assert.True(road.Limited);
        Assert.True(road.Xp <= 2 * budget); // M6 7 finish: the budget is each of the two stats' own

        // What the road bought stands on the Lodge: ranks are never refunded.
        Assert.True(ClimberHealthRule.ProjectedBuy(table, WarMage, 15).Health >= road.Health);
    }

    [RequiresDatsFact]
    public void Against_the_measured_deaths_the_floor_outlasts_the_hits_that_killed()
    {
        var table = XpTableData.Load(fixture.Dats);
        // The level-30 test character: 69 health, 86 in three Crimsonback hits (a mean of 29). M6 7 finish (the 5% aim per stat): the floor at level 30 is 109, which outlasts the three hits with 23 to spare but no longer takes a fourth (115).
        var health30 = ClimberHealthRule.ProjectedBuy(table, WarMage, 30).Health;
        Assert.Equal(109, health30);
        Assert.Equal(23, health30 - 86);
        Assert.True(health30 < 86 + 29, "the margin is under a fourth hit now");
        // M6 7 AC: the Burrow's first run at level 10 (the plan's 16 health died to one Crimsonback hit of the road) is gone with the rung: no rung runs the island before the hub's level 15; the Lodge's 72 is paid at 16 (the test above).
    }

    [RequiresDatsFact]
    public void BSD_Mage_and_the_templates_plans_carry_no_floor_and_level_80s_war_magic_stands()
    {
        var table = XpTableData.Load(fixture.Dats);
        Assert.DoesNotContain(MageTemplate.PlanAt(table, 80).Targets, t => t.Floor);
        var total = table.XpForLevel(80);
        var plain = MageSpendingPlanner.Compute(table, total, [], MageTemplate.SharesAt(80), MageBuild.Traditional);
        Assert.Equal(180, plain.Targets.Single(t => t.Id == MageTemplate.WarMagic && t.Kind == MageStatKind.Skill).Ranks);
        Assert.Equal(308u, SpellPlan.ProjectedWarBase(table, 80)); // the climber's level 80: 309 with no floor, 308 under the ladder's stat cap and her split (M6 item 14; 307 on the dance split) (SpellPlan projects the ladder's character with cap: ladder.StatCap): the same VI at 0.95
        Assert.Equal(SpellPlan.LevelAt(309, WarMage.PlanCastChance), SpellPlan.LevelAt(SpellPlan.ProjectedWarBase(table, 80), WarMage.PlanCastChance));
    }
}
