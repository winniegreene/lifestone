using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using DatScript = DatReaderWriter.DBObjs.PhysicsScript;

namespace Lifestone.Data.Tests.Effects;

/// <summary>PhysicsScripts (0x33), PhysicsScriptTables (0x34) and ParticleEmitterInfos (0x32) from the user's portal.</summary>
public sealed class PhysicsScriptTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatEffectSource Source => _source ??= new DatEffectSource(fixture.Dats);

    private DatEffectSource? _source;

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_physics_script_decodes_exactly()
    {
        var ids = Source.AllScriptIds();
        Assert.Equal(4248, ids.Count); // docs/research/dat-format.md §0

        var hookCounts = new SortedDictionary<AnimHookType, int>();
        var blockingScripts = new List<uint>();
        var blockingNotLast = 0;
        var scriptsWithTies = 0;
        var tiesOver8Hooks = 0;
        foreach (var id in ids)
        {
            var script = Source.GetScript(id)!; // Decode throws unless every byte (and the final alignment) is consumed
            Assert.True(script.FileOrderSorted, $"0x{id:X8} is not stored in start-time order");
            Assert.All(script.Hooks, h => Assert.True(h.StartTime >= 0 && double.IsFinite(h.StartTime)));
            var ties = script.Hooks.GroupBy(h => h.StartTime).Where(g => g.Count() > 1).ToList();
            if (ties.Count > 0)
            {
                scriptsWithTies++;
                if (script.Hooks.Count > 8)
                {
                    tiesOver8Hooks++; // MSVC qsort's short-array pass leaves sorted runs of ≤ 8 untouched; longer arrays may reorder ties
                }

                // No tie ever creates/stops/destroys the same emitter id twice, so a different tie order can only change
                // which emitter draws random numbers first (docs/spell-visuals.md, "Known deviations").
                foreach (var group in ties)
                {
                    var emitterIds = group.Select(h => h.Hook switch
                    {
                        CreateParticleHook c => c.EmitterId,
                        CreateBlockingParticleHook c => c.EmitterId,
                        DestroyParticleHook d => d.EmitterId,
                        StopParticleHook d => d.EmitterId,
                        _ => 0u,
                    }).Where(i => i != 0).ToList();
                    Assert.Equal(emitterIds.Count, emitterIds.Distinct().Count());
                }
            }

            foreach (var entry in script.Hooks)
            {
                hookCounts[entry.Hook.Type] = hookCounts.GetValueOrDefault(entry.Hook.Type) + 1;
                if (entry.Hook is CallPesHook call)
                {
                    Assert.NotNull(Source.GetScript(call.ScriptId));
                }
            }

            foreach (var blocking in script.Hooks.Select(h => h.Hook).OfType<CreateBlockingParticleHook>())
            {
                blockingScripts.Add(id);
                Assert.NotNull(Source.GetEmitterInfo(blocking.EmitterInfoId)); // the 40-byte payload is real data
                if (blocking != script.Hooks[^1].Hook)
                {
                    blockingNotLast++;
                }
            }

            Assert.True(TryDrw(id, out var drw), $"DatReaderWriter cannot read 0x{id:X8}");
            Assert.True(SameAsDrw(script, drw!), $"0x{id:X8} differs from DatReaderWriter");
        }

        // Hook types present in end-of-retail PhysicsScripts (12 of 27), with their counts.
        Assert.Equal(
            new SortedDictionary<AnimHookType, int>
            {
                [AnimHookType.SoundTable] = 626,
                [AnimHookType.Scale] = 122,
                [AnimHookType.CreateParticle] = 10743,
                [AnimHookType.DestroyParticle] = 1,
                [AnimHookType.StopParticle] = 15,
                [AnimHookType.NoDraw] = 2,
                [AnimHookType.CallPES] = 354,
                [AnimHookType.Transparent] = 10,
                [AnimHookType.SoundTweaked] = 368,
                [AnimHookType.TextureVelocity] = 11,
                [AnimHookType.SetLight] = 1,
                [AnimHookType.CreateBlockingParticle] = 15,
            },
            hookCounts);

        // DatReaderWriter 2.1.9's CreateBlockingParticleHook has no fields and reads no payload, while retail reads the
        // same 40 bytes as CreateParticle. In the end-of-retail dats every such hook is its script's last entry, so the
        // types and times above still agree, but DatReaderWriter loses the emitter id/part/offset (and would misalign a
        // following hook). This is why hooks are decoded by AnimHookDecoder.
        Assert.Equal(15, blockingScripts.Count);
        Assert.Equal(0, blockingNotLast);
        Assert.Empty(typeof(DatReaderWriter.Types.CreateBlockingParticleHook).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
        Assert.Equal(2294, scriptsWithTies);
        Assert.Equal(166, tiesOver8Hooks);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_script_table_resolves_to_existing_scripts()
    {
        var ids = Source.AllScriptTableIds();
        Assert.Equal(164, ids.Count);
        foreach (var id in ids)
        {
            var table = Source.GetScriptTable(id)!;
            foreach (var (type, entries) in table.Scripts)
            {
                Assert.NotEmpty(entries);
                foreach (var e in entries)
                {
                    Assert.True(Source.GetScript(e.ScriptId) is not null, $"table 0x{id:X8} {type}: script 0x{e.ScriptId:X8} missing");
                }
            }
        }
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_emitter_info_is_sane_and_uses_eleven_particle_types()
    {
        var ids = Source.AllEmitterInfoIds();
        Assert.Equal(2051, ids.Count);
        var kinds = new SortedSet<ParticleKind>();
        var emitterKinds = new SortedSet<EmitterKind>();
        var lifespanAtLeastRand = 0;
        foreach (var id in ids)
        {
            var e = Source.GetEmitterInfo(id)!;
            kinds.Add(e.ParticleType);
            emitterKinds.Add(e.EmitterType);
            Assert.InRange((int)e.ParticleType, 1, 12);
            Assert.True(e.MaxParticles >= 0 && e.InitialParticles >= 0 && e.TotalParticles >= 0);
            Assert.True(e.Lifespan >= 0 && e.Birthrate >= 0 && e.TotalSeconds >= 0);
            Assert.True(e.HwGfxObjId == 0 || (e.HwGfxObjId & 0xFF000000) == 0x01000000, $"0x{id:X8}: HW GfxObj 0x{e.HwGfxObjId:X8}");
            if (e.Lifespan >= e.LifespanRand)
            {
                lifespanAtLeastRand++;
            }
        }

        // ParticleType 10 (ParabolicGVGA) exists in the enum but no emitter uses it.
        Assert.Equal(
            new[]
            {
                ParticleKind.Still, ParticleKind.LocalVelocity, ParticleKind.ParabolicLVGA, ParticleKind.ParabolicLVGAGR, ParticleKind.Swarm,
                ParticleKind.Explode, ParticleKind.Implode, ParticleKind.ParabolicLVLA, ParticleKind.ParabolicLVLALR, ParticleKind.ParabolicGVGAGR,
                ParticleKind.GlobalVelocity,
            }.Order(),
            kinds);
        Assert.Equal(new[] { EmitterKind.BirthratePerSec, EmitterKind.BirthratePerMeter }.Order(), emitterKinds);

        // Field-order sanity: the first double after TotalSeconds is the lifespan (normally ≥ its random spread).
        Assert.True(lifespanAtLeastRand > ids.Count * 0.9);
    }

    [RequiresDatsFact]
    public void Script_table_lookup_takes_the_first_threshold_at_or_above_the_intensity()
    {
        // Human PhysicsScriptTable 0x34000004, AttribUpRed: [0 → 0x3300007A] [0.5 → 0x33000046] [1 → 0x33000047].
        var table = Source.GetScriptTable(0x34000004)!;
        Assert.Equal(0x3300007Au, table.GetScript(PlayScript.AttribUpRed, 0f));
        Assert.Equal(0x33000046u, table.GetScript(PlayScript.AttribUpRed, 0.05f)); // Lead-scarab spells
        Assert.Equal(0x33000046u, table.GetScript(PlayScript.AttribUpRed, 0.5f));
        Assert.Equal(0x33000047u, table.GetScript(PlayScript.AttribUpRed, 0.6f));
        Assert.Equal(0x33000047u, table.GetScript(PlayScript.AttribUpRed, 1f));
        Assert.Equal(0u, table.GetScript(PlayScript.AttribUpRed, 1.01f)); // above every threshold: nothing
        Assert.Equal(0u, table.GetScript(PlayScript.Launch, 0f));          // type not in the table
    }

    [Fact]
    public void Script_manager_queues_scripts_back_to_back_and_fires_hooks_on_time()
    {
        var first = Script(1, (0.0, 11u), (0.5, 12u));
        var second = Script(2, (0.0, 21u), (0.25, 22u));
        var manager = new ScriptManager();
        var fired = new List<(double Time, uint Id)>();
        manager.AddScript(first, 10.0);
        manager.AddScript(second, 10.1); // starts at 10.0 + first.Length (0.5), not now
        Assert.Equal(new[] { (1u, 10.0), (2u, 10.5) }, manager.Queue.ToArray());

        foreach (var t in new[] { 10.0, 10.3, 10.5, 10.6, 10.75, 10.8 })
        {
            manager.UpdateScripts(t, h => fired.Add((t, ((SoundHook)h).SoundId)));
        }

        Assert.Equal(new[] { (10.0, 11u), (10.5, 12u), (10.5, 21u), (10.75, 22u) }, fired);
        Assert.True(manager.IsIdle);
    }

    [Fact]
    public void Call_pes_plays_the_nested_script_after_a_random_delay_up_to_pause()
    {
        var source = new ParticleSimulationTests.FakeEffects();
        source.Scripts[5] = Script(5, (0.0, 55u));
        var fx = new EffectObject(source, new RetailRandom(3));
        fx.SetPlacement(0, AcFrame.Identity, []);
        fx.ExecuteHook(new CallPesHook(AnimHookDirection.Both, 5, 1.0f));
        Assert.True(fx.Scripts.IsIdle); // the FPHook has not reached 1.0 yet

        var playedAt = -1.0;
        for (var i = 1; i <= 70 && playedAt < 0; i++)
        {
            var t = i / 60.0;
            fx.Update(t, AcFrame.Identity, []);
            if (fx.DrainEvents().Any(e => e.Hook is SoundHook { SoundId: 55 }))
            {
                playedAt = t;
            }
        }

        Assert.InRange(playedAt, 0.0, 1.1);

        fx.ExecuteHook(new CallPesHook(AnimHookDirection.Both, 5, 0f)); // pause < ε: queued at once
        Assert.False(fx.Scripts.IsIdle);
    }

    [Fact]
    public void Interpolated_object_translucency_never_drops_below_the_original()
    {
        // Retail process_fp_hook case 1 → CPhysicsObj::SetTranslucencyInternal clamps to translucencyOriginal, like the
        // immediate path of SetTranslucency2.
        var fx = new EffectObject(new ParticleSimulationTests.FakeEffects(), new RetailRandom(1));
        fx.SetPlacement(0, AcFrame.Identity, []);
        fx.Visual.TranslucencyOriginal = 0.5f;
        fx.ExecuteHook(new TransparentHook(AnimHookDirection.Both, 1f, 0f, 1f)); // fade 1 → 0 over 1 s

        fx.Update(0.25, AcFrame.Identity, []);
        Assert.Equal(0.75f, fx.Visual.Translucency, 5);
        fx.Update(0.75, AcFrame.Identity, []);
        Assert.Equal(0.5f, fx.Visual.Translucency); // 0.25 interpolated, clamped
        fx.Update(2.0, AcFrame.Identity, []);
        Assert.Equal(0.5f, fx.Visual.Translucency);

        fx.ExecuteHook(new TransparentHook(AnimHookDirection.Both, 1f, 0f, 0f)); // immediate path, same clamp
        Assert.Equal(0.5f, fx.Visual.Translucency);
    }

    internal static PhysicsScriptData Script(uint id, params (double Time, uint SoundId)[] hooks)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(id);
        w.Write((uint)hooks.Length);
        foreach (var (time, sound) in hooks)
        {
            w.Write(time);
            w.Write((uint)AnimHookType.Sound);
            w.Write(0);
            w.Write(sound);
        }

        return PhysicsScriptData.Decode(id, ms.ToArray());
    }

    private bool TryDrw(uint id, out DatScript? script)
    {
        try
        {
            var ok = fixture.Dats.TryGetPortal<DatScript>(id, out var s);
            script = s;
            return ok;
        }
        catch (Exception)
        {
            script = null;
            return false;
        }
    }

    private static bool SameAsDrw(PhysicsScriptData mine, DatScript drw)
    {
        if (drw.ScriptData.Count != mine.Hooks.Count)
        {
            return false;
        }

        for (var i = 0; i < mine.Hooks.Count; i++)
        {
            if (drw.ScriptData[i].StartTime != mine.Hooks[i].StartTime || (uint)drw.ScriptData[i].Hook.HookType != (uint)mine.Hooks[i].Hook.Type)
            {
                return false;
            }
        }

        return true;
    }
}
