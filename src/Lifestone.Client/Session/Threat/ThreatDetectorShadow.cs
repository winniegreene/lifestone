using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Threat;

/// <summary>
/// M6 16 E2b (docs/automation-engine.md E2 and section 3, "shadow first"; m6-plan.md D139): one detector of "who is near, who attacks her" beside the threat board, for the detectors E2a did not reach: the fight's
/// self-defence list, its buff refresh's hostile read, its two awake reads and its pull rest's read, the vitals keeper's awake read, the heal station's pre-check, the quest steps' list, the death handler's two reads, the travel
/// fight-back's pick, and the retreat's and the vitals guard's reads off the outer loop's watch. The detector decides as it always did. Where it has answered, its owner calls <see cref="Answered"/> with the answer and with
/// the one board query whose parameters are that detector's own (its reach, its window, what counts); the board is asked that and the pair is compared by value: whether anything answered, which creature when both name
/// one, and how many when the detector counts them. Report-only: nothing here is read by any decision. Allocation-free while the pair stands (a line allocates, on a change only: <see cref="ThreatShadowLane"/>).
/// </summary>
/// <remarks>
/// A distance is never compared: several detectors measure their own (to the path ahead, within a landblock), and a distance moves every frame. A detector that names no creature (it answers yes or no) is compared on
/// that alone; one that does not count is not compared on the count.
/// </remarks>
public sealed class ThreatDetectorShadow
{
    private readonly IGameSessionView _session;
    private readonly ThreatBoard _board;
    private readonly Action<string> _line;
    private readonly ILogSink _log;
    private readonly string _category;

    /// <summary>The shadow of one detector.</summary>
    /// <param name="session">The session.</param>
    /// <param name="detector">The lane's word in the lines (<c>fight-defence</c>, <c>keeper-awake</c>, ...).</param>
    /// <param name="line">The owner's line writer (its own prefix and category).</param>
    /// <param name="log">The owner's log, for the one loud line of a shadow that failed.</param>
    /// <param name="category">The owner's log category.</param>
    /// <param name="hearsSince">True when the detector hears attacks by a watch of its own, begun with it: a difference then says how long it has heard.</param>
    public ThreatDetectorShadow(IGameSessionView session, string detector, Action<string> line, ILogSink log, string category, bool hearsSince = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(log);
        _session = session;
        _board = ThreatBoard.For(session);
        Lane = _board.Lane(detector);
        (_line, _log, _category) = (line, log, category);
        Since = session.Clock.Now;
        HearsSince = hearsSince;
    }

    /// <summary>The session's lane of this detector.</summary>
    public ThreatShadowLane Lane { get; }

    /// <summary>The session time this shadow was made at: the start of a detector that hears by a watch of its own (a query's <see cref="ThreatQuery.Since"/> for the flip's question "the same but for the history").</summary>
    public double Since { get; }

    /// <summary>True when the detector hears attacks by a watch of its own.</summary>
    public bool HearsSince { get; }

