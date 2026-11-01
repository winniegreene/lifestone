using System.Globalization;
using System.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Casting;

/// <summary>How a <see cref="CastAttempt"/> stands: <see cref="Running"/>, or how it ended (the design's §2.2, one end per outcome).</summary>
public enum CastEnd : byte
{
    /// <summary>Not started, or a try is in flight or waiting its spacing.</summary>
    Running,

    /// <summary>Landed: the self enchantment or the projectile confirmed it, or a Sent cast the request's proof proved.</summary>
    Landed,

    /// <summary>Sent (UseDone 0 for a spell with no proof on the wire) and not proved: no proof was given, or it found none (<c>TODO(m3-cast-other-target-proof)</c> for this target).</summary>
    Unproven,

    /// <summary>Fizzled (0x0402) with the rules' retries and level down spent, or no level below.</summary>
    Fizzled,

    /// <summary>Not enough mana: a 0x0401 outside the Mana Conversion tries, or a retry the rules check against the ceiling (<see cref="CastRules.CeilingBeforeRetry"/>), or under the request's try floor.</summary>
    ManaShort,

    /// <summary>UseDone 0x0400: a component is missing (a kit problem, not luck; never retried).</summary>
    NoComponents,

    /// <summary>0x0550 (MissileOutOfRange, ACE <c>WeenieError.cs</c>:1135 @95ef426d): the caller walks or closes in. M5 7 D124: 0x042C is never out of range (<see cref="TargetDied"/>).</summary>
    OutOfRange,

    /// <summary>UseDone 0 that ended a self or projectile cast without its effect (the stance, an invalid target, a stronger spell already held).</summary>
    NoEffect,

    /// <summary>0x001D (busy) past the rules' busy retries.</summary>
    Busy,

    /// <summary>The proof saw "&lt;name&gt; resists your spell" past the rules' resist retries.</summary>
    Resisted,

    /// <summary>The Action API refused the cast at once (the stance guard, not in the world, no spell, a non-loopback server).</summary>
    Refused,

    /// <summary>The kill switch: a queued try was cancelled, or the next try was never sent; or the caller stopped the attempt.</summary>
    Cancelled,

    /// <summary>Dry run: the Action API logged what it would have sent.</summary>
    DryRun,

    /// <summary>The 10 s cast timeout. Never resent: the cast may have happened.</summary>
    Timeout,

    /// <summary>The attempt's <see cref="CastRules.MaxSeconds"/> passed before the next try.</summary>
    TimeCap,

    /// <summary>Any other server code (<see cref="CastReport.Code"/>).</summary>
    ServerError,

    /// <summary>The Action API's loud stub (<see cref="ActionOutcomeKind.NotImplemented"/>).</summary>
    NotImplemented,

    /// <summary>
    /// M5 7 F28, D124: 0x042C (TargetNotAcquired, ACE <c>WeenieError.cs</c>:389 @95ef426d), as a UseDone code or as the WeenieError ACE sends before the cast's UseDone 0. ACE raises it only when the target object is no longer
    /// on the landblock (or teleports) at the cast's start, after the turn, at the wind-up or at the release (<c>Player_Magic.cs</c>:137-140, 173-179, 197-203, 751-757): the target died (or left) during the wind-up. Not a miss, not a
    /// walk and never out of range: the caller picks another at once.
    /// </summary>
    TargetDied,
}

/// <summary>Helpers over <see cref="CastEnd"/>.</summary>
public static class CastEndExtensions
{
    /// <summary>True for <see cref="CastEnd.Landed"/> and <see cref="CastEnd.Unproven"/>: the cast was carried out as far as the wire tells (today's callers accept a Sent cast).</summary>
    /// <param name="end">The end.</param>
    public static bool Succeeded(this CastEnd end) => end is CastEnd.Landed or CastEnd.Unproven;

    /// <summary>True once the attempt has ended.</summary>
    /// <param name="end">The end.</param>
    public static bool IsDone(this CastEnd end) => end != CastEnd.Running;

