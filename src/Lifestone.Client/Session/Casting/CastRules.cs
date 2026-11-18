using System.Globalization;

namespace Lifestone.Client.Session.Casting;

/// <summary>
/// The retry rules of one <see cref="CastAttempt"/> (M5 item 1 lane K1, the P5b design's §2.2): an immutable value, one per caller kind, made from a preset and the named forms
/// (<see cref="WithFizzleRule"/>, <see cref="WithManaTries"/>, <see cref="WithBusyRetries"/>, <see cref="WithResistRetries"/>). Every bound is Lifestone's own number, not retail's: retail's client
/// sends a cast and the server decides (<c>gmSpellcastingUI::Cast</c> 11.6096 0x004C6C40, <c>ClientMagicSystem::CastSpell</c> 0x00568DE0); it never presses again by itself.
/// </summary>
/// <remarks>
/// <para><b>The presets reproduce today's callers exactly</b> (checked against the code at master 6857174d; where the design's §1.2 and §2.3 tables differ from the code, the code wins):
/// <list type="bullet">
/// <item><see cref="BuffLine"/>: a buff line of <c>BuffCycle</c> (<c>CastOutcome</c>): no same-level retry, one level down once, then a named miss; the mana is checked again before the level-down
/// retry (the cycle goes back to its mana phase), which is <see cref="CeilingBeforeRetry"/>.</item>
/// <item><see cref="Once"/>: the upgrade pass, <c>GearScenario</c>'s check cast and the heal station's checks: no retry.</item>
/// <item><see cref="Utility(int)"/>: the same spell, <b>at most n casts</b> while they fizzle (the mana loop's <c>TransferTries</c> 8 fails on the 8th fizzle in a row; the vitals keeper sets a line aside on its
/// 3rd; the pre-trip heal's <c>PreTripMaxHeals</c> 4). The design's table reads "at most n" for both Utility and Travel; the code counts casts here and retries in Travel.</item>
/// <item><see cref="Bolt"/>: <c>FightScenario</c>'s Flame Bolt: one level down, once per scenario (the caller passes <see cref="CastRequest.LevelsDownSpent"/> 1 once it dropped), to the next lower level the
/// request's <see cref="CastRequest.Usable"/> accepts (the fight skips the levels the spellbook lacks).</item>
/// <item><see cref="Travel(int)"/>: <c>RecallHome</c> (5) and the journey's script legs (3): <b>n same-level retries</b> (n + 1 casts), sent at once with no mana check between them.</item>
/// <item><see cref="Timed(double)"/>: <c>HarmHealScript</c>'s harm and heal casts (60): fizzles retried without a count inside the time cap, the mana checked again before each retry (<see cref="CeilingBeforeRetry"/>,
/// M5 1 fix A, 1-R1: a retry the mana cannot pay ends <see cref="CastEnd.ManaShort"/> with nothing sent, and the script goes back to its top-up).</item>
/// </list>
/// No caller retried a busy refusal (0x001D) of a cast before M5 item 1; the owner's DoD 2 (busy waits and resends) and the review's 1-R6 adopt the design's busy rule (<see cref="WithBusyRetries"/>: wait
/// <see cref="DefaultBusyWaitSeconds"/> and send again, <see cref="DefaultBusyRetries"/> times, the vitals keeper's own numbers for its kits and potions) on every preset, each wait and resend counted
/// (<see cref="CastReport.Busy"/>, <c>busy=</c>); the preset's <see cref="Name"/> is unchanged (<see cref="WithBusyRetries"/> on a preset adds "+busy(n)"). No caller retries a resist (none casts a debuff on another
/// target), so <see cref="MaxResists"/> is 0 and the design's rule is <see cref="WithResistRetries"/>.</para>
/// <para><b>The owner's fizzle rule</b> (M5 D14, the design's Q2): <see cref="WithFizzleRule"/>, and the presets <see cref="BuffLineD14"/> and <see cref="BoltD14"/>: the same level twice more, then one
/// level down once, then a named miss.</para>
/// <para><b>Mana.</b> <see cref="MaxManaTries"/> 0 (every preset) ends an attempt <see cref="CastEnd.ManaShort"/> on the first 0x0401, today's behaviour; <see cref="WithManaTries"/> is D15's
/// option and <see cref="ManaDance"/> D27's dance. A refused cast charges nothing (ACE <c>Player_Magic.cs</c> 566-585 @95ef426d), so each press is a fresh Mana Conversion roll.</para>
/// <para>Make the rules once (a constructor or a static field): <see cref="Utility(int)"/>, <see cref="Travel(int)"/>, <see cref="Timed(double)"/> and the forms build a name string.</para>
/// </remarks>
public sealed record CastRules
{
    /// <summary>The Action API's cast timeout (<c>ActionPolicy.CastSeconds</c>, 10 s): the longest one try can last before it ends <see cref="CastEnd.Timeout"/>.</summary>
    public const double TryTimeoutSeconds = 10;

