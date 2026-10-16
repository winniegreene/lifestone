using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

// 11.6096 addresses; the symbol table pins gmSkillUI::* at 0x0049B040 to 0x0049CB30 and the shared base class gmStatManagementUI::* (list, footer, buttons) at 0x004F0870 to 0x004F1700.
/// <summary>
/// The skills tab of the skill page (page 11, <c>gmSkillUI</c> 0x1000002B in the tab element 0x1000022C; docs/retail-ui.md section 21.3). Retail's class lists every skill of the SkillTable grouped by the
/// character's advancement class and sorted by name inside each group (<c>RebuildSkillList</c> 0x0049C610 and <c>AddSortedSkill</c> 0x0049C040): the specialised skills under their header, then the trained, then the
/// untrained (those usable at once: <c>_min_level</c> 1) and last the unusable ones (a skill below its <c>_min_level</c>, or one the description marks 0). A row (template 0) shows the skill's icon, its name and its
/// current value (<c>SkillInfoRegion::Update</c> 0x004F2740; <see cref="SkillRules"/>), green when enchantments raise it above the base and red when they lower it. Clicking a row selects it (a header or the same row again
/// clears the selection, <c>SetSelection</c> 0x0049B2B0); the footer then changes with it (<c>UpdateSelection</c> 0x0049C310): no selection shows the skill credits and the unassigned experience
/// (<c>DisplayDefaultFooter</c> 0x0049B380), an untrained skill the credits it costs to train (<c>DisplaySelectionFooter_Untrained</c> 0x0049B5C0) and a trained or specialised one its value with the base's difference,
/// the experience to raise it, a meter of the progress to the next rank and the unassigned experience (<c>DisplaySelectionFooter_Trained</c> 0x0049BAD0).
/// </summary>
/// <remarks>
/// <para><b>Buttons (owner decision D29: they work).</b> The raise button spends the experience of one rank (<c>RaiseSelection</c> 0x0049CB30; <see cref="SkillRules.CostToRaise(XpTableData, PlayerQualities, uint)"/>) through
/// <c>IActionApi.RaiseSkill</c>, the "10" button the experience of ten ranks (<c>Raise10Selection</c> 0x0049B9D0; <see cref="SkillRules.CostToRaise10(XpTableData, PlayerQualities, uint)"/>); the train button on an untrained skill first asks
/// "Are you sure you want to spend N credits to train X?" (<c>TrainSkill</c> 0x0049C860, a confirmation dialog) and then calls <c>IActionApi.TrainSkill</c> with the SkillTable's trained cost
/// (<c>TrainSkillDialogCallback</c> 0x0049C480). All are <see cref="ActionSource.Ui"/>. A button is ghosted when the experience or the credits do not cover its cost, and while an earlier raise awaits its answer
/// (retail's <c>m_bAwaitingRaise</c>, cleared by the skill's update).</para>
/// <para><b>When it builds.</b> Construction only finds the elements. The list is built the first time the tab shows (the tab element's visibility message, <c>gmStatManagementUI::ListenToElementMessage</c>
/// 0x004F0870), never in the constructor, and is rebuilt when a skill changes its advancement class (<c>RecvNotice_SkillAdvancementClassChanged</c>). Session events only mark the tab dirty; <see cref="Step"/> redraws
/// once, while the tab shows, and a row's text is written only when its value or colour changed.</para>
/// <para><b>The header.</b> The page header (name, level, total experience, the experience to the next level and its meter: <c>gmStatManagementUI::UpdateCharacterInfo</c> 0x004F1400 and <c>UpdateExperience</c> 0x004F1700)
/// is written by <see cref="StatHeader"/>, the helper this tab shares with the attributes tab, each time the tab redraws.</para>
/// <para><b>Placeholders.</b> The header's heritage line and kill status are the helper's (<c>TODO(m3-attribute-heritage)</c>, <c>TODO(m3-pk-status)</c>); a row has no tooltip (<c>SkillInfoRegion::GetTooltip</c> 0x004F2C40, <c>TODO(m3-skill-tooltips)</c>).
/// The augmentation bonuses are not in the value (<c>TODO(m3-skill-aug-bonus)</c>, <see cref="SkillRules"/>).</para>
/// </remarks>
public sealed class GameSkillsTab : IGamePanel, IUiElementListener, IDisposable
{
    /// <summary>The panel id of the skill page.</summary>
    public const uint Page = 11;

