using System.Globalization;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Where a leg of the fight loop's walk stands (<see cref="IFightWalk.Tick"/>).</summary>
public enum FightWalkStatus
{
    /// <summary>No leg is being walked.</summary>
    Idle,

    /// <summary>A leg is being walked.</summary>
    Walking,

    /// <summary>The leg's last waypoint was reached (reported once; the walk is then idle).</summary>
    Arrived,

    /// <summary>The leg failed (<see cref="IFightWalk.Failure"/> says why).</summary>
    Failed,
}

/// <summary>
/// The walk of the fight loop (M3 3.1 lane fa): a body that follows the server's view of the player and walks it along the ground's walkable cells, one leg at a time, to the next waypoint. The loop
/// (<see cref="FightScenario"/>) only asks for legs; <see cref="GroundWalk"/> is the real one (physics and the dats), the tests give a scripted one. Game thread only.
/// </summary>
public interface IFightWalk : IDisposable
{
    /// <summary>The reason a leg failed, or null.</summary>
    string? Failure { get; }

    /// <summary>Makes the body and starts following the server's positions of the player; call before the teleport to the den, once (a second call does nothing).</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Prepare(IGameSessionView session, double now);

    /// <summary>Plans and starts the leg to the next waypoint of the floor from where the player stands.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    /// <param name="description">What the leg is (the waypoint's cell and the doorways), for the log.</param>
    /// <returns>False with <see cref="Failure"/> set when no leg can be planned.</returns>
    bool BeginLeg(IGameSessionView session, double now, out string description);

    /// <summary>One frame: applies the server's positions of the player, ticks the body and the leg. Call every frame after <see cref="Prepare"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    FightWalkStatus Tick(IGameSessionView session, double now);

    /// <summary>Drops the leg and releases every key.</summary>
    void Abandon();

    /// <summary>
    /// True when a bolt flying from <paramref name="from"/> (the caster's feet) to <paramref name="to"/> (the target's feet) meets no wall (M3 4.2 P4: ACE and retail check no line, so a cast through rock explodes on the wall).
    /// The default is true (a walker without physics, the tests' scripted ones, keeps the nearest pick).
    /// </summary>
    /// <param name="from">The caster's position.</param>
    /// <param name="to">The target's position.</param>
    bool HasClearLine(in CellFrame from, in CellFrame to) => true;

    /// <summary>The tour the walk takes its waypoints from (M5 item 2 g6: the fight feeds it the kills and the creates and reads its fields), or null for a walk without one (the tests' scripted walks). The default is null.</summary>
    IGroundTour? Tour => null;

    /// <summary>The failed legs the walk abandoned and went on from (<c>m5-fight-walk-recover</c>; 2-R4: the fight's <c>legs_recovered=</c>), or null for a walk that does not count them (<c>n/a</c> on the line). The default is null.</summary>
    int? LegsRecovered => null;

    /// <summary>
    /// M5 7 P7b (D139 g): the stuck rungs of the leg under way in its current stuck episode (the runner's <see cref="NavRunner.EpisodeRungs"/>; 0 with no leg, in a hold, or once the body got past); the fight gives a leg up at
    /// <see cref="FightScenario.WalkStuckRungsGiveUp"/>. The default is 0 (a walk without a runner never reports one).
    /// </summary>
    int LegStuckRungs => 0;

    /// <summary>The cell the leg under way goes to (a tour's waypoint or a spot), or 0 with no leg. The default is 0.</summary>
    uint LegGoalCell => 0;

    /// <summary>
    /// M5 7 P7b (D139 j): plans and starts a leg to <paramref name="goal"/> itself, not to a tour's waypoint (the loot step's walk back to where a corpse lost from view lay). The leg ends Arrived at the spot (within the walk's stop
    /// distance) or, when it fails, Arrived without recovery onto the tour (its failure is logged): the fight reads where the player stands. The default plans nothing: false, with <paramref name="description"/> naming why.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    /// <param name="goal">The spot.</param>
    /// <param name="description">What the leg is, for the log; or why none was planned.</param>
    bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description)
    {
        description = "TODO(m5-loot-walk-back-walk): this walk has no leg to a spot of its own";
        return false;
    }

    /// <summary>
    /// M5 7 P7i: makes the floor round <paramref name="at"/> costly for the walk's planner until <paramref name="until"/> (the fight's clock; <see cref="RoomPatches"/>), so its plans go round the spot where walks stuck while
    /// another way exists. The default marks nothing: false, with <paramref name="description"/> naming why.
    /// </summary>
    /// <param name="at">The spot.</param>
    /// <param name="until">When the patch ends, on the clock the walk's legs are given.</param>
    /// <param name="description">What was marked, for the log; or why nothing was.</param>
    bool AvoidSpot(in CellFrame at, double until, out string description)
    {
        description = "TODO(m5-stuck-spot-patch): this walk has no planner to mark";
        return false;
    }

    /// <summary>
    /// M5 7 P7s (D148, <c>m5-8-loot-no-go-corpse</c>): the cells a straight walk from <paramref name="from"/> to <paramref name="to"/> passes through, in order (the start's cell first), into <paramref name="cells"/> (cleared first):
    /// the line ACE's own MoveTo takes when a Use of an object beyond reach walks the player to it. <see cref="LineTrace.NotTraced"/> for a walk that has no physics to trace with (the default; the tests' scripted walks);
    /// <see cref="LineTrace.Stopped"/> when the sweep met something on the way, so the cells past it are not known.
    /// </summary>
    /// <param name="from">Where the player stands.</param>
    /// <param name="to">Where the walk ends (a corpse).</param>
    /// <param name="cells">Filled with the cells crossed.</param>
    LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        cells.Clear();
        return LineTrace.NotTraced;
    }

    /// <summary>
    /// M5 7 P7s (D148, <c>m5-8-no-go-attacker</c>): the middle of each room the walk's planner may walk to (the ground's walkable cells), the candidate spots for a short step. The default is none (a walk without a planner).
    /// </summary>
    IReadOnlyList<CellFrame> FloorSpots => [];
}

