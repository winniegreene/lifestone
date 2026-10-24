using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Routes;

/// <summary>
/// Routes between the street and a building's rooms, and into its wall, planned from the landblock's cells and the offline collision
/// (<c>--offline-player-checks</c>, the same walks the tests run): not retail, the way a player walks to a door.
/// </summary>
/// <remarks>
/// <para>A doorway is a portal of a cell that leads outside (<see cref="CellPortalData.LeadsOutside"/>) in an upright polygon
/// (<see cref="CellRoutePlanner.IsUpright"/>; the lean is the planner's <c>maxLean</c>). The street side of
/// it is the portal's centre moved along its normal toward the outside (<see cref="CellRoutePlanner.DoorwayOf"/>), dropped onto the terrain; the rooms inside
/// are the generalised <see cref="CellRoutePlanner"/>'s waypoints (P5a: the one planner indoors).</para>
/// </remarks>
public static class BuildingRoutes
{
    /// <summary>How far outside the doorway the first goal of a walk in lies.</summary>
    public const float DoorStepMetres = 1.5f;

    /// <summary>How far outside the doorway a walk in starts and ends.</summary>
    public const float StartMetres = 4.0f;

    /// <summary>How far from the wall a wall bump starts and how far beyond it its goal lies.</summary>
    public const float WallApproachMetres = 6.5f;

    /// <summary>How far along the wall from the doorway a wall bump starts.</summary>
    public const float WallStartAlongMetres = 6.0f;

    /// <summary>How far along the wall from the doorway the goal behind the wall lies (the player slides that far to line up with it).</summary>
    public const float WallGoalAlongMetres = 2.5f;

    /// <summary>The arrival radius of a goal inside.</summary>
    public const float InsideTolerance = 0.6f;

    /// <summary>How long a walk stands at its turning point and at its end.</summary>
    public const double DwellSeconds = 1.0;

    /// <summary>
    /// A walk from <paramref name="startMetres"/> outside the doorway of <paramref name="doorCellId"/> in through its rooms to
    /// <paramref name="roomCellId"/>, a stand there, and the same way back to the start (<see cref="ScriptedRoute.Start"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The cell has no walkable doorway outside, or no walkable doorways join the rooms.</exception>
    public static ScriptedRoute InAndOut(string name, LandblockCellSet cells, PhysicsWorld world, uint doorCellId, uint roomCellId, float startMetres = StartMetres, float maxLean = 0f)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(world);
        var planner = new CellRoutePlanner(cells, maxLean);
        var (_, _, centre, outward) = Doorway(cells, planner, doorCellId);
        var path = planner.Plan(doorCellId, roomCellId)
            ?? throw new InvalidOperationException($"No walkable doorways lead from cell 0x{doorCellId:X8} to cell 0x{roomCellId:X8}.");
        var step = OutdoorPoint(world, cells.Coord, centre + (outward * DoorStepMetres));
        var start = OutdoorPoint(world, cells.Coord, centre + (outward * startMetres));
        var inside = path.Waypoints;
        var goals = new List<RouteGoal> { new(step, RouteLibrary.PassTolerance, Stop: false, DwellSeconds: 0) };
        for (var i = 0; i < inside.Count; i++)
        {
            var last = i == inside.Count - 1;
            goals.Add(new RouteGoal(inside[i], InsideTolerance, last, last ? DwellSeconds : 0));
        }

        for (var i = inside.Count - 2; i >= 0; i--)
        {
            goals.Add(new RouteGoal(inside[i], InsideTolerance, Stop: false, DwellSeconds: 0));
        }

        goals.Add(new RouteGoal(step, RouteLibrary.PassTolerance, Stop: false, DwellSeconds: 0));
        goals.Add(new RouteGoal(start, InsideTolerance, Stop: true, DwellSeconds));
        return new ScriptedRoute(name, goals) { Start = start };
    }

    /// <summary>
    /// A walk into the wall beside the doorway of <paramref name="doorCellId"/>: it starts <see cref="WallApproachMetres"/> outside and
    /// <see cref="WallStartAlongMetres"/> along the wall from the doorway, and its one goal lies behind the wall, <see cref="WallGoalAlongMetres"/> along
    /// the wall from the doorway on the same side. The wall stops the player, which slides along it to the point nearest the goal and stands there:
    /// the route is expected to end Stuck.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cell has no walkable doorway outside.</exception>
    public static ScriptedRoute WallBump(string name, LandblockCellSet cells, PhysicsWorld world, uint doorCellId)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(world);
        var (_, _, centre, outward) = Doorway(cells, new CellRoutePlanner(cells), doorCellId);
        var along = new Vector3(outward.Y, -outward.X, 0f); // the wall's direction, to the player's right facing out
        var start = OutdoorPoint(world, cells.Coord, centre + (outward * WallApproachMetres) - (along * WallStartAlongMetres));
        var goal = OutdoorPoint(world, cells.Coord, centre - (outward * WallApproachMetres) - (along * WallGoalAlongMetres));
        return new ScriptedRoute(name, [new RouteGoal(goal, 0.5f, Stop: true, DwellSeconds)]) { Start = start };
    }

    /// <summary>
    /// The point at landblock-local <paramref name="local"/> (x and y) as an outdoor position: its outdoor cell and the terrain's height there.
    /// </summary>
    /// <exception cref="InvalidOperationException">No outdoor cell with collision lies there.</exception>
    public static CellFrame OutdoorPoint(PhysicsWorld world, LandblockCoord coord, Vector3 local)
    {
        ArgumentNullException.ThrowIfNull(world);
        var cellId = (coord.LandBlockId & 0xFFFF0000u) | 1u;
        var origin = local;
        if (!LandDefs.AdjustToOutside(ref cellId, ref origin) || world.GetCell(cellId) is not PhysLandCell land)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"No attached outdoor cell at ({local.X:F1}, {local.Y:F1}) of landblock {coord}."));
        }

        var terrain = land.FindTerrainPoly(origin)
            ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"No terrain under ({origin.X:F1}, {origin.Y:F1}) in cell 0x{cellId:X8}."));
        var plane = terrain.Plane;
        origin.Z = -((plane.Normal.X * origin.X) + (plane.Normal.Y * origin.Y) + plane.D) / plane.Normal.Z;
        return new CellFrame(cellId, new AcFrame(origin, Quaternion.Identity));
    }

    /// <summary>The first outside portal of <paramref name="doorCellId"/> a human fits through: the cell, the portal, its centre (landblock space) and the horizontal direction out.</summary>
    private static (EnvCellData Cell, CellPortalData Portal, Vector3 Centre, Vector3 Outward) Doorway(LandblockCellSet cells, CellRoutePlanner planner, uint doorCellId)
    {
        var cell = cells.GetCell(doorCellId)
            ?? throw new InvalidOperationException($"Landblock {cells.Coord} has no cell 0x{doorCellId:X8}.");
        foreach (var portal in cell.Portals)
        {
            if (portal.LeadsOutside && planner.IsUpright(cell, portal) && planner.DoorwayOf(cell, portal) is { } doorway)
            {
                return (cell, portal, doorway.Centre, doorway.Outward);
            }
        }

        throw new InvalidOperationException($"Cell 0x{doorCellId:X8} has no upright doorway to the outside.");
    }
}
