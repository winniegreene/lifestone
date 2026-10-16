using System.Globalization;
using System.Collections.Frozen;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.StandIn;

/// <summary>One item the stand-in created in the player's pack for an <c>item</c> request.</summary>
/// <param name="Guid">The new object's id (a server object's: 0x80000000 and up).</param>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Stack">The stack size it was created with.</param>
internal readonly record struct StandInPackEntry(uint Guid, uint Wcid, string Name, int Stack);

/// <summary>
/// A weenie of the gear kit (M3 1.4 s0, <c>TODO(m3-standin-weenies)</c> answered for the kit): what the world database holds for it, so an <c>item</c> request for the wand, a piece of the leather set or a spell component creates the
/// right object, with its real stack, burden and ValidLocations. The values are <see cref="GearKit"/>'s (the SELECT's) and <see cref="ComponentWeenies"/>'s; a component's burden is 4 (the SELECT's).
/// </summary>
/// <param name="Kind">The weenie as the <c>item</c> rules see it (name, maximum stack).</param>
/// <param name="ItemType">ACE's <c>ItemType</c>.</param>
/// <param name="ValidLocations">The <c>EquipMask</c> bits the item can occupy (0 for a component).</param>
/// <param name="UnitBurden">The burden of one item.</param>
internal sealed record StandInKitWeenie(StandInItemKind Kind, uint ItemType, uint ValidLocations, int UnitBurden);

/// <summary>
/// The stand-in's answers to the server mod's <c>spell learn</c> and <c>item</c> requests (M3 1.1 s2b), in the replies' own bytes (ToolsWire / <see cref="SpellLearnData"/> /
/// <see cref="ItemData"/>), so the client parses one shape against the mod and the stand-in. The rules are the mod's (server/mod Verbs/SpellVerb.cs, Verbs/ItemVerb.cs,
/// Core/ItemCreateRules.cs): every spell id of a request is carried out and each is learned, known (the character had it, or an earlier id of the same request) or unknown (not in
/// the SpellTable); an item is refused as unknown-wcid, not-creatable or no-room, and otherwise created with the stack clamped to the item's maximum (a stack asked for an item that does not
/// stack is ignored). Placeholders, each named: without the SpellTable the stand-in accepts ids 1 to <see cref="PlaceholderMaxSpellId"/> (<c>TODO(m3-standin-spell-table)</c>); the weenies are the
/// visit's few (<c>TODO(m3-standin-weenies)</c>); the pack is a list the stand-in keeps, capped at <see cref="PackCapacity"/> entries. A created item arrives as ACE sends it
/// (<c>Player_Inventory.cs</c> <c>TryCreateInInventoryWithNetworking</c>) in ACE's wire order: GameEvent 0x0022 into the main pack at the next slot, the burden update 02CD EncumbranceVal, the reply line
/// (one UIQueue bundle), and then, <see cref="CreateObjectLagSeconds"/> later, the item's CreateObject (SmartboxQueue: no position, the player as container), so the client's <c>PlayerInventory</c>
/// holds the item before its world object exists (M3 1.2 s2 and the live-gate fix). The stand-in has no weights (<c>TODO(m3-standin-weenies)</c>): the 02CD repeats the burden the model already
/// has and is left out when the description gave none.
/// </summary>
internal sealed partial class StandInWorld
{
    /// <summary>The highest spell id accepted when the visit has no SpellTable (a placeholder; retail's spell ids end below this).</summary>
    internal const uint PlaceholderMaxSpellId = 6500;

    /// <summary>The most entries the stand-in's pack holds before an <c>item</c> answers <c>no-room</c> (a placeholder for ACE's container limit).</summary>
    internal const int PackCapacity = 102;

    /// <summary>How long after the pack's UIQueue messages the item's CreateObject follows (ACE's separate SmartboxQueue bundle, at least 5 ms later; a margin of a tick or two here).</summary>
    internal const double CreateObjectLagSeconds = 0.05;

