using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Client.Avatars;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.ReducerHarness;
using DatFrame = DatReaderWriter.Types.Frame;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M4 4.1 lane v1, the held items. A held item's Setup light is lit while it is held and drawn and follows the hand (retail adds a child's lights to the cell it is
/// changed into: <c>CPhysicsObj::set_parent</c> 11.6096 0x00516590, <c>enter_cell</c> 0x005119A0, <c>CPartArray::AddLightsToCell</c> 0x005189D0); an item that leaves the world
/// keeps no emitter and no lit light; and the message orders ACE sends for the hunt's weapon swap leave the bow parented (the offline half of the owner's missing bow).
/// </summary>
public sealed class HeldItemTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Cell = AvatarRig.HoltburgCell;
    private const uint Holder = 7;
    private const uint Orb = 9;
    // Near the holder: the lit list keeps only the lights within reach of the viewer.
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(32, 28, 2)));

    // ----- the light -----

    [Fact]
    public void A_held_items_light_is_lit_while_it_is_held_and_follows_the_hand()
    {
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0), heading: 45f) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        Assert.True(set.DynamicLights.Contains(Orb), "the orb's Setup light is registered");
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        Assert.NotNull(orb.Parent);
        Assert.False(orb.InWorld);

        set.Advance(1 / 30.0, Viewer, long.MaxValue);
        Assert.Contains(Lit(set), id => id == Orb);
        var seen = new HashSet<Vector3>();
        for (var i = 2; i <= 10; i++)
        {
            // The holder is placed somewhere else each frame (a server position with a newer teleport): the orb follows its hand and the light follows the orb.
            var to = AvatarRig.At(new Vector3(30 + i, 30 + (i * 0.5f), 0), heading: 10f * i);
            Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectMoved(Holder, to, PositionVerdict.Remote, TeleportAdvanced: true, HasContact: true, null, null), now: i / 30.0));
            set.Advance(i / 30.0, Viewer, long.MaxValue);
            var entry = LightOf(set, Orb);
            var expected = orb.Position.Origin + Vector3.Transform(HeldItemRig.LightOffset, orb.Position.Orientation);
            Assert.True(Vector3.Distance(expected, entry.Position) < 1e-3f, $"frame {i}: expected {expected}, got {entry.Position}");
            seen.Add(entry.Position);
        }

        Assert.True(seen.Count == 9, "the light moved with the holder every frame");
    }

    [Fact]
    public void A_held_items_light_goes_dark_when_it_leaves_the_hand_and_when_its_holder_is_hidden_and_returns_with_them()
    {
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        set.Advance(1 / 30.0, Viewer, long.MaxValue);
        Assert.Contains(Lit(set), id => id == Orb);

        // Stowed (F74A): out of the world, its light out of the lit list (the entry stays registered until the object is deleted).
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectPickedUp(Orb)));
        set.Advance(2 / 30.0, Viewer, long.MaxValue);
        Assert.DoesNotContain(Lit(set), id => id == Orb);
        Assert.True(set.DynamicLights.Contains(Orb));

        // Wielded again (F749): lit again.
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectParented(Orb, Holder, Location: 1, Placement: 0)));
        set.Advance(3 / 30.0, Viewer, long.MaxValue);
        Assert.Contains(Lit(set), id => id == Orb);

        // The holder Hidden gives its children NoDraw: dark; shown again: lit.
        Assert.Equal(AvatarApply.Applied, set.SetState(Holder, AvatarStateBits.Gravity | AvatarStateBits.Hidden));
        set.Advance(4 / 30.0, Viewer, long.MaxValue);
        Assert.DoesNotContain(Lit(set), id => id == Orb);
        Assert.Equal(AvatarApply.Applied, set.SetState(Holder, AvatarStateBits.Gravity));
        set.Advance(5 / 30.0, Viewer, long.MaxValue);
        Assert.Contains(Lit(set), id => id == Orb);

        // Deleted: gone from the list.
        Assert.Equal(AvatarApply.Applied, set.Delete(Orb));
        Assert.False(set.DynamicLights.Contains(Orb));
        set.Advance(6 / 30.0, Viewer, long.MaxValue);
        Assert.DoesNotContain(Lit(set), id => id == Orb);
    }

    [Fact]
    public void A_held_item_the_server_describes_without_LightingOn_stays_dark()
    {
        // The light is the Setup's and the state bit decides (retail set_lights): a weenie that sends no LightingOn has a registered, unlit light, however it is held.
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder, state: 0), now: 0);
        set.Advance(1 / 30.0, Viewer, long.MaxValue);
        Assert.True(set.DynamicLights.Contains(Orb));
        Assert.DoesNotContain(Lit(set), id => id == Orb);
        Assert.Equal(AvatarApply.Applied, set.SetState(Orb, AvatarStateBits.LightingOn)); // a later SetState lights it
        set.Advance(2 / 30.0, Viewer, long.MaxValue);
        Assert.Contains(Lit(set), id => id == Orb);
    }

    [Fact]
    public void A_held_item_hidden_by_the_first_person_fade_keeps_its_light()
    {
        // The fade is a translucency (CameraSet::UpdateCamera -> SetTranslucencyHierarchical, 11.6096 0x00458C00), not a cell change and not NoDraw: the body stays drawn
        // as far as the lights are concerned, so the rule that lights a held item reads IsDrawn and never the camera's translucency.
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        orb.Effects.Visual.TranslucencyOriginal = 1f; // what the fade's floor does to a part's translucency
        set.Advance(1 / 30.0, Viewer, long.MaxValue);
        Assert.True(orb.IsDrawn);
        Assert.Contains(Lit(set), id => id == Orb);
    }

    // ----- the emitters -----

    [Fact]
    public void A_held_items_emitters_stop_when_it_leaves_the_hand_and_start_again_when_it_comes_back()
    {
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        for (var i = 1; i <= 10; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.Equal(1, orb.Effects.Particles.Count);
        Assert.Contains(set.Frame.Avatars.ToArray(), d => d.Guid == Orb && d.ParticleCount > 0);

        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectPickedUp(Orb)));
        Assert.Equal(0, orb.Effects.Particles.Count);
        Assert.True(orb.EmittersStopped);
        for (var i = 11; i <= 40; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
            Assert.Equal(0, orb.Effects.Particles.Count);
            Assert.DoesNotContain(set.Frame.Avatars.ToArray(), d => d.Guid == Orb);
            Assert.Empty(set.Frame.Particles); // the holder has no emitter, so nothing of the orb's is left in the frame
        }

        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectParented(Orb, Holder, Location: 1, Placement: 0)));
        for (var i = 41; i <= 50; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.False(orb.EmittersStopped);
        Assert.Equal(1, orb.Effects.Particles.Count);
    }

    [Fact]
    public void A_deleted_holder_lets_its_children_go_without_their_emitters()
    {
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        for (var i = 1; i <= 5; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.Equal(1, orb.Effects.Particles.Count);
        set.Delete(Holder);
        Assert.Null(orb.Parent);
        Assert.Equal(0, orb.Effects.Particles.Count);
        set.Advance(10 / 30.0, Viewer, long.MaxValue);
        Assert.Empty(set.Frame.Avatars.ToArray());
    }

    [Fact]
    public void An_item_dropped_into_the_world_keeps_its_glow_where_it_lies()
    {
        var set = NewSet();
        set.Create(AvatarRig.HumanAt(Holder, new Vector3(30, 30, 0)) with { SetupId = HeldItemRig.Human }, now: 0);
        set.Create(OrbCreate(parent: Holder), now: 0);
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        set.Advance(1 / 30.0, Viewer, long.MaxValue);
        var dropped = AvatarRig.At(new Vector3(31, 31, 0));
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectMoved(Orb, dropped, PositionVerdict.Remote, TeleportAdvanced: false, HasContact: true, null, null), now: 1 / 30.0));
        for (var i = 2; i <= 10; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        Assert.True(orb.InWorld);
        Assert.Equal(1, orb.Effects.Particles.Count);
        Assert.Contains(Lit(set), id => id == Orb);
    }

    // ----- the weapon swap: the message orders ACE sends -----

    /// <summary>
    /// The hunt's swap (HuntScenario.SwapStep, capture.md C2) as ACE sends it (Player_Inventory.cs: <c>TryDequipObjectWithNetworking</c> 396 sends the PickupEvent;
    /// <c>TryEquipObjectWithBroadcasting</c> Creature_Equipment.cs 354 sends the ParentEvent then the ObjDescEvent, and no CreateObject to the wielder itself,
    /// Player_Tracking.cs 124-126): Mace to the pack, the Shortbow wielded, the Arrows wielded, then the restore in reverse. Every wielded item ends parented and drawn,
    /// the stowed ones not; no parent notice fails.
    /// </summary>
    [Fact]
    public void The_hunts_weapon_swap_in_ACEs_message_order_leaves_the_launcher_and_the_ammunition_parented()
    {
        var w = new SwapWorld();
        Assert.True(w.Parent(Mace) is { } && w.Parent(Bow) is null && w.Parent(Arrows) is null);

        // Mace to the pack.
        w.Pickup(Mace);
        Assert.Null(w.Parent(Mace));
        // Shortbow wielded (ParentEvent + ObjDescEvent of the holder).
        w.ParentEvent(Bow, location: 1);
        Assert.Equal(PlayerGuid, w.Parent(Bow));
        // Arrows wielded.
        w.ParentEvent(Arrows, location: 1);
        Assert.Equal(PlayerGuid, w.Parent(Arrows));
        w.Step(20);
        Assert.True(w.Drawn(Bow));
        Assert.True(w.Drawn(Arrows));

        // The restore (HuntScenario.RestoreStep): Arrows, then the Shortbow, to the pack; then the Mace wielded.
        w.Pickup(Arrows);
        w.Pickup(Bow);
        w.ParentEvent(Mace, location: 1);
        w.Step(20);
        Assert.Equal(PlayerGuid, w.Parent(Mace));
        Assert.Null(w.Parent(Bow));
        Assert.Null(w.Parent(Arrows));
        Assert.True(w.Drawn(Mace));
        Assert.False(w.Drawn(Bow));

        // A second swap, the way a hunt that goes round twice does it.
        w.Pickup(Mace);
        w.ParentEvent(Bow, location: 1);
        w.ParentEvent(Arrows, location: 1);
        w.Step(20);
        Assert.Equal(PlayerGuid, w.Parent(Bow));
        Assert.Equal(PlayerGuid, w.Parent(Arrows));
        Assert.True(w.Drawn(Bow));
        Assert.Equal(0, w.World.Avatars.Stats.ParentFailures);
        Assert.Equal(0, w.World.Adapter.Stats.Failed + w.World.Adapter.Stats.Unknown);
    }

    [Fact]
    public void The_arrows_wielded_before_the_bow_and_a_parent_event_ahead_of_its_create_both_end_parented()
    {
        // The arrows first (a launcher-less wield), then the bow; and the bow's ParentEvent arriving before the bow's CreateObject (the reducer parks it under the child).
        var w = new SwapWorld(bowCreated: false);
        w.Pickup(Mace);
        w.ParentEvent(Arrows, location: 1);
        w.ParentEvent(Bow, location: 1); // ahead of the bow's create: parked
        w.Step(2);
        Assert.Null(w.Parent(Bow));
        w.CreateBowInPack();
        w.Step(20);
        Assert.Equal(PlayerGuid, w.Parent(Arrows));
        Assert.Equal(PlayerGuid, w.Parent(Bow));
        Assert.True(w.Drawn(Bow));
        Assert.Equal(0, w.World.Avatars.Stats.ParentFailures);
    }

    [Fact]
    public void A_parent_event_for_a_location_the_holder_lacks_fails_loudly_and_leaves_the_item_unparented()
    {
        // What would make a wielded launcher drop: the holder's Setup lacking the holding location. It is a counted, logged ParentFailed notice (AvatarSet.ParentFailed), never silent.
        var w = new SwapWorld();
        w.Pickup(Mace);
        w.ParentEvent(Bow, location: 6);
        w.Step(2);
        Assert.Null(w.Parent(Bow));
        Assert.Equal(1, w.World.Avatars.Stats.ParentFailures);
        var notices = new List<AvatarNotice>();
        w.World.Avatars.DrainNotices(notices);
        Assert.Contains(notices, n => n.Kind == AvatarNoticeKind.ParentFailed && n.Guid == Bow);
    }

    // ----- the retail dats -----

    /// <summary>
    /// The Healer's Heart on the real human: the orb's Setup 0x020009AD carries a light and a default script with an emitter; held in the right hand the light is lit and
    /// the emitter runs; stowed, neither. (The owner's reports of a glow when it comes out and of a glowing artifact, with the retail data.)
    /// </summary>
    [RequiresDatsFact]
    public void The_healers_hearts_light_and_emitter_follow_the_real_human_hand_and_stop_when_it_is_stowed()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var setup = sources.GetSetup(0x020009AD);
        Assert.NotNull(setup);
        output.WriteLine($"Setup 0x020009AD: {setup!.Parts.Count} parts, {setup.Lights.Count} lights, default script 0x{setup.DefaultScriptId:X8}");
        Assert.NotEmpty(setup.Lights);
        Assert.NotEqual(0u, setup.DefaultScriptId);

        var set = new AvatarSet(sources);
        set.Create(new AvatarCreate { Guid = Holder, SetupId = 0x02000001, IsCreature = true, Position = AvatarRig.At(new Vector3(30, 30, 0)) }, now: 0);
        set.Create(new AvatarCreate { Guid = Orb, SetupId = 0x020009AD, ParentGuid = Holder, ParentLocation = 1, PhysicsState = AvatarStateBits.LightingOn }, now: 0);
        var orb = set.All.Single(a => a.Guid == Orb).Body;
        Assert.NotNull(orb.Parent);
        for (var i = 1; i <= 30; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        output.WriteLine($"held: lit={Lit(set).Contains(Orb)} emitters={orb.Effects.Particles.Count}");
        Assert.Contains(Lit(set), id => id == Orb);
        Assert.True(orb.Effects.Particles.Count > 0, "the default script's emitters run in the hand");

        set.Apply(new ObjectPickedUp(Orb));
        for (var i = 31; i <= 60; i++)
        {
            set.Advance(i / 30.0, Viewer, long.MaxValue);
        }

        output.WriteLine($"stowed: lit={Lit(set).Contains(Orb)} emitters={orb.Effects.Particles.Count}");
        Assert.DoesNotContain(Lit(set), id => id == Orb);
        Assert.Equal(0, orb.Effects.Particles.Count);
    }

    /// <summary>The holding locations ACE names for a bow, its arrows, a shield and an orb exist on the real human Setup, so a ParentEvent for any of them parents.</summary>
    [RequiresDatsFact]
    public void The_real_human_setup_holds_every_location_ACE_parents_a_wielded_item_at()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var set = new AvatarSet(sources);
        set.Create(new AvatarCreate { Guid = Holder, SetupId = 0x02000001, IsCreature = true, Position = AvatarRig.At(new Vector3(30, 30, 0)) }, now: 0);
        var human = set.All.Single(a => a.Guid == Holder).Body;
        output.WriteLine("human holding locations: " + string.Join(", ", human.Setup.HoldingLocations.Keys.Order()));
        uint guid = 100;
        foreach (var location in new[] { 1, 2, 3, 4, 5 })
        {
            // ACE ParentLocation: RightHand 1, LeftHand 2, Shield 3, Belt 4, Quiver 5.
            set.Create(new AvatarCreate { Guid = ++guid, SetupId = 0x020009AD }, now: 0);
            Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectParented(guid, Holder, location, Placement: 0)));
            Assert.Same(human, set.All.Single(a => a.Guid == guid).Body.Parent);
        }
    }

    // ----- helpers -----

    private static AvatarSet NewSet() => new(HeldItemRig.Sources());

    /// <summary>An orb the server describes with LightingOn (retail <c>set_lights</c> 0x005107C0 / the state bit 0x800 is what lights a Setup's lights).</summary>
    private static AvatarCreate OrbCreate(uint parent, uint state = AvatarStateBits.LightingOn) => new() { Guid = Orb, SetupId = HeldItemRig.OrbSetup, ParentGuid = parent, ParentLocation = 1, PhysicsState = state };

    /// <summary>The owners of the lit lights in this frame (the viewer's own light excluded).</summary>
    private static List<uint> Lit(AvatarSet set)
    {
        var owners = new List<uint>();
        for (var i = 0; i < set.DynamicLightFrame.Count; i++)
        {
            if (set.DynamicLightFrame[i].OwnerId != DynamicLightList.ViewerOwner)
            {
                owners.Add(set.DynamicLightFrame[i].OwnerId);
            }
        }

        return owners;
    }

    private static DynamicFrameLight LightOf(AvatarSet set, uint owner)
    {
        for (var i = 0; i < set.DynamicLightFrame.Count; i++)
        {
            if (set.DynamicLightFrame[i].OwnerId == owner)
            {
                return set.DynamicLightFrame[i];
            }
        }

        throw new InvalidOperationException($"no lit light for 0x{owner:X8}");
    }

    private const uint Mace = 0x80000201;
    private const uint Bow = 0x80000202;
    private const uint Arrows = 0x80000203;

    /// <summary>A reducer, a session adapter and an avatar set; the player on the rig's human, the Mace wielded, the Shortbow and the Arrows in the pack (CreateObjects with no position and no parent).</summary>
    private sealed class SwapWorld
    {
        private ushort _positionSeq = 1;

        public SwapWorld(bool bowCreated = true)
        {
            H = new ReducerHarness();
            var avatars = new AvatarSet(AvatarRig.Sources());
            World = (H, avatars, new SessionAvatarAdapter(H.Bus, H.State, avatars, () => Clock));
            H.Reducer.OnPlayerCreate(H.Context, new PlayerCreateMsg(PlayerGuid));
            var player = Create(PlayerGuid, Stamps(instance: 1), Position(Cell, x: 5), name: "Player", setup: AvatarRig.Human);
            H.Reducer.OnCreateObject(
                H.Context,
                player with { Physics = player.Physics with { MotionTableId = AvatarRig.Table, Flags = player.Physics.Flags | PhysicsDescFlags.MTable, State = PhysicsState.Gravity | PhysicsState.ReportCollisions } },
                false);
            H.Reducer.OnCreateObject(H.Context, Create(Mace, Stamps(), parent: new ObjectLink(PlayerGuid, 1), name: "Mace", setup: AvatarRig.Sword), false);
            if (bowCreated)
            {
                CreateBowInPack();
            }

            H.Reducer.OnCreateObject(H.Context, Create(Arrows, Stamps(), name: "Arrow", setup: AvatarRig.Sword), false);
            Adapter.Drain();
        }

        public ReducerHarness H { get; }

        public (ReducerHarness H, AvatarSet Avatars, SessionAvatarAdapter Adapter) World { get; }

        private SessionAvatarAdapter Adapter => World.Adapter;

        public double Clock { get; private set; }

        public void CreateBowInPack()
        {
            H.Reducer.OnCreateObject(H.Context, Create(Bow, Stamps(), name: "Shortbow", setup: AvatarRig.Sword), false);
            Adapter.Drain();
        }

        /// <summary>PickupEvent: ACE's TryDequipObjectWithNetworking (Player_Inventory.cs 404).</summary>
        public void Pickup(uint guid)
        {
            H.Reducer.OnPickupEvent(H.Context, new PickupEventMsg(guid, 1, ++_positionSeq));
            Adapter.Drain();
        }

        /// <summary>ParentEvent: ACE's TryEquipObjectWithBroadcasting (Creature_Equipment.cs 354-366), the holder's instance sequence and the child's next position sequence.</summary>
        public void ParentEvent(uint guid, int location)
        {
            H.Reducer.OnParentEvent(H.Context, new ParentEventMsg(PlayerGuid, guid, location, 0, 1, ++_positionSeq));
            Adapter.Drain();
        }

        public void Step(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Clock += 1 / 30.0;
                World.Avatars.Advance(Clock, Viewer, long.MaxValue);
            }
        }

        public uint? Parent(uint guid) =>
            World.Avatars.TryGet(guid, out var avatar) && avatar.Body.Parent is { } parent ? parent.Guid : null;

        public bool Drawn(uint guid) => World.Avatars.TryGet(guid, out var avatar) && avatar.Body.IsDrawn;
    }
}

