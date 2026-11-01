using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;

namespace Lifestone.Data.Tests.Effects;

/// <summary>
/// The creature buffs' orbs and the cast gestures' hand sparks share emitter id 1 (D100). Retail's <c>ParticleManager::CreateParticleEmitter</c> (11.6096 0x0051C1F0) destroys the emitter
/// holding an id before it makes the new one, so the next cast's gesture removes the orbs at its first frame. Kept as retail on purpose (owner decision D100): the orbs play out when
/// casts are spaced as they were in early retail, and are cut short at the faster buffing pace of late retail, which the autopilot's cast pace matches. Not a glitch.
/// </summary>
public sealed class BuffEmitterIdTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanTable = 0x34000004;

    /// <summary>A talisman cast gesture of the human motion table: hand sparks as emitters 1 to 4 at frame 0.</summary>
    private const uint Gesture = 0x03000596;

    [RequiresDatsFact]
    public void The_next_cast_gesture_removes_the_Strength_Self_orbs_as_retail_does()
    {
        var fx = new EffectObject(new DatEffectSource(fixture.Dats), new RetailRandom(3), HumanTable);
        var parts = Enumerable.Repeat(AcFrame.Identity, 20).ToArray(); // the gesture's sparks hang from parts 12 and 15
        fx.SetPlacement(0, AcFrame.Identity, parts);
        Assert.Equal(0x33000047u, fx.PlayScript(PlayScript.AttribUpRed, 1f)); // ACE's intensity for the levels VI and VII the mage casts
        fx.Update(1 / 30.0, AcFrame.Identity, parts); // the script's hook runs: 100 orbs at once, as emitter 1
        var orbs = Assert.Single(fx.Particles.Emitters);
        Assert.Equal(1u, orbs.Id);
        Assert.Equal(100, orbs.NumParticles);

        var first = new DatAnimationSource(fixture.Dats).GetAnimation(Gesture)!.PartFrames[0].Hooks;
        Assert.Contains(first.OfType<CreateParticleHook>(), h => h.EmitterId == 1);
        foreach (var hook in first)
        {
            fx.QueueAnimHook(hook);
        }

        fx.Update(2 / 30.0, AcFrame.Identity, parts);
        Assert.DoesNotContain(orbs, fx.Particles.Emitters); // gone with their 2.5 s mostly unplayed
        Assert.True(fx.Particles.TryGet(1, out var holder));
        Assert.NotEqual(orbs.Info.Id, holder!.Info.Id); // id 1 is the gesture's sparks now
    }
}