    /// <summary>
    /// The kit's weenies by class id: the wand and the leather set (<see cref="GearKit.Wand"/>, <see cref="GearKit.Armour"/>) and every component of <see cref="ComponentWeenies.All"/>. A class id the visit's own <c>Items</c> does not hold is looked up here
    /// before it answers <c>unknown-wcid</c>; every other class id behaves as before.
    /// </summary>
    internal static IReadOnlyDictionary<uint, StandInKitWeenie> KitWeenies { get; } = BuildKitWeenies();

    private static FrozenDictionary<uint, StandInKitWeenie> BuildKitWeenies()
    {
        var table = new Dictionary<uint, StandInKitWeenie>();
        foreach (var w in new[] { GearKit.Wand }.Concat(GearKit.Armour))
        {
            table[w.Wcid] = new StandInKitWeenie(new StandInItemKind(w.Name, null), w.ItemType, (uint)w.ValidLocations, w.Burden);
        }

        foreach (var c in ComponentWeenies.All)
        {
            table[c.Wcid] = new StandInKitWeenie(new StandInItemKind(c.Name, c.MaxStack), GearKit.ItemTypeSpellComponents, 0, GearKit.ComponentBurden);
        }

        return table.ToFrozenDictionary();
    }

    /// <summary>The first guid the stand-in gives a created item.</summary>
    internal const uint FirstItemGuid = 0x80010000;

    private readonly Dictionary<uint, HashSet<uint>> _knownSpells = [];
    private readonly List<StandInPackEntry> _pack = [];
    private uint _nextItemGuid = FirstItemGuid;

    /// <summary>The items the stand-in has created in the pack, in order.</summary>
    internal IReadOnlyList<StandInPackEntry> Pack => _pack;

    /// <summary>The spell ids the character <paramref name="characterGuid"/> knows (learned through <c>spell learn</c>; the stand-in starts with none).</summary>
    internal IReadOnlySet<uint> KnownSpells(uint characterGuid) => _knownSpells.TryGetValue(characterGuid, out var known) ? known : new HashSet<uint>();

    /// <summary>The stack the mod's <c>ItemCreateRules.ResolveStack</c> settles on: the request clamped to the maximum of a stacking item, else 1.</summary>
    internal static (int Stack, bool Clamped) ResolveStack(int? requested, int? maxStack)
    {
        var stack = requested is { } asked && maxStack is { } max ? Math.Min(asked, max) : 1;
        return (stack, requested is { } r && stack < r);
    }