/// <summary>M5 7 P7s (D148): what <see cref="IFightWalk.CellsOnLine"/> could tell.</summary>
public enum LineTrace
{
    /// <summary>The walk has no physics to trace a line with.</summary>
    NotTraced,

    /// <summary>The sweep met something before the end: the cells past it are unknown.</summary>
    Stopped,

    /// <summary>Every cell of the line is known.</summary>
    Traced,
}

/// <summary>
/// <c>m5-fight-walk-recover</c>: the count of failed legs in a row that every ground walk shares (the runner's <see cref="RunnerLegs"/> and the journey host's den walk). A failed leg is abandoned and the walk goes on to the next
/// waypoint; <see cref="MaxFailedLegsInARow"/> in a row (an arrival resets the count) end the walk with a failure that names each leg's reason. Lifestone's own number, a named constant because the profile's walk section is g1's record.
/// </summary>
/// <param name="log">Where a recovery is logged.</param>
public sealed class LegRecovery(ILogSink log)
{
    /// <summary>The failed legs in a row after which the walk fails (Lifestone's own).</summary>
    public const int MaxFailedLegsInARow = 3;

    private readonly List<string> _reasons = [];

    /// <summary>The failed legs since the last arrival.</summary>
    public int FailedInARow { get; private set; }

    /// <summary>The legs abandoned that the walk went on from.</summary>
    public int Recovered { get; private set; }

    /// <summary>A leg arrived: the failed legs in a row start again from none.</summary>
    public void Arrived()
    {
        FailedInARow = 0;
        _reasons.Clear();
    }

    /// <summary>A leg failed.</summary>
    /// <param name="leg">The leg's number, for the log.</param>
    /// <param name="goalCell">The cell of the waypoint the leg was going to.</param>
    /// <param name="reason">Why it failed.</param>
    /// <returns>Null when the walk goes on to the next waypoint; else the named failure that ends it.</returns>
    public string? Failed(int leg, uint goalCell, string reason)
    {
        FailedInARow++;
        _reasons.Add($"leg {leg} to 0x{goalCell:X8}: {reason}");
        if (FailedInARow >= MaxFailedLegsInARow)
        {
            return $"{FailedInARow} legs failed in a row (" + string.Join("; ", _reasons) + ")";
        }

        Recovered++;
        log.Warn("fight", $"m5-fight-walk-recover: leg {leg} to 0x{goalCell:X8} abandoned ({reason}); {FailedInARow} of {MaxFailedLegsInARow} failed in a row, going on to the next waypoint");
        return null;
    }
}

/// <summary>
/// The legs of a ground walk on a <see cref="NavRunner"/> (M5 item 2 g5), shared by the headless fight's <see cref="GroundWalk"/> and the game's <see cref="GameFightWalk"/>: each leg goes to the next waypoint the ground's
/// <see cref="IGroundTour"/> offers (a waypoint no walk of the room graph joins to the player is skipped, named), as a point goal at the middle of the waypoint's room within the profile's stop distance
/// (<see cref="WalkModel.StopWithinMetres"/>), and ends in the profile's dwell (<see cref="WalkModel.StopDwellSeconds"/>) once the runner arrived.
/// </summary>
/// <remarks>
/// <para><c>m5-fight-walk-recover</c>: a leg that fails (the runner <see cref="NavRunState.Blocked"/> after its stuck ladder, <see cref="NavRunState.Failed"/> by a move of the player no run caused, or over the profile's
/// <see cref="WalkModel.LegSeconds"/>) is abandoned and the walk goes on to the next waypoint from where the player stands, so one stall near a goal no longer ends a fight. After <see cref="MaxFailedLegsInARow"/> failed legs in a
/// row (an arrival resets the count) the walk fails, naming each leg's reason. A person's movement key (<see cref="NavRunState.Interrupted"/>) is not a failed leg: the player's keys win (R6) and the walk fails at once. Entering a
/// no-go cell is the walk's own stop and never reaches here (<see cref="GroundWalk.NoGoReason"/>).</para>
/// <para>The number three is Lifestone's own (retail has no such walk); it is a named constant, not a profile value, because the profile's walk section (<see cref="WalkModel"/>) is g1's record.</para>
/// </remarks>
public sealed class RunnerLegs
{
    /// <summary>The failed legs in a row after which the walk fails (<see cref="LegRecovery.MaxFailedLegsInARow"/>).</summary>
    public const int MaxFailedLegsInARow = LegRecovery.MaxFailedLegsInARow;

    private const string LogCategory = "fight";

