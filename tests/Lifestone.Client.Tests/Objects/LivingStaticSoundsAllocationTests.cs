using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Effects;
using Lifestone.Client.Objects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Data.Tests;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Objects;

/// <summary>
/// M3 1.3 s1: handing a living static's sound hooks to the sink allocates nothing per step (measured against the same steps with the events discarded, as M1 did, so the torch's own
/// particles are not counted), in the non-parallel <see cref="AllocationMeasurementCollection"/>.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
[Trait("Tier", "Exhaustive")]
public sealed class LivingStaticSoundsAllocationTests(ObjectsFixture fixture) : IClassFixture<ObjectsFixture>
{
    private const uint StandingTorch = 0x020005D8;

    [RequiresDatsFact]
    public void Delivering_a_statics_sound_hooks_allocates_nothing_beyond_discarding_them()
    {
        var objects = fixture.Build(LandblockCoord.Holtburg);
        var sources = new LivingStaticSources(new DatEffectSource(fixture.Dats), new DatAnimationSource(fixture.Dats));
        var plan = LivingStaticPlan.Create(objects, fixture.Library, sources)!;
        var set = new LivingStaticSet(plan, fixture.Library, sources, new DatParticleModels(fixture.Dats, fixture.Library.Meshes), 10.0, seed: 1);
        var torch = set.Statics.First(s => s.Object.ModelId == StandingTorch);
        var sink = new CountingSink();
        var sounds = new LivingStaticSounds { Sink = sink };
        sounds.SetListener(new CellFrame(torch.Object.CellId, new AcFrame(torch.Frame.Origin + new Vector3(8f, 0f, 0f), Quaternion.Identity)));
        var always = (ParticleEmitter _) => true;
        var table = new SoundTableHook(AnimHookDirection.Both, 0x1F);
        var wave = new SoundHook(AnimHookDirection.Both, 0x0A000001);
        var now = 10.0;

        void Step(LivingStaticSounds? to)
        {
            now += 0.05;
            torch.Effects.QueueAnimHook(table);
            torch.Effects.QueueAnimHook(wave);
            torch.Animate(now, always, to);
        }

        for (var i = 0; i < 60; i++)
        {
            Step(sounds); // warm up: both paths, the lists' capacity, the JIT
            Step(null);
        }

        long Measure(LivingStaticSounds? to)
        {
            var total = 0L;
            var windows = 0;
            for (var i = 0; i < 300 && windows < 200; i++)
            {
                var window = AllocationWindow.Open();
                Step(to);
                var (bytes, gcInside) = window.Close();
                if (!gcInside)
                {
                    total += bytes;
                    windows++;
                }
            }

            return total;
        }

        var discarded = Measure(null);
        var delivered = Measure(sounds);

        Assert.True(sink.Count >= 2 * 200, $"{sink.Count} events reached the sink");
        Assert.True(delivered <= discarded, $"delivering allocated {delivered} B against {discarded} B for discarding the same events");
    }

    private sealed class CountingSink : IEffectEventSink
    {
        public long Count { get; private set; }

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId) => Count++;
    }
}
