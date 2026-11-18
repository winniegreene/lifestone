using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// M6 14 guard task (the owner, 2026-11-13): a selected kill task in the level up job (<see cref="AutopilotOptions.KillTasks"/>, <see cref="LadderKillTask"/>). The hand-over and back:
/// <list type="number">
/// <item>The arm. While the task is unread and its quest's timer allows, the talk is an errand from home (<see cref="KillTaskErrand"/>: before she leaves home, and at a way home after the trophies): the quest station runs the go to the giver and the
/// talk (<see cref="LadderKillTask.ArmSteps"/>). His reply is the check: the start (the counter armed at 0), his count (armed at N), a turn-in (it was complete), or his wait (on its timer: recorded, an upper bound).</item>
/// <item>The hunt. While the counter is armed and under its count, the rung choice takes the kill step's ground in place of her rung's (<see cref="RungOwner"/>, <c>kill-task:&lt;key&gt;</c>); the run on that ground is the ladder's own loop with
/// the same fight, and every counter line the server sends is read (<see cref="KillTaskWatch"/>). Supply trips, the buff bot's trips and deaths are the loop's own: a trip goes home and back to the task's ground (its gate read at each way
/// out), a death's recovery comes back to it, and a gate read shut at a way out holds the task until the next stop at the bot (<see cref="LadderKillTask.GateHeld"/>).</item>
/// <item>The turn-in. When the counter reads complete, the fight ends in the next lull (<see cref="KillTaskDone"/>), the cycle ends, and the turn-in starts from there (<see cref="LadderKillTask.TurnInSteps"/>): home by the rung change, the
/// Town Network to the giver, his talk; the experience line and his done reply are the check, and the station records the quest's timer.</item>
/// <item>The way back. After each talk the rung is read again (<c>_ladderCycle</c>): an armed task moves her to its ground (<c>rung-moved</c>), a task turned in or on its timer leaves her rung's own ground to the ladder.</item>
/// </list>
/// Every move is one line beginning <see cref="LadderKillTask.LinePrefix"/>.
/// </summary>
public sealed partial class AutopilotScenario
{
    /// <summary>The fight's stop when a kill task's counter reads complete on its ground, in a lull (<see cref="LadderKillTaskDone"/>): the cycle ends and the turn-in starts from where she stands.</summary>
    public static FightStopReason KillTaskDone { get; } = new("kill-task-done");

    /// <summary>The errands a kill task may start in one run (Lifestone's own): a talk that was cut is tried once more, never a loop.</summary>
    public const int KillTaskTriesARun = 2;

    private long _killTaskChatMark = -1;
    private readonly Dictionary<string, int> _killTaskTries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _killTaskNoted = new(StringComparer.Ordinal);

    /// <summary>True when the run carries a kill task to run: the level up job's own loop with a selection that names one.</summary>
    private bool KillTasksOn => _options.Job == AutopilotJob.LevelUp && _options.Task.Kind == AutopilotTaskKind.Job && _options.KillTasks.Count > 0;

    /// <summary>The kill task that steers the rung choice now (its counter armed, no hold), or null.</summary>
    private LadderKillTask? SteeringKillTask() => KillTasksOn ? _options.KillTasks.FirstOrDefault(t => t.Steers) : null;

    /// <summary>
    /// Each tick, in any state: the chat lines since the last read go to the kill tasks (<see cref="LadderKillTask.Read"/>: the counter lines and the giver's talk); a task the giver says is on its timer, which the client's timers did not know, is
    /// recorded now (<see cref="QuestTimers.HowServerStamp"/>: an upper bound, so the next due time is never early).
    /// </summary>
    private void KillTaskWatch(IGameSessionView session)
    {
        if (!KillTasksOn)
        {
            return;
        }

        var chat = session.State.Chat;
        if (_killTaskChatMark < 0)
        {
            _killTaskChatMark = chat.Total;
            foreach (var task in _options.KillTasks)
            {
                _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: the run reads the server's lines of the task from here (the counter after each kill, the giver's talk); arm steps {task.ArmSteps}, turn-in steps {task.TurnInSteps}");
            }

            return;
        }