    private readonly HuntingGround _ground;
    private readonly IGroundTour _tour;
    private readonly RoomGraph _rooms;
    private readonly ILogSink _log;
    private readonly LegRecovery _recovery;
    private readonly Func<uint, CellFrame?> _roomFrame;
    private NavRunner? _runner;
    private bool _active;
    private uint _goalCell;
    private double _legStart;
    private double _dwellUntil = double.NaN;
    private readonly LegPacing _pacing = new();
    private LegMark _mark;
    private bool _spot; // M5 7 P7b (D139 j): the leg goes to a spot of the caller's, not to a tour's waypoint

    /// <summary>F5: a hold (the tour's purpose <see cref="TourPurpose.Hold"/>: no leg) lasts this long before the walk reports it arrived and the fight looks for a target and asks the tour again (Lifestone's own).</summary>
    public const double HoldRecheckSeconds = 1.0;

    /// <summary>True while the walk holds (F5): the tour named no purpose to walk, so the player stands; <see cref="Step"/> answers Walking until <see cref="HoldRecheckSeconds"/> pass, then Arrived.</summary>
    public bool Holding => _pacing.Holding;

    /// <summary>The runner's walking seconds of the leg being walked or last walked (F5: the frames it was running, from its start; the time it was paused or yielding to the server's MoveTo left out).</summary>
    public double LegWalkSeconds => _pacing.WalkSeconds;