    /// <summary>
    /// The default time cap of an attempt (Lifestone's own): two minutes, longer than any preset's worst case (Utility(8): 8 tries of at most <see cref="TryTimeoutSeconds"/>; Travel(5): 6), so a preset's
    /// cap never binds before its counts do; <see cref="Timed(double)"/> and <see cref="ManaDance"/> set their own.
    /// </summary>
    public const double DefaultMaxSeconds = 120;

    /// <summary>The spacing of the Mana Conversion tries (Lifestone's own number: a person's click rate; M5 D15, the design's §3.3 <c>--cast-mana-try-spacing-s</c>).</summary>
    public const double DefaultManaTrySpacingSeconds = 0.25;

    /// <summary>The Mana Conversion tries of one need by default once K5 turns them on (M5 D15: at most 8 tries a need; Lifestone's own number).</summary>
    public const int DefaultManaTries = 8;

    /// <summary>The busy refusals the design's rule waits out (the vitals keeper's <c>MaxBusyRetries</c>, Lifestone's own).</summary>
    public const int DefaultBusyRetries = 3;

    /// <summary>The wait after a busy refusal in the design's rule (the keeper's <c>BusyRetrySeconds</c>, Lifestone's own).</summary>
    public const double DefaultBusyWaitSeconds = 1;

    /// <summary>The same-level retries of the owner's fizzle rule (M5 D14: the same level twice more).</summary>
    public const int FizzleRuleSameLevel = 2;

    /// <summary>A bound that never binds (the count is not the rule's limit; <see cref="Timed(double)"/>'s fizzles).</summary>
    public const int Unbounded = int.MaxValue;

    /// <summary>A short name for the log and the report (<c>BuffLine</c>, <c>Utility(8)</c>, <c>BuffLine+D14</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Same-level retries after a fizzle (0x0402), before any level down; <see cref="Unbounded"/> for none.</summary>
    public int MaxFizzles { get; init; }

    /// <summary>Levels stepped down, one at a time, once the same-level retries are spent (0 or 1 today); a fizzle with none left ends <see cref="CastEnd.Fizzled"/>.</summary>
    public int LevelsDown { get; init; }

    /// <summary>
    /// The 0x0401 refusals the attempt absorbs: a refusal while fewer than this many have come is followed, after <see cref="ManaTrySpacingSeconds"/>, by the same cast again; the refusal that reaches it (and
    /// with 0 the first) ends <see cref="CastEnd.ManaShort"/>. So <c>WithManaTries(8)</c> sends at most 8 casts that are refused, and 0 and 1 both end on the first refusal.
    /// </summary>
    public int MaxManaTries { get; init; }

    /// <summary>The wait after a 0x0401 answer before the next try (<see cref="DefaultManaTrySpacingSeconds"/>).</summary>
    public double ManaTrySpacingSeconds { get; init; } = DefaultManaTrySpacingSeconds;

    /// <summary>The wait after a fizzle answer before the retry: 0 (the Action API holds it in the Magic lane until ACE's UseDone), the dance's spacing in <see cref="ManaDance"/>.</summary>
    public double FizzleSpacingSeconds { get; init; }

