using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions;

/// <summary>Where an <see cref="ActionTracker"/> is.</summary>
internal enum TrackerState : byte
{
    /// <summary>Accepted, waiting in its lane.</summary>
    Queued,

    /// <summary>Sent; waiting for its confirmation, an error or its timeout.</summary>
    InFlight,

    /// <summary>Completed, or handed to the completion step.</summary>
    Done,
}

/// <summary>
/// One action from acceptance to completion (step ST9): its lane, its timeout, the precondition checked when its turn
/// comes, what it sends, and the confirmation it waits for, fed by the session's events. The Action API owns the life
/// cycle (docs/session.md §7); a tracker only says what its action needs.
/// </summary>
/// <remarks>
/// <para>Observations (the <c>On*</c> members) are called for trackers in flight only, during the bus dispatch, and must
/// not complete the handle: they <see cref="Resolve"/> it, and the Action API completes it in its next
/// <see cref="ActionApi.Update"/>, after the tick's events. A tracker resolves once; later resolutions are ignored. An
/// event one tracker takes (a UseDone, an echo, a reply: the members returning bool) goes to the oldest unresolved tracker
/// that takes it; every other event reaches every tracker in flight, resolved or not.</para>
/// <para>Game thread only.</para>
/// </remarks>
internal abstract class ActionTracker
{
    /// <summary>A tracker for <paramref name="handle"/>.</summary>
    /// <param name="handle">The handle the caller holds.</param>
    /// <param name="lane">The lane it waits in.</param>
    /// <param name="timeoutSeconds">How long after sending it may wait for its confirmation (<see cref="double.PositiveInfinity"/>: no limit).</param>
    protected ActionTracker(ActionHandle handle, ActionLane lane, double timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(handle);
        Handle = handle;
        Lane = lane;
        TimeoutSeconds = timeoutSeconds;
    }

    /// <summary>The handle the caller holds.</summary>
    internal ActionHandle Handle { get; }

    /// <summary>The lane it waits in.</summary>
    internal ActionLane Lane { get; }

    /// <summary>How long after sending it may wait for its confirmation.</summary>
    internal double TimeoutSeconds { get; private protected set; }

    /// <summary>Where the tracker is.</summary>
    internal TrackerState State { get; set; }

    /// <summary>True once the lane's rate limit made this action wait (the action log and the inspector count it).</summary>
    internal bool RateLimited { get; set; }

    /// <summary>The outcome an observation settled on, waiting for the next update to complete the handle; null until then.</summary>
    internal ActionOutcome? Resolution { get; private set; }

    /// <summary>True when the action needs the network session (every action but the movement intents).</summary>
    internal virtual bool NeedsSession => true;

    /// <summary>What it would send, for dry run's log line (e.g. <c>GameAction 0x0015 Talk</c>).</summary>
    internal abstract string WouldSend { get; }

    /// <summary>
    /// Settles the outcome of a tracker in flight; ignored when it is not in flight or already resolved. The handle
    /// completes in the next update.
    /// </summary>
    internal void Resolve(ActionOutcome outcome)
    {
        if (State == TrackerState.InFlight && Resolution is null)
        {
            Resolution = outcome;
        }
    }

    /// <summary>
    /// The precondition, checked when the action's turn comes (the state may have changed while it waited): why it cannot
    /// go now, or null.
    /// </summary>
    internal virtual string? Refusal(ActionApi api) => null;

    /// <summary>
    /// M5 7 D141: the action was accepted (queued, or about to go now): a tracker notes what its before-check compares with at its turn, such as whether the object it names is in the
    /// object table now (<see cref="TargetGone"/>).
    /// </summary>
    internal virtual void Accepted(ActionApi api)
    {
    }

    /// <summary>
    /// M5 7 D141: the before-check of an action that names an object: <paramref name="guid"/> was in the object table when the action was accepted (<paramref name="seen"/>) and is not
    /// now (a corpse that rotted, a creature that died or left), so the server would answer NoObject or TargetNotAcquired: the refusal text, else null. An object never seen is not
    /// refused (the client's table is not the server's: the server judges it).
    /// </summary>
    protected string? TargetGone(ActionApi api, uint guid, bool seen, string what) =>
        seen && guid != 0 && guid != api.State.Self.Guid && !api.State.Objects.TryGet(guid, out _)
            ? Refuse(ActionReason.TargetGone, "the " + what + " " + ActionText.Hex(guid) + " is gone: it was in the object table when the action was asked and is not now")
            : null;

    /// <summary>M5 7 D141: the typed reason of the last <see cref="Refusal"/> (set by <see cref="Refuse"/>), or <see cref="ActionReason.None"/>.</summary>
    internal ActionReason RefusalReason { get; private set; }

