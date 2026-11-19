using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>Where a kill task stands, as the server's own lines have told the client (<see cref="LadderKillTask"/>).</summary>
public enum KillTaskPhase
{
    /// <summary>Nothing read this process: the counter is not known. The talk is the check: an errand to the giver when the quest's timer allows.</summary>
    Unread,

    /// <summary>The counter is armed and under the task's count (<see cref="LadderKillTask.Counter"/>): the hunt on the kill step's ground takes precedence over her rung's.</summary>
    Armed,

    /// <summary>The counter reads the task's count (<c>Your task is complete!</c>): the turn-in is due.</summary>
    Complete,

    /// <summary>The giver took the turn-in (his done reply was read): the quest's timer runs; nothing to do until it allows the next round.</summary>
    TurnedIn,

    /// <summary>The giver said the task is on its timer (his wait reply): nothing to do until the timer allows.</summary>
    OnTimer,
}

/// <summary>
/// M6 14 guard task (the owner, 2026-11-13): one kill task the level up job runs (a selected quest of the kill-task shape: a talk that arms the counter, a <c>kill</c> step of N creatures on a ground, a talk that turns it in; the
/// Tusker Guard Kill Task, <c>tusker-guard-kill-task.json</c>). It holds what the server has said of the task, never a guess: every line is read as it arrives (<see cref="Read"/>) and logged, and the phase moves only on the server's
/// own text.
/// <list type="bullet">
/// <item>The kill counter: ACE's <c>You have killed N plural! You must kill M to complete your task.</c> or <c>... Your task is complete!</c> after each counted kill (<see cref="QuestReads.ParseKillCounter"/>; QuestManager.HandleKillTask,
/// @95ef426d <c>Managers/QuestManager.cs:583-626</c>), sent only while the task is armed.</item>
/// <item>The giver's talk: the counter as he reads it (the arm talk's armed reply, <c>You've killed N out of M ...</c>, his DirectBroadcast of %tqc and %tqm, <c>EmoteManager.cs:1820-1845</c>), the start (the arm talk's own reply with no
/// counter line before it: the server armed the counter at 0), the turn-in (the quest's done reply) and the wait (the arm talk's wait reply).</item>
/// </list>
/// One object a process (as <see cref="LadderTrial"/>): the options are parsed once and every run's options are copies, so every run of the climb shares what was read. A new process starts at <see cref="KillTaskPhase.Unread"/>: its first
/// errand's talk reads the counter again from the giver (check before), and the first kill line on the ground confirms it (check after).
/// </summary>
public sealed class LadderKillTask
{
    /// <summary>The prefix of every line.</summary>
    public const string LinePrefix = "ladder: kill task: ";

    private bool _counterLinePending;
    private DateTimeOffset? _gateHeldAt;

    /// <summary>A kill task over <paramref name="quest"/>, whose steps must be the kill-task shape (refused by name otherwise).</summary>
    /// <param name="quest">The quest.</param>
    public LadderKillTask(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        Quest = quest;
        Kill = quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.Kill && s.Ground is not null && s.Count is > 0 && s.Target is not null)
            ?? throw new ArgumentException($"quest {quest.Key} has no kill step with a ground, a count and a target: it is no kill task", nameof(quest));
        ArmTalk = quest.Steps.Take(Kill.Index).LastOrDefault(s => s.Kind == QuestStepKind.Talk)
            ?? throw new ArgumentException($"quest {quest.Key} has no talk before its kill step: nothing arms the counter", nameof(quest));
        TurnInTalk = quest.Steps.Skip(Kill.Index + 1).FirstOrDefault(s => s.Kind == QuestStepKind.Talk)
            ?? throw new ArgumentException($"quest {quest.Key} has no talk after its kill step: nothing turns it in", nameof(quest));
        if (ArmTalk.Expect.Reply is null || quest.Done.Reply is null)
        {
            throw new ArgumentException($"quest {quest.Key}: the arm talk's reply and the quest's done reply are the task's reads; one is missing", nameof(quest));
        }