    /// <summary>
    /// F5: the leg's own report. One <see cref="NavRunner"/> serves every leg and its counters run over its life, so the leg's line takes the differences since the leg began: <c>route_m= run_s= walk_s= speed_mps= plans=
    /// replans= rungs= blocked= turns= key_changes_per_min= creature_replans=</c> are this leg's (speed over the walking seconds); <c>creatures_max_walk= creature_min_m_walk=</c> are the runner's over the whole walk (a maximum and a
    /// minimum have no difference). No audit runs on these walks, so the runner's <c>corrections=</c> and <c>where_max_m=</c> (always <c>none</c> here) are left off.
    /// </summary>
    /// <param name="runner">The leg's runner.</param>
    public string LegFields(NavRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var metres = runner.RouteMetres - _mark.Metres;
        var runSeconds = runner.RunSeconds;
        var keys = runner.KeyChanges - _mark.Keys;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"route_m={metres:0.0} run_s={runSeconds:0.0} walk_s={_pacing.WalkSeconds:0.0} speed_mps={(_pacing.WalkSeconds > 0 ? metres / _pacing.WalkSeconds : 0):0.00} plans={runner.Plans - _mark.Plans} replans={runner.Replans - _mark.Replans} rungs={runner.Rungs - _mark.Rungs} blocked={(runner.State == NavRunState.Blocked ? 1 : 0)} turns={runner.Turns - _mark.Turns} key_changes_per_min={(runSeconds >= 1.0 ? keys * 60.0 / runSeconds : 0):0} creature_replans={runner.CreatureReplans - _mark.CreatureReplans} creatures_max_walk={runner.CreaturesMax} creature_min_m_walk={(runner.CreatureMinMetres is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) : "none")}");
    }

    /// <summary>The runner's life counters when a leg began (F5: the leg's report is their differences).</summary>
    private readonly record struct LegMark(float Metres, int Plans, int Replans, int Rungs, int Turns, int Keys, int CreatureReplans)
    {
        public static LegMark Of(NavRunner runner) => new(runner.RouteMetres, runner.Plans, runner.Replans, runner.Rungs, runner.Turns, runner.KeyChanges, runner.CreatureReplans);
    }

    /// <summary>The legs of a walk over <paramref name="rooms"/>.</summary>
    /// <param name="ground">The ground (its walk section).</param>
    /// <param name="tour">Where the next waypoint comes from.</param>
    /// <param name="rooms">The ground's room graph (the runner's path source is over it).</param>
    /// <param name="log">Where the legs log.</param>
    /// <param name="roomFrame">A waypoint's goal, the middle of its room; null: the room graph's (<see cref="RoomGraph.RoomFrame"/>). A test with rooms that have no floor polygons gives its own.</param>
    public RunnerLegs(HuntingGround ground, IGroundTour tour, RoomGraph rooms, ILogSink log, Func<uint, CellFrame?>? roomFrame = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(tour);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(log);
        (_ground, _tour, _rooms, _log) = (ground, tour, rooms, log);
        _recovery = new LegRecovery(log);
        _roomFrame = roomFrame ?? rooms.RoomFrame;
        foreach (var region in ground.AvoidRegions)
        {
            // M5 7 P7i (D144): the ground's seeded avoid regions, costly boxes of the room graph for good (and kept out of the stuck ladder's moves)
            if ((uint)region.Landblock << 16 == rooms.LandblockId && rooms.Patches.AddBox(region.Min, region.Max, region.Name))
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walk: avoid region seeded: {region.Name} [{region.Min.X:0.#}..{region.Max.X:0.#} {region.Min.Y:0.#}..{region.Max.Y:0.#}] in 0x{region.Landblock:X4} costs the room graph's plans {RoomPatches.SeededCostMetres:0} m more (D144)"));
            }
        }
    }

    /// <summary>The reason the walk failed, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far, recovery legs included.</summary>
    public int Legs { get; private set; }

    /// <summary>The legs abandoned that the walk went on from.</summary>
    public int Recovered => _recovery.Recovered;

    /// <summary>The failed legs since the last arrival.</summary>
    public int FailedInARow => _recovery.FailedInARow;

    /// <summary>True while a leg is being walked.</summary>
    public bool Active => _active;

    /// <summary>Where the next waypoint comes from.</summary>
    public IGroundTour Tour => _tour;

    /// <summary>The runner of the leg being walked or last walked, or null.</summary>
    public NavRunner? Runner => _runner;

    /// <summary>M5 7 P7b (D139 g): the stuck rungs of the leg under way in its current episode (<see cref="NavRunner.EpisodeRungs"/>); 0 with no leg or in a hold.</summary>
    public int StuckRungs => _active && !Holding && _runner is { } runner ? runner.EpisodeRungs : 0;

    /// <summary>The cell the leg under way goes to, or 0 with no leg.</summary>
    public uint GoalCell => _active ? _goalCell : 0;

    /// <summary>M5 7 P7i: the radius of a stuck spot's costly patch, in metres (a den room's corner pocket and its doorway). Lifestone's own.</summary>
    public const float StuckSpotRadiusMetres = 3f;

    /// <summary>M5 7 P7i: marks the floor round <paramref name="at"/> costly for the room graph's plans until <paramref name="until"/> (session clock), logged with the patches in force.</summary>
    /// <param name="at">The spot.</param>
    /// <param name="until">When the patch ends (session clock).</param>
    /// <param name="description">What was marked.</param>
    public bool AvoidSpot(in CellFrame at, double until, out string description)
    {
        if (at.CellId >> 16 != _rooms.LandblockId >> 16)
        {
            description = $"the spot's cell 0x{at.CellId:X8} is not in the room graph's landblock";
            return false;
        }

        _rooms.Patches.Add(at.Origin, StuckSpotRadiusMetres, until);
        description = string.Create(
            CultureInfo.InvariantCulture,
            $"the floor within {StuckSpotRadiusMetres:0.#} m of 0x{at.CellId:X8} [{at.Origin.X:0.#} {at.Origin.Y:0.#} {at.Origin.Z:0.#}] costs the room graph's plans {RoomPatches.CostMetres:0} m more until {until:0} ({_rooms.Patches.Count} patch(es) in force)");
        _log.Info(LogCategory, "walk: stuck spot patched: " + description);
        return true;
    }

    /// <summary>M5 7 P7i: drops the room graph's patches whose time is over at session clock <paramref name="clock"/>, logged.</summary>
    private void ExpirePatches(double clock)
    {
        if (_rooms.Patches.Count > 0 && _rooms.Patches.Expire(clock) is var dropped and > 0)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"walk: {dropped} stuck spot patch(es) expired ({_rooms.Patches.Count} in force)"));
        }
    }

    /// <summary>
    /// M5 7 P7b (D139 j): plans and starts a leg to <paramref name="goal"/> itself (no tour waypoint): a point goal within the profile's stop distance. A spot leg that fails is not recovered onto the tour (the tour did not offer it):
    /// it ends Arrived with its failure logged, and the caller reads where the player stands.
    /// </summary>
    /// <param name="runner">The runner to run it on.</param>
    /// <param name="from">Where the player stands.</param>
    /// <param name="goal">The spot.</param>
    /// <param name="now">The runner's clock.</param>
    /// <param name="clock">The session clock (the leg's time limit).</param>
    /// <param name="description">What the leg is; or why none was planned.</param>
    /// <returns>False when no walk of the room graph joins the player's cell and the spot's.</returns>
    public bool BeginTo(NavRunner runner, in CellFrame from, in CellFrame goal, double now, double clock, out string description)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        ExpirePatches(clock);
        if (_rooms.Plan(from.CellId, from.Origin, goal.CellId, goal.Origin) is null)
        {
            description = $"no walk of the room graph joins the player's cell 0x{from.CellId:X8} and the spot's cell 0x{goal.CellId:X8}";
            return false;
        }

        Legs++;
        _goalCell = goal.CellId;
        _legStart = clock;
        _dwellUntil = double.NaN;
        _active = true;
        _spot = true;
        _pacing.BeginWalk(now);
        _mark = LegMark.Of(runner);
        runner.Start(NavGoal.ToPoint(NavMap.FromCellFrame(goal), goal.CellId) with { StopTolerance = _ground.Walk.StopWithinMetres }, now);
        description = $"leg {Legs} from 0x{from.CellId:X8} to the spot in 0x{goal.CellId:X8} on the runner (the room graph)";
        return true;
    }

    /// <summary>Plans and starts the leg to the next waypoint of the tour from <paramref name="from"/>.</summary>
    /// <param name="runner">The runner to run it on (made by the walk; restarted for each leg).</param>
    /// <param name="from">Where the player stands.</param>
    /// <param name="now">The runner's clock.</param>
    /// <param name="clock">The session clock (the leg's time limit).</param>
    /// <param name="description">What the leg is, for the log.</param>
    /// <returns>False with <see cref="Failure"/> set when no leg can be planned.</returns>
    public bool Begin(NavRunner runner, in CellFrame from, double now, double clock, out string description)
    {
        ArgumentNullException.ThrowIfNull(runner);
        description = string.Empty;
        _runner = runner;
        _spot = false;
        ExpirePatches(clock);
        var walk = _ground.Walk;
        for (var tries = 0; tries < _tour.Waypoints; tries++)
        {
            if (_tour.Next(from.CellId, clock) is not { } cellId)
            {
                break;
            }

            if (_tour.Purpose == TourPurpose.Hold)
            {
                // F5 (D47): no purpose to walk: no leg, the player stands; the walk "arrives" after HoldRecheckSeconds and the fight looks for a target and asks again.
                _goalCell = cellId;
                _active = true;
                runner.Pause();
                description = _pacing.BeginHold(_tour, from.CellId, clock);
                return true;
            }

            if (_roomFrame(cellId) is not { } room || _rooms.Plan(from.CellId, from.Origin, cellId, room.Origin) is null)
            {
                _log.Warn(LogCategory, "no walk joins the player's cell and a waypoint", new LogField("from", $"0x{from.CellId:X8}"), new LogField("to", $"0x{cellId:X8}"));
                continue;
            }

            Legs++;
            _goalCell = cellId;
            _legStart = clock;
            _dwellUntil = double.NaN;
            _active = true;
            _pacing.BeginWalk(now);
            _mark = LegMark.Of(runner);
            runner.Start(NavGoal.ToPoint(NavMap.FromCellFrame(room), cellId) with { StopTolerance = walk.StopWithinMetres }, now);
            description = $"leg {Legs} from 0x{from.CellId:X8} to 0x{cellId:X8} on the runner (the room graph)";
            return true;
        }

        _active = false;
        Failure = $"no walk joins the player's cell 0x{from.CellId:X8} and any waypoint of the ground's tour (the player stands off the walkable cells or they are cut)";
        return false;
    }

    /// <summary>One frame of the leg: steps the runner, ends the leg, or recovers a failed one onto the next waypoint.</summary>
    /// <param name="position">Where the player stands.</param>
    /// <param name="now">The runner's clock.</param>
    /// <param name="clock">The session clock.</param>
    public FightWalkStatus Step(in CellFrame position, double now, double clock)
    {
        if (!_active || _runner is not { } runner)
        {
            return FightWalkStatus.Idle;
        }

        if (Holding)
        {
            if (!_pacing.HoldOver(clock))
            {
                return FightWalkStatus.Walking;
            }

            _active = false;
            return FightWalkStatus.Arrived;
        }

        // F5: the leg's own walking time: the frames the runner was running (paused, or yielding to the server's MoveTo while a cast turns the player, left out).
        _pacing.Walked(runner.State == NavRunState.Running, now);
        switch (runner.Step(now))
        {
            case NavRunState.Arrived:
            case NavRunState.ArrivedWithoutSight:
                if (double.IsNaN(_dwellUntil))
                {
                    _dwellUntil = now + _ground.Walk.StopDwellSeconds;
                    if (!_spot)
                    {
                        _tour.LegWalked(_goalCell, clock, _pacing.WalkSeconds);
                    }

                    _log.Info(LogCategory, $"leg {Legs} arrived at {(_spot ? "the spot in " : string.Empty)}0x{_goalCell:X8}: {LegFields(runner)}");
                }

                if (now < _dwellUntil)
                {
                    return FightWalkStatus.Walking;
                }

                _active = false;
                _recovery.Arrived();
                return FightWalkStatus.Arrived;
            case NavRunState.Blocked:
                return LegFailed("the runner is blocked: " + (runner.FailureReason ?? "no reason given"), position, now, clock);
            case NavRunState.Failed:
                return LegFailed("the runner failed: " + (runner.FailureReason ?? "no reason given"), position, now, clock);
            case NavRunState.Interrupted:
                _active = false;
                Failure = "a movement key of the player's own ended the leg (the player's keys win)";
                return FightWalkStatus.Failed;
        }

        if (clock - _legStart > _ground.Walk.LegSeconds)
        {
            runner.Pause();
            return LegFailed(string.Create(CultureInfo.InvariantCulture, $"the leg did not finish within {_ground.Walk.LegSeconds:0.#} s"), position, now, clock);
        }

        return FightWalkStatus.Walking;
    }

    /// <summary>Drops the leg and releases the runner's keys.</summary>
    public void Abandon()
    {
        _active = false;
        _pacing.Stop();
        _runner?.Pause();
    }

    private FightWalkStatus LegFailed(string reason, in CellFrame position, double now, double clock)
    {
        _active = false;
        if (_spot)
        {
            // M5 7 P7b (D139 j): a spot leg is the caller's own, not the tour's: no rest for a tour cell, no recovery onto the tour; it ends, and the caller reads where the player stands.
            _runner?.Pause();
            _log.Warn(LogCategory, $"leg {Legs} to the spot in 0x{_goalCell:X8} failed ({reason}); no recovery onto the tour: the spot's caller decides");
            return FightWalkStatus.Arrived;
        }

        _tour.LegFailed(_goalCell, clock);
        if (_recovery.Failed(Legs, _goalCell, reason) is { } ended)
        {
            Failure = ended;
            return FightWalkStatus.Failed;
        }

        if (!Begin(_runner!, in position, now, clock, out var next))
        {
            Failure = $"leg {Legs} failed ({reason}) and no leg to the next waypoint could be planned: " + Failure;
            return FightWalkStatus.Failed;
        }

        _log.Info(LogCategory, "recovered: " + next);
        return FightWalkStatus.Walking;
    }
}