    /// <summary>
    /// M5 7 D141 ("check before"): the precondition with its typed reason. Runs <see cref="Refusal"/> and returns its text; <see cref="RefusalReason"/> is the reason the tracker named
    /// (<see cref="Refuse"/>), else <see cref="ActionReason.NotInWorld"/> for the in-world refusal every tracker shares, else none. The Action API asks this, never <see cref="Refusal"/> directly.
    /// </summary>
    internal string? CheckRefusal(ActionApi api)
    {
        RefusalReason = ActionReason.None;
        var refusal = Refusal(api);
        if (refusal is not null && RefusalReason == ActionReason.None && refusal == Trackers.WorldTrackers.NotInWorld(api))
        {
            RefusalReason = ActionReason.NotInWorld;
        }

        return refusal;
    }

    /// <summary>M5 7 D141: a <see cref="Refusal"/> that names its reason: records <paramref name="reason"/> for <see cref="CheckRefusal"/> and returns <paramref name="detail"/>.</summary>
    protected string Refuse(ActionReason reason, string detail)
    {
        RefusalReason = reason;
        return detail;
    }

    /// <summary>
    /// Why the action, which the precondition accepted, must still wait in its lane (its turn has come but what it needs has not): null when it may go. Asked at each dispatch after <see cref="Refusal"/>, so a change
    /// of state that makes it impossible still ends it, and before the lane's rate limit takes a token. Used by LoginComplete's wait for the player's contents.
    /// </summary>
    internal virtual string? WaitReason(ActionApi api) => null;

    /// <summary>
    /// Sends the action. Returns the outcome when it completes at once (<see cref="ActionOutcomeKind.Sent"/> or
    /// <see cref="ActionOutcomeKind.Local"/>), or null to wait for the confirmation. Net's exceptions are mapped by the
    /// caller (a refused send, a closed session, a net loud stub).
    /// </summary>
    /// <param name="session">The network session (null only when <see cref="NeedsSession"/> is false).</param>
    /// <param name="api">The Action API (state, local notices, the bus, the frame time).</param>
    internal abstract ActionOutcome? Send(IAcSession? session, ActionApi api);

    /// <summary>
    /// The outcome to complete with, given the one an observation settled on: a tracker may add what it saw between the
    /// resolution and the update (a use's teleport).
    /// </summary>
    internal virtual ActionOutcome Finish(ActionOutcome resolved) => resolved;

    /// <summary>The tracker reached its timeout and is about to end as <see cref="ActionOutcomeKind.TimedOut"/> (a place to log what it saw; it must not resolve).</summary>
    internal virtual void OnTimedOut(ActionApi api)
    {
    }

    /// <summary>Checks the state after the tick's events, for confirmations that are conditions rather than events.</summary>
    internal virtual void Poll(ActionApi api)
    {
    }

    /// <summary>
    /// The session's phase changed: a failure that carries its reason ends the action (<see cref="SessionFailed"/>), and a
    /// close cancels it. A failure reported without its reason waits for the one that has it (the network session
    /// reports the phase first); the update's check of the phase settles it either way.
    /// </summary>
    internal virtual void OnPhase(in SessionPhaseChanged e, ActionApi api)
    {
        switch (e.Phase)
        {
            case SessionPhase.Failed when e.Failure is not null:
                Resolve(SessionFailed(e.Failure));
                break;
            case SessionPhase.Closed:
                Resolve(new ActionOutcome(ActionOutcomeKind.Cancelled, 0, "the session closed"));
                break;
        }
    }

    /// <summary>
    /// The outcome when the session fails while the action is in flight: <see cref="ActionOutcomeKind.Cancelled"/> by
    /// default; the session-phase trackers make the failure their own outcome (<see cref="FailureOutcome"/>).
    /// </summary>
    internal virtual ActionOutcome SessionFailed(SessionFailure? failure) =>
        new(ActionOutcomeKind.Cancelled, 0, "the session failed: " + DescribeFailure(failure));

    /// <summary>The character list changed.</summary>
    internal virtual void OnCharacterList(in CharacterListChanged e)
    {
    }

    /// <summary>F643 answered a create or a restore.</summary>
    internal virtual void OnCreateResult(in CharacterCreateResult e)
    {
    }

    /// <summary>F659 CharacterError arrived.</summary>
    internal virtual void OnCharacterError(in CharacterErrorReceived e)
    {
    }

    /// <summary>The player's travel state changed.</summary>
    internal virtual void OnTravel(in SelfTravelChanged e)
    {
    }

    /// <summary>F751 PlayerTeleport was accepted.</summary>
    internal virtual void OnTeleport(in PlayerTeleportAnnounced e)
    {
    }

    /// <summary>
    /// A quality of the player changed (the reducer applied a PrivateUpdate; a stale one, dropped by the sequence rule, is not published).
    /// Called for every tracker in flight; the state already holds the new value.
    /// </summary>
    internal virtual void OnQuality(in QualitiesChanged e, ActionApi api)
    {
    }

    /// <summary>
    /// True while the tracker's lane must stay busy although the action is complete (a cast: ACE is busy until its UseDone, so the next cast would be refused).
    /// Asked after the tracker settled; the hold ends when this turns false, or at the tracker's timeout counted from the send.
    /// </summary>
    internal virtual bool HoldsLane => false;

