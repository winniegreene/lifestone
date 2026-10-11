using System.Numerics;

namespace Lifestone.Data.Animation;

/// <summary>Source of decoded animations for <see cref="AnimSequence"/> (normally <see cref="DatAnimationSource"/>).</summary>
public interface IAnimationSource
{
    /// <summary>The animation, or null when the id does not exist (retail then drops the node: <c>has_anim</c> is false).</summary>
    AnimationData? GetAnimation(uint animId);
}

/// <summary>How the displayed pose is sampled between keyframes.</summary>
public enum PoseSampling
{
    /// <summary>
    /// Retail: <c>CSequence::get_curr_animframe</c> shows <c>part_frames[floor(frame_number)]</c>, no interpolation
    /// (acclient 11.4186 PORTAL/canim/CSequence.c). This is the default.
    /// </summary>
    RetailFloor,

    /// <summary>
    /// Non-retail option: lerp origins / slerp orientations towards the next keyframe in the playback direction,
    /// within the current node (OpenAC <c>AnimationSequencer</c> interpolates; documented deviation).
    /// </summary>
    Interpolated,
}

/// <summary>
/// One queued animation of a <see cref="AnimSequence"/> (retail <c>AnimSequenceNode</c>). Frame bounds are clamped
/// to the animation like retail <c>AnimSequenceNode::set_animation_id</c>.
/// </summary>
public sealed class AnimSequenceNode
{
    internal AnimSequenceNode(AnimDataRef data, AnimationData? anim)
    {
        AnimId = data.AnimId;
        Framerate = data.Framerate;
        LowFrame = data.LowFrame;
        HighFrame = data.HighFrame;
        Anim = anim;
        if (anim is null)
        {
            return;
        }

        // AnimSequenceNode::set_animation_id: clamp the requested range to the animation.
        var n = anim.NumFrames;
        if (HighFrame < 0)
        {
            HighFrame = n - 1;
        }

        if (LowFrame >= n)
        {
            LowFrame = n - 1;
        }

        if (HighFrame >= n)
        {
            HighFrame = n - 1;
        }

        if (LowFrame > HighFrame)
        {
            HighFrame = LowFrame;
        }
    }

    public uint AnimId { get; }

    public AnimationData? Anim { get; }

    public float Framerate { get; private set; }

    public int LowFrame { get; private set; }

    public int HighFrame { get; private set; }

    /// <summary>Retail <c>get_starting_frame</c>: <c>high + 1 − ε</c> when playing backwards, else <c>low</c>.</summary>
    public double StartingFrame => Framerate < 0f ? HighFrame + 1 - SequenceConstants.Epsilon : LowFrame;

    /// <summary>Retail <c>get_ending_frame</c>.</summary>
    public double EndingFrame => Framerate < 0f ? LowFrame : HighFrame + 1 - SequenceConstants.Epsilon;

    /// <summary>Retail <c>multiply_framerate</c>: a negative multiplier swaps the frame bounds.</summary>
    internal void MultiplyFramerate(float multiplier)
    {
        if (multiplier < 0f)
        {
            (LowFrame, HighFrame) = (HighFrame, LowFrame);
        }

        Framerate *= multiplier;
    }

    /// <summary>Retail <c>get_part_frame(int)</c>: null when out of range.</summary>
    public AnimationPartFrame? GetPartFrame(int frame) =>
        Anim is not null && frame >= 0 && frame < Anim.NumFrames ? Anim.PartFrames[frame] : null;

    /// <summary>Retail <c>get_pos_frame(int)</c>: null when out of range or when the animation has no pos frames.</summary>
    public AcFrame? GetPosFrame(int frame) =>
        Anim?.PosFrames is { } pos && frame >= 0 && frame < Anim.NumFrames ? pos[frame] : null;
}

internal static class SequenceConstants
{
    /// <summary>Retail <c>F_EPSILON</c> as the double used by <c>CSequence</c> (0.00019999999).</summary>
    public const double Epsilon = 0.00019999999;
}

