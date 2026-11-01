using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi;

/// <summary>Where a drag is: nothing, a press that may become a drag, or an item being carried.</summary>
public enum UiDragPhase
{
    /// <summary>No left button is down on a draggable item.</summary>
    Idle,

    /// <summary>The left button went down on a draggable item and the pointer has not moved far enough yet: a click, so far.</summary>
    Pressed,

    /// <summary>The pointer moved <see cref="UiDrag.StartDistancePixels"/> or more with the button held: the item is being carried.</summary>
    Dragging,
}

/// <summary>How the last drag ended.</summary>
public enum UiDragEnd
{
    /// <summary>No drag has ended yet.</summary>
    None,

    /// <summary>Released on a target that took the item (<see cref="IUiDropTarget.Drop"/> ran).</summary>
    Dropped,

    /// <summary>Released on a target that refused the item: nothing was sent.</summary>
    Refused,

    /// <summary>Released on a non-target or on the source cell, or ended by Escape, the focus or a deleted element: nothing was sent.</summary>
    Cancelled,
}

/// <summary>The item a drag carries: the item the press found, fixed when the button went down.</summary>
/// <param name="ItemId">The item's guid, read from the pressed cell at mouse down (never from what is under the pointer later).</param>
/// <param name="Source">The cell that was pressed.</param>
/// <param name="SourceList">The list the cell belongs to.</param>
/// <param name="Stack">True when the cell shows a stack of more than one (it is dragged whole).</param>
/// <param name="SpellId">The spell the pressed cell shows (a spellbook or spell-bar cell, M5 6 s1), read at mouse down as <paramref name="ItemId"/> is; 0 for an item. A spell drag has no item (<paramref name="ItemId"/> is 0), so every item target refuses it.</param>
public readonly record struct UiDragPayload(uint ItemId, UiItem Source, UiItemList? SourceList, bool Stack, uint SpellId = 0);

/// <summary>An element that takes a dragged item (registered with <see cref="UiDrag.RegisterTarget"/>).</summary>
public interface IUiDropTarget
{
    /// <summary>True when the target shows its answer on the item cell under the pointer (a pack, a side pack, a container's own cell, a paperdoll slot); false when it answers for the whole element (a list, the world).</summary>
    bool HighlightsCell { get; }

    /// <summary>
    /// Whether <paramref name="drag"/> may be dropped here, <paramref name="cell"/> being the item cell under the pointer inside the target element (or null). Called on every pointer move while the item is over the target:
    /// it must not allocate and must not log.
    /// </summary>
    bool Accepts(in UiDragPayload drag, UiItem? cell);

    /// <summary>Why <see cref="Accepts"/> says no, in a sentence for the log (called once, when the item is released on the target and refused); null for no reason to give.</summary>
    string? Refusal(in UiDragPayload drag, UiItem? cell);

    /// <summary>The item was released here and <see cref="Accepts"/> said yes: the target acts, through the Action API only.</summary>
    void Drop(in UiDragPayload drag, UiItem? cell);
}

/// <summary>
/// The owner of a drag source whose cells are shortcuts, not items (the hotbar's slots, M4 3.2 h2): a drag from such a list is not the item's move. It is dropped on a target of the owner (another slot) as any drag is; a release anywhere
/// else (the world, a pack, a panel's empty area) goes to <see cref="DragReleasedAway"/> instead of to the target, so a shortcut dragged off the bar cannot drop the item it names on the ground
/// (retail: <c>gmToolbarUI::RecvNotice_ItemListBeginDrag</c> 0x004BE5C0 removes the shortcut when the drag begins; <c>HandleDropRelease</c> 0x004BF450 puts it back only on a slot).
/// </summary>
public interface IUiDragSourceOwner
{
    /// <summary>True when <paramref name="target"/> is one of the owner's own targets (the release is then an ordinary drop on it); false for any other target or none.</summary>
    bool OwnsTarget(IUiDropTarget? target);

