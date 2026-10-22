namespace Lifestone.Client.Session.Contracts;

/// <summary>One entry of a <see cref="ContainerView"/>: an item the server named in a GameEvent 0x0196 ViewContents.</summary>
/// <param name="Guid">The item's id; its name, icon and values are its <see cref="WorldObject"/> (<see cref="GameState.Objects"/>), never copied here.</param>
/// <param name="ContainerType">ACE's <c>ContainerType</c>: 0 an ordinary item, 1 a container, 2 a foci.</param>
/// <param name="Resolved">True once the item's F745 CreateObject arrived (for a ground container: since that 0196, so a re-open waits for the resent objects); until then the item has no usable world object.</param>
public readonly record struct ContainerViewEntry(uint Guid, uint ContainerType, bool Resolved);

/// <summary>
/// What a container shows (M4 2.1 s1): the entries of the server's 0x0196 ViewContents in its order, and whether every entry's object has arrived. ACE sends the 0196 <b>before</b> the F745 of every
/// guid it names (capture C1 at login and on a pack's arrival, C2 for a corpse), so a view holds guids and resolves them as their objects land.
/// </summary>
/// <remarks>
/// A viewed ground container's view (<see cref="ContainerViews.Viewed"/>) is the reducer's own object and follows every change; read it, never keep it past <see cref="ContainerClosed"/>. A side pack's view
/// (<see cref="ContainerViews.TryGet"/>) is a snapshot built from the inventory model on the call, so it is as current as the call.
/// </remarks>
public sealed class ContainerView
{
    private readonly List<ContainerViewEntry> _entries = [];

    internal ContainerView(uint guid, bool isGround, bool contentsNamed)
    {
        Guid = guid;
        IsGround = isGround;
        ContentsNamed = contentsNamed;
    }

    /// <summary>The container's object id.</summary>
    public uint Guid { get; }

    /// <summary>True for a corpse or chest the player has open (not the player and not one of the player's side packs).</summary>
    public bool IsGround { get; }

    /// <summary>True when a 0196 named the contents; false for a side pack whose contents were only learned from its items' own CreateObjects (the tolerance when no 0196 named them).</summary>
    public bool ContentsNamed { get; }

    /// <summary>The entries in the server's slot order.</summary>
    public IReadOnlyList<ContainerViewEntry> Entries => _entries;

    /// <summary>How many entries have no object yet.</summary>
    public int Unresolved { get; private set; }

    /// <summary>True when every entry's object has arrived (an empty container is resolved).</summary>
    public bool IsResolved => Unresolved == 0;

    internal void Add(ContainerViewEntry entry, int index = -1)
    {
        _entries.Insert(index < 0 || index > _entries.Count ? _entries.Count : index, entry);
        Unresolved += entry.Resolved ? 0 : 1;
    }

    internal bool Resolve(uint guid)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Guid == guid)
            {
                if (_entries[i].Resolved)
                {
                    return false;
                }

                _entries[i] = _entries[i] with { Resolved = true };
                Unresolved--;
                return true;
            }
        }

        return false;
    }

    internal bool Remove(uint guid)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Guid == guid)
            {
                Unresolved -= _entries[i].Resolved ? 0 : 1;
                _entries.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    internal bool Contains(uint guid)
    {
        foreach (var e in _entries)
        {
            if (e.Guid == guid)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The containers the player sees (<see cref="GameState.Containers"/>): the viewed ground container (at most one) and the contents of the player's side packs. Only the reducer changes it.
/// </summary>
public sealed class ContainerViews
{
    private readonly PlayerInventory _inventory;
    private readonly WorldObjectTable _objects;
    private readonly HashSet<uint> _named = [];

    /// <summary>A pack spawn's 0196 comes before the pack's 0022 and F745: held by guid until the pack is known as the player's (or as a ground container).</summary>
    internal Dictionary<uint, InventoryEntry[]> Pending { get; } = [];

    internal ContainerViews(PlayerInventory inventory, WorldObjectTable objects)
    {
        _inventory = inventory;
        _objects = objects;
    }

    /// <summary>The ground container the player has open (a corpse or a chest), or null. At most one: a second open replaces the first.</summary>
    public ContainerView? Viewed { get; internal set; }

    /// <summary>The viewed ground container's id, or 0 when none is open.</summary>
    public uint ViewedGuid => Viewed?.Guid ?? 0;

    /// <summary>
    /// The view of <paramref name="container"/>: the viewed ground container, or a snapshot of a side pack's contents (in the 0196's slot order, the foci type kept). False for any other guid.
    /// </summary>
    /// <param name="container">The container's object id.</param>
    /// <param name="view">The view.</param>
    /// <returns>True when the container is the viewed one or one of the player's side packs.</returns>
    public bool TryGet(uint container, out ContainerView view)
    {
        if (Viewed is { } ground && ground.Guid == container)
        {
            view = ground;
            return true;
        }

        if (container != _inventory.Owner && _inventory.IsKnownContainer(container))
        {
            view = new ContainerView(container, isGround: false, _named.Contains(container));
            foreach (var entry in _inventory.ItemsIn(container))
            {
                view.Add(new ContainerViewEntry(entry.Guid, entry.ContainerType, _objects.TryGet(entry.Guid, out _)));
            }

            return true;
        }

        view = null!;
        return false;
    }

    /// <summary>True when a 0196 named the contents of the side pack <paramref name="pack"/>.</summary>
    public bool ContentsNamed(uint pack) => _named.Contains(pack);

    internal void MarkNamed(uint pack) => _named.Add(pack);

    internal void Forget(uint container)
    {
        _named.Remove(container);
        Pending.Remove(container);
    }

    internal void Clear()
    {
        Viewed = null;
        _named.Clear();
        Pending.Clear();
    }
}
