using System.Globalization;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 finish (the owner, 2026-11-08: no attribute and not Max Health takes more than a small share of the experience): the rule itself. What these prove, with the retail dats: under the ladder's <see cref="StatCap"/> (aim 5%, max 10%, grace 277) no attribute's
/// and not Max Health's cumulative plan cost passes the larger of 10% of the level's total and 277, at levels 5, 10, 15, 20 and 30, with the Strength floor and the health floor in the plan; Max Mana and the four magic skills are not bound; at level 2 (1,000 earned)
/// the grace allows a Strength floor of two ranks and not three. The dat tests skip without the dats.
/// </summary>
public sealed class StatCapTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Ladder WarMage = LadderLoader.WarMage;

    private static readonly uint[] MagicSkills = [MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment];

    private static int Ranks(SpendingPlan plan, MageStatKind kind, uint id) => plan.Targets.Where(t => t.Kind == kind && t.Id == id).Select(t => t.Ranks).DefaultIfEmpty(0).Max();

    /// <summary>The Strength floor of the fresh climber's burden (7 ranks, XpSpendRuleTests): a floor the cap must be able to cut back.</summary>
    private const int StrengthFloorRanks = 7;

    [Fact]
    public void The_ladders_cap_binds_every_attribute_and_Max_Health_and_nothing_else()
    {
        Assert.NotNull(WarMage.StatCap);
        foreach (var id in new uint[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self })
        {
            Assert.True(StatCap.Binds(new StatTarget(MageStatKind.Attribute, id, "x", false, 0)), $"attribute {id}");
        }

        Assert.True(StatCap.Binds(new StatTarget(MageStatKind.Vital, MageTemplate.MaxHealth, "Max Health", false, 0)));
        Assert.False(StatCap.Binds(new StatTarget(MageStatKind.Vital, MageTemplate.MaxMana, "Max Mana", false, 0)));
        Assert.False(StatCap.Binds(new StatTarget(MageStatKind.Vital, MageTemplate.MaxStamina, "Max Stamina", false, 0)));
        foreach (var id in MagicSkills)
        {
            Assert.False(StatCap.Binds(new StatTarget(MageStatKind.Skill, id, "x", true, 0)), $"skill {id}");
        }

        Assert.Equal(277ul, WarMage.StatCap.Max(1_000)); // the grace: 10% of 1,000 is 100
        Assert.Equal(1_000ul, WarMage.StatCap.Max(10_000));
        Assert.Equal(500ul, WarMage.StatCap.Aim(10_000));
    }

    [RequiresDatsTheory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(30)]
    public void No_attribute_and_not_Max_Health_costs_more_than_the_larger_of_ten_percent_and_the_grace_and_the_magic_is_not_bound(int level)
    {
        var table = XpTableData.Load(fixture.Dats);
        var total = table.XpForLevel(level);
        var limit = Math.Max(total / 10, 277ul);
        var plan = MageSpendingPlanner.Compute(table, total, [], MageTemplate.SharesFor(level, climber: true), MageBuild.Traditional, StrengthFloorRanks, ClimberHealthRule.ProjectedBuy(table, WarMage, level), cap: WarMage.StatCap);
        var attributes = new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self };
        foreach (var id in attributes)
        {
            var cost = table.AttributeXpList[Ranks(plan, MageStatKind.Attribute, id)];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"level {level}: attribute {id} costs {cost:N0} of limit {limit:N0}"));
            Assert.True(cost <= limit, $"level {level}: attribute {id} costs {cost}, limit {limit}");
        }

        var health = table.VitalXpList[Ranks(plan, MageStatKind.Vital, MageTemplate.MaxHealth)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"level {level}: Max Health costs {health:N0} of limit {limit:N0}"));
        Assert.True(health <= limit, $"level {level}: Max Health costs {health}, limit {limit}");

        // Not bound: what the capped stats leave goes to the magic skills and Max Mana, and one of them stands over the limit (War Magic alone is under it at level 20: 105,319 of 108,420; Max Mana is over).
        var warCost = table.SpecializedSkillXpList[Ranks(plan, MageStatKind.Skill, MageTemplate.WarMagic)];
        var manaCost = table.VitalXpList[Ranks(plan, MageStatKind.Vital, MageTemplate.MaxMana)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"level {level}: War Magic costs {warCost:N0}, Max Mana {manaCost:N0}"));
        Assert.True(Math.Max(warCost, manaCost) > limit, $"level {level}: War Magic ({warCost}) and Max Mana ({manaCost}) are not bound by the limit {limit}");
        Assert.True(plan.Spent <= plan.TotalXp);
    }

    [RequiresDatsFact]
    public void The_grace_allows_two_Strength_ranks_at_level_two_and_not_three()
    {
        var table = XpTableData.Load(fixture.Dats);
        var total = table.XpForLevel(2);
        Assert.Equal(1_000ul, total);
        Assert.True(total / 10 < table.AttributeXpList[1], "10% of level 2 is under a single rank: the grace is why a floor can buy at all");
        Assert.True(table.AttributeXpList[2] <= 277 && table.AttributeXpList[3] > 277, "277 is the price of the first two ranks, and the third is over it");
        int Strength(int floor, StatCap? cap) => Ranks(MageSpendingPlanner.Compute(table, total, [], MageTemplate.SharesAt(2), MageBuild.Traditional, floor, cap: cap), MageStatKind.Attribute, MageTemplate.Strength);

        Assert.Equal(2, Strength(2, WarMage.StatCap)); // two ranks are allowed by the grace
        Assert.Equal(2, Strength(3, WarMage.StatCap)); // a third is cut back to what the limit allows
        Assert.True(Strength(3, null) >= 3, "with no cap the plan of before buys the third rank, so the cut is the cap's"); // a plan without a cap is unchanged
    }
}
