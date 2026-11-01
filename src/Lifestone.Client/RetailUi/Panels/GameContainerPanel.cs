using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The container window (M4 2.2 s2): the window of a corpse or chest the player has open, over the world view. <b>Retail's function has no address in the docs</b> (<c>TODO(m4-retail-container-window)</c>: "address unknown"); the window is
/// re-expressed from the gameplay layout. The layout has it as <c>gmExternalContainerUI</c> (class 0x1000000D, window 0x1000005D, panel id 0x12) inside the environment floaty <c>gmFloatyEnvPanelUI</c> (0x100005FD, 610 by 120 at 0,400:
/// <see cref="UiFloatyEnvPanel"/>), which also holds the vendor, salvage, secure trade and slumlord windows. It is not <c>gm3DItemsUI</c> (0x10000021): that class is the inventory page's pack list and its title (element 0x100001CF
/// of page 7), a part of a page that has no art of its own. So the window needs no new art and no placeholder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> The window holds the container's own cell (<see cref="ContainerCellListId"/>, one 36 by 36 container cell), a row for containers inside the container (<see cref="SubContainerFieldId"/> with its scroll bar and list
/// 0x10000067), the close button (<see cref="CloseButtonId"/>, 25 by 23) and the item row (<see cref="ItemListId"/>: a horizontal item list that grows, 32 by 32 cells, with its scroll bar). Lifestone puts every entry in the item row, in
/// the server's order, except the containers inside the container (below); the title, "Contents of &lt;container name&gt;" (the corpse's own
/// name, e.g. "Contents of Corpse of Tusker"), is a text made from the inventory pack list's title (element 0x100001C5 of layout 0x21000021) and placed where that row is, as the layout has no title of its own.
/// </para>
/// <para>
/// <b>Containers inside the container</b> (M4 3.1 p, <c>m4-container-window-subcontainers</c>). An entry that is a container (ContainerType 1 in the 0196, or an object whose ItemType has the Container bit 0x200) is listed in the row for
/// containers inside the container (<see cref="SubContainerListId"/>, in field <see cref="SubContainerFieldId"/>), not in the item row, as the inventory page lists a side pack in its own column; the row shows only while the container holds
/// one, and the title text, which sits where that row is, is hidden while it shows (the layout has no free place for both: <c>TODO(m4-container-window-subcontainers-title)</c>). A click on a cell of the row opens that container through
/// <see cref="IActionApi.OpenContainer"/> (<see cref="ActionSource.Ui"/>; the server's 0196 for it replaces the viewed container and the window redraws for it). <b>Retail's use of the row has no address in the docs</b>: the row's behaviour is
/// re-expressed from the layout (the row and the container's own cell are container lists, property 0x10000011: "clicking a cell opens the container it holds") and the inventory page's side-pack column, so <c>TODO(m4-retail-container-window)</c> stays.
/// </para>
/// <para>
/// <b>Follows the container model</b> (<see cref="ContainerViews"/>, M4 2.1 s1). It opens on <see cref="ContainerOpened"/> (the layout shows the window by default, so it hides itself until then, and the floaty that holds it follows),
/// closes on <see cref="ContainerClosed"/> without sending anything (the server's 0x0052 is what closes a container), lists <see cref="ContainerView.Entries"/> in the view's order with each item's icon (<see cref="WorldObject"/>'s
/// <c>ItemFields()</c>, as the inventory page composes them), leaves an entry out until its F745 has landed (<see cref="ContainerViewEntry.Resolved"/> false: ACE sends the 0196 first) and redraws on
/// <see cref="ContainerContentsChanged"/> (an F745 resolving an entry, an item picked out, an item put in). A redraw makes at most <see cref="CellsPerRedraw"/> new cells and finishes over the next frames, so a large chest never builds
/// its cells in one frame. A hidden window is not drawn.
/// </para>
/// <para>
/// <b>Actions</b> (the Action API only, <see cref="ActionSource.Ui"/>): the close button sends <see cref="IActionApi.CloseContainer"/> for the viewed container; a double click on an entry sends <see cref="IActionApi.PickUp"/> into the
/// player (container = the player's id, placement 0: the loot move; the Inventory lane serializes moves). <b>Drag and drop</b> (M4 2.2 s3, D4): an entry is a drag source (a second route out beside the double click) and a held item dropped on the item row or the
/// container's own cell goes into the open container (<see cref="GameContainerDrop"/>, <see cref="IActionApi.MoveToPack"/>). A right click on an entry examines it (<see cref="GameExaminePanel.Open"/>, 3.2-R28).
/// </para>
/// </remarks>
public sealed class GameContainerPanel : UiElement
{
    /// <summary>The retail class id (<c>gmExternalContainerUI</c>; its <c>Register</c> has no address in the docs).</summary>
    public const uint RetailClassId = 0x1000000D;

