namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// How the game's death leg ends its verdict (M3 3.2 dd, live fix; corrected by the review, R3): the measures are the scenario's own (<see cref="DeathScenario.Outcome"/>), frozen in the world when the scenario concluded
/// or failed (<see cref="DeathScenario.FreezeMeasures"/>), never a late re-measure by the runner: the leg's log off runs <c>GameStateReducer.LeaveWorld</c>, which clears the object table before the runner's finish, so a
/// re-measure there finds no lifestone object and reads <c>respawn=elsewhere</c> (the table does not forget the lifestone while the player is in the world). The runner's own failure (a limit, a log off that
/// failed) is added to the scenario's outcome here.
/// </summary>
public static class DeathLegFinish
{
    /// <summary>The scenario's outcome with the runner's own failure added; unchanged when there is none or the scenario already carries it.</summary>
    /// <param name="scenarioOutcome">The scenario's outcome.</param>
    /// <param name="runnerFailure">The runner's failure (a limit, an option error, a failed log off), or null.</param>
    public static DeathLegOutcome WithRunnerFailure(DeathLegOutcome scenarioOutcome, string? runnerFailure)
    {
        ArgumentNullException.ThrowIfNull(scenarioOutcome);
        if (string.IsNullOrEmpty(runnerFailure))
        {
            return scenarioOutcome;
        }

        var problem = "the death loop failed: " + runnerFailure;
        foreach (var existing in scenarioOutcome.Problems)
        {
            if (string.Equals(existing, problem, StringComparison.Ordinal))
            {
                return scenarioOutcome;
            }
        }

        var line = scenarioOutcome.Line.Replace(" ok=true ", " ok=false ", StringComparison.Ordinal);
        return new DeathLegOutcome(false, line, [problem, .. scenarioOutcome.Problems]);
    }
}
