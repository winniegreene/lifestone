namespace Lifestone.Data.Animation;

/// <summary>Result of one <see cref="IPoseDriver.Update"/> quantum.</summary>
/// <param name="Hooks">
/// Hooks fired during the quantum, in retail order (the caller hands effect hooks to an effect object). The list is the
/// driver's own, reused by every <see cref="IPoseDriver.Update"/> so the driver adds no allocation: read it before the next
/// update, and copy what must outlive it.
/// </param>
/// <param name="RootMotion">Root-motion offset of the quantum (retail <c>offset_frame</c>, before the object-scale/contact rule).</param>
public readonly record struct MotionUpdate(IReadOnlyList<AnimHook> Hooks, AcFrame RootMotion);

/// <summary>Something that animates an object's parts (a motion table, or a Setup's default animation).</summary>
public interface IPoseDriver
{
    /// <summary>
    /// Advances by <paramref name="quantum"/> seconds. The returned <see cref="MotionUpdate.Hooks"/> stays valid until the
    /// next call.
    /// </summary>
    MotionUpdate Update(double quantum);

    /// <summary>Per-part frames of the current pose (relative to the object frame), or null.</summary>
    IReadOnlyList<AcFrame>? Pose { get; }
}

/// <summary>
/// A Setup's default animation (retail <c>CPartArray::InitDefaults</c>: <c>append_animation(AnimData(default_anim, 0, −1, 30.0))</c>),
/// looping forever; used by objects without a motion table, e.g. some spell projectiles.
/// </summary>
public sealed class DefaultAnimationPlayer : IPoseDriver
{
    /// <summary>Retail <c>AnimData::AnimData</c> default framerate (30.0).</summary>
    public const float DefaultFramerate = 30f;

    private readonly List<AnimHook> _fired = [];

    public DefaultAnimationPlayer(IAnimationSource animations, uint defaultAnimationId)
    {
        Sequence = new AnimSequence(animations) { HookSink = _fired.Add };
        Sequence.AppendAnimation(new AnimDataRef(defaultAnimationId, 0, -1, DefaultFramerate));
    }

    public AnimSequence Sequence { get; }

    public PoseSampling Sampling { get; set; } = PoseSampling.RetailFloor;

    public IReadOnlyList<AcFrame>? Pose => Sequence.SamplePose(Sampling);

    /// <summary>
    /// Advances the sequence by <paramref name="quantum"/> seconds (retail <c>CPartArray::Update</c>). The hooks are
    /// returned in the player's reused list (valid until the next call), not a copy, so the player adds no allocation.
    /// </summary>
    public MotionUpdate Update(double quantum)
    {
        _fired.Clear();
        var offset = Sequence.Update(quantum);
        return new MotionUpdate(_fired, offset);
    }
}

/// <summary>
/// Animation playback for one object: a <see cref="MotionTableManager"/> (state machine) driving an
/// <see cref="AnimSequence"/> (retail <c>CPartArray</c>'s motion-table manager + <c>CSequence</c>).
/// Time is injected: the caller advances it with <see cref="Update"/>; nothing reads a wall clock.
/// </summary>
/// <remarks>
/// <para>Two ways to drive it. The motion interpreter (<see cref="Motion.MotionInterpreter"/>, retail
/// <c>CMotionInterp</c>) calls the part-array operations <see cref="DoInterpretedMotion"/>,
/// <see cref="StopInterpretedMotion"/>, <see cref="StopCompletelyInternal"/>, <see cref="InitializeMotionTables"/>,
/// <see cref="CheckForCompletedMotions"/> and <see cref="RemoveLinkAnimations"/>, as retail <c>CMotionInterp</c> calls
/// <c>CPhysicsObj</c> and <c>CPartArray</c>; that is how server objects and the player move. The spell viewer's
/// shortcut <see cref="ApplyInterpretedMotion"/> plays a stance and a forward command without an interpreter.</para>
/// <para>Animation-done hooks are fed back into <see cref="MotionTableManager.AnimationDone"/> as retail
/// <c>CPhysicsObj::Hook_AnimDone</c> does; each completed motion raises <see cref="MotionTableManager.MotionDone"/>
/// (retail <c>CPhysicsObj::MotionDone</c>), which the interpreter listens to.</para>
/// <para>Drive the manager only through this class: it counts the manager's pending motions
/// (<see cref="PendingAnimationCount"/>), so that <see cref="RemoveLinkAnimations"/> can complete them all without
/// enumerating the manager's queue.</para>
/// </remarks>
public sealed class MotionPlayer : IPoseDriver
{
    private readonly List<AnimHook> _fired = [];
    private int _pendingAnimations;

    public MotionPlayer(MotionTableData table, IAnimationSource animations)
    {
        Sequence = new AnimSequence(animations) { HookSink = _fired.Add };
        Manager = new MotionTableManager(table);
        Manager.MotionDone += (_, _) => _pendingAnimations--;
        InitializeMotionTables();
        Manager.CheckForCompletedMotions();
    }