    /// <summary>The drag was released away from the owner's targets (<paramref name="target"/> is what was under the pointer, or null): the owner acts, through the Action API only.</summary>
    void DragReleasedAway(in UiDragPayload drag, IUiDropTarget? target);
}

/// <summary>
/// Drag and drop of item cells in the UI runtime (M4 2.2 s3, D4): <b>retail's functions have no address in the docs (address unknown, <c>TODO(m4-retail-drag)</c>)</b>. <c>UIElementManager::StartDragandDrop</c> is the only name the
/// runtime knows (the input router's old TODO) and <c>UIElement_Field::MouseOverTop</c> and <c>CatchDroppedItem</c> (11.6096 0x00472720, 0x00472810) show the drop-target half; the rest is re-expressed from the layouts and from
/// the behaviour the owner described.
/// </summary>
/// <remarks>
/// <para>
/// <b>The state machine.</b> A press (left button down) on a non-empty <see cref="UiItem"/> (an item, or a spell of the spellbook or a spell bar, M5 6 s1: its payload carries <see cref="UiDragPayload.SpellId"/> and no item, so every item target refuses it) whose list is a <see cref="UiItemList.DragSource"/> arms the drag (<see cref="UiDragPhase.Pressed"/>) and fixes <see cref="ItemId"/>
/// from the pressed cell right then (OpenAC #206: "a quick press-and-drag picked up the item that was pressed": a pointer that has already moved on to another cell by the first move event still carries the pressed item). The drag
/// begins (<see cref="UiDragPhase.Dragging"/>) when the pointer has moved <see cref="StartDistancePixels"/> from the press with the button held (retail's distance is unknown: <c>TODO(m4-retail-drag)</c>); the source cell lets go of the
/// mouse capture so the elements under the pointer hear it, an icon of the item follows the pointer (a top-layer root element made once, from the list's own cell template: no Godot node and no allocation per move) and
/// <see cref="UiInputRouter.DragElement"/> answers the pressed cell (which <see cref="UiField"/>'s drag-rollover reads). A release over a target that accepts the item calls <see cref="IUiDropTarget.Drop"/>; a release over a
/// non-target, on the source cell, Escape, a lost focus or a deleted element ends the drag and sends nothing. A press that is released before the distance is a plain click (the cell's selection and double click are unchanged).
/// </para>
/// <para>
/// <b>Targets</b> are elements registered with <see cref="RegisterTarget"/>; the pointer's element and its parents are searched for the first registered one, and the item cell met on the way up is handed to the target. A target that
/// <see cref="IUiDropTarget.HighlightsCell"/> shows its answer on that cell (<see cref="UiItem.ShowDragRollover"/>: the drag-accept frame and the field's rollover states), cleared when the pointer leaves or the drag ends (OpenAC #146).
/// A pointer over no element at all, or over the full-screen 3D viewport, goes to <see cref="NoElementTarget"/> (the world) only where <see cref="WorldPoint"/> says the point is the world; over a panel's empty area the release cancels (M4 2.2 fix R4). A stack is dragged whole: a shift-drag split is <c>TODO(m4-drag-stack-split)</c>, logged once.
/// </para>
/// </remarks>
public sealed class UiDrag
{
    /// <summary>How far the pointer moves from the press, in logical pixels, before the press becomes a drag (retail's value is unknown: <c>TODO(m4-retail-drag)</c>).</summary>
    public const int StartDistancePixels = 4;

    private readonly UiElementManager _manager;
    private readonly Dictionary<UiElement, IUiDropTarget> _targets = [];
    private readonly Dictionary<UiItemList, IUiDragSourceOwner> _sourceOwners = [];
    private UiDragPhase _phase;
    private UiItem? _source;
    private UiItemList? _sourceList;
    private uint _itemId;
    private uint _spellId;
    private bool _stack;
    private int _pressX;
    private int _pressY;
    private UiItem? _hoverCell;
    private UiItem? _icon;
    private bool _iconFailed;

    internal UiDrag(UiElementManager manager) => _manager = manager;

