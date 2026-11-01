using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Physics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>M5 8 npc: the walk steps around a solid server object (<see cref="RouteDriver.Obstacles"/>).</summary>
public sealed partial class RoutePlay
{
    private readonly Dictionary<uint, PhysicsSetup?> _obstacleSetups = [];

    /// <summary>
    /// Gives the route's driver the session's solid server objects and the walker's physics world to probe (the Holtburg ring's Pumpkin Buffer stalled the walk at goal 7,
    /// about once in ten runs; <c>TODO(holtburg-ring-stall)</c>). The objects are the object table's, in the world (not held, not a player: other players do not stop the
    /// body), not ethereal and not ignoring collisions, whose Setup collides as a cylinder or sphere (a physics-BSP-only object, a door or a building, is not walked around);
    /// the radius is the Setup's, scale applied. Every detour is logged once, as a warning when it was needed.
    /// </summary>
    private void AttachObstacles(RouteDriver driver, IGameSessionView session)
    {
        var walker = _walker!;
        var body = walker.Physics.Setup;
        if (body is null)
        {
            return;
        }

        driver.Obstacles = new WorldRouteObstacles(walker.World, body, () => SolidObjects(session));
        driver.Detoured += line => _log.Warn(LogCategory, line);
    }

    private IEnumerable<RouteObstacle> SolidObjects(IGameSessionView session)
    {
        foreach (var o in session.State.Objects.All)
        {
            if (o.IsSelf || o.Parent is not null || o.Position is not { CellId: not 0 } at
                || (o.DescriptionFlags & ServerObjectPhysicsHost.PlayerFlag) != 0
                || ((uint)o.State & (uint)(PhysicsState.Ethereal | PhysicsState.IgnoreCollisions)) != 0
                || o.Physics.SetupId is not { } setupId || SetupOf(setupId) is not { } setup
                || RouteObstacleShapes.RadiusOf(setup, o.Physics.Scale ?? 1f) is not { } radius)
            {
                continue;
            }

            yield return new RouteObstacle(o.Name, o.Weenie.WeenieClassId, at.Normalized(), radius);
        }
    }

    private PhysicsSetup? SetupOf(uint setupId)
    {
        if (_obstacleSetups.TryGetValue(setupId, out var cached))
        {
            return cached;
        }

        PhysicsSetup? setup = null;
        try
        {
            setup = PhysicsSetup.Load(_dats, setupId, _geometry);
        }
        catch (InvalidDataException e)
        {
            _log.Warn(LogCategory, $"the walk's obstacle check: Setup 0x{setupId:X8} is malformed ({e.Message}); an object made from it is not walked around");
        }

        _obstacleSetups[setupId] = setup;
        return setup;
    }
}