    public AnimSequence Sequence { get; }

    public MotionTableManager Manager { get; }

    public MotionTableData Table => Manager.Table;

    public MotionState State => Manager.State;

    /// <summary>
    /// The number of motions queued in the manager whose animations have not finished (retail
    /// <c>MotionTableManager::pending_animations</c>).
    /// </summary>
    public int PendingAnimationCount => _pendingAnimations;

    /// <summary>
    /// The motions queued in the manager since this player was made (<see cref="InitializeMotionTables"/> and every
    /// movement it accepted). Each one allocates its pending entry in <see cref="MotionTableManager"/>, whose queue is
    /// not yet allocation-free, so the allocation tests of the motion paths measure their bytes per queued motion.
    /// </summary>
    public long MotionsQueued { get; private set; }

    /// <summary>Pose sampling; retail floor by default.</summary>
    public PoseSampling Sampling { get; set; } = PoseSampling.RetailFloor;

    /// <summary>Per-part frames of the current pose (relative to the object frame), or null if nothing is posed.</summary>
    public IReadOnlyList<AcFrame>? Pose => Sequence.SamplePose(Sampling);

    /// <summary>
    /// Retail <c>CPartArray::DoInterpretedMotion</c> (11.4186 0x00518750, 11.6096 0x00519280) →
    /// <c>MotionTableManager::PerformMovement</c> type 2, without the completion check. False when the motion table has
    /// no data for the motion (retail error 0x43).
    /// </summary>
    public bool DoInterpretedMotion(uint motion, float speed) =>
        Perform(MotionTableManager.MovementType.InterpretedCommand, motion, speed);

    /// <summary>
    /// Retail <c>CPartArray::StopInterpretedMotion</c> (11.4186 0x005187F0, 11.6096 0x00519320) → <c>PerformMovement</c>
    /// type 4, without the completion check. False when there is nothing to stop (retail error 0x43).
    /// </summary>
    public bool StopInterpretedMotion(uint motion, float speed) =>
        Perform(MotionTableManager.MovementType.StopInterpretedCommand, motion, speed);

    /// <summary>
    /// Retail <c>CPartArray::StopCompletelyInternal</c> (11.4186 0x00518890, 11.6096 0x005193C0) → <c>PerformMovement</c>
    /// type 5.
    /// </summary>
    public void StopCompletelyInternal() => Perform(MotionTableManager.MovementType.StopCompletely, 0, 1f);

    /// <summary>
    /// Retail <c>CPartArray::InitializeMotionTables</c> (11.4186 0x00517D10; TODO(avatars): 11.6096) →
    /// <c>MotionTableManager::initialize_state</c>: the table's default style and substate, queued as a Ready motion.
    /// </summary>
    public void InitializeMotionTables()
    {
        Manager.InitializeState(Sequence);
        _pendingAnimations++;
        MotionsQueued++;
    }

    /// <summary>
    /// Retail <c>CPartArray::CheckForCompletedMotions</c> (11.4186 0x00517D50; TODO(avatars): 11.6096): completes the
    /// queued motions that have no animations.
    /// </summary>
    public void CheckForCompletedMotions() => Manager.CheckForCompletedMotions();

    /// <summary>
    /// Retail <c>CPartArray::HandleMovement</c> (11.4186 0x00517D60, 11.6096 0x00518890) →
    /// <c>MotionTableManager::UseTime</c>, which every object update makes after the movement manager's
    /// (<c>CPhysicsObj::UpdateObjectInternal</c>): the completion check, so a queued motion without animations (a turn
    /// modifier, a stop, a style re-issued, <c>enter_default_state</c>'s Ready) completes in the update that follows it
    /// rather than at the next animation-done hook.
    /// </summary>
    /// <remarks>
    /// <c>MotionTableManager::UseTime</c>'s body is not in the 11.4186 per-file decompile, and the pinned 11.6096 table
    /// lists its name only as the second candidate of <c>RemoveLinkAnimations</c> (0x0051CAD0), which cannot be its body
    /// (links would never play). It is re-expressed as <c>CheckForCompletedMotions</c>, as ACE's
    /// <c>MotionTableManager.UseTime</c> is. TODO(avatars): 11.6096 address of <c>MotionTableManager::UseTime</c>, from
    /// <c>CPartArray::HandleMovement</c>'s call.
    /// </remarks>
    public void HandleMovement() => Manager.CheckForCompletedMotions();