    /// <summary>Where the drag is now.</summary>
    public UiDragPhase Phase => _phase;

    /// <summary>The cell that was pressed, while a drag is armed or on; else null.</summary>
    public UiItem? Source => _source;

    /// <summary>The item the press found (fixed at mouse down), while a drag is armed or on; else 0.</summary>
    public uint ItemId => _itemId;

    /// <summary>The dragged cell while the item is being carried (what <see cref="UiInputRouter.DragElement"/> answers), else null.</summary>
    public UiElement? Element => _phase == UiDragPhase.Dragging ? _source : null;

    /// <summary>The item cell the pointer is over that shows the target's answer now, or null.</summary>
    public UiItem? HoverCell => _hoverCell;

    /// <summary>The icon element that follows the pointer (a root element of the UI), or null before the first drag. It is hidden between drags.</summary>
    public UiItem? Icon => _icon;

    /// <summary>The icon as the hit test must skip it (null before the first drag).</summary>
    internal UiElement? IconRoot => _icon;

    /// <summary>The spell being carried (a spell cell's drag, M5 6 s1), or 0.</summary>
    public uint SpellId => _spellId;

    /// <summary>
    /// The picture fields of a spell (the SpellTable's, set by the spell panels): a dragged spell's composed icon (<c>UIElement_ItemList::PrepareDragIcon</c>, 11.4186 0x004E2A50, over <c>ClientMagicSystem::GetSpellIcon</c>)
    /// follows the pointer. Null, or a spell it does not know, leaves a spell drag without an icon, logged once (<c>TODO(m5-spell-drag-icon)</c>).
    /// </summary>
    public Func<uint, UiSpellIcon?>? SpellIconOf { get; set; }

    /// <summary>Takes a release where no UI element is at all (the world view); null refuses it (the drag cancels).</summary>
    public IUiDropTarget? NoElementTarget { get; set; }

    /// <summary>
    /// M4 2.2 fix R4: says whether the screen point (x, y) is the world view and not a panel's area. When set, <see cref="NoElementTarget"/> takes a release only at such a point (whether the hit test found nothing or the full-screen 3D
    /// viewport, which a panel's non-mouse-visible fields let the point fall through to); a release anywhere else cancels and sends nothing, as retail returns an item no target takes to where it was. Null (the layout has no
    /// SmartBox viewport to compare with): every point that hits no element is the world, as before. Called on every pointer move while an item is carried: it must not allocate.
    /// </summary>
    public Func<int, int, bool>? WorldPoint { get; set; }

    /// <summary>How many drags began.</summary>
    public int Started { get; private set; }

    /// <summary>How many drags ended in a drop that was sent to a target.</summary>
    public int Dropped { get; private set; }

    /// <summary>How many drags ended on a target that refused the item.</summary>
    public int Refused { get; private set; }

    /// <summary>How many drags ended with nothing sent (a non-target, the source cell, Escape, focus lost, an element deleted).</summary>
    public int Cancelled { get; private set; }

    /// <summary>How the last drag ended.</summary>
    public UiDragEnd LastEnd { get; private set; }