    /// <summary>
    /// The detector answered at <paramref name="now"/>: the board is asked <paramref name="query"/> and the pair compared (once a frame: a detector asked again at the same clock is not compared again).
    /// </summary>
    /// <param name="now">The session clock.</param>
    /// <param name="query">The detector's question as one board query.</param>
    /// <param name="found">The detector's answer: something is near, or attacks her.</param>
    /// <param name="guid">The creature the detector named, or 0: it names none (it answers yes or no, or names the first it met).</param>
    /// <param name="metres">That creature's distance as the detector measured it, or NaN; for the words only.</param>
    /// <param name="count">How many the detector counted, or -1: it does not count.</param>
    /// <param name="said">The detector's own words for its answer, or null.</param>
    /// <param name="sameCreature">False when the creature the detector names is not the one the board would name by its rule (the detector names the nearest to a path, or the first it met): it is in the words, and not compared.</param>
    public void Answered(double now, in ThreatQuery query, bool found, uint guid = 0, double metres = double.NaN, int count = -1, string? said = null, bool sameCreature = true)
    {
        if (!Lane.Due(now))
        {
            return;
        }

        try
        {
            var board = _board.Ask(_session, in query, now);
            var bodies = board.Kind == ThreatAnswerKind.Standing; // an attack that answers alone reads no body: neither a creature nor a count to compare
            var agree = found == board.Any && (!found || !bodies || !sameCreature || guid == 0 || guid == board.Guid) && (count < 0 || board.Kind == ThreatAnswerKind.Attacked || count == board.Count);
            var change = Lane.Note(now, agree, ((found ? 1L : 0L) << 56) | ((long)(Math.Max(count, 0) & 0xFFFFFF) << 32) | guid, ((long)board.Kind << 56) | ((long)(board.Count & 0xFFFFFF) << 32) | board.Guid);
            if (change == ThreatShadowChange.Differs)
            {
                _line(Lane.DiffersLine(DetectorWords(now, found, guid, metres, count, said), BoardWords(in query, in board, now)));
            }
            else if (change == ThreatShadowChange.AgreesAgain)
            {
                _line(Lane.AgreesLine());
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Lane.Fail(error, _log, _category);
        }
    }

    private string DetectorWords(double now, bool found, uint guid, double metres, int count, string? said)
    {
        var c = CultureInfo.InvariantCulture;
        var counted = count < 0 ? string.Empty : string.Create(c, $" ({count} counted)");
        if (!found)
        {
            var hears = HearsSince ? string.Create(c, $" (its watch hears since {now - Since:0.#} s)") : string.Empty;
            return (string.IsNullOrEmpty(said) ? "none" : said) + hears;
        }

        if (!string.IsNullOrEmpty(said))
        {
            return said + counted;
        }

        if (guid == 0)
        {
            return "one or more" + counted;
        }

        var at = double.IsNaN(metres) ? string.Empty : string.Create(c, $" at {metres:0.#} m");
        return string.Create(c, $"{NameOf(guid)} 0x{guid:X8}{at}{counted}");
    }

    private string BoardWords(in ThreatQuery query, in ThreatAnswer board, double now)
    {
        var c = CultureInfo.InvariantCulture;
        var reach = double.IsInfinity(query.ReachMetres) ? "any distance" : string.Create(c, $"{query.ReachMetres:0.#} m");
        var before = HearsSince && board.At < Since ? string.Create(c, $", {Since - board.At:0.#} s before the detector began") : string.Empty;
        switch (board.Kind)
        {
            case ThreatAnswerKind.Attacked:
                return string.Create(c, $"{(board.Name.Length > 0 ? board.Name : "an unnamed attacker")} attacked {now - board.At:0.#} s ago{before}");
            case ThreatAnswerKind.Standing:
                var heard = double.IsNegativeInfinity(board.At) ? string.Empty : string.Create(c, $", its name heard attacking {now - board.At:0.#} s ago{before}");
                return string.Create(c, $"{board.Name} 0x{board.Guid:X8} at {board.Metres:0.#} m ({board.Count} within {reach}){heard}");
            default:
                var hits = query.Heard == ThreatHeard.Hits;
                var word = hits ? "hit" : "attack";
                var alone = query.AttackAlone && query.QuietSeconds > 0 ? string.Create(c, $"no {word} within {query.QuietSeconds:0.#} s; ") : string.Empty;
                var standing = query.Standing switch
                {
                    ThreatStanding.None => "no creature is asked for",
                    ThreatStanding.Targets => string.Create(c, $"no target of the ground stands alive within {reach}"),
                    ThreatStanding.Attackers => string.Create(c, $"no creature that may be attacked, of a name heard {(hits ? "hitting" : "attacking")} her within {query.QuietSeconds:0.#} s, stands alive within {reach}"),
                    _ => string.Create(c, $"no creature that may be attacked stands alive within {reach}"),
                };
                var (lastAt, lastBy) = hits ? (_board.LastHitAt, _board.LastHitBy) : (_board.LastAttackAt, _board.LastAttackBy);
                var last = query.QuietSeconds <= 0 ? string.Empty
                    : double.IsNegativeInfinity(lastAt) ? string.Create(c, $"; no {word} heard this session")
                    : string.Create(c, $"; the last {word} heard: {(lastBy.Length > 0 ? lastBy : "an unnamed attacker")} {now - lastAt:0.#} s ago");
                return string.Create(c, $"none ({alone}{standing}{last})");
        }
    }

    private string NameOf(uint guid) => _session.State.Objects.TryGet(guid, out var o) ? o.Name ?? "creature" : "creature";
}

/// <summary>
/// M6 16 E2b: the share of the session's shadow lanes one owner reports on a line of its own (a fight, a quest leg, a death handling): the lanes it asks through and a mark of each, so the line states the asks since the
/// owner began (or since its last line: <see cref="Remark"/>) though a lane is the session's and outlives it. Allocates on <see cref="Add"/> and for a line; reading the counts allocates nothing.
/// </summary>
public sealed class ThreatShadowShare
{
    private readonly List<ThreatShadowLane> _lanes = [];
    private readonly List<ThreatShadowMark> _marks = [];