/// <summary>M4 4.1 v1's in-memory world: a holder with hand locations, an orb Setup with a light and a default script that starts one emitter. No dats.</summary>
internal static class HeldItemRig
{
    public const uint Human = 0x02FF0501;
    public const uint OrbSetup = 0x02FF0502;
    public const uint OrbScript = 0x33FF0501;
    public const uint OrbEmitter = 0x32FF0501;

    /// <summary>The orb Setup light's offset from the orb's origin.</summary>
    public static readonly Vector3 LightOffset = new(0f, 0f, 0.25f);

    public static AvatarSources Sources()
    {
        var animations = new AvatarRig.RigAnimations()
            .Add(AvatarRig.ReadyAnim, 2, parts: 3)
            .Add(AvatarRig.WalkAnim, 4, parts: 3, posStepY: 3.12f / 30f, partStepX: 0.05f);
        var setups = new Dictionary<uint, ModelAssembly>
        {
            [Human] = HumanSetup(),
            [OrbSetup] = Orb(),
            [AvatarRig.Human] = AvatarRig.Sources().GetSetup(AvatarRig.Human)!,
        };
        return new AvatarSources(id => setups.GetValueOrDefault(id), new FlatAppearance(), animations, _ => null, new OrbEffects());
    }

    private static ModelAssembly HumanSetup()
    {
        var setup = new Setup { Height = 1.8f, Radius = 0.4f, Parts = [0x01FF0001, 0x01FF0002, 0x01FF0003] };
        setup.Spheres.Add(new Sphere { Origin = new Vector3(0, 0, 0.4f), Radius = 0.4f });
        setup.PlacementFrames.Add(DatReaderWriter.Enums.Placement.Resting, Frames(3));
        foreach (var location in new[] { DatReaderWriter.Enums.ParentLocation.RightHand, DatReaderWriter.Enums.ParentLocation.LeftHand, DatReaderWriter.Enums.ParentLocation.Shield })
        {
            setup.HoldingLocations.Add(location, new LocationType { PartId = 2, Frame = new DatFrame { Origin = AvatarRig.HandOffset, Orientation = Quaternion.Identity } });
        }

        return ModelAssembly.FromSetup(Human, setup);
    }

