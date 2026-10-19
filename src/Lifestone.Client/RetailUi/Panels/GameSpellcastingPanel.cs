using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The spellcasting window (<c>gmSpellcastingUI</c>, class 0x10000015, window 0x10000061 of the gameplay layout, inside <see cref="UiFloatyCombatPanel"/>; M3 3.2 slice pb, docs/milestones/m3-plan.md section 4.8). Retail's class is an
/// element of its own (34 functions, 11.6096 0x004C6400 to 0x004C86A0 for 11.4186's 0x004C5810 to 0x004C7AB0), so Lifestone's is too: registered for the class, it finds its panel, its eight spell pages and its cast button, and
/// follows the session (<see cref="Attach"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shown by the mode</b> (<c>RecvNotice_SetCombatMode</c>, 11.6096 0x004C7E70): the Magic combat mode (8) shows the window, updates the endowment icon and the cast button's tooltip; any other mode hides it. The layout shows it by
/// default, so <see cref="Attach"/> hides it unless the session is in Magic mode now. The floaty that holds it follows (<see cref="UiFloatyCombatPanel"/>). A hidden window is not drawn and is not rebuilt: a change of the bars waits
/// for the next time it shows.
/// </para>
/// <para>
/// <b>The bars</b> (<c>SpellCastSubMenu::UpdateFromPlayerModule</c>, 11.6096 0x004C7160): the window holds eight tabs (<c>PostInit</c>, 11.6096 0x004C68F0, pairs page 0x100000AA to 0x100000B0 and 0x100005C3 with tab 0x100000A3 to 0x100000A9
/// and 0x100005C2), one <see cref="Spellbook.SpellBars"/> bar each; the tab's item list 0x100000B6 shows the bar's spells in slot order, each with its icon and its name from the SpellTable and the tooltip "name, new line,
/// Double-click to cast this spell". Retail first drops the favourites the character no longer knows from its player module; Lifestone only leaves them out of the list (it writes nothing, <c>TODO(m3-spellcast-unknown-favourite)</c>).
/// Only the shown tab is built; a tab is built again when its bar (or how many of its spells the character knows) changed since it was built, when its page opens, or when the SpellTable arrives (the list waits for it, never a guess). A build
/// writes in place (a cell already showing the right spell is left alone) and nothing here runs per frame except the one-shot poll while the SpellTable is missing. Readiness is the table's own
/// (<see cref="IGameSessionView.SpellTableReady"/>), never a favourite's: a known favourite a loaded table lacks (a custom server's spell, the stand-in's probe id) is left out and named once, and the tab builds with the rest
/// (<c>TODO(m3-spellcast-unknown-spell)</c>; M3 3.2 review finding 9, which probed the bar's first known spell and never built the tab when that spell was the missing one).
/// </para>
/// <para>
/// <b>Selecting and casting</b> (<c>ListenToElementMessage</c>, 11.6096 0x004C86A0 over <c>Cast</c>, 0x004C6C40): a left click on a spell selects it (<c>SpellCastSubMenu::SetSelected</c>, 0x004C66F0: the cell highlights, the name
/// text and the cast button's tooltip follow, <c>UpdateCastButtonTooltip</c> 0x004C7620); a double click selects and casts; the cast button (0x100000B2) casts the selected spell, and with none selected the chat line "You must select
/// a spell to cast". <c>ClientMagicSystem::CastSpell</c> (11.6096 0x00568DE0) decides the cast: a spell with the SelfTargeted flag is cast at the player (<c>FreeHandsAndCastSpell</c> with the player's id, a targeted cast), any other
/// at the selected object, and with none selected the chat line "You must select a suitable target before casting this spell". Lifestone sends it through <see cref="IActionApi.Cast(uint, uint, ActionSource)"/> as
/// <see cref="ActionSource.Ui"/>.
/// </para>
/// <para>
/// <b>Deviations (named).</b> <c>TODO(m3-cast-untargeted)</c>: retail asks the spell's formula for its targeting type (<c>CSpellBase::IsUntargeted</c> 11.6096 0x00598410 over <c>SpellFormula::GetTargetingType</c>) and casts a
/// spell with no target type with no target; the client keeps no component table of target types, so every spell that is not SelfTargeted needs a selection. <c>TODO(m3-cast-components)</c>: retail refuses a spell whose
/// components the pack lacks before sending ("You do not have all of this spell's components"); ACE answers the same through the cast's UseDone, which the action reports. <c>TODO(m3-cast-compat)</c>:
/// <c>ObjectCompatibleWithSpell</c> (the target must suit the spell) is ACE's to judge. <c>TODO(m3-spellcast-selection-notice)</c>: the cast button's tooltip follows the spell selection, not the object selection (the shell
/// polls that one). <c>TODO(m3-spellcast-endowment)</c>: the endowment icon (a wielded item's own spell) is hidden. <c>TODO(m3-spellcast-hotkeys)</c>: next and previous tab and spell, the quickslots, the drop handler and the
/// favourite add/remove by drag are not built (D13: no drag; <c>AddSpellFavorite</c> is an action the play-checks send).
/// </para>
/// </remarks>
public sealed class GameSpellcastingPanel : UiElement
{
    /// <summary>The retail class id (<c>gmSpellcastingUI</c>, Register 11.6096 0x004C6C20).</summary>
    public const uint RetailClassId = 0x10000015;

