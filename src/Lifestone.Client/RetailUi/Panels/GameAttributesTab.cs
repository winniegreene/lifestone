using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>One line of the attributes tab: an attribute (a primary stat) or a vital (a secondary stat, named by its maximum's id) and the list row that shows it.</summary>
/// <param name="IsVital">True for Health, Stamina and Mana.</param>
/// <param name="Id">The attribute id (1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self) or the vital's maximum id (1 health, 3 stamina, 5 mana).</param>
/// <param name="CurrentId">For a vital, the id of its current value (2, 4, 6: what retail's token holds and the stat icon's enum names).</param>
/// <param name="Label">The English name the row shows (<c>SkillSystem::InqAttributeName</c>, <c>InqAttribute2ndName</c>).</param>
/// <param name="Description">The row's tooltip (<c>SkillSystem::InqAttributeDescription</c>, <c>InqAttribute2ndDescription</c>).</param>
public sealed record AttributeLine(bool IsVital, uint Id, uint CurrentId, string Label, string Description)
{
    internal UiElement? Row { get; set; }

    internal UiTextElement? Value { get; set; }

    /// <summary>What the row shows now (empty until the page showed with a known description).</summary>
    public string ValueText => Value?.Text ?? string.Empty;

    /// <summary>The colour index of the row's value (0 normal, 1 raised by enchantments, 2 lowered): the text's current font colour is read from it.</summary>
    public int ValueColor { get; internal set; }
}

/// <summary>
/// The attributes tab of the skill page (page 11, <c>gmAttributeUI</c> 0x1000002A, element 0x1000022B; docs/retail-ui.md section 23), over <c>gmStatManagementUI</c> (the shared header, list and footers). The list holds the
/// six attributes in retail's order (<c>gmAttributeUI::PostInit</c>, 11.6096 0x0049DDE0: Strength, Endurance, Coordination, Quickness, Focus, Self) and the three vitals; a row's value is the stat with the
/// enchantment registry applied (<c>AttributeInfoRegion::Update</c> 0x004F29F0 reads <c>CACQualities::InqAttribute</c> with <c>raw</c> false, the vital's <c>Attribute2ndInfoRegion::Update</c> 0x004F2AE0 the
/// current over the enchanted maximum), in the font colour that says raised, lowered or unchanged against the base. Clicking a row selects it (<c>SetSelection</c> 0x0049CD30; the same row again clears it) and
/// <c>UpdateSelection</c> (0x0049E150) swaps the footer: with none selected it shows the skill credits and the unassigned experience (<c>DisplayDefaultFooter</c> 0x0049CDE0), with one selected its name,
/// value and the base-to-current difference, the experience a rank costs and the unassigned experience, and the two raise buttons (<c>DisplaySelectionFooter_Attribute</c> 0x0049D4F0, <c>_Vital</c> 0x0049D920).
/// The header (<c>gmStatManagementUI::UpdateCharacterInfo</c> 0x004F1400, <c>UpdateExperience</c> 0x004F1700) shows the name, the level, the total experience and the experience to the next level with its meter.
/// </summary>
/// <remarks>
/// <para><b>The raise buttons</b> (owner decision D29). The one-rank button (0x10000246) is <c>RaiseSelection</c> (0x0049D290) and the ten-rank button (0x100005EB) is <c>Raise10Selection</c> (0x0049D3C0):
/// each sends <c>CM_Train::Event_TrainAttribute</c> or <c>Event_TrainAttribute2nd</c> with the cost <c>GetCostToRaise</c> (0x0049CDF0) or <c>GetCostToRaise10</c> (0x0049CEE0) give, which is the XpTable's
/// cumulative cost of the target rank minus the experience already spent on the stat (not the difference of two list entries), and none when the rank is capped (attribute 190, vital 196). A button is
/// ghosted (state 13) when the cost is zero or the unassigned experience does not cover it; a click sends <see cref="IActionApi.RaiseAttribute"/> or <see cref="IActionApi.RaiseVital"/> with
/// <see cref="ActionSource.Ui"/>, ghosts the buttons and ignores further clicks until the raise's action ends (<c>m_bAwaitingRaise</c>; a vital's tick or the experience update does not end it, review 2.2-R1). Tests and play-checks click them on the stand-in only.</para>
/// <para><b>Placeholders.</b> The header's second line (gender, heritage and title: <c>AppraisalSystem::InqGenderHeritageDisplay</c> is not read) is empty with a logged <c>TODO(m3-attribute-heritage)</c>; the player-kill status
/// line always says non-player killer (<c>TODO(m3-pk-status)</c>); the luminance lines are empty (a character below level 200 shows none in retail); the character titles tab shows its art and logs
/// <c>TODO(m3-character-titles)</c> once when it is selected. Not placeholders: everything else on the tab.</para>
/// <para><b>Frames.</b> The rows are built when the page first shows; a change (the qualities or the enchantments) only marks the tab dirty and the redraw happens once, in <see cref="Step"/>, while the tab shows.</para>
/// </remarks>
public sealed class GameAttributesTab : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The skill page's panel id.</summary>
    public const uint SkillPagePanelId = 11;

    /// <summary>The attributes tab button (the panel's first tab).</summary>
    public const uint TabId = 0x10000228;

    /// <summary>The attributes tab's page element (class <c>gmAttributeUI</c>).</summary>
    public const uint PageId = 0x1000022B;

    /// <summary>The character titles tab button.</summary>
    public const uint TitlesTabId = 0x10000538;

    /// <summary>The character titles page (class <c>gmCharacterTitleUI</c>).</summary>
    public const uint TitlesPageId = 0x10000539;

    /// <summary>The class id of <c>gmAttributeUI</c>.</summary>
    public const uint AttributeClassId = 0x1000002A;

    /// <summary>The class id of <c>gmCharacterTitleUI</c>.</summary>
    public const uint TitlesClassId = 0x10000046;

    /// <summary>The page state of the default footer (skill credits and experience).</summary>
    public const uint DefaultFooterState = 0x10000011;

    /// <summary>The page state of the selection footer (the name, the cost, the raise buttons).</summary>
    public const uint SelectionFooterState = 0x10000012;

    /// <summary>The list box.</summary>
    public const uint ListId = 0x1000023D;

    /// <summary>The footer shown with nothing selected.</summary>
    public const uint DefaultFooterId = 0x10000240;

    /// <summary>The footer shown with a stat selected.</summary>
    public const uint SelectionFooterId = 0x10000241;

    /// <summary>The raise-one-rank button (<c>gmStatManagementUI::GetFooterButton</c>).</summary>
    public const uint RaiseButtonId = 0x10000246;

    /// <summary>The raise-ten-ranks button (<c>GetFooter10Button</c>).</summary>
    public const uint Raise10ButtonId = 0x100005EB;

    /// <summary>The string table enum (0x10000001) of the <c>ID_StatManagement_*</c> strings.</summary>
    public const uint StringTableEnum = StatHeader.StringTableEnum;

    /// <summary>The enum group of the attributes' icons (<c>DBObj::GetDIDByEnum(.., attribute, 0x10000002)</c> in <c>PostInit</c>).</summary>
    public static readonly EnumGroup AttributeIcons = new(0x10000002);

    /// <summary>The enum group of the vitals' icons (0x10000003).</summary>
    public static readonly EnumGroup VitalIcons = new(0x10000003);

    // The row template's children (InfoRegion::InfoRegion 0x004F20B0): the icon, the label and the value.
    private const uint RowIconId = 0x10000129;
    private const uint RowLabelId = 0x1000012A;
    private const uint RowValueId = 0x1000012B;

    // A footer's text (gmStatManagementUI::GetFooterTitleLabel 0x004F0150 ... GetFooterLineTwoValue).
    private const uint TitleId = 0x1000024E;
    private const uint LineOneLabelId = 0x10000242;
    private const uint LineOneValueId = 0x10000243;
    private const uint LineTwoLabelId = 0x10000244;
    private const uint LineTwoValueId = 0x10000245;

    // The string-table variables (the hashes of the names the dat strings use).
    private static readonly uint SkillCreditsVariable = StringHash.Tokenize("SKILL_CREDITS");
    private static readonly uint AvailableExperienceVariable = StringHash.Tokenize("AVAILABLE_EXPERIENCE");
    private static readonly uint ExperienceToRaiseVariable = StringHash.Tokenize("XP_TO_RAISE");

    private static readonly AttributeLine[] Lines =
    [
        new(false, 1, 0, "Strength", "Measures your character's muscular power."),
        new(false, 2, 0, "Endurance", "Measures how healthy your character is."),
        new(false, 4, 0, "Coordination", "Measures your character's reflexes"),
        new(false, 3, 0, "Quickness", "Measures how fast your character is."),
        new(false, 5, 0, "Focus", "Measures your character's mind and senses."),
        new(false, 6, 0, "Self", "Measures your character's willpower."),
        new(true, 1, 2, "Health", "(Endurance/2)\nIf you run out of health, you will die!"),
        new(true, 3, 4, "Stamina", "(Endurance)\nAffects your actions and movement."),
        new(true, 5, 6, "Mana", "(Self)\nAffects how much magic you can cast."),
    ];

    private readonly UiLoginContext _context;
    private readonly UiPanel? _panel;
    private readonly UiElement _page;
    private readonly UiListBox? _list;
    private readonly UiElement? _defaultFooter;
    private readonly UiElement? _selectionFooter;
    private readonly UiButton? _raiseButton;
    private readonly UiButton? _raise10Button;
    private readonly StatHeader _header;
    private readonly UiStringVariables _variables = new();
    private readonly AttributeLine[] _lines;
    private readonly IDisposable _qualitiesSubscription;
    private readonly IDisposable _enchantmentSubscription;
    private readonly Action<ActionHandle> _onRaiseCompleted;
    private bool _built;
    private bool _dirty = true;
    private bool _tabWasShown;
    private bool _awaitingRaise;
    private bool _titlesLogged;
    private bool _disposed;
    private int _selected = -1;

    private GameAttributesTab(UiLoginContext context, UiPanel? panel, UiElement page)
    {
        _context = context;
        _panel = panel;
        _page = page;
        _list = page.GetChildRecursive(ListId) as UiListBox;
        _defaultFooter = page.GetChildRecursive(DefaultFooterId);
        _selectionFooter = page.GetChildRecursive(SelectionFooterId);
        _raiseButton = _selectionFooter?.GetChildRecursive(RaiseButtonId) as UiButton;
        _raise10Button = _selectionFooter?.GetChildRecursive(Raise10ButtonId) as UiButton;
        _header = new StatHeader(context, page);
        _lines = new AttributeLine[Lines.Length];
        for (var i = 0; i < Lines.Length; i++)
        {
            var template = Lines[i];
            _lines[i] = new AttributeLine(template.IsVital, template.Id, template.CurrentId, template.Label, template.Description);
        }

        _onRaiseCompleted = OnRaiseCompleted;
        _qualitiesSubscription = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
        _enchantmentSubscription = context.Session.Events.Subscribe<EnchantmentChanged>(OnEnchantmentChanged);
        page.RegisterForElementMessages(this);
    }

    /// <summary>The skill page's panel id (11).</summary>
    public uint PanelId => SkillPagePanelId;

    /// <summary>The nine lines in the order the list shows them: the six attributes, then Health, Stamina and Mana.</summary>
    public IReadOnlyList<AttributeLine> StatLines => _lines;

    /// <summary>The selected line's index, or -1.</summary>
    public int SelectedIndex => _selected;

    /// <summary>How many times the tab was redrawn (a test reads it: a hidden or unchanged tab does not redraw).</summary>
    public int Redraws { get; private set; }

    /// <summary>True when the attributes tab is the open tab of the page.</summary>
    public bool IsTabShown => _page.Visible;

    /// <summary>The raise-one-rank button, or null when the layout lacks it.</summary>
    public UiButton? RaiseButton => _raiseButton;

    /// <summary>The raise-ten-ranks button.</summary>
    public UiButton? Raise10Button => _raise10Button;

    /// <summary>The list box.</summary>
    public UiListBox? List => _list;

    /// <summary>The selection footer's title (the selected stat's name, its value and the base difference).</summary>
    public string FooterTitle => (CurrentFooter()?.GetChildRecursive(TitleId) as UiTextElement)?.Text ?? string.Empty;

    /// <summary>The selection footer's first line, label and value joined by a space.</summary>
    public string FooterLineOne => Joined(LineOneLabelId, LineOneValueId);

    /// <summary>The selection footer's second line, label and value joined by a space.</summary>
    public string FooterLineTwo => Joined(LineTwoLabelId, LineTwoValueId);

    /// <summary>The header's total experience text.</summary>
    public string TotalExperienceText => _header.TotalExperienceText;

    /// <summary>The header's experience-to-level text.</summary>
    public string ExperienceToLevelText => _header.ExperienceToLevelText;

    /// <summary>The header's level text.</summary>
    public string LevelText => _header.LevelText;

    /// <summary>The header's name text.</summary>
    public string NameText => _header.NameText;

    /// <summary>The experience-to-level meter's position (0 to 1).</summary>
    public double ExperienceMeterPosition => _header.MeterPosition;

    /// <summary>Makes the controller for the skill page (the factory <c>GamePanels.A1.cs</c> queues); null, with the reason logged, when the page is missing from the layout.</summary>
    public static GameAttributesTab? Create(GamePanels panels)
    {
        ArgumentNullException.ThrowIfNull(panels);
        var log = panels.Context.Manager.Services.Log;
        if (panels.Host.ElementFor(SkillPagePanelId) is not { } root)
        {
            log.Error(UiElementManager.Category, $"gmAttributeUI: the skill page (panel {SkillPagePanelId}) is missing from the layout; the attributes tab is not built (TODO(g4-panel-page))");
            return null;
        }

        if (root.GetChildRecursive(PageId) is not { } page)
        {
            log.Error(UiElementManager.Category, $"gmAttributeUI: the attributes page 0x{PageId:X8} is missing from the skill page; the attributes tab is not built (TODO(m3-attributes-layout))");
            return null;
        }

        return new GameAttributesTab(panels.Context, root as UiPanel, page);
    }

    /// <summary>The line named by a stat: the attribute <paramref name="id"/> or the vital whose maximum is <paramref name="id"/>.</summary>
    public AttributeLine? LineOf(bool vital, uint id)
    {
        foreach (var line in _lines)
        {
            if (line.IsVital == vital && line.Id == id)
            {
                return line;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void OnShown()
    {
        _dirty = true;
        if (_page.Visible)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public void OnHidden()
    {
    }

    /// <inheritdoc/>
    public void Step(double now)
    {
        var shown = _page.Visible;
        if (shown && !_tabWasShown)
        {
            _dirty = true; // the tab was opened again (a tab click is not a page show)
        }

        _tabWasShown = shown;
        if (_panel is { } panel && panel.OpenPageId == TitlesPageId && !_titlesLogged)
        {
            _titlesLogged = true;
            _context.Manager.Services.Log.Warn("retailui", "TODO(m3-character-titles): gmCharacterTitleUI (the character titles tab) is outside M3's attributes slice; it shows the layout's art only");
        }

        if (shown && _dirty)
        {
            Refresh();
        }
    }

    /// <summary>Redraws the tab now (the rows on first use, then the values, the header and the footer) and clears the dirty mark. Does nothing until the description arrived.</summary>
    public void Refresh()
    {
        var q = _context.Session.State.Self.Qualities;
        if (!q.IsKnown)
        {
            return;
        }

        if (!_built)
        {
            BuildRows();
        }

        _dirty = false;
        Redraws++;
        var stats = new StatValues(q); // the registry read once for this redraw
        for (var i = 0; i < _lines.Length; i++)
        {
            UpdateRow(stats, _lines[i]);
        }

        _header.Update(q);
        UpdateSelection(q, stats);
    }

    /// <summary><c>gmAttributeUI::SetSelection</c> (11.6096 0x0049CD30): selects the line <paramref name="index"/>; the line that is selected already clears the selection (so does -1).</summary>
    public void Select(int index)
    {
        _selected = _selected == index ? -1 : index;
        var q = _context.Session.State.Self.Qualities;
        if (q.IsKnown && _built)
        {
            UpdateSelection(q, new StatValues(q));
        }
    }

    /// <summary>The cost <c>GetCostToRaise</c> (0x0049CDF0) or <c>GetCostToRaise10</c> (0x0049CEE0) give for <paramref name="line"/>: 0 when the rank is capped, the XpTable is not installed or the stat is unknown.</summary>
    public ulong CostToRaise(AttributeLine line, bool ten)
    {
        ArgumentNullException.ThrowIfNull(line);
        var table = _context.Session.XpTable;
        if (table is null)
        {
            _context.Manager.LogOnce("attributes:xp-table", LogSeverity.Warning, "gmAttributeUI: the XpTable is not loaded (GameSession.UseStatTablesFrom); the raise buttons stay ghosted (TODO(m3-stat-tables))");
            return 0;
        }

        return new StatValues(_context.Session.State.Self.Qualities).CostToRaise(line.IsVital, line.Id, ten, table);
    }

    /// <summary><c>gmAttributeUI::RaiseSelection</c> (0x0049D290) and <c>Raise10Selection</c> (0x0049D3C0): sends the raise of the selected stat. False when nothing is selected, a raise is awaited, the cost is none or the experience does not cover it.</summary>
    public bool Raise(bool ten)
    {
        if (_selected < 0 || _awaitingRaise)
        {
            return false;
        }

        var line = _lines[_selected];
        var cost = CostToRaise(line, ten);
        var q = _context.Session.State.Self.Qualities;
        if (cost == 0 || cost > uint.MaxValue || q.AvailableExperience < (long)cost)
        {
            return false;
        }

        _awaitingRaise = true;
        var actions = _context.Session.Actions;
        var handle = line.IsVital ? actions.RaiseVital(line.Id, (uint)cost, ActionSource.Ui) : actions.RaiseAttribute(line.Id, (uint)cost, ActionSource.Ui);
        _raiseButton?.SetState(UiStateIds.Ghosted); // the footer's buttons wait for the stat's update
        _raise10Button?.SetState(UiStateIds.Ghosted);
        if (handle.IsCompleted)
        {
            OnRaiseCompleted(handle);
        }
        else
        {
            handle.Completed += _onRaiseCompleted;
        }

        return true;
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        // gmStatManagementUI::ListenToElementMessage (11.6096 0x004F0870): a click on a row selects it, a click on a footer button raises.
        if (message.MessageId == UiMessageIds.MouseDown && message.Param1 is UiMouseActions.Left or UiMouseActions.LeftDouble or UiMouseActions.Right or UiMouseActions.RightDouble)
        {
            if (_list?.GetItemUnderMouse() is { } item)
            {
                for (var i = 0; i < _lines.Length; i++)
                {
                    if (ReferenceEquals(_lines[i].Row, item))
                    {
                        Select(i);
                        return UiListenResult.Handled;
                    }
                }
            }

            return UiListenResult.Default;
        }

        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            if (ReferenceEquals(message.Element, _raiseButton))
            {
                Raise(ten: false);
                return UiListenResult.Handled;
            }

            if (ReferenceEquals(message.Element, _raise10Button))
            {
                Raise(ten: true);
                return UiListenResult.Handled;
            }
        }

        return UiListenResult.Default;
    }

    /// <summary>Stops listening to the session.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _qualitiesSubscription.Dispose();
        _enchantmentSubscription.Dispose();
    }

    // ---------------------------------------------------------------- events

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        // m_bAwaitingRaise (gmStatManagementUI::ListenToElementMessage 0x004F0870) clears when the raise's own answer arrives, not on any qualities change: a vital's tick
        // and ACE's AvailableExperience update (which comes before the stat's own) must not free the buttons while the raise is in flight (review 2.2-R1). OnRaiseCompleted clears it.
        _dirty = true;
    }

    private void OnEnchantmentChanged(in EnchantmentChanged change) => _dirty = true;

    private void OnRaiseCompleted(ActionHandle handle)
    {
        handle.Completed -= _onRaiseCompleted;
        _awaitingRaise = false;
        _dirty = true;
    }

    // ---------------------------------------------------------------- rows

    private void BuildRows()
    {
        _built = true;
        if (_list is null)
        {
            _context.Manager.Services.Log.Error(UiElementManager.Category, "gmAttributeUI::PostInit: the list box 0x1000023D is missing; the attributes tab has no rows (TODO(m3-attributes-layout))");
            return;
        }

        var maps = _context.Manager.Services.EnumMaps;
        foreach (var line in _lines)
        {
            // InfoRegion::InfoRegion (0x004F20B0): the row comes from the list's template 0; its icon, label and value are found by id.
            var row = _list.AddItemFromTemplateList(0);
            line.Row = row;
            if (row is null)
            {
                continue;
            }

            line.Value = row.GetChildRecursive(RowValueId) as UiTextElement;
            (row.GetChildRecursive(RowLabelId) as UiTextElement)?.SetText(line.Label);
            if (maps is not null && maps.TryResolve(line.IsVital ? VitalIcons : AttributeIcons, line.IsVital ? line.CurrentId : line.Id, out var icon))
            {
                row.GetChildRecursive(RowIconId)?.SetImageByDid(icon, UiDrawMode.AlphaBlend);
            }

            row.SetBoolProperty(UiPropertyIds.ElementTooltipsOn, true);
            row.SetTooltip(UiStringRef.FromLiteral(line.Description));
        }
    }

    private static void UpdateRow(StatValues stats, AttributeLine line)
    {
        if (line.Value is not { } value)
        {
            return;
        }

        if (!line.IsVital)
        {
            // AttributeInfoRegion::Update (0x004F29F0): the value with the registry applied; the colour compares it with the base.
            if (stats.TryAttribute(line.Id, out var raw, out var current))
            {
                Write(line, value, current.ToString(CultureInfo.InvariantCulture), ColorOf(raw, current));
            }

            return;
        }

        // Attribute2ndInfoRegion::Update (0x004F2AE0): current over the enchanted maximum; the vitae alone does not change the colour (GetVitaeModifier, 0x004F1D90).
        if (stats.TryVital(line.Id, out var cur, out var baseMax, out var max, out var vitae))
        {
            Write(line, value, string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}"), ColorOf(baseMax, (long)max - vitae));
        }
    }

    private static int ColorOf(long raw, long modified) => raw < modified ? 1 : raw > modified ? 2 : 0;

    private static void Write(AttributeLine line, UiTextElement value, string text, int color)
    {
        // UIElement_Text::SetTextWithFont: the text replaced, in font 0 and the colour index.
        value.ClearAllText();
        value.AppendTextWithFont(text, 0, color);
        line.ValueColor = color;
    }

    // ---------------------------------------------------------------- selection and footer

    private void UpdateSelection(PlayerQualities q, StatValues stats)
    {
        // gmAttributeUI::UpdateSelection (0x0049E150): every row's state (selected 6, else 1), then the footer.
        for (var i = 0; i < _lines.Length; i++)
        {
            _lines[i].Row?.SetState(i == _selected ? UiStateIds.Highlight : UiStateIds.Normal);
        }

        if (_selected < 0)
        {
            _page.SetState(DefaultFooterState);
            DefaultFooter(q);
            return;
        }

        _page.SetState(SelectionFooterState);
        var line = _lines[_selected];
        if (line.IsVital)
        {
            VitalFooter(q, stats, line);
        }
        else
        {
            AttributeFooter(q, stats, line);
        }
    }

    private UiElement? CurrentFooter() => _selected < 0 ? _defaultFooter : _selectionFooter;

    private void DefaultFooter(PlayerQualities q)
    {
        // DisplayDefaultFooter (0x0049CDE0).
        if (_defaultFooter is not { } footer)
        {
            return;
        }

        SetFooterText(footer, TitleId, Plain("ID_StatManagement_Footer_DefaultAttributeTitle", "Select an Attribute to Improve"));
        SetFooterText(footer, LineOneLabelId, Plain("ID_StatManagement_Footer_SkillCreditsLabel", "Skill Credits Available:"));
        SetFooterText(footer, LineOneValueId, Render("ID_StatManagement_Footer_SkillCreditsValue", SkillCreditsVariable, q.AvailableSkillCredits, $"{q.AvailableSkillCredits}"));
        SetFooterText(footer, LineTwoLabelId, Plain("ID_StatManagement_Footer_ExperienceLabel", "Unassigned Experience:"));
        SetFooterText(footer, LineTwoValueId, Render("ID_StatManagement_Footer_ExperienceValue", AvailableExperienceVariable, q.AvailableExperience, $"{q.AvailableExperience}"));
    }

    private void AttributeFooter(PlayerQualities q, StatValues stats, AttributeLine line)
    {
        // DisplaySelectionFooter_Attribute (0x0049D4F0): "Name: modified", then " (+N)" in the raised or lowered colour.
        if (_selectionFooter is not { } footer || !stats.TryAttribute(line.Id, out var raw, out var modified))
        {
            return;
        }

        var title = (UiTextElement?)footer.GetChildRecursive(TitleId);
        title?.ClearAllText();
        title?.AppendTextWithFont($"{line.Label}: {modified}", 0, 0);
        if (modified != raw)
        {
            var difference = (long)modified - raw;
            title?.AppendTextWithFont(string.Create(CultureInfo.InvariantCulture, $" ({(difference > 0 ? "+" : string.Empty)}{difference})"), 0, ColorOf(raw, modified));
        }

        FooterCost(q, footer, line);
    }

    private void VitalFooter(PlayerQualities q, StatValues stats, AttributeLine line)
    {
        // DisplaySelectionFooter_Vital (0x0049D920): "Name: current/maximum", the vitae's loss, then the other difference.
        if (_selectionFooter is not { } footer || !stats.TryVital(line.Id, out var cur, out var baseMax, out var max, out var vitae))
        {
            return;
        }

        var title = (UiTextElement?)footer.GetChildRecursive(TitleId);
        title?.ClearAllText();
        title?.AppendTextWithFont(string.Create(CultureInfo.InvariantCulture, $"{line.Label}: {cur}/{max}"), 0, 0);
        if (vitae < 0)
        {
            title?.AppendTextWithFont(string.Create(CultureInfo.InvariantCulture, $" ({vitae})"), 0, 3);
        }

        var modified = (long)max - vitae;
        if (modified != baseMax)
        {
            var difference = modified - baseMax;
            title?.AppendTextWithFont(string.Create(CultureInfo.InvariantCulture, $" ({(difference > 0 ? "+" : string.Empty)}{difference})"), 0, ColorOf(baseMax, modified));
        }

        FooterCost(q, footer, line);
    }

    private void FooterCost(PlayerQualities q, UiElement footer, AttributeLine line)
    {
        var one = CostToRaise(line, ten: false);
        var ten = CostToRaise(line, ten: true);
        SetFooterText(footer, LineOneLabelId, Plain("ID_StatManagement_Footer_XPToRaiseLabel", "Experience To Raise:"));
        SetFooterText(footer, LineOneValueId, one != 0
            ? Render("ID_StatManagement_Footer_XPToRaiseValue", ExperienceToRaiseVariable, (long)one, $"{one}")
            : Plain("ID_StatManagement_Header_XPToLevelMeterInfinity", "Infinity!"));
        SetFooterText(footer, LineTwoLabelId, Plain("ID_StatManagement_Footer_ExperienceLabel", "Unassigned Experience:"));
        SetFooterText(footer, LineTwoValueId, Render("ID_StatManagement_Footer_ExperienceValue", AvailableExperienceVariable, q.AvailableExperience, $"{q.AvailableExperience}"));
        var available = q.AvailableExperience;
        _raiseButton?.SetState(!_awaitingRaise && one != 0 && available >= 0 && (ulong)available >= one ? UiStateIds.Normal : UiStateIds.Ghosted);
        _raise10Button?.SetState(!_awaitingRaise && ten != 0 && available >= 0 && (ulong)available >= ten ? UiStateIds.Normal : UiStateIds.Ghosted);
    }

    private static void SetFooterText(UiElement footer, uint id, string text)
    {
        if (footer.GetChildRecursive(id) is UiTextElement element)
        {
            element.SetText(text);
        }
    }

    private string Joined(uint labelId, uint valueId)
    {
        var footer = CurrentFooter();
        var label = (footer?.GetChildRecursive(labelId) as UiTextElement)?.Text ?? string.Empty;
        var value = (footer?.GetChildRecursive(valueId) as UiTextElement)?.Text ?? string.Empty;
        return label + " " + value;
    }

    // ---------------------------------------------------------------- strings

    private string Plain(string token, string fallback)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference) && strings.TryGetString(reference, null, out var text))
        {
            return text;
        }

        _context.Manager.LogOnce("attributes:" + token, LogSeverity.Warning, $"gmAttributeUI: the string {token} does not render in the string tables; it shows '{fallback}' (TODO(m3-attributes-strings))");
        return fallback;
    }

    private string Render(string token, uint variable, long value, string fallback)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings is not null && strings.TryReference(StringTableEnum, token, out var reference))
        {
            if (strings.Language is { } language)
            {
                _variables.SetInteger(variable, value, language);
            }
            else
            {
                _variables.SetText(variable, value.ToString(CultureInfo.InvariantCulture));
            }

            if (strings.TryGetString(reference, _variables, out var text))
            {
                return text;
            }
        }

        _context.Manager.LogOnce("attributes:" + token, LogSeverity.Warning, $"gmAttributeUI: the string {token} does not render in the string tables; it shows '{fallback}' (TODO(m3-attributes-strings))");
        return fallback;
    }
}
