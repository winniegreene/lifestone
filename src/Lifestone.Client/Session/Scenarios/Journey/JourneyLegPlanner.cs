using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The journey's leg planning, one for both hosts (M4 P5b J5b): the goal a <see cref="TravelRequest"/> names (<see cref="GoalOf"/>: a point with the
/// terrain's height when the request has none, an object's own position, the indoor room an object indoors is reached in), the path source the two ends
/// need (<see cref="SourceFor"/>: the den's room graph, the indoor-outdoor source with its exit route when either end is indoors, else the outdoor planner),
/// what counts as an arrival (<see cref="Arrived"/>) and the named failures (<see cref="FailureOf"/>). The headless host (<see cref="HeadlessJourneyRunnerHost"/>)
/// and the Godot host differ only in the body that walks (an <see cref="Movement.OfflineWalker"/> of their own or the player's controller) and in where the
/// collision and the nav tiles come from (<see cref="IJourneyLegBody"/>); the leg itself is <see cref="JourneyRunLeg"/>.
/// </summary>
public sealed class JourneyLegPlanner
{
    private readonly DatSet? _dats;
    private readonly Func<RoomGraph>? _denSource;
    private readonly Dictionary<uint, LandblockCellSet> _cells = [];
    private CellStructureLibrary? _structures;
    private RoomGraph? _den;

    /// <summary>A planner over the dats.</summary>
    /// <param name="dats">The dats (the terrain's heights, the landblocks' cells), or null for a planner that plans only point goals with a height and the host's den (tests).</param>
    /// <param name="structures">The cell structure library to share (null: made here at the first indoor leg).</param>
    /// <param name="den">Where the den's room graph comes from (the Godot host builds it on a worker), or null to build it here from the dats at the first den leg.</param>
    public JourneyLegPlanner(DatSet? dats, CellStructureLibrary? structures = null, Func<RoomGraph>? den = null)
    {
        _dats = dats;
        _structures = structures;
        _denSource = den;
    }

    /// <summary>The den's floor graph (landblock 0x0105, lean <see cref="DenRooms.MaxLean"/>), loaded once.</summary>
    /// <exception cref="InvalidOperationException">The host's den source has no graph (it was not built).</exception>
    public RoomGraph DenGraph() => _den ??= _denSource is { } source ? source() : DenRooms.GraphOf(CellsOf(JourneyGrounds.DenLandblock << 16));