        foreach (var line in QuestReads.Since(chat, _killTaskChatMark))
        {
            foreach (var task in _options.KillTasks)
            {
                if (task.Read(line, _log))
                {
                    KillTaskMoved(session, task);
                    break;
                }
            }
        }

        _killTaskChatMark = chat.Total;
    }

    private void KillTaskMoved(IGameSessionView session, LadderKillTask task)
    {
        if (task.Phase != KillTaskPhase.OnTimer)
        {
            return;
        }

        var now = session.Clock.Time.GetUtcNow();
        if (!_context.QuestTimersInUse.IsDue(task.Quest, now))
        {
            return;
        }

        var entry = _context.CompleteQuest(task.Quest, now, QuestTimers.HowServerStamp);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LadderKillTask.LinePrefix}{task.State}: the giver says the task is on its timer, which the client's timers did not know: recorded as seen now ({QuestTimers.HowServerStamp}, an upper bound: never early): next due {entry.NextDue:yyyy-MM-ddTHH:mm:ssZ}"));
    }

    /// <summary>
    /// M6 14 guard task, the rung choice's read of the task (<see cref="AskRung"/>): each refusal is named once (its ground, the rule); a gate read shut at the way out to its ground holds the task until the next stop at the buff bot
    /// (<see cref="LadderKillTask.GateHeld"/>), so the run stays on her rung's ground instead of swinging back at every check.
    /// </summary>
    private void KillTaskNoted(RungChoice choice, int level, string path)
    {
        if (choice.KillTask is not { } read || _session is not { } session)
        {
            return;
        }

        var task = _options.KillTasks.First(t => t.Key == read.Key);
        if (read.Refused is { } refused)
        {
            if (_killTaskNoted.Add(read.Key + ":" + refused))
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LadderKillTask.LinePrefix}{task.State}: refused at level {level} ({path}): {refused}; her rung's ground stands"));
            }

            if (read.Gate is { Open: false })
            {
                task.GateHeld(refused, session.Clock.Time.GetUtcNow(), _log);
            }

            return;
        }

        if (_killTaskNoted.Add(read.Key + ":steers:" + _ground.Key))
        {
            _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: steers ({path}): {choice.Decision.Why}");
        }
    }

    /// <summary>
    /// True when a kill task's counter reads complete while she fights on its ground and no hostile is in reach (the upkeep's lull, as the move-on's): the fight ends with <see cref="KillTaskDone"/>. The wait for the lull is logged once.
    /// </summary>
    private bool LadderKillTaskDone(IGameSessionView session)
    {
        if (!KillTasksOn || _options.KillTasks.FirstOrDefault(t => t.Phase == KillTaskPhase.Complete && string.Equals(t.Ground, _ground.Key, StringComparison.Ordinal)) is not { } task)
        {
            return false;
        }

        if (_hostiles is { } watch && watch.InReach(_clock, out var what))
        {
            if (_killTaskNoted.Add(task.Key + ":lull"))
            {
                _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: the counter reads complete: the fight ends in the next lull ({what})");
            }

            return false;
        }

        _killTaskNoted.Remove(task.Key + ":lull");
        _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: the counter reads complete and no hostile is in reach: the fight ends; the turn-in follows");
        return true;
    }

    /// <summary>
    /// M6 14 guard task: the kill task's errand, when one is owed (<see cref="LadderKillTask.ErrandSteps"/>: the arm while unread and due, the turn-in when complete), as a ladder errand from home (<see cref="StartErrand"/>), the quest station
    /// over the range of the file's steps. Not when the station already runs, the task failed this run (<c>_questFailed</c>), it was tried <see cref="KillTaskTriesARun"/> times this run, or her level is out of the quest's band. Null: none
    /// runs, and the caller goes on.
    /// </summary>
    /// <param name="then">What the run does after it.</param>
    /// <param name="why">Who asks, for the log.</param>
    private ScenarioStatus? KillTaskErrand(QuestThen then, string why)
    {
        if (!KillTasksOn || _questTripActive || _session is not { } session)
        {
            return null;
        }

        var now = session.Clock.Time.GetUtcNow();
        foreach (var task in _options.KillTasks)
        {
            if (task.ErrandSteps(_context.QuestTimersInUse, now) is not { } steps)
            {
                continue;
            }

            if (_questFailed.TryGetValue(task.Key, out var failed))
            {
                if (_killTaskNoted.Add(task.Key + ":failed"))
                {
                    _log.Warn(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: its errand failed this run ({failed}): not called again until the next run");
                }

                continue;
            }

            var tries = _killTaskTries.GetValueOrDefault(task.Key);
            if (tries >= KillTaskTriesARun)
            {
                if (_killTaskNoted.Add(task.Key + ":tries"))
                {
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"{LadderKillTask.LinePrefix}{task.State}: its errand was started {tries} times this run without a read: not called again until the next run"));
                }

                continue;
            }

            if (task.Quest.Level is { } band && !band.Fits(session.State.Self.Qualities.Level))
            {
                if (_killTaskNoted.Add(task.Key + ":band"))
                {
                    _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: level {session.State.Self.Qualities.Level} is out of the quest's band {band}: no errand");
                }

                continue;
            }

            if (then == QuestThen.Trip && ErrandWaitsForSale(task.Key))
            {
                return null; // the sale first, as a ladder errand's (m6-errand-after-sale): the trip's end asks again from where it ends
            }

            var what = steps == task.ArmSteps
                ? "the arm (his reply reads the counter: the start at 0, his count, a turn-in if it was complete, or his wait)"
                : "the turn-in (the counter reads complete)";
            _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: errand: {what}: the quest station steps={steps} from={JourneyPlans.FromHome} ({why})");
            var started = StartErrand(then, forced: false, task.Key, JourneyPlans.FromHome, ladder: true, $"{why}: the kill task {task.Key}: {what}", steps);
            if (started is not null)
            {
                _killTaskTries[task.Key] = tries + 1;
            }

            return started;
        }

        return null;
    }

    /// <summary>
    /// The quest station of a kill task's errand ended (<see cref="QuestEnded"/>): check after, by what the server said of the counter (<see cref="LadderKillTask.Phase"/>), not by the station's range alone. The talk is not the way's one errand
    /// (a held trophy may still be turned in on this way) and the rung is read again at the next known state. Armed, turned in or on its timer: the run goes on with what it was doing. Read nothing: a failed errand by name
    /// (<c>failed:quest:&lt;key&gt;:kill-task-unread</c>). A station that failed otherwise is the ladder errand's failure (null: the caller's path).
    /// </summary>
    private ScenarioStatus? KillTaskEnded(string key, bool ok, bool done, string? why, string fields)
    {
        if (!KillTasksOn || _options.KillTasks.FirstOrDefault(t => t.Key == key) is not { } task)
        {
            return null;
        }

        _questTripTried = false;
        _ladderCycle = -1;
        _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: check after the errand: ok={(ok ? "yes" : "no")} done={(done ? "yes" : "no")} xp={Token(fields, "xp") ?? "?"} reward_line={Token(fields, "reward_line") ?? "?"} timer={Token(fields, "timer") ?? "?"}{(why is null ? string.Empty : " why=" + JourneyVerdict.Token(why))}");
        switch (task.Phase)
        {
            case KillTaskPhase.Armed when ok:
                _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: armed: the next rung read takes {task.Ground} in place of her rung's ground");
                return QuestThenStep("after the kill task's arm " + key);
            case KillTaskPhase.TurnedIn when ok && done:
                _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: turned in (the experience the station read: {Token(fields, "reward_line") ?? "?"}): the quest's timer runs, and her rung's own ground follows");
                return QuestThenStep("after the kill task's turn-in " + key);
            case KillTaskPhase.OnTimer:
                _log.Info(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: the giver says the task is on its timer: no failure; her rung's own ground follows");
                return QuestThenStep("after the kill task's talk " + key + " (on its timer)");
            case KillTaskPhase.Unread when ok:
                var stop = "failed:quest:" + key + ":kill-task-unread";
                _questFailed[key] = stop;
                _log.Warn(LogCategory, $"{LadderKillTask.LinePrefix}{task.State}: the talk ended and no line of the giver read the counter (no start, no count, no turn-in, no wait): a failed errand by name ({stop})");
                return LadderErrandFailed(key, stop, why);
            default:
                return null;
        }
    }
}
