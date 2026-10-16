using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Widgets;

namespace Lifestone.Client.RetailUi.Indicators;

/// <summary>
/// The effects (class 0x10000002), vitae (0x10000006) and burden (0x10000001) indicators of the indicators window (<c>gmUIElement_EffectsIndicator</c> 11.6096 0x004E7530 family,
/// <c>gmUIElement_VitaeIndicator</c> 0x004E6B30 family, <c>gmUIElement_BurdenIndicator</c> 0x004E7810 family; M3 2.2 a3): buttons whose element state follows the player (lit, ghosted, the three load states). The state
/// is chosen by <see cref="GameIndicators"/>, which hears the session; the button's action (property 0x12) is the ordinary button click that the panel host answers by opening the page. A button with a
/// <see cref="FrameHook"/> also hears the frame message, for the one thing the session cannot announce (the SpellTable finishing its load).
/// </summary>
public sealed class UiStateIndicator : UiButton
{
    /// <summary>The classes of the effects, vitae and burden indicators.</summary>
    public const uint EffectsClassId = 0x10000002, VitaeClassId = 0x10000006, BurdenClassId = 0x10000001;

    /// <summary>The indicator of <paramref name="init"/>.</summary>
    public UiStateIndicator(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>Called on each frame message when set; allocates nothing.</summary>
    public Action? FrameHook { get; set; }

    /// <inheritdoc/>
    protected internal override void PostInit()
    {
        base.PostInit();
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
    }

    /// <inheritdoc/>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        if (messageId == UiGlobalMessageIds.Frame)
        {
            FrameHook?.Invoke();
        }

        base.ListenToGlobalMessage(messageId, data);
    }
}
