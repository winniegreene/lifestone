using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 item 14 (<c>m6-live-position</c>, the fix behind <c>TODO(m6-sticky-position)</c>): where the fight takes a creature to stand. One rule, in one place (<see cref="StandsAt"/>): a creature stands where the session's
/// <see cref="IGameSessionView.Live"/> says, which is the body the screen draws when the session draws bodies and the body can be trusted, else the server's last report (<see cref="LivePositions"/> gives the rule and its
/// fall backs). Every read of a creature's place in the fight goes through it: self-defence (the attackers, the struck proof, the walk toward one), the line test, the pick's distance and cell, the emergency and drain
/// picks, the rings' and bolts' reach, the rest's and the loot cut's hostile checks. The table's creature queries the fight calls take the same answer (<see cref="WorldObjectTable.NearestCreature(IReadOnlyList{uint}, in CellFrame, float, Func{uint, bool}, IReadOnlyList{uint}, out uint, LivePositions)"/>,
/// <see cref="WorldObjectTable.NearestLivingCreatureNamed"/>, <see cref="WorldObjectTable.NearestAttackableCreature"/>), and so does the autopilot's hostile watch.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The Cave's run of 2026-11-12 (UTC): a tusker in melee follows the mage with no position sent (ACE moves a sticky monster with the broadcast off, <c>Monster_Tick.cs:118-119</c>), so the server's last
/// report left it where it stood before her own steps, often round the tunnel's bend. The line test was right about that spot and wrong about the creature: 60 stall lines and 85 walks of up to 17 s in two and a half
/// hours while it went on hitting her, 40 of them with the line blocked in her own cell. The body the screen draws is moved by retail's client physics in between (MoveTo, then the sticky follow for a second from each swing), so it is the better record of the two; where it is no better (the walk back when a stick ends) is said in <see cref="LivePositions"/>' remarks, and nothing here was seen live: the measure below is what the next run reports.</para>
/// <para><b>Left on the server's report</b>, because they do not move or because the report is what is wanted: corpses (the loot's searches, walks and opens), a creature's position in its own CreateObject (the tour's
/// slot learning in <c>OnCreated</c>), a projectile's launch position (<c>HitWaitFor</c>'s bolt), and the player's own position (<c>SelfState.Position</c>, the player body's). M6 14 caught on the run (closing
/// <c>TODO(m6-live-position-reach)</c>): the shared hostile reads (<c>HostileReach</c>: the pack count, the hunt's, the journey legs', the heal stations' and the death tally's) take the same live answer.</para>
/// <para><b>The measure</b> (<see cref="LivePositionWatch"/>, report-only): while bodies are drawn, an attacker or the target whose body stands more than <see cref="LivePositionGapMetres"/> from the server's report is
/// logged with both places, at most once per creature every <see cref="LivePositionLogSeconds"/>, and counted (<c>live_position_gaps=</c> on the verdict line when above zero). A second line
/// (<see cref="LivePositionBlow"/>) marks a blow that came while the fight took its attacker to stand out of a melee blow's reach: the case where both records are wrong together.</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>The measure logs a creature whose body and server report lie farther apart than this, in metres (Lifestone's own: above the sticky follow's 0.3 m gap and an interpolation's lag, below a melee reach's worth of error).</summary>
    public const double LivePositionGapMetres = 1.5;

    /// <summary>The measure logs one creature at most once in this many session-clock seconds.</summary>
    public const double LivePositionLogSeconds = 5;

    private const int LivePositionLoggedMax = 64;

    private readonly Dictionary<uint, double> _livePositionLoggedAt = [];

    /// <summary>The gaps the measure logged (report-only): an attacker or the target drawn more than <see cref="LivePositionGapMetres"/> from the server's report, counted at the log's rate.</summary>
    public int LivePositionGaps { get; private set; }

    /// <summary>The widest gap the measure saw, in metres (0: none).</summary>
    public double LivePositionGapMaxMetres { get; private set; }

    /// <summary>
    /// A blow from an attacker the fight takes to stand farther than this from the mage, in metres, is logged (<see cref="LivePositionBlow"/>; Lifestone's own: a melee blow lands from the two bodies' radii plus ACE's
    /// 0.75 m, <c>Monster_Navigation.cs:28</c>, which 4 m covers for a large creature).
    /// </summary>
    public const double LivePositionBlowMetres = 4;

    /// <summary>The blows the measure logged as coming from farther than <see cref="LivePositionBlowMetres"/> by the fight's own answer (report-only, at the log's rate).</summary>
    public int LivePositionFarBlows { get; private set; }

    /// <summary>
    /// THE rule for a creature's place in the fight (see the class remarks): where <paramref name="o"/> stands now by the session's <see cref="IGameSessionView.Live"/>. An object that is not a creature (a corpse, a
    /// door, a projectile) does not ask the bodies: it stands where the server put it. Null when there is no object or the server's record gives it no position. Allocates nothing.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="o">The object, or null.</param>
    private static CellFrame? StandsAt(IGameSessionView session, WorldObject? o) =>
        o is null ? null : (o.ItemType & WarCreatureItemType) == 0 ? o.Position : session.Live.At(o);

    /// <summary>The verdict line's fields of the measure: nothing while it logged nothing (a host without bodies never does), so the line's pinned shape stands.</summary>
    private string LivePositionFields() =>
        LivePositionGaps == 0 && LivePositionFarBlows == 0 && LivePositionBodyRight + LivePositionReportRight == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" live_position_gaps={LivePositionGaps} live_position_gap_max_m={LivePositionGapMaxMetres:0.#} live_position_far_blows={LivePositionFarBlows} live_position_body_right={LivePositionBodyRight} live_position_report_right={LivePositionReportRight}");

    private Lifestone.Net.Types.ServerMotionState? _livePositionOwnOrder;

    /// <summary>The movement orders of the mage's own that named a creature's place or bearing (report-only count).</summary>
    public int LivePositionOwnOrderLines { get; private set; }

    /// <summary>
    /// M6 14 (the owner, 2026-11-12: use the information the server gives). The server's orders for the MAGE say where the thing she acts on is: a MoveToObject order (a Use or an attack out of reach) carries
    /// "the location of the target" (ACE <c>Network/Motion/MoveToObject.cs</c>:10,16), which the reducer now keeps on the target (<see cref="WorldObject.OrderPosition"/>) and the seam reads
    /// (<see cref="LiveSource.ServerOrder"/>); a TurnToObject order (each cast at a target) carries the heading to it (<c>TurnToObject.cs</c>: DesiredHeading, "heading of the object to turn to"). Each such
    /// order aimed at a creature is logged with what the client believed: how far the named place lies from the record and from the drawn body, or the heading the record gives against the server's.
    /// Report-only: the numbers say how wrong the client's picture was at the moment the server spoke.
    /// </summary>
    private void LivePositionOwnOrders(IGameSessionView session, LivePositions live, double clock, in CellFrame here)
    {
        var self = session.State.Self.Guid;
        if (!session.State.Objects.TryGet(self, out var me) || me.Motion is not { } order || ReferenceEquals(order, _livePositionOwnOrder))
        {
            return;
        }

        _livePositionOwnOrder = order;
        var named = order.MoveTo?.TargetGuid ?? order.TurnTo?.TargetGuid;
        if (named is not { } guid || guid == self || !session.State.Objects.TryGet(guid, out var o) || (o.ItemType & WarCreatureItemType) == 0 || o.Position is not { } report)
        {
            return; // a corpse, a door, a vendor: they do not move
        }

        LivePositionOwnOrderLines++;
        var mage = here.ToGlobal();
        var body = live.Read(o).Body;
        var age = double.IsNaN(o.PositionAt) ? double.NaN : clock - o.PositionAt;
        if (order.MoveTo is { } move)
        {
            var place = new CellFrame(move.OriginCellId, report.Frame with { Origin = move.Origin });
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: order: the server walks the mage to {o.Name} 0x{guid:X8} and names its place: 0x{move.OriginCellId:X8} [{move.Origin.X:0.0} {move.Origin.Y:0.0} {move.Origin.Z:0.0}], {place.ToGlobal().DistanceTo(mage):0.0} m from her; the record (sent {age:0.0} s ago) was {report.ToGlobal().DistanceTo(place.ToGlobal()):0.0} m from that place, the drawn body {(body is { } b ? b.ToGlobal().DistanceTo(place.ToGlobal()).ToString("0.0", CultureInfo.InvariantCulture) : "n/a")} m (the place is kept as the server's newest word for {LivePositions.OrderWordSeconds:0} s)"));
            return;
        }

        var turn = order.TurnTo!.Value;
        var said = turn.ObjectHeading ?? turn.Parameters.DesiredHeading;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: order: the server turns the mage to {o.Name} 0x{guid:X8}: heading {said:0.0} deg; by the record (sent {age:0.0} s ago, {report.ToGlobal().DistanceTo(mage):0.0} m off) the heading is {HeadingTo(in here, in report):0.0} deg{(body is { } drawn ? string.Create(CultureInfo.InvariantCulture, $", by the drawn body ({drawn.ToGlobal().DistanceTo(mage):0.0} m off) {HeadingTo(in here, in drawn):0.0} deg") : string.Empty)} (AC heading: 0 north, clockwise; report-only)"));
    }

    /// <summary>The AC heading, in degrees (0 north, +Y; clockwise), from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static double HeadingTo(in CellFrame from, in CellFrame to)
    {
        var a = from.ToGlobal();
        var b = to.ToGlobal();
        var degrees = Math.Atan2(b.X - a.X, b.Y - a.Y) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    /// <summary>M6 14 (the wire trace): a position report is logged when it comes at least this long after the one before it (the first of a burst: ACE reports a moving monster every 0.2 s, Monster_Tick.cs:10), or moves the record by <see cref="LivePositionWireMetres"/>.</summary>
    public const double LivePositionWireSeconds = 1;

    /// <summary>M6 14 (the wire trace): see <see cref="LivePositionWireSeconds"/>.</summary>
    public const double LivePositionWireMetres = 3;

    private readonly Dictionary<uint, (Lifestone.Net.Types.ServerMotionState? Motion, double At, CellFrame Report, int Reports)> _livePositionWire = [];

    /// <summary>The lines the wire trace wrote in this fight (report-only).</summary>
    public int LivePositionWireLines { get; private set; }

    /// <summary>
    /// M6 14 (the wire trace; the owner, 2026-11-12: the explanation of the wrong positions was a theory: get the correct information). For an attacker or the target, every movement order the server sends
    /// (F74C: its type, the object or point it moves to, its sticky object, its flags) and the position reports that open a burst or move the record far are logged beside where the body is drawn and how far
    /// each stands from the mage. What the server said, in order, and what the client drew: the sequence in the seconds the two disagree is read from the log, not reasoned out. Report-only.
    /// </summary>
    private void LivePositionWire(WorldObject o, uint guid, in CellFrame body, in CellFrame report, in CellFrame here, double clock)
    {
        var at = o.PositionAt;
        var motion = o.Motion;
        if (!_livePositionWire.TryGetValue(guid, out var seen))
        {
            if (_livePositionWire.Count >= LivePositionLoggedMax)
            {
                _livePositionWire.Clear(); // bounded
            }

            _livePositionWire[guid] = (motion, at, report, 0);
            return;
        }

        var mage = here.ToGlobal();
        var reports = seen.Reports;
        if (!ReferenceEquals(motion, seen.Motion) && motion is not null)
        {
            LivePositionWireLines++;
            var to = motion.MoveTo is { } move
                ? string.Create(CultureInfo.InvariantCulture, $" to={(move.TargetGuid is { } target ? $"0x{target:X8}{(target == _view?.State.Self.Guid ? " (the mage)" : string.Empty)}" : "a point")} at 0x{move.OriginCellId:X8} [{move.Origin.X:0.0} {move.Origin.Y:0.0} {move.Origin.Z:0.0}] flags=0x{move.Parameters.Flags:X} distance={move.Parameters.DistanceToObject:0.0#} min={move.Parameters.MinDistance:0.0#} fail={move.Parameters.FailDistance:0.#} speed={move.Parameters.Speed:0.0#}")
                : string.Empty;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: wire: {o.Name} 0x{guid:X8}: a movement order: type={motion.MovementType}{to} sticky={(motion.StickyGuid is { } sticky ? $"0x{sticky:X8}" : "none")} forward=0x{motion.Forward ?? 0:X4} stance=0x{motion.Stance:X4}; the body is drawn {body.ToGlobal().DistanceTo(mage):0.0} m from the mage, the last report {report.ToGlobal().DistanceTo(mage):0.0} m, sent {(double.IsNaN(at) ? double.NaN : clock - at):0.0} s ago ({reports} report(s) since the order before; report-only)"));
            reports = 0;
        }

        if (!double.IsNaN(at) && at != seen.At)
        {
            reports++;
            var gap = double.IsNaN(seen.At) ? double.NaN : at - seen.At;
            var moved = seen.Report.ToGlobal().DistanceTo(report.ToGlobal());
            if (!(gap < LivePositionWireSeconds) || moved >= LivePositionWireMetres)
            {
                LivePositionWireLines++;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: wire: {o.Name} 0x{guid:X8}: a position report {gap:0.0} s after the one before, {moved:0.0} m from it: 0x{report.CellId:X8} [{report.Origin.X:0.0} {report.Origin.Y:0.0} {report.Origin.Z:0.0}], {report.ToGlobal().DistanceTo(mage):0.0} m from the mage; the body is drawn {body.ToGlobal().DistanceTo(report.ToGlobal()):0.0} m from this report, {body.ToGlobal().DistanceTo(mage):0.0} m from the mage (report-only)"));
            }
        }

        _livePositionWire[guid] = (motion, at, report, reports);
    }

    /// <summary>M6 14 (the referee): the least seconds between two server reports of a creature for the new one to judge the body against the old one (a report this old is the stale record the fight used to read).</summary>
    public const double LivePositionTruthSeconds = 2;

    private readonly Dictionary<uint, (CellFrame Report, double At, CellFrame Body)> _livePositionSeen = [];

    /// <summary>The times a new server report found the drawn body nearer to it than the report before it (the referee, report-only).</summary>
    public int LivePositionBodyRight { get; private set; }

    /// <summary>The times a new server report found the report before it nearer to it than the drawn body (the referee, report-only).</summary>
    public int LivePositionReportRight { get; private set; }

    /// <summary>
    /// M6 14 (the referee; live 2026-11-12 14:12 to 14:20 UTC, the Cave: four gaps of 5 to 11 m in which the server's report, 5 to 20 s old, had an attacker at her side and the drawn body had it 4 to 10 m off:
    /// which record was right could not be said). When a new server report arrives for an attacker or the target at least <see cref="LivePositionTruthSeconds"/> after the one before it, the new report is the
    /// truth of that moment: the body as it was drawn the frame before and the old report are each measured against it, the nearer is counted, and one line says both (when either was more than
    /// <see cref="LivePositionGapMetres"/> off). Report-only: it changes nothing the fight reads.
    /// </summary>
    private void LivePositionTruth(WorldObject o, uint guid, in CellFrame body, in CellFrame report)
    {
        var at = o.PositionAt;
        if (double.IsNaN(at))
        {
            return;
        }

        if (_livePositionSeen.TryGetValue(guid, out var seen) && at != seen.At)
        {
            var age = at - seen.At;
            var bodyOff = seen.Body.ToGlobal().DistanceTo(report.ToGlobal());
            var reportOff = seen.Report.ToGlobal().DistanceTo(report.ToGlobal());
            if (age >= LivePositionTruthSeconds && Math.Max(bodyOff, reportOff) > LivePositionGapMetres)
            {
                var bodyRight = bodyOff <= reportOff;
                if (bodyRight)
                {
                    LivePositionBodyRight++;
                }
                else
                {
                    LivePositionReportRight++;
                }

                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: referee: {o.Name} 0x{guid:X8}: a new server report came {age:0.0} s after the last: the drawn body was {bodyOff:0.0} m from it, the old report {reportOff:0.0} m: the {(bodyRight ? "body" : "old report")} was nearer (body right {LivePositionBodyRight}, old report right {LivePositionReportRight} in this fight; report-only)"));
            }
        }

        if (_livePositionSeen.Count >= LivePositionLoggedMax && !_livePositionSeen.ContainsKey(guid))
        {
            _livePositionSeen.Clear(); // bounded
        }

        _livePositionSeen[guid] = (report, at, body);
    }

    /// <summary>The measure, each step (report-only; returns at once without bodies, attackers or a target): each attacker, then the target when it is none of them.</summary>
    private void LivePositionWatch(IGameSessionView session, double clock)
    {
        var live = session.Live;
        if (!live.HasBodies || session.State.Self.Position is not { } here)
        {
            return;
        }

        LivePositionOwnOrders(session, live, clock, in here);
        if (_attackers.Count == 0 && _target == 0)
        {
            return;
        }

        var targetIsAttacker = false;
        for (var i = 0; i < _attackers.Count; i++)
        {
            var isTarget = _attackers[i].Guid == _target;
            targetIsAttacker |= isTarget;
            LivePositionMeasure(session, live, clock, in here, _attackers[i].Guid, isTarget ? "attacker and target" : "attacker");
        }

        if (_target != 0 && !targetIsAttacker)
        {
            LivePositionMeasure(session, live, clock, in here, _target, "target");
        }
    }

    private void LivePositionMeasure(IGameSessionView session, LivePositions live, double clock, in CellFrame here, uint guid, string role)
    {
        if (!session.State.Objects.TryGet(guid, out var o) || live.Read(o) is not { Body: { } body, Report: { } report } read)
        {
            return;
        }

        LivePositionWire(o, guid, in body, in report, in here, clock);
        LivePositionTruth(o, guid, in body, in report);
        var apart = body.ToGlobal().DistanceTo(report.ToGlobal());
        if (!(apart > LivePositionGapMetres) || (_livePositionLoggedAt.TryGetValue(guid, out var last) && clock - last < LivePositionLogSeconds))
        {
            return;
        }

        if (_livePositionLoggedAt.Count >= LivePositionLoggedMax)
        {
            _livePositionLoggedAt.Clear(); // bounded: the rate starts again
        }

        _livePositionLoggedAt[guid] = clock;
        LivePositionGaps++;
        LivePositionGapMaxMetres = Math.Max(LivePositionGapMaxMetres, apart);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: {o.Name} 0x{guid:X8} ({role}) is drawn {apart:0.0} m from where the server last reported it: {LivePositionRecords(o, in read, clock, in here)} (report-only measure, gap {LivePositionGaps} of this fight)"));
    }

    /// <summary>
    /// The measure's second line (report-only), at each melee or missile blow of an attacker while bodies are drawn: a blow that comes while the fight takes its attacker to stand farther than
    /// <see cref="LivePositionBlowMetres"/> from the mage says that neither record has the creature where the server does (or that the blow was a missile's). It catches what <see cref="LivePositionWatch"/> cannot: body
    /// and report agreeing on a place the creature has left (the body walks back to the server's last position when its stick ends: <c>TODO(m6-sticky-walk-back)</c>, <c>AvatarStickyFollowTests</c>). At most once per
    /// attacker every <see cref="LivePositionLogSeconds"/>, counted (<c>live_position_far_blows=</c>).
    /// </summary>
    private void LivePositionBlow(Attacker a, double now, in CellFrame mage)
    {
        if (_view is not { Live.HasBodies: true } view || now - a.FarBlowLoggedAt < LivePositionLogSeconds || !view.State.Objects.TryGet(a.Guid, out var o) || view.Live.Read(o) is not { At: { } at } read)
        {
            return;
        }

        var away = at.ToGlobal().DistanceTo(mage.ToGlobal());
        if (!(away > LivePositionBlowMetres))
        {
            return;
        }

        a.FarBlowLoggedAt = now;
        LivePositionFarBlows++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"m6-live-position: {a.Name} 0x{a.Guid:X8} struck the mage ({a.Heard}) while the fight takes it to stand {away:0.0} m from her: {LivePositionRecords(o, in read, now, in mage)}; a melee blow lands from within about {LivePositionBlowMetres:0} m, so neither record has it where the server does unless the blow was a missile's (TODO(m6-sticky-walk-back); report-only measure, far blow {LivePositionFarBlows} of this fight)"));
    }

    /// <summary>Both records of one reading for the measure's lines: the body's and the report's cell and coordinates, each one's metres from the mage, the report's age, where the mage stands and which record the fight reads.</summary>
    private static string LivePositionRecords(WorldObject o, in LiveReading read, double clock, in CellFrame here)
    {
        var mage = here.ToGlobal();
        var age = double.IsNaN(o.PositionAt) ? "n/a" : (clock - o.PositionAt).ToString("0.0", CultureInfo.InvariantCulture);
        var body = read.Body is { } b
            ? string.Create(CultureInfo.InvariantCulture, $"body 0x{b.CellId:X8} [{b.Origin.X:0.0} {b.Origin.Y:0.0} {b.Origin.Z:0.0}] {b.ToGlobal().DistanceTo(mage):0.0} m from the mage")
            : "no body in the world";
        var report = read.Report is { } r
            ? string.Create(CultureInfo.InvariantCulture, $"report 0x{r.CellId:X8} [{r.Origin.X:0.0} {r.Origin.Y:0.0} {r.Origin.Z:0.0}] {r.ToGlobal().DistanceTo(mage):0.0} m from the mage, sent {age} s ago")
            : "no report";
        var reads = read.Source == LiveSource.Body ? "the body" : read.Source == LiveSource.ServerOrder ? "the place a movement order named" : "the server's report (" + read.Fallback + ")";
        return string.Create(CultureInfo.InvariantCulture, $"{body}, {report}; the mage at 0x{here.CellId:X8} [{here.Origin.X:0.0} {here.Origin.Y:0.0} {here.Origin.Z:0.0}]; the fight reads {reads}");
    }
}
