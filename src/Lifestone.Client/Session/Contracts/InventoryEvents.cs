namespace Lifestone.Client.Session.Contracts;

/// <summary>What an <see cref="InventoryChanged"/> says happened to <see cref="InventoryChanged.Guid"/>.</summary>
public enum InventoryChangeKind
{
    /// <summary>A whole PlayerDescription (GameEvent 0x0013) replaced the lists; <see cref="InventoryChanged.Guid"/> is 0.</summary>
    Reset,

    /// <summary>An item the player did not hold went into a container (GameEvent 0x0022: created in the pack, picked up, given).</summary>
    Added,

    /// <summary>A held, unequipped item went to another container or slot (0x0022).</summary>
    Moved,

    /// <summary>An item was wielded or worn (0x0023); <see cref="InventoryChanged.FromContainer"/> or <see cref="InventoryChanged.FromLocation"/> say where it came from.</summary>
    Wielded,

    /// <summary>An equipped item went into a container (0x0022 for an item on the equipped list); <see cref="InventoryChanged.FromLocation"/> is the location it left.</summary>
    Unwielded,

    /// <summary>A held item was dropped to the ground (GameEvent 0x019A).</summary>
    Dropped,

    /// <summary>A held item is gone (GameMessage 0x0024: a component burnt, an item consumed or given), or was put in a container that is not the player's (0x0022 into a chest).</summary>
    Removed,

    /// <summary>A held item's stack size and value changed (GameMessage 0x0197).</summary>
    StackChanged,

    /// <summary>An int quality of a held item changed (GameMessage 0x02CE, for example CurrentWieldedLocation 10).</summary>
    PropertyChanged,
}

/// <summary>
/// The player's inventory changed (<see cref="PlayerInventory"/>, <see cref="SelfState.Inventory"/>). It carries what a panel or a tracker needs without re-reading the state: the item, where it is now
/// and where it was. Fields that do not apply to the <see cref="Kind"/> are 0 (a placement is -1 when there is none).
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Guid">The item (0 for <see cref="InventoryChangeKind.Reset"/>).</param>
/// <param name="Container">The container the item is in now (the player's id for the main pack; a side pack's id; for <see cref="InventoryChangeKind.Removed"/> by 0022 the foreign container; 0 when none).</param>
/// <param name="Placement">The slot in <paramref name="Container"/> (the index in its list), or -1.</param>
/// <param name="Location">The <c>EquipMask</c> the item occupies now (<see cref="InventoryChangeKind.Wielded"/>), else 0.</param>
/// <param name="FromContainer">The container the item was in before, or 0.</param>
/// <param name="FromPlacement">The slot it had there, or -1.</param>
/// <param name="FromLocation">The <c>EquipMask</c> it occupied before (equipped items), or 0.</param>
/// <param name="StackSize"><see cref="InventoryChangeKind.StackChanged"/>: the new stack size.</param>
/// <param name="Value"><see cref="InventoryChangeKind.StackChanged"/>: the new value.</param>
/// <param name="Property"><see cref="InventoryChangeKind.PropertyChanged"/>: the <c>PropertyInt</c> id.</param>
/// <param name="PropertyValue"><see cref="InventoryChangeKind.PropertyChanged"/>: the new value.</param>
public readonly record struct InventoryChanged(
    InventoryChangeKind Kind,
    uint Guid,
    uint Container = 0,
    int Placement = -1,
    uint Location = 0,
    uint FromContainer = 0,
    int FromPlacement = -1,
    uint FromLocation = 0,
    uint StackSize = 0,
    uint Value = 0,
    uint Property = 0,
    int PropertyValue = 0) : IGameEvent;

/// <summary>
/// GameEvent 0x00A0 InventoryServerSaysFailed: the server refused an inventory action (a wield, a move, a drop). The state is unchanged; a tracker waiting on the item completes with this.
/// </summary>
/// <param name="Item">The item the action was about.</param>
/// <param name="Error">The <c>WeenieError</c> (0 when the server gave none, as ACE does for most refusals).</param>
public readonly record struct InventoryFailed(uint Item, uint Error) : IGameEvent;

/// <summary>The player opened the ground container <see cref="Container"/> (a corpse or chest): its GameEvent 0x0196 ViewContents arrived (M4 2.1 s1). Its entries may not have objects yet (<see cref="ContainerView.IsResolved"/>).</summary>
/// <param name="Container">The container's object id.</param>
public readonly record struct ContainerOpened(uint Container) : IGameEvent;

/// <summary>The viewed ground container <see cref="Container"/> closed: GameEvent 0x0052 (the server's close, also after the client's 0x0195), a second open that replaced it, its deletion, or leaving the world (M4 2.1 s1).</summary>
/// <param name="Container">The container's object id.</param>
public readonly record struct ContainerClosed(uint Container) : IGameEvent;

/// <summary>
/// The set of entries of <see cref="Container"/> (the viewed ground container or a side pack), or whether they are resolved, changed (M4 2.1 s1): a 0196 named them, an item moved in or out, or an
/// entry's F745 arrived. Read <see cref="GameState.Containers"/> for the entries.
/// </summary>
/// <param name="Container">The container's object id.</param>
public readonly record struct ContainerContentsChanged(uint Container) : IGameEvent;
