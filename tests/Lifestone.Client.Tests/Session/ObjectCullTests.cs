using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.State;
using Lifestone.Client.Tests.Avatars;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 item 0 c1 (<c>m5-object-cull</c>, D16): retail's client-side cull (<c>CObjectMaint::AddObjectToBeDestroyed</c>
/// 0x00509A40, <c>UseTime</c> 0x00509480, 11.6096). An object outside the player's landblock and its eight neighbours for
/// 25 s on the session clock leaves the table through the F747 path; a message for it cancels; the player's holdings and
/// the open ground container stay; a culled guid created again is a fresh entry.
/// </summary>
public sealed class ObjectCullTests
{
    private const uint Home = 0x860201AD; // the player's cell: landblock 0x8602
    private const uint Near = 0x870301AD; // a diagonal neighbour landblock: visible
    private const uint Far = 0x890201AD;  // three landblocks east: outside
    private const uint Npc = 0x80000100;
    private const uint Npc2 = 0x80000101;
    private const uint Sword = 0x80000200;
    private const uint Corpse = 0x80000300;
    private const uint Loot = 0x80000301;
    private const uint PackItem = 0x80000400;
    private const uint Wielded = 0x80000401;

    private static ReducerHarness World()
    {
        var h = new ReducerHarness();
        h.EnterWorld(Home);
        return h;
    }

    private static void Tick(ReducerHarness h, double seconds)
    {
        h.Time.Advance(TimeSpan.FromSeconds(seconds));
        h.Reducer.CullObjects();
    }

    private static void Spawn(ReducerHarness h, uint guid, uint cell, PhysicsTimestamps? stamps = null, PhysicsState state = PhysicsState.None) =>
        h.Reducer.OnCreateObject(h.Context, Create(guid, stamps ?? Stamps(), Position(cell), state: state, name: "Tusker"), isUpdate: false);

