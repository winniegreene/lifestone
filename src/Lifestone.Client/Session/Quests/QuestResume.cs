using System.Globalization;

namespace Lifestone.Client.Session.Quests;

/// <summary>Where a quest's run starts (<see cref="QuestResume.From"/>): the step index and why.</summary>
/// <param name="Step">The first step to run (0: the file's start).</param>
/// <param name="Why">The products held that let the run start there, or why it starts at 0.</param>
public sealed record QuestResumePoint(int Step, string Why);

/// <summary>
/// M6 14 the Atlan wand (a chain of 26 steps whose first hunt is about 400 kills: a run cut after the combines must not hunt the motes again): where a quest's run starts from what she holds. Pure. A step's product is what it leaves in the
/// pack: a hunt-until's item to its count, a combine's or an apply's result (one a use), a give's expected item, a pick-up's item, a buy's item. The run starts after the latest product step <c>k</c> whose product she holds to its count and
/// whose later steps find every input that the steps up to <c>k</c> make (a recipe's source and target, twice the count when they are one weenie class; a give's item; a wear's item): a stone held from another errand does not skip the forging,
/// because its apply also wants the wand. None: the file's start.
/// </summary>
public static class QuestResume
{
    /// <summary>The step the run of <paramref name="quest"/> starts at, from <paramref name="held"/>.</summary>
    /// <param name="quest">The quest.</param>
    /// <param name="held">How many of a weenie class the pack and the worn slots hold.</param>
    public static QuestResumePoint From(Quest quest, Func<uint, int> held)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(held);
        var steps = quest.Steps;
        for (var k = steps.Count - 1; k >= 0; k--)
        {
            if (ProductOf(steps[k]) is not { } product || held(product.Wcid) < product.Count)
            {
                continue;
            }

            var need = new Dictionary<uint, int>();
            for (var s = k + 1; s < steps.Count; s++)
            {
                foreach (var (wcid, count) in InputsOf(steps[s]))
                {
                    if (LastProducer(steps, wcid, s) is { } producer && producer <= k)
                    {
                        need[wcid] = need.GetValueOrDefault(wcid) + count;
                    }
                }
            }

            if (need.All(n => held(n.Key) >= n.Value))
            {
                return new QuestResumePoint(k + 1, string.Create(CultureInfo.InvariantCulture, $"step {k} ({QuestStepKinds.Word(steps[k].Kind)}: {steps[k].What}) is made: {product.Wcid} x{product.Count} held{(need.Count == 0 ? string.Empty : "; the later steps' inputs held: " + string.Join(", ", need.Select(n => $"{n.Key} x{n.Value} ({held(n.Key)})")))}"));
            }
        }

        return new QuestResumePoint(0, "no step's product is held with the inputs the steps after it need: the file's start");
    }

    /// <summary>The product a step leaves in the pack (weenie class, count), or null.</summary>
    /// <param name="step">The step.</param>
    public static (uint Wcid, int Count)? ProductOf(QuestStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Kind switch
        {
            QuestStepKind.HuntUntil or QuestStepKind.Buy when step.Item?.Wcid is { } w => (w, step.Count ?? 1),
            QuestStepKind.PickUp when step.Item?.Wcid is { } w => (w, 1),
            QuestStepKind.Combine or QuestStepKind.Apply when step.Expect.Item?.Wcid is { } w => (w, step.Count ?? 1),
            QuestStepKind.Give when step.Expect.Item?.Wcid is { } w => (w, 1),
            _ => null,
        };
    }

    /// <summary>The weenie classes a step uses from the pack, with their counts.</summary>
    /// <param name="step">The step.</param>
    public static IEnumerable<(uint Wcid, int Count)> InputsOf(QuestStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var times = step.Count ?? 1;
        switch (step.Kind)
        {
            case QuestStepKind.Combine or QuestStepKind.Apply when step.Item?.Wcid is { } source && step.Target?.Wcid is { } target:
                if (source == target)
                {
                    yield return (source, 2 * times);
                }
                else
                {
                    yield return (source, times);
                    yield return (target, times);
                }

                break;
            case QuestStepKind.Give or QuestStepKind.Wear when step.Item?.Wcid is { } item:
                yield return (item, 1);
                break;
        }
    }

    private static int? LastProducer(IReadOnlyList<QuestStep> steps, uint wcid, int before)
    {
        for (var j = before - 1; j >= 0; j--)
        {
            if (ProductOf(steps[j]) is { } p && p.Wcid == wcid)
            {
                return j;
            }
        }

        return null;
    }
}
