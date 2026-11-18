using System.Globalization;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Mage;

/// <summary>A job's bolt line: the quest, the ground it hunts, the element its creatures take best and the line's name, with the sums that chose it.</summary>
/// <param name="Quest">The quest's key.</param>
/// <param name="Ground">The ground its hunt-until or kill step fights on.</param>
/// <param name="Element">The element (a key of <see cref="SpellPlan.BoltLineNames"/>).</param>
/// <param name="Line">The bolt line's name.</param>
/// <param name="Why">The sums, for the log line.</param>
public sealed record JobBoltNeed(string Quest, string Ground, string Element, string Line, string Why);

/// <summary>
/// M6 14 the element line, the learn half (the owner, 2026-11-16: the spell plan buys the bolt a job needs): which bolt line a ground's creatures take best, from facts only. Pure. Every element's line hits as hard as any other level by level
/// (<see cref="WarSpellData"/>), so the line a ground wants is the element whose resistances (<see cref="CreatureResists"/>), summed over the creatures the fight hunts there, are the largest: each slot of the ground's respawn whose cell the walk
/// reaches (<see cref="HuntingGround.IsWalkable"/>; the slots past a floorless ramp are not hunted) counts once, its creature's resistance to each element added; a ground with no such slot (outdoors) counts each target once. A tie goes to fire
/// (the ladder's line, already in her book). The Crater Caves: 24 Nubilous Golems (electric 1.0, cold 0.85) and 4 Cursed Wisps (electric 0.9, cold 0) reached: electric 27.6, cold 20.4, so Lightning Bolt.
/// </summary>
public static class JobBolt
{
    /// <summary>The element <paramref name="ground"/>'s hunted creatures take best, with the sums in <paramref name="why"/>.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="why">The sums by element, the largest first.</param>
    public static string ElementFor(HuntingGround ground, out string why)
    {
        ArgumentNullException.ThrowIfNull(ground);
        var hunted = ground.Respawn.Slots.Where(s => ground.IsWalkable(s.Cell)).Select(s => s.Wcid).ToList();
        var source = hunted.Count > 0 ? $"{hunted.Count} hunted slots" : $"{ground.Targets.Count} targets, one each (no slot the walk reaches)";
        if (hunted.Count == 0)
        {
            hunted = [.. ground.Targets.Select(t => t.Wcid)];
        }

        var sums = SpellPlan.BoltLineNames.Keys
            .Select(e => (Element: e, Sum: hunted.Sum(w => Bestiary.ResistOf(w, e))))
            .OrderByDescending(x => Math.Round(x.Sum, 6))
            .ThenBy(x => x.Element == "fire" ? 0 : 1)
            .ToList();
        why = string.Create(CultureInfo.InvariantCulture, $"{ground.Key}, {source}: {string.Join(", ", sums.Select(x => $"{x.Element} {x.Sum:0.##}"))}");
        return sums[0].Element;
    }

    /// <summary>
    /// The bolt lines <paramref name="quests"/> need: for each quest, each ground its hunt-until and kill steps fight on whose best element (<see cref="ElementFor"/>) is not fire (the ladder's line, already hers), one need, the quests'
    /// order; a line two quests need is named once.
    /// </summary>
    /// <param name="quests">The quests the job will run (the due ladder errands, the quest task).</param>
    public static IReadOnlyList<JobBoltNeed> Needs(IEnumerable<Quest> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        var needs = new List<JobBoltNeed>();
        foreach (var quest in quests)
        {
            foreach (var key in quest.Steps.Where(s => s.Kind is QuestStepKind.HuntUntil or QuestStepKind.Kill && s.Ground is not null).Select(s => s.Ground!).Distinct(StringComparer.Ordinal))
            {
                if (GroundCatalog.TryGet(key) is not { } ground)
                {
                    continue;
                }

                var element = ElementFor(ground, out var why);
                var line = SpellPlan.BoltLineNames[element];
                if (element != "fire" && !needs.Any(n => n.Line == line))
                {
                    needs.Add(new JobBoltNeed(quest.Key, key, element, line, why));
                }
            }
        }

        return needs;
    }

    /// <summary>The skills <paramref name="quests"/>' rolls need trained (each recipe step's <see cref="QuestStep.RecipeSkill"/>), distinct, in order.</summary>
    /// <param name="quests">The quests the job will run.</param>
    public static IReadOnlyList<uint> Skills(IEnumerable<Quest> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        return [.. quests.SelectMany(q => q.Steps).Where(s => s.RecipeSkill is not null).Select(s => s.RecipeSkill!.Value).Distinct()];
    }
}
