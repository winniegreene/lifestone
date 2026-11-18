using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Client.Avatars;
using Lifestone.Client.Interiors;
using Lifestone.Client.Options;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using DatFrame = DatReaderWriter.Types.Frame;
using DatPlayScript = DatReaderWriter.Enums.PlayScript;

namespace Lifestone.Client.Tests.Interiors;

/// <summary>
/// M3 4.1 s0, the dynamic-light seam: the list (<c>Render::add_dynamic_light</c> 11.6096 0x0054E030, <c>insert_light</c> 0x0054DDC0, <c>remove_object_light</c> 0x0054CDC0), the
/// viewer light (<c>SmartBox::set_viewer</c> 0x00452C80), the object sources in the avatar set, the merge ahead of the static lights (<c>Render::minimize_object_lighting</c>
/// 0x0054E090) and the frame snapshot. No dats needed.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class DynamicLightsTests
{
    private const uint Cell = 0xA9B40019;
    private const uint BoltSetup = 0x02FF0201;
    private const uint Table = 0x34FF0201;
    private const uint OffScript = 0x33FF0201;
    private const uint Bolt = 0x80000401;
    private const uint Bolt2 = 0x80000402;
    private const double Step = 1.0 / 30.0;

    /// <summary>What ACE gives a spell projectile, with the lights on (the weenie's state; <c>ProjectileImpact</c> clears LightingOn).</summary>
    private const uint BoltState = AvatarStateBits.Missile | AvatarStateBits.AlignPath | AvatarStateBits.ReportCollisions | AvatarStateBits.LightingOn;

    private static readonly Vector3 Orange = new(1f, 0.5f, 0.25f);

    private static CellFrame At(float x, float y, float z = 0f, uint cell = Cell) => new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.Identity));

    private static void Add(DynamicLightList list, uint owner, float x, float y = 0f, bool on = true) =>
        Assert.True(list.Upsert(owner, Cell, new Vector3(x, y, 0f), Orange, 10f, 5f, on));

    // ----- the list -----

    [Fact]
    public void The_list_is_nearest_the_viewer_first_whatever_the_order_the_lights_were_added_in()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Add(list, 3, 30f);
        Add(list, 1, 10f);
        Add(list, 2, 20f);
        list.Update(At(0f, 0f), null);
        Assert.Equal(3, list.Count);
        Assert.Equal(1u, list[0].OwnerId);
        Assert.Equal(2u, list[1].OwnerId);
        Assert.Equal(3u, list[2].OwnerId);
        Assert.Equal(100f, list[0].DistanceSq);

        list.Update(At(31f, 0f), null); // the viewer moved: the order follows it
        Assert.Equal(3u, list[0].OwnerId);
        Assert.Equal(1u, list[2].OwnerId);
    }

    [Fact]
    public void A_tie_goes_behind_the_light_already_there()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Add(list, 7, 10f);
        Add(list, 8, -10f);
        list.Update(At(0f, 0f), null);
        Assert.Equal(7u, list[0].OwnerId); // insert_light: the first entry whose distance is larger; equal ones stay ahead
        Assert.Equal(8u, list[1].OwnerId);
    }

    [Fact]
    public void The_list_holds_seven_and_the_viewer_light_keeps_its_slot_even_when_it_is_the_farthest()
    {
        var list = new DynamicLightList();
        for (uint i = 1; i <= 12; i++)
        {
            Add(list, i, 0.5f * i); // every one nearer to the viewer than the viewer light, which is 20 m away
        }

        list.Update(At(0f, 0f), At(20f, 0f)); // the player stands 20 m from the viewer
        Assert.Equal(DynamicLightList.MaxLights, list.Count);
        Assert.True(list[DynamicLightList.MaxLights - 1].IsViewer, "the viewer light is in, farthest");
        for (var i = 0; i < DynamicLightList.MaxLights - 1; i++)
        {
            Assert.Equal((uint)(i + 1), list[i].OwnerId); // and the six nearest of the twelve
        }
    }

    [Fact]
    public void A_light_pushed_out_of_the_seven_comes_back_when_it_is_nearer()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        for (uint i = 1; i <= 8; i++)
        {
            Add(list, i, i * 10f);
        }

        list.Update(At(0f, 0f), null);
        Assert.Equal(7, list.Count);
        Assert.Equal(7u, list[6].OwnerId); // light 8 is the farthest of a full list: dropped
        list.Update(At(80f, 0f), null);
        Assert.Equal(8u, list[0].OwnerId);
        Assert.Equal(2u, list[6].OwnerId); // and now 1 is the one out
    }

    [Fact]
    public void A_light_leaves_the_list_when_it_is_removed_or_switched_off()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Add(list, 1, 10f);
        Add(list, 2, 20f);
        Add(list, 3, 30f);
        Assert.True(list.SetOn(2, false));
        list.Update(At(0f, 0f), null);
        Assert.Equal(2, list.Count);
        Assert.Equal(3u, list[1].OwnerId);
        Assert.True(list.SetOn(2, true));
        list.Update(At(0f, 0f), null);
        Assert.Equal(3, list.Count);
        Assert.True(list.Remove(1));
        Assert.False(list.Remove(1));
        list.Update(At(0f, 0f), null);
        Assert.Equal(2, list.Count);
        Assert.Equal(2u, list[0].OwnerId);
        Assert.False(list.Contains(1));
    }

    [Fact]
    public void The_viewer_light_is_white_2_25_falloff_10_and_two_metres_above_the_player_in_z_only()
    {
        var list = new DynamicLightList();
        list.Update(At(0f, 0f, 5f), At(3f, 4f, 7f));
        Assert.Equal(1, list.Count);
        ref readonly var viewer = ref list[0];
        Assert.True(viewer.IsViewer);
        Assert.Equal(Vector3.One, viewer.Color); // 0xFFFFFFFF
        Assert.Equal(2.25f, viewer.Intensity);
        Assert.Equal(10f, viewer.Falloff);
        Assert.Equal(new Vector3(3f, 4f, 9f), viewer.Position);
        Assert.Equal(Cell, viewer.CellId);

        list.Update(At(1f, 2f, 3f), null); // no player: at the viewer, no height
        Assert.Equal(new Vector3(1f, 2f, 3f), list[0].Position);

        list.ViewerLightEnabled = false; // --viewer-light=off
        list.Update(At(0f, 0f), At(3f, 4f, 7f));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void The_viewer_light_option_parses_on_and_off_and_defaults_on()
    {
        Assert.True(LightOptions.Parse([]).ViewerLight);
        Assert.False(LightOptions.Parse(["--viewer-light=off"]).ViewerLight);
        Assert.True(LightOptions.Parse(["--viewer-light=on"]).ViewerLight);
        Assert.Contains("--viewer-light", OptionCatalog.HelpText(), StringComparison.Ordinal);
    }

    // ----- the frame -----

    [Fact]
    public void The_frame_carries_radiance_reach_and_the_cell_of_each_light_in_the_lists_order()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Add(list, 5, 12f);
        Add(list, 4, 6f);
        list.Update(At(0f, 0f), null);
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        Assert.Equal(2, frame.Count);
        Assert.Equal(4u, frame[0].OwnerId);
        Assert.Equal(Cell, frame[0].CellId);
        Assert.Equal(new Vector3(6f, 0f, 0f), frame[0].Position);
        Assert.Equal(Orange * 10f, frame[0].Radiance);
        Assert.Equal(5f * 1.5f, frame[0].Reach); // falloff x 1.5 (config_hardware_light)
        Assert.Equal(2, frame.Lights.Length);
        Assert.Equal(1, frame.Version);
    }

    // ----- the merge ahead of the static lights -----

    private static CellLight Static(float x, float falloff = 4f) =>
        new(0x00070143, 0x02000000, new Vector3(x, 0f, 0f), new Vector3(x, 0f, 0f), new Vector3(0f, 1f, 0f), 100f, falloff);

    [Fact]
    public void The_dynamic_lights_come_ahead_of_the_static_ones_and_eight_is_the_most()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        for (uint i = 1; i <= 7; i++)
        {
            Add(list, i, 0.1f * i);
        }

        list.Update(At(0f, 0f), null);
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        IReadOnlyList<CellLight> statics = [Static(0.2f), Static(0.3f), Static(0.4f)];
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(Vector3.Zero, 1f, Cell, statics, frame, set);
        Assert.Equal(8, count);
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(Orange * 10f, set[i].Radiance); // the seven dynamic lights first, in the frame's order
            Assert.Equal(5f * 1.5f, set[i].Reach);
        }

        Assert.Equal(new Vector3(0f, 100f, 0f), set[7].Radiance); // then the first static light; the other two do not fit
    }

    [Fact]
    public void A_dynamic_light_out_of_reach_is_left_out_and_one_in_another_landblock_is_moved_into_the_sphere_space()
    {
        var list = new DynamicLightList { ViewerLightEnabled = false };
        Assert.True(list.Upsert(1, Cell, new Vector3(50f, 0f, 0f), Orange, 10f, 5f, true)); // 50 m away: out
        var north = Cell + 0x10000; // the landblock to the north (the id's second byte is y)
        Assert.Equal(new Vector3(0f, 192f, 0f), Lifestone.Data.World.LandDefs.GetBlockOffset(Cell, north));
        Assert.True(list.Upsert(2, north, new Vector3(0f, -2f, 0f), Orange, 10f, 5f, true)); // 2 m south of the northern block's origin: y = 190 in the sphere's block
        list.Update(At(0f, 0f), null);
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        var count = ObjectLightSets.Select(new Vector3(0f, 190.5f, 0f), 1f, Cell, [], frame, set);
        Assert.Equal(1, count);
        Assert.Equal(new Vector3(0f, 190f, 0f), set[0].Position);
    }

    // ----- the sources in the avatar set -----

    private static AvatarSet NewSet(bool bareSetup = false)
    {
        var setups = new Dictionary<uint, ModelAssembly> { [BoltSetup] = BoltSetupWith(bareSetup) };
        var sources = new AvatarSources(id => setups.GetValueOrDefault(id), new FlatAppearance(), new Lifestone.Client.Tests.Avatars.AvatarRig.RigAnimations(), _ => null, new LightEffects());
        return new AvatarSet(sources);
    }

    private static ModelAssembly BoltSetupWith(bool bare)
    {
        var setup = new Setup { Height = 0.5f, Radius = 0.1f };
        setup.Parts.Add(0x01FF0301);
        setup.Spheres.Add(new Sphere { Origin = Vector3.Zero, Radius = 0.1f });
        if (!bare)
        {
            setup.Lights[0] = new LightInfo
            {
                ViewSpaceLocation = new DatFrame { Origin = new Vector3(0f, 0f, 0.25f), Orientation = Quaternion.Identity },
                Color = new ColorARGB { Alpha = 255, Red = 255, Green = 128, Blue = 0 },
                Intensity = 1.5f,
                Falloff = 8f,
                ConeAngle = 0f,
            };
        }

        var frame = new AnimationFrame(1);
        frame.Frames.Add(new DatFrame { Origin = Vector3.Zero, Orientation = Quaternion.Identity });
        setup.PlacementFrames.Add(Placement.Default, frame);
        return ModelAssembly.FromSetup(BoltSetup, setup);
    }

    private static AvatarCreate BoltAt(uint guid, CellFrame at, Vector3 velocity, uint state = BoltState) => new()
    {
        Guid = guid,
        SetupId = BoltSetup,
        PhysicsState = state,
        Velocity = velocity,
        PhysicsScriptTableId = Table,
        Position = at,
    };

    private static double Run(AvatarSet set, AvatarViewer viewer, double from, int frames)
    {
        var now = from;
        for (var i = 0; i < frames; i++)
        {
            now += Step;
            set.Advance(now, viewer, long.MaxValue);
        }

        return now;
    }

    [Fact]
    public void The_bolt_is_in_the_frame_with_the_viewer_light_and_follows_it_and_the_player_light_sits_two_metres_up()
    {
        var set = NewSet();
        var viewer = new AvatarViewer(At(96f, 90f, 12f));
        set.Create(BoltAt(Bolt, At(96f, 96f, 10f), new Vector3(0f, 10f, 0f)), 0);
        Assert.True(set.TryGet(Bolt, out var bolt));
        var now = 0.0;
        for (var i = 0; i < 30; i++)
        {
            now = Run(set, viewer, now, 1);
            var frame = set.DynamicLightFrame;
            Assert.Equal(2, frame.Count);
            var ownerIsBolt = frame[0].OwnerId == Bolt ? 0 : 1;
            Assert.Equal(Bolt, frame[ownerIsBolt].OwnerId);
            Assert.True(frame[1 - ownerIsBolt].OwnerId == DynamicLightList.ViewerOwner);
            var expected = bolt!.Body.Position.Origin + new Vector3(0f, 0f, 0.25f); // the Setup light's offset above the bolt's origin
            Assert.True(Vector3.Distance(expected, frame[ownerIsBolt].Position) < 1e-3f, $"frame {i}: expected {expected}, got {frame[ownerIsBolt].Position}");
            Assert.Equal(new Vector3(1.5f, 0.75f, 0f), frame[ownerIsBolt].Radiance, new VectorComparer(0.01f)); // colour (255,128,0) x 1.5
            Assert.Equal(12f, frame[ownerIsBolt].Reach);
        }
    }

    [Fact]
    public void A_bolts_light_moves_with_it_across_a_landblock_seam()
    {
        var set = NewSet();
        var start = At(96f, 186f, 10f);
        var viewer = new AvatarViewer(start);
        set.Create(BoltAt(Bolt, start, new Vector3(0f, 10f, 0f)), 0);
        Assert.True(set.TryGet(Bolt, out var bolt));
        var crossed = false;
        var now = 0.0;
        for (var i = 0; i < 60; i++)
        {
            now = Run(set, viewer, now, 1);
            var frame = set.DynamicLightFrame;
            var index = frame[0].OwnerId == Bolt ? 0 : 1;
            ref readonly var light = ref frame[index];
            Assert.Equal(bolt!.Body.Position.CellId, light.CellId); // the entry takes the bolt's cell as the body takes it
            var world = light.Position + Lifestone.Data.World.LandDefs.GetBlockOffset(start.CellId, light.CellId);
            var expected = bolt.Body.Position.Origin + Lifestone.Data.World.LandDefs.GetBlockOffset(start.CellId, bolt.Body.Position.CellId) + new Vector3(0f, 0f, 0.25f);
            Assert.True(Vector3.Distance(expected, world) < 1e-3f, $"frame {i}: expected {expected}, got {world}");
            crossed |= (light.CellId >> 16) != (start.CellId >> 16);
        }

        Assert.True(crossed, "the bolt crossed into the next landblock and its light went with it");
    }

    [Fact]
    public void The_light_leaves_on_F747_and_when_the_state_clears_LightingOn_and_a_static_object_adds_none()
    {
        var set = NewSet();
        var viewer = new AvatarViewer(At(96f, 90f, 12f));
        set.Create(BoltAt(Bolt, At(96f, 96f, 10f), new Vector3(0f, 10f, 0f)), 0);
        set.Create(BoltAt(Bolt2, At(96f, 98f, 10f), Vector3.Zero), 0);
        set.Create(BoltAt(0x80000403, At(96f, 99f, 10f), Vector3.Zero, BoltState | AvatarStateBits.Static), 0);
        var now = Run(set, viewer, 0, 2);
        Assert.Equal(3, set.DynamicLightFrame.Count); // the viewer light and the two non-static bolts
        Assert.Equal(2, set.DynamicLights.SourceCount);

        Assert.Equal(AvatarApply.Applied, set.SetState(Bolt, BoltState & ~AvatarStateBits.LightingOn)); // ProjectileImpact's lights off
        now = Run(set, viewer, now, 1);
        Assert.Equal(2, set.DynamicLightFrame.Count);
        Assert.Equal(Bolt2, set.DynamicLightFrame[0].OwnerId == DynamicLightList.ViewerOwner ? set.DynamicLightFrame[1].OwnerId : set.DynamicLightFrame[0].OwnerId);

        Assert.Equal(AvatarApply.Applied, set.SetState(Bolt, BoltState)); // and back on
        now = Run(set, viewer, now, 1);
        Assert.Equal(3, set.DynamicLightFrame.Count);

        Assert.Equal(AvatarApply.Applied, set.Delete(Bolt2)); // F747
        now = Run(set, viewer, now, 1);
        Assert.Equal(2, set.DynamicLightFrame.Count);
        Assert.False(set.DynamicLights.Contains(Bolt2));
        Assert.Equal(AvatarApply.Applied, set.Delete(Bolt));
        Run(set, viewer, now, 1);
        Assert.Equal(1, set.DynamicLightFrame.Count);
        Assert.True(set.DynamicLightFrame[0].OwnerId == DynamicLightList.ViewerOwner);
    }

    [Fact]
    public void A_SetLight_hook_off_switches_the_objects_light_off_through_EffectVisualState()
    {
        var set = NewSet();
        var viewer = new AvatarViewer(At(96f, 90f, 12f));
        set.Create(BoltAt(Bolt, At(96f, 96f, 10f), new Vector3(0f, 10f, 0f)), 0);
        var now = Run(set, viewer, 0, 2);
        Assert.Equal(2, set.DynamicLightFrame.Count);
        Assert.Equal(AvatarApply.Applied, set.PlayScript(Bolt, (uint)DatPlayScript.Launch, 1f));
        now = Run(set, viewer, now, 3);
        Assert.False(set.TryGet(Bolt, out var bolt) && bolt!.Body.Effects.Visual.LightsOn, "the SetLight(off) hook ran");
        Assert.Equal(1, set.DynamicLightFrame.Count); // only the viewer light is left
        _ = now;
    }

    [Fact]
    public void A_setup_without_a_light_adds_no_source()
    {
        var set = NewSet(bareSetup: true);
        set.Create(BoltAt(Bolt, At(96f, 96f, 10f), new Vector3(0f, 10f, 0f)), 0);
        Run(set, new AvatarViewer(At(96f, 90f, 12f)), 0, 2);
        Assert.Equal(0, set.DynamicLights.SourceCount);
        Assert.Equal(1, set.DynamicLightFrame.Count);
    }

    // ----- no allocation -----

    [Fact]
    [Trait("Tier", "Timing")] // dynamic-lights-alloc-flake: a zero-byte allocation count (4,584 bytes under load; tiered JIT on the test thread)
    public void A_thousand_ticks_allocate_nothing()
    {
        var list = new DynamicLightList();
        for (uint i = 1; i <= 20; i++)
        {
            Add(list, i, i * 3f);
        }

        var frame = new DynamicLightFrame();
        var viewer = At(0f, 0f);
        CellFrame? player = At(1f, 1f);
        Span<ObjectLight> set = stackalloc ObjectLight[ObjectLightSets.MaxLights];
        IReadOnlyList<CellLight> statics = [Static(0.2f)];
        // The warm-up is long enough for tiered JIT to leave tier 0 (the test once measured 4,584 bytes under the full suite's load, a first-call cost); the tier is not changed (M4 D28).
        for (var i = 0; i < 2000; i++)
        {
            Tick(list, frame, viewer, player, statics, set, i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            Tick(list, frame, viewer, player, statics, set, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Tick(DynamicLightList list, DynamicLightFrame frame, in CellFrame viewer, in CellFrame? player, IReadOnlyList<CellLight> statics, Span<ObjectLight> set, int i)
    {
        list.Move(1, Cell, new Vector3(i % 7, 0f, 0f));
        list.SetOn(2, (i & 1) == 0);
        list.Update(viewer, player);
        frame.Fill(list);
        _ = ObjectLightSets.Select(Vector3.Zero, 1f, Cell, statics, frame, set);
    }

    [Fact]
    [Trait("Tier", "Timing")] // dynamic-lights-alloc-flake: the same zero-byte allocation count with a bolt in flight (1,560 bytes once)
    public void A_thousand_avatar_set_advances_with_a_bolt_in_flight_allocate_nothing_for_the_lights()
    {
        var set = NewSet();
        var viewer = new AvatarViewer(At(96f, 90f, 12f));
        set.Create(BoltAt(Bolt, At(96f, 96f, 10f), Vector3.Zero), 0); // hovering: the advance itself is the measure
        var now = Run(set, viewer, 0, 2000); // the warm-up is long enough for tiered JIT to settle (see A_thousand_ticks_allocate_nothing)
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(set, viewer, now, 1000);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes == 0, $"1,000 advances with a lit bolt allocated {bytes} bytes");
    }

    // ----- scaffolding -----

    private sealed class VectorComparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }

    private sealed class FlatAppearance : IAppearanceSource
    {
        public LodChain GetChain(uint gfxObjId) => LodChain.Create(gfxObjId, new GfxObj(), null, _ => true);

        public IReadOnlyList<uint> SurfaceIds(uint gfxObjId) => [];

        public uint OriginalTextureId(uint surfaceId) => 0;

        public uint OriginalPaletteId(uint surfaceId) => 0;

        public PaletteData? GetPalette(uint paletteId) => null;
    }

    /// <summary>One script table whose Launch script is a single SetLight(off) hook at time 0.</summary>
    private sealed class LightEffects : IEffectSource
    {
        private readonly Lifestone.Data.Effects.PhysicsScriptTableData _table = Lifestone.Data.Effects.PhysicsScriptTableData.FromDat(new PhysicsScriptTable
        {
            Id = Table,
            ScriptTable =
            {
                [DatPlayScript.Launch] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = OffScript } } },
            },
        });

        public Lifestone.Data.Effects.PhysicsScriptData? GetScript(uint scriptId)
        {
            if (scriptId != OffScript)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(scriptId);
            w.Write(1u); // one hook
            w.Write(0.0); // at time 0
            w.Write((uint)AnimHookType.SetLight);
            w.Write(0); // direction: both
            w.Write(0); // lights off
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(scriptId, stream.ToArray());
        }

        public Lifestone.Data.Effects.PhysicsScriptTableData? GetScriptTable(uint tableId) => tableId == Table ? _table : null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => null;
    }
}
