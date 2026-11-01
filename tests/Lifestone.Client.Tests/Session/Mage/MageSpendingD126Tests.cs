using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// D126 (b): the experience planner raises only what the soak mage gains from, checked against ACE (docs/session.md section 56). Round 12 bought Loyalty 120 -> 121 (421,961) and Run 120 -> 121 (421,961): Loyalty is read only by the
/// allegiance's pass-up and Salvaging only by salvaging, so both are left out of the dance shares; Run is capped where a rank stops paying for itself within a soak (81 ranks at round 12's figures). The level-126 D19 split is unchanged.
/// </summary>
public sealed class MageSpendingD126Tests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static bool Has(SpendingPlan plan, uint skill) => plan.Targets.Any(t => t.Kind == MageStatKind.Skill && t.Id == skill);

    [Fact]
    public void The_dance_shares_leave_out_Loyalty_and_Salvaging_and_D19_keeps_them()
    {
        Assert.Equal(0, MageShares.Dance.WeightOf(MageStatKind.Skill, MageTemplate.Loyalty));
        Assert.Equal(0, MageShares.Dance.WeightOf(MageStatKind.Skill, MageTemplate.Salvaging));
        Assert.Same(RunPayback.Soak, MageShares.Dance.RunCap);
        Assert.Null(MageShares.D19.RunCap);
        Assert.Equal(1, MageShares.D19.WeightOf(MageStatKind.Skill, MageTemplate.Loyalty));
    }

    [Fact]
    public void A_point_of_Run_is_worth_less_as_the_skill_rises_by_ACEs_run_rate()
    {
        // ACE MovementSystem.GetRunRate (MovementSystem.cs:20-28 @95ef426d): (Run / (Run + 200) x 11 + 4) / 4 with no burden: 2.585 at 272, 2.188 at 152.
        Assert.Equal(2.585, MovementSystem.GetRunRate(0f, 272, 1f), 3);
        Assert.Equal(2.188, MovementSystem.GetRunRate(0f, 152, 1f), 3);
        var soak = RunPayback.Soak;
        Assert.True(soak.XpPerHourOfAPoint(152) > soak.XpPerHourOfAPoint(273));
        Assert.InRange(soak.XpPerHourOfAPoint(273), 1400, 1550); // 0.27 x 5.7 M x (1 - rate(273) / rate(274)): about 1,470 an hour
    }

    [RequiresDatsFact]
    public void At_round_12s_figures_Run_pays_back_up_to_81_ranks_and_its_121st_rank_takes_about_290_hours()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var soak = RunPayback.Soak;
        var list = xp.TrainedSkillXpList;
        var cost = list[121] - (double)list[120];
        var hours = cost / soak.XpPerHourOfAPoint(soak.SkillBeforeRanks + 120);
        output.WriteLine($"rank 120 -> 121: {cost:N0} XP for {soak.XpPerHourOfAPoint(soak.SkillBeforeRanks + 120):N0} XP an hour: {hours:N0} hours; cap {soak.CapRanks(xp)} ranks ({list[soak.CapRanks(xp)]:N0} XP)");
        Assert.Equal(421_961, cost); // round 12's raise, 10:29:35 UTC
        Assert.InRange(hours, 250, 330);
        Assert.Equal(81, soak.CapRanks(xp));
        Assert.True((soak with { HorizonHours = 24 }).CapRanks(xp) > 81); // a longer horizon buys more
        Assert.Equal(0, (soak with { WalkShare = 0 }).CapRanks(xp)); // a mage that never walks buys none
    }

    [RequiresDatsFact]
    public void The_level_80_plan_raises_no_Loyalty_or_Salvaging_caps_Run_and_spends_what_they_leave_on_the_rest()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var total = xp.XpForLevel(80);
        var plan = MageSpendingPlanner.Compute(xp, total, [], MageShares.Dance, MageBuild.Traditional);
        var withAll = MageSpendingPlanner.Compute(xp, total, [], MageShares.Dance with { PoolWeights = new Dictionary<(MageStatKind, uint), int> { [(MageStatKind.Attribute, MageTemplate.Endurance)] = 4, [(MageStatKind.Vital, MageTemplate.MaxStamina)] = 4 }, RunCap = null }, MageBuild.Traditional);
        foreach (var t in plan.Targets)
        {
            output.WriteLine($"  {t.Name}: {t.Ranks} (before D126: {withAll.Targets.SingleOrDefault(w => w.Kind == t.Kind && w.Id == t.Id)?.Ranks})");
        }

        Assert.False(Has(plan, MageTemplate.Loyalty));
        Assert.False(Has(plan, MageTemplate.Salvaging));
        Assert.True(Has(withAll, MageTemplate.Loyalty));
        var run = plan.Targets.Single(t => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.Run);
        Assert.Equal(RunPayback.Soak.CapRanks(xp), run.Ranks);
        Assert.True(withAll.Targets.Single(t => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.Run).Ranks > run.Ranks);
        Assert.True(plan.Remainder < plan.TotalXp / 100, "what the left-out and capped stats leave is spent on the rest");
        foreach (var stat in new[] { (MageStatKind.Attribute, MageTemplate.Strength), (MageStatKind.Attribute, MageTemplate.Endurance), (MageStatKind.Vital, MageTemplate.MaxStamina) })
        {
            Assert.True(plan.Targets.Single(t => (t.Kind, t.Id) == stat).Ranks >= withAll.Targets.Single(t => (t.Kind, t.Id) == stat).Ranks);
        }
    }
}
