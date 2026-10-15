using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The profession page (retail's <c>gmCGProfessionPage</c>, element 0x100003D2, class 0x1000003A): seven profession buttons (the custom one first), a
/// description, six attribute rows (a lock, a name, a slider from 10 to 100 and a field to type the number in), the attribute credits left and the
/// health, stamina and mana they make. A profession click applies the template through <see cref="CharGenState.SetTemplate"/>; a slider or a typed
/// number sets the attribute through the state's balancing setters; a lock keeps an attribute out of the balancing.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCGProfessionPage::InitializePage</c> (11.4186 0x00482D50, 11.6096 0x00483110), <c>Update</c> (0x00482830, 0x00482BF0),
/// <c>UpdateToDefaultAttributes</c> (0x00482860, 0x00482C20), <c>UpdateProfession</c> (0x004821B0, 0x00482570), <c>UpdateAttributeValues</c> (0x00482450,
/// 0x00482810), <c>SetAttribValue</c> (0x00482890, 0x00482C50), <c>SetLock</c> (0x00482000, 0x004823C0), <c>ClearLocks</c> (0x004820D0, 0x00482490),
/// <c>GetSliderIndex</c> (0x00481F90, 0x00482350) and <c>ListenToElementMessage</c> (0x004829C0, 0x00482D80).
/// </para>
/// <para>
/// <b>Rows.</b> The rows are fields 0x100003E6 (Strength), 0x100003E7 (Endurance), 0x100003E9 (Quickness), 0x100003E8 (Coordination), 0x100003EA (Focus)
/// and 0x100003EB (Self), each with the lock 0x100002EC, the name 0x100002ED, the slider 0x100002EE and the number 0x100002EF. The slider's position is
/// the attribute over 100 (property 0x86) and its message carries thousandths, a tenth of which (at least 10) is the attribute; a request above what the
/// credits allow is cut to what they allow before the state hears it. The number's field takes digits only (retail's <c>NumberInputFilter</c>, which
/// its decompile assigns beside the name field; the number is the field it can mean) and means its value, 10 to 100, when it loses the focus; while it
/// is typed in, the slider follows (the goal position 0x85, 0.1 to 1).
/// </para>
/// </remarks>
public sealed class CharGenProfessionPage : CharGenPage
{
    /// <summary>The profession's picture field (one state for each profession).</summary>
    public const uint BackgroundId = 0x100003D8;

    /// <summary>The profession's description.</summary>
    public const uint DescriptionId = 0x100003E0;

    /// <summary>The attribute credits left (a button field; its text is <see cref="CreditsTextId"/>).</summary>
    public const uint CreditsFieldId = 0x100003E2;

    /// <summary>The health field (its text is <see cref="ValueTextId"/>).</summary>
    public const uint HealthFieldId = 0x100003E3;

    /// <summary>The stamina field.</summary>
    public const uint StaminaFieldId = 0x100003E4;

    /// <summary>The mana field.</summary>
    public const uint ManaFieldId = 0x100003E5;

    /// <summary>The text of the credits field.</summary>
    public const uint CreditsTextId = 0x100002F1;

    /// <summary>The text of the health, stamina and mana fields.</summary>
    public const uint ValueTextId = 0x100002F3;

    /// <summary>An attribute row's lock button.</summary>
    public const uint LockId = 0x100002EC;

    /// <summary>An attribute row's name.</summary>
    public const uint NameId = 0x100002ED;

    /// <summary>An attribute row's slider.</summary>
    public const uint SliderId = 0x100002EE;

    /// <summary>An attribute row's number field.</summary>
    public const uint NumberId = 0x100002EF;

    private readonly Dictionary<uint, Row> _rows = [];
    private readonly UiElement? _background;
    private readonly UiTextElement? _description;
    private readonly UiTextElement? _credits;
    private readonly UiTextElement? _health;
    private readonly UiTextElement? _stamina;
    private readonly UiTextElement? _mana;
    private readonly Dictionary<int, UiElement> _templateButtons = [];
    private UiElement? _currentTemplateButton;

    /// <summary>The profession page over <paramref name="root"/>: its elements found, the attribute names set, and the profession shown.</summary>
    public CharGenProfessionPage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGProfessionPage")
    {
        _background = Find<UiElement>(BackgroundId);
        _description = Find<UiTextElement>(DescriptionId);
        foreach (var entry in CharGenRules.Templates)
        {
            if (Find<UiButton>(entry.ButtonId) is { } button)
            {
                _templateButtons[entry.Template] = button;
            }
        }

        foreach (var (attribute, fieldId) in RowFields)
        {
            if (Find<UiElement>(fieldId) is not { } field)
            {
                continue;
            }

            var row = new Row(attribute, field)
            {
                Lock = field.GetChildRecursive(LockId) as UiButton,
                Name = field.GetChildRecursive(NameId) as UiTextElement,
                Slider = field.GetChildRecursive(SliderId) as UiScrollbar,
                Number = field.GetChildRecursive(NumberId) as UiTextElement,
            };
            if (row.Lock is null || row.Name is null || row.Slider is null || row.Number is null)
            {
                Manager.LogOnce($"chargen:{Name}:row:{fieldId:X8}", LogSeverity.Error, $"{Name}: the attribute row 0x{fieldId:X8} lacks its lock, name, slider or number; the row does not work");
            }

            _rows[fieldId] = row;
            row.Name?.SetText(CharGenState.GetAttributeName(attribute));
            row.Lock?.SetState(CharGenRules.UnlockedState);
            if (row.Number is not null)
            {
                row.Number.Filter = char.IsAsciiDigit;
            }
        }

        _credits = FindIn<UiTextElement>(CreditsFieldId, CreditsTextId);
        _health = FindIn<UiTextElement>(HealthFieldId, ValueTextId);
        _stamina = FindIn<UiTextElement>(StaminaFieldId, ValueTextId);
        _mana = FindIn<UiTextElement>(ManaFieldId, ValueTextId);
        UpdateProfession();
    }

    /// <summary>The rows' fields by attribute (1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self).</summary>
    public static IReadOnlyList<(uint Attribute, uint FieldId)> RowFields { get; } =
    [
        (1, 0x100003E6),
        (2, 0x100003E7),
        (3, 0x100003E9),
        (4, 0x100003E8),
        (5, 0x100003EA),
        (6, 0x100003EB),
    ];

    /// <summary>The attribute credits left, as shown.</summary>
    public string CreditsText => _credits?.Text ?? string.Empty;

    /// <summary>The health, as shown.</summary>
    public string HealthText => _health?.Text ?? string.Empty;

    /// <summary>The stamina, as shown.</summary>
    public string StaminaText => _stamina?.Text ?? string.Empty;

    /// <summary>The mana, as shown.</summary>
    public string ManaText => _mana?.Text ?? string.Empty;

    /// <summary>The profession's description, as shown.</summary>
    public string DescriptionText => _description?.Text ?? string.Empty;

    /// <summary>The state of the picture field.</summary>
    public uint BackgroundState => _background?.State ?? 0;

    /// <summary>The state of template <paramref name="template"/>'s button, or 0.</summary>
    public uint TemplateButtonState(int template) => _templateButtons.TryGetValue(template, out var button) ? button.State : 0;

    /// <summary>The number shown in attribute <paramref name="attribute"/>'s field, or null.</summary>
    public string? NumberText(uint attribute) => RowOf(attribute)?.Number?.Text;

    /// <summary>The slider of attribute <paramref name="attribute"/>, or null.</summary>
    public UiScrollbar? SliderOf(uint attribute) => RowOf(attribute)?.Slider;

    /// <summary>The number field of attribute <paramref name="attribute"/>, or null.</summary>
    public UiTextElement? NumberFieldOf(uint attribute) => RowOf(attribute)?.Number;

    /// <summary>The lock button of attribute <paramref name="attribute"/>, or null.</summary>
    public UiButton? LockOf(uint attribute) => RowOf(attribute)?.Lock;

    /// <summary>Whether the page has attribute <paramref name="attribute"/> locked.</summary>
    public bool IsLocked(uint attribute) => RowOf(attribute)?.Locked ?? false;

    /// <summary><c>gmCGProfessionPage::Update</c>: the template fits the attributes, and the profession and the numbers are shown.</summary>
    public override void Update()
    {
        Main.State.FitTemplateToCharacter();
        UpdateProfession();
        UpdateAttributeValues();
    }

    /// <summary><c>gmCGProfessionPage::UpdateToDefaultAttributes</c>: the locks are cleared, then <see cref="Update"/>.</summary>
    public void UpdateToDefaultAttributes()
    {
        ClearLocks();
        Update();
    }

    /// <summary>
    /// <c>gmCGProfessionPage::UpdateProfession</c>: the profession's button is the selected one, its picture and its description show. A template
    /// outside 0-6 (a state's -1) has no button in retail either (its code would fault); here the description is cleared and a loud error logged.
    /// </summary>
    public void UpdateProfession()
    {
        _currentTemplateButton?.SetState(CharGenRules.UnselectedState);
        var entry = CharGenRules.TemplateEntry(Main.State.Template);
        if (entry is null)
        {
            _currentTemplateButton = null;
            _description?.ClearAllText();
            Manager.LogOnce($"chargen:{Name}:template", LogSeverity.Error, $"{Name}: the character's template is {Main.State.Template}, which no profession button stands for; the page shows no profession (retail would fault here)");
            return;
        }

        _background?.SetState(entry.BackgroundState);
        _templateButtons.TryGetValue(entry.Template, out _currentTemplateButton);
        _currentTemplateButton?.SetState(CharGenRules.SelectedState);
        _description?.SetStringInfo(Pregame(entry.DescriptionToken));
    }

    /// <summary>
    /// <c>gmCGProfessionPage::UpdateAttributeValues</c>: each attribute's number and slider, the credits left, and the health (endurance halved), stamina
    /// (endurance) and mana (self); the template is fitted to the numbers afterwards, so a hand-made character becomes the custom profession.
    /// </summary>
    public void UpdateAttributeValues()
    {
        var state = Main.State;
        foreach (var row in _rows.Values)
        {
            var value = state.GetAttribute(row.Attribute);
            row.Number?.SetText(value.ToString(CultureInfo.InvariantCulture));
            row.Slider?.SetFloatProperty(UiPropertyIds.ScrollbarPosition, (float)(value * 0.0099999998));
        }

        _credits?.SetText(state.RemainingAttributeCredits.ToString(CultureInfo.InvariantCulture));
        _health?.SetText(CharGenRules.HealthOf(state.Endurance).ToString(CultureInfo.InvariantCulture));
        _stamina?.SetText(state.Endurance.ToString(CultureInfo.InvariantCulture));
        _mana?.SetText(state.Self.ToString(CultureInfo.InvariantCulture));
        state.FitTemplateToCharacter();
        UpdateProfession();
    }

    /// <summary>
    /// <c>gmCGProfessionPage::SetAttribValue</c>: attribute <paramref name="attribute"/> is asked to be <paramref name="value"/>. A raise is cut to
    /// what the credits allow (<see cref="CharGenState.GetAbsRemainingCredits"/>); the state then sets it with balancing, and the numbers are shown again.
    /// </summary>
    public void SetAttribValue(uint attribute, int value)
    {
        var state = Main.State;
        var current = state.GetAttribute(attribute);
        var allowed = state.GetAbsRemainingCredits(attribute);
        if (value - current >= 0 && value - current > allowed)
        {
            value = allowed + current;
        }

        state.SetAttribute(attribute, value, balance: true);
        UpdateAttributeValues();
    }

    /// <summary>
    /// <c>gmCGProfessionPage::SetLock</c>: attribute <paramref name="attribute"/>'s lock toggles (the slider and the lock button change state) and the
    /// state is told (<see cref="CharGenState.LockAttribute"/>).
    /// </summary>
    public void SetLock(uint attribute)
    {
        if (RowOf(attribute) is not { } row)
        {
            return;
        }

        row.Locked = !row.Locked;
        var state = row.Locked ? CharGenRules.LockedState : CharGenRules.UnlockedState;
        row.Slider?.SetState(state);
        row.Lock?.SetState(state);
        Main.State.LockAttribute(attribute, row.Locked);
    }

    /// <summary><c>gmCGProfessionPage::ClearLocks</c>: the state's locks are cleared, and each lock the page shows is turned off.</summary>
    public void ClearLocks()
    {
        Main.State.ResetAttributeLock();
        foreach (var row in _rows.Values.Where(r => r.Locked).ToArray())
        {
            SetLock(row.Attribute);
        }
    }

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (CharGenRules.TemplateOfButton(message.ElementId) is { } template)
        {
            if (message.MessageId == UiMessageIds.ButtonClick)
            {
                Main.State.SetTemplate(template.Template, apply: true);
                UpdateToDefaultAttributes();
            }

            return UiListenResult.Default;
        }

        switch (message.ElementId)
        {
            case LockId when message.MessageId == UiMessageIds.ButtonClick && RowFor(message.Element) is { } lockRow:
                SetLock(lockRow.Attribute);
                break;
            case SliderId when message.MessageId == UiMessageIds.ScrollbarPosition && RowFor(message.Element) is { } sliderRow:
                SetAttribValue(sliderRow.Attribute, CharGenRules.AttributeFromSlider(message.Param1));
                break;
            case NumberId when RowFor(message.Element) is { Number: { } number } numberRow:
                OnNumberMessage(numberRow, number, message);
                break;
        }

        return UiListenResult.Default;
    }

    private void OnNumberMessage(Row row, UiTextElement number, in UiElementMessage message)
    {
        switch (message.MessageId)
        {
            case UiMessageIds.Focus when message.Param1 != 0:
                number.SelectAll();
                break;
            case UiMessageIds.Focus:
                SetAttribValue(row.Attribute, CharGenRules.AttributeFromTypedText(number.Text));
                break;
            case UiMessageIds.CharacterTyped or UiMessageIds.TextDeleted:
                row.Slider?.SetFloatProperty(UiPropertyIds.ScrollbarGoalPosition, CharGenRules.SliderGoalFromTypedText(number.Text));
                break;
        }
    }

    private Row? RowOf(uint attribute) => _rows.Values.FirstOrDefault(r => r.Attribute == attribute);

    /// <summary>The row whose field holds <paramref name="element"/> (retail asks the element for its parent field).</summary>
    private Row? RowFor(UiElement element)
    {
        for (var at = element; at is not null; at = at.Parent)
        {
            if (_rows.TryGetValue(at.ElementId, out var row) && ReferenceEquals(row.Field, at))
            {
                return row;
            }
        }

        return null;
    }

    private T? FindIn<T>(uint fieldId, uint childId)
        where T : UiElement
    {
        var field = Find<UiElement>(fieldId);
        if (field?.GetChildRecursive(childId) is T found)
        {
            return found;
        }

        if (field is not null)
        {
            Manager.LogOnce($"chargen:{Name}:{fieldId:X8}:{childId:X8}", LogSeverity.Error, $"{Name}: element 0x{childId:X8} of 0x{fieldId:X8} is missing or is not a {typeof(T).Name}; what it shows is not shown");
        }

        return null;
    }

    private sealed class Row(uint attribute, UiElement field)
    {
        public uint Attribute { get; } = attribute;

        public UiElement Field { get; } = field;

        public UiButton? Lock { get; init; }

        public UiTextElement? Name { get; init; }

        public UiScrollbar? Slider { get; init; }

        public UiTextElement? Number { get; init; }

        public bool Locked { get; set; }
    }
}
