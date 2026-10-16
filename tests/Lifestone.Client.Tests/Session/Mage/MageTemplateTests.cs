using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The mage template without the dats: the creation intent, the fixed targets, the expected bases and the read-model check.</summary>
public sealed class MageTemplateTests
{
    private static uint[] StandInClasses()
    {
        var classes = new uint[CharacterCreateIntent.RequiredSkillCount];
        classes[14] = HeadlessCharacter.TrainedSkill;
        classes[22] = HeadlessCharacter.TrainedSkill;
        classes[24] = HeadlessCharacter.TrainedSkill;
        classes[16] = HeadlessCharacter.UntrainedSkill;
        classes[31] = HeadlessCharacter.UntrainedSkill;
        return classes;
    }

    [Fact]
    public void The_creation_intent_is_valid_with_330_attribute_credits_55_classes_and_two_specialised_skills()
    {
        var intent = MageTemplate.CreationIntent("BSD Mage", StandInClasses());
        Assert.True(intent.TryValidate(out var problem), problem);
        Assert.Equal("BSD Mage", intent.Name);
        Assert.Equal((HeadlessCharacter.Heritage, HeadlessCharacter.Gender), (intent.Heritage, intent.Gender));
        Assert.Equal(CharacterCreateIntent.RequiredSkillCount, intent.SkillAdvancementClasses.Count);
        Assert.Equal([10u, 10u, 100u, 10u, 100u, 100u], [intent.Strength, intent.Endurance, intent.Coordination, intent.Quickness, intent.Focus, intent.Self]);
        Assert.Equal(330u, intent.Strength + intent.Endurance + intent.Coordination + intent.Quickness + intent.Focus + intent.Self);

        var classes = intent.SkillAdvancementClasses;
        Assert.Equal(HeadlessCharacter.SpecializedSkill, classes[(int)MageTemplate.WarMagic]);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, classes[(int)MageTemplate.MeleeDefense]);
        Assert.Equal(2, classes.Count(c => c == HeadlessCharacter.SpecializedSkill));
        Assert.Equal(HeadlessCharacter.TrainedSkill, classes[(int)MageTemplate.ArcaneLore]);
        Assert.All(MageTemplate.TrainedLater, id => Assert.NotEqual(HeadlessCharacter.TrainedSkill, classes[(int)id]));
    }

    [Fact]
    public void The_creation_classes_copy_the_retail_ones_and_the_intent_refuses_a_wrong_count()
    {
        var retail = StandInClasses();
        var classes = MageTemplate.CreationSkillClasses(retail);
        Assert.NotSame(retail, classes);
        Assert.Equal(0u, retail[(int)MageTemplate.WarMagic]);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, classes[(int)MageTemplate.WarMagic]);
        Assert.Throws<ArgumentException>(() => MageTemplate.CreationIntent("x", new uint[54]));
    }

    [Fact]
    public void The_credit_plan_fits_the_52_and_the_level_credits()
    {
        Assert.Equal(48, MageTemplate.CreationSkillCredits);
        // D19, Healing dropped by the owner (2026-10-15): the magic skills and Mana Conversion (34) only; 7 of the 41 stay unspent.
        Assert.Equal(34, MageTemplate.TrainCredits.Values.Sum());
        Assert.Equal(MageTemplate.TrainingCredits, MageTemplate.TrainCredits.Values.Sum());
        Assert.True(MageTemplate.TrainingCredits <= (52 - MageTemplate.CreationSkillCredits) + MageTemplate.Level126Credits);
        Assert.Equal([33u, 31u, 32u, 16u], MageTemplate.TrainedLater);
        Assert.DoesNotContain(21u, MageTemplate.TrainCredits.Keys);
        Assert.DoesNotContain(21u, MageTemplate.SurvivalPriority);
        Assert.Equal(MageTemplate.TrainedLater.Order(), MageTemplate.TrainCredits.Keys.Order());
        Assert.Equal([34u, 6u], MageTemplate.SpecializedAtCreation);
        Assert.Equal([14u, 15u, 22u, 24u, 36u, 40u], MageTemplate.CreationTrained);
        Assert.Empty(MageTemplate.TrainedLater.Intersect(MageTemplate.CreationTrained));
    }

    [Fact]
    public void The_survival_credits_go_in_priority_order_skipping_the_trained_and_what_does_not_fit()
    {
        uint[] priority = [15, 24, 21, 22];
        int Cost(uint id) => id switch { 15 => 12, 24 => 4, 21 => 6, 22 => 4, _ => -1 };
        // The real case (Healing dropped by the owner, 2026-10-15): Magic Defense, Run and Jump are trained already, so nothing is bought.
        Assert.Empty(MageSpendingPlanner.ChooseSurvivalSkills(MageTemplate.SurvivalPriority, [15u, 24u, 22u], Cost, 7));
        // The generic rule with Healing in the list: it is bought when it is the only untrained one that fits (6 of 7).
        Assert.Equal([21u], MageSpendingPlanner.ChooseSurvivalSkills(priority, [15u, 24u, 22u], Cost, 7));
        // Nothing trained: Magic Defense (12) does not fit 7, Run (4) does, Healing (6) then does not fit 3, Jump (4) does not fit 3.
        Assert.Equal([24u], MageSpendingPlanner.ChooseSurvivalSkills(priority, [], Cost, 7));
        Assert.Equal([15u, 24u, 21u, 22u], MageSpendingPlanner.ChooseSurvivalSkills(priority, [], Cost, 26));
        Assert.Empty(MageSpendingPlanner.ChooseSurvivalSkills(priority, [], Cost, 3));
        Assert.Empty(MageSpendingPlanner.ChooseSurvivalSkills([99u], [], Cost, 50));
    }

    [Fact]
    public void The_hard_coded_targets_are_21_stats_within_the_rank_caps_and_all_above_the_creation_value()
    {
        Assert.Equal(21, MageTemplate.Targets.Count);
        Assert.Equal(21, MageTemplate.Targets.Select(t => (t.Kind, t.Id)).Distinct().Count());
        Assert.Equal([201, 182, 182, 182, 167, 167, 169, 159, 131, 130, 129, 130, 135, 135, 173, 144, 144, 144, 144, 144, 144], MageTemplate.Targets.Select(t => t.Ranks));
        Assert.All(MageTemplate.Targets, t => Assert.True(t.Ranks > 0, $"{t.Name} has no ranks"));
        // D19: the six attributes (Strength, Endurance, Coordination, Quickness and the magic pair), the three vitals and 12 skills (Healing dropped by the owner, 2026-10-15).
        Assert.Equal(6, MageTemplate.Targets.Count(t => t.Kind == MageStatKind.Attribute));
        Assert.Equal(3, MageTemplate.Targets.Count(t => t.Kind == MageStatKind.Vital));
        Assert.Equal(12, MageTemplate.Targets.Count(t => t.Kind == MageStatKind.Skill));
        // Every trained or specialised skill of the mage has a target: the creation ones, the later ones and the specialised pair.
        Assert.Empty(MageTemplate.CreationTrained.Concat(MageTemplate.TrainedLater).Concat(MageTemplate.SpecializedAtCreation)
            .Except(MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill).Select(t => t.Id)));
        Assert.All(MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Attribute), t => Assert.InRange(t.Ranks, 1, 190));
        Assert.All(MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Vital), t => Assert.InRange(t.Ranks, 1, 196));
        Assert.All(MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill && t.Specialized), t => Assert.InRange(t.Ranks, 1, 226));
        Assert.All(MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill && !t.Specialized), t => Assert.InRange(t.Ranks, 1, 208));
        Assert.True(MageTemplate.XpSpent + MageTemplate.XpRemainder == MageTemplate.Level126Xp);
    }

    [Fact]
    public void The_magic_skill_bases_follow_the_formula_over_the_planned_focus_and_self()
    {
        // Focus and Self are 100 + 167 = 267 each: (267 + 267) / 4 = 133.5, rounded half away from zero to 134.
        Assert.Equal(134u + 10u + 201u, MageTemplate.ExpectedMagicSkillBase(MageTemplate.WarMagic));
        Assert.Equal(134u + 182u, MageTemplate.ExpectedMagicSkillBase(MageTemplate.LifeMagic));
        Assert.Equal(134u + 182u, MageTemplate.ExpectedMagicSkillBase(MageTemplate.CreatureEnchantment));
        Assert.Equal(134u + 182u, MageTemplate.ExpectedMagicSkillBase(MageTemplate.ItemEnchantment));
        Assert.Throws<ArgumentOutOfRangeException>(() => MageTemplate.ExpectedMagicSkillBase(MageTemplate.ManaConversion));
        Assert.Throws<ArgumentOutOfRangeException>(() => MageTemplate.ExpectedMagicSkillBase(MageTemplate.MeleeDefense));
    }

    /// <summary>A read model that meets the template exactly.</summary>
    private static PlayerQualities Met()
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = MageTemplate.TargetLevel;
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, MageTemplate.CreationValue(id), 0);
        }

        foreach (var t in MageTemplate.Targets)
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
                    q.Skills[t.Id] = new SkillDesc(
                        t.Id, (ushort)t.Ranks, 1, t.Specialized ? HeadlessCharacter.SpecializedSkill : HeadlessCharacter.TrainedSkill, 1, t.Specialized ? 10u : 0u, 0, 0);
                    break;
            }
        }

        return q;
    }

    [Fact]
    public void The_finished_mage_has_the_vitals_attributes_and_burden_capacity_of_d19()
    {
        var q = Met();
        Assert.True(q.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var health));
        Assert.True(q.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var stamina));
        Assert.True(q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var mana));

        // Endurance 10 + 130 = 140: health 135 ranks + (140 + 1) / 2 = 70, stamina 135 ranks + 140; mana 159 ranks + Self 267.
        Assert.Equal((205u, 275u, 426u), (health, stamina, mana));
        var attributes = new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Coordination, MageTemplate.Quickness, MageTemplate.Focus, MageTemplate.Self }
            .Select(id => q.Attributes[id].Start + q.Attributes[id].Ranks).ToArray();
        Assert.Equal([141u, 140u, 229u, 140u, 267u, 267u], attributes);

        // Burden capacity is 150 x Strength with no augmentations (retail EncumbranceCapacity, 11.6096 0x004FD7A0).
        Assert.Equal(21_150, Lifestone.Data.Motion.EncumbranceSystem.EncumbranceCapacity((int)attributes[0], 0));
    }

    [Fact]
    public void A_character_that_meets_the_template_is_all_met()
    {
        var check = MageTemplate.IsMet(Met());
        Assert.True(check.AllMet, check.Summary);
        Assert.Equal("ok", check.Summary);
        Assert.Empty(check.Short);
        // The level, six starts, every target's ranks and each skill target's class.
        Assert.Equal(1 + 6 + MageTemplate.Targets.Count + MageTemplate.Targets.Count(t => t.Kind == MageStatKind.Skill), check.Checks.Count);
    }

    [Fact]
    public void One_stat_short_is_reported_by_name_and_nothing_else()
    {
        var q = Met();
        q.Skills[MageTemplate.LifeMagic] = q.Skills[MageTemplate.LifeMagic] with { Ranks = 181 };
        var check = MageTemplate.IsMet(q);
        Assert.False(check.AllMet);
        var shortCheck = Assert.Single(check.Short);
        Assert.Equal("Life Magic ranks", shortCheck.Label);
        Assert.Equal((181L, 182L), (shortCheck.Actual, shortCheck.Expected));
        Assert.Contains("Life Magic ranks 181/182 SHORT", check.Summary);
    }

    [Fact]
    public void A_low_level_an_untrained_skill_a_missing_vital_and_a_wrong_start_are_each_short()
    {
        var q = Met();
        q.Ints[PlayerQualities.LevelProperty] = 125;
        q.Skills[MageTemplate.WarMagic] = q.Skills[MageTemplate.WarMagic] with { AdvancementClass = HeadlessCharacter.TrainedSkill };
        q.Vitals.Remove(MageTemplate.MaxMana);
        q.Attributes[MageTemplate.Focus] = q.Attributes[MageTemplate.Focus] with { Start = 55 };
        var labels = MageTemplate.IsMet(q).Short.Select(c => c.Label).ToArray();
        Assert.Equal(["level", "attribute 5 start", "War Magic class", "Max Mana ranks"], labels);
    }

    [Fact]
    public void An_unknown_character_is_short_everywhere()
    {
        var check = MageTemplate.IsMet(new PlayerQualities());
        Assert.False(check.AllMet);
        Assert.Equal(check.Checks.Count, check.Short.Count());
    }

    [Fact]
    public void More_ranks_than_the_target_still_meets_it()
    {
        var q = Met();
        q.Attributes[MageTemplate.Self] = q.Attributes[MageTemplate.Self] with { Ranks = 190 };
        Assert.True(MageTemplate.IsMet(q).AllMet);
    }
}
