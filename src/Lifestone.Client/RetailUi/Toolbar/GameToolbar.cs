using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
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
/// shows (<see cref="SetCombatModeButtons"/>); Examination (gameplay M3 builds later) still opens the NotInM3 message.</item>
/// </list>
/// The selection display (name, health and mana meters), the stack slider and the shortcut slots wait for selection (g5) and for items (the gameplay milestone): they keep the layout's art.
/// </summary>
public sealed class GameToolbar : IUiElementListener
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
    private readonly Dictionary<uint, string> _features = [];
    private int _combatMode;

    /// <summary>The toolbar of <paramref name="window"/> (null when the layout lacks it).</summary>
    public GameToolbar(UiLoginContext context, UiElement? window)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Window = window;
        _features[ExamineButtonId] = "Examination";
        window?.RegisterForElementMessages(this);
    }

    /// <summary>Where the Use button reads the selected object's id from (0 for none); the shell sets it to the selection display once that exists.</summary>
    public Func<uint>? SelectedSource { get; set; }

    /// <summary>The Use buttons clicked so far (with or without a selection).</summary>
    public int UseClicks { get; private set; }

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
    /// magic mode; the slots wait for items (<c>TODO(m3-toolbar-shortcut-ghost)</c>).
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

    /// <summary>The feature an action button opens the message for, or null when <paramref name="buttonId"/> is not one.</summary>
    public string? FeatureOf(uint buttonId) => _features.TryGetValue(buttonId, out var feature) ? feature : null;

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

        if (message.MessageId == UiMessageIds.ButtonClick && IsCombatButton(message.Element.ElementId))
        {
            CombatPresses++;
            CombatModeRules.Toggle(_context.Session, _context.Manager.Services.Log, ChatNotice);
            return UiListenResult.Handled;
        }

        if (message.MessageId == UiMessageIds.ButtonClick && _features.TryGetValue(message.Element.ElementId, out var feature))
        {
            NotInM3.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, feature);
            return UiListenResult.Handled;
        }

        return UiListenResult.Default;
    }

    private static bool IsCombatButton(uint id) => id is >= 0x10000192 and <= 0x10000195;
}