    /// <summary>The end's name for the log and the fields (lower case, no allocation).</summary>
    /// <param name="end">The end.</param>
    public static string Name(this CastEnd end) => end switch
    {
        CastEnd.Running => "running",
        CastEnd.Landed => "landed",
        CastEnd.Unproven => "unproven",
        CastEnd.Fizzled => "fizzled",
        CastEnd.ManaShort => "mana-short",
        CastEnd.NoComponents => "no-components",
        CastEnd.OutOfRange => "out-of-range",
        CastEnd.TargetDied => "target-died",
        CastEnd.NoEffect => "no-effect",
        CastEnd.Busy => "busy",
        CastEnd.Resisted => "resisted",
        CastEnd.Refused => "refused",
        CastEnd.Cancelled => "cancelled",
        CastEnd.DryRun => "dry-run",
        CastEnd.Timeout => "timeout",
        CastEnd.TimeCap => "time-cap",
        CastEnd.ServerError => "server-error",
        _ => "not-implemented",
    };
}

/// <summary>
/// What one <see cref="CastAttempt"/> did (<see cref="CastAttempt.Report"/>): how it ended and with which code, the level and spell cast last, every count, the seconds, and why. During an attempt it
/// holds the counts so far and <see cref="CastEnd.Running"/>.
/// </summary>
public readonly record struct CastReport
{
    /// <summary>How the attempt ended (<see cref="CastEnd.Running"/> while it runs).</summary>
    public CastEnd End { get; init; }

    /// <summary>The last try's server code (the UseDone code, 0x0402 for a fizzle), else 0.</summary>
    public uint Code { get; init; }

    /// <summary>The last try's outcome kind, or null when nothing was sent.</summary>
    public ActionOutcomeKind? Outcome { get; init; }

    /// <summary>The index into <see cref="CastRequest.Levels"/> of the level cast last (the request's chosen index when nothing was sent).</summary>
    public int LevelIndex { get; init; }

    /// <summary>The spell cast last (the chosen level's when nothing was sent).</summary>
    public uint SpellId { get; init; }

    /// <summary>The level a resumed attempt starts at (<see cref="CastRequest.Resume"/>): the level the next try would have used; <see cref="LevelIndex"/> unless the attempt stepped down and did not send.</summary>
    public int NextLevelIndex { get; init; }

    /// <summary>The casts sent (every try, the first included).</summary>
    public int Tries { get; init; }

    /// <summary>The fizzles (0x0402) answered, mana tries' included.</summary>
    public int Fizzles { get; init; }

    /// <summary>The same-level fizzle retries spent, the request's <see cref="CastRequest.FizzlesSpent"/> included (what a resumed attempt carries).</summary>
    public int FizzleRetriesSpent { get; init; }

    /// <summary>The levels stepped down, the request's <see cref="CastRequest.LevelsDownSpent"/> included.</summary>
    public int LevelsDownSpent { get; init; }

    /// <summary>The tries sent with the mana held under that level's ceiling (the Mana Conversion tries of D15, or the dance's presses).</summary>
    public int ManaTries { get; init; }

    /// <summary>The 0x0401 answers.</summary>
    public int ManaRefusals { get; init; }

    /// <summary>The mana tries that landed: confirmed by the server's effect or proved by the chat proof. A Sent cast nothing proves (<see cref="CastEnd.Unproven"/>) is not counted (M5 1 fix A, 1-R16).</summary>
    public int ManaLanded { get; init; }

    /// <summary>The 0x001D answers.</summary>
    public int Busy { get; init; }

    /// <summary>The resists the proof saw.</summary>
    public int Resists { get; init; }

    /// <summary>The Sent casts the proof proved.</summary>
    public int Proved { get; init; }

    /// <summary>The seconds from the start to the end (to now while it runs).</summary>
    public double Seconds { get; init; }

    /// <summary>One sentence: why it ended (the bound used, the code named, the outcome's own words); null while it runs.</summary>
    public string? Why { get; init; }
}