/// <summary>
/// A port of retail <c>CSequence</c> (acclient 11.4186 PORTAL/canim/CSequence.c; cross-checked with ACE
/// <c>Source/ACE.Server/Physics/Animation/Sequence.cs</c>, AGPL-3.0 @95ef426d): a list of animation nodes where the
/// nodes before <see cref="FirstCyclic"/> are one-shot links and the rest loop. Frame numbers and quanta are doubles
/// (retail <c>long double</c>), frame data single precision.
/// </summary>
/// <remarks>
/// Differences from ACE, all following retail: frame numbers are double (ACE float); hook direction filtering uses the
/// retail rule <c>dir_ == 0 || dir_ == dir</c>; the animation-done hook is queued exactly as
/// <c>update_internal</c> does (when the list head is not the first cyclic node).
/// </remarks>
public sealed class AnimSequence(IAnimationSource animations)
{
    private readonly IAnimationSource _animations = animations;
    private readonly LinkedList<AnimSequenceNode> _list = new();

    /// <summary>Receives hooks as they fire (retail <c>hook_obj-&gt;add_anim_hook</c>). Null = hooks are not collected.</summary>
    public Action<AnimHook>? HookSink { get; set; }

    public LinkedListNode<AnimSequenceNode>? FirstCyclic { get; private set; }

    public LinkedListNode<AnimSequenceNode>? CurrentNode { get; private set; }

    /// <summary>Retail <c>frame_number</c> (double).</summary>
    public double FrameNumber { get; private set; }

    public Vector3 Velocity { get; private set; }

    public Vector3 Omega { get; private set; }

    /// <summary>Pose shown when no animation is queued (retail <c>placement_frame</c>).</summary>
    public AnimationPartFrame? PlacementFrame { get; private set; }

    public uint PlacementFrameId { get; private set; }

    public IEnumerable<AnimSequenceNode> Nodes => _list;

    public int Count => _list.Count;

    /// <summary>Retail <c>has_anims</c>.</summary>
    public bool HasAnims => _list.First is not null;

    public void SetPlacementFrame(AnimationPartFrame? frame, uint id)
    {
        PlacementFrame = frame;
        PlacementFrameId = id;
    }

    public void SetVelocity(Vector3 v) => Velocity = v;

    public void SetOmega(Vector3 o) => Omega = o;

    /// <summary>Retail <c>combine_physics</c>.</summary>
    public void CombinePhysics(Vector3 v, Vector3 o)
    {
        Velocity += v;
        Omega += o;
    }

    /// <summary>Retail <c>subtract_physics</c>.</summary>
    public void SubtractPhysics(Vector3 v, Vector3 o)
    {
        Velocity -= v;
        Omega -= o;
    }

    /// <summary>Retail <c>clear_physics</c>.</summary>
    public void ClearPhysics()
    {
        Velocity = Vector3.Zero;
        Omega = Vector3.Zero;
    }

    /// <summary>Retail <c>clear</c>.</summary>
    public void Clear()
    {
        ClearAnimations();
        ClearPhysics();
        PlacementFrame = null;
        PlacementFrameId = 0;
    }

    /// <summary>Retail <c>clear_animations</c>.</summary>
    public void ClearAnimations()
    {
        _list.Clear();
        FirstCyclic = null;
        FrameNumber = 0;
        CurrentNode = null;
    }

    /// <summary>Retail <c>append_animation</c>: nodes whose animation is missing are dropped; the new tail becomes the first cyclic node.</summary>
    public void AppendAnimation(AnimDataRef data)
    {
        var node = new AnimSequenceNode(data, data.AnimId != 0 ? _animations.GetAnimation(data.AnimId) : null);
        if (node.Anim is null)
        {
            return;
        }

        FirstCyclic = _list.AddLast(node);
        if (CurrentNode is null)
        {
            CurrentNode = _list.First;
            FrameNumber = CurrentNode!.Value.StartingFrame;
        }
    }

