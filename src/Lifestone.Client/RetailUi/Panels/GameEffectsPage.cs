using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The effects pages (M3 2.2 a3): page 4 lists the beneficial enchantments and page 5 the harmful ones, one class (<c>gmEffectsUI</c>, 0x1000001B; 11.6096 0x004B8180 to 0x004B9300) on two pages. Which page
/// a controller serves is the page element's property 0x1000000C (1 beneficial, 2 harmful; <c>PostInit</c> 0x004B8180 reads it). A controller builds nothing until its page shows.
/// </summary>
/// <remarks>
/// <para><b>The list.</b> <c>RebuildList</c> (0x004B8F70) asks the registry for the enchantments in effect (<see cref="EffectRules.InEffect"/>: each spell category once, so an entry a stronger one surpasses
/// and the layers of one spell are not listed), keeps the spells the SpellTable calls beneficial (page 4) or not (page 5; <c>SpellEffectMatchesUIType</c> 0x004B82E0 bit 4 of the spell), and inserts each by the spell
/// name (<c>GetSortedInsertionPlace</c> 0x004B84E0: before the first line whose name is not before it, <c>strcmp</c>). A line is the list's template 0: the spell's icon (<c>EffectInfoRegion</c> 0x004F31C0), its name
/// and its remaining time, <c>H:MM:SS</c> from an hour up, else <c>M:SS</c> (<c>EffectInfoRegion::Update</c> 0x004F2860), blank for a permanent or an overdue enchantment, and only when the character's
/// "spell duration" option is set. A click on a line selects its spell (the same line again clears) and the info box below shows the spell's name and description, or "select a spell", or "no spells"
/// (<c>UpdateSelection</c> 0x004B8BB0). The dispel or remove button of the info box does not exist in the layout (only the page's help button 0x100000FC, which the host answers).</para>
/// <para><b>When it redraws.</b> The registry changing (<c>EnchantmentChanged</c>) or the description arriving marks the list for a rebuild; the rebuild happens once, when the page shows (<c>Update</c> 0x004B9150
/// is a no-op for a hidden page). While shown, the countdown is rewritten for a line only when its whole seconds change (<c>UpdateDurations</c> 0x004B87D0 runs once a second in retail).</para>
/// <para><b>The stale info box (a glitch fix, the default).</b> Retail's <c>RebuildList</c> (11.6096 0x004B8F70) returns before <c>UpdateSelection</c> when the registry has nothing in effect at all, so the last effect
/// to end leaves the old spell's name and text in the info box and its selection standing (and a new character's box stays blank). Lifestone clears the selection and the box when the list empties
/// (decision D32); <c>--retail-effects-stale-info</c> (<see cref="UiRetailGlitches.EffectsStaleInfo"/>) restores retail's. The "no spells" text appears when something is in effect but nothing of this page's kind, as in retail.
/// A spell the SpellTable does not hold (it decodes on a worker at start-up) is not listed and not guessed: the page logs <c>TODO(m3-effects-spell-table)</c> once and lists it as soon as the table answers.</para>
/// </remarks>
public sealed class GameEffectsPage : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The panel ids of the beneficial (4) and harmful (5) pages.</summary>
    public const uint BeneficialPanelId = 4, HarmfulPanelId = 5;

    /// <summary>The class id of <c>gmEffectsUI</c>.</summary>
    public const uint ClassId = 0x1000001B;

    /// <summary>The page's effect-type property (1 beneficial, 2 harmful).</summary>
    public const uint EffectTypeProperty = 0x1000000C;

    /// <summary>The list (0x10000123) and the info box (0x10000126).</summary>
    public const uint ListId = 0x10000123, InfoTextId = 0x10000126;

    /// <summary>The line template's children: the icon, the name and the remaining time.</summary>
    public const uint RowIconId = 0x10000129, RowNameId = 0x1000012A, RowTimeId = 0x1000012B;

    /// <summary>The string table enum of the <c>ID_Effects_*</c> strings.</summary>
    public const uint StringTableEnum = 0x10000001;

    private const long NothingShown = long.MinValue;

    private readonly UiLoginContext _context;
    private readonly uint _panelId;
    private readonly bool _beneficial;
    private readonly UiElement? _page;
    private readonly UiListBox? _list;
    private readonly UiTextElement? _info;
    private readonly List<ActiveEnchantment> _inEffect = new(32);
    private readonly List<Line> _lines = new(32);
    private readonly IDisposable _enchantments;
    private readonly IDisposable _qualities;
    private bool _shown;
    private bool _dirty = true;
    private uint _selected;
    private uint _probe;

    /// <summary>The controller of page <paramref name="panelId"/> (4 or 5) of <paramref name="host"/>.</summary>
    public GameEffectsPage(UiLoginContext context, GamePanelHost host, uint panelId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        _context = context;
        _panelId = panelId;
        _page = host.ElementFor(panelId);
        _beneficial = panelId == BeneficialPanelId;
        if (_page is not null && _page.TryGetEnumProperty(EffectTypeProperty, out var type))
        {
            _beneficial = type == 1; // gmEffectsUI::PostInit reads the page's effects type
        }

        _list = _page?.GetChildRecursive(ListId) as UiListBox;
        _info = _page?.GetChildRecursive(InfoTextId) as UiTextElement;
        if (_page is null || _list is null || _info is null)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, $"gmEffectsUI: page {panelId} lacks its list or info box; it shows its art only (TODO(m3-effects-page-missing))");
        }
        else
        {
            _page.RegisterForElementMessages(this);
        }

        _enchantments = context.Session.Events.Subscribe<EnchantmentChanged>(OnEnchantmentChanged);
        _qualities = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
    }

    /// <inheritdoc/>
    public uint PanelId => _panelId;

    /// <summary>True for the beneficial page.</summary>
    public bool IsBeneficialPage => _beneficial;

    /// <summary>The line list's box (null when the page lacks it).</summary>
    public UiListBox? List => _list;

    /// <summary>The spell selected (0 for none).</summary>
    public uint SelectedSpell => _selected;

    /// <summary>The info box's text now.</summary>
    public string InfoText => _info?.Text ?? string.Empty;

    /// <summary>How many times the list was rebuilt.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>How many times a line's remaining time was written (a rebuild writes every line's).</summary>
    public int TimeWrites { get; private set; }

    /// <summary>The lines in list order for the tests and the play check: spell id, name and the time text shown.</summary>
    public IEnumerable<(uint SpellId, string Name, string Time)> Lines
    {
        get
        {
            foreach (var line in _lines)
            {
                yield return (line.SpellId, line.Name, line.Time?.Text ?? string.Empty);
            }
        }
    }

    /// <summary>How many lines the list has.</summary>
    public int LineCount => _lines.Count;

    /// <summary>The list item of the line of <paramref name="spellId"/>, or null.</summary>
    public UiElement? ItemOf(uint spellId)
    {
        foreach (var line in _lines)
        {
            if (line.SpellId == spellId)
            {
                return line.Item;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void OnShown()
    {
        _shown = true;
        Refresh(); // gmEffectsUI::ListenToElementMessage (a visibility message): Update
    }

    /// <inheritdoc/>
    public void OnHidden() => _shown = false;

    /// <inheritdoc/>
    public void Step(double now)
    {
        if (!_shown)
        {
            return;
        }

        if (!_dirty && _probe != 0 && _context.Session.Spell(_probe) is not null)
        {
            _dirty = true; // the SpellTable has loaded since the last rebuild
        }

        if (_dirty)
        {
            Refresh();
        }
        else
        {
            WriteTimes();
        }
    }

    /// <summary>Redraws the page now if it shows: <c>gmEffectsUI::Update</c> (0x004B9150), a rebuild when the registry changed, else the durations. The tests and the play check call it.</summary>
    public void Refresh()
    {
        if (_list is null || _info is null || !_shown)
        {
            return;
        }

        if (_dirty)
        {
            Rebuild();
        }
        else
        {
            WriteTimes();
        }
    }

    private void OnEnchantmentChanged(in EnchantmentChanged change) => _dirty = true; // RecvNotice_EnchantmentsChanged

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change == QualityChange.Description)
        {
            _dirty = true; // RecvNotice_PlayerDescReceived
        }
    }

    // ------------------------------------------------------------------ the list

    /// <summary><c>gmEffectsUI::RebuildList</c> (0x004B8F70).</summary>
    private void Rebuild()
    {
        var session = _context.Session;
        var q = session.State.Self.Qualities;
        if (!q.IsKnown)
        {
            return; // nothing to list until the description arrives (the Description event marks the page dirty again)
        }

        _dirty = false;
        _probe = 0;
        Rebuilds++;
        _lines.Clear();
        var winners = EffectRules.InEffect(q, _inEffect);
        for (var i = 0; i < _inEffect.Count; i++)
        {
            var entry = _inEffect[i];
            var spell = session.Spell(entry.SpellId);
            if (spell is null)
            {
                if (_probe == 0)
                {
                    _probe = entry.SpellId;
                }

                continue; // InqSpellBase failed: the entry is not listed
            }

            if (((spell.Flags & Lifestone.Data.Magic.SpellFlags.Beneficial) != 0) != _beneficial)
            {
                continue; // SpellEffectMatchesUIType
            }

            var place = 0;
            while (place < _lines.Count && string.CompareOrdinal(spell.Name, _lines[place].Name) > 0)
            {
                place++; // GetSortedInsertionPlace: stop at the first line whose name is not before this one
            }

            _lines.Insert(place, new Line(entry, spell.Id, spell.Name, spell.IconId, null, null, NothingShown));
        }

        if (_probe != 0)
        {
            _context.Manager.LogOnce("effects:spell-table", LogSeverity.Warning, "gmEffectsUI: a spell of the registry is not in the SpellTable (it has not loaded yet, or the table lacks the id); the line waits for it (TODO(m3-effects-spell-table))");
        }

        var list = _list!;
        list.Flush();
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (list.AddItemFromTemplateList(0) is { } item)
            {
                (item.GetChildRecursive(RowNameId) as UiTextElement)?.SetText(line.Name);
                item.GetChildRecursive(RowIconId)?.SetImageByDid(line.IconId, UiDrawMode.AlphaBlend);
                _lines[i] = line with { Item = item, Time = item.GetChildRecursive(RowTimeId) as UiTextElement };
            }
        }

        WriteTimes();
        if (winners > 0)
        {
            // RebuildList clears a selection whose spell is no longer enchanted, then UpdateSelection; with nothing in effect at all it returns before both (retail's quirk: the branch below fixes it unless EffectsStaleInfo is set)
            if (_selected != 0 && !EffectRules.IsEnchanted(q, _selected))
            {
                _selected = 0;
            }

            UpdateSelection();
        }
        else if ((_context.Manager.Services.RetailGlitches & UiRetailGlitches.EffectsStaleInfo) == 0)
        {
            // Lifestone's fix (the default; review 2.2-R7, decision D32): the last effect ended, so the selection and the info box clear, as on a character with no effects.
            // --retail-effects-stale-info (UiRetailGlitches.EffectsStaleInfo) keeps retail's early return above.
            _selected = 0;
            _info?.SetText(string.Empty);
        }
    }

    /// <summary><c>EffectInfoRegion::Update</c> for every line: the remaining time as text, written only when its whole seconds changed.</summary>
    private void WriteTimes()
    {
        var q = _context.Session.State.Self.Qualities;
        if ((q.CharacterOptions1 & EffectRules.SpellDurationOption) == 0)
        {
            return; // PlayerModule::SpellDuration is off: the value text is left alone
        }

        var now = _context.Session.Clock.Now;
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Time is null)
            {
                continue;
            }

            var seconds = EffectRules.TryRemainingWholeSeconds(line.Entry, now, out var remaining) ? remaining : -1;
            if (seconds == line.Shown)
            {
                continue; // the text shows these whole seconds already
            }

            line.Time.SetText(seconds >= 0 ? EffectRules.FormatDuration(seconds) : string.Empty);
            TimeWrites++;
            _lines[i] = line with { Shown = seconds };
        }
    }

    // ------------------------------------------------------------------ the selection and the info box

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId is UiMessageIds.ListBoxSelection or UiMessageIds.ListBoxReselect && ReferenceEquals(message.Element, _list))
        {
            SetSelectedSpell(message.Item);
            return UiListenResult.Handled;
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmEffectsUI::SetSelectedSpell</c> (0x004B8EB0): the same line again clears the selection.</summary>
    private void SetSelectedSpell(UiElement? item)
    {
        uint spell = 0;
        if (item is not null)
        {
            foreach (var line in _lines)
            {
                if (ReferenceEquals(line.Item, item))
                {
                    spell = line.SpellId;
                    break;
                }
            }
        }

        _selected = _selected == spell ? 0 : spell;
        UpdateSelection();
    }

    /// <summary><c>gmEffectsUI::UpdateSelection</c> (0x004B8BB0): the selected line shows the highlight state (6), the others the normal state (1); the info box says what is selected.</summary>
    private void UpdateSelection()
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].Item?.SetState(_lines[i].SpellId == _selected ? UiStateIds.Highlight : UiStateIds.Normal);
        }

        if (_info is null)
        {
            return;
        }

        if (_lines.Count == 0)
        {
            _info.SetText(Text("ID_Effects_Info_NoSpells", "You have no spells of this kind in effect."));
        }
        else if (_selected == 0)
        {
            _info.SetText(Text("ID_Effects_Info_SelectASpell", "Select a spell to see its description."));
        }
        else
        {
            var spell = _context.Session.Spell(_selected);
            _info.SetText(spell is null ? string.Empty : spell.Name + "\n\n" + spell.Description);
        }
    }

    private string Text(string token, string fallback)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference) && strings.TryGetString(reference, null, out var text))
        {
            return text;
        }

        _context.Manager.LogOnce("effects:" + token, LogSeverity.Warning, $"gmEffectsUI: the string {token} does not render in the string tables; it shows a placeholder (TODO(m3-effects-strings))");
        return fallback;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _enchantments.Dispose();
        _qualities.Dispose();
    }

    private readonly record struct Line(ActiveEnchantment Entry, uint SpellId, string Name, uint IconId, UiElement? Item, UiTextElement? Time, long Shown);
}