/// <summary>
/// The sum of many <see cref="CastReport"/>s for a caller's verdict fields: <see cref="Fields"/> writes <c>mana_tries= mana_refusals= mana_landed= busy= resists= proved=</c> (report-only in their first
/// gate, m5-plan.md §5 item 3). The fizzles stay the callers' own fields (<c>fizzles=</c>, <c>recall_fizzles=</c>, <c>tie_fizzles=</c>): a caller adds <see cref="Fizzles"/> to its own count.
/// </summary>
public sealed class CastTally
{
    /// <summary>The attempts added.</summary>
    public int Attempts { get; private set; }

    /// <summary>The attempts that ended <see cref="CastEnd.Landed"/>.</summary>
    public int Landed { get; private set; }

    /// <summary>The attempts that ended <see cref="CastEnd.Unproven"/>.</summary>
    public int Unproven { get; private set; }

    /// <summary>The attempts that ended in any other way.</summary>
    public int Failed { get; private set; }

    /// <summary>The casts sent.</summary>
    public int Tries { get; private set; }

    /// <summary>The fizzles.</summary>
    public int Fizzles { get; private set; }

    /// <summary>The mana tries.</summary>
    public int ManaTries { get; private set; }

    /// <summary>The 0x0401 answers.</summary>
    public int ManaRefusals { get; private set; }

    /// <summary>The mana tries that landed (confirmed or proved; an unproven Sent cast is not one).</summary>
    public int ManaLanded { get; private set; }

    /// <summary>The busy answers.</summary>
    public int Busy { get; private set; }

    /// <summary>The resists.</summary>
    public int Resists { get; private set; }

    /// <summary>The proofs.</summary>
    public int Proved { get; private set; }

    /// <summary>The attempts' seconds in all.</summary>
    public double Seconds { get; private set; }

    /// <summary>Adds an ended attempt's report (a running one is not counted).</summary>
    /// <param name="report">The report.</param>
    public void Add(in CastReport report)
    {
        if (report.End == CastEnd.Running)
        {
            return;
        }

        Attempts++;
        switch (report.End)
        {
            case CastEnd.Landed:
                Landed++;
                break;
            case CastEnd.Unproven:
                Unproven++;
                break;
            default:
                Failed++;
                break;
        }

        Tries += report.Tries;
        Fizzles += report.Fizzles;
        ManaTries += report.ManaTries;
        ManaRefusals += report.ManaRefusals;
        ManaLanded += report.ManaLanded;
        Busy += report.Busy;
        Resists += report.Resists;
        Proved += report.Proved;
        Seconds += report.Seconds;
    }

    /// <summary>Clears every count.</summary>
    public void Clear()
    {
        Attempts = Landed = Unproven = Failed = Tries = Fizzles = ManaTries = ManaRefusals = ManaLanded = Busy = Resists = Proved = 0;
        Seconds = 0;
    }

    /// <summary>The fields <c>mana_tries=&lt;n&gt; mana_refusals=&lt;n&gt; mana_landed=&lt;n&gt; busy=&lt;n&gt; resists=&lt;n&gt; proved=&lt;n&gt;</c>.</summary>
    public string Fields => string.Create(CultureInfo.InvariantCulture, $"mana_tries={ManaTries} mana_refusals={ManaRefusals} mana_landed={ManaLanded} busy={Busy} resists={Resists} proved={Proved}");

    /// <summary>Appends <see cref="Fields"/> (with a leading space) to a verdict line being built.</summary>
    /// <param name="line">The line.</param>
    public StringBuilder AppendFields(StringBuilder line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Append(' ').Append(Fields);
    }
}

/// <summary>What a proof found for one Sent cast.</summary>
public enum CastProofResult : byte
{
    /// <summary>Nothing proves it (it stays <see cref="CastEnd.Unproven"/>).</summary>
    None,

    /// <summary>The cast is proved: <see cref="CastEnd.Landed"/>.</summary>
    Proved,

    /// <summary>The target resisted it ("&lt;name&gt; resists your spell").</summary>
    Resisted,
}