    /// <summary><c>--video-hide-floaties</c> (video capture only, off by default): the Magic mode does not show the window, as any other mode does not. Set once at start-up by the main scene.</summary>
    public static bool HideForVideo { get; set; }

    /// <summary>The window's element id in the gameplay layout.</summary>
    public const uint WindowId = 0x10000061;

    /// <summary>The panel that holds the eight pages and their tabs.</summary>
    public const uint PanelElementId = 0x100000A2;

    /// <summary>The text that names the selected spell.</summary>
    public const uint SpellNameId = 0x1000048B;

    /// <summary>The cast button.</summary>
    public const uint CastButtonId = 0x100000B2;

    /// <summary>The endowment icon.</summary>
    public const uint EndowmentIconId = 0x100000B1;

    /// <summary>The spell item list of each page.</summary>
    public const uint ListId = 0x100000B6;

    /// <summary>The number of tabs (and of favourite bars).</summary>
    public const int TabCount = 8;

    /// <summary>The page element of each tab, in tab order.</summary>
    public static ImmutableArray<uint> PageIds { get; } = [0x100000AA, 0x100000AB, 0x100000AC, 0x100000AD, 0x100000AE, 0x100000AF, 0x100000B0, 0x100005C3];

    /// <summary>The tab element (its caption) of each tab, in tab order.</summary>
    public static ImmutableArray<uint> TabIds { get; } = [0x100000A3, 0x100000A4, 0x100000A5, 0x100000A6, 0x100000A7, 0x100000A8, 0x100000A9, 0x100005C2];

    /// <summary>What a spell cell's tooltip adds under the spell's name (<c>UpdateFromPlayerModule</c>).</summary>
    public const string CastHint = "\nDouble-click to cast this spell";

    /// <summary>The chat line of a targeted spell with nothing selected (<c>ClientMagicSystem::CastSpell</c>).</summary>
    public const string NoTargetText = "You must select a suitable target before casting this spell";

    /// <summary>The chat line of the cast button with no spell selected (<c>gmSpellcastingUI::Cast</c>).</summary>
    public const string NoSpellText = "You must select a spell to cast";

    private readonly UiItemList?[] _lists = new UiItemList?[TabCount];
    private readonly ImmutableArray<uint>[] _built = new ImmutableArray<uint>[TabCount];
    private readonly bool[] _dirty = new bool[TabCount];
    private readonly uint[] _selected = new uint[TabCount];
    private UiLoginContext? _context;
    private UiPanel? _panel;
    private UiTextElement? _name;
    private UiButton? _castButton;
    private IDisposable? _bookSubscription;
    private IDisposable? _modeSubscription;
    private IDisposable? _qualitySubscription;
    private readonly int[] _builtKnown = new int[TabCount];
    private HashSet<uint>? _unknownNamed; // the favourites a loaded table lacked, each named once (allocated at a build, an event)
    private bool _awaitingTable;
    private bool _selectionTodoLogged;

    /// <summary>A spellcasting window of <paramref name="init"/>.</summary>
    public GameSpellcastingPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The combat mode the window last took (<see cref="CombatModes"/>; 0 before any).</summary>
    public int Mode { get; private set; }