    private static void Item(ReducerHarness h, uint guid, uint? container = null, uint? wielder = null, ObjectLink? parent = null)
    {
        var basis = Create(guid, Stamps(), null, parent: parent, name: "Item");
        var header = (container is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Container) | (wielder is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Wielder);
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { Header = header, ContainerId = container, WielderId = wielder } }, isUpdate: false);
    }

    private static void MovePlayer(ReducerHarness h, uint cell, ushort stamp) =>
        h.Reducer.OnUpdatePosition(h.Context, Move(PlayerGuid, 1, stamp, 1, 1, cell));

    [Fact]
    public void An_object_unseen_for_the_cull_time_leaves_the_table_and_the_avatar_set_in_one_logged_batch()
    {
        var h = World();
        var avatars = new AvatarSet(AvatarRig.Sources());
        h.Bus.Subscribe((in ObjectCreated e) => avatars.Apply(e, AvatarRig.HumanAt(e.Guid, new Vector3(10, 10, 0)), now: 0));
        h.Bus.Subscribe((in ObjectDeleted e) => avatars.Apply(e));
        Spawn(h, Npc, Far);
        Spawn(h, Npc2, Far);
        Assert.Equal(2, avatars.Count); // the player entered before the subscription

        Tick(h, 0.5);
        Assert.Equal(2, h.Reducer.Cull.Pending);
        Tick(h, ObjectCull.CullSeconds - 1.0);
        Assert.True(h.State.Objects.Contains(Npc));
        h.ClearEvents();

        Tick(h, 1.0);

        Assert.False(h.State.Objects.Contains(Npc));
        Assert.False(h.State.Objects.Contains(Npc2));
        Assert.True(h.State.Objects.Contains(PlayerGuid));
        Assert.Equal([Npc, Npc2], h.Of<ObjectDeleted>().Select(e => e.Guid).Order());
        Assert.Equal(0, avatars.Count);
        Assert.Equal(0, h.Reducer.Cull.Pending);
        Assert.Equal(2, h.Reducer.Cull.Culled);
        var batch = Assert.Single(h.Log.Entries, e => e.Category == GameStateReducer.ObjectsLogCategory && e.Message.StartsWith("culled", StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Info, batch.Severity);
        Assert.Single(h.Log.Entries, e => e.Message.Contains("TODO(m5-object-cull-cells)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_object_in_the_players_landblock_or_a_neighbour_is_never_queued()
    {
        var h = World();
        Spawn(h, Npc, Home);
        Spawn(h, Npc2, Near);

        for (var i = 0; i < 60; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(Npc));
        Assert.True(h.State.Objects.Contains(Npc2));
        Assert.Equal(0, h.Reducer.Cull.Pending);
    }

    [Fact]
    public void An_object_seen_again_before_the_time_stays()
    {
        var h = World();
        Spawn(h, Npc, Far);
        Tick(h, 0.5);
        Assert.True(h.Reducer.Cull.IsPending(Npc));

        // The server sends it again (ACE keeps updating an object for 25 s after it left its view): the entry is cancelled, and re-queued with a fresh time.
        Tick(h, 20.0);
        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, 2, 1, 1, Far));
        Assert.False(h.Reducer.Cull.IsPending(Npc));
        Tick(h, 10.0);
        Assert.True(h.State.Objects.Contains(Npc));
        Assert.True(h.Reducer.Cull.IsPending(Npc));

        // It walks back into view: no longer pending, kept for good.
        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, 3, 1, 1, Near));
        for (var i = 0; i < 40; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(Npc));
        Assert.False(h.Reducer.Cull.IsPending(Npc));
    }

    [Fact]
    public void The_player_walking_back_before_the_time_keeps_the_object()
    {
        var h = World();
        Spawn(h, Npc, Home);
        MovePlayer(h, Far, 2);
        Tick(h, 1.0);
        Assert.True(h.Reducer.Cull.IsPending(Npc));

        Tick(h, 20.0);
        MovePlayer(h, Home, 3);
        Tick(h, 1.0);
        Tick(h, 30.0);

        Assert.True(h.State.Objects.Contains(Npc));
        Assert.Equal(0, h.Reducer.Cull.Culled);
    }

    [Fact]
    public void A_creature_the_server_re_sends_is_one_entry_not_two()
    {
        var h = World();
        Spawn(h, Npc, Far);
        Tick(h, 1.0);
        Assert.True(h.Reducer.Cull.IsPending(Npc));
        var before = h.State.Objects.Count;
        h.ClearEvents();

        Spawn(h, Npc, Far);

        Assert.Equal(before, h.State.Objects.Count);
        Assert.Single(h.State.Objects.All, o => o.Guid == Npc);
        Assert.False(h.Reducer.Cull.IsPending(Npc));
        Assert.Empty(h.Of<ObjectDeleted>());
    }

    [Fact]
    public void A_culled_creature_takes_what_it_wields_with_it()
    {
        var h = World();
        Spawn(h, Npc, Far);
        Item(h, Sword, wielder: Npc, parent: new ObjectLink(Npc, 1));
        Assert.True(h.State.Objects.Contains(Sword));

        Tick(h, 1.0);
        Assert.False(h.Reducer.Cull.IsPending(Sword)); // no world position of its own: it follows its holder
        Tick(h, ObjectCull.CullSeconds);

        Assert.False(h.State.Objects.Contains(Npc));
        Assert.False(h.State.Objects.Contains(Sword));
        Assert.Equal(2, h.Reducer.Cull.Culled);
    }

    [Fact]
    public void The_player_its_inventory_and_wielded_items_are_never_culled()
    {
        var h = World();
        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [new ContentDesc(PackItem, 0)], Equipped = [new EquippedDesc(Wielded, 0x00100000, 0)] });
        Item(h, PackItem, container: PlayerGuid);
        Item(h, Wielded, wielder: PlayerGuid, parent: new ObjectLink(PlayerGuid, 1));
        Assert.True(h.State.Self.Inventory.Contains(PackItem));

        // The player portals far away and stays there: everything the player holds goes along.
        MovePlayer(h, Far, 2);
        for (var i = 0; i < 90; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(PlayerGuid));
        Assert.True(h.State.Objects.Contains(PackItem));
        Assert.True(h.State.Objects.Contains(Wielded));
        Assert.Equal(0, h.Reducer.Cull.Culled);
    }

    [Fact]
    public void An_open_ground_container_and_its_contents_stay_and_leave_together_once_closed()
    {
        var h = World();
        Spawn(h, Corpse, Far);
        h.Reducer.OnViewContents(h.Context, new GameEventHeader(PlayerGuid, 1, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, [new ViewContentsEntry(Loot, 0)]));
        Item(h, Loot, container: Corpse);
        Assert.Equal(Corpse, h.State.Containers.ViewedGuid);

        for (var i = 0; i < 60; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(Corpse));
        Assert.True(h.State.Objects.Contains(Loot));
        Assert.False(h.Reducer.Cull.IsPending(Corpse));

        h.Reducer.OnCloseGroundContainer(h.Context, new GameEventHeader(PlayerGuid, 2, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(Corpse));
        Tick(h, 1.0);
        Assert.True(h.Reducer.Cull.IsPending(Corpse));
        Tick(h, ObjectCull.CullSeconds);

        Assert.False(h.State.Objects.Contains(Corpse));
        Assert.False(h.State.Objects.Contains(Loot));
    }

    [Fact]
    public void A_static_object_is_kept_as_retail_keeps_it()
    {
        var h = World();
        Spawn(h, Npc, Far, state: PhysicsState.Static);
        for (var i = 0; i < 40; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(Npc));
    }

    [Fact]
    public void A_guid_created_again_after_its_cull_is_a_fresh_entry_whose_positions_apply()
    {
        var h = World();
        Spawn(h, Npc, Far, Stamps(position: 500, objDesc: 500));
        Tick(h, 1.0);
        Tick(h, ObjectCull.CullSeconds);
        Assert.False(h.State.Objects.Contains(Npc));
        h.ClearEvents();

        // ACE hands the guid to a new monster (GuidManager recycles after 6 h, the instance stamp unchanged): a fresh entry, not a merge into the ghost.
        Spawn(h, Npc, Home);
        var created = Assert.Single(h.Of<ObjectCreated>());
        Assert.False(created.Recreated);
        Assert.True(h.State.Objects.TryGet(Npc, out var fresh));
        Assert.Equal((ushort)1, fresh.Stamps.Position);
        Assert.Equal(h.Clock.Now, fresh.CreatedAt, 3);

        h.Reducer.OnUpdatePosition(h.Context, Move(Npc, 1, 2, 1, 1, Home, x: 42));
        Assert.Equal(42f, fresh.Position!.Value.Frame.Origin.X);
        Assert.Empty(h.Of<MessageDropped>());
    }

    [Fact]
    public void A_server_delete_and_a_log_off_forget_the_pending_entries()
    {
        var h = World();
        Spawn(h, Npc, Far);
        Spawn(h, Npc2, Far);
        Tick(h, 1.0);
        Assert.Equal(2, h.Reducer.Cull.Pending);

        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Npc, 1));
        Assert.Equal(1, h.Reducer.Cull.Pending);

        h.Reducer.OnCharacterLogOff(h.Context);
        Assert.Equal(0, h.Reducer.Cull.Pending);
        Tick(h, ObjectCull.CullSeconds + 1);
        Assert.Equal(0, h.Reducer.Cull.Culled);
    }

    [Theory]
    [InlineData(0x860201ADu, 0x860201ADu, true)]
    [InlineData(0x850101ADu, 0x860201ADu, true)]
    [InlineData(0x870301ADu, 0x860201ADu, true)]
    [InlineData(0x86040100u, 0x860201ADu, false)]
    [InlineData(0x880201ADu, 0x860201ADu, false)]
    [InlineData(0x00FF0001u, 0x01FE0001u, true)]
    public void Visible_is_the_players_landblock_and_its_eight_neighbours(uint cell, uint player, bool visible) =>
        Assert.Equal(visible, ObjectCull.Visible(cell, player));

    [Fact]
    public void The_login_check_object_fields_read_n_a_until_reached()
    {
        Assert.Equal(" objects=212 drawn_in_cell=31 light_rows=29", LoginCheckScenario.ObjectFields(212, 31, 29));
        Assert.Equal(" objects=n/a drawn_in_cell=n/a light_rows=n/a", LoginCheckScenario.ObjectFields(-1, -1, -1));
    }
}

