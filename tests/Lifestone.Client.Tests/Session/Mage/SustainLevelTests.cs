using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 sprint (live 2026-11-19 03:16 to 03:23 UTC, run 4, level 9, Life Magic 158 standing): the sustain lines (Heal Self, Drain Health Other) planned every level the catalog holds, capped only by what ACE lets her read, so
/// the plan bought Heal Self IV (cast chance 0.636) and Drain Health Other IV (her level at the plan's 0.95 was II) and sent her by the Town Network to Dryreach for Heal Self V (chance 0.050, 10,000 pyreals). The levels now stop
/// at the plan's level on her skill (level I at the least), lowest first, as M6 7 AN meant them.
/// </summary>
public sealed class SustainLevelTests
{
    private static PlayerQualities Mage(int skill, int level)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = level;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, (ushort)(skill - 50), 1, 2u, 0, 0u, 0, 0);
        return q;
    }

    private static IReadOnlyList<LearnNeed> Needs(int skill, int level, Spellbook book)
    {
        var ladder = LadderLoader.WarMage;
        const string Ground = "holtburg-redoubt";
        return ScrivenerLearnPlan.Needs(GroundCatalog.Get(Ground), ScrivenerLearnPlan.RungOf(ladder, Ground, level)!, ladder.PlanCastChance, Mage(skill, level), book, ScrivenerCatalog.Default, ladder);
    }

    [Theory]
    [InlineData("Heal Self")]
    [InlineData("Drain Health Other")]
    public void At_Life_Magic_158_a_sustain_line_plans_no_level_above_the_one_she_casts_at_the_plan_chance(string line)
    {
        var catalog = ScrivenerCatalog.Default;
        var spells = catalog.LineSpells(line);
        var top = Math.Max(1, ScrivenerLearnPlan.TargetLevel(158, LadderLoader.WarMage.PlanCastChance, [.. spells.Select(s => s.Power)], 0));
        var needs = Needs(158, 9, new Spellbook { IsKnown = true }).Where(n => n.Line.Name == line).ToList();

        Assert.NotEmpty(needs);
        Assert.All(needs, n => Assert.InRange(n.Target, 0, top)); // live: Heal Self IV, V (off-home: the Dryreach trip) and VI; Drain Health Other IV, V and VI
        Assert.DoesNotContain(needs, n => n.Kind == LearnNeedKind.OffHome);
        Assert.Equal(Enumerable.Range(1, top), needs.Select(n => n.Target).Order()); // every level up to hers, lowest first: a fresh book is not left without I
    }

    [Fact]
    public void With_little_Life_Magic_a_sustain_line_still_plans_its_level_I()
    {
        var needs = Needs(60, 5, new Spellbook { IsKnown = true }).Where(n => n.Line.Name == "Drain Health Other").ToList();
        Assert.Contains(needs, n => n.Target == 1);
    }
}