/// <summary>
/// The hold and the walking clock of a fight walk's legs (M5 item 2 fix F6, review nit 2-R14: one copy of F5's rules for every walk): <see cref="RunnerLegs"/> and the headless journey host's walk both use it, so a hold is no leg
/// in either and the tour is told the runner's walking seconds, not the time from the offer to the arrival.
/// </summary>
public sealed class LegPacing
{
    private double _holdUntil = double.NaN;
    private double _lastStep = double.NaN;

    /// <summary>True from <see cref="BeginHold"/> until the next leg or <see cref="Stop"/> (F5: the tour named no purpose to walk, so the player stands).</summary>
    public bool Holding { get; private set; }

    /// <summary>The runner's walking seconds of the leg begun last (the frames it was running; paused or yielding time left out).</summary>
    public double WalkSeconds { get; private set; }

    /// <summary>Starts a hold at session clock <paramref name="clock"/>: no leg for <see cref="RunnerLegs.HoldRecheckSeconds"/>. Returns the hold's description for the log: the purpose the tour names.</summary>
    /// <param name="tour">The tour that named no purpose (its note is logged).</param>
    /// <param name="fromCell">The cell the player stands in.</param>
    /// <param name="clock">The session clock.</param>
    public string BeginHold(IGroundTour tour, uint fromCell, double clock)
    {
        ArgumentNullException.ThrowIfNull(tour);
        Holding = true;
        _holdUntil = clock + RunnerLegs.HoldRecheckSeconds;
        return $"hold in 0x{fromCell:X8} (no leg; {tour.PurposeNote})";
    }