    /// <summary>The tab's element, <c>gmSkillUI</c> (class 0x1000002B).</summary>
    public const uint ElementId = 0x1000022C;

    /// <summary>The class id of <c>gmSkillUI</c>.</summary>
    public const uint ClassId = 0x1000002B;

    /// <summary>The skill list (0x1000023D).</summary>
    public const uint ListId = 0x1000023D;

    /// <summary>The footer of no selection (state 0x10000011).</summary>
    public const uint DefaultFooterId = 0x10000240;

    /// <summary>The footer of an untrained selection (state 0x10000012).</summary>
    public const uint UntrainedFooterId = 0x10000241;

    /// <summary>The footer of a trained or specialised selection (state 0x10000013).</summary>
    public const uint TrainedFooterId = 0x10000247;

    /// <summary>A footer's title (0x1000024E), first line's label and value, second line's label and value.</summary>
    public const uint TitleId = 0x1000024E, LineOneLabelId = 0x10000242, LineOneValueId = 0x10000243, LineTwoLabelId = 0x10000244, LineTwoValueId = 0x10000245;

    /// <summary>A footer's raise (or train) button (0x10000246), its "10" button (0x100005EB) and the trained footer's meter (0x10000247, inside the field of the same id).</summary>
    public const uint RaiseButtonId = 0x10000246, Raise10ButtonId = 0x100005EB, MeterId = 0x10000247;

    /// <summary>The row template's children: the icon, the name and the value (<c>InfoRegion::InfoRegion</c>, 11.6096 0x004F1480).</summary>
    public const uint RowIconId = 0x10000129, RowLabelId = 0x1000012A, RowValueId = 0x1000012B;

    /// <summary>The element states the footers follow (<c>UpdateSelection</c> sets 0x10000011, 0x10000012 or 0x10000013).</summary>
    public const uint DefaultState = 0x10000011, UntrainedState = 0x10000012, TrainedState = 0x10000013;

    /// <summary>The string table enum of the <c>ID_StatManagement_*</c> strings (0x10000001).</summary>
    public const uint TableEnum = 0x10000001;

    private const uint RowNormalState = 1;
    private const uint RowSelectedState = 6;
    private const int ColorNormal = 0;
    private const int ColorRaised = 1;
    private const int ColorLowered = 2;
    private const int ColorVitae = 3;

    // The four section headers are templates 1 to 4 of the list, row is template 0 (RebuildSkillList 0x0049C610).
    private static readonly int[] SectionTemplates = [1, 2, 3, 4];

    private readonly UiLoginContext _context;
    private readonly UiElement? _element;
    private readonly UiListBox? _list;
    private readonly Footer _defaultFooter;
    private readonly Footer _untrainedFooter;
    private readonly Footer _trainedFooter;
    private readonly IDisposable _subscription;
    private readonly StatHeader? _header;
    private readonly List<Row> _rows = new(48);
    private readonly List<SkillTableEntry>[] _sections = [new(8), new(16), new(16), new(8)];
    private bool _pageShown;
    private bool _dirty = true;
    private bool _built;
    private bool _awaiting;
    private bool _tablesTodoLogged;
    private uint _selectedSkill;
    private uint _dialog;

    /// <summary>The tab of <paramref name="host"/>'s skill page (the elements are only looked up; the list builds when the tab first shows).</summary>
    public GameSkillsTab(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        _context = context;
        _element = host.ElementFor(Page)?.GetChildRecursive(ElementId);
        if (_element is null)
        {
            context.Manager.Services.Log.Error(UiElementManager.Category, "gmSkillUI: the skills tab (0x1000022C) is missing from the skill page; the tab shows its art only (TODO(m3-skills-tab-missing))");
            _list = null;
            _defaultFooter = _untrainedFooter = _trainedFooter = default;
        }
        else
        {
            _list = _element.GetChildRecursive(ListId) as UiListBox;
            _defaultFooter = new Footer(_element.GetChildRecursive(DefaultFooterId));
            _untrainedFooter = new Footer(_element.GetChildRecursive(UntrainedFooterId));
            _trainedFooter = new Footer(_element.GetChildRecursive(TrainedFooterId));
            _header = new StatHeader(context, _element);
            _element.RegisterForElementMessages(this);
        }

        _subscription = context.Session.Events.Subscribe<QualitiesChanged>(OnQualitiesChanged);
    }

