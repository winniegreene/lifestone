using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 s2: the stand-in's pack wire. An <c>item</c> the stand-in grants arrives as ACE puts it on the wire (GameEvent 0x0022 into the main pack, the burden update, the reply line, and then the item's CreateObject), so the
/// client's <see cref="PlayerInventory"/> holds it, with its world object carrying the name, class and stack (closes <c>TODO(m3-standin-pack-wire)</c>).
/// </summary>
public sealed class StandInInventoryTests
{
    private static StandInVisit Visit() => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Items = new Dictionary<uint, StandInItemKind>
        {
            [273] = new("Pyreal", 25_000),
            [100] = new("Sword", null),
            [200] = new("Portal Stone", null, Creatable: false),
        },
    };

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig()
        {
            var v = Visit();
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, v);
                    StandInSessions.AnswerWorld(h, v);
                },
                _time,
                new RecordingLogSink());
            Session.Events.Subscribe((in InventoryChanged e) =>
            {
                if (e.Kind != InventoryChangeKind.Reset)
                {
                    Changes.Add(e);
                }
            });
            Session.Events.Subscribe((in ObjectCreated e) => Order.Add("create " + e.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
            Session.Events.Subscribe((in InventoryChanged e) => Order.Add("inventory " + e.Kind));
            Session.Events.Subscribe((in ToolReplyReceived e) => Order.Add("reply " + e.Verb));
            Run(2.0);
        }

        public GameSession Session { get; }

        public List<InventoryChanged> Changes { get; } = [];

        public List<string> Order { get; } = [];

        public PlayerInventory Inventory => Session.State.Self.Inventory;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public ToolHandle<ItemResult> Spawn(uint wcid, int? stack)
        {
            var handle = Session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
            Run(1.5);
            Assert.True(handle.IsCompleted, handle.Summary);
            return handle;
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_granted_item_arrives_in_the_main_pack_and_the_reply_comes_before_its_world_object_as_ACE_orders_them()
    {
        using var rig = new Rig();
        Assert.True(rig.Inventory.IsKnown);
        Assert.Empty(rig.Inventory.Pack);

        var five = rig.Spawn(273, 5);

        var guid = five.Result!.Guid;
        var entry = Assert.Single(rig.Inventory.Pack);
        Assert.Equal(new InventoryEntry(guid, 0), entry);
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, rig.Session.State.Self.Guid, 0, 0), rig.Inventory.Find(guid));
        Assert.Equal(new InventoryChanged(InventoryChangeKind.Added, guid, rig.Session.State.Self.Guid, 0), Assert.Single(rig.Changes));
        Assert.True(rig.Session.State.Objects.TryGet(guid, out var item));
        Assert.Equal(("Pyreal", 273u, rig.Session.State.Self.Guid, (ushort)5), (item.Name, item.Weenie.WeenieClassId, item.ContainerId, item.Weenie.StackSize));
        Assert.False(item.InWorld);
        // ACE's order: the 0022 and the reply (UIQueue) come before the item's CreateObject (SmartboxQueue, a later bundle).
        Assert.Equal(["inventory Added", "reply item", "create " + guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)], rig.Order.Skip(rig.Order.Count - 3));
    }

    [Fact]
    public void Each_grant_takes_the_next_slot_a_refusal_adds_nothing_and_a_clamped_stack_is_the_world_objects()
    {
        using var rig = new Rig();
        var first = rig.Spawn(273, 60_000);
        var sword = rig.Spawn(100, null);
        var none = rig.Spawn(99_999_999, 1);
        var stone = rig.Spawn(200, 1);

        Assert.Null(none.Result);
        Assert.Null(stone.Result);
        Assert.Equal([first.Result!.Guid, sword.Result!.Guid], rig.Inventory.Pack.Select(e => e.Guid));
        Assert.Equal([0, 1], rig.Changes.Select(c => c.Placement));
        Assert.True(rig.Session.State.Objects.TryGet(first.Result.Guid, out var coins));
        Assert.Equal((ushort)25_000, coins.Weenie.StackSize);
        Assert.Equal(25_000u, coins.Weenie.Value);
        Assert.True(rig.Session.State.Objects.TryGet(sword.Result.Guid, out var blade));
        Assert.Null(blade.Weenie.StackSize);
    }

    [Fact]
    public void A_kit_weenie_arrives_in_ACEs_order_with_its_real_stack_burden_item_type_and_locations()
    {
        using var rig = new Rig();
        Assert.Null(rig.Inventory.Burden);

        // Lead Scarab (691): 40 of a stack of up to 100; the stand-in's burden for a component is 4, the SELECT's.
        var lead = rig.Spawn(691, 40);
        var leadGuid = lead.Result!.Guid;
        Assert.Equal((40, 100, false), (lead.Result.Stack, lead.Result.MaxStack, lead.Result.Clamped));
        Assert.True(rig.Session.State.Objects.TryGet(leadGuid, out var scarab));
        Assert.Equal(("Lead Scarab", 691u, (ushort)40, (ushort)100), (scarab.Name, scarab.Weenie.WeenieClassId, scarab.Weenie.StackSize, scarab.Weenie.MaxStackSize));
        Assert.Equal(GearKit.ItemTypeSpellComponents, scarab.ItemType);
        Assert.Equal((ushort)(GearKit.ComponentBurden * 40), scarab.Weenie.Burden);
        Assert.Null(scarab.Weenie.ValidLocations);
        Assert.Equal(GearKit.ComponentBurden * 40, rig.Inventory.Burden);
        Assert.Equal(["inventory Added", "reply item", "create " + leadGuid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)], rig.Order.Skip(rig.Order.Count - 3));

        // The wand and a piece of armour: the SELECT's ValidLocations, item type and burden, and the model's burden grows by each (the 02CD).
        var wand = rig.Spawn(8552, null);
        Assert.True(rig.Session.State.Objects.TryGet(wand.Result!.Guid, out var wandObject));
        Assert.Equal(("The Healer's Heart", GearKit.ItemTypeCaster, 0x01000000u, (ushort)50), (wandObject.Name, wandObject.ItemType, wandObject.Weenie.ValidLocations, wandObject.Weenie.Burden));
        Assert.Null(wandObject.Weenie.StackSize);
        var coat = rig.Spawn(6600, null);
        Assert.True(rig.Session.State.Objects.TryGet(coat.Result!.Guid, out var coatObject));
        Assert.Equal(("Greater Amuli Shadow Coat", GearKit.ItemTypeArmor, 0x1A00u, (ushort)1600), (coatObject.Name, coatObject.ItemType, coatObject.Weenie.ValidLocations, coatObject.Weenie.Burden));
        Assert.Equal((GearKit.ComponentBurden * 40) + 50 + 1600, rig.Inventory.Burden);
    }

    [Fact]
    public void A_kit_stack_above_the_weenies_maximum_is_clamped_and_every_kit_weenie_is_answered_while_other_class_ids_keep_the_old_answers()
    {
        using var rig = new Rig();
        var big = rig.Spawn(691, 500);
        Assert.Equal((100, true), (big.Result!.Stack, big.Result.Clamped));
        Assert.Equal(GearKit.ComponentBurden * 100, rig.Inventory.Burden);
        var burden = rig.Inventory.Burden;

        // Pyreal and the unknown class id behave as before; a non-kit weenie sends no weight (the stand-in has none for it).
        var pyreal = rig.Spawn(273, 5);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(pyreal));
        Assert.Equal(burden, rig.Inventory.Burden);
        Assert.Equal(ItemOutcome.UnknownWcid, ItemOutcomes.Of(rig.Spawn(5, 1)));

        // Prismatic Taper (maximum stack 1,000) is in the map though not in the kit.
        var prismatic = rig.Spawn(20631, 2000);
        Assert.Equal((1000, true), (prismatic.Result!.Stack, prismatic.Result.Clamped));
        Assert.Equal(burden + (GearKit.ComponentBurden * 1000), rig.Inventory.Burden);

        // Every weenie the stand-in knows for the kit is created under its own name.
        foreach (var component in ComponentWeenies.All.Where(c => c.Wcid is not (691 or 20631)))
        {
            var made = rig.Spawn(component.Wcid, 1);
            Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(made));
            Assert.Equal(component.Name, made.Result!.Name);
        }
    }
}
