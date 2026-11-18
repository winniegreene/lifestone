using System.Globalization;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>What <see cref="SkillCreditPlan.Next"/> asks of the character now.</summary>
public enum CreditStepKind
{
    /// <summary>Train <see cref="CreditStep.Skill"/>: it is the order's next untrained skill and the credits cover its cost.</summary>
    Train,

    /// <summary>The order's next untrained skill costs more than the credits: wait for <see cref="CreditStep.Short"/> more (the order is kept: a cheaper skill after it is never trained first).</summary>
    Wait,

    /// <summary>Every skill of the order is trained: the credits stay unspent.</summary>
    Done,
}

/// <summary>One answer of the credit plan.</summary>
/// <param name="Kind">Train, wait or done.</param>
/// <param name="Skill">The order's next untrained skill (null when done).</param>
/// <param name="Cost">Its trained cost (0 when done).</param>
/// <param name="Credits">The credits the character has.</param>
/// <param name="Short">The credits still missing (0 unless waiting).</param>
public sealed record CreditStep(CreditStepKind Kind, uint? Skill, int Cost, int Credits, int Short)
{
    /// <summary>The step for a log line.</summary>
    public override string ToString() => Kind switch
    {
        CreditStepKind.Train => string.Create(CultureInfo.InvariantCulture, $"train {MageTemplate.SkillName(Skill!.Value)} for {Cost} of {Credits} credits"),
        CreditStepKind.Wait => string.Create(CultureInfo.InvariantCulture, $"wait for {Short} credits: {MageTemplate.SkillName(Skill!.Value)} costs {Cost}, the character has {Credits}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"every skill of the order is trained ({Credits} credits stay unspent)"),
    };
}

/// <summary>One skill of the credit order and its trained cost (the SkillTable's, for the heritage).</summary>
/// <param name="Skill">The skill id.</param>
/// <param name="Cost">The credits <c>TrainSkill</c> sends (ACE's <c>HandleActionTrainSkill</c> insists on the SkillTable's trained cost).</param>
public sealed record CreditLine(uint Skill, int Cost);

/// <summary>
/// M6 item 1 b: the climber's skill credits from level 1 (closes <c>m6-climber-creation-credits</c>). Pure. The credits ACE grants at a level-up are the XpTable's <c>CharacterLevelSkillCreditList</c> entry of the new level
/// (<see cref="XpTableData.CharacterLevelSkillCreditList"/>; ACE adds it at each level a gain crosses, @95ef426d <c>WorldObjects/Player_Xp.cs:295-296</c>); the character starts with what the creation left
/// (<see cref="MageTemplate.ClimberCreationPlan.Left"/>: 4 of the Aluvian 52). The order is the creation's unpaid lines (<see cref="MageTemplate.ClimberCreationPlan.Unpaid"/>, <see cref="MageTemplate.ClimberCreationOrder"/> after War
/// Magic and Life Magic specialised: Creature Enchantment, Item Enchantment, Mana Conversion, M6 D43) with the costs the creation read from the dats, never restated here.
/// M6 item 14 S3: a creation planned for the buff bot (<see cref="MageTemplate.ClimberCreationOrderWithBot"/>, the option on) gives the order Item Enchantment, Mana Conversion, Creature Enchantment; a character keeps what she trained (<see cref="Next"/> skips it), so the order only decides what is trained next.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The order's first skill the character holds untrained is the next; it is trained when the credits cover its cost, else the plan waits for the difference (a later, cheaper skill is not trained first:
/// the order is the template's). A skill the order names that the character already holds trained or specialised is skipped. Only a trained class is bought: specialising after creation is not part of the plan (an unpaid
/// specialisation is left out of the order).</para>
/// <para><b>Never.</b> Melee Defense (D5: the traditional mage soaks the damage) is never in the order, whatever the creation's lines say.</para>
/// <para><b>On a need.</b> M6 14 D136 (the owner's ruling, 2026-11-13, amending D132's rule of Alchemy as soon as the credits reach its cost): Alchemy (<see cref="MageTemplate.ClimberCreditsOnNeed"/>) joins the order, last, at the SkillTable's
/// trained cost the creation read, only when a job needs it (<see cref="OrderOf"/>'s <c>needed</c>); until then the credits bank. Never before the creation's lines, whatever they say; never raised with experience (it is not on
/// <see cref="MageTemplate.ClimberSpendSkills"/>: the raises are the wand chain's).</para>
/// </remarks>
public static class SkillCreditPlan
{
    /// <summary>The skills the climber never trains from its credits: Melee Defense (D5). M6 14: Alchemy left this list (D34 reversed for the climber): it is trained on a need (<see cref="MageTemplate.ClimberCreditsOnNeed"/>, D136).</summary>
    public static IReadOnlyList<uint> Never { get; } = [MageTemplate.MeleeDefense];

