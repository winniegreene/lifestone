using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The quest of a <see cref="RoutePlay"/> that ends at a portal (<see cref="PortalExit.QuestName"/>, <see cref="ExitQuestMode"/>): before the Use the
/// character is made to hold it (<see cref="ExitQuestMode.Stamp"/>) or not hold it (<see cref="ExitQuestMode.Erase"/>) through the server mod's
/// <c>@lifestone quest</c> verb, and after the way home the registry is asked and brought back to what the scenario found.
/// </summary>
/// <remarks>
/// <para><b>Stages:</b> <c>quest-check</c> (<c>has</c>: nothing is changed when the character is already as the mode needs it), <c>quest-change</c>
/// (<c>stamp</c> or <c>erase</c>, confirmed against the reply), then <c>using</c>; and at the end <c>quest-restore</c> (<c>has</c> again, then the one verb
/// that brings the registry to what was found, only when it differs).</para>
/// <para><b>The portal changes the registry itself.</b> "Exit to Holtburg" (weenie 29338 in world database v0.9.296) carries an emote set on its portal
/// category (EmoteCategory 4, emote 86996), which ACE runs after the teleport (<c>Portal.ActOnUse</c> to <c>EmoteManager.OnPortal</c>, Portal.cs:287): it erases
/// ten Academy quests (<c>AcademyTokenGiven</c>, <c>SentryTaskComplete</c>, the pickup quests, the wasp-wing and bellows quests, <c>protectionorb</c>), moves the
/// sanctuary to Holtburg, enables recalls, shows a pop-up and sets the last portal. So a <see cref="ExitQuestMode.Stamp"/> run ends with the quest already gone
/// whether the scenario stamped it or not, and a character that held it loses it: the restore therefore asks first (a verb sent blind would add a completion or
/// do nothing) and stamps the quest again when it was found held. The sanctuary, the recall flag and the other nine quests are not put back: the mod has no verb
/// for them, and graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25).</para>
/// <para>ACE's own calls are behind the verbs (<c>QuestManager.Stamp</c>, <c>Erase</c>): a stamp adds a completion unless the quest's world row is at its
/// maximum, an erase removes the row, and a completion count cannot be put back by either. So <see cref="ExitQuestMode.Erase"/> refuses to touch a quest
/// that is held with any count but 1, before it changes anything, and a restore that would need another count than 1 fails loudly.</para>
/// <para>A failed play (and a disposed one) sends the opposite verb once without waiting for its answer when its own stamp or erase may have taken
/// (<see cref="PutTheQuestBackAfterFailure"/>), never after an answer that said the change did not happen, and never from <c>quest-restore</c>. When the registry
/// may differ and no verb is safe, it only warns. <see cref="QuestVerdict"/> says which of these happened.</para>
/// </remarks>
public sealed partial class RoutePlay
{
    private ToolHandle<QuestResult>? _questAsk;
    private QuestResult? _questFound;
    private QuestResult? _questChange;
    private bool _questRequested;
    private bool _questMayHaveChanged;
    private bool _questPutBack;
    private bool _questFailureCleanup;
    private bool _questCleanupHandled;
    private bool? _questRestoreStamp;

    /// <summary>The reply that told whether the character held the quest before the scenario changed anything, or null before it was asked.</summary>
    public QuestResult? QuestFound => _questFound;

    /// <summary>The reply to the stamp or erase this play made (what the registry looked like before and after), or null when it changed nothing (or has not yet).</summary>
    public QuestResult? QuestChange => _questChange;

    /// <summary>True when a stamp or erase was sent before the Use (its answer may not have come), so the registry may differ from how the scenario found it until <see cref="QuestPutBack"/>.</summary>
    public bool QuestRequested => _questRequested;

    /// <summary>True once the quest registry was asked after the way home and is as the scenario found it (the portal's own emote set may have done that; a verb was sent only when it differed).</summary>
    public bool QuestPutBack => _questPutBack;

