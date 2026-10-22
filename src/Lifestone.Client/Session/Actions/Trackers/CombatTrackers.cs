using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Net.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// What <see cref="MeleeAttackTracker"/> and <see cref="MissileAttackTracker"/> share (M4 1.2 s2). ACE answers an attack send with no reply message of its own: it walks the player to the target (MoveToObject
/// F74C), swings or launches (the attack sequence, <see cref="IGameSessionView.Attack"/>), and with AutoRepeatAttacks on repeats by itself, so one send drives a whole run (capture.md C1, C3). The tracker therefore
/// completes at the first sign that ACE took the send and leaves the run's end to the sequence (Done on GameEvent 0x01A7 {0x0036}).
/// </summary>
/// <remarks>
/// <para>A refusal is a WeenieError (0x028A, or 0x028B with a string) or a transient string (0x02EB) followed by 0x01A7 (ACE <c>W/Player_Melee.cs</c>, <c>W/Player_Missile.cs</c>). The error is latched when it is seen
/// and settles in <see cref="Poll"/>, after the tick's events, unless a start came first, so the 0x01A7 that follows it need not be observable (the reducer publishes every 0x01A7, changed or not, so a refusal
/// 0x01A7 {0x0036} after a sequence that already ended 0x0036 is seen at once). A 0x01A7 with a code that starts nothing and follows no error is a silent refusal, reported as <see cref="ActionOutcomeKind.ServerError"/>
/// with the code kept.</para>
/// <para>An attack sent while a sequence is running gets no answer: ACE queues the power (<c>E/AttackQueue.cs</c>), so it completes <see cref="ActionOutcomeKind.Confirmed"/> at once with the detail "queued" and the
/// sequence goes on (a hunt waits for the sequence's Done before it attacks again).</para>
/// </remarks>
internal abstract class AttackTrackerBase(ActionHandle handle, double timeoutSeconds, uint target) : ActionTracker(handle, ActionLane.Attack, timeoutSeconds)
{
    /// <summary>The WeenieError ACE answers "you are too busy" with (<c>YoureTooBusy</c>).</summary>
    internal const uint TooBusyCode = 0x001D;

    /// <summary>ACE's refusal of an attack on a target that cannot be attacked (0x04EB).</summary>
    internal const uint CannotAttackCode = 0x04EB;

    /// <summary>ACE's refusal of an attack while the player cannot (0x04CC).</summary>
    internal const uint CannotAttackNowCode = 0x04CC;

    /// <summary>ACE's refusal of a missile attack on a target out of range (0x0550).</summary>
    internal const uint OutOfRangeCode = 0x0550;

    /// <summary>ACE's refusal of a missile attack (0x03F9).</summary>
    internal const uint MissileRefusedCode = 0x03F9;

    /// <summary>The sequence number at the send: a sequence with a higher number began after the send.</summary>
    protected int BaselineNumber { get; private set; }

    /// <summary>The refusal seen after the send, or null.</summary>
    protected ActionOutcome? Error { get; private set; }

    /// <summary>True once a start was seen (the tracker confirmed).</summary>
    protected bool Started { get; private set; }

    /// <summary>The target the attack is for.</summary>
    protected uint Target => target;

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <summary>Sends the attack's message.</summary>
    protected abstract void SendMessage(IAcSession session);

    /// <summary>True when WeenieError <paramref name="code"/> refuses this attack.</summary>
    protected abstract bool IsRefusalCode(uint code);

