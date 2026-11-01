using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F3 (D87): the mage's build at another level against the retail XpTable: the lowest level that trains all four later skills, level 80's credits, trainable skills, spend and remainder, and the plan at 126 equal to the pinned template.
/// </summary>
public sealed class MageLevelPlanDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const int Soak = 80;

    [RequiresDatsFact]
    public void The_lowest_level_trains_life_creature_and_mana_conversion_and_the_full_level_trains_all_four()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var schools = new[] { MageTemplate.LifeMagic, MageTemplate.CreatureEnchantment, MageTemplate.ManaConversion };
        var lowest = Enumerable.Range(2, MageTemplate.TargetLevel - 1).First(l => schools.All(MageTemplate.PlanAt(xp, l, MageBuild.Gate126).Trained.Contains));
        var full = Enumerable.Range(2, MageTemplate.TargetLevel - 1).First(l => MageTemplate.PlanAt(xp, l, MageBuild.Gate126).LeftOut.Count == 0);
        output.WriteLine($"lowest level that trains Life, Creature and Mana Conversion: {lowest} ({MageTemplate.PlanAt(xp, lowest, MageBuild.Gate126).Credits} credits after creation); all four from level {full} ({MageTemplate.PlanAt(xp, full, MageBuild.Gate126).Credits}; level {full - 1}: {MageTemplate.PlanAt(xp, full - 1, MageBuild.Gate126).Credits})");
        Assert.Equal(MageTemplate.MinLevel, lowest);
        Assert.Equal(MageTemplate.FullTrainingLevel, full);
        Assert.Equal(schools.Sum(id => MageTemplate.TrainCredits[id]), MageTemplate.TrainingCredits - MageTemplate.TrainCredits[MageTemplate.ItemEnchantment]);
        Assert.True(MageTemplate.PlanAt(xp, MageTemplate.FullTrainingLevel, MageBuild.Gate126).Credits >= MageTemplate.TrainingCredits);
        Assert.True(MageTemplate.PlanAt(xp, MageTemplate.FullTrainingLevel - 1, MageBuild.Gate126).Credits < MageTemplate.TrainingCredits);
        Assert.Equal([MageTemplate.ItemEnchantment], MageTemplate.PlanAt(xp, MageTemplate.MinLevel, MageBuild.Gate126).LeftOut);
        Assert.NotEqual([], MageTemplate.PlanAt(xp, MageTemplate.MinLevel - 1, MageBuild.Gate126).LeftOut.Except([MageTemplate.ItemEnchantment]).ToArray());
        Assert.Equal(MageTemplate.HeritageSkillCredits, (int)CharGenData.Load(fixture.Dats).GetHeritage(HeadlessCharacter.Heritage).SkillCredits);
    }

    [RequiresDatsFact]
    public void A_level_below_the_full_level_trains_what_fits_in_order_and_names_what_it_leaves_out()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageTemplate.PlanAt(xp, MageTemplate.FullTrainingLevel - 1, MageBuild.Gate126);
        output.WriteLine($"level {plan.Level}: credits {plan.Credits}, trained {string.Join(",", plan.Trained)}, left out {string.Join(",", plan.LeftOut)}; {plan.LeftOutLine}");
        Assert.Equal([MageTemplate.ItemEnchantment], plan.LeftOut);
        Assert.StartsWith("TODO(m5-mage-level-credits)", plan.LeftOutLine, StringComparison.Ordinal);
        Assert.Contains("Item Enchantment", plan.LeftOutLine, StringComparison.Ordinal);
        Assert.Equal(MageTemplate.TrainedLater.Except(plan.LeftOut), plan.Trained);
        Assert.All(plan.LeftOut, id => Assert.DoesNotContain(plan.Targets, t => t.Kind == MageStatKind.Skill && t.Id == id));
        Assert.Equal(plan.Credits - 26, plan.CreditsLeft);
        Assert.Equal(plan.Spent + plan.Remainder, plan.TotalXp);

        // Far below, the order decides: the credits train Life Magic, then Creature Enchantment, then whatever of the rest still fits.
        var low = MageTemplate.PlanAt(xp, 30, MageBuild.Gate126);
        Assert.Equal(MageTemplate.LifeMagic, low.Trained[0]);
        Assert.Equal(MageTemplate.TrainedLater.Count, low.Trained.Count + low.LeftOut.Count);
        Assert.True(low.CreditsLeft >= 0);
    }

    [RequiresDatsFact]
    public void Level_80_is_the_traditional_mage_without_Melee_Defense_with_War_and_Life_specialised_and_all_four_schools_trained_M5_7_F6_D87()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageTemplate.PlanAt(xp, Soak);
        var fromLevels = (int)xp.SkillCreditsThroughLevel(Soak, 1);
        output.WriteLine($"level {Soak}: build {plan.Build}, xp {plan.TotalXp:N0}, credits {MageTemplate.SpareCreationCredits} + {fromLevels} = {plan.Credits}, left {plan.CreditsLeft}, spent {plan.Spent:N0}, remainder {plan.Remainder:N0}; left out [{plan.LeftOutLine}]");
        foreach (var t in plan.Targets)
        {
            output.WriteLine($"  {t.Kind} {t.Name}{(t.Specialized ? " (specialised)" : string.Empty)}: {t.Ranks} ranks (cap {t.MaxRanks(xp)}), {t.XpToReach(xp, 0):N0} XP");
        }

        Assert.Equal(MageBuild.Traditional, plan.Build);
        Assert.Equal(MageBuild.Gate126, MageTemplate.BuildFor(MageTemplate.TargetLevel));
        Assert.Equal(Soak, plan.Level);
        Assert.Equal(xp.XpForLevel(Soak), plan.TotalXp);
        Assert.Equal(MageTemplate.SpareCreationCredits + fromLevels, plan.Credits);
        Assert.Equal(32, plan.Credits);

        // All four schools and Mana Conversion are trained or specialised: Life Magic at creation, the rest by TrainSkill; nothing is left out and the credits still left are 10.
        Assert.Equal([MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion], plan.Trained);
        Assert.Empty(plan.LeftOut);
        Assert.Equal(string.Empty, plan.LeftOutLine);
        Assert.Equal(22, plan.Trained.Sum(id => MageTemplate.TrainCredits[id]));
        Assert.Equal(10, plan.CreditsLeft);
        Assert.Equal(48, MageTemplate.CreationSkillCredits);
        foreach (var school in new[] { MageTemplate.WarMagic, MageTemplate.LifeMagic })
        {
            Assert.True(plan.Targets.Single(t => t.Kind == MageStatKind.Skill && t.Id == school).Specialized);
        }

        foreach (var school in new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion })
        {
            Assert.False(plan.Targets.Single(t => t.Kind == MageStatKind.Skill && t.Id == school).Specialized);
        }

        // No Melee Defense at all; D19's survival list buys nothing with the 10 left (Magic Defense, Run and Jump are the heritage's free skills).
        var skills = SkillTableData.Load(fixture.Dats);
        Assert.DoesNotContain(plan.Targets, t => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.MeleeDefense);
        Assert.Equal(MageTemplate.Targets.Count - 3, plan.Targets.Count); // D126 (b): nor Loyalty and Salvaging
        Assert.Empty(MageSpendingPlanner.ChooseSurvivalSkills(MageTemplate.SurvivalPriority, MageTemplate.CreationTrained, id => (int)skills.Skills.Single(x => x.Id == id).TrainedCost, plan.CreditsLeft));
        Assert.Same(MageShares.Dance, MageTemplate.SharesAt(Soak));
        Assert.Same(MageShares.D19, MageTemplate.SharesAt(MageTemplate.TargetLevel));

        // The same planner as level 126's, over level 80's experience, the dance shares and the traditional build.
        var again = MageSpendingPlanner.Compute(xp, xp.XpForLevel(Soak), [], MageShares.Dance, MageBuild.Traditional);
        Assert.Equal(again.Targets, plan.Targets);
        Assert.Equal(again.Spent, plan.Spent);
        Assert.Equal(again.Remainder, plan.Remainder);
        Assert.Equal(plan.Spent, plan.Targets.Aggregate(0UL, (sum, t) => sum + t.XpToReach(xp, 0)));
        // D126 (b): the template's order without Melee Defense, Loyalty and Salvaging, and Run (capped) first in the pool, after Max Mana
        var expected = MageTemplate.Targets.Where(t => t.Kind != MageStatKind.Skill || t.Id is not (MageTemplate.MeleeDefense or MageTemplate.Loyalty or MageTemplate.Salvaging or MageTemplate.Run)).Select(t => (t.Kind, t.Id)).ToList();
        expected.Insert(expected.IndexOf((MageStatKind.Vital, MageTemplate.MaxMana)) + 1, (MageStatKind.Skill, MageTemplate.Run));
        Assert.Equal(expected, plan.Targets.Select(t => (t.Kind, t.Id)));
        Assert.All(plan.Targets, t => Assert.True(t.Ranks <= t.MaxRanks(xp)));
        Assert.Equal(plan.Spent + plan.Remainder, plan.TotalXp);
        Assert.True(plan.Remainder < plan.TotalXp / 100);
        Assert.True(plan.TotalXp < MageTemplate.Level126Xp);
    }

    [RequiresDatsFact]
    public void The_traditional_creation_costs_48_credits_leaves_4_spare_and_the_gate_creation_is_unchanged_M5_7_F6_D87()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var retail = HeadlessCharacter.RetailSkillClasses(fixture.Dats);
        int Credits(IReadOnlyList<uint> classes)
        {
            var sum = 0;
            for (uint skill = 0; skill < classes.Count; skill++)
            {
                sum += classes[(int)skill] switch
                {
                    HeadlessCharacter.SpecializedSkill => charGen.GetSkillSpecializedCost(skill, HeadlessCharacter.Heritage, skills),
                    HeadlessCharacter.TrainedSkill => charGen.GetSkillTrainedCost(skill, HeadlessCharacter.Heritage, skills),
                    _ => 0,
                };
            }

            return sum;
        }

        var gate = MageTemplate.CreationIntent("BSD Mage", retail);
        var traditional = MageTemplate.CreationIntent("BSD Mage", retail, MageBuild.Traditional);
        Assert.True(traditional.TryValidate(out var problem), problem);
        Assert.Equal(28, charGen.GetSkillSpecializedCost(MageTemplate.WarMagic, HeadlessCharacter.Heritage, skills));
        Assert.Equal(20, charGen.GetSkillSpecializedCost(MageTemplate.LifeMagic, HeadlessCharacter.Heritage, skills));
        Assert.Equal(48, Credits(traditional.SkillAdvancementClasses));
        Assert.Equal(48, Credits(gate.SkillAdvancementClasses));
        Assert.Equal(MageTemplate.SpareCreationCredits, (int)charGen.GetHeritage(HeadlessCharacter.Heritage).SkillCredits - Credits(traditional.SkillAdvancementClasses));

        // Melee Defense is untrained (the heritage does not give it free: trained it costs 10), War Magic and Life Magic are specialised, nothing else differs from the gate's classes.
        Assert.Equal(HeadlessCharacter.UntrainedSkill, traditional.SkillAdvancementClasses[(int)MageTemplate.MeleeDefense]);
        Assert.Equal(10, charGen.GetSkillTrainedCost(MageTemplate.MeleeDefense, HeadlessCharacter.Heritage, skills));
        Assert.Equal(HeadlessCharacter.SpecializedSkill, traditional.SkillAdvancementClasses[(int)MageTemplate.WarMagic]);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, traditional.SkillAdvancementClasses[(int)MageTemplate.LifeMagic]);
        Assert.Equal(2, traditional.SkillAdvancementClasses.Count(c => c == HeadlessCharacter.SpecializedSkill));
        var differing = Enumerable.Range(0, traditional.SkillAdvancementClasses.Count).Where(i => traditional.SkillAdvancementClasses[i] != gate.SkillAdvancementClasses[i]).Select(i => (uint)i).ToArray();
        Assert.Equal([MageTemplate.MeleeDefense, MageTemplate.LifeMagic], differing);

        // The gate's intent is what it was: the default build.
        Assert.Equal(gate.SkillAdvancementClasses, MageTemplate.CreationIntent("BSD Mage", retail, MageBuild.Gate126).SkillAdvancementClasses);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, gate.SkillAdvancementClasses[(int)MageTemplate.MeleeDefense]);
        Assert.NotEqual(HeadlessCharacter.SpecializedSkill, gate.SkillAdvancementClasses[(int)MageTemplate.LifeMagic]);

        // Later training: the three skills the traditional mage still trains cost 22, from 4 + the 28 level credits to 80.
        Assert.Equal([MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion], MageTemplate.TrainedLaterFor(MageBuild.Traditional));
        Assert.Equal(MageTemplate.TrainedLater, MageTemplate.TrainedLaterFor(MageBuild.Gate126));
        Assert.Equal(22, MageTemplate.TrainedLaterFor(MageBuild.Traditional).Sum(id => (int)skills.Skills.Single(s => s.Id == id).TrainedCost));
    }

    [RequiresDatsFact]
    public void The_traditional_mage_is_judged_without_Melee_Defense_and_the_gate_template_still_wants_it_M5_7_F6_D87()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageTemplate.PlanAt(xp, Soak);
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = Soak;
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, MageTemplate.CreationValue(id), 0);
        }

        foreach (var t in plan.Targets)
        {
            switch (t.Kind)
            {
                case MageStatKind.Attribute:
                    q.Attributes[t.Id] = new AttributeDesc(t.Id, (uint)t.Ranks, MageTemplate.CreationValue(t.Id), 1);
                    break;
                case MageStatKind.Vital:
                    q.Vitals[t.Id] = new VitalDesc(t.Id, (uint)t.Ranks, 0, 1, 100);
                    break;
                default:
                    q.Skills[t.Id] = new SkillDesc(t.Id, (ushort)t.Ranks, 1, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, 1, t.Specialized ? 10u : 0u, 0, 0);
                    break;
            }
        }

        q.Skills[MageTemplate.MeleeDefense] = new SkillDesc(MageTemplate.MeleeDefense, 0, 1, HeadlessCharacter.UntrainedSkill, 0, 0, 0, 0);
        var check = MageTemplate.IsMet(q, plan);
        Assert.True(check.AllMet, check.Summary);
        Assert.Contains(check.Checks, c => c.Label.StartsWith("Melee Defense class", StringComparison.Ordinal) && c.Met);
        Assert.DoesNotContain(check.Checks, c => c.Label.StartsWith("Melee Defense ranks", StringComparison.Ordinal));

        // A trained (or specialised) Melee Defense is short; an absent record counts as untrained.
        q.Skills[MageTemplate.MeleeDefense] = new SkillDesc(MageTemplate.MeleeDefense, 0, 1, HeadlessCharacter.TrainedSkill, 0, 0, 0, 0);
        Assert.Contains("Melee Defense class (untrained)", MageTemplate.IsMet(q, plan).Summary, StringComparison.Ordinal);
        q.Skills.Remove(MageTemplate.MeleeDefense);
        Assert.True(MageTemplate.IsMet(q, plan).AllMet);

        // The gate's template (126) is unchanged: it checks Melee Defense specialised, and the traditional mage is short of it.
        var gate = MageTemplate.PlanAt(xp, MageTemplate.TargetLevel);
        Assert.Equal(MageBuild.Gate126, gate.Build);
        Assert.Contains(gate.Targets, t => t.Kind == MageStatKind.Skill && t.Id == MageTemplate.MeleeDefense && t.Specialized);
        Assert.Contains("Melee Defense", MageTemplate.IsMet(q).Summary, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_traditional_build_trains_all_three_later_skills_from_its_full_level_and_leaves_the_rest_out_by_name_M5_7_F6_D87()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var full = Enumerable.Range(2, MageTemplate.TargetLevel - 2).First(l => MageTemplate.PlanAt(xp, l).LeftOut.Count == 0);
        output.WriteLine($"the traditional mage trains all three from level {full} ({MageTemplate.PlanAt(xp, full).Credits} credits after creation; level {full - 1}: {MageTemplate.PlanAt(xp, full - 1).Credits})");
        Assert.True(full <= MageTemplate.MinLevel, $"level {full} is the first that trains all three; --mage-level takes {MageTemplate.MinLevel} and up");
        Assert.True(MageTemplate.PlanAt(xp, full).Credits >= 22);
        Assert.True(MageTemplate.PlanAt(xp, full - 1).Credits < 22);
        var below = MageTemplate.PlanAt(xp, full - 1);
        Assert.NotEmpty(below.LeftOut);
        Assert.StartsWith("TODO(m5-mage-level-credits)", below.LeftOutLine, StringComparison.Ordinal);
        Assert.DoesNotContain("(all four", below.LeftOutLine, StringComparison.Ordinal);
        Assert.All(below.LeftOut, id => Assert.DoesNotContain(below.Targets, t => t.Kind == MageStatKind.Skill && t.Id == id));

        // 126 itself is the gate build, and the explicit gate plan at 80 is F3's plan (Item Enchantment left out).
        Assert.Equal(MageTemplate.Targets, MageTemplate.PlanAt(xp, MageTemplate.TargetLevel).Targets);
        Assert.Equal([MageTemplate.ItemEnchantment], MageTemplate.PlanAt(xp, Soak, MageBuild.Gate126).LeftOut);
    }

    [RequiresDatsFact]
    public void The_plan_at_126_is_the_pinned_template()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageTemplate.PlanAt(xp, MageTemplate.TargetLevel);
        Assert.Equal(MageTemplate.Targets, plan.Targets);
        Assert.Equal(MageTemplate.XpSpent, plan.Spent);
        Assert.Equal(MageTemplate.XpRemainder, plan.Remainder);
        Assert.Equal(MageTemplate.Level126Xp, plan.TotalXp);
        Assert.Equal(MageTemplate.Level126Credits + MageTemplate.SpareCreationCredits, plan.Credits);
        Assert.Equal(MageTemplate.TrainedLater, plan.Trained);

        // The planner over level 126's experience gives the same targets without the shortcut.
        Assert.Equal(MageTemplate.Targets, MageSpendingPlanner.Compute(xp, xp.XpForLevel(MageTemplate.TargetLevel), [], MageShares.D19).Targets);
        Assert.Equal(MageTemplate.Targets.First(t => t.Id == MageTemplate.Strength && t.Kind == MageStatKind.Attribute).Ranks, GearKitCapacity());
    }

    [RequiresDatsFact]
    public void The_template_check_follows_the_runs_level_a_level_80_mage_meets_its_plan_and_is_short_of_126s()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var plan = MageTemplate.PlanAt(xp, Soak);
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = Soak;
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, MageTemplate.CreationValue(id), 0);
        }

        foreach (var t in plan.Targets)
        {
            switch (t.Kind)
            {
                case MageStatKind.Attribute:
                    q.Attributes[t.Id] = new AttributeDesc(t.Id, (uint)t.Ranks, MageTemplate.CreationValue(t.Id), 1);
                    break;
                case MageStatKind.Vital:
                    q.Vitals[t.Id] = new VitalDesc(t.Id, (uint)t.Ranks, 0, 1, 100);
                    break;
                default:
                    q.Skills[t.Id] = new SkillDesc(t.Id, (ushort)t.Ranks, 1, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, 1, t.Specialized ? 10u : 0u, 0, 0);
                    break;
            }
        }

        var mine = MageTemplate.IsMet(q, plan);
        Assert.True(mine.AllMet, mine.Summary);
        Assert.Contains(mine.Checks, c => c.Label == "level" && c.Expected == Soak && c.Met);
        var theirs = MageTemplate.IsMet(q);
        Assert.False(theirs.AllMet);
        Assert.Contains(theirs.Short, c => c.Label == "level" && c.Expected == MageTemplate.TargetLevel);

        // A kill takes the level past the build's: the check is still met, as the 126 mage's is.
        q.Ints[PlayerQualities.LevelProperty] = Soak + 3;
        Assert.True(MageTemplate.IsMet(q, plan).AllMet);
        q.Ints[PlayerQualities.LevelProperty] = Soak - 1;
        Assert.Contains("level 79/80 SHORT", MageTemplate.IsMet(q, plan).Summary, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_dens_kit_and_purse_fit_a_level_80_mage_of_its_strength_under_the_burden_line_and_the_scarabs_buy_the_hours_printed()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var den = GroundCatalog.Get(FightOptions());
        var plan = MageTemplate.PlanAt(xp, Soak);
        var ranks = plan.RanksOf(MageStatKind.Attribute, MageTemplate.Strength);
        var kit = GearKit.KitPlan.For(den, spells, "kit-test-account", capacityStrength: ranks);
        var real = Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity((int)MageTemplate.LowAttribute + ranks, 0);
        var line = (long)kit.Capacity * den.Supplies.BurdenTripPercent / 100;
        output.WriteLine($"level {Soak}: Strength {MageTemplate.LowAttribute} + {ranks} ranks; {kit.LoadLine}; line {line}; real capacity {real}; headroom under the line {line - kit.Load}");
        Assert.True(kit.Load <= line);
        Assert.True(kit.Load <= (long)real * den.Supplies.BurdenTripPercent / 100);

        // The Pyreal Scarab line is the profile's value; its hours at the lead's measured burn (about 12 a hour): 300 is a day.
        var scarabs = den.Supplies.StockOf(690)!.Keep;
        Assert.True(scarabs >= 12 * 24, $"{scarabs} Pyreal Scarabs are fewer than a day's 12 an hour");
        Assert.Equal(4, GearKit.StackSizes(scarabs, 100).Count); // M5 7 F10: 400, the picker's extra VI casts (the opener, the drains) at the same burn

        // The gate's level 126 casts VII and still passes its load check with the same profile.
        var gate = GearKit.KitPlan.For(den, spells, "kit-test-account");
        Assert.True(gate.Load <= (long)gate.Capacity * den.Supplies.BurdenTripPercent / 100);
        Assert.Equal(GearKit.CapacityStrength, MageTemplate.PlanAt(xp, MageTemplate.TargetLevel).RanksOf(MageStatKind.Attribute, MageTemplate.Strength));
        Assert.Equal(kit.Load, gate.Load);
    }

    private static string FightOptions() => Lifestone.Client.Session.Scenarios.FightOptions.DefaultGround;

    [RequiresDatsFact]
    public void The_dance_shares_give_a_level_80_mage_more_stamina_and_enough_Mana_Conversion_that_a_Stamina_to_Mana_lands_with_little_mana_M5_7_F3_D87()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var dance = MageTemplate.PlanAt(xp, Soak);
        var d19 = MageSpendingPlanner.Compute(xp, xp.XpForLevel(Soak), [], MageShares.D19, MageBuild.Traditional);
        int Ranks(MageLevelPlan p, MageStatKind k, uint id) => p.RanksOf(k, id);
        int Stamina(IReadOnlyList<StatTarget> t) => t.Single(x => x.Kind == MageStatKind.Vital && x.Id == MageTemplate.MaxStamina).Ranks + (int)MageTemplate.LowAttribute + t.Single(x => x.Kind == MageStatKind.Attribute && x.Id == MageTemplate.Endurance).Ranks;
        int Mana(IReadOnlyList<StatTarget> t) => t.Single(x => x.Kind == MageStatKind.Vital && x.Id == MageTemplate.MaxMana).Ranks + (int)MageTemplate.HighAttribute + t.Single(x => x.Kind == MageStatKind.Attribute && x.Id == MageTemplate.Self).Ranks;
        int Mc(IReadOnlyList<StatTarget> t)
        {
            var f = skills.Skills.Single(s => s.Id == MageTemplate.ManaConversion).Formula;
            var focus = MageTemplate.HighAttribute + (uint)t.Single(x => x.Kind == MageStatKind.Attribute && x.Id == MageTemplate.Focus).Ranks;
            var self = MageTemplate.HighAttribute + (uint)t.Single(x => x.Kind == MageStatKind.Attribute && x.Id == MageTemplate.Self).Ranks;
            f.TryCalculate(focus, self, out var baseValue);
            return (int)baseValue + t.Single(x => x.Kind == MageStatKind.Skill && x.Id == MageTemplate.ManaConversion).Ranks;
        }

        output.WriteLine($"dance shares: stamina max {Stamina(dance.Targets)} (D19 shares: {Stamina(d19.Targets)}), mana max {Mana(dance.Targets)} ({Mana(d19.Targets)}), Mana Conversion {Mc(dance.Targets)}");
        foreach (var t in dance.Targets)
        {
            output.WriteLine($"  {t.Name}: dance {t.Ranks} ranks, D19 shares {d19.Targets.Single(x => x.Kind == t.Kind && x.Id == t.Id).Ranks}");
        }

        // Endurance and Max Stamina get more than the equal share, and the stamina pool is larger; the mana pool and the casting skills give up a few ranks for it.
        Assert.True(Ranks(dance, MageStatKind.Attribute, MageTemplate.Endurance) > d19.Targets.Single(x => x.Id == MageTemplate.Endurance && x.Kind == MageStatKind.Attribute).Ranks + 10);
        Assert.True(Stamina(dance.Targets) >= Stamina(d19.Targets) + 30);
        Assert.True(dance.Targets.Single(x => x.Id == MageTemplate.ManaConversion && x.Kind == MageStatKind.Skill).Ranks > 0);

        // A Stamina to Mana VI (base 60, power 250) and VII (base 70, power 300) lands with a fraction of its base mana held: report the chance at 10, 20 and 30 mana.
        var mc = (uint)Mc(dance.Targets);
        foreach (var (name, baseMana, power) in new[] { ("VI", 60u, 250u), ("VII", 70u, 300u) })
        {
            output.WriteLine($"Stamina to Mana {name}: landing chance at 10/20/30 mana = {Lifestone.Client.Session.Casting.ManaConversionModel.LandingChance(baseMana, power, mc, 10):0.00}/{Lifestone.Client.Session.Casting.ManaConversionModel.LandingChance(baseMana, power, mc, 20):0.00}/{Lifestone.Client.Session.Casting.ManaConversionModel.LandingChance(baseMana, power, mc, 30):0.00} (Mana Conversion {mc}; untrained: 0.00 below the base)");
        }

        Assert.True(Lifestone.Client.Session.Casting.ManaConversionModel.LandingChance(60, 250, mc, 30) > 0.5, "a Stamina to Mana VI must land more than half the time at half its base mana");
        Assert.Equal(0.0, Lifestone.Client.Session.Casting.ManaConversionModel.LandingChance(60, 250, 0, 30));
    }

    private static int GearKitCapacity() => Lifestone.Client.Session.Mage.GearKit.CapacityStrength;
}
