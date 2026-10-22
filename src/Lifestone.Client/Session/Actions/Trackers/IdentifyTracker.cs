using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.Identify"/>: GameAction 0x00C8 {guid} (ACE <c>GameActionIdentifyObject</c>, <c>Player.HandleActionIdentifyObject</c> <c>Player.cs:256-345</c>, ACEmulator/ACE@95ef426d), which retail sends from the
/// examination UI (<c>ClientUISystem::ExamineObject</c> 11.6096 0x005657B0, which reaches the sender <c>CM_Item::Event_Appraise</c> 0x006A94A0). ACE has no range or busy check and answers every non-zero guid with one GameEvent 0x00C9, 1 to 10 ms
/// later (capture.md C3: a kit, a wielded mace, an NPC, a repeat at +1 ms, a missing guid with flags 0 and success 0); guid 0 clears its target and gets no reply (the call refuses it before this tracker exists).
/// </summary>
/// <remarks>
/// <para>It completes on the first answer for the guid that arrived after the send, read by polling the model (<see cref="AppraisalCache.Sequence"/> taken at the send against <see cref="Appraisal.Sequence"/>): the
/// 0x00C9 carries the guid, so no lane hold is needed (no busy refusal was seen). <see cref="ActionOutcomeKind.Confirmed"/> is any answer: a success-false answer is a result, not a refusal, with the detail "the roll failed"
/// (<see cref="IdentifyResultKind.Failed"/>); an answer with no section is <see cref="IdentifyResultKind.NotFound"/>. Only creatures roll (Assess Person or Creature against Deception) and ACE's failed roll starts a 5 s cooldown: a repeat
/// within it is answered failed with no roll, which the detail says (<see cref="IdentifyResult.Cooldown"/>). A repeat on the current successful target is answered with success and no roll (capture.md C3: +1 ms).
/// Two Identifies of one guid leave one after the other (<see cref="WaitReason"/>), so each takes its own answer.</para>
/// </remarks>
internal sealed class IdentifyTracker(IdentifyHandle handle, uint guid) : ActionTracker(handle, ActionLane.Query, ActionPolicy.IdentifySeconds)
{
    private long _sentSequence;
    private double _sentAt;
    private Appraisal? _earlier;
    private IdentifyResult? _result;

    /// <summary>The object asked about.</summary>
    internal uint Guid => guid;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x00C8 Identify " + ActionText.Hex(guid);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override string? WaitReason(ActionApi api) => api.IdentifyAwaiting(guid, this) ? "an earlier Identify of this object has not been answered" : null;

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var cache = api.State.Appraisals;
        _sentSequence = cache.Sequence;
        _sentAt = api.Clock.Now;
        _earlier = cache.TryGet(guid, out var earlier) ? earlier : null;
        session!.SendIdentify(guid);
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (!api.State.Appraisals.TryGet(guid, out var answer) || answer.Sequence <= _sentSequence)
        {
            return;
        }

        // ACE's cooldown: a failed answer to a request within 5 s of an earlier failed answer on the same guid is the cooldown's, not a new roll.
        var cooldown = !answer.Success && !answer.IsNotFound && _earlier is { Success: false, IsNotFound: false } earlier && _sentAt - earlier.ReceivedAt < ActionPolicy.IdentifyFailCooldownSeconds;
        if (answer.IsNotFound)
        {
            _result = new IdentifyResult(IdentifyResultKind.NotFound, answer);
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "the server knows no such object (flags 0, success 0)"));
        }
        else if (!answer.Success)
        {
            _result = new IdentifyResult(IdentifyResultKind.Failed, answer, cooldown);
            Resolve(new ActionOutcome(
                ActionOutcomeKind.Confirmed,
                0,
                cooldown
                    ? "the roll failed (a repeat within 5 s of a failed roll on this object: the server answers failed with no new roll)"
                    : "the roll failed"));
        }
        else
        {
            _result = new IdentifyResult(IdentifyResultKind.Appraised, answer);
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed));
        }
    }

    /// <inheritdoc/>
    internal override bool CompleteHandle(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError) =>
        ((IdentifyHandle)Handle).TryComplete(outcome.Kind == ActionOutcomeKind.Confirmed ? _result : null, outcome, time, onHandlerError);
}
