using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The fighter template against the retail dats: skill costs, the creation spend, level 40 and the plan.</summary>
public sealed class FighterTemplateDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Aluvian = HeadlessCharacter.Heritage;

    [RequiresDatsFact]
    public void The_four_skills_cost_12_6_6_and_10_and_are_offered_at_creation()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        Assert.Equal(12, charGen.GetSkillSpecializedCost(FighterTemplate.HeavyWeapons, Aluvian, skills));
        Assert.Equal(6, charGen.GetSkillTrainedCost(FighterTemplate.MissileWeapons, Aluvian, skills));
        Assert.Equal(6, charGen.GetSkillTrainedCost(FighterTemplate.Healing, Aluvian, skills));
        Assert.Equal(10, charGen.GetSkillTrainedCost(FighterTemplate.MeleeDefense, Aluvian, skills));
        foreach (var id in new[] { FighterTemplate.HeavyWeapons, FighterTemplate.MissileWeapons, FighterTemplate.Healing, FighterTemplate.MeleeDefense })
        {
            Assert.True(skills.Skills.Single(s => s.Id == id).ChargenUse, $"skill {id} is not offered at creation");
        }
    }

    [RequiresDatsFact]
    public void The_creation_spends_330_attribute_credits_and_34_of_the_52_skill_credits()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var heritage = charGen.GetHeritage(Aluvian);
        var intent = FighterTemplate.Hunter.CreationIntent(FighterTemplate.HunterName, HeadlessCharacter.RetailSkillClasses(fixture.Dats));
        Assert.True(intent.TryValidate(out var problem), problem);
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
        Assert.Equal(FighterTemplate.Hunter.CreationSkillCredits, credits);
        Assert.Equal(52u, heritage.SkillCredits);
        Assert.True(credits <= heritage.SkillCredits);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, intent.SkillAdvancementClasses[(int)FighterTemplate.HeavyWeapons]);
        Assert.Empty(FighterTemplate.Hunter.TrainedLater);
    }

    [RequiresDatsFact]
    public void The_skill_formulas_are_the_ones_the_template_reasons_from()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        void Formula(uint id, uint firstAttribute, uint secondAttribute, int divisor)
        {
            var f = skills.Skills.Single(s => s.Id == id).Formula;
            Assert.Equal(divisor, f.Divisor);
            Assert.Equal(new[] { firstAttribute, secondAttribute }.Order(), new[] { f.Attribute1, f.Attribute2 }.Order());
        }

        Formula(FighterTemplate.HeavyWeapons, FighterTemplate.Strength, FighterTemplate.Coordination, 3);
        Formula(FighterTemplate.MeleeDefense, FighterTemplate.Coordination, FighterTemplate.Quickness, 3);
        Formula(FighterTemplate.Healing, FighterTemplate.Focus, FighterTemplate.Coordination, 3);
        Assert.Equal(2, skills.Skills.Single(s => s.Id == FighterTemplate.MissileWeapons).Formula.Divisor);
    }

    [RequiresDatsFact]
    public void Level_40_costs_the_pinned_experience_and_credits()
    {
        var xp = XpTableData.Load(fixture.Dats);
        output.WriteLine($"level 40: xp {xp.XpForLevel(40)}, credits {xp.SkillCreditsThroughLevel(40, 1)}");
        Assert.Equal(FighterTemplate.Level40Xp, xp.XpForLevel(FighterTemplate.TargetLevel));
        Assert.Equal(FighterTemplate.Level40Credits, (int)xp.SkillCreditsThroughLevel(FighterTemplate.TargetLevel, 1));
        Assert.True(xp.XpForLevel(FighterTemplate.TargetLevel + 1) > FighterTemplate.Level40Xp);
    }

    [RequiresDatsFact]
    public void The_hard_coded_plan_is_what_the_rule_gives_over_the_xp_table()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = FighterSpendingPlanner.Compute(xp, xp.XpForLevel(FighterTemplate.TargetLevel));
        foreach (var t in plan.Targets)
        {
            output.WriteLine($"target {t.Kind} {t.Id} {t.Name}: {t.Ranks} ranks (cap {t.MaxRanks(xp)}), {t.XpToReach(xp, 0):N0} XP");
        }

        output.WriteLine($"plan: spent {plan.Spent:N0}, remainder {plan.Remainder:N0} of {plan.TotalXp:N0}");
        Assert.Equal(FighterTemplate.Level40Xp, plan.TotalXp);
        Assert.Equal(FighterTemplate.Hunter.Targets, plan.Targets);
        Assert.Equal(FighterTemplate.Hunter.XpSpent, plan.Spent);
        Assert.Equal(FighterTemplate.Hunter.XpRemainder, plan.Remainder);
        Assert.Equal(FighterTemplate.Hunter.XpSpent, FighterTemplate.Hunter.TotalXp(xp));
        Assert.All(FighterTemplate.Hunter.Targets, t => Assert.True(t.Ranks > 0 && t.Ranks <= t.MaxRanks(xp), $"{t.Name} ranks {t.Ranks}"));
        Assert.Equal(plan.Spent + plan.Remainder, plan.TotalXp);
    }

    [RequiresDatsFact]
    public void The_finished_fighter_is_printed_and_its_burden_capacity_carries_the_kit()
    {
        foreach (var t in FighterTemplate.Hunter.Targets)
        {
            var start = t.Kind == MageStatKind.Attribute ? FighterTemplate.Hunter.CreationValue(t.Id) : 0;
            output.WriteLine($"{t.Kind} {t.Id} {t.Name}: creation {start} + {t.Ranks} = {start + t.Ranks}");
        }

        var capacity = Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity((int)FighterTemplate.Hunter.AttributeAtTarget(FighterTemplate.Strength), 0);
        output.WriteLine($"burden capacity {capacity}");
        Assert.True(capacity >= FighterKit.Hunter.TotalBurden, $"the finished fighter carries {capacity} burden units; its kit weighs {FighterKit.Hunter.TotalBurden}");
    }
}
