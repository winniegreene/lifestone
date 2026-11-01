using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// The ground's tour over its respawn schedule (M5 item 2 g6, D25, D40, D46, D47), the <see cref="IGroundTour"/> the walks take for a dungeon (<see cref="For"/>). Any ground's: the storeys, their order, the slots, the generators'
/// delays and ticks are the profile's; the rule is Lifestone's own.
/// </summary>
/// <remarks>
/// <para><b>The purposes</b> (F5, D47: the character always moves with purpose). Every offer of <see cref="Next"/> carries a <see cref="TourPurpose"/>, in this order: <see cref="TourPurpose.Return"/> (the timed return),
/// <see cref="TourPurpose.Seen"/> (the nearest living target the object table holds, <see cref="Look"/>; before any presumed slot), <see cref="TourPurpose.Presumed"/> (a slot standing by the schedule, not yet seen: walk to look),
/// <see cref="TourPurpose.Due"/> (a dead slot due within the walk plus <see cref="ReturnMarginSeconds"/>). With none of these the tour holds (<see cref="TourPurpose.Hold"/>, the offer is the player's own cell): where the
/// earliest due slot's cell is in view, or after one walk (purpose <see cref="TourPurpose.Due"/>) to that cell; the hold is logged once with its slot and due time. There is no walk without a purpose.</para>
/// <para><b>The order.</b> The profile's storeys in their order (<see cref="HuntingGround.Storeys"/>, the arrival's first for the den and Tusker Temple), down to the last storey that has a hunted slot and back up, round and round.
/// A presumed waypoint is the nearest cell (by the walk on the room graph) holding a standing hunted slot (<see cref="RespawnSchedule.IsHunted"/>) on the storey the player stands on; when none stands there, the nearest such cell
/// of the next storey in the order that has one.</para>
/// <para><b>Seen empty and looked at</b> (F5). The fight tells the tour what the player sees (<see cref="Look"/>: the player's cell and its visible-cell list from the dat), and the schedule takes the slots in view with no creature
/// of their own as seen empty (<see cref="RespawnSchedule.Look"/>). A cell a leg arrived at is looked at: it is offered again (seen or presumed) only once one of its slots is due or a create is seen in it.</para>
/// <para><b>The loop guard.</b> The same cells offered in a repeating cycle (period 2 or 3, e.g. A, B, A, B) with no kill and no create between them is logged once by name and the tour holds (<see cref="WalkLoops"/>) until a kill, a
/// create, the earliest due slot or <see cref="LoopHoldSeconds"/>.</para>
/// <para><b>The timed return</b> (D25): while the player is off the arrival's storey, the tour turns back for it when that storey's earliest due slot (<see cref="RespawnSchedule.EarliestDueOn"/>) comes due within the walk back
/// plus <see cref="ReturnMarginSeconds"/>; the walk back is the room graph's walk from the player's cell to that slot's cell at <see cref="RunMetresPerSecond"/>, which the tour measures from its own arrived legs' walking time
/// (<see cref="DefaultRunMetresPerSecond"/> until measured), and the waypoint is that slot's cell. On the arrival's storey the tour waits for slots due within the same window before it leaves again, and then goes down; a slot due in
/// the very cell the player stands in is a hold, so the tour does not leave only to be turned back, and <see cref="Returns"/> counts only returns from another storey (2-R3).</para>
/// <para><b><c>--fight-storeys-min</c></b> (<see cref="StoreysMin"/>): after one kill on a storey the tour leaves it for the next until that many distinct storeys are visited (the mage journey's station tours two); <see cref="WantsToLeave"/> tells the fight's target pick, which then prefers targets off the storey (2-R2).</para>
/// <para>A waypoint whose leg failed is not offered for <see cref="FailedCellRestSeconds"/>; within one leg's asking (the same cell and clock) a waypoint is offered once.</para>
/// <para>Game thread only; one instance serves one walk. The fight feeds it (<see cref="Killed"/>, <see cref="Created"/>, <see cref="Look"/>) and sets <see cref="StoreysMin"/>.</para>
/// </remarks>
public sealed class GroundTour : IGroundTour
{
    /// <summary>The run speed the walk back is measured at until the tour has measured one: m5-plan.md section 8's run speed (10.4 m/s, BSD Mage's).</summary>
    public const double DefaultRunMetresPerSecond = 10.4;

    /// <summary>The margin added to the walk back before a slot of the arrival's storey comes due (Lifestone's own: one 60 s tick, about four kills on the way at 14.5 s a kill).</summary>
    public const double ReturnMarginSeconds = 60;

    /// <summary>A waypoint whose leg failed is not offered again for this long (Lifestone's own).</summary>
    public const double FailedCellRestSeconds = 120;

