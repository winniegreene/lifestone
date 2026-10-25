using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>The headless limit of a death-and-regear run, which the headless options tests pin against the host's own formula (the loopback run it served is retired into the mage journey's death station, M4 P5b J6).</summary>
internal static class DeathLive
{
    /// <summary>The headless limit beyond <c>--death-seconds</c> and <c>--death-fight-seconds</c>: the gear, the bind, a suicide, the respawn, the regear and the log off (the headless host's <c>DeathQuitAfterOverheadSeconds</c>, 900).</summary>
    internal const double OverheadSeconds = 900;

    /// <summary>
    /// M4 3.1 d2: the seconds the stages after the respawn may take besides the fight's own <c>--death-fight-seconds</c>, as the game's leg budgets them (<c>DeathLegRunner</c>: the decoy top-up 30, the rebuff 240, the teleport back to the den 30, the walk to the tuskers 60,
    /// the pre-trip heal 120, the corpse trip 90 and the return 60 = 630). The headless run's limit is <see cref="QuitAfterSecondsFor"/>.
    /// </summary>
    internal const double NewStagesSeconds = 630;

    /// <summary>M4 P4 g3fix: the most the fight stage waits for mana before the den (<see cref="DeathOptions.FightManaWaitSeconds"/>).</summary>
    internal const double FightManaWaitSeconds = 600;

    /// <summary>The headless run's limit for these options: the older overhead, the new stages, the fight's seconds and the wait among the tuskers (a ceiling: the real run takes about a third of it).</summary>
    /// <param name="options">The death loop's options.</param>
    internal static double QuitAfterSecondsFor(DeathOptions options) => OverheadSeconds + NewStagesSeconds + options.FightSeconds + options.Seconds + options.FightManaWaitSeconds;

}
