using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>M6 7 B: the climber's book at a low level (<see cref="ClimberBook"/>), plain unit tests (no backend): what the Throwaway's case-(a) build teaches and withholds, read before the live round.</summary>
public sealed class ClimberBookTests
{
    private static HashSet<uint> Taught(int level) => [.. ArenaProfile.SpellsToLearn.Except(ClimberBook.WithheldAt(level))];

    [Fact]
    public void At_level_6_the_book_holds_the_one_creation_buff_and_no_other_buff_line()
    {
        var taught = Taught(6);
        var buffSpells = ArenaProfile.Lines.Where(l => l.IsBuff).SelectMany(l => l.Spells).Select(s => s.Id).ToArray();
        Assert.Contains(ClimberBook.CreationBuff, buffSpells); // a stale id would leave the book with no buff
        Assert.Equal([ClimberBook.CreationBuff], buffSpells.Where(taught.Contains));
    }

    [Fact]
    public void At_level_6_the_fights_and_the_death_stations_spells_are_still_taught()
    {
        var taught = Taught(6);
        var fightLines = ArenaProfile.Lines.Where(l => !l.ByPlay && l.Role is ProfileRole.WarBolt or ProfileRole.ManaTransfer or ProfileRole.StaminaRestore).ToArray();
        Assert.NotEmpty(fightLines);
        foreach (var line in fightLines)
        {
            Assert.All(line.Spells, s => Assert.Contains(s.Id, taught));
        }

        Assert.Contains(ArenaProfile.ExtraNamed("Harm Self")!.Spells[0].Id, taught);
    }

    [Fact]
    public void Lifestone_Recall_is_withheld_below_sixteen_and_taught_from_it()
    {
        Assert.Contains(RecallHome.LifestoneRecallSpell, ClimberBook.WithheldAt(ClimberBook.RecallLevel - 1));
        Assert.DoesNotContain(RecallHome.LifestoneRecallSpell, ClimberBook.WithheldAt(ClimberBook.RecallLevel));
    }

    /// <summary>
    /// M6 7 H: the level KillTaskLiveTests and TrophyLiveTests (b) rely on: the build's first level with Item Enchantment trained, where Lifestone Tie and Lifestone Recall meet the plan's 0.95 on the build's own skill ((Focus + Self) / 4 + the ranks), so <c>tied=yes</c> does not lean on a roll;
    /// one level lower they stand at 0.35 on the untrained skill, the round's four fizzles.
    /// </summary>
    [RequiresDatsFact]
    public void The_build_trains_Item_Enchantment_at_its_level_and_the_tie_and_the_recall_meet_the_plans_chance_there()
    {
        using var dats = DatSet.Open();
        var xp = XpTableData.Load(dats);
        var spells = SpellTableData.Load(dats).Spells;
        Assert.Contains(MageTemplate.ItemEnchantment, MageTemplate.PlanAt(xp, ClimberBook.ItemTrainedBuildLevel - 1, MageBuild.Traditional).LeftOut);
        foreach (var level in new[] { ClimberBook.ItemTrainedBuildLevel, TrophyLiveTests.HuntLevel })
        {
            var plan = MageTemplate.PlanAt(xp, level, MageBuild.Traditional);
            Assert.Contains(MageTemplate.ItemEnchantment, plan.Trained);
            var focus = MageTemplate.HighAttribute + (uint)plan.RanksOf(MageStatKind.Attribute, MageTemplate.Focus);
            var self = MageTemplate.HighAttribute + (uint)plan.RanksOf(MageStatKind.Attribute, MageTemplate.Self);
            var skill = (uint)Math.Round((focus + self) / (double)MageTemplate.MagicSkillDivisor, MidpointRounding.AwayFromZero) + (uint)plan.RanksOf(MageStatKind.Skill, MageTemplate.ItemEnchantment);
            Assert.True(SpellPlan.Chance(skill, spells[BindStation.LifestoneTieSpell].Power) >= 0.95, $"level {level}: Lifestone Tie on Item Enchantment {skill}");
            Assert.True(SpellPlan.Chance(skill, spells[RecallHome.LifestoneRecallSpell].Power) >= 0.95, $"level {level}: Lifestone Recall on Item Enchantment {skill}");
        }
    }

    [Fact]
    public void The_book_leaves_one_buff_line_where_the_whole_one_had_twenty_four()
    {
        var taught = Taught(6);
        Assert.Equal(1, ArenaProfile.Lines.Count(l => l.IsBuff && l.Spells.Any(s => taught.Contains(s.Id))));
    }
}
