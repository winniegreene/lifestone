namespace Lifestone.Client.Session.Contracts;

/// <summary>An object in one of the player's packs.</summary>
/// <param name="Guid">The object's id; its name, class, icon and values are its <see cref="WorldObject"/> (<see cref="GameState.Objects"/>), never copied here.</param>
/// <param name="ContainerType">ACE's <c>ContainerType</c>: 0 an ordinary item, 1 a container (a side pack), 2 a foci.</param>
public readonly record struct InventoryEntry(uint Guid, uint ContainerType);

/// <summary>An object the player wields or wears.</summary>
/// <param name="Guid">The object's id.</param>
/// <param name="Location">The <c>EquipMask</c> it occupies (one or more bits).</param>
/// <param name="Priority">The clothing priority the description sent, or 0 when the server sent none.</param>
public readonly record struct EquippedItem(uint Guid, uint Location, uint Priority);

/// <summary>Where <see cref="PlayerInventory.Find"/> found an item.</summary>
public enum InventoryPlaceKind
{
    /// <summary>The player does not hold the item (as far as the inventory model knows).</summary>
    None,

    /// <summary>In the main pack (the player is the container): an ordinary item.</summary>
    MainPack,

    /// <summary>In the main pack and itself a container or a foci (a side pack).</summary>
    SidePack,

    /// <summary>In a side pack (<see cref="InventoryPlace.Container"/> names it).</summary>
    InSidePack,

    /// <summary>Wielded or worn (<see cref="InventoryPlace.Location"/> is the <c>EquipMask</c>).</summary>
    Equipped,
}

/// <summary>Where the player holds an item.</summary>
/// <param name="Kind">What kind of place it is.</param>
/// <param name="Container">The container holding it (the player's id for the main pack and the side packs; the side pack's id for <see cref="InventoryPlaceKind.InSidePack"/>); 0 for none or equipped.</param>
/// <param name="Placement">The item's slot in its container (the index in the container's list; ACE's <c>PlacementPosition</c>, which the server sends in 0022), or -1.</param>
/// <param name="Location">The <c>EquipMask</c> for <see cref="InventoryPlaceKind.Equipped"/>, else 0.</param>
public readonly record struct InventoryPlace(InventoryPlaceKind Kind, uint Container, int Placement, uint Location)
{
    /// <summary>The place of an item the player does not hold.</summary>
    public static InventoryPlace NotHeld => new(InventoryPlaceKind.None, 0, -1, 0);

    /// <summary>True when the player holds the item.</summary>
    public bool Held => Kind != InventoryPlaceKind.None;
}

/// <summary>
/// The player's inventory (<see cref="SelfState.Inventory"/>; M3 1.2 s2): the main pack's items in order, the side packs and what is in
/// them, the wielded and worn items by <c>EquipMask</c> location, and the burden. It holds guids and positions only; an item's name, class,
/// icon and values are its <see cref="WorldObject"/> in <see cref="GameState.Objects"/> (the world object's container, wielder, wielded
/// location, stack size and value are kept in step with the server's messages).
/// </summary>
/// <remarks>
/// <para>Filled by GameEvent 0x0013's <c>Inventory</c> and <c>Equipped</c> lists, then by GameEvents 0x0022 (into a container), 0x0023
/// (wielded), 0x019A (dropped) and GameMessages 0x0024 (removed), 0x0197 (stack size), 0x02CE (an int quality of an item) and 0x02DA
/// (container or wielder of an item). The reducer mutates it; <see cref="InventoryChanged"/> follows every change and
/// <see cref="InventoryFailed"/> a refusal. Membership follows 0022, 0023, 019A and 0024 only: ACE sends 0x02DA and 0x02CE alongside them
/// with values that do not always agree with the final state (its wield sends Wielder 0 and CurrentWieldedLocation 0 before 0x0023 names
/// the location), so those two only update the item's qualities.</para>
/// <para>Burden is read from the player's <c>EncumbranceVal</c> quality (int 5; <see cref="PlayerQualities"/>), never computed. The
/// capacity is not sent by the server: retail derives it from Strength and augmentations, and the character panel computes it (<c>GameCharacterInfo.LoadOf</c>; <c>TODO(m3-burden-capacity)</c> closed).</para>
/// </remarks>
public sealed class PlayerInventory
{
    /// <summary>The <c>PropertyInt</c> id of the burden (ACE's <c>EncumbranceVal</c>).</summary>
    public const uint EncumbranceProperty = 5;