    /// <summary>True when a failed or disposed play sent the opposite verb without waiting for the answer: the caller must check that the character is as it was found.</summary>
    public bool QuestPutBackAfterFailure => _questFailureCleanup;

    /// <summary>
    /// What became of the quest registry, for the verdict (<c>quest=</c>): <c>put-back</c> (changed, or taken by the portal, and verified as found), <c>unverified</c> (it may differ
    /// from how it was found and nothing confirmed it is back: a failure, a timeout, a cleanup sent without waiting), <c>unchanged</c> (nothing was sent that could have changed it), or
    /// <c>n/a</c> (a play with no exit portal).
    /// </summary>
    public string QuestVerdict => _exit is null ? "n/a" : _questPutBack ? "put-back" : QuestMayDiffer || _questFailureCleanup ? "unverified" : "unchanged";

    /// <summary>True when the use of the portal was sent (its emote set may have erased the quest).</summary>
    private bool UseSent => _use is not null;

    /// <summary>The registry may differ from how the scenario found it, and nothing has verified it is back.</summary>
    private bool QuestMayDiffer => _exit is not null && !_questPutBack && (_questMayHaveChanged || (_exitQuest == ExitQuestMode.Stamp && UseSent));

    /// <summary>After a passed exit the registry is asked and put back: always for a taken exit (the portal erases the quest), and when a refusal run changed it.</summary>
    private bool QuestRestoreNeeded => _exit is not null && !_questPutBack && (_questRequested || _exitQuest == ExitQuestMode.Stamp);

    /// <summary>Asks whether the character holds the quest, and goes on to change that when the mode needs it.</summary>
    private ScenarioStatus QuestCheck(IGameSessionView session)
    {
        var name = _exit!.QuestName;
        if (_questAsk is null)
        {
            _questAsk = session.Tools.HasQuest(name, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (_questAsk.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running; // the tool's own limit (ActionPolicy.ToolSeconds) ends the wait
        }

        var ask = _questAsk;
        _questAsk = null;
        if (outcome.Kind != ActionOutcomeKind.Confirmed || ask.Result is not { } found)
        {
            return QuestFail($"quest-check: asking whether the character holds quest {name}", outcome);
        }

        _questFound = found;
        _log.Info(LogCategory, "the character's quest registry", new LogField("quest", name), new LogField("held", found.Held), new LogField("completions", found.Completions), new LogField("mode", _exitQuest.ToString()));
        if (_exitQuest == ExitQuestMode.Stamp ? found.Held : !found.Held)
        {
            return Go("using", session); // already as the portal test needs it: nothing to change before the Use
        }

        if (_exitQuest == ExitQuestMode.Erase && found.Completions != 1)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"quest-check: the character holds quest {name} with {found.Completions} completions, and an erase followed by a stamp puts back one: refusing to change it; put the character back by hand"));
        }