    /// <summary>True once the hold has lasted <see cref="RunnerLegs.HoldRecheckSeconds"/> at session clock <paramref name="clock"/>.</summary>
    public bool HoldOver(double clock) => clock >= _holdUntil;

    /// <summary>Starts a leg's walking clock at the runner's time <paramref name="now"/>.</summary>
    public void BeginWalk(double now)
    {
        Holding = false;
        WalkSeconds = 0;
        _lastStep = now;
    }

    /// <summary>One frame of the leg at the runner's time <paramref name="now"/>: the seconds since the last frame count when the runner was <paramref name="running"/> (not paused, not yielding to the server's MoveTo).</summary>
    public void Walked(bool running, double now)
    {
        if (running && !double.IsNaN(_lastStep) && now > _lastStep)
        {
            WalkSeconds += now - _lastStep;
        }

        _lastStep = now;
    }

    /// <summary>Ends a hold.</summary>
    public void Stop() => Holding = false;
}

/// <summary>
/// The real <see cref="IFightWalk"/>: an <see cref="OfflineWalker"/> in the ground's collision (its landblock alone), placed where the server puts the player and given the server's
/// positions and moves as <see cref="RoutePlay"/> gives them to its walker (the same session wiring: the presence-gated movement sink, the player's own ObjectMoved and ObjectMotionChanged,
/// <see cref="SessionTargetWorld"/> for the TurnToObject ACE sends when it turns the caster), and a <see cref="NavRunner"/> over the ground's <see cref="RoomGraph"/> (a <see cref="RoomPathSource"/>, the walker's
/// <see cref="ControllerNavBody"/>, a <see cref="RouteConfirmer"/> and the session's hostile creatures, <see cref="HostileCreatureFeed"/> with the dats' own radii) for each leg (<see cref="RunnerLegs"/>).
/// </summary>
/// <remarks>
/// <para>The waypoints come from the walk's <see cref="IGroundTour"/> (the ground's tour over its respawn schedule, <see cref="GroundTour.For"/>, unless the host gave another); a leg goes to the next one that is not the cell the player stands in and
/// has a walk from it, with the stop distance, dwell and leg limit of the profile's walk section (<see cref="HuntingGround.Walk"/>). A failed leg is abandoned and the walk goes on (<see cref="RunnerLegs"/>, <c>m5-fight-walk-recover</c>). Entering a no-go cell of the ground stops the walk, named (M5 D4 retires M4 D12's "left the floor"). Nothing here asserts that the server agrees with the walker: the loop's fight has its own checks (a cast at a target in range is the server's word on where the player is).</para>
/// <para><c>TODO(m3-fight-walk-audit)</c>: the walk is not audited against ACE as <see cref="RoutePlay"/>'s is (no where-am-I at the stops, no corrections count).</para>
/// </remarks>
public sealed class GroundWalk : IFightWalk
{
    private const string LogCategory = "fight";

    private readonly DatSet _dats;
    private readonly Func<uint, float?> _setupExtent; // M5 5 F2 (5-R10): the doors' extents, built once for this walk (the cache is per Setup id), not per leg
    private readonly HuntingGround _ground;
    private readonly PhysicsGeometryLibrary _geometry;
    private readonly PhysicsWorld _world;
    private readonly RoomGraph _planner;
    private readonly ILogSink _log;
    private readonly List<ObjectMoved> _moves = [];
    private readonly List<ObjectMotionChanged> _serverMotions = [];
    private WireMotionItem[] _actions = new WireMotionItem[16];
    private IDisposable? _movesSubscription;
    private IDisposable? _motionSubscription;
    private OfflineWalker? _walker;
    private MoveIntents? _move;
    private PresenceGatedMovementSink? _gate;
    private SessionTargetWorld? _targets;
    private readonly RunnerLegs _legs;
    private NavRunner? _runner;
    private RouteConfirmer? _confirmer;
    private bool _placed;
    private bool _disposed;

