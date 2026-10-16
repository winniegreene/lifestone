using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>One slot of the paperdoll: its item list, the equip locations it holds and which hand it is on (<c>gmPaperDollUI::GetLocationInfoFromElementID</c>).</summary>
/// <param name="ElementId">The slot's item list (32x32, one cell).</param>
/// <param name="Mask">The equip locations the slot shows (an item that occupies any of these bits is shown in it).</param>
/// <param name="Side">Retail's <c>UI_SLOT_SIDE</c>: 0 none, 1 left, 2 right.</param>
/// <param name="Name">The slot's name (tests and logs).</param>
/// <param name="Armour">True for the nine armour slots, which retail hides until the "slots" checkbox is checked.</param>
public readonly record struct PaperDollSlot(uint ElementId, uint Mask, int Side, string Name, bool Armour);

/// <summary>
/// The paperdoll of the inventory page (panel 7, <c>gmPaperDollUI</c> 0x10000024, element 0x100001CD; M3 2.2 slice b2, docs/milestones/m3-plan.md section 4.6): each worn or wielded item (<see cref="PlayerInventory.Equipped"/>) in the
/// slot of its equip location, on b1's <see cref="UiItem"/>; a double click on one takes it off through <see cref="IActionApi.MoveToPack"/>. Read-only otherwise (D13: no drag and drop in M3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>gmPaperDollUI::GetLocationInfoFromElementID</c> (11.6096 0x004A3B60) maps each slot list's element id to its location mask and side (<see cref="SlotTable"/>);
/// <c>RemakeCharacterInventory</c> (0x004A6930) empties every slot (<c>SetUIItemIntoLocation(0, 0x7FFFFFFF)</c>) and places each of the player's inventory placements, then redresses the figure;
/// <c>SetUIItemIntoLocation</c> (0x004A6300) tests the placement's mask against each slot's locations, so an item that covers several locations (a coat: chest, abdomen, upper arm and lower arm armour) is
/// shown in every one of those slots; the weapon slot takes any of 0x3500000 (melee, missile, held, two-handed). <c>PostInit</c> (0x004A56D0) hides the nine armour slots (head, chest, abdomen, upper arm, lower arm,
/// hand, upper leg, lower leg, foot) and the "slots" checkbox 0x100005BE (<c>ListenToElementMessage</c>, 0x004A5FA0) swaps the views: checked shows those nine and hides the 3D figure and its click mask,
/// unchecked the reverse. The ammunition slot's mask is a string address in the decompile (an inlined constant); it is taken as the ammunition location 0x800000, the only free location of the weapon group, and
/// matches the slot's place beside the weapon slot.
/// </para>
/// <para>
/// <b>The double click.</b> Retail's double click on a worn item is <c>ItemHolder::UseObject</c> (0x005898B0): <c>DetermineUseResult</c> (0x00589290) answers 2 for an item the player wields, and
/// <c>CPlayerSystem::UsingItem</c> (0x00563D10) answers 2 with <c>CPlayerSystem::PlaceInBackpack</c> (0x0055E5E0), which sends GameAction 0x0019 (put in container: the player's main pack). That is
/// <see cref="IActionApi.MoveToPack"/> with container 0, the client's unwield; the pack list shows the item once the server's answer (GameEvent 0x0022) arrives and <see cref="InventoryChanged"/> redraws it.
/// </para>
/// <para>
/// <b>Lifestone.</b> The page marks itself dirty on <see cref="InventoryChanged"/>, <see cref="ObjectCreated"/> of a worn item; the redraw is once, when shown, and once on the next frame after a change, touching only the slots whose item changed. A worn item whose world object has not arrived is left out until it does.
/// <b>The figure</b> (M3 2.2 b4, <see cref="PaperDollFigure"/>): retail draws the player's body in the viewport 0x100001D5 (<c>RedressCreature</c> 0x004A3F30, <c>UpdateForRace</c> 0x004A4240). The page makes the view model when
/// it first shows and hands it to the host (<see cref="ICg3DViewHost.AttachFigure"/>, the same drawing as character creation's); it is made again only when the player's appearance changed (the player's ObjDescEvent, an item
/// worn or taken off), when the page next shows. A page whose host cannot serve it (no view data, no view host) opens on the slots view as before (<c>TODO(m3-paperdoll-figure)</c>, logged once). A figure the page serves
/// opens as retail does: the nine armour slots hidden, the viewport and the click mask shown.
/// </para>
/// <para>
/// Not built, each a named TODO logged once: <c>TODO(m3-paperdoll-part-lighting)</c> (<c>ApplyPartSelectionLighting</c> 0x004A4010: the body part lights
/// when the cursor is over an item), <c>TODO(m3-aetheria)</c> (<c>UpdateAetheria</c> 0x004A41C0: the three sigil slots appear only with an Aetheria-bearing item), <c>TODO(m3-paperdoll-clickmap)</c> (the click mask over the
/// figure: select and examine the item under the cursor, <c>CreateClickMap</c> 0x004A4BC0), <c>TODO(m3-paperdoll-tooltips)</c> (<c>UpdateItemSlotTooltip</c> 0x004A5200), <c>TODO(m3-paperdoll-checkbox-targets)</c> (which two
/// elements the checkbox's handler toggles is read from raw member offsets: taken as the viewport and the click-mask button).
/// </para>
/// </remarks>
public sealed class GamePaperDoll : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The panel id of the page (the inventory page).</summary>
    public const uint Panel = 7;

    /// <summary>The paperdoll element (<c>gmPaperDollUI</c>).</summary>
    public const uint PaperDollId = 0x100001CD;

    /// <summary>The figure's viewport.</summary>
    public const uint ViewportId = 0x100001D5;

    /// <summary>The button over the figure (retail's click mask).</summary>
    public const uint ClickMaskId = 0x100001D6;

    /// <summary>The "slots" checkbox.</summary>
    public const uint SlotsCheckboxId = 0x100005BE;

    /// <summary>True: the figure is built (<see cref="PaperDollFigure"/>, served to the viewport by the host's <see cref="ICg3DViewHost.AttachFigure"/>); a page whose host cannot serve it opens on the slots view (<see cref="FigureServed"/> false).</summary>
    public const bool FigureBuilt = true;

    /// <summary>The weapon slot's locations: melee, missile, held and two-handed.</summary>
    public const uint WeaponLocations = 0x3500000;

    /// <summary>The slots, in the order of retail's <c>SetUIItemIntoLocation</c>.</summary>
    public static IReadOnlyList<PaperDollSlot> SlotTable { get; } =
    [
        new(0x100001DA, 0x8000, 0, "neck", false),
        new(0x100001DB, 0x10000, 1, "left wrist", false),
        new(0x100001DD, 0x20000, 2, "right wrist", false),
        new(0x100001DC, 0x40000, 1, "left ring", false),
        new(0x100001DE, 0x80000, 2, "right ring", false),
        new(0x100001DF, WeaponLocations, 0, "weapon", false),
        new(0x100001E1, 0x200000, 0, "shield", false),
        new(0x100001E0, 0x800000, 0, "ammunition", false),
        new(0x100001E3, 0x40, 0, "pants", false),
        new(0x100001E2, 0x2, 0, "shirt", false),
        new(0x1000058E, 0x4000000, 0, "trinket", false),
        new(0x100005E9, 0x8000000, 0, "cloak", false),
        new(0x10000595, 0x10000000, 0, "sigil one", false),
        new(0x10000596, 0x20000000, 0, "sigil two", false),
        new(0x10000597, 0x40000000, 0, "sigil three", false),
        new(0x100005AB, 0x1, 0, "head", true),
        new(0x100005AC, 0x200, 0, "chest", true),
        new(0x100005AD, 0x400, 0, "abdomen", true),
        new(0x100005AE, 0x800, 0, "upper arm", true),
        new(0x100005AF, 0x1000, 0, "lower arm", true),
        new(0x100005B0, 0x20, 0, "hand", true),
        new(0x100005B1, 0x2000, 0, "upper leg", true),
        new(0x100005B2, 0x4000, 0, "lower leg", true),
        new(0x100005B3, 0x100, 0, "foot", true),
    ];

    private readonly UiLoginContext _context;
    private readonly UiElement _page;
    private readonly UiElement? _viewport;
    private readonly UiElement? _clickMask;
    private readonly UiButton? _checkbox;
    private readonly UiItemList[] _lists;
    private readonly uint[] _shown;
    private readonly IDisposable[] _subscriptions;
    private bool _dirty = true;
    private bool _slotsView;
    private bool _todosLogged;
    private bool _figureTodoLogged;
    private readonly bool _hasHost;
    private PaperDollFigure? _figure;
    private IDisposable? _attachment;
    private bool _figureStale = true;

    private GamePaperDoll(UiLoginContext context, UiElement page, UiElement? viewport, UiElement? clickMask, UiButton? checkbox, UiItemList[] lists)
    {
        _context = context;
        _page = page;
        _viewport = viewport;
        _clickMask = clickMask;
        _checkbox = checkbox;
        _lists = lists;
        _shown = new uint[lists.Length];
        for (var i = 0; i < lists.Length; i++)
        {
            lists[i].ItemActivated += OnSlotActivated;
        }

        // PostInit (0x004A56D0) hides the armour slots and shows the figure; a host that cannot serve the figure (no view data or view host) leaves the page on the slots view.
        _hasHost = viewport is not null && context.CharGen is { View3D: not null, ViewHost: not null };
        if (_hasHost)
        {
            // The viewport owns a surface whose picture draws over the page's own (docs/retail-ui.md section 17), and the slots lie inside its box: each slot gets a surface of its own, after the viewport's
            // in the tree, so the slots draw over the figure as retail's do (the figure's view is black behind it, as retail's creature mode).
            for (var i = 0; i < lists.Length; i++)
            {
                lists[i].SetShouldOwnObject(true);
            }

            checkbox?.SetShouldOwnObject(true); // the "Slots" checkbox lies inside the viewport's box too
        }

        ApplyView(slotsView: !FigureBuilt || !_hasHost);
        page.RegisterForElementMessages(this);
        var events = context.Session.Events;
        _subscriptions = [events.Subscribe<InventoryChanged>(OnInventoryChanged), events.Subscribe<ObjectCreated>(OnObjectCreated), events.Subscribe<ObjectDescChanged>(OnObjectDescChanged)];
    }

    /// <summary>The panel id (7).</summary>
    public uint PanelId => Panel;

    /// <summary>How many times the paperdoll redrew (a test counts redraws).</summary>
    public int Refreshes { get; private set; }

    /// <summary>True while the nine armour slots show (and the figure and its click mask are hidden).</summary>
    public bool SlotsViewShown => _slotsView;

    /// <summary>True while the figure is served to the viewport (the model is built and the host draws it).</summary>
    public bool FigureServed => _attachment is not null;

    /// <summary>The figure's view model, or null before the page first showed (or when the host cannot serve it).</summary>
    public PaperDollFigure? Figure => _figure;

    /// <summary>The "slots" checkbox, or null when the layout lacks it.</summary>
    public UiButton? Checkbox => _checkbox;

    /// <summary>The figure's viewport, or null when the layout lacks it.</summary>
    public UiElement? Viewport => _viewport;

    /// <summary>The button over the figure, or null when the layout lacks it.</summary>
    public UiElement? ClickMask => _clickMask;

    /// <summary>
    /// <c>gmPaperDollUI::GetLocationInfoFromElementID</c> (11.6096 0x004A3B60): the locations and side of slot list <paramref name="elementId"/>. False (and zeros) for an element that is no slot.
    /// </summary>
    public static bool GetLocationInfoFromElementId(uint elementId, out uint location, out int side)
    {
        foreach (var slot in SlotTable)
        {
            if (slot.ElementId == elementId)
            {
                location = slot.Mask;
                side = slot.Side;
                return true;
            }
        }

        location = 0;
        side = 0;
        return false;
    }

    /// <summary>The item list of slot <paramref name="elementId"/>, or null.</summary>
    public UiItemList? SlotList(uint elementId)
    {
        for (var i = 0; i < _lists.Length; i++)
        {
            if (SlotTable[i].ElementId == elementId)
            {
                return _lists[i];
            }
        }

        return null;
    }

    /// <summary>The guid of the item slot <paramref name="elementId"/> shows now (0 for an empty slot or an element that is no slot).</summary>
    public uint ItemIn(uint elementId)
    {
        for (var i = 0; i < _lists.Length; i++)
        {
            if (SlotTable[i].ElementId == elementId)
            {
                return _lists[i].CellAt(0)?.ItemId ?? 0;
            }
        }

        return 0;
    }

    /// <summary>
    /// The controller for the paperdoll, or null (logged) when the page, the paperdoll or one of its slots is missing from the layout or the item list class is not registered.
    /// </summary>
    public static GamePaperDoll? Create(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        var log = context.Manager.Services.Log;
        if (host.ElementFor(Panel) is not { } page || page.GetChildRecursive(PaperDollId) is not { } doll)
        {
            log.Warn("retailui", "the paperdoll (gmPaperDollUI 0x100001CD of panel 7) is not in the layout; no controller (TODO(m3-paperdoll))");
            return null;
        }

        var lists = new UiItemList[SlotTable.Count];
        for (var i = 0; i < lists.Length; i++)
        {
            if (doll.GetChildRecursive(SlotTable[i].ElementId) is not UiItemList list)
            {
                log.Warn("retailui", $"the paperdoll lacks its {SlotTable[i].Name} slot 0x{SlotTable[i].ElementId:X8} or the UIElement_ItemList class is not registered; no controller (TODO(m3-paperdoll))");
                return null;
            }

            lists[i] = list;
        }

        return new GamePaperDoll(context, doll, doll.GetChildRecursive(ViewportId), doll.GetChildRecursive(ClickMaskId), doll.GetChildRecursive(SlotsCheckboxId) as UiButton, lists);
    }

    /// <summary>The page just showed: the slots are made and drawn now.</summary>
    public void OnShown()
    {
        // A parent's visibility change is not announced to its descendants (b1): the page starts the one-cell lists itself.
        for (var i = 0; i < _lists.Length; i++)
        {
            _lists[i].BeginFill();
        }

        LogTodos();
        Refresh();
        ServeFigure();
    }

    /// <summary>The page hid: it draws nothing until it shows again.</summary>
    public void OnHidden()
    {
    }

    /// <summary>Once per shell frame while shown: a change since the last draw is drawn once.</summary>
    public void Step(double now)
    {
        if (_dirty)
        {
            Refresh();
        }

        if (_figureStale && _figure is not null)
        {
            _figureStale = false;
            _figure.Update(now);
        }
    }

    /// <summary>
    /// Redraws every slot from the player's equipped items (<c>RemakeCharacterInventory</c> 0x004A6930 over <c>SetUIItemIntoLocation</c> 0x004A6300): a slot shows the last equipped item whose locations include
    /// one of the slot's; a slot with none (or whose item has no world object yet) is empty and keeps the layout's art.
    /// </summary>
    public void Refresh()
    {
        _dirty = false;
        Refreshes++;
        var session = _context.Session;
        var equipped = session.State.Self.Inventory.Equipped;
        var objects = session.State.Objects;
        for (var i = 0; i < _lists.Length; i++)
        {
            var mask = SlotTable[i].Mask;
            uint want = 0;
            for (var j = 0; j < equipped.Count; j++)
            {
                if ((equipped[j].Location & mask) != 0 && objects.Contains(equipped[j].Guid))
                {
                    want = equipped[j].Guid;
                }
            }

            var list = _lists[i];
            list.EnsureCells(1);
            if (list.CellAt(0) is not { } cell)
            {
                continue;
            }

            if (want != 0 && objects.TryGet(want, out var obj))
            {
                cell.ShowItem(want, GameInventoryPage.IconOf(obj));
                _shown[i] = want;
            }
            else
            {
                if (!cell.IsEmpty)
                {
                    cell.Clear();
                }

                _shown[i] = 0;
            }
        }
    }

    /// <summary>The checkbox's click (<c>gmPaperDollUI::ListenToElementMessage</c>, 0x004A5FA0): checked shows the armour slots, unchecked the figure.</summary>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && _checkbox is not null && ReferenceEquals(message.Element, _checkbox))
        {
            ApplyView(_checkbox.Highlighted);
            return UiListenResult.Handled;
        }

        return UiListenResult.Default;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _page.UnregisterForElementMessages(this);
        for (var i = 0; i < _lists.Length; i++)
        {
            _lists[i].ItemActivated -= OnSlotActivated;
        }

        _attachment?.Dispose();
        _attachment = null;
    }

    /// <summary>
    /// The page's first show (retail: <c>PostInit</c> 0x004A56D0 calls <c>RedressCreature</c> 0x004A3F30): the figure is made from the player's object and handed to the host to draw in the viewport. Once; later
    /// changes re-dress it in <see cref="Step"/>.
    /// </summary>
    private void ServeFigure()
    {
        if (!_hasHost || _figure is not null || _context.CharGen is not { View3D: { } data, ViewHost: { } host })
        {
            return;
        }

        var figure = new PaperDollFigure(data, _context.Session, _context.Manager.Services.Log);
        _figureStale = false;
        figure.Update(_context.Manager.Now);
        _figure = figure;
        _attachment = host.AttachFigure(ViewportId, figure);
        if (_attachment is null)
        {
            _context.Manager.Services.Log.Warn("retailui", "TODO(m3-paperdoll-figure): the host cannot draw the paperdoll's figure; the viewport 0x100001D5 shows what is behind the UI");
        }
    }

    /// <summary>Shows the armour slots (and hides the figure and its click mask) or the reverse, and the checkbox to match.</summary>
    private void ApplyView(bool slotsView)
    {
        _slotsView = slotsView;
        for (var i = 0; i < _lists.Length; i++)
        {
            if (SlotTable[i].Armour)
            {
                _lists[i].SetVisible(slotsView);
            }
        }

        _viewport?.SetVisible(!slotsView);
        _clickMask?.SetVisible(!slotsView);
        if (_checkbox is not null && _checkbox.Highlighted != slotsView)
        {
            _checkbox.SetBoolProperty(UiPropertyIds.ButtonHighlighted, slotsView);
        }

        if (!slotsView && !_hasHost && !_figureTodoLogged)
        {
            _figureTodoLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "TODO(m3-paperdoll-figure): the host gave the paperdoll no figure view (CharGenSetup.View3D / ViewHost); the viewport 0x100001D5 shows what is behind the UI (placeholder)");
        }
    }

    /// <summary>A double click on a worn item takes it off: <c>ItemHolder::UseObject</c> ends in <c>CPlayerSystem::PlaceInBackpack</c> (see the class remarks), which is <see cref="IActionApi.MoveToPack"/> with container 0.</summary>
    private void OnSlotActivated(UiItemList list, UiItem cell)
    {
        var item = cell.ItemId;
        if (item == 0)
        {
            return;
        }

        var session = _context.Session;
        if (!session.State.Self.Inventory.TryGetEquipped(item, out _))
        {
            return; // not worn any more (the redraw is pending)
        }

        session.Actions.MoveToPack(item, 0, 0, ActionSource.Ui);
    }

    private void LogTodos()
    {
        if (_todosLogged)
        {
            return;
        }

        _todosLogged = true;
        var log = _context.Manager.Services.Log;
        log.Warn("retailui", "TODO(m3-paperdoll-part-lighting): the body part under an item does not light when the cursor is over it (ApplyPartSelectionLighting 0x004A4010)");
        log.Warn("retailui", "TODO(m3-aetheria): the sigil slots always show (retail's UpdateAetheria 0x004A41C0 shows them only for an Aetheria-bearing item)");
        log.Warn("retailui", "TODO(m3-paperdoll-clickmap): the button over the figure selects and examines nothing (CreateClickMap 0x004A4BC0)");
        log.Warn("retailui", "TODO(m3-paperdoll-tooltips): the slots have no tooltips (UpdateItemSlotTooltip 0x004A5200)");
        log.Warn("retailui", "TODO(m3-paperdoll-sharp): the figure's view does not use CreatureMode::UseSharpMode (PostInit 0x004A56D0); it draws with creature mode's ordinary filtering");
        log.Warn("retailui", "TODO(m3-paperdoll-checkbox-targets): the slots checkbox toggles the viewport and the click-mask button; retail's handler names them only by member offset");
    }

    private void OnInventoryChanged(in InventoryChanged change)
    {
        _dirty = true;
        if (change.Kind is InventoryChangeKind.Wielded or InventoryChangeKind.Unwielded or InventoryChangeKind.Reset)
        {
            _figureStale = true; // RecvNotice_PlayerObjDescChanged (0x004A46C0) redresses; the server's ObjDescEvent follows a wield, and the same appearance redraws as itself
        }
    }

    private void OnObjectDescChanged(in ObjectDescChanged change)
    {
        if (change.Guid == _context.Session.State.Self.Guid)
        {
            _figureStale = true;
        }
    }

    private void OnObjectCreated(in ObjectCreated created)
    {
        if (_context.Session.State.Self.Inventory.TryGetEquipped(created.Guid, out _))
        {
            _dirty = true;
        }
    }
}
