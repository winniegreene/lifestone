using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>Why a drop is refused before anything is sent.</summary>
public enum DropVerdict
{
    /// <summary>The drop may go to the Action API.</summary>
    Ok,

    /// <summary>The target names no container (an empty part of a list, no open container).</summary>
    NoTarget,

    /// <summary>A pack dropped on itself.</summary>
    Self,

    /// <summary>A pack dropped on one of its own contents (the target container lies inside the item).</summary>
    OwnContents,

    /// <summary>The item is already in the open container (the container window's own entries).</summary>
    AlreadyThere,

    /// <summary>The item is not one the player holds, so the target (a paperdoll slot, the world) cannot take it: it is in the open container or on the ground.</summary>
    NotHeld,

    /// <summary>The item does not fit the paperdoll slot, or is worn there already.</summary>
    DoesNotFit,

    /// <summary>The item cannot be held while in combat (<see cref="GameInventoryPage.HoldRefused"/>).</summary>
    InCombat,
}

/// <summary>
/// The rules of the drops (M4 2.2 s3, D4) the panels' drop targets share: what a dropped item turns into in the Action API (<see cref="ActionSource.Ui"/> always, never a packet), and the local refusals. <b>Retail's drop code has no address in the
/// docs</b> (<c>TODO(m4-retail-drag)</c>); the behaviour is the owner's: onto a pack or a side pack the item goes into it (<see cref="IActionApi.PickUp"/> for an item on the ground or in the open container, <see cref="IActionApi.MoveToPack"/> for
/// one the player holds), onto the container window it goes into the open container, onto a paperdoll slot it is wielded there (<see cref="IActionApi.Wield"/>) and onto the world view a held item is dropped (<see cref="IActionApi.Drop"/>).
/// </summary>
public static class GameDrops
{
    /// <summary>The player's guid (the main pack's container).</summary>
    public static uint PlayerGuid(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var self = session.State.Self;
        return self.Inventory.Owner != 0 ? self.Inventory.Owner : self.Guid;
    }

    /// <summary>Whether <paramref name="item"/> may be put into <paramref name="container"/>: not into itself or its own contents (OpenAC #221's neighbour: a pack move moves that pack), not into the container it is already listed in on the ground.</summary>
    public static DropVerdict CheckMove(IGameSessionView session, uint item, uint container)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (item == 0 || container == 0)
        {
            return DropVerdict.NoTarget;
        }

        if (item == container)
        {
            return DropVerdict.Self;
        }

        var state = session.State;
        var inventory = state.Self.Inventory;
        var place = inventory.Find(container);
        if (place.Kind == InventoryPlaceKind.InSidePack && place.Container == item)
        {
            return DropVerdict.OwnContents;
        }

        // Only the open ground container can list an item the player does not hold (an item in a side pack is held). ContainerViews.TryGet builds a snapshot for a side pack on every call: asking it here, on every pointer move
        // while an item is carried over a side pack's cell, allocated a view and its entries each time (M4 2.2 fix R8).
        if (state.Containers.Viewed is { } view && view.Guid == container && !inventory.Find(item).Held)
        {
            var entries = view.Entries;
            for (var i = 0; i < entries.Count; i++) // an index loop: this runs on every pointer move while an item is carried (M4 2.2 fix R8: no boxed enumerator)
            {
                if (entries[i].Guid == item)
                {
                    return DropVerdict.AlreadyThere;
                }
            }
        }