    /// <summary>The window's element id in the gameplay layout (inside <see cref="UiFloatyEnvPanel"/>, window 0x100005FD).</summary>
    public const uint WindowId = 0x1000005D;

    /// <summary>The container's own cell (a one-cell container list).</summary>
    public const uint ContainerCellListId = 0x10000064;

    /// <summary>The field that holds the row for containers inside the container (hidden until the container holds one: M4 3.1 p).</summary>
    public const uint SubContainerFieldId = 0x10000065;

    /// <summary>The row for containers inside the container (a horizontal item list that grows, 36 by 36 cells, a container list: property 0x10000011).</summary>
    public const uint SubContainerListId = 0x10000067;

    /// <summary>The ContainerType a 0196 gives an entry that is a container (<c>ViewContentsEntry.ContainerType</c> 1; 2 is a focus, listed with the items).</summary>
    public const uint SubContainerType = 1;

    /// <summary>The ItemType bit of a container (<c>ITEM_TYPE::TYPE_CONTAINER</c>, 0x200): an entry whose object has it is a container whatever its ContainerType says.</summary>
    public const uint ContainerItemType = 0x200;

    /// <summary>The close button.</summary>
    public const uint CloseButtonId = 0x10000068;

    /// <summary>The item row (a horizontal item list that grows).</summary>
    public const uint ItemListId = 0x1000006A;

    /// <summary>The layout whose pack-list title is the model for the window's title (the inventory page's <c>gm3DItemsUI</c> layout).</summary>
    public const uint TitleLayoutId = 0x21000021;

    /// <summary>The element of <see cref="TitleLayoutId"/> the title is made from (the pack list's title text).</summary>
    public const uint TitleElementId = 0x100001C5;

    /// <summary>The most cells a redraw makes (the first pass of a large chest; the rest follow a frame at a time).</summary>
    public const int CellsPerRedraw = 24;

    private UiLoginContext? _context;
    private UiItemList? _containerCell;
    private UiItemList? _items;
    private UiItemList? _subs;
    private UiElement? _subField;
    private bool _subShown;
    private uint _subOpenGuid;
    private UiTextElement? _title;
    private IDisposable[] _subscriptions = [];
    private uint _shownGuid;
    private string _titleText = string.Empty;
    private bool _more;
    private bool _todoLogged;
    private bool _subOpenLogged;
    private readonly List<(uint Guid, WorldObject Object)> _entries = new(32);
    private readonly List<(uint Guid, WorldObject Object)> _subEntries = new(8);

    /// <summary>The window of <paramref name="init"/>.</summary>
    public GameContainerPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The item types the examination's review frame takes (M5 7 S6): melee and missile weapons, armour, clothing, jewellery and casters, the loot that carries spells, an activation line, mana and workmanship.</summary>
    public const uint ReviewItemTypes = 0x1 | 0x2 | 0x4 | 0x8 | 0x100 | 0x8000;

