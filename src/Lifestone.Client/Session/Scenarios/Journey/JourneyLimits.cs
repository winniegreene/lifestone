using Lifestone.Client.Options;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The journey's time limits where the den fight's own limit raises them (M5 1 fix B, review 1-R4). The den-fight station runs a <see cref="FightScenario"/> whose failure limit is
/// <see cref="FightOptions.FailSeconds"/> (<c>--fight-seconds</c>, or with <c>--fight-minutes</c> the larger of the two); the station's limit (<see cref="JourneyScenario.StationLimitFactor"/> times its expected
/// seconds plus <see cref="JourneyScenario.StationLimitMarginSeconds"/>) and the two hosts' whole-run limits were sized from the plan alone, so a long fight (<c>--fight-minutes 30 --fight-kills 0</c>) ended
/// <c>timeout:&lt;stage&gt;</c> before its clean stop. Every limit is raised only when the options raise the fight over its default, so the default journeys' limits are unchanged.
/// </summary>
public static class JourneyLimits
{
    /// <summary>The margin on a raised fight limit, in seconds: the fight's own place, mode change and wind-down around its <see cref="FightOptions.FailSeconds"/> (Lifestone's own).</summary>
    public const double FightMarginSeconds = 300;

    /// <summary>The default limit of a station: its expected seconds times <see cref="JourneyScenario.StationLimitFactor"/> plus <see cref="JourneyScenario.StationLimitMarginSeconds"/>.</summary>
    /// <param name="spec">The station's spec.</param>
    public static double DefaultStationSeconds(StationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return (spec.ExpectedSeconds * JourneyScenario.StationLimitFactor) + JourneyScenario.StationLimitMarginSeconds;
    }

    /// <summary>
    /// The limit of the station <paramref name="spec"/>: <see cref="DefaultStationSeconds"/>, and for a den-fight station at least the fight's <see cref="FightOptions.FailSeconds"/> plus <see cref="FightMarginSeconds"/>
    /// when the options raise the fight over <see cref="FightOptions.DefaultSeconds"/> (the default fight's 600 s is under the default station limit, so nothing changes without the options).
    /// </summary>
    /// <param name="spec">The station's spec.</param>
    /// <param name="fight">The journey's fight options.</param>
    public static double StationSeconds(StationSpec spec, FightOptions fight)
    {
        ArgumentNullException.ThrowIfNull(fight);
        var limit = DefaultStationSeconds(spec);
        return spec.Kind == StationKinds.DenFight && fight.FailSeconds > FightOptions.DefaultSeconds ? Math.Max(limit, fight.FailSeconds + FightMarginSeconds) : limit;
    }

    /// <summary>
    /// The seconds a raised fight limit adds to a host's whole-run limit for <paramref name="plan"/>: for each den-fight station, the fight's <see cref="FightOptions.FailSeconds"/> over <see cref="FightOptions.DefaultSeconds"/>
    /// (the part of the default limit the plan's expected seconds and the host's margin already hold), plus the archer hunt's (<see cref="HuntExtraSeconds"/>); 0 for the default options and a plan with neither.
    /// </summary>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="fight">The journey's fight options.</param>
    public static double WholeExtraSeconds(JourneyPlan plan, FightOptions fight)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fight);
        var raised = Math.Max(0, fight.FailSeconds - FightOptions.DefaultSeconds);
        return (raised * plan.Stations.Count(s => s.Kind == StationKinds.DenFight)) + HuntExtraSeconds(plan);
    }

    /// <summary>
    /// The seconds the archer's forest hunt (<see cref="JourneyStations.ArcherHuntSeconds"/>, 1,200 s) runs over the hunt station's expected seconds (<see cref="JourneyPlans.ArcherHuntExpectedSeconds"/>, 400 s) for each hunt station of an archer plan: the whole-run limit sums the
    /// stations' expected seconds, so a hunt that finds nothing for its whole time would otherwise end by the host's limit before its own named reason (M5 item 2 k2). 0 for a plan with no archer hunt.
    /// </summary>
    /// <param name="plan">The journey's plan.</param>
    public static double HuntExtraSeconds(JourneyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Character == Fighter.FighterTemplate.ArcherName
            ? plan.Stations.Where(s => s.Kind == StationKinds.Hunt).Sum(s => Math.Max(0, JourneyStations.ArcherHuntSeconds - s.ExpectedSeconds))
            : 0;
    }

    /// <summary>The fight options of a host's command line (the parser the station factory uses; its errors are the factory's to report).</summary>
    /// <param name="arguments">The host's command-line arguments.</param>
    public static FightOptions FightOf(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return FightOptions.FromArguments(CommandLineArgument.ParseAll(arguments));
    }
}
