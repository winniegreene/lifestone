using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

// M6 6 MC: the job menu's data types, moved here from Session/Autopilot/AutopilotJobs.cs (MA) so the retail UI's menu window and inspector may name them
// (RetailUiArchitectureTests: the retail UI reaches the session only through Session.Contracts). Names and shapes unchanged; JobMenu.Build stays in Session.Autopilot.

/// <summary>M6 6 MC: the job menu's fixed row labels (<c>JobMenu</c>'s constants name these).</summary>
public static class JobLabels
{
    /// <summary>The stop row's label.</summary>
    public const string Stop = "stop";

    /// <summary>The level-up row's label.</summary>
    public const string LevelUp = "level up";

    /// <summary>The den's row label.</summary>
    public const string Hunt = "hunt here";
}

/// <summary>M6 6 MA: what a run does inside the install job (<c>--autopilot-job</c>): its own loop, one quest, or one errand.</summary>
public enum AutopilotTaskKind
{
    /// <summary>The install job's loop: level up (the ladder and the trophy errand), or hunt here (the den).</summary>
    Job,

    /// <summary>One quest of the catalog, done once from home.</summary>
    Quest,

    /// <summary>One forced logistics trip.</summary>
    Errand,

    /// <summary>M6 item 14 (the owner, 2026-11-12): the den run, once: the trip home with its sale, the buff bot, her own pass, the run to a Black Spawn Den's entrance, a minute inside, the tie at the entrance and home (<c>AutopilotScenario.DenRun.cs</c>). By order only (<c>--autopilot-den-run</c>); the menu does not list it.</summary>
    DenRun,
}

/// <summary>M6 6 MA: a task (<c>job</c>, <c>quest:&lt;key&gt;</c> or <c>errand</c>); the menu picks one, <c>--autopilot-task</c> and <c>/auto</c> name one.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="QuestKey">The quest's key for <see cref="AutopilotTaskKind.Quest"/>; M6 14: for <see cref="AutopilotTaskKind.Job"/>, the start quest the level up job runs first (the chat's <c>/auto level-up quest &lt;key&gt;</c>), else null.</param>
public readonly record struct AutopilotTask(AutopilotTaskKind Kind, string? QuestKey = null)
{
    /// <summary>The install job's own loop.</summary>
    public static AutopilotTask Job { get; } = new(AutopilotTaskKind.Job);

    /// <summary>One forced logistics trip.</summary>
    public static AutopilotTask Errand { get; } = new(AutopilotTaskKind.Errand);

    /// <summary>M6 item 14: the den run (<see cref="AutopilotTaskKind.DenRun"/>).</summary>
    public static AutopilotTask DenRun { get; } = new(AutopilotTaskKind.DenRun);

    /// <summary>The name of <see cref="DenRun"/> in options and logs.</summary>
    public const string DenRunName = "den-run";

    /// <summary>One quest.</summary>
    /// <param name="key">The quest's key.</param>
    public static AutopilotTask ForQuest(string key) => new(AutopilotTaskKind.Quest, key);

    /// <summary>The task as options, logs and the menu name it: <c>job</c>, <c>quest:&lt;key&gt;</c>, <c>errand</c>.</summary>
    public string Name => Kind switch
    {
        AutopilotTaskKind.Quest => "quest:" + QuestKey,
        AutopilotTaskKind.Errand => "errand",
        AutopilotTaskKind.DenRun => DenRunName,
        _ => QuestKey is { } start ? "job+quest:" + start : "job", // M6 14: the level up job with a start quest (/auto level-up quest <key>)
    };

    /// <summary>Reads a task name. Syntax only (a quest key is not looked up here); anything else is a named error.</summary>
    /// <param name="text">The text.</param>
    /// <param name="task">The task, or <see cref="Job"/> when refused.</param>
    /// <param name="error">The named error, or null.</param>
    public static bool TryParse(string? text, out AutopilotTask task, out string? error)
    {
        task = Job;
        var t = (text ?? string.Empty).Trim();
        switch (t)
        {
            case "job":
                error = null;
                return true;
            case "errand":
                task = Errand;
                error = null;
                return true;
            case DenRunName:
                task = DenRun;
                error = null;
                return true;
        }

        if (t.StartsWith("quest:", StringComparison.Ordinal))
        {
            var key = t["quest:".Length..];
            if (key.Length == 0 || key.Any(c => char.IsWhiteSpace(c) || char.IsUpper(c)))
            {
                error = $"'{t}' names no quest key (quest:<key>, the key is the quest file's name in lower case)";
                return false;
            }

            task = new AutopilotTask(AutopilotTaskKind.Quest, key);
            error = null;
            return true;
        }

        error = $"'{t}' is not a task (job, quest:<key>, errand or {DenRunName})";
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>M6 6 MA: one row of the job menu.</summary>
/// <param name="Task">The task the row starts (the stop row carries <see cref="AutopilotTask.Job"/> and the label <see cref="JobLabels.Stop"/>).</param>
/// <param name="Label">The row's words.</param>
/// <param name="Experience">The experience the row awards, or null when unknown (<c>xp ?</c>).</param>
/// <param name="Refusal">Why the row is not listed (<c>no-level</c>, <c>level-out-of-band</c>, <c>not-due:&lt;UTC&gt;</c>, <c>done-once</c>, <c>ladder-quest</c>), or null.</param>
/// <param name="Why">The row's detail line.</param>
public sealed record JobEntry(AutopilotTask Task, string Label, long? Experience, string? Refusal, string Why)
{
    /// <summary>True when the row may be chosen.</summary>
    public bool Enabled => Refusal is null;

    /// <summary>The row as one log line: <c>autopilot menu: job=&lt;name&gt; xp=&lt;n|?&gt; why=&lt;...&gt;</c>.</summary>
    public string Line => string.Create(
        CultureInfo.InvariantCulture,
        $"autopilot menu: job={(Label == JobLabels.Stop ? JobLabels.Stop : Task.Name)} xp={(Experience is { } xp ? xp.ToString(CultureInfo.InvariantCulture) : "?")} why={Why}");
}

/// <summary>M6 6 MA: the job menu's rows.</summary>
/// <param name="Level">The level the menu was built for.</param>
/// <param name="Jobs">The rows to show, in order.</param>
/// <param name="Refused">The quests left out, each with its named refusal.</param>
public sealed record JobList(int Level, IReadOnlyList<JobEntry> Jobs, IReadOnlyList<JobEntry> Refused);

/// <summary>M6 6 MA: where the menu reads its rows and the current task (the host's; the menu window subscribes).</summary>
public interface IAutopilotJobSource
{
    /// <summary>The rows now.</summary>
    JobList Jobs { get; }

    /// <summary>The task the dial runs or will run.</summary>
    AutopilotTask Current { get; }

    /// <summary>Raised when the rows or the current task changed.</summary>
    event Action? Changed;
}
