using System.Globalization;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The base of the seven dialog classes (retail's <c>Dialog</c>, a Field): a full-screen root of the dialog layout with a
/// popup (element 0x3D) holding a text (0x3E) and buttons. It carries its data (<see cref="UiDialogData"/>) and its
/// context (the id <see cref="DialogFactory"/> gave it).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>Dialog::Dialog</c> (11.4186 0x00476830, 11.6096 0x00476BF0), <c>SetData</c> (11.4186 0x00476BE0,
/// 11.6096 0x00476FA0), <c>UpdateDialogText</c> (11.4186 0x00476A50, 11.6096 0x00476E10), <c>UpdatePopupSizeAndPosition</c>
/// (11.4186 0x004769B0, 11.6096 0x00476D70), <c>UpdatePendingDialogDisplay</c> (11.4186 0x00476930, 11.6096 0x00476CF0),
/// <c>ListenToGlobalMessage</c> (11.4186 0x00476B90, 11.6096 0x00476F50) and <c>ListenToElementMessage</c> (11.4186
/// 0x004768C0, 11.6096 0x00476C80).
/// </para>
/// <para>
/// A modal dialog (0xAC) blocks every click to what is behind it. The text (0xC5) is shown with the variable <c>time</c>
/// set to the seconds left of the dialog's duration (0xC6), counting down each frame; when the time is up the dialog is
/// cancelled. The popup grows to fit the text and is centred. A pending display (element 0x33) shows whether more dialogs
/// wait in the queue (<see cref="UiStateIds.DialogPendingTrue"/>, <see cref="UiStateIds.DialogPendingFalse"/>).
/// </para>
/// </remarks>
public abstract class UiDialog : UiField
{
    /// <summary>The popup (element 0x3D).</summary>
    public const uint PopupElement = 0x3D;

    /// <summary>The dialog's text (element 0x3E).</summary>
    public const uint TextElement = 0x3E;

    private const uint PendingDisplayElement = 0x33;

    /// <summary>The variable the dialog's text counts down with: <c>time</c>, by its hash.</summary>
    private static readonly uint TimeVariable = StringHash.Tokenize("time");

    private readonly UiStringVariables _variables = new();
    private double _now = double.NaN;
    private double _expiration = double.NaN;
    private long _shownSeconds = long.MinValue;

    /// <summary>A dialog of <paramref name="init"/>, with no context and no data.</summary>
    protected UiDialog(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The dialog's context, the id the dialog factory gave it (<c>m_nContext</c>); 0 once it closed.</summary>
    public uint Context { get; private set; }

    /// <summary>The dialog's data (<c>m_cData</c>): what it was made from, and its response once it closes.</summary>
    public UiDialogData Data { get; private set; } = new();

    /// <summary>The factory that opened the dialog.</summary>
    protected DialogFactory Factory => Manager.Dialogs;

    /// <summary><c>Dialog::SetContext</c> (11.4186 0x00474C60, 11.6096 0x00475020).</summary>
    internal void SetContext(uint context) => Context = context;

    /// <summary>
    /// <c>Dialog::SetData</c>: the dialog takes a copy of <paramref name="data"/>; a modal one blocks clicks; a duration
    /// starts the countdown; the text is shown. The classes override, call this first, and set their buttons.
    /// </summary>
    protected internal virtual void SetData(UiDialogData data)
    {
        Data = new UiDialogData(data);
        if (Data.TryGet(UiPropertyIds.DialogModal, out var modal) && modal is UiBoolProperty { Value: true })
        {
            SetBoolProperty(UiPropertyIds.ElementBlocksClicks, true);
        }

        if (Data.TryGet(UiPropertyIds.DialogDuration, out var duration))
        {
            _now = Manager.Now;
            _expiration = (duration is UiFloatProperty seconds ? seconds.Value : 0f) + _now;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }

        UpdateDialogText();
    }

    /// <summary>
    /// <c>Dialog::UpdateDialogText</c>: the text element shows the dialog's text with <c>time</c> set to the whole seconds
    /// left (0 without a duration), and the popup fits it.
    /// </summary>
    protected void UpdateDialogText()
    {
        var secondsLeft = SecondsLeft();
        _shownSeconds = secondsLeft;
        if (Data.Text is not { } text || GetChildRecursive(TextElement) is not UiTextElement label)
        {
            return;
        }

        if (Manager.Services.Strings?.Language is { } language)
        {
            _variables.SetInteger(TimeVariable, secondsLeft, language);
        }
        else
        {
            _variables.SetText(TimeVariable, secondsLeft.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var (id, value) in Data.TextVariables)
        {
            _variables.SetText(id, value);
        }

        label.SetStringInfo(text, _variables);
        UpdatePopupSizeAndPosition();
    }

    /// <summary>The whole seconds left of the duration (truncated, as retail's conversion), or 0 without one.</summary>
    private long SecondsLeft() => double.IsNaN(_expiration) ? 0L : (long)(_expiration - _now);

    /// <summary>
    /// <c>Dialog::UpdatePopupSizeAndPosition</c>: the popup grows by the text's overflow (never shrinks) and is centred on the
    /// dialog.
    /// </summary>
    protected void UpdatePopupSizeAndPosition()
    {
        if (GetChildRecursive(PopupElement) is not { } popup)
        {
            return;
        }

        if (GetChildRecursive(TextElement) is UiTextElement label)
        {
            label.RecalculateGlyphList();
            var height = popup.Box.Height + (label.PaperHeight - label.Box.Height);
            if (popup.Box.Height < height)
            {
                popup.ResizeTo(popup.Box.Width, height);
            }
        }

        popup.CenterAt(Box.Width / 2, Box.Height / 2);
    }

    /// <summary><c>Dialog::UpdatePendingDialogDisplay</c>: the pending display shows whether <paramref name="pendingCount"/> dialogs wait.</summary>
    internal void UpdatePendingDialogDisplay(int pendingCount) =>
        GetChildRecursive(PendingDisplayElement)?.SetState(pendingCount != 0 ? UiStateIds.DialogPendingTrue : UiStateIds.DialogPendingFalse);

    /// <summary>
    /// The dialog is dismissed without an answer (retail's <c>CancelDialog</c> virtual: the duration ran out, or the host
    /// cancels it): the class sets its "no" response and closes.
    /// </summary>
    public abstract void CancelDialog();

    /// <summary>
    /// <c>Dialog::ListenToGlobalMessage</c>: the countdown, and the cancel when the duration is up. Retail renders the text
    /// again every frame; Lifestone only when the whole seconds shown change, which looks the same and keeps the frame
    /// allocation-free.
    /// </summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame || double.IsNaN(_expiration) || Manager.Now <= _now)
        {
            return;
        }

        _now = Manager.Now;
        if (_now <= _expiration)
        {
            if (SecondsLeft() != _shownSeconds)
            {
                UpdateDialogText();
            }
        }
        else
        {
            _expiration = double.NaN;
            Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
            CancelDialog();
        }
    }

    /// <summary><c>Dialog::ListenToElementMessage</c>: <see cref="UiMessageIds.DialogRelayout"/> about the dialog lays its popup out again.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.DialogRelayout && ReferenceEquals(message.Element, this))
        {
            UpdatePopupSizeAndPosition();
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>Shows <paramref name="property"/>'s text (a StringInfo of the data) in the text element <paramref name="elementId"/>.</summary>
    protected void ShowText(uint property, uint elementId)
    {
        if (Data.TryGet(property, out var value) && value is UiStringInfoProperty text && GetChildRecursive(elementId) is UiTextElement label)
        {
            label.SetStringInfo(text.Value);
        }
    }

    /// <summary>Closes the dialog through its factory (<c>DialogFactory::CloseDialog</c> with the context).</summary>
    protected void Close() => Factory.CloseDialog(Context);
}
