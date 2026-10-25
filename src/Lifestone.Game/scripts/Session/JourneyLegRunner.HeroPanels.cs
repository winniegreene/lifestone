using Lifestone.Client.RetailUi.Panels;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The hero video's panels (the M4 hero step, the owner's re-cut; <c>--journey-film</c> only, <see cref="HeroPanels"/>): the page a moment wants is opened through its toolbar action, as a player's click opens it,
/// and closed the same way <see cref="HeroPanels.HoldSeconds"/> after the moment ends. The character page opens on its attributes tab; the inventory lists the main pack, where the loot lands. Nothing here
/// changes what a station does.
/// </summary>
public sealed partial class JourneyLegRunner
{
    private uint _heroPanel;
    private double _heroPanelEndedAt = double.NaN;
    private bool _heroPanelDressed;

    /// <summary>Opens, dresses and closes the page the moment wants. Called once a frame after the journey stepped.</summary>
    private void StepHeroPanels(IGameSessionView session, JourneyScenario scenario)
    {
        if (!_film || _ui?.Screens?.Flow.CurrentScreen is not GamePlayScreen { IsBuilt: true, PanelHost: { } host, PanelControllers: { } panels })
        {
            return;
        }

        var deathStage = scenario.CurrentRun is IStationInner { Scenario: DeathScenario death } ? death.Stage : null;
        var want = HeroPanels.Want(_observer.Current, deathStage, session.State.Containers.Viewed is not null);
        if (want != 0)
        {
            _heroPanelEndedAt = double.NaN;
            if (_heroPanel != want)
            {
                _heroPanel = want;
                _heroPanelDressed = false;
                if (host.CurrentPanel != want)
                {
                    host.Toggle(want);
                }

                Log.Info(LogCategory, $"journey: the hero panel {want} is opened for station {_observer.Current}" + (deathStage is null ? string.Empty : $" ({deathStage})"));
            }

            if (!_heroPanelDressed && host.CurrentPanel == want)
            {
                _heroPanelDressed = want == HeroPanels.CharacterInfo
                    ? host.ElementFor(want) is UiPanel page && (page.OpenTabId == HeroPanels.AttributesTab || page.OpenTab(HeroPanels.AttributesTab))
                    : MainPackListed(session, panels);
            }

            return;
        }

        if (_heroPanel == 0)
        {
            return;
        }

        if (double.IsNaN(_heroPanelEndedAt))
        {
            _heroPanelEndedAt = _runSeconds;
        }

        if (_runSeconds - _heroPanelEndedAt < HeroPanels.HoldSeconds)
        {
            return;
        }

        if (host.CurrentPanel == _heroPanel)
        {
            host.Toggle(_heroPanel); // closes the page, as its toolbar button does
        }

        Log.Info(LogCategory, $"journey: the hero panel {_heroPanel} is closed");
        (_heroPanel, _heroPanelEndedAt) = (0, double.NaN);
    }

    /// <summary>Lists the main pack on the inventory page (where the loot lands); true once it is listed.</summary>
    private static bool MainPackListed(IGameSessionView session, GamePanels panels)
    {
        if (panels.ControllersOf(HeroPanels.Inventory).OfType<GameInventoryPage>().FirstOrDefault() is not { } page)
        {
            return false;
        }

        var self = session.State.Self;
        var owner = self.Inventory.Owner != 0 ? self.Inventory.Owner : self.Guid;
        if (page.OpenContainer != owner)
        {
            page.ShowContents(owner);
        }

        return page.OpenContainer == owner;
    }
}
