using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The spellbook tab of the spell page (panel 13, <c>gmSpellbookUI</c> 0x1000002E, tab element 0x100002AC; M3 2.2 slice b3, docs/milestones/m3-plan.md section 4.6): the spells the character knows
/// (<see cref="Spellbook.Spells"/>) as icons on the item list 0x10000295, in retail's order, behind the school and level filter buttons. A click on a spell selects it; the delete button answers NotInM3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>gmSpellbookUI::UpdateFromPlayerDesc</c> (11.6096 0x0048BDD0) empties the list, asks the description for the spell ids (<c>CACQualities::TranscribeSpells</c>), adds each with
/// <c>AddSpell</c> (0x0048BA80: the spell's base from the SpellTable, dropped when <c>IsFilteredOut</c> (0x0048B190) says so, otherwise inserted at <c>GetSortedInsertionPlace</c> (0x0048B720): before the first cell whose
/// spell has a greater display order) and resets the filter buttons (<c>ResetFilterButtons</c>, 0x0048B270: button state 6, highlight, when the filter's bit is set, else 1). <c>IsFilteredOut</c> shows a spell when the
/// word has its school's bit (creature 1, item 2, life 4, war 8, void 0x2000) and the bit of its level by <c>CSpellBase::InqSpellLevelByRoughHeuristic</c> (11.6096 0x005981D0) (levels 1 to 9: 0x10 up to 0x800, then 0x1000); a spell whose
/// level the heuristic does not give (a first component that is no scarab) is filtered out. A click on a filter button (<c>ListenToElementMessage</c>, 0x0048C2B0 over <c>UpdateFilter</c>, 0x0048B8C0) sets or clears the
/// bit, sends it to the server (<c>CM_Character::Event_SpellbookFilterEvent</c>), rebuilds the list and scrolls to its top. A new spell (<c>RecvNotice_SpellAdded</c>, 0x0048BBB0) is added, selected
/// (<c>SetSelected</c>, 0x0048B820) and scrolled into view. The delete button (<c>DeleteSpell</c>, 0x0048C030) asks the server to forget the selected spell.
/// </para>
/// <para>
/// <b>Lifestone.</b> The layout shows no selected-spell name or details (its two texts are the "Spell Schools" and "Spell Levels" captions), so a selection is the cell's highlight only. The list is planned when the tab
/// shows or the book changes (the known ids filtered and sorted by display order, ties by spell id: retail's tie order is its hash table's), then written over frames (<see cref="SpellsPerFrame"/> a frame, in place: a cell that
/// already shows the right spell is left alone) so the 247 spells of a mage cost no frame more than a few milliseconds. The spell names and icons come from the SpellTable through
/// <see cref="IGameSessionView.Spell"/>, which answers null until the table has loaded: the list stays empty until it does and fills when it arrives (never a guess). A filter click is session-local, as the options are
/// (M2 D42): no <c>SpellbookFilterEvent</c> is sent (<c>TODO(m3-spellbook-filter-send)</c>: there is no action for it), so the server's word comes back only with the next description. Delete spell and the favourite-bar
/// shortcut of a double click are not built (<c>TODO(m3-spellbook-delete)</c>, <c>TODO(m3-spellbook-shortcut)</c>: the spellcasting panel is item 3.2's). A spell is dragged from the list onto a spell bar (M5 6 s1:
/// the list is a drag source; the drop is <see cref="GameSpellcastingPanel"/>'s).
/// </para>
/// </remarks>
public sealed class GameSpellbookPage : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The panel id of the spell page.</summary>
    public const uint Page = 13;

    /// <summary>The tab's element, <c>gmSpellbookUI</c> (class 0x1000002E).</summary>
    public const uint TabId = 0x100002AC;

    /// <summary>The class id of <c>gmSpellbookUI</c>.</summary>
    public const uint ClassId = 0x1000002E;

    /// <summary>The spell list (0x10000295).</summary>
    public const uint ListId = 0x10000295;

    /// <summary>The delete-spell button (0x100002A5).</summary>
    public const uint DeleteButtonId = 0x100002A5;

    /// <summary>How many spells are written to the list each frame.</summary>
    public const int SpellsPerFrame = 8;

    /// <summary>How many surplus cells are deleted each frame after a filter narrowed the list.</summary>
    public const int TrimPerFrame = 24;

    private static readonly (uint Id, uint Bit)[] FilterButtons =
    [
        (0x10000298, 0x1), // creature
        (0x10000299, 0x2), // item
        (0x1000029A, 0x4), // life
        (0x1000029B, 0x8), // war
        (0x100005C0, 0x2000), // void
        (0x1000029C, 0x10), // level 1
        (0x1000029D, 0x20),
        (0x1000029E, 0x40),
        (0x1000029F, 0x80),
        (0x100002A0, 0x100),
        (0x100002A1, 0x200),
        (0x100002A2, 0x400),
        (0x1000054E, 0x800), // level 8
    ];

    private readonly UiLoginContext _context;
    private readonly UiElement _tab;
    private readonly UiItemList _list;
    private readonly UiButton?[] _buttons = new UiButton?[FilterButtons.Length];
    private readonly IDisposable _subscription;
    private readonly List<PlanItem> _plan = new(256);
    private readonly Func<uint, UiSpellIcon?> _spellIcon; // the picture fields of a dragged spell (UiDrag.SpellIconOf)
    private bool _pageShown;
    private bool _dirty = true;
    private bool _awaitingTable;
    private bool _filling;
    private int _fillIndex;
    private bool _filtersSeeded;
    private uint _seenServerFilters;
    private uint _filters;
    private uint _selectedSpell;
    private bool _scrollToTop;
    private bool _scrollToSelected;
    private bool _deleteTodoLogged;
    private bool _unknownSpellLogged;
    private uint _probe;

    private GameSpellbookPage(UiLoginContext context, UiElement tab, UiItemList list)
    {
        _context = context;
        _tab = tab;
        _list = list;
        for (var i = 0; i < FilterButtons.Length; i++)
        {
            _buttons[i] = tab.GetChildRecursive(FilterButtons[i].Id) as UiButton;
        }

        tab.RegisterForElementMessages(this);
        _list.ItemSelected += OnSpellSelected;
        _list.DragSource = true; // M5 6 s1: a spell is dragged from the book onto a spell bar (gmSpellcastingUI::HandleDropRelease 0x004C8520); every item target refuses it
        _spellIcon = id => context.Session.Spell(id) is { } spell ? UiSpellIcon.Of(spell) : null;
        context.Manager.Input.Drag.SpellIconOf ??= _spellIcon;
        _subscription = context.Session.Events.Subscribe<SpellbookChanged>(OnSpellbookChanged);
    }

    /// <inheritdoc/>
    public uint PanelId => Page;

    /// <summary>The spell list.</summary>
    public UiItemList List => _list;

    /// <summary>The filter word the buttons show now (the book's, or a button click's).</summary>
    public uint Filters => _filters;

    /// <summary>The spell selected (0 for none).</summary>
    public uint SelectedSpell => _selectedSpell;

    /// <summary>True while the list is still being written (or waiting for the SpellTable).</summary>
    public bool IsFilling => _filling || _awaitingTable;

    /// <summary>True while the list waits for the SpellTable to load (<see cref="IGameSessionView.Spell"/> answers null).</summary>
    public bool AwaitingSpellTable => _awaitingTable;

    /// <summary>How many spells the plan holds (the known spells that pass the filters), once planned.</summary>
    public int PlannedCount => _plan.Count;

    /// <summary>How many times the list was planned (a change of the book or a filter plans it again).</summary>
    public int Plans { get; private set; }

    /// <summary>The largest number of spells one frame wrote (the test checks it against <see cref="SpellsPerFrame"/>).</summary>
    public int MostWrittenInAFrame { get; private set; }

    /// <summary>The ids the list shows, in list order.</summary>
    public IEnumerable<uint> ShownSpells
    {
        get
        {
            for (var i = 0; i < _list.CellCount; i++)
            {
                if (_list.CellAt(i) is { SpellId: not 0 } cell)
                {
                    yield return cell.SpellId;
                }
            }
        }
    }

    /// <summary>The filter button for <paramref name="bit"/> (0x1 creature ... 0x800 level 8), or null.</summary>
    public UiButton? ButtonFor(uint bit)
    {
        for (var i = 0; i < FilterButtons.Length; i++)
        {
            if (FilterButtons[i].Bit == bit)
            {
                return _buttons[i];
            }
        }

        return null;
    }

    /// <summary>The controller for the tab, or null (logged) when the page, the tab or the list is missing from the layout or the item list class is not registered.</summary>
    public static GameSpellbookPage? Create(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        var tab = host.ElementFor(Page)?.GetChildRecursive(TabId);
        if (tab is null || tab.GetChildRecursive(ListId) is not UiItemList list)
        {
            context.Manager.Services.Log.Warn("retailui", "the spellbook tab (0x100002AC) or its item list (0x10000295) is missing from the spell page, or UIElement_ItemList is not registered; no controller (TODO(m3-spellbook-page))");
            return null;
        }

        return new GameSpellbookPage(context, tab, list);
    }

    /// <summary>
    /// <c>IsFilteredOut</c> (11.6096 0x0048B190): true when the spell of <paramref name="school"/> and rough <paramref name="level"/> is hidden by the filter word <paramref name="filters"/> (only its low 16 bits count).
    /// </summary>
    public static bool IsFilteredOut(uint filters, uint school, int level)
    {
        var word = filters & 0xFFFF;
        var schoolBit = school switch
        {
            4 => 0x1u,
            3 => 0x2u,
            2 => 0x4u,
            1 => 0x8u,
            5 => 0x2000u,
            _ => 0u,
        };
        if (schoolBit == 0 || (word & schoolBit) == 0)
        {
            return true;
        }

        var levelBit = level switch
        {
            1 => 0x10u,
            2 => 0x20u,
            3 => 0x40u,
            4 => 0x80u,
            5 => 0x100u,
            6 => 0x200u,
            7 => 0x400u,
            8 => 0x800u,
            9 => 0x1000u,
            _ => 0u,
        };
        return levelBit == 0 || (word & levelBit) == 0;
    }

    /// <summary><see cref="IsFilteredOut(uint, uint, int)"/> for a SpellTable spell (its school and <c>InqSpellLevelByRoughHeuristic</c>, 11.6096 0x005981D0).</summary>
    public static bool IsFilteredOut(uint filters, SpellInfo spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return IsFilteredOut(filters, (uint)spell.School, SpellFormulaRules.LevelByRoughHeuristic(spell.Formula));
    }

    /// <summary>The ids of the known spells that pass <paramref name="filters"/>, in the order the list shows them (display order, then id), for the tests and the play check.</summary>
    public static List<uint> SortedSpells(Spellbook book, IGameSessionView session, uint filters)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(session);
        var items = new List<PlanItem>(book.Count);
        foreach (var id in book.Spells.Keys)
        {
            if (session.Spell(id) is { } info && !IsFilteredOut(filters, info))
            {
                items.Add(new PlanItem(info.DisplayOrder, id, UiSpellIcon.Of(info), info.Name));
            }
        }

        items.Sort(PlanItem.Compare);
        var ids = new List<uint>(items.Count);
        foreach (var item in items)
        {
            ids.Add(item.SpellId);
        }

        return ids;
    }

    /// <inheritdoc/>
    public void OnShown()
    {
        _pageShown = true;
        if (_tab.Visible)
        {
            _list.BeginFill();
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

        if (_awaitingTable)
        {
            // The SpellTable decodes on a worker; ask again each frame until it answers (a dictionary lookup).
            if (TableReady())
            {
                _dirty = true;
            }
        }

        if (_dirty)
        {
            Refresh();
        }

        if (_filling)
        {
            FillSome();
        }
    }

    /// <summary>Plans the list from the session now (the tests and the play check call it; the page calls it as it shows and on a change).</summary>
    public void Refresh()
    {
        _dirty = false;
        var session = _context.Session;
        var book = session.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            // The description has not arrived: nothing to list, nothing to guess.
            _plan.Clear();
            _filling = true; // FillSome clears what a previous book left
            _fillIndex = 0;
            _awaitingTable = false;
            _probe = 0;
            return;
        }

        if (!_filtersSeeded || book.Filters != _seenServerFilters)
        {
            _filtersSeeded = true;
            _seenServerFilters = book.Filters;
            _filters = book.Filters;
            ResetFilterButtons();
        }

        _plan.Clear();
        _awaitingTable = false;
        _probe = 0;
        foreach (var id in book.Spells.Keys)
        {
            if (_probe == 0)
            {
                _probe = id;
            }

            var info = session.Spell(id);
            if (info is null)
            {
                if (session.Spell(_probe) is null)
                {
                    _awaitingTable = true;
                    break;
                }

                if (!_unknownSpellLogged)
                {
                    _unknownSpellLogged = true;
                    _context.Manager.Services.Log.Warn("retailui", $"TODO(m3-spellbook-unknown-spell): spell {id} is in the book but not in the SpellTable; retail's InqSpellBase fails for it and the spell is left out of the list");
                }

                continue;
            }

            if (!IsFilteredOut(_filters, info))
            {
                _plan.Add(new PlanItem(info.DisplayOrder, id, UiSpellIcon.Of(info), info.Name));
            }
        }

        if (_awaitingTable)
        {
            _plan.Clear();
            _filling = false;
            return;
        }

        _plan.Sort(PlanItem.Compare);
        Plans++;
        _fillIndex = 0;
        _filling = true;
    }

    /// <summary>
    /// Writes the next spells of the plan into the list now (one frame's worth; <see cref="Step"/> calls it).
    /// <c>TODO(m3-spellbook-first-paint)</c> (4.2 x4, from the code; the live frame is the lead's check): the compositor paints a window's surface only when every image in its dirty area is decoded
    /// (<c>UiCompositor.ImagesReady</c>), and the game's image source decodes on a worker, so cells written ahead of their icons kept the surface unpainted and the previous page on screen for as long as the fill
    /// kept adding undecoded icons (247 spells, 8 a frame). A cell is now written only once its icon has decoded: the next batch's icons are requested ahead, the fill stops at the first one still pending
    /// and goes on next frame, so the page area paints (its frame, then the spells as they come) from the first frame it is open.
    /// </summary>
    public void FillSome()
    {
        var limit = Math.Min(_plan.Count, _fillIndex + SpellsPerFrame);
        var look = Math.Min(_plan.Count, limit + SpellsPerFrame);
        var end = _fillIndex;
        var stalled = false;
        for (var i = _fillIndex; i < look; i++)
        {
            var pending = IconPending(_plan[i].Icon); // also asks the source to start decoding its layers (and composes it when they are ready)
            if (i < limit && !stalled)
            {
                if (pending)
                {
                    stalled = true;
                }
                else
                {
                    end = i + 1;
                }
            }
        }

        var written = 0;
        if (end > _fillIndex)
        {
            _list.EnsureCells(end);
            for (var i = _fillIndex; i < end; i++)
            {
                if (_list.CellAt(i) is not { } cell)
                {
                    break; // the template or layout is missing: the list logs it and stays short
                }

                var item = _plan[i];
                if (cell.SpellId != item.SpellId)
                {
                    cell.ShowSpell(item.Icon, item.Name);
                    written++;
                }

                cell.SetSelected(item.SpellId == _selectedSpell);
                _fillIndex = i + 1;
            }
        }

        if (written > MostWrittenInAFrame)
        {
            MostWrittenInAFrame = written;
        }

        if (_fillIndex < _plan.Count)
        {
            return;
        }

        // Done writing: the cells beyond the plan go (a filter narrowed the list), a few a frame.
        var trim = TrimPerFrame;
        while (_list.CellCount > _plan.Count && trim-- > 0)
        {
            _list.DeleteItem(_list.CellCount - 1);
        }

        if (_list.CellCount > _plan.Count)
        {
            return;
        }

        _filling = false;
        if (_scrollToTop)
        {
            _scrollToTop = false;
            _scrollToSelected = false;
            if (_list.CellCount > 0)
            {
                _list.ScrollToShow(0); // UpdateFilter's caller scrolls to the first row (ListenToElementMessage, 0x0048C2B0)
            }
        }
        else if (_scrollToSelected)
        {
            _scrollToSelected = false;
            var index = IndexOfPlanned(_selectedSpell);
            if (index >= 0)
            {
                _list.ScrollToShow(index); // SetSelected scrolls the new spell into view (0x0048B820)
            }
        }
    }

    /// <summary>A click on a filter button, as the listener hears it; the tests call it after toggling the button.</summary>
    public void FilterClicked(uint buttonId)
    {
        for (var i = 0; i < FilterButtons.Length; i++)
        {
            if (FilterButtons[i].Id != buttonId || _buttons[i] is not { } button)
            {
                continue;
            }

            // UpdateFilter (0x0048B8C0): the button's state after the click (6, highlighted: the bit is set) sets or clears its bit; a changed word rebuilds the list from the top.
            var bit = FilterButtons[i].Bit;
            var word = button.Highlighted ? _filters | bit : _filters & ~bit;
            if (word != _filters)
            {
                _filters = word;
                _scrollToTop = true;
                _dirty = true;
            }

            return;
        }
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityChanged && ReferenceEquals(message.Element, _tab) && message.Param1 != 0)
        {
            _list.BeginFill();
            Refresh(); // the tab was opened
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            var id = message.Element.Desc.ElementId;
            if (id == DeleteButtonId)
            {
                DeleteSpell();
                return UiListenResult.Handled;
            }

            FilterClicked(id);
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmSpellbookUI::DeleteSpell</c> (11.6096 0x0048C030): the server forgets the selected spell after a confirmation. Not built in M3: the answer is the NotInM3 message.</summary>
    public void DeleteSpell()
    {
        if (!_deleteTodoLogged)
        {
            _deleteTodoLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "TODO(m3-spellbook-delete): deleting a spell is not in M3 (no action for it); the button answers NotInM3");
        }

        NotInM3.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, "Delete spell");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _subscription.Dispose();
        _list.ItemSelected -= OnSpellSelected;
        _list.DragSource = false;
        if (ReferenceEquals(_context.Manager.Input.Drag.SpellIconOf, _spellIcon))
        {
            _context.Manager.Input.Drag.SpellIconOf = null;
        }
    }

    /// <summary>True while a layer of <paramref name="icon"/>'s composed picture is still decoding (never when the page has no source, as in a host that decodes at once); once they are ready the picture is composed and kept (<see cref="UiSpellIcons"/>).</summary>
    private bool IconPending(in UiSpellIcon icon) => _context.Manager.SpellIcons.TryGetSpell(icon, out _) == UiImageLookup.Pending;

    /// <summary>The table answers null for every id until it has loaded; probe with the first id the book held when it was planned (no enumerator per frame).</summary>
    private bool TableReady() => _probe == 0 || _context.Session.Spell(_probe) is not null;

    private int IndexOfPlanned(uint spellId)
    {
        for (var i = 0; i < _plan.Count; i++)
        {
            if (_plan[i].SpellId == spellId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary><c>ResetFilterButtons</c> (0x0048B270): each button in state 6 (highlight) when its bit is set, else 1 (normal).</summary>
    private void ResetFilterButtons()
    {
        for (var i = 0; i < FilterButtons.Length; i++)
        {
            _buttons[i]?.SetState((_filters & FilterButtons[i].Bit) != 0 ? UiStateIds.Highlight : UiStateIds.Normal);
        }
    }

    private void OnSpellbookChanged(in SpellbookChanged change)
    {
        _dirty = true;
        if (change is { Change: SpellbookChange.Learned, IsNew: true })
        {
            // RecvNotice_SpellAdded (0x0048BBB0): the new spell is selected and scrolled into view.
            _selectedSpell = change.SpellId;
            _scrollToSelected = true;
        }
    }

    /// <summary><c>ListenToElementMessage</c> (0x0048C2B0), a click on a cell: <c>SetSelected</c> (0x0048B820) highlights that spell's cell and clears the others.</summary>
    private void OnSpellSelected(UiItemList list, UiItem cell)
    {
        if (cell.SpellId == 0)
        {
            return;
        }

        _selectedSpell = cell.SpellId;
        for (var i = 0; i < list.CellCount; i++)
        {
            if (list.CellAt(i) is { } other)
            {
                other.SetSelected(other.SpellId == _selectedSpell);
            }
        }
    }

    /// <summary>One spell of the plan: its display order (<c>CSpellBase::_display_order</c>), id, picture fields (<see cref="UiSpellIcon"/>) and name.</summary>
    public readonly record struct PlanItem(uint DisplayOrder, uint SpellId, UiSpellIcon Icon, string Name)
    {
        /// <summary>Display order, then spell id.</summary>
        public static int Compare(PlanItem a, PlanItem b) => a.DisplayOrder != b.DisplayOrder ? a.DisplayOrder.CompareTo(b.DisplayOrder) : a.SpellId.CompareTo(b.SpellId);
    }
}