    private static ModelAssembly Orb()
    {
        var setup = new Setup { Height = 0.3f, Radius = 0.1f, Parts = [0x01FF0510] };
        setup.PlacementFrames.Add(DatReaderWriter.Enums.Placement.Default, Frames(1));
        setup.Lights[0] = new LightInfo
        {
            ViewSpaceLocation = new DatFrame { Origin = LightOffset, Orientation = Quaternion.Identity },
            Color = new ColorARGB { Alpha = 255, Red = 255, Green = 128, Blue = 0 },
            Intensity = 1.5f,
            Falloff = 6f,
            ConeAngle = 0f,
        };
        setup.DefaultScript = OrbScript;
        return ModelAssembly.FromSetup(OrbSetup, setup);
    }

    private static AnimationFrame Frames(int parts)
    {
        var frame = new AnimationFrame((uint)parts);
        for (var i = 0; i < parts; i++)
        {
            frame.Frames.Add(new DatFrame { Origin = new Vector3(0, 0, i), Orientation = Quaternion.Identity });
        }

        return frame;
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

    /// <summary>The orb's default script: one CreateParticle hook at time 0 for an endless birthrate emitter.</summary>
    private sealed class OrbEffects : IEffectSource
    {
        public Lifestone.Data.Effects.PhysicsScriptData? GetScript(uint scriptId)
        {
            if (scriptId != OrbScript)
            {
                return null;
            }

            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(scriptId);
            w.Write(1u); // one hook
            w.Write(0.0); // at time 0
            w.Write((uint)AnimHookType.CreateParticle);
            w.Write(0); // direction: both
            w.Write(OrbEmitter);
            w.Write(0); // part index
            w.Write(0f);
            w.Write(0f);
            w.Write(0f); // offset origin
            w.Write(1f);
            w.Write(0f);
            w.Write(0f);
            w.Write(0f); // offset orientation (w, x, y, z)
            w.Write(0u); // emitter id: assigned
            w.Flush();
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(scriptId, stream.ToArray());
        }

        public Lifestone.Data.Effects.PhysicsScriptTableData? GetScriptTable(uint tableId) => null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => emitterInfoId != OrbEmitter
            ? null
            : new ParticleEmitterInfoData
            {
                Id = OrbEmitter,
                EmitterType = EmitterKind.BirthratePerSec,
                ParticleType = ParticleKind.LocalVelocity,
                HwGfxObjId = 0x01000123,
                Birthrate = 0.1,
                MaxParticles = 10,
                Lifespan = 1.0,
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
    }
}