    private void SpellLearn(SpellLearnRequest request)
    {
        var guid = _reducer.State.Self.Guid;
        if (!_knownSpells.TryGetValue(guid, out var known))
        {
            known = [];
            _knownSpells[guid] = known;
        }

        var table = _visit.SpellTable;
        var outcomes = new List<SpellOutcome>(request.SpellIds.Count);
        foreach (var id in request.SpellIds)
        {
            string? name = null;
            bool inTable;
            if (table is not null)
            {
                inTable = table.Spells.TryGetValue(id, out var info);
                name = info?.Name;
            }
            else
            {
                inTable = id is >= 1 and <= PlaceholderMaxSpellId;
                name = inTable ? "Spell " + id.ToString(CultureInfo.InvariantCulture) : null;
            }

            var outcome = SpellOutcomes.Classify(inTable, known.Contains(id));
            if (outcome == SpellOutcomes.Learned)
            {
                known.Add(id);
            }

            outcomes.Add(new SpellOutcome(id, outcome, name));
        }

        var data = SpellLearnData.Of(outcomes);
        Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Spell, data), data.Describe())));
    }

    private void Item(ItemRequest request)
    {
        var items = _visit.Items ?? StandInItemKind.Default;
        var id = request.Id;
        ToolError? error = null;
        ItemData? data = null;
        var wcid = request.Wcid.ToString(CultureInfo.InvariantCulture);
        StandInKitWeenie? kit = null;
        var known = items.TryGetValue(request.Wcid, out var kind);
        if (!known && KitWeenies.TryGetValue(request.Wcid, out kit))
        {
            kind = kit.Kind;
            known = true;
        }

        if (!known)
        {
            error = new ToolError(ToolErrors.UnknownWcid, "no weenie has class id " + wcid);
        }
        else if (!kind!.Creatable)
        {
            error = new ToolError(ToolErrors.NotCreatable, "weenie " + wcid + " cannot be picked up, so it cannot go in your pack");
        }
        else if (_pack.Count >= PackCapacity)
        {
            error = new ToolError(ToolErrors.NoRoom, "your pack has no room for " + kind.Name + "; make space or lighten your load");
        }
        else
        {
            var (stack, clamped) = ResolveStack(request.Stack, kind.MaxStack);
            var guid = _nextItemGuid++;
            var placement = (uint)_pack.Count;
            _pack.Add(new StandInPackEntry(guid, request.Wcid, kind.Name, stack));
            data = new ItemData(guid, request.Wcid, kind.Name, stack, request.Stack, kind.MaxStack, clamped);
            var owner = _reducer.State.Self.Guid;
            var created = CreatedItem(guid, owner, request.Wcid, kind, stack, kit);
            // ACE's wire order (M3 1.2 live-gate fix): the UIQueue bundle (0022, the burden's 02CD, the mod's reply line) goes out first and the item's CreateObject (SmartboxQueue) in a later
            // bundle (NetworkSession.cs:192-250 sends the groups in enum order and waits 5 ms between bundles), so a client sees the item in its pack, and the reply, before its world object.
            Later(sink =>
            {
                sink.OnInventoryPutObjInContainer(Context(sink), new GameEventHeader(owner, 0, 0x0022), new InventoryPutObjInContainerEvent(guid, owner, placement, 0));
                if (kit is not null)
                {
                    // A kit weenie weighs what the world database says: the new burden is the model's plus the stack's (the 02CD ACE sends after the grant).
                    PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PlayerInventory.EncumbranceProperty, (_reducer.State.Self.Inventory.Burden ?? 0) + (kit.UnitBurden * stack));
                }
                else if (_reducer.State.Self.Inventory.Burden is { } burden)
                {
                    PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PlayerInventory.EncumbranceProperty, burden);
                }

                Say(sink, ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Item, data), data.Describe()));
                _reducer.StandIn!.At(_reducer.Clock.Now + CreateObjectLagSeconds, late => late.OnCreateObject(Context(late), created, isUpdate: false));
            });
            return;
        }

        Later(sink => Say(sink, ToolsWire.Reply(ToolReply.Failure(id, ToolVerbs.Item, error!), error!.Message)));
    }

    /// <summary>
    /// The CreateObject ACE sends for a created pack item (<c>GameMessageCreateObject</c>): no position, the player as the container, the stack size and its maximum for an item that stacks. The
    /// object's art, ItemType (Money for Pyreal, Misc otherwise), icon and value are placeholders (<c>TODO(m3-standin-weenies)</c>: the stand-in has no weenie database).
    /// </summary>
    private static CreateObjectMsg CreatedItem(uint guid, uint owner, uint wcid, StandInItemKind kind, int stack, StandInKitWeenie? kit)
    {
        var header = WeenieHeaderFlags.Container;
        if (kit is not null)
        {
            header |= WeenieHeaderFlags.Burden;
            if (kit.ValidLocations != 0)
            {
                header |= WeenieHeaderFlags.ValidLocations;
            }
        }

        if (kind.MaxStack is not null)
        {
            header |= WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize;
        }

        var coin = wcid == StandInItemKind.PyrealWcid;
        if (coin)
        {
            header |= WeenieHeaderFlags.Value;
        }

        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup,
            State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal,
            SetupId = 0x02000001,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = header,
            Name = kind.Name,
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = kit is not null ? kit.ItemType : coin ? 0x40u : 0x80u,
            ObjectDescriptionFlags = 0,
            ContainerId = owner,
            StackSize = kind.MaxStack is not null ? (ushort)Math.Min(stack, ushort.MaxValue) : null,
            MaxStackSize = kind.MaxStack is { } max ? (ushort)Math.Min(max, ushort.MaxValue) : null,
            Value = coin ? (uint)stack : null,
            ValidLocations = kit is { ValidLocations: not 0 } ? kit.ValidLocations : null,
            Burden = kit is not null ? (ushort)Math.Min(kit.UnitBurden * stack, ushort.MaxValue) : null,
        };
        return new CreateObjectMsg(guid, ModelData.Empty, physics, weenie);
    }
}
