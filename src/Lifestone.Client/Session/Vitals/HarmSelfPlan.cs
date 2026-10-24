using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// One level of Harm Self (M4 P5a lane C3): its spell id and the two numbers of ACE's world data that fix the health it takes. ACE rolls the harm uniformly between <c>Boost</c> and <c>Boost + BoostVariance</c>
/// (<c>WorldObject_Magic.cs:485-491</c> at ACE 95ef426d: <c>minBoostValue = Math.Min(spell.Boost, spell.MaxBoost)</c>, <c>maxBoostValue = Math.Max(...)</c>, <c>tryBoost = ThreadSafeRandom.Next(min, max)</c>, with
/// <c>MaxBoost = Boost + BoostVariance</c>, <c>SpellProperties.cs:393</c>), then scales it by the target's drain resistance and clamps the vital at 0 (<c>Creature_Vitals.cs:50-55</c>).
/// </summary>
/// <param name="Level">1 to 7 (Harm Self I to VII).</param>
/// <param name="SpellId">The spell's id in the SpellTable and in ACE's <c>spell</c> table.</param>
/// <param name="Boost">The row's <c>boost</c> (negative: health lost).</param>
/// <param name="BoostVariance">The row's <c>boost_Variance</c> (negative too).</param>
public readonly record struct HarmLevel(int Level, uint SpellId, int Boost, int BoostVariance)
{
    /// <summary>The most health one cast can take: the larger of the two roll ends in size.</summary>
    public int MaxHarm => Math.Max(Math.Abs(Boost), Math.Abs(Boost + BoostVariance));

    /// <summary>The least health one cast can take.</summary>
    public int MinHarm => Math.Min(Math.Abs(Boost), Math.Abs(Boost + BoostVariance));

    /// <summary>The mean harm of the uniform roll, in health points.</summary>
    public double MeanHarm => (MinHarm + MaxHarm) / 2.0;
}

/// <summary>Why <see cref="HarmSelfPlan.Choose"/> picked no level.</summary>
public enum HarmStop
{
    /// <summary>A level was chosen.</summary>
    None,

    /// <summary>The health is at or under the target: no cast is due.</summary>
    AtTarget,

    /// <summary>The health exceeds the target but no castable level keeps the margin: a cast could reach 0 or come within the margin of it.</summary>
    Margin,

    /// <summary>No level is castable (none known, none reaching the chance, or the mana pays none).</summary>
    NoSpell,
}

/// <summary>What <see cref="HarmSelfPlan.Choose"/> decided: a level to cast, or why none.</summary>
/// <param name="Level">The level to cast, or null.</param>
/// <param name="Stop">Why there is none (<see cref="HarmStop.None"/> with a level).</param>
public readonly record struct HarmChoice(HarmLevel? Level, HarmStop Stop);

/// <summary>
/// The harm step of the harm-heal station (M4 P5a lane C3, design section 3.3): how a character lowers its own health with Harm Self without ever risking its death. <b>Harm Self can kill</b>: ACE clamps the vital at 0
/// and a self-cast that reaches 0 kills the caster (<c>Creature_Vitals.cs:50-55</c>, <c>Player_Vitals.cs:130-139</c>, <c>WorldObject_Magic.cs:600</c>, <c>:678-691</c> at ACE 95ef426d; the cast gate at <c>:270-274</c> refuses only a dead
/// target). So a cast is allowed only while the health exceeds the level's <b>maximum</b> harm plus <see cref="MarginPoints"/>, and among the allowed levels the plan picks the one whose mean harm brings the health closest to the target.
/// </summary>
/// <remarks>
/// The boost values are ACE's world data (<c>ace_world.spell</c>, ACE-World-Database v0.9.296, the database the bundled backend loads) read for this slice from the pinned world download (the database was down; the table
/// is the same data): Harm Self I-VII <c>boost</c> / <c>boost_Variance</c> are -4/-2, -7/-5, -10/-8, -16/-14, -23/-22, -31/-29, -40/-35, every row's <c>damage_Type</c> 128 (health). The client's SpellTable (the dat) carries the power and the cost
/// but not these numbers: they exist only in the server's world data, hence the pin and the default-tier test of the constants.
/// <para><c>TODO(m4-harm-heal-resist)</c>: the roll is scaled by the target's drain resistance (<c>GetResistanceMod</c>), 1.0 without an enchantment; a character with one would take more than <see cref="HarmLevel.MaxHarm"/> and the margin covers only a small excess. The script that casts the plan logs it where it bites (<see cref="HarmHealScript"/>: a WARN once per leg when a landed harm took more than its level's maximum, and <c>resist=</c> in the verdict).</para>
/// </remarks>
public static class HarmSelfPlan
{
    /// <summary>
    /// The health points that must remain above a level's maximum harm for a cast to be allowed. Not retail: Lifestone's own. It covers the vital update lagging a cast by a frame, the drain resistance scaling the roll
    /// (<c>GetResistanceMod</c>, <c>WorldObject_Magic.cs:491</c>; 1.0 without an enchantment on the character) and a regeneration tick; with it, the health after any one cast stays above this many points.
    /// </summary>
    public const int MarginPoints = 10;

