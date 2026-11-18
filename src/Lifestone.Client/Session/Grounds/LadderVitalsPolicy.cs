namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// M6 14 (<c>m6-vitals-policy</c>, the owner, 2026-11-13: the climber's mana casts by the numbers, experience an hour first): the lines of <see cref="Vitals.VitalsPolicy"/>, the ladder's <c>vitals_policy</c> section. Every other number the policy uses
/// is read from her character, the target or ACE's formulas; these are the ones that are measured or chosen, each with its reason in the ladder's <c>sources.vitals_policy</c>.
/// </summary>
/// <param name="On">The climber's mana loop goes by the policy (false: by <see cref="Vitals.ManaLoopRule.DecideBySum"/>, as before).</param>
/// <param name="BoltFirstChance">The chance that the next bolt kills the target at or over which no fill is cast before it (the floors excepted).</param>
/// <param name="HealthBurstFactor">How many times the fight's mean incoming damage the health floor reads when the recent read is lower: a pull brings more attackers than the mean.</param>
/// <param name="SeedOverheadSeconds">The seconds of a kill that are not casts (the walk to the next tusker, the loot, the target's arrival) before the fight has measured its own kills.</param>
/// <param name="SeedKills">The kills after which the fight's own seconds a kill, attacks and damage a second replace the seeds.</param>
/// <param name="CastSecondsSelf">The seconds of a self cast (Stamina to Mana Self, Heal Self) by level, I first: measured, send to proof.</param>
/// <param name="CastSecondsOther">The seconds of a cast on another creature (Drain Stamina Other, Drain Health Other) by level, I first: measured, the turn to face included.</param>
/// <param name="CastSecondsBolt">The seconds of a bolt by level, I first: measured, send to the projectile.</param>
/// <param name="PressWaitsForBest">True: a press waits until the cheapest level's whole give fits (experience an hour first: the mana runs down about one big press before it refills). False: the cheapest level whose whole give fits now goes
/// (the bars stay nearer full, for one to three percent of the hour).</param>
public sealed record LadderVitalsPolicy(bool On, double BoltFirstChance, double HealthBurstFactor, double SeedOverheadSeconds, int SeedKills, IReadOnlyList<double> CastSecondsSelf, IReadOnlyList<double> CastSecondsOther, IReadOnlyList<double> CastSecondsBolt, bool PressWaitsForBest = true)
{
    /// <summary>No policy: the section is absent (a ladder that does not name it keeps the sum rule).</summary>
    public static LadderVitalsPolicy Off { get; } = new(false, 0.5, 1, 0, 0, [], [], []);

    /// <summary>A self cast's seconds at <paramref name="level"/> (1 based; a level past the table reads its last row).</summary>
    /// <param name="level">The level.</param>
    public double SelfSeconds(int level) => Row(CastSecondsSelf, level, "cast_seconds_self");

    /// <summary>A cast's seconds on another creature at <paramref name="level"/>.</summary>
    /// <param name="level">The level.</param>
    public double OtherSeconds(int level) => Row(CastSecondsOther, level, "cast_seconds_other");

    /// <summary>A bolt's seconds at <paramref name="level"/>.</summary>
    /// <param name="level">The level.</param>
    public double BoltSeconds(int level) => Row(CastSecondsBolt, level, "cast_seconds_bolt");

    private static double Row(IReadOnlyList<double> table, int level, string name) =>
        table.Count == 0 ? throw new InvalidOperationException($"TODO(m6-vitals-policy-off): the ladder's vitals_policy.{name} is empty: the policy is off and must not be asked") : table[Math.Clamp(level, 1, table.Count) - 1];
}
