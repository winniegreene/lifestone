using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Toolbar;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: UIElementManager::DoVisibilityToggleAction 0x0045B660; gmToolbarUI::ListenToElementMessage
// 0x004BEE90, ::RecvNotice_SetCombatMode 0x004BD610.
/// <summary>
/// The toolbar (<c>gmFloatyToolbarUI</c>, window 0x10000603 of <c>classic_floatytoolbar</c>; docs/retail-ui.md section 21.5). Two kinds of button:
/// <list type="bullet">
/// <item>The seven <b>panel buttons</b> (social, spell management, skill management, quest management, map, options, inventory) have no handler in retail's toolbar: each is a button whose click action (property 0x12)
/// is the input action that toggles its page of the panel host (<c>UIElementManager::DoVisibilityToggleAction</c>, 11.6096 0x0045B740). Lifestone keeps that, and <see cref="Panels.GamePanelHost"/> answers the page that opens.</item>
/// <item>The <b>action buttons</b> are heard here (<c>gmToolbarUI::ListenToElementMessage</c>, 11.6096 0x004BFB20): the four combat-mode buttons call <c>ToggleCombatMode</c>, Examine calls
/// <c>ClientUISystem::ExamineObject</c> (or asks for a target) and Use calls <c>ItemHolder::UseObject</c> (or asks for a target). Use uses the selected object through the action layer (nothing happens without a selection, R10). <b>M3 3.2:</b> the combat buttons
/// change the combat mode through <see cref="IActionApi.SetCombatMode"/> (<see cref="CombatModeRules.Toggle"/>: <c>ClientCombatSystem::ToggleCombatMode</c>, 11.6096 0x0056D610, asks for the wielded weapon's mode from non-combat and for non-combat otherwise) and the layout's button for the current mode
/// shows (<see cref="SetCombatModeButtons"/>). <b>M5 7 S6 (D136):</b> Examine examines the selected object (<c>ClientUISystem::ExamineObject</c> 11.6096 0x005657B0, the examine panel) or, with nothing selected, enters the examine target mode
/// (<see cref="UiTargetMode"/>: the pointer changes and the next object clicked in the world or in an item list is examined), as retail's case 0x100001A5 does; it no longer opens the NotInM3 message.</item>
/// </list>
/// The selection display (name, health and mana meters) and the stack slider are built by <see cref="Selection.GameSelection"/>; the 18 shortcut slots (class 0x10000031) are the hotbar (<see cref="GameHotbar"/>, M4 3.2 h2): drawn from the
/// shortcut model, used by the number keys and a click, a drag source and target.
/// <para>
/// <b>The panel buttons are one radio group</b> (M5 7 P7r, D148, <c>m5-8-toolbar-radio</c>). <c>gmToolbarUI::PostInit</c> (11.6096 0x004BF710) keeps each panel button with its panel id (property 0x10000029) in its button array, and
/// <c>gmToolbarUI::RecvNotice_SetPanelVisibility</c> (11.6096 0x004BDF90; 11.4186 0x004BD300) answers the panel host's notice: the button of the panel shown enters Highlight (state 6) and the button of the panel hidden enters Normal (state 1).
/// As the host shows one page at a time (and hides the page it replaces, with its own notice), only the open page's button is lit. Each panel button is a toggle button (property 0x0B), which flips its own highlight on a click whatever
/// the page then does, so Lifestone also re-lights the group from the page shown after every toggle the host answers (<see cref="GamePanelHost.ToggleAnswered"/>: a page with no controller opens the NotInM3 message and no page). The
/// notice follows every path a page takes (a toolbar button, a key, the autopilot, a script, a page that overlays another), because it is the host's <see cref="GamePanelHost.PageVisibilityChanged"/>.
/// </para>
/// </summary>
public sealed class GameToolbar : IUiElementListener, IDisposable
{
    /// <summary>The toolbar window.</summary>
    public const uint WindowId = 0x10000603;

    /// <summary>The four combat-mode buttons (all four toggle combat mode; <c>RecvNotice_SetCombatMode</c>, 11.6096 0x004BE2A0, shows the one for the current mode).</summary>
    public static IReadOnlyList<uint> CombatButtonIds { get; } = [0x10000192, 0x10000193, 0x10000194, 0x10000195];

    /// <summary>The Use button.</summary>
    public const uint UseButtonId = 0x1000019D;

    /// <summary>The Examine button.</summary>
    public const uint ExamineButtonId = 0x100001A5;

    /// <summary>The panel buttons: element id, panel id (property 0x10000029) and the page the button's action toggles.</summary>
    public static IReadOnlyList<(uint ButtonId, uint PanelId)> PanelButtons { get; } =
    [
        (0x10000197, 12), // social
        (0x10000198, 13), // spell management
        (0x10000199, 11), // skill management
        (0x1000055A, 25), // quest management
        (0x1000019A, 16), // world map
        (0x1000019B, 10), // options
        (0x100001B1, 7), // inventory
    ];

