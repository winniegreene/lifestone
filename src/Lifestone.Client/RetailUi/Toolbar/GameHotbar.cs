using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Toolbar;

/// <summary>
/// The shortcut bar of the toolbar (M4 3.2 h2, D24 (1)): the 18 one-cell item lists of <c>gmToolbarUI</c> (class 0x10000031; <c>InitShortcutArray</c>, 11.6096 0x004BE9E0) drawn from <see cref="SelfState.Shortcuts"/>. The first nine, the lists 0x100001A7 to
/// 0x100001AF in the layout's top row, are the bar the player sees in the lower right of the screen; the second nine, 0x100006B7 to 0x100006BF, lie below the toolbar window's 100 pixels and are clipped away (the owner resizes the floaty in retail to see them), so
/// the number keys 1 to 9 use slots 0 to 8 (shown as 1 to 9; Ctrl with a digit is the same action) and Alt with 1 to 9 uses slots 9 to 17 (the keymap's actions 0x1000004B to 0x1000004D and 0x10000132 to 0x10000137).
/// </summary>
/// <remarks>
/// <para>
/// <b>The slots.</b> Each list shows its slot's object as a cell (<see cref="GameInventoryPage.IconOf"/>: the icon, the type background, the stack count), ghosted with the ghosted number when the player does not hold the object (<see cref="ShortcutBar.IsMissing"/>),
/// and the layout's empty-slot art when the slot is empty (the cell of an empty list is the empty cell of <c>UIElement_ItemList</c>, and each list's own template bakes its slot's number into that art as a large numeral); a cell with an item shows its slot's number in a corner box
/// (<see cref="UiItem.SetShortcutNumber"/>, the dats' own digit art). A spell-only row is not drawn
/// (<c>TODO(m4-shortcut-spell)</c>). The bar redraws on <see cref="ShortcutsChanged"/>, <see cref="InventoryChanged"/> (the holdings, a stack count, a missing object coming back) and the create or update of an object it names. An object the world table does not know yet
/// (a missing row at login) is drawn with a blank icon (<c>TODO(m4-shortcut-missing-icon)</c>, logged once).
/// </para>
/// <para>
/// <b>Use.</b> A number key (input map 0x1000000C, which <c>ClientUISystem::OnBeginCharacterSession</c>, 11.6096 table, registers at priority 1000 with the keymap dat's actions 0x10000042 to 0x1000004D and 0x10000132 to 0x10000137; <c>gmToolbarUI::ListenToGlobalMessage</c>
/// 0x004BF170 turns the action into <c>UseShortcut</c> 0x004BDFE0 / <c>RecvNotice_UseShortcut</c> 0x004BE5A0) and a left click on a slot call <see cref="IActionApi.UseShortcut"/>. A focused chat entry registers the keyboard stop map, so the key never gets here
/// while it has the focus. The select variants of the actions (0x1000004E on, <c>UseShortcut(slot, false)</c>: select the object) are not built (<c>TODO(m4-shortcut-select-keys)</c>); the key 0, action 0x1000010D, makes a shortcut to the selected object
/// (<c>CreateShortcutToItem</c> 0x004BE750 with slot -1).
/// </para>
/// <para>
/// <b>The cooldown overlay.</b> While the action a use started is not complete the slot shows the last cooldown step (<see cref="UiItem.SetCooldown"/>, the dats' art), cleared when it completes. Retail drives the steps from the item's cooldown in the enchantment
/// registry, which the client does not track for items; only a use started from this bar shows it (<c>TODO(m4-shortcut-cooldown-source)</c>: the hunt loop's own drink does not).
/// </para>
/// <para>
/// <b>Drag.</b> Every slot list is a drag source and a drop target. An item from a pack onto a slot adds it (<c>HandleDropRelease</c> 0x004BF450, <c>CreateShortcutToItem</c>: <see cref="IActionApi.AddShortcut"/>, which moves an item that is already on the bar and puts an occupant
/// to the right); a slot dragged onto another slot moves it, an occupied target swapping with the dragged slot (retail's drag-begin removes the dragged shortcut first, <c>RecvNotice_ItemListBeginDrag</c> 0x004BE5C0, and the drop gives the displaced one the freed slot); a slot
/// dragged anywhere else (the world, a pack, a panel) is removed (<see cref="IActionApi.RemoveShortcut"/>) and the item it names stays where it is (<see cref="IUiDragSourceOwner"/>). Retail removes the shortcut when the drag begins, so even a drag that ends nowhere loses it; here an Escape
/// or a lost focus keeps it. An object the player does not hold cannot be dropped on a slot (retail picks it up first: <c>TODO(m4-shortcut-pickup)</c>).
/// </para>
/// </remarks>
public sealed class GameHotbar : IDisposable, IUiDragSourceOwner
{
    /// <summary>The number of slots.</summary>
    public const int SlotCount = ShortcutBar.SlotCount;