    private readonly List<InventoryEntry> _main = [];
    private readonly List<InventoryEntry> _sidePacks = [];
    private readonly Dictionary<uint, List<InventoryEntry>> _contents = [];
    private readonly List<EquippedItem> _equipped = [];
    private readonly Dictionary<uint, Dictionary<uint, int>> _ints = [];
    private readonly Dictionary<uint, (uint Size, uint Value)> _stacks = [];
    private readonly Dictionary<(uint Guid, uint Tag), byte> _stamps = [];
    private PlayerQualities? _qualities;

    /// <summary>An empty inventory. Only the session creates one.</summary>
    internal PlayerInventory()
    {
    }

    /// <summary>True once a PlayerDescription's lists were applied (the lists below are empty before).</summary>
    public bool IsKnown { get; internal set; }

    /// <summary>
    /// False when the description could not carry the lists (its gameplay options sit in front of them: <see cref="Lifestone.Net.Messages.PlayerDescriptionMsg.InventoryUndecoded"/>),
    /// so the lists hold only what later messages told. True otherwise.
    /// </summary>
    public bool ListsComplete { get; internal set; } = true;

    /// <summary>The player's object id (the main pack's container id), or 0 outside the world.</summary>
    public uint Owner { get; internal set; }

    /// <summary>The main pack's ordinary items, in slot order.</summary>
    public IReadOnlyList<InventoryEntry> Pack => _main;

    /// <summary>The side packs (containers and foci held in the main pack), in slot order.</summary>
    public IReadOnlyList<InventoryEntry> SidePacks => _sidePacks;

    /// <summary>The wielded and worn items, in the order they were wielded (the description's order first).</summary>
    public IReadOnlyList<EquippedItem> Equipped => _equipped;

    /// <summary>The number of items the player holds: the main pack, the side packs, what is in them and what is equipped.</summary>
    public int Count
    {
        get
        {
            var n = _main.Count + _sidePacks.Count + _equipped.Count;
            foreach (var list in _contents.Values)
            {
                n += list.Count;
            }

            return n;
        }
    }

    /// <summary>The player's burden (the <c>EncumbranceVal</c> quality), or null before the description arrives.</summary>
    public int? Burden => _qualities is { } q && q.Ints.TryGetValue(EncumbranceProperty, out var burden) ? burden : null;

    /// <summary>The items in the container <paramref name="container"/>: the main pack's ordinary items for the player's id, a side pack's items for the side pack's id; empty otherwise.</summary>
    public IReadOnlyList<InventoryEntry> ItemsIn(uint container) =>
        container == Owner && Owner != 0 ? _main : _contents.TryGetValue(container, out var list) ? list : [];

    /// <summary>Where the player holds <paramref name="guid"/>.</summary>
    public InventoryPlace Find(uint guid)
    {
        var at = IndexOf(_main, guid);
        if (at >= 0)
        {
            return new InventoryPlace(InventoryPlaceKind.MainPack, Owner, at, 0);
        }

        at = IndexOf(_sidePacks, guid);
        if (at >= 0)
        {
            return new InventoryPlace(InventoryPlaceKind.SidePack, Owner, at, 0);
        }

        foreach (var pair in _contents)
        {
            at = IndexOf(pair.Value, guid);
            if (at >= 0)
            {
                return new InventoryPlace(InventoryPlaceKind.InSidePack, pair.Key, at, 0);
            }
        }

        foreach (var item in _equipped)
        {
            if (item.Guid == guid)
            {
                return new InventoryPlace(InventoryPlaceKind.Equipped, 0, -1, item.Location);
            }
        }

        return InventoryPlace.NotHeld;
    }

    /// <summary>True when the player holds <paramref name="guid"/> (a pack, a side pack, inside one, or equipped).</summary>
    public bool Contains(uint guid) => Find(guid).Held;

