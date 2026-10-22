using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s1: the container model, fed message by message in the order ACE really sends (cache/m4/2-1/capture.md C1 to C4): the 0196 names guids before their F745s at login, on a pack's arrival and for a
/// corpse; a close is a 0052; the split's 0022 comes before the new guid's F745; a give's 0024 and foreign 0022 take the item out of the inventory.
/// </summary>
public sealed class ReducerContainerTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Sack = 0x80000010;
    private const uint Pack = 0x80000011;
    private const uint Pyreals = 0x80000101;
    private const uint Gem = 0x80000102;
    private const uint Focus = 0x80000012;
    private const uint Corpse = 0x80000201;
    private const uint ChestGuid = 0x7A9B401D;
    private const uint Alfrin = 0x80000301;
    private const uint Arrows = 0x80000401;
    private const uint NewArrows = 0x80000402;
    private const uint Mace = 0x80000403;

    private sealed class Rig
    {
        private uint _event;

        public Rig()
        {
            Harness = new ReducerHarness();
            Harness.EnterWorld();
        }

        public ReducerHarness Harness { get; }

        public PlayerInventory Inventory => Harness.State.Self.Inventory;

        public ContainerViews Views => Harness.State.Containers;

        public void Describe(params ContentDesc[] pack)
        {
            Harness.Reducer.OnPlayerDescription(
                Harness.Context,
                new GameEventHeader(Player, Next(), GameEventTypes.PlayerDescription),
                new PlayerDescriptionMsg { Inventory = [.. pack], Equipped = [] });
            Harness.ClearEvents();
        }

        public void View(uint container, params (uint Guid, uint Type)[] entries) =>
            Harness.Reducer.OnViewContents(
                Harness.Context,
                new GameEventHeader(Player, Next(), GameEventTypes.ViewContents),
                new ViewContentsEvent(container, [.. entries.Select(e => new ViewContentsEntry(e.Guid, e.Type))]));

        public void Close(uint container) =>
            Harness.Reducer.OnCloseGroundContainer(Harness.Context, new GameEventHeader(Player, Next(), GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(container));

        /// <summary>An F745 (no position), the way a pack item or a corpse's item arrives.</summary>
        public WorldObject Create(uint guid, string name, uint? container = null, uint itemType = 0, uint? value = null)
        {
            var header = WeenieHeaderFlags.None;
            header |= container is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Container;
            header |= value is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Value;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
            var created = basis with { Weenie = basis.Weenie with { Header = header, ItemType = itemType, ContainerId = container, Value = value } };
            Harness.Reducer.OnCreateObject(Harness.Context, created, isUpdate: false);
            Assert.True(Harness.State.Objects.TryGet(guid, out var o));
            return o;
        }

        public void Send(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Harness.Context, message, Harness.Reducer).Outcome);

        public void Put(uint item, uint container, uint placement, uint type = 0) => Send(AceM3Writers.PutObjInContainer(Player, Next(), item, container, placement, type));

        public void Delete(uint guid) => Harness.Reducer.OnDeleteObject(Harness.Context, new DeleteObjectMsg(guid, 1));

        public uint Next() => ++_event;

        public List<uint> Items(uint container) => [.. Inventory.ItemsIn(container).Select(e => e.Guid)];
    }

    private static int Missing(Rig r) => LoginCompleteTracker.MissingContents(r.Harness.State, out _);

    [Fact]
    public void C1_login_the_0196s_before_the_F745s_hold_the_entries_and_resolve_as_each_F745_lands()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Sack, 1), new ContentDesc(Pack, 1));

        // The first event bundle: one 0196 per side pack, before the F746 and before every F745.
        r.View(Sack);
        r.View(Pack, (Pyreals, 0));

        Assert.Equal([Pyreals], r.Items(Pack));
        Assert.Empty(r.Items(Sack));
        Assert.True(r.Views.ContentsNamed(Pack));
        Assert.True(r.Views.TryGet(Pack, out var view));
        Assert.Equal(Pack, view.Guid);
        Assert.False(view.IsGround);
        Assert.Equal(1, view.Unresolved);
        Assert.False(view.IsResolved);
        Assert.True(r.Views.TryGet(Sack, out var empty));
        Assert.True(empty.IsResolved);
        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.Equal(2, r.Harness.Of<ContainerContentsChanged>().Count);

        r.Harness.ClearEvents();
        r.Create(Sack, "Sack", container: Player, itemType: 0x200);
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        r.Create(Pyreals, "Pyreal", container: Pack);

        Assert.True(r.Views.TryGet(Pack, out view));
        Assert.True(view.IsResolved);
        Assert.Equal([new ContainerViewEntry(Pyreals, 0, true)], view.Entries);
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.InSidePack, Pack, 0, 0), r.Inventory.Find(Pyreals));
        Assert.Equal([Pack], r.Harness.Of<ContainerContentsChanged>().Select(e => e.Container));

        // Named by the 0196, so the F745 adoption has nothing to add: no Added event, no tolerance note.
        Assert.Empty(r.Harness.Of<InventoryChanged>());
        Assert.DoesNotContain(r.Harness.Log.Entries, e => e.Message.Contains("no 0196", StringComparison.Ordinal));
    }

    [Fact]
    public void A_side_pack_item_no_0196_named_is_adopted_from_its_F745_and_the_tolerance_is_logged_once()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Pack, 1));
        r.View(Pack, (Pyreals, 0));
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        r.Create(Pyreals, "Pyreal", container: Pack);

        r.Create(Gem, "Gem", container: Pack);
        r.Create(0x80000103, "Other", container: Pack);

        Assert.Equal([Pyreals, Gem, 0x80000103], r.Items(Pack));
        Assert.Single(r.Harness.Log.Entries, e => e.Message.Contains("no 0196", StringComparison.Ordinal));
        Assert.False(r.Views.ContentsNamed(Sack));
    }

    [Fact]
    public void LoginComplete_waits_for_the_side_pack_contents_the_0196s_named()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Sack, 1), new ContentDesc(Pack, 1));
        r.View(Sack);
        r.View(Pack, (Pyreals, 0), (Gem, 0));
        r.Create(Sack, "Sack", container: Player, itemType: 0x200);
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        Assert.Equal(2, Missing(r));

        r.Create(Pyreals, "Pyreal", container: Pack);
        Assert.Equal(1, Missing(r));
        r.Create(Gem, "Gem", container: Pack);
        Assert.Equal(0, Missing(r));
    }

    [Fact]
    public void A_focus_among_the_side_packs_keeps_its_type_and_blocks_nothing()
    {
        // OpenAC #226: a carried focus does not make the pack incomplete.
        var r = new Rig();
        r.Describe(new ContentDesc(Pack, 1), new ContentDesc(Focus, 2));
        r.View(Pack, (Gem, 2), (Pyreals, 0));
        r.View(Focus);
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        r.Create(Focus, "Focus", container: Player);
        r.Create(Gem, "Gem Focus", container: Pack);
        r.Create(Pyreals, "Pyreal", container: Pack);

        Assert.Equal(0, Missing(r));
        Assert.Equal([1u, 2u], r.Inventory.SidePacks.Select(e => e.ContainerType));
        Assert.Equal([2u, 0u], r.Inventory.ItemsIn(Pack).Select(e => e.ContainerType));
        Assert.True(r.Views.TryGet(Focus, out var focus));
        Assert.True(focus.IsResolved);
        Assert.Empty(focus.Entries);
    }

    [Fact]
    public void C1_a_spawned_pack_its_0196_comes_before_its_0022_and_its_F745()
    {
        var r = new Rig();
        r.Describe();
        r.View(Pack);
        Assert.Empty(r.Inventory.SidePacks);
        Assert.Equal(0u, r.Views.ViewedGuid);

        r.Put(Pack, Player, 0, type: 1);
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);

        Assert.Equal([new InventoryEntry(Pack, 1)], r.Inventory.SidePacks);
        Assert.True(r.Views.ContentsNamed(Pack));
        Assert.True(r.Views.TryGet(Pack, out var view));
        Assert.True(view.IsResolved);
        Assert.Empty(view.Entries);
        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.Equal([InventoryChangeKind.Added], r.Harness.Of<InventoryChanged>().Select(e => e.Kind));

        // 02DA then 0022 into the pack (C1): the item lands at slot 0.
        r.Create(Pyreals, "Pyreal", container: Player);
        r.Put(Pyreals, Pack, 0);
        Assert.Equal([Pyreals], r.Items(Pack));
        Assert.Contains(new ContainerContentsChanged(Pack), r.Harness.Of<ContainerContentsChanged>());
    }

    [Fact]
    public void A_pack_whose_0196_waits_joins_from_its_own_F745_when_no_0022_came_first()
    {
        var r = new Rig();
        r.Describe();
        r.View(Pack, (Gem, 0));
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        Assert.Equal([Pack], r.Inventory.SidePacks.Select(e => e.Guid));
        Assert.Equal([Gem], r.Items(Pack));
    }

    [Fact]
    public void C2_the_corpse_opens_resolves_closes_reopens_and_closes_by_use()
    {
        var r = new Rig();
        r.Describe();
        r.Create(Corpse, "Corpse of Blue Phyntos Wasp");

        // 36425 the corpse's F745 (container none); 37116 the 0196 {4 entries}; 37129 UseDone; 37145 the four F745s.
        r.View(Corpse, (0x80000501, 0), (0x80000502, 0), (0x80000503, 0), (0x80000504, 0));
        Assert.Equal(Corpse, r.Views.ViewedGuid);
        Assert.Equal([new ContainerOpened(Corpse)], r.Harness.Of<ContainerOpened>());
        var view = r.Views.Viewed!;
        Assert.True(view.IsGround);
        Assert.Equal(4, view.Unresolved);
        Assert.Equal([0x80000501u, 0x80000502, 0x80000503, 0x80000504], view.Entries.Select(e => e.Guid));

        r.Harness.ClearEvents();
        for (var i = 1u; i <= 4; i++)
        {
            r.Create(0x80000500 + i, "Loot " + i, container: Corpse);
            Assert.Equal(4 - (int)i, view.Unresolved);
        }

        Assert.True(view.IsResolved);
        Assert.Equal(4, r.Harness.Of<ContainerContentsChanged>().Count(e => e.Container == Corpse));
        Assert.Empty(r.Harness.Of<InventoryChanged>());

        // The 0195 answer: a 0052 and no UseDone. The viewed container ends; the stale item objects stay.
        r.Close(Corpse);
        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.Equal([new ContainerClosed(Corpse)], r.Harness.Of<ContainerClosed>());
        Assert.True(r.Harness.State.Objects.TryGet(0x80000501, out _));

        // Use on the closed corpse re-opens: a 0196 replaces the entry list, the stale objects are resent.
        r.View(Corpse, (0x80000502, 0), (0x80000501, 0), (0x80000503, 0));
        var again = r.Views.Viewed!;
        Assert.Equal(3, again.Unresolved); // the stale objects do not count: the resent F745s do
        Assert.Equal([0x80000502u, 0x80000501, 0x80000503], again.Entries.Select(e => e.Guid));
        r.Create(0x80000502, "Loot 2", container: Corpse);
        r.Create(0x80000501, "Loot 1", container: Corpse);
        r.Create(0x80000503, "Loot 3", container: Corpse);
        Assert.True(again.IsResolved);
        Assert.Single(r.Harness.Of<ContainerOpened>()); // the re-open (events were cleared after the first)

        // Use on the open corpse closes: the 0052 (and a UseDone in the same tick, which is not this reducer's).
        r.Close(Corpse);
        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.Equal(2, r.Harness.Of<ContainerClosed>().Count);
    }

    [Fact]
    public void A_second_open_closes_the_first_and_a_stray_0052_changes_nothing()
    {
        var r = new Rig();
        r.Describe();
        r.Create(Corpse, "Corpse");
        r.Create(ChestGuid, "Chest");
        r.View(Corpse, (Gem, 0));
        r.View(ChestGuid);

        Assert.Equal(ChestGuid, r.Views.ViewedGuid);
        Assert.Equal(["ContainerOpened", "ContainerClosed", "ContainerOpened"], r.Harness.Names().Where(n => n.StartsWith("Container", StringComparison.Ordinal)));
        Assert.Equal([Corpse], r.Harness.Of<ContainerClosed>().Select(e => e.Container));

        r.Close(Corpse);
        Assert.Equal(ChestGuid, r.Views.ViewedGuid);
        Assert.Single(r.Harness.Of<ContainerClosed>());
    }

    [Fact]
    public void C3_an_empty_chest_opens_resolved_and_closes_by_its_0052()
    {
        var r = new Rig();
        r.Describe();
        r.Create(ChestGuid, "Chest");
        r.View(ChestGuid);
        Assert.True(r.Views.TryGet(ChestGuid, out var view));
        Assert.True(view.IsGround);
        Assert.True(view.IsResolved);
        Assert.Empty(view.Entries);
        r.Close(ChestGuid);
        Assert.False(r.Views.TryGet(ChestGuid, out _));
        Assert.Equal([new ContainerOpened(ChestGuid), new ContainerClosed(ChestGuid)], r.Harness.Events.Where(e => e is ContainerOpened or ContainerClosed).Cast<IGameEvent>().ToList());
    }

    [Fact]
    public void A_corpse_whose_F745_comes_after_its_0196_is_decided_by_the_F745()
    {
        var r = new Rig();
        r.Describe();
        r.View(Corpse, (Gem, 0));
        Assert.Equal(0u, r.Views.ViewedGuid);
        r.Create(Corpse, "Corpse");
        Assert.Equal(Corpse, r.Views.ViewedGuid);
        Assert.Equal(1, r.Views.Viewed!.Unresolved);
    }

    [Fact]
    public void A_pick_up_from_the_open_corpse_leaves_its_view_and_a_put_into_a_chest_joins_it()
    {
        var r = new Rig();
        r.Describe();
        r.Create(Corpse, "Corpse");
        r.View(Corpse, (Gem, 0), (Pyreals, 0));
        r.Create(Gem, "Gem", container: Corpse);
        r.Create(Pyreals, "Pyreal", container: Corpse);

        // 0x0019 item -> player: 02DA {Container = player}, then 0022 {item, player, 0}.
        r.Put(Gem, Player, 0);
        Assert.Equal([Pyreals], r.Views.Viewed!.Entries.Select(e => e.Guid));
        Assert.Equal(InventoryPlaceKind.MainPack, r.Inventory.Find(Gem).Kind);
        Assert.Equal(Corpse, r.Views.ViewedGuid);

        // A put into the viewed container at slot 0: the other entries shift without a message.
        r.Put(Gem, Corpse, 0);
        Assert.Equal([Gem, Pyreals], r.Views.Viewed!.Entries.Select(e => e.Guid));
        Assert.False(r.Inventory.Contains(Gem));
        Assert.True(r.Views.Viewed.IsResolved);
    }

    [Fact]
    public void An_F747_of_the_viewed_container_closes_it_and_of_an_entry_removes_the_entry()
    {
        var r = new Rig();
        r.Describe();
        r.Create(Corpse, "Corpse");
        r.View(Corpse, (Gem, 0), (Pyreals, 0));
        r.Create(Gem, "Gem", container: Corpse);
        r.Create(Pyreals, "Pyreal", container: Corpse);

        r.Delete(Gem);
        Assert.Equal([Pyreals], r.Views.Viewed!.Entries.Select(e => e.Guid));
        r.Delete(Corpse);
        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.Equal([new ContainerClosed(Corpse)], r.Harness.Of<ContainerClosed>());
    }

    [Fact]
    public void The_insert_and_shift_placement_moves_later_items_up_by_one()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Pack, 1));
        r.View(Pack, (Pyreals, 0), (Gem, 0));
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        r.Create(Pyreals, "Pyreal", container: Pack);
        r.Create(Gem, "Gem", container: Pack);
        r.Create(Mace, "Mace", container: Player);

        r.Put(Mace, Pack, 0);

        Assert.Equal([Mace, Pyreals, Gem], r.Items(Pack));
        Assert.Equal(1, r.Inventory.Find(Pyreals).Placement);
        Assert.Equal(2, r.Inventory.Find(Gem).Placement);
        Assert.Equal([Mace, Pyreals, Gem], r.Views.TryGet(Pack, out var view) ? view.Entries.Select(e => e.Guid).ToList() : []);
    }

    [Fact]
    public void C4_the_split_to_a_container_its_0022_names_a_guid_whose_F745_follows()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Arrows, 0));
        r.Create(Arrows, "Arrow", container: Player);

        r.Put(NewArrows, Player, 0); // GE 0022 {new guid, player, 0, 0} first
        r.Send(AceM3Writers.SetStackSize(1, Arrows, 40, 0)); // then 0197 {old stack, 40}
        r.Create(NewArrows, "Arrow", container: Player); // then the new stack's F745

        Assert.Equal([NewArrows, Arrows], r.Inventory.Pack.Select(e => e.Guid));
        Assert.Equal(2, r.Inventory.Pack.Count);
        Assert.Equal([InventoryChangeKind.Added, InventoryChangeKind.StackChanged], r.Harness.Of<InventoryChanged>().Select(e => e.Kind));
    }

    [Fact]
    public void C4_the_drop_019A_then_the_dropper_gets_an_F745_with_no_container()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Arrows, 0));
        var item = r.Create(Arrows, "Arrow", container: Player);

        r.Send(AceM3Writers.PutObjectIn3D(Player, r.Next(), Arrows));
        Assert.False(r.Inventory.Contains(Arrows));
        r.Create(Arrows, "Arrow"); // container none

        Assert.False(r.Inventory.Contains(Arrows));
        Assert.Null(item.ContainerId);
        Assert.Equal([InventoryChangeKind.Dropped], r.Harness.Of<InventoryChanged>().Select(e => e.Kind));
    }

    [Fact]
    public void C4_a_give_the_0024_then_a_0022_naming_the_NPC_takes_the_item_out_once()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Arrows, 0));
        var item = r.Create(Arrows, "Arrow", container: Player);
        r.Create(Alfrin, "Alfrin");
        r.Harness.ClearEvents();

        r.Send(AceM3Writers.InventoryRemoveObject(Arrows)); // GM 0024 lands first
        r.Put(Arrows, Alfrin, 0); // then GE 0022 {item, Alfrin, 0, 0}

        Assert.False(r.Inventory.Contains(Arrows));
        Assert.Equal(Alfrin, item.ContainerId);
        Assert.Equal([InventoryChangeKind.Removed], r.Harness.Of<InventoryChanged>().Select(e => e.Kind));
        Assert.Equal(0u, r.Views.ViewedGuid);
    }

    [Fact]
    public void A_pack_that_leaves_the_inventory_forgets_its_contents_view()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Pack, 1));
        r.View(Pack, (Gem, 0));
        r.Create(Pack, "Pack", container: Player, itemType: 0x200);
        r.Create(Gem, "Gem", container: Pack);

        r.Send(AceM3Writers.PutObjectIn3D(Player, r.Next(), Pack));

        Assert.False(r.Views.TryGet(Pack, out _));
        Assert.False(r.Views.ContentsNamed(Pack));
        Assert.Empty(r.Inventory.SidePacks);
    }

    [Fact]
    public void Leaving_the_world_closes_the_viewed_container_and_clears_the_views()
    {
        var r = new Rig();
        r.Describe(new ContentDesc(Pack, 1));
        r.View(Pack);
        r.Create(Corpse, "Corpse");
        r.View(Corpse);

        r.Harness.Reducer.OnCharacterLogOff(r.Harness.Context);

        Assert.Equal(0u, r.Views.ViewedGuid);
        Assert.False(r.Views.ContentsNamed(Pack));
        Assert.Equal([new ContainerClosed(Corpse)], r.Harness.Of<ContainerClosed>());
    }

    [Fact]
    public void Value_and_ItemType_are_the_F745_weenie_fields_so_nothing_new_is_exposed_for_the_loot_rule()
    {
        var r = new Rig();
        r.Describe();
        var item = r.Create(Gem, "Gem", container: Corpse, itemType: 0x8, value: 4500);

        Assert.Equal(0x8u, item.ItemType);
        Assert.Equal(4500u, item.Weenie.Value);
    }
}