    /// <summary>The slots of the bar that shows (the toolbar window's top row); the other nine are clipped.</summary>
    public const int VisibleSlots = 9;

    /// <summary>The input map of the gameplay shortcut keys (registered by <c>ClientUISystem::OnBeginCharacterSession</c>).</summary>
    public const uint InputMap = 0x1000000C;

    /// <summary>The priority <c>ClientUISystem::OnBeginCharacterSession</c> registers the map at.</summary>
    public const int InputMapPriority = 1000;

    /// <summary>The action of slot 0 (key 1); slots 0 to 11 are the actions 0x10000042 to 0x1000004D.</summary>
    public const uint FirstUseAction = 0x10000042;

    /// <summary>The action of slot 12 (0x10000132); slots 12 to 17 are 0x10000132 to 0x10000137.</summary>
    public const uint FirstHighUseAction = 0x10000132;

    /// <summary>The action that makes a shortcut to the selected object (key 0).</summary>
    public const uint CreateShortcutAction = 0x1000010D;

    private static readonly uint[] ListIds =
    [
        0x100001A7, 0x100001A8, 0x100001A9, 0x100001AA, 0x100001AB, 0x100001AC, 0x100001AD, 0x100001AE, 0x100001AF,
        0x100006B7, 0x100006B8, 0x100006B9, 0x100006BA, 0x100006BB, 0x100006BC, 0x100006BD, 0x100006BE, 0x100006BF,
    ];

    /// <summary>
    /// The item type of money (ACE <c>ItemType.Money</c>): the UI's stand-in for <c>ACCWeenieObject::IsCoinstack</c> (11.6096 0x004BDEE0), which the action tests by weenie class (the retail UI reads no weenie class: <c>TODO(m4-shortcut-coin-test)</c>). The action's own
    /// refusal is the one that counts; this only keeps the slot from lighting up for a stack of pyreals.
    /// </summary>
    private const uint MoneyItemType = 0x40;

    private readonly UiLoginContext _context;
    private readonly UiElement _window;
    private readonly bool[] _watched = new bool[SlotCount];
    private readonly UiItemList?[] _lists = new UiItemList?[SlotCount];
    private readonly SlotDrop?[] _drops = new SlotDrop?[SlotCount];
    private readonly ActionHandle?[] _busy = new ActionHandle?[SlotCount];
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<ActionHandle> _recent = [];
    private readonly KeySink _keys;
    private bool _disposed;
    private bool _loggedBlankIcon;
    private bool _loggedSpell;

    /// <summary>The bar over the slot lists of <paramref name="window"/> (the toolbar window); <paramref name="selectedSource"/> reads the selected object for the create-shortcut key.</summary>
    public GameHotbar(UiLoginContext context, UiElement window, Func<uint>? selectedSource = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(window);
        _context = context;
        _window = window;
        SelectedSource = selectedSource;
        var drag = context.Manager.Input.Drag;
        for (var i = 0; i < SlotCount; i++)
        {
            if (window.GetChildRecursive(ListIds[i]) is not UiItemList list)
            {
                context.Manager.Services.Log.Error(UiElementManager.Category, $"gmToolbarUI: the toolbar has no shortcut list 0x{ListIds[i]:X8} (slot {i}); that slot is not drawn (TODO(m4-shortcut-list-missing))");
                continue;
            }

            _lists[i] = list;
            list.DragSource = true;
            _drops[i] = new SlotDrop(this, i);
            drag.RegisterTarget(list, _drops[i]!);
            drag.RegisterSourceOwner(list, this);
        }

        var events = context.Session.Events;
        _subscriptions.Add(events.Subscribe<ShortcutsChanged>(OnShortcutsChanged));
        _subscriptions.Add(events.Subscribe<InventoryChanged>(OnInventoryChanged));
        _subscriptions.Add(events.Subscribe<ObjectCreated>(OnObjectCreated));
        _subscriptions.Add(events.Subscribe<ObjectUpdated>(OnObjectUpdated));
        _keys = new KeySink(this);
        context.Manager.Input.RegisterInputMap(InputMap, _keys, InputMapPriority);
        Refresh();
    }

