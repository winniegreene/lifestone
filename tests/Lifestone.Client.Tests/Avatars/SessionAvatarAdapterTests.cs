using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// AV13, the session adapter, on the reducer's real event stream and the synthetic avatar world (<see cref="AvatarRig"/>): every object event reaches the avatar
/// set in the reducer's order, inside the caller's budget; the player's Hidden bit is seen going off (materialising); nothing is lost when an object is deleted
/// before the queue reaches it. The real recordings of <c>cache/sessions</c> are replayed through it in <see cref="SessionAvatarAdapterReplayTests"/>.
/// </summary>
public sealed class SessionAvatarAdapterTests
{
    private const uint Npc = 0x80000100;
    private const uint Other = 0x80000101;
    private const uint Cell = AvatarRig.HoltburgCell;

    [Fact]
    public void Creates_are_queued_in_order_and_applied_inside_the_budget_one_event_at_least_per_pump()
    {
        var w = new World();
        EnterAsHuman(w.H);
        for (uint i = 0; i < 4; i++)
        {
            w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc + i, 1, Position(Cell, x: 10 + i)), false);
        }

        Assert.Equal(5, w.Adapter.Pending);
        Assert.Equal(0, w.Avatars.Count);

        // A deadline already passed still applies one event: the queue always drains.
        Assert.Equal(1, w.Adapter.Pump(deadlineTimestamp: 0));
        Assert.True(w.Avatars.TryGet(ReducerHarness.PlayerGuid, out var player), "the player's CreateObject was the first event and is the first applied");
        Assert.True(player.IsPlayer);
        Assert.Equal(1, w.Avatars.Count);
        Assert.Equal(1, w.Adapter.Pump(0));
        Assert.Equal(2, w.Avatars.Count);

        Assert.Equal(3, w.Adapter.Drain());
        Assert.Equal(0, w.Adapter.Pending);
        Assert.Equal(5, w.Avatars.Count);
        Assert.Equal(5, w.Adapter.Stats.Applied);
        Assert.True(w.Adapter.Stats.Failed + w.Adapter.Stats.Unknown == 0, $"failed {w.Adapter.Stats.Failed}, unknown {w.Adapter.Stats.Unknown}");
        Assert.Equal(5, w.Adapter.Stats.MaxPending);
    }

    [Fact]
    public void The_player_created_hidden_is_seen_unhiding_when_the_server_clears_the_bit()
    {
        var w = new World();
        EnterAsHuman(w.H);
        w.Adapter.Drain();
        Assert.True(w.Avatars.Player!.Body.IsHidden);
        Assert.Equal(1, w.Adapter.Stats.PlayerCreatedHidden);
        Assert.Equal(0, w.Adapter.Stats.PlayerUnhides);

        w.H.Reducer.OnSetState(w.H.Context, new SetStateMsg(ReducerHarness.PlayerGuid, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, 2));
        Assert.Equal(1, w.Adapter.Pending);
        w.Adapter.Drain();

        Assert.False(w.Avatars.Player.Body.IsHidden);
        Assert.Equal(1, w.Adapter.Stats.PlayerUnhides);
        var notices = new List<AvatarNotice>();
        w.Avatars.DrainNotices(notices);
        Assert.Contains(notices, n => n.Kind == AvatarNoticeKind.PlayerHiddenChanged && n.Detail == "unhide");

        // Hidden again (a teleport hides the player): counted as a hide.
        w.H.Reducer.OnSetState(w.H.Context, new SetStateMsg(ReducerHarness.PlayerGuid, PhysicsState.Hidden | PhysicsState.Gravity, 1, 3));
        w.Adapter.Drain();
        Assert.True(w.Avatars.Player.Body.IsHidden);
        Assert.Equal(1, w.Adapter.Stats.PlayerHides);
    }

    [Fact]
    public void Positions_motion_vectors_state_parenting_pickup_descriptions_and_deletes_reach_the_set()
    {
        var w = new World();
        EnterAsHuman(w.H);
        w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc, 1, Position(Cell, x: 10)), false);
        w.H.Reducer.OnCreateObject(w.H.Context, Sword(Other, 1), false);
        w.Adapter.Drain();
        Assert.True(w.Avatars.TryGet(Npc, out var npc));

        // UpdatePosition (a remote object: interpolated there).
        w.H.Reducer.OnUpdatePosition(w.H.Context, Move(Npc, 1, position: 2, teleport: 1, forcePosition: 1, cell: Cell, x: 30));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.Moves);

        // UpdateMotion: the walking command moves the object's interpreted state.
        w.H.Reducer.OnUpdateMotion(w.H.Context, new UpdateMotionMsg(Npc, 1, 2, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Forward = 0x0005 }));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.Motions);

        // SetState and VectorUpdate.
        w.H.Reducer.OnSetState(w.H.Context, new SetStateMsg(Npc, PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.LightingOn, 1, 2));
        w.H.Reducer.OnVectorUpdate(w.H.Context, new VectorUpdateMsg(Npc, new Vector3(0, 1, 0), Vector3.Zero, 1, 2));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.StateChanges);
        Assert.True(npc.Body.LightsOn);

        // ParentEvent: the sword is wielded in the right hand; PickupEvent takes it out of the world.
        w.H.Reducer.OnParentEvent(w.H.Context, new ParentEventMsg(Npc, Other, (int)DatReaderWriter.Enums.ParentLocation.RightHand, 0, 1, 2));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.ParentEvents);
        Assert.Same(npc.Body, w.Avatars.TryGet(Other, out var sword) ? sword.Body.Parent : null);
        w.H.Reducer.OnPickupEvent(w.H.Context, new PickupEventMsg(Other, 1, 3));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.Pickups);

        // ObjDescEvent: a new description.
        w.H.Reducer.OnObjDescEvent(w.H.Context, new ObjDescEventMsg(Npc, new ModelData(0x04000001, [], [], []), 1, 2));
        w.Adapter.Drain();
        Assert.Equal(1, w.Avatars.Stats.DescChanges);

        // DeleteObject.
        w.H.Reducer.OnDeleteObject(w.H.Context, new DeleteObjectMsg(Npc, 1));
        w.Adapter.Drain();
        Assert.False(w.Avatars.TryGet(Npc, out _));
        Assert.Equal(1, w.Avatars.Stats.Deleted);
        Assert.Equal(0, w.Adapter.Stats.Failed + w.Adapter.Stats.Unknown);
    }

    [Fact]
    public void An_object_deleted_before_the_queue_reaches_it_is_created_as_it_was_and_then_deleted()
    {
        var w = new World();
        EnterAsHuman(w.H);
        w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc, 1, Position(Cell, x: 10)), false);
        w.H.Reducer.OnDeleteObject(w.H.Context, new DeleteObjectMsg(Npc, 1));
        Assert.False(w.H.State.Objects.TryGet(Npc, out _), "the session no longer holds the object");

        w.Adapter.Drain();

        Assert.False(w.Avatars.TryGet(Npc, out _));
        Assert.Equal(2, w.Avatars.Stats.Created);
        Assert.Equal(1, w.Avatars.Stats.Deleted);
        Assert.Equal(0, w.Adapter.Stats.Unknown);
    }

    [Fact]
    public void A_recreate_replaces_the_avatar_and_an_unchanged_update_does_nothing()
    {
        var w = new World();
        EnterAsHuman(w.H);
        w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc, 5, Position(Cell, x: 10)), false);
        w.Adapter.Drain();
        w.Avatars.TryGet(Npc, out var first);

        w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc, 6, Position(Cell, x: 12)), false);
        w.Adapter.Drain();
        Assert.True(w.Avatars.TryGet(Npc, out var second));
        Assert.NotSame(first, second);
        Assert.Equal(1, w.Avatars.Stats.Recreated);
        Assert.Equal(2, w.Avatars.Count);
    }

    [Fact]
    public void Immediate_mode_applies_inside_the_dispatch_and_clear_forgets_the_queue()
    {
        var h = new ReducerHarness();
        var avatars = new AvatarSet(AvatarRig.Sources());
        using var immediate = new SessionAvatarAdapter(h.Bus, h.State, avatars, () => 0.0, immediate: true);
        EnterAsHuman(h);
        Assert.Equal(0, immediate.Pending);
        Assert.True(avatars.TryGet(ReducerHarness.PlayerGuid, out _));

        var w = new World();
        EnterAsHuman(w.H);
        w.H.Reducer.OnCreateObject(w.H.Context, Human(Npc, 1, Position(Cell)), false);
        Assert.Equal(2, w.Adapter.Pending);
        w.Adapter.Clear();
        Assert.Equal(0, w.Adapter.Pending);
        Assert.Equal(2, w.Adapter.Stats.Discarded);
        Assert.Equal(0, w.Adapter.Drain());
        Assert.Equal(0, w.Avatars.Count);
    }

    [Fact]
    public void Disposing_stops_the_subscriptions()
    {
        var w = new World();
        w.Adapter.Dispose();
        EnterAsHuman(w.H);
        Assert.Equal(0, w.Adapter.Pending);
        Assert.Equal(0, w.Adapter.Stats.Received);
    }

    /// <summary>A reducer, an avatar set over the synthetic world, and an adapter between them.</summary>
    private sealed class World
    {
        public World()
        {
            H = new ReducerHarness();
            Avatars = new AvatarSet(AvatarRig.Sources());
            Adapter = new SessionAvatarAdapter(H.Bus, H.State, Avatars, () => Clock);
        }

        public ReducerHarness H { get; }

        public AvatarSet Avatars { get; }

        public SessionAvatarAdapter Adapter { get; }

        public double Clock { get; set; }
    }

    /// <summary>A CreateObject of a creature on the rig's human Setup and walking motion table.</summary>
    private static CreateObjectMsg Human(uint guid, ushort instance, AcPosition position)
    {
        var msg = Create(guid, Stamps(instance: instance), position, name: "Guard", setup: AvatarRig.Human);
        return msg with
        {
            Physics = msg.Physics with { MotionTableId = AvatarRig.Table, Flags = msg.Physics.Flags | PhysicsDescFlags.MTable, State = PhysicsState.Gravity | PhysicsState.ReportCollisions },
            Weenie = msg.Weenie with { ItemType = 0x10 },
        };
    }

    /// <summary>F746 then the player's own CreateObject, hidden, on the rig's human Setup and motion table (what ACE sends when the world is entered).</summary>
    private static void EnterAsHuman(ReducerHarness h)
    {
        h.Reducer.OnPlayerCreate(h.Context, new PlayerCreateMsg(ReducerHarness.PlayerGuid));
        var msg = Human(ReducerHarness.PlayerGuid, 1, Position(Cell, x: 5));
        h.Reducer.OnCreateObject(
            h.Context,
            msg with { Physics = msg.Physics with { State = PhysicsState.Hidden | PhysicsState.Gravity | PhysicsState.IgnoreCollisions | PhysicsState.EdgeSlide } },
            false);
    }

    private static CreateObjectMsg Sword(uint guid, ushort instance) =>
        Create(guid, Stamps(instance: instance), Position(Cell, x: 11), name: "Sword", setup: AvatarRig.Sword);
}
