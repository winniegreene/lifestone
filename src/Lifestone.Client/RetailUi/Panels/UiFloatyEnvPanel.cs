using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The environment panel's holding floaty (<c>gmFloatyEnvPanelUI</c>, class 0x1000004E, window 0x100005FD of the gameplay layout: 610 by 120 at 0,400, over the world view; M4 2.2 s2): a container that shows while one of its panel
/// windows (a child with a panel id, property 0x10000029) shows and hides when none does. Its windows are the vendor (<c>gmVendorUI</c> 0x10000062, panel 0x17), the external container (<see cref="GameContainerPanel"/>, 0x1000005D,
/// panel 0x12: a corpse or chest the player has open), the salvage window (0x1000005E, panel 0x13), the secure trade window (0x1000005F, panel 0x14) and the slumlord window (0x10000060). Retail's class has no address in the docs
/// (<c>TODO(m4-retail-container-window)</c>); the rule is <see cref="UiFloatyCombatPanel"/>'s, without the host's remembered-panel stack. The two floaties sit at the same place (0,400), so one shows at a time: while this floaty shows a window
/// the combat floaty is hidden (<see cref="UiFloatyCombatPanel.UpdateVisibility"/>), and when this one hides the combat floaty is told and shows again if its windows still want it (the combat mode decides that). Retail's host does this with
/// its remembered-panel stack (<c>RecvNotice_SetPanelVisibility</c>, docs/retail-ui.md section 23.8); that function's address for this host is unknown (<c>TODO(m4-floaty-stack-retail)</c>, logged once when the combat floaty is hidden).
/// </summary>
/// <remarks>
/// The layout shows every window of the panel by default and the floaty itself hidden. Lifestone builds the external container's window (M4 2.2) and the vendor's (<see cref="GameVendorPanel"/>, M4 3.2 s4b: <c>m4-env-panel-windows</c> closes
/// for the vendor): <see cref="PostInit"/> hides the others (plain stand-ins until the item that builds each: salvage, secure trade, slumlord), so showing the floaty for a container or a vendor does not draw them (<c>TODO(m4-env-panel-windows)</c>:
/// the item that builds a window decides when it shows).
/// </remarks>
public sealed class UiFloatyEnvPanel : UiElement
{
    /// <summary>The class id (<c>gmFloatyEnvPanelUI</c>).</summary>
    public const uint RetailClassId = 0x1000004E;

    /// <summary>A floaty of <paramref name="init"/>.</summary>
    public UiFloatyEnvPanel(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>Shows the floaty when one of its panel windows is visible, hides it otherwise. Allocation-free.</summary>
    public void UpdateVisibility()
    {
        var any = false;
        var children = Children;
        for (var i = 0; i < children.Count && !any; i++)
        {
            any = children[i].Visible && children[i].TryGetEnumProperty(UiPropertyIds.ElementPanelId, out var id) && id != 0;
        }

        if (Visible != any)
        {
            SetVisible(any);
            TellCombatFloaty(); // the floaties share their place: the combat floaty hides while this one shows and comes back when this one hides (M4 2.2 fix2, 2.2-R12)
        }
    }

    /// <summary>Asks the combat floaty beside this one to re-read its rule (<see cref="UiFloatyCombatPanel.UpdateVisibility"/> looks at this floaty). Allocation-free.</summary>
    private void TellCombatFloaty()
    {
        var siblings = Parent?.Children;
        for (var i = 0; siblings is not null && i < siblings.Count; i++)
        {
            if (siblings[i] is UiFloatyCombatPanel combat)
            {
                combat.UpdateVisibility();
            }
        }
    }

    /// <summary>Hides the windows that are not built (every panel window but the external container's), then follows the windows that are.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        var children = Children;
        var hidden = 0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Desc.Type != GameContainerPanel.RetailClassId && child.Desc.Type != GameVendorPanel.RetailClassId && child.TryGetEnumProperty(UiPropertyIds.ElementPanelId, out var id) && id != 0 && child.Visible)
            {
                child.SetVisible(false);
                hidden++;
            }
        }

        if (hidden > 0)
        {
            Manager.LogOnce("envpanel:unbuilt", LogSeverity.Warning, $"gmFloatyEnvPanelUI: {hidden} of its panel windows (the salvage, secure trade and slumlord windows) are not built and stay hidden (TODO(m4-env-panel-windows)); the external container's window is GameContainerPanel and the vendor's is GameVendorPanel");
        }

        UpdateVisibility();
    }

    /// <inheritdoc/>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityChanged && ReferenceEquals(message.Element.Parent, this) && message.Element.TryGetEnumProperty(UiPropertyIds.ElementPanelId, out var id) && id != 0)
        {
            UpdateVisibility();
        }

        return base.ListenToElementMessage(message);
    }
}