    /// <summary>Where the create-shortcut key reads the selected object from (0 for none).</summary>
    public Func<uint>? SelectedSource { get; set; }

    /// <summary>How many uses a key or a click asked for (an empty slot counts: the Action API refuses it).</summary>
    public int Uses { get; private set; }

    /// <summary>The handle of the last use, or null.</summary>
    public ActionHandle? LastUse { get; private set; }

    /// <summary>The handle of the last add or remove a drop or the create key sent, or null.</summary>
    public ActionHandle? LastAction { get; private set; }

    /// <summary>The sends of adds and removes so far (a drop that swaps counts each).</summary>
    public int Sends { get; private set; }

    /// <summary>The last <see cref="RecentCapacity"/> adds and removes a drop or the create key sent, oldest first (a swap sends four: the order is retail's).</summary>
    public IReadOnlyList<ActionHandle> RecentSends => _recent;

    /// <summary>How many sends <see cref="RecentSends"/> keeps.</summary>
    public const int RecentCapacity = 32;

    /// <summary>The slot list of <paramref name="slot"/> (0 to 17), or null when the layout lacks it.</summary>
    public UiItemList? ListOf(int slot) => slot is >= 0 and < SlotCount ? _lists[slot] : null;

    /// <summary>The cell of <paramref name="slot"/>, or null.</summary>
    public UiItem? CellOf(int slot) => ListOf(slot)?.CellAt(0);

    /// <summary>The slot whose list is <paramref name="list"/>, or -1.</summary>
    public int SlotOfList(UiItemList? list)
    {
        if (list is null)
        {
            return -1;
        }

        for (var i = 0; i < SlotCount; i++)
        {
            if (ReferenceEquals(_lists[i], list))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The drop target of <paramref name="slot"/> (for a test), or null.</summary>
    public IUiDropTarget? DropOf(int slot) => slot is >= 0 and < SlotCount ? _drops[slot] : null;

    /// <summary>The slot an input action uses (0 to 17), or -1.</summary>
    public static int SlotOfAction(uint action) => action switch
    {
        >= FirstUseAction and <= FirstUseAction + 11 => (int)(action - FirstUseAction),
        >= FirstHighUseAction and <= FirstHighUseAction + 5 => 12 + (int)(action - FirstHighUseAction),
        _ => -1,
    };

    /// <summary>True while a use started from this bar is not complete for <paramref name="slot"/>.</summary>
    public bool IsBusy(int slot) => slot is >= 0 and < SlotCount && _busy[slot] is { IsCompleted: false };

    /// <summary>Redraws every slot from the model.</summary>
    public void Refresh()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            RefreshSlot(i);
        }
    }

    private void RefreshSlot(int slot)
    {
        if (_lists[slot] is not { } list)
        {
            return;
        }

        list.EnsureCells(1);
        if (list.CellAt(0) is not { } cell)
        {
            return;
        }

        cell.SetSelectable(false); // a hotbar click uses the item; the selected frame is the pack's
        if (!_watched[slot])
        {
            // The screen does not dispose the toolbar: the bar ends with its cells (the window is deleted before they are).
            _watched[slot] = true;
            cell.Deleting += OnCellDeleting;
        }

        var bar = _context.Session.State.Self.Shortcuts;
        var row = bar[slot];
        if (!row.IsItem)
        {
            if (row.IsSpell && !_loggedSpell)
            {
                _loggedSpell = true;
                _context.Manager.Services.Log.Warn("retailui", $"TODO(m4-shortcut-spell): shortcut slot {slot} holds a spell, which the hotbar does not draw or use");
            }

            if (!cell.IsEmpty)
            {
                cell.Clear();
            }

            cell.SetGhosted(false);
            cell.SetCooldown(0);
            cell.SetShortcutNumber(slot, false);
            return;
        }

        var missing = bar.IsMissing(slot);
        UiItemIcon icon;
        if (_context.Session.State.Objects.TryGet(row.ObjectId, out var obj))
        {
            icon = GameInventoryPage.IconOf(obj);
        }
        else
        {
            icon = default;
            if (!_loggedBlankIcon)
            {
                _loggedBlankIcon = true;
                _context.Manager.Services.Log.Warn("retailui", $"TODO(m4-shortcut-missing-icon): shortcut slot {slot} names object 0x{row.ObjectId:X8}, which the world table does not know; the slot is drawn ghosted with a blank icon");
            }
        }

        cell.ShowItem(row.ObjectId, icon);
        cell.SetGhosted(missing);
        cell.SetShortcutNumber(slot, missing);
        cell.SetCooldown(IsBusy(slot) ? UiItem.CooldownSteps : 0);
    }

