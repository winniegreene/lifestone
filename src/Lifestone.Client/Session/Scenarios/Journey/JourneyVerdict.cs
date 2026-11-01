using System.Globalization;
using System.Text;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>One station's result in a journey (M4 P5b 3.4).</summary>
/// <param name="Name">The station's name.</param>
/// <param name="Ok">True when the station passed by its leg's verdict rules.</param>
/// <param name="Seconds">The session seconds the station ran (0 for a skip).</param>
/// <param name="Fields">The station's verdict fields (today's leg names), or empty.</param>
/// <param name="Why">Why it failed (<c>ok=false why=</c>), or null.</param>
/// <param name="Skipped">The precondition that skipped it (<c>precondition:&lt;x&gt;</c>), or null when it ran.</param>
/// <param name="CausedBy">The earlier failed station a skip follows (it does not count against <c>ok=</c>), or null.</param>
public sealed record JourneyStationResult(string Name, bool Ok, double Seconds, string Fields, string? Why = null, string? Skipped = null, string? CausedBy = null);

/// <summary>
/// A journey's verdict (M4 P5b 3.4, 3.5): one <c>journey station=&lt;name&gt; ok= s= &lt;fields&gt;</c> line per station and the
/// <c>journey done ok= name= stations=n/m failed= teleports= recalls= budget= route_m= run_s= vitals_passes= recovery_places= granted=</c> line
/// (the Godot host prefixes <c>[logincheck]</c>; these are the bare texts). <c>ok=</c> holds when every station is ok or skipped by a precondition an
/// earlier station's failure caused, every planned station has a result, and <c>teleports&lt;=budget</c>. The gate's judges read the fields; the new ones
/// (<c>teleports</c>, <c>recalls</c>, <c>route_m</c>, <c>granted</c>, <c>recovery_places</c>) are report-only in their first gate.
/// </summary>
public sealed class JourneyVerdict
{
    /// <summary>The log category of the journey's lines.</summary>
    public const string LogCategory = "journey";

    /// <summary>The longest <c>why=</c> a line carries (Lifestone's own: the log keeps the whole sentence).</summary>
    public const int MaxWhyLength = 160;

    private readonly List<JourneyStationResult> _stations = [];

    /// <summary>A verdict for a plan of <paramref name="planned"/> stations.</summary>
    /// <param name="planned">The plan's station count (the <c>m</c> of <c>stations=n/m</c>).</param>
    public JourneyVerdict(int planned) => Planned = planned;

    /// <summary>The plan's station count.</summary>
    public int Planned { get; }

    /// <summary>The results so far, in order.</summary>
    public IReadOnlyList<JourneyStationResult> Stations => _stations;

    /// <summary>The vitals passes that had work to do (the keeper was not ready at once): <c>vitals_passes=</c>.</summary>
    public int VitalsPasses { get; set; }

    /// <summary>1 when the pre-journey known-state placed the player in Holtburg (never budgeted): <c>recovery_place=</c>.</summary>
    public int RecoveryPlace { get; set; }

    /// <summary>The recovery places after a failed station (a fighter whose way home failed): <c>recovery_places=</c>, judged 0 from the second gate.</summary>
    public int RecoveryPlaces { get; set; }

    /// <summary>How the journey recovered in the den: <c>none</c> or <c>suicide</c> (<c>recovery=</c>).</summary>
    public string Recovery { get; set; } = "none";

    /// <summary>The first failed station's name, or null.</summary>
    public string? FirstFailed => _stations.FirstOrDefault(r => r.Skipped is null && !r.Ok)?.Name;

    /// <summary>Records a result and returns its line.</summary>
    /// <param name="result">The station's result.</param>
    public string Add(JourneyStationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _stations.Add(result);
        return StationLine(result);
    }

    /// <summary>The station line: <c>journey station=&lt;name&gt; ok=&lt;true|false&gt; s=&lt;seconds&gt; &lt;fields&gt;</c>, then <c>why=</c>, <c>skipped=</c> and <c>caused_by=</c> when they apply.</summary>
    /// <param name="r">The result.</param>
    public static string StationLine(JourneyStationResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture, $"journey station={r.Name} ok={Bool(r.Ok)} s={r.Seconds:0.0}");
        if (!string.IsNullOrWhiteSpace(r.Fields))
        {
            b.Append(' ').Append(r.Fields.Trim());
        }