    /// <summary>The cells of <paramref name="cellId"/>'s landblock, loaded once.</summary>
    /// <param name="cellId">A cell of the landblock.</param>
    public LandblockCellSet CellsOf(uint cellId)
    {
        var landblock = cellId >> 16;
        if (!_cells.TryGetValue(landblock, out var cells))
        {
            _structures ??= new CellStructureLibrary(_dats ?? throw new InvalidOperationException("the leg planner has no dats to load the cells of 0x" + landblock.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
            _cells[landblock] = cells = LandblockCellSet.Load(_structures, LandblockCoord.FromId(cellId));
        }

        return cells;
    }

    /// <summary>
    /// The goal of <paramref name="request"/>: a point (with the terrain's height when the request's location has none) or, for an object, the object's own position
    /// from the object table (the destination is only a hint, lane J2a), within the request's range, with its sight rule. An object indoors is reached in the room the
    /// request names when that is indoors in the same landblock (the station's own choice: Renald's front room 0xA9B4013F, not his room behind the counter,
    /// TODO(room-steps)), else in its own cell; an object outdoors is an outdoor goal whatever room the request names (live round 2: Thelnoth stands outdoors at
    /// 0xA9B4000F; planning into his cellar 0xA9B40111 put the route's end through the cellar's wall) (M4 P5b F1).
    /// </summary>
    /// <param name="session">The session (the object table).</param>
    /// <param name="request">The request.</param>
    public NavGoal GoalOf(IGameSessionView session, TravelRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        var location = request.Destination.Location;
        var cell = location.Position.CellId;
        var local = location.Position.Local;
        if (!location.HasHeight && LandDefs.IsOutdoorCell(cell))
        {
            local = local with { Z = HeadlessBody.TerrainZ(_dats ?? throw new InvalidOperationException("the leg planner has no dats for the terrain's height"), cell, local.X, local.Y) };
        }

        var point = new NavPoint(cell & 0xFFFF0000u, local);
        var requestCell = cell;
        if (request.ObjectGuid != 0 && session.State.Objects.TryGet(request.ObjectGuid, out var o) && o.Position is { } it)
        {
            point = NavMap.FromCellFrame(it);
            cell = it.CellId;
        }

        uint? goalCell = LandDefs.IsOutdoorCell(cell) ? null
            : request.ObjectGuid != 0 && LandDefs.IsEnvCell(requestCell) && requestCell >> 16 == cell >> 16 ? requestCell
            : cell;
        if (request.ObjectGuid != 0)
        {
            return NavGoal.ToObject(request.ObjectGuid, point, (float)Math.Max(0, request.Range), request.NeedSight, goalCell);
        }

        var goal = NavGoal.ToPoint(point, goalCell);
        return request.Range > 0 ? goal with { StopTolerance = (float)request.Range, PassTolerance = Math.Max((float)request.Range, NavGoal.DefaultPassTolerance) } : goal;
    }

    /// <summary>The cell a goal is planned to: its room, else its landblock.</summary>
    /// <param name="goal">The goal.</param>
    public static uint GoalCellOf(in NavGoal goal) => goal.CellId ?? goal.Point.LandblockId;

    /// <summary>
    /// The path source for a leg from <paramref name="fromCell"/> to <paramref name="toCell"/>: the den's room graph when either end is in the den; the indoor-outdoor
    /// source (its exit route out of a room, M4 P5b F1-2) when either end is indoors; else the outdoor planner over <paramref name="map"/>.
    /// </summary>
    /// <param name="fromCell">The body's cell.</param>
    /// <param name="toCell">The goal's cell (<see cref="GoalCellOf"/>).</param>
    /// <param name="map">The nav map the outdoor planner reads (not read for the den).</param>
    public INavPathSource SourceFor(uint fromCell, uint toCell, NavMap? map)
    {
        if (toCell >> 16 == JourneyGrounds.DenLandblock || fromCell >> 16 == JourneyGrounds.DenLandblock)
        {
            return new RoomPathSource(DenGraph());
        }

        var planner = new PathPlanner(map ?? throw new InvalidOperationException("the leg has no nav map to plan on"));
        if (!LandDefs.IsOutdoorCell(toCell) || !LandDefs.IsOutdoorCell(fromCell))
        {
            var indoor = LandDefs.IsOutdoorCell(toCell) ? fromCell : toCell;
            return new IndoorOutdoorPathSource(planner, CellsOf(indoor));
        }

        return new OutdoorPathSource(planner);
    }

    /// <summary>True when a finished run counts as the leg's arrival: arrived, or arrived without a sight line the request does not need.</summary>
    /// <param name="state">The run's end.</param>
    /// <param name="request">The request.</param>
    public static bool Arrived(NavRunState state, TravelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return state == NavRunState.Arrived || (state == NavRunState.ArrivedWithoutSight && !request.NeedSight);
    }

    /// <summary>
    /// The named failure of a finished run that did not arrive: <c>travel:run:indoor-start-unreachable:&lt;cell&gt;</c> for a start in a room the room graph
    /// cannot leave (TODO(room-steps): Renald's back room 0xA9B40141; the journey's known-state recovery puts a character standing there back at the lifestone),
    /// else <c>travel:run:&lt;state&gt;: &lt;reason&gt;</c> (interrupted when the player's keys won, R6; blocked; arrivedwithoutsight; failed).
    /// </summary>
    /// <param name="state">The run's end.</param>
    /// <param name="reason">The runner's reason, or null.</param>
    /// <param name="startCell">The cell the leg started in.</param>
    public static string FailureOf(NavRunState state, string? reason, uint startCell)
    {
        reason ??= "no reason given";
        if (!LandDefs.IsOutdoorCell(startCell) && reason.Contains(IndoorOutdoorPathSource.IndoorStartUnreachable, StringComparison.Ordinal))
        {
            return $"travel:run:{IndoorOutdoorPathSource.IndoorStartUnreachable}:0x{startCell:X8}: {reason}";
        }

        return $"travel:run:{state.ToString().ToLowerInvariant()}: {reason}";
    }

    /// <summary>True when the server has the player more than <paramref name="metres"/> from the body (across, or up and down).</summary>
    /// <param name="server">The server's position.</param>
    /// <param name="body">The body's position.</param>
    /// <param name="metres">The distance.</param>
    public static bool Moved(in CellFrame server, in CellFrame body, float metres)
    {
        var a = NavMap.FromCellFrame(server);
        var b = NavMap.FromCellFrame(body);
        return Vector2.Distance(a.Global, b.Global) > metres || MathF.Abs(a.Local.Z - b.Local.Z) > metres;
    }

    /// <summary>The fields of a leg's begin line.</summary>
    /// <param name="name">The leg's name.</param>
    /// <param name="request">The request.</param>
    /// <param name="goal">The goal.</param>
    /// <param name="fromCell">The body's cell.</param>
    public static LogField[] BeginFields(string name, TravelRequest request, in NavGoal goal, uint fromCell)
    {
        ArgumentNullException.ThrowIfNull(request);
        return
        [
            new LogField("leg", name), new LogField("to", request.Destination.Name), new LogField("goal", goal.Point.ToString()),
            new LogField("cell", goal.CellId is { } gc ? $"0x{gc:X8}" : "outdoor"), new LogField("from", $"0x{fromCell:X8}"),
            new LogField("object", request.ObjectGuid == 0 ? null : $"0x{request.ObjectGuid:X8}"),
        ];
    }
}
