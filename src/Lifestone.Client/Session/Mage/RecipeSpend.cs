using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Mage;

/// <summary>M6 audit (fix 10): what a ladder errand's roll needs of the level-up spend: the skill, her current value of it (read by the caller from the SkillTable's formula), the recipe's difficulty and the quest.</summary>
/// <param name="Skill">The roll's skill (ACE's id; the Atlan wand's Alchemy).</param>
/// <param name="Current">Her current skill, the buffs she has now in.</param>
/// <param name="Difficulty">The recipe's difficulty.</param>
/// <param name="Quest">The quest's key, for the log.</param>
public sealed record RecipeSpendFacts(uint Skill, uint Current, uint Difficulty, string Quest);

/// <summary>
/// M6 audit (fix 10, the owner's decision of 2026-11-19: the Atlan wand's roll is a dice roll; the errand starts only once she could reasonably get Alchemy to at least an 80% chance): the ranks the level-up spend buys before the
/// shares so the roll's chance reaches <see cref="RecipeChance.RollFloor"/>, while a ladder errand of her band rolls on the skill. "Reasonably" is the caster rule's reach (<see cref="CasterSpend.Allocations"/>, <see cref="CasterSpend.ReachedBy"/>):
/// the ranks are bought when this allocation and the next two level-ups' can pay them, each rank from one allocation; out of that reach nothing is bought and it is said. Pure.
/// </summary>
public static class RecipeSpend
{
    /// <summary>The push that lifts the roll to <see cref="RecipeChance.RollFloor"/>, or null with the reason in <paramref name="line"/>.</summary>
    /// <param name="table">The XpTable.</param>
    /// <param name="q">Her qualities.</param>
    /// <param name="facts">The roll's facts.</param>
    /// <param name="pool">Her unassigned experience.</param>
    /// <param name="owed">The experience the floors, the lore and the caster take off this allocation first.</param>
    /// <param name="line">The decision, in words.</param>
    public static SkillPush? Push(XpTableData table, PlayerQualities q, RecipeSpendFacts facts, long pool, long owed, out string line)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(facts);
        var c = CultureInfo.InvariantCulture;
        var name = MageTemplate.SkillName(facts.Skill);
        var head = string.Create(c, $"{facts.Quest}'s roll ({name} {facts.Current} against {facts.Difficulty}: chance {RecipeChance.Of(facts.Current, facts.Difficulty):0.000})");
        if (!q.TryGetSkill(facts.Skill, out var held) || held.AdvancementClass < SkillRules.TrainedClass)
        {
            line = head + ": untrained: the credit order trains it first (D136); no rank is bought";
            return null;
        }

        var target = RecipeChance.SkillFor(facts.Difficulty, RecipeChance.RollFloor);
        if (facts.Current >= target)
        {
            line = head + string.Create(c, $": at or over the {RecipeChance.RollFloor:0.00} floor (skill {target}): nothing is bought for it");
            return null;
        }

        var list = held.AdvancementClass >= SkillRules.SpecializedClass ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
        var to = (int)held.Ranks + (int)(target - facts.Current);
        if (to >= list.Count)
        {
            line = head + string.Create(c, $": skill {target} for the {RecipeChance.RollFloor:0.00} floor needs rank {to}, past the table's {list.Count - 1}: nothing is bought; the errand waits");
            return null;
        }

        var prices = new List<long>(to - (int)held.Ranks);
        for (var r = (int)held.Ranks; r < to; r++)
        {
            prices.Add((long)list[r + 1] - list[r]);
        }

        var allocations = CasterSpend.Allocations(table, q, pool, owed, out var reach);
        var by = CasterSpend.ReachedBy(prices, allocations);
        var cost = prices.Sum();
        if (by < 0)
        {
            line = head + string.Create(c, $": skill {target} for the {RecipeChance.RollFloor:0.00} floor costs {cost:N0} for {prices.Count} rank(s), out of reach of {reach}: nothing is bought; the errand waits");
            return null;
        }

        line = head + string.Create(c, $": buy first: ranks {held.Ranks} -> {to} (skill {target}, chance {RecipeChance.Of(target, facts.Difficulty):0.000}) for {cost:N0}, in reach of {reach}");
        return new SkillPush(facts.Skill, to, string.Create(c, $"{facts.Quest}'s roll to {RecipeChance.RollFloor:0.00}"));
    }
}
