using Lifestone.Client.Tests.Animation;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// <see cref="EffectObject.DrainEvents"/> into the caller's list, the two-list swap of queued animation hooks, and an
/// <see cref="EffectActor"/> frame that allocates nothing once warm (M2 wave 0, L5). Allocation is measured per frame
/// on this thread in the non-parallel <see cref="AllocationMeasurementCollection"/>; frames with a GC inside are not
/// judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class EffectEventDrainTests
{
    private const uint SyntheticAnimation = 0x03FF0002;

    /// <summary>A hook sink for twin sequences: one cached delegate that drops the hook.</summary>
    private static readonly Action<AnimHook> DropHook = static _ => { };

    [Fact]
    public void Drain_appends_the_pending_events_in_order_and_clears_them()
    {
        var fx = NewEffects();
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 1));
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 2));
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 3));
        fx.Update(1.0, AcFrame.Identity, []);
        Assert.Equal(3, fx.PendingEvents.Count);

        var into = new List<EffectEvent> { new(0.0, new SoundHook(AnimHookDirection.Both, 99)) };
        Assert.Equal(3, fx.DrainEvents(into));
        Assert.Equal(new uint[] { 99, 1, 2, 3 }, into.Select(e => ((SoundHook)e.Hook).SoundId));
        Assert.All(into.Skip(1), e => Assert.Equal(1.0, e.Time));
        Assert.Empty(fx.PendingEvents);
        Assert.Equal(0, fx.DrainEvents(into));
        Assert.Equal(4, into.Count);
    }

    [Fact]
    public void Queued_hooks_run_once_in_queue_order()
    {
        var fx = NewEffects();
        var into = new List<EffectEvent>();
        for (var round = 0; round < 3; round++)
        {
            fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, (uint)(10 * round)));
            fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, (uint)((10 * round) + 1)));
            fx.Update(round, AcFrame.Identity, []);
        }

        fx.Update(3.0, AcFrame.Identity, []); // nothing queued: the swapped-out hooks must not run again
        fx.DrainEvents(into);
        Assert.Equal(new uint[] { 0, 1, 10, 11, 20, 21 }, into.Select(e => ((SoundHook)e.Hook).SoundId));
    }

    [Fact]
    public void A_hook_that_throws_does_not_make_the_hooks_already_run_run_again()
    {
        // The engine keeps running after a script exception, so the next frame must not swap the hooks run before the
        // throw back in: they would repeat their sounds and re-create their emitters. As with the copy-then-clear the swap
        // replaced, the hooks after the throwing one are dropped.
        var fx = new EffectObject(new ThrowingEmitterData(), new RetailRandom(1));
        fx.SetPlacement(0.0, AcFrame.Identity, []);
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 1));
        fx.QueueAnimHook(new CreateParticleHook(AnimHookDirection.Both, 0x32000001, 0, AcFrame.Identity, 1));
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 2));
        Assert.Throws<InvalidOperationException>(() => fx.Update(1.0, AcFrame.Identity, []));

        var into = new List<EffectEvent>();
        fx.Update(2.0, AcFrame.Identity, []);
        fx.DrainEvents(into);
        Assert.Equal(new uint[] { 1 }, into.Select(e => ((SoundHook)e.Hook).SoundId));

        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 3)); // the queue still works
        fx.Update(3.0, AcFrame.Identity, []);
        into.Clear();
        fx.DrainEvents(into);
        Assert.Equal(new uint[] { 3 }, into.Select(e => ((SoundHook)e.Hook).SoundId));
    }

    [Fact]
    public void Discard_drops_the_pending_events_and_counts_them()
    {
        var fx = NewEffects();
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 1));
        fx.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 2));
        fx.Update(1.0, AcFrame.Identity, []);
        Assert.Equal(2, fx.DiscardEvents());
        Assert.Empty(fx.PendingEvents);
        Assert.Equal(0, fx.DiscardEvents());
    }

    [Fact]
    public void An_effect_object_frame_with_hooks_and_a_drain_allocates_nothing()
    {
        var fx = NewEffects();
        var parts = new AcFrame[2];
        var into = new List<EffectEvent>();
        var a = new SoundHook(AnimHookDirection.Both, 1);
        var b = new SoundHook(AnimHookDirection.Both, 2);
        var t = 0.0;
        for (var i = 0; i < 120; i++)
        {
            Frame(fx, t += 1 / 60.0, parts, into, a, b);
        }

        const int Frames = 3_600;
        var allocated = 0L;
        var allocating = 0;
        var gcFrames = 0;
        var drained = 0;
        for (var i = 0; i < Frames; i++)
        {
            var window = AllocationWindow.Open();
            drained += Frame(fx, t += 1 / 60.0, parts, into, a, b);
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
            allocating += bytes != 0 ? 1 : 0;
        }

        Assert.Equal(2 * Frames, drained);
        Assert.True(gcFrames <= Frames / 100, $"{gcFrames} of {Frames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocating} of {Frames - gcFrames} frames without a GC allocated {allocated} B in total");
    }

    [Fact]
    public void An_animated_actor_frame_adds_no_allocation_over_its_animation()
    {
        // EffectActor.AdvanceTo (the ObjectClock's steps, the default animation's reused hook list, the hook queue swap)
        // and the drain into the caller's list, against a bare sequence stepped by the same quantum in lockstep: the
        // actor's frame must allocate exactly what the sequence's own step does.
        var animations = new SyntheticAnimations(SyntheticAnimation, frames: 10);
        var fx = NewEffects();
        var actor = new EffectActor(fx, new DefaultAnimationPlayer(animations, SyntheticAnimation), partCount: 1, AcFrame.Identity, scale: 1f, now: 0.0);
        var twin = new AnimSequence(animations) { HookSink = DropHook };
        twin.AppendAnimation(new AnimDataRef(SyntheticAnimation, 0, -1, DefaultAnimationPlayer.DefaultFramerate));
        var into = new List<EffectEvent>();
        var t = 0.0;
        for (var i = 0; i < 120; i++)
        {
            var quantum = (t += 1 / 60.0) - actor.UpdateTime;
            actor.AdvanceTo(t);
            into.Clear();
            fx.DrainEvents(into);
            twin.Update(quantum);
        }

        const int Frames = 3_600;
        var extra = 0L;
        var judged = 0;
        var drained = 0;
        for (var i = 0; i < Frames; i++)
        {
            var quantum = (t += 1 / 60.0) - actor.UpdateTime; // the one step ObjectClock takes for a 1/60 s gap
            var window = AllocationWindow.Open();
            actor.AdvanceTo(t);
            into.Clear();
            drained += fx.DrainEvents(into);
            var (bytes, gcInside) = window.Close();
            var twinWindow = AllocationWindow.Open();
            twin.Update(quantum);
            var (twinBytes, twinGcInside) = twinWindow.Close();
            if (gcInside || twinGcInside)
            {
                continue;
            }

            judged++;
            extra += bytes - twinBytes;
        }

        Assert.True(drained > 1_000, $"{drained} sound events in {Frames} frames: the animation's hooks no longer reach the effect object");
        Assert.True(judged >= Frames - (Frames / 100), $"only {judged} of {Frames} frame pairs had no GC inside; too few to trust the measurement");
        Assert.True(extra == 0, $"the actor allocated {extra} B more than its animation's sequence over {judged} frames");
    }

    [Fact]
    public void An_animated_actor_frame_allocates_nothing()
    {
        // EffectActor.AdvanceTo: the ObjectClock's steps, the default animation's reused hook list, the hook queue swap
        // and the drain into the caller's list.
        var animations = new SyntheticAnimations(SyntheticAnimation, frames: 10);
        var fx = NewEffects();
        var actor = new EffectActor(fx, new DefaultAnimationPlayer(animations, SyntheticAnimation), partCount: 1, AcFrame.Identity, scale: 1f, now: 0.0);
        var into = new List<EffectEvent>();
        var t = 0.0;
        for (var i = 0; i < 120; i++)
        {
            actor.AdvanceTo(t += 1 / 60.0);
            into.Clear();
            fx.DrainEvents(into);
        }

        const int Frames = 3_600;
        var allocated = 0L;
        var allocating = 0;
        var gcFrames = 0;
        var drained = 0;
        for (var i = 0; i < Frames; i++)
        {
            var window = AllocationWindow.Open();
            actor.AdvanceTo(t += 1 / 60.0);
            into.Clear();
            drained += fx.DrainEvents(into);
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                gcFrames++;
                continue;
            }

            allocated += bytes;
            allocating += bytes != 0 ? 1 : 0;
        }

        Assert.True(drained > 1_000, $"{drained} sound events in {Frames} frames: the animation's hooks no longer reach the effect object");
        Assert.Equal(t, actor.UpdateTime, 9);
        Assert.True(gcFrames <= Frames / 100, $"{gcFrames} of {Frames} frames had a GC inside (not judged); too many to trust the measurement");
        Assert.True(allocated == 0, $"{allocating} of {Frames - gcFrames} frames without a GC allocated {allocated} B in total");
    }

    private static EffectObject NewEffects()
    {
        var fx = new EffectObject(new NoEffectData(), new RetailRandom(1));
        fx.SetPlacement(0.0, AcFrame.Identity, []);
        return fx;
    }

    private static int Frame(EffectObject fx, double now, AcFrame[] parts, List<EffectEvent> into, AnimHook a, AnimHook b)
    {
        fx.QueueAnimHook(a);
        fx.QueueAnimHook(b);
        fx.Update(now, AcFrame.Identity, parts);
        into.Clear();
        return fx.DrainEvents(into);
    }

    /// <summary>An effect source with no scripts, tables or emitters.</summary>
    private sealed class NoEffectData : IEffectSource
    {
        public PhysicsScriptData? GetScript(uint scriptId) => null;

        public PhysicsScriptTableData? GetScriptTable(uint tableId) => null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => null;
    }

    /// <summary>An effect source whose emitter lookup throws, standing in for any hook that fails while it runs.</summary>
    private sealed class ThrowingEmitterData : IEffectSource
    {
        public PhysicsScriptData? GetScript(uint scriptId) => null;

        public PhysicsScriptTableData? GetScriptTable(uint tableId) => null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) =>
            throw new InvalidOperationException($"test: emitter 0x{emitterInfoId:X8} fails to load");
    }
}
