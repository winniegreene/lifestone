using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The skills page (retail's <c>gmCGSkillsPage</c>, element 0x100003D3, class 0x1000003B): the skill credits left, a list of the skills in four groups
/// (specialised, trained, untrained that can be used, untrained that cannot), each skill a row with its level, an up arrow with the credits the step up
/// costs and a down arrow with the credits the step down gives back, and a box that describes the selected skill and the formula its level comes from.
/// An arrow moves the skill a step through <see cref="CharGenState.SetSkillLevel"/> (which refuses what the credits cannot pay), and the row moves to
/// its new group, in name order.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCGSkillsPage::InitializePage</c> (11.4186 0x00481DD0, 11.6096 0x00482190), <c>Update</c> (0x00481DC0, 0x00482180),
/// <c>DoSkillRecords</c> (0x004817E0, 0x00481BA0), <c>UpdateSkillEntry</c> (0x00480BF0, 0x00480FB0), <c>InsertEntrySorted</c> (0x00480A40, 0x00480E00),
/// <c>SetSkillText</c> (0x00480600, 0x004809C0), <c>UpdateAllTrainingValues</c> (0x00480950, 0x00480D10), <c>UpdateCreditsMeter</c> (0x004808F0,
/// 0x00480CB0), <c>IncreaseSkillLevel</c> (0x00480CA0, 0x00481060), <c>DecreaseSkillLevel</c> (0x00480D60, 0x00481120), <c>ShowSkillsText</c> (0x00481250,
/// 0x00481610), <c>MakeSkillFormula</c> (0x00480E10, 0x004811D0) and <c>ListenToElementMessage</c> (0x004814C0, 0x00481880).
/// </para>
/// <para>
/// <b>Costs.</b> The page prices a skill by the SkillTable, then shifts it for the heritage (<see cref="CharGenRules.SkillCosts"/>: a skill the heritage
/// trains for nothing costs 0 to train and cannot be untrained). The state charges by <see cref="CharGenState.TrainedCost"/> and
/// <see cref="CharGenState.SpecializedCost"/>, which take the heritage's own figure whenever the heritage lists the skill. In the user's dats the two agree
/// for every skill of every heritage (the only listed skill, Arcane Lore, is 0 to train and 2 to specialise in all thirteen, which is the SkillTable's step
/// of 6 less 4), and the tests check each step against the state's charge. A step the page prices at 999 or more (Salvaging and the four tinkering skills
/// cannot be specialised) shows no cost.
/// </para>
/// </remarks>
public sealed class CharGenSkillsPage : CharGenPage
{
    /// <summary>The skill list.</summary>
    public const uint ListId = 0x100003F7;

    /// <summary>The credits left (a text of the page).</summary>
    public const uint CreditsId = 0x100002F3;

    /// <summary>The title of the skill box (the skill's name and level).</summary>
    public const uint InfoTitleId = 0x100003FB;

    /// <summary>The body of the skill box (its description, bonus and formula).</summary>
    public const uint InfoTextId = 0x100003FC;

    /// <summary>A skill row's up arrow.</summary>
    public const uint UpButtonId = 0x10000304;

    /// <summary>A skill row's down arrow.</summary>
    public const uint DownButtonId = 0x10000305;

    /// <summary>A skill row's name.</summary>
    public const uint RowNameId = 0x10000301;

    /// <summary>A skill row's level.</summary>
    public const uint RowLevelId = 0x10000302;

    /// <summary>A skill row's up cost.</summary>
    public const uint RowUpCostId = 0x10000303;

    /// <summary>A skill row's down cost.</summary>
    public const uint RowDownCostId = 0x10000306;

    /// <summary>A group header's text.</summary>
    public const uint HeaderTextId = 0x100002F6;

    /// <summary>The property that holds a skill row's skill id (retail's InstanceID property 0x1000000A).</summary>
    public const uint SkillIdProperty = 0x1000000A;

    private static readonly (string Title, string Tooltip)[] GroupTexts =
    [
        ("ID_CharGen_Specialized", "ID_CharGen_Specialized_Tooltip"),
        ("ID_CharGen_Trained", "ID_CharGen_Trained_Tooltip"),
        ("ID_CharGen_UseableUntrained", "ID_CharGen_UseableUntrained_Tooltip"),
        ("ID_CharGen_UnuseableUntrained", "ID_CharGen_UnuseableUntrained_Tooltip"),
    ];

    private readonly UiListBox? _list;
    private readonly UiTextElement? _credits;
    private readonly UiTextElement? _infoTitle;
    private readonly UiTextElement? _infoText;
    private readonly Dictionary<uint, SkillRecord> _records = [];
    private readonly UiElement?[] _headers = new UiElement?[4];

    /// <summary>The skills page over <paramref name="root"/>: its elements found and the skills listed for the screen's character.</summary>
    public CharGenSkillsPage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGSkillsPage")
    {
        _list = Find<UiListBox>(ListId);
        _credits = Find<UiTextElement>(CreditsId);
        _infoTitle = Find<UiTextElement>(InfoTitleId);
        _infoText = Find<UiTextElement>(InfoTextId);
        DoSkillRecords();
        UpdateCreditsMeter();
    }

    /// <summary>The skill list.</summary>
    public UiListBox? List => _list;

    /// <summary>The skill credits left, as shown.</summary>
    public string CreditsText => _credits?.Text ?? string.Empty;

    /// <summary>The skill box's title, as shown.</summary>
    public string InfoTitle => _infoTitle?.Text ?? string.Empty;

    /// <summary>The skill box's body, as shown.</summary>
    public string InfoBody => _infoText?.Text ?? string.Empty;

    /// <summary>The number of skills listed.</summary>
    public int SkillCount => _records.Count;

    /// <summary>The row of skill <paramref name="skill"/>, or null.</summary>
    public UiElement? RowOf(uint skill) => _records.TryGetValue(skill, out var record) ? record.Entry : null;

    /// <summary>The group a skill's row is in, or null when it is not listed.</summary>
    public CharGenSkillGroup? GroupOfSkill(uint skill) => _records.TryGetValue(skill, out var record) ? CharGenRules.GroupOf(record.Level, record.MinLevel) : null;

    /// <summary>What skill <paramref name="skill"/>'s row shows (level, costs, arrows), or null.</summary>
    public (string Level, string UpCost, string DownCost, uint UpState, uint DownState)? RowView(uint skill) =>
        _records.TryGetValue(skill, out var r)
            ? (r.LevelText.Text, r.UpCostText.Text, r.DownCostText.Text, r.UpButton.State, r.DownButton.State)
            : null;

    /// <summary>The up arrow of skill <paramref name="skill"/>, or null.</summary>
    public UiButton? UpArrowOf(uint skill) => _records.TryGetValue(skill, out var record) ? record.UpButton : null;

    /// <summary>The down arrow of skill <paramref name="skill"/>, or null.</summary>
    public UiButton? DownArrowOf(uint skill) => _records.TryGetValue(skill, out var record) ? record.DownButton : null;

    /// <summary>The skills in list order (the group headers left out).</summary>
    public IEnumerable<uint> ListedSkills => _list is null ? [] : _list.Items.Select(InstanceIdOf).Where(id => id != 0);

    /// <summary><c>gmCGSkillsPage::Update</c>: the skills are listed again for the character as it is now, and their rows show.</summary>
    public override void Update()
    {
        DoSkillRecords();
        UpdateAllTrainingValues();
    }

    /// <summary><c>gmCGSkillsPage::UpdateCreditsMeter</c>: the credits left.</summary>
    public void UpdateCreditsMeter() => _credits?.SetText(Main.State.RemainingSkillCredits.ToString(CultureInfo.InvariantCulture));

    /// <summary><c>gmCGSkillsPage::UpdateAllTrainingValues</c>: every row shows its level and costs again, then the credits left.</summary>
    public void UpdateAllTrainingValues()
    {
        foreach (var record in _records.Values)
        {
            SetSkillText(record);
        }

        UpdateCreditsMeter();
    }

    /// <summary>
    /// <c>gmCGSkillsPage::IncreaseSkillLevel</c>: the skill of row <paramref name="entry"/> goes a step up when its cost is within the credits and the state
    /// allows it; the row moves to its new group, and every row is shown again.
    /// </summary>
    public void IncreaseSkillLevel(UiElement entry)
    {
        if (RecordOf(entry) is not { } record)
        {
            return;
        }

        var state = Main.State;
        var moved = false;
        if (record.Level == SkillAdvancementClass.Untrained)
        {
            if (record.TrainCost <= state.RemainingSkillCredits && state.SetSkillLevel(record.Id, SkillAdvancementClass.Trained))
            {
                record.Level = SkillAdvancementClass.Trained;
                moved = true;
            }
        }
        else if (record.Level == SkillAdvancementClass.Trained && record.SpecCost - record.TrainCost <= state.RemainingSkillCredits
                 && state.SetSkillLevel(record.Id, SkillAdvancementClass.Specialized))
        {
            record.Level = SkillAdvancementClass.Specialized;
            moved = true;
        }

        if (moved)
        {
            UpdateSkillEntry(record);
        }

        UpdateAllTrainingValues();
    }

    /// <summary>
    /// <c>gmCGSkillsPage::DecreaseSkillLevel</c>: the skill of row <paramref name="entry"/> goes a step down when the skill may (a skill the heritage trains
    /// for nothing may not go below trained, one it specialises for nothing may not be specialised); the row moves to its new group, and every row is shown again.
    /// </summary>
    public void DecreaseSkillLevel(UiElement entry)
    {
        if (RecordOf(entry) is not { } record)
        {
            return;
        }

        var state = Main.State;
        var moved = false;
        if (record.Level == SkillAdvancementClass.Specialized && record.CanUnspecialize)
        {
            if (state.SetSkillLevel(record.Id, SkillAdvancementClass.Trained))
            {
                record.Level = SkillAdvancementClass.Trained;
                moved = true;
            }
        }
        else if (record.Level == SkillAdvancementClass.Trained && record.CanUntrain && state.SetSkillLevel(record.Id, SkillAdvancementClass.Untrained))
        {
            record.Level = SkillAdvancementClass.Untrained;
            moved = true;
        }

        if (moved)
        {
            UpdateSkillEntry(record);
        }

        UpdateAllTrainingValues();
    }

    /// <summary>
    /// <c>gmCGSkillsPage::ShowSkillsText</c>: the skill box shows the skill of row <paramref name="entry"/> (its name and level, its description, the bonus its
    /// class gives and its formula), or nothing for a row that is not a skill.
    /// </summary>
    public void ShowSkillsText(UiElement? entry)
    {
        if (entry is null || RecordOf(entry) is not { } record)
        {
            _infoTitle?.ClearAllText();
            _infoText?.ClearAllText();
            return;
        }

        var score = Main.State.GetSkillScore(record.Id);
        var cls = Main.State.GetSkillLevel(record.Id);
        _infoTitle?.SetText(string.Create(CultureInfo.InvariantCulture, $"{record.Name} ({score})\n"));
        _infoText?.SetText(record.Description + "\n" + CharGenRules.BonusLine(cls) + CharGenRules.FormulaText(record.Formula) + "\n");
    }

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        switch (message.ElementId)
        {
            case UpButtonId or DownButtonId when message.MessageId == UiMessageIds.ButtonClick && EntryOf(message.Element) is { } entry:
                if (message.ElementId == UpButtonId)
                {
                    IncreaseSkillLevel(entry);
                }
                else
                {
                    DecreaseSkillLevel(entry);
                }

                _list?.SetSelectedItem(entry, broadcast: true);
                break;
            case ListId when message.MessageId == UiMessageIds.ListBoxSelection:
                ShowSkillsText(_list?.GetItem(_list.SelectedIndex));
                break;
        }

        return UiListenResult.Default;
    }

    /// <summary>
    /// <c>gmCGSkillsPage::DoSkillRecords</c>: the list is emptied and filled with the four group headers (each with its tooltip) and a row for every skill
    /// the state lists (by name), priced for the heritage and put in the group of its class.
    /// </summary>
    private void DoSkillRecords()
    {
        if (_list is null)
        {
            return;
        }

        _list.Flush();
        _records.Clear();
        Array.Clear(_headers);
        for (var i = 0; i < GroupTexts.Length; i++)
        {
            var header = _list.AddItemFromTemplateList(0);
            if (header is null)
            {
                Manager.LogOnce($"chargen:{Name}:header", LogSeverity.Error, $"{Name}: the list has no header template (entry 0); the groups are not listed");
                return;
            }

            if (header.GetChildRecursive(HeaderTextId) is UiTextElement text)
            {
                text.SetStringInfo(Pregame(GroupTexts[i].Title));
                text.SetTooltip(Pregame(GroupTexts[i].Tooltip));
            }

            _headers[i] = header;
        }

        var state = Main.State;
        var heritage = Main.Catalog.Data.TryGetHeritage(state.Heritage, out var own) ? own : null;
        foreach (var skill in state.SkillsByName)
        {
            if (_list.AddItemFromTemplateList(1) is not { } row)
            {
                Manager.LogOnce($"chargen:{Name}:row", LogSeverity.Error, $"{Name}: the list has no skill row template (entry 1); the skills are not listed");
                return;
            }

            row.SetState(UiStateIds.Normal);
            row.SetProperty(new UiInstanceIdProperty(SkillIdProperty, skill.Id));
            if (row.GetChildRecursive(RowNameId) is not UiTextElement name
                || row.GetChildRecursive(RowLevelId) is not UiTextElement level
                || row.GetChildRecursive(RowUpCostId) is not UiTextElement upCost
                || row.GetChildRecursive(RowDownCostId) is not UiTextElement downCost
                || row.GetChildRecursive(DownButtonId) is not UiButton down
                || row.GetChildRecursive(UpButtonId) is not UiButton up)
            {
                Manager.LogOnce($"chargen:{Name}:rowparts", LogSeverity.Error, $"{Name}: the skill row template lacks one of its name, level, cost texts or arrows; the skills are not listed");
                return;
            }

            CharGenSkill? listed = heritage?.Skills.Where(s => s.SkillId == skill.Id).Select(s => (CharGenSkill?)s).FirstOrDefault();
            var (trainCost, specCost, canUntrain, canUnspecialize) = CharGenRules.SkillCosts(skill, listed);
            var record = new SkillRecord(row, level, upCost, downCost, up, down)
            {
                Id = skill.Id,
                Name = skill.Name,
                Description = skill.Description,
                Formula = skill.Formula,
                MinLevel = skill.MinLevel,
                TrainCost = trainCost,
                SpecCost = specCost,
                CanUntrain = canUntrain,
                CanUnspecialize = canUnspecialize,
                Level = state.GetSkillLevel(skill.Id),
            };
            name.SetText(skill.Name);
            UpdateSkillEntry(record);
            _records[skill.Id] = record;
        }
    }

    /// <summary><c>gmCGSkillsPage::UpdateSkillEntry</c>: the row's texts and arrows are set, and the row leaves the list and goes back in, in its group by name.</summary>
    private void UpdateSkillEntry(SkillRecord record)
    {
        if (_list is null)
        {
            return;
        }

        var at = IndexOfRow(record.Entry);
        if (at >= 0)
        {
            _list.RemoveItem(at);
        }

        SetSkillText(record);
        switch (CharGenRules.GroupOf(record.Level, record.MinLevel))
        {
            case CharGenSkillGroup.UnusableUntrained:
                InsertEntrySorted(record, _headers[3], null);
                break;
            case CharGenSkillGroup.UsableUntrained:
                InsertEntrySorted(record, _headers[2], _headers[3]);
                break;
            case CharGenSkillGroup.Trained:
                InsertEntrySorted(record, _headers[1], _headers[2]);
                break;
            default:
                InsertEntrySorted(record, _headers[0], _headers[1]);
                break;
        }
    }

    /// <summary>
    /// <c>gmCGSkillsPage::InsertEntrySorted</c>: the row goes between the header <paramref name="after"/> and the next header <paramref name="before"/> (the end
    /// of the list for none), before the first row there whose skill name sorts after its own (ordinal), else last in the group.
    /// </summary>
    private void InsertEntrySorted(SkillRecord record, UiElement? after, UiElement? before)
    {
        var list = _list!;
        var afterIndex = after is null ? -1 : IndexOfRow(after);
        var beforeIndex = before is null ? list.Items.Count : IndexOfRow(before);
        var at = afterIndex + 1;
        while (at < beforeIndex)
        {
            var item = list.GetItem(at);
            if (item is not null && _records.TryGetValue(InstanceIdOf(item), out var existing) && string.CompareOrdinal(record.Name, existing.Name) < 0)
            {
                break;
            }

            at++;
        }

        list.InsertItem(record.Entry, at);
    }

    /// <summary>
    /// <c>gmCGSkillsPage::SetSkillText</c>: the row shows the skill's score and, for its class, the cost of a step up and of a step down, with the arrows
    /// enabled or not (<see cref="CharGenRules.SkillRow"/>).
    /// </summary>
    private void SetSkillText(SkillRecord record)
    {
        var state = Main.State;
        record.Score = state.GetSkillScore(record.Id);
        record.LevelText.SetText(record.Score.ToString(CultureInfo.InvariantCulture));
        if (CharGenRules.SkillRow(record.Level, record.TrainCost, record.SpecCost, record.CanUntrain, record.CanUnspecialize, state.RemainingSkillCredits) is not { } row)
        {
            return;
        }

        if (row.UpCost.Length == 0)
        {
            Manager.LogOnce(
                "chargen:unbuyable-cost",
                LogSeverity.Warning,
                $"TODO(retailui-chargen-data): {record.Name} has a step of {CharGenRules.UnbuyableCost} credits or more; retail shows a wide string at 11.4186 0x00794320 there (not read), taken as empty");
        }

        record.UpCostText.SetText(row.UpCost);
        record.DownCostText.SetText(row.DownCost);
        record.UpButton.SetState(row.UpEnabled ? CharGenRules.ArrowEnabledState : CharGenRules.ArrowDisabledState);
        record.DownButton.SetState(row.DownEnabled ? CharGenRules.ArrowEnabledState : CharGenRules.ArrowDisabledState);
    }

    private int IndexOfRow(UiElement row)
    {
        var items = _list!.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private SkillRecord? RecordOf(UiElement entry) => _records.TryGetValue(InstanceIdOf(entry), out var record) ? record : null;

    /// <summary>The skill row that holds <paramref name="element"/> (the element itself, or the nearest ancestor with a skill id).</summary>
    private static UiElement? EntryOf(UiElement element)
    {
        for (var at = element; at is not null; at = at.Parent)
        {
            if (InstanceIdOf(at) != 0)
            {
                return at;
            }
        }

        return null;
    }

    private static uint InstanceIdOf(UiElement item) =>
        item.InqProperty(SkillIdProperty, out var property) && property is UiInstanceIdProperty id ? id.Value : 0u;

    /// <summary>Retail's <c>gmCGSkillsPage::tagSkillRecord</c>: a listed skill with the row that shows it.</summary>
    private sealed class SkillRecord(UiElement entry, UiTextElement levelText, UiTextElement upCost, UiTextElement downCost, UiButton up, UiButton down)
    {
        public UiElement Entry { get; } = entry;

        public UiTextElement LevelText { get; } = levelText;

        public UiTextElement UpCostText { get; } = upCost;

        public UiTextElement DownCostText { get; } = downCost;

        public UiButton UpButton { get; } = up;

        public UiButton DownButton { get; } = down;

        public uint Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public SkillTableFormula Formula { get; init; }

        public uint MinLevel { get; init; }

        public int TrainCost { get; init; }

        public int SpecCost { get; init; }

        public bool CanUntrain { get; init; }

        public bool CanUnspecialize { get; init; }

        public SkillAdvancementClass Level { get; set; }

        public uint Score { get; set; }
    }
}
