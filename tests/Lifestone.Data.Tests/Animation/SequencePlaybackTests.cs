using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// Unit tests of the <see cref="AnimSequence"/> (retail <c>CSequence</c>) rules on small synthetic animations built as
/// raw records, so the expected values can be worked out by hand from the retail functions.
/// </summary>
public sealed class SequencePlaybackTests
{
    /// <summary>Raw Animation record: one part; part i's origin.x = frame index; a Sound hook (id = frame) per frame.</summary>
    internal static byte[] BuildAnimation(uint id, int frames, AnimHookDirection hookDir = AnimHookDirection.Both, float posStepY = 0f)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var withPos = posStepY != 0f;
        w.Write(id);
        w.Write(withPos ? 1u : 0u);
        w.Write(1u); // parts
        w.Write((uint)frames);
        if (withPos)
        {
            for (var f = 0; f < frames; f++)
            {
                WriteFrame(w, new Vector3(0, posStepY, 0), Quaternion.Identity);
            }
        }

        for (var f = 0; f < frames; f++)
        {
            WriteFrame(w, new Vector3(f, 0, 0), Quaternion.Identity);
            w.Write(1u); // one hook
            w.Write((uint)AnimHookType.Sound);
            w.Write((int)hookDir);
            w.Write((uint)f);
        }