    /// <summary>An arrived leg shorter than this on the room graph does not count toward the measured run speed (the dwell and the turns dominate a short one).</summary>
    public const double MinSpeedSampleMetres = 20;

    /// <summary>F5: the schedule looks at the view at most this often while the player stands in one cell (Lifestone's own; a new cell looks at once).</summary>
    public const double LookIntervalSeconds = 0.25;

    /// <summary>F5: the longest repeating cycle of offers the loop guard knows (A, B, A, B is a period of 2; Lifestone's own).</summary>
    public const int MaxLoopPeriod = 3;

    /// <summary>F5: the longest a hold after a walk loop lasts with no kill, no create and no slot due (Lifestone's own: one tick).</summary>
    public const double LoopHoldSeconds = 60;

    private const string LogCategory = "fight";

    private readonly HuntingGround _ground;
    private readonly Func<uint, uint, double> _metres;
    private readonly Func<uint, IReadOnlyList<uint>?>? _visible;
    private readonly ILogSink _log;
    private readonly bool[] _visited;
    private readonly bool[] _hasHunted;
    private readonly int _arrival;
    private readonly int _waypoints;
    private readonly List<uint> _roundOffered = [];
    private readonly Dictionary<uint, double> _failedAt = [];
    private readonly Dictionary<uint, double> _avoidedAt = []; // M5 7 P7b (D139 g): creatures a stuck walk toward was given up
    private uint _purposeGuid;
    private readonly Dictionary<uint, double> _lookedAt = [];
    private readonly Dictionary<uint, HashSet<uint>> _views = [];
    private readonly List<uint> _history = [];
    private readonly int[] _walksByPurpose = new int[Enum.GetValues<TourPurpose>().Length];
    private HashSet<uint>? _view;
    private uint _viewCell;
    private double _lastLook = double.NaN;
    private WorldObjectTable? _objects;
    private int _storey;
    private int _dir = 1;
    private int _killsHere;
    private uint _roundFrom;
    private double _roundAt = double.NaN;
    private uint? _lastOffer;
    private uint _offerFrom;
    private double _offerAt = double.NaN;
    private double _sampleMetres;
    private double _sampleSeconds;
    private int _samples;
    private TourPurpose _purpose;
    private string _note = string.Empty;
    private double _holdSince = double.NaN;
    private double _holdTotal;
    private double _loopHoldUntil = double.NaN;

    /// <summary>A tour of <paramref name="ground"/>.</summary>
    /// <param name="ground">The ground (a dungeon with storeys; its slots may be none, then the tour holds).</param>
    /// <param name="metres">The walk in metres between two cells on the ground's room graph, or infinity when none joins them (<see cref="RoomMetres"/>; the tests give their own).</param>
    /// <param name="log">Where the tour logs its turns.</param>
    /// <param name="known">The session's object table when the tour is made with one (F4): the creatures already in it are seeded into the schedule (<see cref="RespawnSchedule.Seed(WorldObjectTable, double)"/>). Null: none known.</param>
    /// <param name="now">The session clock, for the seed.</param>
    /// <param name="visible">F5: a cell's visible-cell list from the dat (full ids), or null for none known; the view of a cell is the cell and that list. Null: the view is the player's cell alone.</param>
    public GroundTour(HuntingGround ground, Func<uint, uint, double> metres, ILogSink log, WorldObjectTable? known = null, double now = 0, Func<uint, IReadOnlyList<uint>?>? visible = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(metres);
        ArgumentNullException.ThrowIfNull(log);
        (_ground, _metres, _log, _visible) = (ground, metres, log, visible);
        Schedule = new RespawnSchedule(ground, log, known, now);
        _visited = new bool[ground.Storeys.Count];
        _hasHunted = new bool[ground.Storeys.Count];
        var cells = new HashSet<uint>();
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (Schedule.IsHunted(i) && Schedule.StoreyOf(i) >= 0)
            {
                _hasHunted[Schedule.StoreyOf(i)] = true;
                cells.Add(Schedule.Slot(i).Cell);
            }
        }

