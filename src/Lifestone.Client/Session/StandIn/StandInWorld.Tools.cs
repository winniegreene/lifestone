using Lifestone.Client.Diagnostics;
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
/// <param name="Worn">The piece's dressing (ClothingBase, own Setup, palette, shade), or null for an item that is not clothing.</param>
/// <param name="Stack">The stack size it was created with.</param>
internal readonly record struct StandInPackEntry(uint Guid, uint Wcid, string Name, int Stack, StandInWorn? Worn = null);

/// <summary>
/// A weenie of the gear kit (M3 1.4 s0, <c>TODO(m3-standin-weenies)</c> answered for the kit): what the world database holds for it, so an <c>item</c> request for the wand, a piece of the leather set or a spell component creates the
/// right object, with its real stack, burden and ValidLocations. The values are <see cref="GearKit"/>'s (the SELECT's) and <see cref="ComponentWeenies"/>'s; a component's burden is 4 (the SELECT's).
/// </summary>
/// <param name="Kind">The weenie as the <c>item</c> rules see it (name, maximum stack).</param>
/// <param name="ItemType">ACE's <c>ItemType</c>.</param>
/// <param name="ValidLocations">The <c>EquipMask</c> bits the item can occupy (0 for a component).</param>
/// <param name="UnitBurden">The burden of one item.</param>
/// <param name="ClothingBase">The ClothingTable (0x10xxxxxx) the piece wears through, or 0.</param>
/// <param name="Palette">The weenie's own palette template, or null.</param>
/// <param name="Shade">The weenie's own shade, or null.</param>
/// <param name="SetupId">The art the created item carries: the weenie's own Setup, or the placeholder human Setup when the stand-in does not know it.</param>
internal sealed record StandInKitWeenie(StandInItemKind Kind, uint ItemType, uint ValidLocations, int UnitBurden, uint SetupId = StandInWorld.PlaceholderItemSetup, uint ClothingBase = 0, int? Palette = null, double? Shade = null);

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
    /// The kit's art by class id (the pinned ACE world database 0.9.296, D43 of the M3 plan): the weenie's Setup (<c>weenie_properties_d_i_d</c> type 1), its ClothingBase (type 7), and the palette template and
    /// shade the weenie carries (Healer's Heart none; Olthoi Helm 13 at 0.66; the Amuli coat 21 and leggings 18 at 0.3; the platemail gauntlets and the sollerets template 20 with the shade not read:
    /// <c>TODO(q7-weenie-shades)</c>, so 0 as ACE does for a weenie with none).
    /// </summary>
    private static readonly Dictionary<uint, (uint Setup, uint ClothingBase, int? Palette, double? Shade)> KitArt = new()
    {
        [8552] = (WandSetup, 0x10000249, null, null),
        [3715] = (0x020000DA, 0x1000002C, 13, 0.66),
        [6600] = (0x02000126, 0x100001A1, 21, 0.3),
        [6606] = (0x02000128, 0x100001A0, 18, 0.3),
        [57] = (0x020000D8, 0x10000011, 20, null),
        [107] = (0x020000DE, 0x10000054, 20, null),

        // M5 7 S4 (D122): the live mage's hooded robe and gloves (GearKit.SupplyWeenies), so the stand-in dresses them as ACE does: 5851 robealuvianhood ClothingBase 0x1000018E, palette template 4,
        // shade 0.5; 56 gauntletsleather ClothingBase 0x10000008, palette template 4, shade 0.66 (weenie_properties_d_i_d type 7, int type 3, float type 12 of the pinned world 0.9.296).
        [5851] = (0x020001A6, 0x1000018E, 4, 0.5),
        [56] = (0x020000D8, 0x10000008, 4, 0.66),
    };

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
            var art = KitArt.TryGetValue(w.Wcid, out var a) ? a : (Setup: StandInWeenieArt.SetupOr(w.Wcid, PlaceholderItemSetup), ClothingBase: 0u, Palette: (int?)null, Shade: (double?)null);
            table[w.Wcid] = new StandInKitWeenie(new StandInItemKind(w.Name, null), w.ItemType, (uint)w.ValidLocations, w.Burden, art.Setup, art.ClothingBase, art.Palette, art.Shade);
        }

        // M5 3 m1: the ground's vendor kit, the four foci, the potion, the pyreal coin and the trade notes (GearKit.SupplyWeenies): placeholder art, the item verb creates them offline.
        foreach (var w in GearKit.SupplyWeenies)
        {
            table[w.Wcid] = KitArt.TryGetValue(w.Wcid, out var dressed)
                ? new StandInKitWeenie(new StandInItemKind(w.Name, w.MaxStack), w.ItemType, (uint)w.ValidLocations, w.UnitBurden, dressed.Setup, dressed.ClothingBase, dressed.Palette, dressed.Shade)
                : new StandInKitWeenie(new StandInItemKind(w.Name, w.MaxStack), w.ItemType, (uint)w.ValidLocations, w.UnitBurden, StandInWeenieArt.SetupOr(w.Wcid, PlaceholderItemSetup));
        }

        foreach (var c in ComponentWeenies.All)
        {
            table[c.Wcid] = new StandInKitWeenie(new StandInItemKind(c.Name, c.MaxStack), GearKit.ItemTypeSpellComponents, 0, GearKit.ComponentBurden, StandInWeenieArt.SetupOr(c.Wcid, PlaceholderItemSetup));
        }

        return table.ToFrozenDictionary();
    }

    /// <summary>The first guid the stand-in gives a created item.</summary>
    internal const uint FirstItemGuid = 0x80010000;

    private readonly Dictionary<uint, HashSet<uint>> _knownSpells = [];
    private readonly List<StandInPackEntry> _pack = [];
    private readonly HashSet<uint> _placeholderSetupLogged = [];
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
        Later(sink =>
        {
            // M6 15: ACE's LearnSpellWithNetworking sends GameEvent 0x02C1 MagicUpdateSpell for each spell it adds (Player_Spells.cs 48-83 @95ef426d; the mod's quiet form too: SpellVerb), so the client's spellbook learns it. The
            // stand-in kept the spell to itself until now, which nothing read; the player's rule does (a spell goes on a spell bar only when the client's book holds it).
            foreach (var learned in outcomes)
            {
                if (learned.Outcome == SpellOutcomes.Learned && learned.Id is > 0 and <= ushort.MaxValue)
                {
                    AnswerLog.Add("02C1");
                    sink.OnMagicUpdateSpell(Context(sink), new GameEventHeader(guid, 0, 0x02C1), new MagicUpdateSpellEvent((ushort)learned.Id, 1));
                }
            }

            Say(sink, ToolsWire.Reply(ToolReply.Success(request.Id, ToolVerbs.Spell, data), data.Describe()));
        });
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
            if (!StandInWeenieArt.TryGet(request.Wcid, out _) && _placeholderSetupLogged.Add(request.Wcid))
            {
                // M4 4.1 v4: a weenie the art table does not hold wears the placeholder small item model (the icon's TODO is logged by StandInWeenieArt.Apply).
                Log?.Info(SessionLog.Category, "TODO(m4-standin-art-missing): weenie " + wcid + " (" + kind.Name + ") has no row in StandInWeenieArt; it wears the placeholder small item model 0x" + PlaceholderItemSetup.ToString("X8", CultureInfo.InvariantCulture));
            }

            var (stack, clamped) = ResolveStack(request.Stack, kind.MaxStack);
            var guid = _nextItemGuid++;
            var placement = (uint)_pack.Count;
            var worn = kit is { ClothingBase: not 0 } ? new StandInWorn(kit.ClothingBase, kit.SetupId, request.Palette ?? kit.Palette ?? 0, request.Shade ?? kit.Shade ?? 0.0) : (StandInWorn?)null;
            _pack.Add(new StandInPackEntry(guid, request.Wcid, kind.Name, stack, worn));
            data = new ItemData(guid, request.Wcid, kind.Name, stack, request.Stack, kind.MaxStack, clamped);
            var owner = _reducer.State.Self.Guid;
            var created = CreatedItem(guid, owner, request.Wcid, kind, stack, kit, Log, worn is { } w && _visit.Dresser is { } dresser ? StandInHumanLook.FromObjDesc(dresser.Item(w)) : ModelData.Empty);
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
    /// object's Setup and icons are its weenie's own (<see cref="StandInWeenieArt"/>, M4 4.1 v4); the ItemType (Money for Pyreal, Misc otherwise) and value are placeholders (<c>TODO(m3-standin-weenies)</c>: the stand-in has no weenie database).
    /// </summary>
    /// <summary>The Setup of an item whose class id <see cref="StandInWeenieArt"/> does not hold: the Pyreal coin's small Setup (0x02000B77, read from the dats; <c>TODO(m4-standin-art-missing)</c>, logged once per class id; it closes <c>TODO(m3-standin-unknown-item-setup)</c>, since every weenie the stand-in serves has its own Setup in the table). Never a human Setup: a held item drawn with the player's showed a second body in the hand (M3 4.1 A2; again after D43 moved the caster to weenie 8552).</summary>
    internal const uint PlaceholderItemSetup = 0x02000B77;

    /// <summary>The caster's own Setup: weenie 8552 "The Healer's Heart" (M3 4.2 P7, D43), <c>weenie_properties_d_i_d</c> type 1 (Setup) = 0x020009AD (ACE world database 0.9.296; the old wand 2472 was 0x020000FC).</summary>
    internal const uint WandSetup = 0x020009AD;

    /// <summary>
    /// The physics state bit a weenie's <c>LightsStatus</c> gives (ACE world database 0.9.296: <c>weenie_properties_bool</c> type 15 = 1 on weenie 8552 "The Healer's Heart"; ACE <c>WorldObject_Properties.cs</c> 1057-1060 maps
    /// <c>LightsStatus</c> to <c>PhysicsState.LightingOn</c>, which retail's set_lights 0x005107C0 reads), so the held orb's Setup light can light (M4 4.1 fix2b). Only the Healer's Heart is listed:
    /// <c>TODO(m4-standin-lights-status)</c> for any other weenie that has the property (4,050 do; the stand-in's art table has no lights flag).
    /// </summary>
    internal static PhysicsState LightingOf(uint wcid) => wcid == HealersHeartWcid ? PhysicsState.LightingOn : PhysicsState.None;

    /// <summary>The weenie class id of The Healer's Heart (the caster the kit wields).</summary>
    internal const uint HealersHeartWcid = 8552;

    private static CreateObjectMsg CreatedItem(uint guid, uint owner, uint wcid, StandInItemKind kind, int stack, StandInKitWeenie? kit, ILogSink? log, ModelData model)
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
            State = PhysicsState.IgnoreCollisions | PhysicsState.Ethereal | LightingOf(wcid),
            SetupId = SetupFor(wcid, kit),
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
        return new CreateObjectMsg(guid, model, physics, StandInWeenieArt.Apply(weenie, log));
    }

    /// <summary>The Setup an item of weenie <paramref name="wcid"/> carries: the kit entry's own (a worn piece's, the wand's), else the art table's, else the placeholder small item model (<c>TODO(m4-standin-art-missing)</c>).</summary>
    internal static uint SetupFor(uint wcid, StandInKitWeenie? kit) =>
        kit is not null && kit.SetupId != PlaceholderItemSetup ? kit.SetupId : StandInWeenieArt.SetupOr(wcid, PlaceholderItemSetup);
}
