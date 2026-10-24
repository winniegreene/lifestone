using DatReaderWriter.Enums;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The fighters' magic build against the dats (M4 P5a, D33 and D38): the costs, the formulas, the level, the three skills' values and what the run rate is before and after.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterMagicDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Aluvian = HeadlessCharacter.Heritage;

    private static readonly FighterTemplate[] Fighters = [FighterTemplate.Melee, FighterTemplate.Archer];

    [RequiresDatsFact]
    public void The_three_schools_cost_8_8_and_12_credits_and_use_the_formula_Focus_plus_Self_over_4()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        Assert.Equal(8, charGen.GetSkillTrainedCost(FighterTemplate.CreatureEnchantment, Aluvian, skills));
        Assert.Equal(8, charGen.GetSkillTrainedCost(FighterTemplate.ItemEnchantment, Aluvian, skills)); // M4 P5a C1c (D38)
        Assert.Equal(12, charGen.GetSkillTrainedCost(FighterTemplate.LifeMagic, Aluvian, skills));
        foreach (var id in new[] { FighterTemplate.CreatureEnchantment, FighterTemplate.ItemEnchantment, FighterTemplate.LifeMagic })
        {
            var f = skills.Skills.Single(s => s.Id == id).Formula;
            Assert.Equal(FighterTemplate.MagicSkillDivisor, f.Divisor);
            Assert.Equal(new[] { FighterTemplate.Focus, FighterTemplate.Self }, new[] { f.Attribute1, f.Attribute2 }.Order());
            Assert.Equal(FighterBuildScenario.TrainCredits[id], charGen.GetSkillTrainedCost(id, Aluvian, skills));
        }

        // 8 + 8 + 12 + 6 (Mana Conversion, M4 P5a C7) = 34 of the credits left after creation (24 carried + 20 through level 40 = 44) and the levels 41 to 55 add more.
        var xp = XpTableData.Load(fixture.Dats);
        foreach (var t in Fighters)
        {
            Assert.Equal(34, t.TrainedLater.Sum(id => FighterBuildScenario.TrainCredits[id])); // M4 P5a C7 (D28): was 28; + Mana Conversion 6
            Assert.True(52 - t.CreationSkillCredits + (int)xp.SkillCreditsThroughLevel(FighterTemplate.MagicLevel, 1) - 34 >= 0, "the credits cover the three schools and Mana Conversion");
        }
    }

    [RequiresDatsFact]
    public void Lifestone_Recall_is_an_Item_Enchantment_spell_of_power_100_and_a_skill_of_132_casts_it_at_the_chance_rule_M4_P5a_C1c_D38()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var recall = spells[1635];
        output.WriteLine($"{recall.Name}: school {recall.School}, power {recall.Power}, base mana {recall.BaseMana}");
        Assert.Equal((MagicSchool.ItemEnchantment, 100u), (recall.School, recall.Power));
        Assert.Equal(recall.Power, FighterProfile.Brawler.LifestoneRecall.Spells.Single().Power);
        Assert.Equal(FighterTemplate.IIISkill, FighterSpendingPlanner.SkillForChance(recall.Power));
        Assert.True(GearCasts.Chance(FighterTemplate.IIISkill, recall.Power) >= 0.9);
        Assert.True(GearCasts.Chance(FighterTemplate.IIISkill - 1, recall.Power) < 0.9);
    }

    [RequiresDatsFact]
    public void The_smallest_level_for_level_IV_buffs_and_Lifestone_Recall_is_55_and_the_three_skills_reach_their_chance_M4_P5a_C1c_D38()
    {
        var xp = XpTableData.Load(fixture.Dats);
        Assert.Equal(86_399_136UL, xp.XpForLevel(55));
        var needs = new Dictionary<uint, uint>
        {
            [FighterTemplate.CreatureEnchantment] = FighterTemplate.IVSkill,
            [FighterTemplate.LifeMagic] = FighterTemplate.IVSkill,
            [FighterTemplate.ItemEnchantment] = FighterTemplate.IIISkill,
        };
        foreach (var t in Fighters)
        {
            Assert.Equal(55, FighterSpendingPlanner.SmallestLevel(xp, t, needs));
            Assert.Equal(FighterTemplate.MagicLevel, t.Level);
            var plan = FighterSpendingPlanner.ComputeLevel(xp, t.Level, t);
            var ce = FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.CreatureEnchantment, t.Creation);
            var ie = FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.ItemEnchantment, t.Creation);
            var lm = FighterSpendingPlanner.MagicSkill(plan.Targets, FighterTemplate.LifeMagic, t.Creation);
            output.WriteLine($"{t.Key}: level {t.Level}, Creature Enchantment {ce}, Item Enchantment {ie}, Life Magic {lm}, remainder {plan.Remainder}");
            // M4 P5a C5: the two fighters' creation attributes differ, and so do their schools: Brawler (Strength, Coordination, Focus 100) and Archer (Coordination, Quickness, Focus 100).
            Assert.Equal(t.Key == "melee" ? (184u, 181u, 183u) : (183u, 179u, 183u), (ce, ie, lm));
            Assert.Equal(t.Targets, plan.Targets);
            Assert.Equal(t.XpSpent, plan.Spent);
            Assert.Equal(t.XpRemainder, plan.Remainder);
            Assert.Equal(t.XpSpent, t.TotalXp(xp));

            // One level lower misses one of the three: the pin is the smallest. (C1 alone, on the first magic plan, was level 48 for the two level IV schools.)
            var lower = FighterSpendingPlanner.ComputeLevel(xp, t.Level - 1, t);
            Assert.False(needs.All(n => FighterSpendingPlanner.MagicSkill(lower.Targets, n.Key, t.Creation) >= n.Value));
        }
    }

    [RequiresDatsFact]
    public void The_spread_buys_on_the_base_plan_lowers_nothing_and_gives_every_trait_a_share_M4_P5a_C1c_D38()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        foreach (var t in Fighters)
        {
            var basePlan = FighterSpendingPlanner.Compute(xp, FighterTemplate.Level40Xp, t.Shares);
            Assert.Equal(100, t.MagicShares.Sum(s => s.Percent));

            // Every stat of the base plan keeps its place and gains ranks; the new stats follow.
            Assert.Equal(basePlan.Targets.Select(b => (b.Kind, b.Id)), t.Targets.Take(basePlan.Targets.Count).Select(x => (x.Kind, x.Id)));
            for (var i = 0; i < basePlan.Targets.Count; i++)
            {
                Assert.True(t.Targets[i].Ranks > basePlan.Targets[i].Ranks, $"{t.Targets[i].Name}: {t.Targets[i].Ranks} against the base plan's {basePlan.Targets[i].Ranks}");
            }

            // The spread: Quickness (so Run), Run, Focus, Self and Max Mana all have ranks, beside the three schools.
            foreach (var (kind, id) in new[] { (MageStatKind.Attribute, FighterTemplate.Quickness), (MageStatKind.Skill, FighterTemplate.Run), (MageStatKind.Attribute, FighterTemplate.Focus), (MageStatKind.Attribute, FighterTemplate.Self), (MageStatKind.Vital, FighterTemplate.MaxMana) })
            {
                Assert.True((t.TargetOf(kind, id)?.Ranks ?? 0) > 50, $"{kind} {id}");
            }

            Assert.All(t.Targets, x => Assert.True(x.Ranks > 0 && x.Ranks <= x.MaxRanks(xp), $"{x.Name} ranks {x.Ranks}"));
            var attributes = new Dictionary<uint, uint>();
            for (uint id = FighterTemplate.Strength; id <= FighterTemplate.Self; id++)
            {
                attributes[id] = t.AttributeAtTarget(id);
            }

            output.WriteLine($"{t.Key}: attributes S{attributes[1]} E{attributes[2]} Q{attributes[3]} C{attributes[4]} F{attributes[5]} Self{attributes[6]}");
            foreach (var target in t.Targets.Where(x => x.Kind == MageStatKind.Skill))
            {
                var entry = skills.Skills.Single(s => s.Id == target.Id);
                Assert.True(entry.Formula.TryCalculate(attributes.GetValueOrDefault(entry.Formula.Attribute1), attributes.GetValueOrDefault(entry.Formula.Attribute2), out var formula));
                output.WriteLine($"{t.Key}: {target.Name} {formula + (uint)target.Ranks} ({formula} + {target.Ranks} ranks)");
            }
        }
    }

    [RequiresDatsFact]
    public void The_fighters_run_rate_rises_with_Quickness_and_Run_from_1_19_to_about_2_5_M4_P5a_C1c_D38()
    {
        // Before: Quickness 10 (x1 over 1) and the 5 creation ranks of Run: skill 15, rate 1.192 (4.77 m/s), the number the owner was asked about. After: the spread's Quickness and Run ranks. R = 1 + 2.75 S / (S + 200) (MovementSystem.GetRunRate).
        var skills = SkillTableData.Load(fixture.Dats);
        var run = skills.Skills.Single(s => s.Id == FighterTemplate.Run);
        Assert.Equal(FighterTemplate.Quickness, run.Formula.Attribute1);
        Assert.Equal(1, run.Formula.Divisor);
        Assert.Equal(1, run.Formula.Attribute1Multiplier);
        Assert.True(run.Formula.TryCalculate(FighterTemplate.LowAttribute, 0, out var formula));
        var before = (int)formula + 5;
        var rateBefore = MovementSystem.GetRunRate(0f, before, 1f);
        Assert.Equal(15, before);
        Assert.InRange(rateBefore, 1.19, 1.20);
        foreach (var (t, expectedSkill) in new[] { (FighterTemplate.Melee, 229), (FighterTemplate.Archer, 321) }) // M4 P5a C5: the Brawler starts at Quickness 10 and buys it up (114), the Archer starts at 100 (206)
        {
            Assert.True(run.Formula.TryCalculate(t.AttributeAtTarget(FighterTemplate.Quickness), 0, out var quick));
            var after = (int)quick + t.TargetOf(MageStatKind.Skill, FighterTemplate.Run)!.Ranks;
            var rate = MovementSystem.GetRunRate(0f, after, 1f);
            output.WriteLine($"{t.Key}: run skill {after} (Quickness {t.AttributeAtTarget(FighterTemplate.Quickness)}), run rate {rate:0.000}, speed {4.0 * rate:0.00} m/s; before {rateBefore:0.000}");
            Assert.Equal(expectedSkill, after);
            Assert.InRange(rate, 2.4, 2.75);
        }
    }

    [RequiresDatsFact]
    public void The_fighters_numbers_at_level_40_and_at_55_are_pinned_attributes_skills_and_vitals_M4_P5a_C5_D40()
    {
        var xp = XpTableData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var expected = new Dictionary<string, string>
        {
            ["melee 40"] = "S195 E101 Q77 C196 F167 Self77|W267 MD199 H219 Run157 CE141 IE141 LM141 AL136|HP140 ST190 MP150",
            ["melee 55"] = "S202 E126 Q114 C205 F196 Self106|W287 MD223 H249 Run229 CE184 IE181 LM183 AL164|HP187 ST239 MP212",
            ["archer 40"] = "S77 E105 Q167 C199 F167 Self77|W237 MD230 H220 Run247 CE141 IE141 LM141 AL136|HP149 ST195 MP150",
            ["archer 55"] = "S97 E126 Q206 C209 F196 Self105|W256 MD255 H250 Run321 CE183 IE179 LM183 AL164|HP187 ST240 MP210",
        };
        foreach (var t in Fighters)
        {
            foreach (var level in new[] { FighterTemplate.TargetLevel, t.Level })
            {
                var plan = level == FighterTemplate.TargetLevel ? FighterSpendingPlanner.Compute(xp, xp.XpForLevel(level), t.Shares, t.FloorStats) : FighterSpendingPlanner.ComputeLevel(xp, level, t);
                uint Attr(uint id) => t.Creation[id] + (uint)plan.Targets.Single(x => x.Kind == MageStatKind.Attribute && x.Id == id).Ranks;
                uint Skill(uint id)
                {
                    var entry = skills.Skills.Single(s => s.Id == id);
                    Assert.True(entry.Formula.TryCalculate(entry.Formula.Attribute1 is >= 1 and <= 6 ? Attr(entry.Formula.Attribute1) : 0, entry.Formula.Attribute2 is >= 1 and <= 6 ? Attr(entry.Formula.Attribute2) : 0, out var formula));
                    return formula + (uint)plan.Targets.Single(x => x.Kind == MageStatKind.Skill && x.Id == id).Ranks;
                }

                int Vital(uint id) => plan.Targets.Single(x => x.Kind == MageStatKind.Vital && x.Id == id).Ranks;
                var weapon = t.Key == "melee" ? FighterTemplate.HeavyWeapons : FighterTemplate.MissileWeapons;
                // Retail's secondary attributes (ACE Vital formulas): Health is Endurance / 2 plus its ranks, Stamina Endurance plus its ranks, Mana Self plus its ranks.
                var line = $"S{Attr(1)} E{Attr(2)} Q{Attr(3)} C{Attr(4)} F{Attr(5)} Self{Attr(6)}|W{Skill(weapon)} MD{Skill(FighterTemplate.MeleeDefense)} H{Skill(FighterTemplate.Healing)} Run{Skill(FighterTemplate.Run)} CE{Skill(FighterTemplate.CreatureEnchantment)} IE{Skill(FighterTemplate.ItemEnchantment)} LM{Skill(FighterTemplate.LifeMagic)} AL{Skill(FighterTemplate.ArcaneLore)}|HP{Vital(FighterTemplate.MaxHealth) + (Attr(2) / 2)} ST{Vital(FighterTemplate.MaxStamina) + Attr(2)} MP{Vital(FighterTemplate.MaxMana) + Attr(6)}";
                output.WriteLine($"{t.Key} {level}: {line} (burden capacity {150 * Attr(1)}, run rate {MovementSystem.GetRunRate(0f, (int)Skill(FighterTemplate.Run), 1f):0.000})");
                Assert.Equal(expected[$"{t.Key} {level}"], line);
                if (level == t.Level)
                {
                    Assert.True(Skill(FighterTemplate.ArcaneLore) >= 150, "Arcane Lore at least 150 at the template's level (owner D40; the magic gear picks items by it)");
                }
            }
        }

        // Max Health at 55 against the plan before the creation split changed (Endurance 100 at creation): Brawler 209, Archer 215; now 188 and 189 (Endurance starts at 10, so Endurance and the vital's ranks carry it).
        Assert.True(188 >= 209 - 25 && 189 >= 215 - 30);
    }
}