    /// <summary>True when a transient string (0x02EB) with <paramref name="text"/> refuses this attack.</summary>
    protected abstract bool IsRefusalText(string text, ActionApi api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var attack = api.Reducer.Attack;
        BaselineNumber = attack.Number;
        var running = attack.Active;
        SendMessage(session!);
        return running
            ? new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "queued: a sequence is already running, so ACE queues the power and answers nothing (the sequence goes on)")
            : null;
    }

    /// <summary>Confirms with <paramref name="detail"/>.</summary>
    protected void Confirm(string detail)
    {
        Started = true;
        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, detail));
    }

    /// <inheritdoc/>
    internal override void OnAttack(in AttackChanged e, ActionApi api)
    {
        if (Started)
        {
            return;
        }

        if (e.Number > BaselineNumber)
        {
            // A sequence began after the send: its swing or launch (a sequence is begun only by one) confirms, for the target or an unknown one.
            if (e.Target == 0 || e.Target == Target)
            {
                Confirm("the attack sequence started (swings " + ActionText.Count(e.Swings) + ", shots " + ActionText.Count(e.Shots) + ")");
            }

            return;
        }

        if (e.Number == BaselineNumber && e.State == AttackSequenceState.Done && e.LastWeenieError != 0)
        {
            Resolve(Error ?? new ActionOutcome(
                ActionOutcomeKind.ServerError,
                e.LastWeenieError,
                "the server ended the attack with AttackDone " + ActionText.Hex(e.LastWeenieError) + " and no start and no error text: a silent refusal"));
        }
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        if (Started || Error is not null)
        {
            return false;
        }

        var line = e.Line;
        if (line.Kind == ChatKind.WeenieError && (IsRefusalCode(line.ChatType) || line.Text.Length > 0))
        {
            // A 0x028B carries a string: any of them refuses; a 0x028A refuses by its code.
            Error = new ActionOutcome(
                ActionOutcomeKind.ServerError,
                line.ChatType,
                "the server refused the attack (WeenieError " + ActionText.Hex(line.ChatType) + (line.Text.Length > 0 ? ", " + line.Text : string.Empty) + ")");
        }
        else if (line.Kind == ChatKind.Transient && IsRefusalText(line.Text, api))
        {
            Error = new ActionOutcome(ActionOutcomeKind.ServerError, 0, "the server refused the attack: " + line.Text);
        }

        return false; // an observer: other trackers read the same line
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Error is { } error && !Started && Resolution is null)
        {
            Resolve(error);
        }
    }
}

