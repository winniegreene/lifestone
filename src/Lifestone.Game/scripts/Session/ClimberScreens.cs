using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Game.Core;
using Lifestone.Game.RetailUi;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M6 onscreen start: the Godot side of <see cref="IClimberScreens"/> (<c>--login-check=climber-start</c>). The character list's select and delete and the summary page's Finish are the throwaway check's own clicks (<see cref="ThrowawayScreens"/>, live-proven);
/// the pages of character creation are <see cref="ClimberChargenDriver"/>'s, over the same retail UI and its input router, at the same <c>--video-pace</c>. Nothing here talks to the server except through those screens.
/// </summary>
internal sealed class ClimberScreens(RetailUiLayer ui, IGameSessionView session) : IClimberScreens
{
    private readonly ThrowawayScreens _screens = new(ui, session);

    /// <inheritdoc/>
    public ScreenOp SelectCharacter(string name) => _screens.SelectCharacter(name);

    /// <inheritdoc/>
    public ScreenOp DeleteSelected() => _screens.DeleteSelected();

    /// <inheritdoc/>
    public ScreenOp OpenCharGenAs(ClimberChargenPlan plan) =>
        ClimberChargenDriver.OpenTo(ui.Manager, () => ui.Screens?.Flow.CurrentScreen, plan, () => session.Clock.Now, ThrowawayScreens.VideoPaceSeconds, line => Log.Info(ClimberCreateScenario.LogCategory, line));

    /// <inheritdoc/>
    public ScreenOp Finish(string name) => _screens.Finish(name);
}