        return Go("quest-change", session);
    }

    /// <summary>Stamps or erases the quest (as the mode says) and checks the reply says it took.</summary>
    private ScenarioStatus QuestApply(IGameSessionView session)
    {
        var name = _exit!.QuestName;
        var stamp = _exitQuest == ExitQuestMode.Stamp;
        if (_questAsk is null)
        {
            _questRequested = true;
            _questMayHaveChanged = true;
            _questAsk = stamp ? session.Tools.StampQuest(name, ActionSource.Host) : session.Tools.EraseQuest(name, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (_questAsk.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        var ask = _questAsk;
        _questAsk = null;
        if (AnswerSaysNothingChanged(outcome, ask.Result, stamp))
        {
            _questMayHaveChanged = false; // a refusal, a failure or a reply that says it did not take: a cleanup verb would change a registry that never was
        }

        if (outcome.Kind != ActionOutcomeKind.Confirmed || ask.Result is not { } changed)
        {
            return QuestFail($"quest-change: the {(stamp ? "stamp" : "erase")} of quest {name}", outcome);
        }

        if (changed.Held != stamp)
        {
            return Fail($"quest-change: after the {(stamp ? "stamp" : "erase")} the server says the character {(changed.Held ? "holds" : "does not hold")} quest {name}");
        }

        _questChange = changed;
        _log.Info(
            LogCategory,
            stamp ? "the character was given the exit's quest" : "the exit's quest was taken from the character",
            new LogField("quest", name),
            new LogField("held_before", changed.HeldBefore),
            new LogField("completions_before", changed.CompletionsBefore),
            new LogField("completions", changed.Completions));
        return Go("using", session);
    }

    /// <summary>
    /// After the way home: asks the registry (the portal's emote set has changed it), and sends the one verb that brings it back to what the scenario found when it
    /// differs: a stamp for a quest that was held and is gone, an erase for one that was not held and is. A held quest whose count would differ fails loudly.
    /// </summary>
    private ScenarioStatus QuestRestore(IGameSessionView session)
    {
        var name = _exit!.QuestName;
        if (_questFound is not { } found)
        {
            return Fail($"quest-restore: the scenario never learned whether the character held quest {name}; check it with '@lifestone quest has {name}'");
        }

        if (_questAsk is null)
        {
            _questAsk = _questRestoreStamp switch
            {
                true => session.Tools.StampQuest(name, ActionSource.Host),
                false => session.Tools.EraseQuest(name, ActionSource.Host),
                null => session.Tools.HasQuest(name, ActionSource.Host),
            };
            return ScenarioStatus.Running;
        }

        if (_questAsk.Outcome is not { } outcome)
        {
            return ScenarioStatus.Running;
        }

        var ask = _questAsk;
        _questAsk = null;
        var verb = _questRestoreStamp is { } stampIt ? (stampIt ? "stamp" : "erase") : "has";
        var byHand = _questRestoreStamp is { } withVerb ? $"; put the character back with '@lifestone quest {(withVerb ? "stamp" : "erase")} {name}'" : $"; check it with '@lifestone quest has {name}'";
        if (outcome.Kind != ActionOutcomeKind.Confirmed || ask.Result is not { } answer)
        {
            return QuestFail($"quest-restore: the {verb} of quest {name}{byHand}", outcome);
        }

        if (_questRestoreStamp is not { } stamp)
        {
            if (answer.Held == found.Held && (!found.Held || answer.Completions == found.Completions))
            {
                return PutBackDone(answer, "none");
            }

            if (found.Held && answer.Held)
            {
                return Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"quest-restore: quest {name} was held with {found.Completions} completions and now has {answer.Completions}; the verbs cannot put a count back{byHand}"));
            }

            if (found.Held && found.Completions != 1)
            {
                return Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"quest-restore: quest {name} was held with {found.Completions} completions and the portal took it; a stamp makes 1 and the verbs cannot put another count back; stamp it by hand until it has {found.Completions}"));
            }

            _questRestoreStamp = found.Held; // held when found and gone now: stamp; not held when found and held now: erase
            _questMayHaveChanged = true;
            return ScenarioStatus.Running;
        }

        if (answer.Held != stamp)
        {
            return Fail($"quest-restore: after the {verb} the server says the character {(answer.Held ? "holds" : "does not hold")} quest {name}{byHand}");
        }

        if (stamp && answer.Completions != found.Completions)
        {
            return Fail(string.Create(
                CultureInfo.InvariantCulture,
                $"quest-restore: quest {name} was held with {found.Completions} completions and the stamp made it {answer.Completions}; the verbs cannot put a count back"));
        }

        return PutBackDone(answer, verb);
    }

    private ScenarioStatus PutBackDone(QuestResult answer, string sent)
    {
        _questPutBack = true;
        _questMayHaveChanged = false;
        _log.Info(
            LogCategory,
            "the quest registry is back as it was found",
            new LogField("quest", _exit!.QuestName),
            new LogField("held", answer.Held),
            new LogField("completions", answer.Completions),
            new LogField("verb_sent", sent));
        if (_exitQuest == ExitQuestMode.Stamp)
        {
            _log.Info(
                LogCategory,
                "the exit portal's emote set (weenie 29338, ACE Portal.cs:287) also moved the character's sanctuary to Holtburg, enabled recalls and erased the Academy's other quests; that graduated state is the test characters' baseline (docs/session.md section 25)",
                new LogField("quest", _exit.QuestName));
        }

        Stage = "done";
        return ScenarioStatus.Succeeded;
    }

    /// <summary>A failed quest request: the step and the outcome, naming the tool's 5 s limit when the mod gave no answer within it.</summary>
    private ScenarioStatus QuestFail(string step, in ActionOutcome outcome) =>
        Fail(
            outcome.Kind == ActionOutcomeKind.TimedOut
                ? string.Create(CultureInfo.InvariantCulture, $"{step} (the mod gave no answer within the tools' {ActionPolicy.ToolSeconds:0} s limit)")
                : step,
            outcome);

    /// <summary>
    /// True when the answer says the stamp or erase did not happen: the mod refused or failed (an error reply), or it reported the registry unchanged. A timeout, a cancelled
    /// request or an ok reply that could not be read (<c>ToolTracker</c> reports it as a server error whose detail starts with "failed: ") says nothing, so the change may have happened.
    /// </summary>
    private static bool AnswerSaysNothingChanged(in ActionOutcome outcome, QuestResult? result, bool stamped) =>
        outcome.Kind switch
        {
            ActionOutcomeKind.Confirmed => result is { } r && r.Held != stamped,
            ActionOutcomeKind.ServerError => outcome.Detail?.StartsWith("failed: ", StringComparison.Ordinal) != true,
            ActionOutcomeKind.Refused or ActionOutcomeKind.NotImplemented or ActionOutcomeKind.DryRun => true,
            _ => false,
        };

    /// <summary>
    /// A failed or disposed play: when it may have left the registry different from how it was found, sends the opposite verb once without waiting for the answer
    /// (the play is over; the session goes on to the recovery and the log off, in that order) when this play's own stamp or erase may have taken, and always logs
    /// <c>TODO(st16-quest-restore)</c> so that the caller verifies. It sends nothing from <c>quest-restore</c>, nothing after an answer that said the change did not
    /// happen, and nothing for a quest found held and then possibly taken by the portal (a stamp sent blind would add a completion if the portal had not).
    /// </summary>
    private void PutTheQuestBackAfterFailure()
    {
        if (_questCleanupHandled || !QuestMayDiffer)
        {
            return;
        }

        _questCleanupHandled = true;
        var name = _exit!.QuestName;
        var verb = _exitQuest == ExitQuestMode.Stamp ? "erase" : "stamp";
        var send = _questMayHaveChanged && Stage != "quest-restore" && _questRestoreStamp is null && _session is not null;
        if (send)
        {
            try
            {
                _ = _exitQuest == ExitQuestMode.Stamp ? _session!.Tools.EraseQuest(name, ActionSource.Host) : _session!.Tools.StampQuest(name, ActionSource.Host);
                _questFailureCleanup = true;
            }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
            {
                _log.Warn(LogCategory, "the quest cleanup could not be sent: " + e.Message, new LogField("quest", name));
            }
        }

        _log.Warn(
            LogCategory,
            (_questFailureCleanup
                ? $"TODO(st16-quest-restore): the play ended with the quest registry possibly changed; '{verb}' was sent without waiting for its answer: "
                : "TODO(st16-quest-restore): the play ended with the quest registry possibly different from how it was found and nothing was sent: ")
            + $"verify with '@lifestone quest has {name}' (found held: {_questFound?.Held.ToString() ?? "unknown"}, completions {_questFound?.Completions.ToString(CultureInfo.InvariantCulture) ?? "unknown"})",
            new LogField("quest", name),
            new LogField("mode", _exitQuest.ToString()),
            new LogField("stage", Stage));
    }
}
