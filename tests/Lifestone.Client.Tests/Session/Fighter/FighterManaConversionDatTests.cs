using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>Mana Conversion (skill 16) for the Brawler and the Archer against the dats (M4 P5a C7): its trained cost, its value at level 55 and what it takes off a cast's mana, by ACE's <c>GetManaCost</c> (<c>Creature_Magic.cs</c> 54-80 at 95ef426d).</summary>
[Trait("Tier", "Exhaustive")]
public sealed class FighterManaConversionDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Aluvian = HeadlessCharacter.Heritage;

    /// <summary>The Healer's Heart's ManaConversionMod (PropertyFloat 144 of weenie 8552, the pinned world SQL): a caster in the hand in a combat mode gives the skill x 1.05 (<c>GetWeaponManaConversionModifier</c>, <c>WorldObject_Weapon.cs</c> 235-262).</summary>
    private const double CasterModifier = 1.05;

    /// <summary>ACE's expected mana cost of a cast of base cost <paramref name="baseCost"/> and power <paramref name="power"/> at the Mana Conversion skill <paramref name="skill"/> (the two skill checks, the shared luck roll; a grid over the three rolls).</summary>
    internal static double ExpectedCost(uint baseCost, uint power, uint skill)
    {
        const int N = 100;
        double Chance(double s, double d) => Math.Clamp(1.0 - (1.0 / (1.0 + Math.Exp(0.03 * (s - d)))), 0.0, 1.0);
        var p1 = Chance(skill, power / 2);
        var p2 = Chance(skill, power);
        double sum = 0;
        for (var a = 0; a < N; a++)
        {
            var roll = (a + 0.5) / N;
            for (var b = 0; b < N; b++)
            {
                var luck = (b + 0.5) / N;
                var cost = (double)baseCost;
                if (roll < p1)
                {
                    cost = Math.Round(baseCost * (1.0 - (p1 - (roll * luck))), MidpointRounding.ToEven);
                }

                if (cost > 1)
                {
                    for (var c = 0; c < N; c++)
                    {
                        var roll2 = (c + 0.5) / N;
                        var second = roll2 < p2 ? Math.Round(cost * (1.0 - (p2 - (roll2 * luck))), MidpointRounding.ToEven) : cost;
                        sum += Math.Max(second, 1);
                    }
                }
                else
                {
                    sum += Math.Max(cost, 1) * N;
                }
            }
        }

        return sum / (N * N * (double)N);
    }

    [RequiresDatsFact]
    public void Mana_Conversion_costs_6_credits_and_the_fighters_have_it_at_level_55_M4_P5a_C7()
    {
        var charGen = CharGenData.Load(fixture.Dats);
        var skills = SkillTableData.Load(fixture.Dats);
        var xp = XpTableData.Load(fixture.Dats);
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        Assert.Equal(6, charGen.GetSkillTrainedCost(FighterTemplate.ManaConversion, Aluvian, skills));
        Assert.Equal(FighterBuildScenario.TrainCredits[FighterTemplate.ManaConversion], charGen.GetSkillTrainedCost(FighterTemplate.ManaConversion, Aluvian, skills));
        foreach (var t in new[] { FighterTemplate.Melee, FighterTemplate.Archer })
        {
            var plan = FighterSpendingPlanner.ComputeLevel(xp, t.Level, t);
            var entry = skills.Skills.Single(s => s.Id == FighterTemplate.ManaConversion);
            uint Attr(uint id) => t.Creation[id] + (uint)plan.Targets.Single(x => x.Kind == MageStatKind.Attribute && x.Id == id).Ranks;
            Assert.True(entry.Formula.TryCalculate(entry.Formula.Attribute1 is >= 1 and <= 6 ? Attr(entry.Formula.Attribute1) : 0, entry.Formula.Attribute2 is >= 1 and <= 6 ? Attr(entry.Formula.Attribute2) : 0, out var formula));
            var ranks = plan.Targets.Single(x => x.Kind == MageStatKind.Skill && x.Id == FighterTemplate.ManaConversion).Ranks;
            var mc = formula + (uint)ranks;
            var withCaster = (uint)Math.Round(mc * CasterModifier);
            output.WriteLine($"{t.Key}: Mana Conversion {mc} ({formula} + {ranks} ranks), x{CasterModifier} with the caster in the hand {withCaster}; formula divisor {entry.Formula.Divisor}");
            Assert.Equal((6, 50u, 108), (entry.Formula.Divisor, formula, ranks));
            Assert.Equal((158u, 166u), (mc, withCaster)); // the floor's ranks (0.1% of the level's experience) and the 2% share; (Focus 196 + Self 106) / 6 = 50
            var buff = spells[t.Key == "melee" ? 421u : 470u];
            var harm = spells[VitalsMeans.HarmSelf[4]];
            var buffCost = ExpectedCost(buff.BaseMana, buff.Power, withCaster);
            var harmCost = ExpectedCost(harm.BaseMana, harm.Power, withCaster);
            output.WriteLine($"  {buff.Name}: base {buff.BaseMana}, power {buff.Power}: expected {buffCost:0.0} ({100.0 * (1 - (buffCost / buff.BaseMana)):0}% less); {harm.Name}: base {harm.BaseMana}, power {harm.Power}: expected {harmCost:0.0} ({100.0 * (1 - (harmCost / harm.BaseMana)):0}% less)");
            Assert.InRange(buffCost, 11.5, 13.5); // a level IV buff: 50 -> about 12 (75% less)
            Assert.InRange(harmCost, 27.0, 30.0); // Harm Self V: 60 -> about 28 (53% less)

            // The credits: the heritage's 52, the creation's spend carried, the levels' grants, less the three schools (28) and Mana Conversion (6): 13 are left.
            Assert.Equal(13, 52 - t.CreationSkillCredits + (int)xp.SkillCreditsThroughLevel(FighterTemplate.MagicLevel, 1) - t.TrainedLater.Sum(id => FighterBuildScenario.TrainCredits[id]));
            Assert.Equal(plan.Spent, t.XpSpent);
            Assert.Equal(plan.Remainder, t.XpRemainder);
            Assert.True(plan.Remainder < xp.XpForLevel(56) - xp.XpForLevel(55), "the plan fits the level");
        }
    }
}
