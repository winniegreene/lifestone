using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 item 14, slice S3 (the build that leans on the buff bot): what the bot covers of the climber's own plan. The lines are those of the profile the run asks the bot for (<see cref="Profile"/>: the option's,
/// <c>--autopilot-buff-bot-word</c>, <see cref="BuffBotWire.Default"/> unless it names another; the owner, 2026-11-12), by both names a line goes by (the buff profile's, "Bludgeon Protection Self",
/// and the scrivener catalog's, the dat's root, "Bludgeoning Protection Self"); a line of the bot's that is no line of her own profile (Run and Jump: "Sprint Self", "Jumping Mastery Self"; the six other protections of <c>all</c>) goes by its one name, and since her plan and the catalog
/// hold no such line it adds nothing to buy, keep or read. While the cover is on for a session (<see cref="On"/>: the autopilot sets it while its stop at the bot applies to the run and the bot has not failed without ever
/// answering), the spell plan calls no trip for a covered line (<see cref="ScrivenerLearnPlan.Needs"/>), the keep rule keeps no looted scroll of one (<see cref="ScrollKeepRule"/>), and the experience push counts the bot's level VII lines in the
/// bootstrap's place (<see cref="BuffReach.Push"/>). M6 14 own buffs (the owner, 2026-11-17: she relies on her self-buffs, she does not need the bot): the cover is no reason to skip her own line: the every-spell rule buys each at the
/// highest level her own bootstrap casts, her bootstrap right after the gates (<see cref="EverySpellPlan"/>, <see cref="EverySpellTier.Bootstrap"/>); the bot stays a convenience whose stronger casts still stand over hers. Off (the default:
/// a plain journey, the den job, the option off, another town's home): the plan of before, unchanged.
/// </summary>
/// <remarks>
/// The plans take the cover as the profile that covers, or null for none (<see cref="Covering"/>): a plan asked with the tuskers' profile leaves her a line that only <c>all</c> holds, and the other way round.
/// </remarks>
public sealed class BuffBotCover
{
    private static readonly ConditionalWeakTable<GameState, BuffBotCover> PerSession = new();
    private static readonly ConcurrentDictionary<BuffBotProfile, IReadOnlySet<string>> ByProfile = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _noted = new(StringComparer.Ordinal);
    private bool _set;

    /// <summary>
    /// What a level VII attribute or mastery line adds (the spell table's rows, <see cref="BuffStatMods.BySpell"/>: 2067, 2091 and 2249 read 40; <c>BuffBotCoverTests</c> pins it): the bot's Focus and Willpower lines and each mastery.
    /// </summary>
    public const int Boost = 40;

    /// <summary>Every name of a line <paramref name="profile"/> casts: the buff profile's and the scrivener catalog's (the bot's own name alone for a line her profile does not hold). Made once a profile.</summary>
    /// <param name="profile">The bot's profile.</param>
    public static IReadOnlySet<string> LinesOf(BuffBotProfile profile) => ByProfile.GetOrAdd(
        profile ?? throw new ArgumentNullException(nameof(profile)),
        static p => p.Lines.SelectMany(l => new[] { l.Line, BuffProfiles.Climber.LineNamed(l.Line)?.DatRoot ?? l.Line }).ToFrozenSet(StringComparer.Ordinal));

    /// <summary>The cover of the session behind <paramref name="state"/>.</summary>
    /// <param name="state">The session's read model.</param>
    public static BuffBotCover For(GameState state) => PerSession.GetOrCreateValue(state ?? throw new ArgumentNullException(nameof(state)));

    /// <summary>True when <paramref name="profile"/> casts the line named <paramref name="line"/> (either name).</summary>
    /// <param name="profile">The bot's profile.</param>
    /// <param name="line">A buff profile's or a scrivener catalog's line name.</param>
    public static bool Covers(BuffBotProfile profile, string line) => LinesOf(profile).Contains(line);

    /// <summary>What a plan says of a covered line it leaves alone.</summary>
    /// <param name="profile">The bot's profile that covers it.</param>
    /// <param name="line">The line.</param>
    public static string Note(BuffBotProfile profile, string line) =>
        $"{line}: the buff bot casts it at level VII ({BuffBotWire.Name}, the profile '{(profile ?? throw new ArgumentNullException(nameof(profile))).Key}'): the spell plan calls no trip for it while the bot's stop applies; her own level is the every-spell rule's, {EverySpellPlan.CoveredNote}";

    /// <summary>True while the bot covers its lines for this session's run.</summary>
    public bool On { get; private set; }

    /// <summary>The profile whose lines the cover is, on or off: the one the run asks the bot for, as the autopilot last set it (<see cref="BuffBotWire.Default"/> until it does).</summary>
    public BuffBotProfile Profile { get; private set; } = BuffBotWire.Default;

    /// <summary>What the plans take: the profile that covers her lines now, or null while the cover is off.</summary>
    public BuffBotProfile? Covering => On ? Profile : null;

    /// <summary>Why the cover is on or off, as the autopilot last said it.</summary>
    public string Why { get; private set; } = "no autopilot has set it: her own plan, whole";

    /// <summary>Sets the cover; true when it changed (or was never set), so the caller logs the change once.</summary>
    /// <param name="on">Whether the bot covers its lines now.</param>
    /// <param name="why">Why, for the log.</param>
    /// <param name="profile">The profile the run asks the bot for; null: <see cref="BuffBotWire.Default"/>, the option's default.</param>
    public bool Set(bool on, string why, BuffBotProfile? profile = null)
    {
        profile ??= BuffBotWire.Default;
        if (_set && on == On && ReferenceEquals(profile, Profile))
        {
            return false;
        }

        (_set, On, Why, Profile) = (true, on, why, profile);
        _noted.Clear();
        return true;
    }

    /// <summary>True once the autopilot has set the cover (<see cref="Set"/>).</summary>
    public bool IsSet => _set;

    /// <summary>True the first time <paramref name="what"/> is named since the cover last changed: a covered line is logged once, not once a trip.</summary>
    /// <param name="what">The line (or any name a caller logs once).</param>
    public bool FirstNote(string what) => _noted.Add(what);
}
