using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 6 MB (map-menu.md 6.2): a one-shot task inside the install job (<see cref="AutopilotOptions.Task"/>, chosen by the menu, <c>/auto &lt;task&gt;</c> or <c>--autopilot-task</c>). The known state hands a task that is not the job's own
/// loop to <see cref="TaskArrow"/> after the audit (and after a cut death's station), where the job would go to buff or fight; the job's ladder check is the job's, never a task's.
/// <list type="bullet">
/// <item><b>A quest</b> (<c>quest:&lt;key&gt;</c>): check before, the character's timer (<see cref="QuestTimers.IsDue"/>): not due, the stop <c>task-done:quest:&lt;key&gt;:not-due</c> (ok); due, the quest station <c>from=home</c> in the
/// Trip state (<see cref="QuestSpec"/>, <see cref="QuestThen.Task"/>). Ended well: the recall home before the stop (<see cref="FinalAttempt"/>), stop <c>task-done:quest:&lt;key&gt;</c> (ok). Failed: today's
/// <c>failed:quest:&lt;key&gt;:&lt;fail&gt;</c> through the station failure's one recovery, then that stop. Cut (a death, a reconnect, a watchdog): the known state again, whose due check makes the one retry safe (a turn-in the cut hid is
/// on the timer, so it is not made twice).</item>
/// <item><b>An errand</b>: one forced logistics trip (<see cref="TripSpec"/>), then the recall home and stop <c>task-done:errand</c> (ok); a trip that fails stops <c>failed:errand:&lt;why&gt;</c>.</item>
/// </list>
/// The D58 trophy errand is the job's: a task never calls it (<c>_questTripTried</c> is held), so a task's way home goes straight to the recall.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The stop of a quest task whose turn-in was made (ok).</summary>
    public const string TaskDoneQuestPrefix = "task-done:quest:";

    /// <summary>The stop of an errand task whose trip was made (ok).</summary>
    public const string TaskDoneErrandStop = "task-done:errand";

    /// <summary>The prefix of every task's ok end (the unattended end reads it: exit code 0).</summary>
    public const string TaskDonePrefix = "task-done:";

    /// <summary>The not-due suffix of a quest task's stop (<c>task-done:quest:&lt;key&gt;:not-due</c>).</summary>
    public const string NotDueSuffix = ":not-due";

    /// <summary>
    /// How many times a task's station is made again after a cut (Lifestone's own, never a loop): one. A second cut stops by name (<c>failed:quest:&lt;key&gt;:cut-again</c>, <c>failed:errand:cut-again</c>).
    /// </summary>
    public const int TaskRunsAgain = 1;

    /// <summary>
    /// M6 6 FXM1 (review 6-M1): the longest a task's fight-back runs before the task begins, in seconds (Lifestone's own tuning, never a world fact: about three kills at a den's pace, so a busy ground whose fight keeps drawing attacks
    /// still lets the task start). Logged when it is reached; the task's stations then fight back with their own legs.
    /// </summary>
    public const double TaskFightBackMaxSeconds = 45;

    /// <summary>
    /// M6 6 FXM1 (review 6-M1): the lull of a task's fight-back's "defend only" end, in seconds (Lifestone's own tuning: half of <see cref="HostileWatch.AttackQuietSeconds"/>): once the attacker recorded at its start is dead, gone or out
    /// of reach and no attack was heard for this long, the fight stops at this lull instead of picking the next target.
    /// </summary>
    public const double TaskFightBackLullSeconds = HostileWatch.AttackQuietSeconds / 2;

    /// <summary>
    /// M6 6 F2: the fight stop reason of a task's fight-back first (D88); the known state then begins the task. M6 6 FXM1 (6-M1): asked once the attacker recorded at the start is dead, gone or out of reach with a lull of
    /// <see cref="TaskFightBackLullSeconds"/>, once no attack was heard for <see cref="HostileWatch.AttackQuietSeconds"/>, or at the cap <see cref="TaskFightBackMaxSeconds"/>.
    /// </summary>
    public static FightStopReason TaskFightBackDone { get; } = new("task-fight-back");

    private bool _taskFightBack;
    private bool _taskFightBackSpent; // FXM1: one fight-back before the task begins, and one more after each cut that runs the station again (never a loop)
    private double _taskFightBackAt;
    private uint _taskFightBackGuid; // FXM1: the creature on it at the fight-back's start (0: none in reach, an attack heard from out of reach)
    private string _taskFightBackWho = string.Empty;
    private bool _errandCarried; // FXM1 (6-M3): the carried cut trip's run is the errand's first, not a run again
    private bool _taskBegun;
    private bool _taskDone;
    private int _taskAgain;
    private int _taskTripsAt;

    /// <summary>True once the task's own station was begun this scenario (a quest task's quest station, an errand's trip).</summary>
    public bool TaskBegun => _taskBegun;

    /// <summary>True once the task's work is done (the stop that follows is <c>task-done:*</c>).</summary>
    public bool TaskDone => _taskDone;

    /// <summary>True when <paramref name="stop"/> is a task's ok end (<c>task-done:*</c>).</summary>
    /// <param name="stop">The stop name.</param>
    public static bool IsTaskDone(string? stop) => stop is not null && stop.StartsWith(TaskDonePrefix, StringComparison.Ordinal);

    /// <summary>
    /// The known state's arrow for a task that is not the job's loop. Null: the place's own arrow decides (a dead body takes the death arrow; away from home and the ground the recall home, after which the audit runs again from the top).
    /// M6 6 FXM1 (6-M5): a task decides only at home or on the ground, so the audit's place check (and after it the bind, the tie, the kit, the caster and the buffs) has finished before any task begins.
    /// </summary>
    private ScenarioStatus? TaskArrow(IGameSessionView session)
    {
        var place = Classify(session.State, _ground);
        if (place == AutopilotPlace.Dead)
        {
            return null; // the death arrow first; the task is decided at the next known state
        }

        _questTripTried = true; // the D58 trophy errand is the job's: never called on a task's way home (the elsewhere arm's recall included)
        if (place is not (AutopilotPlace.Home or AutopilotPlace.Ground))
        {
            _log.Info(LogCategory, $"autopilot task: {_options.Task.Name} waits: it is {PlaceName(place)} ({Cell(session)}): the known state's own arrow brings it home first, and the audit finishes before the task decides (6-M5)");
            return null;
        }

        var finished = _finished;
        var fightBack = TaskFightBack(session); // M6 6 F2 (MB's open point 1, M5 D88): an attacker on it on the ground is fought back before the task begins
        var decided = fightBack ?? _options.Task.Kind switch
        {
            AutopilotTaskKind.Quest => QuestTaskArrow(session, _options.Task.QuestKey ?? string.Empty, place),
            AutopilotTaskKind.Errand => ErrandTaskArrow(place, finished),
            AutopilotTaskKind.DenRun => DenRunArrow(session, place), // M6 item 14: the den run's order (AutopilotScenario.DenRun.cs)
            _ => null,
        };
        if (decided is not null && ReferenceEquals(finished, _finished))
        {
            _finished = null; // the audit that finished this frame gets its chat summary here (a null answer leaves it to the place's arrow)
            Announce(finished, (_taskFightBack ? "fighting back first, then " : string.Empty) + "the task " + _options.Task.Name);
        }

        return decided;
    }

    private static string PlaceName(AutopilotPlace place) => place.ToString().ToLowerInvariant();

    private ScenarioStatus? QuestTaskArrow(IGameSessionView session, string key, AutopilotPlace place)
    {
        var name = _options.Task.Name;
        if (_questTripActive && _questThen == QuestThen.Task)
        {
            // cut (a death, a reconnect, a watchdog's recovery): the station's run was dropped; the timer decides whether it is made again
            _questTripActive = false;
            _tripPending = false;
            _taskFightBackSpent = false; // FXM1: the run again may be preceded by one fight-back of its own
            _taskAgain++;
            if (_taskAgain > TaskRunsAgain)
            {
                return TaskStop("failed:quest:" + key + ":cut-again", ok: false, string.Create(CultureInfo.InvariantCulture, $"the quest task's station was cut {_taskAgain} times (it is made again {TaskRunsAgain} time(s) at most)"));
            }

            _log.Warn(LogCategory, $"autopilot task: {name} was cut: the known state's due check decides whether it runs again (a turn-in made before the cut is on the timer and is not made twice)");
        }

        if (_taskDone)
        {
            return TaskStop(TaskDoneQuestPrefix + key, ok: true, "the quest task's turn-in was made (the recall home before the stop was cut and is asked again)");
        }

        if (_questFailed.TryGetValue(key, out var failed))
        {
            return TaskStop(failed, ok: false, "the quest task's station failed and its one recovery is spent: the run stops by the station's own name");
        }

        if ((TrophyQuests ?? QuestCatalog.All).FirstOrDefault(q => q.Key == key) is not { } quest)
        {
            return TaskStop("failed:quest:" + key + ":not-in-catalog", ok: false, $"the quest task names {key}, which is in no quest file (TODO(m6-task-quest-unknown))");
        }

        // check before: the character's timer (the station does not read it: its caller does)
        var now = session.Clock.Time.GetUtcNow();
        var timers = _context.QuestTimersInUse;
        var line = timers.Line(quest, now);
        if (!timers.IsDue(quest, now))
        {
            _log.Info(LogCategory, $"autopilot task: {name} is not due ({line}): nothing to do");
            return TaskStop(TaskDoneQuestPrefix + key + NotDueSuffix, ok: true, $"the quest is not due ({line})");
        }

        // FXM1 (6-M16): the line names where it begins; the station's own from is home, so on the ground it takes the way home first
        var at = PlaceName(place);
        var way = place == AutopilotPlace.Home ? string.Empty : " (the station's way home first)";
        _log.Info(LogCategory, $"autopilot task: {name} begins at {at} (the station from={JourneyPlans.FromHome}{way}) ({line})");
        _taskBegun = true;
        _questTripActive = true;
        _questThen = QuestThen.Task;
        _questThenForced = false;
        _questKey = key;
        return BeginRun(AutopilotState.Trip, QuestSpec(key, JourneyPlans.FromHome), $"the quest task {key}: the quest station from={JourneyPlans.FromHome}, begun at {at} (M6 6)");
    }

    /// <summary>The quest task's station ended well (<see cref="QuestEnded"/>'s <see cref="QuestThen.Task"/> arm): the recall home before the stop (<see cref="FinalAttempt"/>), stop <c>task-done:quest:&lt;key&gt;</c>.</summary>
    private ScenarioStatus TaskQuestDone(string why)
    {
        _taskDone = true;
        _log.Info(LogCategory, $"autopilot task: {_options.Task.Name} done ({why}): the recall home, then stop={TaskDoneQuestPrefix}{_questKey}");
        return TaskStop(TaskDoneQuestPrefix + _questKey, ok: true, $"the quest task's turn-in was made ({why})");
    }

    /// <summary>
    /// The errand's arrow. M6 6 FXM1 (6-M3, 6-M4): the errand owns its resume: a cut trip (or a carried one) runs again here at home or on the ground, and only here is the run again counted (one known-state pass elsewhere, or an
    /// audit station between passes, counts nothing); on the ground with the audit's hostile on it the task's bounded fight-back comes first (never the job's open fight).
    /// </summary>
    private ScenarioStatus? ErrandTaskArrow(AutopilotPlace place, StartupAudit? finished)
    {
        var trips = _total.Trips + _tally.Trips;
        if (_taskDone)
        {
            return TaskStop(TaskDoneErrandStop, ok: true, "the errand's trip was made (the recall home before the stop was cut and is asked again)");
        }

        if (!_taskBegun)
        {
            _taskBegun = true;
            _taskTripsAt = trips;
            if (_tripPending)
            {
                _errandCarried = true;
                _log.Info(LogCategory, $"autopilot task: errand: a cut trip was carried (a hand-back or a resume): it is the errand's, and it runs again from its start");
                return ErrandResume(place, finished);
            }

            _log.Info(LogCategory, $"autopilot task: errand begins at {PlaceName(place)} (one logistics trip, trip=forced)");
            return BeginRun(AutopilotState.Trip, TripSpec(forced: true), "the errand task: one logistics trip (trip=forced, M6 6)");
        }

        if (trips > _taskTripsAt)
        {
            _taskDone = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: errand done (trips {_taskTripsAt} -> {trips}): the recall home, then stop={TaskDoneErrandStop}"));
            return TaskStop(TaskDoneErrandStop, ok: true, "the errand's trip was made");
        }

        if (_tripPending)
        {
            return ErrandResume(place, finished);
        }

        return TaskStop("failed:errand:given-up", ok: false, "the errand's trip was given up after its retry (no trip was counted)");
    }

    /// <summary>
    /// FXM1 (6-M3, 6-M4): the errand's cut trip runs again from its start (<see cref="ResumeTrip"/>), counted here only (the carried trip's first run is not a run again); on the ground with the audit's hostile on it, the task's
    /// bounded fight-back first (the job's place arm would start the open fight, which no task reason ends).
    /// </summary>
    private ScenarioStatus ErrandResume(AutopilotPlace place, StartupAudit? finished)
    {
        if (place == AutopilotPlace.Ground && finished?.Attacker is { } attacker && !_taskFightBackSpent && _hostiles is { } watch)
        {
            var near = watch.Nearest(out var guid, out var creature, out var metres);
            var who = near ? string.Create(CultureInfo.InvariantCulture, $"{creature} 0x{guid:X8} at {metres:0.#} m") : "none in reach now";
            return BeginTaskFightBack(near ? guid : 0, near ? creature : attacker, $"audit: a hostile is on it ({attacker}; {who}) and the errand's trip is to run again");
        }

        if (_errandCarried)
        {
            _errandCarried = false; // the carried trip's run is the errand's first
        }
        else
        {
            _taskAgain++;
            if (_taskAgain > TaskRunsAgain)
            {
                return TaskStop("failed:errand:cut-again", ok: false, string.Create(CultureInfo.InvariantCulture, $"the errand's trip was cut or failed {_taskAgain} times (it is made again {TaskRunsAgain} time(s) at most)"));
            }

            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: errand: the trip was cut or failed before it ended: it runs again from its start at {PlaceName(place)} (run again {_taskAgain} of {TaskRunsAgain})"));
        }

        _taskFightBackSpent = false; // the run again may be preceded by one fight-back of its own
        return ResumeTrip();
    }

    /// <summary>
    /// M6 6 F2 (MB's open point 1, M5 D88: always fight back an attacker): before a task begins from the known state on the ground, an attack heard within <see cref="HostileWatch.AttackQuietSeconds"/> with a target creature of
    /// the ground in reach gets a fight-back first: the ground's fight from where it stands. M6 6 FXM1 (6-M1): bounded to the attack that started it (<see cref="TaskFightBackOver"/>), once before the task begins and once more after
    /// each cut that runs the station again. Off the ground (home, a route) the task's stations fight back with their own legs. Null: no attacker (or the fight-back is spent), the task goes on.
    /// </summary>
    private ScenarioStatus? TaskFightBack(IGameSessionView session)
    {
        if (_taskFightBackSpent || _hostiles is not { } watch || _clock - watch.LastHitAt >= HostileWatch.AttackQuietSeconds || Classify(session.State, _ground) != AutopilotPlace.Ground || !watch.Nearest(out var guid, out var creature, out var metres))
        {
            return null;
        }

        var heard = string.Create(CultureInfo.InvariantCulture, $"{watch.LastHitBy} attacked {_clock - watch.LastHitAt:0.#} s ago; {creature} 0x{guid:X8} at {metres:0.#} m (reach {watch.ReachMetres:0.#} m)");
        return BeginTaskFightBack(guid, creature, "an attacker is on it (" + heard + ")");
    }

    /// <summary>FXM1 (6-M1): the task's fight-back begins: its attacker (the creature on it), its start and the spent flag recorded, then the ground's fight from where it stands.</summary>
    private ScenarioStatus BeginTaskFightBack(uint guid, string who, string why)
    {
        _taskFightBack = true;
        _taskFightBackSpent = true;
        _taskFightBackAt = _clock;
        _taskFightBackGuid = guid;
        _taskFightBackWho = who;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: {_options.Task.Name} waits: {why}: the fight fights back first (D88), bounded to this attack (its attacker {who} 0x{guid:X8}; the lull {TaskFightBackLullSeconds:0} s, the quiet {HostileWatch.AttackQuietSeconds:0} s, the cap {TaskFightBackMaxSeconds:0} s), then the task begins"));
        return BeginRun(AutopilotState.Fight, FightSpec, $"the task {_options.Task.Name}: {why}: the fight fights back first (D88)");
    }

    /// <summary>
    /// M6 6 F2, FXM1 (6-M1): true while a task's fight-back runs and it is over (the fight then stops by <see cref="TaskFightBackDone"/>), each end logged once with its why: the cap <see cref="TaskFightBackMaxSeconds"/> from its
    /// start; no attack heard for <see cref="HostileWatch.AttackQuietSeconds"/> (counted from the later of the last attack and the start); or "defend only": the attacker recorded at the start is dead, gone or out of reach and no attack
    /// was heard for <see cref="TaskFightBackLullSeconds"/> (the fight stops at this lull instead of picking the next target). The read model holds no creature's target, so "no hostile on it" is read as this lull.
    /// </summary>
    private bool TaskFightBackOver()
    {
        if (!_taskFightBack || _hostiles is not { } watch)
        {
            return false;
        }

        var name = _options.Task.Name;
        var ran = _clock - _taskFightBackAt;
        var quiet = _clock - Math.Max(watch.LastHitAt, _taskFightBackAt);
        var last = double.IsFinite(watch.LastHitAt) ? string.Create(CultureInfo.InvariantCulture, $"the last attack by {watch.LastHitBy} {_clock - watch.LastHitAt:0.#} s ago") : "none heard at all";
        if (ran >= TaskFightBackMaxSeconds)
        {
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: {name}: the fight-back reached its cap of {TaskFightBackMaxSeconds:0} s (TaskFightBackMaxSeconds; {last}): it stops and the task begins (the task's stations fight back with their own legs)"));
            return true;
        }

        if (quiet >= HostileWatch.AttackQuietSeconds)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: {name}: the fight-back is over after {ran:0.#} s: no attack heard for {quiet:0.#} s (the quiet time {HostileWatch.AttackQuietSeconds:0} s; {last})"));
            return true;
        }

        if (quiet >= TaskFightBackLullSeconds && AttackerGone(watch) is { } gone)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"autopilot task: {name}: the fight-back is over after {ran:0.#} s (defend only): its attacker {_taskFightBackWho} 0x{_taskFightBackGuid:X8} {gone} and no attack for {quiet:0.#} s (the lull {TaskFightBackLullSeconds:0} s): the fight stops at this lull"));
            return true;
        }

        return false;
    }

    /// <summary>FXM1: how the fight-back's recorded attacker is no longer on it (dead, gone from view, out of reach), or null while it stands in reach (or its place is unread).</summary>
    private string? AttackerGone(HostileWatch watch)
    {
        if (_taskFightBackGuid == 0)
        {
            return "was not in reach at the start";
        }

        if (_session is not { } session || !session.State.Objects.TryGet(_taskFightBackGuid, out var o))
        {
            return "is gone from view";
        }

        if (o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord })
        {
            return "is dead";
        }

        if (o.Position is { } p && session.State.Self.Position is { } at)
        {
            var metres = p.ToGlobal().DistanceTo(at.ToGlobal());
            return metres > watch.ReachMetres ? string.Create(CultureInfo.InvariantCulture, $"is out of reach ({metres:0.#} m, reach {watch.ReachMetres:0.#} m)") : null;
        }

        return null;
    }

    /// <summary>A task's stop: the D58 trophy errand held off (a task's way home is the recall alone), then <see cref="Stop"/> (which recalls home first when the mage is away).</summary>
    private ScenarioStatus TaskStop(string stop, bool ok, string why)
    {
        _questTripTried = true;
        return Stop(stop, ok, why);
    }

    /// <summary>The errand task's trip stops by the task's name: <c>failed:trip:&lt;why&gt;</c> becomes <c>failed:errand:&lt;why&gt;</c> while the errand's trip is the one in hand (logged).</summary>
    private string TaskStopName(string stop)
    {
        if (_options.Task.Kind != AutopilotTaskKind.Errand || !_taskBegun || _taskDone || !stop.StartsWith("failed:trip:", StringComparison.Ordinal))
        {
            return stop;
        }

        var named = "failed:errand:" + stop["failed:trip:".Length..];
        _log.Info(LogCategory, $"autopilot task: errand: the trip's stop {stop} is the task's {named}");
        return named;
    }
}
