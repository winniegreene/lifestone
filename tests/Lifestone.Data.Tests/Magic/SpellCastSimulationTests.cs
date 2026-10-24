using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;

namespace Lifestone.Data.Tests.Magic;

/// <summary>End-to-end: <see cref="SpellCastSimulation"/> plays showcase spells between a human and a drudge.</summary>
[Trait("Tier", "Exhaustive")]
public sealed class SpellCastSimulationTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly AcFrame CasterFrame = new(new Vector3(90, 90, 30), Quaternion.Identity);
    private static readonly AcFrame TargetFrame = new(new Vector3(90, 100, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI));

    private (SpellCastSimulation Sim, int MaxParticles) Play(
        uint spellId, int seed = 1, double seconds = 8.0, Func<SpellVisualResolver, SpellVisual, SpellVisual>? edit = null)
    {
        var world = AceWorldExtract.LoadFile(AceWorldExtract.FindSourceFile()!);
        var resolver = SpellVisualResolver.Create(fixture.Dats, world);
        var visual = resolver.Resolve(spellId, VisualObject.HumanMale(fixture.Dats), VisualObject.FromCreature(world.Creatures[0]));
        visual = edit?.Invoke(resolver, visual) ?? visual;
        var sim = new SpellCastSimulation(resolver, visual, CasterFrame, TargetFrame, seed);
        var max = 0;
        var particles = new List<ParticleInstance>();
        for (var t = 0.0; t <= seconds; t += 1.0 / 60)
        {
            sim.AdvanceTo(t);
            particles.Clear();
            foreach (var actor in new[] { sim.Caster, sim.Target }.OfType<EffectActor>().Concat(sim.Projectiles.Select(p => p.Actor)))
            {
                actor.Effects.CollectParticles(particles);
            }

            max = Math.Max(max, particles.Count);
        }

        return (sim, max);
    }

    [RequiresDatsFact]
    public void Flame_Bolt_VI_winds_up_launches_and_hits_the_drudge()
    {
        var (sim, maxParticles) = Play(85);
        Assert.True(sim.CastFinished);
        var bolt = Assert.Single(sim.Projectiles);
        Assert.True(bolt.Hidden);
        Assert.NotNull(bolt.ImpactTime);
        Assert.InRange(bolt.ImpactTime!.Value - bolt.LaunchTime, 0.2, 1.5); // ~8.5 m at 15 m/s
        Assert.Contains(sim.Events, e => e.Text.Contains("Zojak Quaguz", StringComparison.Ordinal));
        Assert.Contains(sim.Events, e => e.Text.Contains("launch script 0x3300011D", StringComparison.Ordinal));
        Assert.Contains(sim.Events, e => e.Text.Contains("explode script 0x33000120", StringComparison.Ordinal));
        Assert.Contains(sim.Events, e => e.Text.Contains("collision script 0x33000109", StringComparison.Ordinal)); // ProjectileCollision@1
        Assert.True(maxParticles > 50);
        Assert.Equal(MotionCommands.Ready, sim.Caster.Motion is MotionPlayer p ? p.State.Substate : 0);
        Assert.Empty(sim.Caster.Effects.Warnings);
    }

    [RequiresDatsFact]
    public void Self_buff_lands_on_the_caster_and_debuff_on_the_drudge()
    {
        var (heal, _) = Play(6);
        Assert.Null(heal.Target);
        Assert.Contains(heal.Events, e => e.Text.Contains("target HealthUpRed@0.05 → script 0x33000096", StringComparison.Ordinal));

        var (imperil, maxParticles) = Play(25);
        Assert.NotNull(imperil.Target);
        Assert.Contains(imperil.Events, e => e.Text.Contains("target ShieldDownGrey@0.05 → script 0x33000687", StringComparison.Ordinal));
        Assert.True(maxParticles > 0);
        Assert.Empty(imperil.Target!.Effects.Warnings); // the drudge's 17 parts satisfy every part index in the script
    }

    [RequiresDatsFact]
    public void Ring_spell_launches_nine_projectiles_around_the_caster()
    {
        var (ring, _) = Play(1785, seconds: 14.0);
        Assert.Equal(9, ring.Projectiles.Count);
        var dirs = ring.Projectiles.Select(p => Vector3.Normalize(p.Actor.Frame.Origin - CasterFrame.Origin) with { Z = 0 }).ToArray();
        Assert.Contains(dirs, d => d.Y < -0.5f); // some fly away from the target: a full 360° spread
        Assert.All(ring.Projectiles, p => Assert.NotNull(p.ImpactTime));
    }

    [RequiresDatsFact]
    public void Only_ring_projectiles_that_reach_the_target_play_its_target_effect()
    {
        // 1785 has no TargetEffect; give it one to observe ACE's rule (OnCollideObject → DoSpellEffects(projectileHit)).
        var (ring, _) = Play(1785, seconds: 14.0, edit: (resolver, v) => v with
        {
            TargetEffect = resolver.ResolveScript(v.Target.PhysicsScriptTableId, PlayScript.HealthDownRed, v.Plan.EffectIntensity),
        });
        var hits = ring.Events.Count(e => e.Text.StartsWith("projectile hit:", StringComparison.Ordinal));
        var timeouts = ring.Events.Count(e => e.Text.StartsWith("projectile timed out:", StringComparison.Ordinal));
        var targetEffects = ring.Events.Count(e => e.Text.StartsWith("target HealthDownRed", StringComparison.Ordinal));
        Assert.Equal(9, hits + timeouts);
        Assert.InRange(hits, 1, 8);       // the one aimed at the drudge hits; side projectiles fly away
        Assert.Equal(hits, targetEffects); // never at cast time, never on a timeout
    }

    [RequiresDatsFact]
    public void Projectile_spell_missing_from_the_extract_launches_nothing_and_plays_no_target_effect()
    {
        var (sim, _) = Play(4067, seconds: 6.0); // Mucor Bolt: TargetEffect HealthDownRed, weenie not extracted
        Assert.True(sim.Visual.IsProjectileSpell);
        Assert.Null(sim.Visual.Projectile);
        Assert.NotNull(sim.Visual.TargetEffect);
        Assert.NotEmpty(sim.Visual.Notes);
        Assert.Empty(sim.Projectiles);
        Assert.Contains(sim.Events, e => e.Text.StartsWith("no projectile:", StringComparison.Ordinal));
        Assert.DoesNotContain(sim.Events, e => e.Text.StartsWith("target ", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void Same_seed_same_particles()
    {
        static List<ParticleInstance> Snapshot(SpellCastSimulation sim)
        {
            var list = new List<ParticleInstance>();
            sim.Caster.Effects.CollectParticles(list);
            return list;
        }

        var (a, _) = Play(2645, seed: 9, seconds: 2.0);
        var (b, _) = Play(2645, seed: 9, seconds: 2.0);
        var (c, _) = Play(2645, seed: 10, seconds: 2.0);
        Assert.Equal(Snapshot(a), Snapshot(b));
        Assert.NotEqual(Snapshot(a), Snapshot(c));
    }
}
