using System.Globalization;

namespace Lifestone.Client.Session.Creatures;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1, m6-plan.md D139): a creature kind's maximum health and Magic Defense as the bestiary holds them (<see cref="Bestiary.Find"/>), each with where it comes from and the
/// words a log line says it in. It is the shared part of slice E1b's read inside the fight (<c>FightScenario.BestiaryStatsOf</c>), so the fight, the travel leg's fight-back and the quest leg's kill say a kind's two numbers
/// in the same words and name the same two holes: a kind the bestiary holds with no number, and a weenie class it does not hold at all (<c>TODO(engine-bestiary-unknown-kind)</c>).
/// <para>
/// A number is here or it is null; this type never assumes one. What a caller takes where a number is null is the caller's own named fallback (the fight: the class's appraisal, else an unknown kind's assumption; a
/// leg: a large target and a Magic Defense of 0), and the caller says it once a kind (a fight's first read; a leg's <c>LegKindRead</c>). Reading allocates nothing; the words allocate and are asked once a kind.
/// </para>
/// </summary>
public readonly record struct KindNumbers
{
    private KindNumbers(uint wcid, CreatureCapabilities? kind) => (Wcid, Kind) = (wcid, kind);

    /// <summary>The weenie class asked.</summary>
    public uint Wcid { get; }

    /// <summary>The bestiary's record of it, or null when the bestiary does not hold the class (no creature weenie of the pinned world database's tables, and no ground profile's).</summary>
    public CreatureCapabilities? Kind { get; }

    /// <summary>Its maximum health when the bestiary holds one above 0 (a ground profile's <c>max_health</c> first, then the world database's table, as ACE computes it); null otherwise.</summary>
    public int? MaxHealth => Kind?.Vitals.MaxHealth is { } health and > 0 ? health : null;

    /// <summary>Its Magic Defense as ACE computes it (the world database's table), or null when the table does not hold the kind.</summary>
    public int? MagicDefence => Kind?.Defence.MagicDefence;

    /// <summary>True when the bestiary gives both numbers: nothing is assumed of this kind.</summary>
    public bool Complete => MaxHealth is not null && MagicDefence is not null;

    /// <summary>The two numbers of weenie class <paramref name="wcid"/> as the bestiary holds them. Allocates nothing after the bestiary's first load.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static KindNumbers Of(uint wcid) => new(wcid, Bestiary.Find(wcid));

    /// <summary>The two numbers of a record given by hand (a test's: a kind the bestiary holds with no number is none of the tracked tables' today).</summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="kind">Its record, or null for a class the bestiary does not hold.</param>
    internal static KindNumbers For(uint wcid, CreatureCapabilities? kind) => new(wcid, kind);

    /// <summary>The maximum health in words with its source, for a kind the bestiary holds: the number, or that there is none above 0 and what the caller takes instead (<c>TODO(m6-creature-health)</c>).</summary>
    /// <param name="whenNone">What the caller takes where the bestiary holds no maximum health above 0, in words.</param>
    public string HealthWords(string whenNone)
    {
        var source = Kind?.Vitals.MaxHealthSource ?? "the bestiary does not hold the kind";
        return MaxHealth is { } health
            ? string.Create(CultureInfo.InvariantCulture, $"maximum health {health} ({source})")
            : string.Create(CultureInfo.InvariantCulture, $"no maximum health above 0 ({source}; TODO(m6-creature-health): {whenNone})");
    }

    /// <summary>The Magic Defense in words, for a kind the bestiary holds: the number, or that the table does not hold the kind.</summary>
    public string DefenceWords =>
        MagicDefence is { } defence ? string.Create(CultureInfo.InvariantCulture, $"Magic Defense {defence}") : "no Magic Defense (the world database's creature table does not hold the kind)";

    /// <summary>What is said of a weenie class the bestiary does not hold, after the <c>TODO(engine-bestiary-unknown-kind)</c> token: the creature as it is seen, its class, and that it is no kind of the bestiary.</summary>
    /// <param name="seenAs">The name of the creature seen with this class.</param>
    public string NotHeldWords(string seenAs) =>
        string.Create(CultureInfo.InvariantCulture, $"{seenAs} (class {Wcid}) is no kind of the bestiary (no creature weenie of the pinned world database's table)");
}
