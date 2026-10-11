using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests.Animation;

namespace Lifestone.Data.Tests.Magic;

/// <summary>Cast plans, the showcase spell list and the spell-visual chain, resolved against the user's dats.</summary>
public sealed class SpellVisualTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private SpellVisualResolver Resolver => _resolver ??= SpellVisualResolver.Create(fixture.Dats, World);

    private SpellVisualResolver? _resolver;

    private static AceWorldExtract World => _world ??= AceWorldExtract.LoadFile(
        AceWorldExtract.FindSourceFile() ?? throw new FileNotFoundException(AceWorldExtract.FileName));

    private static AceWorldExtract? _world;

    private VisualObject Human => VisualObject.HumanMale(fixture.Dats);

    private static VisualObject Drudge => VisualObject.FromCreature(World.Creatures.Single(c => c.Wcid == 7));

    /// <summary>
    /// The showcase list (docs/spell-visuals.md): spell, expected caster script, expected target script (on the
    /// drudge, or on the caster for self spells), expected projectile Launch script. 0 = none.
    /// </summary>
    public static TheoryData<uint, string, uint, uint, uint> Showcase => new()
    {
        { 27, "Flame Bolt I", 0, 0, 0x3300011B },
        { 85, "Flame Bolt VI", 0, 0, 0x3300011D },
        { 28, "Frost Bolt I", 0, 0, 0x33000115 },
        { 75, "Lightning Bolt I", 0, 0, 0x3300013A },
        { 1796, "Flame Streak I", 0, 0, 0x3300011B },
        { 2739, "Flame Arc I", 0, 0, 0x3300011B },
        { 6, "Heal Self I", 0, 0x33000096, 0 },
        { 2, "Strength Self I", 0, 0x33000046, 0 },
        { 24, "Armor Self I", 0, 0x3300068F, 0 },
        { 35, "Aura of Blood Drinker Self I", 0, 0x330000EF, 0 },
        { 25, "Imperil Other I", 0, 0x33000687, 0 },
        { 7, "Harm Other I", 0, 0x330001C9, 0 },
        { 2645, "Portal Recall", 0x33000040, 0, 0 },
        { 1635, "Lifestone Recall", 0x3300002B, 0, 0 },
    };

    [RequiresDatsTheory]
    [MemberData(nameof(Showcase))]
    public void Showcase_spells_resolve_to_the_documented_scripts(uint spellId, string name, uint casterScript, uint targetScript, uint launchScript)
    {
        var visual = Resolver.Resolve(spellId, Human, Drudge);
        Assert.Equal(name, visual.Spell.Name);
        Assert.Equal(casterScript, visual.CasterEffect?.ScriptId ?? 0);
        Assert.Equal(targetScript, visual.TargetEffect?.ScriptId ?? 0);
        Assert.Equal(launchScript, visual.Projectile?.Launch?.ScriptId ?? 0);
        Assert.Empty(visual.Notes);
        foreach (var resolved in new[] { visual.CasterEffect, visual.TargetEffect, visual.Projectile?.Launch, visual.Projectile?.Explode })
        {
            if (resolved is { ScriptId: not 0 })
            {
                Assert.NotEmpty(resolved.EmitterInfoIds);
                Assert.All(resolved.ParticleGfxObjIds, g => Assert.True(fixture.Dats.TryGetPortal<GfxObj>(g, out _), $"GfxObj 0x{g:X8}"));
            }
        }

        // Self spells land on the caster; "other" spells on the drudge (its own PhysicsScriptTable 0x3400001A).
        var self = (visual.Spell.Flags & SpellFlags.SelfTargeted) != 0;
        Assert.Equal(self ? Human.SetupId : Drudge.SetupId, visual.Target.SetupId);
        if (visual.TargetEffect is { } te)
        {
            Assert.Equal(self ? 0x34000004u : 0x3400001Au, te.TableId);
        }
    }

    [RequiresDatsFact]
    public void Flame_Bolt_VI_plan_follows_ACE_casting()
    {
        var visual = Resolver.Resolve(85, Human, Drudge);
        var plan = visual.Plan;
        Assert.Equal("Zojak Quaguz", plan.Words);
        Assert.Equal(1f, plan.EffectIntensity); // Pyreal scarab → ScarabScale 1.0

        var windup = plan.Steps[0];
        Assert.Equal(CastStepKind.Windup, windup.Kind);
        Assert.Equal(MotionCommands.MagicPowerUp10, windup.Motion);
        Assert.Equal(CastGesturePlanner.CastSpeed, windup.Speed);
        // ACE GetAnimationLength: link frames clamped to the 60-frame animation: (60/24 + 60/57) / 2.
        Assert.Equal((60.0 / 24 + 60.0 / 57) / 2, windup.ServerDuration, 4);

        var cast = plan.Steps[1];
        Assert.Equal(CastStepKind.CastGesture, cast.Kind);
        Assert.Equal(MotionCommands.MagicRecoilMissile, cast.Motion); // Birch Talisman
        Assert.Equal(windup.ServerDuration, cast.StartTime, 9);
        Assert.Equal(CastStepKind.SpellEffects, plan.Steps[2].Kind);
        Assert.Equal(cast.StartTime + cast.ServerDuration, plan.EffectTime, 9);
        Assert.Equal(MotionCommands.Ready, plan.Steps[3].Motion);
        Assert.Equal(1f, plan.Steps[3].Speed);

        var projectile = visual.Projectile!;
        Assert.Equal(ProjectileKind.Bolt, projectile.Kind);
        Assert.Equal(0x0200040Du, projectile.Source.SetupId);
        Assert.Equal(1f, projectile.ScriptIntensity); // level 6
        Assert.Equal(15f, projectile.Speed);
        Assert.Equal(0x33000120u, projectile.Explode!.ScriptId);
    }

    [Fact]
    public void Ace_spell_level_is_the_highest_level_whose_minimum_power_is_met()
    {
        // ACE Spell.Level over SpellFormula.MinPower {1, 50, 100, 150, 200, 250, 300, 400}.
        var cases = new (uint Power, int Level)[]
        {
            (0, 0), (1, 1), (49, 1), (50, 2), (99, 2), (100, 3), (149, 3), (150, 4), (199, 4), (200, 5), (249, 5), (250, 6),
            (275, 6), (299, 6), (300, 7), (320, 7), (399, 7), (400, 8), (10_000, 8),
        };
        Assert.All(cases, c => Assert.Equal(c.Level, SpellFormulaRules.AceSpellLevel(c.Power)));
    }

    [RequiresDatsFact]
    public void Projectile_script_intensity_follows_ACE_power_based_level_not_the_scarab_heuristic()
    {
        var spells = Resolver.Spells.Spells;

        // Rings at power 200 are level 5 → 0.8 (the scarab heuristic says 6 → 0.4); power 320 is level 7 → 1.0.
        Assert.Equal(0.8f, SpellVisualResolver.GetProjectileScriptIntensity(spells[1785], ProjectileKind.Ring));
        Assert.Equal(1.0f, SpellVisualResolver.GetProjectileScriptIntensity(spells[1832], ProjectileKind.Ring));
        Assert.Equal(0f, SpellVisualResolver.GetProjectileScriptIntensity(spells[57], ProjectileKind.Bolt));     // Magic Bolt, power 1
        Assert.Equal(1.0f, SpellVisualResolver.GetProjectileScriptIntensity(spells[1781], ProjectileKind.Blast)); // Exploding Magma, power 275
        Assert.Equal(0.4f, SpellVisualResolver.GetProjectileScriptIntensity(spells[1785], ProjectileKind.Wall));  // walls are always 0.4

        foreach (var id in new uint[] { 1785, 3806 })
        {
            var visual = Resolver.Resolve(id, Human, Drudge);
            Assert.True(visual.IsProjectileSpell);
            Assert.Equal(ProjectileKind.Ring, visual.Projectile!.Kind);
            Assert.Equal(0.8f, visual.Projectile.ScriptIntensity);
        }

        // docs/spell-visuals.md §5: the two level rules disagree for 250 of the 792 projectile spells.
        var projectiles = spells.Values.Where(SpellVisualResolver.IsProjectileMetaType).ToList();
        Assert.Equal(792, projectiles.Count);
        Assert.Equal(250, projectiles.Count(s => SpellFormulaRules.LevelByRoughHeuristic(s.Formula) != SpellFormulaRules.AceSpellLevel(s.Power)));
    }

    [RequiresDatsFact]
    public void Ace_animation_length_uses_the_style_links_only_when_the_current_link_set_exists()
    {
        var anims = Resolver.Animations;
        var table = anims.GetMotionTable(Human.MotionTableId)!;
        const uint style = MotionCommands.Magic;
        var styleKey = style << 16;
        var motion = table.Links[styleKey].Keys.First();
        var direct = CastGesturePlanner.AceAnimationLength(table, anims, style, motion, 0); // key == styleKey itself
        Assert.True(direct > 0);

        // ACE GetAnimData: the (stance, current) link set exists but lacks the motion → Links[stance << 16][motion].
        var current = table.Links.Keys.First(k => (k & 0xFFFF0000) == styleKey && k != styleKey && !table.Links[k].ContainsKey(motion)) & 0xFFFF;
        Assert.Equal(direct, CastGesturePlanner.AceAnimationLength(table, anims, style, motion, current));

        // No (stance, current) link set at all → nothing, without the style fallback.
        const uint noSuchCurrent = 0x4100FFFE;
        Assert.False(table.Links.ContainsKey(styleKey | (noSuchCurrent & 0xFFFFF)));
        Assert.Equal(0.0, CastGesturePlanner.AceAnimationLength(table, anims, style, motion, noSuchCurrent));
    }

    [RequiresDatsFact]
    public void Fast_cast_spells_have_no_windup_and_Lead_scarab_windups_are_no_ops()
    {
        var strength = Resolver.Resolve(2, Human, Drudge).Plan;           // FastCast
        Assert.DoesNotContain(strength.Steps, s => s.Kind == CastStepKind.Windup);
        Assert.Equal(MotionCommands.MagicSelfHead, strength.Steps[0].Motion);

        var heal = Resolver.Resolve(6, Human, Drudge).Plan;               // Lead scarab: ACE sends the invalid gesture
        var windup = Assert.Single(heal.Steps, s => s.Kind == CastStepKind.Windup);
        Assert.Equal(MotionCommands.RetailInvalid, windup.Motion);
        Assert.Equal(0.0, windup.ServerDuration);
        Assert.Equal(MotionCommands.MagicSelfHeart, heal.Steps[1].Motion);
    }

    [RequiresDatsFact]
    public void Component_gesture_time_is_the_ACE_length_of_its_links_except_three_components()
    {
        var table = Resolver.Animations.GetMotionTable(Human.MotionTableId)!;
        var mismatches = new SortedSet<uint>();
        foreach (var comp in Resolver.Components.Components.Values.Where(c => c.HasGesture))
        {
            var length = CastGesturePlanner.AceAnimationLength(table, Resolver.Animations, MotionCommands.Magic, comp.GestureMotion);
            Assert.True(length > 0, $"{comp.Name}: no Magic-stance link for its gesture");
            if (comp.Type == ComponentType.Talisman)
            {
                // A talisman's time also covers the way back: link gesture → Ready (ACE Monster_Magic times this part).
                length += CastGesturePlanner.AceAnimationLength(table, Resolver.Animations, MotionCommands.Magic, MotionCommands.Ready, comp.GestureMotion);
            }

            if (Math.Abs(length - comp.GestureTime) > 1e-4)
            {
                mismatches.Add(comp.Id);
            }
        }

        // Pyreal (6): its MagicPowerUp10 link asks for frames past the 60-frame animation, which ACE (and retail
        // set_animation_id) clamp, while the table's time is unclamped (75/24 + 75/57). Hazel (59): MagicPortal's link
        // plays at 30 fps but the time assumes 24 (19/24 + 41/30). Dark (192): time of PowerUp10, gesture PowerUp08Purple.
        Assert.Equal(new uint[] { 6, 59, 192 }, mismatches);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_spell_effect_resolves_through_the_human_and_drudge_tables()
    {
        var unresolved = new SortedSet<string>();
        var resolved = 0;
        foreach (var spell in Resolver.Spells.Spells.Values)
        {
            var intensity = SpellFormulaRules.AceEffectIntensity(spell.Formula) ?? 1f;
            foreach (var (type, table) in new[] { (spell.CasterEffect, 0x34000004u), (spell.TargetEffect, 0x34000004u), (spell.TargetEffect, 0x3400001Au) })
            {
                if (type == PlayScript.Invalid)
                {
                    continue;
                }

                var r = Resolver.ResolveScript(table, type, intensity);
                if (r.ScriptId == 0)
                {
                    unresolved.Add($"{type}@0x{table:X8}");
                    continue;
                }

                resolved++;
                Assert.All(r.ScriptClosure, s => Assert.NotNull(Resolver.Effects.GetScript(s)));
                Assert.All(r.EmitterInfoIds, e => Assert.NotNull(Resolver.Effects.GetEmitterInfo(e)));
            }
        }

        Assert.Equal(10944, resolved);

        // Every caster/target effect of all 6266 spells exists in the human table 0x34000004; the drudge's table lacks
        // only seven special-event PlayScripts.
        Assert.Equal(
            new[] { "BaelZharonSmite@0x3400001A", "BlackMadness@0x3400001A", "BunnySmite@0x3400001A", "CampingIneptitude@0x3400001A",
                "CampingMastery@0x3400001A", "LayingofHands@0x3400001A", "WeddingBliss@0x3400001A" },
            unresolved);
    }

    [RequiresDatsFact]
    public void Ace_world_extract_matches_the_dats()
    {
        Assert.Equal(13, World.Projectiles.Count);
        foreach (var p in World.Projectiles)
        {
            Assert.Equal(Resolver.Spells.Spells[p.SpellId].Name, p.SpellName);
            Assert.True(fixture.Dats.TryGetPortal<Setup>(p.SetupId, out _), $"setup 0x{p.SetupId:X8}");
            if (p.PhysicsEffectTableId is { } t)
            {
                Assert.NotNull(Resolver.Effects.GetScriptTable(t));
            }
        }

        var drudge = World.Creatures.Single();
        Assert.Equal("Drudge Skulker", drudge.Name);
        Assert.True(fixture.Dats.TryGetPortal<Setup>(drudge.SetupId, out var setup));
        Assert.Equal(17, setup.Parts.Count);
        Assert.NotNull(Resolver.Animations.GetMotionTable(drudge.MotionTableId));
        Assert.NotNull(Resolver.Effects.GetScriptTable(drudge.PhysicsEffectTableId));
    }

    [RequiresDatsFact]
    public void Viewer_loop_plays_Flame_Bolt_VI_from_windup_glow_to_projectile_explosion()
    {
        var visual = Resolver.Resolve(85, Human, Drudge);
        var anims = Resolver.Animations;
        var player = new MotionPlayer(anims.GetMotionTable(Human.MotionTableId)!, anims);
        Assert.True(fixture.Dats.TryGetPortal<Setup>(Human.SetupId, out var setup));
        var casterFx = new EffectObject(Resolver.Effects, new RetailRandom(7), Human.PhysicsScriptTableId);
        var caster = new EffectActor(casterFx, player, setup.Parts.Count, new AcFrame(new Vector3(96, 96, 40), Quaternion.Identity), Human.Scale, 0);

        // Enter the Magic stance first (the cast plan assumes it), then run the plan.
        player.ApplyInterpretedMotion(MotionCommands.Magic, MotionCommands.Ready, 1f);
        var t = 0.0;
        for (; t < 1.5; t += 1.0 / 60)
        {
            caster.AdvanceTo(t);
        }

        var start = t;
        EffectActor? bolt = null;
        var maxCasterParticles = 0;
        var sawProjectileParticles = false;
        var particles = new List<ParticleInstance>();
        var steps = new Queue<CastStep>(visual.Plan.Steps);
        for (; t < start + visual.Plan.EndTime + 2.0; t += 1.0 / 60)
        {
            while (steps.Count > 0 && steps.Peek().StartTime <= t - start)
            {
                var step = steps.Dequeue();
                if (step.Kind == CastStepKind.SpellEffects)
                {
                    var p = visual.Projectile!;
                    var boltFx = new EffectObject(Resolver.Effects, new RetailRandom(8), p.Source.PhysicsEffectTableId ?? 0);
                    var origin = caster.Frame.LocalToGlobal(new Vector3(0, 0.8f, 1.2f));
                    bolt = new EffectActor(boltFx, null, 1, new AcFrame(origin, Quaternion.Identity), p.Source.DefaultScale ?? 1f, t)
                    {
                        Velocity = new Vector3(0, p.Speed, 0),
                        AlignPath = true,
                        OnGround = false,
                    };
                    Assert.Equal(p.Launch!.ScriptId, boltFx.PlayScript(PlayScript.Launch, p.ScriptIntensity));
                }
                else
                {
                    player.ApplyInterpretedMotion(step.Style, step.Motion, step.Speed);
                }
            }

            caster.AdvanceTo(t);
            bolt?.AdvanceTo(t);
            particles.Clear();
            casterFx.CollectParticles(particles);
            maxCasterParticles = Math.Max(maxCasterParticles, particles.Count);
            if (bolt is not null)
            {
                particles.Clear();
                bolt.Effects.CollectParticles(particles);
                sawProjectileParticles |= particles.Count > 0;
            }
        }

        Assert.True(maxCasterParticles > 50, $"wind-up glow peaked at {maxCasterParticles} particles");
        Assert.True(sawProjectileParticles, "the Launch script attached no particles to the bolt");
        Assert.NotNull(bolt);
        Assert.True(bolt.Frame.Origin.Y > 96 + 20, "the bolt flew forward at its MaximumVelocity");
        Assert.Equal(MotionCommands.Magic, player.State.Style);
        Assert.Equal(MotionCommands.Ready, player.State.Substate);
        Assert.Empty(casterFx.Warnings);
    }
}
