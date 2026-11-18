using System.Globalization;
using Lifestone.Client.Session.Creatures;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E1e (docs/automation-engine.md stage E1, m6-plan.md D139): a leg's read of a creature kind's maximum health and Magic Defense (<see cref="KindNumbers"/>, the bestiary's), for the travel leg's fight-back
/// and the quest leg's kill. Slice E1b made the fight's own read name what it assumes; outside the fight three reads still assumed without a word: a life cast took a creature with no maximum health as 10,000, and
/// the life cast and the fight-back's published target took an absent Magic Defense as 0. The numbers they take are unchanged; each is now said, once a kind a leg, as a warning in E1b's words:
/// <list type="bullet">
/// <item>a kind the bestiary holds with no number: <c>target facts: NAME (class N): no maximum health above 0 (...; TODO(m6-creature-health): ...), ..., from the bestiary</c>;</item>
/// <item>a weenie class the bestiary does not hold: <c>TODO(engine-bestiary-unknown-kind): NAME (class N) is no kind of the bestiary ...</c>.</item>
/// </list>
/// A kind with both numbers costs one dictionary read and no line. Class 0 (the object left the world before the read) is no kind and is not named.
/// </summary>
/// <param name="token">The leg's log token the line starts with (<c>fight-back</c>, <c>kill</c>).</param>
/// <param name="warn">Where the leg's warnings go.</param>
/// <param name="read">The read of a kind's two numbers; null for the bestiary's (<see cref="KindNumbers.Of"/>). A test gives its own.</param>
internal sealed class LegKindRead(string token, Action<string> warn, Func<uint, KindNumbers>? read = null)
{
    /// <summary>What a leg's life cast takes where the bestiary holds no maximum health for the target, in words.</summary>
    internal static readonly string HealthFallback =
        string.Create(CultureInfo.InvariantCulture, $"a life cast at it takes a large target, {FightScenario.LifeThroughUnknownHealth} health, so the spell's own take decides");

    /// <summary>What a leg takes where the bestiary holds no Magic Defense for the target, in words.</summary>
    internal const string DefenceFallback = "its Magic Defense is taken as 0, rolled as ACE rolls a Magic Defense of 0 (the 0.03 curve of her skill alone)";

    private readonly HashSet<uint> _named = [];

    /// <summary>The kinds named on this leg (a test reads it).</summary>
    internal IReadOnlyCollection<uint> Named => _named;

    /// <summary>The bestiary's two numbers of weenie class <paramref name="wcid"/>; the first time on this leg that one of them is missing, the warning that names what the leg takes instead.</summary>
    /// <param name="wcid">The creature's weenie class (0: its object is gone).</param>
    /// <param name="seenAs">The creature's name as the world shows it, for the line.</param>
    public KindNumbers Of(uint wcid, string seenAs)
    {
        var kind = read is null ? KindNumbers.Of(wcid) : read(wcid);
        if (kind.Complete || wcid == 0 || !_named.Add(wcid))
        {
            return kind;
        }

        warn(kind.Kind is { } held
            ? string.Create(CultureInfo.InvariantCulture, $"{token}: target facts: {held.Identity.Name} (class {wcid}): {kind.HealthWords(HealthFallback)}, {kind.DefenceWords}{(kind.MagicDefence is null ? " (" + DefenceFallback + ")" : string.Empty)}, from the bestiary; named once on this leg")
            : string.Create(CultureInfo.InvariantCulture, $"{token}: TODO(engine-bestiary-unknown-kind): {kind.NotHeldWords("'" + seenAs + "'")}: {HealthFallback}, and {DefenceFallback}; named once on this leg"));
        return kind;
    }
}
