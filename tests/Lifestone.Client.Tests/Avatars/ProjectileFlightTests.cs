using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Client.Avatars;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using Xunit.Abstractions;
using DatFrame = DatReaderWriter.Types.Frame;
using DatPlayScript = DatReaderWriter.Enums.PlayScript;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 3.1 lane fc, item 1 and 2 and the counters of item 4: a spell projectile as the avatar set moves it. Retail moves a Missile object like any other (<c>CPhysicsObj::UpdateObjectInternal</c>, 11.6096 0x005161B0,
/// then <c>UpdatePhysicsInternal</c> 0x005111D0): its own velocity from the create and the vector updates, gravity only when the state says Gravity (<c>calc_acceleration</c> 0x00511420), no collision transition for a
/// server object (D24), so a bolt flies until the server's hit messages (the state change, the Explode script, the zero vector) and is removed by F747 (ACE keeps it five seconds after the hit). The synthetic cases need no
/// dats; the real ones (the ACE world extract's 13 war-spell projectiles, 9 Setups) skip without them.
/// </summary>
public sealed class ProjectileFlightTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Cell = AvatarRig.HoltburgCell;
    private const uint BoltSetup = 0x02FF0101;
    private const uint SpherelessSetup = 0x02FF0102;
    private const uint FullTable = 0x34FF0101;
    private const uint LaunchOnlyTable = 0x34FF0102;
    private const uint LaunchScript = 0x33FF0101;
    private const uint ExplodeScript = 0x33FF0102;
    private const uint Bolt = 0x80000301;
    private const uint Bolt2 = 0x80000302;
    private const uint Npc = 0x80000303;
    private const double Step = 1.0 / 30.0;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 10)));

    /// <summary>What ACE gives a spell projectile (SetProjectilePhysicsState; the weenie's state): Missile, AlignPath and the rest of its bits, no Gravity.</summary>
    private const uint BoltState = AvatarStateBits.Missile | AvatarStateBits.AlignPath | AvatarStateBits.ReportCollisions;

    /// <summary>What <c>SpellProjectile.ProjectileImpact</c> leaves (WorldObject_Magic / SpellProjectile.cs 209-248): not reporting collisions, IgnoreCollisions, NoDraw, the lights off; Missile stays.</summary>
    private const uint HitState = AvatarStateBits.Missile | AvatarStateBits.AlignPath | AvatarStateBits.IgnoreCollisions | AvatarStateBits.NoDraw;

    private static AvatarSet NewSet(bool retailSphereless = false)
    {
        var setups = new Dictionary<uint, ModelAssembly>
        {
            [BoltSetup] = SetupOf(BoltSetup, withSphere: true),
            [SpherelessSetup] = SetupOf(SpherelessSetup, withSphere: false),
        };
        var sources = new AvatarSources(id => setups.GetValueOrDefault(id), new FlatAppearance(), new AvatarRig.RigAnimations(), _ => null, new BoltEffects());
        return new AvatarSet(sources) { RetailSpherelessMissile = retailSphereless };
    }

    private static ModelAssembly SetupOf(uint id, bool withSphere)
    {
        var setup = new Setup { Height = 0.5f, Radius = 0.1f };
        setup.Parts.Add(0x01FF0301);
        if (withSphere)
        {
            setup.Spheres.Add(new Sphere { Origin = Vector3.Zero, Radius = 0.1f });
        }

        var frame = new AnimationFrame(1);
        frame.Frames.Add(new DatFrame { Origin = Vector3.Zero, Orientation = Quaternion.Identity });
        setup.PlacementFrames.Add(Placement.Default, frame);
        return ModelAssembly.FromSetup(id, setup);
    }

    private static AvatarCreate BoltAt(uint guid, Vector3 velocity, uint state = BoltState, uint setup = BoltSetup, uint table = FullTable) => new()
    {
        Guid = guid,
        SetupId = setup,
        PhysicsState = state,
        Velocity = velocity,
        PhysicsScriptTableId = table,
        Position = AvatarRig.At(new Vector3(96, 96, 10)),
    };

    private static double Run(AvatarSet set, double from, int frames)
    {
        var now = from;
        for (var i = 0; i < frames; i++)
        {
            now += Step;
            set.Advance(now, Viewer, long.MaxValue);
        }

        return now;
    }

    [Fact]
    public void A_bolt_created_with_a_velocity_moves_by_it_each_step_and_stops_on_the_hits_vector_change()
    {
        var set = NewSet();
        Assert.Equal(AvatarApply.Applied, set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0));
        Assert.True(set.TryGet(Bolt, out var bolt));
        var last = bolt!.Body.Position.Origin;
        var now = 0.0;
        for (var i = 0; i < 30; i++)
        {
            now = Run(set, now, 1);
            var here = bolt.Body.Position.Origin;
            AvatarAssert.Near(new Vector3(0, 10f * (float)Step, 0), here - last, 0.02f); // 1/3 m a frame, whatever the cell
            last = here;
        }

        AvatarAssert.Near(new Vector3(96, 106, 10), bolt.Body.Position.Origin, 0.1f); // ten metres in the second it flew

        // The hit: the server's state change (NoDraw, not reporting collisions) and the zero vector. The bolt is not drawn and does not move on.
        Assert.Equal(AvatarApply.Applied, set.SetState(Bolt, HitState));
        Assert.Equal(AvatarApply.Applied, set.SetVectors(Bolt, Vector3.Zero, Vector3.Zero));
        now = Run(set, now, 1);
        Assert.False(bolt.Body.IsDrawn);
        var stopped = bolt.Body.Position.Origin;
        now = Run(set, now, 30);
        AvatarAssert.Near(stopped, bolt.Body.Position.Origin, 1e-4f);
        Assert.Equal(Vector3.Zero, bolt.Body.VelocityVector);
        Assert.True(bolt.Body.IsProjectile, "the hit's state keeps the Missile bit, and the flag is sticky");
        _ = now;
    }

    [Fact]
    public void A_bolt_has_no_gravity_unless_its_state_says_Gravity_and_then_falls_by_it()
    {
        var set = NewSet();
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        set.Create(BoltAt(Bolt2, new Vector3(0, 10, 0), BoltState | AvatarStateBits.Gravity), 0);
        Assert.True(set.TryGet(Bolt, out var flat));
        Assert.True(set.TryGet(Bolt2, out var arc));
        Run(set, 0, 30);
        Assert.Equal(10f, flat!.Body.Position.Origin.Z, 0.01f); // straight: Z unchanged
        Assert.Equal(Vector3.Zero, flat.Body.Acceleration);
        Assert.Equal(new Vector3(0, 0, -9.8f), arc!.Body.Acceleration); // PhysicsGlobals::gravity, calc_acceleration
        var fall = 10f - arc.Body.Position.Origin.Z;
        Assert.InRange(fall, 4.5f, 5.2f); // 1/2 g t^2 = 4.9 m in the second
        AvatarAssert.Near(new Vector3(96, 106, 10), flat.Body.Position.Origin with { Z = 10 }, 0.1f);
        Assert.InRange(arc.Body.Position.Origin.Y, 105.9f, 106.1f);

        // Gravity cleared by a state change stops the pull (calc_acceleration runs on the state change).
        set.SetState(Bolt2, BoltState);
        Assert.Equal(Vector3.Zero, arc.Body.Acceleration);
    }

    [Fact]
    public void The_launch_and_explode_scripts_are_counted_from_the_projectiles_own_table_and_a_missing_type_is_counted_once_logged()
    {
        var log = new Lifestone.Client.Tests.RecordingLogSink();
        var set = NewSet();
        set = new AvatarSet(set.Sources, log);
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        set.Create(BoltAt(Bolt2, new Vector3(0, 10, 0), table: LaunchOnlyTable), 0);
        set.Create(new AvatarCreate { Guid = Npc, SetupId = BoltSetup, PhysicsScriptTableId = FullTable, Position = AvatarRig.At(new Vector3(90, 90, 10)) }, 0);

        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt, (uint)DatPlayScript.Launch, 1f));
        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt, (uint)DatPlayScript.Explode, 1f));
        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt2, (uint)DatPlayScript.Launch, 1f));
        Assert.Equal(AvatarApply.Ignored, set.PlayScript(Bolt2, (uint)DatPlayScript.Explode, 1f)); // its table has no Explode: retail's play_script does nothing
        Assert.Equal(AvatarApply.Ignored, set.PlayScript(Bolt2, (uint)DatPlayScript.Explode, 1f));
        set.PlayScript(Npc, (uint)DatPlayScript.Launch, 1f); // not a projectile: not counted as one

        var stats = set.Stats;
        Assert.Equal(2, stats.ProjectileLaunches);
        Assert.Equal(3, stats.ProjectileExplodes); // the count is the F755s received for a projectile
        Assert.Equal(2, stats.ProjectileScriptsMissing);
        Assert.Equal(4, stats.ServerScriptsPlayed); // Launch + Explode on the first bolt, Launch on the second, Launch on the NPC
        Assert.Equal(2, stats.ServerScriptsMissing);
        Assert.Single(log.Entries, e => e.Message.Contains("is not in the body's PhysicsScriptTable", StringComparison.Ordinal)); // logged once per table and type
    }

    [Fact]
    public void The_projectile_window_is_open_while_any_projectile_lives_and_F747_closes_it()
    {
        var set = NewSet();
        Assert.False(set.ProjectileWindowOpen);
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        Assert.True(set.ProjectileWindowOpen);
        set.Create(BoltAt(Bolt2, new Vector3(0, 10, 0)), 0);
        Assert.Equal(2, set.LiveProjectiles);
        Assert.Equal(1, set.Stats.ProjectileWindows); // one window over both
        Assert.Equal(2, set.Stats.ProjectilesCreated);
        Assert.Equal(Bolt2, set.LastProjectileGuid);
        set.Create(new AvatarCreate { Guid = Npc, SetupId = BoltSetup, Position = AvatarRig.At(new Vector3(90, 90, 10)) }, 0);
        Assert.Equal(2, set.Stats.ProjectilesCreated); // an ordinary object is not a projectile
        Assert.Equal(2, set.LiveProjectiles);

        Assert.Equal(AvatarApply.Applied, set.Delete(Bolt)); // F747
        Assert.True(set.ProjectileWindowOpen);
        Assert.Equal(AvatarApply.Applied, set.Delete(Bolt2));
        Assert.False(set.ProjectileWindowOpen);
        Assert.False(set.TryGet(Bolt, out _));
        set.Delete(Npc);
        Assert.Equal(0, set.LiveProjectiles);

        // A recreate (the same guid again) does not leave the window counting the old body twice.
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0, recreate: true);
        Assert.Equal(1, set.LiveProjectiles);
        set.Delete(Bolt);
        Assert.False(set.ProjectileWindowOpen);
        Assert.Equal(3, set.Stats.ProjectileWindows); // the first, the second, and the recreate's (the old body left, so the window closed and opened again)
    }

    [Fact]
    public void A_projectile_is_counted_drawn_once_when_the_layer_first_draws_it_and_an_ordinary_object_never()
    {
        var set = NewSet();
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        set.Create(new AvatarCreate { Guid = Npc, SetupId = BoltSetup, Position = AvatarRig.At(new Vector3(90, 90, 10)) }, 0);
        Assert.True(set.TryGet(Bolt, out var bolt));
        Assert.True(set.TryGet(Npc, out var npc));
        Assert.Equal(0, set.Stats.ProjectilesShown); // created but not shown yet
        set.NoteDrawn(npc!);
        Assert.Equal(0, set.Stats.ProjectilesShown);
        for (var i = 0; i < 5; i++)
        {
            set.NoteDrawn(bolt!);
        }

        Assert.Equal(1, set.Stats.ProjectilesShown);
    }

    [Fact]
    public void A_projectile_Setup_without_spheres_still_flies_by_default_and_retails_frozen_origin_is_the_flag()
    {
        // Retail: CPhysicsObj::UpdateObjectInternal (11.6096 0x005161B0) puts the old origin back for a Setup with CPartArray::GetNumSphere (0x00518B90) 0, whatever the object's state, so a sphereless
        // Missile never leaves where it was created. The default here is the glitch fix (a bolt hanging at the caster is not worth reproducing); --retail-sphereless-missile restores retail.
        var log = new Lifestone.Client.Tests.RecordingLogSink();
        var set = new AvatarSet(NewSet().Sources, log);
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0), setup: SpherelessSetup), 0);
        set.Create(BoltAt(Bolt2, new Vector3(0, 10, 0), setup: SpherelessSetup), 0);
        set.Create(new AvatarCreate { Guid = Npc, SetupId = SpherelessSetup, PhysicsState = 0, Velocity = new Vector3(0, 10, 0), Position = AvatarRig.At(new Vector3(90, 90, 10)) }, 0);
        Run(set, 0, 30);
        Assert.True(set.TryGet(Bolt, out var bolt));
        Assert.True(set.TryGet(Npc, out var npc));
        AvatarAssert.Near(new Vector3(96, 106, 10), bolt!.Body.Position.Origin, 0.1f);
        AvatarAssert.Near(new Vector3(90, 90, 10), npc!.Body.Position.Origin, 1e-4f); // an ordinary sphereless object stays: retail's branch is unchanged for it
        Assert.Single(log.Entries, e => e.Message.Contains("glitch fix (--retail-sphereless-missile restores retail)", StringComparison.Ordinal)); // named once per Setup

        var retail = new AvatarSet(NewSet().Sources) { RetailSpherelessMissile = true };
        retail.Create(BoltAt(Bolt, new Vector3(0, 10, 0), setup: SpherelessSetup), 0);
        Run(retail, 0, 30);
        Assert.True(retail.TryGet(Bolt, out var frozen));
        AvatarAssert.Near(new Vector3(96, 96, 10), frozen!.Body.Position.Origin, 1e-4f);
    }

    [Fact]
    public void A_state_with_NoDraw_hides_the_bolt_and_the_vector_update_alone_stills_it()
    {
        var set = NewSet();
        set.Create(BoltAt(Bolt, new Vector3(0, 10, 0)), 0);
        Assert.True(set.TryGet(Bolt, out var bolt));
        var now = Run(set, 0, 3);
        Assert.True(bolt!.Body.IsDrawn);
        set.SetVectors(Bolt, Vector3.Zero, Vector3.Zero);
        var at = bolt.Body.Position.Origin;
        Run(set, now, 10);
        AvatarAssert.Near(at, bolt.Body.Position.Origin, 1e-4f);
        Assert.True(bolt.Body.IsDrawn, "a stilled bolt is still drawn until the state change says NoDraw");
    }

    [RequiresDatsFact]
    public void Every_war_spell_projectile_Setup_has_a_sphere_flies_by_its_velocity_and_the_defaults_script_is_queued_at_create()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var extract = AceWorldExtract.Load();
        var setups = extract.Projectiles.Select(p => p.SetupId).Distinct().ToArray();
        Assert.Equal(13, extract.Projectiles.Count);
        Assert.Equal(9, setups.Length);
        var withDefaultScript = 0;
        foreach (var projectile in extract.Projectiles.DistinctBy(p => p.SetupId))
        {
            var set = new AvatarSet(sources);
            var setup = sources.GetSetup(projectile.SetupId);
            Assert.NotNull(setup);
            Assert.True(setup!.Spheres.Count > 0, $"{projectile.SpellName}: retail's sphereless branch would freeze this bolt");
            Assert.Equal(AvatarApply.Applied, set.Create(BoltAt(Bolt, new Vector3(0, 12, 0), table: projectile.PhysicsEffectTableId ?? 0, setup: projectile.SetupId), 0));
            Assert.True(set.TryGet(Bolt, out var bolt));
            if (setup.DefaultScriptId != 0)
            {
                withDefaultScript++;
                Assert.Contains(bolt!.Body.Effects.Scripts.Queue, q => q.ScriptId == setup.DefaultScriptId); // the Setup's default script, played at create (CPhysicsObj::InitDefaults)
            }

            Run(set, 0, 30);
            var moved = bolt!.Body.Position.Origin.Y - 96f;
            Assert.InRange(moved, 11.8f, 12.2f);
            output.WriteLine($"{projectile.SpellName}: setup {projectile.SetupId:X8} parts={setup.Parts.Count} spheres={setup.Spheres.Count} defaultScript={setup.DefaultScriptId:X8} table={projectile.PhysicsEffectTableId:X8} moved={moved:0.00} m in 1 s particles={bolt.Body.Effects.Particles.Count}");
        }

        Assert.True(withDefaultScript >= 2, "the Lightning Bolt and the rings carry a default script");
    }

    [RequiresDatsFact]
    public void The_flame_bolts_own_table_plays_Launch_and_Explode_and_the_launch_script_makes_particles()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var flame = AceWorldExtract.Load().ProjectilesBySpell[27];
        var set = new AvatarSet(sources);
        set.Create(BoltAt(Bolt, new Vector3(0, 15, 0), table: flame.PhysicsEffectTableId ?? 0, setup: flame.SetupId), 0);
        Assert.True(set.TryGet(Bolt, out var bolt));
        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt, (uint)DatPlayScript.Launch, 1f));
        var now = Run(set, 0, 6);
        var trail = bolt!.Body.Effects.Particles.Count;
        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt, (uint)DatPlayScript.Explode, 1f));
        Run(set, now, 3);
        output.WriteLine($"Flame Bolt I table {flame.PhysicsEffectTableId:X8}: launches={set.Stats.ProjectileLaunches} explodes={set.Stats.ProjectileExplodes} missing={set.Stats.ProjectileScriptsMissing} trail particles after 6 frames={trail}");
        Assert.Equal(1, set.Stats.ProjectileLaunches);
        Assert.Equal(1, set.Stats.ProjectileExplodes);
        Assert.Equal(0, set.Stats.ProjectileScriptsMissing);
        Assert.True(trail > 0, "the Launch script of the flame table makes the bolt's trail emitters");
    }

    [RequiresDatsFact]
    public void The_flame_bolts_part_degrades_to_nothing_so_its_look_is_its_scripts_particles()
    {
        // Pins why the layer counts a projectile as drawn when it is shown and not when a part has a mesh: Setup 0x0200040D's one part, GfxObj 0x010001EC, has a degrade table whose second level draws nothing
        // and whose maximum degrade distance is 0 (the DegradeSelector picks it at every distance), so the bolt you see in retail is the particle trail of its Launch script.
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var flame = AceWorldExtract.Load().ProjectilesBySpell[27];
        var set = new AvatarSet(sources);
        set.Create(BoltAt(Bolt, new Vector3(0, 15, 0), table: flame.PhysicsEffectTableId ?? 0, setup: flame.SetupId), 0);
        Run(set, 0, 3);
        Assert.Equal(1, set.Frame.Avatars.Length);
        var draw = set.Frame.Avatars[0];
        Assert.True(draw.Visible);
        Assert.Equal(1, draw.PartCount);
        Assert.Equal(0u, set.Frame.Parts[draw.PartStart].GfxObjId);
        Assert.True(set.TryGet(Bolt, out var bolt));
        var chain = bolt!.Body.Appearance.Parts[0].Chain;
        output.WriteLine($"Flame Bolt part {chain.NearestGfxObjId:X8}: levels {string.Join(",", chain.GfxObjIds.Select(i => i.ToString("X8")))}, max degrade distance {chain.MaxDegradeDistance}");
        Assert.Equal(0u, chain.Select(0.5f).GfxObjId);
    }

    /// <summary>Every GfxObj loads with no surfaces.</summary>
    private sealed class FlatAppearance : IAppearanceSource
    {
        public LodChain GetChain(uint gfxObjId) => LodChain.Create(gfxObjId, new GfxObj(), null, _ => true);

        public IReadOnlyList<uint> SurfaceIds(uint gfxObjId) => [];

        public uint OriginalTextureId(uint surfaceId) => 0;

        public uint OriginalPaletteId(uint surfaceId) => 0;

        public PaletteData? GetPalette(uint paletteId) => null;
    }

    /// <summary>Two script tables, <see cref="FullTable"/> (Launch and Explode) and <see cref="LaunchOnlyTable"/> (Launch), over two empty scripts.</summary>
    private sealed class BoltEffects : IEffectSource
    {
        private readonly Lifestone.Data.Effects.PhysicsScriptTableData _full = Lifestone.Data.Effects.PhysicsScriptTableData.FromDat(new PhysicsScriptTable
        {
            Id = FullTable,
            ScriptTable =
            {
                [DatPlayScript.Launch] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = LaunchScript } } },
                [DatPlayScript.Explode] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = ExplodeScript } } },
            },
        });

        private readonly Lifestone.Data.Effects.PhysicsScriptTableData _launchOnly = Lifestone.Data.Effects.PhysicsScriptTableData.FromDat(new PhysicsScriptTable
        {
            Id = LaunchOnlyTable,
            ScriptTable =
            {
                [DatPlayScript.Launch] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = LaunchScript } } },
            },
        });

        public Lifestone.Data.Effects.PhysicsScriptData? GetScript(uint scriptId)
        {
            if (scriptId is not (LaunchScript or ExplodeScript))
            {
                return null;
            }

            var raw = new byte[8];
            BitConverter.TryWriteBytes(raw, scriptId);
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(scriptId, raw);
        }

        public Lifestone.Data.Effects.PhysicsScriptTableData? GetScriptTable(uint tableId) =>
            tableId == FullTable ? _full : tableId == LaunchOnlyTable ? _launchOnly : null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => null;
    }
}
