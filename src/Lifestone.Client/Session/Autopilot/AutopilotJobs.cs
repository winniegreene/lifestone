using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>M6 6 MA: what the job menu reads, all of it given (the menu is pure).</summary>
/// <param name="Job">The install job (the den's row reads <c>hunt here</c>).</param>
/// <param name="Level">The character's level.</param>
/// <param name="NextLevelXp">The experience from the level to the next.</param>
/// <param name="Quests">The quest catalog.</param>
/// <param name="Timers">The character's quest timers.</param>
/// <param name="Now">Now (UTC).</param>
/// <param name="LadderQuestKeys">The keys of the ladder's quest rungs (the ladder runs those, the menu does not list them).</param>
public sealed record JobMenuInput(AutopilotJob Job, int Level, long NextLevelXp, IReadOnlyList<Quest> Quests, QuestTimers Timers, DateTimeOffset Now, IReadOnlyCollection<string> LadderQuestKeys);

/// <summary>M6 6 MA: builds the job menu (L1, L2).</summary>
public static class JobMenu
{
    /// <summary>The stop row's label.</summary>
    public const string StopLabel = JobLabels.Stop;

    /// <summary>The level-up row's label.</summary>
    public const string LevelUpLabel = JobLabels.LevelUp;

    /// <summary>The den's row label.</summary>
    public const string HuntLabel = JobLabels.Hunt;

    /// <summary>The rows: the install job's loop, one per listed quest (experience descending, then key), the errand, stop; the quests left out are in <see cref="JobList.Refused"/>.</summary>
    /// <param name="input">The input.</param>
    public static JobList Build(JobMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var jobs = new List<JobEntry>();
        var refused = new List<JobEntry>();
        var den = input.Job == AutopilotJob.Den;
        jobs.Add(new JobEntry(
            AutopilotTask.Job,
            den ? HuntLabel : LevelUpLabel,
            null,
            null,
            den
                ? "the den job's own fight loop, here"
                : "the ladder and the trophy errand; the round is item 9's: TODO(m6-menu-round)"));

        var listed = new List<JobEntry>();
        foreach (var quest in input.Quests)
        {
            var (refusal, xp) = Judge(input, quest);
            var task = AutopilotTask.ForQuest(quest.Key);
            if (refusal is null)
            {
                listed.Add(new JobEntry(task, "do a quest: " + quest.Name, xp, null, $"{quest.Name}, level {quest.Level}"));
            }
            else
            {
                refused.Add(new JobEntry(task, quest.Name, xp, refusal, refusal));
            }
        }

        jobs.AddRange(listed.OrderByDescending(e => e.Experience ?? -1).ThenBy(e => e.Task.QuestKey, StringComparer.Ordinal));
        jobs.Add(new JobEntry(AutopilotTask.Errand, "errand", null, null, "one logistics trip: the vendors and the way back"));
        jobs.Add(new JobEntry(AutopilotTask.Job, StopLabel, null, null, "stop the run where it stands"));
        refused.Sort((a, b) => string.CompareOrdinal(a.Task.QuestKey, b.Task.QuestKey));
        return new JobList(input.Level, jobs, refused);
    }

    /// <summary>The menu's summary line: <c>autopilot menu: level=&lt;n&gt; next_xp=&lt;n&gt; listed=&lt;n&gt;/&lt;catalog&gt; refused=&lt;key&gt;:&lt;why&gt;,...</c> (<c>refused=none</c> when nothing was left out).</summary>
    /// <param name="list">The list.</param>
    /// <param name="nextLevelXp">The experience to the next level.</param>
    /// <param name="catalog">The number of quests in the catalog.</param>
    public static string SummaryLine(JobList list, long nextLevelXp, int catalog)
    {
        ArgumentNullException.ThrowIfNull(list);
        var quests = list.Jobs.Count(e => e.Task.Kind == AutopilotTaskKind.Quest);
        var refused = list.Refused.Count == 0 ? "none" : string.Join(",", list.Refused.Select(e => $"{e.Task.QuestKey}:{e.Refusal}"));
        return string.Create(CultureInfo.InvariantCulture, $"autopilot menu: level={list.Level} next_xp={nextLevelXp} listed={quests}/{catalog} refused={refused}");
    }

    /// <summary>
    /// M6 6 FXM2 (6-M9): the one rule for a quest's refusal: the menu lists or refuses with it, and <see cref="AutopilotHostLogic.UseTask"/> (the chat, the option) judges with the same names. <c>not-due:&lt;UTC&gt;</c> is a refusal of the menu only: a task
    /// that is not due stops <c>task-done:quest:&lt;key&gt;:not-due</c> (the scenario's, ok).
    /// </summary>
    /// <param name="input">The menu's input.</param>
    /// <param name="quest">The quest.</param>
    internal static (string? Refusal, long? Xp) Judge(JobMenuInput input, Quest quest)
    {
        long? xp = quest.Experience?.At(input.NextLevelXp);
        if (input.LadderQuestKeys.Contains(quest.Key))
        {
            return ("ladder-quest", xp);
        }

        if (quest.Level is not { } level)
        {
            return ("no-level", xp);
        }

        if (!level.Fits(input.Level))
        {
            return ("level-out-of-band", xp);
        }

        if (input.Timers.NextDue(quest) is not { } due)
        {
            return ("done-once", xp);
        }

        if (input.Now < due)
        {
            return ("not-due:" + due.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), xp);
        }

        return (null, xp);
    }
}