    /// <summary>
    /// True for an action that ends with ACE's UseDone 0x01C7 (a Use, a Cast). The UseDone carries no request id, so two such actions of different lanes are never in flight together
    /// (<see cref="ActionApi"/> serialises them against each other, M3 1.2 fix R1): the one UseDone in the air then belongs to the one action that is out.
    /// </summary>
    internal virtual bool ConsumesUseDone => false;

    /// <summary>An object's motion was accepted (F74C), the player's own included. Called for every tracker in flight.</summary>
    internal virtual void OnMotion(in ObjectMotionChanged e, ActionApi api)
    {
    }

    /// <summary>An object's sound was played (F750). Called for every tracker in flight (M4 2.1 s2: a locked chest's 0x94).</summary>
    internal virtual void OnSound(in ObjectSoundPlayed e, ActionApi api)
    {
    }

    /// <summary>A combat notice arrived (0x01B1 to 0x01B4). Called for every tracker in flight.</summary>
    internal virtual void OnCombat(in CombatEvent e, ActionApi api)
    {
    }

    /// <summary>The player killed something (0x01AD). Called for every tracker in flight.</summary>
    internal virtual void OnKilled(in PlayerKilledSomething e, ActionApi api)
    {
    }

    /// <summary>The attack sequence changed (M4 1.2 s2). Called for every tracker in flight.</summary>
    internal virtual void OnAttack(in AttackChanged e, ActionApi api)
    {
    }

    /// <summary>A player's inventory changed (0x0022, 0x0023, 0x019A, 0x0024, ...). Called for every tracker in flight.</summary>
    internal virtual void OnInventory(in InventoryChanged e, ActionApi api)
    {
    }

    /// <summary>The server refused an inventory request (GameEvent 0x00A0). Called for every tracker in flight.</summary>
    internal virtual void OnInventoryFailed(in InventoryFailed e)
    {
    }

    /// <summary>The player's combat mode changed (the 0x02CD for property 40). Called for every tracker in flight.</summary>
    internal virtual void OnCombatMode(in CombatModeChanged e)
    {
    }

    /// <summary>An enchantment was added to, or removed from, the player's registry. Called for every tracker in flight.</summary>
    internal virtual void OnEnchantment(in EnchantmentChanged e)
    {
    }

    /// <summary>An object was created (CreateObject). Called for every tracker in flight, after the object is in the table.</summary>
    internal virtual void OnObjectCreated(in ObjectCreated e, ActionApi api)
    {
    }

    /// <summary>
    /// UseDone arrived; true when this tracker took it (the oldest Use in flight does; a cast whose lane is held takes it first, in flight or not:
    /// <see cref="Resolve"/> ignores it once the tracker is done).
    /// </summary>
    internal virtual bool OnUseDone(in UseDone e) => false;

    /// <summary>UpdateHealth arrived; true when this tracker took it.</summary>
    internal virtual bool OnHealth(in HealthUpdated e) => false;

    /// <summary>A chat line arrived; true when this tracker took it.</summary>
    internal virtual bool OnChat(in ChatReceived e, ActionApi api) => false;

    /// <summary>A Lifestone tool reply arrived; true when this tracker took it.</summary>
    internal virtual bool OnToolReply(ToolReplyLine reply, ActionApi api) => false;

    /// <summary>The player tools became unavailable.</summary>
    internal virtual void OnToolsUnavailable(in ToolsUnavailable e)
    {
    }

    /// <summary>
    /// Completes the handle (<see cref="ActionHandle.TryComplete(ActionOutcome, double, Action{ActionHandle, Exception})"/>;
    /// a tool tracker sets its typed result first).
    /// </summary>
    internal virtual bool CompleteHandle(ActionOutcome outcome, double time, Action<ActionHandle, Exception> onHandlerError) =>
        Handle.TryComplete(outcome, time, onHandlerError);

    /// <summary>
    /// A session failure as the outcome of a session-phase action: the network session's timeouts and silence are
    /// <see cref="ActionOutcomeKind.TimedOut"/>; a boot, a ban, a data mismatch, a character error and the transport's
    /// failures are <see cref="ActionOutcomeKind.ServerError"/> with the failure's code.
    /// </summary>
    internal static ActionOutcome FailureOutcome(SessionFailure? failure)
    {
        if (failure is null)
        {
            return new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the session failed (no reason given)");
        }

        return failure.Kind switch
        {
            SessionFailureKind.ConnectTimeout or SessionFailureKind.HandshakeTimeout or SessionFailureKind.DataCheckTimeout
                or SessionFailureKind.EnterWorldTimeout or SessionFailureKind.LogoffTimeout or SessionFailureKind.Silence =>
                new ActionOutcome(ActionOutcomeKind.TimedOut, failure.Code, DescribeFailure(failure)),
            _ => new ActionOutcome(ActionOutcomeKind.ServerError, failure.Code, DescribeFailure(failure)),
        };
    }

    /// <summary>A failure in one phrase for an outcome's detail.</summary>
    protected static string DescribeFailure(SessionFailure? failure) =>
        failure is null ? "no reason given" : failure.Kind + ": " + failure.Message;
}
