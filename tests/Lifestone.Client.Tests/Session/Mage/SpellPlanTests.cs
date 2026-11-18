using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 a (D4, D41, D26): the spell plan by level. What these prove: the plan's chance is ACE's <c>SkillCheck.GetMagicSkillChance</c> through the client's one copy (<see cref="GearCasts.Chance"/>), and its 95% line is 43 over
/// the power (0.9530; 42 over is 0.9498) at every level of the war line; <see cref="SpellPlan.LevelAt(uint, double)"/> is the highest level at the chance, at both edges of each level; and, with the retail dats, the climber's
/// projected base War Magic (the creation's 60, the planner's ranks at each level's total experience) and every rung's <c>spell_level</c> in <c>Grounds/ladder/war-mage.json</c> equal to the computation, its source text
/// naming the projected skills (the file is computed, not assumed); and the seven elements' bolt lines read from the SpellTable. The dat tests skip without the dats.
/// </summary>
public sealed class SpellPlanTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII"];

    [Fact]
    public void The_chance_is_ACEs_through_the_clients_one_formula_and_the_95_line_is_43_over_the_power()
    {
        Assert.Equal(0.9530, SpellPlan.Chance(93, 50), 4); // ACE: 1 - 1 / (1 + exp(0.07 x 43))
        Assert.Equal(0.9498, SpellPlan.Chance(92, 50), 4); // 42 over: a hair under 95%
        Assert.Equal(0.5, SpellPlan.Chance(250, 250), 6);
        Assert.Equal(0.0, SpellPlan.Chance(0, 1)); // no skill
        Assert.Equal(0.0, SpellPlan.Chance(49, 100)); // more than 50 under the power fails outright (Player_Magic.cs:534)
        Assert.True(SpellPlan.Chance(50, 100) > 0);
        foreach (var power in SpellPlan.WarLinePowers)
        {
            Assert.Equal(GearCasts.Chance(power + 43, power), SpellPlan.Chance(power + 43, power));
            Assert.True(SpellPlan.Chance(power + 43, power) >= 0.95, $"power {power}: 43 over");
            Assert.True(SpellPlan.Chance(power + 42, power) < 0.95, $"power {power}: 42 over");
        }
    }

    [Fact]
    public void The_level_at_a_chance_is_the_highest_whose_chance_reaches_it_at_both_edges()
    {
        var chance = LadderLoader.WarMage.PlanCastChance;
        Assert.Equal(new uint[] { 1, 50, 100, 150, 200, 250, 300, 400 }, SpellPlan.WarLinePowers); // the profile's Flame Bolt line, the SpellTable's powers (BsdProfileSpellsTests)
        for (var level = 2; level <= SpellPlan.WarLinePowers.Count; level++)
        {
            var power = SpellPlan.WarLinePowers[level - 1];
            Assert.Equal(level, SpellPlan.LevelAt(power + 43, chance));
            Assert.Equal(level - 1, SpellPlan.LevelAt(power + 42, chance));
        }

        Assert.Equal(1, SpellPlan.LevelAt(44, chance)); // level I's power is 1: 44 is its 95% line
        Assert.Equal(0, SpellPlan.LevelAt(43, chance));
        Assert.Equal(0, SpellPlan.LevelAt(0, chance));
        Assert.Equal(2, SpellPlan.LevelAt(106, chance)); // Winnie at level 10 (War Magic 106 current): II at 0.980, III at 0.60
        Assert.Equal(3, SpellPlan.LevelAt(106, 0.5)); // the chance is the caller's (the ladder's), never a constant here
        Assert.Equal(2, SpellPlan.LevelAt(93, chance, [1, 50]));
    }

    [RequiresDatsFact]
    public void The_climbers_projected_base_War_Magic_is_the_creations_plus_the_planners_ranks()
    {
        var table = XpTableData.Load(fixture.Dats);
        Assert.Equal(60u, SpellPlan.ProjectedWarBase(table, 1)); // round((100 + 100) / 4) + 10 specialised, no experience
        var previous = 0u;
        for (var level = 1; level <= LadderLoader.WarMage.TopLevel; level++)
        {
            var skill = SpellPlan.ProjectedWarBase(table, level);
            Assert.True(skill >= previous, $"level {level}: {skill} under {previous}");
            previous = skill;
        }

        // The computed values the ladder file's sources name (a change in the planner or the table moves them, and the rung test below says where).
        Assert.Equal(110u, SpellPlan.ProjectedWarBase(table, 10)); // M6 item 14: 110 on the climber's split (MageShares.Climber), one over the 109 of the plan with no floor. Before it, 109: M6 7 finish (the owner, 2026-11-08): each stat aims at 5% and none passes 10%, and what a capped stat leaves goes to the magic skills; II at 0.95 either way
        Assert.Equal(344u, SpellPlan.ProjectedWarBase(table, 126)); // M6 item 14: the climber's split at every level (MageTemplate.SharesFor), 344 as the D19 split gave under the cap; M6 7 finish: 346 before the stat cap
    }

    [RequiresDatsFact]
    public void Every_rungs_spell_level_in_the_ladder_file_is_the_computation()
    {
        var table = XpTableData.Load(fixture.Dats);
        var ladder = LadderLoader.WarMage;
        Assert.Equal(0.95, ladder.PlanCastChance);
        foreach (var rung in ladder.Rungs)
        {
            IReadOnlyList<uint> powers = rung.Element is null ? SpellPlan.WarLinePowers : [.. SpellPlan.BoltLine(rung.Element, null, out _)!.Select(s => s.Power)];
            var plan = SpellPlan.PlanFor(table, rung.Band, ladder.PlanCastChance, powers);
            var from = SpellPlan.ProjectedWarBase(table, rung.Band.From);
            var to = SpellPlan.ProjectedWarBase(table, rung.Band.To);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{rung.Key} {rung.Band.From}-{rung.Band.To}: War Magic {from}-{to}: {Numerals[plan.Min - 1]}-{Numerals[plan.Max - 1]} (file {rung.SpellLevel.Min}-{rung.SpellLevel.Max})"));
            Assert.True(plan == rung.SpellLevel, $"{rung.Key}: the file says {rung.SpellLevel.Min}-{rung.SpellLevel.Max}, the plan computes {plan.Min}-{plan.Max} (War Magic {from} at {rung.Band.From}, {to} at {rung.Band.To}): write the computed value in war-mage.json");
            var source = rung.Sources["spell_level"];
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"{from} at level {rung.Band.From} ({Numerals[plan.Min - 1]} at 0.95"), source, StringComparison.Ordinal);
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"{to} at level {rung.Band.To} ({Numerals[plan.Max - 1]} at 0.95"), source, StringComparison.Ordinal);
            Assert.Contains("by the fizzle rule (D4, D41)", source, StringComparison.Ordinal);
        }
    }

    [RequiresDatsFact]
    public void Each_elements_bolt_line_is_read_from_the_SpellTable()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var firsts = new Dictionary<string, uint> { ["fire"] = 27, ["cold"] = 28, ["acid"] = 58, ["electric"] = 75, ["pierce"] = 86, ["bludgeon"] = 64, ["slash"] = 92 }; // sel-scroll-rows.tsv, 20215's rows 78002-78020
        foreach (var element in Ladder.Elements)
        {
            var line = SpellPlan.BoltLine(element, spells, out var why);
            Assert.True(line is not null, why);
            output.WriteLine($"{element}: {why}: {string.Join(", ", line.Select(s => $"{s.Id} {s.Name} {s.Power}"))}");
            Assert.Equal(firsts[element], line[0].Id);
            Assert.True(line.Count >= 6, $"{element}: {line.Count} levels");
            for (var i = 0; i < 6; i++)
            {
                Assert.Equal(SpellPlan.WarLinePowers[i], line[i].Power);
                Assert.Equal(SpellPlan.BoltLineNames[element] + " " + Numerals[i], line[i].Name);
                Assert.Equal(MagicSchool.WarMagic, spells[line[i].Id].School);
                Assert.Equal(line[i].Power, spells[line[i].Id].Power);
            }

            if (element != "fire")
            {
                Assert.Equal(6, line.Count);
                Assert.Contains("TODO(m6-bolt-line-vii)", why, StringComparison.Ordinal);
            }
        }
    }
}