    /// <summary>The target health share of the station: half of the maximum health (design section 3.3).</summary>
    public const double DefaultTargetShare = 0.5;

    /// <summary>Harm Self I to VII with ACE's world data (see the class remarks).</summary>
    public static IReadOnlyList<HarmLevel> Levels { get; } =
    [
        new HarmLevel(1, 8, -4, -2),
        new HarmLevel(2, 1167, -7, -5),
        new HarmLevel(3, 1168, -10, -8),
        new HarmLevel(4, 1169, -16, -14),
        new HarmLevel(5, 1170, -23, -22),
        new HarmLevel(6, 1171, -31, -29),
        new HarmLevel(7, 2071, -40, -35),
    ];

    /// <summary>The level whose spell id is <paramref name="spellId"/>, or null when it is not a Harm Self.</summary>
    /// <param name="spellId">The spell id.</param>
    public static HarmLevel? For(uint spellId)
    {
        foreach (var level in Levels)
        {
            if (level.SpellId == spellId)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>The least net drop, as a share of a level's mean harm, that keeps the level among the plan's first picks when the regeneration between harms is measured (M4 P5a fix r4). Not retail: Lifestone's own.</summary>
    public const double MinNetShare = 0.5;

    /// <summary>The penalty that puts a weak level (net drop under <see cref="MinNetShare"/> of its mean) behind every level that is not weak.</summary>
    private const double Weak = 1_000_000;

    /// <summary>True when one cast of <paramref name="level"/> may be sent at <paramref name="health"/>: the health exceeds the level's maximum harm plus <paramref name="margin"/>.</summary>
    /// <param name="health">The current health points.</param>
    /// <param name="level">The level.</param>
    /// <param name="margin">The margin in points (default <see cref="MarginPoints"/>).</param>
    public static bool Allowed(uint health, in HarmLevel level, int margin = MarginPoints) =>
        (long)health > (long)level.MaxHarm + Math.Max(1, margin);

    /// <summary>
    /// Chooses the next cast. Nothing is chosen at or under the target (<see cref="HarmStop.AtTarget"/>); otherwise, of the <paramref name="castable"/> levels that <see cref="Allowed"/> keeps clear of 0, the one whose mean harm leaves the health
    /// closest to the target (a tie goes to the lower level). No level is ever returned that could reach 0.
    /// </summary>
    /// <param name="health">The current health points.</param>
    /// <param name="maxHealth">The maximum health points (0: unknown, nothing is chosen).</param>
    /// <param name="targetShare">The target health share, 0 to 1.</param>
    /// <param name="castable">The spell ids the character can cast now (known, at the chance, paid by the mana).</param>
    /// <param name="margin">The margin in points.</param>
    /// <param name="rise">
    /// The health the character regained between its last two harms (M4 P5a fix r4; 0 when none is measured yet): each level's mean harm counts less by it, so the plan picks the level whose <b>net</b> drop brings the health closest
    /// to the target, and a level whose net drop is under half its mean (<see cref="MinNetShare"/>) is not picked while a level with a better net drop is allowed. Round 4: the Brawler (231 health, Epic regeneration gear)
    /// regained 2 to 4 points between harms and Harm Self I (mean 5) took 18 casts to move 49 to 54%. The margin rule (<see cref="Allowed"/>) is unchanged: it reads the maximum harm alone, so no cast can reach 0.
    /// </param>
    public static HarmChoice Choose(uint health, uint maxHealth, double targetShare, IReadOnlyCollection<uint> castable, int margin = MarginPoints, double rise = 0)
    {
        ArgumentNullException.ThrowIfNull(castable);
        if (maxHealth == 0)
        {
            return new HarmChoice(null, HarmStop.NoSpell);
        }

        var target = Math.Clamp(targetShare, 0.0, 1.0) * maxHealth;
        if (health <= target)
        {
            return new HarmChoice(null, HarmStop.AtTarget);
        }

        HarmLevel? best = null;
        var bestMiss = double.MaxValue;
        var any = false;
        foreach (var level in Levels)
        {
            if (!castable.Contains(level.SpellId))
            {
                continue;
            }

            any = true;
            if (!Allowed(health, in level, margin))
            {
                continue;
            }

            var net = level.MeanHarm - Math.Max(0, rise);
            var miss = Math.Abs(health - net - target) + (net < MinNetShare * level.MeanHarm ? Weak : 0);
            if (miss < bestMiss)
            {
                bestMiss = miss;
                best = level;
            }
        }

        return best is { } chosen ? new HarmChoice(chosen, HarmStop.None) : new HarmChoice(null, any ? HarmStop.Margin : HarmStop.NoSpell);
    }

    /// <summary>The level's name as a log line says it, e.g. <c>Harm Self IV (1169, harm 16 to 30)</c>.</summary>
    /// <param name="level">The level.</param>
    public static string Describe(in HarmLevel level) =>
        string.Create(CultureInfo.InvariantCulture, $"Harm Self {Numeral(level.Level)} ({level.SpellId}, harm {level.MinHarm} to {level.MaxHarm})");

    private static string Numeral(int level) => level switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        _ => "VII",
    };
}