    /// <summary>Busy refusals (0x001D) waited out and resent; the next ends <see cref="CastEnd.Busy"/>. Every preset: <see cref="DefaultBusyRetries"/> (M5 1 fix A, 1-R6).</summary>
    public int MaxBusy { get; init; } = DefaultBusyRetries;

    /// <summary>The wait after a busy refusal before the resend.</summary>
    public double BusyWaitSeconds { get; init; } = DefaultBusyWaitSeconds;

    /// <summary>Resists ("&lt;name&gt; resists your spell", ACE <c>WorldObject_Magic.cs</c> 192, seen by the proof) retried at the same level; the next ends <see cref="CastEnd.Resisted"/>.</summary>
    public int MaxResists { get; init; }

    /// <summary>The tries of an attempt in all, whatever their answers (0: no bound beyond the others; <see cref="ManaDance"/>'s presses).</summary>
    public int MaxTries { get; init; }

    /// <summary>The attempt's time cap: no try is sent after it (a try in flight finishes first: it cannot be unsent), and the attempt ends <see cref="CastEnd.TimeCap"/>.</summary>
    public double MaxSeconds { get; init; } = DefaultMaxSeconds;

    /// <summary>
    /// Before a fizzle's retry (the same level or the level below; never before the first try, a mana try or a busy resend), the mana held is read: under the retry level's ceiling, with no Mana
    /// Conversion tries left to spend on it, the attempt ends <see cref="CastEnd.ManaShort"/> with nothing sent and <see cref="CastReport.NextLevelIndex"/> naming the level to resume at
    /// (<see cref="CastRequest.Resume"/>). Today's callers do this by going back to their mana phase; the script legs and the recall send the retry at once.
    /// </summary>
    public bool CeilingBeforeRetry { get; init; }

    /// <summary><c>BuffCycle</c>'s buff line: no same-level retry, one level down once, the mana checked again before it.</summary>
    public static CastRules BuffLine { get; } = new() { Name = "BuffLine", LevelsDown = 1, CeilingBeforeRetry = true };

    /// <summary>No retry of any kind: the upgrade pass, <c>GearScenario</c>'s check ("a check never retries silently"), the heal station's checks.</summary>
    public static CastRules Once { get; } = new() { Name = "Once" };

    /// <summary><c>FightScenario</c>'s bolt: one level down (once per scenario: <see cref="CastRequest.LevelsDownSpent"/>), the mana checked again before it.</summary>
    public static CastRules Bolt { get; } = new() { Name = "Bolt", LevelsDown = 1, CeilingBeforeRetry = true };

    /// <summary><see cref="BuffLine"/> under the owner's fizzle rule (M5 D14).</summary>
    public static CastRules BuffLineD14 { get; } = BuffLine.WithFizzleRule();

    /// <summary><see cref="Bolt"/> under the owner's fizzle rule (M5 D14).</summary>
    public static CastRules BoltD14 { get; } = Bolt.WithFizzleRule();

    /// <summary>The same spell, at most <paramref name="casts"/> casts while they fizzle (the mana loop 8, the vitals keeper 3 in a row, the pre-trip heal 4); the mana checked again before each retry.</summary>
    /// <param name="casts">The casts in all (at least 1).</param>
    public static CastRules Utility(int casts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(casts, 1);
        return new() { Name = string.Create(CultureInfo.InvariantCulture, $"Utility({casts})"), MaxFizzles = casts - 1, CeilingBeforeRetry = true };
    }

    /// <summary>The same spell again after a fizzle, <paramref name="retries"/> times (<c>RecallHome.MaxFizzles</c> 5, the script legs' 3), sent at once.</summary>
    /// <param name="retries">The same-level retries (at least 0).</param>
    public static CastRules Travel(int retries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        return new() { Name = string.Create(CultureInfo.InvariantCulture, $"Travel({retries})"), MaxFizzles = retries };
    }

