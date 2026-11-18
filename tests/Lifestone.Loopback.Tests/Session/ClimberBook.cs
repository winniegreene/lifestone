using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 7 B (<c>TODO(m6-lowrung-loopback-book)</c>, M6 6 live round 2): the book a Lifestone Throwaway is built with when it stands in for the climber at a low level. The den mage's build learns the arena profile's whole book (24 buff lines), whose 125 mana at level 6
/// cannot carry the buff station's 900 s (0 of 24); the climber at 6 knows only what its plan bought (<see cref="BuffProfiles.Climber"/>: every buff line is by play; item 0's climb: <c>buffs=1/1 levels=Armor:I</c>, the one buff the creation gives, Armor Self I 24).
/// So the build withholds every buff spell but that one, and Lifestone Recall (D43) below <see cref="RecallLevel"/>, and teaches the rest (the war and life-drain lines, the mana lines, the extras: what the fights and the death station cast).
/// Pure and tested offline (<c>ClimberBookTests</c>), so the live build's list is not first read at the round.
/// </summary>
public static class ClimberBook
{
    /// <summary>The level from which the climber holds Lifestone Recall (D43: below it the book has none, so the bind's tie is skipped, <c>tied=skipped</c>).</summary>
    public const int RecallLevel = 16;

    /// <summary>
    /// M6 7 H: the first level at which the build (<see cref="Lifestone.Client.Session.Scenarios.MageBuildScenario"/>, <see cref="MageTemplate.PlanAt"/>, <see cref="MageTemplate.TrainingPriority"/>: Creature, Mana Conversion, then Item Enchantment) trains Item Enchantment: 22 credits, the XpTable's at 32. The climber trains it at 16
    /// by its own order (D43); the build does not follow that order, so a case that needs Lifestone Tie and Lifestone Recall cast is built here (at 31 they fizzle: 0.35 on the untrained skill). Pinned against the XpTable and the SpellTable, 0.95 and more (<c>ClimberBookTests</c>).
    /// </summary>
    public const int ItemTrainedBuildLevel = 32;

    /// <summary>Armor Self I, the one buff spell the creation gives (item 0's climb at level 6: <c>levels=Armor:I</c>).</summary>
    public const uint CreationBuff = 24;

    /// <summary>The spells the build does not teach at <paramref name="level"/>: every buff line's spells but <see cref="CreationBuff"/> (by play for the climber), and Lifestone Recall 1635 below <see cref="RecallLevel"/>.</summary>
    public static IReadOnlyList<uint> WithheldAt(int level)
    {
        var withheld = new List<uint>();
        foreach (var line in ArenaProfile.Lines.Where(l => l.IsBuff))
        {
            withheld.AddRange(line.Spells.Select(s => s.Id).Where(id => id != CreationBuff));
        }

        if (level < RecallLevel)
        {
            withheld.Add(RecallHome.LifestoneRecallSpell);
        }

        return withheld;
    }
}
