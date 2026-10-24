using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Data.Tests.Effects;

/// <summary>Emitter/particle simulation: determinism with a fixed seed, emission rules, stop/destroy/blocking semantics.</summary>
public sealed class ParticleSimulationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    internal sealed class FakeEffects : IEffectSource
    {
        public Dictionary<uint, PhysicsScriptData> Scripts { get; } = [];

        public Dictionary<uint, ParticleEmitterInfoData> Emitters { get; } = [];

        public PhysicsScriptData? GetScript(uint scriptId) => Scripts.GetValueOrDefault(scriptId);

        public PhysicsScriptTableData? GetScriptTable(uint tableId) => null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => Emitters.GetValueOrDefault(emitterInfoId);
    }

    private static ParticleEmitterInfoData Info(uint id, EmitterKind type = EmitterKind.BirthratePerSec, double birthrate = 0.1, int max = 10,
        int initial = 0, int total = 0, double seconds = 0, double lifespan = 1.0) => new()
        {
            Id = id,
            EmitterType = type,
            ParticleType = ParticleKind.LocalVelocity,
            HwGfxObjId = 0x01000123,
            Birthrate = birthrate,
            MaxParticles = max,
            InitialParticles = initial,
            TotalParticles = total,
            TotalSeconds = seconds,
            Lifespan = lifespan,
            A = Vector3.UnitZ,
            MinA = 1,
            MaxA = 1,
            MinB = 1,
            MaxB = 1,
            MinC = 1,
            MaxC = 1,
            StartScale = 1,
            FinalScale = 1,
        };

    private static List<ParticleInstance> Run(IEffectSource source, IEffectRandom random, uint emitterInfoId, double seconds, Func<double, AcFrame>? path = null)
    {
        var fx = new EffectObject(source, random);
        var frame = path?.Invoke(0) ?? new AcFrame(new Vector3(50, 60, 10), Quaternion.Identity);
        fx.SetPlacement(0, frame, [frame]);
        fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, emitterInfoId, -1, new AcFrame(new Vector3(0, 0.4f, 1.2f), Quaternion.Identity), 1));
        var all = new List<ParticleInstance>();
        for (var i = 1; i * (1.0 / 60) <= seconds; i++)
        {
            var t = i / 60.0;
            var f = path?.Invoke(t) ?? frame;
            fx.Update(t, f, [f]);
            fx.CollectParticles(all);
        }

        return all;
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Windup_glow_is_deterministic_for_a_seed()
    {
        var source = new DatEffectSource(fixture.Dats);
        var info = source.GetEmitterInfo(0x3200011E)!; // first wind-up glow emitter of animation 0x030005A0
        Assert.NotEqual(0u, info.HwGfxObjId);

        var a = Run(source, new RetailRandom(42), 0x3200011E, 2.0);
        var b = Run(source, new RetailRandom(42), 0x3200011E, 2.0);
        var c = Run(source, new RetailRandom(43), 0x3200011E, 2.0);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.All(a, p => Assert.Equal(info.HwGfxObjId, p.GfxObjId));
        Assert.All(a, p => Assert.InRange(p.Slot, 0, info.MaxParticles - 1));
    }

    [Fact]
    public void Per_second_emitter_emits_one_particle_per_update_when_due_and_respects_max()
    {
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, birthrate: 0.05, max: 5, lifespan: 10.0);
        var fx = new EffectObject(source, new RetailRandom(1));
        var frame = AcFrame.Identity;
        fx.SetPlacement(0, frame, []);
        fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, 1, -1, AcFrame.Identity, 0));
        var emitter = Assert.Single(fx.Particles.Emitters);
        Assert.Equal(0xFFFF0000u, emitter.Id); // ParticleManager allocates from -65536

        for (var i = 1; i <= 30; i++)
        {
            fx.Update(i / 60.0, frame, []);
        }

        // Due every update after 0.05 s (> birthrate strictly), at most one per update, capped at MaxParticles.
        Assert.Equal(5, emitter.NumParticles);
        Assert.Equal(5, emitter.TotalEmitted);
    }

    [Fact]
    public void Per_meter_emitter_emits_when_moved_more_than_birthrate()
    {
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, EmitterKind.BirthratePerMeter, birthrate: 1.0, max: 100, lifespan: 100.0);
        // The first emission compares against (0,0,0): the fresh particle object's origin in retail.
        var path = (double t) => new AcFrame(new Vector3((float)(10 + t * 18), 0, 0), Quaternion.Identity); // 0.3 m per 1/60 s
        var particles = Run(source, new RetailRandom(5), 1, 1.0, path);
        var emitted = particles.Select(p => p.Slot).Distinct().Count();
        // 18 m travelled; one emission whenever |Δ| > 1 m, checked once per update (0.3 m steps) → every 4th update.
        Assert.InRange(emitted, 15, 17);
    }

    [Fact]
    public void Total_particles_and_total_seconds_stop_the_emitter_and_it_is_removed_when_empty()
    {
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, birthrate: 0.0, max: 10, total: 3, lifespan: 0.5);
        source.Emitters[2] = Info(2, birthrate: 0.0, max: 10, seconds: 0.2, lifespan: 0.5);
        foreach (var id in new uint[] { 1, 2 })
        {
            var fx = new EffectObject(source, new RetailRandom(1));
            fx.SetPlacement(0, AcFrame.Identity, []);
            fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, id, -1, AcFrame.Identity, 7));
            var emitter = fx.Particles.Emitters[0];
            for (var i = 1; i <= 30; i++)
            {
                fx.Update(i / 60.0, AcFrame.Identity, []);
            }

            Assert.True(emitter.Stopped);
            Assert.True(emitter.TotalEmitted is >= 3 and <= 14);
            for (var i = 31; i <= 120; i++)
            {
                fx.Update(i / 60.0, AcFrame.Identity, []);
            }

            Assert.Equal(0, fx.Particles.Count); // UpdateParticles returned false once the last particle died
        }
    }

    [Fact]
    public void Stop_destroy_and_blocking_create_follow_ParticleManager()
    {
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, max: 4, initial: 2, lifespan: 5.0);
        var fx = new EffectObject(source, new RetailRandom(1));
        fx.SetPlacement(0, AcFrame.Identity, []);
        var create = new CreateParticleHook(AnimHookDirection.Both, 1, -1, AcFrame.Identity, 9);
        fx.ExecuteHook(create);
        Assert.Equal(2, fx.Particles.Emitters[0].NumParticles); // InitEnd emits initial_particles immediately

        fx.ExecuteHook(new CreateBlockingParticleHook(AnimHookDirection.Both, 1, -1, AcFrame.Identity, 9));
        Assert.Equal(1, fx.Particles.Count); // blocked: id 9 exists

        fx.ExecuteHook(create); // same id: the old emitter is destroyed and replaced
        Assert.Equal(1, fx.Particles.Count);

        fx.ExecuteHook(new StopParticleHook(AnimHookDirection.Both, 9));
        Assert.True(fx.Particles.Emitters[0].Stopped);
        fx.ExecuteHook(new DestroyParticleHook(AnimHookDirection.Both, 9));
        Assert.Equal(0, fx.Particles.Count);
    }

    [Fact]
    public async Task A_finished_emitter_whose_id_was_allocated_again_leaves_the_per_emitter_update()
    {
        // Allocated ids wrap around after 65,536 creations and can equal a live emitter's id. The older emitter finishes
        // first; the per-emitter update (living statics) must drop it and keep the newer one (it used to spin forever).
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, birthrate: 0.0, max: 2, initial: 1, total: 1, lifespan: 0.1);
        source.Emitters[2] = Info(2, max: 4, initial: 2, lifespan: 100.0);
        var frames = new EffectObject(source, new RetailRandom(1));
        frames.SetPlacement(0, AcFrame.Identity, []);
        var random = new RetailRandom(1);
        var manager = new ParticleManager();
        Assert.Equal(7u, manager.CreateParticleEmitter(source.Emitters[1], -1, AcFrame.Identity, 7, 0, frames, random, null));
        manager.NextEmitterId = 7;
        Assert.Equal(7u, manager.CreateParticleEmitter(source.Emitters[2], -1, AcFrame.Identity, 0, 0, frames, random, null));
        var lasting = manager.Emitters[1];

        var run = Task.Run(() =>
        {
            for (var i = 1; i <= 120; i++)
            {
                manager.UpdateParticles(i / 60.0, frames, random, _ => true, null);
            }
        });

        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)))); // else UpdateParticles never returned
        Assert.Same(lasting, Assert.Single(manager.Emitters));
        Assert.True(manager.TryGet(7, out var mapped));
        Assert.Same(lasting, mapped);
        Assert.True(manager.DestroyParticleEmitter(7));
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Unknown_emitter_info_or_missing_gfxobj_creates_nothing()
    {
        var source = new FakeEffects();
        source.Emitters[2] = new ParticleEmitterInfoData { Id = 2, HwGfxObjId = 0, MaxParticles = 3 };
        var fx = new EffectObject(source, new RetailRandom(1));
        fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, 99, -1, AcFrame.Identity, 0));
        fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, 2, -1, AcFrame.Identity, 0));
        Assert.Equal(0, fx.Particles.Count);
    }

    [Fact]
    public void Out_of_range_part_index_falls_back_to_the_object_frame_with_a_warning()
    {
        var source = new FakeEffects();
        source.Emitters[1] = Info(1, initial: 1);
        var fx = new EffectObject(source, new RetailRandom(1));
        var frame = new AcFrame(new Vector3(1, 2, 3), Quaternion.Identity);
        fx.SetPlacement(0, frame, [frame]);
        fx.ExecuteHook(new CreateParticleHook(AnimHookDirection.Both, 1, 15, AcFrame.Identity, 0));
        Assert.Single(fx.Particles.Emitters);
        Assert.NotEmpty(fx.Warnings);
    }
}
