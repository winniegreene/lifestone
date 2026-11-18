using System.Globalization;

namespace Lifestone.Client.Session.Quests;

/// <summary>
/// M6 14 combine (the owner's ruling, 2026-11-17: she can also combine the motes into nuggets and bars to save pack space): the no-roll combine chain a quest file names for a hunted item, read from its <c>combine</c> steps, never a hand list. A link is a
/// combine step whose source and target are the same weenie class (a pair of two objects, both consumed, one result: the Atlan wand's recipes 790, 792 and 791, each <c>success_Amount</c> 1 with both destroy chances 1 in the pinned world
/// SQL) with no roll: no <see cref="QuestStep.RecipeSkill"/> (ACE's <c>HasDifficulty</c> is false: skill and difficulty 0, chance 1.0, RecipeManager.cs:151-164 @95ef426d) and no fail result or fail line. The chain starts at the hunted item and
/// follows each link's result to the next link whose source it is; a step with a roll ends it (the Atlan wand's recipe 789, Alchemy against 150, is never done during the hunt). Each link halves the count, so an item <c>i</c> links up the chain
/// is worth <c>2^i</c> of the hunted item (16 motes = 8 slivers = 4 nuggets = 2 bars; the file's own counts, 8, 4 and 2, say the same).
/// </summary>
public static class QuestCombineChain
{
    /// <summary>The objects a link consumes for one result (a pair of the same weenie class).</summary>
    public const int Pair = 2;

    /// <summary>True when <paramref name="step"/> is a no-roll link: a combine of a weenie class on itself, its result named, no skill, difficulty or fail result.</summary>
    /// <param name="step">The step.</param>
    public static bool IsNoRollLink(QuestStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Kind == QuestStepKind.Combine && step.Item?.Wcid is { } source && step.Target?.Wcid == source && step.Expect.Item?.Wcid is { } result && result != source
            && step.RecipeSkill is null && step.RecipeDifficulty is null && step.FailItem is null && step.FailReply is null;
    }

    /// <summary>
    /// The chain of <paramref name="quest"/> from <paramref name="hunted"/>: the hunted item first, then each no-roll link's result in turn (the Atlan wand: 6353, 6355, 6354, 6329). Only the hunted item when the file names no link from it.
    /// </summary>
    /// <param name="quest">The quest file.</param>
    /// <param name="hunted">The hunt-until's item.</param>
    public static IReadOnlyList<uint> Of(Quest quest, uint hunted)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var chain = new List<uint> { hunted };
        while (quest.Steps.FirstOrDefault(s => IsNoRollLink(s) && s.Item!.Wcid == chain[^1]) is { } link && !chain.Contains(link.Expect.Item!.Wcid!.Value))
        {
            chain.Add(link.Expect.Item!.Wcid!.Value);
        }

        return chain;
    }

    /// <summary>The worth of the chain's item <paramref name="index"/> in units of its item <paramref name="from"/> (2 to the links between them).</summary>
    public static long Weight(int index, int from = 0) => index < from ? 0 : 1L << (index - from);

    /// <summary>What <paramref name="held"/> counts of the chain's items are worth in units of its item <paramref name="from"/> and above (the hunted item's equivalents with <paramref name="from"/> 0).</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="held">The count held of a weenie class.</param>
    /// <param name="from">The chain's index the units are counted in; items below it are not counted.</param>
    public static long Units(IReadOnlyList<uint> chain, Func<uint, int> held, int from = 0)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(held);
        long units = 0;
        for (var i = from; i < chain.Count; i++)
        {
            units += held(chain[i]) * Weight(i, from);
        }

        return units;
    }

    /// <summary>
    /// M6 14 combine3: the one count of a hunt-until wherever the autopilot reads it (the errand's "a hunt that gained", the sell-first room): the step's item and everything its quest's no-roll chain makes of it, in the item's equivalents
    /// (<see cref="Units"/> over <see cref="Of"/>), as the fight's until and the quest station count it. A step with no item counts 0; one with no chain counts the item itself.
    /// </summary>
    /// <param name="quest">The quest file.</param>
    /// <param name="hunt">Its hunt-until step.</param>
    /// <param name="held">The count held of a weenie class.</param>
    public static int HuntHeld(Quest quest, QuestStep hunt, Func<uint, int> held)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(hunt);
        ArgumentNullException.ThrowIfNull(held);
        return hunt.Item?.Wcid is { } item ? (int)Units(Of(quest, item), held) : 0;
    }

    /// <summary>
    /// M6 14 combine3: the hunt's pack along <paramref name="chain"/> as the fight keeps it (<c>FightScenario.Combine</c>): from what is held now, one item of the chain's first at a time until <paramref name="want"/> equivalents are held, each
    /// followed by every pair combined, lowest link first. The counts held of each item of the chain at the end, and the most objects (one a slot: each item of the Atlan wand's chain stacks to 1) held at once on the way, the new item before its
    /// combine included. Nothing is added when the equivalents held already reach <paramref name="want"/>.
    /// </summary>
    /// <param name="chain">The chain (<see cref="Of"/>).</param>
    /// <param name="held">The count held of a weenie class now.</param>
    /// <param name="want">The equivalents the hunt ends at.</param>
    public static (int[] Final, int PeakObjects) Simulate(IReadOnlyList<uint> chain, Func<uint, int> held, int want)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(held);
        var counts = chain.Select(held).ToArray();
        var peak = counts.Sum();
        while (Units(chain, w => counts[IndexOf(chain, w)]) < want)
        {
            counts[0]++;
            peak = Math.Max(peak, counts.Sum());
            for (var i = 0; i < counts.Length - 1; i++)
            {
                while (counts[i] >= Pair)
                {
                    counts[i] -= Pair;
                    counts[i + 1]++;
                }
            }
        }

        return (counts, peak);
    }

    private static int IndexOf(IReadOnlyList<uint> chain, uint wcid)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            if (chain[i] == wcid)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The counts held along the chain for a log line: <c>6353 x3, 6355 x2, 6354 x0, 6329 x0</c> with names when given.</summary>
    public static string Describe(IReadOnlyList<uint> chain, Func<uint, int> held, Func<uint, string>? name = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(held);
        return string.Join(", ", chain.Select(w => string.Create(CultureInfo.InvariantCulture, $"{(name is null ? w.ToString(CultureInfo.InvariantCulture) : name(w))} x{held(w)}")));
    }

    /// <summary>The spec argument form: the chain's weenie classes, comma-separated (<c>6353,6355,6354,6329</c>).</summary>
    public static string Format(IReadOnlyList<uint> chain) => string.Join(",", chain.Select(w => w.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The chain the argument <paramref name="text"/> names, or null with <paramref name="error"/> (two or more distinct weenie classes).</summary>
    public static IReadOnlyList<uint>? Parse(string? text, out string error)
    {
        error = string.Empty;
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var chain = new List<uint>();
        foreach (var p in parts)
        {
            if (!uint.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var w) || w == 0 || chain.Contains(w))
            {
                error = $"combine='{text}' is not a list of distinct weenie classes";
                return null;
            }

            chain.Add(w);
        }

        if (chain.Count < 2)
        {
            error = $"combine='{text}' names fewer than two weenie classes";
            return null;
        }

        return chain;
    }
}