    /// <summary>Retail <c>remove_cyclic_anims</c>.</summary>
    public void RemoveCyclicAnims()
    {
        var node = FirstCyclic;
        while (node is not null)
        {
            if (CurrentNode == node)
            {
                CurrentNode = node.Previous;
                FrameNumber = CurrentNode is not null ? CurrentNode.Value.EndingFrame : 0.0;
            }

            var next = node.Next;
            _list.Remove(node);
            node = next;
        }

        FirstCyclic = _list.Last;
    }

    /// <summary>Retail <c>remove_link_animations(n)</c>: removes up to <paramref name="count"/> links just before the first cyclic node.</summary>
    public void RemoveLinkAnimations(uint count)
    {
        for (uint i = 0; i < count; i++)
        {
            var prev = FirstCyclic?.Previous;
            if (prev is null)
            {
                break;
            }

            if (prev == CurrentNode)
            {
                CurrentNode = FirstCyclic;
                if (CurrentNode is not null)
                {
                    FrameNumber = CurrentNode.Value.StartingFrame;
                }
            }

            _list.Remove(prev);
        }
    }

    /// <summary>Retail <c>remove_all_link_animations</c>.</summary>
    public void RemoveAllLinkAnimations()
    {
        while (FirstCyclic?.Previous is { } prev)
        {
            if (prev == CurrentNode)
            {
                CurrentNode = FirstCyclic;
                FrameNumber = CurrentNode.Value.StartingFrame;
            }

            _list.Remove(prev);
        }
    }

    /// <summary>Retail <c>multiply_cyclic_animation_framerate</c>.</summary>
    public void MultiplyCyclicAnimationFramerate(float multiplier)
    {
        for (var node = FirstCyclic; node is not null; node = node.Next)
        {
            node.Value.MultiplyFramerate(multiplier);
        }
    }

    /// <summary>Retail <c>get_curr_frame_number</c>: <c>floor(frame_number)</c>.</summary>
    public int CurrentFrameIndex => (int)Math.Floor(FrameNumber);

    /// <summary>Retail <c>get_curr_animframe</c>: the keyframe at <c>floor(frame_number)</c>, or the placement frame.</summary>
    public AnimationPartFrame? CurrentAnimFrame =>
        CurrentNode is null ? PlacementFrame : CurrentNode.Value.GetPartFrame(CurrentFrameIndex);

    /// <summary>
    /// Per-part frames of the current pose. <see cref="PoseSampling.RetailFloor"/> returns exactly
    /// <see cref="CurrentAnimFrame"/>; <see cref="PoseSampling.Interpolated"/> blends towards the next keyframe of the
    /// current node in the playback direction (no blending across nodes).
    /// </summary>
    public IReadOnlyList<AcFrame>? SamplePose(PoseSampling sampling)
    {
        var current = CurrentAnimFrame;
        if (current is null || sampling == PoseSampling.RetailFloor || CurrentNode is null)
        {
            return current?.Frames;
        }

        var node = CurrentNode.Value;
        var index = CurrentFrameIndex;
        var frac = (float)(FrameNumber - index);
        int nextIndex;
        float t;
        if (node.Framerate >= 0f)
        {
            nextIndex = index + 1;
            t = frac;
        }
        else
        {
            nextIndex = index - 1;
            t = 1f - frac;
        }

        var lo = Math.Min(node.LowFrame, node.HighFrame);
        var hi = Math.Max(node.LowFrame, node.HighFrame);
        if (nextIndex < lo || nextIndex > hi || node.GetPartFrame(nextIndex) is not { } next)
        {
            return current.Frames;
        }

        var count = Math.Min(current.Frames.Count, next.Frames.Count);
        var result = new AcFrame[count];
        for (var i = 0; i < count; i++)
        {
            var a = current.Frames[i];
            var b = next.Frames[i];
            result[i] = new AcFrame(Vector3.Lerp(a.Origin, b.Origin, t), Quaternion.Slerp(a.Orientation, b.Orientation, t));
        }

        return result;
    }

