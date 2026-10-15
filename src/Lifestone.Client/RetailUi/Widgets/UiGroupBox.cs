using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The group box (class 0x11, retail's <c>UIElement_GroupBox</c>): radio buttons. The selected button (property 0xB1) is
/// in the Highlight state, the others Normal; clicking another selects it; clicking the selected one keeps it selected
/// and goes no further unless property 0xC1 lets the click through.
/// </summary>
/// <remarks>
/// Re-expressed from <c>UIElement_GroupBox::PostInit</c> (11.4186 0x0046AC60, 11.6096 0x0046AEF0), <c>OnSetAttribute</c>
/// (11.4186 0x0046ACA0, 11.6096 0x0046AF30) and <c>ListenToElementMessage</c> (11.4186 0x0046AD20, 11.6096 0x0046AFB0).
/// </remarks>
public class UiGroupBox : UiElement
{
    /// <summary>A group box of <paramref name="init"/>.</summary>
    public UiGroupBox(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The selected button's element id (<c>m_idSelectedButton</c>), or 0.</summary>
    public uint SelectedButtonId { get; private set; }

    /// <summary>Selects button <paramref name="buttonId"/> (property 0xB1).</summary>
    public void SelectButton(uint buttonId) => SetEnumProperty(UiPropertyIds.GroupBoxSelectedButton, buttonId);

    /// <summary><c>UIElement_GroupBox::PostInit</c>: the default button (property 0xB0) becomes the selected one.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        if (TryGetEnumProperty(UiPropertyIds.GroupBoxDefaultButton, out var id) && id != 0)
        {
            SelectButton(id);
        }
    }

    /// <summary><c>UIElement_GroupBox::OnSetAttribute</c>: a new selection puts the old button back to Normal and the new one in Highlight.</summary>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        if (id == UiPropertyIds.GroupBoxSelectedButton)
        {
            if (SelectedButtonId != 0)
            {
                GetChildRecursive(SelectedButtonId)?.SetState(UiStateIds.Normal);
            }

            if (value is UiEnumProperty selected)
            {
                SelectedButtonId = selected.Value;
            }

            GetChildRecursive(SelectedButtonId)?.SetState(UiStateIds.Highlight);
        }

        base.OnSetAttribute(id, value);
    }

    /// <summary><c>UIElement_GroupBox::ListenToElementMessage</c>: a button click in the box selects that button.</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick)
        {
            var selected = TryGetEnumProperty(UiPropertyIds.GroupBoxSelectedButton, out var id) ? id : 0;
            if (selected == message.ElementId)
            {
                message.Element.SetState(UiStateIds.Highlight);
                if (!(TryGetBoolProperty(UiPropertyIds.GroupBoxAllowButtonClickOnSelectedButton, out var allow) && allow))
                {
                    return UiListenResult.Consumed;
                }
            }
            else
            {
                SelectButton(message.ElementId);
            }
        }

        return base.ListenToElementMessage(message);
    }
}
