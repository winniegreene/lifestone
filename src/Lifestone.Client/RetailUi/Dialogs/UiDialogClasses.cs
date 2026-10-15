using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Dialogs;

/// <summary>
/// The message dialog (class 0x17, type 3; retail's <c>MessageDialog</c>): a text and an OK button (element 0x26, its
/// label property 0x95). OK closes it (<c>MessageDialog::ListenToElementMessage</c>, 11.4186 0x004752C0, 11.6096
/// 0x00475670; <c>MessageDialog::SetData</c>, 11.4186 0x00475330, 11.6096 0x004756F0).
/// </summary>
public sealed class UiMessageDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The OK button.</summary>
    public const uint OkButton = 0x26;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        ShowText(UiPropertyIds.DialogMessageOkButtonText, OkButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == OkButton)
        {
            Close();
            return UiListenResult.Consumed;
        }

        return base.ListenToElementMessage(message);
    }

    /// <summary>Closes the dialog (retail's message dialog cancels by closing).</summary>
    public override void CancelDialog() => Close();
}

/// <summary>
/// The confirmation dialog (class 0x13, type 1; retail's <c>ConfirmationDialog</c>): a question with yes (element 0x17,
/// label 0x90) and no (0x19, label 0x91). The answer is property 0x92 (<c>ConfirmationDialog::SetData</c>, 11.4186
/// 0x004764D0, 11.6096 0x00476890; <c>ListenToElementMessage</c>, 11.4186 0x00476670, 11.6096 0x00476A30;
/// <c>CancelDialog</c>, 11.4186 0x00476770, 11.6096 0x00476B30: no).
/// </summary>
public sealed class UiConfirmationDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The yes button.</summary>
    public const uint YesButton = 0x17;

    /// <summary>The no button.</summary>
    public const uint NoButton = 0x19;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        ShowText(UiPropertyIds.DialogConfirmationYesButtonText, YesButton);
        ShowText(UiPropertyIds.DialogConfirmationNoButtonText, NoButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return base.ListenToElementMessage(message);
        }

        if (message.ElementId is YesButton or NoButton)
        {
            Answer(message.ElementId == YesButton);
            return UiListenResult.Consumed;
        }

        return UiListenResult.Default;
    }

    /// <inheritdoc/>
    public override void CancelDialog() => Answer(false);

    private void Answer(bool yes)
    {
        Data.Add(new UiBoolProperty(UiPropertyIds.DialogConfirmationResponse, yes));
        Close();
    }
}

/// <summary>
/// The wait dialog (class 0x19, type 2; retail's <c>WaitDialog</c>): a text that stays until it is closed, or until the
/// key action property 0x9E names starts unclaimed (<c>WaitDialog::SetData</c>, 11.4186 0x00474D60, 11.6096 0x00475110;
/// <c>ListenToGlobalMessage</c>, 11.4186 0x00474CD0, 11.6096 0x00475090).
/// </summary>
public sealed class UiWaitDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The action that closes the dialog (<c>m_eKey</c>), or 0.</summary>
    public uint Key { get; private set; }

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        if (Data.TryGet(UiPropertyIds.DialogWaitKey, out var key) && key is UiEnumProperty action)
        {
            Key = action.Value;
            Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.ActionStart);
        }
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId == UiGlobalMessageIds.ActionStart && Key != 0 && unchecked((uint)data) == Key)
        {
            Close();
        }
    }

    /// <summary>Closes the dialog.</summary>
    public override void CancelDialog() => Close();
}

