using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.World;

namespace Lifestone.Game.Sessions;

/// <summary>
/// The death scenario's bind walk in the game (M3 3.2 dd): an <see cref="IApproachWalk"/> over the game's own player. Headless, <see cref="HoltburgApproach"/> makes a walker of its own over Holtburg's collision; in the game
/// the walker belongs to the player controller (it ticks, streams the landblock's collision, is placed by every teleport and follows the server's moves), so this class only drives that controller with a
/// <see cref="RouteDriver"/> over a one-goal route to the lifestone, as <see cref="GameFightWalk"/> does for the den. ACE moves a player who is out of the lifestone's use radius only while the client follows its MoveToObject
/// (the first live run: 5.4 m away, nothing came); this walk takes the player within the radius before the Use. The movement goes out through the host's sink (the presence gate over the session's movement channel).
/// </summary>
internal sealed class GameApproachWalk : IApproachWalk
{
    private const string LogCategory = "death";

    private readonly RoutePlayHost _host;
    private readonly ILogSink _log;
    private RouteDriver? _driver;
    private bool _prepared;
    private bool _disposed;

    /// <summary>A walk over the game's player.</summary>
    /// <param name="host">The player's walker, through the scenario host play mode was given.</param>
    /// <param name="log">Where the walk logs.</param>
    public GameApproachWalk(RoutePlayHost host, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(log);
        (_host, _log) = (host, log);
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        if (_prepared)
        {
            return;
        }

        _prepared = true;
        _log.Info(LogCategory, "the game's player is the approach walker; the server's teleport places it");
    }

    /// <inheritdoc/>
    public bool Begin(IGameSessionView session, double now, in CellFrame target, float stopWithinMetres, out string description)
    {
        description = string.Empty;
        if (_host.Walker is not { } walker || walker.Physics.PlacementPending || walker.Physics.Cell is null)
        {
            Failure = "the game's player body is not placed where the server put the player (the teleport to Holtburg did not place it)";
            return false;
        }

        var from = walker.Position;
        var route = new ScriptedRoute("lifestone-approach", [new RouteGoal(target, stopWithinMetres, Stop: true, DwellSeconds: 0)]);
        _driver?.Abandon();
        _driver = new RouteDriver(walker.Controller, route, session.Actions.Move);
        description = $"from cell 0x{from.CellId:X8} toward cell 0x{target.CellId:X8}, stopping within {stopWithinMetres:0.0##} m";
        return true;
    }

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        if (_driver is not { } driver)
        {
            return FightWalkStatus.Idle;
        }

        switch (driver.Tick(now))
        {
            case RouteStatus.Done:
                _driver = null;
                return FightWalkStatus.Arrived;
            case RouteStatus.Stuck:
            case RouteStatus.Failed:
                Failure = driver.FailureReason ?? "the approach leg failed without saying why";
                driver.Abandon();
                _driver = null;
                return FightWalkStatus.Failed;
        }

        return FightWalkStatus.Walking;
    }

    /// <inheritdoc/>
    public void Abandon() => _driver?.Abandon();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver?.Abandon();
    }
}
