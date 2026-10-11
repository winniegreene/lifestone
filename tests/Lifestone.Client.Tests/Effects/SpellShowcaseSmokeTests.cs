using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// The spell viewer's whole engine-agnostic pipeline on the user's dats, without Godot: every showcase spell is cast on
/// a <see cref="SpellStage"/>, stepped at 30 fps, and every frame its actors are posed (<see cref="ActorPartDraws"/>) and
/// its particles batched (<see cref="ParticleDrawList"/>) from an orbiting viewer, exactly as the viewer does before
/// uploading. Nothing may throw, and what comes out must be drawable.
/// </summary>
public sealed class SpellShowcaseSmokeTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const double Step = 1.0 / 30.0;

    [RequiresDatsFact]
    public void Every_showcase_spell_casts_and_draws_without_exceptions()
    {
        var dats = fixture.Dats;
        var world = AceWorldExtract.Load();
        var resolver = SpellVisualResolver.Create(dats, world);
        var meshes = new GfxObjMeshCache(dats);
        var missing = new List<string>();
        var models = new DatParticleModels(dats, meshes) { Missing = (id, why) => missing.Add($"0x{id:X8}: {why}") };
        var drudge = world.Creatures.Single(c => c.Name == "Drudge Skulker");
        var (casterFrame, targetFrame) = SpellStage.FacingFrames(StageSite.CasterStand, StageSite.Heading, StageSite.Distance, (_, _) => 94f);
        var stage = new SpellStage(resolver, VisualObject.HumanMale(dats), VisualObject.FromCreature(drudge), casterFrame, targetFrame);
        var actorDraws = new Dictionary<uint, ActorPartDraws>();
        var list = new ParticleDrawList();
        var report = new List<string>();
        var mid = (StageSite.CasterStand + StageSite.TargetStand) * 0.5f;

        foreach (var entry in SpellStage.Showcase)
        {
            var sim = stage.Cast(entry.SpellId, entry.Fizzle);
            var maxDrawn = 0;
            var batches = new HashSet<uint>();
            for (var frame = 0; frame < 6 * 30; frame++)
            {
                stage.Advance(Step);
                var angle = frame * 0.05f;
                var viewer = new Vector3(mid.X, mid.Y, 95.5f) + new Vector3(MathF.Sin(angle) * 9f, -MathF.Cos(angle) * 9f, 0);

                foreach (var a in stage.Actors)
                {
                    if (!actorDraws.TryGetValue(a.Visual.SetupId, out var draws))
                    {
                        var assembly = ModelAssembly.Load(dats, a.Visual.SetupId, meshes) ?? throw new KeyNotFoundException($"Setup 0x{a.Visual.SetupId:X8}");
                        draws = ActorPartDraws.Create(dats, assembly, meshes);
                        actorDraws.Add(a.Visual.SetupId, draws);
                    }

                    foreach (var part in draws.Compute(a.Actor, viewer, a.Role == StageRole.Caster, a.Hidden, SpellStage.DrawScale(a.Actor)))
                    {
                        AssertFinite(part.Frame.Origin);
                        Assert.True(part.GfxObjId == 0 || meshes.Get(part.GfxObjId) is not null, $"part GfxObj 0x{part.GfxObjId:X8} has no mesh");
                    }
                }

                list.Clear();
                foreach (var fx in stage.EffectObjects)
                {
                    list.Add(fx, viewer, models);
                }

                maxDrawn = Math.Max(maxDrawn, list.Stats.Drawn);
                foreach (var batch in list.Batches)
                {
                    batches.Add(batch.Key.GfxObjId);
                    Assert.NotNull(meshes.Get(batch.Key.GfxObjId));
                    foreach (var d in batch.Draws)
                    {
                        AssertFinite(d.Origin);
                        Assert.InRange(d.Orientation.Length(), 0.999f, 1.001f);
                        Assert.InRange(d.Alpha, 0f, 1f);
                        Assert.InRange(d.Scale, 0.0999f, 10.001f); // start/final scales are clamped to [0.1, 10]; lerped in float
                    }
                }
            }

            report.Add($"{entry.Label}: max {maxDrawn} particles, GfxObjs {string.Join(" ", batches.Select(b => $"0x{b:X8}"))}");
            Assert.True(maxDrawn > 0, $"{entry.Label} drew no particle");
            Assert.Empty(stage.Caster.Effects.Warnings);
            if (sim.Visual.Projectile is not null && sim.Visual.Projectile.Kind != ProjectileKind.Ring)
            {
                Assert.All(sim.Projectiles, p => Assert.NotNull(p.ImpactTime));
            }

            if (entry.Fizzle)
            {
                Assert.Contains(sim.Events, e => e.Text.Contains("caster Fizzle@0.5 → script 0x33000103", StringComparison.Ordinal));
                Assert.Empty(sim.Projectiles);
            }
        }

        Assert.Empty(missing);
        Assert.Equal(16, report.Count);
    }

    [RequiresDatsFact]
    public void Showcase_particles_are_camera_facing_quads()
    {
        // Every particle GfxObj the showcase draws: level 0 is a billboard (mode 2) quad in its local XZ plane, except the
        // armour-shell shards (mode 1, oriented by their particles) and two meshes without a degrade table.
        var dats = fixture.Dats;
        var meshes = new GfxObjMeshCache(dats);
        var models = new DatParticleModels(dats, meshes);
        uint[] billboards = [0x010010F2, 0x01000FF4, 0x01000FBF, 0x01001101, 0x0100112F, 0x0100108E, 0x0100111A, 0x010010B2, 0x01001067, 0x01001098, 0x010010F9, 0x01001073, 0x01001062];
        foreach (var id in billboards)
        {
            var model = models.Get(id) ?? throw new KeyNotFoundException($"0x{id:X8}");
            Assert.Equal(DegradeMode.Billboard, model.Chain.Table!.Levels[0].Mode);
            var mesh = meshes.Get(model.Chain.NearestGfxObjId)!;
            Assert.All(mesh.Mesh.SubMeshes.SelectMany(s => s.Positions), p => Assert.Equal(0f, p.Y));
        }

        foreach (var id in new uint[] { 0x01001666, 0x01001667, 0x01001668, 0x01001669 })
        {
            Assert.Equal(DegradeMode.Normal, models.Get(id)!.Chain.Table!.Levels[0].Mode);
        }

        // Which ones need a separate batch for translucent particles: only surfaces that do not blend without a test
        // (frost's ClipMap|Alpha 0x08000312); the additive sprites and the SRCALPHA/INVSRCALPHA smoke and shards do not.
        Assert.True(models.Get(0x0100112F)!.PartMaterialSwitchesBlend);
        Assert.All(new uint[] { 0x010010F2, 0x01000FF4, 0x01000FBF, 0x01001666, 0x01001CB3 }, id => Assert.False(models.Get(id)!.PartMaterialSwitchesBlend));

        Assert.Null(models.Get(0x01001732)!.Chain.Table);
        Assert.Null(models.Get(0x01001CB3)!.Chain.Table);
    }

    private static void AssertFinite(Vector3 v) =>
        Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), $"non-finite {v}");
}
