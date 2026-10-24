using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Animation;

/// <summary>
/// <see cref="MotionPlayer.Update"/> and <see cref="DefaultAnimationPlayer.Update"/> allocate nothing per step once
/// warm: they hand back their own reused hook list instead of a copy (M2 wave 0, L5). Measured per step on this
/// thread in the non-parallel <see cref="AllocationMeasurementCollection"/>; steps with a GC inside are not judged.
/// </summary>
/// <remarks>
/// The players' own share is also compared with the same step of a bare <see cref="AnimSequence"/> (the "adds no
/// allocation" tests); the sequence itself allocates nothing per step either (no boxed hook enumerator, one shared
/// anim_done_hook).
/// </remarks>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PoseDriverAllocationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanMotionTable = 0x09000001;
    private const uint SyntheticAnimation = 0x03FF0001;

    /// <summary>A hook sink for twin sequences: one cached delegate that drops the hook.</summary>
    private static readonly Action<AnimHook> DropHook = static _ => { };

    [Fact]
    public void A_default_animation_step_adds_no_allocation_over_its_sequence()
    {
        // The player and a bare sequence play the same 10-frame, hook-on-every-frame loop in lockstep; each step of the
        // player must allocate exactly what the sequence's own step does.
        var animations = new SyntheticAnimations(SyntheticAnimation, frames: 10);
        var player = new DefaultAnimationPlayer(animations, SyntheticAnimation);
        var twin = new AnimSequence(animations) { HookSink = DropHook };
        twin.AppendAnimation(new AnimDataRef(SyntheticAnimation, 0, -1, DefaultAnimationPlayer.DefaultFramerate));
        for (var i = 0; i < 120; i++)
        {
            player.Update(1.0 / 60.0);
            twin.Update(1.0 / 60.0);
        }

        var (extra, judged, hooks) = CompareSteps(3_600, () => player.Update(1.0 / 60.0).Hooks.Count, () => twin.Update(1.0 / 60.0));

        Assert.True(judged >= 3_564, $"only {judged} of 3600 step pairs had no GC inside; too few to trust the measurement");
        Assert.True(extra == 0, $"the player allocated {extra} B more than its sequence over {judged} steps");
        Assert.True(hooks > 1_000, $"{hooks} hooks in 3600 steps (about 1800 frames crossed)");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_motion_table_step_adds_no_allocation_over_its_sequence_while_walking()
    {
        // Two identical players walk in lockstep: one through Update, the other only through its sequence (its hooks
        // dropped). While the walk cycle loops no motion completes, so the two sequences stay identical.
        var source = new DatAnimationSource(fixture.Dats);
        var table = source.GetMotionTable(HumanMotionTable)!;
        var player = new MotionPlayer(table, source);
        var twin = new MotionPlayer(table, source);
        player.ApplyInterpretedMotion(MotionCommands.NonCombat, MotionCommands.WalkForward, 1f);
        twin.ApplyInterpretedMotion(MotionCommands.NonCombat, MotionCommands.WalkForward, 1f);
        for (var i = 0; i < 240; i++) // the Ready -> WalkForward link finishes, then the walk cycle loops
        {
            player.Update(1.0 / 60.0);
            twin.Update(1.0 / 60.0);
        }

        twin.Sequence.HookSink = DropHook;
        var (extra, judged, hooks) = CompareSteps(3_600, () => player.Update(1.0 / 60.0).Hooks.Count, () => twin.Sequence.Update(1.0 / 60.0));

        Assert.Equal(player.Sequence.CurrentNode!.Value.AnimId, twin.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(player.Sequence.FrameNumber, twin.Sequence.FrameNumber);
        Assert.True(judged >= 3_564, $"only {judged} of 3600 step pairs had no GC inside; too few to trust the measurement");
        Assert.True(extra == 0, $"the player allocated {extra} B more than its sequence over {judged} steps");

        // An empty list's ToArray() returns a cached empty array, so only steps that fire hooks would show a per-step
        // copy: without them this test could not catch the old _fired.ToArray().
        Assert.True(hooks > 0, $"the walk cycle fired no hooks in 3600 steps: the test no longer covers the hook list copy");
    }

    [Fact]
    public void A_default_animation_step_allocates_nothing_and_still_reports_its_hooks()
    {
        // A 10-frame loop at 30 fps with a Sound hook on every frame, stepped at 60 fps: every other step fires a hook.
        var player = new DefaultAnimationPlayer(new SyntheticAnimations(SyntheticAnimation, frames: 10), SyntheticAnimation);
        for (var i = 0; i < 120; i++)
        {
            player.Update(1.0 / 60.0);
        }

        var (allocated, allocating, gcSteps, hooks) = Measure(player, steps: 3_600);

        Assert.True(gcSteps <= 36, $"{gcSteps} of 3600 steps had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocating} steps allocated {allocated} B in total");
        Assert.True(hooks > 1_000, $"{hooks} hooks in 3600 steps (about 1800 frames crossed)");
    }

    [Fact]
    public void The_hook_list_is_reused_and_valid_until_the_next_step()
    {
        var player = new DefaultAnimationPlayer(new SyntheticAnimations(SyntheticAnimation, frames: 10), SyntheticAnimation);
        var first = player.Update(0.25); // 7.5 frames at 30 fps
        Assert.NotEmpty(first.Hooks);
        Assert.All(first.Hooks, h => Assert.IsType<SoundHook>(h));
        var copy = first.Hooks.ToArray();

        var second = player.Update(0.0);
        Assert.Same(first.Hooks, second.Hooks);
        Assert.Empty(second.Hooks); // the earlier result is now stale: callers copy what must outlive a step
        Assert.NotEmpty(copy);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void A_motion_table_step_allocates_nothing_while_walking()
    {
        var source = new DatAnimationSource(fixture.Dats);
        var player = new MotionPlayer(source.GetMotionTable(HumanMotionTable)!, source);
        player.ApplyInterpretedMotion(MotionCommands.NonCombat, MotionCommands.WalkForward, 1f);
        for (var i = 0; i < 240; i++) // the Ready → WalkForward link finishes, then the walk cycle loops
        {
            player.Update(1.0 / 60.0);
        }

        var (allocated, allocating, gcSteps, _) = Measure(player, steps: 3_600);

        Assert.True(gcSteps <= 36, $"{gcSteps} of 3600 steps had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocating} steps allocated {allocated} B in total");
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Steps_that_finish_a_link_and_run_animation_done_allocate_nothing()
    {
        // The motion changes outside the measured windows (queuing animations allocates nodes); the steps that play the
        // links out, fire AnimationDone hooks and feed them to the motion table are measured.
        var source = new DatAnimationSource(fixture.Dats);
        var player = new MotionPlayer(source.GetMotionTable(HumanMotionTable)!, source);
        uint[] motions = [MotionCommands.WalkForward, MotionCommands.Ready];
        for (var warm = 0; warm < 4; warm++)
        {
            player.ApplyInterpretedMotion(MotionCommands.NonCombat, motions[warm % 2], 1f);
            for (var i = 0; i < 120; i++)
            {
                player.Update(1.0 / 60.0);
            }
        }

        var allocated = 0L;
        var allocating = 0;
        var gcSteps = 0;
        var animationDone = 0;
        for (var change = 0; change < 20; change++)
        {
            player.ApplyInterpretedMotion(MotionCommands.NonCombat, motions[change % 2], 1f);
            for (var i = 0; i < 120; i++)
            {
                var window = AllocationWindow.Open();
                var update = player.Update(1.0 / 60.0);
                var (bytes, gcInside) = window.Close();
                for (var h = 0; h < update.Hooks.Count; h++)
                {
                    animationDone += update.Hooks[h] is AnimationDoneHook ? 1 : 0;
                }

                if (gcInside)
                {
                    gcSteps++;
                    continue;
                }

                allocated += bytes;
                allocating += bytes != 0 ? 1 : 0;
            }
        }

        Assert.True(animationDone > 0, "no AnimationDone hook fired: the test no longer covers the motion-table bookkeeping");
        Assert.Empty(player.State.Actions);
        Assert.True(gcSteps <= 24, $"{gcSteps} of 2400 steps had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocating} steps allocated {allocated} B in total");
    }

    /// <summary>
    /// Runs <paramref name="steps"/> step pairs, each half in its own measured window, and sums how many bytes the first
    /// allocated beyond the second over the pairs with no GC inside either window.
    /// </summary>
    private static (long Extra, int Judged, int Hooks) CompareSteps(int steps, Func<int> step, Func<AcFrame> twinStep)
    {
        var extra = 0L;
        var judged = 0;
        var hooks = 0;
        for (var i = 0; i < steps; i++)
        {
            var window = AllocationWindow.Open();
            var count = step();
            var (bytes, gcInside) = window.Close();
            var twinWindow = AllocationWindow.Open();
            twinStep();
            var (twinBytes, twinGcInside) = twinWindow.Close();
            hooks += count;
            if (gcInside || twinGcInside)
            {
                continue;
            }

            judged++;
            extra += bytes - twinBytes;
        }

        return (extra, judged, hooks);
    }

    private static (long Allocated, int AllocatingSteps, int GcSteps, int Hooks) Measure(IPoseDriver driver, int steps)
    {
        var allocated = 0L;
        var allocating = 0;
        var gcSteps = 0;
        var hooks = 0;
        for (var i = 0; i < steps; i++)
        {
            var window = AllocationWindow.Open();
            var update = driver.Update(1.0 / 60.0);
            var (bytes, gcInside) = window.Close();
            hooks += update.Hooks.Count;
            if (gcInside)
            {
                gcSteps++;
                continue;
            }

            allocated += bytes;
            allocating += bytes != 0 ? 1 : 0;
        }

        return (allocated, allocating, gcSteps, hooks);
    }
}

/// <summary>
/// An <see cref="IAnimationSource"/> with one synthetic Animation record (the raw dat layout
/// <see cref="AnimationData.Decode"/> reads): one part whose origin x is the frame index, and a Sound hook
/// (sound id = frame index) on every frame. Needs no dats.
/// </summary>
internal sealed class SyntheticAnimations : IAnimationSource
{
    private readonly uint _id;
    private readonly AnimationData _animation;

    public SyntheticAnimations(uint id, int frames)
    {
        _id = id;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(id);
        w.Write(0u); // no position frames
        w.Write(1u); // parts
        w.Write((uint)frames);
        for (var f = 0; f < frames; f++)
        {
            w.Write((float)f);
            w.Write(0f);
            w.Write(0f);
            w.Write(Quaternion.Identity.W);
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);
            w.Write(1u); // one hook
            w.Write((uint)AnimHookType.Sound);
            w.Write((int)AnimHookDirection.Both);
            w.Write((uint)f);
        }

        w.Flush();
        _animation = AnimationData.Decode(id, ms.ToArray());
    }

    public AnimationData? GetAnimation(uint animId) => animId == _id ? _animation : null;
}