/// <summary>
/// <see cref="IActionApi.Attack"/>: GameAction 0x0008 (retail <c>CM_Combat::Event_TargetedMeleeAttack</c>; ACE <c>W/Player_Melee.cs</c>). Confirmed on the first of: the player's own MoveToObject or TurnToObject F74C naming the target (the approach;
/// refusals send no motion, so it means ACE accepted: capture.md C1), the attack sequence starting (the swing, or 0x01B8), a 0x01B1 or 0x01B3 naming the target, or a 0x01AD that names it (the squelch list is not kept: a 0x01B1 or 0x01B3
/// can be squelched, <c>TODO(m3-squelch-combat)</c>; the 0x01AD is not). ServerError on WeenieError 0x001D, 0x04EB, 0x04CC, any 0x028B, or a transient "You cannot attack...", or a 0x01A7 with a code that starts nothing. Time limit 20 s
/// from the send: ACE's approach can take up to 15 s (<c>W/Player_Melee.cs</c> 204-226).
/// A 028A <c>YouChargedTooFar</c> (0x003D) after the approach confirmed (ACE cancels an approach once the player is more than 15 m from where it began: <c>Player_Move.cs</c> 167 and 241, <c>MoveToManager.cs</c> 489, then 01A7 0x0036)
/// leaves the handle <b>Confirmed</b>: the attack was taken and the approach did start, and a resolved handle is final; the cancellation reaches the caller as the sequence's 01A7 (<see cref="AttackChanged"/> with no swing)
/// and the 028A chat line, which the hunt reads (<c>HuntScenario</c> retries the approach from where the player stopped).
/// </summary>
internal sealed class MeleeAttackTracker(ActionHandle handle, uint targetGuid, AttackHeight height, float power)
    : AttackTrackerBase(handle, ActionPolicy.AttackSeconds, targetGuid)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0008 TargetedMeleeAttack " + ActionText.Hex(Target) + " height " + (uint)height + " power " + power.ToString("0.###", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session) => session.SendMeleeAttack(Target, (uint)height, power);

    /// <inheritdoc/>
    protected override bool IsRefusalCode(uint code) => code is TooBusyCode or CannotAttackCode or CannotAttackNowCode;

    /// <inheritdoc/>
    protected override bool IsRefusalText(string text, ActionApi api) => text.Contains("cannot attack", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    internal override void OnMotion(in ObjectMotionChanged e, ActionApi api)
    {
        if (!e.IsSelf || Started)
        {
            return;
        }

        var motion = e.Motion;
        if ((motion.MovementType == MovementType.MoveToObject && motion.MoveTo?.TargetGuid == Target) || (motion.MovementType == MovementType.TurnToObject && motion.TurnTo?.TargetGuid == Target))
        {
            Confirm("ACE took the attack: the player's " + motion.MovementType + " toward " + ActionText.Hex(Target) + " (the approach)");
        }
    }

    /// <inheritdoc/>
    internal override void OnCombat(in CombatEvent e, ActionApi api)
    {
        if (Started || e.Kind is not (CombatEventKind.AttackLanded or CombatEventKind.AttackEvaded))
        {
            return;
        }

        if (api.State.Objects.TryGet(Target, out var victim) && string.Equals(victim.Name, e.Who, StringComparison.Ordinal))
        {
            Confirm("a combat notice for the target (" + (e.Evaded ? "0x01B3 evaded" : "0x01B1 hit") + ")");
        }
    }

    /// <inheritdoc/>
    internal override void OnKilled(in PlayerKilledSomething e, ActionApi api)
    {
        if (Started)
        {
            return;
        }

        if (api.State.Objects.TryGet(Target, out var victim) && victim.Name.Length > 0 && e.Text.Contains(victim.Name, StringComparison.Ordinal))
        {
            Confirm("the kill notice (0x01AD) names the target");
        }
    }
}