        var turnInFrom = quest.Steps.Skip(Kill.Index + 1).TakeWhile(s => s.Index < TurnInTalk.Index).FirstOrDefault()?.Index ?? TurnInTalk.Index;
        ArmSteps = string.Create(CultureInfo.InvariantCulture, $"0-{ArmTalk.Index}");
        TurnInSteps = string.Create(CultureInfo.InvariantCulture, $"{turnInFrom}-{quest.Steps.Count - 1}");
    }

    /// <summary>The quest.</summary>
    public Quest Quest { get; }

    /// <summary>The quest's key.</summary>
    public string Key => Quest.Key;

    /// <summary>The kill step (the creature, the count, the ground).</summary>
    public QuestStep Kill { get; }

    /// <summary>The talk that arms the counter (the last talk before the kill step).</summary>
    public QuestStep ArmTalk { get; }

    /// <summary>The talk that turns the task in (the first talk after the kill step).</summary>
    public QuestStep TurnInTalk { get; }

    /// <summary>The ground the kill step hunts.</summary>
    public string Ground => Kill.Ground!;

    /// <summary>The count the task wants.</summary>
    public int Count => Kill.Count!.Value;

    /// <summary>The quest station's range for the arm: the go to the giver and the talk (<c>0-1</c>).</summary>
    public string ArmSteps { get; }

    /// <summary>The quest station's range for the turn-in: the go back to the giver and the talk (<c>3-4</c>); it ends at the file's last step, so the station's done check records the completion.</summary>
    public string TurnInSteps { get; }

    /// <summary>Where the task stands.</summary>
    public KillTaskPhase Phase { get; private set; }

    /// <summary>The counter as last read, or null while none was read (or after the turn-in erased it).</summary>
    public int? Counter { get; private set; }

    /// <summary>Where <see cref="Counter"/> was read: <c>none</c>, <c>talk</c> or <c>kill-line</c>.</summary>
    public string ReadFrom { get; private set; } = "none";

    /// <summary>The kill counter lines read this process.</summary>
    public int KillLines { get; private set; }

    /// <summary>The arms read this process (a start of the counter at 0).</summary>
    public int Arms { get; private set; }

    /// <summary>The turn-ins read this process.</summary>
    public int TurnIns { get; private set; }

    /// <summary>True while the hunt on <see cref="Ground"/> takes precedence over her rung's: the counter is armed and under the count, and no shut gate holds it (<see cref="GateHeld"/>).</summary>
    public bool Steers => Phase == KillTaskPhase.Armed && _gateHeldAt is null;

    /// <summary>The errand the task owes now, as the quest station's range: the arm (<see cref="ArmSteps"/>) while unread and the quest's timer allows, the turn-in (<see cref="TurnInSteps"/>) when the counter is complete, else null.</summary>
    /// <param name="timers">The character's quest timers.</param>
    /// <param name="now">The time (UTC).</param>
    public string? ErrandSteps(QuestTimers timers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(timers);
        return Phase switch
        {
            KillTaskPhase.Unread when timers.IsDue(Quest, now) => ArmSteps,
            KillTaskPhase.TurnedIn or KillTaskPhase.OnTimer when timers.IsDue(Quest, now) && OwnTimerDue(now) => ArmSteps, // M6 audit (fix 3, the owner's decision of 2026-11-19): every round its timer allows, not one a process
            KillTaskPhase.Complete => TurnInSteps,
            _ => null,
        };
    }

    private (DateTimeOffset At, long Xp)? _roundStart;
    private long? _roundKillsXp;
    private DateTimeOffset? _turnedInAt;

    /// <summary>M6 audit (fix 3): true when the quest's own timer has run since this object read the turn-in (<see cref="NoteRound"/>), or it read none: a run that starts with timers not yet read never arms a round early.</summary>
    private bool OwnTimerDue(DateTimeOffset now) => _turnedInAt is not { } at || Quest.TimerSeconds is not { } seconds || now >= at.AddSeconds(seconds);

    /// <summary>
    /// M6 audit (fix 3, the owner's decision of 2026-11-19: the task repeats while it remains a boost to her experience an hour): the last whole round's experience an hour, from its arm to its turn-in: the experience
    /// she earned from the arm to the counter's completion, plus the quest's fixed reward (<see cref="Quest.Experience"/>; the award's own line is not waited for), over the hours from the arm to the turn-in. Null
    /// before a round was measured whole in this process.
    /// </summary>
    public double? LastRoundXpPerHour { get; private set; }

    /// <summary>The last whole round's length (<see cref="LastRoundXpPerHour"/>), or null.</summary>
    public TimeSpan? LastRoundTime { get; private set; }

    /// <summary>M6 audit (fix 3): notes the round's marks after a read that moved the task from <paramref name="before"/>: the start at the arm, the kills' experience at the completion, the rate at the turn-in.</summary>
    /// <param name="before">The phase before the read.</param>
    /// <param name="at">The time (UTC).</param>
    /// <param name="totalXp">Her total experience now.</param>
    public void NoteRound(KillTaskPhase before, DateTimeOffset at, long totalXp)
    {
        if (Phase == KillTaskPhase.Armed && before != KillTaskPhase.Armed && Counter == 0)
        {
            (_roundStart, _roundKillsXp) = ((at, totalXp), null);
        }
        else if (Phase == KillTaskPhase.Complete && before != KillTaskPhase.Complete && _roundStart is { } start)
        {
            _roundKillsXp = Math.Max(0, totalXp - start.Xp);
        }
        else if (Phase == KillTaskPhase.TurnedIn && before != KillTaskPhase.TurnedIn)
        {
            _turnedInAt = at;
        }

        if (Phase == KillTaskPhase.TurnedIn && before != KillTaskPhase.TurnedIn && _roundStart is { } begun && _roundKillsXp is { } kills && at > begun.At)
        {
            var reward = Quest.Experience is { Kind: QuestExperienceKind.Fixed, Amount: { } amount } ? amount : 0;
            LastRoundTime = at - begun.At;
            LastRoundXpPerHour = (kills + reward) / LastRoundTime.Value.TotalHours;
            (_roundStart, _roundKillsXp) = (null, null);
        }
    }

    /// <summary>The task as one log fragment: <c>key=tusker-guard-kill-task phase=armed counter=123/500 read=kill-line ground=tusker-barracks</c>.</summary>
    public string State => string.Create(
        CultureInfo.InvariantCulture,
        $"key={Key} phase={Word(Phase)} counter={(Counter is { } n ? n.ToString(CultureInfo.InvariantCulture) : "?")}/{Count} read={ReadFrom} ground={Ground}{(_gateHeldAt is null ? string.Empty : " gate=held")}");

    /// <summary>The phase's log word.</summary>
    /// <param name="phase">The phase.</param>
    public static string Word(KillTaskPhase phase) => phase switch
    {
        KillTaskPhase.Unread => "unread",
        KillTaskPhase.Armed => "armed",
        KillTaskPhase.Complete => "complete",
        KillTaskPhase.TurnedIn => "turned-in",
        _ => "on-timer",
    };

    /// <summary>
    /// Reads one chat line. Returns true when it is the task's (a counter line of its creature, or a line of its giver's talk); the phase and the counter move by it and one line says so. A counter line that does not follow the last read
    /// by one is logged as a jump (a kill counted while the client read nothing, or a second player's credit), never corrected.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="log">The log.</param>
    public bool Read(ChatLine line, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var text = line.Text;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var c = CultureInfo.InvariantCulture;
        var before = State;
        if (line.Kind == ChatKind.System && QuestReads.ParseKillCounter(text) is { } counter && counter.Plural.StartsWith(Kill.Target!.Name, StringComparison.Ordinal))
        {
            KillLines++;
            var jump = Counter is { } last && Phase == KillTaskPhase.Armed && counter.Killed != last + 1 && counter.Killed != last ? string.Create(c, $" (a jump: the last read was {last})") : string.Empty;
            Counter = counter.Killed;
            ReadFrom = "kill-line";
            Phase = counter.Complete || counter.Killed >= Count ? KillTaskPhase.Complete : KillTaskPhase.Armed;
            Note(log, before, $"the kill line \"{text}\"{jump}", jump.Length > 0);
            return true;
        }

        if (ArmTalk.ArmedReply is { } armed && text.Contains(armed, StringComparison.Ordinal) && LeadingNumber(text, armed) is { } read)
        {
            _counterLinePending = true; // his reply follows this line in the same emote chain: it is no start
            Counter = read;
            ReadFrom = "talk";
            Phase = read >= Count ? KillTaskPhase.Complete : KillTaskPhase.Armed;
            Note(log, before, $"the giver's count \"{text}\"", warn: false);
            return true;
        }

        if (text.Contains(ArmTalk.Expect.Reply!, StringComparison.Ordinal))
        {
            if (_counterLinePending)
            {
                _counterLinePending = false;
                log.Info(AutopilotScenario.LogCategory, $"{LinePrefix}{State}: the giver's reply after his count (the task is armed, not started again): \"{text}\"");
                return true;
            }

            Arms++;
            Counter = 0;
            ReadFrom = "talk";
            Phase = KillTaskPhase.Armed;
            Note(log, before, $"the giver's start \"{text}\": the server armed the counter at 0", warn: false);
            return true;
        }

        if (text.Contains(Quest.Done.Reply!, StringComparison.Ordinal))
        {
            TurnIns++;
            Counter = null;
            ReadFrom = "talk";
            Phase = KillTaskPhase.TurnedIn;
            _counterLinePending = false;
            Note(log, before, $"the giver's turn-in \"{text}\": the counter is erased and the quest's timer runs", warn: false);
            return true;
        }

        if (ArmTalk.WaitReply is { } wait && text.Contains(wait, StringComparison.Ordinal))
        {
            Phase = KillTaskPhase.OnTimer;
            _counterLinePending = false;
            Note(log, before, $"the giver's wait \"{text}\": the task is on its timer", warn: false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The task's gate read shut at the way out to its ground (<see cref="RungOwner"/>'s choice refused it there): the hunt is held, by name, until the next stop at the buff bot puts the gate's lines up again (<see cref="BotVisited"/>), so
    /// the run does not swing between her rung's ground and the task's at every check. True when this read began the hold.
    /// </summary>
    /// <param name="why">The refusal.</param>
    /// <param name="now">The time (UTC).</param>
    /// <param name="log">The log.</param>
    public bool GateHeld(string why, DateTimeOffset now, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (_gateHeldAt is not null)
        {
            return false;
        }

        _gateHeldAt = now;
        log.Warn(AutopilotScenario.LogCategory, $"{LinePrefix}{State}: held: {why}; her rung's ground until the next stop at the buff bot");
        return true;
    }

    /// <summary>The stop at the buff bot ended well: a gate hold ends (logged), and the next rung read asks the gate again.</summary>
    /// <param name="log">The log.</param>
    public void BotVisited(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (_gateHeldAt is null)
        {
            return;
        }

        _gateHeldAt = null;
        log.Info(AutopilotScenario.LogCategory, $"{LinePrefix}{State}: the hold ends: the buff bot's lines are up again, so the next rung read asks the gate again");
    }

    /// <summary>The integer just before <paramref name="tail"/> in <paramref name="text"/> (<c>You've killed 123 out of 500 Tusker Guards.</c> with <c>out of 500 Tusker Guards</c>: 123), or null.</summary>
    private static int? LeadingNumber(string text, string tail)
    {
        var end = text.IndexOf(tail, StringComparison.Ordinal);
        var i = end - 1;
        while (i >= 0 && text[i] == ' ')
        {
            i--;
        }

        var last = i;
        while (i >= 0 && char.IsAsciiDigit(text[i]))
        {
            i--;
        }

        return last > i && int.TryParse(text.AsSpan(i + 1, last - i), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private void Note(ILogSink log, string before, string what, bool warn)
    {
        var line = $"{LinePrefix}{State}: {what} (was {before})";
        if (warn)
        {
            log.Warn(AutopilotScenario.LogCategory, line);
        }
        else
        {
            log.Info(AutopilotScenario.LogCategory, line);
        }
    }
}
