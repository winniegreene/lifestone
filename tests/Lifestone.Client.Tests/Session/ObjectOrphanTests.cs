using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.ReducerHarness;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 7 P7d (D139 row f): the orphans round 13's soak kept (about 110 a den stretch). A deleted container's contents leave with its F747; an object
/// that leaves the player's holdings (GM 0x0024) or moves into a container the player does not see (GE 0x0022) leaves 25 s later with what it holds,
/// as retail's <c>AddObjectToBeDestroyed</c> (11.6096 0x00509A40) from its 0x0024 handler and <c>CPlayerSystem::RecvNotice_ServerSaysMoveItem</c>
/// (0x00564000); what the player holds or wields never goes.
/// </summary>
public sealed class ObjectOrphanTests
{
    private const uint Home = 0x860201AD;
    private const uint Corpse = 0x80000300;
    private const uint Npc = 0x80000310;
    private const uint Vendor = 0x80000320;
    private const uint Unknown = 0x80000399; // a container this client never heard of
    private const uint PackItem = 0x80000400;
    private const uint Wielded = 0x80000401;
    private const uint Looted = 0x80000402;
    private const uint SidePack = 0x80000403;
    private const uint InSidePack = 0x80000404;

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

    private static void Spawn(ReducerHarness h, uint guid) =>
        h.Reducer.OnCreateObject(h.Context, Create(guid, Stamps(), Position(Home), name: "Corpse of Tusker"), isUpdate: false);