    /// <summary>
    /// Retail <c>CPhysicsObj::RemoveLinkAnimations</c> (11.4186 0x0050FE20, 11.6096 0x005108F0) →
    /// <c>CPartArray::RemoveLinkAnimations</c> (0x00517D70 / 0x005188A0) → <c>MotionTableManager::RemoveLinkAnimations</c>
    /// (0x0051BDD0 / 0x0051CAD0; the decompile prints both under the folded name <c>HandleEnterWorld</c>): drops every
    /// link animation before the first cycle, then completes every pending motion as a failure (<c>AnimationDone(0)</c>
    /// until the queue is empty).
    /// </summary>
    public void RemoveLinkAnimations()
    {
        Sequence.RemoveAllLinkAnimations();

        // Each AnimationDone advances the manager's animation counter by one, so the loop ends after at most the queue's
        // total animation count; the guard turns a miscount into a loud failure instead of a hang.
        for (var guard = 0; _pendingAnimations > 0; guard++)
        {
            if (guard > 1_000_000)
            {
                throw new InvalidOperationException(
                    $"MotionTable 0x{Table.Id:X8}: {_pendingAnimations} pending motions never completed; was the manager driven outside MotionPlayer?");
            }

            Manager.AnimationDone(false);
        }
    }

    /// <summary>
    /// Retail <c>CPartArray::DoInterpretedMotion</c> → <c>PerformMovement(type 2)</c>, followed by
    /// <c>CheckForCompletedMotions</c> (as <c>CMotionInterp::PerformMovement</c> does).
    /// </summary>
    public bool DoMotion(uint motion, float speed = 1f)
    {
        var ok = DoInterpretedMotion(motion, speed);
        Manager.CheckForCompletedMotions();
        return ok;
    }

    /// <summary>
    /// Retail <c>CPartArray::StopInterpretedMotion</c> → <c>PerformMovement(type 4)</c>, followed by
    /// <c>CheckForCompletedMotions</c>.
    /// </summary>
    public bool StopMotion(uint motion, float speed = 1f)
    {
        var ok = StopInterpretedMotion(motion, speed);
        Manager.CheckForCompletedMotions();
        return ok;
    }

    /// <summary>Retail <c>StopCompletely</c> → <c>PerformMovement(type 5)</c>, followed by <c>CheckForCompletedMotions</c>.</summary>
    public void StopCompletely()
    {
        StopCompletelyInternal();
        Manager.CheckForCompletedMotions();
    }

    /// <summary>
    /// The spell viewer's shortcut (M1), not a motion interpreter: plays a stance and a forward command the way retail
    /// <c>CMotionInterp::apply_interpreted_movement</c> does for an object in contact with the ground and not in a
    /// standing long jump: <c>DoInterpretedMotion(style)</c>, <c>DoInterpretedMotion(forward, speed)</c>,
    /// <c>StopInterpretedMotion(SideStepRight)</c>, <c>StopInterpretedMotion(TurnRight)</c>. Server objects and the
    /// player go through <see cref="Motion.MotionInterpreter"/>, which also handles sidesteps, turns, actions, contact
    /// and the jump rules.
    /// </summary>
    /// <remarks>
    /// <c>move_to_interpreted_state</c> does not call <c>CheckForCompletedMotions</c> (only
    /// <c>CMotionInterp::PerformMovement</c> does), so zero-animation motions complete on the next animation-done hook.
    /// The style is applied with the interpreted state's default speed of 1.0.
    /// </remarks>
    public void ApplyInterpretedMotion(uint style, uint forwardCommand, float forwardSpeed)
    {
        DoInterpretedMotion(style, 1f);
        DoInterpretedMotion(forwardCommand, forwardSpeed);
        StopInterpretedMotion(MotionCommands.SideStepRight, 1f);
        StopInterpretedMotion(MotionCommands.TurnRight, 1f);
    }

    /// <summary>
    /// Advances the sequence by <paramref name="quantum"/> seconds (retail <c>CPartArray::Update</c>), then runs the
    /// animation-done bookkeeping of the fired hooks (retail <c>process_hooks</c> → <c>Hook_AnimDone</c>). The hooks are
    /// returned in the player's reused list (valid until the next call), not a copy, so the player adds no allocation.
    /// Callers that want retail stepping should keep quanta ≤ 0.2 s: <see cref="Effects.ObjectClock"/> slices a frame's
    /// elapsed time the way retail <c>CPhysicsObj::update_object</c> does.
    /// </summary>
    public MotionUpdate Update(double quantum)
    {
        _fired.Clear();
        var offset = Sequence.Update(quantum);

        // AnimationDone only touches the motion state, never the sequence, so no hook is added while this loop runs.
        for (var i = 0; i < _fired.Count; i++)
        {
            if (_fired[i] is AnimationDoneHook)
            {
                Manager.AnimationDone(true);
            }
        }

        return new MotionUpdate(_fired, offset);
    }

    private bool Perform(MotionTableManager.MovementType type, uint motion, float speed)
    {
        // MotionTableManager::PerformMovement queues exactly one motion when it succeeds (type 5 always succeeds).
        var ok = Manager.PerformMovement(type, motion, speed, Sequence);
        if (ok)
        {
            _pendingAnimations++;
            MotionsQueued++;
        }

        return ok;
    }
}