    private readonly UiLoginContext _context;
    private int _combatMode;
    private GamePanelHost? _host;

    /// <summary>The toolbar of <paramref name="window"/> (null when the layout lacks it).</summary>
    public GameToolbar(UiLoginContext context, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Window = window;
        window?.RegisterForElementMessages(this);
        if (window is not null)
        {
            Hotbar = new GameHotbar(context, window, () => SelectedSource?.Invoke() ?? 0);
        }
    }

    /// <summary>The shortcut bar (M4 3.2 h2), or null when the layout lacks the toolbar window.</summary>
    public GameHotbar? Hotbar { get; }

    /// <summary>Ends the shortcut bar's subscriptions and key map and lets go of the panel host (the session outlives the toolbar). Safe to call twice.</summary>
    public void Dispose()
    {
        Hotbar?.Dispose();
        if (_host is { } host)
        {
            host.PageVisibilityChanged -= OnPanelVisibility;
            host.ToggleAnswered -= SyncPanelButtons;
            _host = null;
        }
    }

    /// <summary>
    /// Hears the panel host (M5 7 P7r, D148): its page notices light the panel buttons (<see cref="OnPanelVisibility"/>, retail's <c>RecvNotice_SetPanelVisibility</c>) and every toggle it answers re-lights the group
    /// (<see cref="SyncPanelButtons"/>). Lights the group from the page shown now. Attach again replaces the host.
    /// </summary>
    public void Attach(GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_host is { } earlier)
        {
            earlier.PageVisibilityChanged -= OnPanelVisibility;
            earlier.ToggleAnswered -= SyncPanelButtons;
        }