    private static void Item(ReducerHarness h, uint guid, uint? container = null, uint? wielder = null, ObjectLink? parent = null)
    {
        var basis = Create(guid, Stamps(), null, parent: parent, name: "Item");
        var header = (container is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Container) | (wielder is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Wielder);
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { Header = header, ContainerId = container, WielderId = wielder } }, isUpdate: false);
    }

    private static void PutIn(ReducerHarness h, uint item, uint container) =>
        h.Reducer.OnInventoryPutObjInContainer(h.Context, new GameEventHeader(PlayerGuid, 9, GameEventTypes.InventoryPutObjInContainer), new InventoryPutObjInContainerEvent(item, container, 0, 0));

    /// <summary>The player with a pack item and a wielded item the model knows (0x0013 PlayerDescription).</summary>
    private static ReducerHarness Holding()
    {
        var h = World();
        h.Reducer.OnPlayerDescription(
            h.Context,
            new GameEventHeader(PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Inventory = [new ContentDesc(PackItem, 0)], Equipped = [new EquippedDesc(Wielded, 0x00100000, 0)] });
        Item(h, PackItem, container: PlayerGuid);
        Item(h, Wielded, wielder: PlayerGuid, parent: new ObjectLink(PlayerGuid, 1));
        return h;
    }

    [Fact]
    public void A_rotted_corpse_takes_its_five_items_with_it_in_one_logged_line()
    {
        var h = World();
        Spawn(h, Corpse);
        h.Reducer.OnViewContents(h.Context, new GameEventHeader(PlayerGuid, 1, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, [.. Enumerable.Range(0, 5).Select(i => new ViewContentsEntry(0x80000500u + (uint)i, 0))]));
        for (uint i = 0; i < 5; i++)
        {
            Item(h, 0x80000500 + i, container: Corpse);
        }

        h.Reducer.OnCloseGroundContainer(h.Context, new GameEventHeader(PlayerGuid, 2, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(Corpse));
        var before = h.State.Objects.Count;
        h.ClearEvents();

        // ACE's corpse decay: one F747 for the corpse, nothing for what was inside it.
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Corpse, 1));

        Assert.False(h.State.Objects.Contains(Corpse));
        for (uint i = 0; i < 5; i++)
        {
            Assert.False(h.State.Objects.Contains(0x80000500 + i));
        }

        Assert.Equal(before - 6, h.State.Objects.Count);
        Assert.Equal(6, h.Of<ObjectDeleted>().Count()); // every consumer hears each one go, the holder first
        Assert.Equal(Corpse, h.Of<ObjectDeleted>().First().Guid);
        Assert.Equal(5, h.Reducer.Cull.OrphansDropped);
        var line = Assert.Single(h.Log.Entries, e => e.Category == GameStateReducer.ObjectsLogCategory && e.Message.Contains("contents left with it", StringComparison.Ordinal));
        Assert.Equal(LogSeverity.Info, line.Severity);
    }

    [Fact]
    public void A_deleted_object_that_holds_nothing_logs_nothing_and_a_creatures_wielded_item_goes_with_it()
    {
        var h = World();
        Spawn(h, Npc);
        Item(h, 0x80000600, wielder: Npc, parent: new ObjectLink(Npc, 1));
        Spawn(h, Corpse);

        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Corpse, 1));
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("contents left with it", StringComparison.Ordinal));

        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Npc, 1));
        Assert.False(h.State.Objects.Contains(0x80000600));
    }

    [Fact]
    public void The_players_pack_and_wielded_items_never_go_with_a_deleted_holder_or_by_the_doom_rule()
    {
        var h = Holding();
        Spawn(h, Corpse);
        Item(h, Looted, container: Corpse);

        // A corpse item looted into the pack before the corpse rots: the pack holds it, so the corpse's F747 leaves it.
        PutIn(h, Looted, PlayerGuid);
        h.Reducer.OnDeleteObject(h.Context, new DeleteObjectMsg(Corpse, 1));
        for (var i = 0; i < 60; i++)
        {
            Tick(h, 1.0);
        }

        Assert.True(h.State.Objects.Contains(PackItem));
        Assert.True(h.State.Objects.Contains(Wielded));
        Assert.True(h.State.Objects.Contains(Looted));
        Assert.True(h.State.Self.Inventory.Contains(Looted));
        Assert.Equal(0, h.Reducer.Cull.OrphansDropped);
        Assert.Equal(0, h.Reducer.Cull.DoomPending);
    }

    [Fact]
    public void An_item_moved_to_a_container_the_client_does_not_know_is_gone_25_s_later()
    {
        var h = Holding();
        PutIn(h, PackItem, Unknown);
        Assert.False(h.State.Self.Inventory.Contains(PackItem));
        Assert.True(h.Reducer.Cull.IsDoomed(PackItem));
        Assert.Single(h.Log.Entries, e => e.Message.StartsWith("an object left the player's holdings", StringComparison.Ordinal) && e.Message.Contains("25 s", StringComparison.Ordinal));

        Tick(h, ObjectCull.CullSeconds - 1.0);
        Assert.True(h.State.Objects.Contains(PackItem)); // retail's 25 s
        h.ClearEvents();
        Tick(h, 1.0);

        Assert.False(h.State.Objects.Contains(PackItem));
        Assert.Equal(PackItem, Assert.Single(h.Of<ObjectDeleted>()).Guid);
        Assert.Equal(1, h.Reducer.Cull.OrphansDropped);
        Assert.True(h.State.Objects.Contains(Wielded));
        var batch = Assert.Single(h.Log.Entries, e => e.Message.StartsWith("culled", StringComparison.Ordinal));
        Assert.Contains(batch.Fields, f => f.Key == "orphans" && Equals(f.Value, 1));
    }

    [Fact]
    public void A_removed_item_and_one_sold_to_a_known_vendor_go_with_what_they_hold_and_the_wielded_item_stays()
    {
        var h = Holding();
        Spawn(h, Vendor);
        Item(h, SidePack, container: PlayerGuid);
        Item(h, InSidePack, container: SidePack);

        // 0024 (a stack merged away, a component burnt, an item given) and 0022 into the vendor (ACE's sell: no 0024, no F747).
        h.Reducer.OnInventoryRemoveObject(h.Context, new InventoryRemoveObjectMsg(SidePack));
        PutIn(h, PackItem, Vendor);
        Assert.Equal(2, h.Reducer.Cull.DoomPending);
        Tick(h, ObjectCull.CullSeconds + 0.5);

        Assert.False(h.State.Objects.Contains(SidePack));
        Assert.False(h.State.Objects.Contains(InSidePack)); // retail's AddContentsToDestructionQueue
        Assert.False(h.State.Objects.Contains(PackItem));
        Assert.True(h.State.Objects.Contains(Vendor));
        Assert.True(h.State.Objects.Contains(Wielded));
        Assert.Equal(3, h.Reducer.Cull.OrphansDropped);
    }

    [Fact]
    public void A_doomed_item_that_comes_back_to_the_player_or_into_the_open_ground_container_stays()
    {
        var h = Holding();
        Spawn(h, Corpse);
        h.Reducer.OnViewContents(h.Context, new GameEventHeader(PlayerGuid, 1, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, []));

        // Away to an unknown container and straight back to the pack: lifted (RemoveObjectToBeDestroyed), logged.
        PutIn(h, PackItem, Unknown);
        PutIn(h, PackItem, PlayerGuid);
        Assert.False(h.Reducer.Cull.IsDoomed(PackItem));
        Assert.Single(h.Log.Entries, e => e.Message.StartsWith("a doomed object came back", StringComparison.Ordinal));

        // Into the open corpse: never doomed (the open ground container is the player's to see).
        PutIn(h, PackItem, Corpse);
        Assert.False(h.Reducer.Cull.IsDoomed(PackItem));

        // Given away (0024), then created again by the server (a container opened that holds it): the create lifts the doom.
        h.Reducer.OnInventoryRemoveObject(h.Context, new InventoryRemoveObjectMsg(Wielded));
        Assert.True(h.Reducer.Cull.IsDoomed(Wielded));
        Item(h, Wielded, container: Corpse);
        Assert.False(h.Reducer.Cull.IsDoomed(Wielded));

        Tick(h, ObjectCull.CullSeconds + 1.0);
        Assert.True(h.State.Objects.Contains(PackItem));
        Assert.True(h.State.Objects.Contains(Wielded));
        Assert.Equal(0, h.Reducer.Cull.OrphansDropped);
    }

    [Fact]
    public void A_doomed_item_whose_time_comes_while_it_is_back_in_the_pack_is_kept_and_logged()
    {
        var h = Holding();

        // The item returns to the model by a route the doom does not hear (here: the model itself); at its time the check finds the player holds it.
        h.Reducer.OnInventoryRemoveObject(h.Context, new InventoryRemoveObjectMsg(PackItem));
        h.State.Self.Inventory.PutInContainer(PackItem, 0, PlayerGuid, 0);
        Tick(h, ObjectCull.CullSeconds + 1.0);

        Assert.True(h.State.Objects.Contains(PackItem));
        Assert.Single(h.Log.Entries, e => e.Message.StartsWith("doomed objects kept", StringComparison.Ordinal));
        Assert.Equal(0, h.Reducer.Cull.DoomPending);
    }

    [Fact]
    public void A_log_off_forgets_the_doomed_entries()
    {
        var h = Holding();
        h.Reducer.OnInventoryRemoveObject(h.Context, new InventoryRemoveObjectMsg(PackItem));
        Assert.Equal(1, h.Reducer.Cull.DoomPending);

        h.Reducer.OnCharacterLogOff(h.Context);

        Assert.Equal(0, h.Reducer.Cull.DoomPending);
    }
}
