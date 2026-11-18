using System.Globalization;

namespace Lifestone.Client.Session.Quests;

/// <summary>What <see cref="RecipeSkillPlan.Next"/> asks before a roll.</summary>
public enum RecipeSkillMoveKind
{
    /// <summary>Train the skill from the credits (its trained cost).</summary>
    Train,

    /// <summary>Raise the skill one rank for <see cref="RecipeSkillMove.Price"/> experience.</summary>
    Raise,

    /// <summary>Roll now: the chance reached the plan's, or nothing more can be bought (<see cref="RecipeSkillMove.Why"/> says which).</summary>
    Roll,

    /// <summary>Stop by name: the skill is untrained and the credits do not cover it (ACE would use nothing).</summary>
    Untrained,
}

/// <summary>One answer of <see cref="RecipeSkillPlan.Next"/>.</summary>
/// <param name="Kind">The move.</param>
/// <param name="Price">The experience a raise costs, or the credits a train costs; 0 otherwise.</param>
/// <param name="Chance">ACE's chance on the current skill (0 while untrained).</param>
/// <param name="Why">The numbers, for the log line.</param>
public sealed record RecipeSkillMove(RecipeSkillMoveKind Kind, long Price, double Chance, string Why);

/// <summary>
/// M6 14 the Atlan wand (D132, D136): the skill a roll needs, made ready before the roll, one move at a time (the station's check, act, check runs each). Pure. Untrained: train it when the credits cover its trained cost, else the stop. Trained: while
/// ACE's chance on the current skill (<see cref="RecipeChance.Of"/>) is under <c>wanted</c> (the ladder's <c>plan_cast_chance</c>), raise one rank when the unassigned experience pays the XpTable's price of the next rank; else roll, saying why.
/// One rank raises the current skill by one (ACE's <c>CreatureSkill.Current</c> adds the ranks to the formula; an enchantment's multiplier is read again from the live state after each raise).
/// </summary>
public static class RecipeSkillPlan
{
    /// <summary>The next move.</summary>
    /// <param name="trained">The skill is trained or specialised.</param>
    /// <param name="credits">Her available skill credits.</param>
    /// <param name="trainedCost">The SkillTable's trained cost.</param>
    /// <param name="current">Her current skill (when trained).</param>
    /// <param name="ranks">Its ranks.</param>
    /// <param name="xpList">The XpTable's cumulative list for its class (trained or specialised).</param>
    /// <param name="unassigned">Her unassigned experience.</param>
    /// <param name="difficulty">The recipe's difficulty.</param>
    /// <param name="wanted">The chance wanted.</param>
    public static RecipeSkillMove Next(bool trained, int credits, int trainedCost, uint current, int ranks, IReadOnlyList<uint> xpList, long unassigned, uint difficulty, double wanted)
    {
        ArgumentNullException.ThrowIfNull(xpList);
        var c = CultureInfo.InvariantCulture;
        if (!trained)
        {
            return trainedCost > 0 && credits >= trainedCost
                ? new RecipeSkillMove(RecipeSkillMoveKind.Train, trainedCost, 0, string.Create(c, $"untrained: train for {trainedCost} of {credits} credits"))
                : new RecipeSkillMove(RecipeSkillMoveKind.Untrained, trainedCost, 0, string.Create(c, $"untrained, and her credits ({credits}) do not cover its trained cost ({trainedCost})"));
        }

        var chance = RecipeChance.Of(current, difficulty);
        if (chance >= wanted)
        {
            return new RecipeSkillMove(RecipeSkillMoveKind.Roll, 0, chance, string.Create(c, $"skill {current} against {difficulty}: chance {chance:0.0000} reaches the plan's {wanted}"));
        }

        if (ranks + 1 >= xpList.Count)
        {
            return new RecipeSkillMove(RecipeSkillMoveKind.Roll, 0, chance, string.Create(c, $"skill {current} at its last rank ({ranks}): chance {chance:0.0000}, under the plan's {wanted} (TODO(m6-roll-under-plan))"));
        }

        var price = (long)xpList[ranks + 1] - xpList[ranks];
        return price <= unassigned
            ? new RecipeSkillMove(RecipeSkillMoveKind.Raise, price, chance, string.Create(c, $"skill {current} against {difficulty}: chance {chance:0.0000} under the plan's {wanted}: rank {ranks} -> {ranks + 1} for {price:N0} of {unassigned:N0} unassigned"))
            : new RecipeSkillMove(RecipeSkillMoveKind.Roll, 0, chance, string.Create(c, $"skill {current} against {difficulty}: chance {chance:0.0000}, under the plan's {wanted}: the next rank costs {price:N0} and {unassigned:N0} is unassigned (TODO(m6-roll-under-plan))"));
    }
}