    /// <summary>
    /// The credit order: the creation's unpaid trained lines, in the creation's order, without <see cref="Never"/>'s skills; then (M6 14 D136) those of the creation's <see cref="MageTemplate.ClimberCreationPlan.Last"/> lines (Alchemy)
    /// that a job needs, last. With no need named the order ends with the creation's lines and the credits bank.
    /// </summary>
    /// <param name="creation">The climber's creation plan (<see cref="MageTemplate.PlanClimberCreation"/>, its costs read from the dats).</param>
    /// <param name="needed">Whether a job needs a held skill now (D136); null: none does.</param>
    public static IReadOnlyList<CreditLine> OrderOf(MageTemplate.ClimberCreationPlan creation, Func<uint, bool>? needed = null)
    {
        ArgumentNullException.ThrowIfNull(creation);
        return [.. creation.Unpaid
            .Where(u => u.WantedClass == HeadlessCharacter.TrainedSkill && !Never.Contains(u.Skill) && !MageTemplate.ClimberCreditsOnNeed.Contains(u.Skill) && creation.ClassOf(u.Skill) is null)
            .Select(u => new CreditLine(u.Skill, u.Cost)),
            .. (creation.Last ?? []).Where(l => !Never.Contains(l.Skill) && creation.ClassOf(l.Skill) is null && needed?.Invoke(l.Skill) == true)];
    }

    /// <summary>The credits ACE grants on reaching <paramref name="level"/> (the XpTable's entry; level 1's is never granted: the creation is level 1).</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="level">The level reached, 2 to the table's last.</param>
    public static int GrantedAt(XpTableData table, int level)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 2);
        return (int)table.CharacterLevelSkillCreditList[level];
    }

    /// <summary>The credits a character that has trained nothing since its creation holds at <paramref name="level"/>: the creation's <paramref name="creationLeft"/> and the levels 2 up to <paramref name="level"/>.</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="creationLeft">The credits the creation left.</param>
    /// <param name="level">The level, 1 to the table's last.</param>
    public static int EarnedThrough(XpTableData table, int creationLeft, int level)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return creationLeft + (int)table.SkillCreditsThroughLevel(level, 1);
    }

    /// <summary>The plan's answer for a character with <paramref name="credits"/> credits that holds trained the skills <paramref name="trained"/> says.</summary>
    /// <param name="order">The credit order (<see cref="OrderOf"/>).</param>
    /// <param name="credits">The character's available skill credits.</param>
    /// <param name="trained">Whether the character holds a skill trained or specialised.</param>
    public static CreditStep Next(IReadOnlyList<CreditLine> order, int credits, Func<uint, bool> trained)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(trained);
        foreach (var line in order)
        {
            if (Never.Contains(line.Skill) || trained(line.Skill))
            {
                continue;
            }

            return line.Cost <= credits
                ? new CreditStep(CreditStepKind.Train, line.Skill, line.Cost, credits, 0)
                : new CreditStep(CreditStepKind.Wait, line.Skill, line.Cost, credits, line.Cost - credits);
        }

        return new CreditStep(CreditStepKind.Done, null, 0, credits, 0);
    }

    /// <summary>
    /// The level each skill of the order is trained at by a climber that trains at every level-up as soon as the credits cover it (the plan's own arithmetic, for the tests and the notes): from <paramref name="creationLeft"/> at
    /// level 1, each level's grant added, the order trained in turn.
    /// </summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="order">The credit order.</param>
    /// <param name="creationLeft">The credits the creation left.</param>
    /// <param name="toLevel">The last level walked.</param>
    public static IReadOnlyList<(uint Skill, int Level, int CreditsLeft)> Schedule(XpTableData table, IReadOnlyList<CreditLine> order, int creationLeft, int toLevel)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(order);
        var done = new HashSet<uint>();
        var schedule = new List<(uint, int, int)>();
        var credits = creationLeft;
        for (var level = 1; level <= toLevel; level++)
        {
            credits += level >= 2 ? GrantedAt(table, level) : 0;
            while (Next(order, credits, done.Contains) is { Kind: CreditStepKind.Train } step)
            {
                credits -= step.Cost;
                done.Add(step.Skill!.Value);
                schedule.Add((step.Skill.Value, level, credits));
            }
        }

        return schedule;
    }
}