/// <summary>The question a proof answers: the spell, its target and the chat log's <see cref="ChatLog.Total"/> when the try was sent (the lines since are the cast's).</summary>
/// <param name="SpellId">The spell sent.</param>
/// <param name="Target">The target's guid (0 untargeted, or the player's own guid).</param>
/// <param name="ChatMark">The chat log's <see cref="ChatLog.Total"/> at the send.</param>
public readonly record struct CastProofQuery(uint SpellId, uint Target, long ChatMark);

/// <summary>
/// The proof hook of a <see cref="CastRequest"/>: asked once when a try ends <see cref="ActionOutcomeKind.Sent"/> (UseDone 0 for a spell with no proof on the wire: a buff on another target, a transfer, a
/// heal, a portal), it says whether the cast is proved. Two proofs exist in the design (§2.2): the target's own registry (in-process, §4.2, not built here) and the caster's chat line
/// (<see cref="CastChatProof"/>). It closes <c>TODO(m3-cast-other-target-proof)</c> for every target that has a proof; a target without one keeps the TODO and ends <see cref="CastEnd.Unproven"/>.
/// </summary>
public interface ICastProof
{
    /// <summary>Whether the cast in <paramref name="query"/> is proved. Must not allocate (it runs inside <see cref="CastAttempt.Step"/>).</summary>
    /// <param name="query">The cast.</param>
    CastProofResult Check(in CastProofQuery query);
}

/// <summary>
/// The caster's own Magic chat line as the proof of a Sent cast (ACE @95ef426d, <c>ChatMessageType.Magic</c> 0x07 through <c>GameMessageSystemChat</c>, which the reducer keeps as a
/// <see cref="ChatKind.System"/> line), read among the lines that arrived since the send:
/// <list type="bullet">
/// <item>an enchantment: "You cast &lt;spell&gt; on &lt;name&gt;" or "on yourself", alone or followed by ", surpassing &lt;spell&gt;", ", refreshing &lt;spell&gt;" or ", but it is surpassed by &lt;spell&gt;"
/// (<c>WorldObject_Magic.cs</c> 409-421 and 443);</item>
/// <item>a dispel: the same followed by " and dispel: ..." or ", but the dispel fails." (1410-1422);</item>
/// <item>a transfer on oneself: "You cast &lt;spell&gt; on yourself and lose ..." (835); on another: "You lose N points of &lt;vital&gt; due to casting &lt;spell&gt; on &lt;name&gt;" (843), and a drain of the target's health, "You gain N points of health due to casting ..." (853);</item>
/// <item>a boost (a heal) on oneself: "You cast &lt;spell&gt; and restore ..." or "and drain ..." (554); on another: "With &lt;spell&gt; you restore ... to &lt;name&gt;." or "... you drain ... from &lt;name&gt;." (546-548);</item>
/// <item>a resist: "&lt;name&gt; resists your spell" (192): <see cref="CastProofResult.Resisted"/>.</item>
/// </list>
/// The spell's name comes from the SpellTable, the target's from the object table (the player's own guid or 0 is "yourself"). Span comparisons only, as <c>FightTally.Classify</c> parses: no allocation.
/// A target the object table does not hold has no name to match, so its cast stays unproven (<c>TODO(m3-cast-other-target-proof)</c> kept for it).
/// </summary>
public sealed class CastChatProof : ICastProof
{
    /// <summary>ACE's <c>ChatMessageType.Magic</c> (ACE.Entity <c>ChatMessageType.cs</c> line 98).</summary>
    public const uint MagicChatType = 0x07;

    private readonly GameState _state;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;

    /// <summary>A proof over the read model's chat and objects and a spell table.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="spells">The SpellTable (the spells' names).</param>
    public CastChatProof(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        _state = state;
        _spells = spells;
    }