    /// <summary>Fizzles retried with no count inside <paramref name="seconds"/> (<c>HarmHealScript.HarmCapSeconds</c> 60); the mana checked again before each retry.</summary>
    /// <param name="seconds">The time cap.</param>
    public static CastRules Timed(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        return new() { Name = string.Create(CultureInfo.InvariantCulture, $"Timed({seconds:0.##})"), MaxFizzles = Unbounded, MaxSeconds = seconds, CeilingBeforeRetry = true };
    }

    /// <summary>
    /// The stamina-to-mana dance (M5 D27): the same spell pressed again <paramref name="spacingSeconds"/> after each 0x0401 or fizzle answer until it lands, at most <paramref name="presses"/> presses in all,
    /// every press counted. The time cap is the presses' worst case (each a try of at most <see cref="TryTimeoutSeconds"/> plus the spacing).
    /// </summary>
    /// <param name="presses">The presses in all (at least 1).</param>
    /// <param name="spacingSeconds">The wait after each refusal or fizzle (at least 0).</param>
    public static CastRules ManaDance(int presses, double spacingSeconds = DefaultManaTrySpacingSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(presses, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(spacingSeconds);
        return new()
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"ManaDance({presses},{spacingSeconds:0.##})"),
            MaxTries = presses,
            MaxManaTries = presses,
            MaxFizzles = Unbounded,
            ManaTrySpacingSeconds = spacingSeconds,
            FizzleSpacingSeconds = spacingSeconds,
            MaxSeconds = presses * (TryTimeoutSeconds + spacingSeconds),
        };
    }

    /// <summary>These rules under the fizzle rule of M5 D14: <see cref="FizzleRuleSameLevel"/> same-level retries, then one level down once, then a named miss.</summary>
    public CastRules WithFizzleRule() => this with { Name = Name + "+D14", MaxFizzles = FizzleRuleSameLevel, LevelsDown = 1 };

    /// <summary>These rules with the Mana Conversion tries (M5 D15, K5's option): a 0x0401 inside the tries waits <paramref name="spacingSeconds"/> after the answer and sends again; outside them it ends <see cref="CastEnd.ManaShort"/>.</summary>
    /// <param name="tries">The refusals absorbed (<see cref="MaxManaTries"/>; 0 restores today's ceiling rule).</param>
    /// <param name="spacingSeconds">The wait after a refusal.</param>
    public CastRules WithManaTries(int tries = DefaultManaTries, double spacingSeconds = DefaultManaTrySpacingSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tries);
        ArgumentOutOfRangeException.ThrowIfNegative(spacingSeconds);
        return this with { Name = string.Create(CultureInfo.InvariantCulture, $"{Name}+mana({tries})"), MaxManaTries = tries, ManaTrySpacingSeconds = spacingSeconds };
    }

    /// <summary>These rules with the design's busy rule (§2.2): a 0x001D waited out <paramref name="waitSeconds"/> and resent, <paramref name="retries"/> times.</summary>
    /// <param name="retries">The busy refusals waited out.</param>
    /// <param name="waitSeconds">The wait after each.</param>
    public CastRules WithBusyRetries(int retries = DefaultBusyRetries, double waitSeconds = DefaultBusyWaitSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        ArgumentOutOfRangeException.ThrowIfNegative(waitSeconds);
        return this with { Name = string.Create(CultureInfo.InvariantCulture, $"{Name}+busy({retries})"), MaxBusy = retries, BusyWaitSeconds = waitSeconds };
    }

    /// <summary>These rules with the design's resist rule (§2.2, debuffs only): a resist retried at the same level <paramref name="retries"/> times.</summary>
    /// <param name="retries">The resists retried.</param>
    public CastRules WithResistRetries(int retries = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retries);
        return this with { Name = string.Create(CultureInfo.InvariantCulture, $"{Name}+resist({retries})"), MaxResists = retries };
    }

    /// <summary>These rules with another time cap.</summary>
    /// <param name="seconds">The cap.</param>
    public CastRules WithMaxSeconds(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        return this with { MaxSeconds = seconds };
    }
}
