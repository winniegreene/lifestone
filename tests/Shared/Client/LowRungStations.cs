using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests;

/// <summary>
/// M6 7 P: the stations of the low-rung death case (DoD 13; <c>LowRungDeathLiveTests</c> case (a)), in one place so the live case and its offline proof (<c>LowRungDeathStationsTests</c>) run the same list.
/// The den-fight's need is <see cref="StationWhere.Den"/>, which the journey meets by "the den is reached by the den-travel station itself": a list with no den-travel station before a fight skipped it
/// (<c>skipped=precondition:where</c>, live round 1c). The climber's own order puts <see cref="AutopilotScenario.DenTravelSpec"/> (an outdoor ground: a walk from Holtburg to its first cell) before the fight, and again after the death, whose respawn is at Holtburg.
/// </summary>
public static class LowRungStations
{
    /// <summary>The case's stations: bind, buff, den-travel, a one-kill fight, the scripted death, den-travel, a one-kill fight.</summary>
    /// <param name="ground">The rung's ground.</param>
    public static IReadOnlyList<StationSpec> Outskirts(HuntingGround ground) =>
    [
        AutopilotScenario.BindSpec,
        AutopilotScenario.BuffSpec,
        AutopilotScenario.DenTravelSpec,
        ShortFight(ground),
        AutopilotScenario.DeathSpec,
        AutopilotScenario.DenTravelSpec,
        ShortFight(ground),
    ];

    /// <summary>A short fight on the rung's ground: one kill, the autopilot's own unattended shape (the mage holds the magic mode) cut to <c>kills=1</c>.</summary>
    /// <param name="ground">The rung's ground.</param>
    public static StationSpec ShortFight(HuntingGround ground) => new(
        "den-fight",
        StationKinds.DenFight,
        null,
        900,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [JourneyStations.GroundArgument] = ground.Key,
            [JourneyPlans.KillsArgument] = "1",
            [JourneyStations.UnattendedArgument] = "yes",
        });
}