        _host = host;
        host.PageVisibilityChanged += OnPanelVisibility;
        host.ToggleAnswered += SyncPanelButtons;
        SyncPanelButtons();
    }

    /// <summary>The panel button of <paramref name="panelId"/>, or null (no button opens that page, or the layout lacks it).</summary>
    public UiButton? PanelButtonFor(uint panelId)
    {
        foreach (var (buttonId, id) in PanelButtons)
        {
            if (id == panelId)
            {
                return Window?.GetChildRecursive(buttonId) as UiButton;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="button"/> shows lit: one of the highlight states (6 to 8: Highlight, its rollover and its pressed state; a toggle button's property 0x0E).</summary>
    public static bool IsLit(UiButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.Highlighted || button.State is UiStateIds.Highlight or UiStateIds.HighlightRollover or UiStateIds.HighlightPressed;
    }

    /// <summary>The panel ids whose buttons show lit now, in <see cref="PanelButtons"/>' order (a test and the review frame read it).</summary>
    public IReadOnlyList<uint> LitPanelButtons
    {
        get
        {
            var lit = new List<uint>(2);
            foreach (var (buttonId, panelId) in PanelButtons)
            {
                if (Window?.GetChildRecursive(buttonId) is UiButton button && IsLit(button))
                {
                    lit.Add(panelId);
                }
            }

            return lit;
        }
    }

    /// <summary>
    /// <c>gmToolbarUI::RecvNotice_SetPanelVisibility</c> (11.6096 0x004BDF90): the button of <paramref name="panelId"/> (if a panel button opens that page) enters Highlight while the page shows and Normal when it hides. The host raises it
    /// for every page change; a page with no toolbar button (character information, the effects, vitae) lights nothing.
    /// </summary>
    public void OnPanelVisibility(uint panelId, bool visible)
    {
        if (PanelButtonFor(panelId) is { } button)
        {
            Light(button, panelId, visible, visible ? "its page showed" : "its page hid");
        }
    }

    /// <summary>
    /// Lights the open page's button and unlights every other panel button (the radio rule, M5 7 P7r). Called after each toggle the host answers, since a toggle button flips its own highlight on the click even when no page changes
    /// (a page with no controller), and on <see cref="Attach"/>.
    /// </summary>
    public void SyncPanelButtons()
    {
        var shown = _host is { IsOpen: true } host ? host.CurrentPanel : 0u;
        foreach (var (buttonId, panelId) in PanelButtons)
        {
            if (Window?.GetChildRecursive(buttonId) is UiButton button)
            {
                Light(button, panelId, panelId == shown, panelId == shown ? "its page is the one shown" : shown == 0 ? "no page is shown" : $"panel {shown} is shown");
            }
        }
    }

    /// <summary>Puts <paramref name="button"/> in Highlight (lit) or Normal (<c>UIElement_Button::SetState</c>: a toggle button's highlight follows) when it is not so already, and logs the change and why.</summary>
    private void Light(UiButton button, uint panelId, bool lit, string why)
    {
        if (IsLit(button) == lit)
        {
            return;
        }

        button.SetState(lit ? UiStateIds.Highlight : UiStateIds.Normal);
        _context.Manager.Services.Log.Info("retailui", $"toolbar: the panel button 0x{button.ElementId:X8} (panel {panelId}) is {(lit ? "lit" : "unlit")}: {why} (gmToolbarUI::RecvNotice_SetPanelVisibility, one lit at a time)");
        if (IsLit(button) != lit)
        {
            _context.Manager.Services.Log.Error("retailui", $"toolbar: the panel button 0x{button.ElementId:X8} (panel {panelId}) did not take its {(lit ? "Highlight" : "Normal")} state (state {button.State}); TODO(m5-8-toolbar-radio-state)");
        }
    }

    /// <summary>Where the Use and Examine buttons read the selected object's id from (0 for none); the shell sets it to the selection display once that exists.</summary>
    public Func<uint>? SelectedSource { get; set; }

    /// <summary>The Use buttons clicked so far (with or without a selection).</summary>
    public int UseClicks { get; private set; }

    /// <summary>The Examine buttons clicked so far (with or without a selection).</summary>
    public int ExamineClicks { get; private set; }

    /// <summary>The toolbar window, or null.</summary>
    public UiElement? Window { get; }

    /// <summary>Where the chat line of a held item that forbids combat goes (the shell's local chat); null: only logged.</summary>
    public Action<string>? ChatNotice { get; set; }

    /// <summary>The combat button presses so far.</summary>
    public int CombatPresses { get; private set; }

    /// <summary>The combat mode the buttons show (<see cref="CombatModes"/>; 0 before any notice).</summary>
    public int ShownCombatMode => _combatMode;

    /// <summary>
    /// <c>gmToolbarUI::RecvNotice_SetCombatMode</c> (11.6096 0x004BE2A0): the button for <paramref name="mode"/> shows (0x10000192 non-combat, 0x10000193 melee, 0x10000194 missile, 0x10000195 magic) and the others hide.
    /// The combat window (<see cref="Panels.GameCombatPanel"/>) calls it on <c>CombatModeChanged</c>, because the toolbar has no end of life to unsubscribe at. Retail also ghosts the shortcut slots that hold spells outside the
    /// magic mode; the hotbar holds items only (a spell row is not drawn: <c>TODO(m4-shortcut-spell)</c>), so there is nothing to ghost (<c>TODO(m3-toolbar-shortcut-ghost)</c>).
    /// </summary>
    public void SetCombatModeButtons(int mode)
    {
        _combatMode = mode;
        var shown = mode switch { CombatModes.Melee => 1, CombatModes.Missile => 2, CombatModes.Magic => 3, _ => 0 };
        for (var i = 0; i < CombatButtonIds.Count; i++)
        {
            Window?.GetChildRecursive(CombatButtonIds[i])?.SetVisible(i == shown);
        }
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.Element.ElementId == UseButtonId)
        {
            // The same Use the double click makes (docs/retail-ui.md section 21.7): the selection is used through the action layer. With nothing selected it does nothing, as retail's does.
            UseClicks++;
            var selected = SelectedSource?.Invoke() ?? 0;
            if (selected != 0)
            {
                _context.Session.Actions.Use(selected, ActionSource.Ui);
            }
            else
            {
                _context.Manager.Services.Log.Info("retailui", "the toolbar's Use button was pressed with nothing selected; nothing happens");
            }

            return UiListenResult.Handled;
        }

        if (message.MessageId is UiMessageIds.Click or UiMessageIds.DoubleClick && message.Param1 == UiMouseActions.Left && Hotbar is not null && Hotbar.HandleClick(message.Element))
        {
            return UiListenResult.Handled; // a left click on a shortcut slot uses it
        }

        if (message.MessageId == UiMessageIds.ButtonClick && IsCombatButton(message.Element.ElementId))
        {
            CombatPresses++;
            CombatModeRules.Toggle(_context.Session, _context.Manager, ChatNotice); // null when the press asks nothing (the mode would not change): the toolbar's log says why
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.ButtonClick && message.Element.ElementId == ExamineButtonId)
        {
            // gmToolbarUI::ListenToElementMessage's 0x100001A5: the selected object is examined; with none, the examine target mode asks for one (the next click names it).
            ExamineClicks++;
            _context.Manager.TargetMode.ExamineObject(SelectedSource?.Invoke() ?? 0);
            return UiListenResult.Handled;
        }

        return UiListenResult.Default;
    }

    private static bool IsCombatButton(uint id) => id is >= 0x10000192 and <= 0x10000195;
}