    /// <summary>
    /// Retail <c>CSequence::update(quantum, retval)</c>: advances by <paramref name="quantum"/> seconds, firing hooks,
    /// and returns the root-motion offset accumulated during this quantum (identity when no root motion), i.e. the
    /// retail <c>retval</c> frame that <c>CPhysicsObj::UpdatePositionInternal</c> combines with the object position.
    /// </summary>
    public AcFrame Update(double quantum)
    {
        var offset = AcFrame.Identity;
        if (_list.First is not null)
        {
            var node = CurrentNode!;
            var frameNumber = FrameNumber;
            UpdateInternal(quantum, ref node, ref frameNumber, ref offset);
            CurrentNode = node;
            FrameNumber = frameNumber;
            Apricot();
        }
        else
        {
            offset = ApplyPhysics(offset, quantum, quantum);
        }

        return offset;
    }

    /// <summary>Retail <c>apply_physics</c>: moves the frame by velocity·|q| and rotates it (local) by omega·|q|, signed by <paramref name="sign"/>.</summary>
    private AcFrame ApplyPhysics(AcFrame frame, double quantum, double sign)
    {
        var q = (float)(sign >= 0.0 ? Math.Abs(quantum) : -Math.Abs(quantum));
        frame = frame with { Origin = frame.Origin + Velocity * q };
        return frame.Rotate(Omega * q);
    }

    /// <summary>Retail <c>apricot</c>: drops finished link nodes in front of the current node.</summary>
    private void Apricot()
    {
        var node = _list.First;
        while (node is not null && node != CurrentNode && node != FirstCyclic)
        {
            _list.Remove(node);
            node = _list.First;
        }
    }

    private void ExecuteHooks(AnimationPartFrame? frame, AnimHookDirection dir)
    {
        if (HookSink is null || frame is null)
        {
            return;
        }

        foreach (var hook in frame.Hooks)
        {
            if (hook.Direction == AnimHookDirection.Both || hook.Direction == dir)
            {
                HookSink(hook);
            }
        }
    }

    /// <summary>Retail <c>CSequence::update_internal</c> (written as a loop, as retail does).</summary>
    private void UpdateInternal(double quantum, ref LinkedListNode<AnimSequenceNode> node, ref double frameNumber, ref AcFrame offset)
    {
        while (true)
        {
            var anim = node.Value;
            double framerate = anim.Framerate;
            var frameQuantum = framerate * quantum;
            var lastFrame = (int)Math.Floor(frameNumber);
            var timeLeft = 0.0;
            var advance = false;
            frameNumber += frameQuantum;

            if (frameQuantum > 0.0)
            {
                if (anim.HighFrame < Math.Floor(frameNumber))
                {
                    var over = frameNumber - anim.HighFrame - 1.0;
                    if (over < 0.0)
                    {
                        over = 0.0;
                    }

                    timeLeft = Math.Abs(framerate) <= SequenceConstants.Epsilon ? 0.0 : over / framerate;
                    advance = true;
                    frameNumber = anim.HighFrame;
                }

                while (Math.Floor(frameNumber) > lastFrame)
                {
                    if (anim.Anim?.PosFrames is not null && anim.GetPosFrame(lastFrame) is { } pos)
                    {
                        offset = AcFrame.Combine(offset, pos);
                    }

                    if (Math.Abs(framerate) > SequenceConstants.Epsilon)
                    {
                        offset = ApplyPhysics(offset, 1.0 / framerate, quantum);
                    }

                    ExecuteHooks(anim.GetPartFrame(lastFrame), AnimHookDirection.Forward);
                    lastFrame++;
                }
            }
            else if (frameQuantum < 0.0)
            {
                if (anim.LowFrame > Math.Floor(frameNumber))
                {
                    var under = frameNumber - anim.LowFrame;
                    if (under > 0.0)
                    {
                        under = 0.0;
                    }

                    timeLeft = Math.Abs(framerate) <= SequenceConstants.Epsilon ? 0.0 : under / framerate;
                    advance = true;
                    frameNumber = anim.LowFrame;
                }

                while (Math.Floor(frameNumber) < lastFrame)
                {
                    if (anim.Anim?.PosFrames is not null && anim.GetPosFrame(lastFrame) is { } pos)
                    {
                        offset = AcFrame.Subtract1(offset, pos);
                    }

                    if (Math.Abs(framerate) > SequenceConstants.Epsilon)
                    {
                        offset = ApplyPhysics(offset, 1.0 / framerate, quantum);
                    }

                    ExecuteHooks(anim.GetPartFrame(lastFrame), AnimHookDirection.Backward);
                    lastFrame--;
                }
            }
            else
            {
                // frame_quantum == 0: no frame advance, just apply the object physics for the quantum.
                if (Math.Abs(quantum) > SequenceConstants.Epsilon)
                {
                    offset = ApplyPhysics(offset, quantum, quantum);
                }

                return;
            }

            if (!advance)
            {
                return;
            }

            // A non-cyclic animation finished: queue the global anim_done_hook.
            if (HookSink is not null && _list.First != FirstCyclic)
            {
                HookSink(new AnimationDoneHook(AnimHookDirection.Both));
            }

            AdvanceToNextAnimation(quantum, ref node, ref frameNumber, ref offset);
            quantum = timeLeft;
        }
    }

