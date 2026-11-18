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
    /// body), that stop the body by the walker's own physics (<see cref="StopsTheBody"/>: not ethereal; an object that only ignores collisions still stops it), whose Setup collides as a cylinder or sphere (a physics-BSP-only object, a door or a building, is not walked around);
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
                || !StopsTheBody((uint)o.State)
                || o.Physics.SetupId is not { } setupId || SetupOf(setupId) is not { } setup
                || RouteObstacleShapes.RadiusOf(setup, o.Physics.Scale ?? 1f) is not { } radius)
            {
                continue;
            }

            yield return new RouteObstacle(o.Name, o.Weenie.WeenieClassId, at.Normalized(), radius);
        }
    }

    /// <summary>
    /// M6 14 (live 2026-11-13 03:44 to 03:53 UTC, Uziz): whether the walker's own physics stops the body at a server object of <paramref name="state"/>, by the rule of
    /// <c>PhysicsObj.FindObjCollisions</c> (retail <c>CPhysicsObj::FindObjCollisions</c>, 11.6096 0x0050FB20): an ethereal object never stops the mover (with IgnoreCollisions as well it is
    /// not even tested), and IgnoreCollisions alone does not let the body through. The walk's obstacles are exactly the objects the body is stopped by (the owner's one world picture), so
    /// the filter reads the same bits. Before this, an object that only ignored collisions was left off the walk though the body met it: the Uziz town marker (wcid 49606, a Setup
    /// 0x020006E7 sphere; ACE sends PhysicsState 0x418, Gravity, IgnoreCollisions and ReportCollisions, not Ethereal, from its weenie's PropertyInt 93 and PropertyBool 13 false,
    /// WorldObject_Networking.cs CalculatedPhysicsState @95ef426d) stands at (179.38, 84.12), 0.9 m off the first leg from the Town Network's drop to Archmage Sammam al-Ashiz; on
    /// 2026-11-13 the body stood against it three trips running (no movement, 12 rungs each) and the run stopped, where at 00:23 UTC the same line had cleared it by about 6 cm.
    /// </summary>
    /// <param name="state">The object's physics state as the server sent it.</param>
    internal static bool StopsTheBody(uint state) => (state & (uint)PhysicsState.Ethereal) == 0;

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
