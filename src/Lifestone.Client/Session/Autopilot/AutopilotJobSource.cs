using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 6 MD2 (L1, map-menu.md 6.1 and 6.4): the job menu's source, owned by the play host. It builds <see cref="JobMenu.Build"/> from the install job, the character's level, the experience to the next level (the dats'
/// XpTable), the quest catalog, the character's quest timers (the live run's, else a read-only load of the character's timer file: the host's delegate decides), now, and the ladder's quest-rung keys. <see cref="Current"/> is the dial's
/// task (<see cref="AutopilotHostLogic.Task"/>). The host calls <see cref="Refresh"/> (a few times a second at most, and after a choice); it raises <see cref="Changed"/> when the level, the task or a timer moved the rows, and logs the
/// <c>autopilot menu: level=... listed=...</c> summary and one line a row once per change of the rows.
/// </summary>
public sealed class AutopilotJobSource : IAutopilotJobSource
{
    /// <summary>The log category (the autopilot's).</summary>
    public const string LogCategory = AutopilotHostLogic.LogCategory;

    /// <summary>The TODO named when the experience to the next level is unknown (no XpTable, or the level at the table's cap): the proportional rows show <c>xp ?</c>.</summary>
    public const string XpUnknownTodo = "m6-menu-xp-table";

    private readonly AutopilotJob _job;
    private readonly Func<AutopilotTask> _current;
    private readonly Func<int> _level;
    private readonly Func<int, long?> _nextLevelXp;
    private readonly IReadOnlyList<Quest> _quests;
    private readonly Func<QuestTimers> _timers;
    private readonly Func<DateTimeOffset> _now;
    private readonly IReadOnlyCollection<string> _ladderKeys;
    private readonly ILogSink _log;
    private JobList? _list;
    private string? _summary;
    private AutopilotTask? _task;
    private bool _xpTodoLogged;

    /// <summary>A source over the given reads.</summary>
    /// <param name="job">The install job.</param>
    /// <param name="current">The dial's task now.</param>
    /// <param name="level">The character's level now (0 when the state has none).</param>
    /// <param name="nextLevelXp">The experience from a level to the next (<see cref="NextLevelXp"/>), or null when unknown.</param>
    /// <param name="quests">The quest catalog.</param>
    /// <param name="timers">The character's quest timers now.</param>
    /// <param name="now">Now (UTC: the session clock).</param>
    /// <param name="ladderQuestKeys">The ladder's quest-rung keys (<see cref="LadderQuestKeys"/>).</param>
    /// <param name="log">The run's log.</param>
    public AutopilotJobSource(AutopilotJob job, Func<AutopilotTask> current, Func<int> level, Func<int, long?> nextLevelXp, IReadOnlyList<Quest> quests, Func<QuestTimers> timers, Func<DateTimeOffset> now, IReadOnlyCollection<string> ladderQuestKeys, ILogSink log)
    {
        _job = job;
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _level = level ?? throw new ArgumentNullException(nameof(level));
        _nextLevelXp = nextLevelXp ?? throw new ArgumentNullException(nameof(nextLevelXp));
        _quests = quests ?? throw new ArgumentNullException(nameof(quests));
        _timers = timers ?? throw new ArgumentNullException(nameof(timers));
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _ladderKeys = ladderQuestKeys ?? throw new ArgumentNullException(nameof(ladderQuestKeys));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// The play host's source over <paramref name="logic"/> (its task) and the session (the level, the XpTable, the clock): the catalog is <see cref="QuestCatalog.All"/> and the ladder <see cref="LadderLoader.WarMage"/>. Without a
    /// session the level is 0 and now is <paramref name="time"/>'s (the host's).
    /// </summary>
    /// <param name="logic">The dial.</param>
    /// <param name="job">The install job.</param>
    /// <param name="session">The session now, or null.</param>
    /// <param name="timers">The character's quest timers now.</param>
    /// <param name="time">The host's time, read only while there is no session (a session's clock is its own TimeProvider).</param>
    /// <param name="log">The run's log.</param>
    public static AutopilotJobSource For(AutopilotHostLogic logic, AutopilotJob job, Func<IGameSessionView?> session, Func<QuestTimers> timers, TimeProvider time, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(logic);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(time);
        return new AutopilotJobSource(
            job,
            () => logic.Task,
            () => session() is { } s ? AutopilotHostLogic.LevelOf(s) : 0,
            level => NextLevelXp(session()?.XpTable?.CharacterLevelXpList, level),
            QuestCatalog.All,
            timers,
            () => session()?.Clock.Time.GetUtcNow() ?? time.GetUtcNow(),
            LadderQuestKeys(LadderLoader.WarMage),
            log);
    }

    /// <summary>The experience from <paramref name="level"/> to the next by the XpTable's character level list (cumulative: the next entry less this one), or null without a table, below level 1 or at the table's cap.</summary>
    /// <param name="levelXp">The XpTable's <c>CharacterLevelXpList</c>, or null.</param>
    /// <param name="level">The level.</param>
    public static long? NextLevelXp(IReadOnlyList<ulong>? levelXp, int level)
    {
        if (levelXp is null || level < 1 || level + 1 >= levelXp.Count || levelXp[level + 1] < levelXp[level])
        {
            return null;
        }

        return (long)(levelXp[level + 1] - levelXp[level]);
    }

    /// <summary>The keys of <paramref name="ladder"/>'s quest rungs (the ladder runs those; the menu lists them refused, <c>ladder-quest</c>).</summary>
    /// <param name="ladder">The ladder.</param>
    public static IReadOnlyCollection<string> LadderQuestKeys(Ladder ladder)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return ladder.Rungs.Where(r => r.Kind == LadderRungKind.Quest).SelectMany(r => r.QuestKeys).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The rows now: every read rebuilds them against the clock (<see cref="Refresh"/>), so the menu's once-a-minute rebuild shows a quest coming due and the inspector's count is current. A read stays cheap (one pass over the
    /// catalog, a few dozen quests, and the timers' lookups; no I/O: the timers delegate returns the timers in memory) and logs or raises <see cref="Changed"/> only when the rows or the task moved.
    /// </summary>
    public JobList Jobs
    {
        get
        {
            Refresh();
            return _list!;
        }
    }

    /// <inheritdoc/>
    public AutopilotTask Current => _current();

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>How many times the rows changed (the summary line was logged).</summary>
    public int Changes { get; private set; }

    /// <summary>The last summary line logged, or null before the first build.</summary>
    public string? Summary => _summary;

    /// <summary>
    /// M6 6 FXU (6-M9): the menu's input now (the install job, the level, the experience to the next level, the catalog, the timers, now, the ladder's quest keys): what <see cref="Refresh"/> builds the rows from and what the
    /// dial's own judgement of a quest task reads (<see cref="AutopilotHostLogic.MenuInput"/>, the play host's), so the menu, the chat and the option refuse by one input. Null while the level is unread (0: the state has no
    /// qualities yet). An unknown next level reads 0 (a proportional award prices nothing; the rows then say <c>xp ?</c>).
    /// </summary>
    public JobMenuInput? BuildInput()
    {
        var level = _level();
        return level < 1 ? null : Input(level, _nextLevelXp(level));
    }

    private JobMenuInput Input(int level, long? next) => new(_job, level, next ?? 0, _quests, _timers(), _now(), _ladderKeys);

    /// <summary>
    /// Rebuilds the rows from the reads now: a change of the rows (the level, the experience, a timer that came due or was set, the catalog) logs the summary and the rows' lines; a change of the rows or of the task raises
    /// <see cref="Changed"/>. Returns true when it raised it.
    /// </summary>
    public bool Refresh()
    {
        var level = _level();
        var next = _nextLevelXp(level);
        if (next is null && level >= 1 && !_xpTodoLogged) // level 0: the state has no level yet (before the qualities); nothing to say
        {
            _xpTodoLogged = true;
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot menu: the experience to level {level + 1} is unknown (no XpTable, or the table's cap), so a proportional quest's row reads xp ? (TODO({XpUnknownTodo}))"));
        }

        var list = JobMenu.Build(Input(level, next));
        if (next is null)
        {
            list = WithoutProportionalXp(list);
        }

        var summary = JobMenu.SummaryLine(list, next ?? 0, _quests.Count);
        var task = _current();
        var rowsMoved = !string.Equals(summary, _summary, StringComparison.Ordinal);
        var taskMoved = _task != task;
        _list = rowsMoved || _list is null ? list : _list;
        _task = task;
        if (rowsMoved)
        {
            _summary = summary;
            Changes++;
            _log.Info(LogCategory, summary);
            foreach (var entry in list.Jobs)
            {
                _log.Info(LogCategory, entry.Line);
            }
        }

        if (!rowsMoved && !taskMoved)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    // An unknown next level: a proportional award cannot be priced, so its row reads xp ? (a fixed award is its amount at any level).
    private JobList WithoutProportionalXp(JobList list)
    {
        bool Fixed(JobEntry e) => e.Task.QuestKey is { } key && _quests.FirstOrDefault(q => q.Key == key)?.Experience is { Kind: QuestExperienceKind.Fixed };
        JobEntry Strip(JobEntry e) => e.Experience is null || Fixed(e) ? e : e with { Experience = null };
        return list with { Jobs = list.Jobs.Select(Strip).ToList(), Refused = list.Refused.Select(Strip).ToList() };
    }
}