        _arrival = ground.StoreyOf(ground.Arrival.Cell);
        _storey = Math.Max(0, _arrival);
        _dir = _arrival >= 0 && _arrival == ground.Storeys.Count - 1 ? -1 : 1;
        _waypoints = Math.Max(1, cells.Count + ground.Tour.Count);
    }

    /// <summary>The ground's tour for a walk over <paramref name="rooms"/>: a <see cref="GroundTour"/> for a dungeon with storeys and generator slots (its view is the room graph's cells' visible-cell lists), else the profile's order (<see cref="ProfileTour"/>).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="rooms">The ground's room graph (the walk back and the nearest cell are measured on it; its cells carry the dat's visible-cell lists).</param>
    /// <param name="log">Where the tour logs.</param>
    /// <param name="known">The session's object table when the tour is made with one (F4); null: the fight seeds the schedule when it starts (<see cref="Seed"/>).</param>
    /// <param name="now">The session clock, for the seed.</param>
    public static IGroundTour For(HuntingGround ground, RoomGraph rooms, ILogSink log, WorldObjectTable? known = null, double now = 0)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(rooms);
        return ground.Kind == GroundKind.Dungeon && ground.Storeys.Count > 0 && ground.Respawn.Slots.Count > 0
            ? new GroundTour(ground, RoomMetres(rooms), log, known, now, cell => rooms.Cells.GetCell(cell)?.VisibleCellIds)
            : new ProfileTour(ground);
    }

    /// <summary>
    /// The walk between two cells on <paramref name="rooms"/>: the room graph's cell path (<see cref="RoomGraph.FindPath"/>) measured from room middle to room middle (<see cref="RoomGraph.RoomFrame"/>), infinity when no path joins
    /// them. Each pair is worked out once and kept (a lower bound of the runner's walk, which the measured run speed absorbs).
    /// </summary>
    /// <param name="rooms">The ground's room graph.</param>
    public static Func<uint, uint, double> RoomMetres(RoomGraph rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var known = new Dictionary<ulong, double>();
        var middles = new Dictionary<uint, Vector3?>();
        Vector3? Middle(uint cell)
        {
            if (!middles.TryGetValue(cell, out var m))
            {
                m = rooms.RoomFrame(cell)?.Origin;
                middles[cell] = m;
            }

            return m;
        }

        return (from, to) =>
        {
            if (from == to)
            {
                return 0;
            }

            var key = from < to ? ((ulong)from << 32) | to : ((ulong)to << 32) | from;
            if (known.TryGetValue(key, out var metres))
            {
                return metres;
            }

            metres = double.PositiveInfinity;
            if (rooms.FindPath(from, to) is { Count: > 0 } path)
            {
                metres = 0;
                Vector3? last = null;
                foreach (var cell in path)
                {
                    if (Middle(cell) is { } here)
                    {
                        metres += last is { } before ? Vector3.Distance(before, here) : 0;
                        last = here;
                    }
                }
            }

            known[key] = metres;
            return metres;
        };
    }

    /// <summary>The respawn schedule the tour reads (fed by <see cref="Killed"/>, <see cref="Created"/> and <see cref="Look"/>).</summary>
    public RespawnSchedule Schedule { get; }

    /// <summary><c>--fight-storeys-min</c>: after one kill on a storey the tour leaves it until this many distinct storeys are visited (1, the default: never).</summary>
    public int StoreysMin { get; set; } = 1;

    /// <summary>The timed returns made (<c>returns=</c>).</summary>
    public int Returns { get; private set; }

    /// <summary>True while the tour heads back for the arrival's storey.</summary>
    public bool Returning { get; private set; }

    /// <summary>The run speed the walk back is measured at: the arrived legs' room-graph metres over their walking seconds, or <see cref="DefaultRunMetresPerSecond"/> until a leg of <see cref="MinSpeedSampleMetres"/> arrived.</summary>
    public double RunMetresPerSecond => _sampleSeconds > 0 ? _sampleMetres / _sampleSeconds : DefaultRunMetresPerSecond;

    /// <summary>The distinct storeys the tour saw the player on (its own view; the fight's <c>storeys_visited=</c> is the body's).</summary>
    public int StoreysVisited { get; private set; }

    /// <summary>
    /// M5 7 P7o: the distinct storeys the BODY stood on, as the fight counts them (<c>storeys_visited=</c>, the field the gate judges), or null when no fight tells it. The leave for <see cref="StoreysMin"/> uses the lower of it and
    /// <see cref="StoreysVisited"/>: a kill made from range on another storey raises the tour's own count, not the body's, so the tour still leaves until the body has stood on that many storeys.
    /// </summary>
    public int? BodyStoreysVisited { get; set; }

    /// <summary>The storeys the leave for <see cref="StoreysMin"/> counts (M5 7 P7o): the lower of the tour's and the body's.</summary>
    private int VisitedForMin => BodyStoreysVisited is { } body ? Math.Min(StoreysVisited, body) : StoreysVisited;

    /// <summary>The index of the arrival's storey, or -1 when the arrival is on none.</summary>
    public int ArrivalStorey => _arrival;

    /// <inheritdoc/>
    public int Waypoints => _waypoints;

    /// <inheritdoc/>
    public TourPurpose Purpose => _purpose;

    /// <inheritdoc/>
    public string PurposeNote => _note;

    /// <inheritdoc/>
    public uint PurposeGuid => _purposeGuid;

    /// <inheritdoc/>
    /// <remarks>The creature is not walked toward (<see cref="TourPurpose.Seen"/>) for <see cref="FailedCellRestSeconds"/>; the fight also rests the leg's cell (<see cref="LegFailed"/>).</remarks>
    public void Avoid(uint guid, double now) => _avoidedAt[guid] = now;

    /// <summary>The holds begun (<c>holds=</c>, F5): each a run of hold offers with no walk, kill or create between them.</summary>
    public int Holds { get; private set; }

    /// <summary>The walk loops the guard caught (<c>walk_loops=</c>, F5, report-only).</summary>
    public int WalkLoops { get; private set; }

    /// <summary>The seconds spent holding (<c>hold_s=</c>, F5): from a hold's first offer to the next walk, kill or target create.</summary>
    /// <param name="now">The session clock (a hold still open counts to it).</param>
    public double HoldSeconds(double now) => _holdTotal + (double.IsNaN(_holdSince) ? 0 : Math.Max(0, now - _holdSince));

    /// <summary>The walks offered with <paramref name="purpose"/> (a same-leg offer that had no walk is not counted).</summary>
    /// <param name="purpose">The purpose.</param>
    public int WalksBy(TourPurpose purpose) => _walksByPurpose[(int)purpose];

    /// <summary>F5's report-only fields: <c>walks_by_purpose=seen:n,presumed:n,due:n,return:n holds=n hold_s=s slots_seen_empty=n walk_loops=n</c>.</summary>
    /// <param name="now">The session clock.</param>
    public string PurposeFields(double now) => string.Create(
        CultureInfo.InvariantCulture,
        $"walks_by_purpose=seen:{WalksBy(TourPurpose.Seen)},presumed:{WalksBy(TourPurpose.Presumed)},due:{WalksBy(TourPurpose.Due)},return:{WalksBy(TourPurpose.Return)} holds={Holds} hold_s={HoldSeconds(now):0} slots_seen_empty={Schedule.SeenEmpty} walk_loops={WalkLoops}");

    /// <inheritdoc/>
    public uint? Next(uint from, double now)
    {
        if (from == _roundFrom && now == _roundAt)
        {
            if (_lastOffer is { } previous)
            {
                _roundOffered.Add(previous); // the same leg asks again: its last offer had no walk
                if (_purpose != TourPurpose.Hold)
                {
                    _walksByPurpose[(int)_purpose]--;
                    if (_history.Count > 0)
                    {
                        _history.RemoveAt(_history.Count - 1);
                    }
                }
            }
        }
        else
        {
            _roundOffered.Clear();
            (_roundFrom, _roundAt) = (from, now);
        }

        Schedule.Expire(now);
        var storey = _ground.StoreyOf(from);
        if (storey >= 0)
        {
            Visit(storey);
        }

        _purposeGuid = 0;
        var offer = Choose(from, now);
        if (_purpose != TourPurpose.Hold)
        {
            _history.Add(offer);
            if (_history.Count > 2 * MaxLoopPeriod)
            {
                _history.RemoveAt(0);
            }
            if (LoopIn(_history) is { } cycle)
            {
                WalkLoops++;
                var due = Schedule.EarliestDueOn(-1);
                _loopHoldUntil = Math.Min(now + LoopHoldSeconds, due >= 0 ? Schedule.DueAt(due) : double.PositiveInfinity);
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"tour: a walk loop: {cycle} offered again with no kill and no create between them; the tour holds until a kill, a create or {_loopHoldUntil:0} s (walk_loops={WalkLoops})"));
                _history.Clear();
                offer = Hold(from, LoopNote(now));
            }
        }

        if (_purpose == TourPurpose.Hold)
        {
            if (double.IsNaN(_holdSince))
            {
                _holdSince = now;
                Holds++;
                _log.Info(LogCategory, $"tour: holding in 0x{from:X8} on '{StoreyName(_ground.StoreyOf(from))}': {_note}");
            }
        }
        else
        {
            EndHold(now);
            _walksByPurpose[(int)_purpose]++;
        }

        (_lastOffer, _offerFrom, _offerAt) = (offer, from, now);
        return offer;
    }

    /// <inheritdoc/>
    public void LegFailed(uint cell, double now) => _failedAt[cell] = now;

    /// <inheritdoc/>
    public void LegArrived(uint cell, double now) => LegWalked(cell, now, double.NaN);

    /// <inheritdoc/>
    /// <remarks>The speed sample is the leg's room-graph metres over <paramref name="walkSeconds"/> (the runner's own walking time); a walk that does not give one (NaN) is timed from the offer to the arrival.</remarks>
    public void LegWalked(uint cell, double now, double walkSeconds)
    {
        if (_lastOffer != cell || double.IsNaN(_offerAt))
        {
            return;
        }

        _lookedAt[cell] = now; // F5: not offered again until one of its slots is due or a create is seen in it
        var metres = _metres(_offerFrom, cell);
        var walked = double.IsFinite(walkSeconds) && walkSeconds > 0;
        var seconds = walked ? walkSeconds : now - _offerAt;
        _offerAt = double.NaN;
        if (double.IsFinite(metres) && metres >= MinSpeedSampleMetres && seconds > 0)
        {
            _sampleMetres += metres;
            _sampleSeconds += seconds;
            if (++_samples == 1)
            {
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"tour: the run speed measured on the first leg: {RunMetresPerSecond:0.0} m/s ({metres:0} m on the room graph in {seconds:0.0} s {(walked ? "of the runner's own walking" : "from the offer to the arrival")}; {DefaultRunMetresPerSecond:0.0} until now)"));
            }
        }
    }

    /// <inheritdoc/>
    public void Killed(uint wcid, uint cell, double now, uint guid = 0)
    {
        Schedule.Killed(wcid, cell, now, guid);
        Progress(now);
        var storey = _ground.StoreyOf(cell);
        if (storey >= 0)
        {
            Visit(storey);
            if (storey == _storey)
            {
                _killsHere++;
            }
        }
    }

    /// <inheritdoc/>
    public void Created(uint wcid, uint cell, double now, uint guid = 0)
    {
        Schedule.Created(wcid, cell, now, guid);
        if (_ground.RoleOf(wcid) == GroundRole.Target)
        {
            _lookedAt.Remove(cell); // F5: a create seen in a looked-at cell makes it offerable again
            Progress(now);
        }
    }

    /// <inheritdoc/>
    public void Seed(WorldObjectTable objects, double now) => Schedule.Seed(objects, now);

    /// <inheritdoc/>
    /// <remarks>Looks at once in a new cell, else at most every <see cref="LookIntervalSeconds"/>; allocates only the first time it sees a cell (its view set).</remarks>
    public void Look(uint cell, WorldObjectTable objects, double now)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects;
        if (_view is null || cell != _viewCell)
        {
            _view = ViewOf(cell);
            _viewCell = cell;
        }
        else if (now - _lastLook < LookIntervalSeconds)
        {
            return;
        }

        _lastLook = now;
        Schedule.Look(_view, objects, now);
    }

    /// <summary>True when <paramref name="cell"/> is in the view from <paramref name="from"/>: the cell itself or one of its visible-cell list (F5).</summary>
    /// <param name="from">The player's cell.</param>
    /// <param name="cell">The cell looked for.</param>
    public bool InView(uint from, uint cell) => from == cell || ViewOf(from).Contains(cell);

    /// <summary>
    /// 2-R2: true when the tour wants the player off the storey of <paramref name="cell"/> now: it is the storey the tour last saw the player on, a kill was made here, fewer storeys than <see cref="StoreysMin"/> are visited, the tour
    /// is not heading back for the arrival, and the next storey in the order has a standing hunted slot the tour could offer. The fight's target pick then prefers targets off this storey (the next one first), so the second kill is
    /// taken on another storey and the walk really leaves. False once <see cref="StoreysMin"/> storeys are visited, and false with no storey to go to (the pick then keeps to this one).
    /// </summary>
    /// <param name="cell">The cell the player stands in.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="next">The index of the storey the tour would go to (the pick prefers its targets), or -1.</param>
    public bool WantsToLeave(uint cell, double now, out int next)
    {
        next = -1;
        var storey = _ground.StoreyOf(cell);
        if (storey < 0 || storey != _storey || Returning || VisitedForMin >= StoreysMin || _killsHere < 1)
        {
            return false;
        }

        var peek = NextStorey(storey, _dir, cell, now, out _);
        if (peek < 0)
        {
            return false;
        }

        next = peek;
        return true;
    }

    /// <summary>
    /// M5 7 P7o (D44 c2b): the storey the tour would take the player to next from <paramref name="cell"/> (the next storey in the order with a standing hunted slot it could offer, as <see cref="WantsToLeave"/> peeks), or -1 when no other
    /// storey is reachable. The fight holds its win on it while fewer than <see cref="StoreysMin"/> storeys are visited.
    /// </summary>
    /// <param name="cell">The cell the player stands in.</param>
    /// <param name="now">The session clock.</param>
    public int OtherStorey(uint cell, double now)
    {
        var storey = _ground.StoreyOf(cell);
        return NextStorey(storey >= 0 ? storey : _storey, _dir, cell, now, out _);
    }

    private HashSet<uint> ViewOf(uint cell)
    {
        if (!_views.TryGetValue(cell, out var view))
        {
            view = [cell];
            if (_visible?.Invoke(cell) is { } list)
            {
                view.UnionWith(list);
            }

            _views[cell] = view;
        }

        return view;
    }

    private void Visit(int storey)
    {
        if (!_visited[storey])
        {
            _visited[storey] = true;
            StoreysVisited++;
        }

        if (storey == _storey)
        {
            return;
        }

        _storey = storey;
        _killsHere = 0;
        if (Returning && storey == _arrival)
        {
            Returning = false;
            _dir = _arrival == _ground.Storeys.Count - 1 ? -1 : 1;
            _log.Info(LogCategory, $"tour: back on the arrival's storey '{_ground.Storeys[storey].Name}'");
        }
    }

    /// <summary>A kill or a target's create: the loop guard starts again and a hold ends.</summary>
    private void Progress(double now)
    {
        _history.Clear();
        _loopHoldUntil = double.NaN;
        EndHold(now);
    }

    private void EndHold(double now)
    {
        if (!double.IsNaN(_holdSince))
        {
            _holdTotal += Math.Max(0, now - _holdSince);
            _holdSince = double.NaN;
        }
    }

    private uint Offer(uint cell, TourPurpose purpose, string note)
    {
        (_purpose, _note) = (purpose, note);
        return cell;
    }

    private uint Hold(uint from, string note) => Offer(from, TourPurpose.Hold, note);

    private uint Choose(uint from, double now)
    {
        var here = _storey;
        if (_arrival >= 0 && here != _arrival && Returned(from, now) is { } back)
        {
            return Offer(back, TourPurpose.Return, _note);
        }

        if (now < _loopHoldUntil)
        {
            return Hold(from, LoopNote(now));
        }

        var leaveForMin = VisitedForMin < StoreysMin && _killsHere >= 1 && NextStorey(here, _dir, from, now, out _) >= 0;
        if (SeenTarget(from, now, leaveForMin ? here : -1) is { } seen)
        {
            return seen;
        }

        var near = NearestStanding(from, here, now);
        if (near is { } presumed && !leaveForMin)
        {
            return Offer(presumed, TourPurpose.Presumed, PresumedNote(presumed));
        }

        if (here == _arrival && DueSoon(from, here, now) is { } wait)
        {
            // 2-R3: back in time, wait on the arrival's storey for its slots; a slot due in the very cell the player stands in is a hold, not a leave to be turned straight back
            return wait == from
                ? Hold(from, $"{DueNote(SlotDueIn(wait), now)}; it is due within the {ReturnMarginSeconds:0} s margin (2-R3: no leaving to be turned back)")
                : Offer(wait, TourPurpose.Due, DueNote(SlotDueIn(wait), now));
        }

        var next = NextStorey(here, _dir, from, now, out var dir);
        if (next >= 0 && NearestStanding(from, next, now) is { } there)
        {
            _dir = dir;
            if (next != here)
            {
                _log.Info(LogCategory, $"tour: leaving storey '{_ground.Storeys[here].Name}' for '{_ground.Storeys[next].Name}' ({(near is null ? "no hunted slot stands here that is not looked at" : $"one kill here; {VisitedForMin} of {StoreysMin} storeys visited")})");
            }

            return Offer(there, TourPurpose.Presumed, PresumedNote(there));
        }

        if (near is { } stay)
        {
            return Offer(stay, TourPurpose.Presumed, PresumedNote(stay)); // --fight-storeys-min with no other storey standing
        }

        if (DueSoon(from, -1, now) is { } soon)
        {
            return soon == from || InView(from, soon) ? Hold(from, DueNote(SlotDueIn(soon), now) + "; in view") : Offer(soon, TourPurpose.Due, DueNote(SlotDueIn(soon), now));
        }

        // D47: no purpose to walk. Hold where the earliest due slot is in view; else walk once to its cell (purpose due) and hold there.
        var first = Schedule.EarliestDueOn(-1);
        if (first < 0)
        {
            return Hold(from, "no hunted slot is dead and none standing is left to look at (every standing one is looked at)");
        }

        var cell = Schedule.Slot(first).Cell;
        return cell == from || InView(from, cell) || !Offerable(cell, from, now, looked: false)
            ? Hold(from, DueNote(first, now) + (InView(from, cell) ? "; in view" : "; its cell is not offerable now"))
            : Offer(cell, TourPurpose.Due, DueNote(first, now) + "; the walk to where it is in view before the hold");
    }

    /// <summary>The timed return: the cell to head for when the arrival's storey's earliest due slot comes due within the walk back plus the margin, or null. Sets the note.</summary>
    private uint? Returned(uint from, double now)
    {
        var first = Schedule.EarliestDueOn(_arrival);
        if (first < 0)
        {
            if (Returning)
            {
                _note = $"the timed return {Returns}: back to the arrival's storey '{StoreyName(_arrival)}'";
                return NearestStanding(from, _arrival, now) ?? NearestAny(from, _arrival, now);
            }

            return null;
        }

        var cell = Schedule.Slot(first).Cell;
        var walk = _metres(from, cell) / RunMetresPerSecond;
        var due = Schedule.DueAt(first);
        if (!Returning && _storey != _arrival && double.IsFinite(walk) && due - now <= walk + ReturnMarginSeconds) // 2-R3: a return is from another storey, never a wait on the arrival's
        {
            Returning = true;
            Returns++;
            _log.Info(
                LogCategory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"tour: the timed return {Returns}: slot {first} in 0x{cell:X8} on '{_ground.Storeys[_arrival].Name}' is due at {due:0} s, in {due - now:0} s; the walk back is {walk:0} s at {RunMetresPerSecond:0.0} m/s plus a {ReturnMarginSeconds:0} s margin{(Schedule.PhaseKnown(first) ? string.Empty : " (phase unknown)")}"));
        }

        if (!Returning)
        {
            return null;
        }

        _note = $"the timed return {Returns}: " + DueNote(first, now);
        return Offerable(cell, from, now, looked: false) ? cell : NearestStanding(from, _arrival, now) ?? NearestAny(from, _arrival, now);
    }

    /// <summary>F5: the nearest living target the object table holds (<see cref="Look"/>'s table), on a walkable cell of the ground that is offerable, off storey <paramref name="notOn"/> when it is not -1; null for none.</summary>
    private uint? SeenTarget(uint from, double now, int notOn)
    {
        if (_objects is null)
        {
            return null;
        }

        WorldObject? best = null;
        var bestCell = 0u;
        var bestMetres = double.PositiveInfinity;
        foreach (var o in _objects.All)
        {
            if (o.Position is not { } at || !_ground.IsTarget(o.Weenie.WeenieClassId, at.CellId) || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord }
                || (_avoidedAt.TryGetValue(o.Guid, out var avoided) && now - avoided < FailedCellRestSeconds))
            {
                continue;
            }

            var cell = at.CellId;
            if ((notOn >= 0 && _ground.StoreyOf(cell) == notOn) || !Offerable(cell, from, now))
            {
                continue;
            }

            var metres = _metres(from, cell);
            if (double.IsFinite(metres) && metres < bestMetres)
            {
                (best, bestCell, bestMetres) = (o, cell, metres);
            }
        }

        _purposeGuid = best?.Guid ?? 0;
        return best is null ? null : Offer(bestCell, TourPurpose.Seen, string.Create(CultureInfo.InvariantCulture, $"a living {best.Name} 0x{best.Guid:X8} in 0x{bestCell:X8}, {bestMetres:0} m on the room graph"));
    }

    /// <summary>
    /// The nearest cell of a dead slot due within the walk to it plus the margin on <paramref name="storey"/> (-1: any storey), or null. The cell the player stands in counts (2-R3): a slot due there within the margin is a hold.
    /// </summary>
    private uint? DueSoon(uint from, int storey, double now)
    {
        uint? best = null;
        var bestMetres = double.PositiveInfinity;
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (!Schedule.IsHunted(i) || Schedule.IsStanding(i) || (storey >= 0 && Schedule.StoreyOf(i) != storey))
            {
                continue;
            }

            var cell = Schedule.Slot(i).Cell;
            var metres = _metres(from, cell);
            if (Offerable(cell, from, now, allowHere: true, looked: false) && Schedule.DueAt(i) - now <= (metres / RunMetresPerSecond) + ReturnMarginSeconds && metres < bestMetres)
            {
                (best, bestMetres) = (cell, metres);
            }
        }

        return best;
    }

    /// <summary>The dead hunted slot of <paramref name="cell"/> due first, or -1.</summary>
    private int SlotDueIn(uint cell)
    {
        var best = -1;
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (Schedule.IsHunted(i) && !Schedule.IsStanding(i) && Schedule.Slot(i).Cell == cell && (best < 0 || Schedule.DueAt(i) < Schedule.DueAt(best)))
            {
                best = i;
            }
        }

        return best;
    }

    private string DueNote(int slot, double now)
    {
        if (slot < 0)
        {
            return "no slot is due";
        }

        var s = Schedule.Slot(slot);
        var due = Schedule.DueAt(slot);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"slot {slot} (wcid {s.Wcid} in 0x{s.Cell:X8}) is due at {due:0} s, in {due - now:0} s{(Schedule.IsSeenEmpty(slot) ? " (at the latest: seen empty)" : string.Empty)}");
    }

    private string PresumedNote(uint cell)
    {
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (Schedule.IsHunted(i) && Schedule.IsStanding(i) && Schedule.Slot(i).Cell == cell)
            {
                return $"slot {i} (wcid {Schedule.Slot(i).Wcid} in 0x{cell:X8}) stands by the schedule and is not yet seen";
            }
        }

        return $"a slot in 0x{cell:X8} stands by the schedule";
    }

    private string LoopNote(double now)
    {
        var first = Schedule.EarliestDueOn(-1);
        return "after a walk loop, until a kill or a create" + (first >= 0 ? "; " + DueNote(first, now) : string.Empty);
    }

    private string StoreyName(int storey) => storey >= 0 ? _ground.Storeys[storey].Name : "no storey";

    /// <summary>The last offers form a repeating cycle of period 2 to <see cref="MaxLoopPeriod"/> (each period twice in a row); the cycle's text, or null.</summary>
    private static string? LoopIn(List<uint> history)
    {
        for (var p = 2; p <= MaxLoopPeriod; p++)
        {
            if (history.Count < 2 * p)
            {
                break;
            }

            var repeats = true;
            for (var k = 0; k < p && repeats; k++)
            {
                repeats = history[history.Count - 1 - k] == history[history.Count - 1 - p - k];
            }

            if (repeats)
            {
                return string.Join(", ", history.Skip(history.Count - (2 * p)).Select(c => $"0x{c:X8}"));
            }
        }

        return null;
    }

    private uint? NearestStanding(uint from, int storey, double now) => Nearest(from, storey, now, standingOnly: true);

    private uint? NearestAny(uint from, int storey, double now) => Nearest(from, storey, now, standingOnly: false);

    private uint? Nearest(uint from, int storey, double now, bool standingOnly)
    {
        uint? best = null;
        var bestMetres = double.PositiveInfinity;
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (!Schedule.IsHunted(i) || (standingOnly && !Schedule.IsStanding(i)) || Schedule.StoreyOf(i) != storey)
            {
                continue;
            }

            var cell = Schedule.Slot(i).Cell;
            if (!Offerable(cell, from, now, looked: standingOnly))
            {
                continue;
            }

            var metres = _metres(from, cell);
            if (metres < bestMetres)
            {
                (best, bestMetres) = (cell, metres);
            }
        }

        return best;
    }

    /// <summary>The next storey in the order (down, then back up) after <paramref name="here"/> from direction <paramref name="dir"/> that has a standing hunted slot the tour could offer, or -1; <paramref name="turned"/> is the direction after it.</summary>
    private int NextStorey(int here, int dir, uint from, double now, out int turned)
    {
        var n = _ground.Storeys.Count;
        var at = here;
        for (var steps = 0; steps < 2 * n; steps++)
        {
            var next = at + dir;
            if (next < 0 || next >= n)
            {
                dir = -dir;
                continue;
            }

            at = next;
            if (at != here && _hasHunted[at] && NearestStanding(from, at, now) is not null)
            {
                turned = dir;
                return at;
            }
        }

        turned = dir;
        return -1;
    }

    /// <summary>
    /// True when <paramref name="cell"/> may be offered: not the player's cell (unless <paramref name="allowHere"/>), not offered already in this leg's asking, not resting after a failed leg, and (with <paramref name="looked"/>) not looked at
    /// (F5: a cell a leg arrived at is offered again only once one of its slots is due or a create is seen in it).
    /// </summary>
    private bool Offerable(uint cell, uint from, double now, bool allowHere = false, bool looked = true) =>
        (allowHere || cell != from) && !_roundOffered.Contains(cell) && !(_failedAt.TryGetValue(cell, out var failed) && now - failed < FailedCellRestSeconds) && !(looked && LookedAt(cell, now));

    private bool LookedAt(uint cell, double now)
    {
        if (!_lookedAt.ContainsKey(cell))
        {
            return false;
        }

        for (var i = 0; i < Schedule.Count; i++)
        {
            if (Schedule.Slot(i).Cell == cell && !Schedule.IsStanding(i) && Schedule.DueAt(i) <= now)
            {
                _lookedAt.Remove(cell); // one of its slots is due: look again
                return false;
            }
        }

        return true;
    }
}