    /// <summary>The object selection (<c>ACCWeenieObject::selectedID</c>, 0 for none), read when a targeted spell is cast. The play mode's selection reaches it through the shell's <see cref="Screens.GamePlayScreen.SelectionUi"/>; null is a named, logged TODO at the first targeted cast.</summary>
    public Func<uint>? SelectedProvider { get; set; }

    /// <summary>Where the chat lines go (<c>ECM_UI::SendNotice_DisplayStringInfo</c>); null only logs them.</summary>
    public Action<string>? ChatNotice { get; set; }

    /// <summary>The open tab (0 to 7).</summary>
    public int OpenTab
    {
        get
        {
            var index = _panel is null ? -1 : PageIds.IndexOf(_panel.OpenPageId);
            return index < 0 ? 0 : index;
        }
    }

    /// <summary>The first tab (0 to 7) whose bar holds a spell the character knows, or -1 when none does or the window has no session. The panel shot opens it (M3 3.2 fix B, 3.2-R7); play keeps retail's own opening tab.</summary>
    public int FirstTabWithSpells()
    {
        if (_context is null)
        {
            return -1;
        }

        var book = _context.Session.State.Self.Spellbook;
        for (var tab = 0; tab < TabCount; tab++)
        {
            if (KnownOn(book.Bar(tab), book) > 0)
            {
                return tab;
            }
        }

        return -1;
    }

    /// <summary>Opens tab <paramref name="tab"/> as a click on its tab button does (<c>UIElement_Panel::OpenTab</c>; the panel's tab message brings the tab up to date). True when it is the open tab afterwards.</summary>
    public bool OpenTabByClick(int tab) => _panel is not null && tab is >= 0 and < TabCount && (_panel.OpenTabId == TabIds[tab] || _panel.OpenTab(TabIds[tab])) && OpenTab == tab;

    /// <summary>True while the SpellTable has not loaded and the shown tab waits for it.</summary>
    public bool AwaitingSpellTable => _awaitingTable;

    /// <summary>How many tab builds ran (a tab built again counts again; a hidden window, an unchanged bar and an unchanged tab build nothing).</summary>
    public int Builds { get; private set; }

    /// <summary>How many casts were asked of the Action API.</summary>
    public int Casts { get; private set; }

    /// <summary>The last cast the window asked for, or null.</summary>
    public ActionHandle? LastCast { get; private set; }

    /// <summary>The spell selected on the open tab (0 for none).</summary>
    public uint SelectedSpell => _selected[OpenTab];

    /// <summary>The cast button (null when the layout lacks it).</summary>
    public UiButton? CastButton => _castButton;

    /// <summary>The name text (null when the layout lacks it).</summary>
    public UiTextElement? NameText => _name;

    /// <summary>The tab element (its caption) of tab <paramref name="tab"/>, or null.</summary>
    public UiElement? TabElement(int tab) => tab is >= 0 and < TabCount ? GetChildRecursive(TabIds[tab]) : null;

    /// <summary>The spell list of tab <paramref name="tab"/>, or null.</summary>
    public UiItemList? ListOf(int tab) => tab is >= 0 and < TabCount ? _lists[tab] : null;

    /// <summary>The spells tab <paramref name="tab"/>'s list shows, in cell order (allocates; for tests and checks).</summary>
    public List<uint> ShownSpells(int tab)
    {
        var shown = new List<uint>();
        if (ListOf(tab) is { } list)
        {
            for (var i = 0; i < list.CellCount; i++)
            {
                if (list.CellAt(i) is { SpellId: not 0 } cell)
                {
                    shown.Add(cell.SpellId);
                }
            }
        }

        return shown;
    }