    private void OnShortcutsChanged(in ShortcutsChanged change)
    {
        if (CheckAlive())
        {
            Refresh();
        }
    }

    private void OnInventoryChanged(in InventoryChanged change)
    {
        if (CheckAlive())
        {
            Refresh();
        }
    }

    private void OnObjectCreated(in ObjectCreated created)
    {
        if (CheckAlive() && _context.Session.State.Self.Shortcuts.SlotOf(created.Guid) is var slot and >= 0)
        {
            RefreshSlot(slot);
        }
    }

    private void OnObjectUpdated(in ObjectUpdated updated)
    {
        if (CheckAlive() && _context.Session.State.Self.Shortcuts.SlotOf(updated.Guid) is var slot and >= 0)
        {
            RefreshSlot(slot);
        }
    }

    private void OnCellDeleting(UiItem cell)
    {
        if (_window.IsDeleted)
        {
            Dispose();
        }
    }

    // The session outlives the screen: a bar whose window is gone ends itself at the first event it hears (and at once when its cells are deleted).
    private bool CheckAlive()
    {
        if (_disposed)
        {
            return false;
        }

        if (_window.IsDeleted)
        {
            Dispose();
            return false;
        }

        return true;
    }

    /// <summary>
    /// <c>gmToolbarUI::UseShortcut</c> (11.6096 0x004BDFE0) from <c>RecvNotice_UseShortcut</c> (0x004BE5A0): uses the object on <paramref name="slot"/> through <see cref="IActionApi.UseShortcut"/>; the slot shows the cooldown while the action is not complete.
    /// An empty slot or a missing object gets the Action API's refusal (a handle, logged). Returns the handle.
    /// </summary>
    public ActionHandle UseSlot(int slot)
    {
        Uses++;
        var handle = _context.Session.Actions.UseShortcut(slot, ActionSource.Ui);
        LastUse = handle;
        _context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"hotbar: slot {slot + 1} used: {handle.Summary}"));
        TrackBusy(slot, handle);
        return handle;
    }

    /// <summary>The slot shows the cooldown overlay until <paramref name="handle"/> completes (nothing for a handle that has).</summary>
    internal void TrackBusy(int slot, ActionHandle handle)
    {
        if (slot is < 0 or >= SlotCount || handle.IsCompleted)
        {
            return;
        }

        _busy[slot] = handle;
        handle.Completed += done => OnUseCompleted(slot, done);
        if (handle.IsCompleted)
        {
            OnUseCompleted(slot, handle); // completed between the check and the subscription
        }
        else
        {
            RefreshSlot(slot);
        }
    }

    private void OnUseCompleted(int slot, ActionHandle done)
    {
        if (_disposed || !ReferenceEquals(_busy[slot], done))
        {
            return;
        }

        _busy[slot] = null;
        if (!_window.IsDeleted)
        {
            RefreshSlot(slot);
        }
    }

    /// <summary>A left click on a cell of a slot list uses the slot; true when <paramref name="element"/> is such a cell.</summary>
    public bool HandleClick(UiElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e is UiItemList list)
            {
                var slot = SlotOfList(list);
                if (slot < 0)
                {
                    return false;
                }

                if (element is UiItem { IsEmpty: false })
                {
                    UseSlot(slot);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>The key 0 (action 0x1000010D): a shortcut to the selected object on the first empty slot (<c>CreateShortcutToItem</c> with slot -1).</summary>
    public ActionHandle? CreateForSelection()
    {
        var selected = SelectedSource?.Invoke() ?? 0;
        if (selected == 0)
        {
            return null;
        }

        return Send(_context.Session.Actions.AddShortcut(ShortcutBar.AutoSlot, selected, ActionSource.Ui));
    }

    private ActionHandle Send(ActionHandle handle)
    {
        Sends++;
        LastAction = handle;
        if (_recent.Count == RecentCapacity)
        {
            _recent.RemoveAt(0);
        }

        _recent.Add(handle);
        _context.Manager.Services.Log.Info("retailui", "hotbar: " + handle.Summary);
        return handle;
    }

    /// <inheritdoc/>
    public bool OwnsTarget(IUiDropTarget? target) => target is SlotDrop slot && ReferenceEquals(slot.Bar, this);

    /// <inheritdoc/>
    public void DragReleasedAway(in UiDragPayload drag, IUiDropTarget? target)
    {
        var from = SlotOfList(drag.SourceList);
        if (from < 0 || !_context.Session.State.Self.Shortcuts[from].IsItem)
        {
            return;
        }

        _context.Manager.Services.Log.Info("retailui", string.Create(CultureInfo.InvariantCulture, $"hotbar: shortcut {from + 1} dragged off the bar; it is removed (the item stays where it is)"));
        Send(_context.Session.Actions.RemoveShortcut(from, ActionSource.Ui));
    }

    /// <summary>
    /// <c>gmToolbarUI::HandleDropRelease</c> (11.6096 0x004BF450) on slot <paramref name="slot"/>: a pack item is added; a slot dragged from another slot moves, swapping with an occupant (the occupant takes the freed slot).
    /// </summary>
    private void DropOn(int slot, in UiDragPayload drag)
    {
        var bar = _context.Session.State.Self.Shortcuts;
        var actions = _context.Session.Actions;
        var from = SlotOfList(drag.SourceList);
        if (from == slot)
        {
            return;
        }

        var occupant = bar[slot];
        if (from >= 0 && occupant.IsItem)
        {
            // R(from), R(slot), A(slot, item), A(from, occupant): the swap, in the order retail's calls make
            Send(actions.RemoveShortcut(from, ActionSource.Ui));
            Send(actions.RemoveShortcut(slot, ActionSource.Ui));
            Send(actions.AddShortcut(slot, drag.ItemId, ActionSource.Ui));
            Send(actions.AddShortcut(from, occupant.ObjectId, ActionSource.Ui));
            return;
        }

        Send(actions.AddShortcut(slot, drag.ItemId, ActionSource.Ui));
    }

    private bool CanDrop(in UiDragPayload drag)
    {
        var from = SlotOfList(drag.SourceList);
        if (from >= 0)
        {
            // A ghosted (missing) shortcut cannot move or swap: the add of an item the player does not carry is refused by the Action API after the removes were sent, which would lose both shortcuts (3.2-R18).
            return !_context.Session.State.Self.Shortcuts.IsMissing(from);
        }

        var state = _context.Session.State;
        if (drag.ItemId == 0 || !state.Self.Inventory.Contains(drag.ItemId))
        {
            return false;
        }

        return !(state.Objects.TryGet(drag.ItemId, out var obj) && (obj.ItemType & MoneyItemType) != 0);
    }

    private string? RefusalOf(in UiDragPayload drag)
    {
        var state = _context.Session.State;
        if (SlotOfList(drag.SourceList) is var from and >= 0 && state.Self.Shortcuts.IsMissing(from))
        {
            return "a shortcut whose item the player does not carry cannot be moved (TODO(m4-shortcut-pickup): retail picks the item up first)";
        }

        if (drag.ItemId == 0 || !state.Self.Inventory.Contains(drag.ItemId))
        {
            return "only an item the player carries can have a shortcut (TODO(m4-shortcut-pickup): retail picks it up first)";
        }

        return "a stack of pyreals cannot have a shortcut (ACCWeenieObject::IsCoinstack)";
    }

    /// <summary>Ends the bar: the subscriptions, the key map, the drag registrations. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        var input = _context.Manager.Input;
        input.UnregisterInputMap(InputMap, _keys);
        var drag = input.Drag;
        for (var i = 0; i < SlotCount; i++)
        {
            if (_lists[i] is { } list)
            {
                drag.UnregisterTarget(list);
                drag.UnregisterSourceOwner(list);
                list.DragSource = false;
            }
        }
    }

    private sealed class SlotDrop(GameHotbar bar, int slot) : IUiDropTarget
    {
        public GameHotbar Bar => bar;

        public bool HighlightsCell => true;

        public bool Accepts(in UiDragPayload drag, UiItem? cell) => bar.CanDrop(drag);

        public string? Refusal(in UiDragPayload drag, UiItem? cell) => bar.RefusalOf(drag);

        public void Drop(in UiDragPayload drag, UiItem? cell) => bar.DropOn(slot, drag);
    }

    // The map's registrant: a number key uses its slot. A key that is not ours, and the release of one, passes on.
    private sealed class KeySink(GameHotbar bar) : IInputActionSink
    {
        public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
        {
            if (inputMapId != InputMap || bar._disposed)
            {
                return false;
            }

            if (actionId == CreateShortcutAction)
            {
                if (activation.Start)
                {
                    bar.CreateForSelection();
                }

                return true;
            }

            var slot = SlotOfAction(actionId);
            if (slot < 0)
            {
                return false;
            }

            if (activation.Start)
            {
                bar.UseSlot(slot);
            }

            return true;
        }
    }
}