    /// <summary>Adds <paramref name="lane"/> (once), marked at its counts now.</summary>
    /// <param name="lane">The lane.</param>
    public void Add(ThreatShadowLane lane)
    {
        ArgumentNullException.ThrowIfNull(lane);
        if (!_lanes.Contains(lane))
        {
            _lanes.Add(lane);
            _marks.Add(lane.Mark());
        }
    }

    /// <summary>The asks of every lane since its mark: those that agreed and those that differed.</summary>
    public (long Agree, long Differ) Counts
    {
        get
        {
            var (agree, differ) = (0L, 0L);
            for (var i = 0; i < _lanes.Count; i++)
            {
                agree += _lanes[i].Agree - _marks[i].Agree;
                differ += _lanes[i].Differ - _marks[i].Differ;
            }

            return (agree, differ);
        }
    }

    /// <summary>The marks move to the lanes' counts now (the owner wrote its line; the next one starts here).</summary>
    public void Remark()
    {
        for (var i = 0; i < _lanes.Count; i++)
        {
            _marks[i] = _lanes[i].Mark();
        }
    }

    /// <summary>The two summary fields of <see cref="Counts"/>: <c>threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt;</c>.</summary>
    public string Fields()
    {
        var (agree, differ) = Counts;
        return ThreatShadowLane.FieldsOf(agree, differ);
    }

    /// <summary>
    /// The owner's line: <c>threat-shadow: &lt;owner&gt;: its asks: threat_shadow_agree=&lt;n&gt; threat_shadow_differ=&lt;n&gt; (&lt;detector&gt; &lt;agreed&gt;/&lt;differed&gt;, ...)</c>, or null when no lane was asked since the marks.
    /// </summary>
    /// <param name="owner">The owner's word (<c>fight</c>, <c>quest-leg</c>, ...).</param>
    public string? Line(string owner)
    {
        var (agree, differ) = Counts;
        if (agree + differ == 0)
        {
            return null;
        }

        var by = new System.Text.StringBuilder();
        for (var i = 0; i < _lanes.Count; i++)
        {
            var (a, d) = (_lanes[i].Agree - _marks[i].Agree, _lanes[i].Differ - _marks[i].Differ);
            if (a + d > 0)
            {
                by.Append(by.Length == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"{_lanes[i].Detector} {a}/{d}");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{ThreatShadowLane.Token}{owner}: its asks: {ThreatShadowLane.FieldsOf(agree, differ)} (agreed/differed by detector: {by})");
    }
}
