using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// What a session offers the components page beside <see cref="IGameSessionView"/>: the SpellComponentTable's icon of a component (<c>SpellComponentTable::InqSpellComponentBase</c>, 11.6096 0x0048A1F0), which retail's
/// <c>gmSpellComponentUI::MakeIcon</c> (0x0048A250) draws in a row. Read through a type test so a session without the table (tests, headless) needs nothing.
/// </summary>
public interface ISpellComponentIcons
{
    /// <summary>The icon (a RenderSurface DID) of the component with SpellComponentTable id <paramref name="componentId"/>, or 0 when the table is not loaded or lacks it.</summary>
    /// <param name="componentId">The SpellComponentTable id (the component id of <see cref="SpellPageData.CategoryOf"/>).</param>
    uint ComponentIcon(uint componentId);
}

/// <summary>
/// The components tab of the spell page (panel 13, <c>gmSpellComponentUI</c> 0x1000002F, tab element 0x100002AD; M3 2.2 slice b3, docs/milestones/m3-plan.md section 4.6): the spell components in the pack, a title row
/// for each of retail's seven categories and under it a row for each component class the pack holds, with its icon, name, count and the desired ("buy rate") amount.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>gmSpellComponentUI::PostInit</c> (11.4186 0x00489FE0, 11.6096 0x0048A4C0) finds the list box 0x10000464 and fills the seven category titles (scarabs, herbs, gems, alchemical, talismans, tapers, peas).
/// <c>UpdateComponents</c> (0x0048ABF0, 11.4186 0x0048A770) walks the player's component tracker category by category: a category with components gets a title row (list template 0, element 0x10000466; its text the
/// category's string of table 0x10000001), and each component class a row (template 1, element 0x10000467) that remembers the class in attribute 0x1000004C and holds the icon 0x10000468 (<c>MakeIcon</c>, 0x0048A250:
/// the SpellComponentTable's icon), the name 0x10000469, the count 0x1000046A (<c>SyncComponentRegionWithData</c>, 0x0048A900: "%d") and the buy rate 0x1000046B (<c>UpdateBuyRates</c>, 0x0048A310: the player module's
/// desired level of the class, an amount outside 0 to 5000 is reset to 0). Typing a buy rate and leaving the box (<c>ListenToElementMessage</c>, 0x0048A700, focus message 0x2F) sends the new level to the server
/// (<c>CM_Character::Event_SetDesiredComponentLevel</c>) when it is 0 to 5000, otherwise puts the old one back; a click on a row selects the component's first object.
/// </para>
/// <para>
/// <b>Lifestone.</b> The tracker is the pack: every item whose ItemType is a spell component (<see cref="SpellPageData.IsSpellComponent"/>) in the main pack and in the side packs the model knows, summed by weenie
/// class (a stack of 12 and a stack of 30 are one row of 42). The category comes from the pinned component map (<see cref="SpellPageData.CategoryOf"/>); a weenie it lacks lists under "peas" and logs
/// <c>TODO(m3-component-category)</c> once. Rows within a category are ordered by the SpellComponentTable id (retail's tracker order is its hash order). The icon is the SpellComponentTable's, as <c>MakeIcon</c> draws it (11.6096 0x0048A250, 11.4186 0x00489F20, over <c>SpellComponentTable::InqSpellComponentBase</c> 0x0048A1F0):
/// a session that can read the table (<see cref="ISpellComponentIcons"/>) gives it by the component's id; a session that cannot (the table is not loaded, no dats) leaves the pack object's own icon in its place and the page logs <c>TODO(m3-component-icon)</c> once. A component the character wants to rebuy but does not carry has no row (retail's tracker is read from the pack; <c>TODO(m3-component-zero-rows)</c>
/// is whether retail keeps a zero-count row). The buy-rate box shows the desired amount; typing in it answers NotInM3 and puts the shown amount back (<c>TODO(m3-component-buy-rate)</c>: no action sends the level).
/// The page redraws once when it shows and, while shown, on the next frame after an <see cref="InventoryChanged"/> or <see cref="SpellbookChanged"/>. A change of the set of rows rebuilds the list over frames
/// (<see cref="RowsPerFrame"/> a frame); a change of a count or a desired amount only rewrites that text.
/// </para>
/// </remarks>
public sealed class GameComponentsPage : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The panel id of the spell page.</summary>
    public const uint Page = 13;

    /// <summary>The tab's element, <c>gmSpellComponentUI</c> (class 0x1000002F).</summary>
    public const uint TabId = 0x100002AD;

    /// <summary>The class id of <c>gmSpellComponentUI</c>.</summary>
    public const uint ClassId = 0x1000002F;

    /// <summary>The list box (0x10000464).</summary>
    public const uint ListId = 0x10000464;

    /// <summary>The row template's children: icon, name, count and buy rate.</summary>
    public const uint RowIconId = 0x10000468, RowNameId = 0x10000469, RowCountId = 0x1000046A, RowBuyRateId = 0x1000046B;

    /// <summary>The string table enum the category titles are in (0x10000001).</summary>
    public const uint TableEnum = 0x10000001;

    /// <summary>How many rows (titles included) are built each frame.</summary>
    public const int RowsPerFrame = 12;

    /// <summary>The largest buy rate retail accepts (0x0048A700 resets an amount over 5000).</summary>
    public const int MaxBuyRate = 5000;

    private const int TitleTemplate = 0;
    private const int RowTemplate = 1;

    private static readonly string[] CategoryTokens =
    [
        "ID_SpellComp_Category_Scarabs",
        "ID_SpellComp_Category_Herbs",
        "ID_SpellComp_Category_Gems",
        "ID_SpellComp_Category_Alchemical",
        "ID_SpellComp_Category_Talismans",
        "ID_SpellComp_Category_Tapers",
        "ID_SpellComp_Category_Peas",
    ];

    // The English titles stand in when the string table does not render a token (logged once).
    private static readonly string[] CategoryFallbacks = ["Scarabs", "Herbs", "Gems", "Alchemical", "Talismans", "Tapers", "Peas"];

    private readonly UiLoginContext _context;
    private readonly UiElement _tab;
    private readonly UiListBox _list;
    private readonly IDisposable[] _subscriptions;
    private readonly List<Line> _lines = new(80);
    private readonly List<Built> _built = new(80);
    private readonly List<(UiElement Icon, uint Did)> _pendingIcons = [];
    private bool _pageShown;
    private bool _dirty = true;
    private bool _building;
    private int _buildIndex;
    private bool _categoryTodoLogged;
    private bool _iconTodoLogged;
    private bool _buyTodoLogged;
    private bool _zeroTodoLogged;
    private bool _titleTodoLogged;

    private GameComponentsPage(UiLoginContext context, UiElement tab, UiListBox list)
    {
        _context = context;
        _tab = tab;
        _list = list;
        tab.RegisterForElementMessages(this);
        var events = context.Session.Events;
        _subscriptions = [events.Subscribe<InventoryChanged>(OnInventoryChanged), events.Subscribe<SpellbookChanged>(OnSpellbookChanged), events.Subscribe<ObjectCreated>(OnObjectCreated)];
    }

    /// <inheritdoc/>
    public uint PanelId => Page;

    /// <summary>The component list.</summary>
    public UiListBox List => _list;

    /// <summary>True while rows are still being built.</summary>
    public bool IsBuilding => _building;

    /// <summary>How many times the set of rows was rebuilt.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>How many times the page checked the pack (a redraw: it rebuilds nothing when the rows are the same).</summary>
    public int Redraws { get; private set; }

    /// <summary>The most rows (titles included) one frame built.</summary>
    public int MostBuiltInAFrame { get; private set; }

    /// <summary>The rows now: a category title (Wcid 0, Category set) or a component row, with the texts the list shows.</summary>
    public IEnumerable<(bool IsTitle, int Category, uint Wcid, string Name, string Count, string BuyRate)> Rows
    {
        get
        {
            for (var i = 0; i < _built.Count; i++)
            {
                var b = _built[i];
                yield return (b.Wcid == 0, b.Category, b.Wcid, b.NameText?.Text ?? b.TitleText?.Text ?? string.Empty, b.CountText?.Text ?? string.Empty, b.BuyText?.Text ?? string.Empty);
            }
        }
    }

    /// <summary>The row element of the component class <paramref name="wcid"/>, or null.</summary>
    public UiElement? RowOf(uint wcid)
    {
        for (var i = 0; i < _built.Count; i++)
        {
            if (_built[i].Wcid == wcid)
            {
                return _built[i].Element;
            }
        }

        return null;
    }

    /// <summary>The controller for the tab, or null (logged) when the page, the tab or the list box is missing from the layout.</summary>
    public static GameComponentsPage? Create(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        var tab = host.ElementFor(Page)?.GetChildRecursive(TabId);
        if (tab is null || tab.GetChildRecursive(ListId) is not UiListBox list)
        {
            context.Manager.Services.Log.Warn("retailui", "the components tab (0x100002AD) or its list box (0x10000464) is missing from the spell page; no controller (TODO(m3-components-page))");
            return null;
        }

        return new GameComponentsPage(context, tab, list);
    }

    /// <inheritdoc/>
    public void OnShown()
    {
        _pageShown = true;
        if (_tab.Visible)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public void OnHidden() => _pageShown = false;

    /// <inheritdoc/>
    public void Step(double now)
    {
        if (!_pageShown || !_tab.Visible)
        {
            return;
        }

        if (_pendingIcons.Count > 0)
        {
            RetryIcons();
        }

        if (_dirty)
        {
            Refresh();
        }

        if (_building)
        {
            BuildSome();
        }
    }

    /// <summary>Reads the pack and plans the rows now. When the rows are the ones already built only the counts and amounts that changed are rewritten.</summary>
    public void Refresh()
    {
        _dirty = false;
        Redraws++;
        Scan();
        if (SameStructure())
        {
            if (!_building)
            {
                UpdateValues();
            }

            return;
        }

        // The rows differ: start again (UpdateComponents adds and removes rows as the tracker changes; here the list is rebuilt).
        _list.Flush();
        _built.Clear();
        _pendingIcons.Clear(); // the rows waiting for a picture are gone
        _buildIndex = 0;
        _building = true;
        Rebuilds++;
    }

    /// <summary>Builds the next rows of the plan now (one frame's worth; <see cref="Step"/> calls it).</summary>
    public void BuildSome()
    {
        var built = 0;
        while (_buildIndex < _lines.Count && built < RowsPerFrame)
        {
            var line = _lines[_buildIndex];
            var row = _list.AddItemFromTemplateList(line.IsTitle ? TitleTemplate : RowTemplate);
            if (row is null)
            {
                _context.Manager.Services.Log.Warn("retailui", "gmSpellComponentUI: the list box has no row template; the components tab stays empty (TODO(m3-components-template))");
                _building = false;
                return;
            }

            _built.Add(line.IsTitle ? BuildTitle(row, line) : BuildRow(row, line));
            _buildIndex++;
            built++;
        }

        if (built > MostBuiltInAFrame)
        {
            MostBuiltInAFrame = built;
        }

        if (_buildIndex >= _lines.Count)
        {
            _building = false;
            UpdateValues();
        }
    }

    /// <summary>A buy-rate box lost the focus with the amount <paramref name="typed"/> (<c>ListenToElementMessage</c> 0x0048A700): the amount shown goes back and the NotInM3 message says it is not built.</summary>
    public void BuyRateEdited(uint wcid, string typed)
    {
        for (var i = 0; i < _built.Count; i++)
        {
            var row = _built[i];
            if (row.Wcid != wcid || row.BuyText is not { } box)
            {
                continue;
            }

            var shown = row.BuyShown.ToString(CultureInfo.InvariantCulture);
            if (typed.Trim() == shown)
            {
                return;
            }

            box.SetText(shown);
            if (!_buyTodoLogged)
            {
                _buyTodoLogged = true;
                _context.Manager.Services.Log.Warn("retailui", "TODO(m3-component-buy-rate): setting the desired amount of a component is not in M3 (no action for it); the buy-rate box answers NotInM3 and keeps the amount the character has");
            }

            NotInM3.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Component buy rate");
            return;
        }
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityChanged && ReferenceEquals(message.Element, _tab) && message.Param1 != 0)
        {
            Refresh(); // the tab was opened
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.Focus && message.Param1 == 0 && message.Element.Desc.ElementId == RowBuyRateId && message.Element is UiTextElement box)
        {
            for (var i = 0; i < _built.Count; i++)
            {
                if (ReferenceEquals(_built[i].BuyText, box))
                {
                    BuyRateEdited(_built[i].Wcid, box.Text);
                    break;
                }
            }
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
    }

    // ------------------------------------------------------------------ the pack

    /// <summary>Plans the lines: for each category with components a title, then one line per class (ordered by component id), counts summed over the stacks.</summary>
    private void Scan()
    {
        _lines.Clear();
        var session = _context.Session;
        var self = session.State.Self;
        var inventory = self.Inventory;
        var objects = session.State.Objects;
        var book = self.Spellbook;
        Collect(inventory.Pack, objects, book);
        var sides = inventory.SidePacks;
        for (var s = 0; s < sides.Count; s++)
        {
            Collect(inventory.ItemsIn(sides[s].Guid), objects, book);
        }

        // Order: category, component id, class id (an insertion sort: a handful of dozens, no delegate).
        for (var i = 1; i < _lines.Count; i++)
        {
            var item = _lines[i];
            var j = i - 1;
            while (j >= 0 && Line.Before(item, _lines[j]))
            {
                _lines[j + 1] = _lines[j];
                j--;
            }

            _lines[j + 1] = item;
        }

        if (book.DesiredComponentCount() > _lines.Count && !_zeroTodoLogged)
        {
            _zeroTodoLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "TODO(m3-component-zero-rows): the character wants to rebuy components it does not carry; a row is made only for a class in the pack (whether retail keeps a zero-count row is not read)");
        }

        // Insert a title before the first line of each category (in place, from the back).
        var previous = -1;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Category != previous)
            {
                previous = _lines[i].Category;
                _lines.Insert(i, Line.Title(previous));
                i++;
            }
        }
    }

    private void Collect(IReadOnlyList<InventoryEntry> entries, WorldObjectTable objects, Spellbook book)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (!objects.TryGet(entries[i].Guid, out var obj) || !obj.IsSpellComponent())
            {
                continue;
            }

            var wcid = obj.WeenieClassOf();
            var fields = obj.ItemFields();
            var count = Math.Max(fields.StackSize, 1);
            var at = -1;
            for (var k = 0; k < _lines.Count; k++)
            {
                if (_lines[k].Wcid == wcid)
                {
                    at = k;
                    break;
                }
            }

            if (at >= 0)
            {
                var existing = _lines[at];
                _lines[at] = existing with { Count = existing.Count + count };
                continue;
            }

            var category = SpellPageData.CategoryOf(wcid, out var componentId, out var known);
            if (!known && !_categoryTodoLogged)
            {
                _categoryTodoLogged = true;
                _context.Manager.Services.Log.Warn("retailui", $"TODO(m3-component-category): the component class {wcid} ({obj.Name}) is not in the pinned component map; it is listed under peas");
            }

            var tableIcon = known && _context.Session is ISpellComponentIcons icons ? icons.ComponentIcon(componentId) : 0u;
            if (tableIcon == 0 && fields.IconId != 0 && !_iconTodoLogged)
            {
                _iconTodoLogged = true;
                _context.Manager.Services.Log.Warn("retailui", $"TODO(m3-component-icon): the component class {wcid} ({obj.Name}) has no SpellComponentTable icon to read (the session gives none); the row draws the pack object's own icon in its place");
            }

            _lines.Add(new Line(false, category, componentId, wcid, obj.Name, tableIcon != 0 ? tableIcon : fields.IconId, count, book.DesiredComponentAmount(wcid), tableIcon != 0));
        }
    }

    /// <summary>True when the lines planned are the rows built (same kinds, categories and classes in the same order).</summary>
    private bool SameStructure()
    {
        if (_building || _built.Count != _lines.Count)
        {
            return false; // a build in progress starts again from the new plan
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            if (_built[i].Wcid != _lines[i].Wcid || _built[i].Category != _lines[i].Category)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateValues()
    {
        for (var i = 0; i < _built.Count; i++)
        {
            var line = _lines[i];
            if (line.IsTitle)
            {
                continue;
            }

            var row = _built[i];
            var changed = false;
            if (row.CountShown != line.Count)
            {
                row = row with { CountShown = line.Count };
                row.CountText?.SetText(line.Count.ToString(CultureInfo.InvariantCulture)); // SyncComponentRegionWithData: "%d"
                changed = true;
            }

            var desired = line.Desired is < 0 or > MaxBuyRate ? 0 : line.Desired; // UpdateBuyRates resets an amount outside 0 to 5000 to 0
            if (row.BuyShown != desired)
            {
                row = row with { BuyShown = desired };
                row.BuyText?.SetText(desired.ToString(CultureInfo.InvariantCulture));
                changed = true;
            }

            if (changed)
            {
                _built[i] = row;
            }
        }
    }

    // ------------------------------------------------------------------ the rows

    private Built BuildTitle(UiElement row, Line line)
    {
        var text = row as UiTextElement ?? FirstText(row);
        text?.SetText(TitleOf(line.Category));
        return new Built(row, 0, line.Category, null, null, null, text, -1, -1);
    }

    private Built BuildRow(UiElement row, Line line)
    {
        var name = row.GetChildRecursive(RowNameId) as UiTextElement;
        name?.SetText(line.Name);
        var icon = row.GetChildRecursive(RowIconId);
        if (line.IconId != 0 && icon is not null)
        {
            if (line.TableIcon)
            {
                ShowComponentIcon(icon, line.IconId); // MakeIcon (0x0048A250)
            }
            else
            {
                icon.SetImageByDid(line.IconId, UiDrawMode.AlphaBlend); // the stand-in of TODO(m3-component-icon): the pack object's own icon, not composed
            }
        }

        return new Built(row, line.Wcid, line.Category, name, row.GetChildRecursive(RowCountId) as UiTextElement, row.GetChildRecursive(RowBuyRateId) as UiTextElement, null, -1, -1);
    }

    /// <summary>
    /// <c>gmSpellComponentUI::MakeIcon</c> (11.6096 0x0048A250): the component's composed picture (<c>ClientMagicSystem::GetSpellComponentIcon</c> 0x00569A50 over <c>CompositeSpellComponentIcon</c> 0x005684C0: the
    /// SpellComponentTable icon with its pure white pixels made black; <see cref="UiSpellIcons"/>, M5 D147) drawn with three-channel alpha. A picture still decoding is drawn on a later frame.
    /// </summary>
    private void ShowComponentIcon(UiElement icon, uint did)
    {
        switch (_context.Manager.SpellIcons.TryGetComponent(did, out var picture))
        {
            case UiImageLookup.Ready:
                icon.SetBlitMode(UiBlitMode.ThreeChannelAlpha);
                icon.SetGeneratedImage(picture!);
                break;
            case UiImageLookup.Pending:
                _pendingIcons.Add((icon, did));
                break;
            default:
                _context.Manager.LogOnce("components:no-images", LogSeverity.Warning, "gmSpellComponentUI::MakeIcon: no image services; the component rows draw no icon");
                break;
        }
    }

    private void RetryIcons()
    {
        var pending = _pendingIcons.ToArray();
        _pendingIcons.Clear();
        foreach (var (icon, did) in pending)
        {
            ShowComponentIcon(icon, did);
        }
    }

    private string TitleOf(int category)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(TableEnum, CategoryTokens[category], out var reference) && strings.TryGetString(reference, out var text))
        {
            return text;
        }

        if (!_titleTodoLogged)
        {
            _titleTodoLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "gmSpellComponentUI: a category title string does not render in the string tables; the English name stands in (TODO(m3-component-strings))");
        }

        return CategoryFallbacks[category];
    }

    private static UiTextElement? FirstText(UiElement element)
    {
        if (element is UiTextElement self)
        {
            return self;
        }

        for (var i = 0; i < element.Children.Count; i++)
        {
            if (FirstText(element.Children[i]) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnInventoryChanged(in InventoryChanged change) => _dirty = true;

    private void OnObjectCreated(in ObjectCreated created)
    {
        if (_context.Session.State.Self.Inventory.Contains(created.Guid))
        {
            _dirty = true; // the item was in the pack before its object arrived
        }
    }

    private void OnSpellbookChanged(in SpellbookChanged change) => _dirty = true;

    /// <summary>A line of the plan: a category title (<see cref="IsTitle"/>) or a component class with its summed count and desired amount.</summary>
    private readonly record struct Line(bool IsTitle, int Category, uint ComponentId, uint Wcid, string Name, uint IconId, int Count, int Desired, bool TableIcon = false)
    {
        public static Line Title(int category) => new(true, category, 0, 0, string.Empty, 0, 0, 0);

        public static bool Before(Line a, Line b) => a.Category != b.Category ? a.Category < b.Category : a.ComponentId != b.ComponentId ? a.ComponentId < b.ComponentId : a.Wcid < b.Wcid;
    }

    /// <summary>A row built into the list, with the texts it shows now.</summary>
    private readonly record struct Built(UiElement Element, uint Wcid, int Category, UiTextElement? NameText, UiTextElement? CountText, UiTextElement? BuyText, UiTextElement? TitleText, int CountShown, int BuyShown);
}
