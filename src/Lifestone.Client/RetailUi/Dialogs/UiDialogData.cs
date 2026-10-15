using System.Diagnostics.CodeAnalysis;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// A text property (master type String, 7), which only code sets: a dialog's typed response (properties 0x98 and 0x9C).
/// The dats hold none, so the data track has no record for it.
/// </summary>
/// <param name="Id">The property id.</param>
/// <param name="Value">The text.</param>
public sealed record UiTextProperty(uint Id, string Value) : UiProperty(Id)
{
    /// <inheritdoc/>
    public override UiPropertyType Type => UiPropertyType.String;
}

/// <summary>
/// What a dialog is made from and what it answers (retail's <c>PropertyCollection</c> a dialog carries): its type, text,
/// buttons, queue and, once closed, its response. Not thread-safe; the UI thread only.
/// </summary>
/// <remarks>
/// The properties a dialog reads (<see cref="UiPropertyIds"/>): 0x8E type, 0xC5 text, 0xAC modal, 0x8D topmost, 0xC3
/// queue, 0xC6 duration; per type the button texts (0x90/0x91 confirmation, 0x95 message, 0x97 text input, 0x9A/0x9B
/// confirmation text input, 0xA0/0xA2/0xA4 menu, 0xA6/0xA8/0xA9/0xAB confirmation menu) and the wait key 0x9E. A closed
/// dialog adds its response: 0x92 (bool) for a confirmation, 0x98 or 0x9C (text) for a text input, 0xA4 or 0xAB (the
/// chosen index, -1 for none) for a menu.
/// </remarks>
public sealed class UiDialogData
{
    private readonly Dictionary<uint, UiProperty> _properties = [];
    private readonly List<(uint Id, string Text)> _textVariables = [];

    /// <summary>Empty data.</summary>
    public UiDialogData()
    {
    }

    /// <summary>A copy of <paramref name="other"/> (<c>PropertyCollection::operator=</c>).</summary>
    public UiDialogData(UiDialogData other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var (id, property) in other._properties)
        {
            _properties[id] = property;
        }

        _textVariables.AddRange(other._textVariables);
    }

    /// <summary>Every property, by id.</summary>
    public IReadOnlyDictionary<uint, UiProperty> Properties => _properties;

    /// <summary>Sets (or replaces) <paramref name="property"/> (<c>HashTable::set</c>).</summary>
    public UiDialogData Set(UiProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        _properties[property.Id] = property;
        return this;
    }

    /// <summary>Adds <paramref name="property"/> unless one with its id is there (<c>HashTable::add</c>, as the confirmation and text-input dialogs answer).</summary>
    public bool Add(UiProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _properties.TryAdd(property.Id, property);
    }

    /// <summary>Property <paramref name="id"/>, or false.</summary>
    public bool TryGet(uint id, [NotNullWhen(true)] out UiProperty? property) => _properties.TryGetValue(id, out property);

    /// <summary>The plain-text variables of the dialog's text (a StringInfo carries its variables in retail), in the order they were added.</summary>
    public IReadOnlyList<(uint Id, string Text)> TextVariables => _textVariables;

    /// <summary>
    /// Gives the dialog's text a plain-text variable (<c>StringInfo::AddVariable_String</c>), as the delete confirmation does with the
    /// character's name: <paramref name="variableId"/> is the variable's id (the hash of its name). A variable already given is replaced.
    /// </summary>
    public UiDialogData WithTextVariable(uint variableId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _textVariables.RemoveAll(v => v.Id == variableId);
        _textVariables.Add((variableId, text));
        return this;
    }

    /// <summary>The dialog type (0x8E), or 0.</summary>
    public uint Type => _properties.GetEnum(UiPropertyIds.DialogType);

    /// <summary>The dialog's text (0xC5), or null.</summary>
    public UiStringRef? Text => _properties.TryGet<UiStringInfoProperty>(UiPropertyIds.DialogText, out var text) ? text.Value : null;

    /// <summary>A dialog of type <paramref name="type"/> showing <paramref name="text"/>.</summary>
    public static UiDialogData Of(UiDialogType type, in UiStringRef text) =>
        new UiDialogData().Set(new UiEnumProperty(UiPropertyIds.DialogType, (uint)type)).Set(new UiStringInfoProperty(UiPropertyIds.DialogText, text));

    /// <summary>Sets the text property <paramref name="id"/> (a button's label).</summary>
    public UiDialogData WithText(uint id, in UiStringRef text) => Set(new UiStringInfoProperty(id, text));

    /// <summary>Sets the bool property <paramref name="id"/> (modal 0xAC, topmost 0x8D).</summary>
    public UiDialogData WithFlag(uint id, bool value) => Set(new UiBoolProperty(id, value));

    /// <summary>Sets the dialog's queue (0xC3): 1 shows it at once beside the others; any other queue shows one dialog at a time.</summary>
    public UiDialogData WithQueue(uint queue) => Set(new UiEnumProperty(UiPropertyIds.DialogQueue, queue));

    /// <summary>Sets the dialog's duration in seconds (0xC6); its text may show the time left as the variable <c>time</c>.</summary>
    public UiDialogData WithDuration(float seconds) => Set(new UiFloatProperty(UiPropertyIds.DialogDuration, seconds));

    /// <summary>The confirmation's answer (0x92), or null before it closed.</summary>
    public bool? ConfirmationResponse => _properties.TryGet<UiBoolProperty>(UiPropertyIds.DialogConfirmationResponse, out var r) ? r.Value : null;

    /// <summary>A text input's answer (0x98, or 0x9C for a confirmation text input), or null.</summary>
    public string? TextResponse =>
        _properties.TryGetValue(UiPropertyIds.DialogTextInputResponse, out var a) && a is UiTextProperty ta ? ta.Value
        : _properties.TryGetValue(UiPropertyIds.DialogConfirmationTextInputResponse, out var b) && b is UiTextProperty tb ? tb.Value
        : null;

    /// <summary>A menu's answer (0xA4, or 0xAB for a confirmation menu): the chosen index, -1 for none; null before it closed.</summary>
    public int? MenuChoice =>
        _properties.TryGet<UiIntegerProperty>(UiPropertyIds.DialogMenuChoice, out var a) ? a.Value
        : _properties.TryGet<UiIntegerProperty>(UiPropertyIds.DialogConfirmationMenuChoice, out var b) ? b.Value
        : null;
}

/// <summary>
/// The dialog types (property 0x8E) and the element of the dialog layout (UILAYOUT 2, 0x2100003C) each one is, as
/// <c>DialogFactory::CreateDialog_</c> maps them.
/// </summary>
public enum UiDialogType : uint
{
    /// <summary>1: yes or no (element 0x15, ConfirmationDialog).</summary>
    Confirmation = 1,

    /// <summary>2: please wait (element 0x31, WaitDialog).</summary>
    Wait = 2,

    /// <summary>3: a message with OK (element 0x24, MessageDialog).</summary>
    Message = 3,

    /// <summary>4: a text to type (element 0x28, TextInputDialog; the end-of-retail dats have no element 0x28, so none opens).</summary>
    TextInput = 4,

    /// <summary>5: a text to type, with Cancel (element 0x2C, ConfirmationTextInputDialog).</summary>
    ConfirmationTextInput = 5,

    /// <summary>6: a choice from a menu (element 0x1B, MenuDialog).</summary>
    Menu = 6,

    /// <summary>7: a choice from a menu, with Cancel (element 0x1F, ConfirmationMenuDialog).</summary>
    ConfirmationMenu = 7,
}