    /// <summary>The equipped entry of <paramref name="guid"/>.</summary>
    /// <param name="guid">The item.</param>
    /// <param name="item">The entry, or default.</param>
    /// <returns>True when the item is equipped.</returns>
    public bool TryGetEquipped(uint guid, out EquippedItem item)
    {
        foreach (var e in _equipped)
        {
            if (e.Guid == guid)
            {
                item = e;
                return true;
            }
        }

        item = default;
        return false;
    }

    /// <summary>The item whose location includes any bit of <paramref name="location"/> (an <c>EquipMask</c>), or null when none is equipped there.</summary>
    public uint? EquippedAt(uint location)
    {
        foreach (var e in _equipped)
        {
            if ((e.Location & location) != 0)
            {
                return e.Guid;
            }
        }

        return null;
    }

    /// <summary>The stack size and value the server last sent for <paramref name="guid"/> in a SetStackSize (0x0197); false when none was sent since it was held.</summary>
    public bool TryGetStack(uint guid, out uint stackSize, out uint value)
    {
        if (_stacks.TryGetValue(guid, out var stack))
        {
            (stackSize, value) = stack;
            return true;
        }

        stackSize = value = 0;
        return false;
    }

    /// <summary>The int quality the server last sent for the held item <paramref name="guid"/> in a PublicUpdatePropertyInt (0x02CE).</summary>
    /// <param name="guid">The item.</param>
    /// <param name="property">The <c>PropertyInt</c> id.</param>
    /// <param name="value">The value, or 0.</param>
    /// <returns>True when one was sent.</returns>
    public bool TryGetInt(uint guid, uint property, out int value)
    {
        if (_ints.TryGetValue(guid, out var ints) && ints.TryGetValue(property, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Forgets everything (the character left the world, or the session was reset).</summary>
    internal void Clear()
    {
        _main.Clear();
        _sidePacks.Clear();
        _contents.Clear();
        _equipped.Clear();
        _ints.Clear();
        _stacks.Clear();
        _stamps.Clear();
        IsKnown = false;
        ListsComplete = true;
        Owner = 0;
    }

    /// <summary>The reducer binds the player's qualities so <see cref="Burden"/> reads them live.</summary>
    internal void Bind(PlayerQualities qualities) => _qualities = qualities;

    /// <summary>Replaces the lists with a PlayerDescription's: side packs (container types 1 and 2) apart from ordinary items, in wire order.</summary>
    internal void Replace(IEnumerable<InventoryEntry> pack, IEnumerable<EquippedItem> equipped, bool complete)
    {
        _main.Clear();
        _sidePacks.Clear();
        _contents.Clear();
        _equipped.Clear();
        _ints.Clear();
        _stacks.Clear();
        foreach (var entry in pack)
        {
            (entry.ContainerType != 0 ? _sidePacks : _main).Add(entry);
            if (entry.ContainerType != 0)
            {
                _contents[entry.Guid] = [];
            }
        }

        _equipped.AddRange(equipped);
        IsKnown = true;
        ListsComplete = complete;
    }

    /// <summary>True when <paramref name="container"/> is the player or one of the side packs.</summary>
    internal bool IsKnownContainer(uint container) => (container == Owner && Owner != 0) || _contents.ContainsKey(container);

    /// <summary>Adds a side pack the server told about after the description (a pack created or learned from its own CreateObject).</summary>
    internal void AddKnownSidePack(InventoryEntry pack, int placement)
    {
        if (_contents.ContainsKey(pack.Guid))
        {
            return;
        }

        Insert(_sidePacks, pack, placement);
        _contents[pack.Guid] = [];
    }

    /// <summary>
    /// Replaces the contents of the side pack <paramref name="pack"/> with what a 0196 ViewContents named, in its order and with its ContainerType (a foci keeps type 2). An entry held elsewhere (the main pack, another
    /// side pack) moves here: the server's word is the latest. An entry that is a side pack or equipped is left where it is. False when <paramref name="pack"/> is not one of the player's side packs.
    /// </summary>
    internal bool SetContents(uint pack, IReadOnlyList<InventoryEntry> entries)
    {
        if (pack == Owner || !_contents.TryGetValue(pack, out var list))
        {
            return false;
        }

        list.Clear();
        foreach (var entry in entries)
        {
            var at = Find(entry.Guid);
            if (at.Kind is InventoryPlaceKind.SidePack or InventoryPlaceKind.Equipped)
            {
                continue;
            }

            if (at.Held)
            {
                Remove(entry.Guid, keepQualities: true);
            }

            list.Add(entry);
        }

        return true;
    }

    /// <summary>
    /// Puts the item in <paramref name="container"/> (the player or a known side pack) at <paramref name="placement"/>, taking it from wherever it was held (the equipped list too), and returns where it was.
    /// </summary>
    internal InventoryPlace PutInContainer(uint guid, uint containerType, uint container, int placement)
    {
        var before = Remove(guid, keepQualities: true);
        var entry = new InventoryEntry(guid, containerType);
        if (container == Owner && Owner != 0)
        {
            if (containerType != 0)
            {
                _contents.TryAdd(guid, []);
            }

            Insert(containerType != 0 ? _sidePacks : _main, entry, placement);
        }
        else
        {
            Insert(_contents[container], entry, placement);
        }

        return before;
    }

    /// <summary>Equips the item at <paramref name="location"/>, taking it from wherever it was held, and returns where it was.</summary>
    internal InventoryPlace Equip(uint guid, uint location, uint priority)
    {
        var kept = PriorityOf(guid, priority);
        var before = Remove(guid, keepQualities: true);
        _equipped.Add(new EquippedItem(guid, location, kept));
        return before;
    }

    /// <summary>Takes the item out of whatever holds it and forgets its stack and qualities; returns where it was.</summary>
    internal InventoryPlace Release(uint guid) => Remove(guid, keepQualities: false);

    /// <summary>Takes the item out of its list; a side pack that leaves for good takes its contents list with it (they are no longer the player's).</summary>
    private InventoryPlace Remove(uint guid, bool keepQualities)
    {
        var before = Find(guid);
        switch (before.Kind)
        {
            case InventoryPlaceKind.MainPack:
                _main.RemoveAt(before.Placement);
                break;
            case InventoryPlaceKind.SidePack:
                _sidePacks.RemoveAt(before.Placement);
                break;
            case InventoryPlaceKind.InSidePack:
                _contents[before.Container].RemoveAt(before.Placement);
                break;
            case InventoryPlaceKind.Equipped:
                _equipped.RemoveAll(e => e.Guid == guid);
                break;
        }

        if (before.Kind == InventoryPlaceKind.SidePack && !keepQualities)
        {
            if (_contents.Remove(guid, out var inside))
            {
                foreach (var item in inside)
                {
                    _ints.Remove(item.Guid);
                    _stacks.Remove(item.Guid);
                }
            }
        }

        if (!keepQualities)
        {
            _ints.Remove(guid);
            _stacks.Remove(guid);
        }

        return before;
    }

    /// <summary>Keeps the last stack size and value the server sent for a held item.</summary>
    internal void SetStack(uint guid, uint size, uint value) => _stacks[guid] = (size, value);

    /// <summary>Keeps an int quality of a held item.</summary>
    internal void SetInt(uint guid, uint property, int value)
    {
        if (!_ints.TryGetValue(guid, out var ints))
        {
            ints = [];
            _ints[guid] = ints;
        }

        ints[property] = value;
    }

    /// <summary>
    /// <c>WTimeStamper::UpdateTS</c> (11.4186 0x006B2D70, 11.6096 0x006B3CB0), per object and quality: a quality seen for the first time is accepted; afterwards an update is refused only when
    /// its 8-bit sequence is older (a distance above 127 means the counter wrapped), and equal is accepted.
    /// </summary>
    internal bool AcceptStamp(uint guid, uint tag, byte sequence)
    {
        if (_stamps.TryGetValue((guid, tag), out var stored) && (Math.Abs(sequence - stored) > 127 ? stored < sequence : sequence < stored))
        {
            return false;
        }

        _stamps[(guid, tag)] = sequence;
        return true;
    }

    private uint PriorityOf(uint guid, uint fallback)
    {
        foreach (var e in _equipped)
        {
            if (e.Guid == guid)
            {
                return e.Priority;
            }
        }

        return fallback;
    }

    private static int IndexOf(List<InventoryEntry> list, uint guid)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Guid == guid)
            {
                return i;
            }
        }

        return -1;
    }

    private static void Insert(List<InventoryEntry> list, InventoryEntry entry, int placement) =>
        list.Insert(Math.Clamp(placement, 0, list.Count), entry);
}