    private GroundWalk(DatSet dats, HuntingGround ground, PhysicsGeometryLibrary geometry, PhysicsWorld world, RoomGraph planner, IGroundTour tour, ILogSink log)
    {
        (_dats, _ground, _geometry, _world, _planner, _log) = (dats, ground, geometry, world, planner, log);
        _setupExtent = DoorCrossings.SetupExtents(dats);
        _legs = new RunnerLegs(ground, tour, planner, log);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far, the recovery legs included.</summary>
    public int Legs => _legs.Legs;

    /// <summary>The failed legs the walk abandoned and went on from (<c>m5-fight-walk-recover</c>).</summary>
    public int RecoveredLegs => _legs.Recovered;

    /// <inheritdoc/>
    int? IFightWalk.LegsRecovered => _legs.Recovered;

    /// <summary>
    /// F3: the sweep <see cref="GameFightWalk.ClearLine"/> runs on the walker's physics world, so the headless fight skips a target behind rock as the game's does (it answered the interface's default <c>true</c> and shot tuskers on other storeys through the rock,
    /// the lead's 30-minute read: <c>wall_hits=94</c> of 144 casts, <c>blocked_picks=0</c>). The den is attached to the world when the walk is built, so the answer does not wait for <see cref="Prepare"/>.
    /// </summary>
    public bool HasClearLine(in CellFrame from, in CellFrame to) => GameFightWalk.ClearLine(_world, in from, in to);

    /// <inheritdoc/>
    public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells) => GameFightWalk.TraceCells(_world, in from, in to, cells);

    /// <inheritdoc/>
    public IReadOnlyList<CellFrame> FloorSpots => _floorSpots ??= GameFightWalk.RoomSpots(_planner);

    private IReadOnlyList<CellFrame>? _floorSpots;

    /// <summary>The runner of the leg walked last, or null before the first leg.</summary>
    public NavRunner? Runner => _runner;

    /// <summary>The walker, or null before <see cref="Prepare"/>.</summary>
    public OfflineWalker? Walker => _walker;

    /// <summary>The floor planner.</summary>
    public RoomGraph Planner => _planner;

    /// <inheritdoc/>
    public IGroundTour Tour => _legs.Tour;