/// <summary>
/// The text input dialog (class 0x18, type 4; retail's <c>TextInputDialog</c>): a field (element 0x2B) and Done (0x2A,
/// label 0x97); the answer is property 0x98, empty when cancelled (<c>TextInputDialog::SetData</c>, 11.4186 0x00474F00,
/// 11.6096 0x004752B0; <c>ListenToElementMessage</c>, 11.4186 0x00475050, 11.6096 0x00475400; <c>CancelDialog</c>,
/// 11.4186 0x00475190, 11.6096 0x00475540). The end-of-retail dialog layout has no element for this type.
/// </summary>
public sealed class UiTextInputDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The Done button.</summary>
    public const uint DoneButton = 0x2A;

    /// <summary>The field.</summary>
    public const uint Field = 0x2B;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        ShowText(UiPropertyIds.DialogTextInputDoneButtonText, DoneButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.ElementId != DoneButton || message.MessageId != UiMessageIds.ButtonClick)
        {
            return base.ListenToElementMessage(message);
        }

        if (GetChildRecursive(Field) is UiTextElement field)
        {
            Data.Add(new UiTextProperty(UiPropertyIds.DialogTextInputResponse, field.Text));
        }

        Close();
        return UiListenResult.Consumed;
    }

    /// <inheritdoc/>
    public override void CancelDialog()
    {
        Data.Add(new UiTextProperty(UiPropertyIds.DialogTextInputResponse, string.Empty));
        Close();
    }
}

/// <summary>
/// The confirmation text input dialog (class 0x15, type 5; retail's <c>ConfirmationTextInputDialog</c>; the
/// delete-character confirmation): a field (element 0x2C), Done (0x2E, label 0x9A) and Cancel (0x2F, label 0x9B); the
/// answer is property 0x9C, empty when cancelled (<c>ConfirmationTextInputDialog::SetData</c>, 11.4186 0x00475A40, 11.6096
/// 0x00475E00; <c>ListenToElementMessage</c>, 11.4186 0x00475BE0, 11.6096 0x00475FA0; <c>CancelDialog</c>, 11.4186
/// 0x00475D40, 11.6096 0x00476100).
/// </summary>
public sealed class UiConfirmationTextInputDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The field.</summary>
    public const uint Field = 0x2C;

    /// <summary>The Done button.</summary>
    public const uint DoneButton = 0x2E;

    /// <summary>The Cancel button.</summary>
    public const uint CancelButton = 0x2F;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        ShowText(UiPropertyIds.DialogConfirmationTextInputDoneButtonText, DoneButton);
        ShowText(UiPropertyIds.DialogConfirmationTextInputCancelButtonText, CancelButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return base.ListenToElementMessage(message);
        }

        if (message.ElementId == CancelButton)
        {
            CancelDialog();
            return UiListenResult.Consumed;
        }

        if (message.ElementId != DoneButton)
        {
            return base.ListenToElementMessage(message);
        }

        if (GetChildRecursive(Field) is UiTextElement field)
        {
            Data.Add(new UiTextProperty(UiPropertyIds.DialogConfirmationTextInputResponse, field.Text));
        }

        Close();
        return UiListenResult.Consumed;
    }

    /// <inheritdoc/>
    public override void CancelDialog()
    {
        Data.Add(new UiTextProperty(UiPropertyIds.DialogConfirmationTextInputResponse, string.Empty));
        Close();
    }
}

/// <summary>
/// The menu dialog (class 0x16, type 6; retail's <c>MenuDialog</c>): a menu (element 0x1D) of the texts property 0xA0
/// lists, starting at the item 0xA4 names (else the first), and OK (0x1E, label 0xA2); the answer is property 0xA4, the
/// chosen index, -1 when cancelled (<c>MenuDialog::SetData</c>, 11.4186 0x004754F0, 11.6096 0x004758B0;
/// <c>ListenToElementMessage</c>, 11.4186 0x004757E0, 11.6096 0x00475BA0; <c>CancelDialog</c>, 11.4186 0x004758E0, 11.6096
/// 0x00475CA0).
/// </summary>
public sealed class UiMenuDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The menu.</summary>
    public const uint MenuElement = 0x1D;

    /// <summary>The OK button.</summary>
    public const uint OkButton = 0x1E;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        MenuDialogs.Fill(this, MenuElement, UiPropertyIds.DialogMenuItemArray, UiPropertyIds.DialogMenuChoice);
        ShowText(UiPropertyIds.DialogMenuOkButtonText, OkButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return base.ListenToElementMessage(message);
        }

        if (message.ElementId != OkButton)
        {
            return UiListenResult.Default;
        }

        Answer((GetChildRecursive(MenuElement) as UiMenu)?.SelectedIndex ?? -1);
        return UiListenResult.Consumed;
    }

    /// <inheritdoc/>
    public override void CancelDialog() => Answer(-1);

    private void Answer(int choice)
    {
        Data.Set(new UiIntegerProperty(UiPropertyIds.DialogMenuChoice, choice));
        Close();
    }
}

