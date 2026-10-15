using Lifestone.Client.RetailUi.Flow;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The screens of the modes <c>gmUIFlow</c> registers, as far as M2 builds them (m2-plan G2 U3-U4, G3 U5): the data check, the intro (skipped,
/// D38), character management, character creation (<see cref="CharGenMainScreen"/>, when the application gives the context its CharGen data),
/// the disconnected screen and the epilogue. The game screen is the in-game shell (<see cref="GamePlayScreen"/>, G4; docs/retail-ui.md §21) and character
/// creation without CharGen data is a stand-in of <see cref="UiStandInScreens"/>, and the credits have no screen (the Credits button opens a "not in M2" dialog, D38).
/// </summary>
public static class LoginScreens
{
    /// <summary>The screen of <paramref name="mode"/> over <paramref name="context"/>, or null for a mode with none (its stand-in logs the TODO).</summary>
    public static IUiScreen? Create(UiLoginContext context, uint mode)
    {
        ArgumentNullException.ThrowIfNull(context);
        return mode switch
        {
            UiModes.DataPatch => new DataPatchScreen(context),
            UiModes.Intro => new IntroSkippedScreen(context),
            UiModes.CharacterManagement => new CharacterManagementScreen(context),
            UiModes.Disconnected => new DisconnectedScreen(context),
            UiModes.CharGen when context.CharGen is not null => new CharGenMainScreen(context),
            UiModes.Epilogue => new EpilogueScreen(context),
            UiModes.GamePlay => new GamePlayScreen(context),
            _ => UiStandInScreens.Create(context.Manager, mode),
        };
    }

    /// <summary>
    /// True when <paramref name="screen"/> of <paramref name="mode"/> is a stand-in (the layout's art and the classes' own behaviour, no screen logic),
    /// which the host labels as a placeholder. A screen that is partly built says what is missing itself (<see cref="IUiPlaceholderScreen"/>).
    /// </summary>
    public static bool IsStandIn(IUiScreen? screen, uint mode) => screen is not IUiPlaceholderScreen && mode is UiModes.CharGen;
}

/// <summary>
/// A screen that is built in part: it tells the host what is still a placeholder (a page with no logic, a 3D view not bound), for the host's note
/// on screen. The note is null when nothing on the screen is a placeholder.
/// </summary>
public interface IUiPlaceholderScreen
{
    /// <summary>What on the screen is a placeholder now, or null.</summary>
    string? PlaceholderNote { get; }

    /// <summary>Raised when <see cref="PlaceholderNote"/> changes.</summary>
    event Action? PlaceholderNoteChanged;
}