    /// <summary>Builds the ground's collision and room graph from the dats (a few seconds).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="ground">The ground the walk runs in.</param>
    /// <param name="log">Where the walk logs.</param>
    /// <param name="tour">Where each leg's next waypoint comes from; null: the ground's tour (<see cref="GroundTour.For"/>: a <see cref="GroundTour"/> for a dungeon with slots).</param>
    /// <exception cref="InvalidDataException">The ground's cells do not load.</exception>
    public static GroundWalk Create(DatSet dats, HuntingGround ground, ILogSink log, IGroundTour? tour = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(log);
        var geometry = new PhysicsGeometryLibrary(dats);
        var coord = GroundRooms.CoordOf(ground);
        var world = new PhysicsWorld();
        world.Attach(new LandblockCollisionBuilder(dats, geometry).Build(coord));
        var graph = GroundRooms.GraphOf(LandblockCellSet.Load(new CellStructureLibrary(dats), coord), ground);
        return new GroundWalk(dats, ground, geometry, world, graph, tour ?? GroundTour.For(ground, graph, log), log);
    }

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_walker is not null)
        {
            return;
        }

        var self = session.State.Self;
        if (!self.Qualities.IsKnown)
        {
            Failure = "preparing the walk: the player's qualities have not arrived (no PlayerDescription), so its run and jump skills are unknown";
            return;
        }

        var weenie = QualitiesWeenie.FromSkillTable(self.Qualities, SkillTableData.Load(_dats), _log);
        _gate = new PresenceGatedMovementSink(session.Actions.Movement, () => session.State.Self.Travel == SelfTravel.Present);
        _world.SetTime(now);
        _walker = OfflineWalker.Create(_dats, _world, _log, weenie, _geometry, id: self.Guid, sink: _gate);
        _move = session.Actions.Move; // scripted movement goes through IActionApi.Move (the human's keys, R2)
        _move.Bind(_walker.Controller, _walker.Controller.Options.RunByDefault);
        _targets = new SessionTargetWorld(session.State, _dats, _geometry, () => _world.CurrentTime, _walker.Physics);
        _walker.Physics.TargetWorld = _targets;
        _confirmer = new RouteConfirmer(_world, _walker.Physics.Setup ?? throw new InvalidOperationException("the ground walker has no Setup"), _log);
        _runner = new NavRunner(
            _move,
            new ControllerNavBody(_walker.Controller),
            new RoomPathSource(_planner),
            _confirmer,
            null,
            new NavRunnerOptions { Name = "ground-walk" },
            _log,
            new HostileCreatureFeed(session.State.Objects, self.Guid, _log, HostileCreatureFeed.SetupRadii(_dats))) // the creature layer, with each creature's own radius
        {
            Doors = new SessionDoorFeed(session, _log, _setupExtent), // M5 5 d2: a closed door on a leg is opened (purpose=door), not walked into
        };
        _movesSubscription = session.Events.Subscribe<ObjectMoved>((in ObjectMoved e) =>
        {
            if (e.IsSelf)
            {
                _moves.Add(e);
            }
        });
        _motionSubscription = session.Events.Subscribe<ObjectMotionChanged>((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && !e.Autonomous)
            {
                _serverMotions.Add(e);
            }
        });
        _log.Info(LogCategory, "the ground walker is made; it is placed by the server's teleport", new LogField("run_rate", weenie.TryGetRunRate(out var rate) ? rate : null));
    }

    /// <inheritdoc/>
    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        description = string.Empty;
        if (_walker is null || !_placed)
        {
            Failure = "the ground walker is not placed: no teleport of the player reached it";
            return false;
        }

        var from = _walker.Position;
        if (_legs.Begin(_runner!, in from, now, session.Clock.Now, out description))
        {
            return true;
        }

        Failure = _legs.Failure;
        return false;
    }

    /// <inheritdoc/>
    public int LegStuckRungs => _legs.StuckRungs;

    /// <inheritdoc/>
    public uint LegGoalCell => _legs.GoalCell;

    /// <inheritdoc/>
    public bool AvoidSpot(in CellFrame at, double until, out string description) => _legs.AvoidSpot(in at, until, out description);

    /// <inheritdoc/>
    public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goal, out string description)
    {
        if (_walker is null || !_placed)
        {
            description = "the ground walker is not placed: no teleport of the player reached it";
            return false;
        }

        var from = _walker.Position;
        return _legs.BeginTo(_runner!, in from, in goal, now, session.Clock.Now, out description);
    }

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_walker is null)
        {
            return FightWalkStatus.Idle;
        }

        DrainMoves(now);
        ApplyServerMotions();
        if (_placed)
        {
            _walker.Controller.Tick(now);
            var cell = _walker.Position.CellId;
            if (_ground.IsNoGo(cell))
            {
                // The legs are planned over the walkable cells only, but the physics collides against the whole landblock: a slide or an overshoot near a doorway can enter a no-go cell (the exit portal's doorway is next to a den arrival). Fail at that moment, not later and indirectly (3.1-R4; D4 retires D12's "left the floor").
                Failure = NoGoReason(_ground, cell);
                _legs.Abandon();
                return FightWalkStatus.Failed;
            }
        }

        var status = _legs.Step(_walker.Position, now, session.Clock.Now);
        if (status == FightWalkStatus.Failed)
        {
            Failure = _legs.Failure;
        }

        return status;
    }

    /// <summary>Why a walk that entered a no-go cell stops (named for the log and the verdict).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="cell">The no-go cell the walker entered.</param>
    public static string NoGoReason(HuntingGround ground, uint cell) =>
        $"the walk entered the no-go cell 0x{cell:X8} of {ground.Name} (TODO(m3-fight-left-floor) if this is a planner or physics gap)";

    /// <inheritdoc/>
    /// <remarks>The leg ends inactive: its <see cref="RunnerLegs.Step"/> answers Idle until the next <see cref="BeginLeg"/>, so an abandoned leg is not reported as a failure on the next tick.</remarks>
    public void Abandon() => _legs.Abandon();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Abandon();
        _runner?.Dispose();
        _confirmer?.Dispose();
        if (_move is not null && _walker is not null)
        {
            _move.Unbind(_walker.Controller);
        }

        _movesSubscription?.Dispose();
        _motionSubscription?.Dispose();
    }

    /// <summary>Hands the player's position updates to the walker, in order, as <see cref="RoutePlay"/> does.</summary>
    private void DrainMoves(double now)
    {
        if (_moves.Count == 0)
        {
            return;
        }

        foreach (var move in _moves)
        {
            var coord = LandblockCoord.FromId(move.Position.CellId);
            if (!_world.IsAttached(coord))
            {
                continue; // the login's own arrival outside the den
            }

            if (!_placed)
            {
                if (move.Verdict != PositionVerdict.SelfTeleport)
                {
                    continue;
                }

                if (!_walker!.Place(move.Position))
                {
                    _log.Error(LogCategory, "no valid position for the walker where the server put the player", new LogField("cell", $"0x{move.Position.CellId:X8}"));
                    continue;
                }

                _placed = true;
            }

            _walker!.Controller.Apply(move, now);
        }

        _moves.Clear();
    }

    /// <summary>Gives the walker the server's MoveTo and TurnTo of the player (ACE turns the caster to a targeted spell's target that way); the rest of the server's motion is left alone.</summary>
    private void ApplyServerMotions()
    {
        if (_serverMotions.Count == 0)
        {
            return;
        }

        foreach (var e in _serverMotions)
        {
            var type = (byte)e.Motion.MovementType;
            if (type is < 6 or > 9 || !_placed || _walker!.Body is not IPlayerMovementBody body)
            {
                continue;
            }

            var count = AvatarWire.CopyActions(e.Motion, _actions);
            if (count > _actions.Length)
            {
                _actions = new WireMotionItem[count];
                AvatarWire.CopyActions(e.Motion, _actions);
            }

            body.ApplyServerMovement(AvatarWire.ToMovement(e.Motion), _actions.AsSpan(0, count), e.Autonomous, out _);
        }

        _serverMotions.Clear();
    }
}
