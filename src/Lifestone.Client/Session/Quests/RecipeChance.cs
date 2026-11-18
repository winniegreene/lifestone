namespace Lifestone.Client.Session.Quests;

/// <summary>
/// M6 14 the Atlan wand (D136, <c>TODO(m6-credit-need)</c> closed): a recipe's chance, as ACE rolls it. Pure. A recipe with a skill and a difficulty (<c>RecipeManager.HasDifficulty</c>: <c>Skill &gt; 0 &amp;&amp; Difficulty &gt; 0</c>) asks the player's
/// skill trained (an untrained one is <c>YouAreNotTrainedInThatTradeSkill</c>, nothing consumed) and succeeds with <c>SkillCheck.GetSkillChance(Current + LumAugSkilledCraft, Difficulty)</c> (<c>RecipeManager.GetRecipeChance</c>,
/// RecipeManager.cs:159-197 @95ef426d), the logistic <c>1 - 1 / (1 + exp(0.03 x (skill - difficulty)))</c> (<c>SkillCheck.GetSkillChance</c>, SkillCheck.cs:7-12). A recipe with no difficulty always succeeds.
/// </summary>
public static class RecipeChance
{
    /// <summary>ACE's factor of a non-magic skill check (<c>SkillCheck.GetSkillChance</c>'s default).</summary>
    public const double Factor = 0.03;

    /// <summary>ACE's chance that <paramref name="skill"/> beats <paramref name="difficulty"/>.</summary>
    /// <param name="skill">The player's current skill (with the augmentation; the climber has none).</param>
    /// <param name="difficulty">The recipe's difficulty.</param>
    public static double Of(uint skill, uint difficulty)
    {
        var chance = 1.0 - (1.0 / (1.0 + Math.Exp(Factor * ((double)skill - difficulty))));
        return Math.Clamp(chance, 0.0, 1.0);
    }

    /// <summary>The least skill whose chance against <paramref name="difficulty"/> is at least <paramref name="chance"/> (the inverse of <see cref="Of"/>, searched exactly).</summary>
    /// <param name="difficulty">The recipe's difficulty.</param>
    /// <param name="chance">The chance wanted, above 0 and under 1.</param>
    public static uint SkillFor(uint difficulty, double chance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chance, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chance, 1.0);
        var skill = 0u;
        while (Of(skill, difficulty) < chance)
        {
            skill++;
        }

        return skill;
    }
}
