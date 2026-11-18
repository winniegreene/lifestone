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
/// and 0x100005C2), one <see cref="Spellbook.SpellBars"/> bar each; the tab's item list 0x100000B6 shows the bar's spells in slot order from its first cell (<c>UIElement_ItemList::ItemList_AddSpellShortcut</c>, 0x004E4D30), each with its icon and its name from the SpellTable and the tooltip "name, new line,
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
/// polls that one). <c>TODO(m3-spellcast-endowment)</c>: the endowment icon (a wielded item's own spell) is hidden, so the spell keys' selection wraps where retail's would select the endowment.
/// </para>
/// <para>
/// <b>The keys</b> (M5 6 s1, the closed <c>m3-spellcast-hotkeys</c>): the magic input map 0x10000005 (<see cref="SpellBarKeys"/>) is registered in Magic mode only and taken out of the router when the window is deleted. Keys 1 to 9 cast
/// the open tab's nth spell (<see cref="CastQuickslot"/>, <c>RecvNotice_CastQuickslotSpell</c> 11.6096 0x004C84A0: selected, then <c>Cast</c>, as a double click); End casts the selected spell; Delete and PgDn select the previous and
/// next spell, Insert and PgUp open the previous and next tab, and with Ctrl the first and last of each.
/// </para>
/// <para>
/// <b>The automation's casts</b> (M6 item 15, the player's rules): when an automation cast goes out the Action API publishes <see cref="SpellBarCast"/> and the window shows it as a person's cast shows (<see cref="ShowCastFromBar"/>):
/// the spell's tab opens and the spell is selected, so the name text and the cast button name it.
/// </para>
/// <para>
/// <b>The drag</b> (M5 6 s1). Retail's drop handler is <c>gmSpellcastingUI::HandleDropRelease</c> (11.6096 0x004C8520): a spell dropped on a cell of the open tab's list goes in at that cell (<c>AddSpellShortcut</c> 0x004C7F10 over
/// <c>SpellCastSubMenu::AddFavorite</c> 0x004C7C50), on the window's background at the end of the open tab, on a tab at the end of that tab; <c>SpellCastSubMenu::OnItemListDragOver</c> (0x004C6580) accepts a drop that carries a
/// spell id only. <c>AddFavorite</c> first takes a spell the bar holds off it (<c>RemoveSpellFromMenu</c> 0x004C74E0, <c>RemoveSpellFromPlayerModule</c> 0x004C7000: RemoveSpellFavorite) and moves the position one left when the old
/// place was before it, then <c>AddSpellToPlayerModule</c> (0x004C70B0) sends AddSpellFavorite at that position; ACE does not add a spell its bar already holds (<c>AddSpellToBar</c>, @95ef426d
/// Source/ACE.Database/Models/Shard/CharacterExtensions.cs:538-539) and clamps the position to the bar (548-549), so a move is a RemoveSpellFavorite then an AddSpellFavorite. A spell dragged off a bar to anywhere else is removed
/// (<c>RecvNotice_ItemListBeginDrag</c> 0x004C7F50 removes it when the drag begins; here at the release, as the shortcut bar does, so an Escape keeps it: <c>TODO(m5-spellbar-drag-begin)</c>). Every action goes through
/// <see cref="IActionApi.AddSpellFavorite"/> and <see cref="IActionApi.RemoveSpellFavorite"/> as <see cref="ActionSource.Ui"/>, and the read model's bar changes at once. A drop ACE would ignore (an item, a spell the character does
/// not know, a bar above 7: @95ef426d Source/ACE.Server/WorldObjects/Player_Character.cs:300) is refused before anything is sent; retail prints no line for it that is known (<c>TODO(m5-spellbar-refusal-line)</c>). Retail's position
/// is the cell's index in a list that holds every favourite; Lifestone's list leaves out the favourites the character does not know, so the drop names the bar position of the cell's spell. Deviation: any part of the window that is
/// not a list or a tab counts as its background (retail compares one background element, address unknown).
/// </para>
/// </remarks>
public sealed class GameSpellcastingPanel : UiElement, IUiDragSourceOwner
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

    /// <summary>Why a drop that carries no spell is refused (<c>OnItemListDragOver</c> accepts a spell id only).</summary>
    public const string NotASpellRefusal = "only a spell goes on a spell bar (SpellCastSubMenu::OnItemListDragOver 0x004C6580 accepts a drop with a spell id only)";

    /// <summary>Why a spell the character does not know is refused before sending (ACE ignores it).</summary>
    public const string UnknownSpellRefusal = "the character does not know the spell, so ACE would ignore it (HandleActionAddSpellFavorite, Player_Character.cs:300); retail prints no line that is known (TODO(m5-spellbar-refusal-line))";

    /// <summary>Why a bar outside 0 to 7 is refused before sending (ACE ignores it).</summary>
    public const string BarRefusal = "ACE ignores a bar above 7 (HandleActionAddSpellFavorite, Player_Character.cs:300); retail prints no line that is known (TODO(m5-spellbar-refusal-line))";

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
    private IDisposable? _barCastSubscription;
    private readonly int[] _builtKnown = new int[TabCount];
    private HashSet<uint>? _unknownNamed; // the favourites a loaded table lacked, each named once (allocated at a build, an event)
    private bool _awaitingTable;
    private bool _selectionTodoLogged;
    private SpellBarKeys? _keys;
    private UiDrag? _drag;
    private Func<uint, UiSpellIcon?>? _spellIcon;

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

    /// <summary>The first tab whose bar holds no spell the character knows (the empty bar the <c>c2-spellcasting-empty</c> frame shows), or -1 when every bar holds one or no session is attached (M4 3.2 lane e).</summary>
    public int FirstTabWithoutSpells()
    {
        if (_context is null)
        {
            return -1;
        }

        var book = _context.Session.State.Self.Spellbook;
        for (var tab = 0; tab < TabCount; tab++)
        {
            if (KnownOn(book.Bar(tab), book) == 0)
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

    /// <summary>M6 item 15: how many automation casts the window showed (<see cref="ShowCastFromBar"/>).</summary>
    public int BarCastsShown { get; private set; }

    /// <summary>The last cast the window asked for, or null.</summary>
    public ActionHandle? LastCast { get; private set; }

    /// <summary>The magic map's keys (<see cref="SpellBarKeys"/>), null before <see cref="Attach"/>.</summary>
    public SpellBarKeys? Keys => _keys;

    /// <summary>The actions the last drag on or off a bar asked for, in the order they were sent (empty for none).</summary>
    public IReadOnlyList<ActionHandle> BarEdits { get; private set; } = [];

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
        _barCastSubscription = context.Session.Events.Subscribe<SpellBarCast>(OnSpellBarCast);
        for (var i = 0; i < TabCount; i++)
        {
            if (_lists[i] is null)
            {
                context.Manager.Services.Log.Error(UiElementManager.Category, $"gmSpellcastingUI: tab {i} has no spell list (page 0x{PageIds[i]:X8}, list 0x{ListId:X8}); the tab shows no spells (TODO(m3-spellcast-list-missing))");
            }
        }

        context.Manager.Services.Log.Warn("retailui", "gmSpellcastingUI: TODO(m3-cast-untargeted) TODO(m3-cast-components) TODO(m3-cast-compat) TODO(m3-spellcast-selection-notice) TODO(m3-spellcast-endowment) TODO(m3-spellcast-unknown-favourite) TODO(m5-spellbar-drag-begin) TODO(m5-spellbar-refusal-line)");
        _keys = new SpellBarKeys(context.Manager, this);
        AttachDrag(context);
        SetCombatMode(context.Session.State.Self.Qualities.CombatMode);
    }

    /// <summary>
    /// <c>gmSpellcastingUI::RecvNotice_SetCombatMode</c> (11.6096 0x004C7E70): the Magic mode (8) shows the window and brings its tab, name and cast button up to date; any other mode hides it. The floaty that holds it follows.
    /// </summary>
    public void SetCombatMode(int mode)
    {
        Mode = mode;
        _keys?.SetMode(mode); // RegisterInputMaps: the magic map in Magic mode only
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

    /// <summary>
    /// M6 item 15: the automation cast <paramref name="spellId"/> from bar <paramref name="bar"/> (<see cref="SpellBarCast"/>). The window takes the state a person's own cast of that spell leaves: the bar's tab is the open tab
    /// (<see cref="OpenTabByClick"/>, as a click on the tab) and the spell is the selected one (<see cref="SelectSpell"/>, as the double click that casts it selects it first). Nothing is cast here: the automation's cast is already
    /// on its way. A hidden window (the mode is not Magic yet) opens the tab and keeps the selection for when it shows. Returns true when the tab is open and the spell is the tab's selected one.
    /// </summary>
    public bool ShowCastFromBar(int bar, uint spellId)
    {
        if (_context is null || spellId == 0 || !OpenTabByClick(bar))
        {
            return false;
        }

        BarCastsShown++;
        if (!Visible)
        {
            _selected[bar] = spellId; // checked against the tab's cells when the window shows (ApplySelection)
            return true;
        }

        Refresh();
        if (_awaitingTable)
        {
            _selected[bar] = spellId; // the tab is built when the SpellTable arrives, and the selection is checked then
            return true;
        }

        SelectSpell(spellId);
        return _selected[bar] == spellId;
    }

    /// <summary><c>RecvNotice_CastQuickslotSpell</c> (11.6096 0x004C84A0): the open tab's spell in cell <paramref name="slot"/> is selected and cast (<see cref="CastSelected"/>), as a double click on it; an empty slot does nothing.</summary>
    public void CastQuickslot(int slot)
    {
        var spell = SpellAt(OpenTab, slot);
        if (spell == 0)
        {
            return;
        }

        SelectSpell(spell);
        CastSelected();
    }

    /// <summary><c>RecvNotice_NextSpellSelection</c> (11.6096 0x004C8050): the spell after the selected one on the open tab; none selected, the first; after the last, the first again (retail selects the endowment there when one shows).</summary>
    public void SelectNextSpell()
    {
        var tab = OpenTab;
        var count = SpellCount(tab);
        if (count == 0)
        {
            return;
        }

        var at = IndexOfSpell(tab, _selected[tab]);
        SelectSpell(SpellAt(tab, at < 0 || at + 1 >= count ? 0 : at + 1));
    }

    /// <summary><c>RecvNotice_PrevSpellSelection</c> (11.6096 0x004C81C0): the spell before the selected one; none selected, the first; before the first, the last.</summary>
    public void SelectPreviousSpell()
    {
        var tab = OpenTab;
        var count = SpellCount(tab);
        if (count == 0)
        {
            return;
        }

        var at = IndexOfSpell(tab, _selected[tab]);
        SelectSpell(SpellAt(tab, at < 0 ? 0 : at == 0 ? count - 1 : at - 1));
    }

    /// <summary><c>RecvNotice_FirstSpellSelection</c> (11.6096 0x004C8390): the open tab's first spell, when it has one.</summary>
    public void SelectFirstSpell()
    {
        if (SpellCount(OpenTab) > 0)
        {
            SelectSpell(SpellAt(OpenTab, 0));
        }
    }

    /// <summary><c>RecvNotice_LastSpellSelection</c> (11.6096 0x004C8410): the open tab's last spell, when it has one.</summary>
    public void SelectLastSpell()
    {
        var count = SpellCount(OpenTab);
        if (count > 0)
        {
            SelectSpell(SpellAt(OpenTab, count - 1));
        }
    }

    /// <summary><c>RecvNotice_NextSpellTab</c> (11.6096 0x004C7FD0) over <c>GetNextTabID</c> (0x004C6400): the next tab opens, the last wrapping to the first.</summary>
    public bool OpenNextTab() => OpenTabByClick((OpenTab + 1) % TabCount);

    /// <summary><c>RecvNotice_PrevSpellTab</c> (11.6096 0x004C8010) over <c>GetPrevTabID</c> (0x004C6480): the previous tab opens, the first wrapping to the last.</summary>
    public bool OpenPreviousTab() => OpenTabByClick((OpenTab + TabCount - 1) % TabCount);

    /// <inheritdoc/>
    public bool OwnsTarget(IUiDropTarget? target) => target is SpellDrop drop && ReferenceEquals(drop.Panel, this);

    /// <summary>A spell dragged off a bar and released anywhere but the window's own targets is taken off that bar (RemoveSpellFavorite, <see cref="ActionSource.Ui"/>).</summary>
    public void DragReleasedAway(in UiDragPayload drag, IUiDropTarget? target)
    {
        var from = BarOf(drag.SourceList);
        if (_context is not { } context || from < 0 || drag.SpellId == 0 || !context.Session.State.Self.Spellbook.Bar(from).Contains(drag.SpellId))
        {
            BarEdits = [];
            return;
        }

        BarEdits = [context.Session.Actions.RemoveSpellFavorite(drag.SpellId, from, ActionSource.Ui)];
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
        _keys?.Dispose();
        _keys = null;
        if (_drag is { } drag)
        {
            for (var i = 0; i < TabCount; i++)
            {
                if (_lists[i] is { } list)
                {
                    drag.UnregisterSourceOwner(list);
                    drag.UnregisterTarget(list);
                }

                if (TabElement(i) is { } tab)
                {
                    drag.UnregisterTarget(tab);
                }
            }

            drag.UnregisterTarget(this);
            if (ReferenceEquals(drag.SpellIconOf, _spellIcon))
            {
                drag.SpellIconOf = null;
            }

            _drag = null;
        }

        _bookSubscription?.Dispose();
        _modeSubscription?.Dispose();
        _qualitySubscription?.Dispose();
        _barCastSubscription?.Dispose();
        _bookSubscription = _modeSubscription = _qualitySubscription = _barCastSubscription = null;
        base.OnDeleting();
    }

    private void OnCombatModeChanged(in CombatModeChanged change) => SetCombatMode(change.Mode);

    // The drag (M5 6 s1): every bar's list is a source and a target, each tab and the window's background a target; a spell's icon under the pointer comes from the SpellTable.
    private void AttachDrag(UiLoginContext context)
    {
        var drag = context.Manager.Input.Drag;
        _drag = drag;
        var session = context.Session;
        _spellIcon = id => session.Spell(id) is { } spell ? UiSpellIcon.Of(spell) : null;
        drag.SpellIconOf ??= _spellIcon;
        for (var i = 0; i < TabCount; i++)
        {
            if (_lists[i] is { } list)
            {
                list.DragSource = true;
                drag.RegisterTarget(list, new SpellDrop(this, i, onList: true));
                drag.RegisterSourceOwner(list, this);
            }

            if (TabElement(i) is { } tab)
            {
                drag.RegisterTarget(tab, new SpellDrop(this, i, onList: false));
            }
        }

        drag.RegisterTarget(this, new SpellDrop(this, -1, onList: false));
    }

    // Why a drop of the drag on the bar is refused before anything is sent, or null. Called on every pointer move over a target: returns constants, allocates nothing.
    private string? RefusalOf(in UiDragPayload drag, int bar)
    {
        if (drag.SpellId == 0)
        {
            return NotASpellRefusal;
        }

        if (bar is < 0 or >= TabCount)
        {
            return BarRefusal;
        }

        return _context is { } context && context.Session.State.Self.Spellbook.Knows(drag.SpellId) ? null : UnknownSpellRefusal;
    }

    // HandleDropRelease (0x004C8520) over AddFavorite (0x004C7C50): the spell goes in at the bar position of the cell's spell (no cell or an empty one: the end); a spell the bar holds is taken off first (the position moves one left
    // when the old place was before it), and a spell from another bar comes off that one (retail's drag-begin removal). Nothing is sent when the spell would land where it is.
    private void DropSpell(in UiDragPayload drag, int bar, UiItem? cell)
    {
        if (_context is not { } context)
        {
            return;
        }

        var spell = drag.SpellId;
        var from = BarOf(drag.SourceList);
        var book = context.Session.State.Self.Spellbook;
        var target = book.Bar(bar);
        var index = target.Length;
        if (cell is { SpellId: not 0 } shown && target.IndexOf(shown.SpellId) is var at and >= 0)
        {
            index = at;
        }

        var actions = context.Session.Actions;
        var edits = new List<ActionHandle>(3);
        if (from >= 0 && from != bar && book.Bar(from).Contains(spell))
        {
            edits.Add(actions.RemoveSpellFavorite(spell, from, ActionSource.Ui));
        }

        var old = target.IndexOf(spell);
        if (old >= 0)
        {
            if (old < index)
            {
                index--;
            }

            if (old == index)
            {
                BarEdits = edits;
                return; // already there
            }

            edits.Add(actions.RemoveSpellFavorite(spell, bar, ActionSource.Ui));
        }

        edits.Add(actions.AddSpellFavorite(spell, index, bar, ActionSource.Ui));
        BarEdits = edits;
    }

    private int BarOf(UiItemList? list)
    {
        if (list is null)
        {
            return -1;
        }

        for (var i = 0; i < TabCount; i++)
        {
            if (ReferenceEquals(_lists[i], list))
            {
                return i;
            }
        }

        return -1;
    }

    // The spells of a tab's list are its first cells (the empty slot boxes follow them).
    private int SpellCount(int tab)
    {
        if (_lists[tab] is not { } list)
        {
            return 0;
        }

        var count = 0;
        while (count < list.CellCount && list.CellAt(count) is { SpellId: not 0 })
        {
            count++;
        }

        return count;
    }

    private uint SpellAt(int tab, int index) => index >= 0 && _lists[tab]?.CellAt(index) is { } cell ? cell.SpellId : 0;

    private int IndexOfSpell(int tab, uint spellId)
    {
        if (spellId == 0)
        {
            return -1;
        }

        var count = SpellCount(tab);
        for (var i = 0; i < count; i++)
        {
            if (SpellAt(tab, i) == spellId)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description && _context is { } context)
        {
            SetCombatMode(context.Session.State.Self.Qualities.CombatMode); // the description may carry the mode
        }
    }

    private void OnSpellBarCast(in SpellBarCast cast) => ShowCastFromBar(cast.Bar, cast.SpellId);

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
                cell.ShowSpell(UiSpellIcon.Of(info), info.Name); // the composed picture (GetSpellIcon, D147)
                cell.SetTooltip(UiStringRef.FromLiteral(info.Name + CastHint)); // the cell's name text stays the name; the tooltip adds the hint
            }

            written++;
        }

        // The surplus of a bar that shrank is emptied, not removed: the bar's cells past its favourites are its empty slot boxes, which UpdateEmptySlots (11.6096 0x004E4390) pads to the bar's width and trims (M4 3.2 lane e).
        for (var i = list.CellCount - 1; i >= written; i--)
        {
            if (list.CellAt(i) is { IsEmpty: false } leftover)
            {
                leftover.Clear();
            }
        }

        list.UpdateEmptySlots();

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
            _castButton?.SetTooltip(UiStringRef.FromLiteral(_lists[tab]?.OccupiedCount > 0 ? "Select a spell to cast" : "You have no spells ready to cast"));
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

    // A drop target of the window: a bar's list (tab, on the cell under the pointer), a tab (tab, at its end) or the background (tab -1: the open tab, at its end).
    private sealed class SpellDrop(GameSpellcastingPanel panel, int tab, bool onList) : IUiDropTarget
    {
        public GameSpellcastingPanel Panel => panel;

        public bool HighlightsCell => onList;

        private int Bar => tab >= 0 ? tab : panel.OpenTab;

        public bool Accepts(in UiDragPayload drag, UiItem? cell) => panel.RefusalOf(drag, Bar) is null;

        public string? Refusal(in UiDragPayload drag, UiItem? cell) => panel.RefusalOf(drag, Bar);

        public void Drop(in UiDragPayload drag, UiItem? cell) => panel.DropSpell(drag, Bar, onList ? cell : null);
    }
}