    /// <summary>
    /// What the examination's live review frames (M5 7 S6, D136, <c>--examine-review-frames</c>) still wait for, or null with <paramref name="window"/> and <paramref name="cell"/> set: the world's wait
    /// (<see cref="GamePaperDoll.WorldReviewWait"/>: placed, no tunnel, materialised, the shell painted), then the container window shown and fully drawn (<see cref="DrawState"/>), then the first cell of its item row whose object
    /// glows magical (UiEffects bit 0) and is of <see cref="ReviewItemTypes"/>, not among <paramref name="tried"/> (an item whose answer lacked a line, or that left before its frame).
    /// </summary>
    public static string? ExamineReviewFrameWait(GamePlayScreen? shell, bool playerPlaced, bool tunnelVisible, bool playerMaterialised, IReadOnlySet<uint> tried, out GameContainerPanel? window, out UiItem? cell)
    {
        ArgumentNullException.ThrowIfNull(tried);
        window = null;
        cell = null;
        if (GamePaperDoll.WorldReviewWait(shell, playerPlaced, tunnelVisible, playerMaterialised) is { } world)
        {
            return world;
        }

        if (shell!.Root?.GetChildRecursive(WindowId) is not GameContainerPanel panel || !panel.IsShown || panel._items is not { IsShown: true } row || panel._context is null)
        {
            return "a container window to show";
        }

        if (panel.DrawState != ContainerWindowDraw.Drawn)
        {
            return "the container window to be drawn";
        }

        var objects = panel._context.Session.State.Objects;
        for (var i = 0; i < row.CellCount; i++)
        {
            if (row.CellAt(i) is { IsEmpty: false, IsShown: true } candidate
                && !tried.Contains(candidate.ItemId)
                && objects.TryGet(candidate.ItemId, out var obj)
                && (obj.ItemFields().UiEffects & 1) != 0
                && (obj.ItemType & ReviewItemTypes) != 0)
            {
                window = panel;
                cell = candidate;
                return null;
            }
        }

        return "a magic weapon, armour, jewellery or caster in the container window";
    }

    /// <summary>The title text now ("Contents of &lt;container name&gt;"), or empty before the window drew.</summary>
    public string Title => _titleText;

    /// <summary>The container the window shows now (0 while it is hidden).</summary>
    public uint ShownContainer => Visible ? _shownGuid : 0;

    /// <summary>The item row (a test reads its cells).</summary>
    public UiItemList? ItemRow => _items;

    /// <summary>The container's own cell list.</summary>
    public UiItemList? ContainerCell => _containerCell;

    /// <summary>The row for containers inside the container (a test reads its cells).</summary>
    public UiItemList? SubContainerRow => _subs;

    /// <summary>True while the row for containers inside the container shows (the viewed container holds one).</summary>
    public bool SubContainerRowShown => _subShown && Visible;

    /// <summary>The ids of the containers inside the viewed container that the row lists, in the view's order.</summary>
    public IEnumerable<uint> ShownSubContainers
    {
        get
        {
            if (_subs is null)
            {
                yield break;
            }

            for (var i = 0; i < _subs.CellCount; i++)
            {
                if (_subs.CellAt(i) is { IsEmpty: false } cell)
                {
                    yield return cell.ItemId;
                }
            }
        }
    }

    /// <summary>How many times a click on a cell of the sub-container row asked to open it (a test counts them).</summary>
    public int SubContainerOpens { get; private set; }

    /// <summary>The last open a click on the sub-container row asked for, or null.</summary>
    public ActionHandle? LastSubContainerOpen { get; private set; }

