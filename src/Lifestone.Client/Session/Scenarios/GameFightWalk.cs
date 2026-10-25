using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight loop's walk in the game (M3 3.1 fd; <c>TODO(m3-fight-no-walker)</c> closed for the game): an <see cref="IFightWalk"/> over the game's own player. Headless, <see cref="DenWalk"/> makes a walker of its own
/// in a physics world of its own; in the game the walker belongs to the player controller (it ticks, streams collision, is placed by every teleport and follows the server's moves and MoveTos), so this class only
/// plans the legs on the den's floor (<see cref="RoomGraph"/> with the den's lean, <see cref="DenRooms"/>, built on the leg's loading worker) and drives that controller with a <see cref="RouteDriver"/>, as <see cref="RoutePlay"/> does through
/// <see cref="RoutePlayHost"/>. The movement goes out through the host's sink (the presence gate over the session's movement channel); no audit sits in between (<c>TODO(m3-fight-walk-audit)</c>, as <see cref="DenWalk"/>).
/// </summary>
/// <remarks>The leg order is <see cref="DenWalk.Tour"/>: the next spawn cell after the one the player stands in that has a walk from it. Engine-agnostic, it moved from the Godot project to the client in M4 P5b J5b so the Godot journey host gives it to the den fight and the death (<see cref="Journey.IJourneyWalks.DenWalk"/>) and the tests can drive it over a body that follows the server.</remarks>
public sealed class GameFightWalk : IFightWalk
{
    private const string LogCategory = "fight";

    private readonly RoutePlayHost _host;
    private readonly RoomGraph _planner;
    private readonly ILogSink _log;
    private RouteDriver? _driver;
    private bool _prepared;
    private bool _disposed;
    private int _next;
    private int _legs;
    private double _legStart;

    /// <summary>A walk over the game's player.</summary>
    /// <param name="host">The player's walker, through the scenario host play mode was given.</param>
    /// <param name="planner">The den's floor planner (built on a worker).</param>
    /// <param name="log">Where the walk logs.</param>
    public GameFightWalk(RoutePlayHost host, RoomGraph planner, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(log);
        (_host, _planner, _log) = (host, planner, log);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far.</summary>
    public int Legs => _legs;

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        if (_prepared)
        {
            return;
        }

        _prepared = true;
        _log.Info(LogCategory, "the game's player is the den walker; the server's teleport places it");
    }

    /// <inheritdoc/>
    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        description = string.Empty;
        if (_host.Walker is not { } walker || walker.Physics.PlacementPending || walker.Physics.Cell is null)
        {
            Failure = "the game's player body is not placed where the server put the player";
            return false;
        }

        var from = walker.Position;
        if (LeftTheFloor(from.CellId))
        {
            Failure = LeftTheFloorReason(from.CellId);
            return false;
        }

        for (var tries = 0; tries < DenWalk.Tour.Count; tries++)
        {
            var cellId = (BlackSpawnDen.Landblock << 16) | DenWalk.Tour[_next++ % DenWalk.Tour.Count];
            if (cellId == from.CellId)
            {
                continue;
            }

            if (_planner.PlanScripted("den-leg-" + (_legs + 1), in from, cellId, DenWalk.StopWithinMetres, DenWalk.StopDwellSeconds) is not { } route)
            {
                _log.Warn(LogCategory, "no walk joins the player's cell and a waypoint", new LogField("from", $"0x{from.CellId:X8}"), new LogField("to", $"0x{cellId:X8}"));
                continue;
            }

            _driver?.Abandon();
            _driver = new RouteDriver(walker.Controller, route, session.Actions.Move);
            _legs++;
            _legStart = session.Clock.Now;
            description = $"leg {_legs} from 0x{from.CellId:X8} to 0x{cellId:X8} ({route.Goals.Count} goals)";
            return true;
        }

