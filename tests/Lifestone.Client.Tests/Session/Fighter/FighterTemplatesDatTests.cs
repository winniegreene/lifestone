using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The three fighter templates (M4 P4, plan D31) against the retail dats, beside <see cref="FighterTemplateDatTests"/> (which pins the Hunter's own numbers): the creation spend
/// of each, the plan each template's shares give over the XpTable (the hard-coded targets are what the rule computes) and the burden capacity of the finished fighter.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterTemplatesDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Aluvian = HeadlessCharacter.Heritage;

    public static TheoryData<string> Keys => new() { "hunter", "melee", "archer" };

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void The_creation_spends_330_attribute_credits_and_the_pinned_skill_credits_of_the_52(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var heritage = charGen.GetHeritage(Aluvian);
        var intent = template.CreationIntent(template.Name, HeadlessCharacter.RetailSkillClasses(fixture.Dats));
        Assert.True(intent.TryValidate(out var problem), problem);
        Assert.Equal(heritage.AttributeCredits, intent.Strength + intent.Endurance + intent.Coordination + intent.Quickness + intent.Focus + intent.Self);
        Assert.All(new[] { intent.Strength, intent.Endurance, intent.Coordination, intent.Quickness, intent.Focus, intent.Self }, v => Assert.InRange(v, 10u, 100u)); // M4 P5a C5: retail's chargen rule, each attribute 10 to 100

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

        output.WriteLine($"{template.Key}: creation heritage credits {heritage.SkillCredits}, spent {credits}");
        Assert.Equal(template.CreationSkillCredits, credits);
        Assert.True(credits <= heritage.SkillCredits);
        foreach (var skill in template.SpecializedAtCreation)
        {
            Assert.Equal(HeadlessCharacter.SpecializedSkill, intent.SkillAdvancementClasses[(int)skill]);
            Assert.True(skills.Skills.Single(s => s.Id == skill).ChargenUse, $"skill {skill} is not offered at creation");
        }

        foreach (var skill in template.TrainedAtCreation)
        {
            Assert.Equal(HeadlessCharacter.TrainedSkill, intent.SkillAdvancementClasses[(int)skill]);
            Assert.True(skills.Skills.Single(s => s.Id == skill).ChargenUse, $"skill {skill} is not offered at creation");
        }

        // M4 P5a: the skills trained after creation are none of the creation's, and the SkillTable's trained cost is what the build sends (Aluvian: Creature Enchantment 8, Item Enchantment 8, Life Magic 12).
        Assert.Empty(template.TrainedLater.Intersect(template.TrainedAtCreation.Concat(template.SpecializedAtCreation)));
        foreach (var later in template.TrainedLater)
        {
            Assert.Equal(HeadlessCharacter.UntrainedSkill, intent.SkillAdvancementClasses[(int)later]);
            Assert.Equal(FighterBuildScenario.TrainCredits[later], charGen.GetSkillTrainedCost(later, Aluvian, skills));
        }
    }

    [RequiresDatsFact]
    public void The_specialised_missile_weapons_skill_costs_what_the_archer_template_says()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var cost = charGen.GetSkillSpecializedCost(FighterTemplate.MissileWeapons, Aluvian, skills);
        output.WriteLine($"Missile Weapons specialised: {cost} credits");
        Assert.Equal(cost + 10 + 6, FighterTemplate.Archer.CreationSkillCredits);
        Assert.Equal(12 + 10 + 6, FighterTemplate.Melee.CreationSkillCredits);
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void The_hard_coded_plan_is_what_the_templates_shares_give_over_the_xp_table(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var xp = XpTableData.Load(fixture.Dats);
        var plan = FighterSpendingPlanner.ComputeLevel(xp, template.Level, template);
        foreach (var t in plan.Targets)
        {
            output.WriteLine($"new(MageStatKind.{t.Kind}, {t.Id}, \"{t.Name}\", {(t.Specialized ? "true" : "false")}, {t.Ranks}), // cap {t.MaxRanks(xp)}, {t.XpToReach(xp, 0):N0} XP");
        }

        output.WriteLine($"{template.Key}: spent {plan.Spent:N0}, remainder {plan.Remainder:N0} of {plan.TotalXp:N0}");
        Assert.Equal(100, template.Shares.Sum(s => s.Percent));
        Assert.Equal(template.LevelXp(xp), plan.TotalXp);
        Assert.Equal(FighterTemplate.Level40Xp, FighterSpendingPlanner.Compute(xp, xp.XpForLevel(FighterTemplate.TargetLevel), template.Shares).TotalXp);
        // The base plan is exactly the plan the characters were first built to; the whole plan keeps its stats in its places and only adds ranks (M4 P5a C1c: a stat in both buys on from its base ranks).
        var basePlan = FighterSpendingPlanner.Compute(xp, FighterTemplate.Level40Xp, template.Shares).Targets;
        Assert.Equal(basePlan.Select(b => (b.Kind, b.Id)), plan.Targets.Take(basePlan.Count).Select(x => (x.Kind, x.Id)));
        Assert.All(basePlan.Select((b, i) => (b, i)), x => Assert.True(plan.Targets[x.i].Ranks >= x.b.Ranks));
        Assert.Equal(template.Targets, plan.Targets);
        Assert.Equal(template.XpSpent, plan.Spent);
        Assert.Equal(template.XpRemainder, plan.Remainder);
        Assert.Equal(template.XpSpent, template.TotalXp(xp));
        Assert.Equal(plan.Spent + plan.Remainder, plan.TotalXp);
        Assert.All(template.Targets, t => Assert.True(t.Ranks > 0 && t.Ranks <= t.MaxRanks(xp), $"{t.Name} ranks {t.Ranks}"));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void Every_target_skill_is_one_the_template_trains_or_specialises_and_the_weapon_skill_is_specialised(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var trainedOrSpecialised = template.SpecializedAtCreation.Concat(template.TrainedAtCreation).Concat(template.TrainedLater).Concat(FighterTemplate.FreeTrainedSkills).ToHashSet(); // the heritage's free trained skills: Run (M4 P5a C1c), and with the floor rule Arcane Lore, Magic Defense, Jump, Loyalty and Salvaging (C5)
        foreach (var t in template.Targets.Where(t => t.Kind == MageStatKind.Skill))
        {
            Assert.Contains(t.Id, trainedOrSpecialised);
            Assert.Equal(template.SpecializedAtCreation.Contains(t.Id), t.Specialized);
        }

        Assert.Equal(template.SpecializedAtCreation.Count, template.Targets.Count(t => t.Specialized));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void The_finished_fighters_burden_capacity_carries_its_kit(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var capacity = Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity((int)template.AttributeAtTarget(FighterTemplate.Strength), 0);
        output.WriteLine($"{template.Key}: burden capacity {capacity}, kit {template.Kit.TotalBurden}");
        Assert.True(capacity >= template.Kit.TotalBurden, $"the finished {template.Key} carries {capacity} burden units; its kit weighs {template.Kit.TotalBurden}");
        // M4 P5a C5: the kit is granted after the raises (build step d), so it is carried at the plan's Strength, not the creation value (the Archer's is 10: capacity 1,500); FighterComponentsDatTests pins the kit with its components and the room for loot at 40 and at 55.
        Assert.True(FighterKit.Capacity((int)template.AttributeAtTarget(FighterTemplate.Strength)) >= template.Kit.TotalBurden);
    }

    [RequiresDatsFact]
    public void The_heritages_free_trained_skills_are_the_ones_the_floor_adds_to_the_plan_M4_P5a_C5_D40()
    {
        var classes = HeadlessCharacter.RetailSkillClasses(fixture.Dats);
        var free = Enumerable.Range(0, classes.Length).Where(i => classes[i] == HeadlessCharacter.TrainedSkill).Select(i => (uint)i).Order().ToArray();
        output.WriteLine("free trained skills (ids): " + string.Join(", ", free));
        Assert.Equal(free, FighterTemplate.FreeTrainedSkills.Order().ToArray());
        // 14 Arcane Lore (the owner asked for it by name; the lead's brief gave id 23, which is Lockpick: it is free, so no credits), 15 Magic Defense, 22 Jump, 24 Run, 36 Loyalty, 40 Salvaging.
        Assert.Equal<uint>([FighterTemplate.ArcaneLore, FighterTemplate.MagicDefense, FighterTemplate.Jump, FighterTemplate.Run, FighterTemplate.Loyalty, FighterTemplate.Salvaging], FighterTemplate.FreeTrainedSkills.Order());
        var skills = SkillTableData.Load(fixture.Dats);
        var arcane = skills.Skills.Single(s => s.Id == FighterTemplate.ArcaneLore);
        Assert.Equal((FighterTemplate.Focus, 3), (arcane.Formula.Attribute1, arcane.Formula.Divisor)); // Arcane Lore is Focus / 3
        foreach (var t in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            Assert.DoesNotContain(t.TrainedAtCreation.Concat(t.TrainedLater), id => id == FighterTemplate.ArcaneLore);
            // The credits: the 52 of the heritage, the creation spends the pinned number, the rest carries forward, the levels through MagicLevel add theirs, and the three schools take 28 of those.
            var xp = XpTableData.Load(fixture.Dats);
            var left = 52 - t.CreationSkillCredits + (int)xp.SkillCreditsThroughLevel(t.Level, 1) - 28;
            output.WriteLine($"{t.Key}: skill credits left unspent after the three schools: {left}");
            Assert.InRange(left, 0, 60);
        }
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Keys))]
    public void The_floor_rule_no_spendable_stats_next_rank_costs_under_a_thousandth_of_the_levels_total_experience_at_40_and_at_55_M4_P5a_C5_D40(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        if (template.FloorStats.Count == 0)
        {
            Assert.Equal("hunter", key); // the Hunter's plan has no floor (it is unchanged)
            return;
        }

        var xp = XpTableData.Load(fixture.Dats);
        var basePlan = FighterSpendingPlanner.Compute(xp, xp.XpForLevel(FighterTemplate.TargetLevel), template.Shares, template.FloorStats);
        var plans = new[] { (FighterTemplate.TargetLevel, basePlan), (template.Level, FighterSpendingPlanner.ComputeLevel(xp, template.Level, template)) };
        foreach (var (level, plan) in plans)
        {
            var total = xp.XpForLevel(level);
            foreach (var f in template.FloorStats)
            {
                var target = plan.Targets.Single(x => x.Kind == f.Kind && x.Id == f.Id);
                var list = target.XpList(xp);
                Assert.True(target.Ranks > 0, $"{target.Name} has no experience at level {level}");
                if (target.Ranks + 1 < list.Count)
                {
                    var next = list[target.Ranks + 1] - (ulong)list[target.Ranks];
                    output.WriteLine($"{template.Key} L{level}: {target.Name} ranks {target.Ranks}, next rank {next:N0} (floor {total / 1000.0:N0})");
                    Assert.True(next * 1000 >= total, $"{target.Name} at level {level}: the next rank costs {next:N0}, under {total / 1000.0:N0}");
                }
            }

            Assert.Equal(plan.Spent + plan.Remainder, total);
        }

        Assert.Equal(template.Targets, plans[1].Item2.Targets);
    }
}