    /// <inheritdoc/>
    public uint PanelId => Page;

    /// <summary>True once the list has been built (the tab has shown with the tables and the player's description known).</summary>
    public bool IsBuilt => _built;

    /// <summary>How many times the list was built (a change of advancement class builds it again).</summary>
    public int Builds { get; private set; }

    /// <summary>How many times the rows' values were redrawn (a build counts).</summary>
    public int Redraws { get; private set; }

    /// <summary>The tab's list box (the attributes tab has one of the same id, so look it up here, not through the page).</summary>
    public UiListBox? List => _list;

    /// <summary>The skill selected (0 for none).</summary>
    public uint SelectedSkill => _selectedSkill;

    /// <summary>The rows in list order, for the tests and the play check: skill id, section (0 specialised ... 3 unusable) and the value text shown.</summary>
    public IEnumerable<(uint Skill, int Section, string Value)> Lines
    {
        get
        {
            foreach (var row in _rows)
            {
                yield return (row.Skill, row.Section, row.Value?.Text ?? string.Empty);
            }
        }
    }

    /// <summary>The row of <paramref name="skill"/>, or null.</summary>
    public UiElement? RowOf(uint skill)
    {
        foreach (var row in _rows)
        {
            if (row.Skill == skill)
            {
                return row.Element;
            }
        }

        return null;
    }

    /// <summary>The footer state now (0x10000011, 0x10000012 or 0x10000013).</summary>
    public uint FooterState { get; private set; } = DefaultState;

    /// <summary>The footer the state shows.</summary>
    public UiElement? FooterElement => FooterState == UntrainedState ? _untrainedFooter.Field : FooterState == TrainedState ? _trainedFooter.Field : _defaultFooter.Field;

    /// <summary>The raise (or train) button of the footer shown.</summary>
    public UiElement? RaiseButton => CurrentFooter.Raise;

    /// <summary>The "10" button of the footer shown (only the trained footer's shows).</summary>
    public UiElement? Raise10Button => CurrentFooter.Raise10;

    /// <summary>The last raise or train this tab asked for (null before the first).</summary>
    public ActionHandle? LastAction { get; private set; }

    private Footer CurrentFooter => FooterState == UntrainedState ? _untrainedFooter : FooterState == TrainedState ? _trainedFooter : _defaultFooter;

    /// <inheritdoc/>
    public void OnShown()
    {
        _pageShown = true;
        if (_element?.Visible == true)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    public void OnHidden() => _pageShown = false;

    /// <inheritdoc/>
    public void Step(double now)
    {
        if (_dirty && _pageShown && _element?.Visible == true)
        {
            Refresh();
        }
    }

    /// <summary>Redraws the tab now if it shows (builds the list the first time). The play checks and the tests call it; the page calls it as it shows and from <see cref="Step"/>.</summary>
    public void Refresh()
    {
        if (_element is null || _list is null)
        {
            return;
        }

        var tables = _context.Session;
        if (tables.SkillTable is null || tables.XpTable is null)
        {
            if (!_tablesTodoLogged)
            {
                _tablesTodoLogged = true;
                _context.Manager.Services.Log.Warn(UiElementManager.Category, "gmSkillUI: the SkillTable or the XpTable is not loaded; the skills list waits for them (TODO(m3-stat-tables))");
            }

            return;
        }

        var q = _context.Session.State.Self.Qualities;
        if (!q.IsKnown)
        {
            return; // nothing to list until the description arrives; the Description event marks the tab dirty again
        }

        _dirty = false;
        if (!_built || ClassesChanged(q, tables.SkillTable))
        {
            Rebuild(q, tables.SkillTable);
        }
        else
        {
            UpdateValues(q, tables.SkillTable);
        }

        UpdateSelection(q);
        _header?.Update(q);
        _context.Manager.LogOnce("skills:tooltips", LogSeverity.Warning, "gmSkillUI: a skill row has no tooltip (SkillInfoRegion::GetTooltip, 11.6096 0x004F2C40; TODO(m3-skill-tooltips))");
    }

    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change is QualityChange.VitalCurrent or QualityChange.Options)
        {
            return; // a vital's current value and the options show nowhere here
        }