    /// <inheritdoc/>
    public CastProofResult Check(in CastProofQuery query)
    {
        if (!_spells.TryGetValue(query.SpellId, out var info))
        {
            return CastProofResult.None;
        }

        var self = query.Target == 0 || query.Target == _state.Self.Guid;
        string? name = null;
        if (!self && _state.Objects.TryGet(query.Target, out var target))
        {
            name = target.Name;
        }

        if (!self && string.IsNullOrEmpty(name))
        {
            return CastProofResult.None;
        }

        var chat = _state.Chat;
        var fresh = (int)Math.Min(chat.Count, Math.Max(0, chat.Total - query.ChatMark));
        var result = CastProofResult.None;
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind != ChatKind.System || line.ChatType != MagicChatType)
            {
                continue;
            }

            var kind = Classify(line.Text, info.Name, self ? null : name);
            if (kind == CastProofResult.Proved)
            {
                return kind;
            }

            if (kind == CastProofResult.Resisted)
            {
                result = kind;
            }
        }

        return result;
    }

    /// <summary>
    /// Whether one Magic line proves a cast of <paramref name="spell"/> on <paramref name="target"/> (null: on the caster), names its resist, or neither. Public so the tests pin the forms.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="spell">The spell's name.</param>
    /// <param name="target">The target's name, or null for the caster.</param>
    public static CastProofResult Classify(ReadOnlySpan<char> text, string spell, string? target)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (target is not null && text.StartsWith(target, StringComparison.Ordinal) && text[target.Length..].SequenceEqual(" resists your spell"))
        {
            return CastProofResult.Resisted;
        }

        if (text.StartsWith("You cast ", StringComparison.Ordinal))
        {
            var rest = text["You cast ".Length..];
            if (!rest.StartsWith(spell, StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            rest = rest[spell.Length..];
            if (target is null && (rest.StartsWith(" and restore ", StringComparison.Ordinal) || rest.StartsWith(" and drain ", StringComparison.Ordinal)))
            {
                return CastProofResult.Proved; // a boost on oneself (554)
            }

            if (!rest.StartsWith(" on ", StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            rest = rest[" on ".Length..];
            var who = target ?? "yourself";
            if (!rest.StartsWith(who, StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            rest = rest[who.Length..];
            return rest.IsEmpty || rest.StartsWith(", ", StringComparison.Ordinal) || rest.StartsWith(" and ", StringComparison.Ordinal) ? CastProofResult.Proved : CastProofResult.None;
        }

        if (target is null)
        {
            return CastProofResult.None;
        }

        if (text.StartsWith("With ", StringComparison.Ordinal))
        {
            var rest = text["With ".Length..];
            if (!rest.StartsWith(spell, StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            rest = rest[spell.Length..];
            if (!rest.StartsWith(" you restore ", StringComparison.Ordinal) && !rest.StartsWith(" you drain ", StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            // "... to <name>." or "... from <name>." (546-548): the name is the line's tail before the full stop.
            return EndsWithName(rest, " to ", target) || EndsWithName(rest, " from ", target) ? CastProofResult.Proved : CastProofResult.None;
        }

        if (text.StartsWith("You lose ", StringComparison.Ordinal) || text.StartsWith("You gain ", StringComparison.Ordinal)) // M5 7 F7: a drain on another is "You gain N points of health due to casting <spell> on <name>" (WorldObject_Magic.cs 853 @95ef426d: the caster is the destination)
        {
            var at = text.IndexOf(" due to casting ", StringComparison.Ordinal);
            if (at < 0)
            {
                return CastProofResult.None;
            }

            var rest = text[(at + " due to casting ".Length)..];
            if (!rest.StartsWith(spell, StringComparison.Ordinal))
            {
                return CastProofResult.None;
            }

            rest = rest[spell.Length..];
            return rest.StartsWith(" on ", StringComparison.Ordinal) && rest[" on ".Length..].SequenceEqual(target) ? CastProofResult.Proved : CastProofResult.None;
        }

        return CastProofResult.None;
    }

    private static bool EndsWithName(ReadOnlySpan<char> rest, string lead, string target)
    {
        var tail = rest.EndsWith(".", StringComparison.Ordinal) ? rest[..^1] : rest;
        return tail.Length > lead.Length + target.Length && tail.EndsWith(target, StringComparison.Ordinal) && tail[..^target.Length].EndsWith(lead, StringComparison.Ordinal);
    }
}