        return DropVerdict.Ok;
    }

    /// <summary>
    /// Sends the move: <see cref="IActionApi.MoveToPack"/> for an item the player holds (a pack from the side-pack column moves that pack alone), <see cref="IActionApi.PickUp"/> for one on the ground or in the open container.
    /// The slot is <paramref name="placement"/> (<c>TODO(m4-drag-placement)</c>: a drop on an empty part of a list goes to the first slot).
    /// </summary>
    public static ActionHandle Move(IGameSessionView session, uint item, uint container, int placement)
    {
        ArgumentNullException.ThrowIfNull(session);
        placement = Math.Max(0, placement);
        return session.State.Self.Inventory.Find(item).Held
            ? session.Actions.MoveToPack(item, container, (uint)placement, ActionSource.Ui)
            : session.Actions.PickUp(item, container, placement, ActionSource.Ui);
    }

    /// <summary>The sentence the log carries for a refusal.</summary>
    public static string Describe(DropVerdict verdict) => verdict switch
    {
        DropVerdict.NoTarget => "there is no container under the pointer",
        DropVerdict.Self => "a pack cannot be put in itself",
        DropVerdict.OwnContents => "a pack cannot be put in its own contents",
        DropVerdict.AlreadyThere => "the item is already in that container",
        DropVerdict.NotHeld => "the item is not carried; pick it up first",
        DropVerdict.DoesNotFit => "the item does not fit that slot, or is worn there already",
        DropVerdict.InCombat => "the item cannot be held in combat",
        _ => "refused",
    };

    /// <summary>The index of <paramref name="cell"/> among the list's cells, or 0 for none.</summary>
    public static int CellIndex(UiItemList list, UiItem? cell)
    {
        if (cell is null)
        {
            return 0;
        }

        for (var i = 0; i < list.CellCount; i++)
        {
            if (ReferenceEquals(list.CellAt(i), cell))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The mask a paperdoll drop wields at. A ring or bracelet slot asks for its own side when that side is free, else for the free side (<see cref="WieldSlotChoice"/>, as the inventory page's double click does), and when both
    /// sides are worn for the slot dropped on (the server swaps it). Any other slot sends the item's own valid locations whole, as the double click does (ACE stores the mask as given; a coat covers several slots), which includes
    /// the slot's bits. Zero when the item does not fit the slot.
    /// </summary>
    public static uint WieldMask(PlayerInventory inventory, uint slotMask, uint valid)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if ((valid & slotMask) == 0)
        {
            return 0;
        }

        const uint pairs = (uint)(EquipMask.WristWearLeft | EquipMask.WristWearRight | EquipMask.FingerWearLeft | EquipMask.FingerWearRight);
        if ((slotMask & pairs) == 0)
        {
            return valid;
        }

        if (inventory.EquippedAt(slotMask) is null)
        {
            return slotMask;
        }

        var choice = WieldSlotChoice.Choose(inventory, valid);
        return inventory.EquippedAt(choice & pairs) is null ? choice & pairs : slotMask;
    }
}

/// <summary>
/// A target that puts the dragged item into a container: the inventory page's pack list (the container it shows), the player's own cell (the main pack), a side pack's cell (that pack), the container window's item row and its
/// own cell (the open container). The Action API call is <see cref="GameDrops.Move"/>; a pack dropped on itself or its own contents is refused with a log line, and nothing else is.
/// </summary>
public sealed class GameContainerDrop : IUiDropTarget
{
    private readonly UiLoginContext _context;
    private readonly Func<UiItem?, uint> _containerFor;
    private readonly Func<UiItem?, int>? _placementFor;

    /// <summary>A target whose container is <paramref name="containerFor"/> (given the cell under the pointer; 0 for none).</summary>
    /// <param name="context">The session the drop acts on.</param>
    /// <param name="containerFor">The container guid for the cell under the pointer, or 0.</param>
    /// <param name="highlightsCell">True when the answer shows on the cell under the pointer.</param>
    /// <param name="placementFor">The slot to put the item in, or null for the first.</param>
    public GameContainerDrop(UiLoginContext context, Func<UiItem?, uint> containerFor, bool highlightsCell, Func<UiItem?, int>? placementFor = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(containerFor);
        _context = context;
        _containerFor = containerFor;
        HighlightsCell = highlightsCell;
        _placementFor = placementFor;
    }

    /// <inheritdoc/>
    public bool HighlightsCell { get; }

    /// <summary>The last move a drop sent, or null.</summary>
    public ActionHandle? LastAction { get; private set; }

    /// <summary>How many drops this target sent.</summary>
    public int Drops { get; private set; }

    /// <inheritdoc/>
    public bool Accepts(in UiDragPayload drag, UiItem? cell) => GameDrops.CheckMove(_context.Session, drag.ItemId, _containerFor(cell)) == DropVerdict.Ok;

    /// <inheritdoc/>
    public string? Refusal(in UiDragPayload drag, UiItem? cell) => GameDrops.Describe(GameDrops.CheckMove(_context.Session, drag.ItemId, _containerFor(cell)));

    /// <inheritdoc/>
    public void Drop(in UiDragPayload drag, UiItem? cell)
    {
        var container = _containerFor(cell);
        var placement = _placementFor?.Invoke(cell) ?? 0;
        if (_placementFor is null || cell is null)
        {
            // The slot is a guess: a drop on an empty part of a list, or on a target with no slots of its own, goes to the first one (retail's placement rule has no address in the docs).
            _context.Manager.LogOnce("drag:placement", LogSeverity.Warning, "TODO(m4-drag-placement): a drop that names no slot (an empty part of a list, a side pack, the container window) is sent with placement 0; retail's placement rule has no address in the docs");
        }

        Drops++;
        LastAction = GameDrops.Move(_context.Session, drag.ItemId, container, placement);
        _context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"drag: item 0x{drag.ItemId:X8} dropped into container 0x{container:X8}: {LastAction.Summary}"));
    }
}

/// <summary>The world view as a drop target: a held item is dropped to the ground (<see cref="IActionApi.Drop"/>); an item that is not held is already in the world (or in a container) and is refused.</summary>
public sealed class GameWorldDrop(UiLoginContext context) : IUiDropTarget
{
    /// <inheritdoc/>
    public bool HighlightsCell => false;

    /// <summary>The last drop sent, or null.</summary>
    public ActionHandle? LastAction { get; private set; }

    /// <summary>How many drops this target sent.</summary>
    public int Drops { get; private set; }

    /// <inheritdoc/>
    public bool Accepts(in UiDragPayload drag, UiItem? cell) => drag.ItemId != 0 && context.Session.State.Self.Inventory.Find(drag.ItemId).Held;

    /// <inheritdoc/>
    public string? Refusal(in UiDragPayload drag, UiItem? cell) => GameDrops.Describe(DropVerdict.NotHeld);

    /// <inheritdoc/>
    public void Drop(in UiDragPayload drag, UiItem? cell)
    {
        Drops++;
        LastAction = context.Session.Actions.Drop(drag.ItemId, ActionSource.Ui);
        context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"drag: item 0x{drag.ItemId:X8} dropped on the world view: {LastAction.Summary}"));
    }
}
