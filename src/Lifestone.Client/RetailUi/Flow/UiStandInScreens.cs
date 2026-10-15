using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Flow;

/// <summary>
/// Stand-in screens for the UI modes whose real screen is not built yet: character creation (G3). The data
/// check, the disconnected screen and character management are real since G2 U3-U4 (<see cref="Screens.LoginScreens"/>), and this class
/// still makes their stand-ins when asked. A stand-in shows its mode root's retail layout with the classes' own behaviour and none of the
/// screen's: a <b>placeholder</b>, labelled on screen, in which nothing fills the lists, ghosts the buttons or answers the dialogs. The
/// layouts, fonts and strings are retail's, so what is on screen is retail's art.
/// </summary>
/// <remarks>
/// The login screens (data check, disconnected, character management and character creation) run in a forced 800x600 display,
/// centred in the window (<c>Device::ForceDisplayResolution</c>, docs/retail-ui.md §6); the game screen (<c>GamePlayScreen</c>) releases it, as
/// <c>gmGamePlayUI</c>'s constructor does. The intro and the credits are skipped in M2 (D38), and the epilogue belongs to the
/// game flow: those modes make no screen here, loudly.
/// </remarks>
public static class UiStandInScreens
{
    /// <summary>
    /// The stand-in screen of <paramref name="mode"/> over <paramref name="manager"/>, or null for a mode with none (logged once
    /// as a TODO; the flow then keeps the mode queued, as retail does for a mode with no screen).
    /// </summary>
    public static IUiScreen? Create(UiElementManager manager, uint mode)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var (root, forced) = mode switch
        {
            UiModes.DataPatch => (UiLayoutEnums.DataPatchRoot, true),
            UiModes.Disconnected => (UiLayoutEnums.DisconnectedRoot, true),
            UiModes.CharacterManagement => (UiLayoutEnums.CharacterManagementRoot, true),
            UiModes.CharGen => (UiLayoutEnums.CharGenRoot, true),
            _ => default,
        };
        if (root == default)
        {
            manager.LogOnce(
                $"standin:{mode:X8}",
                LogSeverity.Warning,
                $"TODO(retailui-runtime): {UiModes.NameOf(mode)} (mode 0x{mode:X8}) has no stand-in screen: the intro and credits are skipped in M2 (D38) and the epilogue belongs to the game flow; the mode stays queued (placeholder)");
            return null;
        }

        manager.LogOnce(
            $"standin:{mode:X8}",
            LogSeverity.Warning,
            $"TODO(retailui-runtime): {UiModes.NameOf(mode)} shows a stand-in: the layout's art and the widget classes' own behaviour, none of the screen's logic (placeholder)");
        return new StandInScreen(manager, root, forced);
    }

    private sealed class StandInScreen : IUiScreen
    {
        private readonly UiElementManager _manager;
        private readonly UiElement? _root;

        public StandInScreen(UiElementManager manager, UiRootRef root, bool forced)
        {
            _manager = manager;
            manager.ForceDisplayResolution(forced);
            _root = manager.CreateRootElement(root.LayoutEnum, root.ElementId);
        }

        public void DisplayError(in UiStringRef text) => _manager.LogOnce("standin:error", LogSeverity.Warning, "TODO(retailui-runtime): a stand-in screen does not show the error it was queued with (placeholder)");

        public void Update()
        {
        }

        public void Show(bool visible) => _root?.SetVisible(visible);

        public void Dispose()
        {
            if (_root is not null)
            {
                _manager.DeleteElement(_root);
            }
        }
    }
}