/// <summary>
/// <see cref="IActionApi.MissileAttack"/>: GameAction 0x000A (ACE <c>W/Player_Missile.cs</c>). Confirmed on the first of: the attack sequence starting or a shot counted (the held ammunition's 0x0197 stack fall: s1's
/// <see cref="AttackSequence.Shots"/>), or the arrow's CreateObject (F745) taken for ours by the war-spell projectile rule (<see cref="CastTracker.RefuseProjectile"/>: Missile physics state, within
/// <see cref="CastTracker.ProjectileRangeMetres"/> of the player, velocity away and within <see cref="CastTracker.ProjectileAimToleranceDegrees"/> of the aim; <c>TODO(m3-cast-projectile-attribution)</c>, shared). The last arrow may delete the
/// stack with no 0x0197 (<c>TODO(m4-last-arrow-launch)</c>): the arrow's F745 confirms that launch. ServerError on WeenieError 0x0550 (out of range), 0x03F9, 0x001D, 0x04EB, 0x04CC, any 0x028B or a transient string (0x02EB) that is "You cannot attack X" or "You are out of ammunition!" (any other transient line is logged once as <c>TODO(m4-missile-transient-text)</c> and does not refuse), or a 0x01A7 with a code that starts nothing. Time limit 10 s.
/// </summary>
internal sealed class MissileAttackTracker(ActionHandle handle, uint targetGuid, AttackHeight height, float accuracy)
    : AttackTrackerBase(handle, ActionPolicy.MissileAttackSeconds, targetGuid)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x000A TargetedMissileAttack " + ActionText.Hex(Target) + " height " + (uint)height + " accuracy " + accuracy.ToString("0.###", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    protected override void SendMessage(IAcSession session) => session.SendMissileAttack(Target, (uint)height, accuracy);

    /// <inheritdoc/>
    protected override bool IsRefusalCode(uint code) => code is OutOfRangeCode or MissileRefusedCode or TooBusyCode or CannotAttackCode or CannotAttackNowCode;

    /// <inheritdoc/>
    protected override bool IsRefusalText(string text, ActionApi api)
    {
        // The texts ACE sends before a launch (cache/m4-cloud-results/PACKAGE-1.md, W/Player_Missile.cs and W/Player_Combat.cs 843-856): "You cannot attack X" (!CanDamage) and "You are out of ammunition!".
        if (text.Contains("cannot attack", StringComparison.OrdinalIgnoreCase) || text.Contains("out of ammunition", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_unknownTransientLogged)
        {
            _unknownTransientLogged = true;
            api.Log.Warn(SessionLog.Category, "TODO(m4-missile-transient-text): an unlisted transient line during a missile attack does not refuse it: \"" + text + "\"");
        }

        return false;
    }

    private bool _unknownTransientLogged;
    private bool _lastArrowLogged;

    /// <inheritdoc/>
    internal override void OnObjectCreated(in ObjectCreated e, ActionApi api)
    {
        if (Started || e.IsSelf || !api.State.Objects.TryGet(e.Guid, out var obj) || (obj.State & PhysicsState.Missile) == 0)
        {
            return;
        }

        CellFrame? targetAt = api.State.Objects.TryGet(Target, out var victim) ? victim.Position : null;
        if (CastTracker.RefuseProjectile(api.State.Self.Position, obj.Position, obj.Velocity, targetAt, null, null) is not null)
        {
            return;
        }

        if (!_lastArrowLogged)
        {
            // M4 1.3 s2 (the 1.2 review's R11): the arrow's F745 is taken as the launch with no 0x0197 before it, the case where AttackSequence.Shots does not count the launch. Once per run (one tracker is one send).
            _lastArrowLogged = true;
            api.Log.Info(SessionLog.Category, "TODO(m4-last-arrow-launch): the arrow " + ActionText.Hex(e.Guid) + " was taken as the launch with no 0x0197 before it (the last arrow may delete the stack instead); the attack sequence's shot count does not include it");
        }

        Confirm("the arrow " + ActionText.Hex(e.Guid) + " was created (TODO(m3-cast-projectile-attribution): taken for ours by its origin within "
            + ActionText.Count((long)CastTracker.ProjectileRangeMetres) + " m of the player, its velocity away from the player and within " + ActionText.Count((long)CastTracker.ProjectileAimToleranceDegrees)
            + " degrees of the aim; not proved)");
    }
}

/// <summary>
/// <see cref="IActionApi.CancelAttack"/>: GameAction 0x01B7 (retail <c>CM_Combat::Event_CancelAttack</c>; ACE <c>W/Player_Melee.cs</c> 240-250). The swing in progress finishes first and the sequence ends with 0x01A7 {0x0036}: that is
/// <see cref="ActionOutcomeKind.Confirmed"/>. With no sequence running ACE answers nothing: the action completes as <see cref="ActionOutcomeKind.Sent"/> after the send. Time limit 5 s.
/// </summary>
internal sealed class CancelAttackTracker(ActionHandle handle) : ActionTracker(handle, ActionLane.Attack, ActionPolicy.CancelAttackSeconds)
{
    private int _number;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x01B7 CancelAttack";

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        var attack = api.Reducer.Attack;
        _number = attack.Number;
        session!.SendCancelAttack();
        return attack.Active ? null : new ActionOutcome(ActionOutcomeKind.Sent, 0, "no attack sequence was running; the server does not answer");
    }

    /// <inheritdoc/>
    internal override void OnAttack(in AttackChanged e, ActionApi api)
    {
        if (e.Number == _number && e.State == AttackSequenceState.Done)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, e.LastWeenieError, "the attack sequence ended (AttackDone " + ActionText.Hex(e.LastWeenieError) + ")"));
        }
    }
}
