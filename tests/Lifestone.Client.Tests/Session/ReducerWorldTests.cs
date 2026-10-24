using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The reducer's world phase (ST8), retail's SmartBox semantics: create, same-instance refresh, re-create, delete, the
/// position verdicts across the 16-bit wrap, parking and replay, the player's travel state, and the per-field
/// sequences of motion, vectors, state, parent, pickup and the visual description.
/// </summary>
public sealed class ReducerWorldTests
{
    private const uint Npc = 0x80000100;
    private const uint Cell = 0x860201AD;

    [Fact]
    public void Create_same_instance_updates_newer_instance_recreates_older_drops()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.ClearEvents();

        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 5), Position(Cell, x: 1), name: "Guard"), false);
        Assert.Equal(new ObjectCreated(Npc, false, true, false), Assert.Single(h.Of<ObjectCreated>()));
        Assert.True(h.State.Objects.TryGet(Npc, out var first));
        Assert.Equal("Guard", first.Name);

        // The same instance: refreshed in place, newer fields applied (position 2 > 1, desc 2 > 1), older ones kept. The
        // whole message is applied before the first of its events is published.
        h.ClearEvents();
        var seenOnUpdate = 0f;
        using var probe = h.Bus.Subscribe((in ObjectUpdated e) => seenOnUpdate = first.Position!.Value.Origin.X);
        var model = new ModelData(0x04000001, [], [], []);
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 5, position: 2, objDesc: 2), Position(Cell, x: 2), name: "Guard Captain", model: model, setup: 0x02000002), false);
        Assert.Same(first, h.State.Objects.TryGet(Npc, out var same) ? same : null);
        Assert.Equal(["ObjectUpdated", "ObjectMoved"], h.Names());
        Assert.Equal(ObjectChanges.Model | ObjectChanges.Physics | ObjectChanges.Weenie, h.Of<ObjectUpdated>()[0].Changes);
        Assert.Equal(PositionVerdict.Remote, h.Of<ObjectMoved>()[0].Verdict);
        Assert.True(h.Of<ObjectMoved>()[0].HasContact);
        Assert.Equal(2f, first.Position!.Value.Origin.X);
        Assert.Equal(2f, seenOnUpdate);
        Assert.Same(model, first.Model);

        // A newer instance re-creates.
        h.ClearEvents();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 6), Position(Cell, x: 3)), false);
        Assert.Equal(new ObjectCreated(Npc, false, true, true), Assert.Single(h.Of<ObjectCreated>()));
        Assert.Empty(h.Of<ObjectDeleted>());
        Assert.NotSame(first, h.State.Objects.TryGet(Npc, out var second) ? second : null);

        // An older instance drops.
        h.ClearEvents();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 5), Position(Cell)), false);
        Assert.Equal(new MessageDropped(MessageOpcodes.ObjectCreate, Npc, DropReason.OlderInstance), Assert.Single(h.Of<MessageDropped>()));
        Assert.Same(second, h.State.Objects.TryGet(Npc, out var still) ? still : null);
    }

    [Fact]
    public void UpdateObject_recreates_even_with_the_same_instance()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 5), Position(Cell)), false);
        h.State.Objects.TryGet(Npc, out var before);
        h.ClearEvents();

        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 5), Position(Cell)), isUpdate: true);
        Assert.Equal(new ObjectCreated(Npc, false, true, true), Assert.Single(h.Of<ObjectCreated>()));
        Assert.NotSame(before, h.State.Objects.TryGet(Npc, out var after) ? after : null);
    }

    [Fact]
    public void A_delete_of_the_player_is_ignored_and_others_are_gated()
    {
        var h = new ReducerHarness();
        h.EnterWorld(stamps: Stamps(instance: 3));
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 4), Position(Cell)), false);
        h.ClearEvents();

        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(PlayerGuid, 3));
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Npc, 3));
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Npc, 4));
        Assert.Equal(
            [new MessageDropped(MessageOpcodes.ObjectDelete, PlayerGuid, DropReason.SelfDelete), new MessageDropped(MessageOpcodes.ObjectDelete, Npc, DropReason.OlderInstance)],
            h.Of<MessageDropped>());
        Assert.Equal(Npc, Assert.Single(h.Of<ObjectDeleted>()).Guid);
        Assert.True(h.State.Objects.Contains(PlayerGuid));
        Assert.False(h.State.Objects.Contains(Npc));
    }

    [Fact]
    public void Messages_for_unknown_objects_are_parked_and_replayed_in_order_after_create()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.ClearEvents();

        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 1, 2));
        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, position: 2, teleport: 1, forcePosition: 1, x: 50));
        h.Reducer.OnVectorUpdate(h.Context, new VectorUpdateMsg(Npc, Vector3.UnitX, Vector3.UnitZ, 1, 2));
        h.Reducer.OnSound(h.Context, new SoundMsg(Npc, 0x13, 0.5f));
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(Npc, 1, 2, 1, false, Motion()));
        Assert.Empty(h.Events);
        Assert.Equal(5, h.Reducer.Parked.CountFor(Npc));

        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(Cell)), false);
        Assert.Equal(
            ["ObjectCreated", "ObjectStateChanged", "ObjectMoved", "ObjectVectorChanged", "ObjectSoundPlayed", "ObjectMotionChanged"],
            h.Names());
        Assert.True(h.State.Objects.TryGet(Npc, out var npc));
        Assert.Equal(PhysicsState.Ethereal, npc.State);
        Assert.Equal(50f, npc.Position!.Value.Origin.X);
        Assert.Equal(Vector3.UnitX, npc.Velocity);
        Assert.NotNull(npc.Motion);
        Assert.Equal(0, h.Reducer.Parked.Count);
        Assert.Equal(5, h.Reducer.Counters.Replayed);
    }

    [Fact]
    public void A_message_for_a_newer_instance_waits_for_that_instance()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 1), Position(Cell)), false);
        h.ClearEvents();

        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 2, 5));
        Assert.Empty(h.Events);
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 2), Position(Cell)), false);
        Assert.Equal(["ObjectCreated", "ObjectStateChanged"], h.Names());
        Assert.True(h.Of<ObjectCreated>()[0].Recreated);
    }

    [Fact]
    public void A_child_waits_for_its_unknown_parent_and_a_parent_event_for_its_unknown_child()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        const uint Torch = 0x80000200;
        const uint Sword = 0x80000201;
        h.ClearEvents();

        // A CreateObject naming an unknown parent parks under the parent.
        h.Reducer.OnCreateObject(h.Context, Create(Torch, Stamps(), parent: new ObjectLink(Npc, 1)), false);
        Assert.Equal(1, h.Reducer.Parked.CountFor(Npc));
        Assert.False(h.State.Objects.Contains(Torch));

        // A ParentEvent with a known parent but an unknown child parks under the child.
        h.Reducer.OnParentEvent(h.Context, new ParentEventMsg(PlayerGuid, Sword, 2, 0, 1, 2));
        Assert.Equal(1, h.Reducer.Parked.CountFor(Sword));

        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(Cell)), false);
        Assert.True(h.State.Objects.TryGet(Torch, out var torch));
        Assert.Equal(new ObjectAttachment(Npc, 1, 0), torch.Parent);
        Assert.True(torch.InWorld);
        Assert.Null(torch.Position);

        h.Reducer.OnCreateObject(h.Context, Create(Sword, Stamps(), Position(Cell)), false);
        Assert.True(h.State.Objects.TryGet(Sword, out var sword));
        Assert.Equal(new ObjectAttachment(PlayerGuid, 2, 0), sword.Parent);
        Assert.Null(sword.Position);
        Assert.Equal(new ObjectParented(Sword, PlayerGuid, 2, 0), h.Of<ObjectParented>()[^1]);
        Assert.Equal(["ObjectCreated", "ObjectCreated", "ObjectCreated", "ObjectParented"], h.Names());
    }

    [Theory]
    // player, stored (pos, tp, fp), incoming (pos, tp, fp), expected, stamps after (pos, tp, fp)
    [InlineData(true, 10, 5, 3, 2, 5, 4, "ForceBlip", 2, 5, 4)] // a newer force sequence blips, even with an older position
    [InlineData(true, 10, 5, 3, 11, 6, 4, "ForceBlip", 11, 5, 4)] // a newer teleport is not older: still a blip, teleport unstored
    [InlineData(true, 10, 5, 3, 11, 4, 4, "StaleTeleport", 10, 5, 4)] // force stored, then the older teleport drops it
    [InlineData(true, 10, 5, 3, 9, 5, 3, "Stale", 10, 5, 3)]
    [InlineData(true, 10, 5, 0xFFFF, 1, 5, 0, "ForceBlip", 1, 5, 0)] // the force sequence wraps forward too
    [InlineData(true, 10, 0, 3, 11, 0xFFFF, 4, "StaleTeleport", 10, 0, 4)] // 0xFFFF is older than 0 (the teleport sequence), the newer force is stored
    [InlineData(true, 10, 5, 3, 11, 5, 3, "SelfEcho", 11, 5, 3)]
    [InlineData(true, 10, 5, 3, 11, 6, 3, "SelfTeleport", 11, 6, 3)]
    [InlineData(true, 10, 5, 3, 10, 5, 3, "Stale", 10, 5, 3)]
    [InlineData(true, 10, 5, 3, 10, 6, 3, "Stale!", 10, 5, 3)] // retail's error count: an old position with a newer teleport
    [InlineData(true, 0xFFFF, 5, 3, 0, 5, 3, "SelfEcho", 0, 5, 3)] // 0xFFFF -> 0 is newer
    [InlineData(true, 0, 5, 3, 0xFFFF, 5, 3, "Stale", 0, 5, 3)]
    [InlineData(true, 10, 0xFFFF, 3, 11, 0, 3, "SelfTeleport", 11, 0, 3)]
    [InlineData(false, 10, 5, 3, 11, 5, 3, "Remote", 11, 5, 3)]
    [InlineData(false, 10, 5, 3, 11, 6, 3, "Remote+", 11, 6, 3)]
    [InlineData(false, 0xFFFE, 0xFFFF, 3, 0, 0, 3, "Remote+", 0, 0, 3)] // both counters wrap
    [InlineData(false, 10, 5, 3, 10, 5, 4, "Stale", 10, 5, 3)] // only the player blips
    [InlineData(false, 10, 5, 3, 11, 4, 3, "StaleTeleport", 10, 5, 3)]
    public void Position_verdicts_follow_HandleReceivedPosition(
        bool player,
        int storedPosition,
        int storedTeleport,
        int storedForce,
        int position,
        int teleport,
        int force,
        string expected,
        int positionAfter,
        int teleportAfter,
        int forceAfter)
    {
        var h = new ReducerHarness();
        var stamps = Stamps(position: (ushort)storedPosition, teleport: (ushort)storedTeleport, forcePosition: (ushort)storedForce);
        h.EnterWorld(stamps: player ? stamps : Stamps());
        var guid = player ? PlayerGuid : Npc;
        if (!player)
        {
            h.Reducer.OnCreateObject(h.Context, Create(Npc, stamps, Position(Cell)), false);
        }

        h.ClearEvents();
        h.Reducer.OnUpdatePosition(h.Context, Move(guid, 1, (ushort)position, (ushort)teleport, (ushort)force, x: 99));

        Assert.True(h.State.Objects.TryGet(guid, out var target));
        Assert.Equal(((ushort)positionAfter, (ushort)teleportAfter, (ushort)forceAfter), (target.Stamps.Position, target.Stamps.Teleport, target.Stamps.ForcePosition));
        switch (expected)
        {
            case "Stale" or "Stale!" or "StaleTeleport":
                var reason = expected == "StaleTeleport" ? DropReason.StaleTeleport : DropReason.Stale;
                Assert.Equal(new MessageDropped(MessageOpcodes.UpdatePosition, guid, reason), Assert.Single(h.Of<MessageDropped>()));
                Assert.Empty(h.Of<ObjectMoved>());
                Assert.NotEqual(99f, target.Position!.Value.Origin.X);
                Assert.Equal(expected == "Stale!" ? 1 : 0, h.Reducer.Counters.RetailErrorCount);
                break;
            default:
                var moved = Assert.Single(h.Of<ObjectMoved>());
                var verdict = expected == "Remote+" ? PositionVerdict.Remote : Enum.Parse<PositionVerdict>(expected);
                Assert.Equal(verdict, moved.Verdict);
                Assert.Equal(expected is "SelfTeleport" or "Remote+", moved.TeleportAdvanced);
                Assert.Equal(99f, moved.Position.Origin.X);
                Assert.Equal(99f, target.Position!.Value.Origin.X);
                Assert.Equal(player, moved.IsSelf);
                if (player)
                {
                    Assert.Equal(target.Stamps, h.State.Self.Stamps);
                    Assert.Equal(99f, h.State.Self.Position!.Value.Origin.X);
                }

                break;
        }
    }

    [Fact]
    public void A_position_from_an_older_instance_is_dropped_and_one_for_an_unknown_object_parked()
    {
        var h = new ReducerHarness();
        h.EnterWorld(stamps: Stamps(instance: 4));
        h.ClearEvents();
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 3, 9, 9, 9));
        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, 9, 9, 9));
        Assert.Equal(new MessageDropped(MessageOpcodes.UpdatePosition, PlayerGuid, DropReason.OlderInstance), Assert.Single(h.Of<MessageDropped>()));
        Assert.Equal(1, h.Reducer.Parked.CountFor(Npc));
    }

    [Fact]
    public void PlayerTeleport_not_older_announces_and_older_is_ignored()
    {
        var h = new ReducerHarness();

        // Before the player exists it waits for PlayerCreate, then does nothing without a player object.
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(7));
        Assert.Equal(1, h.Reducer.Parked.CountFor(PendingObjectMessages.WaitingForPlayer));
        h.EnterWorld(stamps: Stamps(teleport: 5));
        Assert.Empty(h.Of<PlayerTeleportAnnounced>());
        h.ClearEvents();

        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(4));
        Assert.Equal(new MessageDropped(MessageOpcodes.PlayerTeleport, PlayerGuid, DropReason.Stale), Assert.Single(h.Of<MessageDropped>()));
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(5));
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(6));
        Assert.Equal([(ushort)5, (ushort)6], h.Of<PlayerTeleportAnnounced>().Select(t => t.TeleportSequence));
        Assert.Equal(5, h.State.Self.Stamps.Teleport);
        Assert.Equal(SelfTravel.TeleportAnnounced, h.State.Self.Travel);
    }

    [Fact]
    public void Self_travel_follows_create_logincomplete_unhide_and_a_teleport()
    {
        var h = new ReducerHarness();
        h.EnterWorld(stamps: Stamps(teleport: 1, state: 1));
        Assert.Equal(SelfTravel.Arriving, h.State.Self.Travel);
        Assert.True(h.State.Self.Hidden);
        Assert.Equal("Tester", h.State.Self.Name);

        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.LoginCompleteSent, 0, null));
        Assert.Equal(SelfTravel.Materialising, h.State.Self.Travel);
        h.Reducer.OnSetState(h.Context, new SetStateMsg(PlayerGuid, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, 2));
        Assert.Equal(SelfTravel.Present, h.State.Self.Travel);
        Assert.False(h.State.Self.Hidden);

        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnSetState(h.Context, new SetStateMsg(PlayerGuid, PhysicsState.Hidden | PhysicsState.Gravity, 1, 3));
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 1, 2, 2, 1, cell: 0xA9B40019));
        Assert.Equal(SelfTravel.TeleportArrived, h.State.Self.Travel);
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.LoginCompleteSent, 0, null));
        h.Reducer.OnSetState(h.Context, new SetStateMsg(PlayerGuid, PhysicsState.Gravity, 1, 4));
        Assert.Equal(0xA9B40019u, h.State.Self.Position!.Value.CellId);

        Assert.Equal(
            [
                (SelfTravel.NotInWorld, SelfTravel.Arriving), (SelfTravel.Arriving, SelfTravel.Materialising),
                (SelfTravel.Materialising, SelfTravel.Present), (SelfTravel.Present, SelfTravel.TeleportAnnounced),
                (SelfTravel.TeleportAnnounced, SelfTravel.TeleportArrived), (SelfTravel.TeleportArrived, SelfTravel.Materialising),
                (SelfTravel.Materialising, SelfTravel.Present),
            ],
            h.Of<SelfTravelChanged>().Select(t => (t.Previous, t.Travel)));

        // LoginComplete while present changes nothing and says so.
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.LoginCompleteSent, 0, null));
        Assert.Equal(SelfTravel.Present, h.State.Self.Travel);
        Assert.True(h.Log.AnyLineContains("LoginComplete was sent while the player was not arriving"));
    }

    [Fact]
    public void Messages_before_PlayerCreate_wait_and_replay_in_order()
    {
        var h = new ReducerHarness();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(Cell)), false);
        h.Reducer.OnCreateObject(h.Context, Create(PlayerGuid, Stamps(), Position(Cell), state: PhysicsState.Hidden), false);
        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 1, 2));
        Assert.Equal(0, h.State.Objects.Count);
        Assert.Equal(3, h.Reducer.Parked.CountFor(PendingObjectMessages.WaitingForPlayer));

        h.Reducer.OnPlayerCreate(h.Context, new PlayerCreateMsg(PlayerGuid));
        Assert.Equal(["PlayerCreated", "ObjectCreated", "ObjectCreated", "SelfTravelChanged", "ObjectStateChanged"], h.Names());
        Assert.True(h.Of<ObjectCreated>()[1].IsSelf);
        Assert.Equal(2, h.State.Objects.Count);

        // A second PlayerCreate is ignored, as retail's HandleCreatePlayer does.
        h.Reducer.OnPlayerCreate(h.Context, new PlayerCreateMsg(0x50000002));
        Assert.Equal(PlayerGuid, h.State.Self.Guid);
        Assert.Single(h.Of<PlayerCreated>());
    }

    [Fact]
    public void Parking_is_bounded_per_object_in_all_and_in_time()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.ClearEvents();
        for (var i = 0; i < PendingObjectMessages.PerObjectLimit + 1; i++)
        {
            h.Reducer.OnSound(h.Context, new SoundMsg(Npc, (uint)i, 1));
        }

        Assert.Equal(PendingObjectMessages.PerObjectLimit, h.Reducer.Parked.CountFor(Npc));
        Assert.Equal(new MessageDropped(MessageOpcodes.Sound, Npc, DropReason.ParkingFull), Assert.Single(h.Of<MessageDropped>()));

        for (uint g = 1; h.Reducer.Parked.Count < PendingObjectMessages.TotalLimit; g++)
        {
            h.Reducer.OnSound(h.Context, new SoundMsg(0x81000000 + g, 0, 1));
        }

        h.Reducer.OnSound(h.Context, new SoundMsg(0x82000000, 0, 1));
        Assert.Equal(PendingObjectMessages.TotalLimit, h.Reducer.Parked.Count);
        Assert.Equal(2, h.Reducer.Counters.ParkingFull);

        h.Time.Advance(TimeSpan.FromSeconds(59.9));
        h.Reducer.ExpireParked();
        Assert.Equal(PendingObjectMessages.TotalLimit, h.Reducer.Parked.Count);
        h.Time.Advance(TimeSpan.FromSeconds(0.1));
        h.ClearEvents();
        h.Reducer.ExpireParked();
        Assert.Equal(0, h.Reducer.Parked.Count);
        Assert.Equal(PendingObjectMessages.TotalLimit, h.Of<MessageDropped>().Count(d => d.Reason == DropReason.ParkingExpired));
        Assert.Equal(PendingObjectMessages.TotalLimit, h.Reducer.Counters.ParkingExpired);
    }

    [Fact]
    public void A_message_parked_again_during_a_replay_keeps_its_first_parking_time()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 2, 2)); // waits for instance 2
        h.Time.Advance(TimeSpan.FromSeconds(40));
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(instance: 1), Position(Cell)), false);
        Assert.Equal(1, h.Reducer.Parked.CountFor(Npc));
        h.Time.Advance(TimeSpan.FromSeconds(20));
        h.Reducer.ExpireParked();
        Assert.Equal(0, h.Reducer.Parked.Count);
    }

    [Fact]
    public void Inventory_objects_are_kept_out_of_the_world_and_pickups_take_objects_out()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(0x80000300, Stamps(), name: "Pack"), false);
        Assert.False(h.Of<ObjectCreated>()[^1].InWorld);
        Assert.True(h.State.Objects.TryGet(0x80000300, out var pack));
        Assert.False(pack.InWorld);

        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(Cell)), false);
        h.Reducer.OnPickupEvent(h.Context, new PickupEventMsg(Npc, 1, 1));
        Assert.Equal(DropReason.Stale, h.Of<MessageDropped>()[^1].Reason);
        h.Reducer.OnPickupEvent(h.Context, new PickupEventMsg(Npc, 1, 2));
        Assert.Equal(Npc, Assert.Single(h.Of<ObjectPickedUp>()).Guid);
        Assert.True(h.State.Objects.TryGet(Npc, out var npc));
        Assert.False(npc.InWorld);
    }

    [Fact]
    public void Motion_takes_the_movement_and_server_control_sequences_as_SetObjectMovement_does()
    {
        var h = new ReducerHarness();
        h.EnterWorld(stamps: Stamps(movement: 5, serverControl: 5));
        h.ClearEvents();

        // The player's own autonomous echo is stored, flagged; consumers do not replay it.
        var echo = Motion(0x0007);
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(PlayerGuid, 1, 6, 5, true, echo));
        Assert.Equal(new ObjectMotionChanged(PlayerGuid, true, true, echo), Assert.Single(h.Of<ObjectMotionChanged>()));
        Assert.True(h.State.Objects.TryGet(PlayerGuid, out var p));
        Assert.Same(echo, p.Motion);
        Assert.True(p.MotionAutonomous);

        // An older server-control sequence: the movement sequence advances, the motion stays.
        var before = p.Motion;
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(PlayerGuid, 1, 7, 4, false, Motion(0x0005)));
        Assert.Same(before, p.Motion);
        Assert.Equal(7, p.Stamps.Movement);
        Assert.Equal(5, p.Stamps.ServerControl);

        // A stale movement sequence drops; a newer one with a newer server control applies.
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(PlayerGuid, 1, 7, 6, false, Motion(0x0005)));
        h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(PlayerGuid, 1, 8, 6, false, Motion(0x0005)));
        Assert.Equal(6, h.State.Self.Stamps.ServerControl);
        Assert.Equal(new ObjSeqs(1, 6, 1, 1), h.State.Self.Sequences);
        Assert.Equal(2, h.Of<MessageDropped>().Count(d => d.Reason == DropReason.Stale));
        Assert.Equal(2, h.Of<ObjectMotionChanged>().Count);
    }

    [Fact]
    public void Vector_state_and_description_change_only_on_newer_sequences()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(state: 3, vector: 3, objDesc: 3), Position(Cell), state: PhysicsState.Static), false);
        h.ClearEvents();

        h.Reducer.OnVectorUpdate(h.Context, new VectorUpdateMsg(Npc, Vector3.One, Vector3.UnitZ, 1, 3));
        h.Reducer.OnVectorUpdate(h.Context, new VectorUpdateMsg(Npc, Vector3.One, Vector3.UnitZ, 1, 4));
        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 1, 2));
        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Static, 1, 4)); // newer but unchanged: no event
        h.Reducer.OnSetState(h.Context, new SetStateMsg(Npc, PhysicsState.Ethereal, 1, 5));
        var model = new ModelData(0x04000002, [], [], []);
        h.Reducer.OnObjDescEvent(h.Context, new ObjDescEventMsg(Npc, model, 1, 3));
        h.Reducer.OnObjDescEvent(h.Context, new ObjDescEventMsg(Npc, model, 1, 4));

        Assert.Equal(
            ["MessageDropped", "ObjectVectorChanged", "MessageDropped", "ObjectStateChanged", "MessageDropped", "ObjectDescChanged"],
            h.Names());
        Assert.True(h.State.Objects.TryGet(Npc, out var npc));
        Assert.Equal(Vector3.One, npc.Velocity);
        Assert.Equal(new ObjectStateChanged(Npc, false, PhysicsState.Static, PhysicsState.Ethereal), h.Of<ObjectStateChanged>()[0]);
        Assert.Same(model, npc.Model);
    }

    [Fact]
    public void Effects_and_sounds_on_known_objects_are_published()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnPlayEffect(h.Context, new PlayEffectMsg(PlayerGuid, 0x76, 1.5f));
        h.Reducer.OnSound(h.Context, new SoundMsg(PlayerGuid, 0x0B, 0.25f));
        Assert.Equal(new ObjectScriptPlayed(PlayerGuid, 0x76, 1.5f), Assert.Single(h.Of<ObjectScriptPlayed>()));
        Assert.Equal(new ObjectSoundPlayed(PlayerGuid, 0x0B, 0.25f), Assert.Single(h.Of<ObjectSoundPlayed>()));
    }

    [Fact]
    public void A_same_instance_create_of_a_parented_object_applies_the_parent_on_its_position_sequence()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(position: 4), Position(Cell)), false);
        h.ClearEvents();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(position: 5), parent: new ObjectLink(PlayerGuid, 3)), false);
        Assert.Equal(["ObjectUpdated", "ObjectParented"], h.Names());
        Assert.True(h.State.Objects.TryGet(Npc, out var npc));
        Assert.Equal(new ObjectAttachment(PlayerGuid, 3, 0), npc.Parent);
        Assert.Null(npc.Position);

        // A position update then unparents it (HandleReceivedPosition unsets the parent).
        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, 6, 1, 1));
        Assert.Null(npc.Parent);
        Assert.NotNull(npc.Position);
    }

    [Fact]
    public void Health_use_done_and_unknown_messages_are_published()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, Create(Npc, Stamps(), Position(Cell)), false);
        h.ClearEvents();
        var header = new GameEventHeader(PlayerGuid, 1, GameEventTypes.UpdateHealth);
        h.Reducer.OnUpdateHealth(h.Context, header, new UpdateHealthEvent(Npc, 0.5f));
        h.Reducer.OnUseDone(h.Context, header, new UseDoneEvent(0x0402));
        h.Reducer.OnUnknown(h.Context, 0xF7DE, [0xDE, 0xF7, 0, 0]);
        h.Reducer.OnUnknown(h.Context, 0xF7DE, [0xDE, 0xF7, 0, 0]);
        h.Reducer.OnGameEvent(h.Context, new GameEventHeader(PlayerGuid, 2, 0x0282), []);
        h.Reducer.OnMalformed(h.Context, 0xF748, 3, "truncated");

        Assert.True(h.State.Objects.TryGet(Npc, out var npc));
        Assert.Equal(0.5f, npc.Health);
        Assert.Equal(new HealthUpdated(Npc, 0.5f), Assert.Single(h.Of<HealthUpdated>()));
        Assert.Equal(new UseDone(0x0402), Assert.Single(h.Of<UseDone>()));
        Assert.Equal([new UnknownMessage(0xF7DE, 0), new UnknownMessage(0xF7DE, 0), new UnknownMessage(0xF7B0, 0x0282)], h.Of<UnknownMessage>());
        Assert.Equal(2, h.Log.Entries.Count(e => e.Message.Contains("first seen", StringComparison.Ordinal)));
        Assert.Equal(new MessageDropped(0xF748, 0, DropReason.Malformed), Assert.Single(h.Of<MessageDropped>()));
    }

    [Fact]
    public void Qualities_follow_the_description_and_private_updates_by_their_sequences()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        var header = new GameEventHeader(PlayerGuid, 1, GameEventTypes.PlayerDescription);
        h.Reducer.OnPlayerDescription(h.Context, header, new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(25, 1),
            Vitals = [new VitalDesc(1, 0, 50, 0, 50)],
            Attributes = [new AttributeDesc(1, 0, 100, 0)],
            Skills = [new SkillDesc(24, 10, 1, 2, 0, 0, 0, 0)],
        });
        var q = h.State.Self.Qualities;
        Assert.True(q.IsKnown);
        Assert.True(q.TryGetInt(25, out var level) && level == 1);

        Update(PrivateUpdateKind.PropertyInt, 10, 25, 2);
        Update(PrivateUpdateKind.PropertyInt, 9, 25, 3); // older: refused
        Update(PrivateUpdateKind.PropertyInt, 10, 25, 4); // equal: accepted
        Assert.True(q.TryGetInt(25, out level) && level == 4);
        Update(PrivateUpdateKind.PropertyInt, 0, 25, 5); // 255 -> 0 needs a wrap; 10 -> 0 is older
        Assert.True(q.TryGetInt(25, out level) && level == 4);
        Update(PrivateUpdateKind.PropertyInt, 200, 25, 6); // 10 -> 200: a distance over 127 is a wrap, so older
        Assert.True(q.TryGetInt(25, out level) && level == 4);
        Update(PrivateUpdateKind.PropertyInt, 100, 25, 7);
        Assert.True(q.TryGetInt(25, out level) && level == 7);

        Update(PrivateUpdateKind.VitalCurrent, 1, 2, 40);
        Assert.True(q.TryGetVital(1, out var vital) && vital.Current == 40 && vital.Start == 50); // key 2 (the current health's id) updates the vital the description keyed 1
        Update(PrivateUpdateKind.VitalCurrent, 3, 6, 9); // an unknown vital starts from its current value
        Assert.True(q.TryGetVital(5, out vital) && vital.Current == 9);

        Assert.Equal(
            [QualityChange.Description, QualityChange.Int, QualityChange.Int, QualityChange.Int, QualityChange.VitalCurrent, QualityChange.VitalCurrent],
            h.Of<QualitiesChanged>().Select(c => c.Change));
        Assert.Equal(3, h.Of<MessageDropped>().Count(d => d.Opcode == MessageOpcodes.PrivateUpdatePropertyInt));

        void Update(PrivateUpdateKind kind, byte sequence, uint key, long value) =>
            h.Reducer.OnPrivateUpdate(h.Context, new PrivateUpdateMsg(kind, sequence, key, value, default, default, default));
    }
}