        if (r.Why is { } why && !Contains(r.Fields, "why="))
        {
            b.Append(" why=").Append(Token(why));
        }

        if (r.Skipped is { } skipped)
        {
            b.Append(" skipped=").Append(Token(skipped));
        }

        if (r.CausedBy is { } cause)
        {
            b.Append(" caused_by=").Append(cause);
        }

        return b.ToString();
    }

    /// <summary>True when every planned station has a result, each is ok or a skip an earlier failure caused, and the teleports stayed in the budget.</summary>
    /// <param name="facts">The journey's facts.</param>
    public bool Ok(JourneyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return _stations.Count == Planned
            && _stations.All(r => r.Ok || (r.Skipped is not null && r.CausedBy is not null))
            && facts.Teleports <= facts.TeleportBudget;
    }

    /// <summary>
    /// The done line: <c>journey done ok= name= stations=n/m failed=&lt;names|none&gt; teleports= recalls= budget= route_m= run_s= rungs= blocked= sightless= vitals_passes= recovery_places= granted= places=</c>,
    /// then <c>recovery_place= recovery=</c>. <c>granted=</c> is every <c>item</c> and <c>xp</c> the session sent since the end of the build-verify station and <c>places=</c> every <c>place</c> that was not a recovery (M5 item 3 c1, <see cref="Lifestone.Client.Session.Contracts.ModVerbCounter"/>; report-only until item 4's gate). <c>n</c> counts the stations that passed; <c>failed=</c> names the stations that ran and failed (skips are not failures). <c>rungs=</c>, <c>blocked=</c>
    /// and <c>sightless=</c> (the runs' ladder rungs, Blocked ends and sightless arrivals, summed by the travel seam) are report-only: <see cref="Ok"/> never reads them (M4 P5b fix FA, P5b-R6).
    /// </summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="facts">The journey's facts.</param>
    /// <param name="routeMetres">The runs' metres.</param>
    /// <param name="runSeconds">The runs' seconds.</param>
    /// <param name="rungs">The runs' ladder rungs.</param>
    /// <param name="blocked">The runs that ended Blocked.</param>
    /// <param name="sightless">The runs that ended at their range without the clear line they needed.</param>
    public string DoneLine(string name, JourneyFacts facts, double routeMetres, double runSeconds, int rungs = 0, int blocked = 0, int sightless = 0)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var failed = _stations.Where(r => r.Skipped is null && !r.Ok).Select(r => r.Name).ToList();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"journey done ok={Bool(Ok(facts))} name={name} stations={_stations.Count(r => r.Ok)}/{Planned} failed={(failed.Count == 0 ? "none" : string.Join(',', failed))} teleports={facts.Teleports} recalls={facts.Recalls} budget={facts.TeleportBudget} route_m={routeMetres:0.0} run_s={runSeconds:0.0} rungs={rungs} blocked={blocked} sightless={sightless} vitals_passes={VitalsPasses} recovery_places={RecoveryPlaces} granted={facts.Verbs.Granted} places={facts.Verbs.Place} recovery_place={RecoveryPlace} recovery={Recovery}");
    }

    /// <summary>A sentence as one line token: whitespace to underscores, at most <see cref="MaxWhyLength"/> characters.</summary>
    /// <param name="text">The text.</param>
    public static string Token(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var b = new StringBuilder(Math.Min(text.Length, MaxWhyLength));
        foreach (var c in text)
        {
            if (b.Length >= MaxWhyLength)
            {
                break;
            }

            b.Append(char.IsWhiteSpace(c) ? '_' : c);
        }

        return b.ToString();
    }

    private static bool Contains(string fields, string key) =>
        fields.StartsWith(key, StringComparison.Ordinal) || fields.Contains(" " + key, StringComparison.Ordinal);

    private static string Bool(bool value) => value ? "true" : "false";
}