    /// <summary>
    /// True when <paramref name="entry"/> is listed in the sub-container row and not in the item row: the 0196 names it a container (<see cref="SubContainerType"/>) or its object's ItemType has the Container bit
    /// (<see cref="ContainerItemType"/>). The same rule <see cref="Redraw"/> and <see cref="DrawState"/> draw by.
    /// </summary>
    /// <param name="entry">The view's entry.</param>
    /// <param name="obj">Its object (resolved).</param>
    public static bool IsSubContainer(in ContainerViewEntry entry, WorldObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return entry.ContainerType == SubContainerType || (obj.ItemType & ContainerItemType) != 0;
    }

    /// <summary>The ids of the entries the window shows, in the view's order (entries whose objects have not landed are not in it).</summary>
    public IEnumerable<uint> ShownEntries
    {
        get
        {
            if (_items is null)
            {
                yield break;
            }

            for (var i = 0; i < _items.CellCount; i++)
            {
                if (_items.CellAt(i) is { IsEmpty: false } cell)
                {
                    yield return cell.ItemId;
                }
            }
        }
    }

    /// <summary>
    /// Whether the window is fully drawn for the container the model views now (M4 2.2 s4: the hunt's <c>corpse</c> frame and the panel shots wait for <see cref="ContainerWindowDraw.Drawn"/> before they save). A read only: allocation-free,
    /// nothing is drawn or changed. The window counts as drawn when it is shown for the viewed container, every entry of the view has its object (<see cref="ContainerView.IsResolved"/>), the item row holds one cell for each resolved
    /// entry in the view's order (the rule <see cref="Redraw"/> draws by), no redraw has cells left to make and no cell is still waiting for its icon.
    /// </summary>
    public ContainerWindowDraw DrawState
    {
        get
        {
            if (_context is null || !Visible || _items is null)
            {
                return ContainerWindowDraw.Hidden;
            }

            var state = _context.Session.State;
            if (state.Containers.Viewed is not { } view || view.Guid != _shownGuid)
            {
                return ContainerWindowDraw.WrongContainer;
            }

            if (!view.IsResolved)
            {
                return ContainerWindowDraw.Unresolved;
            }

            if (_more)
            {
                return ContainerWindowDraw.Drawing;
            }

            var objects = state.Objects;
            var n = 0;
            var subs = 0;
            var entries = view.Entries;
            for (var e = 0; e < entries.Count; e++) // an index loop: foreach over an IReadOnlyList boxes its enumerator (M4 2.2 fix2, 2.2-R14)
            {
                var entry = entries[e];
                if (!entry.Resolved || !objects.TryGet(entry.Guid, out var obj))
                {
                    continue; // as Redraw: an entry whose object has not landed is not listed
                }

                if (_subs is not null && IsSubContainer(in entry, obj))
                {
                    if (subs >= _subs.CellCount || _subs.CellAt(subs) is not { } subCell || subCell.ItemId != entry.Guid)
                    {
                        return ContainerWindowDraw.EntriesDiffer;
                    }

                    subs++;
                    continue;
                }

                if (n >= _items.CellCount || _items.CellAt(n) is not { } cell || cell.ItemId != entry.Guid)
                {
                    return ContainerWindowDraw.EntriesDiffer;
                }

                n++;
            }

            if (n != _items.OccupiedCount || subs != (_subs?.CellCount ?? 0) || (subs > 0) != _subShown) // the empty slot boxes after the entries are not entries (M4 3.2 lane e)
            {
                return ContainerWindowDraw.EntriesDiffer;
            }

            for (var i = 0; i < n; i++)
            {
                if (_items.CellAt(i) is { IconPending: true })
                {
                    return ContainerWindowDraw.IconsPending;
                }
            }

            for (var i = 0; i < subs; i++)
            {
                if (_subs!.CellAt(i) is { IconPending: true })
                {
                    return ContainerWindowDraw.IconsPending;
                }
            }

            return ContainerWindowDraw.Drawn;
        }
    }

    /// <summary>The drop target of the item row (set by <see cref="Attach"/>).</summary>
    public GameContainerDrop? ItemRowDrop { get; private set; }

