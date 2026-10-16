using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Effects;
using Lifestone.Client.Objects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// Living statics (docs/objects.md §1.7): retail <c>CPhysicsObj::animate_static_object</c> stepping, default scripts that
/// light Holtburg's torches and chimneys, the emitters' draw range, and default animations (Holtburg's circling birds).
/// </summary>
public sealed class LivingStaticsTests(ObjectsFixture fixture, ITestOutputHelper output) : IClassFixture<ObjectsFixture>
{
    private const uint StandingTorch = 0x020005D8; // default script 0x330006F8: flame 0x3200002F + glow 0x32000829
    private const uint Chimney = 0x0200043B;       // the invisible anchor with smoke 0x3200020D
    private const uint Birds = 0x02000494;         // two wing parts 12.2 m out, default animation 0x030006CA with a SetOmega hook

    private LivingStaticSources Sources() => new(new DatEffectSource(fixture.Dats), new DatAnimationSource(fixture.Dats));

    private DatParticleModels Models() => new(fixture.Dats, fixture.Library.Meshes);

    private (LivingStaticSet Set, LandblockObjectSet Objects) Holtburg(double now, WorldObjectKinds kinds = WorldObjectKinds.All) =>
        Landblock(LandblockCoord.Holtburg, now, kinds);

    private (LivingStaticSet Set, LandblockObjectSet Objects) Landblock(LandblockCoord coord, double now, WorldObjectKinds kinds = WorldObjectKinds.All)
    {
        var objects = fixture.Build(coord, kinds);
        var sources = Sources();
        var plan = LivingStaticPlan.Create(objects, fixture.Library, sources) ?? throw new InvalidOperationException($"{coord} has no living statics");
        return (new LivingStaticSet(plan, fixture.Library, sources, Models(), now, seed: 1), objects);
    }

    [RequiresDatsFact]
    public void Holtburg_has_its_torches_chimneys_and_birds_as_living_statics()
    {
        var (set, _) = Holtburg(0);
        int Count(uint model) => set.Statics.Count(s => s.Object.ModelId == model);
        output.WriteLine($"living statics in 0xA9B4: {set.Statics.Count}; torches {Count(StandingTorch)}, chimneys {Count(Chimney)}, birds {Count(Birds)}");
        Assert.True(Count(StandingTorch) >= 3);
        Assert.True(Count(Chimney) >= 1);
        Assert.True(set.Statics.All(s => s.HasDefaultScript || s.HasDefaultAnimation || s.UsesMotionTable));
        Assert.Contains(0x01001689u, set.Plan.ParticleGfxObjs); // the torch flame sprite
    }

    [RequiresDatsFact]
    public void A_static_steps_like_animate_static_object()
    {
        var (set, _) = Holtburg(10.0);
        var torch = set.Statics.First(s => s.Object.ModelId == StandingTorch);
        var always = (ParticleEmitter _) => true;

        // Less than MIN_QUANTUM (1/30 s) after creation: nothing happens and the time accumulates.
        Assert.False(torch.Animate(10.02, always));
        Assert.Equal(0, torch.Steps);
        Assert.Equal(10.0, torch.UpdateTime);

        // 1/30 s or more: one step of the whole elapsed time; the default script creates the flame's emitters.
        torch.Animate(10.04, always);
        Assert.Equal(1, torch.Steps);
        Assert.Equal(10.04, torch.UpdateTime);
        Assert.Equal(2, torch.Effects.Particles.Count);

        // More than 2 s: only the clock moves.
        torch.Animate(13.0, always);
        Assert.Equal(1, torch.Steps);
        Assert.Equal(13.0, torch.UpdateTime);
    }

