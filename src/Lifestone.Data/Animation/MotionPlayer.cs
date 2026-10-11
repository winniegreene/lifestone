namespace Lifestone.Data.Animation;

/// <summary>Result of one <see cref="IPoseDriver.Update"/> quantum.</summary>
/// <param name="Hooks">Hooks fired during the quantum, in retail order (the caller hands effect hooks to an effect object).</param>
/// <param name="RootMotion">Root-motion offset of the quantum (retail <c>offset_frame</c>, before the object-scale/contact rule).</param>
public readonly record struct MotionUpdate(IReadOnlyList<AnimHook> Hooks, AcFrame RootMotion);

/// <summary>Something that animates an object's parts (a motion table, or a Setup's default animation).</summary>
public interface IPoseDriver
{
    /// <summary>Advances by <paramref name="quantum"/> seconds.</summary>
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

    public MotionUpdate Update(double quantum)
    {
        _fired.Clear();
        var offset = Sequence.Update(quantum);
        return new MotionUpdate(_fired.ToArray(), offset);
    }
}

/// <summary>
/// Animation playback for one object: a <see cref="MotionTableManager"/> (state machine) driving an
/// <see cref="AnimSequence"/> (retail <c>CPartArray</c>'s motion-table manager + <c>CSequence</c>).
/// Time is injected: the caller advances it with <see cref="Update"/>; nothing reads a wall clock.
/// </summary>
/// <remarks>
/// Driving it like the client does when the server sends a motion (ACE <c>EnqueueBroadcastMotion</c> →
/// retail <c>CMotionInterp::move_to_interpreted_state</c>): call <see cref="ApplyInterpretedMotion"/> with the stance
/// and forward command. Animation-done hooks are fed back into <see cref="MotionTableManager.AnimationDone"/> as retail
/// <c>CPhysicsObj::Hook_AnimDone</c> does.
/// </remarks>
public sealed class MotionPlayer : IPoseDriver
{
    private readonly List<AnimHook> _fired = [];

    public MotionPlayer(MotionTableData table, IAnimationSource animations)
    {
        Sequence = new AnimSequence(animations) { HookSink = _fired.Add };
        Manager = new MotionTableManager(table);
        Manager.InitializeState(Sequence);
        Manager.CheckForCompletedMotions();
    }

    public AnimSequence Sequence { get; }

    public MotionTableManager Manager { get; }

    public MotionTableData Table => Manager.Table;

    public MotionState State => Manager.State;

    /// <summary>Pose sampling; retail floor by default.</summary>
    public PoseSampling Sampling { get; set; } = PoseSampling.RetailFloor;

    /// <summary>Per-part frames of the current pose (relative to the object frame), or null if nothing is posed.</summary>
    public IReadOnlyList<AcFrame>? Pose => Sequence.SamplePose(Sampling);

    /// <summary>
    /// Retail <c>CPartArray::DoInterpretedMotion</c> → <c>PerformMovement(type 2)</c>, followed by
    /// <c>CheckForCompletedMotions</c> (as <c>CMotionInterp::PerformMovement</c> does).
    /// </summary>
    public bool DoMotion(uint motion, float speed = 1f)
    {
        var ok = Manager.PerformMovement(MotionTableManager.MovementType.InterpretedCommand, motion, speed, Sequence);
        Manager.CheckForCompletedMotions();
        return ok;
    }

    /// <summary>Retail <c>CPartArray::StopInterpretedMotion</c> → <c>PerformMovement(type 4)</c>.</summary>
    public bool StopMotion(uint motion, float speed = 1f)
    {
        var ok = Manager.PerformMovement(MotionTableManager.MovementType.StopInterpretedCommand, motion, speed, Sequence);
        Manager.CheckForCompletedMotions();
        return ok;
    }

    /// <summary>Retail <c>StopCompletely</c> → <c>PerformMovement(type 5)</c>.</summary>
    public void StopCompletely()
    {
        Manager.PerformMovement(MotionTableManager.MovementType.StopCompletely, 0, 1f, Sequence);
        Manager.CheckForCompletedMotions();
    }

    /// <summary>
    /// What the client does for a server motion update with only a stance and a forward command (no sidestep/turn):
    /// retail <c>CMotionInterp::apply_interpreted_movement</c> for an object in contact with the ground and not in a
    /// standing long jump: <c>DoInterpretedMotion(style)</c>, <c>DoInterpretedMotion(forward, speed)</c>,
    /// <c>StopInterpretedMotion(SideStepRight)</c>, <c>StopInterpretedMotion(TurnRight)</c>.
    /// </summary>
    /// <remarks>
    /// <c>move_to_interpreted_state</c> does not call <c>CheckForCompletedMotions</c> (only
    /// <c>CMotionInterp::PerformMovement</c> does), so zero-animation motions complete on the next animation-done hook.
    /// The style is applied with the interpreted state's default speed of 1.0.
    /// </remarks>
    public void ApplyInterpretedMotion(uint style, uint forwardCommand, float forwardSpeed)
    {
        Manager.PerformMovement(MotionTableManager.MovementType.InterpretedCommand, style, 1f, Sequence);
        Manager.PerformMovement(MotionTableManager.MovementType.InterpretedCommand, forwardCommand, forwardSpeed, Sequence);
        Manager.PerformMovement(MotionTableManager.MovementType.StopInterpretedCommand, MotionCommands.SideStepRight, 1f, Sequence);
        Manager.PerformMovement(MotionTableManager.MovementType.StopInterpretedCommand, MotionCommands.TurnRight, 1f, Sequence);
    }

    /// <summary>
    /// Advances the sequence by <paramref name="quantum"/> seconds (retail <c>CPartArray::Update</c>), then runs the
    /// animation-done bookkeeping of the fired hooks (retail <c>process_hooks</c> → <c>Hook_AnimDone</c>).
    /// Callers that want retail stepping should keep quanta ≤ 0.2 s (see <c>Lifestone.Data.Effects.ObjectClock</c>).
    /// </summary>
    public MotionUpdate Update(double quantum)
    {
        _fired.Clear();
        var offset = Sequence.Update(quantum);
        var hooks = _fired.ToArray();
        foreach (var hook in hooks)
        {
            if (hook is AnimationDoneHook)
            {
                Manager.AnimationDone(true);
            }
        }

        return new MotionUpdate(hooks, offset);
    }
}