    /// <summary>The drop target of the container's own cell (set by <see cref="Attach"/>).</summary>
    public GameContainerDrop? ContainerCellDrop { get; private set; }

    /// <summary>How many times a double click asked for a pick-up (a test counts them).</summary>
    public int PickUps { get; private set; }

    /// <summary>The last close the close button asked for, or null.</summary>
    public ActionHandle? LastClose { get; private set; }

    /// <summary>The last pick-up a double click asked for, or null.</summary>
    public ActionHandle? LastPickUp { get; private set; }

    /// <summary>How many times the window redrew (a test counts redraws).</summary>
    public int Redraws { get; private set; }

    /// <summary>
    /// <c>PostInit</c> (retail's has no address in the docs): finds the cells, the close button and the item row, hides the row for containers inside the container, and hides the window itself (the layout shows it by default; the
    /// floaty that holds it follows). Subscribes to nothing until <see cref="Attach"/>.
    /// </summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _containerCell = GetChildRecursive(ContainerCellListId) as UiItemList;
        _items = GetChildRecursive(ItemListId) as UiItemList;
        _subField = GetChildRecursive(SubContainerFieldId);
        _subField?.SetVisible(false); // shown only while the viewed container holds a container (Redraw)
        _subs = GetChildRecursive(SubContainerListId) as UiItemList;
        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        if (_items is not null)
        {
            _items.ItemActivated += OnItemActivated;
            _items.ItemExamined += OnItemExamined; // 3.2-R28 (closes TODO(m4-itemlist-examine) here): a right click on an entry examines it, as on the inventory page
            _items.DragSource = true; // an entry is dragged out to a pack, a side pack, a paperdoll slot or the world view (M4 2.2 s3; the container's own cell is not a source)
        }

