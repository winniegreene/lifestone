using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Tests;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 sprint (the owner's rule of 2026-11-19: a scroll that drops is picked up and read when she does not know it): with the SpellTable the keep rule keeps every scroll whose spell the book lacks and ACE lets her read now
/// (Player.CanReadScroll, Player_Skills.cs:458-471 @95ef426d: power under 50 or from 300 needs no skill, else the school trained and its skill at least the power less 50), any school and level, a line the buff bot casts included;
/// a known one, or one ACE would refuse now, is the loot rules'. Without the SpellTable the plan lines' rule stands (ScrollKeepRuleTests).
/// </summary>
public sealed class ScrollReadRuleDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static PlayerQualities Her(uint warRanks)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Ints[PlayerQualities.LevelProperty] = 12;
        q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 0, 100, 0);
        q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 0, 100, 0);
        q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, (ushort)warRanks, 1, 3u, 0, 10u, 0, 0);
        return q;
    }

    [RequiresDatsFact]
    public void Every_unknown_scroll_she_can_read_is_kept_and_a_known_or_unreadable_one_is_not()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var line = ScrivenerCatalog.Default.LineSpells("Flame Bolt").Where(s => s.Level > 0).OrderBy(s => s.Level).ToList();
        var first = spells[line[0].SpellId];
        var sixth = spells[line[^1].SpellId];
        Assert.True(first.Power < 50, $"level I's power {first.Power} needs no skill");
        Assert.InRange(sixth.Power, 50u, 299u);
        var q = Her(0);
        Assert.True(Lifestone.Client.Session.Scenarios.Journey.ResupplyRun.CanRead(q, sixth.School, sixth.Power, out _) != Lifestone.Client.Session.Scenarios.Journey.LearnReason.None, "level VI is over her War Magic now");

        var book = new Spellbook { IsKnown = true };
        var scroll = ScrollKeepRuleTests.Scroll(1, "Scroll of " + first.Name, first.Id);
        var kept = ScrollKeepRule.Evaluate(scroll, [], book, q, 0.95, spells);
        Assert.Equal(ScrollKeepKind.Keep, kept.Kind);
        Assert.Equal(first.Id, kept.Level!.SpellId);
        Assert.Contains("not in her book and ACE lets her read it", kept.Reason, StringComparison.Ordinal);

        var high = ScrollKeepRule.Evaluate(ScrollKeepRuleTests.Scroll(2, "Scroll of " + sixth.Name, sixth.Id), [], book, q, 0.95, spells);
        Assert.Equal(ScrollKeepKind.Unreadable, high.Kind); // left to the loot rules; the trip reads it once she can

        book.Learn(first.Id);
        Assert.Equal(ScrollKeepKind.NotAbove, ScrollKeepRule.Evaluate(scroll, [], book, q, 0.95, spells).Kind);
    }
}