        Failure = $"no walk joins the player's cell 0x{from.CellId:X8} and any waypoint of the floor (the player stands off the floor's cells or the floor is cut)";
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ACE aims a bolt from the caster's 2/3 height to the target's 2/3 height (<c>ProjHeight</c>, WorldObject_Magic.cs 1526); the sweep is the camera's viewer sphere (<c>PhysicsWorld.SweepViewer</c>, retail <c>SmartBox::update_viewer</c>).
    /// <c>TODO(m3-fight-los-radius)</c>: the bolt's own sphere is not known, 0.3 m is the viewer's. <c>TODO(m3-fight-los-target-height)</c>: the target's height is not read, 1.8 m stands in.
    /// <c>TODO(m3-fight-los-approximation)</c>: moving objects and ethereal flags are not part of the sweep.
    /// </remarks>
    public bool HasClearLine(in CellFrame from, in CellFrame to)
    {
        if (_host.Walker is not { } walker)
        {
            return true;
        }

        var lift = new System.Numerics.Vector3(0f, 0f, StandInBodyHeight * ProjHeight);
        var a = new CellFrame(from.CellId, from.Frame with { Origin = from.Frame.Origin + lift });
        var b = new CellFrame(to.CellId, to.Frame with { Origin = to.Frame.Origin + lift });
        return !walker.World.SweepViewer(in a, in b, LineRadius).Blocked;
    }

    /// <summary>The body height a bolt's start and end heights are taken from (<c>TODO(m3-fight-los-target-height)</c>).</summary>
    private const float StandInBodyHeight = 1.8f;

    /// <summary>ACE's <c>ProjHeight</c>, the share of a body's height a bolt leaves from and aims at.</summary>
    private const float ProjHeight = 2f / 3f;

    /// <summary>The sphere swept along the line (<c>TODO(m3-fight-los-radius)</c>).</summary>
    private const float LineRadius = 0.3f;

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_driver is not { } driver)
        {
            return FightWalkStatus.Idle;
        }

        if (_host.Walker is { } walker && LeftTheFloor(walker.Position.CellId))
        {
            Failure = LeftTheFloorReason(walker.Position.CellId);
            driver.Abandon();
            _driver = null;
            return FightWalkStatus.Failed;
        }

        switch (driver.Tick(now))
        {
            case RouteStatus.Done:
                _driver = null;
                return FightWalkStatus.Arrived;
            case RouteStatus.Stuck:
            case RouteStatus.Failed:
                Failure = driver.FailureReason ?? "the leg failed without saying why";
                driver.Abandon();
                _driver = null;
                return FightWalkStatus.Failed;
        }

        if (session.Clock.Now - _legStart > DenWalk.LegSeconds)
        {
            Failure = $"the leg did not finish within {DenWalk.LegSeconds:0.#} s (goal {driver.GoalIndex})";
            driver.Abandon();
            _driver = null;
            return FightWalkStatus.Failed;
        }

        return FightWalkStatus.Walking;
    }

    /// <summary>
    /// 3.1-R4: the player's cell is in the den's landblock and is not one of the floor's cells (a slide or an overshoot near a doorway; the exit portal's doorway is next to the arrival cell). Reads two integers;
    /// allocates nothing. A cell of another landblock (the player is not in the den) is not "off the floor": the scenario's own presence checks own that.
    /// </summary>
    private static bool LeftTheFloor(uint cellId) => (cellId >> 16) == BlackSpawnDen.Landblock && !BlackSpawnDen.IsFloorCell(cellId);

    private static string LeftTheFloorReason(uint cellId) => $"the walk left the den's floor: the player is in cell 0x{cellId:X8}, which is not a floor cell (3.1-R4)";

    /// <inheritdoc/>
    /// <remarks>The driver is dropped too: <see cref="RouteDriver.Abandon"/> marks a running route Failed and its <c>Tick</c> keeps answering that, so a walk that kept it would report an abandoned leg as a failure on its next tick.</remarks>
    public void Abandon()
    {
        _driver?.Abandon();
        _driver = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Abandon();
    }
}
