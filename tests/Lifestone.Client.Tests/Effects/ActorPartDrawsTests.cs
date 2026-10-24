using System.Numerics;
using Lifestone.Client.Effects;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Effects;

/// <summary>
/// <see cref="ActorPartDraws.DegradesDisabled"/> (retail <c>Render::degrades_disabled</c>, which <c>CreatureMode::Render</c>
/// sets): on the user's dats, a human's parts degrade with distance in the world, and never in creature mode.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class ActorPartDrawsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Degrades_disabled_draws_every_part_at_level_zero_in_normal_mode_at_any_distance()
    {
        var dats = fixture.Dats;
        var meshes = new GfxObjMeshCache(dats);
        var assembly = ModelAssembly.Load(dats, VisualObject.HumanMale(dats).SetupId, meshes) ?? throw new KeyNotFoundException("human setup");
        var draws = ActorPartDraws.Create(dats, assembly, meshes);
        var actor = new EffectActor(new EffectObject(new NoEffects(), new RetailRandom(1)), null, assembly.Parts.Count, AcFrame.Identity, 1f, now: 0);
        Assert.False(draws.DegradesDisabled);

        // In the world, far away, at least one part leaves level 0 (a lower level, a billboard mode, or nothing at all).
        var viewer = new Vector3(0f, 400f, 0f);
        var far = draws.Compute(actor, viewer, isPlayer: false).ToArray();
        Assert.Contains(far, d => d.GfxObjId != draws.Chains[d.PartIndex].NearestGfxObjId || d.Mode != DegradeMode.Normal);

        draws.DegradesDisabled = true;
        foreach (var distance in new[] { 0.5f, 5f, 60f, 400f, 5000f })
        {
            var disabled = draws.Compute(actor, new Vector3(0f, distance, 0f), isPlayer: false).ToArray();
            Assert.Equal(assembly.Parts.Count, disabled.Length);
            foreach (var d in disabled.Where(d => assembly.Parts[d.PartIndex].IsGfxObj))
            {
                Assert.Equal(draws.Chains[d.PartIndex].NearestGfxObjId, d.GfxObjId);
                Assert.Equal(DegradeMode.Normal, d.Mode);
            }
        }

        // Retail draws the player's own parts the same way (level 0, mode 1), wherever the viewer is.
        draws.DegradesDisabled = false;
        var player = draws.Compute(actor, viewer, isPlayer: true).ToArray();
        draws.DegradesDisabled = true;
        var creatureMode = draws.Compute(actor, viewer, isPlayer: false).ToArray();
        Assert.Equal(player.Select(d => (d.GfxObjId, d.Mode)), creatureMode.Select(d => (d.GfxObjId, d.Mode)));

        // A hidden actor still draws nothing.
        Assert.All(draws.Compute(actor, viewer, isPlayer: false, hidden: true).ToArray(), d => Assert.Equal(0u, d.GfxObjId));
    }

    private sealed class NoEffects : IEffectSource
    {
        public PhysicsScriptData? GetScript(uint scriptId) => null;

        public PhysicsScriptTableData? GetScriptTable(uint tableId) => null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => null;
    }
}
