using Lifestone.Data.Animation;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The tunnel's spin: the UIASSET portal-space animation on the tunnel object's part array, looping frames 1 to 119 at
/// 40 frames per second (m2-log-in.md §4.4). A pose driver: <see cref="TunnelObject"/> steps it with retail's
/// creature-mode slicing (<see cref="CreatureModeClock"/>) and queues its hooks (the frame-2 hum) on the tunnel's effect
/// object.
/// </summary>
/// <remarks>
/// <para>Re-expresses, by function name:</para>
/// <list type="bullet">
/// <item><see cref="Restart"/>: <c>CPhysicsObj::set_sequence_animation(anim, interrupt 1, start frame 1, 40.0)</c>
/// (11.4186 0x0050F6F0, 11.6096 0x005101C0). The interrupt clears the sequence (<c>CSequence::clear</c>: its animations,
/// its physics and its placement frame), then appends <c>AnimData(anim, low 1, high −1, 40)</c>; the node clamps the
/// high frame −1 to the animation's last frame, 119 (<c>AnimSequenceNode::set_animation_id</c>).</item>
/// <item><see cref="Stop"/>: <c>CPhysicsObj::clear_sequence_anims</c> (11.4186 0x0050F760, 11.6096 0x00510230), which
/// clears the animations only, so the frame number returns to 0.</item>
/// <item><see cref="CurrentFrame"/>: <c>CPhysicsObj::get_curr_frame_number</c> (11.4186 0x0050F780, 11.6096 0x00510250),
/// ⌊frame number⌋.</item>
/// </list>
/// <para><b>The loop.</b> The node is the sequence's only, cyclic node. When the frame number passes the high frame,
/// <c>CSequence::update_internal</c> carries the overshoot back to the start frame, so frames run 1, 2, …, 119, 1, … with
/// a period of 119 / 40 = 2.975 s; frame 0 is never shown again. Frame 0 is frame 119 turned on by one more 1.5° step (the
/// halves swapped), so the step from 119 back to 1 turns 3°, once per loop: that is retail's start frame of 1, kept. The
/// hum on frame 2 fires once per loop, as the frame number crosses from 2 to 3.</para>
/// <para><b>Allocation.</b> <see cref="Update"/> returns the hooks in a list it reuses, so a steady step allocates
/// nothing; <see cref="Restart"/> allocates the sequence node (once per tunnel showing).</para>
/// </remarks>
public sealed class TunnelSpin : IPoseDriver
{
    /// <summary>The loop's length in seconds: frames 1 to 119 at 40 frames per second.</summary>
    public const double RetailLoopSeconds = (RetailLastFrame - PortalSpaceScene.SpinStartFrame + 1) / (double)PortalSpaceScene.SpinFramerate;

    /// <summary>The last frame of the UIASSET animation (120 frames; PS2 checks it on the real dats).</summary>
    public const int RetailLastFrame = 119;

    private readonly List<AnimHook> _fired = [];

    /// <summary>Creates a stopped spin of animation <paramref name="animationId"/>.</summary>
    /// <param name="animations">Where the animation comes from (normally <see cref="DatAnimationSource"/>).</param>
    /// <param name="animationId">The UIASSET portal-space animation (<see cref="PortalSpaceAssets.TunnelAnimationId"/>).</param>
    /// <exception cref="InvalidDataException">The animation does not exist.</exception>
    public TunnelSpin(IAnimationSource animations, uint animationId)
    {
        ArgumentNullException.ThrowIfNull(animations);
        var animation = animations.GetAnimation(animationId)
            ?? throw new InvalidDataException($"The portal-space animation 0x{animationId:X8} does not exist, so the tunnel cannot spin.");
        AnimationId = animationId;
        FrameCount = animation.NumFrames;
        Sequence = new AnimSequence(animations) { HookSink = _fired.Add };
    }

    /// <summary>The animation's id.</summary>
    public uint AnimationId { get; }

    /// <summary>The animation's frame count (120 for the UIASSET animation).</summary>
    public int FrameCount { get; }

    /// <summary>The sequence (retail <c>CPartArray::sequence</c>).</summary>
    public AnimSequence Sequence { get; }

    /// <summary>The spin is running (the sequence has its node).</summary>
    public bool IsSpinning => Sequence.HasAnims;

    /// <summary>
    /// The current frame (<c>CPhysicsObj::get_curr_frame_number</c>): 1 to 119 while spinning, 0 when stopped.
    /// </summary>
    public int CurrentFrame => Sequence.CurrentFrameIndex;

    /// <summary>The sequence's frame number (retail <c>frame_number</c>, a double).</summary>
    public double FrameNumber => Sequence.FrameNumber;

    /// <summary>The parts' frames of the current pose (retail floor sampling), relative to the object; null when stopped.</summary>
    public IReadOnlyList<AcFrame>? Pose => Sequence.SamplePose(PoseSampling.RetailFloor);

    /// <summary>Starts the spin from frame 1, interrupting anything else (<c>set_sequence_animation</c>).</summary>
    public void Restart()
    {
        Sequence.Clear();
        Sequence.AppendAnimation(new AnimDataRef(AnimationId, PortalSpaceScene.SpinStartFrame, -1, PortalSpaceScene.SpinFramerate));
    }

    /// <summary>Stops the spin (<c>clear_sequence_anims</c>): the frame number returns to 0.</summary>
    public void Stop() => Sequence.ClearAnimations();

    /// <summary>
    /// Advances the spin by <paramref name="quantum"/> seconds (retail <c>CPartArray::Update</c>). The hooks fired are
    /// returned in the spin's reused list, valid until the next call.
    /// </summary>
    public MotionUpdate Update(double quantum)
    {
        _fired.Clear();
        var offset = Sequence.Update(quantum);
        return new MotionUpdate(_fired, offset);
    }
}
