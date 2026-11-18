using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The pre-trip heal's constants and count (M4 3.1 d1 follow-up F1, D20; M5 3 d2 D51). The heal itself is the death handler's (<see cref="DeathHandler"/>, step <c>heal</c>): while the health share is under
/// <see cref="DeathOptions.RecoverMinHealthPercent"/> it casts Heal Self VII (<see cref="HealSelfVii"/>) at most <see cref="PreTripMaxHeals"/> times, a cast skipped for mana resumes when the mana regenerates
/// (<c>m4-death-trip-heal-resume</c>, closed in d1), then waits up to <see cref="PreTripRegenSeconds"/> s for regeneration. M4 2.2/3.1's own pre-trip heal stage and its order are removed.
/// </summary>
public sealed partial class DeathScenario
{
    /// <summary>Heal Self VII, the spell the pre-trip recovery casts (<see cref="ArenaProfile.FightExtras"/>; the den mage knows it; the death handler casts the highest Heal Self level the book knows and the pack pays for, and drops the heal when none: M6 6 FXD1, 6-D1).</summary>
    public const uint HealSelfVii = 2073;

    /// <summary>The most Heal Self casts of the pre-trip recovery.</summary>
    public const int PreTripMaxHeals = 4;

    /// <summary>The most seconds of session clock the recovery waits for the health to regenerate after its casts.</summary>
    public const double PreTripRegenSeconds = 60;

    /// <summary>The Heal Self casts the handler made (0 when no handler ran).</summary>
    public int PretripHeals => _handler?.Heals ?? 0;
}
