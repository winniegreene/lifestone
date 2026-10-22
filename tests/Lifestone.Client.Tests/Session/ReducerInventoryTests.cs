using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 s2: the player's <see cref="PlayerInventory"/> from the description's lists and from the eight inventory, wield and public-quality messages, in the order and with the values ACE sends
/// (Player_Inventory.cs). Every message goes through the real decoder (<see cref="InboundDispatcher"/>) so the codec and the reducer are exercised together.
/// </summary>
public sealed class ReducerInventoryTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Pack1 = 0x80000010;
    private const uint Sword = 0x80000101;
    private const uint Staff = 0x80000102;
    private const uint Comp = 0x80000103;
    private const uint Gem = 0x80000104;
    private const uint Robe = 0x80000105;
    private const uint MainHand = 0x00100000;
    private const uint Chest = 0x80000999;

    private sealed class Rig
    {
        public Rig()
        {
            Harness = new ReducerHarness();
            Harness.EnterWorld();
            Harness.Bus.Subscribe((in InventoryChanged e) => Changes.Add(e));
            Harness.Bus.Subscribe((in InventoryFailed e) => Failures.Add(e));
        }

        public ReducerHarness Harness { get; }

        public List<InventoryChanged> Changes { get; } = [];

        public List<InventoryFailed> Failures { get; } = [];

        public PlayerInventory Inventory => Harness.State.Self.Inventory;

        private uint _event;

        public void Send(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Harness.Context, message, Harness.Reducer).Outcome);

        public void Describe(ContentDesc[]? pack = null, EquippedDesc[]? equipped = null, int? burden = null, bool undecoded = false)
        {
            var ints = ImmutableDictionary<uint, int>.Empty;
            if (burden is { } b)
            {
                ints = ints.Add(PlayerInventory.EncumbranceProperty, b);
            }

            Harness.Reducer.OnPlayerDescription(
                Harness.Context,
                new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
                new PlayerDescriptionMsg
                {
                    IntProperties = ints,
                    Inventory = [.. pack ?? []],
                    Equipped = [.. equipped ?? []],
                    InventoryUndecoded = undecoded,
                });
            Changes.Clear();
        }

        /// <summary>The item's own CreateObject: no position, with the weenie fields a pack item has.</summary>
        public WorldObject Item(uint guid, string name, uint? container = null, uint itemType = 0, ushort? stack = null, uint? wielder = null, uint? wielded = null)
        {
            var header = WeenieHeaderFlags.None;
            header |= container is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Container;
            header |= stack is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.StackSize;
            header |= wielder is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.Wielder;
            header |= wielded is null ? WeenieHeaderFlags.None : WeenieHeaderFlags.CurrentlyWieldedLocation;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
            var created = basis with
            {
                Weenie = basis.Weenie with { Header = header, ItemType = itemType, ContainerId = container, StackSize = stack, WielderId = wielder, CurrentlyWieldedLocation = wielded },
            };
            Harness.Reducer.OnCreateObject(Harness.Context, created, isUpdate: false);
            Assert.True(Harness.State.Objects.TryGet(guid, out var o));
            return o;
        }

        public uint Next() => ++_event;
    }

    // The messages as ACE writes them.

    private static byte[] Put(Rig r, uint item, uint container, uint placement, uint type = 0) => AceM3Writers.PutObjInContainer(Player, r.Next(), item, container, placement, type);

    private static byte[] Wield(Rig r, uint item, uint location) => AceM3Writers.WieldObject(Player, r.Next(), item, (int)location);

    [Fact]
    public void The_description_lists_fill_the_pack_the_side_packs_and_the_equipment_and_burden_is_read()
    {
        var r = new Rig();
        r.Describe(
            [new ContentDesc(Sword, 0), new ContentDesc(Pack1, 1), new ContentDesc(Comp, 0)],
            [new EquippedDesc(Robe, 0x200, 7), new EquippedDesc(Staff, MainHand, 0)],
            burden: 720);

        var inv = r.Inventory;
        Assert.True(inv.IsKnown);
        Assert.True(inv.ListsComplete);
        Assert.Equal(Player, inv.Owner);
        Assert.Equal([new InventoryEntry(Sword, 0), new InventoryEntry(Comp, 0)], inv.Pack);
        Assert.Equal([new InventoryEntry(Pack1, 1)], inv.SidePacks);
        Assert.Empty(inv.ItemsIn(Pack1));
        Assert.Equal([new EquippedItem(Robe, 0x200, 7), new EquippedItem(Staff, MainHand, 0)], inv.Equipped);
        Assert.Equal(5, inv.Count);
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, Player, 1, 0), inv.Find(Comp));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.SidePack, Player, 0, 0), inv.Find(Pack1));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.Equipped, 0, -1, MainHand), inv.Find(Staff));
        Assert.Equal(Staff, inv.EquippedAt(MainHand));
        Assert.Null(inv.EquippedAt(0x400));
        Assert.True(inv.TryGetEquipped(Robe, out var robe) && robe.Priority == 7);
        Assert.False(inv.Contains(Gem));
        Assert.Equal(720, inv.Burden);

        // A later description replaces the lists, and burden follows the player's own quality (never computed).
        r.Describe([new ContentDesc(Gem, 0)], burden: 5);
        Assert.Equal([new InventoryEntry(Gem, 0)], inv.Pack);
        Assert.Empty(inv.Equipped);
        Assert.Equal(5, inv.Burden);
        r.Harness.Reducer.OnPrivateUpdate(r.Harness.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, 1, PlayerInventory.EncumbranceProperty, 905, default, default, default));
        Assert.Equal(905, inv.Burden);
    }

    [Fact]
    public void A_description_announces_a_reset_and_one_without_the_lists_says_the_model_is_incomplete()
    {
        var r = new Rig();
        var seen = new List<InventoryChanged>();
        r.Harness.Bus.Subscribe((in InventoryChanged e) => seen.Add(e));

        r.Describe(undecoded: true);

        Assert.Equal(new InventoryChanged(InventoryChangeKind.Reset, 0), Assert.Single(seen));
        Assert.True(r.Inventory.IsKnown);
        Assert.False(r.Inventory.ListsComplete);
    }

    [Fact]
    public void A_wield_round_trip_in_the_order_ACE_sends_ends_equipped_and_then_back_in_the_pack()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Staff, 0), new ContentDesc(Sword, 0)]);
        var staff = r.Item(Staff, "Staff", container: Player);

        // Wield (Player_Inventory.cs 297-306): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, then 0023 with the location.
        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Staff, 3, 0));
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Staff, 10, 0));
        Assert.DoesNotContain(r.Changes, c => c.Kind is InventoryChangeKind.Wielded or InventoryChangeKind.Unwielded);
        Assert.Equal(InventoryPlaceKind.MainPack, r.Inventory.Find(Staff).Kind);
        r.Send(Wield(r, Staff, MainHand));

        Assert.Equal(new InventoryPlace(InventoryPlaceKind.Equipped, 0, -1, MainHand), r.Inventory.Find(Staff));
        Assert.Equal([new InventoryEntry(Sword, 0)], r.Inventory.Pack);
        Assert.Equal(Staff, r.Inventory.EquippedAt(MainHand));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Wielded, Staff, Location: MainHand, FromContainer: Player, FromPlacement: 0), r.Changes[^1]);
        Assert.Equal((Player, MainHand, null), (staff.WielderId, staff.Weenie.CurrentlyWieldedLocation, staff.ContainerId));

        // Unwield (1359 and 400-405): 02DA Wielder 0, 02CE 0, F74A, then 02DA Container and 0022 into the pack at slot 0.
        r.Changes.Clear();
        r.Send(AceM3Writers.PublicUpdateInstanceId(2, Staff, 3, 0));
        r.Send(AceM3Writers.PublicUpdatePropertyInt(2, Staff, 10, 0));
        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Staff, 2, Player));
        r.Send(Put(r, Staff, Player, 0));

        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, Player, 0, 0), r.Inventory.Find(Staff));
        Assert.Equal([new InventoryEntry(Staff, 0), new InventoryEntry(Sword, 0)], r.Inventory.Pack);
        Assert.Empty(r.Inventory.Equipped);
        var unwielded = Assert.Single(r.Changes, c => c.Kind == InventoryChangeKind.Unwielded);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Unwielded, Staff, Player, 0, 0, 0, -1, MainHand), unwielded);
        Assert.Equal((Player, null, null), (staff.ContainerId, staff.WielderId, staff.Weenie.CurrentlyWieldedLocation));
        Assert.Equal(0, r.Harness.Reducer.Counters.DecodedNotApplied);
    }

    [Fact]
    public void When_the_wield_messages_agree_the_item_is_wielded_and_its_world_object_says_so()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)]);
        var sword = r.Item(Sword, "Sword", container: Player);

        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Sword, 3, Player));
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Sword, 10, (int)MainHand));
        Assert.Equal((Player, MainHand), (sword.WielderId, sword.Weenie.CurrentlyWieldedLocation));
        Assert.True(r.Inventory.TryGetInt(Sword, 10, out var location));
        Assert.Equal((int)MainHand, location);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.PropertyChanged, Sword, Property: 10, PropertyValue: (int)MainHand), r.Changes[^1]);
        r.Send(Wield(r, Sword, MainHand));

        Assert.Equal(Sword, r.Inventory.EquippedAt(MainHand));
        Assert.Equal((Player, MainHand, null), (sword.WielderId, sword.Weenie.CurrentlyWieldedLocation, sword.ContainerId));
    }

    [Fact]
    public void Wielding_from_another_equipped_slot_moves_the_entry_and_keeps_its_priority()
    {
        var r = new Rig();
        r.Describe(equipped: [new EquippedDesc(Sword, MainHand, 3)]);

        r.Send(Wield(r, Sword, 0x200000));

        var item = Assert.Single(r.Inventory.Equipped);
        Assert.Equal(new EquippedItem(Sword, 0x200000, 3), item);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Wielded, Sword, Location: 0x200000, FromLocation: MainHand), r.Changes[^1]);
    }

    [Fact]
    public void An_item_put_in_a_container_is_added_or_moved_at_its_slot_and_the_slots_shift()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0), new ContentDesc(Pack1, 1)]);
        r.Item(Comp, "Component");

        r.Send(Put(r, Comp, Player, 0));
        Assert.Equal([Comp, Sword], r.Inventory.Pack.Select(e => e.Guid));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Added, Comp, Player, 0), r.Changes[^1]);

        r.Send(Put(r, Sword, Pack1, 0));
        Assert.Equal([new InventoryEntry(Sword, 0)], r.Inventory.ItemsIn(Pack1));
        Assert.Equal([Comp], r.Inventory.Pack.Select(e => e.Guid));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.InSidePack, Pack1, 0, 0), r.Inventory.Find(Sword));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Moved, Sword, Pack1, 0, 0, Player, 1), r.Changes[^1]);

        // A new side pack arrives by its own 0022 with container type 1.
        r.Send(Put(r, Gem, Player, 1, type: 1));
        Assert.Equal([Pack1, Gem], r.Inventory.SidePacks.Select(e => e.Guid));
        Assert.Equal(1u, r.Inventory.SidePacks[1].ContainerType);
        Assert.Empty(r.Inventory.ItemsIn(Gem));

        // A placement past the end appends.
        r.Send(Put(r, Robe, Player, 40));
        Assert.Equal(Robe, r.Inventory.Pack[^1].Guid);
        Assert.Equal(3, r.Inventory.Count - r.Inventory.SidePacks.Count);
    }

    [Fact]
    public void A_pack_known_only_by_its_own_create_object_takes_items_and_a_chest_takes_them_out_of_the_players_hands()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)]);
        r.Item(Pack1, "Pack", container: Player, itemType: 0x200);
        var sword = r.Item(Sword, "Sword", container: Player);

        r.Send(Put(r, Sword, Pack1, 0));
        Assert.Equal(Pack1, sword.ContainerId);
        Assert.Equal([Pack1], r.Inventory.SidePacks.Select(e => e.Guid));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.InSidePack, Pack1, 0, 0), r.Inventory.Find(Sword));

        r.Changes.Clear();
        r.Send(Put(r, Sword, Chest, 0));
        Assert.False(r.Inventory.Contains(Sword));
        Assert.Equal(Chest, sword.ContainerId);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Removed, Sword, Chest, -1, 0, Pack1, 0), Assert.Single(r.Changes));

        // An item the player never held, put in a container that is not the player's: nothing to apply.
        r.Changes.Clear();
        r.Send(Put(r, Gem, Chest, 0));
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void A_dropped_item_leaves_the_pack_and_the_equipped_list()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)], [new EquippedDesc(Staff, MainHand, 0)]);
        var sword = r.Item(Sword, "Sword", container: Player);

        // Drop (1441): 02DA Container 0, 019A.
        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Sword, 2, 0));
        r.Send(AceM3Writers.PutObjectIn3D(Player, r.Next(), Sword));
        Assert.False(r.Inventory.Contains(Sword));
        Assert.Null(sword.ContainerId);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Dropped, Sword, FromContainer: Player, FromPlacement: 0), r.Changes[^1]);

        r.Send(AceM3Writers.PutObjectIn3D(Player, r.Next(), Staff));
        Assert.Empty(r.Inventory.Equipped);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Dropped, Staff, FromLocation: MainHand), r.Changes[^1]);

        // Not held: no event, nothing changes.
        r.Changes.Clear();
        r.Send(AceM3Writers.PutObjectIn3D(Player, r.Next(), Gem));
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void A_component_burn_shrinks_the_stack_by_0197_and_removes_it_by_0024()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Comp, 0), new ContentDesc(Sword, 0)]);
        var comp = r.Item(Comp, "Prismatic Taper", container: Player, stack: 5);

        // Player_Magic.cs 1221 and Player_Inventory.cs 116-140: one burnt, SetStackSize with the new size and value.
        r.Send(AceM3Writers.SetStackSize(2, Comp, 4, 80));
        Assert.Equal((ushort)4, comp.Weenie.StackSize);
        Assert.Equal(80u, comp.Weenie.Value);
        Assert.True(comp.Weenie.Header.HasFlag(WeenieHeaderFlags.Value));
        Assert.True(r.Inventory.TryGetStack(Comp, out var size, out var value));
        Assert.Equal((4u, 80u), (size, value));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.StackChanged, Comp, StackSize: 4, Value: 80), r.Changes[^1]);

        // An older sequence is dropped; a newer one is taken.
        r.Send(AceM3Writers.SetStackSize(1, Comp, 99, 99));
        Assert.Equal((ushort)4, comp.Weenie.StackSize);
        Assert.Equal(DropReason.Stale, Assert.Single(r.Harness.Of<MessageDropped>()).Reason);
        r.Send(AceM3Writers.SetStackSize(3, Comp, 1, 20));
        Assert.Equal((ushort)1, comp.Weenie.StackSize);

        // The last one burnt: 0024, and the pack closes up.
        r.Changes.Clear();
        r.Send(AceM3Writers.InventoryRemoveObject(Comp));
        Assert.False(r.Inventory.Contains(Comp));
        Assert.False(r.Inventory.TryGetStack(Comp, out _, out _));
        Assert.Equal([Sword], r.Inventory.Pack.Select(e => e.Guid));
        Assert.Null(comp.ContainerId);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Removed, Comp, FromContainer: Player, FromPlacement: 0), Assert.Single(r.Changes));

        // A 0024 or 0197 for something the player does not hold is no change and no event.
        r.Changes.Clear();
        r.Send(AceM3Writers.InventoryRemoveObject(Gem));
        r.Send(AceM3Writers.SetStackSize(1, Gem, 3, 3));
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void A_stack_size_for_an_item_with_no_world_object_is_kept_on_the_model()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Comp, 0)]);

        r.Send(AceM3Writers.SetStackSize(1, Comp, 12, 60));

        Assert.True(r.Inventory.TryGetStack(Comp, out var size, out var value));
        Assert.Equal((12u, 60u), (size, value));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.StackChanged, Comp, StackSize: 12, Value: 60), r.Changes[^1]);
    }

    [Fact]
    public void A_public_int_for_a_held_item_updates_the_world_object_and_the_model_and_for_the_player_its_qualities()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)]);
        var sword = r.Item(Sword, "Sword", container: Player);
        var ground = r.Item(Gem, "Gem");
        var qualityEvents = new List<QualitiesChanged>();
        r.Harness.Bus.Subscribe((in QualitiesChanged e) => qualityEvents.Add(e));

        r.Send(AceM3Writers.PublicUpdatePropertyInt(3, Sword, 19, 450));
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Sword, 92, 60));
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Sword, 107, -3));
        Assert.Equal(450u, sword.Weenie.Value);
        Assert.Equal((ushort)60, sword.Weenie.Structure);
        Assert.True(r.Inventory.TryGetInt(Sword, 107, out var other));
        Assert.Equal(-3, other);
        Assert.Equal(new InventoryChanged(InventoryChangeKind.PropertyChanged, Sword, Property: 107, PropertyValue: -3), r.Changes[^1]);

        // A stale sequence for the same quality is dropped (retail's per-object stamp).
        r.Send(AceM3Writers.PublicUpdatePropertyInt(2, Sword, 19, 1));
        Assert.Equal(450u, sword.Weenie.Value);
        Assert.Single(r.Harness.Of<MessageDropped>());

        // An object the player does not hold keeps the mirror and raises no inventory event.
        r.Changes.Clear();
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Gem, 19, 77));
        Assert.Equal(77u, ground.Weenie.Value);
        Assert.Empty(r.Changes);
        Assert.False(r.Inventory.TryGetInt(Gem, 19, out _));

        // The player's own object: its qualities.
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Player, 40, 8));
        Assert.Equal(8, r.Harness.State.Self.Qualities.Ints[40]);
        Assert.Equal(new QualitiesChanged(QualityChange.Int, 40), Assert.Single(qualityEvents));

        // CurrentWieldedLocation 0 clears the field.
        r.Send(AceM3Writers.PublicUpdatePropertyInt(1, Sword, 10, (int)MainHand));
        Assert.Equal(MainHand, sword.Weenie.CurrentlyWieldedLocation);
        r.Send(AceM3Writers.PublicUpdatePropertyInt(2, Sword, 10, 0));
        Assert.Null(sword.Weenie.CurrentlyWieldedLocation);
        Assert.False(sword.Weenie.Header.HasFlag(WeenieHeaderFlags.CurrentlyWieldedLocation));
    }

    [Fact]
    public void A_public_instance_id_sets_the_container_and_wielder_of_the_world_object_and_other_properties_are_logged_once()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)]);
        var sword = r.Item(Sword, "Sword");

        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Sword, 2, Player));
        Assert.Equal(Player, sword.ContainerId);
        Assert.True(sword.Weenie.Header.HasFlag(WeenieHeaderFlags.Container));
        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Sword, 3, Player));
        Assert.Equal(Player, sword.WielderId);
        r.Send(AceM3Writers.PublicUpdateInstanceId(2, Sword, 3, 0));
        Assert.Null(sword.WielderId);
        Assert.False(sword.Weenie.Header.HasFlag(WeenieHeaderFlags.Wielder));

        // The model's membership is unchanged by 02DA (0022, 0023, 019A and 0024 move items).
        Assert.Equal(InventoryPlaceKind.MainPack, r.Inventory.Find(Sword).Kind);
        Assert.Empty(r.Changes);

        var before = r.Harness.Log.Entries.Count;
        r.Send(AceM3Writers.PublicUpdateInstanceId(1, Sword, 38, 0x80000777));
        r.Send(AceM3Writers.PublicUpdateInstanceId(2, Sword, 38, 0x80000778));
        var logged = r.Harness.Log.Entries.Skip(before).Where(e => e.Message.Contains("instance property the reducer does not apply", StringComparison.Ordinal)).ToList();
        Assert.Single(logged);
        Assert.Equal(LogSeverity.Info, logged[0].Severity);
    }

    [Fact]
    public void A_failure_publishes_the_item_and_the_error_and_changes_nothing()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)], [new EquippedDesc(Staff, MainHand, 0)]);

        r.Send(AceM3Writers.InventoryServerSaysFailed(Player, r.Next(), Sword, 0x36));
        r.Send(AceM3Writers.InventoryServerSaysFailed(Player, r.Next(), Staff, 0));

        Assert.Equal([new InventoryFailed(Sword, 0x36), new InventoryFailed(Staff, 0)], r.Failures);
        Assert.Empty(r.Changes);
        Assert.Equal([new InventoryEntry(Sword, 0)], r.Inventory.Pack);
        Assert.Equal(Staff, r.Inventory.EquippedAt(MainHand));
    }

    [Fact]
    public void None_of_the_eight_codes_is_counted_as_not_applied_or_unknown()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)]);
        r.Item(Sword, "Sword", container: Player);
        byte[][] bodies =
        [
            AceM3Writers.InventoryRemoveObject(Gem),
            AceM3Writers.SetStackSize(2, Sword, 5, 70),
            AceM3Writers.PublicUpdatePropertyInt(3, Sword, 0x0A, 9),
            AceM3Writers.PublicUpdateInstanceId(4, Sword, 3, Player),
            AceM3Writers.PutObjInContainer(Player, 11, Sword, Player, 1, 0),
            AceM3Writers.WieldObject(Player, 12, Sword, 0x100000),
            AceM3Writers.PutObjectIn3D(Player, 13, Sword),
            AceM3Writers.InventoryServerSaysFailed(Player, 14, Sword, 0x36),
        ];

        foreach (var body in bodies)
        {
            r.Send(body);
        }

        Assert.Equal(0, r.Harness.Reducer.Counters.DecodedNotApplied);
        Assert.Empty(r.Harness.Of<UnknownMessage>());
    }

    [Fact]
    public void Leaving_the_world_forgets_the_inventory()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)], [new EquippedDesc(Staff, MainHand, 0)]);
        r.Send(AceM3Writers.SetStackSize(1, Sword, 2, 2));

        r.Harness.Reducer.Reset();

        var inv = r.Inventory;
        Assert.False(inv.IsKnown);
        Assert.Equal(0u, inv.Owner);
        Assert.Equal(0, inv.Count);
        Assert.Empty(inv.Pack);
        Assert.Empty(inv.Equipped);
        Assert.False(inv.TryGetStack(Sword, out _, out _));
    }

    [Fact]
    public void A_held_object_created_after_the_description_is_adopted_into_its_container_or_slot()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Pack1, 1)]);
        var pack = r.Item(Pack1, "Pack", container: Player, itemType: 0x200);
        var inside = r.Item(Gem, "Gem", container: Pack1);
        var worn = r.Item(Robe, "Robe", wielder: Player, wielded: 0x200);
        var stray = r.Item(Comp, "Component");

        r.Harness.Reducer.AdoptHeldObject(inside);
        r.Harness.Reducer.AdoptHeldObject(worn);
        r.Harness.Reducer.AdoptHeldObject(stray);
        r.Harness.Reducer.AdoptHeldObject(pack);

        Assert.Equal([new InventoryEntry(Gem, 0)], r.Inventory.ItemsIn(Pack1));
        Assert.Equal(Robe, r.Inventory.EquippedAt(0x200));
        Assert.False(r.Inventory.Contains(Comp));
        Assert.Equal([new InventoryEntry(Pack1, 1)], r.Inventory.SidePacks);
        Assert.Equal([InventoryChangeKind.Added, InventoryChangeKind.Wielded], r.Changes.Select(c => c.Kind));
    }

    /// <summary>The player becomes Present (LoginComplete sent, Hidden cleared), as a live grant finds it.</summary>
    private static void BecomePresent(Rig r)
    {
        r.Harness.Reducer.ApplyLocal(new Lifestone.Client.Session.Recording.RecordingNotice(SessionNotices.LoginCompleteSent, 0, null));
        r.Harness.Reducer.OnSetState(r.Harness.Context, new SetStateMsg(Player, PhysicsState.Gravity | PhysicsState.EdgeSlide, 1, 9));
        Assert.Equal(SelfTravel.Present, r.Harness.State.Self.Travel);
        r.Changes.Clear();
    }

    // M3 1.2 fix R3.
    [Fact]
    public void A_live_grant_into_a_side_pack_is_published_once_as_Added_at_the_slot_of_its_own_0022()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Pack1, 1)]);
        r.Item(Pack1, "Pack", container: Player, itemType: 0x200);
        r.Item(Sword, "Sword", container: Pack1); // a login-time item of the side pack: slot 0
        BecomePresent(r);

        // ACE's TryCreateInInventoryWithNetworking into a side pack: the item's CreateObject (its ContainerId the side pack), then its own 0022.
        r.Item(Gem, "Gem", container: Pack1);
        Assert.Empty(r.Changes); // nothing yet: the 0022 follows
        Assert.False(r.Inventory.Contains(Gem));

        r.Send(Put(r, Gem, Pack1, 1));

        var change = Assert.Single(r.Changes);
        Assert.Equal((InventoryChangeKind.Added, Gem, Pack1, 1), (change.Kind, change.Guid, change.Container, change.Placement));
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.InSidePack, Pack1, 1, 0), r.Inventory.Find(Gem));
    }

    [Fact]
    public void During_the_arrival_a_side_pack_item_with_no_0022_is_still_adopted_once_and_a_wielded_one_is_reached()
    {
        var r = new Rig();
        r.Describe([new ContentDesc(Pack1, 1)]);
        r.Item(Pack1, "Pack", container: Player, itemType: 0x200);
        r.Changes.Clear();
        Assert.NotEqual(SelfTravel.Present, r.Harness.State.Self.Travel);

        r.Item(Gem, "Gem", container: Pack1);
        r.Item(Robe, "Robe", wielder: Player, wielded: 0x200); // no container: the wielded arm of the adoption

        Assert.Equal([InventoryChangeKind.Added, InventoryChangeKind.Wielded], r.Changes.Select(c => c.Kind));
        Assert.Equal([new InventoryEntry(Gem, 0)], r.Inventory.ItemsIn(Pack1));
        Assert.Equal(Robe, r.Inventory.EquippedAt(0x200));
    }
    // M3 1.2 live-gate fix: a Pyreal grant exactly as ACE puts it on the wire, through the real decoder.
    [Fact]
    public void A_live_Pyreal_grant_reaches_the_pack_with_the_0022_and_its_world_object_follows_with_the_CreateObject_ACE_sends_in_a_later_bundle()
    {
        const uint Pyreals = 0x8000038D;
        var r = new Rig();
        r.Describe([new ContentDesc(Sword, 0)], burden: 665);
        BecomePresent(r);

        // TryCreateInInventoryWithNetworking (Player_Inventory.cs:90-115) enqueues the item's CreateObject (SmartboxQueue) and then the 0022 and the 02CD EncumbranceVal (UIQueue); the mod adds its
        // reply line (UIQueue). NetworkSession sends the groups in enum order, UIQueue (9) before SmartboxQueue (10), and waits 5 ms between bundles: the 0022 and the 02CD arrive first.
        r.Send(Put(r, Pyreals, Player, 0));
        r.Send(AceWireWriters.PrivateUpdatePropertyInt(1, PlayerInventory.EncumbranceProperty, 665));

        // What the live loopback test saw: the model holds the item, the world object does not exist yet (the CreateObject is still in ACE's next bundle).
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, Player, 0, 0), r.Inventory.Find(Pyreals));
        Assert.False(r.Harness.State.Objects.TryGet(Pyreals, out _));
        Assert.Equal(665, r.Inventory.Burden);
        var added = Assert.Single(r.Changes, c => c.Kind == InventoryChangeKind.Added);
        Assert.Equal((Pyreals, Player, 0), (added.Guid, added.Container, added.Placement));

        // The item's CreateObject as ACE writes it (WeenieDesc Container = the player's guid, no position, Placement Resting).
        r.Send(AceWireWriters.ObjectCreate(new AceCreateObject
        {
            Guid = Pyreals,
            PhysicsState = (uint)(PhysicsState.IgnoreCollisions | PhysicsState.Gravity),
            AnimationFrame = 1,
            Setup = 0x02000179,
            Sequences = [1, 1, 1, 1, 1, 1, 1, 1, 1],
            Name = "Pyreal",
            WeenieClassId = 273,
            Icon = 0x06001355,
            ItemType = 0x40,
            ObjectDescriptionFlags = 0x10,
            PluralName = "Pyreals",
            Value = 7,
            StackSize = 7,
            MaxStackSize = 25000,
            Container = Player,
        }));

        Assert.True(r.Harness.State.Objects.TryGet(Pyreals, out var item), "the world object of the grant is missing after its CreateObject");
        Assert.Equal((uint?)Player, item.ContainerId);
        Assert.Equal((ushort?)7, item.Weenie.StackSize);
        Assert.False(item.InWorld);
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, Player, 0, 0), r.Inventory.Find(Pyreals));
        Assert.Single(r.Changes, c => c.Kind == InventoryChangeKind.Added); // the late CreateObject adds nothing to the model
        Assert.Equal(0, r.Harness.Reducer.Counters.DecodedNotApplied);
    }
}