    /// <summary>
    /// <c>PostInit</c> (11.6096 0x004C68F0): finds the panel, the name text, the cast button and the eight lists, and hides the endowment icon. Subscribes to nothing until <see cref="Attach"/>. The window also hides itself (and the floaty that holds it follows) so that no frame of the shell's staged build draws it in NonCombat:
    /// the layout shows it by default and <see cref="Attach"/> (in a unit after the shell's other controllers) is what shows it by mode (3.2-R9).
    /// </summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        _panel = GetChildRecursive(PanelElementId) as UiPanel;
        _name = GetChildRecursive(SpellNameId) as UiTextElement;
        _castButton = GetChildRecursive(CastButtonId) as UiButton;
        GetChildRecursive(EndowmentIconId)?.SetVisible(false);
        SetVisible(false);
        (Parent as UiFloatyCombatPanel)?.UpdateVisibility();
        for (var i = 0; i < TabCount; i++)
        {
            var list = GetChildRecursive(PageIds[i])?.GetChildRecursive(ListId) as UiItemList;
            _lists[i] = list;
            _dirty[i] = true;
            if (list is not null)
            {
                var tab = i;
                list.ItemSelected += (_, cell) => OnCellSelected(tab, cell);
                list.ItemActivated += (_, cell) => OnCellActivated(tab, cell);
            }
        }
    }

    /// <summary>
    /// Hands the window the session: it follows <see cref="CombatModeChanged"/> (and the description, which may carry the mode) and <see cref="SpellbookChanged"/>, and takes the mode the session holds now (the layout shows the
    /// window; outside Magic mode it hides).
    /// </summary>
    public void Attach(UiLoginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _modeSubscription = context.Session.Events.Subscribe<CombatModeChanged>(OnCombatModeChanged);
        _qualitySubscription = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        _bookSubscription = context.Session.Events.Subscribe<SpellbookChanged>(OnSpellbookChanged);
        for (var i = 0; i < TabCount; i++)
        {
            if (_lists[i] is null)
            {
                context.Manager.Services.Log.Error(UiElementManager.Category, $"gmSpellcastingUI: tab {i} has no spell list (page 0x{PageIds[i]:X8}, list 0x{ListId:X8}); the tab shows no spells (TODO(m3-spellcast-list-missing))");
            }
        }

        context.Manager.Services.Log.Warn("retailui", "gmSpellcastingUI: TODO(m3-cast-untargeted) TODO(m3-cast-components) TODO(m3-cast-compat) TODO(m3-spellcast-selection-notice) TODO(m3-spellcast-endowment) TODO(m3-spellcast-hotkeys) TODO(m3-spellcast-unknown-favourite)");
        SetCombatMode(context.Session.State.Self.Qualities.CombatMode);
    }

    /// <summary>
    /// <c>gmSpellcastingUI::RecvNotice_SetCombatMode</c> (11.6096 0x004C7E70): the Magic mode (8) shows the window and brings its tab, name and cast button up to date; any other mode hides it. The floaty that holds it follows.
    /// </summary>
    public void SetCombatMode(int mode)
    {
        Mode = mode;
        if (mode == CombatModes.Magic && !HideForVideo)
        {
            SetVisible(true);
            Refresh();
        }
        else
        {
            SetVisible(false);
        }

        (Parent as UiFloatyCombatPanel)?.UpdateVisibility();
    }

    /// <summary>Selects <paramref name="spellId"/> on the open tab (<c>SpellCastSubMenu::SetSelected</c>, 11.6096 0x004C66F0): its cell highlights, the others do not; an id the tab does not show selects nothing.</summary>
    public void SelectSpell(uint spellId)
    {
        var tab = OpenTab;
        _selected[tab] = ShowsSpell(tab, spellId) ? spellId : 0;
        ApplySelection(tab);
        UpdateCastButton();
    }

    /// <summary>
    /// <c>gmSpellcastingUI::Cast</c> (11.6096 0x004C6C40) for the open tab's selected spell: with none, the chat line <see cref="NoSpellText"/>; else <see cref="Cast(uint)"/>.
    /// </summary>
    public void CastSelected()
    {
        var spell = SelectedSpell;
        if (spell == 0)
        {
            Say(NoSpellText);
            return;
        }

        Cast(spell);
    }

    /// <summary>
    /// <c>ClientMagicSystem::CastSpell</c> (11.6096 0x00568DE0): a self-targeted spell is cast at the player, any other at the selected object, and with none selected the chat line <see cref="NoTargetText"/> (nothing is sent). An
    /// unknown spell (the SpellTable not loaded, or the spell not in it) is a logged TODO and nothing is sent. Returns the action, or null when nothing was sent.
    /// </summary>
    public ActionHandle? Cast(uint spellId)
    {
        if (_context is not { } context || spellId == 0)
        {
            return null;
        }

        var session = context.Session;
        if (session.Spell(spellId) is not { } info)
        {
            context.Manager.Services.Log.Warn("retailui", $"gmSpellcastingUI: spell {spellId} is not in the SpellTable (or the table has not loaded); retail's InqSpellBase fails and CastSpell returns; nothing is cast (TODO(m3-cast-unknown-spell))");
            return null;
        }

        uint target;
        if ((info.Flags & SpellFlags.SelfTargeted) != 0)
        {
            target = session.State.Self.Guid; // FreeHandsAndCastSpell(spell, player_id): a targeted cast at the player
        }
        else
        {
            target = SelectedObject();
            if (target == 0)
            {
                Say(NoTargetText);
                return null;
            }
        }

        if (target == 0)
        {
            context.Manager.Services.Log.Warn("retailui", $"gmSpellcastingUI: the player's id is not known; {info.Name} is not cast");
            return null;
        }

        Casts++;
        LastCast = session.Actions.Cast(spellId, target, ActionSource.Ui);
        return LastCast;
    }

    /// <summary>The window's messages (<c>ListenToElementMessage</c>, 11.6096 0x004C86A0): the cast button casts the selected spell; a tab change brings the new tab up to date.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == CastButtonId)
        {
            CastSelected();
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.PanelTabChanged && ReferenceEquals(message.Element, _panel) && Visible)
        {
            Refresh();
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>The one-shot poll while the SpellTable has not loaded: builds the shown tab once the table is there (<see cref="IGameSessionView.SpellTableReady"/>, a flag read: nothing is allocated per frame).</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.Frame && _awaitingTable)
        {
            if (!Visible || _context is null)
            {
                _awaitingTable = false;
                return;
            }

            if (_context.Session.SpellTableReady)
            {
                _awaitingTable = false;
                Refresh();
            }
            else
            {
                Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDeleting()
    {
        _bookSubscription?.Dispose();
        _modeSubscription?.Dispose();
        _qualitySubscription?.Dispose();
        _bookSubscription = _modeSubscription = _qualitySubscription = null;
        base.OnDeleting();
    }

    private void OnCombatModeChanged(in CombatModeChanged change) => SetCombatMode(change.Mode);

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description && _context is { } context)
        {
            SetCombatMode(context.Session.State.Self.Qualities.CombatMode); // the description may carry the mode
        }
    }

    private void OnSpellbookChanged(in SpellbookChanged change)
    {
        // A description replaces the bars and the known spells; a learned spell changes the bars only when a favourite was waiting for it (a build compares the bar and how many of its spells are known, so the open tab is checked, the others when they open).
        if (Visible)
        {
            Refresh();
        }
    }

    private void OnCellSelected(int tab, UiItem cell)
    {
        if (tab == OpenTab && cell.SpellId != 0)
        {
            SelectSpell(cell.SpellId);
        }
    }

    private void OnCellActivated(int tab, UiItem cell)
    {
        if (tab == OpenTab && cell.SpellId != 0)
        {
            SelectSpell(cell.SpellId);
            CastSelected();
        }
    }

    // The open tab brought up to date: built when its bar changed, then the name text and the cast button.
    private void Refresh()
    {
        if (_context is null || !Visible)
        {
            return;
        }

        var tab = OpenTab;
        BuildTab(tab);
        ApplySelection(tab);
        UpdateCastButton();
    }

    // SpellCastSubMenu::UpdateFromPlayerModule (11.6096 0x004C7160) for one tab: the bar's known spells in slot order, written in place.
    private void BuildTab(int tab)
    {
        if (_context is not { } context || _lists[tab] is not { } list)
        {
            return;
        }

        var book = context.Session.State.Self.Spellbook;
        var bar = book.Bar(tab);
        var known = KnownOn(bar, book);
        if (!_dirty[tab] && _builtKnown[tab] == known && _built[tab].Equals(bar))
        {
            return; // nothing changed since this tab was built
        }

        // The SpellTable decodes on a worker: until it is published, the list stays as it is and a one-shot poll waits for it (never a guess). The table says when it is there; a spell it then lacks is left out below, named.
        if (!context.Session.SpellTableReady)
        {
            if (!_awaitingTable)
            {
                _awaitingTable = true;
                Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
            }

            return;
        }

        _awaitingTable = false;
        var written = 0;
        foreach (var id in bar)
        {
            if (!book.Knows(id))
            {
                continue; // a favourite the character no longer knows (retail drops it from its player module)
            }

            if (context.Session.Spell(id) is not { } info)
            {
                if ((_unknownNamed ??= []).Add(id))
                {
                    context.Manager.Services.Log.Warn("retailui", $"gmSpellcastingUI: favourite {id} is not in the loaded SpellTable; it is left out of the bar (TODO(m3-spellcast-unknown-spell))");
                }

                continue;
            }

            if (!list.EnsureCells(written + 1) || list.CellAt(written) is not { } cell)
            {
                break; // the cell template is missing: the list logged it
            }

            if (cell.SpellId != id)
            {
                cell.ShowSpell(id, info.IconId, info.Name);
                cell.SetTooltip(UiStringRef.FromLiteral(info.Name + CastHint)); // the cell's name text stays the name; the tooltip adds the hint
            }

            written++;
        }

        while (list.CellCount > written)
        {
            list.DeleteItem(list.CellCount - 1); // the surplus of a bar that shrank
        }

        _built[tab] = bar;
        _builtKnown[tab] = known;
        _dirty[tab] = false;
        Builds++;
    }

    // How many of the bar's spells the character knows: a learned spell that is on no bar changes nothing here.
    private static int KnownOn(ImmutableArray<uint> bar, Spellbook book)
    {
        var known = 0;
        foreach (var id in bar)
        {
            if (book.Knows(id))
            {
                known++;
            }
        }

        return known;
    }

    private bool ShowsSpell(int tab, uint spellId)
    {
        if (spellId == 0 || _lists[tab] is not { } list)
        {
            return false;
        }

        for (var i = 0; i < list.CellCount; i++)
        {
            if (list.CellAt(i) is { } cell && cell.SpellId == spellId)
            {
                return true;
            }
        }

        return false;
    }

    // The highlight follows the tab's selected spell; a spell the tab no longer shows is deselected.
    private void ApplySelection(int tab)
    {
        if (_lists[tab] is not { } list)
        {
            return;
        }

        if (_selected[tab] != 0 && !ShowsSpell(tab, _selected[tab]))
        {
            _selected[tab] = 0;
        }

        for (var i = 0; i < list.CellCount; i++)
        {
            if (list.CellAt(i) is { } cell && cell.SpellId != 0)
            {
                cell.SetSelected(cell.SpellId == _selected[tab]);
            }
        }
    }

    // UpdateCastButtonTooltip (11.6096 0x004C7620): the name text names the selected spell; the cast button is ghosted with none selected, else it reads "CAST <name>".
    private void UpdateCastButton()
    {
        if (_context is not { } context)
        {
            return;
        }

        var tab = OpenTab;
        var spell = _selected[tab];
        var info = spell == 0 ? null : context.Session.Spell(spell);
        if (info is null)
        {
            _name?.ClearAllText();
            _castButton?.SetState(UiStateIds.Ghosted);
            _castButton?.SetTooltip(UiStringRef.FromLiteral(_lists[tab]?.CellCount > 0 ? "Select a spell to cast" : "You have no spells ready to cast"));
            return;
        }

        _name?.SetText(info.Name);
        _castButton?.SetState(UiStateIds.Normal);
        _castButton?.SetTooltip(UiStringRef.FromLiteral("CAST " + info.Name));
    }

    private uint SelectedObject()
    {
        if (SelectedProvider is { } provider)
        {
            return provider();
        }

        if (!_selectionTodoLogged && _context is { } context)
        {
            _selectionTodoLogged = true;
            context.Manager.Services.Log.Warn("retailui", "gmSpellcastingUI: no object selection is attached; a targeted spell has no target (TODO(m3-cast-selection-missing))");
        }

        return 0;
    }

    // ECM_UI::SendNotice_DisplayStringInfo (type 0x1A): the line goes to the chat, and to the log.
    private void Say(string line)
    {
        _context?.Manager.Services.Log.Info("retailui", line);
        ChatNotice?.Invoke(line);
    }
}