        if (_subs is not null)
        {
            _subs.ItemSelected += OnSubContainerSelected; // a click opens the container the cell holds (M4 3.1 p)
            _subs.DragSource = true; // a pack inside the container is dragged out as an item is
        }
    }

    /// <summary>
    /// Hands the window the session: it follows <see cref="ContainerOpened"/>, <see cref="ContainerClosed"/> and <see cref="ContainerContentsChanged"/>, makes its title and opens at once when the session holds an open container now.
    /// </summary>
    public void Attach(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        foreach (var earlier in _subscriptions)
        {
            earlier.Dispose(); // Attach again (a test times it): one set of subscriptions
        }

        var events = context.Session.Events;
        _subscriptions =
        [
            events.Subscribe<ContainerOpened>(OnOpened),
            events.Subscribe<ContainerClosed>(OnClosed),
            events.Subscribe<ContainerContentsChanged>(OnContentsChanged),
        ];
        if (_containerCell is null || _items is null || GetChildRecursive(CloseButtonId) is not UiButton)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, "gmExternalContainerUI: the window lacks its container cell 0x10000064, its item row 0x1000006A or its close button 0x10000068; the container window cannot list or close (TODO(m4-container-window-missing))");
        }

        if (_subs is null || _subField is null)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, "gmExternalContainerUI: the window lacks its row for containers inside the container (field 0x10000065, list 0x10000067); such entries are listed in the item row (TODO(m4-container-window-subcontainers))");
        }

        if (_title is null)
        {
            MakeTitle(context);
        }

        // M4 2.2 s3 (D4): a held item dropped on the item row or the container's own cell goes into the open container (a put into a chest); both answer with the model's open container, so a window with nothing open takes nothing.
        var drag = context.Manager.Input.Drag;
        ItemRowDrop = new GameContainerDrop(context, _ => context.Session.State.Containers.ViewedGuid, false);
        ContainerCellDrop = new GameContainerDrop(context, _ => context.Session.State.Containers.ViewedGuid, true);
        if (_items is not null)
        {
            drag.RegisterTarget(_items, ItemRowDrop);
        }

        if (_containerCell is not null)
        {
            drag.RegisterTarget(_containerCell, ContainerCellDrop);
        }

        context.Manager.Services.Log.Warn("retailui", "gmExternalContainerUI: TODO(m4-retail-container-window) address unknown (the window is re-expressed from the gameplay layout, window 0x1000005D in gmFloatyEnvPanelUI); TODO(m4-container-window-subcontainers-title) the row for containers inside the container (a click opens one through OpenContainer) shows only while the container holds one, and then hides the title; TODO(m4-retail-drag)");
        if (context.Session.State.Containers.Viewed is not null)
        {
            Open();
        }
    }

    /// <summary>Shows the window for the viewed container and draws it (what <see cref="ContainerOpened"/> does).</summary>
    public void Open()
    {
        if (_context is null || _context.Session.State.Containers.Viewed is not { } view)
        {
            return;
        }

        _shownGuid = view.Guid;
        SetVisible(true);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        Redraw();
    }

    /// <summary>Hides the window and lets go of the container (what <see cref="ContainerClosed"/> does); nothing is sent.</summary>
    public void Close()
    {
        _shownGuid = 0;
        _more = false;
        SetVisible(false);
        (Parent as UiFloatyEnvPanel)?.UpdateVisibility();
        _items?.ClearSelection();
        _subs?.ClearSelection();
        ShowSubContainerRow(false);
    }

    /// <summary>The close button's request: <see cref="IActionApi.CloseContainer"/> for the viewed container, or null when none is open. The window closes when the server's close arrives, not before.</summary>
    public ActionHandle? RequestClose()
    {
        if (_context is null)
        {
            return null;
        }

        var guid = _context.Session.State.Containers.ViewedGuid;
        if (guid == 0)
        {
            return null;
        }

        LastClose = _context.Session.Actions.CloseContainer(guid, ActionSource.Ui);
        return LastClose;
    }

    /// <summary>
    /// Draws the window from the container model now (nothing while it is hidden): the title, the container's own cell and the resolved entries in the view's order, changing only what differs. At most
    /// <see cref="CellsPerRedraw"/> new cells are made; the rest follow on the next frames.
    /// </summary>
    public void Redraw()
    {
        if (_context is null || !Visible || _items is null)
        {
            return;
        }

        var session = _context.Session;
        if (session.State.Containers.Viewed is not { } view)
        {
            return;
        }

        Redraws++;
        _more = false;
        var objects = session.State.Objects;
        var container = objects.TryGet(view.Guid, out var found) ? found : null;
        SetTitle(container is { Name.Length: > 0 } ? "Contents of " + container.Name : "Contents");
        if (_containerCell is { } own)
        {
            own.EnsureCells(1);
            if (own.CellAt(0) is { } cell)
            {
                if (container is not null)
                {
                    cell.ShowItem(container.Guid, GameInventoryPage.IconOf(container, view.Entries.Count));
                }
                else if (!cell.IsEmpty)
                {
                    cell.Clear();
                }
            }
        }

        var entries = _entries;
        entries.Clear();
        var subEntries = _subEntries;
        subEntries.Clear();
        foreach (var entry in view.Entries)
        {
            if (entry.Resolved && objects.TryGet(entry.Guid, out var obj))
            {
                (_subs is not null && IsSubContainer(in entry, obj) ? subEntries : entries).Add((entry.Guid, obj));
            }
        }

        // M4 3.1 p: the row for containers inside the container shows while the container holds one (and is shown before its cells are made); the title that sits there gives way.
        ShowSubContainerRow(subEntries.Count > 0);
        var made = 0;
        if (_subs is not null)
        {
            FillRow(_subs, subEntries, ref made, true, session.State);
        }

        FillRow(_items, entries, ref made, false, session.State);
        if (_more)
        {
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary>
    /// Makes <paramref name="row"/> show <paramref name="entries"/> in order, changing only what differs. At most <see cref="CellsPerRedraw"/> new cells are made in all (<paramref name="made"/> counts them across the rows of one redraw);
    /// the rest follow on the next frames. The item row, a list that grows, shows its empty slot boxes after the last entry (<see cref="UiItemList.UpdateEmptySlots"/>, M4 3.2 lane e: <c>UpdateEmptySlots</c> 11.6096 0x004E4390); the cells past the last entry of the row for containers inside the container go. A sub-container's cell shows how many items it holds when the model has its view.
    /// </summary>
    private void FillRow(UiItemList row, List<(uint Guid, WorldObject Object)> entries, ref int made, bool sub, GameState state)
    {
        var shown = 0;
        for (; shown < entries.Count; shown++)
        {
            if (shown >= row.CellCount)
            {
                if (made >= CellsPerRedraw)
                {
                    _more = true;
                    break;
                }

                if (!row.EnsureCells(shown + 1))
                {
                    break; // no cell template: the list logs it once
                }

                made++;
            }

            if (row.CellAt(shown) is not { } target)
            {
                break;
            }

            var (guid, obj) = entries[shown];
            target.ShowItem(guid, sub ? GameInventoryPage.IconOf(obj, state.Containers.TryGet(guid, out var held) ? held.Entries.Count : 0) : GameInventoryPage.IconOf(obj));
        }

        // The cells past the last entry: the item row empties them (they are its empty slot boxes, which UpdateEmptySlots then pads to the row's width or trims); the sub-container row keeps none.
        for (var i = row.CellCount - 1; i >= shown; i--)
        {
            if (sub)
            {
                row.DeleteItem(i);
            }
            else if (row.CellAt(i) is { IsEmpty: false } leftover)
            {
                leftover.Clear();
            }
        }

        if (!sub)
        {
            row.UpdateEmptySlots();
        }

        row.SelectItem(row.SelectedItemId); // the selection goes with an item that left
    }

    /// <summary>Shows or hides the row for containers inside the container; while it shows the title text, which sits where it is, is hidden.</summary>
    private void ShowSubContainerRow(bool show)
    {
        if (_subShown == show)
        {
            return;
        }

        _subShown = show;
        _subField?.SetVisible(show);
        _title?.SetVisible(!show);
    }

    /// <summary>
    /// A click on a cell of the sub-container row: the container it holds is opened through <see cref="IActionApi.OpenContainer"/> (<see cref="ActionSource.Ui"/>; <c>TODO(m4-retail-container-window)</c>: retail's handler of the row has no address
    /// in the docs). The server's 0196 for it replaces the viewed container. A cell of the container already viewed, or a second click while its open is still in flight, sends nothing.
    /// </summary>
    private void OnSubContainerSelected(UiItemList list, UiItem cell)
    {
        if (_context is null || cell.ItemId == 0)
        {
            return;
        }

        var guid = cell.ItemId;
        if (_context.Session.State.Containers.ViewedGuid == guid || (LastSubContainerOpen is { IsCompleted: false } && _subOpenGuid == guid))
        {
            return;
        }

        _subOpenGuid = guid;
        SubContainerOpens++;
        LastSubContainerOpen = _context.Session.Actions.OpenContainer(guid, ActionSource.Ui);
        if (!_subOpenLogged)
        {
            _subOpenLogged = true;
            _context.Manager.Services.Log.Info("retailui", "container window: a click on the row for containers inside the container sends OpenContainer for it (TODO(m4-retail-container-window): retail's handler of the row has no address in the docs)");
        }
    }

    /// <summary>The close button closes (sends); a message about another element is the base's.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == CloseButtonId)
        {
            RequestClose();
            return UiListenResult.Handled;
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>The frame after a redraw that left cells to make: the redraw goes on.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame && _more)
        {
            _more = false;
            Redraw();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions = [];
        base.OnDeleting();
    }

    private void OnItemExamined(UiItemList list, UiItem cell)
    {
        if (_context is not null && cell.ItemId != 0)
        {
            GameExaminePanel.Of(_context)?.Open(cell.ItemId);
        }
    }

    private void OnOpened(in ContainerOpened opened) => Open();

    private void OnClosed(in ContainerClosed closed)
    {
        if (_context is null || _context.Session.State.Containers.ViewedGuid != closed.Container)
        {
            Close(); // the server closed the container (or a second open replaced it): the window closes, and opens again for the replacement
        }
    }

    private void OnContentsChanged(in ContainerContentsChanged changed)
    {
        if (_context is not null && Visible && changed.Container == _context.Session.State.Containers.ViewedGuid)
        {
            Redraw();
        }
    }

    /// <summary>A double click on an entry: the loot move, into the player's own pack (<see cref="IActionApi.PickUp"/>, <c>TODO(m4-retail-container-window)</c>).</summary>
    private void OnItemActivated(UiItemList list, UiItem cell)
    {
        if (_context is null || cell.ItemId == 0 || _context.Session.State.Containers.Viewed is null)
        {
            return;
        }

        var self = _context.Session.State.Self;
        var player = self.Inventory.Owner != 0 ? self.Inventory.Owner : self.Guid;
        PickUps++;
        LastPickUp = _context.Session.Actions.PickUp(cell.ItemId, player, 0, ActionSource.Ui);
        if (!_todoLogged)
        {
            _todoLogged = true;
            _context.Manager.Services.Log.Info("retailui", "container window: a double click on an entry sends PickUp into the pack (the loot move); the Inventory lane serializes moves");
        }
    }

    private void SetTitle(string text)
    {
        if (text != _titleText)
        {
            _titleText = text;
            _title?.SetText(text);
        }
    }

    /// <summary>Makes the title text from the inventory pack list's title (the same font and colour), where the hidden row for containers inside the container is.</summary>
    private void MakeTitle(UiLoginContext context)
    {
        var manager = context.Manager;
        if (!manager.Services.Descs.TryGetLayout(TitleLayoutId, out var layout) || layout.AllElements().FirstOrDefault(static e => e.ElementId == TitleElementId) is not { } desc)
        {
            manager.Services.Log.Error(UiElementManager.Category, $"gmExternalContainerUI: layout 0x{TitleLayoutId:X8} or its title text 0x{TitleElementId:X8} is missing; the window has no title (TODO(m4-container-window-title-missing))");
            return;
        }

        _title = manager.CreateChildElement(this, layout, desc) as UiTextElement;
        if (_title is null)
        {
            manager.Services.Log.Error(UiElementManager.Category, "gmExternalContainerUI: the title text could not be made; the window has no title (TODO(m4-container-window-title-missing))");
            return;
        }

        _title.MoveTo(48, 8);
        _title.ResizeTo(500, 15);
        if (_titleText.Length > 0)
        {
            _title.SetText(_titleText);
        }
    }
}

/// <summary>Whether the container window is fully drawn (<see cref="GameContainerPanel.DrawState"/>), or what it is still waiting for; the first reason that holds, in this order.</summary>
public enum ContainerWindowDraw
{
    /// <summary>The window is not shown (or has no session or item row yet).</summary>
    Hidden,

    /// <summary>The model views no container, or another one than the window shows.</summary>
    WrongContainer,

    /// <summary>An entry of the view has no object yet (its F745 has not landed).</summary>
    Unresolved,

    /// <summary>A redraw has cells left to make; the next frames make them.</summary>
    Drawing,

    /// <summary>The item row's cells are not the view's resolved entries in the view's order (a redraw has not run since the contents changed).</summary>
    EntriesDiffer,

    /// <summary>A cell is still waiting for its icon.</summary>
    IconsPending,

    /// <summary>Shown for the viewed container with every entry listed and every icon drawn.</summary>
    Drawn,
}
