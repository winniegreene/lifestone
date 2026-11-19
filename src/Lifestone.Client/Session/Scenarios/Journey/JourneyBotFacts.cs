namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 14 and the onscreen start: the buff bot's two journey facts every host sets the same way, so the Pathwarden station's stop at the bot (<c>PathwardenStation</c>, before the chest) reads the autopilot's own option in the
/// headless journey (<see cref="HeadlessJourneyScenario"/>) and in the game window's (<c>JourneyLegRunner.BuildParts</c>, which the climber start and the autopilot share). Before this the window's journey left both unset, so the stop
/// read off there while the headless journey read it on.
/// </summary>
public static class JourneyBotFacts
{
    /// <summary>
    /// Sets <see cref="JourneyFacts.BuffBotOn"/> (<see cref="HeadlessJourneyScenario.BotStopOf"/>: the plan's Pathwarden station and <c>--autopilot-buff-bot</c>, on by default; null for any other plan) and
    /// <see cref="JourneyFacts.BuffBotProfile"/> (<see cref="HeadlessJourneyScenario.BotProfileOf"/>: <c>--autopilot-buff-bot-word</c>, <c>all</c> by default). The autopilot's own scenario sets both again from its options when it runs.
    /// </summary>
    /// <param name="facts">The journey's facts.</param>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="arguments">The host's command line.</param>
    public static void Apply(JourneyFacts facts, JourneyPlan plan, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(facts);
        facts.BuffBotOn = HeadlessJourneyScenario.BotStopOf(plan, arguments);
        facts.BuffBotProfile = HeadlessJourneyScenario.BotProfileOf(arguments);
    }
}