    /// <summary>Retail <c>CSequence::advance_to_next_animation</c>.</summary>
    private void AdvanceToNextAnimation(double quantum, ref LinkedListNode<AnimSequenceNode> node, ref double frameNumber, ref AcFrame offset)
    {
        var anim = node.Value;
        if (quantum < 0.0)
        {
            if (anim.Framerate >= 0f)
            {
                if (anim.Anim?.PosFrames is not null && anim.GetPosFrame((int)Math.Floor(frameNumber)) is { } pos)
                {
                    offset = AcFrame.Subtract1(offset, pos);
                }

                if (Math.Abs(anim.Framerate) > SequenceConstants.Epsilon)
                {
                    offset = ApplyPhysics(offset, 1.0 / anim.Framerate, quantum);
                }
            }

            node = node.Previous ?? _list.Last!;
            anim = node.Value;
            frameNumber = anim.EndingFrame;
            if (anim.Framerate < 0f)
            {
                if (anim.Anim?.PosFrames is not null && anim.GetPosFrame((int)Math.Floor(frameNumber)) is { } pos)
                {
                    offset = AcFrame.Combine(offset, pos);
                }

                if (Math.Abs(anim.Framerate) > SequenceConstants.Epsilon)
                {
                    offset = ApplyPhysics(offset, 1.0 / anim.Framerate, quantum);
                }
            }
        }
        else
        {
            if (anim.Framerate < 0f)
            {
                if (anim.Anim?.PosFrames is not null && anim.GetPosFrame((int)Math.Floor(frameNumber)) is { } pos)
                {
                    offset = AcFrame.Subtract1(offset, pos);
                }

                if (Math.Abs(anim.Framerate) > SequenceConstants.Epsilon)
                {
                    offset = ApplyPhysics(offset, 1.0 / anim.Framerate, quantum);
                }
            }

            node = node.Next ?? FirstCyclic!;
            anim = node.Value;
            frameNumber = anim.StartingFrame;
            if (anim.Framerate > 0f)
            {
                if (anim.Anim?.PosFrames is not null && anim.GetPosFrame((int)Math.Floor(frameNumber)) is { } pos)
                {
                    offset = AcFrame.Combine(offset, pos);
                }

                if (Math.Abs(anim.Framerate) > SequenceConstants.Epsilon)
                {
                    offset = ApplyPhysics(offset, 1.0 / anim.Framerate, quantum);
                }
            }
        }
    }
}