/// <summary>The cull's tick allocates nothing in the steady state (objects queued and cancelled as the player moves). Measured alone.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class ObjectCullAllocationTests
{
    [Fact]
    public void The_cull_tick_allocates_nothing_while_objects_are_queued_and_cancelled()
    {
        const uint A = 0x860201AD;
        const uint B = 0x900201AD;
        var h = new ReducerHarness(track: false);
        h.EnterWorld(A);
        for (uint i = 0; i < 200; i++)
        {
            h.Reducer.OnCreateObject(h.Context, Create(0x80001000 + i, Stamps(), Position(i % 2 == 0 ? A : B)), false);
        }

        ushort stamp = 2;
        var context = h.Context;
        AllocationAssert.Nothing(() =>
        {
            for (var round = 0; round < 10; round++)
            {
                h.Time.Advance(TimeSpan.FromSeconds(1.1));
                h.Reducer.OnUpdatePosition(context, Move(PlayerGuid, 1, stamp++, 1, 1, B));
                h.Reducer.CullObjects();
                h.Time.Advance(TimeSpan.FromSeconds(1.1));
                h.Reducer.OnUpdatePosition(context, Move(PlayerGuid, 1, stamp++, 1, 1, A));
                h.Reducer.CullObjects();
            }
        });

        Assert.Equal(201, h.State.Objects.Count);
        Assert.Equal(0, h.Reducer.Cull.Culled);
        Assert.Equal(100, h.Reducer.Cull.Pending);
    }
}