    [RequiresDatsFact]
    public void Torch_flames_burn_above_the_torch_while_the_viewer_is_within_their_degrade_distance()
    {
        var (set, objects) = Holtburg(0);
        var torchIndex = set.Statics.ToList().FindIndex(s => s.Object.ModelId == StandingTorch);
        var torch = set.Statics[torchIndex];
        var near = torch.Object.Origin + new Vector3(4f, 3f, 1.7f);
        for (var t = 1.0 / 60.0; t <= 3.0; t += 1.0 / 60.0)
        {
            set.Advance(t, near, null, long.MaxValue);
        }

        var particles = new List<ParticleInstance>();
        torch.Effects.CollectParticles(particles);
        var flame = particles.Where(p => p.EmitterInfoId == 0x3200002Fu).ToList();
        output.WriteLine($"torch at {torch.Object.Origin}: {particles.Count} particles, {flame.Count} flame; steps {torch.Steps}");
        foreach (var p in flame.Take(5))
        {
            output.WriteLine($"  flame particle at {p.Position - torch.Object.Origin} scale {p.Scale:0.00} translucency {p.Translucency:0.00}");
        }

        // At most one step per 1/30 s: a step every second or third 60 fps frame (two frames' worth of a float clock can
        // fall a hair short of MIN_QUANTUM), between 20 and 30 Hz.
        Assert.InRange(torch.Steps, 59, 91);
        Assert.True(flame.Count >= 8, $"{flame.Count} flame particles");
        foreach (var p in flame)
        {
            var offset = p.Position - torch.Object.Origin;
            Assert.InRange(MathF.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y)), 0f, 1.0f);
            Assert.InRange(offset.Z, 1.0f, 4.0f); // on top of the post
        }

        Assert.Contains(torch.Effects, set.Drawn);
        Assert.True(set.Stats.LiveParticles >= flame.Count);

        // 300 m away (the far-cell rule's horizontal distance is beyond every emitter's 64-128 m): the emitters are
        // degraded out (ParticleEmitter::UpdateParticles with ShouldDrawParticles false) and nothing is drawn.
        var far = torch.Object.Origin + new Vector3(300f, 0f, 0f);
        for (var t = 3.0; t <= 3.5; t += 1.0 / 60.0)
        {
            set.Advance(t, far, null, long.MaxValue);
        }

        Assert.All(torch.Effects.Particles.Emitters, e => Assert.True(e.DegradedOut));
        var list = new ParticleDrawList();
        list.Add(torch.Effects, far, Models());
        Assert.Equal(0, list.Stats.Drawn);
        _ = objects;
    }

    [RequiresDatsFact]
    public void Birds_circle_their_origin_and_flap_by_their_default_animation()
    {
        // The first landblock of Holtburg's 3×3 whose scenery has the birds.
        var coord = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => LandblockCoord.Holtburg.Offset(dx, dy)))
            .OfType<LandblockCoord>()
            .First(c => fixture.Build(c, WorldObjectKinds.Scenery).Objects.Any(o => o.ModelId == Birds));
        var (set, _) = Landblock(coord, 0, WorldObjectKinds.Scenery);
        var bird = set.Statics.FirstOrDefault(s => s.Object.ModelId == Birds);
        Assert.NotNull(bird);
        output.WriteLine($"birds in {coord} at {bird.Object.Origin}, scale {bird.Scale}");
        Assert.True(bird.HasDefaultAnimation);
        var start = bird.PartFrames.ToArray();
        var startFrame = bird.Frame;
        var viewer = bird.Object.Origin + new Vector3(20f, 0f, 2f);
        var moved = 0;
        for (var t = 1.0 / 60.0; t <= 4.0; t += 1.0 / 60.0)
        {
            set.Advance(t, viewer, null, long.MaxValue);
            moved += set.Moved.Count(i => set.Statics[i] == bird);
        }

        output.WriteLine($"bird steps {bird.Steps}, moved {moved}; omega {bird.Effects.Visual.Omega}; frame turned {Angle(startFrame.Orientation, bird.Frame.Orientation):0.0} deg");
        Assert.True(moved >= 80, $"{moved} moves in 4 s"); // one per step (20-30 Hz at 60 fps), plus the initial pose
        Assert.Equal(bird.Steps + 1, moved);
        Assert.NotEqual(Vector3.Zero, bird.Effects.Visual.Omega);
        Assert.True(Angle(startFrame.Orientation, bird.Frame.Orientation) > 1f);
        for (var i = 0; i < start.Length; i++)
        {
            Assert.NotEqual(start[i], bird.PartFrames[i]);

            // The wings stay on their 12.2 m circle (times the scenery scale) around the object's vertical axis.
            var offset = bird.PartFrames[i].Origin - bird.Object.Origin;
            Assert.InRange(MathF.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y)), 11.5f * bird.Scale, 12.5f * bird.Scale);
        }
    }

    [RequiresDatsFact]
    public void A_budget_steps_the_rest_on_a_later_frame()
    {
        var (set, _) = Holtburg(0);
        Assert.False(set.Advance(1.0, Vector3.Zero, null, deadline: 0)); // only the first 8 get a turn
        Assert.Equal(Math.Min(8, set.Statics.Count), set.Stats.Stepped);
        Assert.True(set.Advance(1.1, Vector3.Zero, null, long.MaxValue));
        Assert.Equal(set.Statics.Count, set.Stats.Stepped);
        Assert.All(set.Statics, s => Assert.True(s.UpdateTime >= 1.0));
        _ = Stopwatch.Frequency;
    }

    [RequiresDatsFact]
    public void A_statics_sound_hooks_reach_the_sink_with_its_sound_table_and_its_origin_from_the_listener()
    {
        var (set, _) = Holtburg(10.0);
        var torch = set.Statics.First(s => s.Object.ModelId == StandingTorch);
        var sink = new RecordingSink();
        var sounds = new LivingStaticSounds { Sink = sink };
        set.Sounds = sounds;
        var always = (ParticleEmitter _) => true;

        // A listener 10 m due east of the torch, facing north: the torch is on its left (west).
        var torchCell = new CellFrame(torch.Object.CellId, torch.Frame);
        var listener = new CellFrame(torch.Object.CellId, new AcFrame(torch.Frame.Origin + new Vector3(10f, 0f, 0f), Quaternion.Identity));
        sounds.SetListener(listener);
        torch.Effects.QueueAnimHook(new SoundTableHook(AnimHookDirection.Both, 0x1F));
        torch.Effects.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 0x0A000001));
        torch.Animate(10.05, always, sounds);

        Assert.Equal(2, sink.Events.Count);
        Assert.IsType<SoundTableHook>(sink.Events[0].Hook);
        Assert.IsType<SoundHook>(sink.Events[1].Hook);
        Assert.All(sink.Tables, t => Assert.Equal(torch.Model.Assembly?.DefaultSoundTableId ?? 0, t));
        Assert.Equal(SoundOrigin.Between(listener, torchCell), sink.Origins[0]);
        Assert.True(RetailSoundRules.PanIndex(sink.Origins[0].Distance, sink.Origins[0].BearingDegrees, mono: false) < 0, "a source to the listener's west pans left");
        Assert.Equal(2, sounds.Delivered);
        Assert.Equal(0, torch.EventsDropped);
        Assert.Empty(torch.Effects.PendingEvents);
    }

    [RequiresDatsFact]
    public void A_statics_events_are_dropped_and_counted_with_no_sink_or_no_listener()
    {
        var (set, _) = Holtburg(10.0);
        var torch = set.Statics.First(s => s.Object.ModelId == StandingTorch);
        var always = (ParticleEmitter _) => true;
        var sink = new RecordingSink();
        var sounds = new LivingStaticSounds { Sink = sink }; // a sink but no listener yet

        torch.Effects.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 0x0A000001));
        torch.Animate(10.05, always, sounds);
        torch.Effects.QueueAnimHook(new SoundHook(AnimHookDirection.Both, 0x0A000001));
        torch.Animate(10.10, always, null); // no sounds at all: M1's behaviour

        Assert.Empty(sink.Events);
        Assert.Equal(2, torch.EventsDropped);
        Assert.Empty(torch.Effects.PendingEvents); // nothing accumulates
    }

    private sealed class RecordingSink : IEffectEventSink
    {
        public List<EffectEvent> Events { get; } = [];

        public List<SoundOrigin> Origins { get; } = [];

        public List<uint> Tables { get; } = [];

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId)
        {
            Events.Add(effectEvent);
            Origins.Add(origin);
            Tables.Add(soundTableId);
        }
    }

    private static float Angle(Quaternion a, Quaternion b) =>
        2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f)) * 180f / MathF.PI;
}