        _dirty = true;
    }

    // ------------------------------------------------------------------ the list

    /// <summary><c>RebuildSkillList</c>: a skill is specialised (3), trained (2), untrained when its class is 1 and its <c>_min_level</c> is at most 1, else unusable.</summary>
    private static int SectionOf(uint advancementClass, SkillTableEntry entry) => advancementClass switch
    {
        SkillRules.SpecializedClass => 0,
        SkillRules.TrainedClass => 1,
        SkillRules.UntrainedClass when entry.MinLevel <= 1 => 2,
        _ => 3,
    };

    private bool ClassesChanged(PlayerQualities q, SkillTableData table)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (table.TryGetSkill(row.Skill, out var entry) && SectionOf(SkillRules.ClassOf(q, row.Skill), entry) != row.Section)
            {
                return true;
            }
        }

        return false;
    }

    private void Rebuild(PlayerQualities q, SkillTableData table)
    {
        var list = _list!;
        list.Flush();
        _rows.Clear();
        foreach (var section in _sections)
        {
            section.Clear();
        }

        foreach (var entry in table.Skills)
        {
            _sections[SectionOf(SkillRules.ClassOf(q, entry.Id), entry)].Add(entry);
        }

        for (var s = 0; s < _sections.Length; s++)
        {
            _sections[s].Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name)); // AddSortedSkill compares the names with wcscmp
            list.AddItemFromTemplateList(SectionTemplates[s]);
            foreach (var entry in _sections[s])
            {
                if (list.AddItemFromTemplateList(0) is { } item)
                {
                    (item.GetChildRecursive(RowLabelId) as UiTextElement)?.SetText(entry.Name);
                    item.GetChildRecursive(RowIconId)?.SetImageByDid(entry.IconId, UiDrawMode.AlphaBlend);
                    _rows.Add(new Row(item, entry.Id, s, item.GetChildRecursive(RowValueId) as UiTextElement, uint.MaxValue, -1));
                }
            }
        }

        _built = true;
        Builds++;
        UpdateValues(q, table);
    }

    private void UpdateValues(PlayerQualities q, SkillTableData table)
    {
        Redraws++;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Value is null || !table.TryGetSkill(row.Skill, out var entry))
            {
                continue;
            }

            if (!SkillRules.TryCurrent(q, row.Skill, entry.Formula, entry.MinLevel, out var current))
            {
                continue;
            }

            SkillRules.TryBase(q, row.Skill, entry.Formula, entry.MinLevel, out var baseValue);
            var color = ColorOf(q, baseValue, current);
            if (current == row.Shown && color == row.Color)
            {
                continue;
            }

            row.Value.SetText(string.Empty);
            row.Value.AppendTextWithFont(current.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, color);
            _rows[i] = row with { Shown = current, Color = color };
        }
    }

    /// <summary><c>SkillInfoRegion::Update</c>: the value is green when the base is below the value without the vitae, red when above.</summary>
    private static int ColorOf(PlayerQualities q, uint baseValue, uint current)
    {
        var withoutVitae = (long)current - SkillRules.VitaeModifier(q, baseValue);
        return baseValue < withoutVitae ? ColorRaised : baseValue > withoutVitae ? ColorLowered : ColorNormal;
    }

    // ------------------------------------------------------------------ the selection and the footer

    private void SetSelection(UiElement? item)
    {
        uint skill = 0;
        if (item is not null)
        {
            foreach (var row in _rows)
            {
                if (ReferenceEquals(row.Element, item))
                {
                    skill = row.Skill;
                    break;
                }
            }
        }

        _selectedSkill = _selectedSkill == skill ? 0 : skill; // the same row again clears the selection (SetSelection 0x0049B2B0)
        UpdateSelection(_context.Session.State.Self.Qualities);
    }

    private void UpdateSelection(PlayerQualities q)
    {
        foreach (var row in _rows)
        {
            row.Element.SetState(row.Skill == _selectedSkill ? RowSelectedState : RowNormalState);
        }

        var tables = _context.Session;
        var table = tables.SkillTable;
        if (_selectedSkill == 0 || table is null || !table.TryGetSkill(_selectedSkill, out var entry) || !q.IsKnown)
        {
            _selectedSkill = 0;
            ShowFooter(DefaultState);
            DefaultFooterText(q);
            return;
        }

        var advancementClass = SkillRules.ClassOf(q, _selectedSkill);
        if (advancementClass < SkillRules.TrainedClass)
        {
            ShowFooter(UntrainedState);
            UntrainedFooterText(q, entry);
        }
        else
        {
            ShowFooter(TrainedState);
            TrainedFooterText(q, entry);
        }
    }

    private void ShowFooter(uint state)
    {
        FooterState = state;
        _element!.SetState(state);
    }

    private void DefaultFooterText(PlayerQualities q)
    {
        var f = _defaultFooter;
        Put(f.Title, Text("ID_StatManagement_Footer_DefaultSkillTitle") ?? "Select a Skill to Improve");
        Put(f.LineOneLabel, Text("ID_StatManagement_Footer_SkillCreditsLabel") ?? "Skill Credits Available:");
        Put(f.LineOneValue, Text("ID_StatManagement_Footer_SkillCreditsValue", "SKILL_CREDITS", q.AvailableSkillCredits) ?? Plain(q.AvailableSkillCredits));
        Put(f.LineTwoLabel, Text("ID_StatManagement_Footer_ExperienceLabel") ?? "Unassigned Experience:");
        Put(f.LineTwoValue, Text("ID_StatManagement_Footer_ExperienceValue", "AVAILABLE_EXPERIENCE", q.AvailableExperience) ?? Plain(q.AvailableExperience));
    }

    private void UntrainedFooterText(PlayerQualities q, SkillTableEntry entry)
    {
        var f = _untrainedFooter;
        var cost = TrainCost(entry);
        var credits = q.AvailableSkillCredits;
        Put(f.Title, entry.Name);
        Put(f.LineOneLabel, Text("ID_StatManagement_Footer_SkillCreditsToRaiseLabel") ?? "Skill Credits To Train");
        Put(f.LineOneValue, Text("ID_StatManagement_Footer_SkillCreditsToRaiseValue", "SKILL_CREDITS_TO_RAISE", cost) ?? Plain(cost));
        Put(f.LineTwoLabel, Text("ID_StatManagement_Footer_SkillCreditsLabel") ?? "Skill Credits Available:");
        Put(f.LineTwoValue, Text("ID_StatManagement_Footer_SkillCreditsValue", "SKILL_CREDITS", credits) ?? Plain(credits));
        f.Raise10?.SetVisible(false);
        f.Raise?.SetState(!_awaiting && cost > 0 && credits >= cost ? UiStateIds.Normal : UiStateIds.Ghosted);
    }

    private void TrainedFooterText(PlayerQualities q, SkillTableEntry entry)
    {
        var f = _trainedFooter;
        var tables = _context.Session;
        SkillRules.TryCurrent(q, _selectedSkill, entry.Formula, entry.MinLevel, out var current);
        SkillRules.TryBase(q, _selectedSkill, entry.Formula, entry.MinLevel, out var baseValue);
        var vitae = SkillRules.VitaeModifier(q, baseValue);

        // "Name: value", then the vitae's loss and the enchantments' difference from the base in their colours.
        if (f.Title is { } title)
        {
            title.SetText(string.Empty);
            title.AppendTextWithFont(entry.Name + ": " + current.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, ColorNormal);
            if (vitae < 0)
            {
                title.AppendTextWithFont(" (" + vitae.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", 0, ColorVitae);
            }

            var difference = (long)current - vitae - baseValue;
            if (difference != 0)
            {
                title.AppendTextWithFont(" (" + (difference > 0 ? "+" : string.Empty) + difference.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", 0, difference > 0 ? ColorRaised : ColorLowered);
            }
        }

        var cost = SkillRules.CostToRaise(tables.XpTable!, q, _selectedSkill);
        var cost10 = SkillRules.CostToRaise10(tables.XpTable!, q, _selectedSkill);
        var available = q.AvailableExperience;
        Put(f.LineOneLabel, Text("ID_StatManagement_Footer_XPToRaiseLabel") ?? "Experience To Raise:");
        Put(f.LineOneValue, cost > 0 ? Text("ID_StatManagement_Footer_XPToRaiseValue", "XP_TO_RAISE", cost) ?? Plain(cost) : Text("ID_StatManagement_Header_XPToLevelMeterInfinity") ?? "Infinity!");
        if (f.Meter is { } meter)
        {
            meter.SetFloatProperty(UiPropertyIds.MeterPosition, SkillRules.RankProgress(tables.XpTable!, q, _selectedSkill));
        }

        Put(f.LineTwoLabel, Text("ID_StatManagement_Footer_ExperienceLabel") ?? "Unassigned Experience:");
        Put(f.LineTwoValue, Text("ID_StatManagement_Footer_ExperienceValue", "AVAILABLE_EXPERIENCE", available) ?? Plain(available));
        f.Raise?.SetState(!_awaiting && cost > 0 && available >= cost ? UiStateIds.Normal : UiStateIds.Ghosted);
        f.Raise10?.SetVisible(true);
        f.Raise10?.SetState(!_awaiting && cost10 > 0 && available >= cost10 ? UiStateIds.Normal : UiStateIds.Ghosted);
    }

    private int TrainCost(SkillTableEntry entry) => entry.TrainedCost;

    // ------------------------------------------------------------------ the buttons

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityChanged && ReferenceEquals(message.Element, _element) && message.Param1 != 0)
        {
            Refresh(); // the tab was opened: the first time builds the list (gmStatManagementUI::ListenToElementMessage, message 0x18)
            return UiListenResult.Handled;
        }

        if (message.MessageId is UiMessageIds.ListBoxSelection or UiMessageIds.ListBoxReselect && ReferenceEquals(message.Element, _list)) // a click on the selected row again reselects
        {
            SetSelection(message.Item);
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.ButtonClick && !_awaiting)
        {
            var id = message.Element.Desc.ElementId;
            if (id == RaiseButtonId)
            {
                RaiseSelection();
                return UiListenResult.Handled;
            }

            if (id == Raise10ButtonId)
            {
                Raise10Selection();
                return UiListenResult.Handled;
            }
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmSkillUI::RaiseSelection</c>: an untrained skill goes to the train dialog, a trained one spends the experience of one rank.</summary>
    public bool RaiseSelection()
    {
        var q = _context.Session.State.Self.Qualities;
        var table = _context.Session.SkillTable;
        if (_selectedSkill == 0 || table is null || !table.TryGetSkill(_selectedSkill, out var entry))
        {
            return false;
        }

        if (SkillRules.ClassOf(q, _selectedSkill) < SkillRules.TrainedClass)
        {
            return TrainSelection(q, entry);
        }

        var cost = SkillRules.CostToRaise(_context.Session.XpTable!, q, _selectedSkill);
        return cost > 0 && q.AvailableExperience >= cost && SendRaise(entry, cost);
    }

    /// <summary><c>gmSkillUI::Raise10Selection</c>: the experience of ten ranks (fewer near the cap).</summary>
    public bool Raise10Selection()
    {
        var q = _context.Session.State.Self.Qualities;
        var table = _context.Session.SkillTable;
        if (_selectedSkill == 0 || table is null || !table.TryGetSkill(_selectedSkill, out var entry) || SkillRules.ClassOf(q, _selectedSkill) < SkillRules.TrainedClass)
        {
            return false;
        }

        var cost = SkillRules.CostToRaise10(_context.Session.XpTable!, q, _selectedSkill);
        return cost > 0 && q.AvailableExperience >= cost && SendRaise(entry, cost);
    }

    private bool SendRaise(SkillTableEntry entry, uint cost)
    {
        LastAction = _context.Session.Actions.RaiseSkill(entry.Id, cost, ActionSource.Ui);
        _context.Manager.Services.Log.Info(UiElementManager.Category, $"gmSkillUI: raise {entry.Name} for {cost} experience");
        Await(LastAction);
        return true;
    }

    /// <summary><c>gmSkillUI::TrainSkill</c> (0x0049C860): the confirmation, then <c>TrainSkillDialogCallback</c> (0x0049C480) trains.</summary>
    private bool TrainSelection(PlayerQualities q, SkillTableEntry entry)
    {
        var cost = TrainCost(entry);
        if (cost <= 0 || q.AvailableSkillCredits < cost || _dialog != 0)
        {
            return false;
        }

        var text = UiStringRef.FromLiteral($"Are you sure you want to spend {cost} credits to train {entry.Name}?"); // retail's literal (a sprintf, not a table string)
        var data = UiDialogData.Of(UiDialogType.Confirmation, text).WithQueue(DialogFactory.ImmediateQueue);
        _dialog = _context.Manager.Dialogs.MakeCallbackDialog(data, answer => OnTrainAnswer(answer, entry.Id, cost));
        return _dialog != 0;
    }

    private void OnTrainAnswer(UiDialogData answer, uint skill, int cost)
    {
        _dialog = 0;
        var q = _context.Session.State.Self.Qualities;
        if (answer.ConfirmationResponse != true || SkillRules.ClassOf(q, skill) >= SkillRules.TrainedClass || q.AvailableSkillCredits < cost)
        {
            return;
        }

        LastAction = _context.Session.Actions.TrainSkill(skill, cost, ActionSource.Ui);
        _context.Manager.Services.Log.Info(UiElementManager.Category, $"gmSkillUI: train skill {skill} for {cost} credits");
        Await(LastAction);
    }

    /// <summary>Retail's <c>m_bAwaitingRaise</c>: the buttons stay ghosted until the action's answer arrives (the skill's update clears it).</summary>
    private void Await(ActionHandle handle)
    {
        _awaiting = true;
        _dirty = true;
        handle.Completed += OnActionCompleted;
        if (handle.IsCompleted)
        {
            OnActionCompleted(handle);
        }
        else
        {
            UpdateSelection(_context.Session.State.Self.Qualities);
        }
    }

    private void OnActionCompleted(ActionHandle handle)
    {
        handle.Completed -= OnActionCompleted;
        _awaiting = false;
        _dirty = true;
    }

    // ------------------------------------------------------------------ text

    private static string Plain(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private string? Text(string token)
    {
        var strings = _context.Manager.Services.Strings;
        return strings is not null && strings.TryReference(TableEnum, token, out var reference) && strings.TryGetString(reference, out var text) ? text : Missing(token);
    }

    private string? Text(string token, string variable, long value)
    {
        var strings = _context.Manager.Services.Strings;
        if (strings?.Language is not { } language || !strings.TryReference(TableEnum, token, out var reference))
        {
            return Missing(token);
        }

        var variables = new UiStringVariables().SetInteger(StringHash.Tokenize(variable), value, language);
        return strings.TryGetString(reference, variables, out var text) ? text : Missing(token);
    }

    private string? Missing(string token)
    {
        _context.Manager.LogOnce($"skills:string:{token}", LogSeverity.Warning, $"gmSkillUI: the string {token} does not render in the string tables; a plain text stands in (TODO(m3-skill-strings))");
        return null;
    }

    private static void Put(UiTextElement? element, string text)
    {
        if (element is null)
        {
            return;
        }

        element.SetText(string.Empty);
        element.AppendTextWithFont(text, 0, ColorNormal);
    }

    /// <inheritdoc/>
    public void Dispose() => _subscription.Dispose();

    // ------------------------------------------------------------------ the rows and the footers

    private readonly record struct Row(UiElement Element, uint Skill, int Section, UiTextElement? Value, uint Shown, int Color);

    private readonly struct Footer
    {
        public Footer(UiElement? field)
        {
            Field = field;
            Title = field?.GetChildRecursive(TitleId) as UiTextElement;
            LineOneLabel = field?.GetChildRecursive(LineOneLabelId) as UiTextElement;
            LineOneValue = field?.GetChildRecursive(LineOneValueId) as UiTextElement;
            LineTwoLabel = field?.GetChildRecursive(LineTwoLabelId) as UiTextElement;
            LineTwoValue = field?.GetChildRecursive(LineTwoValueId) as UiTextElement;
            Raise = field?.GetChildRecursive(RaiseButtonId);
            Raise10 = field?.GetChildRecursive(Raise10ButtonId);
            Meter = field?.GetChildRecursive(MeterId);
        }

        public UiElement? Field { get; }

        public UiTextElement? Title { get; }

        public UiTextElement? LineOneLabel { get; }

        public UiTextElement? LineOneValue { get; }

        public UiTextElement? LineTwoLabel { get; }

        public UiTextElement? LineTwoValue { get; }

        public UiElement? Raise { get; }

        public UiElement? Raise10 { get; }

        public UiElement? Meter { get; }
    }
}