        return ms.ToArray();
    }

    private static void WriteFrame(BinaryWriter w, Vector3 o, Quaternion q)
    {
        w.Write(o.X);
        w.Write(o.Y);
        w.Write(o.Z);
        w.Write(q.W);
        w.Write(q.X);
        w.Write(q.Y);
        w.Write(q.Z);
    }

    internal sealed class FakeAnimations : IAnimationSource
    {
        private readonly Dictionary<uint, AnimationData> _anims = [];

        public FakeAnimations Add(uint id, int frames, AnimHookDirection dir = AnimHookDirection.Both, float posStepY = 0f)
        {
            _anims[id] = AnimationData.Decode(id, BuildAnimation(id, frames, dir, posStepY));
            return this;
        }

        public AnimationData? GetAnimation(uint animId) => _anims.GetValueOrDefault(animId);
    }

    private static (AnimSequence Seq, List<AnimHook> Hooks) Make(FakeAnimations anims)
    {
        var hooks = new List<AnimHook>();
        return (new AnimSequence(anims) { HookSink = hooks.Add }, hooks);
    }

    private static uint[] Sounds(IEnumerable<AnimHook> hooks) => hooks.OfType<SoundHook>().Select(h => h.SoundId).ToArray();

    [Fact]
    public void Forward_playback_fires_hooks_of_crossed_frames_and_shows_the_floor_frame()
    {
        var (seq, hooks) = Make(new FakeAnimations().Add(1, 10));
        seq.AppendAnimation(new AnimDataRef(1, 0, -1, 10f));
        Assert.Equal(9, seq.CurrentNode!.Value.HighFrame); // set_animation_id: -1 → last frame

        seq.Update(0.25); // 2.5 frames
        Assert.Equal(2.5, seq.FrameNumber, 9);
        Assert.Equal(new uint[] { 0, 1 }, Sounds(hooks));
        Assert.Equal(2f, seq.SamplePose(PoseSampling.RetailFloor)![0].Origin.X);
        Assert.Equal(2.5f, seq.SamplePose(PoseSampling.Interpolated)![0].Origin.X, 4);
    }

    [Fact]
    public void Direction_filter_follows_execute_hooks()
    {
        var (fwd, fwdHooks) = Make(new FakeAnimations().Add(1, 10, AnimHookDirection.Backward));
        fwd.AppendAnimation(new AnimDataRef(1, 0, -1, 10f));
        fwd.Update(0.5);
        Assert.Empty(fwdHooks); // backward-only hooks never fire when playing forward

        var (rev, revHooks) = Make(new FakeAnimations().Add(1, 10, AnimHookDirection.Backward));
        rev.AppendAnimation(new AnimDataRef(1, 2, 6, -10f));
        Assert.Equal(7 - 0.00019999999, rev.FrameNumber, 12); // get_starting_frame: high + 1 − ε
        Assert.Equal(6f, rev.SamplePose(PoseSampling.RetailFloor)![0].Origin.X);
        rev.Update(0.2); // −2 frames
        Assert.Equal(new uint[] { 6, 5 }, Sounds(revHooks));
        Assert.Equal(4f, rev.SamplePose(PoseSampling.RetailFloor)![0].Origin.X);
    }

    [Fact]
    public void Link_then_cycle_queues_anim_done_and_skips_the_last_link_frame_hook()
    {
        var (seq, hooks) = Make(new FakeAnimations().Add(1, 3).Add(2, 2));
        seq.AppendAnimation(new AnimDataRef(1, 0, -1, 10f)); // link: frames 0..2
        seq.AppendAnimation(new AnimDataRef(2, 0, -1, 10f)); // cycle: frames 0..1
        Assert.Same(seq.Nodes.Last(), seq.FirstCyclic!.Value);

        seq.Update(0.35); // 3.5 frames: past the end of the link by 0.5 frame
        // update_internal clamps to high_frame (2) and only fires hooks below it: frames 0 and 1. Then
        // anim_done_hook, then the remaining 0.05 s plays frames of the cycle from 0 (no crossing yet).
        Assert.Equal(new uint[] { 0, 1 }, Sounds(hooks));
        Assert.Single(hooks.OfType<AnimationDoneHook>());
        Assert.Equal(2u, seq.CurrentNode!.Value.AnimId);
        Assert.Equal(0.5, seq.FrameNumber, 9);
        Assert.Single(seq.Nodes); // apricot removed the finished link

        hooks.Clear();
        seq.Update(1.0); // 10 frames of a 2-frame cycle: loops without anim-done hooks (head == first_cyclic)
        Assert.DoesNotContain(hooks, h => h is AnimationDoneHook);
        Assert.Equal(new uint[] { 0, 0, 0, 0, 0 }, Sounds(hooks)); // frame 1's hook never fires (it is the high frame)
    }

    [Fact]
    public void Pos_frames_and_velocity_accumulate_per_crossed_frame()
    {
        var (seq, _) = Make(new FakeAnimations().Add(1, 10, posStepY: 0.5f));
        seq.AppendAnimation(new AnimDataRef(1, 0, -1, 10f));
        seq.SetVelocity(new Vector3(0, 0, 2f)); // apply_physics: velocity / framerate per frame
        var offset = seq.Update(0.3); // crosses frames 0, 1, 2
        Assert.Equal(1.5f, offset.Origin.Y, 4);
        Assert.Equal(0.6f, offset.Origin.Z, 4);
    }

    [Fact]
    public void Zero_framerate_applies_physics_for_the_whole_quantum()
    {
        var (seq, hooks) = Make(new FakeAnimations().Add(1, 4));
        seq.AppendAnimation(new AnimDataRef(1, 2, -1, 0f));
        seq.SetVelocity(new Vector3(1, 0, 0));
        var offset = seq.Update(0.5);
        Assert.Empty(hooks);
        Assert.Equal(0.5f, offset.Origin.X, 5);
        Assert.Equal(2f, seq.SamplePose(PoseSampling.RetailFloor)![0].Origin.X);
    }

    [Fact]
    public void Missing_animations_are_not_queued()
    {
        var (seq, _) = Make(new FakeAnimations().Add(1, 4));
        seq.AppendAnimation(new AnimDataRef(99, 0, -1, 30f));
        Assert.False(seq.HasAnims);
        Assert.Null(seq.CurrentNode);
    }

    [Fact]
    public void Remove_cyclic_anims_drops_a_current_cycle_and_the_next_append_becomes_current()
    {
        var (seq, _) = Make(new FakeAnimations().Add(1, 3).Add(2, 2));
        seq.AppendAnimation(new AnimDataRef(2, 0, -1, 10f));
        seq.Update(0.15);
        Assert.Equal(1.5, seq.FrameNumber, 9);

        // remove_cyclic_anims: the current node is the first cyclic one, so current moves to its predecessor (none)
        // and the frame number resets to 0; append_animation then makes the new head current at its starting frame.
        seq.RemoveCyclicAnims();
        Assert.Null(seq.CurrentNode);
        Assert.Equal(0.0, seq.FrameNumber);
        seq.AppendAnimation(new AnimDataRef(1, 0, -1, -10f));
        Assert.Equal(1u, seq.CurrentNode!.Value.AnimId);
        Assert.Equal(3 - 0.00019999999, seq.FrameNumber, 12);

        // Every append makes the new tail the first cyclic node: the last animation appended is the loop.
        seq.AppendAnimation(new AnimDataRef(2, 0, -1, 10f));
        Assert.Equal(2u, seq.FirstCyclic!.Value.AnimId);
        Assert.Equal(1u, seq.CurrentNode!.Value.AnimId);
    }
}
