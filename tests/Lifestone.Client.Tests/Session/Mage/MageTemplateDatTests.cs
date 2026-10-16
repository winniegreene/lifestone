using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The mage template against the retail dats: the credits fit, the plan fits level 126's experience, the hard-coded
/// target ranks are what the documented rule gives over the XpTable, and the magic skills' formula is (Focus + Self) / 4.
/// </summary>
public sealed class MageTemplateDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Aluvian = HeadlessCharacter.Heritage;

    [RequiresDatsFact]
    public void The_creation_spends_48_of_the_heritage_52_skill_credits_and_all_330_attribute_credits()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var heritage = charGen.GetHeritage(Aluvian);
        var intent = MageTemplate.CreationIntent("BSD Mage", HeadlessCharacter.RetailSkillClasses(fixture.Dats));

        Assert.True(intent.TryValidate(out var problem), problem);
        Assert.Equal(CharacterCreateIntent.RequiredSkillCount, intent.SkillAdvancementClasses.Count);
        Assert.Equal(heritage.AttributeCredits, intent.Strength + intent.Endurance + intent.Coordination + intent.Quickness + intent.Focus + intent.Self);

        var credits = 0;
        for (uint skill = 0; skill < intent.SkillAdvancementClasses.Count; skill++)
        {
            credits += intent.SkillAdvancementClasses[(int)skill] switch
            {
                HeadlessCharacter.SpecializedSkill => charGen.GetSkillSpecializedCost(skill, Aluvian, skills),
                HeadlessCharacter.TrainedSkill => charGen.GetSkillTrainedCost(skill, Aluvian, skills),
                _ => 0,
            };
        }

        output.WriteLine($"creation: heritage credits {heritage.SkillCredits}, spent {credits}");
        Assert.Equal(MageTemplate.CreationSkillCredits, credits);
        Assert.Equal(52u, heritage.SkillCredits);
        Assert.Equal(28, charGen.GetSkillSpecializedCost(MageTemplate.WarMagic, Aluvian, skills));
        Assert.Equal(20, charGen.GetSkillSpecializedCost(MageTemplate.MeleeDefense, Aluvian, skills));
        Assert.Equal(HeadlessCharacter.TrainedSkill, intent.SkillAdvancementClasses[(int)MageTemplate.ArcaneLore]);
        Assert.Equal(0, charGen.GetSkillTrainedCost(MageTemplate.ArcaneLore, Aluvian, skills));
        Assert.Equal(4u, heritage.SkillCredits - (uint)credits);
    }

    [RequiresDatsFact]
    public void The_later_training_costs_34_credits_the_skill_table_prices_and_level_126_pays_for_it()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var xp = XpTableData.Load(fixture.Dats);

        var sum = 0;
        foreach (var skill in MageTemplate.TrainedLater)
        {
            // ACE's HandleActionTrainSkill insists the credits sent equal the SkillTable's trained cost; the heritage agrees.
            var table = skills.Skills.Single(s => s.Id == skill).TrainedCost;
            Assert.Equal(table, MageTemplate.TrainCredits[skill]);
            Assert.Equal(table, charGen.GetSkillTrainedCost(skill, Aluvian, skills));
            sum += MageTemplate.TrainCredits[skill];
        }

        var spare = (int)charGen.GetHeritage(Aluvian).SkillCredits - MageTemplate.CreationSkillCredits;
        var fromLevels = (int)xp.SkillCreditsThroughLevel(MageTemplate.TargetLevel, 1);
        output.WriteLine($"credits: train {sum}, spare {spare}, levels 2-126 {fromLevels}, left {spare + fromLevels - sum}");
        Assert.Equal(MageTemplate.TrainingCredits, sum);
        Assert.Equal(MageTemplate.Level126Credits, fromLevels);
        Assert.True(sum <= spare + fromLevels);
        Assert.Equal(7, spare + fromLevels - sum);
    }

    [RequiresDatsFact]
    public void The_creation_trained_skills_are_the_retail_ones_and_d19s_spare_credits_buy_nothing()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var xp = XpTableData.Load(fixture.Dats);
        var classes = MageTemplate.CreationSkillClasses(HeadlessCharacter.RetailSkillClasses(fixture.Dats));

        // The skills trained or specialised at creation: the free ones the heritage trains, plus the two specialised.
        var trained = Enumerable.Range(0, classes.Length).Where(i => classes[i] == HeadlessCharacter.TrainedSkill).Select(i => (uint)i).ToArray();
        output.WriteLine("creation trained: " + string.Join(",", trained));
        Assert.Equal(MageTemplate.CreationTrained.Order(), trained);
        Assert.All(trained, id => Assert.Equal(0, charGen.GetSkillTrainedCost(id, Aluvian, skills)));

        // D19's rule, Healing dropped by the owner (2026-10-15): after the magic four and Mana Conversion (34), the 7 spare credits go to
        // Magic Defense, Run, Jump in that priority, each only if untrained and fitting. All three are trained at creation: none is bought.
        var magicCredits = new[] { MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion }
            .Sum(id => charGen.GetSkillTrainedCost(id, Aluvian, skills));
        var left = (int)charGen.GetHeritage(Aluvian).SkillCredits - MageTemplate.CreationSkillCredits + (int)xp.SkillCreditsThroughLevel(MageTemplate.TargetLevel, 1) - magicCredits;
        Assert.Equal(7, left);
        var chosen = MageSpendingPlanner.ChooseSurvivalSkills(
            MageTemplate.SurvivalPriority,
            [.. trained, .. MageTemplate.SpecializedAtCreation],
            id => charGen.GetSkillTrainedCost(id, Aluvian, skills),
            left);
        Assert.Empty(chosen);
        Assert.Equal(4, MageTemplate.TrainedLater.Count);
        Assert.Equal(7, left - chosen.Sum(id => charGen.GetSkillTrainedCost(id, Aluvian, skills)));
    }

    [RequiresDatsFact]
    public void Level_126_costs_the_pinned_experience()
    {
        var xp = XpTableData.Load(fixture.Dats);
        Assert.Equal(MageTemplate.Level126Xp, xp.XpForLevel(MageTemplate.TargetLevel));
        Assert.True(xp.XpForLevel(MageTemplate.TargetLevel + 1) > MageTemplate.Level126Xp);
    }

    [RequiresDatsFact]
    public void The_hard_coded_targets_are_what_the_documented_rule_gives_over_the_xp_table()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageSpendingPlanner.Compute(xp, MageTemplate.Level126Xp);
        foreach (var t in plan.Targets)
        {
            output.WriteLine($"target {t.Kind} {t.Id} {t.Name} {(t.Specialized ? "specialised" : "trained/base")}: {t.Ranks} ranks (cap {t.MaxRanks(xp)}), {t.XpToReach(xp, 0):N0} XP");
        }

        output.WriteLine($"plan: spent {plan.Spent:N0}, remainder {plan.Remainder:N0} of {plan.TotalXp:N0}");
        Assert.Equal(MageTemplate.Targets, plan.Targets);
        Assert.Equal(MageTemplate.XpSpent, plan.Spent);
        Assert.Equal(MageTemplate.XpRemainder, plan.Remainder);
        Assert.Equal(MageTemplate.XpSpent, MageTemplate.TotalXp(xp));
        Assert.Equal(MageTemplate.XpRemainder, MageTemplate.Remainder(xp));
        Assert.True(MageTemplate.TotalXp(xp) <= MageTemplate.Level126Xp);
        Assert.All(MageTemplate.Targets, t => Assert.True(t.Ranks <= t.MaxRanks(xp), $"{t.Name} is past its cap"));
    }

    [RequiresDatsFact]
    public void The_rule_spends_in_budgets_and_leaves_less_than_the_cheapest_next_rank()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageSpendingPlanner.Compute(xp, MageTemplate.Level126Xp);
        Assert.Equal(plan.TotalXp, plan.Spent + plan.Remainder);
        Assert.Equal(plan.Spent, plan.Targets.Aggregate(0UL, (sum, t) => sum + t.XpToReach(xp, 0)));

        // The remainder cannot buy the next rank of the last share's stat (the carry rolled that far).
        var last = plan.Targets[^1];
        Assert.True(last.Ranks < last.MaxRanks(xp));
        var next = last.XpList(xp)[last.Ranks + 1] - (ulong)last.XpList(xp)[last.Ranks];
        Assert.True(plan.Remainder < next, $"remainder {plan.Remainder:N0} could buy a {next:N0} rank");

        // XpToReach is the cumulative difference; a stat at or past the target needs nothing.
        var war = MageTemplate.TargetOf(MageStatKind.Skill, MageTemplate.WarMagic)!;
        Assert.Equal(XpTableData.XpToRaise(xp.SpecializedSkillXpList, 10, war.Ranks), war.XpToReach(xp, 10));
        Assert.Equal(0UL, war.XpToReach(xp, war.Ranks));
        Assert.Equal(0UL, war.XpToReach(xp, war.Ranks + 5));
        Assert.Equal(war.XpToReach(xp, 0), war.XpRemaining(xp, 0));
        Assert.Equal(war.XpToReach(xp, 10), war.XpRemaining(xp, xp.SpecializedSkillXpList[10]));
    }

    [RequiresDatsFact]
    public void The_split_is_ninety_percent_magic_and_ten_percent_in_equal_shares_over_the_rest()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var total = MageTemplate.Level126Xp;
        var plan = MageSpendingPlanner.Compute(xp, total);
        Assert.Equal(21, plan.Targets.Count);
        Assert.Equal(MageTemplate.Targets.Select(t => (t.Kind, t.Id, t.Specialized)), plan.Targets.Select(t => (t.Kind, t.Id, t.Specialized)));

        // The magic shares: the four magic skills, Focus and Self, Mana Conversion, Max Mana (the first eight targets) take at most 90%
        // and fall short only by less than the cheapest next rank of the last of them (Max Mana).
        var magic = plan.Targets.Take(8).Aggregate(0UL, (sum, t) => sum + t.XpToReach(xp, 0));
        var maxMana = plan.Targets[7];
        var manaNext = maxMana.XpList(xp)[maxMana.Ranks + 1] - (ulong)maxMana.XpList(xp)[maxMana.Ranks];
        var magicBudget = (total / 100 * 50) + (total / 100 * 30) + (total / 100 * 5) + (total / 100 * 5);
        output.WriteLine($"magic spent {magic:N0} of {magicBudget:N0}; max mana next rank {manaNext:N0}");
        Assert.True(magic <= magicBudget + 3);
        Assert.True(magic + manaNext > magicBudget - 3);

        // The pool: 13 stats, each close to an equal share of the rest (a share's leftover rolls on, so a stat's cost differs by a rank or a carry).
        var pool = plan.Targets.Skip(8).ToArray();
        Assert.Equal(13, pool.Length);
        var each = (total - magicBudget) / 13;
        foreach (var t in pool)
        {
            var cost = t.XpToReach(xp, 0);
            output.WriteLine($"pool {t.Name}: {cost:N0} XP (equal share {each:N0})");
            Assert.InRange(cost, each - (each / 6), each + (each / 6));
        }

        Assert.All(plan.Targets, t => Assert.InRange(t.Ranks, 1, t.MaxRanks(xp)));
    }

    [RequiresDatsFact]
    public void Print_the_finished_mage_vitals_attributes_and_burden_capacity()
    {
        var xp = XpTableData.Load(fixture.Dats);
        foreach (var t in MageTemplate.Targets)
        {
            var start = t.Kind == MageStatKind.Attribute ? MageTemplate.CreationValue(t.Id) : 0;
            output.WriteLine($"{t.Kind} {t.Id} {t.Name}: creation {start} + {t.Ranks} ranks = {start + t.Ranks}, {t.XpToReach(xp, 0):N0} XP");
        }

        uint Attr(uint id) => MageTemplate.CreationValue(id) + (uint)MageTemplate.TargetOf(MageStatKind.Attribute, id)!.Ranks;
        uint Ranks(uint id) => (uint)MageTemplate.TargetOf(MageStatKind.Vital, id)!.Ranks;
        output.WriteLine($"Max Health {(Attr(MageTemplate.Endurance) + 1) / 2} + {Ranks(MageTemplate.MaxHealth)} = {((Attr(MageTemplate.Endurance) + 1) / 2) + Ranks(MageTemplate.MaxHealth)}");
        output.WriteLine($"Max Stamina {Attr(MageTemplate.Endurance)} + {Ranks(MageTemplate.MaxStamina)} = {Attr(MageTemplate.Endurance) + Ranks(MageTemplate.MaxStamina)}");
        output.WriteLine($"Max Mana {Attr(MageTemplate.Self)} + {Ranks(MageTemplate.MaxMana)} = {Attr(MageTemplate.Self) + Ranks(MageTemplate.MaxMana)}");
        output.WriteLine($"burden capacity {Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity((int)Attr(MageTemplate.Strength), 0)}");
    }

    [RequiresDatsFact]
    public void The_four_magic_skills_are_focus_plus_self_over_four_in_the_skill_table()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        foreach (var id in new[] { MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment })
        {
            var f = skills.Skills.Single(s => s.Id == id).Formula;
            Assert.Equal((0, 1, 1, MageTemplate.MagicSkillDivisor), (f.AdditiveBonus, f.Attribute1Multiplier, f.Attribute2Multiplier, f.Divisor));
            Assert.Equal(new[] { MageTemplate.Focus, MageTemplate.Self }.Order(), new[] { f.Attribute1, f.Attribute2 }.Order());

            // The SkillTable's own arithmetic over the planned attributes gives the template's expected base minus InitLevel and ranks.
            var focus = MageTemplate.HighAttribute + (uint)MageTemplate.TargetOf(MageStatKind.Attribute, MageTemplate.Focus)!.Ranks;
            var self = MageTemplate.HighAttribute + (uint)MageTemplate.TargetOf(MageStatKind.Attribute, MageTemplate.Self)!.Ranks;
            Assert.True(f.TryCalculate(f.Attribute1 == MageTemplate.Focus ? focus : self, f.Attribute2 == MageTemplate.Focus ? focus : self, out var formula));
            var t = MageTemplate.TargetOf(MageStatKind.Skill, id)!;
            Assert.Equal(formula + (t.Specialized ? 10u : 0u) + (uint)t.Ranks, MageTemplate.ExpectedMagicSkillBase(id));
        }
    }

    [RequiresDatsFact]
    public void Print_the_magic_skill_bases_against_the_profile_powers()
    {
        foreach (var id in new[] { MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment })
        {
            var t = MageTemplate.TargetOf(MageStatKind.Skill, id)!;
            output.WriteLine($"{t.Name}: base {MageTemplate.ExpectedMagicSkillBase(id)} ({t.Ranks} ranks{(t.Specialized ? ", specialised +10" : string.Empty)})");
        }
    }
}