    /// <summary>
    /// Makes <paramref name="element"/> take dragged items through <paramref name="target"/>. The element is made mouse-visible, so the hit test finds it over its empty area. A second registration of the element replaces the first.
    /// </summary>
    public void RegisterTarget(UiElement element, IUiDropTarget target)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(target);
        _targets[element] = target;
        if (!element.IsMouseVisible)
        {
            element.SetMouseVisible(true);
        }
    }

    /// <summary>Gives the drags that start from a cell of <paramref name="list"/> to <paramref name="owner"/> (see <see cref="IUiDragSourceOwner"/>). The list should be a <see cref="UiItemList.DragSource"/>.</summary>
    public void RegisterSourceOwner(UiItemList list, IUiDragSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(owner);
        _sourceOwners[list] = owner;
    }

    /// <summary>Ends <see cref="RegisterSourceOwner"/> for <paramref name="list"/>.</summary>
    public void UnregisterSourceOwner(UiItemList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _sourceOwners.Remove(list);
    }

    /// <summary>Stops <paramref name="element"/> taking dragged items.</summary>
    public void UnregisterTarget(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _targets.Remove(element);
    }

    /// <summary>True when <paramref name="element"/> is a registered target.</summary>
    public bool IsTarget(UiElement element) => _targets.ContainsKey(element);

    /// <summary>Ends a drag that is on (Escape, a lost focus): the icon and the highlight go, nothing is sent. A press that has not become a drag is forgotten too.</summary>
    public void Cancel(string reason)
    {
        if (_phase == UiDragPhase.Idle)
        {
            return;
        }

        var dragging = _phase == UiDragPhase.Dragging;
        EndDrag();
        if (dragging)
        {
            Cancelled++;
            LastEnd = UiDragEnd.Cancelled;
            _manager.Services.Log.Info("retailui", $"drag cancelled ({reason}); nothing was sent");
        }
    }

    /// <summary>
    /// The left button went down over <paramref name="over"/> at (<paramref name="x"/>, <paramref name="y"/>): arms a drag when it is a non-empty item cell of a <see cref="UiItemList.DragSource"/> list, taking the item from
    /// the cell now. True when armed.
    /// </summary>
    internal bool Press(UiElement? over, int x, int y)
    {
        if (_phase != UiDragPhase.Idle)
        {
            EndDrag(); // a press while one is armed (a lost release): start afresh
        }


        if (over is not UiItem { IsEmpty: false } cell || ListOf(cell) is not { DragSource: true } list)
        {
            return false;
        }

        _source = cell;
        _sourceList = list;
        _itemId = cell.ItemId;
        _spellId = cell.ItemId == 0 ? cell.SpellId : 0; // a spell cell (the spellbook, a spell bar) carries its spell; an item cell its item
        _stack = cell.Quantity > 1;
        _pressX = x;
        _pressY = y;
        _phase = UiDragPhase.Pressed;
        return true;
    }

    /// <summary>
    /// The pointer moved to (<paramref name="x"/>, <paramref name="y"/>): a press that moved far enough becomes a drag, and the icon of a drag that is on follows. True when the drag began in this call (the router then lets the
    /// source cell give up the mouse capture).
    /// </summary>
    internal bool MovedTo(int x, int y)
    {
        if (_phase == UiDragPhase.Pressed)
        {
            var dx = x - _pressX;
            var dy = y - _pressY;
            if (dx * dx + dy * dy < StartDistancePixels * StartDistancePixels)
            {
                return false;
            }

            Begin();
            MoveIcon(x, y);
            return true;
        }

        if (_phase == UiDragPhase.Dragging)
        {
            MoveIcon(x, y);
        }

        return false;
    }

    /// <summary>The element under the pointer after a move: the target under it answers, and the cell it highlights shows that answer (OpenAC #146).</summary>
    internal void Hover(UiElement? over, int x, int y)
    {
        if (_phase != UiDragPhase.Dragging || _source is null)
        {
            return;
        }

        Resolve(over, x, y, out var target, out var cell);
        UiItem? lit = null;
        var accept = false;
        if (target is not null && (_sourceList is null || !_sourceOwners.TryGetValue(_sourceList, out var hoverOwner) || hoverOwner.OwnsTarget(target)))
        {
            var payload = Payload();
            accept = target.Accepts(payload, cell);
            if (target.HighlightsCell && cell is not null && !ReferenceEquals(cell, _source))
            {
                lit = cell;
            }
        }

        SetHover(lit, accept);
    }

    /// <summary>
    /// The left button came up with the pointer over <paramref name="over"/>. A press that never became a drag is forgotten and false answers (the release is a click). A drag ends: dropped on a target that accepts, else nothing
    /// is sent; true answers (the release is taken).
    /// </summary>
    internal bool Release(UiElement? over, int x, int y)
    {
        if (_phase == UiDragPhase.Idle)
        {
            return false;
        }

        if (_phase == UiDragPhase.Pressed)
        {
            EndDrag();
            return false;
        }

        Resolve(over, x, y, out var target, out var cell);
        var payload = Payload();
        var source = _source!;
        var owner = _sourceList is not null && _sourceOwners.TryGetValue(_sourceList, out var found) ? found : null;
        EndDrag();
        if (target is null && owner is not null)
        {
            Dropped++;
            LastEnd = UiDragEnd.Dropped;
            owner.DragReleasedAway(payload, null);
            return true;
        }

        if (target is null || (cell is not null && ReferenceEquals(cell, source)))
        {
            Cancelled++;
            LastEnd = UiDragEnd.Cancelled;
            _manager.Services.Log.Info("retailui", target is null ? "drag released over no target; nothing was sent" : "drag released on its own cell; nothing was sent");
            return true;
        }

        if (owner is not null && !owner.OwnsTarget(target))
        {
            Dropped++;
            LastEnd = UiDragEnd.Dropped;
            owner.DragReleasedAway(payload, target);
            return true;
        }

        if (!target.Accepts(payload, cell))
        {
            Refused++;
            LastEnd = UiDragEnd.Refused;
            var what = payload.SpellId != 0 ? $"spell {payload.SpellId}" : $"item 0x{payload.ItemId:X8}";
            _manager.Services.Log.Info("retailui", $"drag of {what} refused: {target.Refusal(payload, cell) ?? "the target does not take it"}; nothing was sent");
            return true;
        }

        Dropped++;
        LastEnd = UiDragEnd.Dropped;
        target.Drop(payload, cell);
        return true;
    }

    /// <summary><paramref name="element"/> is being deleted: a drag that holds it ends, and it is no target.</summary>
    internal void Forget(UiElement element)
    {
        _targets.Remove(element);
        if (ReferenceEquals(_source, element) || ReferenceEquals(_hoverCell, element))
        {
            Cancel("an element of the drag was deleted");
        }

        if (ReferenceEquals(_icon, element))
        {
            _icon = null;
        }
    }

    /// <summary>The cell of the press as the router must release its mouse capture (set only while a drag is on).</summary>
    internal UiItem? CaptureHolder => _phase == UiDragPhase.Dragging ? _source : null;

    private UiDragPayload Payload() => new(_itemId, _source!, _sourceList, _stack, _spellId);

    private static UiItemList? ListOf(UiElement cell)
    {
        for (var e = cell.Parent; e is not null; e = e.Parent)
        {
            if (e is UiItemList list)
            {
                return list;
            }
        }

        return null;
    }

    /// <summary>
    /// The first registered target at or above <paramref name="over"/> and the item cell met on the way; null target for no element at all falls to <see cref="NoElementTarget"/>. M4 2.2 fix R4: the world's target
    /// (<see cref="NoElementTarget"/>, also registered on the 3D viewport) takes the release only where <see cref="WorldPoint"/> says the point is the world, never over a panel's empty area (the panel's fields are not
    /// mouse-visible, so the hit test falls through them to the full-screen viewport); a release there cancels and sends nothing.
    /// </summary>
    private void Resolve(UiElement? over, int x, int y, out IUiDropTarget? target, out UiItem? cell)
    {
        cell = null;
        for (var e = over; e is not null; e = e.Parent)
        {
            if (cell is null && e is UiItem found)
            {
                cell = found;
            }

            if (_targets.TryGetValue(e, out target))
            {
                if (target is not null && ReferenceEquals(target, NoElementTarget) && !IsWorldPoint(x, y))
                {
                    target = null; // the world view under a panel: the release cancels
                    cell = null;
                    return;
                }

                if (cell is null && e is UiItemList { CellCount: 1 } single)
                {
                    cell = single.CellAt(0); // a one-cell list (a paperdoll slot, a container's own cell): its cell answers even while empty, which is not mouse-visible
                }

                return;
            }
        }

        target = over is null && IsWorldPoint(x, y) ? NoElementTarget : null;
        cell = null;
    }

    private bool IsWorldPoint(int x, int y) => WorldPoint is null || WorldPoint(x, y);

    private void SetHover(UiItem? lit, bool accept)
    {
        var want = accept ? UiDragRollover.Accept : UiDragRollover.Reject;
        if (ReferenceEquals(lit, _hoverCell) && (lit is null || lit.DragRollover == want))
        {
            return;
        }

        _hoverCell?.ClearDragRollover();
        _hoverCell = lit;
        lit?.ShowDragRollover(accept);
    }

    private void Begin()
    {
        _phase = UiDragPhase.Dragging;
        Started++;
        _manager.LogOnce("drag:todo", LogSeverity.Warning, "TODO(m4-retail-drag): retail's drag functions have no address in the docs (address unknown); the drag is re-expressed from the layouts: it begins after a few pixels of movement, carries the pressed item and drops on the pack, a side pack, the container window, a paperdoll slot or the world view");
        if (_stack || _manager.ShiftKeyDown)
        {
            _manager.LogOnce("drag:split", LogSeverity.Warning, "TODO(m4-drag-stack-split): a stack is dragged whole; a shift-drag split (SplitTo*) is not built");
        }

        var icon = EnsureIcon();
        if (icon is not null && _source is not null && _source.TryGetIcon(out var fields))
        {
            icon.ShowItem(_itemId, fields with { Name = null, PluralName = null });
            icon.SetVisible(true);
            _manager.RootElement.BringChildToTop(icon);
        }
        else if (icon is not null && _spellId != 0)
        {
            if (SpellIconOf?.Invoke(_spellId) is { } spell)
            {
                icon.ShowSpell(spell);
                icon.SetVisible(true);
                _manager.RootElement.BringChildToTop(icon);
            }
            else
            {
                _manager.LogOnce("drag:spell-icon", LogSeverity.Warning, "TODO(m5-spell-drag-icon): no spell icon source is set on the drag; the dragged spell has no icon under the pointer (the spell is still carried)");
            }
        }
    }

    private UiItem? EnsureIcon()
    {
        if (_icon is not null || _iconFailed)
        {
            return _icon;
        }

        if (_sourceList is null || !_sourceList.TryGetEnumProperty(UiItemList.TemplateProperty, out var template) || !_manager.Services.Descs.TryResolveLayoutEnum(UiItemList.ItemLayoutEnum, out var layout)
            || _manager.CreateRootElementByDataId(layout, template) is not UiItem icon)
        {
            _iconFailed = true;
            _manager.Services.Log.Warn("retailui", "the drag has no icon: the item cell template of the source list could not be made as a root element, so the composed icon (the cell's icon, underlay, overlay, type background and glow, UIElement_UIItem 0x004E2E60 over IconData::RenderIcons 0x0058DFB0) is not drawn under the pointer (the item is still carried; TODO(m4-drag-icon))");
            return null;
        }

        icon.SetVisible(false);
        _icon = icon;
        return icon;
    }

    private void MoveIcon(int x, int y)
    {
        if (_icon is not { Visible: true } icon)
        {
            return;
        }

        // A root element hangs from the display, so the display's origin comes off (as the tooltip's placing does).
        var (ox, oy) = _manager.DisplayOrigin;
        icon.MoveTo(x - ox - icon.Box.Width / 2, y - oy - icon.Box.Height / 2);
    }

    private void EndDrag()
    {
        var dragging = _phase == UiDragPhase.Dragging;
        SetHover(null, false);
        if (_icon is { } icon)
        {
            if (icon.Visible)
            {
                icon.SetVisible(false);
            }

            if (!icon.IsEmpty)
            {
                icon.Clear();
            }
        }

        if (dragging)
        {
            _source?.CancelMouseDown(UiMouseActions.Left); // the press became a drag that ended elsewhere: no release message and no click follow (a plain click keeps its press)
        }

        _phase = UiDragPhase.Idle;
        _source = null;
        _sourceList = null;
        _itemId = 0;
        _spellId = 0;
        _stack = false;
    }
}