/// <summary>
/// The confirmation menu dialog (class 0x14, type 7; retail's <c>ConfirmationMenuDialog</c>): a menu (element 0x21) of the
/// texts property 0xA6 lists, OK (0x22, label 0xA8) and Cancel (0x23, label 0xA9); the answer is property 0xAB, -1 for
/// Cancel (<c>ConfirmationMenuDialog::SetData</c>, 11.4186 0x00475EB0, 11.6096 0x00476270; <c>ListenToElementMessage</c>,
/// 11.4186 0x00476230, 11.6096 0x004765F0; <c>CancelDialog</c>, 11.4186 0x00476340, 11.6096 0x00476700).
/// </summary>
public sealed class UiConfirmationMenuDialog(in UiElementInit init) : UiDialog(init)
{
    /// <summary>The menu.</summary>
    public const uint MenuElement = 0x21;

    /// <summary>The OK button.</summary>
    public const uint OkButton = 0x22;

    /// <summary>The Cancel button.</summary>
    public const uint CancelButton = 0x23;

    /// <inheritdoc/>
    protected internal override void SetData(UiDialogData data)
    {
        base.SetData(data);
        MenuDialogs.Fill(this, MenuElement, UiPropertyIds.DialogConfirmationMenuItemArray, UiPropertyIds.DialogConfirmationMenuChoice);
        ShowText(UiPropertyIds.DialogConfirmationMenuOkButtonText, OkButton);
        ShowText(UiPropertyIds.DialogConfirmationMenuCancelButtonText, CancelButton);
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return base.ListenToElementMessage(message);
        }

        if (message.ElementId is not (OkButton or CancelButton))
        {
            return UiListenResult.Default;
        }

        var choice = message.ElementId == OkButton ? (GetChildRecursive(MenuElement) as UiMenu)?.SelectedIndex ?? -1 : -1;
        Data.Set(new UiIntegerProperty(UiPropertyIds.DialogConfirmationMenuChoice, choice));
        Close();
        return UiListenResult.Consumed;
    }

    /// <inheritdoc/>
    public override void CancelDialog()
    {
        Data.Set(new UiIntegerProperty(UiPropertyIds.DialogConfirmationMenuChoice, -1));
        Close();
    }
}

/// <summary>The menu dialogs' shared setup.</summary>
internal static class MenuDialogs
{
    /// <summary>The menu gets one text row per item of the array, and selects the item the choice names (else the first).</summary>
    public static void Fill(UiDialog dialog, uint menuId, uint itemsProperty, uint choiceProperty)
    {
        if (dialog.GetChildRecursive(menuId) is not UiMenu menu)
        {
            return;
        }

        if (dialog.Data.TryGet(itemsProperty, out var items) && items is UiArrayProperty array)
        {
            foreach (var item in array.Items)
            {
                if (item is UiStringInfoProperty text)
                {
                    menu.InsertTextItem(text.Value, menu.ItemCount);
                }
            }

            menu.SetSelectedItem(menu.GetItem(0), broadcast: true);
        }

        if (dialog.Data.TryGet(choiceProperty, out var choice) && choice is UiIntegerProperty index)
        {
            menu.SetSelectedItem(menu.GetItem(index.Value), broadcast: true);
        }
    }
}
